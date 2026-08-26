using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Indicators;

/// <summary>
/// <c>iMACD</c>. Buffer 0 is the main line, buffer 1 the signal line.
/// </summary>
/// <remarks>
/// The main line is the fast EMA minus the slow EMA. MetaTrader's bundled MACD smooths the signal
/// line with a simple moving average rather than an exponential one, so that is the default here;
/// pass <see cref="Mql5MaMethod.Ema"/> to get the textbook exponential signal instead.
/// </remarks>
public sealed class Mql5MacdIndicator : Mql5IndicatorBase
{
    private readonly MovingAverageCalculator fast;
    private readonly MovingAverageCalculator slow;
    private readonly MovingAverageCalculator signal;
    private readonly Mql5AppliedPrice applied;

    /// <summary>Initializes the MACD with the MetaTrader simple signal line.</summary>
    public Mql5MacdIndicator(int fastPeriod, int slowPeriod, int signalPeriod, Mql5AppliedPrice applied)
        : this(fastPeriod, slowPeriod, signalPeriod, applied, Mql5MaMethod.Sma)
    {
    }

    /// <summary>Initializes the MACD with an explicit signal smoothing method.</summary>
    public Mql5MacdIndicator(
        int fastPeriod,
        int slowPeriod,
        int signalPeriod,
        Mql5AppliedPrice applied,
        Mql5MaMethod signalMethod)
        : base("iMACD", 2)
    {
        fast = new MovingAverageCalculator(Math.Max(1, fastPeriod), Mql5MaMethod.Ema);
        slow = new MovingAverageCalculator(Math.Max(1, slowPeriod), Mql5MaMethod.Ema);
        signal = new MovingAverageCalculator(Math.Max(1, signalPeriod), signalMethod);
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
            Push(1, double.NaN);
            return;
        }

        double main = fastValue - slowValue;
        Push(0, main);
        Push(1, signal.Add(main));
    }
}
