namespace YO4X.Mql5.Runtime;

/// <summary>
/// MQL5 timeseries access: the single-bar readers and the bulk <c>Copy*</c> family.
///
/// The single-bar readers are <b>EngineBound</b> and the bulk readers are
/// <b>IndicatorBound</b>, but that is a classification detail - both go to
/// <see cref="IMql5MarketContext"/>. What matters here is the ordering contract.
///
/// <b>The engine returns series data oldest-first.</b> MQL5 arrays flagged with
/// <c>ArraySetAsSeries(array, true)</c> index the other way round, newest at 0, and a
/// great deal of MQL5 code sets that flag on its buffers and then reads
/// <c>close[0]</c> meaning "the current bar". This runtime honours the flag: after the
/// engine has filled a target that <c>ArraySetAsSeries</c> marked, the runtime reverses
/// it. Getting this backwards does not throw or return zero - it silently reads the
/// oldest bar wherever the strategy meant the newest, which is the worst failure mode
/// available, so the rule is stated here rather than left to be inferred.
/// </summary>
public partial interface IMql5Runtime
{
    /// <summary>MQL5 <c>Bars</c>, the whole-history form. EngineBound.</summary>
    int Bars(string? symbol, int timeframe);

    /// <summary>MQL5 <c>Bars</c>, the date-range form. EngineBound.</summary>
    int Bars(string? symbol, int timeframe, long startTime, long stopTime);

    /// <summary>MQL5 <c>iBars</c>. EngineBound.</summary>
    int IBars(string? symbol, int timeframe);

    /// <summary>MQL5 <c>iBarShift</c>. Returns -1 when no bar matches. EngineBound.</summary>
    int IBarShift(string? symbol, int timeframe, long time, bool exact = false);

    /// <summary>MQL5 <c>iOpen</c>. EngineBound.</summary>
    double IOpen(string? symbol, int timeframe, int shift);

    /// <summary>MQL5 <c>iHigh</c>. EngineBound.</summary>
    double IHigh(string? symbol, int timeframe, int shift);

    /// <summary>MQL5 <c>iLow</c>. EngineBound.</summary>
    double ILow(string? symbol, int timeframe, int shift);

    /// <summary>MQL5 <c>iClose</c>. EngineBound.</summary>
    double IClose(string? symbol, int timeframe, int shift);

    /// <summary>MQL5 <c>iTime</c>: the open time of the bar <paramref name="shift"/> back. EngineBound.</summary>
    long ITime(string? symbol, int timeframe, int shift);

    /// <summary>MQL5 <c>iVolume</c>, which reads tick volume. EngineBound.</summary>
    long IVolume(string? symbol, int timeframe, int shift);

    /// <summary>MQL5 <c>iTickVolume</c>. EngineBound.</summary>
    long ITickVolume(string? symbol, int timeframe, int shift);

    /// <summary>MQL5 <c>iRealVolume</c>. EngineBound.</summary>
    long IRealVolume(string? symbol, int timeframe, int shift);

    /// <summary>MQL5 <c>iSpread</c>, in points. EngineBound.</summary>
    long ISpread(string? symbol, int timeframe, int shift);

    /// <summary>MQL5 <c>iHighest</c>. EngineBound.</summary>
    int IHighest(string? symbol, int timeframe, int seriesMode, int count = Mql5Constants.WholeArray, int start = 0);

    /// <summary>MQL5 <c>iLowest</c>. EngineBound.</summary>
    int ILowest(string? symbol, int timeframe, int seriesMode, int count = Mql5Constants.WholeArray, int start = 0);

    /// <summary>MQL5 <c>SeriesInfoInteger</c>, direct-return form. EngineBound.</summary>
    long SeriesInfoInteger(string? symbol, int timeframe, int propertyId);

    /// <summary>MQL5 <c>SeriesInfoInteger</c>, out-parameter form. EngineBound.</summary>
    bool SeriesInfoInteger(string? symbol, int timeframe, int propertyId, out long value);

    /// <summary>MQL5 <c>CopyRates</c>, start-position form. IndicatorBound.</summary>
    int CopyRates(string? symbol, int timeframe, int startPosition, int count, ref Mql5Rates[]? target);

