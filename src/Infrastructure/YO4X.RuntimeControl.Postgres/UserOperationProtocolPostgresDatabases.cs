using Npgsql;
using YO4X.BuildingBlocks;
using YO4X.Persistence.Postgres;
using YO4X.Tenancy;

namespace YO4X.RuntimeControl.Postgres;

public sealed class SupervisorUserOperationPostgresDatabase : IAsyncDisposable
{
    private readonly UserOperationRolePostgresDatabase database;

    public SupervisorUserOperationPostgresDatabase(
        string connectionString,
        ITenantContextCapabilityProvider? tenantContextCapabilityProvider = null,
        bool allowInsecureLoopbackForDevelopment = false)
    {
        database = new UserOperationRolePostgresDatabase(
            UserOperationRoleConnectionString.Require(
                connectionString,
                "yo4x_supervisor_runtime",
                allowInsecureLoopbackForDevelopment),
            tenantContextCapabilityProvider,
            Yo4xPostgresRoleContracts.SupervisorRuntime,
            "user_operation_supervisor_postgres",
            allowInsecureLoopbackForDevelopment);
    }

    public bool HasTenantContextCapabilityProvider =>
        database.HasTenantContextCapabilityProvider;

    public bool UsesTenantContextCapabilityProvider(
        ITenantContextCapabilityProvider provider) =>
        database.UsesTenantContextCapabilityProvider(provider);

    public ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken = default) =>
        database.IsReadyAsync(cancellationToken);

    internal ValueTask<TenantPostgresTransaction> BeginTenantTransactionAsync(
        TenantExecutionContext context,
        CancellationToken cancellationToken) =>
        database.BeginTenantTransactionAsync(context, cancellationToken);

    public ValueTask DisposeAsync() => database.DisposeAsync();
}

public sealed class GatewayUserOperationPostgresDatabase : IAsyncDisposable
{
    private readonly UserOperationRolePostgresDatabase database;

    public GatewayUserOperationPostgresDatabase(
        string connectionString,
        ITenantContextCapabilityProvider? tenantContextCapabilityProvider = null,
        bool allowInsecureLoopbackForDevelopment = false)
    {
        database = new UserOperationRolePostgresDatabase(
            UserOperationRoleConnectionString.Require(
                connectionString,
                "yo4x_gateway_runtime",
                allowInsecureLoopbackForDevelopment),
            tenantContextCapabilityProvider,
            Yo4xPostgresRoleContracts.GatewayRuntime,
            "user_operation_gateway_postgres",
            allowInsecureLoopbackForDevelopment);
    }

    public bool HasTenantContextCapabilityProvider =>
        database.HasTenantContextCapabilityProvider;

    public bool UsesTenantContextCapabilityProvider(
        ITenantContextCapabilityProvider provider) =>
        database.UsesTenantContextCapabilityProvider(provider);

    public ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken = default) =>
        database.IsReadyAsync(cancellationToken);

    internal ValueTask<TenantPostgresTransaction> BeginTenantTransactionAsync(
        TenantExecutionContext context,
        CancellationToken cancellationToken) =>
        database.BeginTenantTransactionAsync(context, cancellationToken);

    public ValueTask DisposeAsync() => database.DisposeAsync();
}

public sealed class CredentialUserOperationPostgresDatabase : IAsyncDisposable
{
    private readonly UserOperationRolePostgresDatabase database;

    public CredentialUserOperationPostgresDatabase(
        string connectionString,
        ITenantContextCapabilityProvider? tenantContextCapabilityProvider = null,
        bool allowInsecureLoopbackForDevelopment = false)
    {
        database = new UserOperationRolePostgresDatabase(
            UserOperationRoleConnectionString.Require(
                connectionString,
                "yo4x_credential_runtime",
                allowInsecureLoopbackForDevelopment),
            tenantContextCapabilityProvider,
            Yo4xPostgresRoleContracts.CredentialRuntime,
            "user_operation_credential_postgres",
            allowInsecureLoopbackForDevelopment);
    }

    public bool HasTenantContextCapabilityProvider =>
        database.HasTenantContextCapabilityProvider;

    public bool UsesTenantContextCapabilityProvider(
        ITenantContextCapabilityProvider provider) =>
        database.UsesTenantContextCapabilityProvider(provider);

    public ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken = default) =>
        database.IsReadyAsync(cancellationToken);

    internal ValueTask<TenantPostgresTransaction> BeginTenantTransactionAsync(
        TenantExecutionContext context,
        CancellationToken cancellationToken) =>
        database.BeginTenantTransactionAsync(context, cancellationToken);

    public ValueTask DisposeAsync() => database.DisposeAsync();
}

internal sealed class UserOperationRolePostgresDatabase : IAsyncDisposable
{
    private readonly PostgresDatabase database;
    private readonly PostgresRoleCapabilityContract roleContract;
    private readonly string capabilityName;

    public UserOperationRolePostgresDatabase(
        string connectionString,
        ITenantContextCapabilityProvider? tenantContextCapabilityProvider,
        PostgresRoleCapabilityContract roleContract,
        string capabilityName,
        bool allowInsecureLoopbackForDevelopment)
    {
        database = new PostgresDatabase(
            connectionString,
            PostgresDatabaseUsage.Runtime,
            tenantContextCapabilityProvider,
            allowInsecureLoopbackForDevelopment);
        this.roleContract = roleContract ?? throw new ArgumentNullException(nameof(roleContract));
        this.capabilityName = string.IsNullOrWhiteSpace(capabilityName)
            ? throw new ArgumentException("A capability name is required.", nameof(capabilityName))
            : capabilityName;
    }

    public bool HasTenantContextCapabilityProvider =>
        database.HasTenantContextCapabilityProvider;

    public bool UsesTenantContextCapabilityProvider(
        ITenantContextCapabilityProvider provider) =>
        database.UsesTenantContextCapabilityProvider(provider);

    public async ValueTask<bool> IsReadyAsync(
        CancellationToken cancellationToken = default)
    {
        if (!HasTenantContextCapabilityProvider
            || !await database.IsTenantContextCapabilityProviderReadyAsync(cancellationToken)
                .ConfigureAwait(false))
        {
            return false;
        }

        await using NpgsqlConnection connection = await database
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        return await PostgresRoleCapabilityFingerprint.IsSatisfiedAsync(
                connection,
                transaction: null,
                roleContract,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<TenantPostgresTransaction> BeginTenantTransactionAsync(
        TenantExecutionContext context,
        CancellationToken cancellationToken)
    {
        TenantPostgresTransaction transaction = await database
            .BeginTenantTransactionAsync(context, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            if (!await PostgresRoleCapabilityFingerprint.IsSatisfiedAsync(
                    transaction,
                    roleContract,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                throw new BackendCapabilityUnavailableException(capabilityName);
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

internal static class UserOperationRoleConnectionString
{
    public static string Require(
        string connectionString,
        string requiredRole,
        bool allowInsecureLoopbackForDevelopment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (!string.Equals(builder.Username, requiredRole, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The PostgreSQL connection is not bound to the required protocol role.",
                nameof(connectionString));
        }

        if (!PostgresRuntimeConnectionPolicy.HasSafeSessionConfiguration(builder)
            || !PostgresRuntimeConnectionPolicy.HasRequiredTransport(
                builder,
                allowInsecureLoopbackForDevelopment))
        {
            throw new ArgumentException(
                "The PostgreSQL protocol connection must use safe session options and SSL Mode=VerifyFull. "
                + "The development-only plaintext escape accepts only SSL Mode=Disable on an explicit loopback host.",
                nameof(connectionString));
        }

        return builder.ConnectionString;
    }
}
