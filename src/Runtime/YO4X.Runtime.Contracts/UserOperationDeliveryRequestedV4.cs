using System.Text.Json;

namespace YO4X.Runtime.Contracts;

/// <summary>
/// A bounded delivery request. Possession authorizes only a supervisor claim;
/// the delivery capability can never begin or execute a gateway invocation.
/// </summary>
public sealed class UserOperationDeliveryRequestedV4
{
    private readonly int schemaVersion;

    private static readonly string[] CanonicalProperties =
    [
        "assignmentLeaseExpiresAtUtc",
        "attemptId",
        "commandSha256",
        "deliveryCapability",
        "dispatchMessageId",
        "dispatchPolicySnapshotSha256",
        "dispatchTargetBindingSha256",
        "dispatchedAtUtc",
        "executeNotAfterUtc",
        "fenceGeneration",
        "operationId",
        "operationType",
        "requestedTargetState",
        "resultCapability",
        "resultCapabilityExpiresAtUtc",
        "routeDeploymentId",
        "schemaVersion",
        "submittedResourceVersion",
        "targetId",
        "targetType",
        "tenantId",
        "workerAssignmentId",
        "workerInstanceId"
    ];

    private UserOperationDeliveryRequestedV4(
        Guid attemptId,
        Guid operationId,
        Guid dispatchMessageId,
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
        DateTimeOffset assignmentLeaseExpiresAtUtc,
        DateTimeOffset dispatchedAtUtc,
        DateTimeOffset executeNotAfterUtc,
        UserOperationBearer deliveryCapability,
        UserOperationBearer resultCapability,
        DateTimeOffset resultCapabilityExpiresAtUtc)
    {
        schemaVersion = UserOperationProtocolVersions.DeliveryRequestedV4;
        AttemptId = attemptId;
        OperationId = operationId;
        DispatchMessageId = dispatchMessageId;
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
        AssignmentLeaseExpiresAtUtc = assignmentLeaseExpiresAtUtc;
        DispatchedAtUtc = dispatchedAtUtc;
        ExecuteNotAfterUtc = executeNotAfterUtc;
        DeliveryCapability = deliveryCapability;
        ResultCapability = resultCapability;
        ResultCapabilityExpiresAtUtc = resultCapabilityExpiresAtUtc;
    }

    public int SchemaVersion => schemaVersion;

    public Guid AttemptId { get; }

    public Guid OperationId { get; }

    public Guid DispatchMessageId { get; }

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

    /// <summary>Exclusive lifetime of the frozen assignment.</summary>
    public DateTimeOffset AssignmentLeaseExpiresAtUtc { get; }

    public DateTimeOffset DispatchedAtUtc { get; }

    /// <summary>Exclusive database-clock deadline for beginning an attempt.</summary>
    public DateTimeOffset ExecuteNotAfterUtc { get; }

    public UserOperationBearer DeliveryCapability { get; }

    public UserOperationBearer ResultCapability { get; }

    /// <summary>Exclusive first-use boundary; an exact accepted replay may outlive it.</summary>
    public DateTimeOffset ResultCapabilityExpiresAtUtc { get; }

    public string MessageType =>
        $"yo4x.{OperationType.Replace('_', '-')}.requested.v4";

    public static UserOperationDeliveryRequestedV4 Create(
        Guid attemptId,
        Guid operationId,
        Guid dispatchMessageId,
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
        DateTimeOffset assignmentLeaseExpiresAtUtc,
        DateTimeOffset dispatchedAtUtc,
        DateTimeOffset executeNotAfterUtc,
        UserOperationBearer deliveryCapability,
        UserOperationBearer resultCapability,
        DateTimeOffset resultCapabilityExpiresAtUtc)
    {
        UserOperationContractValidation.RequireIdentifier(attemptId, nameof(attemptId));
        UserOperationContractValidation.RequireIdentifier(operationId, nameof(operationId));
        UserOperationContractValidation.RequireIdentifier(dispatchMessageId, nameof(dispatchMessageId));
        UserOperationContractValidation.RequireIdentifier(tenantId, nameof(tenantId));
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
            assignmentLeaseExpiresAtUtc,
            nameof(assignmentLeaseExpiresAtUtc));
        UserOperationContractValidation.RequireUtcMicrosecond(dispatchedAtUtc, nameof(dispatchedAtUtc));
        UserOperationContractValidation.RequireUtcMicrosecond(executeNotAfterUtc, nameof(executeNotAfterUtc));
        UserOperationContractValidation.RequireUtcMicrosecond(
            resultCapabilityExpiresAtUtc,
            nameof(resultCapabilityExpiresAtUtc));
        ArgumentNullException.ThrowIfNull(deliveryCapability);
        ArgumentNullException.ThrowIfNull(resultCapability);
        UserOperationContractValidation.RequireDistinctBearers(deliveryCapability, resultCapability);
        if (executeNotAfterUtc <= dispatchedAtUtc
            || assignmentLeaseExpiresAtUtc <= dispatchedAtUtc
            || resultCapabilityExpiresAtUtc <= dispatchedAtUtc
            || executeNotAfterUtc >= assignmentLeaseExpiresAtUtc
            || executeNotAfterUtc >= resultCapabilityExpiresAtUtc)
        {
            throw new ArgumentException("The delivery authority timestamps are inconsistent.");
        }

