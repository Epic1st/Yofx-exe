using YO4X.Risk;
using YO4X.Runtime.Contracts;
using YO4X.Trading.Abstractions;

namespace YO4X.Trading.Postgres;

public sealed record BrokerExposureSnapshotDocument(
    int ContractVersion,
    Guid SnapshotId,
    Guid TenantId,
    Guid BrokerAccountId,
    Guid DeploymentId,
    long Generation,
    Guid WorkerAssignmentId,
    Guid WorkerInstanceId,
    Guid GatewayArtifactId,
    string GatewayArtifactSha256,
    string SourceKind,
    long SourceSequence,
    string SourceEvidenceSha256,
    DateTimeOffset QuoteAsOfUtc,
    DateTimeOffset AccountAsOfUtc,
    DateTimeOffset PositionAsOfUtc,
    DateTimeOffset OrderAsOfUtc,
    DateTimeOffset SymbolAsOfUtc,
    DateTimeOffset ConversionRateAsOfUtc,
    DateTimeOffset RiskDayAsOfUtc,
    DateTimeOffset OrderRateAsOfUtc,
    BrokerAccountSnapshot Account,
    IReadOnlyList<BrokerQuoteSnapshot> Quotes,
    IReadOnlyList<BrokerPositionSnapshot> Positions,
    IReadOnlyList<BrokerOrderSnapshot> Orders,
    IReadOnlyList<BrokerDealSnapshot> Deals);

public sealed record BrokerReconciliationCommitmentDocument(
    int ContractVersion,
    Guid CommandId,
    string Method,
    string ScopeSha256,
    DateTimeOffset MustBeginByUtc,
    DateTimeOffset MustCompleteByUtc);

public sealed record BrokerCommandAuthorizationRequest(
    NormalizedBrokerCommand Command,
    BrokerCommandProvenance Provenance,
    BrokerExposureSnapshotDocument Exposure,
    NumericRiskEvaluationInput RiskInput,
    NumericRiskDecision RiskDecision,
    SignedExecutionLease ExecutionLease,
    ExecutionSafetyAuthorization ExecutionSafety,
    BrokerReconciliationCommitmentDocument Reconciliation,
    Guid RiskDecisionId,
    Guid AuditEventId);

public sealed record BrokerCommandAuthorizationReceipt(
    BrokerCommandAuthorizationRequest Request,
    string AuthorizationSha256,
    string ExecutionSafetyOverlaySha256,
    long ExecutionSafetyPolicyVersionWatermark,
    string ExposureSnapshotSha256,
    DateTimeOffset ExposureReceivedAtUtc,
    DateTimeOffset ExposureValidUntilUtc,
    string RiskInputSha256,
    string RiskDecisionSha256,
    DateTimeOffset AuthorizationExpiresAtUtc,
    long CommandVersion,
    DateTimeOffset AuthorizedAtUtc,
    bool Replayed);

public sealed record BrokerCommandDispatchClaim(
    AuthorizedBrokerCommand Command,
    Guid ClaimToken,
    DateTimeOffset AuthorityNowUtc,
    DateTimeOffset ClaimExpiresAtUtc,
    long CommandVersion,
    bool Replayed);

public sealed record BrokerCommandDispatchReference(
    Guid CommandId,
    string AuthorizationSha256,
    string ExecutionLeaseTokenSha256);

public sealed record BrokerCommandReconciliationClaim(
    AuthorizedBrokerCommand Command,
    Guid ClaimToken,
    string ScopeSha256,
    DateTimeOffset MustBeginByUtc,
    DateTimeOffset MustCompleteByUtc,
    DateTimeOffset AuthorityNowUtc,
    DateTimeOffset ClaimExpiresAtUtc,
    int Attempt,
    string? SendDisposition,
    string? SendResultCode,
    string? BrokerRequestId,
    string? BrokerOrderId,
    string? BrokerDealId,
    long CommandVersion,
    DateTimeOffset QueryWindowStartUtc,
    DateTimeOffset StartedAtUtc,
    bool Replayed);

public sealed record BrokerCommandMutationReceipt(
    Guid CommandId,
    string State,
    string EvidenceSha256,
    long CommandVersion,
    DateTimeOffset RecordedAtUtc,
    bool Replayed);

public sealed record BrokerGatewaySubmissionDocument(
    string Disposition,
    string Code,
    string? BrokerRequestId,
    string? OrderId,
    string? DealId,
    DateTimeOffset ObservedAtUtc,
    bool PreInvocationNotSentProven);

internal sealed record BrokerCommandReconciliationEvidenceDocument(
    Guid CommandId,
    string AuthorizationSha256,
    string ScopeSha256,
    Guid BrokerAccountId,
    Guid DeploymentId,
    long Generation,
    BrokerCommandTargetKind? TargetKind,
    string? TargetBrokerId,
    string OwnershipTag,
    long? SourceSequence,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    string Match,
    string ReasonCode,
    string SourceEvidenceSha256,
    string? OrderId,
    string? DealId,
    DateTimeOffset ObservedAtUtc,
    BrokerReconciliationSnapshot? Snapshot);
