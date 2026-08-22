using YO4X.BuildingBlocks;

namespace YO4X.Trading.Abstractions;

public enum BrokerCommandState
{
    ReadyToSend = 0,
    SendInProgress = 1,
    Acknowledged = 2,
    PartiallyFilled = 3,
    Filled = 4,
    Cancelled = 5,
    Rejected = 6,
    Unknown = 7,
    ReconciliationPending = 8,
    Reconciled = 9
}

public sealed class BrokerCommandLifecycle
{
    private BrokerCommandLifecycle(Guid commandId, DateTimeOffset createdAtUtc)
    {
        CommandId = commandId;
        State = BrokerCommandState.ReadyToSend;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        UpdatedAtUtc = CreatedAtUtc;
        ReasonCode = "broker_command_ready_to_send";
    }

    public Guid CommandId { get; }

    public BrokerCommandState State { get; private set; }

    public int DispatchAttemptCount { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public string ReasonCode { get; private set; }

    public BrokerReconciliationMatch? ReconciledOutcome { get; private set; }

    public bool CanDispatch => State == BrokerCommandState.ReadyToSend;

    public bool RequiresReconciliation =>
        State is BrokerCommandState.Unknown or BrokerCommandState.ReconciliationPending;

    public bool IsTerminal =>
        State is BrokerCommandState.Filled
            or BrokerCommandState.Cancelled
            or BrokerCommandState.Rejected
            or BrokerCommandState.Reconciled;

    public static BrokerCommandLifecycle CreateReady(Guid commandId, DateTimeOffset createdAtUtc)
    {
        if (commandId == Guid.Empty)
        {
            throw new ArgumentException("Broker command identifier cannot be empty.", nameof(commandId));
        }

        return new BrokerCommandLifecycle(commandId, createdAtUtc);
    }

    public void BeginSend(DateTimeOffset occurredAtUtc)
    {
        RequireState(BrokerCommandState.ReadyToSend);
        DispatchAttemptCount = checked(DispatchAttemptCount + 1);
        Transition(BrokerCommandState.SendInProgress, "broker_command_send_started", occurredAtUtc);
    }

    public void RecordGatewayResult(GatewaySendResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        RequireState(BrokerCommandState.SendInProgress);

        switch (result.Disposition)
        {
            case GatewayCommandDisposition.Accepted:
                Transition(BrokerCommandState.Acknowledged, result.Code, result.ObservedAtUtc);
                break;
            case GatewayCommandDisposition.Rejected:
            case GatewayCommandDisposition.SubmissionDisabled:
                Transition(BrokerCommandState.Rejected, result.Code, result.ObservedAtUtc);
                break;
            case GatewayCommandDisposition.Unknown:
                Transition(BrokerCommandState.Unknown, result.Code, result.ObservedAtUtc);
                break;
            default:
                throw new DomainException("broker_command.disposition_invalid", "Gateway disposition is not supported.");
        }
    }

    public void RecordPartialFill(DateTimeOffset occurredAtUtc)
    {
        RequireOneOf(BrokerCommandState.Acknowledged, BrokerCommandState.PartiallyFilled);
        Transition(BrokerCommandState.PartiallyFilled, "broker_command_partially_filled", occurredAtUtc);
    }

    public void RecordFilled(DateTimeOffset occurredAtUtc)
    {
        RequireOneOf(BrokerCommandState.Acknowledged, BrokerCommandState.PartiallyFilled);
        Transition(BrokerCommandState.Filled, "broker_command_filled", occurredAtUtc);
    }

    public void RecordCancelled(DateTimeOffset occurredAtUtc)
    {
        RequireOneOf(BrokerCommandState.Acknowledged, BrokerCommandState.PartiallyFilled);
        Transition(BrokerCommandState.Cancelled, "broker_command_cancelled", occurredAtUtc);
    }

    public void MarkUnknownAfterInterruptedSend(DateTimeOffset occurredAtUtc)
    {
        RequireOneOf(
            BrokerCommandState.SendInProgress,
            BrokerCommandState.Acknowledged,
            BrokerCommandState.PartiallyFilled);
        Transition(BrokerCommandState.Unknown, "broker_command_outcome_unknown", occurredAtUtc);
    }

    public void RecoverAfterRestart(DateTimeOffset occurredAtUtc)
    {
        if (State == BrokerCommandState.ReadyToSend)
        {
            return;
        }

        RequireState(BrokerCommandState.SendInProgress);
        Transition(BrokerCommandState.Unknown, "broker_command_restart_during_send", occurredAtUtc);
    }

    public void BeginReconciliation(DateTimeOffset occurredAtUtc)
    {
        RequireState(BrokerCommandState.Unknown);
        Transition(BrokerCommandState.ReconciliationPending, "broker_command_reconciliation_started", occurredAtUtc);
    }

    public void CompleteReconciliation(BrokerCommandReconciliation result)
    {
        ArgumentNullException.ThrowIfNull(result);
        RequireState(BrokerCommandState.ReconciliationPending);
        if (result.CommandId != CommandId)
        {
            throw new DomainException(
                "broker_command.reconciliation_mismatch",
                "Reconciliation result belongs to a different broker command.");
        }

        if (result.Match == BrokerReconciliationMatch.Inconclusive)
        {
            Transition(BrokerCommandState.Unknown, result.ReasonCode, result.ReconciledAtUtc);
            return;
        }

        ReconciledOutcome = result.Match;
        Transition(BrokerCommandState.Reconciled, result.ReasonCode, result.ReconciledAtUtc);
    }

    private void RequireState(BrokerCommandState requiredState)
    {
        if (State != requiredState)
        {
            throw InvalidTransition(requiredState);
        }
    }

    private void RequireOneOf(params BrokerCommandState[] allowedStates)
    {
        if (!allowedStates.Contains(State))
        {
            throw InvalidTransition(allowedStates);
        }
    }

    private DomainException InvalidTransition(params BrokerCommandState[] targetStates) =>
        new(
            "broker_command.transition_invalid",
            $"Broker command cannot transition from {State} to any of: {string.Join(", ", targetStates)}.");

    private void Transition(BrokerCommandState nextState, string reasonCode, DateTimeOffset occurredAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        State = nextState;
        ReasonCode = reasonCode;
        UpdatedAtUtc = occurredAtUtc.ToUniversalTime();
    }
}
