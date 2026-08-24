using System.Security.Cryptography;
using System.Text;
using YO4X.BuildingBlocks;

namespace YO4X.StrategyGovernance;

public sealed class Mql5IsolatedCompileOrchestrator
{
    private static readonly TimeSpan MaximumAttestationAge = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaximumClockSkew = TimeSpan.FromSeconds(30);

    private readonly IMql5IsolatedCompileRunner runner;
    private readonly IMql5RunnerAttestationVerifier attestationVerifier;
    private readonly TimeProvider timeProvider;
    private readonly Mql5ApprovedCompileProfile? approvedProfile;
    private int runnerInvocationOccupied;

    public Mql5IsolatedCompileOrchestrator(
        IMql5IsolatedCompileRunner runner,
        IMql5RunnerAttestationVerifier attestationVerifier,
        TimeProvider? timeProvider = null,
        Mql5ApprovedCompileProfile? approvedProfile = null)
    {
        this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
        this.attestationVerifier = attestationVerifier
            ?? throw new ArgumentNullException(nameof(attestationVerifier));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.approvedProfile = approvedProfile;
    }

    public Task<Mql5CompileEvidence> CompileAsync(
        Mql5CompileJob job,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();
        if (approvedProfile is null)
        {
            return Task.FromResult(CreateLocalEvidence(
                job,
                Mql5CompileProofState.Blocked,
                "COMPILE_PROFILE_NOT_CONFIGURED"));
        }

        string? cheapPreflightFailure = ValidateCheapPreflight(job);
        if (cheapPreflightFailure is not null)
        {
            return Task.FromResult(CreateLocalEvidence(
                job,
                Mql5CompileProofState.Blocked,
                cheapPreflightFailure));
        }

        if (Interlocked.CompareExchange(ref runnerInvocationOccupied, 1, 0) != 0)
        {
            return Task.FromResult(CreateLocalEvidence(
                job,
                Mql5CompileProofState.Blocked,
                "ISOLATED_RUNNER_CAPACITY_EXHAUSTED"));
        }

        return CompileWithCapacityAsync(
            job,
            new RunnerCapacityLease(this),
            cancellationToken);
    }

    private async Task<Mql5CompileEvidence> CompileWithCapacityAsync(
        Mql5CompileJob job,
        RunnerCapacityLease capacityLease,
        CancellationToken cancellationToken)
    {
        using RunnerCapacityLease ownedCapacity = capacityLease;

        Mql5SourceDocument[]? sourceSnapshots = SnapshotSources(job.Sources, out string? snapshotFailure);
        if (snapshotFailure is not null)
        {
            return CreateLocalEvidence(job, Mql5CompileProofState.Blocked, snapshotFailure);
        }

        Mql5CompileJob? snapshotJob = SnapshotCompileJob(
            job,
            sourceSnapshots!,
            out string? jobSnapshotFailure);
        if (jobSnapshotFailure is not null)
        {
            ZeroSources(sourceSnapshots!);
            return CreateLocalEvidence(job, Mql5CompileProofState.Blocked, jobSnapshotFailure);
        }

        try
        {
            string? preflightFailure = ValidatePreflight(
                snapshotJob!,
                out Mql5TargetCompilePackageDossier? compilePackage);
            if (preflightFailure is not null)
            {
                return CreateLocalEvidence(snapshotJob!, Mql5CompileProofState.Blocked, preflightFailure);
            }

            snapshotJob = snapshotJob! with { CompilePackage = compilePackage! };

            if (!compilePackage!.IsReadyForIsolatedCompile)
            {
                return CreateLocalEvidence(
                    snapshotJob,
                    compilePackage.Disposition == Mql5CompilePackageDisposition.BlockedUnsupportedSemantics
                        ? Mql5CompileProofState.Unsupported
                        : Mql5CompileProofState.Blocked,
                    GetPackageBlockerReasonCode(compilePackage.Disposition));
            }

            cancellationToken.ThrowIfCancellationRequested();
            byte[] challenge = RandomNumberGenerator.GetBytes(32);
            string challengeSha256 = Convert.ToHexString(SHA256.HashData(challenge)).ToLowerInvariant();
            CryptographicOperations.ZeroMemory(challenge);

            Mql5SourceDocument[] requestSources = CreateRequestSources(
                sourceSnapshots!,
                compilePackage);
            Mql5TargetCompilePackageDossier requestDossier = SnapshotDossier(compilePackage);
            var request = new Mql5IsolatedCompileRequest(
                snapshotJob.JobId,
                snapshotJob.RequestedAtUtc,
                challengeSha256,
                approvedProfile!.ProfileId,
                approvedProfile.ProfileSha256,
                snapshotJob.StaticManifest.CorpusSha256,
                compilePackage.StaticManifestSha256,
                compilePackage.ConversionEvidenceSha256,
                compilePackage.ConversionEvidenceContentSha256,
                compilePackage.DependencyGraphSha256,
                compilePackage.PackageSha256,
                compilePackage.SourceClosureSha256,
                compilePackage.TargetRelativePath,
                requestDossier,
                Array.AsReadOnly(requestSources),
                snapshotJob.Toolchain,
                snapshotJob.IsolationPolicy);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(
                snapshotJob.IsolationPolicy.WallClockTimeoutMilliseconds));

            Mql5IsolatedCompileResponse? response = null;
            Task<Mql5IsolatedCompileResponse>? runnerTask = null;
            try
            {
                runnerTask = Task.Run(
                    () => runner.CompileAsync(request, timeout.Token),
                    timeout.Token);
                ObserveRunnerCompletion(runnerTask);
                response = await runnerTask.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (!ValidateCallerEvidenceIntegrity(snapshotJob, compilePackage))
                {
                    response?.ClearCompilerOutput();
                    return CreateLocalEvidence(
                        snapshotJob,
                        Mql5CompileProofState.Blocked,
                        "COMPILE_JOB_EVIDENCE_MUTATED");
                }

                if (!ValidateReturnedRequestIntegrity(
                        snapshotJob,
                        compilePackage,
                        challengeSha256,
                        request))
                {
                    response?.ClearCompilerOutput();
                    return CreateLocalEvidence(
                        snapshotJob,
                        Mql5CompileProofState.Blocked,
                        "ISOLATED_RUNNER_REQUEST_MUTATED");
                }
            }
            catch (Mql5RunnerUnavailableException exception)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return CreateLocalEvidence(
                    snapshotJob,
                    Mql5CompileProofState.Blocked,
                    exception.ReasonCode);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
                && timeout.IsCancellationRequested)
            {
                return CreateLocalEvidence(
                    snapshotJob,
                    Mql5CompileProofState.Blocked,
                    "ISOLATED_RUNNER_RESPONSE_TIMEOUT");
            }
            catch (Exception)
            {
                response?.ClearCompilerOutput();
                cancellationToken.ThrowIfCancellationRequested();
                return CreateLocalEvidence(
                    snapshotJob,
                    Mql5CompileProofState.Blocked,
                    "ISOLATED_RUNNER_FAILED");
            }
            finally
            {
                ReleaseRequestSourcesWhenSafe(runnerTask, requestSources);
                if (runnerTask is { IsCompleted: false })
                {
                    ownedCapacity.HoldUntil(runnerTask);
                }
            }

