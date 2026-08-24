using System.Text.Json;

namespace YO4X.Runtime.Contracts;

/// <summary>
/// A conclusive observation made through one committed gateway invocation.
/// There is deliberately no caller-owned "invoked" or "not sent" assertion.
/// </summary>
public sealed class UserOperationGatewayResultV5
{
    private readonly int schemaVersion;

    private static readonly string[] CanonicalProperties =
    [
        "attemptId",
        "dispatchMessageId",
        "dispatchPolicySnapshotSha256",
        "dispatchTargetBindingSha256",
        "gatewayObservationReceiptId",
        "gatewayReceiptSha256",
        "gatewayStartReceiptId",
        "invocationId",
        "observationSha256",
        "observedAtUtc",
        "operationId",
        "outcome",
        "providerCallAuthorizationReceiptId",
        "requestedTargetState",
        "resultCapability",
        "resultId",
        "schemaVersion",
        "submittedResourceVersion",
        "targetId",
        "targetObservation",
        "targetType"
    ];

    private UserOperationGatewayResultV5(
        Guid resultId,
        Guid attemptId,
        Guid invocationId,
        Guid operationId,
        Guid dispatchMessageId,
        Guid gatewayStartReceiptId,
        Guid gatewayObservationReceiptId,
        Guid providerCallAuthorizationReceiptId,
        string gatewayReceiptSha256,
        string targetType,
        Guid targetId,
        UserOperationTargetObservation targetObservation,
        long submittedResourceVersion,
        string requestedTargetState,
        string dispatchTargetBindingSha256,
        string dispatchPolicySnapshotSha256,
        UserOperationBearer resultCapability,
        UserOperationObservationOutcome outcome,
        string observationSha256,
        DateTimeOffset observedAtUtc)
    {
        schemaVersion = UserOperationProtocolVersions.ResultV5;
        ResultId = resultId;
        AttemptId = attemptId;
        InvocationId = invocationId;
        OperationId = operationId;
        DispatchMessageId = dispatchMessageId;
        GatewayStartReceiptId = gatewayStartReceiptId;
        GatewayObservationReceiptId = gatewayObservationReceiptId;
        ProviderCallAuthorizationReceiptId = providerCallAuthorizationReceiptId;
        GatewayReceiptSha256 = gatewayReceiptSha256;
        TargetType = targetType;
        TargetId = targetId;
        TargetObservation = targetObservation;
        SubmittedResourceVersion = submittedResourceVersion;
        RequestedTargetState = requestedTargetState;
        DispatchTargetBindingSha256 = dispatchTargetBindingSha256;
        DispatchPolicySnapshotSha256 = dispatchPolicySnapshotSha256;
        ResultCapability = resultCapability;
        Outcome = outcome;
        ObservationSha256 = observationSha256;
        ObservedAtUtc = observedAtUtc;
    }

    public const string MessageType = "yo4x.user-operation.result.v5";

    public int SchemaVersion => schemaVersion;

    public Guid ResultId { get; }

    public Guid AttemptId { get; }

    public Guid InvocationId { get; }

    public Guid OperationId { get; }

    public Guid DispatchMessageId { get; }

    public Guid GatewayStartReceiptId { get; }

    public Guid GatewayObservationReceiptId { get; }

    public Guid ProviderCallAuthorizationReceiptId { get; }

    public string GatewayReceiptSha256 { get; }

    public string TargetType { get; }

    public Guid TargetId { get; }

    public UserOperationTargetObservation TargetObservation { get; }

    public long SubmittedResourceVersion { get; }

    public string RequestedTargetState { get; }

    public string DispatchTargetBindingSha256 { get; }

    public string DispatchPolicySnapshotSha256 { get; }

    /// <summary>
    /// One-use result bearer. Persistence derives its exclusive expiry from the
    /// stored attempt and database authority-now; the caller cannot supply it.
    /// </summary>
    public UserOperationBearer ResultCapability { get; }

    public UserOperationObservationOutcome Outcome { get; }

    public string ObservationSha256 { get; }

    public DateTimeOffset ObservedAtUtc { get; }

