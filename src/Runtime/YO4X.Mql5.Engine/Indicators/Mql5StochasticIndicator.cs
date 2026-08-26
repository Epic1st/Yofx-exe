using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Indicators;

/// <summary>Price extremes the stochastic oscillator measures against.</summary>
public enum Mql5StochasticPriceField
{
    /// <summary>Use the bar high and low (<c>STO_LOWHIGH</c>).</summary>
    LowHigh = 0,

    /// <summary>Use the bar close for both extremes (<c>STO_CLOSECLOSE</c>).</summary>
    CloseClose = 1,
}

/// <summary>
/// <c>iStochastic</c>. Buffer 0 is the main %K line, buffer 1 the %D signal line.
/// </summary>
/// <remarks>
/// The slowing is applied MetaTrader style, by averaging the numerator and the denominator
/// separately: main = 100 * MA(close - lowest, slowing) / MA(highest - lowest, slowing).
/// </remarks>
public sealed class Mql5StochasticIndicator : Mql5IndicatorBase
{
    private readonly int kPeriod;
    private readonly Mql5StochasticPriceField priceField;
    private readonly RollingWindow highs;
    private readonly RollingWindow lows;
    private readonly MovingAverageCalculator numerator;
    private readonly MovingAverageCalculator denominator;
    private readonly MovingAverageCalculator signal;

    /// <summary>Initializes the stochastic oscillator.</summary>
    public Mql5StochasticIndicator(
        int kPeriod,
        int dPeriod,
        int slowing,
        Mql5MaMethod method,
        Mql5StochasticPriceField priceField)
        : base("iStochastic", 2)
    {
        this.kPeriod = Math.Max(1, kPeriod);
        this.priceField = priceField;
        highs = new RollingWindow(this.kPeriod);
        lows = new RollingWindow(this.kPeriod);
        numerator = new MovingAverageCalculator(Math.Max(1, slowing), Mql5MaMethod.Sma);
        denominator = new MovingAverageCalculator(Math.Max(1, slowing), Mql5MaMethod.Sma);
        signal = new MovingAverageCalculator(Math.Max(1, dPeriod), method);
    }

    /// <inheritdoc />
    protected override void Compute(in Mql5Bar bar)
    {
        double high = priceField == Mql5StochasticPriceField.CloseClose ? bar.Close : bar.High;
        double low = priceField == Mql5StochasticPriceField.CloseClose ? bar.Close : bar.Low;

        highs.Add(high);
        lows.Add(low);

        if (highs.Count < kPeriod)
        {
            Push(0, double.NaN);
            Push(1, double.NaN);
            return;
        }

        double highest = highs.Highest();
        double lowest = lows.Lowest();

        double smoothedNumerator = numerator.Add(bar.Close - lowest);
        double smoothedDenominator = denominator.Add(highest - lowest);

        if (double.IsNaN(smoothedNumerator) || double.IsNaN(smoothedDenominator))
        {
            Push(0, double.NaN);
            Push(1, double.NaN);
            return;
        }

        double main = smoothedDenominator <= 0.0 ? 50.0 : 100.0 * smoothedNumerator / smoothedDenominator;
        Push(0, main);
        Push(1, signal.Add(main));
    }
}
