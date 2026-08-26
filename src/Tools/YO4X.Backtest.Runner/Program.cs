using System.Globalization;
using Npgsql;
using NpgsqlTypes;
using YO4X.Mql5.Backtest;
using YO4X.Mql5.Compilation;
using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Hosting;
using YO4X.Mql5.Engine.Trading;
using YO4X.StrategyGovernance;

namespace YO4X.Backtest.Runner;

/// <summary>
/// Executes queued backtests: claims a request, compiles the strategy it names, replays it
/// over downloaded bars, and writes back what the run measured.
///
/// <para>
/// Nothing is invented when something is missing. A request whose market data is absent,
/// whose source will not compile, or whose strategy refuses to initialise is recorded as
/// failed with the reason, not completed with zeroes — a zero in a profit column is
/// indistinguishable from a strategy that broke even, and that ambiguity is exactly what a
/// results table must not carry.
/// </para>
/// </summary>
internal static class Program
{
    /// <summary>numeric(8,2) tops out here; a run with no losing trade has no finite factor.</summary>
    private const decimal MaximumProfitFactor = 999999.99m;

    /// <summary>
    /// How many strided equity samples a run may store, before the final sample is added
    /// back. 009_backtest_equity_curve.sql explains the bound: a tick-level curve is
    /// unbounded, the detail page reads a whole curve in one request, and the plot it is
    /// drawn on is 760 viewBox units wide, so 2000 points is already denser than the
    /// polyline can resolve. Nothing is truncated to reach it — the stride that produced
    /// the stored series is written to the row alongside the untouched sample count.
    /// </summary>
    private const int StoredEquityPointLimit = 2_000;

    /// <summary>The largest magnitude simulation.backtest_equity_points.equity can hold.</summary>
    private const decimal MaximumStorableEquity = 99_999_999_999_999.9999m;

    private static async Task<int> Main(string[] arguments)
    {
        try
        {
            return await RunAsync(arguments).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or InvalidDataException
            or NpgsqlException)
        {
            Console.Error.WriteLine("Backtest runner failed: " + exception.Message);
            return 2;
        }
    }

    private static async Task<int> RunAsync(string[] arguments)
    {
        if (arguments.Contains("--help", StringComparer.Ordinal))
        {
            WriteUsage();
            return 0;
        }

        string connectionString = Option(arguments, "--connection")
            ?? Environment.GetEnvironmentVariable("YO4X_BACKTEST_CONNECTION")
            ?? throw new ArgumentException(
                "Pass --connection or set YO4X_BACKTEST_CONNECTION.");
        string corpusRoot = Path.GetFullPath(
            Option(arguments, "--corpus-root") ?? Path.Combine("Testing", "Mq5"));
        string manifestPath = Path.GetFullPath(
            Option(arguments, "--manifest")
            ?? Path.Combine("docs", "backend", "mq5-static-manifest.v1.json"));
        string dataRoot = Path.GetFullPath(
            Option(arguments, "--data-root") ?? DefaultDataRoot());
        string server = Option(arguments, "--server") ?? "VantageMarkets-Demo";
        int limit = int.TryParse(
            Option(arguments, "--limit"),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out int parsed) ? parsed : int.MaxValue;

        StrategySourceResolver resolver = StrategySourceResolver.Load(manifestPath, corpusRoot);
        Console.WriteLine(
            $"corpus    : {resolver.Count} files, digest {resolver.CorpusSha256[..16]}…");
        Console.WriteLine($"data root : {dataRoot}  (server {server})");

        var host = new RoslynMql5CompilationHost();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        int completed = 0;
        int failed = 0;
        while (completed + failed < limit)
        {
            QueuedBacktest? claimed = await ClaimAsync(connection).ConfigureAwait(false);
            if (claimed is not { } request)
            {
                break;
            }

            Console.WriteLine();
            Console.WriteLine(
                $"claimed   : {request.Id}  {request.Symbol} {request.Timeframe} "
                + $"{request.PeriodStart:yyyy-MM-dd}..{request.PeriodEnd:yyyy-MM-dd}");

            BacktestOutcome outcome = Execute(request, resolver, dataRoot, server, host);
            await WriteBackAsync(connection, request, outcome).ConfigureAwait(false);
            if (outcome.Failure is null)
            {
                completed++;
                Console.WriteLine(
                    $"  COMPLETE  trades={outcome.TradeCount} net={outcome.NetProfit:F2} "
                    + $"dd={outcome.MaxDrawdownPercent:F2}% pf={outcome.ProfitFactor:F2} "
                    + $"data={outcome.DataQualityPercent:F1}%");
                Console.WriteLine(outcome.Equity is { } curve
                    ? $"  equity    {curve.SampleCount} samples measured, "
                        + $"{curve.Samples.Count} stored "
                        + (curve.DecimationInterval == 1
                            ? "(the whole series)"
                            : $"(1 in every {curve.DecimationInterval}, plus the last)")
                    : "  equity    no curve stored: the run produced no storable equity reading");
            }
            else
            {
                failed++;
                Console.WriteLine("  FAILED    " + outcome.Failure);
            }
        }

        Console.WriteLine();
        Console.WriteLine($"done      : {completed} completed, {failed} failed");
        return 0;
    }