    public static UserOperationGatewayResultV5 Create(
        Guid resultId,
        Guid attemptId,
        Guid invocationId,
        Guid operationId,
        Guid dispatchMessageId,
        Guid gatewayStartReceiptId,
        Guid gatewayObservationReceiptId,
        Guid providerCallAuthorizationReceiptId,
        string gatewayReceiptSha256,
        string targetType,
        Guid targetId,
        UserOperationTargetObservation targetObservation,
        long submittedResourceVersion,
        string requestedTargetState,
        string dispatchTargetBindingSha256,
        string dispatchPolicySnapshotSha256,
        UserOperationBearer resultCapability,
        UserOperationObservationOutcome outcome,
        string observationSha256,
        DateTimeOffset observedAtUtc)
    {
        UserOperationContractValidation.RequireIdentifier(resultId, nameof(resultId));
        UserOperationContractValidation.RequireIdentifier(attemptId, nameof(attemptId));
        UserOperationContractValidation.RequireIdentifier(invocationId, nameof(invocationId));
        UserOperationContractValidation.RequireIdentifier(operationId, nameof(operationId));
        UserOperationContractValidation.RequireIdentifier(dispatchMessageId, nameof(dispatchMessageId));
        UserOperationContractValidation.RequireIdentifier(
            gatewayStartReceiptId,
            nameof(gatewayStartReceiptId));
        UserOperationContractValidation.RequireIdentifier(
            gatewayObservationReceiptId,
            nameof(gatewayObservationReceiptId));
        UserOperationContractValidation.RequireIdentifier(
            providerCallAuthorizationReceiptId,
            nameof(providerCallAuthorizationReceiptId));
        UserOperationResultV5Validation.RequireTarget(targetType, targetId);
        ArgumentOutOfRangeException.ThrowIfNegative(submittedResourceVersion);
        UserOperationContractValidation.RequireCanonicalState(requestedTargetState, nameof(requestedTargetState));
        UserOperationContractValidation.RequireSha256(gatewayReceiptSha256, nameof(gatewayReceiptSha256));
        UserOperationContractValidation.RequireSha256(
            dispatchTargetBindingSha256,
            nameof(dispatchTargetBindingSha256));
        UserOperationContractValidation.RequireSha256(
            dispatchPolicySnapshotSha256,
            nameof(dispatchPolicySnapshotSha256));
        UserOperationContractValidation.RequireSha256(observationSha256, nameof(observationSha256));
        ArgumentNullException.ThrowIfNull(resultCapability);
        UserOperationContractValidation.RequireUtcMicrosecond(observedAtUtc, nameof(observedAtUtc));
        _ = UserOperationContractValidation.Outcome(outcome);
        UserOperationTargetObservationValidation.RequireResultConsistency(
            targetType,
            requestedTargetState,
            dispatchTargetBindingSha256,
            outcome,
            targetObservation);
        UserOperationTargetObservationValidation.RequireCanonicalSha256(
            targetObservation,
            observationSha256,
            nameof(observationSha256));

        return new UserOperationGatewayResultV5(
            resultId,
            attemptId,
            invocationId,
            operationId,
            dispatchMessageId,
            gatewayStartReceiptId,
            gatewayObservationReceiptId,
            providerCallAuthorizationReceiptId,
            gatewayReceiptSha256,
            targetType,
            targetId,
            targetObservation,
            submittedResourceVersion,
            requestedTargetState,
            dispatchTargetBindingSha256,
            dispatchPolicySnapshotSha256,
            resultCapability,
            outcome,
            observationSha256,
            observedAtUtc);
    }

