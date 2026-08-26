using System.Globalization;

namespace YO4X.Mql5.Engine.Trading;

/// <summary>
/// One entry in the run journal. Every order request, fill, stop hit and cap is recorded with the
/// bar timestamp it happened on.
/// </summary>
public sealed record Mql5OrderEvent
{
    /// <summary>Gets the simulated time of the event.</summary>
    public required DateTime Time { get; init; }

    /// <summary>Gets the event classification.</summary>
    public required Mql5OrderEventKind Kind { get; init; }

    /// <summary>Gets the position or order ticket involved, or zero.</summary>
    public long Ticket { get; init; }

    /// <summary>Gets the symbol, when the event concerns one.</summary>
    public string Symbol { get; init; } = string.Empty;

    /// <summary>Gets the order or position direction, when meaningful.</summary>
    public Mql5OrderType? Type { get; init; }

    /// <summary>Gets the volume involved.</summary>
    public double Volume { get; init; }

    /// <summary>Gets the price involved.</summary>
    public double Price { get; init; }

    /// <summary>Gets the realized profit, for close events.</summary>
    public double Profit { get; init; }

    /// <summary>Gets the balance after the event.</summary>
    public double Balance { get; init; }

    /// <summary>Gets the MQL5 retcode associated with the event.</summary>
    public int Retcode { get; init; }

    /// <summary>Gets a short description of the event.</summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>Renders the event as a stable single line, useful in test assertions.</summary>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Time:yyyy-MM-dd HH:mm} {Kind} #{Ticket} {Symbol} {Type} vol={Volume} price={Price} profit={Profit} rc={Retcode} {Detail}");
}
