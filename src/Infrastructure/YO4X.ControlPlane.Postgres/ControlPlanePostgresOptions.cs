using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace YO4X.ControlPlane.Postgres;

public sealed partial class ControlPlanePostgresOptions
{
    public string? ApprovedGatewayDigest { get; init; }

    public string? ApprovedRegion { get; init; }

    public string? ApprovedBrokerServer { get; init; }

    public Guid? ApprovedBrokerProfileId { get; init; }

    public string? ApprovedRuntimeImageDigest { get; init; }

    public TimeSpan BrokerCapabilityMaximumAge { get; init; } = TimeSpan.FromMinutes(15);

    public TimeSpan CompatibilityEvidenceMaximumAge { get; init; } = TimeSpan.FromHours(24);

    public TimeSpan EvidenceFutureClockSkew { get; init; } = TimeSpan.FromSeconds(30);

    public Uri? SecretIngestionOrigin { get; init; }

    public Uri? ApprovedCredentialClientOrigin { get; init; }

    public TimeSpan IngestionGrantLifetime { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan StrategyImportJobLifetime { get; init; } = TimeSpan.FromMinutes(10);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApprovedGatewayDigest)
            || !Sha256Pattern().IsMatch(ApprovedGatewayDigest))
        {
            throw new InvalidOperationException("U0:ApprovedGatewayDigest must be one exact SHA-256 digest.");
        }

        if (string.IsNullOrWhiteSpace(ApprovedRegion))
        {
            throw new InvalidOperationException("U0:ApprovedRegion is required.");
        }

        if (string.IsNullOrWhiteSpace(ApprovedBrokerServer))
        {
            throw new InvalidOperationException("U0:ApprovedBrokerServer is required.");
        }

        if (ApprovedBrokerProfileId is null || ApprovedBrokerProfileId == Guid.Empty)
        {
            throw new InvalidOperationException("U0:ApprovedBrokerProfileId is required.");
        }

        if (string.IsNullOrWhiteSpace(ApprovedRuntimeImageDigest)
            || !RuntimeImageDigestPattern().IsMatch(ApprovedRuntimeImageDigest))
        {
            throw new InvalidOperationException(
                "RuntimePostgres:ApprovedRuntimeImageDigest must be one exact lowercase sha256 digest.");
        }

        if (BrokerCapabilityMaximumAge <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("U0:BrokerCapabilityMaximumAge must be positive.");
        }

        if (CompatibilityEvidenceMaximumAge <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("U0:CompatibilityEvidenceMaximumAge must be positive.");
        }

        if (EvidenceFutureClockSkew < TimeSpan.Zero || EvidenceFutureClockSkew > TimeSpan.FromMinutes(5))
        {
            throw new InvalidOperationException("U0:EvidenceFutureClockSkew must be between zero and five minutes.");
        }

        if (SecretIngestionOrigin is null
            || !SecretIngestionOrigin.IsAbsoluteUri
            || SecretIngestionOrigin.Scheme != Uri.UriSchemeHttps
            || SecretIngestionOrigin.PathAndQuery != "/"
            || !string.IsNullOrEmpty(SecretIngestionOrigin.UserInfo)
            || !string.IsNullOrEmpty(SecretIngestionOrigin.Fragment))
        {
            throw new InvalidOperationException("SecretIngestion:Origin must be an exact HTTPS origin.");
        }

        if (ApprovedCredentialClientOrigin is null
            || !ApprovedCredentialClientOrigin.IsAbsoluteUri
            || ApprovedCredentialClientOrigin.Scheme != Uri.UriSchemeHttps
            || ApprovedCredentialClientOrigin.PathAndQuery != "/"
            || !string.IsNullOrEmpty(ApprovedCredentialClientOrigin.UserInfo)
            || !string.IsNullOrEmpty(ApprovedCredentialClientOrigin.Fragment))
        {
            throw new InvalidOperationException("SecretIngestion:ApprovedClientOrigin must be an exact HTTPS origin.");
        }

        if (IngestionGrantLifetime <= TimeSpan.Zero
            || IngestionGrantLifetime > TimeSpan.FromMinutes(10))
        {
            throw new InvalidOperationException("SecretIngestion:GrantLifetime must be between zero and ten minutes.");
        }

        if (StrategyImportJobLifetime <= TimeSpan.Zero
            || StrategyImportJobLifetime > TimeSpan.FromMinutes(30))
        {
            throw new InvalidOperationException(
                "Conversion:ImportJobLifetime must be between zero and thirty minutes.");
        }
    }

    [GeneratedRegex("^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex("^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex RuntimeImageDigestPattern();
}

public sealed class CredentialProofKey : IDisposable
{
    private byte[]? key;
    private readonly ReaderWriterLockSlim lifecycleLock = new(LockRecursionPolicy.NoRecursion);

    public CredentialProofKey(byte[] keyBytes)
    {
        ArgumentNullException.ThrowIfNull(keyBytes);
        if (keyBytes.Length < 32)
        {
            throw new ArgumentOutOfRangeException(nameof(keyBytes), "The credential proof key must contain at least 256 bits.");
        }

        key = keyBytes.ToArray();
    }

    internal void ComputeSha256(ReadOnlySpan<byte> input, Span<byte> destination)
    {
        lifecycleLock.EnterReadLock();
        try
        {
            byte[] activeKey = key
                ?? throw new ObjectDisposedException(nameof(CredentialProofKey));
            HMACSHA256.HashData(activeKey, input, destination);
        }
        finally
        {
            lifecycleLock.ExitReadLock();
        }
    }

    public void Dispose()
    {
        lifecycleLock.EnterWriteLock();
        try
        {
            byte[]? owned = Interlocked.Exchange(ref key, null);
            if (owned is not null)
            {
                CryptographicOperations.ZeroMemory(owned);
            }
        }
        finally
        {
            lifecycleLock.ExitWriteLock();
        }

        GC.SuppressFinalize(this);
    }

    public override string ToString() => "[REDACTED CREDENTIAL PROOF KEY]";
}
