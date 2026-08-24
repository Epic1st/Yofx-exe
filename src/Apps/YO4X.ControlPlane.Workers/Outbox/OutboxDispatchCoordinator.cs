using System.Text;

namespace YO4X.ControlPlane.Workers.Outbox;

public enum OutboxDispatchCycleOutcome
{
    Completed,
    PostgresUnavailable,
    DestinationUnavailable,
    StoreOperationFailed,
    StoreContractViolation,
    ScanProgressLagging
}

public sealed record OutboxDispatchCycleResult(
    OutboxDispatchCycleOutcome Outcome,
    int Claimed,
    int Published,
    int ScheduledForRetry,
    int DeadLettered);

public sealed class OutboxDispatchCoordinator
{
    private readonly IPostgresOutboxStore _store;
    private readonly IOutboxDestination _destination;
    private readonly OutboxDispatchOptions _options;
    private readonly OutboxWorkerIdentity _identity;
    private readonly OutboxWorkerReadiness _readiness;
    private readonly RetrySchedule _retrySchedule;
    private readonly TimeProvider _timeProvider;

    public OutboxDispatchCoordinator(
        IPostgresOutboxStore store,
        IOutboxDestination destination,
        OutboxDispatchOptions options,
        OutboxWorkerIdentity identity,
        OutboxWorkerReadiness readiness,
        RetrySchedule retrySchedule,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(retrySchedule);
        ArgumentNullException.ThrowIfNull(timeProvider);
        options.Validate();

        _store = store;
        _destination = destination;
        _options = options;
        _identity = identity;
        _readiness = readiness;
        _retrySchedule = retrySchedule;
        _timeProvider = timeProvider;
    }

