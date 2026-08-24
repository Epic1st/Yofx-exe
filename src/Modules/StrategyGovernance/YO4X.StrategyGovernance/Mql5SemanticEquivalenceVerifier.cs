using System.Security.Cryptography;
using System.Text;
using YO4X.BuildingBlocks;

namespace YO4X.StrategyGovernance;

public sealed class Mql5SemanticEquivalenceVerifier
{
    public const string AttestationSchemaVersion = "yo4x.mql5-semantic-parity-attestation.v1";
    public const string TolerancePolicySchemaVersion = "yo4x.mql5-trace-tolerance-policy.v1";
    public const int MaximumSupportedEventCount = 100_000;

    private static readonly TimeSpan MaximumRequestAge = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MaximumFutureSkew = TimeSpan.FromSeconds(30);
    private const long MaximumAttestationAggregateUtf8Bytes = 32L * 1024 * 1024;

    private readonly IMql5RunnerAttestationVerifier attestationVerifier;
    private readonly Mql5ApprovedSemanticProfile approvedProfile;
    private readonly TimeProvider timeProvider;

    public Mql5SemanticEquivalenceVerifier(
        IMql5RunnerAttestationVerifier attestationVerifier,
        TimeProvider timeProvider,
        Mql5ApprovedSemanticProfile approvedProfile)
    {
        this.attestationVerifier = attestationVerifier
            ?? throw new ArgumentNullException(nameof(attestationVerifier));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.approvedProfile = approvedProfile
            ?? throw new ArgumentNullException(nameof(approvedProfile));
    }

    public Mql5SemanticParityEvidence Verify(
        Mql5SemanticEquivalenceRequest? request,
        Mql5SemanticRunnerAttestation? attestation)
    {
        if (request is null || !ValidateRequest(request))
        {
            return CreateLocalEvidence(request, "SEMANTIC_EQUIVALENCE_REQUEST_INVALID");
        }

        AttestationValidation validation = ValidateAttestation(request, attestation);
        if (!validation.Valid)
        {
            return CreateLocalEvidence(request, validation.ReasonCode);
        }

        Mql5SemanticRunnerAttestationDescriptor descriptor = validation.Descriptor!;
        if (descriptor.RunStatus != Mql5IsolatedRunStatus.Completed)
        {
            return CreateAttestedEvidence(
                request,
                descriptor,
                validation,
                Mql5SemanticParityState.Failed,
                descriptor.RunStatus switch
                {
                    Mql5IsolatedRunStatus.TimedOut => "SEMANTIC_TRACE_RUN_TIMED_OUT",
                    Mql5IsolatedRunStatus.Unsupported => "SEMANTIC_TRACE_RUN_UNSUPPORTED",
                    _ => "SEMANTIC_TRACE_RUN_FAILED"
                });
        }

        (Mql5SemanticParityState State, string ReasonCode) traceResult =
            ValidateTraceComparison(request, descriptor);
        return CreateAttestedEvidence(
            request,
            descriptor,
            validation,
            traceResult.State,
            traceResult.ReasonCode);
    }

    public static string ComputeToolchainBindingSha256(Mql5SemanticToolchainBinding toolchain)
    {
        ArgumentNullException.ThrowIfNull(toolchain);
        return CanonicalJson.Sha256(toolchain);
    }

    public static string ComputeTolerancePolicySha256(Mql5SemanticTolerancePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return CanonicalJson.Sha256(policy);
    }

    public static string ComputeRequestSha256(Mql5SemanticEquivalenceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CanonicalJson.Sha256(request);
    }

    internal static string ComputeInputEventIndexSha256(
        IReadOnlyList<Mql5SemanticTraceEventEvidence> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        InputEventIndexEntry[] index = events.Select(static item => new InputEventIndexEntry(
                item.EventIndex,
                item.EventKind,
                item.InputEventSha256))
            .ToArray();
        return CanonicalJson.Sha256(index);
    }

    internal static string ComputeReferenceOutputEventIndexSha256(
        IReadOnlyList<Mql5SemanticTraceEventEvidence> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        OutputEventIndexEntry[] index = events.Select(static item => new OutputEventIndexEntry(
                item.EventIndex,
                item.EventKind,
                item.ReferenceOutputEventSha256))
            .ToArray();
        return CanonicalJson.Sha256(index);
    }

