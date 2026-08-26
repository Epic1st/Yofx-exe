using System.Collections.Frozen;

namespace YO4X.Mql5.Runtime;

/// <summary>
/// MQL5's <c>typename</c> operator: the MQL5 spelling of the type of a value, as a string.
///
/// MQL5 renders a scalar as its own keyword - <c>"double"</c>, <c>"ulong"</c> - and a
/// user-declared type keyword-prefixed: <c>"struct MyStruct"</c>, <c>"class MyClass"</c>,
/// <c>"enum MyEnum"</c>. A handle carries a trailing star separated by a space,
/// <c>"class MyClass *"</c>; the space is not cosmetic, it is what the compiler produces, and a
/// strategy that compares the string against a literal sees the difference.
///
/// The emitter is the caller here, not the strategy: <c>typename(x)</c> becomes a call to
/// <see cref="Mql5TypeName(Type)"/> on the CLR type behind <c>x</c>.
/// </summary>
/// <remarks>
/// <b>Two MQL5 scalars cannot be recovered from the CLR type, and this class does not pretend
/// otherwise.</b> This toolchain represents <c>datetime</c> as <see cref="long"/> and
/// <c>color</c> as <see cref="int"/> - deliberately, because MQL5 treats both as integers and
/// permits arithmetic on them - so at runtime a <c>datetime</c> is a <c>long</c> and a
/// <c>color</c> is an <c>int</c>, with nothing left to tell them apart. A <see cref="long"/>
/// therefore yields <c>"long"</c> and an <see cref="int"/> yields <c>"int"</c>. Where the true
/// MQL5 declaration was <c>datetime</c> or <c>color</c>, MetaTrader would have said so and this
/// runtime cannot. Recovering it would take the front end's static type carried down to the
/// callsite rather than a <see cref="Type"/>, which is a change to the emitted call and not to
/// this method.
///
/// <b>Where a spelling has not been measured, this refuses rather than invents.</b> Arrays,
/// constructed generics and CLR types with no MQL5 counterpart throw
/// <see cref="Mql5UnsupportedOperationException"/> naming <c>typename</c> and the CLR type. A
/// plausible-looking guess such as <c>"class Int32[]"</c> would be compared against a literal by
/// the strategy, match nothing, and take a silent branch - the failure mode this engine exists
/// to avoid.
/// </remarks>
public static class Mql5TypeInfo
{
    /// <summary>
    /// The complete MQL5 spelling of every CLR type whose answer is fixed: the scalars, and the
    /// runtime-provided structures and standard library classes, which are emitted under
    /// <c>Mql5</c>-prefixed CLR names that MQL5 has never heard of.
    ///
    /// The runtime structures are listed with their MQL5 keyword rather than derived from
    /// <see cref="Type.IsValueType"/>, because the two disagree: MQL5 declares
    /// <c>MqlTradeRequest</c> as a <c>struct</c>, while the runtime models it as a class so the
    /// emitter can hand it to <c>OrderSend</c> by reference the way MQL5 does. Asking the CLR
    /// would answer <c>"class MqlTradeRequest"</c>, which no MQL5 compiler ever prints.
    /// </summary>
    private static readonly FrozenDictionary<Type, string> Spellings = new Dictionary<Type, string>
    {
        // The MQL5 scalar set. MQL5 `char` is one signed byte and `uchar` one unsigned byte, as
        // in C, so they are the reverse of the CLR spelling: `char` is `sbyte` here.
        [typeof(void)] = "void",
        [typeof(bool)] = "bool",
        [typeof(sbyte)] = "char",
        [typeof(byte)] = "uchar",
        [typeof(short)] = "short",
        [typeof(ushort)] = "ushort",
        [typeof(int)] = "int",
        [typeof(uint)] = "uint",
        [typeof(long)] = "long",
        [typeof(ulong)] = "ulong",
        [typeof(float)] = "float",
        [typeof(double)] = "double",
        [typeof(string)] = "string",

        // Runtime-provided MQL5 structures.
        [typeof(Mql5DateTime)] = "struct MqlDateTime",
        [typeof(Mql5Tick)] = "struct MqlTick",
        [typeof(Mql5Rates)] = "struct MqlRates",
        [typeof(Mql5Param)] = "struct MqlParam",
        [typeof(Mql5BookInfo)] = "struct MqlBookInfo",
        [typeof(Mql5TradeRequest)] = "struct MqlTradeRequest",
        [typeof(Mql5TradeResult)] = "struct MqlTradeResult",
        [typeof(Mql5TradeCheckResult)] = "struct MqlTradeCheckResult",
        [typeof(Mql5TradeTransaction)] = "struct MqlTradeTransaction",
        [typeof(Mql5CalendarEvent)] = "struct MqlCalendarEvent",
        [typeof(Mql5CalendarValue)] = "struct MqlCalendarValue",

        // The MQL5 standard library classes this runtime supplies.
        [typeof(Mql5Trade)] = "class CTrade",
        [typeof(Mql5PositionInfo)] = "class CPositionInfo",
        [typeof(Mql5SymbolInfo)] = "class CSymbolInfo",
        [typeof(Mql5OrderInfo)] = "class COrderInfo",
        [typeof(Mql5AccountInfo)] = "class CAccountInfo",
        [typeof(Mql5DealInfo)] = "class CDealInfo",
        [typeof(Mql5HistoryOrderInfo)] = "class CHistoryOrderInfo",
    }.ToFrozenDictionary();

