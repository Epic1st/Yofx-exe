using YO4X.Admin.Application;
using YO4X.BuildingBlocks;
using YO4X.Commands;
using YO4X.Persistence.Postgres;
using YO4X.Policy;

namespace YO4X.Admin.Postgres;

public sealed partial class AdminPostgresApplication
{
    public async Task<CommandSummary?> CancelCommandAsync(
        AdminActor actor,
        Guid commandId,
        AdminRequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(commandId, nameof(commandId));
        ValidateMutationMetadata(metadata);
        await using AdminOperationContext context = await BeginAsync(
            actor,
            metadata.CorrelationId,
            options.MutationAuthenticationMaximumAge,
            cancellationToken).ConfigureAwait(false);
        context.Security.RequirePermission(AdminPermissions.CancelCommands);
        AdminCommandRecord? command = await AdminReadRepository.GetCommandAsync(
            context.Transaction,
            commandId,
            forUpdate: true,
            cancellationToken).ConfigureAwait(false);
        if (command is null)
        {
            await context.Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        AdminResourceScope scope = await ResolveCommandScopeAsync(
            context.Transaction,
            command,
            cancellationToken).ConfigureAwait(false);
        context.Security.RequireAccess(AdminPermissions.CancelCommands, scope);
        string requestSha256 = CanonicalJson.Sha256(new
        {
            Operation = "cancel_command",
            CommandId = commandId,
            metadata.ExpectedVersion,
            metadata.ReasonCode,
            WrittenReasonSha256 = CanonicalJson.Sha256(metadata.WrittenReason),
            metadata.TicketReference
        });
        AdminIdempotencyLease<CommandSummary> idempotency = await AdminIdempotency.AcquireAsync<CommandSummary>(
            context.Transaction,
            $"admin.command.cancel:{commandId:D}",
            metadata.IdempotencyKey,
            requestSha256,
            context.Now,
            options.IdempotencyLifetime,
            cancellationToken).ConfigureAwait(false);
        if (idempotency.IsReplay)
        {
            await context.Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return idempotency.Response
                ?? throw new InvalidOperationException("The cancellation replay response is absent.");
        }

        EnsureExpectedVersion(metadata.ExpectedVersion, command.RowVersion);
        EnsureCommandBinding(actor.TenantId, command);
        if (command.OriginalCommandId is not null)
        {
            throw new ResourceConflictException(
                "COMPENSATION_CANCELLATION_UNSUPPORTED",
                "A compensation command cannot be cancelled because the original lifecycle cannot be restored implicitly.");
        }

        await AdminMutationRepository.LockTargetsAsync(
            context.Transaction,
            command.Id,
            cancellationToken).ConfigureAwait(false);
        if (await AdminMutationRepository.HasDispatchedTargetAsync(
                context.Transaction,
                command.Id,
                cancellationToken).ConfigureAwait(false))
        {
            throw new ResourceConflictException(
                "COMMAND_ALREADY_DISPATCHED",
                "The command has dispatched a target and now requires an allowlisted compensation.");
        }

        if (command.Status is not (
            CommandStatus.Requested
            or CommandStatus.PolicyChecking
            or CommandStatus.WaitingApproval
            or CommandStatus.Approved
            or CommandStatus.Scheduled
            or CommandStatus.Dispatching))
        {
            throw new ResourceConflictException(
                "COMMAND_STATE_TRANSITION_INVALID",
                $"A command in state {command.Status} cannot be cancelled.");
        }

        await AdminMutationRepository.CancelCommandAsync(
            context.Transaction,
            command,
            context.Now,
            cancellationToken).ConfigureAwait(false);
        int invalidatedTargets = await AdminMutationRepository.InvalidatePendingTargetsAsync(
            context.Transaction,
            command.Id,
            context.Now,
            cancellationToken).ConfigureAwait(false);
        await AdminMutationRepository.CancelOpenApprovalAsync(
            context.Transaction,
            command.Id,
            cancellationToken).ConfigureAwait(false);
        CommandSummary response = command.WithLifecycle(
            CommandStatus.Cancelled,
            checked(command.RowVersion + 1),
            context.Now).ToSummary();
        var evidence = new
        {
            CommandId = command.Id,
            PreviousState = command.Status.ToString(),
            State = CommandStatus.Cancelled.ToString(),
            metadata.ReasonCode,
            WrittenReasonSha256 = CanonicalJson.Sha256(metadata.WrittenReason),
            metadata.TicketReference,
            ExpectedVersion = command.RowVersion,
            NewVersion = response.Version,
            InvalidatedTargets = invalidatedTargets
        };
        await AdminMutationRepository.InsertAuditIntentAsync(
            context.Transaction,
            command.Id,
            "command.cancelled",
            evidence,
            context.Now,
            cancellationToken).ConfigureAwait(false);
        await AdminEvidenceWriter.AppendCommandEventAsync(
            context.Transaction,
            command.Id,
            "admin.command.cancelled",
            "admin.command.cancelled.v1",
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

    public async Task<CommandAccepted> RequestCompensationAsync(
        AdminActor actor,
        Guid commandId,
        CompensationInput input,
        AdminRequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(commandId, nameof(commandId));
        ArgumentNullException.ThrowIfNull(input);
        if (!Enum.IsDefined(input.CompensationType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(input),
                "The compensation type is unknown.");
        }

        ValidateMutationMetadata(metadata);
        await using AdminOperationContext context = await BeginAsync(
            actor,
            metadata.CorrelationId,
            options.MutationAuthenticationMaximumAge,
            cancellationToken).ConfigureAwait(false);
        context.Security.RequirePermission(AdminPermissions.RequestCompensation);
        AdminCommandRecord original = await AdminReadRepository.GetCommandAsync(
            context.Transaction,
            commandId,
            forUpdate: true,
            cancellationToken).ConfigureAwait(false)
            ?? throw new AdminResourceNotFoundException();
        DeploymentResource deployment = await ResolveDeploymentCommandAsync(
            context.Transaction,
            original,
            forUpdate: true,
            cancellationToken).ConfigureAwait(false);
        context.Security.RequireAccess(AdminPermissions.RequestCompensation, deployment.ToScope());

        string requestSha256 = CanonicalJson.Sha256(new
        {
            Operation = "request_compensation",
            CommandId = commandId,
            CompensationType = input.CompensationType.ToString(),
            metadata.ExpectedVersion,
            input.ReasonCode,
            WrittenReasonSha256 = CanonicalJson.Sha256(input.WrittenReason),
            metadata.TicketReference
        });
        AdminIdempotencyLease<CommandAccepted> idempotency = await AdminIdempotency.AcquireAsync<CommandAccepted>(
            context.Transaction,
            $"admin.command.compensation:{commandId:D}",
            metadata.IdempotencyKey,
            requestSha256,
            context.Now,
            options.IdempotencyLifetime,
            cancellationToken).ConfigureAwait(false);
        if (idempotency.IsReplay)
        {
            await context.Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return idempotency.Response
                ?? throw new InvalidOperationException("The compensation replay response is absent.");
        }

        EnsureExpectedVersion(metadata.ExpectedVersion, original.RowVersion);
        EnsureCommandBinding(actor.TenantId, original);
        if (original.CompensationCommandId is not null
            || original.Status is not (
                CommandStatus.Propagating
                or CommandStatus.Reconciling
                or CommandStatus.Succeeded
                or CommandStatus.Partial
                or CommandStatus.Failed
                or CommandStatus.Unknown))
        {
            throw new ResourceConflictException(
                "COMMAND_COMPENSATION_STATE_INVALID",
                "The command is not in a state that permits a new compensation request.");
        }

        if (!await AdminMutationRepository.HasDispatchedTargetAsync(
                context.Transaction,
                original.Id,
                cancellationToken).ConfigureAwait(false))
        {
            throw new ResourceConflictException(
                "COMPENSATION_NOT_DISPATCHED",
                "Use cancellation before any target has been dispatched.");
        }

        if (!original.AllowedCompensationTypes.Contains(input.CompensationType))
        {
            throw new ResourceConflictException(
                "COMMAND_NON_COMPENSABLE",
                "The requested compensation type is not allowed by the immutable original command.");
        }

        ExecutionSafetyPolicyVector restriction = CompensationVector(input.CompensationType);
        CommandAccepted accepted = await CreatePendingCommandAsync(
            context,
            actor,
            input.CompensationType,
            deployment,
            restriction,
            metadata with
            {
                ReasonCode = input.ReasonCode.Trim(),
                WrittenReason = input.WrittenReason.Trim()
            },
            idempotency.Lease,
            original.Id,
            Array.Empty<CommandType>(),
            "admin.compensation.two_person.v1",
            cancellationToken).ConfigureAwait(false);
        await AdminMutationRepository.LinkCompensationAsync(
            context.Transaction,
            original,
            accepted.CommandId,
            context.Now,
            cancellationToken).ConfigureAwait(false);
        var linkEvidence = new
        {
            OriginalCommandId = original.Id,
            CompensationCommandId = accepted.CommandId,
            CompensationType = input.CompensationType.ToString(),
            PreviousState = original.Status.ToString(),
            State = CommandStatus.CompensationRequested.ToString(),
            ExpectedVersion = original.RowVersion,
            NewVersion = checked(original.RowVersion + 1)
        };
        await AdminMutationRepository.InsertAuditIntentAsync(
            context.Transaction,
            original.Id,
            $"command.compensation_requested.{accepted.CommandId:D}",
            linkEvidence,
            context.Now,
            cancellationToken).ConfigureAwait(false);
        await AdminEvidenceWriter.AppendCommandEventAsync(
            context.Transaction,
            original.Id,
            "admin.command.compensation_requested",
            "admin.command.compensation_requested.v1",
            input.ReasonCode,
            linkEvidence,
            context.Now,
            cancellationToken).ConfigureAwait(false);
        await AdminIdempotency.CompleteAsync(
            context.Transaction,
            idempotency.Lease,
            202,
            accepted,
            context.Now,
            cancellationToken).ConfigureAwait(false);
        await context.Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return accepted;
    }

    public async Task<CommandAccepted> RequestContainmentAsync(
        AdminActor actor,
        CommandType type,
        ScopeInput scope,
        ExecutionSafetyPolicyVector restrictions,
        AdminRequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(restrictions);
        ValidateMutationMetadata(metadata);
        Guid deploymentId = ParseDeploymentScope(scope);
        string permission = AdminPermissions.ForContainment(type);
        EnsureContainmentRestriction(type, restrictions);

        await using AdminOperationContext context = await BeginAsync(
            actor,
            metadata.CorrelationId,
            options.MutationAuthenticationMaximumAge,
            cancellationToken).ConfigureAwait(false);
        context.Security.RequirePermission(permission);
        DeploymentResource deployment = await AdminReadRepository.GetDeploymentAsync(
            context.Transaction,
            deploymentId,
            forUpdate: true,
            cancellationToken).ConfigureAwait(false)
            ?? throw new AdminResourceNotFoundException();
        context.Security.RequireAccess(permission, deployment.ToScope());

        string requestSha256 = CanonicalJson.Sha256(new
        {
            Operation = "request_containment",
            CommandType = type.ToString(),
            DeploymentId = deploymentId,
            RestrictionDigest = restrictions.ComputeDigest(),
            metadata.ExpectedVersion,
            metadata.ReasonCode,
            WrittenReasonSha256 = CanonicalJson.Sha256(metadata.WrittenReason),
            metadata.TicketReference
        });
        AdminIdempotencyLease<CommandAccepted> idempotency = await AdminIdempotency.AcquireAsync<CommandAccepted>(
            context.Transaction,
            $"admin.deployment.containment:{type.ToStorageValue()}:{deploymentId:D}",
            metadata.IdempotencyKey,
            requestSha256,
            context.Now,
            options.IdempotencyLifetime,
            cancellationToken).ConfigureAwait(false);
        if (idempotency.IsReplay)
        {
            await context.Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return idempotency.Response
                ?? throw new InvalidOperationException("The containment replay response is absent.");
        }

        EnsureExpectedVersion(metadata.ExpectedVersion, deployment.RowVersion);
        CommandAccepted accepted = await CreatePendingCommandAsync(
            context,
            actor,
            type,
            deployment,
            restrictions,
            metadata,
            idempotency.Lease,
            originalCommandId: null,
            AllowedCompensations(type),
            "admin.containment.two_person.v1",
            cancellationToken).ConfigureAwait(false);
        await AdminIdempotency.CompleteAsync(
            context.Transaction,
            idempotency.Lease,
            202,
            accepted,
            context.Now,
            cancellationToken).ConfigureAwait(false);
        await context.Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return accepted;
    }

    private async Task<CommandAccepted> CreatePendingCommandAsync(
        AdminOperationContext context,
        AdminActor actor,
        CommandType type,
        DeploymentResource deployment,
        ExecutionSafetyPolicyVector restriction,
        AdminRequestMetadata metadata,
        IdempotencyLease idempotencyLease,
        Guid? originalCommandId,
        IReadOnlyList<CommandType> allowedCompensations,
        string approvalPolicyKey,
        CancellationToken cancellationToken)
    {
        AdminPolicyEvaluation policy = await AdminPolicyRepository.EvaluateAsync(
            context.Transaction,
            deployment.ToScope(),
            restriction,
            accountConfirmedFlat: false,
            protectedReductionPathAvailable:
                deployment.BrokerHostedStopLoss && deployment.BrokerHostedTakeProfit,
            cancellationToken).ConfigureAwait(false);
        Guid commandId = Identifiers.NewId();
        Guid targetId = Identifiers.NewId();
        Guid previewId = Identifiers.NewId();
        Guid approvalId = Identifiers.NewId();
        var scope = new ScopeInput("deployment", deployment.Id.ToString("D"));
        ImpactTargetSnapshot[] targets =
        [
            new(
                targetId,
                deployment.Id,
                "deployment",
                deployment.RowVersion,
                "reconciled",
                Required: true,
                deployment.WorkerNodeId,
                deployment.WorkerGeneration ?? deployment.FenceGeneration)
        ];
        ImpactPreviewRecord preview = ImpactPreviewRecord.Create(
            previewId,
            actor.TenantId,
            actor.ActorId,
            scope,
            targets,
            policy.VersionWatermark,
            context.Now,
            context.Now.Add(options.ImpactPreviewLifetime));
        string restrictionJson = CanonicalJson.Serialize(restriction.ToDocument());
        string restrictionDigest = restriction.ComputeDigest();
        string payloadSha256 = CanonicalJson.Sha256(new
        {
            CommandType = type.ToString(),
            Scope = scope,
            RestrictionDigest = restrictionDigest,
            metadata.ReasonCode,
            WrittenReasonSha256 = CanonicalJson.Sha256(metadata.WrittenReason),
            metadata.TicketReference,
            metadata.ExpectedVersion,
            OriginalCommandId = originalCommandId,
            PreviewDigest = preview.Digest,
            PolicyVersion = policy.VersionWatermark
        });
        DateTimeOffset approvalExpiry = context.Now.Add(options.ApprovalLifetime);
        string[] allowedStorage = allowedCompensations
            .Select(compensation => compensation.ToStorageValue())
            .Order(StringComparer.Ordinal)
            .ToArray();
        var binding = new AdminCommandBinding(
            commandId,
            actor.TenantId,
            type.ToStorageValue(),
            payloadSha256,
            restrictionJson,
            allowedStorage,
            actor.ActorId,
            actor.SessionId,
            context.Security.Environment,
            "deployment",
            deployment.Id.ToString("D"),
            type is CommandType.RevokeLease or CommandType.ReplaceWorker ? "critical" : "high",
            metadata.ReasonCode.Trim(),
            metadata.WrittenReason.Trim(),
            NormalizeOptional(metadata.TicketReference),
            idempotencyLease.Id,
            metadata.ExpectedVersion,
            preview.Id,
            preview.Digest,
            originalCommandId,
            RequestedExecutionAt: null,
            ExpiresAt: approvalExpiry,
            metadata.CorrelationId);
        string commandDigest = binding.ComputeDigest();

        await AdminMutationRepository.InsertImpactPreviewAsync(
            context.Transaction,
            preview,
            cancellationToken).ConfigureAwait(false);
        await AdminMutationRepository.InsertCommandAsync(
            context.Transaction,
            binding,
            commandDigest,
            context.Now,
            cancellationToken).ConfigureAwait(false);
        await AdminPolicyRepository.InsertEvaluationAsync(
            context.Transaction,
            commandId,
            policy,
            context.Now,
            cancellationToken).ConfigureAwait(false);
        long policyCheckingVersion = await AdminMutationRepository.TransitionCommandAsync(
            context.Transaction,
            commandId,
            "requested",
            expectedVersion: 0,
            "policy_checking",
            context.Now,
            cancellationToken).ConfigureAwait(false);
        long waitingApprovalVersion = await AdminMutationRepository.TransitionCommandAsync(
            context.Transaction,
            commandId,
            "policy_checking",
            policyCheckingVersion,
            "waiting_approval",
            context.Now,
            cancellationToken).ConfigureAwait(false);
        await AdminMutationRepository.InsertApprovalAsync(
            context.Transaction,
            approvalId,
            commandId,
            actor.ActorId,
            approvalPolicyKey,
            preview,
            commandDigest,
            waitingApprovalVersion,
            restrictionDigest,
            requiredApprovals: 1,
            options.ApprovalAuthenticationMaximumAge,
            context.Now,
            approvalExpiry,
            cancellationToken).ConfigureAwait(false);

        var evidence = new
        {
            CommandId = commandId,
            CommandType = type.ToString(),
            Scope = scope,
            RestrictionDigest = restrictionDigest,
            EffectivePolicyDigest = policy.EffectiveDigest,
            PolicyVersion = policy.VersionWatermark,
            PreviewId = preview.Id,
            PreviewDigest = preview.Digest,
            TargetCount = preview.TargetCount,
            ApprovalRequestId = approvalId,
            ApprovalBindingDigest = ApprovalBindingDigest.Compute(
                commandDigest,
                preview.Digest,
                waitingApprovalVersion,
                restrictionDigest),
            OriginalCommandId = originalCommandId,
            metadata.ReasonCode,
            WrittenReasonSha256 = CanonicalJson.Sha256(metadata.WrittenReason),
            metadata.TicketReference,
            ExpectedResourceVersion = metadata.ExpectedVersion,
            WorkerPlanDisposition = policy.WorkerPlan.Disposition.ToString()
        };
        await AdminMutationRepository.InsertAuditIntentAsync(
            context.Transaction,
            commandId,
            originalCommandId is null
                ? "command.containment.requested"
                : "command.compensation.requested",
            evidence,
            context.Now,
            cancellationToken).ConfigureAwait(false);
        await AdminEvidenceWriter.AppendCommandEventAsync(
            context.Transaction,
            commandId,
            originalCommandId is null
                ? "admin.containment.requested"
                : "admin.compensation.requested",
            "admin.command.approval_requested.v1",
            metadata.ReasonCode,
            evidence,
            context.Now,
            cancellationToken).ConfigureAwait(false);
        return new CommandAccepted(
            commandId,
            new Uri($"/admin/v1/commands/{commandId:D}", UriKind.Relative),
            waitingApprovalVersion,
            metadata.CorrelationId,
            approvalId);
    }

    private static void ValidateMutationMetadata(AdminRequestMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadata.IdempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadata.ReasonCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadata.WrittenReason);
        if (metadata.CorrelationId == Guid.Empty)
        {
            throw new ArgumentException("A correlation identifier is required.", nameof(metadata));
        }

        if (metadata.ExpectedVersion is null or < 0)
        {
            throw new ResourceConflictException(
                "EXPECTED_VERSION_REQUIRED",
                "A non-negative expected resource version is required.");
        }
    }

    private static void EnsureExpectedVersion(long? submitted, long actual)
    {
        if (submitted != actual)
        {
            throw new ResourceConflictException(
                "RESOURCE_VERSION_CONFLICT",
                "The resource changed after the submitted expected version.");
        }
    }

    private static void EnsureCommandBinding(Guid tenantId, AdminCommandRecord command)
    {
        string reconstructed = AdminMutationRepository.ReconstructBinding(tenantId, command).ComputeDigest();
        if (!string.Equals(reconstructed, command.CommandDigest, StringComparison.Ordinal))
        {
            throw new ResourceConflictException(
                "COMMAND_BINDING_INVALID",
                "The immutable command binding no longer matches its stored digest.");
        }
    }

    private static Guid ParseDeploymentScope(ScopeInput scope)
    {
        if (!string.Equals(scope.Type?.Trim(), "deployment", StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(scope.Id, out Guid deploymentId)
            || deploymentId == Guid.Empty)
        {
            throw new ArgumentException(
                "Only one exact deployment scope is accepted by this containment use case.",
                nameof(scope));
        }

        return deploymentId;
    }

    private static async Task<DeploymentResource> ResolveDeploymentCommandAsync(
        TenantPostgresTransaction transaction,
        AdminCommandRecord command,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        if (command.ScopeType != "deployment"
            || !Guid.TryParse(command.ScopeId, out Guid deploymentId))
        {
            throw new ResourceConflictException(
                "COMPENSATION_SCOPE_UNSUPPORTED",
                "This adapter supports compensation only for an exact deployment-scoped command.");
        }

        DeploymentResource deployment = await AdminReadRepository.GetDeploymentAsync(
            transaction,
            deploymentId,
            forUpdate,
            cancellationToken).ConfigureAwait(false)
            ?? throw new AdminResourceNotFoundException();
        if (!string.Equals(deployment.Environment, command.Environment, StringComparison.Ordinal))
        {
            throw new ResourceConflictException(
                "COMMAND_SCOPE_CHANGED",
                "The command's immutable environment no longer matches its deployment scope.");
        }

        return deployment;
    }

    private static void EnsureContainmentRestriction(
        CommandType type,
        ExecutionSafetyPolicyVector submitted)
    {
        ExecutionSafetyPolicyVector minimum = MinimumVector(type);
        if (!submitted.IsAtLeastAsRestrictiveAs(minimum))
        {
            throw new ResourceConflictException(
                "CONTAINMENT_VECTOR_TOO_PERMISSIVE",
                "The submitted policy vector is weaker than the typed containment operation requires.");
        }
    }

    private static ExecutionSafetyPolicyVector MinimumVector(CommandType type) => type switch
    {
        CommandType.CloseOnly => new(
            true, false, false, true, true, true, true,
            LeaseMode.Normal,
            WorkerAction.None,
            CredentialMode.Normal,
            PackageEligibility.Eligible),
        CommandType.StopAfterFlat => new(
            true, false, false, true, true, true, true,
            LeaseMode.Normal,
            WorkerAction.StopAfterFlat,
            CredentialMode.Normal,
            PackageEligibility.Eligible),
        CommandType.RevokeLease => new(
            false, false, false, true, true, true, true,
            LeaseMode.Revoke,
            WorkerAction.Drain,
            CredentialMode.Normal,
            PackageEligibility.Eligible),
        CommandType.ReplaceWorker => new(
            false, false, false, true, true, true, true,
            LeaseMode.Revoke,
            WorkerAction.Drain | WorkerAction.Fence | WorkerAction.Replace,
            CredentialMode.DisableNewUse,
            PackageEligibility.Eligible),
        _ => throw new ArgumentOutOfRangeException(
            nameof(type),
            type,
            "The command is not an allowlisted deployment containment type.")
    };

    private static ExecutionSafetyPolicyVector CompensationVector(CommandType type) => type switch
    {
        CommandType.ReleaseContainment => ExecutionSafetyPolicyVector.Unrestricted,
        CommandType.ReplaceWorker => MinimumVector(CommandType.ReplaceWorker),
        _ => throw new ResourceConflictException(
            "COMPENSATION_TYPE_UNSUPPORTED",
            "The compensation type has no safe policy-vector implementation in this adapter.")
    };

    private static CommandType[] AllowedCompensations(CommandType type) => type switch
    {
        CommandType.CloseOnly => [CommandType.ReleaseContainment],
        CommandType.StopAfterFlat => [CommandType.ReleaseContainment],
        CommandType.RevokeLease => [CommandType.ReplaceWorker],
        CommandType.ReplaceWorker => Array.Empty<CommandType>(),
        _ => Array.Empty<CommandType>()
    };

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
