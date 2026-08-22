using YO4X.Supervisor;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<SupervisorRuntimeStatus>();

var app = builder.Build();

app.MapGet("/health/live", (SupervisorRuntimeStatus status) =>
    Results.Json(status.Live));
app.MapGet("/health/startup", (SupervisorRuntimeStatus status) =>
    Results.Json(status.Startup));
app.MapGet("/health/ready", (SupervisorRuntimeStatus status) =>
    Results.Json(status.Ready, statusCode: StatusCodes.Status503ServiceUnavailable));

app.Run();

public partial class Program
{
}
