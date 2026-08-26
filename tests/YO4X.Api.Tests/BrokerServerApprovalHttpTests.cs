using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YO4X.BrokerAccounts;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.Identity;

namespace YO4X.Api.Tests;

/// <summary>
/// Exercises the broker-server approval route over HTTP. The route lives in the
/// Control API's top-level program, so this harness mirrors its registration;
/// <see cref="BrokerAccountDiscoveryBoundaryTests"/> pins the production
/// registration to the same shape so the mirror cannot drift unnoticed.
/// </summary>
public sealed class BrokerServerApprovalHttpTests : IAsyncLifetime
{
    private const string IdempotencyKey = "0123456789abcdef0123456789abcdef";
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid SessionId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid ProfileId = Guid.Parse("40000000-0000-0000-0000-000000000004");
    private static readonly Guid DirectoryServerId = Guid.Parse("70000000-0000-0000-0000-000000000007");
    private WebApplication _application = null!;
    private HttpClient _client = null!;
    private ApprovalApplicationProxy _proxy = null!;

    public async ValueTask InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddYo4xApiFoundation(options => options.ErrorTypeBase = "https://errors.test");
        builder.Services
            .AddAuthentication(TestAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                TestAuthenticationHandler.SchemeName,
                _ => { });
        builder.Services.AddAuthorization(options =>
            options.AddPolicy("user", policy => policy.RequireAuthenticatedUser()));

        var proxy = DispatchProxy.Create<IControlPlaneApplication, ApprovalApplicationProxy>();
        _proxy = (ApprovalApplicationProxy)(object)proxy;
        _proxy.Option = new BrokerAccountRegistrationOption(
            ProfileId,
            DirectoryServerId,
            "Directory Broker Ltd",
            "Directory-Broker-Demo",
            BrokerAccountEnvironment.Demo,
            Approved: true);
        builder.Services.AddSingleton(proxy);

        _application = builder.Build();
        _application.UseYo4xApiFoundation();
        _application.UseAuthentication();
        _application.UseAuthorization();
        _application.MapGroup("/v1")
            .RequireAuthorization("user")
            .MapPost("/broker-server-approvals", async (
                ApproveBrokerServer request,
                HttpContext context,
                IControlPlaneApplication application,
                CancellationToken cancellationToken) =>
            {
                BrokerAccountRegistrationOption option = await application.ApproveBrokerServerAsync(
                    ToUserActor(context.User),
                    request,
                    ToMetadata(context),
                    cancellationToken);
                return Results.Created(
                    $"/v1/broker-account-registration-options?query={Uri.EscapeDataString(option.Server)}",
                    option);
            })
            .AddEndpointFilter(new MutationPreconditionFilter());

