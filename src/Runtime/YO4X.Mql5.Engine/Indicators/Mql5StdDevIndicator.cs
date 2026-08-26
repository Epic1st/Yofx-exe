using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Indicators;

/// <summary>
/// <c>iStdDev</c>. Buffer 0 carries the standard deviation of the applied price around the moving
/// average of the requested method and period.
/// </summary>
/// <remarks>
/// MetaTrader centres the deviation on the moving average value of the current bar rather than on
/// the arithmetic mean of the window; the two coincide only for <see cref="Mql5MaMethod.Sma"/>.
/// The sum of squares is divided by the period, so this is the population deviation.
/// <c>shift</c> displaces the published series forward in time, the same convention
/// <see cref="Mql5MovingAverageIndicator"/> uses in this engine.
/// </remarks>
public sealed class Mql5StdDevIndicator : Mql5IndicatorBase
{
    private readonly int period;
    private readonly int shift;
    private readonly Mql5AppliedPrice applied;
    private readonly MovingAverageCalculator average;
    private readonly RollingWindow window;
    private readonly List<double> raw = [];

    /// <summary>Initializes the standard deviation.</summary>
    public Mql5StdDevIndicator(int period, int shift, Mql5MaMethod method, Mql5AppliedPrice applied)
        : base("iStdDev", 1)
    {
        this.period = Math.Max(1, period);
        this.shift = Math.Max(0, shift);
        this.applied = applied;
        average = new MovingAverageCalculator(this.period, method);
        window = new RollingWindow(this.period);
    }

    /// <inheritdoc />
    protected override void Compute(in Mql5Bar bar)
    {
        double price = AppliedPrice(bar, applied);
        window.Add(price);
        double mean = average.Add(price);

        if (!window.IsFull || double.IsNaN(mean))
        {
            raw.Add(double.NaN);
        }
        else
        {
            double accumulator = 0.0;
            for (int index = 0; index < period; index++)
            {
                double difference = window[index] - mean;
                accumulator += difference * difference;
            }

            raw.Add(Math.Sqrt(accumulator / period));
        }

        int source = raw.Count - 1 - shift;
        Push(0, source >= 0 ? raw[source] : double.NaN);
    }
}
