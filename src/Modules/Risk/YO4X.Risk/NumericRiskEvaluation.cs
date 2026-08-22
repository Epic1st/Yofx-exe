using System.Globalization;
using YO4X.BuildingBlocks;

namespace YO4X.Risk;

public enum RiskActionClass
{
    ExposureIncrease = 0,
    ExposureReduction = 1,
    Protection = 2,
    PendingOrderCancellation = 3,
    EmergencyClose = 4
}

public enum BrokerAccountEnvironment
{
    Unknown = 0,
    Demo = 1,
    Live = 2,
    Contest = 3
}

public enum BrokerAccountMode
{
    Unknown = 0,
    Hedging = 1,
    Netting = 2,
    Exchange = 3
}

public sealed record RiskSnapshotTimestamps(
    DateTimeOffset? QuoteAsOfUtc,
    DateTimeOffset? AccountAsOfUtc,
    DateTimeOffset? PositionAsOfUtc,
    DateTimeOffset? OrderAsOfUtc,
    DateTimeOffset? SymbolAsOfUtc,
    DateTimeOffset? ConversionRateAsOfUtc);

public sealed record MarketRiskSnapshot(
    decimal? SpreadPoints,
    decimal? RequestedSlippagePoints,
    bool? MarketSessionOpen,
    bool? RequestedDirectionTradable,
    decimal? BrokerMinimumStopDistancePoints);

public sealed record AccountRiskSnapshot(
    BrokerAccountEnvironment Environment,
    BrokerAccountMode Mode,
    decimal? CurrentEquity,
    bool? AutomatedTradingAllowed,
    bool? UnexpectedExternalActivity,
    bool? TargetOwnershipConfirmed);

public sealed record ExposureRiskSnapshot(
    decimal? RequestedOrderVolume,
    decimal? ProjectedAccountPositionVolume,
    decimal? ProjectedAccountGrossNotional,
    int? ProjectedOpenPositionCount,
    int? ProjectedOpenOrderCount,
    int? OrdersAlreadySubmittedInWindow,
    DateTimeOffset? OrderRateWindowStartedAtUtc,
    DateTimeOffset? OrderRateSnapshotAsOfUtc);

public sealed record ProtectionRiskSnapshot(
    bool? HasBrokerHostedStopLoss,
    decimal? StopLossDistancePoints,
    bool? HasBrokerHostedTakeProfit,
    decimal? TakeProfitDistancePoints,
    bool? RemovesExistingStopLoss,
    bool? WidensExistingStopLoss);

public sealed record RiskDayStateSnapshot(
    string? RiskDayKey,
    DateTimeOffset? AsOfUtc,
    decimal? StartOfDayEquity,
    decimal? EquityHighWater,
    decimal? VerifiedDepositsSinceBaseline,
    decimal? VerifiedWithdrawalsSinceBaseline);

public sealed record NumericRiskEvaluationInput(
    DateTimeOffset EvaluatedAtUtc,
    RiskActionClass ActionClass,
    RiskSnapshotTimestamps? Timestamps,
    MarketRiskSnapshot? Market,
    AccountRiskSnapshot? Account,
    ExposureRiskSnapshot? Exposure,
    ProtectionRiskSnapshot? Protection,
    RiskDayStateSnapshot? RiskDayState);

public enum RiskRuleOutcome
{
    Passed = 0,
    Failed = 1,
    NotApplicable = 2
}

public sealed record NumericRiskRuleResult(
    string Code,
    RiskRuleOutcome Outcome,
    string? Observed,
    string? Limit);

public enum NumericRiskDecisionDisposition
{
    Allowed = 0,
    Rejected = 1
}

