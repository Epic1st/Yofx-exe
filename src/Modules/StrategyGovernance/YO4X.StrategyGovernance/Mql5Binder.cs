using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

namespace YO4X.StrategyGovernance;

/// <summary>
/// The semantic binder: the pass that turns a structural IR module into a resolved
/// one.
///
/// Lowering proves that a source document was <em>parsed</em>. Binding is what makes
/// it <em>understood</em>: every name is attributed to a declaration, every
/// expression carries a type, and every place where neither could be determined is
/// named explicitly rather than passed silently downstream.
///
/// Two failures are kept apart on purpose. A name that refers to nothing is an
/// error, because no correct code can be generated from it. A reference to a known
/// MQL5 built-in that this runtime does not implement is information, because the
/// source is valid MQL5 and the gap is ours. Collapsing them would answer neither
/// question.
///
/// The pass never throws and never mutates its input.
/// </summary>
public static class Mql5Binder
{
    /// <summary>Maximum expression or statement nesting depth before binding stops descending.</summary>
    internal const int MaxDepth = 160;

    /// <summary>Maximum diagnostics retained for one module. Counters stay exact past this point.</summary>
    internal const int MaxDiagnostics = 4000;

    /// <summary>
    /// Binds <paramref name="module"/> and returns its semantic model together with
    /// every diagnostic the pass produced.
    /// </summary>
    public static Mql5BindResult Bind(Mql5IrV2Module module)
    {
        if (module is null)
        {
            return EmptyResult(
                new Mql5RestrictedDiagnostic(
                    Mql5BindDiagnosticCodes.BudgetExhausted,
                    Mql5RestrictedDiagnosticSeverity.Information,
                    "No module was supplied, so nothing was bound.",
                    1,
                    1));
        }

        try
        {
            var run = new Mql5BinderRun(module);
            return run.Execute();
        }
#pragma warning disable CA1031 // Binding is a best-effort analysis pass and must never propagate a failure.
        catch (Exception error)
#pragma warning restore CA1031
        {
            var model = new Mql5SemanticModel(
                module,
                new Dictionary<Mql5IrExpression, Mql5ResolvedSymbol>(Mql5ExpressionReferenceComparer.Instance),
                new Dictionary<Mql5IrExpression, Mql5ResolvedType>(Mql5ExpressionReferenceComparer.Instance),
                [
                    new Mql5RestrictedDiagnostic(
                        Mql5BindDiagnosticCodes.BudgetExhausted,
                        Mql5RestrictedDiagnosticSeverity.Information,
                        "Binding stopped early: " + error.GetType().Name + ".",
                        1,
                        1),
                ]);
            return new Mql5BindResult(false, model, model.Diagnostics);
        }
    }

    private static Mql5BindResult EmptyResult(Mql5RestrictedDiagnostic diagnostic)
    {
        Mql5IrV2Module empty = Mql5IrV2Module.Create(
            sourcePath: null,
            sourceSha256: null,
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            []);

        var model = new Mql5SemanticModel(
            empty,
            new Dictionary<Mql5IrExpression, Mql5ResolvedSymbol>(Mql5ExpressionReferenceComparer.Instance),
            new Dictionary<Mql5IrExpression, Mql5ResolvedType>(Mql5ExpressionReferenceComparer.Instance),
            [diagnostic]);

        return new Mql5BindResult(false, model, model.Diagnostics);
    }
}

// --------------------------------------------------------------- binder scopes

/// <summary>
/// One lexical scope: a name table, a link to its parent, and the set of names this
/// scope will declare later.
///
/// The pending set is what makes use-before-declaration distinguishable from an
/// unresolved name. Without it both look identical at the point of use, and the
/// diagnostic would blame the wrong thing.
/// </summary>
internal sealed class Mql5BinderScope
{
    public Mql5BinderScope(Mql5BinderScope? parent)
    {
        Parent = parent;
    }

    public Mql5BinderScope? Parent { get; }

    public Dictionary<string, Mql5ResolvedSymbol> Symbols { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, (int Line, int Column)> Pending { get; } = new(StringComparer.Ordinal);
}

/// <summary>A declared structure, class or interface, indexed for constant-time member lookup.</summary>
internal sealed class Mql5BinderTypeInfo
{
    public Mql5BinderTypeInfo(Mql5IrTypeDeclaration declaration)
    {
        Declaration = declaration;
        Name = declaration.Name;
        Keyword = declaration.Keyword;
        BaseTypeName = declaration.BaseTypeName;
    }

    public Mql5IrTypeDeclaration Declaration { get; }

    public string Name { get; }

    public string Keyword { get; }

    public string? BaseTypeName { get; }

    public Dictionary<string, Mql5IrField> Fields { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, List<Mql5IrFunction>> Methods { get; } = new(StringComparer.Ordinal);
}

// ------------------------------------------------------------------ binder run

/// <summary>
/// One binding pass over one module. Holds all mutable state so that
/// <see cref="Mql5Binder"/> itself stays stateless and re-entrant.
/// </summary>
internal sealed class Mql5BinderRun
{
    private readonly Mql5IrV2Module _module;

    private readonly Dictionary<Mql5IrExpression, Mql5ResolvedSymbol> _symbols =
        new(Mql5ExpressionReferenceComparer.Instance);

    private readonly Dictionary<Mql5IrExpression, Mql5ResolvedType> _expressionTypes =
        new(Mql5ExpressionReferenceComparer.Instance);

    private readonly List<Mql5RestrictedDiagnostic> _diagnostics = [];
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

    private readonly Dictionary<string, Mql5BinderTypeInfo> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Mql5IrEnumeration> _enums = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _enumMemberOwner = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Mql5IrFunction>> _functions = new(StringComparer.Ordinal);

    private readonly Dictionary<string, Mql5BinderScope> _memberScopes = new(StringComparer.Ordinal);

    /// <summary>Template parameter names in scope, counted so nested lists can be popped independently.</summary>
    private readonly Dictionary<string, int> _typeParameters = new(StringComparer.Ordinal);

    private readonly Mql5BinderScope _globalScope = new(parent: null);

    private Mql5BinderTypeInfo? _currentType;

    private int _expressions;
    private int _typedExpressions;
    private int _nameExpressions;
    private int _resolvedNames;
    private int _unresolvedNames;
    private int _builtinFunctionReferences;
    private int _builtinConstantReferences;
    private int _calls;
    private int _resolvedCalls;
    private int _unresolvedCalls;
    private int _ambiguousCalls;
    private int _arityMismatches;
    private int _useBeforeDeclarations;
    private int _duplicateDeclarations;
    private int _unknownTypeReferences;
    private int _unknownFromBuiltinCall;
    private int _unknownFromMemberAccess;
    private int _depthLimitHits;

    private readonly HashSet<string> _unimplementedBuiltins = new(StringComparer.Ordinal);

    public Mql5BinderRun(Mql5IrV2Module module)
    {
        _module = module;
    }

    public Mql5BindResult Execute()
    {
        IndexDeclarations();
        PopulateGlobalScope();
        BindModuleBodies();

        _diagnostics.Sort(static (left, right) =>
        {
            int byLine = left.Line.CompareTo(right.Line);
            if (byLine != 0)
            {
                return byLine;
            }

            int byColumn = left.Column.CompareTo(right.Column);
            return byColumn != 0 ? byColumn : string.CompareOrdinal(left.Code, right.Code);
        });

        var statistics = new Mql5BindStatistics(
            _expressions,
            _typedExpressions,
            _nameExpressions,
            _resolvedNames,
            _unresolvedNames,
            _builtinFunctionReferences,
            _builtinConstantReferences,
            _unimplementedBuiltins.Count,
            _calls,
            _resolvedCalls,
            _unresolvedCalls,
            _ambiguousCalls,
            _arityMismatches,
            _useBeforeDeclarations,
            _duplicateDeclarations,
            _unknownTypeReferences,
            _unknownFromBuiltinCall,
            _unknownFromMemberAccess,
            _depthLimitHits);

        var model = new Mql5SemanticModel(_module, _symbols, _expressionTypes, _diagnostics)
        {
            Statistics = statistics,
        };

        bool succeeded = !_diagnostics.Exists(
            static diagnostic => diagnostic.Severity == Mql5RestrictedDiagnosticSeverity.Error);

        return new Mql5BindResult(succeeded, model, _diagnostics);
    }

    // ------------------------------------------------------------- declaration indexing

    private void IndexDeclarations()
    {
        foreach (Mql5IrEnumeration enumeration in _module.Enums)
        {
            IndexEnumeration(enumeration);
        }

        foreach (Mql5IrTypeDeclaration type in _module.Types)
        {
            IndexType(type, depth: 0);
        }

        foreach (Mql5IrFunction function in _module.Functions)
        {
            // A method defined outside its class body is lowered as a module function
            // named 'CType::Method'. It belongs to the type, not to file scope.
            if (TryGetOwningType(function.Name, out Mql5BinderTypeInfo? owner, out string method))
            {
                AddFunction(owner.Methods, method, function);
                continue;
            }

            AddFunction(_functions, function.Name, function);
        }

        foreach (Mql5IrImport import in _module.Imports)
        {
            foreach (Mql5IrFunction function in import.Functions)
            {
                AddFunction(_functions, function.Name, function);
            }
        }
    }

    private bool TryGetOwningType(
        string qualifiedName,
        out Mql5BinderTypeInfo owner,
        out string memberName)
    {
        int separator = qualifiedName.LastIndexOf("::", StringComparison.Ordinal);
        if (separator > 0
            && TryGetDeclaredType(qualifiedName[..separator], out Mql5BinderTypeInfo? found))
        {
            owner = found;
            memberName = qualifiedName[(separator + 2)..];
            return true;
        }

        owner = null!;
        memberName = qualifiedName;
        return false;
    }

    private void IndexEnumeration(Mql5IrEnumeration enumeration)
    {
        _enums.TryAdd(enumeration.Name, enumeration);
        foreach (Mql5IrEnumMember member in enumeration.Members)
        {
            _enumMemberOwner.TryAdd(member.Name, enumeration.Name);
        }
    }

    private void IndexType(Mql5IrTypeDeclaration declaration, int depth)
    {
        if (depth > 16)
        {
            _depthLimitHits++;
            return;
        }

        var info = new Mql5BinderTypeInfo(declaration);
        foreach (Mql5IrField field in declaration.Fields)
        {
            info.Fields[field.Name] = field;
        }

        foreach (Mql5IrFunction method in declaration.Methods)
        {
            AddFunction(info.Methods, method.Name, method);
        }

        _types[declaration.Name] = info;

        foreach (Mql5IrEnumeration nested in declaration.NestedEnums)
        {
            IndexEnumeration(nested);
        }

        foreach (Mql5IrTypeDeclaration nested in declaration.NestedTypes)
        {
            IndexType(nested, depth + 1);
        }
    }

    private static void AddFunction(
        Dictionary<string, List<Mql5IrFunction>> table,
        string key,
        Mql5IrFunction function)
    {
        if (!table.TryGetValue(key, out List<Mql5IrFunction>? overloads))
        {
            overloads = [];
            table[key] = overloads;
        }

        overloads.Add(function);
    }

    // ------------------------------------------------------------------- global scope

