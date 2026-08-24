using YO4X.Tenancy;
using YO4X.Trading.Abstractions;
using YO4X.Trading.Application;

namespace YO4X.GatewayHost.Tests;

public sealed class BrokerCommandOneShotWorkerTests
{
    [Fact]
    public async Task DisabledWorkerNeverCallsExecutor()
    {
        BrokerCommandOneShotSettings settings = BrokerCommandOneShotSettings.Load(
            BrokerCommandOneShotCompositionTests.BuildConfiguration(
                new Dictionary<string, string?>()));
        var executor = new RecordingOneShotExecutor();
        var status = new GatewayHostRuntimeStatus(oneShotEnabled: false);
        var worker = new BrokerCommandOneShotWorker(settings, executor, status);

        await worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, executor.Calls);
        Assert.Equal("gateway_host_one_shot_disabled", status.Startup.Code);
    }

    [Fact]
    public async Task ExecutorUsesExactConfiguredReferenceAndStopsAfterProvenNoSend()
    {
        BrokerCommandOneShotSettings settings = EnabledSettings();
        var coordinator = new RecordingCoordinatorRunner
        {
            DispatchResult = new BrokerCommandDispatchResult(
                BrokerCommandDispatchOutcome.SubmissionRecorded,
                settings.CommandReference!.CommandId,
                false,
                GatewayCommandDisposition.SubmissionDisabled,
                "sensitive_internal_code",
                "submission_disabled")
        };
        var executor = new BrokerCommandOneShotExecutor(settings, coordinator);

        BrokerCommandOneShotOutcome outcome = await executor.ExecuteAsync(
            CancellationToken.None);

        Assert.Equal(BrokerCommandOneShotOutcome.NoSubmissionRecorded, outcome);
        Assert.Equal(1, coordinator.DispatchCalls);
        Assert.Equal(0, coordinator.ReconcileCalls);
        Assert.Same(settings.ExecutionContext, coordinator.Context);
        Assert.Same(settings.CommandReference, coordinator.Reference);
    }

    [Fact]
    public async Task ExecutorPerformsAtMostOneReconciliationWhenRequired()
    {
        BrokerCommandOneShotSettings settings = EnabledSettings();
        var coordinator = new RecordingCoordinatorRunner
        {
            DispatchResult = new BrokerCommandDispatchResult(
                BrokerCommandDispatchOutcome.ReconciliationRequired,
                settings.CommandReference!.CommandId,
                false,
                GatewayCommandDisposition.Unknown,
                "internal_dispatch_code",
                "unknown"),
            ReconciliationResult = new BrokerCommandReconciliationResult(
                BrokerCommandReconciliationOutcome.InconclusiveRetryable,
                settings.CommandReference.CommandId,
                true,
                BrokerReconciliationMatch.Inconclusive,
                "internal_reconciliation_code",
                "unknown")
        };
        var executor = new BrokerCommandOneShotExecutor(settings, coordinator);

        BrokerCommandOneShotOutcome outcome = await executor.ExecuteAsync(
            CancellationToken.None);

        Assert.Equal(BrokerCommandOneShotOutcome.ReconciliationPending, outcome);
        Assert.Equal(1, coordinator.DispatchCalls);
        Assert.Equal(1, coordinator.ReconcileCalls);
    }

    [Fact]
    public async Task ExecutorResumesDurableAcknowledgedCommandWithoutRedispatchingGateway()
    {
        BrokerCommandOneShotSettings settings = EnabledSettings();
        var coordinator = new RecordingCoordinatorRunner
        {
            // PostgreSQL returns no dispatch claim for an acknowledged command.
            // The application maps that fail-closed result to NoDispatchAuthority.
            DispatchResult = new BrokerCommandDispatchResult(
                BrokerCommandDispatchOutcome.NoDispatchAuthority,
                settings.CommandReference!.CommandId,
                false,
                null,
                "broker_command_not_dispatchable",
                null),
            ReconciliationResult = new BrokerCommandReconciliationResult(
                BrokerCommandReconciliationOutcome.Completed,
                settings.CommandReference.CommandId,
                true,
                BrokerReconciliationMatch.Acknowledged,
                "broker_reconciliation_acknowledged",
                "reconciled")
        };
        var executor = new BrokerCommandOneShotExecutor(settings, coordinator);

        BrokerCommandOneShotOutcome outcome = await executor.ExecuteAsync(
            CancellationToken.None);

        Assert.Equal(BrokerCommandOneShotOutcome.ReconciliationCompleted, outcome);
        Assert.Equal(1, coordinator.DispatchCalls);
        Assert.Equal(1, coordinator.ReconcileCalls);
        Assert.False(coordinator.DispatchResult.GatewayInvoked);
        Assert.Same(settings.ExecutionContext, coordinator.Context);
        Assert.Same(settings.CommandReference, coordinator.Reference);
    }

    [Theory]
    [InlineData(BrokerCommandReconciliationOutcome.InconclusiveRetryable)]
    [InlineData(BrokerCommandReconciliationOutcome.DurableRecoveryRequired)]
    public async Task RestartRecoveryAmbiguityRemainsPendingWithoutAnotherDispatch(
        BrokerCommandReconciliationOutcome reconciliationOutcome)
    {
        BrokerCommandOneShotSettings settings = EnabledSettings();
        var coordinator = new RecordingCoordinatorRunner
        {
            DispatchResult = new BrokerCommandDispatchResult(
                BrokerCommandDispatchOutcome.NoDispatchAuthority,
                settings.CommandReference!.CommandId,
                false,
                null,
                "broker_command_not_dispatchable",
                null),
            ReconciliationResult = new BrokerCommandReconciliationResult(
                reconciliationOutcome,
                settings.CommandReference.CommandId,
                false,
                BrokerReconciliationMatch.Inconclusive,
                "broker_reconciliation_pending",
                "unknown")
        };
        var executor = new BrokerCommandOneShotExecutor(settings, coordinator);

        BrokerCommandOneShotOutcome outcome = await executor.ExecuteAsync(
            CancellationToken.None);

        Assert.Equal(BrokerCommandOneShotOutcome.ReconciliationPending, outcome);
        Assert.Equal(1, coordinator.DispatchCalls);
        Assert.Equal(1, coordinator.ReconcileCalls);
    }

    [Fact]
    public async Task NonReconcilableNoAuthorityResultFailsClosed()
    {
        BrokerCommandOneShotSettings settings = EnabledSettings();
        var coordinator = new RecordingCoordinatorRunner
        {
            DispatchResult = new BrokerCommandDispatchResult(
                BrokerCommandDispatchOutcome.NoDispatchAuthority,
                settings.CommandReference!.CommandId,
                false,
                null,
                "broker_command_not_dispatchable",
                null),
            ReconciliationResult = new BrokerCommandReconciliationResult(
                BrokerCommandReconciliationOutcome.NotEligible,
                settings.CommandReference.CommandId,
                false,
                null,
                "broker_command_not_reconcilable",
                null)
        };
        var recoveryWaiter = new RecordingClaimRecoveryWaiter(canContinue: false);
        var executor = new BrokerCommandOneShotExecutor(
            settings,
            coordinator,
            recoveryWaiter);

        BrokerCommandOneShotOutcome outcome = await executor.ExecuteAsync(
            CancellationToken.None);

        Assert.Equal(BrokerCommandOneShotOutcome.Failed, outcome);
        Assert.Equal(1, coordinator.DispatchCalls);
        Assert.Equal(1, coordinator.ReconcileCalls);
        Assert.Equal(1, recoveryWaiter.Calls);
    }

    [Fact]
    public async Task ActiveDispatchClaimWaitsForExpiryThenRecoversWithoutGatewayRedispatch()
    {
        BrokerCommandOneShotSettings settings = EnabledSettings();
        var coordinator = new RecordingCoordinatorRunner();
        coordinator.DispatchResults.Enqueue(new BrokerCommandDispatchResult(
            BrokerCommandDispatchOutcome.NoDispatchAuthority,
            settings.CommandReference!.CommandId,
            false,
            null,
            "broker_command_not_dispatchable",
            null));
        coordinator.DispatchResults.Enqueue(new BrokerCommandDispatchResult(
            BrokerCommandDispatchOutcome.ReconciliationRequired,
            settings.CommandReference.CommandId,
            false,
            null,
            "broker_command_expired_lifecycle_recovered",
            "unknown"));
        coordinator.ReconciliationResults.Enqueue(new BrokerCommandReconciliationResult(
            BrokerCommandReconciliationOutcome.NotEligible,
            settings.CommandReference.CommandId,
            false,
            null,
            "broker_command_not_reconcilable",
            null));
        coordinator.ReconciliationResults.Enqueue(new BrokerCommandReconciliationResult(
            BrokerCommandReconciliationOutcome.Completed,
            settings.CommandReference.CommandId,
            true,
            BrokerReconciliationMatch.NotSent,
            "broker_reconciliation_not_sent",
            "reconciled"));
        var recoveryWaiter = new RecordingClaimRecoveryWaiter(canContinue: true);
        var executor = new BrokerCommandOneShotExecutor(
            settings,
            coordinator,
            recoveryWaiter);

        BrokerCommandOneShotOutcome outcome = await executor.ExecuteAsync(
            CancellationToken.None);

        Assert.Equal(BrokerCommandOneShotOutcome.ReconciliationCompleted, outcome);
        Assert.Equal(2, coordinator.DispatchCalls);
        Assert.Equal(2, coordinator.ReconcileCalls);
        Assert.Equal(1, recoveryWaiter.Calls);
        Assert.All(coordinator.SeenDispatchResults, result =>
            Assert.False(result.GatewayInvoked));
    }

    [Fact]
    public async Task InconsistentNoAuthorityResultFailsWithoutReconciliation()
    {
        BrokerCommandOneShotSettings settings = EnabledSettings();
        var coordinator = new RecordingCoordinatorRunner
        {
            DispatchResult = new BrokerCommandDispatchResult(
                BrokerCommandDispatchOutcome.NoDispatchAuthority,
                settings.CommandReference!.CommandId,
                true,
                GatewayCommandDisposition.Unknown,
                "inconsistent_result",
                null)
        };
        var executor = new BrokerCommandOneShotExecutor(settings, coordinator);

        BrokerCommandOneShotOutcome outcome = await executor.ExecuteAsync(
            CancellationToken.None);

        Assert.Equal(BrokerCommandOneShotOutcome.Failed, outcome);
        Assert.Equal(1, coordinator.DispatchCalls);
        Assert.Equal(0, coordinator.ReconcileCalls);
    }

    [Fact]
    public async Task WorkerFailureHealthNeverContainsExceptionOrCommandMaterial()
    {
        BrokerCommandOneShotSettings settings = EnabledSettings();
        var executor = new RecordingOneShotExecutor
        {
            Exception = new InvalidOperationException(
                $"secret {settings.CommandReference!.AuthorizationSha256}")
        };
        var status = new GatewayHostRuntimeStatus(oneShotEnabled: true);
        var worker = new BrokerCommandOneShotWorker(settings, executor, status);

        await worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, executor.Calls);
        Assert.Equal("failed", status.Startup.Status);
        Assert.Equal("gateway_host_one_shot_failed", status.Startup.Code);
        Assert.DoesNotContain(
            settings.CommandReference.AuthorizationSha256,
            status.Startup.ToString(),
            StringComparison.Ordinal);
        Assert.Equal("gateway_host_proof_only_not_mutation_ready", status.Ready.Code);
    }

    private static BrokerCommandOneShotSettings EnabledSettings() =>
        BrokerCommandOneShotSettings.Load(
            BrokerCommandOneShotCompositionTests.BuildConfiguration(
                BrokerCommandOneShotCompositionTests.ValidValues()));

    private sealed class RecordingOneShotExecutor : IBrokerCommandOneShotExecutor
    {
        public Exception? Exception { get; init; }

        public int Calls { get; private set; }

        public Task<BrokerCommandOneShotOutcome> ExecuteAsync(
            CancellationToken cancellationToken)
        {
            Calls++;
            return Exception is null
                ? Task.FromResult(BrokerCommandOneShotOutcome.NoSubmissionRecorded)
                : Task.FromException<BrokerCommandOneShotOutcome>(Exception);
        }
    }

    private sealed class RecordingCoordinatorRunner : IBrokerCommandCoordinatorRunner
    {
        public BrokerCommandDispatchResult DispatchResult { get; init; } = null!;

        public BrokerCommandReconciliationResult ReconciliationResult { get; init; } = null!;

        public Queue<BrokerCommandDispatchResult> DispatchResults { get; } = new();

        public Queue<BrokerCommandReconciliationResult> ReconciliationResults { get; } = new();

        public List<BrokerCommandDispatchResult> SeenDispatchResults { get; } = [];

        public int DispatchCalls { get; private set; }

        public int ReconcileCalls { get; private set; }

        public TenantExecutionContext? Context { get; private set; }

        public BrokerCommandReference? Reference { get; private set; }

        public Task<BrokerCommandDispatchResult> DispatchAsync(
            TenantExecutionContext context,
            BrokerCommandReference reference,
            CancellationToken cancellationToken)
        {
            DispatchCalls++;
            Context = context;
            Reference = reference;
            BrokerCommandDispatchResult result = DispatchResults.Count == 0
                ? DispatchResult
                : DispatchResults.Dequeue();
            SeenDispatchResults.Add(result);
            return Task.FromResult(result);
        }

        public Task<BrokerCommandReconciliationResult> ReconcileAsync(
            TenantExecutionContext context,
            BrokerCommandReference reference,
            CancellationToken cancellationToken)
        {
            ReconcileCalls++;
            Context = context;
            Reference = reference;
            return Task.FromResult(ReconciliationResults.Count == 0
                ? ReconciliationResult
                : ReconciliationResults.Dequeue());
        }
    }

    private sealed class RecordingClaimRecoveryWaiter(bool canContinue)
        : IBrokerCommandClaimRecoveryWaiter
    {
        public int Calls { get; private set; }

        public Task<bool> WaitAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(canContinue);
        }
    }
}
