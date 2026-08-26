using System.Text;
using YO4X.StrategyGovernance;

namespace YO4X.Domain.Tests;

/// <summary>
/// Templates are carried through the front end, not monomorphised: the parameter names
/// travel with the declaration they introduce and every use site keeps the arguments it
/// wrote. Nothing here asserts that a template can be executed — only that the source
/// survives lowering as written, and that a shape MQL5 itself rejects is still refused.
/// </summary>
public sealed class Mql5TemplateLoweringTests
{
    [Fact]
    public void GenericFunctionLowersWithItsTypeParameters()
    {
        Mql5FrontEndResult result = Compile("""
            template<typename T>
            T Sum(const T &values[], int count)
              {
               T total = 0;
               for(int index = 0; index < count; index++)
                  total += values[index];
               return(total);
              }
            """);

        Assert.True(result.Succeeded);
        Mql5IrFunction sum = Assert.Single(result.Module!.Functions);
        Assert.Equal("Sum", sum.Name);
        Assert.Equal(["T"], sum.TypeParameters);
        Assert.Equal("T", sum.ReturnType.Name);
        Assert.NotNull(sum.Body);
    }

    [Fact]
    public void GenericClassLowersWithItsTypeParametersAndAnInstantiationKeepsItsArguments()
    {
        Mql5FrontEndResult result = Compile("""
            class BlockCalls { public: virtual void Run() { } };
            template<typename T1,typename T2>
            class Condition : public BlockCalls
              {
               public:
                  T1 Left;
                  T2 Right;
                  virtual void Run() { }
              };
            class Block0 : public Condition<double,int>
              {
               public:
                  virtual void Run() { }
              };
            """);

        Assert.True(result.Succeeded);
        Mql5IrTypeDeclaration condition = result.Module!.Types.Single(type => type.Name == "Condition");
        Assert.Equal(["T1", "T2"], condition.TypeParameters);
        Assert.Equal("BlockCalls", condition.BaseTypeName);
        Assert.Equal(["T1", "T2"], condition.Fields.Select(field => field.Type.Name));

        // The arguments stay attached to the written base name; only the declaration side
        // records what they bind to.
        Mql5IrTypeDeclaration block = result.Module.Types.Single(type => type.Name == "Block0");
        Assert.Empty(block.TypeParameters);
        Assert.Equal("Condition<double,int>", block.BaseTypeName);
    }

    [Fact]
    public void GenericMethodInsideAClassLowersWithItsTypeParameters()
    {
        Mql5FrontEndResult result = Compile("""
            class Adapter
              {
               public:
                  template<typename AP>
                  static double Read(string symbol, AP applied)
                    {
                     return(0.0);
                    }
              };
            """);

        Assert.True(result.Succeeded);
        Mql5IrTypeDeclaration adapter = Assert.Single(result.Module!.Types);
        Mql5IrFunction read = Assert.Single(adapter.Methods);
        Assert.Equal("Read", read.Name);
        Assert.Equal(["AP"], read.TypeParameters);
        Assert.True(read.IsStatic);
    }

    [Fact]
    public void OrdinaryDeclarationsCarryNoTypeParameters()
    {
        Mql5FrontEndResult result = Compile("""
            struct Signal { double price; };
            int OnInit() { return(0); }
            """);

        Assert.True(result.Succeeded);
        Assert.Empty(Assert.Single(result.Module!.Types).TypeParameters);
        Assert.Empty(Assert.Single(result.Module.Functions).TypeParameters);
    }

    [Fact]
    public void TypeParametersReachTheCanonicalDocument()
    {
        Mql5FrontEndResult result = Compile("template<typename T> T Identity(T value) { return(value); }");

        Assert.True(result.Succeeded);
        Assert.Contains("\"typeParameters\":[\"T\"]", result.Module!.ToCanonicalJson(), StringComparison.Ordinal);
    }

    /// <summary>
    /// MetaEditor answers a repeated parameter name with <c>error 282: idenfitier 'T'
    /// already used</c>, so lowering it would produce a declaration the source could never
    /// have compiled.
    /// </summary>
    [Fact]
    public void RepeatedTypeParameterIsRefused()
    {
        Mql5FrontEndResult result = Compile("template<typename T,typename T> T Identity(T value) { return(value); }");

        Assert.False(result.Succeeded);
        Assert.Null(result.Module);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "MQL5_LOWER_UNSUPPORTED_TEMPLATE"
            && diagnostic.Severity == Mql5RestrictedDiagnosticSeverity.Error);
    }

    /// <summary>
    /// The eleven corpus files that stopped at lowering all reach IR once templates are
    /// carried. Each is checked for the construct that used to refuse it, so a regression
    /// cannot pass by lowering an empty module.
    /// </summary>
    [Theory]
    [InlineData("18-avg-ma.mq5")]
    [InlineData("EA Correlations.mq5")]
    [InlineData("Elise-EA.mq5")]
    [InlineData("MM3.0 FLIP CODEPRO.mq5")]
    [InlineData("Prop-Firm Expert.mq5")]
    [InlineData("TopBottomEA.mq5")]
    [InlineData("Volume Profile Source Code.mq5")]
    [InlineData("VP Range V6 Source Code.mq5")]
    [InlineData("XAU-GU Scalper.mq5")]
    public void CorpusTemplateFilesReachIr(string fileName)
    {
        string path = Path.Combine(FindRepositoryRoot(), "Testing", "Mq5", fileName);
        Mql5FrontEndResult result = Mql5FrontEnd.Compile(new Mql5SourceDocument(fileName, File.ReadAllBytes(path)));

        Assert.True(result.Succeeded, string.Join(
            " | ",
            result.Diagnostics
                .Where(diagnostic => diagnostic.Severity == Mql5RestrictedDiagnosticSeverity.Error)
                .Take(4)
                .Select(diagnostic => $"{diagnostic.Code}@{diagnostic.Line}")));
        Assert.Contains(
            result.Module!.Functions.Concat(result.Module.Types.SelectMany(type => type.Methods)).Cast<object>()
                .Concat(result.Module.Types),
            declaration => declaration switch
            {
                Mql5IrFunction function => function.TypeParameters.Count > 0,
                Mql5IrTypeDeclaration type => type.TypeParameters.Count > 0,
                _ => false
            });
    }

    private static Mql5FrontEndResult Compile(string source) =>
        Mql5FrontEnd.Compile(new Mql5SourceDocument("test.mq5", Encoding.UTF8.GetBytes(source)));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "YO4X.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
