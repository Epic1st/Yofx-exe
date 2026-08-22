namespace YO4X.StrategyGovernance;

public enum Mql5SemanticParityState
{
    Blocked,
    Failed,
    Proven
}

public sealed record Mql5SemanticTolerancePolicy(
    string SchemaVersion,
    string PolicyId,
    bool RequireExactEventSequence,
    bool RequireExactEventKinds,
    bool RequireExactFieldSets,
    bool RequireExactNonNumericValues,
    bool RequireBothNumericLimits,
    decimal MaximumAbsoluteError,
    decimal MaximumRelativeError,
    int MaximumEventCount);

public sealed record Mql5SemanticToolchainBinding(
    string RunnerImageDigest,
    string MetaEditorSha256,
    string MetaEditorVersion,
    string PlatformLibrarySnapshotSha256,
    string MetaTraderTerminalSha256,
    string MetaTraderTerminalVersion,
    string LoweredRuntimeImageDigest,
    string LoweredRuntimeSha256,
    string LoweredRuntimeVersion);

public sealed record Mql5SemanticEquivalenceRequest(
    Guid JobId,
    DateTimeOffset RequestedAtUtc,
    string RelativePath,
    string SourceSha256,
    string DependencyClosureSha256,
    string DependencyGraphSha256,
    string CorpusSha256,
    string ConversionEvidenceSha256,
    string CompilerArtifactSha256,
    string RestrictedIrSha256,
    Mql5SemanticToolchainBinding Toolchain,
    string ToolchainBindingSha256,
    string ReferenceInputTraceSha256,
    string ReferenceInputEventIndexSha256,
    int InputEventCount,
    Mql5SemanticTolerancePolicy TolerancePolicy,
    string TolerancePolicySha256,
    string TolerancePolicyApprovalSha256,
    Mql5IsolationPolicy IsolationPolicy);

public sealed record Mql5SemanticTraceEventEvidence(
    int EventIndex,
    string EventKind,
    string InputEventSha256,
    string ReferenceOutputEventSha256,
    string LoweredOutputEventSha256,
    int ComparedNumericValueCount,
    int ExactNumericValueCount,
    int NumericDivergenceCount,
    int MissingReferenceFieldCount,
    int MissingLoweredFieldCount,
    int NonNumericMismatchCount,
    decimal MaximumAbsoluteError,
    decimal MaximumRelativeError);

public sealed record Mql5SemanticRunnerAttestationDescriptor(
    string SchemaVersion,
    Guid JobId,
    string RequestSha256,
    string RelativePath,
    string SourceSha256,
    string DependencyClosureSha256,
    string DependencyGraphSha256,
    string CorpusSha256,
    string ConversionEvidenceSha256,
    string CompilerArtifactSha256,
    string RestrictedIrSha256,
    string ToolchainBindingSha256,
    string ReferenceInputTraceSha256,
    string ReferenceInputEventIndexSha256,
    string TolerancePolicySha256,
    string TolerancePolicyApprovalSha256,
    string RunnerId,
    string RunnerSessionId,
    string RunnerImageDigest,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    Mql5IsolatedRunStatus RunStatus,
    string ReferenceOutputTraceSha256,
    string ReferenceOutputEventIndexSha256,
    string LoweredOutputTraceSha256,
    string LoweredOutputEventIndexSha256,
    int ReferenceOutputEventCount,
    int LoweredOutputEventCount,
    IReadOnlyList<Mql5SemanticTraceEventEvidence> Events);

public sealed class Mql5SemanticRunnerAttestation
{
    private readonly byte[] signature;

    public Mql5SemanticRunnerAttestation(
        Mql5SemanticRunnerAttestationDescriptor? descriptor,
        string? algorithm,
        string? signingKeyId,
        byte[]? signature,
        string? signatureSha256,
        string? signedPayloadSha256)
    {
        Descriptor = descriptor;
        Algorithm = algorithm;
        SigningKeyId = signingKeyId;
        this.signature = signature?.ToArray() ?? [];
        SignatureSha256 = signatureSha256;
        SignedPayloadSha256 = signedPayloadSha256;
    }

    public Mql5SemanticRunnerAttestationDescriptor? Descriptor { get; }

    public string? Algorithm { get; }

    public string? SigningKeyId { get; }

    public string? SignatureSha256 { get; }

    public string? SignedPayloadSha256 { get; }

    public byte[] GetSignature() => signature.ToArray();
}

public sealed record Mql5SemanticParityEvidence(
    Guid JobId,
    string RelativePath,
    string SourceSha256,
    string DependencyClosureSha256,
    string CorpusSha256,
    string CompilerArtifactSha256,
    string ToolchainBindingSha256,
    string ReferenceInputTraceSha256,
    string? ReferenceOutputTraceSha256,
    string? LoweredOutputTraceSha256,
    string TolerancePolicySha256,
    Mql5SemanticParityState State,
    string ReasonCode,
    string? RunnerId,
    string? RunnerSessionId,
    string? AttestationSigningKeyId,
    string? AttestationSha256,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int ComparedEventCount)
{
    public bool SemanticParityProven => State == Mql5SemanticParityState.Proven;
}
