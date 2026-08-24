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
using YO4X.ControlPlane.Application;

namespace YO4X.Api.Tests;

public sealed class ApiFoundationTests : IAsyncLifetime
{
    private WebApplication _application = null!;
    private HttpClient _client = null!;
    private readonly HealthProbeHarness _health = new();

    public async ValueTask InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddYo4xApiFoundation(options => options.ErrorTypeBase = "https://errors.test");

        _application = builder.Build();
        _application.UseYo4xApiFoundation();
        _application.MapYo4xHealth(
            _ => ValueTask.FromResult(true),
            _health.ProbeAsync,
            options =>
            {
                options.SnapshotLifetime = TimeSpan.FromMilliseconds(100);
                options.ProbeTimeout = TimeSpan.FromMilliseconds(500);
            });
        _application.MapPost("/mutation", (MutationRequest request, HttpContext context) =>
            Results.Ok(new
            {
                request.Name,
                Preconditions = MutationPreconditionFilter.Get(context)
            }))
            .AddEndpointFilter(new MutationPreconditionFilter(requireExpectedVersion: true));
        _application.MapGet("/domain-error", IResult () => throw new DomainException("SAFE_FAILURE", "The operation is not safe."));
        _application.MapGet("/strategy-compatibility-contract", () => Results.Ok(
            new StrategyCompatibilityProjection(
                4,
                4,
                [
                    new StrategyCompatibilityItem(
                        Guid.Parse("10000000-0000-0000-0000-000000000001"),
                        "Analyzed strategy",
                        StrategyCompatibilitySourceType.Mq5,
                        StrategyCompatibilityAnalysisState.Analyzed,
                        3,
                        null),
                    new StrategyCompatibilityItem(
                        Guid.Parse("10000000-0000-0000-0000-000000000002"),
                        "Review header",
                        StrategyCompatibilitySourceType.Mqh,
                        StrategyCompatibilityAnalysisState.ReviewRequired,
                        2,
                        null),
                    new StrategyCompatibilityItem(
                        Guid.Parse("10000000-0000-0000-0000-000000000003"),
                        "Unsupported strategy",
                        StrategyCompatibilitySourceType.Mq5,
                        StrategyCompatibilityAnalysisState.Unsupported,
                        1,
                        null),
                    new StrategyCompatibilityItem(
                        Guid.Parse("10000000-0000-0000-0000-000000000004"),
                        "Pending header",
                        StrategyCompatibilitySourceType.Mqh,
                        StrategyCompatibilityAnalysisState.Pending,
                        0,
                        null)
                ])));

