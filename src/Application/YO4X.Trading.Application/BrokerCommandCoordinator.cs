using YO4X.BuildingBlocks;
using YO4X.Tenancy;
using YO4X.Trading.Abstractions;

namespace YO4X.Trading.Application;

public sealed class BrokerCommandCoordinator
{
    private readonly IBrokerCommandLifecycleStore store;
    private readonly IMt5Gateway gateway;
    private readonly IExecutionLeaseTrustVerifier leaseTrustVerifier;
    private readonly BrokerCommandCoordinatorOptions options;
    private readonly TimeProvider timeProvider;
    private readonly IBrokerCommandIdentifierSource identifiers;

    public BrokerCommandCoordinator(
        IBrokerCommandLifecycleStore store,
        IMt5Gateway gateway,
        IExecutionLeaseTrustVerifier leaseTrustVerifier,
        BrokerCommandCoordinatorOptions options,
        TimeProvider timeProvider,
        IBrokerCommandIdentifierSource? identifiers = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        this.leaseTrustVerifier = leaseTrustVerifier
            ?? throw new ArgumentNullException(nameof(leaseTrustVerifier));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.identifiers = identifiers ?? new UuidV7BrokerCommandIdentifierSource();
        options.Validate();
    }

    public async Task<BrokerCommandDispatchResult> DispatchAsync(
        TenantExecutionContext context,
        BrokerCommandReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(reference);
        EnsureContext(context, reference.CommandId);
        cancellationToken.ThrowIfCancellationRequested();

        BrokerCommandLifecycleReceipt? recovery;
        try
        {
            recovery = await store.RecoverExpiredLifecycleAsync(
                    context,
                    reference.CommandId,
                    reference.AuthorizationSha256,
                    identifiers.NewId(),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return DispatchRecovery(reference.CommandId, "broker_command_recovery_store_failed");
        }

        if (recovery is not null)
        {
            if (!BrokerCommandLifecycleReceiptValidator.IsExpiredLifecycleRecovery(
                    recovery,
                    reference))
            {
                return DispatchRecovery(
                    reference.CommandId,
                    "broker_command_recovery_receipt_invalid");
            }

            return new BrokerCommandDispatchResult(
                BrokerCommandDispatchOutcome.ReconciliationRequired,
                reference.CommandId,
                false,
                null,
                "broker_command_expired_lifecycle_recovered",
                recovery.State);
        }

        long dispatchClaimStartedTimestamp = timeProvider.GetTimestamp();
        BrokerCommandDispatchClaim claim;
        try
        {
            claim = await store.ClaimForDispatchAsync(
                    context,
                    reference,
                    identifiers.NewId(),
                    identifiers.NewId(),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Claim commit may have succeeded even when its acknowledgement was
            // lost. A durable send_in_progress marker will recover to Unknown.
            return DispatchRecovery(reference.CommandId, "broker_command_claim_outcome_ambiguous");
        }
        catch (UnauthorizedAccessException)
        {
            return new BrokerCommandDispatchResult(
                BrokerCommandDispatchOutcome.NoDispatchAuthority,
                reference.CommandId,
                false,
                null,
                "broker_command_not_dispatchable",
                null);
        }
        catch (Exception)
        {
            return DispatchRecovery(reference.CommandId, "broker_command_claim_store_failed");
        }

        if (claim is null)
        {
            return DispatchRecovery(reference.CommandId, "broker_command_claim_missing");
        }

        DateTimeOffset now = claim.Command.Command.CreatedAtUtc;
        string? guardFailure;
        if (!TryGetConservativeAuthorityNow(
                claim.AuthorityNowUtc,
                dispatchClaimStartedTimestamp,
                out now))
        {
            guardFailure = "broker_command_authority_clock_invalid";
        }
        else
        {
            try
            {
                guardFailure = BrokerCommandDispatchGuard.RejectReason(
                    context,
                    reference,
                    claim,
                    leaseTrustVerifier,
                    now);
            }
            catch (Exception)
            {
                guardFailure = "broker_command_dispatch_claim_invalid";
            }
        }
        GatewaySendResult result;
        bool gatewayInvoked = false;
        if (guardFailure is not null)
        {
            result = new GatewaySendResult(
                claim.Replayed || guardFailure == "broker_command_dispatch_authority_expired"
                    ? GatewayCommandDisposition.Unknown
                    : GatewayCommandDisposition.SubmissionDisabled,
                guardFailure,
                null,
                null,
                null,
                now,
                !claim.Replayed
                    && guardFailure != "broker_command_dispatch_authority_expired");
        }
        else if (cancellationToken.IsCancellationRequested)
        {
            result = new GatewaySendResult(
                GatewayCommandDisposition.SubmissionDisabled,
                "broker_command_cancelled_before_gateway_invocation",
                null,
                null,
                null,
                now,
                true);
        }
        else if (!options.SubmissionEnabled)
        {
            result = new GatewaySendResult(
                GatewayCommandDisposition.SubmissionDisabled,
                "broker_command_gateway_entry_disabled",
                null,
                null,
                null,
                now,
                true);
        }
        else
        {
            using var gatewayCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            if (!TryGetConservativeAuthorityNow(
                    claim.AuthorityNowUtc,
                    dispatchClaimStartedTimestamp,
                    out DateTimeOffset refreshedNow))
            {
                result = new GatewaySendResult(
                    GatewayCommandDisposition.Unknown,
                    "broker_command_authority_clock_invalid",
                    null,
                    null,
                    null,
                    now,
                    false);
            }
            else if (BrokerCommandDispatchGuard.AuthorityWindowRejectReason(
                    claim,
                    refreshedNow,
                    options.MinimumAuthorityWindow) is { } refreshedFailure)
            {
                result = new GatewaySendResult(
                    GatewayCommandDisposition.Unknown,
                    refreshedFailure,
                    null,
                    null,
                    null,
                    refreshedNow,
                    false);
            }
            else if (cancellationToken.IsCancellationRequested)
            {
                result = new GatewaySendResult(
                    GatewayCommandDisposition.SubmissionDisabled,
                    "broker_command_cancelled_before_gateway_invocation",
                    null,
                    null,
                    null,
                    refreshedNow,
                    true);
            }
            else
            {
                now = refreshedNow;
                TimeSpan gatewayWindow = BrokerCommandDispatchGuard.RemainingGatewayWindow(
                    claim,
                    now,
                    options.GatewaySendTimeout);
                gatewayCancellation.CancelAfter(gatewayWindow);
                try
                {
                    long gatewayEntryTimestamp = timeProvider.GetTimestamp();
                    gatewayInvoked = true;
                    Task<GatewaySendResult> send = gateway.SendAsync(
                        claim.Command,
                        gatewayCancellation.Token);
                    if (!TryGetElapsedTime(
                            gatewayEntryTimestamp,
                            out TimeSpan elapsedBeforeAwait)
                        || elapsedBeforeAwait >= gatewayWindow)
                    {
                        _ = ObserveGatewayCompletionAsync(send);
                        result = GatewayTimeoutUnknown(
                            claim,
                            dispatchClaimStartedTimestamp,
                            now);
                    }
                    else
                    {
                        GatewaySendResult raw = await send
                            .WaitAsync(
                                gatewayWindow - elapsedBeforeAwait,
                                cancellationToken)
                            .ConfigureAwait(false);
                        DateTimeOffset receivedAt = ConservativeAuthorityNowOr(
                            claim.AuthorityNowUtc,
                            dispatchClaimStartedTimestamp,
                            now);
                        if (!TryGetElapsedTime(
                                gatewayEntryTimestamp,
                                out TimeSpan totalGatewayElapsed)
                            || totalGatewayElapsed >= gatewayWindow
                            || BrokerCommandDispatchGuard.IsAuthorityExpired(
                                claim,
                                receivedAt))
                        {
                            result = GatewayTimeoutUnknown(
                                claim,
                                dispatchClaimStartedTimestamp,
                                now);
                        }
                        else
                        {
                            result = NormalizeGatewayResult(
                                raw,
                                claim.Command,
                                receivedAt);
                        }
                    }
                }
                catch (Exception)
                {
                    // Once the gateway boundary has been entered, every exception,
                    // timeout, and cancellation is an ambiguous external outcome.
                    result = new GatewaySendResult(
                        GatewayCommandDisposition.Unknown,
                        "broker_command_gateway_outcome_unknown",
                        null,
                        null,
                        null,
                        ConservativeAuthorityNowOr(
                            claim.AuthorityNowUtc,
                            dispatchClaimStartedTimestamp,
                            now),
                        false);
                }
            }
        }

        result = BrokerCommandLifecycleEvidence.NormalizeSubmission(result);

        BrokerCommandLifecycleReceipt receipt;
        try
        {
            using CancellationTokenSource durableWrite = DurableWriteToken();
            receipt = await store.RecordSubmissionAsync(
                    context,
                    claim,
                    result,
                    identifiers.NewId(),
                    durableWrite.Token)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The committed send_in_progress row is the recovery marker. Its
            // expiry is allowed to transition only to Unknown, never Ready.
            return new BrokerCommandDispatchResult(
                BrokerCommandDispatchOutcome.DurableRecoveryRequired,
                reference.CommandId,
                gatewayInvoked,
                result.Disposition,
                "broker_command_submission_persistence_unconfirmed",
                "send_in_progress");
        }

        if (!BrokerCommandLifecycleReceiptValidator.IsSubmissionReceipt(
                receipt,
                claim,
                result))
        {
            return new BrokerCommandDispatchResult(
                BrokerCommandDispatchOutcome.DurableRecoveryRequired,
                reference.CommandId,
                gatewayInvoked,
                result.Disposition,
                "broker_command_submission_receipt_invalid",
                null);
        }

        bool reconcile = result.Disposition is GatewayCommandDisposition.Accepted
            or GatewayCommandDisposition.Unknown;
        return new BrokerCommandDispatchResult(
            reconcile
                ? BrokerCommandDispatchOutcome.ReconciliationRequired
                : BrokerCommandDispatchOutcome.SubmissionRecorded,
            reference.CommandId,
            gatewayInvoked,
            result.Disposition,
            result.Code,
            receipt.State);
    }

    public async Task<BrokerCommandReconciliationResult> ReconcileAsync(
        TenantExecutionContext context,
        BrokerCommandReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(reference);
        EnsureContext(context, reference.CommandId);
        cancellationToken.ThrowIfCancellationRequested();

        BrokerCommandLifecycleReceipt? recovery;
        try
        {
            recovery = await store.RecoverExpiredLifecycleAsync(
                    context,
                    reference.CommandId,
                    reference.AuthorizationSha256,
                    identifiers.NewId(),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ReconciliationRecovery(
                reference.CommandId,
                "broker_reconciliation_recovery_store_failed");
        }

        if (recovery is not null
            && !BrokerCommandLifecycleReceiptValidator.IsExpiredLifecycleRecovery(
                recovery,
                reference))
        {
            return ReconciliationRecovery(
                reference.CommandId,
                "broker_reconciliation_recovery_receipt_invalid");
        }

        long reconciliationClaimStartedTimestamp = timeProvider.GetTimestamp();
        BrokerCommandReconciliationClaim claim;
        try
        {
            claim = await store.BeginReconciliationAsync(
                    context,
                    reference.CommandId,
                    reference.AuthorizationSha256,
                    identifiers.NewId(),
                    identifiers.NewId(),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ReconciliationRecovery(
                reference.CommandId,
                "broker_reconciliation_claim_outcome_ambiguous");
        }
        catch (InvalidOperationException)
        {
            return new BrokerCommandReconciliationResult(
                BrokerCommandReconciliationOutcome.NotEligible,
                reference.CommandId,
                false,
                null,
                "broker_command_not_reconcilable",
                null);
        }
        catch (Exception)
        {
            return ReconciliationRecovery(
                reference.CommandId,
                "broker_reconciliation_claim_store_failed");
        }

        if (claim is null)
        {
            return ReconciliationRecovery(
                reference.CommandId,
                "broker_reconciliation_claim_missing");
        }

        DateTimeOffset now = claim.Command.Command.CreatedAtUtc;
        string? preflightFailure;
        if (!TryGetConservativeAuthorityNow(
                claim.AuthorityNowUtc,
                reconciliationClaimStartedTimestamp,
                out now))
        {
            preflightFailure = "broker_reconciliation_authority_clock_invalid";
        }
        else
        {
            try
            {
                preflightFailure = BrokerCommandReconciliationGuard.RejectReason(
                    context,
                    reference,
                    claim,
                    leaseTrustVerifier,
                    now);
            }
            catch (Exception)
            {
                preflightFailure = "broker_reconciliation_claim_invalid";
            }
        }

        bool gatewayInvoked = false;
        BrokerCommandReconciliationObservation observation;
        DateTimeOffset receivedAt;
        if (preflightFailure is not null)
        {
            receivedAt = now;
            observation = FailureObservation(
                claim,
                receivedAt,
                preflightFailure);
        }
        else
        {
            TimeSpan remaining = Min(
                options.GatewayReconciliationTimeout,
                claim.ClaimExpiresAtUtc - now,
                claim.MustCompleteByUtc - now);
            using var gatewayCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            gatewayCancellation.CancelAfter(remaining);
            try
            {
                gatewayInvoked = true;
                GatewayOperationResult<BrokerReconciliationSnapshot> gatewayResult = await gateway
                    .ReconcileAsync([reference.CommandId], gatewayCancellation.Token)
                    .WaitAsync(remaining, cancellationToken)
                    .ConfigureAwait(false);
                receivedAt = ConservativeAuthorityNowOr(
                    claim.AuthorityNowUtc,
                    reconciliationClaimStartedTimestamp,
                    now);
                observation = gatewayResult is { IsSuccess: true, Value: not null }
                    ? SnapshotObservation(claim, gatewayResult.Value, receivedAt)
                    : FailureObservation(
                        claim,
                        receivedAt,
                        "broker_reconciliation_gateway_unavailable");
            }
            catch (Exception)
            {
                receivedAt = ConservativeAuthorityNowOr(
                    claim.AuthorityNowUtc,
                    reconciliationClaimStartedTimestamp,
                    now);
                observation = FailureObservation(
                    claim,
                    receivedAt,
                    "broker_reconciliation_gateway_outcome_unknown");
            }
        }

        receivedAt = BrokerCommandLifecycleEvidence.NormalizeUtcTimestamp(receivedAt);
        ValidatedBrokerCommandReconciliation evidence =
            BrokerCommandReconciliationValidator.Validate(claim, observation, receivedAt);
        BrokerCommandLifecycleReceipt receipt;
        try
        {
            using CancellationTokenSource durableWrite = DurableWriteToken();
            receipt = await store.CompleteReconciliationAsync(
                    context,
                    claim.ClaimToken,
                    identifiers.NewId(),
                    evidence,
                    identifiers.NewId(),
                    durableWrite.Token)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            return ReconciliationRecovery(
                reference.CommandId,
                "broker_reconciliation_persistence_unconfirmed",
                gatewayInvoked);
        }

        if (!BrokerCommandLifecycleReceiptValidator.IsReconciliationReceipt(
                receipt,
                claim,
                evidence))
        {
            return ReconciliationRecovery(
                reference.CommandId,
                "broker_reconciliation_receipt_invalid",
                gatewayInvoked);
        }

        return new BrokerCommandReconciliationResult(
            evidence.IsConclusive
                ? BrokerCommandReconciliationOutcome.Completed
                : BrokerCommandReconciliationOutcome.InconclusiveRetryable,
            reference.CommandId,
            gatewayInvoked,
            evidence.Match,
            evidence.ReasonCode,
            receipt.State);
    }

    private static BrokerCommandReconciliationObservation SnapshotObservation(
        BrokerCommandReconciliationClaim claim,
        BrokerReconciliationSnapshot snapshot,
        DateTimeOffset receivedAtUtc)
    {
        receivedAtUtc = BrokerCommandLifecycleEvidence.NormalizeUtcTimestamp(receivedAtUtc);
        snapshot = BrokerCommandLifecycleEvidence.NormalizeSnapshot(snapshot);
        DateTimeOffset windowStart = snapshot.QueryWindowStartUtc.Offset == TimeSpan.Zero
            ? snapshot.QueryWindowStartUtc
            : claim.QueryWindowStartUtc;
        DateTimeOffset windowEnd = snapshot.QueryWindowEndUtc.Offset == TimeSpan.Zero
            ? snapshot.QueryWindowEndUtc
            : receivedAtUtc;
        var sourceDocument = new BrokerCommandReconciliationValidator
            .BrokerReconciliationSourceDocument(
                snapshot.SourceSequence,
                windowStart,
                windowEnd,
                snapshot);
        return new BrokerCommandReconciliationObservation(
            snapshot.SourceSequence,
            CanonicalJson.Sha256(sourceDocument),
            windowStart,
            windowEnd,
            snapshot);
    }

    private static BrokerCommandReconciliationObservation FailureObservation(
        BrokerCommandReconciliationClaim claim,
        DateTimeOffset observedAtUtc,
        string code)
    {
        observedAtUtc = BrokerCommandLifecycleEvidence.NormalizeUtcTimestamp(observedAtUtc);
        var failure = new BrokerReconciliationFailureDocument(
            claim.Command.Command.CommandId,
            claim.Command.AuthorizationSha256,
            claim.ScopeSha256,
            claim.Attempt,
            claim.QueryWindowStartUtc,
            observedAtUtc,
            code);
        return new BrokerCommandReconciliationObservation(
            null,
            CanonicalJson.Sha256(failure),
            claim.QueryWindowStartUtc,
            observedAtUtc,
            null);
    }

    private static GatewaySendResult NormalizeGatewayResult(
        GatewaySendResult? result,
        AuthorizedBrokerCommand capability,
        DateTimeOffset receivedAtUtc)
    {
        if (result?.Disposition is GatewayCommandDisposition.Rejected
            or GatewayCommandDisposition.SubmissionDisabled)
        {
            return new GatewaySendResult(
                GatewayCommandDisposition.Unknown,
                "broker_command_gateway_outcome_unproven",
                null,
                null,
                null,
                receivedAtUtc,
                false);
        }

        if (result is null
            || result.Disposition is not (GatewayCommandDisposition.Accepted
                or GatewayCommandDisposition.Unknown)
            || !BrokerCommandLifecycleEvidence.IsCanonicalCode(result.Code)
            || result.ObservedAtUtc.Offset != TimeSpan.Zero
            || result.ObservedAtUtc < capability.Command.CreatedAtUtc
            || result.ObservedAtUtc > receivedAtUtc
            || !ValidOptionalBrokerId(result.BrokerRequestId)
            || !ValidOptionalBrokerId(result.OrderId)
            || !ValidOptionalBrokerId(result.DealId)
            || (result.Disposition == GatewayCommandDisposition.Accepted
                && result.BrokerRequestId is null
                && result.OrderId is null
                && result.DealId is null)
            || (capability.Command.Action is BrokerCommandAction.Cancel
                    or BrokerCommandAction.ModifyProtection
                && result.DealId is not null)
            || (capability.Command.TargetKind == BrokerCommandTargetKind.PendingOrder
                && capability.Command.Action is BrokerCommandAction.Cancel
                    or BrokerCommandAction.ModifyProtection
                && result.OrderId is not null
                && result.OrderId != capability.Command.TargetBrokerId))
        {
            return new GatewaySendResult(
                GatewayCommandDisposition.Unknown,
                "broker_command_gateway_result_invalid",
                null,
                null,
                null,
                receivedAtUtc,
                false);
        }

        return result with { PreInvocationNotSentProven = false };
    }

    private CancellationTokenSource DurableWriteToken()
    {
        var source = new CancellationTokenSource();
        source.CancelAfter(options.DurableWriteTimeout);
        return source;
    }

    private bool TryGetConservativeAuthorityNow(
        DateTimeOffset authorityNowUtc,
        long operationStartedTimestamp,
        out DateTimeOffset currentAuthorityUtc)
    {
        currentAuthorityUtc = authorityNowUtc;
        if (authorityNowUtc.Offset != TimeSpan.Zero)
        {
            return false;
        }

        try
        {
            TimeSpan elapsed = timeProvider.GetElapsedTime(
                operationStartedTimestamp,
                timeProvider.GetTimestamp());
            if (elapsed < TimeSpan.Zero || elapsed > TimeSpan.FromHours(1))
            {
                return false;
            }

            currentAuthorityUtc = authorityNowUtc.Add(elapsed);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private DateTimeOffset ConservativeAuthorityNowOr(
        DateTimeOffset authorityNowUtc,
        long operationStartedTimestamp,
        DateTimeOffset fallbackUtc) =>
        TryGetConservativeAuthorityNow(
            authorityNowUtc,
            operationStartedTimestamp,
            out DateTimeOffset currentAuthorityUtc)
            ? currentAuthorityUtc
            : fallbackUtc;

    private bool TryGetElapsedTime(long startedTimestamp, out TimeSpan elapsed)
    {
        try
        {
            elapsed = timeProvider.GetElapsedTime(
                startedTimestamp,
                timeProvider.GetTimestamp());
            return elapsed >= TimeSpan.Zero && elapsed <= TimeSpan.FromHours(1);
        }
        catch (ArgumentOutOfRangeException)
        {
            elapsed = TimeSpan.Zero;
            return false;
        }
        catch (OverflowException)
        {
            elapsed = TimeSpan.Zero;
            return false;
        }
    }

    private GatewaySendResult GatewayTimeoutUnknown(
        BrokerCommandDispatchClaim claim,
        long dispatchClaimStartedTimestamp,
        DateTimeOffset fallbackUtc) => new(
            GatewayCommandDisposition.Unknown,
            "broker_command_gateway_timeout_unknown",
            null,
            null,
            null,
            ConservativeAuthorityNowOr(
                claim.AuthorityNowUtc,
                dispatchClaimStartedTimestamp,
                fallbackUtc),
            false);

    private static async Task ObserveGatewayCompletionAsync(Task<GatewaySendResult> send)
    {
        try
        {
            await send.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The durable outcome is already Unknown. Observation prevents an
            // eventual fault from escaping through the unobserved-task channel.
        }
    }

    private static void EnsureContext(TenantExecutionContext context, Guid commandId)
    {
        if (context.CorrelationId != commandId)
        {
            throw new ArgumentException(
                "The tenant correlation identifier must equal the broker command identifier.",
                nameof(context));
        }
    }

    private static BrokerCommandDispatchResult DispatchRecovery(Guid commandId, string code) =>
        new(
            BrokerCommandDispatchOutcome.DurableRecoveryRequired,
            commandId,
            false,
            null,
            code,
            null);

    private static BrokerCommandReconciliationResult ReconciliationRecovery(
        Guid commandId,
        string code,
        bool gatewayInvoked = false) => new(
            BrokerCommandReconciliationOutcome.DurableRecoveryRequired,
            commandId,
            gatewayInvoked,
            null,
            code,
            null);

    private static TimeSpan Min(params TimeSpan[] values) => values.Min();

    private static bool ValidOptionalBrokerId(string? value) =>
        value is null || BrokerCommandLifecycleEvidence.IsCanonicalBoundedText(value, 200);

    private sealed record BrokerReconciliationFailureDocument(
        Guid CommandId,
        string AuthorizationSha256,
        string ScopeSha256,
        int Attempt,
        DateTimeOffset WindowStartUtc,
        DateTimeOffset ObservedAtUtc,
        string Code);
}