    private void PopulateGlobalScope()
    {
        foreach (KeyValuePair<string, Mql5IrEnumeration> pair in _enums)
        {
            Declare(
                _globalScope,
                new Mql5ResolvedSymbol(
                    Mql5SymbolKind.EnumerationName,
                    pair.Key,
                    new Mql5ResolvedType(
                        Mql5ResolvedTypeKind.TypeName, Mql5IrScalarKind.None, pair.Key, 0, false, false),
                    pair.Value.Line,
                    pair.Value.Column,
                    true),
                reportDuplicates: false);

            foreach (Mql5IrEnumMember member in pair.Value.Members)
            {
                Declare(
                    _globalScope,
                    new Mql5ResolvedSymbol(
                        Mql5SymbolKind.EnumMember,
                        member.Name,
                        EnumerationType(pair.Key),
                        member.Line,
                        member.Column,
                        true),
                    reportDuplicates: false);
            }
        }

        foreach (KeyValuePair<string, Mql5BinderTypeInfo> pair in _types)
        {
            Declare(
                _globalScope,
                new Mql5ResolvedSymbol(
                    Mql5SymbolKind.TypeName,
                    pair.Key,
                    new Mql5ResolvedType(
                        Mql5ResolvedTypeKind.TypeName, Mql5IrScalarKind.None, pair.Key, 0, false, false),
                    pair.Value.Declaration.Line,
                    pair.Value.Declaration.Column,
                    true),
                reportDuplicates: false);
        }

        foreach (Mql5IrDefine define in _module.Defines)
        {
            Declare(
                _globalScope,
                new Mql5ResolvedSymbol(
                    Mql5SymbolKind.Define,
                    define.Name,
                    ClassifyDefine(define.Replacement),
                    define.Line,
                    define.Column,
                    true),
                reportDuplicates: false);
        }

        foreach (KeyValuePair<string, List<Mql5IrFunction>> pair in _functions)
        {
            Mql5IrFunction first = pair.Value[0];
            Declare(
                _globalScope,
                new Mql5ResolvedSymbol(
                    Mql5SymbolKind.Function,
                    pair.Key,
                    ResolveTypeReference(first.ReturnType, report: false),
                    first.Line,
                    first.Column,
                    true),
                reportDuplicates: false);
        }

        foreach (Mql5IrGlobalVariable global in _module.Globals)
        {
            Declare(
                _globalScope,
                new Mql5ResolvedSymbol(
                    Mql5SymbolKind.GlobalVariable,
                    global.Name,
                    ResolveTypeReference(global.Type, report: false, extraRank: global.ArrayRanks.Count),
                    global.Line,
                    global.Column,
                    true),
                reportDuplicates: true);
        }

        foreach (Mql5IrInput input in _module.Inputs)
        {
            Declare(
                _globalScope,
                new Mql5ResolvedSymbol(
                    Mql5SymbolKind.Input,
                    input.Name,
                    ResolveTypeReference(input.Type, report: false, extraRank: input.ArrayRanks.Count),
                    input.Line,
                    input.Column,
                    true),
                reportDuplicates: true);
        }
    }

    private static Mql5ResolvedType ClassifyDefine(string replacement)
    {
        string text = replacement.Trim();
        if (text.Length == 0)
        {
            return Mql5ResolvedType.Unknown;
        }

        if (text[0] == '"')
        {
            return Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Text);
        }

        if (Mql5IrLiteral.TryFoldWhole(text) is not null)
        {
            return Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Whole32);
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            ? Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Real64)
            : Mql5ResolvedType.Unknown;
    }

    private void Declare(Mql5BinderScope scope, Mql5ResolvedSymbol symbol, bool reportDuplicates)
    {
        if (scope.Symbols.TryGetValue(symbol.Name, out Mql5ResolvedSymbol? existing))
        {
            if (reportDuplicates)
            {
                _duplicateDeclarations++;
                Report(
                    Mql5BindDiagnosticCodes.DuplicateDeclaration,
                    Mql5RestrictedDiagnosticSeverity.Error,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"'{symbol.Name}' is already declared in this scope at line {existing.DeclarationLine}."),
                    symbol.DeclarationLine,
                    symbol.DeclarationColumn,
                    symbol.Name);
            }

            return;
        }

