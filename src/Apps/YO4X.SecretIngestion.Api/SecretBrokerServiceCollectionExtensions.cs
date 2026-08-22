using Microsoft.Extensions.DependencyInjection.Extensions;
using YO4X.SecretCoordination;

namespace YO4X.SecretIngestion.Api;

/// <summary>
/// Explicit composition hook for an externally supplied write-only secret
/// broker. This application deliberately ships without a concrete provider.
/// </summary>
public static class SecretBrokerServiceCollectionExtensions
{
    public static IServiceCollection AddExternalWriteOnlySecretBroker<TBroker>(
        this IServiceCollection services)
        where TBroker : class, IWriteOnlySecretBroker
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IWriteOnlySecretBroker, TBroker>();
        return services;
    }
}
