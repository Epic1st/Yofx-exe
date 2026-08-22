using System.Collections.Frozen;
using YO4X.Admin.Application;
using YO4X.Admin.Postgres;
using YO4X.Authorization;

namespace YO4X.Admin.Postgres.Tests;

public sealed class AdminAuthorizationSnapshotTests
{
    private static readonly Guid TenantId = Guid.Parse("018f0000-0000-7000-8000-000000000001");
    private static readonly Guid ActorId = Guid.Parse("018f0000-0000-7000-8000-000000000002");
    private static readonly Guid SessionId = Guid.Parse("018f0000-0000-7000-8000-000000000003");
    private static readonly Guid DeploymentId = Guid.Parse("018f0000-0000-7000-8000-000000000004");
    private static readonly FrozenSet<string> CookiePermissions =
        new[] { "cookie.only.permission" }.ToFrozenSet(StringComparer.Ordinal);

    [Fact]
    public void DatabaseGrantsNotCookiePermissionClaimsAreAuthoritative()
    {
        AdminSecuritySnapshot snapshot = CreateSnapshot(
            new AdminGrant(
                Guid.NewGuid(),
                AdminPermissions.ReadCommands,
                "production",
                "deployment",
                DeploymentId.ToString("D")));

        Assert.Contains(AdminPermissions.ReadCommands, snapshot.EffectivePermissions);
        Assert.DoesNotContain("cookie.only.permission", snapshot.EffectivePermissions);
        Assert.Throws<AdminAuthorizationDeniedException>(() =>
            snapshot.RequirePermission("cookie.only.permission"));
    }

    [Fact]
    public void ExactScopeAndEnvironmentAreRequired()
    {
        AdminSecuritySnapshot snapshot = CreateSnapshot(
            new AdminGrant(
                Guid.NewGuid(),
                AdminPermissions.ReadCommands,
                "production",
                "deployment",
                DeploymentId.ToString("D")));

        Assert.True(snapshot.CanAccess(
            AdminPermissions.ReadCommands,
            Resource("production", DeploymentId)));
        Assert.False(snapshot.CanAccess(
            AdminPermissions.ReadCommands,
            Resource("pilot", DeploymentId)));
        Assert.False(snapshot.CanAccess(
            AdminPermissions.ReadCommands,
            Resource("production", Guid.NewGuid())));
    }

    [Fact]
    public void GlobalGrantIsStillBoundToItsExactEnvironment()
    {
        AdminSecuritySnapshot snapshot = CreateSnapshot(
            new AdminGrant(
                Guid.NewGuid(),
                AdminPermissions.ReadApprovals,
                "production",
                "global",
                null));

        Assert.True(snapshot.CanAccess(
            AdminPermissions.ReadApprovals,
            Resource("production", Guid.NewGuid())));
        Assert.False(snapshot.CanAccess(
            AdminPermissions.ReadApprovals,
            Resource("pilot", Guid.NewGuid())));
    }

    private static AdminSecuritySnapshot CreateSnapshot(params AdminGrant[] grants)
    {
        var authenticatedAt = new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
        var actor = new AdminActor(
            TenantId,
            ActorId,
            SessionId,
            "production",
            AuthenticationAssurance.PhishingResistant,
            ManagedDevice: true,
            authenticatedAt,
            CookiePermissions);
        return new AdminSecuritySnapshot(
            actor,
            "production",
            new AdminSessionEvidence("webauthn", true, authenticatedAt),
            grants,
            authenticatedAt);
    }

    private static AdminResourceScope Resource(string environment, Guid deploymentId) => new(
        environment,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["environment"] = environment,
            ["deployment"] = deploymentId.ToString("D")
        },
        Version: 7);
}
