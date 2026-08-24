using Npgsql;
using NpgsqlTypes;
using YO4X.ControlPlane.Workers.Operations;
using YO4X.ControlPlane.Workers.Outbox;
using YO4X.Persistence.Postgres;
using YO4X.Tenancy;

namespace YO4X.Postgres.IntegrationTests;

public sealed partial class BrokerCommandAuthorizationPostgresTests
{
    private const string AdvanceGlobalCursorSql = """
        with locked_cursor as materialized
        (
            select last_tenant_id
            from control.worker_tenant_scan_cursors
            where consumer = @consumer
            for update
        ),
        candidate as materialized
        (
            select tenant.id
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
        advanced as
        (
            update control.worker_tenant_scan_cursors as progress
            set last_tenant_id = candidate.id
            from candidate
            where progress.consumer = @consumer
            returning
                progress.last_tenant_id,
                progress.last_scan_at,
                progress.last_advanced_at,
                progress.last_rotation_completed_at,
                progress.rotation_count,
                progress.row_version
        )
        select * from advanced
        """;

    [PostgresFact]
    public async Task CursorGuardsOwnProgressAndRejectForgedOrCrossTenantMutation()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();

        CursorProgress empty = await AdvanceEmptyGlobalCursorAsync(database, "outbox");
        Assert.Null(empty.CursorId);
        Assert.NotNull(empty.LastScanAt);
        Assert.Null(empty.LastAdvancedAt);
        Assert.Equal(empty.LastScanAt, empty.LastRotationCompletedAt);
        Assert.Equal(1, empty.RotationCount);
        Assert.Equal(1, empty.RowVersion);

        await AssertGlobalCursorForgeryRejectedAsync(database, empty);

        Guid[] tenantIds =
        [
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7()
        ];
        foreach (Guid tenantId in tenantIds)
        {
            await SeedCursorTenantAsync(database, tenantId);
        }

        Task<CursorProgress> firstAdvance = AdvanceGlobalCursorAsync(
            database,
            "credential_grant_expiry");
        Task<CursorProgress> secondAdvance = AdvanceGlobalCursorAsync(
            database,
            "credential_grant_expiry");
        CursorProgress[] concurrent = await Task.WhenAll(firstAdvance, secondAdvance);
        Assert.NotNull(concurrent[0].CursorId);
        Assert.NotNull(concurrent[1].CursorId);
        Assert.NotEqual(concurrent[0].CursorId, concurrent[1].CursorId);

        CursorProgress afterRestart = await AdvanceGlobalCursorAsync(
            database,
            "credential_grant_expiry");
        Assert.NotNull(afterRestart.CursorId);
        Assert.Equal(
            tenantIds.ToHashSet(),
            concurrent.Select(static progress => progress.CursorId!.Value)
                .Append(afterRestart.CursorId.Value)
                .ToHashSet());

        CursorProgress wrapped = await AdvanceGlobalCursorAsync(
            database,
            "credential_grant_expiry");
        Assert.Equal(1, wrapped.RotationCount);
        Assert.NotNull(wrapped.LastRotationCompletedAt);
        Assert.True(wrapped.RowVersion > afterRestart.RowVersion);

