using System.Globalization;
using YO4X.StrategyGovernance;

namespace YO4X.Mql5.CodeGen;

/// <summary>
/// Call routing: every MQL5 built-in becomes a member call on the runtime context,
/// never an inline expansion. Routing through one object is what lets the runtime be
/// swapped for a backtest, a paper account or a live account without regenerating a
/// single strategy.
/// </summary>
internal sealed partial class Mql5GeneratorRun
{
    private string EmitCall(Mql5IrCallExpression call, int depth)
    {
        switch (call.Callee)
        {
            case Mql5IrNameExpression name when name.Scope.Count == 0:
                return EmitNamedCall(call, name, depth);
            case Mql5IrMemberExpression member:
                return EmitMemberCall(call, member, depth);
            case Mql5IrNewExpression creation when creation.Type.ArrayRanks.Count == 0:
                return EmitConstructorCall(creation, call, depth);
            case Mql5IrNameExpression qualified:
                return EmitQualifiedName(qualified) + "(" + PlainArguments(call.Arguments, depth) + ")";
            default:
                return Fail(
                    Mql5CodeGenDiagnosticCodes.UnsupportedCall,
                    "A call through a '" + call.Callee.Kind + "' expression is not translated.",
                    call.Line,
                    call.Column);
        }
    }


    /// <summary>
    /// A call through a member expression: a method on a user type or on one of the runtime's
    /// standard library classes.
    /// </summary>
    /// <remarks>
    /// Arguments to a standard library method get the same conversion treatment as arguments to a
    /// free built-in, because the mismatch is the same one: <c>CTrade.SetExpertMagicNumber</c>
    /// takes a <c>ulong</c>, every strategy passes an <c>int</c> literal, and MQL5 widens it
    /// silently. A method on a user-declared type is left alone — its parameters were emitted from
    /// the same MQL5 declaration as the call, so they already agree.
    /// </remarks>
    private string EmitMemberCall(Mql5IrCallExpression call, Mql5IrMemberExpression member, int depth)
    {
        string target = Expr(member.Target, depth + 1);
        string method = Mql5ClrTypes.Identifier(member.Member);

        Mql5ResolvedType targetType = TypeOf(member.Target);
        string? libraryType = targetType.Kind is Mql5ResolvedTypeKind.Structure or Mql5ResolvedTypeKind.Class
            ? Mql5ClrTypes.RuntimeTypeName(targetType.Name)
            : null;

        if (libraryType is null)
        {
            return target + "." + method + "(" + PlainArguments(call.Arguments, depth) + ")";
        }

        var parts = new List<string>(call.Arguments.Count);
        for (int index = 0; index < call.Arguments.Count; index++)
        {
            parts.Add(
                RuntimeArgument(
                    Mql5ClrTypes.LibraryParameterType(libraryType, member.Member, call.Arguments.Count, index),
                    call.Arguments[index],
                    depth));
        }

        return target + "." + method + "(" + string.Join(", ", parts) + ")";
    }


    /// <summary>
    /// A construction that carries constructor arguments.
    /// </summary>
    /// <remarks>
    /// MQL5 writes <c>new CFoo(a, b)</c>, and the parser reads that as a call whose callee is the
    /// <c>new</c> — so the arguments arrive on the call rather than on the creation. Emitting only
    /// the creation would drop them silently, which is why this is a separate path rather than a
    /// fall-through to <c>EmitNew</c>.
    /// </remarks>
    private string EmitConstructorCall(
        Mql5IrNewExpression creation,
        Mql5IrCallExpression call,
        int depth)
    {
        string? core = CoreTypeName(creation.Type);
        if (core is null)
        {
            return Fail(
                Mql5CodeGenDiagnosticCodes.UnsupportedType,
                "The type '" + creation.Type.Name + "' maps onto no CLR type.",
                creation.Line,
                creation.Column);
        }

        string runtimeArgument = ConstructionArguments(core);
        string arguments = PlainArguments(call.Arguments, depth);
        string all = runtimeArgument.Length == 0
            ? arguments
            : arguments.Length == 0 ? runtimeArgument : runtimeArgument + ", " + arguments;

        return "new " + core + "(" + all + ")" + ConstructionInitializer(core);
    }

