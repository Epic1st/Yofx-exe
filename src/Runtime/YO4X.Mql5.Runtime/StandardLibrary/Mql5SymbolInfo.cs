namespace YO4X.Mql5.Runtime;

/// <summary>
/// The MQL5 standard library <c>CSymbolInfo</c>, from <c>&lt;Trade/SymbolInfo.mqh&gt;</c>.
/// </summary>
/// <remarks>
/// The shipped class caches the bid and ask, and <c>RefreshRates</c> is what refills them. That
/// caching is reproduced rather than skipped, because a strategy that calls <c>RefreshRates</c>
/// once per tick and reads <c>Ask()</c> several times expects those reads to agree with each
/// other; serving each one a fresh quote would let a stop and a target be computed from prices
/// that never coexisted.
/// </remarks>
public sealed class Mql5SymbolInfo(IMql5Runtime runtime)
{
    private string name = string.Empty;
    private double bid;
    private double ask;
    private long spread;

    /// <summary><c>Name</c>: reads the symbol this object describes.</summary>
    public string Name() => name.Length > 0 ? name : runtime.Symbol();

    /// <summary><c>Name</c>: binds this object to a symbol.</summary>
    public bool Name(string? value)
    {
        name = value ?? string.Empty;
        return RefreshRates();
    }

    /// <summary><c>Refresh</c>. The contract specification is read through on demand here, so
    /// there is nothing to reload; the rates are refreshed so the call is not a silent no-op.</summary>
    public bool Refresh() => RefreshRates();

    /// <summary><c>RefreshRates</c>: reloads the cached bid and ask.</summary>
    public bool RefreshRates()
    {
        string symbol = Name();
        bid = runtime.SymbolInfoDouble(symbol, Mql5TradeConstants.SymbolBid);
        ask = runtime.SymbolInfoDouble(symbol, Mql5TradeConstants.SymbolAsk);
        spread = runtime.SymbolInfoInteger(symbol, Mql5TradeConstants.SymbolSpread);

        // The shipped class reports failure when either side is missing, which is how a strategy
        // detects a symbol that is not yet subscribed.
        return bid > 0.0 && ask > 0.0;
    }

    /// <summary><c>Bid</c>.</summary>
    public double Bid() => bid;

    /// <summary><c>Ask</c>.</summary>
    public double Ask() => ask;

    /// <summary><c>Spread</c>, in points.</summary>
    public long Spread() => spread;

    /// <summary><c>Point</c>.</summary>
    public double Point() => runtime.SymbolInfoDouble(Name(), Mql5TradeConstants.SymbolPoint);

    /// <summary><c>Digits</c>.</summary>
    public int Digits() => (int)runtime.SymbolInfoInteger(Name(), Mql5TradeConstants.SymbolDigits);

    /// <summary><c>TickSize</c>.</summary>
    public double TickSize() => runtime.SymbolInfoDouble(Name(), Mql5TradeConstants.SymbolTickSize);

    /// <summary><c>TickValue</c>.</summary>
    public double TickValue() => runtime.SymbolInfoDouble(Name(), Mql5TradeConstants.SymbolTickValue);

    /// <summary><c>ContractSize</c>.</summary>
    public double ContractSize() => runtime.SymbolInfoDouble(Name(), Mql5TradeConstants.SymbolContractSize);

    /// <summary><c>LotsMin</c>.</summary>
    public double LotsMin() => runtime.SymbolInfoDouble(Name(), Mql5TradeConstants.SymbolVolumeMin);

    /// <summary><c>LotsMax</c>.</summary>
    public double LotsMax() => runtime.SymbolInfoDouble(Name(), Mql5TradeConstants.SymbolVolumeMax);

    /// <summary><c>LotsStep</c>.</summary>
    public double LotsStep() => runtime.SymbolInfoDouble(Name(), Mql5TradeConstants.SymbolVolumeStep);

    /// <summary><c>LotsLimit</c>: the total volume one direction may hold, or zero for no limit.</summary>
    public double LotsLimit() => runtime.SymbolInfoDouble(Name(), Mql5TradeConstants.SymbolVolumeLimit);

    /// <summary><c>StopsLevel</c>: the minimum distance from price a stop may sit, in points.</summary>
    public int StopsLevel() => (int)runtime.SymbolInfoInteger(Name(), Mql5TradeConstants.SymbolStopsLevel);

    /// <summary><c>FreezeLevel</c>: how close to price an order may no longer be modified, in points.</summary>
    public int FreezeLevel() => (int)runtime.SymbolInfoInteger(Name(), Mql5TradeConstants.SymbolFreezeLevel);

    /// <summary><c>TickValueProfit</c>.</summary>
    public double TickValueProfit() => runtime.SymbolInfoDouble(Name(), Mql5TradeConstants.SymbolTickValueProfit);

