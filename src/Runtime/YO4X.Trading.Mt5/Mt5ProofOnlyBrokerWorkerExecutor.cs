using YO4X.Trading.Abstractions;
using YO4X.Trading.ProcessIsolation;

namespace YO4X.Trading.Mt5;

/// <summary>
/// Child-process composition for the current U0 gate. It deliberately performs no
/// vendor call and keeps every mutation disabled until the separate trust gates pass.
/// </summary>
public sealed class Mt5ProofOnlyBrokerWorkerExecutor : IBrokerWorkerExecutor
{
    public Task<GatewaySendResult> SendAsync(
        BrokerWorkerSendRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            new GatewaySendResult(
                GatewayCommandDisposition.SubmissionDisabled,
                Mt5ProofOnlyGateway.ProofOnlyCode,
                null,
                null,
                null,
                request.Command.CreatedAtUtc.ToUniversalTime(),
                true));
    }

    public Task<GatewayOperationResult<BrokerReconciliationSnapshot>> ReconcileAsync(
        BrokerWorkerReconcileRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            new GatewayOperationResult<BrokerReconciliationSnapshot>(
                false,
                Mt5ProofOnlyGateway.ProofOnlyCode,
                null));
    }
}