    /// <summary>MQL5 <c>CopyRates</c>, start-time form. IndicatorBound.</summary>
    int CopyRates(string? symbol, int timeframe, long startTime, int count, ref Mql5Rates[]? target);

    /// <summary>MQL5 <c>CopyRates</c>, time-range form. IndicatorBound.</summary>
    int CopyRates(string? symbol, int timeframe, long startTime, long stopTime, ref Mql5Rates[]? target);

    /// <summary>MQL5 <c>CopyTime</c>, start-position form. IndicatorBound.</summary>
    int CopyTime(string? symbol, int timeframe, int startPosition, int count, ref long[]? target);

    /// <summary>MQL5 <c>CopyTime</c>, start-time form. IndicatorBound.</summary>
    int CopyTime(string? symbol, int timeframe, long startTime, int count, ref long[]? target);

    /// <summary>MQL5 <c>CopyTime</c>, time-range form. IndicatorBound.</summary>
    int CopyTime(string? symbol, int timeframe, long startTime, long stopTime, ref long[]? target);

    /// <summary>MQL5 <c>CopyOpen</c>, start-position form. IndicatorBound.</summary>
    int CopyOpen(string? symbol, int timeframe, int startPosition, int count, ref double[]? target);

    /// <summary>MQL5 <c>CopyOpen</c>, start-time form. IndicatorBound.</summary>
    int CopyOpen(string? symbol, int timeframe, long startTime, int count, ref double[]? target);

    /// <summary>MQL5 <c>CopyOpen</c>, time-range form. IndicatorBound.</summary>
    int CopyOpen(string? symbol, int timeframe, long startTime, long stopTime, ref double[]? target);

    /// <summary>MQL5 <c>CopyHigh</c>, start-position form. IndicatorBound.</summary>
    int CopyHigh(string? symbol, int timeframe, int startPosition, int count, ref double[]? target);

    /// <summary>MQL5 <c>CopyHigh</c>, start-time form. IndicatorBound.</summary>
    int CopyHigh(string? symbol, int timeframe, long startTime, int count, ref double[]? target);

    /// <summary>MQL5 <c>CopyHigh</c>, time-range form. IndicatorBound.</summary>
    int CopyHigh(string? symbol, int timeframe, long startTime, long stopTime, ref double[]? target);

    /// <summary>MQL5 <c>CopyLow</c>, start-position form. IndicatorBound.</summary>
    int CopyLow(string? symbol, int timeframe, int startPosition, int count, ref double[]? target);

    /// <summary>MQL5 <c>CopyLow</c>, start-time form. IndicatorBound.</summary>
    int CopyLow(string? symbol, int timeframe, long startTime, int count, ref double[]? target);

    /// <summary>MQL5 <c>CopyLow</c>, time-range form. IndicatorBound.</summary>
    int CopyLow(string? symbol, int timeframe, long startTime, long stopTime, ref double[]? target);

    /// <summary>MQL5 <c>CopyClose</c>, start-position form. IndicatorBound.</summary>
    int CopyClose(string? symbol, int timeframe, int startPosition, int count, ref double[]? target);

    /// <summary>MQL5 <c>CopyClose</c>, start-time form. IndicatorBound.</summary>
    int CopyClose(string? symbol, int timeframe, long startTime, int count, ref double[]? target);

    /// <summary>MQL5 <c>CopyClose</c>, time-range form. IndicatorBound.</summary>
    int CopyClose(string? symbol, int timeframe, long startTime, long stopTime, ref double[]? target);

    /// <summary>MQL5 <c>CopyTickVolume</c>, start-position form. IndicatorBound.</summary>
    int CopyTickVolume(string? symbol, int timeframe, int startPosition, int count, ref long[]? target);

    /// <summary>MQL5 <c>CopyTickVolume</c>, start-time form. IndicatorBound.</summary>
    int CopyTickVolume(string? symbol, int timeframe, long startTime, int count, ref long[]? target);

    /// <summary>MQL5 <c>CopyTickVolume</c>, time-range form. IndicatorBound.</summary>
    int CopyTickVolume(string? symbol, int timeframe, long startTime, long stopTime, ref long[]? target);

