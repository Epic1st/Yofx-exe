using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using YO4X.ControlPlane.Application;
using YO4X.Persistence.Postgres;
using YO4X.RuntimeControl.Postgres;

namespace YO4X.ControlPlane.Api;

internal static class RuntimeControlPostgresRegistration
{
    public static IServiceCollection TryAddRuntimeControlPostgres(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        if (!TryReadRuntimeConnectionString(
                configuration.GetConnectionString("RuntimePostgres"),
                "yo4x_worker",
                environment.IsDevelopment(),
                out string connectionString)
            || !PostgresDatabaseEndpoint.TryParse(
                connectionString,
                out PostgresDatabaseEndpoint? runtimeEndpoint)
            || !TenantContextCapabilityRegistration.TryAdd(
                services,
                configuration,
                runtimeEndpoint!)
            || !TryReadDuration(
                configuration["RuntimePostgres:AssignmentLifetime"],
                TimeSpan.FromMinutes(10),
                out TimeSpan assignmentLifetime)
            || !TryReadDuration(
                configuration["RuntimePostgres:MaximumEvidenceAge"],
                TimeSpan.FromMinutes(5),
                out TimeSpan maximumEvidenceAge)
            || !TryReadDuration(
                configuration["RuntimePostgres:MaximumFutureClockSkew"],
                TimeSpan.FromMinutes(1),
                out TimeSpan maximumFutureClockSkew)
            || !TryReadDuration(
                configuration["RuntimePostgres:MaximumLeaseLifetime"],
                TimeSpan.FromMinutes(10),
                out TimeSpan maximumLeaseLifetime)
            || !TryReadDuration(
                configuration["RuntimePostgres:MaximumLeaseGracePeriod"],
                TimeSpan.FromMinutes(15),
                out TimeSpan maximumLeaseGracePeriod)
            || !TryReadInteger(
                configuration["RuntimePostgres:MaximumEventPayloadBytes"],
                64 * 1024,
                out int maximumEventPayloadBytes))
        {
            return services;
        }

        var options = new RuntimeControlPostgresOptions
        {
            ApprovedRuntimeImageDigest = configuration["RuntimePostgres:ApprovedRuntimeImageDigest"]?.Trim(),
            AssignmentLifetime = assignmentLifetime,
            MaximumEvidenceAge = maximumEvidenceAge,
            MaximumFutureClockSkew = maximumFutureClockSkew,
            MaximumLeaseLifetime = maximumLeaseLifetime,
            MaximumLeaseGracePeriod = maximumLeaseGracePeriod,
            MaximumEventPayloadBytes = maximumEventPayloadBytes
        };
        try
        {
            options.Validate();
        }
        catch (InvalidOperationException)
        {
            return services;
        }

        services.TryAddSingleton(options);
        services.TryAddSingleton(serviceProvider => new RuntimePostgresDatabase(
            connectionString,
            serviceProvider.GetRequiredService<ITenantContextCapabilityProvider>(),
            allowInsecureLoopbackForDevelopment: environment.IsDevelopment()));
        bool allowInsecureLoopbackForDevelopment = environment.IsDevelopment();
        if (!TryReadRuntimeConnectionString(
                configuration.GetConnectionString("RuntimeEvidencePostgres"),
                "yo4x_runtime_evidence",
                allowInsecureLoopbackForDevelopment,
                out string evidenceConnectionString)
            || !PostgresDatabaseEndpoint.TryParse(
                evidenceConnectionString,
                out PostgresDatabaseEndpoint? evidenceEndpoint)
            || !TenantContextCapabilityRegistration.TryAdd(
                services,
                configuration,
                evidenceEndpoint!))
        {
            return services;
        }

        services.TryAddSingleton(serviceProvider => new RuntimeEvidencePostgresDatabase(
            evidenceConnectionString,
            serviceProvider.GetRequiredService<ITenantContextCapabilityProvider>(),
            allowInsecureLoopbackForDevelopment));

        if (TryLoadExecutionLeaseSigner(
                configuration,
                out P256ExecutionLeaseSigningProvider? signingProvider))
        {
            services.TryAddSingleton<IExecutionLeaseSigningProvider>(signingProvider!);
            services.TryAddScoped<IExecutionEntitlementProvider, PostgresExecutionEntitlementProvider>();
        }

        services.TryAddScoped<IRuntimeControlPlaneApplication, PostgresRuntimeControlPlaneApplication>();
        return services;
    }

    private static bool TryLoadExecutionLeaseSigner(
        IConfiguration configuration,
        out P256ExecutionLeaseSigningProvider? provider)
    {
        provider = null;
        string? keyId = configuration["ExecutionLeases:SigningKeyId"]?.Trim();
        string? keyPathValue = configuration["ExecutionLeases:PrivateKeyPkcs8File"]?.Trim();
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(keyPathValue))
            return false;

        string keyPath = Path.GetFullPath(keyPathValue);
        if (!File.Exists(keyPath))
            return false;

        byte[] encoded = File.ReadAllBytes(keyPath);
        byte[] keyBytes = [];
        try
        {
            string canonical = System.Text.Encoding.ASCII.GetString(encoded).Trim();
            keyBytes = Convert.FromBase64String(canonical);
            if (!string.Equals(Convert.ToBase64String(keyBytes), canonical, StringComparison.Ordinal))
                return false;
            provider = new P256ExecutionLeaseSigningProvider(keyId, keyBytes);
            return true;
        }
        catch (Exception exception) when (exception is FormatException
            or ArgumentException
            or CryptographicException
            or IOException)
        {
            provider?.Dispose();
            provider = null;
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encoded);
            CryptographicOperations.ZeroMemory(keyBytes);
        }
    }

    private static bool TryReadRuntimeConnectionString(
        string? value,
        string requiredRole,
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
                || !string.Equals(builder.Username, requiredRole, StringComparison.Ordinal)
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

    private static bool TryReadDuration(string? value, TimeSpan defaultValue, out TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            duration = defaultValue;
            return true;
        }

        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out duration);
    }

    private static bool TryReadInteger(string? value, int defaultValue, out int parsed)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            parsed = defaultValue;
            return true;
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed);
    }
}
