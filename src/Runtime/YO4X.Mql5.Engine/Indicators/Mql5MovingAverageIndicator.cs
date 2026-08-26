using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Indicators;

/// <summary>
/// <c>iMA</c>. Buffer 0 carries the average. <c>shift</c> displaces the published series forward
/// in time exactly as MetaTrader does.
/// </summary>
public sealed class Mql5MovingAverageIndicator : Mql5IndicatorBase
{
    private readonly MovingAverageCalculator calculator;
    private readonly int shift;
    private readonly Mql5AppliedPrice applied;
    private readonly List<double> raw = [];

    /// <summary>Initializes the moving average.</summary>
    public Mql5MovingAverageIndicator(int period, int shift, Mql5MaMethod method, Mql5AppliedPrice applied)
        : base("iMA", 1)
    {
        Period = Math.Max(1, period);
        this.shift = Math.Max(0, shift);
        Method = method;
        this.applied = applied;
        calculator = new MovingAverageCalculator(Period, method);
    }

    /// <summary>Gets the averaging period.</summary>
    public int Period { get; }

    /// <summary>Gets the averaging method.</summary>
    public Mql5MaMethod Method { get; }

    /// <inheritdoc />
    protected override void Compute(in Mql5Bar bar)
    {
        raw.Add(calculator.Add(AppliedPrice(bar, applied)));
        int source = raw.Count - 1 - shift;
        Push(0, source >= 0 ? raw[source] : double.NaN);
    }
}
