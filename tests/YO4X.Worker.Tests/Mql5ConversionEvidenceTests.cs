using System.Text;
using YO4X.Conversion.Worker;
using YO4X.StrategyGovernance;

namespace YO4X.Worker.Tests;

public sealed class Mql5ConversionEvidenceTests
{
    [Fact]
    public void BuildsCompleteDependencyClosureInStableDependencyFirstOrder()
    {
        Mql5ConversionCorpusEvidence evidence = Analyze(
            ("main.mq5", "#include \"lib/a.mqh\"\n#include \"lib/b.mqh\"\nvoid OnTick() {}"),
            ("lib/a.mqh", "#include \"b.mqh\"\ndouble A() { return B(); }"),
            ("lib/b.mqh", "double B() { return 1.0; }"));

        Mql5ConversionFileEvidence main = Assert.Single(
            evidence.Files,
            file => file.RelativePath == "main.mq5");
        Assert.Equal(["lib/a.mqh", "lib/b.mqh"], main.DependencyClosure.DirectDependencies);
        Assert.Equal(["lib/a.mqh", "lib/b.mqh"], main.DependencyClosure.TransitiveDependencies);
        Assert.Equal(["lib/b.mqh", "lib/a.mqh"], main.DependencyClosure.DependencyFirstOrder);
        Assert.True(main.DependencyClosure.DependencyFirstOrderProven);
        Assert.Empty(main.DependencyClosure.ReachableCycleMembers);
        Assert.Equal(
            Mql5ConversionEvidenceDisposition.AwaitingIsolatedTypeCheck,
            main.Disposition);
        AssertStage(
            main,
            Mql5EvidenceStageName.DependencyResolution,
            Mql5EvidenceStageStatus.Passed);
        AssertStage(
            main,
            Mql5EvidenceStageName.TypeChecking,
            Mql5EvidenceStageStatus.NotAttempted);
    }

    [Fact]
    public void DetectsCycleMembersAndBlocksEveryReachableRoot()
    {
        Mql5ConversionCorpusEvidence evidence = Analyze(
            ("main.mq5", "#include \"a.mqh\"\nvoid OnTick() {}"),
            ("a.mqh", "#include \"b.mqh\"\ndouble A() { return B(); }"),
            ("b.mqh", "#include \"a.mqh\"\ndouble B() { return A(); }"));

        foreach (Mql5ConversionFileEvidence file in evidence.Files)
        {
            Assert.Equal(
                Mql5ConversionEvidenceDisposition.BlockedDependencyCycle,
                file.Disposition);
            Assert.Equal(["a.mqh", "b.mqh"], file.DependencyClosure.ReachableCycleMembers);
            Assert.False(file.DependencyClosure.DependencyFirstOrderProven);
            Assert.Empty(file.DependencyClosure.DependencyFirstOrder);
            Assert.Contains(
                file.Findings,
                finding => finding.Code == "DEPENDENCY_CYCLE_BLOCKS_ORDERING");
        }
    }

    [Fact]
    public void MissingSourceOutranksExternalPlatformSnapshotAndFailsClosed()
    {
        Mql5ConversionCorpusEvidence evidence = Analyze((
            "main.mq5",
            "#include \"missing.mqh\"\n#include <Trade/Trade.mqh>\nvoid OnTick() {}"));
        Mql5ConversionFileEvidence file = Assert.Single(evidence.Files);

        Assert.Equal(
            Mql5ConversionEvidenceDisposition.BlockedMissingDependency,
            file.Disposition);
        Assert.Contains(
            file.Includes,
            include => include.Resolution == Mql5IncludeResolution.MissingSource);
        Assert.Contains(
            file.Includes,
            include => include.Resolution == Mql5IncludeResolution.PlatformLibrary);
        Assert.Contains(
            file.Findings,
            finding => finding.Code == "PLATFORM_LIBRARY_SNAPSHOT_REQUIRED");
        AssertStage(
            file,
            Mql5EvidenceStageName.DependencyResolution,
            Mql5EvidenceStageStatus.Failed);
        AssertStage(
            file,
            Mql5EvidenceStageName.TypeChecking,
            Mql5EvidenceStageStatus.Blocked);
    }

