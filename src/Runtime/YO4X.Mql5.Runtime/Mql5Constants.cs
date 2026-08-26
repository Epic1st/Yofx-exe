namespace YO4X.Mql5.Runtime;

/// <summary>
/// The MQL5 named constants whose numeric values MetaQuotes actually publishes.
///
/// MQL5 enumeration tables in the official reference carry only an ID column and a
/// description column, so almost no enumeration member has a documented number. Only
/// four groups are published: <c>ENUM_TIMEFRAMES</c>, the free-standing named
/// constants, the uninitialisation reasons and the trade return codes. Those are the
/// ones carried here.
///
/// Everything else - <c>SYMBOL_BID</c>, <c>POSITION_VOLUME</c>, <c>OBJPROP_COLOR</c>
/// and the rest - reaches the runtime as an opaque <c>int</c> property id and is
/// passed straight through to <see cref="IMql5MarketContext"/>. The runtime never
/// guesses an ordinal: the property-id numbering has to come from the terminal the
/// engine is bound to, not from this table.
/// </summary>
public static class Mql5Constants
{
    /// <summary>An invalid indicator or file handle.</summary>
    public const int InvalidHandle = -1;

    /// <summary>MQL5 <c>WHOLE_ARRAY</c>. Note this is -1, where MQL4 used 0.</summary>
    public const int WholeArray = -1;

    /// <summary>MQL5 <c>WRONG_VALUE</c>.</summary>
    public const int WrongValue = -1;

    /// <summary>MQL5 <c>clrNONE</c>. MQL5 never spells this <c>CLR_NONE</c>.</summary>
    public const int ColorNone = -1;

    /// <summary>MQL5 <c>CHARTS_MAX</c>.</summary>
    public const int ChartsMax = 100;

    /// <summary>The highest value <c>MathRand</c> can return.</summary>
    public const int RandMax = 32767;

    /// <summary><c>TIME_DATE</c>: render the date part.</summary>
    public const int TimeDate = 1;

    /// <summary><c>TIME_MINUTES</c>: render hours and minutes.</summary>
    public const int TimeMinutes = 2;

    /// <summary><c>TIME_SECONDS</c>: render hours, minutes and seconds.</summary>
    public const int TimeSeconds = 4;

    /// <summary>
    /// <c>ENUM_TIMEFRAMES</c>. This is the one enumeration MetaQuotes publishes
    /// numbers for, and they are not contiguous: the hour frames start at 16385.
    /// </summary>
    public static class Timeframes
    {
        /// <summary><c>PERIOD_CURRENT</c>: only the engine can resolve this.</summary>
        public const int Current = 0;

        /// <summary><c>PERIOD_M1</c>.</summary>
        public const int M1 = 1;

        /// <summary><c>PERIOD_M2</c>.</summary>
        public const int M2 = 2;

        /// <summary><c>PERIOD_M3</c>.</summary>
        public const int M3 = 3;

        /// <summary><c>PERIOD_M4</c>.</summary>
        public const int M4 = 4;

        /// <summary><c>PERIOD_M5</c>.</summary>
        public const int M5 = 5;

        /// <summary><c>PERIOD_M6</c>.</summary>
        public const int M6 = 6;

        /// <summary><c>PERIOD_M10</c>.</summary>
        public const int M10 = 10;

        /// <summary><c>PERIOD_M12</c>.</summary>
        public const int M12 = 12;

        /// <summary><c>PERIOD_M15</c>.</summary>
        public const int M15 = 15;

        /// <summary><c>PERIOD_M20</c>.</summary>
        public const int M20 = 20;

        /// <summary><c>PERIOD_M30</c>.</summary>
        public const int M30 = 30;

        /// <summary><c>PERIOD_H1</c>.</summary>
        public const int H1 = 16385;

        /// <summary><c>PERIOD_H2</c>.</summary>
        public const int H2 = 16386;

        /// <summary><c>PERIOD_H3</c>.</summary>
        public const int H3 = 16387;

        /// <summary><c>PERIOD_H4</c>.</summary>
        public const int H4 = 16388;

        /// <summary><c>PERIOD_H6</c>.</summary>
        public const int H6 = 16390;

        /// <summary><c>PERIOD_H8</c>.</summary>
        public const int H8 = 16392;

        /// <summary><c>PERIOD_H12</c>.</summary>
        public const int H12 = 16396;

        /// <summary><c>PERIOD_D1</c>.</summary>
        public const int D1 = 16408;

