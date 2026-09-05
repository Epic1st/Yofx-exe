using System.Net;
using YO4X.DevelopmentIdentity;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
DevelopmentIdentityStartupGuard.Validate(builder.Environment, builder.Configuration);
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = 64 * 1024);
builder.Services.AddDevelopmentIdentityProvider(
    builder.Configuration,
    builder.Environment);

WebApplication app = builder.Build();
app.Use(async (context, next) =>
{
    IPAddress? remote = context.Connection.RemoteIpAddress;
    if (remote is not null && !IPAddress.IsLoopback(remote))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    string allowedFormActions = string.Join(' ', LocalIdentityContract.AllowedFrontendOrigins);
    context.Response.Headers.ContentSecurityPolicy =
        $"default-src 'none'; style-src 'self'; form-action 'self' {allowedFormActions}; frame-ancestors 'none'; base-uri 'none'";
    await next(context).ConfigureAwait(false);
});
app.UseHttpsRedirection();
app.UseRouting();
app.UseCors("development-frontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<AuthenticatedAccountFormRecoveryMiddleware>();
app.MapControllers();
app.MapGet("/", () => Results.Redirect("/account/sign-in"));
app.Run();

public partial class Program;
