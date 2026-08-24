using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using YO4X.Persistence.Postgres;
using YO4X.SecretCoordination;

namespace YO4X.SecretIngestion.Api;

internal static class SecretIngestionPostgresRegistration
{
    internal const string RequiredRole = "yo4x_secret_ingestion";

    public static IServiceCollection TryAddSecretIngestionPostgres(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (!services.Any(descriptor => descriptor.ServiceType == typeof(IWriteOnlySecretBroker))
            || !TryReadRuntimeConnectionString(
                configuration.GetConnectionString("Postgres"),
                out string connectionString)
            || !PostgresDatabaseEndpoint.TryParse(
                connectionString,
                out PostgresDatabaseEndpoint? runtimeEndpoint)
            || !TryReadExactHttpsOrigin(
                configuration["SecretIngestion:ApprovedClientOrigin"],
                out Uri? approvedClientOrigin)
            || !TryAddTenantContextCapabilityProvider(
                services,
                configuration,
                runtimeEndpoint!))
        {
            return services;
        }

        try
        {
            var options = new SecretIngestionPostgresOptions(
                RequiredRole,
                approvedClientOrigin!,
                RequireTls: true);

            services.TryAddSingleton(serviceProvider => new PostgresDatabase(
                connectionString,
                PostgresDatabaseUsage.Runtime,
                serviceProvider.GetRequiredService<ITenantContextCapabilityProvider>()));
            services.TryAddSingleton(options);
            services.TryAddSingleton<PostgresCredentialIngestionGrantStore>();
            services.TryAddSingleton<RoleBoundCredentialIngestionGrantStore>();
            services.TryAddSingleton<ICredentialIngestionGrantStore>(serviceProvider =>
                serviceProvider.GetRequiredService<RoleBoundCredentialIngestionGrantStore>());
            services.TryAddScoped<ICredentialIngestionProcessor, CredentialIngestionProcessor>();
            return services;
        }
        catch (ArgumentException)
        {
            return services;
        }
    }

    private static bool TryAddTenantContextCapabilityProvider(
        IServiceCollection services,
        IConfiguration configuration,
        PostgresDatabaseEndpoint requiredEndpoint)
    {
        ServiceDescriptor? existing = services.FirstOrDefault(static descriptor =>
            descriptor.ServiceType == typeof(ITenantContextCapabilityProvider));
        if (existing?.ImplementationInstance is ITenantContextCapabilityProvider provider)
        {
            return provider.Endpoint == requiredEndpoint;
        }

        if (existing is not null)
        {
            return true;
        }

        if (!PostgresTenantContextCapabilityProvider.TryNormalizeIssuerConnectionString(
                configuration.GetConnectionString("ContextIssuer"),
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
        return true;
    }

    internal static bool TryReadRuntimeConnectionString(
        string? value,
        out string connectionString)
    {
        connectionString = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(value);
            if (string.IsNullOrWhiteSpace(builder.Host)
                || string.IsNullOrWhiteSpace(builder.Database)
                || !string.Equals(builder.Username, RequiredRole, StringComparison.Ordinal)
                || !PostgresRuntimeConnectionPolicy.HasSafeSessionConfiguration(builder)
                || !PostgresRuntimeConnectionPolicy.HasRequiredTransport(
                    builder,
                    allowInsecureLoopbackForDevelopment: false))
            {
                return false;
            }

            connectionString = builder.ConnectionString;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    internal static bool TryReadExactHttpsOrigin(string? value, out Uri? origin)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out origin)
            || origin.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(origin.Host)
            || !string.IsNullOrEmpty(origin.UserInfo)
            || origin.AbsolutePath != "/"
            || !string.IsNullOrEmpty(origin.Query)
            || !string.IsNullOrEmpty(origin.Fragment)
            || !string.Equals(origin.GetLeftPart(UriPartial.Authority), value, StringComparison.Ordinal))
        {
            origin = null;
            return false;
        }

        return true;
    }
}
