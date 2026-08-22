using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using YO4X.Api;
using YO4X.BuildingBlocks;

namespace YO4X.Api.Tests;

public sealed class ApiFoundationTests : IAsyncLifetime
{
    private WebApplication _application = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddYo4xApiFoundation(options => options.ErrorTypeBase = "https://errors.test");

        _application = builder.Build();
        _application.UseYo4xApiFoundation();
        _application.MapPost("/mutation", (MutationRequest request, HttpContext context) =>
            Results.Ok(new
            {
                request.Name,
                Preconditions = MutationPreconditionFilter.Get(context)
            }))
            .AddEndpointFilter(new MutationPreconditionFilter(requireExpectedVersion: true));
        _application.MapGet("/domain-error", IResult () => throw new DomainException("SAFE_FAILURE", "The operation is not safe."));

        await _application.StartAsync();
        _client = _application.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _application.DisposeAsync();
    }

    [Fact]
    public async Task MutationWithoutIdempotencyKeyFailsWithPreconditionRequired()
    {
        using HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/mutation",
            new MutationRequest("test"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.PreconditionRequired, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken.None));
        Assert.Equal("IDEMPOTENCY_KEY_REQUIRED", body.RootElement.GetProperty("code").GetString());
        Assert.True(response.Headers.Contains(ApiHeaders.CorrelationId));
    }

    [Fact]
    public async Task MutationWithInvalidVersionFailsWithoutCallingHandler()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mutation")
        {
            Content = JsonContent.Create(new MutationRequest("test"))
        };
        request.Headers.Add(ApiHeaders.IdempotencyKey, "0123456789abcdef0123456789abcdef");
        request.Headers.TryAddWithoutValidation(ApiHeaders.IfMatch, "not-a-version");

        using HttpResponseMessage response = await _client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StrictJsonRejectsUnexpectedProperties()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mutation")
        {
            Content = JsonContent.Create(new { name = "test", unexpected = "rejected" })
        };
        request.Headers.Add(ApiHeaders.IdempotencyKey, "0123456789abcdef0123456789abcdef");
        request.Headers.Add(ApiHeaders.IfMatch, "\"0\"");

        using HttpResponseMessage response = await _client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DomainErrorUsesRedactedProblemContract()
    {
        using HttpResponseMessage response = await _client.GetAsync("/domain-error", CancellationToken.None);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        string json = await response.Content.ReadAsStringAsync(CancellationToken.None);
        Assert.Contains("SAFE_FAILURE", json, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("YO4X.Api.Tests", json, StringComparison.Ordinal);
    }

    private sealed record MutationRequest(string Name);
}
