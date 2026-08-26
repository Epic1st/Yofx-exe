using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Indicators;

/// <summary>
/// <c>iCCI</c>. Commodity channel index over the applied price, which defaults to the typical
/// price, using Lambert's 0.015 constant and the mean absolute deviation.
/// </summary>
public sealed class Mql5CciIndicator : Mql5IndicatorBase
{
    private const double LambertConstant = 0.015;

    private readonly int period;
    private readonly Mql5AppliedPrice applied;
    private readonly RollingWindow window;

    /// <summary>Initializes the commodity channel index.</summary>
    public Mql5CciIndicator(int period, Mql5AppliedPrice applied)
        : base("iCCI", 1)
    {
        this.period = Math.Max(1, period);
        this.applied = applied;
        window = new RollingWindow(this.period);
    }

    /// <inheritdoc />
    protected override void Compute(in Mql5Bar bar)
    {
        double price = AppliedPrice(bar, applied);
        window.Add(price);

        if (!window.IsFull)
        {
            Push(0, double.NaN);
            return;
        }

        double mean = window.Sum / period;
        double deviation = window.MeanAbsoluteDeviation();

        Push(0, deviation <= 0.0 ? 0.0 : (price - mean) / (LambertConstant * deviation));
    }
}
