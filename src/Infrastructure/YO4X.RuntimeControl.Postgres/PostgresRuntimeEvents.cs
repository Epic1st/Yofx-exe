using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.Persistence.Postgres;

namespace YO4X.RuntimeControl.Postgres;

public sealed partial class PostgresRuntimeControlPlaneApplication
{
    public Task<RuntimeAcceptance> RecordDeploymentEventAsync(
        WorkloadActor actor,
        Guid deploymentId,
        RuntimeEventInput request,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireSupervisor(actor);
        if (deploymentId == Guid.Empty || deploymentId != actor.DeploymentId)
        {
            throw WrongRuntimeBinding();
        }

        DateTimeOffset observedAt = ToPostgresMicrosecond(request.ObservedAt);
        string payload = CanonicalJson.Serialize(request.Payload);
        return AcceptEventAsync(
            actor,
            null,
            request.SchemaVersion,
            request.EventId,
            request.Generation,
            request.Sequence,
            observedAt,
            "deployment_event",
            payload,
            null,
            metadata,
            cancellationToken);
    }

    public Task<RuntimeAcceptance> RecordTargetDeliveryAsync(
        WorkloadActor actor,
        Guid targetId,
        TargetDeliveryInput request,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireSupervisor(actor);
        TargetDeliveryInput canonicalRequest = request with
        {
            ObservedAt = ToPostgresMicrosecond(request.ObservedAt)
        };
        ValidateTargetInput(targetId, canonicalRequest);
        return AcceptEventAsync(
            actor,
            targetId,
            canonicalRequest.SchemaVersion,
            canonicalRequest.EventId,
            canonicalRequest.Generation,
            canonicalRequest.Sequence,
            canonicalRequest.ObservedAt,
            "target_delivery",
            CanonicalJson.Serialize(canonicalRequest),
            canonicalRequest,
            metadata,
            cancellationToken);
    }

    public Task<RuntimeAcceptance> RecordTargetReconciliationAsync(
        WorkloadActor actor,
        Guid targetId,
        TargetDeliveryInput request,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireSupervisor(actor);
        TargetDeliveryInput canonicalRequest = request with
        {
            ObservedAt = ToPostgresMicrosecond(request.ObservedAt)
        };
        ValidateTargetInput(targetId, canonicalRequest);
        return AcceptEventAsync(
            actor,
            targetId,
            canonicalRequest.SchemaVersion,
            canonicalRequest.EventId,
            canonicalRequest.Generation,
            canonicalRequest.Sequence,
            canonicalRequest.ObservedAt,
            "target_reconciliation",
            CanonicalJson.Serialize(canonicalRequest),
            canonicalRequest,
            metadata,
            cancellationToken);
    }

