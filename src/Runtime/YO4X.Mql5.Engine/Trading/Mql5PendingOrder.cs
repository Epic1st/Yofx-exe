namespace YO4X.Mql5.Engine.Trading;

/// <summary>A resting pending order awaiting activation.</summary>
public sealed class Mql5PendingOrder
{
    /// <summary>Gets the order ticket.</summary>
    public long Ticket { get; internal set; }

    /// <summary>Gets the symbol.</summary>
    public string Symbol { get; internal set; } = string.Empty;

    /// <summary>Gets the pending order type.</summary>
    public Mql5OrderType Type { get; internal set; }

    /// <summary>Gets the volume in lots.</summary>
    public double Volume { get; internal set; }

    /// <summary>Gets the activation price.</summary>
    public double Price { get; internal set; }

    /// <summary>Gets the stop loss to attach on activation, or zero.</summary>
    public double StopLoss { get; internal set; }

    /// <summary>Gets the take profit to attach on activation, or zero.</summary>
    public double TakeProfit { get; internal set; }

    /// <summary>Gets the time the order was placed.</summary>
    public DateTime TimeSetup { get; internal set; }

    /// <summary>Gets the expert advisor magic number.</summary>
    public long Magic { get; internal set; }

    /// <summary>Gets the order comment.</summary>
    public string Comment { get; internal set; } = string.Empty;

    /// <summary>Gets a value indicating whether activation produces a long position.</summary>
    public bool IsBuySide => Type is Mql5OrderType.BuyLimit or Mql5OrderType.BuyStop or Mql5OrderType.Buy;

    internal Mql5PendingOrder Clone() => (Mql5PendingOrder)MemberwiseClone();
}
