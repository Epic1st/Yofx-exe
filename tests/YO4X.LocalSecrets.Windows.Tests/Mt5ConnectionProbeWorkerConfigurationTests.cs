using YO4X.Mt5.ConnectionProbe.Windows;

namespace YO4X.LocalSecrets.Windows.Tests;

public sealed class Mt5ConnectionProbeWorkerConfigurationTests
{
    [Fact]
    public void ValidConfigurationIsNormalizedAndSecretsAreRedacted()
    {
        Dictionary<string, string?> values = ValidValues();

        Mt5ConnectionProbeWorkerConfiguration configuration =
            Mt5ConnectionProbeWorkerConfiguration.Load(values.GetValueOrDefault);

        Assert.Equal("MetaQuotes-Demo", configuration.ServerName);
        Assert.Equal(443, configuration.Port);
        Assert.True(Path.IsPathFullyQualified(configuration.ArtifactPath));
        Assert.DoesNotContain("certificate-secret", configuration.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", configuration.ToString(), StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", configuration.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AnyArtifactOtherThanExactPinIsRejected()
    {
        Dictionary<string, string?> values = ValidValues();
        values[Name("ARTIFACT_SHA256")] = new string('0', 64);

        Assert.Throws<InvalidDataException>(() =>
            Mt5ConnectionProbeWorkerConfiguration.Load(values.GetValueOrDefault));
    }

    [Theory]
    [InlineData("VAULT_ROOT", "relative-vault")]
    [InlineData("ARTIFACT_PATH", "mt5api.dll")]
    [InlineData("PORT", "0")]
    [InlineData("PORT", "65536")]
    [InlineData("HOST", "https://127.0.0.1")]
    public void UnsafeEndpointOrPathConfigurationIsRejected(string suffix, string value)
    {
        Dictionary<string, string?> values = ValidValues();
        values[Name(suffix)] = value;

        Assert.Throws<InvalidDataException>(() =>
            Mt5ConnectionProbeWorkerConfiguration.Load(values.GetValueOrDefault));
    }

    [Fact]
    public void CompositionHashesPinnedArtifactWithoutLoadingVendorAssembly()
    {
        Assert.DoesNotContain(
            AppDomain.CurrentDomain.GetAssemblies(),
            assembly => string.Equals(assembly.GetName().Name, "mt5api", StringComparison.OrdinalIgnoreCase));
        Mt5ConnectionProbeWorkerConfiguration configuration =
            Mt5ConnectionProbeWorkerConfiguration.Load(ValidValues().GetValueOrDefault);

        _ = Mt5ConnectionProbeWorkerComposition.CreateServer(configuration);

        Assert.DoesNotContain(
            AppDomain.CurrentDomain.GetAssemblies(),
            assembly => string.Equals(assembly.GetName().Name, "mt5api", StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, string?> ValidValues() => new(StringComparer.Ordinal)
    {
        [Name("ARTIFACT_ID")] = "20000000-0000-0000-0000-000000000001",
        [Name("ARTIFACT_SHA256")] =
            PinnedMt5NetApiConnectionClientFactory.ApprovedArtifactSha256.ToLowerInvariant(),
        [Name("ARTIFACT_PATH")] = Path.Combine(
            RepositoryRoot(),
            "mt5-net-api-full-binaries-main",
            "mt5api.dll"),
        [Name("VAULT_ROOT")] = Path.Combine(RepositoryRoot(), ".local", "test-probe-vault"),
        [Name("BROKER_COMPANY")] = "MetaQuotes Software Corp.",
        [Name("SERVER_NAME")] = "MetaQuotes-Demo",
        [Name("HOST")] = "127.0.0.1",
        [Name("PORT")] = "443",
        [Name("PFX_PATH")] = null,
        [Name("PFX_PASSWORD")] = ""
    };

    private static string Name(string suffix) =>
        Mt5ConnectionProbeWorkerConfiguration.Prefix + suffix;

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
}
