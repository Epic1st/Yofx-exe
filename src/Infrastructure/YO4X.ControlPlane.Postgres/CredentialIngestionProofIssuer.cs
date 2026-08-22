using System.Security.Cryptography;
using System.Text;
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

public sealed class CredentialIngestionProofIssuer(CredentialProofKey key)
{
    public IssuedCredentialIngestionProof Issue(
        Guid tenantId,
        Guid userId,
        Guid brokerAccountId,
        CredentialIngestionOperation operation,
        string idempotencyKey)
    {
        RequireIdentifier(tenantId, nameof(tenantId));
        RequireIdentifier(userId, nameof(userId));
        RequireIdentifier(brokerAccountId, nameof(brokerAccountId));
        if (!Enum.IsDefined(operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown credential-ingestion operation.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        string binding = FormattableString.Invariant(
            $"v1:{tenantId:D}:{userId:D}:{brokerAccountId:D}:{operation}:{idempotencyKey}");
        return new IssuedCredentialIngestionProof(
            Derive(binding, "bearer"),
            Derive(binding, "nonce"));
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

    private string Derive(string binding, string purpose)
    {
        byte[] input = Encoding.UTF8.GetBytes($"{binding}:{purpose}");
        try
        {
            Span<byte> digest = stackalloc byte[32];
            key.ComputeSha256(input, digest);
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
}
