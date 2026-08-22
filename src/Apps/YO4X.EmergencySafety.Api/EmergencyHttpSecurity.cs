using YO4X.Api;

namespace YO4X.EmergencySafety.Api;

internal static class ProblemStatusCodeExtensions
{
    public static WebApplication UseEmergencyHttpsOnly(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/emergency/v1")
                && !context.Request.IsHttps)
            {
                await ApiProblems.Create(
                    context,
                    StatusCodes.Status400BadRequest,
                    "HTTPS_REQUIRED",
                    "Emergency safety application routes require HTTPS.").ExecuteAsync(context)
                    .ConfigureAwait(false);
                return;
            }

            await next(context).ConfigureAwait(false);
        });

        return app;
    }

    public static WebApplication UseProblemStatusCodes(this WebApplication app)
    {
        app.UseStatusCodePages(async statusContext =>
        {
            HttpContext context = statusContext.HttpContext;
            (string Code, string Title) problem = context.Response.StatusCode switch
            {
                StatusCodes.Status401Unauthorized =>
                    ("AUTHENTICATION_REQUIRED", "Emergency authentication is required."),
                StatusCodes.Status403Forbidden =>
                    ("RESTRICTIVE_AUTHORITY_REQUIRED", "Restrictive emergency authority is required."),
                StatusCodes.Status404NotFound =>
                    ("RESOURCE_NOT_FOUND", "The resource was not found."),
                StatusCodes.Status405MethodNotAllowed =>
                    ("METHOD_NOT_ALLOWED", "The HTTP method is not allowed."),
                StatusCodes.Status415UnsupportedMediaType =>
                    ("UNSUPPORTED_MEDIA_TYPE", "The request content type is not supported."),
                _ => ("HTTP_ERROR", "The request could not be completed.")
            };

            await ApiProblems.Create(
                context,
                context.Response.StatusCode,
                problem.Code,
                problem.Title).ExecuteAsync(context).ConfigureAwait(false);
        });

        return app;
    }
}
