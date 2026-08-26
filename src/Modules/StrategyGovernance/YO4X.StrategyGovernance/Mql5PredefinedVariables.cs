namespace YO4X.StrategyGovernance;

/// <summary>
/// The MQL5 predefined variables: the handful of names the runtime declares as
/// variables of every program rather than as functions or compile-time constants.
///
/// They are a separate lookup from <see cref="Mql5BuiltinConstants"/> on purpose.
/// A constant folds to a number at compile time and a binder may substitute it; a
/// predefined variable does not fold at all - it carries a value only while the
/// program runs - so the only thing that can be reported about it statically is its
/// type. Answering "what number is <c>_Symbol</c>" with anything at all would be
/// wrong; answering "what type is <c>_Symbol</c>" with <c>string</c> is exactly
/// right, and is what a binder needs.
///
/// Every entry below was read off the MQL5 compiler itself rather than off the
/// documentation. The type was confirmed twice for each name: once positively, by
/// compiling an assignment to a variable of the claimed type and observing no
/// diagnostic, and once negatively, by compiling an assignment to <c>char</c> and
/// reading the source type out of the resulting
/// <c>warning 43: possible loss of data due to type conversion from '...'</c>.
///
/// That second reading corrects the documentation in two places.
/// <c>_StopFlag</c> and <c>_IsX64</c> are widely described as <c>bool</c>, and they
/// are not: the compiler reports both as <c>int</c>. The control that makes this
/// conclusive is that a genuine <c>bool</c> assigned to <c>char</c> produces no
/// warning at all, so the warning naming <c>int</c> cannot be a bool being
/// described loosely.
///
/// The MQL4 carry-overs are not here, and that is not an omission. The same
/// compiler was asked about all five. <c>Ask</c> and <c>Bid</c> answer
/// <c>error 256: undeclared identifier</c> - MQL5 does not declare them in any
/// form. <c>Digits</c>, <c>Point</c> and <c>Bars</c> answer
/// <c>error 132: open parenthesis expected</c>, which is the compiler saying they
/// exist but are functions, not variables; they belong in the function catalog,
/// which already carries them, and putting them here would claim a variable MQL5
/// does not declare. MQL4 sources that read <c>Ask</c>, <c>Bid</c> or a bare
/// <c>Digits</c> are genuinely not MQL5, and should be reported rather than
/// quietly resolved.
/// </summary>
public static class Mql5PredefinedVariables
{
    // Ordered as the MQL5 reference lists them.
    private static readonly (string Name, string TypeName)[] Declared =
    [
        ("_AppliedTo", "int"),
        ("_Digits", "int"),
        ("_IsX64", "int"),
        ("_LastError", "int"),
        ("_Period", "ENUM_TIMEFRAMES"),
        ("_Point", "double"),
        ("_RandomSeed", "uint"),
        ("_StopFlag", "int"),
        ("_Symbol", "string"),
        ("_UninitReason", "int"),
    ];

    private static readonly Dictionary<string, string> TypesByName =
        Declared.ToDictionary(entry => entry.Name, entry => entry.TypeName, StringComparer.Ordinal);

    /// <summary>Every predefined variable, ordinal-sorted by name.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. Declared.Select(entry => entry.Name)];

    /// <summary>
    /// Reports the MQL5 type of a predefined variable, spelled as MQL5 spells it
    /// (<c>string</c>, <c>double</c>, <c>ENUM_TIMEFRAMES</c>, ...), never as a CLR
    /// type name. Returns false for any other name.
    /// </summary>
    public static bool TryGetType(string name, out string typeName)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (TypesByName.TryGetValue(name, out string? found))
        {
            typeName = found;
            return true;
        }

        typeName = string.Empty;
        return false;
    }

    /// <summary>True when <paramref name="name"/> is an MQL5 predefined variable.</summary>
    public static bool IsKnown(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return TypesByName.ContainsKey(name);
    }
}
