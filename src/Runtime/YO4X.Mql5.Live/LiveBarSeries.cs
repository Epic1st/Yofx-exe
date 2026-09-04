using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Indicators;

namespace YO4X.Mql5.Live;

/// <summary>
/// The bars a live strategy sees: downloaded history, extended by quotes as they arrive.
///
/// <para>
/// A strategy that starts with no history is blind — an indicator needs its whole lookback
/// before it produces anything, so a freshly started bot would place no trades for hours and
/// then place them on half-formed values. So the series is seeded from the same downloaded
/// bars the backtest used, and live quotes only ever extend it.
/// </para>
///
/// <para>
/// A bar is published only when its period has fully elapsed. The bar currently forming is
/// held back deliberately: acting on a partial bar means acting on a high and low that have
/// not finished moving, and a backtest that used closed bars would never have made that trade.
/// </para>
/// </summary>
public sealed class LiveBarSeries
{
    private readonly List<Mql5Bar> bars;
    private readonly Dictionary<string, IMql5Indicator> indicators = [];
    private readonly List<IMql5Indicator> handles = [];
    private readonly TimeSpan period;
    private readonly int maximumBars;
    private readonly double point;

    private int inferredDigits;
    private DateTime formingOpenTime;
    private double formingOpen;
    private double formingHigh;
    private double formingLow;
    private double formingClose;
    private long formingTicks;
    private int formingSpread;
    private bool forming;

    /// <summary>Creates a series for one symbol and period.</summary>
    /// <param name="symbol">The instrument.</param>
    /// <param name="periodMinutes">The bar period, in minutes.</param>
    /// <param name="seed">Closed bars already known, oldest first.</param>
    /// <param name="maximumBars">How many bars to retain.</param>
    /// <param name="point">The symbol point size, or zero to infer from seed or quotes.</param>
    public LiveBarSeries(
        string symbol,
        int periodMinutes,
        IEnumerable<Mql5Bar> seed,
        int maximumBars = 5_000,
        double point = 0.0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(periodMinutes);
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBars, 2);

        Symbol = symbol;
        PeriodMinutes = periodMinutes;
        period = TimeSpan.FromMinutes(periodMinutes);
        this.maximumBars = maximumBars;
        this.point = point > 0.0 ? point : 0.0;
        bars = [.. seed];
        if (this.point <= 0.0)
        {
            foreach (Mql5Bar bar in bars)
            {
                inferredDigits = Math.Max(inferredDigits, InferDigits(bar.Open));
                inferredDigits = Math.Max(inferredDigits, InferDigits(bar.High));
                inferredDigits = Math.Max(inferredDigits, InferDigits(bar.Low));
                inferredDigits = Math.Max(inferredDigits, InferDigits(bar.Close));
            }
        }