    public async Task<OutboxDispatchCycleResult> RunCycleAsync(CancellationToken cancellationToken)
    {
        if (_readiness.Condition == OutboxReadinessCondition.Stopped)
        {
            throw new WorkerWorkstreamStoppedException();
        }

        if (!await ProbeStoreAsync(cancellationToken).ConfigureAwait(false))
        {
            _readiness.MarkNotReady(OutboxReadinessCondition.PostgresUnavailable);
            return Empty(OutboxDispatchCycleOutcome.PostgresUnavailable);
        }

        if (!await ProbeDestinationAsync(cancellationToken).ConfigureAwait(false))
        {
            _readiness.MarkNotReady(OutboxReadinessCondition.DestinationUnavailable);
            return Empty(OutboxDispatchCycleOutcome.DestinationUnavailable);
        }

        IReadOnlyList<ClaimedOutboxItem> claimed;
        try
        {
            var request = new OutboxClaimRequest(
                _identity.Value,
                _options.BatchSize,
                _timeProvider.GetUtcNow(),
                _options.ClaimLease).Validate();
            claimed = await WorkerOperationBoundary.ExecuteAsync(
                    token => _store.ClaimAsync(request, token),
                    _options.DependencyTimeout,
                    _options.CancellationConfirmationTimeout,
                    _timeProvider,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (WorkerOperationTerminationUnconfirmedException)
        {
            _readiness.MarkStopped();
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            _readiness.MarkNotReady(OutboxReadinessCondition.StoreOperationFailed);
            return Empty(OutboxDispatchCycleOutcome.StoreOperationFailed);
        }

        if (claimed is null || claimed.Count > _options.BatchSize)
        {
            _readiness.MarkNotReady(OutboxReadinessCondition.StoreContractViolation);
            return new OutboxDispatchCycleResult(
                OutboxDispatchCycleOutcome.StoreContractViolation,
                claimed?.Count ?? 0,
                0,
                0,
                0);
        }

        int published = 0;
        int scheduledForRetry = 0;
        int deadLettered = 0;
        for (int index = 0; index < claimed.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClaimedOutboxItem item = claimed[index];
            if (Encoding.UTF8.GetByteCount(item.PayloadJson) > _options.MaximumPayloadBytes
                || !PayloadHash.Matches(item.PayloadJson, item.PayloadSha256))
            {
                bool settled = await SettleFailureAsync(
                    item,
                    "invalid_or_oversized_payload",
                    permanent: true,
                    cancellationToken).ConfigureAwait(false);
                if (!settled)
                {
                    return StoreFailure(claimed.Count, published, scheduledForRetry, deadLettered);
                }

                deadLettered++;
                continue;
            }

            OutboxDeliveryResult delivery;
            try
            {
                OutboxDeliveryEnvelope envelope = OutboxDeliveryEnvelope.Create(item);
                delivery = await WorkerOperationBoundary.ExecuteAsync(
                        token => _destination.DeliverAsync(envelope, token),
                        _options.DeliveryTimeout,
                        _options.CancellationConfirmationTimeout,
                        _timeProvider,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (delivery is null)
                {
                    throw new InvalidOperationException("The destination returned no delivery outcome.");
                }
            }
            catch (WorkerOperationTerminationUnconfirmedException)
            {
                _readiness.MarkStopped();
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                delivery = OutboxDeliveryResult.DestinationUnavailable("destination_operation_failed");
            }

            switch (delivery.Outcome)
            {
                case OutboxDeliveryOutcome.Accepted:
                case OutboxDeliveryOutcome.Duplicate:
                    if (!await SettlePublishedAsync(item, cancellationToken).ConfigureAwait(false))
                    {
                        return StoreFailure(claimed.Count, published, scheduledForRetry, deadLettered);
                    }

                    published++;
                    break;

                case OutboxDeliveryOutcome.PermanentFailure:
                    if (!await SettleFailureAsync(item, delivery.Code, permanent: true, cancellationToken)
                            .ConfigureAwait(false))
                    {
                        return StoreFailure(claimed.Count, published, scheduledForRetry, deadLettered);
                    }

                    deadLettered++;
                    break;

                case OutboxDeliveryOutcome.RetryableFailure:
                    if (!await SettleFailureAsync(item, delivery.Code, permanent: false, cancellationToken)
                            .ConfigureAwait(false))
                    {
                        return StoreFailure(claimed.Count, published, scheduledForRetry, deadLettered);
                    }

                    IncrementFailureCount(item, ref scheduledForRetry, ref deadLettered);
                    break;

                case OutboxDeliveryOutcome.Unavailable:
                    if (!await SettleFailureAsync(item, delivery.Code, permanent: false, cancellationToken)
                            .ConfigureAwait(false))
                    {
                        return StoreFailure(claimed.Count, published, scheduledForRetry, deadLettered);
                    }

                    IncrementFailureCount(item, ref scheduledForRetry, ref deadLettered);
                    for (int remaining = index + 1; remaining < claimed.Count; remaining++)
                    {
                        ClaimedOutboxItem unprocessed = claimed[remaining];
                        if (!await SettleFailureAsync(
                                unprocessed,
                                "destination_unavailable",
                                permanent: false,
                                cancellationToken).ConfigureAwait(false))
                        {
                            return StoreFailure(claimed.Count, published, scheduledForRetry, deadLettered);
                        }

                        IncrementFailureCount(unprocessed, ref scheduledForRetry, ref deadLettered);
                    }

                    _readiness.MarkNotReady(OutboxReadinessCondition.DestinationOperationFailed);
                    return new OutboxDispatchCycleResult(
                        OutboxDispatchCycleOutcome.DestinationUnavailable,
                        claimed.Count,
                        published,
                        scheduledForRetry,
                        deadLettered);

                default:
                    throw new InvalidOperationException("Unsupported outbox delivery outcome.");
            }
        }

        bool scanProgressHealthy;
        try
        {
            scanProgressHealthy = await WorkerOperationBoundary.ExecuteAsync(
                    token => _store.IsScanProgressHealthyAsync(
                        _options.MaximumTenantScanRotationAge,
                        token),
                    _options.DependencyTimeout,
                    _options.CancellationConfirmationTimeout,
                    _timeProvider,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (WorkerOperationTerminationUnconfirmedException)
        {
            _readiness.MarkStopped();
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            _readiness.MarkNotReady(OutboxReadinessCondition.StoreOperationFailed);
            return new OutboxDispatchCycleResult(
                OutboxDispatchCycleOutcome.StoreOperationFailed,
                claimed.Count,
                published,
                scheduledForRetry,
                deadLettered);
        }

        if (!scanProgressHealthy)
        {
            _readiness.MarkNotReady(OutboxReadinessCondition.ScanProgressLagging);
            return new OutboxDispatchCycleResult(
                OutboxDispatchCycleOutcome.ScanProgressLagging,
                claimed.Count,
                published,
                scheduledForRetry,
                deadLettered);
        }

        _readiness.MarkReady();
        return new OutboxDispatchCycleResult(
            OutboxDispatchCycleOutcome.Completed,
            claimed.Count,
            published,
            scheduledForRetry,
            deadLettered);
    }

    private async Task<bool> ProbeStoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await WorkerOperationBoundary.ExecuteAsync(
                    _store.IsAvailableAsync,
                    _options.DependencyTimeout,
                    _options.CancellationConfirmationTimeout,
                    _timeProvider,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (WorkerOperationTerminationUnconfirmedException)
        {
            _readiness.MarkStopped();
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task<bool> ProbeDestinationAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await WorkerOperationBoundary.ExecuteAsync(
                    _destination.IsAvailableAsync,
                    _options.DependencyTimeout,
                    _options.CancellationConfirmationTimeout,
                    _timeProvider,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (WorkerOperationTerminationUnconfirmedException)
        {
            _readiness.MarkStopped();
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task<bool> SettlePublishedAsync(
        ClaimedOutboxItem item,
        CancellationToken cancellationToken)
    {
        var settlement = new OutboxSettlement(
            item.MessageId,
            item.TenantId,
            _identity.Value,
            OutboxSettlementKind.Published,
            _timeProvider.GetUtcNow(),
            null,
            "published").Validate();
        return await TrySettleAsync(settlement, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> SettleFailureAsync(
        ClaimedOutboxItem item,
        string code,
        bool permanent,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        bool deadLetter = permanent || item.Attempt >= _options.MaximumAttempts;
        var settlement = new OutboxSettlement(
            item.MessageId,
            item.TenantId,
            _identity.Value,
            deadLetter ? OutboxSettlementKind.DeadLetter : OutboxSettlementKind.Retry,
            now,
            deadLetter ? null : now.Add(_retrySchedule.GetDelay(item.MessageId, item.Attempt)),
            code).Validate();
        return await TrySettleAsync(settlement, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> TrySettleAsync(
        OutboxSettlement settlement,
        CancellationToken cancellationToken)
    {
        try
        {
            return await WorkerOperationBoundary.ExecuteAsync(
                    token => _store.SettleAsync(settlement, token),
                    _options.DependencyTimeout,
                    _options.CancellationConfirmationTimeout,
                    _timeProvider,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (WorkerOperationTerminationUnconfirmedException)
        {
            _readiness.MarkStopped();
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void IncrementFailureCount(
        ClaimedOutboxItem item,
        ref int scheduledForRetry,
        ref int deadLettered)
    {
        if (item.Attempt >= _options.MaximumAttempts)
        {
            deadLettered++;
        }
        else
        {
            scheduledForRetry++;
        }
    }

    private OutboxDispatchCycleResult StoreFailure(
        int claimed,
        int published,
        int scheduledForRetry,
        int deadLettered)
    {
        _readiness.MarkNotReady(OutboxReadinessCondition.StoreOperationFailed);
        return new OutboxDispatchCycleResult(
            OutboxDispatchCycleOutcome.StoreOperationFailed,
            claimed,
            published,
            scheduledForRetry,
            deadLettered);
    }

    private static OutboxDispatchCycleResult Empty(OutboxDispatchCycleOutcome outcome) =>
        new(outcome, 0, 0, 0, 0);
}
