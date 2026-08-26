using System.Globalization;
using YO4X.StrategyGovernance;

namespace YO4X.Mql5.CodeGen;

/// <summary>
/// One generation pass over one module. Holds the writer, the diagnostic list and
/// the lookup tables built from the module before emission starts.
/// </summary>
internal sealed partial class Mql5GeneratorRun
{
    /// <summary>Maximum nesting the emitter will descend before it stops.</summary>
    private const int MaxDepth = 200;

    /// <summary>Maximum diagnostics retained. Failure is recorded past this point regardless.</summary>
    private const int MaxDiagnostics = 2000;

    /// <summary>The text substituted for a construct that could not be translated.</summary>
    private const string PoisonToken = "__mql5_unsupported";

    private readonly Mql5IrV2Module _module;
    private readonly Mql5SemanticModel _model;
    private readonly Mql5CSharpWriter _writer;
    private readonly List<Mql5RestrictedDiagnostic> _diagnostics = [];

    /// <summary>Module enumerations by source name, mapped to the C# name they are emitted under.</summary>
    private readonly Dictionary<string, string> _enumTypeNames = new(StringComparer.Ordinal);

    /// <summary>
    /// Enumeration member name to its declaring C# type name. A member declared by more
    /// than one enumeration maps to null: the emitter refuses it rather than picking one.
    /// </summary>
    private readonly Dictionary<string, string?> _enumMemberOwner = new(StringComparer.Ordinal);

    /// <summary>Module structures and classes by source name, mapped to the emitted C# name.</summary>
    private readonly Dictionary<string, string> _typeNames = new(StringComparer.Ordinal);

    /// <summary>Module structures and classes by source name.</summary>
    private readonly Dictionary<string, Mql5IrTypeDeclaration> _typeDeclarations = new(StringComparer.Ordinal);

    /// <summary>The emitted CLR names of types this module declares, which carry a runtime field.</summary>
    private readonly HashSet<string> _moduleTypeClrNames = new(StringComparer.Ordinal);

    /// <summary>
    /// The type parameters in scope: those of the enclosing type and of the method being written.
    /// </summary>
    /// <remarks>
    /// A type parameter is a type name for as long as its declaration is being emitted, and nothing
    /// outside it. Registering them globally would let one template's <c>T</c> resolve inside
    /// another declaration that never declared it.
    /// </remarks>
    private readonly HashSet<string> _typeParametersInScope = new(StringComparer.Ordinal);


    /// <summary>File-scope function definitions by name.</summary>
    private readonly Dictionary<string, List<Mql5IrFunction>> _functions = new(StringComparer.Ordinal);

    /// <summary>Preprocessor replacements by name.</summary>
    private readonly Dictionary<string, string> _defines = new(StringComparer.Ordinal);

    /// <summary>
    /// The out-of-line member definitions this module declares, keyed by the owning type name.
    /// </summary>
    /// <remarks>
    /// MQL5 allows the C++ shape: a member is declared inside the type and defined at file scope
    /// under a qualified name, <c>void CAvg::Open() { … }</c>. IR v2 records the two halves
    /// separately and deliberately does not resolve names, so joining them is this pass's job.
    /// Without the join the prototype looks like a function with no body, and the definition looks
    /// like a module function whose name is not a legal C# identifier.
    /// </remarks>
    private readonly Dictionary<string, List<Mql5IrFunction>> _outOfLineDefinitions =
        new(StringComparer.Ordinal);

    /// <summary>Splits a qualified member name into its owner and member halves.</summary>
    private static (string Owner, string Member)? SplitQualified(string name)
    {
        int marker = name.IndexOf("::", StringComparison.Ordinal);
        return marker <= 0 || marker + 2 >= name.Length
            ? null
            : (name[..marker], name[(marker + 2)..]);
    }


    /// <summary>Names declared by the module at file scope: globals and inputs.</summary>
    private readonly HashSet<string> _fileScopeVariables = new(StringComparer.Ordinal);

    /// <summary>
    /// MQL5 named constants the emitted source actually references. They are
    /// declared into the generated file itself, so a compiled strategy carries its
    /// own constant values and does not depend on a runtime table that might drift.
    /// </summary>
    private readonly SortedSet<string> _referencedConstants = new(StringComparer.Ordinal);

    /// <summary>The MQL5 name of the function whose body is being written, for `__FUNCTION__`.</summary>
    private string _currentFunctionName = string.Empty;

    /// <summary>
    /// The emitted name of every local in the current function that shadows an enclosing one,
    /// keyed by where it was declared. Empty for a function that shadows nothing.
    /// </summary>
    private IReadOnlyDictionary<(int Line, int Column), string> _shadowedLocals =
        new Dictionary<(int, int), string>();

