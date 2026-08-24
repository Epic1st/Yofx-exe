using Microsoft.Extensions.Logging.Abstractions;
using YO4X.ControlPlane.Workers;
using YO4X.ControlPlane.Workers.Operations;
using YO4X.ControlPlane.Workers.Outbox;

namespace YO4X.Worker.Tests;

public sealed class ControlWorkReadinessTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UnavailableDependencySkipsWorkAndFailsTheAggregateClosed()
    {
        var store = new RecordingControlStore { Available = false };
        ServiceFixture fixture = CreateFixture(store);

        ControlWorkCycleOutcome outcome = await fixture.Service
            .RunCycleOnceAsync(FixedNow, CancellationToken.None);

        Assert.Equal(ControlWorkCycleOutcome.RequiredDependencyUnavailable, outcome);
        Assert.Equal(3, store.ProbeCalls);
        Assert.Equal(0, store.RunCalls);
        Assert.Equal(
            ControlWorkReadinessCondition.RequiredDependencyUnavailable,
            fixture.ControlWork.Condition);
        Assert.False(fixture.Aggregate.GetReady().Healthy);
        Assert.Equal("required_dependency_unavailable", fixture.Aggregate.GetReady().Code);
    }

    [Fact]
    public async Task PartialFailureStaysLatchedUntilACompleteHealthyControlCycle()
    {
        var store = new RecordingControlStore
        {
            Result = new ControlWorkCycleResult(1, 1, 0, 1, true)
        };
        ServiceFixture fixture = CreateFixture(store);

        ControlWorkCycleOutcome degraded = await fixture.Service
            .RunCycleOnceAsync(FixedNow, CancellationToken.None);
        fixture.Outbox.MarkReady();

        Assert.Equal(ControlWorkCycleOutcome.PartialCycleFailure, degraded);
        Assert.False(fixture.Aggregate.GetReady().Healthy);
        Assert.Equal("control_work_degraded", fixture.Aggregate.GetReady().Code);

        store.Result = new ControlWorkCycleResult(1, 1, 1, 0, true);
        ControlWorkCycleOutcome recovered = await fixture.Service
            .RunCycleOnceAsync(FixedNow.AddSeconds(1), CancellationToken.None);

        Assert.Equal(ControlWorkCycleOutcome.Completed, recovered);
        Assert.Equal(ControlWorkReadinessCondition.Ready, fixture.ControlWork.Condition);
        Assert.True(fixture.Aggregate.GetReady().Healthy);
    }

    [Fact]
    public async Task IncompleteDurableRotationKeepsControlWorkNotReady()
    {
        var store = new RecordingControlStore
        {
            Result = new ControlWorkCycleResult(100, 0, 0, 0, false)
        };
        ServiceFixture fixture = CreateFixture(store);

        ControlWorkCycleOutcome outcome = await fixture.Service
            .RunCycleOnceAsync(FixedNow, CancellationToken.None);

        Assert.Equal(ControlWorkCycleOutcome.ScanProgressLagging, outcome);
        Assert.Equal(
            ControlWorkReadinessCondition.ScanProgressLagging,
            fixture.ControlWork.Condition);
        Assert.False(fixture.Aggregate.GetReady().Healthy);
        Assert.Equal("tenant_scan_rotation_stale", fixture.Aggregate.GetReady().Code);
    }

    [Fact]
    public async Task StaleEligibleOperationBacklogKeepsControlWorkNotReady()
    {
        var store = new RecordingControlStore
        {
            Result = new ControlWorkCycleResult(1, 32, 32, 0, true, false)
        };
        ServiceFixture fixture = CreateFixture(store);

        ControlWorkCycleOutcome outcome = await fixture.Service
            .RunCycleOnceAsync(FixedNow, CancellationToken.None);

        Assert.Equal(ControlWorkCycleOutcome.OperationBacklogLagging, outcome);
        Assert.Equal(
            ControlWorkReadinessCondition.OperationBacklogLagging,
            fixture.ControlWork.Condition);
        Assert.False(fixture.Aggregate.GetReady().Healthy);
        Assert.Equal("user_operation_backlog_stale", fixture.Aggregate.GetReady().Code);
    }

    [Fact]
    public async Task StoreExceptionDegradesControlWorkEvenWhenOtherStoresComplete()
    {
        var store = new RecordingControlStore
        {
            CycleException = new InvalidOperationException("test failure")
        };
        ServiceFixture fixture = CreateFixture(store);

        ControlWorkCycleOutcome outcome = await fixture.Service
            .RunCycleOnceAsync(FixedNow, CancellationToken.None);

        Assert.Equal(ControlWorkCycleOutcome.StoreOperationFailed, outcome);
        Assert.Equal(3, store.RunCalls);
        Assert.Equal(ControlWorkReadinessCondition.StoreOperationFailed, fixture.ControlWork.Condition);
        Assert.False(fixture.Aggregate.GetReady().Healthy);
    }

    [Fact]
    public async Task CancellationIgnoringStoreFailStopsHostedWorkstreamWithoutOverlap()
    {
        var runTask = new TaskCompletionSource<ControlWorkCycleResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new RecordingControlStore { RunTask = runTask };
        ServiceFixture fixture = CreateFixture(store);
        try
        {
            await fixture.Service.StartAsync(TestContext.Current.CancellationToken);
            Assert.NotNull(fixture.Service.ExecuteTask);
            Task executeTask = fixture.Service.ExecuteTask!;

            await Assert.ThrowsAsync<WorkerOperationTerminationUnconfirmedException>(async () =>
                await executeTask.WaitAsync(
                    TimeSpan.FromSeconds(2),
                    TestContext.Current.CancellationToken));

            Assert.Equal(ControlWorkReadinessCondition.Stopped, fixture.ControlWork.Condition);
            Assert.True(store.LastRunCancellationToken.IsCancellationRequested);
            await Assert.ThrowsAsync<WorkerWorkstreamStoppedException>(() =>
                fixture.Service.RunCycleOnceAsync(
                    FixedNow.AddSeconds(1),
                    TestContext.Current.CancellationToken));
            await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.Equal(1, store.RunCalls);
        }
        finally
        {
            runTask.TrySetResult(new ControlWorkCycleResult(0, 0, 0, 0, true));
            fixture.Service.Dispose();
        }
    }

    [Fact]
    public async Task NestedBoundaryPropagatesUnconfirmedInnerTermination()
    {
        var underlying = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await Assert.ThrowsAsync<WorkerOperationTerminationUnconfirmedException>(() =>
                WorkerOperationBoundary.ExecuteAsync(
                    outerToken => WorkerOperationBoundary.ExecuteAsync(
                        _ => underlying.Task,
                        TimeSpan.FromSeconds(5),
                        TimeSpan.FromMilliseconds(50),
                        TimeProvider.System,
                        outerToken),
                    TimeSpan.FromMilliseconds(50),
                    TimeSpan.FromMilliseconds(250),
                    TimeProvider.System,
                    TestContext.Current.CancellationToken));
        }
        finally
        {
            underlying.TrySetResult(true);
        }
    }

    private static ServiceFixture CreateFixture(RecordingControlStore store)
    {
        var aggregate = new WorkerReadiness(
            TimeProvider.System,
            new WorkerReadinessOptions());
        var outbox = new OutboxWorkerReadiness(aggregate);
        var controlWork = new ControlWorkReadiness(aggregate);
        outbox.MarkStarted();
        controlWork.MarkStarted();
        outbox.MarkReady();
        var options = new ControlWorkOptions
        {
            PollInterval = TimeSpan.FromMilliseconds(100),
            DependencyTimeout = TimeSpan.FromMilliseconds(100),
            OperationTimeout = TimeSpan.FromMilliseconds(100),
            CancellationConfirmationTimeout = TimeSpan.FromMilliseconds(100)
        };
        var service = new ControlWorkBackgroundService(
            store,
            store,
            store,
            options,
            controlWork,
            TimeProvider.System,
            NullLogger<ControlWorkBackgroundService>.Instance);
        return new ServiceFixture(service, aggregate, outbox, controlWork);
    }

    private sealed record ServiceFixture(
        ControlWorkBackgroundService Service,
        WorkerReadiness Aggregate,
        OutboxWorkerReadiness Outbox,
        ControlWorkReadiness ControlWork);

    private sealed class RecordingControlStore :
        IUserOperationWorkStore,
        ICredentialGrantExpiryStore,
        IDeploymentProjectionStore
    {
        public bool Available { get; init; } = true;

        public ControlWorkCycleResult Result { get; set; } = new(0, 0, 0, 0, true);

        public Exception? CycleException { get; init; }

        public TaskCompletionSource<ControlWorkCycleResult>? RunTask { get; init; }

        public int ProbeCalls { get; private set; }

        public int RunCalls { get; private set; }

        public CancellationToken LastRunCancellationToken { get; private set; }

        public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken)
        {
            ProbeCalls++;
            return ValueTask.FromResult(Available);
        }

        public Task<ControlWorkCycleResult> RunCycleAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            RunCalls++;
            LastRunCancellationToken = cancellationToken;
            if (RunTask is not null)
            {
                return RunTask.Task;
            }

            return CycleException is null
                ? Task.FromResult(Result)
                : Task.FromException<ControlWorkCycleResult>(CycleException);
        }
    }
}
