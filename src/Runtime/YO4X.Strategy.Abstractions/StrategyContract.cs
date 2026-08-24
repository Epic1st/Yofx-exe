using System.Text;
using System.Text.Json;
using YO4X.BuildingBlocks;
using YO4X.Runtime.Contracts;

namespace YO4X.Strategy.Abstractions;

public interface IYo4xStrategy
{
    StrategyResult Handle(
        StrategyEvent input,
        StrategySnapshot snapshot,
        StrategyState currentState);
}

public sealed class StrategyResult
{
    public const int MaximumRequestedActionCount = 256;

    public StrategyResult(StrategyState nextState, IEnumerable<RequestedAction>? requestedActions = null)
    {
        ArgumentNullException.ThrowIfNull(nextState);
        ContractVersion = RuntimeContractVersions.StrategyResultV1;
        NextState = nextState;
        RequestedActions = Array.AsReadOnly(SnapshotActions(requestedActions));
    }

    public int ContractVersion { get; }

    public StrategyState NextState { get; }

    public IReadOnlyList<RequestedAction> RequestedActions { get; }

    private static RequestedAction[] SnapshotActions(IEnumerable<RequestedAction>? source)
    {
        if (source is null)
        {
            return [];
        }

        if (source is IReadOnlyList<RequestedAction> list)
        {
            int count = list.Count;
            if (count is < 0 || count > MaximumRequestedActionCount)
            {
                throw new ArgumentException(
                    "The requested-action collection exceeds the durable action limit.",
                    nameof(source));
            }

            var result = new RequestedAction[count];
            for (int index = 0; index < count; index++)
            {
                result[index] = list[index];
            }

            return result;
        }

        var values = new List<RequestedAction>(MaximumRequestedActionCount);
        using IEnumerator<RequestedAction> enumerator = source.GetEnumerator();
        while (enumerator.MoveNext())
        {
            if (values.Count == MaximumRequestedActionCount)
            {
                throw new ArgumentException(
                    "The requested-action collection exceeds the durable action limit.",
                    nameof(source));
            }

            values.Add(enumerator.Current);
        }

        return values.ToArray();
    }
}

public sealed record StrategyResultBounds(
    int MaximumStateBytes,
    int MaximumActionCount,
    int MaximumCombinedActionBytes,
    TimeSpan MaximumWallTime)
{
    public static StrategyResultBounds Create(
        int maximumStateBytes,
        int maximumActionCount,
        int maximumCombinedActionBytes,
        TimeSpan maximumWallTime)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumStateBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumActionCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCombinedActionBytes);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumWallTime, TimeSpan.Zero);

        return new StrategyResultBounds(
            maximumStateBytes,
            maximumActionCount,
            maximumCombinedActionBytes,
            maximumWallTime);
    }
}

public enum StrategyResultValidationCode
{
    Valid = 0,
    MissingResult = 1,
    InvalidStateVersion = 2,
    StateLimitExceeded = 3,
    ActionCountExceeded = 4,
    ActionSizeExceeded = 5,
    DuplicateActionId = 6,
    DuplicateIdempotencyKey = 7,
    WallTimeExceeded = 8,
    StrategyFaulted = 9
}

public sealed record BoundedStrategyResult(
    StrategyResult Result,
    string ResultHash,
    int StateBytes,
    int CombinedActionBytes);

public sealed record StrategyResultValidation(
    StrategyResultValidationCode Code,
    string ReasonCode,
    BoundedStrategyResult? BoundedResult)
{
    public bool IsValid => Code == StrategyResultValidationCode.Valid;
}

public static class StrategyResultValidator
{
    public static StrategyResultValidation Validate(
        StrategyState currentState,
        StrategyResult? result,
        StrategyResultBounds bounds,
        TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        ArgumentNullException.ThrowIfNull(bounds);

        if (result is null)
        {
            return Failure(StrategyResultValidationCode.MissingResult, "strategy_result_missing");
        }

        if (result.NextState.Version != checked(currentState.Version + 1))
        {
            return Failure(StrategyResultValidationCode.InvalidStateVersion, "strategy_state_version_invalid");
        }

        int stateBytes = Encoding.UTF8.GetByteCount(result.NextState.PayloadJson);
        if (stateBytes > bounds.MaximumStateBytes)
        {
            return Failure(StrategyResultValidationCode.StateLimitExceeded, "strategy_state_limit_exceeded");
        }

        if (result.RequestedActions.Count > bounds.MaximumActionCount)
        {
            return Failure(StrategyResultValidationCode.ActionCountExceeded, "strategy_action_count_exceeded");
        }

        var actionIds = new HashSet<Guid>();
        var idempotencyKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (RequestedAction action in result.RequestedActions)
        {
            if (action is null)
            {
                return Failure(
                    StrategyResultValidationCode.StrategyFaulted,
                    "strategy_action_missing");
            }

            if (!HasCanonicalActionText(action))
            {
                return Failure(
                    StrategyResultValidationCode.StrategyFaulted,
                    "strategy_action_text_invalid");
            }

            if (!actionIds.Add(action.ActionId))
            {
                return Failure(StrategyResultValidationCode.DuplicateActionId, "strategy_action_id_duplicate");
            }

            if (!idempotencyKeys.Add(action.IdempotencyKey))
            {
                return Failure(
                    StrategyResultValidationCode.DuplicateIdempotencyKey,
                    "strategy_idempotency_key_duplicate");
            }
        }

        string actionsJson;
        try
        {
            actionsJson = CanonicalJson.Serialize(result.RequestedActions);
        }
        catch (Exception exception) when (exception is NotSupportedException or JsonException)
        {
            return Failure(
                StrategyResultValidationCode.StrategyFaulted,
                "strategy_result_serialization_invalid");
        }

        int actionBytes = Encoding.UTF8.GetByteCount(actionsJson);
        if (actionBytes > bounds.MaximumCombinedActionBytes)
        {
            return Failure(StrategyResultValidationCode.ActionSizeExceeded, "strategy_action_size_exceeded");
        }

        if (elapsed > bounds.MaximumWallTime)
        {
            return Failure(StrategyResultValidationCode.WallTimeExceeded, "strategy_wall_time_exceeded");
        }

        string resultHash;
        try
        {
            resultHash = CanonicalJson.Sha256(new
            {
                ContractVersion = RuntimeContractVersions.StrategyResultV1,
                State = result.NextState,
                Actions = result.RequestedActions
            });
        }
        catch (Exception exception) when (exception is NotSupportedException or JsonException)
        {
            return Failure(
                StrategyResultValidationCode.StrategyFaulted,
                "strategy_result_serialization_invalid");
        }

        return new StrategyResultValidation(
            StrategyResultValidationCode.Valid,
            "strategy_result_valid",
            new BoundedStrategyResult(result, resultHash, stateBytes, actionBytes));
    }

    private static StrategyResultValidation Failure(StrategyResultValidationCode code, string reasonCode) =>
        new(code, reasonCode, null);

    private static bool HasCanonicalActionText(RequestedAction action) =>
        StrategyCanonicalText.IsCanonical(action.IdempotencyKey)
        && StrategyCanonicalText.IsCanonical(action.Symbol)
        && StrategyCanonicalText.IsCanonical(action.ReasonCode)
        && (action switch
        {
            UpdateProtectionAction update =>
                StrategyCanonicalText.IsCanonical(update.PositionId),
            CancelPendingOrderAction cancel =>
                StrategyCanonicalText.IsCanonical(cancel.OrderId),
            ClosePositionAction close =>
                StrategyCanonicalText.IsCanonical(close.PositionId),
            _ => true
        });
}
