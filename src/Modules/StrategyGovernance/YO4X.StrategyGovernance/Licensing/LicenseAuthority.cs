using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace YO4X.StrategyGovernance.Licensing;

public static class LicenseAuthority
{
    private const int MaximumBindings = 128;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static (string PrivateKeyPem, string PublicKeyPem) GenerateMasterKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string privateKey = ecdsa.ExportECPrivateKeyPem();
        string publicKey = ecdsa.ExportSubjectPublicKeyInfoPem();
        return (privateKey, publicKey);
    }

    public static StrategyLicenseToken IssueLicenseToken(
        StrategyLicenseClaims claims,
        string privateKeyPem)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);

        ValidateClaimsShape(claims);
        byte[] claimsBytes = JsonSerializer.SerializeToUtf8Bytes(claims, JsonOptions);

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKeyPem);

        byte[] signature = ecdsa.SignData(
            claimsBytes,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        string sigBase64 = Convert.ToBase64String(signature);

        return new StrategyLicenseToken(claims, sigBase64);
    }

    public static StrategyLicenseClaims ValidateLicense(
        StrategyLicenseToken token,
        ulong currentBrokerLogin,
        string currentBrokerServer,
        string publicKeyPem,
        DateTimeOffset? validationTimeUtc = null)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(token.Claims);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPem);

        DateTimeOffset now = (validationTimeUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        StrategyLicenseClaims claims = VerifySignatureAndShape(token, publicKeyPem);

        ValidateTimeAndBrokerBindings(claims, currentBrokerLogin, currentBrokerServer, now);
        return claims;
    }

    public static StrategyLicenseClaims ValidateLicense(
        StrategyLicenseToken token,
        StrategyLicenseValidationContext context,
        string publicKeyPem)
    {
        ArgumentNullException.ThrowIfNull(context);
        StrategyLicenseClaims claims = VerifySignatureAndShape(token, publicKeyPem);
        DateTimeOffset now = context.ValidationTimeUtc.ToUniversalTime();

        if (claims.TenantId != context.TenantId
            || claims.UserId != context.UserId
            || !string.Equals(claims.StrategyId, context.StrategyId, StringComparison.Ordinal)
            || !string.Equals(claims.StrategyVersion, context.StrategyVersion, StringComparison.Ordinal)
            || !string.Equals(claims.AssemblySha256, context.AssemblySha256, StringComparison.Ordinal))
        {
            throw new LicenseValidationException(
                LicenseStatus.Invalid,
                "The signed license does not match the authoritative execution binding.");
        }

        ValidateTimeAndBrokerBindings(
            claims,
            context.BrokerLogin,
            context.BrokerServer,
            now);
        return claims;
    }

    private static StrategyLicenseClaims VerifySignatureAndShape(
        StrategyLicenseToken token,
        string publicKeyPem)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(token.Claims);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPem);
        ValidateClaimsShape(token.Claims);

        byte[] claimsBytes = JsonSerializer.SerializeToUtf8Bytes(token.Claims, JsonOptions);
        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromBase64String(token.SignatureBase64);
        }
        catch (FormatException exception)
        {
            throw new LicenseValidationException(
                LicenseStatus.Invalid,
                $"The license signature encoding is invalid ({exception.GetType().Name}).");
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(publicKeyPem);
            DSASignatureFormat signatureFormat = signatureBytes.Length == 64
                ? DSASignatureFormat.IeeeP1363FixedFieldConcatenation
                : DSASignatureFormat.Rfc3279DerSequence;
            if (!ecdsa.VerifyData(
                    claimsBytes,
                    signatureBytes,
                    HashAlgorithmName.SHA256,
                    signatureFormat))
            {
                throw new LicenseValidationException(
                    LicenseStatus.Invalid,
                    "Cryptographic signature verification failed.");
            }
        }
        catch (CryptographicException exception)
        {
            throw new LicenseValidationException(
                LicenseStatus.Invalid,
                $"The license verification key or signature is invalid ({exception.GetType().Name}).");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(claimsBytes);
            CryptographicOperations.ZeroMemory(signatureBytes);
        }

        return token.Claims;
    }

    private static void ValidateTimeAndBrokerBindings(
        StrategyLicenseClaims claims,
        ulong currentBrokerLogin,
        string currentBrokerServer,
        DateTimeOffset now)
    {
        if (claims.IssuedAtUtc.ToUniversalTime() > now
            || claims.NotBeforeUtc?.ToUniversalTime() > now)
        {
            throw new LicenseValidationException(
                LicenseStatus.Invalid,
                "The license is not valid yet.");
        }

        if (claims.ExpiresAtUtc.HasValue && now >= claims.ExpiresAtUtc.Value.ToUniversalTime())
        {
            throw new LicenseValidationException(
                LicenseStatus.Expired,
                $"License expired on {claims.ExpiresAtUtc.Value:yyyy-MM-dd HH:mm:ss} UTC (Current time: {now:yyyy-MM-dd HH:mm:ss} UTC).");
        }

        if (claims.BoundAccounts.Count == 0
            || !claims.BoundAccounts.Contains(currentBrokerLogin))
        {
            throw new LicenseValidationException(
                LicenseStatus.AccountMismatch,
                $"License is strictly bound to account(s) [{string.Join(", ", claims.BoundAccounts)}]. Active broker account '{currentBrokerLogin}' is not authorized.");
        }

        string normalizedServer = NormalizeServer(currentBrokerServer);
        if (normalizedServer.Length == 0
            || claims.BoundServers.Count == 0
            || !claims.BoundServers.Any(server =>
                string.Equals(NormalizeServer(server), normalizedServer, StringComparison.Ordinal)))
        {
            throw new LicenseValidationException(
                LicenseStatus.AccountMismatch,
                $"License is bound to server(s) [{string.Join(", ", claims.BoundServers)}]. Active broker server '{currentBrokerServer}' is not authorized.");
        }
    }

    private static void ValidateClaimsShape(StrategyLicenseClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        if (claims.LicenseId == Guid.Empty
            || claims.TenantId == Guid.Empty
            || claims.UserId == Guid.Empty
            || string.IsNullOrWhiteSpace(claims.StrategyId)
            || claims.StrategyId.Length > 200
            || string.IsNullOrWhiteSpace(claims.StrategyName)
            || claims.StrategyName.Length > 300
            || !Enum.IsDefined(claims.LicenseType)
            || claims.BoundAccounts is null
            || claims.BoundAccounts.Count is < 1 or > MaximumBindings
            || claims.BoundAccounts.Any(login => login == 0)
            || claims.BoundServers is null
            || claims.BoundServers.Count is < 1 or > MaximumBindings
            || claims.BoundServers.Any(server => NormalizeServer(server).Length == 0)
            || claims.IssuedAtUtc.Offset != TimeSpan.Zero
            || claims.NotBeforeUtc is { } notBefore && notBefore.Offset != TimeSpan.Zero
            || claims.ExpiresAtUtc is { } expires && expires.Offset != TimeSpan.Zero
            || claims.ExpiresAtUtc <= claims.NotBeforeUtc
            || claims.ExpiresAtUtc <= claims.IssuedAtUtc
            || claims.LicenseType is LicenseType.Subscription or LicenseType.Trial
                && claims.ExpiresAtUtc is null
            || claims.MaxConcurrentBots is < 1 or > 10_000)
        {
            throw new LicenseValidationException(
                LicenseStatus.Invalid,
                "The signed license claims are malformed or incomplete.");
        }

        if (claims.StrategyVersion is not null
            && (string.IsNullOrWhiteSpace(claims.StrategyVersion)
                || claims.StrategyVersion.Length > 100))
        {
            throw new LicenseValidationException(LicenseStatus.Invalid, "The strategy version claim is invalid.");
        }

        if (claims.AssemblySha256 is not null && !IsLowerSha256(claims.AssemblySha256))
        {
            throw new LicenseValidationException(LicenseStatus.Invalid, "The assembly digest claim is invalid.");
        }

        if (claims.SigningKeyId is not null
            && (claims.SigningKeyId.Length is < 1 or > 128
                || claims.SigningKeyId.Any(character => character is not (>= 'A' and <= 'Z')
                    and not (>= 'a' and <= 'z')
                    and not (>= '0' and <= '9')
                    and not '.' and not '_' and not ':' and not '/' and not '-')))
        {
            throw new LicenseValidationException(LicenseStatus.Invalid, "The signing key identifier is invalid.");
        }
    }

    private static string NormalizeServer(string? value) =>
        value?.Trim().ToUpperInvariant() ?? string.Empty;

    private static bool IsLowerSha256(string value) => value.Length == 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
