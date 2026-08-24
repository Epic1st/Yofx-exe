namespace YO4X.ControlPlane.Workers;

public static class WorkerFailStopHosting
{
    public static IServiceCollection AddControlPlaneWorkerFailStopPolicy(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Configure<HostOptions>(options =>
            options.BackgroundServiceExceptionBehavior =
                BackgroundServiceExceptionBehavior.StopHost);
        return services;
    }
}
