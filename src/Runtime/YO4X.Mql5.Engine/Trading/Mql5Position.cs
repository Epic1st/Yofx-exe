namespace YO4X.Mql5.Engine.Trading;

/// <summary>An open simulated position.</summary>
public sealed class Mql5Position
{
    /// <summary>Gets the position ticket.</summary>
    public long Ticket { get; internal set; }

    /// <summary>Gets the symbol.</summary>
    public string Symbol { get; internal set; } = string.Empty;

    /// <summary>Gets the direction.</summary>
    public Mql5PositionType Type { get; internal set; }

    /// <summary>Gets the open volume in lots.</summary>
    public double Volume { get; internal set; }

    /// <summary>Gets the volume-weighted open price.</summary>
    public double PriceOpen { get; internal set; }

    /// <summary>Gets the stop loss price, or zero when unset.</summary>
    public double StopLoss { get; internal set; }

    /// <summary>Gets the take profit price, or zero when unset.</summary>
    public double TakeProfit { get; internal set; }

    /// <summary>Gets the open time, taken from the bar series and never from the wall clock.</summary>
    public DateTime TimeOpen { get; internal set; }

    /// <summary>Gets the expert advisor magic number.</summary>
    public long Magic { get; internal set; }

    /// <summary>Gets the order comment.</summary>
    public string Comment { get; internal set; } = string.Empty;

    /// <summary>Gets the accrued commission, always negative or zero.</summary>
    public double Commission { get; internal set; }

    /// <summary>Gets the accrued swap.</summary>
    public double Swap { get; internal set; }

    /// <summary>Gets the price the position would close at right now.</summary>
    public double PriceCurrent { get; internal set; }

    /// <summary>Gets the floating profit excluding commission and swap.</summary>
    public double Profit { get; internal set; }

    /// <summary>Gets the margin currently locked by the position.</summary>
    public double Margin { get; internal set; }

    internal Mql5Position Clone() => (Mql5Position)MemberwiseClone();
}