        return new UserOperationDeliveryRequestedV4(
            attemptId,
            operationId,
            dispatchMessageId,
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
            assignmentLeaseExpiresAtUtc,
            dispatchedAtUtc,
            executeNotAfterUtc,
            deliveryCapability,
            resultCapability,
            resultCapabilityExpiresAtUtc);
    }

    public static UserOperationDeliveryRequestedV4 ParseCanonical(
        string messageType,
        string canonicalJson)
    {
        using JsonDocument document = UserOperationContractValidation.ParseCanonicalDocument(canonicalJson);
        JsonElement root = document.RootElement;
        UserOperationContractValidation.RequireExactProperties(root, CanonicalProperties);
        UserOperationDeliveryRequestedV4 value = Create(
            UserOperationContractValidation.ReadGuid(root, "attemptId"),
            UserOperationContractValidation.ReadGuid(root, "operationId"),
            UserOperationContractValidation.ReadGuid(root, "dispatchMessageId"),
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
            UserOperationContractValidation.ReadUtcMicrosecond(root, "assignmentLeaseExpiresAtUtc"),
            UserOperationContractValidation.ReadUtcMicrosecond(root, "dispatchedAtUtc"),
            UserOperationContractValidation.ReadUtcMicrosecond(root, "executeNotAfterUtc"),
            UserOperationBearer.Create(UserOperationContractValidation.ReadString(root, "deliveryCapability")),
            UserOperationBearer.Create(UserOperationContractValidation.ReadString(root, "resultCapability")),
            UserOperationContractValidation.ReadUtcMicrosecond(root, "resultCapabilityExpiresAtUtc"));
        if (!string.Equals(messageType, value.MessageType, StringComparison.Ordinal))
        {
            throw UserOperationContractValidation.InvalidPayload("The message type does not match the delivery contract.");
        }

        UserOperationContractValidation.RequireVersion(
            UserOperationContractValidation.ReadInt32(root, "schemaVersion"),
            UserOperationProtocolVersions.DeliveryRequestedV4,
            "schemaVersion");
        UserOperationContractValidation.RequireCanonicalRoundTrip(canonicalJson, value.ToCanonicalJson());
        return value;
    }

    public string ToCanonicalJson() => UserOperationContractValidation.WriteCanonical(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString(
            "assignmentLeaseExpiresAtUtc",
            UserOperationContractValidation.FormatUtcMicrosecond(AssignmentLeaseExpiresAtUtc));
        writer.WriteString("attemptId", AttemptId);
        writer.WriteString("commandSha256", CommandSha256);
        writer.WriteString("deliveryCapability", DeliveryCapability.DangerousGetValue());
        writer.WriteString("dispatchMessageId", DispatchMessageId);
        writer.WriteString("dispatchPolicySnapshotSha256", DispatchPolicySnapshotSha256);
        writer.WriteString("dispatchTargetBindingSha256", DispatchTargetBindingSha256);
        writer.WriteString("dispatchedAtUtc", UserOperationContractValidation.FormatUtcMicrosecond(DispatchedAtUtc));
        writer.WriteString("executeNotAfterUtc", UserOperationContractValidation.FormatUtcMicrosecond(ExecuteNotAfterUtc));
        writer.WriteNumber("fenceGeneration", FenceGeneration);
        writer.WriteString("operationId", OperationId);
        writer.WriteString("operationType", OperationType);
        writer.WriteString("requestedTargetState", RequestedTargetState);
        writer.WriteString("resultCapability", ResultCapability.DangerousGetValue());
        writer.WriteString(
            "resultCapabilityExpiresAtUtc",
            UserOperationContractValidation.FormatUtcMicrosecond(ResultCapabilityExpiresAtUtc));
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
        $"UserOperationDeliveryRequestedV4 {{ AttemptId = {AttemptId:D}, OperationId = {OperationId:D}, TargetType = {TargetType}, ExecuteNotAfterUtc = {ExecuteNotAfterUtc:O}, DeliveryCapability = [REDACTED], ResultCapability = [REDACTED] }}";
}
