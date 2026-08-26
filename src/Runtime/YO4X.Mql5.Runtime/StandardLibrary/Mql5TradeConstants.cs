using System.Globalization;

namespace YO4X.Mql5.Runtime;

/// <summary>
/// The MQL5 identifiers the standard library classes pass to this runtime.
/// </summary>
/// <remarks>
/// Every value here was measured from the MQL5 compiler and is recorded alongside the rest in
/// <c>Mql5BuiltinConstants</c>. They are restated in this assembly rather than referenced, because
/// the runtime deliberately depends on nothing: it is the layer generated strategies compile
/// against, and a dependency here would propagate into every translated assembly.
///
/// <c>StandardLibraryConstantsTests</c> holds these against the measured catalogue, so the
/// duplication cannot quietly diverge from the values the code generator emits.
/// </remarks>
public static class Mql5TradeConstants
{
    // ENUM_TRADE_REQUEST_ACTIONS. Sparse, and not guessable from ordinal position.

    /// <summary><c>TRADE_ACTION_DEAL</c>.</summary>
    public const int TradeActionDeal = 1;

    /// <summary><c>TRADE_ACTION_PENDING</c>.</summary>
    public const int TradeActionPending = 5;

    /// <summary><c>TRADE_ACTION_SLTP</c>.</summary>
    public const int TradeActionSltp = 6;

    /// <summary><c>TRADE_ACTION_MODIFY</c>.</summary>
    public const int TradeActionModify = 7;

    /// <summary><c>TRADE_ACTION_REMOVE</c>.</summary>
    public const int TradeActionRemove = 8;

    /// <summary><c>TRADE_ACTION_CLOSE_BY</c>.</summary>
    public const int TradeActionCloseBy = 10;

    // ENUM_ORDER_TYPE

    /// <summary><c>ORDER_TYPE_BUY</c>.</summary>
    public const int OrderTypeBuy = 0;

    /// <summary><c>ORDER_TYPE_SELL</c>.</summary>
    public const int OrderTypeSell = 1;

    /// <summary><c>ORDER_TYPE_BUY_LIMIT</c>.</summary>
    public const int OrderTypeBuyLimit = 2;

    /// <summary><c>ORDER_TYPE_SELL_LIMIT</c>.</summary>
    public const int OrderTypeSellLimit = 3;

    /// <summary><c>ORDER_TYPE_BUY_STOP</c>.</summary>
    public const int OrderTypeBuyStop = 4;

    /// <summary><c>ORDER_TYPE_SELL_STOP</c>.</summary>
    public const int OrderTypeSellStop = 5;

    /// <summary><c>ORDER_TYPE_BUY_STOP_LIMIT</c>.</summary>
    public const int OrderTypeBuyStopLimit = 6;

    /// <summary><c>ORDER_TYPE_SELL_STOP_LIMIT</c>.</summary>
    public const int OrderTypeSellStopLimit = 7;

    /// <summary><c>ORDER_TYPE_CLOSE_BY</c>.</summary>
    public const int OrderTypeCloseBy = 8;

    // ENUM_ORDER_TYPE_FILLING. FOK 0, IOC 1, RETURN 2, BOC 3 — measured, and contrary to the
    // common claim that BOC was inserted ahead of RETURN.

    /// <summary><c>ORDER_FILLING_FOK</c>.</summary>
    public const int OrderFillingFok = 0;

    /// <summary><c>ORDER_FILLING_IOC</c>.</summary>
    public const int OrderFillingIoc = 1;

    /// <summary><c>ORDER_FILLING_RETURN</c>.</summary>
    public const int OrderFillingReturn = 2;

    /// <summary><c>ORDER_FILLING_BOC</c>.</summary>
    public const int OrderFillingBoc = 3;

    // The permitted filling modes are a bit mask, whose flags are numbered separately from the
    // ENUM_ORDER_TYPE_FILLING members above; the two are easily and silently confused.

    /// <summary><c>SYMBOL_FILLING_MODE</c>.</summary>
    public const int SymbolFillingMode = 50;

    /// <summary><c>SYMBOL_FILLING_FOK</c>, a flag within <see cref="SymbolFillingMode"/>.</summary>
    public const int FillingFokFlag = 1;

    /// <summary><c>SYMBOL_FILLING_IOC</c>, a flag within <see cref="SymbolFillingMode"/>.</summary>
    public const int FillingIocFlag = 2;

    // ENUM_SYMBOL_INFO_DOUBLE / INTEGER

