using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Runtime;
using YO4X.Mt5.ConnectionProbe.Windows;

namespace YO4X.Mql5.Live;

/// <summary>
/// Presents a live broker session to a translated MQL5 strategy.
///
/// <para>
/// This is the same seam as the backtest bridge, pointed at a real account: bars come from the
/// live series rather than a replayed file, and <c>OrderSend</c> reaches a broker rather than a
/// simulator. Everything the strategy can see is what the broker reported; nothing is modelled.
/// </para>
///
/// <para>
/// The strategy cannot widen its own limits through this class. Volume, symbol, account type
/// and the operator enable file are enforced inside the trade client on every instruction, so
/// a strategy asking for ten lots is refused there — not negotiated down to something
/// acceptable, which would have it trading a position it never chose.
/// </para>
/// </summary>
public sealed class LiveBrokerContext : IMql5MarketContext
{
    private readonly LiveBarSeries series;
    private readonly Mt5NetApiDemoTradeClient broker;
    private readonly Action<string> journal;
    private readonly int digits;
    private readonly List<Mt5DemoOrderReceipt> open = [];
    private Mt5DemoOrderReceipt? selected;

    /// <summary>Joins a strategy to one live account and one bar series.</summary>
    /// <param name="series">The live bars, seeded from history.</param>
    /// <param name="broker">The guarded trade client for this account.</param>
    /// <param name="digits">The symbol's price precision.</param>
    /// <param name="journal">Receives a line for anything refused or unsupported.</param>
    public LiveBrokerContext(
        LiveBarSeries series,
        Mt5NetApiDemoTradeClient broker,
        int digits,
        Action<string> journal)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(digits);

