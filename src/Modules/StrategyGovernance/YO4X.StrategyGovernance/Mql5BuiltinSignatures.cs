namespace YO4X.StrategyGovernance;

/// <summary>
/// The raw signature and constant tables behind <see cref="Mql5BuiltinCatalog"/>.
///
/// Provenance: every entry marked <c>Verified: true</c> was extracted from the
/// official MQL5 reference at https://www.mql5.com/en/docs by fetching the page as
/// raw origin HTML, confirming its <c>&lt;link rel="canonical"&gt;</c>, and parsing
/// the declaration table locally. No prototype here was produced by asking a model
/// to read a page and report what it saw - that route was observed reconstructing
/// plausible but wrong signatures from the example code further down the page.
///
/// A second source is the MetaEditor compiler itself, used offline at build time as an
/// oracle. Calling a built-in with the wrong argument count makes it print the exact
/// declaration it holds — <c>error 199: wrong parameters count</c> followed by
/// <c>info built-in: …</c> — and compiling a call at a given arity settles which
/// trailing parameters carry defaults. Entries citing MetaEditor in their note were
/// established that way, which is stronger evidence than the reference page because it
/// is the shipping compiler's own table.
///
/// Where a name is documented but its parameter list cannot be confirmed, the entry
/// must carry <c>Verified: false</c> and an <b>empty</b> parameter list rather than a
/// plausible-looking guess: such an entry asserts only that the name exists and how we
/// classify it, and both back ends refuse to bind it. A wrong signature is worse than
/// an absent one because it mis-binds silently. No entry is in that state any more —
/// the last three, the Calendar* family, were measured off MetaEditor. Classification
/// and shape are independent questions, and the Calendar* entries are the clearest case:
/// their declarations are now known exactly and they remain <c>Unsupported</c>, because
/// there is no calendar data source behind them. Recording the shape is still worth it,
/// since a missing shape makes the code generator report that MQL5 declares no such
/// overload — which is false, and sends the reader hunting a dialect problem that is
/// not there.
///
/// Documented oddities are reproduced rather than tidied up: MQL5 spells
/// <c>ObjectCreate</c>'s fourth parameter as <c>sub_window nwin</c> (type and name
/// transposed), gives <c>iBearsPower</c> and <c>iBullsPower</c> a trailing comma
/// after the last parameter, and takes <c>string</c> in <c>CopyTicks</c> but
/// <c>const string</c> in <c>CopyTicksRange</c>. Array parameters are marked as
/// references only where MQL5 actually writes <c>&amp;</c>: the <c>Array*</c> family
/// does, the <c>Copy*</c> family does not.
///
/// Names MQL5 does <b>not</b> declare are deliberately absent even though the
/// conversion corpus calls them: the MQL4 carry-overs <c>OrderClose</c>,
/// <c>OrderModify</c>, <c>OrderDelete</c>, <c>ObjectSet</c>, <c>AccountNumber</c>,
/// <c>WindowExpertName</c>, <c>iEMA</c> and <c>RefreshRates</c>;
/// <c>PositionSelectByIndex</c>; <c>ErrorDescription</c>, which is a function written
/// in <c>stdlib.mqh</c> rather than a language built-in; <c>CalendarEventHistory</c>,
/// which MetaEditor rejects as an undeclared identifier; and Win32 imports such as
/// <c>PostMessageW</c>. Cataloguing those would let a binder bind a name MQL5 never
/// defined.
///
/// Some catalogued names are additionally called with MQL4 arity by the corpus.
/// Checking every callsite in the 198-file corpus against this table, 427 of 34,845
/// calls to catalogued names use an arity no documented MQL5 overload accepts, and
/// all but one of them are MQL4 spellings: <c>OrderSelect</c> with two or three
/// arguments, <c>OrderSend</c> with eleven, <c>iMA</c> with seven, <c>iATR</c> and
/// <c>iFractals</c> with four, <c>iADX</c> with six, <c>iRSI</c> with five,
/// <c>Bars</c> with none, <c>ObjectCreate</c> with five, <c>ObjectMove</c> with four
/// and <c>ObjectFind</c> with one. Only the MQL5 form is catalogued; the surplus arity
/// is a dialect error for the binder to diagnose, not a signature to invent. The
/// exception was <c>SetIndexBuffer</c> with two, which the oracle shows is valid MQL5.
/// </summary>
internal static class Mql5BuiltinSignatures
{
    internal static Mql5BuiltinSignature[] Declare() =>
    [
        .. BuildMath(),
        .. BuildText(),
        .. BuildArray(),
        .. BuildConversion(),
        .. BuildDateTime(),
        .. BuildChartObject(),
        .. BuildChart(),
        .. BuildIndicator(),
        .. BuildMarketData(),
        .. BuildSymbol(),
        .. BuildAccount(),
        .. BuildTrade(),
        .. BuildHistory(),
        .. BuildTerminal(),
        .. BuildGlobal(),
        .. BuildEvent(),
        .. BuildFile()
    ];

    private static Mql5BuiltinParameter Req(string name, string typeName)
        => new(name, typeName, IsOptional: false, IsReference: false, IsArray: false);

    private static Mql5BuiltinParameter Opt(string name, string typeName)
        => new(name, typeName, IsOptional: true, IsReference: false, IsArray: false);

    private static Mql5BuiltinParameter ByRef(string name, string typeName)
        => new(name, typeName, IsOptional: false, IsReference: true, IsArray: false);

    /// <summary>An array parameter MQL5 writes without an <c>&amp;</c>.</summary>
    private static Mql5BuiltinParameter Arr(string name, string typeName)
        => new(name, typeName, IsOptional: false, IsReference: false, IsArray: true);

    /// <summary>An array parameter MQL5 writes as <c>&amp;name[]</c>.</summary>
    private static Mql5BuiltinParameter RefArr(string name, string typeName)
        => new(name, typeName, IsOptional: false, IsReference: true, IsArray: true);

    private static Mql5BuiltinParameter OptRefArr(string name, string typeName)
        => new(name, typeName, IsOptional: true, IsReference: true, IsArray: true);

    private static Mql5BuiltinSignature Make(
        string name,
        string returnTypeName,
        Mql5BuiltinCategory category,
        Mql5BuiltinSupport support,
        Mql5BuiltinParameter[] parameters)
        => new(name, returnTypeName, parameters, category, support, IsOverloaded: false, Verified: true, Note: null);

    // ---------------------------------------------------------------- Math --
    // https://www.mql5.com/en/docs/math
    // No Math parameter is optional, by reference, or an array, and only MathSwap is
    // overloaded. MetaQuotes documents a C-style alias for almost every one of them;
    // the corpus calls fabs, fmin, fmax, fmod and round directly.
    private static Mql5BuiltinSignature[] BuildMath()
    {
        static Mql5BuiltinSignature Fn(string name, string returnTypeName, params Mql5BuiltinParameter[] parameters)
            => Make(name, returnTypeName, Mql5BuiltinCategory.Math, Mql5BuiltinSupport.Native, parameters);

        static Mql5BuiltinSignature Alias(Mql5BuiltinSignature origin, string aliasName)
            => origin with { Name = aliasName, Note = $"Documented C-style alias of {origin.Name}." };

        const string WidensIntegers =
            "Documented to accept integer types without a cast; the wider operand type wins.";

        Mql5BuiltinSignature abs = Fn("MathAbs", "double", Req("value", "double"));
        Mql5BuiltinSignature max = Fn("MathMax", "double", Req("value1", "double"), Req("value2", "double")) with { Note = WidensIntegers };
        Mql5BuiltinSignature min = Fn("MathMin", "double", Req("value1", "double"), Req("value2", "double")) with { Note = WidensIntegers };
        Mql5BuiltinSignature floor = Fn("MathFloor", "double", Req("val", "double"));
        Mql5BuiltinSignature ceil = Fn("MathCeil", "double", Req("val", "double"));
        Mql5BuiltinSignature round = Fn("MathRound", "double", Req("value", "double"));
        Mql5BuiltinSignature pow = Fn("MathPow", "double", Req("base", "double"), Req("exponent", "double"));
        Mql5BuiltinSignature sqrt = Fn("MathSqrt", "double", Req("value", "double"));
        Mql5BuiltinSignature exp = Fn("MathExp", "double", Req("value", "double"));
        Mql5BuiltinSignature log = Fn("MathLog", "double", Req("val", "double"));
        Mql5BuiltinSignature log10 = Fn("MathLog10", "double", Req("val", "double"));
        Mql5BuiltinSignature mod = Fn("MathMod", "double", Req("value", "double"), Req("value2", "double"));
        Mql5BuiltinSignature rand = Fn("MathRand", "int");
        Mql5BuiltinSignature srand = Fn("MathSrand", "void", Req("seed", "int"));
        Mql5BuiltinSignature sin = Fn("MathSin", "double", Req("value", "double"));
        Mql5BuiltinSignature cos = Fn("MathCos", "double", Req("value", "double"));
        Mql5BuiltinSignature tan = Fn("MathTan", "double", Req("rad", "double"));
        Mql5BuiltinSignature asin = Fn("MathArcsin", "double", Req("val", "double"));
        Mql5BuiltinSignature acos = Fn("MathArccos", "double", Req("val", "double"));
        Mql5BuiltinSignature atan = Fn("MathArctan", "double", Req("value", "double"));
        Mql5BuiltinSignature atan2 = Fn("MathArctan2", "double", Req("y", "double"), Req("x", "double"));
        Mql5BuiltinSignature expm1 = Fn("MathExpm1", "double", Req("value", "double"));
        Mql5BuiltinSignature log1p = Fn("MathLog1p", "double", Req("value", "double"));
        Mql5BuiltinSignature acosh = Fn("MathArccosh", "double", Req("value", "double"));
        Mql5BuiltinSignature asinh = Fn("MathArcsinh", "double", Req("value", "double"));
        Mql5BuiltinSignature atanh = Fn("MathArctanh", "double", Req("value", "double"));
        Mql5BuiltinSignature cosh = Fn("MathCosh", "double", Req("value", "double"));
        Mql5BuiltinSignature sinh = Fn("MathSinh", "double", Req("value", "double"));
        Mql5BuiltinSignature tanh = Fn("MathTanh", "double", Req("value", "double"));

        return
        [
            abs, max, min, floor, ceil, round, pow, sqrt, exp, log, log10, mod,
            rand, srand, sin, cos, tan, asin, acos, atan, atan2,
            expm1, log1p, acosh, asinh, atanh, cosh, sinh, tanh,
            Fn("MathIsValidNumber", "bool", Req("number", "double")),
            Fn("MathSwap", "ushort", Req("value", "ushort")),
            Fn("MathSwap", "uint", Req("value", "uint")),
            Fn("MathSwap", "ulong", Req("value", "ulong")),

            Alias(abs, "fabs"), Alias(max, "fmax"), Alias(min, "fmin"), Alias(mod, "fmod"),
            Alias(pow, "pow"), Alias(sqrt, "sqrt"), Alias(floor, "floor"), Alias(ceil, "ceil"),
            Alias(round, "round"), Alias(exp, "exp"), Alias(log, "log"), Alias(log10, "log10"),
            Alias(rand, "rand"), Alias(srand, "srand"), Alias(sin, "sin"), Alias(cos, "cos"),
            Alias(tan, "tan"), Alias(asin, "asin"), Alias(acos, "acos"), Alias(atan, "atan"),
            Alias(atan2, "atan2"), Alias(expm1, "expm1"), Alias(log1p, "log1p"),
            Alias(acosh, "acosh"), Alias(asinh, "asinh"), Alias(atanh, "atanh"),
            Alias(cosh, "cosh"), Alias(sinh, "sinh"), Alias(tanh, "tanh")
        ];
    }

    // ---------------------------------------------------------------- Text --
    // https://www.mql5.com/en/docs/strings
    // MQL5 string mutators take the subject by reference and return a length or a
    // status flag rather than a new string: StringToUpper and StringTrimLeft edit in
    // place and do not produce a value.
    private static Mql5BuiltinSignature[] BuildText()
    {
        static Mql5BuiltinSignature Fn(string name, string returnTypeName, params Mql5BuiltinParameter[] parameters)
            => Make(name, returnTypeName, Mql5BuiltinCategory.Text, Mql5BuiltinSupport.Native, parameters);

        return
        [
            Fn("StringAdd", "bool", ByRef("string_var", "string"), Req("add_substring", "string")),
            Fn("StringBufferLen", "int", Req("string_var", "string")),
            Fn("StringCompare", "int", ByRef("string1", "string"), ByRef("string2", "string"), Opt("case_sensitive", "bool")),
            Fn("StringConcatenate", "int", ByRef("string_var", "string"), Req("argument1", "void"), Opt("argument2", "void"))
                with { IsVariadic = true, Note = "Variadic: MQL5 documents between 2 and 64 arguments. Returns the length of the formed string." },
            Fn("StringFill", "bool", ByRef("string_var", "string"), Req("character", "ushort")),
            Fn("StringFind", "int", Req("string_value", "string"), Req("match_substring", "string"), Opt("start_pos", "int")),
            Fn("StringGetCharacter", "ushort", Req("string_value", "string"), Req("pos", "int")),
            Fn("StringInit", "bool", ByRef("string_var", "string"), Opt("new_len", "int"), Opt("character", "ushort")),
            Fn("StringLen", "int", Req("string_value", "string")),
            Fn("StringReplace", "int", ByRef("str", "string"), Req("find", "string"), Req("replacement", "string")),
            Fn("StringReserve", "bool", ByRef("string_var", "string"), Req("new_capacity", "uint")),
            Fn("StringSetCharacter", "bool", ByRef("string_var", "string"), Req("pos", "int"), Req("character", "ushort")),
            Fn("StringSplit", "int", Req("string_value", "string"), Req("separator", "ushort"), RefArr("result", "string")),
            Fn("StringSubstr", "string", Req("string_value", "string"), Req("start_pos", "int"), Opt("length", "int")),
            Fn("StringToLower", "bool", ByRef("string_var", "string")),
            Fn("StringToUpper", "bool", ByRef("string_var", "string")),
            Fn("StringTrimLeft", "int", ByRef("string_var", "string")),
            Fn("StringTrimRight", "int", ByRef("string_var", "string"))
        ];
    }

