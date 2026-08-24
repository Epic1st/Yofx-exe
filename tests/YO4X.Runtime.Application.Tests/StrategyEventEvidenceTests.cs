using System.Text;
using System.Text.Json.Nodes;
using YO4X.BuildingBlocks;
using YO4X.Runtime.Application;
using YO4X.Runtime.Contracts;
using YO4X.Strategy.Abstractions;

namespace YO4X.Runtime.Application.Tests;

public sealed class StrategyEventEvidenceTests
{
    [Fact]
    public async Task IntakePersistsCanonicalInputAndValidatesExactReplayReceipt()
    {
        var store = new RecordingIntakeStore(replayed: true);
        var coordinator = new StrategyEventIntakeCoordinator(store);
        StrategyEventInputEvidence expected = StrategyRuntimeFixture.Input();

        StrategyEventIntakeResult result = await coordinator.PersistAsync(
            StrategyRuntimeFixture.Context(),
            expected.Envelope,
            expected.Snapshot,
            TestContext.Current.CancellationToken);

        Assert.Equal(StrategyEventIntakeOutcome.AlreadyPersisted, result.Outcome);
        Assert.True(result.IsDurable);
        Assert.Equal(expected.Reference, result.Reference);
        Assert.Equal(expected.EventJson, store.Input?.EventJson);
        Assert.Equal(expected.SnapshotJson, store.Input?.SnapshotJson);
    }

    [Fact]
    public async Task IntakeRejectsReceiptWithChangedCanonicalBytes()
    {
        var store = new RecordingIntakeStore(replayed: false)
        {
            MutateReceipt = receipt => receipt with { EventJson = receipt.EventJson + " " }
        };
        var coordinator = new StrategyEventIntakeCoordinator(store);
        StrategyEventInputEvidence input = StrategyRuntimeFixture.Input();

        StrategyEventIntakeResult result = await coordinator.PersistAsync(
            StrategyRuntimeFixture.Context(),
            input.Envelope,
            input.Snapshot,
            TestContext.Current.CancellationToken);

        Assert.Equal(StrategyEventIntakeOutcome.InvalidReceipt, result.Outcome);
    }

    [Fact]
    public void IntakeRejectsNonCanonicalEnvelopeTimestampBeforePersistence()
    {
        StrategyEventInputEvidence input = StrategyRuntimeFixture.Input();
        var envelope = input.Envelope with
        {
            ReceivedAtUtc = input.Envelope.ReceivedAtUtc.AddTicks(1)
        };

        Assert.Throws<ArgumentException>(() => StrategyEventInputEvidence.Create(
            envelope,
            input.Snapshot));
    }

    [Fact]
    public void InputCreateRejectsUnsupportedEventSubtypeAndInvalidOrdering()
    {
        RuntimeEnvelope<StrategyEvent> envelope = StrategyRuntimeFixture.Envelope();

        Assert.Throws<ArgumentException>(() => StrategyEventInputEvidence.Create(
            envelope with
            {
                Payload = new UnsupportedInputEvent(StrategyRuntimeFixture.Now)
            },
            StrategyRuntimeFixture.Snapshot()));
        Assert.Throws<ArgumentException>(() => StrategyEventInputEvidence.Create(
            envelope with
            {
                Payload = new TimerEvent(
                    StrategyRuntimeFixture.Now,
                    "future-timer",
                    StrategyRuntimeFixture.Now.AddMicroseconds(1))
            },
            StrategyRuntimeFixture.Snapshot()));
    }

    [Fact]
    public void InputCreateRejectsMalformedSnapshotMarketDataAndDuplicateIds()
    {
        StrategySnapshot invertedQuote = StrategySnapshot.Create(
            1,
            StrategyRuntimeFixture.Now,
            StrategyRuntimeFixture.Now,
            new StrategyAccountSnapshot(1, 10_000m, 10_000m, 9_000m, "USD"),
            [new StrategyQuoteSnapshot(
                1,
                "EURUSD",
                1.12m,
                1.11m,
                StrategyRuntimeFixture.Now)]);
        StrategyPositionSnapshot duplicate = new(
            "position-1",
            "EURUSD",
            StrategyPositionSide.Buy,
            0.01m,
            1.10m,
            null,
            null,
            true);
        StrategySnapshot duplicatePositions = StrategySnapshot.Create(
            1,
            StrategyRuntimeFixture.Now,
            StrategyRuntimeFixture.Now,
            new StrategyAccountSnapshot(1, 10_000m, 10_000m, 9_000m, "USD"),
            positions: [duplicate, duplicate]);

        Assert.Throws<ArgumentException>(() => StrategyEventInputEvidence.Create(
            StrategyRuntimeFixture.Envelope(),
            invertedQuote));
        Assert.Throws<ArgumentException>(() => StrategyEventInputEvidence.Create(
            StrategyRuntimeFixture.Envelope(),
            duplicatePositions));
    }

    [Fact]
    public void InputCreateRejectsDuplicateQuoteIdentityButAllowsDistinctSequences()
    {
        StrategyQuoteSnapshot first = new(
            11,
            "EURUSD",
            1.10m,
            1.11m,
            StrategyRuntimeFixture.Now);
        StrategySnapshot duplicateIdentity = StrategySnapshot.Create(
            1,
            StrategyRuntimeFixture.Now,
            StrategyRuntimeFixture.Now,
            new StrategyAccountSnapshot(1, 10_000m, 10_000m, 9_000m, "USD"),
            [first, first with { Bid = 1.105m }]);

        Assert.Throws<ArgumentException>(() => StrategyEventInputEvidence.Create(
            StrategyRuntimeFixture.Envelope(),
            duplicateIdentity));

        StrategySnapshot distinctSequences = StrategySnapshot.Create(
            1,
            StrategyRuntimeFixture.Now,
            StrategyRuntimeFixture.Now,
            new StrategyAccountSnapshot(1, 10_000m, 10_000m, 9_000m, "USD"),
            [first, first with { Sequence = 10 }]);
        StrategyEventInputEvidence accepted = StrategyEventInputEvidence.Create(
            StrategyRuntimeFixture.Envelope(),
            distinctSequences);

        Assert.Equal(
            [10L, 11L],
            accepted.Snapshot.Quotes.Select(value => value.Sequence));
    }

