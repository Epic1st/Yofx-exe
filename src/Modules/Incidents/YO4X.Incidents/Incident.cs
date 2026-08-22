using YO4X.BuildingBlocks;

namespace YO4X.Incidents;

public enum IncidentSeverity
{
    Sev4,
    Sev3,
    Sev2,
    Sev1
}

public enum IncidentState
{
    Open,
    Containing,
    Monitoring,
    Resolved
}

public sealed record IncidentUpdate(
    Guid Id,
    IncidentState State,
    string Summary,
    string ActorId,
    DateTimeOffset OccurredAt);

public sealed class Incident : VersionedAggregate
{
    private readonly List<IncidentUpdate> _updates = [];

    private Incident(Guid id, string title, IncidentSeverity severity, DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        Title = title;
        Severity = severity;
        State = IncidentState.Open;
    }

    public string Title { get; }

    public IncidentSeverity Severity { get; }

    public IncidentState State { get; private set; }

    public IReadOnlyList<IncidentUpdate> Updates => _updates;

    public static Incident Open(string title, IncidentSeverity severity, string actorId, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        var incident = new Incident(Identifiers.NewId(), title.Trim(), severity, clock.UtcNow);
        incident._updates.Add(new IncidentUpdate(Identifiers.NewId(), IncidentState.Open, "Incident opened.", actorId, clock.UtcNow));
        return incident;
    }

    public void AddUpdate(IncidentState state, string summary, string actorId, DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        if (State == IncidentState.Resolved)
        {
            throw new DomainException("INCIDENT_ALREADY_RESOLVED", "A resolved incident cannot be changed in place.");
        }

        State = state;
        _updates.Add(new IncidentUpdate(Identifiers.NewId(), state, summary.Trim(), actorId, occurredAt.ToUniversalTime()));
        RecordChange(occurredAt);
    }
}