    private string EmitNamedCall(Mql5IrCallExpression call, Mql5IrNameExpression name, int depth)
    {
        // MQL5 spells a conversion as a call: string(x), int(x), datetime(x).
        if (Mql5ClrTypes.ScalarKeywords.TryGetValue(name.Name, out Mql5IrScalarKind scalar))
        {
            if (call.Arguments.Count != 1)
            {
                return Fail(
                    Mql5CodeGenDiagnosticCodes.UnsupportedCall,
                    "A conversion to '" + name.Name + "' takes exactly one argument.",
                    call.Line,
                    call.Column);
            }

            return Coerce(
                Mql5ResolvedType.ForScalar(scalar),
                TypeOf(call.Arguments[0]),
                Expr(call.Arguments[0], depth + 1),
                explicitCast: true);
        }

        // Inside a type, an unqualified call names a sibling method before it names
        // anything at file scope; treating it as a module function would report the
        // wrong thing entirely.
        if (_insideTypeBody && DeclaresMethod(_currentTypeDeclaration, name.Name, 0))
        {
            return Mql5ClrTypes.Identifier(name.Name) + "(" + PlainArguments(call.Arguments, depth) + ")";
        }

        // MQL5 lets a user function share a built-in's name and resolves across both sets by
        // argument count: a file declaring `iMA(sym, tf, period, shift, method, price, extra)`
        // still reaches the built-in `iMA` at six arguments. Refusing as soon as a user declaration
        // exists would reject a program MQL5 compiles, so the built-in is consulted when no user
        // overload accepts the count.
        if (_functions.TryGetValue(name.Name, out List<Mql5IrFunction>? overloads)
            && (AcceptsArgumentCount(overloads, call.Arguments.Count)
                || !Mql5BuiltinCatalog.IsKnown(name.Name)))
        {
            return EmitModuleCall(call, name, overloads, depth);
        }
        // MQL5 spells a conversion to an enumeration as a call: `ENUM_APPLIED_PRICE(n)`. A built-in
        // enumeration is an int-sized integer type here, so the call is that conversion and not a
        // construction — there is no type to construct.
        // A catalogued built-in wins over an enumeration of the same name. The constant catalogue
        // groups the REASON_* members under "UninitializeReason", which is also the name of the
        // function that returns one — so treating every enumeration name as a conversion turned
        // the call `UninitializeReason()` into a conversion with no operand.
        if (!Mql5BuiltinCatalog.IsKnown(name.Name)
            && (name.Name.StartsWith("ENUM_", StringComparison.Ordinal)
                || Mql5BuiltinConstants.EnumNames.Contains(name.Name)))
        {
            if (call.Arguments.Count != 1)
            {
                return Fail(
                    Mql5CodeGenDiagnosticCodes.UnsupportedCall,
                    "A conversion to '" + name.Name + "' takes exactly one argument.",
                    call.Line,
                    call.Column);
            }

            return Coerce(
                Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Whole32),
                TypeOf(call.Arguments[0]),
                Expr(call.Arguments[0], depth + 1),
                explicitCast: true);
        }

        if (_typeNames.ContainsKey(name.Name) || Mql5ClrTypes.RuntimeTypeNames.Contains(name.Name))
        {
            // A type name in call position is an MQL5 constructor-style conversion.
            string constructed = EmitTypeAsName(name);
            return "new " + constructed + "(" + PlainArguments(call.Arguments, depth) + ")"
                + ConstructionInitializer(constructed);
        }

        if (Mql5BuiltinCatalog.IsKnown(name.Name))
        {
            return EmitBuiltinCall(call, name, depth);
        }

