using YO4X.BuildingBlocks;
using YO4X.Trading.Abstractions;

namespace YO4X.Trading.Application;

public static class BrokerCommandReconciliationValidator
{
    private const int MaximumPositions = 10_000;
    private const int MaximumOrders = 10_000;
    private const int MaximumDeals = 50_000;
    private const int MaximumCommandResults = 1;

    public static ValidatedBrokerCommandReconciliation Validate(
        BrokerCommandReconciliationClaim claim,
        BrokerCommandReconciliationObservation observation,
        DateTimeOffset receivedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(claim.Command);
        ArgumentNullException.ThrowIfNull(observation);
        AuthorizedBrokerCommand capability = claim.Command;
        NormalizedBrokerCommand command = capability.Command;

        string? globalFailure = ValidateGlobal(
            claim,
            observation,
            receivedAtUtc,
            out BrokerCommandReconciliation? reported);
        if (globalFailure is not null)
        {
            return Inconclusive(claim, observation, receivedAtUtc, globalFailure);
        }

        BrokerReconciliationMatch derived = command.Action switch
        {
            BrokerCommandAction.Place => DerivePlace(command, observation.Snapshot!, reported!),
            // The current snapshots do not bind non-Place post-state to the
            // exact broker request strongly enough for a terminal result.
            BrokerCommandAction.ModifyProtection or BrokerCommandAction.Cancel
                or BrokerCommandAction.Close => BrokerReconciliationMatch.Inconclusive,
            _ => BrokerReconciliationMatch.Inconclusive
        };

        if (derived == BrokerReconciliationMatch.Inconclusive)
        {
            return Inconclusive(
                claim,
                observation,
                receivedAtUtc,
                command.Action switch
                {
                    BrokerCommandAction.ModifyProtection =>
                        "broker_reconciliation_protection_correlation_not_proven",
                    BrokerCommandAction.Cancel =>
                        "broker_reconciliation_cancel_correlation_not_proven",
                    BrokerCommandAction.Close =>
                        "broker_reconciliation_close_correlation_not_proven",
                    _ => "broker_reconciliation_semantics_not_proven"
                });
        }

        if (reported!.Match != derived)
        {
            return Inconclusive(
                claim,
                observation,
                receivedAtUtc,
                "broker_reconciliation_reported_result_conflicts_with_snapshot");
        }

        return Create(
            claim,
            observation,
            derived,
            "broker_reconciliation_snapshot_proven",
            reported.OrderId,
            reported.DealId,
            observation.WindowEndUtc);
    }

