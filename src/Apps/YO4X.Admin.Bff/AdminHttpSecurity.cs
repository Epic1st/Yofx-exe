using Microsoft.AspNetCore.Http;
using YO4X.Admin.Postgres;
using YO4X.Api;

namespace YO4X.Admin.Bff;

internal sealed class AdminOriginPolicy
{
    private readonly HashSet<string> allowedOrigins;

    public AdminOriginPolicy(IEnumerable<string> origins)
    {
        ArgumentNullException.ThrowIfNull(origins);
        allowedOrigins = origins
            .Select(NormalizeConfiguredOrigin)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (allowedOrigins.Count == 0)
        {
            throw new InvalidOperationException(
                "AdminSecurity:AllowedOrigins must contain at least one exact admin origin.");
        }
    }

    public bool Allows(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Origin", out var values) || values.Count != 1)
        {
            return false;
        }

        return TryNormalizeOrigin(values[0], out string normalized)
            && allowedOrigins.Contains(normalized);
    }

    private static string NormalizeConfiguredOrigin(string value)
    {
        if (!TryNormalizeOrigin(value, out string normalized))
        {
            throw new InvalidOperationException(
                $"AdminSecurity:AllowedOrigins contains an invalid origin: '{value}'.");
        }

        return normalized;
    }

    private static bool TryNormalizeOrigin(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath != "/")
        {
            return false;
        }

        normalized = uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.Unescaped)
            .TrimEnd('/');
        return true;
    }
}

internal sealed class AdminOriginFilter(AdminOriginPolicy policy) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        if (!policy.Allows(context.HttpContext.Request))
        {
            return ApiProblems.Create(
                context.HttpContext,
                StatusCodes.Status403Forbidden,
                "ADMIN_ORIGIN_FORBIDDEN",
                "The mutation did not originate from an allowlisted admin origin.");
        }

        return await next(context).ConfigureAwait(false);
    }
}

internal static class ProblemStatusCodeExtensions
{
    public static WebApplication UseAdminApplicationProblems(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context).ConfigureAwait(false);
            }
            catch (AdminAuthorizationDeniedException exception)
            {
                await ApiProblems.Create(
                    context,
                    StatusCodes.Status403Forbidden,
                    exception.Code,
                    exception.Message).ExecuteAsync(context).ConfigureAwait(false);
            }
            catch (AdminResourceNotFoundException)
            {
                await ApiProblems.Create(
                    context,
                    StatusCodes.Status404NotFound,
                    "RESOURCE_NOT_FOUND",
                    "The resource was not found.").ExecuteAsync(context).ConfigureAwait(false);
            }
        });

        return app;
    }

    public static WebApplication UseAdminHttpsOnly(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/admin/v1")
                && !context.Request.IsHttps)
            {
                await ApiProblems.Create(
                    context,
                    StatusCodes.Status400BadRequest,
                    "HTTPS_REQUIRED",
                    "Admin application routes require HTTPS.").ExecuteAsync(context)
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
                    ("AUTHENTICATION_REQUIRED", "Authentication is required."),
                StatusCodes.Status403Forbidden =>
                    ("AUTHORIZATION_DENIED", "The authenticated actor is not authorized."),
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
