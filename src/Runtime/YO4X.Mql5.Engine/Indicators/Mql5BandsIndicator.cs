using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Indicators;

/// <summary>
/// <c>iBands</c>. Buffer 0 is the base line, 1 the upper band, 2 the lower band, matching the
/// MetaTrader buffer order. The band width uses the population standard deviation.
/// </summary>
public sealed class Mql5BandsIndicator : Mql5IndicatorBase
{
    private readonly int period;
    private readonly int shift;
    private readonly double deviation;
    private readonly Mql5AppliedPrice applied;
    private readonly RollingWindow window;
    private readonly List<double> baseSeries = [];
    private readonly List<double> upperSeries = [];
    private readonly List<double> lowerSeries = [];

    /// <summary>Initializes the Bollinger bands.</summary>
    public Mql5BandsIndicator(int period, int shift, double deviation, Mql5AppliedPrice applied)
        : base("iBands", 3)
    {
        this.period = Math.Max(1, period);
        this.shift = Math.Max(0, shift);
        this.deviation = deviation;
        this.applied = applied;
        window = new RollingWindow(this.period);
    }

    /// <inheritdoc />
    protected override void Compute(in Mql5Bar bar)
    {
        window.Add(AppliedPrice(bar, applied));

        if (!window.IsFull)
        {
            baseSeries.Add(double.NaN);
            upperSeries.Add(double.NaN);
            lowerSeries.Add(double.NaN);
        }
        else
        {
            double middle = window.Sum / period;
            double spread = deviation * window.PopulationStandardDeviation();
            baseSeries.Add(middle);
            upperSeries.Add(middle + spread);
            lowerSeries.Add(middle - spread);
        }

        int source = baseSeries.Count - 1 - shift;
        Push(0, source >= 0 ? baseSeries[source] : double.NaN);
        Push(1, source >= 0 ? upperSeries[source] : double.NaN);
        Push(2, source >= 0 ? lowerSeries[source] : double.NaN);
    }
}
