namespace YO4X.ControlPlane.Workers.Outbox;

public interface IPostgresOutboxStore
{
    ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<ClaimedOutboxItem>> ClaimAsync(
        OutboxClaimRequest request,
        CancellationToken cancellationToken);

    ValueTask<bool> SettleAsync(
        OutboxSettlement settlement,
        CancellationToken cancellationToken);
}

public interface IOutboxDestination
{
    ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Delivers a message using <see cref="OutboxDeliveryEnvelope.IdempotencyKey"/> as the
    /// stable deduplication identity. Implementations must return <see cref="OutboxDeliveryOutcome.Duplicate"/>
    /// when that same immutable message was previously accepted.
    /// </summary>
    ValueTask<OutboxDeliveryResult> DeliverAsync(
        OutboxDeliveryEnvelope message,
        CancellationToken cancellationToken);
}

public sealed record OutboxClaimRequest(
    string WorkerId,
    int MaximumMessages,
    DateTimeOffset ClaimedAtUtc,
    TimeSpan LeaseDuration)
{
    public OutboxClaimRequest Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(WorkerId);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumMessages, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumMessages, 1_000);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(LeaseDuration, TimeSpan.Zero);
        return this;
    }
}

public sealed class ClaimedOutboxItem
{
    public ClaimedOutboxItem(
        Guid messageId,
        Guid tenantId,
        string messageType,
        int schemaVersion,
        string payloadJson,
        string payloadSha256,
        DateTimeOffset occurredAtUtc,
        int attempt)
    {
        RequireIdentifier(messageId, nameof(messageId));
        RequireIdentifier(tenantId, nameof(tenantId));
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentOutOfRangeException.ThrowIfLessThan(schemaVersion, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadSha256);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);

        if (messageType.Length > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(messageType), "Message type cannot exceed 256 characters.");
        }

        if (!PayloadHash.IsSha256(payloadSha256))
        {
            throw new ArgumentException("Payload hash must be a hexadecimal SHA-256 value.", nameof(payloadSha256));
        }

        MessageId = messageId;
        TenantId = tenantId;
        MessageType = messageType.Trim();
        SchemaVersion = schemaVersion;
        PayloadJson = payloadJson;
        PayloadSha256 = payloadSha256.ToLowerInvariant();
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
        Attempt = attempt;
    }

    public Guid MessageId { get; }

    public Guid TenantId { get; }

    public string MessageType { get; }

    public int SchemaVersion { get; }

    public string PayloadJson { get; }

    public string PayloadSha256 { get; }

    public DateTimeOffset OccurredAtUtc { get; }

    public int Attempt { get; }

    private static void RequireIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An identifier is required.", parameterName);
        }
    }
}

public enum OutboxDeliveryOutcome
{
    Accepted,
    Duplicate,
    RetryableFailure,
    PermanentFailure,
    Unavailable
}

public sealed record OutboxDeliveryResult
{
    private OutboxDeliveryResult(OutboxDeliveryOutcome outcome, string code)
    {
        Outcome = outcome;
        Code = FailureCode.Normalize(code);
    }

    public OutboxDeliveryOutcome Outcome { get; }

    public string Code { get; }

    public static OutboxDeliveryResult Accepted { get; } = new(OutboxDeliveryOutcome.Accepted, "accepted");

    public static OutboxDeliveryResult Duplicate { get; } = new(OutboxDeliveryOutcome.Duplicate, "duplicate");

    public static OutboxDeliveryResult Retryable(string code) => new(OutboxDeliveryOutcome.RetryableFailure, code);

    public static OutboxDeliveryResult Permanent(string code) => new(OutboxDeliveryOutcome.PermanentFailure, code);

    public static OutboxDeliveryResult DestinationUnavailable(string code) => new(OutboxDeliveryOutcome.Unavailable, code);
}

public enum OutboxSettlementKind
{
    Published,
    Retry,
    DeadLetter
}

public sealed record OutboxSettlement(
    Guid MessageId,
    Guid TenantId,
    string WorkerId,
    OutboxSettlementKind Kind,
    DateTimeOffset SettledAtUtc,
    DateTimeOffset? RetryAtUtc,
    string Code)
{
    public OutboxSettlement Validate()
    {
        if (MessageId == Guid.Empty || TenantId == Guid.Empty)
        {
            throw new InvalidOperationException("Settlement identifiers are required.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(WorkerId);
        _ = FailureCode.Normalize(Code);

        if ((Kind == OutboxSettlementKind.Retry) != RetryAtUtc.HasValue)
        {
            throw new InvalidOperationException("Only retry settlements require a retry timestamp.");
        }

        return this;
    }
}

internal static class FailureCode
{
    private const int MaximumLength = 64;

    public static string Normalize(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        string normalized = code.Trim().ToLowerInvariant();
        if (normalized.Length > MaximumLength || normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-' and not '.'))
        {
            throw new ArgumentException("Failure codes must be short ASCII identifiers.", nameof(code));
        }

        return normalized;
    }
}
