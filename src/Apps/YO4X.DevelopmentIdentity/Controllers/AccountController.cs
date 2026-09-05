using System.Net;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace YO4X.DevelopmentIdentity.Controllers;

[Route("account")]
public sealed class AccountController(
    UserManager<DevelopmentUser> userManager,
    SignInManager<DevelopmentUser> signInManager,
    IAntiforgery antiforgery,
    LocalIdentityProvisioner provisioner) : Controller
{
    [HttpGet("theme.css")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public ContentResult Theme() => Content(
        """
        :root{font-family:Inter,"Segoe UI",system-ui,sans-serif;color:#10213f;background:#f4f7fc;font-synthesis:none}*{box-sizing:border-box}body{margin:0;min-height:100vh;background:radial-gradient(circle at 12% 8%,#e5efff 0,transparent 30%),linear-gradient(145deg,#f9fbff 0%,#eef4fd 100%);display:grid;place-items:center;padding:32px}.identity-shell{width:min(1080px,100%);min-height:650px;background:#fff;border:1px solid #dfe7f3;border-radius:24px;box-shadow:0 24px 70px rgba(37,72,126,.14);display:grid;grid-template-columns:minmax(320px,44%) 1fr;overflow:hidden}.brand-panel{padding:54px 48px;color:#fff;background:linear-gradient(150deg,#0758e8 0%,#063ba5 70%,#052b77 100%);display:flex;flex-direction:column}.brand{font-size:42px;font-weight:800;letter-spacing:-2px}.brand span{font-size:16px;font-weight:600;letter-spacing:.1px;margin-left:9px;opacity:.86}.brand-copy{margin:auto 0}.brand-copy p:first-child{font-size:13px;font-weight:750;letter-spacing:1.6px;text-transform:uppercase;color:#bcd5ff}.brand-copy h2{font-size:36px;line-height:1.12;letter-spacing:-1.2px;margin:14px 0}.brand-copy p:last-child{font-size:16px;line-height:1.7;color:#dce9ff;max-width:360px}.trust{font-size:13px;color:#c7dbff}.form-panel{padding:68px 70px;display:flex;flex-direction:column;justify-content:center}.eyebrow{margin:0 0 10px;color:#0758e8;font-size:12px;font-weight:800;letter-spacing:1.5px;text-transform:uppercase}h1{font-size:34px;letter-spacing:-1px;margin:0 0 10px}.intro{color:#63728c;line-height:1.6;margin:0 0 28px}.errors{margin:0 0 20px;padding:13px 16px 13px 36px;border:1px solid #f4b8bd;border-radius:10px;background:#fff4f5;color:#9a2530;font-size:14px}.field{display:block;margin-bottom:19px;color:#273957;font-size:13px;font-weight:700}.field input{display:block;width:100%;margin-top:8px;border:1px solid #cbd7e8;border-radius:10px;padding:13px 14px;font:inherit;font-size:15px;color:#142541;outline:none;transition:.15s}.field input:focus{border-color:#1768ec;box-shadow:0 0 0 4px rgba(23,104,236,.11)}.hint{display:block;color:#77869f;font-size:12px;font-weight:400;line-height:1.5;margin-top:7px}.primary{width:100%;border:0;border-radius:10px;padding:14px 18px;background:#0758e8;color:#fff;font:inherit;font-weight:750;cursor:pointer;box-shadow:0 9px 22px rgba(7,88,232,.22)}.primary:hover{background:#064bc5}.alternate{text-align:center;color:#71809a;font-size:14px;margin:24px 0 0}.alternate a{color:#0758e8;font-weight:750;text-decoration:none}.local-note{margin-top:25px;border-top:1px solid #e8edf5;padding-top:18px;color:#8491a7;font-size:12px;line-height:1.55}@media(max-width:760px){body{padding:0;background:#fff}.identity-shell{min-height:100vh;border:0;border-radius:0;display:block}.brand-panel{min-height:190px;padding:30px}.brand-copy{margin:30px 0 0}.brand-copy h2{font-size:25px}.brand-copy p:last-child,.trust{display:none}.form-panel{padding:38px 28px}h1{font-size:29px}}
        """,
        "text/css; charset=utf-8");

    [HttpGet("register")]
    public IActionResult Register([FromQuery] string? returnUrl = null) =>
        HtmlForm("Create local account", "/account/register", returnUrl, register: true);

    [HttpPost("register")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(
        [FromForm] string email,
        [FromForm] string password,
        [FromForm] string? returnUrl = null)
    {
        var user = new DevelopmentUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email.Trim(),
            Email = email.Trim(),
            EmailConfirmed = true,
            TenantId = LocalIdentityContract.TenantId,
            SessionId = Guid.CreateVersion7()
        };
        IdentityResult created = await userManager.CreateAsync(user, password).ConfigureAwait(false);
        if (!created.Succeeded)
        {
            return HtmlForm(
                "Create local account",
                "/account/register",
                returnUrl,
                register: true,
                created.Errors.Select(error => error.Description));
        }

        await provisioner.ProvisionAsync(user, HttpContext.RequestAborted).ConfigureAwait(false);
        await signInManager.SignInAsync(user, isPersistent: true).ConfigureAwait(false);
        return SafeLocalRedirect(returnUrl);
    }

    [HttpGet("sign-in")]
    public IActionResult SignIn([FromQuery] string? returnUrl = null) =>
        HtmlForm("Sign in locally", "/account/sign-in", returnUrl, register: false);

    [HttpPost("sign-in")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SignIn(
        [FromForm] string email,
        [FromForm] string password,
        [FromForm] string? returnUrl = null)
    {
        DevelopmentUser? user = await userManager.FindByEmailAsync(email.Trim()).ConfigureAwait(false);
        if (user is null)
        {
            return HtmlForm("Sign in locally", "/account/sign-in", returnUrl, false,
                ["The email or password is invalid."]);
        }

        Microsoft.AspNetCore.Identity.SignInResult result = await signInManager
            .CheckPasswordSignInAsync(user, password, lockoutOnFailure: true)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return HtmlForm("Sign in locally", "/account/sign-in", returnUrl, false,
                ["The email or password is invalid."]);
        }

        user.SessionId = Guid.CreateVersion7();
        IdentityResult updated = await userManager.UpdateAsync(user).ConfigureAwait(false);
        if (!updated.Succeeded)
        {
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        await provisioner.ProvisionAsync(user, HttpContext.RequestAborted).ConfigureAwait(false);
        await signInManager.SignInAsync(user, isPersistent: true).ConfigureAwait(false);
        return SafeLocalRedirect(returnUrl);
    }

    private ContentResult HtmlForm(
        string title,
        string action,
        string? returnUrl,
        bool register,
        IEnumerable<string>? errors = null)
    {
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(HttpContext);
        string safeReturn = SafeReturnUrl(returnUrl);
        string encodedReturn = HtmlEncoder.Default.Encode(safeReturn);
        string encodedErrors = string.Join(
            string.Empty,
            (errors ?? []).Select(error => $"<li>{HtmlEncoder.Default.Encode(error)}</li>"));
        string alternate = register
            ? $"Already have an account? <a href=\"/account/sign-in?returnUrl={WebUtility.UrlEncode(safeReturn)}\">Sign in</a>"
            : $"New to YO4X? <a href=\"/account/register?returnUrl={WebUtility.UrlEncode(safeReturn)}\">Create account</a>";
        string errorBlock = encodedErrors.Length == 0
            ? string.Empty
            : $"<ul class=\"errors\" role=\"alert\">{encodedErrors}</ul>";
        string heading = register ? "Create your YO4X account" : "Welcome back";
        string introduction = register
            ? "Set up your secure workspace to manage strategies, deployments, and broker connections."
            : "Sign in to continue to your strategy operations workspace.";
        string passwordHint = register
            ? "<span class=\"hint\">Use 12+ characters with upper and lowercase letters, a number, and a symbol.</span>"
            : string.Empty;
        return Content(
            $$"""
            <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>{{HtmlEncoder.Default.Encode(title)}} · YO4X</title><link rel="stylesheet" href="/account/theme.css"></head><body>
            <main class="identity-shell"><section class="brand-panel"><div class="brand">Yo4x <span>Trading Cloud</span></div>
            <div class="brand-copy"><p>Strategy operations</p><h2>Trade automation,<br>under your control.</h2><p>One organized workspace for strategy analysis, controlled deployments, and observable execution.</p></div>
            <div class="trust">Local development identity · Encrypted session · PKCE protected</div></section>
            <section class="form-panel"><p class="eyebrow">YO4X secure access</p><h1>{{heading}}</h1><p class="intro">{{introduction}}</p>
            {{errorBlock}}<form method="post" action="{{action}}">
            <input type="hidden" name="{{tokens.FormFieldName}}" value="{{HtmlEncoder.Default.Encode(tokens.RequestToken!)}}">
            <input type="hidden" name="returnUrl" value="{{encodedReturn}}">
            <label class="field">Email address<input name="email" type="text" inputmode="email" autocomplete="username" maxlength="320" placeholder="you@example.com" required></label>
            <label class="field">Password<input name="password" type="password" autocomplete="{{(register ? "new-password" : "current-password")}}" minlength="12" maxlength="128" placeholder="Enter your password" required>{{passwordHint}}</label>
            <button class="primary" type="submit">{{HtmlEncoder.Default.Encode(title)}}</button></form><p class="alternate">{{alternate}}</p>
            <p class="local-note">This sign-in service is enabled only for the local development build. Credentials stay inside the local identity boundary.</p>
            </section></main></body></html>
            """,
            "text/html; charset=utf-8");
    }

    private IActionResult SafeLocalRedirect(string? returnUrl) =>
        IsAuthorizationReturnUrl(returnUrl)
            ? LocalRedirect(ConsumeLoginPrompt(returnUrl!))
            : Redirect(LocalIdentityContract.FrontendOrigin + "/auth/sign-in");

    // `prompt=login` is a one-time instruction to show this credential form. Sending it back to
    // /connect/authorize after the form succeeds would challenge the new cookie again and create
    // an endless sign-in loop. Preserve the OIDC state/PKCE parameters while consuming only that
    // one instruction.
    private static string ConsumeLoginPrompt(string returnUrl)
    {
        int queryIndex = returnUrl.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex < 0)
        {
            return returnUrl;
        }

        IEnumerable<KeyValuePair<string, string?>> parameters = QueryHelpers
            .ParseQuery(returnUrl[(queryIndex + 1)..])
            .Where(parameter => !string.Equals(parameter.Key, "prompt", StringComparison.Ordinal))
            .SelectMany(parameter => parameter.Value.Select(value =>
                new KeyValuePair<string, string?>(parameter.Key, value)));
        return returnUrl[..queryIndex] + QueryString.Create(parameters);
    }

    private string SafeReturnUrl(string? returnUrl) =>
        IsAuthorizationReturnUrl(returnUrl)
            ? returnUrl!
            : string.Empty;

    private bool IsAuthorizationReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) &&
        Url.IsLocalUrl(returnUrl) &&
        (string.Equals(returnUrl, "/connect/authorize", StringComparison.Ordinal) ||
         returnUrl.StartsWith("/connect/authorize?", StringComparison.Ordinal));
}
