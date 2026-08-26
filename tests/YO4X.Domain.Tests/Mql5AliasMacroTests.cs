using System.Text;
using YO4X.StrategyGovernance;

namespace YO4X.Domain.Tests;

/// <summary>
/// The object-like <c>#define</c> that aliases one identifier to another.
///
/// This is the shape three corpus files use to fake the MQL4 dialect: they declare
/// <c>double MQL4_iFractals(string,int,int,int)</c>, write
/// <c>#define iFractals MQL4_iFractals</c> under it, and then call
/// <c>iFractals(sym,0,1,1)</c>. MetaEditor reports the real declaration as
/// <c>built-in: int iFractals(const string,ENUM_TIMEFRAMES)</c> — two parameters — and
/// still compiles all three files with 0 errors, which is only possible because the
/// four-argument calls never reach the built-in at all. Leaving the macro unexpanded made
/// the back end refuse them as MQL4 arity, a diagnostic that was simply untrue.
///
/// Only the alias shape is expanded, and the refusals below matter as much as the
/// rewrites: a macro standing for a literal, an expression or a reserved word keeps its
/// original spelling, because rewriting one of those would put a token in the stream that
/// the source never wrote.
/// </summary>
public sealed class Mql5AliasMacroTests
{
    private const string FractalShim = """
        double MQL4_iFractals(string symbol,int timeframe,int mode,int shift)
          {
           return(iFractals(symbol,PERIOD_CURRENT));
          }
        #define iFractals MQL4_iFractals
        double Read(string symbol) { return(iFractals(symbol,0,1,1)); }
        """;

    /// <summary>
    /// The call below the directive is the shim; the call inside it, written above the
    /// directive, is still the built-in. Order is the whole point — C and MQL5 both make a
    /// macro visible only after its own line, which is what lets a shim call the very name
    /// it is about to shadow.
    /// </summary>
    [Fact]
    public void CallBelowTheDirectiveBecomesTheAliasAndTheOneAboveDoesNot()
    {
        Mql5IrV2Module module = Lower(FractalShim);

        Assert.Equal("MQL4_iFractals", ReturnedCalleeName(module, "Read"));
        Assert.Equal(4, ReturnedCall(module, "Read").Arguments.Count);
        Assert.Equal("iFractals", ReturnedCalleeName(module, "MQL4_iFractals"));
        Assert.Equal(2, ReturnedCall(module, "MQL4_iFractals").Arguments.Count);
    }

    /// <summary>
    /// The four-argument call is what the back end used to refuse. It now names a module
    /// function that accepts four arguments, so no built-in arity is consulted for it.
    /// </summary>
    [Fact]
    public void AliasedCallBindsWithoutDiagnostics()
    {
        Mql5BindResult bind = Mql5Binder.Bind(Lower(FractalShim));

        Assert.DoesNotContain(
            bind.Diagnostics,
            diagnostic => diagnostic.Severity == Mql5RestrictedDiagnosticSeverity.Error);
    }

    /// <summary>
    /// The three corpus files that carry the shim. In each lowered module the spelling
    /// <c>iFractals</c> survives exactly twice — as the name the <c>#define</c> binds, and
    /// as the one genuine two-argument built-in call inside the shim itself — while
    /// <c>MQL4_iFractals</c> appears four times: the declaration, the replacement the
    /// directive records, and the two rewritten call sites.
    /// </summary>
    [Theory]
    [InlineData("Lizard_1.85.mq5")]
    [InlineData("GoldReaper_MT5_ECN_Patched (1).mq5")]
    [InlineData("The Gold Reaper v4.1 MT5.mq5")]
    public void CorpusFractalShimsRewriteBothFourArgumentCallSites(string fileName)
    {
        string path = Path.Combine(Mql5CorpusPath.Root(), fileName);
        Mql5FrontEndResult front = Mql5FrontEnd.Compile(
            new Mql5SourceDocument(fileName, File.ReadAllBytes(path)));

        Assert.True(front.Succeeded);
        string document = front.Module!.ToCanonicalJson();
        Assert.Equal(2, Occurrences(document, "\"iFractals\""));
        Assert.Equal(4, Occurrences(document, "\"MQL4_iFractals\""));
    }

