using System.Text.RegularExpressions;
using YO4X.BuildingBlocks;
using YO4X.Runtime.Contracts;

namespace YO4X.Trading.Abstractions;

public static class BrokerCommandAuthorizationContractVersions
{
    public const int AuthorizationV1 = 1;
    public const int ExposureSnapshotV1 = 1;
    public const int ReconciliationV1 = 1;
}

public sealed record BrokerCommandProvenance(
    Guid TenantId,
    Guid BrokerAccountId,
    Guid StrategyId,
    Guid StrategyVersionId,
    int StrategyVersion,
    string StrategyPackageSha256,
    Guid StrategySourceBindingId,
    Guid SourceCorpusId,
    string SourceCorpusSha256,
    string SourceManifestSha256,
    string SourceReportSha256,
    string CompiledArtifactSha256,
    string CompilerArtifactSha256,
    string ParseTypecheckProofSha256,
    string CompileProofSha256,
    string SemanticConversionProofSha256,
    string ReferenceParityProofSha256,
    string DemoRuntimeProofSha256,
    string StrategyVerificationEvidenceSha256,
    string StrategyVerificationSignatureSha256,
    string StrategyVerificationSignatureAlgorithm,
    string StrategyVerificationSigningKeyId,
    Guid StrategyVerifiedByWorkloadId,
    DateTimeOffset StrategyVerifiedAtUtc,
    bool StrategySignatureCryptographicallyVerified,
    Guid GatewayArtifactId,
    string GatewayArtifactSha256);

public sealed record NumericRiskAuthorization(
    Guid DecisionId,
    Guid PolicyVersionId,
    string PolicySha256,
    string ActionClass,
    string InputSha256,
    string DecisionSha256,
    DateTimeOffset EvaluatedAtUtc,
    bool IsAllowed);

public sealed record BrokerExposureAuthorization(
    int ContractVersion,
    Guid SnapshotId,
    string SnapshotSha256,
    string SourceKind,
    long SourceSequence,
    string SourceEvidenceSha256,
    DateTimeOffset OldestObservedAtUtc,
    DateTimeOffset ReceivedAtUtc,
    DateTimeOffset ValidUntilUtc);

public sealed record ExecutionLeaseAuthorization(
    SignedExecutionLease Lease,
    string LeaseTokenSha256,
    string LeasePayloadSha256,
    string LeaseSignatureSha256,
    string TrustedVerificationKeySha256);

public sealed record ExecutionSafetyAuthorization(
    string EffectiveOverlaySha256,
    long PolicyVersionWatermark);

public sealed record BrokerReconciliationCommitment(
    int ContractVersion,
    Guid CommandId,
    string Method,
    string ScopeSha256,
    DateTimeOffset MustBeginByUtc,
    DateTimeOffset MustCompleteByUtc,
    string CommitmentSha256);

