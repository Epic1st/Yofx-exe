namespace YO4X.ControlPlane.Workers.Outbox;

public enum OutboxReadinessCondition
{
    Starting,
    DependenciesUnverified,
    Ready,
    PostgresUnavailable,
    DestinationUnavailable,
    StoreOperationFailed,
    DestinationOperationFailed,
    StoreContractViolation,
    ScanProgressLagging,
    Stopped
}

public sealed class OutboxWorkerReadiness
{
    private readonly WorkerReadiness _aggregate;

    public OutboxWorkerReadiness(WorkerReadiness aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _aggregate = aggregate;
    }

    public OutboxReadinessCondition Condition =>
        (OutboxReadinessCondition)_aggregate.GetDetailCondition(RequiredWorkerWorkstream.OutboxDispatch);

    public void MarkStarted()
    {
        _aggregate.MarkStarted(
            RequiredWorkerWorkstream.OutboxDispatch,
            (int)OutboxReadinessCondition.DependenciesUnverified,
            PublicCode(OutboxReadinessCondition.DependenciesUnverified));
    }

    public void MarkReady()
    {
        _aggregate.MarkReady(
            RequiredWorkerWorkstream.OutboxDispatch,
            (int)OutboxReadinessCondition.Ready,
            "dispatch_dependencies_ready");
    }

    public void MarkNotReady(OutboxReadinessCondition condition)
    {
        if (condition is OutboxReadinessCondition.Ready or
            OutboxReadinessCondition.Starting or
            OutboxReadinessCondition.DependenciesUnverified or
            OutboxReadinessCondition.Stopped)
        {
            throw new ArgumentOutOfRangeException(nameof(condition), "A failure condition is required.");
        }

        RequiredWorkstreamState state = condition is
            OutboxReadinessCondition.PostgresUnavailable or
            OutboxReadinessCondition.DestinationUnavailable
                ? RequiredWorkstreamState.DependencyUnavailable
                : RequiredWorkstreamState.Degraded;
        _aggregate.MarkNotReady(
            RequiredWorkerWorkstream.OutboxDispatch,
            state,
            (int)condition,
            PublicCode(condition));
    }

    public void MarkStopped()
    {
        _aggregate.MarkStopped(
            RequiredWorkerWorkstream.OutboxDispatch,
            (int)OutboxReadinessCondition.Stopped);
    }

    private static string PublicCode(OutboxReadinessCondition condition) => condition switch
    {
        OutboxReadinessCondition.Starting => "startup_incomplete",
        OutboxReadinessCondition.DependenciesUnverified => "required_dependencies_unverified",
        OutboxReadinessCondition.PostgresUnavailable or
        OutboxReadinessCondition.DestinationUnavailable => "required_dependency_unavailable",
        OutboxReadinessCondition.StoreOperationFailed or
        OutboxReadinessCondition.DestinationOperationFailed or
        OutboxReadinessCondition.StoreContractViolation => "dispatch_degraded",
        OutboxReadinessCondition.ScanProgressLagging => "tenant_scan_rotation_stale",
        OutboxReadinessCondition.Stopped => "worker_stopped",
        _ => "not_ready"
    };
}