        scope.Symbols[symbol.Name] = symbol;
        scope.Pending.Remove(symbol.Name);
    }

    // ------------------------------------------------------------------ type resolution

    private static Mql5ResolvedType EnumerationType(string name) =>
        new(Mql5ResolvedTypeKind.Enumeration, Mql5IrScalarKind.Whole32, name, 0, false, false);

    /// <summary>
    /// Looks a written type name up in the declared-type table.
    ///
    /// A template instantiation is written with its arguments attached, and lowering
    /// keeps that spelling: <c>class Block0 : public MDL_Condition&lt;double,int&gt;</c>
    /// reaches here as one name. Only one declaration exists behind it, indexed under
    /// <c>MDL_Condition</c>, so the argument list is cut off before the lookup.
    /// Without this, an instantiation would inherit nothing and every member it uses
    /// from its base would be reported as referring to no declaration.
    /// </summary>
    private bool TryGetDeclaredType(string? name, [NotNullWhen(true)] out Mql5BinderTypeInfo? info)
    {
        if (name is null)
        {
            info = null;
            return false;
        }

        int arguments = name.IndexOf('<', StringComparison.Ordinal);
        return _types.TryGetValue(arguments < 0 ? name : name[..arguments], out info);
    }

    /// <summary>
    /// Brings a template's parameter names into scope for the declaration it introduces.
    ///
    /// The counter is per name rather than a plain set because a generic method inside a
    /// generic class nests two lists, and the inner one must not remove a name the outer
    /// one still owns when it is popped.
    /// </summary>
    private void PushTypeParameters(IReadOnlyList<string> typeParameters)
    {
        foreach (string name in typeParameters)
        {
            _typeParameters.TryGetValue(name, out int depth);
            _typeParameters[name] = depth + 1;
        }
    }

    private void PopTypeParameters(IReadOnlyList<string> typeParameters)
    {
        foreach (string name in typeParameters)
        {
            if (!_typeParameters.TryGetValue(name, out int depth))
            {
                continue;
            }

            if (depth <= 1)
            {
                _typeParameters.Remove(name);
                continue;
            }

            _typeParameters[name] = depth - 1;
        }
    }

    private Mql5ResolvedType ResolveTypeReference(
        Mql5IrTypeReference? type,
        bool report,
        int extraRank = 0)
    {
        if (type is null)
        {
            return Mql5ResolvedType.Unknown;
        }

        int rank = type.ArrayRanks.Count + extraRank;

        if (type.Scalar != Mql5IrScalarKind.None)
        {
            return Mql5ResolvedType.ForScalar(type.Scalar) with
            {
                ArrayRank = rank,
                IsPointer = type.IsPointer,
            };
        }

        string name = type.Name;

        // An empty name is not a name that failed to resolve: the parser spells the
        // implicit type of a constructor or destructor that way, because the source
        // declared no return type at all. Reporting it would blame every constructor in
        // the module for naming a type that does not exist.
        if (name.Length == 0)
        {
            return Mql5ResolvedType.Unknown;
        }

        // A template parameter names a type that has no identity until the template is
        // instantiated, and nothing here instantiates one. Saying "unknown" is the
        // honest answer; saying it names nothing at all would be false, so the written
        // name is kept and no diagnostic is raised.
        if (_typeParameters.ContainsKey(name))
        {
            return new Mql5ResolvedType(
                Mql5ResolvedTypeKind.Unknown, Mql5IrScalarKind.None, name, rank, type.IsPointer, false);
        }

        if (_enums.ContainsKey(name))
        {
            return EnumerationType(name) with { ArrayRank = rank, IsPointer = type.IsPointer };
        }

        if (TryGetDeclaredType(name, out Mql5BinderTypeInfo? info))
        {
            Mql5ResolvedTypeKind kind = string.Equals(info.Keyword, "struct", StringComparison.Ordinal)
                ? Mql5ResolvedTypeKind.Structure
                : Mql5ResolvedTypeKind.Class;
            return new Mql5ResolvedType(kind, Mql5IrScalarKind.None, name, rank, type.IsPointer, false);
        }

        if (Mql5BinderRuntime.IsBuiltinEnumeration(name))
        {
            return new Mql5ResolvedType(
                Mql5ResolvedTypeKind.Enumeration, Mql5IrScalarKind.Whole32, name, rank, type.IsPointer, true);
        }

        if (Mql5BinderRuntime.IsBuiltinType(name))
        {
            Mql5ResolvedTypeKind kind = name.Length > 0 && name[0] == 'C'
                ? Mql5ResolvedTypeKind.Class
                : Mql5ResolvedTypeKind.Structure;
            return new Mql5ResolvedType(kind, Mql5IrScalarKind.None, name, rank, type.IsPointer, true);
        }

        _unknownTypeReferences++;
        if (report)
        {
            Report(
                Mql5BindDiagnosticCodes.UnknownType,
                Mql5RestrictedDiagnosticSeverity.Error,
                "'" + name + "' names no declared or built-in type.",
                type.Line,
                type.Column,
                name);
        }

        return Mql5ResolvedType.Unknown;
    }

    // --------------------------------------------------------------------- module walk

    private void BindModuleBodies()
    {
        foreach (Mql5IrEnumeration enumeration in _module.Enums)
        {
            BindEnumerationValues(enumeration);
        }

        foreach (Mql5IrGlobalVariable global in _module.Globals)
        {
            ResolveTypeReference(global.Type, report: true);
            BindArrayRanks(global.ArrayRanks, _globalScope);
            if (global.Initializer is not null)
            {
                BindExpression(global.Initializer, _globalScope, 0);
            }
        }

        foreach (Mql5IrInput input in _module.Inputs)
        {
            ResolveTypeReference(input.Type, report: true);
            BindArrayRanks(input.ArrayRanks, _globalScope);
            if (input.DefaultValue is not null)
            {
                BindExpression(input.DefaultValue, _globalScope, 0);
            }
        }

        foreach (Mql5IrTypeDeclaration declaration in _module.Types)
        {
            BindTypeDeclaration(declaration, 0);
        }

        foreach (Mql5IrFunction function in _module.Functions)
        {
            if (TryGetOwningType(function.Name, out Mql5BinderTypeInfo? owner, out _))
            {
                Mql5BinderTypeInfo? previous = _currentType;
                _currentType = owner;
                BindFunction(function, GetMemberScope(owner));
                _currentType = previous;
                continue;
            }

            BindFunction(function, _globalScope);
        }
    }

    private void BindEnumerationValues(Mql5IrEnumeration enumeration)
    {
        foreach (Mql5IrEnumMember member in enumeration.Members)
        {
            if (member.Value is not null)
            {
                BindExpression(member.Value, _globalScope, 0);
            }
        }
    }

    private void BindTypeDeclaration(Mql5IrTypeDeclaration declaration, int depth)
    {
        if (depth > 16)
        {
            _depthLimitHits++;
            return;
        }

        if (!TryGetDeclaredType(declaration.Name, out Mql5BinderTypeInfo? info))
        {
            return;
        }

        Mql5BinderTypeInfo? previousType = _currentType;
        _currentType = info;
        PushTypeParameters(declaration.TypeParameters);

        Mql5BinderScope memberScope = GetMemberScope(info);

        foreach (Mql5IrField field in declaration.Fields)
        {
            ResolveTypeReference(field.Type, report: true);
            BindArrayRanks(field.ArrayRanks, memberScope);
            if (field.Initializer is not null)
            {
                BindExpression(field.Initializer, memberScope, 0);
            }
        }

        foreach (Mql5IrEnumeration nested in declaration.NestedEnums)
        {
            BindEnumerationValues(nested);
        }

        foreach (Mql5IrFunction method in declaration.Methods)
        {
            BindFunction(method, memberScope);
        }

        _currentType = previousType;

        foreach (Mql5IrTypeDeclaration nested in declaration.NestedTypes)
        {
            BindTypeDeclaration(nested, depth + 1);
        }

        PopTypeParameters(declaration.TypeParameters);
    }

    /// <summary>
    /// The scope in which the methods of <paramref name="info"/> are bound: its own and
    /// inherited members, plus the implicit <c>this</c>. Built once per type, because a
    /// method defined outside its class body needs the same scope as one defined inside.
    /// </summary>
    private Mql5BinderScope GetMemberScope(Mql5BinderTypeInfo info)
    {
        if (_memberScopes.TryGetValue(info.Name, out Mql5BinderScope? existing))
        {
            return existing;
        }

        var scope = new Mql5BinderScope(_globalScope);
        _memberScopes[info.Name] = scope;

        PopulateTypeMembers(info, scope, 0);
        Declare(
            scope,
            new Mql5ResolvedSymbol(
                Mql5SymbolKind.Parameter,
                "this",
                ResolveNamedType(info.Name) with { IsPointer = true },
                info.Declaration.Line,
                info.Declaration.Column,
                true),
            reportDuplicates: false);

        return scope;
    }

    private void PopulateTypeMembers(Mql5BinderTypeInfo info, Mql5BinderScope scope, int depth)
    {
        if (depth > 8)
        {
            _depthLimitHits++;
            return;
        }

        // A member scope is built on demand and can therefore be built from outside the
        // walk over the type's own body, so the type's parameters are brought back into
        // scope here rather than relied on being there already.
        PushTypeParameters(info.Declaration.TypeParameters);

        foreach (KeyValuePair<string, Mql5IrField> pair in info.Fields)
        {
            Declare(
                scope,
                new Mql5ResolvedSymbol(
                    Mql5SymbolKind.Field,
                    pair.Key,
                    ResolveTypeReference(pair.Value.Type, report: false, extraRank: pair.Value.ArrayRanks.Count),
                    pair.Value.Line,
                    pair.Value.Column,
                    true),
                reportDuplicates: false);
        }

        foreach (KeyValuePair<string, List<Mql5IrFunction>> pair in info.Methods)
        {
            Mql5IrFunction first = pair.Value[0];
            Declare(
                scope,
                new Mql5ResolvedSymbol(
                    Mql5SymbolKind.Method,
                    pair.Key,
                    ResolveTypeReference(first.ReturnType, report: false),
                    first.Line,
                    first.Column,
                    true),
                reportDuplicates: false);
        }

        if (info.BaseTypeName is not null
            && TryGetDeclaredType(info.BaseTypeName, out Mql5BinderTypeInfo? baseInfo))
        {
            PopulateTypeMembers(baseInfo, scope, depth + 1);
        }

        PopTypeParameters(info.Declaration.TypeParameters);
    }

    private void BindFunction(Mql5IrFunction function, Mql5BinderScope enclosing)
    {
        PushTypeParameters(function.TypeParameters);
        ResolveTypeReference(function.ReturnType, report: true);

        var scope = new Mql5BinderScope(enclosing);
        foreach (Mql5IrParameter parameter in function.Parameters)
        {
            Mql5ResolvedType parameterType = ResolveTypeReference(parameter.Type, report: true);
            if (parameter.DefaultValue is not null)
            {
                BindExpression(parameter.DefaultValue, enclosing, 0);
            }

            if (parameter.Name.Length == 0)
            {
                continue;
            }

            Declare(
                scope,
                new Mql5ResolvedSymbol(
                    Mql5SymbolKind.Parameter,
                    parameter.Name,
                    parameterType,
                    parameter.Line,
                    parameter.Column,
                    true),
                reportDuplicates: true);
        }

        if (function.Body is not null)
        {
            BindBlock(function.Body, scope, 0);
        }

        PopTypeParameters(function.TypeParameters);
    }

    // ------------------------------------------------------------------ statement walk

    private void BindBlock(Mql5IrBlockStatement block, Mql5BinderScope enclosing, int depth)
    {
        if (depth > Mql5Binder.MaxDepth)
        {
            _depthLimitHits++;
            return;
        }

        var scope = new Mql5BinderScope(enclosing);
        CollectPending(block.Statements, scope);

        foreach (Mql5IrStatement statement in block.Statements)
        {
            BindStatement(statement, scope, depth + 1);
        }
    }

    private static void CollectPending(IReadOnlyList<Mql5IrStatement> statements, Mql5BinderScope scope)
    {
        foreach (Mql5IrStatement statement in statements)
        {
            if (statement is not Mql5IrLocalDeclarationStatement declaration)
            {
                continue;
            }

            foreach (Mql5IrVariable variable in declaration.Variables)
            {
                scope.Pending.TryAdd(variable.Name, (variable.Line, variable.Column));
            }
        }
    }

    private void BindStatement(Mql5IrStatement statement, Mql5BinderScope scope, int depth)
    {
        if (depth > Mql5Binder.MaxDepth)
        {
            _depthLimitHits++;
            return;
        }

        switch (statement)
        {
            case Mql5IrBlockStatement block:
                BindBlock(block, scope, depth + 1);
                break;

            case Mql5IrLocalDeclarationStatement declaration:
                BindLocalDeclaration(declaration, scope, depth);
                break;

            case Mql5IrExpressionStatement expression:
                BindExpression(expression.Expression, scope, depth);
                break;

            case Mql5IrIfStatement branch:
                BindExpression(branch.Condition, scope, depth);
                BindStatement(branch.WhenTrue, scope, depth + 1);
                if (branch.WhenFalse is not null)
                {
                    BindStatement(branch.WhenFalse, scope, depth + 1);
                }

                break;

            case Mql5IrWhileStatement loop:
                BindExpression(loop.Condition, scope, depth);
                BindStatement(loop.Body, scope, depth + 1);
                break;

            case Mql5IrDoWhileStatement loop:
                BindStatement(loop.Body, scope, depth + 1);
                BindExpression(loop.Condition, scope, depth);
                break;

            case Mql5IrForStatement loop:
                BindFor(loop, scope, depth);
                break;

            case Mql5IrSwitchStatement selection:
                BindSwitch(selection, scope, depth);
                break;

            case Mql5IrReturnStatement returned:
                if (returned.Value is not null)
                {
                    BindExpression(returned.Value, scope, depth);
                }

                break;

            case Mql5IrDeleteStatement deleted:
                BindExpression(deleted.Operand, scope, depth);
                break;

            default:
                break;
        }
    }

    private void BindLocalDeclaration(
        Mql5IrLocalDeclarationStatement declaration,
        Mql5BinderScope scope,
        int depth)
    {
        Mql5ResolvedType declaredType = ResolveTypeReference(declaration.Type, report: true);

        foreach (Mql5IrVariable variable in declaration.Variables)
        {
            BindArrayRanks(variable.ArrayRanks, scope);
            if (variable.Initializer is not null)
            {
                BindExpression(variable.Initializer, scope, depth + 1);
            }

            Declare(
                scope,
                new Mql5ResolvedSymbol(
                    Mql5SymbolKind.LocalVariable,
                    variable.Name,
                    declaredType.WithArrayRank(declaredType.ArrayRank + variable.ArrayRanks.Count),
                    variable.Line,
                    variable.Column,
                    true),
                reportDuplicates: true);
        }
    }

    private void BindArrayRanks(IReadOnlyList<Mql5IrArrayRank> ranks, Mql5BinderScope scope)
    {
        foreach (Mql5IrArrayRank rank in ranks)
        {
            if (rank.Size is not null)
            {
                BindExpression(rank.Size, scope, 0);
            }
        }
    }

    private void BindFor(Mql5IrForStatement loop, Mql5BinderScope enclosing, int depth)
    {
        var scope = new Mql5BinderScope(enclosing);
        if (loop.Initializer is not null)
        {
            CollectPending([loop.Initializer], scope);
            BindStatement(loop.Initializer, scope, depth + 1);
        }

        if (loop.Condition is not null)
        {
            BindExpression(loop.Condition, scope, depth + 1);
        }

        if (loop.Increment is not null)
        {
            BindExpression(loop.Increment, scope, depth + 1);
        }

        BindStatement(loop.Body, scope, depth + 1);
    }

    private void BindSwitch(Mql5IrSwitchStatement selection, Mql5BinderScope enclosing, int depth)
    {
        BindExpression(selection.Subject, enclosing, depth);

        var scope = new Mql5BinderScope(enclosing);
        foreach (Mql5IrSwitchSection section in selection.Sections)
        {
            CollectPending(section.Statements, scope);
        }

        foreach (Mql5IrSwitchSection section in selection.Sections)
        {
            foreach (Mql5IrSwitchLabel label in section.Labels)
            {
                if (label.Value is not null)
                {
                    BindExpression(label.Value, scope, depth + 1);
                }
            }

            foreach (Mql5IrStatement statement in section.Statements)
            {
                BindStatement(statement, scope, depth + 1);
            }
        }
    }

    // ----------------------------------------------------------------- expression walk

    private Mql5ResolvedType BindExpression(Mql5IrExpression expression, Mql5BinderScope scope, int depth)
    {
        if (depth > Mql5Binder.MaxDepth)
        {
            _depthLimitHits++;
            return Mql5ResolvedType.Unknown;
        }

        _expressions++;

        Mql5ResolvedType type = expression switch
        {
            Mql5IrLiteralExpression literal => TypeOfLiteral(literal),
            Mql5IrNameExpression name => BindName(name, scope),
            Mql5IrUnaryExpression unary => BindUnary(unary, scope, depth),
            Mql5IrBinaryExpression binary => BindBinary(binary, scope, depth),
            Mql5IrAssignmentExpression assignment => BindAssignment(assignment, scope, depth),
            Mql5IrConditionalExpression conditional => BindConditional(conditional, scope, depth),
            Mql5IrCallExpression call => BindCall(call, scope, depth),
            Mql5IrIndexExpression index => BindIndex(index, scope, depth),
            Mql5IrMemberExpression member => BindMember(member, scope, depth),
            Mql5IrCastExpression cast => BindCast(cast, scope, depth),
            Mql5IrNewExpression created => ResolveTypeReference(created.Type, report: true) with { IsPointer = true },
            Mql5IrSizeOfExpression measurement => BindSizeOf(measurement, scope),
            Mql5IrTypeNameExpression typeName => BindTypeName(typeName, scope, depth),
            Mql5IrInitializerListExpression initializer => BindInitializerList(initializer, scope, depth),
            _ => Mql5ResolvedType.Unknown
        };

        _expressionTypes[expression] = type;
        if (type.IsResolved)
        {
            _typedExpressions++;
        }

        return type;
    }


    /// <summary>
    /// Types the <c>sizeof</c> operator, whose own result is an integer whichever way its
    /// operand reads.
    ///
    /// The work here is settling what is being measured. MQL5 lets <c>sizeof</c> take a
    /// variable as readily as a type, and an undecorated bare name is both grammars at
    /// once, so the parser records the name twice and the answer is decided here against
    /// the symbol table: a name that denotes a type is measured as a type, and a name that
    /// resolves to something else is measured as that value, which is what makes
    /// <c>char post[]; sizeof(post)</c> a dynamic array rather than a structure nobody
    /// declared. The lookup is deliberately silent — it never reports, because a name that
    /// resolves to neither is a type reference we simply could not place, and the operator
    /// is not the right place to complain about it.
    /// </summary>
    private Mql5ResolvedType BindSizeOf(Mql5IrSizeOfExpression measurement, Mql5BinderScope scope)
    {
        if (measurement.Operand is Mql5IrNameExpression name
            && !name.IsScopeQualified
            && !DenotesAType(name.Name))
        {
            for (Mql5BinderScope? current = scope; current is not null; current = current.Parent)
            {
                if (current.Symbols.TryGetValue(name.Name, out Mql5ResolvedSymbol? found))
                {
                    _symbols[name] = found;
                    _expressionTypes[name] = found.Type;
                    break;
                }
            }
        }

        return Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Whole32);
    }

    /// <summary>
    /// Types the <c>typename</c> operator, which always yields a <c>string</c> — the
    /// compiler warns about an implicit conversion when the result is stored in an
    /// <c>int</c>, which is how the return type was established.
    ///
    /// The operand is bound as a value only when it really is one. MQL5 accepts a bare
    /// name for either a type or a variable, and the parser cannot tell them apart, so
    /// the decision lands here: a name that denotes a type — a template parameter, a
    /// declared type or enumeration, or a built-in one — is recorded as a type reference
    /// rather than reported as a name that refers to nothing.
    /// </summary>
    private Mql5ResolvedType BindTypeName(Mql5IrTypeNameExpression typeName, Mql5BinderScope scope, int depth)
    {
        if (typeName.Type is not null)
        {
            ResolveTypeReference(typeName.Type, report: true);
            return Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Text);
        }

        if (typeName.Operand is Mql5IrNameExpression name
            && !name.IsScopeQualified
            && DenotesAType(name.Name))
        {
            _symbols[name] = new Mql5ResolvedSymbol(
                Mql5SymbolKind.TypeName,
                name.Name,
                Mql5ResolvedType.Unknown,
                name.Line,
                name.Column,
                true);
            _expressionTypes[name] = Mql5ResolvedType.Unknown;
            return Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Text);
        }

        if (typeName.Operand is not null)
        {
            BindExpression(typeName.Operand, scope, depth + 1);
        }

        return Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Text);
    }

    /// <summary>True when a bare name names a type rather than a value.</summary>
    private bool DenotesAType(string name) =>
        _typeParameters.ContainsKey(name)
        || _types.ContainsKey(name)
        || _enums.ContainsKey(name)
        || Mql5BinderRuntime.IsBuiltinType(name)
        || Mql5BinderRuntime.IsBuiltinEnumeration(name);
    private static Mql5ResolvedType TypeOfLiteral(Mql5IrLiteralExpression literal) => literal.LiteralKind switch
    {
        Mql5LiteralKind.Whole => literal.FoldedValue is long value and >= int.MinValue and <= int.MaxValue
            ? Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Whole32)
            : Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Whole64),
        Mql5LiteralKind.Real => Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Real64),
        Mql5LiteralKind.Text => Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Text),
        Mql5LiteralKind.Character => Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Natural16),
        Mql5LiteralKind.Boolean => Mql5ResolvedType.Logical,
        Mql5LiteralKind.Colour => Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Colour),
        Mql5LiteralKind.DateTime => Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Moment),
        Mql5LiteralKind.Null => Mql5ResolvedType.Null,
        _ => Mql5ResolvedType.Unknown
    };

    private Mql5ResolvedType BindUnary(Mql5IrUnaryExpression unary, Mql5BinderScope scope, int depth)
    {
        Mql5ResolvedType operand = BindExpression(unary.Operand, scope, depth + 1);
        return unary.Operator switch
        {
            "!" => Mql5ResolvedType.Logical,
            "-" or "+" or "~" => Promote(operand),
            _ => operand
        };
    }

    private Mql5ResolvedType BindBinary(Mql5IrBinaryExpression binary, Mql5BinderScope scope, int depth)
    {
        Mql5ResolvedType left = BindExpression(binary.Left, scope, depth + 1);
        Mql5ResolvedType right = BindExpression(binary.Right, scope, depth + 1);

        switch (binary.Operator)
        {
            case "&&":
            case "||":
            case "==":
            case "!=":
            case "<":
            case ">":
            case "<=":
            case ">=":
                return Mql5ResolvedType.Logical;

            case "+":
                if (left.Scalar == Mql5IrScalarKind.Text || right.Scalar == Mql5IrScalarKind.Text)
                {
                    return Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Text);
                }

                return UsualArithmetic(left, right, preserveMoment: true);

            case "-":
                return UsualArithmetic(left, right, preserveMoment: true);

            case "*":
            case "/":
            case "%":
            case "&":
            case "|":
            case "^":
                return UsualArithmetic(left, right, preserveMoment: false);

            case "<<":
            case ">>":
                return Promote(left);

            case ",":
                return right;

            default:
                return Mql5ResolvedType.Unknown;
        }
    }

    private Mql5ResolvedType BindAssignment(
        Mql5IrAssignmentExpression assignment,
        Mql5BinderScope scope,
        int depth)
    {
        Mql5ResolvedType target = BindExpression(assignment.Target, scope, depth + 1);
        BindExpression(assignment.Value, scope, depth + 1);
        return target;
    }

    private Mql5ResolvedType BindConditional(
        Mql5IrConditionalExpression conditional,
        Mql5BinderScope scope,
        int depth)
    {
        BindExpression(conditional.Condition, scope, depth + 1);
        Mql5ResolvedType whenTrue = BindExpression(conditional.WhenTrue, scope, depth + 1);
        Mql5ResolvedType whenFalse = BindExpression(conditional.WhenFalse, scope, depth + 1);
        return CommonType(whenTrue, whenFalse);
    }

    private Mql5ResolvedType BindIndex(Mql5IrIndexExpression index, Mql5BinderScope scope, int depth)
    {
        Mql5ResolvedType target = BindExpression(index.Target, scope, depth + 1);
        BindExpression(index.Index, scope, depth + 1);
        return target.ElementType();
    }

    private Mql5ResolvedType BindCast(Mql5IrCastExpression cast, Mql5BinderScope scope, int depth)
    {
        BindExpression(cast.Operand, scope, depth + 1);
        return ResolveTypeReference(cast.Type, report: true);
    }

    private Mql5ResolvedType BindInitializerList(
        Mql5IrInitializerListExpression initializer,
        Mql5BinderScope scope,
        int depth)
    {
        Mql5ResolvedType element = Mql5ResolvedType.Unknown;
        bool first = true;
        foreach (Mql5IrExpression item in initializer.Items)
        {
            Mql5ResolvedType itemType = BindExpression(item, scope, depth + 1);
            element = first ? itemType : CommonType(element, itemType);
            first = false;
        }

        return element.IsResolved ? element.WithArrayRank(element.ArrayRank + 1) : Mql5ResolvedType.Unknown;
    }

    // ------------------------------------------------------------------- name binding

    private Mql5ResolvedType BindName(Mql5IrNameExpression name, Mql5BinderScope scope)
    {
        _nameExpressions++;

        Mql5ResolvedSymbol symbol = ResolveName(name, scope, asCallee: false);
        _symbols[name] = symbol;

        if (symbol.IsResolved)
        {
            _resolvedNames++;
        }
        else
        {
            _unresolvedNames++;
        }

        return symbol.Type;
    }

    private Mql5ResolvedSymbol ResolveName(Mql5IrNameExpression name, Mql5BinderScope scope, bool asCallee)
    {
        if (name.IsScopeQualified && name.Scope.Count > 0)
        {
            return ResolveQualifiedName(name, asCallee);
        }

        Mql5BinderScope? start = name.IsScopeQualified ? _globalScope : scope;
        for (Mql5BinderScope? current = start; current is not null; current = current.Parent)
        {
            if (current.Symbols.TryGetValue(name.Name, out Mql5ResolvedSymbol? found))
            {
                return found;
            }
        }

        for (Mql5BinderScope? current = start; current is not null; current = current.Parent)
        {
            if (current.Pending.TryGetValue(name.Name, out (int Line, int Column) position))
            {
                _useBeforeDeclarations++;
                Report(
                    Mql5BindDiagnosticCodes.UseBeforeDeclaration,
                    Mql5RestrictedDiagnosticSeverity.Error,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"'{name.Name}' is used before its declaration at line {position.Line}."),
                    name.Line,
                    name.Column,
                    name.Name);

                return new Mql5ResolvedSymbol(
                    Mql5SymbolKind.LocalVariable,
                    name.Name,
                    Mql5ResolvedType.Unknown,
                    position.Line,
                    position.Column,
                    true);
            }
        }

        return ResolveBuiltinName(name, asCallee);
    }

    private Mql5ResolvedSymbol ResolveBuiltinName(Mql5IrNameExpression name, bool asCallee)
    {
        if (!asCallee && Mql5BinderRuntime.TryGetBuiltinConstant(name.Name, out long value))
        {
            _builtinConstantReferences++;
            return new Mql5ResolvedSymbol(
                Mql5SymbolKind.BuiltinConstant,
                name.Name,
                value is >= int.MinValue and <= int.MaxValue
                    ? Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Whole32)
                    : Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Whole64),
                name.Line,
                name.Column,
                true);
        }

        if (Mql5BinderRuntime.IsBuiltinFunction(name.Name))
        {
            _builtinFunctionReferences++;
            ReportUnimplementedBuiltin(name.Name, name.Line, name.Column);
            return new Mql5ResolvedSymbol(
                Mql5SymbolKind.BuiltinFunction,
                name.Name,
                Mql5ResolvedType.Unknown,
                name.Line,
                name.Column,
                false);
        }

        if (Mql5BinderRuntime.IsBuiltinType(name.Name)
            || Mql5BinderRuntime.IsBuiltinEnumeration(name.Name)
            || Mql5BinderFallback.TryGetScalarKeyword(name.Name, out _))
        {
            return new Mql5ResolvedSymbol(
                Mql5SymbolKind.BuiltinType,
                name.Name,
                new Mql5ResolvedType(
                    Mql5ResolvedTypeKind.TypeName, Mql5IrScalarKind.None, name.Name, 0, false, true),
                name.Line,
                name.Column,
                false);
        }

        if (!asCallee)
        {
            Report(
                Mql5BindDiagnosticCodes.UnresolvedName,
                Mql5RestrictedDiagnosticSeverity.Error,
                "'" + name.Name + "' refers to no declaration, input, enumeration member or built-in.",
                name.Line,
                name.Column,
                name.Name);
        }

        return Mql5ResolvedSymbol.ForUnresolved(name.Name, name.Line, name.Column);
    }

    private Mql5ResolvedSymbol ResolveQualifiedName(Mql5IrNameExpression name, bool asCallee)
    {
        string qualifier = name.Scope[^1];

        if (TryGetDeclaredType(qualifier, out Mql5BinderTypeInfo? info))
        {
            Mql5IrField? field = FindField(info, name.Name);
            if (field is not null)
            {
                return new Mql5ResolvedSymbol(
                    Mql5SymbolKind.Field,
                    name.Name,
                    ResolveTypeReference(field.Type, report: false, extraRank: field.ArrayRanks.Count),
                    field.Line,
                    field.Column,
                    true);
            }

            List<Mql5IrFunction>? methods = FindMethods(info, name.Name);
            if (methods is not null)
            {
                return new Mql5ResolvedSymbol(
                    Mql5SymbolKind.Method,
                    name.Name,
                    ResolveTypeReference(methods[0].ReturnType, report: false),
                    methods[0].Line,
                    methods[0].Column,
                    true);
            }
        }

        if (_enums.TryGetValue(qualifier, out Mql5IrEnumeration? enumeration))
        {
            foreach (Mql5IrEnumMember member in enumeration.Members)
            {
                if (string.Equals(member.Name, name.Name, StringComparison.Ordinal))
                {
                    return new Mql5ResolvedSymbol(
                        Mql5SymbolKind.EnumMember,
                        name.Name,
                        EnumerationType(qualifier),
                        member.Line,
                        member.Column,
                        true);
                }
            }
        }

        if (Mql5BinderRuntime.IsBuiltinType(qualifier) || Mql5BinderRuntime.IsBuiltinEnumeration(qualifier))
        {
            _unknownFromMemberAccess++;
            ReportUnimplementedBuiltin(qualifier + "::" + name.Name, name.Line, name.Column);
            return new Mql5ResolvedSymbol(
                asCallee ? Mql5SymbolKind.BuiltinFunction : Mql5SymbolKind.BuiltinConstant,
                name.Name,
                Mql5ResolvedType.Unknown,
                name.Line,
                name.Column,
                false);
        }

        if (!asCallee)
        {
            Report(
                Mql5BindDiagnosticCodes.UnresolvedName,
                Mql5RestrictedDiagnosticSeverity.Error,
                "'" + qualifier + "::" + name.Name + "' refers to no declaration or built-in.",
                name.Line,
                name.Column,
                qualifier + "::" + name.Name);
        }

        return Mql5ResolvedSymbol.ForUnresolved(name.Name, name.Line, name.Column);
    }

    private Mql5IrField? FindField(Mql5BinderTypeInfo info, string member)
    {
        Mql5BinderTypeInfo? current = info;
        for (int guard = 0; current is not null && guard < 32; guard++)
        {
            if (current.Fields.TryGetValue(member, out Mql5IrField? field))
            {
                return field;
            }

            current = current.BaseTypeName is not null
                && TryGetDeclaredType(current.BaseTypeName, out Mql5BinderTypeInfo? next)
                    ? next
                    : null;
        }

        return null;
    }

    private List<Mql5IrFunction>? FindMethods(Mql5BinderTypeInfo info, string member)
    {
        Mql5BinderTypeInfo? current = info;
        for (int guard = 0; current is not null && guard < 32; guard++)
        {
            if (current.Methods.TryGetValue(member, out List<Mql5IrFunction>? methods))
            {
                return methods;
            }

            current = current.BaseTypeName is not null
                && TryGetDeclaredType(current.BaseTypeName, out Mql5BinderTypeInfo? next)
                    ? next
                    : null;
        }

        return null;
    }

    /// <summary>True when every type in the base chain of <paramref name="info"/> is known.</summary>
    private bool HasCompleteBaseChain(Mql5BinderTypeInfo info)
    {
        Mql5BinderTypeInfo? current = info;
        for (int guard = 0; current is not null && guard < 32; guard++)
        {
            if (current.BaseTypeName is null)
            {
                return true;
            }

            if (!TryGetDeclaredType(current.BaseTypeName, out Mql5BinderTypeInfo? next))
            {
                return false;
            }

            current = next;
        }

        return false;
    }

    // ----------------------------------------------------------------- member binding

    private Mql5ResolvedType BindMember(Mql5IrMemberExpression member, Mql5BinderScope scope, int depth)
    {
        Mql5ResolvedType target = BindExpression(member.Target, scope, depth + 1);
        Mql5ResolvedSymbol? symbol = ResolveMember(target, member);

        if (symbol is not null)
        {
            _symbols[member] = symbol;
            return symbol.Type;
        }

        _unknownFromMemberAccess++;
        return Mql5ResolvedType.Unknown;
    }

    private Mql5ResolvedSymbol? ResolveMember(Mql5ResolvedType target, Mql5IrMemberExpression member)
    {
        if (target.ArrayRank > 0
            || target.Kind is not (Mql5ResolvedTypeKind.Structure or Mql5ResolvedTypeKind.Class))
        {
            return null;
        }

        if (!TryGetDeclaredType(target.Name, out Mql5BinderTypeInfo? info))
        {
            if (target.IsBuiltin)
            {
                ReportUnimplementedBuiltin(target.Name + "." + member.Member, member.Line, member.Column);
            }

            return null;
        }

        Mql5IrField? field = FindField(info, member.Member);
        if (field is not null)
        {
            return new Mql5ResolvedSymbol(
                Mql5SymbolKind.Field,
                member.Member,
                ResolveTypeReference(field.Type, report: false, extraRank: field.ArrayRanks.Count),
                field.Line,
                field.Column,
                true);
        }

        List<Mql5IrFunction>? methods = FindMethods(info, member.Member);
        if (methods is not null)
        {
            return new Mql5ResolvedSymbol(
                Mql5SymbolKind.Method,
                member.Member,
                ResolveTypeReference(methods[0].ReturnType, report: false),
                methods[0].Line,
                methods[0].Column,
                true);
        }

        // Only a type whose whole base chain is known can prove a member absent.
        if (HasCompleteBaseChain(info))
        {
            Report(
                Mql5BindDiagnosticCodes.UnresolvedName,
                Mql5RestrictedDiagnosticSeverity.Error,
                "'" + target.Name + "' declares no member named '" + member.Member + "'.",
                member.Line,
                member.Column,
                target.Name + "." + member.Member);
        }

        return null;
    }

    // ------------------------------------------------------------------- call binding

    private Mql5ResolvedType BindCall(Mql5IrCallExpression call, Mql5BinderScope scope, int depth)
    {
        _calls++;

        foreach (Mql5IrExpression argument in call.Arguments)
        {
            BindExpression(argument, scope, depth + 1);
        }

        int arguments = call.Arguments.Count;

        switch (call.Callee)
        {
            case Mql5IrNameExpression name:
                return BindNamedCall(call, name, scope, arguments);

            case Mql5IrMemberExpression member:
                return BindMemberCall(call, member, scope, depth, arguments);

            default:
                BindExpression(call.Callee, scope, depth + 1);
                _unresolvedCalls++;
                return Mql5ResolvedType.Unknown;
        }
    }

    private Mql5ResolvedType BindNamedCall(
        Mql5IrCallExpression call,
        Mql5IrNameExpression name,
        Mql5BinderScope scope,
        int arguments)
    {
        _nameExpressions++;
        Mql5ResolvedSymbol symbol = ResolveName(name, scope, asCallee: true);
        _symbols[name] = symbol;
        _expressionTypes[name] = symbol.Type;

        if (!symbol.IsResolved)
        {
            _unresolvedNames++;
            _unresolvedCalls++;
            Report(
                Mql5BindDiagnosticCodes.UnresolvedCall,
                Mql5RestrictedDiagnosticSeverity.Error,
                "'" + name.Name + "' names no function, method or built-in that can be called.",
                call.Line,
                call.Column,
                name.Name);
            return Mql5ResolvedType.Unknown;
        }

        _resolvedNames++;
        _resolvedCalls++;

        switch (symbol.Kind)
        {
            case Mql5SymbolKind.Function:
                if (_functions.TryGetValue(name.Name, out List<Mql5IrFunction>? overloads))
                {
                    return CheckOverloads(overloads, arguments, name.Name, call.Line, call.Column);
                }

                return symbol.Type;

            case Mql5SymbolKind.Method:
                if (_currentType is not null)
                {
                    List<Mql5IrFunction>? methods = FindMethods(_currentType, name.Name);
                    if (methods is not null)
                    {
                        return CheckOverloads(methods, arguments, name.Name, call.Line, call.Column);
                    }
                }

                return symbol.Type;

            case Mql5SymbolKind.BuiltinFunction:
                CheckBuiltinArity(name.Name, arguments, call.Line, call.Column);
                _unknownFromBuiltinCall++;
                return Mql5ResolvedType.Unknown;

            case Mql5SymbolKind.TypeName:
            case Mql5SymbolKind.EnumerationName:
            case Mql5SymbolKind.BuiltinType:
                // A type name in call position is a conversion or a constructor.
                return _enums.ContainsKey(name.Name)
                    ? EnumerationType(name.Name)
                    : ResolveNamedType(name.Name);

            default:
                // A variable holding a function pointer: the call yields nothing we can name.
                return Mql5ResolvedType.Unknown;
        }
    }

    private Mql5ResolvedType ResolveNamedType(string name)
    {
        // MQL5 spells a conversion as a call: string(x), int(x), datetime(x).
        if (Mql5BinderFallback.TryGetScalarKeyword(name, out Mql5IrScalarKind scalar))
        {
            return Mql5ResolvedType.ForScalar(scalar);
        }

        if (TryGetDeclaredType(name, out Mql5BinderTypeInfo? info))
        {
            Mql5ResolvedTypeKind kind = string.Equals(info.Keyword, "struct", StringComparison.Ordinal)
                ? Mql5ResolvedTypeKind.Structure
                : Mql5ResolvedTypeKind.Class;
            return new Mql5ResolvedType(kind, Mql5IrScalarKind.None, name, 0, false, false);
        }

        if (Mql5BinderRuntime.IsBuiltinEnumeration(name))
        {
            return new Mql5ResolvedType(
                Mql5ResolvedTypeKind.Enumeration, Mql5IrScalarKind.Whole32, name, 0, false, true);
        }

        if (Mql5BinderRuntime.IsBuiltinType(name))
        {
            return new Mql5ResolvedType(
                Mql5ResolvedTypeKind.Structure, Mql5IrScalarKind.None, name, 0, false, true);
        }

        return Mql5ResolvedType.Unknown;
    }

    private Mql5ResolvedType BindMemberCall(
        Mql5IrCallExpression call,
        Mql5IrMemberExpression member,
        Mql5BinderScope scope,
        int depth,
        int arguments)
    {
        Mql5ResolvedType target = BindExpression(member.Target, scope, depth + 1);

        if (target.ArrayRank == 0
            && target.Kind is Mql5ResolvedTypeKind.Structure or Mql5ResolvedTypeKind.Class
            && TryGetDeclaredType(target.Name, out Mql5BinderTypeInfo? info))
        {
            List<Mql5IrFunction>? methods = FindMethods(info, member.Member);
            if (methods is not null)
            {
                _resolvedCalls++;
                _symbols[member] = new Mql5ResolvedSymbol(
                    Mql5SymbolKind.Method,
                    member.Member,
                    ResolveTypeReference(methods[0].ReturnType, report: false),
                    methods[0].Line,
                    methods[0].Column,
                    true);
                Mql5ResolvedType result = CheckOverloads(
                    methods, arguments, target.Name + "." + member.Member, call.Line, call.Column);
                _expressionTypes[member] = result;
                return result;
            }

            if (HasCompleteBaseChain(info))
            {
                _unresolvedCalls++;
                Report(
                    Mql5BindDiagnosticCodes.UnresolvedCall,
                    Mql5RestrictedDiagnosticSeverity.Error,
                    "'" + target.Name + "' declares no method named '" + member.Member + "'.",
                    call.Line,
                    call.Column,
                    target.Name + "." + member.Member);
                _expressionTypes[member] = Mql5ResolvedType.Unknown;
                return Mql5ResolvedType.Unknown;
            }
        }
        else if (target.IsBuiltin && target.Kind is Mql5ResolvedTypeKind.Structure or Mql5ResolvedTypeKind.Class)
        {
            ReportUnimplementedBuiltin(target.Name + "." + member.Member, call.Line, call.Column);
        }

        _unknownFromBuiltinCall++;
        _expressionTypes[member] = Mql5ResolvedType.Unknown;
        return Mql5ResolvedType.Unknown;
    }

    private Mql5ResolvedType CheckOverloads(
        List<Mql5IrFunction> overloads,
        int arguments,
        string display,
        int line,
        int column)
    {
        int matches = 0;
        Mql5IrFunction? match = null;
        int minimum = int.MaxValue;
        int maximum = 0;

        foreach (Mql5IrFunction candidate in overloads)
        {
            int required = 0;
            foreach (Mql5IrParameter parameter in candidate.Parameters)
            {
                if (parameter.DefaultValue is not null)
                {
                    break;
                }

                required++;
            }

            int allowed = candidate.Parameters.Count;
            minimum = Math.Min(minimum, required);
            maximum = Math.Max(maximum, allowed);

            if (arguments >= required && arguments <= allowed)
            {
                matches++;
                match ??= candidate;
            }
        }

        if (matches == 0)
        {
            // A user function may share a built-in's name, and then the call is resolved
            // across both sets by argument count. MetaEditor compiles
            // 'double MathRound(double,double)' alongside the one-argument built-in, and
            // the corpus relies on it — the user overload's own body calls the built-in
            // form. So an argument count no user overload accepts is not yet wrong; only
            // one the built-in refuses too is.
            if (Mql5BinderRuntime.TryCheckBuiltinArity(display, arguments, out bool builtinAccepts, out _)
                && builtinAccepts)
            {
                _unknownFromBuiltinCall++;
                return Mql5ResolvedType.Unknown;
            }

            _arityMismatches++;
            Report(
                Mql5BindDiagnosticCodes.ArityMismatch,
                Mql5RestrictedDiagnosticSeverity.Error,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{display}' takes {DescribeArity(minimum, maximum)} but was called with {arguments}."),
                line,
                column,
                display);
            return overloads.Count == 1
                ? ResolveTypeReference(overloads[0].ReturnType, report: false)
                : Mql5ResolvedType.Unknown;
        }

        if (matches > 1)
        {
            // Overload ranking by argument type is out of scope: an ambiguous match is
            // accepted, and the first candidate supplies the return type.
            _ambiguousCalls++;
        }

        return ResolveTypeReference(match!.ReturnType, report: false);
    }

    private static string DescribeArity(int minimum, int maximum)
    {
        if (minimum == int.MaxValue)
        {
            minimum = 0;
        }

        return minimum == maximum
            ? string.Create(CultureInfo.InvariantCulture, $"{minimum} argument(s)")
            : string.Create(CultureInfo.InvariantCulture, $"{minimum} to {maximum} argument(s)");
    }

    private void CheckBuiltinArity(string name, int arguments, int line, int column)
    {
        if (!Mql5BinderRuntime.TryCheckBuiltinArity(name, arguments, out bool accepted, out string expected)
            || accepted)
        {
            return;
        }

        _arityMismatches++;
        Report(
            Mql5BindDiagnosticCodes.ArityMismatch,
            Mql5RestrictedDiagnosticSeverity.Error,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Built-in '{name}' takes {expected} but was called with {arguments}."),
            line,
            column,
            name);
    }

    // -------------------------------------------------------------- type arithmetic

    private static Mql5ResolvedType Promote(Mql5ResolvedType type)
    {
        if (!type.IsResolved || type.ArrayRank > 0)
        {
            return Mql5ResolvedType.Unknown;
        }

        if (type.Kind == Mql5ResolvedTypeKind.Enumeration)
        {
            return Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Whole32);
        }

        return type.Scalar switch
        {
            Mql5IrScalarKind.Logical
                or Mql5IrScalarKind.Whole8
                or Mql5IrScalarKind.Natural8
                or Mql5IrScalarKind.Whole16
                or Mql5IrScalarKind.Natural16
                or Mql5IrScalarKind.Colour => Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Whole32),
            Mql5IrScalarKind.Moment => Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Whole64),
            Mql5IrScalarKind.Text or Mql5IrScalarKind.Void or Mql5IrScalarKind.None =>
                Mql5ResolvedType.Unknown,
            _ => type
        };
    }

    private static Mql5ResolvedType UsualArithmetic(
        Mql5ResolvedType left,
        Mql5ResolvedType right,
        bool preserveMoment)
    {
        if (!left.IsResolved || !right.IsResolved || left.ArrayRank > 0 || right.ArrayRank > 0)
        {
            return Mql5ResolvedType.Unknown;
        }

        if (preserveMoment
            && (left.Scalar == Mql5IrScalarKind.Moment || right.Scalar == Mql5IrScalarKind.Moment))
        {
            bool bothMoments = left.Scalar == Mql5IrScalarKind.Moment && right.Scalar == Mql5IrScalarKind.Moment;
            return bothMoments
                ? Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Whole64)
                : Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Moment);
        }

        Mql5ResolvedType promotedLeft = Promote(left);
        Mql5ResolvedType promotedRight = Promote(right);
        if (!promotedLeft.IsResolved || !promotedRight.IsResolved)
        {
            return Mql5ResolvedType.Unknown;
        }

        Mql5IrScalarKind a = promotedLeft.Scalar;
        Mql5IrScalarKind b = promotedRight.Scalar;

        if (a == Mql5IrScalarKind.Real64 || b == Mql5IrScalarKind.Real64)
        {
            return Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Real64);
        }

        if (a == Mql5IrScalarKind.Real32 || b == Mql5IrScalarKind.Real32)
        {
            return Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Real32);
        }

        if (a == Mql5IrScalarKind.Natural64 || b == Mql5IrScalarKind.Natural64)
        {
            return Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Natural64);
        }

        if (a == Mql5IrScalarKind.Whole64 || b == Mql5IrScalarKind.Whole64)
        {
            return Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Whole64);
        }

        if (a == Mql5IrScalarKind.Natural32 || b == Mql5IrScalarKind.Natural32)
        {
            return Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Natural32);
        }

        return Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Whole32);
    }

    private static Mql5ResolvedType CommonType(Mql5ResolvedType left, Mql5ResolvedType right)
    {
        if (left == right)
        {
            return left;
        }

        if (!left.IsResolved || !right.IsResolved)
        {
            return Mql5ResolvedType.Unknown;
        }

        if (left.Kind == Mql5ResolvedTypeKind.NullLiteral)
        {
            return right;
        }

        if (right.Kind == Mql5ResolvedTypeKind.NullLiteral)
        {
            return left;
        }

        if (left.Scalar == Mql5IrScalarKind.Text && right.Scalar == Mql5IrScalarKind.Text)
        {
            return left;
        }

        return left.IsArithmetic && right.IsArithmetic
            ? UsualArithmetic(left, right, preserveMoment: true)
            : Mql5ResolvedType.Unknown;
    }

    // ---------------------------------------------------------------- diagnostics

    private void ReportUnimplementedBuiltin(string name, int line, int column)
    {
        if (!_unimplementedBuiltins.Add(name))
        {
            return;
        }

        Report(
            Mql5BindDiagnosticCodes.UnsupportedBuiltin,
            Mql5RestrictedDiagnosticSeverity.Information,
            "'" + name + "' is a known MQL5 built-in that this runtime does not implement; "
                + "the source is valid, the gap is ours.",
            line,
            column,
            name);
    }

    private void Report(
        string code,
        Mql5RestrictedDiagnosticSeverity severity,
        string message,
        int line,
        int column,
        string subject)
    {
        string key = severity == Mql5RestrictedDiagnosticSeverity.Information
            ? code + "|" + subject
            : string.Create(CultureInfo.InvariantCulture, $"{code}|{subject}|{line}|{column}");

        if (!_reported.Add(key))
        {
            return;
        }

        if (_diagnostics.Count >= Mql5Binder.MaxDiagnostics)
        {
            return;
        }

        _diagnostics.Add(new Mql5RestrictedDiagnostic(code, severity, message, line, column));
    }
}

