using YO4X.Mql5.Engine.Context;

namespace YO4X.Mql5.Engine.Hosting;

/// <summary>
/// The managed shape of a compiled MQL5 expert advisor: the three entry points the terminal calls.
/// A translated EA implements this and the host drives it.
/// </summary>
public interface IMql5Strategy
{
    /// <summary>
    /// Called once before the first tick. Return <see cref="Trading.Mql5InitCode.Succeeded"/> to
    /// let the run proceed; anything else aborts it before any tick is delivered.
    /// </summary>
    int OnInit(IMql5MarketContext context);

    /// <summary>Called once per bar, after the bar has been appended to the series.</summary>
    void OnTick(IMql5MarketContext context);

    /// <summary>Called once after the last tick, or after an aborted initialization.</summary>
    void OnDeinit(IMql5MarketContext context, int reason);
}
