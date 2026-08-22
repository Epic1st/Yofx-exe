using System.Text.RegularExpressions;
using YO4X.BuildingBlocks;

namespace YO4X.Support;

public enum SupportCaseState
{
    Open,
    WaitingForUser,
    WaitingForStaff,
    Closed
}

public sealed record SupportNote(Guid Id, string AuthorId, string Body, DateTimeOffset CreatedAt);

public sealed partial class SupportCase : VersionedAggregate
{
    private readonly List<SupportNote> _notes = [];

    private SupportCase(Guid id, Guid? tenantId, string subject, string purpose, DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        TenantId = tenantId;
        Subject = subject;
        Purpose = purpose;
        State = SupportCaseState.Open;
    }

    public Guid? TenantId { get; }

    public string Subject { get; }

    public string Purpose { get; }

    public SupportCaseState State { get; private set; }

    public IReadOnlyList<SupportNote> Notes => _notes;

    public static SupportCase Open(Guid? tenantId, string subject, string purpose, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        return new SupportCase(Identifiers.NewId(), tenantId, subject.Trim(), purpose.Trim(), clock.UtcNow);
    }

    public void AddSanitizedNote(string authorId, string body, DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        if (State == SupportCaseState.Closed)
        {
            throw new DomainException("SUPPORT_CASE_CLOSED", "Notes cannot be added to a closed support case.");
        }

        if (SecretLikePattern().IsMatch(body))
        {
            throw new DomainException("SENSITIVE_SUPPORT_CONTENT_REJECTED", "The note appears to contain secret material.");
        }

        _notes.Add(new SupportNote(Identifiers.NewId(), authorId, body.Trim(), occurredAt.ToUniversalTime()));
        RecordChange(occurredAt);
    }

    public void Close(DateTimeOffset occurredAt)
    {
        if (State != SupportCaseState.Closed)
        {
            State = SupportCaseState.Closed;
            RecordChange(occurredAt);
        }
    }

    [GeneratedRegex("(?i)(password|passwd|secret|authorization|bearer|private[_ -]?key)\\s*[:=]\\s*\\S+", RegexOptions.CultureInvariant)]
    private static partial Regex SecretLikePattern();
}
