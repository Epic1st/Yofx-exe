using System.Globalization;
using YO4X.Mql5.Engine.Context;
using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Hosting;

/// <summary>
/// Drives a strategy over a feed: <c>OnInit</c>, then one <c>OnTick</c> per bar with stops,
/// targets and pending orders evaluated in between, then <c>OnDeinit</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per bar the order is fixed: append the bar to the series so indicators advance, walk the bar's
/// intrabar path through the broker so any resting stop, target or pending order that the bar
/// touches fires, then hand the strategy a tick at the bar close. A strategy therefore never sees
/// a price the broker has not already reacted to, and never a future bar.
/// </para>
/// <para>
/// The host is a simulator end to end. It opens no sockets, loads no native library and knows
/// nothing about any live trading path in this solution.
/// </para>
/// </remarks>
public static class Mql5StrategyHost
{
    /// <summary>Runs a strategy to completion and returns the report.</summary>
    public static Mql5RunReport Run(IMql5Strategy strategy, IMql5MarketFeed feed, Mql5RunOptions options)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(options);

        var broker = new Mql5SimulatedBroker(options);
        var context = new Mql5MarketContext(broker, options);
        var equityCurve = new List<double>();

        int ticks = 0;
        int barsSeen = 0;
        bool tickCapTriggered = false;
        string fault = string.Empty;

        int initCode;
        try
        {
            initCode = strategy.OnInit(context);
        }
        catch (Exception ex) when (IsStrategyFault(ex))
        {
            initCode = Mql5InitCode.Failed;
            fault = Describe(ex);
        }

        if (initCode != Mql5InitCode.Succeeded)
        {
            var journal = new List<Mql5OrderEvent>(broker.Journal)
            {
                new()
                {
                    Time = broker.Time,
                    Kind = fault.Length > 0 ? Mql5OrderEventKind.StrategyFault : Mql5OrderEventKind.InitFailed,
                    Symbol = options.Symbol.Name,
                    Retcode = initCode,
                    Balance = broker.Balance,
                    Detail = fault.Length > 0 ? fault : "OnInit returned " + initCode.ToString(CultureInfo.InvariantCulture),
                },
            };

            SafeDeinit(strategy, context, Mql5DeinitReason.InitFailed, ref fault);
            return Summarize(options, broker, journal, equityCurve, initCode, 0, 0, false, fault);
        }

        foreach (Mql5Bar bar in feed.ReadBars())
        {
            barsSeen++;

            if (ticks >= options.MaxTicks)
            {
                tickCapTriggered = true;
                break;
            }

            context.AppendBar(bar);
            broker.ApplyBar(bar);
            broker.BeginTick();

            try
            {
                strategy.OnTick(context);
            }
            catch (Exception ex) when (IsStrategyFault(ex))
            {
                fault = Describe(ex);
                break;
            }

            ticks++;
            equityCurve.Add(broker.Equity);
        }

        var events = new List<Mql5OrderEvent>(broker.Journal);

        if (tickCapTriggered)
        {
            events.Add(new Mql5OrderEvent
            {
                Time = broker.Time,
                Kind = Mql5OrderEventKind.TickCapReached,
                Symbol = options.Symbol.Name,
                Balance = broker.Balance,
                Retcode = Mql5TradeRetcode.Done,
                Detail = string.Create(
                    CultureInfo.InvariantCulture,
                    $"tick cap of {options.MaxTicks} reached; the run stopped early"),
            });
        }

        if (fault.Length > 0)
        {
            events.Add(new Mql5OrderEvent
            {
                Time = broker.Time,
                Kind = Mql5OrderEventKind.StrategyFault,
                Symbol = options.Symbol.Name,
                Balance = broker.Balance,
                Retcode = Mql5TradeRetcode.Error,
                Detail = fault,
            });
        }

        if (options.CloseOpenPositionsAtEnd && broker.Positions.Count > 0 && broker.HasQuote)
        {
            int before = broker.Journal.Count;
            broker.CloseAll(Mql5CloseReason.EndOfRun);
            for (int index = before; index < broker.Journal.Count; index++)
            {
                events.Add(broker.Journal[index]);
            }
        }

