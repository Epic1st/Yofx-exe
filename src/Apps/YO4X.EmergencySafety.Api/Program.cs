using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection.Extensions;
using YO4X.Admin.Application;
using YO4X.Api;
using YO4X.EmergencySafety.Api;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 64 * 1024;
    options.ConfigureHttpsDefaults(https =>
        https.ClientCertificateMode = ClientCertificateMode.AllowCertificate);
});

builder.Services.AddYo4xApiFoundation(options =>
    options.ErrorTypeBase = builder.Configuration["Api:ErrorTypeBase"]
        ?? "https://errors.yo4x.invalid");
builder.Services.AddYo4xEmergencyAuthentication(builder.Configuration);
builder.Services.AddAuthorizationBuilder().AddPolicy("emergency-restrictive", policy =>
{
    policy.AddAuthenticationSchemes(AuthenticationSchemes.Emergency);
    policy.RequireAuthenticatedUser();
    policy.RequireClaim("mfa", "hardware_key", "webauthn");
    policy.RequireClaim("authority", "restrict_only");
    policy.RequireClaim("sub");
    policy.RequireClaim("tenant_id");
    policy.RequireClaim("session_id");
    policy.RequireClaim("environment");
    policy.RequireClaim("auth_time");
});
builder.Services.TryAddScoped<IEmergencySafetyApplication, UnavailableAdminApplication>();

WebApplication app = builder.Build();
app.UseYo4xApiFoundation();
app.UseProblemStatusCodes();
app.UseEmergencyHttpsOnly();
app.UseAuthentication();
app.UseAuthorization();

app.MapYo4xHealth(
    _ => ValueTask.FromResult(true),
    cancellationToken => IsEmergencyReady(app.Services, cancellationToken));
app.MapEmergencyRoutes();

app.Run();

static ValueTask<bool> IsEmergencyReady(
    IServiceProvider services,
    CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    using IServiceScope scope = services.CreateScope();
    IEmergencySafetyApplication application =
        scope.ServiceProvider.GetRequiredService<IEmergencySafetyApplication>();
    return ValueTask.FromResult(application is not UnavailableAdminApplication);
}

public partial class Program;
