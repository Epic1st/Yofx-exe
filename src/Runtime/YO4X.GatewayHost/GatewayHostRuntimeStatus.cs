using YO4X.Runtime.Contracts;

namespace YO4X.GatewayHost;

public sealed class GatewayHostRuntimeStatus
{
    public PublicRuntimeHealth Live { get; } = new(
        RuntimeContractVersions.PublicHealthV1,
        "gateway-host",
        "live",
        "gateway_host_process_live");

    public PublicRuntimeHealth Startup { get; } = new(
        RuntimeContractVersions.PublicHealthV1,
        "gateway-host",
        "started",
        "gateway_host_startup_complete");

    public PublicRuntimeHealth Ready { get; } = new(
        RuntimeContractVersions.PublicHealthV1,
        "gateway-host",
        "not-ready",
        "gateway_assignment_not_approved");
}
