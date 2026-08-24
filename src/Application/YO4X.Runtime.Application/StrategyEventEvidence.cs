using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YO4X.BuildingBlocks;
using YO4X.Runtime.Contracts;
using YO4X.Strategy.Abstractions;
using YO4X.Tenancy;

namespace YO4X.Runtime.Application;

public static class StrategyEventEvidenceValidator
{
    private const int MaximumIdentifierCharacters = 200;
    private const int MaximumReasonCodeCharacters = 200;
    private const int MaximumCurrencyCharacters = 20;

    public static void RequireCanonical(
        RuntimeEnvelope<StrategyEvent> envelope,
        StrategySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(envelope.Payload);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (envelope.ContractVersion != RuntimeContractVersions.EnvelopeV1)
        {
            throw new ArgumentException("The runtime envelope version is unsupported.", nameof(envelope));
        }

        if (envelope.DeploymentId == Guid.Empty
            || envelope.WorkerInstanceId == Guid.Empty
            || envelope.EventId == Guid.Empty
            || envelope.Generation <= 0
            || envelope.Sequence <= 0)
        {
            throw new ArgumentException("The runtime envelope identity is invalid.", nameof(envelope));
        }

        if (envelope.Payload.ContractVersion != RuntimeContractVersions.StrategyEventV1)
        {
            throw new ArgumentException("The strategy-event version is unsupported.", nameof(envelope));
        }

        StrategyEvidencePrimitives.RequireCanonicalUtcMicroseconds(
            envelope.ReceivedAtUtc,
            nameof(envelope));
        if (envelope.BrokerTimestampUtc is { } brokerTimestamp)
        {
            StrategyEvidencePrimitives.RequireCanonicalUtcMicroseconds(
                brokerTimestamp,
                nameof(envelope));
        }

        StrategyEvidencePrimitives.RequireCanonicalUtcMicroseconds(
            envelope.Payload.OccurredAtUtc,
            nameof(envelope));
        switch (envelope.Payload)
        {
            case InitializeEvent initialize:
                RequireEventKind(initialize, StrategyEventKind.Initialize, envelope);
                RequireBoundedText(
                    initialize.ReasonCode,
                    MaximumReasonCodeCharacters,
                    "initialize reason code",
                    nameof(envelope));
                break;
            case NewTickEvent tick:
                RequireEventKind(tick, StrategyEventKind.NewTick, envelope);
                RequireSymbol(tick.Symbol, nameof(envelope));
                if (tick.Bid <= 0
                    || tick.Ask <= 0
                    || tick.Ask < tick.Bid
                    || tick.MarketDataSequence <= 0)
                {
                    throw new ArgumentException(
                        "The tick event market data is invalid.",
                        nameof(envelope));
                }

                break;
            case BarClosedEvent bar:
                RequireEventKind(bar, StrategyEventKind.BarClosed, envelope);
                StrategyEvidencePrimitives.RequireCanonicalUtcMicroseconds(
                    bar.OpenedAtUtc,
                    nameof(envelope));
                RequireSymbol(bar.Symbol, nameof(envelope));
                if (bar.Timeframe <= TimeSpan.Zero
                    || bar.OpenedAtUtc > bar.OccurredAtUtc
                    || bar.Open <= 0
                    || bar.High <= 0
                    || bar.Low <= 0
                    || bar.Close <= 0
                    || bar.Low > bar.High
                    || bar.Open < bar.Low
                    || bar.Open > bar.High
                    || bar.Close < bar.Low
                    || bar.Close > bar.High
                    || bar.TickVolume < 0
                    || bar.MarketDataSequence <= 0)
                {
                    throw new ArgumentException(
                        "The closed-bar event is invalid.",
                        nameof(envelope));
                }

                break;
            case TimerEvent timer:
                RequireEventKind(timer, StrategyEventKind.Timer, envelope);
                StrategyEvidencePrimitives.RequireCanonicalUtcMicroseconds(
                    timer.ScheduledAtUtc,
                    nameof(envelope));
                RequireBoundedText(
                    timer.TimerId,
                    MaximumIdentifierCharacters,
                    "timer identifier",
                    nameof(envelope));
                if (timer.ScheduledAtUtc > timer.OccurredAtUtc)
                {
                    throw new ArgumentException(
                        "The timer event precedes its schedule.",
                        nameof(envelope));
                }

                break;
            case ExecutionEvent execution:
                RequireEventKind(execution, StrategyEventKind.Execution, envelope);
                RequireBoundedText(
                    execution.BrokerEventId,
                    MaximumIdentifierCharacters,
                    "broker event identifier",
                    nameof(envelope));
                RequireOptionalBoundedText(
                    execution.OrderId,
                    MaximumIdentifierCharacters,
                    "order identifier",
                    nameof(envelope));
                RequireOptionalBoundedText(
                    execution.DealId,
                    MaximumIdentifierCharacters,
                    "deal identifier",
                    nameof(envelope));
                RequireBoundedText(
                    execution.ReasonCode,
                    MaximumReasonCodeCharacters,
                    "execution reason code",
                    nameof(envelope));
                if (execution.BrokerCommandId == Guid.Empty
                    || !Enum.IsDefined(execution.ExecutionKind)
                    || execution.FilledVolume < 0
                    || execution.FillPrice is <= 0)
                {
                    throw new ArgumentException(
                        "The execution event is invalid.",
                        nameof(envelope));
                }

                break;
            case AccountChangedEvent accountChanged:
                RequireEventKind(accountChanged, StrategyEventKind.AccountChanged, envelope);
                RequireBoundedText(
                    accountChanged.ReasonCode,
                    MaximumReasonCodeCharacters,
                    "account-change reason code",
                    nameof(envelope));
                if (accountChanged.AccountSequence <= 0)
                {
                    throw new ArgumentException(
                        "The account-change sequence is invalid.",
                        nameof(envelope));
                }

                break;
            case StopEvent stop:
                RequireEventKind(stop, StrategyEventKind.Stop, envelope);
                if (!Enum.IsDefined(stop.Reason))
                {
                    throw new ArgumentException(
                        "The stop reason is invalid.",
                        nameof(envelope));
                }

                break;
            default:
                throw new ArgumentException(
                    "The strategy-event subtype is unsupported.",
                    nameof(envelope));
        }

        if (snapshot.ContractVersion != RuntimeContractVersions.StrategySnapshotV1
            || snapshot.Sequence <= 0
            || snapshot.Account is null
            || snapshot.Account.Sequence <= 0
            || snapshot.AsOfUtc > snapshot.DeterministicNowUtc)
        {
            throw new ArgumentException(
                "The strategy snapshot identity or sequence is invalid.",
                nameof(snapshot));
        }

        StrategyEvidencePrimitives.RequireCanonicalUtcMicroseconds(snapshot.AsOfUtc, nameof(snapshot));
        StrategyEvidencePrimitives.RequireCanonicalUtcMicroseconds(
            snapshot.DeterministicNowUtc,
            nameof(snapshot));
        RequireBoundedText(
            snapshot.Account.Currency,
            MaximumCurrencyCharacters,
            "account currency",
            nameof(snapshot));
        var quoteIdentities = new HashSet<(string Symbol, long Sequence)>();
        string? previousQuoteSymbol = null;
        long previousQuoteSequence = 0;
        foreach (StrategyQuoteSnapshot? quote in snapshot.Quotes)
        {
            if (quote is null)
            {
                throw new ArgumentException(
                    "The strategy snapshot contains a null quote.",
                    nameof(snapshot));
            }

            StrategyEvidencePrimitives.RequireCanonicalUtcMicroseconds(
                quote.ObservedAtUtc,
                nameof(snapshot));
            RequireSymbol(quote.Symbol, nameof(snapshot));
            int symbolOrder = previousQuoteSymbol is null
                ? -1
                : string.CompareOrdinal(previousQuoteSymbol, quote.Symbol);
            if (quote.Sequence <= 0
                || quote.Bid <= 0
                || quote.Ask <= 0
                || quote.Ask < quote.Bid
                || quote.ObservedAtUtc > snapshot.AsOfUtc
                || symbolOrder > 0
                || (symbolOrder == 0 && previousQuoteSequence > quote.Sequence)
                || !quoteIdentities.Add((quote.Symbol, quote.Sequence)))
            {
                throw new ArgumentException(
                    "The strategy quote snapshot is invalid.",
                    nameof(snapshot));
            }

            previousQuoteSymbol = quote.Symbol;
            previousQuoteSequence = quote.Sequence;
        }

        var positionIds = new HashSet<string>(StringComparer.Ordinal);
        string? previousPositionId = null;
        foreach (StrategyPositionSnapshot? position in snapshot.Positions)
        {
            if (position is null)
            {
                throw new ArgumentException(
                    "The strategy snapshot contains a null position.",
                    nameof(snapshot));
            }

            RequireBoundedText(
                position.PositionId,
                MaximumIdentifierCharacters,
                "position identifier",
                nameof(snapshot));
            RequireSymbol(position.Symbol, nameof(snapshot));
            if (!Enum.IsDefined(position.Side)
                || position.Volume <= 0
                || position.OpenPrice <= 0
                || position.StopLoss is <= 0
                || position.TakeProfit is <= 0
                || (previousPositionId is not null
                    && string.CompareOrdinal(previousPositionId, position.PositionId) > 0)
                || !positionIds.Add(position.PositionId))
            {
                throw new ArgumentException(
                    "The strategy position snapshot is invalid.",
                    nameof(snapshot));
            }

            previousPositionId = position.PositionId;
        }

        var orderIds = new HashSet<string>(StringComparer.Ordinal);
        string? previousOrderId = null;
        foreach (StrategyPendingOrderSnapshot? order in snapshot.PendingOrders)
        {
            if (order is null)
            {
                throw new ArgumentException(
                    "The strategy snapshot contains a null pending order.",
                    nameof(snapshot));
            }

            RequireBoundedText(
                order.OrderId,
                MaximumIdentifierCharacters,
                "order identifier",
                nameof(snapshot));
            RequireSymbol(order.Symbol, nameof(snapshot));
            if (!Enum.IsDefined(order.Side)
                || order.Volume <= 0
                || order.RequestedPrice <= 0
                || order.StopLoss is <= 0
                || order.TakeProfit is <= 0
                || (previousOrderId is not null
                    && string.CompareOrdinal(previousOrderId, order.OrderId) > 0)
                || !orderIds.Add(order.OrderId))
            {
                throw new ArgumentException(
                    "The strategy pending-order snapshot is invalid.",
                    nameof(snapshot));
            }

            previousOrderId = order.OrderId;
        }
    }

