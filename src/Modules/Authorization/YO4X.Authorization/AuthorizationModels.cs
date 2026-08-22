using YO4X.BuildingBlocks;

namespace YO4X.Authorization;

public enum ActorKind
{
    User,
    Staff,
    Workload,
    Emergency,
    System
}

public enum ActorStatus
{
    Active,
    Suspended,
    SecurityLocked,
    Disabled
}

public enum AuthenticationAssurance
{
    Unknown = 0,
    Password = 1,
    MultiFactor = 2,
    PhishingResistant = 3
}

public enum ActionRisk
{
    Low,
    Sensitive,
    High,
    Critical
}

public enum PurposeKind
{
    SupportCase,
    Incident,
    Change,
    SecurityInvestigation,
    AccessReview,
    Operations
}

public sealed record ActorContext
{
    public ActorContext(
        Guid actorId,
        ActorKind kind,
        ActorStatus status,
        Guid? tenantId,
        AuthenticationAssurance assurance,
        bool managedDevice,
        DateTimeOffset authenticatedAt)
    {
        if (actorId == Guid.Empty)
        {
            throw new DomainException(
                "AUTHORIZATION_ACTOR_ID_EMPTY",
                "An actor identifier cannot be empty.");
        }

        if (!Enum.IsDefined(kind)
            || !Enum.IsDefined(status)
            || !Enum.IsDefined(assurance))
        {
            throw new DomainException(
                "AUTHORIZATION_ACTOR_CONTEXT_INVALID",
                "The actor context contains an unknown value.");
        }

        if (kind == ActorKind.User && tenantId is null)
        {
            throw new DomainException(
                "AUTHORIZATION_USER_TENANT_REQUIRED",
                "A user actor must be bound to a tenant.");
        }

        ActorId = actorId;
        Kind = kind;
        Status = status;
        TenantId = tenantId;
        Assurance = assurance;
        ManagedDevice = managedDevice;
        AuthenticatedAt = authenticatedAt.ToUniversalTime();
    }

    public Guid ActorId { get; }

    public ActorKind Kind { get; }

    public ActorStatus Status { get; }

    public Guid? TenantId { get; }

    public AuthenticationAssurance Assurance { get; }

    public bool ManagedDevice { get; }

    public DateTimeOffset AuthenticatedAt { get; }
}

public sealed record ProtectedResource
{
    public ProtectedResource(
        string resourceType,
        Guid? resourceId,
        Guid? tenantId,
        string environment,
        string? region,
        long? version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(environment);
        if (resourceId == Guid.Empty || tenantId == Guid.Empty || version < 0)
        {
            throw new DomainException(
                "AUTHORIZATION_RESOURCE_CONTEXT_INVALID",
                "The protected resource context contains an invalid identifier or version.");
        }

        ResourceType = resourceType.Trim();
        ResourceId = resourceId;
        TenantId = tenantId;
        Environment = environment.Trim();
        Region = NormalizeOptional(region);
        Version = version;
    }

    public string ResourceType { get; }

    public Guid? ResourceId { get; }

    public Guid? TenantId { get; }

    public string Environment { get; }

    public string? Region { get; }

