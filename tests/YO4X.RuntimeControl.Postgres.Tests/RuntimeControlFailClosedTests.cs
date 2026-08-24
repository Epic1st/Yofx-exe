using System.Globalization;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.Runtime.Contracts;
using YO4X.RuntimeControl.Postgres;

namespace YO4X.RuntimeControl.Postgres.Tests;

public sealed class RuntimeControlFailClosedTests
{
    [Fact]
    public async Task MissingEntitlementProviderFailsLeaseBeforeOpeningDatabase()
    {
        await using var database = new RuntimePostgresDatabase(
            "Host=127.0.0.1;Port=1;Database=unreachable;Username=yo4x_worker;Password=test;SSL Mode=Disable",
            allowInsecureLoopbackForDevelopment: true);
        var application = new PostgresRuntimeControlPlaneApplication(
            database,
            Options(),
            SystemClock.Instance);

        BackendCapabilityUnavailableException exception = await Assert.ThrowsAsync<BackendCapabilityUnavailableException>(
            () => application.IssueLeaseAsync(
                Actor(),
                new IssueExecutionLease(
                    Actor().DeploymentId,
                    Actor().WorkerInstanceId,
                    Actor().Generation,
                    LeaseActionClass.Reduce),
                Metadata(),
                CancellationToken.None));

        Assert.Equal("execution_entitlement_provider", exception.Capability);
    }

    [Fact]
    public async Task MissingSignerFailsLeaseBeforeOpeningDatabase()
    {
        await using var database = new RuntimePostgresDatabase(
            "Host=127.0.0.1;Port=1;Database=unreachable;Username=yo4x_worker;Password=test;SSL Mode=Disable",
            allowInsecureLoopbackForDevelopment: true);
        var application = new PostgresRuntimeControlPlaneApplication(
            database,
            Options(),
            SystemClock.Instance,
            new NeverCalledEntitlementProvider());

        BackendCapabilityUnavailableException exception = await Assert.ThrowsAsync<BackendCapabilityUnavailableException>(
            () => application.IssueLeaseAsync(
                Actor(),
                new IssueExecutionLease(
                    Actor().DeploymentId,
                    Actor().WorkerInstanceId,
                    Actor().Generation,
                    LeaseActionClass.Reduce),
                Metadata(),
                CancellationToken.None));

        Assert.Equal("execution_lease_signing_provider", exception.Capability);
    }

    [Fact]
    public async Task MissingEvidenceWriterFailsBrokerResultBeforeOpeningWorkerDatabase()
    {
        await using var database = new RuntimePostgresDatabase(
            "Host=127.0.0.1;Port=1;Database=unreachable;Username=yo4x_worker;Password=test;SSL Mode=Disable",
            allowInsecureLoopbackForDevelopment: true);
        var application = new PostgresRuntimeControlPlaneApplication(
            database,
            Options(),
            SystemClock.Instance);

        BackendCapabilityUnavailableException exception = await Assert.ThrowsAsync<BackendCapabilityUnavailableException>(
            () => application.RecordBrokerUserOperationResultAsync(
                Actor(),
                Actor().BrokerAccountId,
                BrokerResult(DateTimeOffset.UtcNow),
                Metadata(),
                CancellationToken.None));

        Assert.Equal("runtime_broker_evidence_postgres", exception.Capability);
    }

    [Fact]
    public async Task MissingEvidenceWriterFailsDeploymentResultBeforeOpeningWorkerDatabase()
    {
        await using var database = new RuntimePostgresDatabase(
            "Host=127.0.0.1;Port=1;Database=unreachable;Username=yo4x_worker;Password=test;SSL Mode=Disable",
            allowInsecureLoopbackForDevelopment: true);
        var application = new PostgresRuntimeControlPlaneApplication(
            database,
            Options(),
            SystemClock.Instance);

        BackendCapabilityUnavailableException exception = await Assert.ThrowsAsync<BackendCapabilityUnavailableException>(
            () => application.RecordDeploymentUserOperationResultAsync(
                Actor(),
                Actor().DeploymentId,
                DeploymentResult(DateTimeOffset.UtcNow),
                Metadata(),
                CancellationToken.None));

        Assert.Equal("runtime_deployment_evidence_postgres", exception.Capability);
    }

