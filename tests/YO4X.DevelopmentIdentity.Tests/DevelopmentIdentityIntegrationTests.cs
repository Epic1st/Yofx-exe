using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using YO4X.DevelopmentIdentity;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace YO4X.DevelopmentIdentity.Tests;

public sealed class DevelopmentIdentityIntegrationTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "yo4x-development-identity-tests",
        Guid.NewGuid().ToString("N"));
    private WebApplicationFactory<global::Program>? factory;

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(root);
        factory = new WebApplicationFactory<global::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("LocalIdentity:Enabled", "true");
            builder.UseSetting(
                "ConnectionStrings:LocalIdentityPostgres",
                "Host=127.0.0.1;Port=1;Database=yo4x;Username=yo4x_local_identity;Password=test-only;Timeout=1;Include Error Detail=false;Log Parameters=false");
            builder.UseSetting("LocalIdentity:DatabasePath", Path.Combine(root, "identity.db"));
        });
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (factory is not null)
        {
            await factory.DisposeAsync().ConfigureAwait(false);
        }

        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DiscoveryAdvertisesOnlyCodeFlowAndPkce()
    {
        using HttpClient client = Client();
        using HttpResponseMessage response = await client.GetAsync(
            "/.well-known/openid-configuration",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        JsonElement rootElement = document.RootElement;
        Assert.Equal(LocalIdentityContract.Issuer, rootElement.GetProperty("issuer").GetString());
        string[] grants = rootElement.GetProperty("grant_types_supported").EnumerateArray()
            .Select(item => item.GetString()!).ToArray();
        Assert.Equal([GrantTypes.AuthorizationCode], grants);
        Assert.Contains(
            CodeChallengeMethods.Sha256,
            rootElement.GetProperty("code_challenge_methods_supported").EnumerateArray()
                .Select(item => item.GetString()));
        Assert.DoesNotContain(
            GrantTypes.Password,
            rootElement.GetProperty("grant_types_supported").EnumerateArray()
                .Select(item => item.GetString()));
    }

    [Fact]
    public async Task RegisteredClientIsPublicExactLoopbackAndRequiresPkce()
    {
        _ = Client();
        await using AsyncServiceScope scope = factory!.Services.CreateAsyncScope();
        IOpenIddictApplicationManager manager =
            scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        object application = await manager.FindByClientIdAsync(
            LocalIdentityContract.ClientId,
            TestContext.Current.CancellationToken) ?? throw new InvalidOperationException();

        Assert.Equal(
            ClientTypes.Public,
            await manager.GetClientTypeAsync(
                application,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            LocalIdentityContract.RedirectUris.Order(StringComparer.Ordinal),
            (await manager.GetRedirectUrisAsync(
                application,
                TestContext.Current.CancellationToken)).Order(StringComparer.Ordinal));
        Assert.True(await manager.HasRequirementAsync(
            application,
            Requirements.Features.ProofKeyForCodeExchange,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RegistrationFormUsesSecureHostOnlyAntiforgeryCookieAndStrongIdentityPolicy()
    {
        using HttpClient client = Client();
        using HttpResponseMessage response = await client.GetAsync(
            "/account/register",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("__Host-yo4x-local-antiforgery=", cookie, StringComparison.Ordinal);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        string html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("__RequestVerificationToken", html, StringComparison.Ordinal);
        Assert.Contains("name=\"returnUrl\" value=\"\"", html, StringComparison.Ordinal);
        Assert.Contains("YO4X secure access", html, StringComparison.Ordinal);
        Assert.Contains("/account/theme.css", html, StringComparison.Ordinal);
        Assert.Contains(
            "style-src 'self'",
            Assert.Single(response.Headers.GetValues("Content-Security-Policy")),
            StringComparison.Ordinal);
        Assert.Contains(
            $"form-action 'self' {LocalIdentityContract.FrontendOrigin}",
            Assert.Single(response.Headers.GetValues("Content-Security-Policy")),
            StringComparison.Ordinal);

        using HttpResponseMessage theme = await client.GetAsync(
            "/account/theme.css",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, theme.StatusCode);
        Assert.Equal("text/css", theme.Content.Headers.ContentType?.MediaType);
        Assert.Contains(
            ".identity-shell",
            await theme.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            StringComparison.Ordinal);

        IdentityOptions identity = factory!.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<IdentityOptions>>().Value;
        Assert.True(identity.SignIn.RequireConfirmedEmail);
        Assert.True(identity.User.RequireUniqueEmail);
        Assert.True(identity.Password.RequireDigit);
        Assert.True(identity.Password.RequireLowercase);
        Assert.True(identity.Password.RequireUppercase);
        Assert.True(identity.Password.RequireNonAlphanumeric);
        Assert.True(identity.Password.RequiredLength >= 12);
        Assert.True(identity.Lockout.MaxFailedAccessAttempts <= 5);

        using HttpResponseMessage staleForm = await client.GetAsync(
            "/account/sign-in?returnUrl=%2F",
            TestContext.Current.CancellationToken);
        string staleFormHtml = await staleForm.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("name=\"returnUrl\" value=\"\"", staleFormHtml, StringComparison.Ordinal);

        using HttpResponseMessage authorizationForm = await client.GetAsync(
            "/account/sign-in?returnUrl=%2Fconnect%2Fauthorize%3Fclient_id%3Dyo4x-web-development",
            TestContext.Current.CancellationToken);
        string authorizationFormHtml = await authorizationForm.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        Assert.Contains(
            "name=\"returnUrl\" value=\"/connect/authorize?client_id=yo4x-web-development\"",
            authorizationFormHtml,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The frontend holds its access token in memory only, so it re-asks for a code with
    /// <c>prompt=none</c> on every page load to restore a session a reload would otherwise
    /// discard. If that question rendered the credential form instead of answering
    /// <c>login_required</c> at the redirect URI, signing in would appear to revert straight
    /// back to the sign-in page — which is exactly how the defect reached us.
    /// </summary>
    [Fact]
    public async Task SilentAuthorizationWithoutASessionAnswersLoginRequiredInsteadOfShowingAForm()
    {
        using HttpClient client = Client();
        using HttpResponseMessage response = await client.GetAsync(
            "/connect/authorize?client_id=yo4x-web-development"
            + "&redirect_uri=" + Uri.EscapeDataString(LocalIdentityContract.FrontendOrigin + "/auth/callback")
            + "&response_type=code&scope=openid+profile+email&prompt=none"
            + "&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM&code_challenge_method=S256"
            + "&state=state&nonce=nonce",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Uri location = response.Headers.Location!;
        Assert.Equal(
            LocalIdentityContract.FrontendOrigin + "/auth/callback",
            location.GetLeftPart(UriPartial.Path));
        Assert.Contains($"error={Errors.LoginRequired}", location.Query, StringComparison.Ordinal);
        Assert.Contains("state=state", location.Query, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same request without <c>prompt=none</c> must still take a first-time visitor to the
    /// credential form, so the silent path cannot quietly become the only path.
    /// </summary>
    [Fact]
    public async Task AuthorizationWithoutASessionStillChallengesForCredentials()
    {
        using HttpClient client = Client();
        using HttpResponseMessage response = await client.GetAsync(
            "/connect/authorize?client_id=yo4x-web-development"
            + "&redirect_uri=" + Uri.EscapeDataString(LocalIdentityContract.FrontendOrigin + "/auth/callback")
            + "&response_type=code&scope=openid+profile+email"
            + "&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM&code_challenge_method=S256"
            + "&state=state&nonce=nonce",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains(
            "/account/sign-in",
            response.Headers.Location!.OriginalString,
            StringComparison.Ordinal);
    }

    private HttpClient Client()
    {
        HttpClient client = factory!.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri(LocalIdentityContract.Issuer)
            });
        return client;
    }
}
