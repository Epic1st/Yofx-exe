namespace YO4X.Mql5.Engine.Trading;

/// <summary>A completed round trip, or one partial close of a position.</summary>
public sealed record Mql5ClosedTrade
{
    /// <summary>Gets the ticket of the position that produced the close.</summary>
    public required long Ticket { get; init; }

    /// <summary>Gets the symbol.</summary>
    public required string Symbol { get; init; }

    /// <summary>Gets the direction of the closed exposure.</summary>
    public required Mql5PositionType Type { get; init; }

    /// <summary>Gets the volume closed by this record.</summary>
    public required double Volume { get; init; }

    /// <summary>Gets the open price.</summary>
    public required double PriceOpen { get; init; }

    /// <summary>Gets the close price.</summary>
    public required double PriceClose { get; init; }

    /// <summary>Gets the open time.</summary>
    public required DateTime TimeOpen { get; init; }

    /// <summary>Gets the close time.</summary>
    public required DateTime TimeClose { get; init; }

    /// <summary>Gets the gross profit before commission and swap.</summary>
    public required double GrossProfit { get; init; }

    /// <summary>Gets the commission realized by the close, always negative or zero.</summary>
    public required double Commission { get; init; }

    /// <summary>Gets the swap realized by the close.</summary>
    public required double Swap { get; init; }

    /// <summary>Gets why the exposure was closed.</summary>
    public required Mql5CloseReason Reason { get; init; }

    /// <summary>Gets the expert advisor magic number.</summary>
    public long Magic { get; init; }

    /// <summary>Gets the order comment.</summary>
    public string Comment { get; init; } = string.Empty;

    /// <summary>Gets the profit after commission and swap. This is what hits the balance.</summary>
    public double NetProfit => GrossProfit + Commission + Swap;
}
