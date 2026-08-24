using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using YO4X.ControlPlane.Workers;
using YO4X.ControlPlane.Workers.Outbox;
using YO4X.ControlPlane.Workers.Operations;

namespace YO4X.Worker.Tests;

public sealed class OutboxDispatchCoordinatorTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PostgreSqlUnavailablePreventsDestinationProbeAndClaim()
    {
        var store = new RecordingStore { Available = false };
        var destination = new RecordingDestination();
        CoordinatorFixture fixture = CreateFixture(store, destination);

        OutboxDispatchCycleResult result = await fixture.Coordinator
            .RunCycleAsync(CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(OutboxDispatchCycleOutcome.PostgresUnavailable, result.Outcome);
        Assert.Equal(0, store.ClaimCalls);
        Assert.Equal(0, destination.ProbeCalls);
        Assert.Equal(OutboxReadinessCondition.PostgresUnavailable, fixture.Readiness.Condition);
        Assert.Equal("required_dependency_unavailable", fixture.Aggregate.GetReady().Code);
    }

    [Fact]
    public async Task DestinationUnavailablePreventsClaim()
    {
        var store = new RecordingStore();
        var destination = new RecordingDestination { Available = false };
        CoordinatorFixture fixture = CreateFixture(store, destination);

        OutboxDispatchCycleResult result = await fixture.Coordinator
            .RunCycleAsync(CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(OutboxDispatchCycleOutcome.DestinationUnavailable, result.Outcome);
        Assert.Equal(0, store.ClaimCalls);
        Assert.Equal(1, destination.ProbeCalls);
        Assert.Equal(OutboxReadinessCondition.DestinationUnavailable, fixture.Readiness.Condition);
    }

    [Fact]
    public async Task EmptyHealthyCycleUsesConfiguredBoundAndBecomesReady()
    {
        var store = new RecordingStore();
        var destination = new RecordingDestination();
        OutboxDispatchOptions options = CreateOptions(batchSize: 7);
        CoordinatorFixture fixture = CreateFixture(store, destination, options);

        OutboxDispatchCycleResult result = await fixture.Coordinator
            .RunCycleAsync(CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(OutboxDispatchCycleOutcome.Completed, result.Outcome);
        Assert.Equal(7, store.LastClaimRequest?.MaximumMessages);
        Assert.Equal(options.ClaimLease, store.LastClaimRequest?.LeaseDuration);
        Assert.True(fixture.Aggregate.GetReady().Healthy);
    }

    [Fact]
    public async Task StaleTenantRotationKeepsOutboxNotReadyAfterDeliveryWorkCompletes()
    {
        var store = new RecordingStore { ScanProgressHealthy = false };
        var destination = new RecordingDestination();
        OutboxDispatchOptions options = CreateOptions(batchSize: 7);
        CoordinatorFixture fixture = CreateFixture(store, destination, options);

        OutboxDispatchCycleResult result = await fixture.Coordinator
            .RunCycleAsync(CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(OutboxDispatchCycleOutcome.ScanProgressLagging, result.Outcome);
        Assert.Equal(options.MaximumTenantScanRotationAge, store.LastMaximumRotationAge);
        Assert.Equal(OutboxReadinessCondition.ScanProgressLagging, fixture.Readiness.Condition);
        Assert.False(fixture.Aggregate.GetReady().Healthy);
        Assert.Equal("tenant_scan_rotation_stale", fixture.Aggregate.GetReady().Code);
    }

    [Theory]
    [InlineData(OutboxDeliveryOutcome.Accepted)]
    [InlineData(OutboxDeliveryOutcome.Duplicate)]
    public async Task AcceptedOrDuplicateDeliveryMarksMessagePublished(OutboxDeliveryOutcome outcome)
    {
        ClaimedOutboxItem item = CreateItem(attempt: 1);
        var store = new RecordingStore(item);
        var destination = new RecordingDestination
        {
            NextResult = outcome == OutboxDeliveryOutcome.Accepted
                ? OutboxDeliveryResult.Accepted
                : OutboxDeliveryResult.Duplicate
        };
        CoordinatorFixture fixture = CreateFixture(store, destination);

        OutboxDispatchCycleResult result = await fixture.Coordinator
            .RunCycleAsync(CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(1, result.Published);
        OutboxSettlement settlement = Assert.Single(store.Settlements);
        Assert.Equal(OutboxSettlementKind.Published, settlement.Kind);
        Assert.Equal(item.MessageId, settlement.MessageId);
        Assert.Null(settlement.RetryAtUtc);
    }

    [Fact]
    public async Task InvalidPayloadHashDeadLettersWithoutDelivery()
    {
        ClaimedOutboxItem item = CreateItem(
            attempt: 1,
            payloadSha256: new string('0', 64));
        var store = new RecordingStore(item);
        var destination = new RecordingDestination();
        CoordinatorFixture fixture = CreateFixture(store, destination);

        OutboxDispatchCycleResult result = await fixture.Coordinator
            .RunCycleAsync(CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(1, result.DeadLettered);
        Assert.Equal(0, destination.DeliveryCalls);
        OutboxSettlement settlement = Assert.Single(store.Settlements);
        Assert.Equal(OutboxSettlementKind.DeadLetter, settlement.Kind);
        Assert.Equal("invalid_or_oversized_payload", settlement.Code);
    }

    [Fact]
    public async Task RetryableFailureSchedulesBoundedRetry()
    {
        ClaimedOutboxItem item = CreateItem(attempt: 2);
        var store = new RecordingStore(item);
        var destination = new RecordingDestination
        {
            NextResult = OutboxDeliveryResult.Retryable("rate_limited")
        };
        OutboxDispatchOptions options = CreateOptions(maximumAttempts: 4);
        CoordinatorFixture fixture = CreateFixture(store, destination, options);

        OutboxDispatchCycleResult result = await fixture.Coordinator
            .RunCycleAsync(CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(1, result.ScheduledForRetry);
        Assert.Equal(0, result.DeadLettered);
        OutboxSettlement settlement = Assert.Single(store.Settlements);
        Assert.Equal(OutboxSettlementKind.Retry, settlement.Kind);
        Assert.Equal("rate_limited", settlement.Code);
        Assert.InRange(
            Assert.IsType<DateTimeOffset>(settlement.RetryAtUtc),
            FixedNow.Add(options.BaseRetryDelay + options.BaseRetryDelay),
            FixedNow.Add(options.MaximumRetryDelay));
    }

    [Fact]
    public async Task MaximumAttemptDeadLettersRetryableFailure()
    {
        ClaimedOutboxItem item = CreateItem(attempt: 3);
        var store = new RecordingStore(item);
        var destination = new RecordingDestination
        {
            NextResult = OutboxDeliveryResult.Retryable("temporary_failure")
        };
        CoordinatorFixture fixture = CreateFixture(
            store,
            destination,
            CreateOptions(maximumAttempts: 3));

        OutboxDispatchCycleResult result = await fixture.Coordinator
            .RunCycleAsync(CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(0, result.ScheduledForRetry);
        Assert.Equal(1, result.DeadLettered);
        Assert.Equal(OutboxSettlementKind.DeadLetter, Assert.Single(store.Settlements).Kind);
    }

    [Fact]
    public async Task PermanentFailureDeadLettersImmediately()
    {
        var store = new RecordingStore(CreateItem(attempt: 1));
        var destination = new RecordingDestination
        {
            NextResult = OutboxDeliveryResult.Permanent("unsupported_contract")
        };
        CoordinatorFixture fixture = CreateFixture(store, destination);

        OutboxDispatchCycleResult result = await fixture.Coordinator
            .RunCycleAsync(CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(1, result.DeadLettered);
        Assert.Equal(OutboxSettlementKind.DeadLetter, Assert.Single(store.Settlements).Kind);
    }

    [Fact]
    public async Task MidBatchDestinationOutageReleasesRemainingClaimsWithoutDelivery()
    {
        ClaimedOutboxItem first = CreateItem(attempt: 1);
        ClaimedOutboxItem second = CreateItem(attempt: 1, messageId: Guid.Parse("00000000-0000-0000-0000-000000000002"));
        ClaimedOutboxItem third = CreateItem(attempt: 1, messageId: Guid.Parse("00000000-0000-0000-0000-000000000003"));
        var store = new RecordingStore(first, second, third);
        var destination = new RecordingDestination
        {
            NextResult = OutboxDeliveryResult.DestinationUnavailable("destination_unavailable")
        };
        CoordinatorFixture fixture = CreateFixture(store, destination);

        OutboxDispatchCycleResult result = await fixture.Coordinator
            .RunCycleAsync(CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(OutboxDispatchCycleOutcome.DestinationUnavailable, result.Outcome);
        Assert.Equal(1, destination.DeliveryCalls);
        Assert.Equal(3, result.ScheduledForRetry);
        Assert.Equal(3, store.Settlements.Count);
        Assert.All(store.Settlements, settlement => Assert.Equal(OutboxSettlementKind.Retry, settlement.Kind));
        Assert.False(fixture.Aggregate.GetReady().Healthy);
    }

    [Fact]
    public async Task RejectedSettlementFailsReadinessAndReliesOnStableReplayIdentity()
    {
        var store = new RecordingStore(CreateItem(attempt: 1))
        {
            AcceptSettlements = false
        };
        var destination = new RecordingDestination();
        CoordinatorFixture fixture = CreateFixture(store, destination);

        OutboxDispatchCycleResult result = await fixture.Coordinator
            .RunCycleAsync(CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(OutboxDispatchCycleOutcome.StoreOperationFailed, result.Outcome);
        Assert.Equal(1, destination.DeliveryCalls);
        Assert.Equal(OutboxReadinessCondition.StoreOperationFailed, fixture.Readiness.Condition);
    }

    [Fact]
    public async Task OversizedStoreBatchIsRejectedBeforeAnyDelivery()
    {
        var store = new RecordingStore(
            CreateItem(attempt: 1),
            CreateItem(attempt: 1, messageId: Guid.Parse("00000000-0000-0000-0000-000000000002")));
        var destination = new RecordingDestination();
        CoordinatorFixture fixture = CreateFixture(store, destination, CreateOptions(batchSize: 1));

        OutboxDispatchCycleResult result = await fixture.Coordinator
            .RunCycleAsync(CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(OutboxDispatchCycleOutcome.StoreContractViolation, result.Outcome);
        Assert.Equal(0, destination.DeliveryCalls);
        Assert.Empty(store.Settlements);
        Assert.Equal(OutboxReadinessCondition.StoreContractViolation, fixture.Readiness.Condition);
    }

    [Fact]
    public async Task CallerCancellationInterruptsAvailabilityProbe()
    {
        var store = new RecordingStore
        {
            AvailabilityTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        CoordinatorFixture fixture = CreateFixture(store, new RecordingDestination());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await fixture.Coordinator.RunCycleAsync(cancellation.Token).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Equal(0, store.ClaimCalls);
    }

    [Fact]
    public async Task CancellationIgnoringClaimFailStopsHostedWorkstreamWithoutOverlap()
    {
        var claimTask = new TaskCompletionSource<IReadOnlyList<ClaimedOutboxItem>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new RecordingStore { ClaimTask = claimTask };
        OutboxDispatchOptions options = CreateOptions(
            dependencyTimeout: TimeSpan.FromMilliseconds(100),
            cancellationConfirmationTimeout: TimeSpan.FromMilliseconds(100));
        CoordinatorFixture fixture = CreateFixture(store, new RecordingDestination(), options);
        using var service = new OutboxDispatcherBackgroundService(
            fixture.Coordinator,
            options,
            fixture.Readiness,
            TimeProvider.System,
            NullLogger<OutboxDispatcherBackgroundService>.Instance);
        try
        {
            await service.StartAsync(TestContext.Current.CancellationToken);
            Assert.NotNull(service.ExecuteTask);
            Task executeTask = service.ExecuteTask!;

            await Assert.ThrowsAsync<WorkerOperationTerminationUnconfirmedException>(async () =>
                await executeTask.WaitAsync(
                    TimeSpan.FromSeconds(2),
                    TestContext.Current.CancellationToken));

            Assert.Equal(OutboxReadinessCondition.Stopped, fixture.Readiness.Condition);
            Assert.True(store.LastClaimCancellationToken.IsCancellationRequested);
            await Assert.ThrowsAsync<WorkerWorkstreamStoppedException>(() =>
                fixture.Coordinator.RunCycleAsync(TestContext.Current.CancellationToken));
            await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.Equal(1, store.ClaimCalls);
        }
        finally
        {
            claimTask.TrySetResult([]);
        }
    }

    private static CoordinatorFixture CreateFixture(
        RecordingStore store,
        RecordingDestination destination,
        OutboxDispatchOptions? options = null)
    {
        options ??= CreateOptions();
        var aggregate = new WorkerReadiness(
            TimeProvider.System,
            new WorkerReadinessOptions());
        var readiness = new OutboxWorkerReadiness(aggregate);
        var controlWork = new ControlWorkReadiness(aggregate);
        readiness.MarkStarted();
        controlWork.MarkStarted();
        controlWork.MarkReady();
        var coordinator = new OutboxDispatchCoordinator(
            store,
            destination,
            options,
            OutboxWorkerIdentity.Create("test-worker"),
            readiness,
            new RetrySchedule(options),
            new FixedTimeProvider(FixedNow));
        return new CoordinatorFixture(coordinator, readiness, aggregate);
    }

    private static OutboxDispatchOptions CreateOptions(
        int batchSize = 10,
        int maximumAttempts = 5,
        TimeSpan? dependencyTimeout = null,
        TimeSpan? cancellationConfirmationTimeout = null) =>
        new()
        {
            BatchSize = batchSize,
            PollInterval = TimeSpan.FromMilliseconds(10),
            ClaimLease = TimeSpan.FromSeconds(10),
            DependencyTimeout = dependencyTimeout ?? TimeSpan.FromSeconds(1),
            DeliveryTimeout = TimeSpan.FromSeconds(1),
            CancellationConfirmationTimeout =
                cancellationConfirmationTimeout ?? TimeSpan.FromSeconds(1),
            MaximumAttempts = maximumAttempts,
            BaseRetryDelay = TimeSpan.FromSeconds(1),
            MaximumRetryDelay = TimeSpan.FromSeconds(10),
            MaximumRetryJitter = TimeSpan.FromMilliseconds(100),
            MaximumPayloadBytes = 1_024
        };

    private static ClaimedOutboxItem CreateItem(
        int attempt,
        Guid? messageId = null,
        string? payloadSha256 = null)
    {
        const string payload = "{\"value\":1}";
        return new ClaimedOutboxItem(
            messageId ?? Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            "test.message",
            1,
            payload,
            payloadSha256 ?? Hash(payload),
            FixedNow.AddMinutes(-1),
            attempt);
    }

    private static string Hash(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

    private sealed record CoordinatorFixture(
        OutboxDispatchCoordinator Coordinator,
        OutboxWorkerReadiness Readiness,
        WorkerReadiness Aggregate);

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class RecordingStore : IPostgresOutboxStore
    {
        private readonly IReadOnlyList<ClaimedOutboxItem> _items;

        public RecordingStore(params ClaimedOutboxItem[] items)
        {
            _items = items;
        }

        public bool Available { get; init; } = true;

        public bool AcceptSettlements { get; init; } = true;

        public bool ScanProgressHealthy { get; init; } = true;

        public TaskCompletionSource<bool>? AvailabilityTask { get; init; }

        public TaskCompletionSource<IReadOnlyList<ClaimedOutboxItem>>? ClaimTask { get; init; }

        public int ClaimCalls { get; private set; }

        public OutboxClaimRequest? LastClaimRequest { get; private set; }

        public CancellationToken LastClaimCancellationToken { get; private set; }

        public List<OutboxSettlement> Settlements { get; } = [];

        public TimeSpan? LastMaximumRotationAge { get; private set; }

        public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
            AvailabilityTask is null
                ? ValueTask.FromResult(Available)
                : new ValueTask<bool>(AvailabilityTask.Task);

        public ValueTask<bool> IsScanProgressHealthyAsync(
            TimeSpan maximumRotationAge,
            CancellationToken cancellationToken)
        {
            LastMaximumRotationAge = maximumRotationAge;
            return ValueTask.FromResult(ScanProgressHealthy);
        }

        public ValueTask<IReadOnlyList<ClaimedOutboxItem>> ClaimAsync(
            OutboxClaimRequest request,
            CancellationToken cancellationToken)
        {
            ClaimCalls++;
            LastClaimRequest = request;
            LastClaimCancellationToken = cancellationToken;
            return ClaimTask is null
                ? ValueTask.FromResult(_items)
                : new ValueTask<IReadOnlyList<ClaimedOutboxItem>>(ClaimTask.Task);
        }

        public ValueTask<bool> SettleAsync(
            OutboxSettlement settlement,
            CancellationToken cancellationToken)
        {
            Settlements.Add(settlement);
            return ValueTask.FromResult(AcceptSettlements);
        }
    }

    private sealed class RecordingDestination : IOutboxDestination
    {
        public bool Available { get; init; } = true;

        public OutboxDeliveryResult NextResult { get; init; } = OutboxDeliveryResult.Accepted;

        public int ProbeCalls { get; private set; }

        public int DeliveryCalls { get; private set; }

        public List<OutboxDeliveryEnvelope> Messages { get; } = [];

        public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken)
        {
            ProbeCalls++;
            return ValueTask.FromResult(Available);
        }

        public ValueTask<OutboxDeliveryResult> DeliverAsync(
            OutboxDeliveryEnvelope message,
            CancellationToken cancellationToken)
        {
            DeliveryCalls++;
            Messages.Add(message);
            return ValueTask.FromResult(NextResult);
        }
    }
}
