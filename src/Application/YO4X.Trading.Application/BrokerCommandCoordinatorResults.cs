using YO4X.Trading.Abstractions;

namespace YO4X.Trading.Application;

public enum BrokerCommandDispatchOutcome
{
    SubmissionRecorded = 0,
    ReconciliationRequired = 1,
    NoDispatchAuthority = 2,
    DurableRecoveryRequired = 3
}

public sealed record BrokerCommandDispatchResult(
    BrokerCommandDispatchOutcome Outcome,
    Guid CommandId,
    bool GatewayInvoked,
    GatewayCommandDisposition? Disposition,
    string Code,
    string? DurableState)
{
    public bool RequiresReconciliation =>
        Outcome is BrokerCommandDispatchOutcome.ReconciliationRequired
            or BrokerCommandDispatchOutcome.DurableRecoveryRequired;
}

public enum BrokerCommandReconciliationOutcome
{
    Completed = 0,
    InconclusiveRetryable = 1,
    NotEligible = 2,
    DurableRecoveryRequired = 3
}

public sealed record BrokerCommandReconciliationResult(
    BrokerCommandReconciliationOutcome Outcome,
    Guid CommandId,
    bool GatewayInvoked,
    BrokerReconciliationMatch? Match,
    string Code,
    string? DurableState);