    [Fact]
    public void RejectsUnsafeOperationalLimits()
    {
        var options = new RuntimeControlPostgresOptions
        {
            ApprovedRuntimeImageDigest = $"sha256:{new string('a', 64)}",
            MaximumEventPayloadBytes = 1024 * 1024 + 1
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void AcceptsOnlyExactCapabilityBoundBrokerResultEnvelope()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-22T12:00:00Z", CultureInfo.InvariantCulture);
        PostgresRuntimeControlPlaneApplication.ValidateBrokerResultEnvelope(
            Actor(),
            Actor().BrokerAccountId,
            BrokerResult(now),
            now,
            Options());

        Assert.Throws<DomainException>(() =>
            PostgresRuntimeControlPlaneApplication.ValidateBrokerResultEnvelope(
                Actor(),
                Actor().BrokerAccountId,
                BrokerResult(now) with { CredentialState = "deletion_pending" },
                now,
                Options()));
        PostgresRuntimeControlPlaneApplication.ValidateBrokerResultEnvelope(
            Actor(),
            Actor().BrokerAccountId,
            BrokerResult(now.AddHours(-1)),
            now,
            Options());
        Assert.Throws<DomainException>(() =>
            PostgresRuntimeControlPlaneApplication.ValidateBrokerResultEnvelope(
                Actor(),
                Guid.Parse("50000000-0000-0000-0000-000000000099"),
                BrokerResult(now),
                now,
                Options()));
        Assert.Throws<DomainException>(() =>
            PostgresRuntimeControlPlaneApplication.ValidateBrokerResultEnvelope(
                Actor(),
                Actor().BrokerAccountId,
                BrokerResult(now) with { DispatchTargetBindingSha256 = "unbound" },
                now,
                Options()));
        Assert.Throws<DomainException>(() =>
            PostgresRuntimeControlPlaneApplication.ValidateBrokerResultEnvelope(
                Actor(),
                Actor().BrokerAccountId,
                BrokerResult(now) with { ResultCapability = new string('R', 43) },
                now,
                Options()));
    }

    [Fact]
    public void ResultCapabilitiesUseCanonicalUnpaddedBase64UrlForExactly256Bits()
    {
        const string alphabet =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        const string canonicalFinalCharacters = "AEIMQUYcgkosw048";
        string prefix = new('R', 42);

        foreach (char suffix in alphabet)
        {
            Assert.Equal(
                canonicalFinalCharacters.Contains(suffix),
                CanonicalBase64Url.IsEncodedByteCount($"{prefix}{suffix}", 32));
        }
    }

    [Fact]
    public void BrokerResultIngressRejectsCallerAssertedNonInvocationFailure()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-22T12:00:00Z", CultureInfo.InvariantCulture);
        BrokerUserOperationResultInput invalid = BrokerResult(now) with
        {
            Outcome = "failed",
            PreInvocationNotSentProven = true,
            GatewayInvoked = false,
            BrokerConfirmed = false,
            AccountState = null,
            CredentialState = null,
            ErrorCode = null
        };

        Assert.Throws<DomainException>(() =>
            PostgresRuntimeControlPlaneApplication.ValidateBrokerResultEnvelope(
                Actor(), Actor().BrokerAccountId, invalid, now, Options()));

        BrokerUserOperationResultInput callerAssertedNotSent = invalid with
        {
            ErrorCode = "pre_invocation_not_sent"
        };
        Assert.Throws<DomainException>(() =>
            PostgresRuntimeControlPlaneApplication.ValidateBrokerResultEnvelope(
                Actor(),
                Actor().BrokerAccountId,
                callerAssertedNotSent,
                now,
                Options()));
    }

