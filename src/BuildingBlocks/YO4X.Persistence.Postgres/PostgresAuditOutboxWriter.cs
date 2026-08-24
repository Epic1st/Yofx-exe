using Npgsql;
using NpgsqlTypes;
using YO4X.Audit;
using YO4X.Outbox;

namespace YO4X.Persistence.Postgres;

/// <summary>
/// Appends audit evidence and its integration event to the caller's existing
/// business transaction. A commit can therefore make all three effects visible
/// atomically; disposal without commit rolls every effect back.
/// </summary>
public static class PostgresAuditOutboxWriter
{
    private const string InsertAuditSql = """
        insert into audit.audit_events
        (
            id,
            tenant_id,
            actor_id,
            category,
            action,
            target_type,
            target_id,
            outcome,
            reason,
            correlation_id,
            causation_id,
            payload,
            payload_sha256,
            session_id,
            device_id,
            assurance,
            source_network_class,
            effective_policy_digest,
            policy_version_watermark,
            policy_input_sha256,
            resource_version_before,
            resource_version_after,
            occurred_at
        )
        values
        (
            @id,
            @tenant_id,
            @actor_id,
            @category,
            @action,
            @target_type,
            @target_id,
            @outcome,
            @reason,
            @correlation_id,
            @causation_id,
            @payload,
            @payload_sha256,
            @session_id,
            @device_id,
            @assurance,
            @source_network_class,
            @effective_policy_digest,
            @policy_version_watermark,
            @policy_input_sha256,
            @resource_version_before,
            @resource_version_after,
            @occurred_at
        )
        """;

    private const string InsertOutboxSql = """
        insert into messaging.outbox_messages
        (
            id,
            tenant_id,
            message_type,
            schema_version,
            aggregate_type,
            aggregate_id,
            payload,
            payload_sha256,
            correlation_id,
            causation_id,
            occurred_at,
            available_at
        )
        values
        (
            @id,
            @tenant_id,
            @message_type,
            @schema_version,
            @aggregate_type,
            @aggregate_id,
            @payload,
            @payload_sha256,
            @correlation_id,
            @causation_id,
            @occurred_at,
            @available_at
        )
        """;

    public static async Task AppendAsync(
        TenantPostgresTransaction transaction,
        AuditEvent auditEvent,
        OutboxMessage outboxMessage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(auditEvent);
        ArgumentNullException.ThrowIfNull(outboxMessage);

        Guid tenantId = transaction.Context.TenantId;
        if (auditEvent.TenantId != tenantId || outboxMessage.TenantId != tenantId)
        {
            throw new InvalidOperationException("Audit and outbox records must match the transaction tenant.");
        }

        if (auditEvent.ActorId != transaction.Context.ActorId)
        {
            throw new InvalidOperationException("The audit actor must match the transaction actor.");
        }

        if (auditEvent.CorrelationId != transaction.Context.CorrelationId
            || outboxMessage.CorrelationId != transaction.Context.CorrelationId)
        {
            throw new InvalidOperationException("Audit and outbox correlation must match the transaction context.");
        }

        await InsertAuditAsync(transaction, auditEvent, cancellationToken).ConfigureAwait(false);
        await InsertOutboxAsync(transaction, outboxMessage, cancellationToken).ConfigureAwait(false);
    }

    public static async Task AppendAuditAsync(
        TenantPostgresTransaction transaction,
        AuditEvent auditEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(auditEvent);
        if (auditEvent.TenantId != transaction.Context.TenantId
            || auditEvent.ActorId != transaction.Context.ActorId
            || auditEvent.CorrelationId != transaction.Context.CorrelationId)
        {
            throw new InvalidOperationException("Audit evidence must match the transaction context.");
        }

        await InsertAuditAsync(transaction, auditEvent, cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertAuditAsync(
        TenantPostgresTransaction transaction,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(InsertAuditSql);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, auditEvent.Id);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, auditEvent.TenantId);
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Uuid, auditEvent.ActorId);
        command.Parameters.AddWithValue("category", NpgsqlDbType.Text, auditEvent.Category.ToStorageValue());
        command.Parameters.AddWithValue("action", NpgsqlDbType.Text, auditEvent.Action);
        command.Parameters.AddWithValue("target_type", NpgsqlDbType.Text, auditEvent.TargetType);
        AddNullableText(command, "target_id", auditEvent.TargetId);
        command.Parameters.AddWithValue("outcome", NpgsqlDbType.Text, auditEvent.Outcome.ToStorageValue());
        AddNullableText(command, "reason", auditEvent.Reason);
        command.Parameters.AddWithValue("correlation_id", NpgsqlDbType.Uuid, auditEvent.CorrelationId);
        AddNullableUuid(command, "causation_id", auditEvent.CausationId);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, auditEvent.PayloadJson);
        command.Parameters.AddWithValue("payload_sha256", NpgsqlDbType.Text, auditEvent.PayloadSha256);
        AuditEvidenceContext? evidence = auditEvent.EvidenceContext;
        AddNullableUuid(command, "session_id", evidence?.SessionId);
        AddNullableUuid(command, "device_id", evidence?.DeviceId);
        AddNullableText(command, "assurance", evidence?.Assurance);
        AddNullableText(command, "source_network_class", evidence?.SourceNetworkClass);
        AddNullableText(command, "effective_policy_digest", evidence?.EffectivePolicyDigest);
        AddNullableText(command, "policy_version_watermark", evidence?.PolicyVersionWatermark);
        AddNullableText(command, "policy_input_sha256", evidence?.PolicyInputSha256);
        AddNullableLong(command, "resource_version_before", evidence?.ResourceVersionBefore);
        AddNullableLong(command, "resource_version_after", evidence?.ResourceVersionAfter);
        command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, auditEvent.OccurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async Task InsertOutboxAsync(
        TenantPostgresTransaction transaction,
        OutboxMessage message,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(InsertOutboxSql);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, message.Id);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, message.TenantId);
        command.Parameters.AddWithValue("message_type", NpgsqlDbType.Text, message.MessageType);
        command.Parameters.AddWithValue(
            "schema_version",
            NpgsqlDbType.Smallint,
            checked((short)message.SchemaVersion));
        command.Parameters.AddWithValue("aggregate_type", NpgsqlDbType.Text, message.AggregateType);
        command.Parameters.AddWithValue("aggregate_id", NpgsqlDbType.Text, message.AggregateId);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, message.PayloadJson);
        command.Parameters.AddWithValue("payload_sha256", NpgsqlDbType.Text, message.PayloadSha256);
        command.Parameters.AddWithValue("correlation_id", NpgsqlDbType.Uuid, message.CorrelationId);
        AddNullableUuid(command, "causation_id", message.CausationId);
        command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, message.OccurredAt);
        command.Parameters.AddWithValue("available_at", NpgsqlDbType.TimestampTz, message.AvailableAt);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddNullableText(NpgsqlCommand command, string name, string? value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Text, value is null ? DBNull.Value : value);

    private static void AddNullableUuid(NpgsqlCommand command, string name, Guid? value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Uuid, value is null ? DBNull.Value : value.Value);

    private static void AddNullableLong(NpgsqlCommand command, string name, long? value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Bigint, value is null ? DBNull.Value : value.Value);
}
