using System.Text;
using YO4X.StrategyGovernance;

namespace YO4X.Domain.Tests;

/// <summary>
/// What <c>sizeof</c> is applied to.
///
/// MQL5 measures a variable as readily as a type, and an undecorated bare name is both
/// grammars at once. The front end used to keep only the written type, so
/// <c>char post[]; sizeof(post)</c> reached the back end as a type reference named
/// <c>post</c> and was refused with "the layout of 'post' is not known" — a true statement
/// about a type nobody declared, and the wrong question about a local array.
///
/// The sizes referenced here were measured against MetaEditor with a deliberate wrong
/// control on every probe: <c>switch(0) { case N: break; case (int)sizeof(x): break; }</c>
/// answers <c>error 172: case value already used</c> on a match and compiles clean on a
/// miss. A dynamic array of any element type is 52 bytes — its descriptor, not its
/// contents — while <c>char[10]</c> is 10 and <c>int[3][4]</c> is 48.
/// </summary>
public sealed class Mql5SizeOfOperandTests
{
    /// <summary>
    /// The corpus call is <c>WebRequest(...,post,sizeof(post),...)</c> over
    /// <c>char post[],result[];</c>. The operand must arrive as the local array.
    /// </summary>
    [Fact]
    public void BareNameOfALocalArrayResolvesToThatArray()
    {
        Mql5FrontEndResult front = Compile("int Measure() { char post[]; return(sizeof(post)); }");
        Mql5BindResult bind = Mql5Binder.Bind(front.Module!);
        Mql5IrSizeOfExpression measurement = SingleSizeOf(front.Module!);

        Mql5IrNameExpression operand = Assert.IsType<Mql5IrNameExpression>(measurement.Operand);
        Mql5ResolvedType type = bind.Model.TypeOf(operand);

        Assert.Equal("post", operand.Name);
        Assert.True(type.IsResolved);
        Assert.Equal(1, type.ArrayRank);
        Assert.Equal(Mql5IrScalarKind.Whole8, type.Scalar);
    }

    /// <summary>
    /// The written type is still carried, so a back end that reads only it keeps working
    /// and loses nothing it had before.
    /// </summary>
    [Fact]
    public void WrittenTypeSurvivesAlongsideTheOperand()
    {
        Mql5IrSizeOfExpression measurement =
            SingleSizeOf(Compile("int Measure() { char post[]; return(sizeof(post)); }").Module!);

        Assert.Equal("post", measurement.Type.Name);
        Assert.NotNull(measurement.Operand);
    }

    /// <summary>
    /// A built-in scalar keyword cannot be a value, so it takes the type form alone and no
    /// operand is invented for it.
    /// </summary>
    [Theory]
    [InlineData("sizeof(double)")]
    [InlineData("sizeof(int)")]
    [InlineData("sizeof(char[10])")]
    public void FormsOnlyATypeCanTakeCarryNoOperand(string expression)
    {
        Assert.Null(SingleSizeOf(Compile("int Measure() { return(" + expression + "); }").Module!).Operand);
    }

    /// <summary>
    /// A bare name that really is a type keeps its operand — the parser cannot tell — but
    /// the binder refuses to type it as a value, which is the signal a back end needs to
    /// fall back to the written type. Reporting it as an unresolved name would be a false
    /// diagnostic, so the lookup is silent.
    /// </summary>
    [Fact]
    public void BareNameOfADeclaredTypeIsNotTypedAsAValue()
    {
        Mql5FrontEndResult front = Compile("struct Row { int a; };\nint Measure() { return(sizeof(Row)); }");
        Mql5BindResult bind = Mql5Binder.Bind(front.Module!);
        Mql5IrSizeOfExpression measurement = SingleSizeOf(front.Module!);

        Mql5IrNameExpression operand = Assert.IsType<Mql5IrNameExpression>(measurement.Operand);
        Assert.False(bind.Model.TypeOf(operand).IsResolved);
        Assert.Equal("Row", measurement.Type.Name);
        Assert.DoesNotContain(
            bind.Diagnostics,
            diagnostic => diagnostic.Severity == Mql5RestrictedDiagnosticSeverity.Error);
    }

