using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YO4X.BuildingBlocks;
using YO4X.StrategyGovernance;

namespace YO4X.Domain.Tests;

public sealed class Mql5IsolatedCompileOrchestratorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 13, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("main.mq5;whoami")]
    [InlineData("../main.mq5")]
    [InlineData("main.mq5|powershell")]
    public async Task UnsafePathsNeverReachTheRunner(string unsafePath)
    {
        Mql5CompileJob baseline = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        Mql5CompileJob job = baseline with
        {
            Sources = [new Mql5SourceDocument(unsafePath, baseline.Sources[0].Content)]
        };
        var runner = new RecordingRunner(static (_, _) => throw new InvalidOperationException("Must not run."));

        Mql5CompileEvidence result = await CreateOrchestrator(runner).CompileAsync(job);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("SOURCE_PATH_UNSAFE_FOR_RUNNER", result.ReasonCode);
        Assert.False(result.MetaEditorCompileProven);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task SourceHashDriftNeverReachesTheRunner()
    {
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        job = job with
        {
            Sources = [new Mql5SourceDocument("main.mq5", Encoding.UTF8.GetBytes("void OnTick() { int drift = 1; }"))]
        };
        var runner = new RecordingRunner(static (_, _) => throw new InvalidOperationException("Must not run."));

        Mql5CompileEvidence result = await CreateOrchestrator(runner).CompileAsync(job);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("SOURCE_HASH_DRIFT_DETECTED", result.ReasonCode);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task MissingLocalIncludeIsBlockedBeforeCompilation()
    {
        Mql5CompileJob job = CreateJob(
            "main.mq5",
            "#include \"missing.mqh\"\nvoid OnTick() {}",
            Now.AddMinutes(-1));
        var runner = new RecordingRunner(static (_, _) => throw new InvalidOperationException("Must not run."));

        Mql5CompileEvidence result = await CreateOrchestrator(runner).CompileAsync(job);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("SOURCE_DEPENDENCY_NOT_RESOLVED", result.ReasonCode);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task TamperedStaticManifestCannotHideAMissingInclude()
    {
        Mql5CompileJob job = CreateJob(
            "main.mq5",
            "#include \"missing.mqh\"\nvoid OnTick() {}",
            Now.AddMinutes(-1));
        Mql5SourceManifest tamperedFile = job.StaticManifest.Files[0] with { Includes = [] };
        job = job with { StaticManifest = job.StaticManifest with { Files = [tamperedFile] } };
        var runner = new RecordingRunner(static (_, _) => throw new InvalidOperationException("Must not run."));

        Mql5CompileEvidence result = await CreateOrchestrator(runner).CompileAsync(job);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("STATIC_MANIFEST_CONTENT_DRIFT", result.ReasonCode);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task ForgedAttestationCannotCreateCompileProof()
    {
        using ECDsa trusted = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        byte[] output = CreateSuccessOutput(job);
        var runner = new RecordingRunner((request, _) => Task.FromResult(
            CreateSignedResponse(request, output, attacker, Now.AddSeconds(-2), Now.AddSeconds(-1))));

        Mql5CompileEvidence result = await CreateOrchestrator(runner, trusted).CompileAsync(job);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("RUNNER_ATTESTATION_UNTRUSTED", result.ReasonCode);
        Assert.False(result.MetaEditorCompileProven);
        Assert.Empty(result.Files);
    }

    [Fact]
    public async Task StaleAttestationCannotCreateCompileProof()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-20));
        byte[] output = CreateSuccessOutput(job);
        var runner = new RecordingRunner((request, _) => Task.FromResult(
            CreateSignedResponse(request, output, signer, Now.AddMinutes(-11), Now.AddMinutes(-10))));

        Mql5CompileEvidence result = await CreateOrchestrator(runner, signer).CompileAsync(job);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("RUNNER_ATTESTATION_STALE_OR_TIME_INVALID", result.ReasonCode);
        Assert.False(result.MetaEditorCompileProven);
    }

    [Fact]
    public async Task AttestedToolchainHashDriftCannotCreateCompileProof()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        byte[] output = CreateSuccessOutput(job);
        var runner = new RecordingRunner((request, _) => Task.FromResult(CreateSignedResponse(
            request,
            output,
            signer,
            Now.AddSeconds(-2),
            Now.AddSeconds(-1),
            transformDescriptor: descriptor => descriptor with { MetaEditorSha256 = new string('9', 64) })));

        Mql5CompileEvidence result = await CreateOrchestrator(runner, signer).CompileAsync(job);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("RUNNER_ATTESTATION_BINDING_INVALID", result.ReasonCode);
        Assert.False(result.MetaEditorCompileProven);
    }

    [Fact]
    public async Task HostSideRunnerTimeoutRemainsBlockedWithoutAttestation()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        job = job with { IsolationPolicy = job.IsolationPolicy with { WallClockTimeoutMilliseconds = 1_000 } };
        var runner = new RecordingRunner(static async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        });

        Mql5CompileEvidence result = await CreateOrchestrator(runner, signer).CompileAsync(job);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("ISOLATED_RUNNER_RESPONSE_TIMEOUT", result.ReasonCode);
        Assert.False(result.MetaEditorCompileProven);
    }

    [Fact]
    public async Task SignedRunnerTimeoutIsAttestedFailureNotCompileProof()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        byte[] output = [];
        var runner = new RecordingRunner((request, _) => Task.FromResult(CreateSignedResponse(
            request,
            output,
            signer,
            Now.AddSeconds(-2),
            Now.AddSeconds(-1),
            Mql5IsolatedRunStatus.TimedOut)));

        Mql5CompileEvidence result = await CreateOrchestrator(runner, signer).CompileAsync(job);

        Assert.Equal(Mql5CompileProofState.Failed, result.State);
        Assert.Equal("ISOLATED_COMPILE_TIMED_OUT", result.ReasonCode);
        Assert.NotNull(result.AttestationSha256);
        Assert.False(result.MetaEditorCompileProven);
    }

    [Fact]
    public async Task ExactTrustedAttestationAndRepeatableArtifactsCreateProof()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        byte[] output = CreateSuccessOutput(job);
        var runner = new RecordingRunner((request, _) => Task.FromResult(
            CreateSignedResponse(request, output, signer, Now.AddSeconds(-2), Now.AddSeconds(-1))));

        Mql5CompileEvidence result = await CreateOrchestrator(runner, signer).CompileAsync(job);

        Assert.Equal(Mql5CompileProofState.Proven, result.State);
        Assert.Equal("METAEDITOR_COMPILE_PROVEN_BY_ISOLATED_RUNNER", result.ReasonCode);
        Assert.True(result.MetaEditorCompileProven);
        Assert.NotNull(result.AttestationSha256);
        Mql5FileCompileEvidence file = Assert.Single(result.Files);
        Assert.Equal(file.ArtifactSha256, file.RepeatArtifactSha256);
    }

    [Fact]
    public async Task RepeatArtifactHashDriftFailsClosed()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        byte[] output = CreateSuccessOutput(job, new string('e', 64), new string('f', 64));
        var runner = new RecordingRunner((request, _) => Task.FromResult(
            CreateSignedResponse(request, output, signer, Now.AddSeconds(-2), Now.AddSeconds(-1))));

        Mql5CompileEvidence result = await CreateOrchestrator(runner, signer).CompileAsync(job);

        Assert.Equal(Mql5CompileProofState.Failed, result.State);
        Assert.Equal("COMPILE_ARTIFACT_NONDETERMINISTIC", result.ReasonCode);
        Assert.False(result.MetaEditorCompileProven);
    }

    [Fact]
    public void ParserRejectsUnboundedOrUnexpectedOutputAndOnlyKeepsDiagnosticDigest()
    {
        string sourceSha = new('a', 64);
        string artifactSha = new('b', 64);
        byte[] valid = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            relativePath = "main.mq5",
            sourceSha256 = sourceSha,
            status = "succeeded",
            exitCode = 0,
            artifactSha256 = artifactSha,
            repeatArtifactSha256 = artifactSha,
            diagnostics = new[]
            {
                new { severity = "warning", code = "W100", line = 2, column = 4, message = "sensitive source excerpt" }
            }
        }));

        Mql5FileCompileEvidence result = Assert.Single(Mql5CompilerOutputParser.Parse(valid, 4096, 1));
        Mql5CompilerDiagnosticEvidence diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(64, diagnostic.MessageSha256.Length);
        Assert.DoesNotContain("sensitive", CanonicalJson.Serialize(result), StringComparison.OrdinalIgnoreCase);

        byte[] unexpected = Encoding.UTF8.GetBytes("{\"relativePath\":\"main.mq5\",\"sourceSha256\":\"" + sourceSha
            + "\",\"status\":\"failed\",\"exitCode\":1,\"artifactSha256\":null,"
            + "\"repeatArtifactSha256\":null,\"diagnostics\":[],\"shell\":\"whoami\"}");
        Mql5CompilerOutputException exception = Assert.Throws<Mql5CompilerOutputException>(
            () => Mql5CompilerOutputParser.Parse(unexpected, 4096, 1));
        Assert.Equal("COMPILER_OUTPUT_SHAPE_INVALID", exception.ReasonCode);
    }

    [Fact]
    public void ProofStateTransitionsNeverAllowAnExistingProofToBeDowngraded()
    {
        Assert.True(Mql5CompileProofTransitions.CanTransition(
            Mql5CompileProofState.StaticOnly,
            Mql5CompileProofState.Blocked));
        Assert.True(Mql5CompileProofTransitions.CanTransition(
            Mql5CompileProofState.Failed,
            Mql5CompileProofState.Proven));
        Assert.False(Mql5CompileProofTransitions.CanTransition(
            Mql5CompileProofState.Proven,
            Mql5CompileProofState.Failed));
        Assert.True(Mql5CompileProofTransitions.CanTransition(
            Mql5CompileProofState.Proven,
            Mql5CompileProofState.Proven));
    }

    private static Mql5IsolatedCompileOrchestrator CreateOrchestrator(
        IMql5IsolatedCompileRunner runner,
        ECDsa? trustedSigner = null)
    {
        ECDsa signer = trustedSigner ?? ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] publicKey = signer.ExportSubjectPublicKeyInfo();
        var verifier = new EcdsaP256Mql5RunnerAttestationVerifier(
            new Dictionary<string, byte[]> { ["runner-key-1"] = publicKey });
        if (trustedSigner is null)
        {
            signer.Dispose();
        }

        return new Mql5IsolatedCompileOrchestrator(runner, verifier, new FixedTimeProvider(Now));
    }

    private static Mql5CompileJob CreateJob(string path, string source, DateTimeOffset requestedAt)
    {
        var document = new Mql5SourceDocument(path, Encoding.UTF8.GetBytes(source));
        Mql5CorpusManifest manifest = new Mql5StaticInventoryAnalyzer().Analyze([document]);
        return new Mql5CompileJob(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            requestedAt,
            manifest,
            [document],
            new Mql5PinnedToolchain(
                "sha256:" + new string('c', 64),
                "05718f3fa55f3f59fd2f024d8c433b457fbd58fcf39e947a16ccdad00a614ec7",
                "metaeditor-5-pinned",
                new string('d', 64)),
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

    private static byte[] CreateSuccessOutput(
        Mql5CompileJob job,
        string? artifactSha256 = null,
        string? repeatArtifactSha256 = null)
    {
        string artifact = artifactSha256 ?? new string('e', 64);
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            relativePath = job.StaticManifest.Files[0].RelativePath,
            sourceSha256 = job.StaticManifest.Files[0].Sha256,
            status = "succeeded",
            exitCode = 0,
            artifactSha256 = artifact,
            repeatArtifactSha256 = repeatArtifactSha256 ?? artifact,
            diagnostics = Array.Empty<object>()
        }));
    }

    private static Mql5IsolatedCompileResponse CreateSignedResponse(
        Mql5IsolatedCompileRequest request,
        byte[] output,
        ECDsa signer,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        Mql5IsolatedRunStatus status = Mql5IsolatedRunStatus.Completed,
        Func<Mql5RunnerAttestationDescriptor, Mql5RunnerAttestationDescriptor>? transformDescriptor = null)
    {
        int recordCount = output.Length == 0 ? 0 : Encoding.UTF8.GetString(output).Split('\n').Length;
        var descriptor = new Mql5RunnerAttestationDescriptor(
            "yo4x.mql5-runner-attestation.v1",
            request.JobId,
            request.ChallengeSha256,
            request.CorpusSha256,
            "isolated-runner-1",
            "session-20260822-1",
            request.Toolchain.RunnerImageDigest,
            request.Toolchain.MetaEditorSha256,
            request.Toolchain.MetaEditorVersion,
            request.Toolchain.PlatformLibrarySnapshotSha256,
            request.IsolationPolicy,
            startedAt,
            completedAt,
            status,
            Convert.ToHexString(SHA256.HashData(output)).ToLowerInvariant(),
            recordCount);
        descriptor = transformDescriptor?.Invoke(descriptor) ?? descriptor;

        byte[] payload = Encoding.UTF8.GetBytes(CanonicalJson.Serialize(descriptor));
        byte[] signature = signer.SignData(
            payload,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        CryptographicOperations.ZeroMemory(payload);
        return new Mql5IsolatedCompileResponse(
            new Mql5RunnerAttestation(
                descriptor,
                "ECDSA_P256_SHA256_DER",
                "runner-key-1",
                signature,
                Convert.ToHexString(SHA256.HashData(signature)).ToLowerInvariant(),
                CanonicalJson.Sha256(descriptor)),
            output);
    }

    private sealed class RecordingRunner : IMql5IsolatedCompileRunner
    {
        private readonly Func<Mql5IsolatedCompileRequest, CancellationToken, Task<Mql5IsolatedCompileResponse>> action;

        public RecordingRunner(
            Func<Mql5IsolatedCompileRequest, CancellationToken, Task<Mql5IsolatedCompileResponse>> action)
        {
            this.action = action;
        }

        public int CallCount { get; private set; }

        public Task<Mql5IsolatedCompileResponse> CompileAsync(
            Mql5IsolatedCompileRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return action(request, cancellationToken);
        }
    }

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
