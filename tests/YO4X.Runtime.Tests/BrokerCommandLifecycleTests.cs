using YO4X.BuildingBlocks;
using YO4X.Trading.Abstractions;

namespace YO4X.Runtime.Tests;

public sealed class BrokerCommandLifecycleTests
{
    private static readonly Guid CommandId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void UnknownCommandCannotBeDispatchedAgain()
    {
        BrokerCommandLifecycle command = BrokerCommandLifecycle.CreateReady(CommandId, Now);
        command.BeginSend(Now.AddSeconds(1));
        command.RecordGatewayResult(
            new GatewaySendResult(
                GatewayCommandDisposition.Unknown,
                "gateway_ack_lost",
                null,
                null,
                null,
                Now.AddSeconds(2)));

        Assert.Equal(BrokerCommandState.Unknown, command.State);
        Assert.False(command.CanDispatch);
        Assert.True(command.RequiresReconciliation);
        Assert.Throws<DomainException>(() => command.BeginSend(Now.AddSeconds(3)));
    }

    [Fact]
    public void InconclusiveReconciliationReturnsToUnknownWithoutRetryPath()
    {
        BrokerCommandLifecycle command = UnknownCommand();
        command.BeginReconciliation(Now.AddSeconds(3));
        command.CompleteReconciliation(
            new BrokerCommandReconciliation(
                CommandId,
                BrokerReconciliationMatch.Inconclusive,
                "broker_history_not_fresh",
                null,
                null,
                Now.AddSeconds(4)));

        Assert.Equal(BrokerCommandState.Unknown, command.State);
        Assert.False(command.CanDispatch);
        Assert.Equal(1, command.DispatchAttemptCount);
    }

    [Fact]
    public void AuthoritativeReconciliationRecordsResolvedOutcome()
    {
        BrokerCommandLifecycle command = UnknownCommand();
        command.BeginReconciliation(Now.AddSeconds(3));
        command.CompleteReconciliation(
            new BrokerCommandReconciliation(
                CommandId,
                BrokerReconciliationMatch.Filled,
                "broker_deal_matched",
                "order-1",
                "deal-1",
                Now.AddSeconds(4)));

        Assert.Equal(BrokerCommandState.Reconciled, command.State);
        Assert.Equal(BrokerReconciliationMatch.Filled, command.ReconciledOutcome);
        Assert.True(command.IsTerminal);
    }

    [Fact]
    public void RestartDuringSendMovesCommandToUnknown()
    {
        BrokerCommandLifecycle command = BrokerCommandLifecycle.CreateReady(CommandId, Now);
        command.BeginSend(Now.AddSeconds(1));

        command.RecoverAfterRestart(Now.AddSeconds(2));

        Assert.Equal(BrokerCommandState.Unknown, command.State);
        Assert.True(command.RequiresReconciliation);
    }

    [Fact]
    public void SubmissionDisabledIsATerminalRejection()
    {
        BrokerCommandLifecycle command = BrokerCommandLifecycle.CreateReady(CommandId, Now);
        command.BeginSend(Now.AddSeconds(1));
        command.RecordGatewayResult(
            new GatewaySendResult(
                GatewayCommandDisposition.SubmissionDisabled,
                "u0_no_order",
                null,
                null,
                null,
                Now.AddSeconds(2)));

        Assert.Equal(BrokerCommandState.Rejected, command.State);
        Assert.True(command.IsTerminal);
        Assert.False(command.RequiresReconciliation);
    }

    [Fact]
    public void AcknowledgedCommandCanProgressThroughPartialFillToFill()
    {
        BrokerCommandLifecycle command = BrokerCommandLifecycle.CreateReady(CommandId, Now);
        command.BeginSend(Now.AddSeconds(1));
        command.RecordGatewayResult(
            new GatewaySendResult(
                GatewayCommandDisposition.Accepted,
                "broker_acknowledged",
                "request-1",
                "order-1",
                null,
                Now.AddSeconds(2)));

        command.RecordPartialFill(Now.AddSeconds(3));
        command.RecordFilled(Now.AddSeconds(4));

        Assert.Equal(BrokerCommandState.Filled, command.State);
        Assert.True(command.IsTerminal);
        Assert.Equal(1, command.DispatchAttemptCount);
    }

    private static BrokerCommandLifecycle UnknownCommand()
    {
        BrokerCommandLifecycle command = BrokerCommandLifecycle.CreateReady(CommandId, Now);
        command.BeginSend(Now.AddSeconds(1));
        command.MarkUnknownAfterInterruptedSend(Now.AddSeconds(2));
        return command;
    }
}