    private static void RequireEventKind(
        StrategyEvent value,
        StrategyEventKind expectedKind,
        RuntimeEnvelope<StrategyEvent> envelope)
    {
        if (value.Kind != expectedKind)
        {
            throw new ArgumentException(
                "The strategy-event subtype and kind do not match.",
                nameof(envelope));
        }
    }

    private static void RequireSymbol(string? value, string parameterName) =>
        RequireBoundedText(
            value,
            StrategyDurableEvidenceLimits.MaximumSymbolCharacters,
            "symbol",
            parameterName);

    private static void RequireOptionalBoundedText(
        string? value,
        int maximumCharacters,
        string valueKind,
        string parameterName)
    {
        if (value is not null)
        {
            RequireBoundedText(value, maximumCharacters, valueKind, parameterName);
        }
    }

    private static void RequireBoundedText(
        string? value,
        int maximumCharacters,
        string valueKind,
        string parameterName)
    {
        if (!StrategyCanonicalText.IsCanonical(value))
        {
            throw new ArgumentException(
                $"The {valueKind} is not canonical Unicode text.",
                parameterName);
        }

        int characterCount = 0;
        foreach (Rune _ in value!.EnumerateRunes())
        {
            characterCount++;
            if (characterCount > maximumCharacters)
            {
                throw new ArgumentException(
                    $"The {valueKind} exceeds the supported character limit.",
                    parameterName);
            }
        }
    }

