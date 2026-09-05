namespace YO4X.Desktop;

internal sealed record DesktopLaunchOptions(
    Uri ApplicationUri,
    Uri? IdentityProviderUri,
    string? DevelopmentIdentityCertificateSha256,
    Uri? DevelopmentFixtureUri,
    bool StartInDevelopmentFixture,
    Uri? ControlApiUri)
{
    internal const string ApplicationUrlEnvironmentVariable = "YO4X_DESKTOP_APP_URL";
    internal const string IdentityUrlEnvironmentVariable = "YO4X_DESKTOP_IDENTITY_URL";
    internal const string IdentityCertificateSha256EnvironmentVariable =
        "YO4X_DESKTOP_IDENTITY_CERTIFICATE_SHA256";
    internal const string ControlApiUrlEnvironmentVariable = "YO4X_CONTROL_API_ORIGIN";
    private const string DefaultApplicationUrl = "http://127.0.0.1:4173/";
    private const string DefaultDevelopmentIdentityUrl = "https://127.0.0.1:7210/";
    private const string DefaultDevelopmentControlApiUrl = "https://127.0.0.1:7209/";

    public Uri InitialUri => StartInDevelopmentFixture
        ? DevelopmentFixtureUri!
        : ApplicationUri;

    public static DesktopLaunchOptions Parse(
        IReadOnlyList<string> arguments,
        Func<string, string?> readEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(readEnvironmentVariable);

        string? commandLineUrl = null;
        string? commandLineIdentityUrl = null;
        string? commandLineIdentityCertificateSha256 = null;
        string? commandLineControlApiUrl = null;
        bool startInFixture = false;
        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            if (string.Equals(argument, "--development-fixture", StringComparison.Ordinal))
            {
                if (startInFixture)
                {
                    throw new ArgumentException("--development-fixture can be specified only once.");
                }

                startInFixture = true;
                continue;
            }

            if (string.Equals(argument, "--app-url", StringComparison.Ordinal))
            {
                if (commandLineUrl is not null || index + 1 >= arguments.Count)
                {
                    throw new ArgumentException("--app-url requires exactly one value.");
                }

                commandLineUrl = arguments[++index];
                continue;
            }

            if (string.Equals(argument, "--identity-url", StringComparison.Ordinal))
            {
                if (commandLineIdentityUrl is not null || index + 1 >= arguments.Count)
                {
                    throw new ArgumentException("--identity-url requires exactly one value.");
                }

                commandLineIdentityUrl = arguments[++index];
                continue;
            }

            if (string.Equals(argument, "--development-identity-certificate-sha256", StringComparison.Ordinal))
            {
                if (commandLineIdentityCertificateSha256 is not null || index + 1 >= arguments.Count)
                {
                    throw new ArgumentException(
                        "--development-identity-certificate-sha256 requires exactly one value.");
                }

                commandLineIdentityCertificateSha256 = arguments[++index];
                continue;
            }

            if (string.Equals(argument, "--control-api-url", StringComparison.Ordinal))
            {
                if (commandLineControlApiUrl is not null || index + 1 >= arguments.Count)
                {
                    throw new ArgumentException("--control-api-url requires exactly one value.");
                }

                commandLineControlApiUrl = arguments[++index];
                continue;
            }

            throw new ArgumentException($"Unsupported desktop option '{argument}'.");
        }

        string configuredUrl = commandLineUrl
            ?? readEnvironmentVariable(ApplicationUrlEnvironmentVariable)
            ?? DefaultApplicationUrl;
        Uri applicationUri = ParseApplicationUri(configuredUrl);
        string? configuredIdentityUrl = commandLineIdentityUrl
            ?? readEnvironmentVariable(IdentityUrlEnvironmentVariable)
            ?? (IsLoopbackDevelopmentOrigin(applicationUri) ? DefaultDevelopmentIdentityUrl : null);
        Uri? identityProviderUri = configuredIdentityUrl is null
            ? null
            : ParseIdentityProviderUri(configuredIdentityUrl);
        string? identityCertificateSha256 = NormalizeDevelopmentCertificateSha256(
            commandLineIdentityCertificateSha256
                ?? readEnvironmentVariable(IdentityCertificateSha256EnvironmentVariable),
            applicationUri,
            identityProviderUri);
        Uri? fixtureUri = IsLoopbackDevelopmentOrigin(applicationUri)
            ? AddDevelopmentFixtureQuery(applicationUri)
            : null;
        if (startInFixture && fixtureUri is null)
        {
            throw new ArgumentException(
                "The development fixture is allowed only for an explicit loopback application URL.");
        }

        string? configuredControlApiUrl = commandLineControlApiUrl
            ?? readEnvironmentVariable(ControlApiUrlEnvironmentVariable)
            ?? (IsLoopbackDevelopmentOrigin(applicationUri) ? DefaultDevelopmentControlApiUrl : null);
        Uri? controlApiUri = configuredControlApiUrl is null
            ? null
            : ParseIdentityProviderUri(configuredControlApiUrl);

        return new DesktopLaunchOptions(
            applicationUri,
            identityProviderUri,
            identityCertificateSha256,
            fixtureUri,
            startInFixture,
            controlApiUri);
    }

    private static Uri ParseApplicationUri(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.Query)
            || uri.AbsolutePath != "/"
            || uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
        {
            throw new ArgumentException(
                "The desktop application URL must be a query-free HTTPS origin, or an HTTP loopback origin for development.");
        }

        return new UriBuilder(uri)
        {
            Host = uri.Host.ToLowerInvariant(),
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty
        }.Uri;
    }

    private static bool IsLoopbackDevelopmentOrigin(Uri uri) =>
        uri.IsLoopback && uri.Port is 4173 or 4174 or 5173;

    private static Uri ParseIdentityProviderUri(string value)
    {
        Uri uri = ParseApplicationUri(value);
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("The desktop identity provider URL must use HTTPS.");
        }

        return uri;
    }

    private static string? NormalizeDevelopmentCertificateSha256(
        string? value,
        Uri applicationUri,
        Uri? identityProviderUri)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim().Replace(":", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (normalized.Length != 64
            || normalized.Any(character => !Uri.IsHexDigit(character))
            || !IsLoopbackDevelopmentOrigin(applicationUri)
            || identityProviderUri is null
            || !identityProviderUri.IsLoopback)
        {
            throw new ArgumentException(
                "A development identity certificate pin must be a SHA-256 fingerprint for an explicitly configured loopback identity origin.");
        }

        return normalized;
    }

    private static Uri AddDevelopmentFixtureQuery(Uri uri) => new UriBuilder(uri)
    {
        Query = "fixture=dashboard"
    }.Uri;
}
