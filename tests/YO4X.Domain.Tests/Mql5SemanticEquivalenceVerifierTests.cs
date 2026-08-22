using System.Security.Cryptography;
using YO4X.BuildingBlocks;
using YO4X.StrategyGovernance;

namespace YO4X.Domain.Tests;

public sealed class Mql5SemanticEquivalenceVerifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StaticAndConversionDigestsAloneCanNeverCreateSemanticParityProof()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using EcdsaP256Mql5RunnerAttestationVerifier trust = CreateTrust(signer);
        Mql5SemanticEquivalenceRequest request = CreateRequest(CreateExactEvents());
        var verifier = new Mql5SemanticEquivalenceVerifier(trust, new FixedTimeProvider(Now));

        Mql5SemanticParityEvidence evidence = verifier.Verify(request, attestation: null);

        Assert.Equal(Mql5SemanticParityState.Blocked, evidence.State);
        Assert.Equal("SEMANTIC_RUNNER_ATTESTATION_INVALID", evidence.ReasonCode);
        Assert.False(evidence.SemanticParityProven);
        Assert.Null(evidence.ReferenceOutputTraceSha256);
        Assert.Null(evidence.LoweredOutputTraceSha256);
    }

    [Fact]
    public void ExactBoundTracesAndTrustedRunnerAttestationCreateProof()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using EcdsaP256Mql5RunnerAttestationVerifier trust = CreateTrust(signer);
        Mql5SemanticTraceEventEvidence[] events = CreateExactEvents();
        Mql5SemanticEquivalenceRequest request = CreateRequest(events);
        Mql5SemanticRunnerAttestation attestation = CreateSignedAttestation(
            request,
            events,
            signer,
            referenceTraceSha256: new string('1', 64),
            loweredTraceSha256: new string('1', 64));
        var verifier = new Mql5SemanticEquivalenceVerifier(trust, new FixedTimeProvider(Now));

        Mql5SemanticParityEvidence evidence = verifier.Verify(request, attestation);

        Assert.Equal(Mql5SemanticParityState.Proven, evidence.State);
        Assert.Equal(
            "SEMANTIC_PARITY_PROVEN_BY_ATTESTED_TRACE_COMPARISON",
            evidence.ReasonCode);
        Assert.True(evidence.SemanticParityProven);
        Assert.Equal(events.Length, evidence.ComparedEventCount);
        Assert.NotNull(evidence.AttestationSha256);
        Assert.Equal("runner-key-1", evidence.AttestationSigningKeyId);
    }

    [Fact]
    public void ExplicitApprovedNumericToleranceCanProveNonExactNumericTrace()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using EcdsaP256Mql5RunnerAttestationVerifier trust = CreateTrust(signer);
        Mql5SemanticTraceEventEvidence[] events =
        [
            CreateExactEvents()[0] with
            {
                LoweredOutputEventSha256 = new string('9', 64),
                ExactNumericValueCount = 1,
                NumericDivergenceCount = 1,
                MaximumAbsoluteError = 0.00005m,
                MaximumRelativeError = 0.00004m
            },
            CreateExactEvents()[1]
        ];
        Mql5SemanticEquivalenceRequest request = CreateRequest(events);
        Mql5SemanticRunnerAttestation attestation = CreateSignedAttestation(
            request,
            events,
            signer,
            referenceTraceSha256: new string('1', 64),
            loweredTraceSha256: new string('2', 64));
        var verifier = new Mql5SemanticEquivalenceVerifier(trust, new FixedTimeProvider(Now));

        Mql5SemanticParityEvidence evidence = verifier.Verify(request, attestation);

        Assert.True(evidence.SemanticParityProven);
        Assert.NotEqual(
            evidence.ReferenceOutputTraceSha256,
            evidence.LoweredOutputTraceSha256);
    }

    [Fact]
    public void NumericErrorBeyondEitherRequiredLimitFailsParity()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using EcdsaP256Mql5RunnerAttestationVerifier trust = CreateTrust(signer);
        Mql5SemanticTraceEventEvidence[] events =
        [
            CreateExactEvents()[0] with
            {
                LoweredOutputEventSha256 = new string('9', 64),
                ExactNumericValueCount = 1,
                NumericDivergenceCount = 1,
                MaximumAbsoluteError = 0.0002m,
                MaximumRelativeError = 0.00004m
            },
            CreateExactEvents()[1]
        ];
        Mql5SemanticEquivalenceRequest request = CreateRequest(events);
        Mql5SemanticRunnerAttestation attestation = CreateSignedAttestation(
            request,
            events,
            signer,
            referenceTraceSha256: new string('1', 64),
            loweredTraceSha256: new string('2', 64));
        var verifier = new Mql5SemanticEquivalenceVerifier(trust, new FixedTimeProvider(Now));

        Mql5SemanticParityEvidence evidence = verifier.Verify(request, attestation);

        Assert.Equal(Mql5SemanticParityState.Failed, evidence.State);
        Assert.Equal("SEMANTIC_TRACE_NUMERIC_TOLERANCE_EXCEEDED", evidence.ReasonCode);
        Assert.False(evidence.SemanticParityProven);
    }

    [Fact]
    public void MissingFieldsOrNonNumericDivergenceAlwaysFailParity()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using EcdsaP256Mql5RunnerAttestationVerifier trust = CreateTrust(signer);
        Mql5SemanticTraceEventEvidence[] events =
        [
            CreateExactEvents()[0] with
            {
                LoweredOutputEventSha256 = new string('9', 64),
                NonNumericMismatchCount = 1
            },
            CreateExactEvents()[1]
        ];
        Mql5SemanticEquivalenceRequest request = CreateRequest(events);
        Mql5SemanticRunnerAttestation attestation = CreateSignedAttestation(
            request,
            events,
            signer,
            referenceTraceSha256: new string('1', 64),
            loweredTraceSha256: new string('2', 64));
        var verifier = new Mql5SemanticEquivalenceVerifier(trust, new FixedTimeProvider(Now));

        Mql5SemanticParityEvidence evidence = verifier.Verify(request, attestation);

        Assert.Equal(Mql5SemanticParityState.Failed, evidence.State);
        Assert.Equal("SEMANTIC_TRACE_STRUCTURAL_DIVERGENCE", evidence.ReasonCode);
    }

    [Fact]
    public void SignedDependencyBindingDriftIsStillBlocked()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using EcdsaP256Mql5RunnerAttestationVerifier trust = CreateTrust(signer);
        Mql5SemanticTraceEventEvidence[] events = CreateExactEvents();
        Mql5SemanticEquivalenceRequest request = CreateRequest(events);
        Mql5SemanticRunnerAttestation attestation = CreateSignedAttestation(
            request,
            events,
            signer,
            referenceTraceSha256: new string('1', 64),
            loweredTraceSha256: new string('1', 64),
            transform: descriptor => descriptor with
            {
                DependencyClosureSha256 = new string('8', 64)
            });
        var verifier = new Mql5SemanticEquivalenceVerifier(trust, new FixedTimeProvider(Now));

        Mql5SemanticParityEvidence evidence = verifier.Verify(request, attestation);

        Assert.Equal(Mql5SemanticParityState.Blocked, evidence.State);
        Assert.Equal("SEMANTIC_RUNNER_ATTESTATION_BINDING_INVALID", evidence.ReasonCode);
    }

    [Fact]
    public void SignedEventIndexDigestDriftFailsTraceEvidence()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using EcdsaP256Mql5RunnerAttestationVerifier trust = CreateTrust(signer);
        Mql5SemanticTraceEventEvidence[] events = CreateExactEvents();
        Mql5SemanticEquivalenceRequest request = CreateRequest(events);
        Mql5SemanticRunnerAttestation attestation = CreateSignedAttestation(
            request,
            events,
            signer,
            referenceTraceSha256: new string('1', 64),
            loweredTraceSha256: new string('1', 64),
            transform: descriptor => descriptor with
            {
                ReferenceOutputEventIndexSha256 = new string('8', 64)
            });
        var verifier = new Mql5SemanticEquivalenceVerifier(trust, new FixedTimeProvider(Now));

        Mql5SemanticParityEvidence evidence = verifier.Verify(request, attestation);

        Assert.Equal(Mql5SemanticParityState.Failed, evidence.State);
        Assert.Equal("SEMANTIC_TRACE_INDEX_DIGEST_INVALID", evidence.ReasonCode);
    }

    [Fact]
    public void ForgedRunnerSignatureCannotCreateSemanticProof()
    {
        using ECDsa trustedSigner = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using EcdsaP256Mql5RunnerAttestationVerifier trust = CreateTrust(trustedSigner);
        Mql5SemanticTraceEventEvidence[] events = CreateExactEvents();
        Mql5SemanticEquivalenceRequest request = CreateRequest(events);
        Mql5SemanticRunnerAttestation attestation = CreateSignedAttestation(
            request,
            events,
            attacker,
            referenceTraceSha256: new string('1', 64),
            loweredTraceSha256: new string('1', 64));
        var verifier = new Mql5SemanticEquivalenceVerifier(trust, new FixedTimeProvider(Now));

        Mql5SemanticParityEvidence evidence = verifier.Verify(request, attestation);

        Assert.Equal(Mql5SemanticParityState.Blocked, evidence.State);
        Assert.Equal("SEMANTIC_RUNNER_ATTESTATION_UNTRUSTED", evidence.ReasonCode);
        Assert.False(evidence.SemanticParityProven);
    }

    [Fact]
    public void PolicyOrToolchainDigestDriftInvalidatesRequestBeforeAttestation()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using EcdsaP256Mql5RunnerAttestationVerifier trust = CreateTrust(signer);
        Mql5SemanticEquivalenceRequest request = CreateRequest(CreateExactEvents()) with
        {
            ToolchainBindingSha256 = new string('8', 64)
        };
        var verifier = new Mql5SemanticEquivalenceVerifier(trust, new FixedTimeProvider(Now));

        Mql5SemanticParityEvidence evidence = verifier.Verify(request, attestation: null);

        Assert.Equal(Mql5SemanticParityState.Blocked, evidence.State);
        Assert.Equal("SEMANTIC_EQUIVALENCE_REQUEST_INVALID", evidence.ReasonCode);
    }

    private static Mql5SemanticEquivalenceRequest CreateRequest(
        Mql5SemanticTraceEventEvidence[] events)
    {
        var toolchain = new Mql5SemanticToolchainBinding(
            "sha256:" + new string('a', 64),
            new string('b', 64),
            "metaeditor-5.0.0.6140",
            new string('c', 64),
            new string('d', 64),
            "terminal-5.0.0.6140",
            "sha256:" + new string('e', 64),
            new string('f', 64),
            "yo4x-restricted-runtime-1.0.0");
        var policy = new Mql5SemanticTolerancePolicy(
            Mql5SemanticEquivalenceVerifier.TolerancePolicySchemaVersion,
            "demo-reference-parity-v1",
            RequireExactEventSequence: true,
            RequireExactEventKinds: true,
            RequireExactFieldSets: true,
            RequireExactNonNumericValues: true,
            RequireBothNumericLimits: true,
            MaximumAbsoluteError: 0.0001m,
            MaximumRelativeError: 0.0001m,
            MaximumEventCount: 10_000);
        return new Mql5SemanticEquivalenceRequest(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Now.AddMinutes(-1),
            "main.mq5",
            new string('1', 64),
            new string('2', 64),
            new string('3', 64),
            new string('4', 64),
            new string('5', 64),
            new string('6', 64),
            new string('7', 64),
            toolchain,
            Mql5SemanticEquivalenceVerifier.ComputeToolchainBindingSha256(toolchain),
            new string('8', 64),
            Mql5SemanticEquivalenceVerifier.ComputeInputEventIndexSha256(events),
            events.Length,
            policy,
            Mql5SemanticEquivalenceVerifier.ComputeTolerancePolicySha256(policy),
            new string('9', 64),
            new Mql5IsolationPolicy(
                NetworkAccessDisabled: true,
                ReadOnlyRootFileSystem: true,
                EphemeralWorkspace: true,
                HostMountsDisabled: true,
                NoNewPrivileges: true,
                MemoryLimitBytes: 512 * 1024 * 1024,
                CpuTimeLimitMilliseconds: 60_000,
                WallClockTimeoutMilliseconds: 60_000,
                ProcessLimit: 8,
                TemporaryStorageLimitBytes: 256 * 1024 * 1024,
                CompilerOutputLimitBytes: 1024 * 1024));
    }

    private static Mql5SemanticTraceEventEvidence[] CreateExactEvents() =>
    [
        new(
            0,
            "OnTick",
            new string('a', 64),
            new string('b', 64),
            new string('b', 64),
            ComparedNumericValueCount: 2,
            ExactNumericValueCount: 2,
            NumericDivergenceCount: 0,
            MissingReferenceFieldCount: 0,
            MissingLoweredFieldCount: 0,
            NonNumericMismatchCount: 0,
            MaximumAbsoluteError: 0,
            MaximumRelativeError: 0),
        new(
            1,
            "OnTimer",
            new string('c', 64),
            new string('d', 64),
            new string('d', 64),
            ComparedNumericValueCount: 3,
            ExactNumericValueCount: 3,
            NumericDivergenceCount: 0,
            MissingReferenceFieldCount: 0,
            MissingLoweredFieldCount: 0,
            NonNumericMismatchCount: 0,
            MaximumAbsoluteError: 0,
            MaximumRelativeError: 0)
    ];

    private static Mql5SemanticRunnerAttestation CreateSignedAttestation(
        Mql5SemanticEquivalenceRequest request,
        Mql5SemanticTraceEventEvidence[] events,
        ECDsa signer,
        string referenceTraceSha256,
        string loweredTraceSha256,
        Func<Mql5SemanticRunnerAttestationDescriptor, Mql5SemanticRunnerAttestationDescriptor>? transform = null)
    {
        var descriptor = new Mql5SemanticRunnerAttestationDescriptor(
            Mql5SemanticEquivalenceVerifier.AttestationSchemaVersion,
            request.JobId,
            Mql5SemanticEquivalenceVerifier.ComputeRequestSha256(request),
            request.RelativePath,
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
            request.TolerancePolicyApprovalSha256,
            "isolated-semantic-runner-1",
            "semantic-session-20260822-1",
            request.Toolchain.RunnerImageDigest,
            Now.AddSeconds(-2),
            Now.AddSeconds(-1),
            Mql5IsolatedRunStatus.Completed,
            referenceTraceSha256,
            Mql5SemanticEquivalenceVerifier.ComputeReferenceOutputEventIndexSha256(events),
            loweredTraceSha256,
            Mql5SemanticEquivalenceVerifier.ComputeLoweredOutputEventIndexSha256(events),
            events.Length,
            events.Length,
            events);
        descriptor = transform?.Invoke(descriptor) ?? descriptor;

        byte[] payload = System.Text.Encoding.UTF8.GetBytes(CanonicalJson.Serialize(descriptor));
        byte[] signature = signer.SignData(
            payload,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        CryptographicOperations.ZeroMemory(payload);
        return new Mql5SemanticRunnerAttestation(
            descriptor,
            "ECDSA_P256_SHA256_DER",
            "runner-key-1",
            signature,
            Convert.ToHexString(SHA256.HashData(signature)).ToLowerInvariant(),
            CanonicalJson.Sha256(descriptor));
    }

    private static EcdsaP256Mql5RunnerAttestationVerifier CreateTrust(ECDsa signer) =>
        new(new Dictionary<string, byte[]>
        {
            ["runner-key-1"] = signer.ExportSubjectPublicKeyInfo()
        });

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            this.now = now;
        }

        public override DateTimeOffset GetUtcNow() => now;
    }
}
