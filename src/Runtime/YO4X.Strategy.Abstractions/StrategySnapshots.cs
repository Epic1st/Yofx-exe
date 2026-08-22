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

        StrategyQuoteSnapshot[] orderedQuotes = (quotes ?? [])
            .OrderBy(value => value.Symbol, StringComparer.Ordinal)
            .ThenBy(value => value.Sequence)
            .ToArray();
        StrategyPositionSnapshot[] orderedPositions = (positions ?? [])
            .OrderBy(value => value.PositionId, StringComparer.Ordinal)
            .ToArray();
        StrategyPendingOrderSnapshot[] orderedOrders = (pendingOrders ?? [])
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

        string normalized = CanonicalJson.Serialize(node);
        return new StrategyState(version, normalized, CanonicalJson.Sha256(node));
    }
}
