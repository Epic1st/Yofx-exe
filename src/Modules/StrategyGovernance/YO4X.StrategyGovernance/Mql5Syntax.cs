namespace YO4X.StrategyGovernance;

/// <summary>
/// Abstract syntax tree for the MQL5 language front end.
///
/// This is a faithful syntactic model: it records what the source says, not what
/// it means. Semantic questions — whether a name resolves, whether a call is to a
/// supported built-in, whether a type conversion is legal — belong to a later
/// binding pass and are deliberately absent here.
/// </summary>
public abstract record Mql5Node(int Line, int Column);

// ---------------------------------------------------------------- expressions

public abstract record Mql5Expression(int Line, int Column) : Mql5Node(Line, Column);

/// <summary>
/// Literal forms. <c>Whole</c> is an integer literal, <c>Real</c> a floating-point
/// literal and <c>Text</c> a string literal; the names avoid CLR type names.
/// </summary>
public enum Mql5LiteralKind { Whole, Real, Text, Character, Boolean, Colour, DateTime, Null }

public sealed record Mql5LiteralExpression(
    Mql5LiteralKind Kind,
    string Text,
    int Line,
    int Column) : Mql5Expression(Line, Column);

public sealed record Mql5IdentifierExpression(
    string Name,
    int Line,
    int Column) : Mql5Expression(Line, Column);

/// <summary>A scope-qualified name such as <c>CTrade::Buy</c>.</summary>
public sealed record Mql5ScopeExpression(
    Mql5Expression? Qualifier,
    string Name,
    int Line,
    int Column) : Mql5Expression(Line, Column);

public sealed record Mql5UnaryExpression(
    string Operator,
    Mql5Expression Operand,
    bool IsPrefix,
    int Line,
    int Column) : Mql5Expression(Line, Column);

public sealed record Mql5BinaryExpression(
    string Operator,
    Mql5Expression Left,
    Mql5Expression Right,
    int Line,
    int Column) : Mql5Expression(Line, Column);

public sealed record Mql5AssignmentExpression(
    string Operator,
    Mql5Expression Target,
    Mql5Expression Value,
    int Line,
    int Column) : Mql5Expression(Line, Column);

public sealed record Mql5ConditionalExpression(
    Mql5Expression Condition,
    Mql5Expression WhenTrue,
    Mql5Expression WhenFalse,
    int Line,
    int Column) : Mql5Expression(Line, Column);

public sealed record Mql5CallExpression(
    Mql5Expression Callee,
    IReadOnlyList<Mql5Expression> Arguments,
    int Line,
    int Column) : Mql5Expression(Line, Column);

public sealed record Mql5IndexExpression(
    Mql5Expression Target,
    Mql5Expression Index,
    int Line,
    int Column) : Mql5Expression(Line, Column);

/// <summary>Member access through <c>.</c> or <c>-&gt;</c>.</summary>
public sealed record Mql5MemberExpression(
    Mql5Expression Target,
    string Member,
    bool ThroughPointer,
    int Line,
    int Column) : Mql5Expression(Line, Column);

public sealed record Mql5CastExpression(
    Mql5TypeReference Type,
    Mql5Expression Operand,
    int Line,
    int Column) : Mql5Expression(Line, Column);

public sealed record Mql5NewExpression(
    Mql5TypeReference Type,
    int Line,
    int Column) : Mql5Expression(Line, Column);

/// <summary>
/// The MQL5 <c>sizeof</c> operator, which measures a written type or a variable.
///
/// <paramref name="Type"/> always holds what was written between the parentheses,
/// because that is all the grammar establishes: a bare name is a type name and a
/// variable name in the same breath, and the parser cannot tell which. When the operand
/// could be a value — an undecorated bare name, with no array suffix, handle or
/// <c>const</c> — <see cref="Operand"/> additionally carries it as an expression, so a
/// later pass that does have a symbol table can measure the variable instead of looking
/// for a type of that name. It is null for every form only a type can take.
/// </summary>
public sealed record Mql5SizeOfExpression(
    Mql5TypeReference Type,
    int Line,
    int Column) : Mql5Expression(Line, Column)
{
    public Mql5Expression? Operand { get; init; }
}

