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

        Mql5CompileEvidence result = await CreateOrchestrator(runner).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("SOURCE_PATH_UNSAFE_FOR_RUNNER", result.ReasonCode);
        Assert.False(result.MetaEditorCompileProven);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task ArbitrarySourceCollectionFaultIsNormalizedBeforeRunnerDispatch()
    {
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        job = job with { Sources = new FaultingSourceCollection() };
        var runner = new RecordingRunner(static (_, _) =>
            throw new InvalidOperationException("Must not run."));

        Mql5CompileEvidence result = await CreateOrchestrator(runner).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("SOURCE_CORPUS_INVALID", result.ReasonCode);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task SourceSnapshotUsesBoundedIndexerWithoutEnumeratingCallerCollection()
    {
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        byte[] callerBytes = job.Sources[0].Content;
        string callerHash = Convert.ToHexString(SHA256.HashData(callerBytes));
        var sources = new EnumeratorBombSourceCollection(job.Sources[0]);
        job = job with { Sources = sources };
        var runner = new RecordingRunner(static (_, _) =>
            throw new Mql5RunnerUnavailableException("ISOLATED_RUNNER_NOT_CONFIGURED"));

        Mql5CompileEvidence result = await CreateOrchestrator(runner).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("ISOLATED_RUNNER_NOT_CONFIGURED", result.ReasonCode);
        Assert.Equal(1, runner.CallCount);
        Assert.Equal(1, sources.IndexerAccessCount);
        Assert.Equal(0, sources.EnumeratorAccessCount);
        Assert.Equal(callerHash, Convert.ToHexString(SHA256.HashData(callerBytes)));
    }

    [Fact]
    public async Task OversizedSourceCollectionCountFailsBeforeIndexerEnumerationOrDispatch()
    {
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        var sources = new OversizedCountSourceCollection();
        job = job with { Sources = sources };
        var runner = new RecordingRunner(static (_, _) =>
            throw new InvalidOperationException("Must not run."));

        Mql5CompileEvidence result = await CreateOrchestrator(runner).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("SOURCE_CORPUS_INVALID", result.ReasonCode);
        Assert.Equal(0, runner.CallCount);
        Assert.Equal(0, sources.IndexerAccessCount);
        Assert.Equal(0, sources.EnumeratorAccessCount);
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

        Mql5CompileEvidence result = await CreateOrchestrator(runner).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("SOURCE_HASH_DRIFT_DETECTED", result.ReasonCode);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task OversizedSourceIsRejectedBeforeCloningOrRunnerDispatch()
    {
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        byte[] oversized = new byte[4 * 1024 * 1024 + 1];
        Array.Fill(oversized, (byte)0x5a);
        string beforeSha256 = Convert.ToHexString(SHA256.HashData(oversized));
        job = job with { Sources = [new Mql5SourceDocument("main.mq5", oversized)] };
        var runner = new RecordingRunner(static (_, _) =>
            throw new InvalidOperationException("Must not run."));

        Mql5CompileEvidence result = await CreateOrchestrator(runner).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("SOURCE_SIZE_LIMIT_EXCEEDED", result.ReasonCode);
        Assert.Equal(0, runner.CallCount);
        Assert.Equal(beforeSha256, Convert.ToHexString(SHA256.HashData(oversized)));
    }

    [Fact]
    public async Task AggregateSourceLimitIsCheckedBeforeAnyByteCloneOrRunnerDispatch()
    {
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        byte[] sharedFourMiB = new byte[4 * 1024 * 1024];
        Array.Fill(sharedFourMiB, (byte)0xa5);
        string beforeSha256 = Convert.ToHexString(SHA256.HashData(sharedFourMiB));
        Mql5SourceDocument[] logicalSources = Enumerable.Range(0, 65)
            .Select(index => new Mql5SourceDocument($"logical-{index:D2}.mqh", sharedFourMiB))
            .ToArray();
        job = job with { Sources = logicalSources };
        var runner = new RecordingRunner(static (_, _) =>
            throw new InvalidOperationException("Must not run."));

        Mql5CompileEvidence result = await CreateOrchestrator(runner).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("SOURCE_SIZE_LIMIT_EXCEEDED", result.ReasonCode);
        Assert.Equal(0, runner.CallCount);
        Assert.Equal(beforeSha256, Convert.ToHexString(SHA256.HashData(sharedFourMiB)));
    }

    [Fact]
    public async Task MetadataTextBudgetRejectsBeforeCanonicalSerializationOrDispatch()
    {
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        string oversizedMessage = new('x', 8 * 1024 * 1024 + 1);
        Mql5SourceManifest source = job.StaticManifest.Files[0] with
        {
            Findings =
            [
                new Mql5CompatibilityFinding(
                    "OVERSIZED_METADATA",
                    Mql5FindingSeverity.Warning,
                    Mql5FeatureSupport.ReviewRequired,
                    oversizedMessage,
                    [1])
            ]
        };
        job = job with
        {
            StaticManifest = job.StaticManifest with { Files = [source] }
        };
        var runner = new RecordingRunner(static (_, _) =>
            throw new InvalidOperationException("Must not run."));

        Mql5CompileEvidence result = await CreateOrchestrator(runner).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("COMPILE_JOB_INVALID", result.ReasonCode);
        Assert.Equal(0, runner.CallCount);
        Assert.Same(oversizedMessage, job.StaticManifest.Files[0].Findings[0].Message);
    }

    [Fact]
    public async Task MissingLocalIncludeIsBlockedBeforeCompilation()
    {
        Mql5CompileJob job = CreateJob(
            "main.mq5",
            "#include \"missing.mqh\"\nvoid OnTick() {}",
            Now.AddMinutes(-1));
        var runner = new RecordingRunner(static (_, _) => throw new InvalidOperationException("Must not run."));

        Mql5CompileEvidence result = await CreateOrchestrator(runner).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("COMPILE_PACKAGE_MISSING_DEPENDENCY", result.ReasonCode);
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

        Mql5CompileEvidence result = await CreateOrchestrator(runner).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

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

        Mql5CompileEvidence result = await CreateOrchestrator(runner, trusted).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("RUNNER_ATTESTATION_UNTRUSTED", result.ReasonCode);
        Assert.False(result.MetaEditorCompileProven);
        Assert.Empty(result.Files);
    }

    [Fact]
    public async Task AttestationVerifierFaultIsNormalizedToUntrustedEvidence()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        byte[] output = CreateSuccessOutput(job);
        var runner = new RecordingRunner((request, _) => Task.FromResult(
            CreateSignedResponse(request, output, signer, Now.AddSeconds(-2), Now.AddSeconds(-1))));
        var orchestrator = new Mql5IsolatedCompileOrchestrator(
            runner,
            new FaultingAttestationVerifier(),
            new FixedTimeProvider(Now),
            new Mql5ApprovedCompileProfile(
                "approved-profile-1",
                CreatePinnedToolchain(),
                CreateApprovedPlatformSnapshot(),
                CreateIsolationPolicy(),
                ["runner-key-1"]));

        Mql5CompileEvidence result = await orchestrator.CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("RUNNER_ATTESTATION_UNTRUSTED", result.ReasonCode);
        Assert.False(result.MetaEditorCompileProven);
        Assert.Empty(result.Files);
        Assert.Equal(1, runner.CallCount);
    }

    [Fact]
    public async Task SignedVersionTwoAttestationCannotBeReinterpretedAsVersionThree()
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
            transformDescriptor: descriptor => descriptor with
            {
                SchemaVersion = "yo4x.mql5-runner-attestation.v2"
            })));

        Mql5CompileEvidence result = await CreateOrchestrator(runner, signer).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("RUNNER_ATTESTATION_BINDING_INVALID", result.ReasonCode);
        Assert.False(result.MetaEditorCompileProven);
    }

    [Fact]
    public async Task TrustedButOutOfScopeSigningKeyCannotCreateProof()
    {
        using ECDsa approvedSigner = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa outOfScopeSigner = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        byte[] output = CreateSuccessOutput(job);
        var runner = new RecordingRunner((request, _) => Task.FromResult(CreateSignedResponse(
            request,
            output,
            outOfScopeSigner,
            Now.AddSeconds(-2),
            Now.AddSeconds(-1),
            signingKeyId: "runner-key-2")));
        var verifier = new EcdsaP256Mql5RunnerAttestationVerifier(
            new Dictionary<string, byte[]>
            {
                ["runner-key-1"] = approvedSigner.ExportSubjectPublicKeyInfo(),
                ["runner-key-2"] = outOfScopeSigner.ExportSubjectPublicKeyInfo()
            });
        var orchestrator = new Mql5IsolatedCompileOrchestrator(
            runner,
            verifier,
            new FixedTimeProvider(Now),
            new Mql5ApprovedCompileProfile(
                "approved-profile-1",
                CreatePinnedToolchain(),
                CreateApprovedPlatformSnapshot(),
                CreateIsolationPolicy(),
                ["runner-key-1"]));

        Mql5CompileEvidence result = await orchestrator.CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("RUNNER_ATTESTATION_SIGNER_NOT_APPROVED", result.ReasonCode);
        Assert.False(result.MetaEditorCompileProven);
    }

    [Fact]
    public async Task StaleAttestationCannotCreateCompileProof()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-20));
        byte[] output = CreateSuccessOutput(job);
        var runner = new RecordingRunner((request, _) => Task.FromResult(
            CreateSignedResponse(request, output, signer, Now.AddMinutes(-11), Now.AddMinutes(-10))));

        Mql5CompileEvidence result = await CreateOrchestrator(runner, signer).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

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

        Mql5CompileEvidence result = await CreateOrchestrator(runner, signer).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

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

        Mql5CompileEvidence result = await CreateOrchestrator(runner, signer).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("ISOLATED_RUNNER_RESPONSE_TIMEOUT", result.ReasonCode);
        Assert.False(result.MetaEditorCompileProven);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimedOutRunnerKeepsCapacityClosedUntilActualCompletion(
        bool blocksSynchronously)
    {
        Mql5CompileJob job = CreateJob(
            "main.mq5",
            "void OnTick() {}",
            Now.AddMinutes(-1));
        job = job with
        {
            IsolationPolicy = job.IsolationPolicy with
            {
                WallClockTimeoutMilliseconds = 1_000
            }
        };
        using var synchronousRelease = new ManualResetEventSlim(initialState: false);
        var asynchronousRelease = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        IReadOnlyList<Mql5SourceDocument>? capturedSources = null;
        var response = new Mql5IsolatedCompileResponse(null, []);
        var runner = new RecordingRunner((request, _) =>
        {
            capturedSources = request.Sources;
            entered.TrySetResult(true);
            if (blocksSynchronously)
            {
                synchronousRelease.Wait(CancellationToken.None);
                return Task.FromResult(response);
            }

            return CompleteAsynchronously();
        });
        Mql5IsolatedCompileOrchestrator orchestrator = CreateOrchestrator(runner);

        try
        {
            Task<Mql5CompileEvidence> pending = orchestrator.CompileAsync(
                job,
                TestContext.Current.CancellationToken);
            await entered.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            Mql5CompileEvidence timedOut = await pending;

            Mql5CompileEvidence rejectedRetry = await orchestrator.CompileAsync(
                job,
                TestContext.Current.CancellationToken);

            Assert.Equal("ISOLATED_RUNNER_RESPONSE_TIMEOUT", timedOut.ReasonCode);
            Assert.Equal(Mql5CompileProofState.Blocked, rejectedRetry.State);
            Assert.Equal("ISOLATED_RUNNER_CAPACITY_EXHAUSTED", rejectedRetry.ReasonCode);
            Assert.Equal(1, runner.CallCount);
        }
        finally
        {
            synchronousRelease.Set();
            asynchronousRelease.TrySetResult(true);
        }

        Assert.NotNull(capturedSources);
        await WaitUntilSourcesAreZeroedAsync(capturedSources!);

        async Task<Mql5IsolatedCompileResponse> CompleteAsynchronously()
        {
            await asynchronousRelease.Task;
            return response;
        }
    }

    [Fact]
    public async Task SynchronousRunnerFaultIsNormalizedWithoutLeakingExceptionText()
    {
        const string sensitiveMessage = "private source and host details must not leak";
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        var runner = new RecordingRunner(static (_, _) =>
            throw new InvalidOperationException(sensitiveMessage));

        Mql5CompileEvidence result = await CreateOrchestrator(runner).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("ISOLATED_RUNNER_FAILED", result.ReasonCode);
        Assert.DoesNotContain(sensitiveMessage, CanonicalJson.Serialize(result), StringComparison.Ordinal);
        Assert.False(result.MetaEditorCompileProven);
    }

    [Fact]
    public async Task AsynchronousRunnerFaultIsNormalizedWithoutCompileProof()
    {
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        var runner = new RecordingRunner(static async (_, _) =>
        {
            await Task.Yield();
            throw new InvalidOperationException("late provider fault");
        });

        Mql5CompileEvidence result = await CreateOrchestrator(runner).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("ISOLATED_RUNNER_FAILED", result.ReasonCode);
        Assert.False(result.MetaEditorCompileProven);
    }

    [Fact]
    public async Task IgnoringCancellationLateSuccessCannotPromoteAfterOuterDeadline()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        job = job with { IsolationPolicy = job.IsolationPolicy with { WallClockTimeoutMilliseconds = 1_000 } };
        byte[] output = CreateSuccessOutput(job);
        var started = new TaskCompletionSource<Mql5IsolatedCompileRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new RecordingRunner((request, _) =>
        {
            try
            {
                started.SetResult(request);
                release.Task.GetAwaiter().GetResult();
                return Task.FromResult(CreateSignedResponse(
                    request,
                    output,
                    signer,
                    Now.AddSeconds(-2),
                    Now.AddSeconds(-1)));
            }
            finally
            {
                finished.SetResult(true);
            }
        });

        Task<Mql5CompileEvidence> pending = CreateOrchestrator(runner, signer).CompileAsync(
            job,
            TestContext.Current.CancellationToken);
        Mql5IsolatedCompileRequest capturedRequest = await started.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Mql5CompileEvidence result = await pending.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("ISOLATED_RUNNER_RESPONSE_TIMEOUT", result.ReasonCode);
        Assert.False(result.MetaEditorCompileProven);
        Assert.False(release.Task.IsCompleted);
        Assert.Contains(
            capturedRequest.Sources.SelectMany(static source => source.Content),
            static value => value != 0);

        release.SetResult(true);
        await finished.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await WaitUntilSourcesAreZeroedAsync(capturedRequest.Sources);
        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.False(result.MetaEditorCompileProven);
    }

    [Fact]
    public async Task AttestedDurationBeyondIsolationPolicyCannotCreateCompileProof()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        job = job with { IsolationPolicy = job.IsolationPolicy with { WallClockTimeoutMilliseconds = 1_000 } };
        byte[] output = CreateSuccessOutput(job);
        var runner = new RecordingRunner((request, _) => Task.FromResult(CreateSignedResponse(
            request,
            output,
            signer,
            Now.AddSeconds(-5),
            Now.AddSeconds(-1))));

        Mql5CompileEvidence result = await CreateOrchestrator(runner, signer).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("RUNNER_ATTESTATION_STALE_OR_TIME_INVALID", result.ReasonCode);
        Assert.False(result.MetaEditorCompileProven);
    }

    [Fact]
    public async Task CallerCancellationPropagatesWhenRunnerIgnoresCancellation()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var callerCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        var completion = new TaskCompletionSource<Mql5IsolatedCompileResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<Mql5IsolatedCompileRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new RecordingRunner((request, _) =>
        {
            started.SetResult(request);
            return completion.Task;
        });

        Task<Mql5CompileEvidence> pending = CreateOrchestrator(runner, signer).CompileAsync(
            job,
            callerCancellation.Token);
        Mql5IsolatedCompileRequest capturedRequest = await started.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        callerCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        Assert.Contains(
            capturedRequest.Sources.SelectMany(static source => source.Content),
            static value => value != 0);
        completion.TrySetCanceled(TestContext.Current.CancellationToken);
        await WaitUntilSourcesAreZeroedAsync(capturedRequest.Sources);
    }

    [Fact]
    public async Task CallerSourceMutationCannotChangeValidatedDispatchSnapshot()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        byte[] callerOwnedSource = job.Sources[0].Content;
        byte[] output = CreateSuccessOutput(job);
        string expectedSourceSha256 = job.StaticManifest.Files[0].Sha256;
        Mql5IsolatedCompileRequest? capturedRequest = null;
        var runner = new RecordingRunner((request, _) =>
        {
            capturedRequest = request;
            Assert.NotSame(callerOwnedSource, request.Sources[0].Content);
            Array.Fill(callerOwnedSource, (byte)0x5a);
            Assert.Equal(
                expectedSourceSha256,
                Convert.ToHexString(SHA256.HashData(request.Sources[0].Content)).ToLowerInvariant());
            return Task.FromResult(CreateSignedResponse(
                request,
                output,
                signer,
                Now.AddSeconds(-2),
                Now.AddSeconds(-1)));
        });

        Mql5CompileEvidence result = await CreateOrchestrator(runner, signer).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.True(result.MetaEditorCompileProven);
        Assert.All(callerOwnedSource, static value => Assert.Equal((byte)0x5a, value));
        Assert.NotNull(capturedRequest);
        Assert.All(
            capturedRequest.Sources.SelectMany(static source => source.Content),
            static value => Assert.Equal((byte)0, value));
    }

    [Fact]
    public async Task RunnerReceivesOnlyTheExactOrderedTargetClosure()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Mql5CompileJob job = CreateJob(
            "main.mq5",
            Now.AddMinutes(-1),
            ("main.mq5", "#include \"lib/a.mqh\"\nvoid OnTick() { A(); }"),
            ("lib/a.mqh", "#include \"b.mqh\"\nvoid A() { B(); }"),
            ("lib/b.mqh", "void B() {}"),
            ("unrelated.mqh", "void MustNotReachRunner() {}"));
        byte[] output = CreateSuccessOutput(job);
        var runner = new RecordingRunner((request, _) =>
        {
            Assert.Equal(
                ["lib/b.mqh", "lib/a.mqh", "main.mq5"],
                request.Sources.Select(static source => source.RelativePath));
            Assert.DoesNotContain(
                request.Sources,
                static source => source.RelativePath == "unrelated.mqh");
            Assert.Equal(request.CompilePackage.OrderedSources.Count, request.Sources.Count);
            Assert.Equal(job.CompilePackage.PackageSha256, request.CompilePackageSha256);
            Assert.Equal(job.CompilePackage.SourceClosureSha256, request.SourceClosureSha256);
            Assert.Equal(
                CanonicalJson.Sha256(job.CompilePackage),
                CanonicalJson.Sha256(request.CompilePackage));
            return Task.FromResult(CreateSignedResponse(
                request,
                output,
                signer,
                Now.AddSeconds(-2),
                Now.AddSeconds(-1)));
        });

        Mql5CompileEvidence result = await CreateOrchestrator(runner, signer).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.True(result.MetaEditorCompileProven);
        Assert.Equal(job.CompilePackage.PackageSha256, result.CompilePackageSha256);
        Assert.Equal(job.CompilePackage.SourceClosureSha256, result.SourceClosureSha256);
        Assert.Equal(job.CompilePackage.TargetRelativePath, result.TargetRelativePath);
    }

    [Fact]
    public async Task AttestedCompilePackageDigestDriftCannotCreateProof()
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
            transformDescriptor: descriptor => descriptor with
            {
                CompilePackageSha256 = new string('9', 64)
            })));

        Mql5CompileEvidence result = await CreateOrchestrator(runner, signer).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("RUNNER_ATTESTATION_BINDING_INVALID", result.ReasonCode);
        Assert.False(result.MetaEditorCompileProven);
    }

    [Fact]
    public async Task TamperedCompilePackageNeverReachesRunner()
    {
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        job = job with
        {
            CompilePackage = job.CompilePackage with { PackageSha256 = new string('9', 64) }
        };
        var runner = new RecordingRunner(static (_, _) =>
            throw new InvalidOperationException("Must not run."));

        Mql5CompileEvidence result = await CreateOrchestrator(runner).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("COMPILE_PACKAGE_CONTENT_DRIFT", result.ReasonCode);
        Assert.False(result.MetaEditorCompileProven);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task RunnerMutationOfRequestDossierFailsClosedBeforeAttestationAcceptance()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        byte[] output = CreateSuccessOutput(job);
        Mql5IsolatedCompileResponse? response = null;
        var runner = new RecordingRunner((request, _) =>
        {
            Mql5CompilePackageSource[] mutableSources =
                Assert.IsType<Mql5CompilePackageSource[]>(request.CompilePackage.OrderedSources);
            mutableSources[0] = mutableSources[0] with { SourceSha256 = new string('9', 64) };
            response = CreateSignedResponse(
                request,
                output,
                signer,
                Now.AddSeconds(-2),
                Now.AddSeconds(-1));
            return Task.FromResult(response);
        });

        Mql5CompileEvidence result = await CreateOrchestrator(runner, signer).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("ISOLATED_RUNNER_REQUEST_MUTATED", result.ReasonCode);
        Assert.False(result.MetaEditorCompileProven);
        Assert.NotNull(response);
        Assert.All(
            response.CopyCompilerOutput(job.IsolationPolicy.CompilerOutputLimitBytes),
            static value => Assert.Equal((byte)0, value));
    }

    [Fact]
    public async Task CallerMutationOfOriginalDossierAfterDispatchCannotRedirectProof()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        string expectedPackageSha256 = job.CompilePackage.PackageSha256;
        string expectedTarget = job.CompilePackage.TargetRelativePath;
        byte[] output = CreateSuccessOutput(job);
        Mql5CompilePackageSource[] callerDossierSources =
            Assert.IsType<Mql5CompilePackageSource[]>(job.CompilePackage.OrderedSources);
        var runner = new RecordingRunner((request, _) =>
        {
            callerDossierSources[0] = callerDossierSources[0] with
            {
                SourceSha256 = new string('9', 64)
            };
            Assert.NotEqual(
                CanonicalJson.Sha256(job.CompilePackage),
                CanonicalJson.Sha256(request.CompilePackage));
            return Task.FromResult(CreateSignedResponse(
                request,
                output,
                signer,
                Now.AddSeconds(-2),
                Now.AddSeconds(-1)));
        });

        Mql5CompileEvidence result = await CreateOrchestrator(runner, signer).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Proven, result.State);
        Assert.Equal(expectedPackageSha256, result.CompilePackageSha256);
        Assert.Equal(expectedTarget, result.TargetRelativePath);
    }

    [Fact]
    public async Task CallerMutationAndRestoreDuringRunnerCannotInfluenceOwnedEvidence()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        byte[] output = CreateSuccessOutput(job);
        IList<Mql5SourceManifest> staticFiles =
            Assert.IsAssignableFrom<IList<Mql5SourceManifest>>(job.StaticManifest.Files);
        IList<Mql5ConversionFileEvidence> conversionFiles =
            Assert.IsAssignableFrom<IList<Mql5ConversionFileEvidence>>(job.ConversionEvidence.Files);
        IList<Mql5CompilePackageSource> dossierSources =
            Assert.IsAssignableFrom<IList<Mql5CompilePackageSource>>(
                job.CompilePackage.OrderedSources);
        var dispatched = new TaskCompletionSource<Mql5IsolatedCompileRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRunner = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new RecordingRunner(async (request, cancellationToken) =>
        {
            dispatched.TrySetResult(request);
            await releaseRunner.Task.WaitAsync(cancellationToken);
            return CreateSignedResponse(
                request,
                output,
                signer,
                Now.AddSeconds(-2),
                Now.AddSeconds(-1));
        });

        Task<Mql5CompileEvidence> pending = CreateOrchestrator(runner, signer).CompileAsync(
            job,
            TestContext.Current.CancellationToken);
        Mql5IsolatedCompileRequest ownedRequest = await dispatched.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        Mql5SourceManifest originalStatic = staticFiles[0];
        Mql5ConversionFileEvidence originalConversion = conversionFiles[0];
        Mql5CompilePackageSource originalDossierSource = dossierSources[0];
        staticFiles[0] = originalStatic with { Entrypoints = [] };
        conversionFiles[0] = originalConversion with { EvidenceSha256 = new string('9', 64) };
        dossierSources[0] = originalDossierSource with { SourceSha256 = new string('8', 64) };
        Assert.NotEqual(
            CanonicalJson.Sha256(job.CompilePackage),
            CanonicalJson.Sha256(ownedRequest.CompilePackage));
        Assert.Equal(originalDossierSource.SourceSha256, ownedRequest.CompilePackage.TargetSourceSha256);
        staticFiles[0] = originalStatic;
        conversionFiles[0] = originalConversion;
        dossierSources[0] = originalDossierSource;
        releaseRunner.TrySetResult(true);

        Mql5CompileEvidence result = await pending;

        Assert.Equal(Mql5CompileProofState.Proven, result.State);
        Assert.Equal(job.CompilePackage.PackageSha256, result.CompilePackageSha256);
        Assert.True(result.MetaEditorCompileProven);
    }

    [Fact]
    public async Task OversizedRunnerOutputIsRejectedBeforeASecondOutputClone()
    {
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        byte[] oversizedOutput = new byte[job.IsolationPolicy.CompilerOutputLimitBytes + 1];
        var runner = new RecordingRunner((_, _) => Task.FromResult(
            new Mql5IsolatedCompileResponse(null, oversizedOutput)));

        Mql5CompileEvidence result = await CreateOrchestrator(runner).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("COMPILER_OUTPUT_LIMIT_EXCEEDED", result.ReasonCode);
        Assert.False(result.MetaEditorCompileProven);
    }

    [Fact]
    public async Task AbsoluteCompilerOutputLimitRejectsBeforeResponseCloneAndNormalizesRunnerFault()
    {
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        byte[] oversizedOutput = new byte[16 * 1024 * 1024 + 1];
        Array.Fill(oversizedOutput, (byte)0x5a);
        string callerHash = Convert.ToHexString(SHA256.HashData(oversizedOutput));
        var runner = new RecordingRunner((_, _) => Task.FromResult(
            new Mql5IsolatedCompileResponse(null, oversizedOutput)));

        Mql5CompileEvidence result = await CreateOrchestrator(runner).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("ISOLATED_RUNNER_FAILED", result.ReasonCode);
        Assert.Equal(1, runner.CallCount);
        Assert.Equal(callerHash, Convert.ToHexString(SHA256.HashData(oversizedOutput)));
    }

    [Fact]
    public async Task AbsoluteSignatureLimitRejectsBeforeAttestationCloneAndNormalizesRunnerFault()
    {
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        byte[] oversizedSignature = new byte[257];
        Array.Fill(oversizedSignature, (byte)0xa5);
        string callerHash = Convert.ToHexString(SHA256.HashData(oversizedSignature));
        var runner = new RecordingRunner((_, _) => Task.FromResult(
            new Mql5IsolatedCompileResponse(
                new Mql5RunnerAttestation(
                    descriptor: null,
                    algorithm: "ECDSA_P256_SHA256_DER",
                    signingKeyId: "runner-key-1",
                    signature: oversizedSignature,
                    signatureSha256: new string('a', 64),
                    signedPayloadSha256: new string('b', 64)),
                compilerOutput: [])));

        Mql5CompileEvidence result = await CreateOrchestrator(runner).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("ISOLATED_RUNNER_FAILED", result.ReasonCode);
        Assert.Equal(1, runner.CallCount);
        Assert.Equal(callerHash, Convert.ToHexString(SHA256.HashData(oversizedSignature)));
    }

    [Fact]
    public async Task UnconfiguredBackendApprovalProfileBlocksBeforeRunnerCall()
    {
        Mql5CompileJob baseline = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        Mql5CompileJob job = baseline with { Sources = new FaultingSourceCollection() };
        var runner = new RecordingRunner(static (_, _) =>
            throw new InvalidOperationException("Must not run."));

        Mql5CompileEvidence result = await CreateOrchestrator(
                runner,
                configureProfile: false)
            .CompileAsync(job, TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("COMPILE_PROFILE_NOT_CONFIGURED", result.ReasonCode);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task CallerSelectedExactFormatToolchainIsNotBackendApproval()
    {
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        Mql5PinnedToolchain unapproved = job.Toolchain with
        {
            PlatformLibrarySnapshotSha256 = new string('f', 64)
        };
        var callerFabricatedApproval = new Mql5ApprovedPlatformLibrarySnapshot(
            "caller-fabricated-snapshot",
            unapproved.PlatformLibrarySnapshotSha256,
            new string('b', 64));
        Mql5TargetCompilePackageDossier reboundPackage = Assert.Single(
            Mql5CompilePackageDossierPlanner.Plan(
                job.StaticManifest,
                job.ConversionEvidence,
                job.Sources,
                callerFabricatedApproval).Targets);
        job = job with
        {
            Toolchain = unapproved,
            CompilePackage = reboundPackage,
            Sources = new FaultingSourceCollection()
        };
        var runner = new RecordingRunner(static (_, _) =>
            throw new InvalidOperationException("Must not run."));

        Mql5CompileEvidence result = await CreateOrchestrator(runner).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("COMPILE_TOOLCHAIN_NOT_APPROVED", result.ReasonCode);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task IsolationResourcesBeyondBackendMaximumNeverReachRunner()
    {
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        job = job with
        {
            IsolationPolicy = job.IsolationPolicy with { MemoryLimitBytes = 1024L * 1024 * 1024 },
            Sources = new FaultingSourceCollection()
        };
        var runner = new RecordingRunner(static (_, _) =>
            throw new InvalidOperationException("Must not run."));

        Mql5CompileEvidence result = await CreateOrchestrator(runner).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("COMPILE_ISOLATION_POLICY_NOT_APPROVED", result.ReasonCode);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task UnavailableDefaultRemainsBlockedAndDoesNotAlterCallerSource()
    {
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        byte[] expectedSource = job.Sources[0].Content.ToArray();

        Mql5CompileEvidence result = await CreateOrchestrator(
                new UnavailableMql5IsolatedCompileRunner())
            .CompileAsync(job, TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Blocked, result.State);
        Assert.Equal("ISOLATED_RUNNER_NOT_CONFIGURED", result.ReasonCode);
        Assert.False(result.MetaEditorCompileProven);
        Assert.Equal(expectedSource, job.Sources[0].Content);
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

        Mql5CompileEvidence result = await CreateOrchestrator(runner, signer).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

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

        Mql5CompileEvidence result = await CreateOrchestrator(runner, signer).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Proven, result.State);
        Assert.Equal("METAEDITOR_COMPILE_PROVEN_BY_ISOLATED_RUNNER", result.ReasonCode);
        Assert.True(result.MetaEditorCompileProven);
        Assert.NotNull(result.AttestationSha256);
        Assert.Equal("approved-profile-1", result.CompileProfileId);
        Assert.Matches("^[0-9a-f]{64}$", result.CompileProfileSha256);
        Assert.Equal(job.Toolchain.MetaEditorVersion, result.MetaEditorVersion);
        Assert.Equal(
            job.Toolchain.PlatformLibrarySnapshotSha256,
            result.PlatformLibrarySnapshotSha256);
        Assert.Equal(CanonicalJson.Sha256(job.Toolchain), result.ToolchainSha256);
        Assert.Equal(CanonicalJson.Sha256(job.IsolationPolicy), result.IsolationPolicySha256);
        Mql5FileCompileEvidence file = Assert.Single(result.Files);
        Assert.Equal(file.ArtifactSha256, file.RepeatArtifactSha256);
    }

    [Fact]
    public async Task SignedSuccessRecordWithErrorDiagnosticCannotCreateProof()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        string artifact = new('e', 64);
        byte[] contradictoryOutput = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            relativePath = job.CompilePackage.TargetRelativePath,
            sourceSha256 = job.CompilePackage.TargetSourceSha256,
            status = "succeeded",
            exitCode = 0,
            artifactSha256 = artifact,
            repeatArtifactSha256 = artifact,
            diagnostics = new[]
            {
                new
                {
                    severity = "error",
                    code = "E100",
                    line = 1,
                    column = 1,
                    message = "compiler reported an error"
                }
            }
        }));
        var runner = new RecordingRunner((request, _) => Task.FromResult(
            CreateSignedResponse(
                request,
                contradictoryOutput,
                signer,
                Now.AddSeconds(-2),
                Now.AddSeconds(-1))));

        Mql5CompileEvidence result = await CreateOrchestrator(runner, signer).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

        Assert.Equal(Mql5CompileProofState.Failed, result.State);
        Assert.Equal("COMPILER_OUTPUT_SUCCESS_EVIDENCE_INVALID", result.ReasonCode);
        Assert.NotNull(result.AttestationSha256);
        Assert.False(result.MetaEditorCompileProven);
    }

    [Fact]
    public async Task RepeatArtifactHashDriftFailsClosed()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Mql5CompileJob job = CreateJob("main.mq5", "void OnTick() {}", Now.AddMinutes(-1));
        byte[] output = CreateSuccessOutput(job, new string('e', 64), new string('f', 64));
        var runner = new RecordingRunner((request, _) => Task.FromResult(
            CreateSignedResponse(request, output, signer, Now.AddSeconds(-2), Now.AddSeconds(-1))));

        Mql5CompileEvidence result = await CreateOrchestrator(runner, signer).CompileAsync(
            job,
            TestContext.Current.CancellationToken);

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
    public void ParserRejectsMaximumSizeNewlineStormBeforeMaterializingRecords()
    {
        byte[] newlineStorm = new byte[16 * 1024 * 1024];
        Array.Fill(newlineStorm, (byte)'\n');

        Mql5CompilerOutputException exception = Assert.Throws<Mql5CompilerOutputException>(
            () => Mql5CompilerOutputParser.Parse(
                newlineStorm,
                newlineStorm.Length,
                maximumRecords: 1));

        Assert.Equal("COMPILER_OUTPUT_RECORD_LIMIT_EXCEEDED", exception.ReasonCode);
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
        ECDsa? trustedSigner = null,
        bool configureProfile = true)
    {
        ECDsa signer = trustedSigner ?? ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] publicKey = signer.ExportSubjectPublicKeyInfo();
        var verifier = new EcdsaP256Mql5RunnerAttestationVerifier(
            new Dictionary<string, byte[]> { ["runner-key-1"] = publicKey });
        if (trustedSigner is null)
        {
            signer.Dispose();
        }

        return new Mql5IsolatedCompileOrchestrator(
            runner,
            verifier,
            new FixedTimeProvider(Now),
            configureProfile
                ? new Mql5ApprovedCompileProfile(
                    "approved-profile-1",
                    CreatePinnedToolchain(),
                    CreateApprovedPlatformSnapshot(),
                    CreateIsolationPolicy(),
                    ["runner-key-1"])
                : null);
    }

    private static Mql5CompileJob CreateJob(string path, string source, DateTimeOffset requestedAt)
        => CreateJob(path, requestedAt, (path, source));

    private static Mql5CompileJob CreateJob(
        string targetPath,
        DateTimeOffset requestedAt,
        params (string Path, string Source)[] sources)
    {
        Mql5SourceDocument[] documents = sources.Select(static source =>
                new Mql5SourceDocument(source.Path, Encoding.UTF8.GetBytes(source.Source)))
            .ToArray();
        Mql5CorpusManifest manifest = new Mql5StaticInventoryAnalyzer().Analyze(documents);
        Mql5ConversionCorpusEvidence conversion = new Mql5ConversionEvidenceAnalyzer().Analyze(documents);
        Mql5PinnedToolchain toolchain = CreatePinnedToolchain();
        Mql5TargetCompilePackageDossier package = Assert.Single(
            Mql5CompilePackageDossierPlanner.Plan(
                manifest,
                conversion,
                documents,
                CreateApprovedPlatformSnapshot()).Targets,
            target => target.TargetRelativePath == targetPath);
        Dictionary<string, Mql5SourceDocument> sourceByPath = documents.ToDictionary(
            static source => source.RelativePath,
            StringComparer.OrdinalIgnoreCase);
        Mql5SourceDocument[] closureSources = package.OrderedSources
            .Select(source => sourceByPath[source.RelativePath])
            .ToArray();
        return new Mql5CompileJob(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            requestedAt,
            manifest,
            conversion,
            closureSources,
            package,
            toolchain,
            CreateIsolationPolicy());
    }

    private static byte[] CreateSuccessOutput(
        Mql5CompileJob job,
        string? artifactSha256 = null,
        string? repeatArtifactSha256 = null)
    {
        string artifact = artifactSha256 ?? new string('e', 64);
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            relativePath = job.CompilePackage.TargetRelativePath,
            sourceSha256 = job.CompilePackage.TargetSourceSha256,
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
        Func<Mql5RunnerAttestationDescriptor, Mql5RunnerAttestationDescriptor>? transformDescriptor = null,
        string signingKeyId = "runner-key-1")
    {
        int recordCount = output.Length == 0 ? 0 : Encoding.UTF8.GetString(output).Split('\n').Length;
        var descriptor = new Mql5RunnerAttestationDescriptor(
            "yo4x.mql5-runner-attestation.v3",
            request.JobId,
            request.ChallengeSha256,
            request.CompileProfileId,
            request.CompileProfileSha256,
            request.CorpusSha256,
            request.StaticManifestSha256,
            request.ConversionEvidenceSha256,
            request.ConversionEvidenceContentSha256,
            request.DependencyGraphSha256,
            request.CompilePackageSha256,
            request.SourceClosureSha256,
            request.TargetRelativePath,
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
                signingKeyId,
                signature,
                Convert.ToHexString(SHA256.HashData(signature)).ToLowerInvariant(),
                CanonicalJson.Sha256(descriptor)),
            output);
    }

    private static Mql5PinnedToolchain CreatePinnedToolchain() => new(
        "sha256:" + new string('c', 64),
        "05718f3fa55f3f59fd2f024d8c433b457fbd58fcf39e947a16ccdad00a614ec7",
        "metaeditor-5-pinned",
        new string('d', 64));

    private static Mql5ApprovedPlatformLibrarySnapshot CreateApprovedPlatformSnapshot() => new(
        "approved-platform-snapshot-1",
        new string('d', 64),
        new string('a', 64));

    private static Mql5IsolationPolicy CreateIsolationPolicy() => new(
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
        CompilerOutputLimitBytes: 1024 * 1024);

    private static async Task WaitUntilSourcesAreZeroedAsync(
        IReadOnlyList<Mql5SourceDocument> sources)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (sources.SelectMany(static source => source.Content).Any(static value => value != 0))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
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

    private sealed class FaultingAttestationVerifier : IMql5RunnerAttestationVerifier
    {
        public bool Verify(
            string signingKeyId,
            string algorithm,
            ReadOnlySpan<byte> signature,
            string canonicalPayload) => throw new ObjectDisposedException("test-verifier");
    }

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
            throw new IOException("The caller source enumerator must not be used.");
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
                throw new IOException("The oversized collection indexer must not be used.");
            }
        }

        public IEnumerator<Mql5SourceDocument> GetEnumerator()
        {
            EnumeratorAccessCount++;
            throw new IOException("The oversized collection enumerator must not be used.");
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

    private sealed class FaultingSourceCollection : IReadOnlyList<Mql5SourceDocument>
    {
        public int Count => 1;

        public Mql5SourceDocument this[int index] =>
            throw new IOException("Hostile source collection indexer fault.");

        public IEnumerator<Mql5SourceDocument> GetEnumerator() =>
            throw new IOException("Hostile source collection enumeration fault.");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
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
