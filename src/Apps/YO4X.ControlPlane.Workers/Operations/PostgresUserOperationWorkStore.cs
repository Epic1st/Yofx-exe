using Npgsql;
using NpgsqlTypes;
using System.Text.Json;
using System.Text.Json.Nodes;
using YO4X.Audit;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Workers.Outbox;
using YO4X.Outbox;
using YO4X.Persistence.Postgres;

namespace YO4X.ControlPlane.Workers.Operations;

internal sealed class PostgresUserOperationWorkStore(
    PostgresDatabase database,
    PostgresWorkerReadiness readiness,
    PostgresWorkerTenantCatalog tenantCatalog,
    ControlWorkOptions options,
    OutboxWorkerIdentity workerIdentity,
    WorkerPolicySignatureTrustStore policyTrustStore) : IUserOperationWorkStore
{
    private const string DispatchMessageAggregate = "user_operation";

    public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        readiness.IsReadyAsync(cancellationToken);

    public async Task<ControlWorkCycleResult> RunCycleAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!await IsAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            return new ControlWorkCycleResult(0, 0, 0, 0);
        }

        // The scheduler supplies this for the shared worker interface only;
        // PostgreSQL owns every authorization, freshness, and lifecycle instant.
        _ = now.ToUniversalTime();
        IReadOnlyList<Guid> tenantIds = await tenantCatalog.GetTenantIdsAsync(cancellationToken)
            .ConfigureAwait(false);
        int examined = 0;
        int changed = 0;
        int failed = 0;
        foreach (Guid tenantId in tenantIds)
        {
            IReadOnlyList<OperationCandidate> dispatchCandidates = await ReadCandidatesAsync(
                tenantId,
                dispatch: true,
                cancellationToken).ConfigureAwait(false);
            foreach (OperationCandidate candidate in dispatchCandidates)
            {
                examined++;
                try
                {
                    if (await DispatchAsync(candidate, cancellationToken).ConfigureAwait(false))
                    {
                        changed++;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (NpgsqlException)
                {
                    failed++;
                }
                catch (TimeoutException)
                {
                    failed++;
                }
            }

            IReadOnlyList<OperationCandidate> reconciliationCandidates = await ReadCandidatesAsync(
                tenantId,
                dispatch: false,
                cancellationToken).ConfigureAwait(false);
            foreach (OperationCandidate candidate in reconciliationCandidates)
            {
                examined++;
                try
                {
                    if (await ReconcileAsync(candidate, cancellationToken).ConfigureAwait(false))
                    {
                        changed++;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (NpgsqlException)
                {
                    failed++;
                }
                catch (TimeoutException)
                {
                    failed++;
                }
            }
        }

        return new ControlWorkCycleResult(tenantIds.Count, examined, changed, failed);
    }

    private async Task<IReadOnlyList<OperationCandidate>> ReadCandidatesAsync(
        Guid tenantId,
        bool dispatch,
        CancellationToken cancellationToken)
    {
        Guid correlationId = Guid.CreateVersion7();
        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(
                PostgresWorkerTenantCatalog.CreateContext(tenantId, correlationId),
                cancellationToken).ConfigureAwait(false);
        string states = dispatch
            ? "(state = 'accepted' or (state = 'dispatching' and claim_expires_at <= clock_timestamp()))"
            : "(state in ('propagating', 'reconciling', 'unknown') and (claim_token is null or claim_expires_at <= clock_timestamp()))";
        await using NpgsqlCommand command = transaction.CreateCommand($$"""
            select id, tenant_id, correlation_id, row_version
            from control.user_operations
            where tenant_id = @tenant_id
              and {{states}}
            order by
                case operation_type
                    when 'broker_account.delete' then 0
                    when 'broker_account.disable' then 1
                    when 'deployment.stop_after_flat' then 0
                    when 'deployment.close_only' then 1
                    else 2
                end,
                created_at,
                id
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
                    reader.GetInt64(3)));
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

        if (!UserOperationDispatchGuard.HasCompleteRoute(
            snapshot.RouteDeploymentId,
            snapshot.FenceGeneration,
            snapshot.WorkerAssignmentId,
            snapshot.WorkerInstanceId))
        {
            if (UserOperationDispatchGuard.RouteWaitExpired(
                operation.CreatedAt,
                operation.AuthorizationNow,
                options.OperationExpiresAfter))
            {
                await FinishAsync(
                    transaction,
                    operation,
                    claimToken,
                    "expired",
                    "dispatch_route_timeout",
                    null,
                    cancellationToken).ConfigureAwait(false);
            }

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

        DateTimeOffset dispatchNow = operation.AuthorizationNow;

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
            operation.CreatedAt,
            dispatchNow);
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
            snapshot.WorkerInstanceId
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

        string? dispatchState = await ReadDispatchStateAsync(transaction, operation, cancellationToken)
            .ConfigureAwait(false);
        if (dispatchState is null)
        {
            await FinishAsync(
                transaction,
                operation,
                claimToken,
                "failed",
                "dispatch_evidence_missing",
                null,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (dispatchState == "dead_letter")
        {
            await FinishAsync(
                transaction,
                operation,
                claimToken,
                "failed",
                "dispatch_dead_lettered",
                null,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (dispatchState != "published")
        {
            string nextState = AgeState(operation);
            await FinishAsync(
                transaction,
                operation,
                claimToken,
                nextState,
                nextState == "expired" ? "dispatch_timeout" : null,
                null,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return nextState != operation.State;
        }

        TargetSnapshot? currentTarget = await ReadTargetSnapshotAsync(
            transaction,
            operation,
            cancellationToken).ConfigureAwait(false);
        if (currentTarget is null
            || operation.DispatchTargetBindingSha256 is null
            || !UserOperationDispatchGuard.IsReconciliationBindingCurrent(
                operation.OperationType,
                operation.RequestedTargetState,
                operation.SubmittedResourceVersion,
                currentTarget.ResourceVersion,
                currentTarget.DispatchComparableState,
                operation.DispatchFenceGeneration,
                currentTarget.FenceGeneration,
                operation.DispatchWorkerAssignmentId,
                operation.DispatchWorkerInstanceId,
                operation.DispatchRouteDeploymentId,
                currentTarget.RouteDeploymentId,
                currentTarget.WorkerAssignmentId,
                currentTarget.WorkerInstanceId,
                operation.DispatchTargetBindingSha256,
                CanonicalJson.Sha256(currentTarget.RedactedBinding)))
        {
            await FinishAsync(
                transaction,
                operation,
                claimToken,
                "cancelled",
                "operation_superseded_after_dispatch",
                null,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        PersistedProof? proof = operation.TargetType == "deployment"
            ? await ReadDeploymentProofAsync(
                transaction,
                operation,
                currentTarget,
                cancellationToken).ConfigureAwait(false)
            : await ReadBrokerProofAsync(
                transaction,
                operation,
                currentTarget,
                cancellationToken).ConfigureAwait(false);
        if (operation.TargetType == "broker_account"
            && proof is { Outcome: "succeeded", BrokerResultId: not null }
            && !await ApplyConfirmedBrokerResultAsync(
                transaction,
                operation,
                proof.BrokerResultId.Value,
                cancellationToken).ConfigureAwait(false))
        {
            proof = new PersistedProof(
                "unknown",
                "broker_projection_conflict",
                proof.Reference,
                proof.WorkerAssignmentId,
                proof.WorkerInstanceId,
                proof.BrokerResultId);
        }
        string next = proof?.Outcome ?? AgeState(operation, published: true);
        string? errorCode = proof?.ErrorCode;
        if (proof is null && next == "expired")
        {
            errorCode = "reconciliation_timeout";
        }

        await FinishAsync(
            transaction,
            operation,
            claimToken,
            next,
            errorCode,
            proof?.Reference,
            cancellationToken,
            proof).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return next != operation.State;
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
                      and operation.claim_expires_at <= authority_time.authority_now)
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
                operation.row_version, operation.created_at, authority_time.authority_now
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
                operation.row_version, operation.created_at, authority_time.authority_now
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

        return new PersistedOperation(
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
            reader.GetFieldValue<DateTimeOffset>(24));
    }

    private static async Task<TargetSnapshot?> ReadTargetSnapshotAsync(
        TenantPostgresTransaction transaction,
        PersistedOperation operation,
        CancellationToken cancellationToken)
    {
        if (operation.TargetType == "broker_account")
        {
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                select
                    account.row_version, account.state, account.credential_state,
                    account.environment, account.binding_fingerprint,
                    route.deployment_id, route.fence_generation,
                    route.assignment_id, route.worker_node_id
                from operations.broker_accounts as account
                left join lateral
                (
                    select
                        deployment.id as deployment_id,
                        deployment.fence_generation,
                        assignment.id as assignment_id,
                        assignment.worker_node_id
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
                      and
                      (
                          @dispatch_worker_assignment_id is not null
                          or (assignment.state in ('reconciliation_only', 'active')
                              and assignment.lease_expires_at > transaction_timestamp())
                      )
                    order by assignment.lease_expires_at desc, assignment.id
                    limit 1
                ) as route on true
                where account.tenant_id = @tenant_id and account.id = @target_id
                for share of account
                """);
            AddTargetParameters(command, operation);
            AddDispatchRouteParameters(command, operation);
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
            select
                row_version, desired_state, observed_state, fence_generation,
                broker_account_id, strategy_version_id, risk_policy_version_id,
                gateway_artifact_id, gateway_digest, strategy_package_digest,
                runtime_digest, configuration_sha256, binding_evidence_sha256,
                assignment.id, assignment.worker_node_id
            from operations.deployments as deployment
            left join lateral
            (
                select id, worker_node_id
                from operations.worker_assignments
                where tenant_id = deployment.tenant_id
                  and deployment_id = deployment.id
                  and fence_generation = deployment.fence_generation
                  and (@dispatch_worker_assignment_id is null
                      or id = @dispatch_worker_assignment_id)
                  and (@dispatch_worker_instance_id is null
                      or worker_node_id = @dispatch_worker_instance_id)
                  and
                  (
                      @operation_type <> 'deployment.start'
                      or (state in ('reconciliation_only', 'active')
                          and lease_expires_at > transaction_timestamp())
                  )
                order by id desc
                limit 1
            ) as assignment on true
            where deployment.tenant_id = @tenant_id and deployment.id = @target_id
            for share of deployment
            """))
        {
            AddTargetParameters(command, operation);
            AddDispatchRouteParameters(command, operation);
            command.Parameters.AddWithValue("operation_type", NpgsqlDbType.Text, operation.OperationType);
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
            $"yo4x.{operation.OperationType.Replace('_', '-')}.requested.v1");
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
    }

    private static async Task<PersistedProof?> ReadDeploymentProofAsync(
        TenantPostgresTransaction transaction,
        PersistedOperation operation,
        TargetSnapshot currentTarget,
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
                broker_position_state
            from operations.deployment_reconciliations
            where tenant_id = @tenant_id
              and deployment_id = @target_id
              and completed_at is not null
              and dispatch_message_id = @dispatch_message_id
            order by completed_at desc, id
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
        string expectedObservedState = operation.RequestedTargetState;
        string expectedBrokerState = operation.OperationType switch
        {
            "deployment.start" => "running",
            "deployment.close_only" => "close_only",
            "deployment.stop_after_flat" => "stopped",
            _ => throw new InvalidOperationException("A persisted deployment operation is invalid.")
        };
        string reference = $"deployment-reconciliation/{proofId:D}";
        bool commonBindingValid = dispatchMessageId == operation.DispatchMessageId
            && submittedResourceVersion == operation.SubmittedResourceVersion
            && string.Equals(requestedTargetState, operation.RequestedTargetState, StringComparison.Ordinal)
            && fenceGeneration == operation.DispatchFenceGeneration
            && fenceGeneration == currentTarget.FenceGeneration
            && assignmentId is not null
            && workerInstanceId is not null
            && assignmentId == currentTarget.WorkerAssignmentId
            && workerInstanceId == currentTarget.WorkerInstanceId
            && FixedDigestEquals(policySnapshotSha256 ?? string.Empty, operation.DispatchPolicySnapshotSha256)
            && await AssignmentMatchesAsync(
                transaction,
                operation,
                fenceGeneration!.Value,
                assignmentId.Value,
                workerInstanceId.Value,
                cancellationToken).ConfigureAwait(false);
        if (!commonBindingValid)
        {
            return null;
        }

        if (state == "reconciled"
            && FixedDigestEquals(desiredDigest, observedDigest)
            && IsSha256(brokerDigest)
            && IsSha256(runtimeEvidenceDigest)
            && brokerConfirmed
            && string.Equals(observedState, expectedObservedState, StringComparison.Ordinal)
            && string.Equals(brokerExecutionState, expectedBrokerState, StringComparison.Ordinal)
            && (operation.OperationType != "deployment.stop_after_flat"
                || string.Equals(brokerPositionState, "flat", StringComparison.Ordinal)))
        {
            return new PersistedProof(
                "succeeded",
                null,
                reference,
                assignmentId,
                workerInstanceId);
        }

        return state switch
        {
            "failed" => new PersistedProof("failed", "runtime_reconciliation_failed", reference),
            "diverged" => new PersistedProof("partial", "runtime_reconciliation_diverged", reference),
            "unknown" => new PersistedProof("unknown", "runtime_reconciliation_unknown", reference),
            _ => null
        };
    }

    private static async Task<PersistedProof?> ReadBrokerProofAsync(
        TenantPostgresTransaction transaction,
        PersistedOperation operation,
        TargetSnapshot currentTarget,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select
                id, proof_kind, outcome, evidence_sha256, error_code,
                dispatch_message_id, submitted_resource_version,
                requested_target_state, policy_snapshot_sha256,
                broker_confirmed, account_state, credential_state,
                route_deployment_id, generation,
                worker_assignment_id, worker_instance_id
            from operations.user_operation_results
            where tenant_id = @tenant_id
              and operation_id = @operation_id
              and broker_account_id = @target_id
              and dispatch_message_id = @dispatch_message_id
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
        string expectedKind = operation.OperationType switch
        {
            "broker_account.connection_test" => "connection_verified",
            "broker_account.credential_rotation" => "credential_rotated",
            "broker_account.disable" => "account_disabled",
            "broker_account.delete" => "credential_deleted",
            _ => throw new InvalidOperationException("A persisted broker-account operation is invalid.")
        };
        string brokerResultState = $"{accountState}:{credentialState}";
        if (!string.Equals(proofKind, expectedKind, StringComparison.Ordinal)
            || !IsSha256(evidenceDigest)
            || dispatchMessageId != operation.DispatchMessageId
            || submittedResourceVersion != operation.SubmittedResourceVersion
            || !string.Equals(requestedTargetState, operation.RequestedTargetState, StringComparison.Ordinal)
            || !FixedDigestEquals(policySnapshotSha256 ?? string.Empty, operation.DispatchPolicySnapshotSha256)
            || routeDeploymentId != operation.DispatchRouteDeploymentId
            || routeDeploymentId != currentTarget.RouteDeploymentId
            || generation != operation.DispatchFenceGeneration
            || generation != currentTarget.FenceGeneration
            || assignmentId != operation.DispatchWorkerAssignmentId
            || assignmentId != currentTarget.WorkerAssignmentId
            || workerInstanceId != operation.DispatchWorkerInstanceId
            || workerInstanceId != currentTarget.WorkerInstanceId
            || outcome == "succeeded"
               && !string.Equals(brokerResultState, operation.RequestedTargetState, StringComparison.Ordinal))
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
                assignmentId,
                workerInstanceId,
                proofId),
            "failed" => new PersistedProof("failed", NormalizeError(resultCode, "runtime_operation_failed"), reference),
            _ => null
        };
    }

    private static async Task<bool> AssignmentMatchesAsync(
        TenantPostgresTransaction transaction,
        PersistedOperation operation,
        long fenceGeneration,
        Guid assignmentId,
        Guid workerInstanceId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select exists
            (
                select 1
                from operations.worker_assignments
                where tenant_id = @tenant_id
                  and deployment_id = @deployment_id
                  and id = @assignment_id
                  and worker_node_id = @worker_instance_id
                  and fence_generation = @fence_generation
                  and
                  (
                      @operation_type <> 'deployment.start'
                      or (state in ('reconciliation_only', 'active')
                          and lease_expires_at > clock_timestamp())
                  )
            )
            """);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, operation.TenantId);
        command.Parameters.AddWithValue("deployment_id", NpgsqlDbType.Uuid, operation.TargetId);
        command.Parameters.AddWithValue("assignment_id", NpgsqlDbType.Uuid, assignmentId);
        command.Parameters.AddWithValue("worker_instance_id", NpgsqlDbType.Uuid, workerInstanceId);
        command.Parameters.AddWithValue("fence_generation", NpgsqlDbType.Bigint, fenceGeneration);
        command.Parameters.AddWithValue("operation_type", NpgsqlDbType.Text, operation.OperationType);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true;
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
            operation.DispatchFenceGeneration,
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
            completionNow,
            PolicyAuditContext(operation, nextVersion));
        OutboxMessage message = OutboxMessage.Create(
            operation.TenantId,
            $"user_operation.{state}.v1",
            DispatchMessageAggregate,
            operation.Id.ToString("D"),
            safePayload,
            operation.CorrelationId,
            operation.Id,
            completionNow);
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

    private string AgeState(PersistedOperation operation, bool published = false)
    {
        TimeSpan age = operation.AuthorizationNow - operation.CreatedAt;
        if (age >= options.OperationExpiresAfter)
        {
            return "expired";
        }

        if (published && age >= options.ProofUnknownAfter)
        {
            return "unknown";
        }

        return published ? "reconciling" : operation.State;
    }

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

    private static bool IsSha256(string? value) =>
        value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

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

    private sealed record OperationCandidate(Guid Id, Guid TenantId, Guid CorrelationId, long RowVersion);

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
        Guid? BrokerResultId = null);
}