    public static UserOperationGatewayResultV5 ParseCanonical(string messageType, string canonicalJson)
    {
        UserOperationResultV5Validation.RequireMessageType(messageType);
        using JsonDocument document = UserOperationContractValidation.ParseCanonicalDocument(canonicalJson);
        JsonElement root = document.RootElement;
        UserOperationContractValidation.RequireExactProperties(root, CanonicalProperties);
        UserOperationContractValidation.RequireVersion(
            UserOperationContractValidation.ReadInt32(root, "schemaVersion"),
            UserOperationProtocolVersions.ResultV5,
            "schemaVersion");
        UserOperationGatewayResultV5 value = Create(
            UserOperationContractValidation.ReadGuid(root, "resultId"),
            UserOperationContractValidation.ReadGuid(root, "attemptId"),
            UserOperationContractValidation.ReadGuid(root, "invocationId"),
            UserOperationContractValidation.ReadGuid(root, "operationId"),
            UserOperationContractValidation.ReadGuid(root, "dispatchMessageId"),
            UserOperationContractValidation.ReadGuid(root, "gatewayStartReceiptId"),
            UserOperationContractValidation.ReadGuid(root, "gatewayObservationReceiptId"),
            UserOperationContractValidation.ReadGuid(root, "providerCallAuthorizationReceiptId"),
            UserOperationContractValidation.ReadString(root, "gatewayReceiptSha256"),
            UserOperationContractValidation.ReadString(root, "targetType"),
            UserOperationContractValidation.ReadGuid(root, "targetId"),
            UserOperationTargetObservationValidation.Parse(
                root,
                UserOperationContractValidation.ReadString(root, "targetType")),
            UserOperationContractValidation.ReadInt64(root, "submittedResourceVersion"),
            UserOperationContractValidation.ReadString(root, "requestedTargetState"),
            UserOperationContractValidation.ReadString(root, "dispatchTargetBindingSha256"),
            UserOperationContractValidation.ReadString(root, "dispatchPolicySnapshotSha256"),
            UserOperationBearer.Create(UserOperationContractValidation.ReadString(root, "resultCapability")),
            UserOperationContractValidation.ReadOutcome(root, "outcome"),
            UserOperationContractValidation.ReadString(root, "observationSha256"),
            UserOperationContractValidation.ReadUtcMicrosecond(root, "observedAtUtc"));
        UserOperationContractValidation.RequireCanonicalRoundTrip(canonicalJson, value.ToCanonicalJson());
        return value;
    }

    public string ToCanonicalJson() => UserOperationContractValidation.WriteCanonical(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("attemptId", AttemptId);
        writer.WriteString("dispatchMessageId", DispatchMessageId);
        writer.WriteString("dispatchPolicySnapshotSha256", DispatchPolicySnapshotSha256);
        writer.WriteString("dispatchTargetBindingSha256", DispatchTargetBindingSha256);
        writer.WriteString("gatewayObservationReceiptId", GatewayObservationReceiptId);
        writer.WriteString("gatewayReceiptSha256", GatewayReceiptSha256);
        writer.WriteString("gatewayStartReceiptId", GatewayStartReceiptId);
        writer.WriteString("invocationId", InvocationId);
        writer.WriteString("observationSha256", ObservationSha256);
        writer.WriteString("observedAtUtc", UserOperationContractValidation.FormatUtcMicrosecond(ObservedAtUtc));
        writer.WriteString("operationId", OperationId);
        writer.WriteString("outcome", UserOperationContractValidation.Outcome(Outcome));
        writer.WriteString("providerCallAuthorizationReceiptId", ProviderCallAuthorizationReceiptId);
        writer.WriteString("requestedTargetState", RequestedTargetState);
        writer.WriteString("resultCapability", ResultCapability.DangerousGetValue());
        writer.WriteString("resultId", ResultId);
        writer.WriteNumber("schemaVersion", SchemaVersion);
        writer.WriteNumber("submittedResourceVersion", SubmittedResourceVersion);
        writer.WriteString("targetId", TargetId);
        writer.WritePropertyName("targetObservation");
        TargetObservation.WriteCanonical(writer);
        writer.WriteString("targetType", TargetType);
        writer.WriteEndObject();
    });

    public override string ToString() =>
        $"UserOperationGatewayResultV5 {{ ResultId = {ResultId:D}, AttemptId = {AttemptId:D}, InvocationId = {InvocationId:D}, Outcome = {Outcome}, ResultCapability = [REDACTED] }}";
}

/// <summary>
/// A conclusive observation made under a reconciliation-only challenge. The
/// challenge remains bound to the original committed invocation attempt.
/// </summary>
public sealed class UserOperationReconciliationResultV5
{
    private readonly int schemaVersion;