        await _application.StartAsync();
        _client = _application.GetTestClient();
    }

    public async ValueTask DisposeAsync()
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

    [Fact]
    public async Task StrategyCompatibilityContractMatchesFrontendEnumAndShape()
    {
        using HttpResponseMessage response = await _client.GetAsync(
            "/strategy-compatibility-contract",
            CancellationToken.None);

        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using JsonDocument body = JsonDocument.Parse(json);
        Assert.Equal(4, body.RootElement.GetProperty("analyzedFileCount").GetInt32());
        Assert.Equal(4, body.RootElement.GetProperty("totalFileCount").GetInt32());
        JsonElement[] items = body.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(4, items.Length);
        Assert.Equal(["MQ5", "MQH", "MQ5", "MQH"],
            items.Select(item => item.GetProperty("sourceType").GetString()!).ToArray());
        Assert.Equal(["ANALYZED", "REVIEW_REQUIRED", "UNSUPPORTED", "PENDING"],
            items.Select(item => item.GetProperty("analysisState").GetString()!).ToArray());
        Assert.All(items, item => Assert.Equal(JsonValueKind.Null, item.GetProperty("reportPath").ValueKind));
        Assert.DoesNotContain("sourceContent", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evidenceDocument", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("findings", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnonymousReadinessPollingIsSingleFlightAndShortLived()
    {
        _health.BlockNextProbe();

        Task<HttpResponseMessage>[] requests = Enumerable.Range(0, 32)
            .Select(_ => _client.GetAsync("/health/ready", CancellationToken.None))
            .ToArray();
        await _health.WaitUntilStartedAsync();

        Assert.Equal(1, _health.InvocationCount);
        _health.ReleaseProbe(isHealthy: true);

        HttpResponseMessage[] responses = await Task.WhenAll(requests);
        try
        {
            Assert.All(responses, response =>
                Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }

        using (HttpResponseMessage cached = await _client.GetAsync(
                   "/health/ready",
                   CancellationToken.None))
        {
            Assert.Equal(HttpStatusCode.OK, cached.StatusCode);
        }

        Assert.Equal(1, _health.InvocationCount);

        await Task.Delay(
            TimeSpan.FromMilliseconds(150),
            TestContext.Current.CancellationToken);
        using HttpResponseMessage refreshed = await _client.GetAsync(
            "/health/ready",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Equal(2, _health.InvocationCount);
    }

    [Fact]
    public async Task CancellationIgnoringReadinessProbeTimesOutWithoutOverlapping()
    {
        _health.BlockNextProbe(ignoreCancellation: true);

        using HttpResponseMessage timedOut = await _client.GetAsync(
            "/health/ready",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, timedOut.StatusCode);
        Assert.Equal(1, _health.InvocationCount);

        using HttpResponseMessage sharedFailure = await _client.GetAsync(
            "/health/ready",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, sharedFailure.StatusCode);
        Assert.Equal(1, _health.InvocationCount);

        _health.ReleaseProbe(isHealthy: true);
        await _health.WaitUntilCompletedAsync();
        await Task.Delay(
            TimeSpan.FromMilliseconds(150),
            TestContext.Current.CancellationToken);

        using HttpResponseMessage refreshed = await _client.GetAsync(
            "/health/ready",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Equal(2, _health.InvocationCount);
    }

    [Fact]
    public async Task CancelledReadinessCallerDoesNotCancelTheSharedProbe()
    {
        _health.BlockNextProbe();
        using var callerCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        Task<HttpResponseMessage> cancelledRequest = _client.GetAsync(
            "/health/ready",
            callerCancellation.Token);
        await _health.WaitUntilStartedAsync();
        callerCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledRequest);
        Assert.Equal(1, _health.InvocationCount);

        Task<HttpResponseMessage> survivingRequest = _client.GetAsync(
            "/health/ready",
            TestContext.Current.CancellationToken);
        _health.ReleaseProbe(isHealthy: true);
        using HttpResponseMessage response = await survivingRequest;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _health.InvocationCount);
    }

    private sealed record MutationRequest(string Name);

    private sealed class HealthProbeHarness
    {
        private TaskCompletionSource _started = NewCompletion();
        private TaskCompletionSource _completed = NewCompletion();
        private TaskCompletionSource<bool>? _release;
        private bool _ignoreCancellation;
        private int _invocationCount;

        internal int InvocationCount => Volatile.Read(ref _invocationCount);

        internal void BlockNextProbe(bool ignoreCancellation = false)
        {
            _started = NewCompletion();
            _completed = NewCompletion();
            _release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _ignoreCancellation = ignoreCancellation;
        }

        internal Task WaitUntilStartedAsync() => _started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        internal Task WaitUntilCompletedAsync() =>
            _completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        internal void ReleaseProbe(bool isHealthy)
        {
            TaskCompletionSource<bool>? release = _release;
            Assert.NotNull(release);
            Assert.True(release.TrySetResult(isHealthy));
        }

        internal async ValueTask<bool> ProbeAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocationCount);
            _started.TrySetResult();
            TaskCompletionSource<bool>? release = _release;
            try
            {
                return release is null
                    ? true
                    : _ignoreCancellation
                        ? await release.Task
                        : await release.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                _completed.TrySetResult();
            }
        }

        private static TaskCompletionSource NewCompletion() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
