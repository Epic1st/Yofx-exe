using System.Security.Cryptography;
using System.Text;
using YO4X.Trading.Abstractions;
using YO4X.Trading.ProcessIsolation;

namespace YO4X.GatewayHost.Tests;

public sealed class BrokerConnectionProbeProtocolTests
{
    [Fact]
    public async Task DefaultServerExecutorReturnsOnlyUnavailableWithoutCallingMutationExecutor()
    {
        BrokerWorkerRequest request = ConnectProbeRequest();
        var mutationExecutor = new MutationForbiddenExecutor();
        var server = new AuthenticatedBrokerWorkerServer(mutationExecutor);

        BrokerWorkerResponse response = await RoundTripAsync(server, request);

        Assert.False(response.IsSuccess);
        Assert.Equal(
            BrokerWorkerProtocolContract.ConnectProbeUnavailableCode,
            response.Code);
        Assert.Null(response.ConnectProbeObservation);
        Assert.Null(response.SendResult);
        Assert.Null(response.ReconciliationSnapshot);
        Assert.False(mutationExecutor.Invoked);
    }

    [Fact]
    public async Task SyntheticProbeRoundTripReturnsBoundRedactedObservation()
    {
        BrokerWorkerRequest request = ConnectProbeRequest();
        var executor = new SyntheticConnectionProbeExecutor(request.ConnectProbe!);
        var server = new AuthenticatedBrokerWorkerServer(
            new MutationForbiddenExecutor(),
            connectionProbeExecutor: executor);

        BrokerWorkerResponse response = await RoundTripAsync(server, request);

        Assert.True(response.IsSuccess);
        Assert.Equal(
            BrokerWorkerProtocolContract.ConnectProbeSucceededCode,
            response.Code);
        BrokerConnectionProbeObservation observation = Assert.IsType<
            BrokerConnectionProbeObservation>(response.ConnectProbeObservation);
        Assert.Equal("******1234", observation.MaskedLogin);
        Assert.True(observation.DisconnectConfirmed);
        Assert.DoesNotContain("Password", observation.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.True(executor.Invoked);
    }

    [Fact]
    public void ValidatorRejectsLiveOrMixedOperationRequests()
    {
        BrokerWorkerRequest valid = ConnectProbeRequest();
        BrokerWorkerConnectProbeRequest live = valid.ConnectProbe! with
        {
            ExpectedEnvironment = BrokerEnvironment.Live
        };
        BrokerWorkerRequest mixed = valid with
        {
            Reconcile = new BrokerWorkerReconcileRequest([Guid.CreateVersion7()])
        };

        Assert.Throws<InvalidDataException>(() =>
            BrokerWorkerContractValidator.ValidateRequest(
                valid with { ConnectProbe = live },
                ValidationNow(valid)));
        Assert.Throws<InvalidDataException>(() =>
            BrokerWorkerContractValidator.ValidateRequest(mixed, ValidationNow(valid)));
    }

    [Fact]
    public void StrictDeserializerRejectsUnknownPasswordMember()
    {
        byte[] canonical = BrokerProcessProtocol.SerializeRequest(
            ConnectProbeRequest(),
            BrokerProcessProtocol.DefaultMaximumRequestBytes);
        byte[]? altered = null;
        try
        {
            string json = Encoding.UTF8.GetString(canonical);
            string withUnknownMember = json.Replace(
                "\"connectProbe\":",
                "\"password\":\"synthetic-test-only\",\"connectProbe\":",
                StringComparison.Ordinal);
            altered = Encoding.UTF8.GetBytes(withUnknownMember);

            Assert.Throws<InvalidDataException>(() =>
                BrokerProcessProtocol.DeserializeRequest(altered));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(canonical);
            if (altered is not null)
            {
                CryptographicOperations.ZeroMemory(altered);
            }
        }
    }

    [Theory]
    [InlineData("12345678")]
    [InlineData("*12345")]
    [InlineData("****12x4")]
    public void ValidatorRejectsUnredactedOrMalformedLogin(string maskedLogin)
    {
        BrokerWorkerRequest request = ConnectProbeRequest();
        BrokerWorkerResponse response = SuccessfulResponse(request, maskedLogin);

        Assert.Throws<InvalidDataException>(() =>
            BrokerWorkerContractValidator.ValidateResponse(response, request));
    }

    [Fact]
    public void ValidatorRejectsUnboundOrNotDisconnectedSuccessEvidence()
    {
        BrokerWorkerRequest request = ConnectProbeRequest();
        BrokerWorkerResponse valid = SuccessfulResponse(request, "******1234");
        BrokerConnectionProbeObservation observation = valid.ConnectProbeObservation!;

        Assert.Throws<InvalidDataException>(() =>
            BrokerWorkerContractValidator.ValidateResponse(
                valid with
                {
                    ConnectProbeObservation = observation with
                    {
                        BrokerAccountId = Guid.CreateVersion7()
                    }
                },
                request));
        Assert.Throws<InvalidDataException>(() =>
            BrokerWorkerContractValidator.ValidateResponse(
                valid with
                {
                    ConnectProbeObservation = observation with
                    {
                        DisconnectConfirmed = false
                    }
                },
                request));
    }

    [Fact]
    public void ValidatorRejectsStaleOrFutureConnectionEvidence()
    {
        BrokerWorkerRequest request = ConnectProbeRequest();
        BrokerWorkerResponse valid = SuccessfulResponse(request, "******1234");
        BrokerConnectionProbeObservation observation = valid.ConnectProbeObservation!;

        Assert.Throws<InvalidDataException>(() =>
            BrokerWorkerContractValidator.ValidateResponse(
                valid with
                {
                    ConnectProbeObservation = observation with
                    {
                        ObservedAtUtc = request.ConnectProbe!.ProbeNotBeforeUtc.AddTicks(-1)
                    }
                },
                request));
        Assert.Throws<InvalidDataException>(() =>
            BrokerWorkerContractValidator.ValidateResponse(
                valid with
                {
                    ConnectProbeObservation = observation with
                    {
                        ObservedAtUtc = request.DeadlineUtc.AddTicks(1)
                    }
                },
                request));
    }

    private static async Task<BrokerWorkerResponse> RoundTripAsync(
        AuthenticatedBrokerWorkerServer server,
        BrokerWorkerRequest request)
    {
        byte[] sessionKey = RandomNumberGenerator.GetBytes(
            BrokerProcessProtocol.SessionKeyBytes);
        byte[] requestPayload = BrokerProcessProtocol.SerializeRequest(
            request,
            BrokerProcessProtocol.DefaultMaximumRequestBytes);
        byte[]? responsePayload = null;
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
                requestPayload,
                sessionKey,
                TestContext.Current.CancellationToken);
            input.Position = 0;

            int exitCode = await server.RunOnceAsync(
                input,
                output,
                TestContext.Current.CancellationToken);

            Assert.Equal(0, exitCode);
            output.Position = 0;
            responsePayload = await BrokerProcessProtocol.ReadResponseAsync(
                output,
                sessionKey,
                BrokerProcessProtocol.DefaultMaximumResponseBytes,
                TestContext.Current.CancellationToken);
            BrokerWorkerResponse response = BrokerProcessProtocol.DeserializeResponse(
                responsePayload);
            BrokerWorkerContractValidator.ValidateResponse(response, request);
            return response;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(requestPayload);
            CryptographicOperations.ZeroMemory(sessionKey);
            if (responsePayload is not null)
            {
                CryptographicOperations.ZeroMemory(responsePayload);
            }
        }
    }

    private static BrokerWorkerRequest ConnectProbeRequest()
    {
        DateTimeOffset probeNotBeforeUtc = DateTimeOffset.UtcNow;
        var probe = new BrokerWorkerConnectProbeRequest(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            new string('a', 64),
            new string('b', 64),
            new string('c', 64),
            new BrokerServerIdentity("Synthetic Broker", "Synthetic-Demo"),
            BrokerEnvironment.Demo,
            probeNotBeforeUtc);
        return new BrokerWorkerRequest(
            BrokerWorkerProtocolContract.Version,
            Guid.CreateVersion7(),
            BrokerWorkerProtocolContract.ConnectProbeOperation,
            probeNotBeforeUtc.AddSeconds(30),
            null,
            null,
            probe);
    }

    private static DateTimeOffset ValidationNow(BrokerWorkerRequest request) =>
        request.DeadlineUtc.AddSeconds(-30);

    private static BrokerWorkerResponse SuccessfulResponse(
        BrokerWorkerRequest request,
        string maskedLogin) =>
        new(
            BrokerWorkerProtocolContract.Version,
            request.RequestId,
            request.Operation,
            true,
            BrokerWorkerProtocolContract.ConnectProbeSucceededCode,
            null,
            null,
            Observation(request.ConnectProbe!, maskedLogin));

    private static BrokerConnectionProbeObservation Observation(
        BrokerWorkerConnectProbeRequest request,
        string maskedLogin) =>
        new(
            BrokerWorkerProtocolContract.ConnectProbeObservationVersion,
            request.BrokerAccountId,
            request.GatewayArtifactId,
            request.GatewayArtifactSha256,
            maskedLogin,
            request.Server.BrokerCompany,
            request.Server.ServerName,
            BrokerAccountMode.Hedging,
            BrokerEnvironment.Demo,
            BrokerTradingAccess.Unknown,
            "USD",
            true,
            request.ProbeNotBeforeUtc);

    private sealed class MutationForbiddenExecutor : IBrokerWorkerExecutor
    {
        public bool Invoked { get; private set; }

        public Task<GatewaySendResult> SendAsync(
            BrokerWorkerSendRequest request,
            CancellationToken cancellationToken)
        {
            Invoked = true;
            throw new InvalidOperationException("Mutation execution is forbidden in this test.");
        }

        public Task<GatewayOperationResult<BrokerReconciliationSnapshot>> ReconcileAsync(
            BrokerWorkerReconcileRequest request,
            CancellationToken cancellationToken)
        {
            Invoked = true;
            throw new InvalidOperationException("Mutation execution is forbidden in this test.");
        }
    }

    private sealed class SyntheticConnectionProbeExecutor(
        BrokerWorkerConnectProbeRequest expectedRequest)
        : IBrokerConnectionProbeExecutor
    {
        public bool Invoked { get; private set; }

        public Task<GatewayOperationResult<BrokerConnectionProbeObservation>> ConnectProbeAsync(
            BrokerWorkerConnectProbeRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(expectedRequest, request);
            Invoked = true;
            return Task.FromResult(
                new GatewayOperationResult<BrokerConnectionProbeObservation>(
                    true,
                    BrokerWorkerProtocolContract.ConnectProbeSucceededCode,
                    Observation(request, "******1234")));
        }
    }
}