        int deinitReason = fault.Length > 0 || tickCapTriggered
            ? Mql5DeinitReason.Close
            : Mql5DeinitReason.Program;

        string deinitFault = string.Empty;
        SafeDeinit(strategy, context, deinitReason, ref deinitFault);
        if (fault.Length == 0 && deinitFault.Length > 0)
        {
            fault = deinitFault;
        }

        if (equityCurve.Count == 0)
        {
            equityCurve.Add(broker.Equity);
        }
        else
        {
            equityCurve[^1] = broker.Equity;
        }

        return Summarize(options, broker, events, equityCurve, initCode, ticks, barsSeen, tickCapTriggered, fault);
    }

    private static bool IsStrategyFault(Exception ex) => ex is not (OutOfMemoryException or StackOverflowException);

    private static string Describe(Exception ex) => ex.GetType().Name + ": " + ex.Message;

    private static void SafeDeinit(IMql5Strategy strategy, Mql5MarketContext context, int reason, ref string fault)
    {
        try
        {
            strategy.OnDeinit(context, reason);
        }
        catch (Exception ex) when (IsStrategyFault(ex))
        {
            if (fault.Length == 0)
            {
                fault = Describe(ex);
            }
        }
    }

    private static Mql5RunReport Summarize(
        Mql5RunOptions options,
        Mql5SimulatedBroker broker,
        List<Mql5OrderEvent> events,
        List<double> equityCurve,
        int initCode,
        int ticks,
        int barsSeen,
        bool tickCapTriggered,
        string fault)
    {
        double grossProfit = 0.0;
        double grossLoss = 0.0;
        int wins = 0;
        int losses = 0;

        foreach (Mql5ClosedTrade trade in broker.ClosedTrades)
        {
            double net = trade.NetProfit;
            if (net > 0.0)
            {
                grossProfit += net;
                wins++;
            }
            else if (net < 0.0)
            {
                grossLoss += -net;
                losses++;
            }
        }

        double peak = options.InitialDeposit;
        double maxDrawdown = 0.0;
        double maxDrawdownPercent = 0.0;

        foreach (double equity in equityCurve)
        {
            peak = Math.Max(peak, equity);
            double drawdown = peak - equity;
            if (drawdown > maxDrawdown)
            {
                maxDrawdown = drawdown;
                maxDrawdownPercent = peak > 0.0 ? drawdown / peak * 100.0 : 0.0;
            }
        }

        double profitFactor;
        if (grossLoss > 0.0)
        {
            profitFactor = grossProfit / grossLoss;
        }
        else
        {
            profitFactor = grossProfit > 0.0 ? double.PositiveInfinity : 0.0;
        }

        return new Mql5RunReport
        {
            Symbol = options.Symbol.Name,
            InitRetcode = initCode,
            TicksProcessed = ticks,
            BarsSeen = barsSeen,
            InitialDeposit = options.InitialDeposit,
            FinalBalance = broker.Balance,
            FinalEquity = broker.Equity,
            MaxDrawdown = Math.Round(maxDrawdown, 2, MidpointRounding.AwayFromZero),
            MaxDrawdownPercent = Math.Round(maxDrawdownPercent, 4, MidpointRounding.AwayFromZero),
            GrossProfit = Math.Round(grossProfit, 2, MidpointRounding.AwayFromZero),
            GrossLoss = Math.Round(grossLoss, 2, MidpointRounding.AwayFromZero),
            ProfitFactor = profitFactor,
            TotalTrades = broker.ClosedTrades.Count,
            WinningTrades = wins,
            LosingTrades = losses,
            Trades = [.. broker.ClosedTrades],
            Events = events,
            EquityCurve = equityCurve,
            OrdersPerTickCapTriggered = broker.OrdersPerTickCapTriggered,
            TickCapTriggered = tickCapTriggered,
            StopOutTriggered = broker.StopOutTriggered,
            StrategyFault = fault,
        };
    }
}
