using System.Globalization;

namespace YO4X.StrategyGovernance;

/// <summary>Outcome of lowering one parsed translation unit into IR v2.</summary>
public sealed record Mql5LoweringResult(
    bool Succeeded,
    Mql5IrV2Module? Module,
    IReadOnlyList<Mql5RestrictedDiagnostic> Diagnostics);

/// <summary>
/// Lowers the MQL5 abstract syntax tree into intermediate representation v2.
///
/// The pass is structural: it rewrites syntax into IR nodes and normalises trivial
/// constants. It does not resolve names, select overloads, check types or insert
/// conversions; a later binding pass owns all of that.
///
/// The pass fails closed for each construct it cannot represent. Any construct
/// that cannot be lowered produces a diagnostic carrying a stable code and the
/// source position, and poisons the module: <see cref="Mql5LoweringResult.Module"/>
/// is null and <see cref="Mql5LoweringResult.Succeeded"/> is false. Nothing is ever
/// dropped silently, because an IR that looks executable but has lost a statement
/// is far more dangerous than a refusal. Lowering continues across siblings after a
/// failure so that one run reports every unsupported construct rather than only the
/// first.
///
/// The pass never throws for any input, including a null unit, null child nodes or
/// pathologically nested trees.
/// </summary>
public static class Mql5Lowering
{
    /// <summary>Emitted once, as information, when the whole unit lowered.</summary>
    public const string CompletedCode = "MQL5_LOWER_COMPLETED";

    private const string NoCompilationUnit = "MQL5_LOWER_NO_COMPILATION_UNIT";
    private const string MalformedNode = "MQL5_LOWER_MALFORMED_NODE";
    private const string DepthLimitExceeded = "MQL5_LOWER_DEPTH_LIMIT_EXCEEDED";
    private const string UnsupportedDeclaration = "MQL5_LOWER_UNSUPPORTED_DECLARATION";
    private const string UnsupportedStatement = "MQL5_LOWER_UNSUPPORTED_STATEMENT";
    private const string UnsupportedExpression = "MQL5_LOWER_UNSUPPORTED_EXPRESSION";
    private const string UnsupportedTemplate = "MQL5_LOWER_UNSUPPORTED_TEMPLATE";
    private const string UnsupportedUnion = "MQL5_LOWER_UNSUPPORTED_UNION";
    private const string UnsupportedOperatorOverload = "MQL5_LOWER_UNSUPPORTED_OPERATOR_OVERLOAD";
    private const string UnsupportedImportBody = "MQL5_LOWER_UNSUPPORTED_IMPORT_BODY";
    private const string UnsupportedTypeMember = "MQL5_LOWER_UNSUPPORTED_TYPE_MEMBER";
    private const string UnsupportedScopeQualifier = "MQL5_LOWER_UNSUPPORTED_SCOPE_QUALIFIER";

    /// <summary>Maximum nesting depth accepted before lowering refuses the tree.</summary>
    private const int MaximumDepth = 192;

    /// <summary>Lowers one compilation unit. Never throws.</summary>
    public static Mql5LoweringResult Lower(Mql5CompilationUnit unit)
    {
        var context = new LoweringContext();
        if (unit is null)
        {
            context.Fail(NoCompilationUnit, "No compilation unit was supplied to lowering.", 1, 1);
            return new(false, null, context.Diagnostics);
        }

        var builder = new ModuleBuilder();
        foreach (Mql5Declaration declaration in unit.Declarations ?? [])
        {
            LowerTopLevelDeclaration(declaration, builder, context, 0);
        }

        if (context.Failed)
        {
            return new(false, null, context.Diagnostics);
        }

        Mql5IrV2Module module = Mql5IrV2Module.Create(
            unit.RelativePath,
            unit.SourceSha256,
            builder.Properties,
            builder.Includes,
            builder.Defines,
            builder.Imports,
            builder.Enums,
            builder.Types,
            builder.Globals,
            builder.Inputs,
            builder.Functions);
        context.Note(
            CompletedCode,
            "The translation unit was lowered into MQL5 IR v2 with no unsupported constructs.",
            1,
            1);
        return new(true, module, context.Diagnostics);
    }

    // ----------------------------------------------------------- declarations

    private static void LowerTopLevelDeclaration(
        Mql5Declaration? declaration,
        ModuleBuilder builder,
        LoweringContext context,
        int depth)
    {
        if (!context.CheckDepth(depth, declaration?.Line ?? 1, declaration?.Column ?? 1))
        {
            return;
        }

        switch (declaration)
        {
            case null:
                context.Fail(MalformedNode, "A null top-level declaration cannot be lowered.", 1, 1);
                return;
            case Mql5PropertyDirective property:
                builder.Properties.Add(new(
                    property.Name ?? string.Empty,
                    property.Value,
                    property.Line,
                    property.Column));
                return;
            case Mql5IncludeDirective include:
                builder.Includes.Add(new(
                    include.Path ?? string.Empty,
                    include.IsSystemPath,
                    include.Line,
                    include.Column));
                return;
            case Mql5DefineDirective define:
                builder.Defines.Add(new(
                    define.Name ?? string.Empty,
                    define.Replacement ?? string.Empty,
                    define.Line,
                    define.Column));
                return;
            case Mql5ImportDirective import:
                LowerImport(import, builder, context);
                return;
            case Mql5EnumDeclaration enumeration:
                {
                    Mql5IrEnumeration? lowered = LowerEnum(enumeration, context, depth + 1);
                    if (lowered is not null)
                    {
                        builder.Enums.Add(lowered);
                    }

                    return;
                }

            case Mql5TypeDeclaration type:
                {
                    Mql5IrTypeDeclaration? lowered = LowerTypeDeclaration(type, [], context, depth + 1);
                    if (lowered is not null)
                    {
                        builder.Types.Add(lowered);
                    }

                    return;
                }

            case Mql5GlobalVariableDeclaration global:
                LowerGlobalVariable(global, builder, context, depth + 1);
                return;
            case Mql5FunctionDeclaration function:
                {
                    Mql5IrFunction? lowered = LowerFunction(function, Mql5Access.Public, [], context, depth + 1);
                    if (lowered is not null)
                    {
                        builder.Functions.Add(lowered);
                    }

                    return;
                }

            case Mql5TemplateDeclaration template:
                LowerTemplate(template, builder, context, depth + 1);
                return;
            default:
                context.Fail(
                    UnsupportedDeclaration,
                    $"Declaration form '{declaration.GetType().Name}' has no IR v2 representation.",
                    declaration.Line,
                    declaration.Column);
                return;
        }
    }

