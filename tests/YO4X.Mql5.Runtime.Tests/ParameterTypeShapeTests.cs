using System.Globalization;
using System.Reflection;
using YO4X.Mql5.CodeGen;

namespace YO4X.Mql5.Runtime.Tests;

/// <summary>
/// Re-derives the emitter's parameter type table from the runtime interface and holds the recorded
/// one against it.
/// </summary>
/// <remarks>
/// The emitter uses this table to convert each argument of a built-in call to the type the runtime
/// declares. A stale entry does not fail loudly: a numeric position that quietly becomes "no
/// conversion" produces C# that usually still compiles, because MQL5 code passes the right width
/// most of the time. It only breaks on the strategies that relied on the conversion — which is the
/// argument for deriving the expectation here instead of restating it.
/// </remarks>
public sealed class ParameterTypeShapeTests
{
    [Fact]
    public void RecordedShapesMatchTheRuntimeInterface()
    {
        Dictionary<string, string> derived = Derive();
        var differences = new List<string>();

        foreach ((string name, string expected) in derived)
        {
            if (!Mql5ClrTypes.RuntimeParameterTypes.TryGetValue(name, out string? recorded))
            {
                differences.Add($"{name}: missing from the table, runtime says '{expected}'");
            }
            else if (!string.Equals(recorded, expected, StringComparison.Ordinal))
            {
                differences.Add($"{name}: table says '{recorded}', runtime says '{expected}'");
            }
        }

        foreach (string name in Mql5ClrTypes.RuntimeParameterTypes.Keys)
        {
            if (!derived.ContainsKey(name))
            {
                differences.Add($"{name}: in the table, but the runtime declares no convertible parameter");
            }
        }

        Assert.True(differences.Count == 0, string.Join("; ", differences));
    }

    [Theory]
    // A call that leaves an optional parameter out still selects the overload that declares it.
    [InlineData("ArrayResize", 2, 1, "int")]
    [InlineData("ArrayResize", 3, 2, "int")]
    [InlineData("IClose", 3, 0, "string")]
    [InlineData("IClose", 3, 1, "int")]
    // A by-reference position carries no conversion.
    [InlineData("ArrayResize", 2, 0, null)]
    [InlineData("TimeToStruct", 2, 1, null)]
    public void TypeIsTheOneTheRuntimeDeclares(string clrName, int argumentCount, int index, string? expected)
        => Assert.Equal(expected, Mql5ClrTypes.RuntimeParameterType(clrName, argumentCount, index));
    /// <summary>
    /// Re-derives the table from the runtime, on the same rules the emitter's lookup applies.
    /// </summary>
    /// <remarks>
    /// Keyed by the number of arguments a CALL supplies. Keying by an overload's parameter count
    /// was the bug this replaced: <c>ObjectsDeleteAll</c> declares two overloads whose optional
    /// tails overlap at two arguments, and the old keying recorded whichever came first, declaring
    /// the caller's string an <c>int</c> and casting it.
    /// </remarks>
    private static Dictionary<string, string> Derive()
    {
        var byName = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        foreach (IGrouping<string, MethodInfo> group in typeof(IMql5Runtime).GetMethods()
            .Where(method => method.GetParameters().Length > 0)
            .GroupBy(method => method.Name, StringComparer.Ordinal))
        {
            MethodInfo[] overloads = [.. group];
            int widest = overloads.Max(method => method.GetParameters().Length);

            for (int arity = 1; arity <= widest; arity++)
            {
                MethodInfo[] applicable = [.. overloads.Where(method => Accepts(method, arity))];
                if (applicable.Length == 0)
                {
                    continue;
                }

                var consensus = new string[arity];
                bool any = false;

                for (int index = 0; index < arity; index++)
                {
                    string? agreed = Spell(applicable[0].GetParameters()[index]);
                    foreach (MethodInfo method in applicable)
                    {
                        if (!string.Equals(Spell(method.GetParameters()[index]), agreed, StringComparison.Ordinal))
                        {
                            agreed = null;
                            break;
                        }
                    }

                    consensus[index] = agreed ?? ".";
                    any |= agreed is not null;
                }

                if (!any)
                {
                    continue;
                }

                if (!byName.TryGetValue(group.Key, out SortedSet<string>? shapes))
                {
                    shapes = new SortedSet<string>(StringComparer.Ordinal);
                    byName[group.Key] = shapes;
                }

                shapes.Add(arity.ToString(CultureInfo.InvariantCulture) + ":" + string.Join("|", consensus));
            }
        }

        return byName.ToDictionary(
            pair => pair.Key,
            pair => string.Join(";", pair.Value),
            StringComparer.Ordinal);
    }

    /// <summary>Whether a call of <paramref name="arity"/> arguments can select this overload.</summary>
    private static bool Accepts(MethodInfo method, int arity)
    {
        ParameterInfo[] parameters = method.GetParameters();
        int required = parameters.Count(parameter => !parameter.IsOptional);
        return arity >= required && arity <= parameters.Length;
    }

    private static string? Spell(ParameterInfo parameter)
    {
        if (parameter.ParameterType.IsByRef)
        {
            return null;
        }

        Type actual = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;

        if (actual == typeof(bool)) { return "bool"; }
        if (actual == typeof(sbyte)) { return "sbyte"; }
        if (actual == typeof(byte)) { return "byte"; }
        if (actual == typeof(short)) { return "short"; }
        if (actual == typeof(ushort)) { return "ushort"; }
        if (actual == typeof(int)) { return "int"; }
        if (actual == typeof(uint)) { return "uint"; }
        if (actual == typeof(long)) { return "long"; }
        if (actual == typeof(ulong)) { return "ulong"; }
        if (actual == typeof(float)) { return "float"; }
        if (actual == typeof(double)) { return "double"; }
        if (actual == typeof(DateTime)) { return "datetime"; }
        if (actual == typeof(string)) { return "string"; }

        return null;
    }
}
