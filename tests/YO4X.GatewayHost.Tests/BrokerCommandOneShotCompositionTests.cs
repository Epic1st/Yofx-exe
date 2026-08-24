using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using YO4X.BuildingBlocks;
using YO4X.Persistence.Postgres;
using YO4X.Tenancy;
using YO4X.Trading.Abstractions;
using YO4X.Trading.Application;
using YO4X.Trading.Mt5;
using YO4X.Trading.Postgres;
using YO4X.Trading.ProcessIsolation;

namespace YO4X.GatewayHost.Tests;

public sealed class BrokerCommandOneShotCompositionTests
{
    [Fact]
    public async Task ProductionGatewayRegistrationIsProcessIsolatedAndDisabledByDefault()
    {
        var services = new ServiceCollection();
        services.AddMt5ProcessBoundary(new ConfigurationBuilder().Build());

        await using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<IsolatedMt5ProcessGateway>(
            provider.GetRequiredService<IMt5Gateway>());
        Assert.False(provider.GetRequiredService<IsolatedBrokerProcessOptions>().Enabled);
    }

    [Fact]
    public async Task DisabledByDefaultRegistersNoDatabaseOrCoordinatorBoundary()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMt5Gateway, Mt5ProofOnlyGateway>();
        services.AddBrokerCommandOneShot(new ConfigurationBuilder().Build());

