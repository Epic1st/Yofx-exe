using System.Text.Json;
using YO4X.BuildingBlocks;
using YO4X.Runtime.Contracts;

namespace YO4X.ControlPlane.Application;

public sealed record WorkloadActor(
    Guid TenantId,
    Guid WorkloadId,
    Guid WorkerInstanceId,
    Guid DeploymentId,
    Guid BrokerAccountId,
    long Generation,
    string Region,
    string Component);

public sealed record WorkerRegistration(
    Guid BrokerAccountId,
    Guid DeploymentId,
    Guid WorkerInstanceId,
    long Generation,
    string Region,
    string RuntimeImageDigest,
    string StrategyPackageDigest,
    string GatewayArtifactDigest,
    Guid SupervisorWorkloadId,
    Guid StrategyHostWorkloadId,
    Guid GatewayHostWorkloadId);

public sealed record WorkerRegistrationView(
    Guid WorkerId,
    long Generation,
    string State,
    DateTimeOffset RegisteredAt);

public sealed record ComponentHeartbeat(
    int ContractVersion,
    RuntimeComponentState State,
    long Sequence,
    long LastAcceptedEventSequence,
    FenceEvidenceState FenceState,
    DateTimeOffset StartedAt,
    DateTimeOffset ObservedAt,
    string EvidenceDigest);

public sealed record IssueExecutionLease(
    Guid DeploymentId,
    Guid WorkerInstanceId,
    long Generation,
    LeaseActionClass RequestedActions);

public sealed record RenewExecutionLease(
    Guid LeaseId,
    long Generation,
    LeaseActionClass RequestedActions);

public sealed record RuntimeEventInput(
    int SchemaVersion,
    Guid EventId,
    long Generation,
    long Sequence,
    DateTimeOffset ObservedAt,
    JsonElement Payload);

public sealed record TargetDeliveryInput(
    int SchemaVersion,
    Guid EventId,
    long Generation,
    long Sequence,
    DateTimeOffset ObservedAt,
    string State,
    string? ErrorCode,
    string? ObservedResult,
    string? BrokerEvidenceReference,
    JsonElement Evidence);

/// <summary>
/// Authenticated, non-secret broker evidence for one exact dispatched user operation.
/// The runtime cannot choose a different operation, message, version, target state, or policy binding.
/// </summary>
public sealed record BrokerUserOperationResultInput(
    int SchemaVersion,
    Guid ResultId,
    Guid OperationId,
    Guid DispatchMessageId,
    long SubmittedResourceVersion,
    string RequestedTargetState,
    string PolicySnapshotSha256,
    string Outcome,
    bool BrokerConfirmed,
    string AccountState,
    string CredentialState,
    string EvidenceSha256,
    string? ErrorCode,
    DateTimeOffset ObservedAt);

public sealed record BrokerUserOperationResultAcceptance(Guid ResultId, string State);

public sealed record RuntimeAcceptance(Guid EventId, string State, long ExpectedNextSequence);

public interface IRuntimeControlPlaneApplication
{
    Task<WorkerRegistrationView> RegisterWorkerAsync(
        WorkloadActor actor,
        WorkerRegistration request,
        RequestMetadata metadata,
        CancellationToken cancellationToken);

    Task RecordHeartbeatAsync(
        WorkloadActor actor,
        Guid workerId,
        RuntimeComponentRole component,
        ComponentHeartbeat request,
        RequestMetadata metadata,
        CancellationToken cancellationToken);

    Task<SignedExecutionLease> IssueLeaseAsync(
        WorkloadActor actor,
        IssueExecutionLease request,
        RequestMetadata metadata,
        CancellationToken cancellationToken);

    Task<SignedExecutionLease> RenewLeaseAsync(
        WorkloadActor actor,
        RenewExecutionLease request,
        RequestMetadata metadata,
        CancellationToken cancellationToken);

    Task<RuntimeAcceptance> RecordDeploymentEventAsync(
        WorkloadActor actor,
        Guid deploymentId,
        RuntimeEventInput request,
        RequestMetadata metadata,
        CancellationToken cancellationToken);

    Task<RuntimeAcceptance> RecordTargetDeliveryAsync(
        WorkloadActor actor,
        Guid targetId,
        TargetDeliveryInput request,
        RequestMetadata metadata,
        CancellationToken cancellationToken);

    Task<RuntimeAcceptance> RecordTargetReconciliationAsync(
        WorkloadActor actor,
        Guid targetId,
        TargetDeliveryInput request,
        RequestMetadata metadata,
        CancellationToken cancellationToken);

    Task<BrokerUserOperationResultAcceptance> RecordBrokerUserOperationResultAsync(
        WorkloadActor actor,
        Guid brokerAccountId,
        BrokerUserOperationResultInput request,
        RequestMetadata metadata,
        CancellationToken cancellationToken);
}

public sealed class UnavailableRuntimeControlPlaneApplication : IRuntimeControlPlaneApplication
{
    public Task<WorkerRegistrationView> RegisterWorkerAsync(WorkloadActor actor, WorkerRegistration request, RequestMetadata metadata, CancellationToken cancellationToken) => Unavailable<WorkerRegistrationView>();

    public Task RecordHeartbeatAsync(WorkloadActor actor, Guid workerId, RuntimeComponentRole component, ComponentHeartbeat request, RequestMetadata metadata, CancellationToken cancellationToken) =>
        Task.FromException(new BackendCapabilityUnavailableException("runtime_control_postgres"));

    public Task<SignedExecutionLease> IssueLeaseAsync(WorkloadActor actor, IssueExecutionLease request, RequestMetadata metadata, CancellationToken cancellationToken) =>
        Unavailable<SignedExecutionLease>();

    public Task<SignedExecutionLease> RenewLeaseAsync(WorkloadActor actor, RenewExecutionLease request, RequestMetadata metadata, CancellationToken cancellationToken) =>
        Unavailable<SignedExecutionLease>();

    public Task<RuntimeAcceptance> RecordDeploymentEventAsync(WorkloadActor actor, Guid deploymentId, RuntimeEventInput request, RequestMetadata metadata, CancellationToken cancellationToken) =>
        Unavailable<RuntimeAcceptance>();

    public Task<RuntimeAcceptance> RecordTargetDeliveryAsync(WorkloadActor actor, Guid targetId, TargetDeliveryInput request, RequestMetadata metadata, CancellationToken cancellationToken) =>
        Unavailable<RuntimeAcceptance>();

    public Task<RuntimeAcceptance> RecordTargetReconciliationAsync(WorkloadActor actor, Guid targetId, TargetDeliveryInput request, RequestMetadata metadata, CancellationToken cancellationToken) =>
        Unavailable<RuntimeAcceptance>();

    public Task<BrokerUserOperationResultAcceptance> RecordBrokerUserOperationResultAsync(WorkloadActor actor, Guid brokerAccountId, BrokerUserOperationResultInput request, RequestMetadata metadata, CancellationToken cancellationToken) =>
        Unavailable<BrokerUserOperationResultAcceptance>();

    private static Task<T> Unavailable<T>() => Task.FromException<T>(new BackendCapabilityUnavailableException("runtime_control_postgres"));
}
