using System.Runtime.CompilerServices;

namespace YO4X.StrategyGovernance;

/// <summary>
/// What a resolved type <em>is</em>, independent of how it was written.
///
/// <see cref="Unknown"/> is a first-class answer, not a failure to record one: it
/// means the binder could not name the type, and every consumer downstream must
/// treat it as "do not generate code for this" rather than guess.
/// </summary>
public enum Mql5ResolvedTypeKind
{
    /// <summary>No type could be determined.</summary>
    Unknown = 0,

    /// <summary>One of the MQL5 built-in scalars described by <see cref="Mql5IrScalarKind"/>.</summary>
    Scalar,

    /// <summary>A declared or built-in enumeration.</summary>
    Enumeration,

    /// <summary>A declared or built-in <c>struct</c>.</summary>
    Structure,

    /// <summary>A declared or built-in <c>class</c> or <c>interface</c>.</summary>
    Class,

    /// <summary>The expression names a type rather than a value, as in <c>ENUM_X::Member</c>.</summary>
    TypeName,

    /// <summary>The expression names a function rather than a value.</summary>
    Function,

    /// <summary>The <c>NULL</c> literal, which is assignable to any pointer.</summary>
    NullLiteral
}

/// <summary>
/// A type as the binder resolved it: a classification, an optional scalar kind, a
/// name, an array rank and pointer-ness.
///
/// Array-ness is a rank count rather than a wrapper type so that
/// <c>double buffer[][2]</c> and its element type differ by exactly one integer,
/// which keeps indexing cheap and total.
/// </summary>
public sealed record Mql5ResolvedType(
    Mql5ResolvedTypeKind Kind,
    Mql5IrScalarKind Scalar,
    string Name,
    int ArrayRank,
    bool IsPointer,
    bool IsBuiltin)
{
    /// <summary>True when the binder produced an answer other than <see cref="Mql5ResolvedTypeKind.Unknown"/>.</summary>
    public bool IsResolved => Kind != Mql5ResolvedTypeKind.Unknown;

    /// <summary>True when at least one array dimension is present.</summary>
    public bool IsArray => ArrayRank > 0;

    /// <summary>True when the type is a numeric or logical scalar that participates in arithmetic.</summary>
    public bool IsArithmetic =>
        ArrayRank == 0
        && Kind is Mql5ResolvedTypeKind.Scalar or Mql5ResolvedTypeKind.Enumeration
        && Scalar is not (Mql5IrScalarKind.Text or Mql5IrScalarKind.Void);

    /// <summary>The unresolved type. Shared, so identity comparison is meaningful.</summary>
    public static Mql5ResolvedType Unknown { get; } =
        new(Mql5ResolvedTypeKind.Unknown, Mql5IrScalarKind.None, "?", 0, false, false);

    /// <summary>The type of the <c>NULL</c> literal.</summary>
    public static Mql5ResolvedType Null { get; } =
        new(Mql5ResolvedTypeKind.NullLiteral, Mql5IrScalarKind.None, "NULL", 0, true, true);

    /// <summary><c>void</c>.</summary>
    public static Mql5ResolvedType Nothing { get; } = ForScalar(Mql5IrScalarKind.Void);

    /// <summary><c>bool</c>.</summary>
    public static Mql5ResolvedType Logical { get; } = ForScalar(Mql5IrScalarKind.Logical);

    /// <summary>The canonical resolved type for one MQL5 scalar kind.</summary>
    public static Mql5ResolvedType ForScalar(Mql5IrScalarKind scalar) =>
        scalar == Mql5IrScalarKind.None
            ? Unknown
            : new Mql5ResolvedType(
                Mql5ResolvedTypeKind.Scalar,
                scalar,
                Mql5ScalarNames.Spell(scalar),
                0,
                false,
                true);

    /// <summary>The same type with a different array rank.</summary>
    public Mql5ResolvedType WithArrayRank(int rank) =>
        rank == ArrayRank ? this : this with { ArrayRank = rank < 0 ? 0 : rank };

    /// <summary>
    /// The type obtained by applying one <c>[]</c>. Indexing a <c>string</c> yields a
    /// character; indexing anything with no remaining rank yields <see cref="Unknown"/>.
    /// </summary>
    public Mql5ResolvedType ElementType()
    {
        if (ArrayRank > 0)
        {
            return this with { ArrayRank = ArrayRank - 1 };
        }

        if (Scalar == Mql5IrScalarKind.Text)
        {
            return ForScalar(Mql5IrScalarKind.Natural16);
        }

        return Unknown;
    }

    /// <summary>A short human-readable spelling, used only in diagnostic messages.</summary>
    public string Display()
    {
        string core = IsPointer ? Name + "*" : Name;
        return ArrayRank == 0 ? core : core + string.Concat(Enumerable.Repeat("[]", ArrayRank));
    }
}

