using YO4X.Approvals;
using YO4X.Authorization;
using YO4X.Commands;
using YO4X.Policy;
using YO4X.ReadModels;

namespace YO4X.Admin.Application;

public sealed record AdminActor(
    Guid TenantId,
    Guid ActorId,
    Guid SessionId,
    string Environment,
    YO4X.Authorization.AuthenticationAssurance Assurance,
    bool ManagedDevice,
    DateTimeOffset AuthenticatedAt,
    IReadOnlySet<string> Permissions);

public sealed record AdminRequestMetadata(
    string IdempotencyKey,
    Guid CorrelationId,
    long? ExpectedVersion,
    string ReasonCode,
    string WrittenReason,
    string? TicketReference);

public sealed record AdminMeView(
    Guid Id,
    Guid SessionId,
    string Environment,
    IReadOnlySet<string> Permissions,
    DateTimeOffset AuthenticatedAt);

public sealed record CommandSummary(
    Guid Id,
    CommandType Type,
    CommandStatus Status,
    Guid RequesterId,
    string Reason,
    string? TicketReference,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ApprovalSummary(
    Guid Id,
    Guid CommandId,
    ApprovalStatus Status,
    Guid RequesterId,
    int RequiredApprovals,
    int ReceivedApprovals,
    DateTimeOffset ExpiresAt,
    long Version,
    string BindingDigest);

public sealed record CommandTargetView(
    Guid Id,
    Guid ResourceId,
    string ResourceType,
    long ResourceVersion,
    TargetTerminalProof RequiredProof,
    bool Required,
    Guid? WorkerId,
    long? Generation,
    CommandTargetStatus Status,
    int Attempts,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? AppliedAt,
    DateTimeOffset? ReconciledAt,
    string? ObservedResult,
    string? BrokerEvidenceReference,
    string? LastErrorCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ApprovalDecisionInput(string Reason, string BindingDigest);

public sealed record CompensationInput(CommandType CompensationType, string ReasonCode, string WrittenReason);

public sealed record ScopeInput(string Type, string? Id);

public enum EmergencyTemplate
{
    BlockNewExposure,
    BlockNewDeployments,
    CloseOnly,
    QuarantineExactGatewayDigest,
    RevokeCloudWorker
}

public sealed record RestrictiveCommandInput(
    EmergencyTemplate Template,
    ScopeInput Scope,
    Guid IncidentId,
    string? ExactDigest,
    string ReasonCode,
    string WrittenReason);

public sealed record RestrictivePreview(
    Guid Id,
    EmergencyTemplate Template,
    ScopeInput Scope,
    int TargetCount,
    bool Degraded,
    IReadOnlyList<string> MissingDimensions,
    string Digest,
    DateTimeOffset ExpiresAt);

public sealed record CommandAccepted(
    Guid CommandId,
    Uri StatusUrl,
    long SubmittedVersion,
    Guid CorrelationId,
    Guid? ApprovalRequestId);

public interface IAdminApplication
{
    Task<AdminMeView> GetMeAsync(AdminActor actor, CancellationToken cancellationToken);

    Task<IReadOnlyList<ApprovalSummary>> GetApprovalsAsync(
        AdminActor actor,
        int limit,
        Guid? before,
        CancellationToken cancellationToken);

    Task<ApprovalSummary?> GetApprovalAsync(AdminActor actor, Guid approvalId, CancellationToken cancellationToken);

    Task<CommandSummary?> DecideApprovalAsync(
        AdminActor actor,
        Guid approvalId,
        ApprovalDecisionType decision,
        ApprovalDecisionInput input,
        AdminRequestMetadata metadata,
        CancellationToken cancellationToken);

    Task<CommandSummary?> GetCommandAsync(AdminActor actor, Guid commandId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CommandTargetView>> GetCommandTargetsAsync(
        AdminActor actor,
        Guid commandId,
        CancellationToken cancellationToken);

    Task<CommandSummary?> CancelCommandAsync(
        AdminActor actor,
        Guid commandId,
        AdminRequestMetadata metadata,
        CancellationToken cancellationToken);

    Task<CommandAccepted> RequestCompensationAsync(
        AdminActor actor,
        Guid commandId,
        CompensationInput input,
        AdminRequestMetadata metadata,
        CancellationToken cancellationToken);

    Task<DeploymentOperationsView?> GetDeploymentAsync(
        AdminActor actor,
        Guid deploymentId,
        string purpose,
        CancellationToken cancellationToken);

    Task<CommandAccepted> RequestContainmentAsync(
        AdminActor actor,
        CommandType type,
        ScopeInput scope,
        ExecutionSafetyPolicyVector restrictions,
        AdminRequestMetadata metadata,
        CancellationToken cancellationToken);
}

public interface IEmergencySafetyApplication
{
    Task<RestrictivePreview> PreviewAsync(
        AdminActor actor,
        RestrictiveCommandInput input,
        Guid correlationId,
        CancellationToken cancellationToken);

    Task<CommandAccepted> SubmitAsync(
        AdminActor actor,
        RestrictiveCommandInput input,
        Guid previewId,
        string previewDigest,
        AdminRequestMetadata metadata,
        CancellationToken cancellationToken);

    Task<CommandSummary?> GetAsync(AdminActor actor, Guid commandId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CommandTargetView>> GetTargetsAsync(
        AdminActor actor,
        Guid commandId,
        CancellationToken cancellationToken);
}