        return Fail(
            Mql5CodeGenDiagnosticCodes.UnsupportedCall,
            "The call to '" + name.Name + "' resolved to nothing callable.",
            call.Line,
            call.Column);
    }

    // ------------------------------------------------------------ module calls

    private string EmitModuleCall(
        Mql5IrCallExpression call,
        Mql5IrNameExpression name,
        List<Mql5IrFunction> overloads,
        int depth)
    {
        // A module function is an instance method on the strategy. Inside a module type it is
        // reached through the owner the construction site bound, the same way a file-scope global
        // is — MQL5 puts both in one program scope, and C# puts the type outside it.
        Mql5IrFunction? selected = null;
        foreach (Mql5IrFunction candidate in overloads)
        {
            if (call.Arguments.Count >= RequiredParameterCount(candidate)
                && call.Arguments.Count <= candidate.Parameters.Count)
            {
                selected = candidate;
                break;
            }
        }

        if (selected is null)
        {
            return Fail(
                Mql5CodeGenDiagnosticCodes.UnsupportedCall,
                "No declaration of '" + name.Name + "' accepts "
                    + call.Arguments.Count.ToString(CultureInfo.InvariantCulture) + " argument(s).",
                call.Line,
                call.Column);
        }

        var parts = new List<string>(call.Arguments.Count);
        for (int index = 0; index < call.Arguments.Count; index++)
        {
            Mql5IrExpression argument = call.Arguments[index];
            Mql5IrParameter parameter = selected.Parameters[index];
            if (ParameterIsByRef(parameter))
            {
                parts.Add("ref " + ReferenceArgument(argument, name.Name, index, depth));
                continue;
            }

            parts.Add(
                ConvertTo(
                    ResolveWrittenType(parameter.Type, []),
                    TypeOf(argument),
                    Expr(argument, depth + 1)));
        }

        return (_insideTypeBody ? OwnerFieldName + "." : string.Empty)
            + Mql5ClrTypes.Identifier(name.Name) + "(" + string.Join(", ", parts) + ")";
    }

    /// <summary>
    /// True when <paramref name="declaration"/> or one of its bases declares a method of
    /// this name. The base walk is depth-limited because an MQL5 module can name a base
    /// that names it back.
    /// </summary>
    private bool DeclaresMethod(Mql5IrTypeDeclaration? declaration, string name, int depth)
    {
        if (declaration is null || depth > 32)
        {
            return false;
        }

        foreach (Mql5IrFunction method in declaration.Methods)
        {
            if (string.Equals(method.Name, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return declaration.BaseTypeName is not null
            && _typeDeclarations.TryGetValue(declaration.BaseTypeName, out Mql5IrTypeDeclaration? baseType)
            && DeclaresMethod(baseType, name, depth + 1);
    }


    /// <summary>Whether any of these declarations accepts a call of <paramref name="count"/> arguments.</summary>
    private static bool AcceptsArgumentCount(List<Mql5IrFunction> overloads, int count)
    {
        foreach (Mql5IrFunction candidate in overloads)
        {
            if (count >= RequiredParameterCount(candidate) && count <= candidate.Parameters.Count)
            {
                return true;
            }
        }

        return false;
    }

    private static int RequiredParameterCount(Mql5IrFunction function)
    {
        int required = 0;
        foreach (Mql5IrParameter parameter in function.Parameters)
        {
            if (parameter.DefaultValue is not null)
            {
                break;
            }

            required++;
        }

        return required;
    }

    // ----------------------------------------------------------- built-in calls

    /// <summary>
    /// Routes one MQL5 built-in onto the runtime context.
    ///
    /// The catalog decides the shape: which arity is legal, which parameters MQL5
    /// writes with <c>&amp;</c> and therefore become <c>ref</c>, and how it classifies
    /// the built-in's realisability. An arity no documented overload accepts is refused
    /// here — that is the MQL4 dialect showing through, and inventing a signature for it
    /// would mis-bind silently. A built-in the catalog classifies as unsupported is not
    /// refused: it is emitted as a runtime call and merely noted, because whether file
    /// I/O or terminal control is permitted is the runtime's policy, not the emitter's.
    /// </summary>
    private string EmitBuiltinCall(Mql5IrCallExpression call, Mql5IrNameExpression name, int depth)
    {
        // A built-in called from inside a module type is no longer refused: the type carries its
        // own runtime reference under the same name, so the invocation below resolves there just as
        // it does at file scope.

        string invocation = Mql5RuntimeContract.RuntimeFieldName + "."
            + Mql5ClrTypes.Identifier(Mql5ClrTypes.RuntimeBuiltinName(name.Name));

        if (Mql5ClrTypes.VariadicBuiltins.Contains(name.Name))
        {
            return invocation + "(" + PlainArguments(call.Arguments, depth) + ")";
        }

        if (!Mql5BuiltinCatalog.TryGet(name.Name, out IReadOnlyList<Mql5BuiltinSignature> overloads))
        {
            return Fail(
                Mql5CodeGenDiagnosticCodes.UnsupportedBuiltinArity,
                "The catalog holds no signature for the built-in '" + name.Name + "'.",
                call.Line,
                call.Column);
        }

        var matching = new List<Mql5BuiltinSignature>();
        foreach (Mql5BuiltinSignature signature in overloads)
        {
            if (signature.Verified && signature.AcceptsArgumentCount(call.Arguments.Count))
            {
                matching.Add(signature);
            }
        }

        if (matching.Count == 0)
        {
            return Fail(
                Mql5CodeGenDiagnosticCodes.UnsupportedBuiltinArity,
                "No documented overload of '" + name.Name + "' accepts "
                    + call.Arguments.Count.ToString(CultureInfo.InvariantCulture) + " argument(s).",
                call.Line,
                call.Column);
        }

        foreach (Mql5BuiltinSignature signature in matching)
        {
            if (signature.Support == Mql5BuiltinSupport.Unsupported)
            {
                Note(
                    Mql5CodeGenDiagnosticCodes.RuntimeGatedBuiltin,
                    "The built-in '" + name.Name
                        + "' is classified as not realisable in this engine; the runtime decides whether it runs.",
                    call.Line,
                    call.Column);
                break;
            }
        }

        // The runtime's own signature decides this, not the `&` in the MQL5 declaration. The two
        // disagree in both directions — an MQL5 array parameter is already a CLR array and must
        // not be passed with `ref`, while `CopyBuffer` reallocates its destination and must be —
        // so the emitted call is shaped from what the runtime actually declares.
        string clrName = Mql5ClrTypes.RuntimeBuiltinName(name.Name);
        var parts = new List<string>(call.Arguments.Count);
        for (int index = 0; index < call.Arguments.Count; index++)
        {
            string keyword = Mql5ClrTypes.RuntimeParameterKeyword(clrName, call.Arguments.Count, index);
            if (keyword.Length > 0)
            {
                parts.Add(keyword + ReferenceArgument(call.Arguments[index], name.Name, index, depth));
                continue;
            }

            parts.Add(
                RuntimeArgument(
                    Mql5ClrTypes.RuntimeParameterType(clrName, call.Arguments.Count, index),
                    call.Arguments[index],
                    depth));
        }

        return invocation + "(" + string.Join(", ", parts) + ")";
    }

    /// <summary>
    /// One argument to a runtime built-in, converted to the parameter type the runtime declares.
    /// </summary>
    /// <remarks>
    /// MQL5 performs these conversions implicitly and C# does not. A <c>bool</c> passed where an
    /// integer is expected is ordinary MQL5 and a compile error in C#; so is a <c>datetime</c>,
    /// which MQL5 treats as the count of seconds it is stored as. Converting here rather than
    /// widening the runtime's signatures keeps the runtime honest about what it accepts.
    /// </remarks>
    private string RuntimeArgument(string? parameterType, Mql5IrExpression argument, int depth)
    {
        string text = Expr(argument, depth + 1);
        if (parameterType is null)
        {
            return text;
        }

        Mql5ResolvedType source = TypeOf(argument);

        if (parameterType == "string")
        {
            return source.Scalar == Mql5IrScalarKind.Text && !source.IsArray
                ? text
                : "Mql5Ops.ToText(" + text + ")";
        }

        if (parameterType == "bool")
        {
            return source.Scalar == Mql5IrScalarKind.Logical && !source.IsArray
                ? text
                : "Mql5Ops.Truth(" + text + ")";
        }

        // A numeric parameter. A bool needs bridging to a number; a datetime already is one;
        // otherwise it is only a question of width, which the cast settles.
        string numeric = source.IsArray
            ? text
            : source.Scalar switch
            {
                Mql5IrScalarKind.Logical => "Mql5Ops.Num(" + text + ")",
                Mql5IrScalarKind.Moment => text,

                // MQL5 parses a string into a number; C# will not convert one at all.
                Mql5IrScalarKind.Text => parameterType is "float" or "double"
                    ? "Mql5Ops.ToDouble(" + text + ")"
                    : "Mql5Ops.ToLong(" + text + ")",

                _ => text
            };

        return source.IsArray ? numeric : NarrowingCast(parameterType, numeric);
    }

    private static bool TryAgreeOnReferences(
        List<Mql5BuiltinSignature> matching,
        int argumentCount,
        out bool[] byReference)
    {
        byReference = new bool[argumentCount];
        for (int index = 0; index < argumentCount; index++)
        {
            bool? agreed = null;
            foreach (Mql5BuiltinSignature signature in matching)
            {
                // A runtime-provided MQL5 structure is a CLR class and is already
                // passed by reference; `ref` on one is a compile error.
                bool value = index < signature.Parameters.Count
                    && signature.Parameters[index].IsReference
                    && !Mql5ClrTypes.RuntimeTypeAliases.ContainsKey(signature.Parameters[index].TypeName);
                if (agreed is null)
                {
                    agreed = value;
                }
                else if (agreed.Value != value)
                {
                    return false;
                }
            }

            byReference[index] = agreed ?? false;
        }

        return true;
    }

    /// <summary>
    /// A C# <c>ref</c> argument must be a storage location. An MQL5 <c>&amp;</c>
    /// argument that is not one would silently lose the callee's writes, so it is
    /// refused rather than passed by value.
    /// </summary>
    private string ReferenceArgument(Mql5IrExpression argument, string callee, int index, int depth)
    {
        bool addressable = argument switch
        {
            Mql5IrNameExpression => true,
            Mql5IrMemberExpression => true,
            Mql5IrIndexExpression => true,
            _ => false
        };

        if (!addressable)
        {
            return Fail(
                Mql5CodeGenDiagnosticCodes.UnsupportedReferenceArgument,
                "Argument " + (index + 1).ToString(CultureInfo.InvariantCulture) + " of '" + callee
                    + "' is passed by reference but is not a storage location.",
                argument.Line,
                argument.Column);
        }

        return Expr(argument, depth + 1);
    }

    private string PlainArguments(IReadOnlyList<Mql5IrExpression> arguments, int depth)
    {
        var parts = new List<string>(arguments.Count);
        foreach (Mql5IrExpression argument in arguments)
        {
            parts.Add(Expr(argument, depth + 1));
        }

        return string.Join(", ", parts);
    }
}
