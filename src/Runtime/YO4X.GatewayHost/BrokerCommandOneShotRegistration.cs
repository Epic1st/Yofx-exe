using Microsoft.Extensions.DependencyInjection.Extensions;
using YO4X.BuildingBlocks;
using YO4X.Persistence.Postgres;
using YO4X.Trading.Abstractions;
using YO4X.Trading.Application;
using YO4X.Trading.Postgres;

namespace YO4X.GatewayHost;

internal static class BrokerCommandOneShotRegistration
{
    internal static IServiceCollection AddBrokerCommandOneShot(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        BrokerCommandOneShotSettings settings = BrokerCommandOneShotSettings.Load(configuration);
        services.AddSingleton<BrokerCommandOneShotSettings>(_ => settings);
        services.AddSingleton(new GatewayHostRuntimeStatus(settings.Enabled));

        if (settings.Enabled)
        {
            if (!services.Any(static descriptor =>
                    descriptor.ServiceType == typeof(ITenantContextCapabilityProvider)))
            {
                throw new BackendCapabilityUnavailableException(
                    "gateway-tenant-context-provider");
            }

            services.AddSingleton(settings.CoordinatorOptions);
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton(serviceProvider =>
                new GatewayBrokerCommandLifecycleStoreOwner(
                    settings,
                    serviceProvider.GetRequiredService<ITenantContextCapabilityProvider>()));
            services.AddSingleton<IBrokerCommandLifecycleStore>(provider =>
                provider.GetRequiredService<GatewayBrokerCommandLifecycleStoreOwner>().Store);
            services.AddSingleton<BrokerCommandCoordinator>(provider => new(
                provider.GetRequiredService<IBrokerCommandLifecycleStore>(),
                provider.GetRequiredService<IMt5Gateway>(),
                settings.LeaseTrustVerifier!,
                provider.GetRequiredService<BrokerCommandCoordinatorOptions>(),
                provider.GetRequiredService<TimeProvider>()));
            services.AddSingleton<IBrokerCommandCoordinatorRunner,
                BrokerCommandCoordinatorRunner>();
            services.AddSingleton<IBrokerCommandClaimRecoveryWaiter,
                BrokerCommandClaimRecoveryWaiter>();
            services.AddSingleton<IBrokerCommandOneShotExecutor>(provider =>
                new BrokerCommandOneShotExecutor(
                settings,
                provider.GetRequiredService<IBrokerCommandCoordinatorRunner>(),
                provider.GetRequiredService<IBrokerCommandClaimRecoveryWaiter>()));
        }
        else
        {
            services.AddSingleton<IBrokerCommandOneShotExecutor,
                DisabledBrokerCommandOneShotExecutor>();
        }

        services.AddHostedService<BrokerCommandOneShotWorker>();
        return services;
    }
}

/// <summary>
/// Owns the only gateway-runtime database object in this host. The same
/// least-privilege connection is supplied to both legacy constructor slots,
/// while the raw store is kept local and never registered for resolution.
/// Database permissions still make its authorization method unusable.
/// </summary>
internal sealed class GatewayBrokerCommandLifecycleStoreOwner : IAsyncDisposable
{
    private readonly PostgresDatabase database;

    public GatewayBrokerCommandLifecycleStoreOwner(
        BrokerCommandOneShotSettings settings,
        ITenantContextCapabilityProvider tenantContextCapabilityProvider)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(tenantContextCapabilityProvider);
        if (!settings.Enabled
            || settings.GatewayRuntimeConnectionString is null
            || settings.LeaseTrustVerifier is null)
        {
            throw new InvalidOperationException(
                "The gateway lifecycle store cannot be created while disabled.");
        }

        database = new PostgresDatabase(
            settings.GatewayRuntimeConnectionString,
            PostgresDatabaseUsage.Runtime,
            tenantContextCapabilityProvider);
        var durableStore = new PostgresBrokerCommandStore(
            database,
            database,
            settings.LeaseTrustVerifier);
        Store = new PostgresBrokerCommandLifecycleStore(durableStore);
    }

    internal IBrokerCommandLifecycleStore Store { get; }

    public ValueTask DisposeAsync() => database.DisposeAsync();
}
