using YO4X.BuildingBlocks;

namespace YO4X.AdminIdentity;

public enum AdminAssurance
{
    Unknown,
    Password,
    WebAuthn,
    HardwareKey
}

public enum AdminSessionState
{
    Active,
    Revoked,
    Expired
}

public sealed class AdminSession : VersionedAggregate
{
    private AdminSession(
        Guid id,
        string oidcSubject,
        AdminAssurance assurance,
        bool managedDevice,
        DateTimeOffset expiresAt,
        DateTimeOffset authenticatedAt)
        : base(id, authenticatedAt)
    {
        OidcSubject = oidcSubject;
        Assurance = assurance;
        ManagedDevice = managedDevice;
        ExpiresAt = expiresAt.ToUniversalTime();
        AuthenticatedAt = authenticatedAt.ToUniversalTime();
        State = AdminSessionState.Active;
    }

    public string OidcSubject { get; }

    public AdminAssurance Assurance { get; private set; }

    public bool ManagedDevice { get; }

    public DateTimeOffset AuthenticatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public AdminSessionState State { get; private set; }

    public static AdminSession Start(
        string oidcSubject,
        AdminAssurance assurance,
        bool managedDevice,
        DateTimeOffset expiresAt,
        IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oidcSubject);
        if (!managedDevice || assurance is not (AdminAssurance.WebAuthn or AdminAssurance.HardwareKey))
        {
            throw new DomainException(
                "ADMIN_ASSURANCE_INSUFFICIENT",
                "A managed device and phishing-resistant MFA are required for an admin session.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(expiresAt, clock.UtcNow);

        return new AdminSession(
            Identifiers.NewId(),
            oidcSubject,
            assurance,
            managedDevice,
            expiresAt,
            clock.UtcNow);
    }

    public void RecordStepUp(AdminAssurance assurance, DateTimeOffset occurredAt)
    {
        EnsureActive(occurredAt);
        if (assurance is not (AdminAssurance.WebAuthn or AdminAssurance.HardwareKey))
        {
            throw new DomainException("STEP_UP_REQUIRED", "Phishing-resistant step-up authentication is required.");
        }

        Assurance = assurance;
        AuthenticatedAt = occurredAt.ToUniversalTime();
        RecordChange(occurredAt);
    }

    public void RequireFreshStepUp(DateTimeOffset now, TimeSpan maximumAge)
    {
        EnsureActive(now);
        if (now - AuthenticatedAt > maximumAge)
        {
            throw new DomainException("STEP_UP_REQUIRED", "The admin session assurance is too old for this action.");
        }
    }

    public void Revoke(DateTimeOffset occurredAt)
    {
        if (State == AdminSessionState.Active)
        {
            State = AdminSessionState.Revoked;
            RevokedAt = occurredAt.ToUniversalTime();
            RecordChange(occurredAt);
        }
    }

    private void EnsureActive(DateTimeOffset now)
    {
        if (State != AdminSessionState.Active)
        {
            throw new DomainException("ADMIN_SESSION_INACTIVE", "The admin session is not active.");
        }

        if (now >= ExpiresAt)
        {
            State = AdminSessionState.Expired;
            throw new DomainException("ADMIN_SESSION_EXPIRED", "The admin session expired.");
        }
    }
}

public sealed record StaffIdentity(
    Guid Id,
    string OidcSubject,
    bool IsActive,
    DateTimeOffset? OffboardedAt);