// ------------------------------------------------------------- built-in knowledge

/// <summary>
/// The binder's view of the MQL5 standard runtime.
///
/// The authoritative source is the built-in catalog owned by the catalog module. It
/// is reached reflectively so that this pass compiles and runs whether or not that
/// catalog is present in the assembly, and picks it up automatically the moment it
/// is. When it is absent the binder falls back to the embedded name sets below,
/// which name built-ins without describing their signatures; in that mode no arity
/// checking of built-ins is attempted, and <see cref="CatalogAvailable"/> is false
/// so the difference can be reported rather than hidden.
/// </summary>
internal static class Mql5BinderRuntime
{
    private static readonly Func<string, bool>? IsKnownFunc;
    private static readonly MethodInfo? TryGetOverloadsMethod;
    private static readonly MethodInfo? TryGetConstantMethod;
    private static readonly Dictionary<string, Mql5BinderArity?> ArityCache = new(StringComparer.Ordinal);
    private static readonly object ArityGate = new();

    /// <summary>True when the authoritative built-in catalog was found in this assembly.</summary>
    public static bool CatalogAvailable { get; }

    static Mql5BinderRuntime()
    {
        try
        {
            Assembly assembly = typeof(Mql5BinderRuntime).Assembly;
            Type? catalog = assembly.GetType("YO4X.StrategyGovernance.Mql5BuiltinCatalog", throwOnError: false);
            if (catalog is not null)
            {
                MethodInfo? isKnown = catalog.GetMethod(
                    "IsKnown", BindingFlags.Public | BindingFlags.Static, [typeof(string)]);
                if (isKnown is not null && isKnown.ReturnType == typeof(bool))
                {
                    IsKnownFunc = (Func<string, bool>)Delegate.CreateDelegate(typeof(Func<string, bool>), isKnown);
                }

                MethodInfo? tryGet = catalog.GetMethod("TryGet", BindingFlags.Public | BindingFlags.Static);
                if (tryGet is not null
                    && tryGet.ReturnType == typeof(bool)
                    && tryGet.GetParameters().Length == 2)
                {
                    TryGetOverloadsMethod = tryGet;
                }
            }

            Type? constants = assembly.GetType(
                "YO4X.StrategyGovernance.Mql5BuiltinConstants", throwOnError: false);
            MethodInfo? tryGetValue = constants?.GetMethod(
                "TryGetValue", BindingFlags.Public | BindingFlags.Static);
            if (tryGetValue is not null
                && tryGetValue.ReturnType == typeof(bool)
                && tryGetValue.GetParameters().Length == 2)
            {
                TryGetConstantMethod = tryGetValue;
            }

            CatalogAvailable = IsKnownFunc is not null;
        }
#pragma warning disable CA1031 // A missing or differently shaped catalog must degrade, never fail.
        catch (Exception)
#pragma warning restore CA1031
        {
            CatalogAvailable = false;
        }
    }

