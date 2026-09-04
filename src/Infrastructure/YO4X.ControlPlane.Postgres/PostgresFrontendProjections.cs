using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.Persistence.Postgres;
using YO4X.Tenancy;

namespace YO4X.ControlPlane.Postgres;

/// <summary>
/// PostgreSQL-backed adapter for the frontend projection surfaces. Every statement runs inside a
/// tenant-bound transaction and filters the authenticated tenant; user-owned tables additionally
/// filter the authenticated user. Nothing here grants trading authority or relaxes an existing
/// control-plane guard.
/// </summary>
public sealed class PostgresFrontendProjections : IFrontendProjectionApplication
{
    private const int CatalogPageSizeDefault = 24;
    private const int CatalogPageSizeMaximum = 60;
    private const int CatalogFacetLimit = 200;
    private const int ReviewLimitDefault = 20;
    private const int ReviewLimitMaximum = 100;
    private const int JournalLimitDefault = 50;
    private const int JournalLimitMaximum = 200;
    private const int UptimeDaysDefault = 7;
    private const int UptimeDaysMaximum = 90;
    private const int BotListLimit = 200;
    private const int BotMetricLimit = 1_000;
    private const int BacktestListLimit = 200;
    private const int StrategyInputLimit = 1_000;
    private const int StrategyEnumMemberLimit = 5_000;
    private const int BacktestInputLimit = 1_000;
    private const int BotInputLimit = 1_000;

    /// <summary>
    /// One page of a broker's instrument list. A server offers thousands of symbols,
    /// and the read behind this is a picker the operator narrows with a substring, so
    /// the cap bounds the response instead of the caller doing so.
    /// </summary>
    private const int BrokerSymbolLimit = 500;

    /// <summary>
    /// The whole stored curve for one request. 009_backtest_equity_curve.sql caps a
    /// written curve at 2001 rows — 2000 strided samples plus the retained final one —
    /// so this reads every point a conforming writer can have stored and never returns
    /// a silently shortened curve.
    /// </summary>
    private const int BacktestEquityPointLimit = 2_001;

    private const int PerformanceFigureLimit = 50;
    private const int EquityPointLimit = 500;
    private const int CloudPlanLimit = 50;
    private const int CloudPlanFeatureLimit = 500;
    private const int CloudRegionLimit = 100;
    private const int CloudRunnerLimit = 200;
    private const int MaximumNameLength = 120;
    private const int MaximumSymbolLength = 32;
    private const int MaximumRiskLabelLength = 64;
    private const int MaximumTimeframeLength = 32;
    private const int MaximumInputValueLength = 4_000;
    private const int MaximumBrokerServerLength = 255;

    /// <summary>
    /// The widest volume <c>bots.volume numeric(12,2)</c> can hold. A wider one is
    /// refused here rather than being handed to PostgreSQL to overflow on.
    /// </summary>
    private const decimal MaximumBotVolume = 9_999_999_999.99m;

    /// <summary>
    /// What the settings form starts from for a bot whose operator has never stated a
    /// timeframe or a volume. The columns stay null in the database — nothing is written
    /// on a read — so the row still records plainly that nobody has chosen yet. These are
    /// the values the form offers until somebody does: MetaTrader's own default chart
    /// period, and the smallest size every broker in the directory accepts.
    /// <para>
    /// They are substituted rather than reported as null or zero because the settings
    /// contract types all three fields as non-nullable, and because a form field showing
    /// a blank period or a size of zero offers the operator nothing to correct. A magic
    /// number is different and is reported as zero: zero is MetaTrader's own "no magic
    /// number", so the stored value and the absent one mean the same thing.
    /// </para>
    /// </summary>
    private const string DefaultBotTimeframe = "H1";

    private const decimal DefaultBotVolume = 0.01m;
    private const string DefaultCurrency = "USD";
    private const string RequestInvalidCode = "FRONTEND_PROJECTION_REQUEST_INVALID";

    /// <summary>
    /// Reported when a saved bot input does not match the strategy's own declarations.
    /// It is distinct from <see cref="RequestInvalidCode"/> so the settings form can tell
    /// a refused input apart from a refused symbol, timeframe, volume or magic number.
    /// </summary>
    private const string BotInputInvalidCode = "BOT_INPUTS_INVALID";

    /// <summary>
    /// Marker written into a detail view when a request predates migration 006 and
    /// therefore genuinely never stated the field. It is never a guessed value.
    /// </summary>
    private const string UnspecifiedMarker = "UNSPECIFIED";

    /// <summary>
    /// MetaTrader's twenty-one chart periods, mirrored exactly and in ascending order.
    /// A bot's timeframe is accepted only from this closed set, so a stored value is
    /// always a period MetaTrader itself names: never a minute count, never a lowercase
    /// spelling, and never text the caller invented.
    /// </summary>
    private static readonly string[] BotTimeframes =
    [
        "M1", "M2", "M3", "M4", "M5", "M6", "M10", "M12", "M15", "M20", "M30",
        "H1", "H2", "H3", "H4", "H6", "H8", "H12", "D1", "W1", "MN1"
    ];

    /// <summary>MetaTrader's four modelling modes, mirrored exactly.</summary>
    private static readonly string[] BacktestModels =
    [
        "EVERY_TICK_REAL",
        "EVERY_TICK_M1",
        "OHLC_M1",
        "OPEN_PRICES"
    ];

    private const string StrategyCatalogProjection =
        """
        select
            strategy.id,
            strategy.slug,
            strategy.name,
            strategy.author_name,
            strategy.author_initials,
            strategy.category,
            strategy.symbol,
            strategy.timeframe,
            strategy.version,
            strategy.rating_average,
            strategy.rating_count,
            strategy.active_users,
            strategy.is_free,
            strategy.cloud_price_monthly_cents,
            strategy.cloud_price_yearly_cents,
            strategy.currency,
            strategy.updated_at
        from catalog.strategies as strategy
        where strategy.tenant_id = @tenant_id
          and (
              (strategy.package_format_version >= 2 and lower(btrim(strategy.name)) like '%.yo4x')
              or not exists
              (
                  select 1
                  from catalog.strategies as packaged
                  where packaged.tenant_id = strategy.tenant_id
                    and packaged.id <> strategy.id
                    and packaged.package_format_version >= 2
                    and lower(btrim(packaged.name)) like '%.yo4x'
                    and regexp_replace(lower(btrim(packaged.name)), '\.(mq5|yo4x)$', '')
                        = regexp_replace(lower(btrim(strategy.name)), '\.(mq5|yo4x)$', '')
              )
          )
          and (
              coalesce(strategy.package_format_version, 1) < 2
              or not exists
              (
                  select 1
                  from catalog.strategies as newer_package
                  where newer_package.tenant_id = strategy.tenant_id
                    and newer_package.id <> strategy.id
                    and newer_package.package_format_version >= 2
                    and lower(btrim(newer_package.name)) like '%.yo4x'
                    and regexp_replace(lower(btrim(newer_package.name)), '\.(mq5|yo4x)$', '')
                        = regexp_replace(lower(btrim(strategy.name)), '\.(mq5|yo4x)$', '')
                    and (newer_package.updated_at, newer_package.id)
                        > (strategy.updated_at, strategy.id)
              )
          )
          and (@category is null or strategy.category = @category)
          and (@symbol is null or strategy.symbol = @symbol)
          and (
              @search is null
              or strpos(lower(strategy.name), @search) > 0
              or strpos(lower(strategy.slug), @search) > 0
              or strpos(lower(strategy.summary), @search) > 0
          )
        """;

    private const string StrategyCatalogCount =
        """
        select count(*)
        from catalog.strategies as strategy
        where strategy.tenant_id = @tenant_id
          and (
              (strategy.package_format_version >= 2 and lower(btrim(strategy.name)) like '%.yo4x')
              or not exists
              (
                  select 1 from catalog.strategies as packaged
                  where packaged.tenant_id = strategy.tenant_id
                    and packaged.id <> strategy.id
                    and packaged.package_format_version >= 2
                    and lower(btrim(packaged.name)) like '%.yo4x'
                    and regexp_replace(lower(btrim(packaged.name)), '\.(mq5|yo4x)$', '')
                        = regexp_replace(lower(btrim(strategy.name)), '\.(mq5|yo4x)$', '')
              )
          )
          and (
              coalesce(strategy.package_format_version, 1) < 2
              or not exists
              (
                  select 1 from catalog.strategies as newer_package
                  where newer_package.tenant_id = strategy.tenant_id
                    and newer_package.id <> strategy.id
                    and newer_package.package_format_version >= 2
                    and lower(btrim(newer_package.name)) like '%.yo4x'
                    and regexp_replace(lower(btrim(newer_package.name)), '\.(mq5|yo4x)$', '')
                        = regexp_replace(lower(btrim(strategy.name)), '\.(mq5|yo4x)$', '')
                    and (newer_package.updated_at, newer_package.id)
                        > (strategy.updated_at, strategy.id)
              )
          )
          and (@category is null or strategy.category = @category)
          and (@symbol is null or strategy.symbol = @symbol)
          and (
              @search is null
              or strpos(lower(strategy.name), @search) > 0
              or strpos(lower(strategy.slug), @search) > 0
              or strpos(lower(strategy.summary), @search) > 0
          )
        """;

    private const string StrategyDetailProjection =
        """
        select
            strategy.id,
            strategy.slug,
            strategy.name,
            strategy.author_name,
            strategy.author_initials,
            strategy.category,
            strategy.symbol,
            strategy.timeframe,
            strategy.version,
            strategy.rating_average,
            strategy.rating_count,
            strategy.active_users,
            strategy.is_free,
            strategy.cloud_price_monthly_cents,
            strategy.cloud_price_yearly_cents,
            strategy.currency,
            strategy.updated_at,
            strategy.summary,
            strategy.description
        from catalog.strategies as strategy
        where strategy.tenant_id = @tenant_id
          and strategy.id = @strategy_id
          and (
              (strategy.package_format_version >= 2 and lower(btrim(strategy.name)) like '%.yo4x')
              or not exists
              (
                  select 1 from catalog.strategies as packaged
                  where packaged.tenant_id = strategy.tenant_id
                    and packaged.id <> strategy.id
                    and packaged.package_format_version >= 2
                    and lower(btrim(packaged.name)) like '%.yo4x'
                    and regexp_replace(lower(btrim(packaged.name)), '\.(mq5|yo4x)$', '')
                        = regexp_replace(lower(btrim(strategy.name)), '\.(mq5|yo4x)$', '')
              )
          )
          and (
              coalesce(strategy.package_format_version, 1) < 2
              or not exists
              (
                  select 1 from catalog.strategies as newer_package
                  where newer_package.tenant_id = strategy.tenant_id
                    and newer_package.id <> strategy.id
                    and newer_package.package_format_version >= 2
                    and lower(btrim(newer_package.name)) like '%.yo4x'
                    and regexp_replace(lower(btrim(newer_package.name)), '\.(mq5|yo4x)$', '')
                        = regexp_replace(lower(btrim(strategy.name)), '\.(mq5|yo4x)$', '')
                    and (newer_package.updated_at, newer_package.id)
                        > (strategy.updated_at, strategy.id)
              )
          )
        """;

    private const string StrategyCategoryFacet =
        """
        select distinct strategy.category
        from catalog.strategies as strategy
        where strategy.tenant_id = @tenant_id
          and (
              (strategy.package_format_version >= 2 and lower(btrim(strategy.name)) like '%.yo4x')
              or not exists
              (
                  select 1 from catalog.strategies as packaged
                  where packaged.tenant_id = strategy.tenant_id
                    and packaged.id <> strategy.id
                    and packaged.package_format_version >= 2
                    and lower(btrim(packaged.name)) like '%.yo4x'
                    and regexp_replace(lower(btrim(packaged.name)), '\.(mq5|yo4x)$', '')
                        = regexp_replace(lower(btrim(strategy.name)), '\.(mq5|yo4x)$', '')
              )
          )
          and (
              coalesce(strategy.package_format_version, 1) < 2
              or not exists
              (
                  select 1 from catalog.strategies as newer_package
                  where newer_package.tenant_id = strategy.tenant_id
                    and newer_package.id <> strategy.id
                    and newer_package.package_format_version >= 2
                    and lower(btrim(newer_package.name)) like '%.yo4x'
                    and regexp_replace(lower(btrim(newer_package.name)), '\.(mq5|yo4x)$', '')
                        = regexp_replace(lower(btrim(strategy.name)), '\.(mq5|yo4x)$', '')
                    and (newer_package.updated_at, newer_package.id)
                        > (strategy.updated_at, strategy.id)
              )
          )
        order by 1
        limit @limit
        """;

