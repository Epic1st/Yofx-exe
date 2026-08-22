namespace YO4X.Runtime.Contracts;

public static class RuntimeContractVersions
{
    public const int EnvelopeV1 = 1;
    public const int StrategyEventV1 = 1;
    public const int StrategySnapshotV1 = 1;
    public const int StrategyResultV1 = 1;
    public const int TradingGatewayV1 = 1;
    public const int ExecutionLeaseV1 = 1;
    public const int ComponentEvidenceV1 = 1;
    public const int PublicHealthV1 = 1;
}

public enum RuntimeEnvelopeDecision
{
    Accepted = 0,
    Duplicate = 1,
    UnsupportedVersion = 2,
    InvalidIdentity = 3,
    WrongDeployment = 4,
    WrongWorker = 5,
    FencedGeneration = 6,
    SequenceGap = 7,
    StaleSequence = 8
}

public sealed record RuntimeEnvelope<TPayload>(
    int ContractVersion,
    Guid DeploymentId,
    Guid WorkerInstanceId,
    long Generation,
    long Sequence,
    Guid EventId,
    DateTimeOffset ReceivedAtUtc,
    DateTimeOffset? BrokerTimestampUtc,
    TPayload Payload)
    where TPayload : notnull;

public sealed record RuntimeEnvelopeValidation(
    RuntimeEnvelopeDecision Decision,
    string Code,
    long ExpectedGeneration,
    long ExpectedSequence)
{
    public bool IsAccepted => Decision == RuntimeEnvelopeDecision.Accepted;

    public bool IsDuplicate => Decision == RuntimeEnvelopeDecision.Duplicate;
}
