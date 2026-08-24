namespace YO4X.GatewayHost;

internal static class GatewayHostHealthEndpoints
{
    internal static IEndpointRouteBuilder MapGatewayHostHealthEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/health/live", (GatewayHostRuntimeStatus status) =>
            Results.Json(status.Live));
        endpoints.MapGet("/health/startup", (GatewayHostRuntimeStatus status) =>
        {
            GatewayHostStartupSnapshot snapshot = status.ReadStartup();
            int statusCode = snapshot.IsSuccessful
                ? StatusCodes.Status200OK
                : StatusCodes.Status503ServiceUnavailable;
            return Results.Json(snapshot.Health, statusCode: statusCode);
        });
        endpoints.MapGet("/health/ready", (GatewayHostRuntimeStatus status) =>
            Results.Json(
                status.Ready,
                statusCode: StatusCodes.Status503ServiceUnavailable));

        return endpoints;
    }
}
