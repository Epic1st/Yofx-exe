using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using YO4X.BuildingBlocks;
using YO4X.StrategyGovernance;

namespace YO4X.Domain.Tests;

public sealed class Mql5CompilePackageDossierPlannerTests
{
    private static Mql5ApprovedPlatformLibrarySnapshot ApprovedPlatformSnapshot { get; } = new(
        "approved-platform-snapshot-1",
        new string('d', 64),
        new string('a', 64));

    [Fact]
    public void BuildsAStableDependencyFirstPackageAndExcludesUnrelatedSources()
    {
        Mql5SourceDocument[] documents = CreateDocuments(
            ("main.mq5", "#include \"lib/a.mqh\"\n#include \"lib/b.mqh\"\nvoid OnTick() { A(); B(); }"),
            ("lib/a.mqh", "#include \"b.mqh\"\nvoid A() { B(); }"),
            ("lib/b.mqh", "void B() {}"),
            ("unrelated.mqh", "void Unrelated() {}"));
        byte[][] callerBytes = documents.Select(static document => document.Content.ToArray()).ToArray();
        Mql5CorpusManifest manifest = new Mql5StaticInventoryAnalyzer().Analyze(documents);
        Mql5ConversionCorpusEvidence evidence = new Mql5ConversionEvidenceAnalyzer().Analyze(documents);

        Mql5CompilePackagePlan forward = Mql5CompilePackageDossierPlanner.Plan(
            manifest,
            evidence,
            documents,
            ApprovedPlatformSnapshot);
        Mql5CompilePackagePlan reverse = Mql5CompilePackageDossierPlanner.Plan(
            manifest,
            evidence,
            documents.Reverse().ToArray(),
            ApprovedPlatformSnapshot);

        Mql5TargetCompilePackageDossier package = Assert.Single(forward.Targets);
        Assert.True(package.IsReadyForIsolatedCompile);
        Assert.True(package.DependencyFirstOrderProven);
        Assert.Equal(
            ["lib/b.mqh", "lib/a.mqh", "main.mq5"],
            package.OrderedSources.Select(static source => source.RelativePath));
        Assert.DoesNotContain(
            package.OrderedSources,
            static source => source.RelativePath == "unrelated.mqh");
        Assert.Equal(
            ["lib/a.mqh", "main.mq5", "main.mq5"],
            package.OrderedIncludeEdges.Select(static edge => edge.SourceRelativePath));
        Assert.Equal(package.PackageSha256, Assert.Single(reverse.Targets).PackageSha256);
        Assert.Equal(forward.PlanSha256, reverse.PlanSha256);
        Assert.Equal(
            Mql5CompilePackagePlanFormatter.ToJson(forward),
            Mql5CompilePackagePlanFormatter.ToJson(reverse));
        Assert.DoesNotContain("void OnTick", Mql5CompilePackagePlanFormatter.ToJson(forward));
        Assert.Matches("^[0-9a-f]{64}$", package.SourceClosureSha256);
        Assert.Matches("^[0-9a-f]{64}$", package.PackageSha256);
        for (int index = 0; index < documents.Length; index++)
        {
            Assert.Equal(callerBytes[index], documents[index].Content);
        }
    }

