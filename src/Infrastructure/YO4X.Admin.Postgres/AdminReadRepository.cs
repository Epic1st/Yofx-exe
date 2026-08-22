using Npgsql;
using NpgsqlTypes;
using YO4X.Admin.Application;
using YO4X.Persistence.Postgres;
using YO4X.ReadModels;

namespace YO4X.Admin.Postgres;

internal static class AdminReadRepository
{
    internal const string CommandColumns = """
        command.id,
        command.command_type,
        command.payload_sha256,
        command.command_digest,
        command.restriction_vector::text,
        command.allowed_compensation_types,
        command.actor_id,
        command.session_id,
        command.environment,
        command.scope_type,
        command.scope_id,
        command.risk_level,
        command.reason_code,
        command.written_reason,
        command.ticket_reference,
        command.idempotency_record_id,
        command.expected_resource_version,
        command.impact_preview_id,
        preview.digest,
        command.state,
        command.original_command_id,
        command.compensation_command_id,
        command.requested_execution_at,
        command.expires_at,
        command.correlation_id,
        command.row_version,
        command.created_at,
        command.updated_at
        """;

    public static async Task<AdminCommandRecord?> GetCommandAsync(
        TenantPostgresTransaction transaction,
        Guid commandId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        string lockingClause = forUpdate ? "for update of command" : string.Empty;
        await using NpgsqlCommand command = transaction.CreateCommand($$"""
            select {{CommandColumns}}
            from control.admin_commands as command
            left join control.impact_previews as preview
              on preview.tenant_id = command.tenant_id
             and preview.id = command.impact_preview_id
            where command.tenant_id = @tenant_id
              and command.id = @command_id
            {{lockingClause}}
            """);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        command.Parameters.AddWithValue("command_id", NpgsqlDbType.Uuid, commandId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadCommand(reader, 0)
            : null;
    }

    public static async Task<IReadOnlyList<ApprovalRecord>> GetApprovalPageAsync(
        TenantPostgresTransaction transaction,
        int limit,
        Guid? before,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand($$"""
            select
                approval.id,
                approval.command_id,
                approval.requester_id,
                approval.policy_key,
                approval.impact_preview_id,
                approval.command_digest,
                approval.impact_digest,
                approval.command_row_version,
                approval.restriction_digest,
                approval.binding_snapshot::text,
                approval.binding_digest,
                approval.required_approvals,
                approval.minimum_assurance,
                approval.managed_device_required,
                approval.maximum_session_age_seconds,
                approval.state,
                approval.invalidation_code,
                approval.expires_at,
                approval.row_version,
                approval.created_at,
                (select count(*)::integer
                   from control.approval_decisions as decision
                  where decision.tenant_id = approval.tenant_id
                    and decision.approval_request_id = approval.id
                    and decision.decision = 'approve') as received_approvals,
                {{CommandColumns}}
            from control.approval_requests as approval
            join control.admin_commands as command
              on command.tenant_id = approval.tenant_id
             and command.id = approval.command_id
            left join control.impact_previews as preview
              on preview.tenant_id = command.tenant_id
             and preview.id = command.impact_preview_id
            where approval.tenant_id = @tenant_id
              and (@before is null or approval.id < @before)
            order by approval.id desc
            limit @limit
            """);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        command.Parameters.AddWithValue(
            "before",
            NpgsqlDbType.Uuid,
            before is null ? DBNull.Value : before.Value);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        var approvals = new List<ApprovalRecord>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            approvals.Add(ReadApproval(reader));
        }

        return approvals.AsReadOnly();
    }