/// <summary>MQL5 source spellings for the scalar kinds, for diagnostic text only.</summary>
public static class Mql5ScalarNames
{
    /// <summary>The MQL5 keyword that spells <paramref name="scalar"/>.</summary>
    public static string Spell(Mql5IrScalarKind scalar) => scalar switch
    {
        Mql5IrScalarKind.Void => "void",
        Mql5IrScalarKind.Logical => "bool",
        Mql5IrScalarKind.Whole8 => "char",
        Mql5IrScalarKind.Natural8 => "uchar",
        Mql5IrScalarKind.Whole16 => "short",
        Mql5IrScalarKind.Natural16 => "ushort",
        Mql5IrScalarKind.Whole32 => "int",
        Mql5IrScalarKind.Natural32 => "uint",
        Mql5IrScalarKind.Whole64 => "long",
        Mql5IrScalarKind.Natural64 => "ulong",
        Mql5IrScalarKind.Real32 => "float",
        Mql5IrScalarKind.Real64 => "double",
        Mql5IrScalarKind.Text => "string",
        Mql5IrScalarKind.Moment => "datetime",
        Mql5IrScalarKind.Colour => "color",
        _ => "?"
    };
}

/// <summary>What a name turned out to refer to.</summary>
public enum Mql5SymbolKind
{
    /// <summary>The name refers to nothing the binder could find.</summary>
    Unresolved = 0,

    /// <summary>A variable declared inside a function body or block.</summary>
    LocalVariable,

    /// <summary>A function or method parameter.</summary>
    Parameter,

    /// <summary>A file-scope variable.</summary>
    GlobalVariable,

    /// <summary>An <c>input</c>, <c>sinput</c> or <c>extern</c> declaration.</summary>
    Input,

    /// <summary>A member of a declared enumeration.</summary>
    EnumMember,

    /// <summary>A field of the enclosing <c>struct</c> or <c>class</c>.</summary>
    Field,

    /// <summary>A method of a <c>struct</c> or <c>class</c>.</summary>
    Method,

    /// <summary>A file-scope function declared in this module.</summary>
    Function,

    /// <summary>A <c>struct</c>, <c>class</c> or <c>interface</c> name used as a name.</summary>
    TypeName,

    /// <summary>An enumeration name used as a name.</summary>
    EnumerationName,

    /// <summary>A <c>#define</c> replacement name.</summary>
    Define,

    /// <summary>A function of the MQL5 standard runtime.</summary>
    BuiltinFunction,

    /// <summary>A named constant of the MQL5 standard runtime.</summary>
    BuiltinConstant,

    /// <summary>A type of the MQL5 standard runtime or standard library.</summary>
    BuiltinType
}

/// <summary>
/// The outcome of resolving one name.
///
/// <paramref name="IsImplemented"/> separates the two questions that matter most:
/// whether we <em>understood</em> the name, and whether we can <em>execute</em> it.
/// A built-in the runtime does not yet provide is fully resolved and still not
/// implemented; that combination is an information diagnostic, never an error.
/// </summary>
public sealed record Mql5ResolvedSymbol(
    Mql5SymbolKind Kind,
    string Name,
    Mql5ResolvedType Type,
    int DeclarationLine,
    int DeclarationColumn,
    bool IsImplemented)
{
    /// <summary>True when the name refers to something.</summary>
    public bool IsResolved => Kind != Mql5SymbolKind.Unresolved;

    /// <summary>True when the name refers to something the MQL5 runtime provides.</summary>
    public bool IsBuiltin =>
        Kind is Mql5SymbolKind.BuiltinFunction or Mql5SymbolKind.BuiltinConstant or Mql5SymbolKind.BuiltinType;

    /// <summary>The symbol recorded for a name that resolved to nothing.</summary>
    public static Mql5ResolvedSymbol ForUnresolved(string name, int line, int column) =>
        new(Mql5SymbolKind.Unresolved, name, Mql5ResolvedType.Unknown, line, column, false);
}

/// <summary>
/// Counters describing what one binding pass saw. These exist so that the health of
/// the binder can be measured over a corpus without re-walking the IR, and so that
/// an unresolved result can be attributed to a cause rather than merely counted.
/// </summary>
public sealed record Mql5BindStatistics(
    int Expressions,
    int TypedExpressions,
    int NameExpressions,
    int ResolvedNames,
    int UnresolvedNames,
    int BuiltinFunctionReferences,
    int BuiltinConstantReferences,
    int DistinctUnimplementedBuiltins,
    int Calls,
    int ResolvedCalls,
    int UnresolvedCalls,
    int AmbiguousCalls,
    int ArityMismatches,
    int UseBeforeDeclarations,
    int DuplicateDeclarations,
    int UnknownTypeReferences,
    int UnknownFromBuiltinCall,
    int UnknownFromMemberAccess,
    int DepthLimitHits)
{
    /// <summary>All-zero counters, used for modules that never began binding.</summary>
    public static Mql5BindStatistics Empty { get; } =
        new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>The share of expressions that received a type other than Unknown, in [0, 1].</summary>
    public double TypedExpressionShare => Expressions == 0 ? 0.0 : (double)TypedExpressions / Expressions;

    /// <summary>The share of name expressions that resolved to a symbol, in [0, 1].</summary>
    public double ResolvedNameShare => NameExpressions == 0 ? 0.0 : (double)ResolvedNames / NameExpressions;
}

