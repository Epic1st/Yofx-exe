using System.Security.Cryptography;
using System.Text;
using YO4X.Conversion.Worker;
using YO4X.StrategyGovernance;

namespace YO4X.Worker.Tests;

public sealed class Mql5ReleaseArtifactContractTests
{
    [Fact]
    public async Task ExactWorkspaceArtifactsMatchCurrentFormattersByteForByte()
    {
        string repositoryRoot = FindRepositoryRoot();
        string sourceRoot = Path.Combine(repositoryRoot, "Testing", "Mq5");
        var inventory = new Mql5CorpusInventoryJob(new Mql5StaticInventoryAnalyzer());
        using Mql5AnalyzedCorpus corpus = await inventory.AnalyzeDirectoryForPersistenceAsync(
            sourceRoot,
            TestContext.Current.CancellationToken);
        Mql5ConversionCorpusEvidence conversion = new Mql5ConversionEvidenceAnalyzer()
            .Analyze(corpus.Documents);
        Mql5CompilePackagePlan plan = Mql5CompilePackageDossierPlanner.Plan(
            corpus.Manifest,
            conversion,
            corpus.Documents,
            approvedPlatformLibrarySnapshot: null);
        Mql5QuarantineIntakeEvidence quarantine = await new Mql5QuarantineIntakeJob()
            .AnalyzeDirectoryAsync(
                sourceRoot,
                corpus.Manifest,
                TestContext.Current.CancellationToken);

        AssertArtifact(
            Mql5InventoryFormatter.ToJson(corpus.Manifest),
            repositoryRoot,
            "artifacts", "verification", "mql5", "mq5-static-manifest.v1.json");
        AssertArtifact(
            Mql5InventoryFormatter.ToJson(corpus.Manifest),
            repositoryRoot,
            "docs", "backend", "mq5-static-manifest.v1.json");
        AssertArtifact(
            Mql5InventoryFormatter.ToMarkdown(corpus.Manifest),
            repositoryRoot,
            "artifacts", "verification", "mql5", "mq5-static-compatibility-report.md");
        AssertArtifact(
            Mql5InventoryFormatter.ToMarkdown(corpus.Manifest),
            repositoryRoot,
            "docs", "backend", "MQ5_COMPATIBILITY_REPORT.md");
        AssertArtifact(
            Mql5ConversionEvidenceFormatter.ToJson(conversion),
            repositoryRoot,
            "artifacts", "verification", "mql5", "mq5-conversion-evidence.v1.json");
        AssertArtifact(
            Mql5ConversionEvidenceFormatter.ToMarkdown(conversion),
            repositoryRoot,
            "artifacts", "verification", "mql5", "mq5-conversion-evidence-report.md");
        AssertArtifact(
            Mql5CompilePackagePlanFormatter.ToJson(plan),
            repositoryRoot,
            "artifacts", "verification", "mql5", "mq5-compile-package-plan.v2.json");
        AssertArtifact(
            Mql5QuarantineIntakeFormatter.ToJson(quarantine),
            repositoryRoot,
            "docs", "backend", "mql5-quarantine-intake.v2.json");
        AssertArtifact(
            Mql5QuarantineIntakeFormatter.ToMarkdown(quarantine),
            repositoryRoot,
            "docs", "backend", "MQL5_NONCANONICAL_INTAKE_REPORT.md");
    }

    private static void AssertArtifact(
        string expected,
        string repositoryRoot,
        params string[] relativeSegments)
    {
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expected);
        byte[] actualBytes = File.ReadAllBytes(Path.Combine(
            [repositoryRoot, .. relativeSegments]));
        try
        {
            Assert.Equal(expectedBytes, actualBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expectedBytes);
            CryptographicOperations.ZeroMemory(actualBytes);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "YO4X.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
