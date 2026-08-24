using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Workers.Outbox;
using YO4X.Outbox;
using YO4X.Persistence.Postgres;
using YO4X.Tenancy;

namespace YO4X.ControlPlane.Workers.Operations;

internal static class WorkerDatabaseIdentity
{
    public const string RequiredRole = "yo4x_worker";

    public static readonly Guid ServiceActorId =
        Guid.Parse("21e67e5a-daec-46eb-84af-f97244508616");
}

internal sealed class PostgresWorkerReadiness
{
    private static readonly TimeSpan SnapshotLifetime = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private const string ProbeSql = """
        select
            current_user = 'yo4x_worker'
            -- Same transport posture as PostgresRuntimeConnectionPolicy:
            -- verified TLS session, or plaintext on an explicit loopback endpoint.
            and (
                (
                    select ssl from pg_catalog.pg_stat_ssl
                    where pid = pg_catalog.pg_backend_pid()
                ) is true
                or coalesce(pg_catalog.host(pg_catalog.inet_client_addr()), '')
                    in ('127.0.0.1', '::1')
            )
            and has_function_privilege(current_user, 'control.acquire_u0_authority_lock()', 'EXECUTE')
            and has_function_privilege(current_user, 'control.apply_confirmed_broker_operation_result(uuid,uuid,uuid)', 'EXECUTE')
            and has_function_privilege(current_user, 'control.claim_credential_grant_cleanup(uuid,uuid,bigint,text,integer)', 'EXECUTE')
            and has_function_privilege(current_user, 'control.complete_credential_grant_cleanup(uuid,uuid,bigint,text,uuid,uuid)', 'EXECUTE')
            and has_function_privilege(current_user, 'control.refresh_user_operation_backlog_observation()', 'EXECUTE')
            and has_function_privilege(current_user, 'control.defer_user_operation(uuid,uuid,bigint,text,text)', 'EXECUTE')
            and has_column_privilege(current_user, 'identity.tenants', 'id', 'SELECT')
            and has_column_privilege(current_user, 'control.user_operations', 'state', 'SELECT')
            and has_column_privilege(current_user, 'control.user_operations', 'state', 'UPDATE')
            and has_column_privilege(current_user, 'control.user_operations', 'submitted_resource_version', 'SELECT')
            and has_column_privilege(current_user, 'control.user_operations', 'requested_target_state', 'SELECT')
            and has_column_privilege(current_user, 'control.user_operations', 'dispatch_route_deployment_id', 'SELECT')
            and has_column_privilege(current_user, 'control.user_operations', 'dispatch_target_binding_sha256', 'SELECT')
            and has_column_privilege(current_user, 'control.user_operations', 'next_processing_at', 'SELECT')
            and has_column_privilege(current_user, 'control.user_operations', 'processing_deferral_count', 'SELECT')
            and has_column_privilege(current_user, 'control.user_operations', 'last_processing_error_code', 'SELECT')
            and not has_column_privilege(current_user, 'control.user_operations', 'next_processing_at', 'UPDATE')
            and not has_column_privilege(current_user, 'control.user_operations', 'processing_deferral_count', 'UPDATE')
            and not has_column_privilege(current_user, 'control.user_operations', 'last_processing_error_code', 'UPDATE')
            and has_column_privilege(current_user, 'control.user_operations', 'dispatch_policy_snapshot_sha256', 'UPDATE')
            and has_column_privilege(current_user, 'control.user_operations', 'dispatch_target_binding_sha256', 'UPDATE')
            and has_column_privilege(current_user, 'control.user_operations', 'result_capability_sha256', 'UPDATE')
            and has_column_privilege(current_user, 'control.user_operations', 'result_capability_expires_at', 'UPDATE')
            and has_column_privilege(current_user, 'control.user_operations', 'dispatch_assignment_lease_expires_at', 'UPDATE')
            and has_column_privilege(current_user, 'control.user_operations', 'dispatch_execution_deadline', 'UPDATE')
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
            and has_column_privilege(current_user, 'control.user_operation_backlog_observations', 'tenant_id', 'SELECT')
            and has_column_privilege(current_user, 'control.user_operation_backlog_observations', 'oldest_open_created_at', 'SELECT')
            and not has_any_column_privilege(current_user, 'control.user_operation_backlog_observations', 'INSERT')
            and not has_any_column_privilege(current_user, 'control.user_operation_backlog_observations', 'UPDATE')
            and not has_table_privilege(current_user, 'control.user_operation_backlog_observations', 'DELETE')
            and not has_table_privilege(current_user, 'control.user_operation_backlog_observations', 'TRUNCATE')
            and has_column_privilege(current_user, 'operations.deployments', 'observed_state', 'UPDATE')
            and has_table_privilege(current_user, 'operations.deployment_reconciliations', 'SELECT')
            and not has_table_privilege(current_user, 'operations.deployment_reconciliations', 'INSERT')
            and not has_any_column_privilege(current_user, 'operations.deployment_reconciliations', 'UPDATE')
            and not has_table_privilege(current_user, 'operations.deployment_reconciliations', 'DELETE')
            and not has_table_privilege(current_user, 'operations.deployment_reconciliations', 'TRUNCATE')
            and has_table_privilege(current_user, 'operations.user_operation_results', 'SELECT')
            and not has_table_privilege(current_user, 'operations.user_operation_results', 'INSERT')
            and not has_any_column_privilege(current_user, 'operations.user_operation_results', 'UPDATE')
            and not has_table_privilege(current_user, 'operations.user_operation_results', 'DELETE')
            and not has_table_privilege(current_user, 'operations.user_operation_results', 'TRUNCATE')
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
            and
            (
                select coalesce(
                    array_agg(consumer order by consumer),
                    array[]::text[])
                    = array[
                        'credential_grant_expiry',
                        'deployment_projection',
                        'outbox',
                        'user_operations']::text[]
                from control.worker_tenant_scan_cursors
            )
        """;

