namespace YO4X.Mql5.Runtime;

/// <summary>
/// MQL5 market-information and account functions. Every one is <b>EngineBound</b>:
/// the answers come from <see cref="IMql5MarketContext"/>, never from this library.
///
/// MQL5 gives the <c>SymbolInfo</c> trio two shapes each - a direct-return form and a
/// <c>bool</c> form whose last parameter is an out reference - and strategies use both
/// interchangeably. Both are declared here; the <c>bool</c> forms report false when the
/// property is unavailable, which is the check a careful strategy is relying on.
///
/// Property ids arrive as plain integers. MetaQuotes publishes no numeric values for
/// <c>ENUM_SYMBOL_INFO_DOUBLE</c> and its neighbours, so the runtime passes them
/// through untouched rather than guessing an ordinal that would silently read the
/// wrong field.
/// </summary>
public partial interface IMql5Runtime
{
    /// <summary>MQL5 <c>Symbol()</c>: the symbol the strategy is attached to. EngineBound.</summary>
    string Symbol();

    /// <summary>MQL5 <c>Digits()</c>: the price precision of the current symbol. EngineBound.</summary>
    int Digits();

    /// <summary>MQL5 <c>Point()</c>: the point size of the current symbol. EngineBound.</summary>
    double Point();

    /// <summary>MQL5 <c>Period()</c>: the current timeframe as an <c>ENUM_TIMEFRAMES</c> member. EngineBound.</summary>
    int Period();

    /// <summary>MQL5 <c>SymbolsTotal</c>. EngineBound.</summary>
    int SymbolsTotal(bool selected);

    /// <summary>MQL5 <c>SymbolName</c>. EngineBound.</summary>
    string SymbolName(int position, bool selected);

    /// <summary>MQL5 <c>SymbolSelect</c>. EngineBound.</summary>
    bool SymbolSelect(string? name, bool selectFlag);

    /// <summary>MQL5 <c>SymbolIsSynchronized</c>. EngineBound.</summary>
    bool SymbolIsSynchronized(string? name);

    /// <summary>MQL5 <c>SymbolInfoDouble</c>, direct-return form. EngineBound.</summary>
    double SymbolInfoDouble(string? name, int propertyId);

    /// <summary>MQL5 <c>SymbolInfoDouble</c>, out-parameter form. EngineBound.</summary>
    bool SymbolInfoDouble(string? name, int propertyId, out double value);

    /// <summary>MQL5 <c>SymbolInfoInteger</c>, direct-return form. EngineBound.</summary>
    long SymbolInfoInteger(string? name, int propertyId);

    /// <summary>MQL5 <c>SymbolInfoInteger</c>, out-parameter form. EngineBound.</summary>
    bool SymbolInfoInteger(string? name, int propertyId, out long value);

    /// <summary>MQL5 <c>SymbolInfoString</c>, direct-return form. EngineBound.</summary>
    string SymbolInfoString(string? name, int propertyId);

    /// <summary>MQL5 <c>SymbolInfoString</c>, out-parameter form. EngineBound.</summary>
    bool SymbolInfoString(string? name, int propertyId, out string value);

    /// <summary>MQL5 <c>SymbolInfoTick</c>. EngineBound.</summary>
    bool SymbolInfoTick(string? symbol, out Mql5Tick tick);

    /// <summary>MQL5 <c>SymbolInfoMarginRate</c>. EngineBound.</summary>
    bool SymbolInfoMarginRate(string? name, int orderType, out double initialMarginRate, out double maintenanceMarginRate);

    /// <summary>MQL5 <c>SymbolInfoSessionQuote</c>. EngineBound.</summary>
    bool SymbolInfoSessionQuote(string? name, int dayOfWeek, uint sessionIndex, out long from, out long until);

    /// <summary>MQL5 <c>SymbolInfoSessionTrade</c>. EngineBound.</summary>
    bool SymbolInfoSessionTrade(string? name, int dayOfWeek, uint sessionIndex, out long from, out long until);

    /// <summary>MQL5 <c>MarketBookAdd</c>. EngineBound.</summary>
    bool MarketBookAdd(string? symbol);

    /// <summary>MQL5 <c>MarketBookRelease</c>. EngineBound.</summary>
    bool MarketBookRelease(string? symbol);

    /// <summary>
    /// MQL5 <c>MarketBookGet</c>. Always false, with <c>ERR_MARKET_NOT_SELECTED</c>
    /// recorded and <paramref name="book"/> left alone: this engine models no order
    /// book, and false is MQL5's own answer for "no book is available here".
    /// EngineBound.
    /// </summary>
    bool MarketBookGet(string? symbol, ref Mql5BookInfo[]? book);

    /// <summary>MQL5 <c>AccountInfoDouble</c>. EngineBound.</summary>
    double AccountInfoDouble(int propertyId);

    /// <summary>MQL5 <c>AccountInfoInteger</c>. EngineBound.</summary>
    long AccountInfoInteger(int propertyId);

    /// <summary>MQL5 <c>AccountInfoString</c>. EngineBound.</summary>
    string AccountInfoString(int propertyId);
}

