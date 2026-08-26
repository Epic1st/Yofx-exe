namespace YO4X.Mql5.Runtime;

/// <summary>
/// MQL5 <c>MqlTradeTransaction</c>: the description of one trade-server event, as delivered to
/// <c>OnTradeTransaction</c>.
/// </summary>
/// <remarks>
/// The full structure is carried even though most strategies read only <c>deal</c>,
/// <c>type</c> and <c>deal_type</c>. A structure that omits fields the source assigns does not
/// fail at the omission — it fails at translation, naming a field the reader then has to go and
/// look up. Carrying the whole shape costs nothing and keeps that from happening.
/// </remarks>
public sealed class Mql5TradeTransaction
{
    /// <summary>MQL5 <c>deal</c>: the deal ticket.</summary>
    public ulong Deal { get; set; }

    /// <summary>MQL5 <c>order</c>: the order ticket.</summary>
    public ulong Order { get; set; }

    /// <summary>MQL5 <c>symbol</c>.</summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>MQL5 <c>type</c>: an <c>ENUM_TRADE_TRANSACTION_TYPE</c> member.</summary>
    public int Type { get; set; }

    /// <summary>MQL5 <c>order_type</c>.</summary>
    public int OrderType { get; set; }

    /// <summary>MQL5 <c>order_state</c>.</summary>
    public int OrderState { get; set; }

    /// <summary>MQL5 <c>deal_type</c>.</summary>
    public int DealType { get; set; }

    /// <summary>MQL5 <c>time_type</c>.</summary>
    public int TimeType { get; set; }

    /// <summary>MQL5 <c>time_expiration</c>, as seconds since 1970.</summary>
    public long TimeExpiration { get; set; }

    /// <summary>MQL5 <c>price</c>.</summary>
    public double Price { get; set; }

    /// <summary>MQL5 <c>price_trigger</c>.</summary>
    public double PriceTrigger { get; set; }

    /// <summary>MQL5 <c>price_sl</c>.</summary>
    public double PriceSl { get; set; }

    /// <summary>MQL5 <c>price_tp</c>.</summary>
    public double PriceTp { get; set; }

    /// <summary>MQL5 <c>volume</c>.</summary>
    public double Volume { get; set; }

    /// <summary>MQL5 <c>position</c>.</summary>
    public ulong Position { get; set; }

    /// <summary>MQL5 <c>position_by</c>.</summary>
    public ulong PositionBy { get; set; }
}
