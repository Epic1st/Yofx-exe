using System.Security.Cryptography;
using YO4X.BuildingBlocks;

namespace YO4X.StrategyGovernance;

public enum Mql5CompilePackageBlockerKind
{
    AllNulSource,
    BinarySource,
    InvalidSyntax,
    MissingDependency,
    AmbiguousDependency,
    InvalidDependency,
    DependencyCycle,
    UnsupportedSemantics,
    PlatformSnapshotRequired,
    ApprovedPlatformSnapshotUnavailable
}

public enum Mql5CompilePackageDisposition
{
    ReadyForIsolatedCompile,
    BlockedAllNulSource,
    BlockedBinarySource,
    BlockedInvalidSyntax,
    BlockedMissingDependency,
    BlockedAmbiguousDependency,
    BlockedInvalidDependency,
    BlockedDependencyCycle,
    BlockedUnsupportedSemantics,
    BlockedPlatformSnapshot,
    BlockedApprovedPlatformSnapshotUnavailable
}

public sealed record Mql5CompilePackageSource(
    string RelativePath,
    long ByteLength,
    string SourceSha256);

public sealed record Mql5CompilePackageIncludeEdge(
    string SourceRelativePath,
    int Line,
    string DeclaredPath,
    Mql5IncludeKind Kind,
    Mql5IncludeResolution Resolution,
    string? ResolvedRelativePath);

public sealed record Mql5CompilePackageBlocker(
    Mql5CompilePackageBlockerKind Kind,
    string SourceRelativePath,
    int Line,
    string DeclaredPath);

public sealed record Mql5TargetCompilePackageDossier(
    string SchemaVersion,
    string PlannerVersion,
    string TargetRelativePath,
    string TargetSourceSha256,
    string CorpusSha256,
    string StaticManifestSha256,
    string ConversionEvidenceSha256,
    string ConversionEvidenceContentSha256,
    string DependencyGraphSha256,
    string? PlatformLibrarySnapshotApprovalId,
    string? ApprovedPlatformLibrarySnapshotSha256,
    string? PlatformLibrarySnapshotApprovalSha256,
    string ConversionFileEvidenceSha256,
    string ConversionDependencyClosureSha256,
    string SourceClosureSha256,
    string PackageSha256,
    bool DependencyFirstOrderProven,
    Mql5CompilePackageDisposition IntrinsicDisposition,
    Mql5CompilePackageDisposition Disposition,
    IReadOnlyList<Mql5CompilePackageSource> OrderedSources,
    IReadOnlyList<Mql5CompilePackageIncludeEdge> OrderedIncludeEdges,
    IReadOnlyList<Mql5CompilePackageBlocker> Blockers)
{
    public bool IsReadyForIsolatedCompile =>
        Disposition == Mql5CompilePackageDisposition.ReadyForIsolatedCompile;
}

public sealed record Mql5CompilePackagePlan(
    string SchemaVersion,
    string PlannerVersion,
    string CorpusSha256,
    string StaticManifestSha256,
    string ConversionEvidenceSha256,
    string ConversionEvidenceContentSha256,
    string DependencyGraphSha256,
    string? PlatformLibrarySnapshotApprovalId,
    string? ApprovedPlatformLibrarySnapshotSha256,
    string? PlatformLibrarySnapshotApprovalSha256,
    string PlanSha256,
    IReadOnlyList<Mql5TargetCompilePackageDossier> Targets);

public sealed class Mql5CompilePackagePlanningException : Exception
{
    public Mql5CompilePackagePlanningException(string reasonCode)
        : base("The MQL5 compile package could not be built from exact trusted inputs.")
    {
        ReasonCode = Mql5CompileValidation.IsSafeReasonCode(reasonCode)
            ? reasonCode
            : "COMPILE_PACKAGE_PLANNING_FAILED";
    }

    public string ReasonCode { get; }
}

public static class Mql5CompilePackageDossierPlanner
{
    public const string SchemaVersion = "yo4x.mql5-compile-package.v2";
    public const string PlannerVersion = "yo4x-mql5-compile-package-planner.v2";
    private const int MinimumViableStructureCharacterLength = 32;

    public static Mql5CompilePackagePlan Plan(
        Mql5CorpusManifest staticManifest,
        Mql5ConversionCorpusEvidence conversionEvidence,
        IReadOnlyList<Mql5SourceDocument> sources,
        Mql5ApprovedPlatformLibrarySnapshot? approvedPlatformLibrarySnapshot)
    {
        ArgumentNullException.ThrowIfNull(staticManifest);
        ArgumentNullException.ThrowIfNull(conversionEvidence);
        ArgumentNullException.ThrowIfNull(sources);

        ValidateApprovedPlatformSnapshot(approvedPlatformLibrarySnapshot, required: false);

        Mql5SourceDocument[] ownedSnapshots = SnapshotSources(sources);
        try
        {
            return PlanOwnedSnapshots(
                staticManifest,
                conversionEvidence,
                ownedSnapshots,
                approvedPlatformLibrarySnapshot);
        }
        finally
        {
            ZeroSources(ownedSnapshots);
        }
    }

    public static Mql5TargetCompilePackageDossier ValidateForDispatch(
        Mql5CorpusManifest staticManifest,
        Mql5ConversionCorpusEvidence conversionEvidence,
        IReadOnlyList<Mql5SourceDocument> exactClosureSources,
        Mql5TargetCompilePackageDossier suppliedDossier,
        Mql5ApprovedPlatformLibrarySnapshot? approvedPlatformLibrarySnapshot)
    {
        ArgumentNullException.ThrowIfNull(staticManifest);
        ArgumentNullException.ThrowIfNull(conversionEvidence);
        ArgumentNullException.ThrowIfNull(exactClosureSources);
        ArgumentNullException.ThrowIfNull(suppliedDossier);
        ValidateApprovedPlatformSnapshot(approvedPlatformLibrarySnapshot, required: true);

        Mql5SourceDocument[] ownedSnapshots = SnapshotSources(exactClosureSources);
        try
        {
            return ValidateDispatchOwnedSnapshots(
                staticManifest,
                conversionEvidence,
                ownedSnapshots,
                suppliedDossier,
                approvedPlatformLibrarySnapshot!);
        }
        finally
        {
            ZeroSources(ownedSnapshots);
        }
    }

