using System.Globalization;
using YO4X.ControlPlane.Workers.Operations;

namespace YO4X.Worker.Tests;

public sealed class ControlWorkContractTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid DeploymentId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid AssignmentId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkerId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly string Digest = new('a', 64);

    [Fact]
    public void DispatchGuardRejectsStaleVersionAndSupersededPermissiveState()
    {
        Assert.True(UserOperationDispatchGuard.IsCurrent(
            "deployment.start", "running", 8, 8, "starting"));
        Assert.False(UserOperationDispatchGuard.IsCurrent(
            "deployment.start", "running", 8, 9, "starting"));
        Assert.False(UserOperationDispatchGuard.IsCurrent(
            "deployment.start", "running", 8, 8, "close_only"));
        Assert.False(UserOperationDispatchGuard.IsCurrent(
            "broker_account.connection_test", "active:ready", 3, 3, "disabled:disabled"));
        Assert.True(UserOperationDispatchGuard.IsCurrent(
            "broker_account.delete", "disabled:deleted", 4, 4, "disabled:deletion_pending"));
    }

    [Fact]
    public void ReconciliationGuardUsesDesiredPrecursorInsteadOfMissingFinalObservation()
    {
        Assert.True(UserOperationDispatchGuard.IsReconciliationBindingCurrent(
            "deployment.start", "running", 8, 8, "starting",
            2, 2, AssignmentId, WorkerId, DeploymentId, DeploymentId,
            AssignmentId, WorkerId, Digest, Digest));
        Assert.True(UserOperationDispatchGuard.IsReconciliationBindingCurrent(
            "deployment.close_only", "close_only", 9, 10, "close_only",
            2, 2, AssignmentId, WorkerId, DeploymentId, DeploymentId,
            AssignmentId, WorkerId, Digest, Digest));
        Assert.True(UserOperationDispatchGuard.IsReconciliationBindingCurrent(
            "deployment.stop_after_flat", "stopped", 10, 10, "stop_after_flat",
            2, 2, AssignmentId, WorkerId, DeploymentId, DeploymentId,
            AssignmentId, WorkerId, Digest, Digest));
        Assert.True(UserOperationDispatchGuard.IsReconciliationBindingCurrent(
            "broker_account.delete", "disabled:deleted", 4, 4, "disabled:deletion_pending",
            2, 2, AssignmentId, WorkerId, DeploymentId, DeploymentId,
            AssignmentId, WorkerId, Digest, Digest));
        Assert.True(UserOperationDispatchGuard.IsReconciliationBindingCurrent(
            "broker_account.delete", "disabled:deleted", 4, 5, "disabled:deleted",
            2, 2, AssignmentId, WorkerId, DeploymentId, DeploymentId,
            AssignmentId, WorkerId, Digest, Digest));
        Assert.True(UserOperationDispatchGuard.IsReconciliationBindingCurrent(
            "broker_account.credential_rotation", "active:ready", 5, 6, "active:ready",
            2, 2, AssignmentId, WorkerId, DeploymentId, DeploymentId,
            AssignmentId, WorkerId, Digest, Digest));

        Assert.False(UserOperationDispatchGuard.IsReconciliationBindingCurrent(
            "deployment.start", "running", 8, 8, "close_only",
            2, 2, AssignmentId, WorkerId, DeploymentId, DeploymentId,
            AssignmentId, WorkerId, Digest, Digest));
        Assert.False(UserOperationDispatchGuard.IsReconciliationBindingCurrent(
            "deployment.start", "running", 8, 8, "starting",
            2, 3, AssignmentId, WorkerId, DeploymentId, DeploymentId,
            AssignmentId, WorkerId, Digest, Digest));
        Assert.False(UserOperationDispatchGuard.IsReconciliationBindingCurrent(
            "deployment.start", "running", 8, 8, "starting",
            2, 2, AssignmentId, WorkerId, DeploymentId, DeploymentId,
            AssignmentId, WorkerId, Digest, new string('b', 64)));
    }

    [Fact]
    public void InvalidPolicyOnlyBlocksAuthorityIncreasingOperations()
    {
        Assert.True(UserOperationDispatchGuard.InvalidPolicyBlocksDispatch(
            "deployment.start", integrityValid: false));
        Assert.True(UserOperationDispatchGuard.InvalidPolicyBlocksDispatch(
            "broker_account.connection_test", integrityValid: false));
        Assert.True(UserOperationDispatchGuard.InvalidPolicyBlocksDispatch(
            "broker_account.credential_rotation", integrityValid: false));

        Assert.False(UserOperationDispatchGuard.InvalidPolicyBlocksDispatch(
            "deployment.close_only", integrityValid: false));
        Assert.False(UserOperationDispatchGuard.InvalidPolicyBlocksDispatch(
            "deployment.stop_after_flat", integrityValid: false));
        Assert.False(UserOperationDispatchGuard.InvalidPolicyBlocksDispatch(
            "broker_account.disable", integrityValid: false));
        Assert.False(UserOperationDispatchGuard.InvalidPolicyBlocksDispatch(
            "broker_account.delete", integrityValid: false));
    }

    [Fact]
    public void TypedEnvelopeCarriesExactVersionResultFenceAssignmentAndPolicyBindings()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-22T12:00:00Z", CultureInfo.InvariantCulture);
        UserOperationDispatchEnvelope envelope = UserOperationDispatchEnvelope.Create(
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            TenantId,
            "deployment.start",
            "deployment",
            DeploymentId,
            7,
            8,
            "running",
            Guid.Parse("60000000-0000-0000-0000-000000000001"),
            Guid.Parse("70000000-0000-0000-0000-000000000001"),
            8,
            "starting:unknown",
            DeploymentId,
            2,
            AssignmentId,
            WorkerId,
            new { DeploymentId, Generation = 2 },
            Digest,
            Digest,
            Digest,
            Digest,
            Digest,
            now.AddSeconds(-1),
            now);

        Assert.Equal(8, envelope.SubmittedResourceVersion);
        Assert.Equal("running", envelope.RequestedTargetState);
        Assert.Equal(2, envelope.TargetBinding.FenceGeneration);
        Assert.Equal(DeploymentId, envelope.TargetBinding.RouteDeploymentId);
        Assert.Equal(AssignmentId, envelope.TargetBinding.WorkerAssignmentId);
        Assert.Equal(WorkerId, envelope.TargetBinding.WorkerInstanceId);
        Assert.Equal(Digest, envelope.DispatchPolicySnapshotSha256);
        Assert.NotNull(envelope.PolicyEvidence);
    }

    [Fact]
    public void StartDispatchCannotFreezeAnUnassignedGeneration()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-22T12:00:00Z", CultureInfo.InvariantCulture);
        Assert.Throws<ArgumentException>(() => UserOperationDispatchEnvelope.Create(
            Guid.Parse("50000000-0000-0000-0000-000000000002"),
            TenantId,
            "deployment.start",
            "deployment",
            DeploymentId,
            7,
            8,
            "running",
            Guid.Parse("60000000-0000-0000-0000-000000000002"),
            Guid.Parse("70000000-0000-0000-0000-000000000002"),
            8,
            "starting:unknown",
            null,
            2,
            null,
            null,
            new { DeploymentId, Generation = 2 },
            Digest,
            Digest,
            Digest,
            Digest,
            Digest,
            now.AddSeconds(-1),
            now));

        Assert.False(UserOperationDispatchGuard.HasCompleteRoute(
            null, 2, null, null));
        Assert.False(UserOperationDispatchGuard.RouteWaitExpired(
            now.AddMinutes(-1), now, TimeSpan.FromMinutes(10)));
        Assert.True(UserOperationDispatchGuard.RouteWaitExpired(
            now.AddMinutes(-10), now, TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void BrokerConfirmedStoppedTruthSurvivesRevokedLeaseProjection()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-22T12:00:00Z", CultureInfo.InvariantCulture);
        var deployment = Deployment("stop_after_flat", "stopping");
        var assignment = Assignment(now, "revoked");
        var lease = Lease(now, "revoked");
        var reconciliation = Reconciliation(now, "stopped", brokerConfirmed: true, positionState: "flat");

        PostgresDeploymentProjectionStore.ProjectionDecision? decision =
            PostgresDeploymentProjectionStore.Decide(
                deployment,
                assignment,
                lease,
                reconciliation,
                PostgresDeploymentProjectionStore.ComponentEvidence.None,
                now);

        Assert.Equal("stopped", decision?.ObservedState);
        Assert.Equal("broker_confirmed_terminal_reconciliation", decision?.EvidenceCode);
    }

    [Fact]
    public void BrokerConfirmedCloseOnlyTruthSurvivesExpiredLeaseProjection()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-22T12:00:00Z", CultureInfo.InvariantCulture);
        var reconciliation = Reconciliation(now, "close_only", brokerConfirmed: true, positionState: "open");

        PostgresDeploymentProjectionStore.ProjectionDecision? decision =
            PostgresDeploymentProjectionStore.Decide(
                Deployment("close_only", "running"),
                Assignment(now, "active"),
                Lease(now.AddMinutes(-1), "expired"),
                reconciliation,
                PostgresDeploymentProjectionStore.ComponentEvidence.None,
                now);

        Assert.Equal("close_only", decision?.ObservedState);
        Assert.Equal("broker_confirmed_restrictive_reconciliation", decision?.EvidenceCode);
    }

    [Fact]
    public void StaleHeartbeatEvidenceMakesRunningDeploymentUnreachable()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-22T12:00:00Z", CultureInfo.InvariantCulture);
        var staleComponents = new PostgresDeploymentProjectionStore.ComponentEvidence(
            3,
            2,
            2,
            0,
            true,
            now.AddMinutes(-1));

        PostgresDeploymentProjectionStore.ProjectionDecision? decision =
            PostgresDeploymentProjectionStore.Decide(
                Deployment("running", "running"),
                Assignment(now, "active"),
                Lease(now, "active"),
                Reconciliation(now, "running", brokerConfirmed: true, positionState: "open"),
                staleComponents,
                now);

        Assert.Equal("unreachable", decision?.ObservedState);
        Assert.Equal("persisted_runtime_evidence_stale_or_unbound", decision?.EvidenceCode);
    }

    [Fact]
    public void WrongGenerationReconciliationCannotProjectRunning()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-22T12:00:00Z", CultureInfo.InvariantCulture);
        var wrongGeneration = Reconciliation(
            now,
            "running",
            brokerConfirmed: true,
            positionState: "open") with { FenceGeneration = 1 };
        var fresh = new PostgresDeploymentProjectionStore.ComponentEvidence(3, 3, 3, 0, true, now);

        PostgresDeploymentProjectionStore.ProjectionDecision? decision =
            PostgresDeploymentProjectionStore.Decide(
                Deployment("running", "unknown"),
                Assignment(now, "active"),
                Lease(now, "active"),
                wrongGeneration,
                fresh,
                now);

        Assert.NotEqual("running", decision?.ObservedState);
    }

    [Fact]
    public void OptionsRejectUnboundedHeartbeatWindows()
    {
        var options = new ControlWorkOptions
        {
            ComponentHeartbeatMaximumAge = TimeSpan.FromMinutes(6)
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void MigrationGuardsTerminalAndDispatchBindings()
    {
        string migration = File.ReadAllText(FindRepositoryFile(
            "src", "BuildingBlocks", "YO4X.Persistence.Postgres", "Migrations", "001_foundation.sql"));

        Assert.Contains("create trigger user_operations_transition_guard", migration, StringComparison.Ordinal);
        Assert.Contains("A terminal user operation is immutable.", migration, StringComparison.Ordinal);
        Assert.Contains("The user operation state transition is not allowed.", migration, StringComparison.Ordinal);
        Assert.Contains("The user operation dispatch binding is write-once.", migration, StringComparison.Ordinal);
        Assert.Contains("The user operation reconciliation binding is write-once.", migration, StringComparison.Ordinal);
        Assert.Contains("old.state = 'accepted' and new.state = 'dispatching'", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("old.state = 'accepted' and new.state = 'succeeded'", migration, StringComparison.Ordinal);
        Assert.Contains("create trigger outbox_messages_transition_guard", migration, StringComparison.Ordinal);
        Assert.Contains("The outbox message binding is immutable.", migration, StringComparison.Ordinal);
    }

    private static PostgresDeploymentProjectionStore.DeploymentSnapshot Deployment(
        string desired,
        string observed) => new(
            DeploymentId,
            TenantId,
            desired,
            observed,
            2,
            null,
            null,
            8);

    private static PostgresDeploymentProjectionStore.AssignmentEvidence Assignment(
        DateTimeOffset now,
        string state) => new(AssignmentId, WorkerId, 2, state, now.AddMinutes(5));

    private static PostgresDeploymentProjectionStore.LeaseEvidence Lease(
        DateTimeOffset now,
        string state) => new(
            Guid.Parse("80000000-0000-0000-0000-000000000001"),
            state,
            state == "expired" ? now : now.AddMinutes(5),
            AssignmentId,
            WorkerId,
            2);

    private static PostgresDeploymentProjectionStore.ReconciliationEvidence Reconciliation(
        DateTimeOffset now,
        string state,
        bool brokerConfirmed,
        string positionState) => new(
            Guid.Parse("90000000-0000-0000-0000-000000000001"),
            "reconciled",
            Digest,
            Digest,
            Digest,
            state,
            Digest,
            2,
            AssignmentId,
            WorkerId,
            brokerConfirmed,
            state,
            positionState,
            now);

    private static string FindRepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("The repository file was not found.");
    }
}