    // --------------------------------------------------------------- Array --
    // https://www.mql5.com/en/docs/array
    // The Array family writes its array parameters as &array[]; ArrayInitialize is
    // the exception, documented once per element type with a plain array[]. The
    // catalog keeps one element-agnostic ArrayInitialize entry because arity and
    // reference-ness are what a binder needs, not the element type.
    private static Mql5BuiltinSignature[] BuildArray()
    {
        static Mql5BuiltinSignature Fn(string name, string returnTypeName, params Mql5BuiltinParameter[] parameters)
            => Make(name, returnTypeName, Mql5BuiltinCategory.Array, Mql5BuiltinSupport.Native, parameters);

        const string PerElement = "MQL5 documents one overload per element type; the catalog carries one element-agnostic shape.";

        return
        [
            Fn("ArrayBsearch", "int", RefArr("array", "void"), Req("value", "void")) with { Note = PerElement },
            Fn("ArrayCompare", "int", RefArr("array1", "void"), RefArr("array2", "void"), Opt("start1", "int"), Opt("start2", "int"), Opt("count", "int")),
            Fn("ArrayCopy", "int", RefArr("dst_array", "void"), RefArr("src_array", "void"), Opt("dst_start", "int"), Opt("src_start", "int"), Opt("count", "int")),
            Fn("ArrayFill", "void", RefArr("array", "void"), Req("start", "int"), Req("count", "int"), Req("value", "void")),
            Fn("ArrayFree", "void", RefArr("array", "void")),
            Fn("ArrayGetAsSeries", "bool", RefArr("array", "void")),
            Fn("ArrayInitialize", "int", Arr("array", "void"), Req("value", "void")) with { Note = PerElement },
            Fn("ArrayInsert", "bool", RefArr("dst_array", "void"), RefArr("src_array", "void"), Req("dst_start", "uint"), Opt("src_start", "uint"), Opt("count", "uint")),
            Fn("ArrayIsDynamic", "bool", RefArr("array", "void")),
            Fn("ArrayIsSeries", "bool", RefArr("array", "void")),
            Fn("ArrayMaximum", "int", RefArr("array", "void"), Opt("start", "int"), Opt("count", "int")),
            Fn("ArrayMinimum", "int", RefArr("array", "void"), Opt("start", "int"), Opt("count", "int")),
            Fn("ArrayPrint", "void", RefArr("array", "void"), Opt("digits", "uint"), Opt("separator", "string"), Opt("start", "ulong"), Opt("count", "ulong"), Opt("flags", "ulong")),
            Fn("ArrayRange", "int", RefArr("array", "void"), Req("rank_index", "int")),
            Fn("ArrayRemove", "bool", RefArr("array", "void"), Req("start", "uint"), Opt("count", "uint")),
            Fn("ArrayResize", "int", RefArr("array", "void"), Req("new_size", "int"), Opt("reserve_size", "int")),
            Fn("ArrayReverse", "bool", RefArr("array", "void"), Opt("start", "uint"), Opt("count", "uint")),
            Fn("ArraySetAsSeries", "bool", RefArr("array", "void"), Req("flag", "bool")),
            Fn("ArraySize", "int", RefArr("array", "void")),
            Fn("ArraySort", "bool", RefArr("array", "void")),
            Fn("ArraySwap", "bool", RefArr("array1", "void"), RefArr("array2", "void"))
        ];
    }

    // ---------------------------------------------------------- Conversion --
    // https://www.mql5.com/en/docs/convert
    // Pure formatting and parsing; nothing here reads market state. StringFormat is
    // documented under Conversion rather than Strings.
    private static Mql5BuiltinSignature[] BuildConversion()
    {
        static Mql5BuiltinSignature Fn(string name, string returnTypeName, params Mql5BuiltinParameter[] parameters)
            => Make(name, returnTypeName, Mql5BuiltinCategory.Conversion, Mql5BuiltinSupport.Native, parameters);

        return
        [
            Fn("CharArrayToString", "string", Arr("array", "uchar"), Opt("start", "int"), Opt("count", "int"), Opt("codepage", "uint")),
            Fn("CharArrayToStruct", "bool", ByRef("struct_object", "void"), RefArr("char_array", "uchar"), Opt("start_pos", "uint")),
            Fn("CharToString", "string", Req("char_code", "uchar")),
            Fn("ColorToARGB", "uint", Req("clr", "color"), Opt("alpha", "uchar")),
            Fn("ColorToString", "string", Req("color_value", "color"), Req("color_name", "bool")),
            Fn("DoubleToString", "string", Req("value", "double"), Opt("digits", "int")),
            Fn("EnumToString", "string", Req("value", "any_enum")),
            Fn("IntegerToString", "string", Req("number", "long"), Opt("str_len", "int"), Opt("fill_symbol", "ushort")),
            Fn("NormalizeDouble", "double", Req("value", "double"), Req("digits", "int")),
            Fn("ShortArrayToString", "string", Arr("array", "ushort"), Opt("start", "int"), Opt("count", "int")),
            Fn("ShortToString", "string", Req("symbol_code", "ushort")),
            Fn("StringFormat", "string", Req("format", "string"), Opt("argument1", "void"))
                with { IsVariadic = true, Note = "Variadic; documented under Conversion. Format rules are those of PrintFormat." },
            Fn("StringToCharArray", "int", Req("text_string", "string"), RefArr("array", "uchar"), Opt("start", "int"), Opt("count", "int"), Opt("codepage", "uint")),
            Fn("StringToColor", "color", Req("color_string", "string")),
            Fn("StringToDouble", "double", Req("value", "string")),
            Fn("StringToInteger", "long", Req("value", "string")),
            Fn("StringToShortArray", "int", Req("text_string", "string"), RefArr("array", "ushort"), Opt("start", "int"), Opt("count", "int")),
            Fn("StringToTime", "datetime", Req("value", "string")),
            Fn("StructToCharArray", "bool", ByRef("struct_object", "void"), RefArr("char_array", "uchar"), Opt("start_pos", "uint")),
            Fn("TimeToString", "string", Req("value", "datetime"), Opt("mode", "int"))
        ];
    }

    // ------------------------------------------------------------ DateTime --
    // https://www.mql5.com/en/docs/dateandtime
    // TimeToStruct and StructToTime are pure calendar arithmetic and stay Native.
    // Everything that answers what-time-is-it reads the engine clock rather than the
    // wall clock. PeriodSeconds is documented under Common Functions, not here.
    private static Mql5BuiltinSignature[] BuildDateTime()
    {
        static Mql5BuiltinSignature Clock(string name, string returnTypeName, params Mql5BuiltinParameter[] parameters)
            => Make(name, returnTypeName, Mql5BuiltinCategory.DateTime, Mql5BuiltinSupport.EngineBound, parameters);

        static Mql5BuiltinSignature Pure(string name, string returnTypeName, params Mql5BuiltinParameter[] parameters)
            => Make(name, returnTypeName, Mql5BuiltinCategory.DateTime, Mql5BuiltinSupport.Native, parameters);

        return
        [
            Clock("TimeCurrent", "datetime"),
            Clock("TimeCurrent", "datetime", ByRef("dt_struct", "MqlDateTime")),
            Clock("TimeLocal", "datetime"),
            Clock("TimeLocal", "datetime", ByRef("dt_struct", "MqlDateTime")),
            Clock("TimeGMT", "datetime"),
            Clock("TimeGMT", "datetime", ByRef("dt_struct", "MqlDateTime")),
            Clock("TimeTradeServer", "datetime"),
            Clock("TimeTradeServer", "datetime", ByRef("dt_struct", "MqlDateTime")),
            Clock("TimeGMTOffset", "int"),
            Clock("TimeDaylightSavings", "int"),
            Clock("PeriodSeconds", "int", Opt("period", "ENUM_TIMEFRAMES"))
                with { Note = "Defaults to PERIOD_CURRENT, which only the engine can resolve." },
            Pure("TimeToStruct", "bool", Req("dt", "datetime"), ByRef("dt_struct", "MqlDateTime")),
            Pure("StructToTime", "datetime", ByRef("dt_struct", "MqlDateTime"))
        ];
    }

    // --------------------------------------------------------- ChartObject --
    // https://www.mql5.com/en/docs/objects
    // Graphical objects change neither order flow nor indicator values, so the whole
    // family is a safe no-op in a backtest. It is also the largest single block of
    // the corpus: ObjectSetInteger alone outweighs every other built-in.
    private static Mql5BuiltinSignature[] BuildChartObject()
    {
        static Mql5BuiltinSignature Fn(string name, string returnTypeName, params Mql5BuiltinParameter[] parameters)
            => Make(name, returnTypeName, Mql5BuiltinCategory.ChartObject, Mql5BuiltinSupport.ChartStub, parameters);

        return
        [
            Fn("ObjectCreate", "bool",
                Req("chart_id", "long"), Req("name", "string"), Req("type", "ENUM_OBJECT"), Req("nwin", "int"),
                Req("time1", "datetime"), Req("price1", "double"),
                Opt("time2", "datetime"), Opt("price2", "double"),
                Opt("time3", "datetime"), Opt("price3", "double"))
                with
                {
                    IsVariadic = true,
                    Note = "MQL5 accepts up to 30 time/price pairs, all defaulting to 0; the first three are spelled out and the rest are the variadic tail. The reference transposes this parameter as \"sub_window nwin\"."
                },
            Fn("ObjectDelete", "bool", Req("chart_id", "long"), Req("name", "string")),
            Fn("ObjectFind", "int", Req("chart_id", "long"), Req("name", "string")),
            Fn("ObjectGetDouble", "double", Req("chart_id", "long"), Req("name", "string"), Req("prop_id", "ENUM_OBJECT_PROPERTY_DOUBLE"), Opt("prop_modifier", "int")),
            Fn("ObjectGetDouble", "bool", Req("chart_id", "long"), Req("name", "string"), Req("prop_id", "ENUM_OBJECT_PROPERTY_DOUBLE"), Req("prop_modifier", "int"), ByRef("double_var", "double")),
            Fn("ObjectGetInteger", "long", Req("chart_id", "long"), Req("name", "string"), Req("prop_id", "ENUM_OBJECT_PROPERTY_INTEGER"), Opt("prop_modifier", "int")),
            Fn("ObjectGetInteger", "bool", Req("chart_id", "long"), Req("name", "string"), Req("prop_id", "ENUM_OBJECT_PROPERTY_INTEGER"), Req("prop_modifier", "int"), ByRef("long_var", "long")),
            Fn("ObjectGetString", "string", Req("chart_id", "long"), Req("name", "string"), Req("prop_id", "ENUM_OBJECT_PROPERTY_STRING"), Opt("prop_modifier", "int")),
            Fn("ObjectGetString", "bool", Req("chart_id", "long"), Req("name", "string"), Req("prop_id", "ENUM_OBJECT_PROPERTY_STRING"), Req("prop_modifier", "int"), ByRef("string_var", "string")),
            Fn("ObjectGetTimeByValue", "datetime", Req("chart_id", "long"), Req("name", "string"), Req("value", "double"), Req("line_id", "int")),
            Fn("ObjectGetValueByTime", "double", Req("chart_id", "long"), Req("name", "string"), Req("time", "datetime"), Req("line_id", "int")),
            Fn("ObjectMove", "bool", Req("chart_id", "long"), Req("name", "string"), Req("point_index", "int"), Req("time", "datetime"), Req("price", "double")),
            Fn("ObjectName", "string", Req("chart_id", "long"), Req("pos", "int"), Opt("sub_window", "int"), Opt("type", "int")),
            Fn("ObjectSetDouble", "bool", Req("chart_id", "long"), Req("name", "string"), Req("prop_id", "ENUM_OBJECT_PROPERTY_DOUBLE"), Req("prop_value", "double")),
            Fn("ObjectSetDouble", "bool", Req("chart_id", "long"), Req("name", "string"), Req("prop_id", "ENUM_OBJECT_PROPERTY_DOUBLE"), Req("prop_modifier", "int"), Req("prop_value", "double")),
            Fn("ObjectSetInteger", "bool", Req("chart_id", "long"), Req("name", "string"), Req("prop_id", "ENUM_OBJECT_PROPERTY_INTEGER"), Req("prop_value", "long")),
            Fn("ObjectSetInteger", "bool", Req("chart_id", "long"), Req("name", "string"), Req("prop_id", "ENUM_OBJECT_PROPERTY_INTEGER"), Req("prop_modifier", "int"), Req("prop_value", "long")),
            Fn("ObjectSetString", "bool", Req("chart_id", "long"), Req("name", "string"), Req("prop_id", "ENUM_OBJECT_PROPERTY_STRING"), Req("prop_value", "string")),
            Fn("ObjectSetString", "bool", Req("chart_id", "long"), Req("name", "string"), Req("prop_id", "ENUM_OBJECT_PROPERTY_STRING"), Req("prop_modifier", "int"), Req("prop_value", "string")),
            Fn("ObjectsDeleteAll", "int", Req("chart_id", "long"), Opt("sub_window", "int"), Opt("type", "int")),
            Fn("ObjectsDeleteAll", "int", Req("chart_id", "long"), Req("prefix", "string"), Opt("sub_window", "int"), Opt("object_type", "int")),
            Fn("ObjectsTotal", "int", Req("chart_id", "long"), Opt("sub_window", "int"), Opt("type", "int")),
            Fn("TextGetSize", "bool", Req("text", "string"), ByRef("width", "uint"), ByRef("height", "uint")),
            Fn("TextOut", "bool", Req("text", "string"), Req("x", "int"), Req("y", "int"), Req("anchor", "uint"), RefArr("data", "uint"), Req("width", "uint"), Req("height", "uint"), Req("color", "uint"), Req("color_format", "ENUM_COLOR_FORMAT")),
            Fn("TextSetFont", "bool", Req("name", "string"), Req("size", "int"), Req("flags", "uint"), Opt("orientation", "int"))
        ];
    }

