using Microsoft.Extensions.DependencyInjection.Extensions;
using YO4X.Api;
using YO4X.SecretCoordination;
using YO4X.SecretIngestion.Api;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = SecretBodyReader.MaximumBytes);
string? approvedClientOrigin = ReadExactHttpsOrigin(
    builder.Configuration["SecretIngestion:ApprovedClientOrigin"]);
if (approvedClientOrigin is not null)
{
    builder.Services.AddCors(options => options.AddPolicy(
        "CredentialIngestion",
        policy => policy
            .WithOrigins(approvedClientOrigin)
            .WithMethods(HttpMethods.Post)
            .WithHeaders(
                Microsoft.Net.Http.Headers.HeaderNames.Authorization,
                Microsoft.Net.Http.Headers.HeaderNames.ContentType,
                ApiHeaders.CorrelationId,
                ApiHeaders.IngestionNonce)
            .WithExposedHeaders(ApiHeaders.CorrelationId)
            .SetPreflightMaxAge(TimeSpan.FromMinutes(5))));
}

builder.Services.AddYo4xApiFoundation(options =>
    options.ErrorTypeBase = builder.Configuration["Api:ErrorTypeBase"] ?? "https://errors.yo4x.invalid");
builder.Services.TryAddSecretIngestionPostgres(builder.Configuration);
builder.Services.TryAddScoped<ICredentialIngestionProcessor, UnavailableCredentialIngestionProcessor>();

WebApplication app = builder.Build();
app.UseYo4xApiFoundation();
app.UseYo4xHttpsOnly();
if (approvedClientOrigin is not null)
{
    app.UseCors();
}

app.UseYo4xProblemStatusCodes();
app.MapYo4xHealth(
    _ => ValueTask.FromResult(true),
    IsReadyAsync);

RouteHandlerBuilder ingestion = app.MapPost("/v1/tenants/{tenantId:guid}/credential-ingestion-grants/{grantId:guid}/consume", async (
    Guid tenantId,
    Guid grantId,
    HttpContext context,
    ICredentialIngestionProcessor processor,
    CancellationToken cancellationToken) =>
{
    if (!IngestionProofReader.TryRead(context.Request, tenantId, grantId, out CredentialIngestionProof? proof))
    {
        return ApiProblems.Create(
            context,
            StatusCodes.Status401Unauthorized,
            "INGESTION_PROOF_INVALID",
            "The ingestion grant proof is invalid or inactive.");
    }

    await processor.ConsumeAsync(
        proof!,
        token => SecretBodyReader.ReadAsync(context.Request, token),
        cancellationToken);
    return Results.NoContent();
});
if (approvedClientOrigin is not null)
{
    ingestion.RequireCors("CredentialIngestion");
}

app.Run();

async ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken)
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    return await scope.ServiceProvider
        .GetRequiredService<ICredentialIngestionProcessor>()
        .IsReadyAsync(cancellationToken)
        .ConfigureAwait(false);
}

static string? ReadExactHttpsOrigin(string? value)
{
    if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? origin)
        || origin.Scheme != Uri.UriSchemeHttps
        || origin.PathAndQuery != "/"
        || !string.IsNullOrEmpty(origin.UserInfo)
        || !string.IsNullOrEmpty(origin.Fragment))
    {
        return null;
    }

    return string.Equals(origin.GetLeftPart(UriPartial.Authority), value, StringComparison.Ordinal)
        ? value
        : null;
}

public partial class Program;
