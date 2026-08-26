using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace YO4X.MarketData.Mt5Import;

/// <summary>
/// Canonical JSON serialisation of the fidelity evidence, digested with SHA-256 exactly as the
/// restricted MQL5 IR is: one compact writer, one fixed member order, the digest computed over the
/// payload that omits it and then embedded. The document deliberately carries no timestamp of the
/// run, so two imports of the same input produce byte-identical artifacts.
/// </summary>
internal static class Mt5FidelityArtifact
{
    internal const string SchemaVersion = "yo4x.marketdata.mt5-lean-import-fidelity.v1";
    internal const string ToolVersion = "yo4x-mt5-lean-tick-importer.v1";

    internal const string RealTickCoverageStatement =
        "MT5 fills any minute that has no stored ticks with ticks generated from the M1 bar, and the "
        + "export carries no field distinguishing a generated tick from an observed one. This tool "
        + "therefore cannot measure real-tick coverage after export and does not claim to. The "
        + "emptyMinutesWithinSpan count below is the closest available indicator: those are the "
        + "minutes the MT5 tester would have generated, and it is an indicator, not a determination.";

    internal const string SpreadSubstitutionStatement =
        "Where the historical spread is zero or negative MT5 substitutes the last known spread "
        + "during testing. Rows with a non-positive spread are counted here because they are not "
        + "real observations; this tool never carries a spread forward and never derives an ask "
        + "from a bid.";

    internal const string TimeConversionStatement =
        "Timestamps were converted with the single fixed broker-server offset stated by the caller. "
        + "No daylight-saving transition was applied, because the export does not record the "
        + "server's time-zone rules. If the period crosses a broker DST change the offset is wrong "
        + "for part of it and the import must be split at the transition.";

    internal static Mt5SerializedArtifact Serialize(Mt5ImportEvidence evidence)
    {
        string payload = Write(evidence, digest: null);
        string digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        return new Mt5SerializedArtifact(Write(evidence, digest), digest);
    }

    private static string Write(Mt5ImportEvidence evidence, string? digest)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            Mt5ImportOptions options = evidence.Options;
            writer.WriteStartObject();
            writer.WriteString("schemaVersion", SchemaVersion);
            writer.WriteString("toolVersion", ToolVersion);
            if (digest is not null)
            {
                writer.WriteString("artifactSha256", digest);
            }

            writer.WriteStartObject("input");
            writer.WriteString("fileName", evidence.InputFileName);
            writer.WriteString("fileSha256", evidence.InputSha256);
            writer.WriteNumber("fileByteCount", evidence.InputByteCount);
            writer.WriteString("detectedLayout", LayoutName(evidence.Export.Layout));
            writer.WriteString("delimiter", evidence.Export.DelimiterName);
            writer.WriteString("headerLine", evidence.Export.HeaderLine);
            writer.WriteStartArray("columns");
            foreach (string column in evidence.Export.Columns)
            {
                writer.WriteStringValue(column);
            }

            writer.WriteEndArray();
            writer.WriteBoolean("flagsColumnPresent", evidence.Export.FlagsColumnPresent);
            writer.WriteEndObject();

            writer.WriteStartObject("request");
            writer.WriteString("symbol", options.Symbol);
            writer.WriteString("leanTicker", options.LeanTicker);
            writer.WriteString("market", options.Market);
            writer.WriteString("securityType", options.SecurityTypeFolder);
            writer.WriteString("resolution", LeanTickZipWriter.ResolutionName);
            writer.WriteString("tickType", LeanTickZipWriter.TickTypeName);
            writer.WriteString("lineEnding", LeanTickZipWriter.LineEndingName);
            writer.WriteString("serverUtcOffset", options.ServerUtcOffsetText);
            writer.WriteNumber("serverUtcOffsetMinutes", (long)options.ServerUtcOffset.TotalMinutes);
            writer.WriteNumber("gapThresholdSeconds", (long)options.GapThreshold.TotalSeconds);
            writer.WriteNumber("maximumOutOfOrderRows", options.MaximumOutOfOrderRows);
            writer.WriteNumber("maximumRejectedRows", options.MaximumRejectedRows);
            writer.WriteString("declaredSessionOpenUtc", options.SessionOpenUtc.Text);
            writer.WriteString("declaredSessionCloseUtc", options.SessionCloseUtc.Text);
            writer.WriteBoolean("overwrite", options.Overwrite);
            writer.WriteBoolean("dryRun", options.DryRun);
            writer.WriteEndObject();

