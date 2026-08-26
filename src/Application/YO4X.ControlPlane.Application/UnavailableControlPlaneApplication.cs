using YO4X.BrokerAccounts;
using YO4X.BuildingBlocks;
using YO4X.Deployments;

namespace YO4X.ControlPlane.Application;

public sealed class UnavailableControlPlaneApplication : IControlPlaneApplication
{
    public Task<UserView?> GetMeAsync(UserActor actor, CancellationToken cancellationToken) => Unavailable<UserView?>();

    public Task<IReadOnlyList<SessionView>> GetSessionsAsync(UserActor actor, CancellationToken cancellationToken) =>
        Unavailable<IReadOnlyList<SessionView>>();

    public Task RevokeSessionAsync(UserActor actor, Guid sessionId, RequestMetadata metadata, CancellationToken cancellationToken) =>
        Unavailable();

    public Task<BrokerAccountView?> GetBrokerAccountAsync(UserActor actor, Guid brokerAccountId, CancellationToken cancellationToken) =>
        Unavailable<BrokerAccountView?>();

    public Task<IReadOnlyList<BrokerAccountView>> GetBrokerAccountsAsync(UserActor actor, CancellationToken cancellationToken) =>
        Unavailable<IReadOnlyList<BrokerAccountView>>();

    public Task<IReadOnlyList<BrokerAccountRegistrationOption>> GetBrokerAccountRegistrationOptionsAsync(UserActor actor, string? query, CancellationToken cancellationToken) =>
        Unavailable<IReadOnlyList<BrokerAccountRegistrationOption>>();

    public Task<BrokerAccountRegistrationOption> ApproveBrokerServerAsync(UserActor actor, ApproveBrokerServer request, RequestMetadata metadata, CancellationToken cancellationToken) =>
        Unavailable<BrokerAccountRegistrationOption>();

    public Task<BrokerAccountView> CreateBrokerAccountAsync(UserActor actor, CreateBrokerAccount request, RequestMetadata metadata, CancellationToken cancellationToken) =>
        Unavailable<BrokerAccountView>();

    public Task<CredentialStateView?> GetCredentialStateAsync(UserActor actor, Guid brokerAccountId, CancellationToken cancellationToken) =>
        Unavailable<CredentialStateView?>();

    public Task<CredentialIngestionSessionView> CreateCredentialIngestionSessionAsync(UserActor actor, CreateCredentialIngestionSession request, RequestMetadata metadata, CancellationToken cancellationToken) =>
        Unavailable<CredentialIngestionSessionView>();

    public Task<StrategyImportSessionView> CreateStrategyImportSessionAsync(UserActor actor, CreateStrategyImportSession request, RequestMetadata metadata, CancellationToken cancellationToken) =>
        Unavailable<StrategyImportSessionView>();

    public Task RevokeStrategyImportSessionAsync(UserActor actor, Guid importJobId, RequestMetadata metadata, CancellationToken cancellationToken) =>
        Unavailable();

    public Task<AcceptedOperation> RequestBrokerAccountActionAsync(UserActor actor, Guid brokerAccountId, BrokerAccountAction action, DeploymentAction request, RequestMetadata metadata, CancellationToken cancellationToken) =>
        Unavailable<AcceptedOperation>();

    public Task<UserOperationView?> GetOperationAsync(UserActor actor, Guid operationId, CancellationToken cancellationToken) =>
        Unavailable<UserOperationView?>();

    public Task<IReadOnlyList<string>> ValidateDeploymentAsync(UserActor actor, ValidateDeployment request, CancellationToken cancellationToken) =>
        Unavailable<IReadOnlyList<string>>();

    public Task<DeploymentView> CreateDeploymentAsync(UserActor actor, CreateDeployment request, RequestMetadata metadata, CancellationToken cancellationToken) =>
        Unavailable<DeploymentView>();

    public Task<DeploymentView?> GetDeploymentAsync(UserActor actor, Guid deploymentId, CancellationToken cancellationToken) =>
        Unavailable<DeploymentView?>();

    public Task<AcceptedOperation> RequestDeploymentActionAsync(UserActor actor, Guid deploymentId, DeploymentState requestedState, DeploymentAction request, RequestMetadata metadata, CancellationToken cancellationToken) =>
        Unavailable<AcceptedOperation>();

    public Task<IReadOnlyList<ActivityView>> GetDeploymentActivityAsync(UserActor actor, Guid deploymentId, int limit, Guid? before, CancellationToken cancellationToken) =>
        Unavailable<IReadOnlyList<ActivityView>>();

    public Task<IReadOnlyList<StrategySourceCorpusSummary>> GetStrategySourceCorporaAsync(UserActor actor, CancellationToken cancellationToken) =>
        Unavailable<IReadOnlyList<StrategySourceCorpusSummary>>();

    public Task<StrategyCompatibilityProjection?> GetStrategyCompatibilityAsync(UserActor actor, Guid corpusId, CancellationToken cancellationToken) =>
        Unavailable<StrategyCompatibilityProjection?>();

    private static Task Unavailable() => Task.FromException(new BackendCapabilityUnavailableException("control_plane_postgres"));

    private static Task<T> Unavailable<T>() => Task.FromException<T>(new BackendCapabilityUnavailableException("control_plane_postgres"));
}