    /// <summary><c>SYMBOL_BID</c>.</summary>
    public const int SymbolBid = 1;

    /// <summary><c>SYMBOL_ASK</c>.</summary>
    public const int SymbolAsk = 4;

    /// <summary><c>SYMBOL_POINT</c>.</summary>
    public const int SymbolPoint = 16;

    /// <summary><c>SYMBOL_DIGITS</c>.</summary>
    public const int SymbolDigits = 17;

    /// <summary><c>SYMBOL_SPREAD</c>.</summary>
    public const int SymbolSpread = 18;

    /// <summary><c>SYMBOL_TRADE_TICK_VALUE</c>.</summary>
    public const int SymbolTickValue = 26;

    /// <summary><c>SYMBOL_TRADE_TICK_SIZE</c>.</summary>
    public const int SymbolTickSize = 27;

    /// <summary><c>SYMBOL_TRADE_CONTRACT_SIZE</c>.</summary>
    public const int SymbolContractSize = 28;

    /// <summary><c>SYMBOL_VOLUME_MIN</c>.</summary>
    public const int SymbolVolumeMin = 34;

    /// <summary><c>SYMBOL_VOLUME_MAX</c>.</summary>
    public const int SymbolVolumeMax = 35;

    /// <summary><c>SYMBOL_VOLUME_STEP</c>.</summary>
    public const int SymbolVolumeStep = 36;

    /// <summary><c>SYMBOL_VOLUME_LIMIT</c>: the total volume one direction may hold.</summary>
    public const int SymbolVolumeLimit = 55;

    /// <summary><c>SYMBOL_SELECT</c>: whether the symbol is in Market Watch.</summary>
    public const int SymbolSelected = 0;

    /// <summary><c>SYMBOL_BIDHIGH</c>.</summary>
    public const int SymbolBidHigh = 2;

    /// <summary><c>SYMBOL_BIDLOW</c>.</summary>
    public const int SymbolBidLow = 3;

    /// <summary><c>SYMBOL_ASKHIGH</c>.</summary>
    public const int SymbolAskHigh = 5;

    /// <summary><c>SYMBOL_ASKLOW</c>.</summary>
    public const int SymbolAskLow = 6;

    /// <summary><c>SYMBOL_LAST</c>.</summary>
    public const int SymbolLast = 7;

    /// <summary><c>SYMBOL_LASTHIGH</c>.</summary>
    public const int SymbolLastHigh = 8;

    /// <summary><c>SYMBOL_LASTLOW</c>.</summary>
    public const int SymbolLastLow = 9;

    /// <summary><c>SYMBOL_VOLUME</c>: the last deal volume.</summary>
    public const int SymbolVolume = 10;

    /// <summary><c>SYMBOL_VOLUMEHIGH</c>.</summary>
    public const int SymbolVolumeHigh = 11;

    /// <summary><c>SYMBOL_VOLUMELOW</c>.</summary>
    public const int SymbolVolumeLow = 12;

    /// <summary><c>SYMBOL_TIME</c>: the time of the last quote, in seconds since 1970.</summary>
    public const int SymbolTime = 15;

    /// <summary><c>SYMBOL_BANK</c>.</summary>
    public const int SymbolBank = 19;

    /// <summary><c>SYMBOL_DESCRIPTION</c>.</summary>
    public const int SymbolDescription = 20;

    /// <summary><c>SYMBOL_PATH</c>.</summary>
    public const int SymbolPath = 21;

    /// <summary><c>SYMBOL_CURRENCY_BASE</c>.</summary>
    public const int SymbolCurrencyBase = 22;

    /// <summary><c>SYMBOL_CURRENCY_PROFIT</c>.</summary>
    public const int SymbolCurrencyProfit = 23;

    /// <summary><c>SYMBOL_CURRENCY_MARGIN</c>.</summary>
    public const int SymbolCurrencyMargin = 24;

    /// <summary><c>SYMBOL_TICKS_BOOKDEPTH</c>.</summary>
    public const int SymbolTicksBookDepth = 25;

    /// <summary><c>SYMBOL_TRADE_CALC_MODE</c>.</summary>
    public const int SymbolTradeCalcMode = 29;

    /// <summary><c>SYMBOL_TRADE_MODE</c>.</summary>
    public const int SymbolTradeMode = 30;

    /// <summary><c>SYMBOL_TRADE_STOPS_LEVEL</c>: minimum stop distance, in points.</summary>
    public const int SymbolStopsLevel = 31;

