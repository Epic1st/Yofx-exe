using YO4X.BuildingBlocks;

namespace YO4X.Privacy;

public enum PrivacyRequestKind
{
    Access,
    Correction,
    Deletion,
    Restriction
}

public enum PrivacyRequestState
{
    Received,
    IdentityVerified,
    Previewed,
    WaitingApproval,
    Approved,
    Processing,
    QualityCheck,
    Completed,
    BlockedByLegalHold,
    Rejected
}

public sealed class PrivacyRequest : VersionedAggregate
{
    private PrivacyRequest(Guid id, Guid tenantId, Guid userId, PrivacyRequestKind kind, DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        TenantId = tenantId;
        UserId = userId;
        Kind = kind;
        State = PrivacyRequestState.Received;
    }

    public Guid TenantId { get; }

    public Guid UserId { get; }

    public PrivacyRequestKind Kind { get; }

    public PrivacyRequestState State { get; private set; }

    public bool HasLegalHold { get; private set; }

    public string? CompletionEvidenceDigest { get; private set; }

    public static PrivacyRequest Receive(Guid tenantId, Guid userId, PrivacyRequestKind kind, IClock clock)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty)
        {
            throw new ArgumentException("Tenant and user identifiers are required.");
        }

        return new PrivacyRequest(Identifiers.NewId(), tenantId, userId, kind, clock.UtcNow);
    }

    public void ApplyLegalHold(DateTimeOffset occurredAt)
    {
        HasLegalHold = true;
        State = PrivacyRequestState.BlockedByLegalHold;
        RecordChange(occurredAt);
    }

    public void BeginProcessing(DateTimeOffset occurredAt)
    {
        if (HasLegalHold)
        {
            throw new DomainException("PRIVACY_LEGAL_HOLD", "The request cannot be processed while a legal hold applies.");
        }

        if (State != PrivacyRequestState.Approved)
        {
            throw new DomainException("PRIVACY_APPROVAL_REQUIRED", "The request must be previewed and approved before processing.");
        }

        State = PrivacyRequestState.Processing;
        RecordChange(occurredAt);
    }
}
