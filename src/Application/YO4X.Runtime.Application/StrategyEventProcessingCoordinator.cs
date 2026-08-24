using YO4X.Strategy.Abstractions;
using YO4X.Tenancy;

namespace YO4X.Runtime.Application;

public sealed class StrategyEventProcessingCoordinator
{
    private readonly IStrategyEventTransactionStore store;
    private readonly IStrategyHostClient strategyHost;
    private readonly StrategyEventProcessingOptions options;
    private readonly TimeProvider timeProvider;
    private readonly IStrategyRuntimeIdentifierSource identifiers;
    private int activeHostEvaluations;

    public StrategyEventProcessingCoordinator(
        IStrategyEventTransactionStore store,
        IStrategyHostClient strategyHost,
        StrategyEventProcessingOptions options,
        TimeProvider timeProvider,
        IStrategyRuntimeIdentifierSource? identifiers = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.strategyHost = strategyHost ?? throw new ArgumentNullException(nameof(strategyHost));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.identifiers = identifiers ?? new UuidV7StrategyRuntimeIdentifierSource();
        options.Validate();
    }

    public async Task<StrategyEventProcessingResult> ProcessAsync(
        TenantExecutionContext context,
        StrategyEventReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryAcquireEvaluationCapacity())
        {
            return Result(
                StrategyEventProcessingOutcome.EvaluationFaulted,
                "strategy_host_evaluation_capacity_exhausted",
                reference);
        }