    /// <summary><c>SYMBOL_TRADE_FREEZE_LEVEL</c>: freeze distance, in points.</summary>
    public const int SymbolFreezeLevel = 32;

    /// <summary><c>SYMBOL_TRADE_EXEMODE</c>.</summary>
    public const int SymbolTradeExecutionMode = 33;

    /// <summary><c>SYMBOL_SWAP_MODE</c>.</summary>
    public const int SymbolSwapMode = 37;

    /// <summary><c>SYMBOL_SWAP_LONG</c>.</summary>
    public const int SymbolSwapLong = 38;

    /// <summary><c>SYMBOL_SWAP_SHORT</c>.</summary>
    public const int SymbolSwapShort = 39;

    /// <summary><c>SYMBOL_SWAP_ROLLOVER3DAYS</c>: the weekday charged triple swap.</summary>
    public const int SymbolSwapRollover3Days = 40;

    /// <summary><c>SYMBOL_SPREAD_FLOAT</c>.</summary>
    public const int SymbolSpreadFloat = 41;

    /// <summary><c>SYMBOL_MARGIN_INITIAL</c>.</summary>
    public const int SymbolMarginInitial = 42;

    /// <summary><c>SYMBOL_MARGIN_MAINTENANCE</c>.</summary>
    public const int SymbolMarginMaintenance = 43;

    /// <summary><c>SYMBOL_EXPIRATION_MODE</c>: the permitted expiration modes, a bit mask.</summary>
    public const int SymbolExpirationMode = 49;

    /// <summary><c>SYMBOL_START_TIME</c>, in seconds since 1970.</summary>
    public const int SymbolStartTime = 51;

    /// <summary><c>SYMBOL_EXPIRATION_TIME</c>, in seconds since 1970.</summary>
    public const int SymbolExpirationTime = 52;

    /// <summary><c>SYMBOL_TRADE_TICK_VALUE_PROFIT</c>.</summary>
    public const int SymbolTickValueProfit = 53;

    /// <summary><c>SYMBOL_TRADE_TICK_VALUE_LOSS</c>.</summary>
    public const int SymbolTickValueLoss = 54;

    /// <summary><c>SYMBOL_ORDER_MODE</c>: the permitted order types, a bit mask.</summary>
    public const int SymbolOrderMode = 71;

    /// <summary><c>SYMBOL_MARGIN_HEDGED</c>.</summary>
    public const int SymbolMarginHedged = 77;

    /// <summary><c>SYMBOL_MARGIN_HEDGED_USE_LEG</c>.</summary>
    public const int SymbolMarginHedgedUseLeg = 82;

    // ENUM_SYMBOL_TRADE_EXECUTION

    /// <summary><c>SYMBOL_TRADE_EXECUTION_REQUEST</c>.</summary>
    public const int TradeExecutionRequest = 0;

    /// <summary><c>SYMBOL_TRADE_EXECUTION_INSTANT</c>.</summary>
    public const int TradeExecutionInstant = 1;

    /// <summary><c>SYMBOL_TRADE_EXECUTION_MARKET</c>.</summary>
    public const int TradeExecutionMarket = 2;

    /// <summary><c>SYMBOL_TRADE_EXECUTION_EXCHANGE</c>.</summary>
    public const int TradeExecutionExchange = 3;

    // ENUM_POSITION_PROPERTY_*

    /// <summary><c>POSITION_SYMBOL</c>.</summary>
    public const int PositionSymbol = 0;

    /// <summary><c>POSITION_TIME</c>.</summary>
    public const int PositionTime = 1;

    /// <summary><c>POSITION_TYPE</c>.</summary>
    public const int PositionType = 2;

    /// <summary><c>POSITION_VOLUME</c>.</summary>
    public const int PositionVolume = 3;

    /// <summary><c>POSITION_PRICE_OPEN</c>.</summary>
    public const int PositionPriceOpen = 4;

    /// <summary><c>POSITION_PRICE_CURRENT</c>.</summary>
    public const int PositionPriceCurrent = 5;

    /// <summary><c>POSITION_SL</c>.</summary>
    public const int PositionStopLoss = 6;

    /// <summary><c>POSITION_TP</c>.</summary>
    public const int PositionTakeProfit = 7;

    /// <summary><c>POSITION_COMMISSION</c>.</summary>
    public const int PositionCommission = 8;

    /// <summary><c>POSITION_SWAP</c>.</summary>
    public const int PositionSwap = 9;

