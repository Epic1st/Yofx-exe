using System.Security.Cryptography;
using System.Text;
using YO4X.BuildingBlocks;
using YO4X.Runtime.Contracts;

namespace YO4X.ControlPlane.Application;

public enum UserOperationInvocationAttemptState
{
    Prepared = 0,
    Authorized = 1
}

/// <summary>Supervisor-only consumption of the delivery bearer.</summary>
public sealed class UserOperationSupervisorDeliveryClaimRequest
{
    private UserOperationSupervisorDeliveryClaimRequest(
        Guid attemptId,
        Guid dispatchMessageId,
        UserOperationBearer deliveryCapability)
    {
        AttemptId = attemptId;
        DispatchMessageId = dispatchMessageId;
        DeliveryCapability = deliveryCapability;
    }

    public Guid AttemptId { get; }

    public Guid DispatchMessageId { get; }

    public UserOperationBearer DeliveryCapability { get; }

    public static UserOperationSupervisorDeliveryClaimRequest Create(
        Guid attemptId,
        Guid dispatchMessageId,
        UserOperationBearer deliveryCapability)
    {
        UserOperationInvocationValidation.RequireIdentifier(attemptId, nameof(attemptId));
        UserOperationInvocationValidation.RequireIdentifier(dispatchMessageId, nameof(dispatchMessageId));
        ArgumentNullException.ThrowIfNull(deliveryCapability);
        return new UserOperationSupervisorDeliveryClaimRequest(
            attemptId,
            dispatchMessageId,
            deliveryCapability);
    }

    public override string ToString() =>
        $"UserOperationSupervisorDeliveryClaimRequest {{ AttemptId = {AttemptId:D}, DispatchMessageId = {DispatchMessageId:D}, DeliveryCapability = [REDACTED] }}";
}

/// <summary>A committed claim containing only a transient gateway bearer.</summary>
public sealed class UserOperationGatewayDeliveryClaim
{
    private UserOperationGatewayDeliveryClaim(
        Guid attemptId,
        Guid dispatchMessageId,
        Guid deliveryClaimId,
        int deliveryClaimGeneration,
        UserOperationBearer gatewayCapability,
        DateTimeOffset claimedAtUtc,
        DateTimeOffset gatewayCapabilityExpiresAtUtc)
    {
        AttemptId = attemptId;
        DispatchMessageId = dispatchMessageId;
        DeliveryClaimId = deliveryClaimId;
        DeliveryClaimGeneration = deliveryClaimGeneration;
        GatewayCapability = gatewayCapability;
        ClaimedAtUtc = claimedAtUtc;
        GatewayCapabilityExpiresAtUtc = gatewayCapabilityExpiresAtUtc;
    }

    public Guid AttemptId { get; }

    public Guid DispatchMessageId { get; }

    public Guid DeliveryClaimId { get; }

    public int DeliveryClaimGeneration { get; }

    public UserOperationBearer GatewayCapability { get; }

    public DateTimeOffset ClaimedAtUtc { get; }

    /// <summary>Exclusive first-use boundary for begin or reject-before-begin.</summary>
    public DateTimeOffset GatewayCapabilityExpiresAtUtc { get; }

    public static UserOperationGatewayDeliveryClaim Create(
        Guid attemptId,
        Guid dispatchMessageId,
        Guid deliveryClaimId,
        int deliveryClaimGeneration,
        UserOperationBearer gatewayCapability,
        DateTimeOffset claimedAtUtc,
        DateTimeOffset gatewayCapabilityExpiresAtUtc)
    {
        UserOperationInvocationValidation.RequireIdentifier(attemptId, nameof(attemptId));
        UserOperationInvocationValidation.RequireIdentifier(dispatchMessageId, nameof(dispatchMessageId));
        UserOperationInvocationValidation.RequireIdentifier(deliveryClaimId, nameof(deliveryClaimId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deliveryClaimGeneration);
        ArgumentNullException.ThrowIfNull(gatewayCapability);
        UserOperationInvocationValidation.RequireWindow(
            claimedAtUtc,
            gatewayCapabilityExpiresAtUtc,
            nameof(claimedAtUtc),
            nameof(gatewayCapabilityExpiresAtUtc));
        return new UserOperationGatewayDeliveryClaim(
            attemptId,
            dispatchMessageId,
            deliveryClaimId,
            deliveryClaimGeneration,
            gatewayCapability,
            claimedAtUtc,
            gatewayCapabilityExpiresAtUtc);
    }

    public override string ToString() =>
        $"UserOperationGatewayDeliveryClaim {{ AttemptId = {AttemptId:D}, DeliveryClaimId = {DeliveryClaimId:D}, DeliveryClaimGeneration = {DeliveryClaimGeneration}, GatewayCapabilityExpiresAtUtc = {GatewayCapabilityExpiresAtUtc:O}, GatewayCapability = [REDACTED] }}";
}

/// <summary>Gateway begin uses only the supervisor-minted gateway bearer.</summary>
public sealed class UserOperationGatewayBeginRequest
{
    private UserOperationGatewayBeginRequest(
        Guid attemptId,
        Guid dispatchMessageId,
        Guid deliveryClaimId,
        int deliveryClaimGeneration,
        UserOperationBearer gatewayCapability)
    {
        AttemptId = attemptId;
        DispatchMessageId = dispatchMessageId;
        DeliveryClaimId = deliveryClaimId;
        DeliveryClaimGeneration = deliveryClaimGeneration;
        GatewayCapability = gatewayCapability;
    }