public sealed record NumericRiskDecision(
    NumericRiskDecisionDisposition Disposition,
    RiskActionClass ActionClass,
    string PolicyDigest,
    string InputDigest,
    string DecisionDigest,
    string? RiskDayKey,
    decimal? AdjustedStartOfDayEquity,
    decimal? AdjustedEquityHighWater,
    decimal? DailyLoss,
    decimal? Drawdown,
    IReadOnlyList<NumericRiskRuleResult> Rules)
{
    public bool IsAllowed => Disposition == NumericRiskDecisionDisposition.Allowed;
}

/// <summary>
/// Pure, deterministic risk decision. It performs no broker or database I/O and
/// returns stable rule codes plus hashes suitable for durable replay evidence.
/// </summary>
public static class NumericRiskEvaluator
{
    private const string NotApplicable = "not_applicable";

    public static NumericRiskDecision Evaluate(
        EffectiveNumericRiskPolicy policy,
        NumericRiskEvaluationInput input)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(input);

        string inputDigest = CanonicalJson.Sha256(input);
        List<NumericRiskRuleResult> rules = ValidateInput(input);
        if (rules.Any(rule => rule.Outcome == RiskRuleOutcome.Failed))
        {
            return CreateDecision(
                policy,
                input,
                inputDigest,
                riskDayKey: null,
                adjustedStart: null,
                adjustedHighWater: null,
                dailyLoss: null,
                drawdown: null,
                rules);
        }

        NumericRiskRuleResult policyCurrent = CheckCurrentPolicy(policy, input.EvaluatedAtUtc);
        rules.Add(policyCurrent);
        if (policyCurrent.Outcome == RiskRuleOutcome.Failed)
        {
            return CreateDecision(
                policy,
                input,
                inputDigest,
                riskDayKey: null,
                adjustedStart: null,
                adjustedHighWater: null,
                dailyLoss: null,
                drawdown: null,
                rules);
        }

        RiskSnapshotTimestamps timestamps = input.Timestamps!;
        MarketRiskSnapshot market = input.Market!;
        AccountRiskSnapshot account = input.Account!;
        ExposureRiskSnapshot exposure = input.Exposure!;
        ProtectionRiskSnapshot protection = input.Protection!;
        RiskDayStateSnapshot riskState = input.RiskDayState!;
        bool increase = input.ActionClass == RiskActionClass.ExposureIncrease;
        EffectiveRiskFreshnessLimits freshness = increase
            ? policy.IncreaseFreshness
            : policy.ReduceProtectFreshness;

        string expectedRiskDayKey = CalculateRiskDayKey(policy, input.EvaluatedAtUtc);
        if (!TryCalculateRiskMetrics(
                account,
                riskState,
                out decimal adjustedStart,
                out decimal adjustedHighWater,
                out decimal dailyLoss,
                out decimal drawdown))
        {
            rules.Add(Check("risk_arithmetic_valid", false, "overflow", "exact_decimal"));
            return CreateDecision(
                policy,
                input,
                inputDigest,
                expectedRiskDayKey,
                adjustedStart: null,
                adjustedHighWater: null,
                dailyLoss: null,
                drawdown: null,
                rules);
        }

        rules.Add(Check("risk_arithmetic_valid", true, "exact_decimal", "exact_decimal"));
        rules.Add(Check(
            "demo_account_only",
            account.Environment == BrokerAccountEnvironment.Demo,
            account.Environment.ToString(),
            BrokerAccountEnvironment.Demo.ToString()));
        rules.Add(Check(
            "hedging_account_only",
            account.Mode == BrokerAccountMode.Hedging,
            account.Mode.ToString(),
            BrokerAccountMode.Hedging.ToString()));
        rules.Add(Check(
            "automated_trading_allowed",
            account.AutomatedTradingAllowed!.Value,
            Boolean(account.AutomatedTradingAllowed.Value),
            Boolean(true)));
        rules.Add(Check(
            "target_ownership_confirmed",
            account.TargetOwnershipConfirmed!.Value,
            Boolean(account.TargetOwnershipConfirmed.Value),
            Boolean(true)));
        rules.Add(increase
            ? Check(
                "unexpected_external_activity",
                !account.UnexpectedExternalActivity!.Value,
                Boolean(account.UnexpectedExternalActivity.Value),
                Boolean(false))
            : Skip("unexpected_external_activity"));

