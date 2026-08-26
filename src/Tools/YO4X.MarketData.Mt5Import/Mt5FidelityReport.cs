using System.Globalization;
using System.Text;

namespace YO4X.MarketData.Mt5Import;

/// <summary>
/// A human-readable rendering of exactly the measurements in the canonical artifact. It carries no
/// run timestamp either, so it is reproducible alongside the JSON it describes.
/// </summary>
internal static class Mt5FidelityReport
{
    internal static string Render(Mt5ImportEvidence evidence, string artifactSha256)
    {
        Mt5ImportOptions options = evidence.Options;
        var builder = new StringBuilder();
        builder.Append("# MT5 to LEAN tick import fidelity\n\n");
        builder.Append(CultureInfo.InvariantCulture, $"- Schema: `{Mt5FidelityArtifact.SchemaVersion}`\n");
        builder.Append(CultureInfo.InvariantCulture, $"- Tool: `{Mt5FidelityArtifact.ToolVersion}`\n");
        builder.Append(CultureInfo.InvariantCulture, $"- Artifact SHA-256: `{artifactSha256}`\n");
        builder.Append(CultureInfo.InvariantCulture, $"- Input: `{evidence.InputFileName}` ({evidence.InputByteCount} bytes)\n");
        builder.Append(CultureInfo.InvariantCulture, $"- Input SHA-256: `{evidence.InputSha256}`\n");
        builder.Append(CultureInfo.InvariantCulture, $"- Detected layout: `{Mt5FidelityArtifact.LayoutName(evidence.Export.Layout)}` ({evidence.Export.DelimiterName}-separated)\n");
        builder.Append(CultureInfo.InvariantCulture, $"- Symbol: `{options.Symbol}` -> LEAN `{options.SecurityTypeFolder}/{options.Market}/tick/{options.LeanTicker}`\n");
        builder.Append(CultureInfo.InvariantCulture, $"- Broker server offset applied: `{options.ServerUtcOffsetText}` (fixed, caller-stated)\n");
        builder.Append(CultureInfo.InvariantCulture, $"- Rows: {evidence.Export.DataLineCount} read, {evidence.AcceptedRowCount} accepted, {evidence.RejectedRowCount} rejected, {evidence.WrittenRowCount} written\n");
        if (options.DryRun)
        {
            builder.Append("- Dry run: no zip was written.\n");
        }

        builder.Append('\n');

        if (evidence.Export.Rejections.Count > 0)
        {
            builder.Append("## Rejected rows\n\n| reason | count | first line |\n|---|---|---|\n");
            foreach (Mt5RejectionTally tally in evidence.Export.Rejections)
            {
                builder.Append(CultureInfo.InvariantCulture, $"| {tally.Reason} | {tally.Count} | {tally.FirstLineNumber} |\n");
            }

            builder.Append('\n');
        }

        builder.Append("## Symbol-days\n\n");
        builder.Append("| date | grade | ticks | coverage | gaps | largest gap s | spread min/median/max | non-positive | out-of-order | duplicates | outside session | empty minutes |\n");
        builder.Append("|---|---|---|---|---|---|---|---|---|---|---|---|\n");
        foreach (Mt5DayEvidence day in evidence.Days)
        {
            builder.Append(CultureInfo.InvariantCulture, $"| {day.Date:yyyy-MM-dd} ");
            builder.Append(CultureInfo.InvariantCulture, $"| {day.QualityGrade} ");
            builder.Append(CultureInfo.InvariantCulture, $"| {day.TickCount} ");
            builder.Append(CultureInfo.InvariantCulture, $"| {Mt5FidelityArtifact.Text(day.CoverageRatioWithinObservedSpan)} ");
            builder.Append(CultureInfo.InvariantCulture, $"| {day.GapCount} ");
            builder.Append(CultureInfo.InvariantCulture, $"| {Mt5FidelityArtifact.Text(day.LargestGapSeconds)} ");
            builder.Append(CultureInfo.InvariantCulture, $"| {Mt5FidelityArtifact.Text(day.MinimumSpread)} / {Mt5FidelityArtifact.Text(day.MedianSpread)} / {Mt5FidelityArtifact.Text(day.MaximumSpread)} ");
            builder.Append(CultureInfo.InvariantCulture, $"| {day.NonPositiveSpreadCount} ");
            builder.Append(CultureInfo.InvariantCulture, $"| {day.OutOfOrderCount} ");
            builder.Append(CultureInfo.InvariantCulture, $"| {day.DuplicateTimestampCount} ");
            builder.Append(CultureInfo.InvariantCulture, $"| {day.OutsideDeclaredSessionTickCount} ");
            builder.Append(CultureInfo.InvariantCulture, $"| {day.EmptyMinutesWithinSpan} |\n");
        }

        builder.Append('\n');
        foreach (Mt5DayEvidence day in evidence.Days)
        {
            builder.Append(CultureInfo.InvariantCulture, $"- `{day.Date:yyyy-MM-dd}` grade {day.QualityGrade}");
            if (day.QualityGradeReasons.Count > 0)
            {
                builder.Append(CultureInfo.InvariantCulture, $" because {string.Join(", ", day.QualityGradeReasons)}");
            }

            builder.Append(CultureInfo.InvariantCulture, $"; `{day.LeanRelativePath}`");
            builder.Append(day.ZipSha256 is null ? " (not written)" : $" SHA-256 `{day.ZipSha256}`");
            builder.Append('\n');
        }

        builder.Append("\n## What this evidence does not establish\n\n");
        builder.Append(CultureInfo.InvariantCulture, $"- {Mt5FidelityArtifact.RealTickCoverageStatement}\n");
        builder.Append(CultureInfo.InvariantCulture, $"- {Mt5FidelityArtifact.SpreadSubstitutionStatement}\n");
        builder.Append(CultureInfo.InvariantCulture, $"- {Mt5FidelityArtifact.TimeConversionStatement}\n");
        builder.Append(CultureInfo.InvariantCulture, $"- Grading rule: {Mt5FidelityAnalyzer.GradeRule}\n");
        return builder.ToString();
    }
}