    internal static string ComputeLoweredOutputEventIndexSha256(
        IReadOnlyList<Mql5SemanticTraceEventEvidence> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        OutputEventIndexEntry[] index = events.Select(static item => new OutputEventIndexEntry(
                item.EventIndex,
                item.EventKind,
                item.LoweredOutputEventSha256))
            .ToArray();
        return CanonicalJson.Sha256(index);
    }

    private AttestationValidation ValidateAttestation(
        Mql5SemanticEquivalenceRequest request,
        Mql5SemanticRunnerAttestation? attestation)
    {
        Mql5SemanticRunnerAttestationDescriptor? untrustedDescriptor = attestation?.Descriptor;
        if (untrustedDescriptor is null
            || !TrySnapshotDescriptor(
                untrustedDescriptor,
                request.TolerancePolicy.MaximumEventCount,
                out Mql5SemanticRunnerAttestationDescriptor descriptor)
            || descriptor.ReferenceOutputEventCount is < 0
                || descriptor.ReferenceOutputEventCount > request.TolerancePolicy.MaximumEventCount
            || descriptor.LoweredOutputEventCount is < 0
                || descriptor.LoweredOutputEventCount > request.TolerancePolicy.MaximumEventCount
            || !string.Equals(
                descriptor.SchemaVersion,
                AttestationSchemaVersion,
                StringComparison.Ordinal)
            || descriptor.JobId != request.JobId
            || !Mql5CompileValidation.IsSafeToken(descriptor.RunnerId)
            || !Mql5CompileValidation.IsSafeToken(descriptor.RunnerSessionId)
            || !Mql5CompileValidation.IsExactImageDigest(descriptor.RunnerImageDigest)
            || !Mql5CompileValidation.IsExactSha256(descriptor.RequestSha256)
            || !Mql5CompileValidation.IsExactSha256(descriptor.ReferenceOutputTraceSha256)
            || !Mql5CompileValidation.IsExactSha256(descriptor.ReferenceOutputEventIndexSha256)
            || !Mql5CompileValidation.IsExactSha256(descriptor.LoweredOutputTraceSha256)
            || !Mql5CompileValidation.IsExactSha256(descriptor.LoweredOutputEventIndexSha256)
            || !Mql5CompileValidation.IsSafeToken(attestation!.SigningKeyId)
            || !string.Equals(
                attestation.Algorithm,
                Mql5CompileValidation.SignatureAlgorithm,
                StringComparison.Ordinal)
            || !Mql5CompileValidation.IsExactSha256(attestation.SignatureSha256)
            || !Mql5CompileValidation.IsExactSha256(attestation.SignedPayloadSha256))
        {
            return AttestationValidation.Invalid("SEMANTIC_RUNNER_ATTESTATION_INVALID");
        }

        if (!approvedProfile.ApprovesSigningKey(attestation.SigningKeyId!))
        {
            return AttestationValidation.Invalid("SEMANTIC_RUNNER_SIGNING_KEY_NOT_APPROVED");
        }

        string requestSha256 = ComputeRequestSha256(request);
        if (!Mql5CompileValidation.FixedTimeHexEquals(descriptor.RequestSha256, requestSha256)
            || !string.Equals(descriptor.RelativePath, request.RelativePath, StringComparison.Ordinal)
            || !BoundSha256(descriptor.SourceSha256, request.SourceSha256)
            || !BoundSha256(descriptor.DependencyClosureSha256, request.DependencyClosureSha256)
            || !BoundSha256(descriptor.DependencyGraphSha256, request.DependencyGraphSha256)
            || !BoundSha256(descriptor.CorpusSha256, request.CorpusSha256)
            || !BoundSha256(descriptor.ConversionEvidenceSha256, request.ConversionEvidenceSha256)
            || !BoundSha256(descriptor.CompilerArtifactSha256, request.CompilerArtifactSha256)
            || !BoundSha256(descriptor.RestrictedIrSha256, request.RestrictedIrSha256)
            || !BoundSha256(descriptor.ToolchainBindingSha256, request.ToolchainBindingSha256)
            || !BoundSha256(descriptor.ReferenceInputTraceSha256, request.ReferenceInputTraceSha256)
            || !BoundSha256(
                descriptor.ReferenceInputEventIndexSha256,
                request.ReferenceInputEventIndexSha256)
            || !BoundSha256(descriptor.TolerancePolicySha256, request.TolerancePolicySha256)
            || !BoundSha256(
                descriptor.TolerancePolicyApprovalSha256,
                request.TolerancePolicyApprovalSha256)
            || !string.Equals(
                descriptor.RunnerImageDigest,
                request.Toolchain.RunnerImageDigest,
                StringComparison.Ordinal))
        {
            return AttestationValidation.Invalid("SEMANTIC_RUNNER_ATTESTATION_BINDING_INVALID");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (descriptor.StartedAtUtc.Offset != TimeSpan.Zero
            || descriptor.CompletedAtUtc.Offset != TimeSpan.Zero
            || descriptor.StartedAtUtc < request.RequestedAtUtc.AddSeconds(-5)
            || descriptor.CompletedAtUtc < descriptor.StartedAtUtc
            || descriptor.CompletedAtUtc > now + MaximumFutureSkew
            || descriptor.CompletedAtUtc - descriptor.StartedAtUtc
                > TimeSpan.FromMilliseconds(request.IsolationPolicy.WallClockTimeoutMilliseconds))
        {
            return AttestationValidation.Invalid("SEMANTIC_RUNNER_ATTESTATION_TIME_INVALID");
        }

        byte[] signature = attestation.GetSignature();
        try
        {
            if (signature.Length is < 64 or > 256)
            {
                return AttestationValidation.Invalid("SEMANTIC_RUNNER_ATTESTATION_INVALID");
            }

            string signatureSha256 = Convert.ToHexString(SHA256.HashData(signature)).ToLowerInvariant();
            string canonicalPayload = CanonicalJson.Serialize(descriptor);
            string signedPayloadSha256 = CanonicalJson.Sha256(descriptor);
            if (!Mql5CompileValidation.FixedTimeHexEquals(
                    attestation.SignatureSha256!,
                    signatureSha256)
                || !Mql5CompileValidation.FixedTimeHexEquals(
                    attestation.SignedPayloadSha256!,
                    signedPayloadSha256)
                || !attestationVerifier.Verify(
                    attestation.SigningKeyId!,
                    attestation.Algorithm!,
                    signature,
                    canonicalPayload))
            {
                return AttestationValidation.Invalid("SEMANTIC_RUNNER_ATTESTATION_UNTRUSTED");
            }

            string attestationSha256 = CanonicalJson.Sha256(new AttestationIdentity(
                signedPayloadSha256,
                attestation.Algorithm!,
                attestation.SigningKeyId!,
                signatureSha256));
            return AttestationValidation.Success(
                attestationSha256,
                attestation.SigningKeyId!,
                descriptor);
        }
        catch (Exception exception) when (exception is CryptographicException
            or ArgumentException
            or InvalidOperationException)
        {
            return AttestationValidation.Invalid("SEMANTIC_RUNNER_ATTESTATION_UNTRUSTED");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signature);
        }
    }

