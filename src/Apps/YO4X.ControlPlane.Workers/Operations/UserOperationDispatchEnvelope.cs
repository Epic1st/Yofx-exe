using YO4X.BuildingBlocks;

namespace YO4X.ControlPlane.Workers.Operations;

internal static class UserOperationDispatchGuard
{
    public static bool IncreasesAuthority(string operationType) => operationType is
        "broker_account.connection_test" or
        "broker_account.credential_rotation" or
        "deployment.start";

    public static bool InvalidPolicyBlocksDispatch(string operationType, bool integrityValid) =>
        IncreasesAuthority(operationType) && !integrityValid;

    public static bool HasCompleteRoute(
        Guid? routeDeploymentId,
        long? fenceGeneration,
        Guid? workerAssignmentId,
        Guid? workerInstanceId) =>
        routeDeploymentId is not null && routeDeploymentId != Guid.Empty
        && fenceGeneration > 0
        && workerAssignmentId is not null && workerAssignmentId != Guid.Empty
        && workerInstanceId is not null && workerInstanceId != Guid.Empty;

    public static bool RouteWaitExpired(
        DateTimeOffset createdAt,
        DateTimeOffset now,
        TimeSpan maximumAge) => now - createdAt >= maximumAge;

    public static bool IsReconciliationBindingCurrent(
        string operationType,
        string requestedTargetState,
        long submittedResourceVersion,
        long currentResourceVersion,
        string currentDesiredState,
        long? dispatchFenceGeneration,
        long? currentFenceGeneration,
        Guid? dispatchWorkerAssignmentId,
        Guid? dispatchWorkerInstanceId,
        Guid? dispatchRouteDeploymentId,
        Guid? currentRouteDeploymentId,
        Guid? currentWorkerAssignmentId,
        Guid? currentWorkerInstanceId,
        string dispatchTargetBindingSha256,
        string currentTargetBindingSha256)
    {
        if (currentResourceVersion < submittedResourceVersion
            || !IsExpectedReconciliationState(operationType, requestedTargetState, currentDesiredState)
            || !FixedBindingEquals(dispatchTargetBindingSha256, currentTargetBindingSha256))
        {
            return false;
        }

        return dispatchFenceGeneration == currentFenceGeneration
            && dispatchRouteDeploymentId == currentRouteDeploymentId
            && dispatchWorkerAssignmentId == currentWorkerAssignmentId
            && dispatchWorkerInstanceId == currentWorkerInstanceId;
    }

    public static bool IsCurrent(
        string operationType,
        string requestedTargetState,
        long submittedResourceVersion,
        long currentResourceVersion,
        string currentState)
    {
        if (submittedResourceVersion != currentResourceVersion)
        {
            return false;
        }

        return IsCurrentState(operationType, requestedTargetState, currentState);
    }

    private static bool IsCurrentState(
        string operationType,
        string requestedTargetState,
        string currentState)
    {
        string expected = operationType switch
        {
            "broker_account.connection_test" => "active:ready",
            "broker_account.credential_rotation" => "active:rotation_pending",
            "broker_account.disable" => requestedTargetState,
            "broker_account.delete" => "disabled:deletion_pending",
            "deployment.start" => "starting",
            "deployment.close_only" => "close_only",
            "deployment.stop_after_flat" => "stop_after_flat",
            _ => string.Empty
        };
        return string.Equals(currentState, expected, StringComparison.Ordinal);
    }

    private static bool IsExpectedReconciliationState(
        string operationType,
        string requestedTargetState,
        string currentState) =>
        IsCurrentState(operationType, requestedTargetState, currentState)
        || operationType.StartsWith("broker_account.", StringComparison.Ordinal)
            && string.Equals(currentState, requestedTargetState, StringComparison.Ordinal);