    public Guid AttemptId { get; }

    public Guid DispatchMessageId { get; }

    public Guid DeliveryClaimId { get; }

    public int DeliveryClaimGeneration { get; }

    public UserOperationBearer GatewayCapability { get; }

    public static UserOperationGatewayBeginRequest Create(
        Guid attemptId,
        Guid dispatchMessageId,
        Guid deliveryClaimId,
        int deliveryClaimGeneration,
        UserOperationBearer gatewayCapability)
    {
        UserOperationInvocationValidation.RequireIdentifier(attemptId, nameof(attemptId));
        UserOperationInvocationValidation.RequireIdentifier(dispatchMessageId, nameof(dispatchMessageId));
        UserOperationInvocationValidation.RequireIdentifier(deliveryClaimId, nameof(deliveryClaimId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deliveryClaimGeneration);
        ArgumentNullException.ThrowIfNull(gatewayCapability);
        return new UserOperationGatewayBeginRequest(
            attemptId,
            dispatchMessageId,
            deliveryClaimId,
            deliveryClaimGeneration,
            gatewayCapability);
    }

    public override string ToString() =>
        $"UserOperationGatewayBeginRequest {{ AttemptId = {AttemptId:D}, DeliveryClaimId = {DeliveryClaimId:D}, DeliveryClaimGeneration = {DeliveryClaimGeneration}, GatewayCapability = [REDACTED] }}";
}

/// <summary>
/// Return-after-commit proof that an attempt is prepared. Neither bearer is
/// executable: each must be consumed by its own later database transaction.
/// </summary>
public sealed class UserOperationGatewayBeginAuthority
{
    private readonly UserOperationInvocationAttemptState state =
        UserOperationInvocationAttemptState.Prepared;

    private UserOperationGatewayBeginAuthority(
        Guid attemptId,
        Guid invocationId,
        Guid gatewayStartReceiptId,
        UserOperationBearer redemptionNonce,
        UserOperationBearer gatewayObservationReceiptBearer,
        DateTimeOffset preparedAtUtc,
        DateTimeOffset credentialRedemptionExpiresAtUtc,
        DateTimeOffset gatewayObservationReceiptExpiresAtUtc)
    {
        AttemptId = attemptId;
        InvocationId = invocationId;
        GatewayStartReceiptId = gatewayStartReceiptId;
        RedemptionNonce = redemptionNonce;
        GatewayObservationReceiptBearer = gatewayObservationReceiptBearer;
        PreparedAtUtc = preparedAtUtc;
        CredentialRedemptionExpiresAtUtc = credentialRedemptionExpiresAtUtc;
        GatewayObservationReceiptExpiresAtUtc = gatewayObservationReceiptExpiresAtUtc;
    }

    public Guid AttemptId { get; }

    public Guid InvocationId { get; }

    public Guid GatewayStartReceiptId { get; }

    public UserOperationBearer RedemptionNonce { get; }

    public UserOperationBearer GatewayObservationReceiptBearer { get; }

    public DateTimeOffset PreparedAtUtc { get; }

    public DateTimeOffset CredentialRedemptionExpiresAtUtc { get; }

    public DateTimeOffset GatewayObservationReceiptExpiresAtUtc { get; }

    public UserOperationInvocationAttemptState State => state;

