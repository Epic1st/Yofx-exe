namespace YO4X.Mt5.ConnectionProbe.Windows;

/// <summary>
/// The broker boundary exposed to a translated strategy. Implementations must authorize every
/// mutating member before entering a vendor API; the raw vendor client is only one possible
/// implementation and must not be handed directly to untrusted strategy code.
/// </summary>
public interface IMt5TradeGateway
{
    string Symbol { get; }

    Action<DateTime, double, double>? QuoteObserver { get; set; }

    Mt5LiveAccountSnapshot ReadAccountSnapshot();

    Mt5LiveSymbolSnapshot? ReadSymbolSnapshot() => null;

    Task<Mt5DemoOrderReceipt> SendAsync(
        Mt5DemoSide side,
        double volume,
        double price,
        double stopLoss,
        double takeProfit,
        string comment,
        CancellationToken cancellationToken = default);

    Task<Mt5ExecutionLatency> ModifyAsync(
        Mt5DemoOrderReceipt receipt,
        double stopLoss,
        double takeProfit,
        CancellationToken cancellationToken = default);

    Task<Mt5DemoOrderReceipt> CloseAsync(
        Mt5DemoOrderReceipt receipt,
        CancellationToken cancellationToken = default);

    Task<Mt5ExecutionLatency> CancelAsync(
        Mt5DemoOrderReceipt receipt,
        CancellationToken cancellationToken = default);
}
