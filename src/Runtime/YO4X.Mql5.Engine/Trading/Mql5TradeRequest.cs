namespace YO4X.Mql5.Engine.Trading;

/// <summary>
/// The managed shape of <c>MqlTradeRequest</c>. Mutable because generated MQL5 translations fill it
/// field by field exactly as the source EA does.
/// </summary>
public sealed class Mql5TradeRequest
{
    /// <summary>Gets or sets the trade operation type.</summary>
    public Mql5TradeAction Action { get; set; } = Mql5TradeAction.Deal;

    /// <summary>Gets or sets the expert advisor magic number.</summary>
    public long Magic { get; set; }

    /// <summary>Gets or sets the pending order ticket the request targets.</summary>
    public long Order { get; set; }

    /// <summary>Gets or sets the symbol.</summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>Gets or sets the requested volume in lots.</summary>
    public double Volume { get; set; }

    /// <summary>Gets or sets the requested price. Ignored for market deals.</summary>
    public double Price { get; set; }

    /// <summary>Gets or sets the stop limit price for stop-limit orders.</summary>
    public double StopLimit { get; set; }

    /// <summary>Gets or sets the stop loss price. Zero clears it.</summary>
    public double Sl { get; set; }

    /// <summary>Gets or sets the take profit price. Zero clears it.</summary>
    public double Tp { get; set; }

    /// <summary>Gets or sets the maximum acceptable slippage in points.</summary>
    public long Deviation { get; set; }

    /// <summary>Gets or sets the order type.</summary>
    public Mql5OrderType Type { get; set; }

    /// <summary>Gets or sets the position ticket the request targets, for close and modify.</summary>
    public long Position { get; set; }

    /// <summary>Gets or sets the free-form order comment.</summary>
    public string Comment { get; set; } = string.Empty;

    /// <summary>Creates a copy so the broker never retains a reference the strategy can mutate.</summary>
    internal Mql5TradeRequest Clone() => new()
    {
        Action = Action,
        Magic = Magic,
        Order = Order,
        Symbol = Symbol,
        Volume = Volume,
        Price = Price,
        StopLimit = StopLimit,
        Sl = Sl,
        Tp = Tp,
        Deviation = Deviation,
        Type = Type,
        Position = Position,
        Comment = Comment,
    };
}