    private const string StrategySymbolFacet =
        """
        select distinct strategy.symbol
        from catalog.strategies as strategy
        where strategy.tenant_id = @tenant_id
          and (
              (strategy.package_format_version >= 2 and lower(btrim(strategy.name)) like '%.yo4x')
              or not exists
              (
                  select 1 from catalog.strategies as packaged
                  where packaged.tenant_id = strategy.tenant_id
                    and packaged.id <> strategy.id
                    and packaged.package_format_version >= 2
                    and lower(btrim(packaged.name)) like '%.yo4x'
                    and regexp_replace(lower(btrim(packaged.name)), '\.(mq5|yo4x)$', '')
                        = regexp_replace(lower(btrim(strategy.name)), '\.(mq5|yo4x)$', '')
              )
          )
          and (
              coalesce(strategy.package_format_version, 1) < 2
              or not exists
              (
                  select 1 from catalog.strategies as newer_package
                  where newer_package.tenant_id = strategy.tenant_id
                    and newer_package.id <> strategy.id
                    and newer_package.package_format_version >= 2
                    and lower(btrim(newer_package.name)) like '%.yo4x'
                    and regexp_replace(lower(btrim(newer_package.name)), '\.(mq5|yo4x)$', '')
                        = regexp_replace(lower(btrim(strategy.name)), '\.(mq5|yo4x)$', '')
                    and (newer_package.updated_at, newer_package.id)
                        > (strategy.updated_at, strategy.id)
              )
          )
        order by 1
        limit @limit
        """;

    private const string BotProjection =
        """
        select
            bot.id,
            case
                when strategy.package_format_version = 2
                 and lower(strategy.name) like '%.yo4x'
                then strategy.name
                else bot.name
            end,
            bot.strategy_id,
            strategy.name,
            bot.broker_account_id,
            account.masked_login,
            bot.symbol,
            bot.risk_label,
            bot.status,
            bot.host,
            bot.last_error_code,
            bot.last_error_message,
            bot.created_at,
            bot.updated_at
        from bots.bots as bot
        join catalog.strategies as strategy
          on strategy.tenant_id = bot.tenant_id
         and strategy.id = bot.strategy_id
        left join operations.broker_accounts as account
          on account.tenant_id = bot.tenant_id
         and account.user_id = bot.user_id
         and account.id = bot.broker_account_id
        where bot.tenant_id = @tenant_id
          and bot.user_id = @user_id
          and (@bot_id is null or bot.id = @bot_id)
          and (
              (
                  strategy.package_format_version = 2
                  and lower(strategy.name) like '%.yo4x'
              )
              or bot.status in ('RUNNING', 'STARTING')
              or not exists
              (
                  select 1
                  from bots.bots as packaged_bot
                  join catalog.strategies as packaged_strategy
                    on packaged_strategy.tenant_id = packaged_bot.tenant_id
                   and packaged_strategy.id = packaged_bot.strategy_id
                  where packaged_bot.tenant_id = bot.tenant_id
                    and packaged_bot.user_id = bot.user_id
                    and packaged_bot.broker_account_id is not distinct from bot.broker_account_id
                    and packaged_bot.symbol = bot.symbol
                    and packaged_bot.host = bot.host
                    and packaged_strategy.package_format_version = 2
                    and lower(packaged_strategy.name) like '%.yo4x'
                    and regexp_replace(lower(packaged_strategy.name), '\.yo4x$', '')
                        = regexp_replace(lower(bot.name), '\.(mq5|yo4x)$', '')
              )
          )
        order by bot.created_at desc, bot.id desc
        limit @limit
        """;

    private const string BotMetricProjection =
        """
        select
            metric.bot_id,
            metric.metric_window,
            metric.pl_amount,
            metric.currency,
            metric.trade_count
        from bots.bot_metrics as metric
        join bots.bots as bot
          on bot.tenant_id = metric.tenant_id
         and bot.id = metric.bot_id
        where metric.tenant_id = @tenant_id
          and bot.user_id = @user_id
          and (@bot_id is null or metric.bot_id = @bot_id)
        order by metric.bot_id, metric.metric_window
        limit @limit
        """;

    private static readonly string BridgeVersion = ReadAssemblyVersion();

    private readonly PostgresDatabase database;

    public PostgresFrontendProjections(PostgresDatabase database) =>
        this.database = database ?? throw new ArgumentNullException(nameof(database));

    public async Task<StrategyCatalogPage> GetStrategyCatalogAsync(
        UserActor actor,
        StrategyCatalogQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        int pageSize = ClampPageSize(query.PageSize);
        int page = query.Page < 1 ? 1 : query.Page;
        string? category = NormalizeFilter(query.Category);
        string? symbol = NormalizeFilter(query.Symbol);
        string? search = NormalizeSearch(query.Query);
        string orderBy = ResolveCatalogOrder(query.Sort);

        TenantPostgresTransaction transaction = await BeginAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            int totalCount;
            await using (NpgsqlCommand count = transaction.CreateCommand(StrategyCatalogCount))
            {
                AddUuid(count, "tenant_id", actor.TenantId);
                AddNullableText(count, "category", category);
                AddNullableText(count, "symbol", symbol);
                AddNullableText(count, "search", search);
                totalCount = ToCount(
                    await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            }

            int totalPages = totalCount == 0 ? 0 : (totalCount + pageSize - 1) / pageSize;
            var items = new List<StrategyCatalogItem>();
            await using (NpgsqlCommand command = transaction.CreateCommand(
                StrategyCatalogProjection + "\n" + orderBy + "\nlimit @limit offset @offset"))
            {
                AddUuid(command, "tenant_id", actor.TenantId);
                AddNullableText(command, "category", category);
                AddNullableText(command, "symbol", symbol);
                AddNullableText(command, "search", search);
                AddInteger(command, "limit", pageSize);
                AddInteger(command, "offset", ResolveOffset(page, pageSize));

                await using NpgsqlDataReader reader = await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    items.Add(ReadCatalogItem(reader));
                }
            }

            IReadOnlyList<string> categories = await ReadFacetAsync(
                transaction,
                actor,
                StrategyCategoryFacet,
                cancellationToken).ConfigureAwait(false);
            IReadOnlyList<string> symbols = await ReadFacetAsync(
                transaction,
                actor,
                StrategySymbolFacet,
                cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new StrategyCatalogPage(
                page,
                pageSize,
                totalCount,
                totalPages,
                items.AsReadOnly(),
                categories,
                symbols);
        }
    }

    public async Task<StrategyDetailView?> GetStrategyDetailAsync(
        UserActor actor,
        Guid strategyId,
        CancellationToken cancellationToken)
    {
        TenantPostgresTransaction transaction = await BeginAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            StrategyCatalogItem? item = null;
            string summary = string.Empty;
            string description = string.Empty;
            await using (NpgsqlCommand command = transaction.CreateCommand(StrategyDetailProjection))
            {
                AddUuid(command, "tenant_id", actor.TenantId);
                AddUuid(command, "strategy_id", strategyId);

                await using NpgsqlDataReader reader = await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    item = ReadCatalogItem(reader);
                    summary = reader.IsDBNull(17) ? string.Empty : reader.GetString(17);
                    description = reader.IsDBNull(18) ? string.Empty : reader.GetString(18);
                }
            }

            if (item is null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            int authorStrategyCount;
            decimal authorRating;
            await using (NpgsqlCommand command = transaction.CreateCommand(
                """
                select
                    count(*),
                    coalesce(avg(peer.rating_average), 0)
                from catalog.strategies as peer
                where peer.tenant_id = @tenant_id
                  and peer.author_name = @author_name
                """))
            {
                AddUuid(command, "tenant_id", actor.TenantId);
                AddText(command, "author_name", item.AuthorName);

                await using NpgsqlDataReader reader = await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    authorStrategyCount = ToCount(reader.GetInt64(0));
                    authorRating = Math.Round(reader.GetDecimal(1), 2, MidpointRounding.AwayFromZero);
                }
                else
                {
                    authorStrategyCount = 0;
                    authorRating = 0m;
                }
            }

