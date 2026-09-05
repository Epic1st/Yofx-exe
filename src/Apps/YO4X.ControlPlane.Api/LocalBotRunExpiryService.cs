using Npgsql;
using YO4X.Persistence.Postgres;
using YO4X.Tenancy;

namespace YO4X.ControlPlane.Api;

internal sealed class LocalBotRunExpiryService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<LocalBotRunExpiryService> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> LogExpiryFailure = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(4101, nameof(LocalBotRunExpiryService)),
        "Could not expire stale local bot runs.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        MarketplacePublicationOptions? options = MarketplacePublicationOptions.Load(configuration);
        if (options is null)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await ExpireStaleRunsAsync(options, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
            {
                LogExpiryFailure(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task ExpireStaleRunsAsync(
        MarketplacePublicationOptions options,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        PostgresDatabase database = scope.ServiceProvider.GetRequiredService<PostgresDatabase>();
        var context = new TenantExecutionContext(options.TenantId, options.ActorId, Guid.CreateVersion7(), null);
        await using TenantPostgresTransaction transaction = await database
            .BeginTenantTransactionAsync(context, cancellationToken).ConfigureAwait(false);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            with expired as
            (
                update operations.local_bot_runs
                set state = 'EXPIRED', stopped_at = clock_timestamp(), updated_at = clock_timestamp()
                where tenant_id = @tenant and state in ('ISSUED', 'RUNNING')
                  and expires_at <= clock_timestamp()
                returning bot_id
            ), stale_bots as
            (
                select bot.id
                from bots.bots as bot
                where bot.tenant_id = @tenant
                  and bot.host = 'LOCAL'
                  and bot.status in ('STARTING', 'RUNNING')
                  and not exists
                  (
                      select 1
                      from operations.local_bot_runs as run
                      where run.tenant_id = bot.tenant_id
                        and run.bot_id = bot.id
                        and run.state in ('ISSUED', 'RUNNING')
                        and run.expires_at > clock_timestamp()
                  )
            )
            update bots.bots as bot
            set status = 'FAULTED',
                last_error_message = 'No active desktop runtime heartbeat exists. Start the bot again from YO4X Desktop.',
                updated_at = clock_timestamp()
            where bot.tenant_id = @tenant
              and bot.host = 'LOCAL'
              and bot.id in
              (
                  select bot_id from expired
                  union
                  select id from stale_bots
              )
              and bot.status in ('STARTING', 'RUNNING')
            """);
        command.Parameters.AddWithValue("tenant", options.TenantId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