    private static void LowerImport(Mql5ImportDirective import, ModuleBuilder builder, LoweringContext context)
    {
        builder.Imports.Add(new(import.Library ?? string.Empty, [], import.Line, import.Column));
        IReadOnlyList<Mql5FunctionDeclaration> functions = import.Functions ?? [];
        if (functions.Count == 0)
        {
            return;
        }

        foreach (Mql5FunctionDeclaration function in functions)
        {
            context.Fail(
                UnsupportedImportBody,
                $"Imported prototype '{function?.Name ?? "<unnamed>"}' from '{import.Library ?? string.Empty}' is not lowered by IR v2; native imports are outside the executable subset.",
                function?.Line ?? import.Line,
                function?.Column ?? import.Column);
        }
    }

    /// <summary>
    /// Lowers a file-scope <c>template&lt;typename …&gt;</c> by attaching its parameter
    /// names to the declaration it introduces.
    ///
    /// The template is carried, not monomorphised. Substituting arguments would need
    /// every instantiation to be visible here, and MQL5 gives no such guarantee: an
    /// argument is deduced at each call site, and a call site can sit in a translation
    /// unit this pass never sees. A parameter list attached to the declaration keeps
    /// what the source actually said and leaves the choice between emitting a generic
    /// and specialising it to the back end.
    /// </summary>
    private static void LowerTemplate(
        Mql5TemplateDeclaration template,
        ModuleBuilder builder,
        LoweringContext context,
        int depth)
    {
        IReadOnlyList<string>? typeParameters = AcceptTypeParameters(template, context);
        if (typeParameters is null)
        {
            return;
        }

        switch (template.Declaration)
        {
            case Mql5FunctionDeclaration function:
                {
                    Mql5IrFunction? lowered = LowerFunction(
                        function,
                        Mql5Access.Public,
                        typeParameters,
                        context,
                        depth + 1);
                    if (lowered is not null)
                    {
                        builder.Functions.Add(lowered);
                    }

                    return;
                }

            case Mql5TypeDeclaration type:
                {
                    Mql5IrTypeDeclaration? lowered = LowerTypeDeclaration(
                        type,
                        typeParameters,
                        context,
                        depth + 1);
                    if (lowered is not null)
                    {
                        builder.Types.Add(lowered);
                    }

                    return;
                }

            default:
                context.Fail(
                    UnsupportedTemplate,
                    $"A template over '{template.Declaration?.GetType().Name ?? "nothing"}' is not lowered by IR v2; only a function, structure, class or interface can carry type parameters.",
                    template.Line,
                    template.Column);
                return;
        }
    }

    /// <summary>
    /// Returns the template's parameter names, or null after refusing the template.
    ///
    /// An empty list means the parser never read a <c>&lt;…&gt;</c> list, and a repeated
    /// or unnamed parameter is rejected by MetaEditor itself (error 282, "idenfitier
    /// 'T' already used"). Lowering any of those would produce a declaration that is
    /// quietly not the one the source wrote.
    /// </summary>
    private static IReadOnlyList<string>? AcceptTypeParameters(
        Mql5TemplateDeclaration template,
        LoweringContext context)
    {
        IReadOnlyList<string> declared = template.TypeParameters ?? [];
        if (declared.Count == 0)
        {
            context.Fail(
                UnsupportedTemplate,
                "A template with no type parameters is not lowered by IR v2.",
                template.Line,
                template.Column);
            return null;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in declared)
        {
            if (string.IsNullOrEmpty(name))
            {
                context.Fail(
                    UnsupportedTemplate,
                    "A template type parameter without a name is not lowered by IR v2.",
                    template.Line,
                    template.Column);
                return null;
            }

            if (!seen.Add(name))
            {
                context.Fail(
                    UnsupportedTemplate,
                    $"Template type parameter '{name}' is declared twice, which MQL5 rejects, so it is not lowered by IR v2.",
                    template.Line,
                    template.Column);
                return null;
            }
        }

        return declared;
    }

