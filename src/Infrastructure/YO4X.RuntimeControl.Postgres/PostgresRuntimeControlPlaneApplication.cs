using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using YO4X.Audit;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.Outbox;
using YO4X.Persistence.Postgres;
using YO4X.Runtime.Contracts;
using YO4X.Tenancy;

namespace YO4X.RuntimeControl.Postgres;

public sealed partial class PostgresRuntimeControlPlaneApplication : IRuntimeControlPlaneApplication
{
    private const LeaseActionClass AllLeaseActions =
        LeaseActionClass.Increase
        | LeaseActionClass.Reduce
        | LeaseActionClass.Protect
        | LeaseActionClass.Cancel
        | LeaseActionClass.EmergencyClose;

    private readonly RuntimePostgresDatabase database;
    private readonly RuntimeControlPostgresOptions options;
    private readonly IExecutionEntitlementProvider? entitlementProvider;
    private readonly IExecutionLeaseSigningProvider? signingProvider;
    private readonly RuntimeEvidencePostgresDatabase? evidenceDatabase;

    public PostgresRuntimeControlPlaneApplication(
        RuntimePostgresDatabase database,
        RuntimeControlPostgresOptions options,
        IClock clock,
        IExecutionEntitlementProvider? entitlementProvider = null,
        IExecutionLeaseSigningProvider? signingProvider = null,
        RuntimeEvidencePostgresDatabase? evidenceDatabase = null)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentNullException.ThrowIfNull(clock);
        this.entitlementProvider = entitlementProvider;
        this.signingProvider = signingProvider;
        this.evidenceDatabase = evidenceDatabase;
        options.Validate();
    }

    private async ValueTask<TenantPostgresTransaction> BeginRuntimeAsync(
        WorkloadActor actor,
        RequestMetadata metadata,
        CancellationToken cancellationToken,
        bool requireAuthorityLock = false)
    {
        ValidateActor(actor);
        ValidateMetadata(metadata);
        TenantPostgresTransaction transaction = await database.BeginTenantTransactionAsync(
                new TenantExecutionContext(actor.TenantId, actor.WorkloadId, metadata.CorrelationId, null),
                cancellationToken)
            .ConfigureAwait(false);
        if (!requireAuthorityLock)
        {
            return transaction;
        }

        try
        {
            await using NpgsqlCommand command = transaction.CreateCommand(
                "select control.acquire_u0_authority_lock()");
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<RuntimeBindingSnapshot> LoadBindingAsync(
        TenantPostgresTransaction transaction,
        WorkloadActor actor,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        string lockClause = forUpdate
            ? "for update of assignment, deployment for share of strategy, source_binding"
            : string.Empty;
        await using NpgsqlCommand command = transaction.CreateCommand(
            $$"""
            select
                assignment.id,
                assignment.worker_node_id,
                assignment.supervisor_identity,
                assignment.strategy_host_identity,
                assignment.gateway_host_identity,
                assignment.state,
                assignment.lease_expires_at,
                assignment.row_version,
                deployment.user_id,
                deployment.broker_account_id,
                deployment.strategy_version_id,
                strategy.strategy_id,
                strategy.version_number,
                deployment.strategy_package_digest,
                deployment.deployment_mode,
                deployment.risk_policy_version_id,
                deployment.risk_policy_digest,
                deployment.region,
                deployment.desired_state,
                deployment.observed_state,
                deployment.fence_generation,
                deployment.row_version,
                account.binding_fingerprint,
                account.environment,
                account.dedicated_cloud_use,
                account.manual_or_external_trading_detected,
                account.trading_allowed,
                account.credential_state,
                account.state,
                account.row_version,
                account.capability_valid_until,
                clock_timestamp() as authorization_now
            from operations.worker_assignments as assignment
            join operations.deployments as deployment
              on deployment.tenant_id = assignment.tenant_id
             and deployment.id = assignment.deployment_id
            join operations.broker_accounts as account
              on account.tenant_id = deployment.tenant_id
             and account.id = deployment.broker_account_id
            join governance.strategy_versions as strategy
              on strategy.tenant_id = deployment.tenant_id
             and strategy.id = deployment.strategy_version_id
             and strategy.package_sha256 = deployment.strategy_package_digest
            join governance.strategy_version_source_bindings as source_binding
              on source_binding.tenant_id = deployment.tenant_id
             and source_binding.id = deployment.strategy_source_binding_id
             and source_binding.strategy_version_id = deployment.strategy_version_id
             and source_binding.strategy_package_sha256 = deployment.strategy_package_digest
             and source_binding.verification_evidence_sha256 =
                deployment.strategy_verification_evidence_sha256
             and source_binding.verification_signature_sha256 =
                deployment.strategy_verification_signature_sha256
             and source_binding.verification_signing_key_id =
                deployment.strategy_verification_signing_key_id
            where assignment.tenant_id = @tenant_id
              and assignment.deployment_id = @deployment_id
              and assignment.worker_node_id = @worker_id
              and assignment.fence_generation = @generation
              and strategy.state in ('demo_approved', 'published')
            {{lockClause}}
            """);
        AddUuid(command, "tenant_id", actor.TenantId);
        AddUuid(command, "deployment_id", actor.DeploymentId);
        AddUuid(command, "worker_id", actor.WorkerInstanceId);
        command.Parameters.AddWithValue("generation", NpgsqlDbType.Bigint, actor.Generation);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw WrongRuntimeBinding();
        }

        var snapshot = new RuntimeBindingSnapshot(
            reader.GetGuid(0),
            reader.GetGuid(1),
            ParseStoredWorkloadId(reader.GetString(2)),
            ParseStoredWorkloadId(reader.GetString(3)),
            ParseStoredWorkloadId(reader.GetString(4)),
            reader.GetString(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetInt64(7),
            reader.GetGuid(8),
            reader.GetGuid(9),
            reader.GetGuid(10),
            reader.GetGuid(11),
            reader.GetInt32(12),
            reader.GetString(13),
            ParseExecutionMode(reader.GetString(14)),
            reader.GetGuid(15),
            reader.GetString(16),
            reader.GetString(17),
            reader.GetString(18),
            reader.GetString(19),
            reader.GetInt64(20),
            reader.GetInt64(21),
            reader.GetString(22),
            reader.GetString(23),
            reader.IsDBNull(24) ? null : reader.GetBoolean(24),
            reader.IsDBNull(25) ? null : reader.GetBoolean(25),
            reader.IsDBNull(26) ? null : reader.GetBoolean(26),
            reader.GetString(27),
            reader.GetString(28),
            reader.GetInt64(29),
            reader.IsDBNull(30) ? null : reader.GetFieldValue<DateTimeOffset>(30),
            reader.GetFieldValue<DateTimeOffset>(31));

        ValidateBinding(actor, snapshot);
        return snapshot;
    }

    private static void ValidateBinding(WorkloadActor actor, RuntimeBindingSnapshot binding)
    {
        Guid expectedWorkload = actor.Component switch
        {
            "supervisor" => binding.SupervisorWorkloadId,
            "strategy_host" => binding.StrategyHostWorkloadId,
            "gateway_host" => binding.GatewayHostWorkloadId,
            _ => Guid.Empty
        };

        if (binding.WorkerInstanceId != actor.WorkerInstanceId
            || binding.BrokerAccountId != actor.BrokerAccountId
            || binding.Generation != actor.Generation
            || !string.Equals(binding.Region, actor.Region, StringComparison.Ordinal)
            || expectedWorkload != actor.WorkloadId)
        {
            throw WrongRuntimeBinding();
        }
    }

    private static async Task AppendEvidenceAsync<TPayload>(
        TenantPostgresTransaction transaction,
        string action,
        string targetType,
        Guid targetId,
        RequestMetadata metadata,
        Guid causationId,
        TPayload payload,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand authorityTime = transaction.CreateCommand("select clock_timestamp()");
        object? occurredAtValue = await authorityTime.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);
        if (occurredAtValue is not DateTimeOffset occurredAt)
        {
            throw new InvalidOperationException("PostgreSQL did not return an evidence timestamp.");
        }
        AuditEvent audit = AuditEvent.Create(
            transaction.Context.TenantId,
            transaction.Context.ActorId,
            AuditCategory.Operations,
            action,
            targetType,
            targetId.ToString("D"),
            AuditOutcome.Accepted,
            metadata.Reason,
            transaction.Context.CorrelationId,
            causationId,
            payload,
            occurredAt);
        OutboxMessage outbox = OutboxMessage.Create(
            transaction.Context.TenantId,
            action,
            targetType,
            targetId.ToString("D"),
            payload,
            transaction.Context.CorrelationId,
            causationId,
            occurredAt);
        await PostgresAuditOutboxWriter.AppendAsync(transaction, audit, outbox, cancellationToken)
            .ConfigureAwait(false);
    }

    private static void ValidateActor(WorkloadActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.TenantId == Guid.Empty
            || actor.WorkloadId == Guid.Empty
            || actor.WorkerInstanceId == Guid.Empty
            || actor.DeploymentId == Guid.Empty
            || actor.BrokerAccountId == Guid.Empty
            || actor.Generation <= 0
            || string.IsNullOrWhiteSpace(actor.Region)
            || actor.Region.Length > 100
            || actor.Component is not ("supervisor" or "strategy_host" or "gateway_host"))
        {
            throw new UnauthorizedAccessException("The workload identity binding is invalid.");
        }
    }

    private static void ValidateMetadata(RequestMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (metadata.CorrelationId == Guid.Empty
            || string.IsNullOrWhiteSpace(metadata.IdempotencyKey)
            || metadata.IdempotencyKey.Length > 200
            || metadata.Reason?.Length > 2000)
        {
            throw new ArgumentException("The request metadata is invalid.", nameof(metadata));
        }
    }

    private static void RequireSupervisor(WorkloadActor actor)
    {
        if (!string.Equals(actor.Component, "supervisor", StringComparison.Ordinal))
        {
            throw new AuthorizationDeniedException(
                "SUPERVISOR_WORKLOAD_REQUIRED",
                "The operation requires the assigned supervisor workload.");
        }
    }

    private static void ValidateEventEnvelope(
        long generation,
        long sequence,
        int schemaVersion,
        Guid eventId,
        DateTimeOffset observedAt,
        DateTimeOffset now,
        RuntimeControlPostgresOptions options)
    {
        if (generation <= 0 || sequence <= 0 || schemaVersion <= 0 || eventId == Guid.Empty)
        {
            throw new DomainException("RUNTIME_EVENT_INVALID", "The runtime event envelope is invalid.");
        }

        DateTimeOffset normalized = observedAt.ToUniversalTime();
        if (normalized < now - options.MaximumEvidenceAge
            || normalized > now + options.MaximumFutureClockSkew)
        {
            throw new DomainException("RUNTIME_EVENT_TIME_INVALID", "The runtime event timestamp is outside the accepted window.");
        }
    }

    private static void ValidateRequestedActions(LeaseActionClass actions)
    {
        if (actions == LeaseActionClass.None || (actions & ~AllLeaseActions) != LeaseActionClass.None)
        {
            throw new DomainException("LEASE_ACTIONS_INVALID", "The requested lease actions are invalid.");
        }
    }

    private static Guid ParseStoredWorkloadId(string value) =>
        Guid.TryParseExact(value, "D", out Guid parsed) && parsed != Guid.Empty
            ? parsed
            : throw new InvalidOperationException("The persisted workload identity is invalid.");

    private static ExecutionMode ParseExecutionMode(string value) => value switch
    {
        "cloud_demo" => ExecutionMode.CloudDemo,
        "cloud_live" => ExecutionMode.CloudLive,
        "local" => ExecutionMode.Local,
        _ => throw new InvalidOperationException("The persisted execution mode is invalid.")
    };

    private static string ComponentToStorage(RuntimeComponentRole component) => component switch
    {
        RuntimeComponentRole.Supervisor => "supervisor",
        RuntimeComponentRole.StrategyHost => "strategy_host",
        RuntimeComponentRole.GatewayHost => "gateway_host",
        _ => throw new ArgumentOutOfRangeException(nameof(component))
    };

    private static string StateToStorage(RuntimeComponentState state) => state switch
    {
        RuntimeComponentState.Starting => "starting",
        RuntimeComponentState.Ready => "ready",
        RuntimeComponentState.Degraded => "degraded",
        RuntimeComponentState.Faulted => "faulted",
        RuntimeComponentState.Fenced => "fenced",
        RuntimeComponentState.Stopped => "stopped",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    private static string FenceToStorage(FenceEvidenceState state) => state switch
    {
        FenceEvidenceState.Unverified => "unverified",
        FenceEvidenceState.Valid => "valid",
        FenceEvidenceState.Invalid => "invalid",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    private static string ExecutionModeToStorage(ExecutionMode mode) => mode switch
    {
        ExecutionMode.CloudDemo => "cloud_demo",
        ExecutionMode.CloudLive => "cloud_live",
        ExecutionMode.Local => "local",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    private static string Sha256Utf8(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        byte[] leftBytes = Encoding.ASCII.GetBytes(left);
        byte[] rightBytes = Encoding.ASCII.GetBytes(right);
        try
        {
            return leftBytes.Length == rightBytes.Length
                && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(leftBytes);
            CryptographicOperations.ZeroMemory(rightBytes);
        }
    }

    private static void AddUuid(NpgsqlCommand command, string name, Guid value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Uuid, value);

    private static ResourceConflictException WrongRuntimeBinding() => new(
        "RUNTIME_BINDING_MISMATCH",
        "The workload is not bound to the current assignment generation.");

    private sealed record RuntimeBindingSnapshot(
        Guid AssignmentId,
        Guid WorkerInstanceId,
        Guid SupervisorWorkloadId,
        Guid StrategyHostWorkloadId,
        Guid GatewayHostWorkloadId,
        string AssignmentState,
        DateTimeOffset AssignmentExpiresAt,
        long AssignmentVersion,
        Guid UserId,
        Guid BrokerAccountId,
        Guid StrategyVersionId,
        Guid StrategyId,
        int StrategyVersion,
        string StrategyPackageSha256,
        ExecutionMode ExecutionMode,
        Guid RiskPolicyVersionId,
        string RiskPolicySha256,
        string Region,
        string DeploymentDesiredState,
        string DeploymentObservedState,
        long Generation,
        long DeploymentVersion,
        string BrokerBindingSha256,
        string BrokerEnvironment,
        bool? DedicatedCloudUse,
        bool? ManualOrExternalTradingDetected,
        bool? TradingAllowed,
        string CredentialState,
        string BrokerState,
        long BrokerAccountVersion,
        DateTimeOffset? CapabilityValidUntil,
        DateTimeOffset AuthorizationNow);
}
