using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.Persistence.Postgres;
using YO4X.Runtime.Contracts;

namespace YO4X.RuntimeControl.Postgres;

public sealed partial class PostgresRuntimeControlPlaneApplication
{
    public async Task<SignedExecutionLease> IssueLeaseAsync(
        WorkloadActor actor,
        IssueExecutionLease request,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireSupervisor(actor);
        ValidateRequestedActions(request.RequestedActions);
        if (request.DeploymentId != actor.DeploymentId
            || request.WorkerInstanceId != actor.WorkerInstanceId
            || request.Generation != actor.Generation)
        {
            throw WrongRuntimeBinding();
        }

        (IExecutionEntitlementProvider entitlement, IExecutionLeaseSigningProvider signer) = RequireLeaseProviders();
        RuntimeBindingSnapshot snapshot = await ReadLeaseBindingAsync(actor, metadata, cancellationToken)
            .ConfigureAwait(false);
        DateTimeOffset authorizationNow = snapshot.AuthorizationNow;
        ExecutionEntitlementGrant grant = await ResolveEntitlementAsync(
            entitlement,
            snapshot,
            actor,
            authorizationNow,
            cancellationToken).ConfigureAwait(false);
        ExecutionLeaseClaims claims = CreateLeaseClaims(
            Guid.CreateVersion7(),
            actor,
            snapshot,
            grant,
            request.RequestedActions,
            authorizationNow);
        SignedExecutionLease signed = await ExecutionLeaseEnvelopeFactory
            .CreateAsync(claims, signer, cancellationToken)
            .ConfigureAwait(false);

        await using TenantPostgresTransaction transaction = await BeginRuntimeAsync(
                actor,
                metadata,
                cancellationToken,
                requireAuthorityLock: true)
            .ConfigureAwait(false);
        RuntimeBindingSnapshot current = await LoadBindingAsync(transaction, actor, true, cancellationToken)
            .ConfigureAwait(false);
        EnsureSnapshotUnchanged(snapshot, current);
        EnsureLeaseEligible(current, claims.ExpiresAtUtc, current.AuthorizationNow);

        await using (NpgsqlCommand currentLease = transaction.CreateCommand(
            """
            select 1
            from operations.execution_leases
            where tenant_id = @tenant_id
              and deployment_id = @deployment_id
              and state in ('issued', 'active', 'renew_restricted', 'revoking')
            limit 1
            """))
        {
            AddUuid(currentLease, "tenant_id", actor.TenantId);
            AddUuid(currentLease, "deployment_id", actor.DeploymentId);
            if (await currentLease.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
            {
                throw new ResourceConflictException(
                    "EXECUTION_LEASE_ALREADY_CURRENT",
                    "A current execution lease already exists for the deployment.");
            }
        }

        string tokenSha256 = ExecutionLeaseEnvelopeDigest.Sha256(signed);
        await InsertLeaseAsync(transaction, signed, cancellationToken).ConfigureAwait(false);
        await AppendLeaseEvidenceAsync(transaction, signed, tokenSha256, metadata, false, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return signed;
    }

    public async Task<SignedExecutionLease> RenewLeaseAsync(
        WorkloadActor actor,
        RenewExecutionLease request,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireSupervisor(actor);
        ValidateRequestedActions(request.RequestedActions);
        if (request.LeaseId == Guid.Empty || request.Generation != actor.Generation)
        {
            throw WrongRuntimeBinding();
        }

        (IExecutionEntitlementProvider entitlement, IExecutionLeaseSigningProvider signer) = RequireLeaseProviders();
        (RuntimeBindingSnapshot snapshot, PersistedLease persisted) = await ReadRenewalSnapshotAsync(
            actor,
            request.LeaseId,
            metadata,
            cancellationToken).ConfigureAwait(false);
        DateTimeOffset authorizationNow = snapshot.AuthorizationNow;
        if (persisted.ExpiresAt <= authorizationNow
            || persisted.State is not ("issued" or "active" or "renew_restricted"))
        {
            throw new ResourceConflictException("EXECUTION_LEASE_NOT_RENEWABLE", "The execution lease cannot be renewed.");
        }

        ExecutionEntitlementGrant grant = await ResolveEntitlementAsync(
            entitlement,
            snapshot,
            actor,
            authorizationNow,
            cancellationToken).ConfigureAwait(false);
        if (grant.EntitlementId != persisted.EntitlementId)
        {
            throw new AuthorizationDeniedException(
                "EXECUTION_ENTITLEMENT_CHANGED",
                "The execution entitlement no longer matches the current lease.");
        }

        ExecutionLeaseClaims claims = CreateLeaseClaims(
            request.LeaseId,
            actor,
            snapshot,
            grant,
            request.RequestedActions,
            authorizationNow);
        SignedExecutionLease signed = await ExecutionLeaseEnvelopeFactory
            .CreateAsync(claims, signer, cancellationToken)
            .ConfigureAwait(false);

        await using TenantPostgresTransaction transaction = await BeginRuntimeAsync(
                actor,
                metadata,
                cancellationToken,
                requireAuthorityLock: true)
            .ConfigureAwait(false);
        RuntimeBindingSnapshot current = await LoadBindingAsync(transaction, actor, true, cancellationToken)
            .ConfigureAwait(false);
        EnsureSnapshotUnchanged(snapshot, current);
        EnsureLeaseEligible(current, claims.ExpiresAtUtc, current.AuthorizationNow);
        string tokenSha256 = ExecutionLeaseEnvelopeDigest.Sha256(signed);
        await UpdateLeaseAsync(
            transaction,
            signed,
            persisted.RowVersion,
            cancellationToken).ConfigureAwait(false);
        await AppendLeaseEvidenceAsync(transaction, signed, tokenSha256, metadata, true, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return signed;
    }

    private async Task<RuntimeBindingSnapshot> ReadLeaseBindingAsync(
        WorkloadActor actor,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        await using TenantPostgresTransaction transaction = await BeginRuntimeAsync(actor, metadata, cancellationToken)
            .ConfigureAwait(false);
        RuntimeBindingSnapshot binding = await LoadBindingAsync(transaction, actor, false, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return binding;
    }

    private async Task<(RuntimeBindingSnapshot Binding, PersistedLease Lease)> ReadRenewalSnapshotAsync(
        WorkloadActor actor,
        Guid leaseId,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        await using TenantPostgresTransaction transaction = await BeginRuntimeAsync(actor, metadata, cancellationToken)
            .ConfigureAwait(false);
        RuntimeBindingSnapshot binding = await LoadBindingAsync(transaction, actor, false, cancellationToken)
            .ConfigureAwait(false);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select entitlement_id, state, expires_at, row_version,
                   worker_assignment_id, worker_instance_id, generation,
                   supervisor_workload_id, strategy_host_workload_id, gateway_host_workload_id,
                   broker_account_id, strategy_version_id, risk_policy_version_id
            from operations.execution_leases
            where tenant_id = @tenant_id
              and id = @lease_id
              and deployment_id = @deployment_id
            """);
        AddUuid(command, "tenant_id", actor.TenantId);
        AddUuid(command, "lease_id", leaseId);
        AddUuid(command, "deployment_id", actor.DeploymentId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            || reader.GetGuid(4) != binding.AssignmentId
            || reader.GetGuid(5) != actor.WorkerInstanceId
            || reader.GetInt64(6) != actor.Generation
            || reader.GetGuid(7) != binding.SupervisorWorkloadId
            || reader.GetGuid(8) != binding.StrategyHostWorkloadId
            || reader.GetGuid(9) != binding.GatewayHostWorkloadId
            || reader.GetGuid(10) != actor.BrokerAccountId
            || reader.GetGuid(11) != binding.StrategyVersionId
            || reader.GetGuid(12) != binding.RiskPolicyVersionId)
        {
            throw WrongRuntimeBinding();
        }

        var lease = new PersistedLease(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetFieldValue<DateTimeOffset>(2),
            reader.GetInt64(3));
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return (binding, lease);
    }

    private static async ValueTask<ExecutionEntitlementGrant> ResolveEntitlementAsync(
        IExecutionEntitlementProvider provider,
        RuntimeBindingSnapshot binding,
        WorkloadActor actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ExecutionEntitlementGrant? grant = await provider.ResolveAsync(
            new ExecutionEntitlementRequest(
                actor.TenantId,
                binding.UserId,
                actor.DeploymentId,
                actor.BrokerAccountId,
                binding.StrategyId,
                binding.StrategyVersionId,
                binding.StrategyVersion,
                binding.StrategyPackageSha256,
                binding.ExecutionMode,
                now),
            cancellationToken).ConfigureAwait(false);
        if (grant is null)
        {
            throw new AuthorizationDeniedException(
                "EXECUTION_ENTITLEMENT_REQUIRED",
                "No active execution entitlement is available.");
        }

        return grant;
    }

    private ExecutionLeaseClaims CreateLeaseClaims(
        Guid leaseId,
        WorkloadActor actor,
        RuntimeBindingSnapshot binding,
        ExecutionEntitlementGrant grant,
        LeaseActionClass requestedActions,
        DateTimeOffset now)
    {
        DateTimeOffset normalizedNow = now.ToUniversalTime();
        DateTimeOffset notBefore = grant.NotBeforeUtc.ToUniversalTime() > normalizedNow
            ? grant.NotBeforeUtc.ToUniversalTime()
            : normalizedNow;
        DateTimeOffset maximumExpiry = normalizedNow.Add(options.MaximumLeaseLifetime);
        DateTimeOffset expiresAt = grant.ExpiresAtUtc.ToUniversalTime() < maximumExpiry
            ? grant.ExpiresAtUtc.ToUniversalTime()
            : maximumExpiry;
        if (grant.EntitlementId == Guid.Empty
            || grant.NotBeforeUtc.ToUniversalTime() > normalizedNow.Add(options.MaximumFutureClockSkew)
            || expiresAt <= notBefore
            || binding.AssignmentExpiresAt <= expiresAt)
        {
            throw new AuthorizationDeniedException(
                "EXECUTION_ENTITLEMENT_INVALID",
                "The execution entitlement cannot authorize a bounded lease.");
        }

        bool increaseEligible = IsIncreaseEligible(binding, expiresAt, normalizedNow);
        LeaseActionClass active = grant.ActionPolicy.Active & requestedActions;
        if (!increaseEligible)
        {
            active &= ~LeaseActionClass.Increase;
        }

        if ((active & requestedActions) != requestedActions)
        {
            throw new AuthorizationDeniedException(
                "LEASE_ACTION_NOT_AUTHORIZED",
                "One or more requested actions are not authorized by the current binding.");
        }

        var actionPolicy = new ExecutionLeaseActionPolicy(
            active,
            grant.ActionPolicy.Grace & requestedActions & ~LeaseActionClass.Increase,
            grant.ActionPolicy.Expired & requestedActions & ~LeaseActionClass.Increase,
            grant.ActionPolicy.Revoked & requestedActions & ~LeaseActionClass.Increase);
        var leaseBinding = new ExecutionLeaseBinding(
            actor.TenantId,
            grant.EntitlementId,
            binding.UserId,
            actor.DeploymentId,
            actor.BrokerAccountId,
            binding.BrokerBindingSha256,
            binding.StrategyId,
            binding.StrategyVersionId,
            binding.StrategyVersion,
            binding.StrategyPackageSha256,
            binding.ExecutionMode,
            binding.RiskPolicyVersionId,
            binding.RiskPolicySha256,
            binding.AssignmentId,
            actor.WorkerInstanceId,
            binding.SupervisorWorkloadId,
            binding.StrategyHostWorkloadId,
            binding.GatewayHostWorkloadId,
            actor.Generation,
            actor.Region);
        return new ExecutionLeaseClaims(
            RuntimeContractVersions.ExecutionLeaseV1,
            leaseId,
            leaseBinding,
            normalizedNow,
            notBefore,
            expiresAt,
            expiresAt.Add(options.MaximumLeaseGracePeriod),
            actionPolicy);
    }

    private static bool IsIncreaseEligible(
        RuntimeBindingSnapshot binding,
        DateTimeOffset expiresAt,
        DateTimeOffset now) =>
        binding.ExecutionMode == ExecutionMode.CloudDemo
        && string.Equals(binding.AssignmentState, "active", StringComparison.Ordinal)
        && string.Equals(binding.DeploymentDesiredState, "running", StringComparison.Ordinal)
        && string.Equals(binding.DeploymentObservedState, "running", StringComparison.Ordinal)
        && string.Equals(binding.BrokerEnvironment, "demo", StringComparison.Ordinal)
        && binding.DedicatedCloudUse is true
        && binding.ManualOrExternalTradingDetected is false
        && binding.TradingAllowed is true
        && string.Equals(binding.CredentialState, "ready", StringComparison.Ordinal)
        && string.Equals(binding.BrokerState, "active", StringComparison.Ordinal)
        && binding.CapabilityValidUntil is { } capabilityValidUntil
        && capabilityValidUntil > expiresAt
        && binding.AssignmentExpiresAt > expiresAt
        && now < expiresAt;

    private static void EnsureLeaseEligible(
        RuntimeBindingSnapshot binding,
        DateTimeOffset expiresAt,
        DateTimeOffset now)
    {
        if (binding.AssignmentState is "revoked" or "failed" or "unknown"
            || binding.AssignmentExpiresAt <= expiresAt
            || binding.AssignmentExpiresAt <= now
            || binding.DeploymentDesiredState is "fenced" or "expired" or "revoked" or "stopped" or "faulted")
        {
            throw new ResourceConflictException(
                "EXECUTION_LEASE_BINDING_INACTIVE",
                "The current runtime binding cannot receive an execution lease.");
        }
    }

    private static void EnsureSnapshotUnchanged(RuntimeBindingSnapshot expected, RuntimeBindingSnapshot current)
    {
        if (expected with { AuthorizationNow = current.AuthorizationNow } != current)
        {
            throw new ResourceConflictException(
                "EXECUTION_LEASE_BINDING_CHANGED",
                "The execution binding changed while external authorization was evaluated.");
        }
    }

    private (IExecutionEntitlementProvider Entitlement, IExecutionLeaseSigningProvider Signer) RequireLeaseProviders()
    {
        if (entitlementProvider is null)
        {
            throw new BackendCapabilityUnavailableException("execution_entitlement_provider");
        }

        if (signingProvider is null)
        {
            throw new BackendCapabilityUnavailableException("execution_lease_signing_provider");
        }

        return (entitlementProvider, signingProvider);
    }

    private static async Task InsertLeaseAsync(
        TenantPostgresTransaction transaction,
        SignedExecutionLease lease,
        CancellationToken cancellationToken)
    {
        byte[] envelopeContent = Encoding.UTF8.GetBytes(CanonicalJson.Serialize(lease));
        try
        {
            await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select *
            from control.persist_signed_execution_lease(
                @signed_envelope_content, -1)
            """);
            command.Parameters.AddWithValue(
                "signed_envelope_content",
                NpgsqlDbType.Bytea,
                envelopeContent);
            await using NpgsqlDataReader reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                || reader.GetGuid(0) != lease.Claims.LeaseId
                || reader.GetInt64(1) != 0
                || reader.GetBoolean(3)
                || await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new ResourceConflictException(
                    "EXECUTION_LEASE_EXPIRED_DURING_ISSUANCE",
                    "The signed execution lease was not durably issued exactly once.");
            }
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ResourceConflictException(
                "EXECUTION_LEASE_CONFLICT",
                "A current execution lease already exists or the signed envelope was replayed.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(envelopeContent);
        }
    }

    private static async Task UpdateLeaseAsync(
        TenantPostgresTransaction transaction,
        SignedExecutionLease lease,
        long expectedRowVersion,
        CancellationToken cancellationToken)
    {
        byte[] envelopeContent = Encoding.UTF8.GetBytes(CanonicalJson.Serialize(lease));
        try
        {
            await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select *
            from control.persist_signed_execution_lease(
                @signed_envelope_content, @expected_row_version)
            """);
            command.Parameters.AddWithValue(
                "signed_envelope_content",
                NpgsqlDbType.Bytea,
                envelopeContent);
            command.Parameters.AddWithValue("expected_row_version", NpgsqlDbType.Bigint, expectedRowVersion);
            await using NpgsqlDataReader reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                || reader.GetGuid(0) != lease.Claims.LeaseId
                || reader.GetInt64(1) != expectedRowVersion + 1
                || !reader.GetBoolean(3)
                || await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new ResourceConflictException(
                    "EXECUTION_LEASE_RENEWAL_CONFLICT",
                    "The execution lease changed or expired while renewal was evaluated.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(envelopeContent);
        }
    }

    private static async Task AppendLeaseEvidenceAsync(
        TenantPostgresTransaction transaction,
        SignedExecutionLease lease,
        string tokenSha256,
        RequestMetadata metadata,
        bool renewed,
        CancellationToken cancellationToken) =>
        await AppendEvidenceAsync(
            transaction,
            renewed ? "runtime.execution_lease_renewed" : "runtime.execution_lease_issued",
            "execution_lease",
            lease.Claims.LeaseId,
            metadata,
            lease.Claims.LeaseId,
            new
            {
                leaseId = lease.Claims.LeaseId,
                entitlementId = lease.Claims.Binding.EntitlementId,
                deploymentId = lease.Claims.Binding.DeploymentId,
                workerInstanceId = lease.Claims.Binding.WorkerInstanceId,
                generation = lease.Claims.Binding.Generation,
                payloadSha256 = lease.PayloadSha256,
                leaseTokenSha256 = tokenSha256,
                signingKeyId = lease.SigningKeyId,
                expiresAt = lease.Claims.ExpiresAtUtc,
                renewed
            },
            cancellationToken).ConfigureAwait(false);

    private sealed record PersistedLease(
        Guid EntitlementId,
        string State,
        DateTimeOffset ExpiresAt,
        long RowVersion);
}
