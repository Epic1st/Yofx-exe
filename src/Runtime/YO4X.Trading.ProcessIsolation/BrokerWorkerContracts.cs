using YO4X.Trading.Abstractions;

namespace YO4X.Trading.ProcessIsolation;

public static class BrokerWorkerProtocolContract
{
    public const int Version = 2;
    public const int ConnectProbeObservationVersion = 1;
    public const string SendOperation = "send";
    public const string ReconcileOperation = "reconcile";
    public const string ConnectProbeOperation = "connect_probe";
    public const string ConnectProbeSucceededCode = "mt5_connect_probe_succeeded";
    public const string ConnectProbeUnavailableCode = "mt5_connect_probe_unavailable";
    public const string ConnectProbeRejectedCode = "mt5_connect_probe_rejected";
    public const string ConnectProbeFailedCode = "mt5_connect_probe_failed";
}

public sealed record BrokerWorkerRequest(
    int ContractVersion,
    Guid RequestId,
    string Operation,
    DateTimeOffset DeadlineUtc,
    BrokerWorkerSendRequest? Send,
    BrokerWorkerReconcileRequest? Reconcile,
    BrokerWorkerConnectProbeRequest? ConnectProbe = null);

public sealed record BrokerWorkerSendRequest(
    Guid BrokerAccountId,
    Guid GatewayArtifactId,
    string GatewayArtifactSha256,
    string AuthorizationSha256,
    NormalizedBrokerCommand Command);

public sealed record BrokerWorkerReconcileRequest(IReadOnlyList<Guid> CommandIds);

/// <summary>
/// Non-secret inputs for a one-shot connection probe. The opaque credential key is
/// resolved inside a future dedicated worker; plaintext credentials must never be
/// serialized into this authenticated-but-unencrypted protocol.
/// </summary>
public sealed record BrokerWorkerConnectProbeRequest(
    Guid BrokerAccountId,
    Guid GatewayArtifactId,
    string GatewayArtifactSha256,
    string CredentialKey,
    string CredentialVaultIdentitySha256,
    BrokerServerIdentity Server,
    BrokerEnvironment ExpectedEnvironment,
    DateTimeOffset ProbeNotBeforeUtc);

/// <summary>
/// Redacted proof that a dedicated worker connected and then disconnected. It is
/// deliberately narrower than <see cref="GatewayCapabilities"/> and carries no
/// balance, equity, position, order, endpoint, or credential material.
/// </summary>
public sealed record BrokerConnectionProbeObservation(
    int ContractVersion,
    Guid BrokerAccountId,
    Guid GatewayArtifactId,
    string GatewayArtifactSha256,
    string MaskedLogin,
    string BrokerCompany,
    string ServerName,
    BrokerAccountMode AccountMode,
    BrokerEnvironment Environment,
    BrokerTradingAccess TradingAccess,
    string Currency,
    bool DisconnectConfirmed,
    DateTimeOffset ObservedAtUtc);

public sealed record BrokerWorkerResponse(
    int ContractVersion,
    Guid RequestId,
    string Operation,
    bool IsSuccess,
    string Code,
    GatewaySendResult? SendResult,
    BrokerReconciliationSnapshot? ReconciliationSnapshot,
    BrokerConnectionProbeObservation? ConnectProbeObservation = null);

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

/// <summary>
/// Separate capability implemented only by a dedicated, single-use connection
/// probe worker. It intentionally exposes no broker mutation method.
/// </summary>
public interface IBrokerConnectionProbeExecutor
{
    Task<GatewayOperationResult<BrokerConnectionProbeObservation>> ConnectProbeAsync(
        BrokerWorkerConnectProbeRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Default composition. No vendor assembly, credential provider, or network call is
/// reachable until an explicitly reviewed worker supplies another implementation.
/// </summary>
public sealed class UnavailableBrokerConnectionProbeExecutor
    : IBrokerConnectionProbeExecutor
{
    public Task<GatewayOperationResult<BrokerConnectionProbeObservation>> ConnectProbeAsync(
        BrokerWorkerConnectProbeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            new GatewayOperationResult<BrokerConnectionProbeObservation>(
                false,
                BrokerWorkerProtocolContract.ConnectProbeUnavailableCode,
                null));
    }
}
