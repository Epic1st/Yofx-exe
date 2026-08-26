namespace YO4X.Desktop;

internal sealed class DesktopNavigationPolicy
{
    private readonly Uri applicationOrigin;
    private readonly Uri? identityProviderOrigin;

    public DesktopNavigationPolicy(Uri applicationUri, Uri? identityProviderUri = null)
    {
        ArgumentNullException.ThrowIfNull(applicationUri);
        applicationOrigin = Origin(applicationUri);
        identityProviderOrigin = identityProviderUri is null ? null : Origin(identityProviderUri);
    }

    public bool IsAllowedInShell(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.Scheme == "about" && uri.AbsoluteUri == "about:blank"
            || (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
               && (Origin(uri) == applicationOrigin || Origin(uri) == identityProviderOrigin);
    }

    public static bool CanOpenExternally(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo);
    }

    private static Uri Origin(Uri uri) => new UriBuilder(uri.Scheme, uri.Host, uri.Port).Uri;
}
