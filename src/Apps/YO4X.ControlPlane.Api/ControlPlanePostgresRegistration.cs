using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using YO4X.ControlPlane.Application;
using YO4X.ControlPlane.Postgres;
using YO4X.Persistence.Postgres;

namespace YO4X.ControlPlane.Api;

internal static class ControlPlanePostgresRegistration
{
    private const int CredentialProofKeyBytes = 32;

    public static IServiceCollection TryAddControlPlanePostgres(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment? environment = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        byte[]? proofKeyBytes = null;
        byte[]? strategyImportKeyBytes = null;
        Dictionary<string, byte[]>? policyPublicKeys = null;
        PostgresDatabase? database = null;
        CredentialProofKey? proofKey = null;
        StrategyImportProofKey? strategyImportKey = null;
        PolicySignatureTrustStore? policyTrustStore = null;
        try
        {
            string? proofKeyBase64 = configuration["SecretIngestion:CredentialProofKeyBase64"];
            if (!TryReadRuntimeConnectionString(
                    configuration.GetConnectionString("Postgres"),
                    environment?.IsDevelopment() ?? true,
                    out string connectionString)
                || string.IsNullOrWhiteSpace(proofKeyBase64)
                || !TryReadExactHttpsOrigin(configuration["SecretIngestion:Origin"], out Uri? ingestionOrigin)
                || !TryReadExactHttpsOrigin(
                    configuration["SecretIngestion:ApprovedClientOrigin"],
                    out Uri? approvedClientOrigin)
                || !TryReadDuration(
                    configuration["SecretIngestion:GrantLifetime"],
                    TimeSpan.FromMinutes(5),
                    out TimeSpan grantLifetime)
                || !TryReadDuration(
                    configuration["Conversion:ImportJobLifetime"],
                    TimeSpan.FromMinutes(10),
                    out TimeSpan importJobLifetime)
                || !TryReadDuration(
                    configuration["U0:BrokerCapabilityMaximumAge"],
                    TimeSpan.FromMinutes(15),
                    out TimeSpan capabilityMaximumAge)
                || !TryReadDuration(
                    configuration["U0:CompatibilityEvidenceMaximumAge"],
                    TimeSpan.FromHours(24),
                    out TimeSpan compatibilityMaximumAge)
                || !TryReadDuration(
                    configuration["U0:EvidenceFutureClockSkew"],
                    TimeSpan.FromSeconds(30),
                    out TimeSpan evidenceFutureClockSkew)
                || !Guid.TryParse(
                    configuration["U0:ApprovedBrokerProfileId"],
                    out Guid approvedBrokerProfileId)
                || approvedBrokerProfileId == Guid.Empty
                || !TryReadPolicyTrustKeys(configuration, out policyPublicKeys))
            {
                return services;
            }

            try
            {
                proofKeyBytes = Convert.FromBase64String(proofKeyBase64);
            }
            catch (FormatException)
            {
                return services;
            }

            if (proofKeyBytes.Length != CredentialProofKeyBytes
                || proofKeyBytes.All(static value => value == 0))
            {
                return services;
            }

            string? strategyImportKeyBase64 = configuration["Conversion:ImportProofKeyBase64"];
            if (!string.IsNullOrWhiteSpace(strategyImportKeyBase64))
            {
                try
                {
                    strategyImportKeyBytes = Convert.FromBase64String(strategyImportKeyBase64);
                }
                catch (FormatException)
                {
                    return services;
                }

                if (strategyImportKeyBytes.Length != CredentialProofKeyBytes
                    || strategyImportKeyBytes.All(static value => value == 0))
                {
                    return services;
                }
            }

            var options = new ControlPlanePostgresOptions
            {
                ApprovedGatewayDigest = configuration["U0:ApprovedGatewayDigest"]?.Trim(),
                ApprovedRegion = configuration["U0:ApprovedRegion"]?.Trim(),
                ApprovedBrokerServer = configuration["U0:ApprovedBrokerServer"]?.Trim(),
                ApprovedBrokerProfileId = approvedBrokerProfileId,
                ApprovedRuntimeImageDigest = configuration["RuntimePostgres:ApprovedRuntimeImageDigest"]?.Trim(),
                BrokerCapabilityMaximumAge = capabilityMaximumAge,
                CompatibilityEvidenceMaximumAge = compatibilityMaximumAge,
                EvidenceFutureClockSkew = evidenceFutureClockSkew,
                SecretIngestionOrigin = ingestionOrigin,
                ApprovedCredentialClientOrigin = approvedClientOrigin,
                IngestionGrantLifetime = grantLifetime,
                StrategyImportJobLifetime = importJobLifetime
            };
            options.Validate();

            database = new PostgresDatabase(connectionString, PostgresDatabaseUsage.Runtime);
            proofKey = new CredentialProofKey(proofKeyBytes);
            if (strategyImportKeyBytes is not null)
            {
                strategyImportKey = new StrategyImportProofKey(strategyImportKeyBytes);
            }
            policyTrustStore = new PolicySignatureTrustStore(policyPublicKeys);

            PostgresDatabase registeredDatabase = database;
            CredentialProofKey registeredProofKey = proofKey;
            StrategyImportProofKey? registeredStrategyImportKey = strategyImportKey;
            PolicySignatureTrustStore registeredPolicyTrustStore = policyTrustStore;
            services.TryAddSingleton(_ => registeredDatabase);
            services.TryAddSingleton(options);
            services.TryAddSingleton(_ => registeredProofKey);
            services.TryAddSingleton(_ => registeredPolicyTrustStore);
            services.TryAddSingleton<CredentialIngestionProofIssuer>();
            if (registeredStrategyImportKey is not null)
            {
                services.TryAddSingleton(_ => registeredStrategyImportKey);
                services.TryAddSingleton<StrategyImportProofIssuer>();
            }
            services.TryAddScoped<IControlPlaneApplication, PostgresControlPlaneApplication>();

            database = null;
            proofKey = null;
            strategyImportKey = null;
            policyTrustStore = null;
            return services;
        }
        catch (ArgumentException)
        {
            return services;
        }
        catch (InvalidOperationException)
        {
            return services;
        }
        catch (CryptographicException)
        {
            return services;
        }
        finally
        {
            if (proofKeyBytes is not null)
            {
                CryptographicOperations.ZeroMemory(proofKeyBytes);
            }

            if (strategyImportKeyBytes is not null)
            {
                CryptographicOperations.ZeroMemory(strategyImportKeyBytes);
            }

            if (policyPublicKeys is not null)
            {
                foreach (byte[] encodedKey in policyPublicKeys.Values)
                {
                    CryptographicOperations.ZeroMemory(encodedKey);
                }
            }

            proofKey?.Dispose();
            strategyImportKey?.Dispose();
            policyTrustStore?.Dispose();
            if (database is not null)
            {
                database.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
    }

    private static bool TryReadRuntimeConnectionString(
        string? value,
        bool allowInsecureDevelopment,
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
                || !string.Equals(builder.Username, "yo4x_control_api", StringComparison.Ordinal)
                || builder.IncludeErrorDetail
                || !string.IsNullOrWhiteSpace(builder.Options)
                || !string.IsNullOrWhiteSpace(builder.SearchPath)
                || !allowInsecureDevelopment && builder.SslMode != SslMode.VerifyFull)
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

    private static bool TryReadExactHttpsOrigin(string? value, out Uri? origin)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out origin)
            || origin.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(origin.Host)
            || !string.IsNullOrEmpty(origin.UserInfo)
            || origin.AbsolutePath != "/"
            || !string.IsNullOrEmpty(origin.Query)
            || !string.IsNullOrEmpty(origin.Fragment))
        {
            origin = null;
            return false;
        }

        return true;
    }

    private static bool TryReadDuration(string? value, TimeSpan defaultValue, out TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            duration = defaultValue;
            return true;
        }

        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out duration);
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
        finally
        {
            if (keys.Count == 0)
            {
                foreach (byte[] encodedKey in keys.Values)
                {
                    CryptographicOperations.ZeroMemory(encodedKey);
                }
            }
        }
    }
}
