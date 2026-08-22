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
            || !TryReadExactHttpsOrigin(
                configuration["SecretIngestion:ApprovedClientOrigin"],
                out Uri? approvedClientOrigin))
        {
            return services;
        }

        PostgresDatabase? database = null;
        try
        {
            database = new PostgresDatabase(connectionString, PostgresDatabaseUsage.Runtime);
            PostgresDatabase registeredDatabase = database;
            var options = new SecretIngestionPostgresOptions(
                RequiredRole,
                approvedClientOrigin!,
                RequireTls: true);

            services.TryAddSingleton(_ => registeredDatabase);
            services.TryAddSingleton(options);
            services.TryAddSingleton<PostgresCredentialIngestionGrantStore>();
            services.TryAddSingleton<RoleBoundCredentialIngestionGrantStore>();
            services.TryAddSingleton<ICredentialIngestionGrantStore>(serviceProvider =>
                serviceProvider.GetRequiredService<RoleBoundCredentialIngestionGrantStore>());
            services.TryAddScoped<ICredentialIngestionProcessor, CredentialIngestionProcessor>();
            database = null;
            return services;
        }
        catch (ArgumentException)
        {
            return services;
        }
        finally
        {
            if (database is not null)
            {
                database.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
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
                || builder.SslMode != SslMode.VerifyFull
                || builder.IncludeErrorDetail
                || !string.IsNullOrWhiteSpace(builder.Options)
                || !string.IsNullOrWhiteSpace(builder.SearchPath))
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
