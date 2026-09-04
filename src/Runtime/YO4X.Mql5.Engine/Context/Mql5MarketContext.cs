using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Hosting;
using YO4X.Mql5.Engine.Indicators;
using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Context;

/// <summary>
/// Bridges the simulated broker and the replayed bar series into the surface a translated expert
/// advisor calls. Purely in-memory; it holds no connection of any kind.
/// </summary>
public sealed class Mql5MarketContext : IMql5MarketContext
{
    private readonly Mql5SimulatedBroker broker;
    private readonly Mql5RunOptions options;
    private readonly List<Mql5Bar> bars = [];
    private readonly List<IMql5Indicator> indicators = [];
    private readonly Dictionary<string, int> indicatorHandles = new(StringComparer.Ordinal);

    private Mql5Position? selected;

    /// <summary>Initializes the context over a broker and the run options.</summary>
    public Mql5MarketContext(Mql5SimulatedBroker broker, Mql5RunOptions options)
    {
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentNullException.ThrowIfNull(options);
        this.broker = broker;
        this.options = options;
    }

    /// <inheritdoc />
    public string Symbol => options.Symbol.Name;

    /// <inheritdoc />
    public double Point => options.Symbol.Point;

    /// <inheritdoc />
    public int Digits => options.Symbol.Digits;

    /// <inheritdoc />
    public DateTime TimeCurrent => broker.Time;

    /// <summary>Gets the broker the context trades through.</summary>
    public Mql5SimulatedBroker Broker => broker;

    /// <summary>Gets the bars replayed so far, oldest first.</summary>
    public IReadOnlyList<Mql5Bar> Bars => bars;

    /// <summary>Gets the number of bars replayed so far.</summary>
    public int BarCount => bars.Count;

    /// <summary>Gets the current bid.</summary>
    public double Bid => broker.Bid;

    /// <summary>Gets the current ask.</summary>
    public double Ask => broker.Ask;

    /// <summary>
    /// Appends a bar and advances every allocated indicator. The host calls this once per tick,
    /// before the strategy runs, so an indicator can never see a future bar.
    /// </summary>
    public void AppendBar(in Mql5Bar bar)
    {
        bars.Add(bar);
        foreach (IMql5Indicator indicator in indicators)
        {
            indicator.Append(bar);
        }
    }

    /// <summary>Reads the open of the bar <paramref name="shift"/> bars back, zero being current.</summary>
    public double Open(int shift) => BarAt(shift).Open;

    /// <summary>Reads the high of the bar <paramref name="shift"/> bars back.</summary>
    public double High(int shift) => BarAt(shift).High;

    /// <summary>Reads the low of the bar <paramref name="shift"/> bars back.</summary>
    public double Low(int shift) => BarAt(shift).Low;

    /// <summary>Reads the close of the bar <paramref name="shift"/> bars back.</summary>
    public double Close(int shift) => BarAt(shift).Close;

    /// <summary>Reads the open time of the bar <paramref name="shift"/> bars back.</summary>
    public DateTime Time(int shift) => BarAt(shift).Time;

    /// <summary>Reads the tick volume of the bar <paramref name="shift"/> bars back.</summary>
    public long Volume(int shift) => BarAt(shift).TickVolume;

    /// <inheritdoc />
    public bool SymbolSelect(string symbol, bool enable) => IsKnownSymbol(symbol);

    /// <inheritdoc />
    public double SymbolInfoDouble(string symbol, int propertyId)
    {
        if (!IsKnownSymbol(symbol))
        {
            return 0.0;
        }

        Mql5SymbolSpec spec = options.Symbol;
        return propertyId switch
        {
            Mql5SymbolInfoDouble.Bid => broker.Bid,
            Mql5SymbolInfoDouble.Ask => broker.Ask,
            Mql5SymbolInfoDouble.Last => broker.Bid,
            Mql5SymbolInfoDouble.Point => spec.Point,
            Mql5SymbolInfoDouble.TickValue => spec.TickValue,
            Mql5SymbolInfoDouble.TickSize => spec.TickSize,
            Mql5SymbolInfoDouble.ContractSize => spec.ContractSize,
            Mql5SymbolInfoDouble.VolumeMin => spec.VolumeMin,
            Mql5SymbolInfoDouble.VolumeMax => spec.VolumeMax,
            Mql5SymbolInfoDouble.VolumeStep => spec.VolumeStep,
            Mql5SymbolInfoDouble.VolumeLimit => 0.0,
            Mql5SymbolInfoDouble.SwapLong => spec.SwapLong,
            Mql5SymbolInfoDouble.SwapShort => spec.SwapShort,
            _ => 0.0,
        };
    }

