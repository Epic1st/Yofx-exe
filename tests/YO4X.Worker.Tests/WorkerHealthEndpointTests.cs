using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using YO4X.ControlPlane.Workers;
using YO4X.ControlPlane.Workers.Operations;
using YO4X.ControlPlane.Workers.Outbox;

namespace YO4X.Worker.Tests;

public sealed class WorkerHealthEndpointTests : IAsyncLifetime
{
    private WebApplication _application = null!;
    private HttpClient _client = null!;
    private ManualTimeProvider _timeProvider = null!;
    private WorkerReadiness _aggregate = null!;
    private OutboxWorkerReadiness _outbox = null!;
    private ControlWorkReadiness _controlWork = null!;

    public async ValueTask InitializeAsync()
    {
        _timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero));
        _aggregate = new WorkerReadiness(
            _timeProvider,
            new WorkerReadinessOptions { MaximumHealthyAge = TimeSpan.FromSeconds(1) });
        _outbox = new OutboxWorkerReadiness(_aggregate);
        _controlWork = new ControlWorkReadiness(_aggregate);

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(_aggregate);

        _application = builder.Build();
        _application.MapControlPlaneWorkerHealthEndpoints();
        await _application.StartAsync();
        _client = _application.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _application.DisposeAsync();
    }

    [Fact]
    public async Task ReadyEndpointTracksTheComposedStateWithoutCrossWorkstreamOverwrite()
    {
        HealthResponse initial = await GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, initial.StatusCode);
        Assert.Equal("startup_incomplete", initial.Snapshot.Code);

        _outbox.MarkStarted();
        _controlWork.MarkStarted();
        HealthResponse startup = await GetAsync("/health/startup");
        Assert.Equal(HttpStatusCode.OK, startup.StatusCode);
        Assert.Equal("startup_complete", startup.Snapshot.Code);

        _outbox.MarkReady();
        _controlWork.MarkReady();
        HealthResponse healthy = await GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
        Assert.Equal("required_workstreams_ready", healthy.Snapshot.Code);

        _controlWork.MarkNotReady(ControlWorkReadinessCondition.PartialCycleFailure);
        _outbox.MarkReady();
        HealthResponse degraded = await GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, degraded.StatusCode);
        Assert.Equal("control_work_degraded", degraded.Snapshot.Code);

        _controlWork.MarkNotReady(ControlWorkReadinessCondition.OperationBacklogLagging);
        HealthResponse backlogStale = await GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, backlogStale.StatusCode);
        Assert.Equal("user_operation_backlog_stale", backlogStale.Snapshot.Code);
    }

    [Fact]
    public async Task LiveEndpointDoesNotMisrepresentReadiness()
    {
        HealthResponse live = await GetAsync("/health/live");
        HealthResponse ready = await GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.True(live.Snapshot.Healthy);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.False(ready.Snapshot.Healthy);
    }

    [Fact]
    public async Task SynchronousHungStoreExpiresReadyLeaseWithoutWorkerCallback()
    {
        _outbox.MarkStarted();
        _controlWork.MarkStarted();
        _outbox.MarkReady();
        _controlWork.MarkReady();
        using var store = new SynchronouslyBlockingControlStore();
        using var service = new ControlWorkBackgroundService(
            store,
            store,
            store,
            new ControlWorkOptions
            {
                PollInterval = TimeSpan.FromMilliseconds(100),
                DependencyTimeout = TimeSpan.FromMilliseconds(100),
                OperationTimeout = TimeSpan.FromMilliseconds(100),
                CancellationConfirmationTimeout = TimeSpan.FromMilliseconds(100)
            },
            _controlWork,
            _timeProvider,
            NullLogger<ControlWorkBackgroundService>.Instance);
        Task<ControlWorkCycleOutcome> blockedCycle = Task.Run(() =>
            service.RunCycleOnceAsync(_timeProvider.GetUtcNow(), CancellationToken.None));
        await store.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        _timeProvider.Advance(TimeSpan.FromSeconds(2));
        HealthResponse stale = await GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, stale.StatusCode);
        Assert.Equal("required_workstream_heartbeat_stale", stale.Snapshot.Code);

        _outbox.MarkReady();
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            (await GetAsync("/health/ready")).StatusCode);

        store.Release();
        Assert.Equal(ControlWorkCycleOutcome.Completed, await blockedCycle);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/health/ready")).StatusCode);
    }

    private async Task<HealthResponse> GetAsync(string path)
    {
        using HttpResponseMessage response = await _client.GetAsync(path, CancellationToken.None);
        WorkerHealthSnapshot? snapshot = await response.Content
            .ReadFromJsonAsync<WorkerHealthSnapshot>(CancellationToken.None);
        return new HealthResponse(response.StatusCode, Assert.IsType<WorkerHealthSnapshot>(snapshot));
    }

    private sealed record HealthResponse(
        HttpStatusCode StatusCode,
        WorkerHealthSnapshot Snapshot);

    private sealed class ManualTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset utcNow = initialUtc;

        public override DateTimeOffset GetUtcNow() => utcNow;

        internal void Advance(TimeSpan duration) => utcNow = utcNow.Add(duration);
    }

    private sealed class SynchronouslyBlockingControlStore :
        IUserOperationWorkStore,
        ICredentialGrantExpiryStore,
        IDeploymentProjectionStore,
        IDisposable
    {
        private readonly ManualResetEventSlim release = new(initialState: false);
        private int runCalls;

        internal TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);

        public Task<ControlWorkCycleResult> RunCycleAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref runCalls) == 1)
            {
                Started.TrySetResult(true);
                release.Wait(CancellationToken.None);
            }

            return Task.FromResult(new ControlWorkCycleResult(0, 0, 0, 0, true));
        }

        internal void Release() => release.Set();

        public void Dispose()
        {
            release.Set();
            release.Dispose();
        }
    }
}
