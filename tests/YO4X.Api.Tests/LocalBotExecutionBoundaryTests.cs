using YO4X.ControlPlane.Api;

namespace YO4X.Api.Tests;

public sealed class LocalBotExecutionBoundaryTests
{
    [Fact]
    public void EndpointSelectionSkipsPrivateAddressesAndKeepsReachablePublicOrder()
    {
        IReadOnlyList<LocalMt5Endpoint> endpoints = LocalMt5EndpointSelector.Build(
            [
                "10.131.152.104:443",
                "13.114.223.90:443",
                "13.114.223.90:443",
                "18.61.63.206:443",
            ],
            null,
            0);

        Assert.Collection(
            endpoints,
            endpoint => Assert.Equal(new LocalMt5Endpoint("13.114.223.90", 443), endpoint),
            endpoint => Assert.Equal(new LocalMt5Endpoint("18.61.63.206", 443), endpoint));
    }

    [Fact]
    public void EndpointFailoverDisposesFailedClientAndCreatesAFreshClient()
    {
        LocalMt5Endpoint[] endpoints =
        [
            new("13.114.223.90", 443),
            new("18.61.63.206", 443),
        ];
        var clients = new List<FakeClient>();

        FakeClient connected = LocalMt5EndpointFailover.Connect(
            endpoints,
            endpoint =>
            {
                var client = new FakeClient(endpoint, clients.Count == 0);
                clients.Add(client);
                return client;
            },
            client => client.Connect(),
            exception => exception is IOException);

        Assert.Equal(2, clients.Count);
        Assert.True(clients[0].Disposed);
        Assert.False(clients[1].Disposed);
        Assert.Same(clients[1], connected);
    }

    [Fact]
    public void EndpointFailoverDoesNotRetryAnAuthenticationFailure()
    {
        LocalMt5Endpoint[] endpoints =
        [
            new("13.114.223.90", 443),
            new("18.61.63.206", 443),
        ];
        int created = 0;

        Assert.Throws<UnauthorizedAccessException>(() => LocalMt5EndpointFailover.Connect(
            endpoints,
            endpoint =>
            {
                created++;
                return new FakeClient(endpoint, shouldFail: false);
            },
            _ => throw new UnauthorizedAccessException("Rejected."),
            exception => exception is IOException));
        Assert.Equal(1, created);
    }

    [Fact]
    public void LocalStartIsAtomicAndVerifiesTheResolvedArtifact()
    {
        string source = ReadRepositoryFile(
            "src", "Apps", "YO4X.ControlPlane.Api", "LocalBotExecutionCoordinator.cs");

        Assert.Contains(
            "await SetFaultAsync(actor, settings.BotId, exception, \"BOT_START_FAILED\", CancellationToken.None)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("TryResolveArtifact(binding.PackageSha256", source, StringComparison.Ordinal);
        Assert.Contains("SHA256.HashData(stream)", source, StringComparison.Ordinal);
        Assert.Contains("CryptographicOperations.FixedTimeEquals", source, StringComparison.Ordinal);
        Assert.Contains("and status = 'STARTING'", source, StringComparison.Ordinal);
        Assert.Contains("candidate.package_format_version = 2", source, StringComparison.Ordinal);
        Assert.Contains(
            "regexp_replace(lower(btrim(candidate.name)), '\\.(mq5|yo4x)$', '')",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PublicationRebindsOnlyInactivePackagedBots()
    {
        string source = ReadRepositoryFile(
            "src", "Apps", "YO4X.ControlPlane.Api", "MarketplacePublicationEndpoint.cs");

        Assert.Contains("RebindPackagedBotsAsync(", source, StringComparison.Ordinal);
        Assert.Contains("previous.is_drm_protected", source, StringComparison.Ordinal);
        Assert.Contains("previous.package_format_version >= 2", source, StringComparison.Ordinal);
        Assert.Contains(
            "bot.status in ('DRAFT', 'STOPPED', 'PAUSED', 'FAULTED')",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("bot.status in ('RUNNING'", source, StringComparison.Ordinal);
        Assert.Contains(
            "bytes[7] = (byte)((bytes[7] & 0x0F) | 0x80)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PlatformPublicationPersistsTheArtifactAndDoesNotImpersonateAUserSeller()
    {
        string source = ReadRepositoryFile(
            "src", "Apps", "YO4X.ControlPlane.Api", "MarketplacePublicationEndpoint.cs");
        string migration = ReadRepositoryFile(
            "src", "BuildingBlocks", "YO4X.Persistence.Postgres", "Migrations",
            "023_platform_marketplace_listings.sql");

        Assert.Contains("await StoreArtifactAsync(", source, StringComparison.Ordinal);
        Assert.Contains("package_bytes = excluded.package_bytes", source, StringComparison.Ordinal);
        Assert.Contains("@id, @tenant, null, @strategy, 'listed'", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddWithValue(\"seller\"", source, StringComparison.Ordinal);
        Assert.Contains("alter column seller_user_id drop not null", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void IssuingANewDesktopBundleRotatesThePreviousLocalRun()
    {
        string source = ReadRepositoryFile(
            "src", "Apps", "YO4X.ControlPlane.Api", "MarketplaceUserEndpoints.cs");

        Assert.Contains("and state in ('ISSUED', 'RUNNING')", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "and state in ('ISSUED', 'RUNNING') and expires_at <= clock_timestamp()",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LocalBundleResolvesABrokerSpecificSymbolSuffix()
    {
        string source = ReadRepositoryFile(
            "src", "Apps", "YO4X.ControlPlane.Api", "MarketplaceUserEndpoints.cs");

        Assert.Contains(
            "lower(instrument.symbol) like lower(bot.symbol) || '%'",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "(lower(instrument.symbol) = lower(bot.symbol)) desc",
            source,
            StringComparison.Ordinal);
        Assert.True(
            source.IndexOf("instrument.observed_at desc", StringComparison.Ordinal)
            < source.IndexOf("(lower(instrument.symbol) = lower(bot.symbol)) desc", StringComparison.Ordinal));
        Assert.Contains("length(instrument.symbol)", source, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "YO4X.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        string path = Path.Combine([directory.FullName, .. segments]);
        Assert.True(File.Exists(path), $"The repository contract file {path} was not found.");
        return File.ReadAllText(path);
    }

    private sealed class FakeClient(LocalMt5Endpoint endpoint, bool shouldFail) : IDisposable
    {
        internal LocalMt5Endpoint Endpoint { get; } = endpoint;
        internal bool Disposed { get; private set; }

        internal void Connect()
        {
            if (shouldFail) throw new IOException("Unreachable endpoint.");
        }

        public void Dispose() => Disposed = true;
    }
}