    private static (Mql5SemanticParityState State, string ReasonCode) ValidateTraceComparison(
        Mql5SemanticEquivalenceRequest request,
        Mql5SemanticRunnerAttestationDescriptor descriptor)
    {
        if (descriptor.ReferenceOutputEventCount != request.InputEventCount
            || descriptor.LoweredOutputEventCount != request.InputEventCount
            || descriptor.Events.Count != request.InputEventCount)
        {
            return (Mql5SemanticParityState.Failed, "SEMANTIC_TRACE_EVENT_COUNT_MISMATCH");
        }

        bool structuralDivergence = false;
        bool toleranceExceeded = false;
        bool evidenceInconsistent = false;
        bool allEventOutputsExact = true;
        for (int index = 0; index < descriptor.Events.Count; index++)
        {
            Mql5SemanticTraceEventEvidence item = descriptor.Events[index];
            if (item is null
                || item.EventIndex != index
                || !Mql5CompileValidation.IsSafeToken(item.EventKind, maximum: 100)
                || !Mql5CompileValidation.IsExactSha256(item.InputEventSha256)
                || !Mql5CompileValidation.IsExactSha256(item.ReferenceOutputEventSha256)
                || !Mql5CompileValidation.IsExactSha256(item.LoweredOutputEventSha256)
                || item.ComparedNumericValueCount < 0
                || item.ExactNumericValueCount < 0
                || item.NumericDivergenceCount < 0
                || item.MissingReferenceFieldCount < 0
                || item.MissingLoweredFieldCount < 0
                || item.NonNumericMismatchCount < 0
                || item.MaximumAbsoluteError < 0
                || item.MaximumRelativeError < 0
                || item.ExactNumericValueCount + item.NumericDivergenceCount
                    != item.ComparedNumericValueCount)
            {
                return (Mql5SemanticParityState.Failed, "SEMANTIC_TRACE_EVENT_EVIDENCE_INVALID");
            }

            bool eventOutputsExact = Mql5CompileValidation.FixedTimeHexEquals(
                item.ReferenceOutputEventSha256,
                item.LoweredOutputEventSha256);
            allEventOutputsExact &= eventOutputsExact;
            structuralDivergence |= item.MissingReferenceFieldCount != 0
                || item.MissingLoweredFieldCount != 0
                || item.NonNumericMismatchCount != 0;
            toleranceExceeded |= item.MaximumAbsoluteError
                    > request.TolerancePolicy.MaximumAbsoluteError
                || item.MaximumRelativeError
                    > request.TolerancePolicy.MaximumRelativeError;
            evidenceInconsistent |= item.NumericDivergenceCount == 0
                    && (item.MaximumAbsoluteError != 0
                        || item.MaximumRelativeError != 0
                        || !eventOutputsExact)
                || item.NumericDivergenceCount > 0
                    && (eventOutputsExact
                        || item.MaximumAbsoluteError == 0
                            && item.MaximumRelativeError == 0);
        }

        if (!BoundSha256(
                ComputeInputEventIndexSha256(descriptor.Events),
                request.ReferenceInputEventIndexSha256)
            || !BoundSha256(
                ComputeReferenceOutputEventIndexSha256(descriptor.Events),
                descriptor.ReferenceOutputEventIndexSha256)
            || !BoundSha256(
                ComputeLoweredOutputEventIndexSha256(descriptor.Events),
                descriptor.LoweredOutputEventIndexSha256))
        {
            return (Mql5SemanticParityState.Failed, "SEMANTIC_TRACE_INDEX_DIGEST_INVALID");
        }

        if (structuralDivergence)
        {
            return (Mql5SemanticParityState.Failed, "SEMANTIC_TRACE_STRUCTURAL_DIVERGENCE");
        }

        if (evidenceInconsistent)
        {
            return (Mql5SemanticParityState.Failed, "SEMANTIC_TRACE_DIVERGENCE_EVIDENCE_INVALID");
        }

        if (toleranceExceeded)
        {
            return (Mql5SemanticParityState.Failed, "SEMANTIC_TRACE_NUMERIC_TOLERANCE_EXCEEDED");
        }

        bool exactTraceDigests = BoundSha256(
                descriptor.ReferenceOutputTraceSha256,
                descriptor.LoweredOutputTraceSha256)
            && BoundSha256(
                descriptor.ReferenceOutputEventIndexSha256,
                descriptor.LoweredOutputEventIndexSha256);
        if (allEventOutputsExact != exactTraceDigests)
        {
            return (Mql5SemanticParityState.Failed, "SEMANTIC_TRACE_AGGREGATE_DIGEST_INCONSISTENT");
        }

        return (Mql5SemanticParityState.Proven, "SEMANTIC_PARITY_PROVEN_BY_ATTESTED_TRACE_COMPARISON");
    }