    public static UserOperationGatewayBeginAuthority Create(
        Guid attemptId,
        Guid invocationId,
        Guid gatewayStartReceiptId,
        UserOperationBearer redemptionNonce,
        UserOperationBearer gatewayObservationReceiptBearer,
        DateTimeOffset preparedAtUtc,
        DateTimeOffset credentialRedemptionExpiresAtUtc,
        DateTimeOffset gatewayObservationReceiptExpiresAtUtc)
    {
        UserOperationInvocationValidation.RequireIdentifier(attemptId, nameof(attemptId));
        UserOperationInvocationValidation.RequireIdentifier(invocationId, nameof(invocationId));
        UserOperationInvocationValidation.RequireIdentifier(
            gatewayStartReceiptId,
            nameof(gatewayStartReceiptId));
        ArgumentNullException.ThrowIfNull(redemptionNonce);
        ArgumentNullException.ThrowIfNull(gatewayObservationReceiptBearer);
        UserOperationInvocationValidation.RequireDistinctBearers(
            redemptionNonce,
            gatewayObservationReceiptBearer);
        UserOperationInvocationValidation.RequireWindow(
            preparedAtUtc,
            credentialRedemptionExpiresAtUtc,
            nameof(preparedAtUtc),
            nameof(credentialRedemptionExpiresAtUtc));
        UserOperationInvocationValidation.RequireWindow(
            preparedAtUtc,
            gatewayObservationReceiptExpiresAtUtc,
            nameof(preparedAtUtc),
            nameof(gatewayObservationReceiptExpiresAtUtc));
        return new UserOperationGatewayBeginAuthority(
            attemptId,
            invocationId,
            gatewayStartReceiptId,
            redemptionNonce,
            gatewayObservationReceiptBearer,
            preparedAtUtc,
            credentialRedemptionExpiresAtUtc,
            gatewayObservationReceiptExpiresAtUtc);
    }

    public override string ToString() =>
        $"UserOperationGatewayBeginAuthority {{ AttemptId = {AttemptId:D}, InvocationId = {InvocationId:D}, State = {State}, CredentialRedemptionExpiresAtUtc = {CredentialRedemptionExpiresAtUtc:O}, GatewayObservationReceiptExpiresAtUtc = {GatewayObservationReceiptExpiresAtUtc:O}, RedemptionNonce = [REDACTED], GatewayObservationReceiptBearer = [REDACTED] }}";
}

public sealed class UserOperationGatewayRejectBeforeBeginRequest
{
    public const string SupervisorRejectionReason =
        "supervisor_rejected_before_invocation";

    private UserOperationGatewayRejectBeforeBeginRequest(
        Guid attemptId,
        Guid deliveryClaimId,
        int deliveryClaimGeneration,
        UserOperationBearer gatewayCapability)
    {
        AttemptId = attemptId;
        DeliveryClaimId = deliveryClaimId;
        DeliveryClaimGeneration = deliveryClaimGeneration;
        GatewayCapability = gatewayCapability;
        ReasonCode = SupervisorRejectionReason;
    }

    public Guid AttemptId { get; }

    public Guid DeliveryClaimId { get; }

    public int DeliveryClaimGeneration { get; }

    public UserOperationBearer GatewayCapability { get; }

    public string ReasonCode { get; }

    public static UserOperationGatewayRejectBeforeBeginRequest Create(
        Guid attemptId,
        Guid deliveryClaimId,
        int deliveryClaimGeneration,
        UserOperationBearer gatewayCapability)
    {
        UserOperationInvocationValidation.RequireIdentifier(attemptId, nameof(attemptId));
        UserOperationInvocationValidation.RequireIdentifier(deliveryClaimId, nameof(deliveryClaimId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deliveryClaimGeneration);
        ArgumentNullException.ThrowIfNull(gatewayCapability);
        return new UserOperationGatewayRejectBeforeBeginRequest(
            attemptId,
            deliveryClaimId,
            deliveryClaimGeneration,
            gatewayCapability);
    }

    public override string ToString() =>
        $"UserOperationGatewayRejectBeforeBeginRequest {{ AttemptId = {AttemptId:D}, DeliveryClaimId = {DeliveryClaimId:D}, DeliveryClaimGeneration = {DeliveryClaimGeneration}, ReasonCode = {ReasonCode}, GatewayCapability = [REDACTED] }}";
}

public sealed record UserOperationGatewayRejectBeforeBeginReceipt(
    Guid AttemptId,
    Guid DeliveryClaimId,
    Guid RejectionReceiptId,
    DateTimeOffset RejectedAtUtc);

/// <summary>
/// Credential-boundary request to authorize and execute exactly one provider
/// call. The redemption nonce is consumed internally and never becomes a grant.
/// </summary>
public sealed class UserOperationProviderCallExecutionRequest
{
    private UserOperationProviderCallExecutionRequest(
        Guid attemptId,
        Guid invocationId,
        Guid gatewayStartReceiptId,
        UserOperationBearer redemptionNonce)
    {
        AttemptId = attemptId;
        InvocationId = invocationId;
        GatewayStartReceiptId = gatewayStartReceiptId;
        RedemptionNonce = redemptionNonce;
    }

    public Guid AttemptId { get; }

    public Guid InvocationId { get; }

    public Guid GatewayStartReceiptId { get; }

    public UserOperationBearer RedemptionNonce { get; }

