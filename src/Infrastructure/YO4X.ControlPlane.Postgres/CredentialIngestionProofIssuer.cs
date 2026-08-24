using System.Security.Cryptography;
using System.Text;
using YO4X.BuildingBlocks;
using YO4X.SecretCoordination;

namespace YO4X.ControlPlane.Postgres;

public sealed class IssuedCredentialIngestionProof
{
    internal IssuedCredentialIngestionProof(string bearer, string nonce)
    {
        Bearer = bearer;
        Nonce = nonce;
    }

    public string Bearer { get; }

    public string Nonce { get; }

    public override string ToString() => "IssuedCredentialIngestionProof { Proof = [REDACTED] }";
}

public sealed class CredentialIngestionProofIssuer(CredentialProofKeyRing keyRing)
{
    public string CurrentKeyId => keyRing.CurrentKeyId;

    public IssuedCredentialIngestionProof Issue(
        Guid tenantId,
        Guid userId,
        Guid brokerAccountId,
        Guid grantId,
        CredentialIngestionOperation operation,
        string allowedOrigin,
        string idempotencyKey) =>
        Issue(
            tenantId,
            userId,
            brokerAccountId,
            grantId,
            operation,
            allowedOrigin,
            idempotencyKey,
            keyRing.CurrentKeyId);

    public IssuedCredentialIngestionProof Issue(
        Guid tenantId,
        Guid userId,
        Guid brokerAccountId,
        Guid grantId,
        CredentialIngestionOperation operation,
        string allowedOrigin,
        string idempotencyKey,
        string? proofKeyId)
    {
        RequireIdentifier(tenantId, nameof(tenantId));
        RequireIdentifier(userId, nameof(userId));
        RequireIdentifier(brokerAccountId, nameof(brokerAccountId));
        RequireIdentifier(grantId, nameof(grantId));
        if (!Enum.IsDefined(operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown credential-ingestion operation.");
        }

        ValidateOrigin(allowedOrigin);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        string binding = FormattableString.Invariant(
            $"credential-ingestion:v2:{tenantId:D}:{userId:D}:{brokerAccountId:D}:{grantId:D}:{operation}:{allowedOrigin.Length}:{allowedOrigin}:{idempotencyKey.Length}:{idempotencyKey}");
        return new IssuedCredentialIngestionProof(
            Derive(binding, "bearer", proofKeyId),
            Derive(binding, "nonce", proofKeyId));
    }

    public static string HashProof(string proof)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proof);
        byte[] bytes = Encoding.UTF8.GetBytes(proof);
        try
        {
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private string Derive(string binding, string purpose, string? proofKeyId)
    {
        byte[] input = Encoding.UTF8.GetBytes($"{binding}:{purpose}");
        try
        {
            Span<byte> digest = stackalloc byte[32];
            if (!keyRing.TryComputeSha256(proofKeyId, input, digest))
            {
                throw new BackendCapabilityUnavailableException(
                    "credential-ingestion-proof-key-unavailable");
            }

            return ToBase64Url(digest);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
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

    private static void ValidateOrigin(string allowedOrigin)
    {
        if (!Uri.TryCreate(allowedOrigin, UriKind.Absolute, out Uri? origin)
            || origin.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(origin.Host)
            || !string.IsNullOrEmpty(origin.UserInfo)
            || origin.AbsolutePath != "/"
            || !string.IsNullOrEmpty(origin.Query)
            || !string.IsNullOrEmpty(origin.Fragment)
            || !string.Equals(
                allowedOrigin,
                origin.GetLeftPart(UriPartial.Authority),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The credential-ingestion origin must be one canonical HTTPS authority.",
                nameof(allowedOrigin));
        }
    }
}
