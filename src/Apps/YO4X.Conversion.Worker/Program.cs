using YO4X.Conversion.Worker;
using YO4X.StrategyGovernance;

if (Mql5QuarantineIntakeCommand.IsRequested(args))
{
    Environment.ExitCode = await Mql5QuarantineIntakeCommand.RunAsync(args);
    return;
}

if (ConversionInventoryCommand.IsRequested(args))
{
    Environment.ExitCode = await ConversionInventoryCommand.RunAsync(args);
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<ConversionWorkerStatus>();
builder.Services.AddSingleton<IMql5StaticInventoryAnalyzer, Mql5StaticInventoryAnalyzer>();
builder.Services.AddSingleton<Mql5CorpusInventoryJob>();

var app = builder.Build();

app.MapGet("/health/live", (ConversionWorkerStatus status) =>
    Results.Json(status.Live));
app.MapGet("/health/startup", (ConversionWorkerStatus status) =>
    Results.Json(status.Startup));
app.MapGet("/health/ready", (ConversionWorkerStatus status) =>
    Results.Json(status.Ready, statusCode: StatusCodes.Status503ServiceUnavailable));

app.Run();

public partial class Program
{
}
