using System.Collections;
using YO4X.BuildingBlocks;
using YO4X.Trading.Abstractions;
using YO4X.Trading.Application;

namespace YO4X.Trading.Application.Tests;

public sealed class BrokerCommandReconciliationValidatorTests
{
    [Fact]
    public void SnapshotNormalizationRejectsOversizedCustomListBeforeIndexOrEnumeration()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        BrokerCommandReconciliationClaim claim =
            BrokerCommandTestFixture.ReconciliationClaim(command);
        BrokerReconciliationSnapshot baseline = PlaceAcknowledgedSnapshot(
            command,
            claim,
            command.Exposure.SourceSequence + 1);
        var positions = new HostileReadOnlyList<BrokerPositionSnapshot>(10_001, null, true);
        BrokerReconciliationSnapshot snapshot = baseline with { Positions = positions };

        Assert.Throws<ArgumentException>(() =>
            BrokerCommandLifecycleEvidence.NormalizeSnapshot(snapshot));
        Assert.Equal(0, positions.IndexerCalls);
        Assert.Equal(0, positions.EnumeratorCalls);
    }

    [Fact]
    public void SnapshotNormalizationCopiesByBoundedIndexWithoutUsingHostileEnumerator()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        BrokerCommandReconciliationClaim claim =
            BrokerCommandTestFixture.ReconciliationClaim(command);
        BrokerReconciliationSnapshot baseline = PlaceAcknowledgedSnapshot(
            command,
            claim,
            command.Exposure.SourceSequence + 1);
        var orders = new HostileReadOnlyList<BrokerOrderSnapshot>(
            1,
            baseline.Orders[0],
            throwOnIndex: false);
        BrokerReconciliationSnapshot snapshot = baseline with { Orders = orders };

        BrokerReconciliationSnapshot normalized =
            BrokerCommandLifecycleEvidence.NormalizeSnapshot(snapshot);

        Assert.Single(normalized.Orders);
        Assert.True(orders.IndexerCalls >= 2);
        Assert.Equal(0, orders.EnumeratorCalls);
    }

    [Fact]
    public void ValidatorRejectsOversizedGatewayTextBeforeCanonicalHashing()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        BrokerCommandReconciliationClaim claim =
            BrokerCommandTestFixture.ReconciliationClaim(command);
        BrokerReconciliationSnapshot baseline = PlaceAcknowledgedSnapshot(
            command,
            claim,
            command.Exposure.SourceSequence + 1);
        BrokerReconciliationSnapshot snapshot = baseline with
        {
            Account = baseline.Account with { BrokerCompany = new string('x', 201) }
        };
        var observation = new BrokerCommandReconciliationObservation(
            snapshot.SourceSequence,
            new string('a', 64),
            snapshot.QueryWindowStartUtc,
            snapshot.QueryWindowEndUtc,
            snapshot);

        ValidatedBrokerCommandReconciliation result =
            BrokerCommandReconciliationValidator.Validate(
                claim,
                observation,
                snapshot.CompletedAtUtc);

        Assert.Equal("broker_reconciliation_snapshot_shape_invalid", result.ReasonCode);
        Assert.Null(result.Snapshot);
    }

    [Theory]
    [InlineData("not-a-digest")]
    [InlineData("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    public void ValidatorReplacesUnvalidatedObservationDigestWithCanonicalFailureEvidence(
        string unvalidatedDigest)
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        BrokerCommandReconciliationClaim claim =
            BrokerCommandTestFixture.ReconciliationClaim(command);
        var observation = new BrokerCommandReconciliationObservation(
            null,
            unvalidatedDigest,
            claim.QueryWindowStartUtc,
            claim.StartedAtUtc,
            null);

        ValidatedBrokerCommandReconciliation result =
            BrokerCommandReconciliationValidator.Validate(
                claim,
                observation,
                claim.StartedAtUtc);
        BrokerCommandCanonicalEvidence durable =
            BrokerCommandLifecycleEvidence.Reconciliation(result);

        Assert.Equal(BrokerReconciliationMatch.Inconclusive, result.Match);
        Assert.Equal(
            "broker_reconciliation_gateway_observation_unavailable",
            result.ReasonCode);
        Assert.Matches("^[0-9a-f]{64}$", result.SourceEvidenceSha256);
        Assert.NotEqual(observation.SourceEvidenceSha256, result.SourceEvidenceSha256);
        Assert.NotEmpty(durable.CanonicalJson);
        Assert.Matches("^[0-9a-f]{64}$", durable.Sha256);
    }

    [Fact]
    public void ValidatorReplacesExactButMismatchedSourceDigestWithFailureEvidence()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        BrokerCommandReconciliationClaim claim =
            BrokerCommandTestFixture.ReconciliationClaim(command);
        BrokerReconciliationSnapshot snapshot = PlaceAcknowledgedSnapshot(
            command,
            claim,
            command.Exposure.SourceSequence + 1);
        string mismatchedDigest = new('b', 64);
        var observation = new BrokerCommandReconciliationObservation(
            snapshot.SourceSequence,
            mismatchedDigest,
            snapshot.QueryWindowStartUtc,
            snapshot.QueryWindowEndUtc,
            snapshot);

        ValidatedBrokerCommandReconciliation result =
            BrokerCommandReconciliationValidator.Validate(
                claim,
                observation,
                snapshot.CompletedAtUtc);

        Assert.Equal("broker_reconciliation_source_digest_invalid", result.ReasonCode);
        Assert.Matches("^[0-9a-f]{64}$", result.SourceEvidenceSha256);
        Assert.NotEqual(mismatchedDigest, result.SourceEvidenceSha256);
        _ = BrokerCommandLifecycleEvidence.Reconciliation(result);
    }

    [Theory]
    [InlineData("broker\0name")]
    [InlineData("broker\u200Ename")]
    public void SnapshotNormalizationRejectsNonPersistableGatewayText(string brokerCompany)
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        BrokerCommandReconciliationClaim claim =
            BrokerCommandTestFixture.ReconciliationClaim(command);
        BrokerReconciliationSnapshot baseline = PlaceAcknowledgedSnapshot(
            command,
            claim,
            command.Exposure.SourceSequence + 1);
        BrokerReconciliationSnapshot snapshot = baseline with
        {
            Account = baseline.Account with { BrokerCompany = brokerCompany }
        };

        Assert.Throws<ArgumentException>(() =>
            BrokerCommandLifecycleEvidence.NormalizeSnapshot(snapshot));
    }

    [Fact]
    public void SnapshotTextBoundsCountUnicodeScalarsLikePostgres()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        BrokerCommandReconciliationClaim claim =
            BrokerCommandTestFixture.ReconciliationClaim(command);
        BrokerReconciliationSnapshot baseline = PlaceAcknowledgedSnapshot(
            command,
            claim,
            command.Exposure.SourceSequence + 1);
        string twoHundredScalars = string.Concat(Enumerable.Repeat("😀", 200));
        BrokerReconciliationSnapshot accepted = baseline with
        {
            Account = baseline.Account with { BrokerCompany = twoHundredScalars }
        };
        BrokerReconciliationSnapshot rejected = baseline with
        {
            Account = baseline.Account with { BrokerCompany = twoHundredScalars + "😀" }
        };

        Assert.Equal(
            twoHundredScalars,
            BrokerCommandLifecycleEvidence.NormalizeSnapshot(accepted).Account.BrokerCompany);
        Assert.Throws<ArgumentException>(() =>
            BrokerCommandLifecycleEvidence.NormalizeSnapshot(rejected));
    }

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
    public void ExactProtectionPostStateRemainsInconclusiveWithoutRequestCorrelation()
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

        Assert.False(result.IsConclusive);
        Assert.Equal(
            "broker_reconciliation_protection_correlation_not_proven",
            result.ReasonCode);
        Assert.Null(result.SourceSequence);
        Assert.Null(result.Snapshot);
        Assert.Equal(command.Command.TargetBrokerId, result.TargetBrokerId);
    }

    [Theory]
    [InlineData("unknown", false)]
    [InlineData("accepted", true)]
    public void SameShapePlaceWithoutPersistedExactOrderIdRemainsInconclusive(
        string sendDisposition,
        bool hasRequestId)
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        BrokerCommandReconciliationClaim claim =
            BrokerCommandTestFixture.ReconciliationClaim(command) with
            {
                SendDisposition = sendDisposition,
                BrokerRequestId = hasRequestId ? "request-1" : null,
                BrokerOrderId = null,
                BrokerDealId = null
            };
        BrokerReconciliationSnapshot snapshot = PlaceAcknowledgedSnapshot(
            command,
            claim,
            command.Exposure.SourceSequence + 1);

        ValidatedBrokerCommandReconciliation result = Validate(claim, snapshot);

        Assert.False(result.IsConclusive);
        Assert.Equal(
            "broker_reconciliation_place_order_correlation_not_proven",
            result.ReasonCode);
        Assert.Null(result.SourceSequence);
        Assert.Null(result.Snapshot);
        Assert.Null(result.OrderId);
    }

    [Fact]
    public void ExactCancellationPostStateRemainsInconclusiveWithoutRequestCorrelation()
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

        Assert.False(result.IsConclusive);
        Assert.Equal(
            "broker_reconciliation_cancel_correlation_not_proven",
            result.ReasonCode);
        Assert.Null(result.SourceSequence);
        Assert.Null(result.Snapshot);
        Assert.Null(result.OrderId);
        Assert.Equal(command.Command.TargetBrokerId, result.TargetBrokerId);
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
    public void ExactFilledPlaceRemainsInconclusiveWithoutAuthenticatedBrokerObservation()
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

        Assert.False(result.IsConclusive);
        Assert.Equal(BrokerReconciliationMatch.Inconclusive, result.Match);
        Assert.Equal(
            "broker_reconciliation_terminal_authority_unavailable",
            result.ReasonCode);
        Assert.Null(result.SourceSequence);
        Assert.Null(result.OrderId);
        Assert.Null(result.DealId);
        Assert.Null(result.Snapshot);
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

    private sealed class HostileReadOnlyList<T>(
        int count,
        T? item,
        bool throwOnIndex) : IReadOnlyList<T>
    {
        public int Count => count;

        public int IndexerCalls { get; private set; }

        public int EnumeratorCalls { get; private set; }

        public T this[int index]
        {
            get
            {
                IndexerCalls++;
                if (throwOnIndex)
                {
                    throw new InvalidOperationException("The hostile indexer must not be called.");
                }

                Assert.InRange(index, 0, count - 1);
                return item!;
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            EnumeratorCalls++;
            throw new InvalidOperationException("The hostile enumerator must not be called.");
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