    // --------------------------------------------------------------- Chart --
    // https://www.mql5.com/en/docs/chart_operations
    // Chart windows do not exist in a backtest, so reads and writes of chart
    // properties are stubs. ChartScreenShot and ChartSaveTemplate are refused
    // outright because they write files.
    private static Mql5BuiltinSignature[] BuildChart()
    {
        static Mql5BuiltinSignature Fn(string name, string returnTypeName, params Mql5BuiltinParameter[] parameters)
            => Make(name, returnTypeName, Mql5BuiltinCategory.ChartObject, Mql5BuiltinSupport.ChartStub, parameters);

        return
        [
            Fn("ChartApplyTemplate", "bool", Req("chart_id", "long"), Req("filename", "string")),
            Fn("ChartClose", "bool", Opt("chart_id", "long")),
            Fn("ChartFirst", "long"),
            Fn("ChartGetDouble", "double", Req("chart_id", "long"), Req("prop_id", "ENUM_CHART_PROPERTY_DOUBLE"), Opt("sub_window", "int")),
            Fn("ChartGetDouble", "bool", Req("chart_id", "long"), Req("prop_id", "ENUM_CHART_PROPERTY_DOUBLE"), Req("sub_window", "int"), ByRef("double_var", "double")),
            Fn("ChartGetInteger", "long", Req("chart_id", "long"), Req("prop_id", "ENUM_CHART_PROPERTY_INTEGER"), Opt("sub_window", "int")),
            Fn("ChartGetInteger", "bool", Req("chart_id", "long"), Req("prop_id", "ENUM_CHART_PROPERTY_INTEGER"), Req("sub_window", "int"), ByRef("long_var", "long"))
                with { Note = "The reference prints sub_window with a default here and omits the separating comma; treated as required because the out parameter follows it." },
            Fn("ChartGetString", "string", Req("chart_id", "long"), Req("prop_id", "ENUM_CHART_PROPERTY_STRING")),
            Fn("ChartGetString", "bool", Req("chart_id", "long"), Req("prop_id", "ENUM_CHART_PROPERTY_STRING"), ByRef("string_var", "string")),
            Fn("ChartID", "long"),
            Fn("ChartIndicatorAdd", "bool", Req("chart_id", "long"), Req("sub_window", "int"), Req("indicator_handle", "int")),
            Fn("ChartIndicatorDelete", "bool", Req("chart_id", "long"), Req("sub_window", "int"), Req("indicator_shortname", "string")),
            Fn("ChartIndicatorGet", "int", Req("chart_id", "long"), Req("sub_window", "int"), Req("indicator_shortname", "string")),
            Fn("ChartIndicatorName", "string", Req("chart_id", "long"), Req("sub_window", "int"), Req("index", "int")),
            Fn("ChartIndicatorsTotal", "int", Req("chart_id", "long"), Req("sub_window", "int")),
            Fn("ChartNavigate", "bool", Req("chart_id", "long"), Req("position", "ENUM_CHART_POSITION"), Opt("shift", "int")),
            Fn("ChartNext", "long", Req("chart_id", "long")),
            Fn("ChartOpen", "long", Req("symbol", "string"), Req("period", "ENUM_TIMEFRAMES")),
            Fn("ChartPeriod", "ENUM_TIMEFRAMES", Opt("chart_id", "long")),
            Fn("ChartPriceOnDropped", "double"),
            Fn("ChartRedraw", "void", Opt("chart_id", "long")),
            Fn("ChartSaveTemplate", "bool", Req("chart_id", "long"), Req("filename", "string"))
                with { Support = Mql5BuiltinSupport.Unsupported, Note = "Writes a template file into the terminal sandbox." },
            Fn("ChartScreenShot", "bool", Req("chart_id", "long"), Req("filename", "string"), Req("width", "int"), Req("height", "int"), Opt("align_mode", "ENUM_ALIGN_MODE"))
                with { Support = Mql5BuiltinSupport.Unsupported, Note = "Writes an image file into the terminal sandbox." },
            Fn("ChartSetDouble", "bool", Req("chart_id", "long"), Req("prop_id", "ENUM_CHART_PROPERTY_DOUBLE"), Req("value", "double")),
            Fn("ChartSetInteger", "bool", Req("chart_id", "long"), Req("prop_id", "ENUM_CHART_PROPERTY_INTEGER"), Req("value", "long")),
            Fn("ChartSetInteger", "bool", Req("chart_id", "long"), Req("prop_id", "ENUM_CHART_PROPERTY_INTEGER"), Req("sub_window", "int"), Req("value", "long")),
            Fn("ChartSetString", "bool", Req("chart_id", "long"), Req("prop_id", "ENUM_CHART_PROPERTY_STRING"), Req("str_value", "string")),
            Fn("ChartSetSymbolPeriod", "bool", Req("chart_id", "long"), Req("symbol", "string"), Req("period", "ENUM_TIMEFRAMES")),
            Fn("ChartSymbol", "string", Opt("chart_id", "long")),
            Fn("ChartTimeOnDropped", "datetime"),
            Fn("ChartTimePriceToXY", "bool", Req("chart_id", "long"), Req("sub_window", "int"), Req("time", "datetime"), Req("price", "double"), ByRef("x", "int"), ByRef("y", "int")),
            Fn("ChartWindowFind", "int"),
            Fn("ChartWindowFind", "int", Req("chart_id", "long"), Req("indicator_shortname", "string"))
                with { Note = "The reference renders this overload without its closing parenthesis; the no-argument form is the one callable from inside a custom indicator." },
            Fn("ChartWindowOnDropped", "int"),
            Fn("ChartXOnDropped", "int"),
            Fn("ChartXYToTimePrice", "bool", Req("chart_id", "long"), Req("x", "int"), Req("y", "int"), ByRef("sub_window", "int"), ByRef("time", "datetime"), ByRef("price", "double")),
            Fn("ChartYOnDropped", "int")
        ];
    }

    // ----------------------------------------------------------- Indicator --
    // https://www.mql5.com/en/docs/indicators and .../customind
    // Every iXxx call returns a handle and the values are read later through
    // CopyBuffer, so a converter has to map the handle-creating call onto a LEAN
    // indicator and remember the mapping. That is what IndicatorBound marks.
    //
    // No parameter of any iXxx function has a default value. The corpus calls
    // several of them with MQL4 arity - iMA with seven arguments, iATR with four,
    // iRSI with five, iFractals with four, iADX with six - which is the MQL4 shift
    // parameter and is not part of any MQL5 signature.
    private static Mql5BuiltinSignature[] BuildIndicator()
    {
        static Mql5BuiltinSignature Fn(string name, string returnTypeName, params Mql5BuiltinParameter[] parameters)
            => Make(name, returnTypeName, Mql5BuiltinCategory.Indicator, Mql5BuiltinSupport.IndicatorBound, parameters);

        static Mql5BuiltinSignature Plot(string name, string returnTypeName, params Mql5BuiltinParameter[] parameters)
            => Make(name, returnTypeName, Mql5BuiltinCategory.Indicator, Mql5BuiltinSupport.ChartStub, parameters);

        Mql5BuiltinParameter symbol = Req("symbol", "string");
        Mql5BuiltinParameter period = Req("period", "ENUM_TIMEFRAMES");
        Mql5BuiltinParameter applied = Req("applied_price", "ENUM_APPLIED_PRICE");
        Mql5BuiltinParameter volume = Req("applied_volume", "ENUM_APPLIED_VOLUME");
        Mql5BuiltinParameter method = Req("ma_method", "ENUM_MA_METHOD");

        return
        [
            Fn("iAC", "int", symbol, period),
            Fn("iAD", "int", symbol, period, volume),
            Fn("iADX", "int", symbol, period, Req("adx_period", "int")),
            Fn("iADXWilder", "int", symbol, period, Req("adx_period", "int")),
            Fn("iAlligator", "int", symbol, period,
                Req("jaw_period", "int"), Req("jaw_shift", "int"),
                Req("teeth_period", "int"), Req("teeth_shift", "int"),
                Req("lips_period", "int"), Req("lips_shift", "int"), method, applied),
            Fn("iAMA", "int", symbol, period, Req("ama_period", "int"), Req("fast_ma_period", "int"), Req("slow_ma_period", "int"), Req("ama_shift", "int"), applied),
            Fn("iAO", "int", symbol, period),
            Fn("iATR", "int", symbol, period, Req("ma_period", "int")),
            Fn("iBands", "int", symbol, period, Req("bands_period", "int"), Req("bands_shift", "int"), Req("deviation", "double"), applied),
            Fn("iBearsPower", "int", symbol, period, Req("ma_period", "int")),
            Fn("iBullsPower", "int", symbol, period, Req("ma_period", "int")),
            Fn("iBWMFI", "int", symbol, period, volume),
            Fn("iCCI", "int", symbol, period, Req("ma_period", "int"), applied),
            Fn("iChaikin", "int", symbol, period, Req("fast_ma_period", "int"), Req("slow_ma_period", "int"), method, volume),
            Fn("iDEMA", "int", symbol, period, Req("ma_period", "int"), Req("ma_shift", "int"), applied),
            Fn("iDeMarker", "int", symbol, period, Req("ma_period", "int")),
            Fn("iEnvelopes", "int", symbol, period, Req("ma_period", "int"), Req("ma_shift", "int"), method, applied, Req("deviation", "double")),
            Fn("iForce", "int", symbol, period, Req("ma_period", "int"), method, volume),
            Fn("iFractals", "int", symbol, period),
            Fn("iFrAMA", "int", symbol, period, Req("ma_period", "int"), Req("ma_shift", "int"), applied),
            Fn("iGator", "int", symbol, period,
                Req("jaw_period", "int"), Req("jaw_shift", "int"),
                Req("teeth_period", "int"), Req("teeth_shift", "int"),
                Req("lips_period", "int"), Req("lips_shift", "int"), method, applied),
            Fn("iIchimoku", "int", symbol, period, Req("tenkan_sen", "int"), Req("kijun_sen", "int"), Req("senkou_span_b", "int")),
            Fn("iMA", "int", symbol, period, Req("ma_period", "int"), Req("ma_shift", "int"), method, applied),
            Fn("iMACD", "int", symbol, period, Req("fast_ema_period", "int"), Req("slow_ema_period", "int"), Req("signal_period", "int"), applied),
            Fn("iMFI", "int", symbol, period, Req("ma_period", "int"), volume),
            Fn("iMomentum", "int", symbol, period, Req("mom_period", "int"), applied),
            Fn("iOBV", "int", symbol, period, volume),
            Fn("iOsMA", "int", symbol, period, Req("fast_ema_period", "int"), Req("slow_ema_period", "int"), Req("signal_period", "int"), applied),
            Fn("iRSI", "int", symbol, period, Req("ma_period", "int"), applied),
            Fn("iRVI", "int", symbol, period, Req("ma_period", "int")),
            Fn("iSAR", "int", symbol, period, Req("step", "double"), Req("maximum", "double")),
            Fn("iStdDev", "int", symbol, period, Req("ma_period", "int"), Req("ma_shift", "int"), method, applied),
            Fn("iStochastic", "int", symbol, period, Req("Kperiod", "int"), Req("Dperiod", "int"), Req("slowing", "int"), method, Req("price_field", "ENUM_STO_PRICE")),
            Fn("iTEMA", "int", symbol, period, Req("ma_period", "int"), Req("ma_shift", "int"), applied),
            Fn("iTriX", "int", symbol, period, Req("ma_period", "int"), applied),
            Fn("iVIDyA", "int", symbol, period, Req("cmo_period", "int"), Req("ema_period", "int"), Req("ma_shift", "int"), applied),
            Fn("iVolumes", "int", symbol, period, volume),
            Fn("iWPR", "int", symbol, period, Req("calc_period", "int")),
            Fn("iCustom", "int", symbol, period, Req("name", "string"), Opt("parameter1", "void"))
                with
                {
                    Support = Mql5BuiltinSupport.Unsupported,
                    IsVariadic = true,
                    Note = "Loads a third-party compiled indicator; nothing in our engine can stand in for it."
                },
            Fn("IndicatorCreate", "int", symbol, period, Req("indicator_type", "ENUM_INDICATOR"), Opt("parameters_cnt", "int"), OptRefArr("parameters_array", "MqlParam")),
            Fn("IndicatorRelease", "bool", Req("indicator_handle", "int")),
            Fn("BarsCalculated", "int", Req("indicator_handle", "int")),
            Fn("CopyBuffer", "int", Req("indicator_handle", "int"), Req("buffer_num", "int"), Req("start_pos", "int"), Req("count", "int"), Arr("buffer", "double")),
            Fn("CopyBuffer", "int", Req("indicator_handle", "int"), Req("buffer_num", "int"), Req("start_time", "datetime"), Req("count", "int"), Arr("buffer", "double")),
            Fn("CopyBuffer", "int", Req("indicator_handle", "int"), Req("buffer_num", "int"), Req("start_time", "datetime"), Req("stop_time", "datetime"), Arr("buffer", "double")),

            Make("SetIndexBuffer", "bool", Mql5BuiltinCategory.Indicator, Mql5BuiltinSupport.EngineBound,
                [Req("index", "int"), RefArr("buffer", "double"), Opt("data_type", "ENUM_INDEXBUFFER_TYPE")])
                with
                {
                    Note = "data_type carries a default: MetaEditor reports 'built-in: bool SetIndexBuffer(int,double&[],ENUM_INDEXBUFFER_TYPE)' yet compiles a two-argument call with 0 errors."
                },
            Plot("IndicatorSetDouble", "bool", Req("prop_id", "int"), Req("prop_value", "double")),
            Plot("IndicatorSetDouble", "bool", Req("prop_id", "int"), Req("prop_modifier", "int"), Req("prop_value", "double")),
            Plot("IndicatorSetInteger", "bool", Req("prop_id", "int"), Req("prop_value", "int")),
            Plot("IndicatorSetInteger", "bool", Req("prop_id", "int"), Req("prop_modifier", "int"), Req("prop_value", "int")),
            Plot("IndicatorSetString", "bool", Req("prop_id", "int"), Req("prop_value", "string")),
            Plot("IndicatorSetString", "bool", Req("prop_id", "int"), Req("prop_modifier", "int"), Req("prop_value", "string")),
            Plot("PlotIndexGetInteger", "int", Req("plot_index", "int"), Req("prop_id", "int")),
            Plot("PlotIndexGetInteger", "int", Req("plot_index", "int"), Req("prop_id", "int"), Req("prop_modifier", "int")),
            Plot("PlotIndexSetDouble", "bool", Req("plot_index", "int"), Req("prop_id", "int"), Req("prop_value", "double")),
            Plot("PlotIndexSetInteger", "bool", Req("plot_index", "int"), Req("prop_id", "int"), Req("prop_value", "int")),
            Plot("PlotIndexSetInteger", "bool", Req("plot_index", "int"), Req("prop_id", "int"), Req("prop_modifier", "int"), Req("prop_value", "int")),
            Plot("PlotIndexSetString", "bool", Req("plot_index", "int"), Req("prop_id", "int"), Req("prop_value", "string"))
        ];
    }

