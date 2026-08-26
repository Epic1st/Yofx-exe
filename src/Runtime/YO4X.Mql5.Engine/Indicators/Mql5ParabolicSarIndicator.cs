using YO4X.Mql5.Engine.Feed;

namespace YO4X.Mql5.Engine.Indicators;

/// <summary>
/// <c>iSAR</c>, the Parabolic Stop And Reverse. Buffer 0 carries the stop level for the bar.
/// </summary>
/// <remarks>
/// The original Wilder rules are used. The first bar has no value. The second bar opens the
/// series: the direction is long when it closed at or above the previous close, the extreme point
/// is the better of the two highs (or lows when short) and the stop starts at the worse of the two
/// lows (or highs). From the third bar on, the stop advances by
/// <c>sar + af * (extreme - sar)</c>, is clamped so it never moves inside the range of the two
/// preceding bars, and reverses when the bar trades through it. On a reversal the stop for that
/// bar is the extreme point of the trend that just ended, the extreme point restarts at the
/// current bar and the acceleration factor drops back to the step. The factor rises by
/// <c>step</c> only on a bar that sets a new extreme, and never above <c>maximum</c>.
/// </remarks>
public sealed class Mql5ParabolicSarIndicator : Mql5IndicatorBase
{
    private readonly double step;
    private readonly double maximum;

    private double previousHigh;
    private double previousLow;
    private double previousClose;
    private double olderHigh;
    private double olderLow;

    private double stop;
    private double extreme;
    private double acceleration;
    private bool isLong;
    private int bars;

    /// <summary>Initializes the parabolic stop and reverse.</summary>
    public Mql5ParabolicSarIndicator(double step, double maximum)
        : base("iSAR", 1)
    {
        this.step = step > 0.0 ? step : 0.02;
        this.maximum = maximum > 0.0 ? maximum : 0.2;
    }

    /// <inheritdoc />
    protected override void Compute(in Mql5Bar bar)
    {
        if (bars == 0)
        {
            Push(0, double.NaN);
            Remember(bar);
            bars++;
            return;
        }

        if (bars == 1)
        {
            isLong = bar.Close >= previousClose;
            acceleration = step;

            if (isLong)
            {
                extreme = Math.Max(previousHigh, bar.High);
                stop = Math.Min(previousLow, bar.Low);
            }
            else
            {
                extreme = Math.Min(previousLow, bar.Low);
                stop = Math.Max(previousHigh, bar.High);
            }

            Push(0, stop);
            Remember(bar);
            bars++;
            return;
        }

        double advanced = stop + (acceleration * (extreme - stop));

        if (isLong)
        {
            advanced = Math.Min(advanced, Math.Min(previousLow, olderLow));

            if (bar.Low < advanced)
            {
                advanced = extreme;
                isLong = false;
                extreme = bar.Low;
                acceleration = step;
            }
            else if (bar.High > extreme)
            {
                extreme = bar.High;
                acceleration = Math.Min(acceleration + step, maximum);
            }
        }
        else
        {
            advanced = Math.Max(advanced, Math.Max(previousHigh, olderHigh));

            if (bar.High > advanced)
            {
                advanced = extreme;
                isLong = true;
                extreme = bar.High;
                acceleration = step;
            }
            else if (bar.Low < extreme)
            {
                extreme = bar.Low;
                acceleration = Math.Min(acceleration + step, maximum);
            }
        }

        stop = advanced;
        Push(0, stop);
        Remember(bar);
        bars++;
    }

    private void Remember(in Mql5Bar bar)
    {
        olderHigh = previousHigh;
        olderLow = previousLow;
        previousHigh = bar.High;
        previousLow = bar.Low;
        previousClose = bar.Close;
    }
}