    /// <summary>MQL5 <c>CopyRealVolume</c>, start-position form. IndicatorBound.</summary>
    int CopyRealVolume(string? symbol, int timeframe, int startPosition, int count, ref long[]? target);

    /// <summary>MQL5 <c>CopyRealVolume</c>, start-time form. IndicatorBound.</summary>
    int CopyRealVolume(string? symbol, int timeframe, long startTime, int count, ref long[]? target);

    /// <summary>MQL5 <c>CopyRealVolume</c>, time-range form. IndicatorBound.</summary>
    int CopyRealVolume(string? symbol, int timeframe, long startTime, long stopTime, ref long[]? target);

    /// <summary>MQL5 <c>CopySpread</c>, start-position form. IndicatorBound.</summary>
    int CopySpread(string? symbol, int timeframe, int startPosition, int count, ref int[]? target);

    /// <summary>MQL5 <c>CopySpread</c>, start-time form. IndicatorBound.</summary>
    int CopySpread(string? symbol, int timeframe, long startTime, int count, ref int[]? target);

    /// <summary>MQL5 <c>CopySpread</c>, time-range form. IndicatorBound.</summary>
    int CopySpread(string? symbol, int timeframe, long startTime, long stopTime, ref int[]? target);

    /// <summary>MQL5 <c>CopyTicks</c>. IndicatorBound.</summary>
    int CopyTicks(string? symbol, ref Mql5Tick[]? target, uint flags = 0, ulong from = 0, uint count = 0);

    /// <summary>MQL5 <c>CopyTicksRange</c>. IndicatorBound.</summary>
    int CopyTicksRange(string? symbol, ref Mql5Tick[]? target, uint flags = 0, ulong fromMsc = 0, ulong toMsc = 0);

    /// <summary>MQL5 <c>CalendarEventById</c>. Unsupported: no economic-calendar source exists.</summary>
    bool CalendarEventById(long eventId);

    /// <summary>MQL5 <c>CalendarCountryById</c>. Unsupported: no economic-calendar source exists.</summary>
    bool CalendarCountryById(long countryId);

    /// <summary>MQL5 <c>CalendarValueHistory</c>. Unsupported: no economic-calendar source exists.</summary>
    int CalendarValueHistory(long fromDate, long toDate);

    /// <summary>MQL5 <c>CalendarValueLast</c>. Unsupported: no economic-calendar source exists.</summary>
    int CalendarValueLast(ref ulong change);

    /// <summary>MQL5 <c>CalendarEventHistory</c>. Unsupported: no economic-calendar source exists.</summary>
    int CalendarEventHistory(long fromDate, long toDate);
}

public sealed partial class Mql5Runtime
{
    /// <inheritdoc />
    public int Bars(string? symbol, int timeframe) => context.Bars(Resolve(symbol), Timeframe(timeframe));

    /// <inheritdoc />
    public int Bars(string? symbol, int timeframe, long startTime, long stopTime)
        => context.BarsInRange(Resolve(symbol), Timeframe(timeframe), startTime, stopTime);

    /// <inheritdoc />
    public int IBars(string? symbol, int timeframe) => context.BarCount(Resolve(symbol), Timeframe(timeframe));

    /// <inheritdoc />
    public int IBarShift(string? symbol, int timeframe, long time, bool exact = false)
        => context.BarShift(Resolve(symbol), Timeframe(timeframe), time, exact);

    /// <inheritdoc />
    public double IOpen(string? symbol, int timeframe, int shift) => context.BarOpen(Resolve(symbol), Timeframe(timeframe), shift);

    /// <inheritdoc />
    public double IHigh(string? symbol, int timeframe, int shift) => context.BarHigh(Resolve(symbol), Timeframe(timeframe), shift);

    /// <inheritdoc />
    public double ILow(string? symbol, int timeframe, int shift) => context.BarLow(Resolve(symbol), Timeframe(timeframe), shift);

    /// <inheritdoc />
    public double IClose(string? symbol, int timeframe, int shift) => context.BarClose(Resolve(symbol), Timeframe(timeframe), shift);

    /// <inheritdoc />
    public long ITime(string? symbol, int timeframe, int shift) => context.BarTime(Resolve(symbol), Timeframe(timeframe), shift);

