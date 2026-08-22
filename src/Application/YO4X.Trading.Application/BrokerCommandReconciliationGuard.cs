using YO4X.Runtime.Contracts;
using YO4X.Tenancy;
using YO4X.Trading.Abstractions;

namespace YO4X.Trading.Application;

internal static class BrokerCommandReconciliationGuard
{
    public static string? RejectReason(
        TenantExecutionContext context,
        BrokerCommandReference reference,
        BrokerCommandReconciliationClaim claim,
        IExecutionLeaseTrustVerifier leaseTrustVerifier,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(claim.Command);
        ArgumentNullException.ThrowIfNull(leaseTrustVerifier);

        AuthorizedBrokerCommand capability = claim.Command;
        NormalizedBrokerCommand command = capability.Command;
        SignedExecutionLease lease = capability.ExecutionLease.Lease;
        ExecutionLeaseClaims leaseClaims = lease.Claims;

        if (now.Offset != TimeSpan.Zero
            || command.CreatedAtUtc.Offset != TimeSpan.Zero
            || claim.QueryWindowStartUtc.Offset != TimeSpan.Zero
            || claim.StartedAtUtc.Offset != TimeSpan.Zero
            || claim.MustBeginByUtc.Offset != TimeSpan.Zero
            || claim.MustCompleteByUtc.Offset != TimeSpan.Zero
            || claim.AuthorityNowUtc.Offset != TimeSpan.Zero
            || claim.ClaimExpiresAtUtc.Offset != TimeSpan.Zero
            || claim.QueryWindowStartUtc < command.CreatedAtUtc
            || claim.QueryWindowStartUtc > claim.StartedAtUtc
            || claim.StartedAtUtc > now
            || claim.StartedAtUtc > claim.MustBeginByUtc
            || claim.AuthorityNowUtc > now
            || claim.AuthorityNowUtc >= claim.ClaimExpiresAtUtc
            || claim.ClaimExpiresAtUtc <= now
            || claim.ClaimExpiresAtUtc > claim.MustCompleteByUtc
            || claim.MustCompleteByUtc <= now)
        {
            return "broker_reconciliation_deadline_invalid";
        }

        if (claim.ClaimToken == Guid.Empty
            || claim.Attempt <= 0
            || claim.CommandVersion <= 0
            || capability.Reconciliation.CommandId != command.CommandId
            || context.CorrelationId != command.CommandId
            || context.TenantId != capability.Provenance.TenantId
            || context.ActorId != leaseClaims.Binding.GatewayHostWorkloadId
            || reference.CommandId != command.CommandId
            || !BrokerCommandReference.DigestEquals(
                reference.AuthorizationSha256,
                capability.AuthorizationSha256)
            || !BrokerCommandReference.DigestEquals(
                reference.ExecutionLeaseTokenSha256,
                capability.ExecutionLease.LeaseTokenSha256))
        {
            return "broker_reconciliation_binding_invalid";
        }

        if (!BrokerCommandReference.DigestEquals(
                claim.ScopeSha256,
                capability.Reconciliation.ScopeSha256)
            || claim.MustBeginByUtc != capability.Reconciliation.MustBeginByUtc
            || claim.MustCompleteByUtc != capability.Reconciliation.MustCompleteByUtc)
        {
            return "broker_reconciliation_scope_invalid";
        }

        if (leaseClaims.Binding.ExecutionMode != ExecutionMode.CloudDemo
            || claim.SendDisposition is not ("accepted" or "unknown")
            || !ValidCode(claim.SendResultCode)
            || !ValidOptionalBrokerId(claim.BrokerRequestId)
            || !ValidOptionalBrokerId(claim.BrokerOrderId)
            || !ValidOptionalBrokerId(claim.BrokerDealId)
            || (claim.SendDisposition == "accepted"
                && claim.BrokerRequestId is null
                && claim.BrokerOrderId is null
                && claim.BrokerDealId is null)
            || (command.Action is BrokerCommandAction.Cancel
                    or BrokerCommandAction.ModifyProtection
                && claim.BrokerDealId is not null)
            || (command.TargetKind == BrokerCommandTargetKind.PendingOrder
                && command.Action is BrokerCommandAction.Cancel
                    or BrokerCommandAction.ModifyProtection
                && claim.BrokerOrderId is not null
                && claim.BrokerOrderId != command.TargetBrokerId))
        {
            return "broker_reconciliation_submission_binding_invalid";
        }

        ExecutionLeaseTrustVerification trust = leaseTrustVerifier.Verify(lease);
        if (!trust.IsTrusted
            || trust.TrustedVerificationKeySha256 is null
            || trust.SignatureAlgorithm != lease.SignatureAlgorithm
            || trust.SigningKeyId != lease.SigningKeyId
            || !BrokerCommandReference.DigestEquals(
                trust.TrustedVerificationKeySha256,
                capability.ExecutionLease.TrustedVerificationKeySha256))
        {
            return "broker_reconciliation_lease_signature_untrusted";
        }

        if (!capability.Provenance.StrategySignatureCryptographicallyVerified
            || capability.Provenance.StrategyVerificationSignatureAlgorithm !=
                "ECDSA_P256_SHA256_DER"
            || capability.Provenance.StrategyVerifiedByWorkloadId == Guid.Empty
            || capability.Provenance.StrategyVerifiedAtUtc.Offset != TimeSpan.Zero
            || capability.Provenance.StrategyVerifiedAtUtc > now)
        {
            return "broker_reconciliation_strategy_signature_not_proven";
        }

        return null;
    }

    private static bool ValidCode(string? value) =>
        value is { Length: >= 1 and <= 200 }
        && value == value.Trim()
        && value.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '_' or '-' or '.' or ':');

    private static bool ValidOptionalBrokerId(string? value) =>
        value is null || value is { Length: >= 1 and <= 200 } && value == value.Trim();
}