    private static Mql5IrEnumeration? LowerEnum(Mql5EnumDeclaration declaration, LoweringContext context, int depth)
    {
        if (!context.CheckDepth(depth, declaration.Line, declaration.Column))
        {
            return null;
        }

        var members = new List<Mql5IrEnumMember>();
        long? next = 0;
        bool failed = false;
        foreach (Mql5EnumMemberDeclaration member in declaration.Members ?? [])
        {
            if (member is null)
            {
                context.Fail(MalformedNode, "A null enumeration member cannot be lowered.", declaration.Line, declaration.Column);
                failed = true;
                continue;
            }

            long? folded;
            Mql5IrExpression? value = null;
            if (member.Value is null)
            {
                folded = next;
            }
            else
            {
                value = LowerExpression(member.Value, context, depth + 1);
                if (value is null)
                {
                    failed = true;
                    next = null;
                    continue;
                }

                folded = FoldWhole(value);
            }

            members.Add(new(member.Name ?? string.Empty, value, folded, member.Line, member.Column));
            next = folded is null or long.MaxValue ? null : folded.Value + 1;
        }

        return failed ? null : new Mql5IrEnumeration(declaration.Name ?? string.Empty, members, declaration.Line, declaration.Column);
    }

    private static Mql5IrTypeDeclaration? LowerTypeDeclaration(
        Mql5TypeDeclaration declaration,
        IReadOnlyList<string> typeParameters,
        LoweringContext context,
        int depth)
    {
        if (!context.CheckDepth(depth, declaration.Line, declaration.Column))
        {
            return null;
        }

        string keyword = declaration.Keyword ?? "struct";
        if (string.Equals(keyword, "union", StringComparison.Ordinal))
        {
            context.Fail(
                UnsupportedUnion,
                $"Union '{declaration.Name ?? string.Empty}' is not lowered by IR v2; overlapping storage has no IR v2 representation.",
                declaration.Line,
                declaration.Column);
            return null;
        }

        var fields = new List<Mql5IrField>();
        var methods = new List<Mql5IrFunction>();
        var nestedEnums = new List<Mql5IrEnumeration>();
        var nestedTypes = new List<Mql5IrTypeDeclaration>();
        bool failed = false;
        foreach (Mql5TypeMember member in declaration.Members ?? [])
        {
            if (member?.Declaration is null)
            {
                context.Fail(MalformedNode, "A null member cannot be lowered.", declaration.Line, declaration.Column);
                failed = true;
                continue;
            }

            switch (member.Declaration)
            {
                case Mql5GlobalVariableDeclaration field:
                    if (field.InputKind != Mql5InputKind.None)
                    {
                        context.Fail(
                            UnsupportedTypeMember,
                            "An input or extern storage class is not valid on a type member and is not lowered.",
                            field.Line,
                            field.Column);
                        failed = true;
                        break;
                    }

                    if (!LowerFields(field, member.Access, fields, context, depth + 1))
                    {
                        failed = true;
                    }

                    break;
                case Mql5FunctionDeclaration method:
                    {
                        Mql5IrFunction? lowered = LowerFunction(method, member.Access, [], context, depth + 1);
                        if (lowered is null)
                        {
                            failed = true;
                        }
                        else
                        {
                            methods.Add(lowered);
                        }

                        break;
                    }

                case Mql5EnumDeclaration nestedEnum:
                    {
                        Mql5IrEnumeration? lowered = LowerEnum(nestedEnum, context, depth + 1);
                        if (lowered is null)
                        {
                            failed = true;
                        }
                        else
                        {
                            nestedEnums.Add(lowered);
                        }

                        break;
                    }

                case Mql5TypeDeclaration nestedType:
                    {
                        Mql5IrTypeDeclaration? lowered = LowerTypeDeclaration(nestedType, [], context, depth + 1);
                        if (lowered is null)
                        {
                            failed = true;
                        }
                        else
                        {
                            nestedTypes.Add(lowered);
                        }

                        break;
                    }

                case Mql5TemplateDeclaration template:
                    if (!LowerTemplateMember(template, member.Access, methods, nestedTypes, context, depth + 1))
                    {
                        failed = true;
                    }

                    break;
                default:
                    context.Fail(
                        UnsupportedTypeMember,
                        $"Member form '{member.Declaration.GetType().Name}' has no IR v2 representation.",
                        member.Declaration.Line,
                        member.Declaration.Column);
                    failed = true;
                    break;
            }
        }

        return failed
            ? null
            : new Mql5IrTypeDeclaration(
                keyword,
                declaration.Name ?? string.Empty,
                typeParameters,
                declaration.BaseTypeName,
                fields,
                methods,
                nestedEnums,
                nestedTypes,
                declaration.Line,
                declaration.Column);
    }

    /// <summary>
    /// Lowers a <c>template&lt;typename …&gt;</c> written inside a structure, class or
    /// interface: a generic method, or a generic nested type. Returns false after
    /// refusing anything else, so the enclosing type is poisoned rather than silently
    /// losing the member.
    /// </summary>
    private static bool LowerTemplateMember(
        Mql5TemplateDeclaration template,
        Mql5Access access,
        List<Mql5IrFunction> methods,
        List<Mql5IrTypeDeclaration> nestedTypes,
        LoweringContext context,
        int depth)
    {
        IReadOnlyList<string>? typeParameters = AcceptTypeParameters(template, context);
        if (typeParameters is null)
        {
            return false;
        }

        switch (template.Declaration)
        {
            case Mql5FunctionDeclaration method:
                {
                    Mql5IrFunction? lowered = LowerFunction(method, access, typeParameters, context, depth + 1);
                    if (lowered is null)
                    {
                        return false;
                    }

                    methods.Add(lowered);
                    return true;
                }

            case Mql5TypeDeclaration nestedType:
                {
                    Mql5IrTypeDeclaration? lowered = LowerTypeDeclaration(nestedType, typeParameters, context, depth + 1);
                    if (lowered is null)
                    {
                        return false;
                    }

                    nestedTypes.Add(lowered);
                    return true;
                }

            default:
                context.Fail(
                    UnsupportedTemplate,
                    $"A template member over '{template.Declaration?.GetType().Name ?? "nothing"}' is not lowered by IR v2; only a method or a nested type can carry type parameters.",
                    template.Line,
                    template.Column);
                return false;
        }
    }