    private static bool FixedBindingEquals(string left, string right) =>
        IsSha256(left)
        && IsSha256(right)
        && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(left),
            System.Text.Encoding.ASCII.GetBytes(right));

    private static bool IsSha256(string value) => value.Length == 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public sealed record UserOperationTargetBinding(
    long CurrentResourceVersion,
    string CurrentState,
    Guid RouteDeploymentId,
    long? FenceGeneration,
    Guid? WorkerAssignmentId,
    Guid? WorkerInstanceId,
    string BindingSha256);

public sealed record UserOperationPolicyEvidence(
    string EffectivePolicyDigest,
    string PolicyVersionWatermark,
    string PolicyInputSha256,
    string EvaluationEvidenceSha256);

public sealed record UserOperationDispatchEnvelope
{
    private static readonly Dictionary<string, string> TargetTypes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["broker_account.connection_test"] = "broker_account",
            ["broker_account.credential_rotation"] = "broker_account",
            ["broker_account.disable"] = "broker_account",
            ["broker_account.delete"] = "broker_account",
            ["deployment.start"] = "deployment",
            ["deployment.close_only"] = "deployment",
            ["deployment.stop_after_flat"] = "deployment"
        };

    private UserOperationDispatchEnvelope(
        Guid operationId,
        Guid tenantId,
        string operationType,
        string targetType,
        Guid targetId,
        long? requestedExpectedResourceVersion,
        long submittedResourceVersion,
        string requestedTargetState,
        Guid idempotencyRecordId,
        Guid correlationId,
        UserOperationTargetBinding targetBinding,
        UserOperationPolicyEvidence? policyEvidence,
        string dispatchPolicySnapshotSha256,
        DateTimeOffset requestedAt,
        DateTimeOffset dispatchedAt)
    {
        SchemaVersion = 1;
        OperationId = operationId;
        TenantId = tenantId;
        OperationType = operationType;
        TargetType = targetType;
        TargetId = targetId;
        RequestedExpectedResourceVersion = requestedExpectedResourceVersion;
        SubmittedResourceVersion = submittedResourceVersion;
        RequestedTargetState = requestedTargetState;
        IdempotencyRecordId = idempotencyRecordId;
        CorrelationId = correlationId;
        TargetBinding = targetBinding;
        PolicyEvidence = policyEvidence;
        DispatchPolicySnapshotSha256 = dispatchPolicySnapshotSha256;
        RequestedAt = requestedAt;
        DispatchedAt = dispatchedAt;
    }

    public int SchemaVersion { get; }

    public Guid OperationId { get; }

    public Guid TenantId { get; }

    public string OperationType { get; }

    public string TargetType { get; }

    public Guid TargetId { get; }

    public long? RequestedExpectedResourceVersion { get; }

    public long SubmittedResourceVersion { get; }

    public string RequestedTargetState { get; }

    public Guid IdempotencyRecordId { get; }

    public Guid CorrelationId { get; }

    public UserOperationTargetBinding TargetBinding { get; }

    public UserOperationPolicyEvidence? PolicyEvidence { get; }

    public string DispatchPolicySnapshotSha256 { get; }

    public DateTimeOffset RequestedAt { get; }

    public DateTimeOffset DispatchedAt { get; }

    public string MessageType => $"yo4x.{OperationType.Replace('_', '-')}.requested.v1";

    public static UserOperationDispatchEnvelope Create(
        Guid operationId,
        Guid tenantId,
        string operationType,
        string targetType,
        Guid targetId,
        long? requestedExpectedResourceVersion,
        long submittedResourceVersion,
        string requestedTargetState,
        Guid idempotencyRecordId,
        Guid correlationId,
        long currentResourceVersion,
        string currentState,
        Guid? routeDeploymentId,
        long? fenceGeneration,
        Guid? workerAssignmentId,
        Guid? workerInstanceId,
        object redactedTargetBinding,
        string? effectivePolicyDigest,
        string? policyVersionWatermark,
        string? policyInputSha256,
        string? evaluationEvidenceSha256,
        string dispatchPolicySnapshotSha256,
        DateTimeOffset requestedAt,
        DateTimeOffset dispatchedAt)
    {
        RequireIdentifier(operationId, nameof(operationId));
        RequireIdentifier(tenantId, nameof(tenantId));
        RequireIdentifier(targetId, nameof(targetId));
        RequireIdentifier(idempotencyRecordId, nameof(idempotencyRecordId));
        RequireIdentifier(correlationId, nameof(correlationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(operationType);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetType);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentState);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedTargetState);
        ArgumentNullException.ThrowIfNull(redactedTargetBinding);
        ArgumentOutOfRangeException.ThrowIfNegative(currentResourceVersion);
        ArgumentOutOfRangeException.ThrowIfNegative(submittedResourceVersion);
        if (!IsSha256(dispatchPolicySnapshotSha256))
        {
            throw new ArgumentException(
                "The current policy snapshot must be bound by a SHA-256 digest.",
                nameof(dispatchPolicySnapshotSha256));
        }

        bool hasAssignment = workerAssignmentId is not null && workerInstanceId is not null;
        if ((workerAssignmentId is null) != (workerInstanceId is null)
            || routeDeploymentId is null
            || routeDeploymentId == Guid.Empty
            || !hasAssignment
            || fenceGeneration is null
            || targetType == "deployment" && routeDeploymentId != targetId)
        {
            throw new ArgumentException("The runtime assignment binding is inconsistent.");
        }
        if (!TargetTypes.TryGetValue(operationType, out string? expectedTarget)
            || !string.Equals(expectedTarget, targetType, StringComparison.Ordinal))
        {
            throw new ArgumentException("The operation and target types are not allowlisted.", nameof(operationType));
        }

        string targetDigest = CanonicalJson.Sha256(redactedTargetBinding);
        UserOperationPolicyEvidence? policy = CreatePolicyEvidence(
            operationType,
            effectivePolicyDigest,
            policyVersionWatermark,
            policyInputSha256,
            evaluationEvidenceSha256);
        return new UserOperationDispatchEnvelope(
            operationId,
            tenantId,
            operationType,
            targetType,
            targetId,
            requestedExpectedResourceVersion,
            submittedResourceVersion,
            requestedTargetState.Trim().ToLowerInvariant(),
            idempotencyRecordId,
            correlationId,
            new UserOperationTargetBinding(
                currentResourceVersion,
                currentState.Trim().ToLowerInvariant(),
                routeDeploymentId.Value,
                fenceGeneration,
                workerAssignmentId,
                workerInstanceId,
                targetDigest),
            policy,
            dispatchPolicySnapshotSha256,
            requestedAt.ToUniversalTime(),
            dispatchedAt.ToUniversalTime());
    }

    private static UserOperationPolicyEvidence? CreatePolicyEvidence(
        string operationType,
        string? effectiveDigest,
        string? watermark,
        string? inputDigest,
        string? evaluationDigest)
    {
        string?[] values = [effectiveDigest, watermark, inputDigest, evaluationDigest];
        bool requiresPolicy = string.Equals(operationType, "deployment.start", StringComparison.Ordinal);
        bool hasAny = values.Any(static value => value is not null);
        bool hasAll = values.All(static value => value is not null);
        if ((requiresPolicy && !hasAll) || (!requiresPolicy && hasAny))
        {
            throw new ArgumentException("Deployment start requires one complete persisted policy-evaluation binding.");
        }

        if (!requiresPolicy)
        {
            return null;
        }

        foreach (string? value in values)
        {
            if (!IsSha256(value!))
            {
                throw new ArgumentException("Policy evidence must contain SHA-256 digests.");
            }
        }

        return new UserOperationPolicyEvidence(
            effectiveDigest!,
            watermark!,
            inputDigest!,
            evaluationDigest!);
    }

    private static bool IsSha256(string value) =>
        value.Length == 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void RequireIdentifier(Guid value, string name)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An identifier is required.", name);
        }
    }
}
