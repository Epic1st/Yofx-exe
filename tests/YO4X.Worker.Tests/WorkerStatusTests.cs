using YO4X.ControlPlane.Workers.Outbox;
using YO4X.Conversion.Worker;

namespace YO4X.Worker.Tests;

public sealed class WorkerStatusTests
{
    [Fact]
    public void ControlPlaneReadinessPublishesOnlyGenericDependencyFailure()
    {
        var readiness = new OutboxWorkerReadiness();
        readiness.MarkStarted();
        readiness.MarkNotReady(OutboxReadinessCondition.PostgresUnavailable);

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
}
