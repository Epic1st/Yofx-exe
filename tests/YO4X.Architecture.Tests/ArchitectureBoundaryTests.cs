using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace YO4X.Architecture.Tests;

public sealed class ArchitectureBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void DomainModulesDoNotReferenceInfrastructurePackages()
    {
        string modulesRoot = Path.Combine(RepositoryRoot, "src", "Modules");
        string[] forbidden = ["Npgsql", "Microsoft.EntityFrameworkCore", "AspNetCore", "mt5api.dll"];

        foreach (string projectFile in Directory.EnumerateFiles(modulesRoot, "*.csproj", SearchOption.AllDirectories))
        {
            string xml = File.ReadAllText(projectFile);
            foreach (string dependency in forbidden)
            {
                Assert.DoesNotContain(dependency, xml, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void StrategyHostHasOnlyTheStrategyAbstractionsProjectReference()
    {
        string path = Path.Combine(RepositoryRoot, "src", "Runtime", "YO4X.StrategyHost", "YO4X.StrategyHost.csproj");
        XDocument project = XDocument.Load(path);
        string[] references = project.Descendants("ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension(element.Attribute("Include")?.Value))
            .Where(reference => reference is not null)
            .Select(reference => reference!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["YO4X.Strategy.Abstractions"], references);
    }

    [Fact]
    public void VendorAssemblyIsReferencedOnlyByMt5Adapter()
    {
        string sourceRoot = Path.Combine(RepositoryRoot, "src");
        string[] references = Directory.EnumerateFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("mt5api.dll", StringComparison.OrdinalIgnoreCase))
            .Select(file => Path.GetFileNameWithoutExtension(file))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["YO4X.Trading.Mt5"], references);
    }

    [Fact]
    public async Task VendorArtifactMatchesThePinnedU0Digest()
    {
        string artifactPath = VendorArtifactPath("mt5api.dll");
        await using FileStream stream = File.OpenRead(artifactPath);
        byte[] digest = await SHA256.HashDataAsync(stream, CancellationToken.None);

        Assert.Equal(
            "EB238C958A4D9F80C8A3EEACA07636AE53BC5A78A093BC3FE63923FA50A309C6",
            Convert.ToHexString(digest));
    }

    [Fact]
    public async Task VendorDocumentationMatchesThePinnedDigest()
    {
        await using FileStream stream = File.OpenRead(VendorArtifactPath("mt5api.xml"));
        byte[] digest = await SHA256.HashDataAsync(stream, CancellationToken.None);

        Assert.Equal(
            "D3A9FCD88F0CF24C0D5E05B1E12BB6951C405D3920AC3FADFF81C80826FF5829",
            Convert.ToHexString(digest));
    }

    [Fact]
    public void VendorArtifactMetadataRemainsExplicitlyUntrusted()
    {
        string artifactPath = VendorArtifactPath("mt5api.dll");
        FileVersionInfo version = FileVersionInfo.GetVersionInfo(artifactPath);
        using FileStream stream = File.OpenRead(artifactPath);
        using var peReader = new PEReader(stream);

        Assert.Equal("5.3677.1.2", version.FileVersion);
        Assert.Equal(
            "5.4850.0.0+d5195c9f9a21dd4cddd904d2ec857fc0b6de54fc",
            version.ProductVersion);
        Assert.NotNull(peReader.PEHeaders.CorHeader);
        Assert.False(peReader.PEHeaders.CorHeader.Flags.HasFlag(CorFlags.StrongNameSigned));
        Assert.Equal(0, peReader.PEHeaders.CorHeader.StrongNameSignatureDirectory.Size);
    }

    [Fact]
    public void Mt5AdapterPinsTheSuppliedArtifactWithoutPrivateCopy()
    {
        string projectPath = Path.Combine(
            RepositoryRoot,
            "src",
            "Runtime",
            "YO4X.Trading.Mt5",
            "YO4X.Trading.Mt5.csproj");
        XDocument project = XDocument.Load(projectPath);
        XElement reference = Assert.Single(
            project.Descendants("Reference"),
            element => string.Equals(
                element.Attribute("Include")?.Value,
                "mt5api",
                StringComparison.OrdinalIgnoreCase));

        string hintPath = Assert.IsType<string>(reference.Element("HintPath")?.Value);
        string privateCopy = Assert.IsType<string>(reference.Element("Private")?.Value);
        string artifactRoot = Assert.IsType<string>(
            project.Descendants("Mt5VendorArtifactRoot").Single().Value);
        string artifactPath = Assert.IsType<string>(
            project.Descendants("Mt5VendorArtifactPath").Single().Value);
        string expectedHash = Assert.IsType<string>(
            project.Descendants("Mt5VendorExpectedSha256").Single().Value);

        Assert.Equal("$(Mt5VendorArtifactPath)", hintPath);
        Assert.Contains("mt5-net-api-full-binaries-main", artifactRoot, StringComparison.Ordinal);
        Assert.EndsWith("mt5api.dll", artifactPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("false", privateCopy, ignoreCase: true);
        Assert.Equal(
            "EB238C958A4D9F80C8A3EEACA07636AE53BC5A78A093BC3FE63923FA50A309C6",
            expectedHash);
    }

    [Fact]
    public void CredentialBearingExamplesNeverEnterCompilationOrBuildOutputs()
    {
        string vendorExamplePath = Path.Combine(
            RepositoryRoot,
            "mt5-net-api-full-binaries-main",
            "Examples.cs");
        string projectPath = Path.Combine(
            RepositoryRoot,
            "src",
            "Runtime",
            "YO4X.Trading.Mt5",
            "YO4X.Trading.Mt5.csproj");
        XDocument project = XDocument.Load(projectPath);
        string[] explicitIncludes = project.Descendants("Compile")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();

        Assert.DoesNotContain(
            explicitIncludes,
            value => value.EndsWith("Examples.cs", StringComparison.OrdinalIgnoreCase));
        Assert.False(
            File.Exists(vendorExamplePath),
            "Credential-bearing vendor examples must remain quarantined from the repository working tree.");
        AssertNoBuildOutputFile("Examples.cs");
    }

    [Fact]
    public void VendorAssemblyNeverEntersApplicationOrTestBuildOutputs()
    {
        AssertNoBuildOutputFile("mt5api.dll");
    }

    [Fact]
    public void GatewayHostUsesOnlyTheKillableProcessBoundary()
    {
        string projectPath = Path.Combine(
            RepositoryRoot,
            "src",
            "Runtime",
            "YO4X.GatewayHost",
            "YO4X.GatewayHost.csproj");
        XDocument project = XDocument.Load(projectPath);
        string[] projectReferences = project.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Select(value => Path.GetFileNameWithoutExtension(value!))
            .ToArray();

        Assert.Contains("YO4X.Trading.ProcessIsolation", projectReferences, StringComparer.Ordinal);
        Assert.DoesNotContain("YO4X.Trading.Mt5", projectReferences, StringComparer.Ordinal);
        Assert.DoesNotContain(
            project.Descendants("Reference"),
            element => string.Equals(
                element.Attribute("Include")?.Value,
                "mt5api",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProductionBrokerVendorBindingIsReachableOnlyFromTheWorkerProcess()
    {
        string gatewayProjectPath = Path.Combine(
            RepositoryRoot,
            "src",
            "Runtime",
            "YO4X.GatewayHost",
            "YO4X.GatewayHost.csproj");
        string workerProjectPath = Path.Combine(
            RepositoryRoot,
            "src",
            "Runtime",
            "YO4X.Mt5.WorkerHost",
            "YO4X.Mt5.WorkerHost.csproj");
        string gatewayProgramPath = Path.Combine(
            RepositoryRoot,
            "src",
            "Runtime",
            "YO4X.GatewayHost",
            "Program.cs");
        string processClientPath = Path.Combine(
            RepositoryRoot,
            "src",
            "Runtime",
            "YO4X.Trading.ProcessIsolation",
            "BrokerProcessClient.cs");

        string gatewayProject = File.ReadAllText(gatewayProjectPath);
        string workerProject = File.ReadAllText(workerProjectPath);
        string gatewayProgram = File.ReadAllText(gatewayProgramPath);
        string processClient = File.ReadAllText(processClientPath);

        Assert.DoesNotContain("YO4X.Trading.Mt5", gatewayProject, StringComparison.Ordinal);
        Assert.Contains("YO4X.Trading.Mt5", workerProject, StringComparison.Ordinal);
        Assert.Contains("AddMt5ProcessBoundary", gatewayProgram, StringComparison.Ordinal);
        Assert.DoesNotContain("Mt5ProofOnlyGateway", gatewayProgram, StringComparison.Ordinal);
        Assert.Contains("RedirectStandardInput = true", processClient, StringComparison.Ordinal);
        Assert.Contains("RedirectStandardOutput = true", processClient, StringComparison.Ordinal);
        Assert.Contains("Kill(entireProcessTree: true)", processClient, StringComparison.Ordinal);
    }

    [Fact]
    public void VendorBindingContainsNoActiveNetworkHistoryOrTradeCalls()
    {
        string mapperPath = Path.Combine(
            RepositoryRoot,
            "src",
            "Runtime",
            "YO4X.Trading.Mt5",
            "Mt5VendorReadOnlyMapper.cs");
        string source = File.ReadAllText(mapperPath);
        string[] forbiddenCalls =
        [
            "new MT5API",
            ".Connect(",
            ".Disconnect(",
            ".Subscribe(",
            ".Unsubscribe(",
            ".GetQuote(",
            ".GetOpenedOrders(",
            ".RequestOrderHistory(",
            ".DownloadOrderHistory(",
            ".OrderSend",
            ".OrderClose",
            ".OrderModify"
        ];

        foreach (string forbiddenCall in forbiddenCalls)
        {
            Assert.DoesNotContain(forbiddenCall, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ObsoleteRootVendorArtifactIsAbsent()
    {
        Assert.False(File.Exists(Path.Combine(RepositoryRoot, "mt5api.dll")));
    }

    [Fact]
    public void AdminBffCannotReferenceVaultOrTradingAdapters()
    {
        string projectPath = Path.Combine(RepositoryRoot, "src", "Apps", "YO4X.Admin.Bff", "YO4X.Admin.Bff.csproj");
        string xml = File.ReadAllText(projectPath);

        Assert.DoesNotContain("SecretIngestion", xml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Trading.Mt5", xml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GatewayHost", xml, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "YO4X.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }

    private static string VendorArtifactPath(string fileName) =>
        Path.Combine(RepositoryRoot, "mt5-net-api-full-binaries-main", fileName);

    private static void AssertNoBuildOutputFile(string fileName)
    {
        string[] roots =
        [
            Path.Combine(RepositoryRoot, "src"),
            Path.Combine(RepositoryRoot, "tests")
        ];
        string[] matches = roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories))
            .Where(path => path.Split(Path.DirectorySeparatorChar)
                .Any(segment => string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.Empty(matches);
    }
}