    private static Mql5TargetCompilePackageDossier ValidateDispatchOwnedSnapshots(
        Mql5CorpusManifest staticManifest,
        Mql5ConversionCorpusEvidence conversionEvidence,
        Mql5SourceDocument[] ownedSnapshots,
        Mql5TargetCompilePackageDossier suppliedDossier,
        Mql5ApprovedPlatformLibrarySnapshot approvedPlatformLibrarySnapshot)
    {
        ValidateSourceShape(ownedSnapshots);
        ValidateArtifactShape(staticManifest, conversionEvidence);
        ValidateDossierShape(suppliedDossier, approvedPlatformLibrarySnapshot);

        string staticManifestSha256 = SafeCanonicalSha256(staticManifest, "STATIC_MANIFEST_INVALID");
        string conversionContentSha256 = SafeCanonicalSha256(
            conversionEvidence,
            "CONVERSION_EVIDENCE_INVALID");
        if (!Mql5CompileValidation.FixedTimeHexEquals(
                staticManifestSha256,
                suppliedDossier.StaticManifestSha256))
        {
            throw new Mql5CompilePackagePlanningException("STATIC_MANIFEST_CONTENT_DRIFT");
        }

        if (!Mql5CompileValidation.FixedTimeHexEquals(
                conversionEvidence.EvidenceSha256,
                suppliedDossier.ConversionEvidenceSha256)
            || !Mql5CompileValidation.FixedTimeHexEquals(
                conversionContentSha256,
                suppliedDossier.ConversionEvidenceContentSha256)
            || !Mql5CompileValidation.FixedTimeHexEquals(
                conversionEvidence.DependencyGraphSha256,
                suppliedDossier.DependencyGraphSha256)
            || !Mql5CompileValidation.FixedTimeHexEquals(
                staticManifest.CorpusSha256,
                suppliedDossier.CorpusSha256))
        {
            throw new Mql5CompilePackagePlanningException("COMPILE_PACKAGE_EVIDENCE_BINDING_DRIFT");
        }

        if (ownedSnapshots.Length != suppliedDossier.OrderedSources.Count)
        {
            throw new Mql5CompilePackagePlanningException("COMPILE_PACKAGE_SOURCE_SET_INVALID");
        }

        for (int index = 0; index < ownedSnapshots.Length; index++)
        {
            Mql5SourceDocument snapshot = ownedSnapshots[index];
            Mql5CompilePackageSource expected = suppliedDossier.OrderedSources[index];
            string sourceSha256 = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(snapshot.Content))
                .ToLowerInvariant();
            if (!string.Equals(snapshot.RelativePath, expected.RelativePath, StringComparison.Ordinal))
            {
                throw new Mql5CompilePackagePlanningException("COMPILE_PACKAGE_SOURCE_SET_INVALID");
            }

            if (snapshot.Content.LongLength != expected.ByteLength
                || !Mql5CompileValidation.FixedTimeHexEquals(sourceSha256, expected.SourceSha256))
            {
                throw new Mql5CompilePackagePlanningException("SOURCE_HASH_DRIFT_DETECTED");
            }
        }

        (Mql5CorpusManifest ClosureStatic, Mql5ConversionCorpusEvidence ClosureConversion) closure =
            RebuildEvidence(ownedSnapshots);
        var fullStaticByPath = staticManifest.Files.ToDictionary(
            static file => file.RelativePath,
            StringComparer.OrdinalIgnoreCase);
        var fullConversionByPath = conversionEvidence.Files.ToDictionary(
            static file => file.RelativePath,
            StringComparer.OrdinalIgnoreCase);
        var closureStaticByPath = closure.ClosureStatic.Files.ToDictionary(
            static file => file.RelativePath,
            StringComparer.OrdinalIgnoreCase);
        var closureConversionByPath = closure.ClosureConversion.Files.ToDictionary(
            static file => file.RelativePath,
            StringComparer.OrdinalIgnoreCase);
        var closureSourceByPath = ownedSnapshots.ToDictionary(
            static source => source.RelativePath,
            StringComparer.OrdinalIgnoreCase);

        foreach (Mql5CompilePackageSource source in suppliedDossier.OrderedSources)
        {
            if (!fullStaticByPath.TryGetValue(source.RelativePath, out Mql5SourceManifest? fullStatic)
                || !fullConversionByPath.TryGetValue(
                    source.RelativePath,
                    out Mql5ConversionFileEvidence? fullConversion)
                || !closureStaticByPath.TryGetValue(source.RelativePath, out Mql5SourceManifest? closureStatic)
                || !closureConversionByPath.TryGetValue(
                    source.RelativePath,
                    out Mql5ConversionFileEvidence? closureConversion)
                || !Mql5CompileValidation.FixedTimeHexEquals(
                    SafeCanonicalSha256(fullStatic, "STATIC_MANIFEST_INVALID"),
                    SafeCanonicalSha256(closureStatic, "STATIC_MANIFEST_INVALID"))
                || !Mql5CompileValidation.FixedTimeHexEquals(
                    SafeCanonicalSha256(fullConversion, "CONVERSION_EVIDENCE_INVALID"),
                    SafeCanonicalSha256(closureConversion, "CONVERSION_EVIDENCE_INVALID")))
            {
                throw new Mql5CompilePackagePlanningException("DEPENDENCY_CLOSURE_BINDING_INVALID");
            }
        }

        if (!fullStaticByPath.TryGetValue(
                suppliedDossier.TargetRelativePath,
                out Mql5SourceManifest? target)
            || target.Kind != Mql5SourceKind.ExpertOrProgram
            || !fullConversionByPath.TryGetValue(
                suppliedDossier.TargetRelativePath,
                out Mql5ConversionFileEvidence? targetConversion))
        {
            throw new Mql5CompilePackagePlanningException("COMPILE_PACKAGE_TARGET_INVALID");
        }

        Mql5TargetCompilePackageDossier expectedDossier = BuildDossier(
            target,
            targetConversion,
            fullStaticByPath,
            fullConversionByPath,
            closureSourceByPath,
            staticManifestSha256,
            conversionContentSha256,
            conversionEvidence,
            approvedPlatformLibrarySnapshot);
        if (!Mql5CompileValidation.FixedTimeHexEquals(
                expectedDossier.PackageSha256,
                suppliedDossier.PackageSha256)
            || !Mql5CompileValidation.FixedTimeHexEquals(
                SafeCanonicalSha256(expectedDossier, "COMPILE_PACKAGE_INVALID"),
                SafeCanonicalSha256(suppliedDossier, "COMPILE_PACKAGE_INVALID")))
        {
            throw new Mql5CompilePackagePlanningException("COMPILE_PACKAGE_CONTENT_DRIFT");
        }

        if (!Mql5CompileValidation.FixedTimeHexEquals(
                staticManifestSha256,
                SafeCanonicalSha256(staticManifest, "STATIC_MANIFEST_INVALID"))
            || !Mql5CompileValidation.FixedTimeHexEquals(
                conversionContentSha256,
                SafeCanonicalSha256(conversionEvidence, "CONVERSION_EVIDENCE_INVALID")))
        {
            throw new Mql5CompilePackagePlanningException(
                "COMPILE_PACKAGE_EVIDENCE_BINDING_DRIFT");
        }

