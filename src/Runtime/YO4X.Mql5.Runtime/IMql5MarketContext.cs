namespace YO4X.Mql5.Runtime;

/// <summary>How a <c>Copy*</c> call selects the slice of a series it wants.</summary>
public enum Mql5CopyRangeKind
{
    /// <summary>By bar index and count, counting back from the current bar.</summary>
    FromPosition,

    /// <summary>By start time and count.</summary>
    FromTime,

    /// <summary>By start time and stop time inclusive.</summary>
    TimeRange
}

/// <summary>
/// The slice a <c>Copy*</c> built-in is asking for.
///
/// MQL5 documents three overloads of every <c>Copy*</c> function that differ only in
/// how the slice is addressed. Collapsing them into one shape keeps
/// <see cref="IMql5MarketContext"/> from carrying twenty-seven near-identical methods
/// while still telling the engine exactly which of the three forms was called.
/// </summary>
public readonly record struct Mql5CopyRange
{
    /// <summary>Which of the three addressing forms the strategy used.</summary>
    public Mql5CopyRangeKind Kind { get; init; }

    /// <summary>Bar index of the first element, for <see cref="Mql5CopyRangeKind.FromPosition"/>.</summary>
    public int StartPosition { get; init; }

    /// <summary>Number of elements wanted, for the two count-based forms.</summary>
    public int Count { get; init; }

    /// <summary>Start time, for the two time-based forms. Seconds since 1970-01-01 UTC.</summary>
    public long StartTime { get; init; }

    /// <summary>Stop time, for <see cref="Mql5CopyRangeKind.TimeRange"/>. Seconds since 1970-01-01 UTC.</summary>
    public long StopTime { get; init; }

    /// <summary>A slice addressed by bar index and count.</summary>
    public static Mql5CopyRange FromPosition(int startPosition, int count)
        => new() { Kind = Mql5CopyRangeKind.FromPosition, StartPosition = startPosition, Count = count };

    /// <summary>A slice addressed by start time and count.</summary>
    public static Mql5CopyRange FromTime(long startTime, int count)
        => new() { Kind = Mql5CopyRangeKind.FromTime, StartTime = startTime, Count = count };

    /// <summary>A slice addressed by a closed time interval.</summary>
    public static Mql5CopyRange TimeRange(long startTime, long stopTime)
        => new() { Kind = Mql5CopyRangeKind.TimeRange, StartTime = startTime, StopTime = stopTime };
}

/// <summary>
/// Everything the MQL5 runtime cannot answer on its own: the clock, the symbol, the
/// account, open positions and orders, price series and indicator handles.
///
/// The runtime implements the <c>Native</c> half of the MQL5 standard library itself
/// and delegates the <c>EngineBound</c> and <c>IndicatorBound</c> halves here. No
/// indicator mathematics lives on this side of the seam: the runtime asks for a
/// handle by name and reads buffers back, exactly as MQL5 does.
///
/// Only the members at the top of this interface are abstract. Everything below them
/// carries a default implementation returning the value MQL5 documents for failure -
/// 0, false, an empty string, or -1 for a <c>Copy*</c> count. An engine can therefore
/// come up on the core surface and fill the rest in as it grows, and a strategy that
/// reaches a member the engine has not implemented gets MQL5's own "no data"
/// answer rather than a crash or a fabricated price.
/// </summary>
public interface IMql5MarketContext
{
    // ------------------------------------------------------------- required --

    /// <summary>The symbol the strategy is attached to. MQL5 <c>Symbol()</c>.</summary>
    string Symbol { get; }

    /// <summary>The point size of <see cref="Symbol"/>. MQL5 <c>Point()</c>.</summary>
    double Point { get; }

    /// <summary>The price precision of <see cref="Symbol"/>. MQL5 <c>Digits()</c>.</summary>
    int Digits { get; }

    /// <summary>The trade server clock. MQL5 <c>TimeCurrent()</c>.</summary>
    DateTime TimeCurrent { get; }