/// <summary>
/// Reference identity for IR expression nodes.
///
/// IR nodes are records and therefore compare by value. Two distinct occurrences of
/// <c>i + 1</c> at the same source position would be the same dictionary key under
/// value equality, silently merging their bindings. Every map in a
/// <see cref="Mql5SemanticModel"/> is keyed by node identity instead.
/// </summary>
public sealed class Mql5ExpressionReferenceComparer : IEqualityComparer<Mql5IrExpression>
{
    /// <summary>The shared instance.</summary>
    public static Mql5ExpressionReferenceComparer Instance { get; } = new();

    private Mql5ExpressionReferenceComparer()
    {
    }

    /// <inheritdoc/>
    public bool Equals(Mql5IrExpression? x, Mql5IrExpression? y) => ReferenceEquals(x, y);

    /// <inheritdoc/>
    public int GetHashCode(Mql5IrExpression obj) => RuntimeHelpers.GetHashCode(obj);
}

/// <summary>
/// A structural IR module plus everything the binder learned about it: what each
/// name refers to, what each expression's type is, and where it could not tell.
///
/// The model is deliberately additive. It never rewrites the IR, so a caller can
/// discard it and still hold a valid module, and two bindings of the same module
/// always agree because binding has no state of its own.
/// </summary>
public sealed record Mql5SemanticModel(
    Mql5IrV2Module Module,
    IReadOnlyDictionary<Mql5IrExpression, Mql5ResolvedSymbol> Symbols,
    IReadOnlyDictionary<Mql5IrExpression, Mql5ResolvedType> ExpressionTypes,
    IReadOnlyList<Mql5RestrictedDiagnostic> Diagnostics)
{
    /// <summary>Counters for the pass that produced this model.</summary>
    public Mql5BindStatistics Statistics { get; init; } = Mql5BindStatistics.Empty;

    /// <summary>The type of <paramref name="expression"/>, or Unknown when none was recorded.</summary>
    public Mql5ResolvedType TypeOf(Mql5IrExpression expression) =>
        expression is not null && ExpressionTypes.TryGetValue(expression, out Mql5ResolvedType? type)
            ? type
            : Mql5ResolvedType.Unknown;

    /// <summary>The symbol <paramref name="expression"/> refers to, or null when it names nothing.</summary>
    public Mql5ResolvedSymbol? SymbolOf(Mql5IrExpression expression) =>
        expression is not null && Symbols.TryGetValue(expression, out Mql5ResolvedSymbol? symbol)
            ? symbol
            : null;
}

/// <summary>
/// The result of binding one module.
///
/// <paramref name="Succeeded"/> means only that no diagnostic of error severity was
/// produced. It is never a claim that the module is executable or that its trading
/// behaviour was checked.
/// </summary>
public sealed record Mql5BindResult(
    bool Succeeded,
    Mql5SemanticModel Model,
    IReadOnlyList<Mql5RestrictedDiagnostic> Diagnostics)
{
    /// <summary>The number of error-severity diagnostics produced.</summary>
    public int ErrorCount =>
        Diagnostics.Count(diagnostic => diagnostic.Severity == Mql5RestrictedDiagnosticSeverity.Error);
}

/// <summary>Stable diagnostic codes emitted by <see cref="Mql5Binder"/>.</summary>
public static class Mql5BindDiagnosticCodes
{
    /// <summary>A name refers to nothing declared, imported or built in. Error.</summary>
    public const string UnresolvedName = "MQL5_BIND_UNRESOLVED_NAME";

    /// <summary>The callee of a call expression could not be resolved to anything callable. Error.</summary>
    public const string UnresolvedCall = "MQL5_BIND_UNRESOLVED_CALL";

    /// <summary>An argument count matched no overload of the resolved callee. Error.</summary>
    public const string ArityMismatch = "MQL5_BIND_ARITY_MISMATCH";

    /// <summary>A local name was used before its declaration statement. Error.</summary>
    public const string UseBeforeDeclaration = "MQL5_BIND_USE_BEFORE_DECLARATION";

    /// <summary>Two declarations of the same name collide in one scope. Error.</summary>
    public const string DuplicateDeclaration = "MQL5_BIND_DUPLICATE_DECLARATION";

    /// <summary>A written type name resolved to no declared or built-in type. Error.</summary>
    public const string UnknownType = "MQL5_BIND_UNKNOWN_TYPE";

    /// <summary>
    /// A known MQL5 built-in is referenced that this runtime does not implement.
    /// Information: the source is valid MQL5, we simply cannot execute it yet.
    /// </summary>
    public const string UnsupportedBuiltin = "MQL5_BIND_UNSUPPORTED_BUILTIN";

    /// <summary>
    /// Binding stopped early on a structure past the recursion or diagnostic budget.
    /// Information: the module is not wrong, the pass declined to go further.
    /// </summary>
    public const string BudgetExhausted = "MQL5_BIND_BUDGET_EXHAUSTED";
}