        CursorProgress beforeWrong = await ReadGlobalCursorAsync(
            database,
            "credential_grant_expiry");
        await using (NpgsqlConnection worker = new(database.WorkerConnectionString))
        {
            await worker.OpenAsync();
            await using var wrong = new NpgsqlCommand(
                """
                update control.worker_tenant_scan_cursors
                set last_tenant_id = @wrong_tenant_id
                where consumer = 'credential_grant_expiry'
                """,
                worker);
            wrong.Parameters.AddWithValue(
                "wrong_tenant_id",
                NpgsqlDbType.Uuid,
                Guid.CreateVersion7());
            PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
                () => wrong.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InvalidParameterValue, rejected.SqlState);
        }

        Assert.Equal(
            beforeWrong,
            await ReadGlobalCursorAsync(database, "credential_grant_expiry"));

        Guid firstTenant = tenantIds[0];
        Guid secondTenant = tenantIds[1];
        await InitializeDeploymentCursorAsync(database, firstTenant);
        CursorProgress deploymentEmpty = await AdvanceEmptyDeploymentCursorAsync(
            database,
            firstTenant);
        Assert.Null(deploymentEmpty.CursorId);
        Assert.Equal(1, deploymentEmpty.RotationCount);
        Assert.Equal(1, deploymentEmpty.RowVersion);
        Assert.Equal(deploymentEmpty.LastScanAt, deploymentEmpty.LastRotationCompletedAt);

        await AssertDeploymentCursorForgeryRejectedAsync(database, firstTenant);
        await InitializeDeploymentCursorAsync(database, secondTenant);
        await AssertDeploymentCursorCrossTenantMutationRejectedAsync(
            database,
            firstTenant,
            secondTenant);

        await using (NpgsqlConnection worker = new(database.WorkerConnectionString))
        {
            await worker.OpenAsync();
            await using var globalMetadata = new NpgsqlCommand(
                "select count(*) from control.deployment_scan_cursors",
                worker);
            Assert.Equal(2L, (long)(await globalMetadata.ExecuteScalarAsync())!);
        }
    }

    [PostgresFact]
    public async Task ActualWorkerCatalogCoordinatesConcurrencyRestartAndCeilingWithoutMutation()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        Guid[] tenantIds = Enumerable.Range(0, 5)
            .Select(static _ => Guid.CreateVersion7())
            .ToArray();
        foreach (Guid tenantId in tenantIds)
        {
            await SeedCursorTenantAsync(database, tenantId);
            await SeedCursorOutboxAsync(database, tenantId);
        }

        await PostgresProductionReadinessFixture.RemoveBroadActorGrantsAsync(database);
        var options = new ControlWorkOptions
        {
            TenantBatchSize = 2,
            MaximumTenantScanRotationAge = TimeSpan.FromMinutes(15)
        };
        options.Validate();

        var firstReadiness = new PostgresWorkerReadiness(database.Worker);
        await AssertWorkerReadyEventuallyAsync(firstReadiness);
        var firstCatalog = new PostgresWorkerTenantCatalog(
            database.Worker,
            firstReadiness,
            options);
        var firstOutbox = new PostgresWorkerOutboxStore(
            database.Worker,
            firstReadiness,
            firstCatalog);
        IReadOnlyList<ClaimedOutboxItem> saturated = await firstOutbox.ClaimAsync(
            new OutboxClaimRequest(
                "cursor-worker-a",
                1,
                UtcNow(),
                TimeSpan.FromSeconds(30)),
            CancellationToken.None);
        ClaimedOutboxItem firstClaim = Assert.Single(saturated);

        var restartedReadiness = new PostgresWorkerReadiness(database.Worker);
        var restartedCatalog = new PostgresWorkerTenantCatalog(
            database.Worker,
            restartedReadiness,
            options);
        var restartedOutbox = new PostgresWorkerOutboxStore(
            database.Worker,
            restartedReadiness,
            restartedCatalog);
        ClaimedOutboxItem secondClaim = Assert.Single(
            await restartedOutbox.ClaimAsync(
                new OutboxClaimRequest(
                    "cursor-worker-b",
                    1,
                    UtcNow(),
                    TimeSpan.FromSeconds(30)),
                CancellationToken.None));
        Assert.NotEqual(firstClaim.TenantId, secondClaim.TenantId);

        var concurrentCatalogA = new PostgresWorkerTenantCatalog(
            database.Worker,
            new PostgresWorkerReadiness(database.Worker),
            options);
        var concurrentCatalogB = new PostgresWorkerTenantCatalog(
            database.Worker,
            new PostgresWorkerReadiness(database.Worker),
            options);
        await using WorkerTenantScanLease concurrentLeaseA =
            await concurrentCatalogA.BeginScanAsync(
                WorkerTenantScanConsumer.UserOperations,
                CancellationToken.None);
        await using WorkerTenantScanLease concurrentLeaseB =
            await concurrentCatalogB.BeginScanAsync(
                WorkerTenantScanConsumer.UserOperations,
                CancellationToken.None);
        WorkerTenantScanStep?[] concurrentSteps = await Task.WhenAll(
            concurrentLeaseA.TryBeginNextAsync(CancellationToken.None).AsTask(),
            concurrentLeaseB.TryBeginNextAsync(CancellationToken.None).AsTask());
        Assert.All(concurrentSteps, static step => Assert.NotNull(step));
        Assert.NotEqual(
            concurrentSteps[0]!.Value.TenantId,
            concurrentSteps[1]!.Value.TenantId);

        var seen = new HashSet<Guid>();
        for (int cycle = 0; cycle < 4 && seen.Count < tenantIds.Length; cycle++)
        {
            var catalog = new PostgresWorkerTenantCatalog(
                database.Worker,
                new PostgresWorkerReadiness(database.Worker),
                options);
            await using WorkerTenantScanLease lease = await catalog.BeginScanAsync(
                WorkerTenantScanConsumer.DeploymentProjection,
                CancellationToken.None);
            while (await lease.TryBeginNextAsync(CancellationToken.None) is { } step)
            {
                seen.Add(step.TenantId);
            }
        }

        Assert.Equal(tenantIds.ToHashSet(), seen);

        var ceilingOptions = new ControlWorkOptions
        {
            TenantBatchSize = 100,
            MaximumTenantScanRotationAge = TimeSpan.FromMinutes(15)
        };
        ceilingOptions.Validate();
        await CompleteOneRotationAsync(
            database,
            WorkerTenantScanConsumer.CredentialGrantExpiry,
            ceilingOptions);
        CursorProgress positioned = await ReadGlobalCursorAsync(
            database,
            "credential_grant_expiry");
        Guid finalTenantId = await ReadLastTenantIdAsync(database);
        for (int advance = 0;
             positioned.CursorId != finalTenantId && advance < tenantIds.Length;
             advance++)
        {
            positioned = await AdvanceGlobalCursorAsync(
                database,
                "credential_grant_expiry");
        }

        Assert.Equal(finalTenantId, positioned.CursorId);
        CursorProgress beforeCeiling = await ReadGlobalCursorAsync(
            database,
            "credential_grant_expiry");
        Assert.False(await ProbeGlobalCeilingAsync(
            database,
            "credential_grant_expiry",
            beforeCeiling.RotationCount));
        CursorProgress afterCeiling = await ReadGlobalCursorAsync(
            database,
            "credential_grant_expiry");
        Assert.Equal(beforeCeiling, afterCeiling);
    }

    [PostgresFact]
    public async Task ActualDeploymentProjectionPersistsBatchProgressAndUsesCurrentRowAfterRace()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        VerificationFixture fixture = await SeedVerifiedStrategyAsync(database);
        await RecordVerificationAsync(database, fixture);
        await PromoteAsync(database, fixture);
        _ = await SeedRuntimeAuthorityAsync(database, fixture, desiredState: "running");
        Guid[] additionalDeployments = Enumerable.Range(0, 5)
            .Select(static _ => Guid.CreateVersion7())
            .ToArray();
        await CloneStoppedDeploymentsAsync(database, fixture, additionalDeployments);
        await PostgresProductionReadinessFixture.RemoveBroadActorGrantsAsync(database);

        var options = new ControlWorkOptions
        {
            TenantBatchSize = 1,
            DeploymentBatchSizePerTenant = 1,
            MaximumTenantScanRotationAge = TimeSpan.FromMinutes(15)
        };
        options.Validate();
        var readiness = new PostgresWorkerReadiness(database.Worker);
        await AssertWorkerReadyEventuallyAsync(readiness);
        var catalog = new PostgresWorkerTenantCatalog(database.Worker, readiness, options);
        var store = new PostgresDeploymentProjectionStore(
            database.Worker,
            readiness,
            catalog,
            options);

        await using TenantPostgresTransaction writer =
            await database.ControlApi.BeginTenantTransactionAsync(fixture.UserContext);
        await using (NpgsqlCommand lockAndAdvance = writer.CreateCommand(
            """
            select control.acquire_u0_authority_lock();
            update operations.deployments
            set row_version = row_version + 1,
                updated_at = clock_timestamp()
            where tenant_id = @tenant_id and id = @deployment_id
            """))
        {
            AddUuid(lockAndAdvance, "tenant_id", fixture.TenantId);
            AddUuid(lockAndAdvance, "deployment_id", fixture.DeploymentId);
            Assert.Equal(1, await lockAndAdvance.ExecuteNonQueryAsync());
        }

        Task<ControlWorkCycleResult> blockedCycle = store.RunCycleAsync(
            UtcNow(),
            CancellationToken.None);
        await WaitForDeploymentCursorAsync(database, fixture.TenantId, fixture.DeploymentId);
        await writer.CommitAsync();
        ControlWorkCycleResult raced = await blockedCycle;
        Assert.Equal(1, raced.ItemsExamined);
        Assert.Equal(1, raced.ItemsChanged);

        (long rowVersion, string observedState) = await ReadDeploymentVersionAsync(
            database,
            fixture);
        Assert.True(rowVersion >= 2);
        Assert.Equal("unreachable", observedState);

        var batchOptions = new ControlWorkOptions
        {
            TenantBatchSize = 1,
            DeploymentBatchSizePerTenant = 2,
            MaximumTenantScanRotationAge = TimeSpan.FromMinutes(15)
        };
        batchOptions.Validate();
        int examined = 0;
        for (int cycle = 0; cycle < 5; cycle++)
        {
            var cycleReadiness = new PostgresWorkerReadiness(database.Worker);
            // RunCycleAsync fails closed to a no-op when its readiness snapshot
            // is false, which would silently starve the batch-progress math.
            await AssertWorkerReadyEventuallyAsync(cycleReadiness);
            var cycleCatalog = new PostgresWorkerTenantCatalog(
                database.Worker,
                cycleReadiness,
                batchOptions);
            var cycleStore = new PostgresDeploymentProjectionStore(
                database.Worker,
                cycleReadiness,
                cycleCatalog,
                batchOptions);
            ControlWorkCycleResult result = await cycleStore.RunCycleAsync(
                UtcNow(),
                CancellationToken.None);
            examined += result.ItemsExamined;
            CursorProgress progress = await ReadDeploymentCursorAsync(
                database,
                fixture.TenantId);
            if (progress.RotationCount > 0)
            {
                break;
            }
        }

        CursorProgress completed = await ReadDeploymentCursorAsync(
            database,
            fixture.TenantId);
        Assert.True(examined >= additionalDeployments.Length);
        Assert.True(completed.RotationCount > 0);
        Assert.NotNull(completed.LastRotationCompletedAt);
    }

    [PostgresFact]
    public async Task BacklogObservationIsExactTenantPrivateMonotonicAndDatabaseOwned()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        DateTimeOffset oldCreatedAt = UtcNow().AddMinutes(-20);
        BacklogFixture first = await SeedEligibleUserOperationAsync(
            database,
            oldCreatedAt);
        Guid emptyTenantId = Guid.CreateVersion7();
        await SeedCursorTenantAsync(database, emptyTenantId);

        await using (NpgsqlConnection rawWorker =
            new(database.WorkerConnectionString))
        {
            await rawWorker.OpenAsync();
            await using var noContext = new NpgsqlCommand(
                "select * from control.refresh_user_operation_backlog_observation()",
                rawWorker);
            PostgresException noContextRejected =
                await Assert.ThrowsAsync<PostgresException>(
                    () => noContext.ExecuteNonQueryAsync());
            Assert.Equal(
                PostgresErrorCodes.InsufficientPrivilege,
                noContextRejected.SqlState);

            string[] forbidden =
            [
                "insert into control.user_operation_backlog_observations "
                    + "(tenant_id) values ('" + Guid.CreateVersion7() + "')",
                "update control.user_operation_backlog_observations "
                    + "set refresh_count = refresh_count + 1",
                "delete from control.user_operation_backlog_observations"
            ];
            foreach (string sql in forbidden)
            {
                await using var command = new NpgsqlCommand(sql, rawWorker);
                PostgresException denied = await Assert.ThrowsAsync<PostgresException>(
                    () => command.ExecuteNonQueryAsync());
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
            }
        }

        BacklogObservation initial = await RefreshBacklogAsync(database, first.TenantId);
        Assert.Equal(first.TenantId, initial.TenantId);
        Assert.Equal(oldCreatedAt, initial.OldestOpenCreatedAt);
        Assert.Equal(1, initial.RefreshCount);
        Assert.Equal(1, initial.RowVersion);

        BacklogObservation[] concurrent = await Task.WhenAll(
            RefreshBacklogAsync(database, first.TenantId),
            RefreshBacklogAsync(database, first.TenantId));
        Assert.Equal([2L, 3L], concurrent.Select(static item => item.RefreshCount).Order().ToArray());
        Assert.True(concurrent[1].LastCheckedAt != concurrent[0].LastCheckedAt);
        BacklogObservation newest = await ReadBacklogAsync(database, first.TenantId);
        Assert.Equal(3, newest.RefreshCount);
        Assert.Equal(3, newest.RowVersion);
        Assert.Equal(concurrent.Max(static item => item.LastCheckedAt), newest.LastCheckedAt);

        BacklogObservation empty = await RefreshBacklogAsync(database, emptyTenantId);
        Assert.Null(empty.OldestOpenCreatedAt);
        Assert.Equal(1, empty.RefreshCount);

        await using (NpgsqlConnection worker = new(database.WorkerConnectionString))
        {
            await worker.OpenAsync();
            await using var global = new NpgsqlCommand(
                "select count(*) from control.user_operation_backlog_observations",
                worker);
            Assert.Equal(2L, (long)(await global.ExecuteScalarAsync())!);
        }

        await using (TenantPostgresTransaction otherTenant =
            await database.Application.BeginTenantTransactionAsync(
                new TenantExecutionContext(
                    emptyTenantId,
                    Guid.CreateVersion7(),
                    Guid.CreateVersion7())))
        {
            await using NpgsqlCommand hidden = otherTenant.CreateCommand(
                """
                select count(*)
                from control.user_operation_backlog_observations
                where tenant_id = @tenant_id
                """);
            AddUuid(hidden, "tenant_id", first.TenantId);
            Assert.Equal(0L, (long)(await hidden.ExecuteScalarAsync())!);
        }

        await using (NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync())
        {
            await using var boundary = new NpgsqlCommand(
                """
                select
                    relation.relrowsecurity and relation.relforcerowsecurity
                        and owner.rolname = 'yo4x_migrator',
                    function.prosecdef
                        and function_owner.rolname = 'yo4x_migrator'
                        and function.proconfig @> array['search_path=""']::text[],
                    has_function_privilege(
                        'yo4x_worker', function.oid, 'EXECUTE')
                        and not has_function_privilege(
                            'yo4x_control_api', function.oid, 'EXECUTE'),
                    not exists
                    (
                        select 1
                        from pg_catalog.aclexplode(function.proacl) as privilege
                        where privilege.grantee = 0
                    )
                from pg_catalog.pg_class as relation
                join pg_catalog.pg_roles as owner on owner.oid = relation.relowner
                cross join pg_catalog.pg_proc as function
                join pg_catalog.pg_roles as function_owner
                  on function_owner.oid = function.proowner
                where relation.oid =
                    'control.user_operation_backlog_observations'::regclass
                  and function.oid =
                    'control.refresh_user_operation_backlog_observation()'::regprocedure
                """,
                administrator);
            await using NpgsqlDataReader reader = await boundary.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            for (int index = 0; index < reader.FieldCount; index++)
            {
                Assert.True(reader.GetBoolean(index), $"Backlog boundary assertion {index} failed.");
            }

            Assert.False(await reader.ReadAsync());
            await reader.DisposeAsync();

            await using var forge = new NpgsqlCommand(
                """
                update control.user_operation_backlog_observations
                set last_checked_at = clock_timestamp(),
                    refresh_count = refresh_count + 1,
                    row_version = row_version + 1
                where tenant_id = @tenant_id
                """,
                administrator);
            AddUuid(forge, "tenant_id", first.TenantId);
            PostgresException triggerDenied = await Assert.ThrowsAsync<PostgresException>(
                () => forge.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, triggerDenied.SqlState);
        }

        Assert.Equal(newest, await ReadBacklogAsync(database, first.TenantId));
        Assert.Equal(
            ("accepted", 0L),
            await ReadOperationStateAsync(database, first.OperationId));
    }

    [PostgresFact]
    public async Task BacklogRefreshWaitsForUncommittedOlderOperationBeforeObserving()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        BacklogFixture fixture = await SeedEligibleUserOperationAsync(database, UtcNow());
        DateTimeOffset olderCreatedAt = UtcNow().AddMinutes(-30);
        Guid olderOperationId = Guid.CreateVersion7();

        var writerContext = new TenantExecutionContext(
            fixture.TenantId,
            fixture.UserId,
            Guid.CreateVersion7(),
            fixture.SessionId);
        await using TenantPostgresTransaction writer =
            await database.Application.BeginTenantTransactionAsync(
                writerContext);
        await using (NpgsqlCommand insert = writer.CreateCommand(
            """
            insert into control.idempotency_records
                (id, tenant_id, actor_id, operation, idempotency_key,
                 request_sha256, state, created_at, expires_at)
            values
                (@idempotency_id, @tenant_id, @user_id,
                 'deployment.close_only', @idempotency_key,
                 @request_sha256, 'processing', @created_at,
                 clock_timestamp() + interval '1 hour');
            insert into control.user_operations
                (id, tenant_id, user_id, session_family_id, operation_type,
                 target_type, target_id, state, idempotency_record_id,
                 submitted_resource_version, requested_target_state, reason,
                 correlation_id, row_version, created_at, updated_at)
            values
                (@operation_id, @tenant_id, @user_id, @session_id,
                 'deployment.close_only', 'deployment', @target_id, 'accepted',
                 @idempotency_id, 0, 'close_only', 'held older backlog work',
                 @correlation_id, 0, @created_at, @created_at)
            """))
        {
            AddUuid(insert, "idempotency_id", Guid.CreateVersion7());
            AddUuid(insert, "tenant_id", fixture.TenantId);
            AddUuid(insert, "user_id", fixture.UserId);
            AddText(insert, "idempotency_key", $"held-{olderOperationId:N}");
            AddText(insert, "request_sha256", Digest("held-older-backlog"));
            AddTimestamp(insert, "created_at", olderCreatedAt);
            AddUuid(insert, "operation_id", olderOperationId);
            AddUuid(insert, "session_id", fixture.SessionId);
            AddUuid(insert, "target_id", Guid.CreateVersion7());
            AddUuid(insert, "correlation_id", writerContext.CorrelationId);
            Assert.Equal(2, await insert.ExecuteNonQueryAsync());
        }

        await using TenantPostgresTransaction observer =
            await database.Worker.BeginTenantTransactionAsync(
                PostgresWorkerTenantCatalog.CreateContext(
                    fixture.TenantId,
                    Guid.CreateVersion7()));
        int observerBackendPid;
        await using (NpgsqlCommand pid = observer.CreateCommand("select pg_backend_pid()"))
        {
            observerBackendPid = Convert.ToInt32(
                await pid.ExecuteScalarAsync(),
                System.Globalization.CultureInfo.InvariantCulture);
        }

        await using NpgsqlCommand refresh = observer.CreateCommand(
            "select * from control.refresh_user_operation_backlog_observation()");
        Task<NpgsqlDataReader> pendingRefresh = refresh.ExecuteReaderAsync();
        await WaitForAdvisoryLockWaitAsync(database, observerBackendPid);
        Assert.False(pendingRefresh.IsCompleted);

        await writer.CommitAsync();
        await using (NpgsqlDataReader reader = await pendingRefresh.WaitAsync(
            TimeSpan.FromSeconds(5)))
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal(fixture.TenantId, reader.GetGuid(0));
            Assert.Equal(olderCreatedAt, reader.GetFieldValue<DateTimeOffset>(2));
            Assert.False(await reader.ReadAsync());
        }

        await observer.CommitAsync();
    }

    [PostgresFact]
    public async Task BrokerTargetSnapshotUsesFreshDatabaseClockAfterAuthorityLockWait()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        VerificationFixture fixture = await SeedVerifiedStrategyAsync(database);
        await RecordVerificationAsync(database, fixture);
        await PromoteAsync(database, fixture);
        _ = await SeedRuntimeAuthorityAsync(database, fixture, desiredState: "running");
        DateTimeOffset leaseExpiresAt = await SetAssignmentExpiryForFreshClockTestAsync(
            database,
            fixture,
            TimeSpan.FromSeconds(3));

        await using TenantPostgresTransaction blocker =
            await database.Worker.BeginTenantTransactionAsync(
                PostgresWorkerTenantCatalog.CreateContext(
                    fixture.TenantId,
                    Guid.CreateVersion7()));
        await using (NpgsqlCommand acquire = blocker.CreateCommand(
            "select control.acquire_u0_authority_lock()"))
        {
            await acquire.ExecuteNonQueryAsync();
        }

        await using TenantPostgresTransaction observer =
            await database.Worker.BeginTenantTransactionAsync(
                PostgresWorkerTenantCatalog.CreateContext(
                    fixture.TenantId,
                    Guid.CreateVersion7()));
        int observerBackendPid;
        DateTimeOffset observerTransactionStartedAt;
        await using (NpgsqlCommand transactionClock = observer.CreateCommand(
            "select pg_backend_pid(), transaction_timestamp()"))
        await using (NpgsqlDataReader reader = await transactionClock.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            observerBackendPid = reader.GetInt32(0);
            observerTransactionStartedAt = reader.GetFieldValue<DateTimeOffset>(1);
            Assert.False(await reader.ReadAsync());
        }

        Assert.True(observerTransactionStartedAt < leaseExpiresAt);
        await using NpgsqlCommand blockedAcquire = observer.CreateCommand(
            "select control.acquire_u0_authority_lock()");
        Task<int> pendingAcquire = blockedAcquire.ExecuteNonQueryAsync();
        await WaitForAdvisoryLockWaitAsync(database, observerBackendPid);
        Assert.False(pendingAcquire.IsCompleted);

        await WaitForDatabaseClockAsync(database, leaseExpiresAt);
        await blocker.CommitAsync();
        _ = await pendingAcquire.WaitAsync(TimeSpan.FromSeconds(5));

        await using NpgsqlCommand snapshot = observer.CreateCommand(
            PostgresUserOperationWorkStore.BrokerTargetSnapshotSql);
        AddUuid(snapshot, "tenant_id", fixture.TenantId);
        AddUuid(snapshot, "target_id", fixture.BrokerAccountId);
        snapshot.Parameters.Add("dispatch_route_deployment_id", NpgsqlDbType.Uuid)
            .Value = DBNull.Value;
        snapshot.Parameters.Add("dispatch_fence_generation", NpgsqlDbType.Bigint)
            .Value = DBNull.Value;
        snapshot.Parameters.Add("dispatch_worker_assignment_id", NpgsqlDbType.Uuid)
            .Value = DBNull.Value;
        snapshot.Parameters.Add("dispatch_worker_instance_id", NpgsqlDbType.Uuid)
            .Value = DBNull.Value;
        snapshot.Parameters.AddWithValue(
            "minimum_route_lifetime",
            NpgsqlDbType.Interval,
            TimeSpan.Zero);
        await using (NpgsqlDataReader reader = await snapshot.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal("active", reader.GetString(1));
            Assert.Equal("ready", reader.GetString(2));
            Assert.True(reader.IsDBNull(5));
            Assert.True(reader.IsDBNull(6));
            Assert.True(reader.IsDBNull(7));
            Assert.True(reader.IsDBNull(8));
            Assert.False(await reader.ReadAsync());
        }

        await observer.CommitAsync();
    }

    [PostgresFact]
    public async Task UserOperationDeferralIsDatabaseClockedTenantPrivateAndUnforgeable()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        DateTimeOffset oldCreatedAt = UtcNow().AddMinutes(-20);
        BacklogFixture fixture = await SeedEligibleUserOperationAsync(database, oldCreatedAt);
        Guid claimToken = Guid.CreateVersion7();

        await using (NpgsqlConnection rawWorker = new(database.WorkerConnectionString))
        {
            await rawWorker.OpenAsync();
            await using var noContext = new NpgsqlCommand(
                "select * from control.defer_user_operation(" +
                "@operation_id, @claim_token, 1, 'dispatching', 'route_not_ready')",
                rawWorker);
            AddUuid(noContext, "operation_id", fixture.OperationId);
            AddUuid(noContext, "claim_token", claimToken);
            PostgresException noContextDenied = await Assert.ThrowsAsync<PostgresException>(
                () => noContext.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, noContextDenied.SqlState);

            await using var rawForge = new NpgsqlCommand(
                "update control.user_operations set next_processing_at = clock_timestamp()",
                rawWorker);
            PostgresException rawUpdateDenied = await Assert.ThrowsAsync<PostgresException>(
                () => rawForge.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, rawUpdateDenied.SqlState);
        }

        ProcessingDeferral deferral;
        await using (TenantPostgresTransaction transaction =
            await database.Worker.BeginTenantTransactionAsync(
                PostgresWorkerTenantCatalog.CreateContext(
                    fixture.TenantId,
                    Guid.CreateVersion7())))
        {
            await using NpgsqlCommand claim = transaction.CreateCommand(
                """
                update control.user_operations
                set state = 'dispatching',
                    claimed_by = 'deferral-test-worker',
                    claim_token = @claim_token,
                    claim_expires_at = clock_timestamp() + interval '30 seconds',
                    row_version = row_version + 1,
                    updated_at = clock_timestamp()
                where tenant_id = @tenant_id
                  and id = @operation_id
                  and state = 'accepted'
                  and row_version = 0
                returning row_version, next_processing_at,
                    processing_deferral_count, last_processing_error_code
                """);
            AddUuid(claim, "tenant_id", fixture.TenantId);
            AddUuid(claim, "operation_id", fixture.OperationId);
            AddUuid(claim, "claim_token", claimToken);
            await using (NpgsqlDataReader claimReader = await claim.ExecuteReaderAsync())
            {
                Assert.True(await claimReader.ReadAsync());
                Assert.Equal(1L, claimReader.GetInt64(0));
                Assert.True(claimReader.IsDBNull(1));
                Assert.Equal(0L, claimReader.GetInt64(2));
                Assert.True(claimReader.IsDBNull(3));
                Assert.False(await claimReader.ReadAsync());
            }

            await using NpgsqlCommand defer = transaction.CreateCommand(
                """
                select row_version, deferred_at, next_processing_at,
                    processing_deferral_count
                from control.defer_user_operation(
                    @operation_id, @claim_token, 1,
                    'dispatching', 'route_not_ready')
                """);
            AddUuid(defer, "operation_id", fixture.OperationId);
            AddUuid(defer, "claim_token", claimToken);
            await using (NpgsqlDataReader reader = await defer.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                deferral = new ProcessingDeferral(
                    reader.GetInt64(0),
                    reader.GetFieldValue<DateTimeOffset>(1),
                    reader.GetFieldValue<DateTimeOffset>(2),
                    reader.GetInt64(3));
                Assert.False(await reader.ReadAsync());
            }

            await transaction.CommitAsync();
        }

        Assert.Equal(2L, deferral.RowVersion);
        Assert.Equal(1L, deferral.ProcessingDeferralCount);
        Assert.Equal(TimeSpan.FromSeconds(1), deferral.NextProcessingAt - deferral.DeferredAt);

        await using (TenantPostgresTransaction notDue =
            await database.Worker.BeginTenantTransactionAsync(
                PostgresWorkerTenantCatalog.CreateContext(
                    fixture.TenantId,
                    Guid.CreateVersion7())))
        {
            await using NpgsqlCommand candidate = notDue.CreateCommand(
                """
                select count(*)
                from control.user_operations
                where tenant_id = @tenant_id
                  and id = @operation_id
                  and (next_processing_at is null
                      or next_processing_at <= clock_timestamp())
                """);
            AddUuid(candidate, "tenant_id", fixture.TenantId);
            AddUuid(candidate, "operation_id", fixture.OperationId);
            Assert.Equal(0L, (long)(await candidate.ExecuteScalarAsync())!);

            Guid earlyToken = Guid.CreateVersion7();
            await using NpgsqlCommand earlyClaim = notDue.CreateCommand(
                """
                update control.user_operations
                set claimed_by = 'early-claim', claim_token = @claim_token,
                    claim_expires_at = clock_timestamp() + interval '30 seconds',
                    row_version = row_version + 1,
                    updated_at = clock_timestamp()
                where tenant_id = @tenant_id and id = @operation_id
                """);
            AddUuid(earlyClaim, "tenant_id", fixture.TenantId);
            AddUuid(earlyClaim, "operation_id", fixture.OperationId);
            AddUuid(earlyClaim, "claim_token", earlyToken);
            PostgresException earlyDenied = await Assert.ThrowsAsync<PostgresException>(
                () => earlyClaim.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.ObjectNotInPrerequisiteState, earlyDenied.SqlState);
        }

        BacklogObservation observed = await RefreshBacklogAsync(database, fixture.TenantId);
        Assert.Equal(oldCreatedAt, observed.OldestOpenCreatedAt);

        Guid otherTenantId = Guid.CreateVersion7();
        await SeedCursorTenantAsync(database, otherTenantId);
        await using (TenantPostgresTransaction otherTenant =
            await database.Worker.BeginTenantTransactionAsync(
                PostgresWorkerTenantCatalog.CreateContext(
                    otherTenantId,
                    Guid.CreateVersion7())))
        {
            await using NpgsqlCommand crossTenant = otherTenant.CreateCommand(
                """
                select *
                from control.defer_user_operation(
                    @operation_id, @claim_token, 1,
                    'dispatching', 'route_not_ready')
                """);
            AddUuid(crossTenant, "operation_id", fixture.OperationId);
            AddUuid(crossTenant, "claim_token", claimToken);
            await using NpgsqlDataReader reader = await crossTenant.ExecuteReaderAsync();
            Assert.False(await reader.ReadAsync());
            await reader.DisposeAsync();
            await otherTenant.CommitAsync();
        }

        await using (NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync())
        {
            await using var exact = new NpgsqlCommand(
                """
                select state, claimed_by, claim_token, claim_expires_at,
                    next_processing_at, processing_deferral_count,
                    last_processing_error_code, row_version
                from control.user_operations
                where id = @operation_id
                """,
                administrator);
            AddUuid(exact, "operation_id", fixture.OperationId);
            await using (NpgsqlDataReader reader = await exact.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal("dispatching", reader.GetString(0));
                Assert.True(reader.IsDBNull(1));
                Assert.True(reader.IsDBNull(2));
                Assert.True(reader.IsDBNull(3));
                Assert.Equal(deferral.NextProcessingAt, reader.GetFieldValue<DateTimeOffset>(4));
                Assert.Equal(1L, reader.GetInt64(5));
                Assert.Equal("route_not_ready", reader.GetString(6));
                Assert.Equal(2L, reader.GetInt64(7));
                Assert.False(await reader.ReadAsync());
            }

            await using var forge = new NpgsqlCommand(
                """
                update control.user_operations
                set next_processing_at = clock_timestamp() + interval '1 hour',
                    processing_deferral_count = processing_deferral_count + 1,
                    last_processing_error_code = 'forged',
                    row_version = row_version + 1,
                    updated_at = clock_timestamp()
                where id = @operation_id
                """,
                administrator);
            AddUuid(forge, "operation_id", fixture.OperationId);
            PostgresException forgeDenied = await Assert.ThrowsAsync<PostgresException>(
                () => forge.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, forgeDenied.SqlState);
        }
    }

    [PostgresFact]
    public async Task UserOperationDeferralCounterSaturatesWithoutLivenessLoss()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        BacklogFixture fixture = await SeedEligibleUserOperationAsync(database, UtcNow());
        const long MaximumCounter = long.MaxValue;

        await SetDeferralEvidenceForBoundaryTestAsync(
            database,
            fixture.OperationId,
            MaximumCounter - 1,
            nextProcessingAtIsDue: false);
        ProcessingDeferral reachesMaximum = await ClaimAndDeferAsync(
            database,
            fixture,
            expectedRowVersion: 0);
        Assert.Equal(MaximumCounter, reachesMaximum.ProcessingDeferralCount);
        Assert.Equal(2L, reachesMaximum.RowVersion);
        Assert.Equal(
            TimeSpan.FromSeconds(60),
            reachesMaximum.NextProcessingAt - reachesMaximum.DeferredAt);

        // Move only the DB-owned due instant in this disposable boundary fixture
        // to model the sixty-second wait without introducing a wall-clock sleep.
        await SetDeferralEvidenceForBoundaryTestAsync(
            database,
            fixture.OperationId,
            MaximumCounter,
            nextProcessingAtIsDue: true);
        ProcessingDeferral saturated = await ClaimAndDeferAsync(
            database,
            fixture,
            expectedRowVersion: 2);
        Assert.Equal(MaximumCounter, saturated.ProcessingDeferralCount);
        Assert.Equal(4L, saturated.RowVersion);
        Assert.Equal(
            TimeSpan.FromSeconds(60),
            saturated.NextProcessingAt - saturated.DeferredAt);
    }

    private static async Task AssertWorkerReadyEventuallyAsync(
        PostgresWorkerReadiness readiness)
    {
        // The bounded probe fails closed on its own fixed five-second deadline,
        // so a cold catalog/fingerprint enumeration on a freshly created local
        // cluster can report false before warming. Poll until the dependency is
        // genuinely ready instead of asserting a single cold attempt.
        DateTimeOffset deadline = UtcNow().AddSeconds(90);
        while (true)
        {
            if (await readiness.IsReadyAsync(CancellationToken.None))
            {
                return;
            }

            if (UtcNow() >= deadline)
            {
                Assert.Fail("The worker readiness probe did not become ready within 90 seconds.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
    }

    private static async Task SeedCursorTenantAsync(
        PostgresTestDatabase database,
        Guid tenantId)
    {
        var context = new TenantExecutionContext(
            tenantId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7());
        await using TenantPostgresTransaction transaction =
            await database.Application.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into identity.tenants (id, slug, display_name)
            values (@tenant_id, @slug, 'Durable cursor tenant')
            """);
        AddUuid(command, "tenant_id", tenantId);
        AddText(command, "slug", $"cursor-{tenantId:N}");
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    private static async Task<ProcessingDeferral> ClaimAndDeferAsync(
        PostgresTestDatabase database,
        BacklogFixture fixture,
        long expectedRowVersion)
    {
        Guid claimToken = Guid.CreateVersion7();
        await using TenantPostgresTransaction transaction =
            await database.Worker.BeginTenantTransactionAsync(
                PostgresWorkerTenantCatalog.CreateContext(
                    fixture.TenantId,
                    Guid.CreateVersion7()));
        await using NpgsqlCommand claim = transaction.CreateCommand(
            """
            update control.user_operations
            set state = 'dispatching', claimed_by = 'saturation-test-worker',
                claim_token = @claim_token,
                claim_expires_at = clock_timestamp() + interval '30 seconds',
                row_version = row_version + 1,
                updated_at = clock_timestamp()
            where tenant_id = @tenant_id and id = @operation_id
              and row_version = @expected_version
              and state in ('accepted', 'dispatching')
            returning row_version
            """);
        AddUuid(claim, "tenant_id", fixture.TenantId);
        AddUuid(claim, "operation_id", fixture.OperationId);
        AddUuid(claim, "claim_token", claimToken);
        claim.Parameters.AddWithValue(
            "expected_version",
            NpgsqlDbType.Bigint,
            expectedRowVersion);
        long claimedVersion = (long)(await claim.ExecuteScalarAsync())!;
        Assert.Equal(expectedRowVersion + 1, claimedVersion);

        await using NpgsqlCommand defer = transaction.CreateCommand(
            """
            select row_version, deferred_at, next_processing_at,
                processing_deferral_count
            from control.defer_user_operation(
                @operation_id, @claim_token, @expected_version,
                'dispatching', 'saturation_test')
            """);
        AddUuid(defer, "operation_id", fixture.OperationId);
        AddUuid(defer, "claim_token", claimToken);
        defer.Parameters.AddWithValue(
            "expected_version",
            NpgsqlDbType.Bigint,
            claimedVersion);
        ProcessingDeferral result;
        await using (NpgsqlDataReader reader = await defer.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            result = new ProcessingDeferral(
                reader.GetInt64(0),
                reader.GetFieldValue<DateTimeOffset>(1),
                reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetInt64(3));
            Assert.False(await reader.ReadAsync());
        }

        await transaction.CommitAsync();
        return result;
    }

    private static async Task SetDeferralEvidenceForBoundaryTestAsync(
        PostgresTestDatabase database,
        Guid operationId,
        long deferralCount,
        bool nextProcessingAtIsDue)
    {
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using NpgsqlTransaction transaction = await administrator.BeginTransactionAsync();
        await using (var replica = new NpgsqlCommand(
            "set local session_replication_role = 'replica'",
            administrator,
            transaction))
        {
            await replica.ExecuteNonQueryAsync();
        }

        await using (var command = new NpgsqlCommand(
            """
            update control.user_operations
            set processing_deferral_count = @deferral_count,
                next_processing_at = case when @is_due
                    then greatest(
                        created_at,
                        clock_timestamp() - interval '1 microsecond')
                    else next_processing_at end
            where id = @operation_id
            """,
            administrator,
            transaction))
        {
            AddUuid(command, "operation_id", operationId);
            command.Parameters.AddWithValue(
                "deferral_count",
                NpgsqlDbType.Bigint,
                deferralCount);
            command.Parameters.AddWithValue("is_due", NpgsqlDbType.Boolean, nextProcessingAtIsDue);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        await transaction.CommitAsync();
    }

    private static async Task SeedCursorOutboxAsync(
        PostgresTestDatabase database,
        Guid tenantId)
    {
        TenantExecutionContext context = PostgresWorkerTenantCatalog.CreateContext(
            tenantId,
            Guid.CreateVersion7());
        await using TenantPostgresTransaction transaction =
            await database.Application.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into messaging.outbox_messages
                (id, tenant_id, message_type, aggregate_type, aggregate_id,
                 payload, payload_sha256, correlation_id, occurred_at, available_at)
            values
                (@id, @tenant_id, 'cursor.test.v1', 'cursor', @aggregate_id,
                 '{}'::jsonb, @payload_sha256, @correlation_id,
                 @occurred_at, @occurred_at)
            """);
        Guid messageId = Guid.CreateVersion7();
        AddUuid(command, "id", messageId);
        AddUuid(command, "tenant_id", tenantId);
        AddText(command, "aggregate_id", messageId.ToString("D"));
        AddText(command, "payload_sha256", Digest("{}"));
        AddUuid(command, "correlation_id", context.CorrelationId);
        AddTimestamp(command, "occurred_at", UtcNow());
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    private static async Task<BacklogFixture> SeedEligibleUserOperationAsync(
        PostgresTestDatabase database,
        DateTimeOffset createdAt)
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();
        Guid sessionId = Guid.CreateVersion7();
        Guid idempotencyId = Guid.CreateVersion7();
        Guid operationId = Guid.CreateVersion7();
        var context = new TenantExecutionContext(
            tenantId,
            userId,
            Guid.CreateVersion7(),
            sessionId);
        await using TenantPostgresTransaction transaction =
            await database.Application.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into identity.tenants (id, slug, display_name)
            values (@tenant_id, @slug, 'Backlog observation tenant');
            insert into identity.user_identities
                (id, tenant_id, normalized_email, security_state,
                 email_verified_at, created_at, updated_at)
            values
                (@user_id, @tenant_id, @email, 'active',
                 @created_at, @created_at, @created_at);
            insert into identity.user_session_families
                (id, tenant_id, user_id, device_id, current_token_hash, state,
                 expires_at, created_at, updated_at)
            values
                (@session_id, @tenant_id, @user_id, @device_id,
                 @token_hash, 'active', clock_timestamp() + interval '1 hour',
                 @created_at, @created_at);
            insert into control.idempotency_records
                (id, tenant_id, actor_id, operation, idempotency_key,
                 request_sha256, state, created_at, expires_at)
            values
                (@idempotency_id, @tenant_id, @user_id,
                 'deployment.close_only', @idempotency_key,
                 @request_sha256, 'processing', @created_at,
                 clock_timestamp() + interval '1 hour');
            insert into control.user_operations
                (id, tenant_id, user_id, session_family_id, operation_type,
                 target_type, target_id, state, idempotency_record_id,
                 submitted_resource_version, requested_target_state, reason,
                 correlation_id, row_version, created_at, updated_at)
            values
                (@operation_id, @tenant_id, @user_id, @session_id,
                 'deployment.close_only', 'deployment', @target_id, 'accepted',
                 @idempotency_id, 0, 'close_only', 'backlog boundary test',
                 @correlation_id, 0, @created_at, @created_at)
            """);
        AddUuid(command, "tenant_id", tenantId);
        AddUuid(command, "user_id", userId);
        AddUuid(command, "session_id", sessionId);
        AddUuid(command, "idempotency_id", idempotencyId);
        AddUuid(command, "operation_id", operationId);
        AddUuid(command, "target_id", Guid.CreateVersion7());
        AddUuid(command, "correlation_id", context.CorrelationId);
        AddText(command, "slug", $"backlog-{tenantId:N}");
        AddText(command, "email", $"backlog-{userId:N}@example.test");
        AddUuid(command, "device_id", Guid.CreateVersion7());
        AddText(command, "token_hash", Digest("backlog-session-token"));
        AddText(command, "idempotency_key", $"backlog-{operationId:N}");
        AddText(command, "request_sha256", Digest("backlog-request"));
        AddTimestamp(command, "created_at", createdAt);
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return new BacklogFixture(tenantId, userId, sessionId, operationId);
    }

    private static async Task WaitForAdvisoryLockWaitAsync(
        PostgresTestDatabase database,
        int backendPid)
    {
        await using NpgsqlConnection monitor =
            await database.Administrator.OpenConnectionAsync();
        DateTimeOffset deadline = UtcNow().AddSeconds(5);
        while (UtcNow() < deadline)
        {
            await using var command = new NpgsqlCommand(
                """
                select exists
                (
                    select 1
                    from pg_catalog.pg_locks
                    where pid = @pid
                      and locktype = 'advisory'
                      and not granted
                )
                """,
                monitor);
            command.Parameters.AddWithValue("pid", NpgsqlDbType.Integer, backendPid);
            if (Assert.IsType<bool>(await command.ExecuteScalarAsync()))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        throw new TimeoutException("The backlog refresh did not wait on tenant U0 authority.");
    }

    private static async Task<DateTimeOffset> SetAssignmentExpiryForFreshClockTestAsync(
        PostgresTestDatabase database,
        VerificationFixture fixture,
        TimeSpan lifetime)
    {
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using NpgsqlTransaction transaction = await administrator.BeginTransactionAsync();
        await using (var replica = new NpgsqlCommand(
            "set local session_replication_role = 'replica'",
            administrator,
            transaction))
        {
            await replica.ExecuteNonQueryAsync();
        }

        await using var update = new NpgsqlCommand(
            """
            update operations.worker_assignments
            set state = 'active', revoked_at = null,
                lease_expires_at = clock_timestamp() + @lifetime
            where tenant_id = @tenant_id and id = @assignment_id
            returning lease_expires_at
            """,
            administrator,
            transaction);
        AddUuid(update, "tenant_id", fixture.TenantId);
        AddUuid(update, "assignment_id", fixture.WorkerAssignmentId);
        update.Parameters.AddWithValue("lifetime", NpgsqlDbType.Interval, lifetime);
        DateTimeOffset expiresAt = ReadDatabaseInstant(
            await update.ExecuteScalarAsync());
        await transaction.CommitAsync();
        return expiresAt;
    }

    private static async Task WaitForDatabaseClockAsync(
        PostgresTestDatabase database,
        DateTimeOffset threshold)
    {
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        DateTimeOffset deadline = UtcNow().AddSeconds(10);
        while (UtcNow() < deadline)
        {
            await using var command = new NpgsqlCommand(
                "select clock_timestamp()",
                administrator);
            DateTimeOffset current = ReadDatabaseInstant(
                await command.ExecuteScalarAsync());
            if (current > threshold)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        throw new TimeoutException("The database clock did not advance past the assignment lease.");
    }

    private static DateTimeOffset ReadDatabaseInstant(object? value) => value switch
    {
        DateTimeOffset instant => instant.ToUniversalTime(),
        DateTime instant => new DateTimeOffset(
            DateTime.SpecifyKind(instant, DateTimeKind.Utc)),
        _ => throw new InvalidOperationException(
            "PostgreSQL returned an invalid timestamp value.")
    };

    private static async Task<BacklogObservation> RefreshBacklogAsync(
        PostgresTestDatabase database,
        Guid tenantId)
    {
        await using TenantPostgresTransaction transaction =
            await database.Worker.BeginTenantTransactionAsync(
                PostgresWorkerTenantCatalog.CreateContext(
                    tenantId,
                    Guid.CreateVersion7()));
        await using NpgsqlCommand command = transaction.CreateCommand(
            "select * from control.refresh_user_operation_backlog_observation()");
        BacklogObservation observation;
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            observation = new BacklogObservation(
                reader.GetGuid(0),
                reader.GetFieldValue<DateTimeOffset>(1),
                reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetInt64(3),
                reader.GetInt64(4));
            Assert.False(await reader.ReadAsync());
        }

        await transaction.CommitAsync();
        return observation;
    }

    private static async Task<BacklogObservation> ReadBacklogAsync(
        PostgresTestDatabase database,
        Guid tenantId)
    {
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select tenant_id, last_checked_at, oldest_open_created_at,
                refresh_count, row_version
            from control.user_operation_backlog_observations
            where tenant_id = @tenant_id
            """,
            administrator);
        AddUuid(command, "tenant_id", tenantId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var observation = new BacklogObservation(
            reader.GetGuid(0),
            reader.GetFieldValue<DateTimeOffset>(1),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
            reader.GetInt64(3),
            reader.GetInt64(4));
        Assert.False(await reader.ReadAsync());
        return observation;
    }

    private static async Task<(string State, long RowVersion)> ReadOperationStateAsync(
        PostgresTestDatabase database,
        Guid operationId)
    {
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select state, row_version
            from control.user_operations
            where id = @operation_id
            """,
            administrator);
        AddUuid(command, "operation_id", operationId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var result = (reader.GetString(0), reader.GetInt64(1));
        Assert.False(await reader.ReadAsync());
        return result;
    }

    private static async Task<CursorProgress> AdvanceEmptyGlobalCursorAsync(
        PostgresTestDatabase database,
        string consumer)
    {
        await using NpgsqlConnection worker = new(database.WorkerConnectionString);
        await worker.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            update control.worker_tenant_scan_cursors
            set last_tenant_id = last_tenant_id
            where consumer = @consumer
            returning last_tenant_id, last_scan_at, last_advanced_at,
                last_rotation_completed_at, rotation_count, row_version
            """,
            worker);
        AddText(command, "consumer", consumer);
        return await ReadSingleCursorAsync(command);
    }

    private static async Task<CursorProgress> AdvanceGlobalCursorAsync(
        PostgresTestDatabase database,
        string consumer)
    {
        await using NpgsqlConnection worker = new(database.WorkerConnectionString);
        await worker.OpenAsync();
        await using var command = new NpgsqlCommand(AdvanceGlobalCursorSql, worker);
        AddText(command, "consumer", consumer);
        return await ReadSingleCursorAsync(command);
    }

    private static async Task<CursorProgress> ReadGlobalCursorAsync(
        PostgresTestDatabase database,
        string consumer)
    {
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select last_tenant_id, last_scan_at, last_advanced_at,
                last_rotation_completed_at, rotation_count, row_version
            from control.worker_tenant_scan_cursors
            where consumer = @consumer
            """,
            administrator);
        AddText(command, "consumer", consumer);
        return await ReadSingleCursorAsync(command);
    }

    private static async Task<Guid> ReadLastTenantIdAsync(PostgresTestDatabase database)
    {
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "select id from identity.tenants order by id desc limit 1",
            administrator);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<bool> ProbeGlobalCeilingAsync(
        PostgresTestDatabase database,
        string consumer,
        long rotationCeiling)
    {
        await using NpgsqlConnection worker = new(database.WorkerConnectionString);
        await worker.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            with locked_cursor as materialized
            (
                select last_tenant_id, rotation_count
                from control.worker_tenant_scan_cursors
                where consumer = @consumer
                for update
            ),
            candidate as materialized
            (
                select tenant.id,
                    locked_cursor.rotation_count
                        + case
                            when locked_cursor.last_tenant_id is not null
                                and tenant.id <= locked_cursor.last_tenant_id
                            then 1 else 0
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
                            then 1 else 0
                        end,
                        id
                    limit 1
                ) as tenant
            ),
            eligible as materialized
            (
                select id
                from candidate
                where next_rotation_count <= @rotation_ceiling
            ),
            catalog_state as materialized
            (
                select not exists (select 1 from identity.tenants) as is_empty
            )
            update control.worker_tenant_scan_cursors as progress
            set last_tenant_id = coalesce(eligible.id, progress.last_tenant_id)
            from catalog_state
            left join eligible on true
            where progress.consumer = @consumer
              and (eligible.id is not null or catalog_state.is_empty)
            returning true
            """,
            worker);
        AddText(command, "consumer", consumer);
        command.Parameters.AddWithValue(
            "rotation_ceiling",
            NpgsqlDbType.Bigint,
            rotationCeiling);
        return await command.ExecuteScalarAsync() is true;
    }

    private static async Task AssertGlobalCursorForgeryRejectedAsync(
        PostgresTestDatabase database,
        CursorProgress expected)
    {
        await using (NpgsqlConnection worker = new(database.WorkerConnectionString))
        {
            await worker.OpenAsync();
            string[] forbidden =
            [
                "update control.worker_tenant_scan_cursors "
                    + "set rotation_count = rotation_count + 1 where consumer = 'outbox'",
                "insert into control.worker_tenant_scan_cursors (consumer) values ('outbox')",
                "delete from control.worker_tenant_scan_cursors where consumer = 'outbox'"
            ];
            foreach (string sql in forbidden)
            {
                await using var command = new NpgsqlCommand(sql, worker);
                PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
                    () => command.ExecuteNonQueryAsync());
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, rejected.SqlState);
            }
        }

        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using var forged = new NpgsqlCommand(
            """
            update control.worker_tenant_scan_cursors
            set last_scan_at = clock_timestamp()
            where consumer = 'outbox'
            """,
            administrator);
        PostgresException triggerRejected = await Assert.ThrowsAsync<PostgresException>(
            () => forged.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, triggerRejected.SqlState);
        Assert.Equal(expected, await ReadGlobalCursorAsync(database, "outbox"));
    }

    private static async Task InitializeDeploymentCursorAsync(
        PostgresTestDatabase database,
        Guid tenantId)
    {
        await using TenantPostgresTransaction transaction =
            await database.Worker.BeginTenantTransactionAsync(
                PostgresWorkerTenantCatalog.CreateContext(
                    tenantId,
                    Guid.CreateVersion7()));
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into control.deployment_scan_cursors (tenant_id)
            values (@tenant_id)
            """);
        AddUuid(command, "tenant_id", tenantId);
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    private static async Task<CursorProgress> AdvanceEmptyDeploymentCursorAsync(
        PostgresTestDatabase database,
        Guid tenantId)
    {
        await using TenantPostgresTransaction transaction =
            await database.Worker.BeginTenantTransactionAsync(
                PostgresWorkerTenantCatalog.CreateContext(
                    tenantId,
                    Guid.CreateVersion7()));
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            update control.deployment_scan_cursors
            set last_deployment_id = last_deployment_id
            where tenant_id = @tenant_id
            returning last_deployment_id, last_scan_at, last_advanced_at,
                last_rotation_completed_at, rotation_count, row_version
            """);
        AddUuid(command, "tenant_id", tenantId);
        CursorProgress result = await ReadSingleCursorAsync(command);
        await transaction.CommitAsync();
        return result;
    }

    private static async Task AssertDeploymentCursorForgeryRejectedAsync(
        PostgresTestDatabase database,
        Guid tenantId)
    {
        await using (TenantPostgresTransaction transaction =
            await database.Worker.BeginTenantTransactionAsync(
                PostgresWorkerTenantCatalog.CreateContext(
                    tenantId,
                    Guid.CreateVersion7())))
        {
            await using NpgsqlCommand forge = transaction.CreateCommand(
                """
                update control.deployment_scan_cursors
                set rotation_count = rotation_count + 1
                where tenant_id = @tenant_id
                """);
            AddUuid(forge, "tenant_id", tenantId);
            PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
                () => forge.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, rejected.SqlState);
        }

        await using (TenantPostgresTransaction transaction =
            await database.Worker.BeginTenantTransactionAsync(
                PostgresWorkerTenantCatalog.CreateContext(
                    tenantId,
                    Guid.CreateVersion7())))
        {
            await using NpgsqlCommand wrong = transaction.CreateCommand(
                """
                update control.deployment_scan_cursors
                set last_deployment_id = @deployment_id
                where tenant_id = @tenant_id
                """);
            AddUuid(wrong, "tenant_id", tenantId);
            AddUuid(wrong, "deployment_id", Guid.CreateVersion7());
            PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
                () => wrong.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InvalidParameterValue, rejected.SqlState);
        }
    }

    private static async Task AssertDeploymentCursorCrossTenantMutationRejectedAsync(
        PostgresTestDatabase database,
        Guid activeTenantId,
        Guid otherTenantId)
    {
        await using TenantPostgresTransaction transaction =
            await database.Worker.BeginTenantTransactionAsync(
                PostgresWorkerTenantCatalog.CreateContext(
                    activeTenantId,
                    Guid.CreateVersion7()));
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            update control.deployment_scan_cursors
            set last_deployment_id = last_deployment_id
            where tenant_id = @other_tenant_id
            """);
        AddUuid(command, "other_tenant_id", otherTenantId);
        Assert.Equal(0, await command.ExecuteNonQueryAsync());
        await transaction.CommitAsync();

        await using TenantPostgresTransaction otherActor =
            await database.Application.BeginTenantTransactionAsync(
                new TenantExecutionContext(
                    otherTenantId,
                    Guid.CreateVersion7(),
                    Guid.CreateVersion7()));
        await using NpgsqlCommand hidden = otherActor.CreateCommand(
            """
            select count(*)
            from control.deployment_scan_cursors
            where tenant_id = @active_tenant_id
            """);
        AddUuid(hidden, "active_tenant_id", activeTenantId);
        Assert.Equal(0L, (long)(await hidden.ExecuteScalarAsync())!);
    }

    private static async Task<CursorProgress> ReadDeploymentCursorAsync(
        PostgresTestDatabase database,
        Guid tenantId)
    {
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select last_deployment_id, last_scan_at, last_advanced_at,
                last_rotation_completed_at, rotation_count, row_version
            from control.deployment_scan_cursors
            where tenant_id = @tenant_id
            """,
            administrator);
        AddUuid(command, "tenant_id", tenantId);
        return await ReadSingleCursorAsync(command);
    }

    private static async Task CompleteOneRotationAsync(
        PostgresTestDatabase database,
        WorkerTenantScanConsumer consumer,
        ControlWorkOptions options)
    {
        var catalog = new PostgresWorkerTenantCatalog(
            database.Worker,
            new PostgresWorkerReadiness(database.Worker),
            options);
        await using WorkerTenantScanLease lease = await catalog.BeginScanAsync(
            consumer,
            CancellationToken.None);
        while (await lease.TryBeginNextAsync(CancellationToken.None) is not null)
        {
        }
    }

    private static async Task CloneStoppedDeploymentsAsync(
        PostgresTestDatabase database,
        VerificationFixture fixture,
        Guid[] deploymentIds)
    {
        await using TenantPostgresTransaction transaction =
            await database.Application.BeginTenantTransactionAsync(fixture.UserContext);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into operations.deployments
            select (pg_catalog.jsonb_populate_record(
                null::operations.deployments,
                pg_catalog.to_jsonb(source)
                || pg_catalog.jsonb_build_object(
                    'id', requested.id,
                    'desired_state', 'stopped',
                    'observed_state', 'stopped',
                    'fence_generation', 0,
                    'lease_expires_at', null,
                    'last_reconciled_at', null,
                    'row_version', 0,
                    'created_at', clock_timestamp(),
                    'updated_at', clock_timestamp()))).*
            from pg_catalog.unnest(@deployment_ids::uuid[]) as requested(id)
            cross join lateral
            (
                select *
                from operations.deployments
                where tenant_id = @tenant_id and id = @source_deployment_id
            ) as source
            """);
        command.Parameters.AddWithValue(
            "deployment_ids",
            NpgsqlDbType.Array | NpgsqlDbType.Uuid,
            deploymentIds);
        AddUuid(command, "tenant_id", fixture.TenantId);
        AddUuid(command, "source_deployment_id", fixture.DeploymentId);
        Assert.Equal(deploymentIds.Length, await command.ExecuteNonQueryAsync());
        await transaction.CommitAsync();
    }

    private static async Task WaitForDeploymentCursorAsync(
        PostgresTestDatabase database,
        Guid tenantId,
        Guid deploymentId)
    {
        // The cycle creates the tenant-private cursor row on its first visit,
        // so absence is a normal pre-start state rather than a broken
        // invariant; only the durable advance to the raced deployment counts.
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using NpgsqlConnection administrator =
                await database.Administrator.OpenConnectionAsync();
            await using var command = new NpgsqlCommand(
                """
                select last_deployment_id
                from control.deployment_scan_cursors
                where tenant_id = @tenant_id
                """,
                administrator);
            AddUuid(command, "tenant_id", tenantId);
            object? advanced = await command.ExecuteScalarAsync();
            if (advanced is Guid cursorId && cursorId == deploymentId)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        throw new TimeoutException(
            "The deployment cursor did not durably advance before the race deadline.");
    }

    private static async Task<(long RowVersion, string ObservedState)>
        ReadDeploymentVersionAsync(
            PostgresTestDatabase database,
            VerificationFixture fixture)
    {
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select row_version, observed_state
            from operations.deployments
            where tenant_id = @tenant_id and id = @deployment_id
            """,
            administrator);
        AddUuid(command, "tenant_id", fixture.TenantId);
        AddUuid(command, "deployment_id", fixture.DeploymentId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var result = (reader.GetInt64(0), reader.GetString(1));
        Assert.False(await reader.ReadAsync());
        return result;
    }

    private static async Task<CursorProgress> ReadSingleCursorAsync(
        NpgsqlCommand command)
    {
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var progress = new CursorProgress(
            reader.IsDBNull(0) ? null : reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
            reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetInt64(4),
            reader.GetInt64(5));
        Assert.False(await reader.ReadAsync());
        return progress;
    }

    private sealed record CursorProgress(
        Guid? CursorId,
        DateTimeOffset? LastScanAt,
        DateTimeOffset? LastAdvancedAt,
        DateTimeOffset? LastRotationCompletedAt,
        long RotationCount,
        long RowVersion);

    private sealed record BacklogFixture(
        Guid TenantId,
        Guid UserId,
        Guid SessionId,
        Guid OperationId);

    private sealed record BacklogObservation(
        Guid TenantId,
        DateTimeOffset LastCheckedAt,
        DateTimeOffset? OldestOpenCreatedAt,
        long RefreshCount,
        long RowVersion);

    private sealed record ProcessingDeferral(
        long RowVersion,
        DateTimeOffset DeferredAt,
        DateTimeOffset NextProcessingAt,
        long ProcessingDeferralCount);
}
