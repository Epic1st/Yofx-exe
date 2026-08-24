using System.Xml.Linq;

namespace YO4X.Architecture.Tests;

public sealed class StrategyTransactionBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void RuntimeApplicationRemainsDatabaseAndVendorIndependent()
    {
        string projectPath = Path.Combine(
            RepositoryRoot,
            "src",
            "Application",
            "YO4X.Runtime.Application",
            "YO4X.Runtime.Application.csproj");

        AssertProjectReferences(
            projectPath,
            "YO4X.BuildingBlocks",
            "YO4X.Runtime.Contracts",
            "YO4X.Strategy.Abstractions",
            "YO4X.Tenancy");
        Assert.Empty(XDocument.Load(projectPath).Descendants("PackageReference"));

        string source = ReadProjectSources(Path.GetDirectoryName(projectPath)!);
        Assert.DoesNotContain("Npgsql", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mt5api", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MetaTrader", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MtApi", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OrderSend", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimePostgresAdapterCannotReferenceTradingOrStrategyHostImplementations()
    {
        string projectPath = Path.Combine(
            RepositoryRoot,
            "src",
            "Infrastructure",
            "YO4X.Runtime.Postgres",
            "YO4X.Runtime.Postgres.csproj");

        AssertProjectReferences(
            projectPath,
            "YO4X.Persistence.Postgres",
            "YO4X.Runtime.Application",
            "YO4X.Strategy.Abstractions",
            "YO4X.Tenancy");

        XDocument project = XDocument.Load(projectPath);
        string[] packages = project.Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(static value => value is not null)
            .Select(static value => value!)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["Npgsql"], packages);

        string xml = project.ToString();
        Assert.DoesNotContain("YO4X.Trading.Mt5", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("YO4X.GatewayHost", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("YO4X.StrategyHost", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeTransactionProjectsAreExplicitlyBuiltByTheSolution()
    {
        string[] solutionLines = File.ReadAllLines(
            Path.Combine(RepositoryRoot, "YO4X.sln"));

        AssertSolutionProject(
            solutionLines,
            @"src\Application\YO4X.Runtime.Application\YO4X.Runtime.Application.csproj",
            "{22BAF98C-8415-17C4-B26A-D537657BC863}");
        AssertSolutionProject(
            solutionLines,
            @"src\Infrastructure\YO4X.Runtime.Postgres\YO4X.Runtime.Postgres.csproj",
            "{9048EB7F-3875-A59E-E36B-5BD4C6F2A282}");
        AssertSolutionProject(
            solutionLines,
            @"tests\YO4X.Runtime.Application.Tests\YO4X.Runtime.Application.Tests.csproj",
            "{0AB3BF05-4346-4AA6-1389-037BE0695223}");
    }

    private static void AssertProjectReferences(string projectPath, params string[] expected)
    {
        XDocument project = XDocument.Load(projectPath);
        string[] actual = project.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(static value => value is not null)
            .Select(static value => Path.GetFileNameWithoutExtension(value!))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
    }

    private static void AssertSolutionProject(
        IReadOnlyList<string> solutionLines,
        string expectedPath,
        string expectedFolderId)
    {
        string[] entries = solutionLines
            .Where(line => line.StartsWith("Project(", StringComparison.Ordinal))
            .Where(line => line.Contains(
                $"\", \"{expectedPath}\", \"",
                StringComparison.Ordinal))
            .ToArray();
        string entry = Assert.Single(entries);
        string[] components = entry.Split('"');
        Assert.True(components.Length >= 8, "The solution project entry is malformed.");
        string projectId = components[7];

        string[] configurations = solutionLines
            .Where(line => line.StartsWith($"\t\t{projectId}.", StringComparison.Ordinal))
            .ToArray();
        string[] expectedConfigurations =
        [
            $"\t\t{projectId}.Debug|Any CPU.ActiveCfg = Debug|Any CPU",
            $"\t\t{projectId}.Debug|Any CPU.Build.0 = Debug|Any CPU",
            $"\t\t{projectId}.Debug|x64.ActiveCfg = Debug|Any CPU",
            $"\t\t{projectId}.Debug|x64.Build.0 = Debug|Any CPU",
            $"\t\t{projectId}.Debug|x86.ActiveCfg = Debug|Any CPU",
            $"\t\t{projectId}.Debug|x86.Build.0 = Debug|Any CPU",
            $"\t\t{projectId}.Release|Any CPU.ActiveCfg = Release|Any CPU",
            $"\t\t{projectId}.Release|Any CPU.Build.0 = Release|Any CPU",
            $"\t\t{projectId}.Release|x64.ActiveCfg = Release|Any CPU",
            $"\t\t{projectId}.Release|x64.Build.0 = Release|Any CPU",
            $"\t\t{projectId}.Release|x86.ActiveCfg = Release|Any CPU",
            $"\t\t{projectId}.Release|x86.Build.0 = Release|Any CPU",
        ];
        Assert.Equal(
            expectedConfigurations.Order(StringComparer.Ordinal),
            configurations.Order(StringComparer.Ordinal));
        Assert.Contains(
            $"\t\t{projectId} = {expectedFolderId}",
            solutionLines,
            StringComparer.Ordinal);
    }

    private static string ReadProjectSources(string projectDirectory) => string.Join(
        Environment.NewLine,
        Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.Split(Path.DirectorySeparatorChar).Any(
                segment => segment is "bin" or "obj"))
            .Order(StringComparer.Ordinal)
            .Select(File.ReadAllText));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "YO4X.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
