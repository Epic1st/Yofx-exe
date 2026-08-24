using YO4X.BuildingBlocks;

namespace YO4X.Outbox;

public enum OutboxMessageState
{
    Pending,
    Processing,
    Published,
    DeadLetter
}

public sealed record OutboxMessage
{
    private OutboxMessage(
        Guid id,
        Guid tenantId,
        string messageType,
        int schemaVersion,
        string aggregateType,
        string aggregateId,
        string payloadJson,
        string payloadSha256,
        Guid correlationId,
        Guid? causationId,
        DateTimeOffset occurredAt,
        DateTimeOffset availableAt)
    {
        Id = id;
        TenantId = tenantId;
        MessageType = messageType;
        SchemaVersion = schemaVersion;
        AggregateType = aggregateType;
        AggregateId = aggregateId;
        PayloadJson = payloadJson;
        PayloadSha256 = payloadSha256;
        CorrelationId = correlationId;
        CausationId = causationId;
        OccurredAt = occurredAt;
        AvailableAt = availableAt;
    }

    public Guid Id { get; }

    public Guid TenantId { get; }

    public string MessageType { get; }

    public int SchemaVersion { get; }

    public string AggregateType { get; }

    public string AggregateId { get; }

    public string PayloadJson { get; }

    public string PayloadSha256 { get; }

    public Guid CorrelationId { get; }

    public Guid? CausationId { get; }

    public DateTimeOffset OccurredAt { get; }

    public DateTimeOffset AvailableAt { get; }

    public static OutboxMessage Create<TPayload>(
        Guid tenantId,
        string messageType,
        string aggregateType,
        string aggregateId,
        TPayload payload,
        Guid correlationId,
        Guid? causationId,
        DateTimeOffset occurredAt,
        DateTimeOffset? availableAt = null)
    {
        RequireIdentifier(tenantId, nameof(tenantId));
        RequireIdentifier(correlationId, nameof(correlationId));
        if (causationId == Guid.Empty)
        {
            throw new ArgumentException("A causation identifier cannot be empty.", nameof(causationId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateType);
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);
        if (messageType.Trim().Length > 300)
        {
            throw new ArgumentOutOfRangeException(nameof(messageType), "A message type cannot exceed 300 characters.");
        }

        if (aggregateType.Trim().Length > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(aggregateType), "An aggregate type cannot exceed 200 characters.");
        }

        if (aggregateId.Trim().Length > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(aggregateId), "An aggregate identifier cannot exceed 500 characters.");
        }

        DateTimeOffset normalizedOccurredAt = occurredAt.ToUniversalTime();
        DateTimeOffset normalizedAvailableAt = (availableAt ?? normalizedOccurredAt).ToUniversalTime();

        return new OutboxMessage(
            Identifiers.NewId(),
            tenantId,
            messageType.Trim(),
            OutboxSchemaVersion.ResolveForNewMessage(messageType.Trim()),
            aggregateType.Trim(),
            aggregateId.Trim(),
            CanonicalJson.Serialize(payload),
            CanonicalJson.Sha256(payload),
            correlationId,
            causationId,
            normalizedOccurredAt,
            normalizedAvailableAt);
    }

    private static void RequireIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An identifier is required.", parameterName);
        }
    }
}

public sealed record ClaimedOutboxMessage(
    Guid Id,
    Guid TenantId,
    string MessageType,
    int SchemaVersion,
    string AggregateType,
    string AggregateId,
    string PayloadJson,
    string PayloadSha256,
    Guid CorrelationId,
    Guid? CausationId,
    DateTimeOffset OccurredAt,
    DateTimeOffset AvailableAt,
    int Attempts,
    string LockedBy,
    DateTimeOffset LockedUntil);