    /// <summary>True when <paramref name="name"/> is a function of the MQL5 runtime.</summary>
    public static bool IsBuiltinFunction(string name)
    {
        if (IsKnownFunc is not null)
        {
            try
            {
                // The catalog is authoritative when present, including about absence:
                // it deliberately omits MQL4 carry-overs, and the binder must not
                // resurrect them from its own fallback set.
                return IsKnownFunc(name);
            }
#pragma warning disable CA1031
            catch (Exception)
#pragma warning restore CA1031
            {
                // Fall through to the embedded set.
            }
        }

        return Mql5BinderFallback.Functions.Contains(name);
    }

    /// <summary>True when <paramref name="name"/> is a named constant of the MQL5 runtime.</summary>
    public static bool TryGetBuiltinConstant(string name, out long value)
    {
        value = 0;

        if (TryGetConstantMethod is not null)
        {
            try
            {
                object?[] arguments = [name, 0L];
                if (TryGetConstantMethod.Invoke(null, arguments) is true)
                {
                    value = arguments[1] is long parsed ? parsed : 0L;
                    return true;
                }

                return false;
            }
#pragma warning disable CA1031
            catch (Exception)
#pragma warning restore CA1031
            {
                // Fall through to the embedded set.
            }
        }

        return Mql5BinderFallback.IsConstant(name);
    }