/// <summary>
/// The MQL5 <c>typename</c> operator, which yields the name of a type as a string.
///
/// It takes either a written type or an expression, and exactly one of
/// <paramref name="Type"/> and <paramref name="Operand"/> is non-null. The two are kept
/// apart rather than collapsed into one because they are not the same question:
/// <c>typename(double)</c> names a type outright, while <c>typename(value)</c> asks for
/// the static type of an expression, which only a later pass can answer.
/// </summary>
public sealed record Mql5TypeNameExpression(
    Mql5TypeReference? Type,
    Mql5Expression? Operand,
    int Line,
    int Column) : Mql5Expression(Line, Column);

/// <summary>A brace initialiser such as <c>{1, 2, 3}</c>.</summary>
public sealed record Mql5InitializerListExpression(
    IReadOnlyList<Mql5Expression> Items,
    int Line,
    int Column) : Mql5Expression(Line, Column);

// ---------------------------------------------------------------------- types

/// <summary>
/// A written type. <paramref name="ArrayRanks"/> holds one entry per <c>[]</c>;
/// a null entry is an unsized dimension.
/// </summary>
public sealed record Mql5TypeReference(
    string Name,
    bool IsConst,
    bool IsPointer,
    bool IsReference,
    IReadOnlyList<Mql5Expression?> ArrayRanks,
    int Line,
    int Column) : Mql5Node(Line, Column);

// ----------------------------------------------------------------- statements

public abstract record Mql5Statement(int Line, int Column) : Mql5Node(Line, Column);

public sealed record Mql5BlockStatement(
    IReadOnlyList<Mql5Statement> Statements,
    int Line,
    int Column) : Mql5Statement(Line, Column);

public sealed record Mql5ExpressionStatement(
    Mql5Expression Expression,
    int Line,
    int Column) : Mql5Statement(Line, Column);

public sealed record Mql5EmptyStatement(int Line, int Column) : Mql5Statement(Line, Column);

public sealed record Mql5VariableDeclarator(
    string Name,
    IReadOnlyList<Mql5Expression?> ArrayRanks,
    Mql5Expression? Initializer,
    int Line,
    int Column) : Mql5Node(Line, Column);

public sealed record Mql5VariableDeclarationStatement(
    Mql5TypeReference Type,
    bool IsStatic,
    bool IsConst,
    IReadOnlyList<Mql5VariableDeclarator> Declarators,
    int Line,
    int Column) : Mql5Statement(Line, Column);

public sealed record Mql5IfStatement(
    Mql5Expression Condition,
    Mql5Statement WhenTrue,
    Mql5Statement? WhenFalse,
    int Line,
    int Column) : Mql5Statement(Line, Column);

public sealed record Mql5WhileStatement(
    Mql5Expression Condition,
    Mql5Statement Body,
    int Line,
    int Column) : Mql5Statement(Line, Column);

public sealed record Mql5DoWhileStatement(
    Mql5Statement Body,
    Mql5Expression Condition,
    int Line,
    int Column) : Mql5Statement(Line, Column);

public sealed record Mql5ForStatement(
    Mql5Statement? Initializer,
    Mql5Expression? Condition,
    Mql5Expression? Increment,
    Mql5Statement Body,
    int Line,
    int Column) : Mql5Statement(Line, Column);

public sealed record Mql5SwitchSection(
    IReadOnlyList<Mql5Expression?> Labels,
    IReadOnlyList<Mql5Statement> Statements,
    int Line,
    int Column) : Mql5Node(Line, Column);

public sealed record Mql5SwitchStatement(
    Mql5Expression Subject,
    IReadOnlyList<Mql5SwitchSection> Sections,
    int Line,
    int Column) : Mql5Statement(Line, Column);

public sealed record Mql5ReturnStatement(
    Mql5Expression? Value,
    int Line,
    int Column) : Mql5Statement(Line, Column);

public sealed record Mql5BreakStatement(int Line, int Column) : Mql5Statement(Line, Column);

public sealed record Mql5ContinueStatement(int Line, int Column) : Mql5Statement(Line, Column);

public sealed record Mql5DeleteStatement(
    Mql5Expression Operand,
    int Line,
    int Column) : Mql5Statement(Line, Column);

