using System.Globalization;

namespace YO4X.MarketData.Mt5Import;

/// <summary>
/// Decodes an exported MT5 tick file. The shape is detected from the header only; an
/// unrecognised header refuses the file rather than guessing a column order.
/// </summary>
internal static class Mt5TickExportReader
{
    internal const string RejectionEmptyLine = "EMPTY_LINE";
    internal const string RejectionColumnCount = "COLUMN_COUNT_MISMATCH";
    internal const string RejectionTimestamp = "UNPARSABLE_TIMESTAMP";
    internal const string RejectionBid = "UNPARSABLE_BID";
    internal const string RejectionAsk = "UNPARSABLE_ASK";
    internal const string RejectionQuoteAbsent = "QUOTE_ABSENT_TRADE_ONLY_ROW";
    internal const string RejectionNonPositivePrice = "NON_POSITIVE_PRICE";

    private const char ByteOrderMark = '﻿';

    private static readonly string[] TabularFormats =
    [
        "yyyy.MM.dd HH:mm:ss.fff",
        "yyyy.MM.dd HH:mm:ss",
        "yyyy.MM.dd HH:mm",
        "yyyy-MM-dd HH:mm:ss.fff",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy/MM/dd HH:mm:ss.fff",
        "yyyy/MM/dd HH:mm:ss",
        "yyyy/MM/dd HH:mm"
    ];

    private static readonly string[] BareFormats =
    [
        "yyyy-MM-dd HH:mm:ss.fff",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-ddTHH:mm:ss.fff",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm",
        "yyyy.MM.dd HH:mm:ss.fff",
        "yyyy.MM.dd HH:mm:ss",
        "yyyy.MM.dd HH:mm"
    ];

    private static readonly char[] CandidateDelimiters = ['\t', ',', ';'];

