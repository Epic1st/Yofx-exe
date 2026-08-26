using System.Reflection;
using YO4X.Mql5.CodeGen;
using YO4X.StrategyGovernance;

namespace YO4X.Mql5.Runtime.Tests;

/// <summary>
/// Holds the emitter's built-in name mapping against the runtime interface it emits calls into.
/// </summary>
/// <remarks>
/// The code generator does not reference the runtime assembly — it writes source text against a
/// contract — so nothing at build time stops the two from drifting. The failure that drift causes
/// is not subtle, but it is late: the generated C# names a member that does not exist, and every
/// strategy using that built-in fails to compile at once, with the cause several layers away from
/// the edit that caused it. This test closes that gap by re-deriving the comparison here, where
/// both sides are referenced.
/// </remarks>
public sealed class BuiltinNameResolutionTests
{
    private static readonly HashSet<string> RuntimeMembers =
        typeof(IMql5Runtime).GetMethods().Select(method => method.Name).ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void EveryCataloguedBuiltinResolvesToARuntimeMember()
    {
        var unresolved = new List<string>();

        foreach (string name in Mql5BuiltinCatalog.Names)
        {
            string clrName = Mql5ClrTypes.RuntimeBuiltinName(name);
            if (!RuntimeMembers.Contains(clrName))
            {
                unresolved.Add(name + " -> " + clrName);
            }
        }

        Assert.True(
            unresolved.Count == 0,
            "These MQL5 built-ins map onto no member of IMql5Runtime: " + string.Join(", ", unresolved));
    }

    [Fact]
    public void EveryAliasNamesAnExistingRuntimeMember()
    {
        var dangling = Mql5ClrTypes.RuntimeBuiltinAliases
            .Where(pair => !RuntimeMembers.Contains(pair.Value))
            .Select(pair => pair.Key + " -> " + pair.Value)
            .ToList();

        Assert.True(
            dangling.Count == 0,
            "These aliases point at members the runtime no longer declares: " + string.Join(", ", dangling));
    }

    [Fact]
    public void NoAliasIsRedundant()
    {
        // An alias that the leading-i rule would already produce is dead weight, and dead weight
        // in a mapping table is what makes the table stop describing the thing it maps.
        var redundant = Mql5ClrTypes.RuntimeBuiltinAliases
            .Where(pair => RuntimeMembers.Contains(pair.Key))
            .Select(pair => pair.Key)
            .ToList();

        Assert.True(
            redundant.Count == 0,
            "These names need no alias — the runtime already declares them verbatim: "
                + string.Join(", ", redundant));
    }
}