// --------------------------------------------------------------- declarations

public abstract record Mql5Declaration(int Line, int Column) : Mql5Node(Line, Column);

public sealed record Mql5PropertyDirective(
    string Name,
    string? Value,
    int Line,
    int Column) : Mql5Declaration(Line, Column);

public sealed record Mql5IncludeDirective(
    string Path,
    bool IsSystemPath,
    int Line,
    int Column) : Mql5Declaration(Line, Column);

public sealed record Mql5DefineDirective(
    string Name,
    string Replacement,
    int Line,
    int Column) : Mql5Declaration(Line, Column);

public sealed record Mql5ImportDirective(
    string Library,
    IReadOnlyList<Mql5FunctionDeclaration> Functions,
    int Line,
    int Column) : Mql5Declaration(Line, Column);

public enum Mql5InputKind { None, Input, StaticInput, Extern }

/// <summary>
/// A file-scope variable declaration, including the <c>input</c>, <c>sinput</c> and
/// <c>extern</c> forms that MetaTrader surfaces in the strategy properties dialog.
/// </summary>
/// <param name="Label">
/// The declaration's trailing same-line comment, comment markers stripped and whitespace
/// normalised — the caption MetaTrader shows for the field. Null when the declaration
/// carries no trailing comment; a label is never inferred from the declared name.
/// </param>
/// <param name="GroupLabel">
/// The caption of the most recent preceding <c>input group "…"</c> marker, or null while
/// no group is in effect. Declaration order is source order, so groups apply to every
/// following declaration until the next marker.
/// </param>
public sealed record Mql5GlobalVariableDeclaration(
    Mql5TypeReference Type,
    Mql5InputKind InputKind,
    bool IsStatic,
    bool IsConst,
    IReadOnlyList<Mql5VariableDeclarator> Declarators,
    int Line,
    int Column,
    string? Label = null,
    string? GroupLabel = null) : Mql5Declaration(Line, Column);

public sealed record Mql5Parameter(
    Mql5TypeReference Type,
    string Name,
    Mql5Expression? DefaultValue,
    int Line,
    int Column) : Mql5Node(Line, Column);

/// <param name="IsAbstract">
/// True when the declaration carries MQL5's pure specifier, <c>= 0</c>. It is recorded
/// rather than dropped because it is not the same as a missing body: MetaEditor answers
/// an attempt to instantiate the enclosing class with <c>error 383: cannot instantiate
/// abstract class</c>, so the member is declared to have no definition anywhere, not
/// merely to have its definition elsewhere.
/// </param>
public sealed record Mql5FunctionDeclaration(
    Mql5TypeReference ReturnType,
    string Name,
    IReadOnlyList<Mql5Parameter> Parameters,
    Mql5BlockStatement? Body,
    bool IsStatic,
    bool IsVirtual,
    bool IsAbstract,
    bool IsConst,
    int Line,
    int Column) : Mql5Declaration(Line, Column);

public sealed record Mql5EnumMemberDeclaration(
    string Name,
    Mql5Expression? Value,
    int Line,
    int Column) : Mql5Node(Line, Column);

public sealed record Mql5EnumDeclaration(
    string Name,
    IReadOnlyList<Mql5EnumMemberDeclaration> Members,
    int Line,
    int Column) : Mql5Declaration(Line, Column);

public enum Mql5Access { Public, Protected, Private }

public sealed record Mql5TypeMember(
    Mql5Access Access,
    Mql5Declaration Declaration,
    int Line,
    int Column) : Mql5Node(Line, Column);

public sealed record Mql5TypeDeclaration(
    string Keyword,
    string Name,
    string? BaseTypeName,
    IReadOnlyList<Mql5TypeMember> Members,
    int Line,
    int Column) : Mql5Declaration(Line, Column);

public sealed record Mql5TemplateDeclaration(
    IReadOnlyList<string> TypeParameters,
    Mql5Declaration Declaration,
    int Line,
    int Column) : Mql5Declaration(Line, Column);

/// <summary>One parsed translation unit.</summary>
public sealed record Mql5CompilationUnit(
    string RelativePath,
    string SourceSha256,
    IReadOnlyList<Mql5Declaration> Declarations);
