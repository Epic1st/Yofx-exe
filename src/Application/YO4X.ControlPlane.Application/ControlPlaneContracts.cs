using YO4X.BrokerAccounts;
using YO4X.Deployments;
using YO4X.Identity;
using YO4X.SecretCoordination;

namespace YO4X.ControlPlane.Application;

public sealed record UserActor(
    Guid TenantId,
    Guid UserId,
    Guid SessionId,
    AuthenticationAssurance Assurance);

public sealed record RequestMetadata(
    string IdempotencyKey,
    Guid CorrelationId,
    long? ExpectedVersion,
    string? Reason = null,
    string SourceNetworkClass = "unknown");

public sealed record UserView(
    Guid Id,
    string MaskedEmail,
    bool EmailVerified,
    UserSecurityState SecurityState,
    AuthenticationAssurance Assurance);

public sealed record SessionView(
    Guid Id,
    Guid DeviceId,
    SessionState State,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    bool Current);

public sealed record BrokerAccountView(
    Guid Id,
    Guid BrokerId,
    string Server,
    string MaskedLogin,
    BrokerAccountEnvironment Environment,
    BrokerAccountMode? AccountMode,
    string CapabilityState,
    long Version,
    DateTimeOffset UpdatedAt);

public sealed record DeploymentView(
    Guid Id,
    DeploymentMode Mode,
    DeploymentState DesiredState,
    string OfficialWorkerObservedState,
    string BrokerReconciliationState,
    long FenceGeneration,
    long Version,
    DateTimeOffset UpdatedAt);

public sealed record ActivityView(
    Guid Id,
    string Category,
    string Severity,
    string Code,
    IReadOnlyDictionary<string, string> Details,
    DateTimeOffset OccurredAt);

public enum StrategyCompatibilityAnalysisState
{
    Analyzed,
    ReviewRequired,
    Unsupported,
    Pending
}

public enum StrategyCompatibilitySourceType
{
    Mq5,
    Mqh
}

public sealed record StrategyCompatibilityItem(
    Guid StrategyId,
    string Name,
    StrategyCompatibilitySourceType SourceType,
    StrategyCompatibilityAnalysisState AnalysisState,
    int FeatureCount,
    string? ReportPath);

public sealed record StrategyCompatibilityProjection(
    int AnalyzedFileCount,
    int TotalFileCount,
    IReadOnlyList<StrategyCompatibilityItem> Items);

public sealed record AcceptedOperation(
    Guid CommandId,
    Uri StatusUrl,
    long SubmittedAggregateVersion,
    Guid CorrelationId);

public sealed class CredentialIngestionSessionView
{
    public CredentialIngestionSessionView(
        Guid grantId,
        Uri ingestionUrl,
        string singleUseBearer,
        string singleUseNonce,
        DateTimeOffset expiresAt)
    {
        GrantId = grantId;
        IngestionUrl = ingestionUrl;
        SingleUseBearer = singleUseBearer;
        SingleUseNonce = singleUseNonce;
        ExpiresAt = expiresAt;
    }

    public Guid GrantId { get; }

    public Uri IngestionUrl { get; }

    public string SingleUseBearer { get; }

    public string SingleUseNonce { get; }

    public DateTimeOffset ExpiresAt { get; }

    public override string ToString() =>
        $"CredentialIngestionSessionView {{ GrantId = {GrantId}, Proof = [REDACTED] }}";
}

public sealed record CreateCredentialIngestionSession(
    Guid BrokerAccountId,
    CredentialIngestionOperation Operation,
    Uri ClientOrigin);

public sealed record CreateCredentialRotationSession(Uri ClientOrigin);

public sealed record CreateStrategyImportSession(string SourceLabel);

public sealed class StrategyImportSessionView
{
    public StrategyImportSessionView(
        Guid importJobId,
        string sourceLabel,
        string singleUseCapability,
        DateTimeOffset expiresAt,
        long version)
    {
        ImportJobId = importJobId;
        SourceLabel = sourceLabel;
        SingleUseCapability = singleUseCapability;
        ExpiresAt = expiresAt;
        Version = version;
    }

    public Guid ImportJobId { get; }

    public string SourceLabel { get; }

    public string SingleUseCapability { get; }

    public DateTimeOffset ExpiresAt { get; }

    public long Version { get; }

    public override string ToString() =>
        $"StrategyImportSessionView {{ ImportJobId = {ImportJobId:D}, Capability = [REDACTED] }}";
}

public sealed record ValidateDeployment(DeploymentConfiguration Configuration);

public sealed record CreateDeployment(DeploymentConfiguration Configuration);

public sealed record DeploymentAction(string ReasonCode, string WrittenReason);

public enum BrokerAccountAction
{
    TestCloudConnection,
    DisableCloudUse,
    RequestCredentialDeletion
}

public sealed record UserOperationView(
    Guid Id,
    string OperationType,
    string TargetType,
    Guid TargetId,
    string State,
    string? LastErrorCode,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);

public interface IControlPlaneApplication
{
    Task<UserView?> GetMeAsync(UserActor actor, CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionView>> GetSessionsAsync(UserActor actor, CancellationToken cancellationToken);

    Task RevokeSessionAsync(
        UserActor actor,
        Guid sessionId,
        RequestMetadata metadata,
        CancellationToken cancellationToken);

    Task<BrokerAccountView?> GetBrokerAccountAsync(
        UserActor actor,
        Guid brokerAccountId,
        CancellationToken cancellationToken);

    Task<CredentialStateView?> GetCredentialStateAsync(
        UserActor actor,
        Guid brokerAccountId,
        CancellationToken cancellationToken);

    Task<CredentialIngestionSessionView> CreateCredentialIngestionSessionAsync(
        UserActor actor,
        CreateCredentialIngestionSession request,
        RequestMetadata metadata,
        CancellationToken cancellationToken);

    Task<StrategyImportSessionView> CreateStrategyImportSessionAsync(
        UserActor actor,
        CreateStrategyImportSession request,
        RequestMetadata metadata,
        CancellationToken cancellationToken);

    Task RevokeStrategyImportSessionAsync(
        UserActor actor,
        Guid importJobId,
        RequestMetadata metadata,
        CancellationToken cancellationToken);

    Task<AcceptedOperation> RequestBrokerAccountActionAsync(
        UserActor actor,
        Guid brokerAccountId,
        BrokerAccountAction action,
        DeploymentAction request,
        RequestMetadata metadata,
        CancellationToken cancellationToken);

    Task<UserOperationView?> GetOperationAsync(
        UserActor actor,
        Guid operationId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ValidateDeploymentAsync(
        UserActor actor,
        ValidateDeployment request,
        CancellationToken cancellationToken);

    Task<DeploymentView> CreateDeploymentAsync(
        UserActor actor,
        CreateDeployment request,
        RequestMetadata metadata,
        CancellationToken cancellationToken);

    Task<DeploymentView?> GetDeploymentAsync(
        UserActor actor,
        Guid deploymentId,
        CancellationToken cancellationToken);

    Task<AcceptedOperation> RequestDeploymentActionAsync(
        UserActor actor,
        Guid deploymentId,
        DeploymentState requestedState,
        DeploymentAction request,
        RequestMetadata metadata,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ActivityView>> GetDeploymentActivityAsync(
        UserActor actor,
        Guid deploymentId,
        int limit,
        Guid? before,
        CancellationToken cancellationToken);

    Task<StrategyCompatibilityProjection?> GetStrategyCompatibilityAsync(
        UserActor actor,
        Guid corpusId,
        CancellationToken cancellationToken);
}
