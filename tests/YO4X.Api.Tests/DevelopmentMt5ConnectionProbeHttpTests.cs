using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using YO4X.ControlPlane.Api;

namespace YO4X.Api.Tests;

public sealed class DevelopmentMt5ConnectionProbeHttpTests : IAsyncLifetime
{
    private WebApplication application = null!;
    private HttpClient client = null!;

    public async ValueTask InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Environment.EnvironmentName = Environments.Development;
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DevelopmentMt5ConnectionProbe:Enabled"] = "true"
        });
        builder.Services
            .AddAuthentication(TestAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                TestAuthenticationHandler.SchemeName,
                _ => { });
        builder.Services.AddAuthorization(options =>
            options.AddPolicy("user", policy => policy.RequireAuthenticatedUser()));
        builder.Services.AddSingleton<IDevelopmentMt5ConnectionProbe>(
            new StubProbe());

        application = builder.Build();
        application.Use((context, next) =>
        {
            context.Connection.RemoteIpAddress = context.Request.Headers.ContainsKey("X-Public-Remote")
                ? IPAddress.Parse("203.0.113.10")
                : IPAddress.Loopback;
            return next();
        });
        application.UseAuthentication();
        application.UseAuthorization();
        application.MapGroup("/v1")
            .RequireAuthorization("user")
            .MapDevelopmentMt5ConnectionProbe(builder.Configuration, builder.Environment);
        await application.StartAsync();
        client = application.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        client.Dispose();
        await application.DisposeAsync();
    }

    [Fact]
    public async Task AnonymousProbeIsRejected()
    {
        using HttpResponseMessage response = await client.PostAsync(
            "/v1/development/mt5-connection-probe",
            content: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedProbeReturnsOnlyRedactedConnectionObservation()
    {
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.Header, "authenticated");

        using HttpResponseMessage response = await client.PostAsync(
            "/v1/development/mt5-connection-probe",
            content: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        DevelopmentMt5ConnectionProbeResult result =
            Assert.IsType<DevelopmentMt5ConnectionProbeResult>(
                await response.Content.ReadFromJsonAsync<DevelopmentMt5ConnectionProbeResult>(
                    TestContext.Current.CancellationToken));
        Assert.True(result.IsSuccess);
        Assert.Equal("mt5_connect_probe_succeeded", result.Code);
        Assert.NotNull(result.Observation);
        Assert.True(result.Observation.DisconnectConfirmed);

        string body = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        Assert.DoesNotContain("login", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("endpoint", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("artifact", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("balance", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("position", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthenticatedNonLoopbackProbeIsNotExposed()
    {
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.Header, "authenticated");
        client.DefaultRequestHeaders.Add("X-Public-Remote", "true");

        using HttpResponseMessage response = await client.PostAsync(
            "/v1/development/mt5-connection-probe",
            content: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void ProductionRejectsExplicitProbeEnablement()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DevelopmentMt5ConnectionProbe:Enabled"] = "true"
            })
            .Build();
        var services = new ServiceCollection();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddDevelopmentMt5ConnectionProbe(
                configuration,
                new TestEnvironment(Environments.Production)));

        Assert.Contains("only in Development", exception.Message, StringComparison.Ordinal);
    }

    private sealed class StubProbe : IDevelopmentMt5ConnectionProbe
    {
        public Task<DevelopmentMt5ConnectionProbeResult> ProbeAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new DevelopmentMt5ConnectionProbeResult(
                1,
                true,
                "mt5_connect_probe_succeeded",
                new DevelopmentMt5ConnectionProbeObservation(
                    "HEDGING",
                    "DEMO",
                    "UNKNOWN",
                    "USD",
                    true,
                    new DateTimeOffset(2026, 8, 24, 11, 15, 39, TimeSpan.Zero))));
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "development-mt5-probe-test";
        public const string Header = "X-YO4X-Test-Authentication";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey(Header))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            Claim[] claims = [new("sub", Guid.NewGuid().ToString("D"))];
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, SchemeName)));
        }
    }

    private sealed class TestEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "YO4X.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