        this.series = series;
        this.broker = broker;
        this.digits = digits;
        this.journal = journal;
    }

    /// <summary>Positions this context opened and has not yet closed.</summary>
    public IReadOnlyList<Mt5DemoOrderReceipt> OpenPositions => open;

    /// <inheritdoc />
    public string Symbol => series.Symbol;

    /// <inheritdoc />
    public double Point => 1.0 / Math.Pow(10.0, digits);

    /// <inheritdoc />
    public int Digits => digits;

    /// <inheritdoc />
    public DateTime TimeCurrent => series.LastQuoteTime;

    /// <summary>The chart period this strategy is running on.</summary>
    public int Period => LivePeriods.Identifier(series.PeriodMinutes);

    /// <inheritdoc />
    public double SymbolInfoDouble(string symbol, int propertyId) =>
        !IsRunSymbol(symbol) ? 0.0 : propertyId switch
        {
            1 => series.Bid,
            4 => series.Ask,
            7 => series.Bid,
            16 => Point,
            27 => Point,
            _ => 0.0,
        };

    /// <inheritdoc />
    public long SymbolInfoInteger(string symbol, int propertyId) =>
        !IsRunSymbol(symbol) ? 0L : propertyId switch
        {
            0 => 1,
            17 => digits,
            18 => (long)Math.Round((series.Ask - series.Bid) / Point),
            _ => 0L,
        };

    /// <inheritdoc />
    public bool SymbolSelect(string symbol, bool selectFlag) => IsRunSymbol(symbol);

    /// <summary>
    /// Account figures are not served to a live strategy.
    ///
    /// <para>
    /// Reading balance or equity means a broker round trip, and doing that inside a tick
    /// handler would put ~80ms of network on the decision path several times per bar. A
    /// strategy that sizes from equity would size from a stale number either way, so the
    /// honest answer is zero — MQL5's own value for "unavailable" — rather than a figure that
    /// looks current and is not.
    /// </para>
    /// </summary>
    public double AccountInfoDouble(int propertyId) => 0.0;

    /// <inheritdoc />
    public int PositionsTotal() => open.Count;

    /// <inheritdoc />
    public bool PositionSelect(string symbol)
    {
        if (!IsRunSymbol(symbol))
        {
            return false;
        }

        selected = open.Count > 0 ? open[^1] : null;
        return selected is not null;
    }

    /// <inheritdoc />
    public bool PositionSelectByTicket(ulong ticket)
    {
        selected = open.Find(position => (ulong)position.Ticket == ticket);
        return selected is not null;
    }

    /// <inheritdoc />
    public ulong PositionGetTicket(int index)
    {
        if (index < 0 || index >= open.Count)
        {
            return 0UL;
        }

        selected = open[index];
        return (ulong)selected.Ticket;
    }

    /// <inheritdoc />
    public string PositionGetSymbol(int index) =>
        index >= 0 && index < open.Count ? open[index].Symbol : string.Empty;

    /// <inheritdoc />
    public double PositionGetDouble(int propertyId) => selected is not { } position ? 0.0 : propertyId switch
    {
        3 => position.Volume,
        4 => position.Price,
        5 => position.Side == Mt5DemoSide.Buy ? series.Bid : series.Ask,
        10 => position.Profit,
        _ => 0.0,
    };

    /// <inheritdoc />
    public long PositionGetInteger(int propertyId) => selected is not { } position ? 0L : propertyId switch
    {
        1 => new DateTimeOffset(position.OpenTime, TimeSpan.Zero).ToUnixTimeSeconds(),
        2 => position.Side == Mt5DemoSide.Buy ? 0L : 1L,
        13 => position.Ticket,
        17 => position.Ticket,
        _ => 0L,
    };

    /// <summary>
    /// Sends the strategy's order to the broker.
    ///
    /// <para>
    /// The call is synchronous because MQL5's own <c>OrderSend</c> is: a strategy expects the
    /// result before its next statement, and returning early would let it act on a fill that
    /// has not happened. The wait is the broker's round trip, and it is the strategy's own
    /// semantics that require paying it.
    /// </para>
    /// </summary>
    public bool OrderSend(Mql5TradeRequest request, out Mql5TradeResult result)
    {
        result = new Mql5TradeResult();
        if (request is null)
        {
            result.Retcode = Mql5Constants.TradeRetcode.Invalid;
            return false;
        }

        try
        {
            switch (request.Action)
            {
                case 1 when request.Type is 0 or 1:
                    return Open(request, result);
                case 5:
                    return Place(request, result);
                case 8:
                    return Remove(request, result);
                default:
                    journal($"refused action {request.Action} type {request.Type}: not supported live");
                    result.Retcode = Mql5Constants.TradeRetcode.Invalid;
                    return false;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or ArgumentOutOfRangeException
            or InvalidDataException
            or TimeoutException)
        {
            // A refusal is the broker's answer, not a crash. The strategy is told the order
            // failed, exactly as MQL5 would, and the reason is recorded for the operator.
            journal("order refused: " + exception.Message);
            result.Retcode = Mql5Constants.TradeRetcode.Reject;
            return false;
        }
    }

    private bool Open(Mql5TradeRequest request, Mql5TradeResult result)
    {
        // A close is an open in the opposite direction against a position we already hold.
        Mt5DemoOrderReceipt? facing = open.Find(position =>
            (position.Side == Mt5DemoSide.Buy && request.Type == 1)
            || (position.Side == Mt5DemoSide.Sell && request.Type == 0));
        if (facing is not null && Math.Abs(facing.Volume - request.Volume) < 1e-9)
        {
            Mt5DemoOrderReceipt closed = broker.CloseAsync(facing).GetAwaiter().GetResult();
            open.Remove(facing);
            selected = null;
            result.Retcode = Mql5Constants.TradeRetcode.Done;
            result.Order = (ulong)closed.Ticket;
            result.Price = closed.Price;
            return true;
        }

        Mt5DemoOrderReceipt opened = broker
            .SendAsync(
                request.Type == 0 ? Mt5DemoSide.Buy : Mt5DemoSide.Sell,
                request.Volume,
                0,
                request.StopLoss,
                request.TakeProfit,
                request.Comment)
            .GetAwaiter()
            .GetResult();
        if (opened.Ticket == 0)
        {
            result.Retcode = Mql5Constants.TradeRetcode.Reject;
            return false;
        }

        open.Add(opened);
        result.Retcode = Mql5Constants.TradeRetcode.Done;
        result.Order = (ulong)opened.Ticket;
        result.Deal = (ulong)opened.Ticket;
        result.Price = opened.Price;
        result.Volume = opened.Volume;
        return true;
    }

    private bool Place(Mql5TradeRequest request, Mql5TradeResult result)
    {
        Mt5DemoSide side = request.Type switch
        {
            2 => Mt5DemoSide.BuyLimit,
            3 => Mt5DemoSide.SellLimit,
            4 => Mt5DemoSide.BuyStop,
            5 => Mt5DemoSide.SellStop,
            _ => Mt5DemoSide.Buy,
        };
        Mt5DemoOrderReceipt placed = broker
            .SendAsync(side, request.Volume, request.Price, request.StopLoss, request.TakeProfit, request.Comment)
            .GetAwaiter()
            .GetResult();
        if (placed.Ticket == 0)
        {
            result.Retcode = Mql5Constants.TradeRetcode.Reject;
            return false;
        }

        open.Add(placed);
        result.Retcode = Mql5Constants.TradeRetcode.Placed;
        result.Order = (ulong)placed.Ticket;
        return true;
    }

    private bool Remove(Mql5TradeRequest request, Mql5TradeResult result)
    {
        Mt5DemoOrderReceipt? target = open.Find(position => (ulong)position.Ticket == request.Order);
        if (target is null)
        {
            result.Retcode = Mql5Constants.TradeRetcode.Invalid;
            return false;
        }

        broker.CancelAsync(target).GetAwaiter().GetResult();
        open.Remove(target);
        result.Retcode = Mql5Constants.TradeRetcode.Done;
        return true;
    }

    /// <inheritdoc />
    public int IndicatorHandle(string name, params object[] parameters) =>
        series.ResolveIndicator(name, parameters);

    /// <inheritdoc />
    public int CopyBuffer(int handle, int bufferNum, int start, int count, double[] target) =>
        series.CopyBuffer(handle, bufferNum, start, count, target);

    /// <inheritdoc />
    public int Bars(string symbol, int timeframe) => Serves(symbol, timeframe) ? series.Count : 0;

    /// <inheritdoc />
    public int BarCount(string symbol, int timeframe) => Bars(symbol, timeframe);

    /// <inheritdoc />
    public double BarOpen(string symbol, int timeframe, int shift) =>
        Serves(symbol, timeframe) ? series.At(shift).Open : 0.0;

    /// <inheritdoc />
    public double BarHigh(string symbol, int timeframe, int shift) =>
        Serves(symbol, timeframe) ? series.At(shift).High : 0.0;

    /// <inheritdoc />
    public double BarLow(string symbol, int timeframe, int shift) =>
        Serves(symbol, timeframe) ? series.At(shift).Low : 0.0;

    /// <inheritdoc />
    public double BarClose(string symbol, int timeframe, int shift) =>
        Serves(symbol, timeframe) ? series.At(shift).Close : 0.0;

    /// <inheritdoc />
    public long BarTime(string symbol, int timeframe, int shift) =>
        Serves(symbol, timeframe) ? Mql5Time.FromDateTime(series.At(shift).Time) : 0L;

    /// <inheritdoc />
    public long BarTickVolume(string symbol, int timeframe, int shift) =>
        Serves(symbol, timeframe) ? series.At(shift).TickVolume : 0L;

    /// <inheritdoc />
    public int CopyRates(string symbol, int timeframe, Mql5CopyRange range, ref Mql5Rates[] target)
    {
        if (!Serves(symbol, timeframe) || range.Kind != Mql5CopyRangeKind.FromPosition)
        {
            return -1;
        }

        int count = Available(range);
        if (count <= 0)
        {
            return count;
        }

        if (target.Length < count)
        {
            Array.Resize(ref target, count);
        }

        for (int index = 0; index < count; index++)
        {
            Mql5Bar bar = series.At(range.StartPosition + count - 1 - index);
            target[index] = new Mql5Rates
            {
                Time = Mql5Time.FromDateTime(bar.Time),
                Open = bar.Open,
                High = bar.High,
                Low = bar.Low,
                Close = bar.Close,
                TickVolume = bar.TickVolume,
                Spread = bar.Spread,
                RealVolume = 0,
            };
        }

        return count;
    }

    private int Available(Mql5CopyRange range)
    {
        if (range.Count <= 0 || range.StartPosition < 0)
        {
            return 0;
        }

        int reachable = series.Count - range.StartPosition;
        return reachable <= 0 ? 0 : Math.Min(range.Count, reachable);
    }

    private bool Serves(string symbol, int timeframe) =>
        IsRunSymbol(symbol)
        && (timeframe == Mql5Constants.Timeframes.Current || timeframe == Period);

    private bool IsRunSymbol(string? symbol) =>
        string.IsNullOrEmpty(symbol)
        || string.Equals(symbol, series.Symbol, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Maps whole minutes onto MQL5's timeframe identifiers.</summary>
public static class LivePeriods
{
    /// <summary>The MQL5 timeframe identifier for a period given in minutes.</summary>
    /// <param name="minutes">The period length.</param>
    public static int Identifier(int minutes) => minutes switch
    {
        1 => Mql5Constants.Timeframes.M1,
        5 => Mql5Constants.Timeframes.M5,
        15 => Mql5Constants.Timeframes.M15,
        30 => Mql5Constants.Timeframes.M30,
        60 => Mql5Constants.Timeframes.H1,
        240 => Mql5Constants.Timeframes.H4,
        1440 => Mql5Constants.Timeframes.D1,
        _ => Mql5Constants.Timeframes.Current,
    };
}