    public static bool IsExactClaim(
        StrategyEventReference expectedReference,
        Guid expectedClaimToken,
        ClaimedStrategyEvent? claim)
    {
        if (claim is null
            || claim.Reference != expectedReference
            || claim.ClaimToken == Guid.Empty
            || claim.ClaimToken != expectedClaimToken
            || claim.Envelope is null
            || claim.Envelope.Payload is null
            || claim.Snapshot is null
            || claim.PriorState is null
            || !StrategyDurableEvidenceLimits.HasSupportedEventDocumentSize(claim.EventJson)
            || !StrategyDurableEvidenceLimits.HasSupportedSnapshotDocumentSize(
                claim.SnapshotJson)
            || !StrategyDurableEvidenceLimits.HasSupportedStateDocumentSize(
                claim.PriorStateJson)
            || !StrategyDurableEvidenceLimits.HasSupportedStateDocumentSize(
                claim.PriorState.PayloadJson)
            || claim.AuthorityNowUtc >= claim.ClaimExpiresAtUtc
            || !StrategyEvidencePrimitives.IsCanonicalUtcMicroseconds(claim.AuthorityNowUtc)
            || !StrategyEvidencePrimitives.IsCanonicalUtcMicroseconds(claim.ClaimExpiresAtUtc))
        {
            return false;
        }

        try
        {
            RequireCanonical(claim.Envelope, claim.Snapshot);
            if (claim.Envelope.DeploymentId != expectedReference.DeploymentId
                || claim.Envelope.WorkerInstanceId != expectedReference.WorkerInstanceId
                || claim.Envelope.Generation != expectedReference.Generation
                || claim.Envelope.Sequence != expectedReference.Sequence
                || claim.Envelope.EventId != expectedReference.EventId
                || claim.Envelope.Payload.Kind != expectedReference.EventKind
                || claim.Envelope.Payload.ContractVersion
                    != expectedReference.EventContractVersion
                || claim.Snapshot.Sequence != expectedReference.SnapshotSequence
                || claim.Snapshot.ContractVersion != expectedReference.SnapshotContractVersion)
            {
                return false;
            }

            string canonicalEvent = CanonicalJson.Serialize(claim.Envelope);
            string canonicalSnapshot = CanonicalJson.Serialize(claim.Snapshot);
            return StrategyDurableEvidenceLimits.HasSupportedEventDocumentSize(canonicalEvent)
                && StrategyDurableEvidenceLimits.HasSupportedSnapshotDocumentSize(
                    canonicalSnapshot)
                && StrategyEvidencePrimitives.FixedTimeEquals(
                    canonicalEvent,
                    claim.EventJson)
                && StrategyEvidencePrimitives.FixedTimeEquals(
                    canonicalSnapshot,
                    claim.SnapshotJson)
                && StrategyEvidencePrimitives.FixedTimeEquals(
                    StrategyEvidencePrimitives.Sha256Text(claim.EventJson),
                    expectedReference.EventSha256)
                && StrategyEvidencePrimitives.FixedTimeEquals(
                    StrategyEvidencePrimitives.Sha256Text(claim.SnapshotJson),
                    expectedReference.SnapshotSha256)
                && StrategyEvidencePrimitives.FixedTimeEquals(
                    claim.PriorState.PayloadJson,
                    claim.PriorStateJson)
                && StrategyEvidencePrimitives.FixedTimeEquals(
                    claim.PriorState.ContentHash,
                    claim.PriorStateSha256)
                && StrategyEvidencePrimitives.FixedTimeEquals(
                    StrategyEvidencePrimitives.Sha256Text(claim.PriorStateJson),
                    claim.PriorStateSha256);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidOperationException or
            JsonException or
            NotSupportedException or
            NullReferenceException or
            OverflowException)
        {
            return false;
        }
    }
}

