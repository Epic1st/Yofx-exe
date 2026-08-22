using YO4X.BuildingBlocks;
using YO4X.Trading.Abstractions;
using YO4X.Trading.Application;

namespace YO4X.Trading.Application.Tests;

public sealed class BrokerCommandReconciliationValidatorTests
{
    [Fact]
    public void StaleSourceSequenceCannotProduceTerminalEvidence()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        BrokerCommandReconciliationClaim claim =
            BrokerCommandTestFixture.ReconciliationClaim(command);
        BrokerReconciliationSnapshot snapshot = PlaceAcknowledgedSnapshot(
            command,
            claim,
            command.Exposure.SourceSequence);

        ValidatedBrokerCommandReconciliation result = Validate(claim, snapshot);

        Assert.False(result.IsConclusive);
        Assert.Equal("broker_reconciliation_source_sequence_not_proven", result.ReasonCode);
        Assert.Null(result.SourceSequence);
        Assert.Null(result.OrderId);
        Assert.Null(result.DealId);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public void ForeignCommandResultCannotProduceTerminalEvidence()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        BrokerCommandReconciliationClaim claim =
            BrokerCommandTestFixture.ReconciliationClaim(command);
        BrokerReconciliationSnapshot snapshot = PlaceAcknowledgedSnapshot(
            command,
            claim,
            command.Exposure.SourceSequence + 1);
        snapshot = snapshot with
        {
            CommandResults =
            [
                snapshot.CommandResults[0] with
                {
                    CommandId = Guid.Parse("90000000-0000-0000-0000-000000000001")
                }
            ]
        };

        ValidatedBrokerCommandReconciliation result = Validate(claim, snapshot);

        Assert.False(result.IsConclusive);
        Assert.Equal("broker_reconciliation_command_result_invalid", result.ReasonCode);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void NonAtomicOrIncompleteCutCannotProduceTerminalEvidence(
        bool isAtomic,
        bool isComplete)
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        BrokerCommandReconciliationClaim claim =
            BrokerCommandTestFixture.ReconciliationClaim(command);
        BrokerReconciliationSnapshot snapshot = PlaceAcknowledgedSnapshot(
            command,
            claim,
            command.Exposure.SourceSequence + 1) with
        {
            IsAtomicCut = isAtomic,
            IsComplete = isComplete
        };

        ValidatedBrokerCommandReconciliation result = Validate(claim, snapshot);