    /// <inheritdoc />
    public long IVolume(string? symbol, int timeframe, int shift) => context.BarTickVolume(Resolve(symbol), Timeframe(timeframe), shift);

    /// <inheritdoc />
    public long ITickVolume(string? symbol, int timeframe, int shift) => context.BarTickVolume(Resolve(symbol), Timeframe(timeframe), shift);

    /// <inheritdoc />
    public long IRealVolume(string? symbol, int timeframe, int shift) => context.BarRealVolume(Resolve(symbol), Timeframe(timeframe), shift);

    /// <inheritdoc />
    public long ISpread(string? symbol, int timeframe, int shift) => context.BarSpread(Resolve(symbol), Timeframe(timeframe), shift);

    /// <inheritdoc />
    public int IHighest(string? symbol, int timeframe, int seriesMode, int count = Mql5Constants.WholeArray, int start = 0)
        => context.BarHighest(Resolve(symbol), Timeframe(timeframe), seriesMode, count, start);

    /// <inheritdoc />
    public int ILowest(string? symbol, int timeframe, int seriesMode, int count = Mql5Constants.WholeArray, int start = 0)
        => context.BarLowest(Resolve(symbol), Timeframe(timeframe), seriesMode, count, start);

    /// <inheritdoc />
    public long SeriesInfoInteger(string? symbol, int timeframe, int propertyId)
        => context.SeriesInfoInteger(Resolve(symbol), Timeframe(timeframe), propertyId);

    /// <inheritdoc />
    public bool SeriesInfoInteger(string? symbol, int timeframe, int propertyId, out long value)
    {
        value = context.SeriesInfoInteger(Resolve(symbol), Timeframe(timeframe), propertyId);
        return true;
    }

    /// <inheritdoc />
    public int CopyRates(string? symbol, int timeframe, int startPosition, int count, ref Mql5Rates[]? target)
        => CopyRatesCore(symbol, timeframe, Mql5CopyRange.FromPosition(startPosition, count), ref target);

    /// <inheritdoc />
    public int CopyRates(string? symbol, int timeframe, long startTime, int count, ref Mql5Rates[]? target)
        => CopyRatesCore(symbol, timeframe, Mql5CopyRange.FromTime(startTime, count), ref target);

    /// <inheritdoc />
    public int CopyRates(string? symbol, int timeframe, long startTime, long stopTime, ref Mql5Rates[]? target)
        => CopyRatesCore(symbol, timeframe, Mql5CopyRange.TimeRange(startTime, stopTime), ref target);

    /// <inheritdoc />
    public int CopyTime(string? symbol, int timeframe, int startPosition, int count, ref long[]? target)
        => CopySeriesCore(symbol, timeframe, Mql5CopyRange.FromPosition(startPosition, count), ref target, context.CopyTime);

    /// <inheritdoc />
    public int CopyTime(string? symbol, int timeframe, long startTime, int count, ref long[]? target)
        => CopySeriesCore(symbol, timeframe, Mql5CopyRange.FromTime(startTime, count), ref target, context.CopyTime);

    /// <inheritdoc />
    public int CopyTime(string? symbol, int timeframe, long startTime, long stopTime, ref long[]? target)
        => CopySeriesCore(symbol, timeframe, Mql5CopyRange.TimeRange(startTime, stopTime), ref target, context.CopyTime);

    /// <inheritdoc />
    public int CopyOpen(string? symbol, int timeframe, int startPosition, int count, ref double[]? target)
        => CopySeriesCore(symbol, timeframe, Mql5CopyRange.FromPosition(startPosition, count), ref target, context.CopyOpen);

    /// <inheritdoc />
    public int CopyOpen(string? symbol, int timeframe, long startTime, int count, ref double[]? target)
        => CopySeriesCore(symbol, timeframe, Mql5CopyRange.FromTime(startTime, count), ref target, context.CopyOpen);

    /// <inheritdoc />
    public int CopyOpen(string? symbol, int timeframe, long startTime, long stopTime, ref double[]? target)
        => CopySeriesCore(symbol, timeframe, Mql5CopyRange.TimeRange(startTime, stopTime), ref target, context.CopyOpen);