    /// <inheritdoc />
    public long SymbolInfoInteger(string symbol, int propertyId)
    {
        if (!IsKnownSymbol(symbol))
        {
            return 0L;
        }

        Mql5SymbolSpec spec = options.Symbol;
        return propertyId switch
        {
            Mql5SymbolInfoInteger.Digits => spec.Digits,
            Mql5SymbolInfoInteger.Spread => broker.SpreadPoints,
            Mql5SymbolInfoInteger.StopsLevel => spec.StopsLevelPoints,
            Mql5SymbolInfoInteger.FreezeLevel => spec.FreezeLevelPoints,
            // This simulated symbol supports the complete order surface implemented by the
            // broker. Returning zero here means SYMBOL_TRADE_MODE_DISABLED in MQL5 and causes
            // production experts to refuse initialization even though OrderSend is available.
            Mql5SymbolInfoInteger.TradeMode => 4L, // SYMBOL_TRADE_MODE_FULL
            Mql5SymbolInfoInteger.TradeExecutionMode => 2L, // SYMBOL_TRADE_EXECUTION_MARKET
            Mql5SymbolInfoInteger.FillingMode => 3L, // SYMBOL_FILLING_FOK | SYMBOL_FILLING_IOC
            Mql5SymbolInfoInteger.ExpirationMode => 15L,
            Mql5SymbolInfoInteger.OrderMode => 127L,
            Mql5SymbolInfoInteger.Time => ToUnixSeconds(broker.Time),
            Mql5SymbolInfoInteger.Select => 1L,
            _ => 0L,
        };
    }

    /// <inheritdoc />
    public double AccountInfoDouble(int propertyId) => propertyId switch
    {
        Mql5AccountInfoDouble.Balance => broker.Balance,
        Mql5AccountInfoDouble.Credit => 0.0,
        Mql5AccountInfoDouble.Profit => broker.FloatingProfit,
        Mql5AccountInfoDouble.Equity => broker.Equity,
        Mql5AccountInfoDouble.Margin => broker.Margin,
        Mql5AccountInfoDouble.MarginFree => broker.FreeMargin,
        Mql5AccountInfoDouble.MarginLevel => broker.MarginLevel,
        Mql5AccountInfoDouble.MarginStopOut => options.StopOutLevelPercent,
        _ => 0.0,
    };

    /// <summary>Reads an integer account property. See <see cref="Mql5AccountInfoInteger"/>.</summary>
    public long AccountInfoInteger(int propertyId) => propertyId switch
    {
        Mql5AccountInfoInteger.Login => 0L,
        Mql5AccountInfoInteger.TradeMode => 0L, // ACCOUNT_TRADE_MODE_DEMO
        Mql5AccountInfoInteger.TradeAllowed => 1L,
        Mql5AccountInfoInteger.TradeExpert => 1L,
        Mql5AccountInfoInteger.Leverage => options.Leverage,
        Mql5AccountInfoInteger.CurrencyDigits => 2L,
        Mql5AccountInfoInteger.LimitOrders => options.MaxPendingOrders,
        Mql5AccountInfoInteger.MarginStopoutMode => 0L, // ACCOUNT_STOPOUT_MODE_PERCENT
        Mql5AccountInfoInteger.FifoClose => 0L,
        Mql5AccountInfoInteger.HedgeAllowed => options.MarginMode == Mql5MarginMode.Hedging ? 1L : 0L,
        Mql5AccountInfoInteger.MarginMode => (long)options.MarginMode,
        _ => 0L,
    };

    /// <summary>Calculates simulated margin using the same symbol contract and leverage as fills.</summary>
    public bool OrderCalcMargin(int orderType, string symbol, double volume, double price, out double margin)
    {
        margin = 0.0;
        if (!IsKnownSymbol(symbol) || orderType is < 0 or > 7
            || !double.IsFinite(volume) || volume <= 0.0
            || !double.IsFinite(price) || price <= 0.0
            || options.Leverage <= 0)
        {
            return false;
        }

        margin = options.Symbol.MarginOf(volume, price, options.Leverage);
        return double.IsFinite(margin) && margin >= 0.0;
    }

    /// <summary>Calculates directional simulated profit using the same symbol contract as fills.</summary>
    public bool OrderCalcProfit(
        int orderType,
        string symbol,
        double volume,
        double priceOpen,
        double priceClose,
        out double profit)
    {
        profit = 0.0;
        if (!IsKnownSymbol(symbol) || orderType is < 0 or > 7
            || !double.IsFinite(volume) || volume <= 0.0
            || !double.IsFinite(priceOpen) || priceOpen <= 0.0
            || !double.IsFinite(priceClose) || priceClose <= 0.0)
        {
            return false;
        }

        bool buy = orderType is 0 or 2 or 4 or 6;
        double delta = buy ? priceClose - priceOpen : priceOpen - priceClose;
        profit = options.Symbol.ProfitOf(delta, volume);
        return double.IsFinite(profit);
    }

