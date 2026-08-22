namespace YO4X.StrategyGovernance;

public enum Mql5EvidenceStageName
{
    SourceIntegrity,
    DependencyResolution,
    LexicalAnalysis,
    StructuralParse,
    TypeChecking,
    RestrictedIrLowering
}

public enum Mql5EvidenceStageStatus
{
    Passed,
    Failed,
    Blocked,
    NotAttempted
}

public enum Mql5ConversionEvidenceDisposition
{
    BlockedAllNulSource,
    BlockedBinarySource,
    BlockedInvalidSyntax,
    BlockedMissingDependency,
    BlockedExternalDependencySnapshot,
    BlockedDependencyCycle,
    BlockedUnsupportedSemantics,
    AwaitingIsolatedTypeCheck
}

public sealed record Mql5EvidenceStage(
    Mql5EvidenceStageName Name,
    Mql5EvidenceStageStatus Status,
    string EvidenceCode);

public sealed record Mql5EvidenceLocation(
    int Line,
    int Column);

public sealed record Mql5ConversionEvidenceFinding(
    string Code,
    Mql5FindingSeverity Severity,
    string Message,
    Mql5EvidenceLocation? Location);

public sealed record Mql5DependencyEdgeEvidence(
    string DeclaredPath,
    Mql5IncludeKind Kind,
    Mql5IncludeResolution Resolution,
    string? ResolvedRelativePath,
    int Line);

public sealed record Mql5LexicalEvidence(
    int TokenCount,
    int IdentifierCount,
    int NumericLiteralCount,
    int StringLiteralCount,
    int CharacterLiteralCount,
    int CommentCount,
    int NulCharacterCount,
    int ForbiddenControlCharacterCount,
    int PreprocessorDirectiveCount,
    int MaximumDelimiterDepth);

public sealed record Mql5StructuralEvidence(
    int FunctionDefinitionCount,
    int TypeDeclarationCount,
    int InputDeclarationCount,
    int StatementTerminatorCount,
    int MacroDefinitionCount,
    int ConditionalDirectiveCount,
    bool DelimitersBalanced,
    bool ConditionalDirectivesBalanced,
    bool FullGrammarParseProven,
    bool TypeCheckProven,
    bool RestrictedIrLoweringProven);

public sealed record Mql5DependencyClosureEvidence(
    IReadOnlyList<string> DirectDependencies,
    IReadOnlyList<string> TransitiveDependencies,
    IReadOnlyList<string> DependencyFirstOrder,
    IReadOnlyList<string> ReachableCycleMembers,
    bool DependencyFirstOrderProven);

public sealed record Mql5ConversionFileEvidence(
    string RelativePath,
    string SourceSha256,
    string DependencyClosureSha256,
    string EvidenceSha256,
    string TextEncoding,
    Mql5SourceKind Kind,
    Mql5StaticDisposition StaticDisposition,
    Mql5ConversionEvidenceDisposition Disposition,
    IReadOnlyList<string> Entrypoints,
    IReadOnlyList<Mql5DetectedFeature> StaticFeatures,
    IReadOnlyList<Mql5CompatibilityFinding> StaticFindings,
    IReadOnlyList<Mql5DependencyEdgeEvidence> Includes,
    Mql5DependencyClosureEvidence DependencyClosure,
    Mql5LexicalEvidence Lexical,
    Mql5StructuralEvidence Structural,
    IReadOnlyList<Mql5EvidenceStage> Stages,
    IReadOnlyList<Mql5ConversionEvidenceFinding> Findings);

public sealed record Mql5ConversionCorpusEvidence(
    string SchemaVersion,
    string AnalyzerVersion,
    string InputStaticSchemaVersion,
    string InputStaticAnalyzerVersion,
    string InputCorpusSha256,
    string DependencyGraphSha256,
    string EvidenceSha256,
    int FileCount,
    long TotalBytes,
    IReadOnlyList<Mql5ConversionFileEvidence> Files);
