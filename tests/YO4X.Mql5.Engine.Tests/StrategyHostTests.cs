using YO4X.Mql5.Engine.Context;
using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Hosting;
using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Tests;

/// <summary>The run loop end to end, including the runaway-strategy guards.</summary>
public sealed class StrategyHostTests
{
    private static readonly int[] ExpectedBarCounts = [1, 2, 3, 4, 5, 6];

    /// <summary>
    /// The end-to-end proof: a strategy that buys on bar ten and closes on bar twenty must produce
    /// exactly one round trip, at exactly the prices the spread and the bar series imply.
    /// </summary>
    [Fact]
    public void ScriptedBuyOnBarTenAndCloseOnBarTwentyProducesTheExpectedTradeSequence()
    {
        List<Mql5Bar> bars = EngineTestSupport.Ramp(25);
        var feed = new ListMarketFeed("EURUSD", bars);
        Mql5RunOptions options = EngineTestSupport.Options();

        long ticket = 0;
        var strategy = new ScriptedStrategy((context, tick) =>
        {
            if (tick == 10)
            {
                Assert.True(context.OrderSend(EngineTestSupport.Market(Mql5OrderType.Buy, 0.10), out Mql5TradeResult opened));
                ticket = opened.Position;
            }
            else if (tick == 20)
            {
                Assert.True(context.OrderSend(EngineTestSupport.ClosePosition(ticket), out Mql5TradeResult closed));
                Assert.Equal(Mql5TradeRetcode.Done, closed.Retcode);
            }
        });

        Mql5RunReport report = Mql5StrategyHost.Run(strategy, feed, options);

        Assert.Equal(1, strategy.InitCalls);
        Assert.Equal(1, strategy.DeinitCalls);
        Assert.Equal(Mql5DeinitReason.Program, strategy.LastDeinitReason);
        Assert.Equal(Mql5InitCode.Succeeded, report.InitRetcode);
        Assert.Equal(25, report.TicksProcessed);
        Assert.Equal(25, report.BarsSeen);
        Assert.Empty(report.StrategyFault);
        Assert.True(report.CompletedCleanly);

        // Exactly one round trip.
        Mql5ClosedTrade trade = Assert.Single(report.Trades);
        Assert.Equal(Mql5PositionType.Buy, trade.Type);
        Assert.Equal(0.10, trade.Volume, 6);
        Assert.Equal(bars[10].Time, trade.TimeOpen);
        Assert.Equal(bars[20].Time, trade.TimeClose);
        Assert.Equal(1.10110, trade.PriceOpen, 5);    // bar 10 closes at 1.10100, plus ten points of spread
        Assert.Equal(1.10200, trade.PriceClose, 5);   // bar 20 closes at 1.10200, sold at the bid
        Assert.Equal(Mql5CloseReason.Strategy, trade.Reason);

        // (1.10200 - 1.10110) * 100000 * 0.10 = 9.00
        Assert.Equal(9.00, trade.GrossProfit, 2);
        Assert.Equal(0.00, trade.Commission, 2);
        Assert.Equal(9.00, trade.NetProfit, 2);

        Assert.Equal(10_000.00, report.InitialDeposit, 2);
        Assert.Equal(10_009.00, report.FinalBalance, 2);
        Assert.Equal(10_009.00, report.FinalEquity, 2);
        Assert.Equal(1, report.TotalTrades);
        Assert.Equal(1, report.WinningTrades);
        Assert.Equal(0, report.LosingTrades);
        Assert.Equal(9.00, report.GrossProfit, 2);
        Assert.Equal(0.00, report.GrossLoss, 2);
        Assert.True(double.IsPositiveInfinity(report.ProfitFactor));

        // The only dip below the starting balance is the spread paid on entry.
        Assert.Equal(1.00, report.MaxDrawdown, 2);
        Assert.Equal(0.01, report.MaxDrawdownPercent, 4);

        // The journal carries the whole sequence with its timestamps.
        Assert.Equal(2, report.Events.Count);

        Mql5OrderEvent opened2 = report.Events[0];
        Assert.Equal(Mql5OrderEventKind.PositionOpened, opened2.Kind);
        Assert.Equal(bars[10].Time, opened2.Time);
        Assert.Equal(Mql5OrderType.Buy, opened2.Type);
        Assert.Equal(0.10, opened2.Volume, 6);
        Assert.Equal(1.10110, opened2.Price, 5);
        Assert.Equal(10_000.00, opened2.Balance, 2);

        Mql5OrderEvent closedEvent = report.Events[1];
        Assert.Equal(Mql5OrderEventKind.PositionClosed, closedEvent.Kind);
        Assert.Equal(bars[20].Time, closedEvent.Time);
        Assert.Equal(1.10200, closedEvent.Price, 5);
        Assert.Equal(9.00, closedEvent.Profit, 2);
        Assert.Equal(10_009.00, closedEvent.Balance, 2);
        Assert.Equal(opened2.Ticket, closedEvent.Ticket);

        // Equity is sampled once per tick and ends on the realized balance.
        Assert.Equal(25, report.EquityCurve.Count);
        Assert.Equal(10_000.00, report.EquityCurve[9], 2);
        Assert.Equal(9_999.00, report.EquityCurve[10], 2);
        Assert.Equal(10_009.00, report.EquityCurve[24], 2);
    }

