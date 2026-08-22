using YO4X.BuildingBlocks;

namespace YO4X.Risk;

public sealed record EffectiveRiskFreshnessLimits(
    long QuoteMaxAgeMilliseconds,
    long AccountMaxAgeMilliseconds,
    long PositionMaxAgeMilliseconds,
    long OrderMaxAgeMilliseconds,
    long SymbolMaxAgeMilliseconds,
    long ConversionRateMaxAgeMilliseconds);

public sealed record EffectiveRiskPolicySource(
    Guid PolicyId,
    long Version,
    string Scope,
    string PayloadDigest,
    string SignatureDigest,
    string SigningKeyId);

/// <summary>
/// Deterministic meet of authenticated immutable policy versions. Lower numeric
/// maxima win, higher numeric minima win, and mandatory safety booleans cannot
/// be weakened by any scope.
/// </summary>
public sealed class EffectiveNumericRiskPolicy
{
    private EffectiveNumericRiskPolicy(
        string riskDayTimeZoneId,
        string riskDayTimeZoneRulesVersion,
        TimeOnly riskDayBoundary,
        decimal maxPerOrderVolume,
        decimal maxAccountPositionVolume,
        decimal maxAccountGrossNotional,
        int maxOpenPositions,
        int maxOpenOrders,
        int maxOrdersPerWindow,
        long orderRateWindowMilliseconds,
        decimal maxDailyLoss,
        decimal maxDrawdown,
        decimal maxSpreadPoints,
        decimal maxSlippagePoints,
        decimal maxStopLossDistancePoints,
        decimal minTakeProfitDistancePoints,
        EffectiveRiskFreshnessLimits increaseFreshness,
        EffectiveRiskFreshnessLimits reduceProtectFreshness,
        bool demoOnly,
        bool hedgingOnly,
        bool requireBrokerHostedStopLoss,
        bool requireBrokerHostedTakeProfit,
        bool blockExposureIncreaseOnExternalActivity,
        DateTimeOffset effectiveFromUtc,
        DateTimeOffset expiresAtUtc,
        IReadOnlyList<EffectiveRiskPolicySource> sources,
        string digest)
    {
        RiskDayTimeZoneId = riskDayTimeZoneId;
        RiskDayTimeZoneRulesVersion = riskDayTimeZoneRulesVersion;
        RiskDayBoundary = riskDayBoundary;
        MaxPerOrderVolume = maxPerOrderVolume;
        MaxAccountPositionVolume = maxAccountPositionVolume;
        MaxAccountGrossNotional = maxAccountGrossNotional;
        MaxOpenPositions = maxOpenPositions;
        MaxOpenOrders = maxOpenOrders;
        MaxOrdersPerWindow = maxOrdersPerWindow;
        OrderRateWindowMilliseconds = orderRateWindowMilliseconds;
        MaxDailyLoss = maxDailyLoss;
        MaxDrawdown = maxDrawdown;
        MaxSpreadPoints = maxSpreadPoints;
        MaxSlippagePoints = maxSlippagePoints;
        MaxStopLossDistancePoints = maxStopLossDistancePoints;
        MinTakeProfitDistancePoints = minTakeProfitDistancePoints;
        IncreaseFreshness = increaseFreshness;
        ReduceProtectFreshness = reduceProtectFreshness;
        DemoOnly = demoOnly;
        HedgingOnly = hedgingOnly;
        RequireBrokerHostedStopLoss = requireBrokerHostedStopLoss;
        RequireBrokerHostedTakeProfit = requireBrokerHostedTakeProfit;
        BlockExposureIncreaseOnExternalActivity = blockExposureIncreaseOnExternalActivity;
        EffectiveFromUtc = effectiveFromUtc;
        ExpiresAtUtc = expiresAtUtc;
        Sources = sources;
        Digest = digest;
    }

