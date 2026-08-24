using YO4X.ControlPlane.Workers.Operations;
using YO4X.ControlPlane.Workers.Outbox;

namespace YO4X.ControlPlane.Workers;

internal enum RequiredWorkerWorkstream
{
    OutboxDispatch = 0,
    ControlWork = 1
}

internal enum RequiredWorkstreamState
{
    Starting = 0,
    DependenciesUnverified = 1,
    Ready = 2,
    DependencyUnavailable = 3,
    Degraded = 4,
    Stopped = 5
}

internal readonly record struct RequiredWorkstreamSnapshot(
    bool Started,
    RequiredWorkstreamState State,
    int DetailCondition,
    string PublicCode,
    DateTimeOffset? HealthyAtUtc)
{
    internal static RequiredWorkstreamSnapshot Starting =>
        new(false, RequiredWorkstreamState.Starting, 0, "startup_incomplete", null);
}

/// <summary>
/// Composes the health of every required hosted workstream. All transitions and
/// reads share one lock so an aggregate health response is linearizable with
/// concurrent workstream updates.
/// </summary>
public sealed class WorkerReadiness
{
    private const string ContractVersion = "worker-health.v1";
    private const string Role = "control-plane-workers";
    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider;
    private readonly WorkerReadinessOptions _options;
    private readonly RequiredWorkstreamSnapshot[] _workstreams =
    [
        RequiredWorkstreamSnapshot.Starting,
        RequiredWorkstreamSnapshot.Starting
    ];

