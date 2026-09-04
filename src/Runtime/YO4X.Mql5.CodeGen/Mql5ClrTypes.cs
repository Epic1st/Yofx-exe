using System.Globalization;
using System.Collections.Frozen;
using YO4X.StrategyGovernance;

namespace YO4X.Mql5.CodeGen;

/// <summary>
/// The single place where an MQL5 scalar becomes a CLR type, and where an MQL5
/// identifier becomes a legal C# one.
///
/// The mapping is intentionally total for the scalar set and intentionally partial
/// for everything else: a name the emitter cannot classify has to reach a
/// diagnostic, not a guess.
/// </summary>
internal static class Mql5ClrTypes
{
    /// <summary>
    /// The CLR spelling of one MQL5 scalar. <c>color</c> becomes <c>int</c> because
    /// MQL5 stores a colour as a packed BGR integer and arithmetic on it is legal.
    /// </summary>
    public static string? Spell(Mql5IrScalarKind scalar) => scalar switch
    {
        Mql5IrScalarKind.Void => "void",
        Mql5IrScalarKind.Logical => "bool",
        Mql5IrScalarKind.Whole8 => "sbyte",
        Mql5IrScalarKind.Natural8 => "byte",
        Mql5IrScalarKind.Whole16 => "short",
        Mql5IrScalarKind.Natural16 => "ushort",
        Mql5IrScalarKind.Whole32 => "int",
        Mql5IrScalarKind.Natural32 => "uint",
        Mql5IrScalarKind.Whole64 => "long",
        Mql5IrScalarKind.Natural64 => "ulong",
        Mql5IrScalarKind.Real32 => "float",
        Mql5IrScalarKind.Real64 => "double",
        Mql5IrScalarKind.Text => "string",
        // MQL5 datetime is an 8-byte count of seconds since 1970 — an integer type that supports
        // arithmetic and comparison directly — and the runtime represents it as `long` at every
        // signature. Spelling it as System.DateTime here would be a second representation with no
        // authority behind it, converted at every call boundary in both directions.
        Mql5IrScalarKind.Moment => "long",
        Mql5IrScalarKind.Colour => "int",
        _ => null
    };

    /// <summary>The default value expression for one MQL5 scalar.</summary>
    public static string DefaultFor(Mql5IrScalarKind scalar) => scalar switch
    {
        Mql5IrScalarKind.Logical => "false",
        Mql5IrScalarKind.Real32 => "0F",
        Mql5IrScalarKind.Real64 => "0D",
        Mql5IrScalarKind.Text => "string.Empty",
        Mql5IrScalarKind.Moment => "0L",
        Mql5IrScalarKind.Natural64 => "0UL",
        Mql5IrScalarKind.Whole64 => "0L",
        Mql5IrScalarKind.Natural32 => "0U",
        _ => "0"
    };

    /// <summary>The byte width MQL5 documents for one scalar, used only by <c>sizeof</c>.</summary>
    public static int? WidthOf(Mql5IrScalarKind scalar) => scalar switch
    {
        Mql5IrScalarKind.Logical or Mql5IrScalarKind.Whole8 or Mql5IrScalarKind.Natural8 => 1,
        Mql5IrScalarKind.Whole16 or Mql5IrScalarKind.Natural16 => 2,
        Mql5IrScalarKind.Whole32 or Mql5IrScalarKind.Natural32 or Mql5IrScalarKind.Real32
            or Mql5IrScalarKind.Colour => 4,
        Mql5IrScalarKind.Whole64 or Mql5IrScalarKind.Natural64 or Mql5IrScalarKind.Real64
            or Mql5IrScalarKind.Moment => 8,
        _ => null
    };

