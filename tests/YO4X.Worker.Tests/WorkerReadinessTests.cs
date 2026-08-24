using YO4X.ControlPlane.Workers;
using YO4X.ControlPlane.Workers.Operations;
using YO4X.ControlPlane.Workers.Outbox;

namespace YO4X.Worker.Tests;

public sealed class WorkerReadinessTests
{
    [Fact]
    public void AggregateRequiresEveryWorkstreamAndRecoversOnlyTheFailedWorkstream()
    {
        ReadinessFixture fixture = CreateFixture();

        Assert.True(fixture.Aggregate.GetLive().Healthy);
        Assert.False(fixture.Aggregate.GetStartup().Healthy);
        Assert.False(fixture.Aggregate.GetReady().Healthy);

        fixture.Outbox.MarkStarted();
        fixture.Outbox.MarkReady();

        Assert.False(fixture.Aggregate.GetStartup().Healthy);
        Assert.Equal("startup_incomplete", fixture.Aggregate.GetReady().Code);

        fixture.ControlWork.MarkStarted();

        Assert.True(fixture.Aggregate.GetStartup().Healthy);
        Assert.False(fixture.Aggregate.GetReady().Healthy);
        Assert.Equal("required_dependencies_unverified", fixture.Aggregate.GetReady().Code);

        fixture.ControlWork.MarkReady();
        Assert.True(fixture.Aggregate.GetReady().Healthy);

        fixture.ControlWork.MarkNotReady(ControlWorkReadinessCondition.PartialCycleFailure);
        Assert.False(fixture.Aggregate.GetReady().Healthy);
        Assert.Equal("control_work_degraded", fixture.Aggregate.GetReady().Code);

        fixture.Outbox.MarkReady();
        Assert.False(fixture.Aggregate.GetReady().Healthy);

        fixture.ControlWork.MarkReady();
        Assert.True(fixture.Aggregate.GetReady().Healthy);
    }

    [Fact]
    public void StoppedStateIsTerminalAndFailsStartupAndReadinessClosed()
    {
        ReadinessFixture fixture = CreateHealthyFixture();

        fixture.ControlWork.MarkStopped();
        fixture.ControlWork.MarkReady();
        fixture.ControlWork.MarkNotReady(ControlWorkReadinessCondition.StoreOperationFailed);

        Assert.Equal(ControlWorkReadinessCondition.Stopped, fixture.ControlWork.Condition);
        Assert.False(fixture.Aggregate.GetStartup().Healthy);
        Assert.Equal("worker_stopped", fixture.Aggregate.GetStartup().Code);
        Assert.False(fixture.Aggregate.GetReady().Healthy);
        Assert.Equal("worker_stopped", fixture.Aggregate.GetReady().Code);
    }

    [Fact]
    public void ConcurrentOutboxSuccessCannotClearControlWorkFailure()
    {
        ReadinessFixture fixture = CreateHealthyFixture();
        fixture.ControlWork.MarkNotReady(ControlWorkReadinessCondition.StoreOperationFailed);
        int unexpectedHealthySnapshots = 0;

        Parallel.For(0, 10_000, _ =>
        {
            fixture.Outbox.MarkReady();
            if (fixture.Aggregate.GetReady().Healthy)
            {
                Interlocked.Increment(ref unexpectedHealthySnapshots);
            }
        });

        Assert.Equal(0, unexpectedHealthySnapshots);
        Assert.Equal(ControlWorkReadinessCondition.StoreOperationFailed, fixture.ControlWork.Condition);
        Assert.Equal("control_work_degraded", fixture.Aggregate.GetReady().Code);
    }

    [Fact]
    public void ConcurrentStopCannotBeOverwrittenByRecovery()
    {
        ReadinessFixture fixture = CreateHealthyFixture();

        Parallel.Invoke(
            fixture.ControlWork.MarkStopped,
            () => Parallel.For(0, 10_000, _ => fixture.ControlWork.MarkReady()));

        Assert.Equal(ControlWorkReadinessCondition.Stopped, fixture.ControlWork.Condition);
        Assert.False(fixture.Aggregate.GetReady().Healthy);
        Assert.Equal("worker_stopped", fixture.Aggregate.GetReady().Code);
    }

    [Fact]
    public void HealthyLeaseExpiresWithoutAWorkerCallbackAndRequiresBothRenewals()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero));
        var aggregate = new WorkerReadiness(
            timeProvider,
            new WorkerReadinessOptions { MaximumHealthyAge = TimeSpan.FromSeconds(1) });
        var outbox = new OutboxWorkerReadiness(aggregate);
        var controlWork = new ControlWorkReadiness(aggregate);
        outbox.MarkStarted();
        controlWork.MarkStarted();
        outbox.MarkReady();
        controlWork.MarkReady();

        timeProvider.Advance(TimeSpan.FromSeconds(2));

        Assert.False(aggregate.GetReady().Healthy);
        Assert.Equal("required_workstream_heartbeat_stale", aggregate.GetReady().Code);

        outbox.MarkReady();
        Assert.False(aggregate.GetReady().Healthy);

        controlWork.MarkReady();
        Assert.True(aggregate.GetReady().Healthy);
    }

    private static ReadinessFixture CreateHealthyFixture()
    {
        ReadinessFixture fixture = CreateFixture();
        fixture.Outbox.MarkStarted();
        fixture.ControlWork.MarkStarted();
        fixture.Outbox.MarkReady();
        fixture.ControlWork.MarkReady();
        return fixture;
    }

    private static ReadinessFixture CreateFixture()
    {
        var aggregate = new WorkerReadiness(
            TimeProvider.System,
            new WorkerReadinessOptions());
        return new ReadinessFixture(
            aggregate,
            new OutboxWorkerReadiness(aggregate),
            new ControlWorkReadiness(aggregate));
    }

    private sealed record ReadinessFixture(
        WorkerReadiness Aggregate,
        OutboxWorkerReadiness Outbox,
        ControlWorkReadiness ControlWork);

    private sealed class ManualTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset utcNow = initialUtc;

        public override DateTimeOffset GetUtcNow() => utcNow;

        internal void Advance(TimeSpan duration) => utcNow = utcNow.Add(duration);
    }
}