    public static UserOperationProviderCallExecutionRequest Create(
        Guid attemptId,
        Guid invocationId,
        Guid gatewayStartReceiptId,
        UserOperationBearer redemptionNonce)
    {
        UserOperationInvocationValidation.RequireIdentifier(attemptId, nameof(attemptId));
        UserOperationInvocationValidation.RequireIdentifier(invocationId, nameof(invocationId));
        UserOperationInvocationValidation.RequireIdentifier(
            gatewayStartReceiptId,
            nameof(gatewayStartReceiptId));
        ArgumentNullException.ThrowIfNull(redemptionNonce);
        return new UserOperationProviderCallExecutionRequest(
            attemptId,
            invocationId,
            gatewayStartReceiptId,
            redemptionNonce);
    }

    public override string ToString() =>
        $"UserOperationProviderCallExecutionRequest {{ AttemptId = {AttemptId:D}, InvocationId = {InvocationId:D}, RedemptionNonce = [REDACTED] }}";
}

public enum UserOperationProviderCallExecutionState
{
    Observed = 0,
    Ambiguous = 1
}

/// <summary>
/// Non-executable metadata returned after the credential boundary reaches its
/// point of no return. The sealed derived shapes keep conclusive observation
/// evidence structurally separate from a durable ambiguous outcome.
/// </summary>
public abstract class UserOperationProviderCallExecutionReceipt
{
    private protected UserOperationProviderCallExecutionReceipt(
        Guid attemptId,
        Guid invocationId,
        Guid gatewayStartReceiptId,
        Guid providerCallAuthorizationReceiptId,
        UserOperationProviderCallExecutionState state)
    {
        AttemptId = attemptId;
        InvocationId = invocationId;
        GatewayStartReceiptId = gatewayStartReceiptId;
        ProviderCallAuthorizationReceiptId = providerCallAuthorizationReceiptId;
        State = state;
    }

    public Guid AttemptId { get; }

    public Guid InvocationId { get; }

    public Guid GatewayStartReceiptId { get; }

    public Guid ProviderCallAuthorizationReceiptId { get; }

