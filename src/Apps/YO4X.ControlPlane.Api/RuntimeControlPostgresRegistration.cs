using System.Globalization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using YO4X.ControlPlane.Application;
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

        var database = new RuntimePostgresDatabase(connectionString);
        services.TryAddSingleton(options);
        services.TryAddSingleton(database);
        if (TryReadRuntimeConnectionString(
                configuration.GetConnectionString("RuntimeEvidencePostgres"),
                "yo4x_runtime_evidence",
                environment.IsDevelopment(),
                out string evidenceConnectionString))
        {
            services.TryAddSingleton(new RuntimeEvidencePostgresDatabase(evidenceConnectionString));
        }

        services.TryAddScoped<IRuntimeControlPlaneApplication, PostgresRuntimeControlPlaneApplication>();
        return services;
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