public sealed record BrokerCommandAuthorizationDocument(
    int ContractVersion,
    Guid TenantId,
    Guid BrokerAccountId,
    Guid CommandId,
    Guid IntentId,
    Guid DeploymentId,
    long Generation,
    int CommandContractVersion,
    string CommandSha256,
    string IdempotencyKey,
    Guid StrategyId,
    Guid StrategyVersionId,
    int StrategyVersion,
    string StrategyPackageSha256,
    Guid StrategySourceBindingId,
    Guid SourceCorpusId,
    string SourceCorpusSha256,
    string SourceManifestSha256,
    string SourceReportSha256,
    string CompiledArtifactSha256,
    string CompilerArtifactSha256,
    string ParseTypecheckProofSha256,
    string CompileProofSha256,
    string SemanticConversionProofSha256,
    string ReferenceParityProofSha256,
    string DemoRuntimeProofSha256,
    string StrategyVerificationEvidenceSha256,
    string StrategyVerificationSignatureSha256,
    string StrategyVerificationSignatureAlgorithm,
    string StrategyVerificationSigningKeyId,
    Guid StrategyVerifiedByWorkloadId,
    DateTimeOffset StrategyVerifiedAtUtc,
    bool StrategySignatureCryptographicallyVerified,
    Guid GatewayArtifactId,
    string GatewayArtifactSha256,
    Guid ExposureSnapshotId,
    string ExposureSnapshotSha256,
    string ExposureSourceKind,
    long ExposureSourceSequence,
    string ExposureSourceEvidenceSha256,
    Guid RiskDecisionId,
    Guid RiskPolicyVersionId,
    string RiskPolicySha256,
    string RiskActionClass,
    string RiskInputSha256,
    string RiskDecisionSha256,
    string ExecutionSafetyOverlaySha256,
    long ExecutionSafetyPolicyVersionWatermark,
    Guid ExecutionLeaseId,
    string ExecutionLeaseTokenSha256,
    string ExecutionLeasePayloadSha256,
    string ExecutionLeaseSignatureSha256,
    string ExecutionLeaseSignatureAlgorithm,
    string ExecutionLeaseSigningKeyId,
    string ExecutionLeaseTrustedVerificationKeySha256,
    DateTimeOffset ExecutionLeaseExpiresAtUtc,
    int ReconciliationContractVersion,
    string ReconciliationMethod,
    string ReconciliationScopeSha256,
    DateTimeOffset ReconciliationMustBeginByUtc,
    DateTimeOffset ReconciliationMustCompleteByUtc,
    string ReconciliationCommitmentSha256);

/// <summary>
/// The only value accepted by an order-mutating gateway call. It binds the
/// normalized command to durable provenance, numeric risk, fresh exposure,
/// signed lease, and reconciliation evidence. Signature trust is verified by
/// the lease issuer; this type independently verifies identity and digests.
/// </summary>
public sealed record AuthorizedBrokerCommand
{
    private static readonly Regex LowerSha256 = new(
        "^[0-9a-f]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(100));

    private AuthorizedBrokerCommand(
        NormalizedBrokerCommand command,
        BrokerCommandProvenance provenance,
        NumericRiskAuthorization risk,
        BrokerExposureAuthorization exposure,
        ExecutionSafetyAuthorization executionSafety,
        ExecutionLeaseAuthorization executionLease,
        BrokerReconciliationCommitment reconciliation,
        string authorizationSha256)
    {
        Command = command;
        Provenance = provenance;
        Risk = risk;
        Exposure = exposure;
        ExecutionSafety = executionSafety;
        ExecutionLease = executionLease;
        Reconciliation = reconciliation;
        AuthorizationSha256 = authorizationSha256;
    }

    public NormalizedBrokerCommand Command { get; }

    public BrokerCommandProvenance Provenance { get; }

    public NumericRiskAuthorization Risk { get; }

    public BrokerExposureAuthorization Exposure { get; }

    public ExecutionSafetyAuthorization ExecutionSafety { get; }

    public ExecutionLeaseAuthorization ExecutionLease { get; }

    public BrokerReconciliationCommitment Reconciliation { get; }

    public string AuthorizationSha256 { get; }

    internal static AuthorizedBrokerCommand Create(
        NormalizedBrokerCommand command,
        BrokerCommandProvenance provenance,
        NumericRiskAuthorization risk,
        BrokerExposureAuthorization exposure,
        ExecutionSafetyAuthorization executionSafety,
        SignedExecutionLease lease,
        string trustedLeaseVerificationKeySha256,
        BrokerReconciliationCommitment reconciliation,
        string persistedAuthorizationSha256)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentNullException.ThrowIfNull(risk);
        ArgumentNullException.ThrowIfNull(exposure);
        ArgumentNullException.ThrowIfNull(executionSafety);
        ArgumentNullException.ThrowIfNull(lease);
        RequireDigest(
            trustedLeaseVerificationKeySha256,
            nameof(trustedLeaseVerificationKeySha256));
        ArgumentNullException.ThrowIfNull(reconciliation);

        string leaseTokenSha256 = ExecutionLeaseEnvelopeDigest.Sha256(lease);
        string leaseSignatureSha256 = ExecutionLeaseEnvelopeDigest.SignatureSha256(lease);
        var leaseAuthorization = new ExecutionLeaseAuthorization(
            lease,
            leaseTokenSha256,
            lease.PayloadSha256,
            leaseSignatureSha256,
            trustedLeaseVerificationKeySha256);
        ValidateBindings(
            command,
            provenance,
            risk,
            exposure,
            executionSafety,
            leaseAuthorization,
            reconciliation);

        BrokerCommandAuthorizationDocument document = CreateDocument(
            command,
            provenance,
            risk,
            exposure,
            executionSafety,
            leaseAuthorization,
            reconciliation);
        string computed = CanonicalJson.Sha256(document);
        RequireDigest(persistedAuthorizationSha256, nameof(persistedAuthorizationSha256));
        if (!FixedTimeDigestEquals(computed, persistedAuthorizationSha256))
        {
            throw new DomainException(
                "BROKER_COMMAND_AUTHORIZATION_DIGEST_MISMATCH",
                "The durable broker-command authorization digest does not match the exact envelope.");
        }

        return new AuthorizedBrokerCommand(
            command,
            provenance,
            risk,
            exposure,
            executionSafety,
            leaseAuthorization,
            reconciliation,
            computed);
    }

