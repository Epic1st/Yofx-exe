using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace YO4X.DevelopmentIdentity.Controllers;

public sealed class AuthorizationController(
    UserManager<DevelopmentUser> userManager,
    SignInManager<DevelopmentUser> signInManager) : Controller
{
    [AllowAnonymous]
    [HttpGet("~/connect/authorize")]
    public async Task<IActionResult> Authorize()
    {
        OpenIddictRequest request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request is unavailable.");
        AuthenticateResult cookie = await HttpContext.AuthenticateAsync(
            IdentityConstants.ApplicationScheme).ConfigureAwait(false);
        if (!cookie.Succeeded || request.HasPromptValue(PromptValues.Login))
        {
            // prompt=none means "answer from the existing session, or not at all". The frontend
            // keeps its access token in memory only, so it asks this question on every page load
            // to restore a session a reload would otherwise discard. Showing the credential form
            // here would turn that quiet question into a visible bounce back to sign-in, which is
            // precisely the symptom the silent restore exists to remove.
            if (request.HasPromptValue(PromptValues.None))
            {
                return SignInRequired();
            }

            return Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = Request.PathBase + Request.Path + Request.QueryString
                },
                IdentityConstants.ApplicationScheme);
        }

        DevelopmentUser user = await userManager.GetUserAsync(cookie.Principal!)
            ?? throw new InvalidOperationException("The local development user is unavailable.");
        if (!await signInManager.CanSignInAsync(user).ConfigureAwait(false)
            || !user.EmailConfirmed
            || user.SessionId == Guid.Empty)
        {
            // A cookie whose account can no longer sign in is, from the client's side, the same
            // situation as no cookie at all: the attempt has to end at the sign-in entry point
            // rather than in a half-authorized workspace.
            return SignInRequired();
        }

        ClaimsPrincipal principal = await CreatePrincipalAsync(user, request.GetScopes())
            .ConfigureAwait(false);
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// The OpenID Connect answer for "no usable session": <c>login_required</c>, returned to the
    /// registered redirect URI rather than rendered as a page. The client recognizes it and shows
    /// its own sign-in entry point.
    /// </summary>
    private ForbidResult SignInRequired() => Forbid(
        new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.LoginRequired,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                "No local identity session is available."
        }),
        OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

    [AllowAnonymous]
    [HttpPost("~/connect/token")]
    [IgnoreAntiforgeryToken]
    [Produces("application/json")]
    public async Task<IActionResult> Exchange()
    {
        OpenIddictRequest request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request is unavailable.");
        if (!request.IsAuthorizationCodeGrantType())
        {
            throw new InvalidOperationException("Only the authorization-code grant is enabled.");
        }

        AuthenticateResult result = await HttpContext.AuthenticateAsync(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme).ConfigureAwait(false);
        string? subject = result.Principal?.GetClaim(Claims.Subject);
        DevelopmentUser? user = Guid.TryParse(subject, out Guid userId)
            ? await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false)
            : null;
        if (user is null
            || !await signInManager.CanSignInAsync(user).ConfigureAwait(false)
            || !user.EmailConfirmed
            || user.SessionId == Guid.Empty)
        {
            return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        ClaimsPrincipal principal = await CreatePrincipalAsync(
            user,
            result.Principal!.GetScopes()).ConfigureAwait(false);
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task<ClaimsPrincipal> CreatePrincipalAsync(
        DevelopmentUser user,
        IEnumerable<string> scopes)
    {
        ClaimsPrincipal principal = await signInManager.CreateUserPrincipalAsync(user)
            .ConfigureAwait(false);
        ClaimsIdentity identity = (ClaimsIdentity)principal.Identity!;
        identity.SetClaim(Claims.Subject, user.Id.ToString("D"));
        identity.SetClaim(Claims.Email, user.Email!);
        identity.SetClaim(Claims.EmailVerified, "true");
        identity.SetClaim("tenant_id", user.TenantId.ToString("D"));
        identity.SetClaim("session_id", user.SessionId.ToString("D"));
        identity.SetClaim("assurance", "password");
        principal.SetScopes(scopes);
        principal.SetResources(LocalIdentityContract.ControlPlaneAudience);
        principal.SetDestinations(static claim => claim.Type switch
        {
            Claims.Subject or Claims.Email or Claims.EmailVerified
                or "tenant_id" or "session_id" or "assurance" =>
                [Destinations.AccessToken, Destinations.IdentityToken],
            Claims.Name => [Destinations.AccessToken, Destinations.IdentityToken],
            _ => [Destinations.AccessToken]
        });
        return principal;
    }
}
