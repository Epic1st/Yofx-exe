using System.Security.Cryptography;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.Runtime.Contracts;

namespace YO4X.RuntimeControl.Postgres;

internal static class ExecutionLeaseEnvelopeFactory
{
    public static async ValueTask<SignedExecutionLease> CreateAsync(
        ExecutionLeaseClaims claims,
        IExecutionLeaseSigningProvider signingProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(signingProvider);

        byte[] canonicalPayload = ExecutionLeaseCanonicalizer.Serialize(claims);
        try
        {
            string payloadSha256 = Convert.ToHexString(SHA256.HashData(canonicalPayload)).ToLowerInvariant();
            ExecutionLeaseSignature? signature = await signingProvider
                .SignAsync(canonicalPayload, cancellationToken)
                .ConfigureAwait(false);
            if (signature is null
                || !IsAsymmetricAlgorithm(signature.Algorithm)
                || signature.Algorithm.Length > 100
                || string.IsNullOrWhiteSpace(signature.KeyId)
                || signature.KeyId.Length > 500
                || signature.SignatureBase64Url is not { Length: >= 43 and <= 2048 }
                || signature.SignatureBase64Url.Any(character => character is not (>= 'A' and <= 'Z')
                    and not (>= 'a' and <= 'z')
                    and not (>= '0' and <= '9')
                    and not '-'
                    and not '_'))
            {
                throw new BackendCapabilityUnavailableException("execution_lease_signing_provider");
            }

            return new SignedExecutionLease(
                claims,
                payloadSha256,
                signature.Algorithm.Trim(),
                signature.KeyId.Trim(),
                signature.SignatureBase64Url);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(canonicalPayload);
        }
    }

    private static bool IsAsymmetricAlgorithm(string? algorithm) => algorithm is
        "ECDSA_P256_SHA256_DER" or
        "EdDSA" or "ES256" or "ES384" or "ES512" or "PS256" or "PS384" or "PS512";
}