    public UserOperationProviderCallExecutionState State { get; }
}

/// <summary>
/// Conclusive provider observation. Only this shape carries fields accepted by
/// the later gateway-observation boundary.
/// </summary>
public sealed class UserOperationProviderCallObservedReceipt
    : UserOperationProviderCallExecutionReceipt
{
    private UserOperationProviderCallObservedReceipt(
        Guid attemptId,
        Guid invocationId,
        Guid gatewayStartReceiptId,
        Guid providerCallAuthorizationReceiptId,
        UserOperationObservationOutcome outcome,
        UserOperationTargetObservation targetObservation,
        DateTimeOffset observedAtUtc)
        : base(
            attemptId,
            invocationId,
            gatewayStartReceiptId,
            providerCallAuthorizationReceiptId,
            UserOperationProviderCallExecutionState.Observed)
    {
        Outcome = outcome;
        TargetObservation = targetObservation;
        ObservationSha256 = targetObservation.ComputeCanonicalSha256();
        ObservedAtUtc = observedAtUtc;
    }

    public UserOperationObservationOutcome Outcome { get; }

    public UserOperationTargetObservation TargetObservation { get; }

    public string ObservationSha256 { get; }

    public DateTimeOffset ObservedAtUtc { get; }

    public static UserOperationProviderCallObservedReceipt Create(
        Guid attemptId,
        Guid invocationId,
        Guid gatewayStartReceiptId,
        Guid providerCallAuthorizationReceiptId,
        UserOperationObservationOutcome outcome,
        UserOperationTargetObservation targetObservation,
        DateTimeOffset observedAtUtc)
    {
        UserOperationInvocationValidation.RequireIdentifier(attemptId, nameof(attemptId));
        UserOperationInvocationValidation.RequireIdentifier(invocationId, nameof(invocationId));
        UserOperationInvocationValidation.RequireIdentifier(
            gatewayStartReceiptId,
            nameof(gatewayStartReceiptId));
        UserOperationInvocationValidation.RequireIdentifier(
            providerCallAuthorizationReceiptId,
            nameof(providerCallAuthorizationReceiptId));
        UserOperationInvocationValidation.RequireOutcome(outcome);
        ArgumentNullException.ThrowIfNull(targetObservation);
        UserOperationInvocationValidation.RequireUtcMicrosecond(
            observedAtUtc,
            nameof(observedAtUtc));
        return new UserOperationProviderCallObservedReceipt(
            attemptId,
            invocationId,
            gatewayStartReceiptId,
            providerCallAuthorizationReceiptId,
            outcome,
            targetObservation,
            observedAtUtc);
    }

    public override string ToString() =>
        $"UserOperationProviderCallObservedReceipt {{ AttemptId = {AttemptId:D}, InvocationId = {InvocationId:D}, ProviderCallAuthorizationReceiptId = {ProviderCallAuthorizationReceiptId:D}, State = {State}, Outcome = {Outcome}, ObservedAtUtc = {ObservedAtUtc:O} }}";
}

/// <summary>
/// Durable acknowledgement that authorization occurred but no conclusive
/// provider observation exists. This shape deliberately has no observation
/// outcome, digest, or observation time and cannot be submitted as evidence.
/// </summary>
public sealed class UserOperationProviderCallAmbiguousReceipt
    : UserOperationProviderCallExecutionReceipt
{
    private UserOperationProviderCallAmbiguousReceipt(
        Guid attemptId,
        Guid invocationId,
        Guid gatewayStartReceiptId,
        Guid providerCallAuthorizationReceiptId,
        Guid ambiguityReceiptId,
        DateTimeOffset ambiguityRecordedAtUtc)
        : base(
            attemptId,
            invocationId,
            gatewayStartReceiptId,
            providerCallAuthorizationReceiptId,
            UserOperationProviderCallExecutionState.Ambiguous)
    {
        AmbiguityReceiptId = ambiguityReceiptId;
        AmbiguityRecordedAtUtc = ambiguityRecordedAtUtc;
    }

    public Guid AmbiguityReceiptId { get; }

    public DateTimeOffset AmbiguityRecordedAtUtc { get; }

    public static UserOperationProviderCallAmbiguousReceipt Create(
        Guid attemptId,
        Guid invocationId,
        Guid gatewayStartReceiptId,
        Guid providerCallAuthorizationReceiptId,
        Guid ambiguityReceiptId,
        DateTimeOffset ambiguityRecordedAtUtc)
    {
        UserOperationInvocationValidation.RequireIdentifier(attemptId, nameof(attemptId));
        UserOperationInvocationValidation.RequireIdentifier(invocationId, nameof(invocationId));
        UserOperationInvocationValidation.RequireIdentifier(
            gatewayStartReceiptId,
            nameof(gatewayStartReceiptId));
        UserOperationInvocationValidation.RequireIdentifier(
            providerCallAuthorizationReceiptId,
            nameof(providerCallAuthorizationReceiptId));
        UserOperationInvocationValidation.RequireIdentifier(
            ambiguityReceiptId,
            nameof(ambiguityReceiptId));
        UserOperationInvocationValidation.RequireUtcMicrosecond(
            ambiguityRecordedAtUtc,
            nameof(ambiguityRecordedAtUtc));
        return new UserOperationProviderCallAmbiguousReceipt(
            attemptId,
            invocationId,
            gatewayStartReceiptId,
            providerCallAuthorizationReceiptId,
            ambiguityReceiptId,
            ambiguityRecordedAtUtc);
    }

    public override string ToString() =>
        $"UserOperationProviderCallAmbiguousReceipt {{ AttemptId = {AttemptId:D}, InvocationId = {InvocationId:D}, ProviderCallAuthorizationReceiptId = {ProviderCallAuthorizationReceiptId:D}, AmbiguityReceiptId = {AmbiguityReceiptId:D}, State = {State}, AmbiguityRecordedAtUtc = {AmbiguityRecordedAtUtc:O} }}";
}

/// <summary>Consumes the one-use observation bearer after authorization.</summary>
public sealed class UserOperationGatewayObservationRequest
{
    private UserOperationGatewayObservationRequest(
        Guid attemptId,
        Guid invocationId,
        Guid gatewayStartReceiptId,
        Guid providerCallAuthorizationReceiptId,
        UserOperationBearer gatewayObservationReceiptBearer,
        UserOperationObservationOutcome outcome,
        UserOperationTargetObservation targetObservation,
        DateTimeOffset observedAtUtc)
    {
        AttemptId = attemptId;
        InvocationId = invocationId;
        GatewayStartReceiptId = gatewayStartReceiptId;
        ProviderCallAuthorizationReceiptId = providerCallAuthorizationReceiptId;
        GatewayObservationReceiptBearer = gatewayObservationReceiptBearer;
        Outcome = outcome;
        TargetObservation = targetObservation;
        ObservationSha256 = targetObservation.ComputeCanonicalSha256();
        ObservedAtUtc = observedAtUtc;
    }

    public Guid AttemptId { get; }

    public Guid InvocationId { get; }

    public Guid GatewayStartReceiptId { get; }

    public Guid ProviderCallAuthorizationReceiptId { get; }

    public UserOperationBearer GatewayObservationReceiptBearer { get; }

    public UserOperationObservationOutcome Outcome { get; }

