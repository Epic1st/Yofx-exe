using Npgsql;
using YO4X.Persistence.Postgres;
using YO4X.Tenancy;

namespace YO4X.RuntimeControl.Postgres;

/// <summary>
/// Owns the worker-role pool separately from every user/control-plane pool.
/// </summary>
public sealed class RuntimePostgresDatabase : IAsyncDisposable
{
    private readonly PostgresDatabase database;

    public RuntimePostgresDatabase(string connectionString)
    {
        database = new PostgresDatabase(connectionString, PostgresDatabaseUsage.Runtime);
    }

    public ValueTask<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) =>
        database.OpenConnectionAsync(cancellationToken);

    public ValueTask<TenantPostgresTransaction> BeginTenantTransactionAsync(
        TenantExecutionContext context,
        CancellationToken cancellationToken = default) =>
        database.BeginTenantTransactionAsync(context, cancellationToken);

    public ValueTask DisposeAsync() => database.DisposeAsync();
}