    /// <summary>
    /// MQL5 statics declared inside a function keep their value between calls, which a
    /// C# local cannot. They are hoisted to fields of the strategy class, and this map
    /// rewrites every reference inside the function currently being emitted.
    /// </summary>
    private readonly Dictionary<string, string> _staticLocalNames = new(StringComparer.Ordinal);

    /// <summary>Every hoisted static local, in declaration order, for field emission.</summary>
    private readonly List<StaticLocal> _staticLocals = [];

    private bool _failed;
    private bool _budgetExhausted;
    private string? _currentEnumName;
    private Mql5ResolvedType _currentReturnType = Mql5ResolvedType.Nothing;

    public Mql5GeneratorRun(Mql5IrV2Module module, Mql5SemanticModel model)
    {
        _module = module;
        _model = model;
        _writer = new Mql5CSharpWriter(module.SourcePath);
        StrategyTypeName = Mql5ClrTypes.TypeNameFromPath(module.SourcePath);
    }

    /// <summary>The C# name of the emitted strategy class.</summary>
    public string StrategyTypeName { get; }

    /// <summary>Runs the pass.</summary>
    public Mql5CodeGenResult Execute()
    {
        BuildLookups();
        EmitCompilationUnit();
        EmitReferencedConstants();
        return new Mql5CodeGenResult(!_failed, _writer.ToString(), StrategyTypeName, _diagnostics);
    }

    /// <summary>
    /// Records a reference to an MQL5 named constant and returns the qualified C#
    /// expression for it.
    /// </summary>
    /// <summary>
    /// Expands an MQL5 compile-time context macro, or returns null when the name is not one.
    /// </summary>
    /// <remarks>
    /// These are not constants and do not belong in any catalogue: their value depends on where
    /// they appear, and the MQL5 compiler substitutes them during translation. This emitter knows
    /// the same two facts the compiler does at that point — which file is being translated and
    /// which function is being written — so it can substitute exactly rather than refuse.
    /// </remarks>
    private string? ContextMacro(string name)
    {
        switch (name)
        {
            case "__FILE__":
                return Mql5TextLiteral(Path.GetFileName(_module.SourcePath));
            case "__PATH__":
                return Mql5TextLiteral(_module.SourcePath);
            case "__FUNCTION__":
                return Mql5TextLiteral(_currentFunctionName);
            default:
                return null;
        }
    }

    /// <summary>Renders text as a verbatim-safe C# string literal.</summary>
    private static string Mql5TextLiteral(string text) =>
        "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal)
                   .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    /// <summary>
    /// The argument list for constructing a runtime-provided type.
    /// </summary>
    /// <remarks>
    /// A standard library class needs the runtime that the MQL5 source never names, because in
    /// MetaTrader the built-ins those classes call are ambient. Everything else constructs empty.
    /// </remarks>
    /// <summary>
    /// The object initialiser that binds a newly constructed module type to the runtime.
    /// </summary>
    /// <remarks>
    /// An initialiser rather than a constructor parameter, because MQL5 types declare their own
    /// constructors and inherit from one another: threading an extra argument through every one of
    /// them, and through every base call, would rewrite far more than it fixed. The text is the
    /// same at every site — inside a module type <c>Rt</c> is that type's own field, and at file
    /// scope it is the strategy's — so the binding composes without knowing where it is emitted.
    /// </remarks>
    /// <summary>
    /// The field a module type uses to reach the strategy that owns it.
    /// </summary>
    /// <remarks>
    /// MQL5 lets a type's method read a file-scope global or input directly, because in MetaTrader
    /// they share one program scope. In C# they are fields on the strategy and the type is a
    /// separate declaration, so the type has to be handed the instance. The name is reserved so a
    /// strategy cannot declare something that collides with it.
    /// </remarks>
    private const string OwnerFieldName = "__owner";

    private string ConstructionInitializer(string clrTypeName) =>
        _moduleTypeClrNames.Contains(clrTypeName)
            ? " { " + Mql5RuntimeContract.RuntimeFieldName + " = " + Mql5RuntimeContract.RuntimeFieldName
                + ", " + OwnerFieldName + " = " + (_insideTypeBody ? OwnerFieldName : "this") + " }"
            : string.Empty;

    private static string ConstructionArguments(string clrTypeName) =>
        Mql5ClrTypes.RuntimeTypesTakingTheRuntime.Contains(clrTypeName)
            ? Mql5RuntimeContract.RuntimeFieldName
            : string.Empty;

    /// <summary>
    /// The emitted name of a local declared at this position.
    /// </summary>
    /// <remarks>
    /// Keyed by position rather than by name, because a function can declare the same MQL5 name in
    /// two nested scopes and the two need different C# names.
    /// </remarks>
    private string LocalName(string name, int line, int column) =>
        _shadowedLocals.TryGetValue((line, column), out string? renamed)
            ? renamed
            : Mql5ClrTypes.Identifier(name);