    [Fact]
    public void ReportsStructuralAndConditionalErrorsAtSourceLocations()
    {
        Mql5ConversionCorpusEvidence evidence = Analyze((
            "main.mq5",
            "void OnTick() {\n  int value = (1 + 2];\n}\n#endif\n"));
        Mql5ConversionFileEvidence file = Assert.Single(evidence.Files);

        Mql5ConversionEvidenceFinding delimiter = Assert.Single(
            file.Findings,
            finding => finding.Code == "DELIMITER_KIND_MISMATCH");
        Assert.Equal(2, delimiter.Location?.Line);
        Assert.True(delimiter.Location?.Column > 0);
        Mql5ConversionEvidenceFinding conditional = Assert.Single(
            file.Findings,
            finding => finding.Code == "PREPROCESSOR_ENDIF_WITHOUT_OPEN");
        Assert.Equal(new Mql5EvidenceLocation(4, 2), conditional.Location);
        Assert.False(file.Structural.DelimitersBalanced);
        Assert.False(file.Structural.ConditionalDirectivesBalanced);
        Assert.Equal(
            Mql5ConversionEvidenceDisposition.BlockedInvalidSyntax,
            file.Disposition);
        AssertStage(
            file,
            Mql5EvidenceStageName.StructuralParse,
            Mql5EvidenceStageStatus.Failed);
    }

    [Fact]
    public void EvidenceIsDeterministicAndOmitsArbitraryIdentifiersLiteralsAndBodies()
    {
        const string privateIdentifier = "PRIVATE_ALPHA_LOGIC_MUST_NOT_LEAK";
        const string privateLiteral = "PRIVATE_LITERAL_MUST_NOT_LEAK";
        (string Path, string Source) main = (
            "main.mq5",
            $"#include \"lib/helper.mqh\"\nvoid OnTick() {{ string {privateIdentifier} = \"{privateLiteral}\"; }}");
        (string Path, string Source) helper = (
            "lib/helper.mqh",
            "double Helper() { return 1.0; }");

        Mql5ConversionCorpusEvidence forward = Analyze(main, helper);
        Mql5ConversionCorpusEvidence reverse = Analyze(helper, main);
        string forwardJson = Mql5ConversionEvidenceFormatter.ToJson(forward);
        string reverseJson = Mql5ConversionEvidenceFormatter.ToJson(reverse);
        string report = Mql5ConversionEvidenceFormatter.ToMarkdown(forward);

        Assert.Equal(forward.EvidenceSha256, reverse.EvidenceSha256);
        Assert.Equal(forward.DependencyGraphSha256, reverse.DependencyGraphSha256);
        Assert.Equal(forwardJson, reverseJson);
        Assert.DoesNotContain(privateIdentifier, forwardJson, StringComparison.Ordinal);
        Assert.DoesNotContain(privateIdentifier, report, StringComparison.Ordinal);
        Assert.DoesNotContain(privateLiteral, forwardJson, StringComparison.Ordinal);
        Assert.DoesNotContain(privateLiteral, report, StringComparison.Ordinal);
        Assert.Contains("lib/helper.mqh", forwardJson, StringComparison.Ordinal);
        Assert.Contains("OnTick", forwardJson, StringComparison.Ordinal);
        Assert.All(forward.Files, file =>
        {
            Assert.False(file.Structural.FullGrammarParseProven);
            Assert.False(file.Structural.TypeCheckProven);
            Assert.False(file.Structural.RestrictedIrLoweringProven);
            AssertStage(
                file,
                Mql5EvidenceStageName.RestrictedIrLowering,
                Mql5EvidenceStageStatus.Blocked);
        });
    }