internal static class StrategyEventCommitEvidenceFactory
{
    public const int ContractVersion = 1;
    public const string RiskEvaluationOutboxTopic =
        "strategy.action.risk-evaluation-requested.v1";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static StrategyEventCommitEvidence Create(
        TenantExecutionContext context,
        ClaimedStrategyEvent claim,
        BoundedStrategyResult result,
        Guid commitId,
        IReadOnlyList<Guid> outboxMessageIds,
        DateTimeOffset preparedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(result.Result);
        ArgumentNullException.ThrowIfNull(outboxMessageIds);
        if (commitId == Guid.Empty)
        {
            throw new ArgumentException("A commit identifier is required.", nameof(commitId));
        }

        if (!StrategyEventEvidenceValidator.IsExactClaim(
                claim.Reference,
                claim.ClaimToken,
                claim))
        {
            throw new ArgumentException("The strategy-event claim is invalid.", nameof(claim));
        }

        StrategyEvidencePrimitives.RequireCanonicalUtcMicroseconds(
            preparedAtUtc,
            nameof(preparedAtUtc));
        if (result.Result.NextState.Version != checked(claim.PriorState.Version + 1)
            || !StrategyEvidencePrimitives.FixedTimeEquals(
                result.Result.NextState.ContentHash,
                StrategyEvidencePrimitives.Sha256Text(result.Result.NextState.PayloadJson)))
        {
            throw new ArgumentException("The bounded result state is invalid.", nameof(result));
        }

        if (outboxMessageIds.Count != result.Result.RequestedActions.Count
            || outboxMessageIds.Any(value => value == Guid.Empty)
            || outboxMessageIds.Distinct().Count() != outboxMessageIds.Count)
        {
            throw new ArgumentException(
                "Exactly one unique outbox identifier is required per action.",
                nameof(outboxMessageIds));
        }

        if (result.Result.RequestedActions.Any(
                action => action is null || !HasCanonicalActionText(action)))
        {
            throw new ArgumentException(
                "The bounded result contains non-canonical strategy action text.",
                nameof(result));
        }

        string resultJson = CanonicalJson.Serialize(new StrategyResultEvidenceDocument(
            result.Result.ContractVersion,
            result.Result.NextState,
            result.Result.RequestedActions));
        if (!StrategyEvidencePrimitives.FixedTimeEquals(
                StrategyEvidencePrimitives.Sha256Text(resultJson),
                result.ResultHash))
        {
            throw new ArgumentException("The bounded result digest is invalid.", nameof(result));
        }

        StrategyCommittedActionDocument[] actions = result.Result.RequestedActions
            .Select((action, index) => CreateActionDocument(
                context,
                claim,
                result.Result.NextState.Version,
                action,
                index,
                outboxMessageIds[index]))
            .ToArray();

        var document = new StrategyEventCommitDocument(
            ContractVersion,
            commitId,
            claim.ClaimToken,
            context.TenantId,
            claim.Reference.DeploymentId,
            claim.Reference.WorkerInstanceId,
            claim.Reference.Generation,
            claim.Reference.Sequence,
            claim.Reference.EventId,
            claim.Reference.EventKind,
            claim.Reference.EventContractVersion,
            claim.EventJson,
            claim.Reference.EventSha256,
            claim.Reference.SnapshotSequence,
            claim.Reference.SnapshotContractVersion,
            claim.SnapshotJson,
            claim.Reference.SnapshotSha256,
            claim.PriorState.Version,
            claim.PriorStateJson,
            claim.PriorStateSha256,
            result.Result.NextState.Version,
            result.Result.NextState.PayloadJson,
            result.Result.NextState.ContentHash,
            resultJson,
            result.ResultHash,
            result.StateBytes,
            result.CombinedActionBytes,
            Array.AsReadOnly(actions),
            claim.AuthorityNowUtc,
            claim.ClaimExpiresAtUtc,
            preparedAtUtc);
        if (!IsInternallyConsistent(document))
        {
            throw new ArgumentException(
                "The strategy-event commit exceeds durable evidence invariants.",
                nameof(result));
        }

        string json = CanonicalJson.Serialize(document);
        if (Encoding.UTF8.GetByteCount(json)
            > StrategyDurableEvidenceLimits.MaximumCommitEvidenceBytes)
        {
            throw new ArgumentException(
                "Commit evidence exceeds the supported byte bounds.",
                nameof(result));
        }

        return new StrategyEventCommitEvidence(
            document,
            json,
            StrategyEvidencePrimitives.Sha256Text(json));
    }

    public static StrategyEventCommitEvidence Restore(string canonicalJson, string sha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalJson);
        StrategyEvidencePrimitives.RequireDigest(sha256, nameof(sha256));
        int evidenceBytes = Encoding.UTF8.GetByteCount(canonicalJson);
        if (evidenceBytes is < 2
            or > StrategyDurableEvidenceLimits.MaximumCommitEvidenceBytes)
        {
            throw new ArgumentException(
                "Commit evidence exceeds the supported byte bounds.",
                nameof(canonicalJson));
        }

