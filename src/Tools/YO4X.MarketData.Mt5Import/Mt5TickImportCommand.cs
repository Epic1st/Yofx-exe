using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace YO4X.MarketData.Mt5Import;

/// <summary>
/// Converts an exported MT5 tick file into the LEAN on-disk tick-quote layout and emits the
/// fidelity evidence for the conversion. Every refusal is stated; nothing is repaired silently.
/// </summary>
internal static class Mt5TickImportCommand
{
    private const string CommandSwitch = "--import-ticks";
    private const int ExitSuccess = 0;
    private const int ExitInvalidRequest = 2;
    private const int ExitRefused = 3;

    internal const string Usage = """
        YO4X.MarketData.Mt5Import --import-ticks [options]

          Converts one exported MT5 tick file for one symbol into the LEAN tick-quote layout
          {data-root}/{security-type}/{market}/tick/{ticker}/{yyyyMMdd}_quote.zip and writes a
          deterministic fidelity artifact describing what the data actually contains.

        Required
          --input <path>                 MT5 tick export (tab-separated <DATE> <TIME> <BID> <ASK>
                                         [<LAST> <VOLUME> <FLAGS>], or a bare datetime,bid,ask file).
          --symbol <ticker>              Broker symbol, for example EURUSD.
          --market <name>                LEAN market folder, for example mt5-demo. Use a
                                         broker-specific name so a broker's series never collides
                                         with another's under the same ticker.
          --data-root <path>             LEAN data root directory.
          --report-output <path>         Destination of the fidelity JSON artifact.
          --server-utc-offset <+HH:MM>   The broker server's fixed offset from UTC. There is no
                                         default and none is inferred; without it the import refuses
                                         to run. A single fixed offset is applied, so a period that
                                         crosses a broker daylight-saving change must be split.

        Optional
          --security-type <forex|cfd>    Default forex.
          --report-text-output <path>    Additional human-readable summary of the same evidence.
          --gap-threshold-seconds <n>    Gap size that counts as a gap. Default 60.
          --max-out-of-order <n>         Refuse above this many timestamp regressions. Default 0.
          --max-rejected-rows <n>        Refuse above this many unusable rows. Default 0.
          --session-open-utc <D:HH:MM>   Declared week open in UTC. Default Sunday:22:00.
          --session-close-utc <D:HH:MM>  Declared week close in UTC. Default Friday:22:00.
          --overwrite                    Permit replacing existing target files.
          --dry-run                      Measure and report only; write no zip.

        Exit codes: 0 success, 2 invalid request or I/O failure, 3 refused by a fail-closed policy.
        """;

    internal static bool IsRequested(IReadOnlyList<string> arguments) =>
        arguments.Contains(CommandSwitch, StringComparer.Ordinal);

