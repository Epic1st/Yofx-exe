using System.Security.Cryptography;
using System.Text;
using YO4X.BuildingBlocks;

namespace YO4X.Risk;

public sealed record RiskDayDefinition(
    string? TimeZoneId,
    string? TimeZoneRulesVersion,
    TimeOnly? Boundary);

public sealed record RiskFreshnessLimits(
    long? QuoteMaxAgeMilliseconds,
    long? AccountMaxAgeMilliseconds,
    long? PositionMaxAgeMilliseconds,
    long? OrderMaxAgeMilliseconds,
    long? SymbolMaxAgeMilliseconds,
    long? ConversionRateMaxAgeMilliseconds);

public sealed record NumericRiskPolicyContent(
    RiskDayDefinition? RiskDay,
    decimal? MaxPerOrderVolume,
    decimal? MaxAccountPositionVolume,
    decimal? MaxAccountGrossNotional,
    int? MaxOpenPositions,
    int? MaxOpenOrders,
    int? MaxOrdersPerWindow,
    long? OrderRateWindowMilliseconds,
    decimal? MaxDailyLoss,
    decimal? MaxDrawdown,
    decimal? MaxSpreadPoints,
    decimal? MaxSlippagePoints,
    decimal? MaxStopLossDistancePoints,
    decimal? MinTakeProfitDistancePoints,
    RiskFreshnessLimits? IncreaseFreshness,
    RiskFreshnessLimits? ReduceProtectFreshness,
    bool? DemoOnly,
    bool? HedgingOnly,
    bool? RequireBrokerHostedStopLoss,
    bool? RequireBrokerHostedTakeProfit,
    bool? BlockExposureIncreaseOnExternalActivity);

public sealed record NumericRiskPolicyDescriptor(
    Guid PolicyId,
    long Version,
    string? Scope,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset ExpiresAtUtc,
    NumericRiskPolicyContent? Content);

public sealed class RiskPolicySignature
{
    private readonly byte[] signature;

    public RiskPolicySignature(
        string? algorithm,
        string? signingKeyId,
        byte[]? signature,
        string? signatureSha256,
        string? signedPayloadSha256)
    {
        Algorithm = algorithm;
        SigningKeyId = signingKeyId;
        this.signature = signature?.ToArray() ?? [];
        SignatureSha256 = signatureSha256;
        SignedPayloadSha256 = signedPayloadSha256;
    }

    public string? Algorithm { get; }

    public string? SigningKeyId { get; }

    public string? SignatureSha256 { get; }

    public string? SignedPayloadSha256 { get; }

    public byte[] GetSignature() => signature.ToArray();
}

public sealed record SignedNumericRiskPolicy(
    NumericRiskPolicyDescriptor? Descriptor,
    RiskPolicySignature? Signature);

public interface IRiskPolicySignatureVerifier
{
    bool Verify(
        string signingKeyId,
        string algorithm,
        ReadOnlySpan<byte> signature,
        string canonicalPayload);
}

/// <summary>
/// A cryptographic verifier restricted to P-256 ECDSA/SHA-256 DER signatures.
/// Private policy-signing material never enters this component.
/// </summary>
public sealed class EcdsaP256RiskPolicySignatureVerifier : IRiskPolicySignatureVerifier, IDisposable
{
    public const string Algorithm = "ECDSA_P256_SHA256_DER";

    private readonly Dictionary<string, ECDsa> keys;
    private int disposed;