    /// <summary><c>POSITION_PROFIT</c>.</summary>
    public const int PositionProfit = 10;

    // POSITION_COMMENT is 11 and POSITION_MAGIC is 12, which is the one place the position
    // properties interleave a string id with the integer ids around it. This value carried 15
    // here until it was held against the measured catalogue; 15 is POSITION_TIME_UPDATE, so every
    // Comment() read was answering with an update timestamp read as a string.

    /// <summary><c>POSITION_COMMENT</c>.</summary>
    public const int PositionComment = 11;

    /// <summary><c>POSITION_MAGIC</c>.</summary>
    public const int PositionMagic = 12;


    /// <summary><c>POSITION_IDENTIFIER</c>: the position id a deal carries, not the ticket.</summary>
    public const int PositionIdentifier = 13;

    /// <summary><c>POSITION_TIME_MSC</c>.</summary>
    public const int PositionTimeMsc = 14;

    /// <summary><c>POSITION_TIME_UPDATE</c>, in seconds since 1970.</summary>
    public const int PositionTimeUpdate = 15;

    /// <summary><c>POSITION_TIME_UPDATE_MSC</c>.</summary>
    public const int PositionTimeUpdateMsc = 16;

    /// <summary><c>POSITION_TICKET</c>.</summary>
    public const int PositionTicket = 17;

    /// <summary><c>POSITION_TYPE_BUY</c>.</summary>
    public const int PositionTypeBuy = 0;

    /// <summary><c>POSITION_TYPE_SELL</c>.</summary>
    public const int PositionTypeSell = 1;

    // ENUM_ORDER_PROPERTY_*

    /// <summary><c>ORDER_SYMBOL</c>.</summary>
    public const int OrderSymbol = 0;

    /// <summary><c>ORDER_TIME_SETUP</c>, in seconds since 1970.</summary>
    public const int OrderTimeSetup = 1;

    /// <summary><c>ORDER_TIME_EXPIRATION</c>, in seconds since 1970.</summary>
    public const int OrderTimeExpiration = 2;

    /// <summary><c>ORDER_TIME_DONE</c>, in seconds since 1970.</summary>
    public const int OrderTimeDone = 3;

    /// <summary><c>ORDER_TYPE</c>.</summary>
    public const int OrderTypeProperty = 4;

    /// <summary><c>ORDER_TYPE_FILLING</c>, the property; the enumeration members it holds are
    /// <see cref="OrderFillingFok"/> and its neighbours.</summary>
    public const int OrderTypeFillingProperty = 5;

    /// <summary><c>ORDER_TYPE_TIME</c>, the property.</summary>
    public const int OrderTypeTimeProperty = 6;

    /// <summary><c>ORDER_VOLUME_INITIAL</c>.</summary>
    public const int OrderVolumeInitial = 7;

    /// <summary><c>ORDER_VOLUME_CURRENT</c>: what is left unfilled.</summary>
    public const int OrderVolumeCurrent = 8;

    /// <summary><c>ORDER_PRICE_OPEN</c>.</summary>
    public const int OrderPriceOpen = 9;

    /// <summary><c>ORDER_PRICE_CURRENT</c>.</summary>
    public const int OrderPriceCurrent = 10;

    /// <summary><c>ORDER_PRICE_STOPLIMIT</c>.</summary>
    public const int OrderPriceStopLimit = 11;

    /// <summary><c>ORDER_SL</c>.</summary>
    public const int OrderStopLoss = 12;

    /// <summary><c>ORDER_TP</c>.</summary>
    public const int OrderTakeProfit = 13;

    /// <summary><c>ORDER_STATE</c>.</summary>
    public const int OrderState = 14;

    /// <summary><c>ORDER_MAGIC</c>.</summary>
    public const int OrderMagic = 15;

    /// <summary><c>ORDER_COMMENT</c>.</summary>
    public const int OrderComment = 16;

    /// <summary><c>ORDER_POSITION_ID</c>.</summary>
    public const int OrderPositionId = 17;

    /// <summary><c>ORDER_TIME_SETUP_MSC</c>.</summary>
    public const int OrderTimeSetupMsc = 18;

    /// <summary><c>ORDER_TIME_DONE_MSC</c>.</summary>
    public const int OrderTimeDoneMsc = 19;

    /// <summary><c>ORDER_EXTERNAL_ID</c>.</summary>
    public const int OrderExternalId = 20;

    /// <summary><c>ORDER_POSITION_BY_ID</c>.</summary>
    public const int OrderPositionById = 21;