    internal static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Mt5ImportOptions options = ParseOptions(arguments);
            return await ExecuteAsync(options, cancellationToken).ConfigureAwait(false);
        }
        catch (Mt5ImportRefusedException refusal)
        {
            Console.Error.WriteLine($"Tick import refused: {refusal.Message}");
            return ExitRefused;
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or NotSupportedException)
        {
            Console.Error.WriteLine($"Tick import failed: {exception.Message}");
            return ExitInvalidRequest;
        }
    }

    private static Mt5ImportOptions ParseOptions(IReadOnlyList<string> arguments)
    {
        string inputPath = Path.GetFullPath(Mt5CommandLine.GetRequiredOption(arguments, "--input"));
        string symbol = Mt5CommandLine.GetRequiredOption(arguments, "--symbol").Trim();
        string market = Mt5CommandLine.GetRequiredOption(arguments, "--market").Trim();
        string dataRoot = Path.GetFullPath(Mt5CommandLine.GetRequiredOption(arguments, "--data-root"));
        string reportPath = Path.GetFullPath(
            Mt5CommandLine.GetRequiredOption(arguments, "--report-output"));
        string offsetText = Mt5CommandLine.GetRequiredOption(arguments, "--server-utc-offset");

        if (symbol.Length == 0 || symbol.Any(static value => !char.IsLetterOrDigit(value) && value != '.' && value != '_' && value != '-'))
        {
            throw new ArgumentException(
                "Option '--symbol' must be a non-empty ticker of letters, digits, dot, underscore or hyphen.");
        }

        if (market.Length == 0 || market.Any(static value => !char.IsLetterOrDigit(value) && value != '-' && value != '_'))
        {
            throw new ArgumentException(
                "Option '--market' must be a non-empty name of letters, digits, hyphen or underscore.");
        }

        string securityType = (Mt5CommandLine.GetOptionalOption(arguments, "--security-type") ?? "forex")
            .ToLowerInvariant();
        if (securityType is not ("forex" or "cfd"))
        {
            throw new ArgumentException("Option '--security-type' must be 'forex' or 'cfd'.");
        }

        string? reportTextPath = Mt5CommandLine.GetOptionalOption(arguments, "--report-text-output");
        int gapSeconds = Mt5CommandLine.GetOptionalCount(arguments, "--gap-threshold-seconds", 60, 1, 86400);

        return new Mt5ImportOptions(
            inputPath,
            symbol,
            symbol.ToLowerInvariant(),
            market.ToLowerInvariant(),
            securityType,
            dataRoot,
            reportPath,
            reportTextPath is null ? null : Path.GetFullPath(reportTextPath),
            Mt5CommandLine.ParseServerUtcOffset(offsetText),
            offsetText,
            TimeSpan.FromSeconds(gapSeconds),
            Mt5CommandLine.GetOptionalCount(arguments, "--max-out-of-order", 0, 0, int.MaxValue),
            Mt5CommandLine.GetOptionalCount(arguments, "--max-rejected-rows", 0, 0, int.MaxValue),
            Mt5CommandLine.ParseWeekInstant(
                "--session-open-utc",
                Mt5CommandLine.GetOptionalOption(arguments, "--session-open-utc") ?? "Sunday:22:00"),
            Mt5CommandLine.ParseWeekInstant(
                "--session-close-utc",
                Mt5CommandLine.GetOptionalOption(arguments, "--session-close-utc") ?? "Friday:22:00"),
            Mt5CommandLine.HasSwitch(arguments, "--overwrite"),
            Mt5CommandLine.HasSwitch(arguments, "--dry-run"));
    }

    private static async Task<int> ExecuteAsync(
        Mt5ImportOptions options,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(options.InputPath))
        {
            throw new ArgumentException($"The input file '{options.InputPath}' does not exist.");
        }

        var inputInfo = new FileInfo(options.InputPath);
        string inputSha256;
        await using (FileStream stream = File.OpenRead(options.InputPath))
        {
            inputSha256 = Convert.ToHexStringLower(
                await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        }

        Mt5ParsedExport export = Mt5TickExportReader.Read(options.InputPath, options.ServerUtcOffset);
        if (export.Rows.Count == 0)
        {
            throw new Mt5ImportRefusedException(
                "The export produced no usable quote row; there is nothing to convert and no evidence to emit.");
        }

        int rejectedRows = export.Rejections.Sum(static tally => tally.Count);
        if (rejectedRows > options.MaximumRejectedRows)
        {
            throw new Mt5ImportRefusedException(string.Create(
                CultureInfo.InvariantCulture,
                $"{rejectedRows} row(s) were unusable, above the stated --max-rejected-rows of {options.MaximumRejectedRows}. Reasons: {DescribeRejections(export.Rejections)}."));
        }

        (int outOfOrder, int duplicates) = Mt5FidelityAnalyzer.CountOrderingDefects(export.Rows);
        if (outOfOrder > options.MaximumOutOfOrderRows)
        {
            throw new Mt5ImportRefusedException(string.Create(
                CultureInfo.InvariantCulture,
                $"{outOfOrder} row(s) regress against the preceding timestamp, above the stated --max-out-of-order of {options.MaximumOutOfOrderRows}. The file also carries {duplicates} duplicate timestamp(s). Reordering a file this disordered would hide a real defect, so the import refuses instead."));
        }

        Dictionary<DateOnly, List<Mt5QuoteRow>> byDay = [];
        foreach (Mt5QuoteRow row in export.Rows)
        {
            DateOnly day = DateOnly.FromDateTime(row.TimestampUtc);
            if (!byDay.TryGetValue(day, out List<Mt5QuoteRow>? bucket))
            {
                bucket = [];
                byDay[day] = bucket;
            }

            bucket.Add(row);
        }

        DateOnly[] orderedDays = [.. byDay.Keys.Order()];
        var targets = new Dictionary<DateOnly, string>();
        foreach (DateOnly day in orderedDays)
        {
            targets[day] = Path.Combine(
                options.DataRoot,
                options.SecurityTypeFolder,
                options.Market,
                LeanTickZipWriter.ResolutionName,
                options.LeanTicker,
                LeanTickZipWriter.FileName(day));
        }

        // Every target is checked before a single byte is written, so a refusal never leaves a
        // half-converted dataset behind.
        if (!options.Overwrite)
        {
            var existing = new List<string>();
            if (!options.DryRun)
            {
                existing.AddRange(targets.Values.Where(File.Exists));
            }

            if (File.Exists(options.ReportPath))
            {
                existing.Add(options.ReportPath);
            }

            if (options.ReportTextPath is not null && File.Exists(options.ReportTextPath))
            {
                existing.Add(options.ReportTextPath);
            }

            if (existing.Count > 0)
            {
                throw new Mt5ImportRefusedException(
                    $"{existing.Count} target file(s) already exist and --overwrite was not given: {string.Join(", ", existing.Order(StringComparer.Ordinal))}.");
            }
        }

        var digests = new Dictionary<DateOnly, string>();
        int writtenRows = 0;
        if (!options.DryRun)
        {
            foreach (DateOnly day in orderedDays)
            {
                Mt5QuoteRow[] ascending = [.. byDay[day].OrderBy(static row => row.TimestampUtc)];
                byte[] payload = LeanTickZipWriter.RenderDay(ascending);
                digests[day] = await LeanTickZipWriter.WriteDayAsync(
                        targets[day],
                        LeanTickZipWriter.EntryName(options.LeanTicker, day),
                        payload,
                        cancellationToken)
                    .ConfigureAwait(false);
                writtenRows += ascending.Length;
            }
        }

        IReadOnlyList<Mt5DayEvidence> days = Mt5FidelityAnalyzer.Analyze(options, export.Rows, digests);
        var evidence = new Mt5ImportEvidence(
            options,
            Path.GetFileName(options.InputPath),
            inputSha256,
            inputInfo.Length,
            export,
            days,
            export.Rows.Count,
            rejectedRows,
            writtenRows,
            outOfOrder,
            duplicates,
            BuildFlagHistogram(export.Rows));

        Mt5SerializedArtifact artifact = Mt5FidelityArtifact.Serialize(evidence);
        await WriteTextAtomicallyAsync(options.ReportPath, artifact.CanonicalJson, cancellationToken)
            .ConfigureAwait(false);
        if (options.ReportTextPath is not null)
        {
            await WriteTextAtomicallyAsync(
                    options.ReportTextPath,
                    Mt5FidelityReport.Render(evidence, artifact.ArtifactSha256),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        WriteSummary(evidence, artifact);
        return ExitSuccess;
    }

    private static void WriteSummary(Mt5ImportEvidence evidence, Mt5SerializedArtifact artifact)
    {
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Tick import {(evidence.Options.DryRun ? "measured (dry run, no zip written)" : "complete")}: {evidence.Options.Symbol} on market {evidence.Options.Market}, {evidence.Days.Count} symbol-day(s), {evidence.AcceptedRowCount} accepted row(s), {evidence.RejectedRowCount} rejected row(s)."));
        foreach (Mt5DayEvidence day in evidence.Days)
        {
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {day.Date:yyyy-MM-dd} grade {day.QualityGrade}: {day.TickCount} tick(s), coverage {Mt5FidelityArtifact.Text(day.CoverageRatioWithinObservedSpan)}, {day.GapCount} gap(s), {day.NonPositiveSpreadCount} non-positive spread(s), {day.OutOfOrderCount} out-of-order, {day.DuplicateTimestampCount} duplicate(s) -> {day.LeanRelativePath}"));
        }

        Console.WriteLine(
            $"Fidelity artifact {artifact.ArtifactSha256} written to {evidence.Options.ReportPath}.");
        Console.WriteLine(
            "Real-tick coverage is not measured and is not claimed: MT5 may have generated part of this history from M1 bars and the export does not distinguish it.");
    }

    private static string DescribeRejections(IReadOnlyList<Mt5RejectionTally> rejections) =>
        string.Join(
            ", ",
            rejections.Select(static tally => string.Create(
                CultureInfo.InvariantCulture,
                $"{tally.Reason} x{tally.Count} (first at line {tally.FirstLineNumber})")));

    private static Dictionary<string, int> BuildFlagHistogram(IReadOnlyList<Mt5QuoteRow> rows)
    {
        var histogram = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Mt5QuoteRow row in rows)
        {
            if (row.FlagsText is null)
            {
                continue;
            }

            string key = row.FlagsText.Length == 0 ? "(empty)" : row.FlagsText;
            histogram[key] = histogram.TryGetValue(key, out int count) ? count + 1 : 1;
        }

        return histogram;
    }

    private static async Task WriteTextAtomicallyAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(path)
            ?? throw new IOException($"The output path '{path}' has no directory.");
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            string.Concat(Path.GetFileName(path), ".", Guid.NewGuid().ToString("N"), ".partial"));
        try
        {
            await File.WriteAllBytesAsync(
                    temporaryPath,
                    new UTF8Encoding(false).GetBytes(content),
                    cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
