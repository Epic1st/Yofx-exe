using YO4X.Admin.Application;
using YO4X.Admin.Postgres;
using YO4X.BuildingBlocks;
using YO4X.Commands;
using YO4X.Policy;

namespace YO4X.Admin.Postgres.Tests;

public sealed class AdminBindingTests
{
    private static readonly Guid TenantId = Guid.Parse("018f0000-0000-7000-8000-000000000010");
    private static readonly Guid ActorId = Guid.Parse("018f0000-0000-7000-8000-000000000011");

    [Fact]
    public void PolicyVectorDocumentRoundTripsWithExactDigest()
    {
        var vector = new ExecutionSafetyPolicyVector(
            false,
            false,
            false,
            true,
            true,
            true,
            true,
            LeaseMode.Revoke,
            WorkerAction.Drain | WorkerAction.Fence | WorkerAction.Replace,
            CredentialMode.DisableNewUse,
            PackageEligibility.NoNewAssignment);

        PolicyVectorDocument document = vector.ToDocument();
        string json = CanonicalJson.Serialize(document);
        ExecutionSafetyPolicyVector restored = AdminStorageValues.ParsePolicyDocument(json).ToVector();

        Assert.Equal(vector, restored);
        Assert.Equal(vector.ComputeDigest(), restored.ComputeDigest());
        Assert.Equal(vector.ComputeDigest(), CanonicalJson.Sha256(document));
    }