    private async Task<RuntimeAcceptance> AcceptEventAsync(
        WorkloadActor actor,
        Guid? targetId,
        int schemaVersion,
        Guid eventId,
        long generation,
        long sequence,
        DateTimeOffset observedAt,
        string eventKind,
        string payload,
        TargetDeliveryInput? targetInput,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        observedAt = ToPostgresMicrosecond(observedAt);
        if (generation != actor.Generation)
        {
            throw WrongRuntimeBinding();
        }

        if (Encoding.UTF8.GetByteCount(payload) > options.MaximumEventPayloadBytes)
        {
            throw new DomainException("RUNTIME_EVENT_TOO_LARGE", "The runtime event payload exceeds the configured limit.");
        }

        string payloadSha256 = Sha256Utf8(payload);
        await using TenantPostgresTransaction transaction = await BeginRuntimeAsync(actor, metadata, cancellationToken)
            .ConfigureAwait(false);
        RuntimeBindingSnapshot binding = await LoadBindingAsync(transaction, actor, true, cancellationToken)
            .ConfigureAwait(false);
        DateTimeOffset authorizationNow = binding.AuthorizationNow;
        ValidateEventEnvelope(
            generation,
            sequence,
            schemaVersion,
            eventId,
            observedAt,
            authorizationNow,
            options);
        if (binding.AssignmentExpiresAt <= authorizationNow
            || binding.AssignmentState is "revoked" or "failed")
        {
            throw new ResourceConflictException(
                "WORKER_ASSIGNMENT_INACTIVE",
                "The worker assignment cannot accept runtime events.");
        }

        TargetSnapshot? target = targetId is null
            ? null
            : await LoadTargetAsync(transaction, actor, binding, targetId.Value, cancellationToken)
                .ConfigureAwait(false);

        await EnsureCursorAsync(transaction, actor, targetId, authorizationNow, cancellationToken)
            .ConfigureAwait(false);
        CursorSnapshot cursor = await LockCursorAsync(transaction, actor, targetId, cancellationToken)
            .ConfigureAwait(false);

        InboxReplay? replay = await LoadInboxReplayAsync(transaction, actor, eventId, cancellationToken)
            .ConfigureAwait(false);
        if (replay is not null)
        {
            if (replay.TargetId != targetId
                || replay.Sequence != sequence
                || replay.SchemaVersion != schemaVersion
                || !string.Equals(replay.EventKind, eventKind, StringComparison.Ordinal)
                || !FixedTimeEquals(replay.PayloadSha256, payloadSha256)
                || replay.ObservedAt != observedAt.ToUniversalTime())
            {
                throw new ResourceConflictException(
                    "RUNTIME_EVENT_ID_REUSED",
                    "The runtime event identifier was already used for a different envelope.");
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new RuntimeAcceptance(eventId, "duplicate", checked(cursor.LastAcceptedSequence + 1));
        }

        long expectedSequence = checked(cursor.LastAcceptedSequence + 1);
        if (sequence != expectedSequence)
        {
            throw new ResourceConflictException(
                sequence < expectedSequence ? "RUNTIME_EVENT_SEQUENCE_STALE" : "RUNTIME_EVENT_SEQUENCE_GAP",
                "The runtime event sequence is not the next expected value.");
        }

        Guid inboxId = Guid.CreateVersion7();
        string processingState = target is null ? "accepted" : "applied";
        await InsertInboxAsync(
            transaction,
            actor,
            targetId,
            schemaVersion,
            eventId,
            sequence,
            observedAt,
            eventKind,
            payload,
            payloadSha256,
            processingState,
            authorizationNow,
            inboxId,
            cancellationToken).ConfigureAwait(false);

        if (target is not null && targetInput is not null)
        {
            await ApplyTargetTransitionAsync(
                transaction,
                target,
                targetInput,
                eventKind,
                authorizationNow,
                cancellationToken).ConfigureAwait(false);
        }

        await using (NpgsqlCommand updateCursor = transaction.CreateCommand(
            """
            update operations.runtime_event_cursors
               set last_accepted_sequence = @sequence,
                   last_event_id = @event_id,
                   row_version = row_version + 1,
                   updated_at = @now
             where tenant_id = @tenant_id
               and id = @cursor_id
               and row_version = @cursor_version
            """))
        {
            updateCursor.Parameters.AddWithValue("sequence", NpgsqlDbType.Bigint, sequence);
            AddUuid(updateCursor, "event_id", eventId);
            updateCursor.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, authorizationNow);
            AddUuid(updateCursor, "tenant_id", actor.TenantId);
            AddUuid(updateCursor, "cursor_id", cursor.Id);
            updateCursor.Parameters.AddWithValue("cursor_version", NpgsqlDbType.Bigint, cursor.RowVersion);
            if (await updateCursor.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                throw new ResourceConflictException(
                    "RUNTIME_EVENT_CURSOR_CONFLICT",
                    "The runtime event cursor changed concurrently.");
            }
        }

        await AppendEvidenceAsync(
            transaction,
            target is null ? "runtime.deployment_event_accepted" : $"runtime.{eventKind}_applied",
            target is null ? "deployment" : "command_target",
            targetId ?? actor.DeploymentId,
            metadata,
            eventId,
            new
            {
                eventId,
                deploymentId = actor.DeploymentId,
                targetId,
                workerInstanceId = actor.WorkerInstanceId,
                generation,
                sequence,
                schemaVersion,
                eventKind,
                payloadSha256,
                processingState
            },
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new RuntimeAcceptance(eventId, processingState, checked(sequence + 1));
    }

    private static async Task<TargetSnapshot> LoadTargetAsync(
        TenantPostgresTransaction transaction,
        WorkloadActor actor,
        RuntimeBindingSnapshot binding,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select resource_id, resource_type, resource_version, required_proof,
                   worker_id, generation, state, row_version
            from control.command_targets
            where tenant_id = @tenant_id and id = @target_id
            for update
            """);
        AddUuid(command, "tenant_id", actor.TenantId);
        AddUuid(command, "target_id", targetId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw WrongRuntimeBinding();
        }

        Guid resourceId = reader.GetGuid(0);
        string resourceType = reader.GetString(1);
        long resourceVersion = reader.GetInt64(2);
        Guid? workerId = reader.IsDBNull(4) ? null : reader.GetGuid(4);
        long? generation = reader.IsDBNull(5) ? null : reader.GetInt64(5);
        bool exactResource = string.Equals(resourceType, "deployment", StringComparison.Ordinal)
            && resourceId == actor.DeploymentId
            && resourceVersion == binding.DeploymentVersion
            || string.Equals(resourceType, "broker_account", StringComparison.Ordinal)
            && resourceId == actor.BrokerAccountId
            && resourceVersion == binding.BrokerAccountVersion;
        if (!exactResource || workerId != actor.WorkerInstanceId || generation != actor.Generation)
        {
            throw WrongRuntimeBinding();
        }

        return new TargetSnapshot(
            targetId,
            reader.GetString(3),
            reader.GetString(6),
            reader.GetInt64(7));
    }

    private static async Task EnsureCursorAsync(
        TenantPostgresTransaction transaction,
        WorkloadActor actor,
        Guid? targetId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into operations.runtime_event_cursors
            (
                id, tenant_id, deployment_id, target_id, worker_instance_id,
                generation, last_accepted_sequence, last_event_id,
                row_version, created_at, updated_at
            )
            values
            (
                @id, @tenant_id, @deployment_id, @target_id, @worker_id,
                @generation, 0, null, 0, @now, @now
            )
            on conflict (tenant_id, deployment_id, target_id, generation)
            do nothing
            """);
        AddUuid(command, "id", Guid.CreateVersion7());
        AddUuid(command, "tenant_id", actor.TenantId);
        AddUuid(command, "deployment_id", actor.DeploymentId);
        command.Parameters.AddWithValue("target_id", NpgsqlDbType.Uuid, targetId is null ? DBNull.Value : targetId.Value);
        AddUuid(command, "worker_id", actor.WorkerInstanceId);
        command.Parameters.AddWithValue("generation", NpgsqlDbType.Bigint, actor.Generation);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<CursorSnapshot> LockCursorAsync(
        TenantPostgresTransaction transaction,
        WorkloadActor actor,
        Guid? targetId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select id, worker_instance_id, last_accepted_sequence, row_version
            from operations.runtime_event_cursors
            where tenant_id = @tenant_id
              and deployment_id = @deployment_id
              and target_id is not distinct from @target_id
              and generation = @generation
            for update
            """);
        AddUuid(command, "tenant_id", actor.TenantId);
        AddUuid(command, "deployment_id", actor.DeploymentId);
        command.Parameters.AddWithValue("target_id", NpgsqlDbType.Uuid, targetId is null ? DBNull.Value : targetId.Value);
        command.Parameters.AddWithValue("generation", NpgsqlDbType.Bigint, actor.Generation);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            || reader.GetGuid(1) != actor.WorkerInstanceId)
        {
            throw WrongRuntimeBinding();
        }

        return new CursorSnapshot(reader.GetGuid(0), reader.GetInt64(2), reader.GetInt64(3));
    }

    private static async Task<InboxReplay?> LoadInboxReplayAsync(
        TenantPostgresTransaction transaction,
        WorkloadActor actor,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select target_id, sequence, schema_version, event_kind, payload_sha256, observed_at
            from operations.runtime_event_inbox
            where tenant_id = @tenant_id
              and deployment_id = @deployment_id
              and generation = @generation
              and event_id = @event_id
            """);
        AddUuid(command, "tenant_id", actor.TenantId);
        AddUuid(command, "deployment_id", actor.DeploymentId);
        command.Parameters.AddWithValue("generation", NpgsqlDbType.Bigint, actor.Generation);
        AddUuid(command, "event_id", eventId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new InboxReplay(
                reader.IsDBNull(0) ? null : reader.GetGuid(0),
                reader.GetInt64(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetFieldValue<DateTimeOffset>(5))
            : null;
    }

    private static async Task InsertInboxAsync(
        TenantPostgresTransaction transaction,
        WorkloadActor actor,
        Guid? targetId,
        int schemaVersion,
        Guid eventId,
        long sequence,
        DateTimeOffset observedAt,
        string eventKind,
        string payload,
        string payloadSha256,
        string processingState,
        DateTimeOffset now,
        Guid inboxId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into operations.runtime_event_inbox
            (
                id, tenant_id, deployment_id, target_id, worker_instance_id,
                generation, event_id, sequence, schema_version, event_kind,
                payload, payload_sha256, observed_at, received_at,
                processing_state, processed_at, result_code, row_version
            )
            values
            (
                @id, @tenant_id, @deployment_id, @target_id, @worker_id,
                @generation, @event_id, @sequence, @schema_version, @event_kind,
                @payload, @payload_sha256, @observed_at, @received_at,
                @processing_state, @processed_at, null, 0
            )
            """);
        AddUuid(command, "id", inboxId);
        AddUuid(command, "tenant_id", actor.TenantId);
        AddUuid(command, "deployment_id", actor.DeploymentId);
        command.Parameters.AddWithValue("target_id", NpgsqlDbType.Uuid, targetId is null ? DBNull.Value : targetId.Value);
        AddUuid(command, "worker_id", actor.WorkerInstanceId);
        command.Parameters.AddWithValue("generation", NpgsqlDbType.Bigint, actor.Generation);
        AddUuid(command, "event_id", eventId);
        command.Parameters.AddWithValue("sequence", NpgsqlDbType.Bigint, sequence);
        command.Parameters.AddWithValue("schema_version", NpgsqlDbType.Integer, schemaVersion);
        command.Parameters.AddWithValue("event_kind", NpgsqlDbType.Text, eventKind);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, payload);
        command.Parameters.AddWithValue("payload_sha256", NpgsqlDbType.Text, payloadSha256);
        command.Parameters.AddWithValue("observed_at", NpgsqlDbType.TimestampTz, observedAt.ToUniversalTime());
        command.Parameters.AddWithValue("received_at", NpgsqlDbType.TimestampTz, now);
        command.Parameters.AddWithValue("processing_state", NpgsqlDbType.Text, processingState);
        command.Parameters.AddWithValue("processed_at", NpgsqlDbType.TimestampTz, targetId is null ? DBNull.Value : now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyTargetTransitionAsync(
        TenantPostgresTransaction transaction,
        TargetSnapshot target,
        TargetDeliveryInput input,
        string eventKind,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        RuntimeTargetTransition transition = RuntimeTargetTransition.Create(
            target.State,
            input,
            eventKind,
            now);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            update control.command_targets
               set state = @state,
                   delivered_at = coalesce(@delivered_at, delivered_at),
                   acknowledged_at = coalesce(@acknowledged_at, acknowledged_at),
                   applied_at = coalesce(@applied_at, applied_at),
                   reconciled_at = coalesce(@reconciled_at, reconciled_at),
                   observed_result = coalesce(@observed_result, observed_result),
                   broker_evidence_reference = coalesce(@broker_evidence_reference, broker_evidence_reference),
                   last_error_code = @last_error_code,
                   row_version = row_version + 1,
                   updated_at = @now
             where tenant_id = @tenant_id
               and id = @target_id
               and row_version = @row_version
               and state = @previous_state
            """);
        command.Parameters.AddWithValue("state", NpgsqlDbType.Text, transition.State);
        AddNullableTimestamp(command, "delivered_at", transition.DeliveredAt);
        AddNullableTimestamp(command, "acknowledged_at", transition.AcknowledgedAt);
        AddNullableTimestamp(command, "applied_at", transition.AppliedAt);
        AddNullableTimestamp(command, "reconciled_at", transition.ReconciledAt);
        AddNullableText(command, "observed_result", transition.ObservedResult);
        AddNullableText(command, "broker_evidence_reference", transition.BrokerEvidenceReference);
        AddNullableText(command, "last_error_code", transition.LastErrorCode);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        AddUuid(command, "tenant_id", transaction.Context.TenantId);
        AddUuid(command, "target_id", target.Id);
        command.Parameters.AddWithValue("row_version", NpgsqlDbType.Bigint, target.RowVersion);
        command.Parameters.AddWithValue("previous_state", NpgsqlDbType.Text, target.State);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new ResourceConflictException(
                "COMMAND_TARGET_STATE_CONFLICT",
                "The command target changed concurrently.");
        }
    }

