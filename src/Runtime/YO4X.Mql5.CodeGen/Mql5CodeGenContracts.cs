using YO4X.StrategyGovernance;

namespace YO4X.Mql5.CodeGen;

/// <summary>
/// The outcome of translating one lowered MQL5 module into C# source text.
///
/// <paramref name="Succeeded"/> is false whenever any construct could not be
/// translated. The generator is fail-closed per construct: it never emits a
/// partial method body and never drops a statement, because a strategy that
/// compiles with a missing branch is worse than one that refuses to compile.
/// <paramref name="CSharpSource"/> is still populated on failure — with the
/// untranslatable construct replaced by a compile-time error marker — so that a
/// human can read what was produced, but it must never be compiled and run.
/// </summary>
public sealed record Mql5CodeGenResult(
    bool Succeeded,
    string? CSharpSource,
    string TypeName,
    IReadOnlyList<Mql5RestrictedDiagnostic> Diagnostics)
{
    /// <summary>The number of error-severity diagnostics produced.</summary>
    public int ErrorCount =>
        Diagnostics.Count(diagnostic => diagnostic.Severity == Mql5RestrictedDiagnosticSeverity.Error);

    /// <summary>The fully qualified name of the emitted strategy type.</summary>
    public string FullTypeName => Mql5RuntimeContract.GeneratedNamespace + "." + TypeName;
}

/// <summary>
/// Stable diagnostic codes emitted by <see cref="Mql5CodeGenerator"/>.
///
/// Every code here means the same thing: the construct was understood by the front
/// end and the binder, and this pass declined to translate it. None of them is a
/// claim that the MQL5 source is wrong.
/// </summary>
public static class Mql5CodeGenDiagnosticCodes
{
    /// <summary>A name resolved to nothing, so no C# could be written for it.</summary>
    public const string UnresolvedName = "MQL5_CODEGEN_UNSUPPORTED_UNRESOLVED_NAME";

    /// <summary>A written type maps onto no CLR type.</summary>
    public const string UnsupportedType = "MQL5_CODEGEN_UNSUPPORTED_TYPE";

    /// <summary>An operator has no faithful C# spelling.</summary>
    public const string UnsupportedOperator = "MQL5_CODEGEN_UNSUPPORTED_OPERATOR";

    /// <summary>A literal lexeme could not be re-spelled as a C# literal.</summary>
    public const string UnsupportedLiteral = "MQL5_CODEGEN_UNSUPPORTED_LITERAL";

    /// <summary>A call could not be routed to the runtime or to a module function.</summary>
    public const string UnsupportedCall = "MQL5_CODEGEN_UNSUPPORTED_CALL";

    /// <summary>A built-in reference parameter received an argument that is not an lvalue.</summary>
    public const string UnsupportedReferenceArgument = "MQL5_CODEGEN_UNSUPPORTED_REFERENCE_ARGUMENT";

    /// <summary>The catalog offers no single signature for a built-in at this arity.</summary>
    public const string UnsupportedBuiltinArity = "MQL5_CODEGEN_UNSUPPORTED_BUILTIN_ARITY";

    /// <summary>A built-in is catalogued but classified as not realisable in this engine.</summary>
    public const string UnsupportedBuiltin = "MQL5_CODEGEN_UNSUPPORTED_BUILTIN";

    /// <summary>
    /// A built-in the catalog classifies as not realisable — file I/O, DLL import,
    /// terminal control — was emitted as a runtime call anyway. Information, not error:
    /// the emitter can translate the call perfectly well, and whether the engine
    /// permits it is the runtime's decision, made where the policy actually lives.
    /// Refusing here would push a runtime policy into the compiler.
    /// </summary>
    public const string RuntimeGatedBuiltin = "MQL5_CODEGEN_RUNTIME_GATED_BUILTIN";

    /// <summary>An array of rank three or higher, which the emitter does not model.</summary>
    public const string UnsupportedArrayRank = "MQL5_CODEGEN_UNSUPPORTED_ARRAY_RANK";

    /// <summary>A <c>switch</c> section falls through into the next one.</summary>
    public const string UnsupportedSwitchFallthrough = "MQL5_CODEGEN_UNSUPPORTED_SWITCH_FALLTHROUGH";

    /// <summary>A <c>#define</c> replacement is not a literal and cannot be substituted.</summary>
    public const string UnsupportedDefine = "MQL5_CODEGEN_UNSUPPORTED_DEFINE";

    /// <summary>A function pointer or an address-of expression.</summary>
    public const string UnsupportedPointer = "MQL5_CODEGEN_UNSUPPORTED_POINTER";

    /// <summary>A <c>sizeof</c> of a type whose layout this pass does not know.</summary>
    public const string UnsupportedSizeOf = "MQL5_CODEGEN_UNSUPPORTED_SIZEOF";

    /// <summary>A brace initialiser in a position where no array type is known.</summary>
    public const string UnsupportedInitializer = "MQL5_CODEGEN_UNSUPPORTED_INITIALIZER";

    /// <summary>A declaration collides with a name the emitter reserves.</summary>
    public const string ReservedIdentifier = "MQL5_CODEGEN_UNSUPPORTED_RESERVED_IDENTIFIER";

    /// <summary>A type declaration form the emitter does not model.</summary>
    public const string UnsupportedTypeDeclaration = "MQL5_CODEGEN_UNSUPPORTED_TYPE_DECLARATION";

    /// <summary>Emission stopped because the nesting budget was exhausted.</summary>
    public const string DepthLimit = "MQL5_CODEGEN_UNSUPPORTED_DEPTH_LIMIT";

    /// <summary>An unexpected failure inside the emitter. Never a claim about the source.</summary>
    public const string InternalFailure = "MQL5_CODEGEN_UNSUPPORTED_INTERNAL_FAILURE";

    /// <summary>The module has no entry point the strategy interface can bind to. Information.</summary>
    public const string NoEntryPoint = "MQL5_CODEGEN_NO_ENTRY_POINT";

    /// <summary>The module declares an <c>#import</c> block, whose prototypes are not emitted. Information.</summary>
    public const string ImportsIgnored = "MQL5_CODEGEN_IMPORTS_IGNORED";
}

/// <summary>Additional codes that concern declaration-level constructs.</summary>
public static class Mql5CodeGenDeclarationDiagnosticCodes
{
    /// <summary>A parameter default value is not a compile-time constant in C#.</summary>
    public const string UnsupportedDefaultArgument = "MQL5_CODEGEN_UNSUPPORTED_DEFAULT_ARGUMENT";

    /// <summary>A destructor, operator or other member form with no faithful C# spelling.</summary>
    public const string UnsupportedMember = "MQL5_CODEGEN_UNSUPPORTED_MEMBER";

    /// <summary>A module type reads file-scope state that a C# nested type cannot reach.</summary>
    public const string UnsupportedOuterScopeReference = "MQL5_CODEGEN_UNSUPPORTED_OUTER_SCOPE_REFERENCE";

    /// <summary>A statement form that cannot stand alone as a C# statement.</summary>
    public const string UnsupportedStatement = "MQL5_CODEGEN_UNSUPPORTED_STATEMENT";
}