    public static BrokerCommandAuthorizationDocument CreateDocument(
        NormalizedBrokerCommand command,
        BrokerCommandProvenance provenance,
        NumericRiskAuthorization risk,
        BrokerExposureAuthorization exposure,
        ExecutionSafetyAuthorization executionSafety,
        ExecutionLeaseAuthorization executionLease,
        BrokerReconciliationCommitment reconciliation)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentNullException.ThrowIfNull(risk);
        ArgumentNullException.ThrowIfNull(exposure);
        ArgumentNullException.ThrowIfNull(executionSafety);
        ArgumentNullException.ThrowIfNull(executionLease);
        ArgumentNullException.ThrowIfNull(reconciliation);

        return new BrokerCommandAuthorizationDocument(
            BrokerCommandAuthorizationContractVersions.AuthorizationV1,
            provenance.TenantId,
            provenance.BrokerAccountId,
            command.CommandId,
            command.IntentId,
            command.DeploymentId,
            command.Generation,
            command.ContractVersion,
            CanonicalJson.Sha256(command),
            command.IdempotencyKey,
            provenance.StrategyId,
            provenance.StrategyVersionId,
            provenance.StrategyVersion,
            provenance.StrategyPackageSha256,
            provenance.StrategySourceBindingId,
            provenance.SourceCorpusId,
            provenance.SourceCorpusSha256,
            provenance.SourceManifestSha256,
            provenance.SourceReportSha256,
            provenance.CompiledArtifactSha256,
            provenance.CompilerArtifactSha256,
            provenance.ParseTypecheckProofSha256,
            provenance.CompileProofSha256,
            provenance.SemanticConversionProofSha256,
            provenance.ReferenceParityProofSha256,
            provenance.DemoRuntimeProofSha256,
            provenance.StrategyVerificationEvidenceSha256,
            provenance.StrategyVerificationSignatureSha256,
            provenance.StrategyVerificationSignatureAlgorithm,
            provenance.StrategyVerificationSigningKeyId,
            provenance.StrategyVerifiedByWorkloadId,
            provenance.StrategyVerifiedAtUtc.ToUniversalTime(),
            provenance.StrategySignatureCryptographicallyVerified,
            provenance.GatewayArtifactId,
            provenance.GatewayArtifactSha256,
            exposure.SnapshotId,
            exposure.SnapshotSha256,
            exposure.SourceKind,
            exposure.SourceSequence,
            exposure.SourceEvidenceSha256,
            risk.DecisionId,
            risk.PolicyVersionId,
            risk.PolicySha256,
            risk.ActionClass,
            risk.InputSha256,
            risk.DecisionSha256,
            executionSafety.EffectiveOverlaySha256,
            executionSafety.PolicyVersionWatermark,
            executionLease.Lease.Claims.LeaseId,
            executionLease.LeaseTokenSha256,
            executionLease.LeasePayloadSha256,
            executionLease.LeaseSignatureSha256,
            executionLease.Lease.SignatureAlgorithm,
            executionLease.Lease.SigningKeyId,
            executionLease.TrustedVerificationKeySha256,
            executionLease.Lease.Claims.ExpiresAtUtc.ToUniversalTime(),
            reconciliation.ContractVersion,
            reconciliation.Method,
            reconciliation.ScopeSha256,
            reconciliation.MustBeginByUtc.ToUniversalTime(),
            reconciliation.MustCompleteByUtc.ToUniversalTime(),
            reconciliation.CommitmentSha256);
    }

    private static void ValidateBindings(
        NormalizedBrokerCommand command,
        BrokerCommandProvenance provenance,
        NumericRiskAuthorization risk,
        BrokerExposureAuthorization exposure,
        ExecutionSafetyAuthorization executionSafety,
        ExecutionLeaseAuthorization executionLease,
        BrokerReconciliationCommitment reconciliation)
    {
        ExecutionLeaseClaims claims = executionLease.Lease.Claims;
        ExecutionLeaseBinding binding = claims.Binding;
        if (command.ContractVersion <= 0
            || command.CommandId == Guid.Empty
            || command.IntentId == Guid.Empty
            || command.DeploymentId == Guid.Empty
            || command.Generation <= 0
            || string.IsNullOrWhiteSpace(command.IdempotencyKey)
            || command.IdempotencyKey.Length > 200
            || provenance.TenantId == Guid.Empty
            || provenance.BrokerAccountId == Guid.Empty
            || provenance.StrategyId == Guid.Empty
            || provenance.StrategyVersionId == Guid.Empty
            || provenance.StrategyVersion <= 0
            || provenance.StrategySourceBindingId == Guid.Empty
            || provenance.SourceCorpusId == Guid.Empty
            || provenance.GatewayArtifactId == Guid.Empty
            || risk.DecisionId == Guid.Empty
            || risk.PolicyVersionId == Guid.Empty
            || exposure.ContractVersion != BrokerCommandAuthorizationContractVersions.ExposureSnapshotV1
            || exposure.SnapshotId == Guid.Empty
            || exposure.SourceSequence <= 0
            || executionSafety.PolicyVersionWatermark < 0
            || reconciliation.ContractVersion != BrokerCommandAuthorizationContractVersions.ReconciliationV1
            || reconciliation.CommandId != command.CommandId
            || claims.ContractVersion <= 0
            || claims.LeaseId == Guid.Empty)
        {
            throw InvalidBinding();
        }

        RequireDigest(provenance.StrategyPackageSha256, nameof(provenance.StrategyPackageSha256));
        RequireDigest(provenance.SourceCorpusSha256, nameof(provenance.SourceCorpusSha256));
        RequireDigest(provenance.SourceManifestSha256, nameof(provenance.SourceManifestSha256));
        RequireDigest(provenance.SourceReportSha256, nameof(provenance.SourceReportSha256));
        RequireDigest(provenance.CompiledArtifactSha256, nameof(provenance.CompiledArtifactSha256));
        RequireDigest(provenance.CompilerArtifactSha256, nameof(provenance.CompilerArtifactSha256));
        RequireDigest(provenance.ParseTypecheckProofSha256, nameof(provenance.ParseTypecheckProofSha256));
        RequireDigest(provenance.CompileProofSha256, nameof(provenance.CompileProofSha256));
        RequireDigest(
            provenance.SemanticConversionProofSha256,
            nameof(provenance.SemanticConversionProofSha256));
        RequireDigest(
            provenance.ReferenceParityProofSha256,
            nameof(provenance.ReferenceParityProofSha256));
        RequireDigest(provenance.DemoRuntimeProofSha256, nameof(provenance.DemoRuntimeProofSha256));
        RequireDigest(
            provenance.StrategyVerificationEvidenceSha256,
            nameof(provenance.StrategyVerificationEvidenceSha256));
        RequireDigest(
            provenance.StrategyVerificationSignatureSha256,
            nameof(provenance.StrategyVerificationSignatureSha256));
        if (string.IsNullOrWhiteSpace(provenance.StrategyVerificationSigningKeyId)
            || provenance.StrategyVerificationSigningKeyId.Length > 500
            || !string.Equals(
                provenance.StrategyVerificationSigningKeyId,
                provenance.StrategyVerificationSigningKeyId.Trim(),
                StringComparison.Ordinal))
        {
            throw InvalidBinding();
        }
        if (provenance.StrategyVerificationSignatureAlgorithm !=
                "ECDSA_P256_SHA256_DER"
            || provenance.StrategyVerifiedByWorkloadId == Guid.Empty
            || provenance.StrategyVerifiedAtUtc.Offset != TimeSpan.Zero
            || !provenance.StrategySignatureCryptographicallyVerified)
        {
            throw InvalidBinding();
        }
        RequireDigest(provenance.GatewayArtifactSha256, nameof(provenance.GatewayArtifactSha256));
        RequireDigest(risk.PolicySha256, nameof(risk.PolicySha256));
        RequireDigest(risk.InputSha256, nameof(risk.InputSha256));
        RequireDigest(risk.DecisionSha256, nameof(risk.DecisionSha256));
        RequireDigest(exposure.SnapshotSha256, nameof(exposure.SnapshotSha256));
        RequireDigest(exposure.SourceEvidenceSha256, nameof(exposure.SourceEvidenceSha256));
        RequireDigest(
            executionSafety.EffectiveOverlaySha256,
            nameof(executionSafety.EffectiveOverlaySha256));
        RequireDigest(executionLease.LeaseTokenSha256, nameof(executionLease.LeaseTokenSha256));
        RequireDigest(executionLease.LeasePayloadSha256, nameof(executionLease.LeasePayloadSha256));
        RequireDigest(executionLease.LeaseSignatureSha256, nameof(executionLease.LeaseSignatureSha256));
        RequireDigest(
            executionLease.TrustedVerificationKeySha256,
            nameof(executionLease.TrustedVerificationKeySha256));
        RequireDigest(reconciliation.ScopeSha256, nameof(reconciliation.ScopeSha256));
        RequireDigest(reconciliation.CommitmentSha256, nameof(reconciliation.CommitmentSha256));

        if (!risk.IsAllowed
            || !RiskActionMatches(command.Action, risk.ActionClass)
            || !HasValidTargetShape(command)
            || binding.TenantId != provenance.TenantId
            || binding.DeploymentId != command.DeploymentId
            || binding.BrokerAccountId != provenance.BrokerAccountId
            || binding.StrategyId != provenance.StrategyId
            || binding.StrategyVersionId != provenance.StrategyVersionId
            || binding.StrategyVersion != provenance.StrategyVersion
            || !FixedTimeDigestEquals(binding.StrategyPackageSha256, provenance.StrategyPackageSha256)
            || binding.SafetyPolicyVersionId != risk.PolicyVersionId
            || !FixedTimeDigestEquals(binding.SafetyPolicySha256, risk.PolicySha256)
            || binding.Generation != command.Generation
            || claims.ExpiresAtUtc.Offset != TimeSpan.Zero
            || risk.EvaluatedAtUtc.Offset != TimeSpan.Zero
            || exposure.OldestObservedAtUtc.Offset != TimeSpan.Zero
            || exposure.ReceivedAtUtc.Offset != TimeSpan.Zero
            || exposure.ValidUntilUtc.Offset != TimeSpan.Zero
            || reconciliation.MustBeginByUtc.Offset != TimeSpan.Zero
            || reconciliation.MustCompleteByUtc.Offset != TimeSpan.Zero
            || exposure.OldestObservedAtUtc > risk.EvaluatedAtUtc
            || risk.EvaluatedAtUtc > exposure.ValidUntilUtc
            || exposure.ReceivedAtUtc > exposure.ValidUntilUtc
            || reconciliation.MustBeginByUtc > reconciliation.MustCompleteByUtc
            || reconciliation.MustCompleteByUtc > claims.GraceExpiresAtUtc
            || !FixedTimeDigestEquals(executionLease.LeasePayloadSha256, executionLease.Lease.PayloadSha256)
            || !FixedTimeDigestEquals(
                executionLease.LeaseTokenSha256,
                ExecutionLeaseEnvelopeDigest.Sha256(executionLease.Lease))
            || !FixedTimeDigestEquals(
                executionLease.LeaseSignatureSha256,
                ExecutionLeaseEnvelopeDigest.SignatureSha256(executionLease.Lease)))
        {
            throw InvalidBinding();
        }
    }

    private static bool HasValidTargetShape(NormalizedBrokerCommand command) => command.Action switch
    {
        BrokerCommandAction.Place =>
            command.TargetKind is null
            && command.TargetBrokerId is null
            && command.ExpectedTargetVolume is null
            && command.ExpectedTargetStatus is null
            && command.ExpectedTargetStopLoss is null
            && command.ExpectedTargetTakeProfit is null,
        BrokerCommandAction.ModifyProtection =>
            command.TargetKind is BrokerCommandTargetKind.Position or BrokerCommandTargetKind.PendingOrder
            && HasOpaqueTarget(command)
            && command.ExpectedTargetVolume is > 0,
        BrokerCommandAction.Cancel =>
            command.TargetKind == BrokerCommandTargetKind.PendingOrder
            && HasOpaqueTarget(command)
            && command.ExpectedTargetVolume is > 0
            && !string.IsNullOrWhiteSpace(command.ExpectedTargetStatus),
        BrokerCommandAction.Close =>
            command.TargetKind == BrokerCommandTargetKind.Position
            && HasOpaqueTarget(command)
            && command.ExpectedTargetVolume is > 0
            && command.Volume <= command.ExpectedTargetVolume,
        _ => false
    };

    private static bool HasOpaqueTarget(NormalizedBrokerCommand command) =>
        !string.IsNullOrWhiteSpace(command.TargetBrokerId)
        && command.TargetBrokerId.Length <= 200
        && string.Equals(
            command.TargetBrokerId,
            command.TargetBrokerId.Trim(),
            StringComparison.Ordinal);

    private static bool RiskActionMatches(BrokerCommandAction action, string riskAction) => action switch
    {
        BrokerCommandAction.Place => string.Equals(
            riskAction,
            "exposure_increase",
            StringComparison.Ordinal),
        BrokerCommandAction.ModifyProtection => string.Equals(
            riskAction,
            "protection",
            StringComparison.Ordinal),
        BrokerCommandAction.Cancel => string.Equals(
            riskAction,
            "pending_order_cancellation",
            StringComparison.Ordinal),
        BrokerCommandAction.Close => riskAction is "exposure_reduction" or "emergency_close",
        _ => throw InvalidBinding()
    };

    private static void RequireDigest(string? digest, string parameterName)
    {
        if (digest is null || !LowerSha256.IsMatch(digest))
        {
            throw new ArgumentException("A lowercase SHA-256 digest is required.", parameterName);
        }
    }

    private static bool FixedTimeDigestEquals(string left, string right) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(left),
            System.Text.Encoding.ASCII.GetBytes(right));

    private static DomainException InvalidBinding() => new(
        "BROKER_COMMAND_AUTHORIZATION_BINDING_INVALID",
        "The broker-command authorization envelope is incomplete or internally inconsistent.");
}
