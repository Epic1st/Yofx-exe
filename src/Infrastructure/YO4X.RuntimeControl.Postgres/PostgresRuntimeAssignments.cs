using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.Runtime.Contracts;
using YO4X.RuntimeOperations;

namespace YO4X.RuntimeControl.Postgres;

public sealed partial class PostgresRuntimeControlPlaneApplication
{
    public async Task<WorkerRegistrationView> RegisterWorkerAsync(
        WorkloadActor actor,
        WorkerRegistration request,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireSupervisor(actor);
        ValidateRegistration(actor, request);

        await using var transaction = await BeginRuntimeAsync(
                actor,
                metadata,
                cancellationToken,
                requireAuthorityLock: true)
            .ConfigureAwait(false);
        DateTimeOffset authorizationNow;

        await using (NpgsqlCommand binding = transaction.CreateCommand(
            """
            select
                deployment.broker_account_id,
                deployment.fence_generation,
                deployment.region,
                deployment.strategy_package_digest,
                deployment.gateway_artifact_id,
                deployment.gateway_digest,
                deployment.desired_state,
                deployment.deployment_mode,
                node.region,
                node.image_digest,
                node.state,
                clock_timestamp() as authorization_now,
                strategy.state,
                source_binding.id
            from operations.deployments as deployment
            join operations.worker_nodes as node on node.id = @worker_id
            join governance.strategy_versions as strategy
              on strategy.tenant_id = deployment.tenant_id
             and strategy.id = deployment.strategy_version_id
             and strategy.package_sha256 = deployment.strategy_package_digest
            join governance.strategy_version_source_bindings as source_binding
              on source_binding.tenant_id = deployment.tenant_id
             and source_binding.id = deployment.strategy_source_binding_id
             and source_binding.strategy_version_id = deployment.strategy_version_id
             and source_binding.strategy_package_sha256 = deployment.strategy_package_digest
             and source_binding.verification_evidence_sha256 =
                deployment.strategy_verification_evidence_sha256
             and source_binding.verification_signature_sha256 =
                deployment.strategy_verification_signature_sha256
             and source_binding.verification_signing_key_id =
                deployment.strategy_verification_signing_key_id
            where deployment.tenant_id = @tenant_id
              and deployment.id = @deployment_id
            for update of deployment, node
            for share of strategy, source_binding
            """))
        {
            AddUuid(binding, "tenant_id", actor.TenantId);
            AddUuid(binding, "deployment_id", actor.DeploymentId);
            AddUuid(binding, "worker_id", actor.WorkerInstanceId);
            await using NpgsqlDataReader reader = await binding.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                || reader.GetGuid(0) != request.BrokerAccountId
                || reader.GetInt64(1) != request.Generation
                || !string.Equals(reader.GetString(2), request.Region, StringComparison.Ordinal)
                || !FixedTimeEquals(reader.GetString(3), request.StrategyPackageDigest)
                || !FixedTimeEquals(reader.GetString(5), request.GatewayArtifactDigest)
                || !string.Equals(reader.GetString(6), "starting", StringComparison.Ordinal)
                || !string.Equals(reader.GetString(7), "cloud_demo", StringComparison.Ordinal)
                || !string.Equals(reader.GetString(8), request.Region, StringComparison.Ordinal)
                || !FixedTimeEquals(reader.GetString(9), request.RuntimeImageDigest)
                || !string.Equals(reader.GetString(10), "ready", StringComparison.Ordinal)
                || reader.GetString(12) is not ("demo_approved" or "published")
                || reader.IsDBNull(13))
            {
                throw WrongRuntimeBinding();
            }

            Guid gatewayArtifactId = reader.GetGuid(4);
            authorizationNow = reader.GetFieldValue<DateTimeOffset>(11);
            await reader.DisposeAsync().ConfigureAwait(false);

            WorkerRegistrationView? replay = await TryLoadRegistrationReplayAsync(
                transaction,
                actor,
                request,
                gatewayArtifactId,
                cancellationToken).ConfigureAwait(false);
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return replay;
            }