    /// <summary>True when <paramref name="name"/> is a struct or class of the MQL5 runtime or library.</summary>
    public static bool IsBuiltinType(string name) => Mql5BinderFallback.IsType(name);

    /// <summary>True when <paramref name="name"/> is an enumeration of the MQL5 runtime.</summary>
    public static bool IsBuiltinEnumeration(string name) =>
        name.StartsWith("ENUM_", StringComparison.Ordinal);

    /// <summary>
    /// Checks one built-in call's argument count against the catalog.
    ///
    /// Returns false when no check is possible — the catalog is absent, its signature
    /// shape cannot be read, or the entry is marked unverified. An unverified
    /// signature is treated as no signature at all: a wrong arity check is worse than
    /// an absent one, because it turns a correct program into a reported error.
    /// </summary>
    public static bool TryCheckBuiltinArity(string name, int arguments, out bool accepted, out string expected)
    {
        accepted = true;
        expected = string.Empty;

        if (TryGetOverloadsMethod is null)
        {
            return false;
        }

        Mql5BinderArity? arity;
        lock (ArityGate)
        {
            if (!ArityCache.TryGetValue(name, out arity))
            {
                arity = ComputeArity(name);
                ArityCache[name] = arity;
            }
        }

        if (arity is null)
        {
            return false;
        }

        accepted = arguments >= arity.Minimum && (arity.IsVariadic || arguments <= arity.Maximum);
        expected = arity.Describe();
        return true;
    }