    /// <inheritdoc />
    public int CopyHigh(string? symbol, int timeframe, int startPosition, int count, ref double[]? target)
        => CopySeriesCore(symbol, timeframe, Mql5CopyRange.FromPosition(startPosition, count), ref target, context.CopyHigh);

    /// <inheritdoc />
    public int CopyHigh(string? symbol, int timeframe, long startTime, int count, ref double[]? target)
        => CopySeriesCore(symbol, timeframe, Mql5CopyRange.FromTime(startTime, count), ref target, context.CopyHigh);

    /// <inheritdoc />
    public int CopyHigh(string? symbol, int timeframe, long startTime, long stopTime, ref double[]? target)
        => CopySeriesCore(symbol, timeframe, Mql5CopyRange.TimeRange(startTime, stopTime), ref target, context.CopyHigh);

    /// <inheritdoc />
    public int CopyLow(string? symbol, int timeframe, int startPosition, int count, ref double[]? target)
        => CopySeriesCore(symbol, timeframe, Mql5CopyRange.FromPosition(startPosition, count), ref target, context.CopyLow);

    /// <inheritdoc />
    public int CopyLow(string? symbol, int timeframe, long startTime, int count, ref double[]? target)
        => CopySeriesCore(symbol, timeframe, Mql5CopyRange.FromTime(startTime, count), ref target, context.CopyLow);

    /// <inheritdoc />
    public int CopyLow(string? symbol, int timeframe, long startTime, long stopTime, ref double[]? target)
        => CopySeriesCore(symbol, timeframe, Mql5CopyRange.TimeRange(startTime, stopTime), ref target, context.CopyLow);

    /// <inheritdoc />
    public int CopyClose(string? symbol, int timeframe, int startPosition, int count, ref double[]? target)
        => CopySeriesCore(symbol, timeframe, Mql5CopyRange.FromPosition(startPosition, count), ref target, context.CopyClose);

    /// <inheritdoc />
    public int CopyClose(string? symbol, int timeframe, long startTime, int count, ref double[]? target)
        => CopySeriesCore(symbol, timeframe, Mql5CopyRange.FromTime(startTime, count), ref target, context.CopyClose);

    /// <inheritdoc />
    public int CopyClose(string? symbol, int timeframe, long startTime, long stopTime, ref double[]? target)
        => CopySeriesCore(symbol, timeframe, Mql5CopyRange.TimeRange(startTime, stopTime), ref target, context.CopyClose);

    /// <inheritdoc />
    public int CopyTickVolume(string? symbol, int timeframe, int startPosition, int count, ref long[]? target)
        => CopySeriesCore(symbol, timeframe, Mql5CopyRange.FromPosition(startPosition, count), ref target, context.CopyTickVolume);

    /// <inheritdoc />
    public int CopyTickVolume(string? symbol, int timeframe, long startTime, int count, ref long[]? target)
        => CopySeriesCore(symbol, timeframe, Mql5CopyRange.FromTime(startTime, count), ref target, context.CopyTickVolume);

    /// <inheritdoc />
    public int CopyTickVolume(string? symbol, int timeframe, long startTime, long stopTime, ref long[]? target)
        => CopySeriesCore(symbol, timeframe, Mql5CopyRange.TimeRange(startTime, stopTime), ref target, context.CopyTickVolume);

    /// <inheritdoc />
    public int CopyRealVolume(string? symbol, int timeframe, int startPosition, int count, ref long[]? target)
        => CopySeriesCore(symbol, timeframe, Mql5CopyRange.FromPosition(startPosition, count), ref target, context.CopyRealVolume);

    /// <inheritdoc />
    public int CopyRealVolume(string? symbol, int timeframe, long startTime, int count, ref long[]? target)
        => CopySeriesCore(symbol, timeframe, Mql5CopyRange.FromTime(startTime, count), ref target, context.CopyRealVolume);

    /// <inheritdoc />
    public int CopyRealVolume(string? symbol, int timeframe, long startTime, long stopTime, ref long[]? target)
        => CopySeriesCore(symbol, timeframe, Mql5CopyRange.TimeRange(startTime, stopTime), ref target, context.CopyRealVolume);

    /// <inheritdoc />
    public int CopySpread(string? symbol, int timeframe, int startPosition, int count, ref int[]? target)
        => CopySeriesCore(symbol, timeframe, Mql5CopyRange.FromPosition(startPosition, count), ref target, context.CopySpread);

