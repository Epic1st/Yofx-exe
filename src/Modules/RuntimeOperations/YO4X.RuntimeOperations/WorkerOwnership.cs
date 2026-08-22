using YO4X.Runtime.Contracts;

namespace YO4X.RuntimeOperations;

public enum WorkerOwnershipState
{
    Free = 0,
    Held = 1,
    ReleaseAcknowledged = 2
}

public enum OwnershipAcquireCode
{
    Acquired = 0,
    AlreadyHeld = 1,
    LeaseExpirySafetyWindowActive = 2
}

public sealed record WorkerOwnershipSnapshot(
    Guid DeploymentId,
    Guid BrokerAccountId,
    long Version,
    long Generation,
    Guid? HolderWorkerInstanceId,
    WorkerOwnershipState State,
    DateTimeOffset? NotBeforeUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? ReleaseAcknowledgedAtUtc);

public sealed record OwnershipAcquireResult(
    OwnershipAcquireCode Code,
    string ReasonCode,
    WorkerOwnershipSnapshot Snapshot)
{
    public bool Acquired => Code == OwnershipAcquireCode.Acquired;
}

public enum OwnershipReleaseCode
{
    Released = 0,
    NotHeld = 1,
    WrongGeneration = 2,
    WrongHolder = 3
}

public sealed record OwnershipReleaseResult(
    OwnershipReleaseCode Code,
    string ReasonCode,
    WorkerOwnershipSnapshot Snapshot)
{
    public bool Released => Code == OwnershipReleaseCode.Released;
}

public enum OwnershipRenewCode
{
    Renewed = 0,
    NotHeld = 1,
    WrongGeneration = 2,
    WrongHolder = 3,
    AlreadyExpired = 4
}

public sealed record OwnershipRenewResult(
    OwnershipRenewCode Code,
    string ReasonCode,
    WorkerOwnershipSnapshot Snapshot)
{
    public bool Renewed => Code == OwnershipRenewCode.Renewed;
}

/// <summary>
/// Deterministic ownership state machine. A persistence adapter must serialize calls to this state machine
/// in a linearizable store before it is used by more than one process.
/// </summary>
public sealed class WorkerOwnershipStateMachine
{
    private readonly object _sync = new();
    private long _version;
    private long _generation;
    private Guid? _holderWorkerInstanceId;
    private WorkerOwnershipState _state;
    private DateTimeOffset? _notBeforeUtc;
    private DateTimeOffset? _expiresAtUtc;
    private DateTimeOffset? _releaseAcknowledgedAtUtc;

    public WorkerOwnershipStateMachine(Guid deploymentId, Guid brokerAccountId)
    {
        if (deploymentId == Guid.Empty)
        {
            throw new ArgumentException("Deployment identifier cannot be empty.", nameof(deploymentId));
        }

        if (brokerAccountId == Guid.Empty)
        {
            throw new ArgumentException("Broker account identifier cannot be empty.", nameof(brokerAccountId));
        }

        DeploymentId = deploymentId;
        BrokerAccountId = brokerAccountId;
        _state = WorkerOwnershipState.Free;
    }

    public Guid DeploymentId { get; }

    public Guid BrokerAccountId { get; }

    public WorkerOwnershipSnapshot Read()
    {
        lock (_sync)
        {
            return Snapshot();
        }
    }

