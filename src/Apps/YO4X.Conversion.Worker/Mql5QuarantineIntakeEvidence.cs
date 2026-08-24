using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using YO4X.StrategyGovernance;

namespace YO4X.Conversion.Worker;

public enum Mql5QuarantineClassification
{
    SourceLikeTextCandidate,
    LegacyMql4Source,
    CompiledMql4Binary,
    ZipArchive,
    OfficeDocumentContainer,
    UnknownQuarantined
}

public enum Mql5QuarantineArchiveState
{
    Inspected,
    ContainsUnavailableEntryContent,
    RejectedUnsafeMetadata,
    RejectedLimit,
    InvalidContainer
}

public enum Mql5QuarantineArchiveEntryContentState
{
    Directory,
    VerifiedDigest,
    Encrypted,
    UnsupportedCompression,
    Unreadable,
    IntegrityMismatch
}

public sealed record Mql5QuarantineCanonicalBinding(
    string CorpusSha256,
    int FileCount,
    long TotalBytes,
    IReadOnlyList<string> ExactExtensions);

public sealed record Mql5QuarantineIntakeLimits(
    int MaximumNonCanonicalFileCount,
    long MaximumNonCanonicalFileBytes,
    long MaximumNonCanonicalTotalBytes,
    int MaximumArchiveCount,
    int MaximumArchiveEntryCount,
    long MaximumArchiveEntryBytes,
    long MaximumArchiveTotalDeclaredBytes,
    int MaximumArchiveCompressionRatio,
    int MaximumRelativePathCharacters,
    int MaximumArchivePathDepth,
    int MaximumCanonicalMatchSamplesPerObject,
    int MaximumArtifactUtf8Bytes,
    int MaximumFilesystemEntryTraversalCount,
    int MaximumDirectoryTraversalCount);

public sealed record Mql5QuarantineArchiveEntryEvidence(
    string RelativePath,
    string Extension,
    long DeclaredLength,
    long CompressedLength,
    string Crc32,
    Mql5QuarantineArchiveEntryContentState ContentState,
    string? Sha256,
    int ExactCanonicalMatchCount,
    IReadOnlyList<string> ExactCanonicalMatchSamples,
    int ExactIntakeDuplicateCount);

public sealed record Mql5QuarantineArchiveEvidence(
    Mql5QuarantineArchiveState State,
    string? ReasonCode,
    int EntryCount,
    int FileEntryCount,
    long TotalDeclaredBytes,
    IReadOnlyList<Mql5QuarantineArchiveEntryEvidence> Entries);

public sealed record Mql5QuarantineFileEvidence(
    string RelativePath,
    string Extension,
    long ByteLength,
    string Sha256,
    Mql5QuarantineClassification Classification,
    string TextEncoding,
    IReadOnlyList<string> SourceSignalCodes,
    int ExactCanonicalMatchCount,
    IReadOnlyList<string> ExactCanonicalMatchSamples,
    int ExactIntakeDuplicateCount,
    Mql5QuarantineArchiveEvidence? Archive);

public sealed record Mql5QuarantineIntakeSummary(
    int NonCanonicalFileCount,
    long NonCanonicalTotalBytes,
    int SourceLikeTextCandidateCount,
    int LegacyMql4SourceCount,
    int CompiledMql4BinaryCount,
    int ArchiveCount,
    int OfficeDocumentContainerCount,
    int UnknownQuarantinedCount,
    int ArchiveEntryCount,
    int ArchiveFileEntryCount,
    int VerifiedArchiveFileEntryCount,
    int UnavailableArchiveFileEntryCount,
    int VerifiedObjectsMatchingCanonicalCount,
    int CanonicalPathsMatched,
    int ExactIntakeDuplicateGroupCount,
    int ConversionProofCount,
    int CompileProofCount,
    int RuntimeProofCount);

public sealed record Mql5QuarantineIntakeEvidence(
    string SchemaVersion,
    string AnalyzerVersion,
    string EvidenceSha256,
    Mql5QuarantineCanonicalBinding CanonicalCorpus,
    Mql5QuarantineIntakeLimits Limits,
    Mql5QuarantineIntakeSummary Summary,
    IReadOnlyList<Mql5QuarantineFileEvidence> Files);

public static class Mql5QuarantineIntakeFormatter
{
    public const string SchemaVersion = "yo4x.mql5-quarantine-intake.v2";
    public const string AnalyzerVersion = "1.1.0";