    public EcdsaP256RiskPolicySignatureVerifier(IReadOnlyDictionary<string, byte[]> subjectPublicKeys)
    {
        ArgumentNullException.ThrowIfNull(subjectPublicKeys);
        if (subjectPublicKeys.Count is < 1 or > 32)
        {
            throw new ArgumentOutOfRangeException(
                nameof(subjectPublicKeys),
                "Between one and 32 trusted risk-policy keys are required.");
        }

        keys = new Dictionary<string, ECDsa>(StringComparer.Ordinal);
        try
        {
            foreach ((string keyId, byte[] encodedKey) in subjectPublicKeys)
            {
                if (!RiskPolicyValidation.IsValidKeyId(keyId)
                    || encodedKey is null
                    || encodedKey.Length is < 64 or > 1024)
                {
                    throw new ArgumentException("A risk-policy trust key is invalid.", nameof(subjectPublicKeys));
                }

                var key = ECDsa.Create();
                try
                {
                    key.ImportSubjectPublicKeyInfo(encodedKey, out int bytesRead);
                    ECParameters parameters = key.ExportParameters(false);
                    if (bytesRead != encodedKey.Length
                        || key.KeySize != 256
                        || !string.Equals(
                            parameters.Curve.Oid.Value,
                            "1.2.840.10045.3.1.7",
                            StringComparison.Ordinal))
                    {
                        throw new CryptographicException("Only exact P-256 public keys are accepted.");
                    }

                    keys.Add(keyId, key);
                    key = null!;
                }
                finally
                {
                    key?.Dispose();
                }
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public bool Verify(
        string signingKeyId,
        string algorithm,
        ReadOnlySpan<byte> signature,
        string canonicalPayload)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (!string.Equals(algorithm, Algorithm, StringComparison.Ordinal)
            || !RiskPolicyValidation.IsValidKeyId(signingKeyId)
            || signature.Length is < 64 or > 256
            || canonicalPayload is null
            || !keys.TryGetValue(signingKeyId, out ECDsa? key))
        {
            return false;
        }

        byte[] payload = Encoding.UTF8.GetBytes(canonicalPayload);
        try
        {
            return key.VerifyData(
                payload,
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (CryptographicException)
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        foreach (ECDsa key in keys.Values)
        {
            key.Dispose();
        }

        keys.Clear();
        GC.SuppressFinalize(this);
    }
}

public sealed class VerifiedNumericRiskPolicy
{
    private VerifiedNumericRiskPolicy(
        NumericRiskPolicyDescriptor descriptor,
        string payloadDigest,
        string signatureDigest,
        string signingKeyId)
    {
        Descriptor = descriptor;
        PayloadDigest = payloadDigest;
        SignatureDigest = signatureDigest;
        SigningKeyId = signingKeyId;
    }

    public NumericRiskPolicyDescriptor Descriptor { get; }

    public string PayloadDigest { get; }

    public string SignatureDigest { get; }

    public string SigningKeyId { get; }

    public static VerifiedNumericRiskPolicy Verify(
        SignedNumericRiskPolicy signedPolicy,
        IRiskPolicySignatureVerifier verifier)
    {
        ArgumentNullException.ThrowIfNull(signedPolicy);
        ArgumentNullException.ThrowIfNull(verifier);

        NumericRiskPolicyDescriptor descriptor = signedPolicy.Descriptor
            ?? throw RiskPolicyValidation.Invalid("RISK_POLICY_DESCRIPTOR_MISSING");
        RiskPolicySignature envelope = signedPolicy.Signature
            ?? throw RiskPolicyValidation.Invalid("RISK_POLICY_SIGNATURE_MISSING");

        RiskPolicyValidation.ValidateDescriptor(descriptor);
        if (!string.Equals(
                envelope.Algorithm,
                EcdsaP256RiskPolicySignatureVerifier.Algorithm,
                StringComparison.Ordinal))
        {
            throw RiskPolicyValidation.Invalid("RISK_POLICY_SIGNATURE_ALGORITHM_INVALID");
        }

        if (!RiskPolicyValidation.IsValidKeyId(envelope.SigningKeyId))
        {
            throw RiskPolicyValidation.Invalid("RISK_POLICY_SIGNING_KEY_INVALID");
        }

        byte[] signature = envelope.GetSignature();
        try
        {
            if (signature.Length is < 64 or > 256)
            {
                throw RiskPolicyValidation.Invalid("RISK_POLICY_SIGNATURE_INVALID");
            }

            string actualSignatureDigest = Convert.ToHexString(SHA256.HashData(signature)).ToLowerInvariant();
            if (!RiskPolicyValidation.IsExactSha256(envelope.SignatureSha256)
                || !FixedTimeHexEquals(actualSignatureDigest, envelope.SignatureSha256!))
            {
                throw RiskPolicyValidation.Invalid("RISK_POLICY_SIGNATURE_DIGEST_MISMATCH");
            }

            string canonicalPayload = CanonicalPayload(descriptor);
            string payloadDigest = CanonicalJson.Sha256(descriptor);
            if (!RiskPolicyValidation.IsExactSha256(envelope.SignedPayloadSha256)
                || !FixedTimeHexEquals(payloadDigest, envelope.SignedPayloadSha256!))
            {
                throw RiskPolicyValidation.Invalid("RISK_POLICY_PAYLOAD_DIGEST_MISMATCH");
            }

            if (!verifier.Verify(
                    envelope.SigningKeyId!,
                    envelope.Algorithm!,
                    signature,
                    canonicalPayload))
            {
                throw RiskPolicyValidation.Invalid("RISK_POLICY_SIGNATURE_UNTRUSTED");
            }

            return new VerifiedNumericRiskPolicy(
                descriptor,
                payloadDigest,
                actualSignatureDigest,
                envelope.SigningKeyId!);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signature);
        }
    }

    public static string CanonicalPayload(NumericRiskPolicyDescriptor descriptor) =>
        CanonicalJson.Serialize(descriptor);

    private static bool FixedTimeHexEquals(string left, string right)
    {
        byte[] leftBytes = Convert.FromHexString(left);
        byte[] rightBytes = Convert.FromHexString(right);
        try
        {
            return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(leftBytes);
            CryptographicOperations.ZeroMemory(rightBytes);
        }
    }
}

internal static class RiskPolicyValidation
{
    public static void ValidateDescriptor(NumericRiskPolicyDescriptor descriptor)
    {
        if (descriptor.PolicyId == Guid.Empty)
        {
            throw Invalid("RISK_POLICY_ID_MISSING");
        }

        if (descriptor.Version <= 0)
        {
            throw Invalid("RISK_POLICY_VERSION_INVALID");
        }

        if (!IsSafeToken(descriptor.Scope, 1, 200))
        {
            throw Invalid("RISK_POLICY_SCOPE_INVALID");
        }

        if (descriptor.EffectiveFromUtc.Offset != TimeSpan.Zero
            || descriptor.ExpiresAtUtc.Offset != TimeSpan.Zero
            || descriptor.ExpiresAtUtc <= descriptor.EffectiveFromUtc)
        {
            throw Invalid("RISK_POLICY_VALIDITY_INVALID");
        }

        NumericRiskPolicyContent content = descriptor.Content
            ?? throw Invalid("RISK_POLICY_CONTENT_MISSING");
        ValidateContent(content);
    }

    public static void ValidateContent(NumericRiskPolicyContent content)
    {
        RiskDayDefinition riskDay = content.RiskDay
            ?? throw Invalid("RISK_POLICY_RISK_DAY_MISSING");
        if (!IsSafeToken(riskDay.TimeZoneId, 1, 100)
            || !IsSafeToken(riskDay.TimeZoneRulesVersion, 1, 100)
            || riskDay.Boundary is null)
        {
            throw Invalid("RISK_POLICY_RISK_DAY_INVALID");
        }

        try
        {
            if (!TimeZoneInfo.TryConvertIanaIdToWindowsId(riskDay.TimeZoneId!, out _))
            {
                throw Invalid("RISK_POLICY_TIME_ZONE_NOT_IANA");
            }

            _ = TimeZoneInfo.FindSystemTimeZoneById(riskDay.TimeZoneId!);
        }
        catch (TimeZoneNotFoundException)
        {
            throw Invalid("RISK_POLICY_TIME_ZONE_UNKNOWN");
        }
        catch (InvalidTimeZoneException)
        {
            throw Invalid("RISK_POLICY_TIME_ZONE_UNKNOWN");
        }

        RequireNonNegative(content.MaxPerOrderVolume, "RISK_POLICY_MAX_ORDER_VOLUME_INVALID");
        RequireNonNegative(content.MaxAccountPositionVolume, "RISK_POLICY_MAX_POSITION_VOLUME_INVALID");
        RequireNonNegative(content.MaxAccountGrossNotional, "RISK_POLICY_MAX_NOTIONAL_INVALID");
        RequireNonNegative(content.MaxDailyLoss, "RISK_POLICY_MAX_DAILY_LOSS_INVALID");
        RequireNonNegative(content.MaxDrawdown, "RISK_POLICY_MAX_DRAWDOWN_INVALID");
        RequireNonNegative(content.MaxSpreadPoints, "RISK_POLICY_MAX_SPREAD_INVALID");
        RequireNonNegative(content.MaxSlippagePoints, "RISK_POLICY_MAX_SLIPPAGE_INVALID");
        RequireNonNegative(content.MaxOpenPositions, "RISK_POLICY_MAX_POSITIONS_INVALID");
        RequireNonNegative(content.MaxOpenOrders, "RISK_POLICY_MAX_OPEN_ORDERS_INVALID");
        RequireNonNegative(content.MaxOrdersPerWindow, "RISK_POLICY_MAX_ORDER_RATE_INVALID");
        RequirePositive(content.OrderRateWindowMilliseconds, "RISK_POLICY_ORDER_WINDOW_INVALID");
        RequirePositive(content.MaxStopLossDistancePoints, "RISK_POLICY_MAX_STOP_DISTANCE_INVALID");
        RequirePositive(content.MinTakeProfitDistancePoints, "RISK_POLICY_MIN_TAKE_PROFIT_INVALID");

        ValidateFreshness(content.IncreaseFreshness, "INCREASE");
        ValidateFreshness(content.ReduceProtectFreshness, "REDUCE_PROTECT");

        RequireTrue(content.DemoOnly, "RISK_POLICY_DEMO_ONLY_REQUIRED");
        RequireTrue(content.HedgingOnly, "RISK_POLICY_HEDGING_ONLY_REQUIRED");
        RequireTrue(content.RequireBrokerHostedStopLoss, "RISK_POLICY_HOSTED_STOP_REQUIRED");
        RequireTrue(content.RequireBrokerHostedTakeProfit, "RISK_POLICY_HOSTED_TAKE_PROFIT_REQUIRED");
        RequireTrue(
            content.BlockExposureIncreaseOnExternalActivity,
            "RISK_POLICY_EXTERNAL_ACTIVITY_BLOCK_REQUIRED");
    }

    public static bool IsValidKeyId(string? value) => value is { Length: >= 1 and <= 200 }
        && value.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '-' or '_' or '.' or ':' or '/');

    public static bool IsExactSha256(string? value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static DomainException Invalid(string code) =>
        new(code, "The numeric risk policy is missing, invalid, incompatible, or untrusted.");

    private static void ValidateFreshness(RiskFreshnessLimits? limits, string profile)
    {
        if (limits is null)
        {
            throw Invalid($"RISK_POLICY_{profile}_FRESHNESS_MISSING");
        }

        RequireNonNegative(limits.QuoteMaxAgeMilliseconds, $"RISK_POLICY_{profile}_QUOTE_AGE_INVALID");
        RequireNonNegative(limits.AccountMaxAgeMilliseconds, $"RISK_POLICY_{profile}_ACCOUNT_AGE_INVALID");
        RequireNonNegative(limits.PositionMaxAgeMilliseconds, $"RISK_POLICY_{profile}_POSITION_AGE_INVALID");
        RequireNonNegative(limits.OrderMaxAgeMilliseconds, $"RISK_POLICY_{profile}_ORDER_AGE_INVALID");
        RequireNonNegative(limits.SymbolMaxAgeMilliseconds, $"RISK_POLICY_{profile}_SYMBOL_AGE_INVALID");
        RequireNonNegative(
            limits.ConversionRateMaxAgeMilliseconds,
            $"RISK_POLICY_{profile}_CONVERSION_AGE_INVALID");
    }

    private static void RequireTrue(bool? value, string code)
    {
        if (value is not true)
        {
            throw Invalid(code);
        }
    }

    private static void RequireNonNegative(decimal? value, string code)
    {
        if (value is null or < 0)
        {
            throw Invalid(code);
        }
    }

    private static void RequireNonNegative(int? value, string code)
    {
        if (value is null or < 0)
        {
            throw Invalid(code);
        }
    }

    private static void RequireNonNegative(long? value, string code)
    {
        if (value is null or < 0)
        {
            throw Invalid(code);
        }
    }

    private static void RequirePositive(decimal? value, string code)
    {
        if (value is null or <= 0)
        {
            throw Invalid(code);
        }
    }

    private static void RequirePositive(long? value, string code)
    {
        if (value is null or <= 0)
        {
            throw Invalid(code);
        }
    }

    private static bool IsSafeToken(string? value, int minimum, int maximum) =>
        value is not null
        && value.Length >= minimum
        && value.Length <= maximum
        && value.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '-' or '_' or '.' or ':' or '/');
}
