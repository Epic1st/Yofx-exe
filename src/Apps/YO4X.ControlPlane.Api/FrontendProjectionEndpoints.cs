using System.Security.Claims;
using YO4X.Api;
using YO4X.ControlPlane.Application;
using YO4X.Identity;

namespace YO4X.ControlPlane.Api;

public static class FrontendProjectionEndpoints
{
    private const int DefaultCatalogPage = 1;
    private const int DefaultCatalogPageSize = 24;
    private const int DefaultJournalLimit = 50;
    private const int DefaultUptimeDays = 28;
    private const int DefaultReviewLimit = 20;

    public static RouteGroupBuilder MapFrontendProjections(this RouteGroupBuilder user)
    {
        user.MapGet("/catalog/strategies", async (
            int? page,
            int? pageSize,
            string? category,
            string? symbol,
            string? query,
            string? sort,
            HttpContext context,
            IFrontendProjectionApplication application,
            CancellationToken cancellationToken) =>
            Results.Ok(await application.GetStrategyCatalogAsync(
                ToUserActor(context.User),
                new StrategyCatalogQuery(
                    page ?? DefaultCatalogPage,
                    pageSize ?? DefaultCatalogPageSize,
                    category,
                    symbol,
                    query,
                    sort),
                cancellationToken)));

        user.MapGet("/catalog/strategies/{strategyId:guid}", async (
            Guid strategyId,
            HttpContext context,
            IFrontendProjectionApplication application,
            CancellationToken cancellationToken) =>
        {
            StrategyDetailView? detail = await application.GetStrategyDetailAsync(
                ToUserActor(context.User), strategyId, cancellationToken);
            return detail is null
                ? ApiProblems.Create(context, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "The resource was not found.")
                : Results.Ok(detail);
        });

        user.MapGet("/catalog/strategies/{strategyId:guid}/reviews", async (
            Guid strategyId,
            int? limit,
            HttpContext context,
            IFrontendProjectionApplication application,
            CancellationToken cancellationToken) =>
            Results.Ok(await application.GetStrategyReviewsAsync(
                ToUserActor(context.User),
                strategyId,
                limit ?? DefaultReviewLimit,
                cancellationToken)));

        user.MapGet("/catalog/strategies/{strategyId:guid}/inputs", async (
            Guid strategyId,
            HttpContext context,
            IFrontendProjectionApplication application,
            CancellationToken cancellationToken) =>
        {
            StrategyInputsView? inputs = await application.GetStrategyInputsAsync(
                ToUserActor(context.User), strategyId, cancellationToken);
            return inputs is null
                ? ApiProblems.Create(context, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "The resource was not found.")
                : Results.Ok(inputs);
        });

        user.MapGet("/bots/uptime", async (
            int? days,
            HttpContext context,
            IFrontendProjectionApplication application,
            CancellationToken cancellationToken) =>
            Results.Ok(await application.GetBotUptimeAsync(
                ToUserActor(context.User),
                days ?? DefaultUptimeDays,
                cancellationToken)));

        user.MapGet("/bots", async (
            HttpContext context,
            IFrontendProjectionApplication application,
            CancellationToken cancellationToken) =>
            Results.Ok(await application.GetBotsAsync(
                ToUserActor(context.User),
                cancellationToken)));

        user.MapGet("/bots/{botId:guid}", async (
            Guid botId,
            HttpContext context,
            IFrontendProjectionApplication application,
            CancellationToken cancellationToken) =>
        {
            BotView? bot = await application.GetBotAsync(
                ToUserActor(context.User), botId, cancellationToken);
            return bot is null
                ? ApiProblems.Create(context, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "The resource was not found.")
                : Results.Ok(bot);
        });

        user.MapGet("/bots/{botId:guid}/settings", async (
            Guid botId,
            HttpContext context,
            IFrontendProjectionApplication application,
            CancellationToken cancellationToken) =>
        {
            BotSettingsView? settings = await application.GetBotSettingsAsync(
                ToUserActor(context.User), botId, cancellationToken);
            return settings is null
                ? ApiProblems.Create(context, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "The resource was not found.")
                : Results.Ok(settings);
        });

        // A settings save replaces the whole set, so it is a PUT. The application
        // reports a bot that is not the caller's by returning false rather than by
        // faulting, which is what keeps an identifier that belongs to somebody else a
        // missing resource instead of a server error. A value the strategy or the
        // column refuses surfaces as the API foundation's 422 problem, carrying the
        // code that says which part of the request was refused.
        user.MapPut("/bots/{botId:guid}/settings", async (
            Guid botId,
            UpdateBotSettings request,
            HttpContext context,
            IFrontendProjectionApplication application,
            CancellationToken cancellationToken) =>
        {
            bool saved = await application.UpdateBotSettingsAsync(
                ToUserActor(context.User), botId, request, cancellationToken);
            return saved
                ? Results.NoContent()
                : ApiProblems.Create(context, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "The resource was not found.");
        });

        // The instruments a broker server actually offers, so the settings form picks a
        // symbol from the broker's own list instead of accepting typed text.
        user.MapGet("/broker-symbols", async (
            string? server,
            string? query,
            HttpContext context,
            IFrontendProjectionApplication application,
            CancellationToken cancellationToken) =>
            Results.Ok(await application.GetBrokerSymbolsAsync(
                ToUserActor(context.User),
                server,
                query,
                cancellationToken)));

        user.MapPost("/bots", async (
            CreateBot request,
            HttpContext context,
            IFrontendProjectionApplication application,
            CancellationToken cancellationToken) =>
        {
            BotView view = await application.CreateBotAsync(
                ToUserActor(context.User), request, cancellationToken);
            return Results.Created($"/v1/bots/{view.Id:D}", view);
        });

        user.MapPost("/bots/{botId:guid}/status", async (
            Guid botId,
            BotStatusChange request,
            HttpContext context,
            IFrontendProjectionApplication application,
            CancellationToken cancellationToken) =>
        {
            BotView? view = await application.SetBotStatusAsync(
                ToUserActor(context.User), botId, request, cancellationToken);
            return view is null
                ? ApiProblems.Create(context, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "The resource was not found.")
                : Results.Ok(view);
        });

        user.MapGet("/backtests", async (
            HttpContext context,
            IFrontendProjectionApplication application,
            CancellationToken cancellationToken) =>
            Results.Ok(await application.GetBacktestsAsync(
                ToUserActor(context.User),
                cancellationToken)));

        user.MapGet("/backtests/{backtestId:guid}", async (
            Guid backtestId,
            HttpContext context,
            IFrontendProjectionApplication application,
            CancellationToken cancellationToken) =>
        {
            BacktestDetailView? detail = await application.GetBacktestDetailAsync(
                ToUserActor(context.User), backtestId, cancellationToken);
            return detail is null
                ? ApiProblems.Create(context, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "The resource was not found.")
                : Results.Ok(detail);
        });

        user.MapPost("/backtests", async (
            CreateBacktest request,
            HttpContext context,
            IFrontendProjectionApplication application,
            CancellationToken cancellationToken) =>
        {
            BacktestView view;
            try
            {
                view = await application.CreateBacktestAsync(
                    ToUserActor(context.User), request, cancellationToken);
            }
            catch (BacktestInputValidationException rejected)
            {
                // Submitted inputs are checked against the strategy's own declarations.
                // Every offending field is reported; no value is coerced to make the
                // request succeed.
                return ApiProblems.Create(
                    context,
                    StatusCodes.Status422UnprocessableEntity,
                    rejected.Code,
                    rejected.Message,
                    rejected.Errors
                        .Select(error => new ApiValidationError(
                            "inputs/" + error.Name,
                            error.Code,
                            error.Message))
                        .ToList());
            }

            return Results.Created($"/v1/backtests/{view.Id:D}", view);
        });

        user.MapGet("/cloud/plans", async (
            HttpContext context,
            IFrontendProjectionApplication application,
            CancellationToken cancellationToken) =>
            Results.Ok(await application.GetCloudPlansAsync(
                ToUserActor(context.User),
                cancellationToken)));

        user.MapGet("/cloud/runners", async (
            HttpContext context,
            IFrontendProjectionApplication application,
            CancellationToken cancellationToken) =>
            Results.Ok(await application.GetCloudRunnersAsync(
                ToUserActor(context.User),
                cancellationToken)));

        user.MapGet("/cloud/regions", async (
            HttpContext context,
            IFrontendProjectionApplication application,
            CancellationToken cancellationToken) =>
            Results.Ok(await application.GetCloudRegionsAsync(
                ToUserActor(context.User),
                cancellationToken)));

        user.MapGet("/journal", async (
            int? limit,
            Guid? before,
            DateTimeOffset? from,
            DateTimeOffset? to,
            HttpContext context,
            IFrontendProjectionApplication application,
            CancellationToken cancellationToken) =>
            Results.Ok(await application.GetJournalAsync(
                ToUserActor(context.User),
                new JournalQuery(limit ?? DefaultJournalLimit, before, from, to),
                cancellationToken)));

        user.MapGet("/dashboard/summary", async (
            HttpContext context,
            IFrontendProjectionApplication application,
            CancellationToken cancellationToken) =>
            Results.Ok(await application.GetDashboardSummaryAsync(
                ToUserActor(context.User),
                cancellationToken)));

        user.MapGet("/bridge/status", async (
            HttpContext context,
            IFrontendProjectionApplication application,
            CancellationToken cancellationToken) =>
            Results.Ok(await application.GetBridgeStatusAsync(
                ToUserActor(context.User),
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
