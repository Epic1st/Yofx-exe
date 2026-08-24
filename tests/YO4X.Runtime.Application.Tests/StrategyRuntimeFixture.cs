using YO4X.Runtime.Application;
using YO4X.Runtime.Contracts;
using YO4X.Strategy.Abstractions;
using YO4X.Tenancy;

namespace YO4X.Runtime.Application.Tests;

internal static class StrategyRuntimeFixture
{
    public static readonly DateTimeOffset Now =
        new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    public static readonly Guid TenantId =
        Guid.Parse("81000000-0000-0000-0000-000000000001");

    public static readonly Guid DeploymentId =
        Guid.Parse("81000000-0000-0000-0000-000000000002");

    public static readonly Guid WorkerId =
        Guid.Parse("81000000-0000-0000-0000-000000000003");

    public static TenantExecutionContext Context() => new(
        TenantId,
        Guid.Parse("81000000-0000-0000-0000-000000000004"),
        Guid.Parse("81000000-0000-0000-0000-000000000005"));

    public static RuntimeEnvelope<StrategyEvent> Envelope() => new(
        RuntimeContractVersions.EnvelopeV1,
        DeploymentId,
        WorkerId,
        7,
        11,
        Guid.Parse("81000000-0000-0000-0000-000000000006"),
        Now,
        Now,
        new NewTickEvent(Now, "EURUSD", 1.10m, 1.11m, 42));

    public static StrategySnapshot Snapshot() => StrategySnapshot.Create(
        21,
        Now,
        Now,
        new StrategyAccountSnapshot(9, 10_000m, 10_050m, 9_000m, "USD"),
        [new StrategyQuoteSnapshot(42, "EURUSD", 1.10m, 1.11m, Now)]);

    public static StrategyEventInputEvidence Input() =>
        StrategyEventInputEvidence.Create(Envelope(), Snapshot());

    public static ClaimedStrategyEvent Claim(
        StrategyEventReference reference,
        Guid claimToken,
        StrategyState? state = null)
    {
        StrategyEventInputEvidence input = Input();
        Assert.Equal(reference, input.Reference);
        StrategyState priorState = state ?? StrategyState.Empty;
        return new ClaimedStrategyEvent(
            reference,
            claimToken,
            Now,
            Now.AddSeconds(5),
            input.Envelope,
            input.Snapshot,
            priorState,
            input.EventJson,
            input.SnapshotJson,
            priorState.PayloadJson,
            priorState.ContentHash,
            false);
    }

    public static StrategyResult ValidResult(params RequestedAction[] actions) => new(
        StrategyState.FromJson(1, "{\"counter\":1}"),
        actions.Length == 0 ? [Place()] : actions);

    public static PlaceOrderAction Place(
        Guid? actionId = null,
        string idempotencyKey = "entry-1") => new(
        actionId ?? Guid.Parse("81000000-0000-0000-0000-000000000010"),
        idempotencyKey,
        "EURUSD",
        "fixture_entry",
        42,
        RequestedExposureHint.Increase,
        RequestedOrderSide.Buy,
        RequestedOrderType.Market,
        0.01m,
        null,
        1.08m,
        1.14m,
        10);

    public static ClosePositionAction Close() => new(
        Guid.Parse("81000000-0000-0000-0000-000000000011"),
        "close-1",
        "EURUSD",
        "fixture_exit",
        42,
        "position-1",
        0.01m);

    public static StrategyEventProcessingOptions Options(
        TimeSpan? maximumWallTime = null) => new()
    {
        ResultBounds = StrategyResultBounds.Create(
            4096,
            8,
            8192,
            maximumWallTime ?? TimeSpan.FromSeconds(1)),
        CommitAcknowledgementRecoveryAttempts = 1
    };
}

internal sealed class RecordingStrategyHost : IStrategyHostClient
{
    public Func<StrategyHostEvaluationRequest, CancellationToken, Task<StrategyResult?>> Handler
        { get; init; } = (_, _) => Task.FromResult<StrategyResult?>(
            StrategyRuntimeFixture.ValidResult());

    public int Calls { get; private set; }

    public List<StrategyHostEvaluationRequest> Requests { get; } = [];

    public Task<StrategyResult?> EvaluateAsync(
        StrategyHostEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        Calls++;
        Requests.Add(request);
        return Handler(request, cancellationToken);
    }
}

internal sealed class RecordingStrategyStore : IStrategyEventTransactionStore
{
    public Exception? ClaimException { get; init; }

    public Func<StrategyEventReference, Guid, StrategyEventClaimResult>? ClaimHandler { get; set; }

    public Func<StrategyEventCommitRequest, int, StrategyEventCommitReceipt>? CommitHandler
        { get; set; }

    public int ClaimCalls { get; private set; }

    public int CommitCalls { get; private set; }

    public List<StrategyEventCommitRequest> CommitRequests { get; } = [];

    public StrategyEventCommitReceipt? DurableReceipt { get; set; }

    public Task<StrategyEventClaimResult> ClaimAsync(
        TenantExecutionContext context,
        StrategyEventReference reference,
        Guid claimToken,
        CancellationToken cancellationToken)
    {
        ClaimCalls++;
        if (ClaimException is not null)
        {
            throw ClaimException;
        }

        return Task.FromResult(
            ClaimHandler?.Invoke(reference, claimToken)
            ?? StrategyEventClaimResult.Claimed(
                StrategyRuntimeFixture.Claim(reference, claimToken)));
    }

    public Task<StrategyEventCommitReceipt> CommitAsync(
        TenantExecutionContext context,
        StrategyEventCommitRequest request,
        CancellationToken cancellationToken)
    {
        CommitCalls++;
        CommitRequests.Add(request);
        StrategyEventCommitReceipt receipt = CommitHandler?.Invoke(request, CommitCalls)
            ?? new StrategyEventCommitReceipt(request.Evidence, StrategyRuntimeFixture.Now, false);
        DurableReceipt = receipt;
        return Task.FromResult(receipt);
    }
}

internal sealed class SequenceStrategyIdentifiers : IStrategyRuntimeIdentifierSource
{
    private long next = 100;

    public Guid NewId()
    {
        string suffix = Interlocked.Increment(ref next).ToString("D12", null);
        return Guid.Parse($"82000000-0000-0000-0000-{suffix}");
    }
}

internal sealed class FixedRuntimeTimeProvider(DateTimeOffset value) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => value;

    public override long GetTimestamp() => 0;
}