    private static readonly string[] CanonicalProperties =
    [
        "attemptId",
        "challengeConsumptionId",
        "challengeId",
        "challengeMessageId",
        "challengeResultCapability",
        "dispatchPolicySnapshotSha256",
        "dispatchTargetBindingSha256",
        "gatewayStartReceiptId",
        "observationSha256",
        "observedAtUtc",
        "operationId",
        "originalDispatchMessageId",
        "outcome",
        "providerCallAuthorizationReceiptId",
        "requestedTargetState",
        "resultId",
        "schemaVersion",
        "submittedResourceVersion",
        "targetId",
        "targetObservation",
        "targetType"
    ];

    private UserOperationReconciliationResultV5(
        Guid resultId,
        Guid attemptId,
        Guid operationId,
        Guid originalDispatchMessageId,
        Guid challengeConsumptionId,
        Guid challengeId,
        Guid challengeMessageId,
        Guid gatewayStartReceiptId,
        Guid providerCallAuthorizationReceiptId,
        string targetType,
        Guid targetId,
        UserOperationTargetObservation targetObservation,
        long submittedResourceVersion,
        string requestedTargetState,
        string dispatchTargetBindingSha256,
        string dispatchPolicySnapshotSha256,
        UserOperationBearer challengeResultCapability,
        UserOperationObservationOutcome outcome,
        string observationSha256,
        DateTimeOffset observedAtUtc)
    {
        schemaVersion = UserOperationProtocolVersions.ResultV5;
        ResultId = resultId;
        AttemptId = attemptId;
        OperationId = operationId;
        OriginalDispatchMessageId = originalDispatchMessageId;
        ChallengeConsumptionId = challengeConsumptionId;
        ChallengeId = challengeId;
        ChallengeMessageId = challengeMessageId;
        GatewayStartReceiptId = gatewayStartReceiptId;
        ProviderCallAuthorizationReceiptId = providerCallAuthorizationReceiptId;
        TargetType = targetType;
        TargetId = targetId;
        TargetObservation = targetObservation;
        SubmittedResourceVersion = submittedResourceVersion;
        RequestedTargetState = requestedTargetState;
        DispatchTargetBindingSha256 = dispatchTargetBindingSha256;
        DispatchPolicySnapshotSha256 = dispatchPolicySnapshotSha256;
        ChallengeResultCapability = challengeResultCapability;
        Outcome = outcome;
        ObservationSha256 = observationSha256;
        ObservedAtUtc = observedAtUtc;
    }

    public const string MessageType = "yo4x.user-operation.reconciliation-result.v5";

    public int SchemaVersion => schemaVersion;

    public Guid ResultId { get; }

    public Guid AttemptId { get; }

    public Guid OperationId { get; }

    public Guid OriginalDispatchMessageId { get; }

    /// <summary>The exact immutable challenge-capability consumption.</summary>
    public Guid ChallengeConsumptionId { get; }

    public Guid ChallengeId { get; }

    public Guid ChallengeMessageId { get; }

    public Guid GatewayStartReceiptId { get; }

    public Guid ProviderCallAuthorizationReceiptId { get; }

    public string TargetType { get; }

    public Guid TargetId { get; }

    public UserOperationTargetObservation TargetObservation { get; }

    public long SubmittedResourceVersion { get; }

    public string RequestedTargetState { get; }

    public string DispatchTargetBindingSha256 { get; }

    public string DispatchPolicySnapshotSha256 { get; }

    /// <summary>
    /// One-use challenge bearer. Persistence derives its exclusive expiry from
    /// the stored challenge and database authority-now.
    /// </summary>
    public UserOperationBearer ChallengeResultCapability { get; }

    public UserOperationObservationOutcome Outcome { get; }

    public string ObservationSha256 { get; }

    public DateTimeOffset ObservedAtUtc { get; }

