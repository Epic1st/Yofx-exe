using YO4X.Trading.Abstractions;

namespace YO4X.Trading.Mt5;

/// <summary>
/// Safe U0 adapter boundary. It intentionally performs no vendor calls and cannot submit an order.
/// The production adapter remains gated by gateway rights, artifact, network, and demo proofs.
/// </summary>
public sealed class Mt5ProofOnlyGateway : IMt5Gateway
{
    public const string ProofOnlyCode = "mt5_gateway_u0_proof_only";

    public GatewayConnectionState ConnectionState => GatewayConnectionState.Suspended;

    public Task<GatewayOperationResult<GatewayCapabilities>> ConnectAsync(
        GatewayConnectionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new GatewayOperationResult<GatewayCapabilities>(false, ProofOnlyCode, null));
    }

    public Task<GatewayOperationResult> DisconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(GatewayOperationResult.Success("mt5_gateway_already_disconnected"));
    }

    public Task<GatewayOperationResult<BrokerAccountSnapshot>> GetAccountAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new GatewayOperationResult<BrokerAccountSnapshot>(false, ProofOnlyCode, null));
    }

    public Task<GatewayOperationResult<IReadOnlyList<BrokerQuoteSnapshot>>> GetQuotesAsync(
        IReadOnlyCollection<string> symbols,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        cancellationToken.ThrowIfCancellationRequested();
        return EmptyFailure<BrokerQuoteSnapshot>();
    }

    public Task<GatewayOperationResult<IReadOnlyList<BrokerPositionSnapshot>>> GetPositionsAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return EmptyFailure<BrokerPositionSnapshot>();
    }

    public Task<GatewayOperationResult<IReadOnlyList<BrokerOrderSnapshot>>> GetOrdersAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return EmptyFailure<BrokerOrderSnapshot>();
    }

    public Task<GatewayOperationResult<IReadOnlyList<BrokerDealSnapshot>>> GetDealsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fromUtc, toUtc);

        return EmptyFailure<BrokerDealSnapshot>();
    }

    public Task<GatewaySendResult> SendAsync(
        AuthorizedBrokerCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            new GatewaySendResult(
                GatewayCommandDisposition.SubmissionDisabled,
                ProofOnlyCode,
                null,
                null,
                null,
                command.Command.CreatedAtUtc.ToUniversalTime(),
                false));
    }

    public Task<GatewayOperationResult<BrokerReconciliationSnapshot>> ReconcileAsync(
        IReadOnlyCollection<Guid> commandIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commandIds);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            new GatewayOperationResult<BrokerReconciliationSnapshot>(false, ProofOnlyCode, null));
    }

    private static Task<GatewayOperationResult<IReadOnlyList<T>>> EmptyFailure<T>() =>
        Task.FromResult(
            new GatewayOperationResult<IReadOnlyList<T>>(
                false,
                ProofOnlyCode,
                Array.Empty<T>()));
}
