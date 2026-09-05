using YO4X.ControlPlane.Application;

namespace YO4X.ControlPlane.Api;

/// <summary>
/// Records a bot status change in PostgreSQL. It does not start <c>mt5api.dll</c>,
/// unpack a package, or hold strategy state in this process.
/// </summary>
internal sealed class ProjectionBotExecutionCoordinator(
    IFrontendProjectionApplication projections) : IBotExecutionCoordinator
{
    public async Task<BotView?> ChangeStatusAsync(
        UserActor actor,
        Guid botId,
        BotStatusChange request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        BotView? bot = await projections.GetBotAsync(actor, botId, cancellationToken)
            .ConfigureAwait(false);
        if (bot is null)
            return null;
        if (bot.Host == BotHost.Local)
        {
            throw new YO4X.BuildingBlocks.DomainException(
                "LOCAL_STATUS_REQUIRES_RUNTIME_EVIDENCE",
                "Local bot status is controlled by an authorized desktop heartbeat.");
        }
        return await projections.SetBotStatusAsync(actor, botId, request, cancellationToken)
            .ConfigureAwait(false);
    }
}
