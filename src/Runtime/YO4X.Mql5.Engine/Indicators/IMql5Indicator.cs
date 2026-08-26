using YO4X.Mql5.Engine.Feed;

namespace YO4X.Mql5.Engine.Indicators;

/// <summary>
/// An incremental indicator over the bar series. Exactly one value per buffer is produced for
/// every bar appended, so buffer index and bar index stay aligned for the whole run.
/// </summary>
public interface IMql5Indicator
{
    /// <summary>Gets the MQL5 function name the indicator implements, for example <c>iMA</c>.</summary>
    string Name { get; }

    /// <summary>Gets the number of output buffers.</summary>
    int BufferCount { get; }

    /// <summary>Gets the number of bars processed.</summary>
    int Count { get; }

    /// <summary>Feeds one more bar.</summary>
    void Append(in Mql5Bar bar);

    /// <summary>
    /// Reads a value counted back from the most recent bar: <paramref name="backIndex"/> zero is
    /// the current bar, one the previous, matching MQL5 timeseries indexing.
    /// </summary>
    double Value(int buffer, int backIndex);
}
