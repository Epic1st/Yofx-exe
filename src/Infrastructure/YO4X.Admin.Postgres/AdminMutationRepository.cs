using Npgsql;
using NpgsqlTypes;
using YO4X.Admin.Application;
using YO4X.Approvals;
using YO4X.BuildingBlocks;
using YO4X.Commands;
using YO4X.Persistence.Postgres;

namespace YO4X.Admin.Postgres;

internal static class AdminMutationRepository
{
    public static async Task InsertImpactPreviewAsync(
        TenantPostgresTransaction transaction,
        ImpactPreviewRecord preview,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into control.impact_previews
            (
                id,
                tenant_id,
                actor_id,
                scope_expression,
                target_snapshot,
                target_count,
                resource_version_watermark,
                policy_version,
                digest,
                created_at,
                expires_at
            )
            values
            (
                @id,
                @tenant_id,
                @actor_id,
                @scope_expression,
                @target_snapshot,
                @target_count,
                @resource_version_watermark,
                @policy_version,
                @digest,
                @created_at,
                @expires_at
            )
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, preview.Id);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, preview.TenantId);
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Uuid, preview.ActorId);
        command.Parameters.AddWithValue("scope_expression", NpgsqlDbType.Jsonb, preview.ScopeExpressionJson);
        command.Parameters.AddWithValue("target_snapshot", NpgsqlDbType.Jsonb, preview.TargetSnapshotJson);
        command.Parameters.AddWithValue("target_count", NpgsqlDbType.Integer, preview.TargetCount);
        command.Parameters.AddWithValue("resource_version_watermark", NpgsqlDbType.Text, preview.ResourceVersionWatermark);
        command.Parameters.AddWithValue("policy_version", NpgsqlDbType.Text, preview.PolicyVersion);
        command.Parameters.AddWithValue("digest", NpgsqlDbType.Text, preview.Digest);
        command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, preview.CreatedAt);
        command.Parameters.AddWithValue("expires_at", NpgsqlDbType.TimestampTz, preview.ExpiresAt);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task InsertCommandAsync(
        TenantPostgresTransaction transaction,
        AdminCommandBinding binding,
        string commandDigest,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into control.admin_commands
            (
                id,
                tenant_id,
                command_type,
                payload_sha256,
                command_digest,
                restriction_vector,
                allowed_compensation_types,
                actor_id,
                session_id,
                environment,
                scope_type,
                scope_id,
                risk_level,
                reason_code,
                written_reason,
                ticket_reference,
                idempotency_record_id,
                expected_resource_version,
                impact_preview_id,
                state,
                original_command_id,
                requested_execution_at,
                expires_at,
                correlation_id,
                row_version,
                created_at,
                updated_at
            )
            values
            (
                @id,
                @tenant_id,
                @command_type,
                @payload_sha256,
                @command_digest,
                @restriction_vector,
                @allowed_compensation_types,
                @actor_id,
                @session_id,
                @environment,
                @scope_type,
                @scope_id,
                @risk_level,
                @reason_code,
                @written_reason,
                @ticket_reference,
                @idempotency_record_id,
                @expected_resource_version,
                @impact_preview_id,
                'requested',
                @original_command_id,
                @requested_execution_at,
                @expires_at,
                @correlation_id,
                0,
                @now,
                @now
            )
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, binding.Id);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, binding.TenantId);
        command.Parameters.AddWithValue("command_type", NpgsqlDbType.Text, binding.CommandType);
        command.Parameters.AddWithValue("payload_sha256", NpgsqlDbType.Text, binding.PayloadSha256);
        command.Parameters.AddWithValue("command_digest", NpgsqlDbType.Text, commandDigest);
        command.Parameters.AddWithValue("restriction_vector", NpgsqlDbType.Jsonb, binding.RestrictionVectorJson);
        command.Parameters.AddWithValue(
            "allowed_compensation_types",
            NpgsqlDbType.Array | NpgsqlDbType.Text,
            binding.AllowedCompensationTypes.ToArray());
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Uuid, binding.ActorId);
        command.Parameters.AddWithValue("session_id", NpgsqlDbType.Uuid, binding.SessionId);
        command.Parameters.AddWithValue("environment", NpgsqlDbType.Text, binding.Environment);
        command.Parameters.AddWithValue("scope_type", NpgsqlDbType.Text, binding.ScopeType);
        AddNullableText(command, "scope_id", binding.ScopeId);
        command.Parameters.AddWithValue("risk_level", NpgsqlDbType.Text, binding.RiskLevel);
        command.Parameters.AddWithValue("reason_code", NpgsqlDbType.Text, binding.ReasonCode);
        command.Parameters.AddWithValue("written_reason", NpgsqlDbType.Text, binding.WrittenReason);
        AddNullableText(command, "ticket_reference", binding.TicketReference);
        command.Parameters.AddWithValue("idempotency_record_id", NpgsqlDbType.Uuid, binding.IdempotencyRecordId);
        AddNullableInt64(command, "expected_resource_version", binding.ExpectedResourceVersion);
        AddNullableUuid(command, "impact_preview_id", binding.ImpactPreviewId);
        AddNullableUuid(command, "original_command_id", binding.OriginalCommandId);
        AddNullableTimestamp(command, "requested_execution_at", binding.RequestedExecutionAt);
        AddNullableTimestamp(command, "expires_at", binding.ExpiresAt);
        command.Parameters.AddWithValue("correlation_id", NpgsqlDbType.Uuid, binding.CorrelationId);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<long> TransitionCommandAsync(
        TenantPostgresTransaction transaction,
        Guid commandId,
        string expectedState,
        long expectedVersion,
        string nextState,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            update control.admin_commands
               set state = @next_state,
                   row_version = row_version + 1,
                   updated_at = @now
             where tenant_id = @tenant_id
               and id = @command_id
               and state = @expected_state
               and row_version = @expected_version
            returning row_version
            """);
        command.Parameters.AddWithValue("next_state", NpgsqlDbType.Text, nextState);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        command.Parameters.AddWithValue("command_id", NpgsqlDbType.Uuid, commandId);
        command.Parameters.AddWithValue("expected_state", NpgsqlDbType.Text, expectedState);
        command.Parameters.AddWithValue("expected_version", NpgsqlDbType.Bigint, expectedVersion);
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is long version
            ? version
            : throw VersionConflict();
    }

    public static async Task InsertApprovalAsync(
        TenantPostgresTransaction transaction,
        Guid approvalId,
        Guid commandId,
        Guid requesterId,
        string policyKey,
        ImpactPreviewRecord preview,
        string commandDigest,
        long commandRowVersion,
        string restrictionDigest,
        int requiredApprovals,
        TimeSpan maximumSessionAge,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        string bindingSnapshotJson = ApprovalBindingDigest.SerializeSnapshot(
            commandDigest,
            preview.Digest,
            commandRowVersion,
            restrictionDigest);
        string bindingDigest = ApprovalBindingDigest.Compute(
            commandDigest,
            preview.Digest,
            commandRowVersion,
            restrictionDigest);
        int maximumSessionAgeSeconds = checked((int)Math.Ceiling(maximumSessionAge.TotalSeconds));

        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into control.approval_requests
            (
                id,
                tenant_id,
                command_id,
                requester_id,
                policy_key,
                impact_preview_id,
                command_digest,
                impact_digest,
                command_row_version,
                restriction_digest,
                binding_snapshot,
                binding_digest,
                required_approvals,
                minimum_assurance,
                managed_device_required,
                maximum_session_age_seconds,
                state,
                expires_at,
                row_version,
                created_at
            )
            values
            (
                @id,
                @tenant_id,
                @command_id,
                @requester_id,
                @policy_key,
                @impact_preview_id,
                @command_digest,
                @impact_digest,
                @command_row_version,
                @restriction_digest,
                @binding_snapshot,
                @binding_digest,
                @required_approvals,
                'phishing_resistant',
                true,
                @maximum_session_age_seconds,
                'pending',
                @expires_at,
                0,
                @created_at
            )
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, approvalId);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        command.Parameters.AddWithValue("command_id", NpgsqlDbType.Uuid, commandId);
        command.Parameters.AddWithValue("requester_id", NpgsqlDbType.Uuid, requesterId);
        command.Parameters.AddWithValue("policy_key", NpgsqlDbType.Text, policyKey);
        command.Parameters.AddWithValue("impact_preview_id", NpgsqlDbType.Uuid, preview.Id);
        command.Parameters.AddWithValue("command_digest", NpgsqlDbType.Text, commandDigest);
        command.Parameters.AddWithValue("impact_digest", NpgsqlDbType.Text, preview.Digest);
        command.Parameters.AddWithValue("command_row_version", NpgsqlDbType.Bigint, commandRowVersion);
        command.Parameters.AddWithValue("restriction_digest", NpgsqlDbType.Text, restrictionDigest);
        command.Parameters.AddWithValue("binding_snapshot", NpgsqlDbType.Jsonb, bindingSnapshotJson);
        command.Parameters.AddWithValue("binding_digest", NpgsqlDbType.Text, bindingDigest);
        command.Parameters.AddWithValue("required_approvals", NpgsqlDbType.Smallint, checked((short)requiredApprovals));
        command.Parameters.AddWithValue(
            "maximum_session_age_seconds",
            NpgsqlDbType.Integer,
            maximumSessionAgeSeconds);
        command.Parameters.AddWithValue("expires_at", NpgsqlDbType.TimestampTz, expiresAt);
        command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, createdAt);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task InsertApprovalDecisionAsync(
        TenantPostgresTransaction transaction,
        ApprovalRecord approval,
        ApprovalDecisionType decision,
        string reason,
        AdminSessionEvidence session,
        DateTimeOffset decidedAt,
        CancellationToken cancellationToken)
    {
        string decisionValue = decision switch
        {
            ApprovalDecisionType.Approve => "approve",
            ApprovalDecisionType.Reject => "reject",
            _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "Unknown approval decision.")
        };
        string evidenceSha256 = CanonicalJson.Sha256(new
        {
            ApprovalRequestId = approval.Id,
            ApproverId = transaction.Context.ActorId,
            AdminSessionId = transaction.Context.SessionId,
            Decision = decisionValue,
            ReasonSha256 = CanonicalJson.Sha256(reason.Trim()),
            approval.CommandDigest,
            approval.ImpactDigest,
            approval.BindingDigest,
            AssuranceLevel = "phishing_resistant",
            session.AssuranceMethod,
            session.ManagedDevice,
            session.AuthenticatedAt,
            DecidedAt = decidedAt
        });
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into control.approval_decisions
            (
                id,
                tenant_id,
                approval_request_id,
                approver_id,
                admin_session_id,
                decision,
                reason,
                command_digest,
                impact_digest,
                binding_digest,
                assurance_level,
                assurance_method,
                managed_device,
                authenticated_at,
                evidence_sha256,
                decided_at
            )
            values
            (
                @id,
                @tenant_id,
                @approval_request_id,
                @approver_id,
                @admin_session_id,
                @decision,
                @reason,
                @command_digest,
                @impact_digest,
                @binding_digest,
                'phishing_resistant',
                @assurance_method,
                @managed_device,
                @authenticated_at,
                @evidence_sha256,
                @decided_at
            )
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, Identifiers.NewId());
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        command.Parameters.AddWithValue("approval_request_id", NpgsqlDbType.Uuid, approval.Id);
        command.Parameters.AddWithValue("approver_id", NpgsqlDbType.Uuid, transaction.Context.ActorId);
        command.Parameters.AddWithValue("admin_session_id", NpgsqlDbType.Uuid, transaction.Context.SessionId!.Value);
        command.Parameters.AddWithValue("decision", NpgsqlDbType.Text, decisionValue);
        command.Parameters.AddWithValue("reason", NpgsqlDbType.Text, reason.Trim());
        command.Parameters.AddWithValue("command_digest", NpgsqlDbType.Text, approval.CommandDigest);
        command.Parameters.AddWithValue("impact_digest", NpgsqlDbType.Text, approval.ImpactDigest);
        command.Parameters.AddWithValue("binding_digest", NpgsqlDbType.Text, approval.BindingDigest);
        command.Parameters.AddWithValue("assurance_method", NpgsqlDbType.Text, session.AssuranceMethod);
        command.Parameters.AddWithValue("managed_device", NpgsqlDbType.Boolean, session.ManagedDevice);
        command.Parameters.AddWithValue("authenticated_at", NpgsqlDbType.TimestampTz, session.AuthenticatedAt);
        command.Parameters.AddWithValue("evidence_sha256", NpgsqlDbType.Text, evidenceSha256);
        command.Parameters.AddWithValue("decided_at", NpgsqlDbType.TimestampTz, decidedAt);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ResourceConflictException(
                "APPROVER_ALREADY_DECIDED",
                "This actor already recorded a decision for the approval request.");
        }
    }

    public static async Task<int> CountApprovalsAsync(
        TenantPostgresTransaction transaction,
        Guid approvalId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select count(*)::integer
            from control.approval_decisions
            where tenant_id = @tenant_id
              and approval_request_id = @approval_id
              and decision = 'approve'
            """);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        command.Parameters.AddWithValue("approval_id", NpgsqlDbType.Uuid, approvalId);
        return (int)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Approval count query returned no result."));
    }

    public static async Task UpdateApprovalStateAsync(
        TenantPostgresTransaction transaction,
        Guid approvalId,
        long expectedVersion,
        string expectedState,
        string nextState,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            update control.approval_requests
               set state = @next_state,
                   row_version = row_version + 1
             where tenant_id = @tenant_id
               and id = @approval_id
               and row_version = @expected_version
               and state = @expected_state
            """);
        command.Parameters.AddWithValue("next_state", NpgsqlDbType.Text, nextState);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        command.Parameters.AddWithValue("approval_id", NpgsqlDbType.Uuid, approvalId);
        command.Parameters.AddWithValue("expected_version", NpgsqlDbType.Bigint, expectedVersion);
        command.Parameters.AddWithValue("expected_state", NpgsqlDbType.Text, expectedState);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw VersionConflict();
        }
    }

    public static async Task ExpireApprovalAndCommandAsync(
        TenantPostgresTransaction transaction,
        ApprovalRecord approval,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand approvalCommand = transaction.CreateCommand(
            """
            update control.approval_requests
               set state = 'expired',
                   invalidation_code = 'APPROVAL_EXPIRED',
                   row_version = row_version + 1
             where tenant_id = @tenant_id
               and id = @approval_id
               and row_version = @approval_version
               and state = 'pending'
            """);
        approvalCommand.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        approvalCommand.Parameters.AddWithValue("approval_id", NpgsqlDbType.Uuid, approval.Id);
        approvalCommand.Parameters.AddWithValue("approval_version", NpgsqlDbType.Bigint, approval.RowVersion);
        if (await approvalCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw VersionConflict();
        }

        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            update control.admin_commands
               set state = 'expired',
                   row_version = row_version + 1,
                   updated_at = @now
             where tenant_id = @tenant_id
               and id = @command_id
               and row_version = @command_version
               and state = 'waiting_approval'
            """);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        command.Parameters.AddWithValue("command_id", NpgsqlDbType.Uuid, approval.CommandId);
        command.Parameters.AddWithValue("command_version", NpgsqlDbType.Bigint, approval.Command.RowVersion);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw VersionConflict();
        }
    }

    public static async Task CancelOpenApprovalAsync(
        TenantPostgresTransaction transaction,
        Guid commandId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            update control.approval_requests
               set state = 'invalidated',
                   invalidation_code = 'COMMAND_CANCELLED',
                   row_version = row_version + 1
             where tenant_id = @tenant_id
               and command_id = @command_id
               and state = 'pending'
            """);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        command.Parameters.AddWithValue("command_id", NpgsqlDbType.Uuid, commandId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<bool> HasDispatchedTargetAsync(
        TenantPostgresTransaction transaction,
        Guid commandId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select exists
            (
                select 1
                from control.command_targets
                where tenant_id = @tenant_id
                  and command_id = @command_id
                  and (dispatched_at is not null or state <> 'pending_dispatch')
            )
            """);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        command.Parameters.AddWithValue("command_id", NpgsqlDbType.Uuid, commandId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Target dispatch query returned no result."));
    }

    public static async Task LockTargetsAsync(
        TenantPostgresTransaction transaction,
        Guid commandId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select id
            from control.command_targets
            where tenant_id = @tenant_id
              and command_id = @command_id
            order by id
            for update
            """);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        command.Parameters.AddWithValue("command_id", NpgsqlDbType.Uuid, commandId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // Reading every row acquires and retains the ordered row locks until commit.
        }
    }

    public static async Task CancelCommandAsync(
        TenantPostgresTransaction transaction,
        AdminCommandRecord commandRecord,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            update control.admin_commands
               set state = 'cancelled',
                   row_version = row_version + 1,
                   updated_at = @now
             where tenant_id = @tenant_id
               and id = @command_id
               and row_version = @expected_version
               and state in
               (
                   'requested',
                   'policy_checking',
                   'waiting_approval',
                   'approved',
                   'scheduled',
                   'dispatching'
               )
               and original_command_id is null
               and not exists
               (
                   select 1
                   from control.command_targets as target
                   where target.tenant_id = control.admin_commands.tenant_id
                     and target.command_id = control.admin_commands.id
                     and (target.dispatched_at is not null or target.state <> 'pending_dispatch')
               )
            """);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        command.Parameters.AddWithValue("command_id", NpgsqlDbType.Uuid, commandRecord.Id);
        command.Parameters.AddWithValue("expected_version", NpgsqlDbType.Bigint, commandRecord.RowVersion);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw VersionConflict();
        }
    }

    public static async Task<int> InvalidatePendingTargetsAsync(
        TenantPostgresTransaction transaction,
        Guid commandId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            update control.command_targets
               set state = 'not_applicable',
                   observed_result = 'cancelled_before_dispatch',
                   row_version = row_version + 1,
                   updated_at = @now
             where tenant_id = @tenant_id
               and command_id = @command_id
               and state = 'pending_dispatch'
               and dispatched_at is null
            """);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        command.Parameters.AddWithValue("command_id", NpgsqlDbType.Uuid, commandId);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task LinkCompensationAsync(
        TenantPostgresTransaction transaction,
        AdminCommandRecord original,
        Guid compensationCommandId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            update control.admin_commands
               set state = 'compensation_requested',
                   compensation_command_id = @compensation_command_id,
                   row_version = row_version + 1,
                   updated_at = @now
             where tenant_id = @tenant_id
               and id = @command_id
               and row_version = @expected_version
               and compensation_command_id is null
               and state in ('propagating', 'reconciling', 'succeeded', 'partial', 'failed', 'unknown')
               and exists
               (
                   select 1
                   from control.command_targets as target
                   where target.tenant_id = control.admin_commands.tenant_id
                     and target.command_id = control.admin_commands.id
                     and (target.dispatched_at is not null or target.state <> 'pending_dispatch')
               )
            """);
        command.Parameters.AddWithValue("compensation_command_id", NpgsqlDbType.Uuid, compensationCommandId);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        command.Parameters.AddWithValue("command_id", NpgsqlDbType.Uuid, original.Id);
        command.Parameters.AddWithValue("expected_version", NpgsqlDbType.Bigint, original.RowVersion);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw VersionConflict();
        }
    }

    public static async Task InsertTargetsAsync(
        TenantPostgresTransaction transaction,
        Guid commandId,
        IReadOnlyList<ImpactTargetSnapshot> targets,
        string effectivePolicyDigest,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (ImpactTargetSnapshot target in targets)
        {
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                insert into control.command_targets
                (
                    id,
                    tenant_id,
                    command_id,
                    resource_id,
                    resource_type,
                    resource_version,
                    required_proof,
                    required,
                    worker_id,
                    generation,
                    effective_policy_digest,
                    state,
                    attempts,
                    row_version,
                    created_at,
                    updated_at
                )
                values
                (
                    @id,
                    @tenant_id,
                    @command_id,
                    @resource_id,
                    @resource_type,
                    @resource_version,
                    @required_proof,
                    @required,
                    @worker_id,
                    @generation,
                    @effective_policy_digest,
                    'pending_dispatch',
                    0,
                    0,
                    @now,
                    @now
                )
                """);
            command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, target.TargetId);
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
            command.Parameters.AddWithValue("command_id", NpgsqlDbType.Uuid, commandId);
            command.Parameters.AddWithValue("resource_id", NpgsqlDbType.Uuid, target.ResourceId);
            command.Parameters.AddWithValue("resource_type", NpgsqlDbType.Text, target.ResourceType);
            command.Parameters.AddWithValue("resource_version", NpgsqlDbType.Bigint, target.ResourceVersion);
            command.Parameters.AddWithValue("required_proof", NpgsqlDbType.Text, target.RequiredProof);
            command.Parameters.AddWithValue("required", NpgsqlDbType.Boolean, target.Required);
            AddNullableUuid(command, "worker_id", target.WorkerId);
            AddNullableInt64(command, "generation", target.Generation);
            command.Parameters.AddWithValue("effective_policy_digest", NpgsqlDbType.Text, effectivePolicyDigest);
            command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task InsertAuditIntentAsync(
        TenantPostgresTransaction transaction,
        Guid commandId,
        string eventType,
        object redactedPayload,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into control.command_audit_intents
            (
                id,
                tenant_id,
                command_id,
                actor_id,
                event_type,
                redacted_payload_sha256,
                correlation_id,
                created_at
            )
            values
            (
                @id,
                @tenant_id,
                @command_id,
                @actor_id,
                @event_type,
                @redacted_payload_sha256,
                @correlation_id,
                @created_at
            )
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, Identifiers.NewId());
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        command.Parameters.AddWithValue("command_id", NpgsqlDbType.Uuid, commandId);
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Uuid, transaction.Context.ActorId);
        command.Parameters.AddWithValue("event_type", NpgsqlDbType.Text, eventType);
        command.Parameters.AddWithValue("redacted_payload_sha256", NpgsqlDbType.Text, CanonicalJson.Sha256(redactedPayload));
        command.Parameters.AddWithValue("correlation_id", NpgsqlDbType.Uuid, transaction.Context.CorrelationId);
        command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public static AdminCommandBinding ReconstructBinding(Guid tenantId, AdminCommandRecord command) => new(
        command.Id,
        tenantId,
        command.Type.ToStorageValue(),
        command.PayloadSha256,
        AdminStorageValues.CanonicalizeJson(command.RestrictionVectorJson),
        command.AllowedCompensationTypes.Select(type => type.ToStorageValue()).ToArray(),
        command.ActorId,
        command.SessionId,
        command.Environment,
        command.ScopeType,
        command.ScopeId,
        command.RiskLevel,
        command.ReasonCode,
        command.WrittenReason,
        command.TicketReference,
        command.IdempotencyRecordId,
        command.ExpectedResourceVersion,
        command.ImpactPreviewId,
        command.ImpactDigest,
        command.OriginalCommandId,
        command.RequestedExecutionAt,
        command.ExpiresAt,
        command.CorrelationId);

    private static ResourceConflictException VersionConflict() => new(
        "RESOURCE_VERSION_CONFLICT",
        "The resource changed after the submitted expected version.");

    private static void AddNullableText(NpgsqlCommand command, string name, string? value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Text, value is null ? DBNull.Value : value);

    private static void AddNullableUuid(NpgsqlCommand command, string name, Guid? value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Uuid, value is null ? DBNull.Value : value.Value);

    private static void AddNullableInt64(NpgsqlCommand command, string name, long? value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Bigint, value is null ? DBNull.Value : value.Value);

    private static void AddNullableTimestamp(NpgsqlCommand command, string name, DateTimeOffset? value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.TimestampTz, value is null ? DBNull.Value : value.Value);
}
