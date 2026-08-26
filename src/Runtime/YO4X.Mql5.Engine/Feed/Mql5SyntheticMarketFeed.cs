namespace YO4X.Mql5.Engine.Feed;

/// <summary>
/// Generates a deterministic synthetic bar series from a seed. There is no broker data on the
/// build machine, so this is the default feed for regression runs: the same seed and the same
/// options always yield the identical series.
/// </summary>
public sealed class Mql5SyntheticMarketFeed : IMql5MarketFeed
{
    private const int SubStepsPerBar = 4;

    /// <summary>Initializes a synthetic feed.</summary>
    public Mql5SyntheticMarketFeed(string symbol, ulong seed, int barCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentOutOfRangeException.ThrowIfNegative(barCount);
        Symbol = symbol;
        Seed = seed;
        BarCount = barCount;
    }

    /// <inheritdoc />
    public string Symbol { get; }

    /// <summary>Gets the seed that fixes the generated series.</summary>
    public ulong Seed { get; }

    /// <summary>Gets the number of bars produced.</summary>
    public int BarCount { get; }

    /// <summary>Gets the price of the first bar's open.</summary>
    public double StartPrice { get; init; } = 1.10000;

    /// <summary>Gets the size of one price point.</summary>
    public double Point { get; init; } = 0.00001;

    /// <summary>Gets the per sub-step volatility expressed in points.</summary>
    public double VolatilityPoints { get; init; } = 40.0;

    /// <summary>Gets the per sub-step drift expressed in points.</summary>
    public double DriftPoints { get; init; }

    /// <summary>Gets the time of the first bar.</summary>
    public DateTime StartTime { get; init; } = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Gets the bar period in minutes.</summary>
    public int PeriodMinutes { get; init; } = 60;

    /// <summary>Gets the spread stamped on every generated bar, in points.</summary>
    public int SpreadPoints { get; init; } = 10;

    /// <summary>Gets the floor the random walk is reflected off, keeping prices positive.</summary>
    public double MinimumPrice { get; init; } = 0.00100;

    /// <inheritdoc />
    public IEnumerable<Mql5Bar> ReadBars()
    {
        var random = new Mql5DeterministicRandom(Seed);
        double price = StartPrice;
        DateTime time = StartTime;

        for (int index = 0; index < BarCount; index++)
        {
            double open = price;
            double high = open;
            double low = open;
            double walking = open;

            for (int step = 0; step < SubStepsPerBar; step++)
            {
                walking += ((random.NextSigned() * VolatilityPoints) + DriftPoints) * Point;
                if (walking < MinimumPrice)
                {
                    walking = MinimumPrice + (MinimumPrice - walking);
                }

                high = Math.Max(high, walking);
                low = Math.Min(low, walking);
            }

            double close = walking;
            long tickVolume = random.NextInt32(20, 400);
            price = close;

            yield return new Mql5Bar(
                time,
                Round(open),
                Round(high),
                Round(low),
                Round(close),
                tickVolume,
                SpreadPoints);

            time = time.AddMinutes(PeriodMinutes);
        }
    }

    private double Round(double value)
    {
        if (Point <= 0.0)
        {
            return value;
        }

        return Math.Round(value / Point, MidpointRounding.AwayFromZero) * Point;
    }
}
