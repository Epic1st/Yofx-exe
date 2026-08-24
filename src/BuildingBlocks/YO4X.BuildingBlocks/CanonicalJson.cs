using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace YO4X.BuildingBlocks;

public static class CanonicalJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static string Serialize<T>(T value)
    {
        JsonNode? node = JsonSerializer.SerializeToNode(value, SerializerOptions);
        JsonNode? normalized = Normalize(node);
        return normalized?.ToJsonString(SerializerOptions) ?? "null";
    }

    public static string Sha256<T>(T value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(Serialize(value));
        try
        {
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static JsonNode? Normalize(JsonNode? node) => node switch
    {
        JsonObject value => NormalizeObject(value),
        JsonArray value => NormalizeArray(value),
        _ => node?.DeepClone()
    };

    private static JsonObject NormalizeObject(JsonObject value)
    {
        var normalized = new JsonObject();
        foreach ((string name, JsonNode? child) in value.OrderBy(property => property.Key, StringComparer.Ordinal))
        {
            normalized.Add(name, Normalize(child));
        }

        return normalized;
    }

    private static JsonArray NormalizeArray(JsonArray value)
    {
        var normalized = new JsonArray();
        foreach (JsonNode? child in value)
        {
            normalized.Add(Normalize(child));
        }

        return normalized;
    }
}
