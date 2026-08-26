using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using YO4X.LocalSecrets.Windows;
using YO4X.Trading.ProcessIsolation;

namespace YO4X.Mt5.ConnectionProbe.Windows;

public sealed record Mt5ConnectionProbeWorkerConfiguration(
    Guid ArtifactId,
    string ArtifactSha256,
    string ArtifactPath,
    string VaultRoot,
    string BrokerCompany,
    string ServerName,
    string Host,
    int Port,
    string? CertificatePfxPath,
    string CertificatePassword)
{
    public const string Prefix = "YO4X_MT5_PROBE_";

    public static Mt5ConnectionProbeWorkerConfiguration LoadFromEnvironment() =>
        Load(Environment.GetEnvironmentVariable);

    public static Mt5ConnectionProbeWorkerConfiguration Load(Func<string, string?> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        Guid artifactId = Guid.Parse(Required(read, "ARTIFACT_ID"));
        if (artifactId == Guid.Empty)
        {
            throw new InvalidDataException("The approved artifact id cannot be empty.");
        }

        string artifactSha256 = Required(read, "ARTIFACT_SHA256");
        string expectedSha256 =
            PinnedMt5NetApiConnectionClientFactory.ApprovedArtifactSha256.ToLowerInvariant();
        if (!string.Equals(artifactSha256, expectedSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The configured MT5 artifact digest is not the pinned digest.");
        }

        string artifactPath = FullPath(Required(read, "ARTIFACT_PATH"), "ARTIFACT_PATH");
        if (!string.Equals(Path.GetFileName(artifactPath), "mt5api.dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The approved vendor artifact must be named mt5api.dll.");
        }

        string vaultRoot = FullPath(Required(read, "VAULT_ROOT"), "VAULT_ROOT");
        string brokerCompany = BoundedText(Required(read, "BROKER_COMPANY"), "BROKER_COMPANY");
        string serverName = BoundedText(Required(read, "SERVER_NAME"), "SERVER_NAME");
        string host = Required(read, "HOST");
        if (host.Length > 253
            || host.Any(character => char.IsWhiteSpace(character) || char.IsControl(character))
            || Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            throw new InvalidDataException("The configured MT5 host is invalid.");
        }

        if (!int.TryParse(
                Required(read, "PORT"),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int port)
            || port is < 1 or > ushort.MaxValue)
        {
            throw new InvalidDataException("The configured MT5 port is invalid.");
        }

        string? pfxPathValue = Optional(read, "PFX_PATH");
        string? pfxPath = pfxPathValue is null
            ? null
            : FullPath(pfxPathValue, "PFX_PATH");
        string certificatePassword = Optional(read, "PFX_PASSWORD") ?? string.Empty;
        if (pfxPath is null && certificatePassword.Length != 0)
        {
            throw new InvalidDataException("A certificate password requires a PFX path.");
        }

        return new Mt5ConnectionProbeWorkerConfiguration(
            artifactId,
            artifactSha256,
            artifactPath,
            vaultRoot,
            brokerCompany,
            serverName,
            host,
            port,
            pfxPath,
            certificatePassword);
    }

    public override string ToString() =>
        $"Mt5ConnectionProbeWorkerConfiguration {{ ArtifactId = {ArtifactId:D}, ArtifactSha256 = {ArtifactSha256}, ServerName = {ServerName}, Host = [REDACTED], CertificatePassword = [REDACTED] }}";

    private static string Required(Func<string, string?> read, string suffix)
    {
        string? value = Optional(read, suffix);
        return value ?? throw new InvalidDataException($"Required setting {Prefix}{suffix} is missing.");
    }

    private static string? Optional(Func<string, string?> read, string suffix)
    {
        string? value = read(Prefix + suffix);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string FullPath(string value, string suffix)
    {
        if (!Path.IsPathFullyQualified(value))
        {
            throw new InvalidDataException($"Setting {Prefix}{suffix} must be an absolute path.");
        }

        return Path.GetFullPath(value);
    }

    private static string BoundedText(string value, string suffix)
    {
        if (value.Length > 255 || value.Any(char.IsControl))
        {
            throw new InvalidDataException($"Setting {Prefix}{suffix} is invalid.");
        }

        return value;
    }
}

public static class Mt5ConnectionProbeWorkerComposition
{
    private const int MaximumCertificateBytes = 64 * 1024;

    public static AuthenticatedBrokerConnectionProbeWorkerServer CreateServer(
        Mt5ConnectionProbeWorkerConfiguration configuration,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var clientFactory = new PinnedMt5NetApiConnectionClientFactory(
            configuration.ArtifactPath);
        byte[] certificate = ReadCertificate(configuration.CertificatePfxPath);
        try
        {
            var endpoint = new Mt5NetApiConnectionEndpoint(
                configuration.BrokerCompany,
                configuration.ServerName,
                configuration.Host,
                configuration.Port,
                certificate,
                configuration.CertificatePassword);
            var transport = new Mt5NetApiConnectionOnlyTransport(
                endpoint,
                clientFactory,
                timeProvider);
            var vault = new DpapiLocalMt5CredentialVault(configuration.VaultRoot);
            var approvedArtifacts = new ApprovedMt5ProbeArtifacts(
                new Dictionary<Guid, string>
                {
                    [configuration.ArtifactId] = configuration.ArtifactSha256
                });
            var executor = new VaultBackedBrokerConnectionProbeExecutor(
                vault,
                approvedArtifacts,
                transport);
            return new AuthenticatedBrokerConnectionProbeWorkerServer(
                executor,
                timeProvider);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(certificate);
        }
    }

    private static byte[] ReadCertificate(string? path)
    {
        if (path is null)
        {
            return [];
        }

        var file = new FileInfo(path);
        file.Refresh();
        if (!file.Exists
            || (file.Attributes & FileAttributes.ReparsePoint) != 0
            || file.Length is < 1 or > MaximumCertificateBytes)
        {
            throw new InvalidDataException("The configured MT5 PFX file is invalid.");
        }

        return File.ReadAllBytes(path);
    }
}
