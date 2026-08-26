using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Indicators;

/// <summary>
/// <c>iMomentum</c>. Buffer 0 carries <c>100 * price / price[period bars ago]</c>, the ratio form
/// MetaTrader publishes, so an unchanged price reads as 100 rather than as zero.
/// </summary>
public sealed class Mql5MomentumIndicator : Mql5IndicatorBase
{
    private readonly int period;
    private readonly Mql5AppliedPrice applied;
    private readonly RollingWindow window;

    /// <summary>Initializes the momentum oscillator.</summary>
    public Mql5MomentumIndicator(int period, Mql5AppliedPrice applied)
        : base("iMomentum", 1)
    {
        this.period = Math.Max(1, period);
        this.applied = applied;

        // One extra slot so the window holds the reference bar and the current bar at once.
        window = new RollingWindow(this.period + 1);
    }

    /// <inheritdoc />
    protected override void Compute(in Mql5Bar bar)
    {
        window.Add(AppliedPrice(bar, applied));

        if (!window.IsFull)
        {
            Push(0, double.NaN);
            return;
        }

        double reference = window[0];
        Push(0, reference == 0.0 ? double.NaN : 100.0 * window[period] / reference);
    }
}
