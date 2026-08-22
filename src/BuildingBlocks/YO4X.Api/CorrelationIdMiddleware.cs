using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace YO4X.Api;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string ItemKey = "yo4x.correlation_id";
    private const int MaximumLength = 128;

    public async Task InvokeAsync(HttpContext context)
    {
        string? candidate = context.Request.Headers[ApiHeaders.CorrelationId].FirstOrDefault();
        string correlationId = IsValid(candidate)
            ? Guid.Parse(candidate!).ToString("N")
            : CreateCorrelationId();

        context.Items[ItemKey] = correlationId;
        context.Response.Headers[ApiHeaders.CorrelationId] = correlationId;
        Activity.Current?.SetTag("yo4x.correlation_id", correlationId);

        await next(context).ConfigureAwait(false);
    }

    public static string Get(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out object? value) && value is string correlationId
            ? correlationId
            : CreateCorrelationId();

    public static Guid GetGuid(HttpContext context) => Guid.ParseExact(Get(context), "N");

    private static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= MaximumLength
        && Guid.TryParse(value, out _);

    private static string CreateCorrelationId() => Guid.CreateVersion7().ToString("N");

}
