using System.Text.Json.Nodes;
using YO4X.Admin.Application;
using YO4X.BuildingBlocks;
using YO4X.Commands;

namespace YO4X.Admin.Postgres;

internal sealed record AdminGrant(
    Guid GrantId,
    string Permission,
    string Environment,
    string ScopeType,
    string? ScopeId)
{
    public bool Contains(AdminResourceScope resource)
    {
        if (!string.Equals(Environment, resource.Environment, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(ScopeType, "global", StringComparison.Ordinal))
        {
            return true;
        }

        return ScopeId is not null
            && resource.Dimensions.TryGetValue(ScopeType, out string? resourceScopeId)
            && string.Equals(ScopeId, resourceScopeId, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record AdminResourceScope(
    string Environment,
    IReadOnlyDictionary<string, string> Dimensions,
    long? Version)
{
    public static AdminResourceScope ForCommand(AdminCommandRecord command)
    {
        var dimensions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [command.ScopeType] = command.ScopeId ?? string.Empty
        };
        return new AdminResourceScope(command.Environment, dimensions, command.RowVersion);
    }
}

internal sealed record AdminSessionEvidence(
    string AssuranceMethod,
    bool ManagedDevice,
    DateTimeOffset AuthenticatedAt);

internal sealed class AdminSecuritySnapshot(
    AdminActor actor,
    string environment,
    AdminSessionEvidence session,
    IReadOnlyList<AdminGrant> grants,
    DateTimeOffset authorizationNow)
{
    public AdminActor Actor { get; } = actor;

    public string Environment { get; } = environment;

    public AdminSessionEvidence Session { get; } = session;

    public IReadOnlyList<AdminGrant> Grants { get; } = grants;

    public DateTimeOffset AuthorizationNow { get; } = authorizationNow;

    public IReadOnlySet<string> EffectivePermissions => Grants
        .Select(grant => grant.Permission)
        .ToHashSet(StringComparer.Ordinal);

    public void RequirePermission(string permission)
    {
        if (!Grants.Any(grant => string.Equals(grant.Permission, permission, StringComparison.Ordinal)))
        {
            throw new AdminAuthorizationDeniedException(
                "ADMIN_PERMISSION_DENIED",
                "The active admin grants do not include the required permission.");
        }
    }

    public bool CanAccess(string permission, AdminResourceScope resource)
    {
        RequirePermission(permission);
        return Grants.Any(grant =>
            string.Equals(grant.Permission, permission, StringComparison.Ordinal)
            && grant.Contains(resource));
    }

    public void RequireAccess(string permission, AdminResourceScope resource)
    {
        if (!CanAccess(permission, resource))
        {
            throw new AdminResourceNotFoundException();
        }
    }
}

internal sealed record AdminCommandRecord(
    Guid Id,
    CommandType Type,
    string PayloadSha256,
    string CommandDigest,
    string RestrictionVectorJson,
    IReadOnlyList<CommandType> AllowedCompensationTypes,
    Guid ActorId,
    Guid SessionId,
    string Environment,
    string ScopeType,
    string? ScopeId,
    string RiskLevel,
    string ReasonCode,
    string WrittenReason,
    string? TicketReference,
    Guid IdempotencyRecordId,
    long? ExpectedResourceVersion,
    Guid? ImpactPreviewId,
    string? ImpactDigest,
    CommandStatus Status,
    Guid? OriginalCommandId,
    Guid? CompensationCommandId,
    DateTimeOffset? RequestedExecutionAt,
    DateTimeOffset? ExpiresAt,
    Guid CorrelationId,
    long RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public CommandSummary ToSummary() => new(
        Id,
        Type,
        Status,
        ActorId,
        WrittenReason,
        TicketReference,
        RowVersion,
        CreatedAt,
        UpdatedAt);

    public AdminCommandRecord WithLifecycle(
        CommandStatus status,
        long rowVersion,
        DateTimeOffset updatedAt,
        Guid? compensationCommandId = null) => this with
    {
        Status = status,
        RowVersion = rowVersion,
        UpdatedAt = updatedAt,
        CompensationCommandId = compensationCommandId ?? CompensationCommandId
    };
}

internal sealed record ApprovalRecord(
    Guid Id,
    Guid CommandId,
    Guid RequesterId,
    string PolicyKey,
    Guid ImpactPreviewId,
    string CommandDigest,
    string ImpactDigest,
    long CommandRowVersion,
    string RestrictionDigest,
    string BindingSnapshotJson,
    string BindingDigest,
    int RequiredApprovals,
    string MinimumAssurance,
    bool ManagedDeviceRequired,
    int MaximumSessionAgeSeconds,
    string State,
    string? InvalidationCode,
    DateTimeOffset ExpiresAt,
    long RowVersion,
    DateTimeOffset CreatedAt,
    int ReceivedApprovals,
    AdminCommandRecord Command)
{
    public ApprovalSummary ToSummary(DateTimeOffset now) => new(
        Id,
        CommandId,
        AdminStorageValues.ParseApprovalStatus(State, ExpiresAt, now),
        RequesterId,
        RequiredApprovals,
        ReceivedApprovals,
        ExpiresAt,
        RowVersion,
        ApprovalBindingDigest.Compute(
            CommandDigest,
            ImpactDigest,
            CommandRowVersion,
            RestrictionDigest));
}

internal sealed record DeploymentResource(
    Guid Id,
    Guid TenantId,
    Guid UserId,
    Guid BrokerAccountId,
    Guid BrokerId,
    Guid StrategyVersionId,
    Guid GatewayArtifactId,
    string Region,
    string Environment,
    string DesiredState,
    string ObservedState,
    bool BrokerHostedStopLoss,
    bool BrokerHostedTakeProfit,
    long FenceGeneration,
    Guid? WorkerNodeId,
    long? WorkerGeneration,
    long RowVersion,
    DateTimeOffset UpdatedAt,
    string SupervisorState,
    string StrategyHostState,
    string GatewayHostState,
    string BrokerState,
    long SourceVersion,
    DateTimeOffset ProjectedAt)
{
    public AdminResourceScope ToScope()
    {
        var dimensions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["environment"] = Environment,
            ["deployment"] = Id.ToString("D"),
            ["user"] = UserId.ToString("D"),
            ["account"] = BrokerAccountId.ToString("D"),
            ["broker"] = BrokerId.ToString("D"),
            ["strategy"] = StrategyVersionId.ToString("D"),
            ["gateway"] = GatewayArtifactId.ToString("D"),
            ["region"] = Region
        };
        if (WorkerNodeId is not null)
        {
            dimensions["worker"] = WorkerNodeId.Value.ToString("D");
        }

        return new AdminResourceScope(Environment, dimensions, RowVersion);
    }
}

internal sealed record ImpactTargetSnapshot(
    Guid TargetId,
    Guid ResourceId,
    string ResourceType,
    long ResourceVersion,
    string RequiredProof,
    bool Required,
    Guid? WorkerId,
    long? Generation);

internal sealed record ImpactPreviewRecord(
    Guid Id,
    Guid TenantId,
    Guid ActorId,
    string ScopeExpressionJson,
    string TargetSnapshotJson,
    int TargetCount,
    string ResourceVersionWatermark,
    string PolicyVersion,
    string Digest,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt)
{
    public static ImpactPreviewRecord Create(
        Guid id,
        Guid tenantId,
        Guid actorId,
        ScopeInput scope,
        IReadOnlyList<ImpactTargetSnapshot> targets,
        string policyVersion,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        string scopeJson = CanonicalJson.Serialize(new
        {
            Type = scope.Type.Trim().ToLowerInvariant(),
            Id = string.IsNullOrWhiteSpace(scope.Id) ? null : scope.Id.Trim()
        });
        string targetsJson = CanonicalJson.Serialize(targets);
        string watermark = CanonicalJson.Sha256(targets.Select(target => new
        {
            target.ResourceId,
            target.ResourceVersion,
            target.WorkerId,
            target.Generation
        }).ToArray());
        string digest = ComputeDigest(
            id,
            tenantId,
            actorId,
            scopeJson,
            targetsJson,
            targets.Count,
            watermark,
            policyVersion,
            createdAt,
            expiresAt);
        return new ImpactPreviewRecord(
            id,
            tenantId,
            actorId,
            scopeJson,
            targetsJson,
            targets.Count,
            watermark,
            policyVersion,
            digest,
            createdAt,
            expiresAt);
    }

    public bool HasValidDigest() => string.Equals(
        Digest,
        ComputeDigest(
            Id,
            TenantId,
            ActorId,
            ScopeExpressionJson,
            TargetSnapshotJson,
            TargetCount,
            ResourceVersionWatermark,
            PolicyVersion,
            CreatedAt,
            ExpiresAt),
        StringComparison.Ordinal);

    public IReadOnlyList<ImpactTargetSnapshot> ReadTargets() =>
        System.Text.Json.JsonSerializer.Deserialize<ImpactTargetSnapshot[]>(
            TargetSnapshotJson,
            WebJson.Options) ?? [];

    private static string ComputeDigest(
        Guid id,
        Guid tenantId,
        Guid actorId,
        string scopeJson,
        string targetsJson,
        int targetCount,
        string watermark,
        string policyVersion,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt) => CanonicalJson.Sha256(new
        {
            Id = id,
            TenantId = tenantId,
            ActorId = actorId,
            Scope = JsonNode.Parse(scopeJson),
            Targets = JsonNode.Parse(targetsJson),
            TargetCount = targetCount,
            ResourceVersionWatermark = watermark,
            PolicyVersion = policyVersion,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt
        });
}

internal sealed record AdminCommandBinding(
    Guid Id,
    Guid TenantId,
    string CommandType,
    string PayloadSha256,
    string RestrictionVectorJson,
    IReadOnlyList<string> AllowedCompensationTypes,
    Guid ActorId,
    Guid SessionId,
    string Environment,
    string ScopeType,
    string? ScopeId,
    string RiskLevel,
    string ReasonCode,
    string WrittenReason,
    string? TicketReference,
    Guid IdempotencyRecordId,
    long? ExpectedResourceVersion,
    Guid? ImpactPreviewId,
    string? ImpactDigest,
    Guid? OriginalCommandId,
    DateTimeOffset? RequestedExecutionAt,
    DateTimeOffset? ExpiresAt,
    Guid CorrelationId)
{
    public string ComputeDigest() => CanonicalJson.Sha256(new
    {
        Id,
        TenantId,
        CommandType,
        PayloadSha256,
        RestrictionVector = JsonNode.Parse(RestrictionVectorJson),
        AllowedCompensationTypes = AllowedCompensationTypes.Order(StringComparer.Ordinal).ToArray(),
        ActorId,
        SessionId,
        Environment,
        ScopeType,
        ScopeId,
        RiskLevel,
        ReasonCode,
        WrittenReason,
        TicketReference,
        IdempotencyRecordId,
        ExpectedResourceVersion,
        ImpactPreviewId,
        ImpactDigest,
        OriginalCommandId,
        RequestedExecutionAt,
        ExpiresAt,
        CorrelationId
    });
}

internal static class ApprovalBindingDigest
{
    public static string SerializeSnapshot(
        string commandDigest,
        string impactDigest,
        long commandRowVersion,
        string restrictionDigest) => CanonicalJson.Serialize(new
        {
            CommandDigest = commandDigest,
            ImpactDigest = impactDigest,
            CommandRowVersion = commandRowVersion,
            RestrictionDigest = restrictionDigest
        });

    public static string Compute(
        string commandDigest,
        string impactDigest,
        long commandRowVersion,
        string restrictionDigest) => CanonicalJson.Sha256(new
        {
            CommandDigest = commandDigest,
            ImpactDigest = impactDigest,
            CommandRowVersion = commandRowVersion,
            RestrictionDigest = restrictionDigest
        });
}