    public static UserOperationReconciliationResultV5 Create(
        Guid resultId,
        Guid attemptId,
        Guid operationId,
        Guid originalDispatchMessageId,
        Guid challengeConsumptionId,
        Guid challengeId,
        Guid challengeMessageId,
        Guid gatewayStartReceiptId,
        Guid providerCallAuthorizationReceiptId,
        string targetType,
        Guid targetId,
        UserOperationTargetObservation targetObservation,
        long submittedResourceVersion,
        string requestedTargetState,
        string dispatchTargetBindingSha256,
        string dispatchPolicySnapshotSha256,
        UserOperationBearer challengeResultCapability,
        UserOperationObservationOutcome outcome,
        string observationSha256,
        DateTimeOffset observedAtUtc)
    {
        UserOperationContractValidation.RequireIdentifier(resultId, nameof(resultId));
        UserOperationContractValidation.RequireIdentifier(attemptId, nameof(attemptId));
        UserOperationContractValidation.RequireIdentifier(operationId, nameof(operationId));
        UserOperationContractValidation.RequireIdentifier(
            originalDispatchMessageId,
            nameof(originalDispatchMessageId));
        UserOperationContractValidation.RequireIdentifier(
            challengeConsumptionId,
            nameof(challengeConsumptionId));
        UserOperationContractValidation.RequireIdentifier(challengeId, nameof(challengeId));
        UserOperationContractValidation.RequireIdentifier(challengeMessageId, nameof(challengeMessageId));
        UserOperationContractValidation.RequireIdentifier(
            gatewayStartReceiptId,
            nameof(gatewayStartReceiptId));
        UserOperationContractValidation.RequireIdentifier(
            providerCallAuthorizationReceiptId,
            nameof(providerCallAuthorizationReceiptId));
        UserOperationResultV5Validation.RequireTarget(targetType, targetId);
        ArgumentOutOfRangeException.ThrowIfNegative(submittedResourceVersion);
        UserOperationContractValidation.RequireCanonicalState(requestedTargetState, nameof(requestedTargetState));
        UserOperationContractValidation.RequireSha256(
            dispatchTargetBindingSha256,
            nameof(dispatchTargetBindingSha256));
        UserOperationContractValidation.RequireSha256(
            dispatchPolicySnapshotSha256,
            nameof(dispatchPolicySnapshotSha256));
        UserOperationContractValidation.RequireSha256(observationSha256, nameof(observationSha256));
        ArgumentNullException.ThrowIfNull(challengeResultCapability);
        UserOperationContractValidation.RequireUtcMicrosecond(observedAtUtc, nameof(observedAtUtc));
        _ = UserOperationContractValidation.Outcome(outcome);
        UserOperationTargetObservationValidation.RequireResultConsistency(
            targetType,
            requestedTargetState,
            dispatchTargetBindingSha256,
            outcome,
            targetObservation);
        UserOperationTargetObservationValidation.RequireCanonicalSha256(
            targetObservation,
            observationSha256,
            nameof(observationSha256));

        return new UserOperationReconciliationResultV5(
            resultId,
            attemptId,
            operationId,
            originalDispatchMessageId,
            challengeConsumptionId,
            challengeId,
            challengeMessageId,
            gatewayStartReceiptId,
            providerCallAuthorizationReceiptId,
            targetType,
            targetId,
            targetObservation,
            submittedResourceVersion,
            requestedTargetState,
            dispatchTargetBindingSha256,
            dispatchPolicySnapshotSha256,
            challengeResultCapability,
            outcome,
            observationSha256,
            observedAtUtc);
    }

