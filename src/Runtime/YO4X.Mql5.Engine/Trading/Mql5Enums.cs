namespace YO4X.Mql5.Engine.Trading;

/// <summary>Mirrors <c>ENUM_ORDER_TYPE</c>.</summary>
public enum Mql5OrderType
{
    /// <summary>Market buy.</summary>
    Buy = 0,

    /// <summary>Market sell.</summary>
    Sell = 1,

    /// <summary>Buy limit: rests below the market and fills when the ask falls to it.</summary>
    BuyLimit = 2,

    /// <summary>Sell limit: rests above the market and fills when the bid rises to it.</summary>
    SellLimit = 3,

    /// <summary>Buy stop: rests above the market and fills when the ask rises to it.</summary>
    BuyStop = 4,

    /// <summary>Sell stop: rests below the market and fills when the bid falls to it.</summary>
    SellStop = 5,
}

/// <summary>Mirrors <c>ENUM_POSITION_TYPE</c>.</summary>
public enum Mql5PositionType
{
    /// <summary>Long position.</summary>
    Buy = 0,

    /// <summary>Short position.</summary>
    Sell = 1,
}

/// <summary>Mirrors <c>ENUM_TRADE_REQUEST_ACTIONS</c>.</summary>
public enum Mql5TradeAction
{
    /// <summary>Immediate execution (<c>TRADE_ACTION_DEAL</c>).</summary>
    Deal = 1,

    /// <summary>Place a pending order (<c>TRADE_ACTION_PENDING</c>).</summary>
    Pending = 5,

    /// <summary>Change stop loss / take profit of an open position (<c>TRADE_ACTION_SLTP</c>).</summary>
    Sltp = 6,

    /// <summary>Change the parameters of a pending order (<c>TRADE_ACTION_MODIFY</c>).</summary>
    Modify = 7,

    /// <summary>Delete a pending order (<c>TRADE_ACTION_REMOVE</c>).</summary>
    Remove = 8,
}

/// <summary>Account position accounting mode.</summary>
public enum Mql5MarginMode
{
    /// <summary>One position per symbol; opposite deals net against it.</summary>
    Netting = 0,

    /// <summary>Exchange-cleared accounting.</summary>
    Exchange = 1,

    /// <summary>Independent positions per deal; opposing positions coexist.</summary>
    Hedging = 2,
}

/// <summary>Why a position or order left the book.</summary>
public enum Mql5CloseReason
{
    /// <summary>Closed by an explicit strategy request.</summary>
    Strategy = 0,

    /// <summary>Closed because the stop loss was touched.</summary>
    StopLoss = 1,

    /// <summary>Closed because the take profit was touched.</summary>
    TakeProfit = 2,

    /// <summary>Netted against an opposing deal in netting mode.</summary>
    Netting = 3,

    /// <summary>Force-closed by the margin stop out.</summary>
    StopOut = 4,

    /// <summary>Closed because the run finished while the position was open.</summary>
    EndOfRun = 5,
}

/// <summary>Classifies an entry in the run's order journal.</summary>
public enum Mql5OrderEventKind
{
    /// <summary>A request was rejected; the event carries the retcode.</summary>
    Rejected = 0,

    /// <summary>A position was opened.</summary>
    PositionOpened = 1,

    /// <summary>A position was closed in whole or in part.</summary>
    PositionClosed = 2,

    /// <summary>The stop loss or take profit of a position changed.</summary>
    PositionModified = 3,

    /// <summary>A pending order was placed.</summary>
    PendingPlaced = 4,

    /// <summary>A pending order's price or stops changed.</summary>
    PendingModified = 5,

    /// <summary>A pending order was deleted.</summary>
    PendingRemoved = 6,

    /// <summary>A pending order was touched and converted into a position.</summary>
    PendingActivated = 7,

    /// <summary>The margin stop out fired.</summary>
    StopOut = 8,

    /// <summary>The per-tick order cap fired; further requests on that tick were rejected.</summary>
    OrdersPerTickCapReached = 9,

    /// <summary>The total tick cap fired; the run stopped early.</summary>
    TickCapReached = 10,

    /// <summary>The strategy threw. The run stopped and the message is captured.</summary>
    StrategyFault = 11,

    /// <summary>The strategy's <c>OnInit</c> returned a non-success code.</summary>
    InitFailed = 12,
}

/// <summary>Mirrors the MQL5 moving average methods (<c>ENUM_MA_METHOD</c>).</summary>
public enum Mql5MaMethod
{
    /// <summary>Simple moving average.</summary>
    Sma = 0,

    /// <summary>Exponential moving average.</summary>
    Ema = 1,

    /// <summary>Smoothed (Wilder) moving average.</summary>
    Smma = 2,

    /// <summary>Linear weighted moving average.</summary>
    Lwma = 3,
}

/// <summary>Mirrors the MQL5 applied price constants (<c>ENUM_APPLIED_PRICE</c>).</summary>
public enum Mql5AppliedPrice
{
    /// <summary>Close price.</summary>
    Close = 1,

    /// <summary>Open price.</summary>
    Open = 2,

    /// <summary>High price.</summary>
    High = 3,

    /// <summary>Low price.</summary>
    Low = 4,

    /// <summary>(high + low) / 2.</summary>
    Median = 5,

    /// <summary>(high + low + close) / 3.</summary>
    Typical = 6,

    /// <summary>(high + low + 2 * close) / 4.</summary>
    Weighted = 7,
}
