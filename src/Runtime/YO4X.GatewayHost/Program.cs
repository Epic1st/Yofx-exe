using YO4X.GatewayHost;
using YO4X.Trading.Abstractions;
using YO4X.Trading.Mt5;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IMt5Gateway, Mt5ProofOnlyGateway>();
builder.Services.AddSingleton<GatewayHostRuntimeStatus>();

var app = builder.Build();

app.MapGet("/health/live", (GatewayHostRuntimeStatus status) =>
    Results.Json(status.Live));
app.MapGet("/health/startup", (GatewayHostRuntimeStatus status) =>
    Results.Json(status.Startup));
app.MapGet("/health/ready", (GatewayHostRuntimeStatus status) =>
    Results.Json(status.Ready, statusCode: StatusCodes.Status503ServiceUnavailable));

app.Run();

public partial class Program
{
}