    private static string? ValidateGlobal(
        BrokerCommandReconciliationClaim claim,
        BrokerCommandReconciliationObservation observation,
        DateTimeOffset receivedAtUtc,
        out BrokerCommandReconciliation? reported)
    {
        reported = null;
        AuthorizedBrokerCommand capability = claim.Command;
        if (receivedAtUtc.Offset != TimeSpan.Zero
            || claim.StartedAtUtc.Offset != TimeSpan.Zero
            || claim.ClaimExpiresAtUtc.Offset != TimeSpan.Zero
            || claim.QueryWindowStartUtc.Offset != TimeSpan.Zero
            || claim.MustBeginByUtc.Offset != TimeSpan.Zero
            || claim.MustCompleteByUtc.Offset != TimeSpan.Zero
            || claim.AuthorityNowUtc.Offset != TimeSpan.Zero
            || observation.WindowStartUtc.Offset != TimeSpan.Zero
            || observation.WindowEndUtc.Offset != TimeSpan.Zero
            || observation.WindowStartUtc != claim.QueryWindowStartUtc
            || observation.WindowEndUtc < observation.WindowStartUtc
            || claim.QueryWindowStartUtc > claim.StartedAtUtc
            || claim.StartedAtUtc > observation.WindowEndUtc
            || observation.WindowEndUtc > receivedAtUtc
            || receivedAtUtc < claim.StartedAtUtc
            || receivedAtUtc < claim.AuthorityNowUtc
            || receivedAtUtc > claim.ClaimExpiresAtUtc
            || receivedAtUtc > claim.MustCompleteByUtc
            || claim.ClaimToken == Guid.Empty
            || claim.Attempt <= 0
            || claim.CommandVersion <= 0
            || claim.SendDisposition is not ("accepted" or "unknown")
            || !BrokerCommandReference.DigestEquals(
                claim.ScopeSha256,
                capability.Reconciliation.ScopeSha256)
            || claim.MustBeginByUtc != capability.Reconciliation.MustBeginByUtc
            || claim.MustCompleteByUtc != capability.Reconciliation.MustCompleteByUtc)
        {
            return "broker_reconciliation_window_or_scope_invalid";
        }

        if (observation.Snapshot is null)
        {
            return "broker_reconciliation_gateway_observation_unavailable";
        }

        BrokerReconciliationSnapshot snapshot = observation.Snapshot;
        if (observation.SourceSequence is null
            || observation.SourceSequence <= capability.Exposure.SourceSequence)
        {
            return "broker_reconciliation_source_sequence_not_proven";
        }

        var sourceDocument = new BrokerReconciliationSourceDocument(
            observation.SourceSequence.Value,
            observation.WindowStartUtc,
            observation.WindowEndUtc,
            snapshot);
        if (!BrokerCommandReference.DigestEquals(
                CanonicalJson.Sha256(sourceDocument),
                observation.SourceEvidenceSha256))
        {
            return "broker_reconciliation_source_digest_invalid";
        }

        if (snapshot.Account is null
            || snapshot.ContractVersion != 1
            || snapshot.SourceSequence != observation.SourceSequence
            || snapshot.BrokerAccountId != capability.Provenance.BrokerAccountId
            || snapshot.DeploymentId != capability.Command.DeploymentId
            || snapshot.Generation != capability.Command.Generation
            || snapshot.GatewayArtifactId != capability.Provenance.GatewayArtifactId
            || !BrokerCommandReference.DigestEquals(
                snapshot.GatewayArtifactSha256,
                capability.Provenance.GatewayArtifactSha256)
            || snapshot.QueryWindowStartUtc != observation.WindowStartUtc
            || snapshot.QueryWindowEndUtc != observation.WindowEndUtc
            || !snapshot.IsAtomicCut
            || !snapshot.IsComplete
            || snapshot.Positions is null
            || snapshot.Orders is null
            || snapshot.Deals is null
            || snapshot.CommandResults is null
            || snapshot.Positions.Count > MaximumPositions
            || snapshot.Orders.Count > MaximumOrders
            || snapshot.Deals.Count > MaximumDeals
            || snapshot.CommandResults.Count > MaximumCommandResults
            || snapshot.Positions.Any(item => item is null)
            || snapshot.Orders.Any(item => item is null)
            || snapshot.Deals.Any(item => item is null)
            || snapshot.CommandResults.Any(item => item is null)
            || snapshot.CompletedAtUtc.Offset != TimeSpan.Zero
            || snapshot.CompletedAtUtc != observation.WindowEndUtc
            || snapshot.Account.ObservedAtUtc.Offset != TimeSpan.Zero
            || !WithinWindow(snapshot.Account.ObservedAtUtc, claim.StartedAtUtc, snapshot.CompletedAtUtc)
            || snapshot.Positions.Any(item => item.ObservedAtUtc.Offset != TimeSpan.Zero
                || !WithinWindow(item.ObservedAtUtc, claim.StartedAtUtc, snapshot.CompletedAtUtc))
            || snapshot.Orders.Any(item => item.ObservedAtUtc.Offset != TimeSpan.Zero
                || !WithinWindow(item.ObservedAtUtc, claim.StartedAtUtc, snapshot.CompletedAtUtc))
            || snapshot.Deals.Any(item => item.BrokerTimestampUtc.Offset != TimeSpan.Zero
                || !WithinWindow(
                    item.BrokerTimestampUtc,
                    claim.QueryWindowStartUtc,
                    snapshot.CompletedAtUtc)))
        {
            return "broker_reconciliation_snapshot_shape_invalid";
        }

        if (HasDuplicate(snapshot.Positions.Select(item => item.PositionId))
            || HasDuplicate(snapshot.Orders.Select(item => item.OrderId))
            || HasDuplicate(snapshot.Deals.Select(item => item.DealId))
            || snapshot.Positions.Any(item => !ValidBrokerId(item.PositionId)
                || item.Volume <= 0)
            || snapshot.Orders.Any(item => !ValidBrokerId(item.OrderId)
                || item.RequestedVolume <= 0
                || item.RemainingVolume < 0
                || item.RemainingVolume > item.RequestedVolume)
            || snapshot.Deals.Any(item => !ValidBrokerId(item.DealId)
                || !ValidBrokerId(item.OrderId)
                || item.Volume <= 0
                || item.Price <= 0))
        {
            return "broker_reconciliation_snapshot_identity_invalid";
        }

        if (snapshot.CommandResults.Count != 1)
        {
            return "broker_reconciliation_command_result_ambiguous";
        }

        reported = snapshot.CommandResults[0];
        if (reported.CommandId != capability.Command.CommandId
            || reported.ReconciledAtUtc.Offset != TimeSpan.Zero
            || reported.ReconciledAtUtc != snapshot.CompletedAtUtc
            || !ValidReason(reported.ReasonCode)
            || !ValidOptionalBrokerId(reported.OrderId)
            || !ValidOptionalBrokerId(reported.DealId))
        {
            reported = null;
            return "broker_reconciliation_command_result_invalid";
        }

        if ((claim.BrokerOrderId is not null && claim.BrokerOrderId != reported.OrderId)
            || (claim.BrokerDealId is not null && claim.BrokerDealId != reported.DealId))
        {
            reported = null;
            return "broker_reconciliation_submission_receipt_conflict";
        }

        if (reported.Match == BrokerReconciliationMatch.Inconclusive)
        {
            return "broker_reconciliation_gateway_reported_inconclusive";
        }

        if (reported.Match is BrokerReconciliationMatch.Rejected
            or BrokerReconciliationMatch.NotSent)
        {
            return "broker_reconciliation_negative_receipt_not_proven";
        }

        return null;
    }

