using System.Globalization;
using System.Text;
using YO4X.LocalSecrets.Windows;
using YO4X.Mt5.ConnectionProbe.Windows;
using YO4X.Trading.Abstractions;
using YO4X.Trading.ProcessIsolation;

namespace YO4X.LocalSecrets.Windows.Tests;

public sealed class VaultBackedBrokerConnectionProbeExecutorTests
{
    private static readonly Guid ArtifactId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly string ArtifactSha256 = new('a', 64);
    private static readonly string VaultIdentitySha256 = new('b', 64);

    [Fact]
    public async Task ApprovedBoundDemoProbeOpensCredentialOnceAndDisposesIt()
    {
        using var source = Credential();
        var vault = new RecordingVault(VaultIdentitySha256, source);
        var transport = new RecordingTransport(Observation());
        var executor = Executor(vault, transport);

        GatewayOperationResult<BrokerConnectionProbeObservation> result =
            await executor.ConnectProbeAsync(Request(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(BrokerWorkerProtocolContract.ConnectProbeSucceededCode, result.Code);
        Assert.Equal("******78", result.Value!.MaskedLogin);
        Assert.True(result.Value.DisconnectConfirmed);
        Assert.Equal(1, vault.OpenCount);
        Assert.Equal(1, transport.CallCount);
        Assert.Throws<ObjectDisposedException>(() =>
            transport.CapturedCredential!.UsePassword(static password => password.Length));
    }

    [Fact]
    public async Task UnapprovedArtifactNeverTouchesVaultOrTransport()
    {
        using var source = Credential();
        var vault = new RecordingVault(VaultIdentitySha256, source);
        var transport = new RecordingTransport(Observation());
        var executor = new VaultBackedBrokerConnectionProbeExecutor(
            vault,
            new ApprovedMt5ProbeArtifacts(new Dictionary<Guid, string>()),
            transport);

        GatewayOperationResult<BrokerConnectionProbeObservation> result =
            await executor.ConnectProbeAsync(Request(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(BrokerWorkerProtocolContract.ConnectProbeRejectedCode, result.Code);
        Assert.Equal(0, vault.EvidenceCount);
        Assert.Equal(0, vault.OpenCount);
        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public async Task WrongVaultBindingNeverOpensCredentialOrCallsTransport()
    {
        using var source = Credential();
        var vault = new RecordingVault(new string('c', 64), source);
        var transport = new RecordingTransport(Observation());

        GatewayOperationResult<BrokerConnectionProbeObservation> result =
            await Executor(vault, transport).ConnectProbeAsync(
                Request(),
                TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(BrokerWorkerProtocolContract.ConnectProbeRejectedCode, result.Code);
        Assert.Equal(1, vault.EvidenceCount);
        Assert.Equal(0, vault.OpenCount);
        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public async Task LiveRequestNeverTouchesVaultOrTransport()
    {
        using var source = Credential();
        var vault = new RecordingVault(VaultIdentitySha256, source);
        var transport = new RecordingTransport(Observation());

        GatewayOperationResult<BrokerConnectionProbeObservation> result =
            await Executor(vault, transport).ConnectProbeAsync(
                Request() with { ExpectedEnvironment = BrokerEnvironment.Live },
                TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(BrokerWorkerProtocolContract.ConnectProbeRejectedCode, result.Code);
        Assert.Equal(0, vault.EvidenceCount);
        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public async Task MissingDisconnectProofFailsAndDisposesCredential()
    {
        using var source = Credential();
        var vault = new RecordingVault(VaultIdentitySha256, source);
        var transport = new RecordingTransport(Observation() with { DisconnectConfirmed = false });

        GatewayOperationResult<BrokerConnectionProbeObservation> result =
            await Executor(vault, transport).ConnectProbeAsync(
                Request(),
                TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(BrokerWorkerProtocolContract.ConnectProbeFailedCode, result.Code);
        Assert.Null(result.Value);
        Assert.Throws<ObjectDisposedException>(() =>
            transport.CapturedCredential!.UsePassword(static password => password.Length));
    }

    [Fact]
    public async Task TransportExceptionIsRedactedAndCredentialIsDisposed()
    {
        using var source = Credential();
        var vault = new RecordingVault(VaultIdentitySha256, source);
        var transport = new RecordingTransport(new InvalidOperationException("secret-bearing vendor error"));

        GatewayOperationResult<BrokerConnectionProbeObservation> result =
            await Executor(vault, transport).ConnectProbeAsync(
                Request(),
                TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(BrokerWorkerProtocolContract.ConnectProbeFailedCode, result.Code);
        Assert.Null(result.Value);
        Assert.DoesNotContain("secret", result.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ObjectDisposedException>(() =>
            transport.CapturedCredential!.UsePassword(static password => password.Length));
    }

    private static VaultBackedBrokerConnectionProbeExecutor Executor(
        ILocalMt5CredentialVault vault,
        IMt5ConnectionOnlyTransport transport) => new(
            vault,
            new ApprovedMt5ProbeArtifacts(
                new Dictionary<Guid, string> { [ArtifactId] = ArtifactSha256 }),
            transport);

    private static BrokerWorkerConnectProbeRequest Request()
    {
        using LocalMt5Credential credential = Credential();
        return new BrokerWorkerConnectProbeRequest(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            ArtifactId,
            ArtifactSha256,
            credential.CredentialKey,
            VaultIdentitySha256,
            new BrokerServerIdentity("Synthetic Broker", "Synthetic-Demo"),
            BrokerEnvironment.Demo,
            DateTimeOffset.Parse("2026-08-24T10:00:00Z", CultureInfo.InvariantCulture));
    }

    private static LocalMt5Credential Credential() => new(
        12345678,
        "Synthetic-Demo",
        Encoding.UTF8.GetBytes("not-a-real-password"));

    private static Mt5ConnectionOnlyObservation Observation() => new(
        "Synthetic Broker",
        "Synthetic-Demo",
        BrokerAccountMode.Hedging,
        BrokerEnvironment.Demo,
        BrokerTradingAccess.ReadOnly,
        "USD",
        true,
        DateTimeOffset.Parse("2026-08-24T10:00:01Z", CultureInfo.InvariantCulture));

    private sealed class RecordingTransport : IMt5ConnectionOnlyTransport
    {
        private readonly Mt5ConnectionOnlyObservation? observation;
        private readonly Exception? exception;

        public RecordingTransport(Mt5ConnectionOnlyObservation observation) =>
            this.observation = observation;

        public RecordingTransport(Exception exception) => this.exception = exception;

        public int CallCount { get; private set; }

        public LocalMt5Credential? CapturedCredential { get; private set; }

        public Task<Mt5ConnectionOnlyObservation> ConnectAndDisconnectAsync(
            LocalMt5Credential credential,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CapturedCredential = credential;
            CallCount++;
            if (exception is not null)
            {
                throw exception;
            }

            _ = credential.UsePassword(static password => password.Length);
            return Task.FromResult(observation!);
        }
    }

    private sealed class RecordingVault(
        string evidenceBinding,
        LocalMt5Credential source)
        : ILocalMt5CredentialVault
    {
        public int EvidenceCount { get; private set; }

        public int OpenCount { get; private set; }

        public Task<string> GetEvidenceBindingAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EvidenceCount++;
            return Task.FromResult(evidenceBinding);
        }

        public Task<LocalMt5Credential?> OpenAsync(
            string credentialKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenCount++;
            LocalMt5Credential copy = source.UsePassword(password =>
                new LocalMt5Credential(source.Login, source.Server, password));
            return Task.FromResult<LocalMt5Credential?>(copy);
        }

        public Task<LocalCredentialWriteResult> StoreAsync(
            LocalMt5Credential credential,
            LocalCredentialWriteMode mode,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<LocalCredentialWriteResult>> StoreBatchAsync(
            IReadOnlyList<LocalMt5Credential> credentials,
            LocalCredentialWriteMode mode,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LocalCredentialBatchWriteReceipt> StoreBatchWithEvidenceAsync(
            IReadOnlyList<LocalMt5Credential> credentials,
            LocalCredentialWriteMode mode,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(
            string credentialKey,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
