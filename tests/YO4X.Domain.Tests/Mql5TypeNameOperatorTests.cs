using System.Text;
using YO4X.StrategyGovernance;

namespace YO4X.Domain.Tests;

/// <summary>
/// The MQL5 <c>typename</c> operator.
///
/// It is not a function and is deliberately not catalogued as one: it accepts a written
/// type as readily as an expression, so a signature would have to lie about its argument.
/// Every fact asserted here was measured against MetaEditor, using the operator's own
/// compile-time folding — <c>int probe[(typename(int) == "int") ? 1 : 0]</c> compiles when
/// the comparison holds and answers <c>error 203: invalid index value</c> when it does not.
/// </summary>
public sealed class Mql5TypeNameOperatorTests
{
    [Fact]
    public void WrittenTypeTakesTheTypeForm()
    {
        Mql5IrTypeNameExpression node = SingleTypeName("string Describe() { return(typename(double)); }");

        Assert.NotNull(node.Type);
        Assert.Null(node.Operand);
        Assert.Equal("double", node.Type!.Name);
        Assert.Equal(Mql5IrScalarKind.Real64, node.Type.Scalar);
    }

    [Fact]
    public void BareNameTakesTheExpressionForm()
    {
        // A bare name is indistinguishable from a variable until names are resolved, so the
        // parser must not decide it; the binder does.
        Mql5IrTypeNameExpression node = SingleTypeName("string Describe(double value) { return(typename(value)); }");

        Assert.Null(node.Type);
        Mql5IrNameExpression operand = Assert.IsType<Mql5IrNameExpression>(node.Operand);
        Assert.Equal("value", operand.Name);
    }

    [Fact]
    public void HandleAndArrayDecorationTakeTheTypeForm()
    {
        Assert.True(SingleTypeName("class C { public: int a; };\nstring D() { return(typename(C*)); }").Type?.IsPointer);
        Assert.NotEmpty(SingleTypeName("string D() { return(typename(double[])); }").Type!.ArrayRanks);
    }

    /// <summary>
    /// MetaEditor warns <c>implicit conversion from 'string' to 'int'</c> when the result is
    /// stored in an <c>int</c>, which is how the return type was established.
    /// </summary>
    [Fact]
    public void ResultIsAString()
    {
        Mql5FrontEndResult front = Compile("string Describe(double value) { return(typename(value)); }");
        Mql5BindResult bind = Mql5Binder.Bind(front.Module!);

        Mql5IrTypeNameExpression node = Assert.IsType<Mql5IrTypeNameExpression>(
            ((Mql5IrReturnStatement)front.Module!.Functions[0].Body!.Statements[0]).Value);
        Assert.Equal(Mql5IrScalarKind.Text, bind.Model.TypeOf(node).Scalar);
    }

    /// <summary>
    /// <c>typename(T)</c> names the template parameter, not a variable. Reporting it as an
    /// unresolved name was the whole reason the five corpus files carried false errors.
    /// </summary>
    [Fact]
    public void TemplateParameterOperandIsNotReportedAsUnresolved()
    {
        Mql5FrontEndResult front = Compile("""
            template<typename T>
            void Fill(T &output[])
              {
               T empty = (typename(T) == "string") ? (T)"" : (T)0;
               output[0] = empty;
              }
            """);

        Assert.True(front.Succeeded);
        Assert.DoesNotContain(
            Mql5Binder.Bind(front.Module!).Diagnostics,
            diagnostic => diagnostic.Severity == Mql5RestrictedDiagnosticSeverity.Error);
    }

    [Fact]
    public void TypeNameReachesTheCanonicalDocument()
    {
        Mql5FrontEndResult front = Compile("string Describe(double value) { return(typename(value)); }");

        Assert.Contains("\"kind\":\"typename\"", front.Module!.ToCanonicalJson(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The five corpus files that used <c>typename</c> bind without any diagnostic naming it
    /// or naming the template parameter it was applied to.
    /// </summary>
    [Theory]
    [InlineData("EA Correlations.mq5")]
    [InlineData("MM3.0 FLIP CODEPRO.mq5")]
    [InlineData("Prop-Firm Expert.mq5")]
    [InlineData("XAU-GU Scalper.mq5")]
    public void CorpusTypeNameUsesBindWithoutFalseDiagnostics(string fileName)
    {
        string path = Path.Combine(Mql5CorpusPath.Root(), fileName);
        Mql5FrontEndResult front = Mql5FrontEnd.Compile(
            new Mql5SourceDocument(fileName, File.ReadAllBytes(path)));

        Assert.True(front.Succeeded);
        Assert.DoesNotContain(
            Mql5Binder.Bind(front.Module!).Diagnostics,
            diagnostic => diagnostic.Message.Contains("'typename'", StringComparison.Ordinal));
    }

    private static Mql5IrTypeNameExpression SingleTypeName(string source)
    {
        Mql5FrontEndResult front = Compile(source);
        Assert.True(front.Succeeded);
        Mql5IrFunction function = front.Module!.Functions[^1];
        Mql5IrReturnStatement statement = Assert.IsType<Mql5IrReturnStatement>(function.Body!.Statements[0]);
        return Assert.IsType<Mql5IrTypeNameExpression>(statement.Value);
    }

    private static Mql5FrontEndResult Compile(string source) =>
        Mql5FrontEnd.Compile(new Mql5SourceDocument("test.mq5", Encoding.UTF8.GetBytes(source)));
}
