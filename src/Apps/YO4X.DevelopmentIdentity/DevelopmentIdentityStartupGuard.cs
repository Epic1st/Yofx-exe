namespace YO4X.DevelopmentIdentity;

public static class DevelopmentIdentityStartupGuard
{
    public static void Validate(IHostEnvironment environment, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);

        if (!environment.IsDevelopment()
            || !configuration.GetValue<bool>("LocalIdentity:Enabled")
            || !LocalIdentityPostgresOptions.TryCreate(
                configuration.GetConnectionString("LocalIdentityPostgres"),
                out _))
        {
            throw new InvalidOperationException(
                "The local identity provider requires Development, explicit opt-in, and its dedicated loopback PostgreSQL login.");
        }

        if (!Uri.TryCreate(LocalIdentityContract.Issuer, UriKind.Absolute, out Uri? issuer)
            || !issuer.IsLoopback
            || issuer.Scheme != Uri.UriSchemeHttps
            || LocalIdentityContract.AllowedFrontendOrigins.Any(value =>
                !Uri.TryCreate(value, UriKind.Absolute, out Uri? frontend)
                || !frontend.IsLoopback
                || frontend.Scheme != Uri.UriSchemeHttp
                || frontend.GetLeftPart(UriPartial.Authority) != value))
        {
            throw new InvalidOperationException("The local identity endpoints must remain loopback-only.");
        }
    }
}