        Assert.False(result.IsConclusive);
        Assert.Equal("broker_reconciliation_snapshot_shape_invalid", result.ReasonCode);
    }

    [Fact]
    public void DuplicateBrokerIdentifiersInvalidateSnapshot()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        BrokerCommandReconciliationClaim claim =
            BrokerCommandTestFixture.ReconciliationClaim(command);
        BrokerReconciliationSnapshot snapshot = PlaceAcknowledgedSnapshot(
            command,
            claim,
            command.Exposure.SourceSequence + 1);
        snapshot = snapshot with { Orders = [snapshot.Orders[0], snapshot.Orders[0]] };

        ValidatedBrokerCommandReconciliation result = Validate(claim, snapshot);

        Assert.False(result.IsConclusive);
        Assert.Equal("broker_reconciliation_snapshot_identity_invalid", result.ReasonCode);
    }

    [Fact]
    public void ExactProtectionPostStateCanProveAcknowledgement()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized(
            BrokerCommandAction.ModifyProtection);
        BrokerCommandReconciliationClaim claim =
            BrokerCommandTestFixture.ReconciliationClaim(command);
        DateTimeOffset at = claim.StartedAtUtc;
        var position = new BrokerPositionSnapshot(
            command.Command.TargetBrokerId!,
            command.Command.Symbol,
            command.Command.Side,
            command.Command.ExpectedTargetVolume!.Value,
            1.10m,
            command.Command.StopLoss,
            command.Command.TakeProfit,
            command.Command.OwnershipTag,
            at);
        var reported = new BrokerCommandReconciliation(
            command.Command.CommandId,
            BrokerReconciliationMatch.Acknowledged,
            "acknowledged",
            null,
            null,
            at);
        BrokerReconciliationSnapshot snapshot = Snapshot(
            command,
            claim,
            command.Exposure.SourceSequence + 1,
            [position],
            [],
            [],
            reported);

        ValidatedBrokerCommandReconciliation result = Validate(claim, snapshot);

        Assert.True(result.IsConclusive);
        Assert.Equal(BrokerReconciliationMatch.Acknowledged, result.Match);
        Assert.Equal(command.Command.TargetBrokerId, result.TargetBrokerId);
    }

    [Fact]
    public void CancellationRequiresExactHistoricalTargetOrder()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized(
            BrokerCommandAction.Cancel);
        BrokerCommandReconciliationClaim claim =
            BrokerCommandTestFixture.ReconciliationClaim(command);
        DateTimeOffset at = claim.StartedAtUtc;
        var cancelled = new BrokerOrderSnapshot(
            command.Command.TargetBrokerId!,
            command.Command.Symbol,
            command.Command.Side,
            command.Command.OrderType,
            command.Command.ExpectedTargetVolume!.Value,
            0,
            command.Command.RequestedPrice,
            command.Command.ExpectedTargetStopLoss,
            command.Command.ExpectedTargetTakeProfit,
            "cancelled",
            command.Command.OwnershipTag,
            at);
        var reported = new BrokerCommandReconciliation(
            command.Command.CommandId,
            BrokerReconciliationMatch.Cancelled,
            "cancelled",
            command.Command.TargetBrokerId,
            null,
            at);
        BrokerReconciliationSnapshot snapshot = Snapshot(
            command,
            claim,
            command.Exposure.SourceSequence + 1,
            [],
            [cancelled],
            [],
            reported);

        ValidatedBrokerCommandReconciliation result = Validate(claim, snapshot);

        Assert.True(result.IsConclusive);
        Assert.Equal(BrokerReconciliationMatch.Cancelled, result.Match);
        Assert.Equal(command.Command.TargetBrokerId, result.OrderId);
    }

    [Fact]
    public void CloseCannotBeInferredWithoutDealToPositionAndRequestCorrelation()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized(
            BrokerCommandAction.Close);
        BrokerCommandReconciliationClaim claim =
            BrokerCommandTestFixture.ReconciliationClaim(command);
        DateTimeOffset at = claim.StartedAtUtc;
        var deal = new BrokerDealSnapshot(
            "deal-1",
            "order-1",
            command.Command.Symbol,
            command.Command.Side,
            command.Command.Volume,
            1.10m,
            at);
        var reported = new BrokerCommandReconciliation(
            command.Command.CommandId,
            BrokerReconciliationMatch.Filled,
            "filled",
            deal.OrderId,
            deal.DealId,
            at);
        BrokerReconciliationSnapshot snapshot = Snapshot(
            command,
            claim,
            command.Exposure.SourceSequence + 1,
            [],
            [],
            [deal],
            reported);

        ValidatedBrokerCommandReconciliation result = Validate(claim, snapshot);

        Assert.False(result.IsConclusive);
        Assert.Equal("broker_reconciliation_close_correlation_not_proven", result.ReasonCode);
    }

    [Fact]
    public void FilledPlaceRequiresExactOrderAndLinkedDealVolume()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        BrokerCommandReconciliationClaim claim =
            BrokerCommandTestFixture.ReconciliationClaim(command);
        DateTimeOffset at = claim.StartedAtUtc;
        var order = new BrokerOrderSnapshot(
            "order-1",
            command.Command.Symbol,
            command.Command.Side,
            command.Command.OrderType,
            command.Command.Volume,
            0,
            command.Command.RequestedPrice,
            command.Command.StopLoss,
            command.Command.TakeProfit,
            "filled",
            command.Command.OwnershipTag,
            at);
        var deal = new BrokerDealSnapshot(
            "deal-1",
            order.OrderId,
            command.Command.Symbol,
            command.Command.Side,
            command.Command.Volume,
            1.10m,
            at);
        var reported = new BrokerCommandReconciliation(
            command.Command.CommandId,
            BrokerReconciliationMatch.Filled,
            "filled",
            order.OrderId,
            deal.DealId,
            at);
        BrokerReconciliationSnapshot snapshot = Snapshot(
            command,
            claim,
            command.Exposure.SourceSequence + 1,
            [],
            [order],
            [deal],
            reported);

        ValidatedBrokerCommandReconciliation result = Validate(claim, snapshot);

        Assert.True(result.IsConclusive);
        Assert.Equal(BrokerReconciliationMatch.Filled, result.Match);
    }

    [Fact]
    public void OverflowingDealAggregationCannotCrashOrProduceTerminalEvidence()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        BrokerCommandReconciliationClaim claim =
            BrokerCommandTestFixture.ReconciliationClaim(command);
        DateTimeOffset at = claim.StartedAtUtc;
        var order = new BrokerOrderSnapshot(
            "order-1",
            command.Command.Symbol,
            command.Command.Side,
            command.Command.OrderType,
            command.Command.Volume,
            0,
            command.Command.RequestedPrice,
            command.Command.StopLoss,
            command.Command.TakeProfit,
            "filled",
            command.Command.OwnershipTag,
            at);
        BrokerDealSnapshot Deal(string id) => new(
            id,
            order.OrderId,
            command.Command.Symbol,
            command.Command.Side,
            decimal.MaxValue,
            1.10m,
            at);
        BrokerDealSnapshot first = Deal("deal-1");
        var reported = new BrokerCommandReconciliation(
            command.Command.CommandId,
            BrokerReconciliationMatch.Filled,
            "filled",
            order.OrderId,
            first.DealId,
            at);
        BrokerReconciliationSnapshot snapshot = Snapshot(
            command,
            claim,
            command.Exposure.SourceSequence + 1,
            [],
            [order],
            [first, Deal("deal-2")],
            reported);

        ValidatedBrokerCommandReconciliation result = Validate(claim, snapshot);

        Assert.False(result.IsConclusive);
        Assert.Equal("broker_reconciliation_semantics_not_proven", result.ReasonCode);
        Assert.Null(result.Snapshot);
    }

    private static ValidatedBrokerCommandReconciliation Validate(
        BrokerCommandReconciliationClaim claim,
        BrokerReconciliationSnapshot snapshot)
    {
        var source = new BrokerCommandReconciliationValidator.BrokerReconciliationSourceDocument(
            snapshot.SourceSequence,
            snapshot.QueryWindowStartUtc,
            snapshot.QueryWindowEndUtc,
            snapshot);
        var observation = new BrokerCommandReconciliationObservation(
            snapshot.SourceSequence,
            CanonicalJson.Sha256(source),
            snapshot.QueryWindowStartUtc,
            snapshot.QueryWindowEndUtc,
            snapshot);
        return BrokerCommandReconciliationValidator.Validate(
            claim,
            observation,
            snapshot.CompletedAtUtc);
    }

    private static BrokerReconciliationSnapshot PlaceAcknowledgedSnapshot(
        AuthorizedBrokerCommand command,
        BrokerCommandReconciliationClaim claim,
        long sourceSequence)
    {
        DateTimeOffset at = claim.StartedAtUtc;
        var order = new BrokerOrderSnapshot(
            "order-1",
            command.Command.Symbol,
            command.Command.Side,
            command.Command.OrderType,
            command.Command.Volume,
            command.Command.Volume,
            command.Command.RequestedPrice,
            command.Command.StopLoss,
            command.Command.TakeProfit,
            "placed",
            command.Command.OwnershipTag,
            at);
        var reported = new BrokerCommandReconciliation(
            command.Command.CommandId,
            BrokerReconciliationMatch.Acknowledged,
            "acknowledged",
            order.OrderId,
            null,
            at);
        return Snapshot(command, claim, sourceSequence, [], [order], [], reported);
    }

    private static BrokerReconciliationSnapshot Snapshot(
        AuthorizedBrokerCommand command,
        BrokerCommandReconciliationClaim claim,
        long sourceSequence,
        IReadOnlyList<BrokerPositionSnapshot> positions,
        IReadOnlyList<BrokerOrderSnapshot> orders,
        IReadOnlyList<BrokerDealSnapshot> deals,
        BrokerCommandReconciliation reported)
    {
        DateTimeOffset at = claim.StartedAtUtc;
        var account = new BrokerAccountSnapshot(
            500,
            "***001",
            "Test Broker",
            "Demo",
            BrokerAccountMode.Hedging,
            BrokerEnvironment.Demo,
            BrokerTradingAccess.TradingAllowed,
            "USD",
            10_000m,
            10_000m,
            9_000m,
            at);
        return new BrokerReconciliationSnapshot(
            1,
            sourceSequence,
            command.Provenance.BrokerAccountId,
            command.Command.DeploymentId,
            command.Command.Generation,
            command.Provenance.GatewayArtifactId,
            command.Provenance.GatewayArtifactSha256,
            at,
            at,
            true,
            true,
            account,
            positions,
            orders,
            deals,
            [reported],
            at);
    }
}