    private static void ValidateTargetInput(Guid targetId, TargetDeliveryInput request)
    {
        if (targetId == Guid.Empty
            || request.Evidence.ValueKind != JsonValueKind.Object
            || string.IsNullOrWhiteSpace(request.State)
            || request.State.Length > 100
            || request.ErrorCode?.Length > 200
            || request.ObservedResult?.Length > 2000
            || request.BrokerEvidenceReference?.Length > 2000)
        {
            throw new DomainException("TARGET_EVIDENCE_INVALID", "The target evidence envelope is invalid.");
        }
    }

    private static void AddNullableTimestamp(NpgsqlCommand command, string name, DateTimeOffset? value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.TimestampTz, value is null ? DBNull.Value : value.Value);

    private static void AddNullableText(NpgsqlCommand command, string name, string? value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Text, value is null ? DBNull.Value : value.Trim());

    private static DateTimeOffset ToPostgresMicrosecond(DateTimeOffset value)
    {
        DateTimeOffset utc = value.ToUniversalTime();
        long ticks = utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private sealed record CursorSnapshot(Guid Id, long LastAcceptedSequence, long RowVersion);

    private sealed record InboxReplay(
        Guid? TargetId,
        long Sequence,
        int SchemaVersion,
        string EventKind,
        string PayloadSha256,
        DateTimeOffset ObservedAt);

    private sealed record TargetSnapshot(Guid Id, string RequiredProof, string State, long RowVersion);

}
