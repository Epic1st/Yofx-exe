using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Indicators;

/// <summary>Shared buffer bookkeeping for the built-in indicators.</summary>
public abstract class Mql5IndicatorBase : IMql5Indicator
{
    /// <summary>The value reported for bars where the indicator has not formed yet.</summary>
    public const double EmptyValue = 0.0;

    private readonly List<double>[] buffers;

    /// <summary>Initializes the indicator with the given number of output buffers.</summary>
    protected Mql5IndicatorBase(string name, int bufferCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(bufferCount, 1);
        Name = name;
        buffers = new List<double>[bufferCount];
        for (int index = 0; index < bufferCount; index++)
        {
            buffers[index] = [];
        }
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public int BufferCount => buffers.Length;

    /// <inheritdoc />
    public int Count => buffers[0].Count;

    /// <inheritdoc />
    public void Append(in Mql5Bar bar)
    {
        Compute(bar);

        // Every buffer must advance in lockstep or indexing silently desynchronises.
        int expected = buffers[0].Count;
        for (int index = 1; index < buffers.Length; index++)
        {
            while (buffers[index].Count < expected)
            {
                buffers[index].Add(EmptyValue);
            }
        }
    }

    /// <inheritdoc />
    public double Value(int buffer, int backIndex)
    {
        if (buffer < 0 || buffer >= buffers.Length || backIndex < 0)
        {
            return EmptyValue;
        }

        List<double> target = buffers[buffer];
        int index = target.Count - 1 - backIndex;
        return index < 0 || index >= target.Count ? EmptyValue : target[index];
    }

    /// <summary>Computes and pushes one value into each buffer for the supplied bar.</summary>
    protected abstract void Compute(in Mql5Bar bar);

    /// <summary>Appends a value to a buffer, mapping <see cref="double.NaN"/> to the empty value.</summary>
    protected void Push(int buffer, double value) =>
        buffers[buffer].Add(double.IsNaN(value) ? EmptyValue : value);

    /// <summary>
    /// Overwrites an already published buffer slot, addressed by ascending index exactly as
    /// <see cref="Raw"/> is. MetaTrader recalculates history, so an indicator such as
    /// <c>iFractals</c> that can only confirm a bar once later bars exist writes back into the
    /// bar it belongs to rather than publishing it late. Out-of-range indices are ignored.
    /// </summary>
    protected void Revise(int buffer, int index, double value)
    {
        if (buffer < 0 || buffer >= buffers.Length)
        {
            return;
        }

        List<double> target = buffers[buffer];
        if (index < 0 || index >= target.Count)
        {
            return;
        }

        target[index] = double.IsNaN(value) ? EmptyValue : value;
    }

    /// <summary>Reads a raw buffer slot by ascending index, where zero is the oldest bar.</summary>
    protected double Raw(int buffer, int index) =>
        index < 0 || index >= buffers[buffer].Count ? EmptyValue : buffers[buffer][index];

    /// <summary>Resolves the MQL5 applied price of a bar.</summary>
    protected static double AppliedPrice(in Mql5Bar bar, Mql5AppliedPrice applied) => applied switch
    {
        Mql5AppliedPrice.Open => bar.Open,
        Mql5AppliedPrice.High => bar.High,
        Mql5AppliedPrice.Low => bar.Low,
        Mql5AppliedPrice.Median => bar.Median,
        Mql5AppliedPrice.Typical => bar.Typical,
        Mql5AppliedPrice.Weighted => bar.Weighted,
        _ => bar.Close,
    };
}