    [Fact]
    public void ClassifiesEveryDependencyAndSemanticBlockerWithoutExecution()
    {
        Mql5SourceDocument[] documents = CreateDocuments(
            ("ready.mq5", "void OnTick() {}"),
            ("missing.mq5", "#include \"not-present.mqh\"\nvoid OnTick() {}"),
            ("ambiguous.mq5", "#include <common.mqh>\nvoid OnTick() {}"),
            ("a/common.mqh", "void CommonA() {}"),
            ("b/common.mqh", "void CommonB() {}"),
            ("invalid.mq5", "#include \"/absolute.mqh\"\nvoid OnTick() {}"),
            ("cycle.mq5", "#include \"cycle/a.mqh\"\nvoid OnTick() {}"),
            ("cycle/a.mqh", "#include \"b.mqh\"\nvoid A() {}"),
            ("cycle/b.mqh", "#include \"a.mqh\"\nvoid B() {}"),
            ("unsupported.mq5", "void OnTick() { FileOpen(\"x\", 0); }"),
            ("platform.mq5", "#include <Trade/Trade.mqh>\nvoid OnTick() {}"));
        Mql5CompilePackagePlan plan = Plan(documents);

        AssertDisposition(plan, "ready.mq5", Mql5CompilePackageDisposition.ReadyForIsolatedCompile);
        AssertDisposition(plan, "missing.mq5", Mql5CompilePackageDisposition.BlockedMissingDependency);
        AssertDisposition(plan, "ambiguous.mq5", Mql5CompilePackageDisposition.BlockedAmbiguousDependency);
        AssertDisposition(plan, "invalid.mq5", Mql5CompilePackageDisposition.BlockedInvalidDependency);
        AssertDisposition(plan, "cycle.mq5", Mql5CompilePackageDisposition.BlockedDependencyCycle);
        AssertDisposition(plan, "unsupported.mq5", Mql5CompilePackageDisposition.BlockedUnsupportedSemantics);
        Mql5TargetCompilePackageDossier platform = AssertDisposition(
            plan,
            "platform.mq5",
            Mql5CompilePackageDisposition.BlockedPlatformSnapshot);
        Assert.Contains(
            platform.Blockers,
            static blocker => blocker.Kind == Mql5CompilePackageBlockerKind.PlatformSnapshotRequired
                && blocker.DeclaredPath == "Trade/Trade.mqh");
        Assert.Equal(
            ApprovedPlatformSnapshot.SnapshotSha256,
            platform.ApprovedPlatformLibrarySnapshotSha256);
        Assert.Equal(
            ApprovedPlatformSnapshot.ApprovalSha256,
            platform.PlatformLibrarySnapshotApprovalSha256);
        Assert.False(platform.IsReadyForIsolatedCompile);
    }

    [Fact]
    public void MinimumViableStructureGateBlocksDegenerateCanonicalSources()
    {
        (string Description, byte[] Content, Mql5CompilePackageDisposition ExpectedDisposition, Mql5CompilePackageBlockerKind? ExpectedBlocker)[] cases =
        [
            (
                "bom-only stub",
                [0xEF, 0xBB, 0xBF],
                Mql5CompilePackageDisposition.BlockedInvalidSyntax,
                Mql5CompilePackageBlockerKind.InvalidSyntax),
            (
                "all-nul source",
                new byte[24],
                Mql5CompilePackageDisposition.BlockedAllNulSource,
                Mql5CompilePackageBlockerKind.AllNulSource),
            (
                "normal source",
                Encoding.UTF8.GetBytes("void OnTick() { int ready = 1; }\n"),
                Mql5CompilePackageDisposition.ReadyForIsolatedCompile,
                null)
        ];

        foreach ((string description, byte[] content, Mql5CompilePackageDisposition expected, Mql5CompilePackageBlockerKind? expectedBlocker) in cases)
        {
            Mql5SourceDocument[] documents = [new("degenerate.mq5", content)];
            Mql5CorpusManifest manifest = new Mql5StaticInventoryAnalyzer().Analyze(documents);
            Mql5ConversionCorpusEvidence evidence = new Mql5ConversionEvidenceAnalyzer().Analyze(documents);
            Mql5TargetCompilePackageDossier dossier = Assert.Single(
                Mql5CompilePackageDossierPlanner.Plan(
                    manifest,
                    evidence,
                    documents,
                    ApprovedPlatformSnapshot).Targets);

            Assert.True(
                dossier.IntrinsicDisposition != Mql5CompilePackageDisposition.ReadyForIsolatedCompile
                || expected == Mql5CompilePackageDisposition.ReadyForIsolatedCompile,
                $"{description} must not be intrinsically ready.");
            Assert.Equal(expected, dossier.IntrinsicDisposition);
            Assert.Equal(expected, dossier.Disposition);
            Assert.Equal(
                expected == Mql5CompilePackageDisposition.ReadyForIsolatedCompile,
                dossier.IsReadyForIsolatedCompile);
            if (expectedBlocker is { } blockerKind)
            {
                Assert.Contains(
                    dossier.Blockers,
                    blocker => blocker.Kind == blockerKind
                        && blocker.SourceRelativePath == "degenerate.mq5");
            }
            else
            {
                Assert.Empty(dossier.Blockers);
            }
        }
    }

