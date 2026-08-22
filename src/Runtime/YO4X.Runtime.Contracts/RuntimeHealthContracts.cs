namespace YO4X.Runtime.Contracts;

public enum RuntimeComponentRole
{
    Supervisor = 0,
    StrategyHost = 1,
    GatewayHost = 2
}

public enum RuntimeComponentState
{
    Starting = 0,
    Ready = 1,
    Degraded = 2,
    Faulted = 3,
    Fenced = 4,
    Stopped = 5
}

public enum FenceEvidenceState
{
    Unverified = 0,
    Valid = 1,
    Invalid = 2
}

public sealed record RuntimeComponentEvidence(
    int ContractVersion,
    RuntimeComponentRole Role,
    Guid DeploymentId,
    Guid WorkerInstanceId,
    long Generation,
    long LastAcceptedSequence,
    RuntimeComponentState State,
    FenceEvidenceState FenceState,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset ObservedAtUtc,
    string EvidenceHash);

public sealed record PublicRuntimeHealth(
    int ContractVersion,
    string Role,
    string Status,
    string Code);
