using System.Text;
using YO4X.StrategyGovernance;

namespace YO4X.Domain.Tests;

/// <summary>
/// The two reasons a method can reach the IR with no body, which look identical in the
/// IR unless they are kept apart and need opposite treatment from a back end.
///
/// A prototype has its definition somewhere — often out of line, as <c>CAvg::Open</c>.
/// An abstract member, written with MQL5's <c>= 0</c> pure specifier, has none anywhere:
/// MetaEditor answers an attempt to instantiate the declaring class with
/// <c>error 383: cannot instantiate abstract class</c>.
/// </summary>
public sealed class Mql5AbstractMemberTests
{
    [Fact]
    public void PureSpecifierMarksTheMemberAbstract()
    {
        Mql5IrTypeDeclaration type = SingleType("""
            class Base
              {
               public:
                  virtual void Run() = 0;
              };
            """);

        Mql5IrFunction run = Assert.Single(type.Methods);
        Assert.True(run.IsAbstract);
        Assert.True(run.IsVirtual);
        Assert.Null(run.Body);
    }

    [Fact]
    public void OrdinaryPrototypeIsNotAbstract()
    {
        Mql5IrTypeDeclaration type = SingleType("""
            class CAvg
              {
               public:
                  double Open(const string symbol, const int index) const;
              };
            """);

        Assert.False(Assert.Single(type.Methods).IsAbstract);
    }

    [Fact]
    public void MethodWithABodyIsNotAbstract()
    {
        Mql5IrTypeDeclaration type = SingleType("class C { public: virtual int Value() { return(1); } };");

        Assert.False(Assert.Single(type.Methods).IsAbstract);
    }

    [Fact]
    public void AbstractReachesTheCanonicalDocument()
    {
        Mql5FrontEndResult front = Compile("class Base { public: virtual void Run() = 0; };");

        Assert.Contains("\"abstract\":true", front.Module!.ToCanonicalJson(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The five corpus files whose only bodyless method is <c>BlockCalls._execute_</c>. It is
    /// abstract, and no definition of it exists anywhere in the module — so a back end must
    /// emit an abstract member rather than look for a body it will never find.
    /// </summary>
    [Theory]
    [InlineData("EA Correlations.mq5")]
    [InlineData("MM3.0 FLIP CODEPRO.mq5")]
    [InlineData("MM3.0 FLIP CODEPRO (2).mq5")]
    [InlineData("Prop-Firm Expert.mq5")]
    [InlineData("XAU-GU Scalper.mq5")]
    public void CorpusBlockCallsExecuteIsAbstractWithNoDefinition(string fileName)
    {
        Mql5IrV2Module module = CompileCorpus(fileName);

        Mql5IrFunction execute = module.Types
            .Single(type => type.Name == "BlockCalls")
            .Methods
            .Single(method => method.Name == "_execute_");

        Assert.True(execute.IsAbstract);
        Assert.Null(execute.Body);
        Assert.DoesNotContain(module.Functions, function =>
            function.Name.EndsWith("::_execute_", StringComparison.Ordinal));
    }

    /// <summary>
    /// The opposite case, and the only file in the corpus that shows it: every bodyless
    /// member of <c>CAvg</c> is an ordinary prototype whose definition sits at module scope
    /// under the qualified name. The join has to be by signature rather than by name alone,
    /// because <c>GetMA</c> and <c>AppliedPrice</c> are each declared twice.
    /// </summary>
    [Fact]
    public void CorpusOutOfLineDefinitionsArePresentUnderTheQualifiedName()
    {
        Mql5IrV2Module module = CompileCorpus("avg-ma.mq5");

        Mql5IrTypeDeclaration type = module.Types.Single(candidate => candidate.Name == "CAvg");
        List<Mql5IrFunction> bodyless = [.. type.Methods.Where(method => method.Body is null)];

        Assert.NotEmpty(bodyless);
        Assert.All(bodyless, method => Assert.False(method.IsAbstract));

        List<Mql5IrFunction> definitions = [.. module.Functions.Where(function =>
            function.Name.StartsWith("CAvg::", StringComparison.Ordinal) && function.Body is not null)];

        Assert.Equal(bodyless.Count, definitions.Count);
        Assert.All(bodyless, method => Assert.Contains(
            definitions,
            definition => definition.Name == "CAvg::" + method.Name
                && definition.Parameters.Count == method.Parameters.Count));
    }

    private static Mql5IrTypeDeclaration SingleType(string source)
    {
        Mql5FrontEndResult front = Compile(source);
        Assert.True(front.Succeeded);
        return Assert.Single(front.Module!.Types);
    }

    private static Mql5IrV2Module CompileCorpus(string fileName)
    {
        string path = Path.Combine(Mql5CorpusPath.Root(), fileName);
        Mql5FrontEndResult front = Mql5FrontEnd.Compile(
            new Mql5SourceDocument(fileName, File.ReadAllBytes(path)));
        Assert.True(front.Succeeded);
        return front.Module!;
    }

    private static Mql5FrontEndResult Compile(string source) =>
        Mql5FrontEnd.Compile(new Mql5SourceDocument("test.mq5", Encoding.UTF8.GetBytes(source)));
}
