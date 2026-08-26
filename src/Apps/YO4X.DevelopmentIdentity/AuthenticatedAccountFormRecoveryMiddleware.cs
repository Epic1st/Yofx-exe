namespace YO4X.DevelopmentIdentity;

public sealed class AuthenticatedAccountFormRecoveryMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        await next(context).ConfigureAwait(false);

        if (ShouldRecover(context))
        {
            context.Response.Redirect(LocalIdentityContract.FrontendOrigin + "/auth/sign-in");
        }
    }

    public static bool ShouldRecover(HttpContext context) =>
        !context.Response.HasStarted &&
        context.Response.StatusCode == StatusCodes.Status400BadRequest &&
        HttpMethods.IsPost(context.Request.Method) &&
        context.User.Identity?.IsAuthenticated == true &&
        (context.Request.Path == "/account/register" ||
         context.Request.Path == "/account/sign-in");
}
