using System.Globalization;
using System.Reflection;
using YO4X.Mql5.CodeGen;

namespace YO4X.Mql5.Runtime.Tests;

/// <summary>
/// Re-derives the emitter's by-reference table from the runtime interface and holds the recorded
/// one against it.
/// </summary>
/// <remarks>
/// This is the table the emitter uses to decide whether to write <c>ref</c>, <c>out</c> or nothing
/// at each argument of a built-in call. Getting it wrong does not produce a subtly wrong program —
/// it produces C# that does not compile — but it produces that failure in every strategy using the
/// built-in, far from the runtime edit that caused it. Deriving the expectation here rather than
/// restating it means the test cannot drift with the table it checks.
/// </remarks>
public sealed class ByReferenceShapeTests
{
    [Fact]
    public void RecordedShapesMatchTheRuntimeInterface()
    {
        Dictionary<string, string> derived = Derive();

        var differences = new List<string>();

        foreach ((string name, string expected) in derived)
        {
            if (!Mql5ClrTypes.RuntimeByReferenceParameters.TryGetValue(name, out string? recorded))
            {
                differences.Add($"{name}: missing from the table, runtime says '{expected}'");
            }
            else if (!string.Equals(recorded, expected, StringComparison.Ordinal))
            {
                differences.Add($"{name}: table says '{recorded}', runtime says '{expected}'");
            }
        }

        foreach (string name in Mql5ClrTypes.RuntimeByReferenceParameters.Keys)
        {
            if (!derived.ContainsKey(name))
            {
                differences.Add($"{name}: in the table, but the runtime takes nothing by reference");
            }
        }

        Assert.True(differences.Count == 0, string.Join("; ", differences));
    }

    [Theory]
    [InlineData("OrderSend", 2, 1, "out ")]
    [InlineData("CopyBuffer", 5, 4, "ref ")]
    [InlineData("ArrayResize", 2, 0, "ref ")]
    [InlineData("SymbolInfoDouble", 3, 2, "out ")]
    [InlineData("SymbolInfoDouble", 2, 1, "")]
    [InlineData("IClose", 3, 0, "")]
    public void KeywordIsTheOneCSharpRequires(string clrName, int argumentCount, int index, string expected)
        => Assert.Equal(expected, Mql5ClrTypes.RuntimeParameterKeyword(clrName, argumentCount, index));
    /// <summary>
    /// Re-derives the table from the runtime, on the same rules the emitter's lookup applies.
    /// </summary>
    /// <remarks>
    /// Shapes are keyed by the number of arguments a CALL supplies, not by an overload's parameter
    /// count, because a call can select more than one overload when their optional tails overlap.
    /// A position is recorded only where every overload a call of that arity could select agrees.
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

                var positions = new List<string>();
                for (int index = 0; index < arity; index++)
                {
                    string[] keywords = [.. applicable
                        .Select(method => Keyword(method.GetParameters()[index]))
                        .Where(keyword => keyword is not null)
                        .Distinct(StringComparer.Ordinal)
                        .Select(keyword => keyword!)];

                    if (keywords.Length == 1)
                    {
                        positions.Add(index.ToString(CultureInfo.InvariantCulture) + keywords[0]);
                    }
                }

                if (positions.Count == 0)
                {
                    continue;
                }

                if (!byName.TryGetValue(group.Key, out SortedSet<string>? shapes))
                {
                    shapes = new SortedSet<string>(StringComparer.Ordinal);
                    byName[group.Key] = shapes;
                }

                shapes.Add(arity.ToString(CultureInfo.InvariantCulture) + ":" + string.Join(",", positions));
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

    /// <summary>The keyword a parameter requires, or null when it is passed by value.</summary>
    private static string? Keyword(ParameterInfo parameter) =>
        parameter.ParameterType.IsByRef ? (parameter.IsOut ? "o" : "r") : null;
}