        StrategyEventCommitDocument document;
        try
        {
            document = JsonSerializer.Deserialize<StrategyEventCommitDocument>(
                    canonicalJson,
                    SerializerOptions)
                ?? throw new ArgumentException("Commit evidence is empty.", nameof(canonicalJson));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "Commit evidence is not valid JSON.",
                nameof(canonicalJson),
                exception);
        }

        string normalized = CanonicalJson.Serialize(document);
        if (!StrategyEvidencePrimitives.FixedTimeEquals(normalized, canonicalJson)
            || !StrategyEvidencePrimitives.FixedTimeEquals(
                StrategyEvidencePrimitives.Sha256Text(canonicalJson),
                sha256)
            || !IsInternallyConsistent(document))
        {
            throw new ArgumentException(
                "Commit evidence is not canonical or internally consistent.",
                nameof(canonicalJson));
        }

        return new StrategyEventCommitEvidence(document, canonicalJson, sha256);
    }

    public static bool IsInternallyConsistent(StrategyEventCommitDocument? document)
    {
        if (document is null
            || document.ContractVersion != ContractVersion
            || document.CommitId == Guid.Empty
            || document.ClaimToken == Guid.Empty
            || document.TenantId == Guid.Empty
            || document.DeploymentId == Guid.Empty
            || document.WorkerInstanceId == Guid.Empty
            || document.Generation <= 0
            || document.EventSequence <= 0
            || document.EventId == Guid.Empty
            || !Enum.IsDefined(document.EventKind)
            || document.EventContractVersion != RuntimeContractVersions.StrategyEventV1
            || document.SnapshotSequence <= 0
            || document.SnapshotContractVersion != RuntimeContractVersions.StrategySnapshotV1
            || !StrategyDurableEvidenceLimits.HasSupportedEventDocumentSize(document.EventJson)
            || !StrategyDurableEvidenceLimits.HasSupportedSnapshotDocumentSize(
                document.SnapshotJson)
            || !StrategyDurableEvidenceLimits.HasSupportedStateDocumentSize(
                document.PriorStateJson)
            || !StrategyDurableEvidenceLimits.HasSupportedStateDocumentSize(
                document.NextStateJson)
            || document.PriorStateVersion < 0
            || document.NextStateVersion != document.PriorStateVersion + 1
            || document.StateBytes is < 1
                or > StrategyDurableEvidenceLimits.MaximumStateBytes
            || document.CombinedActionBytes is < 2
                or > StrategyDurableEvidenceLimits.MaximumCombinedActionBytes
            || document.Actions is null
            || document.Actions.Count > StrategyDurableEvidenceLimits.MaximumActionCount
            || document.ClaimAuthorityNowUtc >= document.ClaimExpiresAtUtc
            || document.PreparedAtUtc < document.ClaimAuthorityNowUtc
            || document.PreparedAtUtc >= document.ClaimExpiresAtUtc
            || !StrategyEvidencePrimitives.IsCanonicalUtcMicroseconds(document.ClaimAuthorityNowUtc)
            || !StrategyEvidencePrimitives.IsCanonicalUtcMicroseconds(document.ClaimExpiresAtUtc)
            || !StrategyEvidencePrimitives.IsCanonicalUtcMicroseconds(document.PreparedAtUtc)
            || !TryParseCanonicalObject(document.EventJson, out JsonObject eventObject)
            || !TryParseCanonicalObject(document.SnapshotJson, out JsonObject snapshotObject)
            || !TryParseCanonicalNode(document.PriorStateJson, out _)
            || !TryParseCanonicalNode(document.NextStateJson, out _)
            || !TryParseCanonicalObject(document.ResultJson, out JsonObject resultObject)
            || !MatchesDigest(document.EventJson, document.EventSha256)
            || !MatchesDigest(document.SnapshotJson, document.SnapshotSha256)
            || !MatchesDigest(document.PriorStateJson, document.PriorStateSha256)
            || !MatchesDigest(document.NextStateJson, document.NextStateSha256)
            || !MatchesDigest(document.ResultJson, document.ResultSha256)
            || Encoding.UTF8.GetByteCount(document.NextStateJson) != document.StateBytes
            || !IsBoundInputEvidence(document)
            || !IsBoundEvent(document, eventObject)
            || !IsBoundSnapshot(document, snapshotObject)
            || !IsBoundResult(document, resultObject))
        {
            return false;
        }

        var actionIds = new HashSet<Guid>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var outboxIds = new HashSet<Guid>();
        for (int index = 0; index < document.Actions.Count; index++)
        {
            StrategyCommittedActionDocument action = document.Actions[index];
            if (action is null
                || action.Ordinal != index
                || action.ActionId == Guid.Empty
                || action.OutboxMessageId == Guid.Empty
                || !StrategyDurableEvidenceLimits.HasSupportedIdempotencyKeyLength(
                    action.IdempotencyKey)
                || !StrategyDurableEvidenceLimits.HasSupportedSymbolLength(action.Symbol)
                || action.MarketDataSequence <= 0
                || !Enum.IsDefined(action.Kind)
                || !Enum.IsDefined(action.ExposureHint)
                || !string.Equals(
                    action.OutboxTopic,
                    RiskEvaluationOutboxTopic,
                    StringComparison.Ordinal)
                || !StrategyDurableEvidenceLimits.HasSupportedActionDocumentSize(
                    action.ActionJson)
                || !StrategyDurableEvidenceLimits.HasSupportedOutboxPayloadDocumentSize(
                    action.OutboxPayloadJson)
                || !TryParseCanonicalObject(action.ActionJson, out JsonObject actionObject)
                || !TryParseCanonicalObject(
                    action.OutboxPayloadJson,
                    out JsonObject outboxObject)
                || !MatchesDigest(action.ActionJson, action.ActionSha256)
                || !MatchesDigest(action.OutboxPayloadJson, action.OutboxPayloadSha256)
                || !IsBoundAction(action, actionObject)
                || !IsBoundOutbox(document, action, outboxObject)
                || !actionIds.Add(action.ActionId)
                || !keys.Add(action.IdempotencyKey)
                || !outboxIds.Add(action.OutboxMessageId))
            {
                return false;
            }
        }

        JsonArray resultActions = resultObject["actions"]!.AsArray();
        string actionsJson = CanonicalJson.Serialize(resultActions);
        return Encoding.UTF8.GetByteCount(actionsJson) == document.CombinedActionBytes;
    }

    private static bool IsBoundInputEvidence(StrategyEventCommitDocument document)
    {
        try
        {
            StrategyEventReference reference = StrategyEventInputEvidence.Restore(
                document.EventJson,
                document.EventSha256,
                document.SnapshotJson,
                document.SnapshotSha256).Reference;
            return reference.DeploymentId == document.DeploymentId
                && reference.WorkerInstanceId == document.WorkerInstanceId
                && reference.Generation == document.Generation
                && reference.Sequence == document.EventSequence
                && reference.EventId == document.EventId
                && reference.EventKind == document.EventKind
                && reference.EventContractVersion == document.EventContractVersion
                && reference.SnapshotSequence == document.SnapshotSequence
                && reference.SnapshotContractVersion == document.SnapshotContractVersion;
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidOperationException or
            JsonException or
            NotSupportedException or
            NullReferenceException or
            OverflowException)
        {
            return false;
        }
    }

    private static StrategyCommittedActionDocument CreateActionDocument(
        TenantExecutionContext context,
        ClaimedStrategyEvent claim,
        long stateVersion,
        RequestedAction action,
        int ordinal,
        Guid outboxMessageId)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!StrategyDurableEvidenceLimits.HasSupportedIdempotencyKeyLength(
                action.IdempotencyKey)
            || !StrategyDurableEvidenceLimits.HasSupportedSymbolLength(action.Symbol)
            || !HasCanonicalActionText(action))
        {
            throw new ArgumentException(
                "The strategy action exceeds durable text bounds.",
                nameof(action));
        }

        if (action is PlaceOrderAction { ExpiresAtUtc: { } expiresAtUtc })
        {
            StrategyEvidencePrimitives.RequireCanonicalUtcMicroseconds(
                expiresAtUtc,
                nameof(action));
        }

        string actionJson = CanonicalJson.Serialize(action);
        if (!StrategyDurableEvidenceLimits.HasSupportedActionDocumentSize(actionJson))
        {
            throw new ArgumentException(
                "The canonical strategy action exceeds durable byte bounds.",
                nameof(action));
        }

        string actionSha256 = StrategyEvidencePrimitives.Sha256Text(actionJson);
        var outbox = new StrategyActionOutboxDocument(
            ContractVersion,
            context.TenantId,
            claim.Reference.DeploymentId,
            claim.Reference.WorkerInstanceId,
            claim.Reference.Generation,
            claim.Reference.Sequence,
            claim.Reference.EventId,
            stateVersion,
            ordinal,
            action.ActionId,
            action.IdempotencyKey,
            action.Kind,
            action.ExposureHint,
            actionSha256);
        string outboxJson = CanonicalJson.Serialize(outbox);
        if (!StrategyDurableEvidenceLimits.HasSupportedOutboxPayloadDocumentSize(outboxJson))
        {
            throw new ArgumentException(
                "The canonical strategy outbox payload exceeds durable byte bounds.",
                nameof(action));
        }

        return new StrategyCommittedActionDocument(
            ordinal,
            action.ActionId,
            action.IdempotencyKey,
            action.Kind,
            action.ExposureHint,
            action.Symbol,
            action.MarketDataSequence,
            actionJson,
            actionSha256,
            outboxMessageId,
            RiskEvaluationOutboxTopic,
            outboxJson,
            StrategyEvidencePrimitives.Sha256Text(outboxJson));
    }

    private static bool MatchesDigest(string? json, string? sha256) =>
        json is not null
        && StrategyEvidencePrimitives.IsDigest(sha256)
        && StrategyEvidencePrimitives.FixedTimeEquals(
            StrategyEvidencePrimitives.Sha256Text(json),
            sha256);

    private static bool TryParseCanonicalNode(string? json, out JsonNode? node)
    {
        node = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            node = JsonNode.Parse(json);
            return node is not null
                && StrategyEvidencePrimitives.FixedTimeEquals(
                    CanonicalJson.Serialize(node),
                    json);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseCanonicalObject(string? json, out JsonObject value)
    {
        value = null!;
        if (!TryParseCanonicalNode(json, out JsonNode? node) || node is not JsonObject objectValue)
        {
            return false;
        }

        value = objectValue;
        return true;
    }

    private static bool IsBoundEvent(
        StrategyEventCommitDocument document,
        JsonObject value)
    {
        string[] envelopeProperties =
        [
            "brokerTimestampUtc",
            "contractVersion",
            "deploymentId",
            "eventId",
            "generation",
            "payload",
            "receivedAtUtc",
            "sequence",
            "workerInstanceId"
        ];
        if (!HasExactProperties(value, envelopeProperties)
            || !HasValue(value, "contractVersion", RuntimeContractVersions.EnvelopeV1)
            || !HasValue(value, "deploymentId", document.DeploymentId)
            || !HasValue(value, "workerInstanceId", document.WorkerInstanceId)
            || !HasValue(value, "generation", document.Generation)
            || !HasValue(value, "sequence", document.EventSequence)
            || !HasValue(value, "eventId", document.EventId)
            || !HasCanonicalTimestamp(value, "receivedAtUtc")
            || !HasOptionalCanonicalTimestamp(value, "brokerTimestampUtc")
            || value["payload"] is not JsonObject payload
            || !HasValue(payload, "contractVersion", document.EventContractVersion)
            || !HasValue(payload, "kind", (int)document.EventKind)
            || !HasCanonicalTimestamp(payload, "occurredAtUtc"))
        {
            return false;
        }

        string discriminator;
        string[] properties;
        switch (document.EventKind)
        {
            case StrategyEventKind.Initialize:
                discriminator = "initialize-v1";
                properties = ["$event", "contractVersion", "kind", "occurredAtUtc", "reasonCode"];
                break;
            case StrategyEventKind.NewTick:
                discriminator = "new-tick-v1";
                properties =
                [
                    "$event", "ask", "bid", "contractVersion", "kind",
                    "marketDataSequence", "occurredAtUtc", "symbol"
                ];
                break;
            case StrategyEventKind.BarClosed:
                discriminator = "bar-closed-v1";
                properties =
                [
                    "$event", "close", "contractVersion", "high", "kind", "low",
                    "marketDataSequence", "occurredAtUtc", "open", "openedAtUtc", "symbol",
                    "tickVolume", "timeframe"
                ];
                if (!HasCanonicalTimestamp(payload, "openedAtUtc"))
                {
                    return false;
                }

                break;
            case StrategyEventKind.Timer:
                discriminator = "timer-v1";
                properties =
                [
                    "$event", "contractVersion", "kind", "occurredAtUtc", "scheduledAtUtc",
                    "timerId"
                ];
                if (!HasCanonicalTimestamp(payload, "scheduledAtUtc"))
                {
                    return false;
                }

                break;
            case StrategyEventKind.Execution:
                discriminator = "execution-v1";
                properties =
                [
                    "$event", "brokerCommandId", "brokerEventId", "contractVersion", "dealId",
                    "executionKind", "fillPrice", "filledVolume", "kind", "occurredAtUtc",
                    "orderId", "reasonCode"
                ];
                break;
            case StrategyEventKind.AccountChanged:
                discriminator = "account-changed-v1";
                properties =
                [
                    "$event", "accountSequence", "contractVersion", "kind", "occurredAtUtc",
                    "reasonCode"
                ];
                break;
            case StrategyEventKind.Stop:
                discriminator = "stop-v1";
                properties = ["$event", "contractVersion", "kind", "occurredAtUtc", "reason"];
                break;
            default:
                return false;
        }

        return HasExactProperties(payload, properties)
            && HasValue(payload, "$event", discriminator);
    }

    private static bool IsBoundSnapshot(
        StrategyEventCommitDocument document,
        JsonObject value)
    {
        string[] rootProperties =
        [
            "account", "asOfUtc", "contractVersion", "deterministicNowUtc", "pendingOrders",
            "positions", "quotes", "sequence"
        ];
        if (!HasExactProperties(value, rootProperties)
            || !HasValue(value, "contractVersion", document.SnapshotContractVersion)
            || !HasValue(value, "sequence", document.SnapshotSequence)
            || !HasCanonicalTimestamp(value, "asOfUtc")
            || !HasCanonicalTimestamp(value, "deterministicNowUtc")
            || value["account"] is not JsonObject account
            || !HasExactProperties(
                account,
                ["balance", "currency", "equity", "freeMargin", "sequence"])
            || value["quotes"] is not JsonArray quotes
            || value["positions"] is not JsonArray positions
            || value["pendingOrders"] is not JsonArray pendingOrders)
        {
            return false;
        }

        return quotes.All(node => node is JsonObject quote
                && HasExactProperties(
                    quote,
                    ["ask", "bid", "observedAtUtc", "sequence", "symbol"])
                && HasCanonicalTimestamp(quote, "observedAtUtc"))
            && positions.All(node => node is JsonObject position
                && HasExactProperties(
                    position,
                    [
                        "openPrice", "ownedByDeployment", "positionId", "side", "stopLoss",
                        "symbol", "takeProfit", "volume"
                    ]))
            && pendingOrders.All(node => node is JsonObject order
                && HasExactProperties(
                    order,
                    [
                        "orderId", "ownedByDeployment", "requestedPrice", "side", "stopLoss",
                        "symbol", "takeProfit", "volume"
                    ]));
    }

    private static bool IsBoundResult(
        StrategyEventCommitDocument document,
        JsonObject value)
    {
        if (!HasExactProperties(value, ["actions", "contractVersion", "state"])
            || !HasValue(value, "contractVersion", RuntimeContractVersions.StrategyResultV1)
            || value["state"] is not JsonObject state
            || !HasExactProperties(state, ["contentHash", "payloadJson", "version"])
            || !HasValue(state, "version", document.NextStateVersion)
            || !HasValue(state, "payloadJson", document.NextStateJson)
            || !HasValue(state, "contentHash", document.NextStateSha256)
            || value["actions"] is not JsonArray actions
            || actions.Count != document.Actions.Count)
        {
            return false;
        }

        for (int index = 0; index < actions.Count; index++)
        {
            if (actions[index] is null
                || document.Actions[index] is not { } committedAction
                || !StrategyEvidencePrimitives.FixedTimeEquals(
                    CanonicalJson.Serialize(actions[index]),
                    committedAction.ActionJson))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsBoundAction(
        StrategyCommittedActionDocument document,
        JsonObject value)
    {
        if (!TryRestoreCanonicalAction(document.ActionJson, out RequestedAction action)
            || action.ActionId != document.ActionId
            || !string.Equals(
                action.IdempotencyKey,
                document.IdempotencyKey,
                StringComparison.Ordinal)
            || action.Kind != document.Kind
            || action.ExposureHint != document.ExposureHint
            || !string.Equals(action.Symbol, document.Symbol, StringComparison.Ordinal)
            || action.MarketDataSequence != document.MarketDataSequence)
        {
            return false;
        }

        string discriminator;
        string[] properties;
        string[] canonicalTextProperties;
        switch (document.Kind)
        {
            case RequestedActionKind.PlaceOrder:
                discriminator = "place-order-v1";
                properties =
                [
                    "$action", "actionId", "expiresAtUtc", "exposureHint", "idempotencyKey",
                    "kind", "marketDataSequence", "maximumDeviationPoints", "orderType",
                    "reasonCode", "requestedPrice", "side", "stopLoss", "symbol", "takeProfit",
                    "volume"
                ];
                canonicalTextProperties = ["reasonCode"];
                if (!HasOptionalCanonicalTimestamp(value, "expiresAtUtc"))
                {
                    return false;
                }

                if (!HasDefinedEnum<RequestedOrderSide>(value, "side")
                    || !HasDefinedEnum<RequestedOrderType>(value, "orderType"))
                {
                    return false;
                }

                break;
            case RequestedActionKind.UpdateProtection:
                discriminator = "update-protection-v1";
                properties =
                [
                    "$action", "actionId", "exposureHint", "idempotencyKey", "kind",
                    "marketDataSequence", "positionId", "reasonCode", "stopLoss", "symbol",
                    "takeProfit"
                ];
                canonicalTextProperties = ["positionId", "reasonCode"];
                break;
            case RequestedActionKind.CancelPendingOrder:
                discriminator = "cancel-pending-order-v1";
                properties =
                [
                    "$action", "actionId", "exposureHint", "idempotencyKey", "kind",
                    "marketDataSequence", "orderId", "reasonCode", "symbol"
                ];
                canonicalTextProperties = ["orderId", "reasonCode"];
                break;
            case RequestedActionKind.ClosePosition:
                discriminator = "close-position-v1";
                properties =
                [
                    "$action", "actionId", "exposureHint", "idempotencyKey", "kind",
                    "marketDataSequence", "positionId", "reasonCode", "symbol", "volume"
                ];
                canonicalTextProperties = ["positionId", "reasonCode"];
                break;
            default:
                return false;
        }

        return HasExactProperties(value, properties)
            && HasValue(value, "$action", discriminator)
            && HasValue(value, "actionId", document.ActionId)
            && HasCanonicalText(value, "idempotencyKey")
            && HasValue(value, "idempotencyKey", document.IdempotencyKey)
            && HasValue(value, "kind", (int)document.Kind)
            && HasValue(value, "exposureHint", (int)document.ExposureHint)
            && HasCanonicalText(value, "symbol")
            && HasValue(value, "symbol", document.Symbol)
            && HasValue(value, "marketDataSequence", document.MarketDataSequence)
            && canonicalTextProperties.All(name => HasCanonicalText(value, name));
    }

    private static bool TryRestoreCanonicalAction(
        string canonicalJson,
        out RequestedAction action)
    {
        action = null!;
        try
        {
            RequestedAction? restored = JsonSerializer.Deserialize<RequestedAction>(
                canonicalJson,
                SerializerOptions);
            if (restored is null
                || !StrategyEvidencePrimitives.FixedTimeEquals(
                    CanonicalJson.Serialize(restored),
                    canonicalJson))
            {
                return false;
            }

            action = restored;
            return true;
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidOperationException or
            JsonException or
            NotSupportedException or
            NullReferenceException or
            OverflowException)
        {
            return false;
        }
    }

    private static bool IsBoundOutbox(
        StrategyEventCommitDocument commit,
        StrategyCommittedActionDocument action,
        JsonObject value) =>
        HasExactProperties(
            value,
            [
                "actionId", "actionKind", "actionOrdinal", "actionSha256", "contractVersion",
                "deploymentId", "eventId", "eventSequence", "exposureHint", "generation",
                "idempotencyKey", "stateVersion", "tenantId", "workerInstanceId"
            ])
        && HasValue(value, "contractVersion", ContractVersion)
        && HasValue(value, "tenantId", commit.TenantId)
        && HasValue(value, "deploymentId", commit.DeploymentId)
        && HasValue(value, "workerInstanceId", commit.WorkerInstanceId)
        && HasValue(value, "generation", commit.Generation)
        && HasValue(value, "eventSequence", commit.EventSequence)
        && HasValue(value, "eventId", commit.EventId)
        && HasValue(value, "stateVersion", commit.NextStateVersion)
        && HasValue(value, "actionOrdinal", action.Ordinal)
        && HasValue(value, "actionId", action.ActionId)
        && HasValue(value, "idempotencyKey", action.IdempotencyKey)
        && HasValue(value, "actionKind", (int)action.Kind)
        && HasValue(value, "exposureHint", (int)action.ExposureHint)
        && HasValue(value, "actionSha256", action.ActionSha256);

    private static bool HasExactProperties(JsonObject value, string[] names) =>
        value.Count == names.Length
        && names.All(value.ContainsKey);

    private static bool HasValue<T>(JsonObject value, string name, T expected)
    {
        if (value[name] is not JsonValue jsonValue
            || !jsonValue.TryGetValue(out T? actual))
        {
            return false;
        }

        return EqualityComparer<T>.Default.Equals(actual, expected);
    }

    private static bool HasDefinedEnum<TEnum>(JsonObject value, string name)
        where TEnum : struct, Enum =>
        value[name] is JsonValue jsonValue
        && jsonValue.TryGetValue(out int rawValue)
        && Enum.IsDefined(typeof(TEnum), rawValue);

    private static bool HasCanonicalText(JsonObject value, string name) =>
        value[name] is JsonValue jsonValue
        && jsonValue.TryGetValue(out string? text)
        && StrategyCanonicalText.IsCanonical(text);

    private static bool HasCanonicalTimestamp(JsonObject value, string name) =>
        value[name] is JsonValue jsonValue
        && jsonValue.TryGetValue(out DateTimeOffset timestamp)
        && StrategyEvidencePrimitives.IsCanonicalUtcMicroseconds(timestamp);

    private static bool HasOptionalCanonicalTimestamp(JsonObject value, string name) =>
        value.ContainsKey(name)
        && (value[name] is null || HasCanonicalTimestamp(value, name));

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

    private sealed record StrategyResultEvidenceDocument(
        int ContractVersion,
        StrategyState State,
        IReadOnlyList<RequestedAction> Actions);
}

public static class StrategyEventReceiptValidator
{
    public static bool IsExactIntake(
        StrategyEventInputEvidence input,
        StrategyEventIntakeReceipt? receipt)
    {
        ArgumentNullException.ThrowIfNull(input);
        return receipt is not null
            && receipt.Reference == input.Reference
            && StrategyEvidencePrimitives.FixedTimeEquals(receipt.EventJson, input.EventJson)
            && StrategyEvidencePrimitives.FixedTimeEquals(receipt.SnapshotJson, input.SnapshotJson)
            && StrategyEvidencePrimitives.IsCanonicalUtcMicroseconds(receipt.PersistedAtUtc);
    }

    public static bool IsExactCommit(
        StrategyEventCommitRequest request,
        StrategyEventCommitReceipt? receipt)
    {
        ArgumentNullException.ThrowIfNull(request);
        return receipt is not null
            && receipt.Evidence is not null
            && StrategyEvidencePrimitives.IsCanonicalUtcMicroseconds(receipt.RecordedAtUtc)
            && IsRecordedWithinClaimWindow(
                request.Evidence.Document,
                receipt.RecordedAtUtc)
            && StrategyEvidencePrimitives.FixedTimeEquals(
                request.Evidence.Sha256,
                receipt.Evidence.Sha256)
            && StrategyEvidencePrimitives.FixedTimeEquals(
                request.Evidence.CanonicalJson,
                receipt.Evidence.CanonicalJson)
            && StrategyEventCommitEvidenceFactory.IsInternallyConsistent(
                receipt.Evidence.Document);
    }

    public static bool IsCommittedReference(
        TenantExecutionContext context,
        StrategyEventReference reference,
        StrategyEventCommitReceipt? receipt)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(reference);
        if (receipt is null
            || receipt.Evidence is null
            || !receipt.Replayed
            || !StrategyEvidencePrimitives.IsCanonicalUtcMicroseconds(receipt.RecordedAtUtc)
            || !StrategyEventCommitEvidenceFactory.IsInternallyConsistent(
                receipt.Evidence.Document))
        {
            return false;
        }

        StrategyEventCommitDocument document = receipt.Evidence.Document;
        return document.TenantId == context.TenantId
            && IsRecordedWithinClaimWindow(document, receipt.RecordedAtUtc)
            && document.DeploymentId == reference.DeploymentId
            && document.WorkerInstanceId == reference.WorkerInstanceId
            && document.Generation == reference.Generation
            && document.EventSequence == reference.Sequence
            && document.EventId == reference.EventId
            && document.EventKind == reference.EventKind
            && document.EventContractVersion == reference.EventContractVersion
            && StrategyEvidencePrimitives.FixedTimeEquals(
                document.EventSha256,
                reference.EventSha256)
            && document.SnapshotSequence == reference.SnapshotSequence
            && document.SnapshotContractVersion == reference.SnapshotContractVersion
            && StrategyEvidencePrimitives.FixedTimeEquals(
                document.SnapshotSha256,
                reference.SnapshotSha256)
            && StrategyEvidencePrimitives.FixedTimeEquals(
                receipt.Evidence.CanonicalJson,
                CanonicalJson.Serialize(document))
            && StrategyEvidencePrimitives.FixedTimeEquals(
                receipt.Evidence.Sha256,
                StrategyEvidencePrimitives.Sha256Text(receipt.Evidence.CanonicalJson));
    }

    private static bool IsRecordedWithinClaimWindow(
        StrategyEventCommitDocument document,
        DateTimeOffset recordedAtUtc) =>
        recordedAtUtc >= document.ClaimAuthorityNowUtc
        && recordedAtUtc < document.ClaimExpiresAtUtc
        && document.PreparedAtUtc - recordedAtUtc <= TimeSpan.FromSeconds(1);
}
