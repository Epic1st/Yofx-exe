using Npgsql;
using NpgsqlTypes;
using YO4X.ControlPlane.Workers.Outbox;
using YO4X.Persistence.Postgres;
using YO4X.Tenancy;

namespace YO4X.ControlPlane.Workers.Operations;

internal static class WorkerDatabaseIdentity
{
    public const string RequiredRole = "yo4x_worker";

    public static readonly Guid ServiceActorId =
        Guid.Parse("21e67e5a-daec-46eb-84af-f97244508616");
}

internal sealed class PostgresWorkerReadiness(PostgresDatabase database)
{
    private const string ProbeSql = """
        select
            current_user = 'yo4x_worker'
            and coalesce(
                (select ssl from pg_catalog.pg_stat_ssl where pid = pg_catalog.pg_backend_pid()),
                false)
            and has_function_privilege(current_user, 'control.acquire_u0_authority_lock()', 'EXECUTE')
            and has_function_privilege(current_user, 'control.apply_confirmed_broker_operation_result(uuid,uuid,uuid)', 'EXECUTE')
            and has_function_privilege(current_user, 'control.claim_credential_grant_cleanup(uuid,uuid,bigint,text,integer)', 'EXECUTE')
            and has_function_privilege(current_user, 'control.complete_credential_grant_cleanup(uuid,uuid,bigint,text,uuid,uuid)', 'EXECUTE')
            and has_column_privilege(current_user, 'identity.tenants', 'id', 'SELECT')
            and has_column_privilege(current_user, 'control.user_operations', 'state', 'SELECT')
            and has_column_privilege(current_user, 'control.user_operations', 'state', 'UPDATE')
            and has_column_privilege(current_user, 'control.user_operations', 'submitted_resource_version', 'SELECT')
            and has_column_privilege(current_user, 'control.user_operations', 'requested_target_state', 'SELECT')
            and has_column_privilege(current_user, 'control.user_operations', 'dispatch_route_deployment_id', 'SELECT')
            and has_column_privilege(current_user, 'control.user_operations', 'dispatch_target_binding_sha256', 'SELECT')
            and has_column_privilege(current_user, 'control.user_operations', 'dispatch_policy_snapshot_sha256', 'UPDATE')
            and has_column_privilege(current_user, 'control.user_operations', 'dispatch_target_binding_sha256', 'UPDATE')
            and has_column_privilege(current_user, 'control.user_operations', 'reconciliation_worker_assignment_id', 'UPDATE')
            and has_table_privilege(current_user, 'control.user_policy_evaluations', 'SELECT')
            and has_column_privilege(current_user, 'control.credential_ingestion_grants', 'expires_at', 'SELECT')
            and not has_any_column_privilege(current_user, 'control.credential_ingestion_grants', 'UPDATE')
            and not has_any_column_privilege(current_user, 'control.credential_ingestion_grants', 'INSERT')
            and not has_table_privilege(current_user, 'control.credential_ingestion_grants', 'DELETE')
            and not has_table_privilege(current_user, 'control.credential_ingestion_grants', 'TRUNCATE')
            and has_column_privilege(current_user, 'operations.broker_accounts', 'credential_state', 'SELECT')
            and has_column_privilege(current_user, 'operations.broker_accounts', 'broker_id', 'SELECT')
            and not has_any_column_privilege(current_user, 'operations.broker_accounts', 'UPDATE')
            and not has_any_column_privilege(current_user, 'operations.broker_accounts', 'INSERT')
            and not has_table_privilege(current_user, 'operations.broker_accounts', 'DELETE')
            and not has_table_privilege(current_user, 'operations.broker_accounts', 'TRUNCATE')
            and not has_column_privilege(current_user, 'operations.broker_accounts', 'credential_reference', 'SELECT')
            and has_column_privilege(current_user, 'operations.deployments', 'observed_state', 'UPDATE')
            and has_table_privilege(current_user, 'operations.deployment_reconciliations', 'SELECT')
            and has_table_privilege(current_user, 'operations.user_operation_results', 'SELECT')
            and not has_table_privilege(current_user, 'operations.user_operation_results', 'INSERT')
            and has_column_privilege(current_user, 'operations.deployment_reconciliations', 'generation', 'SELECT')
            and has_column_privilege(current_user, 'operations.deployment_reconciliations', 'broker_confirmed', 'SELECT')
            and has_table_privilege(current_user, 'operations.runtime_component_evidence', 'SELECT')
            and has_table_privilege(current_user, 'operations.execution_leases', 'SELECT')
            and has_table_privilege(current_user, 'audit.audit_events', 'INSERT')
            and has_table_privilege(current_user, 'messaging.outbox_messages', 'SELECT,INSERT')
            and has_column_privilege(current_user, 'messaging.outbox_messages', 'state', 'UPDATE')
            and has_column_privilege(current_user, 'messaging.outbox_messages', 'attempts', 'UPDATE')
            and has_column_privilege(current_user, 'messaging.outbox_messages', 'available_at', 'UPDATE')
            and has_column_privilege(current_user, 'messaging.outbox_messages', 'locked_by', 'UPDATE')
            and has_column_privilege(current_user, 'messaging.outbox_messages', 'locked_until', 'UPDATE')
            and has_column_privilege(current_user, 'messaging.outbox_messages', 'published_at', 'UPDATE')
            and has_column_privilege(current_user, 'messaging.outbox_messages', 'last_error', 'UPDATE')
        """;

