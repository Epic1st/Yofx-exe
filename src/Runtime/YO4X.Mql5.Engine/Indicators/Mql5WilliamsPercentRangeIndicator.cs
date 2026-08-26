using YO4X.Mql5.Engine.Feed;

namespace YO4X.Mql5.Engine.Indicators;

/// <summary>
/// <c>iWPR</c>, Williams Percent Range. Buffer 0 carries
/// <c>-100 * (highest - close) / (highest - lowest)</c> over the calculation period, so the line
/// runs from -100 at the bottom of the range to zero at the top. A period with no range at all
/// reads as zero, which is what MetaTrader publishes for that degenerate case.
/// </summary>
public sealed class Mql5WilliamsPercentRangeIndicator : Mql5IndicatorBase
{
    private readonly RollingWindow highs;
    private readonly RollingWindow lows;

    /// <summary>Initializes the Williams percent range.</summary>
    public Mql5WilliamsPercentRangeIndicator(int period)
        : base("iWPR", 1)
    {
        Period = Math.Max(1, period);
        highs = new RollingWindow(Period);
        lows = new RollingWindow(Period);
    }

    /// <summary>Gets the calculation period.</summary>
    public int Period { get; }

    /// <inheritdoc />
    protected override void Compute(in Mql5Bar bar)
    {
        highs.Add(bar.High);
        lows.Add(bar.Low);

        if (!highs.IsFull)
        {
            Push(0, double.NaN);
            return;
        }

        double highest = highs.Highest();
        double lowest = lows.Lowest();
        double range = highest - lowest;

        Push(0, range <= 0.0 ? 0.0 : -100.0 * (highest - bar.Close) / range);
    }
}