    private static BrokerReconciliationMatch DerivePlace(
        NormalizedBrokerCommand command,
        BrokerReconciliationSnapshot snapshot,
        BrokerCommandReconciliation reported)
    {
        if (!ValidBrokerId(reported.OrderId))
        {
            return BrokerReconciliationMatch.Inconclusive;
        }

        BrokerOrderSnapshot? order = snapshot.Orders.SingleOrDefault(
            item => item.OrderId == reported.OrderId);
        if (order is null || !MatchesPlacedOrder(command, order))
        {
            return BrokerReconciliationMatch.Inconclusive;
        }

        List<BrokerDealSnapshot> deals = snapshot.Deals
            .Where(item => item.OrderId == order.OrderId)
            .ToList();
        if (reported.DealId is not null
            && deals.Count(item => item.DealId == reported.DealId) != 1)
        {
            return BrokerReconciliationMatch.Inconclusive;
        }

        if (deals.Any(item => item.Symbol != command.Symbol || item.Side != command.Side))
        {
            return BrokerReconciliationMatch.Inconclusive;
        }

        decimal filled;
        try
        {
            filled = deals.Sum(item => item.Volume);
        }
        catch (OverflowException)
        {
            return BrokerReconciliationMatch.Inconclusive;
        }
        if (filled == 0
            && reported.DealId is null
            && IsStatus(order.Status, "pending", "placed", "accepted"))
        {
            return BrokerReconciliationMatch.Acknowledged;
        }

        if (filled > 0
            && filled < command.Volume
            && order.RemainingVolume == command.Volume - filled
            && reported.DealId is not null
            && IsStatus(order.Status, "partially_filled"))
        {
            return BrokerReconciliationMatch.PartiallyFilled;
        }

        if (filled == command.Volume
            && order.RemainingVolume == 0
            && reported.DealId is not null
            && IsStatus(order.Status, "filled"))
        {
            return BrokerReconciliationMatch.Filled;
        }

        return BrokerReconciliationMatch.Inconclusive;
    }

