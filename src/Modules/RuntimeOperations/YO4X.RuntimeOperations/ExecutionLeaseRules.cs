using System.Security.Cryptography;
using YO4X.Runtime.Contracts;

namespace YO4X.RuntimeOperations;

public static class ExecutionLeaseRules
{
    private const LeaseActionClass AllActions =
        LeaseActionClass.Increase
        | LeaseActionClass.Reduce
        | LeaseActionClass.Protect
        | LeaseActionClass.Cancel
        | LeaseActionClass.EmergencyClose;

    public static ExecutionLeaseValidation Validate(
        SignedExecutionLease lease,
        bool signatureIsValid,
        ExecutionLeaseBinding expectedBinding,
        WorkerOwnershipSnapshot ownership,
        LeaseActionClass requestedAction,
        DateTimeOffset nowUtc,
        bool revoked = false)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(expectedBinding);
        ArgumentNullException.ThrowIfNull(ownership);

        ExecutionLeaseClaims claims = lease.Claims;
        if (!signatureIsValid || !HasValidPayloadDigest(lease))
        {
            return Invalid(ExecutionLeaseValidationCode.InvalidSignature, "execution_lease_signature_invalid");
        }

        if (claims.ContractVersion != RuntimeContractVersions.ExecutionLeaseV1)
        {
            return Invalid(ExecutionLeaseValidationCode.UnsupportedVersion, "execution_lease_version_unsupported");
        }

        if (!HasValidShape(lease))
        {
            return Invalid(ExecutionLeaseValidationCode.InvalidIdentity, "execution_lease_shape_invalid");
        }

        ExecutionLeaseBinding binding = claims.Binding;
        if (binding.DeploymentId != expectedBinding.DeploymentId
            || ownership.DeploymentId != expectedBinding.DeploymentId)
        {
            return Invalid(ExecutionLeaseValidationCode.WrongDeployment, "execution_lease_deployment_mismatch");
        }

        if (binding.WorkerInstanceId != expectedBinding.WorkerInstanceId)
        {
            return Invalid(ExecutionLeaseValidationCode.WrongWorker, "execution_lease_worker_mismatch");
        }

        if (binding.Generation != expectedBinding.Generation
            || binding.Generation != ownership.Generation)
        {
            return Invalid(ExecutionLeaseValidationCode.WrongGeneration, "execution_lease_generation_mismatch");
        }

        if (binding != expectedBinding)
        {
            return Invalid(ExecutionLeaseValidationCode.WrongBinding, "execution_lease_binding_mismatch");
        }

        if (ownership.BrokerAccountId != binding.BrokerAccountId
            || ownership.State != WorkerOwnershipState.Held
            || ownership.HolderWorkerInstanceId != binding.WorkerInstanceId)
        {
            return Invalid(ExecutionLeaseValidationCode.OwnershipNotHeld, "execution_lease_ownership_not_held");
        }

        DateTimeOffset normalizedNow = nowUtc.ToUniversalTime();
        if (normalizedNow < claims.NotBeforeUtc)
        {
            return Invalid(ExecutionLeaseValidationCode.NotYetValid, "execution_lease_not_yet_valid");
        }

        LeaseActionClass permitted = revoked
            ? claims.ActionPolicy.Revoked
            : normalizedNow < claims.ExpiresAtUtc
                ? claims.ActionPolicy.Active
                : normalizedNow < claims.GraceExpiresAtUtc
                    ? claims.ActionPolicy.Grace
                    : claims.ActionPolicy.Expired;
        if (requestedAction == LeaseActionClass.None
            || (requestedAction & ~AllActions) != LeaseActionClass.None
            || (permitted & requestedAction) != requestedAction)
        {
            return Invalid(ExecutionLeaseValidationCode.ActionNotPermitted, "execution_lease_action_not_permitted");
        }