    public OwnershipAcquireResult TryAcquire(
        Guid holderWorkerInstanceId,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration,
        TimeSpan expirySafetyInterval)
    {
        if (holderWorkerInstanceId == Guid.Empty)
        {
            throw new ArgumentException("Holder worker identifier cannot be empty.", nameof(holderWorkerInstanceId));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(expirySafetyInterval, TimeSpan.Zero);

        DateTimeOffset normalizedNow = nowUtc.ToUniversalTime();
        lock (_sync)
        {
            if (_state == WorkerOwnershipState.Held && _expiresAtUtc is { } expiresAtUtc)
            {
                if (_holderWorkerInstanceId == holderWorkerInstanceId && normalizedNow < expiresAtUtc)
                {
                    return new OwnershipAcquireResult(
                        OwnershipAcquireCode.AlreadyHeld,
                        "worker_ownership_already_held",
                        Snapshot());
                }

                if (normalizedNow < expiresAtUtc + expirySafetyInterval)
                {
                    return new OwnershipAcquireResult(
                        OwnershipAcquireCode.LeaseExpirySafetyWindowActive,
                        "worker_ownership_expiry_safety_window_active",
                        Snapshot());
                }
            }

            _generation = checked(_generation + 1);
            _version = checked(_version + 1);
            _holderWorkerInstanceId = holderWorkerInstanceId;
            _state = WorkerOwnershipState.Held;
            _notBeforeUtc = normalizedNow;
            _expiresAtUtc = normalizedNow + leaseDuration;
            _releaseAcknowledgedAtUtc = null;

            return new OwnershipAcquireResult(
                OwnershipAcquireCode.Acquired,
                "worker_ownership_acquired",
                Snapshot());
        }
    }

    public OwnershipReleaseResult AcknowledgeRelease(
        Guid holderWorkerInstanceId,
        long generation,
        DateTimeOffset acknowledgedAtUtc)
    {
        lock (_sync)
        {
            if (_state != WorkerOwnershipState.Held)
            {
                return ReleaseFailure(OwnershipReleaseCode.NotHeld, "worker_ownership_not_held");
            }

            if (generation != _generation)
            {
                return ReleaseFailure(OwnershipReleaseCode.WrongGeneration, "worker_ownership_generation_mismatch");
            }

            if (holderWorkerInstanceId != _holderWorkerInstanceId)
            {
                return ReleaseFailure(OwnershipReleaseCode.WrongHolder, "worker_ownership_holder_mismatch");
            }

            _version = checked(_version + 1);
            _state = WorkerOwnershipState.ReleaseAcknowledged;
            _releaseAcknowledgedAtUtc = acknowledgedAtUtc.ToUniversalTime();
            return new OwnershipReleaseResult(
                OwnershipReleaseCode.Released,
                "worker_ownership_release_acknowledged",
                Snapshot());
        }
    }

    public OwnershipRenewResult TryRenew(
        Guid holderWorkerInstanceId,
        long generation,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);

        lock (_sync)
        {
            if (_state != WorkerOwnershipState.Held)
            {
                return RenewFailure(OwnershipRenewCode.NotHeld, "worker_ownership_not_held");
            }

            if (generation != _generation)
            {
                return RenewFailure(OwnershipRenewCode.WrongGeneration, "worker_ownership_generation_mismatch");
            }

            if (holderWorkerInstanceId != _holderWorkerInstanceId)
            {
                return RenewFailure(OwnershipRenewCode.WrongHolder, "worker_ownership_holder_mismatch");
            }

            DateTimeOffset normalizedNow = nowUtc.ToUniversalTime();
            if (_expiresAtUtc is null || normalizedNow >= _expiresAtUtc)
            {
                return RenewFailure(OwnershipRenewCode.AlreadyExpired, "worker_ownership_already_expired");
            }

            _version = checked(_version + 1);
            _expiresAtUtc = normalizedNow + leaseDuration;
            return new OwnershipRenewResult(
                OwnershipRenewCode.Renewed,
                "worker_ownership_renewed",
                Snapshot());
        }
    }

    public bool IsFenceValid(long generation, Guid holderWorkerInstanceId, DateTimeOffset nowUtc)
    {
        lock (_sync)
        {
            DateTimeOffset normalizedNow = nowUtc.ToUniversalTime();
            return _state == WorkerOwnershipState.Held
                && generation == _generation
                && holderWorkerInstanceId == _holderWorkerInstanceId
                && _notBeforeUtc is { } notBeforeUtc
                && _expiresAtUtc is { } expiresAtUtc
                && notBeforeUtc <= normalizedNow
                && normalizedNow < expiresAtUtc;
        }
    }

    private OwnershipReleaseResult ReleaseFailure(OwnershipReleaseCode code, string reasonCode) =>
        new(code, reasonCode, Snapshot());

    private OwnershipRenewResult RenewFailure(OwnershipRenewCode code, string reasonCode) =>
        new(code, reasonCode, Snapshot());

    private WorkerOwnershipSnapshot Snapshot() =>
        new(
            DeploymentId,
            BrokerAccountId,
            _version,
            _generation,
            _holderWorkerInstanceId,
            _state,
            _notBeforeUtc,
            _expiresAtUtc,
            _releaseAcknowledgedAtUtc);
}
