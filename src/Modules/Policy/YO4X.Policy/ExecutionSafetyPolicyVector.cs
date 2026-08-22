using YO4X.BuildingBlocks;

namespace YO4X.Policy;

public enum LeaseMode
{
    Normal = 0,
    RenewRestricted = 1,
    Revoke = 2
}

public enum CredentialMode
{
    Normal = 0,
    DisableNewUse = 1,
    RevokeReference = 2
}

public enum PackageEligibility
{
    Eligible = 0,
    NoNewAssignment = 1,
    Quarantined = 2
}

[Flags]
public enum WorkerAction
{
    None = 0,
    Drain = 1 << 0,
    Fence = 1 << 1,
    Replace = 1 << 2,
    StopAfterFlat = 1 << 3
}

/// <summary>
/// An immutable execution-safety policy. A meet can only retain or remove
/// authority; it can never grant authority denied by one of its inputs.
/// </summary>
public sealed record ExecutionSafetyPolicyVector
{
    private const WorkerAction KnownWorkerActions =
        WorkerAction.Drain |
        WorkerAction.Fence |
        WorkerAction.Replace |
        WorkerAction.StopAfterFlat;

    public ExecutionSafetyPolicyVector(
        bool allowNewDeployment,
        bool allowStrategySignals,
        bool allowExposureIncrease,
        bool allowExposureReduction,
        bool allowProtection,
        bool allowPendingOrderCancellation,
        bool allowEmergencyClose,
        LeaseMode leaseMode,
        WorkerAction workerActions,
        CredentialMode credentialMode,
        PackageEligibility packageEligibility)
    {
        if (!Enum.IsDefined(leaseMode))
        {
            throw new DomainException("POLICY_LEASE_MODE_UNKNOWN", "The lease mode is unknown.");
        }

        if (!Enum.IsDefined(credentialMode))
        {
            throw new DomainException(
                "POLICY_CREDENTIAL_MODE_UNKNOWN",
                "The credential mode is unknown.");
        }

        if (!Enum.IsDefined(packageEligibility))
        {
            throw new DomainException(
                "POLICY_PACKAGE_ELIGIBILITY_UNKNOWN",
                "The package eligibility is unknown.");
        }

        if ((workerActions & ~KnownWorkerActions) != WorkerAction.None)
        {
            throw new DomainException(
                "POLICY_WORKER_ACTION_UNKNOWN",
                "The policy contains an unknown worker action.");
        }

        AllowNewDeployment = allowNewDeployment;
        AllowStrategySignals = allowStrategySignals;
        AllowExposureIncrease = allowExposureIncrease;
        AllowExposureReduction = allowExposureReduction;
        AllowProtection = allowProtection;
        AllowPendingOrderCancellation = allowPendingOrderCancellation;
        AllowEmergencyClose = allowEmergencyClose;
        LeaseMode = leaseMode;
        WorkerActions = workerActions;
        CredentialMode = credentialMode;
        PackageEligibility = packageEligibility;
    }

    public static ExecutionSafetyPolicyVector Unrestricted { get; } = new(
        allowNewDeployment: true,
        allowStrategySignals: true,
        allowExposureIncrease: true,
        allowExposureReduction: true,
        allowProtection: true,
        allowPendingOrderCancellation: true,
        allowEmergencyClose: true,
        LeaseMode.Normal,
        WorkerAction.None,
        CredentialMode.Normal,
        PackageEligibility.Eligible);

    public bool AllowNewDeployment { get; }

    public bool AllowStrategySignals { get; }

    public bool AllowExposureIncrease { get; }

    public bool AllowExposureReduction { get; }

    public bool AllowProtection { get; }

    public bool AllowPendingOrderCancellation { get; }

    public bool AllowEmergencyClose { get; }

    public LeaseMode LeaseMode { get; }

    public WorkerAction WorkerActions { get; }

    public CredentialMode CredentialMode { get; }

    public PackageEligibility PackageEligibility { get; }