        Trim();
        if (bars.Count > 0)
        {
            Mql5Bar latest = bars[^1];
            Bid = latest.Close;
            Ask = latest.Close + Math.Max(0, latest.Spread) * Point;
            LastQuoteTime = latest.Time;
        }
    }

    /// <summary>The instrument this series carries.</summary>
    public string Symbol { get; }

    /// <summary>The bar period, in minutes.</summary>
    public int PeriodMinutes { get; }

    /// <summary>The size of one point for this symbol.</summary>
    public double Point => point > 0.0 ? point : (inferredDigits > 0 ? 1.0 / Math.Pow(10.0, inferredDigits) : 1.0);

    /// <summary>How many closed bars are held.</summary>
    public int Count => bars.Count;

    /// <summary>The most recent bid seen, or the last close when no quote has arrived.</summary>
    public double Bid { get; private set; }

    /// <summary>The most recent ask seen, or the last close when no quote has arrived.</summary>
    public double Ask { get; private set; }

    /// <summary>The broker time of the most recent quote.</summary>
    public DateTime LastQuoteTime { get; private set; }

    /// <summary>A closed bar by shift, where zero is the most recent.</summary>
    /// <param name="shift">How many bars back to look.</param>
    public Mql5Bar At(int shift) =>
        shift >= 0 && shift < bars.Count ? bars[bars.Count - 1 - shift] : default;

    /// <summary>
    /// Folds one quote into the forming bar, and publishes the previous bar when the quote
    /// belongs to a later period.
    /// </summary>
    /// <param name="time">The quote's broker time.</param>
    /// <param name="bid">The bid.</param>
    /// <param name="ask">The ask.</param>
    /// <returns>True when a bar closed and the strategy should be ticked.</returns>
    public bool Accept(DateTime time, double bid, double ask)
    {
        if (bid <= 0 || ask <= 0)
        {
            return false;
        }

        Bid = bid;
        Ask = ask;
        LastQuoteTime = time;

        if (point <= 0.0)
        {
            inferredDigits = Math.Max(inferredDigits, Math.Max(InferDigits(bid), InferDigits(ask)));
        }

        double pt = Point;
        int spread = pt > 0.0 ? Math.Max(0, (int)Math.Round((ask - bid) / pt)) : 0;

        DateTime slot = FloorToPeriod(time);
        if (!forming)
        {
            StartBar(slot, bid, spread);
            return false;
        }

        if (slot > formingOpenTime)
        {
            Publish();
            StartBar(slot, bid, spread);
            return true;
        }

        formingHigh = Math.Max(formingHigh, bid);
        formingLow = Math.Min(formingLow, bid);
        formingClose = bid;
        formingTicks++;
        formingSpread = spread;
        return false;
    }

    private void StartBar(DateTime slot, double price, int spread)
    {
        formingOpenTime = slot;
        formingOpen = price;
        formingHigh = price;
        formingLow = price;
        formingClose = price;
        formingTicks = 1;
        formingSpread = spread;
        forming = true;
    }

    private void Publish()
    {
        var bar = new Mql5Bar(
            formingOpenTime,
            formingOpen,
            formingHigh,
            formingLow,
            formingClose,
            formingTicks,
            formingSpread);
        bars.Add(bar);
        foreach (IMql5Indicator indicator in handles)
        {
            indicator.Append(bar);
        }

        Trim();
    }

    private void Trim()
    {
        int excess = bars.Count - maximumBars;
        if (excess > 0)
        {
            bars.RemoveRange(0, excess);
        }
    }

    private DateTime FloorToPeriod(DateTime time)
    {
        long ticks = time.Ticks - (time.Ticks % period.Ticks);
        return new DateTime(ticks, time.Kind);
    }

    /// <summary>
    /// Resolves an indicator handle, building the indicator over the history already held so
    /// it is immediately usable rather than blind for its whole lookback.
    /// </summary>
    /// <param name="name">The MQL5 indicator function name.</param>
    /// <param name="parameters">Its arguments, as the strategy passed them.</param>
    public int ResolveIndicator(string name, IReadOnlyList<object?> parameters)
    {
        string key = Mql5IndicatorFactory.BuildKey(name, parameters);
        if (indicators.TryGetValue(key, out IMql5Indicator? existing))
        {
            return handles.IndexOf(existing) + 1;
        }

        IMql5Indicator? created = Mql5IndicatorFactory.Create(name, parameters);
        if (created is null)
        {
            return -1;
        }

        foreach (Mql5Bar bar in bars)
        {
            created.Append(bar);
        }

        indicators[key] = created;
        handles.Add(created);
        return handles.Count;
    }

    /// <summary>Copies indicator values oldest-first, matching the engine's own ordering.</summary>
    /// <param name="handle">The handle returned by <see cref="ResolveIndicator"/>.</param>
    /// <param name="buffer">Which buffer of the indicator to read.</param>
    /// <param name="start">How many bars back the newest requested value sits.</param>
    /// <param name="count">How many values to copy.</param>
    /// <param name="target">The destination array.</param>
    public int CopyBuffer(int handle, int buffer, int start, int count, double[] target)
    {
        if (target is null
            || count <= 0
            || start < 0
            || target.Length < count
            || handle < 1
            || handle > handles.Count)
        {
            return -1;
        }

        IMql5Indicator indicator = handles[handle - 1];
        if (buffer < 0 || buffer >= indicator.BufferCount || indicator.Count < start + count)
        {
            return -1;
        }

        for (int index = 0; index < count; index++)
        {
            target[index] = indicator.Value(buffer, start + count - 1 - index);
        }

        return count;
    }

    private static int InferDigits(double value)
    {
        if (value <= 0.0 || double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        try
        {
            return ((decimal)value).Scale;
        }
        catch
        {
            return 0;
        }
    }
}
