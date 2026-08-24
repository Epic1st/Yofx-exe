using System.Text.Json.Nodes;
using YO4X.BuildingBlocks;
using YO4X.Runtime.Contracts;

namespace YO4X.Strategy.Abstractions;

public enum StrategyPositionSide
{
    Buy = 0,
    Sell = 1
}

public sealed record StrategyAccountSnapshot(
    long Sequence,
    decimal Balance,
    decimal Equity,
    decimal FreeMargin,
    string Currency);

public sealed record StrategyQuoteSnapshot(
    long Sequence,
    string Symbol,
    decimal Bid,
    decimal Ask,
    DateTimeOffset ObservedAtUtc);

public sealed record StrategyPositionSnapshot(
    string PositionId,
    string Symbol,
    StrategyPositionSide Side,
    decimal Volume,
    decimal OpenPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    bool OwnedByDeployment);

public sealed record StrategyPendingOrderSnapshot(
    string OrderId,
    string Symbol,
    StrategyPositionSide Side,
    decimal Volume,
    decimal RequestedPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    bool OwnedByDeployment);

public sealed class StrategySnapshot
{
    public const int MaximumQuoteCount = 10_000;
    public const int MaximumPositionCount = 10_000;
    public const int MaximumPendingOrderCount = 10_000;

    private StrategySnapshot(
        long sequence,
        DateTimeOffset asOfUtc,
        DateTimeOffset deterministicNowUtc,
        StrategyAccountSnapshot account,
        IReadOnlyList<StrategyQuoteSnapshot> quotes,
        IReadOnlyList<StrategyPositionSnapshot> positions,
        IReadOnlyList<StrategyPendingOrderSnapshot> pendingOrders)
    {
        ContractVersion = RuntimeContractVersions.StrategySnapshotV1;
        Sequence = sequence;
        AsOfUtc = asOfUtc.ToUniversalTime();
        DeterministicNowUtc = deterministicNowUtc.ToUniversalTime();
        Account = account;
        Quotes = quotes;
        Positions = positions;
        PendingOrders = pendingOrders;
    }

    public int ContractVersion { get; }

    public long Sequence { get; }

    public DateTimeOffset AsOfUtc { get; }

    public DateTimeOffset DeterministicNowUtc { get; }

    public StrategyAccountSnapshot Account { get; }

    public IReadOnlyList<StrategyQuoteSnapshot> Quotes { get; }

    public IReadOnlyList<StrategyPositionSnapshot> Positions { get; }

    public IReadOnlyList<StrategyPendingOrderSnapshot> PendingOrders { get; }

    public static StrategySnapshot Create(
        long sequence,
        DateTimeOffset asOfUtc,
        DateTimeOffset deterministicNowUtc,
        StrategyAccountSnapshot account,
        IEnumerable<StrategyQuoteSnapshot>? quotes = null,
        IEnumerable<StrategyPositionSnapshot>? positions = null,
        IEnumerable<StrategyPendingOrderSnapshot>? pendingOrders = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        ArgumentNullException.ThrowIfNull(account);

        StrategyQuoteSnapshot[] quoteValues = SnapshotBounded(
            quotes,
            MaximumQuoteCount,
            nameof(quotes));
        StrategyPositionSnapshot[] positionValues = SnapshotBounded(
            positions,
            MaximumPositionCount,
            nameof(positions));
        StrategyPendingOrderSnapshot[] orderValues = SnapshotBounded(
            pendingOrders,
            MaximumPendingOrderCount,
            nameof(pendingOrders));
        if (quoteValues.Any(value => value is null)
            || positionValues.Any(value => value is null)
            || orderValues.Any(value => value is null))
        {
            throw new ArgumentException("Snapshot collections cannot contain null values.");
        }

        StrategyQuoteSnapshot[] orderedQuotes = quoteValues
            .OrderBy(value => value.Symbol, StringComparer.Ordinal)
            .ThenBy(value => value.Sequence)
            .ToArray();
        StrategyPositionSnapshot[] orderedPositions = positionValues
            .OrderBy(value => value.PositionId, StringComparer.Ordinal)
            .ToArray();
        StrategyPendingOrderSnapshot[] orderedOrders = orderValues
            .OrderBy(value => value.OrderId, StringComparer.Ordinal)
            .ToArray();

        return new StrategySnapshot(
            sequence,
            asOfUtc,
            deterministicNowUtc,
            account,
            Array.AsReadOnly(orderedQuotes),
            Array.AsReadOnly(orderedPositions),
            Array.AsReadOnly(orderedOrders));
    }

    private static T[] SnapshotBounded<T>(
        IEnumerable<T>? source,
        int maximumCount,
        string parameterName)
    {
        if (source is null)
        {
            return [];
        }

        if (source is IReadOnlyList<T> list)
        {
            int count = list.Count;
            if (count is < 0 || count > maximumCount)
            {
                throw new ArgumentException(
                    "A snapshot collection exceeds its supported element limit.",
                    parameterName);
            }

            var result = new T[count];
            for (int index = 0; index < count; index++)
            {
                result[index] = list[index];
            }

            return result;
        }

        var values = new List<T>(Math.Min(maximumCount, 256));
        using IEnumerator<T> enumerator = source.GetEnumerator();
        while (enumerator.MoveNext())
        {
            if (values.Count == maximumCount)
            {
                throw new ArgumentException(
                    "A snapshot collection exceeds its supported element limit.",
                    parameterName);
            }

            values.Add(enumerator.Current);
        }

        return values.ToArray();
    }
}

public sealed record StrategyState
{
    private StrategyState(long version, string payloadJson, string contentHash)
    {
        Version = version;
        PayloadJson = payloadJson;
        ContentHash = contentHash;
    }

    public long Version { get; }

    public string PayloadJson { get; }

    public string ContentHash { get; }

    public static StrategyState Empty { get; } = FromJson(0, "{}");

    public static StrategyState FromJson(long version, string json)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        JsonNode? node = JsonNode.Parse(json);
        if (node is null)
        {
            throw new ArgumentException("Strategy state must be a JSON value.", nameof(json));
        }

        if (ContainsNullCharacter(node))
        {
            throw new ArgumentException(
                "Strategy state cannot contain the Unicode null character.",
                nameof(json));
        }

        string normalized = CanonicalJson.Serialize(node);
        return new StrategyState(version, normalized, CanonicalJson.Sha256(node));
    }

    private static bool ContainsNullCharacter(JsonNode node) => node switch
    {
        JsonObject value => value.Any(property =>
            property.Key.Contains('\0', StringComparison.Ordinal)
            || property.Value is not null && ContainsNullCharacter(property.Value)),
        JsonArray value => value.Any(item =>
            item is not null && ContainsNullCharacter(item)),
        JsonValue value => value.TryGetValue(out string? text)
            && text is not null
            && text.Contains('\0', StringComparison.Ordinal),
        _ => false
    };
}
