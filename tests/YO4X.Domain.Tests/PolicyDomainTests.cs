using YO4X.BuildingBlocks;
using YO4X.Policy;

namespace YO4X.Domain.Tests;

public sealed class PolicyDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MeetIsCommutativeForGeneratedVectors()
    {
        var random = new Random(94721);

        for (int index = 0; index < 250; index++)
        {
            ExecutionSafetyPolicyVector left = NextVector(random);
            ExecutionSafetyPolicyVector right = NextVector(random);

            Assert.Equal(left.Meet(right), right.Meet(left));
        }
    }

    [Fact]
    public void MeetIsAssociativeForGeneratedVectors()
    {
        var random = new Random(31877);

        for (int index = 0; index < 250; index++)
        {
            ExecutionSafetyPolicyVector first = NextVector(random);
            ExecutionSafetyPolicyVector second = NextVector(random);
            ExecutionSafetyPolicyVector third = NextVector(random);

            Assert.Equal(
                first.Meet(second).Meet(third),
                first.Meet(second.Meet(third)));
        }
    }

    [Fact]
    public void MeetIsIdempotentAndMonotonicallyRestrictive()
    {
        var random = new Random(7623);

        for (int index = 0; index < 250; index++)
        {
            ExecutionSafetyPolicyVector baseline = NextVector(random);
            ExecutionSafetyPolicyVector restriction = NextVector(random);
            ExecutionSafetyPolicyVector effective = baseline.Meet(restriction);

            Assert.Equal(baseline, baseline.Meet(baseline));
            Assert.True(effective.IsAtLeastAsRestrictiveAs(baseline));
            Assert.True(effective.IsAtLeastAsRestrictiveAs(restriction));
        }
    }

    [Fact]
    public void MeetUsesRestrictiveEnumOrdersAndWorkerActionUnion()
    {
        ExecutionSafetyPolicyVector first = CreateVector(
            leaseMode: LeaseMode.RenewRestricted,
            workerActions: WorkerAction.Drain,
            credentialMode: CredentialMode.Normal,
            packageEligibility: PackageEligibility.Quarantined);
        ExecutionSafetyPolicyVector second = CreateVector(
            leaseMode: LeaseMode.Revoke,
            workerActions: WorkerAction.Fence,
            credentialMode: CredentialMode.DisableNewUse,
            packageEligibility: PackageEligibility.NoNewAssignment);

        ExecutionSafetyPolicyVector effective = first.Meet(second);

        Assert.Equal(LeaseMode.Revoke, effective.LeaseMode);
        Assert.Equal(CredentialMode.DisableNewUse, effective.CredentialMode);
        Assert.Equal(PackageEligibility.Quarantined, effective.PackageEligibility);
        Assert.Equal(WorkerAction.Drain | WorkerAction.Fence, effective.WorkerActions);
    }

    [Fact]
    public void MeetRejectsAnEmptyPolicySet()
    {
        DomainException exception = Assert.Throws<DomainException>(() =>
            ExecutionSafetyPolicyVector.Meet(Array.Empty<ExecutionSafetyPolicyVector>()));

        Assert.Equal("POLICY_VECTOR_SET_EMPTY", exception.Code);
    }

    [Fact]
    public void DigestIsStableAndChangesWhenAuthorityChanges()
    {
        ExecutionSafetyPolicyVector baseline = CreateVector();
        ExecutionSafetyPolicyVector same = CreateVector();
        ExecutionSafetyPolicyVector restricted = CreateVector(allowExposureIncrease: false);

        Assert.Equal(baseline.ComputeDigest(), same.ComputeDigest());
        Assert.NotEqual(baseline.ComputeDigest(), restricted.ComputeDigest());
    }

    [Fact]
    public void WorkerPlannerProducesDeterministicSafeSequence()
    {
        ExecutionSafetyPolicyVector vector = CreateVector(
            workerActions: WorkerAction.Replace
                | WorkerAction.StopAfterFlat
                | WorkerAction.Fence
                | WorkerAction.Drain);

        WorkerActionPlan plan = WorkerActionPlanner.Plan(
            vector,
            new WorkerActionPlanningContext(
                AccountConfirmedFlat: true,
                ProtectedReductionPathAvailable: false));

        Assert.Equal(WorkerActionPlanDisposition.Ready, plan.Disposition);
        Assert.Equal(
            [
                WorkerAction.Drain,
                WorkerAction.StopAfterFlat,
                WorkerAction.Fence,
                WorkerAction.Replace
            ],
            plan.Steps);
    }

    [Fact]
    public void WorkerPlannerFailsClosedWhenFenceConflictsWithStopAfterFlat()
    {
        ExecutionSafetyPolicyVector vector = CreateVector(
            workerActions: WorkerAction.Fence | WorkerAction.StopAfterFlat);

        WorkerActionPlan plan = WorkerActionPlanner.Plan(
            vector,
            new WorkerActionPlanningContext(
                AccountConfirmedFlat: false,
                ProtectedReductionPathAvailable: false));

        Assert.Equal(WorkerActionPlanDisposition.ReviewRequired, plan.Disposition);
        Assert.Contains(plan.Issues, issue => issue.Code == "FENCE_CONFLICTS_WITH_STOP_AFTER_FLAT");
        Assert.False(plan.CanExecuteAutomatically);
    }

    [Fact]
    public void WorkerPlannerRequiresReconciliationWhenFenceWouldRemoveReductionPath()
    {
        ExecutionSafetyPolicyVector vector = CreateVector(workerActions: WorkerAction.Fence);

        WorkerActionPlan plan = WorkerActionPlanner.Plan(
            vector,
            new WorkerActionPlanningContext(
                AccountConfirmedFlat: false,
                ProtectedReductionPathAvailable: false));

        Assert.Equal(WorkerActionPlanDisposition.ReconciliationRequired, plan.Disposition);
        Assert.Contains(plan.Issues, issue => issue.Code == "FENCE_REQUIRES_PROTECTED_REDUCTION_PATH");
    }

    [Fact]
    public void WorkerPlannerRequiresExplicitFenceForReplacement()
    {
        WorkerActionPlan plan = WorkerActionPlanner.Plan(
            CreateVector(workerActions: WorkerAction.Replace),
            new WorkerActionPlanningContext(
                AccountConfirmedFlat: true,
                ProtectedReductionPathAvailable: true));

        Assert.Equal(WorkerActionPlanDisposition.ReviewRequired, plan.Disposition);
        Assert.Contains(plan.Issues, issue => issue.Code == "WORKER_REPLACE_REQUIRES_FENCE");
    }

    [Fact]
    public void ContainmentExpiryRequiresReviewAndNeverAutoReleases()
    {
        ContainmentPolicy policy = ContainmentPolicy.Activate(
            Identifiers.NewId(),
            CreateVector(allowExposureIncrease: false),
            Now,
            Now.AddMinutes(10));

        Assert.True(policy.IsReviewDue(Now.AddMinutes(11)));
        Assert.Equal(ContainmentPolicyState.Active, policy.State);

        policy.RequireExpiryReview(Now.AddMinutes(11));

        Assert.Equal(ContainmentPolicyState.ExpiryReviewRequired, policy.State);
        Assert.NotEqual(ContainmentPolicyState.Inactive, policy.State);
    }

    [Fact]
    public void ContainmentReleaseRequiresEveryGovernedTransition()
    {
        ContainmentPolicy policy = ContainmentPolicy.Activate(
            Identifiers.NewId(),
            CreateVector(allowNewDeployment: false),
            Now);

        Assert.Throws<DomainException>(() => policy.BeginDeactivation(Now.AddMinutes(1)));

        policy.ApproveRelease(
            CanonicalJson.Sha256(new { Preview = 1 }),
            CanonicalJson.Sha256(new { Approval = 1 }),
            Now.AddMinutes(1));
        policy.BeginDeactivation(Now.AddMinutes(2));
        policy.BeginReconciliation(Now.AddMinutes(3));
        policy.CompleteRelease(Now.AddMinutes(4));

        Assert.Equal(ContainmentPolicyState.Inactive, policy.State);
        Assert.Equal(4, policy.Version);
    }

    [Fact]
    public void ContainmentCannotBeActivatedWithAnUnrestrictedVector()
    {
        DomainException exception = Assert.Throws<DomainException>(() =>
            ContainmentPolicy.Activate(
                Identifiers.NewId(),
                ExecutionSafetyPolicyVector.Unrestricted,
                Now));

        Assert.Equal("CONTAINMENT_POLICY_NOT_RESTRICTIVE", exception.Code);
    }

    private static ExecutionSafetyPolicyVector NextVector(Random random) => new(
        random.Next(2) == 1,
        random.Next(2) == 1,
        random.Next(2) == 1,
        random.Next(2) == 1,
        random.Next(2) == 1,
        random.Next(2) == 1,
        random.Next(2) == 1,
        (LeaseMode)random.Next(3),
        (WorkerAction)random.Next(16),
        (CredentialMode)random.Next(3),
        (PackageEligibility)random.Next(3));

    private static ExecutionSafetyPolicyVector CreateVector(
        bool allowNewDeployment = true,
        bool allowStrategySignals = true,
        bool allowExposureIncrease = true,
        bool allowExposureReduction = true,
        bool allowProtection = true,
        bool allowPendingOrderCancellation = true,
        bool allowEmergencyClose = true,
        LeaseMode leaseMode = LeaseMode.Normal,
        WorkerAction workerActions = WorkerAction.None,
        CredentialMode credentialMode = CredentialMode.Normal,
        PackageEligibility packageEligibility = PackageEligibility.Eligible) => new(
            allowNewDeployment,
            allowStrategySignals,
            allowExposureIncrease,
            allowExposureReduction,
            allowProtection,
            allowPendingOrderCancellation,
            allowEmergencyClose,
            leaseMode,
            workerActions,
            credentialMode,
            packageEligibility);
}
