namespace YO4X.StrategyHost;

public sealed record StrategyHostHealth(int ContractVersion, string Role, string Status, string Code);

public sealed class StrategyHostRuntimeStatus
{
    public StrategyHostHealth Live { get; } = new(
        1,
        "strategy-host",
        "live",
        "strategy_host_process_live");

    public StrategyHostHealth Startup { get; } = new(
        1,
        "strategy-host",
        "started",
        "strategy_host_startup_complete");

    public StrategyHostHealth Ready { get; } = new(
        1,
        "strategy-host",
        "not-ready",
        "strategy_package_not_loaded");
}
