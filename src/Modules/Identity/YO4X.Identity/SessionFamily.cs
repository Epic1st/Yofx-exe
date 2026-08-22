using System.Security.Cryptography;
using YO4X.BuildingBlocks;

namespace YO4X.Identity;

public enum SessionState
{
    Active,
    Revoked,
    Expired,
    Compromised
}

public sealed record RefreshRotationResult(bool Accepted, bool FamilyCompromised, long Generation);

public sealed class SessionFamily : VersionedAggregate
{
    private readonly HashSet<string> _invalidatedTokenHashes = new(StringComparer.Ordinal);

    private SessionFamily(
        Guid id,
        Guid tenantId,
        Guid userId,
        Guid deviceId,
        string currentTokenHash,
        DateTimeOffset expiresAt,
        DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        TenantId = tenantId;
        UserId = userId;
        DeviceId = deviceId;
        CurrentTokenHash = currentTokenHash;
        ExpiresAt = expiresAt.ToUniversalTime();
        State = SessionState.Active;
    }

    public Guid TenantId { get; }

    public Guid UserId { get; }

    public Guid DeviceId { get; }

    public string CurrentTokenHash { get; private set; }

    public long Generation { get; private set; }

    public DateTimeOffset ExpiresAt { get; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public SessionState State { get; private set; }

    public static SessionFamily Issue(
        Guid tenantId,
        Guid userId,
        Guid deviceId,
        string initialTokenHash,
        DateTimeOffset expiresAt,
        IClock clock)
    {
        ValidateHash(initialTokenHash);
        if (tenantId == Guid.Empty || userId == Guid.Empty || deviceId == Guid.Empty)
        {
            throw new ArgumentException("Tenant, user, and device identifiers are required.");
        }

        if (expiresAt <= clock.UtcNow)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt), "Session expiry must be in the future.");
        }

        return new SessionFamily(
            Identifiers.NewId(),
            tenantId,
            userId,
            deviceId,
            initialTokenHash,
            expiresAt,
            clock.UtcNow);
    }

    public RefreshRotationResult Rotate(
        string presentedTokenHash,
        string replacementTokenHash,
        DateTimeOffset occurredAt)
    {
        ValidateHash(presentedTokenHash);
        ValidateHash(replacementTokenHash);

        if (State != SessionState.Active || occurredAt >= ExpiresAt)
        {
            State = occurredAt >= ExpiresAt ? SessionState.Expired : State;
            return new RefreshRotationResult(false, State == SessionState.Compromised, Generation);
        }

        if (_invalidatedTokenHashes.Contains(presentedTokenHash))
        {
            State = SessionState.Compromised;
            RevokedAt = occurredAt.ToUniversalTime();
            RecordChange(occurredAt);
            return new RefreshRotationResult(false, true, Generation);
        }

        if (!FixedTimeEquals(CurrentTokenHash, presentedTokenHash))
        {
            return new RefreshRotationResult(false, false, Generation);
        }

        _invalidatedTokenHashes.Add(CurrentTokenHash);
        CurrentTokenHash = replacementTokenHash;
        Generation = checked(Generation + 1);
        RecordChange(occurredAt);
        return new RefreshRotationResult(true, false, Generation);
    }

    public void Revoke(DateTimeOffset occurredAt)
    {
        if (State == SessionState.Active)
        {
            State = SessionState.Revoked;
            RevokedAt = occurredAt.ToUniversalTime();
            RecordChange(occurredAt);
        }
    }

    private static void ValidateHash(string tokenHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        if (tokenHash.Length < 43 || tokenHash.Length > 128)
        {
            throw new ArgumentException("A keyed token hash is required.", nameof(tokenHash));
        }
    }

    private static bool FixedTimeEquals(string first, string second)
    {
        byte[] firstBytes = System.Text.Encoding.UTF8.GetBytes(first);
        byte[] secondBytes = System.Text.Encoding.UTF8.GetBytes(second);
        return CryptographicOperations.FixedTimeEquals(firstBytes, secondBytes);
    }
}

public interface ISessionFamilyRepository
{
    Task<SessionFamily?> FindForUpdateAsync(Guid tenantId, Guid sessionId, CancellationToken cancellationToken);

    Task SaveAsync(SessionFamily session, long expectedVersion, CancellationToken cancellationToken);
}
