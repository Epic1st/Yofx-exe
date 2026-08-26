using System.Security.Cryptography;
using System.Text;

namespace YO4X.StrategyGovernance;

public enum Mql5RestrictedCorpusDisposition
{
    NotEligible,
    Failed,
    Lowered
}

public sealed record Mql5RestrictedCorpusFileResult(
    string RelativePath,
    string SourceSha256,
    Mql5RestrictedCorpusDisposition Disposition,
    string? IrSha256,
    Mql5RestrictedIr? Ir,
    IReadOnlyList<Mql5RestrictedDiagnostic> Diagnostics);

public sealed record Mql5RestrictedCorpusArtifact(
    string SchemaVersion,
    string CompilerVersion,
    string InputCorpusSha256,
    string InputConversionEvidenceSha256,
    string ArtifactSha256,
    int FileCount,
    int AttemptedCount,
    int LoweredCount,
    int FailedCount,
    IReadOnlyList<Mql5RestrictedCorpusFileResult> Files);

public static class Mql5RestrictedCorpusCompiler
{
    public const string SchemaVersion = "yo4x.mql5.restricted-corpus.v1";
    private const int MaximumPersistedDiagnosticsPerFile = 32;

    public static Mql5RestrictedCorpusArtifact Compile(
        Mql5CorpusManifest manifest,
        Mql5ConversionCorpusEvidence conversionEvidence,
        IEnumerable<Mql5SourceDocument> sourceDocuments)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(conversionEvidence);
        ArgumentNullException.ThrowIfNull(sourceDocuments);
        if (!string.Equals(manifest.CorpusSha256, conversionEvidence.InputCorpusSha256, StringComparison.Ordinal)
            || manifest.FileCount != conversionEvidence.FileCount)
        {
            throw new ArgumentException("Conversion evidence is not bound to the supplied corpus manifest.");
        }

        Mql5SourceDocument[] documents = sourceDocuments.ToArray();
        Mql5ConversionCorpusEvidence rebuiltConversionEvidence =
            new Mql5ConversionEvidenceAnalyzer().Analyze(documents);
        if (!FixedTimeSha256Equals(
                rebuiltConversionEvidence.EvidenceSha256,
                conversionEvidence.EvidenceSha256))
        {
            throw new ArgumentException(
                "Conversion evidence does not match evidence rebuilt from the supplied source bytes.",
                nameof(conversionEvidence));
        }

        var documentsByPath = documents.ToDictionary(
            static document => document.RelativePath,
            StringComparer.OrdinalIgnoreCase);
        var evidenceByPath = conversionEvidence.Files.ToDictionary(
            static file => file.RelativePath,
            StringComparer.OrdinalIgnoreCase);
        if (documentsByPath.Count != manifest.FileCount
            || evidenceByPath.Count != manifest.FileCount)
        {
            throw new ArgumentException("Every manifest file must have one unique document and evidence record.");
        }

        var results = new List<Mql5RestrictedCorpusFileResult>(manifest.FileCount);
        foreach (Mql5SourceManifest file in manifest.Files)
        {
            if (!documentsByPath.TryGetValue(file.RelativePath, out Mql5SourceDocument? document)
                || !evidenceByPath.TryGetValue(file.RelativePath, out Mql5ConversionFileEvidence? evidence)
                || !string.Equals(file.Sha256, evidence.SourceSha256, StringComparison.Ordinal)
                || !CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(file.Sha256),
                    SHA256.HashData(document.Content)))
            {
                throw new ArgumentException("A source document or evidence record is not hash-bound to its manifest file.");
            }

            if (evidence.Disposition != Mql5ConversionEvidenceDisposition.AwaitingIsolatedTypeCheck)
            {
                results.Add(new(
                    file.RelativePath,
                    file.Sha256,
                    Mql5RestrictedCorpusDisposition.NotEligible,
                    null,
                    null,
                    []));
                continue;
            }

            Mql5RestrictedCompilation compilation = Mql5RestrictedSubsetCompiler.Compile(document);
            IReadOnlyList<Mql5RestrictedDiagnostic> persistedDiagnostics =
                BoundDiagnostics(compilation.Diagnostics);
            results.Add(new(
                file.RelativePath,
                file.Sha256,
                compilation.Succeeded
                    ? Mql5RestrictedCorpusDisposition.Lowered
                    : Mql5RestrictedCorpusDisposition.Failed,
                compilation.Ir?.IrSha256,
                compilation.Ir,
                persistedDiagnostics));
        }

        int lowered = results.Count(static result => result.Disposition == Mql5RestrictedCorpusDisposition.Lowered);
        int failed = results.Count(static result => result.Disposition == Mql5RestrictedCorpusDisposition.Failed);
        var artifact = new Mql5RestrictedCorpusArtifact(
            SchemaVersion,
            Mql5RestrictedSubsetCompiler.CompilerVersion,
            manifest.CorpusSha256,
            conversionEvidence.EvidenceSha256,
            string.Empty,
            results.Count,
            lowered + failed,
            lowered,
            failed,
            results);
        string hash = Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(Mql5RestrictedCorpusArtifactFormatter.ToHashPayload(artifact))));
        return artifact with { ArtifactSha256 = hash };
    }

    private static IReadOnlyList<Mql5RestrictedDiagnostic> BoundDiagnostics(
        IReadOnlyList<Mql5RestrictedDiagnostic> diagnostics)
    {
        if (diagnostics.Count <= MaximumPersistedDiagnosticsPerFile)
        {
            return diagnostics;
        }

        return diagnostics
            .Take(MaximumPersistedDiagnosticsPerFile)
            .Append(new Mql5RestrictedDiagnostic(
                "DIAGNOSTICS_TRUNCATED",
                Mql5RestrictedDiagnosticSeverity.Information,
                $"Only the first {MaximumPersistedDiagnosticsPerFile} diagnostics are retained in the corpus artifact.",
                1,
                1))
            .ToArray();
    }

    private static bool FixedTimeSha256Equals(string left, string right)
    {
        try
        {
            byte[] leftBytes = Convert.FromHexString(left);
            byte[] rightBytes = Convert.FromHexString(right);
            try
            {
                return leftBytes.Length == 32
                    && rightBytes.Length == 32
                    && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(leftBytes);
                CryptographicOperations.ZeroMemory(rightBytes);
            }
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
