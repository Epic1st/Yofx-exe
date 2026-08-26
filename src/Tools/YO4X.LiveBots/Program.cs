using System.Globalization;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using YO4X.LocalSecrets.Windows;
using YO4X.Mql5.Compilation;
using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Live;
using YO4X.Mt5.ConnectionProbe.Windows;
using YO4X.StrategyGovernance;

namespace YO4X.LiveBots;

/// <summary>
/// Runs profitable strategies against live broker accounts and records each as a bot the
/// frontend can show.
///
/// <para>
/// "Profitable" here means only that a recorded backtest showed a positive result. That is a
/// far weaker statement than it sounds, so the selection is printed with the data quality
/// behind it and the operator can see exactly what the claim rests on.
/// </para>
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] arguments)
    {
        try
        {
            return await RunAsync(arguments).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or InvalidDataException
            or InvalidOperationException
            or NpgsqlException)
        {
            Console.Error.WriteLine("Live bots failed: " + exception.Message);
            return 2;
        }
    }

    private static async Task<int> RunAsync(string[] arguments)
    {
        string connectionString = Option(arguments, "--connection")
            ?? Environment.GetEnvironmentVariable("YO4X_BACKTEST_CONNECTION")
            ?? throw new ArgumentException("Pass --connection or set YO4X_BACKTEST_CONNECTION.");
        string credentialKey = Required(arguments, "--credential-key");
        string host = Required(arguments, "--host");
        int port = int.Parse(Required(arguments, "--port"), CultureInfo.InvariantCulture);
        string artifact = Path.GetFullPath(Required(arguments, "--artifact"));
        string symbol = Option(arguments, "--symbol") ?? "EURUSD";
        string enableFile = Path.GetFullPath(Required(arguments, "--enable-file"));
        string dataRoot = Path.GetFullPath(Option(arguments, "--data-root") ?? DefaultDataRoot());
        string server = Option(arguments, "--server") ?? "VantageMarkets-Demo";
        string corpusRoot = Path.GetFullPath(Option(arguments, "--corpus-root") ?? Path.Combine("Testing", "Mq5"));
        string timeframe = Option(arguments, "--timeframe") ?? "H1";
        int minutes = int.Parse(Option(arguments, "--minutes") ?? "60", CultureInfo.InvariantCulture);
        int seconds = int.Parse(Option(arguments, "--seconds") ?? "300", CultureInfo.InvariantCulture);

        await using var database = new NpgsqlConnection(connectionString);
        await database.OpenAsync().ConfigureAwait(false);

        IReadOnlyList<Selection> chosen = await SelectProfitableAsync(database).ConfigureAwait(false);
        if (chosen.Count == 0)
        {
            Console.Error.WriteLine("No completed backtest shows a positive result, so nothing will be started.");
            return 3;
        }

        Console.WriteLine("strategies selected from recorded backtests:");
        foreach (Selection selection in chosen)
        {
            Console.WriteLine(
                $"  {selection.Name,-34} net {selection.NetProfit,10:F2}  "
                + $"dd {selection.Drawdown,6:F2}%  pf {selection.ProfitFactor,8:F2}  "
                + $"trades {selection.Trades,5}  data {selection.DataQuality,5:F1}%");
        }

        Console.WriteLine();
        Console.WriteLine(
            "  NOTE: these figures come from a backtest over partial history. A positive");
        Console.WriteLine(
            "  backtest is not evidence of edge, and two strategies in this same corpus");
        Console.WriteLine("  emptied their accounts entirely.");

        string csv = Path.Combine(dataRoot, server, symbol, timeframe + ".csv");
        if (!File.Exists(csv))
        {
            Console.Error.WriteLine($"No seed history at {csv}. Download it before starting bots.");
            return 4;
        }

        List<Mql5Bar> seed = [.. new Mql5CsvMarketFeed(csv, symbol).ReadBars()];
        Console.WriteLine();
        Console.WriteLine($"seed history      : {seed.Count} {timeframe} bars from {Path.GetFileName(csv)}");

        var vault = new DpapiLocalMt5CredentialVault(DpapiLocalMt5CredentialVault.GetDefaultVaultRoot());
        using LocalMt5Credential? credential = await vault
            .OpenAsync(credentialKey, CancellationToken.None).ConfigureAwait(false);
        if (credential is null)
        {
            Console.Error.WriteLine("No credential is stored under that key.");
            return 5;
        }

        // Only the first selection is started. Several strategies on one account would net
        // each other's positions against one another and neither result would mean anything.
        Selection run = chosen[0];
        string sourcePath = Path.Combine(corpusRoot, run.Name);
        if (!File.Exists(sourcePath))
        {
            Console.Error.WriteLine("The strategy source is not on disk: " + sourcePath);
            return 6;
        }

        Guid botId = await UpsertBotAsync(database, run, symbol, "STARTING").ConfigureAwait(false);
        Console.WriteLine($"bot recorded      : {botId} (visible in the frontend under My bots)");

        string password = credential.UsePassword(Encoding.UTF8.GetString);
        using var broker = Mt5NetApiDemoTradeClient.Create(
            artifact, credential.Login, password, host, port, symbol, enableFile,
            line => Console.WriteLine("  " + line), Mt5TradingEnvironment.Demo);
        broker.SetConnectTimeout(60_000);
        broker.Connect();
        broker.StartQuoteStream();

        await SetStatusAsync(database, botId, "RUNNING", null).ConfigureAwait(false);
        Console.WriteLine($"running           : {run.Name} on {symbol} {timeframe} for {seconds}s");
        Console.WriteLine();

        var runner = new LiveStrategyRunner(
            new RoslynMql5CompilationHost(),
            line => Console.WriteLine("  " + line));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        LiveRunOutcome outcome = await runner.RunAsync(
            new Mql5SourceDocument(run.Name, File.ReadAllBytes(sourcePath)),
            broker,
            seed,
            minutes,
            symbol.Contains("XAU", StringComparison.OrdinalIgnoreCase) ? 2 : 5,
            stop.Token).ConfigureAwait(false);

        string status = outcome.Reason switch
        {
            LiveStopReason.Requested => "STOPPED",
            LiveStopReason.Faulted => "FAULTED",
            _ => "FAULTED",
        };
        await SetStatusAsync(database, botId, status, outcome.Detail).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine($"stopped           : {outcome.Reason}{(outcome.Detail is null ? "" : " — " + outcome.Detail)}");
        Console.WriteLine($"bars closed       : {outcome.BarsSeen}");
        Console.WriteLine($"positions open    : {outcome.OrdersSent}");
        return 0;
    }

    /// <summary>
    /// The strategies whose recorded backtest finished with a positive result, best first.
    /// </summary>
    private static async Task<IReadOnlyList<Selection>> SelectProfitableAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            """
            select strategy.id, strategy.name, backtest.net_profit_amount,
                   backtest.max_drawdown_percent, backtest.profit_factor,
                   backtest.trade_count, coalesce(backtest.data_quality_percent, 0),
                   backtest.tenant_id, backtest.user_id
            from simulation.backtests as backtest
            join catalog.strategies as strategy
              on strategy.tenant_id = backtest.tenant_id
             and strategy.id = backtest.strategy_id
            where backtest.status = 'COMPLETE'
              and backtest.net_profit_amount > 0
              and backtest.trade_count > 0
            order by backtest.net_profit_amount desc
            """,
            connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var chosen = new List<Selection>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            chosen.Add(new Selection(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetDecimal(2),
                reader.GetDecimal(3),
                reader.GetDecimal(4),
                reader.GetInt32(5),
                reader.GetDecimal(6),
                reader.GetGuid(7),
                reader.GetGuid(8)));
        }

        return chosen;
    }

    private static async Task<Guid> UpsertBotAsync(
        NpgsqlConnection connection,
        Selection selection,
        string symbol,
        string status)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into bots.bots
                (id, tenant_id, user_id, strategy_id, name, symbol, risk_label, status, host)
            values
                (@id, @tenant_id, @user_id, @strategy_id, @name, @symbol, @risk, @status, 'LOCAL')
            on conflict (id) do update set status = excluded.status, updated_at = clock_timestamp()
            returning id
            """,
            connection);
        // Deterministic per strategy and symbol, so restarting a bot updates its row rather
        // than filling the list with duplicates of the same thing.
        Guid botId = Deterministic(selection.StrategyId, symbol);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, botId);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, selection.TenantId);
        command.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, selection.UserId);
        command.Parameters.AddWithValue("strategy_id", NpgsqlDbType.Uuid, selection.StrategyId);
        command.Parameters.AddWithValue("name", NpgsqlDbType.Text, Trim(selection.Name, 200));
        command.Parameters.AddWithValue("symbol", NpgsqlDbType.Text, symbol);
        command.Parameters.AddWithValue("risk", NpgsqlDbType.Text, "0.01 lots, demo only");
        command.Parameters.AddWithValue("status", NpgsqlDbType.Text, status);
        return (Guid)(await command.ExecuteScalarAsync().ConfigureAwait(false))!;
    }

    private static async Task SetStatusAsync(
        NpgsqlConnection connection,
        Guid botId,
        string status,
        string? detail)
    {
        await using var command = new NpgsqlCommand(
            "update bots.bots set status = @status, updated_at = clock_timestamp() where id = @id",
            connection);
        command.Parameters.AddWithValue("status", NpgsqlDbType.Text, status);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, botId);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        if (detail is { Length: > 0 })
        {
            Console.WriteLine("  status detail: " + detail);
        }
    }

    private static Guid Deterministic(Guid strategyId, string symbol)
    {
        byte[] material = Encoding.UTF8.GetBytes(strategyId.ToString("D") + "|" + symbol);
        byte[] digest = System.Security.Cryptography.SHA256.HashData(material);
        Span<byte> sixteen = digest.AsSpan(0, 16);
        sixteen[6] = (byte)((sixteen[6] & 0x0F) | 0x70);
        sixteen[8] = (byte)((sixteen[8] & 0x3F) | 0x80);
        return new Guid(sixteen);
    }

    private static string Trim(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];

    private static string DefaultDataRoot() => Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify),
        "YO4X",
        "marketdata");

    private static string? Option(string[] arguments, string option)
    {
        for (int index = 0; index < arguments.Length - 1; index++)
        {
            if (arguments[index].Equals(option, StringComparison.Ordinal))
            {
                return arguments[index + 1];
            }
        }

        return null;
    }

    private static string Required(string[] arguments, string option) =>
        Option(arguments, option) ?? throw new ArgumentException("Option '" + option + "' is required.");

    private sealed record Selection(
        Guid StrategyId,
        string Name,
        decimal NetProfit,
        decimal Drawdown,
        decimal ProfitFactor,
        int Trades,
        decimal DataQuality,
        Guid TenantId,
        Guid UserId);
}