    /// <summary>MQL5 <c>SymbolInfoDouble</c>. <paramref name="propertyId"/> is an <c>ENUM_SYMBOL_INFO_DOUBLE</c> member.</summary>
    double SymbolInfoDouble(string symbol, int propertyId);

    /// <summary>MQL5 <c>SymbolInfoInteger</c>. <paramref name="propertyId"/> is an <c>ENUM_SYMBOL_INFO_INTEGER</c> member.</summary>
    long SymbolInfoInteger(string symbol, int propertyId);

    /// <summary>MQL5 <c>AccountInfoDouble</c>. <paramref name="propertyId"/> is an <c>ENUM_ACCOUNT_INFO_DOUBLE</c> member.</summary>
    double AccountInfoDouble(int propertyId);

    /// <summary>MQL5 <c>PositionsTotal</c>.</summary>
    int PositionsTotal();

    /// <summary>MQL5 <c>PositionSelect</c>. Selects the position on <paramref name="symbol"/> for the getters below.</summary>
    bool PositionSelect(string symbol);

    /// <summary>MQL5 <c>PositionGetDouble</c>, reading the selected position.</summary>
    double PositionGetDouble(int propertyId);

    /// <summary>MQL5 <c>PositionGetInteger</c>, reading the selected position.</summary>
    long PositionGetInteger(int propertyId);

    /// <summary>
    /// MQL5 <c>OrderSend</c>. The single route by which an MQL5 strategy changes
    /// trading state: there is no OrderClose, OrderModify or OrderDelete.
    /// </summary>
    bool OrderSend(Mql5TradeRequest request, out Mql5TradeResult result);

    /// <summary>
    /// Resolves an indicator handle. <paramref name="name"/> is the MQL5 built-in
    /// spelling - <c>iMA</c>, <c>iATR</c>, <c>iRSI</c> - and
    /// <paramref name="parameters"/> carries its documented arguments in declaration
    /// order. Returns <see cref="Mql5Constants.InvalidHandle"/> when the indicator
    /// cannot be created.
    /// </summary>
    int IndicatorHandle(string name, params object[] parameters);

    /// <summary>
    /// MQL5 <c>CopyBuffer</c> in its start-position form. Returns the number of
    /// elements written, or -1 on failure.
    /// </summary>
    int CopyBuffer(int handle, int bufferNum, int start, int count, double[] target);

    // -------------------------------------------------------------- clock ---

    /// <summary>MQL5 <c>TimeLocal</c>. Defaults to the trade server clock.</summary>
    DateTime TimeLocal => TimeCurrent;

    /// <summary>MQL5 <c>TimeGMT</c>. Defaults to the trade server clock.</summary>
    DateTime TimeGmt => TimeCurrent;

    /// <summary>MQL5 <c>TimeTradeServer</c>. Defaults to the trade server clock.</summary>
    DateTime TimeTradeServer => TimeCurrent;

    /// <summary>MQL5 <c>TimeGMTOffset</c>, in seconds.</summary>
    int TimeGmtOffset => 0;

    /// <summary>MQL5 <c>TimeDaylightSavings</c>, in seconds.</summary>
    int TimeDaylightSavings => 0;

    /// <summary>MQL5 <c>Period()</c>. An <c>ENUM_TIMEFRAMES</c> member.</summary>
    int Period => Mql5Constants.Timeframes.Current;

    // ------------------------------------------------------------- symbol ---

    /// <summary>MQL5 <c>SymbolInfoString</c>.</summary>
    string SymbolInfoString(string symbol, int propertyId) => string.Empty;

    /// <summary>MQL5 <c>SymbolInfoTick</c>.</summary>
    bool SymbolInfoTick(string symbol, out Mql5Tick tick)
    {
        tick = default;
        return false;
    }

    /// <summary>MQL5 <c>SymbolsTotal</c>.</summary>
    int SymbolsTotal(bool selected) => 0;

