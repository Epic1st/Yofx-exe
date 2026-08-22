using YO4X.Runtime.Contracts;
using YO4X.Tenancy;
using YO4X.Trading.Abstractions;

namespace YO4X.Trading.Application;

internal static class BrokerCommandDispatchGuard
{
    public static string? RejectReason(
        TenantExecutionContext context,
        BrokerCommandReference reference,
        BrokerCommandDispatchClaim claim,
        IExecutionLeaseTrustVerifier leaseTrustVerifier,
        DateTimeOffset now,
        TimeSpan minimumAuthorityWindow)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(claim.Command);
        ArgumentNullException.ThrowIfNull(leaseTrustVerifier);

        AuthorizedBrokerCommand capability = claim.Command;
        NormalizedBrokerCommand command = capability.Command;
        SignedExecutionLease lease = capability.ExecutionLease.Lease;
        ExecutionLeaseClaims claims = lease.Claims;
        DateTimeOffset deadline = Earliest(
            claim.ClaimExpiresAtUtc,
            capability.Exposure.ValidUntilUtc,
            claims.ExpiresAtUtc,
            capability.Reconciliation.MustBeginByUtc,
            capability.Reconciliation.MustCompleteByUtc);
        if (now.Offset != TimeSpan.Zero
            || claim.ClaimExpiresAtUtc.Offset != TimeSpan.Zero
            || capability.Exposure.ValidUntilUtc.Offset != TimeSpan.Zero
            || claims.NotBeforeUtc.Offset != TimeSpan.Zero
            || claims.ExpiresAtUtc.Offset != TimeSpan.Zero
            || claims.GraceExpiresAtUtc.Offset != TimeSpan.Zero
            || capability.Reconciliation.MustBeginByUtc.Offset != TimeSpan.Zero
            || capability.Reconciliation.MustCompleteByUtc.Offset != TimeSpan.Zero
            || command.CreatedAtUtc.Offset != TimeSpan.Zero)
        {
            return "broker_command_authority_timestamp_invalid";
        }

        if (claim.Replayed)
        {
            return "broker_command_dispatch_claim_replayed";
        }

        if (context.CorrelationId != command.CommandId
            || context.TenantId != capability.Provenance.TenantId
            || context.ActorId != claims.Binding.GatewayHostWorkloadId
            || reference.CommandId != command.CommandId
            || !BrokerCommandReference.DigestEquals(
                reference.AuthorizationSha256,
                capability.AuthorizationSha256)
            || !BrokerCommandReference.DigestEquals(
                reference.ExecutionLeaseTokenSha256,
                capability.ExecutionLease.LeaseTokenSha256))
        {
            return "broker_command_dispatch_binding_invalid";
        }

        if (now < claims.NotBeforeUtc
            || command.CreatedAtUtc > now
            || deadline <= now
            || deadline - now < minimumAuthorityWindow)
        {
            return "broker_command_dispatch_authority_expired";
        }

        if (claims.Binding.ExecutionMode != ExecutionMode.CloudDemo)
        {
            return "broker_command_dispatch_not_demo";
        }

        LeaseActionClass requiredAction = RequiredLeaseAction(capability);
        if ((claims.ActionPolicy.Active & requiredAction) != requiredAction)
        {
            return "broker_command_lease_action_not_allowed";
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
            return "broker_command_lease_signature_untrusted";
        }

        if (!capability.Provenance.StrategySignatureCryptographicallyVerified
            || capability.Provenance.StrategyVerificationSignatureAlgorithm !=
                "ECDSA_P256_SHA256_DER"
            || capability.Provenance.StrategyVerifiedByWorkloadId == Guid.Empty
            || capability.Provenance.StrategyVerifiedAtUtc.Offset != TimeSpan.Zero
            || capability.Provenance.StrategyVerifiedAtUtc > now)
        {
            return "broker_command_strategy_signature_not_proven";
        }

        return null;
    }

    public static TimeSpan RemainingGatewayWindow(
        BrokerCommandDispatchClaim claim,
        DateTimeOffset now,
        TimeSpan configuredTimeout)
    {
        AuthorizedBrokerCommand capability = claim.Command;
        DateTimeOffset deadline = Earliest(
            claim.ClaimExpiresAtUtc,
            capability.Exposure.ValidUntilUtc,
            capability.ExecutionLease.Lease.Claims.ExpiresAtUtc,
            capability.Reconciliation.MustBeginByUtc,
            capability.Reconciliation.MustCompleteByUtc);
        TimeSpan remaining = deadline - now;
        return remaining < configuredTimeout ? remaining : configuredTimeout;
    }

    private static LeaseActionClass RequiredLeaseAction(AuthorizedBrokerCommand capability) =>
        capability.Command.Action switch
        {
            BrokerCommandAction.Place => LeaseActionClass.Increase,
            BrokerCommandAction.ModifyProtection => LeaseActionClass.Protect,
            BrokerCommandAction.Cancel => LeaseActionClass.Cancel,
            BrokerCommandAction.Close when capability.Risk.ActionClass == "emergency_close" =>
                LeaseActionClass.EmergencyClose,
            BrokerCommandAction.Close => LeaseActionClass.Reduce,
            _ => LeaseActionClass.None
        };

    private static DateTimeOffset Earliest(params DateTimeOffset[] values) => values.Min();
}
