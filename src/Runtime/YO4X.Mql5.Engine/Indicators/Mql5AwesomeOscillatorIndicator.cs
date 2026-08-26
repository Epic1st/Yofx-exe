using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Indicators;

/// <summary>
/// <c>iAO</c>, the Awesome Oscillator. Buffer 0 carries the five-bar simple average of the median
/// price minus the thirty-four-bar simple average of the same, so the first value appears on the
/// thirty-fourth bar. The indicator takes no parameters in MQL5.
/// </summary>
public sealed class Mql5AwesomeOscillatorIndicator : Mql5IndicatorBase
{
    private const int FastPeriod = 5;
    private const int SlowPeriod = 34;

    private readonly MovingAverageCalculator fast = new(FastPeriod, Mql5MaMethod.Sma);
    private readonly MovingAverageCalculator slow = new(SlowPeriod, Mql5MaMethod.Sma);

    /// <summary>Initializes the awesome oscillator.</summary>
    public Mql5AwesomeOscillatorIndicator()
        : base("iAO", 1)
    {
    }

    /// <inheritdoc />
    protected override void Compute(in Mql5Bar bar)
    {
        double median = bar.Median;
        double fastValue = fast.Add(median);
        double slowValue = slow.Add(median);

        Push(0, double.IsNaN(fastValue) || double.IsNaN(slowValue) ? double.NaN : fastValue - slowValue);
    }
}