        /// <summary><c>PERIOD_W1</c>.</summary>
        public const int W1 = 32769;

        /// <summary><c>PERIOD_MN1</c>.</summary>
        public const int MN1 = 49153;

        /// <summary>
        /// The number of seconds in one bar of <paramref name="timeframe"/>, which is
        /// what <c>PeriodSeconds</c> reports. Returns 0 for a timeframe the table does
        /// not know, including <see cref="Current"/> - only the engine can resolve
        /// that one.
        /// </summary>
        public static int Seconds(int timeframe) => timeframe switch
        {
            M1 => 60,
            M2 => 120,
            M3 => 180,
            M4 => 240,
            M5 => 300,
            M6 => 360,
            M10 => 600,
            M12 => 720,
            M15 => 900,
            M20 => 1200,
            M30 => 1800,
            H1 => 3600,
            H2 => 7200,
            H3 => 10800,
            H4 => 14400,
            H6 => 21600,
            H8 => 28800,
            H12 => 43200,
            D1 => 86400,
            W1 => 604800,
            MN1 => 2592000,
            _ => 0
        };
    }

    /// <summary><c>ENUM_TRADE_RETURN_CODES</c>: the only trade constants with published numbers.</summary>
    public static class TradeRetcode
    {
        /// <summary><c>TRADE_RETCODE_REQUOTE</c>.</summary>
        public const int Requote = 10004;

        /// <summary><c>TRADE_RETCODE_REJECT</c>.</summary>
        public const int Reject = 10006;

        /// <summary><c>TRADE_RETCODE_CANCEL</c>.</summary>
        public const int Cancel = 10007;

        /// <summary><c>TRADE_RETCODE_PLACED</c>.</summary>
        public const int Placed = 10008;

        /// <summary><c>TRADE_RETCODE_DONE</c>.</summary>
        public const int Done = 10009;

        /// <summary><c>TRADE_RETCODE_DONE_PARTIAL</c>.</summary>
        public const int DonePartial = 10010;

        /// <summary><c>TRADE_RETCODE_ERROR</c>.</summary>
        public const int Error = 10011;

        /// <summary><c>TRADE_RETCODE_TIMEOUT</c>.</summary>
        public const int Timeout = 10012;

        /// <summary><c>TRADE_RETCODE_INVALID</c>.</summary>
        public const int Invalid = 10013;

        /// <summary><c>TRADE_RETCODE_INVALID_VOLUME</c>.</summary>
        public const int InvalidVolume = 10014;

        /// <summary><c>TRADE_RETCODE_INVALID_PRICE</c>.</summary>
        public const int InvalidPrice = 10015;

        /// <summary><c>TRADE_RETCODE_INVALID_STOPS</c>.</summary>
        public const int InvalidStops = 10016;

        /// <summary><c>TRADE_RETCODE_TRADE_DISABLED</c>.</summary>
        public const int TradeDisabled = 10017;

        /// <summary><c>TRADE_RETCODE_MARKET_CLOSED</c>.</summary>
        public const int MarketClosed = 10018;

        /// <summary><c>TRADE_RETCODE_NO_MONEY</c>.</summary>
        public const int NoMoney = 10019;

        /// <summary><c>TRADE_RETCODE_CONNECTION</c>.</summary>
        public const int Connection = 10031;
    }

    /// <summary><c>UninitializeReason</c> codes.</summary>
    public static class UninitReason
    {
        /// <summary><c>REASON_PROGRAM</c>.</summary>
        public const int Program = 0;

        /// <summary><c>REASON_REMOVE</c>.</summary>
        public const int Remove = 1;

        /// <summary><c>REASON_RECOMPILE</c>.</summary>
        public const int Recompile = 2;

        /// <summary><c>REASON_CHARTCHANGE</c>.</summary>
        public const int ChartChange = 3;

        /// <summary><c>REASON_CHARTCLOSE</c>.</summary>
        public const int ChartClose = 4;

        /// <summary><c>REASON_PARAMETERS</c>.</summary>
        public const int Parameters = 5;

        /// <summary><c>REASON_ACCOUNT</c>.</summary>
        public const int Account = 6;

        /// <summary><c>REASON_TEMPLATE</c>.</summary>
        public const int Template = 7;

        /// <summary><c>REASON_INITFAILED</c>.</summary>
        public const int InitFailed = 8;

        /// <summary><c>REASON_CLOSE</c>.</summary>
        public const int Close = 9;
    }
}
