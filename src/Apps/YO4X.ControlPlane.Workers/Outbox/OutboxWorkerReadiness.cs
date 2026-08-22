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
    Stopped
}

public sealed class OutboxWorkerReadiness
{
    private const string ContractVersion = "worker-health.v1";
    private const string Role = "control-plane-workers";
    private int _condition = (int)OutboxReadinessCondition.Starting;
    private int _started;

    public OutboxReadinessCondition Condition =>
        (OutboxReadinessCondition)Volatile.Read(ref _condition);

    public WorkerHealthSnapshot GetLive() =>
        new(ContractVersion, Role, true, "live", "process_live");

    public WorkerHealthSnapshot GetStartup()
    {
        bool started = Volatile.Read(ref _started) == 1;
        return started
            ? new WorkerHealthSnapshot(ContractVersion, Role, true, "started", "startup_complete")
            : new WorkerHealthSnapshot(ContractVersion, Role, false, "starting", "startup_incomplete");
    }

    public WorkerHealthSnapshot GetReady()
    {
        OutboxReadinessCondition condition = Condition;
        return condition == OutboxReadinessCondition.Ready
            ? new WorkerHealthSnapshot(ContractVersion, Role, true, "ready", "dispatch_dependencies_ready")
            : new WorkerHealthSnapshot(ContractVersion, Role, false, "not-ready", PublicCode(condition));
    }

    public void MarkStarted()
    {
        Volatile.Write(ref _started, 1);
        Volatile.Write(ref _condition, (int)OutboxReadinessCondition.DependenciesUnverified);
    }

    public void MarkReady() =>
        Volatile.Write(ref _condition, (int)OutboxReadinessCondition.Ready);

    public void MarkNotReady(OutboxReadinessCondition condition)
    {
        if (condition is OutboxReadinessCondition.Ready or OutboxReadinessCondition.Starting)
        {
            throw new ArgumentOutOfRangeException(nameof(condition), "A failure condition is required.");
        }

        Volatile.Write(ref _condition, (int)condition);
    }

    public void MarkStopped() =>
        Volatile.Write(ref _condition, (int)OutboxReadinessCondition.Stopped);

    private static string PublicCode(OutboxReadinessCondition condition) => condition switch
    {
        OutboxReadinessCondition.Starting => "startup_incomplete",
        OutboxReadinessCondition.DependenciesUnverified => "required_dependencies_unverified",
        OutboxReadinessCondition.PostgresUnavailable or
        OutboxReadinessCondition.DestinationUnavailable => "required_dependency_unavailable",
        OutboxReadinessCondition.StoreOperationFailed or
        OutboxReadinessCondition.DestinationOperationFailed or
        OutboxReadinessCondition.StoreContractViolation => "dispatch_degraded",
        OutboxReadinessCondition.Stopped => "worker_stopped",
        _ => "not_ready"
    };
}
