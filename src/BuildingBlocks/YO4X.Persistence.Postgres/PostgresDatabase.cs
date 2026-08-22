using Npgsql;
using YO4X.Tenancy;

namespace YO4X.Persistence.Postgres;

public enum PostgresDatabaseUsage
{
    Runtime,
    Migrator
}

/// <summary>
/// Owns the pooled Npgsql data source and creates transaction-scoped tenant
/// sessions. Authorization context is never stored as session-global state.
/// </summary>
public sealed class PostgresDatabase : IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly PostgresDatabaseUsage _usage;

    public PostgresDatabase(
        string connectionString,
        PostgresDatabaseUsage usage = PostgresDatabaseUsage.Runtime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        if (!Enum.IsDefined(usage))
        {
            throw new ArgumentOutOfRangeException(nameof(usage));
        }

        var connectionOptions = new NpgsqlConnectionStringBuilder(connectionString);
        if (usage == PostgresDatabaseUsage.Runtime
            && (connectionOptions.IncludeErrorDetail || connectionOptions.LogParameters))
        {
            throw new ArgumentException(
                "Runtime PostgreSQL connections cannot expose error details or bind parameter values.",
                nameof(connectionString));
        }

        var builder = new NpgsqlDataSourceBuilder(connectionOptions.ConnectionString);
        _dataSource = builder.Build();
        _usage = usage;
    }

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        if (_usage != PostgresDatabaseUsage.Migrator)
        {
            throw new InvalidOperationException(
                "Schema migrations require a separately configured migrator database connection.");
        }

        await PostgresMigrationRunner.MigrateAsync(_dataSource, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<NpgsqlConnection> OpenConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        if (_usage == PostgresDatabaseUsage.Migrator)
        {
            return connection;
        }

        try
        {
            await using var assertion = new NpgsqlCommand(
                "select control.assert_safe_runtime_role()",
                connection);
            await assertion.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask<TenantPostgresTransaction> BeginTenantTransactionAsync(
        TenantExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var session = new TenantPostgresTransaction(connection, transaction, context);
                await session.ApplyContextAsync(cancellationToken).ConfigureAwait(false);
                return session;
            }
            catch
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync() =>
        await _dataSource.DisposeAsync().ConfigureAwait(false);
}
