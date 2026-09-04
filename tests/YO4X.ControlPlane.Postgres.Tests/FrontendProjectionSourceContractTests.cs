namespace YO4X.ControlPlane.Postgres.Tests;

/// <summary>
/// Source contract for the PostgreSQL frontend projection adapter. Every statement the
/// adapter issues must filter the authenticated tenant, every statement that touches a
/// user-owned relation must additionally filter the authenticated user, and no statement
/// may interpolate caller input into its command text.
/// </summary>
public sealed class FrontendProjectionSourceContractTests
{
    /// <summary>
    /// Relations the adapter reads or writes that carry a <c>tenant_id</c> column.
    /// </summary>
    private static readonly string[] TenantScopedRelations =
    [
        "catalog.strategies",
        "catalog.strategy_performance",
        "catalog.strategy_equity_points",
        "catalog.strategy_reviews",
        "catalog.strategy_inputs",
        "catalog.strategy_enum_members",
        "bots.bots",
        "bots.bot_metrics",
        "bots.bot_inputs",
        "bots.broker_symbols",
        "bots.uptime_samples",
        "simulation.backtests",
        "simulation.backtest_inputs",
        "simulation.backtest_equity_points",
        "billing.cloud_plans",
        "billing.cloud_runners",
        "journal.trades",
        "operations.broker_accounts"
    ];

    /// <summary>
    /// Relations whose rows belong to a single authenticated user inside a tenant.
    /// </summary>
    private static readonly string[] UserOwnedRelations =
    [
        "bots.bots",
        "bots.bot_metrics",
        "bots.bot_inputs",
        "bots.uptime_samples",
        "simulation.backtests",
        "simulation.backtest_equity_points",
        "billing.cloud_runners",
        "journal.trades",
        "operations.broker_accounts"
    ];

    /// <summary>
    /// <c>billing.cloud_regions</c> is a catalogue-wide table with no tenant column, so its
    /// read is gated on the authenticated tenant still being active instead.
    /// </summary>
    private const string CatalogueWideRelation = "billing.cloud_regions";

    private static readonly string[] ProjectionEntryPoints =
    [
        "GetStrategyCatalogAsync",
        "GetStrategyDetailAsync",
        "GetStrategyReviewsAsync",
        "GetStrategyInputsAsync",
        "GetBotsAsync",
        "GetBotAsync",
        "CreateBotAsync",
        "SetBotStatusAsync",
        "GetBotUptimeAsync",
        "GetBotSettingsAsync",
        "UpdateBotSettingsAsync",
        "GetBrokerSymbolsAsync",
        "GetBacktestsAsync",
        "GetBacktestDetailAsync",
        "CreateBacktestAsync",
        "GetCloudPlansAsync",
        "GetCloudRunnersAsync",
        "GetCloudRegionsAsync",
        "GetJournalAsync",
        "GetDashboardSummaryAsync",
        "GetBridgeStatusAsync"
    ];

    [Fact]
    public void EveryProjectionStatementFiltersTheAuthenticatedTenant()
    {
        var unscoped = new List<string>();
        foreach (string statement in ReadProjectionStatements())
        {
            if (!ReferencesAny(statement, TenantScopedRelations))
            {
                continue;
            }

            if (IsInsert(statement))
            {
                if (!statement.Contains("tenant_id", StringComparison.Ordinal)
                    || !statement.Contains("@tenant_id", StringComparison.Ordinal))
                {
                    unscoped.Add(statement);
                }

                continue;
            }

            if (!statement.Contains("tenant_id = @tenant_id", StringComparison.Ordinal))
            {
                unscoped.Add(statement);
            }
        }

        Assert.Empty(unscoped);
    }

    [Fact]
    public void EveryUserOwnedProjectionStatementAlsoFiltersTheAuthenticatedUser()
    {
        var unscoped = new List<string>();
        foreach (string statement in ReadProjectionStatements())
        {
            if (!ReferencesAny(statement, UserOwnedRelations))
            {
                continue;
            }

            if (IsInsert(statement))
            {
                if (!statement.Contains("user_id", StringComparison.Ordinal)
                    || !statement.Contains("@user_id", StringComparison.Ordinal))
                {
                    unscoped.Add(statement);
                }

                continue;
            }

            if (!statement.Contains("user_id = @user_id", StringComparison.Ordinal))
            {
                unscoped.Add(statement);
            }
        }

        Assert.Empty(unscoped);
    }