    public static UserOperationReconciliationResultV5 ParseCanonical(
        string messageType,
        string canonicalJson)
    {
        if (!string.Equals(messageType, MessageType, StringComparison.Ordinal))
        {
            throw UserOperationContractValidation.InvalidPayload("The message type is not reconciliation result.v5.");
        }

        using JsonDocument document = UserOperationContractValidation.ParseCanonicalDocument(canonicalJson);
        JsonElement root = document.RootElement;
        UserOperationContractValidation.RequireExactProperties(root, CanonicalProperties);
        UserOperationContractValidation.RequireVersion(
            UserOperationContractValidation.ReadInt32(root, "schemaVersion"),
            UserOperationProtocolVersions.ResultV5,
            "schemaVersion");
        UserOperationReconciliationResultV5 value = Create(
            UserOperationContractValidation.ReadGuid(root, "resultId"),
            UserOperationContractValidation.ReadGuid(root, "attemptId"),
            UserOperationContractValidation.ReadGuid(root, "operationId"),
            UserOperationContractValidation.ReadGuid(root, "originalDispatchMessageId"),
            UserOperationContractValidation.ReadGuid(root, "challengeConsumptionId"),
            UserOperationContractValidation.ReadGuid(root, "challengeId"),
            UserOperationContractValidation.ReadGuid(root, "challengeMessageId"),
            UserOperationContractValidation.ReadGuid(root, "gatewayStartReceiptId"),
            UserOperationContractValidation.ReadGuid(root, "providerCallAuthorizationReceiptId"),
            UserOperationContractValidation.ReadString(root, "targetType"),
            UserOperationContractValidation.ReadGuid(root, "targetId"),
            UserOperationTargetObservationValidation.Parse(
                root,
                UserOperationContractValidation.ReadString(root, "targetType")),
            UserOperationContractValidation.ReadInt64(root, "submittedResourceVersion"),
            UserOperationContractValidation.ReadString(root, "requestedTargetState"),
            UserOperationContractValidation.ReadString(root, "dispatchTargetBindingSha256"),
            UserOperationContractValidation.ReadString(root, "dispatchPolicySnapshotSha256"),
            UserOperationBearer.Create(
                UserOperationContractValidation.ReadString(root, "challengeResultCapability")),
            UserOperationContractValidation.ReadOutcome(root, "outcome"),
            UserOperationContractValidation.ReadString(root, "observationSha256"),
            UserOperationContractValidation.ReadUtcMicrosecond(root, "observedAtUtc"));
        UserOperationContractValidation.RequireCanonicalRoundTrip(canonicalJson, value.ToCanonicalJson());
        return value;
    }

    public string ToCanonicalJson() => UserOperationContractValidation.WriteCanonical(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("attemptId", AttemptId);
        writer.WriteString("challengeConsumptionId", ChallengeConsumptionId);
        writer.WriteString("challengeId", ChallengeId);
        writer.WriteString("challengeMessageId", ChallengeMessageId);
        writer.WriteString("challengeResultCapability", ChallengeResultCapability.DangerousGetValue());
        writer.WriteString("dispatchPolicySnapshotSha256", DispatchPolicySnapshotSha256);
        writer.WriteString("dispatchTargetBindingSha256", DispatchTargetBindingSha256);
        writer.WriteString("gatewayStartReceiptId", GatewayStartReceiptId);
        writer.WriteString("observationSha256", ObservationSha256);
        writer.WriteString("observedAtUtc", UserOperationContractValidation.FormatUtcMicrosecond(ObservedAtUtc));
        writer.WriteString("operationId", OperationId);
        writer.WriteString("originalDispatchMessageId", OriginalDispatchMessageId);
        writer.WriteString("outcome", UserOperationContractValidation.Outcome(Outcome));
        writer.WriteString("providerCallAuthorizationReceiptId", ProviderCallAuthorizationReceiptId);
        writer.WriteString("requestedTargetState", RequestedTargetState);
        writer.WriteString("resultId", ResultId);
        writer.WriteNumber("schemaVersion", SchemaVersion);
        writer.WriteNumber("submittedResourceVersion", SubmittedResourceVersion);
        writer.WriteString("targetId", TargetId);
        writer.WritePropertyName("targetObservation");
        TargetObservation.WriteCanonical(writer);
        writer.WriteString("targetType", TargetType);
        writer.WriteEndObject();
    });

    public override string ToString() =>
        $"UserOperationReconciliationResultV5 {{ ResultId = {ResultId:D}, ChallengeId = {ChallengeId:D}, AttemptId = {AttemptId:D}, Outcome = {Outcome}, ChallengeResultCapability = [REDACTED] }}";
}

internal static class UserOperationResultV5Validation
{
    public static void RequireMessageType(string messageType)
    {
        if (!string.Equals(messageType, UserOperationGatewayResultV5.MessageType, StringComparison.Ordinal))
        {
            throw UserOperationContractValidation.InvalidPayload("The message type is not gateway result.v5.");
        }
    }

    public static void RequireTarget(string targetType, Guid targetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetType);
        UserOperationContractValidation.RequireIdentifier(targetId, nameof(targetId));
        if (targetType is not ("broker_account" or "deployment"))
        {
            throw new ArgumentException("The result target type is invalid.", nameof(targetType));
        }
    }
}