    public UserOperationTargetObservation TargetObservation { get; }

    public string ObservationSha256 { get; }

    public DateTimeOffset ObservedAtUtc { get; }

    public static UserOperationGatewayObservationRequest Create(
        Guid attemptId,
        Guid invocationId,
        Guid gatewayStartReceiptId,
        Guid providerCallAuthorizationReceiptId,
        UserOperationBearer gatewayObservationReceiptBearer,
        UserOperationObservationOutcome outcome,
        UserOperationTargetObservation targetObservation,
        DateTimeOffset observedAtUtc)
    {
        UserOperationInvocationValidation.RequireIdentifier(attemptId, nameof(attemptId));
        UserOperationInvocationValidation.RequireIdentifier(invocationId, nameof(invocationId));
        UserOperationInvocationValidation.RequireIdentifier(
            gatewayStartReceiptId,
            nameof(gatewayStartReceiptId));
        UserOperationInvocationValidation.RequireIdentifier(
            providerCallAuthorizationReceiptId,
            nameof(providerCallAuthorizationReceiptId));
        ArgumentNullException.ThrowIfNull(gatewayObservationReceiptBearer);
        UserOperationInvocationValidation.RequireOutcome(outcome);
        ArgumentNullException.ThrowIfNull(targetObservation);
        UserOperationInvocationValidation.RequireUtcMicrosecond(observedAtUtc, nameof(observedAtUtc));
        return new UserOperationGatewayObservationRequest(
            attemptId,
            invocationId,
            gatewayStartReceiptId,
            providerCallAuthorizationReceiptId,
            gatewayObservationReceiptBearer,
            outcome,
            targetObservation,
            observedAtUtc);
    }

    public override string ToString() =>
        $"UserOperationGatewayObservationRequest {{ AttemptId = {AttemptId:D}, InvocationId = {InvocationId:D}, Outcome = {Outcome}, GatewayObservationReceiptBearer = [REDACTED] }}";
}

public sealed class UserOperationGatewayObservationReceipt
{
    private UserOperationGatewayObservationReceipt(
        Guid attemptId,
        Guid invocationId,
        Guid gatewayObservationReceiptId,
        Guid providerCallAuthorizationReceiptId,
        UserOperationObservationOutcome outcome,
        UserOperationTargetObservation targetObservation,
        string observationReceiptSha256,
        DateTimeOffset observedAtUtc)
    {
        AttemptId = attemptId;
        InvocationId = invocationId;
        GatewayObservationReceiptId = gatewayObservationReceiptId;
        ProviderCallAuthorizationReceiptId = providerCallAuthorizationReceiptId;
        Outcome = outcome;
        TargetObservation = targetObservation;
        ObservationSha256 = targetObservation.ComputeCanonicalSha256();
        ObservationReceiptSha256 = observationReceiptSha256;
        ObservedAtUtc = observedAtUtc;
    }

    public Guid AttemptId { get; }

    public Guid InvocationId { get; }

    public Guid GatewayObservationReceiptId { get; }

    public Guid ProviderCallAuthorizationReceiptId { get; }

    public UserOperationObservationOutcome Outcome { get; }

    public UserOperationTargetObservation TargetObservation { get; }

    public string ObservationSha256 { get; }

    public string ObservationReceiptSha256 { get; }

    public DateTimeOffset ObservedAtUtc { get; }

    public static UserOperationGatewayObservationReceipt Create(
        Guid attemptId,
        Guid invocationId,
        Guid gatewayObservationReceiptId,
        Guid providerCallAuthorizationReceiptId,
        UserOperationObservationOutcome outcome,
        UserOperationTargetObservation targetObservation,
        string observationReceiptSha256,
        DateTimeOffset observedAtUtc)
    {
        UserOperationInvocationValidation.RequireIdentifier(attemptId, nameof(attemptId));
        UserOperationInvocationValidation.RequireIdentifier(invocationId, nameof(invocationId));
        UserOperationInvocationValidation.RequireIdentifier(
            gatewayObservationReceiptId,
            nameof(gatewayObservationReceiptId));
        UserOperationInvocationValidation.RequireIdentifier(
            providerCallAuthorizationReceiptId,
            nameof(providerCallAuthorizationReceiptId));
        UserOperationInvocationValidation.RequireOutcome(outcome);
        ArgumentNullException.ThrowIfNull(targetObservation);
        UserOperationInvocationValidation.RequireSha256(
            observationReceiptSha256,
            nameof(observationReceiptSha256));
        UserOperationInvocationValidation.RequireUtcMicrosecond(observedAtUtc, nameof(observedAtUtc));
        return new UserOperationGatewayObservationReceipt(
            attemptId,
            invocationId,
            gatewayObservationReceiptId,
            providerCallAuthorizationReceiptId,
            outcome,
            targetObservation,
            observationReceiptSha256,
            observedAtUtc);
    }

