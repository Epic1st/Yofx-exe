using Npgsql;
using YO4X.Admin.Application;
using YO4X.BuildingBlocks;
using YO4X.Persistence.Postgres;
using YO4X.Tenancy;

namespace YO4X.Admin.Postgres;

public interface IAdminPostgresReadiness
{
    ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken);
}

public sealed partial class AdminPostgresApplication : IAdminApplication, IAdminPostgresReadiness
{
    private readonly PostgresDatabase database;
    private readonly AdminPostgresOptions options;

    public AdminPostgresApplication(
        PostgresDatabase database,
        IClock clock,
        AdminPostgresOptions options)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
        ArgumentNullException.ThrowIfNull(clock);
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        options.Validate();
    }

    public async ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var command = new NpgsqlCommand(
                "select control.assert_safe_runtime_role()",
                connection);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (NpgsqlException)
        {
            return false;
        }
    }

    private async ValueTask<AdminOperationContext> BeginAsync(
        AdminActor actor,
        Guid correlationId,
        TimeSpan maximumAuthenticationAge,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.TenantId == Guid.Empty
            || actor.ActorId == Guid.Empty
            || actor.SessionId == Guid.Empty
            || correlationId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("The admin identity context is incomplete.");
        }

        var tenantContext = new TenantExecutionContext(
            actor.TenantId,
            actor.ActorId,
            correlationId,
            actor.SessionId);
        TenantPostgresTransaction transaction = await database.BeginTenantTransactionAsync(
            tenantContext,
            cancellationToken).ConfigureAwait(false);
        try
        {
            AdminSecuritySnapshot security = await AdminSecurityRepository.LoadAsync(
                transaction,
                actor,
                maximumAuthenticationAge,
                options.MaximumClockSkew,
                cancellationToken).ConfigureAwait(false);
            return new AdminOperationContext(transaction, security, security.AuthorizationNow);
        }
        catch
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class AdminOperationContext(
        TenantPostgresTransaction transaction,
        AdminSecuritySnapshot security,
        DateTimeOffset now) : IAsyncDisposable
    {
        public TenantPostgresTransaction Transaction { get; } = transaction;

        public AdminSecuritySnapshot Security { get; } = security;

        public DateTimeOffset Now { get; } = now;

        public ValueTask DisposeAsync() => Transaction.DisposeAsync();
    }
}