    [Fact]
    public void FailsClosedOnSourceStaticConversionAndDossierDrift()
    {
        Mql5SourceDocument[] documents = CreateDocuments(("main.mq5", "void OnTick() {}"));
        Mql5CorpusManifest manifest = new Mql5StaticInventoryAnalyzer().Analyze(documents);
        Mql5ConversionCorpusEvidence evidence = new Mql5ConversionEvidenceAnalyzer().Analyze(documents);
        Mql5TargetCompilePackageDossier package = Assert.Single(
            Mql5CompilePackageDossierPlanner.Plan(
                manifest,
                evidence,
                documents,
                ApprovedPlatformSnapshot).Targets);

        Mql5SourceManifest changedManifestFile = manifest.Files[0] with { Entrypoints = [] };
        Mql5CompilePackagePlanningException staticDrift = Assert.Throws<Mql5CompilePackagePlanningException>(
            () => Mql5CompilePackageDossierPlanner.Plan(
                manifest with { Files = [changedManifestFile] },
                evidence,
                documents,
                ApprovedPlatformSnapshot));
        Assert.Equal("STATIC_MANIFEST_CONTENT_DRIFT", staticDrift.ReasonCode);

        Mql5ConversionFileEvidence changedEvidenceFile = evidence.Files[0] with
        {
            EvidenceSha256 = new string('a', 64)
        };
        Mql5CompilePackagePlanningException conversionDrift = Assert.Throws<Mql5CompilePackagePlanningException>(
            () => Mql5CompilePackageDossierPlanner.Plan(
                manifest,
                evidence with { Files = [changedEvidenceFile] },
                documents,
                ApprovedPlatformSnapshot));
        Assert.Equal("CONVERSION_EVIDENCE_CONTENT_DRIFT", conversionDrift.ReasonCode);

        Mql5SourceDocument[] changedSources = CreateDocuments(("main.mq5", "void OnTick() { int drift = 1; }"));
        Mql5CompilePackagePlanningException sourceDrift = Assert.Throws<Mql5CompilePackagePlanningException>(
            () => Mql5CompilePackageDossierPlanner.Plan(
                manifest,
                evidence,
                changedSources,
                ApprovedPlatformSnapshot));
        Assert.Equal("SOURCE_HASH_DRIFT_DETECTED", sourceDrift.ReasonCode);

        Mql5CompilePackagePlanningException packageDrift = Assert.Throws<Mql5CompilePackagePlanningException>(
            () => Mql5CompilePackageDossierPlanner.ValidateForDispatch(
                manifest,
                evidence,
                documents,
                package with { SourceClosureSha256 = new string('b', 64) },
                ApprovedPlatformSnapshot));
        Assert.Equal("COMPILE_PACKAGE_CONTENT_DRIFT", packageDrift.ReasonCode);
    }

    [Fact]
    public void PublicPlannerBoundariesRejectSizeLimitsBeforeCloningCallerBytes()
    {
        Mql5SourceDocument[] baseline = CreateDocuments(("main.mq5", "void OnTick() {}"));
        Mql5CorpusManifest manifest = new Mql5StaticInventoryAnalyzer().Analyze(baseline);
        Mql5ConversionCorpusEvidence evidence = new Mql5ConversionEvidenceAnalyzer().Analyze(baseline);
        Mql5TargetCompilePackageDossier package = Assert.Single(
            Mql5CompilePackageDossierPlanner.Plan(
                manifest,
                evidence,
                baseline,
                ApprovedPlatformSnapshot).Targets);
        byte[] oversized = new byte[4 * 1024 * 1024 + 1];
        Array.Fill(oversized, (byte)0x5a);
        string oversizedBefore = Convert.ToHexString(SHA256.HashData(oversized));
        Mql5SourceDocument[] oversizedSource = [new("main.mq5", oversized)];

        Mql5CompilePackagePlanningException planOversized = Assert.Throws<Mql5CompilePackagePlanningException>(
            () => Mql5CompilePackageDossierPlanner.Plan(
                manifest,
                evidence,
                oversizedSource,
                ApprovedPlatformSnapshot));
        Assert.Equal("SOURCE_SIZE_LIMIT_EXCEEDED", planOversized.ReasonCode);
        Mql5CompilePackagePlanningException dispatchOversized = Assert.Throws<Mql5CompilePackagePlanningException>(
            () => Mql5CompilePackageDossierPlanner.ValidateForDispatch(
                manifest,
                evidence,
                oversizedSource,
                package,
                ApprovedPlatformSnapshot));
        Assert.Equal("SOURCE_SIZE_LIMIT_EXCEEDED", dispatchOversized.ReasonCode);
        Assert.Equal(oversizedBefore, Convert.ToHexString(SHA256.HashData(oversized)));

        byte[] sharedFourMiB = new byte[4 * 1024 * 1024];
        Array.Fill(sharedFourMiB, (byte)0xa5);
        string aggregateBefore = Convert.ToHexString(SHA256.HashData(sharedFourMiB));
        Mql5SourceDocument[] aggregate = Enumerable.Range(0, 65)
            .Select(index => new Mql5SourceDocument($"logical-{index:D2}.mqh", sharedFourMiB))
            .ToArray();
        Mql5CompilePackagePlanningException aggregateLimit = Assert.Throws<Mql5CompilePackagePlanningException>(
            () => Mql5CompilePackageDossierPlanner.Plan(
                manifest,
                evidence,
                aggregate,
                ApprovedPlatformSnapshot));
        Assert.Equal("SOURCE_SIZE_LIMIT_EXCEEDED", aggregateLimit.ReasonCode);
        Assert.Equal(aggregateBefore, Convert.ToHexString(SHA256.HashData(sharedFourMiB)));
    }