    /// <summary><c>TickValueLoss</c>.</summary>
    public double TickValueLoss() => runtime.SymbolInfoDouble(Name(), Mql5TradeConstants.SymbolTickValueLoss);

    /// <summary><c>SwapLong</c>.</summary>
    public double SwapLong() => runtime.SymbolInfoDouble(Name(), Mql5TradeConstants.SymbolSwapLong);

    /// <summary><c>SwapShort</c>.</summary>
    public double SwapShort() => runtime.SymbolInfoDouble(Name(), Mql5TradeConstants.SymbolSwapShort);

    /// <summary><c>SwapMode</c>.</summary>
    public int SwapMode() => (int)runtime.SymbolInfoInteger(Name(), Mql5TradeConstants.SymbolSwapMode);

    /// <summary><c>SwapRollover3days</c>: the weekday charged triple swap.</summary>
    public int SwapRollover3days() => (int)runtime.SymbolInfoInteger(Name(), Mql5TradeConstants.SymbolSwapRollover3Days);

    /// <summary><c>MarginInitial</c>.</summary>
    public double MarginInitial() => runtime.SymbolInfoDouble(Name(), Mql5TradeConstants.SymbolMarginInitial);

    /// <summary><c>MarginMaintenance</c>.</summary>
    public double MarginMaintenance() => runtime.SymbolInfoDouble(Name(), Mql5TradeConstants.SymbolMarginMaintenance);

    /// <summary><c>MarginHedged</c>.</summary>
    public double MarginHedged() => runtime.SymbolInfoDouble(Name(), Mql5TradeConstants.SymbolMarginHedged);

    /// <summary><c>MarginHedgedUseLeg</c>.</summary>
    public bool MarginHedgedUseLeg()
        => runtime.SymbolInfoInteger(Name(), Mql5TradeConstants.SymbolMarginHedgedUseLeg) != 0;

    /// <summary><c>TradeMode</c>: whether the symbol is disabled, one-way, close-only or open.</summary>
    public int TradeMode() => (int)runtime.SymbolInfoInteger(Name(), Mql5TradeConstants.SymbolTradeMode);

    /// <summary><c>TradeCalcMode</c>: how profit and margin are computed for this symbol.</summary>
    public int TradeCalcMode() => (int)runtime.SymbolInfoInteger(Name(), Mql5TradeConstants.SymbolTradeCalcMode);

    /// <summary><c>TradeExecution</c>: request, instant, market or exchange.</summary>
    public int TradeExecution()
        => (int)runtime.SymbolInfoInteger(Name(), Mql5TradeConstants.SymbolTradeExecutionMode);

    /// <summary><c>TradeExecutionDescription</c>.</summary>
    public string TradeExecutionDescription() => Mql5TradeConstants.DescribeTradeExecution(TradeExecution());

    /// <summary><c>TradeFillFlags</c>: the filling modes the symbol permits, as a bit mask.</summary>
    /// <remarks>
    /// The flags in this mask are <see cref="Mql5TradeConstants.FillingFokFlag"/> and its
    /// neighbours, which are numbered separately from the <c>ENUM_ORDER_TYPE_FILLING</c> members
    /// a request carries. Passing one where the other belongs compiles and silently picks the
    /// wrong policy.
    /// </remarks>
    public int TradeFillFlags() => (int)runtime.SymbolInfoInteger(Name(), Mql5TradeConstants.SymbolFillingMode);

    /// <summary><c>TradeTimeFlags</c>: the expiration modes the symbol permits, as a bit mask.</summary>
    public int TradeTimeFlags()
        => (int)runtime.SymbolInfoInteger(Name(), Mql5TradeConstants.SymbolExpirationMode);

    /// <summary><c>OrderMode</c>: the order types the symbol permits, as a bit mask.</summary>
    public int OrderMode() => (int)runtime.SymbolInfoInteger(Name(), Mql5TradeConstants.SymbolOrderMode);

    /// <summary><c>Select</c>: whether the symbol is in Market Watch.</summary>
    public bool Select() => runtime.SymbolInfoInteger(Name(), Mql5TradeConstants.SymbolSelected) != 0;

    /// <summary><c>Select</c>: adds the symbol to Market Watch, or removes it.</summary>
    public bool Select(bool select) => runtime.SymbolSelect(Name(), select);

    /// <summary><c>IsSynchronized</c>.</summary>
    public bool IsSynchronized() => runtime.SymbolIsSynchronized(Name());

    /// <summary><c>SpreadFloat</c>.</summary>
    public bool SpreadFloat() => runtime.SymbolInfoInteger(Name(), Mql5TradeConstants.SymbolSpreadFloat) != 0;

    /// <summary><c>TicksBookDepth</c>.</summary>
    public int TicksBookDepth()
        => (int)runtime.SymbolInfoInteger(Name(), Mql5TradeConstants.SymbolTicksBookDepth);