        return expectedDossier;
    }

    private static Mql5CompilePackagePlan PlanOwnedSnapshots(
        Mql5CorpusManifest staticManifest,
        Mql5ConversionCorpusEvidence conversionEvidence,
        Mql5SourceDocument[] ownedSnapshots,
        Mql5ApprovedPlatformLibrarySnapshot? approvedPlatformLibrarySnapshot)
    {
        ValidateSourceShape(ownedSnapshots);
        (Mql5CorpusManifest RebuiltStatic, Mql5ConversionCorpusEvidence RebuiltConversion) rebuilt =
            RebuildEvidence(ownedSnapshots);
        ValidateStaticBinding(staticManifest, rebuilt.RebuiltStatic);
        ValidateConversionBinding(conversionEvidence, rebuilt.RebuiltConversion);

        string staticManifestSha256 = CanonicalJson.Sha256(rebuilt.RebuiltStatic);
        string conversionContentSha256 = CanonicalJson.Sha256(rebuilt.RebuiltConversion);
        var sourceByPath = ownedSnapshots.ToDictionary(
            static source => source.RelativePath,
            StringComparer.OrdinalIgnoreCase);
        var staticByPath = rebuilt.RebuiltStatic.Files.ToDictionary(
            static file => file.RelativePath,
            StringComparer.OrdinalIgnoreCase);
        var conversionByPath = rebuilt.RebuiltConversion.Files.ToDictionary(
            static file => file.RelativePath,
            StringComparer.OrdinalIgnoreCase);

        Mql5TargetCompilePackageDossier[] targets = rebuilt.RebuiltStatic.Files
            .Where(static file => file.Kind == Mql5SourceKind.ExpertOrProgram)
            .OrderBy(static file => file.RelativePath, StablePathComparer.Instance)
            .Select(target => BuildDossier(
                target,
                conversionByPath[target.RelativePath],
                staticByPath,
                conversionByPath,
                sourceByPath,
                staticManifestSha256,
                conversionContentSha256,
                rebuilt.RebuiltConversion,
                approvedPlatformLibrarySnapshot))
            .ToArray();

        string planSha256 = CanonicalJson.Sha256(new
        {
            SchemaVersion,
            PlannerVersion,
            rebuilt.RebuiltStatic.CorpusSha256,
            StaticManifestSha256 = staticManifestSha256,
            ConversionEvidenceSha256 = rebuilt.RebuiltConversion.EvidenceSha256,
            ConversionEvidenceContentSha256 = conversionContentSha256,
            rebuilt.RebuiltConversion.DependencyGraphSha256,
            PlatformLibrarySnapshotApprovalId = approvedPlatformLibrarySnapshot?.ApprovalId,
            ApprovedPlatformLibrarySnapshotSha256 = approvedPlatformLibrarySnapshot?.SnapshotSha256,
            PlatformLibrarySnapshotApprovalSha256 = approvedPlatformLibrarySnapshot?.ApprovalSha256,
            Targets = targets.Select(static target => new
            {
                target.TargetRelativePath,
                target.TargetSourceSha256,
                target.IntrinsicDisposition,
                target.Disposition,
                target.SourceClosureSha256,
                target.PackageSha256
            }).ToArray()
        });

        return new Mql5CompilePackagePlan(
            SchemaVersion,
            PlannerVersion,
            rebuilt.RebuiltStatic.CorpusSha256,
            staticManifestSha256,
            rebuilt.RebuiltConversion.EvidenceSha256,
            conversionContentSha256,
            rebuilt.RebuiltConversion.DependencyGraphSha256,
            approvedPlatformLibrarySnapshot?.ApprovalId,
            approvedPlatformLibrarySnapshot?.SnapshotSha256,
            approvedPlatformLibrarySnapshot?.ApprovalSha256,
            planSha256,
            targets);
    }

    private static Mql5SourceDocument[] SnapshotSources(IReadOnlyList<Mql5SourceDocument> sources)
    {
        Mql5SourceDocument[] references;
        try
        {
            int count = sources.Count;
            if (count is < 1 or > Mql5CompileValidation.MaximumSourceFileCount)
            {
                throw new Mql5CompilePackagePlanningException("SOURCE_CORPUS_INVALID");
            }

            references = new Mql5SourceDocument[count];
            for (int index = 0; index < count; index++)
            {
                references[index] = sources[index];
            }
        }
        catch (Mql5CompilePackagePlanningException)
        {
            throw;
        }
        catch (Exception exception) when (IsNonCatastrophic(exception))
        {
            throw new Mql5CompilePackagePlanningException("SOURCE_CORPUS_INVALID");
        }

        ValidateSourceShape(references);

        var snapshots = new Mql5SourceDocument[references.Length];
        try
        {
            for (int index = 0; index < references.Length; index++)
            {
                Mql5SourceDocument? source = references[index];
                if (source is null || source.Content is null)
                {
                    throw new Mql5CompilePackagePlanningException("SOURCE_PATH_UNSAFE_FOR_RUNNER");
                }

                snapshots[index] = new Mql5SourceDocument(
                    source.RelativePath,
                    source.Content.ToArray());
            }

            return snapshots;
        }
        catch
        {
            ZeroSources(snapshots);
            throw;
        }
    }

    private static void ZeroSources(IEnumerable<Mql5SourceDocument?> sources)
    {
        foreach (Mql5SourceDocument? source in sources)
        {
            if (source?.Content is not null)
            {
                CryptographicOperations.ZeroMemory(source.Content);
            }
        }
    }

    private static bool IsNonCatastrophic(Exception exception) => exception is not (
        OutOfMemoryException
        or StackOverflowException
        or AccessViolationException);

    private static void ValidateArtifactShape(
        Mql5CorpusManifest staticManifest,
        Mql5ConversionCorpusEvidence conversionEvidence)
    {
        if (!string.Equals(staticManifest.SchemaVersion, Mql5StaticInventoryAnalyzer.SchemaVersion, StringComparison.Ordinal)
            || !string.Equals(staticManifest.AnalyzerVersion, Mql5StaticInventoryAnalyzer.AnalyzerVersion, StringComparison.Ordinal)
            || !Mql5CompileValidation.IsExactSha256(staticManifest.CorpusSha256)
            || staticManifest.Files is null
            || staticManifest.FileCount != staticManifest.Files.Count
            || staticManifest.FileCount is < 1 or > Mql5CompileValidation.MaximumSourceFileCount
            || staticManifest.TotalBytes is < 1 or > Mql5CompileValidation.MaximumSourceCorpusBytes)
        {
            throw new Mql5CompilePackagePlanningException("STATIC_MANIFEST_INVALID");
        }

        var staticPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long summedStaticBytes = 0;
        foreach (Mql5SourceManifest? file in staticManifest.Files)
        {
            if (file is null
                || !Mql5CompileValidation.IsSafeRelativeSourcePath(file.RelativePath)
                || !Mql5CompileValidation.IsExactSha256(file.Sha256)
                || file.ByteLength is < 0 or > Mql5CompileValidation.MaximumSourceFileBytes
                || file.Includes is null
                || file.Entrypoints is null
                || file.Features is null
                || file.Findings is null
                || file.Verification is null
                || !staticPaths.Add(file.RelativePath))
            {
                throw new Mql5CompilePackagePlanningException("STATIC_MANIFEST_INVALID");
            }

            summedStaticBytes = checked(summedStaticBytes + file.ByteLength);
            if (summedStaticBytes > Mql5CompileValidation.MaximumSourceCorpusBytes)
            {
                throw new Mql5CompilePackagePlanningException("STATIC_MANIFEST_INVALID");
            }
        }

        if (summedStaticBytes != staticManifest.TotalBytes)
        {
            throw new Mql5CompilePackagePlanningException("STATIC_MANIFEST_INVALID");
        }

        if (!string.Equals(conversionEvidence.SchemaVersion, Mql5ConversionEvidenceAnalyzer.SchemaVersion, StringComparison.Ordinal)
            || !string.Equals(conversionEvidence.AnalyzerVersion, Mql5ConversionEvidenceAnalyzer.AnalyzerVersion, StringComparison.Ordinal)
            || !string.Equals(conversionEvidence.InputStaticSchemaVersion, staticManifest.SchemaVersion, StringComparison.Ordinal)
            || !string.Equals(conversionEvidence.InputStaticAnalyzerVersion, staticManifest.AnalyzerVersion, StringComparison.Ordinal)
            || !Mql5CompileValidation.FixedTimeHexEquals(
                conversionEvidence.InputCorpusSha256,
                staticManifest.CorpusSha256)
            || !Mql5CompileValidation.IsExactSha256(conversionEvidence.DependencyGraphSha256)
            || !Mql5CompileValidation.IsExactSha256(conversionEvidence.EvidenceSha256)
            || conversionEvidence.Files is null
            || conversionEvidence.FileCount != conversionEvidence.Files.Count
            || conversionEvidence.FileCount != staticManifest.FileCount
            || conversionEvidence.TotalBytes != staticManifest.TotalBytes)
        {
            throw new Mql5CompilePackagePlanningException("CONVERSION_EVIDENCE_INVALID");
        }

        var conversionPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, Mql5SourceManifest> staticByPath = staticManifest.Files.ToDictionary(
            static file => file.RelativePath,
            StringComparer.OrdinalIgnoreCase);
        foreach (Mql5ConversionFileEvidence? file in conversionEvidence.Files)
        {
            if (file is null
                || !Mql5CompileValidation.IsSafeRelativeSourcePath(file.RelativePath)
                || !Mql5CompileValidation.IsExactSha256(file.SourceSha256)
                || !Mql5CompileValidation.IsExactSha256(file.DependencyClosureSha256)
                || !Mql5CompileValidation.IsExactSha256(file.EvidenceSha256)
                || file.DependencyClosure is null
                || file.Includes is null
                || file.Entrypoints is null
                || file.StaticFeatures is null
                || file.StaticFindings is null
                || file.Stages is null
                || file.Findings is null
                || !conversionPaths.Add(file.RelativePath)
                || !staticByPath.TryGetValue(file.RelativePath, out Mql5SourceManifest? staticFile)
                || !Mql5CompileValidation.FixedTimeHexEquals(file.SourceSha256, staticFile.Sha256))
            {
                throw new Mql5CompilePackagePlanningException("CONVERSION_EVIDENCE_INVALID");
            }
        }

        if (!staticPaths.SetEquals(conversionPaths))
        {
            throw new Mql5CompilePackagePlanningException("CONVERSION_EVIDENCE_INVALID");
        }
    }

    private static void ValidateDossierShape(
        Mql5TargetCompilePackageDossier dossier,
        Mql5ApprovedPlatformLibrarySnapshot approvedPlatformLibrarySnapshot)
    {
        if (!string.Equals(dossier.SchemaVersion, SchemaVersion, StringComparison.Ordinal)
            || !string.Equals(dossier.PlannerVersion, PlannerVersion, StringComparison.Ordinal)
            || !Mql5CompileValidation.IsSafeRelativeSourcePath(dossier.TargetRelativePath)
            || !Mql5CompileValidation.IsExactSha256(dossier.TargetSourceSha256)
            || !Mql5CompileValidation.IsExactSha256(dossier.CorpusSha256)
            || !Mql5CompileValidation.IsExactSha256(dossier.StaticManifestSha256)
            || !Mql5CompileValidation.IsExactSha256(dossier.ConversionEvidenceSha256)
            || !Mql5CompileValidation.IsExactSha256(dossier.ConversionEvidenceContentSha256)
            || !Mql5CompileValidation.IsExactSha256(dossier.DependencyGraphSha256)
            || !string.Equals(
                dossier.PlatformLibrarySnapshotApprovalId,
                approvedPlatformLibrarySnapshot.ApprovalId,
                StringComparison.Ordinal)
            || dossier.ApprovedPlatformLibrarySnapshotSha256 is null
            || !Mql5CompileValidation.FixedTimeHexEquals(
                dossier.ApprovedPlatformLibrarySnapshotSha256,
                approvedPlatformLibrarySnapshot.SnapshotSha256)
            || dossier.PlatformLibrarySnapshotApprovalSha256 is null
            || !Mql5CompileValidation.FixedTimeHexEquals(
                dossier.PlatformLibrarySnapshotApprovalSha256,
                approvedPlatformLibrarySnapshot.ApprovalSha256)
            || !Mql5CompileValidation.IsExactSha256(dossier.ConversionFileEvidenceSha256)
            || !Mql5CompileValidation.IsExactSha256(dossier.ConversionDependencyClosureSha256)
            || !Mql5CompileValidation.IsExactSha256(dossier.SourceClosureSha256)
            || !Mql5CompileValidation.IsExactSha256(dossier.PackageSha256)
            || !Enum.IsDefined(dossier.IntrinsicDisposition)
            || !Enum.IsDefined(dossier.Disposition)
            || dossier.OrderedSources is null
            || dossier.OrderedIncludeEdges is null
            || dossier.Blockers is null
            || dossier.OrderedSources.Count is < 1 or > Mql5CompileValidation.MaximumSourceFileCount)
        {
            throw new Mql5CompilePackagePlanningException("COMPILE_PACKAGE_INVALID");
        }

        var sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long summedSourceBytes = 0;
        foreach (Mql5CompilePackageSource? source in dossier.OrderedSources)
        {
            if (source is null
                || !Mql5CompileValidation.IsSafeRelativeSourcePath(source.RelativePath)
                || source.ByteLength is < 0 or > Mql5CompileValidation.MaximumSourceFileBytes
                || !Mql5CompileValidation.IsExactSha256(source.SourceSha256)
                || !sourcePaths.Add(source.RelativePath))
            {
                throw new Mql5CompilePackagePlanningException("COMPILE_PACKAGE_INVALID");
            }

            summedSourceBytes = checked(summedSourceBytes + source.ByteLength);
            if (summedSourceBytes > Mql5CompileValidation.MaximumSourceCorpusBytes)
            {
                throw new Mql5CompilePackagePlanningException("COMPILE_PACKAGE_INVALID");
            }
        }

        Mql5CompilePackageSource finalSource = dossier.OrderedSources[^1];
        if (!string.Equals(finalSource.RelativePath, dossier.TargetRelativePath, StringComparison.Ordinal)
            || !Mql5CompileValidation.FixedTimeHexEquals(
                finalSource.SourceSha256,
                dossier.TargetSourceSha256))
        {
            throw new Mql5CompilePackagePlanningException("COMPILE_PACKAGE_TARGET_INVALID");
        }

        if (dossier.OrderedIncludeEdges.Any(edge => edge is null
                || !sourcePaths.Contains(edge.SourceRelativePath)
                || edge.Line < 1
                || edge.DeclaredPath is not { Length: >= 1 and <= 500 }
                || !Enum.IsDefined(edge.Kind)
                || !Enum.IsDefined(edge.Resolution)
                || edge.ResolvedRelativePath is not null
                    && !Mql5CompileValidation.IsSafeRelativeSourcePath(edge.ResolvedRelativePath))
            || dossier.Blockers.Any(blocker => blocker is null
                || !sourcePaths.Contains(blocker.SourceRelativePath)
                || blocker.Line < 0
                || blocker.DeclaredPath is not { Length: <= 500 }
                || !Enum.IsDefined(blocker.Kind)))
        {
            throw new Mql5CompilePackagePlanningException("COMPILE_PACKAGE_INVALID");
        }
    }

    private static void ValidateSourceShape(Mql5SourceDocument[] sources)
    {
        string? failureCode = Mql5CompileValidation.ValidateSourceReferences(sources);
        if (failureCode is not null)
        {
            throw new Mql5CompilePackagePlanningException(failureCode);
        }
    }

    private static (Mql5CorpusManifest, Mql5ConversionCorpusEvidence) RebuildEvidence(
        Mql5SourceDocument[] sources)
    {
        try
        {
            Mql5CorpusManifest rebuiltStatic = new Mql5StaticInventoryAnalyzer().Analyze(sources);
            Mql5ConversionCorpusEvidence rebuiltConversion =
                new Mql5ConversionEvidenceAnalyzer().Analyze(sources);
            return (rebuiltStatic, rebuiltConversion);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new Mql5CompilePackagePlanningException("SOURCE_CORPUS_INVALID");
        }
    }

    private static void ValidateStaticBinding(
        Mql5CorpusManifest supplied,
        Mql5CorpusManifest rebuilt)
    {
        if (!string.Equals(supplied.SchemaVersion, Mql5StaticInventoryAnalyzer.SchemaVersion, StringComparison.Ordinal)
            || !string.Equals(supplied.AnalyzerVersion, Mql5StaticInventoryAnalyzer.AnalyzerVersion, StringComparison.Ordinal)
            || !Mql5CompileValidation.IsExactSha256(supplied.CorpusSha256)
            || supplied.Files is null
            || supplied.FileCount != supplied.Files.Count
            || supplied.FileCount is < 1 or > Mql5CompileValidation.MaximumSourceFileCount
            || supplied.Files.Any(static file => file is null
                || file.ByteLength is < 0 or > Mql5CompileValidation.MaximumSourceFileBytes)
            || supplied.TotalBytes is < 1 or > Mql5CompileValidation.MaximumSourceCorpusBytes)
        {
            throw new Mql5CompilePackagePlanningException("STATIC_MANIFEST_INVALID");
        }

        bool sourceDigestDrift;
        try
        {
            sourceDigestDrift = !Mql5CompileValidation.FixedTimeHexEquals(
                    rebuilt.CorpusSha256,
                    supplied.CorpusSha256)
                || rebuilt.TotalBytes != supplied.TotalBytes
                || rebuilt.FileCount != supplied.FileCount
                || !rebuilt.Files.Zip(supplied.Files).All(static pair =>
                    pair.Second is not null
                    && string.Equals(pair.First.RelativePath, pair.Second.RelativePath, StringComparison.Ordinal)
                    && pair.First.ByteLength == pair.Second.ByteLength
                    && Mql5CompileValidation.FixedTimeHexEquals(pair.First.Sha256, pair.Second.Sha256));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new Mql5CompilePackagePlanningException("STATIC_MANIFEST_INVALID");
        }

        if (sourceDigestDrift)
        {
            throw new Mql5CompilePackagePlanningException("SOURCE_HASH_DRIFT_DETECTED");
        }

        if (!Mql5CompileValidation.FixedTimeHexEquals(
                SafeCanonicalSha256(rebuilt, "STATIC_MANIFEST_INVALID"),
                SafeCanonicalSha256(supplied, "STATIC_MANIFEST_INVALID")))
        {
            throw new Mql5CompilePackagePlanningException("STATIC_MANIFEST_CONTENT_DRIFT");
        }
    }

    private static void ValidateConversionBinding(
        Mql5ConversionCorpusEvidence supplied,
        Mql5ConversionCorpusEvidence rebuilt)
    {
        if (!string.Equals(supplied.SchemaVersion, Mql5ConversionEvidenceAnalyzer.SchemaVersion, StringComparison.Ordinal)
            || !string.Equals(supplied.AnalyzerVersion, Mql5ConversionEvidenceAnalyzer.AnalyzerVersion, StringComparison.Ordinal)
            || !string.Equals(supplied.InputStaticSchemaVersion, Mql5StaticInventoryAnalyzer.SchemaVersion, StringComparison.Ordinal)
            || !string.Equals(supplied.InputStaticAnalyzerVersion, Mql5StaticInventoryAnalyzer.AnalyzerVersion, StringComparison.Ordinal)
            || !Mql5CompileValidation.IsExactSha256(supplied.InputCorpusSha256)
            || !Mql5CompileValidation.IsExactSha256(supplied.DependencyGraphSha256)
            || !Mql5CompileValidation.IsExactSha256(supplied.EvidenceSha256)
            || supplied.Files is null
            || supplied.FileCount != supplied.Files.Count
            || supplied.FileCount is < 1 or > Mql5CompileValidation.MaximumSourceFileCount
            || supplied.TotalBytes is < 1 or > Mql5CompileValidation.MaximumSourceCorpusBytes)
        {
            throw new Mql5CompilePackagePlanningException("CONVERSION_EVIDENCE_INVALID");
        }

        if (!Mql5CompileValidation.FixedTimeHexEquals(rebuilt.InputCorpusSha256, supplied.InputCorpusSha256)
            || !Mql5CompileValidation.FixedTimeHexEquals(rebuilt.DependencyGraphSha256, supplied.DependencyGraphSha256)
            || !Mql5CompileValidation.FixedTimeHexEquals(rebuilt.EvidenceSha256, supplied.EvidenceSha256)
            || rebuilt.FileCount != supplied.FileCount
            || rebuilt.TotalBytes != supplied.TotalBytes)
        {
            throw new Mql5CompilePackagePlanningException("CONVERSION_EVIDENCE_DIGEST_DRIFT");
        }

        if (!Mql5CompileValidation.FixedTimeHexEquals(
                SafeCanonicalSha256(rebuilt, "CONVERSION_EVIDENCE_INVALID"),
                SafeCanonicalSha256(supplied, "CONVERSION_EVIDENCE_INVALID")))
        {
            throw new Mql5CompilePackagePlanningException("CONVERSION_EVIDENCE_CONTENT_DRIFT");
        }
    }

    private static Mql5TargetCompilePackageDossier BuildDossier(
        Mql5SourceManifest target,
        Mql5ConversionFileEvidence targetConversion,
        Dictionary<string, Mql5SourceManifest> staticByPath,
        Dictionary<string, Mql5ConversionFileEvidence> conversionByPath,
        Dictionary<string, Mql5SourceDocument> sourceByPath,
        string staticManifestSha256,
        string conversionContentSha256,
        Mql5ConversionCorpusEvidence conversionCorpus,
        Mql5ApprovedPlatformLibrarySnapshot? approvedPlatformLibrarySnapshot)
    {
        string[] orderedPaths = BuildOrderedClosurePaths(targetConversion, staticByPath);
        Mql5CompilePackageSource[] orderedSources = orderedPaths
            .Select(path =>
            {
                Mql5SourceManifest manifest = staticByPath[path];
                Mql5SourceDocument source = sourceByPath[path];
                if (source.Content.LongLength != manifest.ByteLength)
                {
                    throw new Mql5CompilePackagePlanningException("SOURCE_HASH_DRIFT_DETECTED");
                }

                return new Mql5CompilePackageSource(
                    manifest.RelativePath,
                    manifest.ByteLength,
                    manifest.Sha256);
            })
            .ToArray();
        Mql5CompilePackageIncludeEdge[] edges = BuildOrderedEdges(orderedPaths, staticByPath);
        Mql5CompilePackageBlocker[] intrinsicBlockers = BuildBlockers(
            targetConversion,
            orderedPaths,
            staticByPath,
            conversionByPath,
            sourceByPath);
        Mql5CompilePackageDisposition intrinsicDisposition = DetermineDisposition(intrinsicBlockers);
        Mql5CompilePackageBlocker[] blockers = approvedPlatformLibrarySnapshot is null
            ? intrinsicBlockers
                .Append(SourceBlocker(
                    Mql5CompilePackageBlockerKind.ApprovedPlatformSnapshotUnavailable,
                    target.RelativePath))
                .OrderBy(static blocker => BlockerPrecedence(blocker.Kind))
                .ThenBy(static blocker => blocker.SourceRelativePath, StablePathComparer.Instance)
                .ThenBy(static blocker => blocker.Line)
                .ThenBy(static blocker => blocker.DeclaredPath, StringComparer.Ordinal)
                .ToArray()
            : intrinsicBlockers;
        Mql5CompilePackageDisposition disposition = approvedPlatformLibrarySnapshot is null
            && intrinsicDisposition == Mql5CompilePackageDisposition.ReadyForIsolatedCompile
                ? Mql5CompilePackageDisposition.BlockedApprovedPlatformSnapshotUnavailable
                : intrinsicDisposition;

        if (intrinsicDisposition == Mql5CompilePackageDisposition.ReadyForIsolatedCompile
            && (targetConversion.Disposition != Mql5ConversionEvidenceDisposition.AwaitingIsolatedTypeCheck
                || !targetConversion.DependencyClosure.DependencyFirstOrderProven))
        {
            throw new Mql5CompilePackagePlanningException("CONVERSION_EVIDENCE_CONTENT_DRIFT");
        }

        string sourceClosureSha256 = CanonicalJson.Sha256(new
        {
            SchemaVersion,
            target.RelativePath,
            target.Sha256,
            targetConversion.DependencyClosureSha256,
            targetConversion.DependencyClosure.DependencyFirstOrderProven,
            OrderedSources = orderedSources,
            OrderedIncludeEdges = edges
        });
        string packageSha256 = CanonicalJson.Sha256(new
        {
            SchemaVersion,
            PlannerVersion,
            TargetRelativePath = target.RelativePath,
            TargetSourceSha256 = target.Sha256,
            CorpusSha256 = conversionCorpus.InputCorpusSha256,
            StaticManifestSha256 = staticManifestSha256,
            ConversionEvidenceSha256 = conversionCorpus.EvidenceSha256,
            ConversionEvidenceContentSha256 = conversionContentSha256,
            conversionCorpus.DependencyGraphSha256,
            PlatformLibrarySnapshotApprovalId = approvedPlatformLibrarySnapshot?.ApprovalId,
            ApprovedPlatformLibrarySnapshotSha256 = approvedPlatformLibrarySnapshot?.SnapshotSha256,
            PlatformLibrarySnapshotApprovalSha256 = approvedPlatformLibrarySnapshot?.ApprovalSha256,
            ConversionFileEvidenceSha256 = targetConversion.EvidenceSha256,
            ConversionDependencyClosureSha256 = targetConversion.DependencyClosureSha256,
            SourceClosureSha256 = sourceClosureSha256,
            targetConversion.DependencyClosure.DependencyFirstOrderProven,
            IntrinsicDisposition = intrinsicDisposition,
            Disposition = disposition,
            OrderedSources = orderedSources,
            OrderedIncludeEdges = edges,
            Blockers = blockers
        });

        return new Mql5TargetCompilePackageDossier(
            SchemaVersion,
            PlannerVersion,
            target.RelativePath,
            target.Sha256,
            conversionCorpus.InputCorpusSha256,
            staticManifestSha256,
            conversionCorpus.EvidenceSha256,
            conversionContentSha256,
            conversionCorpus.DependencyGraphSha256,
            approvedPlatformLibrarySnapshot?.ApprovalId,
            approvedPlatformLibrarySnapshot?.SnapshotSha256,
            approvedPlatformLibrarySnapshot?.ApprovalSha256,
            targetConversion.EvidenceSha256,
            targetConversion.DependencyClosureSha256,
            sourceClosureSha256,
            packageSha256,
            targetConversion.DependencyClosure.DependencyFirstOrderProven,
            intrinsicDisposition,
            disposition,
            orderedSources,
            edges,
            blockers);
    }

    private static string[] BuildOrderedClosurePaths(
        Mql5ConversionFileEvidence target,
        Dictionary<string, Mql5SourceManifest> staticByPath)
    {
        string[] transitive = target.DependencyClosure.TransitiveDependencies.ToArray();
        if (transitive.Any(path => !staticByPath.ContainsKey(path))
            || transitive.Distinct(StringComparer.OrdinalIgnoreCase).Count() != transitive.Length
            || transitive.Contains(target.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            throw new Mql5CompilePackagePlanningException("CONVERSION_EVIDENCE_CONTENT_DRIFT");
        }

        string[] dependencies;
        if (target.DependencyClosure.DependencyFirstOrderProven)
        {
            dependencies = target.DependencyClosure.DependencyFirstOrder.ToArray();
            if (dependencies.Distinct(StringComparer.OrdinalIgnoreCase).Count() != dependencies.Length
                || !new HashSet<string>(dependencies, StringComparer.OrdinalIgnoreCase).SetEquals(transitive))
            {
                throw new Mql5CompilePackagePlanningException("CONVERSION_EVIDENCE_CONTENT_DRIFT");
            }
        }
        else
        {
            dependencies = transitive
                .OrderBy(static path => path, StablePathComparer.Instance)
                .ToArray();
        }

        return dependencies.Append(target.RelativePath).ToArray();
    }

    private static Mql5CompilePackageIncludeEdge[] BuildOrderedEdges(
        string[] orderedPaths,
        Dictionary<string, Mql5SourceManifest> staticByPath)
    {
        var closure = new HashSet<string>(orderedPaths, StringComparer.OrdinalIgnoreCase);
        var edges = new List<Mql5CompilePackageIncludeEdge>();
        foreach (string sourcePath in orderedPaths)
        {
            foreach (Mql5IncludeManifest include in staticByPath[sourcePath].Includes
                         .OrderBy(static include => include.Line)
                         .ThenBy(static include => include.DeclaredPath, StringComparer.Ordinal))
            {
                if (include.Resolution == Mql5IncludeResolution.ResolvedInCorpus
                    && (include.ResolvedRelativePath is null
                        || !closure.Contains(include.ResolvedRelativePath)))
                {
                    throw new Mql5CompilePackagePlanningException("DEPENDENCY_CLOSURE_BINDING_INVALID");
                }

                edges.Add(new Mql5CompilePackageIncludeEdge(
                    sourcePath,
                    include.Line,
                    include.DeclaredPath,
                    include.Kind,
                    include.Resolution,
                    include.ResolvedRelativePath));
            }
        }

        return edges.ToArray();
    }

    private static Mql5CompilePackageBlocker[] BuildBlockers(
        Mql5ConversionFileEvidence target,
        string[] orderedPaths,
        Dictionary<string, Mql5SourceManifest> staticByPath,
        Dictionary<string, Mql5ConversionFileEvidence> conversionByPath,
        Dictionary<string, Mql5SourceDocument> sourceByPath)
    {
        var blockers = new List<Mql5CompilePackageBlocker>();
        foreach (string path in orderedPaths.OrderBy(static path => path, StablePathComparer.Instance))
        {
            Mql5SourceManifest manifest = staticByPath[path];
            Mql5ConversionFileEvidence conversion = conversionByPath[path];
            if (string.Equals(manifest.TextEncoding, "binary-all-nul", StringComparison.Ordinal))
            {
                blockers.Add(SourceBlocker(Mql5CompilePackageBlockerKind.AllNulSource, path));
            }
            else if (string.Equals(manifest.TextEncoding, "binary-non-text", StringComparison.Ordinal))
            {
                blockers.Add(SourceBlocker(Mql5CompilePackageBlockerKind.BinarySource, path));
            }

            if (conversion.Stages.Any(static stage =>
                    stage.Name is Mql5EvidenceStageName.LexicalAnalysis
                        or Mql5EvidenceStageName.StructuralParse
                    && stage.Status == Mql5EvidenceStageStatus.Failed))
            {
                blockers.Add(SourceBlocker(Mql5CompilePackageBlockerKind.InvalidSyntax, path));
            }

            if (IsBelowMinimumViableStructure(
                    staticByPath[path],
                    conversion,
                    sourceByPath[path]))
            {
                blockers.Add(SourceBlocker(Mql5CompilePackageBlockerKind.InvalidSyntax, path));
            }

            if (manifest.Disposition == Mql5StaticDisposition.Unsupported)
            {
                blockers.Add(SourceBlocker(Mql5CompilePackageBlockerKind.UnsupportedSemantics, path));
            }
            else if (manifest.Disposition is Mql5StaticDisposition.NeedsSource or Mql5StaticDisposition.Rejected
                && !manifest.Includes.Any(static include => include.Resolution is
                    Mql5IncludeResolution.MissingSource
                    or Mql5IncludeResolution.Ambiguous
                    or Mql5IncludeResolution.Invalid))
            {
                blockers.Add(SourceBlocker(Mql5CompilePackageBlockerKind.MissingDependency, path));
            }

            foreach (Mql5IncludeManifest include in manifest.Includes)
            {
                Mql5CompilePackageBlockerKind? kind = include.Resolution switch
                {
                    Mql5IncludeResolution.MissingSource => Mql5CompilePackageBlockerKind.MissingDependency,
                    Mql5IncludeResolution.Ambiguous => Mql5CompilePackageBlockerKind.AmbiguousDependency,
                    Mql5IncludeResolution.Invalid => Mql5CompilePackageBlockerKind.InvalidDependency,
                    Mql5IncludeResolution.PlatformLibrary => Mql5CompilePackageBlockerKind.PlatformSnapshotRequired,
                    _ => null
                };
                if (kind.HasValue)
                {
                    blockers.Add(new Mql5CompilePackageBlocker(
                        kind.Value,
                        path,
                        include.Line,
                        include.DeclaredPath));
                }
            }
        }

        foreach (string cycleMember in target.DependencyClosure.ReachableCycleMembers)
        {
            blockers.Add(SourceBlocker(Mql5CompilePackageBlockerKind.DependencyCycle, cycleMember));
        }

        return blockers
            .Distinct()
            .OrderBy(static blocker => BlockerPrecedence(blocker.Kind))
            .ThenBy(static blocker => blocker.SourceRelativePath, StablePathComparer.Instance)
            .ThenBy(static blocker => blocker.Line)
            .ThenBy(static blocker => blocker.DeclaredPath, StringComparer.Ordinal)
            .ToArray();
    }

    private static Mql5CompilePackageBlocker SourceBlocker(
        Mql5CompilePackageBlockerKind kind,
        string path) => new(kind, path, 0, string.Empty);

    // A canonical source with no recognized structural anchors and less than the
    // minimum viable length of decoded text carries no parseable MQL5 structure.
    // All-NUL and binary-detected content keeps its dedicated encoding blockers.
    private static bool IsBelowMinimumViableStructure(
        Mql5SourceManifest manifest,
        Mql5ConversionFileEvidence conversion,
        Mql5SourceDocument source)
    {
        if (manifest.Entrypoints.Count > 0
            || manifest.Includes.Count > 0
            || conversion.Lexical.TokenCount > 0)
        {
            return false;
        }

        Mql5DecodedSource decoded = Mql5SourceDecoder.Decode(source.Content);
        return decoded.ContentKind == Mql5SourceContentKind.Text
            && decoded.Text.Trim().Length < MinimumViableStructureCharacterLength;
    }

    private static Mql5CompilePackageDisposition DetermineDisposition(
        Mql5CompilePackageBlocker[] blockers)
    {
        if (blockers.Length == 0)
        {
            return Mql5CompilePackageDisposition.ReadyForIsolatedCompile;
        }

        return blockers.MinBy(static blocker => BlockerPrecedence(blocker.Kind))!.Kind switch
        {
            Mql5CompilePackageBlockerKind.AllNulSource => Mql5CompilePackageDisposition.BlockedAllNulSource,
            Mql5CompilePackageBlockerKind.BinarySource => Mql5CompilePackageDisposition.BlockedBinarySource,
            Mql5CompilePackageBlockerKind.InvalidSyntax => Mql5CompilePackageDisposition.BlockedInvalidSyntax,
            Mql5CompilePackageBlockerKind.MissingDependency => Mql5CompilePackageDisposition.BlockedMissingDependency,
            Mql5CompilePackageBlockerKind.AmbiguousDependency => Mql5CompilePackageDisposition.BlockedAmbiguousDependency,
            Mql5CompilePackageBlockerKind.InvalidDependency => Mql5CompilePackageDisposition.BlockedInvalidDependency,
            Mql5CompilePackageBlockerKind.DependencyCycle => Mql5CompilePackageDisposition.BlockedDependencyCycle,
            Mql5CompilePackageBlockerKind.UnsupportedSemantics => Mql5CompilePackageDisposition.BlockedUnsupportedSemantics,
            Mql5CompilePackageBlockerKind.PlatformSnapshotRequired => Mql5CompilePackageDisposition.BlockedPlatformSnapshot,
            Mql5CompilePackageBlockerKind.ApprovedPlatformSnapshotUnavailable =>
                Mql5CompilePackageDisposition.BlockedApprovedPlatformSnapshotUnavailable,
            _ => throw new Mql5CompilePackagePlanningException("COMPILE_PACKAGE_PLANNING_FAILED")
        };
    }

    private static int BlockerPrecedence(Mql5CompilePackageBlockerKind kind) => kind switch
    {
        Mql5CompilePackageBlockerKind.AllNulSource => 0,
        Mql5CompilePackageBlockerKind.BinarySource => 1,
        Mql5CompilePackageBlockerKind.InvalidSyntax => 2,
        Mql5CompilePackageBlockerKind.MissingDependency => 3,
        Mql5CompilePackageBlockerKind.AmbiguousDependency => 4,
        Mql5CompilePackageBlockerKind.InvalidDependency => 5,
        Mql5CompilePackageBlockerKind.DependencyCycle => 6,
        Mql5CompilePackageBlockerKind.UnsupportedSemantics => 7,
        Mql5CompilePackageBlockerKind.PlatformSnapshotRequired => 8,
        Mql5CompilePackageBlockerKind.ApprovedPlatformSnapshotUnavailable => 9,
        _ => int.MaxValue
    };

    private static void ValidateApprovedPlatformSnapshot(
        Mql5ApprovedPlatformLibrarySnapshot? approvedPlatformLibrarySnapshot,
        bool required)
    {
        if (approvedPlatformLibrarySnapshot is null)
        {
            if (required)
            {
                throw new Mql5CompilePackagePlanningException(
                    "APPROVED_PLATFORM_SNAPSHOT_NOT_CONFIGURED");
            }

            return;
        }

        string expectedApprovalSha256 = SafeCanonicalSha256(new
        {
            SchemaVersion = Mql5ApprovedPlatformLibrarySnapshot.ApprovalSchemaVersion,
            approvedPlatformLibrarySnapshot.ApprovalId,
            approvedPlatformLibrarySnapshot.SnapshotSha256,
            approvedPlatformLibrarySnapshot.ProvenanceEvidenceSha256
        }, "APPROVED_PLATFORM_SNAPSHOT_INVALID");
        if (!Mql5CompileValidation.IsSafeToken(approvedPlatformLibrarySnapshot.ApprovalId)
            || !Mql5CompileValidation.IsExactSha256(approvedPlatformLibrarySnapshot.SnapshotSha256)
            || !Mql5CompileValidation.IsExactSha256(
                approvedPlatformLibrarySnapshot.ProvenanceEvidenceSha256)
            || !Mql5CompileValidation.FixedTimeHexEquals(
                expectedApprovalSha256,
                approvedPlatformLibrarySnapshot.ApprovalSha256))
        {
            throw new Mql5CompilePackagePlanningException(
                "APPROVED_PLATFORM_SNAPSHOT_INVALID");
        }
    }

    private static string SafeCanonicalSha256<T>(T value, string failureCode)
    {
        try
        {
            return CanonicalJson.Sha256(value);
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException
            or System.Text.Json.JsonException
            or NotSupportedException)
        {
            throw new Mql5CompilePackagePlanningException(failureCode);
        }
    }

    private sealed class StablePathComparer : IComparer<string>
    {
        public static StablePathComparer Instance { get; } = new();

        public int Compare(string? left, string? right)
        {
            int insensitive = StringComparer.OrdinalIgnoreCase.Compare(left, right);
            return insensitive != 0
                ? insensitive
                : StringComparer.Ordinal.Compare(left, right);
        }
    }
}
