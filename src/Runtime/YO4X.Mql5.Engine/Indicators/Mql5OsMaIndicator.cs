using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Indicators;

/// <summary>
/// <c>iOsMA</c>, the moving average of oscillator. Buffer 0 carries the MACD main line minus its
/// signal line.
/// </summary>
/// <remarks>
/// The main line is the fast EMA minus the slow EMA and the signal line is a simple moving
/// average of it, the same pair <see cref="Mql5MacdIndicator"/> publishes, so <c>iOsMA</c> and
/// <c>iMACD</c> can never disagree about the same bar.
/// </remarks>
public sealed class Mql5OsMaIndicator : Mql5IndicatorBase
{
    private readonly MovingAverageCalculator fast;
    private readonly MovingAverageCalculator slow;
    private readonly MovingAverageCalculator signal;
    private readonly Mql5AppliedPrice applied;

    /// <summary>Initializes the moving average of oscillator.</summary>
    public Mql5OsMaIndicator(int fastPeriod, int slowPeriod, int signalPeriod, Mql5AppliedPrice applied)
        : base("iOsMA", 1)
    {
        fast = new MovingAverageCalculator(Math.Max(1, fastPeriod), Mql5MaMethod.Ema);
        slow = new MovingAverageCalculator(Math.Max(1, slowPeriod), Mql5MaMethod.Ema);
        signal = new MovingAverageCalculator(Math.Max(1, signalPeriod), Mql5MaMethod.Sma);
        this.applied = applied;
    }

    /// <inheritdoc />
    protected override void Compute(in Mql5Bar bar)
    {
        double price = AppliedPrice(bar, applied);
        double fastValue = fast.Add(price);
        double slowValue = slow.Add(price);

        if (double.IsNaN(fastValue) || double.IsNaN(slowValue))
        {
            Push(0, double.NaN);
            return;
        }

        double main = fastValue - slowValue;
        double signalValue = signal.Add(main);

        Push(0, double.IsNaN(signalValue) ? double.NaN : main - signalValue);
    }
}
