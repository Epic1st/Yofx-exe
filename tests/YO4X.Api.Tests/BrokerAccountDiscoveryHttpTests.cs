using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YO4X.BrokerAccounts;
using YO4X.ControlPlane.Api;
using YO4X.ControlPlane.Application;

namespace YO4X.Api.Tests;

public sealed class BrokerAccountDiscoveryHttpTests : IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid SessionId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid ProfileId = Guid.Parse("40000000-0000-0000-0000-000000000004");
    private static readonly Guid DirectoryServerId = Guid.Parse("70000000-0000-0000-0000-000000000007");
    private WebApplication _application = null!;
    private HttpClient _client = null!;
    private DiscoveryApplicationProxy _proxy = null!;

    public async ValueTask InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services
            .AddAuthentication(TestAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                TestAuthenticationHandler.SchemeName,
                _ => { });
        builder.Services.AddAuthorization(options =>
            options.AddPolicy("user", policy => policy.RequireAuthenticatedUser()));
        IControlPlaneApplication application = CreateApplication();
        _proxy = (DiscoveryApplicationProxy)(object)application;
        builder.Services.AddSingleton(application);

        _application = builder.Build();
        _application.UseAuthentication();
        _application.UseAuthorization();
        _application.MapGroup("/v1")
            .RequireAuthorization("user")
            .MapBrokerAccountDiscovery();
        await _application.StartAsync();
        _client = _application.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _application.DisposeAsync();
    }

    [Fact]
    public async Task AnonymousDiscoveryRequestsAreRejected()
    {
        using HttpResponseMessage accounts = await _client.GetAsync(
            "/v1/broker-accounts",
            TestContext.Current.CancellationToken);
        using HttpResponseMessage options = await _client.GetAsync(
            "/v1/broker-account-registration-options",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, accounts.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, options.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedDiscoveryReturnsOnlyRedactedActorBoundContracts()
    {
        _client.DefaultRequestHeaders.Add(TestAuthenticationHandler.Header, "authenticated");

        using HttpResponseMessage accountsResponse = await _client.GetAsync(
            "/v1/broker-accounts",
            TestContext.Current.CancellationToken);
        using HttpResponseMessage optionsResponse = await _client.GetAsync(
            "/v1/broker-account-registration-options",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, accountsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, optionsResponse.StatusCode);
        BrokerAccountView account = Assert.Single(
            await accountsResponse.Content.ReadFromJsonAsync<IReadOnlyList<BrokerAccountView>>(
                TestContext.Current.CancellationToken)
            ?? []);
        BrokerAccountRegistrationOption option = Assert.Single(
            await optionsResponse.Content.ReadFromJsonAsync<IReadOnlyList<BrokerAccountRegistrationOption>>(
                TestContext.Current.CancellationToken)
            ?? []);
        Assert.Equal("******42", account.MaskedLogin);
        Assert.Equal(ProfileId, option.BrokerProfileId);
        Assert.Equal("Broker-Demo", option.Server);
        Assert.Equal(BrokerAccountEnvironment.Demo, option.Environment);

        string bodies = await accountsResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
            + await optionsResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("password", bodies, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", bodies, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fingerprint", bodies, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("123456789", bodies, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegistrationOptionsWithoutQuerySearchNothingAndStayOnTheApprovedList()
    {
        _client.DefaultRequestHeaders.Add(TestAuthenticationHandler.Header, "authenticated");

        using HttpResponseMessage response = await _client.GetAsync(
            "/v1/broker-account-registration-options",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(_proxy.QueryObserved);
        Assert.Null(_proxy.ObservedQuery);
        BrokerAccountRegistrationOption option = Assert.Single(
            await response.Content.ReadFromJsonAsync<IReadOnlyList<BrokerAccountRegistrationOption>>(
                TestContext.Current.CancellationToken)
            ?? []);
        Assert.True(option.Approved);
        Assert.Equal(ProfileId, option.BrokerProfileId);
    }

    [Fact]
    public async Task RegistrationOptionSearchForwardsTheQueryAndReturnsUnapprovedDirectoryMatches()
    {
        _client.DefaultRequestHeaders.Add(TestAuthenticationHandler.Header, "authenticated");

        using HttpResponseMessage response = await _client.GetAsync(
            "/v1/broker-account-registration-options?query=Directory%20Broker",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Directory Broker", _proxy.ObservedQuery);
        BrokerAccountRegistrationOption match = Assert.Single(
            await response.Content.ReadFromJsonAsync<IReadOnlyList<BrokerAccountRegistrationOption>>(
                TestContext.Current.CancellationToken)
            ?? []);
        Assert.False(match.Approved);
        Assert.Null(match.BrokerProfileId);
        Assert.Equal(DirectoryServerId, match.DirectoryServerId);
        Assert.Equal("Directory Broker Ltd", match.BrokerCompany);
        Assert.Equal("Directory-Broker-Demo", match.Server);
        Assert.Equal(BrokerAccountEnvironment.Demo, match.Environment);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fingerprint", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnonymousRegistrationOptionSearchIsRejectedBeforeReachingTheApplication()
    {
        using HttpResponseMessage response = await _client.GetAsync(
            "/v1/broker-account-registration-options?query=Directory%20Broker",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(_proxy.QueryObserved);
    }

    private static IControlPlaneApplication CreateApplication()
    {
        var proxy = DispatchProxy.Create<IControlPlaneApplication, DiscoveryApplicationProxy>();
        ((DiscoveryApplicationProxy)(object)proxy).Accounts =
        [
            new BrokerAccountView(
                Guid.Parse("50000000-0000-0000-0000-000000000005"),
                Guid.Parse("60000000-0000-0000-0000-000000000006"),
                "Broker-Demo",
                "******42",
                BrokerAccountEnvironment.Demo,
                null,
                "UNKNOWN",
                0,
                new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero))
        ];
        ((DiscoveryApplicationProxy)(object)proxy).Options =
        [
            new BrokerAccountRegistrationOption(
                ProfileId,
                null,
                "Broker Demo Ltd",
                "Broker-Demo",
                BrokerAccountEnvironment.Demo,
                Approved: true)
        ];

        // A directory hit the tenant has not approved carries no broker profile,
        // which is what stops the dashboard from offering it as a registration
        // target before an approval exists.
        ((DiscoveryApplicationProxy)(object)proxy).DirectoryMatches =
        [
            new BrokerAccountRegistrationOption(
                null,
                DirectoryServerId,
                "Directory Broker Ltd",
                "Directory-Broker-Demo",
                BrokerAccountEnvironment.Demo,
                Approved: false)
        ];
        return proxy;
    }

    public class DiscoveryApplicationProxy : DispatchProxy
    {
        public IReadOnlyList<BrokerAccountView> Accounts { get; set; } = [];

        public IReadOnlyList<BrokerAccountRegistrationOption> Options { get; set; } = [];

        public IReadOnlyList<BrokerAccountRegistrationOption> DirectoryMatches { get; set; } = [];

        /// <summary>
        /// Records the search term exactly as the route handed it over, so the
        /// test can prove the query reaches the application unmodified instead
        /// of being silently normalized or dropped at the HTTP boundary.
        /// </summary>
        public string? ObservedQuery { get; private set; }

        public bool QueryObserved { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.NotNull(targetMethod);
            Assert.NotNull(args);
            UserActor actor = Assert.IsType<UserActor>(args[0]);
            Assert.Equal(TenantId, actor.TenantId);
            Assert.Equal(UserId, actor.UserId);
            Assert.Equal(SessionId, actor.SessionId);

            switch (targetMethod.Name)
            {
                case nameof(IControlPlaneApplication.GetBrokerAccountsAsync):
                    return Task.FromResult(Accounts);
                case nameof(IControlPlaneApplication.GetBrokerAccountRegistrationOptionsAsync):
                    ObservedQuery = args[1] is null ? null : Assert.IsType<string>(args[1]);
                    QueryObserved = true;
                    return Task.FromResult(ObservedQuery is null ? Options : DirectoryMatches);
                default:
                    throw new NotSupportedException(targetMethod.Name);
            }
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "broker-discovery-test";
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