        rules.Add(CheckFreshness(
            "quote_freshness",
            input.EvaluatedAtUtc,
            timestamps.QuoteAsOfUtc!.Value,
            freshness.QuoteMaxAgeMilliseconds));
        rules.Add(CheckFreshness(
            "account_freshness",
            input.EvaluatedAtUtc,
            timestamps.AccountAsOfUtc!.Value,
            freshness.AccountMaxAgeMilliseconds));
        rules.Add(CheckFreshness(
            "position_freshness",
            input.EvaluatedAtUtc,
            timestamps.PositionAsOfUtc!.Value,
            freshness.PositionMaxAgeMilliseconds));
        rules.Add(CheckFreshness(
            "order_freshness",
            input.EvaluatedAtUtc,
            timestamps.OrderAsOfUtc!.Value,
            freshness.OrderMaxAgeMilliseconds));
        rules.Add(CheckFreshness(
            "symbol_freshness",
            input.EvaluatedAtUtc,
            timestamps.SymbolAsOfUtc!.Value,
            freshness.SymbolMaxAgeMilliseconds));
        rules.Add(CheckFreshness(
            "conversion_rate_freshness",
            input.EvaluatedAtUtc,
            timestamps.ConversionRateAsOfUtc!.Value,
            freshness.ConversionRateMaxAgeMilliseconds));
        rules.Add(CheckFreshness(
            "risk_state_freshness",
            input.EvaluatedAtUtc,
            riskState.AsOfUtc!.Value,
            freshness.AccountMaxAgeMilliseconds));

        rules.Add(Check(
            "market_session_open",
            market.MarketSessionOpen!.Value,
            Boolean(market.MarketSessionOpen.Value),
            Boolean(true)));
        rules.Add(Check(
            "requested_direction_tradable",
            market.RequestedDirectionTradable!.Value,
            Boolean(market.RequestedDirectionTradable.Value),
            Boolean(true)));
        rules.Add(CheckMaximum(
            "spread_limit",
            market.SpreadPoints!.Value,
            policy.MaxSpreadPoints));
        rules.Add(CheckMaximum(
            "slippage_limit",
            market.RequestedSlippagePoints!.Value,
            policy.MaxSlippagePoints));

        rules.Add(increase
            ? CheckMaximum(
                "per_order_volume_limit",
                exposure.RequestedOrderVolume!.Value,
                policy.MaxPerOrderVolume)
            : Skip("per_order_volume_limit"));
        rules.Add(increase
            ? CheckMaximum(
                "account_position_volume_limit",
                exposure.ProjectedAccountPositionVolume!.Value,
                policy.MaxAccountPositionVolume)
            : Skip("account_position_volume_limit"));
        rules.Add(increase
            ? CheckMaximum(
                "account_gross_notional_limit",
                exposure.ProjectedAccountGrossNotional!.Value,
                policy.MaxAccountGrossNotional)
            : Skip("account_gross_notional_limit"));
        rules.Add(increase
            ? CheckMaximum(
                "open_position_count_limit",
                exposure.ProjectedOpenPositionCount!.Value,
                policy.MaxOpenPositions)
            : Skip("open_position_count_limit"));
        rules.Add(increase
            ? CheckMaximum(
                "open_order_count_limit",
                exposure.ProjectedOpenOrderCount!.Value,
                policy.MaxOpenOrders)
            : Skip("open_order_count_limit"));