    /// <summary>
    /// Takes one queued request, marking it running in the same statement so two runners
    /// cannot claim the same row. The database supplies the instant; a clock read on this
    /// side would let a skewed machine backdate a run.
    /// </summary>
    private static async Task<QueuedBacktest?> ClaimAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            """
            with claimable as (
                select backtest.id
                from simulation.backtests as backtest
                where backtest.status = 'QUEUED'
                order by backtest.created_at, backtest.id
                for update skip locked
                limit 1
            )
            update simulation.backtests as backtest
            set status = 'RUNNING'
            from claimable
            where backtest.id = claimable.id
            returning backtest.id, backtest.tenant_id, backtest.strategy_id,
                      backtest.symbol, backtest.timeframe, backtest.model,
                      backtest.period_start, backtest.period_end
            """,
            connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        if (!await reader.ReadAsync().ConfigureAwait(false))
        {
            return null;
        }

        return new QueuedBacktest(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
            reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
            reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
            DateOnly.FromDateTime(reader.GetDateTime(6)),
            DateOnly.FromDateTime(reader.GetDateTime(7)));
    }

    private static BacktestOutcome Execute(
        QueuedBacktest request,
        StrategySourceResolver resolver,
        string dataRoot,
        string server,
        RoslynMql5CompilationHost host)
    {
        if (!resolver.TryRead(request.StrategyId, out CorpusFile file, out byte[] content, out string? refusal))
        {
            return BacktestOutcome.Refused(refusal ?? "The strategy source could not be resolved.");
        }

        string csv = Path.Combine(dataRoot, server, request.Symbol, request.Timeframe + ".csv");
        if (!File.Exists(csv))
        {
            return BacktestOutcome.Refused(
                $"No market data for {request.Symbol} {request.Timeframe} on this machine. "
                + $"Download it first; expected {csv}.");
        }

        if (!TryPeriodMinutes(request.Timeframe, out int periodMinutes))
        {
            return BacktestOutcome.Refused($"Unsupported timeframe '{request.Timeframe}'.");
        }

        // The requested modelling fidelity is honoured or the request is refused. The engine
        // replays one strategy tick per bar; it does not reconstruct intrabar ticks, and it
        // has no tick series to replay. Recording a tick-model run that was really executed
        // bar by bar would put a fidelity claim in the results table that nothing performed.
        if (!CanModel(request.Model, out string? unsupportedModel))
        {
            return BacktestOutcome.Refused(unsupportedModel!);
        }

        var window = new List<Mql5Bar>();
        var feed = new Mql5CsvMarketFeed(csv, request.Symbol);
        DateTime start = request.PeriodStart.ToDateTime(TimeOnly.MinValue);
        DateTime end = request.PeriodEnd.ToDateTime(TimeOnly.MaxValue);
        foreach (Mql5Bar bar in feed.ReadBars())
        {
            if (bar.Time >= start && bar.Time <= end)
            {
                window.Add(bar);
            }
        }

        if (window.Count == 0)
        {
            return BacktestOutcome.Refused(
                $"The downloaded {request.Symbol} {request.Timeframe} series holds no bars inside "
                + $"{request.PeriodStart:yyyy-MM-dd}..{request.PeriodEnd:yyyy-MM-dd}.");
        }

        decimal coverage = MeasureCoverage(window, periodMinutes);
        var options = new Mql5RunOptions
        {
            Symbol = new Mql5SymbolSpec
            {
                Name = request.Symbol,
                Digits = InferDigits(window),
            },
            InitialDeposit = 10_000.0,
        };

        Mql5BacktestResult result = Mql5BacktestRunner.Run(
            new Mql5SourceDocument(file.RelativePath, content),
            new ListFeed(request.Symbol, window),
            options,
            periodMinutes,
            host);
        if (!result.Succeeded || result.Report is null)
        {
            return BacktestOutcome.Refused(Truncate(result.Explain(), 2000));
        }

        Mql5RunReport report = result.Report;
        return new BacktestOutcome(
            (decimal)(report.FinalBalance - report.InitialDeposit),
            ClampDrawdown((decimal)report.MaxDrawdownPercent),
            ClampProfitFactor(report.ProfitFactor),
            report.TotalTrades,
            coverage,
            Truncate(DescribeFidelity(csv, request.Timeframe, window.Count), 200),
            null,
            BuildEquityCurve(report));
    }

    /// <summary>
    /// Prepares the run's equity curve for storage without hiding anything about it.
    ///
    /// <para>
    /// A curve carries one sample per processed tick and is unbounded, so at most
    /// <see cref="StoredEquityPointLimit"/> strided samples are kept. The stride is
    /// written to the row next to the untouched sample count, and each stored sample
    /// keeps the index it came from, so a reader can see precisely which samples survived.
    /// The first and the final sample are always kept: the final sample is the equity the
    /// recorded net profit is computed from.
    /// </para>
    ///
    /// <para>
    /// Returns null when the run produced no samples, or produced a value the equity
    /// column cannot hold. Storing a partial or coerced curve under a header that claims
    /// otherwise would put a shape in the results table that the run did not measure, so
    /// nothing is stored at all and the outcome columns are written on their own.
    /// </para>
    /// </summary>
    private static EquityCurve? BuildEquityCurve(Mql5RunReport report)
    {
        IReadOnlyList<double> curve = report.EquityCurve;
        if (curve.Count == 0 || !double.IsFinite(report.InitialDeposit))
        {
            return null;
        }

        int stride = curve.Count <= StoredEquityPointLimit
            ? 1
            : ((curve.Count + StoredEquityPointLimit) - 1) / StoredEquityPointLimit;

        var samples = new List<EquitySample>();
        int last = curve.Count - 1;
        for (int source = 0; source < curve.Count; source += stride)
        {
            if (!TryStorableEquity(curve[source], out decimal equity))
            {
                return null;
            }

            samples.Add(new EquitySample(samples.Count, source, equity));
        }

        if (samples[^1].SourceOrdinal != last)
        {
            if (!TryStorableEquity(curve[last], out decimal equity))
            {
                return null;
            }

            samples.Add(new EquitySample(samples.Count, last, equity));
        }

        if (!TryStorableEquity(report.InitialDeposit, out decimal deposit))
        {
            return null;
        }

        return new EquityCurve(deposit, curve.Count, stride, samples);
    }

    /// <summary>
    /// Whether a measured equity reading fits <c>numeric(18,4)</c> exactly. A reading that
    /// does not is refused rather than clamped: a clamped equity is a number nothing
    /// measured, sitting in a column that says it did.
    /// </summary>
    private static bool TryStorableEquity(double value, out decimal equity)
    {
        equity = 0m;
        if (!double.IsFinite(value) || Math.Abs(value) > (double)MaximumStorableEquity)
        {
            return false;
        }

        equity = Math.Round((decimal)value, 4, MidpointRounding.ToEven);
        return Math.Abs(equity) <= MaximumStorableEquity;
    }

    /// <summary>
    /// The share of the bars a gap-free series would hold over the same span. This is
    /// measured from the data itself rather than assumed: a broker's demo history is often
    /// partial, and a backtest run over half a year of a year deserves to say so.
    /// </summary>
    private static decimal MeasureCoverage(List<Mql5Bar> window, int periodMinutes)
    {
        if (window.Count < 2)
        {
            return 0m;
        }

        double spanMinutes = (window[^1].Time - window[0].Time).TotalMinutes;
        if (spanMinutes <= 0)
        {
            return 0m;
        }

        // Weekends carry no bars for any instrument here, so they are removed from the
        // denominator; counting them would understate every series by two sevenths.
        int weekendDays = 0;
        for (DateTime day = window[0].Time.Date; day <= window[^1].Time.Date; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                weekendDays++;
            }
        }

        double expected = (spanMinutes - (weekendDays * 1440.0)) / periodMinutes;
        if (expected <= 0)
        {
            return 0m;
        }

        decimal measured = (decimal)(window.Count / expected) * 100m;
        return Math.Clamp(Math.Round(measured, 2), 0m, 100m);
    }

    /// <summary>The price precision the series actually carries.</summary>
    private static int InferDigits(List<Mql5Bar> window)
    {
        int digits = 0;
        foreach (Mql5Bar bar in window)
        {
            string text = bar.Close.ToString("0.##########", CultureInfo.InvariantCulture);
            int point = text.IndexOf('.', StringComparison.Ordinal);
            if (point >= 0)
            {
                digits = Math.Max(digits, text.Length - point - 1);
            }
        }

        return Math.Clamp(digits, 1, 8);
    }

    private static decimal ClampDrawdown(decimal value) =>
        Math.Clamp(Math.Round(value, 2), 0m, 9999.99m);

    /// <summary>
    /// A run with winning trades and no losing ones has an infinite profit factor, which the
    /// column cannot hold. It is clamped to the column's maximum rather than reported as
    /// zero, because zero is the value for "no winning trades" — the opposite situation.
    /// </summary>
    private static decimal ClampProfitFactor(double value)
    {
        if (double.IsNaN(value) || value <= 0)
        {
            return 0m;
        }

        return double.IsInfinity(value) || value >= (double)MaximumProfitFactor
            ? MaximumProfitFactor
            : Math.Round((decimal)value, 2);
    }

    /// <summary>
    /// Records what the run measured. The outcome columns, the curve header and the curve
    /// points are written in one transaction: a row that says its series was 6188 samples
    /// long thinned by 4 must never be visible next to points from a different run, and a
    /// requeued request that is claimed again replaces its whole curve rather than merging
    /// into the previous one.
    /// </summary>
    private static async Task WriteBackAsync(
        NpgsqlConnection connection,
        QueuedBacktest request,
        BacktestOutcome outcome)
    {
        await using NpgsqlTransaction transaction = await connection
            .BeginTransactionAsync()
            .ConfigureAwait(false);

        await using (var clear = new NpgsqlCommand(
            """
            delete from simulation.backtest_equity_points
            where tenant_id = @tenant_id and backtest_id = @backtest_id
            """,
            connection,
            transaction))
        {
            clear.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, request.TenantId);
            clear.Parameters.AddWithValue("backtest_id", NpgsqlDbType.Uuid, request.Id);
            await clear.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await using (var command = new NpgsqlCommand(
            """
            update simulation.backtests
            set status = @status,
                net_profit_amount = @net_profit,
                max_drawdown_percent = @max_drawdown,
                profit_factor = @profit_factor,
                trade_count = @trade_count,
                data_quality_percent = @data_quality,
                data_quality_source = @data_source,
                failure_reason = @failure_reason,
                equity_initial_deposit = @equity_deposit,
                equity_sample_count = @equity_samples,
                equity_decimation_interval = @equity_stride,
                completed_at = clock_timestamp()
            where tenant_id = @tenant_id and id = @id
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue("status", NpgsqlDbType.Text,
                outcome.Failure is null ? "COMPLETE" : "FAILED");
            command.Parameters.AddWithValue("net_profit", NpgsqlDbType.Numeric,
                Math.Round(outcome.NetProfit, 2));
            command.Parameters.AddWithValue("max_drawdown", NpgsqlDbType.Numeric, outcome.MaxDrawdownPercent);
            command.Parameters.AddWithValue("profit_factor", NpgsqlDbType.Numeric, outcome.ProfitFactor);
            command.Parameters.AddWithValue("trade_count", NpgsqlDbType.Integer, outcome.TradeCount);
            command.Parameters.AddWithValue("data_quality", NpgsqlDbType.Numeric,
                outcome.DataQualitySource is null ? DBNull.Value : outcome.DataQualityPercent);
            command.Parameters.AddWithValue("data_source", NpgsqlDbType.Text,
                (object?)outcome.DataQualitySource ?? DBNull.Value);
            command.Parameters.AddWithValue("failure_reason", NpgsqlDbType.Text,
                (object?)outcome.Failure ?? DBNull.Value);
            command.Parameters.AddWithValue("equity_deposit", NpgsqlDbType.Numeric,
                outcome.Equity is null ? DBNull.Value : outcome.Equity.InitialDeposit);
            command.Parameters.AddWithValue("equity_samples", NpgsqlDbType.Integer,
                outcome.Equity is null ? DBNull.Value : outcome.Equity.SampleCount);
            command.Parameters.AddWithValue("equity_stride", NpgsqlDbType.Integer,
                outcome.Equity is null ? DBNull.Value : outcome.Equity.DecimationInterval);
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, request.TenantId);
            command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, request.Id);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        if (outcome.Equity is { } curve && curve.Samples.Count > 0)
        {
            await using var insert = new NpgsqlCommand(
                """
                insert into simulation.backtest_equity_points
                    (id, tenant_id, backtest_id, ordinal, source_ordinal, equity)
                select
                    sample.id, @tenant_id, @backtest_id,
                    sample.ordinal, sample.source_ordinal, sample.equity
                from unnest(@ids, @ordinals, @source_ordinals, @equities)
                    as sample(id, ordinal, source_ordinal, equity)
                """,
                connection,
                transaction);
            insert.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, request.TenantId);
            insert.Parameters.AddWithValue("backtest_id", NpgsqlDbType.Uuid, request.Id);
            insert.Parameters.AddWithValue(
                "ids",
                NpgsqlDbType.Array | NpgsqlDbType.Uuid,
                curve.Samples.Select(static _ => Guid.CreateVersion7()).ToArray());
            insert.Parameters.AddWithValue(
                "ordinals",
                NpgsqlDbType.Array | NpgsqlDbType.Integer,
                curve.Samples.Select(static sample => sample.Ordinal).ToArray());
            insert.Parameters.AddWithValue(
                "source_ordinals",
                NpgsqlDbType.Array | NpgsqlDbType.Integer,
                curve.Samples.Select(static sample => sample.SourceOrdinal).ToArray());
            insert.Parameters.AddWithValue(
                "equities",
                NpgsqlDbType.Array | NpgsqlDbType.Numeric,
                curve.Samples.Select(static sample => sample.Equity).ToArray());
            await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Whether the engine can execute the requested MetaTrader modelling mode.
    ///
    /// <para>
    /// The two tick modes need a price path inside each bar: <c>EVERY_TICK_REAL</c> replays
    /// the broker's recorded ticks, and <c>EVERY_TICK_M1</c> interpolates a path from
    /// minute bars. This engine does neither, so both are refused rather than quietly
    /// downgraded to a bar-close run whose result would carry their name.
    /// </para>
    /// </summary>
    private static bool CanModel(string model, out string? refusal)
    {
        switch (model.Trim().ToUpperInvariant())
        {
            case "OHLC_M1":
            case "OPEN_PRICES":
            case "":
                refusal = null;
                return true;
            case "EVERY_TICK_REAL":
                refusal = "This run asked for every real tick. No tick series has been "
                    + "downloaded for this symbol and the engine does not yet replay ticks, so "
                    + "the request is refused rather than run bar by bar under a tick label.";
                return false;
            case "EVERY_TICK_M1":
                refusal = "This run asked for modelled ticks interpolated from minute bars. "
                    + "The engine does not reconstruct an intrabar path, so the request is "
                    + "refused rather than run bar by bar under a tick label.";
                return false;
            default:
                refusal = $"Unknown modelling mode '{model}'.";
                return false;
        }
    }

    /// <summary>
    /// States the fidelity the run actually achieved, next to the file it read. The column is
    /// the only place a later reader can learn how the numbers were produced, so it records
    /// what happened rather than what was asked for.
    /// </summary>
    private static string DescribeFidelity(string csv, string timeframe, int barCount) =>
        $"{csv} — {barCount} {timeframe} bars, one strategy tick per bar close; "
        + "stops and pending orders evaluated on the intrabar path";

    private static bool TryPeriodMinutes(string timeframe, out int minutes)
    {
        minutes = timeframe.Trim().ToUpperInvariant() switch
        {
            "M1" => 1,
            "M5" => 5,
            "M15" => 15,
            "M30" => 30,
            "H1" => 60,
            "H4" => 240,
            "D1" => 1440,
            "W1" => 10080,
            _ => 0,
        };
        return minutes > 0;
    }

    private static string Truncate(string value, int maximum) =>
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

    private static void WriteUsage() => Console.Error.WriteLine(
        """
        usage: YO4X.Backtest.Runner
                   --connection <npgsql>       or set YO4X_BACKTEST_CONNECTION
                   [--corpus-root <dir>]       default Testing/Mq5
                   [--manifest <file>]         default docs/backend/mq5-static-manifest.v1.json
                   [--data-root <dir>]         default %LOCALAPPDATA%\\YO4X\\marketdata
                   [--server <name>]           default VantageMarkets-Demo
                   [--limit <n>]               stop after this many requests

        Claims queued backtests, compiles each strategy, replays it over downloaded bars,
        and writes back the measured result. A request whose data or source is missing is
        recorded as failed with the reason rather than completed with zeroes.
        """);

    private sealed record QueuedBacktest(
        Guid Id,
        Guid TenantId,
        Guid StrategyId,
        string Symbol,
        string Timeframe,
        string Model,
        DateOnly PeriodStart,
        DateOnly PeriodEnd);

    private sealed record BacktestOutcome(
        decimal NetProfit,
        decimal MaxDrawdownPercent,
        decimal ProfitFactor,
        int TradeCount,
        decimal DataQualityPercent,
        string? DataQualitySource,
        string? Failure,
        EquityCurve? Equity = null)
    {
        public static BacktestOutcome Refused(string reason) =>
            new(0m, 0m, 0m, 0, 0m, null, reason);
    }

    /// <summary>One stored sample: where it sits in the stored series, where it came from
    /// in the untouched one, and the account equity it recorded.</summary>
    private readonly record struct EquitySample(int Ordinal, int SourceOrdinal, decimal Equity);

    /// <summary>
    /// A run's equity curve as it will be stored: the deposit it started from, how many
    /// samples the run actually produced, the stride that survived, and the samples.
    /// <see cref="SampleCount"/> is the untouched length, never the stored length, so a
    /// reader is never told the series was shorter than it was.
    /// </summary>
    private sealed record EquityCurve(
        decimal InitialDeposit,
        int SampleCount,
        int DecimationInterval,
        IReadOnlyList<EquitySample> Samples);

    /// <summary>Replays an already-windowed bar list.</summary>
    private sealed class ListFeed(string symbol, IReadOnlyList<Mql5Bar> bars) : IMql5MarketFeed
    {
        public string Symbol { get; } = symbol;

        public IEnumerable<Mql5Bar> ReadBars() => bars;
    }
}
