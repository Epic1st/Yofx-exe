using YO4X.Risk;

namespace YO4X.Domain.Tests;

public sealed class NumericRiskEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SafeDemoIncreaseIsAllowedAndByteReplayDigestIsStable()
    {
        EffectiveNumericRiskPolicy policy = CreatePolicy();
        NumericRiskEvaluationInput input = CreateInput(policy);

        NumericRiskDecision first = NumericRiskEvaluator.Evaluate(policy, input);
        NumericRiskDecision replay = NumericRiskEvaluator.Evaluate(policy, input);

        Assert.True(first.IsAllowed);
        Assert.Equal(first.InputDigest, replay.InputDigest);
        Assert.Equal(first.DecisionDigest, replay.DecisionDigest);
        Assert.Equal(first.Rules, replay.Rules);
    }

    [Fact]
    public void NumericExposureCapsAllowEqualityAndRejectAnyExcess()
    {
        EffectiveNumericRiskPolicy policy = CreatePolicy();
        NumericRiskEvaluationInput baseline = CreateInput(policy);
        ExposureRiskSnapshot exact = baseline.Exposure! with
        {
            RequestedOrderVolume = policy.MaxPerOrderVolume,
            ProjectedAccountPositionVolume = policy.MaxAccountPositionVolume,
            ProjectedAccountGrossNotional = policy.MaxAccountGrossNotional,
            ProjectedOpenPositionCount = policy.MaxOpenPositions,
            ProjectedOpenOrderCount = policy.MaxOpenOrders
        };

        Assert.True(NumericRiskEvaluator.Evaluate(policy, baseline with { Exposure = exact }).IsAllowed);

        (string Code, ExposureRiskSnapshot Exposure)[] excesses =
        [
            ("per_order_volume_limit", exact with { RequestedOrderVolume = policy.MaxPerOrderVolume + 0.00000001m }),
            ("account_position_volume_limit", exact with { ProjectedAccountPositionVolume = policy.MaxAccountPositionVolume + 0.00000001m }),
            ("account_gross_notional_limit", exact with { ProjectedAccountGrossNotional = policy.MaxAccountGrossNotional + 0.01m }),
            ("open_position_count_limit", exact with { ProjectedOpenPositionCount = policy.MaxOpenPositions + 1 }),
            ("open_order_count_limit", exact with { ProjectedOpenOrderCount = policy.MaxOpenOrders + 1 })
        ];

        foreach ((string code, ExposureRiskSnapshot exposure) in excesses)
        {
            AssertFailed(policy, baseline with { Exposure = exposure }, code);
        }
    }

    [Fact]
    public void DailyLossUsesCashFlowAdjustedEquityAndHasAnInclusiveBoundary()
    {
        EffectiveNumericRiskPolicy policy = CreatePolicy();
        NumericRiskEvaluationInput baseline = CreateInput(policy);
        // Adjusted start = 10,200 + 100 verified deposits - 50 verified withdrawals = 10,250.
        NumericRiskEvaluationInput exact = baseline with
        {
            Account = baseline.Account! with { CurrentEquity = 9_750m }
        };
        NumericRiskDecision exactDecision = NumericRiskEvaluator.Evaluate(policy, exact);

        Assert.True(exactDecision.IsAllowed);
        Assert.Equal(10_250m, exactDecision.AdjustedStartOfDayEquity);
        Assert.Equal(500m, exactDecision.DailyLoss);

        AssertFailed(
            policy,
            exact with { Account = exact.Account! with { CurrentEquity = 9_749.99m } },
            "daily_loss_limit");
    }

    [Fact]
    public void DrawdownUsesDurableCashFlowAdjustedHighWaterAndHasAnInclusiveBoundary()
    {
        EffectiveNumericRiskPolicy policy = CreatePolicy();
        NumericRiskEvaluationInput baseline = CreateInput(policy);
        RiskDayStateSnapshot state = baseline.RiskDayState! with
        {
            StartOfDayEquity = 9_000m,
            EquityHighWater = 10_500m,
            VerifiedDepositsSinceBaseline = 100m,
            VerifiedWithdrawalsSinceBaseline = 50m
        };
        NumericRiskEvaluationInput exact = baseline with
        {
            Account = baseline.Account! with { CurrentEquity = 9_550m },
            RiskDayState = state
        };
        NumericRiskDecision exactDecision = NumericRiskEvaluator.Evaluate(policy, exact);

        Assert.True(exactDecision.IsAllowed);
        Assert.Equal(10_550m, exactDecision.AdjustedEquityHighWater);
        Assert.Equal(1_000m, exactDecision.Drawdown);

        AssertFailed(
            policy,
            exact with { Account = exact.Account! with { CurrentEquity = 9_549.99m } },
            "drawdown_limit");
    }

    [Fact]
    public void RiskDayBoundaryIsDeterministicOnBothSidesOfRollover()
    {
        NumericRiskPolicyContent content = NumericRiskPolicyTests.CreateContent() with
        {
            RiskDay = new RiskDayDefinition("UTC", "tzdb-test-v1", new TimeOnly(17, 0))
        };
        EffectiveNumericRiskPolicy policy = CreatePolicy(content);
        DateTimeOffset before = new(2026, 8, 22, 16, 59, 59, TimeSpan.Zero);
        DateTimeOffset at = new(2026, 8, 22, 17, 0, 0, TimeSpan.Zero);

        string beforeKey = NumericRiskEvaluator.CalculateRiskDayKey(policy, before);
        string atKey = NumericRiskEvaluator.CalculateRiskDayKey(policy, at);

        Assert.Contains("2026-08-21", beforeKey, StringComparison.Ordinal);
        Assert.Contains("2026-08-22", atKey, StringComparison.Ordinal);
        NumericRiskEvaluationInput wrongState = CreateInput(policy) with
        {
            RiskDayState = CreateInput(policy).RiskDayState! with { RiskDayKey = atKey }
        };
        AssertFailed(policy, wrongState, "risk_day_boundary");
    }

    [Fact]
    public void EveryRequiredSnapshotAllowsExactFreshnessAndRejectsOneMillisecondOver()
    {
        EffectiveNumericRiskPolicy policy = CreatePolicy();
        NumericRiskEvaluationInput baseline = CreateInput(policy);
        DateTimeOffset exact = Now.AddMilliseconds(-policy.IncreaseFreshness.QuoteMaxAgeMilliseconds);
        RiskSnapshotTimestamps allExact = new(exact, exact, exact, exact, exact, exact);
        NumericRiskEvaluationInput exactInput = baseline with
        {
            Timestamps = allExact,
            RiskDayState = baseline.RiskDayState! with { AsOfUtc = exact }
        };
        Assert.True(NumericRiskEvaluator.Evaluate(policy, exactInput).IsAllowed);

        (string Code, RiskSnapshotTimestamps Timestamps)[] stale =
        [
            ("quote_freshness", allExact with { QuoteAsOfUtc = exact.AddMilliseconds(-1) }),
            ("account_freshness", allExact with { AccountAsOfUtc = exact.AddMilliseconds(-1) }),
            ("position_freshness", allExact with { PositionAsOfUtc = exact.AddMilliseconds(-1) }),
            ("order_freshness", allExact with { OrderAsOfUtc = exact.AddMilliseconds(-1) }),
            ("symbol_freshness", allExact with { SymbolAsOfUtc = exact.AddMilliseconds(-1) }),
            ("conversion_rate_freshness", allExact with { ConversionRateAsOfUtc = exact.AddMilliseconds(-1) })
        ];

        foreach ((string code, RiskSnapshotTimestamps timestamps) in stale)
        {
            AssertFailed(policy, baseline with { Timestamps = timestamps }, code);
        }

        AssertFailed(
            policy,
            baseline with
            {
                RiskDayState = baseline.RiskDayState! with { AsOfUtc = exact.AddMilliseconds(-1) }
            },
            "risk_state_freshness");
    }

    [Fact]
    public void FutureDatedSnapshotsFailFreshnessInsteadOfAppearingFresh()
    {
        EffectiveNumericRiskPolicy policy = CreatePolicy();
        NumericRiskEvaluationInput baseline = CreateInput(policy);
        RiskSnapshotTimestamps future = baseline.Timestamps! with
        {
            QuoteAsOfUtc = Now.AddMilliseconds(1)
        };

        AssertFailed(policy, baseline with { Timestamps = future }, "quote_freshness");
    }

    [Fact]
    public void SpreadAndSlippageLimitsAreInclusiveAndFailAboveLimit()
    {
        EffectiveNumericRiskPolicy policy = CreatePolicy();
        NumericRiskEvaluationInput baseline = CreateInput(policy);
        NumericRiskEvaluationInput exact = baseline with
        {
            Market = baseline.Market! with
            {
                SpreadPoints = policy.MaxSpreadPoints,
                RequestedSlippagePoints = policy.MaxSlippagePoints
            }
        };
        Assert.True(NumericRiskEvaluator.Evaluate(policy, exact).IsAllowed);

        AssertFailed(
            policy,
            exact with { Market = exact.Market! with { SpreadPoints = policy.MaxSpreadPoints + 0.01m } },
            "spread_limit");
        AssertFailed(
            policy,
            exact with { Market = exact.Market! with { RequestedSlippagePoints = policy.MaxSlippagePoints + 0.01m } },
            "slippage_limit");
    }

    [Fact]
    public void OrderRateUsesExactWindowAndCountsTheCandidateOrder()
    {
        EffectiveNumericRiskPolicy policy = CreatePolicy();
        NumericRiskEvaluationInput baseline = CreateInput(policy);
        ExposureRiskSnapshot exact = baseline.Exposure! with
        {
            OrdersAlreadySubmittedInWindow = policy.MaxOrdersPerWindow - 1
        };
        Assert.True(NumericRiskEvaluator.Evaluate(policy, baseline with { Exposure = exact }).IsAllowed);

        AssertFailed(
            policy,
            baseline with
            {
                Exposure = exact with { OrdersAlreadySubmittedInWindow = policy.MaxOrdersPerWindow }
            },
            "order_rate_limit");
        AssertFailed(
            policy,
            baseline with
            {
                Exposure = exact with { OrderRateWindowStartedAtUtc = Now.AddSeconds(-30) }
            },
            "order_rate_window_exact");
    }

    [Fact]
    public void NewExposureRequiresBrokerHostedStopAndTakeProfitWithinBounds()
    {
        EffectiveNumericRiskPolicy policy = CreatePolicy();
        NumericRiskEvaluationInput baseline = CreateInput(policy);
        decimal brokerMinimum = baseline.Market!.BrokerMinimumStopDistancePoints!.Value;

        AssertFailed(
            policy,
            baseline with
            {
                Protection = baseline.Protection! with { HasBrokerHostedStopLoss = false }
            },
            "broker_hosted_stop_loss");
        AssertFailed(
            policy,
            baseline with
            {
                Protection = baseline.Protection! with { StopLossDistancePoints = brokerMinimum - 0.01m }
            },
            "stop_loss_distance");
        AssertFailed(
            policy,
            baseline with
            {
                Protection = baseline.Protection! with
                {
                    StopLossDistancePoints = policy.MaxStopLossDistancePoints + 0.01m
                }
            },
            "stop_loss_distance");
        AssertFailed(
            policy,
            baseline with
            {
                Protection = baseline.Protection! with { HasBrokerHostedTakeProfit = false }
            },
            "broker_hosted_take_profit");
        AssertFailed(
            policy,
            baseline with
            {
                Protection = baseline.Protection! with
                {
                    TakeProfitDistancePoints = policy.MinTakeProfitDistancePoints - 0.01m
                }
            },
            "take_profit_distance");
    }

    [Theory]
    [InlineData(true, false, "stop_loss_not_removed")]
    [InlineData(false, true, "stop_loss_not_widened")]
    public void ExistingStopCanNeverBeRemovedOrWidened(
        bool removes,
        bool widens,
        string reasonCode)
    {
        EffectiveNumericRiskPolicy policy = CreatePolicy();
        NumericRiskEvaluationInput baseline = CreateInput(policy);

        AssertFailed(
            policy,
            baseline with
            {
                ActionClass = RiskActionClass.Protection,
                Protection = baseline.Protection! with
                {
                    RemovesExistingStopLoss = removes,
                    WidensExistingStopLoss = widens
                }
            },
            reasonCode);
    }

    [Fact]
    public void ExternalActivityBlocksIncreaseButDoesNotInventOwnershipOfReduction()
    {
        EffectiveNumericRiskPolicy policy = CreatePolicy();
        NumericRiskEvaluationInput baseline = CreateInput(policy);
        NumericRiskEvaluationInput external = baseline with
        {
            Account = baseline.Account! with { UnexpectedExternalActivity = true }
        };

        AssertFailed(policy, external, "unexpected_external_activity");
        Assert.True(NumericRiskEvaluator.Evaluate(
            policy,
            external with { ActionClass = RiskActionClass.ExposureReduction }).IsAllowed);
        AssertFailed(
            policy,
            external with
            {
                ActionClass = RiskActionClass.ExposureReduction,
                Account = external.Account! with { TargetOwnershipConfirmed = false }
            },
            "target_ownership_confirmed");
    }

    [Theory]
    [InlineData(BrokerAccountEnvironment.Live, BrokerAccountMode.Hedging, "demo_account_only")]
    [InlineData(BrokerAccountEnvironment.Unknown, BrokerAccountMode.Hedging, "demo_account_only")]
    [InlineData(BrokerAccountEnvironment.Demo, BrokerAccountMode.Netting, "hedging_account_only")]
    public void NonDemoOrNonHedgingAccountsAreAlwaysRejected(
        BrokerAccountEnvironment environment,
        BrokerAccountMode mode,
        string reasonCode)
    {
        EffectiveNumericRiskPolicy policy = CreatePolicy();
        NumericRiskEvaluationInput baseline = CreateInput(policy);

        AssertFailed(
            policy,
            baseline with { Account = baseline.Account! with { Environment = environment, Mode = mode } },
            reasonCode);
    }

    [Fact]
    public void MissingOrInvalidRuntimeInputReturnsAHashedFailClosedDecision()
    {
        EffectiveNumericRiskPolicy policy = CreatePolicy();
        NumericRiskEvaluationInput invalid = CreateInput(policy) with
        {
            Market = null,
            Account = null,
            Exposure = null
        };

        NumericRiskDecision decision = NumericRiskEvaluator.Evaluate(policy, invalid);

        Assert.False(decision.IsAllowed);
        Assert.NotEmpty(decision.InputDigest);
        Assert.NotEmpty(decision.DecisionDigest);
        Assert.Contains(decision.Rules, rule =>
            rule.Code == "input_market_present" && rule.Outcome == RiskRuleOutcome.Failed);
    }

    [Fact]
    public void ExpiredEffectivePolicyIsRejectedAtDecisionTime()
    {
        EffectiveNumericRiskPolicy policy = CreatePolicy();
        NumericRiskEvaluationInput input = CreateInput(policy) with
        {
            EvaluatedAtUtc = policy.ExpiresAtUtc
        };

        AssertFailed(policy, input, "policy_current");
    }

    [Fact]
    public void ExtremeNumericInputFailsClosedWithoutArithmeticExceptions()
    {
        EffectiveNumericRiskPolicy policy = CreatePolicy();
        NumericRiskEvaluationInput baseline = CreateInput(policy);
        NumericRiskEvaluationInput overflow = baseline with
        {
            Account = baseline.Account! with { CurrentEquity = decimal.MaxValue },
            RiskDayState = baseline.RiskDayState! with
            {
                StartOfDayEquity = decimal.MaxValue,
                EquityHighWater = decimal.MaxValue,
                VerifiedDepositsSinceBaseline = decimal.MaxValue,
                VerifiedWithdrawalsSinceBaseline = 0m
            },
            Exposure = baseline.Exposure! with { OrdersAlreadySubmittedInWindow = int.MaxValue }
        };

        AssertFailed(policy, overflow, "risk_arithmetic_valid");
    }

    [Fact]
    public void RandomizedLimitBoundariesNeverAllowAValueAboveThePolicyMaximum()
    {
        EffectiveNumericRiskPolicy policy = CreatePolicy();
        NumericRiskEvaluationInput baseline = CreateInput(policy);
        var random = new Random(20260822);

        for (int index = 0; index < 500; index++)
        {
            decimal delta = random.Next(1, 10_000) / 10_000m;
            decimal observed = index % 2 == 0
                ? policy.MaxPerOrderVolume - Math.Min(policy.MaxPerOrderVolume, delta)
                : policy.MaxPerOrderVolume + delta;
            NumericRiskEvaluationInput candidate = baseline with
            {
                Exposure = baseline.Exposure! with { RequestedOrderVolume = observed }
            };
            NumericRiskDecision decision = NumericRiskEvaluator.Evaluate(policy, candidate);

            Assert.Equal(observed <= policy.MaxPerOrderVolume, decision.IsAllowed);
        }
    }

    private static EffectiveNumericRiskPolicy CreatePolicy(NumericRiskPolicyContent? content = null)
    {
        VerifiedNumericRiskPolicy verified = NumericRiskPolicyTests.Verify(
            NumericRiskPolicyTests.CreateDescriptor(content: content));
        return EffectiveNumericRiskPolicy.Meet([verified], Now);
    }

    private static NumericRiskEvaluationInput CreateInput(EffectiveNumericRiskPolicy policy)
    {
        DateTimeOffset snapshot = Now.AddMilliseconds(-500);
        string riskDayKey = NumericRiskEvaluator.CalculateRiskDayKey(policy, Now);
        return new NumericRiskEvaluationInput(
            Now,
            RiskActionClass.ExposureIncrease,
            new RiskSnapshotTimestamps(snapshot, snapshot, snapshot, snapshot, snapshot, snapshot),
            new MarketRiskSnapshot(
                SpreadPoints: 10m,
                RequestedSlippagePoints: 5m,
                MarketSessionOpen: true,
                RequestedDirectionTradable: true,
                BrokerMinimumStopDistancePoints: 20m),
            new AccountRiskSnapshot(
                BrokerAccountEnvironment.Demo,
                BrokerAccountMode.Hedging,
                CurrentEquity: 10_000m,
                AutomatedTradingAllowed: true,
                UnexpectedExternalActivity: false,
                TargetOwnershipConfirmed: true),
            new ExposureRiskSnapshot(
                RequestedOrderVolume: 1m,
                ProjectedAccountPositionVolume: 5m,
                ProjectedAccountGrossNotional: 50_000m,
                ProjectedOpenPositionCount: 2,
                ProjectedOpenOrderCount: 2,
                OrdersAlreadySubmittedInWindow: 2,
                OrderRateWindowStartedAtUtc: Now.AddMilliseconds(-policy.OrderRateWindowMilliseconds),
                OrderRateSnapshotAsOfUtc: Now),
            new ProtectionRiskSnapshot(
                HasBrokerHostedStopLoss: true,
                StopLossDistancePoints: 100m,
                HasBrokerHostedTakeProfit: true,
                TakeProfitDistancePoints: 150m,
                RemovesExistingStopLoss: false,
                WidensExistingStopLoss: false),
            new RiskDayStateSnapshot(
                riskDayKey,
                snapshot,
                StartOfDayEquity: 10_200m,
                EquityHighWater: 10_500m,
                VerifiedDepositsSinceBaseline: 100m,
                VerifiedWithdrawalsSinceBaseline: 50m));
    }

    private static void AssertFailed(
        EffectiveNumericRiskPolicy policy,
        NumericRiskEvaluationInput input,
        string reasonCode)
    {
        NumericRiskDecision decision = NumericRiskEvaluator.Evaluate(policy, input);
        Assert.False(decision.IsAllowed);
        Assert.Contains(decision.Rules, rule =>
            rule.Code == reasonCode && rule.Outcome == RiskRuleOutcome.Failed);
    }
}
