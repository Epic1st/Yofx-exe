using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YO4X.ControlPlane.Api;
using YO4X.ControlPlane.Application;

namespace YO4X.Api.Tests;

/// <summary>
/// Boundary contract for the frontend projection surface. The projections are plain
/// authenticated CRUD reads and writes: they must never be reachable anonymously, must
/// never silently accept an unmapped request member, must report a missing resource with
/// the canonical problem shape, and must not demand the mutation preconditions reserved
/// for authority-bearing control-plane commands.
/// </summary>
public sealed class FrontendProjectionBoundaryTests : IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("22222222-0000-0000-0000-000000000002");
    private static readonly Guid SessionId = Guid.Parse("33333333-0000-0000-0000-000000000003");
    private static readonly Guid KnownStrategyId = Guid.Parse("44444444-0000-0000-0000-000000000004");
    private static readonly Guid KnownBotId = Guid.Parse("55555555-0000-0000-0000-000000000005");

    private static readonly string[] Routes =
    [
        "GET /v1/catalog/strategies",
        "GET /v1/catalog/strategies/44444444-0000-0000-0000-000000000004",
        "GET /v1/catalog/strategies/44444444-0000-0000-0000-000000000004/reviews",
        "GET /v1/catalog/strategies/44444444-0000-0000-0000-000000000004/inputs",
        "GET /v1/bots",
        "GET /v1/bots/55555555-0000-0000-0000-000000000005",
        "GET /v1/bots/uptime",
        "GET /v1/bots/55555555-0000-0000-0000-000000000005/settings",
        "PUT /v1/bots/55555555-0000-0000-0000-000000000005/settings",
        "GET /v1/broker-symbols",
        "POST /v1/bots",
        "POST /v1/bots/55555555-0000-0000-0000-000000000005/status",
        "GET /v1/backtests",
        "GET /v1/backtests/77777777-0000-0000-0000-000000000007",
        "POST /v1/backtests",
        "GET /v1/cloud/plans",
        "GET /v1/cloud/runners",
        "GET /v1/cloud/regions",
        "GET /v1/journal",
        "GET /v1/dashboard/summary",
        "GET /v1/bridge/status"
    ];

    private WebApplication _application = null!;
    private HttpClient _client = null!;
    private FrontendProjectionProxy _projections = null!;

    public async ValueTask InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddYo4xApiFoundation(options =>
            options.ErrorTypeBase = "https://errors.yo4x.invalid");
        builder.Services
            .AddAuthentication(TestAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                TestAuthenticationHandler.SchemeName,
                _ => { });
        builder.Services.AddAuthorization(options =>
            options.AddPolicy("user", policy => policy.RequireAuthenticatedUser()));
        IFrontendProjectionApplication application = CreateApplication();
        _projections = (FrontendProjectionProxy)application;
        builder.Services.AddSingleton(application);
        builder.Services.AddSingleton<IBotExecutionCoordinator>(
            new ProjectionBotExecutionCoordinator(application));

        _application = builder.Build();
        _application.UseYo4xApiFoundation();
        _application.UseYo4xProblemStatusCodes();
        _application.UseAuthentication();
        _application.UseAuthorization();
        _application.MapGroup("/v1")
            .RequireAuthorization("user")
            .MapFrontendProjections();
        await _application.StartAsync();
        _client = _application.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _application.DisposeAsync();
    }

    private sealed class ProjectionBotExecutionCoordinator(
        IFrontendProjectionApplication application) : IBotExecutionCoordinator
    {
        public Task<BotView?> ChangeStatusAsync(
            UserActor actor,
            Guid botId,
            BotStatusChange request,
            CancellationToken cancellationToken) =>
            application.SetBotStatusAsync(actor, botId, request, cancellationToken);
    }

    [Fact]
    public async Task EveryFrontendProjectionRouteRejectsAnonymousRequests()
    {
        var observed = new List<string>(Routes.Length);
        foreach (string route in Routes)
        {
            using HttpResponseMessage response = await SendAsync(route, "{}");
            observed.Add($"{route} -> {(int)response.StatusCode}");
        }

        Assert.Equal(
            Routes.Select(static route => $"{route} -> 401").ToList(),
            observed);
        Assert.Empty(_projections.Invocations);
    }

    [Theory]
    [InlineData(
        "POST /v1/bots",
        """
        {
            "strategyId": "44444444-0000-0000-0000-000000000004",
            "brokerAccountId": null,
            "name": "Trend rider",
            "symbol": "EURUSD",
            "riskLabel": "Balanced",
            "host": "LOCAL",
            "unmappedMember": "rejected"
        }
        """)]
    [InlineData(
        "POST /v1/backtests",
        """
        {
            "strategyId": "44444444-0000-0000-0000-000000000004",
            "periodStart": "2026-01-01",
            "periodEnd": "2026-06-30",
            "unmappedMember": "rejected"
        }
        """)]
    public async Task UnmappedRequestMembersAreRejectedBeforeTheApplicationIsReached(
        string route,
        string body)
    {
        Authenticate();

        using HttpResponseMessage response = await SendAsync(route, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument problem = await ReadProblemAsync(response);
        Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("HTTP_ERROR", problem.RootElement.GetProperty("code").GetString());
        Assert.False(
            string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("correlationId").GetString()));
        Assert.Empty(_projections.Invocations);
    }

    [Theory]
    [InlineData(
        "POST /v1/bots",
        """
        {
            "strategyId": "44444444-0000-0000-0000-000000000004",
            "brokerAccountId": null,
            "name": "Trend rider",
            "symbol": "EURUSD",
            "riskLabel": "Balanced",
            "host": "LOCAL"
        }
        """,
        201)]
    [InlineData(
        "POST /v1/backtests",
        """
        {
            "strategyId": "44444444-0000-0000-0000-000000000004",
            "periodStart": "2026-01-01",
            "periodEnd": "2026-06-30"
        }
        """,
        201)]
    [InlineData(
        "POST /v1/bots/55555555-0000-0000-0000-000000000005/status",
        """
        {
            "status": "RUNNING"
        }
        """,
        200)]
    public async Task ProjectionMutationsDoNotDemandMutationPreconditionHeaders(
        string route,
        string body,
        int expectedStatus)
    {
        Authenticate();

        using HttpResponseMessage response = await SendAsync(route, body);

        Assert.False(_client.DefaultRequestHeaders.Contains(ApiHeaders.IdempotencyKey));
        Assert.False(_client.DefaultRequestHeaders.Contains(ApiHeaders.IfMatch));
        Assert.Equal(expectedStatus, (int)response.StatusCode);
        Assert.NotEqual(HttpStatusCode.PreconditionRequired, response.StatusCode);
        Assert.NotEmpty(_projections.Invocations);
    }

    [Theory]
    [InlineData("GET /v1/catalog/strategies/66666666-0000-0000-0000-000000000006", "{}")]
    [InlineData("GET /v1/bots/66666666-0000-0000-0000-000000000006", "{}")]
    [InlineData("GET /v1/bots/66666666-0000-0000-0000-000000000006/settings", "{}")]
    [InlineData(
        "PUT /v1/bots/66666666-0000-0000-0000-000000000006/settings",
        """
        {
            "symbol": "EURUSD",
            "timeframe": "H1",
            "volume": 0.10,
            "magicNumber": 2026,
            "inputs": []
        }
        """)]
    [InlineData(
        "POST /v1/bots/66666666-0000-0000-0000-000000000006/status",
        """
        {
            "status": "RUNNING"
        }
        """)]
    public async Task UnknownResourceIdentifiersReportTheCanonicalProblemShape(
        string route,
        string body)
    {
        Authenticate();

        using HttpResponseMessage response = await SendAsync(route, body);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using JsonDocument problem = await ReadProblemAsync(response);
        Assert.Equal(404, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("RESOURCE_NOT_FOUND", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            "The resource was not found.",
            problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(
            "https://errors.yo4x.invalid/resource-not-found",
            problem.RootElement.GetProperty("type").GetString());
        string? correlationId = problem.RootElement.GetProperty("correlationId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(correlationId));
        Assert.True(Guid.TryParseExact(correlationId, "N", out _));
        Assert.False(problem.RootElement.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task KnownResourceIdentifiersAreServedInsteadOfTheNotFoundProblem()
    {
        Authenticate();

        using HttpResponseMessage detail = await SendAsync(
            $"GET /v1/catalog/strategies/{KnownStrategyId:D}",
            "{}");
        using HttpResponseMessage bot = await SendAsync($"GET /v1/bots/{KnownBotId:D}", "{}");

        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bot.StatusCode);
    }

    /// <summary>
    /// A settings save that reached a bot the caller owns answers 204 with no body, and
    /// an unmapped member is refused before the application is reached, exactly as every
    /// other projection mutation is.
    /// </summary>
    [Fact]
    public async Task SavingBotSettingsAnswersWithNoContentAndRejectsUnmappedMembers()
    {
        Authenticate();

        using HttpResponseMessage saved = await SendAsync(
            $"PUT /v1/bots/{KnownBotId:D}/settings",
            """
            {
                "symbol": "EURUSD",
                "timeframe": "H1",
                "volume": 0.10,
                "magicNumber": 2026,
                "inputs": [{ "name": "InpLots", "value": "0.25" }]
            }
            """);

        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        Assert.Equal(0, saved.Content.Headers.ContentLength ?? 0);
        Assert.Contains(
            nameof(IFrontendProjectionApplication.UpdateBotSettingsAsync),
            _projections.Invocations);

        _projections.Invocations.Clear();
        using HttpResponseMessage rejected = await SendAsync(
            $"PUT /v1/bots/{KnownBotId:D}/settings",
            """
            {
                "symbol": "EURUSD",
                "timeframe": "H1",
                "volume": 0.10,
                "magicNumber": 2026,
                "inputs": [],
                "unmappedMember": "rejected"
            }
            """);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Empty(_projections.Invocations);
    }

    /// <summary>
    /// The broker instrument list is a read like any other: it carries the caller's actor
    /// and passes the optional server and substring filters through untouched.
    /// </summary>
    [Fact]
    public async Task BrokerSymbolsAreServedThroughTheAuthenticatedActor()
    {
        Authenticate();

        using HttpResponseMessage response = await SendAsync(
            "GET /v1/broker-symbols?server=Demo-Server&query=eur",
            "{}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            nameof(IFrontendProjectionApplication.GetBrokerSymbolsAsync),
            _projections.Invocations);
        Assert.Equal("Demo-Server", _projections.LastBrokerSymbolServer);
        Assert.Equal("eur", _projections.LastBrokerSymbolQuery);
    }

    [Fact]
    public void FrontendProjectionRoutesAreActorBoundAndFreeOfMutationPreconditions()
    {
        string program = ReadRepositoryFile(
            "src",
            "Apps",
            "YO4X.ControlPlane.Api",
            "Program.cs");
        string routes = ReadRepositoryFile(
            "src",
            "Apps",
            "YO4X.ControlPlane.Api",
            "FrontendProjectionEndpoints.cs");

        Assert.Contains("app.MapGroup(\"/v1\").RequireAuthorization(\"user\")", program, StringComparison.Ordinal);
        Assert.Contains("user.MapFrontendProjections();", program, StringComparison.Ordinal);
        Assert.Equal(Routes.Length, CountOccurrences(routes, "ToUserActor(context.User)"));
        Assert.Equal(
            Routes.Count(static route => route.StartsWith("GET ", StringComparison.Ordinal)),
            CountOccurrences(routes, "user.MapGet("));
        Assert.Equal(
            Routes.Count(static route => route.StartsWith("POST ", StringComparison.Ordinal)),
            CountOccurrences(routes, "user.MapPost("));
        Assert.Equal(
            Routes.Count(static route => route.StartsWith("PUT ", StringComparison.Ordinal)),
            CountOccurrences(routes, "user.MapPut("));
        Assert.DoesNotContain("AllowAnonymous", routes, StringComparison.Ordinal);
        Assert.DoesNotContain("MutationPreconditionFilter", routes, StringComparison.Ordinal);
        Assert.DoesNotContain("AddEndpointFilter", routes, StringComparison.Ordinal);
        Assert.DoesNotContain("Idempotency", routes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("If-Match", routes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RequireAuthorization", routes, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(routes, "Results.Created("));
        Assert.Equal(7, CountOccurrences(routes, "\"RESOURCE_NOT_FOUND\""));

        // A settings save answers with no body at all. Returning the saved view would
        // invite the caller to treat the echo as confirmation of what is stored, when
        // the only reading that proves anything is a fresh GET.
        Assert.Equal(1, CountOccurrences(routes, "Results.NoContent()"));
    }

    private void Authenticate() =>
        _client.DefaultRequestHeaders.Add(TestAuthenticationHandler.Header, "authenticated");

    private async Task<HttpResponseMessage> SendAsync(string route, string body)
    {
        int separator = route.IndexOf(' ', StringComparison.Ordinal);
        string method = route[..separator];
        string path = route[(separator + 1)..];
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (string.Equals(method, "POST", StringComparison.Ordinal)
            || string.Equals(method, "PUT", StringComparison.Ordinal))
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<JsonDocument> ReadProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static int CountOccurrences(string value, string pattern) =>
        value.Split(pattern, StringSplitOptions.None).Length - 1;

    private static string ReadRepositoryFile(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments]));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "YO4X.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root could not be located.");
    }

    private static IFrontendProjectionApplication CreateApplication() =>
        DispatchProxy.Create<IFrontendProjectionApplication, FrontendProjectionProxy>();

    public class FrontendProjectionProxy : DispatchProxy
    {
        private static readonly DateTimeOffset Instant =
            new(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);

        private static readonly StrategyCatalogItem Item = new(
            KnownStrategyId,
            "trend-rider",
            "Trend rider",
            "Aurora Labs",
            "AL",
            "Trend",
            "EURUSD",
            "H1",
            "1.4.0",
            4.5m,
            12,
            340,
            false,
            2900,
            29000,
            "USD",
            Instant);

        private static readonly BotView Bot = new(
            KnownBotId,
            "Trend rider",
            KnownStrategyId,
            "Trend rider",
            null,
            null,
            "EURUSD",
            "Balanced",
            BotStatus.Running,
            BotHost.Local,
            null,
            null,
            [],
            Instant,
            Instant);

        private static readonly BacktestView Backtest = new(
            Guid.Parse("77777777-0000-0000-0000-000000000007"),
            KnownStrategyId,
            "Trend rider",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 6, 30),
            0m,
            0m,
            0m,
            0,
            "USD",
            BacktestStatus.Queued,
            Instant,
            null);

        public List<string> Invocations { get; } = [];

        public string? LastBrokerSymbolServer { get; private set; }

        public string? LastBrokerSymbolQuery { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.NotNull(targetMethod);
            Assert.NotNull(args);
            UserActor actor = Assert.IsType<UserActor>(args[0]);
            Assert.Equal(TenantId, actor.TenantId);
            Assert.Equal(UserId, actor.UserId);
            Assert.Equal(SessionId, actor.SessionId);
            Invocations.Add(targetMethod.Name);

            return targetMethod.Name switch
            {
                nameof(IFrontendProjectionApplication.GetStrategyCatalogAsync) =>
                    Task.FromResult(new StrategyCatalogPage(1, 24, 1, 1, [Item], ["Trend"], ["EURUSD"])),
                nameof(IFrontendProjectionApplication.GetStrategyDetailAsync) =>
                    Task.FromResult(IsKnown(args[1], KnownStrategyId)
                        ? new StrategyDetailView(
                            Item,
                            "A trend following projection.",
                            "A trend following projection.",
                            new StrategyAuthorView("Aurora Labs", "AL", 1, 4.5m),
                            [],
                            [],
                            0)
                        : null),
                nameof(IFrontendProjectionApplication.GetStrategyReviewsAsync) =>
                    Task.FromResult<IReadOnlyList<StrategyReviewView>>([]),
                nameof(IFrontendProjectionApplication.GetBotsAsync) =>
                    Task.FromResult<IReadOnlyList<BotView>>([Bot]),
                nameof(IFrontendProjectionApplication.GetBotAsync) =>
                    Task.FromResult(IsKnown(args[1], KnownBotId) ? Bot : null),
                nameof(IFrontendProjectionApplication.CreateBotAsync) => Task.FromResult(Bot),
                nameof(IFrontendProjectionApplication.SetBotStatusAsync) =>
                    Task.FromResult(IsKnown(args[1], KnownBotId) ? Bot : null),
                nameof(IFrontendProjectionApplication.GetBotUptimeAsync) =>
                    Task.FromResult(new BotUptimeProjection(7, 0, [])),
                nameof(IFrontendProjectionApplication.GetBotSettingsAsync) =>
                    Task.FromResult(IsKnown(args[1], KnownBotId)
                        ? new BotSettingsView(
                            KnownBotId,
                            KnownStrategyId,
                            "Trend rider",
                            "EURUSD",
                            "H1",
                            0.10m,
                            2026,
                            [],
                            [])
                        : null),
                nameof(IFrontendProjectionApplication.UpdateBotSettingsAsync) =>
                    Task.FromResult(IsKnown(args[1], KnownBotId)),
                nameof(IFrontendProjectionApplication.GetBrokerSymbolsAsync) =>
                    RecordBrokerSymbolQuery(args[1] as string, args[2] as string),
                nameof(IFrontendProjectionApplication.GetStrategyInputsAsync) =>
                    Task.FromResult(IsKnown(args[1], KnownStrategyId)
                        ? new StrategyInputsView(KnownStrategyId, "Trend rider", [])
                        : null),
                nameof(IFrontendProjectionApplication.GetBacktestsAsync) =>
                    Task.FromResult<IReadOnlyList<BacktestView>>([Backtest]),
                nameof(IFrontendProjectionApplication.GetBacktestDetailAsync) =>
                    Task.FromResult(IsKnown(args[1], Backtest.Id)
                        ? new BacktestDetailView(
                            Backtest,
                            "EURUSD",
                            "H1",
                            "EVERY_TICK_REAL",
                            null,
                            null,
                            null,
                            [])
                        : null),
                nameof(IFrontendProjectionApplication.CreateBacktestAsync) =>
                    Task.FromResult(Backtest),
                nameof(IFrontendProjectionApplication.GetCloudPlansAsync) =>
                    Task.FromResult<IReadOnlyList<CloudPlanView>>([]),
                nameof(IFrontendProjectionApplication.GetCloudRunnersAsync) =>
                    Task.FromResult<IReadOnlyList<CloudRunnerView>>([]),
                nameof(IFrontendProjectionApplication.GetCloudRegionsAsync) =>
                    Task.FromResult<IReadOnlyList<CloudRegionView>>([]),
                nameof(IFrontendProjectionApplication.GetJournalAsync) =>
                    Task.FromResult(new JournalPage([], null)),
                nameof(IFrontendProjectionApplication.GetDashboardSummaryAsync) =>
                    Task.FromResult(new DashboardSummaryView([], [], 0, 0)),
                nameof(IFrontendProjectionApplication.GetBridgeStatusAsync) =>
                    Task.FromResult(new BridgeStatusView(true, "0.0.0", 1, 0, 0)),
                _ => throw new NotSupportedException(targetMethod.Name)
            };
        }

        private static bool IsKnown(object? candidate, Guid expected) =>
            candidate is Guid value && value == expected;

        private Task<IReadOnlyList<BrokerSymbolView>> RecordBrokerSymbolQuery(
            string? server,
            string? query)
        {
            LastBrokerSymbolServer = server;
            LastBrokerSymbolQuery = query;
            return Task.FromResult<IReadOnlyList<BrokerSymbolView>>([]);
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "frontend-projection-test";
        public const string Header = "X-YO4X-Test-Authentication";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey(Header))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            Claim[] claims =
            [
                new("tenant_id", TenantId.ToString("D")),
                new("sub", UserId.ToString("D")),
                new("session_id", SessionId.ToString("D")),
                new("assurance", "password")
            ];
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
