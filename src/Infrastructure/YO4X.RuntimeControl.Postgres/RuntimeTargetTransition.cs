using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;

namespace YO4X.RuntimeControl.Postgres;

internal sealed record RuntimeTargetTransition(
    string State,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? AppliedAt,
    DateTimeOffset? ReconciledAt,
    string? ObservedResult,
    string? BrokerEvidenceReference,
    string? LastErrorCode)
{
    public static RuntimeTargetTransition Create(
        string currentState,
        TargetDeliveryInput input,
        string eventKind,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentState);
        ArgumentNullException.ThrowIfNull(input);
        string state = input.State.Trim().ToLowerInvariant();
        if (string.Equals(eventKind, "target_delivery", StringComparison.Ordinal))
        {
            return state switch
            {
                "delivered" when currentState == "dispatched" => new(state, now, null, null, null, null, null, null),
                "acknowledged" when currentState == "delivered" => new(state, null, now, null, null, null, null, null),
                "applied" when currentState == "acknowledged" => new(state, null, null, now, null, TrimOptional(input.ObservedResult), null, null),
                "not_applicable" when currentState == "pending_dispatch" && HasText(input.ObservedResult) =>
                    new(state, null, null, null, null, input.ObservedResult!.Trim(), null, null),
                "unreachable" or "failed" when currentState is
                    "pending_dispatch" or "dispatched" or "delivered" or "acknowledged" or "applied" or "reconciling"
                    && HasText(input.ErrorCode) =>
                    new(state, null, null, null, null, TrimOptional(input.ObservedResult), null, input.ErrorCode!.Trim()),
                "unknown" when currentState is "dispatched" or "delivered" or "acknowledged" or "applied" or "reconciling"
                    && HasText(input.ErrorCode) =>
                    new(state, null, null, null, null, TrimOptional(input.ObservedResult), null, input.ErrorCode!.Trim()),
                _ => InvalidTransition()
            };
        }

        if (!string.Equals(eventKind, "target_reconciliation", StringComparison.Ordinal))
        {
            return InvalidTransition();
        }

        return state switch
        {
            "reconciling" when currentState is "applied" or "unknown" or "unreachable" =>
                new(state, null, null, null, null, null, null, null),
            "reconciled" when currentState == "reconciling"
                && HasText(input.ObservedResult)
                && HasText(input.BrokerEvidenceReference) =>
                new(
                    state,
                    null,
                    null,
                    null,
                    now,
                    input.ObservedResult!.Trim(),
                    input.BrokerEvidenceReference!.Trim(),
                    null),
            "failed" when currentState is
                "pending_dispatch" or "dispatched" or "delivered" or "acknowledged" or "applied" or "reconciling"
                && HasText(input.ErrorCode) =>
                new(state, null, null, null, null, TrimOptional(input.ObservedResult), null, input.ErrorCode!.Trim()),
            "unknown" when currentState is "dispatched" or "delivered" or "acknowledged" or "applied" or "reconciling"
                && HasText(input.ErrorCode) =>
                new(state, null, null, null, null, TrimOptional(input.ObservedResult), null, input.ErrorCode!.Trim()),
            _ => InvalidTransition()
        };
    }

    private static RuntimeTargetTransition InvalidTransition() => throw new ResourceConflictException(
        "COMMAND_TARGET_TRANSITION_INVALID",
        "The requested command-target transition is not valid from its current state.");

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    private static string? TrimOptional(string? value) => HasText(value) ? value!.Trim() : null;
}
