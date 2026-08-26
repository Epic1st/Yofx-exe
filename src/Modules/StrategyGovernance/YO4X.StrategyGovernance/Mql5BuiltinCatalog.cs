using System.Collections.Frozen;

namespace YO4X.StrategyGovernance;

/// <summary>
/// The broad area of the MQL5 standard library a built-in belongs to.
///
/// Category is descriptive only: it says where MetaQuotes documents the function,
/// not whether we can execute it. Executability is <see cref="Mql5BuiltinSupport"/>.
/// </summary>
public enum Mql5BuiltinCategory
{
    Math,
    Text,
    Array,
    Conversion,
    DateTime,
    ChartObject,
    Terminal,
    Account,
    Symbol,
    MarketData,
    Indicator,
    Trade,
    Position,
    Order,
    History,
    File,
    Global,
    Event,
    Other
}

/// <summary>
/// How a built-in can be realised in our engine.
/// </summary>
public enum Mql5BuiltinSupport
{
    /// <summary>Implementable directly in C# with no market context.</summary>
    Native,

    /// <summary>Needs the runtime context (symbol, account, positions, time).</summary>
    EngineBound,

    /// <summary>Maps onto a LEAN indicator.</summary>
    IndicatorBound,

    /// <summary>Chart/visual only; safe no-op for backtesting.</summary>
    ChartStub,

    /// <summary>File I/O, DLL import, terminal control - refuse.</summary>
    Unsupported
}

/// <summary>
/// One formal parameter of an MQL5 built-in.
///
/// <paramref name="TypeName"/> is the MQL5 spelling exactly as documented
/// (<c>double</c>, <c>string</c>, <c>ENUM_TIMEFRAMES</c>, <c>MqlTradeRequest</c>, ...),
/// never a CLR type name - mapping MQL5 types onto engine types is the binder's job,
/// not the catalog's.
///
/// <paramref name="IsReference"/> is true for MQL5 <c>&amp;</c> parameters. Array
/// parameters are always passed by reference in MQL5, so an entry with
/// <paramref name="IsArray"/> also has <paramref name="IsReference"/> set.
/// </summary>
public sealed record Mql5BuiltinParameter(
    string Name,
    string TypeName,
    bool IsOptional,
    bool IsReference,
    bool IsArray);

/// <summary>
/// One documented signature of an MQL5 built-in function.
///
/// A name with several documented forms contributes several entries, all carrying
/// <see cref="IsOverloaded"/> = true. Overload selection is the binder's job.
///
/// <see cref="Verified"/> means the signature was read off the official MQL5
/// reference. An entry marked false records that we know the name exists and how we
/// would classify it, but that the exact parameter list has not been confirmed - a
/// binder must refuse to bind it rather than assume the shape is right. A wrong
/// signature is worse than an absent one because it mis-binds silently.
/// </summary>
public sealed record Mql5BuiltinSignature(
    string Name,
    string ReturnTypeName,
    IReadOnlyList<Mql5BuiltinParameter> Parameters,
    Mql5BuiltinCategory Category,
    Mql5BuiltinSupport Support,
    bool IsOverloaded,
    bool Verified,
    string? Note)
{
    /// <summary>
    /// True when MQL5 documents the function as accepting a trailing variable
    /// argument list (<c>Print</c>, <c>PrintFormat</c>, <c>StringFormat</c>,
    /// <c>Comment</c>, <c>Alert</c>, <c>FileWrite</c>, <c>StringConcatenate</c>).
    /// </summary>
    public bool IsVariadic { get; init; }

    /// <summary>Number of leading parameters that must be supplied.</summary>
    public int RequiredParameterCount
    {
        get
        {
            int required = 0;
            for (int index = 0; index < Parameters.Count; index++)
            {
                if (Parameters[index].IsOptional)
                {
                    break;
                }

                required++;
            }

            return required;
        }
    }

    /// <summary>
    /// True when <paramref name="argumentCount"/> is a possible arity for this
    /// signature. This is arity only - it proves nothing about argument types.
    /// </summary>
    public bool AcceptsArgumentCount(int argumentCount)
    {
        if (argumentCount < RequiredParameterCount)
        {
            return false;
        }

        return IsVariadic || argumentCount <= Parameters.Count;
    }
}

/// <summary>
/// One named MQL5 compile-time constant: an enumeration member or a predefined limit.
///
/// <paramref name="Value"/> is null when MetaQuotes documents the constant by name
/// but never publishes its number - which is the case for almost every MQL5
/// enumeration, because the reference tables carry only an ID and a description
/// column. A null value means "we do not know", never zero, and the ordinals are
/// not safely guessable: ENUM_TRADE_REQUEST_ACTIONS is not contiguous from zero and
/// ORDER_FILLING_BOC was inserted ahead of ORDER_FILLING_RETURN in a later build.
///
/// <paramref name="EnumName"/> is the declaring MQL5 enumeration where the constant
/// has one (<c>ENUM_TIMEFRAMES</c>, <c>ENUM_ORDER_TYPE</c>, ...) and null for free
/// standing constants such as <c>INVALID_HANDLE</c> and <c>clrNONE</c>.
/// </summary>
public sealed record Mql5BuiltinConstant(string Name, long? Value, string? EnumName);

