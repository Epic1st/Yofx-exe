using System.Globalization;
using System.Security.Cryptography;
using Npgsql;
using YO4X.Persistence.Postgres;
using YO4X.Tenancy;
using YO4X.Trading.Application;
using YO4X.Trading.Postgres;

namespace YO4X.GatewayHost;

internal sealed class BrokerCommandOneShotSettings
{
    internal const string SectionName = "BrokerCommandOneShot";
    internal const string GatewayRuntimeRole = "yo4x_gateway_runtime";

    private BrokerCommandOneShotSettings(bool enabled)
    {
        Enabled = enabled;
        CoordinatorOptions = new BrokerCommandCoordinatorOptions();
        OverallTimeout = TimeSpan.FromSeconds(45);
    }

    private BrokerCommandOneShotSettings(
        TenantExecutionContext executionContext,
        BrokerCommandReference commandReference,
        string gatewayRuntimeConnectionString,
        P256ExecutionLeaseTrustVerifier leaseTrustVerifier,
        BrokerCommandCoordinatorOptions coordinatorOptions,
        TimeSpan overallTimeout)
    {
        Enabled = true;
        ExecutionContext = executionContext;
        CommandReference = commandReference;
        GatewayRuntimeConnectionString = gatewayRuntimeConnectionString;
        LeaseTrustVerifier = leaseTrustVerifier;
        CoordinatorOptions = coordinatorOptions;
        OverallTimeout = overallTimeout;
    }

    internal bool Enabled { get; }

    internal TenantExecutionContext? ExecutionContext { get; }

    internal BrokerCommandReference? CommandReference { get; }

    internal string? GatewayRuntimeConnectionString { get; }

    internal P256ExecutionLeaseTrustVerifier? LeaseTrustVerifier { get; }

    internal BrokerCommandCoordinatorOptions CoordinatorOptions { get; }

    internal TimeSpan OverallTimeout { get; }

