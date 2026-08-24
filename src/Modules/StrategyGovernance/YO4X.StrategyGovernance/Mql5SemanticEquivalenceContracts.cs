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

public sealed class Mql5ApprovedSemanticProfile
{
    public const string ApprovalSchemaVersion = "yo4x.mql5-approved-semantic-profile.v1";

    private readonly HashSet<string> approvedSigningKeyIds;
    private readonly IReadOnlyList<string> orderedSigningKeyIds;

    public Mql5ApprovedSemanticProfile(
        string profileId,
        Mql5SemanticToolchainBinding toolchain,
        Mql5SemanticTolerancePolicy tolerancePolicy,
        Mql5IsolationPolicy maximumIsolationPolicy,
        IEnumerable<string> approvedSigningKeyIds)
    {
        ArgumentNullException.ThrowIfNull(toolchain);
        ArgumentNullException.ThrowIfNull(tolerancePolicy);
        ArgumentNullException.ThrowIfNull(maximumIsolationPolicy);
        ArgumentNullException.ThrowIfNull(approvedSigningKeyIds);

        string[] signingKeys = approvedSigningKeyIds.Take(33).ToArray();
        if (!Mql5CompileValidation.IsSafeToken(profileId)
            || !ValidateToolchain(toolchain)
            || !ValidateTolerancePolicy(tolerancePolicy)
            || Mql5CompileValidation.ValidateIsolationPolicy(maximumIsolationPolicy) is not null
            || !maximumIsolationPolicy.NetworkAccessDisabled
            || !maximumIsolationPolicy.ReadOnlyRootFileSystem
            || !maximumIsolationPolicy.EphemeralWorkspace
            || !maximumIsolationPolicy.HostMountsDisabled
            || !maximumIsolationPolicy.NoNewPrivileges
            || signingKeys.Length is < 1 or > 32
            || signingKeys.Any(static key => !Mql5CompileValidation.IsSafeToken(key))
            || signingKeys.Distinct(StringComparer.Ordinal).Count() != signingKeys.Length)
        {
            throw new ArgumentException("The approved semantic profile is invalid.", nameof(profileId));
        }

        Array.Sort(signingKeys, StringComparer.Ordinal);

        ProfileId = profileId;
        Toolchain = toolchain;
        TolerancePolicy = tolerancePolicy;
        MaximumIsolationPolicy = maximumIsolationPolicy;
        this.approvedSigningKeyIds = new HashSet<string>(signingKeys, StringComparer.Ordinal);
        orderedSigningKeyIds = Array.AsReadOnly(signingKeys);
        ApprovalSha256 = YO4X.BuildingBlocks.CanonicalJson.Sha256(new
        {
            SchemaVersion = ApprovalSchemaVersion,
            ProfileId,
            Toolchain,
            TolerancePolicy,
            MaximumIsolationPolicy,
            ApprovedSigningKeyIds = signingKeys
        });
    }

    public string ProfileId { get; }

    public string ApprovalSha256 { get; }

    public Mql5SemanticToolchainBinding Toolchain { get; }

    public Mql5SemanticTolerancePolicy TolerancePolicy { get; }

    public Mql5IsolationPolicy MaximumIsolationPolicy { get; }

    public IReadOnlyList<string> ApprovedSigningKeyIds => orderedSigningKeyIds;

    public bool Approves(
        Mql5SemanticToolchainBinding toolchain,
        Mql5SemanticTolerancePolicy tolerancePolicy,
        Mql5IsolationPolicy isolationPolicy) =>
        toolchain == Toolchain
        && tolerancePolicy == TolerancePolicy
        && isolationPolicy.NetworkAccessDisabled
        && isolationPolicy.ReadOnlyRootFileSystem
        && isolationPolicy.EphemeralWorkspace
        && isolationPolicy.HostMountsDisabled
        && isolationPolicy.NoNewPrivileges
        && isolationPolicy.MemoryLimitBytes <= MaximumIsolationPolicy.MemoryLimitBytes
        && isolationPolicy.CpuTimeLimitMilliseconds <= MaximumIsolationPolicy.CpuTimeLimitMilliseconds
        && isolationPolicy.WallClockTimeoutMilliseconds <= MaximumIsolationPolicy.WallClockTimeoutMilliseconds
        && isolationPolicy.ProcessLimit <= MaximumIsolationPolicy.ProcessLimit
        && isolationPolicy.TemporaryStorageLimitBytes <= MaximumIsolationPolicy.TemporaryStorageLimitBytes
        && isolationPolicy.CompilerOutputLimitBytes <= MaximumIsolationPolicy.CompilerOutputLimitBytes;

    public bool ApprovesSigningKey(string signingKeyId) =>
        approvedSigningKeyIds.Contains(signingKeyId);

    private static bool ValidateToolchain(Mql5SemanticToolchainBinding toolchain) =>
        Mql5CompileValidation.IsExactImageDigest(toolchain.RunnerImageDigest)
        && Mql5CompileValidation.IsExactSha256(toolchain.MetaEditorSha256)
        && Mql5CompileValidation.IsSafeToken(toolchain.MetaEditorVersion)
        && Mql5CompileValidation.IsExactSha256(toolchain.PlatformLibrarySnapshotSha256)
        && Mql5CompileValidation.IsExactSha256(toolchain.MetaTraderTerminalSha256)
        && Mql5CompileValidation.IsSafeToken(toolchain.MetaTraderTerminalVersion)
        && Mql5CompileValidation.IsExactImageDigest(toolchain.LoweredRuntimeImageDigest)
        && Mql5CompileValidation.IsExactSha256(toolchain.LoweredRuntimeSha256)
        && Mql5CompileValidation.IsSafeToken(toolchain.LoweredRuntimeVersion);

    private static bool ValidateTolerancePolicy(Mql5SemanticTolerancePolicy policy) =>
        string.Equals(
            policy.SchemaVersion,
            Mql5SemanticEquivalenceVerifier.TolerancePolicySchemaVersion,
            StringComparison.Ordinal)
        && Mql5CompileValidation.IsSafeToken(policy.PolicyId)
        && policy.RequireExactEventSequence
        && policy.RequireExactEventKinds
        && policy.RequireExactFieldSets
        && policy.RequireExactNonNumericValues
        && policy.RequireBothNumericLimits
        && policy.MaximumAbsoluteError >= 0
        && policy.MaximumRelativeError >= 0
        && policy.MaximumEventCount is >= 1
            and <= Mql5SemanticEquivalenceVerifier.MaximumSupportedEventCount;
}

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
        const int maximumRetainedSignatureBytes = 257;
        int retainedSignatureLength = Math.Min(
            signature?.Length ?? 0,
            maximumRetainedSignatureBytes);
        this.signature = signature is null
            ? []
            : signature.AsSpan(0, retainedSignatureLength).ToArray();
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
    string ApprovedSemanticProfileId,
    string TolerancePolicyApprovalSha256,
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