    [Fact]
    public void EveryScopedRelationIsActuallyExercisedByTheAdapter()
    {
        List<string> statements = ReadProjectionStatements();

        foreach (string relation in TenantScopedRelations)
        {
            Assert.Contains(
                statements,
                statement => statement.Contains(relation, StringComparison.Ordinal));
        }

        foreach (string relation in UserOwnedRelations)
        {
            Assert.Contains(
                statements,
                statement => statement.Contains(relation, StringComparison.Ordinal));
        }

        Assert.Contains(
            statements,
            statement => statement.Contains(CatalogueWideRelation, StringComparison.Ordinal));
    }

    [Fact]
    public void CloudRegionCatalogueIsGatedOnAnActiveAuthenticatedTenantInsteadOfATenantColumn()
    {
        string statement = Assert.Single(
            ReadProjectionStatements()
                .Where(candidate => candidate.Contains(
                    "from " + CatalogueWideRelation,
                    StringComparison.Ordinal))
                .ToList());

        Assert.DoesNotContain("tenant_id = @tenant_id", statement, StringComparison.Ordinal);
        Assert.Contains("exists ( select 1 from identity.tenants as tenant", statement, StringComparison.Ordinal);
        Assert.Contains("tenant.id = @tenant_id", statement, StringComparison.Ordinal);
        Assert.Contains("tenant.state = 'active'", statement, StringComparison.Ordinal);
        Assert.Contains("limit @limit", statement, StringComparison.Ordinal);

        // The gate exists because the migration deliberately gives the catalogue table no
        // tenant column; if that ever changes the read must become a normal tenant filter.
        string migration = Normalize(ReadRepositoryFile(
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Migrations",
            "005_frontend_projections.sql"));
        string table = Slice(
            migration,
            "create table billing.cloud_regions",
            "create index cloud_regions_display_order_idx");
        Assert.DoesNotContain("tenant_id", table, StringComparison.Ordinal);
        Assert.Contains("code text primary key", table, StringComparison.Ordinal);
    }

