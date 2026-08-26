using YO4X.Mt5.DemoCanary;

namespace YO4X.LocalSecrets.Windows.Tests;

public sealed class DemoCanaryOptionsTests
{
    [Fact]
    public void ParserCarriesOnlyOpaqueCredentialBindingsIntoRequest()
    {
        using var deployment = new TemporaryDeployment();
        DemoCanaryOptions options = DemoCanaryOptions.Parse(
            Arguments(deployment),
            _ => null);

        Assert.Equal(new string('b', 64), options.CredentialKey);
        Assert.DoesNotContain("127.0.0.1", options.ToString(), StringComparison.Ordinal);
        IReadOnlyDictionary<string, string> environment = options.CreateWorkerEnvironment();
        Assert.DoesNotContain(environment.Keys, key => key.Contains("CREDENTIAL", StringComparison.Ordinal));
        Assert.DoesNotContain(environment.Values, value => value == options.CredentialKey);
        Assert.DoesNotContain(environment.Values, value => value == new string('c', 64));
        Assert.Equal(
            new string('c', 64),
            options.CreateProbe(DateTimeOffset.UnixEpoch, new string('c', 64))
                .CredentialVaultIdentitySha256);
    }

    [Fact]
    public void ParserRejectsBrokerPasswordOption()
    {
        using var deployment = new TemporaryDeployment();
        string[] valid = Arguments(deployment);
        string[] altered = [.. valid, "--password", "must-not-be-accepted"];

        Assert.Throws<ArgumentException>(() => DemoCanaryOptions.Parse(altered, _ => null));
    }

    [Fact]
    public void ParserRejectsCallerSuppliedVaultIdentity()
    {
        using var deployment = new TemporaryDeployment();
        string[] altered =
        [
            .. Arguments(deployment),
            "--vault-identity-sha256", new string('c', 64)
        ];

        Assert.Throws<ArgumentException>(() => DemoCanaryOptions.Parse(altered, _ => null));
    }

    private static string[] Arguments(TemporaryDeployment deployment) =>
    [
        "--broker-account-id", "10000000-0000-0000-0000-000000000001",
        "--credential-key", new string('b', 64),
        "--artifact-id", "20000000-0000-0000-0000-000000000001",
        "--artifact-sha256", "eb238c958a4d9f80c8a3eeaca07636ae53bc5a78a093bc3fe63923fa50a309c6",
        "--artifact-path", Path.Combine(RepositoryRoot(), "mt5-net-api-full-binaries-main", "mt5api.dll"),
        "--vault-root", Path.Combine(RepositoryRoot(), ".local", "demo-canary-test-vault"),
        "--broker-company", "MetaQuotes Software Corp.",
        "--server-name", "MetaQuotes-Demo",
        "--host", "127.0.0.1",
        "--port", "443",
        "--worker-path", deployment.WorkerPath,
        "--worker-sha256", new string('d', 64),
        "--manifest-path", deployment.ManifestPath,
        "--manifest-sha256", new string('e', 64),
        "--timeout-ms", "5000"
    ];

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "YO4X.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed class TemporaryDeployment : IDisposable
    {
        private readonly string root = Path.Combine(
            Path.GetTempPath(),
            $"yo4x-demo-canary-options-{Guid.NewGuid():N}");

        public TemporaryDeployment()
        {
            Directory.CreateDirectory(root);
            WorkerPath = Path.Combine(root, "probe-worker.exe");
            ManifestPath = Path.Combine(root, "broker-worker-launch.v1.json");
            File.WriteAllBytes(WorkerPath, [1]);
            File.WriteAllBytes(ManifestPath, [2]);
        }

        public string WorkerPath { get; }

        public string ManifestPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
