using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace YO4X.Api;

public sealed record ApiValidationError(string Path, string Code, string Message);

public static class ApiProblems
{
    public static IResult Create(
        HttpContext context,
        int status,
        string code,
        string title,
        IReadOnlyList<ApiValidationError>? errors = null)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["code"] = code,
            ["correlationId"] = CorrelationIdMiddleware.Get(context)
        };

        if (errors is { Count: > 0 })
        {
            extensions["errors"] = errors;
        }

        string errorBase = context.RequestServices
            .GetRequiredService<ApiFoundationOptions>()
            .ErrorTypeBase.TrimEnd('/');

        return Results.Problem(
            statusCode: status,
            title: title,
            type: $"{errorBase}/{ToKebabCase(code)}",
            extensions: extensions);
    }

    private static string ToKebabCase(string value) => value.ToLowerInvariant().Replace('_', '-');
}