/// <summary>
/// The typed description of the MQL5 standard library surface that the binder and
/// the code generator both consume.
///
/// The catalog is a fact table, not a policy: it reports what MQL5 declares and how
/// we have classified each entry's realisability. It never decides whether a given
/// strategy may use a given built-in - that is the binder's call, informed by
/// <see cref="Mql5BuiltinSignature.Support"/> and
/// <see cref="Mql5BuiltinSignature.Verified"/>.
///
/// All tables are static readonly, built once, ordinal-keyed and reflection-free.
/// Lookup is O(1).
/// </summary>
public static class Mql5BuiltinCatalog
{
    private static readonly Mql5BuiltinSignature[] AllSignatures = BuildSignatures();

    private static readonly FrozenDictionary<string, IReadOnlyList<Mql5BuiltinSignature>> SignaturesByName =
        AllSignatures
            .GroupBy(signature => signature.Name, StringComparer.Ordinal)
            .ToFrozenDictionary(
                group => group.Key,
                group => (IReadOnlyList<Mql5BuiltinSignature>)[.. group],
                StringComparer.Ordinal);

    private static readonly FrozenDictionary<Mql5BuiltinCategory, IReadOnlyList<Mql5BuiltinSignature>> SignaturesByCategory =
        AllSignatures
            .GroupBy(signature => signature.Category)
            .ToFrozenDictionary(
                group => group.Key,
                group => (IReadOnlyList<Mql5BuiltinSignature>)[.. group]);

    private static readonly FrozenDictionary<Mql5BuiltinSupport, IReadOnlyList<Mql5BuiltinSignature>> SignaturesBySupport =
        AllSignatures
            .GroupBy(signature => signature.Support)
            .ToFrozenDictionary(
                group => group.Key,
                group => (IReadOnlyList<Mql5BuiltinSignature>)[.. group]);

    /// <summary>
    /// Every catalogued signature, ordered by name then by declared arity so the
    /// table is stable across builds and diffable.
    /// </summary>
    public static IReadOnlyList<Mql5BuiltinSignature> All => AllSignatures;

    /// <summary>Every catalogued built-in name, ordinal-sorted.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. AllSignatures.Select(s => s.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>
    /// Looks up every documented overload of <paramref name="name"/>. Names are
    /// matched ordinally: MQL5 identifiers are case sensitive.
    /// </summary>
    public static bool TryGet(string name, out IReadOnlyList<Mql5BuiltinSignature> overloads)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (SignaturesByName.TryGetValue(name, out IReadOnlyList<Mql5BuiltinSignature>? found))
        {
            overloads = found;
            return true;
        }

        overloads = [];
        return false;
    }

    /// <summary>
    /// True when the catalog knows <paramref name="name"/> as any built-in of the
    /// MQL5 runtime: a function of the standard library, a named constant, or one of
    /// the predefined variables.
    ///
    /// This is deliberately broader than <see cref="TryGet"/>, which answers only
    /// about functions and their signatures. The question a caller asks here is
    /// "does MQL5 declare this name at all", and the honest answer for
    /// <c>_Symbol</c> or <c>EMPTY_VALUE</c> is yes even though neither has a
    /// signature and neither folds to an integer. A caller that needs the
    /// distinction asks <see cref="Mql5PredefinedVariables.IsKnown"/> or
    /// <see cref="Mql5BuiltinConstants.TryGetValue"/>, both of which are precise
    /// about what kind of thing the name is.
    /// </summary>
    public static bool IsKnown(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return SignaturesByName.ContainsKey(name)
            || Mql5BuiltinConstants.IsKnown(name)
            || Mql5PredefinedVariables.IsKnown(name);
    }

    /// <summary>Every signature MetaQuotes documents under <paramref name="category"/>.</summary>
    public static IReadOnlyList<Mql5BuiltinSignature> ByCategory(Mql5BuiltinCategory category)
        => SignaturesByCategory.TryGetValue(category, out IReadOnlyList<Mql5BuiltinSignature>? found) ? found : [];

    /// <summary>Every signature we have classified as <paramref name="support"/>.</summary>
    public static IReadOnlyList<Mql5BuiltinSignature> BySupport(Mql5BuiltinSupport support)
        => SignaturesBySupport.TryGetValue(support, out IReadOnlyList<Mql5BuiltinSignature>? found) ? found : [];

    private static Mql5BuiltinSignature[] BuildSignatures()
    {
        Mql5BuiltinSignature[] declared = Mql5BuiltinSignatures.Declare();

        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        foreach (Mql5BuiltinSignature signature in declared)
        {
            counts[signature.Name] = counts.GetValueOrDefault(signature.Name) + 1;
        }

        return
        [
            .. declared
                .Select(signature => counts[signature.Name] > 1
                    ? signature with { IsOverloaded = true }
                    : signature)
                .OrderBy(signature => signature.Name, StringComparer.Ordinal)
                .ThenBy(signature => signature.Parameters.Count)
        ];
    }
}

