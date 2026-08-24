using System.Text.Json;

namespace YO4X.Runtime.Contracts;

/// <summary>
/// A reconciliation-only observation challenge. It is bound to one committed
/// invocation attempt and cannot authorize a first broker mutation.
/// </summary>
public sealed class UserOperationReconciliationRequestedV3
{
    private readonly bool reconciliationOnly;
    private readonly int schemaVersion;

    private static readonly string[] CanonicalProperties =
    [
        "attemptId",
        "challengeCapabilityExpiresAtUtc",
        "challengeId",
        "challengeIssuedAtUtc",
        "challengeMessageId",
        "challengeResultCapability",
        "commandSha256",
        "dispatchPolicySnapshotSha256",
        "dispatchTargetBindingSha256",
        "fenceGeneration",
        "gatewayStartReceiptId",
        "operationId",
        "operationType",
        "originalDispatchMessageId",
        "providerCallAuthorizationReceiptId",
        "reconciliationOnly",
        "requestedTargetState",
        "routeDeploymentId",
        "schemaVersion",
        "submittedResourceVersion",
        "targetId",
        "targetType",
        "tenantId",
        "workerAssignmentId",
        "workerInstanceId"
    ];

    private UserOperationReconciliationRequestedV3(
        Guid attemptId,
        Guid challengeId,
        Guid challengeMessageId,
        Guid operationId,
        Guid originalDispatchMessageId,
        Guid tenantId,
        string operationType,
        string targetType,
        Guid targetId,
        long submittedResourceVersion,
        string requestedTargetState,
        string commandSha256,
        string dispatchTargetBindingSha256,
        string dispatchPolicySnapshotSha256,
        Guid routeDeploymentId,
        long fenceGeneration,
        Guid workerAssignmentId,
        Guid workerInstanceId,
        Guid gatewayStartReceiptId,
        Guid providerCallAuthorizationReceiptId,
        DateTimeOffset challengeIssuedAtUtc,
        DateTimeOffset challengeCapabilityExpiresAtUtc,
        UserOperationBearer challengeResultCapability)
    {
        schemaVersion = UserOperationProtocolVersions.ReconciliationRequestedV3;
        reconciliationOnly = true;
        AttemptId = attemptId;
        ChallengeId = challengeId;
        ChallengeMessageId = challengeMessageId;
        OperationId = operationId;
        OriginalDispatchMessageId = originalDispatchMessageId;
        TenantId = tenantId;
        OperationType = operationType;
        TargetType = targetType;
        TargetId = targetId;
        SubmittedResourceVersion = submittedResourceVersion;
        RequestedTargetState = requestedTargetState;
        CommandSha256 = commandSha256;
        DispatchTargetBindingSha256 = dispatchTargetBindingSha256;
        DispatchPolicySnapshotSha256 = dispatchPolicySnapshotSha256;
        RouteDeploymentId = routeDeploymentId;
        FenceGeneration = fenceGeneration;
        WorkerAssignmentId = workerAssignmentId;
        WorkerInstanceId = workerInstanceId;
        GatewayStartReceiptId = gatewayStartReceiptId;
        ProviderCallAuthorizationReceiptId = providerCallAuthorizationReceiptId;
        ChallengeIssuedAtUtc = challengeIssuedAtUtc;
        ChallengeCapabilityExpiresAtUtc = challengeCapabilityExpiresAtUtc;
        ChallengeResultCapability = challengeResultCapability;
    }

    public int SchemaVersion => schemaVersion;

    public const string MessageType = "yo4x.user-operation.reconciliation-requested.v3";

    public Guid AttemptId { get; }

    public Guid ChallengeId { get; }

    public Guid ChallengeMessageId { get; }

    public Guid OperationId { get; }

    public Guid OriginalDispatchMessageId { get; }

    public Guid TenantId { get; }

    public string OperationType { get; }

    public string TargetType { get; }

    public Guid TargetId { get; }

    public long SubmittedResourceVersion { get; }

    public string RequestedTargetState { get; }

    public string CommandSha256 { get; }

    public string DispatchTargetBindingSha256 { get; }

    public string DispatchPolicySnapshotSha256 { get; }

    public Guid RouteDeploymentId { get; }

    public long FenceGeneration { get; }

    public Guid WorkerAssignmentId { get; }

    public Guid WorkerInstanceId { get; }

    /// <summary>
    /// The committed start receipt that makes this an observation challenge,
    /// rather than authority for a first invocation.
    /// </summary>
    public Guid GatewayStartReceiptId { get; }

    /// <summary>The committed point-of-no-return receipt for the provider call.</summary>
    public Guid ProviderCallAuthorizationReceiptId { get; }

    public DateTimeOffset ChallengeIssuedAtUtc { get; }

    /// <summary>Exclusive database-clock expiry for first acceptance.</summary>
    public DateTimeOffset ChallengeCapabilityExpiresAtUtc { get; }