    // ---------------------------------------------------------- MarketData --
    // https://www.mql5.com/en/docs/series
    // Bar and tick series access. Single-value readers are EngineBound; the bulk
    // Copy* readers sit with the indicator surface because a converter satisfies
    // them from the same LEAN history request.
    //
    // Not to be normalised: MQL5 spells the symbol parameter "symbol_name" without
    // const in Bars and the Copy* family, but "const string symbol" in the iXxx
    // series readers. None of the Copy* array parameters is a reference and none of
    // their parameters has a default; CopyTicks and CopyTicksRange are the only two
    // that have either.
    private static Mql5BuiltinSignature[] BuildMarketData()
    {
        static Mql5BuiltinSignature Fn(string name, string returnTypeName, params Mql5BuiltinParameter[] parameters)
            => Make(name, returnTypeName, Mql5BuiltinCategory.MarketData, Mql5BuiltinSupport.EngineBound, parameters);

        Mql5BuiltinParameter symbolName = Req("symbol_name", "string");
        Mql5BuiltinParameter symbol = Req("symbol", "string");
        Mql5BuiltinParameter timeframe = Req("timeframe", "ENUM_TIMEFRAMES");

        (string Name, string ElementType, string ArrayName)[] bulk =
        [
            ("CopyRates", "MqlRates", "rates_array"),
            ("CopyTime", "datetime", "time_array"),
            ("CopyOpen", "double", "open_array"),
            ("CopyHigh", "double", "high_array"),
            ("CopyLow", "double", "low_array"),
            ("CopyClose", "double", "close_array"),
            ("CopyTickVolume", "long", "volume_array"),
            ("CopyRealVolume", "long", "volume_array"),
            ("CopySpread", "int", "spread_array")
        ];

        List<Mql5BuiltinSignature> copies = [];
        foreach ((string name, string elementType, string arrayName) in bulk)
        {
            Mql5BuiltinParameter target = Arr(arrayName, elementType);
            copies.Add(Make(name, "int", Mql5BuiltinCategory.MarketData, Mql5BuiltinSupport.IndicatorBound,
                [symbolName, timeframe, Req("start_pos", "int"), Req("count", "int"), target]));
            copies.Add(Make(name, "int", Mql5BuiltinCategory.MarketData, Mql5BuiltinSupport.IndicatorBound,
                [symbolName, timeframe, Req("start_time", "datetime"), Req("count", "int"), target]));
            copies.Add(Make(name, "int", Mql5BuiltinCategory.MarketData, Mql5BuiltinSupport.IndicatorBound,
                [symbolName, timeframe, Req("start_time", "datetime"), Req("stop_time", "datetime"), target]));
        }

        return
        [
            .. copies,
            Fn("Bars", "int", symbolName, timeframe),
            Fn("Bars", "int", symbolName, timeframe, Req("start_time", "datetime"), Req("stop_time", "datetime")),
            Fn("iBars", "int", symbol, timeframe),
            Fn("iBarShift", "int", symbol, timeframe, Req("time", "datetime"), Opt("exact", "bool")),
            Fn("iClose", "double", symbol, timeframe, Req("shift", "int")),
            Fn("iHigh", "double", symbol, timeframe, Req("shift", "int")),
            Fn("iHighest", "int", symbol, timeframe, Req("type", "ENUM_SERIESMODE"), Opt("count", "int"), Opt("start", "int")),
            Fn("iLow", "double", symbol, timeframe, Req("shift", "int")),
            Fn("iLowest", "int", symbol, timeframe, Req("type", "ENUM_SERIESMODE"), Opt("count", "int"), Opt("start", "int")),
            Fn("iOpen", "double", symbol, timeframe, Req("shift", "int")),
            Fn("iRealVolume", "long", symbol, timeframe, Req("shift", "int")),
            Fn("iSpread", "long", symbol, timeframe, Req("shift", "int")),
            Fn("iTickVolume", "long", symbol, timeframe, Req("shift", "int")),
            Fn("iTime", "datetime", symbol, timeframe, Req("shift", "int")),
            Fn("iVolume", "long", symbol, timeframe, Req("shift", "int")),
            Fn("SeriesInfoInteger", "long", symbolName, timeframe, Req("prop_id", "ENUM_SERIES_INFO_INTEGER")),
            Fn("SeriesInfoInteger", "bool", symbolName, timeframe, Req("prop_id", "ENUM_SERIES_INFO_INTEGER"), ByRef("long_var", "long")),
            Fn("CopyTicks", "int", symbolName, RefArr("ticks_array", "MqlTick"), Opt("flags", "uint"), Opt("from", "ulong"), Opt("count", "uint")),
            Fn("CopyTicksRange", "int", symbolName, RefArr("ticks_array", "MqlTick"), Opt("flags", "uint"), Opt("from_msc", "ulong"), Opt("to_msc", "ulong")),

            // Measured the same way as CalendarValueHistory below. MetaEditor reports
            // 'built-in: bool CalendarEventById(ulong,MqlCalendarEvent&)' — exactly two
            // parameters, neither optional. It stays Unsupported for the same reason: knowing the
            // shape does not give us calendar data, and the runtime refuses the call. But the
            // shape has to be right, because a wrong arity is refused by the code generator with a
            // diagnostic that says MQL5 has no such overload, which is untrue and sends the reader
            // looking for a dialect problem that is not there.
            Make("CalendarEventById", "bool", Mql5BuiltinCategory.MarketData, Mql5BuiltinSupport.Unsupported,
                [
                    Req("event_id", "ulong"),
                    ByRef("event", "MqlCalendarEvent")
                ])
                with { Note = "Economic-calendar read; we have no calendar data source." },
            // Measured the same way as CalendarEventById above. MetaEditor reports
            // 'built-in: bool CalendarCountryById(ulong,MqlCalendarCountry&)' and refuses a
            // one-argument call with error 199, so both parameters are required and neither
            // carries a default. Same reasoning as its sibling for staying Unsupported: the
            // shape has to be right so the code generator stops claiming MQL5 declares no
            // such overload, but knowing the shape does not give us calendar data. MetaEditor
            // prints types only, so the parameter names here are ours and carry no evidence.
            Make("CalendarCountryById", "bool", Mql5BuiltinCategory.MarketData, Mql5BuiltinSupport.Unsupported,
                [
                    Req("country_id", "ulong"),
                    ByRef("country", "MqlCalendarCountry")
                ])
                with { Note = "Economic-calendar read; we have no calendar data source." },

            // MetaEditor reports
            // 'built-in: int CalendarValueLast(ulong&,MqlCalendarValue&[...],const string,const string)'
            // and compiles calls with two, three and four arguments while refusing one with
            // error 199, so the two trailing strings carry defaults and the leading change_id
            // is an in/out reference rather than a plain ulong — it is both read and rewritten
            // by the call. Unsupported for the same reason as the rest of the family.
            Make("CalendarValueLast", "int", Mql5BuiltinCategory.MarketData, Mql5BuiltinSupport.Unsupported,
                [
                    ByRef("change_id", "ulong"),
                    RefArr("values", "MqlCalendarValue"),
                    Opt("country_code", "string"),
                    Opt("currency", "string")
                ])
                with { Note = "Economic-calendar read; we have no calendar data source." },

            // The one calendar declaration we have measured. MetaEditor reports
            // 'built-in: int CalendarValueHistory(MqlCalendarValue&[...],datetime,datetime,const string,const string)'
            // and compiles calls with two, three, four and five arguments, so the last three
            // parameters carry defaults. It stays Unsupported: knowing the shape does not
            // give us calendar data.
            Make("CalendarValueHistory", "int", Mql5BuiltinCategory.MarketData, Mql5BuiltinSupport.Unsupported,
                [
                    RefArr("values", "MqlCalendarValue"),
                    Req("datetime_from", "datetime"),
                    Opt("datetime_to", "datetime"),
                    Opt("country_code", "string"),
                    Opt("currency", "string")
                ])
                with { Note = "Economic-calendar read; we have no calendar data source." }
        ];
    }

    // -------------------------------------------------------------- Symbol --
    // https://www.mql5.com/en/docs/marketinformation
    // The SymbolInfoDouble/Integer/String trio each have a direct-return form and a
    // bool form whose last parameter is an out reference.
    private static Mql5BuiltinSignature[] BuildSymbol()
    {
        static Mql5BuiltinSignature Fn(string name, string returnTypeName, params Mql5BuiltinParameter[] parameters)
            => Make(name, returnTypeName, Mql5BuiltinCategory.Symbol, Mql5BuiltinSupport.EngineBound, parameters);

        return
        [
            Fn("SymbolsTotal", "int", Req("selected", "bool")),
            Fn("SymbolName", "string", Req("pos", "int"), Req("selected", "bool")),
            Fn("SymbolSelect", "bool", Req("name", "string"), Req("select", "bool")),
            Fn("SymbolIsSynchronized", "bool", Req("name", "string")),
            Fn("SymbolInfoDouble", "double", Req("name", "string"), Req("prop_id", "ENUM_SYMBOL_INFO_DOUBLE")),
            Fn("SymbolInfoDouble", "bool", Req("name", "string"), Req("prop_id", "ENUM_SYMBOL_INFO_DOUBLE"), ByRef("double_var", "double")),
            Fn("SymbolInfoInteger", "long", Req("name", "string"), Req("prop_id", "ENUM_SYMBOL_INFO_INTEGER")),
            Fn("SymbolInfoInteger", "bool", Req("name", "string"), Req("prop_id", "ENUM_SYMBOL_INFO_INTEGER"), ByRef("long_var", "long")),
            Fn("SymbolInfoString", "string", Req("name", "string"), Req("prop_id", "ENUM_SYMBOL_INFO_STRING")),
            Fn("SymbolInfoString", "bool", Req("name", "string"), Req("prop_id", "ENUM_SYMBOL_INFO_STRING"), ByRef("string_var", "string")),
            Fn("SymbolInfoTick", "bool", Req("symbol", "string"), ByRef("tick", "MqlTick")),
            Fn("SymbolInfoMarginRate", "bool", Req("name", "string"), Req("order_type", "ENUM_ORDER_TYPE"), ByRef("initial_margin_rate", "double"), ByRef("maintenance_margin_rate", "double")),
            Fn("SymbolInfoSessionQuote", "bool", Req("name", "string"), Req("day_of_week", "ENUM_DAY_OF_WEEK"), Req("session_index", "uint"), ByRef("from", "datetime"), ByRef("to", "datetime")),
            Fn("SymbolInfoSessionTrade", "bool", Req("name", "string"), Req("day_of_week", "ENUM_DAY_OF_WEEK"), Req("session_index", "uint"), ByRef("from", "datetime"), ByRef("to", "datetime")),
            Fn("MarketBookAdd", "bool", Req("symbol", "string")),
            Fn("MarketBookRelease", "bool", Req("symbol", "string")),
            Fn("MarketBookGet", "bool", Req("symbol", "string"), RefArr("book", "MqlBookInfo"))
                with { Support = Mql5BuiltinSupport.Unsupported, Note = "Depth of market; no order-book feed exists in the engine." },
            Make("Symbol", "string", Mql5BuiltinCategory.Symbol, Mql5BuiltinSupport.EngineBound, []),
            Make("Digits", "int", Mql5BuiltinCategory.Symbol, Mql5BuiltinSupport.EngineBound, []),
            Make("Point", "double", Mql5BuiltinCategory.Symbol, Mql5BuiltinSupport.EngineBound, []),
            Make("Period", "ENUM_TIMEFRAMES", Mql5BuiltinCategory.Symbol, Mql5BuiltinSupport.EngineBound, [])
        ];
    }