    private static bool LowerFields(
        Mql5GlobalVariableDeclaration declaration,
        Mql5Access access,
        List<Mql5IrField> fields,
        LoweringContext context,
        int depth)
    {
        Mql5IrTypeReference? type = LowerType(declaration.Type, context, depth);
        if (type is null)
        {
            return false;
        }

        bool succeeded = true;
        foreach (Mql5VariableDeclarator declarator in declaration.Declarators ?? [])
        {
            if (declarator is null)
            {
                context.Fail(MalformedNode, "A null field declarator cannot be lowered.", declaration.Line, declaration.Column);
                succeeded = false;
                continue;
            }

            List<Mql5IrArrayRank>? ranks = LowerRanks(declarator.ArrayRanks, context, depth, declarator.Line, declarator.Column);
            if (ranks is null)
            {
                succeeded = false;
                continue;
            }

            Mql5IrExpression? initializer = null;
            if (declarator.Initializer is not null)
            {
                initializer = LowerExpression(declarator.Initializer, context, depth + 1);
                if (initializer is null)
                {
                    succeeded = false;
                    continue;
                }
            }

            fields.Add(new(
                access,
                type,
                declarator.Name ?? string.Empty,
                ranks,
                initializer,
                declaration.IsStatic,
                declaration.IsConst,
                declarator.Line,
                declarator.Column));
        }

        return succeeded;
    }

    private static void LowerGlobalVariable(
        Mql5GlobalVariableDeclaration declaration,
        ModuleBuilder builder,
        LoweringContext context,
        int depth)
    {
        Mql5IrTypeReference? type = LowerType(declaration.Type, context, depth);
        if (type is null)
        {
            return;
        }

        foreach (Mql5VariableDeclarator declarator in declaration.Declarators ?? [])
        {
            if (declarator is null)
            {
                context.Fail(MalformedNode, "A null declarator cannot be lowered.", declaration.Line, declaration.Column);
                continue;
            }

            List<Mql5IrArrayRank>? ranks = LowerRanks(declarator.ArrayRanks, context, depth, declarator.Line, declarator.Column);
            if (ranks is null)
            {
                continue;
            }

            Mql5IrExpression? initializer = null;
            if (declarator.Initializer is not null)
            {
                initializer = LowerExpression(declarator.Initializer, context, depth + 1);
                if (initializer is null)
                {
                    continue;
                }
            }

            if (declaration.InputKind == Mql5InputKind.None)
            {
                builder.Globals.Add(new(
                    type,
                    declarator.Name ?? string.Empty,
                    ranks,
                    initializer,
                    declaration.IsStatic,
                    declaration.IsConst,
                    declarator.Line,
                    declarator.Column));
                continue;
            }

            // The label and group are properties of the declaration, so every declarator of
            // a multi-name 'input int a, b;' shares the one trailing comment that follows it.
            builder.Inputs.Add(new(
                declaration.InputKind,
                type,
                declarator.Name ?? string.Empty,
                ranks,
                initializer,
                CanonicalDefault(initializer),
                declaration.IsConst,
                declarator.Line,
                declarator.Column,
                declaration.Label,
                declaration.GroupLabel));
        }
    }

    private static Mql5IrFunction? LowerFunction(
        Mql5FunctionDeclaration declaration,
        Mql5Access access,
        IReadOnlyList<string> typeParameters,
        LoweringContext context,
        int depth)
    {
        if (!context.CheckDepth(depth, declaration.Line, declaration.Column))
        {
            return null;
        }

        string name = declaration.Name ?? string.Empty;
        if (IsOperatorName(name))
        {
            context.Fail(
                UnsupportedOperatorOverload,
                $"Operator overload '{name}' is not lowered by IR v2; call an ordinary method instead.",
                declaration.Line,
                declaration.Column);
            return null;
        }

        Mql5IrTypeReference? returnType = LowerType(declaration.ReturnType, context, depth);
        if (returnType is null)
        {
            return null;
        }

        var parameters = new List<Mql5IrParameter>();
        bool failed = false;
        foreach (Mql5Parameter parameter in declaration.Parameters ?? [])
        {
            if (parameter is null)
            {
                context.Fail(MalformedNode, "A null parameter cannot be lowered.", declaration.Line, declaration.Column);
                failed = true;
                continue;
            }

            Mql5IrTypeReference? parameterType = LowerType(parameter.Type, context, depth + 1);
            if (parameterType is null)
            {
                failed = true;
                continue;
            }

            Mql5IrExpression? defaultValue = null;
            if (parameter.DefaultValue is not null)
            {
                defaultValue = LowerExpression(parameter.DefaultValue, context, depth + 1);
                if (defaultValue is null)
                {
                    failed = true;
                    continue;
                }
            }

            parameters.Add(new(parameterType, parameter.Name ?? string.Empty, defaultValue, parameter.Line, parameter.Column));
        }

        Mql5IrBlockStatement? body = null;
        if (declaration.Body is not null)
        {
            body = LowerStatement(declaration.Body, context, depth + 1) as Mql5IrBlockStatement;
            if (body is null)
            {
                failed = true;
            }
        }

        return failed
            ? null
            : new Mql5IrFunction(
                returnType,
                name,
                typeParameters,
                parameters,
                body,
                declaration.IsStatic,
                declaration.IsVirtual,
                declaration.IsAbstract,
                declaration.IsConst,
                access,
                declaration.Line,
                declaration.Column);
    }