            var performance = new List<StrategyPerformanceFigure>();
            await using (NpgsqlCommand command = transaction.CreateCommand(
                """
                select figure.ordinal, figure.label, figure.value
                from catalog.strategy_performance as figure
                where figure.tenant_id = @tenant_id
                  and figure.strategy_id = @strategy_id
                order by figure.ordinal
                limit @limit
                """))
            {
                AddUuid(command, "tenant_id", actor.TenantId);
                AddUuid(command, "strategy_id", strategyId);
                AddInteger(command, "limit", PerformanceFigureLimit);

                await using NpgsqlDataReader reader = await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    performance.Add(new StrategyPerformanceFigure(
                        reader.GetInt32(0),
                        reader.GetString(1),
                        reader.GetString(2)));
                }
            }

            var equityCurve = new List<StrategyEquityPoint>();
            await using (NpgsqlCommand command = transaction.CreateCommand(
                """
                select point.ordinal, point.period_label, point.equity
                from catalog.strategy_equity_points as point
                where point.tenant_id = @tenant_id
                  and point.strategy_id = @strategy_id
                order by point.ordinal
                limit @limit
                """))
            {
                AddUuid(command, "tenant_id", actor.TenantId);
                AddUuid(command, "strategy_id", strategyId);
                AddInteger(command, "limit", EquityPointLimit);

                await using NpgsqlDataReader reader = await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    equityCurve.Add(new StrategyEquityPoint(
                        reader.GetInt32(0),
                        reader.GetString(1),
                        reader.GetDecimal(2)));
                }
            }

            int reviewCount;
            await using (NpgsqlCommand command = transaction.CreateCommand(
                """
                select count(*)
                from catalog.strategy_reviews as review
                where review.tenant_id = @tenant_id
                  and review.strategy_id = @strategy_id
                """))
            {
                AddUuid(command, "tenant_id", actor.TenantId);
                AddUuid(command, "strategy_id", strategyId);
                reviewCount = ToCount(
                    await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new StrategyDetailView(
                item,
                summary,
                description,
                new StrategyAuthorView(
                    item.AuthorName,
                    item.AuthorInitials,
                    authorStrategyCount,
                    authorRating),
                performance.AsReadOnly(),
                equityCurve.AsReadOnly(),
                reviewCount);
        }
    }

    public async Task<IReadOnlyList<StrategyReviewView>> GetStrategyReviewsAsync(
        UserActor actor,
        Guid strategyId,
        int limit,
        CancellationToken cancellationToken)
    {
        int clampedLimit = limit <= 0
            ? ReviewLimitDefault
            : Math.Min(limit, ReviewLimitMaximum);

        TenantPostgresTransaction transaction = await BeginAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                select
                    review.id,
                    review.display_name,
                    review.initials,
                    review.rating,
                    review.body,
                    review.meta,
                    review.created_at
                from catalog.strategy_reviews as review
                where review.tenant_id = @tenant_id
                  and review.strategy_id = @strategy_id
                order by review.created_at desc, review.id desc
                limit @limit
                """);
            AddUuid(command, "tenant_id", actor.TenantId);
            AddUuid(command, "strategy_id", strategyId);
            AddInteger(command, "limit", clampedLimit);

            var reviews = new List<StrategyReviewView>();
            await using (NpgsqlDataReader reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    reviews.Add(new StrategyReviewView(
                        reader.GetGuid(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetInt16(3),
                        reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                        reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                        reader.GetFieldValue<DateTimeOffset>(6)));
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return reviews.AsReadOnly();
        }
    }

    public async Task<StrategyInputsView?> GetStrategyInputsAsync(
        UserActor actor,
        Guid strategyId,
        CancellationToken cancellationToken)
    {
        TenantPostgresTransaction transaction = await BeginAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            string? strategyName = await FindStrategyNameAsync(
                transaction,
                actor,
                strategyId,
                cancellationToken).ConfigureAwait(false);
            if (strategyName is null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            IReadOnlyList<StrategyInputView> inputs = await LoadStrategyInputsAsync(
                transaction,
                actor,
                strategyId,
                cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new StrategyInputsView(strategyId, strategyName, inputs);
        }
    }

    public async Task<IReadOnlyList<BotView>> GetBotsAsync(
        UserActor actor,
        CancellationToken cancellationToken)
    {
        TenantPostgresTransaction transaction = await BeginAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            IReadOnlyList<BotView> bots = await LoadBotsAsync(
                transaction,
                actor,
                botId: null,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return bots;
        }
    }

    public async Task<BotView?> GetBotAsync(
        UserActor actor,
        Guid botId,
        CancellationToken cancellationToken)
    {
        TenantPostgresTransaction transaction = await BeginAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            IReadOnlyList<BotView> bots = await LoadBotsAsync(
                transaction,
                actor,
                botId,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return bots.Count == 0 ? null : bots[0];
        }
    }

    public async Task<BotView> CreateBotAsync(
        UserActor actor,
        CreateBot request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string name = RequireBoundedText(request.Name, MaximumNameLength, "name");
        string symbol = RequireBoundedText(request.Symbol, MaximumSymbolLength, "symbol");
        string riskLabel = RequireBoundedText(request.RiskLabel, MaximumRiskLabelLength, "risk label");
        string host = FormatBotHost(request.Host);
        if (request.StrategyId == Guid.Empty)
        {
            throw new DomainException(RequestInvalidCode, "The strategy identifier is required.");
        }

        TenantPostgresTransaction transaction = await BeginAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            await RequireStrategyAsync(transaction, actor, request.StrategyId, cancellationToken)
                .ConfigureAwait(false);
            if (request.BrokerAccountId is Guid brokerAccountId)
            {
                await using NpgsqlCommand account = transaction.CreateCommand(
                    """
                    select 1
                    from operations.broker_accounts as account
                    where account.tenant_id = @tenant_id
                      and account.user_id = @user_id
                      and account.id = @broker_account_id
                      and account.state <> 'deleted'
                    """);
                AddUuid(account, "tenant_id", actor.TenantId);
                AddUuid(account, "user_id", actor.UserId);
                AddUuid(account, "broker_account_id", brokerAccountId);
                if (await account.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is null)
                {
                    throw new ResourceNotFoundException();
                }
            }

            Guid botId = Guid.CreateVersion7();
            await using (NpgsqlCommand insert = transaction.CreateCommand(
                """
                insert into bots.bots
                (
                    id, tenant_id, user_id, strategy_id, broker_account_id,
                    name, symbol, risk_label, status, host, created_at, updated_at
                )
                values
                (
                    @id, @tenant_id, @user_id, @strategy_id, @broker_account_id,
                    @name, @symbol, @risk_label, 'DRAFT', @host,
                    clock_timestamp(), clock_timestamp()
                )
                """))
            {
                AddUuid(insert, "id", botId);
                AddUuid(insert, "tenant_id", actor.TenantId);
                AddUuid(insert, "user_id", actor.UserId);
                AddUuid(insert, "strategy_id", request.StrategyId);
                AddNullableUuid(insert, "broker_account_id", request.BrokerAccountId);
                AddText(insert, "name", name);
                AddText(insert, "symbol", symbol);
                AddText(insert, "risk_label", riskLabel);
                AddText(insert, "host", host);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            IReadOnlyList<BotView> created = await LoadBotsAsync(
                transaction,
                actor,
                botId,
                cancellationToken).ConfigureAwait(false);
            if (created.Count == 0)
            {
                throw new InvalidOperationException("The bot was not created.");
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return created[0];
        }
    }

    public async Task<BotView?> SetBotStatusAsync(
        UserActor actor,
        Guid botId,
        BotStatusChange request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string status = FormatBotStatus(request.Status);

        TenantPostgresTransaction transaction = await BeginAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            int affected;
            await using (NpgsqlCommand update = transaction.CreateCommand(
                """
                update bots.bots as bot
                set status = @status,
                    updated_at = clock_timestamp()
                where bot.tenant_id = @tenant_id
                  and bot.user_id = @user_id
                  and bot.id = @bot_id
                """))
            {
                AddUuid(update, "tenant_id", actor.TenantId);
                AddUuid(update, "user_id", actor.UserId);
                AddUuid(update, "bot_id", botId);
                AddText(update, "status", status);
                affected = await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            if (affected == 0)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            IReadOnlyList<BotView> bots = await LoadBotsAsync(
                transaction,
                actor,
                botId,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return bots.Count == 0 ? null : bots[0];
        }
    }

    public async Task<BotUptimeProjection> GetBotUptimeAsync(
        UserActor actor,
        int days,
        CancellationToken cancellationToken)
    {
        int clampedDays = days <= 0 ? UptimeDaysDefault : Math.Min(days, UptimeDaysMaximum);

        TenantPostgresTransaction transaction = await BeginAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                select
                    sample.ordinal,
                    sample.sampled_on,
                    sample.uptime_ratio,
                    sample.downtime_minutes
                from bots.uptime_samples as sample
                where sample.tenant_id = @tenant_id
                  and sample.user_id = @user_id
                order by sample.sampled_on desc, sample.ordinal desc
                limit @limit
                """);
            AddUuid(command, "tenant_id", actor.TenantId);
            AddUuid(command, "user_id", actor.UserId);
            AddInteger(command, "limit", clampedDays);

            var samples = new List<BotUptimeSample>();
            long totalDowntimeMinutes = 0;
            await using (NpgsqlDataReader reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    int downtimeMinutes = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
                    totalDowntimeMinutes += downtimeMinutes;
                    samples.Add(new BotUptimeSample(
                        reader.GetInt32(0),
                        reader.GetFieldValue<DateOnly>(1),
                        reader.IsDBNull(2) ? 0m : reader.GetDecimal(2),
                        downtimeMinutes));
                }
            }

            samples.Reverse();
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new BotUptimeProjection(
                clampedDays,
                ToCount(totalDowntimeMinutes),
                samples.AsReadOnly());
        }
    }

    public async Task<BotSettingsView?> GetBotSettingsAsync(
        UserActor actor,
        Guid botId,
        CancellationToken cancellationToken)
    {
        TenantPostgresTransaction transaction = await BeginAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            Guid strategyId;
            string strategyName;
            string symbol;
            string timeframe;
            decimal volume;
            long magicNumber;
            await using (NpgsqlCommand command = transaction.CreateCommand(
                """
                select
                    bot.strategy_id,
                    strategy.name,
                    bot.symbol,
                    bot.timeframe,
                    bot.volume,
                    bot.magic_number
                from bots.bots as bot
                join catalog.strategies as strategy
                  on strategy.tenant_id = bot.tenant_id
                 and strategy.id = bot.strategy_id
                where bot.tenant_id = @tenant_id
                  and bot.user_id = @user_id
                  and bot.id = @bot_id
                """))
            {
                AddUuid(command, "tenant_id", actor.TenantId);
                AddUuid(command, "user_id", actor.UserId);
                AddUuid(command, "bot_id", botId);

                await using NpgsqlDataReader reader = await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    // The reader holds the connection until it is closed and PostgreSQL will
                    // not accept a commit through a busy one, so a bot that is not the
                    // caller's has to close this reader before the transaction is ended.
                    // Without it the "no such bot" path faults instead of answering, and the
                    // caller sees a server error where it should see a missing resource.
                    await reader.DisposeAsync().ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return null;
                }

                strategyId = reader.GetGuid(0);
                strategyName = reader.GetString(1);
                symbol = reader.GetString(2);

                // A setting the operator has never stated is stored null — every bot that
                // predates this projection has all three null — and the settings form is
                // given something it can render and the operator can correct. Nothing is
                // written back here: the row keeps recording that nobody has chosen, and
                // the substituted value only becomes stored once a save states it.
                timeframe = reader.IsDBNull(3) ? DefaultBotTimeframe : reader.GetString(3);
                volume = reader.IsDBNull(4) ? DefaultBotVolume : reader.GetDecimal(4);
                magicNumber = reader.IsDBNull(5) ? 0L : reader.GetInt64(5);
            }

            // The declared set is read live from the strategy rather than copied onto the
            // bot, so a re-import that corrects a declaration is reflected immediately and
            // the stored overrides keep meaning what they said.
            IReadOnlyList<StrategyInputView> declared = await LoadStrategyInputsAsync(
                transaction,
                actor,
                strategyId,
                cancellationToken).ConfigureAwait(false);

            var overrides = new List<BotInputValue>();
            await using (NpgsqlCommand command = transaction.CreateCommand(
                """
                select
                    saved.name,
                    saved.value
                from bots.bot_inputs as saved
                join bots.bots as bot
                  on bot.tenant_id = saved.tenant_id
                 and bot.id = saved.bot_id
                where saved.tenant_id = @tenant_id
                  and bot.user_id = @user_id
                  and saved.bot_id = @bot_id
                order by saved.name
                limit @limit
                """))
            {
                AddUuid(command, "tenant_id", actor.TenantId);
                AddUuid(command, "user_id", actor.UserId);
                AddUuid(command, "bot_id", botId);
                AddInteger(command, "limit", BotInputLimit);

                await using NpgsqlDataReader reader = await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    overrides.Add(new BotInputValue(reader.GetString(0), reader.GetString(1)));
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new BotSettingsView(
                botId,
                strategyId,
                strategyName,
                symbol,
                timeframe,
                volume,
                magicNumber,
                declared,
                overrides.AsReadOnly());
        }
    }

    public async Task<bool> UpdateBotSettingsAsync(
        UserActor actor,
        Guid botId,
        UpdateBotSettings request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string symbol = RequireBoundedText(request.Symbol, MaximumSymbolLength, "symbol");
        string timeframe = RequireBotTimeframe(request.Timeframe);
        decimal volume = RequireTradableVolume(request.Volume);
        long magicNumber = RequireMagicNumber(request.MagicNumber);
        IReadOnlyList<BotInputValue> submitted = request.Inputs ?? [];
        if (submitted.Count > BotInputLimit)
        {
            throw new DomainException(
                RequestInvalidCode,
                "The request carries more input parameters than a strategy can declare.");
        }

        TenantPostgresTransaction transaction = await BeginAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            // The update is the ownership check as well as the write: a bot that is not the
            // caller's matches no row and returns nothing, so nothing after this point can
            // touch another operator's bot. ExecuteScalar deliberately, not a reader: the
            // connection has to be free for the statements below and for the commit.
            object? scalar;
            await using (NpgsqlCommand update = transaction.CreateCommand(
                """
                update bots.bots as bot
                set symbol = @symbol,
                    timeframe = @timeframe,
                    volume = @volume,
                    magic_number = @magic_number,
                    updated_at = clock_timestamp()
                where bot.tenant_id = @tenant_id
                  and bot.user_id = @user_id
                  and bot.id = @bot_id
                returning bot.strategy_id
                """))
            {
                AddUuid(update, "tenant_id", actor.TenantId);
                AddUuid(update, "user_id", actor.UserId);
                AddUuid(update, "bot_id", botId);
                AddText(update, "symbol", symbol);
                AddText(update, "timeframe", timeframe);
                AddNumeric(update, "volume", volume);
                AddBigInteger(update, "magic_number", magicNumber);
                scalar = await update.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            }

            if (scalar is not Guid strategyId)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            IReadOnlyList<StrategyInputView> declared = await LoadStrategyInputsAsync(
                transaction,
                actor,
                strategyId,
                cancellationToken).ConfigureAwait(false);
            ReadOnlyCollection<BotInputValue> overrides = ResolveBotInputOverrides(
                declared,
                submitted);

            // The submitted set is the whole intended set, so the stored overrides are
            // replaced rather than merged: an input the operator returned to its declared
            // default has to stop being stored, and a merge would leave the old value
            // behind claiming the operator still wants it.
            await using (NpgsqlCommand delete = transaction.CreateCommand(
                """
                delete from bots.bot_inputs as saved
                using bots.bots as bot
                where saved.tenant_id = @tenant_id
                  and bot.tenant_id = @tenant_id
                  and bot.id = saved.bot_id
                  and bot.user_id = @user_id
                  and saved.bot_id = @bot_id
                """))
            {
                AddUuid(delete, "tenant_id", actor.TenantId);
                AddUuid(delete, "user_id", actor.UserId);
                AddUuid(delete, "bot_id", botId);
                await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            if (overrides.Count > 0)
            {
                await using NpgsqlCommand insert = transaction.CreateCommand(
                    """
                    insert into bots.bot_inputs (id, tenant_id, bot_id, name, value)
                    select entry.id, @tenant_id, bot.id, entry.name, entry.value
                    from unnest(@ids, @names, @values) as entry(id, name, value)
                    join bots.bots as bot
                      on bot.tenant_id = @tenant_id
                     and bot.id = @bot_id
                     and bot.user_id = @user_id
                    """);
                AddUuid(insert, "tenant_id", actor.TenantId);
                AddUuid(insert, "user_id", actor.UserId);
                AddUuid(insert, "bot_id", botId);
                AddUuidArray(
                    insert,
                    "ids",
                    overrides.Select(static _ => Guid.CreateVersion7()).ToArray());
                AddTextArray(insert, "names", overrides.Select(entry => entry.Name).ToArray());
                AddTextArray(insert, "values", overrides.Select(entry => entry.Value).ToArray());
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
    }

    public async Task<IReadOnlyList<BrokerSymbolView>> GetBrokerSymbolsAsync(
        UserActor actor,
        string? server,
        string? query,
        CancellationToken cancellationToken)
    {
        string? normalizedServer = NormalizeBrokerServer(server);
        string? search = NormalizeSearch(query);

        TenantPostgresTransaction transaction = await BeginAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var symbols = new List<BrokerSymbolView>();
            await using (NpgsqlCommand command = transaction.CreateCommand(
                """
                select
                    instrument.server,
                    instrument.symbol,
                    instrument.description,
                    instrument.digits,
                    instrument.volume_min,
                    instrument.volume_max,
                    instrument.volume_step,
                    instrument.path
                from bots.broker_symbols as instrument
                where instrument.tenant_id = @tenant_id
                  and (@server is null or instrument.server = @server)
                  and (
                      @search is null
                      or strpos(lower(instrument.symbol), @search) > 0
                      or strpos(lower(coalesce(instrument.description, '')), @search) > 0
                  )
                order by instrument.symbol, instrument.server
                limit @limit
                """))
            {
                AddUuid(command, "tenant_id", actor.TenantId);
                AddNullableText(command, "server", normalizedServer);
                AddNullableText(command, "search", search);
                AddInteger(command, "limit", BrokerSymbolLimit);

                await using NpgsqlDataReader reader = await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    symbols.Add(new BrokerSymbolView(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetInt32(3),
                        reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                        reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                        reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                        reader.IsDBNull(7) ? null : reader.GetString(7)));
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return symbols.AsReadOnly();
        }
    }

    public async Task<IReadOnlyList<BacktestView>> GetBacktestsAsync(
        UserActor actor,
        CancellationToken cancellationToken)
    {
        TenantPostgresTransaction transaction = await BeginAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                select
                    backtest.id,
                    backtest.strategy_id,
                    strategy.name,
                    backtest.period_start,
                    backtest.period_end,
                    backtest.net_profit_amount,
                    backtest.max_drawdown_percent,
                    backtest.profit_factor,
                    backtest.trade_count,
                    backtest.currency,
                    backtest.status,
                    backtest.created_at,
                    backtest.completed_at
                from simulation.backtests as backtest
                join catalog.strategies as strategy
                  on strategy.tenant_id = backtest.tenant_id
                 and strategy.id = backtest.strategy_id
                where backtest.tenant_id = @tenant_id
                  and backtest.user_id = @user_id
                order by backtest.created_at desc, backtest.id desc
                limit @limit
                """);
            AddUuid(command, "tenant_id", actor.TenantId);
            AddUuid(command, "user_id", actor.UserId);
            AddInteger(command, "limit", BacktestListLimit);

            var backtests = new List<BacktestView>();
            await using (NpgsqlDataReader reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    backtests.Add(new BacktestView(
                        reader.GetGuid(0),
                        reader.GetGuid(1),
                        reader.GetString(2),
                        reader.GetFieldValue<DateOnly>(3),
                        reader.GetFieldValue<DateOnly>(4),
                        reader.IsDBNull(5) ? 0m : reader.GetDecimal(5),
                        reader.IsDBNull(6) ? 0m : reader.GetDecimal(6),
                        reader.IsDBNull(7) ? 0m : reader.GetDecimal(7),
                        reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                        reader.IsDBNull(9) ? DefaultCurrency : reader.GetString(9),
                        ParseBacktestStatus(reader.GetString(10)),
                        reader.GetFieldValue<DateTimeOffset>(11),
                        reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12)));
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return backtests.AsReadOnly();
        }
    }

    public async Task<BacktestDetailView?> GetBacktestDetailAsync(
        UserActor actor,
        Guid backtestId,
        CancellationToken cancellationToken)
    {
        TenantPostgresTransaction transaction = await BeginAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            BacktestView summary;
            string symbol;
            string timeframe;
            string model;
            decimal? dataQualityPercent;
            string? dataQualitySource;
            string? failureReason;
            decimal? equityInitialDeposit;
            int? equitySampleCount;
            int? equityDecimationInterval;
            await using (NpgsqlCommand command = transaction.CreateCommand(
                """
                select
                    backtest.id,
                    backtest.strategy_id,
                    strategy.name,
                    backtest.period_start,
                    backtest.period_end,
                    backtest.net_profit_amount,
                    backtest.max_drawdown_percent,
                    backtest.profit_factor,
                    backtest.trade_count,
                    backtest.currency,
                    backtest.status,
                    backtest.created_at,
                    backtest.completed_at,
                    backtest.symbol,
                    backtest.timeframe,
                    backtest.model,
                    backtest.data_quality_percent,
                    backtest.data_quality_source,
                    backtest.failure_reason,
                    backtest.equity_initial_deposit,
                    backtest.equity_sample_count,
                    backtest.equity_decimation_interval
                from simulation.backtests as backtest
                join catalog.strategies as strategy
                  on strategy.tenant_id = backtest.tenant_id
                 and strategy.id = backtest.strategy_id
                where backtest.tenant_id = @tenant_id
                  and backtest.user_id = @user_id
                  and backtest.id = @backtest_id
                """))
            {
                AddUuid(command, "tenant_id", actor.TenantId);
                AddUuid(command, "user_id", actor.UserId);
                AddUuid(command, "backtest_id", backtestId);

                await using NpgsqlDataReader reader = await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    // The reader holds the connection until it is closed, and PostgreSQL will
                    // not accept a commit through a busy connection. Without this the "no such
                    // backtest" path throws instead of answering, so a request for a run the
                    // caller does not own comes back as a server fault rather than a 404.
                    await reader.DisposeAsync().ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return null;
                }

                summary = new BacktestView(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetFieldValue<DateOnly>(3),
                    reader.GetFieldValue<DateOnly>(4),
                    reader.IsDBNull(5) ? 0m : reader.GetDecimal(5),
                    reader.IsDBNull(6) ? 0m : reader.GetDecimal(6),
                    reader.IsDBNull(7) ? 0m : reader.GetDecimal(7),
                    reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                    reader.IsDBNull(9) ? DefaultCurrency : reader.GetString(9),
                    ParseBacktestStatus(reader.GetString(10)),
                    reader.GetFieldValue<DateTimeOffset>(11),
                    reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12));
                symbol = reader.IsDBNull(13) ? UnspecifiedMarker : reader.GetString(13);
                timeframe = reader.IsDBNull(14) ? UnspecifiedMarker : reader.GetString(14);
                model = reader.IsDBNull(15) ? UnspecifiedMarker : reader.GetString(15);
                dataQualityPercent = reader.IsDBNull(16) ? null : reader.GetDecimal(16);
                dataQualitySource = reader.IsDBNull(17) ? null : reader.GetString(17);
                failureReason = reader.IsDBNull(18) ? null : reader.GetString(18);
                equityInitialDeposit = reader.IsDBNull(19) ? null : reader.GetDecimal(19);
                equitySampleCount = reader.IsDBNull(20) ? null : reader.GetInt32(20);
                equityDecimationInterval = reader.IsDBNull(21) ? null : reader.GetInt32(21);
            }

            var inputs = new List<BacktestInputValue>();
            await using (NpgsqlCommand command = transaction.CreateCommand(
                """
                select
                    supplied.name,
                    supplied.value
                from simulation.backtest_inputs as supplied
                join simulation.backtests as backtest
                  on backtest.tenant_id = supplied.tenant_id
                 and backtest.id = supplied.backtest_id
                where supplied.tenant_id = @tenant_id
                  and backtest.user_id = @user_id
                  and supplied.backtest_id = @backtest_id
                order by supplied.name
                limit @limit
                """))
            {
                AddUuid(command, "tenant_id", actor.TenantId);
                AddUuid(command, "user_id", actor.UserId);
                AddUuid(command, "backtest_id", backtestId);
                AddInteger(command, "limit", BacktestInputLimit);

                await using NpgsqlDataReader reader = await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    inputs.Add(new BacktestInputValue(reader.GetString(0), reader.GetString(1)));
                }
            }

            // The stored equity curve. The header on the request row states how long the
            // untouched series was and what stride was kept, so this read never has to
            // infer either from the rows it gets back. A request whose header is absent
            // has no curve at all, and no query is issued for one.
            var points = new List<BacktestEquityPoint>();
            if (equityInitialDeposit is not null
                && equitySampleCount is not null
                && equityDecimationInterval is not null)
            {
                await using NpgsqlCommand command = transaction.CreateCommand(
                    """
                    select
                        point.ordinal,
                        point.source_ordinal,
                        point.equity
                    from simulation.backtest_equity_points as point
                    join simulation.backtests as backtest
                      on backtest.tenant_id = point.tenant_id
                     and backtest.id = point.backtest_id
                    where point.tenant_id = @tenant_id
                      and backtest.user_id = @user_id
                      and point.backtest_id = @backtest_id
                    order by point.ordinal
                    limit @limit
                    """);
                AddUuid(command, "tenant_id", actor.TenantId);
                AddUuid(command, "user_id", actor.UserId);
                AddUuid(command, "backtest_id", backtestId);
                AddInteger(command, "limit", BacktestEquityPointLimit);

                await using NpgsqlDataReader reader = await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    points.Add(new BacktestEquityPoint(
                        reader.GetInt32(0),
                        reader.GetInt32(1),
                        reader.GetDecimal(2)));
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new BacktestDetailView(
                summary,
                symbol,
                timeframe,
                model,
                dataQualityPercent,
                dataQualitySource,
                failureReason,
                inputs.AsReadOnly(),
                points.Count == 0
                    ? null
                    : new BacktestEquityCurveView(
                        equityInitialDeposit!.Value,
                        equitySampleCount!.Value,
                        equityDecimationInterval!.Value,
                        points.AsReadOnly()));
        }
    }

    public async Task<BacktestView> CreateBacktestAsync(
        UserActor actor,
        CreateBacktest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.StrategyId == Guid.Empty)
        {
            throw new DomainException(RequestInvalidCode, "The strategy identifier is required.");
        }

        if (request.PeriodEnd < request.PeriodStart)
        {
            throw new DomainException(
                RequestInvalidCode,
                "The backtest period end must not precede the period start.");
        }

        string symbol = RequireBoundedText(request.Symbol, MaximumSymbolLength, "symbol");
        string timeframe = RequireBoundedText(
            request.Timeframe,
            MaximumTimeframeLength,
            "timeframe");
        string model = RequireBacktestModel(request.Model);
        IReadOnlyList<BacktestInputValue> submitted = request.Inputs ?? [];
        if (submitted.Count > BacktestInputLimit)
        {
            throw new DomainException(
                RequestInvalidCode,
                "The request carries more input parameters than a strategy can declare.");
        }

        TenantPostgresTransaction transaction = await BeginAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            string strategyName = await RequireStrategyAsync(
                transaction,
                actor,
                request.StrategyId,
                cancellationToken).ConfigureAwait(false);

            IReadOnlyList<StrategyInputView> declared = await LoadStrategyInputsAsync(
                transaction,
                actor,
                request.StrategyId,
                cancellationToken).ConfigureAwait(false);
            ReadOnlyCollection<BacktestInputValue> resolved = ResolveBacktestInputs(declared, submitted);

            Guid backtestId = Guid.CreateVersion7();
            DateTimeOffset createdAt;
            await using (NpgsqlCommand insert = transaction.CreateCommand(
                """
                insert into simulation.backtests
                (
                    id, tenant_id, user_id, strategy_id, period_start, period_end,
                    net_profit_amount, max_drawdown_percent, profit_factor, trade_count,
                    currency, status, created_at,
                    symbol, timeframe, model, requested_at
                )
                values
                (
                    @id, @tenant_id, @user_id, @strategy_id, @period_start, @period_end,
                    0, 0, 0, 0,
                    'USD', 'QUEUED', clock_timestamp(),
                    @symbol, @timeframe, @model, clock_timestamp()
                )
                returning created_at
                """))
            {
                AddUuid(insert, "id", backtestId);
                AddUuid(insert, "tenant_id", actor.TenantId);
                AddUuid(insert, "user_id", actor.UserId);
                AddUuid(insert, "strategy_id", request.StrategyId);
                AddDate(insert, "period_start", request.PeriodStart);
                AddDate(insert, "period_end", request.PeriodEnd);
                AddText(insert, "symbol", symbol);
                AddText(insert, "timeframe", timeframe);
                AddText(insert, "model", model);

                await using NpgsqlDataReader reader = await insert
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("The backtest was not created.");
                }

                createdAt = reader.GetFieldValue<DateTimeOffset>(0);
            }

            if (resolved.Count > 0)
            {
                await using NpgsqlCommand insert = transaction.CreateCommand(
                    """
                    insert into simulation.backtest_inputs (id, tenant_id, backtest_id, name, value)
                    select entry.id, @tenant_id, @backtest_id, entry.name, entry.value
                    from unnest(@ids, @names, @values) as entry(id, name, value)
                    """);
                AddUuid(insert, "tenant_id", actor.TenantId);
                AddUuid(insert, "backtest_id", backtestId);
                AddUuidArray(
                    insert,
                    "ids",
                    resolved.Select(static _ => Guid.CreateVersion7()).ToArray());
                AddTextArray(insert, "names", resolved.Select(entry => entry.Name).ToArray());
                AddTextArray(insert, "values", resolved.Select(entry => entry.Value).ToArray());
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new BacktestView(
                backtestId,
                request.StrategyId,
                strategyName,
                request.PeriodStart,
                request.PeriodEnd,
                0m,
                0m,
                0m,
                0,
                DefaultCurrency,
                BacktestStatus.Queued,
                createdAt,
                CompletedAt: null);
        }
    }

    public async Task<IReadOnlyList<CloudPlanView>> GetCloudPlansAsync(
        UserActor actor,
        CancellationToken cancellationToken)
    {
        TenantPostgresTransaction transaction = await BeginAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var features = new Dictionary<Guid, List<string>>();
            await using (NpgsqlCommand command = transaction.CreateCommand(
                """
                select feature.plan_id, feature.label
                from billing.cloud_plan_features as feature
                join billing.cloud_plans as plan
                  on plan.id = feature.plan_id
                where (plan.tenant_id is null or plan.tenant_id = @tenant_id)
                order by feature.plan_id, feature.ordinal
                limit @limit
                """))
            {
                AddUuid(command, "tenant_id", actor.TenantId);
                AddInteger(command, "limit", CloudPlanFeatureLimit);

                await using NpgsqlDataReader reader = await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    Guid planId = reader.GetGuid(0);
                    if (!features.TryGetValue(planId, out List<string>? labels))
                    {
                        labels = [];
                        features[planId] = labels;
                    }

                    labels.Add(reader.GetString(1));
                }
            }

            var plans = new List<CloudPlanView>();
            await using (NpgsqlCommand command = transaction.CreateCommand(
                """
                select
                    plan.id,
                    plan.code,
                    plan.name,
                    plan.tag,
                    plan.blurb,
                    plan.price_monthly_cents,
                    plan.price_yearly_cents,
                    plan.currency,
                    plan.unit,
                    plan.cta_label,
                    plan.highlighted
                from billing.cloud_plans as plan
                where (plan.tenant_id is null or plan.tenant_id = @tenant_id)
                order by plan.display_order, plan.code
                limit @limit
                """))
            {
                AddUuid(command, "tenant_id", actor.TenantId);
                AddInteger(command, "limit", CloudPlanLimit);

                await using NpgsqlDataReader reader = await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    Guid planId = reader.GetGuid(0);
                    plans.Add(new CloudPlanView(
                        planId,
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                        reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                        reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                        reader.IsDBNull(7) ? DefaultCurrency : reader.GetString(7),
                        reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                        reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                        !reader.IsDBNull(10) && reader.GetBoolean(10),
                        features.TryGetValue(planId, out List<string>? labels)
                            ? labels.AsReadOnly()
                            : []));
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return plans.AsReadOnly();
        }
    }

    public async Task<IReadOnlyList<CloudRunnerView>> GetCloudRunnersAsync(
        UserActor actor,
        CancellationToken cancellationToken)
    {
        TenantPostgresTransaction transaction = await BeginAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                select
                    runner.id,
                    runner.bot_id,
                    bot.name,
                    runner.region_code,
                    region.label,
                    runner.uptime_30d_percent,
                    runner.latency_ms,
                    runner.monthly_price_cents,
                    runner.currency,
                    runner.status,
                    runner.next_invoice_at
                from billing.cloud_runners as runner
                join bots.bots as bot
                  on bot.tenant_id = runner.tenant_id
                 and bot.user_id = runner.user_id
                 and bot.id = runner.bot_id
                join billing.cloud_regions as region
                  on region.code = runner.region_code
                where runner.tenant_id = @tenant_id
                  and runner.user_id = @user_id
                order by runner.created_at desc, runner.id desc
                limit @limit
                """);
            AddUuid(command, "tenant_id", actor.TenantId);
            AddUuid(command, "user_id", actor.UserId);
            AddInteger(command, "limit", CloudRunnerLimit);

            var runners = new List<CloudRunnerView>();
            await using (NpgsqlDataReader reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    runners.Add(new CloudRunnerView(
                        reader.GetGuid(0),
                        reader.GetGuid(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.IsDBNull(4) ? reader.GetString(3) : reader.GetString(4),
                        reader.IsDBNull(5) ? 0m : reader.GetDecimal(5),
                        reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                        reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                        reader.IsDBNull(8) ? DefaultCurrency : reader.GetString(8),
                        ParseCloudRunnerStatus(reader.GetString(9)),
                        reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10)));
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return runners.AsReadOnly();
        }
    }

    public async Task<IReadOnlyList<CloudRegionView>> GetCloudRegionsAsync(
        UserActor actor,
        CancellationToken cancellationToken)
    {
        TenantPostgresTransaction transaction = await BeginAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            // billing.cloud_regions is a catalogue-wide table with no tenant column, so the read is
            // gated on the authenticated tenant still being active instead of on a tenant_id filter.
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                select region.code, region.label
                from billing.cloud_regions as region
                where exists (
                    select 1
                    from identity.tenants as tenant
                    where tenant.id = @tenant_id
                      and tenant.state = 'active'
                )
                order by region.display_order, region.code
                limit @limit
                """);
            AddUuid(command, "tenant_id", actor.TenantId);
            AddInteger(command, "limit", CloudRegionLimit);

            var regions = new List<CloudRegionView>();
            await using (NpgsqlDataReader reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    regions.Add(new CloudRegionView(
                        reader.GetString(0),
                        reader.IsDBNull(1) ? reader.GetString(0) : reader.GetString(1)));
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return regions.AsReadOnly();
        }
    }

    public async Task<JournalPage> GetJournalAsync(
        UserActor actor,
        JournalQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        int limit = query.Limit <= 0
            ? JournalLimitDefault
            : Math.Min(query.Limit, JournalLimitMaximum);

        TenantPostgresTransaction transaction = await BeginAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            DateTimeOffset? cursorOpenedAt = null;
            if (query.Before is Guid before)
            {
                DateTimeOffset? anchor = null;
                await using (NpgsqlCommand cursor = transaction.CreateCommand(
                    """
                    select trade.opened_at
                    from journal.trades as trade
                    where trade.tenant_id = @tenant_id
                      and trade.user_id = @user_id
                      and trade.id = @before
                    """))
                {
                    AddUuid(cursor, "tenant_id", actor.TenantId);
                    AddUuid(cursor, "user_id", actor.UserId);
                    AddUuid(cursor, "before", before);

                    // Read through the reader rather than ExecuteScalar: Npgsql boxes a
                    // timestamptz scalar as DateTime, so a DateTimeOffset type test would
                    // always fail and silently truncate every cursor page to empty.
                    await using NpgsqlDataReader reader = await cursor
                        .ExecuteReaderAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        anchor = reader.GetFieldValue<DateTimeOffset>(0);
                    }
                }

                if (anchor is not DateTimeOffset openedAt)
                {
                    // An unknown cursor cannot be positioned safely; report an empty page rather
                    // than silently restarting from the newest trade.
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return new JournalPage([], NextCursor: null);
                }

                cursorOpenedAt = openedAt;
            }

            var items = new List<JournalEntryView>();
            await using (NpgsqlCommand command = transaction.CreateCommand(
                """
                select
                    trade.id,
                    trade.bot_id,
                    bot.name,
                    trade.symbol,
                    trade.side,
                    trade.volume,
                    trade.entry_price,
                    trade.exit_price,
                    trade.result_amount,
                    trade.currency,
                    trade.opened_at,
                    trade.closed_at
                from journal.trades as trade
                left join bots.bots as bot
                  on bot.tenant_id = trade.tenant_id
                 and bot.user_id = trade.user_id
                 and bot.id = trade.bot_id
                where trade.tenant_id = @tenant_id
                  and trade.user_id = @user_id
                  and (@from is null or trade.opened_at >= @from)
                  and (@to is null or trade.opened_at < @to)
                  and (
                      @cursor_opened_at is null
                      or trade.opened_at < @cursor_opened_at
                      or (trade.opened_at = @cursor_opened_at and trade.id < @cursor_id)
                  )
                order by trade.opened_at desc, trade.id desc
                limit @limit
                """))
            {
                AddUuid(command, "tenant_id", actor.TenantId);
                AddUuid(command, "user_id", actor.UserId);
                AddTimestamp(command, "from", query.From);
                AddTimestamp(command, "to", query.To);
                AddTimestamp(command, "cursor_opened_at", cursorOpenedAt);
                AddNullableUuid(command, "cursor_id", query.Before);
                AddInteger(command, "limit", limit + 1);

                await using NpgsqlDataReader reader = await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    items.Add(new JournalEntryView(
                        reader.GetGuid(0),
                        reader.IsDBNull(1) ? null : reader.GetGuid(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.GetString(3),
                        ParseTradeSide(reader.GetString(4)),
                        reader.IsDBNull(5) ? 0m : reader.GetDecimal(5),
                        reader.IsDBNull(6) ? 0m : reader.GetDecimal(6),
                        reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                        reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                        reader.IsDBNull(9) ? DefaultCurrency : reader.GetString(9),
                        reader.GetFieldValue<DateTimeOffset>(10),
                        reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11)));
                }
            }

            Guid? nextCursor = null;
            if (items.Count > limit)
            {
                items.RemoveRange(limit, items.Count - limit);
                nextCursor = items[^1].Id;
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new JournalPage(items.AsReadOnly(), nextCursor);
        }
    }

    public async Task<DashboardSummaryView> GetDashboardSummaryAsync(
        UserActor actor,
        CancellationToken cancellationToken)
    {
        TenantPostgresTransaction transaction = await BeginAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            int totalBotCount;
            int liveBotCount;
            await using (NpgsqlCommand command = transaction.CreateCommand(
                """
                select
                    count(*),
                    count(*) filter (where bot.status = 'RUNNING')
                from bots.bots as bot
                where bot.tenant_id = @tenant_id
                  and bot.user_id = @user_id
                """))
            {
                AddUuid(command, "tenant_id", actor.TenantId);
                AddUuid(command, "user_id", actor.UserId);

                await using NpgsqlDataReader reader = await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    totalBotCount = ToCount(reader.GetInt64(0));
                    liveBotCount = ToCount(reader.GetInt64(1));
                }
                else
                {
                    totalBotCount = 0;
                    liveBotCount = 0;
                }
            }

            decimal todayProfitLoss = 0m;
            int todayTradeCount = 0;
            decimal sevenDayProfitLoss = 0m;
            int sevenDayTradeCount = 0;
            string currency = DefaultCurrency;
            await using (NpgsqlCommand command = transaction.CreateCommand(
                """
                select
                    metric.metric_window,
                    coalesce(sum(metric.pl_amount), 0),
                    coalesce(sum(metric.trade_count), 0),
                    max(metric.currency)
                from bots.bot_metrics as metric
                join bots.bots as bot
                  on bot.tenant_id = metric.tenant_id
                 and bot.id = metric.bot_id
                where metric.tenant_id = @tenant_id
                  and bot.user_id = @user_id
                  and metric.metric_window in ('TODAY', 'SEVEN_DAY')
                group by metric.metric_window
                """))
            {
                AddUuid(command, "tenant_id", actor.TenantId);
                AddUuid(command, "user_id", actor.UserId);

                await using NpgsqlDataReader reader = await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    string window = reader.GetString(0);
                    decimal profitLoss = reader.GetDecimal(1);
                    int tradeCount = ToCount(reader.GetInt64(2));
                    if (!reader.IsDBNull(3))
                    {
                        currency = reader.GetString(3);
                    }

                    if (string.Equals(window, "TODAY", StringComparison.Ordinal))
                    {
                        todayProfitLoss = profitLoss;
                        todayTradeCount = tradeCount;
                    }
                    else
                    {
                        sevenDayProfitLoss = profitLoss;
                        sevenDayTradeCount = tradeCount;
                    }
                }
            }

            int totalRunnerCount;
            int activeRunnerCount;
            await using (NpgsqlCommand command = transaction.CreateCommand(
                """
                select
                    count(*),
                    count(*) filter (where runner.status = 'ACTIVE')
                from billing.cloud_runners as runner
                where runner.tenant_id = @tenant_id
                  and runner.user_id = @user_id
                """))
            {
                AddUuid(command, "tenant_id", actor.TenantId);
                AddUuid(command, "user_id", actor.UserId);

                await using NpgsqlDataReader reader = await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    totalRunnerCount = ToCount(reader.GetInt64(0));
                    activeRunnerCount = ToCount(reader.GetInt64(1));
                }
                else
                {
                    totalRunnerCount = 0;
                    activeRunnerCount = 0;
                }
            }

            IReadOnlyList<BotView> bots = await LoadBotsAsync(
                transaction,
                actor,
                botId: null,
                cancellationToken).ConfigureAwait(false);
            var runningBots = new List<BotView>();
            foreach (BotView bot in bots)
            {
                if (bot.Status == BotStatus.Running)
                {
                    runningBots.Add(bot);
                }
            }

            var stats = new List<DashboardStatView>(4)
            {
                new(
                    "live-bots",
                    "Live bots",
                    liveBotCount.ToString(CultureInfo.InvariantCulture),
                    totalBotCount.ToString(CultureInfo.InvariantCulture) + " configured",
                    liveBotCount > 0 ? TrendDirection.Up : TrendDirection.Flat),
                new(
                    "pl-today",
                    "P/L today",
                    currency + " " + todayProfitLoss.ToString("0.00", CultureInfo.InvariantCulture),
                    currency
                        + " "
                        + sevenDayProfitLoss.ToString("0.00", CultureInfo.InvariantCulture)
                        + " 7d",
                    ResolveTrend(todayProfitLoss)),
                new(
                    "trades-today",
                    "Trades today",
                    todayTradeCount.ToString(CultureInfo.InvariantCulture),
                    sevenDayTradeCount.ToString(CultureInfo.InvariantCulture) + " 7d",
                    todayTradeCount > 0 ? TrendDirection.Up : TrendDirection.Flat),
                new(
                    "cloud-runners",
                    "Cloud runners",
                    activeRunnerCount.ToString(CultureInfo.InvariantCulture),
                    totalRunnerCount.ToString(CultureInfo.InvariantCulture) + " provisioned",
                    activeRunnerCount > 0 ? TrendDirection.Up : TrendDirection.Flat)
            };

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new DashboardSummaryView(
                stats.AsReadOnly(),
                runningBots.AsReadOnly(),
                liveBotCount,
                activeRunnerCount);
        }
    }

    public async Task<BridgeStatusView> GetBridgeStatusAsync(
        UserActor actor,
        CancellationToken cancellationToken)
    {
        TenantPostgresTransaction transaction = await BeginAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            bool connected;
            long elapsedMilliseconds;
            await using (NpgsqlCommand probe = transaction.CreateCommand("select 1"))
            {
                var roundTrip = Stopwatch.StartNew();
                object? scalar = await probe.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                roundTrip.Stop();
                elapsedMilliseconds = roundTrip.ElapsedMilliseconds;
                connected = scalar is int probeValue && probeValue == 1;
            }

            int ordersToday;
            int rejections;
            await using (NpgsqlCommand command = transaction.CreateCommand(
                """
                select
                    count(*),
                    count(*) filter (
                        where trade.closed_at is not null and trade.result_amount is null)
                from journal.trades as trade
                where trade.tenant_id = @tenant_id
                  and trade.user_id = @user_id
                  and trade.opened_at >= @day_start
                  and trade.opened_at < @day_end
                """))
            {
                DateTimeOffset dayStart = new(
                    DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc));
                AddUuid(command, "tenant_id", actor.TenantId);
                AddUuid(command, "user_id", actor.UserId);
                AddTimestamp(command, "day_start", dayStart);
                AddTimestamp(command, "day_end", dayStart.AddDays(1));

                await using NpgsqlDataReader reader = await command
                    .ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    ordersToday = ToCount(reader.GetInt64(0));
                    rejections = ToCount(reader.GetInt64(1));
                }
                else
                {
                    ordersToday = 0;
                    rejections = 0;
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new BridgeStatusView(
                connected,
                BridgeVersion,
                ToCount(elapsedMilliseconds),
                ordersToday,
                rejections);
        }
    }

    private async ValueTask<TenantPostgresTransaction> BeginAsync(
        UserActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!Enum.IsDefined(actor.Assurance))
        {
            throw new AuthorizationDeniedException(
                "AUTHENTICATION_ASSURANCE_INVALID",
                "The authentication assurance is not accepted.");
        }

        var executionContext = new TenantExecutionContext(
            actor.TenantId,
            actor.UserId,
            Guid.CreateVersion7(),
            actor.SessionId);
        TenantPostgresTransaction transaction = await database
            .BeginTenantTransactionAsync(executionContext, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                select
                    identity.security_state,
                    session.state,
                    tenant.state
                from identity.user_identities as identity
                join identity.tenants as tenant
                  on tenant.id = identity.tenant_id
                join identity.user_session_families as session
                  on session.tenant_id = identity.tenant_id
                 and session.user_id = identity.id
                where identity.tenant_id = @tenant_id
                  and identity.id = @user_id
                  and session.id = @session_id
                  and session.expires_at > clock_timestamp()
                """);
            AddUuid(command, "tenant_id", actor.TenantId);
            AddUuid(command, "user_id", actor.UserId);
            AddUuid(command, "session_id", actor.SessionId);

            await using NpgsqlDataReader reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                || !string.Equals(reader.GetString(0), "active", StringComparison.Ordinal)
                || !string.Equals(reader.GetString(1), "active", StringComparison.Ordinal)
                || !string.Equals(reader.GetString(2), "active", StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException("The authenticated user session is not active.");
            }

            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<IReadOnlyList<BotView>> LoadBotsAsync(
        TenantPostgresTransaction transaction,
        UserActor actor,
        Guid? botId,
        CancellationToken cancellationToken)
    {
        var metrics = new Dictionary<Guid, List<BotMetricView>>();
        await using (NpgsqlCommand command = transaction.CreateCommand(BotMetricProjection))
        {
            AddUuid(command, "tenant_id", actor.TenantId);
            AddUuid(command, "user_id", actor.UserId);
            AddNullableUuid(command, "bot_id", botId);
            AddInteger(command, "limit", BotMetricLimit);

            await using NpgsqlDataReader reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                Guid metricBotId = reader.GetGuid(0);
                if (!metrics.TryGetValue(metricBotId, out List<BotMetricView>? windows))
                {
                    windows = [];
                    metrics[metricBotId] = windows;
                }

                windows.Add(new BotMetricView(
                    reader.GetString(1),
                    reader.IsDBNull(2) ? 0m : reader.GetDecimal(2),
                    reader.IsDBNull(3) ? DefaultCurrency : reader.GetString(3),
                    reader.IsDBNull(4) ? 0 : reader.GetInt32(4)));
            }
        }

        var bots = new List<BotView>();
        await using (NpgsqlCommand command = transaction.CreateCommand(BotProjection))
        {
            AddUuid(command, "tenant_id", actor.TenantId);
            AddUuid(command, "user_id", actor.UserId);
            AddNullableUuid(command, "bot_id", botId);
            AddInteger(command, "limit", botId is null ? BotListLimit : 1);

            await using NpgsqlDataReader reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                Guid currentBotId = reader.GetGuid(0);
                bots.Add(new BotView(
                    currentBotId,
                    reader.GetString(1),
                    reader.GetGuid(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetGuid(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    ParseBotStatus(reader.GetString(8)),
                    ParseBotHost(reader.GetString(9)),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.IsDBNull(11) ? null : reader.GetString(11),
                    metrics.TryGetValue(currentBotId, out List<BotMetricView>? windows)
                        ? windows.AsReadOnly()
                        : [],
                    reader.GetFieldValue<DateTimeOffset>(12),
                    reader.GetFieldValue<DateTimeOffset>(13)));
            }
        }

        return bots.AsReadOnly();
    }

    private static async Task<string?> FindStrategyNameAsync(
        TenantPostgresTransaction transaction,
        UserActor actor,
        Guid strategyId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select strategy.name
            from catalog.strategies as strategy
            where strategy.tenant_id = @tenant_id
              and strategy.id = @strategy_id
            """);
        AddUuid(command, "tenant_id", actor.TenantId);
        AddUuid(command, "strategy_id", strategyId);
        object? scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return scalar as string;
    }

    /// <summary>
    /// Reads a strategy's declared MQL5 input parameters in source order, with the
    /// members of every enumeration the strategy itself declares. An input whose
    /// enumeration is not declared in the strategy carries an empty member list:
    /// its members are genuinely unknown and nothing is substituted for them.
    /// </summary>
    private static async Task<IReadOnlyList<StrategyInputView>> LoadStrategyInputsAsync(
        TenantPostgresTransaction transaction,
        UserActor actor,
        Guid strategyId,
        CancellationToken cancellationToken)
    {
        Guid declarationStrategyId = await ResolveInputDeclarationStrategyIdAsync(
            transaction,
            actor,
            strategyId,
            cancellationToken).ConfigureAwait(false);
        Dictionary<string, List<StrategyEnumMemberView>> members = await LoadStrategyEnumMembersAsync(
            transaction,
            actor,
            declarationStrategyId,
            cancellationToken).ConfigureAwait(false);

        var inputs = new List<StrategyInputView>();
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select
                declared.ordinal,
                declared.name,
                declared.label,
                declared.group_label,
                declared.declared_type,
                declared.value_kind,
                declared.default_value,
                declared.enum_type_name,
                declared.source_line
            from catalog.strategy_inputs as declared
            where declared.tenant_id = @tenant_id
              and declared.strategy_id = @strategy_id
            order by declared.ordinal, declared.name
            limit @limit
            """);
        AddUuid(command, "tenant_id", actor.TenantId);
        AddUuid(command, "strategy_id", declarationStrategyId);
        AddInteger(command, "limit", StrategyInputLimit);

        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string? enumTypeName = reader.IsDBNull(7) ? null : reader.GetString(7);
            IReadOnlyList<StrategyEnumMemberView> declaredMembers =
                enumTypeName is not null
                && members.TryGetValue(enumTypeName, out List<StrategyEnumMemberView>? candidates)
                    ? candidates.AsReadOnly()
                    : [];
            inputs.Add(new StrategyInputView(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                enumTypeName,
                declaredMembers,
                reader.GetInt32(8)));
        }

        return inputs.AsReadOnly();
    }

    /// <summary>
    /// A converted package normally owns a materialized copy of its source inputs. During the
    /// short interval before that copy exists, resolve the declaration from the closest earlier
    /// package generation with the same canonical filename. Direct declarations always win.
    /// </summary>
    private static async Task<Guid> ResolveInputDeclarationStrategyIdAsync(
        TenantPostgresTransaction transaction,
        UserActor actor,
        Guid strategyId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select
                case
                    when exists
                    (
                        select 1
                        from catalog.strategy_inputs as direct
                        where direct.tenant_id = target.tenant_id
                          and direct.strategy_id = target.id
                    )
                    then target.id
                    else coalesce
                    (
                        (
                            select source.id
                            from catalog.strategies as source
                            where source.tenant_id = target.tenant_id
                              and source.id <> target.id
                              and target.package_format_version >= 2
                              and coalesce(source.package_format_version, 1)
                                  < target.package_format_version
                              and regexp_replace(lower(source.name), '\.(mq5|yo4x)$', '')
                                  = regexp_replace(lower(target.name), '\.(mq5|yo4x)$', '')
                              and exists
                              (
                                  select 1
                                  from catalog.strategy_inputs as declared
                                  where declared.tenant_id = source.tenant_id
                                    and declared.strategy_id = source.id
                              )
                            order by
                                coalesce(source.package_format_version, 1) desc,
                                source.updated_at desc,
                                source.id desc
                            limit @limit
                        ),
                        target.id
                    )
                end
            from catalog.strategies as target
            where target.tenant_id = @tenant_id
              and target.id = @strategy_id
            """);
        AddUuid(command, "tenant_id", actor.TenantId);
        AddUuid(command, "strategy_id", strategyId);
        AddInteger(command, "limit", 1);
        object? scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return scalar is Guid resolved ? resolved : strategyId;
    }

    private static async Task<Dictionary<string, List<StrategyEnumMemberView>>> LoadStrategyEnumMembersAsync(
        TenantPostgresTransaction transaction,
        UserActor actor,
        Guid strategyId,
        CancellationToken cancellationToken)
    {
        var members = new Dictionary<string, List<StrategyEnumMemberView>>(StringComparer.Ordinal);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select
                member.enum_type_name,
                member.ordinal,
                member.member_name,
                member.member_value,
                member.label
            from catalog.strategy_enum_members as member
            where member.tenant_id = @tenant_id
              and member.strategy_id = @strategy_id
            order by member.enum_type_name, member.ordinal
            limit @limit
            """);
        AddUuid(command, "tenant_id", actor.TenantId);
        AddUuid(command, "strategy_id", strategyId);
        AddInteger(command, "limit", StrategyEnumMemberLimit);

        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string enumTypeName = reader.GetString(0);
            if (!members.TryGetValue(enumTypeName, out List<StrategyEnumMemberView>? bucket))
            {
                bucket = [];
                members[enumTypeName] = bucket;
            }

            bucket.Add(new StrategyEnumMemberView(
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetInt64(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return members;
    }

    /// <summary>
    /// Accepts a volume the bot could actually trade and that the column can hold
    /// without changing it. A value carrying more than two decimal places is refused
    /// rather than stored, because <c>numeric(12,2)</c> would round it and the bot
    /// would then be running a size the operator never asked for.
    /// </summary>
    private static decimal RequireTradableVolume(decimal value)
    {
        if (value <= 0m)
        {
            throw new DomainException(
                RequestInvalidCode,
                "The volume must be greater than zero.");
        }

        if (value > MaximumBotVolume)
        {
            throw new DomainException(RequestInvalidCode, "The volume is too large.");
        }

        return decimal.Round(value, 2) == value
            ? value
            : throw new DomainException(
                RequestInvalidCode,
                "The volume must not carry more than two decimal places.");
    }

    /// <summary>
    /// Accepts one of MetaTrader's twenty-one chart periods and nothing else. The set is
    /// closed on the server, so a saved timeframe is always a period the platform names
    /// and a later read cannot hand the front end something it has no chart for.
    /// </summary>
    private static string RequireBotTimeframe(string? value)
    {
        string trimmed = RequireBoundedText(value, MaximumTimeframeLength, "timeframe");
        return Array.Exists(
            BotTimeframes,
            candidate => string.Equals(candidate, trimmed, StringComparison.Ordinal))
            ? trimmed
            : throw new DomainException(
                RequestInvalidCode,
                "The timeframe must be one of " + string.Join(", ", BotTimeframes) + ".");
    }

    private static long RequireMagicNumber(long value) => value >= 0L
        ? value
        : throw new DomainException(
            RequestInvalidCode,
            "The magic number must not be negative.");

    private static string? NormalizeBrokerServer(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        return trimmed.Length > MaximumBrokerServerLength
            ? throw new DomainException(
                RequestInvalidCode,
                "The broker server name is too long.")
            : trimmed;
    }

    /// <summary>
    /// Validates every submitted input against the strategy's own declarations and
    /// returns only the values that differ from what the source declares. A submitted
    /// name the strategy does not declare, a duplicate, or a value that does not parse
    /// for its declared kind is refused and nothing is written; nothing is coerced.
    /// <para>
    /// Unlike a backtest, which records the complete resolved set so a run is
    /// reproducible from its rows alone, a bot records only the overrides. An input
    /// whose submitted value is byte for byte the declared default is therefore
    /// dropped rather than stored: it is not an override, and storing it would freeze
    /// a default the operator never chose against a later corrected import.
    /// </para>
    /// </summary>
    private static ReadOnlyCollection<BotInputValue> ResolveBotInputOverrides(
        IReadOnlyList<StrategyInputView> declared,
        IReadOnlyList<BotInputValue> submitted)
    {
        var declarations = new Dictionary<string, StrategyInputView>(StringComparer.Ordinal);
        foreach (StrategyInputView input in declared)
        {
            declarations[input.Name] = input;
        }

        var accepted = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (BotInputValue entry in submitted)
        {
            if (entry is null)
            {
                throw new DomainException(BotInputInvalidCode, "An input entry was empty.");
            }

            string name = entry.Name ?? string.Empty;
            if (!declarations.TryGetValue(name, out StrategyInputView? declaration))
            {
                throw new DomainException(
                    BotInputInvalidCode,
                    "The strategy does not declare an input named " + name + ".");
            }

            string value = entry.Value ?? string.Empty;
            if (value.Length > MaximumInputValueLength)
            {
                throw new DomainException(
                    BotInputInvalidCode,
                    "The value of " + name + " exceeds the maximum recorded length.");
            }

            BacktestInputError? failure = ValidateInputValue(declaration, value);
            if (failure is not null)
            {
                // The per-field checker is shared with the backtest request path: the
                // question "is this value acceptable for the kind this input declares"
                // is the same one on both sides. Only its message needs the field name
                // added, because a settings save reports one refusal rather than a list.
                throw new DomainException(
                    BotInputInvalidCode,
                    "The value of " + failure.Name + " was refused. " + failure.Message);
            }

            if (!accepted.TryAdd(name, value))
            {
                throw new DomainException(
                    BotInputInvalidCode,
                    "The input " + name + " was supplied more than once.");
            }
        }

        var overrides = new List<BotInputValue>(accepted.Count);
        foreach (StrategyInputView input in declared)
        {
            if (accepted.TryGetValue(input.Name, out string? value)
                && !string.Equals(value, input.DefaultValue, StringComparison.Ordinal))
            {
                overrides.Add(new BotInputValue(input.Name, value));
            }
        }

        return overrides.AsReadOnly();
    }

    private static string RequireBacktestModel(string? value)
    {
        string trimmed = RequireBoundedText(value, MaximumNameLength, "modelling mode");
        return Array.Exists(
            BacktestModels,
            candidate => string.Equals(candidate, trimmed, StringComparison.Ordinal))
            ? trimmed
            : throw new DomainException(
                RequestInvalidCode,
                "The modelling mode must be one of EVERY_TICK_REAL, EVERY_TICK_M1, OHLC_M1 or OPEN_PRICES.");
    }

    /// <summary>
    /// Validates every submitted input against the strategy's own declarations and
    /// returns the complete set of values the run will use. A submitted name that
    /// the strategy does not declare, a duplicate, or a value that does not parse
    /// for its declared kind is reported per field and the whole request is
    /// rejected; nothing is ever coerced. Inputs the caller did not submit take
    /// the default the strategy source declares, so the recorded set is complete
    /// and the run is reproducible from the row alone.
    /// </summary>
    private static ReadOnlyCollection<BacktestInputValue> ResolveBacktestInputs(
        IReadOnlyList<StrategyInputView> declared,
        IReadOnlyList<BacktestInputValue> submitted)
    {
        var declarations = new Dictionary<string, StrategyInputView>(StringComparer.Ordinal);
        foreach (StrategyInputView input in declared)
        {
            declarations[input.Name] = input;
        }

        var errors = new List<BacktestInputError>();
        var accepted = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (BacktestInputValue entry in submitted)
        {
            if (entry is null)
            {
                errors.Add(new BacktestInputError(
                    string.Empty,
                    "INPUT_MISSING",
                    "An input entry was empty."));
                continue;
            }

            string name = entry.Name ?? string.Empty;
            if (!declarations.TryGetValue(name, out StrategyInputView? declaration))
            {
                errors.Add(new BacktestInputError(
                    name,
                    "INPUT_NOT_DECLARED",
                    "The strategy does not declare an input with this name."));
                continue;
            }

            if (!accepted.TryAdd(name, string.Empty))
            {
                errors.Add(new BacktestInputError(
                    name,
                    "INPUT_DUPLICATED",
                    "The input was supplied more than once."));
                continue;
            }

            string value = entry.Value ?? string.Empty;
            if (value.Length > MaximumInputValueLength)
            {
                errors.Add(new BacktestInputError(
                    name,
                    "VALUE_TOO_LONG",
                    "The value exceeds the maximum recorded length."));
                continue;
            }

            BacktestInputError? failure = ValidateInputValue(declaration, value);
            if (failure is not null)
            {
                errors.Add(failure);
                continue;
            }

            accepted[name] = value;
        }

        if (errors.Count > 0)
        {
            throw new BacktestInputValidationException(errors.AsReadOnly());
        }

        var resolved = new List<BacktestInputValue>(declared.Count);
        foreach (StrategyInputView input in declared)
        {
            resolved.Add(new BacktestInputValue(
                input.Name,
                accepted.TryGetValue(input.Name, out string? supplied)
                    ? supplied
                    : input.DefaultValue));
        }

        return resolved.AsReadOnly();
    }

    /// <summary>
    /// Checks one value against the kind its declaration carries. Returns null when
    /// the value is acceptable and a single field error otherwise. No value is
    /// rewritten, rounded, widened or otherwise adjusted here.
    /// </summary>
    private static BacktestInputError? ValidateInputValue(StrategyInputView declaration, string value)
    {
        // The strategy's own declared default is always acceptable, byte for byte.
        // Some sources write a symbolic constant, or a literal the language accepts
        // but this checker cannot re-derive; refusing the value the source itself
        // states would reject the unmodified form of the request.
        if (string.Equals(value, declaration.DefaultValue, StringComparison.Ordinal))
        {
            return null;
        }

        switch (declaration.ValueKind)
        {
            case "WHOLE":
                return long.TryParse(
                    value.Trim(),
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out _)
                    ? null
                    : new BacktestInputError(
                        declaration.Name,
                        "VALUE_NOT_A_WHOLE_NUMBER",
                        "The value must be a 64-bit whole number.");
            case "REAL":
                return double.TryParse(
                    value.Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double real) && double.IsFinite(real)
                    ? null
                    : new BacktestInputError(
                        declaration.Name,
                        "VALUE_NOT_A_REAL_NUMBER",
                        "The value must be a finite real number.");
            case "LOGICAL":
                return bool.TryParse(value.Trim(), out _)
                    ? null
                    : new BacktestInputError(
                        declaration.Name,
                        "VALUE_NOT_LOGICAL",
                        "The value must be true or false.");
            case "MOMENT":
                return IsMoment(value.Trim())
                    ? null
                    : new BacktestInputError(
                        declaration.Name,
                        "VALUE_NOT_A_MOMENT",
                        "The value must be a date, optionally with a time of day.");
            case "COLOUR":
                return IsColour(value.Trim())
                    ? null
                    : new BacktestInputError(
                        declaration.Name,
                        "VALUE_NOT_A_COLOUR",
                        "The value must be a named colour, a C'r,g,b' triplet or an integer.");
            case "ENUM":
                if (declaration.EnumMembers.Count == 0)
                {
                    return new BacktestInputError(
                        declaration.Name,
                        "ENUM_MEMBERS_NOT_DECLARED",
                        "The strategy does not declare the members of "
                            + (declaration.EnumTypeName ?? "this enumeration")
                            + ", so a submitted value cannot be verified.");
                }

                return declaration.EnumMembers.Any(member =>
                    string.Equals(member.Name, value.Trim(), StringComparison.Ordinal)
                    || string.Equals(
                        member.Value.ToString(CultureInfo.InvariantCulture),
                        value.Trim(),
                        StringComparison.Ordinal))
                    ? null
                    : new BacktestInputError(
                        declaration.Name,
                        "VALUE_NOT_A_DECLARED_MEMBER",
                        "The value is not a member the strategy declares for this enumeration.");
            case "TEXT":
                return null;
            default:
                return new BacktestInputError(
                    declaration.Name,
                    "VALUE_KIND_UNKNOWN",
                    "The declared kind of this input is not recognised, so no value can be verified.");
        }
    }

    /// <summary>
    /// Accepts MQL5's <c>D'yyyy.MM.dd HH:mm:ss'</c> literal, the same text without
    /// the literal markers, an ISO calendar date or date and time, and the plain
    /// second count MQL5's <c>datetime</c> is stored as.
    /// </summary>
    private static bool IsMoment(string value)
    {
        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return true;
        }

        string candidate = value;
        if (candidate.Length > 2
            && (candidate[0] == 'D' || candidate[0] == 'd')
            && candidate[1] == '\''
            && candidate[^1] == '\'')
        {
            candidate = candidate[2..^1].Trim();
        }

        string[] formats =
        [
            "yyyy.MM.dd",
            "yyyy.MM.dd HH:mm",
            "yyyy.MM.dd HH:mm:ss",
            "yyyy-MM-dd",
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm",
            "yyyy-MM-ddTHH:mm:ss"
        ];

        return DateTime.TryParseExact(
            candidate,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _);
    }

    /// <summary>
    /// Accepts a named colour constant (<c>clrRed</c>, and the legacy bare
    /// <c>Red</c> the corpus also uses), a <c>C'r,g,b'</c> triplet, and a packed
    /// integer written in decimal or as <c>0x</c> hexadecimal.
    /// </summary>
    private static bool IsColour(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        if (value.Length > 3
            && (value[0] == 'C' || value[0] == 'c')
            && value[1] == '\''
            && value[^1] == '\'')
        {
            string[] channels = value[2..^1].Split(',');
            return channels.Length == 3
                && channels.All(static channel => byte.TryParse(
                    channel.Trim(),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out _));
        }

        if (char.IsAsciiLetter(value[0]) || value[0] == '_')
        {
            return value.Length <= 64
                && value.All(static character => char.IsAsciiLetterOrDigit(character)
                    || character == '_');
        }

        if (value.StartsWith("0x", StringComparison.Ordinal)
            || value.StartsWith("0X", StringComparison.Ordinal))
        {
            return value.Length is > 2 and <= 10
                && value.Skip(2).All(static character => char.IsAsciiHexDigit(character));
        }

        return uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }

    private static async Task<string> RequireStrategyAsync(
        TenantPostgresTransaction transaction,
        UserActor actor,
        Guid strategyId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select strategy.name
            from catalog.strategies as strategy
            where strategy.tenant_id = @tenant_id
              and strategy.id = @strategy_id
            """);
        AddUuid(command, "tenant_id", actor.TenantId);
        AddUuid(command, "strategy_id", strategyId);
        object? scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return scalar as string ?? throw new ResourceNotFoundException();
    }

    private static async Task<IReadOnlyList<string>> ReadFacetAsync(
        TenantPostgresTransaction transaction,
        UserActor actor,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(commandText);
        AddUuid(command, "tenant_id", actor.TenantId);
        AddInteger(command, "limit", CatalogFacetLimit);

        var values = new List<string>();
        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!reader.IsDBNull(0))
            {
                values.Add(reader.GetString(0));
            }
        }

        return values.AsReadOnly();
    }

    private static StrategyCatalogItem ReadCatalogItem(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetString(6),
        reader.GetString(7),
        reader.GetString(8),
        reader.IsDBNull(9) ? 0m : reader.GetDecimal(9),
        reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
        reader.IsDBNull(11) ? 0 : reader.GetInt32(11),
        !reader.IsDBNull(12) && reader.GetBoolean(12),
        reader.IsDBNull(13) ? 0 : reader.GetInt32(13),
        reader.IsDBNull(14) ? 0 : reader.GetInt32(14),
        reader.IsDBNull(15) ? DefaultCurrency : reader.GetString(15),
        reader.GetFieldValue<DateTimeOffset>(16));

    private static int ClampPageSize(int pageSize) => pageSize <= 0
        ? CatalogPageSizeDefault
        : Math.Min(pageSize, CatalogPageSizeMaximum);

    private static int ResolveOffset(int page, int pageSize)
    {
        long offset = (long)(page - 1) * pageSize;
        return offset >= int.MaxValue ? int.MaxValue : (int)offset;
    }

    private static string ResolveCatalogOrder(string? sort) => sort switch
    {
        "TOP_RATED" =>
            "order by strategy.rating_average desc, strategy.rating_count desc, strategy.id desc",
        "RECENT" => "order by strategy.updated_at desc, strategy.id desc",
        "NAME" => "order by strategy.name asc, strategy.id asc",
        _ => "order by strategy.active_users desc, strategy.rating_average desc, strategy.id desc"
    };

    private static string? NormalizeFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        return trimmed.Length > MaximumNameLength ? trimmed[..MaximumNameLength] : trimmed;
    }

    private static string? NormalizeSearch(string? value) =>
        NormalizeFilter(value)?.ToLowerInvariant();

    private static string RequireBoundedText(string? value, int maximumLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException(RequestInvalidCode, "The " + field + " is required.");
        }

        string trimmed = value.Trim();
        if (trimmed.Length > maximumLength)
        {
            throw new DomainException(RequestInvalidCode, "The " + field + " is too long.");
        }

        return trimmed;
    }

    private static TrendDirection ResolveTrend(decimal value) => value switch
    {
        > 0m => TrendDirection.Up,
        < 0m => TrendDirection.Down,
        _ => TrendDirection.Flat
    };

    private static int ToCount(object? scalar) => scalar is long value ? ToCount(value) : 0;

    private static int ToCount(long value) =>
        value <= 0 ? 0 : (int)Math.Min(value, int.MaxValue);

    private static string ReadAssemblyVersion()
    {
        string? informational = typeof(PostgresFrontendProjections).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return "0.0.0";
        }

        int metadataIndex = informational.IndexOf('+', StringComparison.Ordinal);
        return metadataIndex < 0 ? informational : informational[..metadataIndex];
    }

    private static BotStatus ParseBotStatus(string value) => value switch
    {
        "DRAFT" => BotStatus.Draft,
        "STARTING" => BotStatus.Starting,
        "RUNNING" => BotStatus.Running,
        "PAUSED" => BotStatus.Paused,
        "STOPPED" => BotStatus.Stopped,
        "FAULTED" => BotStatus.Faulted,
        _ => throw new InvalidOperationException("An unknown bot status is persisted.")
    };

    private static string FormatBotStatus(BotStatus value) => value switch
    {
        BotStatus.Draft => "DRAFT",
        BotStatus.Starting => "STARTING",
        BotStatus.Running => "RUNNING",
        BotStatus.Paused => "PAUSED",
        BotStatus.Stopped => "STOPPED",
        BotStatus.Faulted => "FAULTED",
        _ => throw new DomainException(RequestInvalidCode, "The bot status is not accepted.")
    };

    private static BotHost ParseBotHost(string value) => value switch
    {
        "LOCAL" => BotHost.Local,
        "CLOUD" => BotHost.Cloud,
        _ => throw new InvalidOperationException("An unknown bot host is persisted.")
    };

    private static string FormatBotHost(BotHost value) => value switch
    {
        BotHost.Local => "LOCAL",
        BotHost.Cloud => "CLOUD",
        _ => throw new DomainException(RequestInvalidCode, "The bot host is not accepted.")
    };

    private static BacktestStatus ParseBacktestStatus(string value) => value switch
    {
        "QUEUED" => BacktestStatus.Queued,
        "RUNNING" => BacktestStatus.Running,
        "COMPLETE" => BacktestStatus.Complete,
        "FAILED" => BacktestStatus.Failed,
        _ => throw new InvalidOperationException("An unknown backtest status is persisted.")
    };

    private static CloudRunnerStatus ParseCloudRunnerStatus(string value) => value switch
    {
        "PROVISIONING" => CloudRunnerStatus.Provisioning,
        "ACTIVE" => CloudRunnerStatus.Active,
        "SUSPENDED" => CloudRunnerStatus.Suspended,
        "CANCELLED" => CloudRunnerStatus.Cancelled,
        _ => throw new InvalidOperationException("An unknown cloud runner status is persisted.")
    };

    private static TradeSide ParseTradeSide(string value) => value switch
    {
        "BUY" => TradeSide.Buy,
        "SELL" => TradeSide.Sell,
        _ => throw new InvalidOperationException("An unknown trade side is persisted.")
    };

    private static void AddUuid(NpgsqlCommand command, string name, Guid value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Uuid, value);

    private static void AddNullableUuid(NpgsqlCommand command, string name, Guid? value) =>
        command.Parameters.AddWithValue(
            name,
            NpgsqlDbType.Uuid,
            value is null ? DBNull.Value : value.Value);

    private static void AddUuidArray(NpgsqlCommand command, string name, Guid[] value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Array | NpgsqlDbType.Uuid, value);

    private static void AddTextArray(NpgsqlCommand command, string name, string[] value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Array | NpgsqlDbType.Text, value);

    private static void AddNumeric(NpgsqlCommand command, string name, decimal value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Numeric, value);

    private static void AddBigInteger(NpgsqlCommand command, string name, long value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Bigint, value);

    private static void AddInteger(NpgsqlCommand command, string name, int value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Integer, value);

    private static void AddText(NpgsqlCommand command, string name, string value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Text, value);

    private static void AddNullableText(NpgsqlCommand command, string name, string? value) =>
        command.Parameters.AddWithValue(
            name,
            NpgsqlDbType.Text,
            value is null ? DBNull.Value : value);

    private static void AddTimestamp(NpgsqlCommand command, string name, DateTimeOffset? value) =>
        command.Parameters.AddWithValue(
            name,
            NpgsqlDbType.TimestampTz,
            value is null ? DBNull.Value : value.Value.ToUniversalTime());

    private static void AddDate(NpgsqlCommand command, string name, DateOnly value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Date, value);
}