        await using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<Mt5ProofOnlyGateway>(provider.GetRequiredService<IMt5Gateway>());
        Assert.Null(provider.GetService<IBrokerCommandLifecycleStore>());
        Assert.Null(provider.GetService<BrokerCommandCoordinator>());
        Assert.Null(provider.GetService<PostgresBrokerCommandStore>());
        Assert.Null(provider.GetService<PostgresBrokerCommandLifecycleStore>());
        GatewayHostRuntimeStatus status = provider.GetRequiredService<GatewayHostRuntimeStatus>();
        Assert.Equal("gateway_host_one_shot_disabled", status.Startup.Code);
        Assert.Equal("not-ready", status.Ready.Status);
        Assert.Equal("gateway_host_proof_only_not_mutation_ready", status.Ready.Code);
        Assert.IsType<BrokerCommandOneShotWorker>(
            Assert.Single(provider.GetServices<IHostedService>()));
    }

    [Fact]
    public async Task EnabledCompositionResolvesOnlyTheGatewayLifecyclePort()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMt5Gateway, Mt5ProofOnlyGateway>();
        AddTenantContextProvider(services);
        services.AddBrokerCommandOneShot(BuildConfiguration(ValidValues()));

        await using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

        Assert.IsType<PostgresBrokerCommandLifecycleStore>(
            provider.GetRequiredService<IBrokerCommandLifecycleStore>());
        Assert.NotNull(provider.GetRequiredService<BrokerCommandCoordinator>());
        Assert.Null(provider.GetService<PostgresBrokerCommandStore>());
        Assert.Null(provider.GetService<PostgresBrokerCommandLifecycleStore>());
        Assert.Null(provider.GetService<YO4X.Persistence.Postgres.PostgresDatabase>());
        Assert.Null(provider.GetService<IExecutionLeaseTrustVerifier>());
        Assert.False(provider.GetRequiredService<BrokerCommandCoordinatorOptions>()
            .SubmissionEnabled);
        Assert.IsType<Mt5ProofOnlyGateway>(provider.GetRequiredService<IMt5Gateway>());
    }

    [Fact]
    public async Task ConfigurationCannotEnableTheMutationEntryGate()
    {
        Dictionary<string, string?> values = ValidValues();
        values[Key("SubmissionEnabled")] = "true";
        var services = new ServiceCollection();
        services.AddSingleton<IMt5Gateway, Mt5ProofOnlyGateway>();
        AddTenantContextProvider(services);
        services.AddBrokerCommandOneShot(BuildConfiguration(values));

        await using ServiceProvider provider = services.BuildServiceProvider();

        Assert.False(provider.GetRequiredService<BrokerCommandCoordinatorOptions>()
            .SubmissionEnabled);
        Assert.IsType<Mt5ProofOnlyGateway>(provider.GetRequiredService<IMt5Gateway>());
        Assert.Null(provider.GetService<PostgresBrokerCommandStore>());
    }

    [Fact]
    public void WrongDatabaseRoleFailsStartupWithoutLeakingConfiguration()
    {
        Dictionary<string, string?> values = ValidValues();
        const string secret = "do-not-expose-this-password";
        values[Key("GatewayRuntimeConnectionString")] =
            "Host=db.example;Database=yo4x;Username=yo4x_trade_authorizer;"
            + $"Password={secret};SSL Mode=VerifyFull";

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddBrokerCommandOneShot(BuildConfiguration(values)));

        Assert.Equal("Broker command one-shot configuration is invalid.", exception.Message);
        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            values[Key("AuthorizationSha256")]!,
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void UnsafeDatabaseDiagnosticsFailStartup()
    {
        Dictionary<string, string?> values = ValidValues();
        values[Key("GatewayRuntimeConnectionString")] =
            "Host=db.example;Database=yo4x;Username=yo4x_gateway_runtime;"
            + "Password=secret;SSL Mode=VerifyFull;Include Error Detail=true";

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddBrokerCommandOneShot(BuildConfiguration(values)));

        Assert.Equal("Broker command one-shot configuration is invalid.", exception.Message);
    }

    [Fact]
    public void AuthorityWindowShorterThanSendTimeoutAndMarginFailsStartup()
    {
        Dictionary<string, string?> values = ValidValues();
        values[Key("GatewaySendTimeout")] = "00:00:05";
        values[Key("AuthoritySafetyMargin")] = "00:00:01";
        values[Key("MinimumAuthorityWindow")] = "00:00:05.999";

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddBrokerCommandOneShot(BuildConfiguration(values)));

        Assert.Equal("Broker command one-shot configuration is invalid.", exception.Message);
    }

    [Fact]
    public void MissingPinnedP256TrustSetFailsStartup()
    {
        Dictionary<string, string?> values = ValidValues();
        values.Remove(Key("TrustedLeasePublicKeys:0:KeyId"));
        values.Remove(Key("TrustedLeasePublicKeys:0:SubjectPublicKeyInfoBase64"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddBrokerCommandOneShot(BuildConfiguration(values)));

        Assert.Equal("Broker command one-shot configuration is invalid.", exception.Message);
    }

    [Fact]
    public void EnabledGatewayWithoutScopedContextProviderFailsStartupRedacted()
    {
        Dictionary<string, string?> values = ValidValues();

        BackendCapabilityUnavailableException exception =
            Assert.Throws<BackendCapabilityUnavailableException>(() =>
                new ServiceCollection().AddBrokerCommandOneShot(
                    BuildConfiguration(values)));

        Assert.Equal("gateway-tenant-context-provider", exception.Capability);
        Assert.DoesNotContain("secret", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            values[Key("AuthorizationSha256")]!,
            exception.ToString(),
            StringComparison.Ordinal);
    }

    internal static IConfiguration BuildConfiguration(
        IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    internal static Dictionary<string, string?> ValidValues()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string spki = Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo());
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Key("Enabled")] = "true",
            [Key("TenantId")] = "10000000-0000-0000-0000-000000000001",
            [Key("GatewayWorkloadId")] = "20000000-0000-0000-0000-000000000002",
            [Key("CommandId")] = "30000000-0000-0000-0000-000000000003",
            [Key("AuthorizationSha256")] = new string('a', 64),
            [Key("LeaseTokenSha256")] = new string('b', 64),
            [Key("GatewayRuntimeConnectionString")] =
                "Host=db.example;Database=yo4x;Username=yo4x_gateway_runtime;"
                + "Password=secret;SSL Mode=VerifyFull",
            [Key("TrustedLeasePublicKeys:0:KeyId")] = "lease-key-1",
            [Key("TrustedLeasePublicKeys:0:SubjectPublicKeyInfoBase64")] = spki
        };
    }

    private static string Key(string suffix) =>
        $"{BrokerCommandOneShotSettings.SectionName}:{suffix}";

    private static void AddTenantContextProvider(IServiceCollection services) =>
        services.AddSingleton<ITenantContextCapabilityProvider>(
            new StubTenantContextCapabilityProvider());

    private sealed class StubTenantContextCapabilityProvider :
        ITenantContextCapabilityProvider
    {
        public PostgresDatabaseEndpoint Endpoint { get; } = new(
            "db.example",
            5432,
            "yo4x");

        public ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);

        public ValueTask<TenantContextCapability> AcquireAsync(
            TenantExecutionContext context,
            TenantContextTransactionBinding binding,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TenantContextCapability.Create(
                Enumerable.Repeat((byte)1, TenantContextCapability.SizeInBytes).ToArray()));
    }
}
