using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace YO4X.Runtime.Contracts;

public static class UserOperationProtocolVersions
{
    public const int DeliveryRequestedV4 = 4;

    public const int ReconciliationRequestedV3 = 3;

    public const int ResultV5 = 5;
}

public enum UserOperationObservationOutcome
{
    Succeeded = 0,
    Diverged = 1
}

/// <summary>
/// An opaque, canonical 256-bit bearer. Its value is deliberately omitted
/// from diagnostics; callers must opt in explicitly when crossing an
/// authenticated transport or PostgreSQL capability boundary.
/// </summary>
[DebuggerDisplay("[REDACTED]")]
public sealed class UserOperationBearer
{
    private readonly string value;

    private UserOperationBearer(string value)
    {
        this.value = value;
    }

    public static UserOperationBearer Create(string value)
    {
        UserOperationContractValidation.RequireBearer(value, nameof(value));
        return new UserOperationBearer(value);
    }

    public string DangerousGetValue() => value;

    public override string ToString() => "[REDACTED]";
}

internal static class UserOperationContractValidation
{
    private const int MaximumPayloadBytes = 64 * 1024;
    private const string UtcMicrosecondFormat = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";

    private static readonly Dictionary<string, string> TargetTypes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["broker_account.connection_test"] = "broker_account",
            ["broker_account.credential_rotation"] = "broker_account",
            ["broker_account.disable"] = "broker_account",
            ["broker_account.delete"] = "broker_account",
            ["deployment.start"] = "deployment",
            ["deployment.close_only"] = "deployment",
            ["deployment.stop_after_flat"] = "deployment"
        };

    public static void RequireIdentifier(Guid value, string name)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty identifier is required.", name);
        }
    }

    public static void RequireVersion(int actual, int expected, string name)
    {
        if (actual != expected)
        {
            throw new ArgumentOutOfRangeException(
                name,
                actual,
                $"Contract version {expected} is required.");
        }
    }

    public static void RequireOperationBinding(
        string operationType,
        string targetType,
        Guid targetId,
        Guid routeDeploymentId,
        long fenceGeneration,
        Guid workerAssignmentId,
        Guid workerInstanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationType);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetType);
        RequireIdentifier(targetId, nameof(targetId));
        RequireIdentifier(routeDeploymentId, nameof(routeDeploymentId));
        RequireIdentifier(workerAssignmentId, nameof(workerAssignmentId));
        RequireIdentifier(workerInstanceId, nameof(workerInstanceId));
        if (!TargetTypes.TryGetValue(operationType, out string? expectedTargetType)
            || !string.Equals(expectedTargetType, targetType, StringComparison.Ordinal)
            || fenceGeneration <= 0
            || targetType == "deployment" && routeDeploymentId != targetId)
        {
            throw new ArgumentException("The operation route binding is invalid.");
        }
    }

    public static void RequireCanonicalState(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        if (value.Length > 200
            || !string.Equals(value, value.Trim().ToLowerInvariant(), StringComparison.Ordinal)
            || value.Any(static character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '_' and not '-' and not '.' and not ':'))
        {
            throw new ArgumentException("The requested state is not canonical.", name);
        }
    }

    public static void RequireSha256(string value, string name)
    {
        ArgumentNullException.ThrowIfNull(value, name);
        if (value.Length != 64
            || value.Any(static character =>
                character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new ArgumentException("A lowercase SHA-256 digest is required.", name);
        }
    }

    public static void RequireBearer(string value, string name)
    {
        ArgumentNullException.ThrowIfNull(value, name);
        if (value.Length != 43
            || value.Any(static character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '_'))
        {
            throw new ArgumentException("A canonical 256-bit base64url bearer is required.", name);
        }

        byte[]? decoded = null;
        try
        {
            decoded = Convert.FromBase64String(
                value.Replace('-', '+').Replace('_', '/') + "=");
            if (decoded.Length != 32
                || !string.Equals(ToBase64Url(decoded), value, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A canonical 256-bit base64url bearer is required.",
                    name);
            }
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(
                "A canonical 256-bit base64url bearer is required.",
                name,
                exception);
        }
        finally
        {
            if (decoded is not null)
            {
                CryptographicOperations.ZeroMemory(decoded);
            }
        }
    }

    public static void RequireDistinctBearers(
        UserOperationBearer first,
        UserOperationBearer second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        byte[] firstBytes = Encoding.ASCII.GetBytes(first.DangerousGetValue());
        byte[] secondBytes = Encoding.ASCII.GetBytes(second.DangerousGetValue());
        try
        {
            if (CryptographicOperations.FixedTimeEquals(firstBytes, secondBytes))
            {
                throw new ArgumentException("Protocol bearers must be independently generated.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(firstBytes);
            CryptographicOperations.ZeroMemory(secondBytes);
        }
    }

    public static void RequireUtcMicrosecond(DateTimeOffset value, string name)
    {
        if (value == default
            || value.Offset != TimeSpan.Zero
            || value.Ticks % 10 != 0)
        {
            throw new ArgumentException(
                "A non-default UTC timestamp with microsecond precision is required.",
                name);
        }
    }

    public static string FormatUtcMicrosecond(DateTimeOffset value)
    {
        RequireUtcMicrosecond(value, nameof(value));
        return value.UtcDateTime.ToString(UtcMicrosecondFormat, CultureInfo.InvariantCulture);
    }

    public static DateTimeOffset ParseUtcMicrosecond(JsonElement value, string name)
    {
        string encoded = ReadStringValue(value, name);
        if (!DateTimeOffset.TryParseExact(
                encoded,
                UtcMicrosecondFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset parsed)
            || !string.Equals(FormatUtcMicrosecond(parsed), encoded, StringComparison.Ordinal))
        {
            throw InvalidPayload($"Property '{name}' is not a canonical UTC timestamp.");
        }

        return parsed;
    }

    public static JsonDocument ParseCanonicalDocument(string canonicalJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalJson);
        if (Encoding.UTF8.GetByteCount(canonicalJson) > MaximumPayloadBytes)
        {
            throw InvalidPayload("The protocol payload exceeds its maximum size.");
        }

        try
        {
            JsonDocument document = JsonDocument.Parse(
                canonicalJson,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 8
                });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                throw InvalidPayload("The protocol payload must be a JSON object.");
            }

            return document;
        }
        catch (JsonException exception)
        {
            throw InvalidPayload("The protocol payload is not valid JSON.", exception);
        }
    }

    public static void RequireExactProperties(JsonElement root, IReadOnlyList<string> names)
    {
        JsonProperty[] properties = root.EnumerateObject().ToArray();
        if (properties.Length != names.Count)
        {
            throw InvalidPayload("The protocol payload has missing, duplicate, or unknown properties.");
        }

        for (int index = 0; index < names.Count; index++)
        {
            if (!string.Equals(properties[index].Name, names[index], StringComparison.Ordinal))
            {
                throw InvalidPayload("The protocol payload is not in canonical property order.");
            }
        }
    }

    public static string ReadString(JsonElement root, string name) =>
        ReadStringValue(RequireProperty(root, name), name);

    public static Guid ReadGuid(JsonElement root, string name)
    {
        string encoded = ReadString(root, name);
        if (!Guid.TryParseExact(encoded, "D", out Guid parsed)
            || !string.Equals(parsed.ToString("D"), encoded, StringComparison.Ordinal))
        {
            throw InvalidPayload($"Property '{name}' is not a canonical UUID.");
        }

        return parsed;
    }

    public static long ReadInt64(JsonElement root, string name)
    {
        JsonElement property = RequireProperty(root, name);
        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt64(out long value))
        {
            throw InvalidPayload($"Property '{name}' is not an integer.");
        }

        return value;
    }

    public static int ReadInt32(JsonElement root, string name)
    {
        JsonElement property = RequireProperty(root, name);
        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out int value))
        {
            throw InvalidPayload($"Property '{name}' is not an integer.");
        }

        return value;
    }

    public static bool ReadBoolean(JsonElement root, string name)
    {
        JsonElement property = RequireProperty(root, name);
        if (property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw InvalidPayload($"Property '{name}' is not a boolean.");
        }

        return property.GetBoolean();
    }

    public static JsonElement ReadObject(JsonElement root, string name)
    {
        JsonElement property = RequireProperty(root, name);
        if (property.ValueKind != JsonValueKind.Object)
        {
            throw InvalidPayload($"Property '{name}' is not an object.");
        }

        return property;
    }

    public static DateTimeOffset ReadUtcMicrosecond(JsonElement root, string name) =>
        ParseUtcMicrosecond(RequireProperty(root, name), name);

    public static UserOperationObservationOutcome ReadOutcome(JsonElement root, string name) =>
        ReadString(root, name) switch
        {
            "succeeded" => UserOperationObservationOutcome.Succeeded,
            "diverged" => UserOperationObservationOutcome.Diverged,
            _ => throw InvalidPayload("Only conclusive succeeded or diverged observations are accepted.")
        };

    public static string Outcome(UserOperationObservationOutcome outcome) => outcome switch
    {
        UserOperationObservationOutcome.Succeeded => "succeeded",
        UserOperationObservationOutcome.Diverged => "diverged",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome))
    };

    public static string WriteCanonical(Action<Utf8JsonWriter> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(
            buffer,
            new JsonWriterOptions { Indented = false, SkipValidation = false }))
        {
            write(writer);
            writer.Flush();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static void RequireCanonicalRoundTrip(string supplied, string canonical)
    {
        if (!string.Equals(supplied, canonical, StringComparison.Ordinal))
        {
            throw InvalidPayload("The protocol payload is not canonically encoded.");
        }
    }

    public static InvalidDataException InvalidPayload(string message, Exception? inner = null) =>
        new(message, inner);

    private static JsonElement RequireProperty(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement property))
        {
            throw InvalidPayload($"Property '{name}' is required.");
        }

        return property;
    }

    private static string ReadStringValue(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not string text)
        {
            throw InvalidPayload($"Property '{name}' is not a string.");
        }

        return text;
    }

    private static string ToBase64Url(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}
