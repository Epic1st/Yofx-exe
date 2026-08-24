using System.Globalization;
using YO4X.BuildingBlocks;
using YO4X.Trading.Abstractions;

namespace YO4X.Trading.Application;

/// <summary>
/// Owns the exact canonical evidence persisted for broker-command lifecycle
/// transitions. Infrastructure must persist <see cref="BrokerCommandCanonicalEvidence.CanonicalJson"/>
/// verbatim and must not independently normalize or reshape it.
/// </summary>
public static class BrokerCommandLifecycleEvidence
{
    internal const int MaximumPositions = 10_000;
    internal const int MaximumOrders = 10_000;
    internal const int MaximumDeals = 50_000;
    internal const int MaximumCommandResults = 1;
    private const long MaximumEstimatedSnapshotBytes = 16L * 1024 * 1024;

    public static DateTimeOffset NormalizeUtcTimestamp(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            return value;
        }

        long ticks = value.Ticks
            - (value.Ticks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    public static GatewaySendResult NormalizeSubmission(GatewaySendResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result with
        {
            ObservedAtUtc = NormalizeUtcTimestamp(result.ObservedAtUtc)
        };
    }

    public static BrokerReconciliationSnapshot NormalizeSnapshot(
        BrokerReconciliationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!IsBoundedSnapshot(snapshot))
        {
            throw new ArgumentException(
                "The reconciliation snapshot exceeds its trusted boundary.",
                nameof(snapshot));
        }

        BrokerReconciliationSnapshot normalized = snapshot with
        {
            QueryWindowStartUtc = NormalizeUtcTimestamp(snapshot.QueryWindowStartUtc),
            QueryWindowEndUtc = NormalizeUtcTimestamp(snapshot.QueryWindowEndUtc),
            CompletedAtUtc = NormalizeUtcTimestamp(snapshot.CompletedAtUtc),
            Account = snapshot.Account is null
                ? null!
                : snapshot.Account with
                {
                    ObservedAtUtc = NormalizeUtcTimestamp(snapshot.Account.ObservedAtUtc)
                },
            Positions = snapshot.Positions is null
                ? null!
                : SnapshotList(
                    snapshot.Positions,
                    MaximumPositions,
                    item => item is null
                        ? null!
                        : item with
                        {
                            ObservedAtUtc = NormalizeUtcTimestamp(item.ObservedAtUtc)
                        }),
            Orders = snapshot.Orders is null
                ? null!
                : SnapshotList(
                    snapshot.Orders,
                    MaximumOrders,
                    item => item is null
                        ? null!
                        : item with
                        {
                            ObservedAtUtc = NormalizeUtcTimestamp(item.ObservedAtUtc)
                        }),
            Deals = snapshot.Deals is null
                ? null!
                : SnapshotList(
                    snapshot.Deals,
                    MaximumDeals,
                    item => item is null
                        ? null!
                        : item with
                        {
                            BrokerTimestampUtc = NormalizeUtcTimestamp(item.BrokerTimestampUtc)
                        }),
            CommandResults = snapshot.CommandResults is null
                ? null!
                : SnapshotList(
                    snapshot.CommandResults,
                    MaximumCommandResults,
                    item => item is null
                        ? null!
                        : item with
                        {
                            ReconciledAtUtc = NormalizeUtcTimestamp(item.ReconciledAtUtc)
                        })
        };

        if (!IsBoundedSnapshot(normalized))
        {
            throw new ArgumentException(
                "The reconciliation snapshot changed while it was copied.",
                nameof(snapshot));
        }

        return normalized;
    }