        double actualWindow = (
            exposure.OrderRateSnapshotAsOfUtc!.Value
            - exposure.OrderRateWindowStartedAtUtc!.Value).TotalMilliseconds;
        bool exactWindowValue = actualWindow >= long.MinValue
            && actualWindow <= long.MaxValue
            && actualWindow == Math.Truncate(actualWindow);
        long actualWindowMilliseconds = exactWindowValue ? (long)actualWindow : long.MaxValue;
        rules.Add(Check(
            "order_rate_window_exact",
            exactWindowValue && actualWindowMilliseconds == policy.OrderRateWindowMilliseconds,
            exactWindowValue ? Integer(actualWindowMilliseconds) : "non_integral_or_out_of_range",
            Integer(policy.OrderRateWindowMilliseconds)));
        rules.Add(CheckMaximum(
            "order_rate_limit",
            (long)exposure.OrdersAlreadySubmittedInWindow!.Value + 1L,
            policy.MaxOrdersPerWindow));

        rules.Add(Check(
            "risk_day_boundary",
            string.Equals(riskState.RiskDayKey, expectedRiskDayKey, StringComparison.Ordinal),
            riskState.RiskDayKey,
            expectedRiskDayKey));
        rules.Add(increase
            ? CheckMaximum("daily_loss_limit", dailyLoss, policy.MaxDailyLoss)
            : Skip("daily_loss_limit"));
        rules.Add(increase
            ? CheckMaximum("drawdown_limit", drawdown, policy.MaxDrawdown)
            : Skip("drawdown_limit"));

        rules.Add(increase
            ? Check(
                "broker_hosted_stop_loss",
                protection.HasBrokerHostedStopLoss!.Value,
                Boolean(protection.HasBrokerHostedStopLoss.Value),
                Boolean(true))
            : Skip("broker_hosted_stop_loss"));
        rules.Add(increase
            ? CheckStopLossDistance(policy, market, protection)
            : Skip("stop_loss_distance"));
        rules.Add(increase
            ? Check(
                "broker_hosted_take_profit",
                protection.HasBrokerHostedTakeProfit!.Value,
                Boolean(protection.HasBrokerHostedTakeProfit.Value),
                Boolean(true))
            : Skip("broker_hosted_take_profit"));
        rules.Add(increase
            ? CheckTakeProfitDistance(policy, market, protection)
            : Skip("take_profit_distance"));
        rules.Add(Check(
            "stop_loss_not_removed",
            !protection.RemovesExistingStopLoss!.Value,
            Boolean(protection.RemovesExistingStopLoss.Value),
            Boolean(false)));
        rules.Add(Check(
            "stop_loss_not_widened",
            !protection.WidensExistingStopLoss!.Value,
            Boolean(protection.WidensExistingStopLoss.Value),
            Boolean(false)));