    [Fact]
    public void DispatchValidationRequiresTheExactClosureInTheExactOrder()
    {
        Mql5SourceDocument[] documents = CreateDocuments(
            ("main.mq5", "#include \"lib/a.mqh\"\nvoid OnTick() { A(); }"),
            ("lib/a.mqh", "void A() {}"),
            ("unrelated.mqh", "void Unrelated() {}"));
        Mql5CorpusManifest manifest = new Mql5StaticInventoryAnalyzer().Analyze(documents);
        Mql5ConversionCorpusEvidence evidence = new Mql5ConversionEvidenceAnalyzer().Analyze(documents);
        Mql5TargetCompilePackageDossier package = Assert.Single(
            Mql5CompilePackageDossierPlanner.Plan(
                manifest,
                evidence,
                documents,
                ApprovedPlatformSnapshot).Targets);
        Dictionary<string, Mql5SourceDocument> byPath = documents.ToDictionary(
            static document => document.RelativePath,
            StringComparer.OrdinalIgnoreCase);
        Mql5SourceDocument[] closure = package.OrderedSources
            .Select(source => byPath[source.RelativePath])
            .ToArray();

        Mql5TargetCompilePackageDossier validated =
            Mql5CompilePackageDossierPlanner.ValidateForDispatch(
                manifest,
                evidence,
                closure,
                package,
                ApprovedPlatformSnapshot);
        Assert.Equal(package.PackageSha256, validated.PackageSha256);

        Mql5CompilePackagePlanningException extra = Assert.Throws<Mql5CompilePackagePlanningException>(
            () => Mql5CompilePackageDossierPlanner.ValidateForDispatch(
                manifest,
                evidence,
                closure.Append(byPath["unrelated.mqh"]).ToArray(),
                package,
                ApprovedPlatformSnapshot));
        Assert.Equal("COMPILE_PACKAGE_SOURCE_SET_INVALID", extra.ReasonCode);

        Mql5CompilePackagePlanningException reordered = Assert.Throws<Mql5CompilePackagePlanningException>(
            () => Mql5CompilePackageDossierPlanner.ValidateForDispatch(
                manifest,
                evidence,
                closure.Reverse().ToArray(),
                package,
                ApprovedPlatformSnapshot));
        Assert.Equal("COMPILE_PACKAGE_SOURCE_SET_INVALID", reordered.ReasonCode);
    }

