namespace YO4X.Mql5.Runtime;

/// <summary>
/// MQL5 <c>MqlTradeRequest</c>, field-for-field and in MQL5 declaration order.
///
/// MQL5 has no <c>OrderClose</c>, <c>OrderModify</c> or <c>OrderDelete</c>: every
/// state change - opening, closing, moving a stop, deleting a pending order - is one
/// of these filled in and handed to <c>OrderSend</c>. The MQL5 spelling of each field
/// is recorded on its documentation comment so a code generator can map the two
/// names without guessing.
///
/// Property ids such as <see cref="Action"/> and <see cref="Type"/> are plain
/// integers rather than CLR enumerations because MetaQuotes does not publish the
/// numeric values of <c>ENUM_TRADE_REQUEST_ACTIONS</c> or <c>ENUM_ORDER_TYPE</c>.
/// Inventing ordinals here would mis-bind silently; the numbers have to come from the
/// terminal the engine is bound to.
/// </summary>
public sealed class Mql5TradeRequest
{
    /// <summary>MQL5 <c>action</c>. An <c>ENUM_TRADE_REQUEST_ACTIONS</c> member.</summary>
    public int Action { get; set; }

    /// <summary>MQL5 <c>magic</c>. The expert advisor identifier stamped on the order.</summary>
    public ulong Magic { get; set; }

    /// <summary>MQL5 <c>order</c>. Ticket of the order being modified or removed.</summary>
    public ulong Order { get; set; }

    /// <summary>MQL5 <c>symbol</c>.</summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>MQL5 <c>volume</c>, in lots.</summary>
    public double Volume { get; set; }

    /// <summary>MQL5 <c>price</c>.</summary>
    public double Price { get; set; }

    /// <summary>MQL5 <c>stoplimit</c>. The limit price of a stop-limit order.</summary>
    public double StopLimit { get; set; }

    /// <summary>MQL5 <c>sl</c>. Stop loss price.</summary>
    public double StopLoss { get; set; }

    /// <summary>MQL5 <c>tp</c>. Take profit price.</summary>
    public double TakeProfit { get; set; }

    /// <summary>MQL5 <c>deviation</c>. Maximum acceptable slippage, in points.</summary>
    public ulong Deviation { get; set; }

    /// <summary>MQL5 <c>type</c>. An <c>ENUM_ORDER_TYPE</c> member.</summary>
    public int Type { get; set; }

    /// <summary>MQL5 <c>type_filling</c>. An <c>ENUM_ORDER_TYPE_FILLING</c> member.</summary>
    public int TypeFilling { get; set; }

    /// <summary>MQL5 <c>type_time</c>. An <c>ENUM_ORDER_TYPE_TIME</c> member.</summary>
    public int TypeTime { get; set; }

    /// <summary>MQL5 <c>expiration</c>. Seconds since 1970-01-01 UTC.</summary>
    public long Expiration { get; set; }

    /// <summary>MQL5 <c>comment</c>.</summary>
    public string Comment { get; set; } = string.Empty;

    /// <summary>MQL5 <c>position</c>. Ticket of the position being acted on.</summary>
    public ulong Position { get; set; }

    /// <summary>MQL5 <c>position_by</c>. Ticket of the opposite position in a close-by.</summary>
    public ulong PositionBy { get; set; }

    /// <summary>Resets every field to the value a freshly zeroed MQL5 structure holds.</summary>
    public void Clear()
    {
        Action = 0;
        Magic = 0;
        Order = 0;
        Symbol = string.Empty;
        Volume = 0;
        Price = 0;
        StopLimit = 0;
        StopLoss = 0;
        TakeProfit = 0;
        Deviation = 0;
        Type = 0;
        TypeFilling = 0;
        TypeTime = 0;
        Expiration = 0;
        Comment = string.Empty;
        Position = 0;
        PositionBy = 0;
    }
}

/// <summary>
/// MQL5 <c>MqlTradeResult</c>, field-for-field and in MQL5 declaration order.
///
/// <see cref="Retcode"/> is the field that decides whether the request succeeded;
/// <c>TRADE_RETCODE_DONE</c> is 10009 and <c>TRADE_RETCODE_PLACED</c> is 10008.
/// See <see cref="Mql5Constants.TradeRetcode"/>.
/// </summary>
public sealed class Mql5TradeResult
{
    /// <summary>MQL5 <c>retcode</c>. An <c>ENUM_TRADE_RETURN_CODES</c> member.</summary>
    public uint Retcode { get; set; }

    /// <summary>MQL5 <c>deal</c>. Ticket of the deal the request produced.</summary>
    public ulong Deal { get; set; }

    /// <summary>MQL5 <c>order</c>. Ticket of the order the request produced.</summary>
    public ulong Order { get; set; }

    /// <summary>MQL5 <c>volume</c>. Volume actually dealt.</summary>
    public double Volume { get; set; }

    /// <summary>MQL5 <c>price</c>. Price actually dealt.</summary>
    public double Price { get; set; }

    /// <summary>MQL5 <c>bid</c>. Current bid at the moment of the reply.</summary>
    public double Bid { get; set; }

    /// <summary>MQL5 <c>ask</c>. Current ask at the moment of the reply.</summary>
    public double Ask { get; set; }

    /// <summary>MQL5 <c>comment</c>. Broker commentary on the outcome.</summary>
    public string Comment { get; set; } = string.Empty;

    /// <summary>MQL5 <c>request_id</c>.</summary>
    public uint RequestId { get; set; }

    /// <summary>MQL5 <c>retcode_external</c>. The external trading system reply code.</summary>
    public int RetcodeExternal { get; set; }

    /// <summary>Resets every field to the value a freshly zeroed MQL5 structure holds.</summary>
    public void Clear()
    {
        Retcode = 0;
        Deal = 0;
        Order = 0;
        Volume = 0;
        Price = 0;
        Bid = 0;
        Ask = 0;
        Comment = string.Empty;
        RequestId = 0;
        RetcodeExternal = 0;
    }
}

/// <summary>MQL5 <c>MqlTradeCheckResult</c>, field-for-field, as filled by <c>OrderCheck</c>.</summary>
public sealed class Mql5TradeCheckResult
{
    /// <summary>MQL5 <c>retcode</c>.</summary>
    public uint Retcode { get; set; }

    /// <summary>MQL5 <c>balance</c>. Balance the account would hold after the deal.</summary>
    public double Balance { get; set; }

    /// <summary>MQL5 <c>equity</c>. Equity the account would hold after the deal.</summary>
    public double Equity { get; set; }

    /// <summary>MQL5 <c>profit</c>. Floating profit the deal would produce.</summary>
    public double Profit { get; set; }

    /// <summary>MQL5 <c>margin</c>. Margin the deal would require.</summary>
    public double Margin { get; set; }

    /// <summary>MQL5 <c>margin_free</c>. Free margin left after the deal.</summary>
    public double MarginFree { get; set; }

    /// <summary>MQL5 <c>margin_level</c>. Margin level after the deal.</summary>
    public double MarginLevel { get; set; }

    /// <summary>MQL5 <c>comment</c>. Description of the return code.</summary>
    public string Comment { get; set; } = string.Empty;

    /// <summary>Resets every field to the value a freshly zeroed MQL5 structure holds.</summary>
    public void Clear()
    {
        Retcode = 0;
        Balance = 0;
        Equity = 0;
        Profit = 0;
        Margin = 0;
        MarginFree = 0;
        MarginLevel = 0;
        Comment = string.Empty;
    }
}