    public static async Task<ApprovalRecord?> GetApprovalAsync(
        TenantPostgresTransaction transaction,
        Guid approvalId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        string lockingClause = forUpdate ? "for update of approval" : string.Empty;
        await using NpgsqlCommand command = transaction.CreateCommand($$"""
            select
                approval.id,
                approval.command_id,
                approval.requester_id,
                approval.policy_key,
                approval.impact_preview_id,
                approval.command_digest,
                approval.impact_digest,
                approval.command_row_version,
                approval.restriction_digest,
                approval.binding_snapshot::text,
                approval.binding_digest,
                approval.required_approvals,
                approval.minimum_assurance,
                approval.managed_device_required,
                approval.maximum_session_age_seconds,
                approval.state,
                approval.invalidation_code,
                approval.expires_at,
                approval.row_version,
                approval.created_at,
                (select count(*)::integer
                   from control.approval_decisions as decision
                  where decision.tenant_id = approval.tenant_id
                    and decision.approval_request_id = approval.id
                    and decision.decision = 'approve') as received_approvals,
                {{CommandColumns}}
            from control.approval_requests as approval
            join control.admin_commands as command
              on command.tenant_id = approval.tenant_id
             and command.id = approval.command_id
            left join control.impact_previews as preview
              on preview.tenant_id = command.tenant_id
             and preview.id = command.impact_preview_id
            where approval.tenant_id = @tenant_id
              and approval.id = @approval_id
            {{lockingClause}}
            """);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        command.Parameters.AddWithValue("approval_id", NpgsqlDbType.Uuid, approvalId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadApproval(reader)
            : null;
    }

    public static async Task<IReadOnlyList<CommandTargetView>> GetTargetsAsync(
        TenantPostgresTransaction transaction,
        Guid commandId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select
                id,
                resource_id,
                resource_type,
                resource_version,
                required_proof,
                required,
                worker_id,
                generation,
                state,
                attempts,
                dispatched_at,
                delivered_at,
                acknowledged_at,
                applied_at,
                reconciled_at,
                observed_result,
                broker_evidence_reference,
                last_error_code,
                created_at,
                updated_at
            from control.command_targets
            where tenant_id = @tenant_id
              and command_id = @command_id
            order by id
            """);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        command.Parameters.AddWithValue("command_id", NpgsqlDbType.Uuid, commandId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        var targets = new List<CommandTargetView>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            targets.Add(new CommandTargetView(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetInt64(3),
                AdminStorageValues.ParseTargetProof(reader.GetString(4)),
                reader.GetBoolean(5),
                NullableGuid(reader, 6),
                NullableInt64(reader, 7),
                AdminStorageValues.ParseTargetStatus(reader.GetString(8)),
                reader.GetInt32(9),
                NullableTimestamp(reader, 10),
                NullableTimestamp(reader, 11),
                NullableTimestamp(reader, 12),
                NullableTimestamp(reader, 13),
                NullableTimestamp(reader, 14),
                NullableString(reader, 15),
                NullableString(reader, 16),
                NullableString(reader, 17),
                reader.GetFieldValue<DateTimeOffset>(18),
                reader.GetFieldValue<DateTimeOffset>(19)));
        }

        return targets.AsReadOnly();
    }

    public static async Task<DeploymentResource?> GetDeploymentAsync(
        TenantPostgresTransaction transaction,
        Guid deploymentId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        string lockingClause = forUpdate ? "for update of deployment" : string.Empty;
        await using NpgsqlCommand command = transaction.CreateCommand($$"""
            select
                deployment.id,
                deployment.tenant_id,
                deployment.user_id,
                deployment.broker_account_id,
                account.broker_id,
                deployment.strategy_version_id,
                deployment.gateway_artifact_id,
                deployment.region,
                deployment.environment,
                deployment.desired_state,
                deployment.observed_state,
                deployment.broker_hosted_stop_loss,
                deployment.broker_hosted_take_profit,
                deployment.fence_generation,
                worker.worker_node_id,
                worker.fence_generation,
                deployment.row_version,
                deployment.updated_at,
                coalesce(health.supervisor_state, 'unknown'),
                coalesce(health.strategy_host_state, 'unknown'),
                coalesce(health.gateway_host_state, 'unknown'),
                coalesce(health.broker_state, 'unknown'),
                coalesce(health.source_version, deployment.row_version),
                coalesce(health.projected_at, deployment.updated_at)
            from operations.deployments as deployment
            join operations.broker_accounts as account
              on account.tenant_id = deployment.tenant_id
             and account.id = deployment.broker_account_id
            left join lateral
            (
                select assignment.worker_node_id, assignment.fence_generation
                from operations.worker_assignments as assignment
                where assignment.tenant_id = deployment.tenant_id
                  and assignment.deployment_id = deployment.id
                  and assignment.state in ('assigned', 'reconciliation_only', 'active', 'revoking')
                order by assignment.fence_generation desc, assignment.id desc
                limit 1
            ) as worker on true
            left join readmodel.deployment_health as health
              on health.tenant_id = deployment.tenant_id
             and health.deployment_id = deployment.id
            where deployment.tenant_id = @tenant_id
              and deployment.id = @deployment_id
            {{lockingClause}}
            """);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        command.Parameters.AddWithValue("deployment_id", NpgsqlDbType.Uuid, deploymentId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new DeploymentResource(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetGuid(4),
            reader.GetGuid(5),
            reader.GetGuid(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.GetBoolean(11),
            reader.GetBoolean(12),
            reader.GetInt64(13),
            NullableGuid(reader, 14),
            NullableInt64(reader, 15),
            reader.GetInt64(16),
            reader.GetFieldValue<DateTimeOffset>(17),
            reader.GetString(18),
            reader.GetString(19),
            reader.GetString(20),
            reader.GetString(21),
            reader.GetInt64(22),
            reader.GetFieldValue<DateTimeOffset>(23));
    }

    public static DeploymentOperationsView ToView(DeploymentResource deployment) => new(
        deployment.Id,
        deployment.TenantId,
        deployment.DesiredState,
        deployment.SupervisorState,
        deployment.StrategyHostState,
        deployment.GatewayHostState,
        deployment.BrokerState,
        deployment.FenceGeneration,
        deployment.SourceVersion,
        deployment.ProjectedAt);

    public static async Task<ImpactPreviewRecord?> GetImpactPreviewAsync(
        TenantPostgresTransaction transaction,
        Guid previewId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select
                id,
                tenant_id,
                actor_id,
                scope_expression::text,
                target_snapshot::text,
                target_count,
                resource_version_watermark,
                policy_version,
                digest,
                created_at,
                expires_at
            from control.impact_previews
            where tenant_id = @tenant_id
              and id = @preview_id
            """);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        command.Parameters.AddWithValue("preview_id", NpgsqlDbType.Uuid, previewId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new ImpactPreviewRecord(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt32(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetFieldValue<DateTimeOffset>(9),
            reader.GetFieldValue<DateTimeOffset>(10));
    }

    private static ApprovalRecord ReadApproval(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetGuid(2),
        reader.GetString(3),
        reader.GetGuid(4),
        reader.GetString(5),
        reader.GetString(6),
        reader.GetInt64(7),
        reader.GetString(8),
        reader.GetString(9),
        reader.GetString(10),
        reader.GetInt16(11),
        reader.GetString(12),
        reader.GetBoolean(13),
        reader.GetInt32(14),
        reader.GetString(15),
        NullableString(reader, 16),
        reader.GetFieldValue<DateTimeOffset>(17),
        reader.GetInt64(18),
        reader.GetFieldValue<DateTimeOffset>(19),
        reader.GetInt32(20),
        ReadCommand(reader, 21));

    internal static AdminCommandRecord ReadCommand(NpgsqlDataReader reader, int offset)
    {
        string[] compensationTypes = reader.GetFieldValue<string[]>(offset + 5);
        return new AdminCommandRecord(
            reader.GetGuid(offset),
            AdminStorageValues.ParseCommandType(reader.GetString(offset + 1)),
            reader.GetString(offset + 2),
            reader.GetString(offset + 3),
            reader.GetString(offset + 4),
            compensationTypes.Select(AdminStorageValues.ParseCommandType).ToArray(),
            reader.GetGuid(offset + 6),
            reader.GetGuid(offset + 7),
            reader.GetString(offset + 8),
            reader.GetString(offset + 9),
            NullableString(reader, offset + 10),
            reader.GetString(offset + 11),
            reader.GetString(offset + 12),
            reader.GetString(offset + 13),
            NullableString(reader, offset + 14),
            reader.GetGuid(offset + 15),
            NullableInt64(reader, offset + 16),
            NullableGuid(reader, offset + 17),
            NullableString(reader, offset + 18),
            AdminStorageValues.ParseCommandStatus(reader.GetString(offset + 19)),
            NullableGuid(reader, offset + 20),
            NullableGuid(reader, offset + 21),
            NullableTimestamp(reader, offset + 22),
            NullableTimestamp(reader, offset + 23),
            reader.GetGuid(offset + 24),
            reader.GetInt64(offset + 25),
            reader.GetFieldValue<DateTimeOffset>(offset + 26),
            reader.GetFieldValue<DateTimeOffset>(offset + 27));
    }

    internal static Guid? NullableGuid(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);

    internal static long? NullableInt64(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    internal static string? NullableString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    internal static DateTimeOffset? NullableTimestamp(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
}