    private bool ValidateRequest(Mql5SemanticEquivalenceRequest request)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (request.JobId == Guid.Empty
            || request.RequestedAtUtc.Offset != TimeSpan.Zero
            || request.RequestedAtUtc < now - MaximumRequestAge
            || request.RequestedAtUtc > now + MaximumFutureSkew
            || !Mql5CompileValidation.IsSafeRelativeSourcePath(request.RelativePath)
            || !Path.GetExtension(request.RelativePath).Equals(".mq5", StringComparison.OrdinalIgnoreCase)
            || !AllExactSha256(
                request.SourceSha256,
                request.DependencyClosureSha256,
                request.DependencyGraphSha256,
                request.CorpusSha256,
                request.ConversionEvidenceSha256,
                request.CompilerArtifactSha256,
                request.RestrictedIrSha256,
                request.ToolchainBindingSha256,
                request.ReferenceInputTraceSha256,
                request.ReferenceInputEventIndexSha256,
                request.TolerancePolicySha256,
                request.TolerancePolicyApprovalSha256)
            || request.Toolchain is null
            || request.TolerancePolicy is null
            || request.IsolationPolicy is null
            || request.InputEventCount is < 1 or > MaximumSupportedEventCount
            || !ValidateToolchain(request.Toolchain)
            || !ValidateTolerancePolicy(request.TolerancePolicy)
            || request.InputEventCount > request.TolerancePolicy.MaximumEventCount
            || !ValidateIsolationPolicy(request.IsolationPolicy)
            || !approvedProfile.Approves(
                request.Toolchain,
                request.TolerancePolicy,
                request.IsolationPolicy)
            || !BoundSha256(
                request.TolerancePolicyApprovalSha256,
                approvedProfile.ApprovalSha256)
            || !BoundSha256(
                request.ToolchainBindingSha256,
                ComputeToolchainBindingSha256(request.Toolchain))
            || !BoundSha256(
                request.TolerancePolicySha256,
                ComputeTolerancePolicySha256(request.TolerancePolicy)))
        {
            return false;
        }