    // ------------------------------------------------------------- Account --
    // https://www.mql5.com/en/docs/account
    // Only the direct-return forms are documented; there is no bool/out overload
    // here, unlike the Symbol and Position families.
    private static Mql5BuiltinSignature[] BuildAccount()
    {
        static Mql5BuiltinSignature Fn(string name, string returnTypeName, params Mql5BuiltinParameter[] parameters)
            => Make(name, returnTypeName, Mql5BuiltinCategory.Account, Mql5BuiltinSupport.EngineBound, parameters);

        return
        [
            Fn("AccountInfoDouble", "double", Req("property_id", "ENUM_ACCOUNT_INFO_DOUBLE")),
            Fn("AccountInfoInteger", "long", Req("property_id", "ENUM_ACCOUNT_INFO_INTEGER")),
            Fn("AccountInfoString", "string", Req("property_id", "ENUM_ACCOUNT_INFO_STRING"))
        ];
    }

    // --------------------------------------------- Trade / Position / Order --
    // https://www.mql5.com/en/docs/trading
    // MQL5 has no OrderClose, OrderModify or OrderDelete: every state change goes
    // through OrderSend with an MqlTradeRequest. The corpus calls all three, and
    // calls OrderSelect with MQL4's three arguments rather than MQL5's one - those
    // are dialect errors, not missing catalog entries.
    private static Mql5BuiltinSignature[] BuildTrade()
    {
        static Mql5BuiltinSignature Trade(string name, string returnTypeName, params Mql5BuiltinParameter[] parameters)
            => Make(name, returnTypeName, Mql5BuiltinCategory.Trade, Mql5BuiltinSupport.EngineBound, parameters);

        static Mql5BuiltinSignature Position(string name, string returnTypeName, params Mql5BuiltinParameter[] parameters)
            => Make(name, returnTypeName, Mql5BuiltinCategory.Position, Mql5BuiltinSupport.EngineBound, parameters);

        static Mql5BuiltinSignature Order(string name, string returnTypeName, params Mql5BuiltinParameter[] parameters)
            => Make(name, returnTypeName, Mql5BuiltinCategory.Order, Mql5BuiltinSupport.EngineBound, parameters);

        return
        [
            Trade("OrderSend", "bool", ByRef("request", "MqlTradeRequest"), ByRef("result", "MqlTradeResult")),
            Trade("OrderSendAsync", "bool", ByRef("request", "MqlTradeRequest"), ByRef("result", "MqlTradeResult")),
            Trade("OrderCheck", "bool", ByRef("request", "MqlTradeRequest"), ByRef("result", "MqlTradeCheckResult")),
            Trade("OrderCalcMargin", "bool", Req("action", "ENUM_ORDER_TYPE"), Req("symbol", "string"), Req("volume", "double"), Req("price", "double"), ByRef("margin", "double")),
            Trade("OrderCalcProfit", "bool", Req("action", "ENUM_ORDER_TYPE"), Req("symbol", "string"), Req("volume", "double"), Req("price_open", "double"), Req("price_close", "double"), ByRef("profit", "double")),

            Position("PositionsTotal", "int"),
            Position("PositionGetSymbol", "string", Req("index", "int")),
            Position("PositionSelect", "bool", Req("symbol", "string")),
            Position("PositionSelectByTicket", "bool", Req("ticket", "ulong")),
            Position("PositionGetTicket", "ulong", Req("index", "int")),
            Position("PositionGetDouble", "double", Req("property_id", "ENUM_POSITION_PROPERTY_DOUBLE")),
            Position("PositionGetDouble", "bool", Req("property_id", "ENUM_POSITION_PROPERTY_DOUBLE"), ByRef("double_var", "double")),
            Position("PositionGetInteger", "long", Req("property_id", "ENUM_POSITION_PROPERTY_INTEGER")),
            Position("PositionGetInteger", "bool", Req("property_id", "ENUM_POSITION_PROPERTY_INTEGER"), ByRef("long_var", "long")),
            Position("PositionGetString", "string", Req("property_id", "ENUM_POSITION_PROPERTY_STRING")),
            Position("PositionGetString", "bool", Req("property_id", "ENUM_POSITION_PROPERTY_STRING"), ByRef("string_var", "string")),

            Order("OrdersTotal", "int"),
            Order("OrderGetTicket", "ulong", Req("index", "int")),
            Order("OrderSelect", "bool", Req("ticket", "ulong"))
                with { Note = "MQL5 selects by ticket. The corpus also calls this with MQL4's (index, select, pool) triple, which MQL5 does not declare." },
            Order("OrderGetDouble", "double", Req("property_id", "ENUM_ORDER_PROPERTY_DOUBLE")),
            Order("OrderGetDouble", "bool", Req("property_id", "ENUM_ORDER_PROPERTY_DOUBLE"), ByRef("double_var", "double")),
            Order("OrderGetInteger", "long", Req("property_id", "ENUM_ORDER_PROPERTY_INTEGER")),
            Order("OrderGetInteger", "bool", Req("property_id", "ENUM_ORDER_PROPERTY_INTEGER"), ByRef("long_var", "long")),
            Order("OrderGetString", "string", Req("property_id", "ENUM_ORDER_PROPERTY_STRING")),
            Order("OrderGetString", "bool", Req("property_id", "ENUM_ORDER_PROPERTY_STRING"), ByRef("string_var", "string"))
        ];
    }

    // ------------------------------------------------------------- History --
    // https://www.mql5.com/en/docs/trading
    // HistorySelect must be called before any of the getters; the getters address a
    // deal or order by ticket, not by the current selection.
    private static Mql5BuiltinSignature[] BuildHistory()
    {
        static Mql5BuiltinSignature Fn(string name, string returnTypeName, params Mql5BuiltinParameter[] parameters)
            => Make(name, returnTypeName, Mql5BuiltinCategory.History, Mql5BuiltinSupport.EngineBound, parameters);

        return
        [
            Fn("HistorySelect", "bool", Req("from_date", "datetime"), Req("to_date", "datetime")),
            Fn("HistorySelectByPosition", "bool", Req("position_id", "ulong")),
            Fn("HistoryOrderSelect", "bool", Req("ticket", "ulong")),
            Fn("HistoryOrdersTotal", "int"),
            Fn("HistoryOrderGetTicket", "ulong", Req("index", "int")),
            Fn("HistoryOrderGetDouble", "double", Req("ticket_number", "ulong"), Req("property_id", "ENUM_ORDER_PROPERTY_DOUBLE")),
            Fn("HistoryOrderGetDouble", "bool", Req("ticket_number", "ulong"), Req("property_id", "ENUM_ORDER_PROPERTY_DOUBLE"), ByRef("double_var", "double")),
            Fn("HistoryOrderGetInteger", "long", Req("ticket_number", "ulong"), Req("property_id", "ENUM_ORDER_PROPERTY_INTEGER")),
            Fn("HistoryOrderGetInteger", "bool", Req("ticket_number", "ulong"), Req("property_id", "ENUM_ORDER_PROPERTY_INTEGER"), ByRef("long_var", "long")),
            Fn("HistoryOrderGetString", "string", Req("ticket_number", "ulong"), Req("property_id", "ENUM_ORDER_PROPERTY_STRING")),
            Fn("HistoryOrderGetString", "bool", Req("ticket_number", "ulong"), Req("property_id", "ENUM_ORDER_PROPERTY_STRING"), ByRef("string_var", "string")),
            Fn("HistoryDealSelect", "bool", Req("ticket", "ulong")),
            Fn("HistoryDealsTotal", "int"),
            Fn("HistoryDealGetTicket", "ulong", Req("index", "int")),
            Fn("HistoryDealGetDouble", "double", Req("ticket_number", "ulong"), Req("property_id", "ENUM_DEAL_PROPERTY_DOUBLE")),
            Fn("HistoryDealGetDouble", "bool", Req("ticket_number", "ulong"), Req("property_id", "ENUM_DEAL_PROPERTY_DOUBLE"), ByRef("double_var", "double")),
            Fn("HistoryDealGetInteger", "long", Req("ticket_number", "ulong"), Req("property_id", "ENUM_DEAL_PROPERTY_INTEGER")),
            Fn("HistoryDealGetInteger", "bool", Req("ticket_number", "ulong"), Req("property_id", "ENUM_DEAL_PROPERTY_INTEGER"), ByRef("long_var", "long")),
            Fn("HistoryDealGetString", "string", Req("ticket_number", "ulong"), Req("property_id", "ENUM_DEAL_PROPERTY_STRING")),
            Fn("HistoryDealGetString", "bool", Req("ticket_number", "ulong"), Req("property_id", "ENUM_DEAL_PROPERTY_STRING"), ByRef("string_var", "string"))
        ];
    }

    // ------------------------------------------------------------ Terminal --
    // https://www.mql5.com/en/docs/common and .../check
    // Print and PrintFormat are diagnostics we can honour with an engine log, so
    // they are Native. Comment and Alert paint the terminal and are stubs. Anything
    // that reaches outside the process - mail, notifications, HTTP, sound files,
    // resources, terminal state - is refused.
    //
    // Sleep is refused rather than stubbed on purpose: silently dropping it changes
    // the meaning of retry and throttle loops, and a backtest has no clock to sleep
    // against. A converter must restructure those loops instead.
    private static Mql5BuiltinSignature[] BuildTerminal()
    {
        static Mql5BuiltinSignature Fn(string name, string returnTypeName, Mql5BuiltinSupport support, params Mql5BuiltinParameter[] parameters)
            => Make(name, returnTypeName, Mql5BuiltinCategory.Terminal, support, parameters);

        const string OutsideProcess = "Reaches outside the process; refused with the rest of the terminal I/O surface.";

        return
        [
            Fn("Print", "void", Mql5BuiltinSupport.Native, Req("argument", "void"))
                with { IsVariadic = true, Note = "Variadic; maps onto the engine log. The corpus calls it with up to 26 arguments." },
            Fn("PrintFormat", "void", Mql5BuiltinSupport.Native, Req("format_string", "string"))
                with { IsVariadic = true, Note = "Variadic; maps onto the engine log." },
            Fn("printf", "void", Mql5BuiltinSupport.Native, Req("format_string", "string"))
                with
                {
                    IsVariadic = true,
                    Note = "Documented C-style alias of PrintFormat. MetaEditor reports 'built-in: void printf(const string,...)'."
                },
            Fn("Comment", "void", Mql5BuiltinSupport.ChartStub, Req("argument", "void"))
                with { IsVariadic = true, Note = "Variadic; paints the chart comment area." },
            Fn("Alert", "void", Mql5BuiltinSupport.ChartStub, Req("argument", "void"))
                with { IsVariadic = true, Note = "Variadic; opens a terminal dialog." },
            Fn("Sleep", "void", Mql5BuiltinSupport.Unsupported, Req("milliseconds", "int"))
                with { Note = "No deterministic backtest semantics; dropping it silently would change the meaning of retry loops." },
            Fn("MessageBox", "int", Mql5BuiltinSupport.Unsupported, Req("text", "string"), Opt("caption", "string"), Opt("flags", "int"))
                with { Note = "Blocks on a modal dialog; there is no operator to answer it." },
            Fn("PlaySound", "bool", Mql5BuiltinSupport.ChartStub, Req("filename", "string")),
            Fn("ExpertRemove", "void", Mql5BuiltinSupport.EngineBound),
            Fn("GetTickCount", "uint", Mql5BuiltinSupport.Native),
            Fn("GetTickCount64", "ulong", Mql5BuiltinSupport.Native),
            Fn("GetMicrosecondCount", "ulong", Mql5BuiltinSupport.Native),
            Fn("ZeroMemory", "void", Mql5BuiltinSupport.Native, ByRef("variable", "void")),
            Fn("GetLastError", "int", Mql5BuiltinSupport.EngineBound),
            Fn("ResetLastError", "void", Mql5BuiltinSupport.EngineBound),
            Fn("IsStopped", "bool", Mql5BuiltinSupport.EngineBound),
            Fn("UninitializeReason", "int", Mql5BuiltinSupport.EngineBound),
            Fn("MQLInfoInteger", "int", Mql5BuiltinSupport.EngineBound, Req("property_id", "int")),
            Fn("MQLInfoString", "string", Mql5BuiltinSupport.EngineBound, Req("property_id", "int")),

            // The MQL5* spellings are still live, not withdrawn: MetaEditor reports
            // 'built-in: int MQL5InfoInteger(ENUM_MQL5_INFO_INTEGER)' and compiles a call,
            // warning only that MQL5_TESTING is deprecated in favour of MQL_TESTER. The
            // ENUM_MQL5_* members are already carried in Mql5BuiltinConstants with the same
            // measured values as their ENUM_MQL_* counterparts, so the two spellings agree.
            Fn("MQL5InfoInteger", "int", Mql5BuiltinSupport.EngineBound, Req("property_id", "ENUM_MQL5_INFO_INTEGER"))
                with { Note = "Deprecated spelling of MQLInfoInteger; MetaEditor still declares it." },
            Fn("MQL5InfoString", "string", Mql5BuiltinSupport.EngineBound, Req("property_id", "ENUM_MQL5_INFO_STRING"))
                with { Note = "Deprecated spelling of MQLInfoString; MetaEditor still declares it." },
            Fn("TesterStatistics", "double", Mql5BuiltinSupport.EngineBound, Req("statistic_id", "ENUM_STATISTICS")),
            Fn("TesterHideIndicators", "void", Mql5BuiltinSupport.ChartStub, Req("hide", "bool")),
            Fn("TerminalInfoInteger", "int", Mql5BuiltinSupport.Unsupported, Req("property_id", "int"))
                with { Note = "Reports terminal state that has no counterpart in the engine." },
            Fn("TerminalInfoDouble", "double", Mql5BuiltinSupport.Unsupported, Req("property_id", "int"))
                with { Note = "Reports terminal state that has no counterpart in the engine." },
            Fn("TerminalInfoString", "string", Mql5BuiltinSupport.Unsupported, Req("property_id", "int"))
                with { Note = "Reports terminal paths and state that have no counterpart in the engine." },
            Fn("ResourceCreate", "bool", Mql5BuiltinSupport.Unsupported, Req("resource_name", "string"), Req("path", "string"))
                with { Note = OutsideProcess },
            Fn("ResourceCreate", "bool", Mql5BuiltinSupport.Unsupported,
                Req("resource_name", "string"), RefArr("data", "uint"), Req("img_width", "uint"), Req("img_height", "uint"),
                Req("data_xoffset", "uint"), Req("data_yoffset", "uint"), Req("data_width", "uint"), Req("color_format", "ENUM_COLOR_FORMAT"))
                with { Note = OutsideProcess },
            Fn("ResourceReadImage", "bool", Mql5BuiltinSupport.Unsupported, Req("resource_name", "string"), RefArr("data", "uint"), ByRef("width", "uint"), ByRef("height", "uint"))
                with { Note = OutsideProcess },

            // Documented under /docs/network, not /docs/common.
            Fn("WebRequest", "int", Mql5BuiltinSupport.Unsupported,
                Req("method", "string"), Req("url", "string"), Req("cookie", "string"), Req("referer", "string"),
                Req("timeout", "int"), RefArr("data", "char"), Req("data_size", "int"),
                RefArr("result", "char"), ByRef("result_headers", "string"))
                with { Note = OutsideProcess },
            Fn("WebRequest", "int", Mql5BuiltinSupport.Unsupported,
                Req("method", "string"), Req("url", "string"), Req("headers", "string"),
                Req("timeout", "int"), RefArr("data", "char"), RefArr("result", "char"), ByRef("result_headers", "string"))
                with { Note = OutsideProcess },
            Fn("SendMail", "bool", Mql5BuiltinSupport.Unsupported, Req("subject", "string"), Req("some_text", "string"))
                with { Note = OutsideProcess },
            Fn("SendNotification", "bool", Mql5BuiltinSupport.Unsupported, Req("text", "string"))
                with { Note = OutsideProcess },
            Fn("SendFTP", "bool", Mql5BuiltinSupport.Unsupported, Req("filename", "string"), Opt("ftp_path", "string"))
                with { Note = OutsideProcess },
            Fn("TerminalClose", "bool", Mql5BuiltinSupport.Unsupported, Req("ret_code", "int"))
                with { Note = "Shuts the terminal down." },
            Fn("ResourceFree", "bool", Mql5BuiltinSupport.Unsupported, Req("resource_name", "string"))
                with { Note = OutsideProcess },
            Fn("ResourceSave", "bool", Mql5BuiltinSupport.Unsupported, Req("resource_name", "string"), Req("file_name", "string"))
                with { Note = OutsideProcess },
            Fn("TesterStop", "void", Mql5BuiltinSupport.EngineBound),
            Fn("TesterWithdrawal", "bool", Mql5BuiltinSupport.EngineBound, Req("money", "double")),
            Fn("DebugBreak", "void", Mql5BuiltinSupport.Unsupported)
                with { Note = "Debugger breakpoint; meaningless outside MetaEditor." },
            Fn("TranslateKey", "short", Mql5BuiltinSupport.Unsupported, Req("key_code", "int"))
                with { Note = "Reads the operating system keyboard layout." },
            Fn("CryptEncode", "int", Mql5BuiltinSupport.Unsupported, Req("method", "ENUM_CRYPT_METHOD"), RefArr("data", "uchar"), RefArr("key", "uchar"), RefArr("result", "uchar"))
                with { Note = OutsideProcess },
            Fn("CryptDecode", "int", Mql5BuiltinSupport.Unsupported, Req("method", "ENUM_CRYPT_METHOD"), RefArr("data", "uchar"), RefArr("key", "uchar"), RefArr("result", "uchar"))
                with { Note = "The CryptDecode reference page titles its own declaration block \"CryptEncode\"; this is the parameter list that page shows." }
        ];
    }