        return CreateDecision(
            policy,
            input,
            inputDigest,
            expectedRiskDayKey,
            adjustedStart,
            adjustedHighWater,
            dailyLoss,
            drawdown,
            rules);
    }

    public static string CalculateRiskDayKey(
        EffectiveNumericRiskPolicy policy,
        DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (timestampUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainException("RISK_TIMESTAMP_NOT_UTC", "Risk timestamps must use UTC.");
        }

        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(policy.RiskDayTimeZoneId);
        DateTimeOffset local = TimeZoneInfo.ConvertTime(timestampUtc, timeZone);
        DateOnly boundaryDate = DateOnly.FromDateTime(local.DateTime);
        if (TimeOnly.FromDateTime(local.DateTime) < policy.RiskDayBoundary)
        {
            boundaryDate = boundaryDate.AddDays(-1);
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{policy.RiskDayTimeZoneId}|{policy.RiskDayTimeZoneRulesVersion}|{boundaryDate:yyyy-MM-dd}|{policy.RiskDayBoundary:HH:mm:ss.fffffff}");
    }

    private static List<NumericRiskRuleResult> ValidateInput(NumericRiskEvaluationInput input)
    {
        var rules = new List<NumericRiskRuleResult>();
        AddSchemaRule(
            rules,
            "input_action_class_valid",
            Enum.IsDefined(input.ActionClass));
        AddSchemaRule(
            rules,
            "input_evaluation_time_utc",
            input.EvaluatedAtUtc.Offset == TimeSpan.Zero);
        AddSchemaRule(rules, "input_timestamps_present", input.Timestamps is not null);
        AddSchemaRule(rules, "input_market_present", input.Market is not null);
        AddSchemaRule(rules, "input_account_present", input.Account is not null);
        AddSchemaRule(rules, "input_exposure_present", input.Exposure is not null);
        AddSchemaRule(rules, "input_protection_present", input.Protection is not null);
        AddSchemaRule(rules, "input_risk_day_state_present", input.RiskDayState is not null);

        if (input.Timestamps is not null)
        {
            DateTimeOffset?[] values =
            [
                input.Timestamps.QuoteAsOfUtc,
                input.Timestamps.AccountAsOfUtc,
                input.Timestamps.PositionAsOfUtc,
                input.Timestamps.OrderAsOfUtc,
                input.Timestamps.SymbolAsOfUtc,
                input.Timestamps.ConversionRateAsOfUtc
            ];
            AddSchemaRule(
                rules,
                "input_snapshot_times_valid",
                values.All(value => value is { Offset: var offset } && offset == TimeSpan.Zero));
        }

        if (input.Market is not null)
        {
            AddSchemaRule(
                rules,
                "input_market_values_valid",
                IsNonNegative(input.Market.SpreadPoints)
                && IsNonNegative(input.Market.RequestedSlippagePoints)
                && IsNonNegative(input.Market.BrokerMinimumStopDistancePoints)
                && input.Market.MarketSessionOpen is not null
                && input.Market.RequestedDirectionTradable is not null);
        }

        if (input.Account is not null)
        {
            AddSchemaRule(
                rules,
                "input_account_values_valid",
                Enum.IsDefined(input.Account.Environment)
                && Enum.IsDefined(input.Account.Mode)
                && IsNonNegative(input.Account.CurrentEquity)
                && input.Account.AutomatedTradingAllowed is not null
                && input.Account.UnexpectedExternalActivity is not null
                && input.Account.TargetOwnershipConfirmed is not null);
        }

        if (input.Exposure is not null)
        {
            AddSchemaRule(
                rules,
                "input_exposure_values_valid",
                IsNonNegative(input.Exposure.RequestedOrderVolume)
                && IsNonNegative(input.Exposure.ProjectedAccountPositionVolume)
                && IsNonNegative(input.Exposure.ProjectedAccountGrossNotional)
                && IsNonNegative(input.Exposure.ProjectedOpenPositionCount)
                && IsNonNegative(input.Exposure.ProjectedOpenOrderCount)
                && IsNonNegative(input.Exposure.OrdersAlreadySubmittedInWindow)
                && IsUtc(input.Exposure.OrderRateWindowStartedAtUtc)
                && IsUtc(input.Exposure.OrderRateSnapshotAsOfUtc)
                && input.Exposure.OrderRateWindowStartedAtUtc
                    <= input.Exposure.OrderRateSnapshotAsOfUtc
                && input.Exposure.OrderRateSnapshotAsOfUtc <= input.EvaluatedAtUtc);
        }

        if (input.Protection is not null)
        {
            AddSchemaRule(
                rules,
                "input_protection_values_valid",
                input.Protection.HasBrokerHostedStopLoss is not null
                && IsOptionalNonNegative(input.Protection.StopLossDistancePoints)
                && input.Protection.HasBrokerHostedTakeProfit is not null
                && IsOptionalNonNegative(input.Protection.TakeProfitDistancePoints)
                && input.Protection.RemovesExistingStopLoss is not null
                && input.Protection.WidensExistingStopLoss is not null);
        }

        if (input.RiskDayState is not null)
        {
            AddSchemaRule(
                rules,
                "input_risk_day_values_valid",
                input.RiskDayState.RiskDayKey is { Length: >= 1 and <= 300 }
                && IsUtc(input.RiskDayState.AsOfUtc)
                && IsNonNegative(input.RiskDayState.StartOfDayEquity)
                && IsNonNegative(input.RiskDayState.EquityHighWater)
                && IsNonNegative(input.RiskDayState.VerifiedDepositsSinceBaseline)
                && IsNonNegative(input.RiskDayState.VerifiedWithdrawalsSinceBaseline));
        }

        return rules;
    }

    private static NumericRiskDecision CreateDecision(
        EffectiveNumericRiskPolicy policy,
        NumericRiskEvaluationInput input,
        string inputDigest,
        string? riskDayKey,
        decimal? adjustedStart,
        decimal? adjustedHighWater,
        decimal? dailyLoss,
        decimal? drawdown,
        List<NumericRiskRuleResult> rules)
    {
        NumericRiskRuleResult[] frozenRules = rules.ToArray();
        NumericRiskDecisionDisposition disposition = frozenRules.Any(
            rule => rule.Outcome == RiskRuleOutcome.Failed)
            ? NumericRiskDecisionDisposition.Rejected
            : NumericRiskDecisionDisposition.Allowed;
        string decisionDigest = CanonicalJson.Sha256(new
        {
            Disposition = disposition.ToString(),
            ActionClass = input.ActionClass.ToString(),
            policy.Digest,
            InputDigest = inputDigest,
            RiskDayKey = riskDayKey,
            AdjustedStartOfDayEquity = adjustedStart,
            AdjustedEquityHighWater = adjustedHighWater,
            DailyLoss = dailyLoss,
            Drawdown = drawdown,
            Rules = frozenRules.Select(rule => new
            {
                rule.Code,
                Outcome = rule.Outcome.ToString(),
                rule.Observed,
                rule.Limit
            }).ToArray()
        });

        return new NumericRiskDecision(
            disposition,
            input.ActionClass,
            policy.Digest,
            inputDigest,
            decisionDigest,
            riskDayKey,
            adjustedStart,
            adjustedHighWater,
            dailyLoss,
            drawdown,
            Array.AsReadOnly(frozenRules));
    }

    private static NumericRiskRuleResult CheckCurrentPolicy(
        EffectiveNumericRiskPolicy policy,
        DateTimeOffset nowUtc) => Check(
            "policy_current",
            nowUtc >= policy.EffectiveFromUtc && nowUtc < policy.ExpiresAtUtc,
            nowUtc.ToString("O", CultureInfo.InvariantCulture),
            string.Create(
                CultureInfo.InvariantCulture,
                $"[{policy.EffectiveFromUtc:O},{policy.ExpiresAtUtc:O})"));

    private static NumericRiskRuleResult CheckFreshness(
        string code,
        DateTimeOffset evaluatedAtUtc,
        DateTimeOffset snapshotAtUtc,
        long maximumAgeMilliseconds)
    {
        double totalMilliseconds = (evaluatedAtUtc - snapshotAtUtc).TotalMilliseconds;
        bool integral = totalMilliseconds >= long.MinValue
            && totalMilliseconds <= long.MaxValue
            && totalMilliseconds == Math.Truncate(totalMilliseconds);
        long observed = integral ? (long)totalMilliseconds : long.MaxValue;
        return Check(
            code,
            integral && observed >= 0 && observed <= maximumAgeMilliseconds,
            integral ? Integer(observed) : "non_integral_or_out_of_range",
            Integer(maximumAgeMilliseconds));
    }

    private static NumericRiskRuleResult CheckStopLossDistance(
        EffectiveNumericRiskPolicy policy,
        MarketRiskSnapshot market,
        ProtectionRiskSnapshot protection)
    {
        decimal? observed = protection.StopLossDistancePoints;
        decimal minimum = market.BrokerMinimumStopDistancePoints!.Value;
        bool passed = observed is not null
            && observed.Value >= minimum
            && observed.Value <= policy.MaxStopLossDistancePoints;
        return Check(
            "stop_loss_distance",
            passed,
            observed is null ? "missing" : Decimal(observed.Value),
            $"[{Decimal(minimum)},{Decimal(policy.MaxStopLossDistancePoints)}]");
    }

    private static NumericRiskRuleResult CheckTakeProfitDistance(
        EffectiveNumericRiskPolicy policy,
        MarketRiskSnapshot market,
        ProtectionRiskSnapshot protection)
    {
        decimal? observed = protection.TakeProfitDistancePoints;
        decimal minimum = Math.Max(
            market.BrokerMinimumStopDistancePoints!.Value,
            policy.MinTakeProfitDistancePoints);
        return Check(
            "take_profit_distance",
            observed is not null && observed.Value >= minimum,
            observed is null ? "missing" : Decimal(observed.Value),
            $"[{Decimal(minimum)},infinity)");
    }

    private static NumericRiskRuleResult CheckMaximum(string code, decimal observed, decimal limit) =>
        Check(code, observed <= limit, Decimal(observed), Decimal(limit));

    private static NumericRiskRuleResult CheckMaximum(string code, int observed, int limit) =>
        Check(code, observed <= limit, Integer(observed), Integer(limit));

    private static NumericRiskRuleResult CheckMaximum(string code, long observed, long limit) =>
        Check(code, observed <= limit, Integer(observed), Integer(limit));

    private static bool TryCalculateRiskMetrics(
        AccountRiskSnapshot account,
        RiskDayStateSnapshot riskState,
        out decimal adjustedStart,
        out decimal adjustedHighWater,
        out decimal dailyLoss,
        out decimal drawdown)
    {
        adjustedStart = 0m;
        adjustedHighWater = 0m;
        dailyLoss = 0m;
        drawdown = 0m;
        try
        {
            decimal cashFlowAdjustment = checked(
                riskState.VerifiedDepositsSinceBaseline!.Value
                - riskState.VerifiedWithdrawalsSinceBaseline!.Value);
            adjustedStart = checked(riskState.StartOfDayEquity!.Value + cashFlowAdjustment);
            adjustedHighWater = checked(riskState.EquityHighWater!.Value + cashFlowAdjustment);
            decimal currentEquity = account.CurrentEquity!.Value;
            dailyLoss = adjustedStart > currentEquity
                ? checked(adjustedStart - currentEquity)
                : 0m;
            drawdown = adjustedHighWater > currentEquity
                ? checked(adjustedHighWater - currentEquity)
                : 0m;
            return true;
        }
        catch (OverflowException)
        {
            adjustedStart = 0m;
            adjustedHighWater = 0m;
            dailyLoss = 0m;
            drawdown = 0m;
            return false;
        }
    }

    private static NumericRiskRuleResult Check(
        string code,
        bool passed,
        string? observed,
        string? limit) => new(
            code,
            passed ? RiskRuleOutcome.Passed : RiskRuleOutcome.Failed,
            observed,
            limit);

    private static NumericRiskRuleResult Skip(string code) =>
        new(code, RiskRuleOutcome.NotApplicable, NotApplicable, NotApplicable);

    private static void AddSchemaRule(
        List<NumericRiskRuleResult> rules,
        string code,
        bool passed) => rules.Add(Check(code, passed, Boolean(passed), Boolean(true)));

    private static bool IsNonNegative(decimal? value) => value is >= 0;

    private static bool IsNonNegative(int? value) => value is >= 0;

    private static bool IsOptionalNonNegative(decimal? value) => value is null or >= 0;

    private static bool IsUtc(DateTimeOffset? value) =>
        value is { Offset: var offset } && offset == TimeSpan.Zero;

    private static string Boolean(bool value) => value ? "true" : "false";

    private static string Decimal(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Integer(long value) => value.ToString(CultureInfo.InvariantCulture);
}