        return new ExecutionLeaseValidation(ExecutionLeaseValidationCode.Valid, "execution_lease_valid");
    }

    private static bool HasValidShape(SignedExecutionLease lease)
    {
        ExecutionLeaseClaims claims = lease.Claims;
        ExecutionLeaseBinding binding = claims.Binding;
        ExecutionLeaseActionPolicy policy = claims.ActionPolicy;
        return claims.LeaseId != Guid.Empty
            && binding.TenantId != Guid.Empty
            && binding.EntitlementId != Guid.Empty
            && binding.UserId != Guid.Empty
            && binding.DeploymentId != Guid.Empty
            && binding.BrokerAccountId != Guid.Empty
            && IsSha256(binding.BrokerAccountBindingSha256)
            && binding.StrategyId != Guid.Empty
            && binding.StrategyVersionId != Guid.Empty
            && binding.StrategyVersion > 0
            && IsSha256(binding.StrategyPackageSha256)
            && Enum.IsDefined(binding.ExecutionMode)
            && binding.SafetyPolicyVersionId != Guid.Empty
            && IsSha256(binding.SafetyPolicySha256)
            && binding.WorkerAssignmentId != Guid.Empty
            && binding.WorkerInstanceId != Guid.Empty
            && binding.SupervisorWorkloadId != Guid.Empty
            && binding.StrategyHostWorkloadId != Guid.Empty
            && binding.GatewayHostWorkloadId != Guid.Empty
            && binding.SupervisorWorkloadId != binding.StrategyHostWorkloadId
            && binding.SupervisorWorkloadId != binding.GatewayHostWorkloadId
            && binding.StrategyHostWorkloadId != binding.GatewayHostWorkloadId
            && binding.Generation > 0
            && !string.IsNullOrWhiteSpace(binding.Region)
            && binding.Region.Length <= 100
            && claims.IssuedAtUtc.Offset == TimeSpan.Zero
            && claims.NotBeforeUtc.Offset == TimeSpan.Zero
            && claims.ExpiresAtUtc.Offset == TimeSpan.Zero
            && claims.GraceExpiresAtUtc.Offset == TimeSpan.Zero
            && claims.IssuedAtUtc <= claims.NotBeforeUtc
            && claims.NotBeforeUtc < claims.ExpiresAtUtc
            && claims.ExpiresAtUtc <= claims.GraceExpiresAtUtc
            && IsActionMask(policy.Active)
            && IsActionMask(policy.Grace)
            && IsActionMask(policy.Expired)
            && IsActionMask(policy.Revoked)
            && (policy.Grace & LeaseActionClass.Increase) == 0
            && (policy.Expired & LeaseActionClass.Increase) == 0
            && (policy.Revoked & LeaseActionClass.Increase) == 0
            && IsSha256(lease.PayloadSha256)
            && !string.IsNullOrWhiteSpace(lease.SignatureAlgorithm)
            && lease.SignatureAlgorithm.Length <= 100
            && !string.IsNullOrWhiteSpace(lease.SigningKeyId)
            && lease.SigningKeyId.Length <= 500
            && IsBase64Url(lease.SignatureBase64Url);
    }

    private static bool HasValidPayloadDigest(SignedExecutionLease lease)
    {
        if (!IsSha256(lease.PayloadSha256))
        {
            return false;
        }

        string expected = ExecutionLeaseCanonicalizer.Sha256(lease.Claims);
        byte[] expectedBytes = Convert.FromHexString(expected);
        byte[] actualBytes = Convert.FromHexString(lease.PayloadSha256);
        try
        {
            return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expectedBytes);
            CryptographicOperations.ZeroMemory(actualBytes);
        }
    }

    private static bool IsActionMask(LeaseActionClass actions) =>
        (actions & ~AllActions) == LeaseActionClass.None;

    private static bool IsSha256(string? value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsBase64Url(string? value) => value is { Length: >= 43 and <= 2048 }
        && value.All(character => character is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '-'
            or '_');

    private static ExecutionLeaseValidation Invalid(
        ExecutionLeaseValidationCode code,
        string reasonCode) => new(code, reasonCode);
}
