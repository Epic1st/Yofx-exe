namespace YO4X.Mql5.Engine.Feed;

/// <summary>
/// A replayable source of simulated bars. Implementations must be side-effect free and must
/// produce an identical sequence on every enumeration.
/// </summary>
public interface IMql5MarketFeed
{
    /// <summary>Gets the symbol the feed describes.</summary>
    string Symbol { get; }

    /// <summary>Enumerates the bars in ascending time order.</summary>
    IEnumerable<Mql5Bar> ReadBars();
}
