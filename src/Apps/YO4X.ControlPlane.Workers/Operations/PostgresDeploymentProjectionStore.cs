using Npgsql;
using NpgsqlTypes;
using YO4X.Audit;
using YO4X.Outbox;
using YO4X.Persistence.Postgres;

namespace YO4X.ControlPlane.Workers.Operations;

internal sealed class PostgresDeploymentProjectionStore(
    PostgresDatabase database,
    PostgresWorkerReadiness readiness,
    PostgresWorkerTenantCatalog tenantCatalog,
    ControlWorkOptions options) : IDeploymentProjectionStore
{
    private const string AdvanceDeploymentScanSql = """
        with locked_cursor as materialized
        (
            select last_deployment_id, rotation_count
            from control.deployment_scan_cursors
            where tenant_id = @tenant_id
            for update
        ),
        candidate as materialized
        (
            select
                deployment.id,
                deployment.tenant_id,
                locked_cursor.last_deployment_id is not null
                    and deployment.id <= locked_cursor.last_deployment_id
                    as completes_rotation,
                locked_cursor.rotation_count
                    + case
                        when locked_cursor.last_deployment_id is not null
                            and deployment.id <= locked_cursor.last_deployment_id
                        then 1
                        else 0
                      end as next_rotation_count
            from locked_cursor
            cross join lateral
            (
                select id, tenant_id
                from operations.deployments
                where tenant_id = @tenant_id
                  and desired_state <> 'draft'
                order by
                    case
                        when locked_cursor.last_deployment_id is not null
                            and id <= locked_cursor.last_deployment_id
                        then 1
                        else 0
                    end,
                    id
                limit 1
            ) as deployment
        ),
        eligible as materialized
        (
            select
                id,
                tenant_id,
                completes_rotation,
                next_rotation_count
            from candidate
            where @rotation_ceiling is null
               or next_rotation_count <= @rotation_ceiling
        ),
        catalog_state as materialized
        (
            select not exists
            (
                select 1
                from operations.deployments
                where tenant_id = @tenant_id
                  and desired_state <> 'draft'
            ) as is_empty
        ),
        advanced as
        (
            update control.deployment_scan_cursors as progress
            set last_deployment_id = coalesce(
                    eligible.id,
                    progress.last_deployment_id)
            from locked_cursor
            cross join catalog_state
            left join eligible on true
            where progress.tenant_id = @tenant_id
              and (eligible.id is not null or catalog_state.is_empty)
            returning
                eligible.id as id,
                eligible.tenant_id as tenant_id,
                coalesce(eligible.completes_rotation, false)
                    as completes_rotation,
                progress.rotation_count as rotation_count
        )
        select
            id,
            tenant_id,
            completes_rotation,
            rotation_count
        from advanced
        """;

    public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        readiness.IsReadyAsync(cancellationToken);

    public async Task<ControlWorkCycleResult> RunCycleAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!await IsAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            return new ControlWorkCycleResult(0, 0, 0, 0, false);
        }

        DateTimeOffset normalizedNow = now.ToUniversalTime();
        await using WorkerTenantScanLease tenantScan = await tenantCatalog.BeginScanAsync(
                WorkerTenantScanConsumer.DeploymentProjection,
                cancellationToken)
            .ConfigureAwait(false);
        int tenantsVisited = 0;
        int examined = 0;
        int changed = 0;
        int failed = 0;
        while (true)
        {
            WorkerTenantScanStep? tenantStep = await tenantScan.TryBeginNextAsync(cancellationToken)
                .ConfigureAwait(false);
            if (tenantStep is not { } durableTenantStep)
            {
                break;
            }

            Guid tenantId = durableTenantStep.TenantId;
            tenantsVisited++;
            long? deploymentRotationCeiling = null;
            var acquiredDeploymentIds = new HashSet<Guid>();
            for (int candidateIndex = 0;
                 candidateIndex < options.DeploymentBatchSizePerTenant;
                 candidateIndex++)
            {
                DeploymentScanStep? scanStep = await ReadNextCandidateAsync(
                    tenantId,
                    deploymentRotationCeiling,
                    cancellationToken).ConfigureAwait(false);
                if (scanStep is not { } durableScanStep)
                {
                    break;
                }

                if (!acquiredDeploymentIds.Add(durableScanStep.Candidate.Id))
                {
                    break;
                }

                deploymentRotationCeiling ??= durableScanStep.RotationCompleted
                    ? durableScanStep.RotationCount
                    : checked(durableScanStep.RotationCount + 1);
                examined++;
                try
                {
                    if (await ProjectAsync(
                            durableScanStep.Candidate,
                            normalizedNow,
                            cancellationToken)
                            .ConfigureAwait(false))
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

        bool scanRotationHealthy = await tenantCatalog.IsScanProgressHealthyAsync(
                WorkerTenantScanConsumer.DeploymentProjection,
                options.MaximumTenantScanRotationAge,
                cancellationToken)
            .ConfigureAwait(false);
        return new ControlWorkCycleResult(
            tenantsVisited,
            examined,
            changed,
            failed,
            scanRotationHealthy);
    }

    private async Task<DeploymentScanStep?> ReadNextCandidateAsync(
        Guid tenantId,
        long? rotationCeiling,
        CancellationToken cancellationToken)
    {
        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(
                PostgresWorkerTenantCatalog.CreateContext(tenantId, Guid.CreateVersion7()),
                cancellationToken).ConfigureAwait(false);
        await using (NpgsqlCommand initialize = transaction.CreateCommand(
            """
            insert into control.deployment_scan_cursors (tenant_id)
            values (@tenant_id)
            on conflict (tenant_id) do nothing
            """))
        {
            initialize.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
            await initialize.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using NpgsqlCommand command = transaction.CreateCommand(AdvanceDeploymentScanSql);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
        command.Parameters.Add(new NpgsqlParameter("rotation_ceiling", NpgsqlDbType.Bigint)
        {
            Value = rotationCeiling is long ceiling ? ceiling : DBNull.Value
        });
        DeploymentScanStep? step;
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                step = null;
            }
            else if (reader.IsDBNull(0))
            {
                step = null;
            }
            else
            {
                step = new DeploymentScanStep(
                    new DeploymentCandidate(
                        reader.GetGuid(0),
                        reader.GetGuid(1)),
                    reader.GetBoolean(2),
                    reader.GetInt64(3));
                step.Validate(tenantId);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return step;
    }

    private async Task<bool> ProjectAsync(
        DeploymentCandidate candidate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Guid correlationId = Guid.CreateVersion7();
        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(
                PostgresWorkerTenantCatalog.CreateContext(candidate.TenantId, correlationId),
                cancellationToken).ConfigureAwait(false);
        await AcquireAuthorityLockAsync(transaction, cancellationToken).ConfigureAwait(false);
        DeploymentSnapshot? deployment = await LockDeploymentAsync(transaction, candidate, cancellationToken)
            .ConfigureAwait(false);
        if (deployment is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        AssignmentEvidence? assignment = await ReadAssignmentAsync(
            transaction,
            deployment,
            cancellationToken).ConfigureAwait(false);
        LeaseEvidence? lease = await ReadLeaseAsync(
            transaction,
            deployment,
            assignment,
            cancellationToken)
            .ConfigureAwait(false);
        ReconciliationEvidence? reconciliation = ProjectableReconciliation(
            await ReadReconciliationAsync(
                transaction,
                deployment,
                assignment,
                cancellationToken).ConfigureAwait(false));
        ComponentEvidence components = await ReadComponentEvidenceAsync(
            transaction,
            deployment,
            assignment,
            now,
            options.ComponentHeartbeatMaximumAge,
            options.EvidenceFutureClockSkew,
            cancellationToken).ConfigureAwait(false);
        ProjectionDecision? decision = Decide(
            deployment,
            assignment,
            lease,
            reconciliation,
            components,
            now);
        if (decision is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        DateTimeOffset? nextLeaseExpiry = lease?.ExpiresAt ?? deployment.LeaseExpiresAt;
        DateTimeOffset? nextReconciledAt = reconciliation?.CompletedAt ?? deployment.LastReconciledAt;
        if (string.Equals(decision.ObservedState, deployment.ObservedState, StringComparison.Ordinal)
            && nextLeaseExpiry == deployment.LeaseExpiresAt
            && nextReconciledAt == deployment.LastReconciledAt)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        await using NpgsqlCommand update = transaction.CreateCommand(
            """
            update operations.deployments
            set observed_state = @observed_state,
                lease_expires_at = @lease_expires_at,
                last_reconciled_at = @last_reconciled_at,
                row_version = row_version + 1,
                updated_at = @now
            where tenant_id = @tenant_id
              and id = @deployment_id
              and row_version = @expected_version
            returning row_version
            """);
        update.Parameters.AddWithValue("observed_state", NpgsqlDbType.Text, decision.ObservedState);
        update.Parameters.AddWithValue(
            "lease_expires_at",
            NpgsqlDbType.TimestampTz,
            nextLeaseExpiry is null ? DBNull.Value : nextLeaseExpiry.Value);
        update.Parameters.AddWithValue(
            "last_reconciled_at",
            NpgsqlDbType.TimestampTz,
            nextReconciledAt is null ? DBNull.Value : nextReconciledAt.Value);
        update.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        update.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, deployment.TenantId);
        update.Parameters.AddWithValue("deployment_id", NpgsqlDbType.Uuid, deployment.Id);
        update.Parameters.AddWithValue("expected_version", NpgsqlDbType.Bigint, deployment.RowVersion);
        object? nextVersionValue = await update.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (nextVersionValue is not long nextVersion)
        {
            throw new InvalidOperationException("The deployment projection compare-and-swap failed.");
        }

        var safePayload = new
        {
            DeploymentId = deployment.Id,
            deployment.DesiredState,
            PreviousObservedState = deployment.ObservedState,
            decision.ObservedState,
            LeaseId = lease?.Id,
            AssignmentId = assignment?.Id,
            WorkerInstanceId = assignment?.WorkerInstanceId,
            deployment.FenceGeneration,
            ReconciliationId = reconciliation?.Id,
            decision.EvidenceCode,
            ComponentEvidenceCount = components.Total,
            FreshComponentEvidenceCount = components.Fresh,
            ValidReadyComponentCount = components.ValidReady,
            components.LatestObservedAt
        };
        AuditEvent audit = AuditEvent.Create(
            deployment.TenantId,
            WorkerDatabaseIdentity.ServiceActorId,
            AuditCategory.Operations,
            "deployment.projection_updated",
            "deployment",
            deployment.Id.ToString("D"),
            AuditOutcome.Succeeded,
            "Deployment observation was projected from persisted runtime evidence.",
            correlationId,
            reconciliation?.Id ?? lease?.Id,
            safePayload,
            now,
            new AuditEvidenceContext(
                Assurance: "workload",
                SourceNetworkClass: "unknown",
                ResourceVersionBefore: deployment.RowVersion,
                ResourceVersionAfter: nextVersion));
        OutboxMessage message = OutboxMessage.Create(
            deployment.TenantId,
            "deployment.observation_projected.v1",
            "deployment",
            deployment.Id.ToString("D"),
            safePayload,
            correlationId,
            reconciliation?.Id ?? lease?.Id,
            now);
        await PostgresAuditOutboxWriter.AppendAsync(
            transaction,
            audit,
            message,
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static async Task<DeploymentSnapshot?> LockDeploymentAsync(
        TenantPostgresTransaction transaction,
        DeploymentCandidate candidate,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select
                id, tenant_id, desired_state, observed_state, fence_generation,
                lease_expires_at, last_reconciled_at, row_version
            from operations.deployments
            where tenant_id = @tenant_id
              and id = @deployment_id
              and desired_state <> 'draft'
            for update
            """);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, candidate.TenantId);
        command.Parameters.AddWithValue("deployment_id", NpgsqlDbType.Uuid, candidate.Id);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new DeploymentSnapshot(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt64(4),
            reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
            reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetInt64(7));
    }

    private static async Task<AssignmentEvidence?> ReadAssignmentAsync(
        TenantPostgresTransaction transaction,
        DeploymentSnapshot deployment,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select id, worker_node_id, state, lease_expires_at
            from operations.worker_assignments
            where tenant_id = @tenant_id
              and deployment_id = @deployment_id
              and fence_generation = @generation
            order by assigned_at desc, id desc
            limit 1
            """);
        AddDeploymentParameters(command, deployment);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new AssignmentEvidence(
                reader.GetGuid(0),
                reader.GetGuid(1),
                deployment.FenceGeneration,
                reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3))
            : null;
    }

    private static async Task<LeaseEvidence?> ReadLeaseAsync(
        TenantPostgresTransaction transaction,
        DeploymentSnapshot deployment,
        AssignmentEvidence? assignment,
        CancellationToken cancellationToken)
    {
        if (assignment is null)
        {
            return null;
        }

        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select id, state, expires_at, worker_assignment_id, worker_instance_id, generation
            from operations.execution_leases
            where tenant_id = @tenant_id
              and deployment_id = @deployment_id
              and generation = @generation
              and worker_assignment_id = @assignment_id
              and worker_instance_id = @worker_instance_id
            order by issued_at desc, id
            limit 1
            """);
        AddDeploymentParameters(command, deployment);
        command.Parameters.AddWithValue("assignment_id", NpgsqlDbType.Uuid, assignment.Id);
        command.Parameters.AddWithValue("worker_instance_id", NpgsqlDbType.Uuid, assignment.WorkerInstanceId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new LeaseEvidence(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetGuid(3),
                reader.GetGuid(4),
                reader.GetInt64(5))
            : null;
    }

    private static async Task<ReconciliationEvidence?> ReadReconciliationAsync(
        TenantPostgresTransaction transaction,
        DeploymentSnapshot deployment,
        AssignmentEvidence? assignment,
        CancellationToken cancellationToken)
    {
        if (assignment is null)
        {
            return null;
        }

        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select
                reconciliation.id, reconciliation.state,
                reconciliation.desired_digest, reconciliation.observed_digest,
                reconciliation.broker_digest, reconciliation.observed_state,
                reconciliation.runtime_evidence_sha256,
                reconciliation.generation,
                reconciliation.worker_assignment_id,
                reconciliation.worker_instance_id,
                reconciliation.broker_confirmed,
                reconciliation.broker_execution_state,
                reconciliation.broker_position_state,
                reconciliation.completed_at,
                reconciliation.pre_invocation_not_sent_proven,
                reconciliation.reconciliation_challenge_id,
                challenge.route_deployment_id,
                challenge.fence_generation,
                challenge.worker_assignment_id,
                challenge.worker_instance_id,
                consumption.challenge_id as consumed_challenge_id
            from operations.deployment_reconciliations as reconciliation
            left join control.user_operation_reconciliation_challenges as challenge
              on challenge.tenant_id = reconciliation.tenant_id
             and challenge.id = reconciliation.reconciliation_challenge_id
            left join control.user_operation_reconciliation_challenge_consumptions as consumption
              on consumption.tenant_id = challenge.tenant_id
             and consumption.challenge_id = challenge.id
             and consumption.target_type = 'deployment'
             and consumption.result_record_id = reconciliation.id
             and consumption.result_id = reconciliation.result_id
             and consumption.request_sha256 = reconciliation.request_sha256
            where reconciliation.tenant_id = @tenant_id
              and reconciliation.deployment_id = @deployment_id
              and reconciliation.completed_at is not null
              and
              (
                  reconciliation.dispatch_message_id is null
                  or
                  (
                      reconciliation.state in ('reconciled', 'diverged')
                      and reconciliation.pre_invocation_not_sent_proven = false
                      and reconciliation.gateway_invoked = true
                      and reconciliation.broker_confirmed = true
                      and reconciliation.observed_state is not null
                      and reconciliation.observed_digest is not null
                      and reconciliation.runtime_evidence_sha256 is not null
                      and reconciliation.broker_digest is not null
                      and reconciliation.broker_execution_state is not null
                      and reconciliation.broker_position_state is not null
                  )
              )
              and
              (
                  reconciliation.reconciliation_challenge_id is null
                  or
                  (
                      consumption.challenge_id = reconciliation.reconciliation_challenge_id
                      and challenge.route_deployment_id = reconciliation.deployment_id
                  )
              )
            order by reconciliation.completed_at desc, reconciliation.id
            limit 1
            """);
        AddDeploymentParameters(command, deployment);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        long generation = reader.GetInt64(7);
        Guid assignmentId = reader.GetGuid(8);
        Guid workerInstanceId = reader.GetGuid(9);
        Guid? challengeId = reader.IsDBNull(15) ? null : reader.GetGuid(15);
        if (challengeId is not null)
        {
            if (reader.IsDBNull(16)
                || reader.IsDBNull(17)
                || reader.IsDBNull(18)
                || reader.IsDBNull(19)
                || reader.IsDBNull(20)
                || reader.GetGuid(20) != challengeId
                || reader.GetGuid(16) != deployment.Id)
            {
                return null;
            }

            generation = reader.GetInt64(17);
            assignmentId = reader.GetGuid(18);
            workerInstanceId = reader.GetGuid(19);
        }
        if (generation != deployment.FenceGeneration
            || assignmentId != assignment.Id
            || workerInstanceId != assignment.WorkerInstanceId)
        {
            return null;
        }

        return new ReconciliationEvidence(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            generation,
            assignmentId,
            workerInstanceId,
            reader.GetBoolean(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.GetFieldValue<DateTimeOffset>(13),
            !reader.IsDBNull(14) && reader.GetBoolean(14));
    }

    private static async Task<ComponentEvidence> ReadComponentEvidenceAsync(
        TenantPostgresTransaction transaction,
        DeploymentSnapshot deployment,
        AssignmentEvidence? assignment,
        DateTimeOffset now,
        TimeSpan maximumAge,
        TimeSpan futureClockSkew,
        CancellationToken cancellationToken)
    {
        if (assignment is null)
        {
            return ComponentEvidence.None;
        }

        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            with latest as
            (
                select distinct on (component_role)
                    component_role, component_state, fence_evidence_state,
                    evidence_sha256, observed_at
                from operations.runtime_component_evidence
                where tenant_id = @tenant_id
                  and deployment_id = @deployment_id
                  and generation = @generation
                  and worker_instance_id = @worker_instance_id
                order by component_role, heartbeat_sequence desc
            )
            select
                count(*)::integer,
                count(*) filter
                    (where observed_at between @minimum_observed_at and @maximum_observed_at)::integer,
                count(*) filter
                    (where component_state = 'ready'
                       and fence_evidence_state = 'valid'
                       and observed_at between @minimum_observed_at and @maximum_observed_at)::integer,
                count(*) filter
                    (where component_state = 'faulted'
                       and fence_evidence_state = 'valid'
                       and observed_at between @minimum_observed_at and @maximum_observed_at)::integer,
                bool_and(evidence_sha256 ~ '^[0-9a-f]{64}$'),
                max(observed_at)
            from latest
            """);
        AddDeploymentParameters(command, deployment);
        command.Parameters.AddWithValue("worker_instance_id", NpgsqlDbType.Uuid, assignment.WorkerInstanceId);
        command.Parameters.AddWithValue(
            "minimum_observed_at",
            NpgsqlDbType.TimestampTz,
            now.Subtract(maximumAge));
        command.Parameters.AddWithValue(
            "maximum_observed_at",
            NpgsqlDbType.TimestampTz,
            now.Add(futureClockSkew));
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return ComponentEvidence.None;
        }

        return new ComponentEvidence(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            !reader.IsDBNull(4) && reader.GetBoolean(4),
            reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5));
    }

    internal static ProjectionDecision? Decide(
        DeploymentSnapshot deployment,
        AssignmentEvidence? assignment,
        LeaseEvidence? lease,
        ReconciliationEvidence? reconciliation,
        ComponentEvidence components,
        DateTimeOffset now)
    {
        reconciliation = ProjectableReconciliation(reconciliation);
        bool reconciledBinding = reconciliation is not null
            && assignment is not null
            && reconciliation.FenceGeneration == deployment.FenceGeneration
            && reconciliation.WorkerAssignmentId == assignment.Id
            && reconciliation.WorkerInstanceId == assignment.WorkerInstanceId;
        bool brokerConfirmedStopped = reconciledBinding
            && reconciliation!.State == "reconciled"
            && FixedDigestEquals(reconciliation.DesiredDigest, reconciliation.ObservedDigest)
            && IsSha256(reconciliation.BrokerDigest)
            && IsSha256(reconciliation.RuntimeEvidenceSha256)
            && reconciliation.BrokerConfirmed
            && reconciliation.ObservedState == "stopped"
            && reconciliation.BrokerExecutionState == "stopped"
            && reconciliation.BrokerPositionState == "flat"
            && deployment.DesiredState is "stop_after_flat" or "stopping" or "stopped" or "expired" or "revoked";
        if (brokerConfirmedStopped)
        {
            return new ProjectionDecision("stopped", "broker_confirmed_terminal_reconciliation");
        }

        bool brokerConfirmedCloseOnly = reconciledBinding
            && reconciliation!.State == "reconciled"
            && FixedDigestEquals(reconciliation.DesiredDigest, reconciliation.ObservedDigest)
            && IsSha256(reconciliation.BrokerDigest)
            && IsSha256(reconciliation.RuntimeEvidenceSha256)
            && reconciliation.BrokerConfirmed
            && reconciliation.ObservedState == "close_only"
            && reconciliation.BrokerExecutionState == "close_only"
            && deployment.DesiredState == "close_only";
        if (brokerConfirmedCloseOnly)
        {
            return new ProjectionDecision("close_only", "broker_confirmed_restrictive_reconciliation");
        }

        if (lease?.State is "revoked" or "fenced")
        {
            return new ProjectionDecision("fenced", "persisted_lease_fence");
        }

        if (lease is not null && (lease.State == "expired" || lease.ExpiresAt <= now))
        {
            return new ProjectionDecision("unreachable", "persisted_lease_expiry");
        }

        bool expectsReachableRuntime = deployment.DesiredState is
            "starting" or "reconciling" or "running" or "close_only" or
            "stop_after_flat" or "stopping";
        if (expectsReachableRuntime
            && (assignment is null
                || assignment.State is not ("assigned" or "reconciliation_only" or "active")
                || assignment.LeaseExpiresAt <= now
                || lease is null
                || lease.WorkerAssignmentId != assignment.Id
                || lease.WorkerInstanceId != assignment.WorkerInstanceId
                || lease.Generation != deployment.FenceGeneration
                || components.Total != 3
                || components.Fresh != 3
                || !components.AllDigestsValid))
        {
            return new ProjectionDecision("unreachable", "persisted_runtime_evidence_stale_or_unbound");
        }

        if (components.ValidFaulted > 0)
        {
            return new ProjectionDecision("faulted", "persisted_component_fault");
        }

        if (!reconciledBinding)
        {
            return lease is null
                ? null
                : new ProjectionDecision(deployment.ObservedState, "persisted_fresh_runtime_without_reconciliation");
        }

        ReconciliationEvidence boundReconciliation = reconciliation!;

        if (boundReconciliation.State == "failed")
        {
            return new ProjectionDecision("faulted", "persisted_reconciliation_failure");
        }

        if (boundReconciliation.State == "unknown")
        {
            return new ProjectionDecision("unreachable", "persisted_reconciliation_unknown");
        }

        if (boundReconciliation.State == "diverged")
        {
            return new ProjectionDecision("reconciling", "persisted_reconciliation_divergence");
        }

        if (boundReconciliation.State != "reconciled"
            || !FixedDigestEquals(boundReconciliation.DesiredDigest, boundReconciliation.ObservedDigest)
            || !IsSha256(boundReconciliation.BrokerDigest)
            || !IsSha256(boundReconciliation.RuntimeEvidenceSha256))
        {
            return null;
        }

        string? expected = deployment.DesiredState switch
        {
            "starting" or "running" => "running",
            "close_only" => "close_only",
            "stop_after_flat" or "stopping" => "stopped",
            _ => null
        };
        if (expected is null
            || !boundReconciliation.BrokerConfirmed
            || !string.Equals(boundReconciliation.ObservedState, expected, StringComparison.Ordinal)
            || !string.Equals(boundReconciliation.BrokerExecutionState, expected, StringComparison.Ordinal)
            || expected == "stopped"
                && !string.Equals(
                    boundReconciliation.BrokerPositionState,
                    "flat",
                    StringComparison.Ordinal))
        {
            return null;
        }

        if (expected == "running"
            && (lease is null
                || lease.State is not ("issued" or "active" or "renew_restricted")
                || lease.ExpiresAt <= now
                || components.ValidReady != 3))
        {
            return new ProjectionDecision("unreachable", "persisted_running_evidence_not_ready");
        }

        if (expected == "close_only" && components.ValidReady != 3)
        {
            return new ProjectionDecision("unreachable", "persisted_close_only_evidence_not_ready");
        }

        return new ProjectionDecision(expected, "persisted_reconciliation_match");
    }

    internal static ReconciliationEvidence? ProjectableReconciliation(
        ReconciliationEvidence? reconciliation) =>
        reconciliation is { PreInvocationNotSentProven: true } ? null : reconciliation;

    private static void AddDeploymentParameters(NpgsqlCommand command, DeploymentSnapshot deployment)
    {
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, deployment.TenantId);
        command.Parameters.AddWithValue("deployment_id", NpgsqlDbType.Uuid, deployment.Id);
        if (command.CommandText.Contains("@generation", StringComparison.Ordinal))
        {
            command.Parameters.AddWithValue("generation", NpgsqlDbType.Bigint, deployment.FenceGeneration);
        }
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

    private static bool FixedDigestEquals(string first, string? second)
    {
        if (!IsSha256(first) || !IsSha256(second))
        {
            return false;
        }

        byte[] firstBytes = Convert.FromHexString(first);
        byte[] secondBytes = Convert.FromHexString(second!);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(firstBytes, secondBytes);
    }

    internal sealed record DeploymentSnapshot(
        Guid Id,
        Guid TenantId,
        string DesiredState,
        string ObservedState,
        long FenceGeneration,
        DateTimeOffset? LeaseExpiresAt,
        DateTimeOffset? LastReconciledAt,
        long RowVersion);

    internal sealed record AssignmentEvidence(
        Guid Id,
        Guid WorkerInstanceId,
        long FenceGeneration,
        string State,
        DateTimeOffset LeaseExpiresAt);

    internal sealed record LeaseEvidence(
        Guid Id,
        string State,
        DateTimeOffset ExpiresAt,
        Guid WorkerAssignmentId,
        Guid WorkerInstanceId,
        long Generation);

    internal sealed record ReconciliationEvidence(
        Guid Id,
        string State,
        string DesiredDigest,
        string? ObservedDigest,
        string? BrokerDigest,
        string? ObservedState,
        string? RuntimeEvidenceSha256,
        long FenceGeneration,
        Guid WorkerAssignmentId,
        Guid WorkerInstanceId,
        bool BrokerConfirmed,
        string? BrokerExecutionState,
        string? BrokerPositionState,
        DateTimeOffset CompletedAt,
        bool PreInvocationNotSentProven);

    internal sealed record ComponentEvidence(
        int Total,
        int Fresh,
        int ValidReady,
        int ValidFaulted,
        bool AllDigestsValid,
        DateTimeOffset? LatestObservedAt)
    {
        public static ComponentEvidence None { get; } = new(0, 0, 0, 0, false, null);
    }

    internal sealed record ProjectionDecision(string ObservedState, string EvidenceCode);

    private sealed record DeploymentCandidate(Guid Id, Guid TenantId);

    private sealed record DeploymentScanStep(
        DeploymentCandidate Candidate,
        bool RotationCompleted,
        long RotationCount)
    {
        public void Validate(Guid expectedTenantId)
        {
            if (Candidate.Id == Guid.Empty
                || Candidate.TenantId != expectedTenantId
                || RotationCount < 0
                || (RotationCompleted && RotationCount == 0))
            {
                throw new InvalidOperationException(
                    "The durable deployment scan returned invalid progress metadata.");
            }
        }
    }
}
