namespace YO4X.Mql5.Runtime;

/// <summary>
/// Optional host capability for MQL5 <c>Sleep</c>. Live contexts may block their isolated
/// strategy thread; replay contexts advance a virtual monotonic clock without pausing the host.
/// </summary>
public interface IMql5DelayContext
{
    /// <summary>Deterministic milliseconds consumed in addition to market-clock time.</summary>
    long VirtualDelayMilliseconds => 0;

    /// <summary>Consumes a bounded delay using the host's live or deterministic clock.</summary>
    void Delay(int milliseconds);
}
