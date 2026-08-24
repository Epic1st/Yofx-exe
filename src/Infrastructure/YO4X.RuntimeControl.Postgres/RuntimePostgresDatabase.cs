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

    public RuntimePostgresDatabase(
        string connectionString,
        ITenantContextCapabilityProvider? tenantContextCapabilityProvider = null,
        bool allowInsecureLoopbackForDevelopment = false)
    {
        database = new PostgresDatabase(
            connectionString,
            PostgresDatabaseUsage.Runtime,
            tenantContextCapabilityProvider,
            allowInsecureLoopbackForDevelopment);
    }

    public bool HasTenantContextCapabilityProvider =>
        database.HasTenantContextCapabilityProvider;

    public bool UsesTenantContextCapabilityProvider(
        ITenantContextCapabilityProvider provider) =>
        database.UsesTenantContextCapabilityProvider(provider);

    public ValueTask<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) =>
        database.OpenConnectionAsync(cancellationToken);

    public ValueTask<TenantPostgresTransaction> BeginTenantTransactionAsync(
        TenantExecutionContext context,
        CancellationToken cancellationToken = default) =>
        database.BeginTenantTransactionAsync(context, cancellationToken);

    public ValueTask DisposeAsync() => database.DisposeAsync();
}
