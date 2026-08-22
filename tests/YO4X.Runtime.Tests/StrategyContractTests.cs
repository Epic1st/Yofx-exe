using YO4X.Runtime.Contracts;
using YO4X.Strategy.Abstractions;

namespace YO4X.Runtime.Tests;

public sealed class StrategyContractTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);
    private static readonly string[] ExpectedQuoteSymbols = ["EURUSD", "USDJPY"];

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
        StrategyQuoteSnapshot earlierSymbol = new(1, "EURUSD", 1.1m, 1.11m, Now);

        StrategySnapshot snapshot = StrategySnapshot.Create(
            1,
            Now,
            Now,
            account,
            [laterSymbol, earlierSymbol]);

        Assert.Equal(ExpectedQuoteSymbols, snapshot.Quotes.Select(value => value.Symbol));
    }

    private static StrategyResult ValidResult(string stateJson) =>
        new(
            StrategyState.FromJson(1, stateJson),
            [Place(Guid.Parse("41000000-0000-0000-0000-000000000003"), "entry-1")]);

    private static PlaceOrderAction Place(Guid actionId, string idempotencyKey) =>
        new(
            actionId,
            idempotencyKey,
            "EURUSD",
            "signal_entry",
            1,
            RequestedExposureHint.Increase,
            RequestedOrderSide.Buy,
            RequestedOrderType.Market,
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
