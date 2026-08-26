using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Context;

/// <summary>
/// The surface a translated MQL5 expert advisor calls into. Deliberately mirrors the MQL5 standard
/// library free functions one for one so generated code reads like the source EA.
/// </summary>
/// <remarks>
/// Every implementation in this assembly is backed by <see cref="Mql5SimulatedBroker"/> and a
/// replayed bar series. Nothing here can reach a live broker.
/// </remarks>
public interface IMql5MarketContext
{
    /// <summary>Gets the symbol the run trades.</summary>
    string Symbol { get; }

    /// <summary>Gets the size of one point for <see cref="Symbol"/>.</summary>
    double Point { get; }

    /// <summary>Gets the number of quote decimals for <see cref="Symbol"/>.</summary>
    int Digits { get; }

    /// <summary>Gets the simulated clock, which is the current bar's time.</summary>
    DateTime TimeCurrent { get; }

    /// <summary>
    /// Reports whether the named symbol is tradable. See <c>SymbolSelect</c>.
    ///
    /// The engine simulates one instrument per run, so the honest answer is whether the caller is
    /// asking about that instrument. Strategies routinely gate their whole tick on this call, so
    /// answering a blanket false would silently stop them dead rather than fail visibly.
    /// </summary>
    bool SymbolSelect(string symbol, bool enable);

    /// <summary>Reads a floating point symbol property. See <see cref="Mql5SymbolInfoDouble"/>.</summary>
    double SymbolInfoDouble(string symbol, int propertyId);

    /// <summary>Reads an integer symbol property. See <see cref="Mql5SymbolInfoInteger"/>.</summary>
    long SymbolInfoInteger(string symbol, int propertyId);

    /// <summary>Reads a floating point account property. See <see cref="Mql5AccountInfoDouble"/>.</summary>
    double AccountInfoDouble(int propertyId);

    /// <summary>Gets the number of open positions.</summary>
    int PositionsTotal();

    /// <summary>
    /// Selects the first open position on the given symbol so the <c>PositionGet</c> family can
    /// read it. Returns <see langword="false"/> when nothing is open.
    /// </summary>
    bool PositionSelect(string symbol);

    /// <summary>Reads a floating point property of the selected position.</summary>
    double PositionGetDouble(int propertyId);

    /// <summary>Reads an integer property of the selected position.</summary>
    long PositionGetInteger(int propertyId);

    /// <summary>
    /// Submits a trade request. Returns <see langword="false"/> and a populated retcode rather
    /// than throwing when the request is invalid.
    /// </summary>
    bool OrderSend(Mql5TradeRequest request, out Mql5TradeResult result);

    /// <summary>
    /// Allocates, or reuses, an indicator handle. Returns -1 for an unknown indicator, matching
    /// <c>INVALID_HANDLE</c>.
    /// </summary>
    int IndicatorHandle(string name, params object[] parameters);

    /// <summary>
    /// Copies indicator values into <paramref name="target"/>, oldest first, so that
    /// <c>target[count - 1]</c> is the bar <paramref name="start"/> bars back from the current
    /// one. Returns the number copied, or -1 when the request cannot be satisfied.
    /// </summary>
    int CopyBuffer(int handle, int bufferNum, int start, int count, double[] target);
}