    [Fact]
    public void CloudPlanCatalogueAcceptsOnlyGlobalOrAuthenticatedTenantRows()
    {
        List<string> statements = ReadProjectionStatements()
            .Where(candidate => candidate.Contains("billing.cloud_plans", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, statements.Count);
        foreach (string statement in statements)
        {
            Assert.Contains(
                "(plan.tenant_id is null or plan.tenant_id = @tenant_id)",
                statement,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryProjectionEntryPointOpensATenantBoundTransaction()
    {
        string source = ReadAdapterSource();
        string contracts = ReadRepositoryFile(
            "src",
            "Application",
            "YO4X.ControlPlane.Application",
            "FrontendProjectionContracts.cs");

        foreach (string entryPoint in ProjectionEntryPoints)
        {
            Assert.Contains(entryPoint, contracts, StringComparison.Ordinal);
            string method = ExtractMethod(source, entryPoint);
            Assert.Contains(
                "BeginAsync(actor, cancellationToken)",
                method,
                StringComparison.Ordinal);
            Assert.Contains(
                "await transaction.CommitAsync(cancellationToken)",
                method,
                StringComparison.Ordinal);
        }

        // The single authorization gate proves the caller's identity, session, and tenant are
        // all still active before any projection statement runs.
        string begin = ExtractMethod(source, "BeginAsync");
        Assert.Contains("identity.security_state", begin, StringComparison.Ordinal);
        Assert.Contains("session.state", begin, StringComparison.Ordinal);
        Assert.Contains("tenant.state", begin, StringComparison.Ordinal);
        Assert.Contains("session.expires_at > clock_timestamp()", begin, StringComparison.Ordinal);
        Assert.Contains("throw new UnauthorizedAccessException", begin, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectionCommandTextIsNeverInterpolatedFromCallerInput()
    {
        string source = ReadAdapterSource();

        Assert.DoesNotContain("$\"\"\"", source, StringComparison.Ordinal);
        foreach (string statement in ReadProjectionStatements())
        {
            Assert.DoesNotContain("{", statement, StringComparison.Ordinal);
            Assert.DoesNotContain("}", statement, StringComparison.Ordinal);
        }

        // The catalog order clause is the only text composed at runtime, and it is chosen
        // from a closed server-side allow list rather than echoed from the request.
        string resolver = ExtractMethod(source, "ResolveCatalogOrder");
        Assert.Contains("sort switch", resolver, StringComparison.Ordinal);
        Assert.Contains("\"TOP_RATED\" =>", resolver, StringComparison.Ordinal);
        Assert.Contains("\"RECENT\" =>", resolver, StringComparison.Ordinal);
        Assert.Contains("\"NAME\" =>", resolver, StringComparison.Ordinal);
        Assert.Contains("_ => \"order by strategy.active_users desc", resolver, StringComparison.Ordinal);
        Assert.DoesNotContain("sort +", resolver, StringComparison.Ordinal);
        Assert.DoesNotContain("+ sort", resolver, StringComparison.Ordinal);
        Assert.Equal(
            1,
            CountOccurrences(source, "StrategyCatalogProjection + \"\\n\" + orderBy"));
    }

    [Fact]
    public void CatalogPrefersNewestV2Yo4xPackageWithoutStrategySpecificRules()
    {
        string source = ReadAdapterSource();

        Assert.True(CountOccurrences(source, "package_format_version >= 2") >= 5);
        Assert.True(CountOccurrences(source, "lower(btrim(strategy.name)) like '%.yo4x'") >= 5);
        Assert.True(CountOccurrences(source, "from catalog.strategies as newer_package") >= 5);
        Assert.True(CountOccurrences(source, "(newer_package.updated_at, newer_package.id)") >= 5);
        Assert.DoesNotContain("lower(strategy.name) not like 'straddle%'", source, StringComparison.Ordinal);
        Assert.DoesNotContain("lower(strategy.name) = 'straddle_1.1.36.yo4x'", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BotProjectionPrefersPackagedStrategiesAndSuppressesTheirSourceTwin()
    {
        string source = ReadAdapterSource();

        Assert.Contains("when strategy.package_format_version = 2", source, StringComparison.Ordinal);
        Assert.Contains("then strategy.name", source, StringComparison.Ordinal);
        Assert.Contains("from bots.bots as packaged_bot", source, StringComparison.Ordinal);
        Assert.Contains("packaged_strategy.package_format_version = 2", source, StringComparison.Ordinal);
        Assert.Contains("regexp_replace(lower(bot.name), '\\.(mq5|yo4x)$', '')", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectionRequestBoundsAreClampedByTheAdapterNotTheCaller()
    {
        string source = ReadAdapterSource();

        Assert.Contains("private const int CatalogPageSizeDefault = 24;", source, StringComparison.Ordinal);
        Assert.Contains("private const int CatalogPageSizeMaximum = 60;", source, StringComparison.Ordinal);
        Assert.Contains("private const int JournalLimitMaximum = 200;", source, StringComparison.Ordinal);
        Assert.Contains("private const int ReviewLimitMaximum = 100;", source, StringComparison.Ordinal);
        Assert.Contains("private const int UptimeDaysMaximum = 90;", source, StringComparison.Ordinal);
        Assert.Contains(
            "Math.Min(pageSize, CatalogPageSizeMaximum)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("query.Page < 1 ? 1 : query.Page", source, StringComparison.Ordinal);

        foreach (string statement in ReadProjectionStatements())
        {
            if (statement.StartsWith("select", StringComparison.Ordinal)
                && statement.Contains(" order by ", StringComparison.Ordinal))
            {
                Assert.Contains("limit @limit", statement, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// The settings surface is consumed by a decoder that refuses a timeframe outside
    /// MetaTrader's own period names, a volume that is not positive, and a null in any of
    /// the three settings the contract types as non-nullable. Those are properties of this
    /// adapter, not of the caller: the write accepts a period only from the closed set
    /// below, and the read substitutes for a bot that has never stated one instead of
    /// handing back a null the contract cannot carry.
    /// </summary>
    [Fact]
    public void BotSettingsAreAlwaysAnsweredWithARenderableTimeframeAndVolume()
    {
        string source = ReadAdapterSource();

        string[] periods =
        [
            "M1", "M2", "M3", "M4", "M5", "M6", "M10", "M12", "M15", "M20", "M30",
            "H1", "H2", "H3", "H4", "H6", "H8", "H12", "D1", "W1", "MN1"
        ];
        int start = source.IndexOf(
            "private static readonly string[] BotTimeframes =",
            StringComparison.Ordinal);
        Assert.True(start >= 0, "The closed timeframe set was not declared.");
        string declaration = source[start..source.IndexOf("];", start, StringComparison.Ordinal)];
        foreach (string period in periods)
        {
            Assert.Contains("\"" + period + "\"", declaration, StringComparison.Ordinal);
        }

        // Exactly twenty-one, so a period MetaTrader does not name cannot be added
        // silently alongside the ones it does.
        Assert.Equal(periods.Length, CountOccurrences(declaration, "\"" ) / 2);

        string write = ExtractMethod(source, "RequireBotTimeframe");
        Assert.Contains("BotTimeframes", write, StringComparison.Ordinal);
        Assert.Contains("throw new DomainException", write, StringComparison.Ordinal);

        string read = ExtractMethod(source, "GetBotSettingsAsync");
        Assert.Contains(
            "reader.IsDBNull(3) ? DefaultBotTimeframe : reader.GetString(3)",
            read,
            StringComparison.Ordinal);
        Assert.Contains(
            "reader.IsDBNull(4) ? DefaultBotVolume : reader.GetDecimal(4)",
            read,
            StringComparison.Ordinal);
        Assert.Contains("private const string DefaultBotTimeframe = \"H1\";", source, StringComparison.Ordinal);
        Assert.Contains("private const decimal DefaultBotVolume = 0.01m;", source, StringComparison.Ordinal);

        // The substitution is a read-time default for the form, never a write: a bot that
        // has stated nothing must still read as one that has stated nothing.
        string update = ExtractMethod(source, "UpdateBotSettingsAsync");
        Assert.DoesNotContain("DefaultBotTimeframe", update, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultBotVolume", update, StringComparison.Ordinal);
        Assert.Contains("RequireTradableVolume(request.Volume)", update, StringComparison.Ordinal);
        Assert.Contains("RequireBotTimeframe(request.Timeframe)", update, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectionsNeverReadCredentialOrBindingMaterial()
    {
        string source = ReadAdapterSource();

        Assert.Contains("account.masked_login", source, StringComparison.Ordinal);
        Assert.DoesNotContain("credential_reference", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("binding_fingerprint", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("current_token_hash", source, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInsert(string statement) =>
        statement.StartsWith("insert into", StringComparison.Ordinal);

    private static bool ReferencesAny(string statement, string[] relations) =>
        relations.Any(relation => statement.Contains(relation, StringComparison.Ordinal));

    private static List<string> ReadProjectionStatements()
    {
        string[] parts = ReadAdapterSource().Split("\"\"\"", StringSplitOptions.None);
        Assert.True(
            parts.Length % 2 == 1,
            "The adapter's raw string literals are unbalanced.");

        var statements = new List<string>(parts.Length / 2);
        for (int index = 1; index < parts.Length; index += 2)
        {
            string statement = Normalize(parts[index]);
            if (statement.Length > 0)
            {
                statements.Add(statement);
            }
        }

        Assert.NotEmpty(statements);
        return statements;
    }

    /// <summary>
    /// Returns the declaration of <paramref name="methodName"/> up to the next member. Only a
    /// line indented exactly one level introduces a member, so call sites inside another
    /// method body are never mistaken for the declaration.
    /// </summary>
    private static string ExtractMethod(string source, string methodName)
    {
        string needle = methodName + "(";
        for (int index = source.IndexOf(needle, StringComparison.Ordinal);
            index >= 0;
            index = source.IndexOf(needle, index + 1, StringComparison.Ordinal))
        {
            string prefix = source[(source.LastIndexOf('\n', index) + 1)..index];
            if (!prefix.StartsWith("    public ", StringComparison.Ordinal)
                && !prefix.StartsWith("    private ", StringComparison.Ordinal))
            {
                continue;
            }

            int next = source.IndexOf("\n    p", index, StringComparison.Ordinal);
            return next < 0 ? source[index..] : source[index..next];
        }

        Assert.Fail($"The expected method {methodName} was not declared.");
        return string.Empty;
    }

    private static int CountOccurrences(string value, string pattern) =>
        value.Split(pattern, StringSplitOptions.None).Length - 1;

    private static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string ReadAdapterSource() =>
        ReadRepositoryFile(
            "src",
            "Infrastructure",
            "YO4X.ControlPlane.Postgres",
            "PostgresFrontendProjections.cs");

    private static string ReadRepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "YO4X.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        string path = Path.Combine([directory.FullName, .. segments]);
        Assert.True(File.Exists(path), $"The repository contract file {path} was not found.");
        return File.ReadAllText(path);
    }

    private static string Slice(string value, string startMarker, string endMarker)
    {
        int start = value.IndexOf(startMarker, StringComparison.Ordinal);
        int end = value.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Contract section {startMarker} was not found.");
        return value[start..end];
    }
}
