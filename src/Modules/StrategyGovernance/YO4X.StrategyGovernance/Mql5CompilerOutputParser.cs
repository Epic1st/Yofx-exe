using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YO4X.BuildingBlocks;

namespace YO4X.StrategyGovernance;

/// <summary>
/// Parses the bounded, normalized JSON-lines protocol emitted by an isolated runner.
/// It deliberately does not parse localized MetaEditor console text on the control host.
/// </summary>
public static class Mql5CompilerOutputParser
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] RecordProperties =
    [
        "artifactSha256",
        "diagnostics",
        "exitCode",
        "relativePath",
        "repeatArtifactSha256",
        "sourceSha256",
        "status"
    ];

    private static readonly string[] DiagnosticProperties =
    [
        "code",
        "column",
        "line",
        "message",
        "severity"
    ];

    public static IReadOnlyList<Mql5FileCompileEvidence> Parse(
        byte[] outputUtf8,
        int maximumBytes,
        int maximumRecords)
    {
        ArgumentNullException.ThrowIfNull(outputUtf8);
        if (maximumBytes is < 1 or > 16 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        if (maximumRecords is < 1 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRecords));
        }

        if (outputUtf8.Length > maximumBytes)
        {
            throw new Mql5CompilerOutputException("COMPILER_OUTPUT_LIMIT_EXCEEDED");
        }

        string output;
        try
        {
            output = StrictUtf8.GetString(outputUtf8);
        }
        catch (DecoderFallbackException)
        {
            throw new Mql5CompilerOutputException("COMPILER_OUTPUT_UTF8_INVALID");
        }

        if (output.Length == 0)
        {
            return [];
        }

        string[] lines = output.Split('\n');
        if (lines[^1].Length == 0)
        {
            lines = lines[..^1];
        }

        if (lines.Length > maximumRecords)
        {
            throw new Mql5CompilerOutputException("COMPILER_OUTPUT_RECORD_LIMIT_EXCEEDED");
        }

        var results = new List<Mql5FileCompileEvidence>(lines.Length);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (string rawLine in lines)
        {
            string line = rawLine.EndsWith('\r') ? rawLine[..^1] : rawLine;
            if (line.Length is < 2 or > 64 * 1024)
            {
                throw new Mql5CompilerOutputException("COMPILER_OUTPUT_RECORD_INVALID");
            }

            results.Add(ParseLine(line, paths));
        }

        return results
            .OrderBy(static result => result.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static Mql5FileCompileEvidence ParseLine(string line, HashSet<string> paths)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8
            });
        }
        catch (JsonException)
        {
            throw new Mql5CompilerOutputException("COMPILER_OUTPUT_JSON_INVALID");
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            RequireExactProperties(root, RecordProperties);

            string relativePath = GetRequiredString(root, "relativePath", 500);
            string sourceSha256 = GetRequiredString(root, "sourceSha256", 64);
            string statusValue = GetRequiredString(root, "status", 20);
            int exitCode = GetRequiredInt32(root, "exitCode");
            string? artifactSha256 = GetOptionalString(root, "artifactSha256", 64);
            string? repeatArtifactSha256 = GetOptionalString(root, "repeatArtifactSha256", 64);

            if (!Mql5CompileValidation.IsSafeRelativeSourcePath(relativePath)
                || !Path.GetExtension(relativePath).Equals(".mq5", StringComparison.OrdinalIgnoreCase)
                || !paths.Add(relativePath))
            {
                throw new Mql5CompilerOutputException("COMPILER_OUTPUT_PATH_INVALID");
            }

            if (!Mql5CompileValidation.IsExactSha256(sourceSha256)
                || artifactSha256 is not null && !Mql5CompileValidation.IsExactSha256(artifactSha256)
                || repeatArtifactSha256 is not null && !Mql5CompileValidation.IsExactSha256(repeatArtifactSha256))
            {
                throw new Mql5CompilerOutputException("COMPILER_OUTPUT_DIGEST_INVALID");
            }

            Mql5FileCompileStatus status = statusValue switch
            {
                "succeeded" => Mql5FileCompileStatus.Succeeded,
                "failed" => Mql5FileCompileStatus.Failed,
                _ => throw new Mql5CompilerOutputException("COMPILER_OUTPUT_STATUS_INVALID")
            };

            if (status == Mql5FileCompileStatus.Succeeded
                && (exitCode != 0 || artifactSha256 is null || repeatArtifactSha256 is null))
            {
                throw new Mql5CompilerOutputException("COMPILER_OUTPUT_SUCCESS_EVIDENCE_INVALID");
            }

            JsonElement diagnosticsElement = root.GetProperty("diagnostics");
            if (diagnosticsElement.ValueKind != JsonValueKind.Array
                || diagnosticsElement.GetArrayLength() > 200)
            {
                throw new Mql5CompilerOutputException("COMPILER_OUTPUT_DIAGNOSTICS_INVALID");
            }

            Mql5CompilerDiagnosticEvidence[] diagnostics = diagnosticsElement
                .EnumerateArray()
                .Select(ParseDiagnostic)
                .OrderBy(static diagnostic => diagnostic.Line)
                .ThenBy(static diagnostic => diagnostic.Column)
                .ThenBy(static diagnostic => diagnostic.Severity, StringComparer.Ordinal)
                .ThenBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ThenBy(static diagnostic => diagnostic.MessageSha256, StringComparer.Ordinal)
                .ToArray();

            string evidenceSha256 = CanonicalJson.Sha256(new
            {
                RelativePath = relativePath,
                SourceSha256 = sourceSha256,
                Status = status,
                ExitCode = exitCode,
                ArtifactSha256 = artifactSha256,
                RepeatArtifactSha256 = repeatArtifactSha256,
                Diagnostics = diagnostics
            });

            return new Mql5FileCompileEvidence(
                relativePath,
                sourceSha256,
                status,
                exitCode,
                artifactSha256,
                repeatArtifactSha256,
                diagnostics,
                evidenceSha256);
        }
    }

    private static Mql5CompilerDiagnosticEvidence ParseDiagnostic(JsonElement element)
    {
        RequireExactProperties(element, DiagnosticProperties);
        string severity = GetRequiredString(element, "severity", 20);
        string code = GetRequiredString(element, "code", 100);
        int line = GetRequiredInt32(element, "line");
        int column = GetRequiredInt32(element, "column");
        string message = GetRequiredString(element, "message", 2048);

        if (severity is not ("error" or "warning" or "info")
            || !Mql5CompileValidation.IsSafeToken(code, 100)
            || line < 0
            || column < 0)
        {
            throw new Mql5CompilerOutputException("COMPILER_OUTPUT_DIAGNOSTIC_INVALID");
        }

        byte[] messageBytes = Encoding.UTF8.GetBytes(message);
        try
        {
            return new Mql5CompilerDiagnosticEvidence(
                severity,
                code,
                line,
                column,
                Convert.ToHexString(SHA256.HashData(messageBytes)).ToLowerInvariant());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(messageBytes);
        }
    }

    private static void RequireExactProperties(JsonElement element, IReadOnlyList<string> expected)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new Mql5CompilerOutputException("COMPILER_OUTPUT_SHAPE_INVALID");
        }

        string[] actual = element.EnumerateObject()
            .Select(static property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new Mql5CompilerOutputException("COMPILER_OUTPUT_SHAPE_INVALID");
        }
    }

    private static string GetRequiredString(JsonElement element, string name, int maximumLength)
    {
        JsonElement value = element.GetProperty(name);
        string? text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (text is not { Length: >= 1 } || text.Length > maximumLength)
        {
            throw new Mql5CompilerOutputException("COMPILER_OUTPUT_VALUE_INVALID");
        }

        return text;
    }

    private static string? GetOptionalString(JsonElement element, string name, int maximumLength)
    {
        JsonElement value = element.GetProperty(name);
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return GetRequiredString(element, name, maximumLength);
    }

    private static int GetRequiredInt32(JsonElement element, string name)
    {
        JsonElement value = element.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number))
        {
            throw new Mql5CompilerOutputException("COMPILER_OUTPUT_VALUE_INVALID");
        }

        return number;
    }
}

public sealed class Mql5CompilerOutputException : Exception
{
    public Mql5CompilerOutputException(string reasonCode)
        : base("The isolated compiler output did not match the bounded evidence protocol.")
    {
        ReasonCode = reasonCode;
    }

    public string ReasonCode { get; }
}
