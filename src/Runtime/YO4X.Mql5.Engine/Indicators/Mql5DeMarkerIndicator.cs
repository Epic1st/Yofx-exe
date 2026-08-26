using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Indicators;

/// <summary>
/// <c>iDeMarker</c>. Buffer 0 carries <c>SMA(DeMax) / (SMA(DeMax) + SMA(DeMin))</c>, so the line
/// lives between zero and one.
/// </summary>
/// <remarks>
/// <c>DeMax</c> is the rise of the high over the previous high, or zero when the high did not
/// rise; <c>DeMin</c> is the fall of the low below the previous low, or zero when the low did not
/// fall. Both are averaged simply, as the bundled MetaTrader DeMarker does. A period in which
/// neither extreme moved has a zero denominator and reads as zero.
/// </remarks>
public sealed class Mql5DeMarkerIndicator : Mql5IndicatorBase
{
    private readonly MovingAverageCalculator maximums;
    private readonly MovingAverageCalculator minimums;

    private double previousHigh;
    private double previousLow;
    private bool hasPrevious;

    /// <summary>Initializes the DeMarker oscillator.</summary>
    public Mql5DeMarkerIndicator(int period)
        : base("iDeMarker", 1)
    {
        Period = Math.Max(1, period);
        maximums = new MovingAverageCalculator(Period, Mql5MaMethod.Sma);
        minimums = new MovingAverageCalculator(Period, Mql5MaMethod.Sma);
    }

    /// <summary>Gets the averaging period.</summary>
    public int Period { get; }

    /// <inheritdoc />
    protected override void Compute(in Mql5Bar bar)
    {
        if (!hasPrevious)
        {
            previousHigh = bar.High;
            previousLow = bar.Low;
            hasPrevious = true;
            Push(0, double.NaN);
            return;
        }

        double maximum = bar.High > previousHigh ? bar.High - previousHigh : 0.0;
        double minimum = bar.Low < previousLow ? previousLow - bar.Low : 0.0;

        previousHigh = bar.High;
        previousLow = bar.Low;

        double averageMaximum = maximums.Add(maximum);
        double averageMinimum = minimums.Add(minimum);

        if (double.IsNaN(averageMaximum) || double.IsNaN(averageMinimum))
        {
            Push(0, double.NaN);
            return;
        }

        double total = averageMaximum + averageMinimum;
        Push(0, total <= 0.0 ? 0.0 : averageMaximum / total);
    }
}