    [Fact]
    public void DependencyClosureDigestBindsExactDependencyBytes()
    {
        Mql5ConversionCorpusEvidence first = Analyze(
            ("main.mq5", "#include \"helper.mqh\"\nvoid OnTick() { Helper(); }"),
            ("helper.mqh", "void Helper() { int value = 1; }"));
        Mql5ConversionCorpusEvidence second = Analyze(
            ("main.mq5", "#include \"helper.mqh\"\nvoid OnTick() { Helper(); }"),
            ("helper.mqh", "void Helper() { int value = 2; }"));
        Mql5ConversionFileEvidence firstMain = Assert.Single(
            first.Files,
            static file => file.RelativePath == "main.mq5");
        Mql5ConversionFileEvidence secondMain = Assert.Single(
            second.Files,
            static file => file.RelativePath == "main.mq5");

        Assert.Equal(firstMain.SourceSha256, secondMain.SourceSha256);
        Assert.Equal(first.DependencyGraphSha256, second.DependencyGraphSha256);
        Assert.NotEqual(first.InputCorpusSha256, second.InputCorpusSha256);
        Assert.NotEqual(firstMain.DependencyClosureSha256, secondMain.DependencyClosureSha256);
        Assert.NotEqual(firstMain.EvidenceSha256, secondMain.EvidenceSha256);
        Assert.NotEqual(first.EvidenceSha256, second.EvidenceSha256);
    }

    [Theory]
    [InlineData("../../escape.mq5")]
    [InlineData("C:\\absolute\\strategy.mq5")]
    [InlineData("/absolute/strategy.mq5")]
    public void RejectsUnsafeSourcePaths(string path)
    {
        var analyzer = new Mql5ConversionEvidenceAnalyzer();
        Assert.Throws<ArgumentException>(() => analyzer.Analyze(
        [
            new Mql5SourceDocument(path, Encoding.UTF8.GetBytes("void OnTick() {}"))
        ]));
    }

    [Fact]
    public void RejectsDuplicatePathsAfterNormalizationAndCaseFolding()
    {
        var analyzer = new Mql5ConversionEvidenceAnalyzer();
        Assert.Throws<ArgumentException>(() => analyzer.Analyze(
        [
            new Mql5SourceDocument("folder/../MAIN.mq5", Encoding.UTF8.GetBytes("void OnTick() {}")),
            new Mql5SourceDocument("main.mq5", Encoding.UTF8.GetBytes("void OnTick() {}"))
        ]));
    }