    /// <summary>
    /// The MQL5 predefined variables, mapped onto members of the runtime context.
    /// These are variables rather than functions in MQL5, so they never reach the
    /// call path and must be handled where a bare name is emitted.
    /// </summary>
    public static FrozenDictionary<string, string> PredefinedVariables { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_Symbol"] = "Symbol()",
            ["_Point"] = "Point()",
            ["_Digits"] = "Digits()",
            ["_Period"] = "Period()",
            ["_LastError"] = "GetLastError()",
            ["_StopFlag"] = "IsStopped()",
            ["_UninitReason"] = "UninitializeReason()",
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// MQL5 built-ins whose documented signature takes a trailing variable argument
    /// list. The catalog records this on the signature, but the set is restated here
    /// because the emitter must decide before it has selected an overload.
    /// </summary>
    public static FrozenSet<string> VariadicBuiltins { get; } =
        new[] { "Alert", "Comment", "FileWrite", "Print", "PrintFormat", "StringConcatenate", "StringFormat" }
            .ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Structures and classes the MQL5 runtime and standard library provide. The set
    /// mirrors the binder's own table, which is internal to the governance assembly;
    /// keeping a copy here lets the emitter decide whether a written type name is one
    /// the generated code may reference from the runtime or one it must refuse.
    /// </summary>
    /// <summary>
    /// MQL5 structure names the runtime provides under a different CLR name. The
    /// runtime prefixes its own types <c>Mql5</c>; generated source writes the MQL5
    /// spelling, so it is translated here rather than in every emission site.
    /// </summary>
    public static FrozenDictionary<string, string> RuntimeTypeAliases { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MqlTradeRequest"] = "Mql5TradeRequest",
            ["MqlTradeResult"] = "Mql5TradeResult",
            ["MqlTradeCheckResult"] = "Mql5TradeCheckResult",
            ["MqlTick"] = "Mql5Tick",
            ["MqlRates"] = "Mql5Rates",
            ["MqlParam"] = "Mql5Param",
            ["MqlBookInfo"] = "Mql5BookInfo",
            ["MqlDateTime"] = "Mql5DateTime",
            ["MqlTradeTransaction"] = "Mql5TradeTransaction",
            ["MqlCalendarEvent"] = "Mql5CalendarEvent",
            ["MqlCalendarValue"] = "Mql5CalendarValue",

            // The MQL5 standard library classes. These are not language types: they ship as
            // source in <Trade/*.mqh> and are written against the same built-ins a strategy calls,
            // so the runtime supplies them the same way it supplies MqlTradeRequest.
            ["CTrade"] = "Mql5Trade",
            ["CPositionInfo"] = "Mql5PositionInfo",
            ["CSymbolInfo"] = "Mql5SymbolInfo",
            ["COrderInfo"] = "Mql5OrderInfo",
            ["CAccountInfo"] = "Mql5AccountInfo",
            ["CDealInfo"] = "Mql5DealInfo",
            ["CHistoryOrderInfo"] = "Mql5HistoryOrderInfo",
        }.ToFrozenDictionary(StringComparer.Ordinal);


    /// <summary>
    /// The runtime-provided types whose constructor takes the runtime.
    /// </summary>
    /// <remarks>
    /// The standard library classes are written against <c>IMql5Runtime</c>, so an instance is
    /// useless without one. MQL5 declares them with a default constructor — <c>CTrade trade;</c> —
    /// and the emitter has to supply the runtime that the MQL5 source never mentions, because in
    /// MetaTrader the built-ins these classes call are ambient rather than injected.
    ///
    /// Plain data structures such as <c>MqlTradeRequest</c> are deliberately not here: they carry
    /// fields and call nothing.
    /// </remarks>
    public static FrozenSet<string> RuntimeTypesTakingTheRuntime { get; } =
        new[]
        {
            "Mql5Trade",
            "Mql5PositionInfo",
            "Mql5SymbolInfo",
            "Mql5OrderInfo",
            "Mql5AccountInfo",
            "Mql5DealInfo",
            "Mql5HistoryOrderInfo",
        }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The CLR name a runtime-provided MQL5 type is emitted under.</summary>
    public static string RuntimeTypeName(string mql5Name) =>
        RuntimeTypeAliases.TryGetValue(mql5Name, out string? clrName) ? clrName : mql5Name;

    /// <summary>
    /// Field names on runtime-provided MQL5 structures. MQL5 declares them in
    /// lower_snake_case; the runtime exposes CLR properties. The mapping is taken
    /// from the MQL5 spelling recorded on each runtime property, so it is a
    /// translation rather than a guess.
    /// </summary>
    public static FrozenDictionary<string, string> RuntimeMemberAliases { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // MqlTradeRequest
            ["action"] = "Action",
            ["magic"] = "Magic",
            ["order"] = "Order",
            ["symbol"] = "Symbol",
            ["volume"] = "Volume",
            ["price"] = "Price",
            ["stoplimit"] = "StopLimit",
            ["sl"] = "StopLoss",
            ["tp"] = "TakeProfit",
            ["deviation"] = "Deviation",
            ["type"] = "Type",
            ["type_filling"] = "TypeFilling",
            ["type_time"] = "TypeTime",
            ["expiration"] = "Expiration",
            ["comment"] = "Comment",
            ["position"] = "Position",
            ["position_by"] = "PositionBy",

            // MqlTradeResult
            ["retcode"] = "Retcode",
            ["deal"] = "Deal",
            ["bid"] = "Bid",
            ["ask"] = "Ask",
            ["request_id"] = "RequestId",
            ["retcode_external"] = "RetcodeExternal",

            // MqlTradeCheckResult
            ["balance"] = "Balance",
            ["equity"] = "Equity",
            ["profit"] = "Profit",
            ["margin"] = "Margin",
            ["margin_free"] = "MarginFree",
            ["margin_level"] = "MarginLevel",

            // MqlRates / MqlTick
            ["time"] = "Time",
            ["open"] = "Open",
            ["high"] = "High",
            ["low"] = "Low",
            ["close"] = "Close",
            ["tick_volume"] = "TickVolume",
            ["spread"] = "Spread",
            ["real_volume"] = "RealVolume",
            ["last"] = "Last",
            ["flags"] = "Flags",
            ["volume_real"] = "VolumeReal",
            ["time_msc"] = "TimeMsc",

            // MqlTradeTransaction
            ["deal_type"] = "DealType",
            ["order_type"] = "OrderType",
            ["order_state"] = "OrderState",
            ["time_type"] = "TimeType",
            ["time_expiration"] = "TimeExpiration",
            ["price_trigger"] = "PriceTrigger",
            ["price_sl"] = "PriceSl",
            ["price_tp"] = "PriceTp",

            // MqlDateTime
            ["year"] = "Year",
            ["mon"] = "Month",
            ["day"] = "Day",
            ["hour"] = "Hour",
            ["min"] = "Minute",
            ["sec"] = "Second",
            ["day_of_week"] = "DayOfWeek",
            ["day_of_year"] = "DayOfYear",

            // MqlParam
            ["integer_value"] = "IntegerValue",
            ["double_value"] = "DoubleValue",
            ["string_value"] = "StringValue",
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The CLR member name for an MQL5 field on a runtime-provided type.</summary>
    public static string RuntimeMemberName(string mql5Member) =>
        RuntimeMemberAliases.TryGetValue(mql5Member, out string? clrName) ? clrName : mql5Member;

    /// <summary>
    /// CLR types of runtime structure fields that are not <c>int</c>, <c>double</c>
    /// or <c>string</c>. MQL5 converts between integral widths implicitly and C# does
    /// not, so an assignment to one of these needs an explicit conversion.
    /// </summary>
    public static FrozenDictionary<string, string> RuntimeMemberClrTypes { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["magic"] = "ulong",
            ["order"] = "ulong",
            ["deviation"] = "ulong",
            ["position"] = "ulong",
            ["position_by"] = "ulong",
            ["expiration"] = "long",
            ["retcode"] = "uint",
            ["deal"] = "ulong",
            ["request_id"] = "uint",
            ["time"] = "long",
            ["time_msc"] = "long",
            ["tick_volume"] = "long",
            ["real_volume"] = "long",
            ["flags"] = "uint",
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The CLR type of a runtime structure field, or null when it needs no conversion.</summary>
    public static string? RuntimeMemberClrType(string mql5Member) =>
        RuntimeMemberClrTypes.TryGetValue(mql5Member, out string? clrType) ? clrType : null;

    /// <summary>
    /// The MQL5 types the runtime actually provides.
    /// </summary>
    /// <remarks>
    /// Derived from <see cref="RuntimeTypeAliases"/> rather than listed, because the two must agree
    /// and a hand-written list did not. It previously named 110 types of which only 16 existed —
    /// every standard library class from <c>CExpert</c> to <c>CCanvas</c>, the whole dialog
    /// hierarchy, the indicator wrappers. The set means "emit this name verbatim, the runtime has
    /// it", so each phantom entry turned a codegen diagnostic naming the MQL5 type into a Roslyn
    /// error naming a C# symbol that was never going to exist — reporting the wrong thing about
    /// roughly ninety files. Deriving it makes that class of mistake unrepresentable.
    /// </remarks>
    public static FrozenSet<string> RuntimeTypeNames { get; } =
        RuntimeTypeAliases.Keys.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// MQL5 predefined constants that stand outside any enumeration: numeric limits,
    /// mathematical constants and the sentinel values. They are listed rather than
    /// matched by shape, because accepting every upper-snake-case name would turn a
    /// misspelling into a downstream C# compile error instead of a diagnostic here.
    /// </summary>
    public static FrozenSet<string> PredefinedConstants { get; } =
        new[]
        {
            "EMPTY", "EMPTY_VALUE", "WHOLE_ARRAY", "INVALID_HANDLE", "WRONG_VALUE",
            "CHARTS_MAX", "CLR_NONE", "clrNONE", "IS_DEBUG_MODE", "IS_PROFILE_MODE",
            "CHAR_MIN", "CHAR_MAX", "UCHAR_MAX", "SHORT_MIN", "SHORT_MAX", "USHORT_MAX",
            "INT_MIN", "INT_MAX", "UINT_MAX", "LONG_MIN", "LONG_MAX", "ULONG_MAX",
            "DBL_MIN", "DBL_MAX", "DBL_EPSILON", "DBL_DIG", "DBL_MANT_DIG",
            "DBL_MAX_10_EXP", "DBL_MAX_EXP", "DBL_MIN_10_EXP", "DBL_MIN_EXP",
            "FLT_MIN", "FLT_MAX", "FLT_EPSILON", "FLT_DIG", "FLT_MANT_DIG",
            "FLT_MAX_10_EXP", "FLT_MAX_EXP", "FLT_MIN_10_EXP", "FLT_MIN_EXP",
            "M_E", "M_LOG2E", "M_LOG10E", "M_LN2", "M_LN10", "M_PI", "M_PI_2", "M_PI_4",
            "M_1_PI", "M_2_PI", "M_2_SQRTPI", "M_SQRT2", "M_SQRT1_2",
            "NULL"
        }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// True when a name is one MQL5 itself defines: a catalogued constant, a listed
    /// predefined constant, or one of the <c>clr*</c> colour names.
    /// </summary>
    public static bool IsPredefinedConstant(string name) =>
        PredefinedConstants.Contains(name)
        || (name.StartsWith("clr", StringComparison.Ordinal)
            && name.Length > 3
            && char.IsAsciiLetterUpper(name[3]));

    /// <summary>The MQL5 scalar keywords, as written in a conversion such as <c>string(x)</c>.</summary>
    public static FrozenDictionary<string, Mql5IrScalarKind> ScalarKeywords { get; } =
        new Dictionary<string, Mql5IrScalarKind>(StringComparer.Ordinal)
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
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenSet<string> CSharpKeywords =
        new[]
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
            "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw",
            "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
            "virtual", "void", "volatile", "while"
        }.ToFrozenSet(StringComparer.Ordinal);
    /// <summary>
    /// Finds the shape recorded for a call supplying <paramref name="argumentCount"/> arguments, or
    /// null when none is.
    /// </summary>
    /// <remarks>
    /// Shapes are keyed by call arity, not by an overload's parameter count. The distinction is not
    /// academic: <c>ObjectsDeleteAll</c> declares both <c>(long, int, int)</c> and
    /// <c>(long, string, int, int)</c> with optional tails, so a two-argument call selects either,
    /// and keying by parameter count picked whichever was recorded first — declaring the caller's
    /// string an int and casting it. Each recorded position is now the agreement across every
    /// overload a call of that arity could select, so a position the overloads disagree on is left
    /// for C# to resolve rather than decided here.
    /// </remarks>
    private static string? SelectOverload(string shapes, int argumentCount)
    {
        foreach (string overload in shapes.Split(';'))
        {
            int colon = overload.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0
                || !int.TryParse(
                    overload[..colon],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int arity)
                || arity != argumentCount)
            {
                continue;
            }

            return overload[(colon + 1)..];
        }

        return null;
    }

    /// <summary>
    /// Names the emitted class uses for itself. An MQL5 declaration that collides with
    /// one of them is refused rather than renamed, because renaming would silently
    /// change what a later diagnostic points at.
    /// </summary>
    public static FrozenSet<string> ReservedNames { get; } =
        new[] { Mql5RuntimeContract.RuntimeFieldName, Mql5RuntimeContract.ConstantHolderName, "Mql5Ops", "__owner" }
            .ToFrozenSet(StringComparer.Ordinal);
    /// <summary>
    /// The return type of each standard library method, keyed <c>Type.Method</c>.
    /// </summary>
    /// <remarks>
    /// The emitter has to type a member call to convert around it. Without this a call such as
    /// <c>Trade.ResultOrder()</c> is untyped, every conversion is skipped, and the <c>ulong</c> it
    /// returns is assigned straight into the <c>long</c> the strategy declared — which C# rejects,
    /// and which would be a silent narrowing if it did not.
    ///
    /// A method whose overloads disagree on their return type is absent: it cannot be typed from
    /// the name alone, and leaving the call untyped is better than typing it wrongly. Transcribed
    /// by reflection over the runtime and re-derived by <c>LibraryReturnTypeTests</c>.
    /// </remarks>
    public static FrozenDictionary<string, string> LibraryReturnTypes { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Mql5AccountInfo.Balance"] = "double",
            ["Mql5AccountInfo.Company"] = "string",
            ["Mql5AccountInfo.Credit"] = "double",
            ["Mql5AccountInfo.Currency"] = "string",
            ["Mql5AccountInfo.Equity"] = "double",
            ["Mql5AccountInfo.FreeMargin"] = "double",
            ["Mql5AccountInfo.FreeMarginCheck"] = "double",
            ["Mql5AccountInfo.Leverage"] = "long",
            ["Mql5AccountInfo.LimitOrders"] = "int",
            ["Mql5AccountInfo.Login"] = "long",
            ["Mql5AccountInfo.Margin"] = "double",
            ["Mql5AccountInfo.MarginCall"] = "double",
            ["Mql5AccountInfo.MarginCheck"] = "double",
            ["Mql5AccountInfo.MarginLevel"] = "double",
            ["Mql5AccountInfo.MarginMode"] = "int",
            ["Mql5AccountInfo.MarginStopOut"] = "double",
            ["Mql5AccountInfo.MaxLotCheck"] = "double",
            ["Mql5AccountInfo.Name"] = "string",
            ["Mql5AccountInfo.OrderProfitCheck"] = "double",
            ["Mql5AccountInfo.Profit"] = "double",
            ["Mql5AccountInfo.Server"] = "string",
            ["Mql5AccountInfo.StopoutMode"] = "int",
            ["Mql5AccountInfo.TradeAllowed"] = "bool",
            ["Mql5AccountInfo.TradeExpert"] = "bool",
            ["Mql5AccountInfo.TradeMode"] = "int",
            ["Mql5DealInfo.Comment"] = "string",
            ["Mql5DealInfo.Commission"] = "double",
            ["Mql5DealInfo.DealType"] = "int",
            ["Mql5DealInfo.Entry"] = "int",
            ["Mql5DealInfo.ExternalId"] = "string",
            ["Mql5DealInfo.Magic"] = "long",
            ["Mql5DealInfo.Order"] = "long",
            ["Mql5DealInfo.PositionId"] = "long",
            ["Mql5DealInfo.Price"] = "double",
            ["Mql5DealInfo.Profit"] = "double",
            ["Mql5DealInfo.SelectByIndex"] = "bool",
            ["Mql5DealInfo.Swap"] = "double",
            ["Mql5DealInfo.Symbol"] = "string",
            ["Mql5DealInfo.Time"] = "long",
            ["Mql5DealInfo.TimeMsc"] = "long",
            ["Mql5DealInfo.Volume"] = "double",
            ["Mql5HistoryOrderInfo.Comment"] = "string",
            ["Mql5HistoryOrderInfo.ExternalId"] = "string",
            ["Mql5HistoryOrderInfo.Magic"] = "long",
            ["Mql5HistoryOrderInfo.OrderType"] = "int",
            ["Mql5HistoryOrderInfo.PositionById"] = "long",
            ["Mql5HistoryOrderInfo.PositionId"] = "long",
            ["Mql5HistoryOrderInfo.PriceCurrent"] = "double",
            ["Mql5HistoryOrderInfo.PriceOpen"] = "double",
            ["Mql5HistoryOrderInfo.PriceStopLimit"] = "double",
            ["Mql5HistoryOrderInfo.SelectByIndex"] = "bool",
            ["Mql5HistoryOrderInfo.State"] = "int",
            ["Mql5HistoryOrderInfo.StopLoss"] = "double",
            ["Mql5HistoryOrderInfo.Symbol"] = "string",
            ["Mql5HistoryOrderInfo.TakeProfit"] = "double",
            ["Mql5HistoryOrderInfo.TimeDone"] = "long",
            ["Mql5HistoryOrderInfo.TimeDoneMsc"] = "long",
            ["Mql5HistoryOrderInfo.TimeExpiration"] = "long",
            ["Mql5HistoryOrderInfo.TimeSetup"] = "long",
            ["Mql5HistoryOrderInfo.TimeSetupMsc"] = "long",
            ["Mql5HistoryOrderInfo.Type"] = "int",
            ["Mql5HistoryOrderInfo.TypeDescription"] = "string",
            ["Mql5HistoryOrderInfo.TypeFilling"] = "int",
            ["Mql5HistoryOrderInfo.TypeTime"] = "int",
            ["Mql5HistoryOrderInfo.VolumeCurrent"] = "double",
            ["Mql5HistoryOrderInfo.VolumeInitial"] = "double",
            ["Mql5OrderInfo.CheckState"] = "bool",
            ["Mql5OrderInfo.Comment"] = "string",
            ["Mql5OrderInfo.ExternalId"] = "string",
            ["Mql5OrderInfo.Magic"] = "long",
            ["Mql5OrderInfo.OrderType"] = "int",
            ["Mql5OrderInfo.PositionById"] = "long",
            ["Mql5OrderInfo.PositionId"] = "long",
            ["Mql5OrderInfo.PriceCurrent"] = "double",
            ["Mql5OrderInfo.PriceOpen"] = "double",
            ["Mql5OrderInfo.PriceStopLimit"] = "double",
            ["Mql5OrderInfo.Select"] = "bool",
            ["Mql5OrderInfo.SelectByIndex"] = "bool",
            ["Mql5OrderInfo.State"] = "int",
            ["Mql5OrderInfo.StopLoss"] = "double",
            ["Mql5OrderInfo.Symbol"] = "string",
            ["Mql5OrderInfo.TakeProfit"] = "double",
            ["Mql5OrderInfo.Ticket"] = "ulong",
            ["Mql5OrderInfo.TimeDone"] = "long",
            ["Mql5OrderInfo.TimeDoneMsc"] = "long",
            ["Mql5OrderInfo.TimeExpiration"] = "long",
            ["Mql5OrderInfo.TimeSetup"] = "long",
            ["Mql5OrderInfo.TimeSetupMsc"] = "long",
            ["Mql5OrderInfo.Type"] = "int",
            ["Mql5OrderInfo.TypeDescription"] = "string",
            ["Mql5OrderInfo.TypeFilling"] = "int",
            ["Mql5OrderInfo.TypeTime"] = "int",
            ["Mql5OrderInfo.VolumeCurrent"] = "double",
            ["Mql5OrderInfo.VolumeInitial"] = "double",
            ["Mql5PositionInfo.CheckState"] = "bool",
            ["Mql5PositionInfo.Comment"] = "string",
            ["Mql5PositionInfo.Commission"] = "double",
            ["Mql5PositionInfo.Identifier"] = "long",
            ["Mql5PositionInfo.Magic"] = "long",
            ["Mql5PositionInfo.PositionType"] = "int",
            ["Mql5PositionInfo.PriceCurrent"] = "double",
            ["Mql5PositionInfo.PriceOpen"] = "double",
            ["Mql5PositionInfo.Profit"] = "double",
            ["Mql5PositionInfo.Select"] = "bool",
            ["Mql5PositionInfo.SelectByIndex"] = "bool",
            ["Mql5PositionInfo.SelectByMagic"] = "bool",
            ["Mql5PositionInfo.SelectByTicket"] = "bool",
            ["Mql5PositionInfo.StopLoss"] = "double",
            ["Mql5PositionInfo.Swap"] = "double",
            ["Mql5PositionInfo.Symbol"] = "string",
            ["Mql5PositionInfo.TakeProfit"] = "double",
            ["Mql5PositionInfo.Ticket"] = "ulong",
            ["Mql5PositionInfo.Time"] = "long",
            ["Mql5PositionInfo.TimeMsc"] = "long",
            ["Mql5PositionInfo.TimeUpdate"] = "long",
            ["Mql5PositionInfo.TimeUpdateMsc"] = "long",
            ["Mql5PositionInfo.Type"] = "int",
            ["Mql5PositionInfo.TypeDescription"] = "string",
            ["Mql5PositionInfo.Volume"] = "double",
            ["Mql5SymbolInfo.Ask"] = "double",
            ["Mql5SymbolInfo.AskHigh"] = "double",
            ["Mql5SymbolInfo.AskLow"] = "double",
            ["Mql5SymbolInfo.Bank"] = "string",
            ["Mql5SymbolInfo.Bid"] = "double",
            ["Mql5SymbolInfo.BidHigh"] = "double",
            ["Mql5SymbolInfo.BidLow"] = "double",
            ["Mql5SymbolInfo.ContractSize"] = "double",
            ["Mql5SymbolInfo.CurrencyBase"] = "string",
            ["Mql5SymbolInfo.CurrencyMargin"] = "string",
            ["Mql5SymbolInfo.CurrencyProfit"] = "string",
            ["Mql5SymbolInfo.Description"] = "string",
            ["Mql5SymbolInfo.Digits"] = "int",
            ["Mql5SymbolInfo.ExpirationTime"] = "long",
            ["Mql5SymbolInfo.FreezeLevel"] = "int",
            ["Mql5SymbolInfo.IsSynchronized"] = "bool",
            ["Mql5SymbolInfo.Last"] = "double",
            ["Mql5SymbolInfo.LastHigh"] = "double",
            ["Mql5SymbolInfo.LastLow"] = "double",
            ["Mql5SymbolInfo.LotsLimit"] = "double",
            ["Mql5SymbolInfo.LotsMax"] = "double",
            ["Mql5SymbolInfo.LotsMin"] = "double",
            ["Mql5SymbolInfo.LotsStep"] = "double",
            ["Mql5SymbolInfo.MarginHedged"] = "double",
            ["Mql5SymbolInfo.MarginHedgedUseLeg"] = "bool",
            ["Mql5SymbolInfo.MarginInitial"] = "double",
            ["Mql5SymbolInfo.MarginMaintenance"] = "double",
            ["Mql5SymbolInfo.NormalizePrice"] = "double",
            ["Mql5SymbolInfo.OrderMode"] = "int",
            ["Mql5SymbolInfo.Path"] = "string",
            ["Mql5SymbolInfo.Point"] = "double",
            ["Mql5SymbolInfo.Refresh"] = "bool",
            ["Mql5SymbolInfo.RefreshRates"] = "bool",
            ["Mql5SymbolInfo.Select"] = "bool",
            ["Mql5SymbolInfo.Spread"] = "long",
            ["Mql5SymbolInfo.SpreadFloat"] = "bool",
            ["Mql5SymbolInfo.StartTime"] = "long",
            ["Mql5SymbolInfo.StopsLevel"] = "int",
            ["Mql5SymbolInfo.SwapLong"] = "double",
            ["Mql5SymbolInfo.SwapMode"] = "int",
            ["Mql5SymbolInfo.SwapRollover3days"] = "int",
            ["Mql5SymbolInfo.SwapShort"] = "double",
            ["Mql5SymbolInfo.TickSize"] = "double",
            ["Mql5SymbolInfo.TickValue"] = "double",
            ["Mql5SymbolInfo.TickValueLoss"] = "double",
            ["Mql5SymbolInfo.TickValueProfit"] = "double",
            ["Mql5SymbolInfo.TicksBookDepth"] = "int",
            ["Mql5SymbolInfo.Time"] = "long",
            ["Mql5SymbolInfo.TradeCalcMode"] = "int",
            ["Mql5SymbolInfo.TradeExecution"] = "int",
            ["Mql5SymbolInfo.TradeExecutionDescription"] = "string",
            ["Mql5SymbolInfo.TradeFillFlags"] = "int",
            ["Mql5SymbolInfo.TradeMode"] = "int",
            ["Mql5SymbolInfo.TradeTimeFlags"] = "int",
            ["Mql5SymbolInfo.Volume"] = "long",
            ["Mql5SymbolInfo.VolumeHigh"] = "long",
            ["Mql5SymbolInfo.VolumeLow"] = "long",
            ["Mql5Trade.Buy"] = "bool",
            ["Mql5Trade.BuyLimit"] = "bool",
            ["Mql5Trade.BuyStop"] = "bool",
            ["Mql5Trade.IsHedging"] = "bool",
            ["Mql5Trade.MarginMode"] = "int",
            ["Mql5Trade.OrderDelete"] = "bool",
            ["Mql5Trade.OrderModify"] = "bool",
            ["Mql5Trade.OrderOpen"] = "bool",
            ["Mql5Trade.PositionClose"] = "bool",
            ["Mql5Trade.PositionClosePartial"] = "bool",
            ["Mql5Trade.PositionModify"] = "bool",
            ["Mql5Trade.PositionOpen"] = "bool",
            ["Mql5Trade.RequestAction"] = "int",
            ["Mql5Trade.RequestComment"] = "string",
            ["Mql5Trade.RequestDeviation"] = "ulong",
            ["Mql5Trade.RequestExpiration"] = "long",
            ["Mql5Trade.RequestMagic"] = "ulong",
            ["Mql5Trade.RequestOrder"] = "ulong",
            ["Mql5Trade.RequestPosition"] = "ulong",
            ["Mql5Trade.RequestPositionBy"] = "ulong",
            ["Mql5Trade.RequestPrice"] = "double",
            ["Mql5Trade.RequestSL"] = "double",
            ["Mql5Trade.RequestStopLimit"] = "double",
            ["Mql5Trade.RequestSymbol"] = "string",
            ["Mql5Trade.RequestTP"] = "double",
            ["Mql5Trade.RequestType"] = "int",
            ["Mql5Trade.RequestTypeDescription"] = "string",
            ["Mql5Trade.RequestTypeFilling"] = "int",
            ["Mql5Trade.RequestTypeTime"] = "int",
            ["Mql5Trade.RequestVolume"] = "double",
            ["Mql5Trade.ResultAsk"] = "double",
            ["Mql5Trade.ResultBid"] = "double",
            ["Mql5Trade.ResultComment"] = "string",
            ["Mql5Trade.ResultDeal"] = "ulong",
            ["Mql5Trade.ResultOrder"] = "ulong",
            ["Mql5Trade.ResultPrice"] = "double",
            ["Mql5Trade.ResultRetcode"] = "uint",
            ["Mql5Trade.ResultRetcodeDescription"] = "string",
            ["Mql5Trade.ResultRetcodeExternal"] = "int",
            ["Mql5Trade.ResultVolume"] = "double",
            ["Mql5Trade.Sell"] = "bool",
            ["Mql5Trade.SellLimit"] = "bool",
            ["Mql5Trade.SellStop"] = "bool",
            ["Mql5Trade.SetTypeFillingBySymbol"] = "bool",
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The return type of a standard library method, or null when it is not known.</summary>
    public static string? LibraryReturnType(string clrTypeName, string methodName)
    {
        ArgumentNullException.ThrowIfNull(clrTypeName);
        ArgumentNullException.ThrowIfNull(methodName);

        return LibraryReturnTypes.GetValueOrDefault(clrTypeName + "." + methodName);
    }


    /// <summary>
    /// The parameter types of the runtime's standard library classes, keyed <c>Type.Method</c>.
    /// </summary>
    /// <remarks>
    /// These need the same conversions as the free built-ins and for the same reason: MQL5 widens
    /// an <c>int</c> to a <c>ulong</c> without comment when a strategy writes
    /// <c>trade.SetExpertMagicNumber(123456)</c>, and C# does not. Keyed by type and method
    /// because, unlike the built-ins, these names are only unique within their class.
    /// </remarks>
    public static FrozenDictionary<string, string> LibraryParameterTypes { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Mql5AccountInfo.FreeMarginCheck"] = "4:string|int|double|double",
            ["Mql5AccountInfo.MarginCheck"] = "4:string|int|double|double",
            ["Mql5AccountInfo.MaxLotCheck"] = "3:string|int|double;4:string|int|double|double",
            ["Mql5AccountInfo.OrderProfitCheck"] = "5:string|int|double|double|double",
            ["Mql5DealInfo.SelectByIndex"] = "1:int",
            ["Mql5DealInfo.Ticket"] = "1:ulong",
            ["Mql5HistoryOrderInfo.SelectByIndex"] = "1:int",
            ["Mql5HistoryOrderInfo.Ticket"] = "1:ulong",
            ["Mql5OrderInfo.Select"] = "1:ulong",
            ["Mql5OrderInfo.SelectByIndex"] = "1:int",
            ["Mql5PositionInfo.Select"] = "1:string",
            ["Mql5PositionInfo.SelectByIndex"] = "1:int",
            ["Mql5PositionInfo.SelectByMagic"] = "2:string|ulong",
            ["Mql5PositionInfo.SelectByTicket"] = "1:ulong",
            ["Mql5SymbolInfo.Name"] = "1:string",
            ["Mql5SymbolInfo.NormalizePrice"] = "1:double",
            ["Mql5SymbolInfo.Select"] = "1:bool",
            ["Mql5Trade.Buy"] = "1:double;2:double|string;3:double|string|double;4:double|string|double|double;5:double|string|double|double|double;6:double|string|double|double|double|string",
            ["Mql5Trade.BuyLimit"] = "2:double|double;3:double|double|string;4:double|double|string|double;5:double|double|string|double|double;6:double|double|string|double|double|int;7:double|double|string|double|double|int|long;8:double|double|string|double|double|int|long|string",
            ["Mql5Trade.BuyStop"] = "2:double|double;3:double|double|string;4:double|double|string|double;5:double|double|string|double|double;6:double|double|string|double|double|int;7:double|double|string|double|double|int|long;8:double|double|string|double|double|int|long|string",
            ["Mql5Trade.LogLevel"] = "1:int",
            ["Mql5Trade.OrderDelete"] = "1:ulong",
            ["Mql5Trade.OrderModify"] = "6:ulong|double|double|double|int|long;7:ulong|double|double|double|int|long|double",
            ["Mql5Trade.OrderOpen"] = "10:string|int|double|double|double|double|double|int|long|string;7:string|int|double|double|double|double|double;8:string|int|double|double|double|double|double|int;9:string|int|double|double|double|double|double|int|long",
            ["Mql5Trade.PositionClose"] = "2:.|ulong",
            ["Mql5Trade.PositionClosePartial"] = "2:.|double;3:.|double|ulong",
            ["Mql5Trade.PositionModify"] = "3:.|double|double",
            ["Mql5Trade.PositionOpen"] = "6:string|int|double|double|double|double;7:string|int|double|double|double|double|string",
            ["Mql5Trade.Sell"] = "1:double;2:double|string;3:double|string|double;4:double|string|double|double;5:double|string|double|double|double;6:double|string|double|double|double|string",
            ["Mql5Trade.SellLimit"] = "2:double|double;3:double|double|string;4:double|double|string|double;5:double|double|string|double|double;6:double|double|string|double|double|int;7:double|double|string|double|double|int|long;8:double|double|string|double|double|int|long|string",
            ["Mql5Trade.SellStop"] = "2:double|double;3:double|double|string;4:double|double|string|double;5:double|double|string|double|double;6:double|double|string|double|double|int;7:double|double|string|double|double|int|long;8:double|double|string|double|double|int|long|string",
            ["Mql5Trade.SetAsyncMode"] = "1:bool",
            ["Mql5Trade.SetDeviationInPoints"] = "1:ulong",
            ["Mql5Trade.SetExpertMagicNumber"] = "1:ulong",
            ["Mql5Trade.SetTypeFilling"] = "1:int",
            ["Mql5Trade.SetTypeFillingBySymbol"] = "1:string",
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// The declared type of parameter <paramref name="index"/> of a standard library method, or
    /// null when no conversion should be emitted for it.
    /// </summary>
    public static string? LibraryParameterType(
        string clrTypeName,
        string methodName,
        int argumentCount,
        int index)
    {
        ArgumentNullException.ThrowIfNull(clrTypeName);
        ArgumentNullException.ThrowIfNull(methodName);

        if (!LibraryParameterTypes.TryGetValue(clrTypeName + "." + methodName, out string? shapes)
            || SelectOverload(shapes, argumentCount) is not string spelled)
        {
            return null;
        }

        string[] types = spelled.Split('|');
        return index < 0 || index >= types.Length || types[index] == "." ? null : types[index];
    }

    /// <summary>
    /// The by-value parameter types of each runtime built-in, keyed by CLR member name.
    /// </summary>
    /// <remarks>
    /// MQL5 converts freely between its scalar types — a <c>bool</c> is usable as an integer, a
    /// <c>datetime</c> is a count of seconds — and C# does not. So an argument that is correct
    /// MQL5 is frequently not assignable to the parameter the runtime declares, and the emitter
    /// has to insert the conversion MQL5 was performing implicitly.
    ///
    /// Each value lists the overloads as <c>argumentCount:type|type|type</c>, separated by
    /// semicolons. A position spelled <c>.</c> takes no conversion: either it is by reference, or
    /// the overloads at that arity disagree about its type, in which case converting would pick an
    /// overload the source never chose. Transcribed by reflection over <c>IMql5Runtime</c> and
    /// re-derived by <c>ParameterTypeShapeTests</c>.
    /// </remarks>
    public static FrozenDictionary<string, string> RuntimeParameterTypes { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AccountInfoDouble"] = "1:int",
            ["AccountInfoInteger"] = "1:int",
            ["AccountInfoString"] = "1:int",
            ["Acos"] = "1:double",
            ["Acosh"] = "1:double",
            ["ArrayCompare"] = "3:.|.|int;4:.|.|int|int;5:.|.|int|int|int",
            ["ArrayCopy"] = "3:.|.|int;4:.|.|int|int;5:.|.|int|int|int",
            ["ArrayFill"] = "4:.|int|int|.",
            ["ArrayInsert"] = "3:.|.|uint;4:.|.|uint|uint;5:.|.|uint|uint|uint",
            ["ArrayMaximum"] = "2:.|int;3:.|int|int",
            ["ArrayMinimum"] = "2:.|int;3:.|int|int",
            ["ArrayPrint"] = "2:.|uint;3:.|uint|string;4:.|uint|string|ulong;5:.|uint|string|ulong|ulong;6:.|uint|string|ulong|ulong|ulong",
            ["ArrayRange"] = "2:.|int",
            ["ArrayRemove"] = "2:.|uint;3:.|uint|uint",
            ["ArrayResize"] = "2:.|int;3:.|int|int",
            ["ArrayReverse"] = "2:.|uint;3:.|uint|uint",
            ["ArraySetAsSeries"] = "2:.|bool",
            ["Asin"] = "1:double",
            ["Asinh"] = "1:double",
            ["Atan"] = "1:double",
            ["Atan2"] = "2:double|double",
            ["Atanh"] = "1:double",
            ["Bars"] = "2:string|int;4:string|int|long|long",
            ["BarsCalculated"] = "1:int",
            ["CalendarCountryById"] = "1:long",
            ["CalendarEventById"] = "1:long",
            ["CalendarEventHistory"] = "2:long|long",
            ["CalendarValueHistory"] = "2:long|long",
            ["Ceil"] = "1:double",
            ["CharArrayToString"] = "2:.|int;3:.|int|int;4:.|int|int|uint",
            ["CharArrayToStruct"] = "2:.|uint",
            ["CharToString"] = "1:byte",
            ["ChartApplyTemplate"] = "2:long|string",
            ["ChartClose"] = "1:long",
            ["ChartGetDouble"] = "2:long|int;3:long|int|int;4:long|int|int|.",
            ["ChartGetInteger"] = "2:long|int;3:long|int|int;4:long|int|int|.",
            ["ChartGetString"] = "2:long|int;3:long|int|.",
            ["ChartIndicatorAdd"] = "3:long|int|int",
            ["ChartIndicatorDelete"] = "3:long|int|string",
            ["ChartIndicatorGet"] = "3:long|int|string",
            ["ChartIndicatorName"] = "3:long|int|int",
            ["ChartIndicatorsTotal"] = "2:long|int",
            ["ChartNavigate"] = "2:long|int;3:long|int|int",
            ["ChartNext"] = "1:long",
            ["ChartOpen"] = "2:string|int",
            ["ChartPeriod"] = "1:long",
            ["ChartRedraw"] = "1:long",
            ["ChartSaveTemplate"] = "2:long|string",
            ["ChartScreenShot"] = "4:long|string|int|int;5:long|string|int|int|int",
            ["ChartSetDouble"] = "3:long|int|double",
            ["ChartSetInteger"] = "3:long|int|long;4:long|int|int|long",
            ["ChartSetString"] = "3:long|int|string",
            ["ChartSetSymbolPeriod"] = "3:long|string|int",
            ["ChartSymbol"] = "1:long",
            ["ChartTimePriceToXY"] = "6:long|int|long|double|.|.",
            ["ChartWindowFind"] = "2:long|string",
            ["ChartXYToTimePrice"] = "6:long|int|int|.|.|.",
            ["ColorToArgb"] = "1:int;2:int|byte",
            ["ColorToString"] = "1:int;2:int|bool",
            ["CopyBuffer"] = "5:int|int|.|.|.",
            ["CopyClose"] = "5:string|int|.|.|.",
            ["CopyHigh"] = "5:string|int|.|.|.",
            ["CopyLow"] = "5:string|int|.|.|.",
            ["CopyOpen"] = "5:string|int|.|.|.",
            ["CopyRates"] = "5:string|int|.|.|.",
            ["CopyRealVolume"] = "5:string|int|.|.|.",
            ["CopySpread"] = "5:string|int|.|.|.",
            ["CopyTickVolume"] = "5:string|int|.|.|.",
            ["CopyTicks"] = "2:string|.;3:string|.|uint;4:string|.|uint|ulong;5:string|.|uint|ulong|uint",
            ["CopyTicksRange"] = "2:string|.;3:string|.|uint;4:string|.|uint|ulong;5:string|.|uint|ulong|ulong",
            ["CopyTime"] = "5:string|int|.|.|.",
            ["Cos"] = "1:double",
            ["Cosh"] = "1:double",
            ["CryptDecode"] = "4:int|.|.|.",
            ["CryptEncode"] = "4:int|.|.|.",
            ["DoubleToString"] = "1:double;2:double|int",
            ["EventChartCustom"] = "5:long|ushort|long|double|string",
            ["EventSetMillisecondTimer"] = "1:int",
            ["EventSetTimer"] = "1:int",
            ["Exp"] = "1:double",
            ["Expm1"] = "1:double",
            ["Fabs"] = "1:double",
            ["FileClose"] = "1:int",
            ["FileCopy"] = "4:string|int|string|int",
            ["FileDelete"] = "1:string;2:string|int",
            ["FileFindClose"] = "1:long",
            ["FileFindFirst"] = "2:string|.;3:string|.|int",
            ["FileFindNext"] = "2:long|.",
            ["FileFlush"] = "1:int",
            ["FileIsEnding"] = "1:int",
            ["FileIsExist"] = "1:string;2:string|int",
            ["FileIsLineEnding"] = "1:int",
            ["FileMove"] = "4:string|int|string|int",
            ["FileOpen"] = "2:string|int;3:string|int|short;4:string|int|short|uint",
            ["FileReadArray"] = "2:int|.;3:int|.|int;4:int|.|int|int",
            ["FileReadBool"] = "1:int",
            ["FileReadDatetime"] = "1:int",
            ["FileReadDouble"] = "1:int",
            ["FileReadFloat"] = "1:int",
            ["FileReadInteger"] = "1:int;2:int|int",
            ["FileReadLong"] = "1:int",
            ["FileReadNumber"] = "1:int",
            ["FileReadString"] = "1:int;2:int|int",
            ["FileReadStruct"] = "1:int;2:int|int",
            ["FileSeek"] = "3:int|long|int",
            ["FileSize"] = "1:int",
            ["FileTell"] = "1:int",
            ["FileWrite"] = "2:int|.",
            ["FileWriteArray"] = "2:int|.;3:int|.|int;4:int|.|int|int",
            ["FileWriteDouble"] = "2:int|double",
            ["FileWriteFloat"] = "2:int|float",
            ["FileWriteInteger"] = "2:int|int;3:int|int|int",
            ["FileWriteLong"] = "2:int|long",
            ["FileWriteString"] = "2:int|string;3:int|string|int",
            ["FileWriteStruct"] = "1:int;2:int|int",
            ["Floor"] = "1:double",
            ["Fmax"] = "2:double|double",
            ["Fmin"] = "2:double|double",
            ["Fmod"] = "2:double|double",
            ["FolderClean"] = "1:string;2:string|int",
            ["FolderCreate"] = "1:string;2:string|int",
            ["FolderDelete"] = "1:string;2:string|int",
            ["GlobalVariableCheck"] = "1:string",
            ["GlobalVariableDel"] = "1:string",
            ["GlobalVariableGet"] = "1:string;2:string|.",
            ["GlobalVariableName"] = "1:int",
            ["GlobalVariableSet"] = "2:string|double",
            ["GlobalVariableSetOnCondition"] = "3:string|double|double",
            ["GlobalVariableTemp"] = "1:string",
            ["GlobalVariableTime"] = "1:string",
            ["GlobalVariablesDeleteAll"] = "1:string;2:string|long",
            ["HistoryDealGetDouble"] = "2:ulong|int;3:ulong|int|.",
            ["HistoryDealGetInteger"] = "2:ulong|int;3:ulong|int|.",
            ["HistoryDealGetString"] = "2:ulong|int;3:ulong|int|.",
            ["HistoryDealGetTicket"] = "1:int",
            ["HistoryDealSelect"] = "1:ulong",
            ["HistoryOrderGetDouble"] = "2:ulong|int;3:ulong|int|.",
            ["HistoryOrderGetInteger"] = "2:ulong|int;3:ulong|int|.",
            ["HistoryOrderGetString"] = "2:ulong|int;3:ulong|int|.",
            ["HistoryOrderGetTicket"] = "1:int",
            ["HistoryOrderSelect"] = "1:ulong",
            ["HistorySelect"] = "2:long|long",
            ["HistorySelectByPosition"] = "1:ulong",
            ["IAC"] = "2:string|int",
            ["IAD"] = "3:string|int|int",
            ["IADX"] = "3:string|int|int",
            ["IADXWilder"] = "3:string|int|int",
            ["IAMA"] = "7:string|int|int|int|int|int|int",
            ["IAO"] = "2:string|int",
            ["IATR"] = "3:string|int|int",
            ["IAlligator"] = "10:string|int|int|int|int|int|int|int|int|int",
            ["IBWMFI"] = "3:string|int|int",
            ["IBands"] = "6:string|int|int|int|double|int",
            ["IBarShift"] = "3:string|int|long;4:string|int|long|bool",
            ["IBars"] = "2:string|int",
            ["IBearsPower"] = "3:string|int|int",
            ["IBullsPower"] = "3:string|int|int",
            ["ICCI"] = "4:string|int|int|int",
            ["IChaikin"] = "6:string|int|int|int|int|int",
            ["IClose"] = "3:string|int|int",
            ["ICustom"] = "4:string|int|string|.",
            ["IDEMA"] = "5:string|int|int|int|int",
            ["IDeMarker"] = "3:string|int|int",
            ["IEnvelopes"] = "7:string|int|int|int|int|int|double",
            ["IForce"] = "5:string|int|int|int|int",
            ["IFrAMA"] = "5:string|int|int|int|int",
            ["IFractals"] = "2:string|int",
            ["IGator"] = "10:string|int|int|int|int|int|int|int|int|int",
            ["IHigh"] = "3:string|int|int",
            ["IHighest"] = "3:string|int|int;4:string|int|int|int;5:string|int|int|int|int",
            ["IIchimoku"] = "5:string|int|int|int|int",
            ["ILow"] = "3:string|int|int",
            ["ILowest"] = "3:string|int|int;4:string|int|int|int;5:string|int|int|int|int",
            ["IMA"] = "6:string|int|int|int|int|int",
            ["IMACD"] = "6:string|int|int|int|int|int",
            ["IMFI"] = "4:string|int|int|int",
            ["IMomentum"] = "4:string|int|int|int",
            ["IOBV"] = "3:string|int|int",
            ["IOpen"] = "3:string|int|int",
            ["IOsMA"] = "6:string|int|int|int|int|int",
            ["IRSI"] = "4:string|int|int|int",
            ["IRVI"] = "3:string|int|int",
            ["IRealVolume"] = "3:string|int|int",
            ["ISAR"] = "4:string|int|double|double",
            ["ISpread"] = "3:string|int|int",
            ["IStdDev"] = "6:string|int|int|int|int|int",
            ["IStochastic"] = "7:string|int|int|int|int|int|int",
            ["ITEMA"] = "5:string|int|int|int|int",
            ["ITickVolume"] = "3:string|int|int",
            ["ITime"] = "3:string|int|int",
            ["ITriX"] = "4:string|int|int|int",
            ["IVIDyA"] = "6:string|int|int|int|int|int",
            ["IVolume"] = "3:string|int|int",
            ["IVolumes"] = "3:string|int|int",
            ["IWPR"] = "3:string|int|int",
            ["IndicatorCreate"] = "3:string|int|int;4:string|int|int|.",
            ["IndicatorRelease"] = "1:int",
            ["IndicatorSetDouble"] = "2:int|double;3:int|int|double",
            ["IndicatorSetInteger"] = "2:int|int;3:int|int|int",
            ["IndicatorSetString"] = "2:int|string;3:int|int|string",
            ["IntegerToString"] = "1:long;2:long|int;3:long|int|ushort",
            ["Log"] = "1:double",
            ["Log10"] = "1:double",
            ["Log1p"] = "1:double",
            ["MarketBookAdd"] = "1:string",
            ["MarketBookGet"] = "2:string|.",
            ["MarketBookRelease"] = "1:string",
            ["MathArccos"] = "1:double",
            ["MathArccosh"] = "1:double",
            ["MathArcsin"] = "1:double",
            ["MathArcsinh"] = "1:double",
            ["MathArctan"] = "1:double",
            ["MathArctan2"] = "2:double|double",
            ["MathArctanh"] = "1:double",
            ["MathCeil"] = "1:double",
            ["MathCos"] = "1:double",
            ["MathCosh"] = "1:double",
            ["MathExp"] = "1:double",
            ["MathExpm1"] = "1:double",
            ["MathFloor"] = "1:double",
            ["MathIsValidNumber"] = "1:double",
            ["MathLog"] = "1:double",
            ["MathLog10"] = "1:double",
            ["MathLog1p"] = "1:double",
            ["MathMod"] = "2:double|double",
            ["MathPow"] = "2:double|double",
            ["MathRound"] = "1:double",
            ["MathSin"] = "1:double",
            ["MathSinh"] = "1:double",
            ["MathSqrt"] = "1:double",
            ["MathSrand"] = "1:int",
            ["MathTan"] = "1:double",
            ["MathTanh"] = "1:double",
            ["MessageBox"] = "1:string;2:string|string;3:string|string|int",
            ["MqlInfoInteger"] = "1:int",
            ["MqlInfoString"] = "1:int",
            ["NormalizeDouble"] = "2:double|int",
            ["ObjectCreate"] = "10:long|string|int|int|long|double|long|double|long|double;6:long|string|int|int|long|double;7:long|string|int|int|long|double|long;8:long|string|int|int|long|double|long|double;9:long|string|int|int|long|double|long|double|long",
            ["ObjectDelete"] = "2:long|string",
            ["ObjectFind"] = "2:long|string",
            ["ObjectGetDouble"] = "3:long|string|int;4:long|string|int|int;5:long|string|int|int|.",
            ["ObjectGetInteger"] = "3:long|string|int;4:long|string|int|int;5:long|string|int|int|.",
            ["ObjectGetString"] = "3:long|string|int;4:long|string|int|int;5:long|string|int|int|.",
            ["ObjectGetTimeByValue"] = "3:long|string|double;4:long|string|double|int",
            ["ObjectGetValueByTime"] = "3:long|string|long;4:long|string|long|int",
            ["ObjectMove"] = "5:long|string|int|long|double",
            ["ObjectName"] = "2:long|int;3:long|int|int;4:long|int|int|int",
            ["ObjectSetDouble"] = "4:long|string|int|double;5:long|string|int|int|double",
            ["ObjectSetInteger"] = "4:long|string|int|long;5:long|string|int|int|long",
            ["ObjectSetString"] = "4:long|string|int|string;5:long|string|int|int|string",
            ["ObjectsDeleteAll"] = "1:long;2:long|.;3:long|.|int;4:long|string|int|int",
            ["ObjectsTotal"] = "1:long;2:long|int;3:long|int|int",
            ["OrderCalcMargin"] = "5:int|string|double|double|.",
            ["OrderCalcProfit"] = "6:int|string|double|double|double|.",
            ["OrderGetDouble"] = "1:int;2:int|.",
            ["OrderGetInteger"] = "1:int;2:int|.",
            ["OrderGetString"] = "1:int;2:int|.",
            ["OrderGetTicket"] = "1:int",
            ["OrderSelect"] = "1:ulong",
            ["PeriodSeconds"] = "1:int",
            ["PlaySound"] = "1:string",
            ["PlotIndexGetInteger"] = "2:int|int;3:int|int|int",
            ["PlotIndexSetDouble"] = "3:int|int|double",
            ["PlotIndexSetInteger"] = "3:int|int|int;4:int|int|int|int",
            ["PlotIndexSetString"] = "3:int|int|string",
            ["PositionGetDouble"] = "1:int;2:int|.",
            ["PositionGetInteger"] = "1:int;2:int|.",
            ["PositionGetString"] = "1:int;2:int|.",
            ["PositionGetSymbol"] = "1:int",
            ["PositionGetTicket"] = "1:int",
            ["PositionSelect"] = "1:string",
            ["PositionSelectByTicket"] = "1:ulong",
            ["Pow"] = "2:double|double",
            ["PrintFormat"] = "2:string|.",
            ["ResourceCreate"] = "2:string|string;8:string|.|uint|uint|uint|uint|uint|uint",
            ["ResourceFree"] = "1:string",
            ["ResourceReadImage"] = "4:string|.|.|.",
            ["ResourceSave"] = "2:string|string",
            ["Round"] = "1:double",
            ["SendFtp"] = "1:string;2:string|string",
            ["SendMail"] = "2:string|string",
            ["SendNotification"] = "1:string",
            ["SeriesInfoInteger"] = "3:string|int|int;4:string|int|int|.",
            ["SetIndexBuffer"] = "2:int|.;3:int|.|int",
            ["ShortArrayToString"] = "2:.|int;3:.|int|int",
            ["ShortToString"] = "1:ushort",
            ["Sin"] = "1:double",
            ["Sinh"] = "1:double",
            ["Sleep"] = "1:int",
            ["Sqrt"] = "1:double",
            ["Srand"] = "1:int",
            ["StringAdd"] = "2:.|string",
            ["StringBufferLen"] = "1:string",
            ["StringCompare"] = "2:string|string;3:string|string|bool",
            ["StringFill"] = "2:.|ushort",
            ["StringFind"] = "2:string|string;3:string|string|int",
            ["StringFormat"] = "2:string|.",
            ["StringGetCharacter"] = "2:string|int",
            ["StringInit"] = "2:.|int;3:.|int|ushort",
            ["StringLen"] = "1:string",
            ["StringReplace"] = "3:.|string|string",
            ["StringReserve"] = "2:.|uint",
            ["StringSetCharacter"] = "3:.|int|ushort",
            ["StringSplit"] = "3:string|ushort|.",
            ["StringSubstr"] = "2:string|int;3:string|int|int",
            ["StringToCharArray"] = "2:string|.;3:string|.|int;4:string|.|int|int;5:string|.|int|int|uint",
            ["StringToColor"] = "1:string",
            ["StringToDouble"] = "1:string",
            ["StringToInteger"] = "1:string",
            ["StringToShortArray"] = "2:string|.;3:string|.|int;4:string|.|int|int",
            ["StringToTime"] = "1:string",
            ["StructToCharArray"] = "2:.|uint",
            ["SymbolInfoDouble"] = "2:string|int;3:string|int|.",
            ["SymbolInfoInteger"] = "2:string|int;3:string|int|.",
            ["SymbolInfoMarginRate"] = "4:string|int|.|.",
            ["SymbolInfoSessionQuote"] = "5:string|int|uint|.|.",
            ["SymbolInfoSessionTrade"] = "5:string|int|uint|.|.",
            ["SymbolInfoString"] = "2:string|int;3:string|int|.",
            ["SymbolInfoTick"] = "2:string|.",
            ["SymbolIsSynchronized"] = "1:string",
            ["SymbolName"] = "2:int|bool",
            ["SymbolSelect"] = "2:string|bool",
            ["SymbolsTotal"] = "1:bool",
            ["Tan"] = "1:double",
            ["Tanh"] = "1:double",
            ["TerminalClose"] = "1:int",
            ["TerminalInfoDouble"] = "1:int",
            ["TerminalInfoInteger"] = "1:int",
            ["TerminalInfoString"] = "1:int",
            ["TesterHideIndicators"] = "1:bool",
            ["TesterStatistics"] = "1:int",
            ["TesterWithdrawal"] = "1:double",
            ["TextGetSize"] = "3:string|.|.",
            ["TextOut"] = "9:string|int|int|uint|.|uint|uint|uint|int",
            ["TextSetFont"] = "2:string|int;3:string|int|uint;4:string|int|uint|int",
            ["TimeToString"] = "1:long;2:long|int",
            ["TimeToStruct"] = "2:long|.",
            ["TranslateKey"] = "1:int",
            ["WebRequest"] = "7:string|string|string|int|.|.|.",
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// The declared type of parameter <paramref name="index"/> of the runtime member
    /// <paramref name="clrName"/> at <paramref name="argumentCount"/> arguments, or null when no
    /// conversion should be emitted for it.
    /// </summary>
    public static string? RuntimeParameterType(string clrName, int argumentCount, int index)
    {
        ArgumentNullException.ThrowIfNull(clrName);

        if (!RuntimeParameterTypes.TryGetValue(clrName, out string? shapes)
            || SelectOverload(shapes, argumentCount) is not string spelled)
        {
            return null;
        }

        string[] types = spelled.Split('|');
        return index < 0 || index >= types.Length || types[index] == "." ? null : types[index];
    }

    /// <summary>
    /// Which parameters each runtime built-in takes by reference, keyed by CLR member name.
    /// </summary>
    /// <remarks>
    /// MQL5 marks a parameter with <c>&amp;</c>, but that mark does not decide the C# shape. An
    /// MQL5 array is already a CLR array and needs no <c>ref</c>; a runtime structure is already a
    /// CLR class and needs none either; yet <c>CopyBuffer</c> really does take its destination by
    /// reference, because it reallocates it. So the source-level mark and the emitted call agree
    /// only sometimes, and inferring one from the other produces a <c>ref</c> where C# forbids it
    /// as often as it omits one C# requires.
    ///
    /// The shape is therefore read off the runtime rather than derived. Each value lists the
    /// overloads as <c>argumentCount:index,index</c> separated by semicolons, each index suffixed
    /// <c>r</c> for <c>ref</c> or <c>o</c> for <c>out</c>; an absent name takes
    /// nothing by reference. The entries were transcribed by reflection over
    /// <c>IMql5Runtime</c>, and <c>ByReferenceShapeTests</c> re-derives them on every run.
    /// </remarks>
    public static FrozenDictionary<string, string> RuntimeByReferenceParameters { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ArrayCopy"] = "2:0r;3:0r;4:0r;5:0r",
            ["ArrayFree"] = "1:0r",
            ["ArrayInsert"] = "3:0r;4:0r;5:0r",
            ["ArrayRemove"] = "2:0r;3:0r",
            ["ArrayResize"] = "2:0r;3:0r",
            ["ArraySwap"] = "2:0r,1r",
            ["CalendarValueLast"] = "1:0r",
            ["ChartGetDouble"] = "4:3o",
            ["ChartGetInteger"] = "4:3o",
            ["ChartGetString"] = "3:2o",
            ["ChartTimePriceToXY"] = "6:4o,5o",
            ["ChartXYToTimePrice"] = "6:3o,4o,5o",
            ["CopyBuffer"] = "5:4r",
            ["CopyClose"] = "5:4r",
            ["CopyHigh"] = "5:4r",
            ["CopyLow"] = "5:4r",
            ["CopyOpen"] = "5:4r",
            ["CopyRates"] = "5:4r",
            ["CopyRealVolume"] = "5:4r",
            ["CopySpread"] = "5:4r",
            ["CopyTickVolume"] = "5:4r",
            ["CopyTicks"] = "2:1r;3:1r;4:1r;5:1r",
            ["CopyTicksRange"] = "2:1r;3:1r;4:1r;5:1r",
            ["CopyTime"] = "5:4r",
            ["CryptDecode"] = "4:3r",
            ["CryptEncode"] = "4:3r",
            ["FileFindFirst"] = "2:1r;3:1r",
            ["FileFindNext"] = "2:1r",
            ["FileReadArray"] = "2:1r;3:1r;4:1r",
            ["GlobalVariableGet"] = "2:1o",
            ["HistoryDealGetDouble"] = "3:2o",
            ["HistoryDealGetInteger"] = "3:2o",
            ["HistoryDealGetString"] = "3:2o",
            ["HistoryOrderGetDouble"] = "3:2o",
            ["HistoryOrderGetInteger"] = "3:2o",
            ["HistoryOrderGetString"] = "3:2o",
            ["MarketBookGet"] = "2:1r",
            ["ObjectGetDouble"] = "5:4o",
            ["ObjectGetInteger"] = "5:4o",
            ["ObjectGetString"] = "5:4o",
            ["OrderCalcMargin"] = "5:4o",
            ["OrderCalcProfit"] = "6:5o",
            ["OrderGetDouble"] = "2:1o",
            ["OrderGetInteger"] = "2:1o",
            ["OrderGetString"] = "2:1o",
            ["OrderSend"] = "2:1o",
            ["PositionGetDouble"] = "2:1o",
            ["PositionGetInteger"] = "2:1o",
            ["PositionGetString"] = "2:1o",
            ["ResourceReadImage"] = "4:1r,2r,3r",
            ["SeriesInfoInteger"] = "4:3o",
            ["StringAdd"] = "2:0r",
            ["StringConcatenate"] = "2:0r",
            ["StringFill"] = "2:0r",
            ["StringInit"] = "1:0r;2:0r;3:0r",
            ["StringReplace"] = "3:0r",
            ["StringReserve"] = "2:0r",
            ["StringSetCharacter"] = "3:0r",
            ["StringSplit"] = "3:2r",
            ["StringToCharArray"] = "2:1r;3:1r;4:1r;5:1r",
            ["StringToLower"] = "1:0r",
            ["StringToShortArray"] = "2:1r;3:1r;4:1r",
            ["StringToUpper"] = "1:0r",
            ["StringTrimLeft"] = "1:0r",
            ["StringTrimRight"] = "1:0r",
            ["StructToCharArray"] = "1:0r;2:0r",
            ["StructToTime"] = "1:0r",
            ["SymbolInfoDouble"] = "3:2o",
            ["SymbolInfoInteger"] = "3:2o",
            ["SymbolInfoMarginRate"] = "4:2o,3o",
            ["SymbolInfoSessionQuote"] = "5:3o,4o",
            ["SymbolInfoSessionTrade"] = "5:3o,4o",
            ["SymbolInfoString"] = "3:2o",
            ["SymbolInfoTick"] = "2:1o",
            ["TextGetSize"] = "3:1o,2o",
            ["TimeCurrent"] = "1:0o",
            ["TimeGmt"] = "1:0o",
            ["TimeLocal"] = "1:0o",
            ["TimeToStruct"] = "2:1o",
            ["TimeTradeServer"] = "1:0o",
            ["WebRequest"] = "7:5r,6r",
            ["ZeroMemory"] = "1:0r",
        }.ToFrozenDictionary(StringComparer.Ordinal);


    /// <summary>
    /// The C# keyword the runtime member <paramref name="clrName"/> requires at parameter
    /// <paramref name="index"/>, for a call with <paramref name="argumentCount"/> arguments.
    /// Returns <c>"ref "</c>, <c>"out "</c>, or an empty string when the parameter is by value.
    /// </summary>
    /// <remarks>
    /// The keyword is not interchangeable. C# rejects <c>ref</c> on an <c>out</c> parameter and
    /// the other way round, so a table that recorded only "by reference" would emit a call that
    /// does not compile for half the members it describes.
    /// </remarks>
    public static string RuntimeParameterKeyword(string clrName, int argumentCount, int index)
    {
        ArgumentNullException.ThrowIfNull(clrName);

        if (!RuntimeByReferenceParameters.TryGetValue(clrName, out string? shapes)
            || SelectOverload(shapes, argumentCount) is not string positions)
        {
            return string.Empty;
        }

        foreach (string position in positions.Split(','))
        {
            if (position.Length >= 2
                && int.TryParse(
                    position[..^1],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int at)
                && at == index)
            {
                return position[^1] == 'o' ? "out " : "ref ";
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// MQL5 built-ins whose runtime member name differs from the source name by more than the
    /// leading-<c>i</c> rule.
    /// </summary>
    /// <remarks>
    /// Two groups, both mechanical. MQL5 spells the C standard maths functions in lower case
    /// (<c>sqrt</c>, <c>fmod</c>, <c>atan2</c>), which no public CLR member may be; and it embeds
    /// acronyms in full capitals (<c>ColorToARGB</c>, <c>MQLInfoInteger</c>, <c>TimeGMT</c>),
    /// which the runtime spells in the usual .NET way.
    ///
    /// The entries were transcribed from the runtime interface by reflection rather than written
    /// out by hand, and <c>BuiltinNamesResolveToRuntimeMembersTests</c> re-derives the comparison
    /// on every run: if a runtime member is renamed, that test fails rather than this table
    /// quietly emitting a call to a member that no longer exists.
    /// </remarks>
    public static FrozenDictionary<string, string> RuntimeBuiltinAliases { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ChartID"] = "ChartId",
            ["ColorToARGB"] = "ColorToArgb",
            ["MQLInfoInteger"] = "MqlInfoInteger",

            // The MQL5_-prefixed spellings are the older names for the same two built-ins. The
            // compiler says so itself — it warns "deprecated, use MQL_PROGRAM_TYPE instead" — and
            // folding MQL5_PROGRAM_TYPE == MQL_PROGRAM_TYPE yields true, so the enumerations they
            // take are numbered identically and the alias reads the property the source named.
            ["MQL5InfoInteger"] = "MqlInfoInteger",
            ["MQL5InfoString"] = "MqlInfoString",
            ["MQLInfoString"] = "MqlInfoString",
            ["SendFTP"] = "SendFtp",
            ["TimeGMT"] = "TimeGmt",
            ["TimeGMTOffset"] = "TimeGmtOffset",
            ["acos"] = "Acos",
            ["acosh"] = "Acosh",
            ["asin"] = "Asin",
            ["asinh"] = "Asinh",
            ["atan"] = "Atan",
            ["atan2"] = "Atan2",
            ["atanh"] = "Atanh",
            ["ceil"] = "Ceil",
            ["cos"] = "Cos",
            ["cosh"] = "Cosh",
            ["exp"] = "Exp",
            ["expm1"] = "Expm1",
            ["fabs"] = "Fabs",
            ["floor"] = "Floor",
            ["fmax"] = "Fmax",
            ["fmin"] = "Fmin",
            ["fmod"] = "Fmod",
            ["log"] = "Log",
            ["log10"] = "Log10",
            ["log1p"] = "Log1p",
            ["pow"] = "Pow",
            ["printf"] = "PrintFormat",
            ["rand"] = "Rand",
            ["round"] = "Round",
            ["sin"] = "Sin",
            ["sinh"] = "Sinh",
            ["sqrt"] = "Sqrt",
            ["srand"] = "Srand",
            ["tan"] = "Tan",
            ["tanh"] = "Tanh",
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// The CLR member name for an MQL5 built-in function.
    /// </summary>
    /// <remarks>
    /// MQL5 names its timeseries and technical indicator built-ins with a leading lowercase
    /// <c>i</c> — <c>iClose</c>, <c>iMA</c>, <c>iATR</c> — which is not a legal start for a public
    /// CLR member, so the runtime spells them <c>IClose</c>, <c>IMA</c>, <c>IATR</c>. The mapping
    /// Where a name diverges for some other reason — a lower-case maths function, an embedded
    /// acronym — <see cref="RuntimeBuiltinAliases"/> carries the exception. The rule handles the
    /// fifty-odd indicator names because that is what the runtime actually
    /// applied across all of them; enumerating them would be a second place to keep in step.
    ///
    /// The rule is deliberately narrow: it fires only on a lowercase <c>i</c> followed by an
    /// uppercase letter, so <c>int</c>, <c>iTime</c>'s user-defined lookalikes and any ordinary
    /// identifier are untouched. It is applied only when routing a catalogued built-in, never to
    /// a name the strategy declared.
    /// </remarks>
    public static string RuntimeBuiltinName(string mql5Name)
    {
        ArgumentNullException.ThrowIfNull(mql5Name);

        if (RuntimeBuiltinAliases.TryGetValue(mql5Name, out string? alias))
        {
            return alias;
        }

        return mql5Name.Length >= 2 && mql5Name[0] == 'i' && char.IsAsciiLetterUpper(mql5Name[1])
            ? string.Concat("I", mql5Name.AsSpan(1))
            : mql5Name;
    }

    /// <summary>
    /// An MQL5 identifier spelled so that C# accepts it. Only a keyword collision is
    /// rewritten, and only by the verbatim <c>@</c> prefix, so the emitted name always
    /// reads as the source name.
    /// </summary>
    /// <summary>
    /// The C# name for a local that shadows one in an enclosing scope.
    /// </summary>
    /// <remarks>
    /// The suffix is a double underscore and an ordinal, which no MQL5 source in the corpus uses
    /// and which reads at a glance as machine-chosen rather than as something the author wrote.
    /// The result is still put through <see cref="Identifier"/>, so a collision with a C# keyword
    /// is escaped rather than emitted.
    /// </remarks>
    public static string ShadowName(string name, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Identifier(name + "__" + ordinal.ToString(CultureInfo.InvariantCulture));
    }

    public static string Identifier(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "@__unnamed";
        }

        return CSharpKeywords.Contains(name) ? "@" + name : name;
    }

    /// <summary>
    /// A C# type name derived from a source file name. The result always starts with a
    /// letter, contains only identifier characters, and is a pure function of the input.
    /// </summary>
    public static string TypeNameFromPath(string? sourcePath)
    {
        string stem = sourcePath ?? string.Empty;
        int slash = stem.LastIndexOfAny(['/', (char)92]);
        if (slash >= 0)
        {
            stem = stem[(slash + 1)..];
        }

        int dot = stem.LastIndexOf('.');
        if (dot > 0)
        {
            stem = stem[..dot];
        }

        Span<char> buffer = stackalloc char[stem.Length + 1];
        int length = 0;
        buffer[length++] = 'S';
        bool previousWasSeparator = false;
        foreach (char character in stem)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                buffer[length++] = previousWasSeparator ? char.ToUpperInvariant(character) : character;
                previousWasSeparator = false;
            }
            else
            {
                previousWasSeparator = true;
            }
        }

        return length == 1 ? "SStrategy" : new string(buffer[..length]);
    }
}