    [Fact]
    public void TheStrategySeesTheBarSeriesGrowByOneEveryTick()
    {
        List<Mql5Bar> bars = EngineTestSupport.Ramp(6);
        var seen = new List<int>();
        var strategy = new ScriptedStrategy((context, _) =>
        {
            var concrete = (Mql5MarketContext)context;
            seen.Add(concrete.BarCount);
        });

        Mql5StrategyHost.Run(strategy, new ListMarketFeed("EURUSD", bars), EngineTestSupport.Options());

        Assert.Equal(ExpectedBarCounts, seen);
    }

    [Fact]
    public void StopsAreEvaluatedBetweenTicksWithoutAnyStrategyInvolvement()
    {
        var bars = new List<Mql5Bar>
        {
            EngineTestSupport.Flat(0, 1.10000),
            EngineTestSupport.Bar(1, 1.10000, 1.10050, 1.09850, 1.09900),
            EngineTestSupport.Flat(2, 1.09900),
        };

        var strategy = new ScriptedStrategy((context, tick) =>
        {
            if (tick == 0)
            {
                context.OrderSend(EngineTestSupport.Market(Mql5OrderType.Buy, 0.10, sl: 1.09900), out _);
            }
        });

        Mql5RunReport report = Mql5StrategyHost.Run(
            strategy,
            new ListMarketFeed("EURUSD", bars),
            EngineTestSupport.Options());

        Mql5ClosedTrade trade = Assert.Single(report.Trades);
        Assert.Equal(Mql5CloseReason.StopLoss, trade.Reason);
        Assert.Equal(bars[1].Time, trade.TimeClose);
        Assert.Equal(-11.00, trade.GrossProfit, 2);
        Assert.Equal(9_989.00, report.FinalBalance, 2);
        Assert.Equal(0, report.WinningTrades);
        Assert.Equal(1, report.LosingTrades);
        Assert.Equal(0.0, report.ProfitFactor);
    }

    [Fact]
    public void OpenPositionsAreClosedWhenTheFeedRunsOut()
    {
        List<Mql5Bar> bars = EngineTestSupport.Ramp(5);
        var strategy = new ScriptedStrategy((context, tick) =>
        {
            if (tick == 0)
            {
                context.OrderSend(EngineTestSupport.Market(Mql5OrderType.Buy, 0.10), out _);
            }
        });

        Mql5RunReport report = Mql5StrategyHost.Run(
            strategy,
            new ListMarketFeed("EURUSD", bars),
            EngineTestSupport.Options());

        Mql5ClosedTrade trade = Assert.Single(report.Trades);
        Assert.Equal(Mql5CloseReason.EndOfRun, trade.Reason);
        Assert.Equal(report.FinalBalance, report.FinalEquity, 2);
    }

    [Fact]
    public void AFailedInitializationSkipsEveryTick()
    {
        var strategy = new FailingInitStrategy();
        Mql5RunReport report = Mql5StrategyHost.Run(
            strategy,
            new ListMarketFeed("EURUSD", EngineTestSupport.Ramp(10)),
            EngineTestSupport.Options());

        Assert.Equal(Mql5InitCode.ParametersIncorrect, report.InitRetcode);
        Assert.Equal(0, report.TicksProcessed);
        Assert.Equal(0, strategy.TickCalls);
        Assert.Equal(Mql5DeinitReason.InitFailed, strategy.LastDeinitReason);
        Assert.Contains(report.Events, e => e.Kind == Mql5OrderEventKind.InitFailed);
    }

