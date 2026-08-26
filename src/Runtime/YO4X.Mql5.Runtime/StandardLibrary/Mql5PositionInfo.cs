namespace YO4X.Mql5.Runtime;

/// <summary>
/// The MQL5 standard library <c>CPositionInfo</c>, from <c>&lt;Trade/PositionInfo.mqh&gt;</c>.
/// </summary>
/// <remarks>
/// The class is a reader over whichever position is currently selected. Selection is global state
/// in MQL5 — <c>PositionSelect</c> sets it and every <c>PositionGet*</c> reads it — so this type
/// holds no copy of the position; it selects, then reads through. Caching the fields at selection
/// time would look tidier and would return stale values to any strategy that selects a second
/// position between reads, which is the normal shape of a loop over open positions.
/// </remarks>
public sealed class Mql5PositionInfo(IMql5Runtime runtime)
{
    // The only state this class carries: the snapshot StoreState takes, which CheckState compares
    // against. WRONG_VALUE is the shipped class's initial type, so a CheckState with no prior
    // StoreState reports a change rather than claiming a match against a position never seen.
    private int storedType = Mql5Constants.WrongValue;
    private double storedVolume;
    private double storedPriceOpen;
    private double storedStopLoss;
    private double storedTakeProfit;

    /// <summary><c>Select</c>: selects the position on a symbol.</summary>
    public bool Select(string? symbol) => runtime.PositionSelect(symbol);

    /// <summary><c>SelectByIndex</c>: selects the position at an index in the open list.</summary>
    public bool SelectByIndex(int index) => runtime.PositionGetTicket(index) != 0;

    /// <summary><c>SelectByTicket</c>.</summary>
    public bool SelectByTicket(ulong ticket) => runtime.PositionSelectByTicket(ticket);

    /// <summary><c>Ticket</c>.</summary>
    public ulong Ticket() => (ulong)runtime.PositionGetInteger(Mql5TradeConstants.PositionTicket);

    /// <summary><c>Time</c>: the open time, as seconds since 1970.</summary>
    public long Time() => runtime.PositionGetInteger(Mql5TradeConstants.PositionTime);

    /// <summary><c>PositionType</c>.</summary>
    public int PositionType() => (int)runtime.PositionGetInteger(Mql5TradeConstants.PositionType);

    /// <summary><c>Type</c>, the alias the shipped class also offers.</summary>
    public int Type() => PositionType();

    /// <summary><c>Magic</c>.</summary>
    public long Magic() => runtime.PositionGetInteger(Mql5TradeConstants.PositionMagic);

    /// <summary><c>Volume</c>.</summary>
    public double Volume() => runtime.PositionGetDouble(Mql5TradeConstants.PositionVolume);

    /// <summary><c>PriceOpen</c>.</summary>
    public double PriceOpen() => runtime.PositionGetDouble(Mql5TradeConstants.PositionPriceOpen);

    /// <summary><c>PriceCurrent</c>.</summary>
    public double PriceCurrent() => runtime.PositionGetDouble(Mql5TradeConstants.PositionPriceCurrent);

    /// <summary><c>StopLoss</c>.</summary>
    public double StopLoss() => runtime.PositionGetDouble(Mql5TradeConstants.PositionStopLoss);

    /// <summary><c>TakeProfit</c>.</summary>
    public double TakeProfit() => runtime.PositionGetDouble(Mql5TradeConstants.PositionTakeProfit);

    /// <summary><c>Profit</c>.</summary>
    public double Profit() => runtime.PositionGetDouble(Mql5TradeConstants.PositionProfit);

    /// <summary><c>Swap</c>.</summary>
    public double Swap() => runtime.PositionGetDouble(Mql5TradeConstants.PositionSwap);

    /// <summary><c>Symbol</c>.</summary>
    public string Symbol() => runtime.PositionGetString(Mql5TradeConstants.PositionSymbol);

    /// <summary><c>Comment</c>.</summary>
    public string Comment() => runtime.PositionGetString(Mql5TradeConstants.PositionComment);

    /// <summary><c>Commission</c>.</summary>
    /// <remarks>
    /// The shipped class stopped reading <c>POSITION_COMMISSION</c> and now raises a user error
    /// and returns zero, MetaQuotes having deprecated the property. The strategies in the corpus
    /// predate that and add this figure into their own accounting, and the broker behind this
    /// runtime does model a per-position commission, so the property is read: returning zero here
    /// would understate the cost of every position rather than announce that it cannot be had.
    /// </remarks>
    public double Commission() => runtime.PositionGetDouble(Mql5TradeConstants.PositionCommission);

    /// <summary><c>Identifier</c>: the position id deals refer to, which is not the ticket.</summary>
    public long Identifier() => runtime.PositionGetInteger(Mql5TradeConstants.PositionIdentifier);

    /// <summary><c>TimeMsc</c>: the open time, in milliseconds since 1970.</summary>
    public long TimeMsc() => runtime.PositionGetInteger(Mql5TradeConstants.PositionTimeMsc);

    /// <summary><c>TimeUpdate</c>: when the position was last changed, in seconds since 1970.</summary>
    public long TimeUpdate() => runtime.PositionGetInteger(Mql5TradeConstants.PositionTimeUpdate);

    /// <summary><c>TimeUpdateMsc</c>, in milliseconds since 1970.</summary>
    public long TimeUpdateMsc() => runtime.PositionGetInteger(Mql5TradeConstants.PositionTimeUpdateMsc);

    /// <summary><c>SelectByMagic</c>: selects the first position on a symbol carrying a magic number.</summary>
    /// <remarks>
    /// The scan walks <c>PositionGetSymbol</c>, which selects each position as it goes, so the
    /// selection this leaves behind is the matching one when it returns true — and the last
    /// position examined when it returns false, exactly as the shipped class leaves it.
    /// </remarks>
    public bool SelectByMagic(string? symbol, ulong magic)
    {
        int total = runtime.PositionsTotal();

        for (int index = 0; index < total; index++)
        {
            if (runtime.PositionGetSymbol(index) == symbol
                && (ulong)runtime.PositionGetInteger(Mql5TradeConstants.PositionMagic) == magic)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><c>TypeDescription</c>.</summary>
    public string TypeDescription() => Mql5TradeConstants.DescribePositionType(PositionType());

    /// <summary><c>StoreState</c>: remembers the selected position's shape for <see cref="CheckState"/>.</summary>
    public void StoreState()
    {
        storedType = PositionType();
        storedVolume = Volume();
        storedPriceOpen = PriceOpen();
        storedStopLoss = StopLoss();
        storedTakeProfit = TakeProfit();
    }

    /// <summary><c>CheckState</c>: whether the position changed since <see cref="StoreState"/>.</summary>
    /// <remarks>
    /// The comparison is exact, not tolerant. A stop moved by a trailing routine differs in its
    /// last digit and that is precisely the change the caller is watching for; a tolerance here
    /// would swallow it.
    /// </remarks>
    public bool CheckState()
        => storedType != PositionType()
            || storedVolume != Volume()
            || storedPriceOpen != PriceOpen()
            || storedStopLoss != StopLoss()
            || storedTakeProfit != TakeProfit();
}
