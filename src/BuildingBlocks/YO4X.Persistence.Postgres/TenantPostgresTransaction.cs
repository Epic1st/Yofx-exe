using System.Globalization;
using Npgsql;
using NpgsqlTypes;
using YO4X.Tenancy;

namespace YO4X.Persistence.Postgres;

public sealed class TenantPostgresTransaction : IAsyncDisposable
{
    private const string ReadTransactionBindingSql = """
        select control.assert_safe_runtime_role();
        select
            current_database(),
            current_user,
            pg_backend_pid(),
            pg_current_xact_id()::text
        """;

    private const string ActivateContextSql = """
        select control.activate_tenant_context(
            @capability,
            @tenant_id,
            @actor_id,
            @correlation_id,
            @session_id)
        """;

    private const string ActivateCredentialRuntimeContextSql = """
        select control.activate_credential_runtime_tenant_context(
            @capability,
            @tenant_id,
            @actor_id,
            @correlation_id,
            @session_id)
        """;

    private const string VerifyActivatedContextSql = """
        select
            control.assert_safe_runtime_role(),
            coalesce(control.current_tenant_id() = @tenant_id, false),
            coalesce(control.current_actor_id() = @actor_id, false),
            coalesce(control.current_correlation_id() = @correlation_id, false),
            control.current_session_id() is not distinct from @session_id
        """;

    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;
    private bool _completed;
    private bool _disposed;

    internal TenantPostgresTransaction(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TenantExecutionContext context)
    {
        _connection = connection;
        _transaction = transaction;
        Context = context;
    }

    public TenantExecutionContext Context { get; }

    public NpgsqlCommand CreateCommand(string commandText)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed)
        {
            throw new InvalidOperationException("The PostgreSQL transaction has already completed.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(commandText);
        return new NpgsqlCommand(commandText, _connection, _transaction);
    }

    internal async Task<TenantContextTransactionBinding> ReadTransactionBindingAsync(
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = CreateCommand(ReadTransactionBindingSql);
        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.NextResultAsync(cancellationToken).ConfigureAwait(false)
            || !await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "PostgreSQL did not return the transaction binding required for tenant-context activation.");
        }

        string transactionIdText = reader.GetString(3);
        if (!ulong.TryParse(
                transactionIdText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out ulong transactionId)
            || transactionId == 0
            || !string.Equals(
                transactionId.ToString(CultureInfo.InvariantCulture),
                transactionIdText,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "PostgreSQL returned a non-canonical full transaction identifier.");
        }

        return new TenantContextTransactionBinding(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt32(2),
            transactionId);
    }

    internal async Task ActivateContextAsync(
        TenantContextCapability capability,
        string runtimeRole,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeRole);

        string activationSql = string.Equals(
            runtimeRole,
            PostgresTenantContextCapabilityProvider.CredentialRuntimeRole,
            StringComparison.Ordinal)
            ? ActivateCredentialRuntimeContextSql
            : ActivateContextSql;
        await using NpgsqlCommand command = CreateCommand(activationSql);
        command.Parameters.AddWithValue(
            "capability",
            NpgsqlDbType.Bytea,
            capability.BorrowMaterial());
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, Context.TenantId);
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Uuid, Context.ActorId);
        command.Parameters.AddWithValue("correlation_id", NpgsqlDbType.Uuid, Context.CorrelationId);
        command.Parameters.AddWithValue(
            "session_id",
            NpgsqlDbType.Uuid,
            Context.SessionId is null ? DBNull.Value : Context.SessionId.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task VerifyActivatedContextAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = CreateCommand(VerifyActivatedContextSql);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, Context.TenantId);
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Uuid, Context.ActorId);
        command.Parameters.AddWithValue("correlation_id", NpgsqlDbType.Uuid, Context.CorrelationId);
        command.Parameters.AddWithValue(
            "session_id",
            NpgsqlDbType.Uuid,
            Context.SessionId is null ? DBNull.Value : Context.SessionId.Value);
        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            || !reader.GetBoolean(1)
            || !reader.GetBoolean(2)
            || !reader.GetBoolean(3)
            || !reader.GetBoolean(4)
            || await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new UnauthorizedAccessException(
                "PostgreSQL did not establish the exact authenticated tenant context.");
        }
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        EnsureCanComplete();
        await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        _completed = true;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        EnsureCanComplete();
        await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (!_completed)
            {
                await _transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                await _transaction.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    await _connection.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    _disposed = true;
                }
            }
        }
    }

    private void EnsureCanComplete()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed)
        {
            throw new InvalidOperationException("The PostgreSQL transaction has already completed.");
        }
    }
}
