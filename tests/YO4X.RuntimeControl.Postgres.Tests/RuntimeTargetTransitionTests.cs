using System.Text.Json;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.RuntimeControl.Postgres;

namespace YO4X.RuntimeControl.Postgres.Tests;

public sealed class RuntimeTargetTransitionTests
{
    [Theory]
    [InlineData("dispatched", "delivered")]
    [InlineData("delivered", "acknowledged")]
    [InlineData("acknowledged", "applied")]
    public void AcceptsOnlyTheOrderedDeliveryPath(string currentState, string nextState)
    {
        RuntimeTargetTransition result = RuntimeTargetTransition.Create(
            currentState,
            Input(nextState),
            "target_delivery",
            Timestamp);

        Assert.Equal(nextState, result.State);
    }

    [Theory]
    [InlineData("pending_dispatch", "delivered")]
    [InlineData("dispatched", "acknowledged")]
    [InlineData("delivered", "applied")]
    [InlineData("reconciled", "failed")]
    public void RejectsSkippedOrTerminalDeliveryTransitions(string currentState, string nextState)
    {
        ResourceConflictException exception = Assert.Throws<ResourceConflictException>(() =>
            RuntimeTargetTransition.Create(
                currentState,
                Input(nextState, errorCode: "TARGET_FAILED"),
                "target_delivery",
                Timestamp));

        Assert.Equal("COMMAND_TARGET_TRANSITION_INVALID", exception.Code);
    }

    [Fact]
    public void ReconciledRequiresObservedAndBrokerEvidence()
    {
        ResourceConflictException exception = Assert.Throws<ResourceConflictException>(() =>
            RuntimeTargetTransition.Create(
                "reconciling",
                Input("reconciled", observedResult: "flat"),
                "target_reconciliation",
                Timestamp));

        Assert.Equal("COMMAND_TARGET_TRANSITION_INVALID", exception.Code);
    }

    [Fact]
    public void ReconciledCapturesBothEvidenceBindings()
    {
        RuntimeTargetTransition result = RuntimeTargetTransition.Create(
            "reconciling",
            Input("reconciled", observedResult: " flat ", brokerEvidence: " deal-history:42 "),
            "target_reconciliation",
            Timestamp);

        Assert.Equal("flat", result.ObservedResult);
        Assert.Equal("deal-history:42", result.BrokerEvidenceReference);
        Assert.Equal(Timestamp, result.ReconciledAt);
    }

    private static readonly DateTimeOffset Timestamp = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    private static TargetDeliveryInput Input(
        string state,
        string? errorCode = null,
        string? observedResult = null,
        string? brokerEvidence = null) => new(
        1,
        Guid.Parse("10000000-0000-0000-0000-000000000001"),
        1,
        1,
        Timestamp,
        state,
        errorCode,
        observedResult,
        brokerEvidence,
        JsonDocument.Parse("{}").RootElement.Clone());
}
