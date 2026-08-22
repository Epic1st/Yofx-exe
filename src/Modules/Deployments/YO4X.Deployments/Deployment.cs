using System.Text.RegularExpressions;
using YO4X.BuildingBlocks;

namespace YO4X.Deployments;

public enum DeploymentState
{
    Draft,
    Validating,
    Ready,
    Starting,
    Reconciling,
    Running,
    CloseOnly,
    StopAfterFlat,
    Stopping,
    Stopped,
    Faulted,
    Fenced,
    Expired,
    Revoked
}

public enum DeploymentMode
{
    CloudDemo
}

public sealed partial record DeploymentConfiguration(
    Guid BrokerAccountId,
    Guid StrategyVersionId,
    Guid RiskPolicyVersionId,
    string GatewayDigest,
    string StrategyPackageDigest,
    string Region,
    bool DedicatedAccount,
    bool HedgingAccount,
    bool BrokerHostedStopLoss,
    bool BrokerHostedTakeProfit,
    bool ManualOrExternalTradingDetected)
{
    public string ConfigurationHash => CanonicalJson.Sha256(this);

    public IReadOnlyList<string> ValidateForU0(
        string approvedGatewayDigest,
        string approvedRegion)
    {
        var failures = new List<string>();
        if (BrokerAccountId == Guid.Empty || StrategyVersionId == Guid.Empty || RiskPolicyVersionId == Guid.Empty)
        {
            failures.Add("REQUIRED_BINDING_MISSING");
        }

        if (string.IsNullOrWhiteSpace(GatewayDigest)
            || string.IsNullOrWhiteSpace(StrategyPackageDigest)
            || !DigestPattern().IsMatch(GatewayDigest)
            || !DigestPattern().IsMatch(StrategyPackageDigest))
        {
            failures.Add("INVALID_PACKAGE_DIGEST");
        }

        if (!string.Equals(GatewayDigest, approvedGatewayDigest, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("GATEWAY_DIGEST_NOT_APPROVED");
        }

        if (!string.Equals(Region, approvedRegion, StringComparison.Ordinal))
        {
            failures.Add("REGION_NOT_APPROVED");
        }

        if (!DedicatedAccount)
        {
            failures.Add("DEDICATED_ACCOUNT_REQUIRED");
        }

        if (!HedgingAccount)
        {
            failures.Add("HEDGING_ACCOUNT_REQUIRED");
        }

        if (!BrokerHostedStopLoss || !BrokerHostedTakeProfit)
        {
            failures.Add("BROKER_HOSTED_PROTECTION_REQUIRED");
        }

        if (ManualOrExternalTradingDetected)
        {
            failures.Add("UNEXPECTED_ACCOUNT_ACTIVITY");
        }

        return failures;
    }

    [GeneratedRegex("^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex DigestPattern();
}

public sealed record DeploymentTransition(
    DeploymentState From,
    DeploymentState To,
    string ActorId,
    string ReasonCode,
    string CorrelationId,
    DateTimeOffset OccurredAt);

public sealed class Deployment : VersionedAggregate
{
    private readonly List<DeploymentTransition> _transitions = [];

    private Deployment(
        Guid id,
        Guid tenantId,
        Guid userId,
        DeploymentConfiguration configuration,
        DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        TenantId = tenantId;
        UserId = userId;
        Configuration = configuration;
        State = DeploymentState.Draft;
    }

    public Guid TenantId { get; }

    public Guid UserId { get; }

    public DeploymentMode Mode { get; } = DeploymentMode.CloudDemo;

    public DeploymentConfiguration Configuration { get; }

    public DeploymentState State { get; private set; }

    public bool BrokerReconciled { get; private set; }

    public IReadOnlyList<DeploymentTransition> Transitions => _transitions;

    public static Deployment Create(
        Guid tenantId,
        Guid userId,
        DeploymentConfiguration configuration,
        IClock clock)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty)
        {
            throw new ArgumentException("Tenant and user identifiers are required.");
        }

        ArgumentNullException.ThrowIfNull(configuration);
        return new Deployment(Identifiers.NewId(), tenantId, userId, configuration, clock.UtcNow);
    }

    public void MarkValidated(
        IReadOnlyCollection<string> failures,
        string actorId,
        string correlationId,
        DateTimeOffset occurredAt)
    {
        if (State is not (DeploymentState.Draft or DeploymentState.Validating))
        {
            throw InvalidTransition(DeploymentState.Ready);
        }

        if (failures.Count > 0)
        {
            throw new DomainException("DEPLOYMENT_VALIDATION_FAILED", string.Join(',', failures));
        }

        TransitionTo(DeploymentState.Ready, actorId, "VALIDATION_PASSED", correlationId, occurredAt);
    }

    public void Start(string actorId, string correlationId, DateTimeOffset occurredAt)
    {
        if (State != DeploymentState.Ready)
        {
            throw InvalidTransition(DeploymentState.Starting);
        }

        BrokerReconciled = false;
        TransitionTo(DeploymentState.Starting, actorId, "START_REQUESTED", correlationId, occurredAt);
    }

    public void BeginReconciliation(string actorId, string correlationId, DateTimeOffset occurredAt)
    {
        if (State is not (DeploymentState.Starting or DeploymentState.Fenced or DeploymentState.Faulted))
        {
            throw InvalidTransition(DeploymentState.Reconciling);
        }

        BrokerReconciled = false;
        TransitionTo(DeploymentState.Reconciling, actorId, "RECONCILIATION_STARTED", correlationId, occurredAt);
    }

    public void ConfirmReconciled(string actorId, string correlationId, DateTimeOffset occurredAt)
    {
        if (State != DeploymentState.Reconciling)
        {
            throw InvalidTransition(DeploymentState.Running);
        }

        BrokerReconciled = true;
        TransitionTo(DeploymentState.Running, actorId, "BROKER_RECONCILED", correlationId, occurredAt);
    }

    public void EnterCloseOnly(string actorId, string reasonCode, string correlationId, DateTimeOffset occurredAt)
    {
        if (State is DeploymentState.Stopped or DeploymentState.Revoked)
        {
            throw InvalidTransition(DeploymentState.CloseOnly);
        }

        TransitionTo(DeploymentState.CloseOnly, actorId, reasonCode, correlationId, occurredAt);
    }

    public void StopAfterFlat(string actorId, string reasonCode, string correlationId, DateTimeOffset occurredAt)
    {
        if (State is DeploymentState.Stopped or DeploymentState.Revoked)
        {
            throw InvalidTransition(DeploymentState.StopAfterFlat);
        }

        TransitionTo(DeploymentState.StopAfterFlat, actorId, reasonCode, correlationId, occurredAt);
    }

    public void ConfirmFlatAndStopped(string actorId, string correlationId, DateTimeOffset occurredAt)
    {
        if (State is not (DeploymentState.StopAfterFlat or DeploymentState.Stopping or DeploymentState.CloseOnly))
        {
            throw InvalidTransition(DeploymentState.Stopped);
        }

        if (!BrokerReconciled)
        {
            throw new DomainException("BROKER_RECONCILIATION_REQUIRED", "A deployment cannot be reported stopped before broker reconciliation.");
        }

        TransitionTo(DeploymentState.Stopped, actorId, "FLAT_RECONCILED", correlationId, occurredAt);
    }

    public void Fence(string actorId, string reasonCode, string correlationId, DateTimeOffset occurredAt)
    {
        BrokerReconciled = false;
        TransitionTo(DeploymentState.Fenced, actorId, reasonCode, correlationId, occurredAt);
    }

    private void TransitionTo(
        DeploymentState next,
        string actorId,
        string reasonCode,
        string correlationId,
        DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        DeploymentState previous = State;
        State = next;
        _transitions.Add(new DeploymentTransition(previous, next, actorId, reasonCode, correlationId, occurredAt.ToUniversalTime()));
        RecordChange(occurredAt);
    }

    private DomainException InvalidTransition(DeploymentState requested) =>
        new("DEPLOYMENT_STATE_CONFLICT", $"Deployment state {State} cannot transition to {requested}.");
}

public interface IDeploymentRepository
{
    Task<Deployment?> FindAsync(Guid tenantId, Guid deploymentId, CancellationToken cancellationToken);

    Task<bool> HasActiveDeploymentForAccountAsync(Guid tenantId, Guid brokerAccountId, CancellationToken cancellationToken);

    Task SaveAsync(Deployment deployment, long expectedVersion, CancellationToken cancellationToken);
}