    public bool ReconciliationOnly => reconciliationOnly;

    public UserOperationBearer ChallengeResultCapability { get; }

    public static UserOperationReconciliationRequestedV3 Create(
        Guid attemptId,
        Guid challengeId,
        Guid challengeMessageId,
        Guid operationId,
        Guid originalDispatchMessageId,
        Guid tenantId,
        string operationType,
        string targetType,
        Guid targetId,
        long submittedResourceVersion,
        string requestedTargetState,
        string commandSha256,
        string dispatchTargetBindingSha256,
        string dispatchPolicySnapshotSha256,
        Guid routeDeploymentId,
        long fenceGeneration,
        Guid workerAssignmentId,
        Guid workerInstanceId,
        Guid gatewayStartReceiptId,
        Guid providerCallAuthorizationReceiptId,
        DateTimeOffset challengeIssuedAtUtc,
        DateTimeOffset challengeCapabilityExpiresAtUtc,
        UserOperationBearer challengeResultCapability)
    {
        UserOperationContractValidation.RequireIdentifier(attemptId, nameof(attemptId));
        UserOperationContractValidation.RequireIdentifier(challengeId, nameof(challengeId));
        UserOperationContractValidation.RequireIdentifier(challengeMessageId, nameof(challengeMessageId));
        UserOperationContractValidation.RequireIdentifier(operationId, nameof(operationId));
        UserOperationContractValidation.RequireIdentifier(
            originalDispatchMessageId,
            nameof(originalDispatchMessageId));
        UserOperationContractValidation.RequireIdentifier(tenantId, nameof(tenantId));
        UserOperationContractValidation.RequireIdentifier(gatewayStartReceiptId, nameof(gatewayStartReceiptId));
        UserOperationContractValidation.RequireIdentifier(
            providerCallAuthorizationReceiptId,
            nameof(providerCallAuthorizationReceiptId));
        UserOperationContractValidation.RequireOperationBinding(
            operationType,
            targetType,
            targetId,
            routeDeploymentId,
            fenceGeneration,
            workerAssignmentId,
            workerInstanceId);
        ArgumentOutOfRangeException.ThrowIfNegative(submittedResourceVersion);
        UserOperationContractValidation.RequireCanonicalState(requestedTargetState, nameof(requestedTargetState));
        UserOperationContractValidation.RequireSha256(commandSha256, nameof(commandSha256));
        UserOperationContractValidation.RequireSha256(
            dispatchTargetBindingSha256,
            nameof(dispatchTargetBindingSha256));
        UserOperationContractValidation.RequireSha256(
            dispatchPolicySnapshotSha256,
            nameof(dispatchPolicySnapshotSha256));
        UserOperationContractValidation.RequireUtcMicrosecond(
            challengeIssuedAtUtc,
            nameof(challengeIssuedAtUtc));
        UserOperationContractValidation.RequireUtcMicrosecond(
            challengeCapabilityExpiresAtUtc,
            nameof(challengeCapabilityExpiresAtUtc));
        ArgumentNullException.ThrowIfNull(challengeResultCapability);
        if (challengeCapabilityExpiresAtUtc <= challengeIssuedAtUtc)
        {
            throw new ArgumentException("The reconciliation authority timestamps are inconsistent.");
        }

        return new UserOperationReconciliationRequestedV3(
            attemptId,
            challengeId,
            challengeMessageId,
            operationId,
            originalDispatchMessageId,
            tenantId,
            operationType,
            targetType,
            targetId,
            submittedResourceVersion,
            requestedTargetState,
            commandSha256,
            dispatchTargetBindingSha256,
            dispatchPolicySnapshotSha256,
            routeDeploymentId,
            fenceGeneration,
            workerAssignmentId,
            workerInstanceId,
            gatewayStartReceiptId,
            providerCallAuthorizationReceiptId,
            challengeIssuedAtUtc,
            challengeCapabilityExpiresAtUtc,
            challengeResultCapability);
    }