            writer.WriteStartObject("rowCounts");
            writer.WriteNumber("dataLinesRead", evidence.Export.DataLineCount);
            writer.WriteNumber("accepted", evidence.AcceptedRowCount);
            writer.WriteNumber("rejected", evidence.RejectedRowCount);
            writer.WriteNumber("writtenToLean", evidence.WrittenRowCount);
            writer.WriteNumber("outOfOrderAcrossFile", evidence.TotalOutOfOrderRows);
            writer.WriteNumber("duplicateTimestampsAcrossFile", evidence.TotalDuplicateTimestampRows);
            writer.WriteEndObject();

            writer.WriteStartArray("rejections");
            foreach (Mt5RejectionTally tally in evidence.Export.Rejections)
            {
                writer.WriteStartObject();
                writer.WriteString("reason", tally.Reason);
                writer.WriteNumber("count", tally.Count);
                writer.WriteNumber("firstLineNumber", tally.FirstLineNumber);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartArray("days");
            foreach (Mt5DayEvidence day in evidence.Days)
            {
                WriteDay(writer, day);
            }

            writer.WriteEndArray();

            writer.WriteStartObject("realTickCoverage");
            writer.WriteBoolean("measured", false);
            writer.WriteString("statement", RealTickCoverageStatement);
            writer.WriteBoolean("flagsColumnPresent", evidence.Export.FlagsColumnPresent);
            writer.WriteStartArray("flagHistogram");
            foreach (KeyValuePair<string, int> entry in evidence.FlagHistogram
                .OrderBy(static entry => entry.Key, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("flags", entry.Key);
                writer.WriteNumber("count", entry.Value);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();

            writer.WriteStartObject("qualityGrading");
            writer.WriteString("rule", Mt5FidelityAnalyzer.GradeRule);
            writer.WriteNumber("minimumTickCount", Mt5FidelityAnalyzer.GradeMinimumTickCount);
            writer.WriteString("coverageFloorForD", Text(Mt5FidelityAnalyzer.GradeCoverageFloorForD));
            writer.WriteString("coverageFloorForC", Text(Mt5FidelityAnalyzer.GradeCoverageFloorForC));
            writer.WriteString("coverageFloorForB", Text(Mt5FidelityAnalyzer.GradeCoverageFloorForB));
            writer.WriteString(
                "nonPositiveSpreadCeilingForC",
                Text(Mt5FidelityAnalyzer.GradeNonPositiveSpreadCeilingForC));
            writer.WriteString(
                "nonPositiveSpreadCeilingForD",
                Text(Mt5FidelityAnalyzer.GradeNonPositiveSpreadCeilingForD));
            writer.WriteEndObject();

            writer.WriteStartObject("statements");
            writer.WriteString("timeConversion", TimeConversionStatement);
            writer.WriteString("spreadSubstitution", SpreadSubstitutionStatement);
            writer.WriteString("ordering", Mt5FidelityAnalyzer.OrderingApplied);
            writer.WriteString(
                "provenance",
                "This artifact describes one MT5 export as received. It asserts nothing about the "
                + "broker feed upstream of the export, and it is not evidence that the data matches "
                + "any other broker's series for the same ticker.");
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteDay(Utf8JsonWriter writer, Mt5DayEvidence day)
    {
        writer.WriteStartObject();
        writer.WriteString("date", day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        writer.WriteString("leanRelativePath", day.LeanRelativePath);
        writer.WriteString("zipEntryName", day.ZipEntryName);
        if (day.ZipSha256 is null)
        {
            writer.WriteNull("zipSha256");
        }
        else
        {
            writer.WriteString("zipSha256", day.ZipSha256);
        }

        writer.WriteNumber("tickCount", day.TickCount);
        writer.WriteString("firstTickUtc", Text(day.FirstTickUtc));
        writer.WriteString("lastTickUtc", Text(day.LastTickUtc));
        writer.WriteString("observedSpanSeconds", Text(day.ObservedSpanSeconds));
        writer.WriteString("observedSpanRatioOfDay", Text(day.ObservedSpanRatioOfDay));
        writer.WriteString("coverageRatioWithinObservedSpan", Text(day.CoverageRatioWithinObservedSpan));

        writer.WriteStartObject("gaps");
        writer.WriteNumber("countBeyondThreshold", day.GapCount);
        writer.WriteString("secondsBeyondThreshold", Text(day.GapSecondsTotal));
        writer.WriteString("largestGapSeconds", Text(day.LargestGapSeconds));
        WriteNullableInstant(writer, "largestGapFromUtc", day.LargestGapFromUtc);
        WriteNullableInstant(writer, "largestGapToUtc", day.LargestGapToUtc);
        writer.WriteEndObject();

        writer.WriteStartObject("spread");
        writer.WriteString("minimum", Text(day.MinimumSpread));
        writer.WriteString("median", Text(day.MedianSpread));
        writer.WriteString("maximum", Text(day.MaximumSpread));
        writer.WriteString("mean", Text(day.MeanSpread));
        writer.WriteNumber("zeroOrNegativeCount", day.NonPositiveSpreadCount);
        writer.WriteString("zeroOrNegativeRatio", Text(day.NonPositiveSpreadRatio));
        writer.WriteEndObject();

        writer.WriteStartObject("monotonicity");
        writer.WriteNumber("outOfOrderCount", day.OutOfOrderCount);
        writer.WriteNumber("duplicateTimestampCount", day.DuplicateTimestampCount);
        writer.WriteString("orderingApplied", Mt5FidelityAnalyzer.OrderingApplied);
        writer.WriteEndObject();

        writer.WriteStartObject("session");
        writer.WriteNumber("outsideDeclaredSessionTickCount", day.OutsideDeclaredSessionTickCount);
        writer.WriteNumber("weekendTickCount", day.WeekendTickCount);
        writer.WriteEndObject();

        writer.WriteStartObject("minuteBuckets");
        writer.WriteNumber("spanMinutes", day.SpanMinutes);
        writer.WriteNumber("withTicks", day.MinutesWithTicks);
        writer.WriteNumber("emptyWithinSpan", day.EmptyMinutesWithinSpan);
        writer.WriteEndObject();

        writer.WriteString("qualityGrade", day.QualityGrade);
        writer.WriteStartArray("qualityGradeReasons");
        foreach (string reason in day.QualityGradeReasons)
        {
            writer.WriteStringValue(reason);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteNullableInstant(Utf8JsonWriter writer, string name, DateTime? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteString(name, Text(value.Value));
    }

    internal static string LayoutName(Mt5ExportLayout layout) => layout switch
    {
        Mt5ExportLayout.Mt5TabularExport => "mt5-tick-export-tabular",
        Mt5ExportLayout.BareDateTimeBidAsk => "bare-datetime-bid-ask",
        _ => "unknown"
    };

    internal static string Text(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    internal static string Text(DateTime value) =>
        value.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
}

/// <summary>The canonical document and the digest computed over its digest-free form.</summary>
internal sealed record Mt5SerializedArtifact(string CanonicalJson, string ArtifactSha256);
