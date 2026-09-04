using System.Globalization;
using YO4X.StrategyGovernance;

namespace YO4X.Mql5.CodeGen;

/// <summary>Declaration-level emission: the compilation unit, types and members.</summary>
internal sealed partial class Mql5GeneratorRun
{
    /// <summary>True while a method body of a module-declared struct or class is emitted.</summary>
    private bool _insideTypeBody;

    /// <summary>The type whose members are being emitted, or null at file scope.</summary>
    private Mql5IrTypeDeclaration? _currentTypeDeclaration;

    // ------------------------------------------------------------- type mapping

    /// <summary>
    /// The CLR spelling of a written MQL5 type, ignoring array rank. Null means the
    /// name maps onto nothing this emitter can name, which is always a diagnostic and
    /// never a guess.
    /// </summary>
    private string? CoreTypeName(Mql5IrTypeReference type)
    {
        if (type.Scalar != Mql5IrScalarKind.None)
        {
            return Mql5ClrTypes.Spell(type.Scalar);
        }

        if (_typeParametersInScope.Contains(type.Name))
        {
            // A type parameter is spelled through: the emitted declaration carries the same
            // name in its C# type parameter list.
            return Mql5ClrTypes.Identifier(type.Name);
        }

        if (_typeNames.TryGetValue(type.Name, out string? declared))
        {
            return declared;
        }

        if (_enumTypeNames.TryGetValue(type.Name, out string? enumeration))
        {
            return enumeration;
        }

        if (Mql5ClrTypes.RuntimeTypeNames.Contains(type.Name))
        {
            return Mql5ClrTypes.Identifier(Mql5ClrTypes.RuntimeTypeName(type.Name));
        }

        // A built-in MQL5 enumeration is an int-sized integer type, and its members are already
        // emitted as integer constants. Spelling the type as `int` is therefore the faithful
        // mapping, not a fallback: MQL5 converts between an enumeration and an integer freely, so
        // a real C# enum would only add casts at every use without adding a distinction MQL5 makes.
        if (type.Name.StartsWith("ENUM_", StringComparison.Ordinal)
            || Mql5BuiltinConstants.EnumNames.Contains(type.Name))
        {
            return "int";
        }

        return null;
    }

    /// <summary>The CLR spelling of a written type together with its declarator ranks.</summary>
    private string TypeText(Mql5IrTypeReference type, IReadOnlyList<Mql5IrArrayRank> extraRanks)
    {
        string? core = CoreTypeName(type);
        if (core is null)
        {
            return Fail(
                Mql5CodeGenDiagnosticCodes.UnsupportedType,
                "The type '" + type.Name + "' maps onto no CLR type.",
                type.Line,
                type.Column);
        }

        int rank = type.ArrayRanks.Count + extraRanks.Count;
        return rank switch
        {
            0 => core,
            1 => core + "[]",
            2 => core + "[][]",
            3 => core + "[][][]",
            _ => Fail(
                Mql5CodeGenDiagnosticCodes.UnsupportedArrayRank,
                "An array of rank " + rank.ToString(CultureInfo.InvariantCulture) + " is not modelled.",
                type.Line,
                type.Column)
        };
    }

    /// <summary>
    /// The right-hand side of one declaration, with brace initialisers resolved against
    /// the declared type.
    ///
    /// <c>MqlTradeRequest request = {0}</c> is the idiomatic MQL5 way of zeroing a
    /// structure; C# spells the same thing <c>new MqlTradeRequest()</c>. A brace list
    /// that sets anything other than zero is refused, because pairing its items with
    /// fields would mean guessing at a declaration order the IR does not promise.
    /// </summary>
    private string ValueText(
        Mql5IrTypeReference type,
        IReadOnlyList<Mql5IrArrayRank> ranks,
        Mql5IrExpression initializer,
        int depth)
    {
        Mql5ResolvedType target = ResolveWrittenType(type, ranks);
        if (initializer is not Mql5IrInitializerListExpression list)
        {
            return ConvertTo(target, TypeOf(initializer), Expr(initializer, depth));
        }

        if (target.IsArray)
        {
            return ArrayLiteral(type, list, depth);
        }

        if (target.Kind is Mql5ResolvedTypeKind.Structure or Mql5ResolvedTypeKind.Class
            && IsZeroInitializer(list))
        {
            string clr = TypeText(type, ranks);
            return "new " + clr + "(" + ConstructionArguments(clr) + ")" + ConstructionInitializer(clr);
        }

        return Fail(
            Mql5CodeGenDiagnosticCodes.UnsupportedInitializer,
            "A brace initialiser for '" + type.Name + "' cannot be matched to fields.",
            list.Line,
            list.Column);
    }

