namespace YO4X.Mql5.Runtime;

/// <summary>
/// Host-supplied policy for one <see cref="Mql5Runtime"/> instance.
///
/// Everything here exists so that a backtest is reproducible. The pseudo-random
/// sequence comes from an injected seed rather than the clock, and output goes to an
/// injected sink rather than to a console the host does not control.
/// </summary>
public sealed class Mql5RuntimeOptions
{
    /// <summary>The defaults: seed 1, no logging, chart calls recorded but not logged.</summary>
    public static Mql5RuntimeOptions Default { get; } = new();

    /// <summary>
    /// The initial <c>MathRand</c> seed.
    ///
    /// MQL5 inherits the Microsoft C runtime generator, whose unseeded state is 1, so
    /// that is the default here too. A strategy that calls <c>MathSrand</c> overwrites
    /// it; a strategy that does not gets the same sequence on every run, which is the
    /// point.
    /// </summary>
    public uint RandomSeed { get; init; } = 1;

    /// <summary>Where <c>Print</c>, <c>Comment</c> and <c>Alert</c> go. Defaults to discarding.</summary>
    public IMql5LogSink LogSink { get; init; } = NullMql5LogSink.Instance;

    /// <summary>
    /// When set, every chart-drawing call is additionally written to
    /// <see cref="LogSink"/> on <see cref="Mql5LogChannel.Chart"/>. Off by default:
    /// <c>ObjectSetInteger</c> alone accounts for more callsites than any other
    /// built-in in the corpus, and logging all of them buries the strategy's own
    /// output.
    /// </summary>
    public bool LogChartCalls { get; init; }

    /// <summary>
    /// The chart id <c>ChartID()</c> reports and that <c>0</c> resolves to in the
    /// <c>Object*</c> family. MQL5 uses 0 for "the current chart".
    /// </summary>
    public long ChartId { get; init; }
}