    /// <inheritdoc />
    public int CopySpread(string? symbol, int timeframe, long startTime, int count, ref int[]? target)
        => CopySeriesCore(symbol, timeframe, Mql5CopyRange.FromTime(startTime, count), ref target, context.CopySpread);

    /// <inheritdoc />
    public int CopySpread(string? symbol, int timeframe, long startTime, long stopTime, ref int[]? target)
        => CopySeriesCore(symbol, timeframe, Mql5CopyRange.TimeRange(startTime, stopTime), ref target, context.CopySpread);

    /// <inheritdoc />
    public int CopyTicks(string? symbol, ref Mql5Tick[]? target, uint flags = 0, ulong from = 0, uint count = 0)
    {
        Mql5Tick[] buffer = target ?? [];
        int written = context.CopyTicks(Resolve(symbol), flags, from, 0, count, ref buffer);
        target = buffer;
        return Finish(written, target);
    }

    /// <inheritdoc />
    public int CopyTicksRange(string? symbol, ref Mql5Tick[]? target, uint flags = 0, ulong fromMsc = 0, ulong toMsc = 0)
    {
        Mql5Tick[] buffer = target ?? [];
        int written = context.CopyTicks(Resolve(symbol), flags, fromMsc, toMsc, 0, ref buffer);
        target = buffer;
        return Finish(written, target);
    }

    /// <inheritdoc />
    public bool CalendarEventById(long eventId)
        => throw Refuse(nameof(CalendarEventById), "no economic-calendar data source is available to the engine");

    /// <inheritdoc />
    public bool CalendarCountryById(long countryId)
        => throw Refuse(nameof(CalendarCountryById), "no economic-calendar data source is available to the engine");

    /// <inheritdoc />
    public int CalendarValueHistory(long fromDate, long toDate)
        => throw Refuse(nameof(CalendarValueHistory), "no economic-calendar data source is available to the engine");

    /// <inheritdoc />
    public int CalendarValueLast(ref ulong change)
        => throw Refuse(nameof(CalendarValueLast), "no economic-calendar data source is available to the engine");

    /// <inheritdoc />
    public int CalendarEventHistory(long fromDate, long toDate)
        => throw Refuse(nameof(CalendarEventHistory), "no economic-calendar data source is available to the engine");

    private delegate int SeriesCopy<T>(string symbol, int timeframe, Mql5CopyRange range, ref T[] target);

    private int CopySeriesCore<T>(string? symbol, int timeframe, Mql5CopyRange range, ref T[]? target, SeriesCopy<T> copy)
    {
        T[] buffer = target ?? [];
        if (range.Kind != Mql5CopyRangeKind.TimeRange && range.Count > 0 && buffer.Length < range.Count)
        {
            Array.Resize(ref buffer, range.Count);
        }

        int written = copy(Resolve(symbol), Timeframe(timeframe), range, ref buffer);
        target = buffer;
        return Finish(written, target);
    }

    private int CopyRatesCore(string? symbol, int timeframe, Mql5CopyRange range, ref Mql5Rates[]? target)
    {
        Mql5Rates[] buffer = target ?? [];
        if (range.Kind != Mql5CopyRangeKind.TimeRange && range.Count > 0 && buffer.Length < range.Count)
        {
            Array.Resize(ref buffer, range.Count);
        }

        int written = context.CopyRates(Resolve(symbol), Timeframe(timeframe), range, ref buffer);
        target = buffer;
        return Finish(written, target);
    }

    // The engine fills oldest-first. A target the strategy flagged with
    // ArraySetAsSeries expects newest-first, so it is reversed here rather than at
    // every call site.
    private int Finish<T>(int written, T[]? target)
    {
        if (written < 0)
        {
            SetError(Mql5ErrorCodes.IndicatorDataNotFound);
            return written;
        }

        if (written > 1 && target is not null && IsSeriesArray(target))
        {
            Array.Reverse(target, 0, Math.Min(written, target.Length));
        }

        return written;
    }

    private int Timeframe(int timeframe)
        => timeframe == Mql5Constants.Timeframes.Current ? context.Period : timeframe;
}