    private static readonly Dictionary<string, string> TabularColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["<DATE>"] = "date",
        ["<TIME>"] = "time",
        ["<BID>"] = "bid",
        ["<ASK>"] = "ask",
        ["<LAST>"] = "last",
        ["<VOLUME>"] = "volume",
        ["<VOL>"] = "volume",
        ["<FLAGS>"] = "flags",
        ["<SPREAD>"] = "spread",
        ["<TIME_MSC>"] = "timeMsc",
        ["<REAL_VOLUME>"] = "realVolume"
    };

    private static readonly HashSet<string> BareTimestampNames = new(
        ["datetime", "date_time", "timestamp", "time"],
        StringComparer.OrdinalIgnoreCase);

    internal static Mt5ParsedExport Read(string path, TimeSpan serverUtcOffset)
    {
        using var reader = new StreamReader(path);
        string headerLine = reader.ReadLine()
            ?? throw new ArgumentException("The MT5 export file is empty; there is no header to detect.");

        headerLine = headerLine.TrimStart(ByteOrderMark);
        Mt5HeaderShape shape = DetectShape(headerLine);

        var rows = new List<Mt5QuoteRow>();
        var rejections = new Dictionary<string, Mt5RejectionTally>(StringComparer.Ordinal);
        long lineNumber = 1;
        long dataLineCount = 0;
        long sequence = 0;

        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (line.AsSpan().Trim().IsEmpty)
            {
                Reject(rejections, RejectionEmptyLine, lineNumber);
                continue;
            }

            dataLineCount++;
            string[] fields = line.Split(shape.Delimiter);
            if (fields.Length < shape.MinimumFieldCount || fields.Length > shape.Columns.Count)
            {
                Reject(rejections, RejectionColumnCount, lineNumber);
                continue;
            }

            if (!TryReadTimestamp(shape, fields, serverUtcOffset, out DateTime timestampUtc))
            {
                Reject(rejections, RejectionTimestamp, lineNumber);
                continue;
            }

            string bidText = Field(fields, shape.BidIndex);
            string askText = Field(fields, shape.AskIndex);
            if (bidText.Length == 0 && askText.Length == 0)
            {
                Reject(rejections, RejectionQuoteAbsent, lineNumber);
                continue;
            }

            if (!TryReadPrice(bidText, out decimal bid))
            {
                Reject(rejections, RejectionBid, lineNumber);
                continue;
            }

            if (!TryReadPrice(askText, out decimal ask))
            {
                Reject(rejections, RejectionAsk, lineNumber);
                continue;
            }

            if (bid <= 0m || ask <= 0m)
            {
                // MT5 zeroes the quote columns on trade-only ticks. Those rows are not quote
                // observations; they are counted and dropped, never reconstructed from a neighbour.
                Reject(rejections, RejectionNonPositivePrice, lineNumber);
                continue;
            }

            sequence++;
            string? flagsText = shape.FlagsIndex >= 0 ? Field(fields, shape.FlagsIndex) : null;
            rows.Add(new Mt5QuoteRow(sequence, lineNumber, timestampUtc, bid, ask, flagsText));
        }

        Mt5RejectionTally[] ordered =
            [.. rejections.Values.OrderBy(static tally => tally.Reason, StringComparer.Ordinal)];
        return new Mt5ParsedExport(
            shape.Layout,
            shape.DelimiterName,
            headerLine,
            shape.Columns,
            shape.FlagsIndex >= 0,
            dataLineCount,
            rows,
            ordered);
    }

    private static string Field(string[] fields, int index) =>
        index >= 0 && index < fields.Length ? fields[index].Trim() : string.Empty;

    private static bool TryReadPrice(string text, out decimal value) =>
        decimal.TryParse(
            text,
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out value);

    private static bool TryReadTimestamp(
        Mt5HeaderShape shape,
        string[] fields,
        TimeSpan serverUtcOffset,
        out DateTime timestampUtc)
    {
        timestampUtc = default;
        string combined = shape.TimeIndex >= 0
            ? string.Concat(Field(fields, shape.DateIndex), " ", Field(fields, shape.TimeIndex))
            : Field(fields, shape.DateIndex);

        string[] formats = shape.Layout == Mt5ExportLayout.Mt5TabularExport ? TabularFormats : BareFormats;
        if (!DateTime.TryParseExact(
                combined,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime serverTime))
        {
            return false;
        }

        // The export carries broker-server wall-clock time. The caller states the offset; this tool
        // never infers it, and applies that single fixed offset to every row.
        timestampUtc = DateTime.SpecifyKind(serverTime - serverUtcOffset, DateTimeKind.Utc);
        return true;
    }

    private static void Reject(
        Dictionary<string, Mt5RejectionTally> rejections,
        string reason,
        long lineNumber)
    {
        if (rejections.TryGetValue(reason, out Mt5RejectionTally? existing))
        {
            rejections[reason] = existing with { Count = existing.Count + 1 };
            return;
        }

        rejections[reason] = new Mt5RejectionTally(reason, 1, lineNumber);
    }

    private static Mt5HeaderShape DetectShape(string headerLine)
    {
        foreach (char delimiter in CandidateDelimiters)
        {
            if (!headerLine.Contains(delimiter, StringComparison.Ordinal))
            {
                continue;
            }

            string[] tokens = [.. headerLine.Split(delimiter).Select(static token => token.Trim())];
            return tokens[0].StartsWith('<')
                ? DetectTabularShape(tokens, delimiter)
                : DetectBareShape(tokens, delimiter);
        }

        throw new ArgumentException(
            $"The header '{headerLine}' uses no recognised delimiter (tab, comma or semicolon).");
    }

    private static Mt5HeaderShape DetectTabularShape(string[] tokens, char delimiter)
    {
        var columns = new List<string>(tokens.Length);
        foreach (string token in tokens)
        {
            if (!TabularColumns.TryGetValue(token, out string? name))
            {
                throw new ArgumentException(
                    $"The MT5 export header contains the unrecognised column '{token}'. The import refuses to guess its meaning.");
            }

            columns.Add(name);
        }

        int dateIndex = columns.IndexOf("date");
        int timeIndex = columns.IndexOf("time");
        int bidIndex = columns.IndexOf("bid");
        int askIndex = columns.IndexOf("ask");
        if (dateIndex < 0 || timeIndex < 0 || bidIndex < 0 || askIndex < 0)
        {
            throw new ArgumentException(
                "The MT5 export header must carry <DATE>, <TIME>, <BID> and <ASK>; a quote cannot be formed without them.");
        }

        int highest = Math.Max(Math.Max(dateIndex, timeIndex), Math.Max(bidIndex, askIndex));
        return new Mt5HeaderShape(
            Mt5ExportLayout.Mt5TabularExport,
            delimiter,
            columns,
            dateIndex,
            timeIndex,
            bidIndex,
            askIndex,
            columns.IndexOf("flags"),
            highest + 1);
    }

    private static Mt5HeaderShape DetectBareShape(string[] tokens, char delimiter)
    {
        if (tokens.Length != 3
            || !BareTimestampNames.Contains(tokens[0])
            || !tokens[1].Equals("bid", StringComparison.OrdinalIgnoreCase)
            || !tokens[2].Equals("ask", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The header '{string.Join(delimiter, tokens)}' matches neither the MT5 tick export nor the bare datetime,bid,ask shape.");
        }

        return new Mt5HeaderShape(
            Mt5ExportLayout.BareDateTimeBidAsk,
            delimiter,
            ["datetime", "bid", "ask"],
            0,
            -1,
            1,
            2,
            -1,
            3);
    }

    private sealed record Mt5HeaderShape(
        Mt5ExportLayout Layout,
        char Delimiter,
        IReadOnlyList<string> Columns,
        int DateIndex,
        int TimeIndex,
        int BidIndex,
        int AskIndex,
        int FlagsIndex,
        int MinimumFieldCount)
    {
        internal string DelimiterName => Delimiter switch
        {
            '\t' => "tab",
            ',' => "comma",
            ';' => "semicolon",
            _ => "unknown"
        };
    }
}
