using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using YO4X.BuildingBlocks;

namespace YO4X.Api;

public sealed class ApiFoundationOptions
{
    public string ErrorTypeBase { get; set; } = "https://errors.yo4x.invalid";
}

public sealed class ApiHealthOptions
{
    /// <summary>
    /// Maximum interval for which a completed process-local health result may
    /// be reused. This deliberately small window bounds stale readiness while
    /// preventing anonymous callers from amplifying dependency probes.
    /// </summary>
    public TimeSpan SnapshotLifetime { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Independent deadline for a shared dependency probe. Disconnecting one
    /// caller does not cancel work awaited by other callers.
    /// </summary>
    public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(5);
}

public static class ApiFoundation
{
    public static IServiceCollection AddYo4xApiFoundation(
        this IServiceCollection services,
        Action<ApiFoundationOptions>? configure = null)
    {
        var options = new ApiFoundationOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddProblemDetails(problemOptions =>
        {
            problemOptions.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Extensions["correlationId"] = CorrelationIdMiddleware.Get(context.HttpContext);
                context.ProblemDetails.Extensions.Remove("traceId");
            };
        });
        services.AddExceptionHandler<Yo4xExceptionHandler>();
        services.Configure<JsonOptions>(json =>
        {
            json.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            json.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            json.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper));
        });

        return services;
    }

    public static WebApplication UseYo4xApiFoundation(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseExceptionHandler();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.XFrameOptions = "DENY";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            await next(context).ConfigureAwait(false);
        });

        return app;
    }

    public static IEndpointRouteBuilder MapYo4xHealth(
        this IEndpointRouteBuilder endpoints,
        Func<CancellationToken, ValueTask<bool>> startup,
        Func<CancellationToken, ValueTask<bool>> ready,
        Action<ApiHealthOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(ready);

        var options = new ApiHealthOptions();
        configure?.Invoke(options);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            options.SnapshotLifetime,
            TimeSpan.Zero,
            nameof(options.SnapshotLifetime));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            options.ProbeTimeout,
            TimeSpan.Zero,
            nameof(options.ProbeTimeout));

        var startupProbe = new BoundedBooleanProbe(
            startup,
            options.SnapshotLifetime,
            options.ProbeTimeout);
        var readinessProbe = new BoundedBooleanProbe(
            ready,
            options.SnapshotLifetime,
            options.ProbeTimeout);

        endpoints.MapGet("/health/live", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
        endpoints.MapGet("/health/startup", async (CancellationToken cancellationToken) =>
            await startupProbe.GetAsync(cancellationToken).ConfigureAwait(false)
                ? Results.Ok(new { status = "healthy" })
                : Results.Json(new { status = "unhealthy" }, statusCode: StatusCodes.Status503ServiceUnavailable))
            .AllowAnonymous();
        endpoints.MapGet("/health/ready", async (CancellationToken cancellationToken) =>
            await readinessProbe.GetAsync(cancellationToken).ConfigureAwait(false)
                ? Results.Ok(new { status = "healthy" })
                : Results.Json(new { status = "unhealthy" }, statusCode: StatusCodes.Status503ServiceUnavailable))
            .AllowAnonymous();

        return endpoints;
    }
}

internal sealed partial class Yo4xExceptionHandler(ILogger<Yo4xExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        int status = exception switch
        {
            BackendCapabilityUnavailableException => StatusCodes.Status503ServiceUnavailable,
            ResourceNotFoundException => StatusCodes.Status404NotFound,
            ResourceConflictException => StatusCodes.Status409Conflict,
            AuthorizationDeniedException => StatusCodes.Status403Forbidden,
            UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
            DomainException => StatusCodes.Status422UnprocessableEntity,
            BadHttpRequestException badRequest => badRequest.StatusCode,
            _ => StatusCodes.Status500InternalServerError
        };

        string code = exception switch
        {
            BackendCapabilityUnavailableException => "CAPABILITY_UNAVAILABLE",
            ResourceNotFoundException => "RESOURCE_NOT_FOUND",
            ResourceConflictException conflict => conflict.Code,
            AuthorizationDeniedException denied => denied.Code,
            UnauthorizedAccessException => "AUTHENTICATION_REQUIRED",
            DomainException domain => domain.Code,
            BadHttpRequestException badRequest when badRequest.StatusCode == StatusCodes.Status413PayloadTooLarge => "PAYLOAD_TOO_LARGE",
            BadHttpRequestException badRequest when badRequest.StatusCode == StatusCodes.Status415UnsupportedMediaType => "UNSUPPORTED_MEDIA_TYPE",
            BadHttpRequestException => "INVALID_REQUEST",
            _ => "INTERNAL_ERROR"
        };

        string title = exception switch
        {
            BackendCapabilityUnavailableException unavailable => unavailable.Message,
            ResourceNotFoundException notFound => notFound.Message,
            ResourceConflictException conflict => conflict.Message,
            AuthorizationDeniedException denied => denied.Message,
            UnauthorizedAccessException => "Authentication is required.",
            DomainException domain => domain.Message,
            BadHttpRequestException => "The request is invalid.",
            _ => "The service could not complete the request."
        };

        // Every mapped exception above turns into a deliberate, self-describing problem code, and
        // logging those would be noise. An unmapped one becomes a bare 500 whose body says only
        // "the service could not complete the request" — so without this line the cause exists
        // nowhere: not in the response, not in the log, not in any artifact. That is exactly the
        // shape of failure that is hardest to diagnose, and it cost a real investigation to find.
        //
        // The exception is logged, never the request. Bodies on this API can carry a broker
        // password, and a logger that reached into the request would defeat the boundary that
        // keeps that password out of every store but the vault.
        if (status == StatusCodes.Status500InternalServerError)
        {
            UnhandledException(
                logger,
                httpContext.Request.Method,
                httpContext.Request.Path.Value ?? string.Empty,
                CorrelationIdMiddleware.Get(httpContext),
                exception);
        }

        IResult problem = ApiProblems.Create(httpContext, status, code, title);
        await problem.ExecuteAsync(httpContext).ConfigureAwait(false);
        return true;
    }

    [LoggerMessage(
        EventId = 5000,
        Level = LogLevel.Error,
        Message = "Unhandled exception for {Method} {Path}; correlation {CorrelationId}.")]
    private static partial void UnhandledException(
        ILogger logger,
        string method,
        string path,
        string correlationId,
        Exception exception);
}
