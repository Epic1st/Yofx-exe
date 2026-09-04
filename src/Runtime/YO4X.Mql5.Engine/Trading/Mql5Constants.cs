namespace YO4X.Mql5.Engine.Trading;

/// <summary>
/// MQL5 server return codes. A misbehaving strategy receives one of these; the engine never
/// throws out of <c>OrderSend</c>.
/// </summary>
public static class Mql5TradeRetcode
{
    /// <summary>Request completed (<c>TRADE_RETCODE_DONE</c>).</summary>
    public const int Done = 10009;

    /// <summary>Request completed partially (<c>TRADE_RETCODE_DONE_PARTIAL</c>).</summary>
    public const int DonePartial = 10010;

    /// <summary>Request rejected (<c>TRADE_RETCODE_REJECT</c>).</summary>
    public const int Reject = 10006;

    /// <summary>Request cancelled by the trader (<c>TRADE_RETCODE_CANCEL</c>).</summary>
    public const int Cancel = 10007;

    /// <summary>Common processing error (<c>TRADE_RETCODE_ERROR</c>).</summary>
    public const int Error = 10011;

    /// <summary>Invalid request (<c>TRADE_RETCODE_INVALID</c>).</summary>
    public const int Invalid = 10013;

    /// <summary>Invalid volume (<c>TRADE_RETCODE_INVALID_VOLUME</c>).</summary>
    public const int InvalidVolume = 10014;

    /// <summary>Invalid price (<c>TRADE_RETCODE_INVALID_PRICE</c>).</summary>
    public const int InvalidPrice = 10015;

    /// <summary>Invalid stops (<c>TRADE_RETCODE_INVALID_STOPS</c>).</summary>
    public const int InvalidStops = 10016;

    /// <summary>Not enough money (<c>TRADE_RETCODE_NO_MONEY</c>).</summary>
    public const int NoMoney = 10019;

    /// <summary>No quotes to process the request (<c>TRADE_RETCODE_PRICE_OFF</c>).</summary>
    public const int PriceOff = 10021;

    /// <summary>No changes in the request (<c>TRADE_RETCODE_NO_CHANGES</c>).</summary>
    public const int NoChanges = 10025;

    /// <summary>The number of pending orders reached the limit (<c>TRADE_RETCODE_LIMIT_ORDERS</c>).</summary>
    public const int LimitOrders = 10033;

    /// <summary>A close volume exceeds the current position volume (<c>TRADE_RETCODE_INVALID_CLOSE_VOLUME</c>).</summary>
    public const int InvalidCloseVolume = 10038;

    /// <summary>The position specified has already been closed (<c>TRADE_RETCODE_POSITION_CLOSED</c>).</summary>
    public const int PositionClosed = 10036;
}

/// <summary>Return codes from <c>OnInit</c>.</summary>
public static class Mql5InitCode
{
    /// <summary><c>INIT_SUCCEEDED</c>.</summary>
    public const int Succeeded = 0;

    /// <summary><c>INIT_FAILED</c>.</summary>
    public const int Failed = 1;

    /// <summary><c>INIT_PARAMETERS_INCORRECT</c>.</summary>
    public const int ParametersIncorrect = 32767;
}

/// <summary>Reasons passed to <c>OnDeinit</c>.</summary>
public static class Mql5DeinitReason
{
    /// <summary><c>REASON_PROGRAM</c> - the run ended normally.</summary>
    public const int Program = 0;

    /// <summary><c>REASON_REMOVE</c>.</summary>
    public const int Remove = 1;

    /// <summary><c>REASON_INITFAILED</c>.</summary>
    public const int InitFailed = 8;

    /// <summary><c>REASON_CLOSE</c> - the host aborted the run.</summary>
    public const int Close = 9;
}