    [Fact]
    public async Task CommandRequiresBothEvidenceOutputsAndWritesNoPartialArtifacts()
    {
        string root = Path.Combine(Path.GetTempPath(), "yo4x-evidence-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string sourceRoot = Path.Combine(root, "source");
            Directory.CreateDirectory(sourceRoot);
            await File.WriteAllTextAsync(
                Path.Combine(sourceRoot, "main.mq5"),
                "void OnTick() {}");
            string staticJson = Path.Combine(root, "static.json");
            string staticReport = Path.Combine(root, "static.md");
            string evidenceJson = Path.Combine(root, "evidence.json");

            int exitCode = await ConversionInventoryCommand.RunAsync(
            [
                "--static-inventory",
                "--source-root", sourceRoot,
                "--manifest-output", staticJson,
                "--report-output", staticReport,
                "--conversion-evidence-output", evidenceJson
            ]);

            Assert.Equal(2, exitCode);
            Assert.False(File.Exists(staticJson));
            Assert.False(File.Exists(staticReport));
            Assert.False(File.Exists(evidenceJson));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExactWorkspaceCorpusHasOneFailClosedEvidenceRecordPerSourceFile()
    {
        string repositoryRoot = FindRepositoryRoot();
        string sourceRoot = Path.Combine(repositoryRoot, "Testing", "Mq5");
        var job = new Mql5CorpusInventoryJob(new Mql5StaticInventoryAnalyzer());
        using Mql5AnalyzedCorpus corpus = await job.AnalyzeDirectoryForPersistenceAsync(sourceRoot);
        Mql5ConversionCorpusEvidence evidence = new Mql5ConversionEvidenceAnalyzer()
            .Analyze(corpus.Documents);

        Assert.Equal(198, evidence.FileCount);
        Assert.Equal(166, evidence.Files.Count(static file => file.Kind == Mql5SourceKind.ExpertOrProgram));
        Assert.Equal(32, evidence.Files.Count(static file => file.Kind == Mql5SourceKind.Header));
        Assert.Equal(13_100_995, evidence.TotalBytes);
        Assert.Equal(
            "8052d74d395516aef01f221bf1a663b775ed02ccccbfa0476704d52112ee43b6",
            evidence.InputCorpusSha256);
        Assert.Equal(
            "c463d3a6de0eaef29b912cfb9af5bd949c0591b26896d866acb2c088943ba10a",
            evidence.DependencyGraphSha256);
        Assert.Equal(
            "6d4a18038f8b10ee8e4c68de55e96966d60293aa4d5186723e1363fae07537b1",
            evidence.EvidenceSha256);
        Assert.Equal(
            corpus.Manifest.Files.Select(static file => file.RelativePath),
            evidence.Files.Select(static file => file.RelativePath));
        Assert.Equal(
            evidence.FileCount,
            evidence.Files.Select(static file => file.RelativePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
        Assert.All(evidence.Files, file =>
        {
            Assert.Matches("^[0-9a-f]{64}$", file.SourceSha256);
            Assert.Matches("^[0-9a-f]{64}$", file.DependencyClosureSha256);
            Assert.Matches("^[0-9a-f]{64}$", file.EvidenceSha256);
            Assert.False(file.Structural.FullGrammarParseProven);
            Assert.False(file.Structural.TypeCheckProven);
            Assert.False(file.Structural.RestrictedIrLoweringProven);
            AssertStage(
                file,
                Mql5EvidenceStageName.SourceIntegrity,
                Mql5EvidenceStageStatus.Passed);
            Assert.DoesNotContain(
                file.Stages,
                stage => stage.Name is Mql5EvidenceStageName.TypeChecking
                    or Mql5EvidenceStageName.RestrictedIrLowering
                    && stage.Status == Mql5EvidenceStageStatus.Passed);
        });
        Assert.DoesNotContain(
            evidence.Files,
            file => file.Disposition.ToString().Contains("Converted", StringComparison.Ordinal));

        Assert.Equal(109, evidence.Files.Count(static file => file.TextEncoding == "utf-8"));
        Assert.Equal(35, evidence.Files.Count(static file => file.TextEncoding == "utf-8-bom"));
        Assert.Equal(44, evidence.Files.Count(static file => file.TextEncoding == "utf-16le"));
        Assert.Equal(5, evidence.Files.Count(static file => file.TextEncoding == "utf-16le-no-bom"));
        Assert.Equal(3, evidence.Files.Count(static file => file.TextEncoding == "windows-1252"));
        Assert.Single(evidence.Files, static file => file.TextEncoding == "binary-all-nul");
        Assert.Single(evidence.Files, static file => file.TextEncoding == "binary-non-text");

        Assert.Equal(
            194,
            evidence.Files.Count(static file => file.Stages.Any(static stage =>
                stage.Name == Mql5EvidenceStageName.LexicalAnalysis
                && stage.Status == Mql5EvidenceStageStatus.Passed)));
        Assert.Equal(
            194,
            evidence.Files.Count(static file => file.Stages.Any(static stage =>
                stage.Name == Mql5EvidenceStageName.StructuralParse
                && stage.Status == Mql5EvidenceStageStatus.Passed)));
        Assert.Equal(
            30,
            evidence.Files.Count(static file => file.Disposition
                == Mql5ConversionEvidenceDisposition.AwaitingIsolatedTypeCheck));
        Assert.Equal(
            121,
            evidence.Files.Count(static file => file.Disposition
                == Mql5ConversionEvidenceDisposition.BlockedUnsupportedSemantics));
        Assert.Equal(
            37,
            evidence.Files.Count(static file => file.Disposition
                == Mql5ConversionEvidenceDisposition.BlockedExternalDependencySnapshot));
        Assert.Equal(
            6,
            evidence.Files.Count(static file => file.Disposition
                == Mql5ConversionEvidenceDisposition.BlockedMissingDependency));
        Assert.Equal(
            2,
            evidence.Files.Count(static file => file.Disposition
                == Mql5ConversionEvidenceDisposition.BlockedInvalidSyntax));
        Assert.Single(
            evidence.Files,
            static file => file.Disposition
                == Mql5ConversionEvidenceDisposition.BlockedAllNulSource);
        Assert.Single(
            evidence.Files,
            static file => file.Disposition
                == Mql5ConversionEvidenceDisposition.BlockedBinarySource);

        Mql5DependencyEdgeEvidence[] includeEdges = evidence.Files
            .SelectMany(static file => file.Includes)
            .ToArray();
        Assert.Equal(
            10,
            includeEdges.Count(static edge => edge.Resolution
                == Mql5IncludeResolution.ResolvedInCorpus));
        Assert.Equal(
            7,
            includeEdges.Count(static edge => edge.Resolution
                == Mql5IncludeResolution.MissingSource));
        Assert.Equal(
            208,
            includeEdges.Count(static edge => edge.Resolution
                == Mql5IncludeResolution.PlatformLibrary));
        Assert.Equal(
            2,
            evidence.Files.SelectMany(static file => file.Findings)
                .Count(static finding => finding.Code == "LEXICAL_NUL_CHARACTERS_PRESENT"));
        Assert.Equal(
            3,
            evidence.Files.SelectMany(static file => file.Findings)
                .Count(static finding => finding.Code
                    == "LEXICAL_FORBIDDEN_CONTROL_CHARACTERS_PRESENT"));

        Mql5ConversionFileEvidence allNul = Assert.Single(
            evidence.Files,
            static file => file.RelativePath == "Simple_Classic_Trailing.mq5");
        Assert.Equal("binary-all-nul", allNul.TextEncoding);
        Assert.Equal(7_156, allNul.Lexical.NulCharacterCount);
        Mql5ConversionFileEvidence binary = Assert.Single(
            evidence.Files,
            static file => file.RelativePath == "Lopez Strategy EA.mq5");
        Assert.Equal("binary-non-text", binary.TextEncoding);
        Assert.Equal(Mql5ConversionEvidenceDisposition.BlockedBinarySource, binary.Disposition);

        string[] bomlessUtf16Paths = evidence.Files
            .Where(static file => file.TextEncoding == "utf-16le-no-bom")
            .Select(static file => file.RelativePath)
            .ToArray();
        Assert.Equal(
        [
            "EA Correlations.mq5",
            "MM3.0 FLIP CODEPRO (2).mq5",
            "MM3.0 FLIP CODEPRO.mq5",
            "Prop-Firm Expert.mq5",
            "XAU-GU Scalper.mq5"
        ],
            bomlessUtf16Paths);
    }

    private static Mql5ConversionCorpusEvidence Analyze(
        params (string Path, string Source)[] sources)
    {
        var analyzer = new Mql5ConversionEvidenceAnalyzer();
        return analyzer.Analyze(sources.Select(source => new Mql5SourceDocument(
            source.Path,
            Encoding.UTF8.GetBytes(source.Source))));
    }

    private static void AssertStage(
        Mql5ConversionFileEvidence file,
        Mql5EvidenceStageName name,
        Mql5EvidenceStageStatus status)
    {
        Mql5EvidenceStage stage = Assert.Single(file.Stages, stage => stage.Name == name);
        Assert.Equal(status, stage.Status);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "YO4X.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("The YO4X repository root was not found.");
    }
}