    private static Mql5BinderArity? ComputeArity(string name)
    {
        try
        {
            object?[] arguments = [name, null];
            if (TryGetOverloadsMethod!.Invoke(null, arguments) is not true
                || arguments[1] is not IEnumerable overloads)
            {
                return null;
            }

            int minimum = int.MaxValue;
            int maximum = -1;
            bool variadic = false;

            foreach (object? overload in overloads)
            {
                if (overload is null)
                {
                    return null;
                }

                Type type = overload.GetType();
                if (ReadBoolean(overload, type, "Verified") == false)
                {
                    // The catalog says the shape is unconfirmed. Do not check arity.
                    return null;
                }

                if (!TryReadOverloadArity(overload, type, out int required, out int allowed))
                {
                    return null;
                }

                variadic |= ReadBoolean(overload, type, "IsVariadic") == true;
                minimum = Math.Min(minimum, required);
                maximum = Math.Max(maximum, allowed);
            }

            return maximum < 0 ? null : new Mql5BinderArity(minimum, maximum, variadic);
        }
#pragma warning disable CA1031
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    private static bool? ReadBoolean(object instance, Type type, string propertyName)
    {
        PropertyInfo? property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        return property is not null && property.PropertyType == typeof(bool)
            ? (bool?)property.GetValue(instance)
            : null;
    }

    private static bool TryReadOverloadArity(object overload, Type type, out int required, out int allowed)
    {
        required = 0;
        allowed = 0;

        PropertyInfo? parameters = null;
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.Name.Contains("Parameter", StringComparison.Ordinal)
                && typeof(IEnumerable).IsAssignableFrom(property.PropertyType)
                && property.PropertyType != typeof(string))
            {
                parameters = property;
                break;
            }
        }

        if (parameters is null || parameters.GetValue(overload) is not IEnumerable values)
        {
            return false;
        }

        int total = 0;
        int optional = 0;
        foreach (object? parameter in values)
        {
            total++;
            if (parameter is not null && IsOptionalParameter(parameter))
            {
                optional++;
            }
        }