    private static readonly JsonSerializerOptions CompactJsonOptions = CreateJsonOptions(false);
    private static readonly JsonSerializerOptions IndentedJsonOptions = CreateJsonOptions(true);

    internal static Mql5QuarantineIntakeEvidence Create(
        Mql5QuarantineCanonicalBinding canonicalCorpus,
        Mql5QuarantineIntakeLimits limits,
        Mql5QuarantineIntakeSummary summary,
        IReadOnlyList<Mql5QuarantineFileEvidence> files)
    {
        ArgumentNullException.ThrowIfNull(canonicalCorpus);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(files);

        var ownedCanonicalCorpus = canonicalCorpus with
        {
            ExactExtensions = Own(canonicalCorpus.ExactExtensions)
        };
        Mql5QuarantineFileEvidence[] ownedFileArray = files.Select(static file => file with
        {
            SourceSignalCodes = Own(file.SourceSignalCodes),
            ExactCanonicalMatchSamples = Own(file.ExactCanonicalMatchSamples),
            Archive = file.Archive is null
                    ? null
                    : file.Archive with
                    {
                        Entries = Array.AsReadOnly(file.Archive.Entries.Select(
                            static entry => entry with
                            {
                                ExactCanonicalMatchSamples = Own(entry.ExactCanonicalMatchSamples)
                            }).ToArray())
                    }
        }).ToArray();
        var unsigned = new Mql5QuarantineIntakeEvidence(
            SchemaVersion,
            AnalyzerVersion,
            new string('0', 64),
            ownedCanonicalCorpus,
            limits,
            summary,
            Array.AsReadOnly(ownedFileArray));
        byte[] canonicalJson = JsonSerializer.SerializeToUtf8Bytes(unsigned, CompactJsonOptions);
        try
        {
            if (canonicalJson.Length > limits.MaximumArtifactUtf8Bytes)
            {
                throw new InvalidDataException(
                    "The quarantine evidence exceeds the bounded artifact limit.");
            }

            string digest = Convert.ToHexString(SHA256.HashData(canonicalJson)).ToLowerInvariant();
            return unsigned with { EvidenceSha256 = digest };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(canonicalJson);
        }
    }

    public static string ToJson(Mql5QuarantineIntakeEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return NormalizeLineEndings(JsonSerializer.Serialize(evidence, IndentedJsonOptions)) + "\n";
    }

    internal static bool HasValidEvidenceDigest(Mql5QuarantineIntakeEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (!IsSha256(evidence.EvidenceSha256))
        {
            return false;
        }

        byte[] canonicalJson = JsonSerializer.SerializeToUtf8Bytes(
            evidence with { EvidenceSha256 = new string('0', 64) },
            CompactJsonOptions);
        try
        {
            Span<byte> expected = stackalloc byte[32];
            byte[] supplied = Convert.FromHexString(evidence.EvidenceSha256);
            SHA256.HashData(canonicalJson, expected);
            return CryptographicOperations.FixedTimeEquals(expected, supplied);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(canonicalJson);
        }
    }