/// <summary>
/// Property identifiers accepted by <c>SymbolInfoDouble</c>. Generated code should reference these
/// constants rather than raw literals so the engine and the code generator cannot drift.
///
/// The values are the ones MQL5 itself assigns, measured from the compiler and recorded in
/// <c>Mql5BuiltinConstants</c>. They are not free to choose: generated code passes the value a
/// strategy's own source names, so an identifier the engine numbers differently is not a mismatch
/// it can detect — it is a property read that silently answers with the wrong field.
/// </summary>
public static class Mql5SymbolInfoDouble
{
    /// <summary><c>SYMBOL_BID</c>.</summary>
    public const int Bid = 1;

    /// <summary><c>SYMBOL_ASK</c>.</summary>
    public const int Ask = 4;

    /// <summary><c>SYMBOL_LAST</c>.</summary>
    public const int Last = 7;

    /// <summary><c>SYMBOL_POINT</c>.</summary>
    public const int Point = 16;

    /// <summary><c>SYMBOL_TRADE_TICK_VALUE</c>.</summary>
    public const int TickValue = 26;

    /// <summary><c>SYMBOL_TRADE_TICK_SIZE</c>.</summary>
    public const int TickSize = 27;

    /// <summary><c>SYMBOL_TRADE_CONTRACT_SIZE</c>.</summary>
    public const int ContractSize = 28;

    /// <summary><c>SYMBOL_VOLUME_MIN</c>.</summary>
    public const int VolumeMin = 34;

    /// <summary><c>SYMBOL_VOLUME_MAX</c>.</summary>
    public const int VolumeMax = 35;

    /// <summary><c>SYMBOL_VOLUME_STEP</c>.</summary>
    public const int VolumeStep = 36;

    /// <summary><c>SYMBOL_VOLUME_LIMIT</c>; zero means no aggregate directional limit.</summary>
    public const int VolumeLimit = 55;

    /// <summary><c>SYMBOL_SWAP_LONG</c>.</summary>
    public const int SwapLong = 38;

    /// <summary><c>SYMBOL_SWAP_SHORT</c>.</summary>
    public const int SwapShort = 39;
}

/// <summary>Property identifiers accepted by <c>SymbolInfoInteger</c>.</summary>
public static class Mql5SymbolInfoInteger
{
    /// <summary><c>SYMBOL_SELECT</c>.</summary>
    public const int Select = 0;

    /// <summary><c>SYMBOL_TIME</c> as a Unix timestamp.</summary>
    public const int Time = 15;

    /// <summary><c>SYMBOL_DIGITS</c>.</summary>
    public const int Digits = 17;

    /// <summary><c>SYMBOL_SPREAD</c> in points.</summary>
    public const int Spread = 18;

    /// <summary><c>SYMBOL_TRADE_STOPS_LEVEL</c>.</summary>
    public const int StopsLevel = 31;

    /// <summary><c>SYMBOL_TRADE_FREEZE_LEVEL</c>.</summary>
    public const int FreezeLevel = 32;

    /// <summary><c>SYMBOL_TRADE_MODE</c>.</summary>
    public const int TradeMode = 30;

    /// <summary><c>SYMBOL_TRADE_EXEMODE</c>.</summary>
    public const int TradeExecutionMode = 33;

    /// <summary><c>SYMBOL_FILLING_MODE</c>.</summary>
    public const int FillingMode = 50;

    /// <summary><c>SYMBOL_EXPIRATION_MODE</c>.</summary>
    public const int ExpirationMode = 49;

    /// <summary><c>SYMBOL_ORDER_MODE</c>.</summary>
    public const int OrderMode = 71;
}

/// <summary>Property identifiers accepted by <c>AccountInfoDouble</c>.</summary>
public static class Mql5AccountInfoDouble
{
    /// <summary><c>ACCOUNT_BALANCE</c>.</summary>
    public const int Balance = 37;

    /// <summary><c>ACCOUNT_CREDIT</c>.</summary>
    public const int Credit = 38;

