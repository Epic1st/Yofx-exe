using YO4X.Mql5.Engine.Feed;

namespace YO4X.Mql5.Engine.Indicators;

/// <summary>
/// <c>iFractals</c>. Buffer 0 carries upper fractals and buffer 1 lower fractals, matching the
/// MetaTrader buffer order (<c>UPPER_LINE</c>, <c>LOWER_LINE</c>).
/// </summary>
/// <remarks>
/// A bar is an upper fractal when its high is strictly above the highs of the two bars either
/// side of it, and a lower fractal when its low is strictly below the lows of the two bars either
/// side. Bars that are not fractals keep <see cref="Mql5IndicatorBase.EmptyValue"/>.
/// <para>
/// A fractal cannot be known until two more bars have closed, so the value is written back into
/// the bar it belongs to once those bars arrive, exactly as MetaTrader recalculates its history.
/// Reading buffer 0 at back index zero or one therefore never shows a fractal; back index two is
/// the first slot that can carry one.
/// </para>
/// </remarks>
public sealed class Mql5FractalsIndicator : Mql5IndicatorBase
{
    private const int Wing = 2;

    private readonly List<double> highs = [];
    private readonly List<double> lows = [];

    /// <summary>Initializes the fractals indicator. It takes no parameters in MQL5.</summary>
    public Mql5FractalsIndicator()
        : base("iFractals", 2)
    {
    }

    /// <inheritdoc />
    protected override void Compute(in Mql5Bar bar)
    {
        highs.Add(bar.High);
        lows.Add(bar.Low);

        Push(0, double.NaN);
        Push(1, double.NaN);

        int centre = highs.Count - 1 - Wing;
        if (centre < Wing)
        {
            return;
        }

        if (IsPeak(highs, centre))
        {
            Revise(0, centre, highs[centre]);
        }

        if (IsTrough(lows, centre))
        {
            Revise(1, centre, lows[centre]);
        }
    }

    private static bool IsPeak(List<double> series, int centre)
    {
        double value = series[centre];
        for (int offset = 1; offset <= Wing; offset++)
        {
            if (value <= series[centre - offset] || value <= series[centre + offset])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsTrough(List<double> series, int centre)
    {
        double value = series[centre];
        for (int offset = 1; offset <= Wing; offset++)
        {
            if (value >= series[centre - offset] || value >= series[centre + offset])
            {
                return false;
            }
        }

        return true;
    }
}