    /// <summary>Gets the deposit currency of the simulated account.</summary>
    public string AccountCurrency => options.DepositCurrency;

    /// <inheritdoc />
    public int PositionsTotal() => broker.Positions.Count;

    /// <inheritdoc />
    public bool PositionSelect(string symbol)
    {
        selected = broker.FindPositionBySymbol(string.IsNullOrEmpty(symbol) ? Symbol : symbol);
        return selected is not null;
    }

    /// <summary>Selects a position by ticket.</summary>
    public bool PositionSelectByTicket(long ticket)
    {
        selected = broker.FindPosition(ticket);
        return selected is not null;
    }

    /// <summary>
    /// Returns the ticket of the position at <paramref name="index"/> and selects it, mirroring
    /// <c>PositionGetTicket</c>. Returns zero when the index is out of range.
    /// </summary>
    public long PositionGetTicket(int index)
    {
        if (index < 0 || index >= broker.Positions.Count)
        {
            selected = null;
            return 0L;
        }

        selected = broker.Positions[index];
        return selected.Ticket;
    }

    /// <inheritdoc />
    public double PositionGetDouble(int propertyId)
    {
        if (selected is null)
        {
            return 0.0;
        }

        return propertyId switch
        {
            Mql5PositionDouble.Volume => selected.Volume,
            Mql5PositionDouble.PriceOpen => selected.PriceOpen,
            Mql5PositionDouble.StopLoss => selected.StopLoss,
            Mql5PositionDouble.TakeProfit => selected.TakeProfit,
            Mql5PositionDouble.PriceCurrent => selected.PriceCurrent,
            Mql5PositionDouble.Commission => selected.Commission,
            Mql5PositionDouble.Swap => selected.Swap,
            Mql5PositionDouble.Profit => selected.Profit,
            _ => 0.0,
        };
    }

    /// <inheritdoc />
    public long PositionGetInteger(int propertyId)
    {
        if (selected is null)
        {
            return 0L;
        }

        return propertyId switch
        {
            Mql5PositionInteger.Ticket => selected.Ticket,
            Mql5PositionInteger.Time => ToUnixSeconds(selected.TimeOpen),
            Mql5PositionInteger.Type => (long)selected.Type,
            Mql5PositionInteger.Magic => selected.Magic,
            Mql5PositionInteger.Identifier => selected.Ticket,
            _ => 0L,
        };
    }

    /// <summary>Reads the symbol of the selected position.</summary>
    public string PositionGetSymbol() => selected?.Symbol ?? string.Empty;

    /// <inheritdoc />
    public bool OrderSend(Mql5TradeRequest request, out Mql5TradeResult result) =>
        broker.Send(request, out result);

    /// <inheritdoc />
    public int IndicatorHandle(string name, params object[] parameters)
    {
        object?[] arguments = parameters ?? [];
        string key = Mql5IndicatorFactory.BuildKey(name, arguments);

        if (indicatorHandles.TryGetValue(key, out int existing))
        {
            return existing;
        }

        IMql5Indicator? indicator = Mql5IndicatorFactory.Create(name, arguments);
        if (indicator is null)
        {
            return -1;
        }

        // Back-fill so an indicator allocated mid-run is aligned with the bars already replayed.
        foreach (Mql5Bar bar in bars)
        {
            indicator.Append(bar);
        }

        indicators.Add(indicator);
        int handle = indicators.Count;
        indicatorHandles[key] = handle;
        return handle;
    }

    /// <summary>Returns the indicator behind a handle, or <see langword="null"/>.</summary>
    public IMql5Indicator? ResolveIndicator(int handle) =>
        handle >= 1 && handle <= indicators.Count ? indicators[handle - 1] : null;

    /// <inheritdoc />
    public int CopyBuffer(int handle, int bufferNum, int start, int count, double[] target)
    {
        if (target is null || count <= 0 || start < 0 || target.Length < count)
        {
            return -1;
        }

        IMql5Indicator? indicator = ResolveIndicator(handle);
        if (indicator is null || bufferNum < 0 || bufferNum >= indicator.BufferCount)
        {
            return -1;
        }

        if (indicator.Count < start + count)
        {
            return -1;
        }

        for (int index = 0; index < count; index++)
        {
            int back = start + count - 1 - index;
            target[index] = indicator.Value(bufferNum, back);
        }

        return count;
    }

    private static long ToUnixSeconds(DateTime value) =>
        (long)(DateTime.SpecifyKind(value, DateTimeKind.Utc) - DateTime.UnixEpoch).TotalSeconds;

    private bool IsKnownSymbol(string symbol) =>
        string.IsNullOrEmpty(symbol) || string.Equals(symbol, Symbol, StringComparison.Ordinal);

    private Mql5Bar BarAt(int shift)
    {
        int index = bars.Count - 1 - shift;
        return index < 0 || index >= bars.Count ? default : bars[index];
    }
}
