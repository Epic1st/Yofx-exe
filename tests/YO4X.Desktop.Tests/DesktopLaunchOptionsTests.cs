using YO4X.Desktop;

namespace YO4X.Desktop.Tests;

public sealed class DesktopLaunchOptionsTests
{
    [Fact]
    public void DefaultsToRealLoopbackFrontend()
    {
        DesktopLaunchOptions options = Parse();

        Assert.Equal("http://127.0.0.1:4173/", options.ApplicationUri.AbsoluteUri);
        Assert.Equal("https://127.0.0.1:7210/", options.IdentityProviderUri!.AbsoluteUri);
        Assert.Null(options.DevelopmentIdentityCertificateSha256);
        Assert.Equal(
            "http://127.0.0.1:4173/?fixture=dashboard",
            options.DevelopmentFixtureUri!.AbsoluteUri);
        Assert.False(options.StartInDevelopmentFixture);
        Assert.Equal(options.ApplicationUri, options.InitialUri);
    }

    [Fact]
    public void ExplicitDevelopmentFixtureStartsOnlyTheFixtureView()
    {
        DesktopLaunchOptions options = Parse(["--development-fixture"]);

        Assert.True(options.StartInDevelopmentFixture);
        Assert.Equal(options.DevelopmentFixtureUri, options.InitialUri);
    }

    [Fact]
    public void CommandLineHttpsOriginOverridesEnvironment()
    {
        DesktopLaunchOptions options = Parse(
            ["--app-url", "https://control.yo4x.example/"],
            "https://ignored.yo4x.example/");

        Assert.Equal("https://control.yo4x.example/", options.ApplicationUri.AbsoluteUri);
        Assert.Null(options.IdentityProviderUri);
        Assert.Null(options.DevelopmentFixtureUri);
    }

    [Theory]
    [InlineData("http://control.yo4x.example/")]
    [InlineData("https://user@control.yo4x.example/")]
    [InlineData("https://control.yo4x.example/path")]
    [InlineData("https://control.yo4x.example/?fixture=dashboard")]
    [InlineData("https://control.yo4x.example/#fragment")]
    [InlineData("file:///C:/frontend/index.html")]
    public void RejectsUnsafeOrNonCanonicalOrigins(string value)
    {
        Assert.Throws<ArgumentException>(() => Parse(["--app-url", value]));
    }

    [Fact]
    public void RejectsFixtureForRemoteOrigin()
    {
        Assert.Throws<ArgumentException>(() => Parse(
            ["--app-url", "https://control.yo4x.example/", "--development-fixture"]));
    }

    [Fact]
    public void RejectsUnknownAndDuplicateOptions()
    {
        Assert.Throws<ArgumentException>(() => Parse(["--unknown"]));
        Assert.Throws<ArgumentException>(() => Parse(
            ["--development-fixture", "--development-fixture"]));
        Assert.Throws<ArgumentException>(() => Parse(
            ["--app-url", "http://127.0.0.1:4173/", "--app-url", "http://127.0.0.1:4174/"]));
        Assert.Throws<ArgumentException>(() => Parse(
            ["--identity-url", "https://identity.yo4x.example/", "--identity-url", "https://other.example/"]));
    }

    [Fact]
    public void ExplicitIdentityOriginIsStrictlyAllowlisted()
    {
        DesktopLaunchOptions options = Parse(
            ["--app-url", "https://control.yo4x.example/", "--identity-url", "https://identity.yo4x.example/"]);
        var policy = new DesktopNavigationPolicy(options.ApplicationUri, options.IdentityProviderUri);

        Assert.True(policy.IsAllowedInShell(new Uri("https://identity.yo4x.example/connect/authorize")));
        Assert.False(policy.IsAllowedInShell(new Uri("https://evil.example/connect/authorize")));
        Assert.Throws<ArgumentException>(() => Parse(
            ["--identity-url", "http://127.0.0.1:7210/"]));
    }

    [Fact]
    public void DevelopmentIdentityCertificatePinIsRestrictedToLoopback()
    {
        const string fingerprint = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        DesktopLaunchOptions options = Parse(
            ["--development-identity-certificate-sha256", fingerprint]);

        Assert.Equal(fingerprint.ToUpperInvariant(), options.DevelopmentIdentityCertificateSha256);
        Assert.Throws<ArgumentException>(() => Parse(
            ["--app-url", "https://control.yo4x.example/",
             "--identity-url", "https://identity.yo4x.example/",
             "--development-identity-certificate-sha256", fingerprint]));
        Assert.Throws<ArgumentException>(() => Parse(
            ["--development-identity-certificate-sha256", "not-a-fingerprint"]));
    }

    [Fact]
    public void NavigationPolicyAllowsOnlyTheConfiguredOriginInsideTheShell()
    {
        var policy = new DesktopNavigationPolicy(new Uri("https://control.yo4x.example/"));

        Assert.True(policy.IsAllowedInShell(new Uri("about:blank")));
        Assert.True(policy.IsAllowedInShell(new Uri("https://control.yo4x.example/v1/me")));
        Assert.False(policy.IsAllowedInShell(new Uri("https://other.yo4x.example/")));
        Assert.False(policy.IsAllowedInShell(new Uri("javascript:alert(1)")));
        Assert.False(policy.IsAllowedInShell(new Uri("file:///C:/secret.txt")));
    }

    [Fact]
    public void ExternalNavigationAllowsOnlyCredentialFreeHttps()
    {
        Assert.True(DesktopNavigationPolicy.CanOpenExternally(
            new Uri("https://identity.yo4x.example/login")));
        Assert.False(DesktopNavigationPolicy.CanOpenExternally(
            new Uri("http://identity.yo4x.example/login")));
        Assert.False(DesktopNavigationPolicy.CanOpenExternally(
            new Uri("https://user@identity.yo4x.example/login")));
    }

    private static DesktopLaunchOptions Parse(
        IReadOnlyList<string>? arguments = null,
        string? environmentValue = null) =>
        DesktopLaunchOptions.Parse(
            arguments ?? [],
            name => name == DesktopLaunchOptions.ApplicationUrlEnvironmentVariable
                ? environmentValue
                : null);
}