    public ExecutionSafetyPolicyVector Meet(ExecutionSafetyPolicyVector other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return new ExecutionSafetyPolicyVector(
            AllowNewDeployment && other.AllowNewDeployment,
            AllowStrategySignals && other.AllowStrategySignals,
            AllowExposureIncrease && other.AllowExposureIncrease,
            AllowExposureReduction && other.AllowExposureReduction,
            AllowProtection && other.AllowProtection,
            AllowPendingOrderCancellation && other.AllowPendingOrderCancellation,
            AllowEmergencyClose && other.AllowEmergencyClose,
            MostRestrictive(LeaseMode, other.LeaseMode),
            WorkerActions | other.WorkerActions,
            MostRestrictive(CredentialMode, other.CredentialMode),
            MostRestrictive(PackageEligibility, other.PackageEligibility));
    }

    public static ExecutionSafetyPolicyVector Meet(
        IEnumerable<ExecutionSafetyPolicyVector> vectors)
    {
        ArgumentNullException.ThrowIfNull(vectors);

        using IEnumerator<ExecutionSafetyPolicyVector> enumerator = vectors.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            throw new DomainException(
                "POLICY_VECTOR_SET_EMPTY",
                "At least one policy vector is required to calculate an effective policy.");
        }

        ExecutionSafetyPolicyVector result = enumerator.Current
            ?? throw new DomainException("POLICY_VECTOR_NULL", "A policy vector cannot be null.");

        while (enumerator.MoveNext())
        {
            ExecutionSafetyPolicyVector next = enumerator.Current
                ?? throw new DomainException("POLICY_VECTOR_NULL", "A policy vector cannot be null.");
            result = result.Meet(next);
        }

        return result;
    }

    /// <summary>
    /// Returns true when this vector grants no authority that the baseline denies
    /// and contains every restrictive worker action present in the baseline.
    /// </summary>
    public bool IsAtLeastAsRestrictiveAs(ExecutionSafetyPolicyVector baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);

        return IsAtLeastAsRestrictive(AllowNewDeployment, baseline.AllowNewDeployment)
            && IsAtLeastAsRestrictive(AllowStrategySignals, baseline.AllowStrategySignals)
            && IsAtLeastAsRestrictive(AllowExposureIncrease, baseline.AllowExposureIncrease)
            && IsAtLeastAsRestrictive(AllowExposureReduction, baseline.AllowExposureReduction)
            && IsAtLeastAsRestrictive(AllowProtection, baseline.AllowProtection)
            && IsAtLeastAsRestrictive(
                AllowPendingOrderCancellation,
                baseline.AllowPendingOrderCancellation)
            && IsAtLeastAsRestrictive(AllowEmergencyClose, baseline.AllowEmergencyClose)
            && (int)LeaseMode >= (int)baseline.LeaseMode
            && (int)CredentialMode >= (int)baseline.CredentialMode
            && (int)PackageEligibility >= (int)baseline.PackageEligibility
            && (WorkerActions & baseline.WorkerActions) == baseline.WorkerActions;
    }

    public string ComputeDigest() => CanonicalJson.Sha256(new
    {
        AllowNewDeployment,
        AllowStrategySignals,
        AllowExposureIncrease,
        AllowExposureReduction,
        AllowProtection,
        AllowPendingOrderCancellation,
        AllowEmergencyClose,
        LeaseMode = LeaseMode.ToString(),
        WorkerActions = EnumerateWorkerActions().Select(action => action.ToString()).ToArray(),
        CredentialMode = CredentialMode.ToString(),
        PackageEligibility = PackageEligibility.ToString()
    });

    public IReadOnlyList<WorkerAction> EnumerateWorkerActions()
    {
        WorkerAction[] orderedActions =
        [
            WorkerAction.Drain,
            WorkerAction.StopAfterFlat,
            WorkerAction.Fence,
            WorkerAction.Replace
        ];

        WorkerAction[] presentActions = orderedActions
            .Where(action => WorkerActions.HasFlag(action))
            .ToArray();
        return Array.AsReadOnly(presentActions);
    }

    private static bool IsAtLeastAsRestrictive(bool candidate, bool baseline) =>
        !candidate || baseline;

    private static LeaseMode MostRestrictive(LeaseMode left, LeaseMode right) =>
        (int)left >= (int)right ? left : right;

    private static CredentialMode MostRestrictive(CredentialMode left, CredentialMode right) =>
        (int)left >= (int)right ? left : right;

    private static PackageEligibility MostRestrictive(
        PackageEligibility left,
        PackageEligibility right) => (int)left >= (int)right ? left : right;
}