    /// <summary>
    /// MetaEditor reports <c>built-in: int iFractals(const string,ENUM_TIMEFRAMES)</c> and
    /// nothing else, so a four-argument call that really does reach the built-in stays
    /// refused. The alias pass must not have loosened the catalogue.
    /// </summary>
    [Fact]
    public void UnaliasedFractalArityStaysRefused()
    {
        Assert.True(Mql5BuiltinCatalog.TryGet("iFractals", out IReadOnlyList<Mql5BuiltinSignature> overloads));
        Assert.All(overloads, signature => Assert.False(signature.AcceptsArgumentCount(4)));
        Assert.Contains(overloads, signature => signature.AcceptsArgumentCount(2));
    }

    /// <summary>
    /// A replacement list that is not exactly one identifier is left alone. Rewriting
    /// <c>Limit</c> into <c>10</c> here would put a literal where the source wrote a name,
    /// and the IR would stop describing what was written.
    /// </summary>
    [Theory]
    [InlineData("#define Limit 10\nint Read() { return(Limit); }", "Limit")]
    [InlineData("#define Limit (1 + 2)\nint Read() { return(Limit); }", "Limit")]
    [InlineData("#define Limit\nint Read() { return(Limit); }", "Limit")]
    [InlineData("#define Limit int\nint Read() { return(Limit); }", "Limit")]
    [InlineData("#define Limit Total\nint Read() { return(Limit); }", "Total")]
    public void OnlyASingleIdentifierReplacementRewritesTheName(string source, string expected)
    {
        Assert.Equal(expected, ReturnedName(Lower(source), "Read"));
    }

    /// <summary>
    /// A function-like macro is a different construct with a different expansion, and its
    /// name is not an alias for the first token of its parameter list.
    /// </summary>
    [Fact]
    public void FunctionLikeMacroIsNotAnAlias()
    {
        Mql5IrV2Module module = Lower("#define Twice(x) x\nint Read(int a) { return(Twice(a)); }");

        Assert.Equal("Twice", ReturnedCalleeName(module, "Read"));
    }

    /// <summary>An alias stops at <c>#undef</c>, and a later <c>#define</c> rebinds it.</summary>
    [Fact]
    public void UndefAndRedefinitionChangeTheAliasInPlace()
    {
        Mql5IrV2Module module = Lower("""
            #define Limit Total
            int First() { return(Limit); }
            #undef Limit
            int Second() { return(Limit); }
            #define Limit Ceiling
            int Third() { return(Limit); }
            """);

        Assert.Equal("Total", ReturnedName(module, "First"));
        Assert.Equal("Limit", ReturnedName(module, "Second"));
        Assert.Equal("Ceiling", ReturnedName(module, "Third"));
    }

    /// <summary>
    /// A chain resolves to its end, and a macro naming itself expands once and stops rather
    /// than looping — the same rule C gives a self-referential macro.
    /// </summary>
    [Fact]
    public void ChainsResolveAndSelfReferenceTerminates()
    {
        Assert.Equal(
            "Ceiling",
            ReturnedName(Lower("#define A B\n#define B Ceiling\nint Read() { return(A); }"), "Read"));
        Assert.Equal(
            "Limit",
            ReturnedName(Lower("#define Limit Limit\nint Read() { return(Limit); }"), "Read"));
    }

    private static int Occurrences(string document, string needle)
    {
        int count = 0;
        int cursor = 0;
        while ((cursor = document.IndexOf(needle, cursor, StringComparison.Ordinal)) >= 0)
        {
            count++;
            cursor += needle.Length;
        }

        return count;
    }

    private static Mql5IrExpression Returned(Mql5IrV2Module module, string functionName)
    {
        Mql5IrFunction function = module.Functions.Single(
            candidate => string.Equals(candidate.Name, functionName, StringComparison.Ordinal));
        Mql5IrReturnStatement statement = Assert.IsType<Mql5IrReturnStatement>(function.Body!.Statements[0]);
        return statement.Value!;
    }

    private static string ReturnedName(Mql5IrV2Module module, string functionName)
        => Assert.IsType<Mql5IrNameExpression>(Returned(module, functionName)).Name;

    private static Mql5IrCallExpression ReturnedCall(Mql5IrV2Module module, string functionName)
        => Assert.IsType<Mql5IrCallExpression>(Returned(module, functionName));

    private static string ReturnedCalleeName(Mql5IrV2Module module, string functionName)
        => Assert.IsType<Mql5IrNameExpression>(ReturnedCall(module, functionName).Callee).Name;

    private static Mql5IrV2Module Lower(string source)
    {
        Mql5FrontEndResult front = Mql5FrontEnd.Compile(
            new Mql5SourceDocument("test.mq5", Encoding.UTF8.GetBytes(source)));
        Assert.True(front.Succeeded);
        return front.Module!;
    }
}
