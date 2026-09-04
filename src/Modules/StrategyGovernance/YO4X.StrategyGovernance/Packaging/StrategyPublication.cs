using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YO4X.StrategyGovernance.Packaging;

/// <summary>Publisher-signed identity of one immutable, marketplace-safe assembly.</summary>
public sealed record StrategyPublicationClaims(
    [property: JsonPropertyName("publicationId")] Guid PublicationId,
    [property: JsonPropertyName("strategyId")] string StrategyId,
    [property: JsonPropertyName("strategyName")] string StrategyName,
    [property: JsonPropertyName("strategyVersion")] string StrategyVersion,
    [property: JsonPropertyName("assemblySha256")] string AssemblySha256,
    [property: JsonPropertyName("issuedAtUtc")] DateTimeOffset IssuedAtUtc,
    [property: JsonPropertyName("signingKeyId")] string SigningKeyId);

public sealed record StrategyPublicationToken(
    [property: JsonPropertyName("claims")] StrategyPublicationClaims Claims,
    [property: JsonPropertyName("signature")] string SignatureBase64);

/// <summary>Signs and verifies common marketplace artifacts independently of user licenses.</summary>
public static class StrategyPublicationAuthority
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static StrategyPublicationToken Issue(
        StrategyPublicationClaims claims,
        string privateKeyPem)
    {
        ValidateShape(claims);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(claims, JsonOptions);
        try
        {
            using ECDsa signer = ECDsa.Create();
            signer.ImportFromPem(privateKeyPem);
            byte[] signature = signer.SignData(
                payload,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence);
            try
            {
                return new StrategyPublicationToken(claims, Convert.ToBase64String(signature));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(signature);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public static StrategyPublicationClaims Validate(
        StrategyPublicationToken token,
        string publicKeyPem)
    {
        ArgumentNullException.ThrowIfNull(token);
        ValidateShape(token.Claims);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPem);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(token.Claims, JsonOptions);
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(token.SignatureBase64);
        }
        catch (FormatException exception)
        {
            throw new CryptographicException("The publication signature encoding is invalid.", exception);
        }

        try
        {
            using ECDsa verifier = ECDsa.Create();
            verifier.ImportFromPem(publicKeyPem);
            if (!verifier.VerifyData(
                    payload,
                    signature,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.Rfc3279DerSequence))
            {
                throw new CryptographicException("The strategy publication signature is invalid.");
            }
            return token.Claims;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
            CryptographicOperations.ZeroMemory(signature);
        }
    }

    private static void ValidateShape(StrategyPublicationClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        if (claims.PublicationId == Guid.Empty
            || string.IsNullOrWhiteSpace(claims.StrategyId) || claims.StrategyId.Length > 200
            || string.IsNullOrWhiteSpace(claims.StrategyName) || claims.StrategyName.Length > 300
            || string.IsNullOrWhiteSpace(claims.StrategyVersion) || claims.StrategyVersion.Length > 100
            || claims.AssemblySha256.Length != 64
            || claims.AssemblySha256.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
            || claims.IssuedAtUtc.Offset != TimeSpan.Zero
            || string.IsNullOrWhiteSpace(claims.SigningKeyId) || claims.SigningKeyId.Length > 128)
        {
            throw new InvalidDataException("The strategy publication claims are malformed.");
        }
    }
}
