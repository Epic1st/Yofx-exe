using YO4X.Tenancy;
using YO4X.Trading.Abstractions;
using YO4X.Trading.Application;

namespace YO4X.GatewayHost;

internal enum BrokerCommandOneShotOutcome
{
    NoSubmissionRecorded = 0,
    ReconciliationCompleted = 1,
    ReconciliationPending = 2,
    Failed = 3
}

internal interface IBrokerCommandOneShotExecutor
{
    Task<BrokerCommandOneShotOutcome> ExecuteAsync(CancellationToken cancellationToken);
}

internal interface IBrokerCommandCoordinatorRunner
{
    Task<BrokerCommandDispatchResult> DispatchAsync(
        TenantExecutionContext context,
        BrokerCommandReference reference,
        CancellationToken cancellationToken);

    Task<BrokerCommandReconciliationResult> ReconcileAsync(
        TenantExecutionContext context,
        BrokerCommandReference reference,
        CancellationToken cancellationToken);
}

internal interface IBrokerCommandClaimRecoveryWaiter
{
    Task<bool> WaitAsync(CancellationToken cancellationToken);
}

internal sealed class BrokerCommandClaimRecoveryWaiter : IBrokerCommandClaimRecoveryWaiter
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    public async Task<bool> WaitAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}

internal sealed class BrokerCommandCoordinatorRunner(BrokerCommandCoordinator coordinator)
    : IBrokerCommandCoordinatorRunner
{
    private readonly BrokerCommandCoordinator coordinator = coordinator
        ?? throw new ArgumentNullException(nameof(coordinator));

    public Task<BrokerCommandDispatchResult> DispatchAsync(
        TenantExecutionContext context,
        BrokerCommandReference reference,
        CancellationToken cancellationToken) =>
        coordinator.DispatchAsync(context, reference, cancellationToken);

    public Task<BrokerCommandReconciliationResult> ReconcileAsync(
        TenantExecutionContext context,
        BrokerCommandReference reference,
        CancellationToken cancellationToken) =>
        coordinator.ReconcileAsync(context, reference, cancellationToken);
}

internal sealed class DisabledBrokerCommandOneShotExecutor : IBrokerCommandOneShotExecutor
{
    public Task<BrokerCommandOneShotOutcome> ExecuteAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The disabled one-shot executor cannot run.");
}

internal sealed class BrokerCommandOneShotExecutor : IBrokerCommandOneShotExecutor
{
    private readonly BrokerCommandOneShotSettings settings;
    private readonly IBrokerCommandCoordinatorRunner coordinator;
    private readonly IBrokerCommandClaimRecoveryWaiter recoveryWaiter;

    internal BrokerCommandOneShotExecutor(
        BrokerCommandOneShotSettings settings,
        IBrokerCommandCoordinatorRunner coordinator)
        : this(settings, coordinator, new BrokerCommandClaimRecoveryWaiter())
    {
    }

    internal BrokerCommandOneShotExecutor(
        BrokerCommandOneShotSettings settings,
        IBrokerCommandCoordinatorRunner coordinator,
        IBrokerCommandClaimRecoveryWaiter recoveryWaiter)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        this.recoveryWaiter = recoveryWaiter
            ?? throw new ArgumentNullException(nameof(recoveryWaiter));
    }

    public async Task<BrokerCommandOneShotOutcome> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        if (!settings.Enabled
            || settings.ExecutionContext is null
            || settings.CommandReference is null)
        {
            return BrokerCommandOneShotOutcome.Failed;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.OverallTimeout);
        while (true)
        {
            BrokerCommandDispatchResult dispatch = await coordinator.DispatchAsync(
                    settings.ExecutionContext,
                    settings.CommandReference,
                    timeout.Token)
                .ConfigureAwait(false);
            if (dispatch.Outcome == BrokerCommandDispatchOutcome.SubmissionRecorded
                && dispatch.Disposition == GatewayCommandDisposition.SubmissionDisabled
                && !dispatch.GatewayInvoked
                && string.Equals(
                    dispatch.DurableState,
                    "submission_disabled",
                    StringComparison.Ordinal))
            {
                return BrokerCommandOneShotOutcome.NoSubmissionRecorded;
            }

            // A process can restart after an Accepted submission was durably
            // recorded, or after the durable send_in_progress claim committed
            // but before submission evidence was recorded. In the latter case
            // dispatch and reconciliation both fail closed until the claim
            // expires. Poll only inside the one-shot's bounded overall timeout;
            // dispatch cannot invoke the gateway while that marker is live, and
            // its first post-expiry call atomically recovers to Unknown.
            bool durableRestartCandidate =
                dispatch.Outcome == BrokerCommandDispatchOutcome.NoDispatchAuthority
                && !dispatch.GatewayInvoked
                && dispatch.Disposition is null;
            if (!dispatch.RequiresReconciliation && !durableRestartCandidate)
            {
                return BrokerCommandOneShotOutcome.Failed;
            }

            BrokerCommandReconciliationResult reconciliation =
                await coordinator.ReconcileAsync(
                        settings.ExecutionContext,
                        settings.CommandReference,
                        timeout.Token)
                    .ConfigureAwait(false);
            if (durableRestartCandidate
                && reconciliation.Outcome == BrokerCommandReconciliationOutcome.NotEligible)
            {
                if (!await recoveryWaiter.WaitAsync(timeout.Token).ConfigureAwait(false))
                {
                    return BrokerCommandOneShotOutcome.Failed;
                }

                continue;
            }

            return reconciliation.Outcome switch
            {
                BrokerCommandReconciliationOutcome.Completed =>
                    BrokerCommandOneShotOutcome.ReconciliationCompleted,
                BrokerCommandReconciliationOutcome.InconclusiveRetryable
                    or BrokerCommandReconciliationOutcome.DurableRecoveryRequired =>
                    BrokerCommandOneShotOutcome.ReconciliationPending,
                _ => BrokerCommandOneShotOutcome.Failed
            };
        }
    }
}

internal sealed class BrokerCommandOneShotWorker(
    BrokerCommandOneShotSettings settings,
    IBrokerCommandOneShotExecutor executor,
    GatewayHostRuntimeStatus runtimeStatus)
    : BackgroundService
{
    private readonly BrokerCommandOneShotSettings settings = settings
        ?? throw new ArgumentNullException(nameof(settings));
    private readonly IBrokerCommandOneShotExecutor executor = executor
        ?? throw new ArgumentNullException(nameof(executor));
    private readonly GatewayHostRuntimeStatus runtimeStatus = runtimeStatus
        ?? throw new ArgumentNullException(nameof(runtimeStatus));

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        RunOnceAsync(stoppingToken);

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!settings.Enabled)
        {
            return;
        }

        runtimeStatus.MarkRunning();
        try
        {
            BrokerCommandOneShotOutcome outcome = await executor.ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
            switch (outcome)
            {
                case BrokerCommandOneShotOutcome.NoSubmissionRecorded:
                    runtimeStatus.MarkNoSubmissionRecorded();
                    break;
                case BrokerCommandOneShotOutcome.ReconciliationCompleted:
                    runtimeStatus.MarkReconciliationCompleted();
                    break;
                case BrokerCommandOneShotOutcome.ReconciliationPending:
                    runtimeStatus.MarkReconciliationPending();
                    break;
                default:
                    runtimeStatus.MarkFailed();
                    break;
            }
        }
        catch (Exception)
        {
            // No exception details are logged: they may contain connection or
            // durable command material. The public status is deliberately fixed.
            runtimeStatus.MarkFailed();
        }
    }
}