    [Fact]
    public void AcceptsOnlyConclusiveCapabilityBoundDeploymentResultEnvelope()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-22T12:00:00Z", CultureInfo.InvariantCulture);
        PostgresRuntimeControlPlaneApplication.ValidateDeploymentResultEnvelope(
            Actor(),
            Actor().DeploymentId,
            DeploymentResult(now),
            now,
            Options());

        Assert.Throws<DomainException>(() =>
            PostgresRuntimeControlPlaneApplication.ValidateDeploymentResultEnvelope(
                Actor(),
                Actor().DeploymentId,
                DeploymentResult(now) with { SchemaVersion = 3 },
                now,
                Options()));
        Assert.Throws<DomainException>(() =>
            PostgresRuntimeControlPlaneApplication.ValidateDeploymentResultEnvelope(
                Actor(),
                Guid.Parse("40000000-0000-0000-0000-000000000099"),
                DeploymentResult(now),
                now,
                Options()));
        Assert.Throws<DomainException>(() =>
            PostgresRuntimeControlPlaneApplication.ValidateDeploymentResultEnvelope(
                Actor(),
                Actor().DeploymentId,
                DeploymentResult(now) with { ResultCapability = new string('R', 43) },
                now,
                Options()));
        Assert.Throws<DomainException>(() =>
            PostgresRuntimeControlPlaneApplication.ValidateDeploymentResultEnvelope(
                Actor(),
                Actor().DeploymentId,
                DeploymentResult(now) with { ObservedDigest = new string('e', 64) },
                now,
                Options()));
        Assert.Throws<DomainException>(() =>
            PostgresRuntimeControlPlaneApplication.ValidateDeploymentResultEnvelope(
                Actor(),
                Actor().DeploymentId,
                DeploymentResult(now) with
                {
                    RequestedTargetState = "stopped",
                    ObservedState = "stopped",
                    BrokerExecutionState = "stopped",
                    BrokerPositionState = "open"
                },
                now,
                Options()));
    }

    [Fact]
    public void DeploymentIngressDistinguishesDivergenceFromAmbiguousFailure()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-22T12:00:00Z", CultureInfo.InvariantCulture);
        DeploymentUserOperationResultInput divergence = DeploymentResult(now) with
        {
            Outcome = "diverged",
            ObservedState = "faulted",
            BrokerExecutionState = "unknown",
            ErrorCode = "runtime-state-diverged"
        };
        PostgresRuntimeControlPlaneApplication.ValidateDeploymentResultEnvelope(
            Actor(), Actor().DeploymentId, divergence, now, Options());

        Assert.Throws<DomainException>(() =>
            PostgresRuntimeControlPlaneApplication.ValidateDeploymentResultEnvelope(
                Actor(),
                Actor().DeploymentId,
                DeploymentResult(now) with
                {
                    Outcome = "diverged",
                    ErrorCode = "no-observed-divergence"
                },
                now,
                Options()));
        Assert.Throws<DomainException>(() =>
            PostgresRuntimeControlPlaneApplication.ValidateDeploymentResultEnvelope(
                Actor(),
                Actor().DeploymentId,
                DeploymentResult(now) with
                {
                    Outcome = "failed",
                    PreInvocationNotSentProven = true,
                    GatewayInvoked = false,
                    BrokerConfirmed = false,
                    BrokerDigest = null,
                    BrokerExecutionState = null,
                    BrokerPositionState = null,
                    ErrorCode = null
                },
                now,
                Options()));

        Assert.Throws<DomainException>(() =>
            PostgresRuntimeControlPlaneApplication.ValidateDeploymentResultEnvelope(
                Actor(),
                Actor().DeploymentId,
                DeploymentResult(now) with
                {
                    Outcome = "failed",
                    ObservedState = null,
                    ObservedDigest = null,
                    PreInvocationNotSentProven = true,
                    GatewayInvoked = false,
                    BrokerConfirmed = false,
                    BrokerDigest = null,
                    BrokerExecutionState = null,
                    BrokerPositionState = null,
                    ErrorCode = "command-rejected"
                },
                now,
                Options()));

        Assert.Throws<DomainException>(() =>
            PostgresRuntimeControlPlaneApplication.ValidateDeploymentResultEnvelope(
                Actor(),
                Actor().DeploymentId,
                DeploymentResult(now) with
                {
                    Outcome = "failed",
                    ObservedState = null,
                    ObservedDigest = null,
                    PreInvocationNotSentProven = false,
                    GatewayInvoked = true,
                    BrokerConfirmed = false,
                    BrokerDigest = null,
                    BrokerExecutionState = null,
                    BrokerPositionState = null,
                    ErrorCode = "ambiguous-after-invocation"
                },
                now,
                Options()));
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("unknown")]
    public void BrokerIngressRejectsAmbiguousNonTerminalResults(string outcome)
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-22T12:00:00Z", CultureInfo.InvariantCulture);
        BrokerUserOperationResultInput invalid = BrokerResult(now) with
        {
            Outcome = outcome,
            BrokerConfirmed = false,
            ErrorCode = "not-terminal"
        };

        Assert.Throws<DomainException>(() =>
            PostgresRuntimeControlPlaneApplication.ValidateBrokerResultEnvelope(
                Actor(), Actor().BrokerAccountId, invalid, now, Options()));
    }

    [Fact]
    public void MigrationProvidesDedicatedImmutableBrokerResultPath()
    {
        string migration = File.ReadAllText(FindRepositoryFile(
            "src", "BuildingBlocks", "YO4X.Persistence.Postgres", "Migrations", "001_foundation.sql"));

        Assert.Contains("create table operations.user_operation_results", migration, StringComparison.Ordinal);
        Assert.Contains("user_operation_results_immutable", migration, StringComparison.Ordinal);
        Assert.Contains("create function control.record_broker_user_operation_result", migration, StringComparison.Ordinal);
        string recorder = migration[
            migration.IndexOf(
                "create function control.record_broker_user_operation_result",
                StringComparison.Ordinal)..
            migration.IndexOf(
                "create function control.apply_confirmed_broker_operation_result",
                StringComparison.Ordinal)];
        Assert.Contains("p_raw_result_capability", migration, StringComparison.Ordinal);
        Assert.Contains("apply_confirmed_broker_operation_result", migration, StringComparison.Ordinal);
        Assert.Contains("result.dispatch_message_id = operation.dispatch_message_id", migration, StringComparison.Ordinal);
        Assert.Contains("result.worker_assignment_id = operation.dispatch_worker_assignment_id", migration, StringComparison.Ordinal);
        Assert.Contains("unique (tenant_id, operation_id, dispatch_message_id)", migration, StringComparison.Ordinal);
        Assert.Contains(
            "outcome in ('succeeded', 'diverged', 'failed')",
            migration,
            StringComparison.Ordinal);
        Assert.Contains("state_observed_diverged", migration, StringComparison.Ordinal);
        Assert.Contains(
            "or p_outcome not in ('succeeded', 'diverged')",
            recorder,
            StringComparison.Ordinal);
        Assert.Contains("or p_pre_invocation_not_sent_proven", recorder, StringComparison.Ordinal);
        Assert.Contains("or not p_gateway_invoked", recorder, StringComparison.Ordinal);
        Assert.DoesNotContain("p_outcome = 'failed'", recorder, StringComparison.Ordinal);
        Assert.Contains("if account_record.credential_state = 'deleted'", migration, StringComparison.Ordinal);
        Assert.Contains("if account_record.credential_state = 'ready'", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void MigrationProvidesExecuteOnlyImmutableDeploymentResultPath()
    {
        string migration = File.ReadAllText(FindRepositoryFile(
            "src", "BuildingBlocks", "YO4X.Persistence.Postgres", "Migrations", "001_foundation.sql"));

        Assert.Contains(
            "create function control.record_deployment_user_operation_result",
            migration,
            StringComparison.Ordinal);
        string recorder = migration[
            migration.IndexOf(
                "create function control.record_deployment_user_operation_result",
                StringComparison.Ordinal)..
            migration.IndexOf(
                "-- Dedicated, immutable broker-operation proof.",
                StringComparison.Ordinal)];
        Assert.Contains("p_raw_result_capability", migration, StringComparison.Ordinal);
        Assert.Contains("result_capability_sha256 text", migration, StringComparison.Ordinal);
        Assert.Contains("request_sha256 text", migration, StringComparison.Ordinal);
        Assert.Contains("pre_invocation_not_sent_proven boolean", migration, StringComparison.Ordinal);
        Assert.Contains("gateway_invoked boolean", migration, StringComparison.Ordinal);
        Assert.Contains(
            "or p_outcome not in ('succeeded', 'diverged')",
            recorder,
            StringComparison.Ordinal);
        Assert.Contains("or p_pre_invocation_not_sent_proven", recorder, StringComparison.Ordinal);
        Assert.Contains("or not p_gateway_invoked", recorder, StringComparison.Ordinal);
        Assert.DoesNotContain("p_outcome = 'failed'", recorder, StringComparison.Ordinal);
        Assert.Contains(
            "existing_result.pre_invocation_not_sent_proven",
            recorder,
            StringComparison.Ordinal);
        Assert.Contains("existing_result.gateway_invoked", recorder, StringComparison.Ordinal);
        Assert.DoesNotContain("for update of reconciliation", recorder, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unique (tenant_id, result_id)", migration, StringComparison.Ordinal);
        Assert.Contains(
            "deployment_reconciliations_result_capability_fk",
            migration,
            StringComparison.Ordinal);
        Assert.Contains(
            "desired_digest = dispatch_target_binding_sha256",
            migration,
            StringComparison.Ordinal);
        Assert.Contains(
            "state in ('reconciled', 'diverged', 'failed')",
            migration,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "state in ('reconciled', 'diverged', 'unknown', 'failed')",
            migration,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerCannotForgeResultsAndIngressRoleOwnsOnlyResultV5()
    {
        string roles = File.ReadAllText(FindRepositoryFile(
            "src", "BuildingBlocks", "YO4X.Persistence.Postgres", "Security", "least_privilege_roles.sql"));
        string normalized = string.Join(' ', roles.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        string workerSection = normalized[normalized.IndexOf("-- Worker:", StringComparison.Ordinal)..];

        Assert.Contains(
            "grant execute on function control.record_user_operation_result_v5( uuid, uuid, uuid, uuid, uuid, uuid, uuid, uuid, text, uuid, uuid, uuid, text, text, uuid, jsonb, bigint, text, text, text, text, text, timestamptz, text, uuid, uuid, uuid, bigint, text) to yo4x_runtime_evidence;",
            normalized,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "grant execute on function control.record_broker_user_operation_result(",
            normalized,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "grant execute on function control.record_deployment_user_operation_result(",
            normalized,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "grant insert on operations.deployment_reconciliations to yo4x_runtime_evidence",
            normalized,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "grant insert on operations.user_operation_results to yo4x_runtime_evidence",
            normalized,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "grant execute on function control.apply_confirmed_broker_operation_result(uuid, uuid, uuid) to yo4x_runtime_evidence",
            normalized,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "grant insert on operations.worker_assignments, operations.execution_leases, operations.runtime_event_cursors, operations.runtime_event_inbox, operations.deployment_reconciliations, operations.user_operation_results",
            workerSection,
            StringComparison.Ordinal);
        Assert.Contains(
            "revoke insert, update, delete on operations.deployment_reconciliations from yo4x_worker;",
            workerSection,
            StringComparison.Ordinal);
        string workerInsertSection = workerSection[
            workerSection.IndexOf("grant insert on operations.worker_assignments", StringComparison.Ordinal)..
            workerSection.IndexOf("grant update (state, lease_expires_at", StringComparison.Ordinal)];
        Assert.DoesNotContain(
            "operations.deployment_reconciliations",
            workerInsertSection,
            StringComparison.Ordinal);

        string workerInfrastructure = File.ReadAllText(FindRepositoryFile(
            "src", "Apps", "YO4X.ControlPlane.Workers", "Operations",
            "PostgresWorkerInfrastructure.cs"));
        Assert.Contains(
            "not has_any_column_privilege(current_user, 'operations.deployment_reconciliations', 'UPDATE')",
            workerInfrastructure,
            StringComparison.Ordinal);
        Assert.Contains(
            "not has_table_privilege(current_user, 'operations.deployment_reconciliations', 'DELETE')",
            workerInfrastructure,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RecorderSqlStateMappingIsSanitizedForDeploymentAndBrokerIngress()
    {
        string mapping = File.ReadAllText(FindRepositoryFile(
            "src", "Infrastructure", "YO4X.RuntimeControl.Postgres",
            "UserOperationResultPostgresErrors.cs"));

        Assert.Contains("PostgresErrorCodes.InvalidParameterValue", mapping, StringComparison.Ordinal);
        Assert.Contains("PostgresErrorCodes.UniqueViolation", mapping, StringComparison.Ordinal);
        Assert.Contains("PostgresErrorCodes.InsufficientPrivilege", mapping, StringComparison.Ordinal);
        Assert.Contains("DEPLOYMENT_OPERATION_RESULT_INVALID", mapping, StringComparison.Ordinal);
        Assert.Contains("DEPLOYMENT_OPERATION_RESULT_CONFLICT", mapping, StringComparison.Ordinal);
        Assert.Contains(
            "DEPLOYMENT_OPERATION_RESULT_CAPABILITY_REJECTED",
            mapping,
            StringComparison.Ordinal);
        Assert.DoesNotContain("exception.Message", mapping, StringComparison.Ordinal);
        Assert.DoesNotContain("ResultCapability", mapping, StringComparison.Ordinal);
    }

    private static WorkloadActor Actor() => new(
        Guid.Parse("10000000-0000-0000-0000-000000000001"),
        Guid.Parse("20000000-0000-0000-0000-000000000001"),
        Guid.Parse("30000000-0000-0000-0000-000000000001"),
        Guid.Parse("40000000-0000-0000-0000-000000000001"),
        Guid.Parse("50000000-0000-0000-0000-000000000001"),
        3,
        "region-1",
        "supervisor");

    private static RequestMetadata Metadata() => new(
        "runtime-test-request",
        Guid.Parse("60000000-0000-0000-0000-000000000001"),
        null);

    private static RuntimeControlPostgresOptions Options() => new()
    {
        ApprovedRuntimeImageDigest = $"sha256:{new string('a', 64)}"
    };

    private static BrokerUserOperationResultInput BrokerResult(DateTimeOffset observedAt) => new(
        4,
        Guid.Parse("70000000-0000-0000-0000-000000000001"),
        Guid.Parse("80000000-0000-0000-0000-000000000001"),
        Guid.Parse("90000000-0000-0000-0000-000000000001"),
        4,
        "disabled:deleted",
        new string('b', 64),
        new string('d', 64),
        $"{new string('R', 42)}A",
        "succeeded",
        false,
        true,
        true,
        "disabled",
        "deleted",
        new string('c', 64),
        null,
        observedAt);

    private static DeploymentUserOperationResultInput DeploymentResult(DateTimeOffset observedAt) => new(
        4,
        Guid.Parse("71000000-0000-0000-0000-000000000001"),
        Guid.Parse("81000000-0000-0000-0000-000000000001"),
        Guid.Parse("91000000-0000-0000-0000-000000000001"),
        4,
        "running",
        new string('b', 64),
        new string('d', 64),
        $"{new string('R', 42)}A",
        "succeeded",
        false,
        true,
        "running",
        new string('d', 64),
        new string('c', 64),
        true,
        new string('e', 64),
        "running",
        "open",
        null,
        observedAt);

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

    private sealed class NeverCalledEntitlementProvider : IExecutionEntitlementProvider
    {
        public ValueTask<ExecutionEntitlementGrant?> ResolveAsync(
            ExecutionEntitlementRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The database should not be opened without a signing provider.");
    }
}