            Guid assignmentId = Guid.CreateVersion7();
            DateTimeOffset expiresAt = authorizationNow.Add(options.AssignmentLifetime);
            await using NpgsqlCommand insert = transaction.CreateCommand(
                """
                insert into operations.worker_assignments
                (
                    id, tenant_id, deployment_id, worker_node_id,
                    supervisor_identity, strategy_host_identity, gateway_host_identity,
                    fence_generation, runtime_digest, gateway_artifact_id,
                    state, assigned_at, lease_expires_at, row_version
                )
                values
                (
                    @id, @tenant_id, @deployment_id, @worker_id,
                    @supervisor_identity, @strategy_identity, @gateway_identity,
                    @generation, @runtime_digest, @gateway_artifact_id,
                    'reconciliation_only', @assigned_at, @expires_at, 0
                )
                """);
            AddUuid(insert, "id", assignmentId);
            AddUuid(insert, "tenant_id", actor.TenantId);
            AddUuid(insert, "deployment_id", actor.DeploymentId);
            AddUuid(insert, "worker_id", actor.WorkerInstanceId);
            insert.Parameters.AddWithValue("supervisor_identity", NpgsqlDbType.Text, request.SupervisorWorkloadId.ToString("D"));
            insert.Parameters.AddWithValue("strategy_identity", NpgsqlDbType.Text, request.StrategyHostWorkloadId.ToString("D"));
            insert.Parameters.AddWithValue("gateway_identity", NpgsqlDbType.Text, request.GatewayHostWorkloadId.ToString("D"));
            insert.Parameters.AddWithValue("generation", NpgsqlDbType.Bigint, request.Generation);
            insert.Parameters.AddWithValue("runtime_digest", NpgsqlDbType.Text, request.RuntimeImageDigest);
            AddUuid(insert, "gateway_artifact_id", gatewayArtifactId);
            insert.Parameters.AddWithValue("assigned_at", NpgsqlDbType.TimestampTz, authorizationNow);
            insert.Parameters.AddWithValue("expires_at", NpgsqlDbType.TimestampTz, expiresAt);
            try
            {
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                throw new ResourceConflictException(
                    "WORKER_ASSIGNMENT_CONFLICT",
                    "A different current worker assignment already owns the deployment.");
            }