        await _application.StartAsync();
        _client = _application.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _application.DisposeAsync();
    }

    [Fact]
    public async Task AnonymousApprovalIsRejectedBeforeReachingTheApplication()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/broker-server-approvals")
        {
            Content = JsonContent.Create(new ApproveBrokerServer(DirectoryServerId))
        };
        request.Headers.Add(ApiHeaders.IdempotencyKey, IdempotencyKey);

        using HttpResponseMessage response = await _client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, _proxy.Invocations);
    }

    [Fact]
    public async Task ApprovalWithoutIdempotencyKeyIsRejectedBeforeReachingTheApplication()
    {
        _client.DefaultRequestHeaders.Add(TestAuthenticationHandler.Header, "authenticated");

        using HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/v1/broker-server-approvals",
            new ApproveBrokerServer(DirectoryServerId),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.PreconditionRequired, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("IDEMPOTENCY_KEY_REQUIRED", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, _proxy.Invocations);
    }

    [Fact]
    public async Task AuthenticatedApprovalPromotesExactlyOneDirectoryServer()
    {
        _client.DefaultRequestHeaders.Add(TestAuthenticationHandler.Header, "authenticated");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/broker-server-approvals")
        {
            Content = JsonContent.Create(new ApproveBrokerServer(DirectoryServerId))
        };
        request.Headers.Add(ApiHeaders.IdempotencyKey, IdempotencyKey);

        using HttpResponseMessage response = await _client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, _proxy.Invocations);
        Assert.Equal(DirectoryServerId, _proxy.ObservedRequest?.DirectoryServerId);
        Assert.Equal(IdempotencyKey, _proxy.ObservedMetadata?.IdempotencyKey);
        Assert.NotEqual(Guid.Empty, _proxy.ObservedMetadata?.CorrelationId);
        Assert.Equal(
            "/v1/broker-account-registration-options?query=Directory-Broker-Demo",
            response.Headers.Location?.ToString());

        // Asserted on the wire shape rather than a deserialized record so the
        // response contract itself - camel-cased names, the enum spelled the way
        // the API foundation writes it - is what the test pins.
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement option = document.RootElement;
        Assert.True(option.GetProperty("approved").GetBoolean());
        Assert.Equal(ProfileId, option.GetProperty("brokerProfileId").GetGuid());
        Assert.Equal(DirectoryServerId, option.GetProperty("directoryServerId").GetGuid());
        Assert.Equal("Directory Broker Ltd", option.GetProperty("brokerCompany").GetString());
        Assert.Equal("Directory-Broker-Demo", option.GetProperty("server").GetString());
        Assert.Equal("DEMO", option.GetProperty("environment").GetString());
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fingerprint", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApprovalRejectsAnyPropertyOutsideTheSingleServerContract()
    {
        _client.DefaultRequestHeaders.Add(TestAuthenticationHandler.Header, "authenticated");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/broker-server-approvals")
        {
            Content = JsonContent.Create(new
            {
                directoryServerId = DirectoryServerId,
                brokerProfileId = ProfileId
            })
        };
        request.Headers.Add(ApiHeaders.IdempotencyKey, IdempotencyKey);

        using HttpResponseMessage response = await _client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _proxy.Invocations);
    }

    [Fact]
    public async Task DeniedApprovalIsSurfacedAsARedactedForbiddenProblem()
    {
        _client.DefaultRequestHeaders.Add(TestAuthenticationHandler.Header, "authenticated");
        _proxy.Failure = new AuthorizationDeniedException(
            "BROKER_SERVER_APPROVAL_DENIED",
            "The broker server could not be approved for demo registration.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/broker-server-approvals")
        {
            Content = JsonContent.Create(new ApproveBrokerServer(DirectoryServerId))
        };
        request.Headers.Add(ApiHeaders.IdempotencyKey, IdempotencyKey);

        using HttpResponseMessage response = await _client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("BROKER_SERVER_APPROVAL_DENIED", body.RootElement.GetProperty("code").GetString());
    }

    private static UserActor ToUserActor(ClaimsPrincipal principal) => new(
        ClaimReader.RequiredGuid(principal, "tenant_id"),
        ClaimReader.RequiredGuid(principal, "sub"),
        ClaimReader.RequiredGuid(principal, "session_id"),
        AuthenticationAssurance.Password);

    private static RequestMetadata ToMetadata(HttpContext context)
    {
        MutationPreconditions preconditions = MutationPreconditionFilter.Get(context);
        return new RequestMetadata(
            preconditions.IdempotencyKey,
            CorrelationIdMiddleware.GetGuid(context),
            preconditions.ExpectedVersion,
            null,
            "loopback");
    }

    public class ApprovalApplicationProxy : DispatchProxy
    {
        public BrokerAccountRegistrationOption Option { get; set; } = null!;

        public Exception? Failure { get; set; }

        public int Invocations { get; private set; }

        public ApproveBrokerServer? ObservedRequest { get; private set; }

        public RequestMetadata? ObservedMetadata { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.NotNull(targetMethod);
            Assert.NotNull(args);
            Assert.Equal(nameof(IControlPlaneApplication.ApproveBrokerServerAsync), targetMethod.Name);

            UserActor actor = Assert.IsType<UserActor>(args[0]);
            Assert.Equal(TenantId, actor.TenantId);
            Assert.Equal(UserId, actor.UserId);
            Assert.Equal(SessionId, actor.SessionId);

            Invocations++;
            ObservedRequest = Assert.IsType<ApproveBrokerServer>(args[1]);
            ObservedMetadata = Assert.IsType<RequestMetadata>(args[2]);
            return Failure is null
                ? Task.FromResult(Option)
                : Task.FromException<BrokerAccountRegistrationOption>(Failure);
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "broker-server-approval-test";
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
