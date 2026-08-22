using System.Text.Json;
using System.Text.Json.Nodes;
using YO4X.Admin.Application;
using YO4X.Approvals;
using YO4X.BuildingBlocks;
using YO4X.Commands;
using YO4X.Policy;

namespace YO4X.Admin.Postgres;

internal static class AdminStorageValues
{
    public static string ToStorageValue(this CommandType value) => value switch
    {
        CommandType.RequestUserReauthentication => "request_user_reauthentication",
        CommandType.DisableCloudUse => "disable_cloud_use",
        CommandType.DeleteCredentialReference => "delete_credential_reference",
        CommandType.CloseOnly => "close_only",
        CommandType.StopAfterFlat => "stop_after_flat",
        CommandType.RevokeLease => "revoke_lease",
        CommandType.ReplaceWorker => "replace_worker",
        CommandType.BlockNewExposure => "block_new_exposure",
        CommandType.BlockNewDeployments => "block_new_deployments",
        CommandType.QuarantineGatewayArtifact => "quarantine_gateway_artifact",
        CommandType.ExtendContainment => "extend_containment",
        CommandType.ReleaseContainment => "release_containment",
        CommandType.PromoteGatewayArtifact => "promote_gateway_artifact",
        CommandType.RollbackGatewayRelease => "rollback_gateway_release",
        CommandType.RevokeGatewayArtifact => "revoke_gateway_artifact",
        CommandType.RevokeAccessAssignment => "revoke_access_assignment",
        CommandType.RevokeAdminSession => "revoke_admin_session",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown command type.")
    };

    public static CommandType ParseCommandType(string value) => value switch
    {
        "request_user_reauthentication" => CommandType.RequestUserReauthentication,
        "disable_cloud_use" => CommandType.DisableCloudUse,
        "delete_credential_reference" => CommandType.DeleteCredentialReference,
        "close_only" => CommandType.CloseOnly,
        "stop_after_flat" => CommandType.StopAfterFlat,
        "revoke_lease" => CommandType.RevokeLease,
        "replace_worker" => CommandType.ReplaceWorker,
        "block_new_exposure" => CommandType.BlockNewExposure,
        "block_new_deployments" => CommandType.BlockNewDeployments,
        "quarantine_gateway_artifact" => CommandType.QuarantineGatewayArtifact,
        "extend_containment" => CommandType.ExtendContainment,
        "release_containment" => CommandType.ReleaseContainment,
        "promote_gateway_artifact" => CommandType.PromoteGatewayArtifact,
        "rollback_gateway_release" => CommandType.RollbackGatewayRelease,
        "revoke_gateway_artifact" => CommandType.RevokeGatewayArtifact,
        "revoke_access_assignment" => CommandType.RevokeAccessAssignment,
        "revoke_admin_session" => CommandType.RevokeAdminSession,
        _ => throw InvalidStorage("command type", value)
    };

    public static CommandStatus ParseCommandStatus(string value) => value switch
    {
        "requested" => CommandStatus.Requested,
        "policy_checking" => CommandStatus.PolicyChecking,
        "waiting_approval" => CommandStatus.WaitingApproval,
        "approved" => CommandStatus.Approved,
        "scheduled" => CommandStatus.Scheduled,
        "dispatching" => CommandStatus.Dispatching,
        "propagating" => CommandStatus.Propagating,
        "reconciling" => CommandStatus.Reconciling,
        "succeeded" => CommandStatus.Succeeded,
        "cancelled" => CommandStatus.Cancelled,
        "rejected" => CommandStatus.Rejected,
        "expired" => CommandStatus.Expired,
        "partial" => CommandStatus.Partial,
        "failed" => CommandStatus.Failed,
        "unknown" => CommandStatus.Unknown,
        "compensation_requested" => CommandStatus.CompensationRequested,
        "compensating" => CommandStatus.Compensating,
        "compensated" => CommandStatus.Compensated,
        "compensation_partial" => CommandStatus.CompensationPartial,
        "compensation_failed" => CommandStatus.CompensationFailed,
        _ => throw InvalidStorage("command status", value)
    };

