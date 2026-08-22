using YO4X.Authorization;
using YO4X.BuildingBlocks;

namespace YO4X.Domain.Tests;

public sealed class AuthorizationDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ExactPermissionScopeAssuranceAndPolicyAllAuthorize()
    {
        Guid tenantId = Identifiers.NewId();
        ActorContext actor = CreateActor(tenantId);
        ProtectedResource resource = CreateResource(tenantId);
        PermissionGrant grant = CreateGrant(actor.ActorId, AuthorizationScope.ForTenant(tenantId, "DEMO"));

        AuthorizationDecision decision = Evaluate(actor, resource, [grant]);

        Assert.True(decision.Allowed);
        Assert.Equal(grant.GrantId, decision.MatchedGrantId);
        Assert.Empty(decision.DenialCodes);
        Assert.Equal(64, decision.InputSnapshotDigest.Length);
    }

    [Fact]
    public void UserTenantOwnershipCannotBeBypassedByGlobalPermission()
    {
        ActorContext actor = CreateActor(Identifiers.NewId());
        ProtectedResource otherTenantResource = CreateResource(Identifiers.NewId());
        PermissionGrant globalGrant = CreateGrant(actor.ActorId, AuthorizationScope.GlobalScope());

        AuthorizationDecision decision = Evaluate(actor, otherTenantResource, [globalGrant]);

        Assert.False(decision.Allowed);
        Assert.Contains("TENANT_OR_RESOURCE_SCOPE_DENIED", decision.DenialCodes);
    }

    [Fact]
    public void StaffPermissionDoesNotEscapeItsResourceScope()
    {
        Guid actorId = Identifiers.NewId();
        ActorContext actor = new(
            actorId,
            ActorKind.Staff,
            ActorStatus.Active,
            tenantId: null,
            AuthenticationAssurance.PhishingResistant,
            managedDevice: true,
            Now);
        Guid allowedTenant = Identifiers.NewId();
        PermissionGrant grant = CreateGrant(actorId, AuthorizationScope.ForTenant(allowedTenant, "DEMO"));

        AuthorizationDecision decision = Evaluate(
            actor,
            CreateResource(Identifiers.NewId()),
            [grant]);

        Assert.False(decision.Allowed);
        Assert.Contains("PERMISSION_OR_RESOURCE_SCOPE_DENIED", decision.DenialCodes);
    }

    [Fact]
    public void ExpiredOrNotYetActiveGrantFailsClosed()
    {
        Guid tenantId = Identifiers.NewId();
        ActorContext actor = CreateActor(tenantId);
        ProtectedResource resource = CreateResource(tenantId);
        PermissionGrant expired = new(
            Identifiers.NewId(),
            actor.ActorId,
            "DEPLOYMENT_CLOSE_ONLY",
            AuthorizationScope.ForTenant(tenantId, "DEMO"),
            Now.AddHours(-2),
            Now,
            PurposeKind.Incident);

        AuthorizationDecision decision = Evaluate(actor, resource, [expired]);

        Assert.False(decision.Allowed);
        Assert.Contains("PERMISSION_OR_RESOURCE_SCOPE_DENIED", decision.DenialCodes);
    }

    [Theory]
    [InlineData(ActorStatus.Suspended)]
    [InlineData(ActorStatus.SecurityLocked)]
    [InlineData(ActorStatus.Disabled)]
    public void NonActiveActorIsDenied(ActorStatus status)
    {
        Guid tenantId = Identifiers.NewId();
        ActorContext actor = new(
            Identifiers.NewId(),
            ActorKind.User,
            status,
            tenantId,
            AuthenticationAssurance.PhishingResistant,
            managedDevice: true,
            Now);
        PermissionGrant grant = CreateGrant(actor.ActorId, AuthorizationScope.ForTenant(tenantId, "DEMO"));

        AuthorizationDecision decision = Evaluate(actor, CreateResource(tenantId), [grant]);

        Assert.False(decision.Allowed);
        Assert.Contains("ACTOR_NOT_ACTIVE", decision.DenialCodes);
    }

    [Fact]
    public void AssuranceManagedDeviceAndFreshSessionAreIndependentRequirements()
    {
        Guid tenantId = Identifiers.NewId();
        ActorContext actor = new(
            Identifiers.NewId(),
            ActorKind.User,
            ActorStatus.Active,
            tenantId,
            AuthenticationAssurance.Password,
            managedDevice: false,
            Now.AddHours(-2));
        PermissionGrant grant = CreateGrant(actor.ActorId, AuthorizationScope.ForTenant(tenantId, "DEMO"));

        AuthorizationDecision decision = Evaluate(actor, CreateResource(tenantId), [grant]);

        Assert.False(decision.Allowed);
        Assert.Contains("AUTHENTICATION_ASSURANCE_INSUFFICIENT", decision.DenialCodes);
        Assert.Contains("MANAGED_DEVICE_REQUIRED", decision.DenialCodes);
        Assert.Contains("STEP_UP_AUTHENTICATION_REQUIRED", decision.DenialCodes);
    }

    [Fact]
    public void PurposeTicketAndPurposeBoundGrantAreEnforced()
    {
        Guid tenantId = Identifiers.NewId();
        ActorContext actor = CreateActor(tenantId);
        ProtectedResource resource = CreateResource(tenantId);
        PermissionGrant grant = CreateGrant(actor.ActorId, AuthorizationScope.ForTenant(tenantId, "DEMO"));

        AuthorizationDecision missing = Evaluate(actor, resource, [grant], omitPurpose: true);
        AuthorizationDecision wrongPurpose = Evaluate(
            actor,
            resource,
            [grant],
            new AuthorizationPurpose(PurposeKind.SupportCase, "Support", "CASE-1"));

        Assert.False(missing.Allowed);
        Assert.Contains("PURPOSE_REQUIRED", missing.DenialCodes);
        Assert.Contains("TICKET_REFERENCE_REQUIRED", missing.DenialCodes);
        Assert.False(wrongPurpose.Allowed);
        Assert.Contains("PERMISSION_OR_RESOURCE_SCOPE_DENIED", wrongPurpose.DenialCodes);
    }

    [Fact]
    public void SeparationOfDutiesExpectedVersionAndPolicyAreEnforcedTogether()
    {
        Guid tenantId = Identifiers.NewId();
        ActorContext actor = CreateActor(tenantId);
        ProtectedResource resource = CreateResource(tenantId, version: 8);
        PermissionGrant grant = CreateGrant(actor.ActorId, AuthorizationScope.ForTenant(tenantId, "DEMO"));
        AuthorizationRequirement requirement = CreateRequirement(separatedFromActorId: actor.ActorId);

        AuthorizationDecision decision = Evaluate(
            actor,
            resource,
            [grant],
            expectedVersion: 7,
            restrictivePolicyAllows: false,
            requirement: requirement);

        Assert.False(decision.Allowed);
        Assert.Contains("SEPARATION_OF_DUTIES_VIOLATION", decision.DenialCodes);
        Assert.Contains("RESOURCE_VERSION_CONFLICT", decision.DenialCodes);
        Assert.Contains("RESTRICTIVE_POLICY_DENIED", decision.DenialCodes);
    }

    [Fact]
    public void MissingExpectedVersionFailsAsAPrecondition()
    {
        Guid tenantId = Identifiers.NewId();
        ActorContext actor = CreateActor(tenantId);
        PermissionGrant grant = CreateGrant(actor.ActorId, AuthorizationScope.ForTenant(tenantId, "DEMO"));

        AuthorizationDecision decision = Evaluate(
            actor,
            CreateResource(tenantId),
            [grant],
            expectedVersion: null);

        Assert.False(decision.Allowed);
        Assert.Contains("EXPECTED_VERSION_REQUIRED", decision.DenialCodes);
    }

    [Fact]
    public void WorkloadTenantBindingAlsoFailsClosed()
    {
        Guid workloadTenant = Identifiers.NewId();
        Guid actorId = Identifiers.NewId();
        ActorContext actor = new(
            actorId,
            ActorKind.Workload,
            ActorStatus.Active,
            workloadTenant,
            AuthenticationAssurance.PhishingResistant,
            managedDevice: true,
            Now);
        PermissionGrant globalGrant = CreateGrant(actorId, AuthorizationScope.GlobalScope());

        AuthorizationDecision decision = Evaluate(
            actor,
            CreateResource(Identifiers.NewId()),
            [globalGrant]);

        Assert.False(decision.Allowed);
        Assert.Contains("TENANT_OR_RESOURCE_SCOPE_DENIED", decision.DenialCodes);
    }

    [Fact]
    public void MatchingGrantSelectionAndInputDigestAreOrderIndependent()
    {
        Guid tenantId = Identifiers.NewId();
        ActorContext actor = CreateActor(tenantId);
        ProtectedResource resource = CreateResource(tenantId);
        PermissionGrant first = new(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            actor.ActorId,
            "DEPLOYMENT_CLOSE_ONLY",
            AuthorizationScope.ForTenant(tenantId, "DEMO"),
            Now.AddHours(-1),
            Now.AddHours(1),
            PurposeKind.Incident);
        PermissionGrant second = new(
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            actor.ActorId,
            "DEPLOYMENT_CLOSE_ONLY",
            AuthorizationScope.ForTenant(tenantId, "DEMO"),
            Now.AddHours(-1),
            Now.AddHours(1),
            PurposeKind.Incident);

        AuthorizationDecision forward = Evaluate(actor, resource, [first, second]);
        AuthorizationDecision reverse = Evaluate(actor, resource, [second, first]);

        Assert.True(forward.Allowed);
        Assert.Equal(first.GrantId, forward.MatchedGrantId);
        Assert.Equal(forward.MatchedGrantId, reverse.MatchedGrantId);
        Assert.Equal(forward.InputSnapshotDigest, reverse.InputSnapshotDigest);
    }

    private static AuthorizationDecision Evaluate(
        ActorContext actor,
        ProtectedResource resource,
        IEnumerable<PermissionGrant> grants,
        AuthorizationPurpose? purpose = null,
        long? expectedVersion = 7,
        bool restrictivePolicyAllows = true,
        AuthorizationRequirement? requirement = null,
        bool omitPurpose = false) => AuthorizationDecisionEngine.Evaluate(
            new AuthorizationEvaluation(
                actor,
                resource,
                requirement ?? CreateRequirement(),
                grants,
                omitPurpose
                    ? null
                    : purpose ?? new AuthorizationPurpose(
                        PurposeKind.Incident,
                        "Contain an incident",
                        "INC-42"),
                expectedVersion,
                restrictivePolicyAllows,
                CanonicalJson.Sha256(new { Policy = "effective-v1" }),
                Now));

    private static ActorContext CreateActor(Guid tenantId) => new(
        Identifiers.NewId(),
        ActorKind.User,
        ActorStatus.Active,
        tenantId,
        AuthenticationAssurance.PhishingResistant,
        managedDevice: true,
        Now.AddMinutes(-2));

    private static ProtectedResource CreateResource(Guid tenantId, long version = 7) => new(
        "DEPLOYMENT",
        Identifiers.NewId(),
        tenantId,
        "DEMO",
        "region-1",
        version);

    private static PermissionGrant CreateGrant(Guid actorId, AuthorizationScope scope) => new(
        Identifiers.NewId(),
        actorId,
        "DEPLOYMENT_CLOSE_ONLY",
        scope,
        Now.AddHours(-1),
        Now.AddHours(1),
        PurposeKind.Incident);

    private static AuthorizationRequirement CreateRequirement(Guid? separatedFromActorId = null) => new(
        "DEPLOYMENT_CLOSE_ONLY",
        AuthenticationAssurance.MultiFactor,
        managedDeviceRequired: true,
        maximumSessionAge: TimeSpan.FromMinutes(15),
        purposeRequired: true,
        ticketRequired: true,
        separatedFromActorId,
        expectedVersionRequired: true,
        ActionRisk.High);
}