    internal static BrokerCommandOneShotSettings Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        try
        {
            return LoadCore(configuration.GetSection(SectionName));
        }
        catch (Exception exception) when (exception is ArgumentException
            or CryptographicException
            or FormatException
            or OverflowException)
        {
            // Configuration values can contain credentials and durable command
            // references. Never attach the original exception or value.
            throw new InvalidOperationException(
                "Broker command one-shot configuration is invalid.");
        }
    }

    private static BrokerCommandOneShotSettings LoadCore(IConfigurationSection section)
    {
        string? enabledValue = section["Enabled"];
        if (enabledValue is null)
        {
            return new BrokerCommandOneShotSettings(enabled: false);
        }

        if (!bool.TryParse(enabledValue, out bool enabled))
        {
            throw new FormatException("Enabled is not a Boolean value.");
        }

        if (!enabled)
        {
            return new BrokerCommandOneShotSettings(enabled: false);
        }

        Guid tenantId = ReadGuid(section, "TenantId");
        Guid gatewayWorkloadId = ReadGuid(section, "GatewayWorkloadId");
        Guid commandId = ReadGuid(section, "CommandId");
        var context = new TenantExecutionContext(
            tenantId,
            gatewayWorkloadId,
            commandId);
        var reference = new BrokerCommandReference(
            commandId,
            ReadRequired(section, "AuthorizationSha256", 64),
            ReadRequired(section, "LeaseTokenSha256", 64));
        string connectionString = ReadGatewayRuntimeConnectionString(
            ReadRequired(section, "GatewayRuntimeConnectionString", 4096));
        P256ExecutionLeaseTrustVerifier trustVerifier = ReadTrustVerifier(
            section.GetSection("TrustedLeasePublicKeys"));

        var coordinatorOptions = new BrokerCommandCoordinatorOptions
        {
            GatewaySendTimeout = ReadDuration(
                section,
                "GatewaySendTimeout",
                TimeSpan.FromMilliseconds(500)),
            GatewayReconciliationTimeout = ReadDuration(
                section,
                "GatewayReconciliationTimeout",
                TimeSpan.FromSeconds(10)),
            DurableWriteTimeout = ReadDuration(
                section,
                "DurableWriteTimeout",
                TimeSpan.FromSeconds(10)),
            AuthoritySafetyMargin = ReadDuration(
                section,
                "AuthoritySafetyMargin",
                TimeSpan.FromMilliseconds(100)),
            MinimumAuthorityWindow = ReadDuration(
                section,
                "MinimumAuthorityWindow",
                TimeSpan.FromMilliseconds(600))
        };
        coordinatorOptions.Validate();

        TimeSpan overallTimeout = ReadDuration(
            section,
            "OverallTimeout",
            TimeSpan.FromSeconds(45));
        TimeSpan minimumOverallTimeout = coordinatorOptions.GatewaySendTimeout
            + coordinatorOptions.GatewayReconciliationTimeout
            + coordinatorOptions.DurableWriteTimeout
            + coordinatorOptions.DurableWriteTimeout;
        if (overallTimeout < minimumOverallTimeout
            || overallTimeout > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentOutOfRangeException(
                nameof(section),
                "The bounded worker timeout is invalid.");
        }

        return new BrokerCommandOneShotSettings(
            context,
            reference,
            connectionString,
            trustVerifier,
            coordinatorOptions,
            overallTimeout);
    }

    private static Guid ReadGuid(IConfiguration section, string name)
    {
        string value = ReadRequired(section, name, 36);
        if (!Guid.TryParseExact(value, "D", out Guid parsed) || parsed == Guid.Empty)
        {
            throw new FormatException("A required identifier is invalid.");
        }

        return parsed;
    }

    private static string ReadRequired(IConfiguration section, string name, int maximumLength)
    {
        string? value = section[name];
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maximumLength
            || value != value.Trim())
        {
            throw new FormatException("A required configuration value is invalid.");
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
            throw new FormatException("A configured duration is invalid.");
        }

        return parsed;
    }

    private static string ReadGatewayRuntimeConnectionString(string value)
    {
        var builder = new NpgsqlConnectionStringBuilder(value);
        if (string.IsNullOrWhiteSpace(builder.Host)
            || string.IsNullOrWhiteSpace(builder.Database)
            || !string.Equals(
                builder.Username,
                GatewayRuntimeRole,
                StringComparison.Ordinal)
            || !PostgresRuntimeConnectionPolicy.HasSafeSessionConfiguration(builder)
            || !PostgresRuntimeConnectionPolicy.HasRequiredTransport(
                builder,
                allowInsecureLoopbackForDevelopment: false))
        {
            throw new ArgumentException("The gateway runtime database settings are unsafe.");
        }

        return builder.ConnectionString;
    }

    private static P256ExecutionLeaseTrustVerifier ReadTrustVerifier(
        IConfigurationSection section)
    {
        IConfigurationSection[] entries = section.GetChildren().ToArray();
        if (entries.Length is < 1 or > 32)
        {
            throw new ArgumentException("The lease trust set size is invalid.");
        }

        var keyBytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        try
        {
            foreach (IConfigurationSection entry in entries)
            {
                string keyId = ReadRequired(entry, "KeyId", 128);
                string encoded = ReadRequired(
                    entry,
                    "SubjectPublicKeyInfoBase64",
                    1400);
                byte[] decoded = Convert.FromBase64String(encoded);
                if (!string.Equals(
                        Convert.ToBase64String(decoded),
                        encoded,
                        StringComparison.Ordinal)
                    || !keyBytes.TryAdd(keyId, decoded))
                {
                    CryptographicOperations.ZeroMemory(decoded);
                    throw new FormatException("A lease trust key is invalid.");
                }
            }

            var trustSet = keyBytes.ToDictionary(
                entry => entry.Key,
                entry => (ReadOnlyMemory<byte>)entry.Value,
                StringComparer.Ordinal);
            return new P256ExecutionLeaseTrustVerifier(trustSet);
        }
        finally
        {
            foreach (byte[] bytes in keyBytes.Values)
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
    }
}