    /// <summary>
    /// The one corpus file that measures a variable. <c>post</c> is <c>char post[]</c>, a
    /// dynamic array, which MetaEditor sizes at 52 bytes.
    /// </summary>
    [Fact]
    public void CorpusNewsStopperMeasuresADynamicCharArray()
    {
        const string fileName = "News Stopper MT5.mq5";
        string path = Path.Combine(Mql5CorpusPath.Root(), fileName);
        Mql5FrontEndResult front = Mql5FrontEnd.Compile(
            new Mql5SourceDocument(fileName, File.ReadAllBytes(path)));

        Assert.True(front.Succeeded);
        Mql5BindResult bind = Mql5Binder.Bind(front.Module!);
        Mql5IrSizeOfExpression measurement = SingleSizeOf(front.Module!);
        Mql5IrNameExpression operand = Assert.IsType<Mql5IrNameExpression>(measurement.Operand);
        Mql5ResolvedType type = bind.Model.TypeOf(operand);

        Assert.Equal("post", operand.Name);
        Assert.Equal(1, type.ArrayRank);
        Assert.Equal(Mql5IrScalarKind.Whole8, type.Scalar);
    }

    /// <summary>The operator reaches the canonical document in both forms.</summary>
    [Fact]
    public void BothFormsReachTheCanonicalDocument()
    {
        string withOperand = Compile("int Measure() { char post[]; return(sizeof(post)); }")
            .Module!.ToCanonicalJson();
        string withoutOperand = Compile("int Measure() { return(sizeof(double)); }").Module!.ToCanonicalJson();

        Assert.Contains("\"kind\":\"sizeof\"", withOperand, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"post\"", withOperand, StringComparison.Ordinal);
        Assert.Contains("\"operand\":null", withoutOperand, StringComparison.Ordinal);
    }

    private static Mql5IrSizeOfExpression SingleSizeOf(Mql5IrV2Module module)
    {
        foreach (Mql5IrFunction function in module.Functions)
        {
            Mql5IrSizeOfExpression? found = InStatement(function.Body);
            if (found is not null)
            {
                return found;
            }
        }

        throw new InvalidOperationException("No sizeof expression was lowered.");
    }

    private static Mql5IrSizeOfExpression? InStatement(Mql5IrStatement? statement) => statement switch
    {
        Mql5IrBlockStatement block => block.Statements.Select(InStatement).FirstOrDefault(f => f is not null),
        Mql5IrReturnStatement result => InExpression(result.Value),
        Mql5IrExpressionStatement expression => InExpression(expression.Expression),
        Mql5IrLocalDeclarationStatement declaration =>
            declaration.Variables.Select(v => InExpression(v.Initializer)).FirstOrDefault(f => f is not null),
        Mql5IrIfStatement branch =>
            InExpression(branch.Condition) ?? InStatement(branch.WhenTrue) ?? InStatement(branch.WhenFalse),
        _ => null
    };

    private static Mql5IrSizeOfExpression? InExpression(Mql5IrExpression? expression) => expression switch
    {
        null => null,
        Mql5IrSizeOfExpression measurement => measurement,
        Mql5IrCallExpression call => call.Arguments.Select(InExpression).FirstOrDefault(f => f is not null),
        Mql5IrBinaryExpression binary => InExpression(binary.Left) ?? InExpression(binary.Right),
        Mql5IrUnaryExpression unary => InExpression(unary.Operand),
        Mql5IrCastExpression cast => InExpression(cast.Operand),
        Mql5IrAssignmentExpression assignment => InExpression(assignment.Target) ?? InExpression(assignment.Value),
        _ => null
    };

    private static Mql5FrontEndResult Compile(string source)
    {
        Mql5FrontEndResult front = Mql5FrontEnd.Compile(
            new Mql5SourceDocument("test.mq5", Encoding.UTF8.GetBytes(source)));
        Assert.True(front.Succeeded);
        return front;
    }
}