    public static string ToMarkdown(Mql5QuarantineIntakeEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var report = new StringBuilder();
        report.AppendLine("# MQL5 non-canonical quarantine intake report")
            .AppendLine()
            .AppendLine("This is deterministic metadata-only quarantine evidence for files that are not exact `.mq5` or `.mqh` inputs. It does not add any file to the canonical corpus, extract an archive to disk, load a compiled binary, parse a DOCX package, convert source, compile code, or run a strategy.")
            .AppendLine()
            .Append("- Schema: `").Append(evidence.SchemaVersion).AppendLine("`")
            .Append("- Analyzer: `").Append(evidence.AnalyzerVersion).AppendLine("`")
            .Append("- Evidence SHA-256: `").Append(evidence.EvidenceSha256).AppendLine("`")
            .Append("- Canonical corpus binding: `").Append(evidence.CanonicalCorpus.CorpusSha256)
            .Append("` (").Append(evidence.CanonicalCorpus.FileCount).Append(" files, ")
            .Append(evidence.CanonicalCorpus.TotalBytes).AppendLine(" bytes)")
            .Append("- Non-canonical files: ").Append(evidence.Summary.NonCanonicalFileCount)
            .Append(" (").Append(evidence.Summary.NonCanonicalTotalBytes).AppendLine(" bytes)")
            .Append("- Source-like text candidates: ")
            .Append(evidence.Summary.SourceLikeTextCandidateCount).AppendLine()
            .Append("- Legacy MQ4 sources: ").Append(evidence.Summary.LegacyMql4SourceCount)
            .AppendLine()
            .Append("- ZIP archives: ").Append(evidence.Summary.ArchiveCount)
            .Append("; entries: ").Append(evidence.Summary.ArchiveEntryCount)
            .Append("; verified file-entry digests: ")
            .Append(evidence.Summary.VerifiedArchiveFileEntryCount)
            .Append("; unavailable file-entry digests: ")
            .Append(evidence.Summary.UnavailableArchiveFileEntryCount).AppendLine()
            .Append("- Verified objects matching canonical content: ")
            .Append(evidence.Summary.VerifiedObjectsMatchingCanonicalCount)
            .Append("; matched canonical paths: ")
            .Append(evidence.Summary.CanonicalPathsMatched).AppendLine()
            .AppendLine("- Conversion, compile, and runtime proofs: 0 / 0 / 0")
            .AppendLine()
            .AppendLine("## Non-canonical files")
            .AppendLine()
            .AppendLine("| File | Bytes | SHA-256 | Quarantine classification | Source signals | Exact intake duplicates | Archive state |")
            .AppendLine("|---|---:|---|---|---:|---:|---|");

        foreach (Mql5QuarantineFileEvidence file in evidence.Files)
        {
            report.Append("| ").Append(Mql5MarkdownEscaper.EscapeTableCell(file.RelativePath))
                .Append(" | ").Append(file.ByteLength)
                .Append(" | `").Append(file.Sha256).Append('`')
                .Append(" | ").Append(file.Classification)
                .Append(" | ").Append(file.SourceSignalCodes.Count)
                .Append(" | ").Append(file.ExactIntakeDuplicateCount)
                .Append(" | ").Append(file.Archive?.State.ToString() ?? "-")
                .AppendLine(" |");
        }

        report.AppendLine()
            .AppendLine("## Archive entry metadata")
            .AppendLine()
            .AppendLine("Archive entries were streamed only to bounded hash/CRC verification; no entry was written to disk or loaded as code. Encrypted, unsupported, unsafe, oversized, or unreadable content remains unavailable and is never inferred from names or CRC values.")
            .AppendLine()
            .AppendLine("| Archive | Entry | Declared bytes | Compressed bytes | CRC-32 | Content state | SHA-256 | Canonical exact matches |")
            .AppendLine("|---|---|---:|---:|---|---|---|---:|");

        foreach (Mql5QuarantineFileEvidence file in evidence.Files)
        {
            if (file.Archive is null)
            {
                continue;
            }

            foreach (Mql5QuarantineArchiveEntryEvidence entry in file.Archive.Entries)
            {
                report.Append("| ").Append(Mql5MarkdownEscaper.EscapeTableCell(file.RelativePath))
                    .Append(" | ").Append(Mql5MarkdownEscaper.EscapeTableCell(entry.RelativePath))
                    .Append(" | ").Append(entry.DeclaredLength)
                    .Append(" | ").Append(entry.CompressedLength)
                    .Append(" | `").Append(entry.Crc32).Append('`')
                    .Append(" | ").Append(entry.ContentState)
                    .Append(" | ").Append(entry.Sha256 is null ? "-" : $"`{entry.Sha256}`")
                    .Append(" | ").Append(entry.ExactCanonicalMatchCount)
                    .AppendLine(" |");
            }
        }

        report.AppendLine()
            .AppendLine("## Honest blockers")
            .AppendLine()
            .AppendLine("- Source-like text and legacy MQ4 files are quarantine candidates only. They require explicit provenance/licensing, a deliberate rename/import decision, and the same isolated parse/type-check/conversion gates as any other source.")
            .AppendLine("- EX4/EX5 content is compiled code for a different trust lane. It is not source and is never loaded or treated as convertible.")
            .AppendLine("- Encrypted or unreadable archive entries have no verified content digest and therefore no exact-duplicate or compatibility claim.")
            .AppendLine("- DOCX containers are not inspected by this lane and cannot supply strategy source evidence.")
            .AppendLine("- Archive entry names and CRC-32 values are metadata, not authenticity, provenance, source equivalence, or execution evidence.");

        return NormalizeLineEndings(report.ToString());
    }

    private static string NormalizeLineEndings(string value) => value
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n');

    private static JsonSerializerOptions CreateJsonOptions(bool indented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = indented
        };
        options.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    private static bool IsSha256(string value) =>
        value is { Length: 64 }
        && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static ReadOnlyCollection<string> Own(IReadOnlyList<string> values) =>
        Array.AsReadOnly(values.ToArray());

}
