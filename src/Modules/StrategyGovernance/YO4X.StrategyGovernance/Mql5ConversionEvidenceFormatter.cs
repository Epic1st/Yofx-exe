using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YO4X.StrategyGovernance;

public static class Mql5ConversionEvidenceFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static string ToJson(Mql5ConversionCorpusEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return JsonSerializer.Serialize(evidence, JsonOptions) + "\n";
    }

    public static string ToMarkdown(Mql5ConversionCorpusEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var report = new StringBuilder();
        report.AppendLine("# MQL5 conversion evidence report")
            .AppendLine()
            .AppendLine("This is non-executing dependency, lexical, and structural evidence. It is not a complete MQL5 grammar parse, type check, semantic conversion, MetaEditor compile, reference-parity result, or runtime proof. Every file remains fail-closed until those later gates produce separately bound evidence.")
            .AppendLine()
            .Append("- Schema: `").Append(evidence.SchemaVersion).AppendLine("`")
            .Append("- Analyzer: `").Append(evidence.AnalyzerVersion).AppendLine("`")
            .Append("- Input corpus SHA-256: `").Append(evidence.InputCorpusSha256).AppendLine("`")
            .Append("- Dependency graph SHA-256: `").Append(evidence.DependencyGraphSha256).AppendLine("`")
            .Append("- Evidence SHA-256: `").Append(evidence.EvidenceSha256).AppendLine("`")
            .Append("- Files: ").Append(evidence.FileCount).AppendLine()
            .Append("- Bytes: ").Append(evidence.TotalBytes).AppendLine()
            .AppendLine("- Full grammar parse proofs: 0")
            .AppendLine("- Type-check proofs: 0")
            .AppendLine("- Restricted-IR lowering proofs: 0")
            .AppendLine("- Semantic conversion proofs: 0")
            .AppendLine()
            .AppendLine("## Disposition summary")
            .AppendLine()
            .AppendLine("| Disposition | Files |")
            .AppendLine("|---|---:|");

        foreach (IGrouping<Mql5ConversionEvidenceDisposition, Mql5ConversionFileEvidence> group in
                 evidence.Files
                     .GroupBy(static file => file.Disposition)
                     .OrderBy(static group => group.Key))
        {
            report.Append("| ").Append(group.Key)
                .Append(" | ").Append(group.Count())
                .AppendLine(" |");
        }

        report.AppendLine()
            .AppendLine("## Source encoding summary")
            .AppendLine()
            .AppendLine("| Encoding/classification | Files |")
            .AppendLine("|---|---:|");
        foreach (IGrouping<string, Mql5ConversionFileEvidence> group in evidence.Files
                     .GroupBy(static file => file.TextEncoding, StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            report.Append("| ").Append(Escape(group.Key))
                .Append(" | ").Append(group.Count())
                .AppendLine(" |");
        }

        report.AppendLine()
            .AppendLine("## Stage summary")
            .AppendLine()
            .AppendLine("| Stage | Status | Files |")
            .AppendLine("|---|---|---:|");
        foreach (var group in evidence.Files
                     .SelectMany(static file => file.Stages)
                     .GroupBy(static stage => (stage.Name, stage.Status))
                     .OrderBy(static group => group.Key.Name)
                     .ThenBy(static group => group.Key.Status))
        {
            report.Append("| ").Append(group.Key.Name)
                .Append(" | ").Append(group.Key.Status)
                .Append(" | ").Append(group.Count())
                .AppendLine(" |");
        }

        report.AppendLine()
            .AppendLine("## Dependency evidence")
            .AppendLine()
            .Append("- Resolved local include edges: ")
            .Append(evidence.Files.Sum(static file => file.Includes.Count(include =>
                include.Resolution == Mql5IncludeResolution.ResolvedInCorpus)))
            .AppendLine()
            .Append("- Missing/ambiguous/invalid include edges: ")
            .Append(evidence.Files.Sum(static file => file.Includes.Count(include =>
                include.Resolution is Mql5IncludeResolution.MissingSource
                    or Mql5IncludeResolution.Ambiguous
                    or Mql5IncludeResolution.Invalid)))
            .AppendLine()
            .Append("- Platform/search-path include edges: ")
            .Append(evidence.Files.Sum(static file => file.Includes.Count(include =>
                include.Resolution == Mql5IncludeResolution.PlatformLibrary)))
            .AppendLine()
            .Append("- Files with a reachable local include cycle: ")
            .Append(evidence.Files.Count(static file =>
                file.DependencyClosure.ReachableCycleMembers.Count > 0))
            .AppendLine()
            .AppendLine()
            .AppendLine("## Feature inventory")
            .AppendLine()
            .AppendLine("Features below are inherited from the bound static inventory. `SupportedSubsetCandidate` remains a lexical classification only.")
            .AppendLine()
            .AppendLine("| Feature | Classification | Files | Occurrences |")
            .AppendLine("|---|---|---:|---:|");
        foreach (var group in evidence.Files
                     .SelectMany(static file => file.StaticFeatures)
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
            .AppendLine("## Per-file evidence")
            .AppendLine()
            .AppendLine("Source bodies and arbitrary identifier/literal token values are excluded. Evidence intentionally retains relative file paths, declared include paths, known entrypoint names, feature codes/counts, locations, findings, and digests.")
            .AppendLine()
            .AppendLine("| File | Encoding | Source SHA-256 | Dependency SHA-256 | Evidence SHA-256 | Tokens | Functions | Dependency closure | Cycles | Disposition |")
            .AppendLine("|---|---|---|---|---|---:|---:|---:|---:|---|");
        foreach (Mql5ConversionFileEvidence file in evidence.Files)
        {
            report.Append("| ").Append(Escape(file.RelativePath))
                .Append(" | ").Append(Escape(file.TextEncoding))
                .Append(" | `").Append(file.SourceSha256).Append('`')
                .Append(" | `").Append(file.DependencyClosureSha256).Append('`')
                .Append(" | `").Append(file.EvidenceSha256).Append('`')
                .Append(" | ").Append(file.Lexical.TokenCount)
                .Append(" | ").Append(file.Structural.FunctionDefinitionCount)
                .Append(" | ").Append(file.DependencyClosure.TransitiveDependencies.Count)
                .Append(" | ").Append(file.DependencyClosure.ReachableCycleMembers.Count)
                .Append(" | ").Append(file.Disposition)
                .AppendLine(" |");
        }

        report.AppendLine()
            .AppendLine("## Required next gates")
            .AppendLine()
            .AppendLine("1. Supply every missing source dependency and version-bound platform library snapshot.")
            .AppendLine("2. Resolve all local include cycles or prove the exact preprocessor semantics in the identified toolchain.")
            .AppendLine("3. Run a complete grammar parser and MQL5 type checker in a network-denied isolated runner.")
            .AppendLine("4. Lower only explicitly supported, type-checked constructs into a versioned restricted IR and reject every unknown construct.")
            .AppendLine("5. Bind MetaEditor compile, deterministic simulation, reference trace, review, and demo-runtime evidence before deployment.");

        return report.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(
            JsonNamingPolicy.CamelCase,
            allowIntegerValues: false));
        return options;
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
