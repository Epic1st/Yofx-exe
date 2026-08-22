using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace YO4X.Api;

public sealed class ClientCertificateFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        System.Security.Cryptography.X509Certificates.X509Certificate2? certificate =
            await context.HttpContext.Connection.GetClientCertificateAsync().ConfigureAwait(false);

        string? confirmation = context.HttpContext.User.FindFirstValue("certificate_sha256");
        if (certificate is null
            || DateTimeOffset.UtcNow < certificate.NotBefore
            || DateTimeOffset.UtcNow >= certificate.NotAfter
            || !MatchesCertificate(certificate.RawData, confirmation))
        {
            return ApiProblems.Create(
                context.HttpContext,
                StatusCodes.Status401Unauthorized,
                "WORKLOAD_CERTIFICATE_REQUIRED",
                "A valid workload client certificate is required.");
        }

        return await next(context).ConfigureAwait(false);
    }

    private static bool MatchesCertificate(byte[] rawCertificate, string? confirmation)
    {
        if (confirmation is not { Length: 64 }
            || confirmation.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            return false;
        }

        byte[] claimedDigest = Convert.FromHexString(confirmation);
        byte[] actualDigest = SHA256.HashData(rawCertificate);
        try
        {
            return CryptographicOperations.FixedTimeEquals(claimedDigest, actualDigest);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(claimedDigest);
            CryptographicOperations.ZeroMemory(actualDigest);
        }
    }
}