    // ------------------------------------------------------------- statements

    private static Mql5IrStatement? LowerStatement(Mql5Statement? statement, LoweringContext context, int depth)
    {
        if (statement is null)
        {
            context.Fail(MalformedNode, "A null statement cannot be lowered.", 1, 1);
            return null;
        }

        if (!context.CheckDepth(depth, statement.Line, statement.Column))
        {
            return null;
        }

        switch (statement)
        {
            case Mql5BlockStatement block:
                {
                    var statements = new List<Mql5IrStatement>();
                    bool failed = false;
                    foreach (Mql5Statement child in block.Statements ?? [])
                    {
                        Mql5IrStatement? lowered = LowerStatement(child, context, depth + 1);
                        if (lowered is null)
                        {
                            failed = true;
                            continue;
                        }

                        statements.Add(lowered);
                    }

                    return failed ? null : new Mql5IrBlockStatement(statements, block.Line, block.Column);
                }

            case Mql5EmptyStatement:
                return new Mql5IrEmptyStatement(statement.Line, statement.Column);
            case Mql5ExpressionStatement expression:
                {
                    Mql5IrExpression? lowered = LowerExpression(expression.Expression, context, depth + 1);
                    return lowered is null ? null : new Mql5IrExpressionStatement(lowered, expression.Line, expression.Column);
                }

            case Mql5VariableDeclarationStatement declaration:
                return LowerLocalDeclaration(declaration, context, depth + 1);
            case Mql5IfStatement branch:
                {
                    Mql5IrExpression? condition = LowerExpression(branch.Condition, context, depth + 1);
                    Mql5IrStatement? whenTrue = LowerStatement(branch.WhenTrue, context, depth + 1);
                    Mql5IrStatement? whenFalse = null;
                    bool failed = condition is null || whenTrue is null;
                    if (branch.WhenFalse is not null)
                    {
                        whenFalse = LowerStatement(branch.WhenFalse, context, depth + 1);
                        failed |= whenFalse is null;
                    }

                    return failed || condition is null || whenTrue is null
                        ? null
                        : new Mql5IrIfStatement(condition, whenTrue, whenFalse, branch.Line, branch.Column);
                }

            case Mql5WhileStatement loop:
                {
                    Mql5IrExpression? condition = LowerExpression(loop.Condition, context, depth + 1);
                    Mql5IrStatement? body = LowerStatement(loop.Body, context, depth + 1);
                    return condition is null || body is null
                        ? null
                        : new Mql5IrWhileStatement(condition, body, loop.Line, loop.Column);
                }

            case Mql5DoWhileStatement loop:
                {
                    Mql5IrStatement? body = LowerStatement(loop.Body, context, depth + 1);
                    Mql5IrExpression? condition = LowerExpression(loop.Condition, context, depth + 1);
                    return condition is null || body is null
                        ? null
                        : new Mql5IrDoWhileStatement(body, condition, loop.Line, loop.Column);
                }

            case Mql5ForStatement loop:
                return LowerFor(loop, context, depth + 1);
            case Mql5SwitchStatement selection:
                return LowerSwitch(selection, context, depth + 1);
            case Mql5ReturnStatement result:
                {
                    if (result.Value is null)
                    {
                        return new Mql5IrReturnStatement(null, result.Line, result.Column);
                    }

                    Mql5IrExpression? value = LowerExpression(result.Value, context, depth + 1);
                    return value is null ? null : new Mql5IrReturnStatement(value, result.Line, result.Column);
                }

            case Mql5BreakStatement:
                return new Mql5IrBreakStatement(statement.Line, statement.Column);
            case Mql5ContinueStatement:
                return new Mql5IrContinueStatement(statement.Line, statement.Column);
            case Mql5DeleteStatement removal:
                {
                    Mql5IrExpression? operand = LowerExpression(removal.Operand, context, depth + 1);
                    return operand is null ? null : new Mql5IrDeleteStatement(operand, removal.Line, removal.Column);
                }

            default:
                context.Fail(
                    UnsupportedStatement,
                    $"Statement form '{statement.GetType().Name}' has no IR v2 representation.",
                    statement.Line,
                    statement.Column);
                return null;
        }
    }

    private static Mql5IrLocalDeclarationStatement? LowerLocalDeclaration(
        Mql5VariableDeclarationStatement declaration,
        LoweringContext context,
        int depth)
    {
        Mql5IrTypeReference? type = LowerType(declaration.Type, context, depth);
        bool failed = type is null;
        var variables = new List<Mql5IrVariable>();
        foreach (Mql5VariableDeclarator declarator in declaration.Declarators ?? [])
        {
            if (declarator is null)
            {
                context.Fail(MalformedNode, "A null declarator cannot be lowered.", declaration.Line, declaration.Column);
                failed = true;
                continue;
            }

            List<Mql5IrArrayRank>? ranks = LowerRanks(declarator.ArrayRanks, context, depth, declarator.Line, declarator.Column);
            if (ranks is null)
            {
                failed = true;
                continue;
            }

            Mql5IrExpression? initializer = null;
            if (declarator.Initializer is not null)
            {
                initializer = LowerExpression(declarator.Initializer, context, depth + 1);
                if (initializer is null)
                {
                    failed = true;
                    continue;
                }
            }

            variables.Add(new(declarator.Name ?? string.Empty, ranks, initializer, declarator.Line, declarator.Column));
        }

        return failed || type is null
            ? null
            : new Mql5IrLocalDeclarationStatement(
                type,
                declaration.IsStatic,
                declaration.IsConst,
                variables,
                declaration.Line,
                declaration.Column);
    }

