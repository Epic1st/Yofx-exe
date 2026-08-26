using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Indicators;

/// <summary>Mirrors the MQL5 volume selectors (<c>ENUM_APPLIED_VOLUME</c>).</summary>
public enum Mql5AppliedVolume
{
    /// <summary>Tick volume (<c>VOLUME_TICK</c>).</summary>
    Tick = 0,

    /// <summary>Exchange volume (<c>VOLUME_REAL</c>).</summary>
    Real = 1,
}

/// <summary>
/// <c>iForce</c>, the Force Index. Buffer 0 carries
/// <c>volume * (MA(close) - MA(close)[1])</c>, the bundled MetaTrader formula.
/// </summary>
/// <remarks>
/// The engine feed carries tick volume only, so <see cref="Mql5AppliedVolume.Real"/> resolves to
/// the same tick volume rather than being silently refused. The selector is still recorded on the
/// instance so a caller can tell which one the strategy asked for.
/// </remarks>
public sealed class Mql5ForceIndexIndicator : Mql5IndicatorBase
{
    private readonly MovingAverageCalculator average;
    private double previousAverage;
    private bool hasPrevious;

    /// <summary>Initializes the force index.</summary>
    public Mql5ForceIndexIndicator(int period, Mql5MaMethod method, Mql5AppliedVolume volume)
        : base("iForce", 1)
    {
        Period = Math.Max(1, period);
        Method = method;
        Volume = volume;
        average = new MovingAverageCalculator(Period, method);
    }

    /// <summary>Gets the averaging period.</summary>
    public int Period { get; }

    /// <summary>Gets the averaging method applied to the close series.</summary>
    public Mql5MaMethod Method { get; }

    /// <summary>Gets the volume series the strategy asked for.</summary>
    public Mql5AppliedVolume Volume { get; }

    /// <inheritdoc />
    protected override void Compute(in Mql5Bar bar)
    {
        double current = average.Add(bar.Close);

        if (double.IsNaN(current) || !hasPrevious)
        {
            if (!double.IsNaN(current))
            {
                previousAverage = current;
                hasPrevious = true;
            }

            Push(0, double.NaN);
            return;
        }

        double force = bar.TickVolume * (current - previousAverage);
        previousAverage = current;
        Push(0, force);
    }
}
