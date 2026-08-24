using System.Globalization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using YO4X.Trading.Abstractions;
using YO4X.Trading.ProcessIsolation;

namespace YO4X.GatewayHost;

internal static class Mt5ProcessBoundaryRegistration
{
    internal const string SectionName = "Mt5ProcessBoundary";

    internal static IServiceCollection AddMt5ProcessBoundary(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        IsolatedBrokerProcessOptions options = Load(
            configuration.GetSection(SectionName));
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IMt5Gateway>(provider =>
            new IsolatedMt5ProcessGateway(
                provider.GetRequiredService<IsolatedBrokerProcessOptions>(),
                provider.GetRequiredService<TimeProvider>()));
        return services;
    }

    private static IsolatedBrokerProcessOptions Load(IConfigurationSection section)
    {
        try
        {
            string? enabledValue = section["Enabled"];
            if (enabledValue is null)
            {
                return IsolatedBrokerProcessOptions.Disabled;
            }

            if (!bool.TryParse(enabledValue, out bool enabled))
            {
                throw new FormatException("The process boundary enabled flag is invalid.");
            }

            if (!enabled)
            {
                return IsolatedBrokerProcessOptions.Disabled;
            }

            string executablePath = ReadRequired(section, "WorkerExecutablePath", 4096);
            string executableSha256 = ReadRequired(section, "WorkerExecutableSha256", 64);
            string manifestPath = ReadRequired(section, "WorkerLaunchManifestPath", 4096);
            string manifestSha256 = ReadRequired(
                section,
                "WorkerLaunchManifestSha256",
                64);
            TimeSpan operationTimeout = ReadDuration(
                section,
                "OperationTimeout",
                TimeSpan.FromSeconds(5));
            TimeSpan shutdownTimeout = ReadDuration(
                section,
                "ShutdownTimeout",
                TimeSpan.FromSeconds(2));
            return new IsolatedBrokerProcessOptions(
                executablePath,
                executableSha256,
                manifestPath,
                manifestSha256,
                operationTimeout,
                shutdownTimeout);
        }
        catch (Exception exception) when (exception is ArgumentException
            or FormatException
            or IOException
            or UnauthorizedAccessException)
        {
            // Paths and deployment details are never included in the startup error.
            throw new InvalidOperationException(
                "The MT5 process boundary configuration is invalid.");
        }
    }

    private static string ReadRequired(
        IConfiguration section,
        string name,
        int maximumLength)
    {
        string? value = section[name];
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maximumLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new FormatException("A process boundary value is invalid.");
        }

        return value;
    }

    private static TimeSpan ReadDuration(
        IConfiguration section,
        string name,
        TimeSpan defaultValue)
    {
        string? value = section[name];
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out TimeSpan parsed))
        {
            throw new FormatException("A process boundary duration is invalid.");
        }

        return parsed;
    }
}