    private static Mql5IrForStatement? LowerFor(Mql5ForStatement loop, LoweringContext context, int depth)
    {
        bool failed = false;
        Mql5IrStatement? initializer = null;
        if (loop.Initializer is not null)
        {
            initializer = LowerStatement(loop.Initializer, context, depth + 1);
            failed |= initializer is null;
        }

        Mql5IrExpression? condition = null;
        if (loop.Condition is not null)
        {
            condition = LowerExpression(loop.Condition, context, depth + 1);
            failed |= condition is null;
        }

        Mql5IrExpression? increment = null;
        if (loop.Increment is not null)
        {
            increment = LowerExpression(loop.Increment, context, depth + 1);
            failed |= increment is null;
        }

        Mql5IrStatement? body = LowerStatement(loop.Body, context, depth + 1);
        failed |= body is null;
        return failed || body is null
            ? null
            : new Mql5IrForStatement(initializer, condition, increment, body, loop.Line, loop.Column);
    }

    private static Mql5IrSwitchStatement? LowerSwitch(Mql5SwitchStatement selection, LoweringContext context, int depth)
    {
        Mql5IrExpression? subject = LowerExpression(selection.Subject, context, depth + 1);
        bool failed = subject is null;
        var sections = new List<Mql5IrSwitchSection>();
        foreach (Mql5SwitchSection section in selection.Sections ?? [])
        {
            if (section is null)
            {
                context.Fail(MalformedNode, "A null switch section cannot be lowered.", selection.Line, selection.Column);
                failed = true;
                continue;
            }

            var labels = new List<Mql5IrSwitchLabel>();
            foreach (Mql5Expression? label in section.Labels ?? [])
            {
                if (label is null)
                {
                    labels.Add(new(null, true));
                    continue;
                }

                Mql5IrExpression? value = LowerExpression(label, context, depth + 1);
                if (value is null)
                {
                    failed = true;
                    continue;
                }

                labels.Add(new(value, false));
            }

            var statements = new List<Mql5IrStatement>();
            foreach (Mql5Statement statement in section.Statements ?? [])
            {
                Mql5IrStatement? lowered = LowerStatement(statement, context, depth + 1);
                if (lowered is null)
                {
                    failed = true;
                    continue;
                }

                statements.Add(lowered);
            }

            sections.Add(new(labels, statements, section.Line, section.Column));
        }

        return failed || subject is null
            ? null
            : new Mql5IrSwitchStatement(subject, sections, selection.Line, selection.Column);
    }

    // ------------------------------------------------------------ expressions