    [Fact]
    public void AbsentApprovalBlocksDispatchAndAHashCannotResolvePlatformIncludes()
    {
        Mql5SourceDocument[] documents = CreateDocuments(
            ("ready.mq5", "void OnTick() {}"),
            ("platform.mq5", "#include <Trade/Trade.mqh>\nvoid OnTick() {}"));
        Mql5CorpusManifest manifest = new Mql5StaticInventoryAnalyzer().Analyze(documents);
        Mql5ConversionCorpusEvidence evidence = new Mql5ConversionEvidenceAnalyzer().Analyze(documents);
        Mql5CompilePackagePlan absent = Mql5CompilePackageDossierPlanner.Plan(
            manifest,
            evidence,
            documents,
            approvedPlatformLibrarySnapshot: null);

        Assert.Null(absent.PlatformLibrarySnapshotApprovalId);
        Assert.Null(absent.ApprovedPlatformLibrarySnapshotSha256);
        Assert.Null(absent.PlatformLibrarySnapshotApprovalSha256);
        Mql5TargetCompilePackageDossier ready = Assert.Single(
            absent.Targets,
            static target => target.TargetRelativePath == "ready.mq5");
        Assert.Equal(Mql5CompilePackageDisposition.ReadyForIsolatedCompile, ready.IntrinsicDisposition);
        Assert.Equal(
            Mql5CompilePackageDisposition.BlockedApprovedPlatformSnapshotUnavailable,
            ready.Disposition);
        Assert.Contains(
            ready.Blockers,
            static blocker => blocker.Kind
                == Mql5CompilePackageBlockerKind.ApprovedPlatformSnapshotUnavailable);
        Mql5TargetCompilePackageDossier platform = Assert.Single(
            absent.Targets,
            static target => target.TargetRelativePath == "platform.mq5");
        Assert.Equal(Mql5CompilePackageDisposition.BlockedPlatformSnapshot, platform.IntrinsicDisposition);
        Assert.Equal(Mql5CompilePackageDisposition.BlockedPlatformSnapshot, platform.Disposition);
        Assert.Contains(
            platform.Blockers,
            static blocker => blocker.Kind
                == Mql5CompilePackageBlockerKind.ApprovedPlatformSnapshotUnavailable);

        Mql5CompilePackagePlanningException unavailable =
            Assert.Throws<Mql5CompilePackagePlanningException>(() =>
                Mql5CompilePackageDossierPlanner.ValidateForDispatch(
                    manifest,
                    evidence,
                    [documents[0]],
                    ready,
                    approvedPlatformLibrarySnapshot: null));
        Assert.Equal("APPROVED_PLATFORM_SNAPSHOT_NOT_CONFIGURED", unavailable.ReasonCode);

        var callerFabricatedApproval = new Mql5ApprovedPlatformLibrarySnapshot(
            "caller-fabricated-snapshot",
            new string('f', 64),
            new string('b', 64));
        Mql5TargetCompilePackageDossier stillBlocked = Assert.Single(
            Mql5CompilePackageDossierPlanner.Plan(
                manifest,
                evidence,
                documents,
                callerFabricatedApproval).Targets,
            static target => target.TargetRelativePath == "platform.mq5");
        Assert.Equal(Mql5CompilePackageDisposition.BlockedPlatformSnapshot, stillBlocked.Disposition);
        Assert.Contains(
            stillBlocked.Blockers,
            static blocker => blocker.Kind == Mql5CompilePackageBlockerKind.PlatformSnapshotRequired);
    }

    [Fact]
    public void PublicPlannerUsesBoundedCountAndIndexerWithoutCallerEnumeration()
    {
        Mql5SourceDocument[] documents = CreateDocuments(("main.mq5", "void OnTick() {}"));
        Mql5CorpusManifest manifest = new Mql5StaticInventoryAnalyzer().Analyze(documents);
        Mql5ConversionCorpusEvidence evidence = new Mql5ConversionEvidenceAnalyzer().Analyze(documents);
        Mql5TargetCompilePackageDossier package = Assert.Single(
            Mql5CompilePackageDossierPlanner.Plan(
                manifest,
                evidence,
                documents,
                ApprovedPlatformSnapshot).Targets);

        var planSources = new EnumeratorBombSourceCollection(documents[0]);
        Mql5CompilePackagePlan plan = Mql5CompilePackageDossierPlanner.Plan(
            manifest,
            evidence,
            planSources,
            ApprovedPlatformSnapshot);
        Assert.Single(plan.Targets);
        Assert.Equal(1, planSources.IndexerAccessCount);
        Assert.Equal(0, planSources.EnumeratorAccessCount);

        var dispatchSources = new EnumeratorBombSourceCollection(documents[0]);
        Mql5TargetCompilePackageDossier validated =
            Mql5CompilePackageDossierPlanner.ValidateForDispatch(
                manifest,
                evidence,
                dispatchSources,
                package,
                ApprovedPlatformSnapshot);
        Assert.Equal(package.PackageSha256, validated.PackageSha256);
        Assert.Equal(1, dispatchSources.IndexerAccessCount);
        Assert.Equal(0, dispatchSources.EnumeratorAccessCount);

        var oversizedPlanSources = new OversizedCountSourceCollection();
        Mql5CompilePackagePlanningException oversizedPlan =
            Assert.Throws<Mql5CompilePackagePlanningException>(() =>
                Mql5CompilePackageDossierPlanner.Plan(
                    manifest,
                    evidence,
                    oversizedPlanSources,
                    ApprovedPlatformSnapshot));
        Assert.Equal("SOURCE_CORPUS_INVALID", oversizedPlan.ReasonCode);
        Assert.Equal(0, oversizedPlanSources.IndexerAccessCount);
        Assert.Equal(0, oversizedPlanSources.EnumeratorAccessCount);

        var oversizedDispatchSources = new OversizedCountSourceCollection();
        Mql5CompilePackagePlanningException oversizedDispatch =
            Assert.Throws<Mql5CompilePackagePlanningException>(() =>
                Mql5CompilePackageDossierPlanner.ValidateForDispatch(
                    manifest,
                    evidence,
                    oversizedDispatchSources,
                    package,
                    ApprovedPlatformSnapshot));
        Assert.Equal("SOURCE_CORPUS_INVALID", oversizedDispatch.ReasonCode);
        Assert.Equal(0, oversizedDispatchSources.IndexerAccessCount);
        Assert.Equal(0, oversizedDispatchSources.EnumeratorAccessCount);

        var faultingPlanSources = new FaultingSourceCollection();
        Mql5CompilePackagePlanningException faultingPlan =
            Assert.Throws<Mql5CompilePackagePlanningException>(() =>
                Mql5CompilePackageDossierPlanner.Plan(
                    manifest,
                    evidence,
                    faultingPlanSources,
                    ApprovedPlatformSnapshot));
        Assert.Equal("SOURCE_CORPUS_INVALID", faultingPlan.ReasonCode);
        Assert.Equal(1, faultingPlanSources.IndexerAccessCount);
        Assert.Equal(0, faultingPlanSources.EnumeratorAccessCount);

        var faultingDispatchSources = new FaultingSourceCollection();
        Mql5CompilePackagePlanningException faultingDispatch =
            Assert.Throws<Mql5CompilePackagePlanningException>(() =>
                Mql5CompilePackageDossierPlanner.ValidateForDispatch(
                    manifest,
                    evidence,
                    faultingDispatchSources,
                    package,
                    ApprovedPlatformSnapshot));
        Assert.Equal("SOURCE_CORPUS_INVALID", faultingDispatch.ReasonCode);
        Assert.Equal(1, faultingDispatchSources.IndexerAccessCount);
        Assert.Equal(0, faultingDispatchSources.EnumeratorAccessCount);
    }