        bool evaluationCapacityHeld = true;
        try
        {
            Guid claimToken = RequireIdentifier(identifiers.NewId(), "claim");
            StrategyEventClaimResult claimResult;
            try
            {
                claimResult = await store.ClaimAsync(
                        context,
                        reference,
                        claimToken,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Result(
                    StrategyEventProcessingOutcome.ClaimRecoveryRequired,
                    "strategy_event_claim_outcome_ambiguous",
                    reference);
            }
            catch (Exception)
            {
                return Result(
                    StrategyEventProcessingOutcome.ClaimRecoveryRequired,
                    "strategy_event_claim_store_failed",
                    reference);
            }

            if (claimResult is null)
            {
                return Result(
                    StrategyEventProcessingOutcome.InvalidClaim,
                    "strategy_event_claim_missing",
                    reference);
            }

            switch (claimResult.Disposition)
            {
                case StrategyEventClaimDisposition.NoWork:
                    if (claimResult.Claim is not null || claimResult.Receipt is not null)
                    {
                        return Result(
                            StrategyEventProcessingOutcome.InvalidClaim,
                            "strategy_event_no_work_claim_invalid",
                            reference);
                    }

                    return Result(
                        StrategyEventProcessingOutcome.NoWork,
                        claimResult.Code,
                        reference);

                case StrategyEventClaimDisposition.AlreadyCommitted:
                    if (claimResult.Claim is not null
                        || !StrategyEventReceiptValidator.IsCommittedReference(
                            context,
                            reference,
                            claimResult.Receipt))
                    {
                        return Result(
                            StrategyEventProcessingOutcome.InvalidCommitReceipt,
                            "strategy_event_replay_receipt_invalid",
                            reference);
                    }

                    return new StrategyEventProcessingResult(
                        StrategyEventProcessingOutcome.AlreadyCommitted,
                        "strategy_event_already_committed",
                        reference,
                        null,
                        claimResult.Receipt);

                case StrategyEventClaimDisposition.Claimed:
                    break;

                default:
                    return Result(
                        StrategyEventProcessingOutcome.InvalidClaim,
                        "strategy_event_claim_disposition_invalid",
                        reference);
            }

            ClaimedStrategyEvent? claim = claimResult.Claim;
            if (claimResult.Receipt is not null
                || !StrategyEventEvidenceValidator.IsExactClaim(reference, claimToken, claim))
            {
                return Result(
                    StrategyEventProcessingOutcome.InvalidClaim,
                    "strategy_event_claim_invalid",
                    reference);
            }

            var evaluationRequest = new StrategyHostEvaluationRequest(
                context.TenantId,
                reference.DeploymentId,
                reference.WorkerInstanceId,
                reference.Generation,
                reference.Sequence,
                reference.EventId,
                claim!.Envelope.Payload,
                claim.Snapshot,
                claim.PriorState,
                reference.EventSha256,
                reference.SnapshotSha256,
                claim.PriorStateSha256);
            TimeSpan claimLifetime = claim.ClaimExpiresAtUtc - claim.AuthorityNowUtc;
            TimeSpan evaluationDeadline = claimLifetime < options.ResultBounds.MaximumWallTime
                ? claimLifetime
                : options.ResultBounds.MaximumWallTime;

            long evaluationStarted = timeProvider.GetTimestamp();
            StrategyResult? strategyResult;
            Task<StrategyResult?>? evaluation = null;
            var evaluationCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bool cancellationDisposalDeferred = false;
            try
            {
                // The transport implementation itself is untrusted. Invoke it on
                // a worker so a synchronous block before Task return cannot pin
                // the serialized supervisor loop outside the enforced deadline.
                evaluation = Task.Run(
                    () => strategyHost.EvaluateAsync(
                        evaluationRequest,
                        evaluationCancellation.Token),
                    CancellationToken.None);
                ReleaseCapacityWhenComplete(evaluation, this);
                evaluationCapacityHeld = false;
                if (evaluation is null)
                {
                    return Result(
                        StrategyEventProcessingOutcome.EvaluationFaulted,
                        "strategy_host_task_missing",
                        reference);
                }

                strategyResult = await evaluation.WaitAsync(
                        evaluationDeadline,
                        timeProvider,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                cancellationDisposalDeferred = true;
                RequestCancellationWithoutTrustingCallbacks(evaluationCancellation);
                ObserveLateFault(evaluation);
                return Result(
                    StrategyEventProcessingOutcome.EvaluationTimedOut,
                    "strategy_evaluation_timed_out",
                    reference);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancellationDisposalDeferred = true;
                RequestCancellationWithoutTrustingCallbacks(evaluationCancellation);
                ObserveLateFault(evaluation);
                return Result(
                    StrategyEventProcessingOutcome.EvaluationCancelled,
                    "strategy_evaluation_cancelled",
                    reference);
            }
            catch (OperationCanceledException)
            {
                return Result(
                    StrategyEventProcessingOutcome.EvaluationCancelled,
                    "strategy_host_cancelled",
                    reference);
            }
            catch (Exception)
            {
                return Result(
                    StrategyEventProcessingOutcome.EvaluationFaulted,
                    "strategy_evaluation_faulted",
                    reference);
            }
            finally
            {
                if (!cancellationDisposalDeferred)
                {
                    evaluationCancellation.Dispose();
                }
            }

            TimeSpan elapsed = timeProvider.GetElapsedTime(
                evaluationStarted,
                timeProvider.GetTimestamp());
            StrategyResultValidation validation;
            try
            {
                validation = StrategyResultValidator.Validate(
                    claim.PriorState,
                    strategyResult,
                    options.ResultBounds,
                    elapsed);
            }
            catch (Exception exception) when (exception is
                ArgumentException or
                InvalidOperationException or
                NullReferenceException or
                OverflowException)
            {
                return new StrategyEventProcessingResult(
                    StrategyEventProcessingOutcome.InvalidResult,
                    "strategy_result_validation_faulted",
                    reference,
                    StrategyResultValidationCode.StrategyFaulted);
            }
            if (!validation.IsValid || validation.BoundedResult is null)
            {
                return new StrategyEventProcessingResult(
                    StrategyEventProcessingOutcome.InvalidResult,
                    validation.ReasonCode,
                    reference,
                    validation.Code);
            }

            StrategyEventCommitRequest commitRequest;
            try
            {
                DateTimeOffset localPreparedAtUtc =
                    StrategyEvidencePrimitives.NormalizeUtcMicroseconds(timeProvider.GetUtcNow());
                if (localPreparedAtUtc >= claim.ClaimExpiresAtUtc)
                {
                    return Result(
                        StrategyEventProcessingOutcome.EvaluationTimedOut,
                        "strategy_event_claim_expired_before_commit",
                        reference);
                }

                DateTimeOffset preparedAtUtc = localPreparedAtUtc < claim.AuthorityNowUtc
                    ? claim.AuthorityNowUtc
                    : localPreparedAtUtc;
                Guid[] outboxIds = validation.BoundedResult.Result.RequestedActions
                    .Select(_ => RequireIdentifier(identifiers.NewId(), "outbox message"))
                    .ToArray();
                StrategyEventCommitEvidence evidence = StrategyEventCommitEvidenceFactory.Create(
                    context,
                    claim,
                    validation.BoundedResult,
                    RequireIdentifier(identifiers.NewId(), "commit"),
                    outboxIds,
                    preparedAtUtc);
                commitRequest = new StrategyEventCommitRequest(
                    claim,
                    validation.BoundedResult,
                    evidence);
            }
            catch (Exception exception) when (exception is ArgumentException or OverflowException)
            {
                return new StrategyEventProcessingResult(
                    StrategyEventProcessingOutcome.InvalidResult,
                    "strategy_commit_evidence_invalid",
                    reference,
                    validation.Code);
            }

            int maximumAttempts = checked(1 + options.CommitAcknowledgementRecoveryAttempts);
            for (int attempt = 1; attempt <= maximumAttempts; attempt++)
            {
                StrategyEventCommitReceipt receipt;
                try
                {
                    receipt = await store.CommitAsync(
                            context,
                            commitRequest,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return Result(
                        StrategyEventProcessingOutcome.CommitRecoveryRequired,
                        "strategy_event_commit_outcome_ambiguous",
                        reference);
                }
                catch (Exception) when (attempt < maximumAttempts)
                {
                    continue;
                }
                catch (Exception)
                {
                    return Result(
                        StrategyEventProcessingOutcome.CommitRecoveryRequired,
                        "strategy_event_commit_store_failed",
                        reference);
                }

                if (!StrategyEventReceiptValidator.IsExactCommit(commitRequest, receipt))
                {
                    return Result(
                        StrategyEventProcessingOutcome.InvalidCommitReceipt,
                        "strategy_event_commit_receipt_invalid",
                        reference);
                }

                return new StrategyEventProcessingResult(
                    receipt.Replayed
                        ? StrategyEventProcessingOutcome.AlreadyCommitted
                        : StrategyEventProcessingOutcome.Committed,
                    receipt.Replayed
                        ? "strategy_event_commit_replayed"
                        : "strategy_event_committed",
                    reference,
                    validation.Code,
                    receipt);
            }

            return Result(
                StrategyEventProcessingOutcome.CommitRecoveryRequired,
                "strategy_event_commit_store_failed",
                reference);
        }
        finally
        {
            if (evaluationCapacityHeld)
            {
                ReleaseEvaluationCapacity();
            }
        }
    }

    private static StrategyEventProcessingResult Result(
        StrategyEventProcessingOutcome outcome,
        string code,
        StrategyEventReference reference) => new(outcome, code, reference);

    private static Guid RequireIdentifier(Guid value, string kind) => value == Guid.Empty
        ? throw new InvalidOperationException($"The {kind} identifier source returned an empty value.")
        : value;

    private static void ObserveLateFault(Task? task)
    {
        if (task is null || task.IsCompletedSuccessfully || task.IsCanceled)
        {
            return;
        }

        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private bool TryAcquireEvaluationCapacity()
    {
        while (true)
        {
            int current = Volatile.Read(ref activeHostEvaluations);
            if (current >= options.MaximumConcurrentHostEvaluations)
            {
                return false;
            }

            if (Interlocked.CompareExchange(
                    ref activeHostEvaluations,
                    current + 1,
                    current) == current)
            {
                return true;
            }
        }
    }

    private void ReleaseEvaluationCapacity()
    {
        _ = Interlocked.Decrement(ref activeHostEvaluations);
    }

    private static void ReleaseCapacityWhenComplete(
        Task task,
        StrategyEventProcessingCoordinator coordinator)
    {
        _ = task.ContinueWith(
            static (_, state) =>
                ((StrategyEventProcessingCoordinator)state!).ReleaseEvaluationCapacity(),
            coordinator,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void RequestCancellationWithoutTrustingCallbacks(
        CancellationTokenSource cancellation)
    {
        Task cancellationTask;
        try
        {
            cancellationTask = cancellation.CancelAsync();
        }
        catch (Exception)
        {
            // A client-controlled cancellation callback must not replace the
            // deterministic timeout/cancellation outcome.
            return;
        }

        _ = cancellationTask.ContinueWith(
            static (completed, state) =>
            {
                _ = completed.Exception;
                try
                {
                    ((CancellationTokenSource)state!).Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Disposal is best-effort and must not escape a late continuation.
                }
            },
            cancellation,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