    [Fact]
    public void InputCreateRejectsAmbiguousTextAndAcceptsValidReplacementScalar()
    {
        string[] invalidValues =
        [
            " leading",
            "trailing ",
            "line\nfeed",
            "bidi\u200Econtrol",
            "high\uD800surrogate",
            "low\uDC00surrogate"
        ];

        foreach (string invalid in invalidValues)
        {
            RuntimeEnvelope<StrategyEvent> invalidEvent = StrategyRuntimeFixture.Envelope() with
            {
                Payload = new InitializeEvent(StrategyRuntimeFixture.Now, invalid)
            };
            StrategySnapshot invalidCurrency = StrategySnapshot.Create(
                1,
                StrategyRuntimeFixture.Now,
                StrategyRuntimeFixture.Now,
                new StrategyAccountSnapshot(1, 10_000m, 10_000m, 9_000m, invalid));

            Assert.Throws<ArgumentException>(() => StrategyEventInputEvidence.Create(
                invalidEvent,
                StrategyRuntimeFixture.Snapshot()));
            Assert.Throws<ArgumentException>(() => StrategyEventInputEvidence.Create(
                StrategyRuntimeFixture.Envelope(),
                invalidCurrency));
        }

        const string validReplacement = "value-\uFFFD-text";
        StrategySnapshot validSnapshot = StrategySnapshot.Create(
            1,
            StrategyRuntimeFixture.Now,
            StrategyRuntimeFixture.Now,
            new StrategyAccountSnapshot(1, 10_000m, 10_000m, 9_000m, "U\uFFFD"),
            [new StrategyQuoteSnapshot(
                1,
                "EUR\uFFFDUSD",
                1.10m,
                1.11m,
                StrategyRuntimeFixture.Now)]);

        StrategyEventInputEvidence accepted = StrategyEventInputEvidence.Create(
            StrategyRuntimeFixture.Envelope() with
            {
                Payload = new InitializeEvent(StrategyRuntimeFixture.Now, validReplacement)
            },
            validSnapshot);

        Assert.Contains("\\ufffd", accepted.EventJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\\ufffd", accepted.SnapshotJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InputCreateRejectsInvalidUnicodeScalarsInSymbolsAndIdentifiers()
    {
        const string invalidScalar = "invalid\uD800text";
        RuntimeEnvelope<StrategyEvent> invalidTimer = StrategyRuntimeFixture.Envelope() with
        {
            Payload = new TimerEvent(
                StrategyRuntimeFixture.Now,
                invalidScalar,
                StrategyRuntimeFixture.Now)
        };
        StrategySnapshot invalidQuote = StrategySnapshot.Create(
            1,
            StrategyRuntimeFixture.Now,
            StrategyRuntimeFixture.Now,
            new StrategyAccountSnapshot(1, 10_000m, 10_000m, 9_000m, "USD"),
            [new StrategyQuoteSnapshot(
                1,
                invalidScalar,
                1.10m,
                1.11m,
                StrategyRuntimeFixture.Now)]);
        StrategySnapshot invalidPosition = StrategySnapshot.Create(
            1,
            StrategyRuntimeFixture.Now,
            StrategyRuntimeFixture.Now,
            new StrategyAccountSnapshot(1, 10_000m, 10_000m, 9_000m, "USD"),
            positions:
            [
                new StrategyPositionSnapshot(
                    invalidScalar,
                    "EURUSD",
                    StrategyPositionSide.Buy,
                    0.01m,
                    1.10m,
                    null,
                    null,
                    true)
            ]);
        StrategySnapshot invalidOrder = StrategySnapshot.Create(
            1,
            StrategyRuntimeFixture.Now,
            StrategyRuntimeFixture.Now,
            new StrategyAccountSnapshot(1, 10_000m, 10_000m, 9_000m, "USD"),
            pendingOrders:
            [
                new StrategyPendingOrderSnapshot(
                    invalidScalar,
                    "EURUSD",
                    StrategyPositionSide.Buy,
                    0.01m,
                    1.10m,
                    null,
                    null,
                    true)
            ]);

        Assert.Throws<ArgumentException>(() => StrategyEventInputEvidence.Create(
            invalidTimer,
            StrategyRuntimeFixture.Snapshot()));
        Assert.Throws<ArgumentException>(() => StrategyEventInputEvidence.Create(
            StrategyRuntimeFixture.Envelope(),
            invalidQuote));
        Assert.Throws<ArgumentException>(() => StrategyEventInputEvidence.Create(
            StrategyRuntimeFixture.Envelope(),
            invalidPosition));
        Assert.Throws<ArgumentException>(() => StrategyEventInputEvidence.Create(
            StrategyRuntimeFixture.Envelope(),
            invalidOrder));
    }

    [Fact]
    public void InputCreateRejectsUndefinedSnapshotEnumsAndOversizedText()
    {
        StrategySnapshot undefinedSide = StrategySnapshot.Create(
            1,
            StrategyRuntimeFixture.Now,
            StrategyRuntimeFixture.Now,
            new StrategyAccountSnapshot(1, 10_000m, 10_000m, 9_000m, "USD"),
            positions:
            [
                new StrategyPositionSnapshot(
                    "position-1",
                    "EURUSD",
                    (StrategyPositionSide)99,
                    0.01m,
                    1.10m,
                    null,
                    null,
                    true)
            ]);
        StrategySnapshot oversizedCurrency = StrategySnapshot.Create(
            1,
            StrategyRuntimeFixture.Now,
            StrategyRuntimeFixture.Now,
            new StrategyAccountSnapshot(
                1,
                10_000m,
                10_000m,
                9_000m,
                new string('C', 21)));
        StrategySnapshot oversizedSymbol = StrategySnapshot.Create(
            1,
            StrategyRuntimeFixture.Now,
            StrategyRuntimeFixture.Now,
            new StrategyAccountSnapshot(1, 10_000m, 10_000m, 9_000m, "USD"),
            [new StrategyQuoteSnapshot(
                1,
                new string('S', StrategyDurableEvidenceLimits.MaximumSymbolCharacters + 1),
                1.10m,
                1.11m,
                StrategyRuntimeFixture.Now)]);

        Assert.Throws<ArgumentException>(() => StrategyEventInputEvidence.Create(
            StrategyRuntimeFixture.Envelope(),
            undefinedSide));
        Assert.Throws<ArgumentException>(() => StrategyEventInputEvidence.Create(
            StrategyRuntimeFixture.Envelope(),
            oversizedCurrency));
        Assert.Throws<ArgumentException>(() => StrategyEventInputEvidence.Create(
            StrategyRuntimeFixture.Envelope(),
            oversizedSymbol));
    }

    [Fact]
    public void CanonicalInputEvidenceRoundTripsThroughApplicationHydrator()
    {
        StrategyEventInputEvidence original = StrategyRuntimeFixture.Input();

        StrategyEventInputEvidence restored = StrategyEventInputEvidence.Restore(
            original.EventJson,
            original.Reference.EventSha256,
            original.SnapshotJson,
            original.Reference.SnapshotSha256);

        Assert.Equal(original.Reference, restored.Reference);
        Assert.Equal(original.EventJson, restored.EventJson);
        Assert.Equal(original.SnapshotJson, restored.SnapshotJson);
    }

    [Fact]
    public void InputRestoreRejectsNonCanonicalSnapshotCollectionOrder()
    {
        StrategySnapshot snapshot = StrategySnapshot.Create(
            1,
            StrategyRuntimeFixture.Now,
            StrategyRuntimeFixture.Now,
            new StrategyAccountSnapshot(1, 10_000m, 10_000m, 9_000m, "USD"),
            [
                new StrategyQuoteSnapshot(
                    2,
                    "EURUSD",
                    1.11m,
                    1.12m,
                    StrategyRuntimeFixture.Now),
                new StrategyQuoteSnapshot(
                    1,
                    "EURUSD",
                    1.10m,
                    1.11m,
                    StrategyRuntimeFixture.Now)
            ]);
        StrategyEventInputEvidence original = StrategyEventInputEvidence.Create(
            StrategyRuntimeFixture.Envelope(),
            snapshot);
        JsonObject snapshotNode = JsonNode.Parse(original.SnapshotJson)!.AsObject();
        JsonArray quotes = snapshotNode["quotes"]!.AsArray();
        JsonNode first = quotes[0]!.DeepClone();
        JsonNode second = quotes[1]!.DeepClone();
        quotes.Clear();
        quotes.Add(second);
        quotes.Add(first);
        string nonCanonicalSnapshot = CanonicalJson.Serialize(snapshotNode);

        Assert.Throws<ArgumentException>(() => StrategyEventInputEvidence.Restore(
            original.EventJson,
            original.Reference.EventSha256,
            nonCanonicalSnapshot,
            CanonicalJson.Sha256(snapshotNode)));
    }

    [Fact]
    public void InputRestoreRejectsUndefinedSnapshotSideWithRecomputedDigest()
    {
        StrategyEventInputEvidence original = StrategyRuntimeFixture.Input();
        JsonObject snapshot = JsonNode.Parse(original.SnapshotJson)!.AsObject();
        snapshot["positions"] = new JsonArray
        {
            new JsonObject
            {
                ["openPrice"] = 1.10m,
                ["ownedByDeployment"] = true,
                ["positionId"] = "position-1",
                ["side"] = 99,
                ["stopLoss"] = null,
                ["symbol"] = "EURUSD",
                ["takeProfit"] = null,
                ["volume"] = 0.01m
            }
        };
        string forgedSnapshot = CanonicalJson.Serialize(snapshot);

        Assert.Throws<ArgumentException>(() => StrategyEventInputEvidence.Restore(
            original.EventJson,
            original.Reference.EventSha256,
            forgedSnapshot,
            CanonicalJson.Sha256(snapshot)));
    }

    [Fact]
    public void InputRestoreRejectsUndefinedExecutionKindWithRecomputedDigest()
    {
        RuntimeEnvelope<StrategyEvent> envelope = StrategyRuntimeFixture.Envelope() with
        {
            Payload = new ExecutionEvent(
                StrategyRuntimeFixture.Now,
                Guid.Parse("83000000-0000-0000-0000-000000000080"),
                "broker-event-1",
                StrategyExecutionEventKind.Acknowledged,
                "order-1",
                null,
                0,
                null,
                "acknowledged")
        };
        StrategyEventInputEvidence original = StrategyEventInputEvidence.Create(
            envelope,
            StrategyRuntimeFixture.Snapshot());
        JsonObject eventNode = JsonNode.Parse(original.EventJson)!.AsObject();
        eventNode["payload"]!["executionKind"] = 99;
        string forgedEvent = CanonicalJson.Serialize(eventNode);

        Assert.Throws<ArgumentException>(() => StrategyEventInputEvidence.Restore(
            forgedEvent,
            CanonicalJson.Sha256(eventNode),
            original.SnapshotJson,
            original.Reference.SnapshotSha256));
    }

    [Fact]
    public void DurableInputDocumentBoundsUseExactUtf8Bytes()
    {
        string eventAtLimit = string.Concat(
            '"',
            new string('e', StrategyDurableEvidenceLimits.MaximumEventDocumentBytes - 2),
            '"');
        string snapshotAtLimit = string.Concat(
            '"',
            new string(
                's',
                StrategyDurableEvidenceLimits.MaximumSnapshotDocumentBytes - 2),
            '"');

        Assert.True(StrategyDurableEvidenceLimits.HasSupportedEventDocumentSize(
            eventAtLimit));
        Assert.False(StrategyDurableEvidenceLimits.HasSupportedEventDocumentSize(
            string.Concat(eventAtLimit[..^1], "e\"")));
        Assert.True(StrategyDurableEvidenceLimits.HasSupportedSnapshotDocumentSize(
            snapshotAtLimit));
        Assert.False(StrategyDurableEvidenceLimits.HasSupportedSnapshotDocumentSize(
            string.Concat(snapshotAtLimit[..^1], "s\"")));
    }

    [Fact]
    public void InputRestoreRejectsOversizedEventBeforeJsonHydration()
    {
        StrategyEventInputEvidence valid = StrategyRuntimeFixture.Input();
        string oversized = string.Concat(
            "{",
            new string(' ', StrategyDurableEvidenceLimits.MaximumEventDocumentBytes));

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            StrategyEventInputEvidence.Restore(
                oversized,
                new string('0', 64),
                valid.SnapshotJson,
                valid.Reference.SnapshotSha256));

        Assert.Contains("byte bounds", exception.Message, StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void InputRestoreRejectsOversizedSnapshotBeforeJsonHydration()
    {
        StrategyEventInputEvidence valid = StrategyRuntimeFixture.Input();
        string oversized = string.Concat(
            "{",
            new string(' ', StrategyDurableEvidenceLimits.MaximumSnapshotDocumentBytes));

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            StrategyEventInputEvidence.Restore(
                valid.EventJson,
                valid.Reference.EventSha256,
                oversized,
                new string('0', 64)));

        Assert.Contains("byte bounds", exception.Message, StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task IntakeRejectsOversizedEventBeforeStoreCall()
    {
        var store = new RecordingIntakeStore(replayed: false);
        var coordinator = new StrategyEventIntakeCoordinator(store);

        await Assert.ThrowsAsync<ArgumentException>(() => coordinator.PersistAsync(
            StrategyRuntimeFixture.Context(),
            CreateEnvelopeWithBytes(
                StrategyDurableEvidenceLimits.MaximumEventDocumentBytes + 1),
            StrategyRuntimeFixture.Snapshot(),
            TestContext.Current.CancellationToken));

        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task IntakeRejectsOversizedSnapshotBeforeStoreCall()
    {
        var store = new RecordingIntakeStore(replayed: false);
        var coordinator = new StrategyEventIntakeCoordinator(store);

        await Assert.ThrowsAsync<ArgumentException>(() => coordinator.PersistAsync(
            StrategyRuntimeFixture.Context(),
            StrategyRuntimeFixture.Envelope(),
            CreateSnapshotWithBytes(
                StrategyDurableEvidenceLimits.MaximumSnapshotDocumentBytes + 1),
            TestContext.Current.CancellationToken));

        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task RestoreRejectsNestedEventTamperEvenWithRecomputedOuterDigests()
    {
        StrategyEventCommitEvidence original = await ProduceEvidenceAsync();
        StrategyEventCommitDocument document = original.Document;
        JsonObject eventNode = JsonNode.Parse(document.EventJson)!.AsObject();
        eventNode["forged"] = true;
        string eventJson = CanonicalJson.Serialize(eventNode);
        StrategyEventCommitDocument forged = document with
        {
            EventJson = eventJson,
            EventSha256 = CanonicalJson.Sha256(eventNode)
        };
        string forgedJson = CanonicalJson.Serialize(forged);

        Assert.Throws<ArgumentException>(() => StrategyEventCommitEvidence.Restore(
            forgedJson,
            CanonicalJson.Sha256(forged)));
    }

    [Fact]
    public async Task RestoreRejectsNestedActionTamperWithMatchingNestedAndOuterDigests()
    {
        StrategyEventCommitEvidence original = await ProduceEvidenceAsync();
        StrategyCommittedActionDocument action = original.Document.Actions[0];
        JsonObject actionNode = JsonNode.Parse(action.ActionJson)!.AsObject();
        actionNode["idempotencyKey"] = "forged-key";
        string actionJson = CanonicalJson.Serialize(actionNode);
        StrategyCommittedActionDocument forgedAction = action with
        {
            IdempotencyKey = "forged-key",
            ActionJson = actionJson,
            ActionSha256 = CanonicalJson.Sha256(actionNode)
        };
        StrategyEventCommitDocument forged = original.Document with
        {
            Actions = [forgedAction]
        };
        string forgedJson = CanonicalJson.Serialize(forged);

        Assert.Throws<ArgumentException>(() => StrategyEventCommitEvidence.Restore(
            forgedJson,
            CanonicalJson.Sha256(forged)));
    }

    [Theory]
    [InlineData("side")]
    [InlineData("orderType")]
    public async Task RestoreRejectsUndefinedPlaceOrderEnumsWithExactBindings(string propertyName)
    {
        StrategyEventCommitEvidence original = await ProduceEvidenceAsync();
        StrategyCommittedActionDocument action = original.Document.Actions[0];
        JsonObject actionNode = JsonNode.Parse(action.ActionJson)!.AsObject();
        actionNode[propertyName] = 99;
        string actionJson = CanonicalJson.Serialize(actionNode);
        string actionSha256 = CanonicalJson.Sha256(actionNode);

        JsonObject outboxNode = JsonNode.Parse(action.OutboxPayloadJson)!.AsObject();
        outboxNode["actionSha256"] = actionSha256;
        string outboxJson = CanonicalJson.Serialize(outboxNode);
        var forgedAction = action with
        {
            ActionJson = actionJson,
            ActionSha256 = actionSha256,
            OutboxPayloadJson = outboxJson,
            OutboxPayloadSha256 = CanonicalJson.Sha256(outboxNode)
        };

        JsonObject resultNode = JsonNode.Parse(original.Document.ResultJson)!.AsObject();
        JsonArray resultActions = resultNode["actions"]!.AsArray();
        resultActions[0] = actionNode.DeepClone();
        string resultJson = CanonicalJson.Serialize(resultNode);
        StrategyEventCommitDocument forged = original.Document with
        {
            Actions = [forgedAction],
            ResultJson = resultJson,
            ResultSha256 = CanonicalJson.Sha256(resultNode),
            CombinedActionBytes = Encoding.UTF8.GetByteCount(
                CanonicalJson.Serialize(resultActions))
        };
        string forgedJson = CanonicalJson.Serialize(forged);

        Assert.Throws<ArgumentException>(() => StrategyEventCommitEvidence.Restore(
            forgedJson,
            CanonicalJson.Sha256(forged)));
    }

    [Theory]
    [InlineData("place-volume-zero")]
    [InlineData("place-requested-price-zero")]
    [InlineData("place-stop-loss-zero")]
    [InlineData("place-take-profit-zero")]
    [InlineData("place-negative-deviation")]
    [InlineData("place-market-sequence-zero")]
    [InlineData("place-wrong-exposure")]
    [InlineData("update-stop-loss-zero")]
    [InlineData("update-wrong-exposure")]
    [InlineData("cancel-wrong-exposure")]
    [InlineData("close-volume-zero")]
    [InlineData("close-wrong-exposure")]
    public async Task RestoreRejectsActionConstructorInvariantTampering(
        string mutation)
    {
        RequestedAction sourceAction = CreateActionForMutation(mutation);
        StrategyEventCommitEvidence original = await ProduceEvidenceAsync(sourceAction);
        StrategyCommittedActionDocument action = original.Document.Actions[0];
        JsonObject actionNode = JsonNode.Parse(action.ActionJson)!.AsObject();
        RequestedExposureHint? forgedExposure = null;
        long? forgedMarketDataSequence = null;

        switch (mutation)
        {
            case "place-volume-zero":
            case "close-volume-zero":
                actionNode["volume"] = 0;
                break;
            case "place-requested-price-zero":
                actionNode["requestedPrice"] = 0;
                break;
            case "place-stop-loss-zero":
            case "update-stop-loss-zero":
                actionNode["stopLoss"] = 0;
                break;
            case "place-take-profit-zero":
                actionNode["takeProfit"] = 0;
                break;
            case "place-negative-deviation":
                actionNode["maximumDeviationPoints"] = -1;
                break;
            case "place-market-sequence-zero":
                actionNode["marketDataSequence"] = 0;
                forgedMarketDataSequence = 0;
                break;
            case "place-wrong-exposure":
                actionNode["exposureHint"] = (int)RequestedExposureHint.Protect;
                forgedExposure = RequestedExposureHint.Protect;
                break;
            case "update-wrong-exposure":
                actionNode["exposureHint"] = (int)RequestedExposureHint.Reduce;
                forgedExposure = RequestedExposureHint.Reduce;
                break;
            case "cancel-wrong-exposure":
                actionNode["exposureHint"] = (int)RequestedExposureHint.Protect;
                forgedExposure = RequestedExposureHint.Protect;
                break;
            case "close-wrong-exposure":
                actionNode["exposureHint"] = (int)RequestedExposureHint.Increase;
                forgedExposure = RequestedExposureHint.Increase;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        string actionJson = CanonicalJson.Serialize(actionNode);
        string actionSha256 = CanonicalJson.Sha256(actionNode);
        JsonObject outboxNode = JsonNode.Parse(action.OutboxPayloadJson)!.AsObject();
        outboxNode["actionSha256"] = actionSha256;
        if (forgedExposure is { } exposure)
        {
            outboxNode["exposureHint"] = (int)exposure;
        }

        string outboxJson = CanonicalJson.Serialize(outboxNode);
        StrategyCommittedActionDocument forgedAction = action with
        {
            ExposureHint = forgedExposure ?? action.ExposureHint,
            MarketDataSequence = forgedMarketDataSequence ?? action.MarketDataSequence,
            ActionJson = actionJson,
            ActionSha256 = actionSha256,
            OutboxPayloadJson = outboxJson,
            OutboxPayloadSha256 = CanonicalJson.Sha256(outboxNode)
        };

        JsonObject resultNode = JsonNode.Parse(original.Document.ResultJson)!.AsObject();
        JsonArray resultActions = resultNode["actions"]!.AsArray();
        resultActions[0] = actionNode.DeepClone();
        string resultJson = CanonicalJson.Serialize(resultNode);
        StrategyEventCommitDocument forged = original.Document with
        {
            Actions = [forgedAction],
            ResultJson = resultJson,
            ResultSha256 = CanonicalJson.Sha256(resultNode),
            CombinedActionBytes = Encoding.UTF8.GetByteCount(
                CanonicalJson.Serialize(resultActions))
        };
        string forgedJson = CanonicalJson.Serialize(forged);

        Assert.Throws<ArgumentException>(() => StrategyEventCommitEvidence.Restore(
            forgedJson,
            CanonicalJson.Sha256(forged)));
    }

    [Fact]
    public async Task RestoreRejectsNonCanonicalActionTextWithRecomputedBindings()
    {
        StrategyEventCommitEvidence original = await ProduceEvidenceAsync();
        string[] invalidReasonCodes = [" leading", "control\u0001value", "bidi\u200Evalue"];

        foreach (string invalidReasonCode in invalidReasonCodes)
        {
            StrategyCommittedActionDocument action = original.Document.Actions[0];
            JsonObject actionNode = JsonNode.Parse(action.ActionJson)!.AsObject();
            actionNode["reasonCode"] = invalidReasonCode;
            string actionJson = CanonicalJson.Serialize(actionNode);
            string actionSha256 = CanonicalJson.Sha256(actionNode);

            JsonObject outboxNode = JsonNode.Parse(action.OutboxPayloadJson)!.AsObject();
            outboxNode["actionSha256"] = actionSha256;
            string outboxJson = CanonicalJson.Serialize(outboxNode);
            var forgedAction = action with
            {
                ActionJson = actionJson,
                ActionSha256 = actionSha256,
                OutboxPayloadJson = outboxJson,
                OutboxPayloadSha256 = CanonicalJson.Sha256(outboxNode)
            };

            JsonObject resultNode = JsonNode.Parse(original.Document.ResultJson)!.AsObject();
            JsonArray resultActions = resultNode["actions"]!.AsArray();
            resultActions[0] = actionNode.DeepClone();
            string resultJson = CanonicalJson.Serialize(resultNode);
            StrategyEventCommitDocument forged = original.Document with
            {
                Actions = [forgedAction],
                ResultJson = resultJson,
                ResultSha256 = CanonicalJson.Sha256(resultNode),
                CombinedActionBytes = Encoding.UTF8.GetByteCount(
                    CanonicalJson.Serialize(resultActions))
            };
            string forgedJson = CanonicalJson.Serialize(forged);

            Assert.Throws<ArgumentException>(() => StrategyEventCommitEvidence.Restore(
                forgedJson,
                CanonicalJson.Sha256(forged)));
        }
    }

    [Fact]
    public async Task RestoreRejectsRecomputedByteCountDrift()
    {
        StrategyEventCommitEvidence original = await ProduceEvidenceAsync();
        StrategyEventCommitDocument forged = original.Document with
        {
            StateBytes = original.Document.StateBytes + 1,
            CombinedActionBytes = original.Document.CombinedActionBytes + 1
        };
        string forgedJson = CanonicalJson.Serialize(forged);

        Assert.Throws<ArgumentException>(() => StrategyEventCommitEvidence.Restore(
            forgedJson,
            CanonicalJson.Sha256(forged)));
    }

    [Fact]
    public async Task RestoreRejectsPreparedTimeBeforeDatabaseClaimAuthority()
    {
        StrategyEventCommitEvidence original = await ProduceEvidenceAsync();
        StrategyEventCommitDocument forged = original.Document with
        {
            PreparedAtUtc = original.Document.ClaimAuthorityNowUtc.AddMicroseconds(-1)
        };
        string forgedJson = CanonicalJson.Serialize(forged);

        Assert.Throws<ArgumentException>(() => StrategyEventCommitEvidence.Restore(
            forgedJson,
            CanonicalJson.Sha256(forged)));
    }

    [Fact]
    public async Task RestoreRejectsPreparedTimeAtExclusiveClaimExpiry()
    {
        StrategyEventCommitEvidence original = await ProduceEvidenceAsync();
        StrategyEventCommitDocument forged = original.Document with
        {
            PreparedAtUtc = original.Document.ClaimExpiresAtUtc
        };
        string forgedJson = CanonicalJson.Serialize(forged);

        Assert.Throws<ArgumentException>(() => StrategyEventCommitEvidence.Restore(
            forgedJson,
            CanonicalJson.Sha256(forged)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_048_577)]
    public async Task RestoreRejectsStateByteBounds(int forgedStateBytes)
    {
        StrategyEventCommitEvidence original = await ProduceEvidenceAsync();
        StrategyEventCommitDocument forged = original.Document with
        {
            StateBytes = forgedStateBytes
        };
        string forgedJson = CanonicalJson.Serialize(forged);

        Assert.Throws<ArgumentException>(() => StrategyEventCommitEvidence.Restore(
            forgedJson,
            CanonicalJson.Sha256(forged)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4_194_305)]
    public async Task RestoreRejectsCombinedActionByteBounds(int forgedActionBytes)
    {
        StrategyEventCommitEvidence original = await ProduceEvidenceAsync();
        StrategyEventCommitDocument forged = original.Document with
        {
            CombinedActionBytes = forgedActionBytes
        };
        string forgedJson = CanonicalJson.Serialize(forged);

        Assert.Throws<ArgumentException>(() => StrategyEventCommitEvidence.Restore(
            forgedJson,
            CanonicalJson.Sha256(forged)));
    }

    [Fact]
    public async Task RestoreRejectsMoreThanTwoHundredFiftySixActions()
    {
        StrategyEventCommitEvidence original = await ProduceEvidenceAsync();
        StrategyCommittedActionDocument action = Assert.Single(original.Document.Actions);
        StrategyEventCommitDocument forged = original.Document with
        {
            Actions = Enumerable.Repeat(action, 257).ToArray()
        };
        string forgedJson = CanonicalJson.Serialize(forged);

        Assert.Throws<ArgumentException>(() => StrategyEventCommitEvidence.Restore(
            forgedJson,
            CanonicalJson.Sha256(forged)));
    }

    [Fact]
    public void RestoreRejectsOversizedEvidenceBeforeJsonHydration()
    {
        string oversized = string.Concat("{", new string(' ', 8 * 1024 * 1024));

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            StrategyEventCommitEvidence.Restore(
            oversized,
            new string('0', 64)));

        Assert.Contains("byte bounds", exception.Message, StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [InlineData("event", 1_048_577)]
    [InlineData("snapshot", 4_194_305)]
    [InlineData("prior-state", 1_048_577)]
    [InlineData("next-state", 1_048_577)]
    public async Task CommitRestoreRejectsNestedEvidenceBeyondSqlByteLimits(
        string evidenceKind,
        int oversizedBytes)
    {
        StrategyEventCommitEvidence original = await ProduceEvidenceAsync();
        string oversizedJson = CreateCanonicalObjectWithBytes(oversizedBytes);
        JsonNode oversizedNode = JsonNode.Parse(oversizedJson)!;
        string oversizedSha256 = CanonicalJson.Sha256(oversizedNode);
        StrategyEventCommitDocument forged = evidenceKind switch
        {
            "event" => original.Document with
            {
                EventJson = oversizedJson,
                EventSha256 = oversizedSha256
            },
            "snapshot" => original.Document with
            {
                SnapshotJson = oversizedJson,
                SnapshotSha256 = oversizedSha256
            },
            "prior-state" => original.Document with
            {
                PriorStateJson = oversizedJson,
                PriorStateSha256 = oversizedSha256
            },
            "next-state" => original.Document with
            {
                NextStateJson = oversizedJson,
                NextStateSha256 = oversizedSha256,
                StateBytes = oversizedBytes
            },
            _ => throw new ArgumentOutOfRangeException(nameof(evidenceKind))
        };
        string forgedJson = CanonicalJson.Serialize(forged);

        Assert.Throws<ArgumentException>(() => StrategyEventCommitEvidence.Restore(
            forgedJson,
            CanonicalJson.Sha256(forged)));
    }

    [Fact]
    public async Task CommitEvidenceRejectsOversizedIdempotencyKeyBeforeStoreCall()
    {
        PlaceOrderAction action = CreatePlaceAction(
            idempotencyKey: new string('k',
                StrategyDurableEvidenceLimits.MaximumIdempotencyKeyCharacters + 1));

        (StrategyEventProcessingResult result, RecordingStrategyStore store) =
            await ProcessActionAsync(action);

        Assert.Equal(StrategyEventProcessingOutcome.InvalidResult, result.Outcome);
        Assert.Equal("strategy_commit_evidence_invalid", result.Code);
        Assert.Equal(0, store.CommitCalls);
    }

    [Fact]
    public async Task CommitEvidenceRejectsOversizedSymbolBeforeStoreCall()
    {
        PlaceOrderAction action = CreatePlaceAction(
            symbol: new string('S', StrategyDurableEvidenceLimits.MaximumSymbolCharacters + 1));

        (StrategyEventProcessingResult result, RecordingStrategyStore store) =
            await ProcessActionAsync(action);

        Assert.Equal(StrategyEventProcessingOutcome.InvalidResult, result.Outcome);
        Assert.Equal("strategy_commit_evidence_invalid", result.Code);
        Assert.Equal(0, store.CommitCalls);
    }

    [Fact]
    public async Task CommitEvidenceRejectsOversizedIndividualActionBeforeStoreCall()
    {
        PlaceOrderAction action = CreatePlaceAction(
            reasonCode: new string(
                'r',
                StrategyDurableEvidenceLimits.MaximumActionDocumentBytes));

        (StrategyEventProcessingResult result, RecordingStrategyStore store) =
            await ProcessActionAsync(action);

        Assert.Equal(StrategyEventProcessingOutcome.InvalidResult, result.Outcome);
        Assert.Equal("strategy_commit_evidence_invalid", result.Code);
        Assert.Equal(0, store.CommitCalls);
    }

    [Fact]
    public void DurableDocumentBoundsUseExactUtf8Bytes()
    {
        string atLimit = string.Concat(
            '"',
            new string(
                'x',
                StrategyDurableEvidenceLimits.MaximumOutboxPayloadDocumentBytes - 2),
            '"');
        string overLimit = string.Concat(atLimit[..^1], "x\"");

        Assert.True(
            StrategyDurableEvidenceLimits.HasSupportedOutboxPayloadDocumentSize(atLimit));
        Assert.False(
            StrategyDurableEvidenceLimits.HasSupportedOutboxPayloadDocumentSize(overLimit));
    }

    [Fact]
    public void DurableTextBoundsCountUnicodeScalarsLikePostgres()
    {
        string atLimit = string.Concat(
            Enumerable.Repeat(
                "📈",
                StrategyDurableEvidenceLimits.MaximumIdempotencyKeyCharacters));
        string overLimit = string.Concat(atLimit, "📈");

        Assert.True(
            StrategyDurableEvidenceLimits.HasSupportedIdempotencyKeyLength(atLimit));
        Assert.False(
            StrategyDurableEvidenceLimits.HasSupportedIdempotencyKeyLength(overLimit));
    }

    [Fact]
    public void DurableTextBoundsRejectAmbiguousTextWithoutBanningReplacementScalar()
    {
        string[] invalidValues =
        [
            " leading",
            "trailing ",
            "control\u0001value",
            "format\u200Evalue",
            "high\uD800surrogate",
            "low\uDC00surrogate"
        ];

        Assert.All(invalidValues, value =>
        {
            Assert.False(StrategyDurableEvidenceLimits.HasSupportedIdempotencyKeyLength(value));
            Assert.False(StrategyDurableEvidenceLimits.HasSupportedSymbolLength(value));
        });
        Assert.True(StrategyDurableEvidenceLimits.HasSupportedIdempotencyKeyLength(
            "replacement-\uFFFD-character"));
        Assert.True(StrategyDurableEvidenceLimits.HasSupportedSymbolLength(
            "EUR\uFFFDUSD"));
    }

    [Fact]
    public async Task CanonicalCommitEvidenceRoundTripsExactly()
    {
        StrategyEventCommitEvidence original = await ProduceEvidenceAsync();

        StrategyEventCommitEvidence restored = StrategyEventCommitEvidence.Restore(
            original.CanonicalJson,
            original.Sha256);

        Assert.Equal(original.CanonicalJson, restored.CanonicalJson);
        Assert.Equal(original.Sha256, restored.Sha256);
        Assert.Equal(original.Document.CommitId, restored.Document.CommitId);
        Assert.Equal(
            original.Document.Actions,
            restored.Document.Actions);
    }

    private static async Task<StrategyEventCommitEvidence> ProduceEvidenceAsync(
        RequestedAction? requestedAction = null)
    {
        var store = new RecordingStrategyStore();
        var host = requestedAction is null
            ? new RecordingStrategyHost()
            : new RecordingStrategyHost
            {
                Handler = (_, _) => Task.FromResult<StrategyResult?>(
                    StrategyRuntimeFixture.ValidResult(requestedAction))
            };
        var coordinator = new StrategyEventProcessingCoordinator(
            store,
            host,
            StrategyRuntimeFixture.Options(),
            new FixedRuntimeTimeProvider(StrategyRuntimeFixture.Now),
            new SequenceStrategyIdentifiers());

        StrategyEventProcessingResult result = await coordinator.ProcessAsync(
            StrategyRuntimeFixture.Context(),
            StrategyRuntimeFixture.Input().Reference,
            TestContext.Current.CancellationToken);

        Assert.Equal(StrategyEventProcessingOutcome.Committed, result.Outcome);
        return Assert.Single(store.CommitRequests).Evidence;
    }

    private static RequestedAction CreateActionForMutation(string mutation) =>
        mutation.StartsWith("update-", StringComparison.Ordinal)
            ? new UpdateProtectionAction(
                Guid.Parse("83000000-0000-0000-0000-000000000002"),
                "protect-1",
                "EURUSD",
                "protect_fixture",
                42,
                "position-1",
                1.08m,
                1.14m)
            : mutation.StartsWith("cancel-", StringComparison.Ordinal)
                ? new CancelPendingOrderAction(
                    Guid.Parse("83000000-0000-0000-0000-000000000003"),
                    "cancel-1",
                    "EURUSD",
                    "cancel_fixture",
                    42,
                    "order-1")
                : mutation.StartsWith("close-", StringComparison.Ordinal)
                    ? new ClosePositionAction(
                        Guid.Parse("83000000-0000-0000-0000-000000000004"),
                        "close-1",
                        "EURUSD",
                        "close_fixture",
                        42,
                        "position-1",
                        0.01m)
                    : CreatePlaceAction();

    private static RuntimeEnvelope<StrategyEvent> CreateEnvelopeWithBytes(int targetBytes)
    {
        RuntimeEnvelope<StrategyEvent> template = StrategyRuntimeFixture.Envelope() with
        {
            Payload = new InitializeEvent(StrategyRuntimeFixture.Now, "x")
        };
        int templateBytes = Encoding.UTF8.GetByteCount(CanonicalJson.Serialize(template));
        int reasonLength = checked(targetBytes - templateBytes + 1);
        if (reasonLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetBytes));
        }

        return template with
        {
            Payload = new InitializeEvent(
                StrategyRuntimeFixture.Now,
                new string('e', reasonLength))
        };
    }

    private static StrategySnapshot CreateSnapshotWithBytes(int targetBytes)
    {
        StrategySnapshot template = StrategySnapshot.Create(
            21,
            StrategyRuntimeFixture.Now,
            StrategyRuntimeFixture.Now,
            new StrategyAccountSnapshot(9, 10_000m, 10_050m, 9_000m, "X"));
        int templateBytes = Encoding.UTF8.GetByteCount(CanonicalJson.Serialize(template));
        int currencyLength = checked(targetBytes - templateBytes + 1);
        if (currencyLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetBytes));
        }

        return StrategySnapshot.Create(
            template.Sequence,
            template.AsOfUtc,
            template.DeterministicNowUtc,
            template.Account with { Currency = new string('C', currencyLength) });
    }

    private static string CreateCanonicalObjectWithBytes(int targetBytes)
    {
        var template = new JsonObject { ["padding"] = "x" };
        int templateBytes = Encoding.UTF8.GetByteCount(CanonicalJson.Serialize(template));
        int paddingLength = checked(targetBytes - templateBytes + 1);
        if (paddingLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetBytes));
        }

        string result = CanonicalJson.Serialize(new JsonObject
        {
            ["padding"] = new string('p', paddingLength)
        });
        Assert.Equal(targetBytes, Encoding.UTF8.GetByteCount(result));
        return result;
    }

    private static async Task<(
        StrategyEventProcessingResult Result,
        RecordingStrategyStore Store)> ProcessActionAsync(RequestedAction action)
    {
        var store = new RecordingStrategyStore();
        var host = new RecordingStrategyHost
        {
            Handler = (_, _) => Task.FromResult<StrategyResult?>(
                StrategyRuntimeFixture.ValidResult(action))
        };
        var coordinator = new StrategyEventProcessingCoordinator(
            store,
            host,
            new StrategyEventProcessingOptions
            {
                ResultBounds = StrategyResultBounds.Create(
                    StrategyDurableEvidenceLimits.MaximumStateBytes,
                    StrategyDurableEvidenceLimits.MaximumActionCount,
                    StrategyDurableEvidenceLimits.MaximumCombinedActionBytes,
                    TimeSpan.FromSeconds(1)),
                CommitAcknowledgementRecoveryAttempts = 1
            },
            new FixedRuntimeTimeProvider(StrategyRuntimeFixture.Now),
            new SequenceStrategyIdentifiers());

        StrategyEventProcessingResult result = await coordinator.ProcessAsync(
            StrategyRuntimeFixture.Context(),
            StrategyRuntimeFixture.Input().Reference,
            TestContext.Current.CancellationToken);
        return (result, store);
    }

    private static PlaceOrderAction CreatePlaceAction(
        string idempotencyKey = "entry-1",
        string symbol = "EURUSD",
        string reasonCode = "fixture_entry") => new(
        Guid.Parse("83000000-0000-0000-0000-000000000001"),
        idempotencyKey,
        symbol,
        reasonCode,
        42,
        RequestedExposureHint.Increase,
        RequestedOrderSide.Buy,
        RequestedOrderType.Market,
        0.01m,
        null,
        1.08m,
        1.14m,
        10);

    private sealed class RecordingIntakeStore(bool replayed) : IStrategyEventIntakeStore
    {
        public Func<StrategyEventIntakeReceipt, StrategyEventIntakeReceipt>? MutateReceipt
        { get; init; }

        public StrategyEventInputEvidence? Input { get; private set; }

        public int Calls { get; private set; }

        public Task<StrategyEventIntakeReceipt> PersistAsync(
            YO4X.Tenancy.TenantExecutionContext context,
            StrategyEventInputEvidence input,
            CancellationToken cancellationToken)
        {
            Calls++;
            Input = input;
            var receipt = new StrategyEventIntakeReceipt(
                input.Reference,
                input.EventJson,
                input.SnapshotJson,
                StrategyRuntimeFixture.Now,
                replayed);
            return Task.FromResult(MutateReceipt?.Invoke(receipt) ?? receipt);
        }
    }

    private sealed record UnsupportedInputEvent : StrategyEvent
    {
        public UnsupportedInputEvent(DateTimeOffset occurredAtUtc)
            : base(occurredAtUtc)
        {
        }

        public override StrategyEventKind Kind => StrategyEventKind.Initialize;
    }
}