    private static Mql5IrExpression? LowerExpression(Mql5Expression? expression, LoweringContext context, int depth)
    {
        if (expression is null)
        {
            context.Fail(MalformedNode, "A null expression cannot be lowered.", 1, 1);
            return null;
        }

        if (!context.CheckDepth(depth, expression.Line, expression.Column))
        {
            return null;
        }

        switch (expression)
        {
            case Mql5LiteralExpression literal:
                {
                    string text = literal.Text ?? string.Empty;
                    long? folded = literal.Kind == Mql5LiteralKind.Whole ? Mql5IrLiteral.TryFoldWhole(text) : null;
                    return new Mql5IrLiteralExpression(
                        literal.Kind,
                        text,
                        Mql5IrLiteral.Canonicalize(literal.Kind, text),
                        folded,
                        literal.Line,
                        literal.Column);
                }

            case Mql5IdentifierExpression identifier:
                return new Mql5IrNameExpression([], false, identifier.Name ?? string.Empty, identifier.Line, identifier.Column);
            case Mql5ScopeExpression scope:
                {
                    var segments = new List<string>();
                    if (!TryFlattenScope(scope.Qualifier, segments, 0))
                    {
                        context.Fail(
                            UnsupportedScopeQualifier,
                            "Only identifier chains may qualify a scoped name in IR v2.",
                            scope.Line,
                            scope.Column);
                        return null;
                    }

                    return new Mql5IrNameExpression(segments, true, scope.Name ?? string.Empty, scope.Line, scope.Column);
                }

            case Mql5UnaryExpression unary:
                {
                    Mql5IrExpression? operand = LowerExpression(unary.Operand, context, depth + 1);
                    if (operand is null)
                    {
                        return null;
                    }

                    string op = unary.Operator ?? string.Empty;
                    long? folded = null;
                    if (unary.IsPrefix && op is "-" or "+")
                    {
                        long? inner = FoldWhole(operand);
                        if (inner is not null)
                        {
                            folded = op == "+" ? inner : inner == long.MinValue ? null : -inner;
                        }
                    }

                    return new Mql5IrUnaryExpression(op, unary.IsPrefix, operand, folded, unary.Line, unary.Column);
                }

            case Mql5BinaryExpression binary:
                {
                    Mql5IrExpression? left = LowerExpression(binary.Left, context, depth + 1);
                    Mql5IrExpression? right = LowerExpression(binary.Right, context, depth + 1);
                    return left is null || right is null
                        ? null
                        : new Mql5IrBinaryExpression(binary.Operator ?? string.Empty, left, right, binary.Line, binary.Column);
                }

            case Mql5AssignmentExpression assignment:
                {
                    Mql5IrExpression? target = LowerExpression(assignment.Target, context, depth + 1);
                    Mql5IrExpression? value = LowerExpression(assignment.Value, context, depth + 1);
                    return target is null || value is null
                        ? null
                        : new Mql5IrAssignmentExpression(assignment.Operator ?? string.Empty, target, value, assignment.Line, assignment.Column);
                }

            case Mql5ConditionalExpression conditional:
                {
                    Mql5IrExpression? condition = LowerExpression(conditional.Condition, context, depth + 1);
                    Mql5IrExpression? whenTrue = LowerExpression(conditional.WhenTrue, context, depth + 1);
                    Mql5IrExpression? whenFalse = LowerExpression(conditional.WhenFalse, context, depth + 1);
                    return condition is null || whenTrue is null || whenFalse is null
                        ? null
                        : new Mql5IrConditionalExpression(condition, whenTrue, whenFalse, conditional.Line, conditional.Column);
                }

            case Mql5CallExpression call:
                {
                    Mql5IrExpression? callee = LowerExpression(call.Callee, context, depth + 1);
                    bool failed = callee is null;
                    var arguments = new List<Mql5IrExpression>();
                    foreach (Mql5Expression argument in call.Arguments ?? [])
                    {
                        Mql5IrExpression? lowered = LowerExpression(argument, context, depth + 1);
                        if (lowered is null)
                        {
                            failed = true;
                            continue;
                        }

                        arguments.Add(lowered);
                    }

                    return failed || callee is null
                        ? null
                        : new Mql5IrCallExpression(callee, arguments, call.Line, call.Column);
                }

            case Mql5IndexExpression index:
                {
                    Mql5IrExpression? target = LowerExpression(index.Target, context, depth + 1);
                    Mql5IrExpression? subscript = LowerExpression(index.Index, context, depth + 1);
                    return target is null || subscript is null
                        ? null
                        : new Mql5IrIndexExpression(target, subscript, index.Line, index.Column);
                }

            case Mql5MemberExpression member:
                {
                    Mql5IrExpression? target = LowerExpression(member.Target, context, depth + 1);
                    return target is null
                        ? null
                        : new Mql5IrMemberExpression(target, member.Member ?? string.Empty, member.ThroughPointer, member.Line, member.Column);
                }

            case Mql5CastExpression cast:
                {
                    Mql5IrTypeReference? type = LowerType(cast.Type, context, depth + 1);
                    Mql5IrExpression? operand = LowerExpression(cast.Operand, context, depth + 1);
                    return type is null || operand is null
                        ? null
                        : new Mql5IrCastExpression(type, operand, cast.Line, cast.Column);
                }

            case Mql5NewExpression allocation:
                {
                    Mql5IrTypeReference? type = LowerType(allocation.Type, context, depth + 1);
                    return type is null ? null : new Mql5IrNewExpression(type, allocation.Line, allocation.Column);
                }

            case Mql5SizeOfExpression measurement:
                {
                    // The written type is always carried. The operand rides alongside it when the
                    // text could equally name a value; which of the two the source meant is a
                    // binding question, and lowering answers none.
                    Mql5IrTypeReference? type = LowerType(measurement.Type, context, depth + 1);
                    return type is null
                        ? null
                        : new Mql5IrSizeOfExpression(type, measurement.Line, measurement.Column)
                        {
                            Operand = measurement.Operand is null
                                ? null
                                : LowerExpression(measurement.Operand, context, depth + 1)
                        };
                }

            case Mql5TypeNameExpression typeName:
                {
                    // Exactly one side is written. The type form resolves here; the
                    // expression form is carried whole, because its static type is a
                    // binding question and lowering answers none.
                    if (typeName.Type is not null)
                    {
                        Mql5IrTypeReference? type = LowerType(typeName.Type, context, depth + 1);
                        return type is null
                            ? null
                            : new Mql5IrTypeNameExpression(type, null, typeName.Line, typeName.Column);
                    }

                    if (typeName.Operand is null)
                    {
                        context.Fail(
                            MalformedNode,
                            "A 'typename' operator with neither a type nor an expression cannot be lowered.",
                            typeName.Line,
                            typeName.Column);
                        return null;
                    }

                    Mql5IrExpression? operand = LowerExpression(typeName.Operand, context, depth + 1);
                    return operand is null
                        ? null
                        : new Mql5IrTypeNameExpression(null, operand, typeName.Line, typeName.Column);
                }

            case Mql5InitializerListExpression initializer:
                {
                    bool failed = false;
                    var items = new List<Mql5IrExpression>();
                    foreach (Mql5Expression item in initializer.Items ?? [])
                    {
                        Mql5IrExpression? lowered = LowerExpression(item, context, depth + 1);
                        if (lowered is null)
                        {
                            failed = true;
                            continue;
                        }

                        items.Add(lowered);
                    }

                    return failed ? null : new Mql5IrInitializerListExpression(items, initializer.Line, initializer.Column);
                }

            default:
                context.Fail(
                    UnsupportedExpression,
                    $"Expression form '{expression.GetType().Name}' has no IR v2 representation.",
                    expression.Line,
                    expression.Column);
                return null;
        }
    }

    // ------------------------------------------------------------------ types

    private static Mql5IrTypeReference? LowerType(Mql5TypeReference? type, LoweringContext context, int depth)
    {
        if (type is null)
        {
            context.Fail(MalformedNode, "A null type reference cannot be lowered.", 1, 1);
            return null;
        }

        if (!context.CheckDepth(depth, type.Line, type.Column))
        {
            return null;
        }

        List<Mql5IrArrayRank>? ranks = LowerRanks(type.ArrayRanks, context, depth, type.Line, type.Column);
        if (ranks is null)
        {
            return null;
        }

        string name = type.Name ?? string.Empty;
        return new(
            name,
            Mql5IrLiteral.ClassifyScalar(name),
            type.IsConst,
            type.IsPointer,
            type.IsReference,
            ranks,
            type.Line,
            type.Column);
    }