    /// <summary>MQL5 <c>SymbolName</c>.</summary>
    string SymbolName(int position, bool selected) => string.Empty;

    /// <summary>MQL5 <c>SymbolSelect</c>.</summary>
    bool SymbolSelect(string symbol, bool selectFlag) => false;

    /// <summary>MQL5 <c>SymbolIsSynchronized</c>.</summary>
    bool SymbolIsSynchronized(string symbol) => false;

    /// <summary>MQL5 <c>SymbolInfoMarginRate</c>.</summary>
    bool SymbolInfoMarginRate(string symbol, int orderType, out double initialMarginRate, out double maintenanceMarginRate)
    {
        initialMarginRate = 0;
        maintenanceMarginRate = 0;
        return false;
    }

    /// <summary>MQL5 <c>SymbolInfoSessionQuote</c>. Times are seconds since 1970-01-01 UTC.</summary>
    bool SymbolInfoSessionQuote(string symbol, int dayOfWeek, uint sessionIndex, out long from, out long until)
    {
        from = 0;
        until = 0;
        return false;
    }

    /// <summary>MQL5 <c>SymbolInfoSessionTrade</c>. Times are seconds since 1970-01-01 UTC.</summary>
    bool SymbolInfoSessionTrade(string symbol, int dayOfWeek, uint sessionIndex, out long from, out long until)
    {
        from = 0;
        until = 0;
        return false;
    }

    /// <summary>MQL5 <c>MarketBookAdd</c>.</summary>
    bool MarketBookAdd(string symbol) => false;

    /// <summary>MQL5 <c>MarketBookRelease</c>.</summary>
    bool MarketBookRelease(string symbol) => false;

    // ------------------------------------------------------------ account ---

    /// <summary>MQL5 <c>AccountInfoInteger</c>.</summary>
    long AccountInfoInteger(int propertyId) => 0;

    /// <summary>MQL5 <c>AccountInfoString</c>.</summary>
    string AccountInfoString(int propertyId) => string.Empty;

    // ----------------------------------------------------------- position ---

    /// <summary>MQL5 <c>PositionGetString</c>, reading the selected position.</summary>
    string PositionGetString(int propertyId) => string.Empty;

    /// <summary>MQL5 <c>PositionSelectByTicket</c>.</summary>
    bool PositionSelectByTicket(ulong ticket) => false;

    /// <summary>MQL5 <c>PositionGetTicket</c>. Also selects the position, as MQL5 does.</summary>
    ulong PositionGetTicket(int index) => 0;

    /// <summary>MQL5 <c>PositionGetSymbol</c>. Also selects the position, as MQL5 does.</summary>
    string PositionGetSymbol(int index) => string.Empty;

    // -------------------------------------------------------------- order ---

    /// <summary>MQL5 <c>OrdersTotal</c>: pending orders, not positions.</summary>
    int OrdersTotal() => 0;

    /// <summary>MQL5 <c>OrderGetTicket</c>. Also selects the order, as MQL5 does.</summary>
    ulong OrderGetTicket(int index) => 0;

    /// <summary>MQL5 <c>OrderSelect</c>, which in MQL5 selects by ticket, not by index.</summary>
    bool OrderSelect(ulong ticket) => false;

    /// <summary>MQL5 <c>OrderGetDouble</c>, reading the selected order.</summary>
    double OrderGetDouble(int propertyId) => 0;

    /// <summary>MQL5 <c>OrderGetInteger</c>, reading the selected order.</summary>
    long OrderGetInteger(int propertyId) => 0;

    /// <summary>MQL5 <c>OrderGetString</c>, reading the selected order.</summary>
    string OrderGetString(int propertyId) => string.Empty;

    // -------------------------------------------------------------- trade ---

    /// <summary>MQL5 <c>OrderSendAsync</c>. Defaults to the synchronous route.</summary>
    bool OrderSendAsync(Mql5TradeRequest request, out Mql5TradeResult result) => OrderSend(request, out result);

