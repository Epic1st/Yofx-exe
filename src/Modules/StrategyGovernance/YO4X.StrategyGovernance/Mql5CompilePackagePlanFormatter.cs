using System.Text.Json;
using System.Text.Json.Serialization;

namespace YO4X.StrategyGovernance;

public static class Mql5CompilePackagePlanFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static string ToJson(Mql5CompilePackagePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        string serialized = JsonSerializer.Serialize(plan, JsonOptions)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        return serialized + "\n";
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
}