    /// <summary><c>Time</c>: the time of the last quote, as seconds since 1970.</summary>
    /// <remarks>
    /// The shipped class serves this from the same cached tick as <c>Bid</c> and <c>Ask</c>. There
    /// is no tick structure behind this reader, so it is read through; a caller pairing the time
    /// with the cached bid can therefore see a quote timestamp newer than the price beside it.
    /// </remarks>
    public long Time() => runtime.SymbolInfoInteger(Name(), Mql5TradeConstants.SymbolTime);

    /// <summary><c>Volume</c>: the volume of the last deal.</summary>
    public long Volume() => runtime.SymbolInfoInteger(Name(), Mql5TradeConstants.SymbolVolume);

    /// <summary><c>VolumeHigh</c>.</summary>
    public long VolumeHigh() => runtime.SymbolInfoInteger(Name(), Mql5TradeConstants.SymbolVolumeHigh);

    /// <summary><c>VolumeLow</c>.</summary>
    public long VolumeLow() => runtime.SymbolInfoInteger(Name(), Mql5TradeConstants.SymbolVolumeLow);

    /// <summary><c>BidHigh</c>.</summary>
    public double BidHigh() => runtime.SymbolInfoDouble(Name(), Mql5TradeConstants.SymbolBidHigh);

    /// <summary><c>BidLow</c>.</summary>
    public double BidLow() => runtime.SymbolInfoDouble(Name(), Mql5TradeConstants.SymbolBidLow);

    /// <summary><c>AskHigh</c>.</summary>
    public double AskHigh() => runtime.SymbolInfoDouble(Name(), Mql5TradeConstants.SymbolAskHigh);

    /// <summary><c>AskLow</c>.</summary>
    public double AskLow() => runtime.SymbolInfoDouble(Name(), Mql5TradeConstants.SymbolAskLow);

    /// <summary><c>Last</c>.</summary>
    public double Last() => runtime.SymbolInfoDouble(Name(), Mql5TradeConstants.SymbolLast);

    /// <summary><c>LastHigh</c>.</summary>
    public double LastHigh() => runtime.SymbolInfoDouble(Name(), Mql5TradeConstants.SymbolLastHigh);

    /// <summary><c>LastLow</c>.</summary>
    public double LastLow() => runtime.SymbolInfoDouble(Name(), Mql5TradeConstants.SymbolLastLow);

    /// <summary><c>StartTime</c>, as seconds since 1970. Zero for an instrument that never expires.</summary>
    public long StartTime() => runtime.SymbolInfoInteger(Name(), Mql5TradeConstants.SymbolStartTime);

    /// <summary><c>ExpirationTime</c>, as seconds since 1970.</summary>
    public long ExpirationTime()
        => runtime.SymbolInfoInteger(Name(), Mql5TradeConstants.SymbolExpirationTime);

    /// <summary><c>CurrencyBase</c>.</summary>
    public string CurrencyBase() => runtime.SymbolInfoString(Name(), Mql5TradeConstants.SymbolCurrencyBase);

    /// <summary><c>CurrencyProfit</c>.</summary>
    public string CurrencyProfit() => runtime.SymbolInfoString(Name(), Mql5TradeConstants.SymbolCurrencyProfit);

    /// <summary><c>CurrencyMargin</c>.</summary>
    public string CurrencyMargin() => runtime.SymbolInfoString(Name(), Mql5TradeConstants.SymbolCurrencyMargin);

    /// <summary><c>Bank</c>.</summary>
    public string Bank() => runtime.SymbolInfoString(Name(), Mql5TradeConstants.SymbolBank);

    /// <summary><c>Description</c>.</summary>
    public string Description() => runtime.SymbolInfoString(Name(), Mql5TradeConstants.SymbolDescription);

    /// <summary><c>Path</c>.</summary>
    public string Path() => runtime.SymbolInfoString(Name(), Mql5TradeConstants.SymbolPath);

    /// <summary><c>NormalizePrice</c>: rounds to a price the symbol can actually quote.</summary>
    /// <remarks>
    /// Rounding to the digit count alone is not enough: an instrument whose tick size is larger
    /// than one point — index CFDs and futures routinely quote in 0.25 or 0.5 steps — rejects a
    /// stop that sits between two ticks, and the rejection names an invalid price rather than the
    /// rounding that produced it. The tick size takes precedence when the symbol declares one,
    /// which is what the shipped class does.
    /// </remarks>
    public double NormalizePrice(double price)
    {
        int digits = Math.Max(0, Digits());
        double tickSize = TickSize();

        return tickSize != 0.0
            ? Math.Round(Math.Round(price / tickSize, MidpointRounding.AwayFromZero) * tickSize, digits, MidpointRounding.AwayFromZero)
            : Math.Round(price, digits, MidpointRounding.AwayFromZero);
    }
}
