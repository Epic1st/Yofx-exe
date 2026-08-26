using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace YO4X.StrategyGovernance;

/// <summary>
/// Intermediate representation v2 for the MQL5 front end.
///
/// Where restricted IR v1 could only describe data (structures, enumerations and
/// input constants) and therefore could never carry a strategy, v2 describes
/// executable code: declarations, statements and expressions.
///
/// The representation stays deliberately structural. Names are not resolved,
/// overloads are not selected, types are not checked and implicit conversions are
/// not inserted; those are binding concerns handled by a later pass. What v2
/// guarantees is that every construct present in the source is present in the IR,
/// that every node carries its source position, and that the whole module
/// serialises to byte-stable canonical JSON with a reproducible SHA-256 digest.
/// </summary>
public abstract record Mql5IrNode(int Line, int Column)
{
    /// <summary>Stable discriminator emitted into canonical JSON.</summary>
    public abstract string Kind { get; }
}

// ---------------------------------------------------------------------- types

/// <summary>
/// Classification of the MQL5 built-in scalar set. Member names avoid CLR type
/// names on purpose: <c>Whole</c> is a signed integer, <c>Natural</c> an unsigned
/// integer, <c>Real</c> a floating-point value and <c>Text</c> a string.
/// </summary>
public enum Mql5IrScalarKind
{
    /// <summary>Not a built-in scalar: an enumeration, structure, class or unresolved name.</summary>
    None = 0,
    Void,
    Logical,
    Whole8,
    Natural8,
    Whole16,
    Natural16,
    Whole32,
    Natural32,
    Whole64,
    Natural64,
    Real32,
    Real64,
    Text,
    Moment,
    Colour
}

/// <summary>
/// One <c>[]</c> of an array declarator. <paramref name="Size"/> is null for an
/// unsized dimension; <paramref name="FoldedSize"/> is populated only when the
/// size is a trivially foldable constant.
/// </summary>
public sealed record Mql5IrArrayRank(Mql5IrExpression? Size, long? FoldedSize);

/// <summary>A written type as it appears in source: normalised, but not resolved.</summary>
public sealed record Mql5IrTypeReference(
    string Name,
    Mql5IrScalarKind Scalar,
    bool IsConst,
    bool IsPointer,
    bool IsReference,
    IReadOnlyList<Mql5IrArrayRank> ArrayRanks,
    int Line,
    int Column) : Mql5IrNode(Line, Column)
{
    public override string Kind => "type";
}

// ---------------------------------------------------------------- expressions

public abstract record Mql5IrExpression(int Line, int Column) : Mql5IrNode(Line, Column);

/// <summary>
/// A literal. <paramref name="Text"/> is the lexeme as written,
/// <paramref name="CanonicalText"/> its normalised form, and
/// <paramref name="FoldedValue"/> is set only for integer literals that parse
/// trivially into <see cref="long"/>.
/// </summary>
public sealed record Mql5IrLiteralExpression(
    Mql5LiteralKind LiteralKind,
    string Text,
    string CanonicalText,
    long? FoldedValue,
    int Line,
    int Column) : Mql5IrExpression(Line, Column)
{
    public override string Kind => "literal";
}

/// <summary>
/// A referenced name. <paramref name="Scope"/> holds the qualifier chain of a
/// scope-qualified name such as <c>CTrade::Buy</c>; <paramref name="IsScopeQualified"/>
/// is true even when the chain is empty, which denotes global scope (<c>::Name</c>).
/// </summary>
public sealed record Mql5IrNameExpression(
    IReadOnlyList<string> Scope,
    bool IsScopeQualified,
    string Name,
    int Line,
    int Column) : Mql5IrExpression(Line, Column)
{
    public override string Kind => "name";
}

public sealed record Mql5IrUnaryExpression(
    string Operator,
    bool IsPrefix,
    Mql5IrExpression Operand,
    long? FoldedValue,
    int Line,
    int Column) : Mql5IrExpression(Line, Column)
{
    public override string Kind => "unary";
}

public sealed record Mql5IrBinaryExpression(
    string Operator,
    Mql5IrExpression Left,
    Mql5IrExpression Right,
    int Line,
    int Column) : Mql5IrExpression(Line, Column)
{
    public override string Kind => "binary";
}

public sealed record Mql5IrAssignmentExpression(
    string Operator,
    Mql5IrExpression Target,
    Mql5IrExpression Value,
    int Line,
    int Column) : Mql5IrExpression(Line, Column)
{
    public override string Kind => "assign";
}

public sealed record Mql5IrConditionalExpression(
    Mql5IrExpression Condition,
    Mql5IrExpression WhenTrue,
    Mql5IrExpression WhenFalse,
    int Line,
    int Column) : Mql5IrExpression(Line, Column)
{
    public override string Kind => "conditional";
}

public sealed record Mql5IrCallExpression(
    Mql5IrExpression Callee,
    IReadOnlyList<Mql5IrExpression> Arguments,
    int Line,
    int Column) : Mql5IrExpression(Line, Column)
{
    public override string Kind => "call";
}

public sealed record Mql5IrIndexExpression(
    Mql5IrExpression Target,
    Mql5IrExpression Index,
    int Line,
    int Column) : Mql5IrExpression(Line, Column)
{
    public override string Kind => "index";
}

public sealed record Mql5IrMemberExpression(
    Mql5IrExpression Target,
    string Member,
    bool ThroughPointer,
    int Line,
    int Column) : Mql5IrExpression(Line, Column)
{
    public override string Kind => "member";
}

public sealed record Mql5IrCastExpression(
    Mql5IrTypeReference Type,
    Mql5IrExpression Operand,
    int Line,
    int Column) : Mql5IrExpression(Line, Column)
{
    public override string Kind => "cast";
}

public sealed record Mql5IrNewExpression(
    Mql5IrTypeReference Type,
    int Line,
    int Column) : Mql5IrExpression(Line, Column)
{
    public override string Kind => "new";
}

