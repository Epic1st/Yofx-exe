using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace YO4X.Api;

public static class ProblemStatusCodeExtensions
{
    public static WebApplication UseYo4xProblemStatusCodes(this WebApplication app)
    {
        app.UseStatusCodePages(async statusContext =>
        {
            HttpContext context = statusContext.HttpContext;
            (string Code, string Title) problem = context.Response.StatusCode switch
            {
                StatusCodes.Status401Unauthorized =>
                    ("AUTHENTICATION_REQUIRED", "Authentication is required."),
                StatusCodes.Status403Forbidden =>
                    ("AUTHORIZATION_DENIED", "The authenticated actor is not authorized."),
                StatusCodes.Status404NotFound =>
                    ("RESOURCE_NOT_FOUND", "The resource was not found."),
                StatusCodes.Status405MethodNotAllowed =>
                    ("METHOD_NOT_ALLOWED", "The HTTP method is not allowed."),
                StatusCodes.Status413PayloadTooLarge =>
                    ("PAYLOAD_TOO_LARGE", "The request payload is too large."),
                StatusCodes.Status415UnsupportedMediaType =>
                    ("UNSUPPORTED_MEDIA_TYPE", "The request content type is not supported."),
                StatusCodes.Status429TooManyRequests =>
                    ("RATE_LIMITED", "Too many requests were received."),
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
