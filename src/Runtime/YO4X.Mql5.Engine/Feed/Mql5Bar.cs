namespace YO4X.Mql5.Engine.Feed;

/// <summary>
/// A single OHLC bar of simulated market history.
/// </summary>
/// <param name="Time">Bar open time. Always treated as UTC; never sourced from the wall clock.</param>
/// <param name="Open">Bar open price (bid side).</param>
/// <param name="High">Bar high price (bid side).</param>
/// <param name="Low">Bar low price (bid side).</param>
/// <param name="Close">Bar close price (bid side).</param>
/// <param name="TickVolume">Number of ticks recorded inside the bar.</param>
/// <param name="Spread">Spread in points for the bar. Zero means "use the run default".</param>
public readonly record struct Mql5Bar(
    DateTime Time,
    double Open,
    double High,
    double Low,
    double Close,
    long TickVolume,
    int Spread)
{
    /// <summary>Gets a value indicating whether the bar closed at or above its open.</summary>
    public bool IsBullish => Close >= Open;

    /// <summary>Gets the median price (high + low) / 2.</summary>
    public double Median => (High + Low) / 2.0;

    /// <summary>Gets the typical price (high + low + close) / 3.</summary>
    public double Typical => (High + Low + Close) / 3.0;

    /// <summary>Gets the weighted close (high + low + 2 * close) / 4.</summary>
    public double Weighted => (High + Low + (2.0 * Close)) / 4.0;
}
