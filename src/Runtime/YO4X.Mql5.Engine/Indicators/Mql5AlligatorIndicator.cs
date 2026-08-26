using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Indicators;

/// <summary>
/// <c>iAlligator</c>. Buffer 0 is the jaw, buffer 1 the teeth and buffer 2 the lips, matching the
/// MetaTrader buffer order (<c>GATORJAW_LINE</c>, <c>GATORTEETH_LINE</c>,
/// <c>GATORLIPS_LINE</c>). The three lines are smoothed moving averages of the median price with
/// the classic 13/8, 8/5 and 5/3 period and shift defaults.
/// </summary>
/// <remarks>
/// Each shift displaces that line forward in time, the same convention
/// <see cref="Mql5MovingAverageIndicator"/> uses in this engine, so the value read at the current
/// bar is the average computed <c>shift</c> bars earlier.
/// </remarks>
public sealed class Mql5AlligatorIndicator : Mql5IndicatorBase
{
    private readonly Line jaw;
    private readonly Line teeth;
    private readonly Line lips;
    private readonly Mql5AppliedPrice applied;

    /// <summary>Initializes the alligator.</summary>
    public Mql5AlligatorIndicator(
        int jawPeriod,
        int jawShift,
        int teethPeriod,
        int teethShift,
        int lipsPeriod,
        int lipsShift,
        Mql5MaMethod method,
        Mql5AppliedPrice applied)
        : base("iAlligator", 3)
    {
        jaw = new Line(jawPeriod, jawShift, method);
        teeth = new Line(teethPeriod, teethShift, method);
        lips = new Line(lipsPeriod, lipsShift, method);
        this.applied = applied;
    }

    /// <inheritdoc />
    protected override void Compute(in Mql5Bar bar)
    {
        double price = AppliedPrice(bar, applied);
        Push(0, jaw.Add(price));
        Push(1, teeth.Add(price));
        Push(2, lips.Add(price));
    }

    private sealed class Line
    {
        private readonly MovingAverageCalculator calculator;
        private readonly int shift;
        private readonly List<double> raw = [];

        internal Line(int period, int shift, Mql5MaMethod method)
        {
            calculator = new MovingAverageCalculator(Math.Max(1, period), method);
            this.shift = Math.Max(0, shift);
        }

        internal double Add(double price)
        {
            raw.Add(calculator.Add(price));
            int source = raw.Count - 1 - shift;
            return source >= 0 ? raw[source] : double.NaN;
        }
    }
}
