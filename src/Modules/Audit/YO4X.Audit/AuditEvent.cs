using YO4X.BuildingBlocks;

namespace YO4X.Audit;

public enum AuditCategory
{
    Authentication,
    Authorization,
    SensitiveRead,
    Support,
    Governance,
    Operations,
    Billing,
    Privacy,
    Release,
    Incident,
    System
}

public enum AuditOutcome
{
    Accepted,
    Succeeded,
    Failed,
    Denied,
    Unknown
}

public sealed record AuditEvidenceContext(
    Guid? SessionId = null,
    Guid? DeviceId = null,
    string? Assurance = null,
    string? SourceNetworkClass = null,
    string? EffectivePolicyDigest = null,
    string? PolicyVersionWatermark = null,
    string? PolicyInputSha256 = null,
    long? ResourceVersionBefore = null,
    long? ResourceVersionAfter = null);

/// <summary>
/// A redacted, append-only administrative evidence record. Payloads must be
/// redacted before construction; the persistence layer never receives secrets
/// to remove later.
/// </summary>
public sealed record AuditEvent
{
    private AuditEvent(
        Guid id,
        Guid tenantId,
        Guid actorId,
        AuditCategory category,
        string action,
        string targetType,
        string? targetId,
        AuditOutcome outcome,
        string? reason,
        Guid correlationId,
        Guid? causationId,
        string payloadJson,
        string payloadSha256,
        DateTimeOffset occurredAt,
        AuditEvidenceContext? evidenceContext)
    {
        Id = id;
        TenantId = tenantId;
        ActorId = actorId;
        Category = category;
        Action = action;
        TargetType = targetType;
        TargetId = targetId;
        Outcome = outcome;
        Reason = reason;
        CorrelationId = correlationId;
        CausationId = causationId;
        PayloadJson = payloadJson;
        PayloadSha256 = payloadSha256;
        OccurredAt = occurredAt;
        EvidenceContext = evidenceContext;
    }

    public Guid Id { get; }

    public Guid TenantId { get; }

    public Guid ActorId { get; }

    public AuditCategory Category { get; }

    public string Action { get; }

    public string TargetType { get; }

    public string? TargetId { get; }

    public AuditOutcome Outcome { get; }

    public string? Reason { get; }

    public Guid CorrelationId { get; }

    public Guid? CausationId { get; }

    public string PayloadJson { get; }

    public string PayloadSha256 { get; }

    public DateTimeOffset OccurredAt { get; }

    public AuditEvidenceContext? EvidenceContext { get; }

    public static AuditEvent Create<TPayload>(
        Guid tenantId,
        Guid actorId,
        AuditCategory category,
        string action,
        string targetType,
        string? targetId,
        AuditOutcome outcome,
        string? reason,
        Guid correlationId,
        Guid? causationId,
        TPayload redactedPayload,
        DateTimeOffset occurredAt,
        AuditEvidenceContext? evidenceContext = null)
    {
        RequireIdentifier(tenantId, nameof(tenantId));
        RequireIdentifier(actorId, nameof(actorId));
        RequireIdentifier(correlationId, nameof(correlationId));
        if (causationId == Guid.Empty)
        {
            throw new ArgumentException("A causation identifier cannot be empty.", nameof(causationId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetType);
        if (action.Trim().Length > 300)
        {
            throw new ArgumentOutOfRangeException(nameof(action), "An audit action cannot exceed 300 characters.");
        }

        if (targetType.Trim().Length > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(targetType), "An audit target type cannot exceed 200 characters.");
        }

        return new AuditEvent(
            Identifiers.NewId(),
            tenantId,
            actorId,
            category,
            action.Trim(),
            targetType.Trim(),
            NormalizeOptional(targetId),
            outcome,
            NormalizeOptional(reason),
            correlationId,
            causationId,
            CanonicalJson.Serialize(redactedPayload),
            CanonicalJson.Sha256(redactedPayload),
            occurredAt.ToUniversalTime(),
            ValidateEvidenceContext(evidenceContext));
    }

    private static void RequireIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An identifier is required.", parameterName);
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AuditEvidenceContext? ValidateEvidenceContext(AuditEvidenceContext? context)
    {
        if (context is null)
        {
            return null;
        }

        if (context.SessionId == Guid.Empty || context.DeviceId == Guid.Empty)
        {
            throw new ArgumentException("Audit context identifiers cannot be empty.", nameof(context));
        }

        string? assurance = NormalizeOptional(context.Assurance)?.ToLowerInvariant();
        if (assurance is not null and not ("password" or "totp" or "webauthn" or "hardware_key" or "workload"))
        {
            throw new ArgumentException("The audit assurance is not allowlisted.", nameof(context));
        }

        string? sourceNetworkClass = NormalizeOptional(context.SourceNetworkClass)?.ToLowerInvariant();
        if (sourceNetworkClass is not null and not ("unknown" or "loopback" or "private" or "public" or "trusted_proxy"))
        {
            throw new ArgumentException("The audit source-network class is not allowlisted.", nameof(context));
        }

        ValidateOptionalDigest(context.EffectivePolicyDigest, nameof(context.EffectivePolicyDigest));
        ValidateOptionalDigest(context.PolicyVersionWatermark, nameof(context.PolicyVersionWatermark));
        ValidateOptionalDigest(context.PolicyInputSha256, nameof(context.PolicyInputSha256));
        if (context.ResourceVersionBefore < 0 || context.ResourceVersionAfter < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(context), "Audit resource versions cannot be negative.");
        }

        return context with
        {
            Assurance = assurance,
            SourceNetworkClass = sourceNetworkClass,
            EffectivePolicyDigest = NormalizeOptional(context.EffectivePolicyDigest)?.ToLowerInvariant(),
            PolicyVersionWatermark = NormalizeOptional(context.PolicyVersionWatermark)?.ToLowerInvariant(),
            PolicyInputSha256 = NormalizeOptional(context.PolicyInputSha256)?.ToLowerInvariant()
        };
    }

    private static void ValidateOptionalDigest(string? value, string parameterName)
    {
        if (value is not null
            && (value.Length != 64
                || value.Any(character => character is not (>= '0' and <= '9')
                    and not (>= 'A' and <= 'F')
                    and not (>= 'a' and <= 'f'))))
        {
            throw new ArgumentException("Audit policy evidence requires a hexadecimal SHA-256 digest.", parameterName);
        }
    }
}

public static class AuditStorageValues
{
    public static string ToStorageValue(this AuditCategory category) => category switch
    {
        AuditCategory.Authentication => "authentication",
        AuditCategory.Authorization => "authorization",
        AuditCategory.SensitiveRead => "sensitive_read",
        AuditCategory.Support => "support",
        AuditCategory.Governance => "governance",
        AuditCategory.Operations => "operations",
        AuditCategory.Billing => "billing",
        AuditCategory.Privacy => "privacy",
        AuditCategory.Release => "release",
        AuditCategory.Incident => "incident",
        AuditCategory.System => "system",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown audit category.")
    };

    public static string ToStorageValue(this AuditOutcome outcome) => outcome switch
    {
        AuditOutcome.Accepted => "accepted",
        AuditOutcome.Succeeded => "succeeded",
        AuditOutcome.Failed => "failed",
        AuditOutcome.Denied => "denied",
        AuditOutcome.Unknown => "unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown audit outcome.")
    };
}
