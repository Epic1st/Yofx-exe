namespace YO4X.Trading.Abstractions;

public interface IMt5Gateway
{
    GatewayConnectionState ConnectionState { get; }

    Task<GatewayOperationResult<GatewayCapabilities>> ConnectAsync(
        GatewayConnectionRequest request,
        CancellationToken cancellationToken);

    Task<GatewayOperationResult> DisconnectAsync(CancellationToken cancellationToken);

    Task<GatewayOperationResult<BrokerAccountSnapshot>> GetAccountAsync(CancellationToken cancellationToken);

    Task<GatewayOperationResult<IReadOnlyList<BrokerQuoteSnapshot>>> GetQuotesAsync(
        IReadOnlyCollection<string> symbols,
        CancellationToken cancellationToken);

    Task<GatewayOperationResult<IReadOnlyList<BrokerPositionSnapshot>>> GetPositionsAsync(
        CancellationToken cancellationToken);

    Task<GatewayOperationResult<IReadOnlyList<BrokerOrderSnapshot>>> GetOrdersAsync(
        CancellationToken cancellationToken);

    Task<GatewayOperationResult<IReadOnlyList<BrokerDealSnapshot>>> GetDealsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken);

    Task<GatewaySendResult> SendAsync(
        AuthorizedBrokerCommand command,
        CancellationToken cancellationToken);

    Task<GatewayOperationResult<BrokerReconciliationSnapshot>> ReconcileAsync(
        IReadOnlyCollection<Guid> commandIds,
        CancellationToken cancellationToken);
}
