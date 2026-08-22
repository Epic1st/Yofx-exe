using YO4X.Runtime.Contracts;
using YO4X.RuntimeOperations;

namespace YO4X.Runtime.Tests;

public sealed class OwnershipLeaseAndReadinessTests
{
    private static readonly Guid DeploymentId = Guid.Parse("60000000-0000-0000-0000-000000000001");
    private static readonly Guid AccountId = Guid.Parse("61000000-0000-0000-0000-000000000001");
    private static readonly Guid SupervisorId = Guid.Parse("62000000-0000-0000-0000-000000000001");
    private static readonly Guid ReplacementId = Guid.Parse("62000000-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewGenerationIsDeniedUntilExpiryPlusSafetyInterval()
    {
        var ownership = new WorkerOwnershipStateMachine(DeploymentId, AccountId);
        OwnershipAcquireResult first = ownership.TryAcquire(
            SupervisorId,
            Now,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromSeconds(30));

        OwnershipAcquireResult earlyReplacement = ownership.TryAcquire(
            ReplacementId,
            Now.AddMinutes(10).AddSeconds(29),
            TimeSpan.FromMinutes(10),
            TimeSpan.FromSeconds(30));
        OwnershipAcquireResult safeReplacement = ownership.TryAcquire(
            ReplacementId,
            Now.AddMinutes(10).AddSeconds(30),
            TimeSpan.FromMinutes(10),
            TimeSpan.FromSeconds(30));

        Assert.True(first.Acquired);
        Assert.Equal(OwnershipAcquireCode.LeaseExpirySafetyWindowActive, earlyReplacement.Code);
        Assert.True(safeReplacement.Acquired);
        Assert.Equal(2, safeReplacement.Snapshot.Generation);
    }

    [Fact]
    public void AcknowledgedReleaseAllowsImmediateNextGeneration()
    {
        var ownership = new WorkerOwnershipStateMachine(DeploymentId, AccountId);
        OwnershipAcquireResult first = ownership.TryAcquire(
            SupervisorId,
            Now,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromSeconds(30));

        OwnershipReleaseResult released = ownership.AcknowledgeRelease(
            SupervisorId,
            first.Snapshot.Generation,
            Now.AddSeconds(5));
        OwnershipAcquireResult replacement = ownership.TryAcquire(
            ReplacementId,
            Now.AddSeconds(5),
            TimeSpan.FromMinutes(10),
            TimeSpan.FromSeconds(30));

        Assert.True(released.Released);
        Assert.True(replacement.Acquired);
        Assert.Equal(2, replacement.Snapshot.Generation);
        Assert.False(ownership.IsFenceValid(1, SupervisorId, Now.AddSeconds(6)));
    }

    [Fact]
    public void OnlyCurrentHolderCanRenewCurrentGenerationBeforeExpiry()
    {
        var ownership = new WorkerOwnershipStateMachine(DeploymentId, AccountId);
        WorkerOwnershipSnapshot initial = ownership.TryAcquire(
            SupervisorId,
            Now,
            TimeSpan.FromMinutes(10),
            TimeSpan.Zero).Snapshot;

        OwnershipRenewResult wrongHolder = ownership.TryRenew(
            ReplacementId,
            initial.Generation,
            Now.AddMinutes(1),
            TimeSpan.FromMinutes(10));
        OwnershipRenewResult renewed = ownership.TryRenew(
            SupervisorId,
            initial.Generation,
            Now.AddMinutes(1),
            TimeSpan.FromMinutes(10));

        Assert.Equal(OwnershipRenewCode.WrongHolder, wrongHolder.Code);
        Assert.True(renewed.Renewed);
        Assert.Equal(Now.AddMinutes(11), renewed.Snapshot.ExpiresAtUtc);
        Assert.Equal(initial.Generation, renewed.Snapshot.Generation);
    }

    [Fact]
    public void LeaseMustMatchSignedOwnerGenerationAndAction()
    {
        var ownership = new WorkerOwnershipStateMachine(DeploymentId, AccountId);
        WorkerOwnershipSnapshot snapshot = ownership.TryAcquire(
            SupervisorId,
            Now,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromSeconds(30)).Snapshot;
        SignedExecutionLease lease = Lease(snapshot.Generation, LeaseActionClass.Reduce | LeaseActionClass.Protect);

        ExecutionLeaseValidation valid = ExecutionLeaseRules.Validate(
            lease,
            signatureIsValid: true,
            lease.Claims.Binding,
            snapshot,
            LeaseActionClass.Reduce,
            Now.AddMinutes(1));
        ExecutionLeaseValidation increase = ExecutionLeaseRules.Validate(
            lease,
            signatureIsValid: true,
            lease.Claims.Binding,
            snapshot,
            LeaseActionClass.Increase,
            Now.AddMinutes(1));
        ExecutionLeaseValidation unsigned = ExecutionLeaseRules.Validate(
            lease,
            signatureIsValid: false,
            lease.Claims.Binding,
            snapshot,
            LeaseActionClass.Reduce,
            Now.AddMinutes(1));

        Assert.True(valid.IsValid);
        Assert.Equal(ExecutionLeaseValidationCode.ActionNotPermitted, increase.Code);
        Assert.Equal(ExecutionLeaseValidationCode.InvalidSignature, unsigned.Code);
    }

    [Fact]
    public void ReadinessRequiresFreshFencedEvidenceFromAllThreeComponents()
    {
        var ownership = new WorkerOwnershipStateMachine(DeploymentId, AccountId);
        WorkerOwnershipSnapshot snapshot = ownership.TryAcquire(
            SupervisorId,
            Now,
            TimeSpan.FromMinutes(10),
            TimeSpan.Zero).Snapshot;
        var evaluator = new RuntimeReadinessEvaluator(
            TimeSpan.FromSeconds(45),
            TimeSpan.FromSeconds(5));

        RuntimeReadinessDecision ready = evaluator.Evaluate(
            DeploymentId,
            snapshot.Generation,
            snapshot,
            CompleteEvidence(snapshot.Generation),
            Now.AddSeconds(10));
        RuntimeReadinessDecision incomplete = evaluator.Evaluate(
            DeploymentId,
            snapshot.Generation,
            snapshot,
            CompleteEvidence(snapshot.Generation).Take(2),
            Now.AddSeconds(10));

        Assert.True(ready.IsReady);
        Assert.False(incomplete.IsReady);
        Assert.Equal("runtime_component_evidence_incomplete", incomplete.ReasonCode);
    }

    [Fact]
    public void MismatchedOrStaleComponentEvidenceFailsClosed()
    {
        var ownership = new WorkerOwnershipStateMachine(DeploymentId, AccountId);
        WorkerOwnershipSnapshot snapshot = ownership.TryAcquire(
            SupervisorId,
            Now,
            TimeSpan.FromMinutes(10),
            TimeSpan.Zero).Snapshot;
        var evaluator = new RuntimeReadinessEvaluator(
            TimeSpan.FromSeconds(45),
            TimeSpan.FromSeconds(5));
        RuntimeComponentEvidence[] wrongGeneration = CompleteEvidence(snapshot.Generation);
        wrongGeneration[2] = wrongGeneration[2] with { Generation = snapshot.Generation + 1 };

        RuntimeReadinessDecision fenced = evaluator.Evaluate(
            DeploymentId,
            snapshot.Generation,
            snapshot,
            wrongGeneration,
            Now.AddSeconds(10));
        RuntimeReadinessDecision stale = evaluator.Evaluate(
            DeploymentId,
            snapshot.Generation,
            snapshot,
            CompleteEvidence(snapshot.Generation),
            Now.AddMinutes(1));

        Assert.Equal("runtime_component_fenced", fenced.ReasonCode);
        Assert.Equal("runtime_component_evidence_stale", stale.ReasonCode);
    }

    [Fact]
    public void TamperedComponentEvidenceHashFailsClosed()
    {
        var ownership = new WorkerOwnershipStateMachine(DeploymentId, AccountId);
        WorkerOwnershipSnapshot snapshot = ownership.TryAcquire(
            SupervisorId,
            Now,
            TimeSpan.FromMinutes(10),
            TimeSpan.Zero).Snapshot;
        var evaluator = new RuntimeReadinessEvaluator(
            TimeSpan.FromSeconds(45),
            TimeSpan.FromSeconds(5));
        RuntimeComponentEvidence[] evidence = CompleteEvidence(snapshot.Generation);
        evidence[1] = evidence[1] with { LastAcceptedSequence = 10 };

        RuntimeReadinessDecision decision = evaluator.Evaluate(
            DeploymentId,
            snapshot.Generation,
            snapshot,
            evidence,
            Now.AddSeconds(10));

        Assert.Equal("runtime_component_evidence_invalid", decision.ReasonCode);
    }

    private static SignedExecutionLease Lease(long generation, LeaseActionClass actions)
    {
        var binding = new ExecutionLeaseBinding(
            Guid.Parse("64000000-0000-0000-0000-000000000001"),
            Guid.Parse("64000000-0000-0000-0000-000000000002"),
            Guid.Parse("64000000-0000-0000-0000-000000000003"),
            DeploymentId,
            AccountId,
            new string('a', 64),
            Guid.Parse("64000000-0000-0000-0000-000000000004"),
            Guid.Parse("64000000-0000-0000-0000-000000000005"),
            1,
            new string('b', 64),
            ExecutionMode.CloudDemo,
            Guid.Parse("64000000-0000-0000-0000-000000000006"),
            new string('c', 64),
            Guid.Parse("64000000-0000-0000-0000-000000000007"),
            SupervisorId,
            Guid.Parse("64000000-0000-0000-0000-000000000008"),
            Guid.Parse("64000000-0000-0000-0000-000000000009"),
            Guid.Parse("64000000-0000-0000-0000-00000000000a"),
            generation,
            "region-1");
        var claims = new ExecutionLeaseClaims(
            RuntimeContractVersions.ExecutionLeaseV1,
            Guid.Parse("63000000-0000-0000-0000-000000000001"),
            binding,
            Now,
            Now,
            Now.AddMinutes(10),
            Now.AddMinutes(15),
            new ExecutionLeaseActionPolicy(
                actions,
                LeaseActionClass.Reduce | LeaseActionClass.Protect,
                LeaseActionClass.Reduce | LeaseActionClass.Protect,
                LeaseActionClass.Reduce | LeaseActionClass.Protect));
        return new SignedExecutionLease(
            claims,
            ExecutionLeaseCanonicalizer.Sha256(claims),
            "EdDSA",
            "u0-test-key",
            new string('A', 43));
    }

    private static RuntimeComponentEvidence[] CompleteEvidence(long generation) =>
    [
        Evidence(RuntimeComponentRole.Supervisor, SupervisorId, generation),
        Evidence(
            RuntimeComponentRole.StrategyHost,
            Guid.Parse("62000000-0000-0000-0000-000000000003"),
            generation),
        Evidence(
            RuntimeComponentRole.GatewayHost,
            Guid.Parse("62000000-0000-0000-0000-000000000004"),
            generation)
    ];

    private static RuntimeComponentEvidence Evidence(
        RuntimeComponentRole role,
        Guid workerInstanceId,
        long generation) =>
        RuntimeComponentEvidenceFactory.Create(
            role,
            DeploymentId,
            workerInstanceId,
            generation,
            0,
            RuntimeComponentState.Ready,
            FenceEvidenceState.Valid,
            Now,
            Now.AddSeconds(5));
}
