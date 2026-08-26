using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Indicators;

/// <summary>
/// <c>iATR</c>. True range of the first bar is its high minus low, because no previous close
/// exists; from then on it is the usual three-way maximum. The default smoothing is Wilder's,
/// which is the canonical ATR; pass <see cref="Mql5MaMethod.Sma"/> for the plain moving average of
/// true range that some MetaTrader builds ship.
/// </summary>
public sealed class Mql5AtrIndicator : Mql5IndicatorBase
{
    private readonly MovingAverageCalculator calculator;
    private double previousClose;
    private bool hasPrevious;

    /// <summary>Initializes the average true range with Wilder smoothing.</summary>
    public Mql5AtrIndicator(int period)
        : this(period, Mql5MaMethod.Smma)
    {
    }

    /// <summary>Initializes the average true range with an explicit smoothing method.</summary>
    public Mql5AtrIndicator(int period, Mql5MaMethod smoothing)
        : base("iATR", 1)
    {
        Period = Math.Max(1, period);
        Smoothing = smoothing;
        calculator = new MovingAverageCalculator(Period, smoothing);
    }

    /// <summary>Gets the averaging period.</summary>
    public int Period { get; }

    /// <summary>Gets the smoothing applied to the true range series.</summary>
    public Mql5MaMethod Smoothing { get; }

    /// <inheritdoc />
    protected override void Compute(in Mql5Bar bar)
    {
        double trueRange = hasPrevious
            ? Math.Max(
                bar.High - bar.Low,
                Math.Max(Math.Abs(bar.High - previousClose), Math.Abs(bar.Low - previousClose)))
            : bar.High - bar.Low;

        previousClose = bar.Close;
        hasPrevious = true;
        Push(0, calculator.Add(trueRange));
    }
}
