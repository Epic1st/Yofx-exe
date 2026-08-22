using YO4X.StrategyHost;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<StrategyHostRuntimeStatus>();

var app = builder.Build();

app.MapGet("/health/live", (StrategyHostRuntimeStatus status) =>
    Results.Json(status.Live));
app.MapGet("/health/startup", (StrategyHostRuntimeStatus status) =>
    Results.Json(status.Startup));
app.MapGet("/health/ready", (StrategyHostRuntimeStatus status) =>
    Results.Json(status.Ready, statusCode: StatusCodes.Status503ServiceUnavailable));

app.Run();

public partial class Program
{
}
