using System.Security.Cryptography;
using System.Text;
using Microsoft.Net.Http.Headers;
using YO4X.Api;
using YO4X.SecretCoordination;

namespace YO4X.SecretIngestion.Api;

internal static class IngestionProofReader
{
    private const int MinimumProofLength = 32;
    private const int MaximumProofLength = 512;

    public static bool TryRead(
        HttpRequest request,
        Guid tenantId,
        Guid grantId,
        out CredentialIngestionProof? proof)
    {
        proof = null;
        string authorization = request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string bearer = authorization["Bearer ".Length..];
        string nonce = request.Headers[ApiHeaders.IngestionNonce].ToString();
        string origin = request.Headers[HeaderNames.Origin].ToString();
        if (!IsBounded(bearer) || !IsBounded(nonce) || !IsHttpsOrigin(origin))
        {
            return false;
        }

        proof = new CredentialIngestionProof(
            tenantId,
            grantId,
            origin,
            HashAndClear(bearer),
            HashAndClear(nonce));
        return true;
    }

    private static bool IsBounded(string value) =>
        value.Length is >= MinimumProofLength and <= MaximumProofLength;

    private static bool IsHttpsOrigin(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? origin)
        && origin.Scheme == Uri.UriSchemeHttps
        && origin.PathAndQuery == "/"
        && string.Equals(origin.GetLeftPart(UriPartial.Authority), value, StringComparison.Ordinal);

    private static string HashAndClear(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
