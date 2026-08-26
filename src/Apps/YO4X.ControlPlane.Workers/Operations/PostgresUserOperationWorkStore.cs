using Npgsql;
using NpgsqlTypes;
using System.Text.Json;
using System.Text.Json.Nodes;
using YO4X.Audit;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Workers.Outbox;
using YO4X.Outbox;
using YO4X.Persistence.Postgres;
using YO4X.Runtime.Contracts;

namespace YO4X.ControlPlane.Workers.Operations;

internal sealed class PostgresUserOperationWorkStore(
    PostgresDatabase database,
    PostgresWorkerReadiness readiness,
    PostgresWorkerTenantCatalog tenantCatalog,
    ControlWorkOptions options,
    OutboxWorkerIdentity workerIdentity,
    WorkerPolicySignatureTrustStore policyTrustStore,
    TimeProvider timeProvider) : IUserOperationWorkStore
{
    private const string DispatchMessageAggregate = "user_operation";

    internal const string BrokerTargetSnapshotSql = """
        with authority_time as materialized
        (
            select clock_timestamp() as authorization_now
        )
        select
            account.row_version, account.state, account.credential_state,
            account.environment, account.binding_fingerprint,
            route.deployment_id, route.fence_generation,
            route.assignment_id, route.worker_node_id, route.lease_expires_at
        from authority_time
        cross join operations.broker_accounts as account
        left join lateral
        (
            select
                deployment.id as deployment_id,
                deployment.fence_generation,
                assignment.id as assignment_id,
                assignment.worker_node_id,
                assignment.lease_expires_at
            from operations.deployments as deployment
            join operations.worker_assignments as assignment
              on assignment.tenant_id = deployment.tenant_id
             and assignment.deployment_id = deployment.id
             and assignment.fence_generation = deployment.fence_generation
            where deployment.tenant_id = account.tenant_id
              and deployment.broker_account_id = account.id
              and (@dispatch_route_deployment_id is null
                  or deployment.id = @dispatch_route_deployment_id)
              and (@dispatch_fence_generation is null
                  or deployment.fence_generation = @dispatch_fence_generation)
              and (@dispatch_worker_assignment_id is null
                  or assignment.id = @dispatch_worker_assignment_id)
              and (@dispatch_worker_instance_id is null
                  or assignment.worker_node_id = @dispatch_worker_instance_id)
              and assignment.state = 'active'
              and assignment.revoked_at is null
              and assignment.lease_expires_at >
                  authority_time.authorization_now + @minimum_route_lifetime
            order by assignment.lease_expires_at desc, assignment.id
            limit 1
        ) as route on true
        where account.tenant_id = @tenant_id and account.id = @target_id
        """;

    private const string RefreshBacklogObservationSql = """
        select
            tenant_id,
            last_checked_at,
            oldest_open_created_at,
            refresh_count,
            row_version
        from control.refresh_user_operation_backlog_observation()
        """;

    private const string AdvanceInvocationTimeoutsSql = """
        select
            attempt_id,
            prior_state,
            next_state,
            state_version,
            receipt_id,
            occurred_at,
            reason_code
        from control.advance_user_operation_invocation_timeouts(@max_rows)
        """;

    public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        readiness.IsReadyAsync(cancellationToken);

    public async Task<ControlWorkCycleResult> RunCycleAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!await IsAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            return new ControlWorkCycleResult(0, 0, 0, 0, false, false);
        }

        // The scheduler supplies this for the shared worker interface only;
        // PostgreSQL owns every authorization, freshness, and lifecycle instant.
        _ = now.ToUniversalTime();
        await using WorkerTenantScanLease tenantScan = await tenantCatalog.BeginScanAsync(
                WorkerTenantScanConsumer.UserOperations,
                cancellationToken)
            .ConfigureAwait(false);
        int tenantsVisited = 0;
        int examined = 0;
        int changed = 0;
        int failed = 0;
        while (true)
        {
            WorkerTenantScanStep? step = await tenantScan.TryBeginNextAsync(cancellationToken)
                .ConfigureAwait(false);
            if (step is not { } tenantStep)
            {
                break;
            }

            Guid tenantId = tenantStep.TenantId;
            tenantsVisited++;
            try
            {
                int advancedTimeouts = await WorkerOperationBoundary.ExecuteAsync(
                        token => AdvanceInvocationTimeoutsAsync(tenantId, token),
                        ItemOperationTimeout(),
                        options.CancellationConfirmationTimeout,
                        timeProvider,
                        cancellationToken)
                    .ConfigureAwait(false);
                examined += advancedTimeouts;
                changed += advancedTimeouts;
            }
            catch (WorkerOperationTerminationUnconfirmedException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsRecoverableProcessingFailure(exception))
            {
                // A later tenant rotation retries the database-owned timeout
                // transition. No attempt is retried or invoked from this path.
                failed++;
            }

            IReadOnlyList<OperationCandidate> candidates = await ReadCandidatesAsync(
                tenantId,
                cancellationToken).ConfigureAwait(false);
            foreach (OperationCandidate candidate in candidates)
            {
                examined++;
                try
                {
                    bool itemChanged = await WorkerOperationBoundary.ExecuteAsync(
                            token => candidate.ForDispatch
                                ? DispatchAsync(candidate, token)
                                : ReconcileAsync(candidate, token),
                            ItemOperationTimeout(),
                            options.CancellationConfirmationTimeout,
                            timeProvider,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (itemChanged)
                    {
                        changed++;
                    }
                }
                catch (WorkerOperationTerminationUnconfirmedException)
                {
                    // Continuing could overlap an unobserved transaction with
                    // another claim for the same broker-facing operation.
                    throw;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (WorkerOperationTimedOutException)
                {
                    failed++;
                    await TryRecoverCandidateAsync(
                        candidate,
                        "operation_processing_timeout",
                        terminalizePreDispatch: false,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    failed++;
                    await TryRecoverCandidateAsync(
                        candidate,
                        "operation_processing_cancelled",
                        terminalizePreDispatch: false,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (NpgsqlException)
                {
                    failed++;
                    await TryRecoverCandidateAsync(
                        candidate,
                        "operation_processing_database_error",
                        terminalizePreDispatch: false,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (IsDeterministicProcessingFailure(exception))
                {
                    failed++;
                    await TryRecoverCandidateAsync(
                        candidate,
                        "operation_processing_invalid",
                        terminalizePreDispatch: candidate.ForDispatch && !candidate.IsProtective,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (IsRecoverableProcessingFailure(exception))
                {
                    failed++;
                    await TryRecoverCandidateAsync(
                        candidate,
                        "operation_processing_error",
                        terminalizePreDispatch: false,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            await RefreshBacklogObservationAsync(tenantId, cancellationToken).ConfigureAwait(false);
        }

        bool scanRotationHealthy = await tenantCatalog.IsScanProgressHealthyAsync(
                WorkerTenantScanConsumer.UserOperations,
                options.MaximumTenantScanRotationAge,
                cancellationToken)
            .ConfigureAwait(false);
        bool operationBacklogHealthy = await tenantCatalog.IsUserOperationBacklogHealthyAsync(
                options.MaximumTenantScanRotationAge,
                options.MaximumOperationBacklogAge,
                cancellationToken)
            .ConfigureAwait(false);
        return new ControlWorkCycleResult(
            tenantsVisited,
            examined,
            changed,
            failed,
            scanRotationHealthy,
            operationBacklogHealthy);
    }

    private TimeSpan ItemOperationTimeout()
    {
        long halfCycleTicks = Math.Max(1, options.OperationTimeout.Ticks / 2);
        return TimeSpan.FromTicks(Math.Min(options.DependencyTimeout.Ticks, halfCycleTicks));
    }

    private async Task<int> AdvanceInvocationTimeoutsAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(
                PostgresWorkerTenantCatalog.CreateContext(tenantId, Guid.CreateVersion7()),
                cancellationToken).ConfigureAwait(false);
        await using NpgsqlCommand command = transaction.CreateCommand(
            AdvanceInvocationTimeoutsSql);
        command.Parameters.AddWithValue(
            "max_rows",
            NpgsqlDbType.Integer,
            options.InvocationTimeoutBatchSizePerTenant);

        int advanced = 0;
        var attempts = new HashSet<Guid>();
        var receipts = new HashSet<Guid>();
        await using (NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                Guid attemptId = reader.GetGuid(0);
                string priorState = reader.GetString(1);
                string nextState = reader.GetString(2);
                long stateVersion = reader.GetInt64(3);
                Guid receiptId = reader.GetGuid(4);
                DateTimeOffset occurredAt = reader
                    .GetFieldValue<DateTimeOffset>(5).ToUniversalTime();
                string reasonCode = reader.GetString(6);
                bool validTransition =
                    priorState is "pending" or "delivered" or "prepared"
                        && nextState == "not_sent"
                        && reasonCode is "delivery_authority_expired"
                            or "redemption_expired_without_authorization"
                    || priorState == "authorized"
                        && nextState == "ambiguous"
                        && reasonCode == "gateway_invocation_receipt_timeout";
                if (attemptId == Guid.Empty
                    || receiptId == Guid.Empty
                    || stateVersion <= 0
                    || occurredAt == default
                    || occurredAt.Offset != TimeSpan.Zero
                    || !validTransition
                    || !attempts.Add(attemptId)
                    || !receipts.Add(receiptId)
                    || ++advanced > options.InvocationTimeoutBatchSizePerTenant)
                {
                    throw new InvalidOperationException(
                        "PostgreSQL returned invalid invocation-timeout evidence.");
                }
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return advanced;
    }

    private async Task TryRecoverCandidateAsync(
        OperationCandidate candidate,
        string processingErrorCode,
        bool terminalizePreDispatch,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await WorkerOperationBoundary.ExecuteAsync(
                    token => RecoverCandidateAsync(
                        candidate,
                        processingErrorCode,
                        terminalizePreDispatch,
                        token),
                    ItemOperationTimeout(),
                    options.CancellationConfirmationTimeout,
                    timeProvider,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (WorkerOperationTerminationUnconfirmedException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // The cycle is already degraded. A future scan retries the exact
            // CAS-bound candidate; no untrusted exception detail is persisted.
        }
    }

    private async Task<bool> RecoverCandidateAsync(
        OperationCandidate candidate,
        string processingErrorCode,
        bool terminalizePreDispatch,
        CancellationToken cancellationToken)
    {
        Guid claimToken = Guid.CreateVersion7();
        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(
                PostgresWorkerTenantCatalog.CreateContext(candidate.TenantId, candidate.CorrelationId),
                cancellationToken).ConfigureAwait(false);
        await AcquireAuthorityLockAsync(transaction, cancellationToken).ConfigureAwait(false);
        PersistedOperation? operation = candidate.ForDispatch
            ? await ClaimForDispatchAsync(
                transaction,
                candidate,
                claimToken,
                cancellationToken).ConfigureAwait(false)
            : await ClaimOpenAsync(
                transaction,
                candidate,
                claimToken,
                cancellationToken).ConfigureAwait(false);
        if (operation is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (terminalizePreDispatch)
        {
            await FinishAsync(
                transaction,
                operation,
                claimToken,
                "failed",
                processingErrorCode,
                null,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _ = await DeferAsync(
                transaction,
                operation,
                claimToken,
                operation.State,
                processingErrorCode,
                cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static bool IsDeterministicProcessingFailure(Exception exception) => exception is
        InvalidOperationException or
        ArgumentException or
        FormatException or
        OverflowException or
        JsonException or
        System.Security.Cryptography.CryptographicException;

    private static bool IsRecoverableProcessingFailure(Exception exception) => exception is not
        (OutOfMemoryException or
        AccessViolationException or
        AppDomainUnloadedException or
        BadImageFormatException);

    private async Task RefreshBacklogObservationAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(
                PostgresWorkerTenantCatalog.CreateContext(tenantId, Guid.CreateVersion7()),
                cancellationToken).ConfigureAwait(false);
        await using NpgsqlCommand command = transaction.CreateCommand(RefreshBacklogObservationSql);
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    "The user-operation backlog observation was not refreshed.");
            }

            Guid observedTenantId = reader.GetGuid(0);
            DateTimeOffset checkedAt = reader.GetFieldValue<DateTimeOffset>(1);
            DateTimeOffset? oldestEligible = reader.IsDBNull(2)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(2);
            long refreshCount = reader.GetInt64(3);
            long rowVersion = reader.GetInt64(4);
            if (observedTenantId != tenantId
                || checkedAt == default
                || oldestEligible > checkedAt
                || refreshCount <= 0
                || rowVersion <= 0
                || await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    "The user-operation backlog observation was invalid.");
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<OperationCandidate>> ReadCandidatesAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        Guid correlationId = Guid.CreateVersion7();
        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(
                PostgresWorkerTenantCatalog.CreateContext(tenantId, correlationId),
                cancellationToken).ConfigureAwait(false);
        await using NpgsqlCommand command = transaction.CreateCommand("""
            with work_clock as materialized
            (
                select clock_timestamp() as checked_at
            )
            select
                operation.id,
                operation.tenant_id,
                operation.correlation_id,
                operation.row_version,
                operation.state in ('accepted', 'dispatching') as for_dispatch,
                operation.operation_type
            from control.user_operations as operation
            cross join work_clock
            where operation.tenant_id = @tenant_id
              and
              (
                  operation.state = 'accepted'
                  or
                  (
                      operation.state = 'dispatching'
                      and
                      (
                          operation.claim_token is null
                          or operation.claim_expires_at <= work_clock.checked_at
                      )
                  )
                  or
                  (
                      operation.state in ('propagating', 'reconciling', 'unknown')
                      and
                      (
                          operation.claim_token is null
                          or operation.claim_expires_at <= work_clock.checked_at
                      )
                  )
              )
              and
              (
                  operation.next_processing_at is null
                  or operation.next_processing_at <= work_clock.checked_at
              )
            order by
                case
                    when operation.operation_type in
                    (
                        'broker_account.delete',
                        'broker_account.disable',
                        'deployment.stop_after_flat',
                        'deployment.close_only'
                    ) then 0
                    else 1
                end,
                coalesce(operation.next_processing_at, operation.created_at),
                operation.created_at,
                operation.id
            limit @batch_size
            """);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
        command.Parameters.AddWithValue("batch_size", NpgsqlDbType.Integer, options.OperationBatchSizePerTenant);
        var candidates = new List<OperationCandidate>(options.OperationBatchSizePerTenant);
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                candidates.Add(new OperationCandidate(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetGuid(2),
                    reader.GetInt64(3),
                    reader.GetBoolean(4),
                    reader.GetString(5)));
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return candidates;
    }

    private async Task<bool> DispatchAsync(
        OperationCandidate candidate,
        CancellationToken cancellationToken)
    {
        Guid claimToken = Guid.CreateVersion7();
        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(
                PostgresWorkerTenantCatalog.CreateContext(
                    candidate.TenantId,
                    candidate.CorrelationId),
                cancellationToken).ConfigureAwait(false);
        await AcquireAuthorityLockAsync(transaction, cancellationToken).ConfigureAwait(false);
        PersistedOperation? operation = await ClaimForDispatchAsync(
            transaction,
            candidate,
            claimToken,
            cancellationToken).ConfigureAwait(false);
        if (operation is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        TargetSnapshot? snapshot = await ReadTargetSnapshotAsync(
            transaction,
            operation,
            cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            await FinishAsync(
                transaction,
                operation,
                claimToken,
                "failed",
                "dispatch_target_missing",
                null,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (!UserOperationDispatchGuard.IsCurrent(
            operation.OperationType,
            operation.RequestedTargetState,
            operation.SubmittedResourceVersion,
            snapshot.ResourceVersion,
            snapshot.DispatchComparableState))
        {
            await FinishAsync(
                transaction,
                operation,
                claimToken,
                "cancelled",
                "operation_superseded_before_dispatch",
                null,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (UserOperationDispatchGuard.ShouldExpireBeforeDispatch(
            operation.OperationType,
            operation.CreatedAt,
            operation.AuthorizationNow,
            options.OperationExpiresAfter))
        {
            await FinishAsync(
                transaction,
                operation,
                claimToken,
                "expired",
                "operation_expired_before_dispatch",
                null,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (!UserOperationDispatchGuard.HasCompleteRoute(
            snapshot.RouteDeploymentId,
            snapshot.FenceGeneration,
            snapshot.WorkerAssignmentId,
            snapshot.WorkerInstanceId))
        {
            _ = await DeferAsync(
                transaction,
                operation,
                claimToken,
                "dispatching",
                candidate.IsProtective ? "protective_dispatch_route_pending" : null,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        DispatchPolicyDecision policy = await EvaluateDispatchPolicyAsync(
            transaction,
            operation,
            cancellationToken).ConfigureAwait(false);
        if (!policy.Allowed)
        {
            await FinishAsync(
                transaction,
                operation,
                claimToken,
                "cancelled",
                policy.ErrorCode ?? "dispatch_policy_restricted",
                null,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        InvocationAttemptCreation? creation = await CreateInvocationAttemptAsync(
            transaction,
            operation,
            claimToken,
            snapshot,
            cancellationToken).ConfigureAwait(false);
        if (creation is null)
        {
            _ = await DeferAsync(
                transaction,
                operation,
                claimToken,
                "dispatching",
                "invocation_attempt_creation_deferred",
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<InvocationAttemptCreation?> CreateInvocationAttemptAsync(
        TenantPostgresTransaction transaction,
        PersistedOperation operation,
        Guid claimToken,
        TargetSnapshot? expectedRoute,
        CancellationToken cancellationToken)
    {
        Guid attemptId = Guid.CreateVersion7();
        Guid dispatchMessageId = Guid.CreateVersion7();
        Guid auditEventId = Guid.CreateVersion7();
        string resultCapability = CreateResultCapability();
        string deliveryCapability;
        do
        {
            deliveryCapability = CreateResultCapability();
        }
        while (string.Equals(
            deliveryCapability,
            resultCapability,
            StringComparison.Ordinal));

        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select
                creation_status,
                attempt_id,
                dispatch_message_id,
                attempt_number,
                command_sha256,
                execute_not_after,
                result_capability_expires_at,
                route_deployment_id,
                fence_generation,
                worker_assignment_id,
                worker_instance_id
            from control.create_user_operation_invocation_attempt(
                @attempt_id,
                @operation_id,
                @claim_token,
                @expected_row_version,
                @dispatch_message_id,
                @audit_event_id,
                @raw_result_capability,
                @raw_delivery_capability,
                @requested_invocation_window,
                @requested_result_lifetime,
                @proof_margin)
            """);
        command.Parameters.AddWithValue("attempt_id", NpgsqlDbType.Uuid, attemptId);
        command.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, operation.Id);
        command.Parameters.AddWithValue("claim_token", NpgsqlDbType.Uuid, claimToken);
        command.Parameters.AddWithValue(
            "expected_row_version",
            NpgsqlDbType.Bigint,
            operation.RowVersion);
        command.Parameters.AddWithValue(
            "dispatch_message_id",
            NpgsqlDbType.Uuid,
            dispatchMessageId);
        command.Parameters.AddWithValue("audit_event_id", NpgsqlDbType.Uuid, auditEventId);
        command.Parameters.AddWithValue(
            "raw_result_capability",
            NpgsqlDbType.Text,
            resultCapability);
        command.Parameters.AddWithValue(
            "raw_delivery_capability",
            NpgsqlDbType.Text,
            deliveryCapability);
        command.Parameters.AddWithValue(
            "requested_invocation_window",
            NpgsqlDbType.Interval,
            options.DispatchExecutionWindow);
        command.Parameters.AddWithValue(
            "requested_result_lifetime",
            NpgsqlDbType.Interval,
            options.ResultCapabilityLifetime);
        command.Parameters.AddWithValue(
            "proof_margin",
            NpgsqlDbType.Interval,
            options.AssignmentProofMargin);

        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        string status = reader.GetString(0);
        Guid persistedAttemptId = reader.GetGuid(1);
        Guid persistedDispatchMessageId = reader.GetGuid(2);
        int attemptNumber = reader.GetInt32(3);
        string commandSha256 = reader.GetString(4);
        DateTimeOffset executeNotAfter = reader
            .GetFieldValue<DateTimeOffset>(5).ToUniversalTime();
        DateTimeOffset resultCapabilityExpiresAt = reader
            .GetFieldValue<DateTimeOffset>(6).ToUniversalTime();
        Guid routeDeploymentId = reader.GetGuid(7);
        long fenceGeneration = reader.GetInt64(8);
        Guid workerAssignmentId = reader.GetGuid(9);
        Guid workerInstanceId = reader.GetGuid(10);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            || status is not ("created" or "duplicate")
            || persistedAttemptId != attemptId
            || persistedDispatchMessageId != dispatchMessageId
            || attemptNumber <= 0
            || !IsSha256(commandSha256)
            || executeNotAfter <= operation.AuthorizationNow
            || resultCapabilityExpiresAt <= executeNotAfter
            || resultCapabilityExpiresAt - operation.AuthorizationNow > TimeSpan.FromHours(24)
            || routeDeploymentId == Guid.Empty
            || fenceGeneration <= 0
            || workerAssignmentId == Guid.Empty
            || workerInstanceId == Guid.Empty
            || expectedRoute is not null
                && (expectedRoute.RouteDeploymentId != routeDeploymentId
                    || expectedRoute.FenceGeneration != fenceGeneration
                    || expectedRoute.WorkerAssignmentId != workerAssignmentId
                    || expectedRoute.WorkerInstanceId != workerInstanceId))
        {
            throw new InvalidOperationException(
                "PostgreSQL returned invalid invocation-attempt creation evidence.");
        }

        return new InvocationAttemptCreation(
            status,
            persistedAttemptId,
            persistedDispatchMessageId,
            attemptNumber,
            commandSha256,
            executeNotAfter,
            resultCapabilityExpiresAt,
            routeDeploymentId,
            fenceGeneration,
            workerAssignmentId,
            workerInstanceId);
    }

    /// <summary>
    /// Retained only to keep the pre-v4 implementation inspectable while the
    /// immutable baseline remains supported. No active worker path calls it.
    /// </summary>
    private async Task<bool> DispatchLegacyV3Async(
        OperationCandidate candidate,
        CancellationToken cancellationToken)
    {
        Guid claimToken = Guid.CreateVersion7();
        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(
                PostgresWorkerTenantCatalog.CreateContext(candidate.TenantId, candidate.CorrelationId),
                cancellationToken).ConfigureAwait(false);
        await AcquireAuthorityLockAsync(transaction, cancellationToken).ConfigureAwait(false);
        PersistedOperation? operation = await ClaimForDispatchAsync(
            transaction,
            candidate,
            claimToken,
            cancellationToken).ConfigureAwait(false);
        if (operation is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        TargetSnapshot? snapshot = await ReadTargetSnapshotAsync(
            transaction,
            operation,
            cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            await FinishAsync(
                transaction,
                operation,
                claimToken,
                "failed",
                "dispatch_target_missing",
                null,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (!UserOperationDispatchGuard.IsCurrent(
            operation.OperationType,
            operation.RequestedTargetState,
            operation.SubmittedResourceVersion,
            snapshot.ResourceVersion,
            snapshot.DispatchComparableState))
        {
            await FinishAsync(
                transaction,
                operation,
                claimToken,
                "cancelled",
                "operation_superseded_before_dispatch",
                null,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        // Expiry is authoritative only before the first durable handoff. Once
        // an outbox message exists, execution may be ambiguous and the
        // operation must remain reconcilable. Protective intent never expires
        // merely because routing or proof is delayed.
        if (UserOperationDispatchGuard.ShouldExpireBeforeDispatch(
            operation.OperationType,
            operation.CreatedAt,
            operation.AuthorizationNow,
            options.OperationExpiresAfter))
        {
            await FinishAsync(
                transaction,
                operation,
                claimToken,
                "expired",
                "operation_expired_before_dispatch",
                null,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (!UserOperationDispatchGuard.HasCompleteRoute(
            snapshot.RouteDeploymentId,
            snapshot.FenceGeneration,
            snapshot.WorkerAssignmentId,
            snapshot.WorkerInstanceId))
        {
            _ = await DeferAsync(
                transaction,
                operation,
                claimToken,
                "dispatching",
                candidate.IsProtective ? "protective_dispatch_route_pending" : null,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        DispatchPolicyDecision policy = await EvaluateDispatchPolicyAsync(
            transaction,
            operation,
            cancellationToken).ConfigureAwait(false);
        if (!policy.Allowed)
        {
            await FinishAsync(
                transaction,
                operation,
                claimToken,
                "cancelled",
                policy.ErrorCode ?? "dispatch_policy_restricted",
                null,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        DateTimeOffset dispatchNow = await ReadAuthorityNowAsync(transaction, cancellationToken)
            .ConfigureAwait(false);
        if (UserOperationDispatchGuard.ShouldExpireBeforeDispatch(
            operation.OperationType,
            operation.CreatedAt,
            dispatchNow,
            options.OperationExpiresAfter))
        {
            await FinishAsync(
                transaction,
                operation,
                claimToken,
                "expired",
                "operation_expired_before_dispatch",
                null,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        DateTimeOffset assignmentLeaseExpiresAt = snapshot.AssignmentLeaseExpiresAt
            ?? throw new InvalidOperationException(
                "A complete dispatch route did not carry its assignment lease.");
        string resultCapability = CreateResultCapability();
        string resultCapabilitySha256 = Sha256Utf8(resultCapability);
        DateTimeOffset resultCapabilityExpiresAt =
            dispatchNow + options.ResultCapabilityLifetime;
        DateTimeOffset executionDeadline = Earliest(
            dispatchNow + options.DispatchExecutionWindow,
            resultCapabilityExpiresAt,
            assignmentLeaseExpiresAt);
        if (!UserOperationDispatchGuard.IsProtective(operation.OperationType))
        {
            executionDeadline = Earliest(
                executionDeadline,
                operation.CreatedAt + options.OperationExpiresAfter);
        }
        if (executionDeadline <= dispatchNow)
        {
            _ = await DeferAsync(
                transaction,
                operation,
                claimToken,
                "dispatching",
                "dispatch_execution_window_unavailable",
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        UserOperationDispatchEnvelope envelope = UserOperationDispatchEnvelope.Create(
            operation.Id,
            operation.TenantId,
            operation.OperationType,
            operation.TargetType,
            operation.TargetId,
            operation.ExpectedResourceVersion,
            operation.SubmittedResourceVersion,
            operation.RequestedTargetState,
            operation.IdempotencyRecordId,
            operation.CorrelationId,
            snapshot.ResourceVersion,
            snapshot.CurrentState,
            snapshot.RouteDeploymentId,
            snapshot.FenceGeneration,
            snapshot.WorkerAssignmentId,
            snapshot.WorkerInstanceId,
            snapshot.RedactedBinding,
            operation.EffectivePolicyDigest,
            operation.PolicyVersionWatermark,
            operation.PolicyInputSha256,
            policy.EvaluationEvidenceSha256,
            policy.SnapshotSha256,
            resultCapability,
            operation.CreatedAt,
            dispatchNow,
            resultCapabilityExpiresAt,
            assignmentLeaseExpiresAt,
            executionDeadline);
        OutboxMessage dispatchMessage = OutboxMessage.Create(
            operation.TenantId,
            envelope.MessageType,
            DispatchMessageAggregate,
            operation.Id.ToString("D"),
            envelope,
            operation.CorrelationId,
            operation.Id,
            dispatchNow);
        var auditPayload = new
        {
            OperationId = operation.Id,
            operation.OperationType,
            operation.TargetType,
            operation.TargetId,
            DispatchMessageId = dispatchMessage.Id,
            CurrentResourceVersion = snapshot.ResourceVersion,
            TargetBindingSha256 = envelope.TargetBinding.BindingSha256,
            PolicyEvidenceSha256 = policy.EvaluationEvidenceSha256,
            PolicySnapshotSha256 = policy.SnapshotSha256,
            snapshot.FenceGeneration,
            snapshot.WorkerAssignmentId,
            snapshot.WorkerInstanceId,
            AssignmentLeaseExpiresAt = assignmentLeaseExpiresAt,
            ExecutionDeadline = executionDeadline,
            ResultCapabilityExpiresAt = resultCapabilityExpiresAt
        };
        AuditEvent audit = AuditEvent.Create(
            operation.TenantId,
            WorkerDatabaseIdentity.ServiceActorId,
            AuditCategory.Operations,
            "user_operation.dispatched",
            operation.TargetType,
            operation.TargetId.ToString("D"),
            AuditOutcome.Accepted,
            "A typed user operation was durably dispatched.",
            operation.CorrelationId,
            operation.Id,
            auditPayload,
            dispatchNow,
            PolicyAuditContext(operation, snapshot.ResourceVersion));
        await PostgresAuditOutboxWriter.AppendAsync(
            transaction,
            audit,
            dispatchMessage,
            cancellationToken).ConfigureAwait(false);

        await using NpgsqlCommand settle = transaction.CreateCommand(
            """
            update control.user_operations
            set state = 'propagating',
                dispatch_message_id = @dispatch_message_id,
                dispatch_route_deployment_id = @dispatch_route_deployment_id,
                dispatch_fence_generation = @dispatch_fence_generation,
                dispatch_worker_assignment_id = @dispatch_worker_assignment_id,
                dispatch_worker_instance_id = @dispatch_worker_instance_id,
                dispatch_target_binding_sha256 = @dispatch_target_binding_sha256,
                dispatch_policy_snapshot_sha256 = @dispatch_policy_snapshot_sha256,
                result_capability_sha256 = @result_capability_sha256,
                result_capability_expires_at = @result_capability_expires_at,
                dispatch_assignment_lease_expires_at = @dispatch_assignment_lease_expires_at,
                dispatch_execution_deadline = @dispatch_execution_deadline,
                dispatch_attempts = dispatch_attempts + 1,
                dispatched_at = @dispatched_at,
                claimed_by = null,
                claim_token = null,
                claim_expires_at = null,
                last_error_code = null,
                row_version = row_version + 1,
                updated_at = @dispatched_at
            where tenant_id = @tenant_id
              and id = @operation_id
              and state = 'dispatching'
              and claim_token = @claim_token
              and row_version = @expected_version
            """);
        settle.Parameters.AddWithValue("dispatch_message_id", NpgsqlDbType.Uuid, dispatchMessage.Id);
        settle.Parameters.AddWithValue(
            "dispatch_route_deployment_id",
            NpgsqlDbType.Uuid,
            snapshot.RouteDeploymentId.GetValueOrDefault());
        settle.Parameters.AddWithValue(
            "dispatch_fence_generation",
            NpgsqlDbType.Bigint,
            snapshot.FenceGeneration is null ? DBNull.Value : snapshot.FenceGeneration.Value);
        settle.Parameters.AddWithValue(
            "dispatch_worker_assignment_id",
            NpgsqlDbType.Uuid,
            snapshot.WorkerAssignmentId is null ? DBNull.Value : snapshot.WorkerAssignmentId.Value);
        settle.Parameters.AddWithValue(
            "dispatch_worker_instance_id",
            NpgsqlDbType.Uuid,
            snapshot.WorkerInstanceId is null ? DBNull.Value : snapshot.WorkerInstanceId.Value);
        settle.Parameters.AddWithValue(
            "dispatch_target_binding_sha256",
            NpgsqlDbType.Text,
            envelope.TargetBinding.BindingSha256);
        settle.Parameters.AddWithValue(
            "dispatch_policy_snapshot_sha256",
            NpgsqlDbType.Text,
            policy.SnapshotSha256);
        settle.Parameters.AddWithValue(
            "result_capability_sha256",
            NpgsqlDbType.Text,
            resultCapabilitySha256);
        settle.Parameters.AddWithValue(
            "result_capability_expires_at",
            NpgsqlDbType.TimestampTz,
            resultCapabilityExpiresAt);
        settle.Parameters.AddWithValue(
            "dispatch_assignment_lease_expires_at",
            NpgsqlDbType.TimestampTz,
            assignmentLeaseExpiresAt);
        settle.Parameters.AddWithValue(
            "dispatch_execution_deadline",
            NpgsqlDbType.TimestampTz,
            executionDeadline);
        settle.Parameters.AddWithValue("dispatched_at", NpgsqlDbType.TimestampTz, dispatchNow);
        settle.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, operation.TenantId);
        settle.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, operation.Id);
        settle.Parameters.AddWithValue("claim_token", NpgsqlDbType.Uuid, claimToken);
        settle.Parameters.AddWithValue("expected_version", NpgsqlDbType.Bigint, operation.RowVersion);
        if (await settle.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("The user-operation dispatch claim was lost.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> ReconcileAsync(
        OperationCandidate candidate,
        CancellationToken cancellationToken)
    {
        Guid claimToken = Guid.CreateVersion7();
        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(
                PostgresWorkerTenantCatalog.CreateContext(candidate.TenantId, candidate.CorrelationId),
                cancellationToken).ConfigureAwait(false);
        await AcquireAuthorityLockAsync(transaction, cancellationToken).ConfigureAwait(false);
        PersistedOperation? operation = await ClaimOpenAsync(
            transaction,
            candidate,
            claimToken,
            cancellationToken).ConfigureAwait(false);
        if (operation is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        InvocationAttemptReconciliation? invocation =
            await ReconcileInvocationAttemptAsync(
                transaction,
                operation,
                claimToken,
                cancellationToken).ConfigureAwait(false);
        if (invocation is not null)
        {
            bool invocationChanged = await HandleInvocationReconciliationAsync(
                transaction,
                operation,
                claimToken,
                invocation,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return invocationChanged;
        }

        if (operation.InvocationProtocolVersion == 4)
        {
            throw new InvalidOperationException(
                "The active invocation protocol returned no reconciliation evidence.");
        }

        string? dispatchState = await ReadDispatchStateAsync(transaction, operation, cancellationToken)
            .ConfigureAwait(false);
        PersistedProof? proof = operation.TargetType == "deployment"
            ? await ReadDeploymentProofAsync(
                transaction,
                operation,
                cancellationToken).ConfigureAwait(false)
            : await ReadBrokerProofAsync(
                transaction,
                operation,
                cancellationToken).ConfigureAwait(false);
        if (operation.TargetType == "broker_account"
            && proof is { Outcome: "succeeded", BrokerResultId: not null }
            && !await ApplyConfirmedBrokerResultAsync(
                transaction,
                operation,
                proof.BrokerResultId.Value,
                cancellationToken).ConfigureAwait(false))
        {
            proof = proof with
            {
                Outcome = "partial",
                ErrorCode = "broker_projection_conflict"
            };
        }

        bool conclusiveProof = proof?.Outcome is "succeeded" or "partial";
        string next;
        if (conclusiveProof)
        {
            next = proof!.Outcome;
            await FinishAsync(
                transaction,
                operation,
                claimToken,
                next,
                proof.ErrorCode,
                proof.Reference,
                cancellationToken,
                proof).ConfigureAwait(false);
        }
        else
        {
            string? challengeStatus = await TryIssueLegacyReconciliationChallengeAsync(
                transaction,
                operation,
                cancellationToken).ConfigureAwait(false);
            bool challengeActive = challengeStatus is not null;
            bool published = string.Equals(dispatchState, "published", StringComparison.Ordinal);
            string? processingError = dispatchState switch
            {
                null => "dispatch_transport_binding_missing",
                "dead_letter" => "dispatch_delivery_ambiguous",
                _ when challengeStatus == "issued" => "reconciliation_challenge_issued",
                _ => proof?.ErrorCode
            };
            next = challengeActive
                ? "reconciling"
                : dispatchState is null or "dead_letter"
                ? "unknown"
                : AwaitingProofState(operation, published);
            _ = await DeferAsync(
                transaction,
                operation,
                claimToken,
                next,
                processingError,
                cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return next != operation.State;
    }

    private static async Task<InvocationAttemptReconciliation?>
        ReconcileInvocationAttemptAsync(
            TenantPostgresTransaction transaction,
            PersistedOperation operation,
            Guid claimToken,
            CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select
                reconciliation_status,
                attempt_id,
                attempt_number,
                attempt_state,
                attempt_state_version,
                proof_source,
                outcome,
                observation_sha256,
                observed_at,
                received_at,
                result_id,
                result_record_id,
                request_sha256,
                route_deployment_id,
                fence_generation,
                worker_assignment_id,
                worker_instance_id,
                target_type,
                target_id,
                target_observation::text,
                projection_status,
                projected_target_row_version
            from control.reconcile_user_operation_invocation_attempt(
                @operation_id,
                @claim_token,
                @expected_row_version)
            """);
        command.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, operation.Id);
        command.Parameters.AddWithValue("claim_token", NpgsqlDbType.Uuid, claimToken);
        command.Parameters.AddWithValue(
            "expected_row_version",
            NpgsqlDbType.Bigint,
            operation.RowVersion);

        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        string status = reader.GetString(0);
        Guid attemptId = reader.GetGuid(1);
        int attemptNumber = reader.GetInt32(2);
        string attemptState = reader.GetString(3);
        long stateVersion = reader.GetInt64(4);
        string? proofSource = ReadNullableString(reader, 5);
        string? outcome = ReadNullableString(reader, 6);
        string? observationSha256 = ReadNullableString(reader, 7);
        DateTimeOffset? observedAt = reader.IsDBNull(8)
            ? null
            : reader.GetFieldValue<DateTimeOffset>(8).ToUniversalTime();
        DateTimeOffset? receivedAt = reader.IsDBNull(9)
            ? null
            : reader.GetFieldValue<DateTimeOffset>(9).ToUniversalTime();
        Guid? resultId = reader.IsDBNull(10) ? null : reader.GetGuid(10);
        Guid? resultRecordId = reader.IsDBNull(11) ? null : reader.GetGuid(11);
        string? requestSha256 = ReadNullableString(reader, 12);
        Guid routeDeploymentId = reader.GetGuid(13);
        long fenceGeneration = reader.GetInt64(14);
        Guid workerAssignmentId = reader.GetGuid(15);
        Guid workerInstanceId = reader.GetGuid(16);
        string? targetType = ReadNullableString(reader, 17);
        Guid? targetId = reader.IsDBNull(18) ? null : reader.GetGuid(18);
        string? targetObservationJson = ReadNullableString(reader, 19);
        string? projectionStatus = ReadNullableString(reader, 20);
        long? projectedTargetRowVersion = reader.IsDBNull(21)
            ? null
            : reader.GetInt64(21);
        bool targetObservationValid = TryParseTargetObservation(
            targetType,
            targetObservationJson,
            out UserOperationTargetObservation? targetObservation);

        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            || status is not (
                "conclusive_projected_result" or
                "conclusive_diverged_result" or
                "projection_blocked" or
                "not_sent" or
                "challenge_outstanding" or
                "awaiting_evidence")
            || attemptId == Guid.Empty
            || operation.InvocationProtocolVersion != 4
            || operation.CurrentInvocationAttemptId != attemptId
            || attemptNumber <= 0
            || attemptState is not (
                "pending" or
                "delivered" or
                "prepared" or
                "authorized" or
                "observed" or
                "ambiguous" or
                "not_sent")
            || stateVersion < 0
            || routeDeploymentId == Guid.Empty
            || fenceGeneration <= 0
            || workerAssignmentId == Guid.Empty
            || workerInstanceId == Guid.Empty
            || status == "not_sent" && attemptState != "not_sent"
            || status == "challenge_outstanding"
                && attemptState is not ("authorized" or "ambiguous")
            || !targetObservationValid
            || !InvocationReconciliationShapeIsValid(
                operation,
                status,
                attemptState,
                proofSource,
                outcome,
                observationSha256,
                observedAt,
                receivedAt,
                resultId,
                resultRecordId,
                requestSha256,
                targetType,
                targetId,
                targetObservation,
                projectionStatus,
                projectedTargetRowVersion,
                routeDeploymentId,
                fenceGeneration,
                workerAssignmentId,
                workerInstanceId))
        {
            throw new InvalidOperationException(
                "PostgreSQL returned invalid invocation-reconciliation evidence.");
        }

        return new InvocationAttemptReconciliation(
            status,
            attemptId,
            attemptNumber,
            attemptState,
            stateVersion,
            proofSource,
            outcome,
            observationSha256,
            observedAt,
            receivedAt,
            resultId,
            resultRecordId,
            requestSha256,
            routeDeploymentId,
            fenceGeneration,
            workerAssignmentId,
            workerInstanceId,
            targetType,
            targetId,
            targetObservation,
            projectionStatus,
            projectedTargetRowVersion);
    }

    private async Task<bool> HandleInvocationReconciliationAsync(
        TenantPostgresTransaction transaction,
        PersistedOperation operation,
        Guid claimToken,
        InvocationAttemptReconciliation invocation,
        CancellationToken cancellationToken)
    {
        if (invocation.Status == "not_sent")
        {
            if (!UserOperationDispatchGuard.IsProtective(operation.OperationType))
            {
                await FinishAsync(
                    transaction,
                    operation,
                    claimToken,
                    "failed",
                    "invocation_expired_before_provider_call",
                    $"invocation-attempt/{invocation.AttemptId:D}",
                    cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (operation.State == "propagating")
            {
                return await DeferAsync(
                    transaction,
                    operation,
                    claimToken,
                    "unknown",
                    "pre_invocation_not_sent_proven",
                    cancellationToken).ConfigureAwait(false);
            }

            InvocationAttemptCreation? retry = await CreateInvocationAttemptAsync(
                transaction,
                operation,
                claimToken,
                expectedRoute: null,
                cancellationToken).ConfigureAwait(false);
            if (retry is not null)
            {
                return true;
            }

            return await DeferAsync(
                transaction,
                operation,
                claimToken,
                operation.State,
                "protective_invocation_retry_deferred",
                cancellationToken).ConfigureAwait(false);
        }

        if (invocation.Status is
            "conclusive_projected_result" or
            "conclusive_diverged_result" or
            "projection_blocked")
        {
            string reference = invocation.ResultRecordId is Guid resultRecordId
                ? $"invocation-result/{resultRecordId:D}"
                : $"invocation-observation/{invocation.AttemptId:D}";
            var proof = new PersistedProof(
                invocation.Status == "conclusive_projected_result"
                    ? "succeeded"
                    : "partial",
                invocation.Status switch
                {
                    "conclusive_diverged_result" =>
                        "runtime_reconciliation_diverged",
                    "projection_blocked" => "invocation_projection_blocked",
                    _ => null
                },
                reference,
                invocation.WorkerAssignmentId,
                invocation.WorkerInstanceId,
                RouteDeploymentId: invocation.RouteDeploymentId,
                FenceGeneration: invocation.FenceGeneration);
            await FinishAsync(
                transaction,
                operation,
                claimToken,
                proof.Outcome,
                proof.ErrorCode,
                proof.Reference,
                cancellationToken,
                proof).ConfigureAwait(false);
            return true;
        }

        if (invocation.Status == "challenge_outstanding")
        {
            return await DeferAsync(
                transaction,
                operation,
                claimToken,
                "reconciling",
                null,
                cancellationToken).ConfigureAwait(false);
        }

        string? challengeStatus = await TryIssueInvocationReconciliationChallengeAsync(
            transaction,
            operation,
            claimToken,
            cancellationToken).ConfigureAwait(false);
        string next = challengeStatus is null
            ? AwaitingProofState(operation)
            : "reconciling";
        return await DeferAsync(
            transaction,
            operation,
            claimToken,
            next,
            challengeStatus == "issued"
                ? "invocation_reconciliation_challenge_issued"
                : null,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> TryIssueInvocationReconciliationChallengeAsync(
        TenantPostgresTransaction transaction,
        PersistedOperation operation,
        Guid claimToken,
        CancellationToken cancellationToken)
    {
        Guid challengeId = Guid.CreateVersion7();
        Guid messageId = Guid.CreateVersion7();
        Guid auditId = Guid.CreateVersion7();
        string resultCapability = CreateResultCapability();
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select
                challenge_status,
                challenge_id,
                challenge_message_id,
                attempt_id,
                operation_id,
                original_dispatch_message_id,
                issued_at,
                expires_at,
                route_deployment_id,
                fence_generation,
                worker_assignment_id,
                worker_instance_id
            from control.issue_user_operation_invocation_reconciliation_challenge_v3(
                @operation_id,
                @claim_token,
                @expected_row_version,
                @challenge_id,
                @challenge_message_id,
                @audit_event_id,
                @raw_result_capability,
                @requested_lifetime)
            """);
        command.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, operation.Id);
        command.Parameters.AddWithValue("claim_token", NpgsqlDbType.Uuid, claimToken);
        command.Parameters.AddWithValue(
            "expected_row_version",
            NpgsqlDbType.Bigint,
            operation.RowVersion);
        command.Parameters.AddWithValue("challenge_id", NpgsqlDbType.Uuid, challengeId);
        command.Parameters.AddWithValue("challenge_message_id", NpgsqlDbType.Uuid, messageId);
        command.Parameters.AddWithValue("audit_event_id", NpgsqlDbType.Uuid, auditId);
        command.Parameters.AddWithValue(
            "raw_result_capability",
            NpgsqlDbType.Text,
            resultCapability);
        command.Parameters.AddWithValue(
            "requested_lifetime",
            NpgsqlDbType.Interval,
            options.ResultCapabilityLifetime);

        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        string status = reader.GetString(0);
        Guid persistedChallengeId = reader.GetGuid(1);
        Guid persistedMessageId = reader.GetGuid(2);
        Guid attemptId = reader.GetGuid(3);
        Guid operationId = reader.GetGuid(4);
        Guid originalDispatchMessageId = reader.GetGuid(5);
        DateTimeOffset issuedAt = reader.GetFieldValue<DateTimeOffset>(6).ToUniversalTime();
        DateTimeOffset expiresAt = reader.GetFieldValue<DateTimeOffset>(7).ToUniversalTime();
        Guid routeDeploymentId = reader.GetGuid(8);
        long fenceGeneration = reader.GetInt64(9);
        Guid assignmentId = reader.GetGuid(10);
        Guid workerInstanceId = reader.GetGuid(11);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            || status is not ("issued" or "duplicate" or "outstanding")
            || status is not "outstanding" && persistedChallengeId != challengeId
            || status is not "outstanding" && persistedMessageId != messageId
            || attemptId == Guid.Empty
            || operationId != operation.Id
            || originalDispatchMessageId == Guid.Empty
            || issuedAt >= expiresAt
            || expiresAt - issuedAt > TimeSpan.FromHours(24)
            || routeDeploymentId == Guid.Empty
            || fenceGeneration <= 0
            || assignmentId == Guid.Empty
            || workerInstanceId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "PostgreSQL returned invalid invocation-challenge evidence.");
        }

        return status;
    }

    private async Task<string?> TryIssueLegacyReconciliationChallengeAsync(
        TenantPostgresTransaction transaction,
        PersistedOperation operation,
        CancellationToken cancellationToken)
    {
        Guid challengeId = Guid.CreateVersion7();
        Guid messageId = Guid.CreateVersion7();
        Guid auditId = Guid.CreateVersion7();
        string resultCapability = CreateResultCapability();
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select issue_status, challenge_id, challenge_message_id,
                issued_at, expires_at, route_deployment_id,
                fence_generation, worker_assignment_id, worker_instance_id
            from control.issue_user_operation_reconciliation_challenge(
                @challenge_id, @challenge_message_id, @audit_event_id,
                @operation_id, @raw_result_capability, @requested_lifetime)
            """);
        command.Parameters.AddWithValue("challenge_id", NpgsqlDbType.Uuid, challengeId);
        command.Parameters.AddWithValue("challenge_message_id", NpgsqlDbType.Uuid, messageId);
        command.Parameters.AddWithValue("audit_event_id", NpgsqlDbType.Uuid, auditId);
        command.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, operation.Id);
        command.Parameters.AddWithValue(
            "raw_result_capability",
            NpgsqlDbType.Text,
            resultCapability);
        command.Parameters.AddWithValue(
            "requested_lifetime",
            NpgsqlDbType.Interval,
            options.ResultCapabilityLifetime);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        string status = reader.GetString(0);
        Guid persistedChallengeId = reader.GetGuid(1);
        Guid persistedMessageId = reader.GetGuid(2);
        DateTimeOffset issuedAt = reader.GetFieldValue<DateTimeOffset>(3).ToUniversalTime();
        DateTimeOffset expiresAt = reader.GetFieldValue<DateTimeOffset>(4).ToUniversalTime();
        Guid routeDeploymentId = reader.GetGuid(5);
        long fenceGeneration = reader.GetInt64(6);
        Guid assignmentId = reader.GetGuid(7);
        Guid workerInstanceId = reader.GetGuid(8);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            || status is not ("issued" or "duplicate" or "outstanding")
            || status is not "outstanding" && persistedChallengeId != challengeId
            || status is not "outstanding" && persistedMessageId != messageId
            || issuedAt >= expiresAt
            || expiresAt - issuedAt > TimeSpan.FromHours(24)
            || routeDeploymentId == Guid.Empty
            || fenceGeneration <= 0
            || assignmentId == Guid.Empty
            || workerInstanceId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "The reconciliation-challenge capability returned invalid evidence.");
        }

        return status;
    }

    private async Task<PersistedOperation?> ClaimForDispatchAsync(
        TenantPostgresTransaction transaction,
        OperationCandidate candidate,
        Guid claimToken,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            with authority_time as materialized
            (
                select clock_timestamp() as authority_now
            )
            update control.user_operations as operation
            set state = 'dispatching',
                claimed_by = @worker_id,
                claim_token = @claim_token,
                claim_expires_at = authority_time.authority_now + @claim_lease,
                row_version = operation.row_version + 1,
                updated_at = greatest(operation.updated_at, authority_time.authority_now)
            from authority_time
            where operation.tenant_id = @tenant_id
              and operation.id = @operation_id
              and operation.row_version = @expected_version
              and
              (
                  operation.state = 'accepted'
                  or (operation.state = 'dispatching'
                      and (operation.claim_token is null
                          or operation.claim_expires_at <= authority_time.authority_now))
              )
            returning
                operation.id, operation.tenant_id, operation.operation_type,
                operation.target_type, operation.target_id, operation.user_id,
                operation.idempotency_record_id, operation.expected_resource_version,
                operation.submitted_resource_version, operation.requested_target_state,
                operation.correlation_id, operation.state, operation.effective_policy_digest,
                operation.policy_version_watermark, operation.policy_input_sha256,
                operation.dispatch_message_id, operation.dispatch_fence_generation,
                operation.dispatch_route_deployment_id,
                operation.dispatch_worker_assignment_id, operation.dispatch_worker_instance_id,
                operation.dispatch_target_binding_sha256,
                operation.dispatch_policy_snapshot_sha256,
                operation.row_version, operation.created_at, operation.dispatched_at,
                operation.invocation_protocol_version,
                operation.current_invocation_attempt_id,
                authority_time.authority_now
            """);
        AddClaimParameters(command, candidate, claimToken);
        return await ReadOperationAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PersistedOperation?> ClaimOpenAsync(
        TenantPostgresTransaction transaction,
        OperationCandidate candidate,
        Guid claimToken,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            with authority_time as materialized
            (
                select clock_timestamp() as authority_now
            )
            update control.user_operations as operation
            set claimed_by = @worker_id,
                claim_token = @claim_token,
                claim_expires_at = authority_time.authority_now + @claim_lease,
                row_version = operation.row_version + 1,
                updated_at = greatest(operation.updated_at, authority_time.authority_now)
            from authority_time
            where operation.tenant_id = @tenant_id
              and operation.id = @operation_id
              and operation.row_version = @expected_version
              and operation.state in ('propagating', 'reconciling', 'unknown')
              and (operation.claim_token is null
                  or operation.claim_expires_at <= authority_time.authority_now)
            returning
                operation.id, operation.tenant_id, operation.operation_type,
                operation.target_type, operation.target_id, operation.user_id,
                operation.idempotency_record_id, operation.expected_resource_version,
                operation.submitted_resource_version, operation.requested_target_state,
                operation.correlation_id, operation.state, operation.effective_policy_digest,
                operation.policy_version_watermark, operation.policy_input_sha256,
                operation.dispatch_message_id, operation.dispatch_fence_generation,
                operation.dispatch_route_deployment_id,
                operation.dispatch_worker_assignment_id, operation.dispatch_worker_instance_id,
                operation.dispatch_target_binding_sha256,
                operation.dispatch_policy_snapshot_sha256,
                operation.row_version, operation.created_at, operation.dispatched_at,
                operation.invocation_protocol_version,
                operation.current_invocation_attempt_id,
                authority_time.authority_now
            """);
        AddClaimParameters(command, candidate, claimToken);
        return await ReadOperationAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private void AddClaimParameters(
        NpgsqlCommand command,
        OperationCandidate candidate,
        Guid claimToken)
    {
        command.Parameters.AddWithValue("worker_id", NpgsqlDbType.Text, workerIdentity.Value);
        command.Parameters.AddWithValue("claim_token", NpgsqlDbType.Uuid, claimToken);
        command.Parameters.AddWithValue("claim_lease", NpgsqlDbType.Interval, options.ClaimLease);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, candidate.TenantId);
        command.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, candidate.Id);
        command.Parameters.AddWithValue("expected_version", NpgsqlDbType.Bigint, candidate.RowVersion);
    }

    private static async Task<PersistedOperation?> ReadOperationAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        short? invocationProtocolVersion = reader.IsDBNull(25)
            ? null
            : reader.GetInt16(25);
        Guid? currentInvocationAttemptId = reader.IsDBNull(26)
            ? null
            : reader.GetGuid(26);
        if ((invocationProtocolVersion is null) !=
                (currentInvocationAttemptId is null)
            || invocationProtocolVersion is not null and not 4
            || currentInvocationAttemptId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "PostgreSQL returned an invalid user-operation protocol binding.");
        }

        var operation = new PersistedOperation(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetGuid(4),
            reader.GetGuid(5),
            reader.GetGuid(6),
            reader.IsDBNull(7) ? null : reader.GetInt64(7),
            reader.GetInt64(8),
            reader.GetString(9),
            reader.GetGuid(10),
            reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            reader.IsDBNull(15) ? null : reader.GetGuid(15),
            reader.IsDBNull(16) ? null : reader.GetInt64(16),
            reader.IsDBNull(17) ? null : reader.GetGuid(17),
            reader.IsDBNull(18) ? null : reader.GetGuid(18),
            reader.IsDBNull(19) ? null : reader.GetGuid(19),
            reader.IsDBNull(20) ? null : reader.GetString(20),
            reader.IsDBNull(21) ? null : reader.GetString(21),
            reader.GetInt64(22),
            reader.GetFieldValue<DateTimeOffset>(23),
            reader.IsDBNull(24) ? null : reader.GetFieldValue<DateTimeOffset>(24),
            invocationProtocolVersion,
            currentInvocationAttemptId,
            reader.GetFieldValue<DateTimeOffset>(27));
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "PostgreSQL returned duplicate user-operation claims.");
        }

        return operation;
    }

    private async Task<TargetSnapshot?> ReadTargetSnapshotAsync(
        TenantPostgresTransaction transaction,
        PersistedOperation operation,
        CancellationToken cancellationToken)
    {
        if (operation.TargetType == "broker_account")
        {
            await using NpgsqlCommand command = transaction.CreateCommand(
                BrokerTargetSnapshotSql);
            AddTargetParameters(command, operation);
            AddDispatchRouteParameters(command, operation);
            command.Parameters.AddWithValue(
                "minimum_route_lifetime",
                NpgsqlDbType.Interval,
                options.DispatchExecutionWindow + options.ClaimLease);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            long version = reader.GetInt64(0);
            string accountState = reader.GetString(1);
            string credentialState = reader.GetString(2);
            string environment = reader.GetString(3);
            string bindingFingerprint = reader.GetString(4);
            return new TargetSnapshot(
                version,
                $"{accountState}:{credentialState}",
                $"{accountState}:{credentialState}",
                $"{accountState}:{credentialState}",
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.IsDBNull(6) ? null : reader.GetInt64(6),
                reader.IsDBNull(7) ? null : reader.GetGuid(7),
                reader.IsDBNull(8) ? null : reader.GetGuid(8),
                reader.IsDBNull(9)
                    ? null
                    : reader.GetFieldValue<DateTimeOffset>(9).ToUniversalTime(),
                new
                {
                    Environment = environment,
                    BindingFingerprint = bindingFingerprint,
                    RouteDeploymentId = reader.IsDBNull(5) ? (Guid?)null : reader.GetGuid(5),
                    FenceGeneration = reader.IsDBNull(6) ? (long?)null : reader.GetInt64(6),
                    WorkerAssignmentId = reader.IsDBNull(7) ? (Guid?)null : reader.GetGuid(7),
                    WorkerInstanceId = reader.IsDBNull(8) ? (Guid?)null : reader.GetGuid(8)
                });
        }

        await using (NpgsqlCommand command = transaction.CreateCommand(
            """
            with authority_time as materialized
            (
                select clock_timestamp() as authorization_now
            )
            select
                row_version, desired_state, observed_state, fence_generation,
                broker_account_id, strategy_version_id, risk_policy_version_id,
                gateway_artifact_id, gateway_digest, strategy_package_digest,
                runtime_digest, configuration_sha256, binding_evidence_sha256,
                assignment.id, assignment.worker_node_id,
                assignment.lease_expires_at
            from authority_time
            cross join operations.deployments as deployment
            left join lateral
            (
                select id, worker_node_id, lease_expires_at
                from operations.worker_assignments
                where tenant_id = deployment.tenant_id
                  and deployment_id = deployment.id
                  and fence_generation = deployment.fence_generation
                  and (@dispatch_worker_assignment_id is null
                      or id = @dispatch_worker_assignment_id)
                  and (@dispatch_worker_instance_id is null
                      or worker_node_id = @dispatch_worker_instance_id)
                  and state = 'active'
                  and revoked_at is null
                  and lease_expires_at >
                      authority_time.authorization_now + @minimum_route_lifetime
                order by id desc
                limit 1
            ) as assignment on true
            where deployment.tenant_id = @tenant_id and deployment.id = @target_id
            """))
        {
            AddTargetParameters(command, operation);
            AddDispatchRouteParameters(command, operation);
            command.Parameters.AddWithValue(
                "minimum_route_lifetime",
                NpgsqlDbType.Interval,
                options.DispatchExecutionWindow + options.ClaimLease);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            long version = reader.GetInt64(0);
            string desiredState = reader.GetString(1);
            string observedState = reader.GetString(2);
            long generation = reader.GetInt64(3);
            return new TargetSnapshot(
                version,
                $"{desiredState}:{observedState}",
                desiredState,
                observedState,
                operation.TargetId,
                generation,
                reader.IsDBNull(13) ? null : reader.GetGuid(13),
                reader.IsDBNull(14) ? null : reader.GetGuid(14),
                reader.IsDBNull(15)
                    ? null
                    : reader.GetFieldValue<DateTimeOffset>(15).ToUniversalTime(),
                new
                {
                    DesiredState = desiredState,
                    FenceGeneration = generation,
                    BrokerAccountId = reader.GetGuid(4),
                    StrategyVersionId = reader.GetGuid(5),
                    RiskPolicyVersionId = reader.GetGuid(6),
                    GatewayArtifactId = reader.GetGuid(7),
                    GatewayDigest = reader.GetString(8),
                    StrategyPackageDigest = reader.GetString(9),
                    RuntimeDigest = reader.GetString(10),
                    ConfigurationSha256 = reader.GetString(11),
                    BindingEvidenceSha256 = reader.GetString(12),
                    WorkerAssignmentId = reader.IsDBNull(13) ? (Guid?)null : reader.GetGuid(13),
                    WorkerInstanceId = reader.IsDBNull(14) ? (Guid?)null : reader.GetGuid(14)
                });
        }
    }

    private async Task<DispatchPolicyDecision> EvaluateDispatchPolicyAsync(
        TenantPostgresTransaction transaction,
        PersistedOperation operation,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CurrentPolicyBinding> policies = await ReadApplicablePoliciesAsync(
            transaction,
            operation,
            cancellationToken).ConfigureAwait(false);
        bool increasesAuthority = UserOperationDispatchGuard.IncreasesAuthority(operation.OperationType);
        if (UserOperationDispatchGuard.InvalidPolicyBlocksDispatch(
            operation.OperationType,
            policies.All(static policy => policy.IntegrityValid)))
        {
            return DispatchPolicyDecision.Deny("current_policy_signature_or_digest_invalid");
        }
        if (increasesAuthority
            && policies.Any(static policy =>
                policy.CredentialMode != "NORMAL"
                || policy.LeaseMode != "NORMAL"
                || policy.PackageEligibility != "ELIGIBLE"
                || policy.WorkerActions.Count != 0))
        {
            return DispatchPolicyDecision.Deny("current_containment_policy_restricts_dispatch");
        }

        if (operation.OperationType != "deployment.start")
        {
            string snapshotSha256 = CanonicalJson.Sha256(new
            {
                operation.OperationType,
                Policies = policies
            });
            return DispatchPolicyDecision.Allow(snapshotSha256, null);
        }

        StartEvaluation? evaluation = await ReadStartEvaluationAsync(
            transaction,
            operation,
            cancellationToken).ConfigureAwait(false);
        if (evaluation is null || !evaluation.HasValidCanonicalDigest())
        {
            return DispatchPolicyDecision.Deny("policy_evidence_missing_or_invalid");
        }

        if (!StartRulesAllow(evaluation.EffectiveVectorJson, evaluation.RuleResultsJson)
            || !ExpectedOverlaysMatch(evaluation.ApplicablePoliciesJson, policies))
        {
            return DispatchPolicyDecision.Deny("policy_evaluation_superseded");
        }

        CurrentBaseline? baseline = await ReadCurrentBaselineAsync(
            transaction,
            operation,
            cancellationToken).ConfigureAwait(false);
        if (baseline is null
            || !ExpectedBaselineMatches(evaluation.ApplicablePoliciesJson, baseline))
        {
            return DispatchPolicyDecision.Deny("policy_baseline_superseded");
        }

        string currentSnapshotSha256 = CanonicalJson.Sha256(new
        {
            operation.OperationType,
            EvaluationEvidenceSha256 = evaluation.EvidenceSha256,
            Baseline = baseline,
            Policies = policies
        });
        return DispatchPolicyDecision.Allow(currentSnapshotSha256, evaluation.EvidenceSha256);
    }

    private async Task<IReadOnlyList<CurrentPolicyBinding>> ReadApplicablePoliciesAsync(
        TenantPostgresTransaction transaction,
        PersistedOperation operation,
        CancellationToken cancellationToken)
    {
        string targetCte = operation.TargetType == "deployment"
            ? """
                select
                    deployment.environment,
                    deployment.region,
                    account.broker_id,
                    deployment.gateway_artifact_id,
                    deployment.runtime_digest,
                    strategy.strategy_id,
                    deployment.strategy_version_id,
                    deployment.user_id,
                    deployment.broker_account_id,
                    deployment.id as deployment_id
                from operations.deployments as deployment
                join operations.broker_accounts as account
                  on account.tenant_id = deployment.tenant_id
                 and account.id = deployment.broker_account_id
                join governance.strategy_versions as strategy
                  on strategy.tenant_id = deployment.tenant_id
                 and strategy.id = deployment.strategy_version_id
                where deployment.tenant_id = @tenant_id
                  and deployment.id = @target_id
                  and deployment.user_id = @user_id
                """
            : """
                select
                    account.environment,
                    null::text as region,
                    account.broker_id,
                    null::uuid as gateway_artifact_id,
                    null::text as runtime_digest,
                    null::uuid as strategy_id,
                    null::uuid as strategy_version_id,
                    account.user_id,
                    account.id as broker_account_id,
                    null::uuid as deployment_id
                from operations.broker_accounts as account
                where account.tenant_id = @tenant_id
                  and account.id = @target_id
                  and account.user_id = @user_id
                """;
        await using NpgsqlCommand command = transaction.CreateCommand($$"""
            with target as
            (
                {{targetCte}}
            )
            select
                policy.id,
                policy.policy_version,
                policy.scope_type,
                policy.scope_id,
                policy.allow_new_deployment,
                policy.allow_strategy_signals,
                policy.allow_exposure_increase,
                policy.allow_exposure_reduction,
                policy.allow_protection,
                policy.allow_pending_order_cancellation,
                policy.allow_emergency_close,
                policy.lease_mode,
                policy.worker_actions,
                policy.credential_mode,
                policy.package_eligibility,
                policy.policy_digest,
                policy.signature_algorithm,
                policy.signature_sha256,
                policy.signing_key_id,
                policy.reason,
                policy.incident_id,
                policy.owner_id,
                policy.authority_expires_at,
                policy.review_deadline,
                policy.signature_bytes
            from control.execution_safety_policies as policy
            cross join target
            where policy.tenant_id = @tenant_id
              and policy.state in
              (
                  'active', 'expiry_review_required', 'safe_to_release',
                  'deactivating', 'reconciling', 'partial'
              )
              and
              (
                  (policy.scope_type = 'global' and policy.scope_id is null)
                  or (policy.scope_type = 'environment'
                      and lower(policy.scope_id) = lower(target.environment))
                  or (policy.scope_type = 'region' and target.region is not null
                      and lower(policy.scope_id) = lower(target.region))
                  or (policy.scope_type = 'broker'
                      and lower(policy.scope_id) = lower(target.broker_id::text))
                  or (policy.scope_type = 'gateway' and target.gateway_artifact_id is not null
                      and lower(policy.scope_id) = lower(target.gateway_artifact_id::text))
                  or (policy.scope_type = 'runtime' and target.runtime_digest is not null
                      and lower(policy.scope_id) = lower(target.runtime_digest))
                  or (policy.scope_type = 'strategy' and target.strategy_id is not null
                      and lower(policy.scope_id) = lower(target.strategy_id::text))
                  or (policy.scope_type = 'strategy_version' and target.strategy_version_id is not null
                      and lower(policy.scope_id) = lower(target.strategy_version_id::text))
                  or (policy.scope_type = 'user'
                      and lower(policy.scope_id) = lower(target.user_id::text))
                  or (policy.scope_type = 'account'
                      and lower(policy.scope_id) = lower(target.broker_account_id::text))
                  or (policy.scope_type = 'deployment' and target.deployment_id is not null
                      and lower(policy.scope_id) = lower(target.deployment_id::text))
              )
            order by policy.scope_type, policy.scope_id nulls first,
                policy.policy_version, policy.id
            """);
        AddTargetParameters(command, operation);
        command.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, operation.UserId);
        var result = new List<CurrentPolicyBinding>();
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string[] workerActions = reader.GetFieldValue<string[]>(12);
            result.Add(CurrentPolicyBinding.Create(
                reader.GetGuid(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetBoolean(4),
                reader.GetBoolean(5),
                reader.GetBoolean(6),
                reader.GetBoolean(7),
                reader.GetBoolean(8),
                reader.GetBoolean(9),
                reader.GetBoolean(10),
                reader.GetString(11),
                workerActions,
                reader.GetString(13),
                reader.GetString(14),
                reader.GetString(15),
                reader.GetString(16),
                reader.GetString(17),
                reader.GetString(18),
                reader.GetString(19),
                reader.IsDBNull(20) ? null : reader.GetGuid(20),
                reader.GetGuid(21),
                reader.IsDBNull(22) ? null : reader.GetFieldValue<DateTimeOffset>(22),
                reader.GetFieldValue<DateTimeOffset>(23),
                reader.GetFieldValue<byte[]>(24),
                operation.TenantId,
                policyTrustStore));
        }

        return result;
    }

    private static async Task<StartEvaluation?> ReadStartEvaluationAsync(
        TenantPostgresTransaction transaction,
        PersistedOperation operation,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select
                input_snapshot::text,
                applicable_policies::text,
                effective_vector::text,
                rule_results::text,
                effective_policy_digest,
                policy_version_watermark,
                input_sha256,
                evidence_sha256
            from control.user_policy_evaluations
            where tenant_id = @tenant_id
              and user_id = @user_id
              and idempotency_record_id = @idempotency_record_id
              and decision_type = 'deployment.start'
              and target_type = 'deployment'
              and target_id = @target_id
              and decision = 'allow'
              and effective_policy_digest = @effective_policy_digest
              and policy_version_watermark = @policy_version_watermark
              and input_sha256 = @policy_input_sha256
            """);
        AddTargetParameters(command, operation);
        command.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, operation.UserId);
        command.Parameters.AddWithValue("idempotency_record_id", NpgsqlDbType.Uuid, operation.IdempotencyRecordId);
        command.Parameters.AddWithValue("effective_policy_digest", NpgsqlDbType.Text, operation.EffectivePolicyDigest!);
        command.Parameters.AddWithValue("policy_version_watermark", NpgsqlDbType.Text, operation.PolicyVersionWatermark!);
        command.Parameters.AddWithValue("policy_input_sha256", NpgsqlDbType.Text, operation.PolicyInputSha256!);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new StartEvaluation(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7))
            : null;
    }

    private async Task<CurrentBaseline?> ReadCurrentBaselineAsync(
        TenantPostgresTransaction transaction,
        PersistedOperation operation,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select
                policy.id,
                policy.policy_id,
                policy.version_number,
                policy.normalized_policy::text,
                policy.policy_digest,
                policy.signature_algorithm,
                policy.signature_bytes,
                policy.signature_sha256,
                policy.signing_key_id
            from operations.deployments as deployment
            join governance.risk_policy_versions as policy
              on policy.tenant_id = deployment.tenant_id
             and policy.id = deployment.risk_policy_version_id
             and policy.policy_digest = deployment.risk_policy_digest
            where deployment.tenant_id = @tenant_id
              and deployment.id = @target_id
              and deployment.user_id = @user_id
              and policy.state = 'active'
            """);
        AddTargetParameters(command, operation);
        command.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, operation.UserId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        string normalizedPolicyJson = reader.GetString(3);
        Guid versionId = reader.GetGuid(0);
        Guid policyId = reader.GetGuid(1);
        int version = reader.GetInt32(2);
        string digest = reader.GetString(4);
        string signatureAlgorithm = reader.GetString(5);
        byte[] signature = reader.GetFieldValue<byte[]>(6);
        string signatureSha256 = reader.GetString(7);
        string signingKeyId = reader.GetString(8);
        return new CurrentBaseline(
            policyId,
            version,
            digest,
            CanonicalJson.Sha256(JsonNode.Parse(normalizedPolicyJson)),
            signatureAlgorithm,
            signatureSha256,
            signingKeyId,
            policyTrustStore.Verify(
                signingKeyId,
                signatureAlgorithm,
                signature,
                signatureSha256,
                CreateRiskPolicySignaturePayload(
                    operation.TenantId,
                    versionId,
                    policyId,
                    version,
                    digest)));
    }

    private static bool StartRulesAllow(string effectiveVectorJson, string ruleResultsJson)
    {
        using JsonDocument vectorDocument = JsonDocument.Parse(effectiveVectorJson);
        using JsonDocument rulesDocument = JsonDocument.Parse(ruleResultsJson);
        JsonElement vector = vectorDocument.RootElement;
        JsonElement rules = rulesDocument.RootElement;
        return ReadBoolean(vector, "allowNewDeployment")
            && ReadBoolean(vector, "allowStrategySignals")
            && ReadBoolean(vector, "allowExposureIncrease")
            && ReadString(vector, "leaseMode") == "Normal"
            && ReadString(vector, "credentialMode") == "Normal"
            && ReadString(vector, "packageEligibility") == "Eligible"
            && ReadArrayLength(vector, "workerActions") == 0
            && ReadBoolean(rules, "integrityValid")
            && ReadBoolean(rules, "allowsNewExecution");
    }

    private static bool ExpectedBaselineMatches(string applicablePoliciesJson, CurrentBaseline current)
    {
        using JsonDocument document = JsonDocument.Parse(applicablePoliciesJson);
        if (!document.RootElement.TryGetProperty("baseline", out JsonElement baseline))
        {
            return false;
        }

        return ReadGuid(baseline, "id") == current.Id
            && current.SignatureValid
            && ReadInt64(baseline, "version") == current.Version
            && FixedDigestEquals(ReadString(baseline, "digest"), current.Digest)
            && FixedDigestEquals(
                ReadString(baseline, "canonicalInputDigest"),
                current.CanonicalInputDigest)
            && ReadString(baseline, "signatureAlgorithm") == current.SignatureAlgorithm
            && FixedDigestEquals(ReadString(baseline, "signatureSha256"), current.SignatureSha256)
            && ReadString(baseline, "signingKeyId") == current.SigningKeyId;
    }

    private static bool ExpectedOverlaysMatch(
        string applicablePoliciesJson,
        IReadOnlyList<CurrentPolicyBinding> current)
    {
        using JsonDocument document = JsonDocument.Parse(applicablePoliciesJson);
        if (!document.RootElement.TryGetProperty("overlays", out JsonElement overlays)
            || overlays.ValueKind != JsonValueKind.Array
            || overlays.GetArrayLength() != current.Count)
        {
            return false;
        }

        var expectedById = new Dictionary<Guid, JsonElement>();
        foreach (JsonElement overlay in overlays.EnumerateArray())
        {
            Guid? id = ReadGuid(overlay, "id");
            if (id is null || !expectedById.TryAdd(id.Value, overlay.Clone()))
            {
                return false;
            }
        }

        return current.All(policy =>
            expectedById.TryGetValue(policy.Id, out JsonElement expected)
            && policy.Matches(expected));
    }

    private static bool ReadBoolean(JsonElement value, string name) =>
        value.TryGetProperty(name, out JsonElement property)
        && property.ValueKind is JsonValueKind.True or JsonValueKind.False
        && property.GetBoolean();

    private static string? ReadString(JsonElement value, string name) =>
        value.TryGetProperty(name, out JsonElement property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static Guid? ReadGuid(JsonElement value, string name) =>
        Guid.TryParse(ReadString(value, name), out Guid parsed) ? parsed : null;

    private static long? ReadInt64(JsonElement value, string name) =>
        value.TryGetProperty(name, out JsonElement property)
        && property.TryGetInt64(out long parsed)
            ? parsed
            : null;

    private static int ReadArrayLength(JsonElement value, string name) =>
        value.TryGetProperty(name, out JsonElement property)
        && property.ValueKind == JsonValueKind.Array
            ? property.GetArrayLength()
            : -1;

    private static async Task<string?> ReadDispatchStateAsync(
        TenantPostgresTransaction transaction,
        PersistedOperation operation,
        CancellationToken cancellationToken)
    {
        if (operation.DispatchMessageId is null)
        {
            return null;
        }

        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select state
            from messaging.outbox_messages
            where tenant_id = @tenant_id
              and id = @message_id
              and aggregate_type = 'user_operation'
              and aggregate_id = @operation_id
              and correlation_id = @correlation_id
              and causation_id = @operation_uuid
              and message_type = @message_type
            """);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, operation.TenantId);
        command.Parameters.AddWithValue("message_id", NpgsqlDbType.Uuid, operation.DispatchMessageId.Value);
        command.Parameters.AddWithValue("operation_id", NpgsqlDbType.Text, operation.Id.ToString("D"));
        command.Parameters.AddWithValue("operation_uuid", NpgsqlDbType.Uuid, operation.Id);
        command.Parameters.AddWithValue("correlation_id", NpgsqlDbType.Uuid, operation.CorrelationId);
        command.Parameters.AddWithValue(
            "message_type",
            NpgsqlDbType.Text,
            $"yo4x.{operation.OperationType.Replace('_', '-')}.requested.v3");
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
    }

    private static async Task<PersistedProof?> ReadDeploymentProofAsync(
        TenantPostgresTransaction transaction,
        PersistedOperation operation,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select
                id, state, desired_digest, observed_digest, broker_digest,
                observed_state,
                runtime_evidence_sha256,
                dispatch_message_id,
                submitted_resource_version,
                requested_target_state,
                generation,
                worker_assignment_id,
                worker_instance_id,
                policy_snapshot_sha256,
                broker_confirmed,
                broker_execution_state,
                broker_position_state,
                reconciliation.dispatch_target_binding_sha256,
                reconciliation.pre_invocation_not_sent_proven,
                reconciliation.gateway_invoked,
                reconciliation.reconciliation_challenge_id,
                reconciliation.request_sha256,
                challenge.route_deployment_id,
                challenge.fence_generation,
                challenge.worker_assignment_id,
                challenge.worker_instance_id,
                consumption.request_sha256,
                challenge.operation_id,
                challenge.original_dispatch_message_id
            from operations.deployment_reconciliations as reconciliation
            left join control.user_operation_reconciliation_challenges as challenge
              on challenge.tenant_id = reconciliation.tenant_id
             and challenge.id = reconciliation.reconciliation_challenge_id
            left join control.user_operation_reconciliation_challenge_consumptions as consumption
              on consumption.tenant_id = reconciliation.tenant_id
             and consumption.challenge_id = reconciliation.reconciliation_challenge_id
             and consumption.target_type = 'deployment'
             and consumption.result_record_id = reconciliation.id
             and consumption.result_id = reconciliation.result_id
            where reconciliation.tenant_id = @tenant_id
              and reconciliation.deployment_id = @target_id
              and reconciliation.completed_at is not null
              and reconciliation.dispatch_message_id = @dispatch_message_id
            order by reconciliation.completed_at desc, reconciliation.id
            limit 1
            """);
        AddTargetParameters(command, operation);
        command.Parameters.AddWithValue("dispatch_message_id", NpgsqlDbType.Uuid, operation.DispatchMessageId!.Value);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        Guid proofId = reader.GetGuid(0);
        string state = reader.GetString(1);
        string desiredDigest = reader.GetString(2);
        string? observedDigest = reader.IsDBNull(3) ? null : reader.GetString(3);
        string? brokerDigest = reader.IsDBNull(4) ? null : reader.GetString(4);
        string? observedState = reader.IsDBNull(5) ? null : reader.GetString(5);
        string? runtimeEvidenceDigest = reader.IsDBNull(6) ? null : reader.GetString(6);
        Guid? dispatchMessageId = reader.IsDBNull(7) ? null : reader.GetGuid(7);
        long? submittedResourceVersion = reader.IsDBNull(8) ? null : reader.GetInt64(8);
        string? requestedTargetState = ReadNullableString(reader, 9);
        long? fenceGeneration = reader.IsDBNull(10) ? null : reader.GetInt64(10);
        Guid? assignmentId = reader.IsDBNull(11) ? null : reader.GetGuid(11);
        Guid? workerInstanceId = reader.IsDBNull(12) ? null : reader.GetGuid(12);
        string? policySnapshotSha256 = ReadNullableString(reader, 13);
        bool brokerConfirmed = !reader.IsDBNull(14) && reader.GetBoolean(14);
        string? brokerExecutionState = ReadNullableString(reader, 15);
        string? brokerPositionState = ReadNullableString(reader, 16);
        string? dispatchTargetBindingSha256 = ReadNullableString(reader, 17);
        bool preInvocationNotSentProven = reader.GetBoolean(18);
        bool gatewayInvoked = reader.GetBoolean(19);
        Guid? challengeId = reader.IsDBNull(20) ? null : reader.GetGuid(20);
        string requestSha256 = reader.GetString(21);
        Guid? challengeRouteDeploymentId = reader.IsDBNull(22) ? null : reader.GetGuid(22);
        long? challengeFenceGeneration = reader.IsDBNull(23) ? null : reader.GetInt64(23);
        Guid? challengeAssignmentId = reader.IsDBNull(24) ? null : reader.GetGuid(24);
        Guid? challengeWorkerInstanceId = reader.IsDBNull(25) ? null : reader.GetGuid(25);
        string? consumptionRequestSha256 = ReadNullableString(reader, 26);
        Guid? challengeOperationId = reader.IsDBNull(27) ? null : reader.GetGuid(27);
        Guid? challengeOriginalDispatchMessageId = reader.IsDBNull(28) ? null : reader.GetGuid(28);
        string expectedObservedState = operation.RequestedTargetState;
        string expectedBrokerState = operation.OperationType switch
        {
            "deployment.start" => "running",
            "deployment.close_only" => "close_only",
            "deployment.stop_after_flat" => "stopped",
            _ => throw new InvalidOperationException("A persisted deployment operation is invalid.")
        };
        string reference = $"deployment-reconciliation/{proofId:D}";
        bool legacyDispatchRouteValid = fenceGeneration == operation.DispatchFenceGeneration
            && assignmentId == operation.DispatchWorkerAssignmentId
            && workerInstanceId == operation.DispatchWorkerInstanceId;
        bool challengeRouteValid = challengeId is null
            || challengeOperationId == operation.Id
            && challengeOriginalDispatchMessageId == operation.DispatchMessageId
            && challengeRouteDeploymentId == operation.TargetId
            && challengeFenceGeneration is > 0
            && challengeAssignmentId is not null
            && challengeWorkerInstanceId is not null
            && FixedDigestEquals(consumptionRequestSha256 ?? string.Empty, requestSha256);
        Guid proofRouteDeploymentId = challengeRouteDeploymentId ?? operation.TargetId;
        long? proofFenceGeneration = challengeFenceGeneration ?? fenceGeneration;
        Guid? proofAssignmentId = challengeAssignmentId ?? assignmentId;
        Guid? proofWorkerInstanceId = challengeWorkerInstanceId ?? workerInstanceId;
        bool commonBindingValid = dispatchMessageId == operation.DispatchMessageId
            && submittedResourceVersion == operation.SubmittedResourceVersion
            && string.Equals(requestedTargetState, operation.RequestedTargetState, StringComparison.Ordinal)
            && assignmentId is not null
            && workerInstanceId is not null
            && legacyDispatchRouteValid
            && challengeRouteValid
            && FixedDigestEquals(policySnapshotSha256 ?? string.Empty, operation.DispatchPolicySnapshotSha256)
            && FixedDigestEquals(
                dispatchTargetBindingSha256 ?? string.Empty,
                operation.DispatchTargetBindingSha256);
        if (!commonBindingValid)
        {
            return null;
        }

        if (state == "reconciled"
            && FixedDigestEquals(desiredDigest, observedDigest)
            && IsSha256(brokerDigest)
            && IsSha256(runtimeEvidenceDigest)
            && brokerConfirmed
            && !preInvocationNotSentProven
            && gatewayInvoked
            && string.Equals(observedState, expectedObservedState, StringComparison.Ordinal)
            && string.Equals(brokerExecutionState, expectedBrokerState, StringComparison.Ordinal)
            && (operation.OperationType != "deployment.stop_after_flat"
                || string.Equals(brokerPositionState, "flat", StringComparison.Ordinal)))
        {
            return new PersistedProof(
                "succeeded",
                null,
                reference,
                proofAssignmentId,
                proofWorkerInstanceId,
                RouteDeploymentId: proofRouteDeploymentId,
                FenceGeneration: proofFenceGeneration);
        }

        return state switch
        {
            "diverged" when !preInvocationNotSentProven
                && gatewayInvoked
                && brokerConfirmed => new PersistedProof(
                    "partial", "runtime_reconciliation_diverged", reference,
                    proofAssignmentId, proofWorkerInstanceId,
                    RouteDeploymentId: proofRouteDeploymentId,
                    FenceGeneration: proofFenceGeneration),
            "unknown" => new PersistedProof("unknown", "runtime_reconciliation_unknown", reference),
            _ => null
        };
    }

    private static async Task<PersistedProof?> ReadBrokerProofAsync(
        TenantPostgresTransaction transaction,
        PersistedOperation operation,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select
                result.id, result.proof_kind, result.outcome,
                result.evidence_sha256, result.error_code,
                result.dispatch_message_id, result.submitted_resource_version,
                result.requested_target_state, result.policy_snapshot_sha256,
                result.broker_confirmed, result.account_state,
                result.credential_state, result.route_deployment_id,
                result.generation, result.worker_assignment_id,
                result.worker_instance_id,
                result.dispatch_target_binding_sha256,
                result.pre_invocation_not_sent_proven,
                result.gateway_invoked, result.reconciliation_challenge_id,
                result.request_sha256, challenge.route_deployment_id,
                challenge.fence_generation, challenge.worker_assignment_id,
                challenge.worker_instance_id, consumption.request_sha256,
                challenge.operation_id, challenge.original_dispatch_message_id
            from operations.user_operation_results as result
            left join control.user_operation_reconciliation_challenges as challenge
              on challenge.tenant_id = result.tenant_id
             and challenge.id = result.reconciliation_challenge_id
            left join control.user_operation_reconciliation_challenge_consumptions as consumption
              on consumption.tenant_id = result.tenant_id
             and consumption.challenge_id = result.reconciliation_challenge_id
             and consumption.target_type = 'broker_account'
             and consumption.result_record_id = result.id
             and consumption.result_id = result.result_id
            where result.tenant_id = @tenant_id
              and result.operation_id = @operation_id
              and result.broker_account_id = @target_id
              and result.dispatch_message_id = @dispatch_message_id
            """);
        AddTargetParameters(command, operation);
        command.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, operation.Id);
        command.Parameters.AddWithValue(
            "dispatch_message_id",
            NpgsqlDbType.Uuid,
            operation.DispatchMessageId!.Value);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        Guid proofId = reader.GetGuid(0);
        string? proofKind = reader.IsDBNull(1) ? null : reader.GetString(1);
        string? outcome = reader.IsDBNull(2) ? null : reader.GetString(2);
        string? evidenceDigest = reader.IsDBNull(3) ? null : reader.GetString(3);
        string? resultCode = reader.IsDBNull(4) ? null : reader.GetString(4);
        Guid dispatchMessageId = reader.GetGuid(5);
        long submittedResourceVersion = reader.GetInt64(6);
        string? requestedTargetState = ReadNullableString(reader, 7);
        string? policySnapshotSha256 = ReadNullableString(reader, 8);
        bool brokerConfirmed = reader.GetBoolean(9);
        string? accountState = ReadNullableString(reader, 10);
        string? credentialState = ReadNullableString(reader, 11);
        Guid routeDeploymentId = reader.GetGuid(12);
        long generation = reader.GetInt64(13);
        Guid assignmentId = reader.GetGuid(14);
        Guid workerInstanceId = reader.GetGuid(15);
        string? dispatchTargetBindingSha256 = ReadNullableString(reader, 16);
        bool preInvocationNotSentProven = reader.GetBoolean(17);
        bool gatewayInvoked = reader.GetBoolean(18);
        Guid? challengeId = reader.IsDBNull(19) ? null : reader.GetGuid(19);
        string requestSha256 = reader.GetString(20);
        Guid? challengeRouteDeploymentId = reader.IsDBNull(21) ? null : reader.GetGuid(21);
        long? challengeFenceGeneration = reader.IsDBNull(22) ? null : reader.GetInt64(22);
        Guid? challengeAssignmentId = reader.IsDBNull(23) ? null : reader.GetGuid(23);
        Guid? challengeWorkerInstanceId = reader.IsDBNull(24) ? null : reader.GetGuid(24);
        string? consumptionRequestSha256 = ReadNullableString(reader, 25);
        Guid? challengeOperationId = reader.IsDBNull(26) ? null : reader.GetGuid(26);
        Guid? challengeOriginalDispatchMessageId = reader.IsDBNull(27) ? null : reader.GetGuid(27);
        string actionSuccessKind = operation.OperationType switch
        {
            "broker_account.connection_test" => "connection_verified",
            "broker_account.credential_rotation" => "credential_rotated",
            "broker_account.disable" => "account_disabled",
            "broker_account.delete" => "credential_deleted",
            _ => throw new InvalidOperationException("A persisted broker-account operation is invalid.")
        };
        if (outcome is not ("succeeded" or "diverged"))
        {
            // Result v4 has no database-owned invocation-attempt receipt. Historical
            // caller-asserted failures remain audit evidence only and must never
            // terminalize or make a fresh mutation retryable.
            return null;
        }

        string expectedKind = outcome switch
        {
            "diverged" => "state_observed_diverged",
            _ => actionSuccessKind
        };
        string brokerResultState = $"{accountState}:{credentialState}";
        bool legacyDispatchRouteValid = routeDeploymentId == operation.DispatchRouteDeploymentId
            && generation == operation.DispatchFenceGeneration
            && assignmentId == operation.DispatchWorkerAssignmentId
            && workerInstanceId == operation.DispatchWorkerInstanceId;
        bool challengeRouteValid = challengeId is null
            || challengeOperationId == operation.Id
            && challengeOriginalDispatchMessageId == operation.DispatchMessageId
            && challengeRouteDeploymentId is not null
            && challengeFenceGeneration is > 0
            && challengeAssignmentId is not null
            && challengeWorkerInstanceId is not null
            && FixedDigestEquals(consumptionRequestSha256 ?? string.Empty, requestSha256);
        Guid proofRouteDeploymentId = challengeRouteDeploymentId ?? routeDeploymentId;
        long proofFenceGeneration = challengeFenceGeneration ?? generation;
        Guid proofAssignmentId = challengeAssignmentId ?? assignmentId;
        Guid proofWorkerInstanceId = challengeWorkerInstanceId ?? workerInstanceId;
        if (!string.Equals(proofKind, expectedKind, StringComparison.Ordinal)
            || !IsSha256(evidenceDigest)
            || dispatchMessageId != operation.DispatchMessageId
            || submittedResourceVersion != operation.SubmittedResourceVersion
            || !string.Equals(requestedTargetState, operation.RequestedTargetState, StringComparison.Ordinal)
            || !FixedDigestEquals(policySnapshotSha256 ?? string.Empty, operation.DispatchPolicySnapshotSha256)
            || !legacyDispatchRouteValid
            || !challengeRouteValid
            || !FixedDigestEquals(
                dispatchTargetBindingSha256 ?? string.Empty,
                operation.DispatchTargetBindingSha256)
            || outcome == "succeeded"
                && (!gatewayInvoked
                    || preInvocationNotSentProven
                    || !brokerConfirmed
                    || accountState is null
                    || credentialState is null
                    || resultCode is not null
                    || !string.Equals(brokerResultState, operation.RequestedTargetState, StringComparison.Ordinal))
            || outcome == "diverged"
                && (!gatewayInvoked
                    || preInvocationNotSentProven
                    || !brokerConfirmed
                    || accountState is null
                    || credentialState is null
                    || resultCode is null
                    || string.Equals(brokerResultState, operation.RequestedTargetState, StringComparison.Ordinal)))
        {
            return null;
        }

        string reference = $"broker-operation-result/{proofId:D}";
        return outcome switch
        {
            "succeeded" when brokerConfirmed => new PersistedProof(
                "succeeded",
                null,
                reference,
                proofAssignmentId,
                proofWorkerInstanceId,
                proofId,
                proofRouteDeploymentId,
                proofFenceGeneration),
            "diverged" => new PersistedProof(
                "partial",
                NormalizeError(resultCode, "runtime_reconciliation_diverged"),
                reference,
                proofAssignmentId,
                proofWorkerInstanceId,
                RouteDeploymentId: proofRouteDeploymentId,
                FenceGeneration: proofFenceGeneration),
            _ => null
        };
    }

    private static async Task<bool> ApplyConfirmedBrokerResultAsync(
        TenantPostgresTransaction transaction,
        PersistedOperation operation,
        Guid resultId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            "select control.apply_confirmed_broker_operation_result(@tenant_id, @operation_id, @result_id)");
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, operation.TenantId);
        command.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, operation.Id);
        command.Parameters.AddWithValue("result_id", NpgsqlDbType.Uuid, resultId);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true;
    }

    private static string? ReadNullableString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static async Task<bool> DeferAsync(
        TenantPostgresTransaction transaction,
        PersistedOperation operation,
        Guid claimToken,
        string state,
        string? processingErrorCode,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            "select * from control.defer_user_operation(@operation_id, @claim_token, @expected_version, @state, @processing_error_code)");
        command.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, operation.Id);
        command.Parameters.AddWithValue("claim_token", NpgsqlDbType.Uuid, claimToken);
        command.Parameters.AddWithValue("expected_version", NpgsqlDbType.Bigint, operation.RowVersion);
        command.Parameters.AddWithValue("state", NpgsqlDbType.Text, state);
        command.Parameters.AddWithValue(
            "processing_error_code",
            NpgsqlDbType.Text,
            processingErrorCode is null
                ? DBNull.Value
                : NormalizeProcessingError(processingErrorCode));
        long nextVersion;
        DateTimeOffset deferredAt;
        DateTimeOffset nextProcessingAt;
        long deferralCount;
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("The user-operation deferral claim was lost.");
            }

            nextVersion = reader.GetInt64(0);
            deferredAt = reader.GetFieldValue<DateTimeOffset>(1);
            nextProcessingAt = reader.GetFieldValue<DateTimeOffset>(2);
            deferralCount = reader.GetInt64(3);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                || nextVersion != operation.RowVersion + 1
                || nextProcessingAt <= deferredAt
                || deferralCount <= 0)
            {
                throw new InvalidOperationException("The user-operation deferral evidence was invalid.");
            }
        }

        bool stateChanged = !string.Equals(operation.State, state, StringComparison.Ordinal);
        if (stateChanged)
        {
            await AppendLifecycleEventAsync(
                transaction,
                operation,
                state,
                processingErrorCode,
                null,
                null,
                nextVersion,
                deferredAt,
                cancellationToken).ConfigureAwait(false);
        }

        return stateChanged;
    }

    private static async Task FinishAsync(
        TenantPostgresTransaction transaction,
        PersistedOperation operation,
        Guid claimToken,
        string state,
        string? errorCode,
        string? resultReference,
        CancellationToken cancellationToken,
        PersistedProof? proof = null)
    {
        bool terminal = state is "succeeded" or "failed" or "partial" or "cancelled" or "expired";
        await using NpgsqlCommand update = transaction.CreateCommand(
            """
            with authority_time as materialized
            (
                select clock_timestamp() as authority_now
            )
            update control.user_operations as operation
            set state = @state,
                last_error_code = @last_error_code,
                result_reference = @result_reference,
                reconciliation_route_deployment_id = coalesce(
                    operation.reconciliation_route_deployment_id,
                    @proof_route_deployment_id),
                reconciliation_fence_generation = coalesce(
                    operation.reconciliation_fence_generation,
                    @proof_fence_generation),
                reconciliation_worker_assignment_id = coalesce(
                    operation.reconciliation_worker_assignment_id,
                    @proof_worker_assignment_id),
                reconciliation_worker_instance_id = coalesce(
                    operation.reconciliation_worker_instance_id,
                    @proof_worker_instance_id),
                claimed_by = null,
                claim_token = null,
                claim_expires_at = null,
                completed_at = case
                    when @terminal then authority_time.authority_now
                    else null
                end,
                row_version = operation.row_version + 1,
                updated_at = greatest(operation.updated_at, authority_time.authority_now)
            from authority_time
            where operation.tenant_id = @tenant_id
              and operation.id = @operation_id
              and operation.claim_token = @claim_token
              and operation.row_version = @expected_version
              and (@proof_route_deployment_id is null
                  or operation.reconciliation_route_deployment_id is null
                  or operation.reconciliation_route_deployment_id = @proof_route_deployment_id)
              and (@proof_fence_generation is null
                  or operation.reconciliation_fence_generation is null
                  or operation.reconciliation_fence_generation = @proof_fence_generation)
              and (@proof_worker_assignment_id is null
                  or operation.reconciliation_worker_assignment_id is null
                  or operation.reconciliation_worker_assignment_id = @proof_worker_assignment_id)
              and (@proof_worker_instance_id is null
                  or operation.reconciliation_worker_instance_id is null
                  or operation.reconciliation_worker_instance_id = @proof_worker_instance_id)
            returning operation.row_version, authority_time.authority_now
            """);
        update.Parameters.AddWithValue("state", NpgsqlDbType.Text, state);
        update.Parameters.AddWithValue(
            "last_error_code",
            NpgsqlDbType.Text,
            errorCode is null ? DBNull.Value : NormalizeError(errorCode, "operation_failed"));
        update.Parameters.AddWithValue(
            "result_reference",
            NpgsqlDbType.Text,
            resultReference is null ? DBNull.Value : resultReference);
        update.Parameters.AddWithValue("terminal", NpgsqlDbType.Boolean, terminal);
        update.Parameters.AddWithValue(
            "proof_route_deployment_id",
            NpgsqlDbType.Uuid,
            proof?.RouteDeploymentId is Guid routeDeploymentId ? routeDeploymentId : DBNull.Value);
        update.Parameters.AddWithValue(
            "proof_fence_generation",
            NpgsqlDbType.Bigint,
            proof?.FenceGeneration is long fenceGeneration ? fenceGeneration : DBNull.Value);
        update.Parameters.AddWithValue(
            "proof_worker_assignment_id",
            NpgsqlDbType.Uuid,
            proof?.WorkerAssignmentId is Guid assignmentId ? assignmentId : DBNull.Value);
        update.Parameters.AddWithValue(
            "proof_worker_instance_id",
            NpgsqlDbType.Uuid,
            proof?.WorkerInstanceId is Guid workerInstanceId ? workerInstanceId : DBNull.Value);
        update.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, operation.TenantId);
        update.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, operation.Id);
        update.Parameters.AddWithValue("claim_token", NpgsqlDbType.Uuid, claimToken);
        update.Parameters.AddWithValue("expected_version", NpgsqlDbType.Bigint, operation.RowVersion);
        long nextVersion;
        DateTimeOffset completionNow;
        await using (NpgsqlDataReader reader = await update.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("The user-operation reconciliation claim was lost.");
            }

            nextVersion = reader.GetInt64(0);
            completionNow = reader.GetFieldValue<DateTimeOffset>(1);
        }

        if (string.Equals(operation.State, state, StringComparison.Ordinal)
            && errorCode is null
            && resultReference is null)
        {
            return;
        }

        await AppendLifecycleEventAsync(
            transaction,
            operation,
            state,
            errorCode,
            resultReference,
            proof,
            nextVersion,
            completionNow,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task AppendLifecycleEventAsync(
        TenantPostgresTransaction transaction,
        PersistedOperation operation,
        string state,
        string? errorCode,
        string? resultReference,
        PersistedProof? proof,
        long nextVersion,
        DateTimeOffset eventTime,
        CancellationToken cancellationToken)
    {
        var safePayload = new
        {
            OperationId = operation.Id,
            operation.OperationType,
            operation.TargetType,
            operation.TargetId,
            State = state,
            ErrorCode = errorCode,
            ResultReference = resultReference,
            WorkerAssignmentId = proof?.WorkerAssignmentId ?? operation.DispatchWorkerAssignmentId,
            WorkerInstanceId = proof?.WorkerInstanceId ?? operation.DispatchWorkerInstanceId,
            RouteDeploymentId = proof?.RouteDeploymentId ?? operation.DispatchRouteDeploymentId,
            FenceGeneration = proof?.FenceGeneration ?? operation.DispatchFenceGeneration,
            operation.DispatchPolicySnapshotSha256
        };
        AuditOutcome outcome = state switch
        {
            "succeeded" => AuditOutcome.Succeeded,
            "failed" or "partial" or "expired" => AuditOutcome.Failed,
            "unknown" => AuditOutcome.Unknown,
            _ => AuditOutcome.Accepted
        };
        AuditEvent audit = AuditEvent.Create(
            operation.TenantId,
            WorkerDatabaseIdentity.ServiceActorId,
            AuditCategory.Operations,
            $"user_operation.{state}",
            operation.TargetType,
            operation.TargetId.ToString("D"),
            outcome,
            errorCode,
            operation.CorrelationId,
            operation.Id,
            safePayload,
            eventTime,
            PolicyAuditContext(operation, nextVersion));
        OutboxMessage message = OutboxMessage.Create(
            operation.TenantId,
            $"user_operation.{state}.v1",
            DispatchMessageAggregate,
            operation.Id.ToString("D"),
            safePayload,
            operation.CorrelationId,
            operation.Id,
            eventTime);
        await PostgresAuditOutboxWriter.AppendAsync(
            transaction,
            audit,
            message,
            cancellationToken).ConfigureAwait(false);
    }

    private static AuditEvidenceContext PolicyAuditContext(PersistedOperation operation, long afterVersion) =>
        new(
            Assurance: "workload",
            SourceNetworkClass: "unknown",
            EffectivePolicyDigest: operation.EffectivePolicyDigest,
            PolicyVersionWatermark: operation.PolicyVersionWatermark,
            PolicyInputSha256: operation.PolicyInputSha256,
            ResourceVersionBefore: operation.RowVersion,
            ResourceVersionAfter: afterVersion);

    private static string NormalizeProcessingError(string value)
    {
        string candidate = value.Trim().ToLowerInvariant();
        return candidate.Length is >= 1 and <= 100
            && char.IsAsciiLetter(candidate[0])
            && candidate.All(character => char.IsAsciiLetterOrDigit(character) || character == '_')
            ? candidate
            : "operation_processing_error";
    }

    private string AwaitingProofState(PersistedOperation operation, bool published = false) =>
        UserOperationDispatchGuard.AwaitingProofState(
            operation.State,
            operation.DispatchedAt ?? operation.CreatedAt,
            operation.AuthorizationNow,
            options.ProofUnknownAfter,
            published);

    private static void AddTargetParameters(NpgsqlCommand command, PersistedOperation operation)
    {
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, operation.TenantId);
        command.Parameters.AddWithValue("target_id", NpgsqlDbType.Uuid, operation.TargetId);
    }

    private static void AddDispatchRouteParameters(
        NpgsqlCommand command,
        PersistedOperation operation)
    {
        command.Parameters.AddWithValue(
            "dispatch_route_deployment_id",
            NpgsqlDbType.Uuid,
            operation.DispatchRouteDeploymentId is Guid routeDeploymentId
                ? routeDeploymentId
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "dispatch_fence_generation",
            NpgsqlDbType.Bigint,
            operation.DispatchFenceGeneration is long fenceGeneration
                ? fenceGeneration
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "dispatch_worker_assignment_id",
            NpgsqlDbType.Uuid,
            operation.DispatchWorkerAssignmentId is Guid assignmentId
                ? assignmentId
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "dispatch_worker_instance_id",
            NpgsqlDbType.Uuid,
            operation.DispatchWorkerInstanceId is Guid workerInstanceId
                ? workerInstanceId
                : DBNull.Value);
    }

    private static async Task AcquireAuthorityLockAsync(
        TenantPostgresTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            "select control.acquire_u0_authority_lock()");
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<DateTimeOffset> ReadAuthorityNowAsync(
        TenantPostgresTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            "select clock_timestamp()");
        object? value = await command.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);
        return value is DateTimeOffset authorityNow
            ? authorityNow.ToUniversalTime()
            : throw new InvalidOperationException(
                "PostgreSQL did not return the dispatch authority clock.");
    }

    private static DateTimeOffset Earliest(params DateTimeOffset[] values) => values.Min();

    private static bool IsSha256(string? value) =>
        value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool TryParseTargetObservation(
        string? targetType,
        string? json,
        out UserOperationTargetObservation? value)
    {
        value = null;
        if (targetType is null && json is null)
        {
            return true;
        }

        if (targetType is null || json is null)
        {
            return false;
        }

        try
        {
            value = UserOperationTargetObservation.ParseDatabaseJson(targetType, json);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidDataException)
        {
            return false;
        }
    }

    private static bool InvocationReconciliationShapeIsValid(
        PersistedOperation operation,
        string status,
        string attemptState,
        string? proofSource,
        string? outcome,
        string? observationSha256,
        DateTimeOffset? observedAt,
        DateTimeOffset? receivedAt,
        Guid? resultId,
        Guid? resultRecordId,
        string? requestSha256,
        string? targetType,
        Guid? targetId,
        UserOperationTargetObservation? targetObservation,
        string? projectionStatus,
        long? projectedTargetRowVersion,
        Guid routeDeploymentId,
        long fenceGeneration,
        Guid workerAssignmentId,
        Guid workerInstanceId)
    {
        bool terminal = status is
            "conclusive_projected_result" or
            "conclusive_diverged_result" or
            "projection_blocked";
        if (!terminal)
        {
            return proofSource is null
                && outcome is null
                && observationSha256 is null
                && observedAt is null
                && receivedAt is null
                && resultId is null
                && resultRecordId is null
                && requestSha256 is null
                && targetType is null
                && targetId is null
                && targetObservation is null
                && projectionStatus is null
                && projectedTargetRowVersion is null
                && (status != "awaiting_evidence"
                    || attemptState is not ("observed" or "not_sent"));
        }

        bool proofShapeValid = proofSource switch
        {
            "gateway_result_v5" or "reconciliation_result_v5" =>
                resultId is Guid persistedResultId
                    && persistedResultId != Guid.Empty
                    && resultRecordId is Guid persistedResultRecordId
                    && persistedResultRecordId != Guid.Empty
                    && IsSha256(requestSha256),
            "gateway_observation_receipt" =>
                resultId is null
                    && resultRecordId is null
                    && requestSha256 is null,
            _ => false
        };
        bool commonEvidenceValid = attemptState == "observed"
            && outcome is "succeeded" or "diverged"
            && IsSha256(observationSha256)
            && observedAt is not null
            && receivedAt is not null
            && observedAt.Value != default
            && receivedAt.Value != default
            && receivedAt.Value >= observedAt.Value
            && string.Equals(targetType, operation.TargetType, StringComparison.Ordinal)
            && targetId == operation.TargetId
            && targetObservation is not null
            && FixedDigestEquals(
                targetObservation.ComputeCanonicalSha256(),
                observationSha256)
            && TargetObservationIsConsistent(
                operation,
                outcome,
                targetObservation)
            && InvocationProofRouteIsValid(
                operation,
                proofSource,
                routeDeploymentId,
                fenceGeneration,
                workerAssignmentId,
                workerInstanceId)
            && proofShapeValid;
        if (!commonEvidenceValid)
        {
            return false;
        }

        return status switch
        {
            "conclusive_projected_result" => outcome == "succeeded"
                && projectionStatus is "projected" or "already_projected"
                && projectedTargetRowVersion is >= 0,
            "conclusive_diverged_result" => outcome == "diverged"
                && projectionStatus == "not_applicable"
                && projectedTargetRowVersion is null,
            "projection_blocked" => outcome == "succeeded"
                && projectionStatus == "blocked"
                && projectedTargetRowVersion is null,
            _ => false
        };
    }

    private static bool TargetObservationIsConsistent(
        PersistedOperation operation,
        string? outcome,
        UserOperationTargetObservation observation)
    {
        if (IsSha256(operation.DispatchTargetBindingSha256))
        {
            try
            {
                observation.ValidateResultConsistency(
                    operation.TargetType,
                    operation.RequestedTargetState,
                    operation.DispatchTargetBindingSha256!,
                    outcome == "succeeded"
                        ? UserOperationObservationOutcome.Succeeded
                        : UserOperationObservationOutcome.Diverged);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        // Requested.v4 keeps the immutable target-binding digest on the
        // current attempt rather than duplicating it onto the legacy operation
        // columns. The SECURITY DEFINER reconciliation function has already
        // validated that exact digest binding. Repeat every consistency check
        // available from the closed returned evidence without inventing a
        // replacement digest.
        bool visibleTargetMatches = observation switch
        {
            UserOperationBrokerTargetObservation broker => string.Equals(
                operation.RequestedTargetState,
                $"{broker.AccountState}:{broker.CredentialState}",
                StringComparison.Ordinal),
            UserOperationDeploymentTargetObservation deployment =>
                string.Equals(
                    operation.RequestedTargetState,
                    deployment.ObservedState,
                    StringComparison.Ordinal)
                && string.Equals(
                    operation.RequestedTargetState,
                    deployment.BrokerExecutionState,
                    StringComparison.Ordinal)
                && (operation.RequestedTargetState != "stopped"
                    || deployment.BrokerPositionState == "flat"),
            _ => false
        };
        return outcome switch
        {
            "succeeded" => visibleTargetMatches,
            "diverged" when observation is UserOperationBrokerTargetObservation =>
                !visibleTargetMatches,
            "diverged" => true,
            _ => false
        };
    }

    private static bool InvocationProofRouteIsValid(
        PersistedOperation operation,
        string? proofSource,
        Guid routeDeploymentId,
        long fenceGeneration,
        Guid workerAssignmentId,
        Guid workerInstanceId)
    {
        if (proofSource == "reconciliation_result_v5")
        {
            // The SECURITY DEFINER protocol function binds this route to the
            // consumed challenge. The operation is bound independently below
            // when the terminal lifecycle evidence is persisted.
            return true;
        }

        bool legacyRouteUnpopulated = operation.DispatchRouteDeploymentId is null
            && operation.DispatchFenceGeneration is null
            && operation.DispatchWorkerAssignmentId is null
            && operation.DispatchWorkerInstanceId is null;
        return legacyRouteUnpopulated
            || operation.DispatchRouteDeploymentId == routeDeploymentId
                && operation.DispatchFenceGeneration == fenceGeneration
                && operation.DispatchWorkerAssignmentId == workerAssignmentId
                && operation.DispatchWorkerInstanceId == workerInstanceId;
    }

    private static string CreateResultCapability()
    {
        byte[] randomBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        try
        {
            string value = CanonicalBase64Url.Encode(randomBytes);
            return CanonicalBase64Url.IsEncodedByteCount(value, 32)
                ? value
                : throw new InvalidOperationException(
                    "The broker-result capability encoding was invalid.");
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(randomBytes);
        }
    }

    private static string Sha256Utf8(string value) => Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(value)))
        .ToLowerInvariant();

    private static bool FixedDigestEquals(string? first, string? second)
    {
        if (!IsSha256(first) || !IsSha256(second))
        {
            return false;
        }

        byte[] firstBytes = Convert.FromHexString(first!);
        byte[] secondBytes = Convert.FromHexString(second!);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(firstBytes, secondBytes);
    }

    private static string NormalizeError(string? value, string fallback)
    {
        string candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
        return candidate.Length <= 200
            && candidate.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.')
            ? candidate
            : fallback;
    }

    private static string CreateRiskPolicySignaturePayload(
        Guid tenantId,
        Guid versionId,
        Guid policyId,
        int version,
        string policyDigest) => CanonicalJson.Serialize(new
        {
            Contract = "yo4x.risk-policy.v1",
            TenantId = tenantId.ToString("D"),
            VersionId = versionId.ToString("D"),
            PolicyId = policyId.ToString("D"),
            Version = version,
            PolicyDigest = policyDigest
        });

    private static string CreateExecutionSafetyPolicySignaturePayload(
        Guid tenantId,
        Guid policyId,
        long version,
        string scopeType,
        string? scopeId,
        string policyDigest,
        string reason,
        Guid? incidentId,
        Guid ownerId,
        DateTimeOffset? authorityExpiresAt,
        DateTimeOffset reviewDeadline) => CanonicalJson.Serialize(new
        {
            Contract = "yo4x.execution-safety-policy.v1",
            TenantId = tenantId.ToString("D"),
            PolicyId = policyId.ToString("D"),
            Version = version,
            ScopeType = scopeType,
            ScopeId = scopeId,
            PolicyDigest = policyDigest,
            Reason = reason,
            IncidentId = incidentId?.ToString("D"),
            OwnerId = ownerId.ToString("D"),
            AuthorityExpiresAt = authorityExpiresAt?.ToUniversalTime().ToString("O"),
            ReviewDeadline = reviewDeadline.ToUniversalTime().ToString("O")
        });

    private sealed record OperationCandidate(
        Guid Id,
        Guid TenantId,
        Guid CorrelationId,
        long RowVersion,
        bool ForDispatch,
        string OperationType)
    {
        public bool IsProtective => UserOperationDispatchGuard.IsProtective(OperationType);
    }

    private sealed record InvocationAttemptCreation(
        string Status,
        Guid AttemptId,
        Guid DispatchMessageId,
        int AttemptNumber,
        string CommandSha256,
        DateTimeOffset ExecuteNotAfter,
        DateTimeOffset ResultCapabilityExpiresAt,
        Guid RouteDeploymentId,
        long FenceGeneration,
        Guid WorkerAssignmentId,
        Guid WorkerInstanceId);

    private sealed record InvocationAttemptReconciliation(
        string Status,
        Guid AttemptId,
        int AttemptNumber,
        string AttemptState,
        long StateVersion,
        string? ProofSource,
        string? Outcome,
        string? ObservationSha256,
        DateTimeOffset? ObservedAt,
        DateTimeOffset? ReceivedAt,
        Guid? ResultId,
        Guid? ResultRecordId,
        string? RequestSha256,
        Guid RouteDeploymentId,
        long FenceGeneration,
        Guid WorkerAssignmentId,
        Guid WorkerInstanceId,
        string? TargetType,
        Guid? TargetId,
        UserOperationTargetObservation? TargetObservation,
        string? ProjectionStatus,
        long? ProjectedTargetRowVersion);

    private sealed record PersistedOperation(
        Guid Id,
        Guid TenantId,
        string OperationType,
        string TargetType,
        Guid TargetId,
        Guid UserId,
        Guid IdempotencyRecordId,
        long? ExpectedResourceVersion,
        long SubmittedResourceVersion,
        string RequestedTargetState,
        Guid CorrelationId,
        string State,
        string? EffectivePolicyDigest,
        string? PolicyVersionWatermark,
        string? PolicyInputSha256,
        Guid? DispatchMessageId,
        long? DispatchFenceGeneration,
        Guid? DispatchRouteDeploymentId,
        Guid? DispatchWorkerAssignmentId,
        Guid? DispatchWorkerInstanceId,
        string? DispatchTargetBindingSha256,
        string? DispatchPolicySnapshotSha256,
        long RowVersion,
        DateTimeOffset CreatedAt,
        DateTimeOffset? DispatchedAt,
        short? InvocationProtocolVersion,
        Guid? CurrentInvocationAttemptId,
        DateTimeOffset AuthorizationNow);

    private sealed record TargetSnapshot(
        long ResourceVersion,
        string CurrentState,
        string DispatchComparableState,
        string ReconciledComparableState,
        Guid? RouteDeploymentId,
        long? FenceGeneration,
        Guid? WorkerAssignmentId,
        Guid? WorkerInstanceId,
        DateTimeOffset? AssignmentLeaseExpiresAt,
        object RedactedBinding);

    private sealed record DispatchPolicyDecision(
        bool Allowed,
        string? ErrorCode,
        string SnapshotSha256,
        string? EvaluationEvidenceSha256)
    {
        public static DispatchPolicyDecision Allow(string snapshotSha256, string? evaluationEvidenceSha256) =>
            new(true, null, snapshotSha256, evaluationEvidenceSha256);

        public static DispatchPolicyDecision Deny(string errorCode) =>
            new(false, errorCode, new string('0', 64), null);
    }

    private sealed record StartEvaluation(
        string InputSnapshotJson,
        string ApplicablePoliciesJson,
        string EffectiveVectorJson,
        string RuleResultsJson,
        string EffectivePolicyDigest,
        string PolicyVersionWatermark,
        string InputSha256,
        string EvidenceSha256)
    {
        public bool HasValidCanonicalDigest()
        {
            if (!IsSha256(EffectivePolicyDigest)
                || !IsSha256(PolicyVersionWatermark)
                || !IsSha256(InputSha256)
                || !IsSha256(EvidenceSha256))
            {
                return false;
            }

            JsonNode? input = JsonNode.Parse(InputSnapshotJson);
            JsonNode? policies = JsonNode.Parse(ApplicablePoliciesJson);
            JsonNode? vector = JsonNode.Parse(EffectiveVectorJson);
            JsonNode? rules = JsonNode.Parse(RuleResultsJson);
            return input is not null
                && policies is not null
                && vector is not null
                && rules is not null
                && FixedDigestEquals(CanonicalJson.Sha256(input), InputSha256)
                && FixedDigestEquals(
                    CanonicalJson.Sha256(new
                    {
                        InputSnapshot = input,
                        ApplicablePolicies = policies,
                        EffectiveVector = vector,
                        RuleResults = rules,
                        EffectivePolicyDigest,
                        PolicyVersionWatermark,
                        InputSha256
                    }),
                    EvidenceSha256);
        }
    }

    private sealed record CurrentBaseline(
        Guid Id,
        int Version,
        string Digest,
        string CanonicalInputDigest,
        string SignatureAlgorithm,
        string SignatureSha256,
        string SigningKeyId,
        bool SignatureValid);

    private sealed record CurrentPolicyBinding(
        Guid Id,
        long Version,
        string ScopeType,
        string? ScopeId,
        bool AllowNewDeployment,
        bool AllowStrategySignals,
        bool AllowExposureIncrease,
        bool AllowExposureReduction,
        bool AllowProtection,
        bool AllowPendingOrderCancellation,
        bool AllowEmergencyClose,
        string LeaseMode,
        IReadOnlyList<string> WorkerActions,
        string CredentialMode,
        string PackageEligibility,
        string Digest,
        string SignatureAlgorithm,
        string SignatureSha256,
        string SigningKeyId,
        bool IntegrityValid)
    {
        public static CurrentPolicyBinding Create(
            Guid id,
            long version,
            string scopeType,
            string? scopeId,
            bool allowNewDeployment,
            bool allowStrategySignals,
            bool allowExposureIncrease,
            bool allowExposureReduction,
            bool allowProtection,
            bool allowPendingOrderCancellation,
            bool allowEmergencyClose,
            string leaseMode,
            IEnumerable<string> workerActions,
            string credentialMode,
            string packageEligibility,
            string digest,
            string signatureAlgorithm,
            string signatureSha256,
            string signingKeyId,
            string reason,
            Guid? incidentId,
            Guid ownerId,
            DateTimeOffset? authorityExpiresAt,
            DateTimeOffset reviewDeadline,
            byte[] signature,
            Guid tenantId,
            WorkerPolicySignatureTrustStore trustStore)
        {
            string[] normalizedActions = workerActions
                .Distinct(StringComparer.Ordinal)
                .OrderBy(ActionOrder)
                .ToArray();
            string computedDigest = CanonicalJson.Sha256(new
            {
                AllowNewDeployment = allowNewDeployment,
                AllowStrategySignals = allowStrategySignals,
                AllowExposureIncrease = allowExposureIncrease,
                AllowExposureReduction = allowExposureReduction,
                AllowProtection = allowProtection,
                AllowPendingOrderCancellation = allowPendingOrderCancellation,
                AllowEmergencyClose = allowEmergencyClose,
                LeaseMode = ToEvidenceEnum(leaseMode),
                WorkerActions = normalizedActions.Select(ToEvidenceEnum).ToArray(),
                CredentialMode = ToEvidenceEnum(credentialMode),
                PackageEligibility = ToEvidenceEnum(packageEligibility)
            });
            bool signatureValid = trustStore.Verify(
                signingKeyId,
                signatureAlgorithm,
                signature,
                signatureSha256,
                CreateExecutionSafetyPolicySignaturePayload(
                    tenantId,
                    id,
                    version,
                    scopeType,
                    scopeId,
                    digest,
                    reason,
                    incidentId,
                    ownerId,
                    authorityExpiresAt,
                    reviewDeadline));
            return new CurrentPolicyBinding(
                id,
                version,
                scopeType,
                scopeId,
                allowNewDeployment,
                allowStrategySignals,
                allowExposureIncrease,
                allowExposureReduction,
                allowProtection,
                allowPendingOrderCancellation,
                allowEmergencyClose,
                leaseMode,
                normalizedActions,
                credentialMode,
                packageEligibility,
                digest,
                signatureAlgorithm,
                signatureSha256,
                signingKeyId,
                FixedDigestEquals(computedDigest, digest) && signatureValid);
        }

        public bool Matches(JsonElement expected)
        {
            if (ReadGuid(expected, "id") != Id
                || ReadInt64(expected, "version") != Version
                || ReadString(expected, "scopeType") != ScopeType
                || ReadString(expected, "scopeId") != ScopeId
                || !FixedDigestEquals(ReadString(expected, "digest"), Digest)
                || ReadString(expected, "signatureAlgorithm") != SignatureAlgorithm
                || !FixedDigestEquals(ReadString(expected, "signatureSha256"), SignatureSha256)
                || ReadString(expected, "signingKeyId") != SigningKeyId
                || !expected.TryGetProperty("vector", out JsonElement vector))
            {
                return false;
            }

            string[] expectedActions = vector.TryGetProperty("workerActions", out JsonElement actions)
                && actions.ValueKind == JsonValueKind.Array
                    ? actions.EnumerateArray()
                        .Where(static item => item.ValueKind == JsonValueKind.String)
                        .Select(static item => item.GetString()!)
                        .OrderBy(ActionOrder)
                        .ToArray()
                    : [];
            return ReadOptionalBoolean(vector, "allowNewDeployment") == AllowNewDeployment
                && ReadOptionalBoolean(vector, "allowStrategySignals") == AllowStrategySignals
                && ReadOptionalBoolean(vector, "allowExposureIncrease") == AllowExposureIncrease
                && ReadOptionalBoolean(vector, "allowExposureReduction") == AllowExposureReduction
                && ReadOptionalBoolean(vector, "allowProtection") == AllowProtection
                && ReadOptionalBoolean(vector, "allowPendingOrderCancellation") == AllowPendingOrderCancellation
                && ReadOptionalBoolean(vector, "allowEmergencyClose") == AllowEmergencyClose
                && ReadString(vector, "leaseMode") == ToEvidenceEnum(LeaseMode)
                && expectedActions.SequenceEqual(WorkerActions.Select(ToEvidenceEnum), StringComparer.Ordinal)
                && ReadString(vector, "credentialMode") == ToEvidenceEnum(CredentialMode)
                && ReadString(vector, "packageEligibility") == ToEvidenceEnum(PackageEligibility);
        }

        private static bool? ReadOptionalBoolean(JsonElement value, string name) =>
            value.TryGetProperty(name, out JsonElement property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? property.GetBoolean()
                : null;

        private static int ActionOrder(string value) => value switch
        {
            "DRAIN" or "Drain" => 0,
            "STOP_AFTER_FLAT" or "StopAfterFlat" => 1,
            "FENCE" or "Fence" => 2,
            "REPLACE" or "Replace" => 3,
            _ => 99
        };

        private static string ToEvidenceEnum(string value) => value switch
        {
            "NORMAL" => "Normal",
            "RENEW_RESTRICTED" => "RenewRestricted",
            "REVOKE" => "Revoke",
            "DRAIN" => "Drain",
            "FENCE" => "Fence",
            "REPLACE" => "Replace",
            "STOP_AFTER_FLAT" => "StopAfterFlat",
            "DISABLE_NEW_USE" => "DisableNewUse",
            "REVOKE_REFERENCE" => "RevokeReference",
            "ELIGIBLE" => "Eligible",
            "NO_NEW_ASSIGNMENT" => "NoNewAssignment",
            "QUARANTINED" => "Quarantined",
            _ => value
        };
    }

    private sealed record PersistedProof(
        string Outcome,
        string? ErrorCode,
        string Reference,
        Guid? WorkerAssignmentId = null,
        Guid? WorkerInstanceId = null,
        Guid? BrokerResultId = null,
        Guid? RouteDeploymentId = null,
        long? FenceGeneration = null);
}
