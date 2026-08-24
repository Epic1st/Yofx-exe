using Npgsql;
using YO4X.BuildingBlocks;
using YO4X.Persistence.Postgres;
using YO4X.Tenancy;

namespace YO4X.RuntimeControl.Postgres;

/// <summary>
/// Owns the capability-scoped database pool used only by authenticated
/// user-operation result ingress. The general runtime/worker identity never
/// receives raw proof insertion rights.
/// </summary>
public sealed class RuntimeEvidencePostgresDatabase : IAsyncDisposable
{
    private const string AssertCapabilitiesSql =
        """
        with forbidden_relation(schema_name, relation_name) as
        (
            values
                ('operations', 'user_operation_results'),
                ('operations', 'deployment_reconciliations'),
                ('control', 'user_operation_reconciliation_challenges'),
                ('control', 'user_operation_reconciliation_challenge_consumptions'),
                ('operations', 'user_operation_invocation_attempts'),
                ('operations', 'user_operation_invocation_receipts'),
                ('operations', 'user_operation_invocation_results'),
                ('operations', 'user_operation_invocation_challenges'),
                ('operations', 'user_operation_invocation_challenge_consumptions'),
                ('control', 'user_operations'),
                ('operations', 'worker_assignments'),
                ('operations', 'deployments'),
                ('operations', 'broker_accounts'),
                ('audit', 'audit_events'),
                ('messaging', 'outbox_messages')
        ),
        forbidden_relation_oid as
        (
            select relation.oid
            from forbidden_relation as expected
            join pg_catalog.pg_namespace as namespace
              on namespace.nspname = expected.schema_name
            join pg_catalog.pg_class as relation
              on relation.relnamespace = namespace.oid
             and relation.relname = expected.relation_name
        )
        select current_user = 'yo4x_runtime_evidence'
           and not has_function_privilege(
               current_user, 'control.acquire_u0_authority_lock()', 'EXECUTE')
           and has_function_privilege(
               current_user,
               'control.record_user_operation_result_v5(uuid,uuid,uuid,uuid,uuid,uuid,uuid,uuid,text,uuid,uuid,uuid,text,text,uuid,jsonb,bigint,text,text,text,text,text,timestamp with time zone,text,uuid,uuid,uuid,bigint,text)',
               'EXECUTE')
           and not has_function_privilege(
               current_user,
               'control.record_broker_user_operation_result(uuid,uuid,uuid,uuid,text,uuid,bigint,text,text,text,text,boolean,boolean,boolean,text,text,text,text,text,timestamp with time zone)',
               'EXECUTE')
           and not has_function_privilege(
               current_user,
               'control.record_deployment_user_operation_result(uuid,uuid,uuid,uuid,text,uuid,bigint,text,text,text,text,boolean,boolean,text,text,text,boolean,text,text,text,text,text,timestamp with time zone)',
               'EXECUTE')
           and (select count(*) from forbidden_relation_oid) = 15
           and not exists
           (
               select 1
               from forbidden_relation_oid as forbidden
               where has_table_privilege(current_user, forbidden.oid, 'SELECT')
                  or has_table_privilege(current_user, forbidden.oid, 'INSERT')
                  or has_table_privilege(current_user, forbidden.oid, 'UPDATE')
                  or has_table_privilege(current_user, forbidden.oid, 'DELETE')
                  or has_table_privilege(current_user, forbidden.oid, 'TRUNCATE')
                  or has_table_privilege(current_user, forbidden.oid, 'REFERENCES')
                  or has_table_privilege(current_user, forbidden.oid, 'TRIGGER')
                  or has_any_column_privilege(current_user, forbidden.oid, 'SELECT')
                  or has_any_column_privilege(current_user, forbidden.oid, 'INSERT')
                  or has_any_column_privilege(current_user, forbidden.oid, 'UPDATE')
                  or has_any_column_privilege(current_user, forbidden.oid, 'REFERENCES')
           )
        """;

    private readonly PostgresDatabase database;

    public RuntimeEvidencePostgresDatabase(
        string connectionString,
        ITenantContextCapabilityProvider? tenantContextCapabilityProvider = null,
        bool allowInsecureLoopbackForDevelopment = false)
    {
        database = new PostgresDatabase(
            UserOperationRoleConnectionString.Require(
                connectionString,
                "yo4x_runtime_evidence",
                allowInsecureLoopbackForDevelopment),
            PostgresDatabaseUsage.Runtime,
            tenantContextCapabilityProvider,
            allowInsecureLoopbackForDevelopment);
    }

    public bool HasTenantContextCapabilityProvider =>
        database.HasTenantContextCapabilityProvider;

    public bool UsesTenantContextCapabilityProvider(
        ITenantContextCapabilityProvider provider) =>
        database.UsesTenantContextCapabilityProvider(provider);

    public async ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        if (!HasTenantContextCapabilityProvider)
        {
            return false;
        }

        if (!await database.IsTenantContextCapabilityProviderReadyAsync(cancellationToken)
                .ConfigureAwait(false))
        {
            return false;
        }

        return await IsDatabaseIdentityReadyAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Probes only the runtime-evidence login and catalog contract. Callers
    /// using this narrower probe must have already verified the exact shared
    /// tenant-context capability provider for the same readiness request.
    /// </summary>
    public async ValueTask<bool> IsDatabaseIdentityReadyAsync(
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await database
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await PostgresRoleCapabilityFingerprint.IsSatisfiedAsync(
                connection,
                transaction: null,
                Yo4xPostgresRoleContracts.RuntimeEvidence,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return false;
        }

        await using var command = new NpgsqlCommand(AssertCapabilitiesSql, connection);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true;
    }

    public async ValueTask<TenantPostgresTransaction> BeginTenantTransactionAsync(
        TenantExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        TenantPostgresTransaction transaction = await database
            .BeginTenantTransactionAsync(context, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            if (!await PostgresRoleCapabilityFingerprint.IsSatisfiedAsync(
                    transaction,
                    Yo4xPostgresRoleContracts.RuntimeEvidence,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                throw new BackendCapabilityUnavailableException(
                    "runtime_broker_evidence_postgres");
            }

            await using NpgsqlCommand command = transaction.CreateCommand(AssertCapabilitiesSql);
            object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (result is not true)
            {
                throw new BackendCapabilityUnavailableException("runtime_broker_evidence_postgres");
            }

            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public ValueTask DisposeAsync() => database.DisposeAsync();
}