    public async ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using NpgsqlCommand command = new(ProbeSql, connection);
            return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true;
        }
        catch (NpgsqlException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (TimeoutException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}

internal sealed class PostgresWorkerTenantCatalog(
    PostgresDatabase database,
    PostgresWorkerReadiness readiness,
    ControlWorkOptions options)
{
    public async Task<IReadOnlyList<Guid>> GetTenantIdsAsync(CancellationToken cancellationToken)
    {
        if (!await readiness.IsReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using NpgsqlCommand command = new(
            "select id from identity.tenants order by id limit @maximum_tenants",
            connection);
        command.Parameters.AddWithValue("maximum_tenants", NpgsqlDbType.Integer, options.TenantBatchSize);
        var tenantIds = new List<Guid>(options.TenantBatchSize);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            tenantIds.Add(reader.GetGuid(0));
        }

        return tenantIds;
    }

    public static TenantExecutionContext CreateContext(Guid tenantId, Guid correlationId) =>
        new(tenantId, WorkerDatabaseIdentity.ServiceActorId, correlationId);
}

internal sealed class PostgresWorkerOutboxStore(
    PostgresDatabase database,
    PostgresWorkerReadiness readiness,
    PostgresWorkerTenantCatalog tenantCatalog) : IPostgresOutboxStore
{
    public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        readiness.IsReadyAsync(cancellationToken);

    public async ValueTask<IReadOnlyList<ClaimedOutboxItem>> ClaimAsync(
        OutboxClaimRequest request,
        CancellationToken cancellationToken)
    {
        request.Validate();
        IReadOnlyList<Guid> tenantIds = await tenantCatalog.GetTenantIdsAsync(cancellationToken)
            .ConfigureAwait(false);
        var claimed = new List<ClaimedOutboxItem>(request.MaximumMessages);
        foreach (Guid tenantId in tenantIds)
        {
            int remaining = request.MaximumMessages - claimed.Count;
            if (remaining == 0)
            {
                break;
            }

            Guid correlationId = Guid.CreateVersion7();
            await using TenantPostgresTransaction transaction =
                await database.BeginTenantTransactionAsync(
                    PostgresWorkerTenantCatalog.CreateContext(tenantId, correlationId),
                    cancellationToken).ConfigureAwait(false);
            IReadOnlyList<YO4X.Outbox.ClaimedOutboxMessage> messages =
                await PostgresOutboxRepository.ClaimAsync(
                    transaction,
                    request.WorkerId,
                    remaining,
                    request.ClaimedAtUtc,
                    request.LeaseDuration,
                    cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            claimed.AddRange(messages.Select(static message => new ClaimedOutboxItem(
                message.Id,
                message.TenantId,
                message.MessageType,
                schemaVersion: 1,
                message.PayloadJson,
                message.PayloadSha256,
                message.OccurredAt,
                message.Attempts)));
        }

        return claimed;
    }

    public async ValueTask<bool> SettleAsync(
        OutboxSettlement settlement,
        CancellationToken cancellationToken)
    {
        settlement.Validate();
        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(
                PostgresWorkerTenantCatalog.CreateContext(settlement.TenantId, Guid.CreateVersion7()),
                cancellationToken).ConfigureAwait(false);
        bool settled = settlement.Kind switch
        {
            OutboxSettlementKind.Published => await PostgresOutboxRepository.MarkPublishedAsync(
                transaction,
                settlement.MessageId,
                settlement.WorkerId,
                settlement.SettledAtUtc,
                cancellationToken).ConfigureAwait(false),
            OutboxSettlementKind.Retry or OutboxSettlementKind.DeadLetter =>
                await PostgresOutboxRepository.ReleaseAfterFailureAsync(
                    transaction,
                    settlement.MessageId,
                    settlement.WorkerId,
                    settlement.Code,
                    settlement.RetryAtUtc ?? settlement.SettledAtUtc,
                    settlement.Kind == OutboxSettlementKind.DeadLetter ? 1 : int.MaxValue,
                    cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(settlement))
        };
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return settled;
    }
}
