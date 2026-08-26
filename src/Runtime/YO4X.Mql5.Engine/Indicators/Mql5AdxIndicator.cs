using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Indicators;

/// <summary>
/// <c>iADX</c>, the average directional movement index. Buffer 0 is the ADX main line, buffer 1
/// the +DI line and buffer 2 the -DI line, matching the MetaTrader buffer order
/// (<c>MAIN_LINE</c>, <c>PLUSDI_LINE</c>, <c>MINUSDI_LINE</c>).
/// </summary>
/// <remarks>
/// The original Wilder construction is used throughout. Directional movement of a bar is the
/// larger of the two moves and only that one: <c>+DM</c> is <c>high - previousHigh</c> when it
/// exceeds <c>previousLow - low</c> and is positive, <c>-DM</c> is the mirror, and both are zero
/// when the moves tie or when the bar is an inside bar. True range, <c>+DM</c> and <c>-DM</c> are
/// smoothed with the Wilder recursive average seeded on the plain mean of the first
/// <c>period</c> samples, which leaves the directional indices
/// <c>100 * smoothed(DM) / smoothed(TR)</c> identical to the Wilder running-sum form. <c>DX</c>
/// is then smoothed the same way to give ADX.
/// </remarks>
public sealed class Mql5AdxIndicator : Mql5IndicatorBase
{
    private readonly MovingAverageCalculator trueRange;
    private readonly MovingAverageCalculator plusMovement;
    private readonly MovingAverageCalculator minusMovement;
    private readonly MovingAverageCalculator directionalIndex;

    private double previousHigh;
    private double previousLow;
    private double previousClose;
    private bool hasPrevious;

    /// <summary>Initializes the average directional movement index.</summary>
    public Mql5AdxIndicator(int period)
        : this("iADX", period)
    {
    }

    /// <summary>Initializes the index under an explicit MQL5 function name.</summary>
    /// <param name="name">The MQL5 function this instance answers for.</param>
    /// <param name="period">The Wilder averaging period.</param>
    public Mql5AdxIndicator(string name, int period)
        : base(name, 3)
    {
        Period = Math.Max(1, period);
        trueRange = new MovingAverageCalculator(Period, Mql5MaMethod.Smma);
        plusMovement = new MovingAverageCalculator(Period, Mql5MaMethod.Smma);
        minusMovement = new MovingAverageCalculator(Period, Mql5MaMethod.Smma);
        directionalIndex = new MovingAverageCalculator(Period, Mql5MaMethod.Smma);
    }

    /// <summary>Gets the averaging period.</summary>
    public int Period { get; }

    /// <inheritdoc />
    protected override void Compute(in Mql5Bar bar)
    {
        if (!hasPrevious)
        {
            previousHigh = bar.High;
            previousLow = bar.Low;
            previousClose = bar.Close;
            hasPrevious = true;
            PushAll(double.NaN, double.NaN, double.NaN);
            return;
        }

        double up = bar.High - previousHigh;
        double down = previousLow - bar.Low;
        double plus = up > down && up > 0.0 ? up : 0.0;
        double minus = down > up && down > 0.0 ? down : 0.0;

        double range = Math.Max(
            bar.High - bar.Low,
            Math.Max(Math.Abs(bar.High - previousClose), Math.Abs(bar.Low - previousClose)));

        previousHigh = bar.High;
        previousLow = bar.Low;
        previousClose = bar.Close;

        double smoothedRange = trueRange.Add(range);
        double smoothedPlus = plusMovement.Add(plus);
        double smoothedMinus = minusMovement.Add(minus);

        if (double.IsNaN(smoothedRange) || double.IsNaN(smoothedPlus) || double.IsNaN(smoothedMinus))
        {
            PushAll(double.NaN, double.NaN, double.NaN);
            return;
        }

        double plusIndex = smoothedRange > 0.0 ? 100.0 * smoothedPlus / smoothedRange : 0.0;
        double minusIndex = smoothedRange > 0.0 ? 100.0 * smoothedMinus / smoothedRange : 0.0;

        double total = plusIndex + minusIndex;
        double index = total > 0.0 ? 100.0 * Math.Abs(plusIndex - minusIndex) / total : 0.0;

        PushAll(directionalIndex.Add(index), plusIndex, minusIndex);
    }

    private void PushAll(double main, double plusIndex, double minusIndex)
    {
        Push(0, main);
        Push(1, plusIndex);
        Push(2, minusIndex);
    }
}
