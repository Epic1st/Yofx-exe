using System.Security.Cryptography;
using System.Text;
using YO4X.BuildingBlocks;

namespace YO4X.ControlPlane.Postgres;

public sealed class IssuedStrategyImportProof
{
    internal IssuedStrategyImportProof(string capability)
    {
        Capability = capability;
    }

    public string Capability { get; }

    public override string ToString() =>
        "IssuedStrategyImportProof { Capability = [REDACTED] }";
}

public sealed class StrategyImportProofIssuer(StrategyImportProofKeyRing keyRing)
{
    public string CurrentKeyId => keyRing.CurrentKeyId;

    public IssuedStrategyImportProof Issue(
        Guid tenantId,
        Guid userId,
        Guid importJobId,
        Guid correlationId,
        string sourceLabel,
        DateTimeOffset expiresAt) =>
        Issue(
            tenantId,
            userId,
            importJobId,
            correlationId,
            sourceLabel,
            expiresAt,
            keyRing.CurrentKeyId);

    public IssuedStrategyImportProof Issue(
        Guid tenantId,
        Guid userId,
        Guid importJobId,
        Guid correlationId,
        string sourceLabel,
        DateTimeOffset expiresAt,
        string? proofKeyId)
    {
        RequireIdentifier(tenantId, nameof(tenantId));
        RequireIdentifier(userId, nameof(userId));
        RequireIdentifier(importJobId, nameof(importJobId));
        RequireIdentifier(correlationId, nameof(correlationId));
        ValidateSourceLabel(sourceLabel);

        string binding = FormattableString.Invariant(
            $"strategy-import:v1:{tenantId:D}:{userId:D}:{importJobId:D}:{correlationId:D}:{sourceLabel}:{expiresAt.UtcTicks}");
        byte[] input = Encoding.UTF8.GetBytes(binding);
        try
        {
            Span<byte> digest = stackalloc byte[32];
            if (!keyRing.TryComputeSha256(proofKeyId, input, digest))
            {
                throw new BackendCapabilityUnavailableException(
                    "strategy-import-proof-key-unavailable");
            }

            return new IssuedStrategyImportProof(ToBase64Url(digest));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    public static byte[] HashCapability(string capability)
    {
        byte[] bytes = DecodeCapability(capability);
        try
        {
            return SHA256.HashData(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static byte[] DecodeCapability(string capability)
    {
        if (capability is not { Length: 43 }
            || capability.Any(character => character is not (>= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '-' or '_')))
        {
            throw new ArgumentException("The strategy import capability format is invalid.", nameof(capability));
        }

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(
                capability.Replace('-', '+').Replace('_', '/') + "=");
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(
                "The strategy import capability format is invalid.",
                nameof(capability),
                exception);
        }

        if (decoded.Length != 32
            || decoded.All(static value => value == 0)
            || !string.Equals(ToBase64Url(decoded), capability, StringComparison.Ordinal))
        {
            CryptographicOperations.ZeroMemory(decoded);
            throw new ArgumentException("The strategy import capability format is invalid.", nameof(capability));
        }

        return decoded;
    }

    private static void ValidateSourceLabel(string sourceLabel)
    {
        if (sourceLabel is not { Length: >= 1 and <= 100 }
            || sourceLabel.Any(character => character is not (>= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '-' or '_' or '.')))
        {
            throw new ArgumentException("The source label format is invalid.", nameof(sourceLabel));
        }
    }

    private static string ToBase64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void RequireIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An identifier is required.", parameterName);
        }
    }
}
