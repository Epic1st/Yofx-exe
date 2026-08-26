using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace YO4X.Backtest.Runner;

/// <summary>One corpus file as the verified manifest records it.</summary>
public sealed record CorpusFile(string RelativePath, string Sha256);

/// <summary>
/// Recovers the MQL5 source behind a catalog strategy identifier.
///
/// <para>
/// The identifier is not stored anywhere alongside a path — it is derived from the corpus
/// digest and the file's relative path. So the mapping is re-derived here and checked,
/// rather than trusted: a row whose identifier does not follow from the file it names is
/// refused, because executing the wrong source under a strategy's name would attribute one
/// program's results to another.
/// </para>
/// </summary>
public sealed class StrategySourceResolver
{
    private readonly Dictionary<Guid, CorpusFile> byIdentifier = [];
    private readonly string corpusRoot;

    private StrategySourceResolver(string corpusRoot, string corpusSha256)
    {
        this.corpusRoot = corpusRoot;
        CorpusSha256 = corpusSha256;
    }

    /// <summary>The digest the identifiers were derived from.</summary>
    public string CorpusSha256 { get; }

    /// <summary>How many files the manifest lists.</summary>
    public int Count => byIdentifier.Count;

    /// <summary>Reads the static manifest and derives an identifier for every file it lists.</summary>
    public static StrategySourceResolver Load(string manifestPath, string corpusRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(corpusRoot);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        JsonElement root = document.RootElement;
        string corpusSha256 = root.GetProperty("corpusSha256").GetString()
            ?? throw new InvalidDataException("The manifest carries no corpus digest.");

        var resolver = new StrategySourceResolver(Path.GetFullPath(corpusRoot), corpusSha256);
        foreach (JsonElement file in root.GetProperty("files").EnumerateArray())
        {
            string relativePath = file.GetProperty("relativePath").GetString()
                ?? throw new InvalidDataException("A manifest entry carries no relative path.");
            string sha256 = file.GetProperty("sha256").GetString()
                ?? throw new InvalidDataException("A manifest entry carries no digest.");
            resolver.byIdentifier[DeriveIdentifier(corpusSha256, relativePath)] =
                new CorpusFile(relativePath, sha256);
        }

        return resolver;
    }

    /// <summary>
    /// Returns the source bytes for one strategy identifier, or null when the identifier is
    /// not in the manifest. The file is re-hashed and refused if it has drifted from what
    /// the manifest recorded.
    /// </summary>
    public bool TryRead(Guid strategyId, out CorpusFile file, out byte[] content, out string? refusal)
    {
        content = [];
        refusal = null;
        if (!byIdentifier.TryGetValue(strategyId, out CorpusFile? found))
        {
            file = new CorpusFile(string.Empty, string.Empty);
            refusal = "No file in the verified corpus derives this strategy identifier.";
            return false;
        }

        file = found;
        string path = Path.Combine(corpusRoot, found.RelativePath);
        if (!File.Exists(path))
        {
            refusal = "The corpus file named by the manifest is not on disk: " + found.RelativePath;
            return false;
        }

        byte[] bytes = File.ReadAllBytes(path);
        string actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(actual, found.Sha256, StringComparison.Ordinal))
        {
            refusal = "The corpus file has changed since it was verified: " + found.RelativePath;
            return false;
        }

        content = bytes;
        return true;
    }

    /// <summary>
    /// Reproduces the identifier scheme the catalog projection uses: the first sixteen bytes
    /// of SHA-256 over "corpusDigest relativePath", with the version nibble forced to 7 and
    /// the variant nibble folded into 8, 9, a or b.
    /// </summary>
    public static Guid DeriveIdentifier(string corpusSha256, string relativePath)
    {
        byte[] material = Encoding.UTF8.GetBytes(corpusSha256 + " " + relativePath);
        char[] hex = Convert.ToHexString(SHA256.HashData(material))
            .ToLowerInvariant()
            .AsSpan(0, 32)
            .ToArray();
        CryptographicOperations.ZeroMemory(material);

        hex[12] = '7';
        hex[16] = "89ab"[int.Parse(
            hex[16].ToString(),
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture) % 4];
        var value = new string(hex);
        return Guid.ParseExact(
            string.Concat(
                value[..8], "-", value[8..12], "-", value[12..16], "-", value[16..20], "-", value[20..]),
            "D");
    }
}
