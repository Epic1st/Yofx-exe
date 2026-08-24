using YO4X.BuildingBlocks;

namespace YO4X.Tenancy;

/// <summary>
/// Identifies the tenant and actor for one database transaction. The context is
/// deliberately explicit so pooled PostgreSQL connections never carry ambient
/// authorization state between requests.
/// </summary>
public sealed record TenantExecutionContext
{
    public TenantExecutionContext(
        Guid tenantId,
        Guid actorId,
        Guid correlationId,
        Guid? sessionId = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant identifier is required.", nameof(tenantId));
        }

        if (actorId == Guid.Empty)
        {
            throw new ArgumentException("An actor identifier is required.", nameof(actorId));
        }

        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("A correlation identifier is required.", nameof(correlationId));
        }

        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("A session identifier cannot be empty.", nameof(sessionId));
        }

        TenantId = tenantId;
        ActorId = actorId;
        CorrelationId = correlationId;
        SessionId = sessionId;
    }

    public Guid TenantId { get; }

    public Guid ActorId { get; }

    public Guid CorrelationId { get; }

    public Guid? SessionId { get; }

    public static TenantExecutionContext Create(Guid tenantId, Guid actorId, Guid? sessionId = null) =>
        new(tenantId, actorId, Identifiers.NewId(), sessionId);
}

public sealed record TenantContextEntry(
    Guid Id,
    Guid TenantId,
    Guid ActorId,
    Guid CorrelationId,
    Guid? SessionId,
    DateTimeOffset EstablishedAt,
    DateTimeOffset ExpiresAt)
{
    public static TenantContextEntry Create(
        TenantExecutionContext context,
        DateTimeOffset establishedAt,
        TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Context lifetime must be positive.");
        }

        DateTimeOffset normalized = establishedAt.ToUniversalTime();
        return new TenantContextEntry(
            Identifiers.NewId(),
            context.TenantId,
            context.ActorId,
            context.CorrelationId,
            context.SessionId,
            normalized,
            normalized.Add(lifetime));
    }
}
