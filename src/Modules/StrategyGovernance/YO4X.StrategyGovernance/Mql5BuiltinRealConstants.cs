using System.Collections.Frozen;
using System.Globalization;

namespace YO4X.StrategyGovernance;

/// <summary>
/// The MQL5 named constants whose value is a <c>double</c>.
///
/// <para>
/// These are deliberately not in <see cref="Mql5BuiltinConstants"/>. That catalogue measures a
/// constant by folding it into an integer and reading the truncation warning, which cannot tell a
/// double from an integer: it would record <c>M_PI</c> as 3. Rather than weaken that invariant,
/// the floating point constants are measured a different way and kept apart.
/// </para>
///
/// <para>
/// How these were measured. The compiler is asked to fold an equality between the constant and a
/// hypothesised literal, and the resulting boolean is what overflows the integer and appears in
/// the warning. So the compiler answers yes or no to a value proposed to it, and only a yes is
/// recorded. Nothing here is a value read off documentation or inferred from a C library: every
/// entry is a hypothesis MetaEditor confirmed as exactly equal.
/// </para>
///
/// <para>
/// What is absent and why. <c>FLT_MAX</c>, <c>FLT_MIN</c> and <c>FLT_EPSILON</c> were probed and
/// the compiler answered no. They are <c>float</c>-typed in MQL5, so comparing one against a
/// decimal double literal promotes it and the two differ in the low bits. The honest record of a
/// hypothesis the compiler rejected is absence, not the rejected number, so they are not carried
/// and a module that references one is refused with a diagnostic.
/// </para>
///
/// <para>
/// <c>EMPTY_VALUE</c> is the entry that matters most in practice. It marks "no value" in an
/// indicator buffer, so a strategy comparing against it is deciding whether an indicator produced
/// a reading at all. It was confirmed equal to <c>DBL_MAX</c> by the same probe rather than
/// assumed, because a near-miss here would not fail: it would quietly turn every empty bar into a
/// real one.
/// </para>
/// </summary>
public static class Mql5BuiltinRealConstants
{
    private static readonly FrozenDictionary<string, double> ValuesByName =
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            // Floating point limits. Confirmed exactly equal by the compiler.
            ["DBL_MAX"] = 1.7976931348623157e+308,
            ["DBL_MIN"] = 2.2250738585072014e-308,
            ["DBL_EPSILON"] = 2.2204460492503131e-016,

            // "No value" in an indicator buffer. Confirmed equal to DBL_MAX.
            ["EMPTY_VALUE"] = 1.7976931348623157e+308,

            // Mathematical constants. Each confirmed exactly equal by the compiler.
            ["M_PI"] = 3.14159265358979323846,
            ["M_PI_2"] = 1.57079632679489661923,
            ["M_PI_4"] = 0.785398163397448309616,
            ["M_1_PI"] = 0.318309886183790671538,
            ["M_2_PI"] = 0.636619772367581343076,
            ["M_E"] = 2.71828182845904523536,
            ["M_LN2"] = 0.693147180559945309417,
            ["M_LN10"] = 2.30258509299404568402,
            ["M_SQRT2"] = 1.41421356237309504880,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Every catalogued name, ordinal-sorted.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. ValuesByName.Keys.Order(StringComparer.Ordinal)];

    /// <summary>Resolves a named constant to its <c>double</c> value.</summary>
    public static bool TryGetValue(string name, out double value)
    {
        ArgumentNullException.ThrowIfNull(name);
        return ValuesByName.TryGetValue(name, out value);
    }

    /// <summary>True when the catalogue carries a value for <paramref name="name"/>.</summary>
    public static bool IsKnown(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return ValuesByName.ContainsKey(name);
    }

    /// <summary>
    /// Renders a value as a C# <c>double</c> literal that round-trips to the same bits.
    /// </summary>
    /// <remarks>
    /// The round-trip format is not cosmetic. A shortened rendering of <c>DBL_MAX</c> parses to
    /// infinity, and one of <c>M_PI</c> lands a few ulps away; either would leave generated code
    /// comparing against a number the strategy never named.
    /// </remarks>
    public static string ToLiteral(double value) =>
        value.ToString("R", CultureInfo.InvariantCulture) switch
        {
            var text when text.Contains('E', StringComparison.Ordinal)
                || text.Contains('.', StringComparison.Ordinal) => text + "D",
            var text => text + ".0D",
        };
}
