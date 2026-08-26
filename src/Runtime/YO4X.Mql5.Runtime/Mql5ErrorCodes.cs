namespace YO4X.Mql5.Runtime;

/// <summary>
/// The MQL5 runtime error codes this library sets on <c>GetLastError</c>.
///
/// MQL5 built-ins do not raise: they return a documented failure value and leave a
/// code behind for <c>GetLastError</c>. This runtime does the same, so a strategy
/// written against MetaTrader's error-checking idiom keeps working. Only the codes the
/// implemented surface can actually produce are listed; the numbers are the ones
/// MetaQuotes publishes under <c>/docs/constants/errorswarnings/errorcodes</c>.
/// </summary>
public static class Mql5ErrorCodes
{
    /// <summary><c>ERR_SUCCESS</c>: the operation completed.</summary>
    public const int Success = 0;

    /// <summary><c>ERR_INTERNAL_ERROR</c>.</summary>
    public const int InternalError = 4001;

    /// <summary><c>ERR_INVALID_PARAMETER</c>: a built-in was handed an argument it cannot use.</summary>
    public const int InvalidParameter = 4003;

    /// <summary><c>ERR_NOT_ENOUGH_MEMORY</c>.</summary>
    public const int NotEnoughMemory = 4004;

    /// <summary><c>ERR_STRUCT_WITHOBJECTS_ORCLASS</c>.</summary>
    public const int StructWithObjectsOrClass = 4005;

    /// <summary><c>ERR_INVALID_ARRAY</c>: the array is of the wrong type or unallocated.</summary>
    public const int InvalidArray = 4006;

    /// <summary><c>ERR_ARRAY_RESIZE_ERROR</c>: not enough memory to resize a dynamic array.</summary>
    public const int ArrayResizeError = 4007;

    /// <summary><c>ERR_STRING_RESIZE_ERROR</c>.</summary>
    public const int StringResizeError = 4008;

    /// <summary><c>ERR_NOTINITIALIZED_STRING</c>.</summary>
    public const int NotInitializedString = 4009;

    /// <summary><c>ERR_INVALID_DATETIME</c>.</summary>
    public const int InvalidDatetime = 4010;

    /// <summary><c>ERR_ARRAY_BAD_SIZE</c>.</summary>
    public const int ArrayBadSize = 4011;

    /// <summary><c>ERR_INVALID_POINTER</c>.</summary>
    public const int InvalidPointer = 4012;

    /// <summary><c>ERR_STRING_SMALL_LEN</c>: a string index was outside the string.</summary>
    public const int StringSmallLength = 5035;

    /// <summary><c>ERR_GLOBALVARIABLE_NOT_FOUND</c>: no global variable of that name exists.</summary>
    public const int GlobalVariableNotFound = 4501;

    /// <summary><c>ERR_GLOBALVARIABLE_EXISTS</c>: a global variable of that name already exists.</summary>
    public const int GlobalVariableExists = 4502;

    /// <summary><c>ERR_GLOBALVARIABLE_NOT_MODIFIED</c>: the global variable was left unchanged.</summary>
    public const int GlobalVariableNotModified = 4503;

    /// <summary><c>ERR_MARKET_UNKNOWN_SYMBOL</c>.</summary>
    public const int MarketUnknownSymbol = 4301;

    /// <summary><c>ERR_MARKET_NOT_SELECTED</c>.</summary>
    public const int MarketNotSelected = 4302;

    /// <summary><c>ERR_OBJECT_ERROR</c>: a graphical-object operation failed.</summary>
    public const int ObjectError = 4101;

    /// <summary><c>ERR_OBJECT_NOT_FOUND</c>.</summary>
    public const int ObjectNotFound = 4102;

    /// <summary><c>ERR_INDICATOR_CANNOT_CREATE</c>.</summary>
    public const int IndicatorCannotCreate = 4801;

    /// <summary><c>ERR_INDICATOR_DATA_NOT_FOUND</c>.</summary>
    public const int IndicatorDataNotFound = 4806;

    /// <summary><c>ERR_TRADE_SEND_FAILED</c>.</summary>
    public const int TradeSendFailed = 4756;

    /// <summary><c>ERR_TRADE_POSITION_NOT_FOUND</c>.</summary>
    public const int TradePositionNotFound = 4753;

    /// <summary><c>ERR_TRADE_ORDER_NOT_FOUND</c>.</summary>
    public const int TradeOrderNotFound = 4754;

    /// <summary><c>ERR_TRADE_DEAL_NOT_FOUND</c>.</summary>
    public const int TradeDealNotFound = 4755;
}
