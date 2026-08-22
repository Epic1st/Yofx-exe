using System.Security.Cryptography;
using YO4X.BuildingBlocks;

namespace YO4X.StrategyGovernance;

public sealed class Mql5IsolatedCompileOrchestrator
{
    private static readonly TimeSpan MaximumAttestationAge = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaximumClockSkew = TimeSpan.FromSeconds(30);

    private readonly IMql5IsolatedCompileRunner runner;
    private readonly IMql5RunnerAttestationVerifier attestationVerifier;
    private readonly TimeProvider timeProvider;

    public Mql5IsolatedCompileOrchestrator(
        IMql5IsolatedCompileRunner runner,
        IMql5RunnerAttestationVerifier attestationVerifier,
        TimeProvider? timeProvider = null)
    {
        this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
        this.attestationVerifier = attestationVerifier
            ?? throw new ArgumentNullException(nameof(attestationVerifier));
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<Mql5CompileEvidence> CompileAsync(
        Mql5CompileJob job,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        string? preflightFailure = ValidatePreflight(job, out Mql5SourceManifest[] compileTargets);
        if (preflightFailure is not null)
        {
            return CreateLocalEvidence(job, Mql5CompileProofState.Blocked, preflightFailure);
        }

        if (compileTargets.Length == 0)
        {
            return CreateLocalEvidence(job, Mql5CompileProofState.Unsupported, "NO_MQ5_COMPILE_TARGETS");
        }

        byte[] challenge = RandomNumberGenerator.GetBytes(32);
        string challengeSha256 = Convert.ToHexString(SHA256.HashData(challenge)).ToLowerInvariant();
        CryptographicOperations.ZeroMemory(challenge);

        Mql5SourceDocument[] runnerSources = job.Sources
            .Select(static source => new Mql5SourceDocument(source.RelativePath, source.Content.ToArray()))
            .ToArray();
        var request = new Mql5IsolatedCompileRequest(
            job.JobId,
            job.RequestedAtUtc,
            challengeSha256,
            job.StaticManifest.CorpusSha256,
            runnerSources,
            compileTargets.Select(static target => target.RelativePath).ToArray(),
            job.Toolchain,
            job.IsolationPolicy);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(job.IsolationPolicy.WallClockTimeoutMilliseconds));

        Mql5IsolatedCompileResponse response;
        try
        {
            response = await runner.CompileAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (Mql5RunnerUnavailableException exception)
        {
            return CreateLocalEvidence(job, Mql5CompileProofState.Blocked, exception.ReasonCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CreateLocalEvidence(job, Mql5CompileProofState.Blocked, "ISOLATED_RUNNER_RESPONSE_TIMEOUT");
        }
        finally
        {
            foreach (Mql5SourceDocument source in runnerSources)
            {
                CryptographicOperations.ZeroMemory(source.Content);
            }
        }

        return EvaluateResponse(job, compileTargets, challengeSha256, response);
    }

    private Mql5CompileEvidence EvaluateResponse(
        Mql5CompileJob job,
        Mql5SourceManifest[] compileTargets,
        string challengeSha256,
        Mql5IsolatedCompileResponse response)
    {
        if (response is null || response.Attestation is null)
        {
            return CreateLocalEvidence(job, Mql5CompileProofState.Blocked, "RUNNER_ATTESTATION_MISSING");
        }

        byte[] output = response.GetCompilerOutput();
        try
        {
            string outputSha256 = Convert.ToHexString(SHA256.HashData(output)).ToLowerInvariant();
            AttestationValidation validation = ValidateAttestation(
                job,
                challengeSha256,
                outputSha256,
                response.Attestation);
            if (!validation.Valid)
            {
                return CreateLocalEvidence(job, Mql5CompileProofState.Blocked, validation.ReasonCode);
            }

            Mql5RunnerAttestationDescriptor descriptor = response.Attestation.Descriptor!;
            IReadOnlyList<Mql5FileCompileEvidence> files;
            try
            {
                files = Mql5CompilerOutputParser.Parse(
                    output,
                    job.IsolationPolicy.CompilerOutputLimitBytes,
                    compileTargets.Length);
            }
            catch (Mql5CompilerOutputException exception)
            {
                return CreateAttestedEvidence(
                    job,
                    descriptor,
                    validation.AttestationSha256,
                    validation.SigningKeyId,
                    Mql5CompileProofState.Failed,
                    exception.ReasonCode,
                    []);
            }

            if (files.Count != descriptor.OutputRecordCount)
            {
                return CreateAttestedEvidence(
                    job,
                    descriptor,
                    validation.AttestationSha256,
                    validation.SigningKeyId,
                    Mql5CompileProofState.Failed,
                    "ATTESTED_OUTPUT_COUNT_MISMATCH",
                    files);
            }

            string? bindingFailure = ValidateFileBindings(compileTargets, files, descriptor.RunStatus);
            if (bindingFailure is not null)
            {
                return CreateAttestedEvidence(
                    job,
                    descriptor,
                    validation.AttestationSha256,
                    validation.SigningKeyId,
                    Mql5CompileProofState.Failed,
                    bindingFailure,
                    files);
            }

            (Mql5CompileProofState State, string ReasonCode) outcome = DetermineOutcome(descriptor.RunStatus, files);
            return CreateAttestedEvidence(
                job,
                descriptor,
                validation.AttestationSha256,
                validation.SigningKeyId,
                outcome.State,
                outcome.ReasonCode,
                files);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(output);
        }
    }

    private AttestationValidation ValidateAttestation(
        Mql5CompileJob job,
        string challengeSha256,
        string outputSha256,
        Mql5RunnerAttestation attestation)
    {
        Mql5RunnerAttestationDescriptor? descriptor = attestation.Descriptor;
        if (descriptor is null
            || !string.Equals(
                descriptor.SchemaVersion,
                Mql5CompileValidation.AttestationSchemaVersion,
                StringComparison.Ordinal)
            || descriptor.JobId != job.JobId
            || !Mql5CompileValidation.FixedTimeHexEquals(descriptor.ChallengeSha256, challengeSha256)
            || !Mql5CompileValidation.FixedTimeHexEquals(descriptor.CorpusSha256, job.StaticManifest.CorpusSha256)
            || !Mql5CompileValidation.IsSafeToken(descriptor.RunnerId)
            || !Mql5CompileValidation.IsSafeToken(descriptor.RunnerSessionId)
            || !string.Equals(descriptor.RunnerImageDigest, job.Toolchain.RunnerImageDigest, StringComparison.Ordinal)
            || !Mql5CompileValidation.FixedTimeHexEquals(descriptor.MetaEditorSha256, job.Toolchain.MetaEditorSha256)
            || !string.Equals(descriptor.MetaEditorVersion, job.Toolchain.MetaEditorVersion, StringComparison.Ordinal)
            || !Mql5CompileValidation.FixedTimeHexEquals(
                descriptor.PlatformLibrarySnapshotSha256,
                job.Toolchain.PlatformLibrarySnapshotSha256)
            || descriptor.IsolationPolicy != job.IsolationPolicy
            || !Mql5CompileValidation.FixedTimeHexEquals(descriptor.OutputSha256, outputSha256)
            || descriptor.OutputRecordCount < 0
            || !Enum.IsDefined(descriptor.RunStatus))
        {
            return AttestationValidation.Invalid("RUNNER_ATTESTATION_BINDING_INVALID");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (descriptor.StartedAtUtc.Offset != TimeSpan.Zero
            || descriptor.CompletedAtUtc.Offset != TimeSpan.Zero
            || descriptor.CompletedAtUtc < descriptor.StartedAtUtc
            || descriptor.StartedAtUtc < job.RequestedAtUtc
            || descriptor.CompletedAtUtc > now + MaximumClockSkew
            || now - descriptor.CompletedAtUtc > MaximumAttestationAge)
        {
            return AttestationValidation.Invalid("RUNNER_ATTESTATION_STALE_OR_TIME_INVALID");
        }

        if (!string.Equals(
                attestation.Algorithm,
                Mql5CompileValidation.SignatureAlgorithm,
                StringComparison.Ordinal)
            || !Mql5CompileValidation.IsSafeToken(attestation.SigningKeyId)
            || !Mql5CompileValidation.IsExactSha256(attestation.SignatureSha256)
            || !Mql5CompileValidation.IsExactSha256(attestation.SignedPayloadSha256))
        {
            return AttestationValidation.Invalid("RUNNER_ATTESTATION_SIGNATURE_INVALID");
        }

        byte[] signature = attestation.GetSignature();
        try
        {
            if (signature.Length is < 64 or > 256)
            {
                return AttestationValidation.Invalid("RUNNER_ATTESTATION_SIGNATURE_INVALID");
            }

            string signatureSha256 = Convert.ToHexString(SHA256.HashData(signature)).ToLowerInvariant();
            string payloadSha256 = CanonicalJson.Sha256(descriptor);
            if (!Mql5CompileValidation.FixedTimeHexEquals(signatureSha256, attestation.SignatureSha256!)
                || !Mql5CompileValidation.FixedTimeHexEquals(payloadSha256, attestation.SignedPayloadSha256!))
            {
                return AttestationValidation.Invalid("RUNNER_ATTESTATION_SIGNATURE_DIGEST_MISMATCH");
            }

            string canonicalPayload = CanonicalJson.Serialize(descriptor);
            if (!attestationVerifier.Verify(
                    attestation.SigningKeyId!,
                    attestation.Algorithm!,
                    signature,
                    canonicalPayload))
            {
                return AttestationValidation.Invalid("RUNNER_ATTESTATION_UNTRUSTED");
            }

            string attestationSha256 = CanonicalJson.Sha256(new
            {
                DescriptorSha256 = payloadSha256,
                SignatureSha256 = signatureSha256,
                attestation.SigningKeyId,
                attestation.Algorithm
            });
            return AttestationValidation.Success(attestationSha256, attestation.SigningKeyId!);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signature);
        }
    }

    private static string? ValidatePreflight(
        Mql5CompileJob job,
        out Mql5SourceManifest[] compileTargets)
    {
        compileTargets = [];
        if (job.JobId == Guid.Empty
            || job.RequestedAtUtc.Offset != TimeSpan.Zero
            || job.StaticManifest is null
            || job.Sources is null
            || job.Toolchain is null
            || job.IsolationPolicy is null)
        {
            return "COMPILE_JOB_INVALID";
        }

        if (!Mql5CompileValidation.IsExactImageDigest(job.Toolchain.RunnerImageDigest)
            || !Mql5CompileValidation.IsExactSha256(job.Toolchain.MetaEditorSha256)
            || !Mql5CompileValidation.IsSafeToken(job.Toolchain.MetaEditorVersion)
            || !Mql5CompileValidation.IsExactSha256(job.Toolchain.PlatformLibrarySnapshotSha256))
        {
            return "PINNED_TOOLCHAIN_INVALID";
        }

        string? isolationFailure = ValidateIsolationPolicy(job.IsolationPolicy);
        if (isolationFailure is not null)
        {
            return isolationFailure;
        }

        if (!string.Equals(job.StaticManifest.SchemaVersion, Mql5StaticInventoryAnalyzer.SchemaVersion, StringComparison.Ordinal)
            || !string.Equals(job.StaticManifest.AnalyzerVersion, Mql5StaticInventoryAnalyzer.AnalyzerVersion, StringComparison.Ordinal)
            || !Mql5CompileValidation.IsExactSha256(job.StaticManifest.CorpusSha256)
            || job.StaticManifest.FileCount != job.StaticManifest.Files.Count
            || job.StaticManifest.Files.Count is < 1 or > 10_000
            || job.StaticManifest.TotalBytes is < 1 or > 512L * 1024 * 1024
            || job.StaticManifest.Files.Any(static file => file.ByteLength is < 0 or > 64L * 1024 * 1024))
        {
            return "STATIC_MANIFEST_INVALID";
        }

        if (job.Sources.Count != job.StaticManifest.Files.Count
            || job.Sources.Any(static source => source is null
                || source.Content is null
                || !Mql5CompileValidation.IsSafeRelativeSourcePath(source.RelativePath)))
        {
            return "SOURCE_PATH_UNSAFE_FOR_RUNNER";
        }

        Mql5CorpusManifest rebuilt;
        try
        {
            rebuilt = new Mql5StaticInventoryAnalyzer().Analyze(job.Sources);
        }
        catch (ArgumentException)
        {
            return "SOURCE_CORPUS_INVALID";
        }

        if (!Mql5CompileValidation.FixedTimeHexEquals(rebuilt.CorpusSha256, job.StaticManifest.CorpusSha256)
            || rebuilt.TotalBytes != job.StaticManifest.TotalBytes
            || rebuilt.FileCount != job.StaticManifest.FileCount
            || !rebuilt.Files.Zip(job.StaticManifest.Files).All(static pair =>
                string.Equals(pair.First.RelativePath, pair.Second.RelativePath, StringComparison.Ordinal)
                && pair.First.ByteLength == pair.Second.ByteLength
                && Mql5CompileValidation.FixedTimeHexEquals(pair.First.Sha256, pair.Second.Sha256)))
        {
            return "SOURCE_HASH_DRIFT_DETECTED";
        }

        if (!Mql5CompileValidation.FixedTimeHexEquals(
                CanonicalJson.Sha256(rebuilt),
                CanonicalJson.Sha256(job.StaticManifest)))
        {
            return "STATIC_MANIFEST_CONTENT_DRIFT";
        }

        if (job.StaticManifest.Files.SelectMany(static file => file.Includes).Any(static include =>
            include.Resolution is Mql5IncludeResolution.MissingSource
                or Mql5IncludeResolution.Ambiguous
                or Mql5IncludeResolution.Invalid))
        {
            return "SOURCE_DEPENDENCY_NOT_RESOLVED";
        }

        compileTargets = job.StaticManifest.Files
            .Where(static file => file.Kind == Mql5SourceKind.ExpertOrProgram)
            .OrderBy(static file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
        return null;
    }

    private static string? ValidateIsolationPolicy(Mql5IsolationPolicy policy)
    {
        if (!policy.NetworkAccessDisabled
            || !policy.ReadOnlyRootFileSystem
            || !policy.EphemeralWorkspace
            || !policy.HostMountsDisabled
            || !policy.NoNewPrivileges)
        {
            return "ISOLATION_POLICY_NOT_FAIL_CLOSED";
        }

        return policy.MemoryLimitBytes is < 64 * 1024 * 1024 or > 4L * 1024 * 1024 * 1024
            || policy.CpuTimeLimitMilliseconds is < 1_000 or > 10 * 60 * 1_000
            || policy.WallClockTimeoutMilliseconds is < 1_000 or > 15 * 60 * 1_000
            || policy.ProcessLimit is < 1 or > 64
            || policy.TemporaryStorageLimitBytes is < 1024 * 1024 or > 4L * 1024 * 1024 * 1024
            || policy.CompilerOutputLimitBytes is < 1024 or > 16 * 1024 * 1024
                ? "ISOLATION_RESOURCE_LIMIT_INVALID"
                : null;
    }

    private static string? ValidateFileBindings(
        Mql5SourceManifest[] targets,
        IReadOnlyList<Mql5FileCompileEvidence> files,
        Mql5IsolatedRunStatus runStatus)
    {
        Dictionary<string, Mql5SourceManifest> targetIndex = targets.ToDictionary(
            static target => target.RelativePath,
            StringComparer.Ordinal);
        foreach (Mql5FileCompileEvidence file in files)
        {
            if (!targetIndex.TryGetValue(file.RelativePath, out Mql5SourceManifest? target)
                || !Mql5CompileValidation.FixedTimeHexEquals(file.SourceSha256, target.Sha256))
            {
                return "COMPILE_RESULT_SOURCE_BINDING_INVALID";
            }
        }

        if (runStatus == Mql5IsolatedRunStatus.Completed
            && (files.Count != targets.Length
                || targets.Any(target => files.All(file =>
                    !string.Equals(file.RelativePath, target.RelativePath, StringComparison.Ordinal)))))
        {
            return "COMPILE_RESULT_INCOMPLETE";
        }

        return null;
    }

    private static (Mql5CompileProofState State, string ReasonCode) DetermineOutcome(
        Mql5IsolatedRunStatus runStatus,
        IReadOnlyList<Mql5FileCompileEvidence> files)
    {
        if (runStatus == Mql5IsolatedRunStatus.Unsupported)
        {
            return (Mql5CompileProofState.Unsupported, "ISOLATED_RUNNER_REPORTED_UNSUPPORTED");
        }

        if (runStatus == Mql5IsolatedRunStatus.TimedOut)
        {
            return (Mql5CompileProofState.Failed, "ISOLATED_COMPILE_TIMED_OUT");
        }

        if (runStatus == Mql5IsolatedRunStatus.Failed
            || files.Any(static file => file.Status != Mql5FileCompileStatus.Succeeded || file.ExitCode != 0))
        {
            return (Mql5CompileProofState.Failed, "METAEDITOR_COMPILE_FAILED");
        }

        if (files.Any(static file =>
            !Mql5CompileValidation.FixedTimeHexEquals(file.ArtifactSha256!, file.RepeatArtifactSha256!)))
        {
            return (Mql5CompileProofState.Failed, "COMPILE_ARTIFACT_NONDETERMINISTIC");
        }

        return (Mql5CompileProofState.Proven, "METAEDITOR_COMPILE_PROVEN_BY_ISOLATED_RUNNER");
    }

    private static Mql5CompileEvidence CreateLocalEvidence(
        Mql5CompileJob job,
        Mql5CompileProofState state,
        string reasonCode) => new(
            job.JobId,
            job.StaticManifest?.CorpusSha256 ?? string.Empty,
            state,
            reasonCode,
            job.Toolchain?.RunnerImageDigest ?? string.Empty,
            job.Toolchain?.MetaEditorSha256 ?? string.Empty,
            null,
            null,
            null,
            null,
            null,
            null,
            []);

    private static Mql5CompileEvidence CreateAttestedEvidence(
        Mql5CompileJob job,
        Mql5RunnerAttestationDescriptor descriptor,
        string attestationSha256,
        string signingKeyId,
        Mql5CompileProofState state,
        string reasonCode,
        IReadOnlyList<Mql5FileCompileEvidence> files) => new(
            job.JobId,
            job.StaticManifest.CorpusSha256,
            state,
            reasonCode,
            job.Toolchain.RunnerImageDigest,
            job.Toolchain.MetaEditorSha256,
            descriptor.RunnerId,
            descriptor.RunnerSessionId,
            signingKeyId,
            attestationSha256,
            descriptor.StartedAtUtc,
            descriptor.CompletedAtUtc,
            files);

    private sealed record AttestationValidation(
        bool Valid,
        string ReasonCode,
        string AttestationSha256,
        string SigningKeyId)
    {
        public static AttestationValidation Invalid(string reasonCode) => new(false, reasonCode, string.Empty, string.Empty);

        public static AttestationValidation Success(string attestationSha256, string signingKeyId) =>
            new(true, string.Empty, attestationSha256, signingKeyId);
    }
}
