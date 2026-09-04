using YO4X.Mql5.Engine.Feed;

namespace YO4X.Mql5.Engine.Indicators;

/// <summary>
/// <c>iRVI</c>, the Relative Vigor Index. Buffer 0 is the main line and buffer 1 the signal line,
/// matching the MetaTrader buffer order (<c>MAIN_LINE</c>, <c>SIGNAL_LINE</c>).
/// </summary>
/// <remarks>
/// Both numerator and denominator are the four-bar triangular average that MetaTrader uses,
/// <c>(x[0] + 2 * x[1] + 2 * x[2] + x[3]) / 6</c>, taken over <c>close - open</c> and over
/// <c>high - low</c> respectively. Those two smoothed series are then summed over the averaging
/// period and divided. The signal line is the same four-bar triangular average of the main line.
/// A period whose ranges sum to zero falls back to the numerator alone, as MetaTrader does.
/// </remarks>
public sealed class Mql5RelativeVigorIndexIndicator : Mql5IndicatorBase
{
    private const int TriangleSpan = 4;

    private readonly RollingWindow bodies = new(TriangleSpan);
    private readonly RollingWindow ranges = new(TriangleSpan);
    private readonly RollingWindow numerators;
    private readonly RollingWindow denominators;
    private readonly RollingWindow mains = new(TriangleSpan);
    private int mainCount;

    /// <summary>Initializes the relative vigor index.</summary>
    public Mql5RelativeVigorIndexIndicator(int period)
        : base("iRVI", 2)
    {
        Period = Math.Max(1, period);
        numerators = new RollingWindow(Period);
        denominators = new RollingWindow(Period);
    }

    /// <summary>Gets the averaging period.</summary>
    public int Period { get; }

    /// <inheritdoc />
    protected override void Compute(in Mql5Bar bar)
    {
        bodies.Add(bar.Close - bar.Open);
        ranges.Add(bar.High - bar.Low);

        if (!bodies.IsFull)
        {
            Push(0, double.NaN);
            Push(1, double.NaN);
            return;
        }

        numerators.Add(Triangular(bodies));
        denominators.Add(Triangular(ranges));

        if (!numerators.IsFull)
        {
            Push(0, double.NaN);
            Push(1, double.NaN);
            return;
        }

        double numerator = numerators.Sum;
        double denominator = denominators.Sum;
        double main = Math.Abs(denominator) < 1e-12 ? 0.0 : numerator / denominator;

        mains.Add(main);
        mainCount++;
        Push(0, main);
        Push(1, mainCount >= TriangleSpan ? Triangular(mains) : double.NaN);
    }

    /// <summary>
    /// The MetaTrader four-bar triangular average of a window whose newest value sits last.
    /// </summary>
    private static double Triangular(RollingWindow window) =>
        (window[3] + (2.0 * window[2]) + (2.0 * window[1]) + window[0]) / 6.0;
}
