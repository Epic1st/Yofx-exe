using System.Text;
using YO4X.LocalSecrets.Windows;
using YO4X.Mt5.ConnectionProbe.Windows;
using YO4X.Trading.Abstractions;

namespace YO4X.LocalSecrets.Windows.Tests;

public sealed class Mt5NetApiConnectionOnlyTransportTests
{
    [Fact]
    public async Task ConnectsReadsBoundedIdentityAndDisconnects()
    {
        var client = new FakeClient();
        var factory = new FakeFactory(client);
        var transport = new Mt5NetApiConnectionOnlyTransport(
            Endpoint(),
            factory,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 24, 10, 0, 1, TimeSpan.Zero)));
        using LocalMt5Credential credential = Credential();

        Mt5ConnectionOnlyObservation observation = await transport.ConnectAndDisconnectAsync(
            credential,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, factory.CreateCount);
        Assert.True(factory.PasswordMatched);
        Assert.Equal(1, client.ConnectCount);
        Assert.Equal(1, client.DisconnectCount);
        Assert.True(client.Disposed);
        Assert.True(observation.DisconnectConfirmed);
        Assert.Equal(BrokerEnvironment.Demo, observation.Environment);
        Assert.Equal(BrokerAccountMode.Hedging, observation.AccountMode);
        Assert.Equal(BrokerTradingAccess.Unknown, observation.TradingAccess);
        Assert.Equal("USD", observation.Currency);
    }

    [Fact]
    public async Task ConnectionFailureStillAttemptsDisconnectAndDisposes()
    {
        var client = new FakeClient { ConnectFailure = new TimeoutException("vendor detail") };
        var transport = new Mt5NetApiConnectionOnlyTransport(
            Endpoint(),
            new FakeFactory(client));
        using LocalMt5Credential credential = Credential();

        await Assert.ThrowsAsync<TimeoutException>(() => transport.ConnectAndDisconnectAsync(
            credential,
            TestContext.Current.CancellationToken));

        Assert.Equal(1, client.ConnectCount);
        Assert.Equal(1, client.DisconnectCount);
        Assert.True(client.Disposed);
    }

    [Fact]
    public async Task ServerMismatchFailsBeforeVendorFactory()
    {
        var factory = new FakeFactory(new FakeClient());
        var transport = new Mt5NetApiConnectionOnlyTransport(Endpoint(), factory);
        using var credential = new LocalMt5Credential(
            12345678,
            "Different-Demo",
            Encoding.UTF8.GetBytes("not-a-real-password"));

        await Assert.ThrowsAsync<InvalidDataException>(() => transport.ConnectAndDisconnectAsync(
            credential,
            TestContext.Current.CancellationToken));

        Assert.Equal(0, factory.CreateCount);
    }

    [Fact]
    public void PinnedFactoryChecksArtifactBeforeAssemblyLoad()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"yo4x-missing-mt5-{Guid.NewGuid():N}.dll");
        Assert.Throws<FileNotFoundException>(() =>
            new PinnedMt5NetApiConnectionClientFactory(missingPath));
    }

    private static Mt5NetApiConnectionEndpoint Endpoint() => new(
        "Synthetic Broker",
        "Synthetic-Demo",
        "127.0.0.1",
        443,
        [],
        string.Empty);

    private static LocalMt5Credential Credential() => new(
        12345678,
        "Synthetic-Demo",
        Encoding.UTF8.GetBytes("not-a-real-password"));

    private sealed class FakeFactory(FakeClient client) : IMt5NetApiConnectionClientFactory
    {
        public int CreateCount { get; private set; }

        public bool PasswordMatched { get; private set; }

        public IMt5NetApiConnectionClient Create(
            ulong login,
            string password,
            string host,
            int port,
            byte[] certificatePfx,
            string certificatePassword)
        {
            CreateCount++;
            Assert.Equal(12345678UL, login);
            Assert.Equal("127.0.0.1", host);
            Assert.Equal(443, port);
            Assert.Empty(certificatePfx);
            Assert.Empty(certificatePassword);
            PasswordMatched = string.Equals(
                password,
                "not-a-real-password",
                StringComparison.Ordinal);
            return client;
        }
    }

    private sealed class FakeClient : IMt5NetApiConnectionClient
    {
        public bool Connected { get; private set; }

        public ulong User => 12345678;

        public string AccountCompanyName => "Synthetic Broker";

        public string AccountCurrency => "usd";

        public object AccountMethod => "Hedging";

        public int ConnectCount { get; private set; }

        public int DisconnectCount { get; private set; }

        public bool Disposed { get; private set; }

        public Exception? ConnectFailure { get; init; }

        public void Connect()
        {
            ConnectCount++;
            if (ConnectFailure is not null)
            {
                throw ConnectFailure;
            }

            Connected = true;
        }

        public void Disconnect()
        {
            DisconnectCount++;
            Connected = false;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
