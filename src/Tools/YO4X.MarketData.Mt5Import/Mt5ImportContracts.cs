namespace YO4X.MarketData.Mt5Import;

/// <summary>
/// The recognised shapes of an exported MT5 tick file. Any other header fails closed.
/// </summary>
internal enum Mt5ExportLayout
{
    /// <summary>The MetaTrader 5 "Export Ticks" table with angle-bracket column tokens.</summary>
    Mt5TabularExport,

    /// <summary>A bare <c>datetime,bid,ask</c> table.</summary>
    BareDateTimeBidAsk
}

/// <summary>A single accepted quote observation, carried in original file order.</summary>
internal sealed record Mt5QuoteRow(
    long Sequence,
    long LineNumber,
    DateTime TimestampUtc,
    decimal Bid,
    decimal Ask,
    string? FlagsText);

/// <summary>An aggregated count of rows refused for one stated reason.</summary>
internal sealed record Mt5RejectionTally(string Reason, int Count, long FirstLineNumber);

/// <summary>The result of decoding one MT5 export file.</summary>
internal sealed record Mt5ParsedExport(
    Mt5ExportLayout Layout,
    string DelimiterName,
    string HeaderLine,
    IReadOnlyList<string> Columns,
    bool FlagsColumnPresent,
    long DataLineCount,
    IReadOnlyList<Mt5QuoteRow> Rows,
    IReadOnlyList<Mt5RejectionTally> Rejections);

/// <summary>A point in the trading week, expressed in UTC.</summary>
internal sealed record Mt5WeekInstant(DayOfWeek Day, TimeSpan TimeOfDay)
{
    internal int SecondsOfWeek => ((int)Day * 86400) + (int)TimeOfDay.TotalSeconds;

    internal string Text => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"{Day}:{TimeOfDay.Hours:D2}:{TimeOfDay.Minutes:D2}");
}

/// <summary>The fully validated request for one import run.</summary>
internal sealed record Mt5ImportOptions(
    string InputPath,
    string Symbol,
    string LeanTicker,
    string Market,
    string SecurityTypeFolder,
    string DataRoot,
    string ReportPath,
    string? ReportTextPath,
    TimeSpan ServerUtcOffset,
    string ServerUtcOffsetText,
    TimeSpan GapThreshold,
    int MaximumOutOfOrderRows,
    int MaximumRejectedRows,
    Mt5WeekInstant SessionOpenUtc,
    Mt5WeekInstant SessionCloseUtc,
    bool Overwrite,
    bool DryRun);

/// <summary>Per symbol-day fidelity measurements. Every field is measured, never asserted.</summary>
internal sealed record Mt5DayEvidence
{
    internal required DateOnly Date { get; init; }

    internal required string LeanRelativePath { get; init; }

    internal required string ZipEntryName { get; init; }

    internal string? ZipSha256 { get; init; }

    internal required int TickCount { get; init; }

    internal required DateTime FirstTickUtc { get; init; }

    internal required DateTime LastTickUtc { get; init; }

    internal required decimal ObservedSpanSeconds { get; init; }

    internal required decimal ObservedSpanRatioOfDay { get; init; }

    internal required decimal CoverageRatioWithinObservedSpan { get; init; }

    internal required int GapCount { get; init; }

    internal required decimal GapSecondsTotal { get; init; }

    internal required decimal LargestGapSeconds { get; init; }

    internal DateTime? LargestGapFromUtc { get; init; }

    internal DateTime? LargestGapToUtc { get; init; }

    internal required decimal MinimumSpread { get; init; }

    internal required decimal MedianSpread { get; init; }

    internal required decimal MaximumSpread { get; init; }

    internal required decimal MeanSpread { get; init; }

    internal required int NonPositiveSpreadCount { get; init; }

    internal required decimal NonPositiveSpreadRatio { get; init; }

    internal required int OutOfOrderCount { get; init; }

    internal required int DuplicateTimestampCount { get; init; }

    internal required int OutsideDeclaredSessionTickCount { get; init; }

    internal required int WeekendTickCount { get; init; }

    internal required int SpanMinutes { get; init; }

    internal required int MinutesWithTicks { get; init; }

    internal required int EmptyMinutesWithinSpan { get; init; }

    internal required string QualityGrade { get; init; }

    internal required IReadOnlyList<string> QualityGradeReasons { get; init; }
}

/// <summary>The complete evidence set for one import run, before canonical serialisation.</summary>
internal sealed record Mt5ImportEvidence(
    Mt5ImportOptions Options,
    string InputFileName,
    string InputSha256,
    long InputByteCount,
    Mt5ParsedExport Export,
    IReadOnlyList<Mt5DayEvidence> Days,
    int AcceptedRowCount,
    int RejectedRowCount,
    int WrittenRowCount,
    int TotalOutOfOrderRows,
    int TotalDuplicateTimestampRows,
    IReadOnlyDictionary<string, int> FlagHistogram);
