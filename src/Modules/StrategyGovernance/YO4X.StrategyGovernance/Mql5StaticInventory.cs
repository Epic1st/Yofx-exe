namespace YO4X.StrategyGovernance;

public enum Mql5SourceKind
{
    ExpertOrProgram,
    Header
}

public enum Mql5FeatureSupport
{
    SupportedSubsetCandidate,
    ReviewRequired,
    NeedsSource,
    Unsupported
}

public enum Mql5FindingSeverity
{
    Information,
    Warning,
    Error
}

public enum Mql5StaticDisposition
{
    NeedsSemanticValidation,
    NeedsSource,
    Unsupported,
    Rejected
}

public enum Mql5IncludeKind
{
    Local,
    PlatformOrSearchPath
}

public enum Mql5IncludeResolution
{
    ResolvedInCorpus,
    PlatformLibrary,
    MissingSource,
    Ambiguous,
    Invalid
}

public sealed record Mql5SourceDocument(string RelativePath, byte[] Content);

public interface IMql5StaticInventoryAnalyzer
{
    Mql5CorpusManifest Analyze(IEnumerable<Mql5SourceDocument> sourceDocuments);
}

public sealed record Mql5IncludeManifest(
    string DeclaredPath,
    Mql5IncludeKind Kind,
    Mql5IncludeResolution Resolution,
    string? ResolvedRelativePath,
    int Line);

public sealed record Mql5DetectedFeature(
    string Code,
    Mql5FeatureSupport Support,
    int OccurrenceCount,
    IReadOnlyList<int> Lines);

public sealed record Mql5CompatibilityFinding(
    string Code,
    Mql5FindingSeverity Severity,
    Mql5FeatureSupport Support,
    string Message,
    IReadOnlyList<int> Lines);

public sealed record Mql5VerificationClaims(
    bool StaticInventoryCompleted,
    bool ParsedAndTypeChecked,
    bool SemanticConversionProven,
    bool MetaEditorCompileProven,
    bool ReferenceParityProven,
    bool DemoRuntimeProven);

public sealed record Mql5SourceManifest(
    string RelativePath,
    Mql5SourceKind Kind,
    long ByteLength,
    string Sha256,
    string TextEncoding,
    IReadOnlyList<string> Entrypoints,
    IReadOnlyList<Mql5IncludeManifest> Includes,
    IReadOnlyList<Mql5DetectedFeature> Features,
    IReadOnlyList<Mql5CompatibilityFinding> Findings,
    Mql5StaticDisposition Disposition,
    Mql5VerificationClaims Verification);

public sealed record Mql5CorpusManifest(
    string SchemaVersion,
    string AnalyzerVersion,
    string CorpusSha256,
    int FileCount,
    long TotalBytes,
    IReadOnlyList<Mql5SourceManifest> Files);
