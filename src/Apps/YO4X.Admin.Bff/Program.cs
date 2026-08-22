using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.DependencyInjection.Extensions;
using YO4X.Admin.Application;
using YO4X.Admin.Bff;
using YO4X.Admin.Postgres;
using YO4X.Api;
using YO4X.Persistence.Postgres;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 256 * 1024);

builder.Services.AddYo4xApiFoundation(options =>
    options.ErrorTypeBase = builder.Configuration["Api:ErrorTypeBase"]
        ?? "https://errors.yo4x.invalid");
builder.Services.AddYo4xAdminAuthentication();
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "__Host-yo4x-admin-csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.HeaderName = "X-CSRF-Token";
    options.SuppressXFrameOptionsHeader = true;
});

string[] configuredOrigins = builder.Configuration
    .GetSection("AdminSecurity:AllowedOrigins")
    .Get<string[]>() ?? [];
var originPolicy = new AdminOriginPolicy(configuredOrigins);
builder.Services.AddSingleton(originPolicy);

string? adminPostgresConnection = builder.Configuration.GetConnectionString("AdminPostgres");
if (string.IsNullOrWhiteSpace(adminPostgresConnection))
{
    builder.Services.TryAddScoped<IAdminApplication, UnavailableAdminApplication>();
}
else
{
    AdminPostgresOptions postgresOptions = builder.Configuration
        .GetSection("AdminPostgres")
        .Get<AdminPostgresOptions>() ?? new AdminPostgresOptions();
    postgresOptions.Validate();
    builder.Services.AddSingleton(postgresOptions);
    builder.Services.AddSingleton(new PostgresDatabase(adminPostgresConnection));
    builder.Services.AddScoped<AdminPostgresApplication>();
    builder.Services.AddScoped<IAdminApplication>(services =>
        services.GetRequiredService<AdminPostgresApplication>());
}

WebApplication app = builder.Build();
app.UseYo4xApiFoundation();
app.UseAdminApplicationProblems();
app.UseProblemStatusCodes();
app.UseAdminHttpsOnly();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapYo4xHealth(
    _ => ValueTask.FromResult(true),
    cancellationToken => IsAdminReadyAsync(app.Services, cancellationToken));
app.MapAdminRoutes(originPolicy);

app.Run();

static async ValueTask<bool> IsAdminReadyAsync(
    IServiceProvider services,
    CancellationToken cancellationToken)
{
    await using AsyncServiceScope scope = services.CreateAsyncScope();
    IAdminApplication application = scope.ServiceProvider.GetRequiredService<IAdminApplication>();
    return application switch
    {
        IAdminPostgresReadiness postgres => await postgres.IsReadyAsync(cancellationToken)
            .ConfigureAwait(false),
        UnavailableAdminApplication => false,
        _ => false
    };
}

public partial class Program;