    private static bool MatchesPlacedOrder(
        NormalizedBrokerCommand command,
        BrokerOrderSnapshot order) =>
        order.Symbol == command.Symbol
        && order.Side == command.Side
        && order.OrderType == command.OrderType
        && order.RequestedVolume == command.Volume
        && order.RequestedPrice == command.RequestedPrice
        && order.StopLoss == command.StopLoss
        && order.TakeProfit == command.TakeProfit
        && order.OwnershipTag == command.OwnershipTag;

    private static ValidatedBrokerCommandReconciliation Inconclusive(
        BrokerCommandReconciliationClaim claim,
        BrokerCommandReconciliationObservation observation,
        DateTimeOffset receivedAtUtc,
        string reasonCode)
    {
        AuthorizedBrokerCommand capability = claim.Command;
        return new ValidatedBrokerCommandReconciliation(
            capability.Command.CommandId,
            capability.AuthorizationSha256,
            capability.Reconciliation.ScopeSha256,
            capability.Provenance.BrokerAccountId,
            capability.Command.DeploymentId,
            capability.Command.Generation,
            capability.Command.TargetKind,
            capability.Command.TargetBrokerId,
            capability.Command.OwnershipTag,
            null,
            observation.WindowStartUtc,
            observation.WindowEndUtc,
            BrokerReconciliationMatch.Inconclusive,
            reasonCode,
            observation.SourceEvidenceSha256,
            null,
            null,
            receivedAtUtc,
            null);
    }

    private static ValidatedBrokerCommandReconciliation Create(
        BrokerCommandReconciliationClaim claim,
        BrokerCommandReconciliationObservation observation,
        BrokerReconciliationMatch match,
        string reasonCode,
        string? orderId,
        string? dealId,
        DateTimeOffset observedAtUtc)
    {
        AuthorizedBrokerCommand capability = claim.Command;
        return new ValidatedBrokerCommandReconciliation(
            capability.Command.CommandId,
            capability.AuthorizationSha256,
            capability.Reconciliation.ScopeSha256,
            capability.Provenance.BrokerAccountId,
            capability.Command.DeploymentId,
            capability.Command.Generation,
            capability.Command.TargetKind,
            capability.Command.TargetBrokerId,
            capability.Command.OwnershipTag,
            observation.SourceSequence,
            observation.WindowStartUtc,
            observation.WindowEndUtc,
            match,
            reasonCode,
            observation.SourceEvidenceSha256,
            orderId,
            dealId,
            observedAtUtc,
            observation.Snapshot);
    }

    private static bool HasDuplicate(IEnumerable<string> ids)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in ids)
        {
            if (!seen.Add(id))
            {
                return true;
            }
        }

        return false;
    }

    private static bool WithinWindow(
        DateTimeOffset value,
        DateTimeOffset start,
        DateTimeOffset end) => value >= start && value <= end;

    private static bool IsStatus(string value, params string[] allowed) =>
        value is { Length: >= 1 and <= 100 }
        && value == value.Trim()
        && allowed.Contains(value, StringComparer.Ordinal);

    private static bool ValidBrokerId(string? value) =>
        value is { Length: >= 1 and <= 200 } && value == value.Trim();

    private static bool ValidOptionalBrokerId(string? value) =>
        value is null || ValidBrokerId(value);

    private static bool ValidReason(string? value) =>
        value is { Length: >= 1 and <= 200 } && value == value.Trim();

    public sealed record BrokerReconciliationSourceDocument(
        long SourceSequence,
        DateTimeOffset WindowStartUtc,
        DateTimeOffset WindowEndUtc,
        BrokerReconciliationSnapshot Snapshot);
}
