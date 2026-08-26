using YO4X.BuildingBlocks;

namespace YO4X.ControlPlane.Application;

public sealed class UnavailableFrontendProjectionApplication : IFrontendProjectionApplication
{
    public Task<StrategyCatalogPage> GetStrategyCatalogAsync(UserActor actor, StrategyCatalogQuery query, CancellationToken cancellationToken) =>
        Unavailable<StrategyCatalogPage>();

    public Task<StrategyDetailView?> GetStrategyDetailAsync(UserActor actor, Guid strategyId, CancellationToken cancellationToken) =>
        Unavailable<StrategyDetailView?>();

    public Task<IReadOnlyList<StrategyReviewView>> GetStrategyReviewsAsync(UserActor actor, Guid strategyId, int limit, CancellationToken cancellationToken) =>
        Unavailable<IReadOnlyList<StrategyReviewView>>();

    public Task<IReadOnlyList<BotView>> GetBotsAsync(UserActor actor, CancellationToken cancellationToken) =>
        Unavailable<IReadOnlyList<BotView>>();

    public Task<BotView?> GetBotAsync(UserActor actor, Guid botId, CancellationToken cancellationToken) =>
        Unavailable<BotView?>();

    public Task<BotView> CreateBotAsync(UserActor actor, CreateBot request, CancellationToken cancellationToken) =>
        Unavailable<BotView>();

    public Task<BotView?> SetBotStatusAsync(UserActor actor, Guid botId, BotStatusChange request, CancellationToken cancellationToken) =>
        Unavailable<BotView?>();

    public Task<BotUptimeProjection> GetBotUptimeAsync(UserActor actor, int days, CancellationToken cancellationToken) =>
        Unavailable<BotUptimeProjection>();

    public Task<BotSettingsView?> GetBotSettingsAsync(UserActor actor, Guid botId, CancellationToken cancellationToken) =>
        Unavailable<BotSettingsView?>();

    public Task<bool> UpdateBotSettingsAsync(UserActor actor, Guid botId, UpdateBotSettings request, CancellationToken cancellationToken) =>
        Unavailable<bool>();

    public Task<IReadOnlyList<BrokerSymbolView>> GetBrokerSymbolsAsync(UserActor actor, string? server, string? query, CancellationToken cancellationToken) =>
        Unavailable<IReadOnlyList<BrokerSymbolView>>();

    public Task<StrategyInputsView?> GetStrategyInputsAsync(UserActor actor, Guid strategyId, CancellationToken cancellationToken) =>
        Unavailable<StrategyInputsView?>();

    public Task<IReadOnlyList<BacktestView>> GetBacktestsAsync(UserActor actor, CancellationToken cancellationToken) =>
        Unavailable<IReadOnlyList<BacktestView>>();

    public Task<BacktestDetailView?> GetBacktestDetailAsync(UserActor actor, Guid backtestId, CancellationToken cancellationToken) =>
        Unavailable<BacktestDetailView?>();

    public Task<BacktestView> CreateBacktestAsync(UserActor actor, CreateBacktest request, CancellationToken cancellationToken) =>
        Unavailable<BacktestView>();

    public Task<IReadOnlyList<CloudPlanView>> GetCloudPlansAsync(UserActor actor, CancellationToken cancellationToken) =>
        Unavailable<IReadOnlyList<CloudPlanView>>();

    public Task<IReadOnlyList<CloudRunnerView>> GetCloudRunnersAsync(UserActor actor, CancellationToken cancellationToken) =>
        Unavailable<IReadOnlyList<CloudRunnerView>>();

    public Task<IReadOnlyList<CloudRegionView>> GetCloudRegionsAsync(UserActor actor, CancellationToken cancellationToken) =>
        Unavailable<IReadOnlyList<CloudRegionView>>();

    public Task<JournalPage> GetJournalAsync(UserActor actor, JournalQuery query, CancellationToken cancellationToken) =>
        Unavailable<JournalPage>();

    public Task<DashboardSummaryView> GetDashboardSummaryAsync(UserActor actor, CancellationToken cancellationToken) =>
        Unavailable<DashboardSummaryView>();

    public Task<BridgeStatusView> GetBridgeStatusAsync(UserActor actor, CancellationToken cancellationToken) =>
        Unavailable<BridgeStatusView>();

    private static Task<T> Unavailable<T>() => Task.FromException<T>(new BackendCapabilityUnavailableException("control_plane_postgres"));
}
