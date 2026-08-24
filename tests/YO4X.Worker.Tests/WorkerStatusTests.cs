using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using YO4X.ControlPlane.Workers;
using YO4X.ControlPlane.Workers.Outbox;
using YO4X.ControlPlane.Workers.Operations;
using YO4X.Conversion.Worker;

namespace YO4X.Worker.Tests;

public sealed class WorkerStatusTests
{
    [Fact]
    public void ControlPlaneReadinessPublishesOnlyGenericDependencyFailure()
    {
        var readiness = new YO4X.ControlPlane.Workers.WorkerReadiness(
            TimeProvider.System,
            new YO4X.ControlPlane.Workers.WorkerReadinessOptions());
        var outbox = new OutboxWorkerReadiness(readiness);
        var controlWork = new ControlWorkReadiness(readiness);
        outbox.MarkStarted();
        controlWork.MarkStarted();
        controlWork.MarkReady();
        outbox.MarkNotReady(OutboxReadinessCondition.PostgresUnavailable);

        YO4X.ControlPlane.Workers.WorkerHealthSnapshot health = readiness.GetReady();

        Assert.False(health.Healthy);
        Assert.Equal("required_dependency_unavailable", health.Code);
        Assert.DoesNotContain("postgres", health.Code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("destination", health.Code, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConversionWorkerIsExplicitlyDisabledAndNotReady()
    {
        var status = new ConversionWorkerStatus();

        Assert.False(status.Enabled);
        Assert.True(status.Live.Healthy);
        Assert.True(status.Startup.Healthy);
        Assert.False(status.Ready.Healthy);
        Assert.Equal("conversion_worker_disabled", status.Startup.Code);
        Assert.Equal("conversion_prerequisites_missing", status.Ready.Code);
    }

    [Fact]
    public void ControlPlaneWorkersExplicitlyStopTheHostOnBackgroundFailure()
    {
        var services = new ServiceCollection();
        services.AddControlPlaneWorkerFailStopPolicy();
        using ServiceProvider provider = services.BuildServiceProvider();

        HostOptions options = provider.GetRequiredService<IOptions<HostOptions>>().Value;

        Assert.Equal(
            BackgroundServiceExceptionBehavior.StopHost,
            options.BackgroundServiceExceptionBehavior);
    }
}
