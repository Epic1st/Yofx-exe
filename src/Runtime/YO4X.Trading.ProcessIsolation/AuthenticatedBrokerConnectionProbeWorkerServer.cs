using System.Security.Cryptography;
using YO4X.Trading.Abstractions;

namespace YO4X.Trading.ProcessIsolation;

/// <summary>
/// Single-purpose authenticated worker server. Unlike the trading worker server,
/// this type has no send or reconciliation executor and accepts only connect_probe.
/// </summary>
public sealed class AuthenticatedBrokerConnectionProbeWorkerServer(
    IBrokerConnectionProbeExecutor executor,
    TimeProvider? timeProvider = null)
{
    private readonly IBrokerConnectionProbeExecutor executor =
        executor ?? throw new ArgumentNullException(nameof(executor));
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;

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
            if (request.Operation != BrokerWorkerProtocolContract.ConnectProbeOperation
                || request.ConnectProbe is null)
            {
                return 70;
            }

            TimeSpan remaining = request.DeadlineUtc - now;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(remaining);
            GatewayOperationResult<BrokerConnectionProbeObservation> probe = await executor
                .ConnectProbeAsync(request.ConnectProbe, deadline.Token)
                .ConfigureAwait(false);
            var response = new BrokerWorkerResponse(
                BrokerWorkerProtocolContract.Version,
                request.RequestId,
                request.Operation,
                probe.IsSuccess && probe.Value is not null,
                probe.Code,
                null,
                null,
                probe.IsSuccess ? probe.Value : null);
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
            return 70;
        }
        finally
        {
            Zero(requestPayload);
            Zero(responsePayload);
            Zero(sessionKey);
        }
    }

    private static void Zero(byte[]? value)
    {
        if (value is not null)
        {
            CryptographicOperations.ZeroMemory(value);
        }
    }
}
