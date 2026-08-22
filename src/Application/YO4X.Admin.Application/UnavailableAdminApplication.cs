using YO4X.Approvals;
using YO4X.BuildingBlocks;
using YO4X.Commands;
using YO4X.Policy;
using YO4X.ReadModels;

namespace YO4X.Admin.Application;

public sealed class UnavailableAdminApplication : IAdminApplication, IEmergencySafetyApplication
{
    public Task<AdminMeView> GetMeAsync(AdminActor actor, CancellationToken cancellationToken) => Unavailable<AdminMeView>();

    public Task<IReadOnlyList<ApprovalSummary>> GetApprovalsAsync(AdminActor actor, int limit, Guid? before, CancellationToken cancellationToken) =>
        Unavailable<IReadOnlyList<ApprovalSummary>>();

    public Task<ApprovalSummary?> GetApprovalAsync(AdminActor actor, Guid approvalId, CancellationToken cancellationToken) =>
        Unavailable<ApprovalSummary?>();

    public Task<CommandSummary?> DecideApprovalAsync(AdminActor actor, Guid approvalId, ApprovalDecisionType decision, ApprovalDecisionInput input, AdminRequestMetadata metadata, CancellationToken cancellationToken) =>
        Unavailable<CommandSummary?>();

    public Task<CommandSummary?> GetCommandAsync(AdminActor actor, Guid commandId, CancellationToken cancellationToken) =>
        Unavailable<CommandSummary?>();

    public Task<IReadOnlyList<CommandTargetView>> GetCommandTargetsAsync(AdminActor actor, Guid commandId, CancellationToken cancellationToken) =>
        Unavailable<IReadOnlyList<CommandTargetView>>();

    public Task<CommandSummary?> CancelCommandAsync(AdminActor actor, Guid commandId, AdminRequestMetadata metadata, CancellationToken cancellationToken) =>
        Unavailable<CommandSummary?>();

    public Task<CommandAccepted> RequestCompensationAsync(AdminActor actor, Guid commandId, CompensationInput input, AdminRequestMetadata metadata, CancellationToken cancellationToken) =>
        Unavailable<CommandAccepted>();

    public Task<DeploymentOperationsView?> GetDeploymentAsync(AdminActor actor, Guid deploymentId, string purpose, CancellationToken cancellationToken) =>
        Unavailable<DeploymentOperationsView?>();

    public Task<CommandAccepted> RequestContainmentAsync(AdminActor actor, CommandType type, ScopeInput scope, ExecutionSafetyPolicyVector restrictions, AdminRequestMetadata metadata, CancellationToken cancellationToken) =>
        Unavailable<CommandAccepted>();

    public Task<RestrictivePreview> PreviewAsync(AdminActor actor, RestrictiveCommandInput input, Guid correlationId, CancellationToken cancellationToken) =>
        Unavailable<RestrictivePreview>();

    public Task<CommandAccepted> SubmitAsync(AdminActor actor, RestrictiveCommandInput input, Guid previewId, string previewDigest, AdminRequestMetadata metadata, CancellationToken cancellationToken) =>
        Unavailable<CommandAccepted>();

    public Task<CommandSummary?> GetAsync(AdminActor actor, Guid commandId, CancellationToken cancellationToken) =>
        Unavailable<CommandSummary?>();

    public Task<IReadOnlyList<CommandTargetView>> GetTargetsAsync(AdminActor actor, Guid commandId, CancellationToken cancellationToken) =>
        Unavailable<IReadOnlyList<CommandTargetView>>();

    private static Task<T> Unavailable<T>() => Task.FromException<T>(new BackendCapabilityUnavailableException("admin_postgres"));
}
