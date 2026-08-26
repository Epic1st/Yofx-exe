using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Indicators;

/// <summary>
/// Incremental moving average over an arbitrary scalar stream.
/// </summary>
/// <remarks>
/// EMA and SMMA are seeded with the simple average of the first <c>period</c> samples, which is
/// what the MetaTrader moving average does, and only then switch to their recursive form. Before
/// the window fills, <see cref="Add"/> returns <see cref="double.NaN"/>.
/// </remarks>
internal sealed class MovingAverageCalculator
{
    private readonly int period;
    private readonly Mql5MaMethod method;
    private readonly RollingWindow window;
    private double value;
    private bool seeded;

    internal MovingAverageCalculator(int period, Mql5MaMethod method)
    {
        this.period = Math.Max(1, period);
        this.method = method;
        window = new RollingWindow(this.period);
        value = double.NaN;
    }

    internal bool HasValue => seeded;

    internal double Value => seeded ? value : double.NaN;

    internal double Add(double price)
    {
        if (double.IsNaN(price))
        {
            return seeded ? value : double.NaN;
        }

        window.Add(price);
        if (!window.IsFull)
        {
            return double.NaN;
        }

        double simple = window.Sum / period;

        switch (method)
        {
            case Mql5MaMethod.Ema:
                if (!seeded)
                {
                    value = simple;
                }
                else
                {
                    double k = 2.0 / (period + 1.0);
                    value = (price * k) + (value * (1.0 - k));
                }

                break;

            case Mql5MaMethod.Smma:
                value = seeded
                    ? ((value * (period - 1)) + price) / period
                    : simple;
                break;

            case Mql5MaMethod.Lwma:
                value = WeightedAverage();
                break;

            default:
                value = simple;
                break;
        }

        seeded = true;
        return value;
    }

    private double WeightedAverage()
    {
        double weighted = 0.0;
        double weights = 0.0;
        for (int index = 0; index < period; index++)
        {
            double weight = index + 1;
            weighted += window[index] * weight;
            weights += weight;
        }

        return weighted / weights;
    }
}
