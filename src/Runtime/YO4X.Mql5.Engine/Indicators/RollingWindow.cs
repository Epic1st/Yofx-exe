namespace YO4X.Mql5.Engine.Indicators;

/// <summary>A fixed-size ring of the most recent values, oldest first when indexed.</summary>
internal sealed class RollingWindow
{
    private readonly double[] items;
    private int head;

    internal RollingWindow(int capacity)
    {
        items = new double[Math.Max(1, capacity)];
    }

    internal int Capacity => items.Length;

    internal int Count { get; private set; }

    internal bool IsFull => Count == items.Length;

    internal double Sum { get; private set; }

    /// <summary>Gets a value by age, where zero is the oldest retained value.</summary>
    internal double this[int index] => items[(head + index) % items.Length];

    internal void Add(double value)
    {
        if (!IsFull)
        {
            Count++;
        }

        items[head] = value;
        head = (head + 1) % items.Length;

        // Summed afresh rather than carried incrementally. Adding the new value and
        // subtracting the evicted one drifts by ~1e-13 over a long backtest, which is
        // enough to stop a flat series producing an exactly zero deviation - CCI then
        // divides by that residue and reports a large value where it should report zero.
        double sum = 0.0;
        for (int index = 0; index < Count; index++)
        {
            sum += this[index];
        }

        Sum = sum;
    }

    internal double Average() => Count == 0 ? double.NaN : Sum / Count;

    internal double Highest()
    {
        if (Count == 0)
        {
            return double.NaN;
        }

        double best = this[0];
        for (int index = 1; index < Count; index++)
        {
            best = Math.Max(best, this[index]);
        }

        return best;
    }

    internal double Lowest()
    {
        if (Count == 0)
        {
            return double.NaN;
        }

        double best = this[0];
        for (int index = 1; index < Count; index++)
        {
            best = Math.Min(best, this[index]);
        }

        return best;
    }

    /// <summary>Population standard deviation, matching the MetaTrader Bollinger Bands convention.</summary>
    internal double PopulationStandardDeviation()
    {
        if (Count == 0)
        {
            return double.NaN;
        }

        double mean = Sum / Count;
        double accumulator = 0.0;
        for (int index = 0; index < Count; index++)
        {
            double diff = this[index] - mean;
            accumulator += diff * diff;
        }

        return Math.Sqrt(accumulator / Count);
    }

    /// <summary>Mean absolute deviation from the window mean, used by CCI.</summary>
    internal double MeanAbsoluteDeviation()
    {
        if (Count == 0)
        {
            return double.NaN;
        }

        double mean = Sum / Count;
        double accumulator = 0.0;
        for (int index = 0; index < Count; index++)
        {
            accumulator += Math.Abs(this[index] - mean);
        }

        return accumulator / Count;
    }
}
