using System.Text.Json;
using System.Text.Json.Serialization;

namespace YO4X.StrategyGovernance;

public static class Mql5RestrictedCorpusArtifactFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static string ToJson(Mql5RestrictedCorpusArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        return NormalizeLineEndings(JsonSerializer.Serialize(artifact, JsonOptions)) + "\n";
    }

    internal static string ToHashPayload(Mql5RestrictedCorpusArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        return NormalizeLineEndings(JsonSerializer.Serialize(
            artifact with { ArtifactSha256 = string.Empty },
            JsonOptions));
    }

    private static string NormalizeLineEndings(string value) => value
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n');

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
}
