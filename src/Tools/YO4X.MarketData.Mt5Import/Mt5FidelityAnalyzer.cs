namespace YO4X.MarketData.Mt5Import;

/// <summary>
/// Measures the imported dataset. Nothing here repairs, interpolates or smooths the data; every
/// finding is counted and reported so that a reader can judge the dataset for themselves.
/// </summary>
internal static class Mt5FidelityAnalyzer
{
    internal const int GradeMinimumTickCount = 100;
    internal const decimal GradeCoverageFloorForD = 0.50m;
    internal const decimal GradeCoverageFloorForC = 0.80m;
    internal const decimal GradeCoverageFloorForB = 0.95m;
    internal const decimal GradeNonPositiveSpreadCeilingForC = 0.02m;
    internal const decimal GradeNonPositiveSpreadCeilingForD = 0.10m;

    internal const string GradeRule =
        "Each symbol-day starts at A and is demoted by every finding; the worst demotion wins and "
        + "all of them are listed in qualityGradeReasons. D: fewer than 100 ticks, coverage < 0.50, "
        + "or non-positive-spread ratio > 0.10. C: coverage < 0.80, non-positive-spread ratio > "
        + "0.02, any out-of-order row, or any tick outside the declared session. B: coverage < "
        + "0.95, any non-positive spread, any duplicate timestamp, or any gap beyond the stated "
        + "threshold. A: none of the above. Coverage is measured within the observed span only, not "
        + "against the whole calendar day; observedSpanRatioOfDay states how much of the day was "
        + "observed at all. The grade describes what this file contains and is not a statement "
        + "about how much of the history MT5 generated from M1 bars.";

    internal const string OrderingApplied = "stable-ascending-sort-by-utc-timestamp";

    private const decimal SecondsPerDay = 86400m;

    internal static IReadOnlyList<Mt5DayEvidence> Analyze(
        Mt5ImportOptions options,
        IReadOnlyList<Mt5QuoteRow> rows,
        IReadOnlyDictionary<DateOnly, string> zipDigests)
    {
        var days = new List<Mt5DayEvidence>();
        foreach (IGrouping<DateOnly, Mt5QuoteRow> group in rows
            .GroupBy(static row => DateOnly.FromDateTime(row.TimestampUtc))
            .OrderBy(static group => group.Key))
        {
            days.Add(AnalyzeDay(
                options,
                group.Key,
                [.. group],
                zipDigests.TryGetValue(group.Key, out string? digest) ? digest : null));
        }

        return days;
    }

    /// <summary>Counts rows whose timestamp regresses against the preceding row, in file order.</summary>
    internal static (int OutOfOrder, int Duplicates) CountOrderingDefects(IReadOnlyList<Mt5QuoteRow> fileOrder)
    {
        int outOfOrder = 0;
        int duplicates = 0;
        for (int index = 1; index < fileOrder.Count; index++)
        {
            int comparison = fileOrder[index].TimestampUtc.CompareTo(fileOrder[index - 1].TimestampUtc);
            if (comparison < 0)
            {
                outOfOrder++;
            }
            else if (comparison == 0)
            {
                duplicates++;
            }
        }

        return (outOfOrder, duplicates);
    }