    internal static bool IsBoundedSnapshot(BrokerReconciliationSnapshot snapshot)
    {
        try
        {
            if (snapshot.Account is null
                || !ExactDigest(snapshot.GatewayArtifactSha256)
                || !IsCanonicalBoundedText(snapshot.Account.MaskedLogin, 200)
                || !IsCanonicalBoundedText(snapshot.Account.BrokerCompany, 200)
                || !IsCanonicalBoundedText(snapshot.Account.ServerName, 200)
                || !IsCanonicalBoundedText(snapshot.Account.Currency, 16)
                || !Enum.IsDefined(snapshot.Account.AccountMode)
                || !Enum.IsDefined(snapshot.Account.Environment)
                || !Enum.IsDefined(snapshot.Account.TradingAccess))
            {
                return false;
            }

            var budget = new SnapshotSizeBudget(MaximumEstimatedSnapshotBytes);
            if (!budget.Take(
                    2048,
                    snapshot.GatewayArtifactSha256,
                    snapshot.Account.MaskedLogin,
                    snapshot.Account.BrokerCompany,
                    snapshot.Account.ServerName,
                    snapshot.Account.Currency)
                || !CheckList(
                    snapshot.Positions,
                    MaximumPositions,
                    512,
                    static item => item is not null
                        && IsCanonicalBoundedText(item.PositionId, 200)
                        && IsCanonicalBoundedText(item.Symbol, 100)
                        && IsCanonicalBoundedText(item.OwnershipTag, 200)
                        && Enum.IsDefined(item.Side),
                    static item => [item.PositionId, item.Symbol, item.OwnershipTag],
                    budget)
                || !CheckList(
                    snapshot.Orders,
                    MaximumOrders,
                    640,
                    static item => item is not null
                        && IsCanonicalBoundedText(item.OrderId, 200)
                        && IsCanonicalBoundedText(item.Symbol, 100)
                        && IsCanonicalBoundedText(item.Status, 100)
                        && IsCanonicalBoundedText(item.OwnershipTag, 200)
                        && Enum.IsDefined(item.Side)
                        && Enum.IsDefined(item.OrderType),
                    static item =>
                        [item.OrderId, item.Symbol, item.Status, item.OwnershipTag],
                    budget)
                || !CheckList(
                    snapshot.Deals,
                    MaximumDeals,
                    384,
                    static item => item is not null
                        && IsCanonicalBoundedText(item.DealId, 200)
                        && IsCanonicalBoundedText(item.OrderId, 200)
                        && IsCanonicalBoundedText(item.Symbol, 100)
                        && Enum.IsDefined(item.Side),
                    static item => [item.DealId, item.OrderId, item.Symbol],
                    budget)
                || !CheckList(
                    snapshot.CommandResults,
                    MaximumCommandResults,
                    384,
                    static item => item is not null
                        && IsCanonicalBoundedText(item.ReasonCode, 200)
                        && OptionalBoundedText(item.OrderId, 200)
                        && OptionalBoundedText(item.DealId, 200)
                        && Enum.IsDefined(item.Match),
                    static item => [item.ReasonCode, item.OrderId, item.DealId],
                    budget))
            {
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            return false;
        }
    }

    private static T[] SnapshotList<T>(
        IReadOnlyList<T> source,
        int maximumCount,
        Func<T, T> normalize)
    {
        int count = source.Count;
        if (count is < 0 || count > maximumCount)
        {
            throw new ArgumentException(
                "The reconciliation snapshot collection exceeds its element limit.",
                nameof(source));
        }

        var result = new T[count];
        for (int index = 0; index < count; index++)
        {
            result[index] = normalize(source[index]);
        }

        return result;
    }

    private static bool CheckList<T>(
        IReadOnlyList<T>? source,
        int maximumCount,
        int fixedBytesPerItem,
        Func<T, bool> isValid,
        Func<T, string?[]> text,
        SnapshotSizeBudget budget)
    {
        if (source is null)
        {
            return false;
        }

        int count = source.Count;
        if (count is < 0 || count > maximumCount)
        {
            return false;
        }

        for (int index = 0; index < count; index++)
        {
            T item = source[index];
            if (!isValid(item) || !budget.Take(fixedBytesPerItem, text(item)))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsCanonicalBoundedText(string? value, int maximumCharacters)
    {
        if (value is not { Length: >= 1 }
            || maximumCharacters <= 0
            || char.IsWhiteSpace(value[0])
            || char.IsWhiteSpace(value[^1]))
        {
            return false;
        }

        int characterCount = 0;
        for (int index = 0; index < value.Length;)
        {
            char current = value[index];
            int scalarWidth;
            if (char.IsHighSurrogate(current))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                {
                    return false;
                }

                scalarWidth = 2;
            }
            else if (char.IsLowSurrogate(current))
            {
                return false;
            }
            else
            {
                scalarWidth = 1;
            }

            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(value, index);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format)
            {
                return false;
            }

            characterCount++;
            if (characterCount > maximumCharacters)
            {
                return false;
            }

            index += scalarWidth;
        }

        return true;
    }

    internal static bool IsCanonicalCode(string? value) =>
        value is { Length: >= 1 and <= 200 }
        && value.All(static character => char.IsAsciiLetterOrDigit(character)
            || character is '_' or '-' or '.' or ':');

    private static bool OptionalBoundedText(string? value, int maximumLength) =>
        value is null || IsCanonicalBoundedText(value, maximumLength);

    internal static bool ExactDigest(string? value) =>
        value is { Length: 64 }
        && value.All(static character => character is >= '0' and <= '9'
            or >= 'a' and <= 'f');

    private sealed class SnapshotSizeBudget(long maximumBytes)
    {
        private long consumedBytes;

        public bool Take(long fixedBytes, params string?[] values)
        {
            long requested = fixedBytes;
            foreach (string? value in values)
            {
                if (value is not null)
                {
                    requested = checked(requested + (value.Length * 6L));
                }
            }

            if (requested < 0 || consumedBytes > maximumBytes - requested)
            {
                return false;
            }

            consumedBytes += requested;
            return true;
        }
    }

    public static BrokerCommandCanonicalEvidence Submission(GatewaySendResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        RequireCanonicalTimestamp(result.ObservedAtUtc, nameof(result));
        RequireCanonicalSubmission(result);
        var document = new SubmissionDocument(
            ToStorage(result.Disposition),
            result.Code,
            result.BrokerRequestId,
            result.OrderId,
            result.DealId,
            result.ObservedAtUtc,
            result.PreInvocationNotSentProven);
        return Create(document);
    }

    private static void RequireCanonicalSubmission(GatewaySendResult result)
    {
        bool hasBrokerIdentifier = result.BrokerRequestId is not null
            || result.OrderId is not null
            || result.DealId is not null;
        bool dispositionSemanticsAreValid = result.Disposition switch
        {
            GatewayCommandDisposition.Accepted =>
                !result.PreInvocationNotSentProven && hasBrokerIdentifier,
            GatewayCommandDisposition.Unknown => !result.PreInvocationNotSentProven,
            GatewayCommandDisposition.SubmissionDisabled =>
                result.PreInvocationNotSentProven && !hasBrokerIdentifier,
            _ => false
        };

        if (!dispositionSemanticsAreValid
            || !IsCanonicalCode(result.Code)
            || !OptionalBoundedText(result.BrokerRequestId, 200)
            || !OptionalBoundedText(result.OrderId, 200)
            || !OptionalBoundedText(result.DealId, 200))
        {
            throw new ArgumentException(
                "The broker submission is not valid canonical durable evidence.",
                nameof(result));
        }
    }

    public static BrokerCommandCanonicalEvidence Reconciliation(
        ValidatedBrokerCommandReconciliation evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        RequireCanonicalTimestamp(evidence.WindowStartUtc, nameof(evidence));
        RequireCanonicalTimestamp(evidence.WindowEndUtc, nameof(evidence));
        RequireCanonicalTimestamp(evidence.ObservedAtUtc, nameof(evidence));
        if (evidence.CommandId == Guid.Empty
            || evidence.BrokerAccountId == Guid.Empty
            || evidence.DeploymentId == Guid.Empty
            || evidence.Generation <= 0
            || !ExactDigest(evidence.AuthorizationSha256)
            || !ExactDigest(evidence.ScopeSha256)
            || !ExactDigest(evidence.SourceEvidenceSha256)
            || (evidence.TargetKind is not null && !Enum.IsDefined(evidence.TargetKind.Value))
            || !OptionalBoundedText(evidence.TargetBrokerId, 200)
            || !IsCanonicalBoundedText(evidence.OwnershipTag, 200)
            || !IsCanonicalBoundedText(evidence.ReasonCode, 200)
            || evidence.WindowStartUtc > evidence.WindowEndUtc
            || evidence.ObservedAtUtc != evidence.WindowEndUtc
            || evidence.Match != BrokerReconciliationMatch.Inconclusive
            || evidence.SourceSequence is not null
            || evidence.OrderId is not null
            || evidence.DealId is not null
            || evidence.Snapshot is not null)
        {
            throw new ArgumentException(
                "The reconciliation is not valid canonical durable evidence.",
                nameof(evidence));
        }

        if (evidence.Snapshot is not null)
        {
            RequireCanonicalSnapshot(evidence.Snapshot, nameof(evidence));
        }

        var document = new ReconciliationDocument(
            evidence.CommandId,
            evidence.AuthorizationSha256,
            evidence.ScopeSha256,
            evidence.BrokerAccountId,
            evidence.DeploymentId,
            evidence.Generation,
            evidence.TargetKind,
            evidence.TargetBrokerId,
            evidence.OwnershipTag,
            evidence.SourceSequence,
            evidence.WindowStartUtc,
            evidence.WindowEndUtc,
            ToStorage(evidence.Match),
            evidence.ReasonCode,
            evidence.SourceEvidenceSha256,
            evidence.OrderId,
            evidence.DealId,
            evidence.ObservedAtUtc,
            evidence.Snapshot);
        return Create(document);
    }

    private static BrokerCommandCanonicalEvidence Create<T>(T document)
    {
        string json = CanonicalJson.Serialize(document);
        return new BrokerCommandCanonicalEvidence(json, CanonicalJson.Sha256(document));
    }

    private static void RequireCanonicalSnapshot(
        BrokerReconciliationSnapshot snapshot,
        string parameterName)
    {
        if (snapshot.Account is null
            || snapshot.Positions is null
            || snapshot.Orders is null
            || snapshot.Deals is null
            || snapshot.CommandResults is null)
        {
            throw new ArgumentException(
                "Reconciliation snapshot collections and account are required.",
                parameterName);
        }

        RequireCanonicalTimestamp(snapshot.QueryWindowStartUtc, parameterName);
        RequireCanonicalTimestamp(snapshot.QueryWindowEndUtc, parameterName);
        RequireCanonicalTimestamp(snapshot.CompletedAtUtc, parameterName);
        RequireCanonicalTimestamp(snapshot.Account.ObservedAtUtc, parameterName);
        foreach (BrokerPositionSnapshot position in snapshot.Positions)
        {
            ArgumentNullException.ThrowIfNull(position, parameterName);
            RequireCanonicalTimestamp(position.ObservedAtUtc, parameterName);
        }

        foreach (BrokerOrderSnapshot order in snapshot.Orders)
        {
            ArgumentNullException.ThrowIfNull(order, parameterName);
            RequireCanonicalTimestamp(order.ObservedAtUtc, parameterName);
        }

        foreach (BrokerDealSnapshot deal in snapshot.Deals)
        {
            ArgumentNullException.ThrowIfNull(deal, parameterName);
            RequireCanonicalTimestamp(deal.BrokerTimestampUtc, parameterName);
        }

        foreach (BrokerCommandReconciliation result in snapshot.CommandResults)
        {
            ArgumentNullException.ThrowIfNull(result, parameterName);
            RequireCanonicalTimestamp(result.ReconciledAtUtc, parameterName);
        }
    }

    private static void RequireCanonicalTimestamp(
        DateTimeOffset value,
        string parameterName)
    {
        if (value.Offset != TimeSpan.Zero
            || value.Ticks % TimeSpan.TicksPerMicrosecond != 0)
        {
            throw new ArgumentException(
                "A UTC timestamp at whole-microsecond precision is required.",
                parameterName);
        }
    }

    private static string ToStorage(GatewayCommandDisposition disposition) => disposition switch
    {
        GatewayCommandDisposition.Accepted => "accepted",
        GatewayCommandDisposition.Rejected => "rejected",
        GatewayCommandDisposition.Unknown => "unknown",
        GatewayCommandDisposition.SubmissionDisabled => "submission_disabled",
        _ => throw new ArgumentOutOfRangeException(nameof(disposition))
    };

    private static string ToStorage(BrokerReconciliationMatch match) => match switch
    {
        BrokerReconciliationMatch.Inconclusive => "inconclusive",
        BrokerReconciliationMatch.Acknowledged => "acknowledged",
        BrokerReconciliationMatch.PartiallyFilled => "partially_filled",
        BrokerReconciliationMatch.Filled => "filled",
        BrokerReconciliationMatch.Cancelled => "cancelled",
        BrokerReconciliationMatch.Rejected => "rejected",
        BrokerReconciliationMatch.NotSent => "not_sent",
        _ => throw new ArgumentOutOfRangeException(nameof(match))
    };

    private sealed record SubmissionDocument(
        string Disposition,
        string Code,
        string? BrokerRequestId,
        string? OrderId,
        string? DealId,
        DateTimeOffset ObservedAtUtc,
        bool PreInvocationNotSentProven);

    private sealed record ReconciliationDocument(
        Guid CommandId,
        string AuthorizationSha256,
        string ScopeSha256,
        Guid BrokerAccountId,
        Guid DeploymentId,
        long Generation,
        BrokerCommandTargetKind? TargetKind,
        string? TargetBrokerId,
        string OwnershipTag,
        long? SourceSequence,
        DateTimeOffset WindowStartUtc,
        DateTimeOffset WindowEndUtc,
        string Match,
        string ReasonCode,
        string SourceEvidenceSha256,
        string? OrderId,
        string? DealId,
        DateTimeOffset ObservedAtUtc,
        BrokerReconciliationSnapshot? Snapshot);
}

public sealed class BrokerCommandCanonicalEvidence
{
    internal BrokerCommandCanonicalEvidence(string canonicalJson, string sha256)
    {
        CanonicalJson = canonicalJson;
        Sha256 = sha256;
    }

    public string CanonicalJson { get; }

    public string Sha256 { get; }
}
