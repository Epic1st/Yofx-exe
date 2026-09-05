using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using System.Security.Claims;
using YO4X.DevelopmentIdentity;

namespace YO4X.DevelopmentIdentity.Tests;

public sealed class DevelopmentIdentitySecurityTests
{
    private const string ValidPostgres =
        "Host=127.0.0.1;Database=yo4x;Username=yo4x_local_identity;Password=test-only;Include Error Detail=false;Log Parameters=false";

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Staging", true)]
    [InlineData("Development", false)]
    public void StartupFailsUnlessDevelopmentAndExplicitlyEnabled(
        string environmentName,
        bool enabled)
    {
        var environment = new TestEnvironment(environmentName);
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LocalIdentity:Enabled"] = enabled.ToString()
                , ["ConnectionStrings:LocalIdentityPostgres"] = ValidPostgres
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            DevelopmentIdentityStartupGuard.Validate(environment, configuration));
    }

    [Fact]
    public void ExplicitDevelopmentOptInPasses()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LocalIdentity:Enabled"] = "true"
                , ["ConnectionStrings:LocalIdentityPostgres"] = ValidPostgres
            })
            .Build();

        DevelopmentIdentityStartupGuard.Validate(
            new TestEnvironment("Development"),
            configuration);
    }

    [Theory]
    [InlineData("Host=127.0.0.1;Database=yo4x;Username=yo4x_control_api;Password=x")]
    [InlineData("Host=192.0.2.1;Database=yo4x;Username=yo4x_local_identity;Password=x")]
    [InlineData("Host=127.0.0.1;Database=yo4x;Username=yo4x_local_identity;Password=x;Include Error Detail=true")]
    public void DedicatedPostgresConnectionRejectsBroaderOrRemoteIdentity(string connectionString)
    {
        Assert.False(LocalIdentityPostgresOptions.TryCreate(connectionString, out _));
    }

    [Fact]
    public void PublicClientIsPinnedToExactLoopbackUris()
    {
        Assert.Equal("yo4x-web-development", LocalIdentityContract.ClientId);
        Assert.Equal("yo4x-control-plane", LocalIdentityContract.ControlPlaneAudience);
        Assert.Equal("http://127.0.0.1:4173", LocalIdentityContract.FrontendOrigin);
        Assert.Equal("http://127.0.0.1:4173/auth/callback", LocalIdentityContract.RedirectUri);
        Assert.Equal(
            ["http://127.0.0.1:4173", "http://127.0.0.1:4174", "http://127.0.0.1:5173"],
            LocalIdentityContract.AllowedFrontendOrigins);
        Assert.True(new Uri(LocalIdentityContract.RedirectUri).IsLoopback);
        Assert.True(new Uri(LocalIdentityContract.Issuer).IsLoopback);
        Assert.Equal(Uri.UriSchemeHttps, new Uri(LocalIdentityContract.Issuer).Scheme);
        Assert.NotEqual(Guid.Empty, LocalIdentityContract.TenantId);
    }

    [Theory]
    [InlineData("/account/register")]
    [InlineData("/account/sign-in")]
    public void AuthenticatedRepeatedAccountPostRecoversFromAntiforgeryFailure(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = path;
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "test"));

        Assert.True(AuthenticatedAccountFormRecoveryMiddleware.ShouldRecover(context));
    }

    [Theory]
    [InlineData(false, "POST", "/account/register", 400)]
    [InlineData(true, "GET", "/account/register", 400)]
    [InlineData(true, "POST", "/connect/authorize", 400)]
    [InlineData(true, "POST", "/account/register", 422)]
    public void AntiforgeryRecoveryRemainsNarrow(
        bool authenticated,
        string method,
        string path,
        int statusCode)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.StatusCode = statusCode;
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity(
                authenticated ? [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())] : [],
                authenticated ? "test" : null));

        Assert.False(AuthenticatedAccountFormRecoveryMiddleware.ShouldRecover(context));
    }

    private sealed class TestEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "YO4X.DevelopmentIdentity.Tests";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