    [Fact]
    public void ExactSuppliedCorpusProducesTwelveIntrinsicCandidatesButZeroDispatchReadyDossiers()
    {
        string repositoryRoot = FindRepositoryRoot();
        string sourceRoot = Path.Combine(repositoryRoot, "Testing", "Mq5");
        string[] paths = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(static path => Path.GetExtension(path) is ".mq5" or ".mqh"
                || Path.GetExtension(path).Equals(".mq5", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(path).Equals(".mqh", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        Mql5SourceDocument[] documents = paths.Select(path => new Mql5SourceDocument(
                Path.GetRelativePath(sourceRoot, path).Replace('\\', '/'),
                File.ReadAllBytes(path)))
            .ToArray();
        try
        {
            Mql5CorpusManifest manifest = new Mql5StaticInventoryAnalyzer().Analyze(documents);
            Mql5ConversionCorpusEvidence evidence = new Mql5ConversionEvidenceAnalyzer().Analyze(documents);
            Mql5CompilePackagePlan plan = Mql5CompilePackageDossierPlanner.Plan(
                manifest,
                evidence,
                documents,
                approvedPlatformLibrarySnapshot: null);

            Assert.Equal(198, manifest.FileCount);
            Assert.Equal(166, plan.Targets.Count);
            Assert.Equal(
                "9a53e844cfd3ffe5dfcf28544bb4909ce69741ac6a373e80b139f8227779dd47",
                plan.CorpusSha256);
            Assert.Null(plan.PlatformLibrarySnapshotApprovalId);
            Assert.Null(plan.ApprovedPlatformLibrarySnapshotSha256);
            Assert.Null(plan.PlatformLibrarySnapshotApprovalSha256);
            Assert.Equal(0, Count(plan, Mql5CompilePackageDisposition.ReadyForIsolatedCompile));
            Assert.Equal(
                12,
                Count(
                    plan,
                    Mql5CompilePackageDisposition.BlockedApprovedPlatformSnapshotUnavailable));
            Assert.Equal(36, Count(plan, Mql5CompilePackageDisposition.BlockedPlatformSnapshot));
            Assert.Equal(108, Count(plan, Mql5CompilePackageDisposition.BlockedUnsupportedSemantics));
            Assert.Equal(5, Count(plan, Mql5CompilePackageDisposition.BlockedMissingDependency));
            Assert.Equal(3, Count(plan, Mql5CompilePackageDisposition.BlockedInvalidSyntax));
            Assert.Equal(1, Count(plan, Mql5CompilePackageDisposition.BlockedBinarySource));
            Assert.Equal(1, Count(plan, Mql5CompilePackageDisposition.BlockedAllNulSource));
            Assert.Equal(
                12,
                CountIntrinsic(plan, Mql5CompilePackageDisposition.ReadyForIsolatedCompile));
            Assert.Equal(
                36,
                CountIntrinsic(plan, Mql5CompilePackageDisposition.BlockedPlatformSnapshot));
            Assert.Equal(
                108,
                CountIntrinsic(plan, Mql5CompilePackageDisposition.BlockedUnsupportedSemantics));
            Assert.Equal(
                5,
                CountIntrinsic(plan, Mql5CompilePackageDisposition.BlockedMissingDependency));
            Assert.Equal(
                3,
                CountIntrinsic(plan, Mql5CompilePackageDisposition.BlockedInvalidSyntax));
            Assert.Equal(
                1,
                CountIntrinsic(plan, Mql5CompilePackageDisposition.BlockedBinarySource));
            Assert.Equal(
                1,
                CountIntrinsic(plan, Mql5CompilePackageDisposition.BlockedAllNulSource));
            string formattedArtifact = Mql5CompilePackagePlanFormatter.ToJson(plan);
            Assert.DoesNotContain('\r', formattedArtifact);
            Assert.EndsWith("\n", formattedArtifact, StringComparison.Ordinal);
            byte[] formattedArtifactBytes = Encoding.UTF8.GetBytes(formattedArtifact);
            byte[] checkedInArtifactBytes = File.ReadAllBytes(Path.Combine(
                repositoryRoot,
                "artifacts",
                "verification",
                "mql5",
                "mq5-compile-package-plan.v2.json"));
            string formattedArtifactSha256;
            try
            {
                formattedArtifactSha256 = Convert.ToHexString(
                        SHA256.HashData(formattedArtifactBytes))
                    .ToLowerInvariant();
                Assert.Equal(formattedArtifactBytes, checkedInArtifactBytes);
                string orchestrationReport = File.ReadAllText(Path.Combine(
                    repositoryRoot,
                    "docs",
                    "backend",
                    "MQL5_ISOLATED_COMPILE_ORCHESTRATION.md"));
                Assert.Contains(
                    $"| Metadata-only formatted JSON bytes | {formattedArtifactBytes.Length.ToString("N0", CultureInfo.InvariantCulture)} |",
                    orchestrationReport,
                    StringComparison.Ordinal);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(formattedArtifactBytes);
                CryptographicOperations.ZeroMemory(checkedInArtifactBytes);
            }

            // The two digests below are pinned to the checked-in artifact, which must be
            // regenerated together with these constants after planner behavior changes:
            //   dotnet run --project src/Apps/YO4X.Conversion.Worker -- --static-inventory
            //     --source-root Testing/Mq5 --manifest-output <tmp> --report-output <tmp>
            //     --conversion-evidence-output <tmp> --conversion-evidence-report-output <tmp>
            //     --compile-package-plan-output artifacts/verification/mql5/mq5-compile-package-plan.v2.json
            // The values below pin the checked-in artifact as regenerated with the
            // minimum-viable-structure gate active (FIB 2.mq5 now blockedInvalidSyntax).
            Assert.Equal(
                "30ceaabef530b6e43522608658db718d466ba52cc5851ff6430f30d21116c80e",
                plan.PlanSha256);
            Assert.Equal(
                "51e88beddabc6e2d11f00a6b8a2671a27642f58f2d302453f16199da368569e7",
                formattedArtifactSha256);
            Assert.DoesNotContain("#include", formattedArtifact, StringComparison.Ordinal);
            Assert.DoesNotContain("void OnTick", formattedArtifact, StringComparison.Ordinal);
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"No-execution, snapshot-unavailable compile-package plan SHA-256: {plan.PlanSha256}");
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"Formatted metadata-only artifact SHA-256: {formattedArtifactSha256}");
            foreach (Mql5TargetCompilePackageDossier candidate in plan.Targets.Where(
                         static target => target.IntrinsicDisposition
                             == Mql5CompilePackageDisposition.ReadyForIsolatedCompile))
            {
                TestContext.Current.TestOutputHelper?.WriteLine(
                    $"INTRINSIC_CANDIDATE {candidate.TargetRelativePath} package={candidate.PackageSha256} closure={candidate.SourceClosureSha256}");
            }

            Assert.All(plan.Targets, static target =>
            {
                Assert.Matches("^[0-9a-f]{64}$", target.PackageSha256);
                Assert.Matches("^[0-9a-f]{64}$", target.SourceClosureSha256);
                Assert.Equal(target.TargetRelativePath, target.OrderedSources[^1].RelativePath);
                Assert.False(target.IsReadyForIsolatedCompile);
                Assert.Contains(
                    target.Blockers,
                    static blocker => blocker.Kind
                        == Mql5CompilePackageBlockerKind.ApprovedPlatformSnapshotUnavailable);
            });
        }
        finally
        {
            foreach (Mql5SourceDocument document in documents)
            {
                CryptographicOperations.ZeroMemory(document.Content);
            }
        }
    }

    private static int Count(
        Mql5CompilePackagePlan plan,
        Mql5CompilePackageDisposition disposition) =>
        plan.Targets.Count(target => target.Disposition == disposition);

    private static int CountIntrinsic(
        Mql5CompilePackagePlan plan,
        Mql5CompilePackageDisposition disposition) =>
        plan.Targets.Count(target => target.IntrinsicDisposition == disposition);

    private static Mql5TargetCompilePackageDossier AssertDisposition(
        Mql5CompilePackagePlan plan,
        string path,
        Mql5CompilePackageDisposition disposition)
    {
        Mql5TargetCompilePackageDossier package = Assert.Single(
            plan.Targets,
            target => target.TargetRelativePath == path);
        Assert.Equal(disposition, package.Disposition);
        if (disposition != Mql5CompilePackageDisposition.ReadyForIsolatedCompile)
        {
            Assert.NotEmpty(package.Blockers);
        }

        return package;
    }

    private static Mql5CompilePackagePlan Plan(Mql5SourceDocument[] documents)
    {
        Mql5CorpusManifest manifest = new Mql5StaticInventoryAnalyzer().Analyze(documents);
        Mql5ConversionCorpusEvidence evidence = new Mql5ConversionEvidenceAnalyzer().Analyze(documents);
        return Mql5CompilePackageDossierPlanner.Plan(
            manifest,
            evidence,
            documents,
            ApprovedPlatformSnapshot);
    }

    private static Mql5SourceDocument[] CreateDocuments(
        params (string Path, string Source)[] sources) => sources.Select(static source =>
            new Mql5SourceDocument(source.Path, Encoding.UTF8.GetBytes(source.Source))).ToArray();

    private sealed class EnumeratorBombSourceCollection : IReadOnlyList<Mql5SourceDocument>
    {
        private readonly Mql5SourceDocument source;

        public EnumeratorBombSourceCollection(Mql5SourceDocument source)
        {
            this.source = source;
        }

        public int Count => 1;

        public int IndexerAccessCount { get; private set; }

        public int EnumeratorAccessCount { get; private set; }

        public Mql5SourceDocument this[int index]
        {
            get
            {
                IndexerAccessCount++;
                return index == 0
                    ? source
                    : throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public IEnumerator<Mql5SourceDocument> GetEnumerator()
        {
            EnumeratorAccessCount++;
            throw new IOException("The planner must not enumerate the caller collection.");
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

    private sealed class OversizedCountSourceCollection : IReadOnlyList<Mql5SourceDocument>
    {
        public int Count => 10_001;

        public int IndexerAccessCount { get; private set; }

        public int EnumeratorAccessCount { get; private set; }

        public Mql5SourceDocument this[int index]
        {
            get
            {
                IndexerAccessCount++;
                throw new IOException("The oversized planner indexer must not be used.");
            }
        }

        public IEnumerator<Mql5SourceDocument> GetEnumerator()
        {
            EnumeratorAccessCount++;
            throw new IOException("The oversized planner enumerator must not be used.");
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

    private sealed class FaultingSourceCollection : IReadOnlyList<Mql5SourceDocument>
    {
        public int Count => 1;

        public int IndexerAccessCount { get; private set; }

        public int EnumeratorAccessCount { get; private set; }

        public Mql5SourceDocument this[int index]
        {
            get
            {
                IndexerAccessCount++;
                throw new IOException("Hostile planner indexer fault.");
            }
        }

        public IEnumerator<Mql5SourceDocument> GetEnumerator()
        {
            EnumeratorAccessCount++;
            throw new IOException("Hostile planner enumerator fault.");
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "YO4X.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new DirectoryNotFoundException("The YO4X repository root was not found.");
    }
}