    private static List<Mql5IrArrayRank>? LowerRanks(
        IReadOnlyList<Mql5Expression?>? ranks,
        LoweringContext context,
        int depth,
        int line,
        int column)
    {
        if (!context.CheckDepth(depth, line, column))
        {
            return null;
        }

        var lowered = new List<Mql5IrArrayRank>();
        bool failed = false;
        foreach (Mql5Expression? rank in ranks ?? [])
        {
            if (rank is null)
            {
                lowered.Add(new(null, null));
                continue;
            }

            Mql5IrExpression? size = LowerExpression(rank, context, depth + 1);
            if (size is null)
            {
                failed = true;
                continue;
            }

            lowered.Add(new(size, FoldWhole(size)));
        }

        return failed ? null : lowered;
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Reads back the folded value a node already carries. Only literals and signed
    /// literals fold; no arithmetic is evaluated, because general constant folding
    /// belongs to the binder.
    /// </summary>
    private static long? FoldWhole(Mql5IrExpression? expression) => expression switch
    {
        Mql5IrLiteralExpression literal => literal.FoldedValue,
        Mql5IrUnaryExpression unary => unary.FoldedValue,
        _ => null
    };

    private static string? CanonicalDefault(Mql5IrExpression? expression)
    {
        switch (expression)
        {
            case null:
                return null;
            case Mql5IrLiteralExpression literal:
                return literal.CanonicalText;
            case Mql5IrNameExpression name:
                return name.Scope is null || name.Scope.Count == 0
                    ? name.Name
                    : string.Join("::", name.Scope) + "::" + name.Name;
            case Mql5IrUnaryExpression unary when unary.IsPrefix && unary.Operator is "-" or "+":
                if (unary.FoldedValue is not null)
                {
                    return unary.FoldedValue.Value.ToString(CultureInfo.InvariantCulture);
                }

                return unary.Operand is Mql5IrLiteralExpression inner && inner.LiteralKind == Mql5LiteralKind.Real
                    ? (unary.Operator == "-" ? "-" : string.Empty) + inner.CanonicalText
                    : null;
            default:
                return null;
        }
    }

    /// <summary>
    /// Detects the <c>operator</c> keyword used as a declaration name, while leaving
    /// ordinary identifiers such as <c>operatorCount</c> alone.
    /// </summary>
    private static bool IsOperatorName(string name)
    {
        const string Keyword = "operator";
        if (!name.StartsWith(Keyword, StringComparison.Ordinal))
        {
            return false;
        }

        if (name.Length == Keyword.Length)
        {
            return true;
        }

        char following = name[Keyword.Length];
        return !char.IsLetterOrDigit(following) && following != '_';
    }

    private static bool TryFlattenScope(Mql5Expression? qualifier, List<string> segments, int depth)
    {
        if (depth > MaximumDepth)
        {
            return false;
        }

        switch (qualifier)
        {
            case null:
                return true;
            case Mql5IdentifierExpression identifier:
                segments.Add(identifier.Name ?? string.Empty);
                return true;
            case Mql5ScopeExpression scope:
                if (!TryFlattenScope(scope.Qualifier, segments, depth + 1))
                {
                    return false;
                }

                segments.Add(scope.Name ?? string.Empty);
                return true;
            default:
                return false;
        }
    }

    private sealed class ModuleBuilder
    {
        public List<Mql5IrProperty> Properties { get; } = [];
        public List<Mql5IrInclude> Includes { get; } = [];
        public List<Mql5IrDefine> Defines { get; } = [];
        public List<Mql5IrImport> Imports { get; } = [];
        public List<Mql5IrEnumeration> Enums { get; } = [];
        public List<Mql5IrTypeDeclaration> Types { get; } = [];
        public List<Mql5IrGlobalVariable> Globals { get; } = [];
        public List<Mql5IrInput> Inputs { get; } = [];
        public List<Mql5IrFunction> Functions { get; } = [];
    }

    private sealed class LoweringContext
    {
        private bool depthReported;

        public List<Mql5RestrictedDiagnostic> Diagnostics { get; } = [];

        public bool Failed { get; private set; }

        public void Fail(string code, string message, int line, int column)
        {
            Failed = true;
            Diagnostics.Add(new(code, Mql5RestrictedDiagnosticSeverity.Error, message, line, column));
        }

        public void Note(string code, string message, int line, int column) =>
            Diagnostics.Add(new(code, Mql5RestrictedDiagnosticSeverity.Information, message, line, column));

        /// <summary>
        /// Guards against unbounded recursion on adversarial or generated sources.
        /// The limit is reported once so that a deep tree cannot flood diagnostics.
        /// </summary>
        public bool CheckDepth(int depth, int line, int column)
        {
            if (depth <= MaximumDepth)
            {
                return true;
            }

            Failed = true;
            if (!depthReported)
            {
                depthReported = true;
                Diagnostics.Add(new(
                    DepthLimitExceeded,
                    Mql5RestrictedDiagnosticSeverity.Error,
                    $"Syntax nesting exceeds the lowering limit of {MaximumDepth.ToString(CultureInfo.InvariantCulture)} levels.",
                    line,
                    column));
            }

            return false;
        }
    }
}