    [Fact]
    public void ApprovalBindingChangesForEveryBoundDimension()
    {
        string command = new('a', 64);
        string impact = new('b', 64);
        string restriction = new('c', 64);
        string baseline = ApprovalBindingDigest.Compute(command, impact, 2, restriction);

        string[] changed =
        [
            ApprovalBindingDigest.Compute(new string('d', 64), impact, 2, restriction),
            ApprovalBindingDigest.Compute(command, new string('d', 64), 2, restriction),
            ApprovalBindingDigest.Compute(command, impact, 3, restriction),
            ApprovalBindingDigest.Compute(command, impact, 2, new string('d', 64))
        ];

        Assert.All(changed, digest => Assert.NotEqual(baseline, digest));
        Assert.Equal(changed.Length, changed.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ApprovalSummaryRecomputesTheExactFourFieldBindingDigest()
    {
        string commandDigest = new('a', 64);
        string impactDigest = new('b', 64);
        string restrictionDigest = new('c', 64);
        const long commandVersion = 2;
        var now = new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
        AdminCommandRecord command = CreateCommandRecord(
            commandDigest,
            impactDigest,
            commandVersion,
            now);
        var approval = new ApprovalRecord(
            Guid.NewGuid(),
            command.Id,
            ActorId,
            "admin.containment.two_person.v1",
            command.ImpactPreviewId!.Value,
            commandDigest,
            impactDigest,
            commandVersion,
            restrictionDigest,
            ApprovalBindingDigest.SerializeSnapshot(
                commandDigest,
                impactDigest,
                commandVersion,
                restrictionDigest),
            new string('f', 64),
            1,
            "phishing_resistant",
            true,
            300,
            "pending",
            null,
            now.AddMinutes(5),
            0,
            now,
            0,
            command);

        ApprovalSummary summary = approval.ToSummary(now);

        Assert.Equal(
            ApprovalBindingDigest.Compute(
                commandDigest,
                impactDigest,
                commandVersion,
                restrictionDigest),
            summary.BindingDigest);
        Assert.NotEqual(approval.BindingDigest, summary.BindingDigest);
    }

    [Fact]
    public void ApprovalBindingSnapshotHashesToThePublishedDigest()
    {
        string command = new('a', 64);
        string impact = new('b', 64);
        string restriction = new('c', 64);
        string snapshot = ApprovalBindingDigest.SerializeSnapshot(command, impact, 2, restriction);

        Assert.Equal(
            ApprovalBindingDigest.Compute(command, impact, 2, restriction),
            CanonicalJson.Sha256(System.Text.Json.Nodes.JsonNode.Parse(snapshot)));
    }

    [Fact]
    public void ImpactPreviewDetectsAnySnapshotMutation()
    {
        var now = new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
        var target = new ImpactTargetSnapshot(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "deployment",
            17,
            "reconciled",
            true,
            Guid.NewGuid(),
            4);
        ImpactPreviewRecord preview = ImpactPreviewRecord.Create(
            Guid.NewGuid(),
            TenantId,
            ActorId,
            new ScopeInput("deployment", target.ResourceId.ToString("D")),
            [target],
            new string('e', 64),
            now,
            now.AddMinutes(10));

        Assert.True(preview.HasValidDigest());
        Assert.False((preview with { TargetSnapshotJson = "[]" }).HasValidDigest());
        Assert.False((preview with { ResourceVersionWatermark = new string('f', 64) }).HasValidDigest());
        Assert.False((preview with { ExpiresAt = preview.ExpiresAt.AddSeconds(1) }).HasValidDigest());
    }

    [Fact]
    public void CommandDigestBindsImmutableRequestFields()
    {
        AdminCommandBinding baseline = CreateCommandBinding();
        string digest = baseline.ComputeDigest();
        AdminCommandBinding[] variants =
        [
            baseline with { ExpectedResourceVersion = 18 },
            baseline with { ScopeId = Guid.NewGuid().ToString("D") },
            baseline with { WrittenReason = "different audited reason" },
            baseline with { RestrictionVectorJson = CanonicalJson.Serialize(ExecutionSafetyPolicyVector.Unrestricted.ToDocument()) },
            baseline with { ImpactDigest = new string('f', 64) },
            baseline with { OriginalCommandId = Guid.NewGuid() }
        ];

        Assert.All(variants, variant => Assert.NotEqual(digest, variant.ComputeDigest()));
    }

    [Fact]
    public void StorageMappingsRoundTripEveryAllowlistedCommandType()
    {
        foreach (CommandType type in Enum.GetValues<CommandType>())
        {
            Assert.Equal(type, AdminStorageValues.ParseCommandType(type.ToStorageValue()));
        }
    }

    private static AdminCommandBinding CreateCommandBinding()
    {
        Guid commandId = Guid.NewGuid();
        Guid previewId = Guid.NewGuid();
        var vector = new ExecutionSafetyPolicyVector(
            true, false, false, true, true, true, true,
            LeaseMode.Normal,
            WorkerAction.StopAfterFlat,
            CredentialMode.Normal,
            PackageEligibility.Eligible);
        return new AdminCommandBinding(
            commandId,
            TenantId,
            CommandType.StopAfterFlat.ToStorageValue(),
            new string('1', 64),
            CanonicalJson.Serialize(vector.ToDocument()),
            [CommandType.ReleaseContainment.ToStorageValue()],
            ActorId,
            Guid.NewGuid(),
            "production",
            "deployment",
            Guid.NewGuid().ToString("D"),
            "high",
            "INCIDENT_CONTAINMENT",
            "contain exact deployment",
            "INC-42",
            Guid.NewGuid(),
            17,
            previewId,
            new string('2', 64),
            null,
            null,
            new DateTimeOffset(2026, 8, 22, 12, 10, 0, TimeSpan.Zero),
            Guid.NewGuid());
    }

    private static AdminCommandRecord CreateCommandRecord(
        string commandDigest,
        string impactDigest,
        long rowVersion,
        DateTimeOffset now) => new(
            Guid.NewGuid(),
            CommandType.StopAfterFlat,
            new string('1', 64),
            commandDigest,
            CanonicalJson.Serialize(ExecutionSafetyPolicyVector.Unrestricted.ToDocument()),
            [CommandType.ReleaseContainment],
            ActorId,
            Guid.NewGuid(),
            "production",
            "deployment",
            Guid.NewGuid().ToString("D"),
            "high",
            "INCIDENT_CONTAINMENT",
            "contain exact deployment",
            "INC-42",
            Guid.NewGuid(),
            17,
            Guid.NewGuid(),
            impactDigest,
            CommandStatus.WaitingApproval,
            null,
            null,
            null,
            now.AddMinutes(5),
            Guid.NewGuid(),
            rowVersion,
            now,
            now);
}
