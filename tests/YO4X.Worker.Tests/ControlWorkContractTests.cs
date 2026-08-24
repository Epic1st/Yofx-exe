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
            ResultCapability(),
            now.AddSeconds(-1),
            now,
            now.AddHours(24),
            now.AddMinutes(10),
            now.AddMinutes(2));

        Assert.Equal(8, envelope.SubmittedResourceVersion);
        Assert.Equal("running", envelope.RequestedTargetState);
        Assert.Equal(2, envelope.TargetBinding.FenceGeneration);
        Assert.Equal(DeploymentId, envelope.TargetBinding.RouteDeploymentId);
        Assert.Equal(AssignmentId, envelope.TargetBinding.WorkerAssignmentId);
        Assert.Equal(WorkerId, envelope.TargetBinding.WorkerInstanceId);
        Assert.Equal(Digest, envelope.DispatchPolicySnapshotSha256);
        Assert.Equal(3, envelope.SchemaVersion);
        Assert.Equal("yo4x.deployment.start.requested.v3", envelope.MessageType);
        Assert.Equal(ResultCapability(), envelope.ResultCapability);
        Assert.Equal(now.AddHours(24), envelope.ResultCapabilityExpiresAt);
        Assert.Equal(now.AddMinutes(10), envelope.AssignmentLeaseExpiresAt);
        Assert.Equal(now.AddMinutes(2), envelope.ExecutionDeadline);
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
            ResultCapability(),
            now.AddSeconds(-1),
            now,
            now.AddHours(24),
            now.AddMinutes(10),
            now.AddMinutes(2)));

        Assert.False(UserOperationDispatchGuard.HasCompleteRoute(
            null, 2, null, null));
        Assert.False(UserOperationDispatchGuard.ShouldExpireBeforeDispatch(
            "deployment.start", now.AddMinutes(-1), now, TimeSpan.FromMinutes(10)));
        Assert.True(UserOperationDispatchGuard.ShouldExpireBeforeDispatch(
            "deployment.start", now.AddMinutes(-10), now, TimeSpan.FromMinutes(10)));
        Assert.False(UserOperationDispatchGuard.ShouldExpireBeforeDispatch(
            "deployment.stop_after_flat", now.AddDays(-1), now, TimeSpan.FromMinutes(10)));
        Assert.False(UserOperationDispatchGuard.ShouldExpireBeforeDispatch(
            "broker_account.delete", now.AddDays(-1), now, TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void TypedEnvelopeRejectsExecutionPastTheFrozenAssignmentLease()
    {
        DateTimeOffset now = DateTimeOffset.Parse(
            "2026-08-22T12:00:00Z",
            CultureInfo.InvariantCulture);

        Assert.Throws<ArgumentException>(() => UserOperationDispatchEnvelope.Create(
            Guid.Parse("50000000-0000-0000-0000-000000000003"),
            TenantId,
            "deployment.start",
            "deployment",
            DeploymentId,
            7,
            8,
            "running",
            Guid.Parse("60000000-0000-0000-0000-000000000003"),
            Guid.Parse("70000000-0000-0000-0000-000000000003"),
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
            ResultCapability(),
            now.AddSeconds(-1),
            now,
            now.AddHours(24),
            now.AddMinutes(1),
            now.AddMinutes(1).AddTicks(1)));
    }

    [Fact]
    public void DurableHandoffNeverBecomesTerminalMerelyBecauseProofIsLate()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-22T12:00:00Z", CultureInfo.InvariantCulture);

        Assert.Equal("propagating", UserOperationDispatchGuard.AwaitingProofState(
            "propagating", now.AddMinutes(-1), now, TimeSpan.FromMinutes(2), published: false));
        Assert.Equal("reconciling", UserOperationDispatchGuard.AwaitingProofState(
            "propagating", now.AddMinutes(-1), now, TimeSpan.FromMinutes(2), published: true));
        Assert.Equal("unknown", UserOperationDispatchGuard.AwaitingProofState(
            "propagating", now.AddDays(-1), now, TimeSpan.FromMinutes(2), published: false));
        Assert.Equal("unknown", UserOperationDispatchGuard.AwaitingProofState(
            "reconciling", now.AddDays(-1), now, TimeSpan.FromMinutes(2), published: true));
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
    public void ProvenNotSentOperationFailureDoesNotProjectDeploymentFaultOrReconciliationTime()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-22T12:00:00Z", CultureInfo.InvariantCulture);
        var notSent = Reconciliation(
            now,
            "running",
            brokerConfirmed: false,
            positionState: "open") with
        {
            State = "failed",
            ObservedDigest = null,
            BrokerDigest = null,
            ObservedState = null,
            RuntimeEvidenceSha256 = Digest,
            BrokerExecutionState = null,
            BrokerPositionState = null,
            PreInvocationNotSentProven = true
        };
        var fresh = new PostgresDeploymentProjectionStore.ComponentEvidence(
            3,
            3,
            3,
            0,
            true,
            now);

        Assert.Null(PostgresDeploymentProjectionStore.ProjectableReconciliation(notSent));
        PostgresDeploymentProjectionStore.ProjectionDecision? decision =
            PostgresDeploymentProjectionStore.Decide(
                Deployment("running", "running"),
                Assignment(now, "active"),
                Lease(now, "active"),
                notSent,
                fresh,
                now);

        Assert.Equal("running", decision?.ObservedState);
        Assert.Equal(
            "persisted_fresh_runtime_without_reconciliation",
            decision?.EvidenceCode);
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
    public void OptionsRejectBacklogSlaShorterThanOperationExpiry()
    {
        var options = new ControlWorkOptions
        {
            OperationExpiresAfter = TimeSpan.FromMinutes(15),
            MaximumOperationBacklogAge = TimeSpan.FromMinutes(14)
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void OptionsRejectObservationWindowLongerThanBacklogSla()
    {
        var options = new ControlWorkOptions
        {
            MaximumTenantScanRotationAge = TimeSpan.FromMinutes(16),
            MaximumOperationBacklogAge = TimeSpan.FromMinutes(15)
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void ResultCapabilityLifetimeMatchesTheDatabaseHardLimit()
    {
        new ControlWorkOptions
        {
            ResultCapabilityLifetime = TimeSpan.FromHours(24)
        }.Validate();

        var beyondDatabaseLimit = new ControlWorkOptions
        {
            ResultCapabilityLifetime = TimeSpan.FromHours(24) + TimeSpan.FromTicks(1)
        };
        Assert.Throws<InvalidOperationException>(beyondDatabaseLimit.Validate);
    }

    [Theory]
    [InlineData(14_999)]
    [InlineData(300_001)]
    public void DispatchExecutionWindowIsStrictlyBounded(int milliseconds)
    {
        var options = new ControlWorkOptions
        {
            DispatchExecutionWindow = TimeSpan.FromMilliseconds(milliseconds)
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData(15_000)]
    [InlineData(300_000)]
    public void DispatchExecutionWindowAcceptsExactSafetyBoundaries(int milliseconds)
    {
        new ControlWorkOptions
        {
            DispatchExecutionWindow = TimeSpan.FromMilliseconds(milliseconds)
        }.Validate();
    }

    [Theory]
    [InlineData(999)]
    [InlineData(60_001)]
    public void AssignmentProofMarginIsStrictlyBounded(int milliseconds)
    {
        var options = new ControlWorkOptions
        {
            AssignmentProofMargin = TimeSpan.FromMilliseconds(milliseconds)
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(512)]
    public void InvocationTimeoutBatchAcceptsBoundedValues(int batchSize)
    {
        new ControlWorkOptions
        {
            InvocationTimeoutBatchSizePerTenant = batchSize
        }.Validate();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(513)]
    [InlineData(1_000)]
    public void InvocationTimeoutBatchRejectsUnboundedValues(int batchSize)
    {
        var options = new ControlWorkOptions
        {
            InvocationTimeoutBatchSizePerTenant = batchSize
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void ActiveWorkerUsesInvocationV4AndKeepsLegacyDispatchDormant()
    {
        string source = File.ReadAllText(FindRepositoryFile(
            "src", "Apps", "YO4X.ControlPlane.Workers", "Operations",
            "PostgresUserOperationWorkStore.cs"));
        int activeDispatch = source.IndexOf(
            "private async Task<bool> DispatchAsync(",
            StringComparison.Ordinal);
        int legacyDispatch = source.IndexOf(
            "private async Task<bool> DispatchLegacyV3Async(",
            StringComparison.Ordinal);

        Assert.True(activeDispatch >= 0);
        Assert.True(legacyDispatch > activeDispatch);
        Assert.Contains(
            "control.create_user_operation_invocation_attempt(",
            source[activeDispatch..legacyDispatch],
            StringComparison.Ordinal);
        Assert.Contains(
            "control.advance_user_operation_invocation_timeouts(@max_rows)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "control.reconcile_user_operation_invocation_attempt(",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "control.issue_user_operation_invocation_reconciliation_challenge_v3(",
            source,
            StringComparison.Ordinal);
        Assert.Equal(
            1,
            source.Split(
                "DispatchLegacyV3Async(",
                StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void ProjectionFiltersDispatchNonObservationsBeforeLatestRowSelection()
    {
        string source = File.ReadAllText(FindRepositoryFile(
            "src", "Apps", "YO4X.ControlPlane.Workers", "Operations",
            "PostgresDeploymentProjectionStore.cs"));
        int filter = source.IndexOf(
            "reconciliation.pre_invocation_not_sent_proven = false",
            StringComparison.Ordinal);
        int ordering = source.IndexOf(
            "order by reconciliation.completed_at desc",
            StringComparison.Ordinal);

        Assert.True(filter >= 0);
        Assert.True(ordering > filter);
        Assert.Contains(
            "consumption.challenge_id = reconciliation.reconciliation_challenge_id",
            source,
            StringComparison.Ordinal);
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
        Assert.Contains("dispatch_execution_deadline <= dispatch_assignment_lease_expires_at", migration, StringComparison.Ordinal);
        Assert.Contains("dispatch_execution_deadline <= result_capability_expires_at", migration, StringComparison.Ordinal);
        Assert.Contains(".requested.v3", migration, StringComparison.Ordinal);
        Assert.Contains(
            "authority_now >= bound_operation.result_capability_expires_at",
            migration,
            StringComparison.Ordinal);
        Assert.Contains(
            "p_observed_at >= bound_operation.dispatch_execution_deadline",
            migration,
            StringComparison.Ordinal);
        Assert.Contains(
            "p_observed_at >= matched_challenge.expires_at",
            migration,
            StringComparison.Ordinal);
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
            now,
            false);

    private static string ResultCapability() => $"{new string('R', 42)}A";

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
