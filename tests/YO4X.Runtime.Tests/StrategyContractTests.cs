using YO4X.Runtime.Contracts;
using YO4X.Strategy.Abstractions;

namespace YO4X.Runtime.Tests;

public sealed class StrategyContractTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);
    [Fact]
    public void EventContractExposesAllSevenVersionedTypedEvents()
    {
        StrategyEvent[] events =
        [
            new InitializeEvent(Now, "deployment_start"),
            new NewTickEvent(Now, "EURUSD", 1.10m, 1.11m, 1),
            new BarClosedEvent(
                Now,
                "EURUSD",
                TimeSpan.FromMinutes(1),
                Now.AddMinutes(-1),
                1.10m,
                1.12m,
                1.09m,
                1.11m,
                10,
                2),
            new TimerEvent(Now, "risk-check", Now),
            new ExecutionEvent(
                Now,
                Guid.Parse("40000000-0000-0000-0000-000000000001"),
                "broker-event-1",
                StrategyExecutionEventKind.Rejected,
                null,
                null,
                0,
                null,
                "broker_rejected"),
            new AccountChangedEvent(Now, 1, "equity_changed"),
            new StopEvent(Now, StrategyStopReason.Requested)
        ];

        Assert.Equal(7, events.Select(value => value.Kind).Distinct().Count());
        Assert.All(events, value => Assert.Equal(RuntimeContractVersions.StrategyEventV1, value.ContractVersion));
    }

    [Fact]
    public void EventContractsRejectUndefinedEnums()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExecutionEvent(
            Now,
            Guid.NewGuid(),
            "broker-event-1",
            (StrategyExecutionEventKind)99,
            null,
            null,
            0,
            null,
            "invalid-kind"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StopEvent(
            Now,
            (StrategyStopReason)99));
    }

    [Fact]
    public void StrategyHandleContractIsSynchronous()
    {
        System.Reflection.MethodInfo handle = typeof(IYo4xStrategy).GetMethod(nameof(IYo4xStrategy.Handle))
            ?? throw new InvalidOperationException("The synchronous Handle contract is missing.");

        Assert.Equal(typeof(StrategyResult), handle.ReturnType);
        Assert.False(typeof(Task).IsAssignableFrom(handle.ReturnType));
    }

    [Fact]
    public void EquivalentResultsProduceIdenticalCommittedHashes()
    {
        StrategyState current = StrategyState.Empty;
        StrategyResultBounds bounds = Bounds();

        StrategyResult first = ValidResult("{\"counter\":1,\"signal\":\"buy\"}");
        StrategyResult second = ValidResult("{\"signal\":\"buy\",\"counter\":1}");

        StrategyResultValidation firstValidation = StrategyResultValidator.Validate(
            current,
            first,
            bounds,
            TimeSpan.FromMilliseconds(1));
        StrategyResultValidation secondValidation = StrategyResultValidator.Validate(
            current,
            second,
            bounds,
            TimeSpan.FromMilliseconds(1));

        Assert.True(firstValidation.IsValid);
        Assert.True(secondValidation.IsValid);
        Assert.Equal(firstValidation.BoundedResult!.ResultHash, secondValidation.BoundedResult!.ResultHash);
    }

    [Theory]
    [InlineData("{\"value\":\"\\u0000\"}")]
    [InlineData("{\"\\u0000\":true}")]
    [InlineData("[\"safe\",{\"nested\":\"before\\u0000after\"}]")]
    public void StrategyStateRejectsPostgresUnsupportedNullCharacters(string json)
    {
        Assert.Throws<ArgumentException>(() => StrategyState.FromJson(1, json));
    }

    [Fact]
    public void StrategyStateAllowsEscapedTextThatOnlySpellsAUnicodeEscape()
    {
        StrategyState state = StrategyState.FromJson(
            1,
            "{\"value\":\"\\\\u0000\"}");

        Assert.Contains("\\\\u0000", state.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public void SameHandleInputProducesSameBoundedResultHash()
    {
        var strategy = new DeterministicTestStrategy();
        StrategyState state = StrategyState.Empty;
        var input = new NewTickEvent(Now, "EURUSD", 1.10m, 1.11m, 1);
        StrategySnapshot snapshot = Snapshot();

        StrategyResult first = strategy.Handle(input, snapshot, state);
        StrategyResult second = strategy.Handle(input, snapshot, state);
        StrategyResultValidation firstValidation = StrategyResultValidator.Validate(
            state,
            first,
            Bounds(),
            TimeSpan.Zero);
        StrategyResultValidation secondValidation = StrategyResultValidator.Validate(
            state,
            second,
            Bounds(),
            TimeSpan.Zero);

        Assert.True(firstValidation.IsValid);
        Assert.Equal(firstValidation.BoundedResult!.ResultHash, secondValidation.BoundedResult!.ResultHash);
    }

    [Fact]
    public void ResultWithWrongStateVersionIsRejected()
    {
        var result = new StrategyResult(StrategyState.FromJson(3, "{}"));

        StrategyResultValidation validation = StrategyResultValidator.Validate(
            StrategyState.Empty,
            result,
            Bounds(),
            TimeSpan.Zero);

        Assert.Equal(StrategyResultValidationCode.InvalidStateVersion, validation.Code);
    }

    [Fact]
    public void ResultBoundsRejectDuplicateIdempotencyKeys()
    {
        Guid firstId = Guid.Parse("41000000-0000-0000-0000-000000000001");
        Guid secondId = Guid.Parse("41000000-0000-0000-0000-000000000002");
        RequestedAction[] actions =
        [
            Place(firstId, "same-key"),
            Place(secondId, "same-key")
        ];
        var result = new StrategyResult(StrategyState.FromJson(1, "{}"), actions);

        StrategyResultValidation validation = StrategyResultValidator.Validate(
            StrategyState.Empty,
            result,
            Bounds(),
            TimeSpan.Zero);

        Assert.Equal(StrategyResultValidationCode.DuplicateIdempotencyKey, validation.Code);
    }

    [Fact]
    public void ResultValidatorRejectsNullActionDeterministically()
    {
        var result = new StrategyResult(
            StrategyState.FromJson(1, "{}"),
            [null!]);

        StrategyResultValidation validation = StrategyResultValidator.Validate(
            StrategyState.Empty,
            result,
            Bounds(),
            TimeSpan.Zero);

        Assert.Equal(StrategyResultValidationCode.StrategyFaulted, validation.Code);
        Assert.Equal("strategy_action_missing", validation.ReasonCode);
    }

    [Fact]
    public void ActionContractsRejectUndefinedEnums()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Place(
            Guid.NewGuid(),
            "undefined-exposure",
            exposureHint: (RequestedExposureHint)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => Place(
            Guid.NewGuid(),
            "undefined-side",
            side: (RequestedOrderSide)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => Place(
            Guid.NewGuid(),
            "undefined-order-type",
            orderType: (RequestedOrderType)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClosePositionAction(
            Guid.NewGuid(),
            "undefined-close-exposure",
            "EURUSD",
            "signal_exit",
            1,
            "position-1",
            0.01m,
            (RequestedExposureHint)99));
    }

    [Theory]
    [InlineData(RequestedExposureHint.Reduce)]
    [InlineData(RequestedExposureHint.Protect)]
    [InlineData(RequestedExposureHint.Cancel)]
    [InlineData(RequestedExposureHint.EmergencyClose)]
    public void PlaceOrderOnlyAcceptsTheIncreaseExposureHint(RequestedExposureHint exposureHint)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Place(
            Guid.NewGuid(),
            "invalid-place-exposure",
            exposureHint));
    }

    [Fact]
    public void CanonicalTextRejectsBoundaryWhitespaceControlsAndInvalidSurrogates()
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

        Assert.All(invalidValues, value => Assert.False(
            StrategyCanonicalText.IsCanonical(value)));
        Assert.True(StrategyCanonicalText.IsCanonical("replacement-\uFFFD-character"));
        Assert.True(StrategyCanonicalText.IsCanonical("supplementary-\U0001F4C8-scalar"));
    }

    [Fact]
    public void ActionContractsRejectNonCanonicalTextInEveryDurableTextField()
    {
        const string invalidScalar = "invalid\uD800text";

        Assert.Throws<ArgumentException>(() => Place(Guid.NewGuid(), invalidScalar));
        Assert.Throws<ArgumentException>(() => Place(
            Guid.NewGuid(),
            "invalid-symbol",
            symbol: invalidScalar));
        Assert.Throws<ArgumentException>(() => Place(
            Guid.NewGuid(),
            "invalid-reason",
            reasonCode: invalidScalar));
        Assert.Throws<ArgumentException>(() => new UpdateProtectionAction(
            Guid.NewGuid(),
            "invalid-position",
            "EURUSD",
            "protect",
            1,
            invalidScalar,
            1.08m,
            1.14m));
        Assert.Throws<ArgumentException>(() => new CancelPendingOrderAction(
            Guid.NewGuid(),
            "invalid-order",
            "EURUSD",
            "cancel",
            1,
            invalidScalar));
        Assert.Throws<ArgumentException>(() => new ClosePositionAction(
            Guid.NewGuid(),
            "invalid-close",
            "EURUSD",
            "close",
            1,
            invalidScalar,
            0.01m));
    }

    [Fact]
    public void ResultValidatorRejectsHostileNonCanonicalActionTextBeforeHashing()
    {
        (RequestedAction Action, Type Owner, string FieldName)[] hostileCases =
        [
            (Place(Guid.NewGuid(), "hostile-key"),
                typeof(RequestedAction), "<IdempotencyKey>k__BackingField"),
            (Place(Guid.NewGuid(), "hostile-symbol"),
                typeof(RequestedAction), "<Symbol>k__BackingField"),
            (Place(Guid.NewGuid(), "hostile-reason"),
                typeof(RequestedAction), "<ReasonCode>k__BackingField"),
            (new UpdateProtectionAction(
                    Guid.NewGuid(), "hostile-update", "EURUSD", "protect", 1,
                    "position-1", 1.08m, 1.14m),
                typeof(UpdateProtectionAction), "<PositionId>k__BackingField"),
            (new CancelPendingOrderAction(
                    Guid.NewGuid(), "hostile-cancel", "EURUSD", "cancel", 1, "order-1"),
                typeof(CancelPendingOrderAction), "<OrderId>k__BackingField"),
            (new ClosePositionAction(
                    Guid.NewGuid(), "hostile-close", "EURUSD", "close", 1,
                    "position-1", 0.01m),
                typeof(ClosePositionAction), "<PositionId>k__BackingField")
        ];

        foreach ((RequestedAction action, Type owner, string fieldName) in hostileCases)
        {
            System.Reflection.FieldInfo textField = owner.GetField(
                    fieldName,
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    $"Requested-action text field {fieldName} is missing.");
            textField.SetValue(action, "invalid\uD800text");
            var result = new StrategyResult(StrategyState.FromJson(1, "{}"), [action]);

            StrategyResultValidation validation = StrategyResultValidator.Validate(
                StrategyState.Empty,
                result,
                Bounds(),
                TimeSpan.Zero);

            Assert.Equal(StrategyResultValidationCode.StrategyFaulted, validation.Code);
            Assert.Equal("strategy_action_text_invalid", validation.ReasonCode);
            Assert.Null(validation.BoundedResult);
        }
    }

    [Fact]
    public void ResultBoundsRejectStateAndWallTimeOverruns()
    {
        StrategyResult stateHeavy = new(StrategyState.FromJson(1, "{\"value\":\"1234567890\"}"));
        StrategyResultBounds stateBounds = StrategyResultBounds.Create(
            5,
            1,
            1024,
            TimeSpan.FromMilliseconds(10));

        StrategyResultValidation stateValidation = StrategyResultValidator.Validate(
            StrategyState.Empty,
            stateHeavy,
            stateBounds,
            TimeSpan.Zero);
        StrategyResultValidation timeValidation = StrategyResultValidator.Validate(
            StrategyState.Empty,
            new StrategyResult(StrategyState.FromJson(1, "{}")),
            Bounds(),
            TimeSpan.FromSeconds(1));

        Assert.Equal(StrategyResultValidationCode.StateLimitExceeded, stateValidation.Code);
        Assert.Equal(StrategyResultValidationCode.WallTimeExceeded, timeValidation.Code);
    }

    [Fact]
    public void SnapshotCollectionsAreNormalizedIntoStableOrder()
    {
        StrategyAccountSnapshot account = new(1, 10_000m, 10_000m, 9_000m, "USD");
        StrategyQuoteSnapshot laterSymbol = new(2, "USDJPY", 145m, 145.1m, Now);
        StrategyQuoteSnapshot laterSequence = new(2, "EURUSD", 1.11m, 1.12m, Now);
        StrategyQuoteSnapshot earlierSequence = new(1, "EURUSD", 1.1m, 1.11m, Now);
        StrategyPositionSnapshot laterPosition = new(
            "position-b",
            "EURUSD",
            StrategyPositionSide.Buy,
            0.01m,
            1.10m,
            null,
            null,
            true);
        StrategyPositionSnapshot earlierPosition = laterPosition with
        {
            PositionId = "position-a"
        };
        StrategyPendingOrderSnapshot laterOrder = new(
            "order-b",
            "EURUSD",
            StrategyPositionSide.Buy,
            0.01m,
            1.10m,
            null,
            null,
            true);
        StrategyPendingOrderSnapshot earlierOrder = laterOrder with
        {
            OrderId = "order-a"
        };

        StrategySnapshot snapshot = StrategySnapshot.Create(
            1,
            Now,
            Now,
            account,
            [laterSymbol, laterSequence, earlierSequence],
            [laterPosition, earlierPosition],
            [laterOrder, earlierOrder]);

        Assert.Equal(
            [("EURUSD", 1L), ("EURUSD", 2L), ("USDJPY", 2L)],
            snapshot.Quotes.Select(value => (value.Symbol, value.Sequence)));
        Assert.Equal(
            ["position-a", "position-b"],
            snapshot.Positions.Select(value => value.PositionId));
        Assert.Equal(
            ["order-a", "order-b"],
            snapshot.PendingOrders.Select(value => value.OrderId));
    }

    [Fact]
    public void SnapshotCreationRejectsNullCollectionElements()
    {
        Assert.Throws<ArgumentException>(() => StrategySnapshot.Create(
            1,
            Now,
            Now,
            new StrategyAccountSnapshot(1, 10_000m, 10_000m, 9_000m, "USD"),
            quotes: [null!]));
    }

    [Fact]
    public void SnapshotCreationStopsInfiniteCollectionsAtTheHardElementLimit()
    {
        int yielded = 0;
        StrategyQuoteSnapshot quote = new(1, "EURUSD", 1.10m, 1.11m, Now);

        Assert.Throws<ArgumentException>(() => StrategySnapshot.Create(
            1,
            Now,
            Now,
            new StrategyAccountSnapshot(1, 10_000m, 10_000m, 9_000m, "USD"),
            quotes: Infinite(quote, () => yielded++)));
        Assert.Equal(StrategySnapshot.MaximumQuoteCount + 1, yielded);
    }

    [Fact]
    public void StrategyResultStopsInfiniteActionsAtTheDurableLimit()
    {
        int yielded = 0;
        PlaceOrderAction action = Place(
            Guid.Parse("41000000-0000-0000-0000-000000000099"),
            "bounded-action");

        Assert.Throws<ArgumentException>(() => new StrategyResult(
            StrategyState.FromJson(1, "{}"),
            Infinite<RequestedAction>(action, () => yielded++)));
        Assert.Equal(StrategyResult.MaximumRequestedActionCount + 1, yielded);
    }

    private static StrategyResult ValidResult(string stateJson) =>
        new(
            StrategyState.FromJson(1, stateJson),
            [Place(Guid.Parse("41000000-0000-0000-0000-000000000003"), "entry-1")]);

    private static PlaceOrderAction Place(
        Guid actionId,
        string idempotencyKey,
        RequestedExposureHint exposureHint = RequestedExposureHint.Increase,
        RequestedOrderSide side = RequestedOrderSide.Buy,
        RequestedOrderType orderType = RequestedOrderType.Market,
        string symbol = "EURUSD",
        string reasonCode = "signal_entry") =>
        new(
            actionId,
            idempotencyKey,
            symbol,
            reasonCode,
            1,
            exposureHint,
            side,
            orderType,
            0.01m,
            null,
            1.08m,
            1.14m,
            10);

    private static StrategyResultBounds Bounds() =>
        StrategyResultBounds.Create(
            4096,
            8,
            8192,
            TimeSpan.FromMilliseconds(100));

    private static StrategySnapshot Snapshot() =>
        StrategySnapshot.Create(
            1,
            Now,
            Now,
            new StrategyAccountSnapshot(1, 10_000m, 10_000m, 9_000m, "USD"),
            [new StrategyQuoteSnapshot(1, "EURUSD", 1.10m, 1.11m, Now)]);

    private static IEnumerable<T> Infinite<T>(T value, Action onYield)
    {
        while (true)
        {
            onYield();
            yield return value;
        }
    }

    private sealed class DeterministicTestStrategy : IYo4xStrategy
    {
        public StrategyResult Handle(
            StrategyEvent input,
            StrategySnapshot snapshot,
            StrategyState currentState)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(currentState);
            return new StrategyResult(
                StrategyState.FromJson(checked(currentState.Version + 1), "{\"signal\":\"buy\"}"),
                [Place(Guid.Parse("41000000-0000-0000-0000-000000000004"), "deterministic-entry")]);
        }
    }
}