    /// <summary>MQL5 <c>OrderCheck</c>.</summary>
    bool OrderCheck(Mql5TradeRequest request, out Mql5TradeCheckResult result)
    {
        result = new Mql5TradeCheckResult();
        return false;
    }

    /// <summary>MQL5 <c>OrderCalcMargin</c>.</summary>
    bool OrderCalcMargin(int orderType, string symbol, double volume, double price, out double margin)
    {
        margin = 0;
        return false;
    }

    /// <summary>MQL5 <c>OrderCalcProfit</c>.</summary>
    bool OrderCalcProfit(int orderType, string symbol, double volume, double priceOpen, double priceClose, out double profit)
    {
        profit = 0;
        return false;
    }

    // ------------------------------------------------------------ history ---

    /// <summary>MQL5 <c>HistorySelect</c>. Times are seconds since 1970-01-01 UTC.</summary>
    bool HistorySelect(long fromDate, long toDate) => false;

    /// <summary>MQL5 <c>HistorySelectByPosition</c>.</summary>
    bool HistorySelectByPosition(ulong positionId) => false;

    /// <summary>MQL5 <c>HistoryOrderSelect</c>.</summary>
    bool HistoryOrderSelect(ulong ticket) => false;

    /// <summary>MQL5 <c>HistoryOrdersTotal</c>.</summary>
    int HistoryOrdersTotal() => 0;

    /// <summary>MQL5 <c>HistoryOrderGetTicket</c>.</summary>
    ulong HistoryOrderGetTicket(int index) => 0;

    /// <summary>MQL5 <c>HistoryOrderGetDouble</c>.</summary>
    double HistoryOrderGetDouble(ulong ticket, int propertyId) => 0;

    /// <summary>MQL5 <c>HistoryOrderGetInteger</c>.</summary>
    long HistoryOrderGetInteger(ulong ticket, int propertyId) => 0;

    /// <summary>MQL5 <c>HistoryOrderGetString</c>.</summary>
    string HistoryOrderGetString(ulong ticket, int propertyId) => string.Empty;

    /// <summary>MQL5 <c>HistoryDealSelect</c>.</summary>
    bool HistoryDealSelect(ulong ticket) => false;

    /// <summary>MQL5 <c>HistoryDealsTotal</c>.</summary>
    int HistoryDealsTotal() => 0;

    /// <summary>MQL5 <c>HistoryDealGetTicket</c>.</summary>
    ulong HistoryDealGetTicket(int index) => 0;

    /// <summary>MQL5 <c>HistoryDealGetDouble</c>.</summary>
    double HistoryDealGetDouble(ulong ticket, int propertyId) => 0;

    /// <summary>MQL5 <c>HistoryDealGetInteger</c>.</summary>
    long HistoryDealGetInteger(ulong ticket, int propertyId) => 0;

    /// <summary>MQL5 <c>HistoryDealGetString</c>.</summary>
    string HistoryDealGetString(ulong ticket, int propertyId) => string.Empty;

    // --------------------------------------------------------- price data ---

    /// <summary>MQL5 <c>Bars</c> in its two-argument form.</summary>
    int Bars(string symbol, int timeframe) => 0;

    /// <summary>MQL5 <c>Bars</c> in its four-argument form. Times are seconds since 1970-01-01 UTC.</summary>
    int BarsInRange(string symbol, int timeframe, long startTime, long stopTime) => 0;

    /// <summary>MQL5 <c>iTime</c>. Returns the bar open time, or 0 when the bar is missing.</summary>
    long BarTime(string symbol, int timeframe, int shift) => 0;

    /// <summary>MQL5 <c>iOpen</c>.</summary>
    double BarOpen(string symbol, int timeframe, int shift) => 0;

    /// <summary>MQL5 <c>iHigh</c>.</summary>
    double BarHigh(string symbol, int timeframe, int shift) => 0;

