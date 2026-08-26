using System.Text;
using YO4X.StrategyGovernance;

namespace YO4X.Domain.Tests;

/// <summary>
/// What the binder must and must not say about declarations it cannot fully resolve.
///
/// Each case here is a place where the pass used to report an error that was not one.
/// A false error is worse than silence: it tells a caller the source is wrong when the
/// source is valid MQL5, and the caller has no way to tell the two apart.
/// </summary>
public sealed class Mql5BinderTemplateTests
{
    [Fact]
    public void TemplateParameterIsNotReportedAsAnUnknownType()
    {
        IReadOnlyList<Mql5RestrictedDiagnostic> errors = BindErrors("""
            template<typename T>
            T Identity(T value)
              {
               T copy = value;
               return(copy);
              }
            """);

        Assert.Empty(errors);
    }

    [Fact]
    public void TemplateParameterLeavesScopeAfterItsDeclaration()
    {
        IReadOnlyList<Mql5RestrictedDiagnostic> errors = BindErrors("""
            template<typename T>
            T Identity(T value) { return(value); }
            T Stray() { T value = 0; return(value); }
            """);

        Assert.Contains(errors, diagnostic =>
            diagnostic.Code == Mql5BindDiagnosticCodes.UnknownType && diagnostic.Message.Contains("'T'", StringComparison.Ordinal));
    }

    /// <summary>
    /// A base written with template arguments names one declaration, so its members are
    /// inherited. Without stripping the arguments the instantiation inherits nothing and
    /// every member it uses is reported as referring to no declaration.
    /// </summary>
    [Fact]
    public void InstantiatedBaseContributesItsMembers()
    {
        IReadOnlyList<Mql5RestrictedDiagnostic> errors = BindErrors("""
            template<typename T1,typename T2>
            class Condition
              {
               public:
                  T1 Left;
                  T2 Right;
              };
            class Block0 : public Condition<double,int>
              {
               public:
                  void Run() { Left = 1; Right = 2; }
              };
            """);

        Assert.Empty(errors);
    }

    /// <summary>
    /// The parser spells a constructor's absent return type as an empty type name. That
    /// is an absence, not a name that failed to resolve.
    /// </summary>
    [Fact]
    public void ConstructorIsNotReportedAsNamingAnUnknownType()
    {
        IReadOnlyList<Mql5RestrictedDiagnostic> errors = BindErrors("""
            class Holder
              {
               public:
                  int Value;
                  Holder() { Value = 0; }
                  ~Holder() { }
              };
            """);

        Assert.Empty(errors);
    }

    /// <summary>
    /// MQL5 lets a user function share a built-in's name and resolves the call across
    /// both sets by argument count; MetaEditor compiles this file with 0 errors.
    /// </summary>
    [Fact]
    public void UserOverloadOfABuiltinNameDoesNotHideTheBuiltinArity()
    {
        IReadOnlyList<Mql5RestrictedDiagnostic> errors = BindErrors("""
            double MathRound(const double value, const double error)
              {
               return(error == 0 ? value : MathRound(value / error) * error);
              }
            """);

        Assert.Empty(errors);
    }

    /// <summary>
    /// The MQL4 arity of a name MQL5 also declares stays an error: MQL5's
    /// <c>iMA</c> takes six arguments and no user declaration here supplies a seventh.
    /// </summary>
    [Fact]
    public void Mql4ArityOfABuiltinIsStillReported()
    {
        IReadOnlyList<Mql5RestrictedDiagnostic> errors = BindErrors(
            "int OnInit() { return(iMA(_Symbol, PERIOD_CURRENT, 14, 0, MODE_EMA, PRICE_CLOSE, 1)); }");

        Assert.Contains(errors, diagnostic => diagnostic.Code == Mql5BindDiagnosticCodes.ArityMismatch);
    }

    private static IReadOnlyList<Mql5RestrictedDiagnostic> BindErrors(string source)
    {
        Mql5FrontEndResult front = Mql5FrontEnd.Compile(
            new Mql5SourceDocument("test.mq5", Encoding.UTF8.GetBytes(source)));
        Assert.True(front.Succeeded);

        return [.. Mql5Binder.Bind(front.Module!).Diagnostics
            .Where(diagnostic => diagnostic.Severity == Mql5RestrictedDiagnosticSeverity.Error)];
    }
}