/// <summary>
/// The MQL5 <c>sizeof</c> operator.
///
/// <paramref name="Type"/> is what the source wrote between the parentheses, always.
/// <see cref="Operand"/> is non-null only when that text could equally be a value — an
/// undecorated bare name — and then carries the same name as an expression, because MQL5
/// measures a variable as readily as a type and the grammar cannot separate the two. A
/// back end that has resolved names should prefer <see cref="Operand"/> when its type
/// resolves, and fall back to <paramref name="Type"/> otherwise; one that has not can
/// keep reading <paramref name="Type"/> alone and lose nothing it had before.
/// </summary>
public sealed record Mql5IrSizeOfExpression(
    Mql5IrTypeReference Type,
    int Line,
    int Column) : Mql5IrExpression(Line, Column)
{
    public override string Kind => "sizeof";

    public Mql5IrExpression? Operand { get; init; }
}

/// <summary>
/// The MQL5 <c>typename</c> operator. Exactly one of <paramref name="Type"/> and
/// <paramref name="Operand"/> is non-null: the operator accepts a written type or an
/// expression, and which one was written decides what a back end has to do.
///
/// MQL5 folds this to a constant, per template instantiation — MetaEditor rejects
/// <c>typename(T) == "double"</c> as an array size when the instantiation is
/// <c>[T=string]</c> and accepts it when it is <c>[T=double]</c>. A back end that does
/// not monomorphise cannot reproduce that folding and has to ask at run time instead.
/// </summary>
public sealed record Mql5IrTypeNameExpression(
    Mql5IrTypeReference? Type,
    Mql5IrExpression? Operand,
    int Line,
    int Column) : Mql5IrExpression(Line, Column)
{
    public override string Kind => "typename";
}

public sealed record Mql5IrInitializerListExpression(
    IReadOnlyList<Mql5IrExpression> Items,
    int Line,
    int Column) : Mql5IrExpression(Line, Column)
{
    public override string Kind => "initializer";
}

// ----------------------------------------------------------------- statements

public abstract record Mql5IrStatement(int Line, int Column) : Mql5IrNode(Line, Column);

public sealed record Mql5IrBlockStatement(
    IReadOnlyList<Mql5IrStatement> Statements,
    int Line,
    int Column) : Mql5IrStatement(Line, Column)
{
    public override string Kind => "block";
}

/// <summary>One declared variable: a local, a global, an input or a field.</summary>
public sealed record Mql5IrVariable(
    string Name,
    IReadOnlyList<Mql5IrArrayRank> ArrayRanks,
    Mql5IrExpression? Initializer,
    int Line,
    int Column) : Mql5IrNode(Line, Column)
{
    public override string Kind => "variable";
}

public sealed record Mql5IrLocalDeclarationStatement(
    Mql5IrTypeReference Type,
    bool IsStatic,
    bool IsConst,
    IReadOnlyList<Mql5IrVariable> Variables,
    int Line,
    int Column) : Mql5IrStatement(Line, Column)
{
    public override string Kind => "localDeclaration";
}

public sealed record Mql5IrExpressionStatement(
    Mql5IrExpression Expression,
    int Line,
    int Column) : Mql5IrStatement(Line, Column)
{
    public override string Kind => "expression";
}

/// <summary>A lone <c>;</c>, kept so that no written construct is dropped.</summary>
public sealed record Mql5IrEmptyStatement(int Line, int Column) : Mql5IrStatement(Line, Column)
{
    public override string Kind => "empty";
}

public sealed record Mql5IrIfStatement(
    Mql5IrExpression Condition,
    Mql5IrStatement WhenTrue,
    Mql5IrStatement? WhenFalse,
    int Line,
    int Column) : Mql5IrStatement(Line, Column)
{
    public override string Kind => "if";
}

public sealed record Mql5IrWhileStatement(
    Mql5IrExpression Condition,
    Mql5IrStatement Body,
    int Line,
    int Column) : Mql5IrStatement(Line, Column)
{
    public override string Kind => "while";
}

public sealed record Mql5IrDoWhileStatement(
    Mql5IrStatement Body,
    Mql5IrExpression Condition,
    int Line,
    int Column) : Mql5IrStatement(Line, Column)
{
    public override string Kind => "doWhile";
}

public sealed record Mql5IrForStatement(
    Mql5IrStatement? Initializer,
    Mql5IrExpression? Condition,
    Mql5IrExpression? Increment,
    Mql5IrStatement Body,
    int Line,
    int Column) : Mql5IrStatement(Line, Column)
{
    public override string Kind => "for";
}

/// <summary>
/// One <c>case</c> or <c>default</c> label. <paramref name="Value"/> is null when
/// <paramref name="IsDefault"/> is true.
/// </summary>
public sealed record Mql5IrSwitchLabel(Mql5IrExpression? Value, bool IsDefault);

public sealed record Mql5IrSwitchSection(
    IReadOnlyList<Mql5IrSwitchLabel> Labels,
    IReadOnlyList<Mql5IrStatement> Statements,
    int Line,
    int Column) : Mql5IrNode(Line, Column)
{
    public override string Kind => "switchSection";
}

public sealed record Mql5IrSwitchStatement(
    Mql5IrExpression Subject,
    IReadOnlyList<Mql5IrSwitchSection> Sections,
    int Line,
    int Column) : Mql5IrStatement(Line, Column)
{
    public override string Kind => "switch";
}

public sealed record Mql5IrReturnStatement(
    Mql5IrExpression? Value,
    int Line,
    int Column) : Mql5IrStatement(Line, Column)
{
    public override string Kind => "return";
}

public sealed record Mql5IrBreakStatement(int Line, int Column) : Mql5IrStatement(Line, Column)
{
    public override string Kind => "break";
}

public sealed record Mql5IrContinueStatement(int Line, int Column) : Mql5IrStatement(Line, Column)
{
    public override string Kind => "continue";
}

public sealed record Mql5IrDeleteStatement(
    Mql5IrExpression Operand,
    int Line,
    int Column) : Mql5IrStatement(Line, Column)
{
    public override string Kind => "delete";
}

