using Microsoft.Extensions.DependencyInjection.Extensions;
using YO4X.Persistence.Postgres;

namespace YO4X.ControlPlane.Api;

internal static class TenantContextCapabilityRegistration
{
    private const string IssuerConnectionName = "ContextIssuer";

    public static bool TryAdd(
        IServiceCollection services,
        IConfiguration configuration,
        PostgresDatabaseEndpoint requiredEndpoint)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(requiredEndpoint);

        TenantContextCapabilityEndpointRegistration? existingEndpoint = services
            .Where(static descriptor =>
                descriptor.ServiceType == typeof(TenantContextCapabilityEndpointRegistration))
            .Select(static descriptor =>
                descriptor.ImplementationInstance as TenantContextCapabilityEndpointRegistration)
            .FirstOrDefault(static registration => registration is not null);
        if (existingEndpoint is not null)
        {
            return existingEndpoint.Endpoint == requiredEndpoint;
        }

        ServiceDescriptor? existingProvider = services.FirstOrDefault(static descriptor =>
            descriptor.ServiceType == typeof(ITenantContextCapabilityProvider));
        if (existingProvider?.ImplementationInstance is ITenantContextCapabilityProvider provider)
        {
            return provider.Endpoint == requiredEndpoint;
        }

        if (existingProvider is not null)
        {
            return true;
        }

        if (!PostgresTenantContextCapabilityProvider.TryNormalizeIssuerConnectionString(
                configuration.GetConnectionString(IssuerConnectionName),
                requireTls: true,
                out string issuerConnectionString))
        {
            return false;
        }

        if (!PostgresDatabaseEndpoint.TryParse(
                issuerConnectionString,
                out PostgresDatabaseEndpoint? issuerEndpoint)
            || issuerEndpoint != requiredEndpoint)
        {
            return false;
        }

        services.TryAddSingleton<ITenantContextCapabilityProvider>(_ =>
            new PostgresTenantContextCapabilityProvider(issuerConnectionString));
        services.TryAddSingleton(
            new TenantContextCapabilityEndpointRegistration(issuerEndpoint));
        return true;
    }

    private sealed record TenantContextCapabilityEndpointRegistration(
        PostgresDatabaseEndpoint Endpoint);
}
