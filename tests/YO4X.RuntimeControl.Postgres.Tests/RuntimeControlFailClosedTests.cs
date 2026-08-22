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
            "Host=127.0.0.1;Port=1;Database=unreachable;Username=yo4x_worker;Password=test");
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
            "Host=127.0.0.1;Port=1;Database=unreachable;Username=yo4x_worker;Password=test");
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
            "Host=127.0.0.1;Port=1;Database=unreachable;Username=yo4x_worker;Password=test");
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
    public void AcceptsOnlyExactFreshBrokerResultEnvelope()
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
        Assert.Throws<DomainException>(() =>
            PostgresRuntimeControlPlaneApplication.ValidateBrokerResultEnvelope(
                Actor(),
                Actor().BrokerAccountId,
                BrokerResult(now.AddHours(-1)),
                now,
                Options()));
        Assert.Throws<DomainException>(() =>
            PostgresRuntimeControlPlaneApplication.ValidateBrokerResultEnvelope(
                Actor(),
                Guid.Parse("50000000-0000-0000-0000-000000000099"),
                BrokerResult(now),
                now,
                Options()));
    }

    [Fact]
    public void FailedBrokerResultRequiresBoundErrorCode()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-22T12:00:00Z", CultureInfo.InvariantCulture);
        BrokerUserOperationResultInput invalid = BrokerResult(now) with
        {
            Outcome = "failed",
            BrokerConfirmed = false,
            ErrorCode = null
        };

        Assert.Throws<DomainException>(() =>
            PostgresRuntimeControlPlaneApplication.ValidateBrokerResultEnvelope(
                Actor(), Actor().BrokerAccountId, invalid, now, Options()));
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
        Assert.Contains("apply_confirmed_broker_operation_result", migration, StringComparison.Ordinal);
        Assert.Contains("result.dispatch_message_id = operation.dispatch_message_id", migration, StringComparison.Ordinal);
        Assert.Contains("result.worker_assignment_id = operation.dispatch_worker_assignment_id", migration, StringComparison.Ordinal);
        Assert.Contains("unique (tenant_id, operation_id, dispatch_message_id)", migration, StringComparison.Ordinal);
        Assert.Contains("outcome in ('succeeded', 'failed')", migration, StringComparison.Ordinal);
        Assert.Contains("if account_record.credential_state = 'deleted'", migration, StringComparison.Ordinal);
        Assert.Contains("if account_record.credential_state = 'ready'", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerCannotForgeBrokerResultsAndIngressRoleCannotProjectThem()
    {
        string roles = File.ReadAllText(FindRepositoryFile(
            "src", "BuildingBlocks", "YO4X.Persistence.Postgres", "Security", "least_privilege_roles.sql"));
        string normalized = string.Join(' ', roles.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        string workerSection = normalized[normalized.IndexOf("-- Worker:", StringComparison.Ordinal)..];

        Assert.Contains(
            "grant select, insert on operations.user_operation_results to yo4x_runtime_evidence;",
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
    }

    private static WorkloadActor Actor() => new(
        Guid.Parse("10000000-0000-0000-0000-000000000001"),
        Guid.Parse("20000000-0000-0000-0000-000000000001"),
        Guid.Parse("30000000-0000-0000-0000-000000000001"),
        Guid.Parse("40000000-0000-0000-0000-000000000001"),
        Guid.Parse("50000000-0000-0000-0000-000000000001"),
        1,
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
        1,
        Guid.Parse("70000000-0000-0000-0000-000000000001"),
        Guid.Parse("80000000-0000-0000-0000-000000000001"),
        Guid.Parse("90000000-0000-0000-0000-000000000001"),
        4,
        "disabled:deleted",
        new string('b', 64),
        "succeeded",
        true,
        "disabled",
        "deleted",
        new string('c', 64),
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