    public WorkerReadiness(
        TimeProvider timeProvider,
        WorkerReadinessOptions options)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _timeProvider = timeProvider;
        _options = options;
    }

    public WorkerHealthSnapshot GetLive() =>
        new(ContractVersion, Role, true, "live", "process_live");

    /// <summary>
    /// Startup is complete only while every required workstream has entered its
    /// run loop. A terminally stopped workstream fails startup closed. Runtime
    /// dependency degradation remains a readiness concern after startup.
    /// </summary>
    public WorkerHealthSnapshot GetStartup()
    {
        lock (_sync)
        {
            if (AnyState(RequiredWorkstreamState.Stopped))
            {
                return Unhealthy("starting", "worker_stopped");
            }

            return AllStarted()
                ? new WorkerHealthSnapshot(ContractVersion, Role, true, "started", "startup_complete")
                : Unhealthy("starting", "startup_incomplete");
        }
    }

    /// <summary>
    /// Readiness is healthy only when every required workstream has completed a
    /// healthy cycle. Recovery is explicit: the failed workstream must report a
    /// later complete healthy cycle; activity from another workstream cannot
    /// overwrite its state.
    /// </summary>
    public WorkerHealthSnapshot GetReady()
    {
        lock (_sync)
        {
            if (AnyState(RequiredWorkstreamState.Stopped))
            {
                return Unhealthy("not-ready", "worker_stopped");
            }

            if (!AllStarted())
            {
                return Unhealthy("not-ready", "startup_incomplete");
            }

            if (AnyState(RequiredWorkstreamState.DependencyUnavailable))
            {
                return Unhealthy("not-ready", "required_dependency_unavailable");
            }

            for (int index = 0; index < _workstreams.Length; index++)
            {
                if (_workstreams[index].State == RequiredWorkstreamState.Degraded)
                {
                    return Unhealthy("not-ready", _workstreams[index].PublicCode);
                }
            }

            if (AnyState(RequiredWorkstreamState.DependenciesUnverified))
            {
                return Unhealthy("not-ready", "required_dependencies_unverified");
            }

            DateTimeOffset now = _timeProvider.GetUtcNow();
            for (int index = 0; index < _workstreams.Length; index++)
            {
                DateTimeOffset? healthyAt = _workstreams[index].HealthyAtUtc;
                if (_workstreams[index].State == RequiredWorkstreamState.Ready
                    && (healthyAt is null
                        || now < healthyAt.Value
                        || now - healthyAt.Value > _options.MaximumHealthyAge))
                {
                    return Unhealthy("not-ready", "required_workstream_heartbeat_stale");
                }
            }

            return AllState(RequiredWorkstreamState.Ready)
                ? new WorkerHealthSnapshot(
                    ContractVersion,
                    Role,
                    true,
                    "ready",
                    "required_workstreams_ready")
                : Unhealthy("not-ready", "not_ready");
        }
    }

    internal int GetDetailCondition(RequiredWorkerWorkstream workstream)
    {
        lock (_sync)
        {
            return _workstreams[Index(workstream)].DetailCondition;
        }
    }

    internal void MarkStarted(
        RequiredWorkerWorkstream workstream,
        int dependenciesUnverifiedCondition,
        string publicCode)
    {
        lock (_sync)
        {
            int index = Index(workstream);
            RequiredWorkstreamSnapshot current = _workstreams[index];
            if (current.State == RequiredWorkstreamState.Stopped || current.Started)
            {
                return;
            }

            _workstreams[index] = current.State == RequiredWorkstreamState.Starting
                ? new RequiredWorkstreamSnapshot(
                    true,
                    RequiredWorkstreamState.DependenciesUnverified,
                    dependenciesUnverifiedCondition,
                    publicCode,
                    null)
                : current with { Started = true };
        }
    }

    internal void MarkReady(
        RequiredWorkerWorkstream workstream,
        int readyCondition,
        string publicCode)
    {
        lock (_sync)
        {
            int index = Index(workstream);
            RequiredWorkstreamSnapshot current = _workstreams[index];
            if (!current.Started || current.State == RequiredWorkstreamState.Stopped)
            {
                return;
            }

            _workstreams[index] = new RequiredWorkstreamSnapshot(
                true,
                RequiredWorkstreamState.Ready,
                readyCondition,
                publicCode,
                _timeProvider.GetUtcNow());
        }
    }

    internal void MarkNotReady(
        RequiredWorkerWorkstream workstream,
        RequiredWorkstreamState state,
        int detailCondition,
        string publicCode)
    {
        if (state is not (RequiredWorkstreamState.DependencyUnavailable or RequiredWorkstreamState.Degraded))
        {
            throw new ArgumentOutOfRangeException(nameof(state), "A recoverable failure state is required.");
        }

        lock (_sync)
        {
            int index = Index(workstream);
            RequiredWorkstreamSnapshot current = _workstreams[index];
            if (current.State == RequiredWorkstreamState.Stopped)
            {
                return;
            }

            _workstreams[index] = new RequiredWorkstreamSnapshot(
                current.Started,
                state,
                detailCondition,
                publicCode,
                null);
        }
    }

    internal void MarkStopped(
        RequiredWorkerWorkstream workstream,
        int stoppedCondition)
    {
        lock (_sync)
        {
            int index = Index(workstream);
            RequiredWorkstreamSnapshot current = _workstreams[index];
            _workstreams[index] = new RequiredWorkstreamSnapshot(
                current.Started,
                RequiredWorkstreamState.Stopped,
                stoppedCondition,
                "worker_stopped",
                null);
        }
    }

    private static int Index(RequiredWorkerWorkstream workstream) => workstream switch
    {
        RequiredWorkerWorkstream.OutboxDispatch => 0,
        RequiredWorkerWorkstream.ControlWork => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(workstream))
    };

    private bool AllStarted()
    {
        for (int index = 0; index < _workstreams.Length; index++)
        {
            if (!_workstreams[index].Started)
            {
                return false;
            }
        }

        return true;
    }

    private bool AnyState(RequiredWorkstreamState state)
    {
        for (int index = 0; index < _workstreams.Length; index++)
        {
            if (_workstreams[index].State == state)
            {
                return true;
            }
        }

        return false;
    }

    private bool AllState(RequiredWorkstreamState state)
    {
        for (int index = 0; index < _workstreams.Length; index++)
        {
            if (_workstreams[index].State != state)
            {
                return false;
            }
        }

        return true;
    }

    private static WorkerHealthSnapshot Unhealthy(string state, string code) =>
        new(ContractVersion, Role, false, state, code);
}
