namespace YO4X.ControlPlane.Workers.Operations;

public enum ControlWorkReadinessCondition
{
    Starting,
    DependenciesUnverified,
    Ready,
    RequiredDependencyUnavailable,
    PartialCycleFailure,
    ScanProgressLagging,
    StoreOperationFailed,
    Stopped,
    OperationBacklogLagging
}

public sealed class ControlWorkReadiness
{
    private readonly WorkerReadiness _aggregate;

    public ControlWorkReadiness(WorkerReadiness aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _aggregate = aggregate;
    }

    public ControlWorkReadinessCondition Condition =>
        (ControlWorkReadinessCondition)_aggregate.GetDetailCondition(RequiredWorkerWorkstream.ControlWork);

    public void MarkStarted()
    {
        _aggregate.MarkStarted(
            RequiredWorkerWorkstream.ControlWork,
            (int)ControlWorkReadinessCondition.DependenciesUnverified,
            "required_dependencies_unverified");
    }

    /// <summary>
    /// Clears a recoverable control-work failure only after a complete cycle in
    /// which every required dependency and store operation succeeded.
    /// </summary>
    public void MarkReady()
    {
        _aggregate.MarkReady(
            RequiredWorkerWorkstream.ControlWork,
            (int)ControlWorkReadinessCondition.Ready,
            "control_work_ready");
    }

    public void MarkNotReady(ControlWorkReadinessCondition condition)
    {
        if (condition is ControlWorkReadinessCondition.Ready or
            ControlWorkReadinessCondition.Starting or
            ControlWorkReadinessCondition.DependenciesUnverified or
            ControlWorkReadinessCondition.Stopped)
        {
            throw new ArgumentOutOfRangeException(nameof(condition), "A failure condition is required.");
        }

        RequiredWorkstreamState state = condition == ControlWorkReadinessCondition.RequiredDependencyUnavailable
            ? RequiredWorkstreamState.DependencyUnavailable
            : RequiredWorkstreamState.Degraded;
        _aggregate.MarkNotReady(
            RequiredWorkerWorkstream.ControlWork,
            state,
            (int)condition,
            condition switch
            {
                ControlWorkReadinessCondition.RequiredDependencyUnavailable =>
                    "required_dependency_unavailable",
                ControlWorkReadinessCondition.ScanProgressLagging =>
                    "tenant_scan_rotation_stale",
                ControlWorkReadinessCondition.OperationBacklogLagging =>
                    "user_operation_backlog_stale",
                _ => "control_work_degraded"
            });
    }

    public void MarkStopped()
    {
        _aggregate.MarkStopped(
            RequiredWorkerWorkstream.ControlWork,
            (int)ControlWorkReadinessCondition.Stopped);
    }
}
