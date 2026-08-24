using YO4X.ControlPlane.Workers;
using YO4X.ControlPlane.Workers.Outbox;
using YO4X.ControlPlane.Workers.Operations;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);
OutboxDispatchOptions options = builder.Configuration
    .GetSection(OutboxDispatchOptions.SectionName)
    .Get<OutboxDispatchOptions>() ?? new OutboxDispatchOptions();
options.Validate();
ControlWorkOptions controlWorkOptions = builder.Configuration
    .GetSection(ControlWorkOptions.SectionName)
    .Get<ControlWorkOptions>() ?? new ControlWorkOptions();
controlWorkOptions.Validate();
WorkerReadinessOptions readinessOptions = builder.Configuration
    .GetSection(WorkerReadinessOptions.SectionName)
    .Get<WorkerReadinessOptions>() ?? new WorkerReadinessOptions();
readinessOptions.Validate();

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(controlWorkOptions);
builder.Services.AddSingleton(readinessOptions);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddControlPlaneWorkerFailStopPolicy();
builder.Services.AddSingleton<OutboxWorkerIdentity>(_ => OutboxWorkerIdentity.Create());
builder.Services.AddSingleton<WorkerReadiness>();
builder.Services.AddSingleton<OutboxWorkerReadiness>();
builder.Services.AddSingleton<ControlWorkReadiness>();
builder.Services.TryAddWorkerPostgres(builder.Configuration);
builder.Services.TryAddSingleton<IPostgresOutboxStore, UnavailablePostgresOutboxStore>();
builder.Services.TryAddSingleton<IOutboxDestination, UnavailableOutboxDestination>();
builder.Services.TryAddSingleton<IUserOperationWorkStore, UnavailableUserOperationWorkStore>();
builder.Services.TryAddSingleton<ICredentialGrantExpiryStore, UnavailableCredentialGrantExpiryStore>();
builder.Services.TryAddSingleton<IDeploymentProjectionStore, UnavailableDeploymentProjectionStore>();
builder.Services.AddSingleton<RetrySchedule>();
builder.Services.AddSingleton<OutboxDispatchCoordinator>();
builder.Services.AddHostedService<OutboxDispatcherBackgroundService>();
builder.Services.AddHostedService<ControlWorkBackgroundService>();

var app = builder.Build();

app.MapControlPlaneWorkerHealthEndpoints();

app.Run();

public partial class Program
{
}
