using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using System.Security.Cryptography;
using YO4X.ControlPlane.Workers.Outbox;
using YO4X.Persistence.Postgres;

namespace YO4X.ControlPlane.Workers.Operations;

internal static class PostgresWorkerRegistration
{
    public static IServiceCollection TryAddWorkerPostgres(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!TryReadRuntimeConnectionString(
            configuration.GetConnectionString("Postgres"),
            out string connectionString))
        {
            return services;
        }

        PostgresDatabase? database = null;
        WorkerPolicySignatureTrustStore? policyTrustStore = null;
        Dictionary<string, byte[]>? policyPublicKeys = null;
        try
        {
            if (!TryReadPolicyTrustKeys(configuration, out policyPublicKeys))
            {
                return services;
            }

            database = new PostgresDatabase(connectionString, PostgresDatabaseUsage.Runtime);
            policyTrustStore = new WorkerPolicySignatureTrustStore(policyPublicKeys);
            PostgresDatabase registeredDatabase = database;
            WorkerPolicySignatureTrustStore registeredPolicyTrustStore = policyTrustStore;
            services.TryAddSingleton(_ => registeredDatabase);
            services.TryAddSingleton(_ => registeredPolicyTrustStore);
            services.TryAddSingleton<PostgresWorkerReadiness>();
            services.TryAddSingleton<PostgresWorkerTenantCatalog>();
            services.TryAddSingleton<IPostgresOutboxStore, PostgresWorkerOutboxStore>();
            services.TryAddSingleton<IUserOperationWorkStore, PostgresUserOperationWorkStore>();
            services.TryAddSingleton<ICredentialGrantExpiryStore, PostgresCredentialGrantExpiryStore>();
            services.TryAddSingleton<IDeploymentProjectionStore, PostgresDeploymentProjectionStore>();
            database = null;
            policyTrustStore = null;
            return services;
        }
        catch (ArgumentException)
        {
            return services;
        }
        catch (CryptographicException)
        {
            return services;
        }
        finally
        {
            if (policyPublicKeys is not null)
            {
                foreach (byte[] encodedKey in policyPublicKeys.Values)
                {
                    CryptographicOperations.ZeroMemory(encodedKey);
                }
            }

            policyTrustStore?.Dispose();
            if (database is not null)
            {
                database.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
    }

    private static bool TryReadPolicyTrustKeys(
        IConfiguration configuration,
        out Dictionary<string, byte[]> keys)
    {
        keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        try
        {
            foreach (IConfigurationSection child in configuration
                .GetSection("PolicyTrust:EcdsaP256Keys")
                .GetChildren())
            {
                if (string.IsNullOrWhiteSpace(child.Value)
                    || keys.Count >= 32
                    || !keys.TryAdd(child.Key, Convert.FromBase64String(child.Value)))
                {
                    return false;
                }
            }

            return keys.Count != 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    internal static bool TryReadRuntimeConnectionString(string? value, out string connectionString)
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
                || !string.Equals(builder.Username, WorkerDatabaseIdentity.RequiredRole, StringComparison.Ordinal)
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
}
