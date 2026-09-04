using System.Text.Json.Serialization;

namespace YO4X.StrategyGovernance.Licensing;

public enum LicenseType
{
    Developer = 0,
    Lifetime = 1,
    Subscription = 2,
    Trial = 3
}

public enum LicenseStatus
{
    Active = 0,
    Expired = 1,
    Revoked = 2,
    AccountMismatch = 3,
    Invalid = 4
}

public sealed record StrategyLicenseClaims(
    [property: JsonPropertyName("licenseId")] Guid LicenseId,
    [property: JsonPropertyName("tenantId")] Guid TenantId,
    [property: JsonPropertyName("userId")] Guid UserId,
    [property: JsonPropertyName("strategyId")] string StrategyId,
    [property: JsonPropertyName("strategyName")] string StrategyName,
    [property: JsonPropertyName("licenseType")] LicenseType LicenseType,
    [property: JsonPropertyName("boundAccounts")] IReadOnlyList<ulong> BoundAccounts,
    [property: JsonPropertyName("boundServers")] IReadOnlyList<string> BoundServers,
    [property: JsonPropertyName("issuedAtUtc")] DateTimeOffset IssuedAtUtc,
    [property: JsonPropertyName("expiresAtUtc")] DateTimeOffset? ExpiresAtUtc,
    [property: JsonPropertyName("maxConcurrentBots")] int MaxConcurrentBots,
    [property: JsonPropertyName("notBeforeUtc")] DateTimeOffset? NotBeforeUtc = null,
    [property: JsonPropertyName("strategyVersion")] string? StrategyVersion = null,
    [property: JsonPropertyName("assemblySha256")] string? AssemblySha256 = null,
    [property: JsonPropertyName("signingKeyId")] string? SigningKeyId = null);

public sealed record StrategyLicenseToken(
    [property: JsonPropertyName("claims")] StrategyLicenseClaims Claims,
    [property: JsonPropertyName("signature")] string SignatureBase64);

/// <summary>
/// Authoritative facts supplied by the execution boundary. Signed claims are accepted only
/// when they match these values exactly; a package cannot choose its own runtime identity.
/// </summary>
public sealed record StrategyLicenseValidationContext(
    Guid TenantId,
    Guid UserId,
    string StrategyId,
    string StrategyVersion,
    string AssemblySha256,
    ulong BrokerLogin,
    string BrokerServer,
    DateTimeOffset ValidationTimeUtc);

public sealed class LicenseValidationException : Exception
{
    public LicenseStatus Status { get; }

    public LicenseValidationException(LicenseStatus status, string message) : base(message)
    {
        Status = status;
    }
}