    public override string ToString() =>
        $"UserOperationGatewayObservationReceipt {{ AttemptId = {AttemptId:D}, InvocationId = {InvocationId:D}, GatewayObservationReceiptId = {GatewayObservationReceiptId:D}, Outcome = {Outcome}, TargetType = {TargetObservation.TargetType}, ObservationSha256 = [REDACTED], ObservationReceiptSha256 = [REDACTED], ObservedAtUtc = {ObservedAtUtc:O} }}";
}

public sealed record UserOperationResultV5Acceptance(Guid ResultId, string State);

public interface IUserOperationSupervisorDeliveryApplication
{
    Task<UserOperationGatewayDeliveryClaim> ClaimForGatewayAsync(
        WorkloadActor actor,
        UserOperationSupervisorDeliveryClaimRequest request,
        RequestMetadata metadata,
        CancellationToken cancellationToken);

    Task<UserOperationGatewayRejectBeforeBeginReceipt> RejectBeforeBeginAsync(
        WorkloadActor actor,
        UserOperationGatewayRejectBeforeBeginRequest request,
        RequestMetadata metadata,
        CancellationToken cancellationToken);
}

public interface IUserOperationGatewayBeginApplication
{
    /// <exception cref="UserOperationAuthorityAlreadyCommittedException">
    /// The exact begin identity was already committed and its one-shot
    /// bearers cannot be returned or recreated. The caller must not retry.
    /// </exception>
    Task<UserOperationGatewayBeginAuthority> BeginAsync(
        WorkloadActor actor,
        UserOperationGatewayBeginRequest request,
        RequestMetadata metadata,
        CancellationToken cancellationToken);
}

public interface IUserOperationCredentialBoundaryApplication
{
    /// <summary>
    /// Internally commits the first prepared-to-authorized transition, closes
    /// that transaction, and then owns exactly one provider call. The database
    /// authorization primitive remains an adapter implementation detail: no
    /// caller callback, executable credential, or reusable grant crosses this
    /// seam. An exact replay, including a retry after a lost response, fails
    /// closed without a second call. A post-authorization failure returns a
    /// durable <see cref="UserOperationProviderCallAmbiguousReceipt"/> when it
    /// can be acknowledged; it is never exposed as a retryable failed outcome.
    /// If the caller receives an exception, that exception grants no retry
    /// authority and a replay must still fail closed.
    /// </summary>
    /// <exception cref="UserOperationAuthorityAlreadyCommittedException">
    /// The provider authorization was already committed and cannot be
    /// reissued. The provider is not called by this invocation.
    /// </exception>
    Task<UserOperationProviderCallExecutionReceipt> ExecuteProviderCallOnceAsync(
        WorkloadActor actor,
        UserOperationProviderCallExecutionRequest request,
        RequestMetadata metadata,
        CancellationToken cancellationToken);
}

public interface IUserOperationGatewayObservationApplication
{
    Task<UserOperationGatewayObservationReceipt> RecordObservationAsync(
        WorkloadActor actor,
        UserOperationGatewayObservationRequest request,
        RequestMetadata metadata,
        CancellationToken cancellationToken);
}

public interface IUserOperationResultV5Application
{
    Task<UserOperationResultV5Acceptance> RecordGatewayResultAsync(
        WorkloadActor actor,
        UserOperationGatewayResultV5 request,
        RequestMetadata metadata,
        CancellationToken cancellationToken);