    /// <summary>MQL5 <c>iLow</c>.</summary>
    double BarLow(string symbol, int timeframe, int shift) => 0;

    /// <summary>MQL5 <c>iClose</c>.</summary>
    double BarClose(string symbol, int timeframe, int shift) => 0;

    /// <summary>MQL5 <c>iVolume</c> and <c>iTickVolume</c>.</summary>
    long BarTickVolume(string symbol, int timeframe, int shift) => 0;

    /// <summary>MQL5 <c>iRealVolume</c>.</summary>
    long BarRealVolume(string symbol, int timeframe, int shift) => 0;

    /// <summary>MQL5 <c>iSpread</c>, in points.</summary>
    long BarSpread(string symbol, int timeframe, int shift) => 0;

    /// <summary>MQL5 <c>iBars</c>.</summary>
    int BarCount(string symbol, int timeframe) => 0;

    /// <summary>MQL5 <c>iBarShift</c>. Returns -1 when no bar matches.</summary>
    int BarShift(string symbol, int timeframe, long time, bool exact) => -1;

    /// <summary>MQL5 <c>iHighest</c>. <paramref name="seriesMode"/> is an <c>ENUM_SERIESMODE</c> member.</summary>
    int BarHighest(string symbol, int timeframe, int seriesMode, int count, int start) => -1;

    /// <summary>MQL5 <c>iLowest</c>. <paramref name="seriesMode"/> is an <c>ENUM_SERIESMODE</c> member.</summary>
    int BarLowest(string symbol, int timeframe, int seriesMode, int count, int start) => -1;

    /// <summary>MQL5 <c>SeriesInfoInteger</c>.</summary>
    long SeriesInfoInteger(string symbol, int timeframe, int propertyId) => 0;

    /// <summary>MQL5 <c>CopyRates</c>. Returns the number of bars written, or -1.</summary>
    int CopyRates(string symbol, int timeframe, Mql5CopyRange range, ref Mql5Rates[] target) => -1;

    /// <summary>MQL5 <c>CopyTime</c>. Returns the number of elements written, or -1.</summary>
    int CopyTime(string symbol, int timeframe, Mql5CopyRange range, ref long[] target) => -1;

    /// <summary>MQL5 <c>CopyOpen</c>. Returns the number of elements written, or -1.</summary>
    int CopyOpen(string symbol, int timeframe, Mql5CopyRange range, ref double[] target) => -1;

    /// <summary>MQL5 <c>CopyHigh</c>. Returns the number of elements written, or -1.</summary>
    int CopyHigh(string symbol, int timeframe, Mql5CopyRange range, ref double[] target) => -1;

    /// <summary>MQL5 <c>CopyLow</c>. Returns the number of elements written, or -1.</summary>
    int CopyLow(string symbol, int timeframe, Mql5CopyRange range, ref double[] target) => -1;

    /// <summary>MQL5 <c>CopyClose</c>. Returns the number of elements written, or -1.</summary>
    int CopyClose(string symbol, int timeframe, Mql5CopyRange range, ref double[] target) => -1;

    /// <summary>MQL5 <c>CopyTickVolume</c>. Returns the number of elements written, or -1.</summary>
    int CopyTickVolume(string symbol, int timeframe, Mql5CopyRange range, ref long[] target) => -1;

    /// <summary>MQL5 <c>CopyRealVolume</c>. Returns the number of elements written, or -1.</summary>
    int CopyRealVolume(string symbol, int timeframe, Mql5CopyRange range, ref long[] target) => -1;

    /// <summary>MQL5 <c>CopySpread</c>. Returns the number of elements written, or -1.</summary>
    int CopySpread(string symbol, int timeframe, Mql5CopyRange range, ref int[] target) => -1;