    // -------------------------------------------------------------- Global --
    // https://www.mql5.com/en/docs/globals
    // Global variables of the terminal outlive the program and are shared across
    // every chart in the installation. That is hidden cross-run state a backtest
    // cannot reproduce, so the whole family is refused rather than emulated.
    private static Mql5BuiltinSignature[] BuildGlobal()
    {
        static Mql5BuiltinSignature Fn(string name, string returnTypeName, params Mql5BuiltinParameter[] parameters)
            => Make(name, returnTypeName, Mql5BuiltinCategory.Global, Mql5BuiltinSupport.Unsupported, parameters);

        return
        [
            Fn("GlobalVariableCheck", "bool", Req("name", "string")),
            Fn("GlobalVariableDel", "bool", Req("name", "string")),
            Fn("GlobalVariableGet", "double", Req("name", "string")),
            Fn("GlobalVariableGet", "bool", Req("name", "string"), ByRef("double_var", "double")),
            Fn("GlobalVariableName", "string", Req("index", "int")),
            Fn("GlobalVariableSet", "datetime", Req("name", "string"), Req("value", "double")),
            Fn("GlobalVariablesTotal", "int"),
            Fn("GlobalVariableTime", "datetime", Req("name", "string")),
            Fn("GlobalVariableTemp", "bool", Req("name", "string")),
            Fn("GlobalVariableSetOnCondition", "bool", Req("name", "string"), Req("value", "double"), Req("check_value", "double")),
            Fn("GlobalVariablesFlush", "void"),
            Fn("GlobalVariablesDeleteAll", "int", Opt("prefix_name", "string"), Opt("limit_data", "datetime"))
        ];
    }

    // --------------------------------------------------------------- Event --
    // https://www.mql5.com/en/docs/eventfunctions
    private static Mql5BuiltinSignature[] BuildEvent() =>
    [
        Make("EventSetTimer", "bool", Mql5BuiltinCategory.Event, Mql5BuiltinSupport.EngineBound, [Req("seconds", "int")]),
        Make("EventSetMillisecondTimer", "bool", Mql5BuiltinCategory.Event, Mql5BuiltinSupport.EngineBound, [Req("milliseconds", "int")]),
        Make("EventKillTimer", "void", Mql5BuiltinCategory.Event, Mql5BuiltinSupport.EngineBound, []),
        Make("EventChartCustom", "bool", Mql5BuiltinCategory.Event, Mql5BuiltinSupport.ChartStub,
            [Req("chart_id", "long"), Req("custom_event_id", "ushort"), Req("lparam", "long"), Req("dparam", "double"), Req("sparam", "string")])
    ];

    // ---------------------------------------------------------------- File --
    // https://www.mql5.com/en/docs/files
    // Every entry is refused. File access makes a strategy depend on state outside
    // the backtest, and the terminal sandbox it reads and writes does not exist in
    // our engine. Catalogued anyway so a binder can name what it is refusing.
    private static Mql5BuiltinSignature[] BuildFile()
    {
        static Mql5BuiltinSignature Fn(string name, string returnTypeName, params Mql5BuiltinParameter[] parameters)
            => Make(name, returnTypeName, Mql5BuiltinCategory.File, Mql5BuiltinSupport.Unsupported, parameters);

        return
        [
            Fn("FileOpen", "int", Req("file_name", "string"), Req("open_flags", "int"), Opt("delimiter", "short"), Opt("codepage", "uint")),
            Fn("FileClose", "void", Req("file_handle", "int")),
            Fn("FileDelete", "bool", Req("file_name", "string"), Opt("common_flag", "int")),
            Fn("FileCopy", "bool", Req("src_file_name", "string"), Req("common_flag", "int"), Req("dst_file_name", "string"), Req("mode_flags", "int")),
            Fn("FileMove", "bool", Req("src_file_name", "string"), Req("common_flag", "int"), Req("dst_file_name", "string"), Req("mode_flags", "int")),
            Fn("FileIsExist", "bool", Req("file_name", "string"), Opt("common_flag", "int")),
            Fn("FileIsEnding", "bool", Req("file_handle", "int")),
            Fn("FileIsLineEnding", "bool", Req("file_handle", "int")),
            Fn("FileSeek", "bool", Req("file_handle", "int"), Req("offset", "long"), Req("origin", "ENUM_FILE_POSITION")),
            Fn("FileSize", "ulong", Req("file_handle", "int")),
            Fn("FileTell", "ulong", Req("file_handle", "int")),
            Fn("FileFlush", "void", Req("file_handle", "int")),
            Fn("FileReadString", "string", Req("file_handle", "int"), Opt("length", "int")),
            Fn("FileReadDouble", "double", Req("file_handle", "int")),
            Fn("FileReadInteger", "int", Req("file_handle", "int"), Opt("size", "int")),
            Fn("FileReadLong", "long", Req("file_handle", "int")),
            Fn("FileReadNumber", "double", Req("file_handle", "int")),
            Fn("FileReadBool", "bool", Req("file_handle", "int")),
            Fn("FileReadDatetime", "datetime", Req("file_handle", "int")),
            Fn("FileReadFloat", "float", Req("file_handle", "int")),
            Fn("FileReadArray", "uint", Req("file_handle", "int"), RefArr("array", "void"), Opt("start", "int"), Opt("count", "int")),
            Fn("FileReadStruct", "uint", Req("file_handle", "int"), ByRef("struct_object", "void"), Opt("size", "int")),
            Fn("FileWrite", "uint", Req("file_handle", "int"))
                with { IsVariadic = true, Note = "Variadic; the corpus calls it with up to 27 arguments." },
            Fn("FileWriteString", "uint", Req("file_handle", "int"), Req("text_string", "string"), Opt("length", "int")),
            Fn("FileWriteDouble", "uint", Req("file_handle", "int"), Req("value", "double")),
            Fn("FileWriteInteger", "uint", Req("file_handle", "int"), Req("value", "int"), Opt("size", "int")),
            Fn("FileWriteLong", "uint", Req("file_handle", "int"), Req("value", "long")),
            Fn("FileWriteFloat", "uint", Req("file_handle", "int"), Req("value", "float")),
            Fn("FileWriteArray", "uint", Req("file_handle", "int"), RefArr("array", "void"), Opt("start", "int"), Opt("count", "int")),
            Fn("FileWriteStruct", "uint", Req("file_handle", "int"), ByRef("struct_object", "void"), Opt("size", "int")),
            Fn("FolderCreate", "bool", Req("folder_name", "string"), Opt("common_flag", "int")),
            Fn("FolderDelete", "bool", Req("folder_name", "string"), Opt("common_flag", "int")),
            Fn("FolderClean", "bool", Req("folder_name", "string"), Opt("common_flag", "int")),

            // The find family uses long handles, unlike the int handles everywhere else here.
            Fn("FileFindFirst", "long", Req("file_filter", "string"), ByRef("returned_filename", "string"), Opt("common_flag", "int")),
            Fn("FileFindNext", "bool", Req("search_handle", "long"), ByRef("returned_filename", "string")),
            Fn("FileFindClose", "void", Req("search_handle", "long"))
        ];
    }

