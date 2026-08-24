using YO4X.Trading.Abstractions;

namespace YO4X.Trading.ProcessIsolation;

public static class BrokerWorkerProtocolContract
{
    public const int Version = 1;
    public const string SendOperation = "send";
    public const string ReconcileOperation = "reconcile";
}

public sealed record BrokerWorkerRequest(
    int ContractVersion,
    Guid RequestId,
    string Operation,
    DateTimeOffset DeadlineUtc,
    BrokerWorkerSendRequest? Send,
    BrokerWorkerReconcileRequest? Reconcile);

public sealed record BrokerWorkerSendRequest(
    Guid BrokerAccountId,
    Guid GatewayArtifactId,
    string GatewayArtifactSha256,
    string AuthorizationSha256,
    NormalizedBrokerCommand Command);

public sealed record BrokerWorkerReconcileRequest(IReadOnlyList<Guid> CommandIds);

public sealed record BrokerWorkerResponse(
    int ContractVersion,
    Guid RequestId,
    string Operation,
    bool IsSuccess,
    string Code,
    GatewaySendResult? SendResult,
    BrokerReconciliationSnapshot? ReconciliationSnapshot);

/// <summary>
/// Executes inside the disposable worker process. A production implementation is never
/// registered in the long-lived gateway host process.
/// </summary>
public interface IBrokerWorkerExecutor
{
    Task<GatewaySendResult> SendAsync(
        BrokerWorkerSendRequest request,
        CancellationToken cancellationToken);

    Task<GatewayOperationResult<BrokerReconciliationSnapshot>> ReconcileAsync(
        BrokerWorkerReconcileRequest request,
        CancellationToken cancellationToken);
}
