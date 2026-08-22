using YO4X.Commands;

namespace YO4X.Admin.Postgres;

public static class AdminPermissions
{
    public const string ReadOwnSession = "admin.session.self.read";
    public const string ReadApprovals = "admin.approval.read";
    public const string DecideApprovals = "admin.approval.decide";
    public const string ReadCommands = "admin.command.read";
    public const string CancelCommands = "admin.command.cancel";
    public const string RequestCompensation = "admin.command.compensation.request";
    public const string ReadDeployments = "admin.deployment.read";
    public const string CloseOnly = "admin.deployment.close_only";
    public const string StopAfterFlat = "admin.deployment.stop_after_flat";
    public const string RevokeLease = "admin.deployment.lease.revoke";
    public const string ReplaceWorker = "admin.deployment.worker.replace";

    public static string ForContainment(CommandType type) => type switch
    {
        CommandType.CloseOnly => CloseOnly,
        CommandType.StopAfterFlat => StopAfterFlat,
        CommandType.RevokeLease => RevokeLease,
        CommandType.ReplaceWorker => ReplaceWorker,
        _ => throw new ArgumentOutOfRangeException(
            nameof(type),
            type,
            "The command is not an allowlisted deployment-containment operation.")
    };
}
