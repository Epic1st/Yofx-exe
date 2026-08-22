using YO4X.BuildingBlocks;

namespace YO4X.Authorization;

public sealed record AuthorizationEvaluation
{
    public AuthorizationEvaluation(
        ActorContext actor,
        ProtectedResource resource,
        AuthorizationRequirement requirement,
        IEnumerable<PermissionGrant> grants,
        AuthorizationPurpose? purpose,
        long? expectedVersion,
        bool restrictivePolicyAllows,
        string effectivePolicyDigest,
        DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentException.ThrowIfNullOrWhiteSpace(effectivePolicyDigest);
        if (expectedVersion < 0)
        {
            throw new DomainException(
                "AUTHORIZATION_EXPECTED_VERSION_INVALID",
                "An expected resource version cannot be negative.");
        }

        if (effectivePolicyDigest.Length != 64
            || effectivePolicyDigest.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new DomainException(
                "AUTHORIZATION_POLICY_DIGEST_INVALID",
                "The effective policy digest must be a hexadecimal SHA-256 value.");
        }

        Actor = actor;
        Resource = resource;
        Requirement = requirement;
        Grants = Array.AsReadOnly(grants.ToArray());
        Purpose = purpose;
        ExpectedVersion = expectedVersion;
        RestrictivePolicyAllows = restrictivePolicyAllows;
        EffectivePolicyDigest = effectivePolicyDigest.Trim();
        EvaluatedAt = evaluatedAt.ToUniversalTime();
    }

    public ActorContext Actor { get; }

    public ProtectedResource Resource { get; }

    public AuthorizationRequirement Requirement { get; }

    public IReadOnlyList<PermissionGrant> Grants { get; }

    public AuthorizationPurpose? Purpose { get; }

    public long? ExpectedVersion { get; }

    public bool RestrictivePolicyAllows { get; }

    public string EffectivePolicyDigest { get; }

    public DateTimeOffset EvaluatedAt { get; }
}

public sealed record AuthorizationDecision(
    bool Allowed,
    Guid? MatchedGrantId,
    IReadOnlyList<string> DenialCodes,
    string EffectivePolicyDigest,
    string InputSnapshotDigest,
    DateTimeOffset EvaluatedAt);

public static class AuthorizationDecisionEngine
{
    public static AuthorizationDecision Evaluate(AuthorizationEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);

        var denials = new List<string>();
        ActorContext actor = evaluation.Actor;
        ProtectedResource resource = evaluation.Resource;
        AuthorizationRequirement requirement = evaluation.Requirement;
        DateTimeOffset now = evaluation.EvaluatedAt;

        AddDenialIf(actor.Status != ActorStatus.Active, "ACTOR_NOT_ACTIVE");
        AddDenialIf(
            (int)actor.Assurance < (int)requirement.MinimumAssurance,
            "AUTHENTICATION_ASSURANCE_INSUFFICIENT");
        AddDenialIf(
            requirement.ManagedDeviceRequired && !actor.ManagedDevice,
            "MANAGED_DEVICE_REQUIRED");

        if (requirement.MaximumSessionAge is not null)
        {
            AddDenialIf(
                actor.AuthenticatedAt > now
                    || now - actor.AuthenticatedAt > requirement.MaximumSessionAge,
                "STEP_UP_AUTHENTICATION_REQUIRED");
        }

        if (actor.Kind == ActorKind.User)
        {
            AddDenialIf(
                resource.TenantId is null || actor.TenantId != resource.TenantId,
                "TENANT_OR_RESOURCE_SCOPE_DENIED");
        }
        else if (actor.Kind == ActorKind.Workload
            && actor.TenantId is not null
            && resource.TenantId != actor.TenantId)
        {
            denials.Add("TENANT_OR_RESOURCE_SCOPE_DENIED");
        }

        AddDenialIf(
            requirement.PurposeRequired && evaluation.Purpose is null,
            "PURPOSE_REQUIRED");
        AddDenialIf(
            requirement.TicketRequired
                && string.IsNullOrWhiteSpace(evaluation.Purpose?.TicketReference),
            "TICKET_REFERENCE_REQUIRED");
        AddDenialIf(
            requirement.SeparatedFromActorId == actor.ActorId,
            "SEPARATION_OF_DUTIES_VIOLATION");

        if (requirement.ExpectedVersionRequired)
        {
            AddDenialIf(evaluation.ExpectedVersion is null, "EXPECTED_VERSION_REQUIRED");
            AddDenialIf(
                evaluation.ExpectedVersion is not null
                    && resource.Version != evaluation.ExpectedVersion,
                "RESOURCE_VERSION_CONFLICT");
        }

        AddDenialIf(!evaluation.RestrictivePolicyAllows, "RESTRICTIVE_POLICY_DENIED");

        PermissionGrant? matchingGrant = evaluation.Grants
            .Where(grant => grant.ActorId == actor.ActorId
                && string.Equals(grant.Permission, requirement.Permission, StringComparison.Ordinal)
                && grant.IsActiveAt(now)
                && grant.Scope.Contains(resource)
                && grant.MatchesPurpose(evaluation.Purpose))
            .OrderBy(grant => grant.GrantId)
            .FirstOrDefault();

        AddDenialIf(matchingGrant is null, "PERMISSION_OR_RESOURCE_SCOPE_DENIED");

        string inputDigest = CanonicalJson.Sha256(new
        {
            Actor = new
            {
                actor.ActorId,
                Kind = actor.Kind.ToString(),
                Status = actor.Status.ToString(),
                actor.TenantId,
                Assurance = actor.Assurance.ToString(),
                actor.ManagedDevice,
                actor.AuthenticatedAt
            },
            Resource = new
            {
                resource.ResourceType,
                resource.ResourceId,
                resource.TenantId,
                resource.Environment,
                resource.Region,
                resource.Version
            },
            Requirement = new
            {
                requirement.Permission,
                MinimumAssurance = requirement.MinimumAssurance.ToString(),
                requirement.ManagedDeviceRequired,
                requirement.MaximumSessionAge,
                requirement.PurposeRequired,
                requirement.TicketRequired,
                requirement.SeparatedFromActorId,
                requirement.ExpectedVersionRequired,
                Risk = requirement.Risk.ToString()
            },
            Purpose = evaluation.Purpose,
            Grants = evaluation.Grants
                .OrderBy(grant => grant.GrantId)
                .Select(grant => new
                {
                    grant.GrantId,
                    grant.ActorId,
                    grant.Permission,
                    Scope = new
                    {
                        grant.Scope.Global,
                        grant.Scope.TenantId,
                        grant.Scope.Environment,
                        grant.Scope.Region,
                        grant.Scope.ResourceType,
                        grant.Scope.ResourceId
                    },
                    grant.StartsAt,
                    grant.ExpiresAt,
                    BoundPurpose = grant.BoundPurpose?.ToString()
                }).ToArray(),
            evaluation.ExpectedVersion,
            evaluation.RestrictivePolicyAllows,
            evaluation.EffectivePolicyDigest,
            evaluation.EvaluatedAt
        });

        string[] distinctDenials = denials.Distinct(StringComparer.Ordinal).ToArray();
        return new AuthorizationDecision(
            distinctDenials.Length == 0,
            distinctDenials.Length == 0 ? matchingGrant?.GrantId : null,
            Array.AsReadOnly(distinctDenials),
            evaluation.EffectivePolicyDigest,
            inputDigest,
            now);

        void AddDenialIf(bool condition, string code)
        {
            if (condition)
            {
                denials.Add(code);
            }
        }
    }
}