// --------------------------------------------------------------- declarations

public sealed record Mql5IrProperty(
    string Name,
    string? Value,
    int Line,
    int Column) : Mql5IrNode(Line, Column)
{
    public override string Kind => "property";
}

public sealed record Mql5IrInclude(
    string Path,
    bool IsSystemPath,
    int Line,
    int Column) : Mql5IrNode(Line, Column)
{
    public override string Kind => "include";
}

public sealed record Mql5IrDefine(
    string Name,
    string Replacement,
    int Line,
    int Column) : Mql5IrNode(Line, Column)
{
    public override string Kind => "define";
}

/// <summary>
/// An <c>#import</c> block. Imported prototypes are not lowered by the current
/// pass; <c>Mql5Lowering</c> reports them explicitly rather than dropping them.
/// </summary>
public sealed record Mql5IrImport(
    string Library,
    IReadOnlyList<Mql5IrFunction> Functions,
    int Line,
    int Column) : Mql5IrNode(Line, Column)
{
    public override string Kind => "import";
}

public sealed record Mql5IrEnumMember(
    string Name,
    Mql5IrExpression? Value,
    long? FoldedValue,
    int Line,
    int Column) : Mql5IrNode(Line, Column)
{
    public override string Kind => "enumMember";
}

public sealed record Mql5IrEnumeration(
    string Name,
    IReadOnlyList<Mql5IrEnumMember> Members,
    int Line,
    int Column) : Mql5IrNode(Line, Column)
{
    public override string Kind => "enum";
}

public sealed record Mql5IrField(
    Mql5Access Access,
    Mql5IrTypeReference Type,
    string Name,
    IReadOnlyList<Mql5IrArrayRank> ArrayRanks,
    Mql5IrExpression? Initializer,
    bool IsStatic,
    bool IsConst,
    int Line,
    int Column) : Mql5IrNode(Line, Column)
{
    public override string Kind => "field";
}

public sealed record Mql5IrParameter(
    Mql5IrTypeReference Type,
    string Name,
    Mql5IrExpression? DefaultValue,
    int Line,
    int Column) : Mql5IrNode(Line, Column)
{
    public override string Kind => "parameter";
}

/// <summary>
/// A function or method. <paramref name="Body"/> is null for a prototype, which is
/// recorded rather than dropped so that a later binding pass can match it to a
/// definition.
///
/// <paramref name="IsAbstract"/> separates the two reasons a body can be missing. A
/// prototype has its definition somewhere; an abstract member — MQL5's <c>= 0</c> pure
/// specifier — has none anywhere, and MetaEditor refuses to instantiate the class that
/// declares it. A back end has to emit different code for each, so the IR keeps them apart.
/// </summary>
/// <param name="TypeParameters">
/// The names introduced by an enclosing <c>template&lt;typename …&gt;</c>, in source
/// order; empty for an ordinary function. The names are carried rather than
/// substituted because monomorphisation would need every instantiation to be visible
/// in one translation unit, which MQL5 does not guarantee: a template is written once
/// and instantiated wherever the compiler infers an argument. Keeping the parameter
/// list means the declaration survives lowering intact and a back end is free to emit
/// it as a generic or to specialise it later.
/// </param>
public sealed record Mql5IrFunction(
    Mql5IrTypeReference ReturnType,
    string Name,
    IReadOnlyList<string> TypeParameters,
    IReadOnlyList<Mql5IrParameter> Parameters,
    Mql5IrBlockStatement? Body,
    bool IsStatic,
    bool IsVirtual,
    bool IsAbstract,
    bool IsConst,
    Mql5Access Access,
    int Line,
    int Column) : Mql5IrNode(Line, Column)
{
    public override string Kind => "function";
}

/// <param name="TypeParameters">
/// The names introduced by an enclosing <c>template&lt;typename …&gt;</c>, in source
/// order; empty for an ordinary structure, class or interface. A use site keeps its
/// arguments in the written type name — <c>Block0 : public MDL_Condition&lt;double,int&gt;</c>
/// lowers with <paramref name="BaseTypeName"/> spelled exactly that way — so the
/// declaration side only has to record what the arguments bind to.
/// </param>
public sealed record Mql5IrTypeDeclaration(
    string Keyword,
    string Name,
    IReadOnlyList<string> TypeParameters,
    string? BaseTypeName,
    IReadOnlyList<Mql5IrField> Fields,
    IReadOnlyList<Mql5IrFunction> Methods,
    IReadOnlyList<Mql5IrEnumeration> NestedEnums,
    IReadOnlyList<Mql5IrTypeDeclaration> NestedTypes,
    int Line,
    int Column) : Mql5IrNode(Line, Column)
{
    public override string Kind => "typeDeclaration";
}

public sealed record Mql5IrGlobalVariable(
    Mql5IrTypeReference Type,
    string Name,
    IReadOnlyList<Mql5IrArrayRank> ArrayRanks,
    Mql5IrExpression? Initializer,
    bool IsStatic,
    bool IsConst,
    int Line,
    int Column) : Mql5IrNode(Line, Column)
{
    public override string Kind => "global";
}

/// <summary>
/// An <c>input</c>, <c>sinput</c> or <c>extern</c> declaration.
/// <paramref name="CanonicalDefault"/> is the normalised default when the
/// initialiser is a literal, a signed numeric literal or a symbolic name; it is
/// null when the default is absent or is a compound expression, in which case
/// <paramref name="DefaultValue"/> still carries the full expression tree.
///
/// <paramref name="Label"/> is the caption MetaTrader shows for the field — the
/// declaration's trailing comment — and <paramref name="GroupLabel"/> the section it
/// is shown under. Both are null when the source states no such text; neither is ever
/// derived from <paramref name="Name"/>, so a consumer that wants a fallback caption
/// must apply <c>Label ?? Name</c> itself.
/// </summary>
public sealed record Mql5IrInput(
    Mql5InputKind InputKind,
    Mql5IrTypeReference Type,
    string Name,
    IReadOnlyList<Mql5IrArrayRank> ArrayRanks,
    Mql5IrExpression? DefaultValue,
    string? CanonicalDefault,
    bool IsConst,
    int Line,
    int Column,
    string? Label = null,
    string? GroupLabel = null) : Mql5IrNode(Line, Column)
{
    public override string Kind => "input";
}

