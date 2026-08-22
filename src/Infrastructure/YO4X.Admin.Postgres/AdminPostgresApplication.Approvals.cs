using YO4X.Admin.Application;
using YO4X.Approvals;
using YO4X.BuildingBlocks;
using YO4X.Commands;

namespace YO4X.Admin.Postgres;

public sealed partial class AdminPostgresApplication
{
    public async Task<CommandSummary?> DecideApprovalAsync(
        AdminActor actor,
        Guid approvalId,
        ApprovalDecisionType decision,
        ApprovalDecisionInput input,
        AdminRequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(approvalId, nameof(approvalId));
        ArgumentNullException.ThrowIfNull(input);
        if (!Enum.IsDefined(decision))
        {
            throw new ArgumentOutOfRangeException(nameof(decision), decision, "Unknown approval decision.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(input.Reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.BindingDigest);
        ValidateMutationMetadata(metadata);
        if (!string.Equals(
                input.Reason.Trim(),
                metadata.WrittenReason.Trim(),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The approval reason must exactly match the audited written reason.",
                nameof(input));
        }

        await using AdminOperationContext context = await BeginAsync(
            actor,
            metadata.CorrelationId,
            options.ApprovalAuthenticationMaximumAge,
            cancellationToken).ConfigureAwait(false);
        context.Security.RequirePermission(AdminPermissions.DecideApprovals);
        ApprovalRecord? locatedApproval = await AdminReadRepository.GetApprovalAsync(
            context.Transaction,
            approvalId,
            forUpdate: false,
            cancellationToken).ConfigureAwait(false);
        if (locatedApproval is null)
        {
            await context.Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        // All command workflows acquire locks in command -> approval -> deployment order.
        // The preliminary unlocked lookup resolves the immutable command identifier without
        // allowing an approval row to be held while waiting on its command.
        AdminCommandRecord lockedCommand = await AdminReadRepository.GetCommandAsync(
            context.Transaction,
            locatedApproval.CommandId,
            forUpdate: true,
            cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceConflictException(
                "APPROVAL_COMMAND_MISSING",
                "The approval's bound command is unavailable.");
        ApprovalRecord approval = await AdminReadRepository.GetApprovalAsync(
            context.Transaction,
            approvalId,
            forUpdate: true,
            cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceConflictException(
                "APPROVAL_INVALIDATED",
                "The approval request changed while it was being acquired.");
        if (approval.CommandId != lockedCommand.Id)
        {
            throw new ResourceConflictException(
                "APPROVAL_BINDING_INVALID",
                "The approval's command binding changed while it was being acquired.");
        }

        approval = approval with { Command = lockedCommand };
        DeploymentResource deployment = await ResolveDeploymentCommandAsync(
            context.Transaction,
            approval.Command,
            forUpdate: true,
            cancellationToken).ConfigureAwait(false);
        context.Security.RequireAccess(AdminPermissions.DecideApprovals, deployment.ToScope());
        if (approval.RequesterId == actor.ActorId)
        {
            throw new AdminAuthorizationDeniedException(
                "APPROVAL_SELF_DECISION_FORBIDDEN",
                "The requester cannot approve or reject their own command.");
        }

        if (approval.State != "pending")
        {
            throw new ResourceConflictException(
                "APPROVAL_NOT_PENDING",
                "The approval request is no longer pending.");
        }

        if (approval.ExpiresAt <= context.Now)
        {
            await ExpireApprovalAsync(context, approval, metadata, cancellationToken)
                .ConfigureAwait(false);
            await context.Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            throw new ResourceConflictException(
                "APPROVAL_EXPIRED",
                "The approval request and its bound command have expired.");
        }

        string requestSha256 = CanonicalJson.Sha256(new
        {
            Operation = "decide_approval",
            ApprovalId = approvalId,
            Decision = decision.ToString(),
            BindingDigest = input.BindingDigest.Trim().ToLowerInvariant(),
            metadata.ExpectedVersion,
            metadata.ReasonCode,
            WrittenReasonSha256 = CanonicalJson.Sha256(metadata.WrittenReason),
            metadata.TicketReference
        });
        AdminIdempotencyLease<CommandSummary> idempotency = await AdminIdempotency.AcquireAsync<CommandSummary>(
            context.Transaction,
            $"admin.approval.{decision.ToString().ToLowerInvariant()}:{approvalId:D}",
            metadata.IdempotencyKey,
            requestSha256,
            context.Now,
            options.IdempotencyLifetime,
            cancellationToken).ConfigureAwait(false);
        if (idempotency.IsReplay)
        {
            await context.Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return idempotency.Response
                ?? throw new InvalidOperationException("The approval replay response is absent.");
        }

        EnsureExpectedVersion(metadata.ExpectedVersion, approval.RowVersion);
        EnsureApprovalBindings(actor, approval, input.BindingDigest);
        EnsureApprovalSessionRequirement(context.Security, approval, context.Now);
        ImpactPreviewRecord preview = await LoadAndValidatePreviewAsync(
            context,
            approval,
            deployment,
            cancellationToken).ConfigureAwait(false);
        PolicyVectorDocument restrictionDocument = AdminStorageValues.ParsePolicyDocument(
            approval.Command.RestrictionVectorJson);
        var restriction = restrictionDocument.ToVector();
        AdminPolicyEvaluation policy = await AdminPolicyRepository.EvaluateAsync(
            context.Transaction,
            deployment.ToScope(),
            restriction,
            accountConfirmedFlat: false,
            protectedReductionPathAvailable:
                deployment.BrokerHostedStopLoss && deployment.BrokerHostedTakeProfit,
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(policy.VersionWatermark, preview.PolicyVersion, StringComparison.Ordinal))
        {
            throw new ResourceConflictException(
                "PREVIEW_STALE_REAPPROVAL_REQUIRED",
                "Applicable safety policies changed after the approved impact preview.");
        }

        await AdminPolicyRepository.InsertEvaluationAsync(
            context.Transaction,
            approval.CommandId,
            policy,
            context.Now,
            cancellationToken).ConfigureAwait(false);
        await AdminMutationRepository.InsertApprovalDecisionAsync(
            context.Transaction,
            approval,
            decision,
            input.Reason,
            context.Security.Session,
            context.Now,
            cancellationToken).ConfigureAwait(false);

        CommandSummary response;
        string approvalState;
        string eventSuffix;
        if (decision == ApprovalDecisionType.Reject)
        {
            await AdminMutationRepository.UpdateApprovalStateAsync(
                context.Transaction,
                approval.Id,
                approval.RowVersion,
                "pending",
                "rejected",
                cancellationToken).ConfigureAwait(false);
            long rejectedVersion = await AdminMutationRepository.TransitionCommandAsync(
                context.Transaction,
                approval.CommandId,
                "waiting_approval",
                approval.Command.RowVersion,
                "rejected",
                context.Now,
                cancellationToken).ConfigureAwait(false);
            response = approval.Command.WithLifecycle(
                CommandStatus.Rejected,
                rejectedVersion,
                context.Now).ToSummary();
            approvalState = "rejected";
            eventSuffix = "rejected";
        }
        else
        {
            int receivedApprovals = await AdminMutationRepository.CountApprovalsAsync(
                context.Transaction,
                approval.Id,
                cancellationToken).ConfigureAwait(false);
            bool quorumReached = receivedApprovals >= approval.RequiredApprovals;
            await AdminMutationRepository.UpdateApprovalStateAsync(
                context.Transaction,
                approval.Id,
                approval.RowVersion,
                "pending",
                quorumReached ? "approved" : "pending",
                cancellationToken).ConfigureAwait(false);
            if (quorumReached)
            {
                long approvedVersion = await AdminMutationRepository.TransitionCommandAsync(
                    context.Transaction,
                    approval.CommandId,
                    "waiting_approval",
                    approval.Command.RowVersion,
                    "approved",
                    context.Now,
                    cancellationToken).ConfigureAwait(false);
                long scheduledVersion = await AdminMutationRepository.TransitionCommandAsync(
                    context.Transaction,
                    approval.CommandId,
                    "approved",
                    approvedVersion,
                    "scheduled",
                    context.Now,
                    cancellationToken).ConfigureAwait(false);
                await AdminMutationRepository.InsertTargetsAsync(
                    context.Transaction,
                    approval.CommandId,
                    preview.ReadTargets(),
                    policy.EffectiveDigest,
                    context.Now,
                    cancellationToken).ConfigureAwait(false);
                response = approval.Command.WithLifecycle(
                    CommandStatus.Scheduled,
                    scheduledVersion,
                    context.Now).ToSummary();
                approvalState = "approved";
                eventSuffix = "approved";
            }
            else
            {
                response = approval.Command.ToSummary();
                approvalState = "pending";
                eventSuffix = "decision_recorded";
            }
        }

        var evidence = new
        {
            ApprovalRequestId = approval.Id,
            CommandId = approval.CommandId,
            Decision = decision.ToString(),
            ApprovalState = approvalState,
            CommandState = response.Status.ToString(),
            BindingDigest = ApprovalBindingDigest.Compute(
                approval.CommandDigest,
                approval.ImpactDigest,
                approval.CommandRowVersion,
                approval.RestrictionDigest),
            EffectivePolicyDigest = policy.EffectiveDigest,
            PolicyVersion = policy.VersionWatermark,
            metadata.ReasonCode,
            WrittenReasonSha256 = CanonicalJson.Sha256(metadata.WrittenReason),
            metadata.TicketReference,
            ApproverSessionId = actor.SessionId,
            AssuranceMethod = context.Security.Session.AssuranceMethod,
            context.Security.Session.ManagedDevice,
            context.Security.Session.AuthenticatedAt
        };
        await AdminMutationRepository.InsertAuditIntentAsync(
            context.Transaction,
            approval.CommandId,
            $"approval.{eventSuffix}.{approval.Id:D}.{actor.ActorId:D}",
            evidence,
            context.Now,
            cancellationToken).ConfigureAwait(false);
        await AdminEvidenceWriter.AppendCommandEventAsync(
            context.Transaction,
            approval.CommandId,
            $"admin.approval.{eventSuffix}",
            $"admin.approval.{eventSuffix}.v1",
            metadata.ReasonCode,
            evidence,
            context.Now,
            cancellationToken).ConfigureAwait(false);
        await AdminIdempotency.CompleteAsync(
            context.Transaction,
            idempotency.Lease,
            200,
            response,
            context.Now,
            cancellationToken).ConfigureAwait(false);
        await context.Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return response;
    }

    private static void EnsureApprovalBindings(
        AdminActor actor,
        ApprovalRecord approval,
        string submittedBindingDigest)
    {
        AdminCommandRecord command = approval.Command;
        EnsureCommandBinding(actor.TenantId, command);
        if (command.Status != CommandStatus.WaitingApproval
            || command.RowVersion != approval.CommandRowVersion
            || command.ImpactPreviewId != approval.ImpactPreviewId
            || !string.Equals(command.CommandDigest, approval.CommandDigest, StringComparison.Ordinal)
            || !string.Equals(command.ImpactDigest, approval.ImpactDigest, StringComparison.Ordinal))
        {
            throw new ResourceConflictException(
                "APPROVAL_BINDING_INVALID",
                "The approval no longer binds the current immutable command and preview.");
        }

        string restrictionDigest = AdminStorageValues.ParsePolicyDocument(
            command.RestrictionVectorJson).ToVector().ComputeDigest();
        if (!string.Equals(restrictionDigest, approval.RestrictionDigest, StringComparison.Ordinal))
        {
            throw new ResourceConflictException(
                "APPROVAL_RESTRICTION_DIGEST_MISMATCH",
                "The approval restriction digest does not match the command restriction vector.");
        }

        string expectedBindingDigest = ApprovalBindingDigest.Compute(
            approval.CommandDigest,
            approval.ImpactDigest,
            approval.CommandRowVersion,
            approval.RestrictionDigest);
        string expectedBindingSnapshot = ApprovalBindingDigest.SerializeSnapshot(
            approval.CommandDigest,
            approval.ImpactDigest,
            approval.CommandRowVersion,
            approval.RestrictionDigest);
        if (!string.Equals(
                AdminStorageValues.CanonicalizeJson(approval.BindingSnapshotJson),
                expectedBindingSnapshot,
                StringComparison.Ordinal)
            || !string.Equals(
                approval.BindingDigest,
                expectedBindingDigest,
                StringComparison.Ordinal)
            || !string.Equals(
                expectedBindingDigest,
                submittedBindingDigest.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ResourceConflictException(
                "APPROVAL_BINDING_DIGEST_MISMATCH",
                "The submitted approval binding digest is not the current exact binding.");
        }
    }

    private static void EnsureApprovalSessionRequirement(
        AdminSecuritySnapshot security,
        ApprovalRecord approval,
        DateTimeOffset now)
    {
        if (!string.Equals(
                approval.MinimumAssurance,
                "phishing_resistant",
                StringComparison.Ordinal)
            || !approval.ManagedDeviceRequired
            || approval.MaximumSessionAgeSeconds <= 0)
        {
            throw new ResourceConflictException(
                "APPROVAL_ASSURANCE_REQUIREMENT_INVALID",
                "The persisted approval assurance requirement is not valid for an admin command.");
        }

        if (!security.Session.ManagedDevice
            || security.Session.AuthenticatedAt > now
            || now - security.Session.AuthenticatedAt
                > TimeSpan.FromSeconds(approval.MaximumSessionAgeSeconds))
        {
            throw new AdminAuthorizationDeniedException(
                "APPROVAL_STEP_UP_REQUIRED",
                "The current admin session does not satisfy the approval's bound assurance requirement.");
        }
    }

    private static async Task<ImpactPreviewRecord> LoadAndValidatePreviewAsync(
        AdminOperationContext context,
        ApprovalRecord approval,
        DeploymentResource deployment,
        CancellationToken cancellationToken)
    {
        ImpactPreviewRecord preview = await AdminReadRepository.GetImpactPreviewAsync(
            context.Transaction,
            approval.ImpactPreviewId,
            cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceConflictException(
                "APPROVAL_PREVIEW_MISSING",
                "The exact approval impact preview is unavailable.");
        if (!preview.HasValidDigest()
            || !string.Equals(preview.Digest, approval.ImpactDigest, StringComparison.Ordinal)
            || preview.ExpiresAt <= context.Now
            || approval.ExpiresAt > preview.ExpiresAt)
        {
            throw new ResourceConflictException(
                "PREVIEW_STALE_REAPPROVAL_REQUIRED",
                "The approval impact preview is expired or has invalid binding evidence.");
        }

        IReadOnlyList<ImpactTargetSnapshot> storedTargets = preview.ReadTargets();
        if (storedTargets.Count != 1)
        {
            throw new ResourceConflictException(
                "PREVIEW_TARGET_SNAPSHOT_INVALID",
                "The exact deployment command must bind one frozen target.");
        }

        ImpactTargetSnapshot stored = storedTargets[0];
        var current = new ImpactTargetSnapshot(
            stored.TargetId,
            deployment.Id,
            "deployment",
            deployment.RowVersion,
            "reconciled",
            Required: true,
            deployment.WorkerNodeId,
            deployment.WorkerGeneration ?? deployment.FenceGeneration);
        string currentTargetsJson = CanonicalJson.Serialize(new[] { current });
        string currentWatermark = CanonicalJson.Sha256(new[]
        {
            new
            {
                current.ResourceId,
                current.ResourceVersion,
                current.WorkerId,
                current.Generation
            }
        });
        if (!string.Equals(
                AdminStorageValues.CanonicalizeJson(preview.TargetSnapshotJson),
                currentTargetsJson,
                StringComparison.Ordinal)
            || !string.Equals(
                preview.ResourceVersionWatermark,
                currentWatermark,
                StringComparison.Ordinal))
        {
            throw new ResourceConflictException(
                "PREVIEW_STALE_REAPPROVAL_REQUIRED",
                "The resolved deployment target or resource version changed after preview.");
        }

        return preview;
    }

    private static async Task ExpireApprovalAsync(
        AdminOperationContext context,
        ApprovalRecord approval,
        AdminRequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        await AdminMutationRepository.ExpireApprovalAndCommandAsync(
            context.Transaction,
            approval,
            context.Now,
            cancellationToken).ConfigureAwait(false);
        var evidence = new
        {
            ApprovalRequestId = approval.Id,
            CommandId = approval.CommandId,
            ApprovalState = "expired",
            CommandState = "expired",
            approval.ExpiresAt,
            metadata.ReasonCode
        };
        await AdminMutationRepository.InsertAuditIntentAsync(
            context.Transaction,
            approval.CommandId,
            $"approval.expired.{approval.Id:D}",
            evidence,
            context.Now,
            cancellationToken).ConfigureAwait(false);
        await AdminEvidenceWriter.AppendCommandEventAsync(
            context.Transaction,
            approval.CommandId,
            "admin.approval.expired",
            "admin.approval.expired.v1",
            metadata.ReasonCode,
            evidence,
            context.Now,
            cancellationToken).ConfigureAwait(false);
    }
}