    public long? Version { get; }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record AuthorizationScope
{
    public AuthorizationScope(
        bool global,
        Guid? tenantId = null,
        string? environment = null,
        string? region = null,
        string? resourceType = null,
        Guid? resourceId = null)
    {
        if (tenantId == Guid.Empty || resourceId == Guid.Empty)
        {
            throw new DomainException(
                "AUTHORIZATION_SCOPE_ID_INVALID",
                "A scope identifier cannot be empty.");
        }

        if (global && (tenantId is not null
            || environment is not null
            || region is not null
            || resourceType is not null
            || resourceId is not null))
        {
            throw new DomainException(
                "AUTHORIZATION_GLOBAL_SCOPE_MIXED",
                "A global scope cannot also contain resource constraints.");
        }

        if (!global && tenantId is null
            && string.IsNullOrWhiteSpace(environment)
            && string.IsNullOrWhiteSpace(region)
            && string.IsNullOrWhiteSpace(resourceType)
            && resourceId is null)
        {
            throw new DomainException(
                "AUTHORIZATION_SCOPE_EMPTY",
                "A non-global authorization scope requires at least one constraint.");
        }

        Global = global;
        TenantId = tenantId;
        Environment = NormalizeOptional(environment);
        Region = NormalizeOptional(region);
        ResourceType = NormalizeOptional(resourceType);
        ResourceId = resourceId;
    }

    public bool Global { get; }

    public Guid? TenantId { get; }

    public string? Environment { get; }

    public string? Region { get; }

    public string? ResourceType { get; }

    public Guid? ResourceId { get; }

    public static AuthorizationScope GlobalScope() => new(global: true);

    public static AuthorizationScope ForTenant(Guid tenantId, string? environment = null) =>
        new(global: false, tenantId, environment);

    public bool Contains(ProtectedResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (Global)
        {
            return true;
        }

        return Matches(TenantId, resource.TenantId)
            && Matches(Environment, resource.Environment)
            && Matches(Region, resource.Region)
            && Matches(ResourceType, resource.ResourceType)
            && Matches(ResourceId, resource.ResourceId);
    }

    private static bool Matches<T>(T? constraint, T? actual)
        where T : struct => constraint is null || EqualityComparer<T?>.Default.Equals(constraint, actual);

    private static bool Matches(string? constraint, string? actual) =>
        constraint is null || string.Equals(constraint, actual, StringComparison.Ordinal);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record AuthorizationPurpose
{
    public AuthorizationPurpose(PurposeKind kind, string reason, string? ticketReference)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new DomainException(
                "AUTHORIZATION_PURPOSE_UNKNOWN",
                "The authorization purpose is unknown.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Kind = kind;
        Reason = reason.Trim();
        TicketReference = string.IsNullOrWhiteSpace(ticketReference)
            ? null
            : ticketReference.Trim();
    }

    public PurposeKind Kind { get; }

    public string Reason { get; }

    public string? TicketReference { get; }
}

public sealed record PermissionGrant
{
    public PermissionGrant(
        Guid grantId,
        Guid actorId,
        string permission,
        AuthorizationScope scope,
        DateTimeOffset startsAt,
        DateTimeOffset? expiresAt,
        PurposeKind? boundPurpose = null)
    {
        if (grantId == Guid.Empty || actorId == Guid.Empty)
        {
            throw new DomainException(
                "AUTHORIZATION_GRANT_ID_INVALID",
                "Grant and actor identifiers cannot be empty.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        ArgumentNullException.ThrowIfNull(scope);
        if (expiresAt is not null && expiresAt <= startsAt)
        {
            throw new DomainException(
                "AUTHORIZATION_GRANT_EXPIRY_INVALID",
                "A grant must expire after it starts.");
        }

        if (boundPurpose is not null && !Enum.IsDefined(boundPurpose.Value))
        {
            throw new DomainException(
                "AUTHORIZATION_GRANT_PURPOSE_UNKNOWN",
                "The grant purpose is unknown.");
        }

        GrantId = grantId;
        ActorId = actorId;
        Permission = permission.Trim();
        Scope = scope;
        StartsAt = startsAt.ToUniversalTime();
        ExpiresAt = expiresAt?.ToUniversalTime();
        BoundPurpose = boundPurpose;
    }

    public Guid GrantId { get; }

    public Guid ActorId { get; }

    public string Permission { get; }

    public AuthorizationScope Scope { get; }

    public DateTimeOffset StartsAt { get; }

    public DateTimeOffset? ExpiresAt { get; }

    public PurposeKind? BoundPurpose { get; }

    public bool IsActiveAt(DateTimeOffset now) =>
        now >= StartsAt && (ExpiresAt is null || now < ExpiresAt);

    public bool MatchesPurpose(AuthorizationPurpose? purpose) =>
        BoundPurpose is null || purpose?.Kind == BoundPurpose;
}

public sealed record AuthorizationRequirement
{
    public AuthorizationRequirement(
        string permission,
        AuthenticationAssurance minimumAssurance,
        bool managedDeviceRequired,
        TimeSpan? maximumSessionAge,
        bool purposeRequired,
        bool ticketRequired,
        Guid? separatedFromActorId,
        bool expectedVersionRequired,
        ActionRisk risk)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        if (!Enum.IsDefined(minimumAssurance) || !Enum.IsDefined(risk))
        {
            throw new DomainException(
                "AUTHORIZATION_REQUIREMENT_INVALID",
                "The authorization requirement contains an unknown value.");
        }

        if (maximumSessionAge is not null && maximumSessionAge <= TimeSpan.Zero)
        {
            throw new DomainException(
                "AUTHORIZATION_SESSION_AGE_INVALID",
                "Maximum session age must be positive.");
        }

        if (separatedFromActorId == Guid.Empty)
        {
            throw new DomainException(
                "AUTHORIZATION_SEPARATION_ACTOR_INVALID",
                "A separation-of-duties actor identifier cannot be empty.");
        }

        Permission = permission.Trim();
        MinimumAssurance = minimumAssurance;
        ManagedDeviceRequired = managedDeviceRequired;
        MaximumSessionAge = maximumSessionAge;
        PurposeRequired = purposeRequired;
        TicketRequired = ticketRequired;
        SeparatedFromActorId = separatedFromActorId;
        ExpectedVersionRequired = expectedVersionRequired;
        Risk = risk;
    }

    public string Permission { get; }

    public AuthenticationAssurance MinimumAssurance { get; }

    public bool ManagedDeviceRequired { get; }

    public TimeSpan? MaximumSessionAge { get; }

    public bool PurposeRequired { get; }

    public bool TicketRequired { get; }

    public Guid? SeparatedFromActorId { get; }

    public bool ExpectedVersionRequired { get; }

    public ActionRisk Risk { get; }
}
