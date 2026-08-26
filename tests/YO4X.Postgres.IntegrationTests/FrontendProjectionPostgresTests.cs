using System.Security.Cryptography;
using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.ControlPlane.Postgres;
using YO4X.Identity;

namespace YO4X.Postgres.IntegrationTests;

/// <summary>
/// Real PostgreSQL coverage for the frontend projection adapter. The projections are the
/// only surface that reads the additive catalog, bots, simulation, billing and journal
/// schemas, so isolation is proven against a live database rather than against SQL text:
/// a second tenant and a second user of the same tenant are always seeded, and every read
/// an actor performs must return that actor's rows and nothing else.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class FrontendProjectionPostgresTests(PostgresContainerFixture postgres)
{
    private static readonly DateTimeOffset SeedInstant =
        new(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresContainerFixture postgres = postgres;

    [PostgresFact]
    public async Task EveryProjectionReadReturnsOnlyTheAuthenticatedTenantsRows()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        ProjectionWorld world = await SeedAsync(database);
        var projections = new PostgresFrontendProjections(database.ControlApi);

        StrategyCatalogPage catalog = await projections.GetStrategyCatalogAsync(
            world.ActorA1,
            new StrategyCatalogQuery(1, 60, null, null, null, null),
            CancellationToken.None);
        Assert.Equal(world.StrategiesA.Count, catalog.TotalCount);
        Assert.Equal(
            world.StrategiesA.Order().ToList(),
            catalog.Items.Select(static item => item.Id).Order().ToList());
        Assert.DoesNotContain(world.StrategyB, catalog.Items.Select(static item => item.Id));
        Assert.Equal(["Arb", "Carry", "Grid", "Scalp", "Trend"], catalog.Categories);
        Assert.Equal(["EURUSD", "GBPUSD", "USDJPY"], catalog.Symbols);

        Assert.Null(await projections.GetStrategyDetailAsync(
            world.ActorA1,
            world.StrategyB,
            CancellationToken.None));
        Assert.NotNull(await projections.GetStrategyDetailAsync(
            world.ActorA1,
            world.StrategiesA[0],
            CancellationToken.None));
        Assert.Empty(await projections.GetStrategyReviewsAsync(
            world.ActorA1,
            world.StrategyB,
            20,
            CancellationToken.None));
        Assert.Equal(
            3,
            (await projections.GetStrategyReviewsAsync(
                world.ActorA1,
                world.StrategiesA[0],
                20,
                CancellationToken.None)).Count);

        IReadOnlyList<BotView> bots = await projections.GetBotsAsync(
            world.ActorA1,
            CancellationToken.None);
        Assert.Equal(
            world.BotsA1.Order().ToList(),
            bots.Select(static bot => bot.Id).Order().ToList());
        Assert.DoesNotContain(world.BotB1, bots.Select(static bot => bot.Id));
        Assert.Null(await projections.GetBotAsync(
            world.ActorA1,
            world.BotB1,
            CancellationToken.None));

        IReadOnlyList<BacktestView> backtests = await projections.GetBacktestsAsync(
            world.ActorA1,
            CancellationToken.None);
        Assert.Equal(2, backtests.Count);
        Assert.All(
            backtests,
            backtest => Assert.Contains(backtest.StrategyId, world.StrategiesA));

        IReadOnlyList<CloudRunnerView> runners = await projections.GetCloudRunnersAsync(
            world.ActorA1,
            CancellationToken.None);
        Assert.Equal(2, runners.Count);
        Assert.All(runners, runner => Assert.Contains(runner.BotId, world.BotsA1));

        IReadOnlyList<CloudPlanView> plans = await projections.GetCloudPlansAsync(
            world.ActorA1,
            CancellationToken.None);
        Assert.Equal(["plan-global", "plan-tenant-a"], plans.Select(static plan => plan.Code));
        Assert.Equal(["Unlimited backtests", "Priority runners"], plans[0].Features);

        // The catalogue-wide region table carries no tenant column: it is gated on the
        // authenticated tenant still being active, so every tenant sees the same list.
        IReadOnlyList<CloudRegionView> regionsA = await projections.GetCloudRegionsAsync(
            world.ActorA1,
            CancellationToken.None);
        IReadOnlyList<CloudRegionView> regionsB = await projections.GetCloudRegionsAsync(
            world.ActorB1,
            CancellationToken.None);
        Assert.Equal(["eu-central", "us-east", "ap-south"], regionsA.Select(static r => r.Code));
        Assert.Equal(regionsA, regionsB);

        JournalPage journal = await projections.GetJournalAsync(
            world.ActorA1,
            new JournalQuery(200, null, null, null),
            CancellationToken.None);
        Assert.Equal(
            world.TradesA1.Order().ToList(),
            journal.Items.Select(static item => item.Id).Order().ToList());
        Assert.Null(journal.NextCursor);

        BotUptimeProjection uptime = await projections.GetBotUptimeAsync(
            world.ActorA1,
            7,
            CancellationToken.None);
        Assert.Equal(7, uptime.Days);
        Assert.Equal(3, uptime.Samples.Count);
        Assert.Equal(60, uptime.TotalDowntimeMinutes);

        DashboardSummaryView dashboard = await projections.GetDashboardSummaryAsync(
            world.ActorA1,
            CancellationToken.None);
        Assert.Equal(1, dashboard.LiveBotCount);
        Assert.Equal(1, dashboard.CloudRunnerCount);
        BotView running = Assert.Single(dashboard.RunningBots);
        Assert.Equal(world.BotsA1[0], running.Id);
        Assert.Equal("USD 12.50", Stat(dashboard, "pl-today").Value);
        Assert.Equal(TrendDirection.Up, Stat(dashboard, "pl-today").Direction);
        Assert.Equal("4", Stat(dashboard, "trades-today").Value);

        // The bridge probe counts only the authenticated user's trades opened today; the
        // trades seeded "today" belong to the second user and to the second tenant.
        BridgeStatusView bridge = await projections.GetBridgeStatusAsync(
            world.ActorA1,
            CancellationToken.None);
        Assert.True(bridge.Connected);
        Assert.Equal(0, bridge.OrdersToday);
        Assert.Equal(0, bridge.Rejections);
        Assert.False(string.IsNullOrWhiteSpace(bridge.Version));
        Assert.True(bridge.RoundTripMs >= 0);
        Assert.Equal(
            1,
            (await projections.GetBridgeStatusAsync(world.ActorB1, CancellationToken.None))
                .OrdersToday);
    }

    [PostgresFact]
    public async Task OneUserNeverObservesAnotherUsersRowsInsideTheSameTenant()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        ProjectionWorld world = await SeedAsync(database);
        var projections = new PostgresFrontendProjections(database.ControlApi);

        IReadOnlyList<BotView> firstBots = await projections.GetBotsAsync(
            world.ActorA1,
            CancellationToken.None);
        IReadOnlyList<BotView> secondBots = await projections.GetBotsAsync(
            world.ActorA2,
            CancellationToken.None);
        Assert.Equal(world.BotsA1.Order().ToList(), firstBots.Select(static bot => bot.Id).Order().ToList());
        Assert.Equal(world.BotsA2.Order().ToList(), secondBots.Select(static bot => bot.Id).Order().ToList());
        Assert.Empty(firstBots.Select(static bot => bot.Id).Intersect(world.BotsA2));
        Assert.Null(await projections.GetBotAsync(
            world.ActorA1,
            world.BotsA2[0],
            CancellationToken.None));

        // Metrics are joined through the owning bot, so another user's windows never leak.
        Assert.All(firstBots, bot => Assert.All(
            bot.Metrics,
            metric => Assert.Contains(metric.Window, (string[])["TODAY", "SEVEN_DAY"])));
        Assert.Empty(secondBots.SelectMany(static bot => bot.Metrics));

        // The masked broker login is exposed only to the user who owns the account.
        BotView broker = Assert.Single(firstBots, bot => bot.BrokerAccountId is not null);
        Assert.Equal(world.BrokerAccountA1, broker.BrokerAccountId);
        Assert.Equal("******11", broker.MaskedLogin);

        Assert.Equal(2, (await projections.GetBacktestsAsync(world.ActorA1, CancellationToken.None)).Count);
        Assert.Single(await projections.GetBacktestsAsync(world.ActorA2, CancellationToken.None));
        Assert.Equal(2, (await projections.GetCloudRunnersAsync(world.ActorA1, CancellationToken.None)).Count);
        Assert.Single(await projections.GetCloudRunnersAsync(world.ActorA2, CancellationToken.None));

        JournalPage first = await projections.GetJournalAsync(
            world.ActorA1,
            new JournalQuery(200, null, null, null),
            CancellationToken.None);
        JournalPage second = await projections.GetJournalAsync(
            world.ActorA2,
            new JournalQuery(200, null, null, null),
            CancellationToken.None);
        Assert.Empty(
            first.Items.Select(static item => item.Id)
                .Intersect(second.Items.Select(static item => item.Id)));
        Assert.Equal(world.TradesA1.Count, first.Items.Count);
        Assert.Single(second.Items);

        BotUptimeProjection secondUptime = await projections.GetBotUptimeAsync(
            world.ActorA2,
            30,
            CancellationToken.None);
        Assert.Empty(secondUptime.Samples);
        Assert.Equal(0, secondUptime.TotalDowntimeMinutes);

        // The second user owns a running bot and an active runner of their own; the first
        // user's dashboard above counted exactly one of each, so neither summary borrows the
        // other's rows.
        DashboardSummaryView secondDashboard = await projections.GetDashboardSummaryAsync(
            world.ActorA2,
            CancellationToken.None);
        Assert.Equal(1, secondDashboard.LiveBotCount);
        Assert.Equal(1, secondDashboard.CloudRunnerCount);
        Assert.Equal(world.BotsA2[1], Assert.Single(secondDashboard.RunningBots).Id);
        Assert.Equal("USD 0.00", Stat(secondDashboard, "pl-today").Value);
        Assert.Equal(TrendDirection.Flat, Stat(secondDashboard, "pl-today").Direction);
        Assert.Equal("0", Stat(secondDashboard, "trades-today").Value);
    }

    [PostgresFact]
    public async Task CatalogPagingClampsItsBoundsAndReportsExactTotals()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        ProjectionWorld world = await SeedAsync(database);
        var projections = new PostgresFrontendProjections(database.ControlApi);

        StrategyCatalogPage defaults = await projections.GetStrategyCatalogAsync(
            world.ActorA1,
            new StrategyCatalogQuery(0, 0, null, null, null, null),
            CancellationToken.None);
        Assert.Equal(1, defaults.Page);
        Assert.Equal(24, defaults.PageSize);
        Assert.Equal(7, defaults.TotalCount);
        Assert.Equal(1, defaults.TotalPages);
        Assert.Equal(7, defaults.Items.Count);

        StrategyCatalogPage clamped = await projections.GetStrategyCatalogAsync(
            world.ActorA1,
            new StrategyCatalogQuery(-4, 10_000, null, null, null, null),
            CancellationToken.None);
        Assert.Equal(1, clamped.Page);
        Assert.Equal(60, clamped.PageSize);
        Assert.Equal(1, clamped.TotalPages);

        var pages = new List<Guid>();
        for (int page = 1; page <= 3; page++)
        {
            StrategyCatalogPage slice = await projections.GetStrategyCatalogAsync(
                world.ActorA1,
                new StrategyCatalogQuery(page, 3, null, null, null, "NAME"),
                CancellationToken.None);
            Assert.Equal(page, slice.Page);
            Assert.Equal(3, slice.PageSize);
            Assert.Equal(7, slice.TotalCount);
            Assert.Equal(3, slice.TotalPages);
            pages.AddRange(slice.Items.Select(static item => item.Id));
        }

        Assert.Equal(7, pages.Count);
        Assert.Equal(7, pages.Distinct().Count());
        StrategyCatalogPage beyond = await projections.GetStrategyCatalogAsync(
            world.ActorA1,
            new StrategyCatalogQuery(4, 3, null, null, null, "NAME"),
            CancellationToken.None);
        Assert.Empty(beyond.Items);
        Assert.Equal(7, beyond.TotalCount);
        Assert.Equal(3, beyond.TotalPages);

        Assert.Equal(
            ["Alpha Grid", "Beta Trend", "Delta Swing", "Epsilon Arb", "Eta Momentum", "Gamma Scalp", "Zeta Carry"],
            await NamesAsync(projections, world, "NAME"));
        Assert.Equal(
            ["Eta Momentum", "Zeta Carry", "Epsilon Arb", "Delta Swing", "Gamma Scalp", "Beta Trend", "Alpha Grid"],
            await NamesAsync(projections, world, "RECENT"));
        Assert.Equal(
            ["Epsilon Arb", "Alpha Grid", "Beta Trend", "Eta Momentum", "Gamma Scalp", "Delta Swing", "Zeta Carry"],
            await NamesAsync(projections, world, "TOP_RATED"));
        Assert.Equal(
            ["Delta Swing", "Eta Momentum", "Beta Trend", "Gamma Scalp", "Zeta Carry", "Epsilon Arb", "Alpha Grid"],
            await NamesAsync(projections, world, null));
        Assert.Equal(
            await NamesAsync(projections, world, null),
            await NamesAsync(projections, world, "not-a-sort"));

        StrategyCatalogPage filtered = await projections.GetStrategyCatalogAsync(
            world.ActorA1,
            new StrategyCatalogQuery(1, 60, "Trend", "EURUSD", null, "NAME"),
            CancellationToken.None);
        Assert.Equal(["Beta Trend", "Eta Momentum"], filtered.Items.Select(static item => item.Name));
        Assert.Equal(2, filtered.TotalCount);

        StrategyCatalogPage searched = await projections.GetStrategyCatalogAsync(
            world.ActorA1,
            new StrategyCatalogQuery(1, 60, null, null, "  CARRY  ", null),
            CancellationToken.None);
        Assert.Equal(["Zeta Carry"], searched.Items.Select(static item => item.Name));

        StrategyCatalogPage empty = await projections.GetStrategyCatalogAsync(
            world.ActorA1,
            new StrategyCatalogQuery(1, 60, "Nonexistent", null, null, null),
            CancellationToken.None);
        Assert.Empty(empty.Items);
        Assert.Equal(0, empty.TotalCount);
        Assert.Equal(0, empty.TotalPages);

        Assert.Equal(
            2,
            (await projections.GetStrategyReviewsAsync(
                world.ActorA1,
                world.StrategiesA[0],
                2,
                CancellationToken.None)).Count);
        Assert.Equal(
            3,
            (await projections.GetStrategyReviewsAsync(
                world.ActorA1,
                world.StrategiesA[0],
                0,
                CancellationToken.None)).Count);
    }

    [PostgresFact]
    public async Task JournalKeysetCursorWalksEveryTradeExactlyOnce()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        ProjectionWorld world = await SeedAsync(database);
        var projections = new PostgresFrontendProjections(database.ControlApi);

        List<Guid> single = (await projections.GetJournalAsync(
            world.ActorA1,
            new JournalQuery(200, null, null, null),
            CancellationToken.None)).Items.Select(static item => item.Id).ToList();
        Assert.Equal(world.TradesA1.Count, single.Count);

        var walked = new List<Guid>();
        Guid? cursor = null;
        int guard = 0;
        do
        {
            JournalPage page = await projections.GetJournalAsync(
                world.ActorA1,
                new JournalQuery(4, cursor, null, null),
                CancellationToken.None);
            Assert.True(page.Items.Count <= 4);
            walked.AddRange(page.Items.Select(static item => item.Id));
            cursor = page.NextCursor;
            Assert.True(++guard <= 10, "The journal cursor did not terminate.");
        }
        while (cursor is not null);

        Assert.Equal(single, walked);
        Assert.Equal(walked.Count, walked.Distinct().Count());
        Assert.Equal(world.TradesA1.Order().ToList(), walked.Order().ToList());

        // An unknown cursor cannot be positioned, so the page is empty rather than a silent
        // restart from the newest trade.
        JournalPage unknown = await projections.GetJournalAsync(
            world.ActorA1,
            new JournalQuery(4, Guid.CreateVersion7(), null, null),
            CancellationToken.None);
        Assert.Empty(unknown.Items);
        Assert.Null(unknown.NextCursor);

        // Another user's trade identifier is an unknown cursor here as well.
        JournalPage foreignCursor = await projections.GetJournalAsync(
            world.ActorA1,
            new JournalQuery(4, world.TradeA2, null, null),
            CancellationToken.None);
        Assert.Empty(foreignCursor.Items);
        Assert.Null(foreignCursor.NextCursor);

        JournalPage windowed = await projections.GetJournalAsync(
            world.ActorA1,
            new JournalQuery(200, null, SeedInstant.AddMinutes(5), SeedInstant.AddMinutes(9)),
            CancellationToken.None);
        Assert.Equal(5, windowed.Items.Count);
        Assert.All(
            windowed.Items,
            item =>
            {
                Assert.True(item.OpenedAt >= SeedInstant.AddMinutes(5));
                Assert.True(item.OpenedAt < SeedInstant.AddMinutes(9));
            });
    }

    [PostgresFact]
    public async Task ProjectionWritesRefuseForeignTenantAndForeignUserReferences()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        ProjectionWorld world = await SeedAsync(database);
        var projections = new PostgresFrontendProjections(database.ControlApi);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() => projections.CreateBotAsync(
            world.ActorA1,
            new CreateBot(world.StrategyB, null, "Foreign strategy", "EURUSD", "Balanced", BotHost.Local),
            CancellationToken.None));

        await Assert.ThrowsAsync<ResourceNotFoundException>(() => projections.CreateBotAsync(
            world.ActorA1,
            new CreateBot(
                world.StrategiesA[0],
                world.BrokerAccountA2,
                "Foreign broker account",
                "EURUSD",
                "Balanced",
                BotHost.Local),
            CancellationToken.None));

        await Assert.ThrowsAsync<DomainException>(() => projections.CreateBotAsync(
            world.ActorA1,
            new CreateBot(world.StrategiesA[0], null, "   ", "EURUSD", "Balanced", BotHost.Local),
            CancellationToken.None));

        BotView created = await projections.CreateBotAsync(
            world.ActorA1,
            new CreateBot(
                world.StrategiesA[0],
                world.BrokerAccountA1,
                "  Accepted bot  ",
                "EURUSD",
                "Balanced",
                BotHost.Cloud),
            CancellationToken.None);
        Assert.Equal("Accepted bot", created.Name);
        Assert.Equal(BotStatus.Draft, created.Status);
        Assert.Equal(BotHost.Cloud, created.Host);
        Assert.Equal(world.BrokerAccountA1, created.BrokerAccountId);
        Assert.Equal("******11", created.MaskedLogin);
        Assert.Empty(created.Metrics);

        // The new bot is visible to its owner and to nobody else.
        Assert.NotNull(await projections.GetBotAsync(world.ActorA1, created.Id, CancellationToken.None));
        Assert.Null(await projections.GetBotAsync(world.ActorA2, created.Id, CancellationToken.None));
        Assert.Null(await projections.GetBotAsync(world.ActorB1, created.Id, CancellationToken.None));

        Assert.Null(await projections.SetBotStatusAsync(
            world.ActorA2,
            created.Id,
            new BotStatusChange(BotStatus.Running),
            CancellationToken.None));
        Assert.Null(await projections.SetBotStatusAsync(
            world.ActorB1,
            created.Id,
            new BotStatusChange(BotStatus.Running),
            CancellationToken.None));
        Assert.Null(await projections.SetBotStatusAsync(
            world.ActorA1,
            world.BotsA2[0],
            new BotStatusChange(BotStatus.Running),
            CancellationToken.None));

        BotView? started = await projections.SetBotStatusAsync(
            world.ActorA1,
            created.Id,
            new BotStatusChange(BotStatus.Running),
            CancellationToken.None);
        Assert.NotNull(started);
        Assert.Equal(BotStatus.Running, started.Status);
        Assert.Equal(BotStatus.Draft, (await projections.GetBotAsync(
            world.ActorA2,
            world.BotsA2[0],
            CancellationToken.None))!.Status);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() => projections.CreateBacktestAsync(
            world.ActorA1,
            new CreateBacktest(
                world.StrategyB,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 2, 1),
                "EURUSD",
                "H1",
                "EVERY_TICK_REAL",
                []),
            CancellationToken.None));
        await Assert.ThrowsAsync<DomainException>(() => projections.CreateBacktestAsync(
            world.ActorA1,
            new CreateBacktest(
                world.StrategiesA[0],
                new DateOnly(2026, 2, 1),
                new DateOnly(2026, 1, 1),
                "EURUSD",
                "H1",
                "EVERY_TICK_REAL",
                []),
            CancellationToken.None));

        BacktestView backtest = await projections.CreateBacktestAsync(
            world.ActorA1,
            new CreateBacktest(
                world.StrategiesA[0],
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 2, 1),
                "EURUSD",
                "H1",
                "EVERY_TICK_REAL",
                []),
            CancellationToken.None);
        Assert.Equal(BacktestStatus.Queued, backtest.Status);
        Assert.Equal(0, backtest.TradeCount);
        Assert.Null(backtest.CompletedAt);
        Assert.Contains(
            backtest.Id,
            (await projections.GetBacktestsAsync(world.ActorA1, CancellationToken.None))
                .Select(static view => view.Id));
        Assert.DoesNotContain(
            backtest.Id,
            (await projections.GetBacktestsAsync(world.ActorA2, CancellationToken.None))
                .Select(static view => view.Id));
    }

    [PostgresFact]
    public async Task EmptyProjectionsReportZeroesAndFabricateNothing()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        ProjectionWorld world = await SeedAsync(database);
        var projections = new PostgresFrontendProjections(database.ControlApi);
        UserActor barren = world.ActorA3;

        DashboardSummaryView dashboard = await projections.GetDashboardSummaryAsync(
            barren,
            CancellationToken.None);
        Assert.Equal(0, dashboard.LiveBotCount);
        Assert.Equal(0, dashboard.CloudRunnerCount);
        Assert.Empty(dashboard.RunningBots);
        Assert.Equal(
            ["live-bots", "pl-today", "trades-today", "cloud-runners"],
            dashboard.Stats.Select(static stat => stat.Id));
        Assert.All(
            dashboard.Stats,
            stat => Assert.Equal(TrendDirection.Flat, stat.Direction));
        Assert.Equal("0", Stat(dashboard, "live-bots").Value);
        Assert.Equal("0 configured", Stat(dashboard, "live-bots").Delta);
        Assert.Equal("USD 0.00", Stat(dashboard, "pl-today").Value);
        Assert.Equal("USD 0.00 7d", Stat(dashboard, "pl-today").Delta);
        Assert.Equal("0", Stat(dashboard, "trades-today").Value);
        Assert.Equal("0 7d", Stat(dashboard, "trades-today").Delta);
        Assert.Equal("0", Stat(dashboard, "cloud-runners").Value);
        Assert.Equal("0 provisioned", Stat(dashboard, "cloud-runners").Delta);

        Assert.Empty(await projections.GetBotsAsync(barren, CancellationToken.None));
        Assert.Empty(await projections.GetBacktestsAsync(barren, CancellationToken.None));
        Assert.Empty(await projections.GetCloudRunnersAsync(barren, CancellationToken.None));
        Assert.Empty((await projections.GetJournalAsync(
            barren,
            new JournalQuery(50, null, null, null),
            CancellationToken.None)).Items);

        BotUptimeProjection uptime = await projections.GetBotUptimeAsync(
            barren,
            0,
            CancellationToken.None);
        Assert.Equal(7, uptime.Days);
        Assert.Empty(uptime.Samples);
        Assert.Equal(0, uptime.TotalDowntimeMinutes);
        Assert.Equal(
            90,
            (await projections.GetBotUptimeAsync(barren, 10_000, CancellationToken.None)).Days);

        BridgeStatusView bridge = await projections.GetBridgeStatusAsync(
            barren,
            CancellationToken.None);
        Assert.True(bridge.Connected);
        Assert.Equal(0, bridge.OrdersToday);
        Assert.Equal(0, bridge.Rejections);

        // A revoked session cannot read any projection at all.
        await RevokeSessionAsync(database, barren);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            projections.GetDashboardSummaryAsync(barren, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            projections.GetCloudRegionsAsync(barren, CancellationToken.None));
    }

    private static DashboardStatView Stat(DashboardSummaryView dashboard, string id) =>
        Assert.Single(dashboard.Stats, stat => stat.Id == id);

    private static async Task<IReadOnlyList<string>> NamesAsync(
        PostgresFrontendProjections projections,
        ProjectionWorld world,
        string? sort)
    {
        StrategyCatalogPage page = await projections.GetStrategyCatalogAsync(
            world.ActorA1,
            new StrategyCatalogQuery(1, 60, null, null, null, sort),
            CancellationToken.None);
        return page.Items.Select(static item => item.Name).ToList();
    }

    private static async Task RevokeSessionAsync(PostgresTestDatabase database, UserActor actor)
    {
        await using NpgsqlConnection connection = await database.Administrator.OpenConnectionAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        await ReplicaModeAsync(connection, transaction);
        await using (var command = new NpgsqlCommand(
            """
            update identity.user_session_families
            set state = 'revoked', revoked_at = clock_timestamp()
            where tenant_id = @tenant_id and id = @session_id
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, actor.TenantId);
            command.Parameters.AddWithValue("session_id", NpgsqlDbType.Uuid, actor.SessionId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        await transaction.CommitAsync();
    }

    private static async Task ReplicaModeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction)
    {
        await using var command = new NpgsqlCommand(
            "set local session_replication_role = replica",
            connection,
            transaction);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<ProjectionWorld> SeedAsync(PostgresTestDatabase database)
    {
        Guid tenantA = Guid.CreateVersion7();
        Guid tenantB = Guid.CreateVersion7();
        Guid userA1 = Guid.CreateVersion7();
        Guid userA2 = Guid.CreateVersion7();
        Guid userA3 = Guid.CreateVersion7();
        Guid userB1 = Guid.CreateVersion7();
        Guid sessionA1 = Guid.CreateVersion7();
        Guid sessionA2 = Guid.CreateVersion7();
        Guid sessionA3 = Guid.CreateVersion7();
        Guid sessionB1 = Guid.CreateVersion7();
        Guid brokerAccountA1 = Guid.CreateVersion7();
        Guid brokerAccountA2 = Guid.CreateVersion7();
        List<Guid> strategiesA = [.. Enumerable.Range(0, 7).Select(static _ => Guid.CreateVersion7())];
        Guid strategyB = Guid.CreateVersion7();
        List<Guid> botsA1 = [.. Enumerable.Range(0, 3).Select(static _ => Guid.CreateVersion7())];
        List<Guid> botsA2 = [.. Enumerable.Range(0, 2).Select(static _ => Guid.CreateVersion7())];
        Guid botB1 = Guid.CreateVersion7();
        List<Guid> tradesA1 = [.. Enumerable.Range(0, 11).Select(static _ => Guid.CreateVersion7())];
        Guid tradeA2 = Guid.CreateVersion7();
        Guid tradeB1 = Guid.CreateVersion7();

        await using NpgsqlConnection connection = await database.Administrator.OpenConnectionAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        await ReplicaModeAsync(connection, transaction);

        async Task ExecuteAsync(string sql, Action<NpgsqlParameterCollection> bind)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            bind(command.Parameters);
            await command.ExecuteNonQueryAsync();
        }

        foreach ((Guid tenantId, string label) in new[] { (tenantA, "a"), (tenantB, "b") })
        {
            await ExecuteAsync(
                """
                insert into identity.tenants (id, slug, display_name, state)
                values (@id, @slug, @name, 'active')
                """,
                parameters =>
                {
                    parameters.AddWithValue("id", NpgsqlDbType.Uuid, tenantId);
                    parameters.AddWithValue("slug", NpgsqlDbType.Text, $"projection-{label}-{tenantId:N}");
                    parameters.AddWithValue("name", NpgsqlDbType.Text, $"Projection tenant {label}");
                });
        }

        foreach ((Guid tenantId, Guid userId, Guid sessionId) in new[]
        {
            (tenantA, userA1, sessionA1),
            (tenantA, userA2, sessionA2),
            (tenantA, userA3, sessionA3),
            (tenantB, userB1, sessionB1)
        })
        {
            await ExecuteAsync(
                """
                insert into identity.user_identities
                    (id, tenant_id, normalized_email, security_state, email_verified_at)
                values
                    (@user_id, @tenant_id, @email, 'active', statement_timestamp());

                insert into identity.user_session_families
                    (id, tenant_id, user_id, device_id, current_token_hash, state, expires_at)
                values
                    (@session_id, @tenant_id, @user_id, @device_id, @token_hash, 'active',
                     statement_timestamp() + interval '1 hour');
                """,
                parameters =>
                {
                    parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
                    parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, userId);
                    parameters.AddWithValue("email", NpgsqlDbType.Text, $"projection-{userId:N}@example.test");
                    parameters.AddWithValue("session_id", NpgsqlDbType.Uuid, sessionId);
                    parameters.AddWithValue("device_id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
                    parameters.AddWithValue(
                        "token_hash",
                        NpgsqlDbType.Text,
                        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());
                });
        }

        foreach ((Guid accountId, Guid userId, string maskedLogin) in new[]
        {
            (brokerAccountA1, userA1, "******11"),
            (brokerAccountA2, userA2, "******22")
        })
        {
            await ExecuteAsync(
                """
                insert into operations.broker_accounts
                    (id, tenant_id, user_id, broker_id, server, masked_login,
                     binding_fingerprint, environment, state)
                values
                    (@id, @tenant_id, @user_id, @broker_id, 'Broker-Demo', @masked_login,
                     @fingerprint, 'demo', 'active')
                """,
                parameters =>
                {
                    parameters.AddWithValue("id", NpgsqlDbType.Uuid, accountId);
                    parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantA);
                    parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, userId);
                    parameters.AddWithValue("broker_id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
                    parameters.AddWithValue("masked_login", NpgsqlDbType.Text, maskedLogin);
                    parameters.AddWithValue(
                        "fingerprint",
                        NpgsqlDbType.Text,
                        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());
                });
        }

        (string Name, string Category, string Symbol, decimal Rating, int RatingCount, int ActiveUsers)[] seeds =
        [
            ("Alpha Grid", "Grid", "EURUSD", 4.90m, 10, 100),
            ("Beta Trend", "Trend", "EURUSD", 4.50m, 20, 700),
            ("Gamma Scalp", "Scalp", "GBPUSD", 3.10m, 30, 500),
            ("Delta Swing", "Trend", "USDJPY", 2.20m, 5, 900),
            ("Epsilon Arb", "Arb", "GBPUSD", 4.90m, 40, 200),
            ("Zeta Carry", "Carry", "USDJPY", 1.00m, 1, 300),
            ("Eta Momentum", "Trend", "EURUSD", 3.75m, 15, 800)
        ];

        for (int index = 0; index < seeds.Length; index++)
        {
            (string name, string category, string symbol, decimal rating, int ratingCount, int activeUsers) =
                seeds[index];
            await InsertStrategyAsync(
                ExecuteAsync,
                strategiesA[index],
                tenantA,
                name,
                category,
                symbol,
                rating,
                ratingCount,
                activeUsers,
                SeedInstant.AddDays(index));
        }

        await InsertStrategyAsync(
            ExecuteAsync,
            strategyB,
            tenantB,
            "Foreign Tenant Strategy",
            "Foreign",
            "AUDNZD",
            5.00m,
            99,
            9_999,
            SeedInstant.AddDays(30));

        for (int ordinal = 0; ordinal < 2; ordinal++)
        {
            int captured = ordinal;
            await ExecuteAsync(
                """
                insert into catalog.strategy_performance
                    (id, tenant_id, strategy_id, ordinal, label, value)
                values (@id, @tenant_id, @strategy_id, @ordinal, @label, @value);

                insert into catalog.strategy_equity_points
                    (id, tenant_id, strategy_id, ordinal, period_label, equity)
                values (@point_id, @tenant_id, @strategy_id, @ordinal, @period, @equity);
                """,
                parameters =>
                {
                    parameters.AddWithValue("id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
                    parameters.AddWithValue("point_id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
                    parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantA);
                    parameters.AddWithValue("strategy_id", NpgsqlDbType.Uuid, strategiesA[0]);
                    parameters.AddWithValue("ordinal", NpgsqlDbType.Integer, captured);
                    parameters.AddWithValue("label", NpgsqlDbType.Text, $"Figure {captured}");
                    parameters.AddWithValue("value", NpgsqlDbType.Text, $"{captured}.5%");
                    parameters.AddWithValue("period", NpgsqlDbType.Text, $"M{captured}");
                    parameters.AddWithValue("equity", NpgsqlDbType.Numeric, 1000m + captured);
                });
        }

        for (int index = 0; index < 3; index++)
        {
            int captured = index;
            await ExecuteAsync(
                """
                insert into catalog.strategy_reviews
                    (id, tenant_id, strategy_id, user_id, display_name, initials, rating, body,
                     meta, created_at)
                values
                    (@id, @tenant_id, @strategy_id, @user_id, @display_name, 'RA', @rating,
                     @body, 'verified', @created_at)
                """,
                parameters =>
                {
                    parameters.AddWithValue("id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
                    parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantA);
                    parameters.AddWithValue("strategy_id", NpgsqlDbType.Uuid, strategiesA[0]);
                    parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, userA1);
                    parameters.AddWithValue("display_name", NpgsqlDbType.Text, $"Reviewer {captured}");
                    parameters.AddWithValue("rating", NpgsqlDbType.Smallint, (short)(captured + 3));
                    parameters.AddWithValue("body", NpgsqlDbType.Text, $"Review body {captured}");
                    parameters.AddWithValue(
                        "created_at",
                        NpgsqlDbType.TimestampTz,
                        SeedInstant.AddHours(captured));
                });
        }

        await ExecuteAsync(
            """
            insert into catalog.strategy_reviews
                (id, tenant_id, strategy_id, user_id, display_name, initials, rating, body,
                 meta, created_at)
            values
                (@id, @tenant_id, @strategy_id, @user_id, 'Foreign reviewer', 'FR', 5,
                 'Foreign review', 'verified', @created_at)
            """,
            parameters =>
            {
                parameters.AddWithValue("id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
                parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantB);
                parameters.AddWithValue("strategy_id", NpgsqlDbType.Uuid, strategyB);
                parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, userB1);
                parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, SeedInstant);
            });

        (Guid Id, Guid Tenant, Guid User, Guid Strategy, Guid? Account, string Status, int Offset)[] bots =
        [
            (botsA1[0], tenantA, userA1, strategiesA[0], brokerAccountA1, "RUNNING", 3),
            (botsA1[1], tenantA, userA1, strategiesA[1], null, "PAUSED", 2),
            (botsA1[2], tenantA, userA1, strategiesA[2], null, "STOPPED", 1),
            (botsA2[0], tenantA, userA2, strategiesA[3], brokerAccountA2, "DRAFT", 4),
            (botsA2[1], tenantA, userA2, strategiesA[4], null, "RUNNING", 5),
            (botB1, tenantB, userB1, strategyB, null, "RUNNING", 6)
        ];

        foreach ((Guid id, Guid tenantId, Guid userId, Guid strategyId, Guid? accountId, string status, int offset)
            in bots)
        {
            await ExecuteAsync(
                """
                insert into bots.bots
                    (id, tenant_id, user_id, strategy_id, broker_account_id, name, symbol,
                     risk_label, status, host, created_at, updated_at)
                values
                    (@id, @tenant_id, @user_id, @strategy_id, @broker_account_id, @name,
                     'EURUSD', 'Balanced', @status, 'LOCAL', @created_at, @created_at)
                """,
                parameters =>
                {
                    parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
                    parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
                    parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, userId);
                    parameters.AddWithValue("strategy_id", NpgsqlDbType.Uuid, strategyId);
                    parameters.AddWithValue(
                        "broker_account_id",
                        NpgsqlDbType.Uuid,
                        accountId is null ? DBNull.Value : (object)accountId.Value);
                    parameters.AddWithValue("name", NpgsqlDbType.Text, $"Bot {offset}");
                    parameters.AddWithValue("status", NpgsqlDbType.Text, status);
                    parameters.AddWithValue(
                        "created_at",
                        NpgsqlDbType.TimestampTz,
                        SeedInstant.AddMinutes(offset));
                });
        }

        (Guid Bot, string Window, decimal Amount, int Trades)[] metrics =
        [
            (botsA1[0], "TODAY", 10.25m, 3),
            (botsA1[0], "SEVEN_DAY", 40.00m, 9),
            (botsA1[1], "TODAY", 2.25m, 1),
            (botsA1[1], "SEVEN_DAY", 5.00m, 4)
        ];

        foreach ((Guid botId, string window, decimal amount, int trades) in metrics)
        {
            await ExecuteAsync(
                """
                insert into bots.bot_metrics
                    (id, tenant_id, bot_id, metric_window, pl_amount, trade_count)
                values (@id, @tenant_id, @bot_id, @window, @amount, @trades)
                """,
                parameters =>
                {
                    parameters.AddWithValue("id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
                    parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantA);
                    parameters.AddWithValue("bot_id", NpgsqlDbType.Uuid, botId);
                    parameters.AddWithValue("window", NpgsqlDbType.Text, window);
                    parameters.AddWithValue("amount", NpgsqlDbType.Numeric, amount);
                    parameters.AddWithValue("trades", NpgsqlDbType.Integer, trades);
                });
        }

        // A foreign-tenant metric row for the same window proves the tenant filter and the
        // owning-bot join are both required.
        await ExecuteAsync(
            """
            insert into bots.bot_metrics
                (id, tenant_id, bot_id, metric_window, pl_amount, trade_count)
            values (@id, @tenant_id, @bot_id, 'TODAY', 999.99, 999)
            """,
            parameters =>
            {
                parameters.AddWithValue("id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
                parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantB);
                parameters.AddWithValue("bot_id", NpgsqlDbType.Uuid, botB1);
            });

        for (int ordinal = 0; ordinal < 3; ordinal++)
        {
            int captured = ordinal;
            await ExecuteAsync(
                """
                insert into bots.uptime_samples
                    (id, tenant_id, user_id, ordinal, sampled_on, uptime_ratio, downtime_minutes)
                values (@id, @tenant_id, @user_id, @ordinal, @sampled_on, 0.9900, @downtime)
                """,
                parameters =>
                {
                    parameters.AddWithValue("id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
                    parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantA);
                    parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, userA1);
                    parameters.AddWithValue("ordinal", NpgsqlDbType.Integer, captured);
                    parameters.AddWithValue(
                        "sampled_on",
                        NpgsqlDbType.Date,
                        new DateOnly(2025, 6, 1).AddDays(captured));
                    parameters.AddWithValue("downtime", NpgsqlDbType.Integer, 20);
                });
        }

        await ExecuteAsync(
            """
            insert into bots.uptime_samples
                (id, tenant_id, user_id, ordinal, sampled_on, uptime_ratio, downtime_minutes)
            values (@id, @tenant_id, @user_id, 0, date '2025-06-01', 0.5000, 720)
            """,
            parameters =>
            {
                parameters.AddWithValue("id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
                parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantB);
                parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, userB1);
            });

        (Guid Tenant, Guid User, Guid Strategy, int Offset)[] backtests =
        [
            (tenantA, userA1, strategiesA[0], 1),
            (tenantA, userA1, strategiesA[1], 2),
            (tenantA, userA2, strategiesA[2], 3),
            (tenantB, userB1, strategyB, 4)
        ];

        foreach ((Guid tenantId, Guid userId, Guid strategyId, int offset) in backtests)
        {
            await ExecuteAsync(
                """
                insert into simulation.backtests
                    (id, tenant_id, user_id, strategy_id, period_start, period_end,
                     net_profit_amount, max_drawdown_percent, profit_factor, trade_count,
                     status, created_at)
                values
                    (@id, @tenant_id, @user_id, @strategy_id, date '2025-01-01',
                     date '2025-03-01', 100.00, 4.00, 1.80, 42, 'COMPLETE', @created_at)
                """,
                parameters =>
                {
                    parameters.AddWithValue("id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
                    parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
                    parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, userId);
                    parameters.AddWithValue("strategy_id", NpgsqlDbType.Uuid, strategyId);
                    parameters.AddWithValue(
                        "created_at",
                        NpgsqlDbType.TimestampTz,
                        SeedInstant.AddMinutes(offset));
                });
        }

        (string Code, string Label, int Order)[] regions =
        [
            ("eu-central", "EU Central", 0),
            ("us-east", "US East", 1),
            ("ap-south", "AP South", 2)
        ];

        foreach ((string code, string label, int order) in regions)
        {
            await ExecuteAsync(
                """
                insert into billing.cloud_regions (code, label, display_order)
                values (@code, @label, @display_order)
                """,
                parameters =>
                {
                    parameters.AddWithValue("code", NpgsqlDbType.Text, code);
                    parameters.AddWithValue("label", NpgsqlDbType.Text, label);
                    parameters.AddWithValue("display_order", NpgsqlDbType.Integer, order);
                });
        }

        Guid globalPlan = Guid.CreateVersion7();
        Guid tenantPlan = Guid.CreateVersion7();
        Guid foreignPlan = Guid.CreateVersion7();
        (Guid Id, Guid? Tenant, string Code, int Order)[] plans =
        [
            (globalPlan, null, "plan-global", 0),
            (tenantPlan, tenantA, "plan-tenant-a", 1),
            (foreignPlan, tenantB, "plan-tenant-b", 2)
        ];

        foreach ((Guid planId, Guid? planTenant, string code, int order) in plans)
        {
            await ExecuteAsync(
                """
                insert into billing.cloud_plans
                    (id, tenant_id, code, name, tag, blurb, price_monthly_cents,
                     price_yearly_cents, unit, cta_label, highlighted, display_order)
                values
                    (@id, @tenant_id, @code, @name, 'Popular', 'A plan.', 900, 9000,
                     'per runner', 'Choose', true, @display_order)
                """,
                parameters =>
                {
                    parameters.AddWithValue("id", NpgsqlDbType.Uuid, planId);
                    parameters.AddWithValue(
                        "tenant_id",
                        NpgsqlDbType.Uuid,
                        planTenant is null ? DBNull.Value : (object)planTenant.Value);
                    parameters.AddWithValue("code", NpgsqlDbType.Text, code);
                    parameters.AddWithValue("name", NpgsqlDbType.Text, code);
                    parameters.AddWithValue("display_order", NpgsqlDbType.Integer, order);
                });
        }

        foreach ((Guid planId, int ordinal, string label) in new[]
        {
            (globalPlan, 0, "Unlimited backtests"),
            (globalPlan, 1, "Priority runners"),
            (foreignPlan, 0, "Foreign tenant feature")
        })
        {
            await ExecuteAsync(
                """
                insert into billing.cloud_plan_features (id, plan_id, ordinal, label)
                values (@id, @plan_id, @ordinal, @label)
                """,
                parameters =>
                {
                    parameters.AddWithValue("id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
                    parameters.AddWithValue("plan_id", NpgsqlDbType.Uuid, planId);
                    parameters.AddWithValue("ordinal", NpgsqlDbType.Integer, ordinal);
                    parameters.AddWithValue("label", NpgsqlDbType.Text, label);
                });
        }

        (Guid Tenant, Guid User, Guid Bot, string Status, int Offset)[] runners =
        [
            (tenantA, userA1, botsA1[0], "ACTIVE", 1),
            (tenantA, userA1, botsA1[1], "SUSPENDED", 2),
            (tenantA, userA2, botsA2[0], "ACTIVE", 3),
            (tenantB, userB1, botB1, "ACTIVE", 4)
        ];

        foreach ((Guid tenantId, Guid userId, Guid botId, string status, int offset) in runners)
        {
            await ExecuteAsync(
                """
                insert into billing.cloud_runners
                    (id, tenant_id, user_id, bot_id, region_code, uptime_30d_percent,
                     latency_ms, monthly_price_cents, status, created_at)
                values
                    (@id, @tenant_id, @user_id, @bot_id, 'eu-central', 99.50, 18, 900,
                     @status, @created_at)
                """,
                parameters =>
                {
                    parameters.AddWithValue("id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
                    parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
                    parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, userId);
                    parameters.AddWithValue("bot_id", NpgsqlDbType.Uuid, botId);
                    parameters.AddWithValue("status", NpgsqlDbType.Text, status);
                    parameters.AddWithValue(
                        "created_at",
                        NpgsqlDbType.TimestampTz,
                        SeedInstant.AddMinutes(offset));
                });
        }

        for (int index = 0; index < tradesA1.Count; index++)
        {
            // Two trades deliberately share an instant so the keyset cursor must fall back to
            // its identifier tiebreak instead of skipping or repeating a row.
            int minutes = index >= 6 ? index - 1 : index;
            await ExecuteAsync(
                """
                insert into journal.trades
                    (id, tenant_id, user_id, bot_id, symbol, side, volume, entry_price,
                     exit_price, result_amount, opened_at, closed_at)
                values
                    (@id, @tenant_id, @user_id, @bot_id, 'EURUSD', @side, 1.00, 1.08500,
                     1.08700, 20.00, @opened_at, @opened_at)
                """,
                parameters =>
                {
                    parameters.AddWithValue("id", NpgsqlDbType.Uuid, tradesA1[index]);
                    parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantA);
                    parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, userA1);
                    parameters.AddWithValue("bot_id", NpgsqlDbType.Uuid, botsA1[0]);
                    parameters.AddWithValue("side", NpgsqlDbType.Text, minutes % 2 == 0 ? "BUY" : "SELL");
                    parameters.AddWithValue(
                        "opened_at",
                        NpgsqlDbType.TimestampTz,
                        SeedInstant.AddMinutes(minutes));
                });
        }

        foreach ((Guid tradeId, Guid tenantId, Guid userId) in new[]
        {
            (tradeA2, tenantA, userA2),
            (tradeB1, tenantB, userB1)
        })
        {
            await ExecuteAsync(
                """
                insert into journal.trades
                    (id, tenant_id, user_id, bot_id, symbol, side, volume, entry_price,
                     opened_at)
                values
                    (@id, @tenant_id, @user_id, null, 'GBPUSD', 'BUY', 2.00, 1.26000,
                     clock_timestamp())
                """,
                parameters =>
                {
                    parameters.AddWithValue("id", NpgsqlDbType.Uuid, tradeId);
                    parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
                    parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, userId);
                });
        }

        await transaction.CommitAsync();

        return new ProjectionWorld(
            new UserActor(tenantA, userA1, sessionA1, AuthenticationAssurance.Totp),
            new UserActor(tenantA, userA2, sessionA2, AuthenticationAssurance.Totp),
            new UserActor(tenantA, userA3, sessionA3, AuthenticationAssurance.Totp),
            new UserActor(tenantB, userB1, sessionB1, AuthenticationAssurance.Totp),
            strategiesA,
            strategyB,
            botsA1,
            botsA2,
            botB1,
            brokerAccountA1,
            brokerAccountA2,
            tradesA1,
            tradeA2);
    }

    private static Task InsertStrategyAsync(
        Func<string, Action<NpgsqlParameterCollection>, Task> executeAsync,
        Guid strategyId,
        Guid tenantId,
        string name,
        string category,
        string symbol,
        decimal rating,
        int ratingCount,
        int activeUsers,
        DateTimeOffset updatedAt) =>
        executeAsync(
            """
            insert into catalog.strategies
                (id, tenant_id, slug, name, author_name, author_initials, category, symbol,
                 timeframe, version, description, summary, rating_average, rating_count,
                 active_users, is_free, cloud_price_monthly_cents, cloud_price_yearly_cents,
                 updated_at)
            values
                (@id, @tenant_id, @slug, @name, 'Aurora Labs', 'AL', @category, @symbol,
                 'H1', '1.0.0', @description, @summary, @rating, @rating_count,
                 @active_users, false, 2900, 29000, @updated_at)
            """,
            parameters =>
            {
                parameters.AddWithValue("id", NpgsqlDbType.Uuid, strategyId);
                parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
                parameters.AddWithValue("slug", NpgsqlDbType.Text, $"strategy-{strategyId:N}");
                parameters.AddWithValue("name", NpgsqlDbType.Text, name);
                parameters.AddWithValue("category", NpgsqlDbType.Text, category);
                parameters.AddWithValue("symbol", NpgsqlDbType.Text, symbol);
                parameters.AddWithValue("description", NpgsqlDbType.Text, $"{name} description.");
                parameters.AddWithValue("summary", NpgsqlDbType.Text, $"{name} summary.");
                parameters.AddWithValue("rating", NpgsqlDbType.Numeric, rating);
                parameters.AddWithValue("rating_count", NpgsqlDbType.Integer, ratingCount);
                parameters.AddWithValue("active_users", NpgsqlDbType.Integer, activeUsers);
                parameters.AddWithValue("updated_at", NpgsqlDbType.TimestampTz, updatedAt);
            });

    private sealed record ProjectionWorld(
        UserActor ActorA1,
        UserActor ActorA2,
        UserActor ActorA3,
        UserActor ActorB1,
        IReadOnlyList<Guid> StrategiesA,
        Guid StrategyB,
        IReadOnlyList<Guid> BotsA1,
        IReadOnlyList<Guid> BotsA2,
        Guid BotB1,
        Guid BrokerAccountA1,
        Guid BrokerAccountA2,
        IReadOnlyList<Guid> TradesA1,
        Guid TradeA2);
}
