using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YO4X.BuildingBlocks;

namespace YO4X.ControlPlane.Workers.Outbox;

public sealed record OutboxDeliveryEnvelope(
    string ContractVersion,
    string StableMessageId,
    string IdempotencyKey,
    Guid MessageId,
    Guid TenantId,
    string MessageType,
    int SchemaVersion,
    string PayloadJson,
    string PayloadSha256,
    DateTimeOffset OccurredAtUtc,
    int Attempt)
{
    public const string CurrentContractVersion = "outbox-delivery.v1";

    public static OutboxDeliveryEnvelope Create(ClaimedOutboxItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!PayloadHash.TryCanonicalizeAndMatch(
                item.PayloadJson,
                item.PayloadSha256,
                out string canonicalPayload))
        {
            throw new InvalidDataException("The claimed outbox payload does not match its immutable hash.");
        }

        string stableMessageId = $"{item.TenantId:N}/{item.MessageId:N}";
        return new OutboxDeliveryEnvelope(
            CurrentContractVersion,
            stableMessageId,
            $"yo4x-outbox-v1/{stableMessageId}",
            item.MessageId,
            item.TenantId,
            item.MessageType,
            item.SchemaVersion,
            canonicalPayload,
            item.PayloadSha256,
            item.OccurredAtUtc,
            item.Attempt);
    }
}

internal static class PayloadHash
{
    public static bool IsSha256(string value)
    {
        if (value.Length != 64)
        {
            return false;
        }

        return value.All(Uri.IsHexDigit);
    }

    public static bool Matches(string payloadJson, string expectedHash)
        => TryCanonicalizeAndMatch(payloadJson, expectedHash, out _);

    public static bool TryCanonicalizeAndMatch(
        string payloadJson,
        string expectedHash,
        out string canonicalPayload)
    {
        canonicalPayload = string.Empty;
        if (!IsSha256(expectedHash))
        {
            return false;
        }

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(payloadJson);
        }
        catch (JsonException)
        {
            return false;
        }

        if (parsed is null)
        {
            return false;
        }

        canonicalPayload = CanonicalJson.Serialize(parsed);
        byte[] payloadBytes = Encoding.UTF8.GetBytes(canonicalPayload);
        byte[] expected = Convert.FromHexString(expectedHash);
        try
        {
            byte[] actual = SHA256.HashData(payloadBytes);
            try
            {
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(actual);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payloadBytes);
            CryptographicOperations.ZeroMemory(expected);
        }
    }
}