    /// <summary>
    /// The MQL5 <c>typename</c> spelling of <paramref name="type"/>, for a value rather than a
    /// handle. See <see cref="Mql5TypeName(Type, bool)"/> for the handle form.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is null.</exception>
    /// <exception cref="Mql5UnsupportedOperationException">
    /// <paramref name="type"/> has no measured MQL5 spelling.
    /// </exception>
    public static string Mql5TypeName(Type type) => Mql5TypeName(type, isHandle: false);

    /// <summary>
    /// The MQL5 <c>typename</c> spelling of <paramref name="type"/>, with
    /// <paramref name="isHandle"/> appending the <c>" *"</c> that MQL5 prints for a handle.
    /// </summary>
    /// <remarks>
    /// Handle-ness has to come from the caller. MQL5 distinguishes <c>CFoo obj</c> from
    /// <c>CFoo *ptr</c> in the declaration, and both arrive here as one CLR reference to a
    /// <c>CFoo</c> - the distinction survives in the emitter's static type and nowhere in
    /// <see cref="Type"/>, so deciding it here would mean guessing.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is null.</exception>
    /// <exception cref="Mql5UnsupportedOperationException">
    /// <paramref name="type"/> has no measured MQL5 spelling.
    /// </exception>
    public static string Mql5TypeName(Type type, bool isHandle)
    {
        ArgumentNullException.ThrowIfNull(type);

        // A `ref`/`out` parameter reaches reflection as a by-ref type. MQL5 spells a reference
        // parameter with the referent's own type name, so unwrap rather than refuse.
        Type target = type.IsByRef ? type.GetElementType()! : type;

        string spelling = Spell(target);
        return isHandle ? spelling + " *" : spelling;
    }

    private static string Spell(Type type)
    {
        if (Spellings.TryGetValue(type, out string? known))
        {
            return known;
        }

        if (type.IsArray)
        {
            throw Refuse(type, "the MQL5 spelling of an array type has not been measured against "
                + "the MQL5 compiler, and a guessed one would be compared against a literal, fail "
                + "to match, and take a silent branch");
        }

        if (type.IsGenericType)
        {
            throw Refuse(type, "a constructed generic type comes from a template instantiation, "
                + "whose MQL5 spelling has not been measured against the MQL5 compiler");
        }

        if (type.IsPointer)
        {
            throw Refuse(type, "MQL5 has no pointer-to-pointer type; a handle is spelled by "
                + "passing isHandle to Mql5TypeName, not by an unmanaged pointer type");
        }

        // Anything left under `System` is a framework type that reached here by mistake: `char`
        // (MQL5's character type is `ushort`), `decimal`, `nint`, `DateTime`, `object`. MQL5
        // declares no type in a `System` namespace and the emitter puts strategy types in the
        // generated namespace, so the namespace is a reliable divider. Without this the fallback
        // below would answer "struct Char" or "struct DateTime" - type names MQL5 does not have,
        // which would be compared against a literal and quietly fail to match.
        if (IsFrameworkType(type))
        {
            throw Refuse(type, "the CLR type is a framework type with no MQL5 counterpart");
        }

        // Enumerations are tested before the value-type branch below, which would otherwise
        // classify every one of them as a struct: a CLR enum is a value type.
        if (type.IsEnum)
        {
            return "enum " + type.Name;
        }

        // Everything left is a type the emitter declared from the strategy's own source, where
        // the CLR kind and the MQL5 keyword do agree: an MQL5 `struct` is emitted as a C# struct
        // and an MQL5 `class` as a C# class.
        return (type.IsValueType ? "struct " : "class ") + type.Name;
    }

    private static bool IsFrameworkType(Type type)
    {
        string? space = type.Namespace;
        return space is not null
            && (string.Equals(space, "System", StringComparison.Ordinal)
                || space.StartsWith("System.", StringComparison.Ordinal));
    }

    private static Mql5UnsupportedOperationException Refuse(Type type, string reason)
        => Mql5UnsupportedOperationException.For(
            "typename",
            $"no MQL5 type name is known for CLR type '{type.FullName ?? type.Name}': {reason}");
}