    public string RiskDayTimeZoneId { get; }
    public string RiskDayTimeZoneRulesVersion { get; }
    public TimeOnly RiskDayBoundary { get; }
    public decimal MaxPerOrderVolume { get; }
    public decimal MaxAccountPositionVolume { get; }
    public decimal MaxAccountGrossNotional { get; }
    public int MaxOpenPositions { get; }
    public int MaxOpenOrders { get; }
    public int MaxOrdersPerWindow { get; }
    public long OrderRateWindowMilliseconds { get; }
    public decimal MaxDailyLoss { get; }
    public decimal MaxDrawdown { get; }
    public decimal MaxSpreadPoints { get; }
    public decimal MaxSlippagePoints { get; }
    public decimal MaxStopLossDistancePoints { get; }
    public decimal MinTakeProfitDistancePoints { get; }
    public EffectiveRiskFreshnessLimits IncreaseFreshness { get; }
    public EffectiveRiskFreshnessLimits ReduceProtectFreshness { get; }
    public bool DemoOnly { get; }
    public bool HedgingOnly { get; }
    public bool RequireBrokerHostedStopLoss { get; }
    public bool RequireBrokerHostedTakeProfit { get; }
    public bool BlockExposureIncreaseOnExternalActivity { get; }
    public DateTimeOffset EffectiveFromUtc { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
    public IReadOnlyList<EffectiveRiskPolicySource> Sources { get; }
    public string Digest { get; }

    public static EffectiveNumericRiskPolicy Meet(
        IEnumerable<VerifiedNumericRiskPolicy> policies,
        DateTimeOffset evaluatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(policies);
        if (evaluatedAtUtc.Offset != TimeSpan.Zero)
        {
            throw RiskPolicyValidation.Invalid("RISK_POLICY_EVALUATION_TIME_NOT_UTC");
        }

        VerifiedNumericRiskPolicy[] ordered = Normalize(policies);
        if (ordered.Length == 0)
        {
            throw RiskPolicyValidation.Invalid("RISK_POLICY_SET_EMPTY");
        }

        foreach (VerifiedNumericRiskPolicy policy in ordered)
        {
            if (evaluatedAtUtc < policy.Descriptor.EffectiveFromUtc
                || evaluatedAtUtc >= policy.Descriptor.ExpiresAtUtc)
            {
                throw RiskPolicyValidation.Invalid("RISK_POLICY_NOT_CURRENT");
            }
        }

        NumericRiskPolicyDescriptor firstDescriptor = ordered[0].Descriptor;
        NumericRiskPolicyContent first = firstDescriptor.Content!;
        RiskDayDefinition firstRiskDay = first.RiskDay!;
        EnsureCompatibleRiskDays(ordered, firstRiskDay);
        EnsureCompatibleOrderRateWindows(ordered, first.OrderRateWindowMilliseconds!.Value);

        DateTimeOffset effectiveFrom = ordered.Max(policy => policy.Descriptor.EffectiveFromUtc);
        DateTimeOffset expiresAt = ordered.Min(policy => policy.Descriptor.ExpiresAtUtc);
        if (expiresAt <= effectiveFrom)
        {
            throw RiskPolicyValidation.Invalid("RISK_POLICY_VALIDITY_INTERSECTION_EMPTY");
        }

        EffectiveRiskFreshnessLimits increaseFreshness = MeetFreshness(
            ordered.Select(policy => policy.Descriptor.Content!.IncreaseFreshness!));
        EffectiveRiskFreshnessLimits reduceFreshness = MeetFreshness(
            ordered.Select(policy => policy.Descriptor.Content!.ReduceProtectFreshness!));
        EffectiveRiskPolicySource[] sources = ordered
            .Select(policy => new EffectiveRiskPolicySource(
                policy.Descriptor.PolicyId,
                policy.Descriptor.Version,
                policy.Descriptor.Scope!,
                policy.PayloadDigest,
                policy.SignatureDigest,
                policy.SigningKeyId))
            .ToArray();

        var digestInput = new
        {
            RiskDayTimeZoneId = firstRiskDay.TimeZoneId,
            RiskDayTimeZoneRulesVersion = firstRiskDay.TimeZoneRulesVersion,
            RiskDayBoundary = firstRiskDay.Boundary,
            MaxPerOrderVolume = ordered.Min(Value(policy => policy.MaxPerOrderVolume)),
            MaxAccountPositionVolume = ordered.Min(Value(policy => policy.MaxAccountPositionVolume)),
            MaxAccountGrossNotional = ordered.Min(Value(policy => policy.MaxAccountGrossNotional)),
            MaxOpenPositions = ordered.Min(IntValue(policy => policy.MaxOpenPositions)),
            MaxOpenOrders = ordered.Min(IntValue(policy => policy.MaxOpenOrders)),
            MaxOrdersPerWindow = ordered.Min(IntValue(policy => policy.MaxOrdersPerWindow)),
            OrderRateWindowMilliseconds = first.OrderRateWindowMilliseconds.Value,
            MaxDailyLoss = ordered.Min(Value(policy => policy.MaxDailyLoss)),
            MaxDrawdown = ordered.Min(Value(policy => policy.MaxDrawdown)),
            MaxSpreadPoints = ordered.Min(Value(policy => policy.MaxSpreadPoints)),
            MaxSlippagePoints = ordered.Min(Value(policy => policy.MaxSlippagePoints)),
            MaxStopLossDistancePoints = ordered.Min(Value(policy => policy.MaxStopLossDistancePoints)),
            MinTakeProfitDistancePoints = ordered.Max(Value(policy => policy.MinTakeProfitDistancePoints)),
            IncreaseFreshness = increaseFreshness,
            ReduceProtectFreshness = reduceFreshness,
            DemoOnly = ordered.Any(policy => policy.Descriptor.Content!.DemoOnly!.Value),
            HedgingOnly = ordered.Any(policy => policy.Descriptor.Content!.HedgingOnly!.Value),
            RequireBrokerHostedStopLoss = ordered.Any(
                policy => policy.Descriptor.Content!.RequireBrokerHostedStopLoss!.Value),
            RequireBrokerHostedTakeProfit = ordered.Any(
                policy => policy.Descriptor.Content!.RequireBrokerHostedTakeProfit!.Value),
            BlockExposureIncreaseOnExternalActivity = ordered.Any(
                policy => policy.Descriptor.Content!.BlockExposureIncreaseOnExternalActivity!.Value),
            EffectiveFromUtc = effectiveFrom,
            ExpiresAtUtc = expiresAt,
            Sources = sources
        };

        string digest = CanonicalJson.Sha256(digestInput);
        return new EffectiveNumericRiskPolicy(
            firstRiskDay.TimeZoneId!,
            firstRiskDay.TimeZoneRulesVersion!,
            firstRiskDay.Boundary!.Value,
            digestInput.MaxPerOrderVolume,
            digestInput.MaxAccountPositionVolume,
            digestInput.MaxAccountGrossNotional,
            digestInput.MaxOpenPositions,
            digestInput.MaxOpenOrders,
            digestInput.MaxOrdersPerWindow,
            digestInput.OrderRateWindowMilliseconds,
            digestInput.MaxDailyLoss,
            digestInput.MaxDrawdown,
            digestInput.MaxSpreadPoints,
            digestInput.MaxSlippagePoints,
            digestInput.MaxStopLossDistancePoints,
            digestInput.MinTakeProfitDistancePoints,
            increaseFreshness,
            reduceFreshness,
            digestInput.DemoOnly,
            digestInput.HedgingOnly,
            digestInput.RequireBrokerHostedStopLoss,
            digestInput.RequireBrokerHostedTakeProfit,
            digestInput.BlockExposureIncreaseOnExternalActivity,
            effectiveFrom,
            expiresAt,
            Array.AsReadOnly(sources),
            digest);
    }

    public bool IsAtLeastAsRestrictiveAs(EffectiveNumericRiskPolicy baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        return HasSameRiskDay(baseline)
            && MaxPerOrderVolume <= baseline.MaxPerOrderVolume
            && MaxAccountPositionVolume <= baseline.MaxAccountPositionVolume
            && MaxAccountGrossNotional <= baseline.MaxAccountGrossNotional
            && MaxOpenPositions <= baseline.MaxOpenPositions
            && MaxOpenOrders <= baseline.MaxOpenOrders
            && MaxOrdersPerWindow <= baseline.MaxOrdersPerWindow
            && OrderRateWindowMilliseconds == baseline.OrderRateWindowMilliseconds
            && MaxDailyLoss <= baseline.MaxDailyLoss
            && MaxDrawdown <= baseline.MaxDrawdown
            && MaxSpreadPoints <= baseline.MaxSpreadPoints
            && MaxSlippagePoints <= baseline.MaxSlippagePoints
            && MaxStopLossDistancePoints <= baseline.MaxStopLossDistancePoints
            && MinTakeProfitDistancePoints >= baseline.MinTakeProfitDistancePoints
            && IsAtLeastAsRestrictive(IncreaseFreshness, baseline.IncreaseFreshness)
            && IsAtLeastAsRestrictive(ReduceProtectFreshness, baseline.ReduceProtectFreshness)
            && (DemoOnly || !baseline.DemoOnly)
            && (HedgingOnly || !baseline.HedgingOnly)
            && (RequireBrokerHostedStopLoss || !baseline.RequireBrokerHostedStopLoss)
            && (RequireBrokerHostedTakeProfit || !baseline.RequireBrokerHostedTakeProfit)
            && (BlockExposureIncreaseOnExternalActivity
                || !baseline.BlockExposureIncreaseOnExternalActivity);
    }

    private static VerifiedNumericRiskPolicy[] Normalize(
        IEnumerable<VerifiedNumericRiskPolicy> policies)
    {
        var unique = new Dictionary<(Guid PolicyId, long Version), VerifiedNumericRiskPolicy>();
        foreach (VerifiedNumericRiskPolicy? policy in policies)
        {
            if (policy is null)
            {
                throw RiskPolicyValidation.Invalid("RISK_POLICY_NULL");
            }

            var key = (policy.Descriptor.PolicyId, policy.Descriptor.Version);
            if (unique.TryGetValue(key, out VerifiedNumericRiskPolicy? existing)
                && !string.Equals(existing.PayloadDigest, policy.PayloadDigest, StringComparison.Ordinal))
            {
                throw RiskPolicyValidation.Invalid("RISK_POLICY_VERSION_DIGEST_CONFLICT");
            }

            if (existing is not null
                && (!string.Equals(
                    existing.SignatureDigest,
                    policy.SignatureDigest,
                    StringComparison.Ordinal)
                    || !string.Equals(
                        existing.SigningKeyId,
                        policy.SigningKeyId,
                        StringComparison.Ordinal)))
            {
                throw RiskPolicyValidation.Invalid("RISK_POLICY_VERSION_SIGNATURE_CONFLICT");
            }

            unique[key] = policy;
        }

        return unique.Values
            .OrderBy(policy => policy.Descriptor.PolicyId)
            .ThenBy(policy => policy.Descriptor.Version)
            .ThenBy(policy => policy.PayloadDigest, StringComparer.Ordinal)
            .ToArray();
    }

    private static void EnsureCompatibleRiskDays(
        IEnumerable<VerifiedNumericRiskPolicy> policies,
        RiskDayDefinition expected)
    {
        foreach (VerifiedNumericRiskPolicy policy in policies)
        {
            RiskDayDefinition candidate = policy.Descriptor.Content!.RiskDay!;
            if (!string.Equals(candidate.TimeZoneId, expected.TimeZoneId, StringComparison.Ordinal)
                || !string.Equals(
                    candidate.TimeZoneRulesVersion,
                    expected.TimeZoneRulesVersion,
                    StringComparison.Ordinal)
                || candidate.Boundary != expected.Boundary)
            {
                throw RiskPolicyValidation.Invalid("RISK_POLICY_RISK_DAY_INCOMPATIBLE");
            }
        }
    }

    private static void EnsureCompatibleOrderRateWindows(
        IEnumerable<VerifiedNumericRiskPolicy> policies,
        long expectedWindowMilliseconds)
    {
        if (policies.Any(policy =>
                policy.Descriptor.Content!.OrderRateWindowMilliseconds!.Value
                != expectedWindowMilliseconds))
        {
            // A count/window pair has no safe scalar meet when window sizes differ.
            // Keeping both independent rules would be required before allowing that case.
            throw RiskPolicyValidation.Invalid("RISK_POLICY_ORDER_WINDOW_INCOMPATIBLE");
        }
    }

    private static EffectiveRiskFreshnessLimits MeetFreshness(
        IEnumerable<RiskFreshnessLimits> limits)
    {
        RiskFreshnessLimits[] values = limits.ToArray();
        return new EffectiveRiskFreshnessLimits(
            values.Min(value => value.QuoteMaxAgeMilliseconds!.Value),
            values.Min(value => value.AccountMaxAgeMilliseconds!.Value),
            values.Min(value => value.PositionMaxAgeMilliseconds!.Value),
            values.Min(value => value.OrderMaxAgeMilliseconds!.Value),
            values.Min(value => value.SymbolMaxAgeMilliseconds!.Value),
            values.Min(value => value.ConversionRateMaxAgeMilliseconds!.Value));
    }

    private static Func<VerifiedNumericRiskPolicy, decimal> Value(
        Func<NumericRiskPolicyContent, decimal?> selector) =>
        policy => selector(policy.Descriptor.Content!)!.Value;

    private static Func<VerifiedNumericRiskPolicy, int> IntValue(
        Func<NumericRiskPolicyContent, int?> selector) =>
        policy => selector(policy.Descriptor.Content!)!.Value;

    private bool HasSameRiskDay(EffectiveNumericRiskPolicy baseline) =>
        string.Equals(RiskDayTimeZoneId, baseline.RiskDayTimeZoneId, StringComparison.Ordinal)
        && string.Equals(
            RiskDayTimeZoneRulesVersion,
            baseline.RiskDayTimeZoneRulesVersion,
            StringComparison.Ordinal)
        && RiskDayBoundary == baseline.RiskDayBoundary;

    private static bool IsAtLeastAsRestrictive(
        EffectiveRiskFreshnessLimits candidate,
        EffectiveRiskFreshnessLimits baseline) =>
        candidate.QuoteMaxAgeMilliseconds <= baseline.QuoteMaxAgeMilliseconds
        && candidate.AccountMaxAgeMilliseconds <= baseline.AccountMaxAgeMilliseconds
        && candidate.PositionMaxAgeMilliseconds <= baseline.PositionMaxAgeMilliseconds
        && candidate.OrderMaxAgeMilliseconds <= baseline.OrderMaxAgeMilliseconds
        && candidate.SymbolMaxAgeMilliseconds <= baseline.SymbolMaxAgeMilliseconds
        && candidate.ConversionRateMaxAgeMilliseconds <= baseline.ConversionRateMaxAgeMilliseconds;
}
