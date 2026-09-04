using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using YO4X.LocalSecrets.Windows;
using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Live;
using YO4X.Mt5.ConnectionProbe.Windows;
using YO4X.StrategyGovernance.Licensing;
using YO4X.StrategyGovernance.Packaging;

namespace YO4X.LiveBots;

/// <summary>
/// Runs an authenticated .yo4x package against one demo broker account. This executable has
/// no raw-source compilation path: production-like execution starts from a signed package.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] arguments)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        Console.Error.WriteLine(
            "Direct live-bot execution is disabled. Start bots through the authenticated control plane.");
        return 2;
    }

    private static async Task<int> RunAsync(string[] arguments)
    {
        string connectionString = Option(arguments, "--connection")
            ?? Environment.GetEnvironmentVariable("YO4X_BACKTEST_CONNECTION")
            ?? throw new ArgumentException("Pass --connection or set YO4X_BACKTEST_CONNECTION.");
        string credentialKey = Required(arguments, "--credential-key");
        string host = Required(arguments, "--host");
        int port = int.Parse(Required(arguments, "--port"), CultureInfo.InvariantCulture);
        string artifact = ExistingFile(Required(arguments, "--artifact"));
        string packagePath = ExistingFile(Required(arguments, "--package"));
        string enableFile = ExistingFile(Required(arguments, "--enable-file"));
        string publicKeyPem = File.ReadAllText(ExistingFile(Required(arguments, "--license-public-key")));
        byte[] aesKey = ReadSecretKey(Required(arguments, "--package-aes-key"));
        byte[] hmacKey = ReadSecretKey(Required(arguments, "--package-hmac-key"));
        Guid tenantId = Guid.Parse(Required(arguments, "--tenant"));
        Guid userId = Guid.Parse(Required(arguments, "--user"));
        string expectedStrategyId = Required(arguments, "--strategy-package-id");
        string expectedStrategyVersion = Required(arguments, "--strategy-version");
        string expectedAssemblySha256 = Required(arguments, "--assembly-sha256");
        string symbol = Option(arguments, "--symbol") ?? "EURUSD";
        string server = Option(arguments, "--server") ?? "VantageMarkets-Demo";
        string timeframe = Option(arguments, "--timeframe") ?? "H1";
        int minutes = int.Parse(Option(arguments, "--minutes") ?? "60", CultureInfo.InvariantCulture);
        int seconds = int.Parse(Option(arguments, "--seconds") ?? "300", CultureInfo.InvariantCulture);
        string dataRoot = Path.GetFullPath(Option(arguments, "--data-root") ?? DefaultDataRoot());

        try
        {
            var vault = new DpapiLocalMt5CredentialVault(DpapiLocalMt5CredentialVault.GetDefaultVaultRoot());
            using LocalMt5Credential? credential = await vault
                .OpenAsync(credentialKey, CancellationToken.None).ConfigureAwait(false);
            if (credential is null)
                throw new InvalidOperationException("The requested broker credential is unavailable.");

            byte[] packageBytes = File.ReadAllBytes(packagePath);
            var context = new StrategyLicenseValidationContext(
                tenantId,
                userId,
                expectedStrategyId,
                expectedStrategyVersion,
                expectedAssemblySha256,
                credential.Login,
                server,
                DateTimeOffset.UtcNow);
            (Yo4xStrategyManifest manifest, byte[] assemblyBytes) =
                Yo4xStrategyPackage.UnpackAndValidate(
                    packageBytes,
                    context,
                    publicKeyPem,
                    aesKey,
                    hmacKey);
            try
            {
                RequireDeclaredMarket(manifest, symbol, timeframe);
                await using var database = new NpgsqlConnection(connectionString);
                await database.OpenAsync().ConfigureAwait(false);
                Selection run = await SelectStrategyAsync(
                        database,
                        manifest.Name,
                        tenantId,
                        userId)
                    .ConfigureAwait(false);

                string csv = Path.Combine(dataRoot, server, symbol, timeframe + ".csv");
                if (!File.Exists(csv))
                    throw new InvalidOperationException("Seed market history is unavailable.");
                List<Mql5Bar> seed = [.. new Mql5CsvMarketFeed(csv, symbol).ReadBars()];

                Console.WriteLine($"licensed package  : {manifest.Name} {manifest.Version}");
                Console.WriteLine($"assembly digest   : {manifest.AssemblySha256}");
                Console.WriteLine(
                    $"backtest evidence : net {run.NetProfit:F2}, dd {run.Drawdown:F2}%, "
                    + $"pf {run.ProfitFactor:F2}, trades {run.Trades}, data {run.DataQuality:F1}%");
                Console.WriteLine("  NOTE: positive backtest evidence is not evidence of future profitability.");

                Guid botId = await UpsertBotAsync(database, run, symbol, "STARTING")
                    .ConfigureAwait(false);
                string password = credential.UsePassword(Encoding.UTF8.GetString);
                using var broker = Mt5NetApiDemoTradeClient.Create(
                    artifact,
                    credential.Login,
                    password,
                    host,
                    port,
                    symbol,
                    enableFile,
                    line => Console.WriteLine("  " + line),
                    Mt5TradingEnvironment.Demo);
                broker.SetConnectTimeout(60_000);
                broker.Connect();
                broker.StartQuoteStream();

                await SetStatusAsync(database, botId, "RUNNING", null).ConfigureAwait(false);
                var runner = new LiveStrategyRunner(line => Console.WriteLine("  " + line));
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
                LiveRunOutcome outcome = await runner.RunPackagedAsync(
                        manifest,
                        assemblyBytes,
                        broker,
                        seed,
                        minutes,
                        symbol.Contains("XAU", StringComparison.OrdinalIgnoreCase) ? 2 : 5,
                        stop.Token)
                    .ConfigureAwait(false);

                string status = outcome.Reason == LiveStopReason.Requested ? "STOPPED" : "FAULTED";
                await SetStatusAsync(database, botId, status, outcome.Detail).ConfigureAwait(false);
                Console.WriteLine($"stopped           : {outcome.Reason}");
                Console.WriteLine($"bars closed       : {outcome.BarsSeen}");
                Console.WriteLine($"orders sent       : {outcome.OrdersSent}");
                return 0;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(assemblyBytes);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(aesKey);
            CryptographicOperations.ZeroMemory(hmacKey);
        }
    }

    private static void RequireDeclaredMarket(
        Yo4xStrategyManifest manifest,
        string symbol,
        string timeframe)
    {
        if (!manifest.SupportedSymbols.Contains(symbol, StringComparer.OrdinalIgnoreCase)
            || !manifest.SupportedTimeframes.Contains(timeframe, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The authenticated package does not declare the requested symbol and timeframe.");
        }
    }

    private static async Task<Selection> SelectStrategyAsync(
        NpgsqlConnection connection,
        string packageStrategyName,
        Guid tenantId,
        Guid userId)
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
              and backtest.tenant_id = @tenant_id
              and backtest.user_id = @user_id
              and (strategy.name = @strategy_name or strategy.name = @strategy_file_name)
            order by backtest.net_profit_amount desc
            limit 1
            """,
            connection);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
        command.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, userId);
        command.Parameters.AddWithValue("strategy_name", NpgsqlDbType.Text, packageStrategyName);
        command.Parameters.AddWithValue("strategy_file_name", NpgsqlDbType.Text, packageStrategyName + ".mq5");
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        if (!await reader.ReadAsync().ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "The licensed package has no matching positive completed backtest for this tenant and user.");
        }

        return new Selection(
            reader.GetGuid(0), reader.GetString(1), reader.GetDecimal(2), reader.GetDecimal(3),
            reader.GetDecimal(4), reader.GetInt32(5), reader.GetDecimal(6), reader.GetGuid(7),
            reader.GetGuid(8));
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
        Guid botId = Deterministic(selection.StrategyId, symbol);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, botId);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, selection.TenantId);
        command.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, selection.UserId);
        command.Parameters.AddWithValue("strategy_id", NpgsqlDbType.Uuid, selection.StrategyId);
        command.Parameters.AddWithValue("name", NpgsqlDbType.Text, Trim(selection.Name, 200));
        command.Parameters.AddWithValue("symbol", NpgsqlDbType.Text, symbol);
        command.Parameters.AddWithValue("risk", NpgsqlDbType.Text, "licensed package, demo only");
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
            Console.WriteLine("  status detail: " + detail);
    }

    private static Guid Deterministic(Guid strategyId, string symbol)
    {
        byte[] material = Encoding.UTF8.GetBytes(strategyId.ToString("D") + "|" + symbol);
        byte[] digest = SHA256.HashData(material);
        Span<byte> sixteen = digest.AsSpan(0, 16);
        sixteen[6] = (byte)((sixteen[6] & 0x0F) | 0x70);
        sixteen[8] = (byte)((sixteen[8] & 0x3F) | 0x80);
        return new Guid(sixteen);
    }

    private static byte[] ReadSecretKey(string path)
    {
        string encoded = File.ReadAllText(ExistingFile(path)).Trim();
        byte[] key = Convert.FromBase64String(encoded);
        if (key.Length != 32 || !string.Equals(Convert.ToBase64String(key), encoded, StringComparison.Ordinal))
        {
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidDataException("A package key file is invalid.");
        }
        return key;
    }

    private static string ExistingFile(string value)
    {
        string path = Path.GetFullPath(value);
        return File.Exists(path) ? path : throw new FileNotFoundException("A required file was not found.", path);
    }

    private static string Trim(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];

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
                return arguments[index + 1];
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