    /// <summary>MQL5 <c>CopyTicks</c> and <c>CopyTicksRange</c>. Returns the number of ticks written, or -1.</summary>
    int CopyTicks(string symbol, uint flags, ulong fromMsc, ulong toMsc, uint count, ref Mql5Tick[] target) => -1;

    // ---------------------------------------------------------- indicator ---

    /// <summary>
    /// MQL5 <c>CopyBuffer</c> in its time-addressed forms. Defaults to the
    /// start-position route when the range is position-addressed, so an engine that
    /// implements only the required <see cref="CopyBuffer(int, int, int, int, double[])"/>
    /// still answers the common case.
    /// </summary>
    int CopyBufferRange(int handle, int bufferNum, Mql5CopyRange range, ref double[] target)
    {
        if (range.Kind != Mql5CopyRangeKind.FromPosition)
        {
            return -1;
        }

        int count = range.Count;
        if (count <= 0)
        {
            return 0;
        }

        if (target.Length < count)
        {
            Array.Resize(ref target, count);
        }

        return CopyBuffer(handle, bufferNum, range.StartPosition, count, target);
    }

    /// <summary>MQL5 <c>BarsCalculated</c>. Returns -1 when the handle is not ready.</summary>
    int BarsCalculated(int handle) => -1;

    /// <summary>MQL5 <c>IndicatorRelease</c>.</summary>
    bool IndicatorRelease(int handle) => false;

    /// <summary>MQL5 <c>SetIndexBuffer</c>. Only meaningful for a converted custom indicator.</summary>
    bool SetIndexBuffer(int index, double[] buffer, int dataType) => false;

    // ----------------------------------------------------------- terminal ---

    /// <summary>MQL5 <c>IsStopped</c>.</summary>
    bool IsStopped() => false;

    /// <summary>MQL5 <c>UninitializeReason</c>.</summary>
    int UninitializeReason() => Mql5Constants.UninitReason.Program;

    /// <summary>
    /// MQL5 <c>MQLInfoInteger</c>. Defaults to <see cref="Mql5ProgramInfo.InfoInteger"/>,
    /// which answers the run-mode and permission properties truthfully - this engine is
    /// a tester, it accepts orders, it loads no DLLs - and refuses the ones only a host
    /// or a real process can answer. A host that overrides this should delegate the
    /// properties it does not own back to <see cref="Mql5ProgramInfo"/>.
    /// </summary>
    long MqlInfoInteger(int propertyId) => Mql5ProgramInfo.InfoInteger(propertyId);

    /// <summary>
    /// MQL5 <c>MQLInfoString</c>. Defaults to <see cref="Mql5ProgramInfo.InfoString"/>,
    /// which refuses both published properties: the program's name and path are the
    /// host's to state, and the empty string is MQL5's failure value rather than an
    /// answer.
    /// </summary>
    string MqlInfoString(int propertyId) => Mql5ProgramInfo.InfoString(propertyId);

    /// <summary>MQL5 <c>TesterStatistics</c>.</summary>
    double TesterStatistics(int statisticId) => 0;

    /// <summary>MQL5 <c>ExpertRemove</c>: asks the engine to unload the strategy.</summary>
    void ExpertRemove()
    {
        // Default: the engine has no unload channel and the request is dropped.
    }

    /// <summary>MQL5 <c>TesterStop</c>.</summary>
    void TesterStop()
    {
        // Default: the engine has no early-stop channel and the request is dropped.
    }

    /// <summary>MQL5 <c>TesterWithdrawal</c>.</summary>
    bool TesterWithdrawal(double money) => false;

    /// <summary>MQL5 <c>EventSetTimer</c>.</summary>
    bool EventSetTimer(int seconds) => false;

    /// <summary>MQL5 <c>EventSetMillisecondTimer</c>.</summary>
    bool EventSetMillisecondTimer(int milliseconds) => false;

    /// <summary>MQL5 <c>EventKillTimer</c>.</summary>
    void EventKillTimer()
    {
        // Default: no timer was ever armed, so there is nothing to cancel.
    }
}