    /// <summary>
    /// The catalogued constant a bare web-colour name refers to, or null when it is not one.
    /// </summary>
    /// <remarks>
    /// MQL5 accepts both spellings of every named colour: <c>Gray</c> and <c>clrGray</c> are the
    /// same constant, which the compiler confirms by folding <c>Gray == clrGray</c> to true. Only
    /// the prefixed form was recognised here, so the bare one — which is what older sources use —
    /// resolved to nothing.
    /// </remarks>
    private static string? ColourConstantFor(string name)
    {
        if (name.Length == 0 || !char.IsAsciiLetterUpper(name[0]))
        {
            return null;
        }

        string prefixed = "clr" + name;
        return Mql5BuiltinConstants.IsKnown(prefixed) ? prefixed : null;
    }

    private string ConstantReference(string name)
    {
        if (ColourConstantFor(name) is string colour)
        {
            return ConstantReference(colour);
        }

        if (ContextMacro(name) is string expansion)
        {
            return expansion;
        }

        string identifier = Mql5ClrTypes.Identifier(name);
        _referencedConstants.Add(name);
        return Mql5RuntimeContract.ConstantHolderName + "." + identifier;
    }

    /// <summary>
    /// Declares every referenced constant into the generated file.
    ///
    /// Values come from <see cref="Mql5BuiltinConstants"/>, which measured them from
    /// the MQL5 compiler itself. A constant whose value is genuinely unpublished is
    /// emitted as a diagnostic rather than a guessed number: a wrong constant would
    /// silently change trading behaviour.
    /// </summary>
    private void EmitReferencedConstants()
    {
        if (_referencedConstants.Count == 0)
        {
            return;
        }

        _writer.Blank();
        _writer.Line("/// <summary>MQL5 named constants referenced by this module.</summary>");
        _writer.Line("file static class " + Mql5RuntimeContract.ConstantHolderName);
        _writer.Line("{");
        _writer.Indent();

        foreach (string name in _referencedConstants)
        {
            string identifier = Mql5ClrTypes.Identifier(name);
            if (Mql5BuiltinConstants.TryGetValue(name, out long value))
            {
                string keyword = value is >= int.MinValue and <= int.MaxValue ? "int" : "long";
                string literal = value.ToString(CultureInfo.InvariantCulture)
                    + (keyword == "long" ? "L" : string.Empty);
                _writer.Line("public const " + keyword + " " + identifier + " = " + literal + ";");
            }
            else if (Mql5BuiltinRealConstants.TryGetValue(name, out double real))
            {
                _writer.Line(
                    "public const double " + identifier + " = "
                        + Mql5BuiltinRealConstants.ToLiteral(real) + ";");
            }
            else
            {
                Fail(
                    Mql5CodeGenDiagnosticCodes.UnsupportedLiteral,
                    "MQL5 constant '" + name + "' has no published value, so it cannot be emitted.",
                    0,
                    0);
            }
        }

        _writer.Outdent();
        _writer.Line("}");
    }

    /// <summary>One MQL5 function-scope static, hoisted to a strategy field.</summary>
    private sealed record StaticLocal(string FieldName, Mql5IrTypeReference Type, Mql5IrVariable Variable);

    // ------------------------------------------------------------------ lookups

    private void BuildLookups()
    {
        foreach (Mql5IrEnumeration enumeration in _module.Enums)
        {
            RegisterEnum(enumeration, owner: null);
        }

        foreach (Mql5IrTypeDeclaration declaration in _module.Types)
        {
            RegisterType(declaration, owner: null);
        }

        foreach (Mql5IrDefine define in _module.Defines)
        {
            _defines[define.Name] = define.Replacement;
        }

        foreach (Mql5IrGlobalVariable global in _module.Globals)
        {
            _fileScopeVariables.Add(global.Name);
        }

        foreach (Mql5IrInput input in _module.Inputs)
        {
            _fileScopeVariables.Add(input.Name);
        }

        foreach (Mql5IrFunction function in _module.Functions)
        {
            if (function.Body is null)
            {
                continue;
            }

            if (SplitQualified(function.Name) is (string owner, _))
            {
                // An out-of-line definition belongs to its type, not to file scope. Registering it
                // as a module function would offer a callee whose emitted name carries a "::".
                if (!_outOfLineDefinitions.TryGetValue(owner, out List<Mql5IrFunction>? members))
                {
                    members = [];
                    _outOfLineDefinitions[owner] = members;
                }

                members.Add(function);
                continue;
            }

            if (!_functions.TryGetValue(function.Name, out List<Mql5IrFunction>? overloads))
            {
                overloads = [];
                _functions[function.Name] = overloads;
            }

            overloads.Add(function);
        }

        foreach (Mql5IrFunction function in _module.Functions)
        {
            if (function.Body is not null)
            {
                CollectStaticLocals(function.Name, function.Body);
            }
        }
    }