            return EvaluateResponse(snapshotJob, compilePackage, challengeSha256, response!);
        }
        finally
        {
            ZeroSources(sourceSnapshots!);
        }
    }

    private static Mql5SourceDocument[]? SnapshotSources(
        IReadOnlyList<Mql5SourceDocument>? sources,
        out string? failureCode)
    {
        if (sources is null)
        {
            failureCode = "COMPILE_JOB_INVALID";
            return null;
        }

        Mql5SourceDocument[] sourceReferences;
        try
        {
            int count = sources.Count;
            if (count is < 1 or > Mql5CompileValidation.MaximumSourceFileCount)
            {
                failureCode = "SOURCE_CORPUS_INVALID";
                return null;
            }

            sourceReferences = new Mql5SourceDocument[count];
            for (int index = 0; index < count; index++)
            {
                sourceReferences[index] = sources[index];
            }
        }
        catch (Exception exception) when (IsNonCatastrophic(exception))
        {
            failureCode = "SOURCE_CORPUS_INVALID";
            return null;
        }

        string? referenceFailure = Mql5CompileValidation.ValidateSourceReferences(sourceReferences);
        if (referenceFailure is not null)
        {
            failureCode = referenceFailure;
            return null;
        }

        var snapshots = new Mql5SourceDocument[sourceReferences.Length];
        try
        {
            for (int index = 0; index < sourceReferences.Length; index++)
            {
                Mql5SourceDocument source = sourceReferences[index];
                snapshots[index] = new Mql5SourceDocument(
                    source.RelativePath,
                    source.Content.ToArray());
            }
        }
        catch
        {
            ZeroSources(snapshots);
            throw;
        }

        failureCode = null;
        return snapshots;
    }

    private static Mql5CompileJob? SnapshotCompileJob(
        Mql5CompileJob source,
        Mql5SourceDocument[] ownedSourceSnapshots,
        out string? failureCode)
    {
        try
        {
            if (source.StaticManifest is null
                || source.ConversionEvidence is null
                || source.CompilePackage is null
                || source.Toolchain is null
                || source.IsolationPolicy is null)
            {
                failureCode = "COMPILE_JOB_INVALID";
                return null;
            }

            var budget = new MetadataSnapshotBudget();
            foreach (Mql5SourceDocument sourceDocument in ownedSourceSnapshots)
            {
                _ = budget.TakeText(sourceDocument.RelativePath);
            }

            Mql5CorpusManifest staticManifest = SnapshotStaticManifest(
                source.StaticManifest,
                budget);
            Mql5ConversionCorpusEvidence conversionEvidence = SnapshotConversionEvidence(
                source.ConversionEvidence,
                budget);
            Mql5TargetCompilePackageDossier compilePackage = SnapshotDossier(
                source.CompilePackage,
                budget);
            failureCode = null;
            return new Mql5CompileJob(
                source.JobId,
                source.RequestedAtUtc,
                staticManifest,
                conversionEvidence,
                ownedSourceSnapshots,
                compilePackage,
                source.Toolchain with
                {
                    RunnerImageDigest = budget.TakeText(source.Toolchain.RunnerImageDigest),
                    MetaEditorSha256 = budget.TakeText(source.Toolchain.MetaEditorSha256),
                    MetaEditorVersion = budget.TakeText(source.Toolchain.MetaEditorVersion),
                    PlatformLibrarySnapshotSha256 = budget.TakeText(
                        source.Toolchain.PlatformLibrarySnapshotSha256)
                },
                source.IsolationPolicy with { });
        }
        catch (Exception exception) when (IsNonCatastrophic(exception))
        {
            failureCode = "COMPILE_JOB_INVALID";
            return null;
        }
    }

    private static Mql5CorpusManifest SnapshotStaticManifest(
        Mql5CorpusManifest source,
        MetadataSnapshotBudget budget) => source with
        {
            SchemaVersion = budget.TakeText(source.SchemaVersion),
            AnalyzerVersion = budget.TakeText(source.AnalyzerVersion),
            CorpusSha256 = budget.TakeText(source.CorpusSha256),
            Files = SnapshotList(source.Files, budget, file => file with
            {
                RelativePath = budget.TakeText(file.RelativePath),
                Sha256 = budget.TakeText(file.Sha256),
                TextEncoding = budget.TakeText(file.TextEncoding),
                Entrypoints = SnapshotList(
                    file.Entrypoints,
                    budget,
                    budget.TakeText),
                Includes = SnapshotList(
                    file.Includes,
                    budget,
                    include => include with
                    {
                        DeclaredPath = budget.TakeText(include.DeclaredPath),
                        ResolvedRelativePath = budget.TakeNullableText(
                            include.ResolvedRelativePath)
                    }),
                Features = SnapshotList(file.Features, budget, feature => feature with
                {
                    Code = budget.TakeText(feature.Code),
                    Lines = SnapshotList(feature.Lines, budget, static line => line)
                }),
                Findings = SnapshotList(file.Findings, budget, finding => finding with
                {
                    Code = budget.TakeText(finding.Code),
                    Message = budget.TakeText(finding.Message),
                    Lines = SnapshotList(finding.Lines, budget, static line => line)
                }),
                Verification = file.Verification is null
                    ? throw new InvalidOperationException("Missing verification metadata.")
                    : file.Verification with { }
            })
        };

    private static Mql5ConversionCorpusEvidence SnapshotConversionEvidence(
        Mql5ConversionCorpusEvidence source,
        MetadataSnapshotBudget budget) => source with
        {
            SchemaVersion = budget.TakeText(source.SchemaVersion),
            AnalyzerVersion = budget.TakeText(source.AnalyzerVersion),
            InputStaticSchemaVersion = budget.TakeText(source.InputStaticSchemaVersion),
            InputStaticAnalyzerVersion = budget.TakeText(source.InputStaticAnalyzerVersion),
            InputCorpusSha256 = budget.TakeText(source.InputCorpusSha256),
            DependencyGraphSha256 = budget.TakeText(source.DependencyGraphSha256),
            EvidenceSha256 = budget.TakeText(source.EvidenceSha256),
            Files = SnapshotList(source.Files, budget, file => file with
            {
                RelativePath = budget.TakeText(file.RelativePath),
                SourceSha256 = budget.TakeText(file.SourceSha256),
                DependencyClosureSha256 = budget.TakeText(file.DependencyClosureSha256),
                EvidenceSha256 = budget.TakeText(file.EvidenceSha256),
                TextEncoding = budget.TakeText(file.TextEncoding),
                Entrypoints = SnapshotList(
                    file.Entrypoints,
                    budget,
                    budget.TakeText),
                StaticFeatures = SnapshotList(
                    file.StaticFeatures,
                    budget,
                    feature => feature with
                    {
                        Code = budget.TakeText(feature.Code),
                        Lines = SnapshotList(feature.Lines, budget, static line => line)
                    }),
                StaticFindings = SnapshotList(
                    file.StaticFindings,
                    budget,
                    finding => finding with
                    {
                        Code = budget.TakeText(finding.Code),
                        Message = budget.TakeText(finding.Message),
                        Lines = SnapshotList(finding.Lines, budget, static line => line)
                    }),
                Includes = SnapshotList(
                    file.Includes,
                    budget,
                    include => include with
                    {
                        DeclaredPath = budget.TakeText(include.DeclaredPath),
                        ResolvedRelativePath = budget.TakeNullableText(
                            include.ResolvedRelativePath)
                    }),
                DependencyClosure = file.DependencyClosure is null
                    ? throw new InvalidOperationException("Missing dependency metadata.")
                    : file.DependencyClosure with
                    {
                        DirectDependencies = SnapshotList(
                            file.DependencyClosure.DirectDependencies,
                            budget,
                            budget.TakeText),
                        TransitiveDependencies = SnapshotList(
                            file.DependencyClosure.TransitiveDependencies,
                            budget,
                            budget.TakeText),
                        DependencyFirstOrder = SnapshotList(
                            file.DependencyClosure.DependencyFirstOrder,
                            budget,
                            budget.TakeText),
                        ReachableCycleMembers = SnapshotList(
                            file.DependencyClosure.ReachableCycleMembers,
                            budget,
                            budget.TakeText)
                    },
                Lexical = file.Lexical is null
                    ? throw new InvalidOperationException("Missing lexical metadata.")
                    : file.Lexical with { },
                Structural = file.Structural is null
                    ? throw new InvalidOperationException("Missing structural metadata.")
                    : file.Structural with { },
                Stages = SnapshotList(
                    file.Stages,
                    budget,
                    stage => stage with
                    {
                        EvidenceCode = budget.TakeText(stage.EvidenceCode)
                    }),
                Findings = SnapshotList(file.Findings, budget, finding => finding with
                {
                    Code = budget.TakeText(finding.Code),
                    Message = budget.TakeText(finding.Message),
                    Location = finding.Location is null ? null : finding.Location with { }
                })
            })
        };

    private static TResult[] SnapshotList<TSource, TResult>(
        IReadOnlyList<TSource>? source,
        MetadataSnapshotBudget budget,
        Func<TSource, TResult> snapshot)
    {
        if (source is null)
        {
            throw new InvalidOperationException("Missing metadata collection.");
        }

        int count = source.Count;
        budget.Take(count);
        var result = new TResult[count];
        for (int index = 0; index < count; index++)
        {
            TSource value = source[index];
            if (value is null)
            {
                throw new InvalidOperationException("Null metadata item.");
            }

            result[index] = snapshot(value);
        }

        return result;
    }

    private static void ObserveRunnerCompletion(Task runnerTask)
    {
        _ = runnerTask.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void ReleaseRunnerCapacity() =>
        Interlocked.Exchange(ref runnerInvocationOccupied, 0);

    private sealed class RunnerCapacityLease(
        Mql5IsolatedCompileOrchestrator owner) : IDisposable
    {
        private int state;

        public void HoldUntil(Task runnerTask)
        {
            ArgumentNullException.ThrowIfNull(runnerTask);
            if (runnerTask.IsCompleted)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref state, 1, 0) != 0)
            {
                return;
            }

            _ = runnerTask.ContinueWith(
                static (_, lease) =>
                    ((RunnerCapacityLease)lease!).ReleaseTransferred(),
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref state, 2, 0) == 0)
            {
                owner.ReleaseRunnerCapacity();
            }
        }

        private void ReleaseTransferred()
        {
            if (Interlocked.CompareExchange(ref state, 2, 1) == 1)
            {
                owner.ReleaseRunnerCapacity();
            }
        }
    }

    private static void ReleaseRequestSourcesWhenSafe(
        Task<Mql5IsolatedCompileResponse>? runnerTask,
        Mql5SourceDocument[] requestSources)
    {
        if (runnerTask is null || runnerTask.IsCompleted)
        {
            ZeroSources(requestSources);
            return;
        }

        _ = runnerTask.ContinueWith(
            static (completed, state) =>
            {
                _ = completed.Exception;
                if (completed.Status == TaskStatus.RanToCompletion)
                {
                    completed.Result?.ClearCompilerOutput();
                }

                ZeroSources((Mql5SourceDocument[])state!);
            },
            requestSources,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static bool IsNonCatastrophic(Exception exception) => exception is not (
        OutOfMemoryException
        or StackOverflowException
        or AccessViolationException);

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

    private static Mql5SourceDocument[] CreateRequestSources(
        Mql5SourceDocument[] corpusSnapshots,
        Mql5TargetCompilePackageDossier compilePackage)
    {
        Dictionary<string, Mql5SourceDocument> sourceByPath = corpusSnapshots.ToDictionary(
            static source => source.RelativePath,
            StringComparer.OrdinalIgnoreCase);
        var requestSources = new Mql5SourceDocument[compilePackage.OrderedSources.Count];
        try
        {
            for (int index = 0; index < requestSources.Length; index++)
            {
                Mql5CompilePackageSource source = compilePackage.OrderedSources[index];
                if (!sourceByPath.TryGetValue(source.RelativePath, out Mql5SourceDocument? snapshot)
                    || snapshot.Content.LongLength != source.ByteLength)
                {
                    throw new Mql5CompilePackagePlanningException("DEPENDENCY_CLOSURE_BINDING_INVALID");
                }

                byte[] ownedContent = snapshot.Content.ToArray();
                string ownedSha256 = Convert.ToHexString(SHA256.HashData(ownedContent)).ToLowerInvariant();
                if (!Mql5CompileValidation.FixedTimeHexEquals(ownedSha256, source.SourceSha256))
                {
                    CryptographicOperations.ZeroMemory(ownedContent);
                    throw new Mql5CompilePackagePlanningException("DEPENDENCY_CLOSURE_BINDING_INVALID");
                }

                requestSources[index] = new Mql5SourceDocument(source.RelativePath, ownedContent);
            }

            return requestSources;
        }
        catch
        {
            ZeroSources(requestSources);
            throw;
        }
    }

    private static Mql5TargetCompilePackageDossier SnapshotDossier(
        Mql5TargetCompilePackageDossier source) => SnapshotDossier(
            source,
            new MetadataSnapshotBudget());

    private static Mql5TargetCompilePackageDossier SnapshotDossier(
        Mql5TargetCompilePackageDossier source,
        MetadataSnapshotBudget budget) => source with
        {
            SchemaVersion = budget.TakeText(source.SchemaVersion),
            PlannerVersion = budget.TakeText(source.PlannerVersion),
            TargetRelativePath = budget.TakeText(source.TargetRelativePath),
            TargetSourceSha256 = budget.TakeText(source.TargetSourceSha256),
            CorpusSha256 = budget.TakeText(source.CorpusSha256),
            StaticManifestSha256 = budget.TakeText(source.StaticManifestSha256),
            ConversionEvidenceSha256 = budget.TakeText(source.ConversionEvidenceSha256),
            ConversionEvidenceContentSha256 = budget.TakeText(
                source.ConversionEvidenceContentSha256),
            DependencyGraphSha256 = budget.TakeText(source.DependencyGraphSha256),
            PlatformLibrarySnapshotApprovalId = budget.TakeNullableText(
                source.PlatformLibrarySnapshotApprovalId),
            ApprovedPlatformLibrarySnapshotSha256 = budget.TakeNullableText(
                source.ApprovedPlatformLibrarySnapshotSha256),
            PlatformLibrarySnapshotApprovalSha256 = budget.TakeNullableText(
                source.PlatformLibrarySnapshotApprovalSha256),
            ConversionFileEvidenceSha256 = budget.TakeText(
                source.ConversionFileEvidenceSha256),
            ConversionDependencyClosureSha256 = budget.TakeText(
                source.ConversionDependencyClosureSha256),
            SourceClosureSha256 = budget.TakeText(source.SourceClosureSha256),
            PackageSha256 = budget.TakeText(source.PackageSha256),
            OrderedSources = SnapshotList(
                source.OrderedSources,
                budget,
                item => item with
                {
                    RelativePath = budget.TakeText(item.RelativePath),
                    SourceSha256 = budget.TakeText(item.SourceSha256)
                }),
            OrderedIncludeEdges = SnapshotList(
                source.OrderedIncludeEdges,
                budget,
                item => item with
                {
                    SourceRelativePath = budget.TakeText(item.SourceRelativePath),
                    DeclaredPath = budget.TakeText(item.DeclaredPath),
                    ResolvedRelativePath = budget.TakeNullableText(item.ResolvedRelativePath)
                }),
            Blockers = SnapshotList(
                source.Blockers,
                budget,
                item => item with
                {
                    SourceRelativePath = budget.TakeText(item.SourceRelativePath),
                    DeclaredPath = budget.TakeText(item.DeclaredPath)
                })
        };

    private bool ValidateReturnedRequestIntegrity(
        Mql5CompileJob job,
        Mql5TargetCompilePackageDossier expectedPackage,
        string expectedChallengeSha256,
        Mql5IsolatedCompileRequest request)
    {
        try
        {
            Mql5SourceDocument[] returnedSources = request.Sources.ToArray();
            if (returnedSources.Length != expectedPackage.OrderedSources.Count)
            {
                return false;
            }

            for (int index = 0; index < returnedSources.Length; index++)
            {
                Mql5SourceDocument? returnedSource = returnedSources[index];
                Mql5CompilePackageSource expectedSource = expectedPackage.OrderedSources[index];
                if (returnedSource is null
                    || returnedSource.Content is null
                    || !string.Equals(
                        returnedSource.RelativePath,
                        expectedSource.RelativePath,
                        StringComparison.Ordinal)
                    || returnedSource.Content.LongLength != expectedSource.ByteLength
                    || !Mql5CompileValidation.FixedTimeHexEquals(
                        Convert.ToHexString(SHA256.HashData(returnedSource.Content)).ToLowerInvariant(),
                        expectedSource.SourceSha256))
                {
                    return false;
                }
            }

            return request.JobId == job.JobId
                && request.RequestedAtUtc == job.RequestedAtUtc
                && Mql5CompileValidation.FixedTimeHexEquals(
                    request.ChallengeSha256,
                    expectedChallengeSha256)
                && string.Equals(request.CompileProfileId, approvedProfile!.ProfileId, StringComparison.Ordinal)
                && Mql5CompileValidation.FixedTimeHexEquals(
                    request.CompileProfileSha256,
                    approvedProfile.ProfileSha256)
                && Mql5CompileValidation.FixedTimeHexEquals(
                    request.CorpusSha256,
                    job.StaticManifest.CorpusSha256)
                && Mql5CompileValidation.FixedTimeHexEquals(
                    request.StaticManifestSha256,
                    expectedPackage.StaticManifestSha256)
                && Mql5CompileValidation.FixedTimeHexEquals(
                    request.ConversionEvidenceSha256,
                    expectedPackage.ConversionEvidenceSha256)
                && Mql5CompileValidation.FixedTimeHexEquals(
                    request.ConversionEvidenceContentSha256,
                    expectedPackage.ConversionEvidenceContentSha256)
                && Mql5CompileValidation.FixedTimeHexEquals(
                    request.DependencyGraphSha256,
                    expectedPackage.DependencyGraphSha256)
                && Mql5CompileValidation.FixedTimeHexEquals(
                    request.CompilePackageSha256,
                    expectedPackage.PackageSha256)
                && Mql5CompileValidation.FixedTimeHexEquals(
                    request.SourceClosureSha256,
                    expectedPackage.SourceClosureSha256)
                && string.Equals(
                    request.TargetRelativePath,
                    expectedPackage.TargetRelativePath,
                    StringComparison.Ordinal)
                && request.Toolchain == job.Toolchain
                && request.IsolationPolicy == job.IsolationPolicy
                && Mql5CompileValidation.FixedTimeHexEquals(
                    CanonicalJson.Sha256(request.CompilePackage),
                    CanonicalJson.Sha256(expectedPackage));
        }
        catch (Exception exception) when (IsNonCatastrophic(exception))
        {
            return false;
        }
    }

    private static bool ValidateCallerEvidenceIntegrity(
        Mql5CompileJob job,
        Mql5TargetCompilePackageDossier expectedPackage)
    {
        try
        {
            return Mql5CompileValidation.FixedTimeHexEquals(
                    CanonicalJson.Sha256(job.StaticManifest),
                    expectedPackage.StaticManifestSha256)
                && Mql5CompileValidation.FixedTimeHexEquals(
                    CanonicalJson.Sha256(job.ConversionEvidence),
                    expectedPackage.ConversionEvidenceContentSha256)
                && Mql5CompileValidation.FixedTimeHexEquals(
                    job.StaticManifest.CorpusSha256,
                    expectedPackage.CorpusSha256)
                && Mql5CompileValidation.FixedTimeHexEquals(
                    job.ConversionEvidence.EvidenceSha256,
                    expectedPackage.ConversionEvidenceSha256)
                && Mql5CompileValidation.FixedTimeHexEquals(
                    job.ConversionEvidence.DependencyGraphSha256,
                    expectedPackage.DependencyGraphSha256);
        }
        catch (Exception exception) when (IsNonCatastrophic(exception))
        {
            return false;
        }
    }

    private static string GetPackageBlockerReasonCode(
        Mql5CompilePackageDisposition disposition) => disposition switch
        {
            Mql5CompilePackageDisposition.BlockedAllNulSource => "COMPILE_PACKAGE_ALL_NUL_SOURCE",
            Mql5CompilePackageDisposition.BlockedBinarySource => "COMPILE_PACKAGE_BINARY_SOURCE",
            Mql5CompilePackageDisposition.BlockedInvalidSyntax => "COMPILE_PACKAGE_INVALID_SYNTAX",
            Mql5CompilePackageDisposition.BlockedMissingDependency => "COMPILE_PACKAGE_MISSING_DEPENDENCY",
            Mql5CompilePackageDisposition.BlockedAmbiguousDependency => "COMPILE_PACKAGE_AMBIGUOUS_DEPENDENCY",
            Mql5CompilePackageDisposition.BlockedInvalidDependency => "COMPILE_PACKAGE_INVALID_DEPENDENCY",
            Mql5CompilePackageDisposition.BlockedDependencyCycle => "COMPILE_PACKAGE_DEPENDENCY_CYCLE",
            Mql5CompilePackageDisposition.BlockedUnsupportedSemantics => "COMPILE_PACKAGE_UNSUPPORTED_SEMANTICS",
            Mql5CompilePackageDisposition.BlockedPlatformSnapshot => "COMPILE_PACKAGE_PLATFORM_SNAPSHOT_REQUIRED",
            Mql5CompilePackageDisposition.BlockedApprovedPlatformSnapshotUnavailable =>
                "COMPILE_PACKAGE_APPROVED_PLATFORM_SNAPSHOT_UNAVAILABLE",
            _ => "COMPILE_PACKAGE_NOT_READY"
        };

    private Mql5CompileEvidence EvaluateResponse(
        Mql5CompileJob job,
        Mql5TargetCompilePackageDossier compilePackage,
        string challengeSha256,
        Mql5IsolatedCompileResponse response)
    {
        if (response is null)
        {
            return CreateLocalEvidence(job, Mql5CompileProofState.Blocked, "RUNNER_ATTESTATION_MISSING");
        }

        if (response.CompilerOutputLength > job.IsolationPolicy.CompilerOutputLimitBytes)
        {
            response.ClearCompilerOutput();
            return CreateLocalEvidence(
                job,
                Mql5CompileProofState.Blocked,
                "COMPILER_OUTPUT_LIMIT_EXCEEDED");
        }

        if (response.Attestation is null)
        {
            response.ClearCompilerOutput();
            return CreateLocalEvidence(job, Mql5CompileProofState.Blocked, "RUNNER_ATTESTATION_MISSING");
        }

        byte[] output = response.CopyCompilerOutput(job.IsolationPolicy.CompilerOutputLimitBytes);
        try
        {
            string outputSha256 = Convert.ToHexString(SHA256.HashData(output)).ToLowerInvariant();
            AttestationValidation validation = ValidateAttestation(
                job,
                compilePackage,
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
                    1);
            }
            catch (Mql5CompilerOutputException exception)
            {
                return CreateAttestedEvidence(
                    job,
                    compilePackage,
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
                    compilePackage,
                    descriptor,
                    validation.AttestationSha256,
                    validation.SigningKeyId,
                    Mql5CompileProofState.Failed,
                    "ATTESTED_OUTPUT_COUNT_MISMATCH",
                    files);
            }

            string? bindingFailure = ValidateFileBindings(compilePackage, files, descriptor.RunStatus);
            if (bindingFailure is not null)
            {
                return CreateAttestedEvidence(
                    job,
                    compilePackage,
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
                compilePackage,
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
            response.ClearCompilerOutput();
        }
    }

    private AttestationValidation ValidateAttestation(
        Mql5CompileJob job,
        Mql5TargetCompilePackageDossier compilePackage,
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
            || !string.Equals(descriptor.CompileProfileId, approvedProfile!.ProfileId, StringComparison.Ordinal)
            || !Mql5CompileValidation.FixedTimeHexEquals(
                descriptor.CompileProfileSha256,
                approvedProfile.ProfileSha256)
            || !Mql5CompileValidation.FixedTimeHexEquals(
                descriptor.CorpusSha256,
                compilePackage.CorpusSha256)
            || !Mql5CompileValidation.FixedTimeHexEquals(
                descriptor.StaticManifestSha256,
                compilePackage.StaticManifestSha256)
            || !Mql5CompileValidation.FixedTimeHexEquals(
                descriptor.ConversionEvidenceSha256,
                compilePackage.ConversionEvidenceSha256)
            || !Mql5CompileValidation.FixedTimeHexEquals(
                descriptor.ConversionEvidenceContentSha256,
                compilePackage.ConversionEvidenceContentSha256)
            || !Mql5CompileValidation.FixedTimeHexEquals(
                descriptor.DependencyGraphSha256,
                compilePackage.DependencyGraphSha256)
            || !Mql5CompileValidation.FixedTimeHexEquals(
                descriptor.CompilePackageSha256,
                compilePackage.PackageSha256)
            || !Mql5CompileValidation.FixedTimeHexEquals(
                descriptor.SourceClosureSha256,
                compilePackage.SourceClosureSha256)
            || !string.Equals(
                descriptor.TargetRelativePath,
                compilePackage.TargetRelativePath,
                StringComparison.Ordinal)
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
            || descriptor.CompletedAtUtc - descriptor.StartedAtUtc
                > TimeSpan.FromMilliseconds(job.IsolationPolicy.WallClockTimeoutMilliseconds)
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

        if (!approvedProfile!.ApprovesSigningKey(attestation.SigningKeyId!))
        {
            return AttestationValidation.Invalid("RUNNER_ATTESTATION_SIGNER_NOT_APPROVED");
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
            bool trusted;
            try
            {
                trusted = attestationVerifier.Verify(
                    attestation.SigningKeyId!,
                    attestation.Algorithm!,
                    signature,
                    canonicalPayload);
            }
            catch (Exception exception) when (IsNonCatastrophic(exception))
            {
                trusted = false;
            }

            if (!trusted)
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

    private string? ValidatePreflight(
        Mql5CompileJob job,
        out Mql5TargetCompilePackageDossier? compilePackage)
    {
        compilePackage = null;
        string? cheapPreflightFailure = ValidateCheapPreflight(job);
        if (cheapPreflightFailure is not null)
        {
            return cheapPreflightFailure;
        }

        try
        {
            compilePackage = Mql5CompilePackageDossierPlanner.ValidateForDispatch(
                job.StaticManifest,
                job.ConversionEvidence,
                job.Sources,
                job.CompilePackage,
                approvedProfile!.PlatformLibrarySnapshot);
        }
        catch (Mql5CompilePackagePlanningException exception)
        {
            return exception.ReasonCode;
        }

        return null;
    }

    private string? ValidateCheapPreflight(Mql5CompileJob job)
    {
        if (job.JobId == Guid.Empty
            || job.RequestedAtUtc.Offset != TimeSpan.Zero
            || job.StaticManifest is null
            || job.ConversionEvidence is null
            || job.Sources is null
            || job.CompilePackage is null
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

        string? isolationFailure = Mql5CompileValidation.ValidateIsolationPolicy(job.IsolationPolicy);
        if (isolationFailure is not null)
        {
            return isolationFailure;
        }

        if (approvedProfile is null)
        {
            return "COMPILE_PROFILE_NOT_CONFIGURED";
        }

        if (!approvedProfile.ApprovesToolchain(job.Toolchain))
        {
            return "COMPILE_TOOLCHAIN_NOT_APPROVED";
        }

        if (!approvedProfile.ApprovesIsolationPolicy(job.IsolationPolicy))
        {
            return "COMPILE_ISOLATION_POLICY_NOT_APPROVED";
        }

        return null;
    }

    private static string? ValidateFileBindings(
        Mql5TargetCompilePackageDossier compilePackage,
        IReadOnlyList<Mql5FileCompileEvidence> files,
        Mql5IsolatedRunStatus runStatus)
    {
        foreach (Mql5FileCompileEvidence file in files)
        {
            if (!string.Equals(
                    file.RelativePath,
                    compilePackage.TargetRelativePath,
                    StringComparison.Ordinal)
                || !Mql5CompileValidation.FixedTimeHexEquals(
                    file.SourceSha256,
                    compilePackage.TargetSourceSha256))
            {
                return "COMPILE_RESULT_SOURCE_BINDING_INVALID";
            }
        }

        if (runStatus == Mql5IsolatedRunStatus.Completed
            && (files.Count != 1
                || files.All(file => !string.Equals(
                    file.RelativePath,
                    compilePackage.TargetRelativePath,
                    StringComparison.Ordinal))))
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

    private Mql5CompileEvidence CreateLocalEvidence(
        Mql5CompileJob job,
        Mql5CompileProofState state,
        string reasonCode) => new(
            job.JobId,
            SafeSha256OrNull(job.StaticManifest?.CorpusSha256) ?? string.Empty,
            approvedProfile?.ProfileId,
            approvedProfile?.ProfileSha256,
            SafeSha256OrNull(job.CompilePackage?.StaticManifestSha256),
            SafeSha256OrNull(job.CompilePackage?.ConversionEvidenceSha256),
            SafeSha256OrNull(job.CompilePackage?.ConversionEvidenceContentSha256),
            SafeSha256OrNull(job.CompilePackage?.DependencyGraphSha256),
            SafeSha256OrNull(job.CompilePackage?.PackageSha256),
            SafeSha256OrNull(job.CompilePackage?.SourceClosureSha256),
            SafeSourcePathOrNull(job.CompilePackage?.TargetRelativePath),
            state,
            reasonCode,
            Mql5CompileValidation.IsExactImageDigest(job.Toolchain?.RunnerImageDigest)
                ? job.Toolchain!.RunnerImageDigest
                : string.Empty,
            SafeSha256OrNull(job.Toolchain?.MetaEditorSha256) ?? string.Empty,
            Mql5CompileValidation.IsSafeToken(job.Toolchain?.MetaEditorVersion)
                ? job.Toolchain!.MetaEditorVersion
                : null,
            SafeSha256OrNull(job.Toolchain?.PlatformLibrarySnapshotSha256),
            TryComputeToolchainSha256(job.Toolchain),
            job.IsolationPolicy is null ? null : CanonicalJson.Sha256(job.IsolationPolicy),
            null,
            null,
            null,
            null,
            null,
            null,
            []);

    private static Mql5CompileEvidence CreateAttestedEvidence(
        Mql5CompileJob job,
        Mql5TargetCompilePackageDossier compilePackage,
        Mql5RunnerAttestationDescriptor descriptor,
        string attestationSha256,
        string signingKeyId,
        Mql5CompileProofState state,
        string reasonCode,
        IReadOnlyList<Mql5FileCompileEvidence> files) => new(
            job.JobId,
            compilePackage.CorpusSha256,
            descriptor.CompileProfileId,
            descriptor.CompileProfileSha256,
            descriptor.StaticManifestSha256,
            descriptor.ConversionEvidenceSha256,
            descriptor.ConversionEvidenceContentSha256,
            descriptor.DependencyGraphSha256,
            descriptor.CompilePackageSha256,
            descriptor.SourceClosureSha256,
            descriptor.TargetRelativePath,
            state,
            reasonCode,
            job.Toolchain.RunnerImageDigest,
            job.Toolchain.MetaEditorSha256,
            descriptor.MetaEditorVersion,
            descriptor.PlatformLibrarySnapshotSha256,
            CanonicalJson.Sha256(job.Toolchain),
            CanonicalJson.Sha256(descriptor.IsolationPolicy),
            descriptor.RunnerId,
            descriptor.RunnerSessionId,
            signingKeyId,
            attestationSha256,
            descriptor.StartedAtUtc,
            descriptor.CompletedAtUtc,
            files);

    private static string? SafeSha256OrNull(string? value) =>
        Mql5CompileValidation.IsExactSha256(value) ? value : null;

    private static string? SafeSourcePathOrNull(string? value) =>
        Mql5CompileValidation.IsSafeRelativeSourcePath(value) ? value : null;

    private static string? TryComputeToolchainSha256(Mql5PinnedToolchain? toolchain) =>
        toolchain is not null
        && Mql5CompileValidation.IsExactImageDigest(toolchain.RunnerImageDigest)
        && Mql5CompileValidation.IsExactSha256(toolchain.MetaEditorSha256)
        && Mql5CompileValidation.IsSafeToken(toolchain.MetaEditorVersion)
        && Mql5CompileValidation.IsExactSha256(toolchain.PlatformLibrarySnapshotSha256)
            ? CanonicalJson.Sha256(toolchain)
            : null;

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

    private sealed class MetadataSnapshotBudget
    {
        private const int MaximumMetadataItemCount = 100_000;
        private const int MaximumMetadataUtf8Bytes = 8 * 1024 * 1024;
        private int itemCount;
        private int utf8ByteCount;

        public void Take(int count)
        {
            if (count < 0
                || itemCount > MaximumMetadataItemCount - count)
            {
                throw new InvalidOperationException("Compile metadata exceeds the snapshot limit.");
            }

            itemCount += count;
        }

        public string TakeText(string? value)
        {
            if (value is null)
            {
                throw new InvalidOperationException("Missing compile metadata text.");
            }

            int byteCount = Encoding.UTF8.GetByteCount(value);
            if (utf8ByteCount > MaximumMetadataUtf8Bytes - byteCount)
            {
                throw new InvalidOperationException(
                    "Compile metadata text exceeds the snapshot limit.");
            }

            utf8ByteCount += byteCount;
            return value;
        }

        public string? TakeNullableText(string? value) => value is null
            ? null
            : TakeText(value);
    }
}
