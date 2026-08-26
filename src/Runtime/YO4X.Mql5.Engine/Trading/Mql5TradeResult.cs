namespace YO4X.Mql5.Engine.Trading;

/// <summary>The managed shape of <c>MqlTradeResult</c>.</summary>
public sealed class Mql5TradeResult
{
    /// <summary>Gets or sets the server return code. See <see cref="Mql5TradeRetcode"/>.</summary>
    public int Retcode { get; set; } = Mql5TradeRetcode.Error;

    /// <summary>Gets or sets the deal ticket produced by the request.</summary>
    public long Deal { get; set; }

    /// <summary>Gets or sets the order ticket produced by the request.</summary>
    public long Order { get; set; }

    /// <summary>Gets or sets the position ticket affected by the request.</summary>
    public long Position { get; set; }

    /// <summary>Gets or sets the volume actually executed.</summary>
    public double Volume { get; set; }

    /// <summary>Gets or sets the price actually executed.</summary>
    public double Price { get; set; }

    /// <summary>Gets or sets the bid at the moment of execution.</summary>
    public double Bid { get; set; }

    /// <summary>Gets or sets the ask at the moment of execution.</summary>
    public double Ask { get; set; }

    /// <summary>Gets or sets a human readable comment describing the outcome.</summary>
    public string Comment { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether the request completed.</summary>
    public bool Succeeded => Retcode is Mql5TradeRetcode.Done or Mql5TradeRetcode.DonePartial;
}