    [Fact]
    public void TheTotalTickCapStopsTheRunAndIsReported()
    {
        Mql5RunOptions options = EngineTestSupport.Options(maxTicks: 3);
        var strategy = new ScriptedStrategy((_, _) => { });

        Mql5RunReport report = Mql5StrategyHost.Run(
            strategy,
            new ListMarketFeed("EURUSD", EngineTestSupport.Ramp(10)),
            options);

        Assert.Equal(3, report.TicksProcessed);
        Assert.True(report.TickCapTriggered);
        Assert.False(report.CompletedCleanly);
        Assert.Contains(report.Events, e => e.Kind == Mql5OrderEventKind.TickCapReached);
        Assert.Equal(Mql5DeinitReason.Close, strategy.LastDeinitReason);
    }

    [Fact]
    public void ARunawayStrategyIsCappedPerTickAndTheRunStillCompletes()
    {
        Mql5RunOptions options = EngineTestSupport.Options(Mql5MarginMode.Hedging, maxOrdersPerTick: 2);
        var accepted = 0;
        var rejected = 0;

        var strategy = new ScriptedStrategy((context, _) =>
        {
            for (int index = 0; index < 20; index++)
            {
                if (context.OrderSend(EngineTestSupport.Market(Mql5OrderType.Buy, 0.01), out Mql5TradeResult result))
                {
                    accepted++;
                }
                else
                {
                    rejected++;
                    Assert.Equal(Mql5TradeRetcode.LimitOrders, result.Retcode);
                }
            }
        });

        Mql5RunReport report = Mql5StrategyHost.Run(
            strategy,
            new ListMarketFeed("EURUSD", EngineTestSupport.Ramp(3)),
            options);

        Assert.Equal(6, accepted);      // two per tick over three ticks
        Assert.Equal(54, rejected);
        Assert.True(report.OrdersPerTickCapTriggered);
        Assert.Equal(3, report.Events.Count(e => e.Kind == Mql5OrderEventKind.OrdersPerTickCapReached));
    }

    [Fact]
    public void AThrowingStrategyStopsTheRunWithoutEscapingTheHost()
    {
        var strategy = new ScriptedStrategy((_, tick) =>
        {
            if (tick == 4)
            {
                throw new InvalidOperationException("strategy blew up");
            }
        });

        Mql5RunReport report = Mql5StrategyHost.Run(
            strategy,
            new ListMarketFeed("EURUSD", EngineTestSupport.Ramp(10)),
            EngineTestSupport.Options());

        Assert.Equal(4, report.TicksProcessed);
        Assert.Contains("strategy blew up", report.StrategyFault, StringComparison.Ordinal);
        Assert.Contains(report.Events, e => e.Kind == Mql5OrderEventKind.StrategyFault);
        Assert.Equal(1, strategy.DeinitCalls);
    }

    [Fact]
    public void ProfitFactorIsGrossProfitOverGrossLoss()
    {
        // Bar 0 flat, bar 1 up 100 points, bar 2 down 200 points, bar 3 flat.
        var bars = new List<Mql5Bar>
        {
            EngineTestSupport.Flat(0, 1.10000),
            EngineTestSupport.Flat(1, 1.10100),
            EngineTestSupport.Flat(2, 1.09900),
            EngineTestSupport.Flat(3, 1.09900),
        };

        long ticket = 0;
        var strategy = new ScriptedStrategy((context, tick) =>
        {
            switch (tick)
            {
                case 0:
                    context.OrderSend(EngineTestSupport.Market(Mql5OrderType.Buy, 0.10), out Mql5TradeResult first);
                    ticket = first.Position;
                    break;
                case 1:
                    context.OrderSend(EngineTestSupport.ClosePosition(ticket), out _);
                    context.OrderSend(EngineTestSupport.Market(Mql5OrderType.Buy, 0.10), out Mql5TradeResult second);
                    ticket = second.Position;
                    break;
                case 2:
                    context.OrderSend(EngineTestSupport.ClosePosition(ticket), out _);
                    break;
                default:
                    break;
            }
        });

        Mql5RunReport report = Mql5StrategyHost.Run(
            strategy,
            new ListMarketFeed("EURUSD", bars),
            EngineTestSupport.Options());

        // Trade one: bought 1.10010, sold 1.10100 -> +9.00
        // Trade two: bought 1.10110, sold 1.09900 -> -21.00
        Assert.Equal(2, report.TotalTrades);
        Assert.Equal(9.00, report.GrossProfit, 2);
        Assert.Equal(21.00, report.GrossLoss, 2);
        Assert.Equal(9.00 / 21.00, report.ProfitFactor, 6);
        Assert.Equal(9_988.00, report.FinalBalance, 2);
    }

