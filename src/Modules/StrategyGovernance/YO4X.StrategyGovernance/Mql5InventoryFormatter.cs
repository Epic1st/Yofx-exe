using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YO4X.StrategyGovernance;

public static class Mql5InventoryFormatter
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = CreateJsonOptions();

    public static string ToJson(Mql5CorpusManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return JsonSerializer.Serialize(manifest, ManifestJsonOptions) + "\n";
    }

    public static string ToJsonFragment<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return JsonSerializer.Serialize(value, ManifestJsonOptions);
    }

    public static string ToMarkdown(Mql5CorpusManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var report = new StringBuilder();
        report.AppendLine("# MQL5 static compatibility report")
            .AppendLine()
            .AppendLine("This report is a deterministic, non-executing lexical inventory. It is not an MQL5 parse/type-check result, a semantic conversion, a MetaEditor compile result, an MT5 reference-parity result, or demo/runtime evidence. No file in this report is deployable on the strength of this inventory alone.")
            .AppendLine()
            .Append("- Schema: `").Append(manifest.SchemaVersion).AppendLine("`")
            .Append("- Analyzer: `").Append(manifest.AnalyzerVersion).AppendLine("`")
            .Append("- Corpus SHA-256: `").Append(manifest.CorpusSha256).AppendLine("`")
            .Append("- Files: ").Append(manifest.FileCount).AppendLine()
            .Append("- Bytes: ").Append(manifest.TotalBytes).AppendLine()
            .AppendLine("- Machine-readable detail: [mq5-static-manifest.v1.json](./mq5-static-manifest.v1.json)")
            .AppendLine("- Semantic conversion proofs: 0")
            .AppendLine("- MetaEditor compile proofs: 0")
            .AppendLine("- MT5 reference-parity proofs: 0")
            .AppendLine("- Demo runtime proofs: 0")
            .AppendLine()
            .AppendLine("## Inventory summary")
            .AppendLine()
            .AppendLine("| Dimension | Value | Files |")
            .AppendLine("|---|---|---:|");

        AppendCounts(report, "Source kind", manifest.Files.GroupBy(static file => file.Kind.ToString()));
        AppendCounts(report, "Static disposition", manifest.Files.GroupBy(static file => file.Disposition.ToString()));

        report.AppendLine()
            .AppendLine("## Feature inventory")
            .AppendLine()
            .AppendLine("`SupportedSubsetCandidate` means only that the token shape belongs to the planned subset; it does not mean the surrounding program is supported.")
            .AppendLine()
            .AppendLine("| Feature | Classification | Files | Occurrences |")
            .AppendLine("|---|---|---:|---:|");

        foreach (var group in manifest.Files
                     .SelectMany(static file => file.Features)
                     .GroupBy(static feature => (feature.Code, feature.Support))
                     .OrderBy(static group => group.Key.Code, StringComparer.Ordinal))
        {
            report.Append("| ").Append(Escape(group.Key.Code))
                .Append(" | ").Append(group.Key.Support)
                .Append(" | ").Append(group.Count())
                .Append(" | ").Append(group.Sum(static feature => feature.OccurrenceCount))
                .AppendLine(" |");
        }

        report.AppendLine()
            .AppendLine("## Finding inventory")
            .AppendLine()
            .AppendLine("| Finding | Severity | Classification | Files |")
            .AppendLine("|---|---|---|---:|");

        foreach (var group in manifest.Files
                     .SelectMany(static file => file.Findings)
                     .GroupBy(static finding => (finding.Code, finding.Severity, finding.Support))
                     .OrderByDescending(static group => group.Key.Severity)
                     .ThenBy(static group => group.Key.Code, StringComparer.Ordinal))
        {
            report.Append("| ").Append(Escape(group.Key.Code))
                .Append(" | ").Append(group.Key.Severity)
                .Append(" | ").Append(group.Key.Support)
                .Append(" | ").Append(group.Count())
                .AppendLine(" |");
        }

        report.AppendLine()
            .AppendLine("## Per-file inventory")
            .AppendLine()
            .AppendLine("The machine-readable manifest contains include resolution, feature counts, and source line numbers. This table intentionally contains metadata only, never source bodies.")
            .AppendLine()
            .AppendLine("| File | Kind | Bytes | SHA-256 | Entrypoints | Includes | Disposition | Blocking/review findings |")
            .AppendLine("|---|---|---:|---|---|---:|---|---|");

        foreach (Mql5SourceManifest file in manifest.Files)
        {
            string entrypoints = file.Entrypoints.Count == 0
                ? "-"
                : string.Join(", ", file.Entrypoints);
            string findingCodes = string.Join(
                ", ",
                file.Findings
                    .Where(static finding => finding.Code != "SEMANTIC_VALIDATION_NOT_PERFORMED")
                    .Select(static finding => finding.Code)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal));

            report.Append("| ").Append(Escape(file.RelativePath))
                .Append(" | ").Append(file.Kind)
                .Append(" | ").Append(file.ByteLength)
                .Append(" | `").Append(file.Sha256).Append('`')
                .Append(" | ").Append(Escape(entrypoints))
                .Append(" | ").Append(file.Includes.Count)
                .Append(" | ").Append(file.Disposition)
                .Append(" | ").Append(Escape(string.IsNullOrEmpty(findingCodes) ? "-" : findingCodes))
                .AppendLine(" |");
        }

        report.AppendLine()
            .AppendLine("## Required next gates")
            .AppendLine()
            .AppendLine("1. Resolve every missing/ambiguous include and custom-indicator dependency from owned source.")
            .AppendLine("2. Parse and type-check each complete dependency graph in an isolated, network-denied conversion sandbox.")
            .AppendLine("3. Lower only the versioned supported subset into restricted IR; stop on ambiguous or unsupported semantics.")
            .AppendLine("4. Compile the original source in an identified MetaEditor/MT5 build and retain diagnostics as separate evidence.")
            .AppendLine("5. Run deterministic simulation, reference trace comparison, manual review, and tightly gated demo validation before any deployable status.");

        return report.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    private static void AppendCounts(
        StringBuilder report,
        string dimension,
        IEnumerable<IGrouping<string, Mql5SourceManifest>> groups)
    {
        foreach (IGrouping<string, Mql5SourceManifest> group in groups.OrderBy(
                     static group => group.Key,
                     StringComparer.Ordinal))
        {
            report.Append("| ").Append(dimension)
                .Append(" | ").Append(Escape(group.Key))
                .Append(" | ").Append(group.Count())
                .AppendLine(" |");
        }
    }

    private static string Escape(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }
}
