using System.Security.Cryptography;
using YO4X.Trading.Abstractions;
using YO4X.Trading.ProcessIsolation;

namespace YO4X.GatewayHost.Tests;

public sealed class DedicatedConnectionProbeWorkerServerTests
{
    [Fact]
    public async Task ConnectProbeUsesOnlyConnectionExecutor()
    {
        var executor = new RecordingProbeExecutor();
        var server = new AuthenticatedBrokerConnectionProbeWorkerServer(executor);
        BrokerWorkerRequest request = ConnectRequest();

        (int exitCode, BrokerWorkerResponse? response) = await RoundTripAsync(server, request);

        Assert.Equal(0, exitCode);
        Assert.True(executor.Invoked);
        Assert.NotNull(response);
        Assert.False(response.IsSuccess);
        Assert.Equal(BrokerWorkerProtocolContract.ConnectProbeUnavailableCode, response.Code);
    }

    [Fact]
    public async Task ValidSendFrameIsRejectedWithoutAResponse()
    {
        var executor = new RecordingProbeExecutor();
        var server = new AuthenticatedBrokerConnectionProbeWorkerServer(executor);

        (int exitCode, BrokerWorkerResponse? response) = await RoundTripAsync(
            server,
            SendRequest());

        Assert.Equal(70, exitCode);
        Assert.False(executor.Invoked);
        Assert.Null(response);
    }

    private static async Task<(int ExitCode, BrokerWorkerResponse? Response)> RoundTripAsync(
        AuthenticatedBrokerConnectionProbeWorkerServer server,
        BrokerWorkerRequest request)
    {
        byte[] sessionKey = RandomNumberGenerator.GetBytes(BrokerProcessProtocol.SessionKeyBytes);
        byte[] payload = BrokerProcessProtocol.SerializeRequest(
            request,
            BrokerProcessProtocol.DefaultMaximumRequestBytes);
        try
        {
            using var input = new MemoryStream();
            using var output = new MemoryStream();
            await BrokerProcessProtocol.WriteBootstrapAsync(
                input,
                sessionKey,
                TestContext.Current.CancellationToken);
            await BrokerProcessProtocol.WriteRequestAsync(
                input,
                payload,
                sessionKey,
                TestContext.Current.CancellationToken);
            input.Position = 0;

            int exitCode = await server.RunOnceAsync(
                input,
                output,
                TestContext.Current.CancellationToken);
            if (output.Length == 0)
            {
                return (exitCode, null);
            }

            output.Position = 0;
            byte[] responsePayload = await BrokerProcessProtocol.ReadResponseAsync(
                output,
                sessionKey,
                BrokerProcessProtocol.DefaultMaximumResponseBytes,
                TestContext.Current.CancellationToken);
            try
            {
                return (exitCode, BrokerProcessProtocol.DeserializeResponse(responsePayload));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(responsePayload);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sessionKey);
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private static BrokerWorkerRequest ConnectRequest()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new BrokerWorkerRequest(
            BrokerWorkerProtocolContract.Version,
            Guid.CreateVersion7(),
            BrokerWorkerProtocolContract.ConnectProbeOperation,
            now.AddSeconds(30),
            null,
            null,
            new BrokerWorkerConnectProbeRequest(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                new string('a', 64),
                new string('b', 64),
                new string('c', 64),
                new BrokerServerIdentity("Synthetic Broker", "Synthetic-Demo"),
                BrokerEnvironment.Demo,
                now));
    }

    private static BrokerWorkerRequest SendRequest()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var command = new NormalizedBrokerCommand(
            1,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            1,
            $"probe-reject-{Guid.CreateVersion7():N}",
            BrokerCommandAction.Place,
            "EURUSD",
            BrokerOrderSide.Buy,
            BrokerOrderType.Market,
            0.01m,
            null,
            null,
            null,
            10,
            "probe-must-not-trade",
            null,
            null,
            null,
            null,
            null,
            null,
            now);
        return new BrokerWorkerRequest(
            BrokerWorkerProtocolContract.Version,
            Guid.CreateVersion7(),
            BrokerWorkerProtocolContract.SendOperation,
            now.AddSeconds(30),
            new BrokerWorkerSendRequest(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                new string('a', 64),
                new string('b', 64),
                command),
            null);
    }

    private sealed class RecordingProbeExecutor : IBrokerConnectionProbeExecutor
    {
        public bool Invoked { get; private set; }

        public Task<GatewayOperationResult<BrokerConnectionProbeObservation>> ConnectProbeAsync(
            BrokerWorkerConnectProbeRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Invoked = true;
            return Task.FromResult(
                new GatewayOperationResult<BrokerConnectionProbeObservation>(
                    false,
                    BrokerWorkerProtocolContract.ConnectProbeUnavailableCode,
                    null));
        }
    }
}
