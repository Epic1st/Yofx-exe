using System.Security.Cryptography;
using YO4X.Trading.Abstractions;

namespace YO4X.Trading.ProcessIsolation;

public sealed class AuthenticatedBrokerWorkerServer
{
    private readonly IBrokerWorkerExecutor executor;
    private readonly IBrokerConnectionProbeExecutor connectionProbeExecutor;
    private readonly TimeProvider timeProvider;

    public AuthenticatedBrokerWorkerServer(
        IBrokerWorkerExecutor executor,
        TimeProvider? timeProvider = null,
        IBrokerConnectionProbeExecutor? connectionProbeExecutor = null)
    {
        this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.connectionProbeExecutor = connectionProbeExecutor
            ?? new UnavailableBrokerConnectionProbeExecutor();
    }

    public async Task<int> RunOnceAsync(
        Stream input,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        byte[]? sessionKey = null;
        byte[]? requestPayload = null;
        byte[]? responsePayload = null;
        try
        {
            sessionKey = await BrokerProcessProtocol.ReadBootstrapAsync(input, cancellationToken)
                .ConfigureAwait(false);
            requestPayload = await BrokerProcessProtocol.ReadRequestAsync(
                    input,
                    sessionKey,
                    BrokerProcessProtocol.DefaultMaximumRequestBytes,
                    cancellationToken)
                .ConfigureAwait(false);
            BrokerWorkerRequest request = BrokerProcessProtocol.DeserializeRequest(requestPayload);
            DateTimeOffset now = timeProvider.GetUtcNow();
            BrokerWorkerContractValidator.ValidateRequest(request, now);

            TimeSpan remaining = request.DeadlineUtc - now;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(remaining);
            BrokerWorkerResponse response = await ExecuteAsync(request, deadline.Token)
                .ConfigureAwait(false);
            BrokerWorkerContractValidator.ValidateResponse(response, request);
            responsePayload = BrokerProcessProtocol.SerializeResponse(
                response,
                BrokerProcessProtocol.DefaultMaximumResponseBytes);
            await BrokerProcessProtocol.WriteResponseAsync(
                    output,
                    responsePayload,
                    sessionKey,
                    deadline.Token)
                .ConfigureAwait(false);
            return 0;
        }
        catch
        {
            // A worker is single-use and communicates only through authenticated,
            // fixed-schema frames. No exception or request material is logged.
            return 70;
        }
        finally
        {
            Zero(requestPayload);
            Zero(responsePayload);
            Zero(sessionKey);
        }
    }

    private async Task<BrokerWorkerResponse> ExecuteAsync(
        BrokerWorkerRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Operation == BrokerWorkerProtocolContract.SendOperation)
        {
            GatewaySendResult result = await executor.SendAsync(request.Send!, cancellationToken)
                .ConfigureAwait(false);
            return new BrokerWorkerResponse(
                BrokerWorkerProtocolContract.Version,
                request.RequestId,
                request.Operation,
                result.Disposition == GatewayCommandDisposition.Accepted,
                result.Code,
                result,
                null);
        }

        if (request.Operation == BrokerWorkerProtocolContract.ConnectProbeOperation)
        {
            GatewayOperationResult<BrokerConnectionProbeObservation> probe =
                await connectionProbeExecutor
                    .ConnectProbeAsync(request.ConnectProbe!, cancellationToken)
                    .ConfigureAwait(false);
            return new BrokerWorkerResponse(
                BrokerWorkerProtocolContract.Version,
                request.RequestId,
                request.Operation,
                probe.IsSuccess && probe.Value is not null,
                probe.Code,
                null,
                null,
                probe.IsSuccess ? probe.Value : null);
        }

        GatewayOperationResult<BrokerReconciliationSnapshot> reconciliation = await executor
            .ReconcileAsync(request.Reconcile!, cancellationToken)
            .ConfigureAwait(false);
        return new BrokerWorkerResponse(
            BrokerWorkerProtocolContract.Version,
            request.RequestId,
            request.Operation,
            reconciliation.IsSuccess && reconciliation.Value is not null,
            reconciliation.Code,
            null,
            reconciliation.IsSuccess ? reconciliation.Value : null);
    }

    private static void Zero(byte[]? value)
    {
        if (value is not null)
        {
            CryptographicOperations.ZeroMemory(value);
        }
    }
}
