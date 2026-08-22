using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace YO4X.Api;

public sealed class HttpsOnlyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.IsHttps
            && !context.Request.Path.StartsWithSegments("/health", StringComparison.Ordinal))
        {
            await ApiProblems.Create(
                context,
                StatusCodes.Status400BadRequest,
                "HTTPS_REQUIRED",
                "This API endpoint requires HTTPS.").ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }
}

public static class HttpsOnlyExtensions
{
    public static WebApplication UseYo4xHttpsOnly(this WebApplication app)
    {
        app.UseMiddleware<HttpsOnlyMiddleware>();
        return app;
    }
}
