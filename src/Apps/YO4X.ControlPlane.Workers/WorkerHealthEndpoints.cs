namespace YO4X.ControlPlane.Workers;

public static class WorkerHealthEndpoints
{
    public static IEndpointRouteBuilder MapControlPlaneWorkerHealthEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/health/live", (WorkerReadiness readiness) =>
            ToHealthResult(readiness.GetLive()));
        endpoints.MapGet("/health/startup", (WorkerReadiness readiness) =>
            ToHealthResult(readiness.GetStartup()));
        endpoints.MapGet("/health/ready", (WorkerReadiness readiness) =>
            ToHealthResult(readiness.GetReady()));
        return endpoints;
    }

    private static IResult ToHealthResult(WorkerHealthSnapshot snapshot) =>
        Results.Json(
            snapshot,
            statusCode: snapshot.Healthy
                ? StatusCodes.Status200OK
                : StatusCodes.Status503ServiceUnavailable);
}
