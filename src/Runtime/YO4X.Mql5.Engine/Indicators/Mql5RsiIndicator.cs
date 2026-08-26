using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Indicators;

/// <summary>
/// <c>iRSI</c> using Wilder smoothing. The first value appears on the bar that completes
/// <c>period</c> price changes: the seed averages are the plain means of those changes, and every
/// later bar applies Wilder's recursive average.
/// </summary>
public sealed class Mql5RsiIndicator : Mql5IndicatorBase
{
    private readonly int period;
    private readonly Mql5AppliedPrice applied;

    private double previousPrice;
    private bool hasPrevious;
    private int seedCount;
    private double seedGain;
    private double seedLoss;
    private double averageGain;
    private double averageLoss;
    private bool seeded;

    /// <summary>Initializes the relative strength index.</summary>
    public Mql5RsiIndicator(int period, Mql5AppliedPrice applied)
        : base("iRSI", 1)
    {
        this.period = Math.Max(1, period);
        this.applied = applied;
    }

    /// <inheritdoc />
    protected override void Compute(in Mql5Bar bar)
    {
        double price = AppliedPrice(bar, applied);

        if (!hasPrevious)
        {
            previousPrice = price;
            hasPrevious = true;
            Push(0, double.NaN);
            return;
        }

        double change = price - previousPrice;
        previousPrice = price;
        double gain = change > 0.0 ? change : 0.0;
        double loss = change < 0.0 ? -change : 0.0;

        if (!seeded)
        {
            seedCount++;
            seedGain += gain;
            seedLoss += loss;

            if (seedCount < period)
            {
                Push(0, double.NaN);
                return;
            }

            averageGain = seedGain / period;
            averageLoss = seedLoss / period;
            seeded = true;
        }
        else
        {
            averageGain = ((averageGain * (period - 1)) + gain) / period;
            averageLoss = ((averageLoss * (period - 1)) + loss) / period;
        }

        if (averageLoss <= 0.0)
        {
            Push(0, averageGain <= 0.0 ? 50.0 : 100.0);
            return;
        }

        double strength = averageGain / averageLoss;
        Push(0, 100.0 - (100.0 / (1.0 + strength)));
    }
}
