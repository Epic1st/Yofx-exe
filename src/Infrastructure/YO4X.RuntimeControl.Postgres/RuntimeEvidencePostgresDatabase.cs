using Npgsql;
using YO4X.BuildingBlocks;
using YO4X.Persistence.Postgres;
using YO4X.Tenancy;

namespace YO4X.RuntimeControl.Postgres;

/// <summary>
/// Owns the capability-scoped database pool used only by the authenticated
/// broker-operation result ingress. The general runtime/worker identity never
/// receives proof insertion rights.
/// </summary>
public sealed class RuntimeEvidencePostgresDatabase : IAsyncDisposable
{
    private const string AssertCapabilitiesSql =
        """
        select current_user = 'yo4x_runtime_evidence'
           and has_function_privilege(current_user, 'control.acquire_u0_authority_lock()', 'EXECUTE')
           and has_table_privilege(current_user, 'operations.user_operation_results', 'SELECT,INSERT')
           and not has_table_privilege(current_user, 'operations.user_operation_results', 'UPDATE')
           and not has_table_privilege(current_user, 'operations.user_operation_results', 'DELETE')
           and has_column_privilege(current_user, 'control.user_operations', 'dispatch_message_id', 'SELECT')
           and has_column_privilege(current_user, 'operations.worker_assignments', 'supervisor_identity', 'SELECT')
           and has_column_privilege(current_user, 'operations.deployments', 'broker_account_id', 'SELECT')
           and has_column_privilege(current_user, 'operations.broker_accounts', 'id', 'SELECT')
           and has_column_privilege(current_user, 'messaging.outbox_messages', 'state', 'SELECT')
           and has_table_privilege(current_user, 'audit.audit_events', 'INSERT')
           and has_table_privilege(current_user, 'messaging.outbox_messages', 'INSERT')
           and not has_any_column_privilege(current_user, 'control.user_operations', 'UPDATE')
           and not has_any_column_privilege(current_user, 'operations.worker_assignments', 'UPDATE')
           and not has_any_column_privilege(current_user, 'operations.deployments', 'UPDATE')
           and not has_any_column_privilege(current_user, 'operations.broker_accounts', 'UPDATE')
           and not has_column_privilege(current_user, 'operations.broker_accounts', 'credential_reference', 'SELECT')
        """;

    private readonly PostgresDatabase database;

    public RuntimeEvidencePostgresDatabase(string connectionString)
    {
        database = new PostgresDatabase(connectionString, PostgresDatabaseUsage.Runtime);
    }

    public async ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await database
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
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