        allowed = total;
        required = total - optional;
        return true;
    }

    private static bool IsOptionalParameter(object parameter)
    {
        Type type = parameter.GetType();

        foreach (string candidate in OptionalFlagNames)
        {
            PropertyInfo? flag = type.GetProperty(candidate, BindingFlags.Public | BindingFlags.Instance);
            if (flag is not null && flag.PropertyType == typeof(bool) && flag.GetValue(parameter) is true)
            {
                return true;
            }
        }

        foreach (string candidate in DefaultValueNames)
        {
            PropertyInfo? value = type.GetProperty(candidate, BindingFlags.Public | BindingFlags.Instance);
            if (value is not null && value.GetValue(parameter) is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static readonly string[] OptionalFlagNames =
        ["IsOptional", "Optional", "HasDefault", "HasDefaultValue"];

    private static readonly string[] DefaultValueNames = ["DefaultValue", "Default"];
}

/// <summary>The argument-count envelope of one built-in name, across all its overloads.</summary>
internal sealed record Mql5BinderArity(int Minimum, int Maximum, bool IsVariadic)
{
    /// <summary>A short description of the accepted counts, for diagnostic text.</summary>
    public string Describe()
    {
        int low = Minimum == int.MaxValue ? 0 : Minimum;
        if (IsVariadic)
        {
            return string.Create(CultureInfo.InvariantCulture, $"at least {low} argument(s)");
        }

        return low == Maximum
            ? string.Create(CultureInfo.InvariantCulture, $"{low} argument(s)")
            : string.Create(CultureInfo.InvariantCulture, $"{low} to {Maximum} argument(s)");
    }
}

/// <summary>
/// Names of the MQL5 standard runtime, used only when the authoritative catalog is
/// not present in the assembly.
///
/// This is a recognition set, not a signature model: it answers "is this MQL5's or
/// is it the module's?" and nothing more. MQL4-only names are deliberately excluded,
/// so a legacy call such as <c>OrderClose</c> reports as unresolved, which is the
/// truthful answer for an MQL5 target.
/// </summary>
internal static class Mql5BinderFallback
{
    /// <summary>Function names of the MQL5 runtime.</summary>
    public static IReadOnlySet<string> Functions { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        // Account
        "AccountInfoDouble", "AccountInfoInteger", "AccountInfoString",
        // Arrays
        "ArrayBsearch", "ArrayCompare", "ArrayCopy", "ArrayFill", "ArrayFree", "ArrayGetAsSeries",
        "ArrayInitialize", "ArrayInsert", "ArrayIsDynamic", "ArrayIsSeries", "ArrayMaximum",
        "ArrayMinimum", "ArrayPrint", "ArrayRange", "ArrayRemove", "ArrayResize", "ArrayReverse",
        "ArraySetAsSeries", "ArraySize", "ArraySort", "ArraySwap",
        // Checkup and terminal
        "GetLastError", "IsStopped", "MQLInfoInteger", "MQLInfoString", "MQLSetInteger",
        "ResetLastError", "SetUserError", "TerminalInfoDouble", "TerminalInfoInteger",
        "TerminalInfoString", "UninitializeReason", "Symbol", "Period", "Digits", "Point",
        // Common
        "Alert", "CheckPointer", "Comment", "CryptDecode", "CryptEncode", "DebugBreak",
        "ExpertRemove", "GetMicrosecondCount", "GetPointer", "GetTickCount", "GetTickCount64",
        "MessageBox", "PeriodSeconds", "PlaySound", "Print", "PrintFormat", "ResourceCreate",
        "ResourceFree", "ResourceReadImage", "ResourceSave", "SendFTP", "SendMail",
        "SendNotification", "Sleep", "TerminalClose", "TesterHideIndicators", "TesterStatistics",
        "TesterStop", "TesterWithdrawal", "TranslateKey", "WebRequest", "ZeroMemory",
        // Conversion
        "CharArrayToString", "CharArrayToStruct", "CharToString", "ColorToARGB", "ColorToString",
        "DoubleToString", "EnumToString", "IntegerToString", "NormalizeDouble", "ShortArrayToString",
        "ShortToString", "StringToCharArray", "StringToColor", "StringToDouble", "StringToInteger",
        "StringToShortArray", "StringToTime", "StringFormat", "StructToCharArray", "TimeToString",
        // Custom indicator plumbing
        "IndicatorSetDouble", "IndicatorSetInteger", "IndicatorSetString", "PlotIndexGetInteger",
        "PlotIndexSetDouble", "PlotIndexSetInteger", "PlotIndexSetString", "SetIndexBuffer",
        // Date and time
        "StructToTime", "TimeCurrent", "TimeDaylightSavings", "TimeGMT", "TimeGMTOffset",
        "TimeLocal", "TimeToStruct", "TimeTradeServer",
        // Files
        "FileClose", "FileCopy", "FileDelete", "FileFindClose", "FileFindFirst", "FileFindNext",
        "FileFlush", "FileGetInteger", "FileIsEnding", "FileIsExist", "FileIsLineEnding", "FileMove",
        "FileOpen", "FileReadArray", "FileReadBool", "FileReadDatetime", "FileReadDouble",
        "FileReadFloat", "FileReadInteger", "FileReadLong", "FileReadNumber", "FileReadString",
        "FileReadStruct", "FileSeek", "FileSize", "FileTell", "FileWrite", "FileWriteArray",
        "FileWriteDouble", "FileWriteFloat", "FileWriteInteger", "FileWriteLong", "FileWriteString",
        "FileWriteStruct", "FolderClean", "FolderCreate", "FolderDelete",
        // Global variables of the terminal
        "GlobalVariableCheck", "GlobalVariableDel", "GlobalVariableGet", "GlobalVariableName",
        "GlobalVariableSet", "GlobalVariableSetOnCondition", "GlobalVariableTemp",
        "GlobalVariableTime", "GlobalVariablesDeleteAll", "GlobalVariablesFlush",
        "GlobalVariablesTotal",
        // Technical indicators
        "iAC", "iAD", "iADX", "iADXWilder", "iAlligator", "iAMA", "iAO", "iATR", "iBands",
        "iBearsPower", "iBullsPower", "iBWMFI", "iCCI", "iChaikin", "iCustom", "iDEMA",
        "iDeMarker", "iEnvelopes", "iForce", "iFractals", "iFrAMA", "iGator", "iIchimoku",
        "iMA", "iMACD", "iMFI", "iMomentum", "iOBV", "iOsMA", "iRSI", "iRVI", "iSAR", "iStdDev",
        "iStochastic", "iTEMA", "iTriX", "iVIDyA", "iVolumes", "iWPR",
        "BarsCalculated", "CopyBuffer", "IndicatorCreate", "IndicatorParameters", "IndicatorRelease",
        // Time series
        "Bars", "CopyClose", "CopyHigh", "CopyLow", "CopyOpen", "CopyRates", "CopyRealVolume",
        "CopySpread", "CopyTickVolume", "CopyTicks", "CopyTicksRange", "CopyTime", "iBars",
        "iBarShift", "iClose", "iHigh", "iHighest", "iLow", "iLowest", "iOpen", "iRealVolume",
        "iSpread", "iTickVolume", "iTime", "iVolume", "SeriesInfoInteger",
        // Charts
        "ChartApplyTemplate", "ChartClose", "ChartFirst", "ChartGetDouble", "ChartGetInteger",
        "ChartGetString", "ChartID", "ChartIndicatorAdd", "ChartIndicatorDelete",
        "ChartIndicatorGet", "ChartIndicatorName", "ChartIndicatorsTotal", "ChartNavigate",
        "ChartNext", "ChartOpen", "ChartPeriod", "ChartPriceOnDropped", "ChartRedraw",
        "ChartSaveTemplate", "ChartScreenShot", "ChartSetDouble", "ChartSetInteger",
        "ChartSetString", "ChartSetSymbolPeriod", "ChartSymbol", "ChartTimeOnDropped",
        "ChartTimePriceToXY", "ChartWindowFind", "ChartWindowOnDropped", "ChartXOnDropped",
        "ChartXYToTimePrice", "ChartYOnDropped",
        // Objects
        "ObjectCreate", "ObjectDelete", "ObjectFind", "ObjectGetDouble", "ObjectGetInteger",
        "ObjectGetString", "ObjectGetTimeByValue", "ObjectGetValueByTime", "ObjectMove",
        "ObjectName", "ObjectSetDouble", "ObjectSetInteger", "ObjectSetString", "ObjectsDeleteAll",
        "ObjectsTotal", "TextGetSize", "TextOut", "TextSetFont",
        // Mathematics
        "MathAbs", "MathArccos", "MathArccosh", "MathArcsin", "MathArcsinh", "MathArctan",
        "MathArctan2", "MathArctanh", "MathCeil", "MathClassify", "MathCos", "MathCosh",
        "MathExp", "MathExpm1", "MathFloor", "MathIsValidNumber", "MathLog", "MathLog10",
        "MathLog1p", "MathMax", "MathMin", "MathMod", "MathPow", "MathRand", "MathRound",
        "MathSin", "MathSinh", "MathSqrt", "MathSrand", "MathSwap", "MathTan", "MathTanh",
        "fabs", "fmax", "fmin", "fmod", "pow", "sqrt", "log", "log10", "log1p", "exp", "expm1",
        "sin", "cos", "tan", "asin", "acos", "atan", "atan2", "sinh", "cosh", "tanh",
        "asinh", "acosh", "atanh", "ceil", "floor", "round", "rand", "srand",
        // Strings
        "StringAdd", "StringBufferLen", "StringCompare", "StringConcatenate", "StringFill",
        "StringFind", "StringGetCharacter", "StringInit", "StringLen", "StringReplace",
        "StringReserve", "StringSetCharacter", "StringSetLength", "StringSplit", "StringSubstr",
        "StringToLower", "StringToUpper", "StringTrimLeft", "StringTrimRight",
        // Trading
        "HistoryDealGetDouble", "HistoryDealGetInteger", "HistoryDealGetString",
        "HistoryDealGetTicket", "HistoryDealSelect", "HistoryDealsTotal", "HistoryOrderGetDouble",
        "HistoryOrderGetInteger", "HistoryOrderGetString", "HistoryOrderGetTicket",
        "HistoryOrderSelect", "HistoryOrdersTotal", "HistorySelect", "HistorySelectByPosition",
        "OrderCalcMargin", "OrderCalcProfit", "OrderCheck", "OrderGetDouble", "OrderGetInteger",
        "OrderGetString", "OrderGetTicket", "OrderSelect", "OrderSend", "OrderSendAsync",
        "OrdersTotal", "PositionGetDouble", "PositionGetInteger", "PositionGetString",
        "PositionGetSymbol", "PositionGetTicket", "PositionSelect", "PositionSelectByTicket",
        "PositionsTotal",
        // Market information
        "MarketBookAdd", "MarketBookGet", "MarketBookRelease", "SymbolInfoDouble",
        "SymbolInfoInteger", "SymbolInfoMarginRate", "SymbolInfoSessionQuote",
        "SymbolInfoSessionTrade", "SymbolInfoString", "SymbolInfoTick", "SymbolIsSynchronized",
        "SymbolName", "SymbolSelect", "SymbolsTotal",
        // Events
        "EventChartCustom", "EventKillTimer", "EventSetMillisecondTimer", "EventSetTimer",
        // Economic calendar
        "CalendarCountryById", "CalendarEventByCountry", "CalendarEventByCurrency",
        "CalendarEventById", "CalendarValueById", "CalendarValueHistory",
        "CalendarValueHistoryByEvent", "CalendarValueLast", "CalendarValueLastByEvent",
        // Database
        "DatabaseBind", "DatabaseBindArray", "DatabaseClose", "DatabaseColumnBlob",
        "DatabaseColumnDouble", "DatabaseColumnInteger", "DatabaseColumnLong",
        "DatabaseColumnName", "DatabaseColumnSize", "DatabaseColumnText", "DatabaseColumnType",
        "DatabaseColumnsCount", "DatabaseExecute", "DatabaseExport", "DatabaseFinalize",
        "DatabaseImport", "DatabaseOpen", "DatabasePrepare", "DatabasePrint", "DatabaseRead",
        "DatabaseReadBind", "DatabaseReset", "DatabaseTableExists", "DatabaseTransactionBegin",
        "DatabaseTransactionCommit", "DatabaseTransactionRollback",
        // Network
        "SocketClose", "SocketConnect", "SocketCreate", "SocketIsConnected", "SocketIsReadable",
        "SocketIsWritable", "SocketRead", "SocketSend", "SocketTimeouts",
        "SocketTlsCertificate", "SocketTlsHandshake", "SocketTlsRead", "SocketTlsReadAvailable",
        "SocketTlsSend",
        // Custom symbols
        "CustomRatesDelete", "CustomRatesReplace", "CustomRatesUpdate", "CustomSymbolCreate",
        "CustomSymbolDelete", "CustomSymbolSetDouble", "CustomSymbolSetInteger",
        "CustomSymbolSetString", "CustomTicksAdd", "CustomTicksDelete", "CustomTicksReplace",
        // Aliases
        "printf",
    };

    /// <summary>Struct, class and interface names of the MQL5 runtime and standard library.</summary>
    private static readonly HashSet<string> TypeNames = new(StringComparer.Ordinal)
    {
        "MqlTick", "MqlRates", "MqlBookInfo", "MqlDateTime", "MqlParam", "MqlTradeRequest",
        "MqlTradeResult", "MqlTradeCheckResult", "MqlTradeTransaction", "MqlCalendarValue",
        "MqlCalendarEvent", "MqlCalendarCountry", "MqlNet", "MqlString",
        "CObject", "CArrayObj", "CArrayDouble", "CArrayInt", "CArrayLong", "CArrayString",
        "CList", "CTrade", "CSymbolInfo", "CAccountInfo", "CPositionInfo", "COrderInfo",
        "CDealInfo", "CHistoryOrderInfo", "CTerminalInfo", "CExpert", "CExpertBase",
        "CExpertSignal", "CExpertTrailing", "CExpertMoney", "CIndicators", "CIndicator",
        "CiMA", "CiRSI", "CiMACD", "CiATR", "CiStochastic", "CiBands", "CiCCI", "CiADX",
        "CiCustom", "CChart", "CChartObject", "CChartObjectText", "CChartObjectLabel",
        "CChartObjectTrend", "CChartObjectVLine", "CChartObjectHLine", "CChartObjectRectangle",
        "CCanvas", "CFileBin", "CFileTxt", "CFile", "CAppDialog", "CDialog", "CWnd",
        "CWndObj", "CWndClient", "CWndContainer", "CButton", "CEdit", "CLabel", "CPanel",
        "CComboBox", "CCheckBox", "CRadioButton", "CSpinEdit", "CDatePicker", "CListView",
        "CScroll", "CScrollV", "CScrollH", "CBmpButton", "CPicture", "CRect", "CString",
        "CDoubleArray", "CTradeInfo", "CMoneyFixedLot", "CMoneyFixedMargin", "CMoneyFixedRisk",
        "CMoneyNone", "CMoneySizeOptimized", "CTrailingFixedPips", "CTrailingMA", "CTrailingNone",
        "CTrailingParabolicSAR", "CTradeManager", "CJAVal", "CJSONValue", "CHashMap",
    };

    private static readonly HashSet<string> Constants = new(StringComparer.Ordinal)
    {
        "NULL", "EMPTY", "EMPTY_VALUE", "WHOLE_ARRAY", "INVALID_HANDLE", "WRONG_VALUE",
        "CHARTS_MAX", "clrNONE", "IS_DEBUG_MODE", "IS_PROFILE_MODE",
        "INT_MAX", "INT_MIN", "UINT_MAX", "LONG_MAX", "LONG_MIN", "ULONG_MAX",
        "SHORT_MAX", "SHORT_MIN", "USHORT_MAX", "CHAR_MAX", "CHAR_MIN", "UCHAR_MAX",
        "DBL_MAX", "DBL_MIN", "DBL_EPSILON", "DBL_DIG", "DBL_MANT_DIG", "DBL_MAX_10_EXP",
        "DBL_MAX_EXP", "DBL_MIN_10_EXP", "DBL_MIN_EXP",
        "FLT_MAX", "FLT_MIN", "FLT_EPSILON", "FLT_DIG", "FLT_MANT_DIG",
        "M_E", "M_LOG2E", "M_LOG10E", "M_LN2", "M_LN10", "M_PI", "M_PI_2", "M_PI_4",
        "M_1_PI", "M_2_PI", "M_2_SQRTPI", "M_SQRT2", "M_SQRT1_2", "M_3PI_4",
        "_Digits", "_Point", "_Period", "_Symbol", "_LastError", "_RandomSeed", "_StopFlag",
        "_UninitReason", "_IsX64", "_AppliedTo",
        "__FILE__", "__LINE__", "__FUNCTION__", "__FUNCSIG__", "__PATH__", "__DATE__",
        "__DATETIME__", "__COUNTER__", "__RANDOM__", "__MQL5BUILD__", "__MQLBUILD__",
        "SUNDAY", "MONDAY", "TUESDAY", "WEDNESDAY", "THURSDAY", "FRIDAY", "SATURDAY",
        "INIT_SUCCEEDED", "INIT_FAILED", "INIT_PARAMETERS_INCORRECT", "INIT_AGENT_NOT_SUITABLE",
    };

    /// <summary>
    /// Prefixes of the MQL5 named-constant families. An identifier in one of these
    /// families that is written in upper snake case is treated as a runtime constant.
    /// This is a recognition heuristic, not a table of values, and it is the reason
    /// the fallback path reports no constant values.
    /// </summary>
    private static readonly string[] ConstantPrefixes =
    [
        "ACCOUNT_", "ALIGN_", "ANCHOR_", "BOOK_", "BORDER_", "CHART_", "CHARTEVENT_", "CHART_EVENT_",
        "CORNER_", "CRYPT_", "DEAL_", "DRAW_", "ERR_", "FILE_", "FRAME_", "GANN_", "IND_",
        "INDICATOR_", "LICENSE_", "MODE_", "MQL_", "OBJ_", "OBJPROP_", "ORDER_", "PERIOD_",
        "PLOT_", "POINTER_", "POSITION_", "PRICE_", "PROGRAM_", "REASON_", "SEEK_", "SERIES_",
        "STAT_", "STO_", "STYLE_", "SYMBOL_", "TERMINAL_", "TICK_", "TRADE_", "VOLUME_",
        "SIGNAL_", "SERVER_", "SEND_", "WEB_", "TEXT_", "FLAG_", "CHARTEVENT",
        "TIME_", "CALENDAR_", "WEEK_", "TESTER_", "DATABASE_", "OPENCL_", "COPY_",
        "MQLINFO_",
    ];

    /// <summary>True when <paramref name="name"/> is recognised as a runtime constant.</summary>
    public static bool IsConstant(string name)
    {
        if (Constants.Contains(name))
        {
            return true;
        }

        if (name.StartsWith("clr", StringComparison.Ordinal)
            && name.Length > 3
            && char.IsUpper(name[3]))
        {
            return true;
        }

        foreach (string prefix in ConstantPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal) && IsUpperSnakeCase(name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when <paramref name="name"/> is recognised as a runtime type.</summary>
    public static bool IsType(string name) => TypeNames.Contains(name);

    private static readonly Dictionary<string, Mql5IrScalarKind> ScalarKeywords =
        new(StringComparer.Ordinal)
        {
            ["void"] = Mql5IrScalarKind.Void,
            ["bool"] = Mql5IrScalarKind.Logical,
            ["char"] = Mql5IrScalarKind.Whole8,
            ["uchar"] = Mql5IrScalarKind.Natural8,
            ["short"] = Mql5IrScalarKind.Whole16,
            ["ushort"] = Mql5IrScalarKind.Natural16,
            ["int"] = Mql5IrScalarKind.Whole32,
            ["uint"] = Mql5IrScalarKind.Natural32,
            ["long"] = Mql5IrScalarKind.Whole64,
            ["ulong"] = Mql5IrScalarKind.Natural64,
            ["float"] = Mql5IrScalarKind.Real32,
            ["double"] = Mql5IrScalarKind.Real64,
            ["string"] = Mql5IrScalarKind.Text,
            ["datetime"] = Mql5IrScalarKind.Moment,
            ["color"] = Mql5IrScalarKind.Colour,
        };

    /// <summary>
    /// Maps a scalar type keyword written in expression position — MQL5 spells a
    /// conversion as <c>string(x)</c> — onto the scalar it denotes.
    /// </summary>
    public static bool TryGetScalarKeyword(string name, out Mql5IrScalarKind scalar) =>
        ScalarKeywords.TryGetValue(name, out scalar);

    private static bool IsUpperSnakeCase(string name)
    {
        foreach (char character in name)
        {
            if (!char.IsAsciiLetterUpper(character) && !char.IsAsciiDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }
}