    [Fact]
    public void IdenticalFeedAndSeedProduceAnIdenticalTradeSequence()
    {
        var feed = new Mql5SyntheticMarketFeed("EURUSD", seed: 0xC0FFEE, barCount: 400);
        Mql5RunOptions options = EngineTestSupport.Options();

        Mql5RunReport first = Mql5StrategyHost.Run(new CrossoverStrategy(), feed, options);
        Mql5RunReport second = Mql5StrategyHost.Run(new CrossoverStrategy(), feed, options);

        Assert.True(first.TotalTrades > 0, "the reference strategy should have traded at least once");
        Assert.Equal(first.FinalBalance, second.FinalBalance, 6);
        Assert.Equal(first.MaxDrawdown, second.MaxDrawdown, 6);
        Assert.Equal(first.Events.Count, second.Events.Count);
        Assert.Equal(
            first.Events.Select(e => e.ToString()).ToArray(),
            second.Events.Select(e => e.ToString()).ToArray());
    }

    [Fact]
    public void TheEngineAssemblyLinksNoNetworkingOrInteropSurface()
    {
        string[] forbidden = ["System.Net", "System.IO.Pipes"];
        string[] referenced = [.. typeof(Mql5SimulatedBroker).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)];

        foreach (string name in referenced)
        {
            Assert.DoesNotContain(forbidden, prefix => name.StartsWith(prefix, StringComparison.Ordinal));
        }
    }
}

/// <summary>A strategy whose initialization refuses to start.</summary>
internal sealed class FailingInitStrategy : IMql5Strategy
{
    public int TickCalls { get; private set; }

    public int LastDeinitReason { get; private set; } = -1;

    public int OnInit(IMql5MarketContext context) => Mql5InitCode.ParametersIncorrect;

    public void OnTick(IMql5MarketContext context) => TickCalls++;

    public void OnDeinit(IMql5MarketContext context, int reason) => LastDeinitReason = reason;
}

/// <summary>A small but genuine indicator-driven strategy, used to check reproducibility.</summary>
internal sealed class CrossoverStrategy : IMql5Strategy
{
    private int fastHandle = -1;
    private int slowHandle = -1;

    public int OnInit(IMql5MarketContext context)
    {
        fastHandle = context.IndicatorHandle("iMA", 5, 0, Mql5MaMethod.Ema, Mql5AppliedPrice.Close);
        slowHandle = context.IndicatorHandle("iMA", 20, 0, Mql5MaMethod.Ema, Mql5AppliedPrice.Close);
        return fastHandle > 0 && slowHandle > 0 ? Mql5InitCode.Succeeded : Mql5InitCode.Failed;
    }

    public void OnTick(IMql5MarketContext context)
    {
        double[] fast = new double[2];
        double[] slow = new double[2];

        if (context.CopyBuffer(fastHandle, 0, 0, 2, fast) != 2 ||
            context.CopyBuffer(slowHandle, 0, 0, 2, slow) != 2)
        {
            return;
        }

        if (fast[0] <= 0.0 || slow[0] <= 0.0)
        {
            return;
        }

        bool crossedUp = fast[0] <= slow[0] && fast[1] > slow[1];
        bool crossedDown = fast[0] >= slow[0] && fast[1] < slow[1];
        bool holding = context.PositionSelect(context.Symbol);

        if (crossedUp && !holding)
        {
            context.OrderSend(EngineTestSupport.Market(Mql5OrderType.Buy, 0.10), out _);
        }
        else if (crossedDown && holding)
        {
            long ticket = context.PositionGetInteger(Mql5PositionInteger.Ticket);
            context.OrderSend(EngineTestSupport.ClosePosition(ticket), out _);
        }
    }

    public void OnDeinit(IMql5MarketContext context, int reason)
    {
    }
}