    private void RegisterEnum(Mql5IrEnumeration enumeration, string? owner)
    {
        string emitted = owner is null
            ? Mql5ClrTypes.Identifier(enumeration.Name)
            : owner + "." + Mql5ClrTypes.Identifier(enumeration.Name);
        _enumTypeNames[enumeration.Name] = emitted;

        foreach (Mql5IrEnumMember member in enumeration.Members)
        {
            if (_enumMemberOwner.TryGetValue(member.Name, out string? existing))
            {
                if (!string.Equals(existing, emitted, StringComparison.Ordinal))
                {
                    _enumMemberOwner[member.Name] = null;
                }

                continue;
            }

            _enumMemberOwner[member.Name] = emitted;
        }
    }

    private void RegisterType(Mql5IrTypeDeclaration declaration, string? owner)
    {
        string emitted = owner is null
            ? Mql5ClrTypes.Identifier(declaration.Name)
            : owner + "." + Mql5ClrTypes.Identifier(declaration.Name);
        _typeNames[declaration.Name] = emitted;
        _typeDeclarations[declaration.Name] = declaration;
        _moduleTypeClrNames.Add(emitted);

        foreach (Mql5IrEnumeration nested in declaration.NestedEnums)
        {
            RegisterEnum(nested, emitted);
        }

        foreach (Mql5IrTypeDeclaration nested in declaration.NestedTypes)
        {
            RegisterType(nested, emitted);
        }
    }

    private void CollectStaticLocals(string functionName, Mql5IrStatement statement)
    {
        switch (statement)
        {
            case Mql5IrLocalDeclarationStatement declaration when declaration.IsStatic:
                foreach (Mql5IrVariable variable in declaration.Variables)
                {
                    _staticLocals.Add(
                        new StaticLocal(StaticFieldName(functionName, variable.Name), declaration.Type, variable));
                }

                break;
            case Mql5IrBlockStatement block:
                foreach (Mql5IrStatement child in block.Statements)
                {
                    CollectStaticLocals(functionName, child);
                }

                break;
            case Mql5IrIfStatement conditional:
                CollectStaticLocals(functionName, conditional.WhenTrue);
                if (conditional.WhenFalse is not null)
                {
                    CollectStaticLocals(functionName, conditional.WhenFalse);
                }

                break;
            case Mql5IrWhileStatement loop:
                CollectStaticLocals(functionName, loop.Body);
                break;
            case Mql5IrDoWhileStatement loop:
                CollectStaticLocals(functionName, loop.Body);
                break;
            case Mql5IrForStatement loop:
                if (loop.Initializer is not null)
                {
                    CollectStaticLocals(functionName, loop.Initializer);
                }

                CollectStaticLocals(functionName, loop.Body);
                break;
            case Mql5IrSwitchStatement selection:
                foreach (Mql5IrSwitchSection section in selection.Sections)
                {
                    foreach (Mql5IrStatement child in section.Statements)
                    {
                        CollectStaticLocals(functionName, child);
                    }
                }

                break;
            default:
                break;
        }
    }

    private static string StaticFieldName(string functionName, string variableName) =>
        "__static_" + functionName + "_" + variableName;

    // -------------------------------------------------------------- diagnostics

    /// <summary>
    /// Records an untranslatable construct and returns the poison text that replaces
    /// it. The poison is an undefined identifier so that emitted source which failed
    /// generation cannot compile even if a caller ignores the result flag.
    /// </summary>
    private string Fail(string code, string message, int line, int column)
    {
        _failed = true;
        if (_diagnostics.Count < MaxDiagnostics)
        {
            _diagnostics.Add(
                new Mql5RestrictedDiagnostic(code, Mql5RestrictedDiagnosticSeverity.Error, message, line, column));
        }

        return PoisonToken;
    }

    private void Note(string code, string message, int line, int column)
    {
        if (_diagnostics.Count < MaxDiagnostics)
        {
            _diagnostics.Add(
                new Mql5RestrictedDiagnostic(
                    code, Mql5RestrictedDiagnosticSeverity.Information, message, line, column));
        }
    }

    private bool Budget(int depth, int line, int column)
    {
        if (depth <= MaxDepth)
        {
            return true;
        }

        if (!_budgetExhausted)
        {
            _budgetExhausted = true;
            Fail(
                Mql5CodeGenDiagnosticCodes.DepthLimit,
                "Nesting exceeded the emitter budget of " + MaxDepth + " levels.",
                line,
                column);
        }

        _failed = true;
        return false;
    }
}