    /// <summary><c>ACCOUNT_PROFIT</c> - floating profit of open positions.</summary>
    public const int Profit = 39;

    /// <summary><c>ACCOUNT_EQUITY</c>.</summary>
    public const int Equity = 40;

    /// <summary><c>ACCOUNT_MARGIN</c>.</summary>
    public const int Margin = 41;

    /// <summary><c>ACCOUNT_MARGIN_FREE</c>.</summary>
    public const int MarginFree = 42;

    /// <summary><c>ACCOUNT_MARGIN_LEVEL</c> in percent.</summary>
    public const int MarginLevel = 43;

    /// <summary><c>ACCOUNT_MARGIN_SO_SO</c> - the stop out level in percent.</summary>
    public const int MarginStopOut = 46;
}

/// <summary>Property identifiers accepted by <c>AccountInfoInteger</c>.</summary>
public static class Mql5AccountInfoInteger
{
    /// <summary><c>ACCOUNT_LOGIN</c>.</summary>
    public const int Login = 0;

    /// <summary><c>ACCOUNT_TRADE_MODE</c>.</summary>
    public const int TradeMode = 32;

    /// <summary><c>ACCOUNT_TRADE_ALLOWED</c>.</summary>
    public const int TradeAllowed = 33;

    /// <summary><c>ACCOUNT_TRADE_EXPERT</c>.</summary>
    public const int TradeExpert = 34;

    /// <summary><c>ACCOUNT_LEVERAGE</c>.</summary>
    public const int Leverage = 35;

    /// <summary><c>ACCOUNT_MARGIN_MODE</c>.</summary>
    public const int MarginMode = 53;

    /// <summary><c>ACCOUNT_LIMIT_ORDERS</c>.</summary>
    public const int LimitOrders = 47;

    /// <summary><c>ACCOUNT_MARGIN_SO_MODE</c>.</summary>
    public const int MarginStopoutMode = 44;

    /// <summary><c>ACCOUNT_FIFO_CLOSE</c>.</summary>
    public const int FifoClose = 55;

    /// <summary><c>ACCOUNT_HEDGE_ALLOWED</c>.</summary>
    public const int HedgeAllowed = 56;

    /// <summary><c>ACCOUNT_CURRENCY_DIGITS</c>.</summary>
    public const int CurrencyDigits = 54;
}

/// <summary>Property identifiers accepted by <c>PositionGetDouble</c>.</summary>
public static class Mql5PositionDouble
{
    /// <summary><c>POSITION_VOLUME</c>.</summary>
    public const int Volume = 3;

    /// <summary><c>POSITION_PRICE_OPEN</c>.</summary>
    public const int PriceOpen = 4;

    /// <summary><c>POSITION_PRICE_CURRENT</c>.</summary>
    public const int PriceCurrent = 5;

    /// <summary><c>POSITION_SL</c>.</summary>
    public const int StopLoss = 6;

    /// <summary><c>POSITION_TP</c>.</summary>
    public const int TakeProfit = 7;

    /// <summary><c>POSITION_COMMISSION</c>.</summary>
    public const int Commission = 8;

    /// <summary><c>POSITION_SWAP</c>.</summary>
    public const int Swap = 9;

    /// <summary><c>POSITION_PROFIT</c>.</summary>
    public const int Profit = 10;
}

/// <summary>Property identifiers accepted by <c>PositionGetInteger</c>.</summary>
public static class Mql5PositionInteger
{
    /// <summary><c>POSITION_TIME</c>.</summary>
    public const int Time = 1;

    /// <summary><c>POSITION_TYPE</c>.</summary>
    public const int Type = 2;

    /// <summary><c>POSITION_MAGIC</c>.</summary>
    public const int Magic = 12;

    /// <summary><c>POSITION_IDENTIFIER</c>.</summary>
    public const int Identifier = 13;

    /// <summary><c>POSITION_TICKET</c>.</summary>
    public const int Ticket = 17;
}
