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
    private const int ProofKeyBytes = 32;
    private static readonly TimeSpan PreviousProofKeyMinimumRetention =
        ControlPlanePostgresOptions.PreviousProofKeyMinimumStartupRetention;

    public static IServiceCollection TryAddControlPlanePostgres(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment? environment = null) =>
        TryAddControlPlanePostgres(
            services,
            configuration,
            environment,
            TimeProvider.System);

    internal static IServiceCollection TryAddControlPlanePostgres(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment? environment,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(timeProvider);

        byte[]? proofKeyBytes = null;
        byte[]? previousProofKeyBytes = null;
        byte[]? strategyImportKeyBytes = null;
        byte[]? previousStrategyImportKeyBytes = null;
        Dictionary<string, byte[]>? policyPublicKeys = null;
        CredentialProofKeyRing? proofKeyRing = null;
        StrategyImportProofKeyRing? strategyImportKeyRing = null;
        PolicySignatureTrustStore? policyTrustStore = null;
        try
        {
            string? proofKeyBase64 = configuration["SecretIngestion:CredentialProofKeyBase64"];
            if (!TryReadRuntimeConnectionString(
                    configuration.GetConnectionString("Postgres"),
                    environment?.IsDevelopment() ?? false,
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

            if (!TryReadProofKey(proofKeyBase64, out proofKeyBytes))
            {
                return services;
            }

            string? strategyImportKeyBase64 = configuration["Conversion:ImportProofKeyBase64"];
            if (string.IsNullOrWhiteSpace(strategyImportKeyBase64))
            {
                return services;
            }

            if (!TryReadProofKey(strategyImportKeyBase64, out strategyImportKeyBytes))
            {
                return services;
            }

            DateTimeOffset startupNow = timeProvider.GetUtcNow().ToUniversalTime();
            if (!TryReadPreviousProofKey(
                    configuration,
                    "SecretIngestion:PreviousCredentialProofKeyBase64",
                    "SecretIngestion:PreviousCredentialProofKeyRetainUntilUtc",
                    startupNow,
                    PreviousProofKeyMinimumRetention,
                    out previousProofKeyBytes,
                    out DateTimeOffset? previousProofKeyRetainUntil)
                || !TryReadPreviousProofKey(
                    configuration,
                    "Conversion:PreviousImportProofKeyBase64",
                    "Conversion:PreviousImportProofKeyRetainUntilUtc",
                    startupNow,
                    PreviousProofKeyMinimumRetention,
                    out previousStrategyImportKeyBytes,
                    out DateTimeOffset? previousStrategyImportKeyRetainUntil))
            {
                return services;
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

            if (!PostgresDatabaseEndpoint.TryParse(
                    connectionString,
                    out PostgresDatabaseEndpoint? runtimeEndpoint)
                || !TenantContextCapabilityRegistration.TryAdd(
                    services,
                    configuration,
                    runtimeEndpoint!))
            {
                return services;
            }

            proofKeyRing = new CredentialProofKeyRing(
                proofKeyBytes,
                previousProofKeyBytes,
                previousProofKeyRetainUntil,
                timeProvider);
            strategyImportKeyRing = new StrategyImportProofKeyRing(
                strategyImportKeyBytes,
                previousStrategyImportKeyBytes,
                previousStrategyImportKeyRetainUntil,
                timeProvider);
            policyTrustStore = new PolicySignatureTrustStore(policyPublicKeys);

            CredentialProofKeyRing registeredProofKeyRing = proofKeyRing;
            StrategyImportProofKeyRing registeredStrategyImportKeyRing = strategyImportKeyRing;
            PolicySignatureTrustStore registeredPolicyTrustStore = policyTrustStore;
            services.TryAddSingleton(serviceProvider => new PostgresDatabase(
                connectionString,
                PostgresDatabaseUsage.Runtime,
                serviceProvider.GetRequiredService<ITenantContextCapabilityProvider>(),
                allowInsecureLoopbackForDevelopment: environment?.IsDevelopment() ?? false));
            services.TryAddSingleton(options);
            services.TryAddSingleton(_ => registeredProofKeyRing);
            services.TryAddSingleton(_ => registeredPolicyTrustStore);
            services.TryAddSingleton<CredentialIngestionProofIssuer>();
            services.TryAddSingleton(_ => registeredStrategyImportKeyRing);
            services.TryAddSingleton<StrategyImportProofIssuer>();
            services.TryAddScoped<IControlPlaneApplication, PostgresControlPlaneApplication>();
            services.TryAddScoped<IFrontendProjectionApplication, PostgresFrontendProjections>();

            proofKeyRing = null;
            strategyImportKeyRing = null;
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

            if (previousProofKeyBytes is not null)
            {
                CryptographicOperations.ZeroMemory(previousProofKeyBytes);
            }

            if (previousStrategyImportKeyBytes is not null)
            {
                CryptographicOperations.ZeroMemory(previousStrategyImportKeyBytes);
            }

            if (policyPublicKeys is not null)
            {
                foreach (byte[] encodedKey in policyPublicKeys.Values)
                {
                    CryptographicOperations.ZeroMemory(encodedKey);
                }
            }

            proofKeyRing?.Dispose();
            strategyImportKeyRing?.Dispose();
            policyTrustStore?.Dispose();
        }
    }

    private static bool TryReadProofKey(string? value, out byte[] keyBytes)
    {
        keyBytes = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            keyBytes = Convert.FromBase64String(value);
            if (keyBytes.Length != ProofKeyBytes
                || keyBytes.All(static item => item == 0))
            {
                CryptographicOperations.ZeroMemory(keyBytes);
                keyBytes = [];
                return false;
            }

            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool TryReadPreviousProofKey(
        IConfiguration configuration,
        string keyConfigurationName,
        string retainUntilConfigurationName,
        DateTimeOffset startupNow,
        TimeSpan minimumRequiredRetention,
        out byte[]? keyBytes,
        out DateTimeOffset? retainUntil)
    {
        keyBytes = null;
        retainUntil = null;
        string? encodedKey = configuration[keyConfigurationName];
        string? retainUntilText = configuration[retainUntilConfigurationName];
        bool hasKey = !string.IsNullOrWhiteSpace(encodedKey);
        bool hasRetainUntil = !string.IsNullOrWhiteSpace(retainUntilText);
        if (!hasKey && !hasRetainUntil)
        {
            return true;
        }

        if (!hasKey
            || !hasRetainUntil
            || retainUntilText!.Length > 64
            || !string.Equals(retainUntilText, retainUntilText.Trim(), StringComparison.Ordinal)
            || !DateTimeOffset.TryParse(
                retainUntilText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset parsedRetainUntil)
            || parsedRetainUntil.Offset != TimeSpan.Zero
            || parsedRetainUntil <= startupNow.Add(minimumRequiredRetention)
            || !TryReadProofKey(encodedKey, out byte[] parsedKey))
        {
            return false;
        }

        keyBytes = parsedKey;
        retainUntil = parsedRetainUntil;
        return true;
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
                || !PostgresRuntimeConnectionPolicy.HasSafeSessionConfiguration(builder)
                || !PostgresRuntimeConnectionPolicy.HasRequiredTransport(
                    builder,
                    allowInsecureDevelopment))
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