    public static UserOperationReconciliationRequestedV3 ParseCanonical(
        string messageType,
        string canonicalJson)
    {
        if (!string.Equals(messageType, MessageType, StringComparison.Ordinal))
        {
            throw UserOperationContractValidation.InvalidPayload(
                "The message type does not match the reconciliation contract.");
        }

        using JsonDocument document = UserOperationContractValidation.ParseCanonicalDocument(canonicalJson);
        JsonElement root = document.RootElement;
        UserOperationContractValidation.RequireExactProperties(root, CanonicalProperties);
        if (!UserOperationContractValidation.ReadBoolean(root, "reconciliationOnly"))
        {
            throw UserOperationContractValidation.InvalidPayload(
                "A reconciliation request cannot authorize an invocation.");
        }

        UserOperationContractValidation.RequireVersion(
            UserOperationContractValidation.ReadInt32(root, "schemaVersion"),
            UserOperationProtocolVersions.ReconciliationRequestedV3,
            "schemaVersion");
        UserOperationReconciliationRequestedV3 value = Create(
            UserOperationContractValidation.ReadGuid(root, "attemptId"),
            UserOperationContractValidation.ReadGuid(root, "challengeId"),
            UserOperationContractValidation.ReadGuid(root, "challengeMessageId"),
            UserOperationContractValidation.ReadGuid(root, "operationId"),
            UserOperationContractValidation.ReadGuid(root, "originalDispatchMessageId"),
            UserOperationContractValidation.ReadGuid(root, "tenantId"),
            UserOperationContractValidation.ReadString(root, "operationType"),
            UserOperationContractValidation.ReadString(root, "targetType"),
            UserOperationContractValidation.ReadGuid(root, "targetId"),
            UserOperationContractValidation.ReadInt64(root, "submittedResourceVersion"),
            UserOperationContractValidation.ReadString(root, "requestedTargetState"),
            UserOperationContractValidation.ReadString(root, "commandSha256"),
            UserOperationContractValidation.ReadString(root, "dispatchTargetBindingSha256"),
            UserOperationContractValidation.ReadString(root, "dispatchPolicySnapshotSha256"),
            UserOperationContractValidation.ReadGuid(root, "routeDeploymentId"),
            UserOperationContractValidation.ReadInt64(root, "fenceGeneration"),
            UserOperationContractValidation.ReadGuid(root, "workerAssignmentId"),
            UserOperationContractValidation.ReadGuid(root, "workerInstanceId"),
            UserOperationContractValidation.ReadGuid(root, "gatewayStartReceiptId"),
            UserOperationContractValidation.ReadGuid(root, "providerCallAuthorizationReceiptId"),
            UserOperationContractValidation.ReadUtcMicrosecond(root, "challengeIssuedAtUtc"),
            UserOperationContractValidation.ReadUtcMicrosecond(root, "challengeCapabilityExpiresAtUtc"),
            UserOperationBearer.Create(
                UserOperationContractValidation.ReadString(root, "challengeResultCapability")));
        UserOperationContractValidation.RequireCanonicalRoundTrip(canonicalJson, value.ToCanonicalJson());
        return value;
    }

    public string ToCanonicalJson() => UserOperationContractValidation.WriteCanonical(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("attemptId", AttemptId);
        writer.WriteString(
            "challengeCapabilityExpiresAtUtc",
            UserOperationContractValidation.FormatUtcMicrosecond(ChallengeCapabilityExpiresAtUtc));
        writer.WriteString("challengeId", ChallengeId);
        writer.WriteString(
            "challengeIssuedAtUtc",
            UserOperationContractValidation.FormatUtcMicrosecond(ChallengeIssuedAtUtc));
        writer.WriteString("challengeMessageId", ChallengeMessageId);
        writer.WriteString("challengeResultCapability", ChallengeResultCapability.DangerousGetValue());
        writer.WriteString("commandSha256", CommandSha256);
        writer.WriteString("dispatchPolicySnapshotSha256", DispatchPolicySnapshotSha256);
        writer.WriteString("dispatchTargetBindingSha256", DispatchTargetBindingSha256);
        writer.WriteNumber("fenceGeneration", FenceGeneration);
        writer.WriteString("gatewayStartReceiptId", GatewayStartReceiptId);
        writer.WriteString("operationId", OperationId);
        writer.WriteString("operationType", OperationType);
        writer.WriteString("originalDispatchMessageId", OriginalDispatchMessageId);
        writer.WriteString("providerCallAuthorizationReceiptId", ProviderCallAuthorizationReceiptId);
        writer.WriteBoolean("reconciliationOnly", ReconciliationOnly);
        writer.WriteString("requestedTargetState", RequestedTargetState);
        writer.WriteString("routeDeploymentId", RouteDeploymentId);
        writer.WriteNumber("schemaVersion", SchemaVersion);
        writer.WriteNumber("submittedResourceVersion", SubmittedResourceVersion);
        writer.WriteString("targetId", TargetId);
        writer.WriteString("targetType", TargetType);
        writer.WriteString("tenantId", TenantId);
        writer.WriteString("workerAssignmentId", WorkerAssignmentId);
        writer.WriteString("workerInstanceId", WorkerInstanceId);
        writer.WriteEndObject();
    });

    public override string ToString() =>
        $"UserOperationReconciliationRequestedV3 {{ ChallengeId = {ChallengeId:D}, AttemptId = {AttemptId:D}, OperationId = {OperationId:D}, ChallengeCapabilityExpiresAtUtc = {ChallengeCapabilityExpiresAtUtc:O}, ChallengeResultCapability = [REDACTED] }}";
}
