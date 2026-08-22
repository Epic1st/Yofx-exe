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
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, Guid> scanCursors = new();

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

        DateTimeOffset normalizedNow = now.ToUniversalTime();
        IReadOnlyList<Guid> tenantIds = await tenantCatalog.GetTenantIdsAsync(cancellationToken)
            .ConfigureAwait(false);
        int examined = 0;
        int changed = 0;
        int failed = 0;
        foreach (Guid tenantId in tenantIds)
        {
            IReadOnlyList<DeploymentCandidate> candidates = await ReadCandidatesAsync(
                tenantId,
                cancellationToken).ConfigureAwait(false);
            foreach (DeploymentCandidate candidate in candidates)
            {
                examined++;
                try
                {
                    if (await ProjectAsync(candidate, normalizedNow, cancellationToken).ConfigureAwait(false))
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

    private async Task<IReadOnlyList<DeploymentCandidate>> ReadCandidatesAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(
                PostgresWorkerTenantCatalog.CreateContext(tenantId, Guid.CreateVersion7()),
                cancellationToken).ConfigureAwait(false);
        bool hasCursor = scanCursors.TryGetValue(tenantId, out Guid cursor);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select id, tenant_id, row_version
            from operations.deployments
            where tenant_id = @tenant_id
              and desired_state <> 'draft'
            order by
                case when @has_cursor and id <= @cursor then 1 else 0 end,
                id
            limit @batch_size
            """);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
        command.Parameters.AddWithValue("has_cursor", NpgsqlDbType.Boolean, hasCursor);
        command.Parameters.AddWithValue("cursor", NpgsqlDbType.Uuid, hasCursor ? cursor : Guid.Empty);
        command.Parameters.AddWithValue("batch_size", NpgsqlDbType.Integer, options.DeploymentBatchSizePerTenant);
        var candidates = new List<DeploymentCandidate>(options.DeploymentBatchSizePerTenant);
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                candidates.Add(new DeploymentCandidate(reader.GetGuid(0), reader.GetGuid(1), reader.GetInt64(2)));
            }
        }

        if (candidates.Count != 0)
        {
            scanCursors[tenantId] = candidates[^1].Id;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return candidates;
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
        ReconciliationEvidence? reconciliation = await ReadReconciliationAsync(
            transaction,
            deployment,
            assignment,
            cancellationToken).ConfigureAwait(false);
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
              and row_version = @expected_version
            for update
            """);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, candidate.TenantId);
        command.Parameters.AddWithValue("deployment_id", NpgsqlDbType.Uuid, candidate.Id);
        command.Parameters.AddWithValue("expected_version", NpgsqlDbType.Bigint, candidate.RowVersion);
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
                id, state, desired_digest, observed_digest, broker_digest,
                observed_state,
                runtime_evidence_sha256,
                generation,
                worker_assignment_id,
                worker_instance_id,
                broker_confirmed,
                broker_execution_state,
                broker_position_state,
                completed_at
            from operations.deployment_reconciliations
            where tenant_id = @tenant_id
              and deployment_id = @deployment_id
              and completed_at is not null
            order by completed_at desc, id
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
            reader.GetFieldValue<DateTimeOffset>(13));
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
        DateTimeOffset CompletedAt);

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

    private sealed record DeploymentCandidate(Guid Id, Guid TenantId, long RowVersion);
}
