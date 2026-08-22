using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace YO4X.Api;

public sealed record MutationPreconditions(string IdempotencyKey, long? ExpectedVersion);

public sealed partial class MutationPreconditionFilter(bool requireExpectedVersion = false) : IEndpointFilter
{
    private const int MaximumKeyLength = 200;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        HttpContext httpContext = context.HttpContext;
        string? idempotencyKey = httpContext.Request.Headers[ApiHeaders.IdempotencyKey].FirstOrDefault();
        if (!IsAcceptableIdempotencyKey(idempotencyKey))
        {
            return ApiProblems.Create(
                httpContext,
                StatusCodes.Status428PreconditionRequired,
                "IDEMPOTENCY_KEY_REQUIRED",
                "A high-entropy Idempotency-Key is required.");
        }

        long? expectedVersion = null;
        if (httpContext.Request.Headers.TryGetValue(ApiHeaders.IfMatch, out var values))
        {
            string value = values.ToString().Trim().Trim('"');
            if (!long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long parsed)
                || parsed < 0)
            {
                return ApiProblems.Create(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    "INVALID_IF_MATCH",
                    "If-Match must contain a non-negative aggregate version.");
            }

            expectedVersion = parsed;
        }

        if (requireExpectedVersion && expectedVersion is null)
        {
            return ApiProblems.Create(
                httpContext,
                StatusCodes.Status428PreconditionRequired,
                "EXPECTED_VERSION_REQUIRED",
                "If-Match is required for this mutation.");
        }

        httpContext.Items[typeof(MutationPreconditions)] = new MutationPreconditions(idempotencyKey!, expectedVersion);
        return await next(context).ConfigureAwait(false);
    }

    public static MutationPreconditions Get(HttpContext context) =>
        context.Items.TryGetValue(typeof(MutationPreconditions), out object? value)
            && value is MutationPreconditions preconditions
                ? preconditions
                : throw new InvalidOperationException("Mutation preconditions were not evaluated for this endpoint.");

    private static bool IsAcceptableIdempotencyKey(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= MaximumKeyLength
        && (HexKeyPattern().IsMatch(value) || Base64UrlKeyPattern().IsMatch(value));

    [GeneratedRegex("^[A-Fa-f0-9]{32,200}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexKeyPattern();

    [GeneratedRegex("^[A-Za-z0-9_-]{22,200}$", RegexOptions.CultureInvariant)]
    private static partial Regex Base64UrlKeyPattern();
}