        return true;
    }

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

    private static bool HasSafeEventEnvelope(Mql5SemanticTraceEventEvidence? item) =>
        item is not null
        && item.EventIndex >= 0
        && Mql5CompileValidation.IsSafeToken(item.EventKind, maximum: 100)
        && Mql5CompileValidation.IsExactSha256(item.InputEventSha256)
        && Mql5CompileValidation.IsExactSha256(item.ReferenceOutputEventSha256)
        && Mql5CompileValidation.IsExactSha256(item.LoweredOutputEventSha256)
        && item.ComparedNumericValueCount >= 0
        && item.ExactNumericValueCount >= 0
        && item.NumericDivergenceCount >= 0
        && item.MissingReferenceFieldCount >= 0
        && item.MissingLoweredFieldCount >= 0
        && item.NonNumericMismatchCount >= 0
        && item.MaximumAbsoluteError >= 0
        && item.MaximumRelativeError >= 0;

    private static bool TrySnapshotDescriptor(
        Mql5SemanticRunnerAttestationDescriptor source,
        int policyMaximumEventCount,
        out Mql5SemanticRunnerAttestationDescriptor snapshot)
    {
        snapshot = null!;
        if (source.Events is null)
        {
            return false;
        }

        try
        {
            long aggregateBytes = 0;
            if (!AddBoundedUtf8(ref aggregateBytes, source.SchemaVersion, 100)
                || !AddBoundedUtf8(ref aggregateBytes, source.RequestSha256, 64)
                || !AddBoundedUtf8(ref aggregateBytes, source.RelativePath, 4_096)
                || !AddBoundedUtf8(ref aggregateBytes, source.SourceSha256, 64)
                || !AddBoundedUtf8(ref aggregateBytes, source.DependencyClosureSha256, 64)
                || !AddBoundedUtf8(ref aggregateBytes, source.DependencyGraphSha256, 64)
                || !AddBoundedUtf8(ref aggregateBytes, source.CorpusSha256, 64)
                || !AddBoundedUtf8(ref aggregateBytes, source.ConversionEvidenceSha256, 64)
                || !AddBoundedUtf8(ref aggregateBytes, source.CompilerArtifactSha256, 64)
                || !AddBoundedUtf8(ref aggregateBytes, source.RestrictedIrSha256, 64)
                || !AddBoundedUtf8(ref aggregateBytes, source.ToolchainBindingSha256, 64)
                || !AddBoundedUtf8(ref aggregateBytes, source.ReferenceInputTraceSha256, 64)
                || !AddBoundedUtf8(ref aggregateBytes, source.ReferenceInputEventIndexSha256, 64)
                || !AddBoundedUtf8(ref aggregateBytes, source.TolerancePolicySha256, 64)
                || !AddBoundedUtf8(ref aggregateBytes, source.TolerancePolicyApprovalSha256, 64)
                || !AddBoundedUtf8(ref aggregateBytes, source.RunnerId, 200)
                || !AddBoundedUtf8(ref aggregateBytes, source.RunnerSessionId, 200)
                || !AddBoundedUtf8(ref aggregateBytes, source.RunnerImageDigest, 71)
                || !AddBoundedUtf8(ref aggregateBytes, source.ReferenceOutputTraceSha256, 64)
                || !AddBoundedUtf8(ref aggregateBytes, source.ReferenceOutputEventIndexSha256, 64)
                || !AddBoundedUtf8(ref aggregateBytes, source.LoweredOutputTraceSha256, 64)
                || !AddBoundedUtf8(ref aggregateBytes, source.LoweredOutputEventIndexSha256, 64))
            {
                return false;
            }

            int maximumEvents = Math.Min(policyMaximumEventCount, MaximumSupportedEventCount);
            int eventCount = source.Events.Count;
            if (eventCount < 0 || eventCount > maximumEvents)
            {
                return false;
            }

            var events = new Mql5SemanticTraceEventEvidence[eventCount];
            for (int index = 0; index < eventCount; index++)
            {
                Mql5SemanticTraceEventEvidence? item = source.Events[index];
                if (!HasSafeEventEnvelope(item)
                    || !AddBoundedUtf8(ref aggregateBytes, item.EventKind, 100)
                    || !AddBoundedUtf8(ref aggregateBytes, item.InputEventSha256, 64)
                    || !AddBoundedUtf8(ref aggregateBytes, item.ReferenceOutputEventSha256, 64)
                    || !AddBoundedUtf8(ref aggregateBytes, item.LoweredOutputEventSha256, 64)
                    || !AddAggregateBytes(ref aggregateBytes, 96))
                {
                    return false;
                }

                events[index] = item;
            }

            snapshot = source with { Events = Array.AsReadOnly(events) };
            return true;
        }
        catch (Exception exception) when (IsNonCatastrophic(exception))
        {
            snapshot = null!;
            return false;
        }
    }

    private static bool AddBoundedUtf8(
        ref long aggregateBytes,
        string? value,
        int maximumCharacters)
    {
        if (value is null || value.Length > maximumCharacters)
        {
            return false;
        }

        return AddAggregateBytes(ref aggregateBytes, Encoding.UTF8.GetByteCount(value));
    }

    private static bool AddAggregateBytes(ref long aggregateBytes, int additionalBytes)
    {
        if (additionalBytes < 0
            || aggregateBytes > MaximumAttestationAggregateUtf8Bytes - additionalBytes)
        {
            return false;
        }

        aggregateBytes += additionalBytes;
        return true;
    }

    private static bool IsNonCatastrophic(Exception exception) => exception is not (
        OutOfMemoryException
        or StackOverflowException
        or AccessViolationException);

    private static bool ValidateTolerancePolicy(Mql5SemanticTolerancePolicy policy) =>
        string.Equals(
            policy.SchemaVersion,
            TolerancePolicySchemaVersion,
            StringComparison.Ordinal)
        && Mql5CompileValidation.IsSafeToken(policy.PolicyId)
        && policy.RequireExactEventSequence
        && policy.RequireExactEventKinds
        && policy.RequireExactFieldSets
        && policy.RequireExactNonNumericValues
        && policy.RequireBothNumericLimits
        && policy.MaximumAbsoluteError >= 0
        && policy.MaximumRelativeError >= 0
        && policy.MaximumEventCount is >= 1 and <= MaximumSupportedEventCount;

    private static bool ValidateIsolationPolicy(Mql5IsolationPolicy policy) =>
        policy.NetworkAccessDisabled
        && policy.ReadOnlyRootFileSystem
        && policy.EphemeralWorkspace
        && policy.HostMountsDisabled
        && policy.NoNewPrivileges
        && policy.MemoryLimitBytes is >= 64 * 1024 * 1024 and <= 4L * 1024 * 1024 * 1024
        && policy.CpuTimeLimitMilliseconds is >= 1_000 and <= 10 * 60 * 1_000
        && policy.WallClockTimeoutMilliseconds is >= 1_000 and <= 15 * 60 * 1_000
        && policy.ProcessLimit is >= 1 and <= 64
        && policy.TemporaryStorageLimitBytes is >= 1024 * 1024 and <= 4L * 1024 * 1024 * 1024
        && policy.CompilerOutputLimitBytes is >= 1024 and <= 16 * 1024 * 1024;

    private static bool AllExactSha256(params string[] values) =>
        values.All(Mql5CompileValidation.IsExactSha256);

    private static bool BoundSha256(string left, string right) =>
        Mql5CompileValidation.FixedTimeHexEquals(left, right);

    private Mql5SemanticParityEvidence CreateLocalEvidence(
        Mql5SemanticEquivalenceRequest? request,
        string reasonCode) => new(
            request?.JobId ?? Guid.Empty,
            SafeSourcePathOrEmpty(request?.RelativePath),
            SafeSha256OrEmpty(request?.SourceSha256),
            SafeSha256OrEmpty(request?.DependencyClosureSha256),
            SafeSha256OrEmpty(request?.CorpusSha256),
            SafeSha256OrEmpty(request?.CompilerArtifactSha256),
            SafeSha256OrEmpty(request?.ToolchainBindingSha256),
            SafeSha256OrEmpty(request?.ReferenceInputTraceSha256),
            null,
            null,
            SafeSha256OrEmpty(request?.TolerancePolicySha256),
            approvedProfile.ProfileId,
            approvedProfile.ApprovalSha256,
            Mql5SemanticParityState.Blocked,
            reasonCode,
            null,
            null,
            null,
            null,
            null,
            null,
            0);

    private Mql5SemanticParityEvidence CreateAttestedEvidence(
        Mql5SemanticEquivalenceRequest request,
        Mql5SemanticRunnerAttestationDescriptor descriptor,
        AttestationValidation validation,
        Mql5SemanticParityState state,
        string reasonCode) => new(
            request.JobId,
            request.RelativePath,
            request.SourceSha256,
            request.DependencyClosureSha256,
            request.CorpusSha256,
            request.CompilerArtifactSha256,
            request.ToolchainBindingSha256,
            request.ReferenceInputTraceSha256,
            descriptor.ReferenceOutputTraceSha256,
            descriptor.LoweredOutputTraceSha256,
            request.TolerancePolicySha256,
            approvedProfile.ProfileId,
            approvedProfile.ApprovalSha256,
            state,
            reasonCode,
            descriptor.RunnerId,
            descriptor.RunnerSessionId,
            validation.SigningKeyId,
            validation.AttestationSha256,
            descriptor.StartedAtUtc,
            descriptor.CompletedAtUtc,
            descriptor.Events.Count);

    private static string SafeSha256OrEmpty(string? value) =>
        Mql5CompileValidation.IsExactSha256(value) ? value! : string.Empty;

    private static string SafeSourcePathOrEmpty(string? value) =>
        Mql5CompileValidation.IsSafeRelativeSourcePath(value) ? value! : string.Empty;

    private sealed record InputEventIndexEntry(
        int EventIndex,
        string EventKind,
        string InputEventSha256);

    private sealed record OutputEventIndexEntry(
        int EventIndex,
        string EventKind,
        string OutputEventSha256);

    private sealed record AttestationIdentity(
        string SignedPayloadSha256,
        string Algorithm,
        string SigningKeyId,
        string SignatureSha256);

    private sealed record AttestationValidation(
        bool Valid,
        string ReasonCode,
        string AttestationSha256,
        string SigningKeyId,
        Mql5SemanticRunnerAttestationDescriptor? Descriptor)
    {
        public static AttestationValidation Invalid(string reasonCode) =>
            new(false, reasonCode, string.Empty, string.Empty, null);

        public static AttestationValidation Success(
            string attestationSha256,
            string signingKeyId,
            Mql5SemanticRunnerAttestationDescriptor descriptor) =>
            new(true, string.Empty, attestationSha256, signingKeyId, descriptor);
    }
}