    /// <summary><c>ORDER_TICKET</c>.</summary>
    public const int OrderTicket = 22;

    /// <summary><c>ORDER_TIME_GTC</c>.</summary>
    public const int OrderTimeGtc = 0;

    // Return codes the standard library reports on.

    /// <summary><c>TRADE_RETCODE_DONE</c>.</summary>
    public const uint RetcodeDone = 10009;

    /// <summary><c>TRADE_RETCODE_INVALID</c>.</summary>
    public const uint RetcodeInvalid = 10013;

    /// <summary>
    /// The text <c>CTrade.ResultRetcodeDescription</c> returns for a return code.
    /// </summary>
    /// <remarks>
    /// Strategies print this and some branch on it. An unknown code renders as its number rather
    /// than as a plausible-sounding phrase, so a code this runtime does not model cannot be
    /// mistaken for one it does.
    /// </remarks>
    public static string Describe(uint retcode) => retcode switch
    {
        10004 => "requote",
        10006 => "request rejected",
        10007 => "request cancelled by trader",
        10008 => "order placed",
        10009 => "request completed",
        10010 => "only part of the request was completed",
        10011 => "request processing error",
        10012 => "request cancelled by timeout",
        10013 => "invalid request",
        10014 => "invalid volume in the request",
        10015 => "invalid price in the request",
        10016 => "invalid stops in the request",
        10017 => "trade is disabled",
        10018 => "market is closed",
        10019 => "there is not enough money to complete the request",
        10020 => "prices changed",
        10021 => "there are no quotes to process the request",
        10022 => "invalid order expiration date in the request",
        10023 => "order state changed",
        10024 => "too frequent requests",
        10025 => "no changes in request",
        10026 => "autotrading disabled by server",
        10027 => "autotrading disabled by client terminal",
        10028 => "request locked for processing",
        10029 => "order or position frozen",
        10030 => "invalid order filling type",
        10031 => "no connection with the trade server",
        10032 => "operation is allowed only for live accounts",
        10033 => "the number of pending orders has reached the limit",
        10034 => "the volume of orders and positions has reached the limit",
        10035 => "incorrect or prohibited order type",
        10036 => "position with the specified identifier has already been closed",
        10038 => "close volume exceeds the current position volume",
        10039 => "a close order already exists for the position",
        10040 => "the number of open positions has reached the limit",
        _ => retcode.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// The text <c>CPositionInfo.TypeDescription</c> returns for an <c>ENUM_POSITION_TYPE</c>
    /// member.
    /// </summary>
    /// <remarks>
    /// The shipped class spells the unknown case out, and it is kept: a position that is neither
    /// long nor short is a broker state this runtime does not model, and calling it a sell would
    /// hide that.
    /// </remarks>
    public static string DescribePositionType(int positionType) => positionType switch
    {
        PositionTypeBuy => "buy",
        PositionTypeSell => "sell",
        _ => "unknown position type " + positionType.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// The text <c>COrderInfo.TypeDescription</c> returns for an <c>ENUM_ORDER_TYPE</c> member.
    /// </summary>
    public static string DescribeOrderType(int orderType) => orderType switch
    {
        OrderTypeBuy => "buy",
        OrderTypeSell => "sell",
        OrderTypeBuyLimit => "buy limit",
        OrderTypeSellLimit => "sell limit",
        OrderTypeBuyStop => "buy stop",
        OrderTypeSellStop => "sell stop",
        OrderTypeBuyStopLimit => "buy stop limit",
        OrderTypeSellStopLimit => "sell stop limit",
        OrderTypeCloseBy => "close by",
        _ => "unknown order type " + orderType.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// The text <c>CSymbolInfo.TradeExecutionDescription</c> returns for an
    /// <c>ENUM_SYMBOL_TRADE_EXECUTION</c> member.
    /// </summary>
    /// <remarks>
    /// The four phrases are the ones the shipped class produces. A mode outside the enumeration
    /// renders as its number for the same reason <see cref="Describe"/> does: a value this
    /// runtime does not model must not read as one it does.
    /// </remarks>
    public static string DescribeTradeExecution(int mode) => mode switch
    {
        TradeExecutionRequest => "Trading on request",
        TradeExecutionInstant => "Trading on live streaming prices",
        TradeExecutionMarket => "Execution of orders on the market",
        TradeExecutionExchange => "Exchange execution",
        _ => mode.ToString(CultureInfo.InvariantCulture),
    };
}
