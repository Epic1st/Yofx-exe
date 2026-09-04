namespace YO4X.ControlPlane.Application;

/// <summary>
/// Coordinates a real bot lifecycle. Projection storage is deliberately not an execution
/// authority: a bot may be reported as running only after this boundary has acknowledged it.
/// </summary>
public interface IBotExecutionCoordinator
{
    Task<BotView?> ChangeStatusAsync(
        UserActor actor,
        Guid botId,
        BotStatusChange request,
        CancellationToken cancellationToken);
}

public sealed class UnavailableBotExecutionCoordinator : IBotExecutionCoordinator
{
    public Task<BotView?> ChangeStatusAsync(
        UserActor actor,
        Guid botId,
        BotStatusChange request,
        CancellationToken cancellationToken) =>
        Task.FromException<BotView?>(
            new YO4X.BuildingBlocks.BackendCapabilityUnavailableException(
                "local-bot-execution-coordinator"));
}