public sealed partial class Mql5Runtime
{
    /// <inheritdoc />
    public string Symbol() => context.Symbol;

    /// <inheritdoc />
    public int Digits() => context.Digits;

    /// <inheritdoc />
    public double Point() => context.Point;

    /// <inheritdoc />
    public int Period() => context.Period;

    /// <inheritdoc />
    public int SymbolsTotal(bool selected) => context.SymbolsTotal(selected);

    /// <inheritdoc />
    public string SymbolName(int position, bool selected) => context.SymbolName(position, selected);

    /// <inheritdoc />
    public bool SymbolSelect(string? name, bool selectFlag) => context.SymbolSelect(Resolve(name), selectFlag);

    /// <inheritdoc />
    public bool SymbolIsSynchronized(string? name) => context.SymbolIsSynchronized(Resolve(name));

    /// <inheritdoc />
    public double SymbolInfoDouble(string? name, int propertyId) => context.SymbolInfoDouble(Resolve(name), propertyId);

    /// <inheritdoc />
    public bool SymbolInfoDouble(string? name, int propertyId, out double value)
    {
        value = context.SymbolInfoDouble(Resolve(name), propertyId);
        return true;
    }

    /// <inheritdoc />
    public long SymbolInfoInteger(string? name, int propertyId) => context.SymbolInfoInteger(Resolve(name), propertyId);

    /// <inheritdoc />
    public bool SymbolInfoInteger(string? name, int propertyId, out long value)
    {
        value = context.SymbolInfoInteger(Resolve(name), propertyId);
        return true;
    }

    /// <inheritdoc />
    public string SymbolInfoString(string? name, int propertyId) => context.SymbolInfoString(Resolve(name), propertyId);

    /// <inheritdoc />
    public bool SymbolInfoString(string? name, int propertyId, out string value)
    {
        value = context.SymbolInfoString(Resolve(name), propertyId);
        return true;
    }

    /// <inheritdoc />
    public bool SymbolInfoTick(string? symbol, out Mql5Tick tick)
    {
        bool ok = context.SymbolInfoTick(Resolve(symbol), out tick);
        if (!ok)
        {
            SetError(Mql5ErrorCodes.MarketNotSelected);
        }

        return ok;
    }

    /// <inheritdoc />
    public bool SymbolInfoMarginRate(string? name, int orderType, out double initialMarginRate, out double maintenanceMarginRate)
        => context.SymbolInfoMarginRate(Resolve(name), orderType, out initialMarginRate, out maintenanceMarginRate);

    /// <inheritdoc />
    public bool SymbolInfoSessionQuote(string? name, int dayOfWeek, uint sessionIndex, out long from, out long until)
        => context.SymbolInfoSessionQuote(Resolve(name), dayOfWeek, sessionIndex, out from, out until);

    /// <inheritdoc />
    public bool SymbolInfoSessionTrade(string? name, int dayOfWeek, uint sessionIndex, out long from, out long until)
        => context.SymbolInfoSessionTrade(Resolve(name), dayOfWeek, sessionIndex, out from, out until);

    /// <inheritdoc />
    public bool MarketBookAdd(string? symbol) => context.MarketBookAdd(Resolve(symbol));

    /// <inheritdoc />
    public bool MarketBookRelease(string? symbol) => context.MarketBookRelease(Resolve(symbol));

    /// <inheritdoc />
    public bool MarketBookGet(string? symbol, ref Mql5BookInfo[]? book)
    {
        // Not a refusal, and not an invention either. MQL5 requires MarketBookAdd to
        // succeed before a book can be read; the engine has no depth-of-market feed, so
        // MarketBookAdd already answers false and the symbol's book is genuinely not
        // selected. Reporting that with false and ERR_MARKET_NOT_SELECTED is the answer
        // MQL5 itself gives on a symbol with no book, and every caller has to handle it:
        // the corpus use is `if(MarketBookGet(_Symbol, book)) { ... }` guarding an
        // optional block, which is the only shape this call has.
        //
        // The alternative - synthesising a book out of bid and ask - was rejected. An
        // order book is depth, and depth is exactly the information this engine does not
        // have; two fabricated levels would let a strategy size itself against liquidity
        // that was never measured. Throwing was rejected too: it punishes a strategy for
        // asking a question MQL5 lets it ask and answers negatively every day on symbols
        // with no market depth.
        //
        // `book` is deliberately left untouched. MQL5 fills the array only on success,
        // and a caller that reads it after a false return is reading its own prior
        // contents in MetaTrader as well.
        SetError(Mql5ErrorCodes.MarketNotSelected);
        return false;
    }

    /// <inheritdoc />
    public double AccountInfoDouble(int propertyId) => context.AccountInfoDouble(propertyId);

    /// <inheritdoc />
    public long AccountInfoInteger(int propertyId) => context.AccountInfoInteger(propertyId);

    /// <inheritdoc />
    public string AccountInfoString(int propertyId) => context.AccountInfoString(propertyId);

    // MQL5 treats NULL and the empty string as "the current symbol" throughout the
    // market-information surface, and the corpus relies on it heavily.
    private string Resolve(string? symbol) => string.IsNullOrEmpty(symbol) ? context.Symbol : symbol;
}
