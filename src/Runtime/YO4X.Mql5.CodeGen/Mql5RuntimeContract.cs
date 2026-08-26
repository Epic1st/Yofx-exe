namespace YO4X.Mql5.CodeGen;

/// <summary>
/// The executable shape of a generated strategy.
///
/// This is a <em>local declaration</em> of the contract that
/// <c>YO4X.Mql5.Runtime</c> owns. It exists so that this project builds and is
/// testable without a reference to the runtime, and so that the generator has a
/// single authoritative statement of the names it emits. The generated C# text
/// references <c>YO4X.Mql5.Runtime</c>, never this copy; if the two ever diverge
/// the runtime wins and this file must be corrected.
/// </summary>
public interface IMql5Strategy
{
    /// <summary>Runs the module's <c>OnInit</c> handler, discarding any MQL5 return code.</summary>
    void OnInit();

    /// <summary>Runs the module's <c>OnTick</c> handler.</summary>
    void OnTick();

    /// <summary>Runs the module's <c>OnDeinit</c> handler.</summary>
    /// <param name="reason">The MQL5 deinitialisation reason code.</param>
    void OnDeinit(int reason);
}

/// <summary>
/// The host object every generated strategy calls into for MQL5 built-ins and
/// predefined variables.
///
/// Declared here as a marker only. The member surface is generated from
/// <see cref="StrategyGovernance.Mql5BuiltinCatalog"/> by the runtime project, and
/// the emission contract that both sides must honour is stated by
/// <see cref="Mql5RuntimeContract"/>.
/// </summary>
public interface IMql5Runtime
{
    /// <summary>The chart symbol, emitted for the MQL5 predefined variable <c>_Symbol</c>.</summary>
    string Symbol { get; }

    /// <summary>The symbol point size, emitted for <c>_Point</c>.</summary>
    double Point { get; }

    /// <summary>The symbol digit count, emitted for <c>_Digits</c>.</summary>
    int Digits { get; }
}

/// <summary>
/// The precise, mechanical rules the generator follows when it emits a call into
/// <see cref="IMql5Runtime"/>. The runtime implementation is generated from the same
/// catalog and must follow the identical rules, which is why they are written down
/// here rather than left implicit in the emitter.
/// </summary>
public static class Mql5RuntimeContract
{
    /// <summary>Namespace the generated source imports for the runtime surface.</summary>
    public const string RuntimeNamespace = "YO4X.Mql5.Runtime";

    /// <summary>Namespace the generated strategies are declared in.</summary>
    public const string GeneratedNamespace = "YO4X.Generated.Strategies";

    /// <summary>Name of the runtime field on the generated strategy class.</summary>
    public const string RuntimeFieldName = "Rt";

    /// <summary>
    /// Name of the flat static class that carries every MQL5 named constant.
    /// The generator emits <c>Mql5Const.ACCOUNT_BALANCE</c> for a built-in constant,
    /// never a bare name, so that no <c>using static</c> ordering can change meaning.
    /// Members must be declared <c>const</c> so that they remain usable as
    /// <c>switch</c> case labels.
    /// </summary>
    public const string ConstantHolderName = "Mql5Const";

    /// <summary>
    /// Every MQL5 <c>&amp;</c> parameter — including the <c>Array*</c> family's array
    /// parameters, which MQL5 does write with <c>&amp;</c> — becomes a C# <c>ref</c>
    /// parameter, and the generator emits <c>ref</c> at the call site. Array
    /// parameters MQL5 writes <em>without</em> <c>&amp;</c> (the <c>Copy*</c> family)
    /// stay by value, because a C# array is already a reference.
    /// </summary>
    public const bool ReferenceParametersUseRef = true;

    /// <summary>
    /// A variadic MQL5 built-in (<c>Print</c>, <c>Comment</c>, <c>Alert</c>,
    /// <c>PrintFormat</c>, <c>StringFormat</c>, <c>StringConcatenate</c>) is emitted
    /// as a plain argument list; the runtime declares it <c>params object?[]</c>.
    /// </summary>
    public const bool VariadicBuiltinsUseParamsArray = true;
}