    // ----------------------------------------------------------- Constants --
    // MetaQuotes does NOT publish numeric values for its enumerations: every
    // enumeration table in the reference has exactly two columns, ID and
    // Description. Values are published in only four places, and those are the only
    // ones carried here with a value:
    //   ENUM_TIMEFRAMES          - the official MQL5 Book
    //   named constants          - /constants/namedconstants/otherconstants
    //   uninitialisation reasons - /constants/namedconstants/uninit
    //   trade return codes       - /constants/errorswarnings/enum_trade_return_codes
    //
    // Every other constant is carried by name and declaring enumeration with a null
    // value. That is deliberate: a binder that needs to know POSITION_TYPE_BUY is a
    // member of ENUM_POSITION_TYPE gets that fact, and a binder that asks for its
    // number is told we do not know rather than being handed a plausible ordinal.
    // The enumerations are not safely ordinal - ENUM_TRADE_REQUEST_ACTIONS is not
    // contiguous from zero, and ORDER_FILLING_BOC was inserted before
    // ORDER_FILLING_RETURN in a later build.
    internal static Mql5BuiltinConstant[] DeclareConstants() =>
    [
        .. Valued("ENUM_TIMEFRAMES",
            ("PERIOD_CURRENT", 0), ("PERIOD_M1", 1), ("PERIOD_M2", 2), ("PERIOD_M3", 3),
            ("PERIOD_M4", 4), ("PERIOD_M5", 5), ("PERIOD_M6", 6), ("PERIOD_M10", 10),
            ("PERIOD_M12", 12), ("PERIOD_M15", 15), ("PERIOD_M20", 20), ("PERIOD_M30", 30),
            ("PERIOD_H1", 16385), ("PERIOD_H2", 16386), ("PERIOD_H3", 16387), ("PERIOD_H4", 16388),
            ("PERIOD_H6", 16390), ("PERIOD_H8", 16392), ("PERIOD_H12", 16396), ("PERIOD_D1", 16408),
            ("PERIOD_W1", 32769), ("PERIOD_MN1", 49153)),

        // Free-standing named constants. WHOLE_ARRAY is -1 in MQL5 where MQL4 used
        // 0, and MQL5 spells the absent colour clrNONE, never MQL4's CLR_NONE.
        new("CHARTS_MAX", 100, null),
        new("clrNONE", -1, null),
        new("INVALID_HANDLE", -1, null),
        new("NULL", 0, null),
        new("WHOLE_ARRAY", -1, null),
        new("WRONG_VALUE", -1, null),

        .. Valued("UninitializeReason",
            ("REASON_PROGRAM", 0), ("REASON_REMOVE", 1), ("REASON_RECOMPILE", 2),
            ("REASON_CHARTCHANGE", 3), ("REASON_CHARTCLOSE", 4), ("REASON_PARAMETERS", 5),
            ("REASON_ACCOUNT", 6), ("REASON_TEMPLATE", 7), ("REASON_INITFAILED", 8),
            ("REASON_CLOSE", 9)),

        .. Valued("ENUM_TRADE_RETURN_CODES",
            ("TRADE_RETCODE_REQUOTE", 10004), ("TRADE_RETCODE_REJECT", 10006),
            ("TRADE_RETCODE_CANCEL", 10007), ("TRADE_RETCODE_PLACED", 10008),
            ("TRADE_RETCODE_DONE", 10009), ("TRADE_RETCODE_DONE_PARTIAL", 10010),
            ("TRADE_RETCODE_ERROR", 10011), ("TRADE_RETCODE_TIMEOUT", 10012),
            ("TRADE_RETCODE_INVALID", 10013), ("TRADE_RETCODE_INVALID_VOLUME", 10014),
            ("TRADE_RETCODE_INVALID_PRICE", 10015), ("TRADE_RETCODE_INVALID_STOPS", 10016),
            ("TRADE_RETCODE_TRADE_DISABLED", 10017), ("TRADE_RETCODE_MARKET_CLOSED", 10018),
            ("TRADE_RETCODE_NO_MONEY", 10019), ("TRADE_RETCODE_PRICE_CHANGED", 10020),
            ("TRADE_RETCODE_PRICE_OFF", 10021), ("TRADE_RETCODE_INVALID_EXPIRATION", 10022),
            ("TRADE_RETCODE_ORDER_CHANGED", 10023), ("TRADE_RETCODE_TOO_MANY_REQUESTS", 10024),
            ("TRADE_RETCODE_NO_CHANGES", 10025), ("TRADE_RETCODE_SERVER_DISABLES_AT", 10026),
            ("TRADE_RETCODE_CLIENT_DISABLES_AT", 10027), ("TRADE_RETCODE_LOCKED", 10028),
            ("TRADE_RETCODE_FROZEN", 10029), ("TRADE_RETCODE_INVALID_FILL", 10030),
            ("TRADE_RETCODE_CONNECTION", 10031), ("TRADE_RETCODE_ONLY_REAL", 10032),
            ("TRADE_RETCODE_LIMIT_ORDERS", 10033), ("TRADE_RETCODE_LIMIT_VOLUME", 10034),
            ("TRADE_RETCODE_INVALID_ORDER", 10035), ("TRADE_RETCODE_POSITION_CLOSED", 10036),
            ("TRADE_RETCODE_INVALID_CLOSE_VOLUME", 10038), ("TRADE_RETCODE_CLOSE_ORDER_EXIST", 10039),
            ("TRADE_RETCODE_LIMIT_POSITIONS", 10040), ("TRADE_RETCODE_REJECT_CANCEL", 10041),
            ("TRADE_RETCODE_LONG_ONLY", 10042), ("TRADE_RETCODE_SHORT_ONLY", 10043),
            ("TRADE_RETCODE_CLOSE_ONLY", 10044), ("TRADE_RETCODE_FIFO_CLOSE", 10045),
            ("TRADE_RETCODE_HEDGE_PROHIBITED", 10046)),

        .. Named("ENUM_ORDER_TYPE",
            "ORDER_TYPE_BUY", "ORDER_TYPE_SELL", "ORDER_TYPE_BUY_LIMIT", "ORDER_TYPE_SELL_LIMIT",
            "ORDER_TYPE_BUY_STOP", "ORDER_TYPE_SELL_STOP", "ORDER_TYPE_BUY_STOP_LIMIT",
            "ORDER_TYPE_SELL_STOP_LIMIT", "ORDER_TYPE_CLOSE_BY"),
        .. Named("ENUM_TRADE_REQUEST_ACTIONS",
            "TRADE_ACTION_DEAL", "TRADE_ACTION_PENDING", "TRADE_ACTION_SLTP",
            "TRADE_ACTION_MODIFY", "TRADE_ACTION_REMOVE", "TRADE_ACTION_CLOSE_BY"),
        .. Named("ENUM_ORDER_STATE",
            "ORDER_STATE_STARTED", "ORDER_STATE_PLACED", "ORDER_STATE_CANCELED", "ORDER_STATE_PARTIAL",
            "ORDER_STATE_FILLED", "ORDER_STATE_REJECTED", "ORDER_STATE_EXPIRED",
            "ORDER_STATE_REQUEST_ADD", "ORDER_STATE_REQUEST_MODIFY", "ORDER_STATE_REQUEST_CANCEL"),
        .. Named("ENUM_ORDER_TYPE_FILLING",
            "ORDER_FILLING_FOK", "ORDER_FILLING_IOC", "ORDER_FILLING_BOC", "ORDER_FILLING_RETURN"),
        .. Named("ENUM_ORDER_TYPE_TIME",
            "ORDER_TIME_GTC", "ORDER_TIME_DAY", "ORDER_TIME_SPECIFIED", "ORDER_TIME_SPECIFIED_DAY"),
        .. Named("ENUM_ORDER_REASON",
            "ORDER_REASON_CLIENT", "ORDER_REASON_MOBILE", "ORDER_REASON_WEB", "ORDER_REASON_EXPERT",
            "ORDER_REASON_SL", "ORDER_REASON_TP", "ORDER_REASON_SO"),
        .. Named("ENUM_ORDER_PROPERTY_INTEGER",
            "ORDER_TICKET", "ORDER_TIME_SETUP", "ORDER_TYPE", "ORDER_STATE", "ORDER_TIME_EXPIRATION",
            "ORDER_TIME_DONE", "ORDER_TIME_SETUP_MSC", "ORDER_TIME_DONE_MSC", "ORDER_TYPE_FILLING",
            "ORDER_TYPE_TIME", "ORDER_MAGIC", "ORDER_REASON", "ORDER_POSITION_ID", "ORDER_POSITION_BY_ID"),
        .. Named("ENUM_ORDER_PROPERTY_DOUBLE",
            "ORDER_VOLUME_INITIAL", "ORDER_VOLUME_CURRENT", "ORDER_PRICE_OPEN", "ORDER_SL", "ORDER_TP",
            "ORDER_PRICE_CURRENT", "ORDER_PRICE_STOPLIMIT"),
        .. Named("ENUM_ORDER_PROPERTY_STRING", "ORDER_SYMBOL", "ORDER_COMMENT", "ORDER_EXTERNAL_ID"),

        .. Named("ENUM_POSITION_TYPE", "POSITION_TYPE_BUY", "POSITION_TYPE_SELL"),
        .. Named("ENUM_POSITION_REASON",
            "POSITION_REASON_CLIENT", "POSITION_REASON_MOBILE", "POSITION_REASON_WEB", "POSITION_REASON_EXPERT"),
        .. Named("ENUM_POSITION_PROPERTY_INTEGER",
            "POSITION_TICKET", "POSITION_TIME", "POSITION_TIME_MSC", "POSITION_TIME_UPDATE",
            "POSITION_TIME_UPDATE_MSC", "POSITION_TYPE", "POSITION_MAGIC", "POSITION_IDENTIFIER",
            "POSITION_REASON"),
        .. Named("ENUM_POSITION_PROPERTY_DOUBLE",
            "POSITION_VOLUME", "POSITION_PRICE_OPEN", "POSITION_SL", "POSITION_TP",
            "POSITION_PRICE_CURRENT", "POSITION_SWAP", "POSITION_PROFIT"),
        .. Named("ENUM_POSITION_PROPERTY_STRING",
            "POSITION_SYMBOL", "POSITION_COMMENT", "POSITION_EXTERNAL_ID"),

        .. Named("ENUM_DEAL_PROPERTY_INTEGER",
            "DEAL_TICKET", "DEAL_ORDER", "DEAL_TIME", "DEAL_TIME_MSC", "DEAL_TYPE", "DEAL_ENTRY",
            "DEAL_MAGIC", "DEAL_REASON", "DEAL_POSITION_ID"),
        .. Named("ENUM_DEAL_PROPERTY_DOUBLE",
            "DEAL_VOLUME", "DEAL_PRICE", "DEAL_COMMISSION", "DEAL_SWAP", "DEAL_PROFIT", "DEAL_FEE",
            "DEAL_SL", "DEAL_TP"),
        .. Named("ENUM_DEAL_PROPERTY_STRING", "DEAL_SYMBOL", "DEAL_COMMENT", "DEAL_EXTERNAL_ID"),
        .. Named("ENUM_DEAL_TYPE",
            "DEAL_TYPE_BUY", "DEAL_TYPE_SELL", "DEAL_TYPE_BALANCE", "DEAL_TYPE_CREDIT",
            "DEAL_TYPE_CHARGE", "DEAL_TYPE_CORRECTION", "DEAL_TYPE_BONUS", "DEAL_TYPE_COMMISSION",
            "DEAL_TYPE_COMMISSION_DAILY", "DEAL_TYPE_COMMISSION_MONTHLY",
            "DEAL_TYPE_COMMISSION_AGENT_DAILY", "DEAL_TYPE_COMMISSION_AGENT_MONTHLY",
            "DEAL_TYPE_INTEREST", "DEAL_TYPE_BUY_CANCELED", "DEAL_TYPE_SELL_CANCELED",
            "DEAL_DIVIDEND", "DEAL_DIVIDEND_FRANKED", "DEAL_TAX"),
        .. Named("ENUM_DEAL_ENTRY", "DEAL_ENTRY_IN", "DEAL_ENTRY_OUT", "DEAL_ENTRY_INOUT", "DEAL_ENTRY_OUT_BY"),

        .. Named("ENUM_ACCOUNT_INFO_INTEGER",
            "ACCOUNT_LOGIN", "ACCOUNT_TRADE_MODE", "ACCOUNT_LEVERAGE", "ACCOUNT_LIMIT_ORDERS",
            "ACCOUNT_MARGIN_SO_MODE", "ACCOUNT_TRADE_ALLOWED", "ACCOUNT_TRADE_EXPERT",
            "ACCOUNT_MARGIN_MODE", "ACCOUNT_CURRENCY_DIGITS", "ACCOUNT_FIFO_CLOSE", "ACCOUNT_HEDGE_ALLOWED"),
        .. Named("ENUM_ACCOUNT_INFO_DOUBLE",
            "ACCOUNT_BALANCE", "ACCOUNT_CREDIT", "ACCOUNT_PROFIT", "ACCOUNT_EQUITY", "ACCOUNT_MARGIN",
            "ACCOUNT_MARGIN_FREE", "ACCOUNT_MARGIN_LEVEL", "ACCOUNT_MARGIN_SO_CALL",
            "ACCOUNT_MARGIN_SO_SO", "ACCOUNT_MARGIN_INITIAL", "ACCOUNT_MARGIN_MAINTENANCE",
            "ACCOUNT_ASSETS", "ACCOUNT_LIABILITIES", "ACCOUNT_COMMISSION_BLOCKED"),
        .. Named("ENUM_ACCOUNT_INFO_STRING",
            "ACCOUNT_NAME", "ACCOUNT_SERVER", "ACCOUNT_CURRENCY", "ACCOUNT_COMPANY"),
        .. Named("ENUM_ACCOUNT_TRADE_MODE",
            "ACCOUNT_TRADE_MODE_DEMO", "ACCOUNT_TRADE_MODE_CONTEST", "ACCOUNT_TRADE_MODE_REAL"),
        .. Named("ENUM_ACCOUNT_MARGIN_MODE",
            "ACCOUNT_MARGIN_MODE_RETAIL_NETTING", "ACCOUNT_MARGIN_MODE_EXCHANGE",
            "ACCOUNT_MARGIN_MODE_RETAIL_HEDGING"),

        .. Named("ENUM_SYMBOL_INFO_INTEGER",
            "SYMBOL_SUBSCRIPTION_DELAY", "SYMBOL_SECTOR", "SYMBOL_INDUSTRY", "SYMBOL_CUSTOM",
            "SYMBOL_BACKGROUND_COLOR", "SYMBOL_CHART_MODE", "SYMBOL_EXIST", "SYMBOL_SELECT",
            "SYMBOL_VISIBLE", "SYMBOL_SESSION_DEALS", "SYMBOL_SESSION_BUY_ORDERS",
            "SYMBOL_SESSION_SELL_ORDERS", "SYMBOL_VOLUME", "SYMBOL_VOLUMEHIGH", "SYMBOL_VOLUMELOW",
            "SYMBOL_TIME", "SYMBOL_TIME_MSC", "SYMBOL_DIGITS", "SYMBOL_SPREAD_FLOAT", "SYMBOL_SPREAD",
            "SYMBOL_TICKS_BOOKDEPTH", "SYMBOL_TRADE_CALC_MODE", "SYMBOL_TRADE_MODE", "SYMBOL_START_TIME",
            "SYMBOL_EXPIRATION_TIME", "SYMBOL_TRADE_STOPS_LEVEL", "SYMBOL_TRADE_FREEZE_LEVEL",
            "SYMBOL_TRADE_EXEMODE", "SYMBOL_SWAP_MODE", "SYMBOL_SWAP_ROLLOVER3DAYS",
            "SYMBOL_MARGIN_HEDGED_USE_LEG", "SYMBOL_EXPIRATION_MODE", "SYMBOL_FILLING_MODE",
            "SYMBOL_ORDER_MODE", "SYMBOL_ORDER_GTC_MODE", "SYMBOL_OPTION_MODE", "SYMBOL_OPTION_RIGHT"),
        .. Named("ENUM_SYMBOL_INFO_DOUBLE",
            "SYMBOL_BID", "SYMBOL_BIDHIGH", "SYMBOL_BIDLOW", "SYMBOL_ASK", "SYMBOL_ASKHIGH",
            "SYMBOL_ASKLOW", "SYMBOL_LAST", "SYMBOL_LASTHIGH", "SYMBOL_LASTLOW", "SYMBOL_VOLUME_REAL",
            "SYMBOL_VOLUMEHIGH_REAL", "SYMBOL_VOLUMELOW_REAL", "SYMBOL_OPTION_STRIKE", "SYMBOL_POINT",
            "SYMBOL_TRADE_TICK_VALUE", "SYMBOL_TRADE_TICK_VALUE_PROFIT", "SYMBOL_TRADE_TICK_VALUE_LOSS",
            "SYMBOL_TRADE_TICK_SIZE", "SYMBOL_TRADE_CONTRACT_SIZE", "SYMBOL_TRADE_ACCRUED_INTEREST",
            "SYMBOL_TRADE_FACE_VALUE", "SYMBOL_TRADE_LIQUIDITY_RATE", "SYMBOL_VOLUME_MIN",
            "SYMBOL_VOLUME_MAX", "SYMBOL_VOLUME_STEP", "SYMBOL_VOLUME_LIMIT", "SYMBOL_SWAP_LONG",
            "SYMBOL_SWAP_SHORT", "SYMBOL_MARGIN_INITIAL", "SYMBOL_MARGIN_MAINTENANCE",
            "SYMBOL_SESSION_VOLUME", "SYMBOL_SESSION_TURNOVER", "SYMBOL_SESSION_INTEREST",
            "SYMBOL_SESSION_BUY_ORDERS_VOLUME", "SYMBOL_SESSION_SELL_ORDERS_VOLUME",
            "SYMBOL_SESSION_OPEN", "SYMBOL_SESSION_CLOSE", "SYMBOL_SESSION_AW",
            "SYMBOL_SESSION_PRICE_SETTLEMENT", "SYMBOL_SESSION_PRICE_LIMIT_MIN",
            "SYMBOL_SESSION_PRICE_LIMIT_MAX", "SYMBOL_MARGIN_HEDGED", "SYMBOL_PRICE_CHANGE",
            "SYMBOL_PRICE_VOLATILITY", "SYMBOL_PRICE_THEORETICAL", "SYMBOL_PRICE_DELTA",
            "SYMBOL_PRICE_THETA", "SYMBOL_PRICE_GAMMA", "SYMBOL_PRICE_VEGA", "SYMBOL_PRICE_RHO",
            "SYMBOL_PRICE_OMEGA", "SYMBOL_PRICE_SENSITIVITY"),
        .. Named("ENUM_SYMBOL_INFO_STRING",
            "SYMBOL_BASIS", "SYMBOL_CATEGORY", "SYMBOL_COUNTRY", "SYMBOL_SECTOR_NAME",
            "SYMBOL_INDUSTRY_NAME", "SYMBOL_CURRENCY_BASE", "SYMBOL_CURRENCY_PROFIT",
            "SYMBOL_CURRENCY_MARGIN", "SYMBOL_BANK", "SYMBOL_DESCRIPTION", "SYMBOL_EXCHANGE",
            "SYMBOL_FORMULA", "SYMBOL_ISIN", "SYMBOL_PAGE", "SYMBOL_PATH"),

        .. Named("ENUM_MA_METHOD", "MODE_SMA", "MODE_EMA", "MODE_SMMA", "MODE_LWMA"),
        .. Named("ENUM_APPLIED_PRICE",
            "PRICE_CLOSE", "PRICE_OPEN", "PRICE_HIGH", "PRICE_LOW", "PRICE_MEDIAN",
            "PRICE_TYPICAL", "PRICE_WEIGHTED"),
        .. Named("ENUM_APPLIED_VOLUME", "VOLUME_TICK", "VOLUME_REAL"),
        .. Named("ENUM_STO_PRICE", "STO_LOWHIGH", "STO_CLOSECLOSE"),

        .. Named("ENUM_OBJECT",
            "OBJ_VLINE", "OBJ_HLINE", "OBJ_TREND", "OBJ_TRENDBYANGLE", "OBJ_CYCLES", "OBJ_ARROWED_LINE",
            "OBJ_CHANNEL", "OBJ_STDDEVCHANNEL", "OBJ_REGRESSION", "OBJ_PITCHFORK", "OBJ_GANNLINE",
            "OBJ_GANNFAN", "OBJ_GANNGRID", "OBJ_FIBO", "OBJ_FIBOTIMES", "OBJ_FIBOFAN", "OBJ_FIBOARC",
            "OBJ_FIBOCHANNEL", "OBJ_EXPANSION", "OBJ_ELLIOTWAVE5", "OBJ_ELLIOTWAVE3", "OBJ_RECTANGLE",
            "OBJ_TRIANGLE", "OBJ_ELLIPSE", "OBJ_ARROW_THUMB_UP", "OBJ_ARROW_THUMB_DOWN", "OBJ_ARROW_UP",
            "OBJ_ARROW_DOWN", "OBJ_ARROW_STOP", "OBJ_ARROW_CHECK", "OBJ_ARROW_LEFT_PRICE",
            "OBJ_ARROW_RIGHT_PRICE", "OBJ_ARROW_BUY", "OBJ_ARROW_SELL", "OBJ_ARROW", "OBJ_TEXT",
            "OBJ_LABEL", "OBJ_BUTTON", "OBJ_CHART", "OBJ_BITMAP", "OBJ_BITMAP_LABEL", "OBJ_EDIT",
            "OBJ_EVENT", "OBJ_RECTANGLE_LABEL"),
        .. Named("ENUM_OBJECT_PROPERTY_INTEGER",
            "OBJPROP_COLOR", "OBJPROP_STYLE", "OBJPROP_WIDTH", "OBJPROP_BACK", "OBJPROP_ZORDER",
            "OBJPROP_FILL", "OBJPROP_HIDDEN", "OBJPROP_SELECTED", "OBJPROP_READONLY", "OBJPROP_TYPE",
            "OBJPROP_TIME", "OBJPROP_SELECTABLE", "OBJPROP_CREATETIME", "OBJPROP_LEVELS",
            "OBJPROP_LEVELCOLOR", "OBJPROP_LEVELSTYLE", "OBJPROP_LEVELWIDTH", "OBJPROP_ALIGN",
            "OBJPROP_FONTSIZE", "OBJPROP_RAY_LEFT", "OBJPROP_RAY_RIGHT", "OBJPROP_RAY", "OBJPROP_ELLIPSE",
            "OBJPROP_ARROWCODE", "OBJPROP_TIMEFRAMES", "OBJPROP_ANCHOR", "OBJPROP_XDISTANCE",
            "OBJPROP_YDISTANCE", "OBJPROP_DIRECTION", "OBJPROP_DEGREE", "OBJPROP_DRAWLINES",
            "OBJPROP_STATE", "OBJPROP_CHART_ID", "OBJPROP_XSIZE", "OBJPROP_YSIZE", "OBJPROP_XOFFSET",
            "OBJPROP_YOFFSET", "OBJPROP_PERIOD", "OBJPROP_DATE_SCALE", "OBJPROP_PRICE_SCALE",
            "OBJPROP_CHART_SCALE", "OBJPROP_BGCOLOR", "OBJPROP_CORNER", "OBJPROP_BORDER_TYPE",
            "OBJPROP_BORDER_COLOR"),
        .. Named("ENUM_OBJECT_PROPERTY_DOUBLE",
            "OBJPROP_PRICE", "OBJPROP_LEVELVALUE", "OBJPROP_SCALE", "OBJPROP_ANGLE", "OBJPROP_DEVIATION"),
        .. Named("ENUM_OBJECT_PROPERTY_STRING",
            "OBJPROP_NAME", "OBJPROP_TEXT", "OBJPROP_TOOLTIP", "OBJPROP_LEVELTEXT", "OBJPROP_FONT",
            "OBJPROP_BMPFILE", "OBJPROP_SYMBOL"),
        .. Named("ENUM_ANCHOR_POINT",
            "ANCHOR_LEFT_UPPER", "ANCHOR_LEFT", "ANCHOR_LEFT_LOWER", "ANCHOR_LOWER",
            "ANCHOR_RIGHT_LOWER", "ANCHOR_RIGHT", "ANCHOR_RIGHT_UPPER", "ANCHOR_UPPER", "ANCHOR_CENTER"),
        .. Named("ENUM_ARROW_ANCHOR", "ANCHOR_TOP", "ANCHOR_BOTTOM"),
        .. Named("ENUM_BORDER_TYPE", "BORDER_FLAT", "BORDER_RAISED", "BORDER_SUNKEN"),
        .. Named("ENUM_ALIGN_MODE", "ALIGN_LEFT", "ALIGN_CENTER", "ALIGN_RIGHT"),
        .. Named("ENUM_BASE_CORNER",
            "CORNER_LEFT_UPPER", "CORNER_LEFT_LOWER", "CORNER_RIGHT_LOWER", "CORNER_RIGHT_UPPER"),

        .. Named("ENUM_CHART_PROPERTY_INTEGER",
            "CHART_SHOW", "CHART_IS_OBJECT", "CHART_BRING_TO_TOP", "CHART_CONTEXT_MENU",
            "CHART_CROSSHAIR_TOOL", "CHART_MOUSE_SCROLL", "CHART_EVENT_MOUSE_WHEEL",
            "CHART_EVENT_MOUSE_MOVE", "CHART_EVENT_OBJECT_CREATE", "CHART_EVENT_OBJECT_DELETE",
            "CHART_MODE", "CHART_FOREGROUND", "CHART_SHIFT", "CHART_AUTOSCROLL",
            "CHART_KEYBOARD_CONTROL", "CHART_QUICK_NAVIGATION", "CHART_SCALE", "CHART_SCALEFIX",
            "CHART_SCALEFIX_11", "CHART_SCALE_PT_PER_BAR", "CHART_SHOW_TICKER", "CHART_SHOW_OHLC",
            "CHART_SHOW_BID_LINE", "CHART_SHOW_ASK_LINE", "CHART_SHOW_LAST_LINE",
            "CHART_SHOW_PERIOD_SEP", "CHART_SHOW_GRID", "CHART_SHOW_VOLUMES", "CHART_SHOW_OBJECT_DESCR",
            "CHART_SHOW_TRADE_HISTORY", "CHART_VISIBLE_BARS", "CHART_WINDOWS_TOTAL",
            "CHART_WINDOW_IS_VISIBLE", "CHART_WINDOW_HANDLE", "CHART_WINDOW_YDISTANCE",
            "CHART_FIRST_VISIBLE_BAR", "CHART_WIDTH_IN_BARS", "CHART_WIDTH_IN_PIXELS",
            "CHART_HEIGHT_IN_PIXELS", "CHART_COLOR_BACKGROUND", "CHART_COLOR_FOREGROUND",
            "CHART_COLOR_GRID", "CHART_COLOR_VOLUME", "CHART_COLOR_CHART_UP", "CHART_COLOR_CHART_DOWN",
            "CHART_COLOR_CHART_LINE", "CHART_COLOR_CANDLE_BULL", "CHART_COLOR_CANDLE_BEAR",
            "CHART_COLOR_BID", "CHART_COLOR_ASK", "CHART_COLOR_LAST", "CHART_COLOR_STOP_LEVEL",
            "CHART_SHOW_TRADE_LEVELS", "CHART_DRAG_TRADE_LEVELS", "CHART_SHOW_DATE_SCALE",
            "CHART_SHOW_PRICE_SCALE", "CHART_SHOW_ONE_CLICK", "CHART_IS_MAXIMIZED",
            "CHART_IS_MINIMIZED", "CHART_IS_DOCKED", "CHART_FLOAT_LEFT", "CHART_FLOAT_TOP",
            "CHART_FLOAT_RIGHT", "CHART_FLOAT_BOTTOM"),
        .. Named("ENUM_CHART_PROPERTY_DOUBLE",
            "CHART_SHIFT_SIZE", "CHART_FIXED_POSITION", "CHART_FIXED_MAX", "CHART_FIXED_MIN",
            "CHART_POINTS_PER_BAR", "CHART_PRICE_MIN", "CHART_PRICE_MAX"),
        .. Named("ENUM_CHART_PROPERTY_STRING",
            "CHART_COMMENT", "CHART_EXPERT_NAME", "CHART_SCRIPT_NAME")
    ];

    private static Mql5BuiltinConstant[] Valued(string enumName, params (string Name, long Value)[] members)
        => [.. members.Select(member => new Mql5BuiltinConstant(member.Name, member.Value, enumName))];

    /// <summary>
    /// Members MetaQuotes documents by name only. They carry a null value on
    /// purpose: the reference publishes no numbers for these enumerations, and their
    /// ordinals are not safely guessable.
    /// </summary>
    private static Mql5BuiltinConstant[] Named(string enumName, params string[] members)
        => [.. members.Select(member => new Mql5BuiltinConstant(member, null, enumName))];
}