    private static bool IsZeroInitializer(Mql5IrInitializerListExpression list)
    {
        foreach (Mql5IrExpression item in list.Items)
        {
            if (item is not Mql5IrLiteralExpression literal || literal.FoldedValue is not 0L)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The resolved form of a written type, used to drive implicit conversions.</summary>
    private Mql5ResolvedType ResolveWrittenType(Mql5IrTypeReference type, IReadOnlyList<Mql5IrArrayRank> extraRanks)
    {
        int rank = type.ArrayRanks.Count + extraRanks.Count;
        if (type.Scalar != Mql5IrScalarKind.None)
        {
            return Mql5ResolvedType.ForScalar(type.Scalar).WithArrayRank(rank);
        }

        if (_enumTypeNames.TryGetValue(type.Name, out string? enumeration))
        {
            return new Mql5ResolvedType(
                Mql5ResolvedTypeKind.Enumeration, Mql5IrScalarKind.None, enumeration, rank, false, false);
        }

        if (type.Name.StartsWith("ENUM_", StringComparison.Ordinal)
            || Mql5BuiltinConstants.EnumNames.Contains(type.Name))
        {
            return new Mql5ResolvedType(
                Mql5ResolvedTypeKind.Enumeration, Mql5IrScalarKind.None, type.Name, rank, false, true);
        }

        if (_typeNames.TryGetValue(type.Name, out string? declared))
        {
            bool isStruct = _typeDeclarations.TryGetValue(type.Name, out Mql5IrTypeDeclaration? decl)
                && string.Equals(decl.Keyword, "struct", StringComparison.Ordinal);
            return new Mql5ResolvedType(
                isStruct ? Mql5ResolvedTypeKind.Structure : Mql5ResolvedTypeKind.Class,
                Mql5IrScalarKind.None,
                declared,
                rank,
                type.IsPointer,
                false);
        }

        if (Mql5ClrTypes.RuntimeTypeNames.Contains(type.Name))
        {
            return new Mql5ResolvedType(
                Mql5ResolvedTypeKind.Structure, Mql5IrScalarKind.None, type.Name, rank, type.IsPointer, true);
        }

        return Mql5ResolvedType.Unknown;
    }

    /// <summary>
    /// The creation expression for an array declarator. An unsized dimension becomes an
    /// empty array rather than null, so that a generated strategy never dereferences a
    /// null where MQL5 would have had a zero-length array.
    /// </summary>
    private string ArrayCreation(
        string core,
        Mql5IrTypeReference type,
        IReadOnlyList<Mql5IrArrayRank> extraRanks,
        int depth)
    {
        List<Mql5IrArrayRank> ranks = [.. type.ArrayRanks, .. extraRanks];
        if (ranks.Count == 1)
        {
            Mql5IrExpression? size = ranks[0].Size;
            return size is null
                ? "System.Array.Empty<" + core + ">()"
                : "new " + core + "[" + IndexText(size, depth + 1) + "]";
        }

        // MQL5 declares a multi-dimensional array with every dimension but the first fixed, so a
        // rank-2 or rank-3 declaration allocates the whole shape up front. A jagged C# array left
        // with null rows compiles and then throws on the first element access.
        if (ranks.Count == 3
            && ranks[0].Size is not null && ranks[1].Size is not null && ranks[2].Size is not null)
        {
            return "Mql5Ops.NewArray3<" + core + ">("
                + IndexText(ranks[0].Size!, depth + 1) + ", "
                + IndexText(ranks[1].Size!, depth + 1) + ", "
                + IndexText(ranks[2].Size!, depth + 1) + ")";
        }

        if (ranks.Count == 3)
        {
            return "System.Array.Empty<" + core + "[][]>()";
        }

        if (ranks[0].Size is not null && ranks[1].Size is not null)
        {
            return "Mql5Ops.NewArray2<" + core + ">("
                + IndexText(ranks[0].Size!, depth + 1) + ", "
                + IndexText(ranks[1].Size!, depth + 1) + ")";
        }

        return "System.Array.Empty<" + core + "[]>()";
    }

    // -------------------------------------------------------- compilation unit

    private void EmitCompilationUnit()
    {
        _writer.Line("// <auto-generated />");
        _writer.Line("// Generated from " + (_module.SourcePath.Length == 0 ? "an unnamed module" : _module.SourcePath));
        _writer.Line("// IR digest: " + _module.IrSha256);
        _writer.Line("#nullable enable");
        _writer.Blank();
        _writer.Line("namespace " + Mql5RuntimeContract.GeneratedNamespace + ";");
        _writer.Blank();
        _writer.Line("using " + Mql5RuntimeContract.RuntimeNamespace + ";");
        _writer.Blank();
        _writer.Line(Mql5EmittedHelpers.Source);
        _writer.Blank();

        if (_module.Imports.Count != 0)
        {
            Note(
                Mql5CodeGenDiagnosticCodes.ImportsIgnored,
                "The module declares " + _module.Imports.Count.ToString(CultureInfo.InvariantCulture)
                    + " import block(s); imported prototypes are not emitted.",
                _module.Imports[0].Line,
                _module.Imports[0].Column);
        }

        foreach (Mql5IrEnumeration enumeration in _module.Enums)
        {
            EmitEnum(enumeration);
            _writer.Blank();
        }

        foreach (Mql5IrTypeDeclaration declaration in _module.Types)
        {
            EmitTypeDeclaration(declaration, 0);
            _writer.Blank();
        }

        EmitStrategyClass();
    }

    // ------------------------------------------------------------ enumerations

    private void EmitEnum(Mql5IrEnumeration enumeration)
    {
        RefuseReservedName(enumeration.Name, enumeration.Line, enumeration.Column);

        bool needsWide = enumeration.Members.Any(
            member => member.FoldedValue is > int.MaxValue or < int.MinValue);

        _writer.EndLineDirectives();
        _writer.LineDirective(enumeration.Line);
        _writer.Line(
            "public enum " + Mql5ClrTypes.Identifier(enumeration.Name) + (needsWide ? " : long" : string.Empty));
        _writer.OpenBrace();

        string? previousEnum = _currentEnumName;
        _currentEnumName = enumeration.Name;
        for (int index = 0; index < enumeration.Members.Count; index++)
        {
            Mql5IrEnumMember member = enumeration.Members[index];
            string suffix = index == enumeration.Members.Count - 1 ? string.Empty : ",";
            _writer.LineDirective(member.Line);
            if (member.FoldedValue is long folded)
            {
                _writer.Line(
                    Mql5ClrTypes.Identifier(member.Name) + " = "
                    + folded.ToString(CultureInfo.InvariantCulture) + (needsWide ? "L" : string.Empty) + suffix);
            }
            else if (member.Value is not null)
            {
                _writer.Line(Mql5ClrTypes.Identifier(member.Name) + " = " + Expr(member.Value, 1) + suffix);
            }
            else
            {
                _writer.Line(Mql5ClrTypes.Identifier(member.Name) + suffix);
            }
        }

        _currentEnumName = previousEnum;
        _writer.CloseBrace();
        _writer.EndLineDirectives();
    }

    // ------------------------------------------------------- type declarations

    /// <summary>
    /// The C# type parameter list for a template declaration, or empty when there is none.
    /// </summary>
    /// <remarks>
    /// MQL5 templates map onto C# generics directly. They are carried through rather than
    /// monomorphised because MQL5 deduces the arguments at each call site, and a call site can sit
    /// in a translation unit this compiler never sees.
    /// </remarks>
    private static string TypeParameterList(IReadOnlyList<string> typeParameters) =>
        typeParameters.Count == 0
            ? string.Empty
            : "<" + string.Join(", ", typeParameters.Select(Mql5ClrTypes.Identifier)) + ">";

    /// <summary>
    /// The declared name with any template argument list removed.
    /// </summary>
    /// <remarks>
    /// A base type is written as <c>MDL_Condition&lt;double,int&gt;</c>, but the declaration it
    /// refers to is registered under the bare name. Looking up the written spelling finds nothing
    /// and reports the base as mapping onto no CLR type, which is a diagnostic about a type that
    /// does exist.
    /// </remarks>
    private static string BareTypeName(string writtenName)
    {
        int angle = writtenName.IndexOf(char.Parse("<"), StringComparison.Ordinal);
        return angle < 0 ? writtenName : writtenName[..angle];
    }

    /// <summary>
    /// The out-of-line definition of <paramref name="method"/>, or null when it has none.
    /// </summary>
    /// <remarks>
    /// Matched on member name and parameter count rather than on name alone: a type may declare
    /// two overloads of the same member, and giving both the first definition found would emit one
    /// body twice and drop the other.
    /// </remarks>
    private Mql5IrFunction? OutOfLineBody(Mql5IrTypeDeclaration owner, Mql5IrFunction method)
    {
        if (!_outOfLineDefinitions.TryGetValue(owner.Name, out List<Mql5IrFunction>? members))
        {
            return null;
        }

        foreach (Mql5IrFunction candidate in members)
        {
            if (SplitQualified(candidate.Name) is (_, string member)
                && string.Equals(member, method.Name, StringComparison.Ordinal)
                && candidate.Parameters.Count == method.Parameters.Count)
            {
                return candidate;
            }
        }

        return null;
    }

    private void EmitTypeDeclaration(Mql5IrTypeDeclaration declaration, int depth)
    {
        if (!Budget(depth, declaration.Line, declaration.Column))
        {
            return;
        }

        RefuseReservedName(declaration.Name, declaration.Line, declaration.Column);

        // The type parameters must be in scope before anything inside the declaration is written:
        // a field declared `T1 GroupMode;` is emitted before the first method, and would otherwise
        // report that T1 maps onto no CLR type.
        foreach (string typeParameter in declaration.TypeParameters)
        {
            _typeParametersInScope.Add(typeParameter);
        }

        bool isStruct = string.Equals(declaration.Keyword, "struct", StringComparison.Ordinal);
        bool isInterface = string.Equals(declaration.Keyword, "interface", StringComparison.Ordinal);
        string keyword = isStruct ? "struct" : isInterface ? "interface" : "class";

        string baseClause = string.Empty;
        if (!string.IsNullOrEmpty(declaration.BaseTypeName))
        {
            if (isStruct)
            {
                Fail(
                    Mql5CodeGenDiagnosticCodes.UnsupportedTypeDeclaration,
                    "A struct with a base type has no C# equivalent: '" + declaration.Name + "'.",
                    declaration.Line,
                    declaration.Column);
            }
            else if (_typeNames.TryGetValue(BareTypeName(declaration.BaseTypeName), out string? baseName))
            {
                // The written spelling carries the argument list, which the emitted base clause
                // needs; only the lookup wanted the bare name.
                int angle = declaration.BaseTypeName.IndexOf(char.Parse("<"), StringComparison.Ordinal);
                baseClause = " : " + baseName
                    + (angle < 0 ? string.Empty : declaration.BaseTypeName[angle..]);
            }
            else if (Mql5ClrTypes.RuntimeTypeNames.Contains(BareTypeName(declaration.BaseTypeName)))
            {
                baseClause = " : " + Mql5ClrTypes.Identifier(Mql5ClrTypes.RuntimeTypeName(declaration.BaseTypeName));
            }
            else
            {
                Fail(
                    Mql5CodeGenDiagnosticCodes.UnsupportedType,
                    "The base type '" + declaration.BaseTypeName + "' maps onto no CLR type.",
                    declaration.Line,
                    declaration.Column);
            }
        }

        _writer.EndLineDirectives();
        _writer.LineDirective(declaration.Line);
        _writer.Line(
            "public " + (declaration.Methods.Any(method => method.IsAbstract) ? "abstract " : string.Empty)
                + keyword + " " + Mql5ClrTypes.Identifier(declaration.Name)
                + TypeParameterList(declaration.TypeParameters) + baseClause);
        _writer.OpenBrace();

        bool inheritsRuntime = declaration.BaseTypeName is not null
            && _typeNames.ContainsKey(declaration.BaseTypeName);
        if (!isInterface && !inheritsRuntime)
        {
            // A module type's methods call MQL5 built-ins as freely as file-scope code does, and in
            // MetaTrader those built-ins are ambient. Here they are members of the runtime, which
            // lives on the strategy — and a C# nested type has no path to its enclosing instance.
            // Giving the type its own runtime reference, under the same name the strategy uses,
            // makes every call inside a method body resolve without rewriting any of them. The
            // reference is filled in by the object initialiser at each construction site.
            //
            // The owner reference beside it serves the same purpose for file-scope globals and
            // inputs, which are fields on the strategy rather than members of the runtime.
            _writer.EndLineDirectives();
            _writer.Line(
                "internal IMql5Runtime " + Mql5RuntimeContract.RuntimeFieldName
                + (isStruct ? ";" : " = null!;"));
            _writer.Line(
                "internal " + StrategyTypeName + " " + OwnerFieldName
                + (isStruct ? ";" : " = null!;"));
            _writer.Blank();
        }

        bool hasFieldInitializer = false;
        foreach (Mql5IrField field in declaration.Fields)
        {
            hasFieldInitializer |= EmitField(field, depth + 1, isInterface);
        }

        bool hasConstructor = declaration.Methods.Any(
            method => string.Equals(method.Name, declaration.Name, StringComparison.Ordinal));

        if (isStruct && hasFieldInitializer && !hasConstructor)
        {
            // C# requires a struct with field initialisers to declare a constructor.
            _writer.EndLineDirectives();
            _writer.Blank();
            _writer.Line("public " + Mql5ClrTypes.Identifier(declaration.Name) + "()");
            _writer.OpenBrace();
            _writer.CloseBrace();
        }

        bool previousInsideType = _insideTypeBody;
        _insideTypeBody = true;
        Mql5IrTypeDeclaration? previousType = _currentTypeDeclaration;
        _currentTypeDeclaration = declaration;
        foreach (Mql5IrFunction method in declaration.Methods)
        {
            _writer.Blank();
            EmitMethod(
                method.Body is null && OutOfLineBody(declaration, method) is Mql5IrFunction defined
                    ? method with { Body = defined.Body }
                    : method,
                declaration,
                isInterface,
                depth + 1);
        }

        _insideTypeBody = previousInsideType;
        _currentTypeDeclaration = previousType;
        foreach (string typeParameter in declaration.TypeParameters)
        {
            _typeParametersInScope.Remove(typeParameter);
        }

        foreach (Mql5IrEnumeration nested in declaration.NestedEnums)
        {
            _writer.Blank();
            EmitEnum(nested);
        }

        foreach (Mql5IrTypeDeclaration nested in declaration.NestedTypes)
        {
            _writer.Blank();
            EmitTypeDeclaration(nested, depth + 1);
        }

        _writer.EndLineDirectives();
        _writer.CloseBrace();
    }

    /// <summary>Emits one field. Returns true when it carried an inline initialiser.</summary>
    private bool EmitField(Mql5IrField field, int depth, bool isInterface)
    {
        if (isInterface)
        {
            Fail(
                Mql5CodeGenDiagnosticCodes.UnsupportedTypeDeclaration,
                "An interface cannot declare the field '" + field.Name + "'.",
                field.Line,
                field.Column);
            return false;
        }

        RefuseReservedName(field.Name, field.Line, field.Column);

        string typeText = TypeText(field.Type, field.ArrayRanks);
        // MQL5 access is not reproduced. A generated file is one translation unit containing the
        // strategy and every type it declares, and MQL5 permits things inside a module that C#
        // accessibility would forbid across a type boundary — an out-of-line definition assigning a
        // private static member is the common one. Enforcing the source access here would refuse
        // programs MQL5 compiles, and enforcing it buys nothing: nothing outside the generated file
        // can reach these types at all.
        const string access = "internal";

        string modifiers = access + (field.IsStatic ? " static" : string.Empty);
        string? initializer = null;

        if (field.Initializer is not null)
        {
            if (ReferencesInstanceState(field.Initializer, 0))
            {
                Fail(
                    Mql5CodeGenDiagnosticCodes.UnsupportedInitializer,
                    "The initialiser of '" + field.Name + "' reads instance state, which a C# field initialiser cannot.",
                    field.Line,
                    field.Column);
            }
            else
            {
                initializer = ValueText(field.Type, field.ArrayRanks, field.Initializer, depth + 1);
            }
        }
        else if (field.Type.ArrayRanks.Count + field.ArrayRanks.Count > 0)
        {
            string? core = CoreTypeName(field.Type);
            if (core is not null)
            {
                initializer = ArrayCreation(core, field.Type, field.ArrayRanks, depth);
            }
        }

        _writer.LineDirective(field.Line);
        _writer.Line(
            modifiers + " " + typeText + " " + Mql5ClrTypes.Identifier(field.Name)
            + (initializer is null ? string.Empty : " = " + initializer) + ";");
        return initializer is not null;
    }

    /// <summary>
    /// True when an expression reads anything that only exists once an instance does.
    /// A C# field initialiser runs before the constructor body and cannot do that.
    /// </summary>
    private bool ReferencesInstanceState(Mql5IrExpression expression, int depth)
    {
        if (depth > MaxDepth)
        {
            return true;
        }

        switch (expression)
        {
            case Mql5IrLiteralExpression:
            case Mql5IrSizeOfExpression:
                return false;
            case Mql5IrNameExpression name:
                Mql5ResolvedSymbol? symbol = _model.SymbolOf(name);
                if (symbol is not null
                    && symbol.Kind is Mql5SymbolKind.GlobalVariable or Mql5SymbolKind.Input
                        or Mql5SymbolKind.Field or Mql5SymbolKind.LocalVariable or Mql5SymbolKind.Parameter)
                {
                    return true;
                }

                return _fileScopeVariables.Contains(name.Name);
            case Mql5IrCallExpression:
            case Mql5IrNewExpression:
                return true;
            case Mql5IrUnaryExpression unary:
                return ReferencesInstanceState(unary.Operand, depth + 1);
            case Mql5IrBinaryExpression binary:
                return ReferencesInstanceState(binary.Left, depth + 1)
                    || ReferencesInstanceState(binary.Right, depth + 1);
            case Mql5IrConditionalExpression conditional:
                return ReferencesInstanceState(conditional.Condition, depth + 1)
                    || ReferencesInstanceState(conditional.WhenTrue, depth + 1)
                    || ReferencesInstanceState(conditional.WhenFalse, depth + 1);
            case Mql5IrCastExpression cast:
                return ReferencesInstanceState(cast.Operand, depth + 1);
            case Mql5IrInitializerListExpression list:
                return list.Items.Any(item => ReferencesInstanceState(item, depth + 1));
            default:
                return true;
        }
    }

    // ------------------------------------------------------------- the strategy

    private void EmitStrategyClass()
    {
        _writer.EndLineDirectives();
        _writer.Line("/// <summary>Generated from " + XmlText(_module.SourcePath) + ".</summary>");
        _writer.Line("public sealed class " + StrategyTypeName + " : IMql5Strategy");
        _writer.OpenBrace();

        _writer.Line("private readonly IMql5Runtime " + Mql5RuntimeContract.RuntimeFieldName + ";");
        _writer.Blank();

        EmitInputFields();
        EmitGlobalFields();
        EmitStaticLocalFields();
        EmitConstructor();

        foreach (Mql5IrFunction function in _module.Functions)
        {
            if (function.Body is null)
            {
                continue;
            }

            _writer.Blank();
            EmitMethod(function, owner: null, isInterface: false, depth: 1);
        }

        EmitEntryPointShims();

        _writer.EndLineDirectives();
        _writer.CloseBrace();
    }

    /// <summary>
    /// Inputs become public fields rather than properties. MQL5 passes an input to a
    /// built-in that writes through a reference — <c>ArrayResize</c> and its family —
    /// and C# cannot take a <c>ref</c> to a property, so a property here would turn a
    /// legal MQL5 program into an ungeneratable one.
    /// </summary>
    private void EmitInputFields()
    {
        foreach (Mql5IrInput input in _module.Inputs)
        {
            RefuseReservedName(input.Name, input.Line, input.Column);
            if (input.Label is not null)
            {
                _writer.EndLineDirectives();
                _writer.Line("/// <summary>" + XmlText(input.Label) + "</summary>");
            }

            _writer.LineDirective(input.Line);
            _writer.Line(
                "public " + TypeText(input.Type, input.ArrayRanks) + " "
                + Mql5ClrTypes.Identifier(input.Name) + ";");
        }

        if (_module.Inputs.Count != 0)
        {
            _writer.EndLineDirectives();
            _writer.Blank();
        }
    }

    private void EmitGlobalFields()
    {
        foreach (Mql5IrGlobalVariable global in _module.Globals)
        {
            if (SplitQualified(global.Name) is not null)
            {
                // An out-of-line definition of a static member the type already declares. The
                // field exists; only its initial value belongs here, and that is written in the
                // constructor alongside every other file-scope initialiser.
                continue;
            }

            RefuseReservedName(global.Name, global.Line, global.Column);
            _writer.LineDirective(global.Line);
            _writer.Line(
                "public " + TypeText(global.Type, global.ArrayRanks) + " "
                + Mql5ClrTypes.Identifier(global.Name) + ";");
        }

        if (_module.Globals.Count != 0)
        {
            _writer.EndLineDirectives();
            _writer.Blank();
        }
    }

    private void EmitStaticLocalFields()
    {
        foreach (StaticLocal local in _staticLocals)
        {
            _writer.LineDirective(local.Variable.Line);
            _writer.Line(
                "private " + TypeText(local.Type, local.Variable.ArrayRanks) + " " + local.FieldName + ";");
        }

        if (_staticLocals.Count != 0)
        {
            _writer.EndLineDirectives();
            _writer.Blank();
        }
    }

    /// <summary>
    /// Every file-scope initialiser runs here, in source order.
    ///
    /// MQL5 initialises globals and inputs in declaration order before the first
    /// handler runs, and those initialisers routinely read one another. A C# field
    /// initialiser cannot, so the whole sequence moves into the constructor, which
    /// preserves both the order and the ability to read what came before.
    /// </summary>
    private void EmitConstructor()
    {
        _writer.EndLineDirectives();
        _writer.Line("/// <summary>Creates the strategy over one runtime context.</summary>");
        _writer.Line("public " + StrategyTypeName + "(IMql5Runtime runtime)");
        _writer.OpenBrace();
        _writer.Line(
            Mql5RuntimeContract.RuntimeFieldName
            + " = runtime ?? throw new System.ArgumentNullException(nameof(runtime));");

        foreach (Mql5IrInput input in _module.Inputs)
        {
            EmitFileScopeInitializer(
                input.Name, input.Type, input.ArrayRanks, input.DefaultValue, input.Line);
        }

        foreach (Mql5IrGlobalVariable global in _module.Globals)
        {
            EmitFileScopeInitializer(
                global.Name, global.Type, global.ArrayRanks, global.Initializer, global.Line);
        }

        foreach (StaticLocal local in _staticLocals)
        {
            EmitStaticLocalInitializer(local);
        }

        _writer.EndLineDirectives();
        _writer.CloseBrace();
    }

    private void EmitFileScopeInitializer(
        string name,
        Mql5IrTypeReference type,
        IReadOnlyList<Mql5IrArrayRank> ranks,
        Mql5IrExpression? initializer,
        int line)
    {
        // A qualified name is the out-of-line definition of a static member: it assigns to the
        // field the type already declares, so `Owner::Member` becomes `Owner.Member`.
        string identifier = SplitQualified(name) is (string owner, string member)
            ? Mql5ClrTypes.Identifier(_typeNames.GetValueOrDefault(owner, owner)) + "."
                + Mql5ClrTypes.Identifier(member)
            : Mql5ClrTypes.Identifier(name);
        _writer.LineDirective(line);
        if (initializer is not null)
        {
            string value = ValueText(type, ranks, initializer, 1);
            _writer.Line(identifier + " = " + value + ";");
            return;
        }

        if (type.ArrayRanks.Count + ranks.Count > 0)
        {
            string? core = CoreTypeName(type);
            _writer.Line(
                identifier + " = " + (core is null ? PoisonToken : ArrayCreation(core, type, ranks, 1)) + ";");
            return;
        }

        Mql5ResolvedType resolved = ResolveWrittenType(type, ranks);
        if (resolved.Kind is Mql5ResolvedTypeKind.Structure or Mql5ResolvedTypeKind.Class
            && !type.IsPointer)
        {
            // MQL5 gives a file-scope structure or object zeroed storage before the first handler
            // runs, exactly as it does a local. Leaving the field null instead compiles cleanly and
            // then throws on the first use — which for a `CTrade` global is the first order the
            // strategy tries to place.
            string clr = TypeText(type, ranks);
            _writer.Line(
                identifier + " = new " + clr + "(" + ConstructionArguments(clr) + ")"
                    + ConstructionInitializer(clr) + ";");
            return;
        }

        if (type.Scalar == Mql5IrScalarKind.Text || type.Scalar == Mql5IrScalarKind.Moment)
        {
            _writer.Line(identifier + " = " + Mql5ClrTypes.DefaultFor(type.Scalar) + ";");
        }
    }

    private void EmitStaticLocalInitializer(StaticLocal local)
    {
        _writer.LineDirective(local.Variable.Line);
        if (local.Variable.Initializer is not null)
        {
            string value = ValueText(local.Type, local.Variable.ArrayRanks, local.Variable.Initializer, 1);
            _writer.Line(local.FieldName + " = " + value + ";");
            return;
        }

        if (local.Type.ArrayRanks.Count + local.Variable.ArrayRanks.Count > 0)
        {
            string? core = CoreTypeName(local.Type);
            _writer.Line(
                local.FieldName + " = "
                + (core is null ? PoisonToken : ArrayCreation(core, local.Type, local.Variable.ArrayRanks, 1))
                + ";");
            return;
        }

        if (local.Type.Scalar == Mql5IrScalarKind.Text || local.Type.Scalar == Mql5IrScalarKind.Moment)
        {
            _writer.Line(local.FieldName + " = " + Mql5ClrTypes.DefaultFor(local.Type.Scalar) + ";");
        }
    }

    // ---------------------------------------------------------------- methods

    private void EmitMethod(Mql5IrFunction function, Mql5IrTypeDeclaration? owner, bool isInterface, int depth)
    {
        bool isConstructor = owner is not null
            && string.Equals(function.Name, owner.Name, StringComparison.Ordinal);

        if (function.Name.StartsWith('~') || function.Name.StartsWith("operator", StringComparison.Ordinal))
        {
            Fail(
                Mql5CodeGenDeclarationDiagnosticCodes.UnsupportedMember,
                "The member '" + function.Name + "' has no faithful C# spelling.",
                function.Line,
                function.Column);
            return;
        }

        RefuseReservedName(function.Name, function.Line, function.Column);

        // `__FUNCTION__` expands to the name of the function it appears in, so the emitter has to
        // know which one that is while a body is being written.
        foreach (string typeParameter in function.TypeParameters)
        {
            _typeParametersInScope.Add(typeParameter);
        }

        string enclosingFunction = _currentFunctionName;
        _currentFunctionName = function.Name;
        IReadOnlyDictionary<(int Line, int Column), string> enclosingShadows = _shadowedLocals;
        _shadowedLocals = Mql5ShadowedLocals.Resolve(function);

        try
        {
            EmitMethodCore(function, owner, isInterface, depth, isConstructor);
        }
        finally
        {
            _currentFunctionName = enclosingFunction;
            _shadowedLocals = enclosingShadows;
            foreach (string typeParameter in function.TypeParameters)
            {
                _typeParametersInScope.Remove(typeParameter);
            }
        }
    }

    private void EmitMethodCore(
        Mql5IrFunction function,
        Mql5IrTypeDeclaration? owner,
        bool isInterface,
        int depth,
        bool isConstructor)
    {

        string parameters = ParameterList(function, depth);
        string returnText = isConstructor ? string.Empty : TypeText(function.ReturnType, []) + " ";

        string modifiers;
        if (isInterface)
        {
            modifiers = string.Empty;
        }
        else
        {
            string access = owner is null
                ? "public"
                : function.Access switch
                {
                    Mql5Access.Private => "private",
                    Mql5Access.Protected => "protected",
                    _ => "public"
                };
            string binding = function.IsAbstract
                ? " abstract"
                : function.IsStatic
                ? " static"
                : !isConstructor && function.IsVirtual
                    ? OverridesBase(function, owner) ? " override" : " virtual"
                    : string.Empty;
            modifiers = access + binding + " ";
        }

        _writer.EndLineDirectives();
        _writer.LineDirective(function.Line);
        _writer.Line(
            modifiers + returnText + Mql5ClrTypes.Identifier(function.Name)
                + TypeParameterList(function.TypeParameters) + "(" + parameters + ")");

        if (function.Body is null)
        {
            if (function.IsAbstract)
            {
                // MQL5 spells a pure virtual as `= 0`, which is an abstract member and not a
                // missing definition. The declaring type is marked abstract alongside it, because
                // C# refuses an abstract member on a concrete class where MQL5 only complains at
                // the point of instantiation.
                _writer.Line(";");
                _writer.EndLineDirectives();
                return;
            }

            if (isInterface)
            {
                _writer.Line(";");
                _writer.EndLineDirectives();
                return;
            }

            Fail(
                Mql5CodeGenDeclarationDiagnosticCodes.UnsupportedMember,
                "The function '" + function.Name + "' has no body in this module.",
                function.Line,
                function.Column);
            _writer.OpenBrace();
            _writer.Line(PoisonToken + ";");
            _writer.CloseBrace();
            _writer.EndLineDirectives();
            return;
        }

        Mql5ResolvedType previousReturn = _currentReturnType;
        _currentReturnType = isConstructor
            ? Mql5ResolvedType.Nothing
            : ResolveWrittenType(function.ReturnType, []);

        _staticLocalNames.Clear();
        if (owner is null)
        {
            foreach (StaticLocal local in _staticLocals)
            {
                if (local.FieldName.StartsWith(
                    "__static_" + function.Name + "_", StringComparison.Ordinal))
                {
                    _staticLocalNames[local.Variable.Name] = local.FieldName;
                }
            }
        }

        EmitBlock(function.Body, depth);
        _staticLocalNames.Clear();
        _currentReturnType = previousReturn;
        _writer.EndLineDirectives();
    }

    private bool OverridesBase(Mql5IrFunction function, Mql5IrTypeDeclaration? owner)
    {
        string? baseName = owner?.BaseTypeName;
        int guard = 0;
        while (!string.IsNullOrEmpty(baseName) && guard++ < 32)
        {
            if (!_typeDeclarations.TryGetValue(baseName, out Mql5IrTypeDeclaration? baseType))
            {
                return false;
            }

            if (baseType.Methods.Any(
                method => string.Equals(method.Name, function.Name, StringComparison.Ordinal)))
            {
                return true;
            }

            baseName = baseType.BaseTypeName;
        }

        return false;
    }

    private string ParameterList(Mql5IrFunction function, int depth)
    {
        var parts = new List<string>(function.Parameters.Count);
        foreach (Mql5IrParameter parameter in function.Parameters)
        {
            string typeText = TypeText(parameter.Type, []);
            bool byRef = ParameterIsByRef(parameter);
            string text = (byRef ? "ref " : string.Empty) + typeText + " "
                + Mql5ClrTypes.Identifier(parameter.Name);

            if (parameter.DefaultValue is not null)
            {
                if (byRef)
                {
                    Fail(
                        Mql5CodeGenDeclarationDiagnosticCodes.UnsupportedDefaultArgument,
                        "A by-reference parameter cannot carry a default value: '" + parameter.Name + "'.",
                        parameter.Line,
                        parameter.Column);
                }
                else if (TryConstantText(parameter.DefaultValue, depth, out string? constant))
                {
                    Mql5ResolvedType target = ResolveWrittenType(parameter.Type, []);
                    text += " = " + ConvertTo(target, TypeOf(parameter.DefaultValue), constant);
                }
                else
                {
                    Fail(
                        Mql5CodeGenDeclarationDiagnosticCodes.UnsupportedDefaultArgument,
                        "The default for '" + parameter.Name + "' is not a C# compile-time constant.",
                        parameter.Line,
                        parameter.Column);
                    text += " = " + PoisonToken;
                }
            }

            parts.Add(text);
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// An MQL5 array parameter is already a reference in C#, and a <c>const</c>
    /// reference exists only to avoid a copy, so neither becomes <c>ref</c>. What
    /// remains — a mutable scalar or structure reference — must, or the callee's writes
    /// would be lost.
    /// </summary>
    private static bool ParameterIsByRef(Mql5IrParameter parameter) =>
        parameter.Type.IsReference
        && !parameter.Type.IsConst
        && parameter.Type.ArrayRanks.Count == 0;

    private bool TryConstantText(Mql5IrExpression expression, int depth, out string text)
    {
        switch (expression)
        {
            case Mql5IrLiteralExpression literal:
                text = Expr(literal, depth + 1);
                return true;
            case Mql5IrUnaryExpression unary
                when unary.IsPrefix
                    && unary.Operator is "-" or "+"
                    && unary.Operand is Mql5IrLiteralExpression:
                text = Expr(unary, depth + 1);
                return true;
            case Mql5IrNameExpression name:
                Mql5ResolvedSymbol? symbol = _model.SymbolOf(name);
                bool constant = _enumMemberOwner.ContainsKey(name.Name)
                    || Mql5BuiltinConstants.IsKnown(name.Name)
                    || symbol?.Kind is Mql5SymbolKind.EnumMember or Mql5SymbolKind.BuiltinConstant
                        or Mql5SymbolKind.Define;
                text = constant ? Expr(name, depth + 1) : string.Empty;
                return constant;
            default:
                text = string.Empty;
                return false;
        }
    }

    // ------------------------------------------------------------ entry points

    private void EmitEntryPointShims()
    {
        _writer.EndLineDirectives();
        EmitShim("OnInit", "void IMql5Strategy.OnInit()", []);
        EmitShim("OnTick", "void IMql5Strategy.OnTick()", []);
        EmitShim("OnDeinit", "void IMql5Strategy.OnDeinit(int reason)", ["reason"]);
    }

    private void EmitShim(string handler, string signature, IReadOnlyList<string> available)
    {
        _writer.Blank();
        _writer.Line("/// <summary>Routes the strategy interface onto the module's " + handler + " handler.</summary>");
        _writer.Line(signature);
        _writer.OpenBrace();

        if (_functions.TryGetValue(handler, out List<Mql5IrFunction>? overloads) && overloads.Count != 0)
        {
            Mql5IrFunction function = overloads[0];
            if (function.Parameters.Count <= available.Count)
            {
                _writer.Line(
                    Mql5ClrTypes.Identifier(handler) + "("
                    + string.Join(", ", available.Take(function.Parameters.Count)) + ");");
            }
            else
            {
                Note(
                    Mql5CodeGenDiagnosticCodes.NoEntryPoint,
                    "The handler '" + handler + "' takes arguments the strategy interface does not supply.",
                    function.Line,
                    function.Column);
            }
        }
        else
        {
            Note(
                Mql5CodeGenDiagnosticCodes.NoEntryPoint,
                "The module declares no '" + handler + "' handler.",
                1,
                1);
        }

        _writer.CloseBrace();
    }

    // ----------------------------------------------------------------- helpers

    private void RefuseReservedName(string name, int line, int column)
    {
        if (Mql5ClrTypes.ReservedNames.Contains(name))
        {
            Fail(
                Mql5CodeGenDiagnosticCodes.ReservedIdentifier,
                "The name '" + name + "' collides with a name the generated class reserves.",
                line,
                column);
        }
    }

    private static string XmlText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }
}
