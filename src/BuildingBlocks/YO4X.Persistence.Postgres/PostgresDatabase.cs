using Npgsql;
using YO4X.BuildingBlocks;
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
    private readonly ITenantContextCapabilityProvider? _tenantContextCapabilityProvider;
    private readonly PostgresDatabaseUsage _usage;

    public PostgresDatabaseEndpoint Endpoint { get; }

    public bool HasTenantContextCapabilityProvider =>
        _tenantContextCapabilityProvider is not null;

    public bool UsesTenantContextCapabilityProvider(
        ITenantContextCapabilityProvider provider) =>
        ReferenceEquals(
            _tenantContextCapabilityProvider,
            provider ?? throw new ArgumentNullException(nameof(provider)));

    public PostgresDatabase(
        string connectionString,
        PostgresDatabaseUsage usage = PostgresDatabaseUsage.Runtime,
        ITenantContextCapabilityProvider? tenantContextCapabilityProvider = null,
        bool allowInsecureLoopbackForDevelopment = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        if (!Enum.IsDefined(usage))
        {
            throw new ArgumentOutOfRangeException(nameof(usage));
        }

        var connectionOptions = new NpgsqlConnectionStringBuilder(connectionString);
        Endpoint = PostgresDatabaseEndpoint.From(connectionOptions);
        PostgresConnectionSafety.ValidateNoCallerControlledSessionState(
            connectionOptions,
            nameof(connectionString));
        if (!PostgresRuntimeConnectionPolicy.HasRequiredTransport(
                connectionOptions,
                allowInsecureLoopbackForDevelopment))
        {
            throw new ArgumentException(
                "PostgreSQL security-boundary connections require verified TLS transport, or an explicit loopback endpoint while the insecure-development escape is enabled.",
                nameof(connectionString));
        }

        connectionOptions.Enlist = false;
        connectionOptions.PersistSecurityInfo = false;
        if (usage == PostgresDatabaseUsage.Runtime)
        {
            if (tenantContextCapabilityProvider is not null
                && tenantContextCapabilityProvider.Endpoint != Endpoint)
            {
                throw new ArgumentException(
                    "The tenant-context capability provider must target the exact runtime PostgreSQL endpoint.",
                    nameof(tenantContextCapabilityProvider));
            }
        }
        else if (tenantContextCapabilityProvider is not null)
        {
            throw new ArgumentException(
                "A migrator connection cannot use a runtime tenant-context capability provider.",
                nameof(tenantContextCapabilityProvider));
        }

        var builder = new NpgsqlDataSourceBuilder(connectionOptions.ConnectionString);
        _dataSource = builder.Build();
        _usage = usage;
        _tenantContextCapabilityProvider = tenantContextCapabilityProvider;
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

    public ValueTask<bool> IsTenantContextCapabilityProviderReadyAsync(
        CancellationToken cancellationToken = default) =>
        _tenantContextCapabilityProvider is null
            ? ValueTask.FromResult(false)
            : _tenantContextCapabilityProvider.IsReadyAsync(cancellationToken);

    public async ValueTask<TenantPostgresTransaction> BeginTenantTransactionAsync(
        TenantExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_usage != PostgresDatabaseUsage.Runtime)
        {
            throw new InvalidOperationException(
                "Tenant transactions require a runtime PostgreSQL connection.");
        }

        ITenantContextCapabilityProvider capabilityProvider =
            _tenantContextCapabilityProvider
            ?? throw new BackendCapabilityUnavailableException(
                "postgres-tenant-context-issuer");

        NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var session = new TenantPostgresTransaction(connection, transaction, context);
                TenantContextTransactionBinding binding = await session
                    .ReadTransactionBindingAsync(cancellationToken)
                    .ConfigureAwait(false);
                using TenantContextCapability capability = await capabilityProvider
                    .AcquireAsync(context, binding, cancellationToken)
                    .ConfigureAwait(false);
                if (capability is null)
                {
                    throw new BackendCapabilityUnavailableException(
                        "postgres-tenant-context-issuer");
                }

                await session.ActivateContextAsync(
                        capability,
                        binding.RuntimeRole,
                        cancellationToken)
                    .ConfigureAwait(false);
                await session.VerifyActivatedContextAsync(cancellationToken).ConfigureAwait(false);
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
