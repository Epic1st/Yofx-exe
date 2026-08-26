using YO4X.Mql5.Runtime;
using EngineContext = YO4X.Mql5.Engine.Context.Mql5MarketContext;
using EngineOrderType = YO4X.Mql5.Engine.Trading.Mql5OrderType;
using EngineRequest = YO4X.Mql5.Engine.Trading.Mql5TradeRequest;
using EngineResult = YO4X.Mql5.Engine.Trading.Mql5TradeResult;
using EngineTradeAction = YO4X.Mql5.Engine.Trading.Mql5TradeAction;

namespace YO4X.Mql5.Backtest;

/// <summary>
/// Presents the offline engine to a translated MQL5 strategy.
///
/// <para>
/// A generated strategy talks to <see cref="IMql5MarketContext"/>; the engine implements a
/// different, narrower interface of the same name. This class is the join between them. It
/// forwards what the engine genuinely knows and leaves everything else at the interface's
/// own "no data" defaults, so a strategy that asks for something the simulator does not
/// model reads MQL5's absence value rather than a number that was made up to fill the gap.
/// </para>
///
/// <para>
/// The engine simulates exactly one symbol on exactly one period. Every request naming a
/// different symbol or period is answered as absent rather than served with this series,
/// because silently substituting one instrument's prices for another's would corrupt a
/// backtest in a way nothing downstream could detect.
/// </para>
/// </summary>
public sealed class EngineRuntimeContext : IMql5MarketContext
{
    private readonly EngineContext engine;
    private readonly int periodMinutes;

