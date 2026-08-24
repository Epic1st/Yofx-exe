using System.Globalization;
using System.Text.Json;

namespace YO4X.Outbox;

/// <summary>
/// Resolves the immutable transport schema version from a canonical message
/// type and verifies versioned wire payloads against the persisted outbox
/// column. Unversioned legacy evidence remains schema version 1.
/// </summary>
public static class OutboxSchemaVersion
{
    public const int Maximum = 100;

    public static int ResolveForNewMessage(string messageType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        int markerIndex = VersionMarkerIndex(messageType);
        if (markerIndex < 0)
        {
            return 1;
        }

        ReadOnlySpan<char> encodedVersion = messageType.AsSpan(markerIndex + 2);
        if (!int.TryParse(
                encodedVersion,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int version)
            || version is < 1 or > Maximum
            || !encodedVersion.SequenceEqual(
                version.ToString(CultureInfo.InvariantCulture).AsSpan()))
        {
            throw new ArgumentException(
                "The message type has an invalid schema-version suffix.",
                nameof(messageType));
        }

        return version;
    }

    public static int ValidateStored(
        string messageType,
        int storedVersion,
        string payloadJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        if (storedVersion is < 1 or > Maximum)
        {
            throw new InvalidDataException("The immutable outbox schema version is invalid.");
        }

        int markerIndex = VersionMarkerIndex(messageType);
        if (markerIndex < 0)
        {
            return storedVersion;
        }

        int resolvedVersion;
        try
        {
            resolvedVersion = ResolveForNewMessage(messageType);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "The outbox message type has an invalid schema-version suffix.",
                exception);
        }

        if (storedVersion != resolvedVersion)
        {
            throw new InvalidDataException(
                "The immutable outbox schema version does not match its message type.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(
                payloadJson,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64
                });
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                bool requiresExactSchemaVersion =
                    messageType.StartsWith("yo4x.", StringComparison.Ordinal)
                    && (messageType.EndsWith(".requested.v4", StringComparison.Ordinal)
                        || string.Equals(
                            messageType,
                            "yo4x.user-operation.reconciliation-requested.v3",
                            StringComparison.Ordinal));
                ValidateNumericVersionProperty(
                    document.RootElement,
                    "schemaVersion",
                    resolvedVersion,
                    requiresExactSchemaVersion);
                ValidateNumericVersionProperty(
                    document.RootElement,
                    "contractVersion",
                    resolvedVersion,
                    required: false);
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The outbox payload is not valid JSON.", exception);
        }

        return storedVersion;
    }

    private static int VersionMarkerIndex(string messageType)
    {
        int lastSeparator = messageType.LastIndexOf('.');
        return lastSeparator >= 0
            && lastSeparator + 2 < messageType.Length
            && messageType[lastSeparator + 1] == 'v'
            && char.IsAsciiDigit(messageType[lastSeparator + 2])
                ? lastSeparator
                : -1;
    }

    private static void ValidateNumericVersionProperty(
        JsonElement payload,
        string propertyName,
        int expectedVersion,
        bool required)
    {
        JsonElement versionProperty = default;
        int occurrenceCount = 0;
        foreach (JsonProperty property in payload.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.Ordinal))
            {
                occurrenceCount++;
                versionProperty = property.Value;
            }
        }

        if (occurrenceCount > 1)
        {
            throw new InvalidDataException(
                $"The outbox payload contains duplicate '{propertyName}' properties.");
        }

        if (occurrenceCount == 0)
        {
            if (required)
            {
                throw new InvalidDataException(
                    $"The outbox payload requires numeric '{propertyName}'.");
            }

            return;
        }

        // Some non-wire domain evidence uses a string-valued schemaVersion.
        // Numeric wire versions, when present, must agree with the immutable
        // message-type suffix propagated in the delivery envelope.
        if (versionProperty.ValueKind != JsonValueKind.Number)
        {
            if (required)
            {
                throw new InvalidDataException(
                    $"The outbox payload requires numeric '{propertyName}'.");
            }

            return;
        }

        if (!versionProperty.TryGetInt32(out int actualVersion)
            || actualVersion != expectedVersion)
        {
            throw new InvalidDataException(
                $"The outbox payload '{propertyName}' does not match its message type.");
        }
    }
}