    private readonly PostgresDatabase database;
    private readonly BoundedBooleanProbe readinessProbe;

    public PostgresWorkerReadiness(PostgresDatabase database)
    {
        this.database = database;
        readinessProbe = new BoundedBooleanProbe(
            ProbeCoreAsync,
            SnapshotLifetime,
            ProbeTimeout);
    }

    public ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken) =>
        readinessProbe.GetAsync(cancellationToken);

    private async ValueTask<bool> ProbeCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!database.HasTenantContextCapabilityProvider)
        {
            return false;
        }

        if (!await database.IsTenantContextCapabilityProviderReadyAsync(cancellationToken)
                .ConfigureAwait(false))
        {
            return false;
        }

        try
        {
            await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!await PostgresRoleCapabilityFingerprint.IsSatisfiedAsync(
                    connection,
                    transaction: null,
                    Yo4xPostgresRoleContracts.Worker,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                return false;
            }

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
    private const string ScanProgressHealthSql = """
        with scan_clock as materialized
        (
            select clock_timestamp() as checked_at
        ),
        global_progress as
        (
            select last_scan_at, last_rotation_completed_at
            from control.worker_tenant_scan_cursors
            where consumer = @consumer
        )
        select
            (select count(*) from global_progress) = 1
            and exists
            (
                select 1
                from global_progress
                cross join scan_clock
                where last_scan_at is not null
                  and last_rotation_completed_at is not null
                  and last_rotation_completed_at >=
                      scan_clock.checked_at - @maximum_rotation_age
            )
            and
            (
                @consumer <> 'deployment_projection'
                or not exists
                (
                    select 1
                    from identity.tenants as tenant
                    left join control.deployment_scan_cursors as progress
                      on progress.tenant_id = tenant.id
                    cross join scan_clock
                    where progress.tenant_id is null
                       or progress.last_scan_at is null
                       or progress.last_rotation_completed_at is null
                       or progress.last_rotation_completed_at <
                           scan_clock.checked_at - @maximum_rotation_age
                )
            )
        """;

    private const string AdvanceTenantScanSql = """
        with locked_cursor as materialized
        (
            select last_tenant_id, rotation_count
            from control.worker_tenant_scan_cursors
            where consumer = @consumer
            for update
        ),
        candidate as materialized
        (
            select
                tenant.id,
                locked_cursor.last_tenant_id is not null
                    and tenant.id <= locked_cursor.last_tenant_id
                    as completes_rotation,
                locked_cursor.rotation_count
                    + case
                        when locked_cursor.last_tenant_id is not null
                            and tenant.id <= locked_cursor.last_tenant_id
                        then 1
                        else 0
                      end as next_rotation_count
            from locked_cursor
            cross join lateral
            (
                select id
                from identity.tenants
                order by
                    case
                        when locked_cursor.last_tenant_id is not null
                            and id <= locked_cursor.last_tenant_id
                        then 1
                        else 0
                    end,
                    id
                limit 1
            ) as tenant
        ),
        eligible as materialized
        (
            select id, completes_rotation, next_rotation_count
            from candidate
            where @rotation_ceiling is null
               or next_rotation_count <= @rotation_ceiling
        ),
        catalog_state as materialized
        (
            select not exists (select 1 from identity.tenants) as is_empty
        ),
        advanced as
        (
            update control.worker_tenant_scan_cursors as progress
            set last_tenant_id = coalesce(eligible.id, progress.last_tenant_id)
            from locked_cursor
            cross join catalog_state
            left join eligible on true
            where progress.consumer = @consumer
              and (eligible.id is not null or catalog_state.is_empty)
            returning
                eligible.id as id,
                coalesce(eligible.completes_rotation, false)
                    as completes_rotation,
                progress.rotation_count as rotation_count
        )
        select id, completes_rotation, rotation_count
        from advanced
        """;

    private const string UserOperationBacklogHealthSql = """
        with health_clock as materialized
        (
            select clock_timestamp() as checked_at
        )
        select not exists
        (
            select 1
            from identity.tenants as tenant
            left join control.user_operation_backlog_observations as observation
              on observation.tenant_id = tenant.id
            cross join health_clock
            where observation.tenant_id is null
               or observation.last_checked_at <
                    health_clock.checked_at - @maximum_observation_age
               or
               (
                   observation.oldest_open_created_at is not null
                   and observation.oldest_open_created_at <
                        health_clock.checked_at - @maximum_backlog_age
               )
        )
        """;

    private readonly WorkerTenantScanCoordinator scanCoordinator =
        new(options.TenantBatchSize);

    public ValueTask<WorkerTenantScanLease> BeginScanAsync(
        WorkerTenantScanConsumer consumer,
        CancellationToken cancellationToken) =>
        scanCoordinator.AcquireAsync(
            consumer,
            AdvanceDurableCursorAsync,
            cancellationToken);

    private async ValueTask<WorkerTenantScanStep?> AdvanceDurableCursorAsync(
        WorkerTenantScanConsumer consumer,
        long? rotationCeiling,
        CancellationToken cancellationToken)
    {
        if (!await readiness.IsReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new BackendCapabilityUnavailableException("postgres-worker-readiness");
        }

        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using NpgsqlCommand command = new(AdvanceTenantScanSql, connection);
        command.Parameters.AddWithValue(
            "consumer",
            NpgsqlDbType.Text,
            DatabaseConsumer(consumer));
        command.Parameters.Add(new NpgsqlParameter("rotation_ceiling", NpgsqlDbType.Bigint)
        {
            Value = rotationCeiling is long ceiling ? ceiling : DBNull.Value
        });
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        if (reader.IsDBNull(0))
        {
            return null;
        }

        var step = new WorkerTenantScanStep(
            reader.GetGuid(0),
            reader.GetBoolean(1),
            reader.GetInt64(2));
        step.Validate();
        return step;
    }

    public async ValueTask<bool> IsScanProgressHealthyAsync(
        WorkerTenantScanConsumer consumer,
        TimeSpan maximumRotationAge,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            maximumRotationAge,
            TimeSpan.Zero);
        if (!await readiness.IsReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using NpgsqlCommand command = new(ScanProgressHealthSql, connection);
        command.Parameters.AddWithValue(
            "consumer",
            NpgsqlDbType.Text,
            DatabaseConsumer(consumer));
        command.Parameters.AddWithValue(
            "maximum_rotation_age",
            NpgsqlDbType.Interval,
            maximumRotationAge);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true;
    }

    public async ValueTask<bool> IsUserOperationBacklogHealthyAsync(
        TimeSpan maximumObservationAge,
        TimeSpan maximumBacklogAge,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            maximumObservationAge,
            TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            maximumBacklogAge,
            TimeSpan.Zero);
        if (!await readiness.IsReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using NpgsqlCommand command = new(UserOperationBacklogHealthSql, connection);
        command.Parameters.AddWithValue(
            "maximum_observation_age",
            NpgsqlDbType.Interval,
            maximumObservationAge);
        command.Parameters.AddWithValue(
            "maximum_backlog_age",
            NpgsqlDbType.Interval,
            maximumBacklogAge);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true;
    }

    private static string DatabaseConsumer(WorkerTenantScanConsumer consumer) =>
        consumer switch
        {
            WorkerTenantScanConsumer.Outbox => "outbox",
            WorkerTenantScanConsumer.CredentialGrantExpiry => "credential_grant_expiry",
            WorkerTenantScanConsumer.DeploymentProjection => "deployment_projection",
            WorkerTenantScanConsumer.UserOperations => "user_operations",
            _ => throw new ArgumentOutOfRangeException(nameof(consumer))
        };

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

    public ValueTask<bool> IsScanProgressHealthyAsync(
        TimeSpan maximumRotationAge,
        CancellationToken cancellationToken) =>
        tenantCatalog.IsScanProgressHealthyAsync(
            WorkerTenantScanConsumer.Outbox,
            maximumRotationAge,
            cancellationToken);

    public async ValueTask<IReadOnlyList<ClaimedOutboxItem>> ClaimAsync(
        OutboxClaimRequest request,
        CancellationToken cancellationToken)
    {
        request.Validate();
        await using WorkerTenantScanLease tenantScan = await tenantCatalog.BeginScanAsync(
                WorkerTenantScanConsumer.Outbox,
                cancellationToken)
            .ConfigureAwait(false);
        var claimed = new List<ClaimedOutboxItem>(request.MaximumMessages);
        while (claimed.Count < request.MaximumMessages)
        {
            WorkerTenantScanStep? step = await tenantScan.TryBeginNextAsync(cancellationToken)
                .ConfigureAwait(false);
            if (step is not { } tenantStep)
            {
                break;
            }

            Guid tenantId = tenantStep.TenantId;
            int remaining = request.MaximumMessages - claimed.Count;
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
                OutboxSchemaVersion.ValidateStored(
                    message.MessageType,
                    message.SchemaVersion,
                    message.PayloadJson),
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
