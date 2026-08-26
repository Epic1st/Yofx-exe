using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
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

        Assert.DoesNotContain('\r', forwardJson);
        Assert.DoesNotContain('\r', report);
        Assert.EndsWith("\n", forwardJson, StringComparison.Ordinal);

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
    public void ConversionReportRendersHostileMetadataAsInertTableText()
    {
        const string HostilePath =
            "nested/[click](javascript-alert)<img src=x>`tick`&name|row.mq5";
        Mql5ConversionCorpusEvidence baseline = Analyze(("main.mq5", "void OnTick() {}"));
        Mql5ConversionCorpusEvidence evidence = baseline with
        {
            Files =
            [
                baseline.Files[0] with { RelativePath = HostilePath }
            ]
        };

        string report = Mql5ConversionEvidenceFormatter.ToMarkdown(evidence);

        Assert.DoesNotContain("[click](javascript-alert)", report, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`tick`", report, StringComparison.Ordinal);
        Assert.DoesNotContain("name|row", report, StringComparison.Ordinal);
        Assert.Contains(
            "&#91;click&#93;&#40;javascript-alert&#41;",
            report,
            StringComparison.Ordinal);
        Assert.Contains("&lt;img src=x&gt;", report, StringComparison.Ordinal);
        Assert.Contains("&#96;tick&#96;&amp;name&#124;row.mq5", report, StringComparison.Ordinal);
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
                "void OnTick() {}",
                TestContext.Current.CancellationToken);
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
            ], TestContext.Current.CancellationToken);

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
    public async Task CommandWritesDeterministicMetadataOnlyCompilePackagePlan()
    {
        string root = Path.Combine(Path.GetTempPath(), "yo4x-compile-plan-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string sourceRoot = Path.Combine(root, "source");
            Directory.CreateDirectory(sourceRoot);
            await File.WriteAllTextAsync(
                Path.Combine(sourceRoot, "main.mq5"),
                "void OnTick() {}",
                TestContext.Current.CancellationToken);
            string staticJson = Path.Combine(root, "static.json");
            string staticReport = Path.Combine(root, "static.md");
            string compilePlan = Path.Combine(root, "compile-plan.json");

            int exitCode = await ConversionInventoryCommand.RunAsync(
            [
                "--static-inventory",
                "--source-root", sourceRoot,
                "--manifest-output", staticJson,
                "--report-output", staticReport,
                "--compile-package-plan-output", compilePlan
            ], TestContext.Current.CancellationToken);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(staticJson));
            Assert.True(File.Exists(staticReport));
            string planJson = await File.ReadAllTextAsync(
                compilePlan,
                TestContext.Current.CancellationToken);
            JsonObject plan = Assert.IsType<JsonObject>(JsonNode.Parse(planJson));
            Assert.Single(Assert.IsType<JsonArray>(plan["targets"]));
            Assert.DoesNotContain("void OnTick", planJson, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CommandWritesSeparateDeterministicRestrictedIrArtifact()
    {
        string root = Path.Combine(Path.GetTempPath(), "yo4x-restricted-ir-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string sourceRoot = Path.Combine(root, "source");
            Directory.CreateDirectory(sourceRoot);
            await File.WriteAllTextAsync(
                Path.Combine(sourceRoot, "types.mqh"),
                "struct Signal { datetime time; double price; };",
                TestContext.Current.CancellationToken);
            string staticJson = Path.Combine(root, "static.json");
            string staticReport = Path.Combine(root, "static.md");
            string restrictedIr = Path.Combine(root, "restricted-ir.json");

            int exitCode = await ConversionInventoryCommand.RunAsync(
            [
                "--static-inventory",
                "--source-root", sourceRoot,
                "--manifest-output", staticJson,
                "--report-output", staticReport,
                "--restricted-ir-output", restrictedIr
            ], TestContext.Current.CancellationToken);

            Assert.Equal(0, exitCode);
            string json = await File.ReadAllTextAsync(
                restrictedIr,
                TestContext.Current.CancellationToken);
            JsonObject artifact = Assert.IsType<JsonObject>(JsonNode.Parse(json));
            Assert.Equal(1, artifact["fileCount"]?.GetValue<int>());
            Assert.Equal(1, artifact["attemptedCount"]?.GetValue<int>());
            Assert.Equal(1, artifact["loweredCount"]?.GetValue<int>());
            Assert.Equal(0, artifact["failedCount"]?.GetValue<int>());
            Assert.Matches("^[0-9a-f]{64}$", artifact["artifactSha256"]?.GetValue<string>());
            JsonObject file = Assert.IsType<JsonObject>(Assert.Single(
                Assert.IsType<JsonArray>(artifact["files"])));
            Assert.Equal("lowered", file["disposition"]?.GetValue<string>());
            Assert.NotNull(file["ir"]);
            Assert.Contains("Signal", json, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RestrictedCorpusArtifactIsDeterministicAndDoesNotChangeLegacyEvidence()
    {
        Mql5SourceDocument[] documents =
        [
            new("empty.mq5", []),
            new("runtime.mq5", Encoding.UTF8.GetBytes("void OnTick() {}"))
        ];
        var staticAnalyzer = new Mql5StaticInventoryAnalyzer();
        Mql5CorpusManifest manifest = staticAnalyzer.Analyze(documents);
        Mql5ConversionCorpusEvidence evidence = new Mql5ConversionEvidenceAnalyzer(staticAnalyzer)
            .Analyze(documents);
        string legacyJson = Mql5ConversionEvidenceFormatter.ToJson(evidence);

        Mql5RestrictedCorpusArtifact first = Mql5RestrictedCorpusCompiler.Compile(
            manifest,
            evidence,
            documents);
        Mql5RestrictedCorpusArtifact second = Mql5RestrictedCorpusCompiler.Compile(
            manifest,
            evidence,
            documents.Reverse());

        Assert.Equal(first.ArtifactSha256, second.ArtifactSha256);
        Assert.Equal(
            Mql5RestrictedCorpusArtifactFormatter.ToJson(first),
            Mql5RestrictedCorpusArtifactFormatter.ToJson(second));
        Assert.Equal(legacyJson, Mql5ConversionEvidenceFormatter.ToJson(evidence));
        Assert.Equal(2, first.AttemptedCount);
        Assert.Equal(1, first.LoweredCount);
        Assert.Equal(1, first.FailedCount);
        Assert.Equal(
            [Mql5RestrictedCorpusDisposition.Lowered, Mql5RestrictedCorpusDisposition.Failed],
            first.Files.Select(static file => file.Disposition));
    }

    [Fact]
    public void RestrictedCorpusArtifactRejectsConversionEvidenceNotRebuiltFromSources()
    {
        Mql5SourceDocument[] documents =
        [
            new("data.mqh", "struct Point { double x; double y; };"u8.ToArray())
        ];
        Mql5CorpusManifest manifest = new Mql5StaticInventoryAnalyzer().Analyze(documents);
        Mql5ConversionCorpusEvidence evidence = new Mql5ConversionEvidenceAnalyzer().Analyze(documents);
        Mql5ConversionCorpusEvidence tampered = evidence with
        {
            EvidenceSha256 = new string('0', 64)
        };

        ArgumentException failure = Assert.Throws<ArgumentException>(() =>
            Mql5RestrictedCorpusCompiler.Compile(manifest, tampered, documents));

        Assert.Equal("conversionEvidence", failure.ParamName);
    }

    [Fact]
    public void ArtifactOutputGuardAcceptsSixDistinctOutputsForCompleteInventoryRun()
    {
        string root = Path.Combine(Path.GetTempPath(), "yo4x-six-artifacts-" + Guid.NewGuid().ToString("N"));
        string sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceRoot);
        try
        {
            string[] outputs = Enumerable.Range(1, 6)
                .Select(index => Path.Combine(root, $"artifact-{index}.json"))
                .ToArray();

            Mql5ArtifactPathSet paths = Mql5ArtifactOutputGuard.Resolve(sourceRoot, outputs);

            Assert.Equal(6, paths.OutputPaths.Count);
            Assert.Equal(6, paths.OutputPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CommandRejectsSecretBeforeWritingAnyArtifact()
    {
        string root = Path.Combine(Path.GetTempPath(), "yo4x-secret-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string syntheticToken = string.Concat("246813579", ":", new string('C', 35));
        try
        {
            string sourceRoot = Path.Combine(root, "source");
            Directory.CreateDirectory(sourceRoot);
            await File.WriteAllTextAsync(
                Path.Combine(sourceRoot, "main.mq5"),
                "input string TelegramBotToken = \"" + syntheticToken + "\";",
                TestContext.Current.CancellationToken);
            string[] outputs =
            [
                Path.Combine(root, "static.json"),
                Path.Combine(root, "static.md"),
                Path.Combine(root, "conversion.json"),
                Path.Combine(root, "conversion.md"),
                Path.Combine(root, "compile-plan.json")
            ];

            int exitCode = await ConversionInventoryCommand.RunAsync(
            [
                "--static-inventory",
                "--source-root", sourceRoot,
                "--manifest-output", outputs[0],
                "--report-output", outputs[1],
                "--conversion-evidence-output", outputs[2],
                "--conversion-evidence-report-output", outputs[3],
                "--compile-package-plan-output", outputs[4]
            ], TestContext.Current.CancellationToken);

            Assert.Equal(2, exitCode);
            Assert.All(outputs, static output => Assert.False(File.Exists(output)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, "main.mq5")]
    [InlineData(1, "nested/report.md")]
    [InlineData(2, "case-alias/evidence.json")]
    [InlineData(3, "nested/evidence.md")]
    [InlineData(4, "nested/compile-plan.json")]
    public async Task StaticInventoryCommandRejectsEveryOutputInsideSourceRoot(
        int hostileOutputIndex,
        string relativeHostileOutput)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "yo4x-evidence-output-boundary-" + Guid.NewGuid().ToString("N"));
        string sourceRoot = Path.Combine(root, "Source");
        Directory.CreateDirectory(sourceRoot);
        try
        {
            string sourcePath = Path.Combine(sourceRoot, "main.mq5");
            const string Source = "void OnTick() {}";
            await File.WriteAllTextAsync(
                sourcePath,
                Source,
                TestContext.Current.CancellationToken);
            string[] outputs =
            [
                Path.Combine(root, "static.json"),
                Path.Combine(root, "static.md"),
                Path.Combine(root, "evidence.json"),
                Path.Combine(root, "evidence.md"),
                Path.Combine(root, "compile-plan.json")
            ];
            string aliasedSourceRoot = hostileOutputIndex == 2
                ? Path.Combine(root, "sOURCE")
                : sourceRoot;
            outputs[hostileOutputIndex] = Path.Combine(
                aliasedSourceRoot,
                relativeHostileOutput.Replace('/', Path.DirectorySeparatorChar));

            int exitCode = await ConversionInventoryCommand.RunAsync(
            [
                "--static-inventory",
                "--source-root", sourceRoot,
                "--manifest-output", outputs[0],
                "--report-output", outputs[1],
                "--conversion-evidence-output", outputs[2],
                "--conversion-evidence-report-output", outputs[3],
                "--compile-package-plan-output", outputs[4]
            ], TestContext.Current.CancellationToken);

            Assert.Equal(2, exitCode);
            Assert.Equal(Source, await File.ReadAllTextAsync(
                sourcePath,
                TestContext.Current.CancellationToken));
            Assert.False(File.Exists(Path.Combine(root, "static.json")));
            Assert.False(File.Exists(Path.Combine(root, "static.md")));
            Assert.False(File.Exists(Path.Combine(root, "evidence.json")));
            Assert.False(File.Exists(Path.Combine(root, "evidence.md")));
            Assert.False(File.Exists(Path.Combine(root, "compile-plan.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ArtifactOutputGuardRejectsAReparsePointAncestor()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "yo4x-evidence-output-reparse-" + Guid.NewGuid().ToString("N"));
        string sourceRoot = Path.Combine(root, "source");
        string targetRoot = Path.Combine(root, "target");
        string linkRoot = Path.Combine(root, "linked-output");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(targetRoot);
        try
        {
            CreateDirectoryJunction(linkRoot, targetRoot);

            IOException error = Assert.Throws<IOException>(() =>
                Mql5ArtifactOutputGuard.Resolve(
                    sourceRoot,
                    Path.Combine(linkRoot, "manifest.json"),
                    Path.Combine(root, "report.md")));

            Assert.Equal(
                "Artifact paths and their existing ancestors cannot be reparse points.",
                error.Message);
        }
        finally
        {
            if (Directory.Exists(linkRoot))
            {
                Directory.Delete(linkRoot);
            }

            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ArtifactOutputGuardRejectsWindowsNamespaceAndDosShortNameAliases()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = Path.Combine(
            Path.GetTempPath(),
            "yo4x-evidence-output-alias-" + Guid.NewGuid().ToString("N"));
        string sourceRoot = Path.Combine(root, "SourceDirectory");
        Directory.CreateDirectory(sourceRoot);
        try
        {
            string sourcePath = Path.Combine(sourceRoot, "main.mq5");
            File.WriteAllText(sourcePath, "void OnTick() {}");

            Assert.Throws<ArgumentException>(() => Mql5ArtifactOutputGuard.Resolve(
                sourceRoot,
                @"\\?\" + sourcePath,
                Path.Combine(root, "report.md")));
            Assert.Throws<ArgumentException>(() => Mql5ArtifactOutputGuard.Resolve(
                sourceRoot,
                Path.Combine(root, "SOURCE~1", "manifest.json"),
                Path.Combine(root, "report.md")));
            Assert.Equal("void OnTick() {}", File.ReadAllText(sourcePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ArtifactOutputGuardRejectsASecondDriveAliasForTheSourceRoot()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = Path.Combine(
            Path.GetTempPath(),
            "yo4x-evidence-output-drive-alias-" + Guid.NewGuid().ToString("N"));
        string sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceRoot);
        string drive = Enumerable.Range('D', 'Z' - 'D' + 1)
            .Reverse()
            .Select(static code => $"{(char)code}:")
            .First(static candidate => !Directory.Exists(candidate + Path.DirectorySeparatorChar));
        bool driveMapped = false;
        try
        {
            RunSubst(drive, sourceRoot);
            driveMapped = true;

            Assert.Throws<ArgumentException>(() => Mql5ArtifactOutputGuard.Resolve(
                sourceRoot,
                Path.Combine(drive + Path.DirectorySeparatorChar, "manifest.json"),
                Path.Combine(root, "report.md")));
        }
        finally
        {
            if (driveMapped)
            {
                RunSubst(drive, target: null);
            }

            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ArtifactOutputGuardRejectsTwoOutputsThatResolveToTheSamePhysicalPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = Path.Combine(
            Path.GetTempPath(),
            "yo4x-evidence-output-duplicate-alias-" + Guid.NewGuid().ToString("N"));
        string sourceRoot = Path.Combine(root, "source");
        string outputRoot = Path.Combine(root, "output");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(outputRoot);
        string drive = Enumerable.Range('D', 'Z' - 'D' + 1)
            .Reverse()
            .Select(static code => $"{(char)code}:")
            .First(static candidate => !Directory.Exists(candidate + Path.DirectorySeparatorChar));
        bool driveMapped = false;
        try
        {
            RunSubst(drive, outputRoot);
            driveMapped = true;

            ArgumentException error = Assert.Throws<ArgumentException>(() =>
                Mql5ArtifactOutputGuard.Resolve(
                    sourceRoot,
                    Path.Combine(outputRoot, "artifact.json"),
                    Path.Combine(drive + Path.DirectorySeparatorChar, "artifact.json")));

            Assert.Contains("different physical path", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (driveMapped)
            {
                RunSubst(drive, target: null);
            }

            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExactWorkspaceCorpusHasOneFailClosedEvidenceRecordPerSourceFile()
    {
        string repositoryRoot = FindRepositoryRoot();
        string sourceRoot = Path.Combine(repositoryRoot, "Testing", "Mq5");
        var job = new Mql5CorpusInventoryJob(new Mql5StaticInventoryAnalyzer());
        using Mql5AnalyzedCorpus corpus = await job.AnalyzeDirectoryForPersistenceAsync(
            sourceRoot,
            TestContext.Current.CancellationToken);
        Mql5ConversionCorpusEvidence evidence = new Mql5ConversionEvidenceAnalyzer()
            .Analyze(corpus.Documents);

        Assert.Equal(198, evidence.FileCount);
        Assert.Equal(166, evidence.Files.Count(static file => file.Kind == Mql5SourceKind.ExpertOrProgram));
        Assert.Equal(32, evidence.Files.Count(static file => file.Kind == Mql5SourceKind.Header));
        Assert.Equal(12_979_438, evidence.TotalBytes);
        Assert.Equal(
            "9a53e844cfd3ffe5dfcf28544bb4909ce69741ac6a373e80b139f8227779dd47",
            evidence.InputCorpusSha256);
        Assert.Equal(
            "c463d3a6de0eaef29b912cfb9af5bd949c0591b26896d866acb2c088943ba10a",
            evidence.DependencyGraphSha256);
        Assert.Equal(
            "e191d8a5b1e572f08b16d420edfef5a8f386b003dbc0e2b122ae201a16c065b7",
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

    private static void CreateDirectoryJunction(string link, string target)
    {
        string command = $"mklink /J \"{link}\" \"{target}\"";
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = $"/d /s /c \"{command}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start the junction creation process.");
        }

        process.WaitForExit();
        if (process.ExitCode != 0 || !Directory.Exists(link))
        {
            throw new InvalidOperationException("Could not create a disposable test reparse point.");
        }
    }

    private static void RunSubst(string drive, string? target)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "subst.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add(drive);
        if (target is null)
        {
            process.StartInfo.ArgumentList.Add("/D");
        }
        else
        {
            process.StartInfo.ArgumentList.Add(target);
        }

        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start the disposable drive-alias command.");
        }

        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("Could not update the disposable drive alias.");
        }
    }
}
