using YO4X.Runtime.Contracts;

namespace YO4X.GatewayHost;

public sealed class GatewayHostRuntimeStatus
{
    private GatewayHostStartupSnapshot startup;

    internal GatewayHostRuntimeStatus(bool oneShotEnabled)
    {
        startup = oneShotEnabled
            ? Snapshot(
                "configured",
                "gateway_host_one_shot_configured",
                isSuccessful: false)
            : Snapshot(
                "disabled",
                "gateway_host_one_shot_disabled",
                isSuccessful: true);
    }

    public PublicRuntimeHealth Live { get; } = new(
        RuntimeContractVersions.PublicHealthV1,
        "gateway-host",
        "live",
        "gateway_host_process_live");

    public PublicRuntimeHealth Startup => ReadStartup().Health;

    public PublicRuntimeHealth Ready { get; } = new(
        RuntimeContractVersions.PublicHealthV1,
        "gateway-host",
        "not-ready",
        "gateway_host_proof_only_not_mutation_ready");

    internal GatewayHostStartupSnapshot ReadStartup() => Volatile.Read(ref startup);

    internal void MarkRunning() =>
        Set("running", "gateway_host_one_shot_running", isSuccessful: false);

    internal void MarkNoSubmissionRecorded() =>
        Set(
            "completed",
            "gateway_host_one_shot_no_submission_recorded",
            isSuccessful: true);

    internal void MarkReconciliationCompleted() =>
        Set(
            "completed",
            "gateway_host_one_shot_reconciliation_completed",
            isSuccessful: true);

    internal void MarkReconciliationPending() =>
        Set(
            "degraded",
            "gateway_host_one_shot_reconciliation_pending",
            isSuccessful: false);

    internal void MarkFailed() =>
        Set("failed", "gateway_host_one_shot_failed", isSuccessful: false);

    private void Set(string status, string code, bool isSuccessful) =>
        Volatile.Write(ref startup, Snapshot(status, code, isSuccessful));

    private static GatewayHostStartupSnapshot Snapshot(
        string status,
        string code,
        bool isSuccessful) =>
        new(Health(status, code), isSuccessful);

    private static PublicRuntimeHealth Health(string status, string code) => new(
        RuntimeContractVersions.PublicHealthV1,
        "gateway-host",
        status,
        code);
}

internal sealed record GatewayHostStartupSnapshot(
    PublicRuntimeHealth Health,
    bool IsSuccessful);