    private static Mt5DayEvidence AnalyzeDay(
        Mt5ImportOptions options,
        DateOnly date,
        IReadOnlyList<Mt5QuoteRow> fileOrder,
        string? zipDigest)
    {
        (int outOfOrder, int duplicates) = CountOrderingDefects(fileOrder);
        Mt5QuoteRow[] ascending = [.. fileOrder.OrderBy(static row => row.TimestampUtc)];

        DateTime first = ascending[0].TimestampUtc;
        DateTime last = ascending[^1].TimestampUtc;
        decimal observedSpanSeconds = ToSeconds(last - first);

        int gapCount = 0;
        decimal gapSecondsTotal = 0m;
        TimeSpan largestGap = TimeSpan.Zero;
        DateTime? largestGapFrom = null;
        DateTime? largestGapTo = null;
        for (int index = 1; index < ascending.Length; index++)
        {
            TimeSpan gap = ascending[index].TimestampUtc - ascending[index - 1].TimestampUtc;
            if (gap > largestGap)
            {
                largestGap = gap;
                largestGapFrom = ascending[index - 1].TimestampUtc;
                largestGapTo = ascending[index].TimestampUtc;
            }

            if (gap > options.GapThreshold)
            {
                gapCount++;
                gapSecondsTotal += ToSeconds(gap);
            }
        }

        decimal[] spreads = [.. ascending.Select(static row => row.Ask - row.Bid).Order()];
        int nonPositiveSpreadCount = spreads.Count(static spread => spread <= 0m);
        decimal spreadSum = 0m;
        foreach (decimal spread in spreads)
        {
            spreadSum += spread;
        }

        int outsideSession = ascending.Count(row => !IsInsideSession(options, row.TimestampUtc));
        int weekend = ascending.Count(static row =>
            row.TimestampUtc.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);

        long firstMinute = first.Ticks / TimeSpan.TicksPerMinute;
        long lastMinute = last.Ticks / TimeSpan.TicksPerMinute;
        int spanMinutes = (int)(lastMinute - firstMinute + 1);
        int minutesWithTicks = ascending
            .Select(static row => row.TimestampUtc.Ticks / TimeSpan.TicksPerMinute)
            .Distinct()
            .Count();

        decimal coverage = observedSpanSeconds <= 0m
            ? 1m
            : Round6(Math.Max(0m, observedSpanSeconds - gapSecondsTotal) / observedSpanSeconds);
        decimal nonPositiveRatio = Round6((decimal)nonPositiveSpreadCount / ascending.Length);

        (string grade, IReadOnlyList<string> reasons) = Grade(
            ascending.Length,
            coverage,
            nonPositiveRatio,
            gapCount,
            outOfOrder,
            duplicates,
            outsideSession);

        return new Mt5DayEvidence
        {
            Date = date,
            LeanRelativePath = LeanTickZipWriter.RelativePath(
                options.SecurityTypeFolder,
                options.Market,
                options.LeanTicker,
                date),
            ZipEntryName = LeanTickZipWriter.EntryName(options.LeanTicker, date),
            ZipSha256 = zipDigest,
            TickCount = ascending.Length,
            FirstTickUtc = first,
            LastTickUtc = last,
            ObservedSpanSeconds = observedSpanSeconds,
            ObservedSpanRatioOfDay = Round6(observedSpanSeconds / SecondsPerDay),
            CoverageRatioWithinObservedSpan = coverage,
            GapCount = gapCount,
            GapSecondsTotal = gapSecondsTotal,
            LargestGapSeconds = ToSeconds(largestGap),
            LargestGapFromUtc = largestGapFrom,
            LargestGapToUtc = largestGapTo,
            MinimumSpread = spreads[0],
            MedianSpread = Median(spreads),
            MaximumSpread = spreads[^1],
            MeanSpread = Math.Round(spreadSum / spreads.Length, 10, MidpointRounding.ToEven),
            NonPositiveSpreadCount = nonPositiveSpreadCount,
            NonPositiveSpreadRatio = nonPositiveRatio,
            OutOfOrderCount = outOfOrder,
            DuplicateTimestampCount = duplicates,
            OutsideDeclaredSessionTickCount = outsideSession,
            WeekendTickCount = weekend,
            SpanMinutes = spanMinutes,
            MinutesWithTicks = minutesWithTicks,
            EmptyMinutesWithinSpan = spanMinutes - minutesWithTicks,
            QualityGrade = grade,
            QualityGradeReasons = reasons
        };
    }

    private static (string Grade, IReadOnlyList<string> Reasons) Grade(
        int tickCount,
        decimal coverage,
        decimal nonPositiveRatio,
        int gapCount,
        int outOfOrder,
        int duplicates,
        int outsideSession)
    {
        var reasons = new List<string>();
        string grade = "A";

        void Demote(string candidate, string reason)
        {
            reasons.Add(reason);
            if (string.CompareOrdinal(candidate, grade) > 0)
            {
                grade = candidate;
            }
        }

        if (tickCount < GradeMinimumTickCount)
        {
            Demote("D", "TICK_COUNT_BELOW_MINIMUM");
        }

        if (coverage < GradeCoverageFloorForD)
        {
            Demote("D", "COVERAGE_BELOW_D_FLOOR");
        }
        else if (coverage < GradeCoverageFloorForC)
        {
            Demote("C", "COVERAGE_BELOW_C_FLOOR");
        }
        else if (coverage < GradeCoverageFloorForB)
        {
            Demote("B", "COVERAGE_BELOW_B_FLOOR");
        }

        if (nonPositiveRatio > GradeNonPositiveSpreadCeilingForD)
        {
            Demote("D", "NON_POSITIVE_SPREAD_RATIO_ABOVE_D_CEILING");
        }
        else if (nonPositiveRatio > GradeNonPositiveSpreadCeilingForC)
        {
            Demote("C", "NON_POSITIVE_SPREAD_RATIO_ABOVE_C_CEILING");
        }
        else if (nonPositiveRatio > 0m)
        {
            Demote("B", "NON_POSITIVE_SPREAD_PRESENT");
        }

        if (outOfOrder > 0)
        {
            Demote("C", "OUT_OF_ORDER_ROWS_PRESENT");
        }

        if (outsideSession > 0)
        {
            Demote("C", "TICKS_OUTSIDE_DECLARED_SESSION");
        }

        if (duplicates > 0)
        {
            Demote("B", "DUPLICATE_TIMESTAMPS_PRESENT");
        }

        if (gapCount > 0)
        {
            Demote("B", "GAPS_BEYOND_THRESHOLD_PRESENT");
        }

        reasons.Sort(StringComparer.Ordinal);
        return (grade, reasons);
    }

    private static bool IsInsideSession(Mt5ImportOptions options, DateTime timestampUtc)
    {
        int secondsOfWeek = ((int)timestampUtc.DayOfWeek * 86400)
            + (int)timestampUtc.TimeOfDay.TotalSeconds;
        int open = options.SessionOpenUtc.SecondsOfWeek;
        int close = options.SessionCloseUtc.SecondsOfWeek;
        return open <= close
            ? secondsOfWeek >= open && secondsOfWeek < close
            : secondsOfWeek >= open || secondsOfWeek < close;
    }

    private static decimal Median(decimal[] sorted) =>
        sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2m;

    private static decimal ToSeconds(TimeSpan value) => (decimal)value.Ticks / TimeSpan.TicksPerSecond;

    private static decimal Round6(decimal value) => Math.Round(value, 6, MidpointRounding.ToEven);
}