    Task<UserOperationResultV5Acceptance> RecordReconciliationResultAsync(
        WorkloadActor actor,
        UserOperationReconciliationResultV5 request,
        RequestMetadata metadata,
        CancellationToken cancellationToken);
}

public sealed class UnavailableUserOperationSupervisorDeliveryApplication
    : IUserOperationSupervisorDeliveryApplication
{
    public Task<UserOperationGatewayDeliveryClaim> ClaimForGatewayAsync(
        WorkloadActor actor,
        UserOperationSupervisorDeliveryClaimRequest request,
        RequestMetadata metadata,
        CancellationToken cancellationToken) =>
        Task.FromException<UserOperationGatewayDeliveryClaim>(
            new BackendCapabilityUnavailableException("user_operation_supervisor_delivery_postgres"));

    public Task<UserOperationGatewayRejectBeforeBeginReceipt> RejectBeforeBeginAsync(
        WorkloadActor actor,
        UserOperationGatewayRejectBeforeBeginRequest request,
        RequestMetadata metadata,
        CancellationToken cancellationToken) =>
        Task.FromException<UserOperationGatewayRejectBeforeBeginReceipt>(Unavailable());

    private static BackendCapabilityUnavailableException Unavailable() =>
        new("user_operation_supervisor_delivery_postgres");
}

public sealed class UnavailableUserOperationGatewayBeginApplication
    : IUserOperationGatewayBeginApplication
{
    public Task<UserOperationGatewayBeginAuthority> BeginAsync(
        WorkloadActor actor,
        UserOperationGatewayBeginRequest request,
        RequestMetadata metadata,
        CancellationToken cancellationToken) =>
        Task.FromException<UserOperationGatewayBeginAuthority>(Unavailable());

    private static BackendCapabilityUnavailableException Unavailable() =>
        new("user_operation_gateway_begin_postgres");
}

public sealed class UnavailableUserOperationCredentialBoundaryApplication
    : IUserOperationCredentialBoundaryApplication
{
    public Task<UserOperationProviderCallExecutionReceipt> ExecuteProviderCallOnceAsync(
        WorkloadActor actor,
        UserOperationProviderCallExecutionRequest request,
        RequestMetadata metadata,
        CancellationToken cancellationToken) =>
        Task.FromException<UserOperationProviderCallExecutionReceipt>(
            new BackendCapabilityUnavailableException("user_operation_provider_call_boundary"));
}

public sealed class UnavailableUserOperationGatewayObservationApplication
    : IUserOperationGatewayObservationApplication
{
    public Task<UserOperationGatewayObservationReceipt> RecordObservationAsync(
        WorkloadActor actor,
        UserOperationGatewayObservationRequest request,
        RequestMetadata metadata,
        CancellationToken cancellationToken) =>
        Task.FromException<UserOperationGatewayObservationReceipt>(
            new BackendCapabilityUnavailableException("user_operation_gateway_observation_postgres"));
}

public sealed class UnavailableUserOperationResultV5Application : IUserOperationResultV5Application
{
    public Task<UserOperationResultV5Acceptance> RecordGatewayResultAsync(
        WorkloadActor actor,
        UserOperationGatewayResultV5 request,
        RequestMetadata metadata,
        CancellationToken cancellationToken) => Unavailable();

    public Task<UserOperationResultV5Acceptance> RecordReconciliationResultAsync(
        WorkloadActor actor,
        UserOperationReconciliationResultV5 request,
        RequestMetadata metadata,
        CancellationToken cancellationToken) => Unavailable();

    private static Task<UserOperationResultV5Acceptance> Unavailable() =>
        Task.FromException<UserOperationResultV5Acceptance>(
            new BackendCapabilityUnavailableException("user_operation_result_v5_postgres"));
}

internal static class UserOperationInvocationValidation
{
    public static void RequireIdentifier(Guid value, string name)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty identifier is required.", name);
        }
    }

    public static void RequireUtcMicrosecond(DateTimeOffset value, string name)
    {
        if (value == default || value.Offset != TimeSpan.Zero || value.Ticks % 10 != 0)
        {
            throw new ArgumentException(
                "A non-default UTC timestamp with microsecond precision is required.",
                name);
        }
    }

    public static void RequireWindow(
        DateTimeOffset issuedAtUtc,
        DateTimeOffset expiresAtUtc,
        string issuedName,
        string expiresName)
    {
        RequireUtcMicrosecond(issuedAtUtc, issuedName);
        RequireUtcMicrosecond(expiresAtUtc, expiresName);
        if (expiresAtUtc <= issuedAtUtc)
        {
            throw new ArgumentException("The exclusive authorization window is inconsistent.");
        }
    }

    public static void RequireDistinctBearers(
        UserOperationBearer first,
        UserOperationBearer second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        byte[] firstBytes = Encoding.ASCII.GetBytes(first.DangerousGetValue());
        byte[] secondBytes = Encoding.ASCII.GetBytes(second.DangerousGetValue());
        try
        {
            if (CryptographicOperations.FixedTimeEquals(firstBytes, secondBytes))
            {
                throw new ArgumentException("Protocol bearers must be independently generated.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(firstBytes);
            CryptographicOperations.ZeroMemory(secondBytes);
        }
    }

    public static void RequireCode(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        if (value.Length > 64
            || value.Any(static character =>
                character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-')))
        {
            throw new ArgumentException("A canonical reason code is required.", name);
        }
    }

    public static void RequireSha256(string value, string name)
    {
        ArgumentNullException.ThrowIfNull(value, name);
        if (value.Length != 64
            || value.Any(static character =>
                character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new ArgumentException("A lowercase SHA-256 digest is required.", name);
        }
    }

    public static void RequireOutcome(UserOperationObservationOutcome outcome)
    {
        if (outcome is not (UserOperationObservationOutcome.Succeeded
            or UserOperationObservationOutcome.Diverged))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }
    }
}
