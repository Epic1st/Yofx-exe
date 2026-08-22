using YO4X.Runtime.Contracts;

namespace YO4X.Supervisor;

public sealed class SupervisorRuntimeStatus
{
    public PublicRuntimeHealth Live { get; } = new(
        RuntimeContractVersions.PublicHealthV1,
        "supervisor",
        "live",
        "supervisor_process_live");

    public PublicRuntimeHealth Startup { get; } = new(
        RuntimeContractVersions.PublicHealthV1,
        "supervisor",
        "started",
        "supervisor_startup_complete");

    public PublicRuntimeHealth Ready { get; } = new(
        RuntimeContractVersions.PublicHealthV1,
        "supervisor",
        "not-ready",
        "runtime_component_evidence_incomplete");
}