            var view = new WorkerRegistrationView(
                actor.WorkerInstanceId,
                actor.Generation,
                "reconciliation_only",
                authorizationNow);
            await AppendEvidenceAsync(
                transaction,
                "runtime.worker_registered",
                "worker_assignment",
                assignmentId,
                metadata,
                assignmentId,
                new
                {
                    assignmentId,
                    deploymentId = actor.DeploymentId,
                    workerInstanceId = actor.WorkerInstanceId,
                    generation = actor.Generation,
                    region = actor.Region,
                    runtimeDigestSha256 = Sha256Utf8(request.RuntimeImageDigest),
                    strategyPackageSha256 = request.StrategyPackageDigest,
                    gatewayArtifactSha256 = request.GatewayArtifactDigest,
                    state = "reconciliation_only"
                },
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return view;
        }
    }

    public async Task RecordHeartbeatAsync(
        WorkloadActor actor,
        Guid workerId,
        RuntimeComponentRole component,
        ComponentHeartbeat request,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateActor(actor);
        if (workerId == Guid.Empty
            || workerId != actor.WorkerInstanceId
            || !Enum.IsDefined(component)
            || !string.Equals(ComponentToStorage(component), actor.Component, StringComparison.Ordinal)
            || request.ContractVersion != RuntimeContractVersions.ComponentEvidenceV1
            || request.Sequence <= 0
            || request.LastAcceptedEventSequence < 0
            || !Enum.IsDefined(request.State)
            || !Enum.IsDefined(request.FenceState))
        {
            throw WrongRuntimeBinding();
        }

        if (request.StartedAt.ToUniversalTime() > request.ObservedAt.ToUniversalTime())
        {
            throw new DomainException("HEARTBEAT_TIME_INVALID", "The component start time is after the observation time.");
        }

        RuntimeComponentEvidence expected = RuntimeComponentEvidenceFactory.Create(
            component,
            actor.DeploymentId,
            actor.WorkerInstanceId,
            actor.Generation,
            request.LastAcceptedEventSequence,
            request.State,
            request.FenceState,
            request.StartedAt,
            request.ObservedAt);
        if (!FixedTimeEquals(expected.EvidenceHash, request.EvidenceDigest))
        {
            throw new DomainException("HEARTBEAT_EVIDENCE_INVALID", "The component heartbeat evidence digest is invalid.");
        }

        await using var transaction = await BeginRuntimeAsync(actor, metadata, cancellationToken)
            .ConfigureAwait(false);
        RuntimeBindingSnapshot binding = await LoadBindingAsync(transaction, actor, true, cancellationToken)
            .ConfigureAwait(false);
        DateTimeOffset authorizationNow = binding.AuthorizationNow;
        ValidateEventEnvelope(
            actor.Generation,
            request.Sequence,
            request.ContractVersion,
            Guid.NewGuid(),
            request.ObservedAt,
            authorizationNow,
            options);
        if (binding.AssignmentExpiresAt <= authorizationNow
            || binding.AssignmentState is "revoked" or "failed")
        {
            throw new ResourceConflictException(
                "WORKER_ASSIGNMENT_INACTIVE",
                "The worker assignment is no longer active for evidence submission.");
        }

        HeartbeatSequence? latest = await LoadLatestHeartbeatAsync(
            transaction,
            actor,
            component,
            cancellationToken).ConfigureAwait(false);
        if (latest is not null && request.Sequence == latest.Sequence)
        {
            if (!FixedTimeEquals(latest.EvidenceSha256, request.EvidenceDigest))
            {
                throw new ResourceConflictException(
                    "HEARTBEAT_SEQUENCE_REUSED",
                    "The heartbeat sequence was already used for different evidence.");
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        long expectedSequence = latest is null ? 1 : checked(latest.Sequence + 1);
        if (request.Sequence != expectedSequence)
        {
            throw new ResourceConflictException(
                request.Sequence < expectedSequence ? "HEARTBEAT_SEQUENCE_STALE" : "HEARTBEAT_SEQUENCE_GAP",
                "The component heartbeat sequence is not the next expected value.");
        }

        await using NpgsqlCommand insert = transaction.CreateCommand(
            """
            insert into operations.runtime_component_evidence
            (
                id, tenant_id, deployment_id, worker_instance_id, generation,
                component_role, contract_version, heartbeat_sequence,
                last_accepted_event_sequence, component_state, fence_evidence_state,
                evidence_sha256, started_at, observed_at, received_at
            )
            values
            (
                @id, @tenant_id, @deployment_id, @worker_id, @generation,
                @component, @contract_version, @sequence,
                @last_event_sequence, @state, @fence_state,
                @evidence_sha256, @started_at, @observed_at, @received_at
            )
            """);
        AddUuid(insert, "id", Guid.CreateVersion7());
        AddUuid(insert, "tenant_id", actor.TenantId);
        AddUuid(insert, "deployment_id", actor.DeploymentId);
        AddUuid(insert, "worker_id", actor.WorkerInstanceId);
        insert.Parameters.AddWithValue("generation", NpgsqlDbType.Bigint, actor.Generation);
        insert.Parameters.AddWithValue("component", NpgsqlDbType.Text, ComponentToStorage(component));
        insert.Parameters.AddWithValue("contract_version", NpgsqlDbType.Integer, request.ContractVersion);
        insert.Parameters.AddWithValue("sequence", NpgsqlDbType.Bigint, request.Sequence);
        insert.Parameters.AddWithValue("last_event_sequence", NpgsqlDbType.Bigint, request.LastAcceptedEventSequence);
        insert.Parameters.AddWithValue("state", NpgsqlDbType.Text, StateToStorage(request.State));
        insert.Parameters.AddWithValue("fence_state", NpgsqlDbType.Text, FenceToStorage(request.FenceState));
        insert.Parameters.AddWithValue("evidence_sha256", NpgsqlDbType.Text, request.EvidenceDigest);
        insert.Parameters.AddWithValue("started_at", NpgsqlDbType.TimestampTz, request.StartedAt.ToUniversalTime());
        insert.Parameters.AddWithValue("observed_at", NpgsqlDbType.TimestampTz, request.ObservedAt.ToUniversalTime());
        insert.Parameters.AddWithValue("received_at", NpgsqlDbType.TimestampTz, authorizationNow);
        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void ValidateRegistration(WorkloadActor actor, WorkerRegistration request)
    {
        ValidateActor(actor);
        if (request.BrokerAccountId != actor.BrokerAccountId
            || request.DeploymentId != actor.DeploymentId
            || request.WorkerInstanceId != actor.WorkerInstanceId
            || request.Generation != actor.Generation
            || !string.Equals(request.Region, actor.Region, StringComparison.Ordinal)
            || request.SupervisorWorkloadId != actor.WorkloadId
            || request.StrategyHostWorkloadId == Guid.Empty
            || request.GatewayHostWorkloadId == Guid.Empty
            || request.SupervisorWorkloadId == request.StrategyHostWorkloadId
            || request.SupervisorWorkloadId == request.GatewayHostWorkloadId
            || request.StrategyHostWorkloadId == request.GatewayHostWorkloadId
            || !IsRuntimeDigest(request.RuntimeImageDigest)
            || !FixedTimeEquals(options.ApprovedRuntimeImageDigest!, request.RuntimeImageDigest)
            || !IsSha256(request.StrategyPackageDigest)
            || !IsSha256(request.GatewayArtifactDigest))
        {
            throw WrongRuntimeBinding();
        }
    }

    private static bool IsRuntimeDigest(string? value) => value is { Length: 71 }
        && value.StartsWith("sha256:", StringComparison.Ordinal)
        && IsSha256(value[7..]);

    private static bool IsSha256(string? value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static async Task<WorkerRegistrationView?> TryLoadRegistrationReplayAsync(
        YO4X.Persistence.Postgres.TenantPostgresTransaction transaction,
        WorkloadActor actor,
        WorkerRegistration request,
        Guid gatewayArtifactId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select
                id, worker_node_id, supervisor_identity, strategy_host_identity,
                gateway_host_identity, runtime_digest, gateway_artifact_id,
                state, assigned_at
            from operations.worker_assignments
            where tenant_id = @tenant_id
              and deployment_id = @deployment_id
              and fence_generation = @generation
            """);
        AddUuid(command, "tenant_id", actor.TenantId);
        AddUuid(command, "deployment_id", actor.DeploymentId);
        command.Parameters.AddWithValue("generation", NpgsqlDbType.Bigint, actor.Generation);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        if (reader.GetGuid(1) != request.WorkerInstanceId
            || !string.Equals(reader.GetString(2), request.SupervisorWorkloadId.ToString("D"), StringComparison.Ordinal)
            || !string.Equals(reader.GetString(3), request.StrategyHostWorkloadId.ToString("D"), StringComparison.Ordinal)
            || !string.Equals(reader.GetString(4), request.GatewayHostWorkloadId.ToString("D"), StringComparison.Ordinal)
            || !FixedTimeEquals(reader.GetString(5), request.RuntimeImageDigest)
            || reader.GetGuid(6) != gatewayArtifactId)
        {
            throw new ResourceConflictException(
                "WORKER_REGISTRATION_REPLAY_MISMATCH",
                "The deployment generation was already registered with different bindings.");
        }

        return new WorkerRegistrationView(
            request.WorkerInstanceId,
            request.Generation,
            reader.GetString(7),
            reader.GetFieldValue<DateTimeOffset>(8));
    }

    private static async Task<HeartbeatSequence?> LoadLatestHeartbeatAsync(
        YO4X.Persistence.Postgres.TenantPostgresTransaction transaction,
        WorkloadActor actor,
        RuntimeComponentRole component,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select heartbeat_sequence, evidence_sha256
            from operations.runtime_component_evidence
            where tenant_id = @tenant_id
              and deployment_id = @deployment_id
              and generation = @generation
              and component_role = @component
            order by heartbeat_sequence desc
            limit 1
            """);
        AddUuid(command, "tenant_id", actor.TenantId);
        AddUuid(command, "deployment_id", actor.DeploymentId);
        command.Parameters.AddWithValue("generation", NpgsqlDbType.Bigint, actor.Generation);
        command.Parameters.AddWithValue("component", NpgsqlDbType.Text, ComponentToStorage(component));
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new HeartbeatSequence(reader.GetInt64(0), reader.GetString(1))
            : null;
    }

    private sealed record HeartbeatSequence(long Sequence, string EvidenceSha256);
}
