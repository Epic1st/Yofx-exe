using Npgsql;
using NpgsqlTypes;
using YO4X.Tenancy;

namespace YO4X.Persistence.Postgres;

public sealed class TenantPostgresTransaction : IAsyncDisposable
{
    private const string SetContextSql = """
        select
            control.assert_safe_runtime_role(),
            set_config('yo4x.tenant_id', @tenant_id::text, true),
            set_config('yo4x.actor_id', @actor_id::text, true),
            set_config('yo4x.correlation_id', @correlation_id::text, true),
            set_config('yo4x.session_id', coalesce(@session_id::text, ''), true)
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

    internal async Task ApplyContextAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = CreateCommand(SetContextSql);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, Context.TenantId);
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Uuid, Context.ActorId);
        command.Parameters.AddWithValue("correlation_id", NpgsqlDbType.Uuid, Context.CorrelationId);
        command.Parameters.AddWithValue(
            "session_id",
            NpgsqlDbType.Uuid,
            Context.SessionId is null ? DBNull.Value : Context.SessionId.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