    public static ApprovalStatus ParseApprovalStatus(string value, DateTimeOffset expiresAt, DateTimeOffset now) =>
        value switch
        {
            "pending" when expiresAt <= now => ApprovalStatus.Expired,
            "pending" => ApprovalStatus.Pending,
            "approved" => ApprovalStatus.Approved,
            "rejected" => ApprovalStatus.Rejected,
            "expired" => ApprovalStatus.Expired,
            "invalidated" => ApprovalStatus.Invalidated,
            _ => throw InvalidStorage("approval status", value)
        };

    public static TargetTerminalProof ParseTargetProof(string value) => value switch
    {
        "applied" => TargetTerminalProof.Applied,
        "reconciled" => TargetTerminalProof.Reconciled,
        _ => throw InvalidStorage("target terminal proof", value)
    };

    public static CommandTargetStatus ParseTargetStatus(string value) => value switch
    {
        "pending_dispatch" => CommandTargetStatus.PendingDispatch,
        "dispatched" => CommandTargetStatus.Dispatched,
        "delivered" => CommandTargetStatus.Delivered,
        "acknowledged" => CommandTargetStatus.Acknowledged,
        "applied" => CommandTargetStatus.Applied,
        "reconciling" => CommandTargetStatus.Reconciling,
        "reconciled" => CommandTargetStatus.Reconciled,
        "not_applicable" => CommandTargetStatus.NotApplicable,
        "unreachable" => CommandTargetStatus.Unreachable,
        "failed" => CommandTargetStatus.Failed,
        "unknown" => CommandTargetStatus.Unknown,
        _ => throw InvalidStorage("command target status", value)
    };

    public static string NormalizeEnvironment(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Trim().ToLowerInvariant();
        return normalized is "development" or "test" or "demo" or "pilot" or "production"
            ? normalized
            : throw new AdminAuthorizationDeniedException(
                "ADMIN_ENVIRONMENT_INVALID",
                "The admin environment claim is not allowlisted.");
    }

    public static PolicyVectorDocument ToDocument(this ExecutionSafetyPolicyVector vector)
    {
        ArgumentNullException.ThrowIfNull(vector);
        return new PolicyVectorDocument(
            vector.AllowNewDeployment,
            vector.AllowStrategySignals,
            vector.AllowExposureIncrease,
            vector.AllowExposureReduction,
            vector.AllowProtection,
            vector.AllowPendingOrderCancellation,
            vector.AllowEmergencyClose,
            vector.LeaseMode.ToString(),
            vector.EnumerateWorkerActions().Select(action => action.ToString()).ToArray(),
            vector.CredentialMode.ToString(),
            vector.PackageEligibility.ToString());
    }

    public static ExecutionSafetyPolicyVector ToVector(this PolicyVectorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        WorkerAction actions = WorkerAction.None;
        foreach (string action in document.WorkerActions)
        {
            actions |= Enum.Parse<WorkerAction>(action, ignoreCase: false);
        }

        return new ExecutionSafetyPolicyVector(
            document.AllowNewDeployment,
            document.AllowStrategySignals,
            document.AllowExposureIncrease,
            document.AllowExposureReduction,
            document.AllowProtection,
            document.AllowPendingOrderCancellation,
            document.AllowEmergencyClose,
            Enum.Parse<LeaseMode>(document.LeaseMode, ignoreCase: false),
            actions,
            Enum.Parse<CredentialMode>(document.CredentialMode, ignoreCase: false),
            Enum.Parse<PackageEligibility>(document.PackageEligibility, ignoreCase: false));
    }

    public static PolicyVectorDocument ParsePolicyDocument(string json) =>
        JsonSerializer.Deserialize<PolicyVectorDocument>(json, WebJson.Options)
        ?? throw InvalidStorage("policy vector", json);

    public static string CanonicalizeJson(string json)
    {
        JsonNode? node = JsonNode.Parse(json);
        return CanonicalJson.Serialize(node);
    }

    private static InvalidOperationException InvalidStorage(string field, string value) =>
        new($"PostgreSQL contains an unknown {field} value: '{value}'.");
}

internal sealed record PolicyVectorDocument(
    bool AllowNewDeployment,
    bool AllowStrategySignals,
    bool AllowExposureIncrease,
    bool AllowExposureReduction,
    bool AllowProtection,
    bool AllowPendingOrderCancellation,
    bool AllowEmergencyClose,
    string LeaseMode,
    IReadOnlyList<string> WorkerActions,
    string CredentialMode,
    string PackageEligibility);

internal static class WebJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
