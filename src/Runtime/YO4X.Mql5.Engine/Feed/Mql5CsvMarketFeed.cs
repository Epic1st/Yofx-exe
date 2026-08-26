using System.Globalization;

namespace YO4X.Mql5.Engine.Feed;

/// <summary>
/// Reads OHLC bars from a CSV source. Accepts comma, semicolon or tab separated rows with the
/// columns <c>time,open,high,low,close[,tickvolume[,spread]]</c>. A leading header row is detected
/// and skipped. Parsing is invariant-culture only, so the same file yields the same bars on any
/// machine.
/// </summary>
public sealed class Mql5CsvMarketFeed : IMql5MarketFeed
{
    private static readonly char[] Separators = [',', ';', '\t'];

    private static readonly string[] TimeFormats =
    [
        "yyyy.MM.dd HH:mm:ss",
        "yyyy.MM.dd HH:mm",
        "yyyy.MM.dd",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-dd",
    ];

    private readonly Func<IEnumerable<string>> lineSource;

    /// <summary>Initializes a feed that reads from a CSV file on disk.</summary>
    public Mql5CsvMarketFeed(string path, string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        Symbol = symbol;
        lineSource = () => File.ReadLines(path);
    }

    /// <summary>Initializes a feed that reads from an in-memory sequence of CSV lines.</summary>
    public Mql5CsvMarketFeed(IEnumerable<string> lines, string symbol)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        Symbol = symbol;
        lineSource = () => lines;
    }

    /// <inheritdoc />
    public string Symbol { get; }

    /// <summary>Gets or sets the default spread applied to rows that carry no spread column.</summary>
    public int DefaultSpreadPoints { get; init; }

    /// <inheritdoc />
    public IEnumerable<Mql5Bar> ReadBars()
    {
        foreach (string line in lineSource())
        {
            if (TryParseBar(line, DefaultSpreadPoints, out Mql5Bar bar))
            {
                yield return bar;
            }
        }
    }

    /// <summary>
    /// Parses a single CSV row. Returns <see langword="false"/> for blank lines, comment lines and
    /// header rows rather than throwing, so a malformed export degrades to fewer bars.
    /// </summary>
    internal static bool TryParseBar(string line, int defaultSpread, out Mql5Bar bar)
    {
        bar = default;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        string trimmed = line.Trim();
        if (trimmed.StartsWith('#') || trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        string[] fields = trimmed.Split(Separators, StringSplitOptions.TrimEntries);
        if (fields.Length < 5)
        {
            return false;
        }

        if (!TryParseTime(fields, out DateTime time, out int priceOffset))
        {
            return false;
        }

        if (fields.Length < priceOffset + 4)
        {
            return false;
        }

        if (!TryParseDouble(fields[priceOffset], out double open) ||
            !TryParseDouble(fields[priceOffset + 1], out double high) ||
            !TryParseDouble(fields[priceOffset + 2], out double low) ||
            !TryParseDouble(fields[priceOffset + 3], out double close))
        {
            return false;
        }

        long tickVolume = 0;
        if (fields.Length > priceOffset + 4 &&
            long.TryParse(fields[priceOffset + 4], NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedVolume))
        {
            tickVolume = parsedVolume;
        }

        int spread = defaultSpread;
        if (fields.Length > priceOffset + 5 &&
            int.TryParse(fields[priceOffset + 5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedSpread))
        {
            spread = parsedSpread;
        }

        bar = new Mql5Bar(time, open, high, low, close, tickVolume, spread);
        return true;
    }

    private static bool TryParseTime(string[] fields, out DateTime time, out int priceOffset)
    {
        // Layout B first: a bare date in column zero also parses on its own, so the date-only
        // reading must not win when column one holds the time rather than the open price.
        if (fields.Length >= 6 &&
            !TryParseDouble(fields[1], out _) &&
            TryParseTimestamp(fields[0] + " " + fields[1], out time))
        {
            priceOffset = 2;
            return true;
        }

        // Layout A: a single "date time" column.
        if (TryParseTimestamp(fields[0], out time))
        {
            priceOffset = 1;
            return true;
        }

        priceOffset = 0;
        return false;
    }

    private static bool TryParseTimestamp(string value, out DateTime time)
    {
        if (DateTime.TryParseExact(
                value,
                TimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime parsed))
        {
            time = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            return true;
        }

        time = default;
        return false;
    }

    private static bool TryParseDouble(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
}
