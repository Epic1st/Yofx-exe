using System.Globalization;
using System.Text;
using YO4X.BuildingBlocks;

namespace YO4X.Identity;

public enum UserSecurityState
{
    Invited,
    Active,
    Locked,
    RecoveryRequired,
    Disabled
}

public enum AuthenticationAssurance
{
    Password,
    Totp,
    WebAuthn,
    HardwareKey
}

public sealed class UserIdentity : VersionedAggregate
{
    private UserIdentity(Guid id, Guid tenantId, string normalizedEmail, DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        TenantId = tenantId;
        NormalizedEmail = normalizedEmail;
        SecurityState = UserSecurityState.Invited;
    }

    public Guid TenantId { get; }

    public string NormalizedEmail { get; }

    public UserSecurityState SecurityState { get; private set; }

    public DateTimeOffset? EmailVerifiedAt { get; private set; }

    public DateTimeOffset? LockedAt { get; private set; }

    public static UserIdentity Invite(Guid tenantId, string email, IClock clock)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant identifier is required.", nameof(tenantId));
        }

        string normalizedEmail = NormalizeEmail(email);
        return new UserIdentity(Identifiers.NewId(), tenantId, normalizedEmail, clock.UtcNow);
    }

    public void VerifyEmail(DateTimeOffset occurredAt)
    {
        EnsureNotDisabled();
        if (EmailVerifiedAt is not null)
        {
            return;
        }

        EmailVerifiedAt = occurredAt.ToUniversalTime();
        SecurityState = UserSecurityState.Active;
        RecordChange(occurredAt);
    }

    public void Lock(DateTimeOffset occurredAt)
    {
        EnsureNotDisabled();
        SecurityState = UserSecurityState.Locked;
        LockedAt = occurredAt.ToUniversalTime();
        RecordChange(occurredAt);
    }

    public void CompleteVerifiedRecovery(DateTimeOffset occurredAt)
    {
        if (SecurityState is not (UserSecurityState.Locked or UserSecurityState.RecoveryRequired))
        {
            throw new DomainException("RECOVERY_NOT_REQUIRED", "The identity is not in a recoverable locked state.");
        }

        SecurityState = UserSecurityState.Active;
        LockedAt = null;
        RecordChange(occurredAt);
    }

    public void Disable(DateTimeOffset occurredAt)
    {
        SecurityState = UserSecurityState.Disabled;
        RecordChange(occurredAt);
    }

    public void RequireRecovery(DateTimeOffset occurredAt)
    {
        EnsureNotDisabled();
        SecurityState = UserSecurityState.RecoveryRequired;
        RecordChange(occurredAt);
    }

    public bool IsEligibleForBrokerOnboarding =>
        SecurityState == UserSecurityState.Active && EmailVerifiedAt is not null;

    private static string NormalizeEmail(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        string normalized = email.Trim().Normalize(NormalizationForm.FormKC).ToUpper(CultureInfo.InvariantCulture);
        if (normalized.Length > 320 || !normalized.Contains('@', StringComparison.Ordinal))
        {
            throw new ArgumentException("The email address is invalid.", nameof(email));
        }

        return normalized;
    }

    private void EnsureNotDisabled()
    {
        if (SecurityState == UserSecurityState.Disabled)
        {
            throw new DomainException("IDENTITY_DISABLED", "The identity is disabled.");
        }
    }
}

public interface IUserIdentityRepository
{
    Task<UserIdentity?> FindAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
}
