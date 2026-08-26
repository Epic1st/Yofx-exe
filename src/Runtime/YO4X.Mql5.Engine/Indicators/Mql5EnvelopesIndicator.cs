using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Indicators;

/// <summary>
/// <c>iEnvelopes</c>. Buffer 0 is the upper band and buffer 1 the lower band, matching the
/// MetaTrader buffer order (<c>UPPER_LINE</c>, <c>LOWER_LINE</c>). The deviation is a percentage:
/// the bands are <c>MA * (1 +/- deviation / 100)</c>.
/// </summary>
/// <remarks>
/// <c>shift</c> displaces the published series forward in time, the same convention
/// <see cref="Mql5MovingAverageIndicator"/> uses in this engine.
/// </remarks>
public sealed class Mql5EnvelopesIndicator : Mql5IndicatorBase
{
    private readonly int shift;
    private readonly double deviation;
    private readonly Mql5AppliedPrice applied;
    private readonly MovingAverageCalculator average;
    private readonly List<double> upperSeries = [];
    private readonly List<double> lowerSeries = [];

    /// <summary>Initializes the envelopes.</summary>
    public Mql5EnvelopesIndicator(
        int period,
        int shift,
        Mql5MaMethod method,
        Mql5AppliedPrice applied,
        double deviation)
        : base("iEnvelopes", 2)
    {
        this.shift = Math.Max(0, shift);
        this.deviation = deviation;
        this.applied = applied;
        average = new MovingAverageCalculator(Math.Max(1, period), method);
    }

    /// <inheritdoc />
    protected override void Compute(in Mql5Bar bar)
    {
        double middle = average.Add(AppliedPrice(bar, applied));

        if (double.IsNaN(middle))
        {
            upperSeries.Add(double.NaN);
            lowerSeries.Add(double.NaN);
        }
        else
        {
            double fraction = deviation / 100.0;
            upperSeries.Add(middle * (1.0 + fraction));
            lowerSeries.Add(middle * (1.0 - fraction));
        }

        int source = upperSeries.Count - 1 - shift;
        Push(0, source >= 0 ? upperSeries[source] : double.NaN);
        Push(1, source >= 0 ? lowerSeries[source] : double.NaN);
    }
}
