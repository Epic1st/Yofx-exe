using YO4X.Runtime.Contracts;

namespace YO4X.RuntimeOperations;

public sealed record RuntimeReadinessDecision(bool IsReady, string ReasonCode)
{
    public static RuntimeReadinessDecision Ready() => new(true, "runtime_ready");

    public static RuntimeReadinessDecision NotReady(string reasonCode) => new(false, reasonCode);
}

public sealed class RuntimeReadinessEvaluator
{
    private static readonly RuntimeComponentRole[] RequiredRoles =
    [
        RuntimeComponentRole.Supervisor,
        RuntimeComponentRole.StrategyHost,
        RuntimeComponentRole.GatewayHost
    ];

    private readonly TimeSpan _maximumEvidenceAge;
    private readonly TimeSpan _maximumFutureClockSkew;

    public RuntimeReadinessEvaluator(TimeSpan maximumEvidenceAge, TimeSpan maximumFutureClockSkew)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumEvidenceAge, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumFutureClockSkew, TimeSpan.Zero);

        _maximumEvidenceAge = maximumEvidenceAge;
        _maximumFutureClockSkew = maximumFutureClockSkew;
    }

    public RuntimeReadinessDecision Evaluate(
        Guid deploymentId,
        long generation,
        WorkerOwnershipSnapshot ownership,
        IEnumerable<RuntimeComponentEvidence> componentEvidence,
        DateTimeOffset nowUtc)
    {
        if (deploymentId == Guid.Empty || generation <= 0)
        {
            return RuntimeReadinessDecision.NotReady("runtime_identity_invalid");
        }

        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(componentEvidence);
        RuntimeComponentEvidence[] evidence = componentEvidence.ToArray();

        if (ownership.DeploymentId != deploymentId
            || ownership.Generation != generation
            || ownership.State != WorkerOwnershipState.Held
            || ownership.HolderWorkerInstanceId is null)
        {
            return RuntimeReadinessDecision.NotReady("runtime_ownership_not_held");
        }

        if (evidence.Length != RequiredRoles.Length)
        {
            return RuntimeReadinessDecision.NotReady("runtime_component_evidence_incomplete");
        }

        if (evidence.Select(value => value.Role).Distinct().Count() != RequiredRoles.Length
            || RequiredRoles.Except(evidence.Select(value => value.Role)).Any())
        {
            return RuntimeReadinessDecision.NotReady("runtime_component_roles_invalid");
        }

        if (evidence.Select(value => value.WorkerInstanceId).Distinct().Count() != RequiredRoles.Length)
        {
            return RuntimeReadinessDecision.NotReady("runtime_component_identity_reused");
        }

        RuntimeComponentEvidence supervisor = evidence.Single(value => value.Role == RuntimeComponentRole.Supervisor);
        if (supervisor.WorkerInstanceId != ownership.HolderWorkerInstanceId)
        {
            return RuntimeReadinessDecision.NotReady("runtime_supervisor_not_owner");
        }

        DateTimeOffset normalizedNow = nowUtc.ToUniversalTime();
        foreach (RuntimeComponentEvidence item in evidence)
        {
            if (item.ContractVersion != RuntimeContractVersions.ComponentEvidenceV1)
            {
                return RuntimeReadinessDecision.NotReady("runtime_component_evidence_version_unsupported");
            }

            if (item.DeploymentId != deploymentId || item.Generation != generation)
            {
                return RuntimeReadinessDecision.NotReady("runtime_component_fenced");
            }

            if (item.WorkerInstanceId == Guid.Empty
                || item.LastAcceptedSequence < 0
                || item.StartedAtUtc > item.ObservedAtUtc
                || !RuntimeComponentEvidenceFactory.HasValidHash(item))
            {
                return RuntimeReadinessDecision.NotReady("runtime_component_evidence_invalid");
            }

            if (item.State != RuntimeComponentState.Ready)
            {
                return RuntimeReadinessDecision.NotReady("runtime_component_not_ready");
            }

            if (item.FenceState != FenceEvidenceState.Valid)
            {
                return RuntimeReadinessDecision.NotReady("runtime_component_fence_unverified");
            }

            DateTimeOffset observedAtUtc = item.ObservedAtUtc.ToUniversalTime();
            if (observedAtUtc > normalizedNow + _maximumFutureClockSkew)
            {
                return RuntimeReadinessDecision.NotReady("runtime_component_evidence_from_future");
            }

            if (normalizedNow - observedAtUtc > _maximumEvidenceAge)
            {
                return RuntimeReadinessDecision.NotReady("runtime_component_evidence_stale");
            }
        }

        if (ownership.NotBeforeUtc is null
            || ownership.ExpiresAtUtc is null
            || normalizedNow < ownership.NotBeforeUtc
            || normalizedNow >= ownership.ExpiresAtUtc)
        {
            return RuntimeReadinessDecision.NotReady("runtime_ownership_expired");
        }

        return RuntimeReadinessDecision.Ready();
    }
}
