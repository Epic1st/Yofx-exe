namespace YO4X.DevelopmentIdentity;

public static class LocalIdentityContract
{
    public const string ClientId = "yo4x-web-development";
    public const string ControlPlaneAudience = "yo4x-control-plane";
    public const string FrontendOrigin = "http://127.0.0.1:4173";
    public const string RedirectUri = FrontendOrigin + "/auth/callback";
    public const string PostLogoutRedirectUri = FrontendOrigin + "/";
    public const string Issuer = "https://127.0.0.1:7210/";
    public static readonly Guid TenantId =
        Guid.Parse("019c8d27-763d-7000-8000-000000000001");
}