// --------------------------------------------------------------------- module

/// <summary>
/// A complete lowered translation unit and the root of canonical serialisation.
/// </summary>
public sealed record Mql5IrV2Module(
    string SchemaVersion,
    string SourcePath,
    string SourceSha256,
    string IrSha256,
    IReadOnlyList<Mql5IrProperty> Properties,
    IReadOnlyList<Mql5IrInclude> Includes,
    IReadOnlyList<Mql5IrDefine> Defines,
    IReadOnlyList<Mql5IrImport> Imports,
    IReadOnlyList<Mql5IrEnumeration> Enums,
    IReadOnlyList<Mql5IrTypeDeclaration> Types,
    IReadOnlyList<Mql5IrGlobalVariable> Globals,
    IReadOnlyList<Mql5IrInput> Inputs,
    IReadOnlyList<Mql5IrFunction> Functions)
{
    /// <summary>Schema identifier written into every canonical document.</summary>
    public const string CurrentSchemaVersion = "yo4x.mql5.ir.v2";

    /// <summary>Identifier of the lowering pass that produces this schema.</summary>
    public const string LoweringVersion = "yo4x-mql5-lowering.v2";

    /// <summary>
    /// Builds a module and computes its IR digest. The digest is the SHA-256 of the
    /// UTF-8 canonical document serialised without the digest field itself, so it is
    /// reproducible from the module alone.
    /// </summary>
    public static Mql5IrV2Module Create(
        string? sourcePath,
        string? sourceSha256,
        IReadOnlyList<Mql5IrProperty> properties,
        IReadOnlyList<Mql5IrInclude> includes,
        IReadOnlyList<Mql5IrDefine> defines,
        IReadOnlyList<Mql5IrImport> imports,
        IReadOnlyList<Mql5IrEnumeration> enums,
        IReadOnlyList<Mql5IrTypeDeclaration> types,
        IReadOnlyList<Mql5IrGlobalVariable> globals,
        IReadOnlyList<Mql5IrInput> inputs,
        IReadOnlyList<Mql5IrFunction> functions)
    {
        var seed = new Mql5IrV2Module(
            CurrentSchemaVersion,
            NormalizeSourcePath(sourcePath),
            sourceSha256 ?? string.Empty,
            string.Empty,
            properties ?? [],
            includes ?? [],
            defines ?? [],
            imports ?? [],
            enums ?? [],
            types ?? [],
            globals ?? [],
            inputs ?? [],
            functions ?? []);
        byte[] payload = Encoding.UTF8.GetBytes(seed.Serialize(false));
        return seed with { IrSha256 = Convert.ToHexStringLower(SHA256.HashData(payload)) };
    }

    /// <summary>
    /// Produces the byte-stable canonical JSON document for this module.
    ///
    /// Determinism is structural: every property is written by hand in a fixed
    /// order, no dictionary or reflection ordering is involved, every number goes
    /// through an integer <see cref="Utf8JsonWriter"/> overload, every text value
    /// through a pinned encoder, the source path is normalised to a
    /// separator-independent relative form, and nothing machine- or time-dependent
    /// is ever emitted.
    /// </summary>
    public string ToCanonicalJson() => Serialize(true);

    /// <summary>
    /// Normalises a source path so that the digest cannot depend on the machine:
    /// separators become <c>/</c>, and any drive prefix, leading root or leading
    /// <c>./</c> segment is removed.
    /// </summary>
    public static string NormalizeSourcePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string normalized = path.Trim().Replace('\\', '/');
        if (normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':')
        {
            normalized = normalized[2..];
        }

        normalized = normalized.TrimStart('/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..].TrimStart('/');
        }

        return normalized;
    }

    private string Serialize(bool includeDigest)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = false,
            Encoder = JavaScriptEncoder.Default
        }))
        {
            writer.WriteStartObject();
            writer.WriteString("schemaVersion", SchemaVersion ?? CurrentSchemaVersion);
            writer.WriteString("loweringVersion", LoweringVersion);
            writer.WriteString("sourcePath", SourcePath ?? string.Empty);
            writer.WriteString("sourceSha256", SourceSha256 ?? string.Empty);
            if (includeDigest)
            {
                writer.WriteString("irSha256", IrSha256 ?? string.Empty);
            }

            writer.WriteStartArray("properties");
            foreach (Mql5IrProperty item in Properties ?? [])
            {
                Mql5IrCanonicalWriter.WriteProperty(writer, item);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("includes");
            foreach (Mql5IrInclude item in Includes ?? [])
            {
                Mql5IrCanonicalWriter.WriteInclude(writer, item);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("defines");
            foreach (Mql5IrDefine item in Defines ?? [])
            {
                Mql5IrCanonicalWriter.WriteDefine(writer, item);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("imports");
            foreach (Mql5IrImport item in Imports ?? [])
            {
                Mql5IrCanonicalWriter.WriteImport(writer, item);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("enums");
            foreach (Mql5IrEnumeration item in Enums ?? [])
            {
                Mql5IrCanonicalWriter.WriteEnum(writer, item);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("types");
            foreach (Mql5IrTypeDeclaration item in Types ?? [])
            {
                Mql5IrCanonicalWriter.WriteTypeDeclaration(writer, item);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("globals");
            foreach (Mql5IrGlobalVariable item in Globals ?? [])
            {
                Mql5IrCanonicalWriter.WriteGlobal(writer, item);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("inputs");
            foreach (Mql5IrInput item in Inputs ?? [])
            {
                Mql5IrCanonicalWriter.WriteInput(writer, item);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("functions");
            foreach (Mql5IrFunction item in Functions ?? [])
            {
                Mql5IrCanonicalWriter.WriteFunction(writer, item);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}

/// <summary>
/// Literal and vocabulary normalisation shared by lowering and canonical
/// serialisation. Only trivial forms are folded; general constant evaluation is a
/// binder concern and is deliberately not attempted here.
/// </summary>
public static class Mql5IrLiteral
{
    /// <summary>
    /// Parses a decimal or hexadecimal MQL5 integer literal, tolerating the
    /// <c>u</c>, <c>U</c>, <c>l</c> and <c>L</c> suffixes and a leading sign.
    /// Returns null when the lexeme is not trivially a <see cref="long"/>.
    /// </summary>
    public static long? TryFoldWhole(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string candidate = text.Trim();
        while (candidate.Length > 0 && candidate[^1] is 'u' or 'U' or 'l' or 'L')
        {
            candidate = candidate[..^1];
        }

        bool negative = false;
        if (candidate.Length > 0 && candidate[0] is '+' or '-')
        {
            negative = candidate[0] == '-';
            candidate = candidate[1..];
        }

        if (candidate.Length == 0)
        {
            return null;
        }

        long magnitude;
        if (candidate.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            string digits = candidate[2..];
            if (digits.Length is 0 or > 16
                || !long.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out magnitude))
            {
                return null;
            }
        }
        else if (!long.TryParse(candidate, NumberStyles.None, CultureInfo.InvariantCulture, out magnitude))
        {
            return null;
        }

        if (!negative)
        {
            return magnitude;
        }

        return magnitude == long.MinValue ? null : -magnitude;
    }

    /// <summary>Normalises a literal lexeme into a stable canonical text form.</summary>
    public static string Canonicalize(Mql5LiteralKind kind, string? text)
    {
        string lexeme = text ?? string.Empty;
        switch (kind)
        {
            case Mql5LiteralKind.Whole:
                long? whole = TryFoldWhole(lexeme);
                return whole is null ? lexeme : whole.Value.ToString(CultureInfo.InvariantCulture);
            case Mql5LiteralKind.Real:
                return double.TryParse(lexeme, NumberStyles.Float, CultureInfo.InvariantCulture, out double real)
                    && double.IsFinite(real)
                        ? real.ToString("R", CultureInfo.InvariantCulture)
                        : lexeme;
            case Mql5LiteralKind.Boolean:
                if (string.Equals(lexeme, "true", StringComparison.OrdinalIgnoreCase))
                {
                    return "true";
                }

                return string.Equals(lexeme, "false", StringComparison.OrdinalIgnoreCase) ? "false" : lexeme;
            case Mql5LiteralKind.Null:
                return "NULL";
            default:
                // Text, Character, Colour and DateTime lexemes are kept verbatim:
                // unescaping and calendar interpretation belong to the binder.
                return lexeme;
        }
    }

    /// <summary>Stable canonical token for a literal kind.</summary>
    public static string KindToken(Mql5LiteralKind kind) => kind switch
    {
        Mql5LiteralKind.Whole => "whole",
        Mql5LiteralKind.Real => "real",
        Mql5LiteralKind.Text => "text",
        Mql5LiteralKind.Character => "character",
        Mql5LiteralKind.Boolean => "logical",
        Mql5LiteralKind.Colour => "colour",
        Mql5LiteralKind.DateTime => "moment",
        Mql5LiteralKind.Null => "null",
        _ => "unknown"
    };

    /// <summary>Stable canonical token for a scalar classification.</summary>
    public static string ScalarToken(Mql5IrScalarKind scalar) => scalar switch
    {
        Mql5IrScalarKind.Void => "void",
        Mql5IrScalarKind.Logical => "logical",
        Mql5IrScalarKind.Whole8 => "whole8",
        Mql5IrScalarKind.Natural8 => "natural8",
        Mql5IrScalarKind.Whole16 => "whole16",
        Mql5IrScalarKind.Natural16 => "natural16",
        Mql5IrScalarKind.Whole32 => "whole32",
        Mql5IrScalarKind.Natural32 => "natural32",
        Mql5IrScalarKind.Whole64 => "whole64",
        Mql5IrScalarKind.Natural64 => "natural64",
        Mql5IrScalarKind.Real32 => "real32",
        Mql5IrScalarKind.Real64 => "real64",
        Mql5IrScalarKind.Text => "text",
        Mql5IrScalarKind.Moment => "moment",
        Mql5IrScalarKind.Colour => "colour",
        _ => "none"
    };

    /// <summary>Stable canonical token for a member access level.</summary>
    public static string AccessToken(Mql5Access access) => access switch
    {
        Mql5Access.Protected => "protected",
        Mql5Access.Private => "private",
        _ => "public"
    };

    /// <summary>Stable canonical token for an input storage class.</summary>
    public static string InputToken(Mql5InputKind kind) => kind switch
    {
        Mql5InputKind.Input => "input",
        Mql5InputKind.StaticInput => "sinput",
        Mql5InputKind.Extern => "extern",
        _ => "none"
    };

    /// <summary>Maps a written MQL5 type name onto the built-in scalar set.</summary>
    public static Mql5IrScalarKind ClassifyScalar(string? name) => name switch
    {
        "void" => Mql5IrScalarKind.Void,
        "bool" => Mql5IrScalarKind.Logical,
        "char" => Mql5IrScalarKind.Whole8,
        "uchar" => Mql5IrScalarKind.Natural8,
        "short" => Mql5IrScalarKind.Whole16,
        "ushort" => Mql5IrScalarKind.Natural16,
        "int" => Mql5IrScalarKind.Whole32,
        "uint" => Mql5IrScalarKind.Natural32,
        "long" => Mql5IrScalarKind.Whole64,
        "ulong" => Mql5IrScalarKind.Natural64,
        "float" => Mql5IrScalarKind.Real32,
        "double" => Mql5IrScalarKind.Real64,
        "string" => Mql5IrScalarKind.Text,
        "datetime" => Mql5IrScalarKind.Moment,
        "color" => Mql5IrScalarKind.Colour,
        _ => Mql5IrScalarKind.None
    };
}

/// <summary>
/// Hand-written canonical serialiser. Every object writes its discriminator, then
/// its source position, then its payload, always in the same order, so the encoded
/// bytes depend only on the IR contents.
/// </summary>
internal static class Mql5IrCanonicalWriter
{
    internal static void WriteProperty(Utf8JsonWriter writer, Mql5IrProperty? node)
    {
        if (node is null)
        {
            WriteAbsent(writer);
            return;
        }

        WriteHeader(writer, node);
        writer.WriteString("name", node.Name ?? string.Empty);
        WriteTextProperty(writer, "value", node.Value);
        writer.WriteEndObject();
    }

    internal static void WriteInclude(Utf8JsonWriter writer, Mql5IrInclude? node)
    {
        if (node is null)
        {
            WriteAbsent(writer);
            return;
        }

        WriteHeader(writer, node);
        writer.WriteString("path", node.Path ?? string.Empty);
        writer.WriteBoolean("system", node.IsSystemPath);
        writer.WriteEndObject();
    }

    internal static void WriteDefine(Utf8JsonWriter writer, Mql5IrDefine? node)
    {
        if (node is null)
        {
            WriteAbsent(writer);
            return;
        }

        WriteHeader(writer, node);
        writer.WriteString("name", node.Name ?? string.Empty);
        writer.WriteString("replacement", node.Replacement ?? string.Empty);
        writer.WriteEndObject();
    }

    internal static void WriteImport(Utf8JsonWriter writer, Mql5IrImport? node)
    {
        if (node is null)
        {
            WriteAbsent(writer);
            return;
        }

        WriteHeader(writer, node);
        writer.WriteString("library", node.Library ?? string.Empty);
        writer.WriteStartArray("functions");
        foreach (Mql5IrFunction function in node.Functions ?? [])
        {
            WriteFunction(writer, function);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    internal static void WriteEnum(Utf8JsonWriter writer, Mql5IrEnumeration? node)
    {
        if (node is null)
        {
            WriteAbsent(writer);
            return;
        }

        WriteHeader(writer, node);
        writer.WriteString("name", node.Name ?? string.Empty);
        writer.WriteStartArray("members");
        foreach (Mql5IrEnumMember member in node.Members ?? [])
        {
            if (member is null)
            {
                WriteAbsent(writer);
                continue;
            }

            WriteHeader(writer, member);
            writer.WriteString("name", member.Name ?? string.Empty);
            WriteExpressionProperty(writer, "value", member.Value);
            WriteWholeProperty(writer, "folded", member.FoldedValue);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    internal static void WriteTypeDeclaration(Utf8JsonWriter writer, Mql5IrTypeDeclaration? node)
    {
        if (node is null)
        {
            WriteAbsent(writer);
            return;
        }

        WriteHeader(writer, node);
        writer.WriteString("keyword", node.Keyword ?? string.Empty);
        writer.WriteString("name", node.Name ?? string.Empty);
        WriteTypeParameters(writer, node.TypeParameters);
        WriteTextProperty(writer, "base", node.BaseTypeName);
        writer.WriteStartArray("fields");
        foreach (Mql5IrField field in node.Fields ?? [])
        {
            WriteField(writer, field);
        }

        writer.WriteEndArray();
        writer.WriteStartArray("methods");
        foreach (Mql5IrFunction method in node.Methods ?? [])
        {
            WriteFunction(writer, method);
        }

        writer.WriteEndArray();
        writer.WriteStartArray("nestedEnums");
        foreach (Mql5IrEnumeration nested in node.NestedEnums ?? [])
        {
            WriteEnum(writer, nested);
        }

        writer.WriteEndArray();
        writer.WriteStartArray("nestedTypes");
        foreach (Mql5IrTypeDeclaration nested in node.NestedTypes ?? [])
        {
            WriteTypeDeclaration(writer, nested);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    internal static void WriteField(Utf8JsonWriter writer, Mql5IrField? node)
    {
        if (node is null)
        {
            WriteAbsent(writer);
            return;
        }

        WriteHeader(writer, node);
        writer.WriteString("access", Mql5IrLiteral.AccessToken(node.Access));
        WriteTypeProperty(writer, "type", node.Type);
        writer.WriteString("name", node.Name ?? string.Empty);
        WriteRanks(writer, node.ArrayRanks);
        writer.WriteBoolean("static", node.IsStatic);
        writer.WriteBoolean("const", node.IsConst);
        WriteExpressionProperty(writer, "initializer", node.Initializer);
        writer.WriteEndObject();
    }

    internal static void WriteGlobal(Utf8JsonWriter writer, Mql5IrGlobalVariable? node)
    {
        if (node is null)
        {
            WriteAbsent(writer);
            return;
        }

        WriteHeader(writer, node);
        WriteTypeProperty(writer, "type", node.Type);
        writer.WriteString("name", node.Name ?? string.Empty);
        WriteRanks(writer, node.ArrayRanks);
        writer.WriteBoolean("static", node.IsStatic);
        writer.WriteBoolean("const", node.IsConst);
        WriteExpressionProperty(writer, "initializer", node.Initializer);
        writer.WriteEndObject();
    }

    internal static void WriteInput(Utf8JsonWriter writer, Mql5IrInput? node)
    {
        if (node is null)
        {
            WriteAbsent(writer);
            return;
        }

        WriteHeader(writer, node);
        writer.WriteString("storage", Mql5IrLiteral.InputToken(node.InputKind));
        WriteTypeProperty(writer, "type", node.Type);
        writer.WriteString("name", node.Name ?? string.Empty);

        // Fixed position: 'label' and 'groupLabel' always follow 'name', and are always
        // written — as JSON null when absent — so the canonical document has one shape.
        WriteTextProperty(writer, "label", node.Label);
        WriteTextProperty(writer, "groupLabel", node.GroupLabel);
        WriteRanks(writer, node.ArrayRanks);
        writer.WriteBoolean("const", node.IsConst);
        WriteTextProperty(writer, "canonicalDefault", node.CanonicalDefault);
        WriteExpressionProperty(writer, "default", node.DefaultValue);
        writer.WriteEndObject();
    }

    internal static void WriteFunction(Utf8JsonWriter writer, Mql5IrFunction? node)
    {
        if (node is null)
        {
            WriteAbsent(writer);
            return;
        }

        WriteHeader(writer, node);
        writer.WriteString("name", node.Name ?? string.Empty);
        writer.WriteString("access", Mql5IrLiteral.AccessToken(node.Access));
        WriteTypeParameters(writer, node.TypeParameters);
        WriteTypeProperty(writer, "returns", node.ReturnType);
        writer.WriteStartArray("parameters");
        foreach (Mql5IrParameter parameter in node.Parameters ?? [])
        {
            if (parameter is null)
            {
                WriteAbsent(writer);
                continue;
            }

            WriteHeader(writer, parameter);
            WriteTypeProperty(writer, "type", parameter.Type);
            writer.WriteString("name", parameter.Name ?? string.Empty);
            WriteExpressionProperty(writer, "default", parameter.DefaultValue);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteBoolean("static", node.IsStatic);
        writer.WriteBoolean("virtual", node.IsVirtual);
        writer.WriteBoolean("abstract", node.IsAbstract);
        writer.WriteBoolean("const", node.IsConst);
        writer.WriteBoolean("prototype", node.Body is null);
        if (node.Body is null)
        {
            writer.WriteNull("body");
        }
        else
        {
            writer.WritePropertyName("body");
            WriteStatement(writer, node.Body);
        }

        writer.WriteEndObject();
    }

    internal static void WriteStatement(Utf8JsonWriter writer, Mql5IrStatement? node)
    {
        if (node is null)
        {
            WriteAbsent(writer);
            return;
        }

        WriteHeader(writer, node);
        switch (node)
        {
            case Mql5IrBlockStatement block:
                writer.WriteStartArray("statements");
                foreach (Mql5IrStatement statement in block.Statements ?? [])
                {
                    WriteStatement(writer, statement);
                }

                writer.WriteEndArray();
                break;
            case Mql5IrLocalDeclarationStatement declaration:
                WriteTypeProperty(writer, "type", declaration.Type);
                writer.WriteBoolean("static", declaration.IsStatic);
                writer.WriteBoolean("const", declaration.IsConst);
                writer.WriteStartArray("variables");
                foreach (Mql5IrVariable variable in declaration.Variables ?? [])
                {
                    if (variable is null)
                    {
                        WriteAbsent(writer);
                        continue;
                    }

                    WriteHeader(writer, variable);
                    writer.WriteString("name", variable.Name ?? string.Empty);
                    WriteRanks(writer, variable.ArrayRanks);
                    WriteExpressionProperty(writer, "initializer", variable.Initializer);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                break;
            case Mql5IrExpressionStatement expression:
                WriteExpressionProperty(writer, "expression", expression.Expression);
                break;
            case Mql5IrIfStatement branch:
                WriteExpressionProperty(writer, "condition", branch.Condition);
                writer.WritePropertyName("then");
                WriteStatement(writer, branch.WhenTrue);
                if (branch.WhenFalse is null)
                {
                    writer.WriteNull("else");
                }
                else
                {
                    writer.WritePropertyName("else");
                    WriteStatement(writer, branch.WhenFalse);
                }

                break;
            case Mql5IrWhileStatement loop:
                WriteExpressionProperty(writer, "condition", loop.Condition);
                writer.WritePropertyName("body");
                WriteStatement(writer, loop.Body);
                break;
            case Mql5IrDoWhileStatement loop:
                writer.WritePropertyName("body");
                WriteStatement(writer, loop.Body);
                WriteExpressionProperty(writer, "condition", loop.Condition);
                break;
            case Mql5IrForStatement loop:
                if (loop.Initializer is null)
                {
                    writer.WriteNull("initializer");
                }
                else
                {
                    writer.WritePropertyName("initializer");
                    WriteStatement(writer, loop.Initializer);
                }

                WriteExpressionProperty(writer, "condition", loop.Condition);
                WriteExpressionProperty(writer, "increment", loop.Increment);
                writer.WritePropertyName("body");
                WriteStatement(writer, loop.Body);
                break;
            case Mql5IrSwitchStatement selection:
                WriteExpressionProperty(writer, "subject", selection.Subject);
                writer.WriteStartArray("sections");
                foreach (Mql5IrSwitchSection section in selection.Sections ?? [])
                {
                    WriteSwitchSection(writer, section);
                }

                writer.WriteEndArray();
                break;
            case Mql5IrReturnStatement result:
                WriteExpressionProperty(writer, "value", result.Value);
                break;
            case Mql5IrDeleteStatement removal:
                WriteExpressionProperty(writer, "operand", removal.Operand);
                break;
            default:
                // Empty, break and continue carry no payload beyond their header.
                break;
        }

        writer.WriteEndObject();
    }

    internal static void WriteExpression(Utf8JsonWriter writer, Mql5IrExpression? node)
    {
        if (node is null)
        {
            WriteAbsent(writer);
            return;
        }

        WriteHeader(writer, node);
        switch (node)
        {
            case Mql5IrLiteralExpression literal:
                writer.WriteString("literal", Mql5IrLiteral.KindToken(literal.LiteralKind));
                writer.WriteString("text", literal.Text ?? string.Empty);
                writer.WriteString("canonical", literal.CanonicalText ?? string.Empty);
                WriteWholeProperty(writer, "folded", literal.FoldedValue);
                break;
            case Mql5IrNameExpression name:
                writer.WriteBoolean("scoped", name.IsScopeQualified);
                writer.WriteStartArray("scope");
                foreach (string segment in name.Scope ?? [])
                {
                    writer.WriteStringValue(segment ?? string.Empty);
                }

                writer.WriteEndArray();
                writer.WriteString("name", name.Name ?? string.Empty);
                break;
            case Mql5IrUnaryExpression unary:
                writer.WriteString("operator", unary.Operator ?? string.Empty);
                writer.WriteBoolean("prefix", unary.IsPrefix);
                WriteExpressionProperty(writer, "operand", unary.Operand);
                WriteWholeProperty(writer, "folded", unary.FoldedValue);
                break;
            case Mql5IrBinaryExpression binary:
                writer.WriteString("operator", binary.Operator ?? string.Empty);
                WriteExpressionProperty(writer, "left", binary.Left);
                WriteExpressionProperty(writer, "right", binary.Right);
                break;
            case Mql5IrAssignmentExpression assignment:
                writer.WriteString("operator", assignment.Operator ?? string.Empty);
                WriteExpressionProperty(writer, "target", assignment.Target);
                WriteExpressionProperty(writer, "value", assignment.Value);
                break;
            case Mql5IrConditionalExpression conditional:
                WriteExpressionProperty(writer, "condition", conditional.Condition);
                WriteExpressionProperty(writer, "then", conditional.WhenTrue);
                WriteExpressionProperty(writer, "else", conditional.WhenFalse);
                break;
            case Mql5IrCallExpression call:
                WriteExpressionProperty(writer, "callee", call.Callee);
                writer.WriteStartArray("arguments");
                foreach (Mql5IrExpression argument in call.Arguments ?? [])
                {
                    WriteExpression(writer, argument);
                }

                writer.WriteEndArray();
                break;
            case Mql5IrIndexExpression index:
                WriteExpressionProperty(writer, "target", index.Target);
                WriteExpressionProperty(writer, "index", index.Index);
                break;
            case Mql5IrMemberExpression member:
                WriteExpressionProperty(writer, "target", member.Target);
                writer.WriteString("member", member.Member ?? string.Empty);
                writer.WriteBoolean("throughHandle", member.ThroughPointer);
                break;
            case Mql5IrCastExpression cast:
                WriteTypeProperty(writer, "type", cast.Type);
                WriteExpressionProperty(writer, "operand", cast.Operand);
                break;
            case Mql5IrNewExpression allocation:
                WriteTypeProperty(writer, "type", allocation.Type);
                break;
            case Mql5IrSizeOfExpression measurement:
                // Both members are always written, one of them as JSON null, for the same
                // reason as 'typename' below: one document shape whichever form was written.
                WriteTypeProperty(writer, "type", measurement.Type);
                WriteExpressionProperty(writer, "operand", measurement.Operand);
                break;
            case Mql5IrTypeNameExpression typeName:
                // Both members are always written, one of them as JSON null, so the
                // document keeps one shape whichever form the source used.
                WriteTypeProperty(writer, "type", typeName.Type);
                WriteExpressionProperty(writer, "operand", typeName.Operand);
                break;
            case Mql5IrInitializerListExpression initializer:
                writer.WriteStartArray("items");
                foreach (Mql5IrExpression item in initializer.Items ?? [])
                {
                    WriteExpression(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                break;
        }

        writer.WriteEndObject();
    }

    private static void WriteSwitchSection(Utf8JsonWriter writer, Mql5IrSwitchSection? section)
    {
        if (section is null)
        {
            WriteAbsent(writer);
            return;
        }

        WriteHeader(writer, section);
        writer.WriteStartArray("labels");
        foreach (Mql5IrSwitchLabel label in section.Labels ?? [])
        {
            writer.WriteStartObject();
            writer.WriteBoolean("default", label is null || label.IsDefault);
            WriteExpressionProperty(writer, "value", label?.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("statements");
        foreach (Mql5IrStatement statement in section.Statements ?? [])
        {
            WriteStatement(writer, statement);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteExpressionProperty(Utf8JsonWriter writer, string name, Mql5IrExpression? node)
    {
        if (node is null)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WritePropertyName(name);
        WriteExpression(writer, node);
    }

    private static void WriteTypeProperty(Utf8JsonWriter writer, string name, Mql5IrTypeReference? type)
    {
        if (type is null)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WritePropertyName(name);
        WriteHeader(writer, type);
        writer.WriteString("name", type.Name ?? string.Empty);
        writer.WriteString("scalar", Mql5IrLiteral.ScalarToken(type.Scalar));
        writer.WriteBoolean("const", type.IsConst);
        writer.WriteBoolean("handle", type.IsPointer);
        writer.WriteBoolean("byRef", type.IsReference);
        WriteRanks(writer, type.ArrayRanks);
        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes the template parameter list. The array is always emitted, empty for a
    /// non-generic declaration, so the canonical document keeps one shape.
    /// </summary>
    private static void WriteTypeParameters(Utf8JsonWriter writer, IReadOnlyList<string>? typeParameters)
    {
        writer.WriteStartArray("typeParameters");
        foreach (string name in typeParameters ?? [])
        {
            writer.WriteStringValue(name ?? string.Empty);
        }

        writer.WriteEndArray();
    }

    private static void WriteRanks(Utf8JsonWriter writer, IReadOnlyList<Mql5IrArrayRank>? ranks)
    {
        writer.WriteStartArray("ranks");
        foreach (Mql5IrArrayRank rank in ranks ?? [])
        {
            writer.WriteStartObject();
            WriteExpressionProperty(writer, "size", rank?.Size);
            WriteWholeProperty(writer, "folded", rank?.FoldedSize);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteTextProperty(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    private static void WriteWholeProperty(Utf8JsonWriter writer, string name, long? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteNumber(name, value.Value);
        }
    }

    private static void WriteHeader(Utf8JsonWriter writer, Mql5IrNode node)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", node.Kind);
        writer.WriteNumber("line", node.Line);
        writer.WriteNumber("column", node.Column);
    }

    private static void WriteAbsent(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", "absent");
        writer.WriteNumber("line", 0);
        writer.WriteNumber("column", 0);
        writer.WriteEndObject();
    }
}
