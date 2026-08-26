using System.Security.Claims;
using YO4X.Api;
using YO4X.ControlPlane.Application;
using YO4X.Identity;

namespace YO4X.ControlPlane.Api;

public static class BrokerAccountDiscoveryEndpoints
{
    public static RouteGroupBuilder MapBrokerAccountDiscovery(this RouteGroupBuilder user)
    {
        user.MapGet("/broker-accounts", async (
            HttpContext context,
            IControlPlaneApplication application,
            CancellationToken cancellationToken) =>
            Results.Ok(await application.GetBrokerAccountsAsync(
                ToUserActor(context.User),
                cancellationToken)));

        // `query` searches the imported MetaTrader 5 server directory. Omitting
        // it keeps the original meaning of this route: only what the caller's
        // tenant may already link.
        user.MapGet("/broker-account-registration-options", async (
            HttpContext context,
            IControlPlaneApplication application,
            string? query,
            CancellationToken cancellationToken) =>
            Results.Ok(await application.GetBrokerAccountRegistrationOptionsAsync(
                ToUserActor(context.User),
                query,
                cancellationToken)));

        return user;
    }

    private static UserActor ToUserActor(ClaimsPrincipal principal)
    {
        string assuranceValue = principal.FindFirstValue("assurance") ?? "password";
        AuthenticationAssurance assurance = assuranceValue.ToLowerInvariant() switch
        {
            "hardware_key" => AuthenticationAssurance.HardwareKey,
            "webauthn" => AuthenticationAssurance.WebAuthn,
            "totp" => AuthenticationAssurance.Totp,
            _ => AuthenticationAssurance.Password
        };

        return new UserActor(
            ClaimReader.RequiredGuid(principal, "tenant_id"),
            ClaimReader.RequiredGuid(principal, "sub"),
            ClaimReader.RequiredGuid(principal, "session_id"),
            assurance);
    }
}
