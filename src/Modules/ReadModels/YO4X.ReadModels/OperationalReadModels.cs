namespace YO4X.ReadModels;

public sealed record ComponentHealthView(
    string Component,
    string ObservedState,
    DateTimeOffset? LastHeartbeatAt,
    bool IsStale);

public sealed record DeploymentOperationsView(
    Guid DeploymentId,
    Guid TenantId,
    string DesiredState,
    string SupervisorObservedState,
    string StrategyHostObservedState,
    string GatewayHostObservedState,
    string BrokerReconciliationState,
    long Generation,
    long SourceVersion,
    DateTimeOffset ProjectedAt);

public sealed record RedactedUserOperationsView(
    Guid UserId,
    string MaskedEmail,
    string SecurityState,
    int ActiveSessionCount,
    long SourceVersion,
    DateTimeOffset ProjectedAt);

public static class ReadModelAuthority
{
    public const bool CanAuthorizeMutations = false;
}
