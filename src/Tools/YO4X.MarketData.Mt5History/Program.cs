using System.Globalization;
using System.Text;
using YO4X.LocalSecrets.Windows;
using YO4X.Mt5.ConnectionProbe.Windows;

namespace YO4X.MarketData.Mt5History;

/// <summary>
/// Downloads historical bars from a broker through the pinned MetaTrader 5 network API and
/// writes them to this machine as CSV, in the exact column shape the engine's own
/// <c>Mql5CsvMarketFeed</c> already reads — so a downloaded file is a backtest input with no
/// converter in between.
///
/// <para>
/// No MetaTrader terminal is involved. The account password is read from the local DPAPI
/// vault at the moment of connection and is never accepted on the command line, written to
/// the output, or logged: a password on a command line is visible to every other process on
/// the machine.
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
            or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("History download failed: " + exception.Message);
            return 2;
        }
    }

    private static async Task<int> RunAsync(string[] arguments)
    {
        if (arguments.Length == 0 || arguments.Contains("--help", StringComparer.Ordinal))
        {
            WriteUsage();
            return arguments.Length == 0 ? 2 : 0;
        }

        string credentialKey = RequiredOption(arguments, "--credential-key");
        string symbol = RequiredOption(arguments, "--symbol");
        Mt5HistoryPeriod period = ParsePeriod(RequiredOption(arguments, "--timeframe"));
        DateTime from = ParseDate(RequiredOption(arguments, "--from"));
        DateTime to = ParseDate(RequiredOption(arguments, "--to")).AddDays(1).AddSeconds(-1);
        string host = RequiredOption(arguments, "--host");
        int port = ParsePort(RequiredOption(arguments, "--port"));
        string artifact = Path.GetFullPath(RequiredOption(arguments, "--artifact"));
        string dataRoot = Path.GetFullPath(
            OptionalOption(arguments, "--data-root") ?? DefaultDataRoot());
        int connectTimeoutMs = int.TryParse(
            OptionalOption(arguments, "--connect-timeout-ms"),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out int parsedTimeout) ? parsedTimeout : 30_000;

        if (to < from)
        {
            throw new ArgumentException("Option '--to' precedes '--from'.");
        }

        var vault = new DpapiLocalMt5CredentialVault(DpapiLocalMt5CredentialVault.GetDefaultVaultRoot());
        using LocalMt5Credential? credential = await vault
            .OpenAsync(credentialKey, CancellationToken.None)
            .ConfigureAwait(false);
        if (credential is null)
        {
            Console.Error.WriteLine(
                "No credential is stored under that key. Link the account first, or pass the key of one that is.");
            return 3;
        }

        Console.WriteLine(
            $"account   : {Mask(credential.Login)} on {credential.Server}");
        Console.WriteLine($"symbol    : {symbol}  {period} ({(int)period} minute bars)");
        Console.WriteLine(
            "range     : "
            + from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            + " .. "
            + to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        // The vendor constructor takes the password as a string, so it must exist in managed
        // memory for the length of the call. It is materialised as late as possible, used
        // once, and never stored on this side.
        int? serverOffsetMinutes = null;
        IReadOnlyList<Mt5HistoryBar> bars = credential.UsePassword(utf8 =>
        {
            string password = Encoding.UTF8.GetString(utf8);
            using Mt5NetApiQuoteHistoryClient client = Mt5NetApiQuoteHistoryClient.Create(
                artifact,
                credential.Login,
                password,
                host,
                port);
            client.SetConnectTimeout(connectTimeoutMs);
            Console.WriteLine("connecting…");
            client.Connect();
            Console.WriteLine(
                $"connected : {client.Connected} ({client.AccountCompanyName ?? "unnamed broker"})");
            serverOffsetMinutes = client.ServerTimeZoneInMinutes;
            return client.Download(symbol, from, to, period);
        });

        if (bars.Count == 0)
        {
            Console.Error.WriteLine(
                "The broker served no bars for that symbol, period and range. Nothing was written.");
            return 4;
        }

        // The requested period is checked against what actually arrived. The vendor exposes no
        // chart-period enumeration, so this is what turns "minutes is the unit" from an
        // assumption into a measurement.
        int observed = ObservedPeriodMinutes(bars);
        if (observed != (int)period)
        {
            Console.Error.WriteLine(
                $"The broker returned {observed}-minute bars for a {(int)period}-minute request. "
                + "Nothing was written, because the file would claim a period it does not hold.");
            return 5;
        }

        string path = WriteCsv(dataRoot, credential.Server, symbol, period, bars, serverOffsetMinutes);
        Console.WriteLine();
        Console.WriteLine($"wrote     : {path}");
        Console.WriteLine(
            $"bars      : {bars.Count.ToString(CultureInfo.InvariantCulture)}  "
            + $"{bars[0].Time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} .. "
            + $"{bars[^1].Time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} broker server time");
        return 0;
    }

    /// <summary>
    /// The most common gap between consecutive bars. The mode rather than the minimum, so a
    /// single duplicated timestamp cannot decide the answer, and rather than the mean, so
    /// weekends and holidays cannot drag it upwards.
    /// </summary>
    private static int ObservedPeriodMinutes(IReadOnlyList<Mt5HistoryBar> bars)
    {
        if (bars.Count < 2)
        {
            return 0;
        }

        var counts = new Dictionary<int, int>();
        for (int index = 1; index < bars.Count; index++)
        {
            int minutes = (int)Math.Round((bars[index].Time - bars[index - 1].Time).TotalMinutes);
            if (minutes <= 0)
            {
                continue;
            }

            counts[minutes] = counts.TryGetValue(minutes, out int seen) ? seen + 1 : 1;
        }

        int best = 0;
        int bestCount = 0;
        foreach ((int minutes, int seen) in counts)
        {
            if (seen > bestCount)
            {
                best = minutes;
                bestCount = seen;
            }
        }

        return best;
    }

    /// <summary>
    /// Writes the bars through a temporary file and one atomic move, so an interrupted run
    /// never leaves a half-written series where a complete one is expected.
    /// </summary>
    private static string WriteCsv(
        string dataRoot,
        string server,
        string symbol,
        Mt5HistoryPeriod period,
        IReadOnlyList<Mt5HistoryBar> bars,
        int? serverOffsetMinutes)
    {
        string directory = Path.Combine(dataRoot, Sanitize(server), Sanitize(symbol));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, period + ".csv");
        string staging = path + ".partial";

        // Provenance travels with the data. The CSV feed skips '#' lines, so these are inert
        // to the engine but keep the file from becoming an anonymous column of numbers whose
        // timezone nobody can later establish.
        var builder = new StringBuilder(bars.Count * 64);
        builder.Append("# broker ").Append(server).Append(" symbol ").Append(symbol)
            .Append(' ').Append(period).Append('\n');
        builder.Append("# timestamps are broker server time, not UTC; server offset from UTC: ")
            .Append(serverOffsetMinutes is { } offset
                ? offset.ToString(CultureInfo.InvariantCulture) + " minutes"
                : "not reported by the server")
            .Append('\n');
        builder.Append("time,open,high,low,close,tickvolume,spread\n");
        foreach (Mt5HistoryBar bar in bars)
        {
            builder
                .Append(bar.Time.ToString("yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(',')
                .Append(bar.Open.ToString("0.#########", CultureInfo.InvariantCulture)).Append(',')
                .Append(bar.High.ToString("0.#########", CultureInfo.InvariantCulture)).Append(',')
                .Append(bar.Low.ToString("0.#########", CultureInfo.InvariantCulture)).Append(',')
                .Append(bar.Close.ToString("0.#########", CultureInfo.InvariantCulture)).Append(',')
                .Append(bar.TickVolume.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(bar.Spread.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        File.WriteAllText(staging, builder.ToString(), new UTF8Encoding(false));
        File.Move(staging, path, overwrite: true);
        return path;
    }

    private static string DefaultDataRoot() => Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify),
        "YO4X",
        "marketdata");

    /// <summary>Keeps a broker or symbol name usable as one path segment.</summary>
    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char character in value.Trim())
        {
            builder.Append(
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'
                    ? character
                    : '-');
        }

        string sanitized = builder.ToString().Trim('-');
        return sanitized.Length == 0 ? "unnamed" : sanitized;
    }

    private static string Mask(ulong login)
    {
        string value = login.ToString(CultureInfo.InvariantCulture);
        return value.Length <= 2
            ? new string('*', value.Length)
            : new string('*', value.Length - 2) + value[^2..];
    }

    private static Mt5HistoryPeriod ParsePeriod(string value) =>
        Enum.TryParse(value.Trim(), ignoreCase: true, out Mt5HistoryPeriod period)
        && Enum.IsDefined(period)
            ? period
            : throw new ArgumentException(
                "Option '--timeframe' must be one of "
                + string.Join(", ", Enum.GetNames<Mt5HistoryPeriod>())
                + ".");

    private static DateTime ParseDate(string value) =>
        DateTime.TryParseExact(
            value.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTime parsed)
            ? parsed
            : throw new ArgumentException("Dates must be written as yyyy-MM-dd.");

    private static int ParsePort(string value) =>
        int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int port)
        && port is > 0 and <= 65535
            ? port
            : throw new ArgumentException("Option '--port' must be a TCP port number.");

    private static string RequiredOption(string[] arguments, string option) =>
        OptionalOption(arguments, option)
        ?? throw new ArgumentException("Option '" + option + "' is required.");

    private static string? OptionalOption(string[] arguments, string option)
    {
        int index = -1;
        for (int candidate = 0; candidate < arguments.Length; candidate++)
        {
            if (!arguments[candidate].Equals(option, StringComparison.Ordinal))
            {
                continue;
            }

            if (index >= 0)
            {
                throw new ArgumentException("Option '" + option + "' can be given only once.");
            }

            index = candidate;
        }

        if (index < 0)
        {
            return null;
        }

        if (index + 1 >= arguments.Length
            || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException("Option '" + option + "' has no value.");
        }

        return arguments[index + 1];
    }

    private static void WriteUsage() => Console.Error.WriteLine(
        """
        usage: YO4X.MarketData.Mt5History
                   --credential-key <64 hex>   the linked account's local vault key
                   --symbol <name>             e.g. EURUSD, XAUUSD
                   --timeframe <period>        M1 M5 M15 M30 H1 H4 D1 W1
                   --from <yyyy-MM-dd>         first day, inclusive
                   --to <yyyy-MM-dd>           last day, inclusive
                   --host <hostname>           broker access-server host
                   --port <number>             broker access-server port
                   --artifact <path>           the pinned mt5api.dll
                   [--data-root <directory>]   default %LOCALAPPDATA%\\YO4X\\marketdata
                   [--connect-timeout-ms <n>]  default 30000

        Downloads bars straight from the broker and writes
        <data-root>/<server>/<symbol>/<period>.csv, which the engine's CSV feed reads as is.
        The password is read from the local vault and never appears on the command line.
        """);
}
