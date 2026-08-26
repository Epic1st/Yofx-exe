using System.Globalization;
using YO4X.Mt5.ConnectionProbe.Windows;
using YO4X.Trading.Abstractions;
using YO4X.Trading.ProcessIsolation;

namespace YO4X.Mt5.DemoCanary;

public sealed record DemoCanaryOptions(
    Guid BrokerAccountId,
    string CredentialKey,
    Mt5ConnectionProbeWorkerConfiguration WorkerConfiguration,
    IsolatedBrokerProcessOptions ProcessOptions)
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "broker-account-id", "credential-key",
        "artifact-id", "artifact-sha256", "artifact-path", "vault-root",
        "broker-company", "server-name", "host", "port", "pfx-path",
        "worker-path", "worker-sha256", "manifest-path", "manifest-sha256",
        "timeout-ms"
    };

    public static DemoCanaryOptions Parse(
        string[] args,
        Func<string, string?>? readEnvironment = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length is < 2 or > 40 || args.Length % 2 != 0)
        {
            throw new ArgumentException("Named option/value pairs are required.", nameof(args));
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < args.Length; index += 2)
        {
            string token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal)
                || token.Length <= 2
                || !Allowed.Contains(token[2..])
                || string.IsNullOrWhiteSpace(args[index + 1])
                || args[index + 1].IndexOfAny(['\0', '\r', '\n']) >= 0
                || !values.TryAdd(token[2..], args[index + 1]))
            {
                throw new ArgumentException("The demo-canary options are invalid.", nameof(args));
            }
        }

        string Required(string name) => values.TryGetValue(name, out string? value)
            ? value
            : throw new ArgumentException($"Required option --{name} is missing.", nameof(args));

        Guid brokerAccountId = Guid.Parse(Required("broker-account-id"));
        if (brokerAccountId == Guid.Empty)
        {
            throw new ArgumentException("The broker account id cannot be empty.", nameof(args));
        }

        string credentialKey = ExactLowerSha256(Required("credential-key"), "credential-key");
        readEnvironment ??= Environment.GetEnvironmentVariable;
        var workerValues = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Name("ARTIFACT_ID")] = Required("artifact-id"),
            [Name("ARTIFACT_SHA256")] = Required("artifact-sha256"),
            [Name("ARTIFACT_PATH")] = Required("artifact-path"),
            [Name("VAULT_ROOT")] = Required("vault-root"),
            [Name("BROKER_COMPANY")] = Required("broker-company"),
            [Name("SERVER_NAME")] = Required("server-name"),
            [Name("HOST")] = Required("host"),
            [Name("PORT")] = Required("port"),
            [Name("PFX_PATH")] = values.GetValueOrDefault("pfx-path"),
            [Name("PFX_PASSWORD")] = readEnvironment(Name("PFX_PASSWORD"))
        };
        Mt5ConnectionProbeWorkerConfiguration workerConfiguration =
            Mt5ConnectionProbeWorkerConfiguration.Load(workerValues.GetValueOrDefault);

        if (!int.TryParse(
                Required("timeout-ms"),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int timeoutMilliseconds)
            || timeoutMilliseconds is < 100 or > 30_000)
        {
            throw new ArgumentOutOfRangeException(nameof(args), "The canary timeout is invalid.");
        }

        var processOptions = new IsolatedBrokerProcessOptions(
            Required("worker-path"),
            Required("worker-sha256"),
            Required("manifest-path"),
            Required("manifest-sha256"),
            TimeSpan.FromMilliseconds(timeoutMilliseconds));
        return new DemoCanaryOptions(
            brokerAccountId,
            credentialKey,
            workerConfiguration,
            processOptions);
    }

    public IReadOnlyDictionary<string, string> CreateWorkerEnvironment()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Name("ARTIFACT_ID")] = WorkerConfiguration.ArtifactId.ToString("D"),
            [Name("ARTIFACT_SHA256")] = WorkerConfiguration.ArtifactSha256,
            [Name("ARTIFACT_PATH")] = WorkerConfiguration.ArtifactPath,
            [Name("VAULT_ROOT")] = WorkerConfiguration.VaultRoot,
            [Name("BROKER_COMPANY")] = WorkerConfiguration.BrokerCompany,
            [Name("SERVER_NAME")] = WorkerConfiguration.ServerName,
            [Name("HOST")] = WorkerConfiguration.Host,
            [Name("PORT")] = WorkerConfiguration.Port.ToString(CultureInfo.InvariantCulture)
        };
        if (WorkerConfiguration.CertificatePfxPath is not null)
        {
            values.Add(Name("PFX_PATH"), WorkerConfiguration.CertificatePfxPath);
            if (WorkerConfiguration.CertificatePassword.Length != 0)
            {
                values.Add(Name("PFX_PASSWORD"), WorkerConfiguration.CertificatePassword);
            }
        }

        return values;
    }

    public BrokerWorkerConnectProbeRequest CreateProbe(
        DateTimeOffset nowUtc,
        string vaultIdentitySha256) => new(
        BrokerAccountId,
        WorkerConfiguration.ArtifactId,
        WorkerConfiguration.ArtifactSha256,
        CredentialKey,
        ExactLowerSha256(vaultIdentitySha256, nameof(vaultIdentitySha256)),
        new BrokerServerIdentity(
            WorkerConfiguration.BrokerCompany,
            WorkerConfiguration.ServerName),
        BrokerEnvironment.Demo,
        nowUtc);

    public override string ToString() =>
        $"DemoCanaryOptions {{ BrokerAccountId = {BrokerAccountId:D}, CredentialKey = {CredentialKey}, Endpoint = [REDACTED], CertificatePassword = [REDACTED] }}";

    private static string Name(string suffix) =>
        Mt5ConnectionProbeWorkerConfiguration.Prefix + suffix;

    private static string ExactLowerSha256(string value, string name)
    {
        if (value.Length != 64
            || value.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException($"Option --{name} requires a lowercase SHA-256.", name);
        }

        return value;
    }
}
