using YO4X.BuildingBlocks;
using YO4X.Runtime.Contracts;

namespace YO4X.RuntimeOperations;

public static class RuntimeComponentEvidenceFactory
{
    public static RuntimeComponentEvidence Create(
        RuntimeComponentRole role,
        Guid deploymentId,
        Guid workerInstanceId,
        long generation,
        long lastAcceptedSequence,
        RuntimeComponentState state,
        FenceEvidenceState fenceState,
        DateTimeOffset startedAtUtc,
        DateTimeOffset observedAtUtc)
    {
        DateTimeOffset normalizedStartedAt = startedAtUtc.ToUniversalTime();
        DateTimeOffset normalizedObservedAt = observedAtUtc.ToUniversalTime();
        string hash = ComputeHash(
            role,
            deploymentId,
            workerInstanceId,
            generation,
            lastAcceptedSequence,
            state,
            fenceState,
            normalizedStartedAt,
            normalizedObservedAt);

        return new RuntimeComponentEvidence(
            RuntimeContractVersions.ComponentEvidenceV1,
            role,
            deploymentId,
            workerInstanceId,
            generation,
            lastAcceptedSequence,
            state,
            fenceState,
            normalizedStartedAt,
            normalizedObservedAt,
            hash);
    }

    public static bool HasValidHash(RuntimeComponentEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.ContractVersion != RuntimeContractVersions.ComponentEvidenceV1)
        {
            return false;
        }

        string expected = ComputeHash(
            evidence.Role,
            evidence.DeploymentId,
            evidence.WorkerInstanceId,
            evidence.Generation,
            evidence.LastAcceptedSequence,
            evidence.State,
            evidence.FenceState,
            evidence.StartedAtUtc.ToUniversalTime(),
            evidence.ObservedAtUtc.ToUniversalTime());
        return string.Equals(expected, evidence.EvidenceHash, StringComparison.Ordinal);
    }

    private static string ComputeHash(
        RuntimeComponentRole role,
        Guid deploymentId,
        Guid workerInstanceId,
        long generation,
        long lastAcceptedSequence,
        RuntimeComponentState state,
        FenceEvidenceState fenceState,
        DateTimeOffset startedAtUtc,
        DateTimeOffset observedAtUtc) =>
        CanonicalJson.Sha256(new
        {
            ContractVersion = RuntimeContractVersions.ComponentEvidenceV1,
            Role = role,
            DeploymentId = deploymentId,
            WorkerInstanceId = workerInstanceId,
            Generation = generation,
            LastAcceptedSequence = lastAcceptedSequence,
            State = state,
            FenceState = fenceState,
            StartedAtUtc = startedAtUtc,
            ObservedAtUtc = observedAtUtc
        });
}