    /// <summary>Joins a generated strategy to one engine context.</summary>
    /// <param name="engine">The engine context the strategy host created for this run.</param>
    /// <param name="periodMinutes">The bar period the feed supplies, in minutes.</param>
    public EngineRuntimeContext(EngineContext engine, int periodMinutes)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(periodMinutes);
        this.engine = engine;
        this.periodMinutes = periodMinutes;
    }

    /// <inheritdoc />
    public string Symbol => engine.Symbol;

    /// <inheritdoc />
    public double Point => engine.Point;

    /// <inheritdoc />
    public int Digits => engine.Digits;

    /// <inheritdoc />
    public DateTime TimeCurrent => engine.TimeCurrent;

    /// <summary>The run's period, as the MQL5 timeframe identifier for it.</summary>
    public int Period => TimeframeIdentifier(periodMinutes);

    /// <inheritdoc />
    public double SymbolInfoDouble(string symbol, int propertyId) =>
        IsRunSymbol(symbol) ? engine.SymbolInfoDouble(engine.Symbol, propertyId) : 0.0;

    /// <inheritdoc />
    public long SymbolInfoInteger(string symbol, int propertyId) =>
        IsRunSymbol(symbol) ? engine.SymbolInfoInteger(engine.Symbol, propertyId) : 0L;

    /// <inheritdoc />
    public bool SymbolSelect(string symbol, bool selectFlag) =>
        IsRunSymbol(symbol) && engine.SymbolSelect(engine.Symbol, selectFlag);

    /// <inheritdoc />
    public double AccountInfoDouble(int propertyId) => engine.AccountInfoDouble(propertyId);

    /// <inheritdoc />
    public long AccountInfoInteger(int propertyId) => engine.AccountInfoInteger(propertyId);

    /// <inheritdoc />
    public int PositionsTotal() => engine.PositionsTotal();

    /// <inheritdoc />
    public bool PositionSelect(string symbol) =>
        IsRunSymbol(symbol) && engine.PositionSelect(engine.Symbol);

    /// <inheritdoc />
    public bool PositionSelectByTicket(ulong ticket) =>
        ticket <= long.MaxValue && engine.PositionSelectByTicket((long)ticket);

    /// <inheritdoc />
    public ulong PositionGetTicket(int index)
    {
        long ticket = engine.PositionGetTicket(index);
        return ticket <= 0 ? 0UL : (ulong)ticket;
    }

    /// <inheritdoc />
    public string PositionGetSymbol(int index) =>
        engine.PositionGetTicket(index) <= 0 ? string.Empty : engine.PositionGetSymbol();

    /// <inheritdoc />
    public double PositionGetDouble(int propertyId) => engine.PositionGetDouble(propertyId);

    /// <inheritdoc />
    public long PositionGetInteger(int propertyId) => engine.PositionGetInteger(propertyId);

    /// <inheritdoc />
    public bool OrderSend(Mql5TradeRequest request, out Mql5TradeResult result)
    {
        result = new Mql5TradeResult();
        if (request is null)
        {
            result.Retcode = Mql5Constants.TradeRetcode.Invalid;
            return false;
        }

        // The simulator models the six plain order types. Stop-limit and close-by requests
        // are refused outright rather than approximated by a nearer type, because filling
        // one order type as another is exactly the kind of substitution a backtest cannot
        // be allowed to make silently.
        if (!TryMapAction(request.Action, out EngineTradeAction action)
            || !TryMapOrderType(request.Type, out EngineOrderType orderType))
        {
            result.Retcode = Mql5Constants.TradeRetcode.Invalid;
            return false;
        }

        var engineRequest = new EngineRequest
        {
            Action = action,
            Type = orderType,
            Symbol = string.IsNullOrEmpty(request.Symbol) ? engine.Symbol : request.Symbol,
            Volume = request.Volume,
            Price = request.Price,
            StopLimit = request.StopLimit,
            Sl = request.StopLoss,
            Tp = request.TakeProfit,
            Deviation = ToSignedOrZero(request.Deviation),
            Magic = ToSignedOrZero(request.Magic),
            Order = ToSignedOrZero(request.Order),
            Position = ToSignedOrZero(request.Position),
            Comment = request.Comment,
        };

        bool accepted = engine.OrderSend(engineRequest, out EngineResult engineResult);
        result.Retcode = engineResult.Retcode < 0 ? 0u : (uint)engineResult.Retcode;
        result.Deal = ToUnsignedOrZero(engineResult.Deal);
        result.Order = ToUnsignedOrZero(engineResult.Order);
        result.Volume = engineResult.Volume;
        result.Price = engineResult.Price;
        result.Bid = engineResult.Bid;
        result.Ask = engineResult.Ask;
        result.Comment = engineResult.Comment;
        return accepted;
    }

    /// <inheritdoc />
    public int IndicatorHandle(string name, params object[] parameters) =>
        engine.IndicatorHandle(name, parameters);

    /// <inheritdoc />
    public int CopyBuffer(int handle, int bufferNum, int start, int count, double[] target) =>
        engine.CopyBuffer(handle, bufferNum, start, count, target);

    // ---- price series -------------------------------------------------------------
    // The interface defaults every one of these to "no data". They are overridden here
    // because the engine does hold the series, and a translated EA that reads bars through
    // CopyRates or iClose would otherwise run blind against a feed that is right there.

    /// <inheritdoc />
    public int Bars(string symbol, int timeframe) =>
        Serves(symbol, timeframe) ? engine.BarCount : 0;

    /// <inheritdoc />
    public int BarCount(string symbol, int timeframe) => Bars(symbol, timeframe);

    /// <inheritdoc />
    public double BarOpen(string symbol, int timeframe, int shift) =>
        Serves(symbol, timeframe) ? engine.Open(shift) : 0.0;

    /// <inheritdoc />
    public double BarHigh(string symbol, int timeframe, int shift) =>
        Serves(symbol, timeframe) ? engine.High(shift) : 0.0;

    /// <inheritdoc />
    public double BarLow(string symbol, int timeframe, int shift) =>
        Serves(symbol, timeframe) ? engine.Low(shift) : 0.0;

    /// <inheritdoc />
    public double BarClose(string symbol, int timeframe, int shift) =>
        Serves(symbol, timeframe) ? engine.Close(shift) : 0.0;

    /// <inheritdoc />
    public long BarTime(string symbol, int timeframe, int shift) =>
        Serves(symbol, timeframe) ? Mql5Time.FromDateTime(engine.Time(shift)) : 0L;

    /// <inheritdoc />
    public long BarTickVolume(string symbol, int timeframe, int shift) =>
        Serves(symbol, timeframe) ? engine.Volume(shift) : 0L;

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

        // Oldest first, matching the engine's own CopyBuffer ordering.
        for (int index = 0; index < count; index++)
        {
            int shift = range.StartPosition + count - 1 - index;
            target[index] = new Mql5Rates
            {
                Time = Mql5Time.FromDateTime(engine.Time(shift)),
                Open = engine.Open(shift),
                High = engine.High(shift),
                Low = engine.Low(shift),
                Close = engine.Close(shift),
                TickVolume = engine.Volume(shift),
                Spread = (int)engine.SymbolInfoInteger(engine.Symbol, SpreadPropertyId),
                RealVolume = 0,
            };
        }

        return count;
    }

    /// <inheritdoc />
    public int CopyOpen(string symbol, int timeframe, Mql5CopyRange range, ref double[] target) =>
        CopySeries(symbol, timeframe, range, ref target, engine.Open);

    /// <inheritdoc />
    public int CopyHigh(string symbol, int timeframe, Mql5CopyRange range, ref double[] target) =>
        CopySeries(symbol, timeframe, range, ref target, engine.High);

    /// <inheritdoc />
    public int CopyLow(string symbol, int timeframe, Mql5CopyRange range, ref double[] target) =>
        CopySeries(symbol, timeframe, range, ref target, engine.Low);

    /// <inheritdoc />
    public int CopyClose(string symbol, int timeframe, Mql5CopyRange range, ref double[] target) =>
        CopySeries(symbol, timeframe, range, ref target, engine.Close);

    /// <inheritdoc />
    public int CopyTime(string symbol, int timeframe, Mql5CopyRange range, ref long[] target) =>
        CopySeries(symbol, timeframe, range, ref target, shift => Mql5Time.FromDateTime(engine.Time(shift)));

    /// <inheritdoc />
    public int CopyTickVolume(string symbol, int timeframe, Mql5CopyRange range, ref long[] target) =>
        CopySeries(symbol, timeframe, range, ref target, engine.Volume);

    private const int SpreadPropertyId = 18;

    private int CopySeries<T>(
        string symbol,
        int timeframe,
        Mql5CopyRange range,
        ref T[] target,
        Func<int, T> read)
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
            target[index] = read(range.StartPosition + count - 1 - index);
        }

        return count;
    }

    /// <summary>
    /// How much of the requested window the feed has actually replayed so far. A request
    /// reaching past the start of history is trimmed rather than padded with zeroes.
    /// </summary>
    private int Available(Mql5CopyRange range)
    {
        if (range.Count <= 0 || range.StartPosition < 0)
        {
            return 0;
        }

        int reachable = engine.BarCount - range.StartPosition;
        return reachable <= 0 ? 0 : Math.Min(range.Count, reachable);
    }

    private bool Serves(string symbol, int timeframe) =>
        IsRunSymbol(symbol) && IsRunPeriod(timeframe);

    private bool IsRunSymbol(string? symbol) =>
        string.IsNullOrEmpty(symbol)
        || string.Equals(symbol, engine.Symbol, StringComparison.OrdinalIgnoreCase);

    /// <summary>Timeframe 0 means "the chart's own period", which is the run's period.</summary>
    private bool IsRunPeriod(int timeframe) =>
        timeframe == Mql5Constants.Timeframes.Current || timeframe == Period;

    private static long ToSignedOrZero(ulong value) =>
        value > long.MaxValue ? 0L : (long)value;

    private static ulong ToUnsignedOrZero(long value) =>
        value <= 0 ? 0UL : (ulong)value;

    private static bool TryMapAction(int action, out EngineTradeAction mapped)
    {
        mapped = (EngineTradeAction)action;
        return Enum.IsDefined(mapped);
    }

    private static bool TryMapOrderType(int type, out EngineOrderType mapped)
    {
        mapped = (EngineOrderType)type;
        return Enum.IsDefined(mapped);
    }

    /// <summary>
    /// The MQL5 timeframe identifier for a whole number of minutes. Minute periods are
    /// their own identifier; hour and day periods are not, so they are mapped from the
    /// measured constants rather than computed.
    /// </summary>
    public static int TimeframeIdentifier(int minutes) => minutes switch
    {
        1 => Mql5Constants.Timeframes.M1,
        5 => Mql5Constants.Timeframes.M5,
        15 => Mql5Constants.Timeframes.M15,
        30 => Mql5Constants.Timeframes.M30,
        60 => Mql5Constants.Timeframes.H1,
        240 => Mql5Constants.Timeframes.H4,
        1440 => Mql5Constants.Timeframes.D1,
        10080 => Mql5Constants.Timeframes.W1,
        _ => Mql5Constants.Timeframes.Current,
    };
}
