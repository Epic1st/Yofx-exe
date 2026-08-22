using YO4X.Tenancy;
using YO4X.Trading.Abstractions;
using YO4X.Trading.Application;
using YO4X.Trading.Mt5;

namespace YO4X.Trading.Application.Tests;

public sealed class BrokerCommandCoordinatorTests
{
    [Theory]
    [InlineData(GatewayCommandDisposition.Accepted)]
    [InlineData(GatewayCommandDisposition.Unknown)]
    public async Task AcceptedAndUnknownAreRecordedAndRequireReconciliation(
        GatewayCommandDisposition disposition)
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        var store = new RecordingStore(BrokerCommandTestFixture.DispatchClaim(command));
        var gateway = new RecordingGateway
        {
            SendResult = new GatewaySendResult(
                disposition,
                "gateway_result",
                "request-1",
                "order-1",
                null,
                BrokerCommandTestFixture.Now)
        };
        BrokerCommandCoordinator coordinator = Create(store, gateway);

        BrokerCommandDispatchResult result = await coordinator.DispatchAsync(
            BrokerCommandTestFixture.Context(command),
            BrokerCommandTestFixture.Reference(command));

        Assert.Equal(BrokerCommandDispatchOutcome.ReconciliationRequired, result.Outcome);
        Assert.Equal(1, gateway.SendCalls);
        Assert.Equal(1, store.RecordSubmissionCalls);
        Assert.Equal(disposition, store.Submission?.Disposition);
        Assert.False(store.SubmissionTokenWasCancelled);
    }

    [Fact]
    public async Task RejectedSubmissionIsDurablyTerminalWithoutReconciliationFlag()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        var store = new RecordingStore(BrokerCommandTestFixture.DispatchClaim(command));
        var gateway = new RecordingGateway
        {
            SendResult = new GatewaySendResult(
                GatewayCommandDisposition.Rejected,
                "broker_rejected",
                "request-1",
                null,
                null,
                BrokerCommandTestFixture.Now)
        };

        BrokerCommandDispatchResult result = await Create(store, gateway).DispatchAsync(
            BrokerCommandTestFixture.Context(command),
            BrokerCommandTestFixture.Reference(command));

        Assert.Equal(BrokerCommandDispatchOutcome.SubmissionRecorded, result.Outcome);
        Assert.False(result.RequiresReconciliation);
        Assert.Equal(1, gateway.SendCalls);
    }

    [Fact]
    public async Task ProofOnlyGatewayPersistsSubmissionDisabledWithoutVendorExecution()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        var store = new RecordingStore(BrokerCommandTestFixture.DispatchClaim(command));
        BrokerCommandCoordinator coordinator = Create(store, new Mt5ProofOnlyGateway());

        BrokerCommandDispatchResult result = await coordinator.DispatchAsync(
            BrokerCommandTestFixture.Context(command),
            BrokerCommandTestFixture.Reference(command));

        Assert.Equal(BrokerCommandDispatchOutcome.SubmissionRecorded, result.Outcome);
        Assert.Equal(GatewayCommandDisposition.SubmissionDisabled, result.Disposition);
        Assert.Equal(Mt5ProofOnlyGateway.ProofOnlyCode, result.Code);
        Assert.Equal(GatewayCommandDisposition.SubmissionDisabled, store.Submission?.Disposition);
    }

    [Fact]
    public async Task ReplayedClaimNeverCallsGatewayAndBecomesUnknown()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        var store = new RecordingStore(
            BrokerCommandTestFixture.DispatchClaim(command, replayed: true));
        var gateway = new RecordingGateway();

        BrokerCommandDispatchResult result = await Create(store, gateway).DispatchAsync(
            BrokerCommandTestFixture.Context(command),
            BrokerCommandTestFixture.Reference(command));

        Assert.Equal(0, gateway.SendCalls);
        Assert.Equal(GatewayCommandDisposition.Unknown, store.Submission?.Disposition);
        Assert.Equal(BrokerCommandDispatchOutcome.ReconciliationRequired, result.Outcome);
    }

    [Fact]
    public async Task ExpiredClaimNeverCallsGatewayAndCanOnlyBecomeUnknown()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        var store = new RecordingStore(
            BrokerCommandTestFixture.DispatchClaim(
                command,
                expiresAt: BrokerCommandTestFixture.Now));
        var gateway = new RecordingGateway();

        BrokerCommandDispatchResult result = await Create(store, gateway).DispatchAsync(
            BrokerCommandTestFixture.Context(command),
            BrokerCommandTestFixture.Reference(command));

        Assert.Equal(0, gateway.SendCalls);
        Assert.Equal(GatewayCommandDisposition.Unknown, store.Submission?.Disposition);
        Assert.Equal(BrokerCommandDispatchOutcome.ReconciliationRequired, result.Outcome);
    }

    [Theory]
    [InlineData("exposure")]
    [InlineData("lease")]
    public async Task ExpiredAuthorityNeverCallsGatewayAndCanOnlyBecomeUnknown(string authority)
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized(
            exposureValidUntil: authority == "exposure"
                ? BrokerCommandTestFixture.Now
                : null,
            leaseExpiresAt: authority == "lease"
                ? BrokerCommandTestFixture.Now
                : null);
        var store = new RecordingStore(BrokerCommandTestFixture.DispatchClaim(command));
        var gateway = new RecordingGateway();

        BrokerCommandDispatchResult result = await Create(store, gateway).DispatchAsync(
            BrokerCommandTestFixture.Context(command),
            BrokerCommandTestFixture.Reference(command));

        Assert.Equal(0, gateway.SendCalls);
        Assert.Equal(GatewayCommandDisposition.Unknown, store.Submission?.Disposition);
        Assert.Equal(BrokerCommandDispatchOutcome.ReconciliationRequired, result.Outcome);
    }

    [Fact]
    public async Task AmbiguousClaimStoreFailureNeverCallsGateway()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        var store = new RecordingStore(BrokerCommandTestFixture.DispatchClaim(command))
        {
            ClaimException = new IOException("claim response lost")
        };
        var gateway = new RecordingGateway();

        BrokerCommandDispatchResult result = await Create(store, gateway).DispatchAsync(
            BrokerCommandTestFixture.Context(command),
            BrokerCommandTestFixture.Reference(command));

        Assert.Equal(BrokerCommandDispatchOutcome.DurableRecoveryRequired, result.Outcome);
        Assert.Equal("broker_command_claim_store_failed", result.Code);
        Assert.Equal(0, gateway.SendCalls);
        Assert.Equal(0, store.RecordSubmissionCalls);
    }

    [Fact]
    public async Task UntrustedLeaseIsPersistedAsSubmissionDisabledWithoutGatewayCall()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        var store = new RecordingStore(BrokerCommandTestFixture.DispatchClaim(command));
        var gateway = new RecordingGateway();

        BrokerCommandDispatchResult result = await Create(
                store,
                gateway,
                new FixedLeaseTrustVerifier(trusted: false))
            .DispatchAsync(
                BrokerCommandTestFixture.Context(command),
                BrokerCommandTestFixture.Reference(command));

        Assert.Equal(0, gateway.SendCalls);
        Assert.Equal(GatewayCommandDisposition.SubmissionDisabled, store.Submission?.Disposition);
        Assert.Equal(BrokerCommandDispatchOutcome.SubmissionRecorded, result.Outcome);
    }

    [Fact]
    public async Task GatewayExceptionAfterInvocationPersistsUnknownAndNeverRetriesSend()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        var store = new RecordingStore(BrokerCommandTestFixture.DispatchClaim(command));
        var gateway = new RecordingGateway { SendException = new IOException("ambiguous") };

        BrokerCommandDispatchResult result = await Create(store, gateway).DispatchAsync(
            BrokerCommandTestFixture.Context(command),
            BrokerCommandTestFixture.Reference(command));

        Assert.Equal(1, gateway.SendCalls);
        Assert.Equal(GatewayCommandDisposition.Unknown, store.Submission?.Disposition);
        Assert.Equal(BrokerCommandDispatchOutcome.ReconciliationRequired, result.Outcome);
    }

    [Fact]
    public async Task CallerCancellationAfterClaimIsSettledWithoutGatewayInvocation()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        using var cancellation = new CancellationTokenSource();
        var store = new RecordingStore(BrokerCommandTestFixture.DispatchClaim(command))
        {
            AfterClaim = cancellation.Cancel
        };
        var gateway = new RecordingGateway();

        BrokerCommandDispatchResult result = await Create(store, gateway).DispatchAsync(
            BrokerCommandTestFixture.Context(command),
            BrokerCommandTestFixture.Reference(command),
            cancellation.Token);

        Assert.Equal(0, gateway.SendCalls);
        Assert.Equal(GatewayCommandDisposition.SubmissionDisabled, store.Submission?.Disposition);
        Assert.False(store.SubmissionTokenWasCancelled);
        Assert.Equal(BrokerCommandDispatchOutcome.SubmissionRecorded, result.Outcome);
    }

    [Fact]
    public async Task PostSendPersistenceFailureReliesOnDurableMarkerAndNeverResends()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        var store = new RecordingStore(BrokerCommandTestFixture.DispatchClaim(command))
        {
            RecordSubmissionException = new IOException("database response lost")
        };
        var gateway = new RecordingGateway();

        BrokerCommandDispatchResult result = await Create(store, gateway).DispatchAsync(
            BrokerCommandTestFixture.Context(command),
            BrokerCommandTestFixture.Reference(command));

        Assert.Equal(BrokerCommandDispatchOutcome.DurableRecoveryRequired, result.Outcome);
        Assert.Equal("send_in_progress", result.DurableState);
        Assert.Equal(1, gateway.SendCalls);
        Assert.Equal(1, store.RecordSubmissionCalls);
    }

    [Fact]
    public async Task ExpiredSendRecoveryGoesDirectlyToReconciliationWithoutClaimOrSend()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        var store = new RecordingStore(BrokerCommandTestFixture.DispatchClaim(command))
        {
            Recovery = Receipt(command, "unknown")
        };
        var gateway = new RecordingGateway();

        BrokerCommandDispatchResult result = await Create(store, gateway).DispatchAsync(
            BrokerCommandTestFixture.Context(command),
            BrokerCommandTestFixture.Reference(command));

        Assert.Equal(BrokerCommandDispatchOutcome.ReconciliationRequired, result.Outcome);
        Assert.Equal(0, store.ClaimCalls);
        Assert.Equal(0, gateway.SendCalls);
    }

    [Fact]
    public async Task InvalidRecoveryReceiptStopsWithoutClaimOrGatewaySend()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        var store = new RecordingStore(BrokerCommandTestFixture.DispatchClaim(command))
        {
            Recovery = Receipt(command, "acknowledged")
        };
        var gateway = new RecordingGateway();

        BrokerCommandDispatchResult result = await Create(store, gateway).DispatchAsync(
            BrokerCommandTestFixture.Context(command),
            BrokerCommandTestFixture.Reference(command));

        Assert.Equal(BrokerCommandDispatchOutcome.DurableRecoveryRequired, result.Outcome);
        Assert.Equal("broker_command_recovery_receipt_invalid", result.Code);
        Assert.Equal(0, store.ClaimCalls);
        Assert.Equal(0, gateway.SendCalls);
    }

    [Fact]
    public async Task InvalidAcceptedGatewayReceiptIsDowngradedToUnknown()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        var store = new RecordingStore(BrokerCommandTestFixture.DispatchClaim(command));
        var gateway = new RecordingGateway
        {
            SendResult = new GatewaySendResult(
                GatewayCommandDisposition.Accepted,
                "accepted",
                null,
                null,
                null,
                BrokerCommandTestFixture.Now)
        };

        await Create(store, gateway).DispatchAsync(
            BrokerCommandTestFixture.Context(command),
            BrokerCommandTestFixture.Reference(command));

        Assert.Equal(GatewayCommandDisposition.Unknown, store.Submission?.Disposition);
        Assert.Equal("broker_command_gateway_result_invalid", store.Submission?.Code);
    }

    [Fact]
    public async Task PendingOrderMutationReceiptMustNameTheExactTargetOrder()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized(
            BrokerCommandAction.Cancel);
        var store = new RecordingStore(BrokerCommandTestFixture.DispatchClaim(command));
        var gateway = new RecordingGateway
        {
            SendResult = new GatewaySendResult(
                GatewayCommandDisposition.Accepted,
                "accepted",
                "request-1",
                "different-order",
                null,
                BrokerCommandTestFixture.Now)
        };

        await Create(store, gateway).DispatchAsync(
            BrokerCommandTestFixture.Context(command),
            BrokerCommandTestFixture.Reference(command));

        Assert.Equal(GatewayCommandDisposition.Unknown, store.Submission?.Disposition);
        Assert.Equal("broker_command_gateway_result_invalid", store.Submission?.Code);
    }

    [Fact]
    public async Task InvalidSubmissionStoreReceiptIsReportedAsDurableAmbiguity()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        var store = new RecordingStore(BrokerCommandTestFixture.DispatchClaim(command))
        {
            SubmissionReceipt = Receipt(command, "rejected")
        };
        var gateway = new RecordingGateway();

        BrokerCommandDispatchResult result = await Create(store, gateway).DispatchAsync(
            BrokerCommandTestFixture.Context(command),
            BrokerCommandTestFixture.Reference(command));

        Assert.Equal(BrokerCommandDispatchOutcome.DurableRecoveryRequired, result.Outcome);
        Assert.Equal("broker_command_submission_receipt_invalid", result.Code);
        Assert.Equal(1, gateway.SendCalls);
        Assert.Equal(1, store.RecordSubmissionCalls);
    }

    [Fact]
    public async Task ProofOnlyReconciliationIsPersistedInconclusiveAndRetryable()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        var store = new RecordingStore(BrokerCommandTestFixture.DispatchClaim(command))
        {
            ReconciliationClaim = BrokerCommandTestFixture.ReconciliationClaim(command)
        };

        BrokerCommandReconciliationResult result = await Create(
                store,
                new Mt5ProofOnlyGateway())
            .ReconcileAsync(
                BrokerCommandTestFixture.Context(command),
                BrokerCommandTestFixture.Reference(command));

        Assert.Equal(BrokerCommandReconciliationOutcome.InconclusiveRetryable, result.Outcome);
        Assert.Equal(BrokerReconciliationMatch.Inconclusive, store.Reconciliation?.Match);
        Assert.Null(store.Reconciliation?.Snapshot);
        Assert.Equal(1, store.CompleteReconciliationCalls);
    }

    [Fact]
    public async Task CompleteAtomicSnapshotWithNewSourceSequenceCanBecomeTerminal()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        BrokerCommandReconciliationClaim claim =
            BrokerCommandTestFixture.ReconciliationClaim(command);
        var store = new RecordingStore(BrokerCommandTestFixture.DispatchClaim(command))
        {
            ReconciliationClaim = claim
        };
        var gateway = new RecordingGateway { ReconciliationSnapshot = Snapshot(command, claim) };

        BrokerCommandReconciliationResult result = await Create(store, gateway).ReconcileAsync(
            BrokerCommandTestFixture.Context(command),
            BrokerCommandTestFixture.Reference(command));

        Assert.Equal(BrokerCommandReconciliationOutcome.Completed, result.Outcome);
        Assert.Equal(BrokerReconciliationMatch.Acknowledged, store.Reconciliation?.Match);
        Assert.Equal("broker_reconciliation_snapshot_proven", store.Reconciliation?.ReasonCode);
    }

    [Fact]
    public async Task UntrustedReconciliationClaimNeverQueriesGateway()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        var store = new RecordingStore(BrokerCommandTestFixture.DispatchClaim(command))
        {
            ReconciliationClaim = BrokerCommandTestFixture.ReconciliationClaim(command)
        };
        var gateway = new RecordingGateway();

        BrokerCommandReconciliationResult result = await Create(
                store,
                gateway,
                new FixedLeaseTrustVerifier(trusted: false))
            .ReconcileAsync(
                BrokerCommandTestFixture.Context(command),
                BrokerCommandTestFixture.Reference(command));

        Assert.Equal(0, gateway.ReconcileCalls);
        Assert.Equal(BrokerCommandReconciliationOutcome.InconclusiveRetryable, result.Outcome);
        Assert.Equal(
            "broker_reconciliation_gateway_observation_unavailable",
            store.Reconciliation?.ReasonCode);
    }

    [Fact]
    public async Task FutureReconciliationClaimNeverQueriesGateway()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        var store = new RecordingStore(BrokerCommandTestFixture.DispatchClaim(command))
        {
            ReconciliationClaim = BrokerCommandTestFixture.ReconciliationClaim(
                command,
                startedAt: BrokerCommandTestFixture.Now.AddSeconds(1))
        };
        var gateway = new RecordingGateway();

        BrokerCommandReconciliationResult result = await Create(store, gateway).ReconcileAsync(
            BrokerCommandTestFixture.Context(command),
            BrokerCommandTestFixture.Reference(command));

        Assert.Equal(0, gateway.ReconcileCalls);
        Assert.Equal(BrokerCommandReconciliationOutcome.InconclusiveRetryable, result.Outcome);
    }

    [Fact]
    public async Task ReconciliationPersistenceFailureLeavesClaimForDurableRecovery()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        var store = new RecordingStore(BrokerCommandTestFixture.DispatchClaim(command))
        {
            ReconciliationClaim = BrokerCommandTestFixture.ReconciliationClaim(command),
            CompleteReconciliationException = new IOException("response lost")
        };

        BrokerCommandReconciliationResult result = await Create(
                store,
                new Mt5ProofOnlyGateway())
            .ReconcileAsync(
                BrokerCommandTestFixture.Context(command),
                BrokerCommandTestFixture.Reference(command));

        Assert.Equal(BrokerCommandReconciliationOutcome.DurableRecoveryRequired, result.Outcome);
        Assert.True(result.GatewayInvoked);
    }

    [Fact]
    public async Task InvalidReconciliationStoreReceiptIsReportedAsDurableAmbiguity()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        var store = new RecordingStore(BrokerCommandTestFixture.DispatchClaim(command))
        {
            ReconciliationClaim = BrokerCommandTestFixture.ReconciliationClaim(command),
            ReconciliationReceipt = Receipt(command, "reconciled")
        };

        BrokerCommandReconciliationResult result = await Create(
                store,
                new Mt5ProofOnlyGateway())
            .ReconcileAsync(
                BrokerCommandTestFixture.Context(command),
                BrokerCommandTestFixture.Reference(command));

        Assert.Equal(BrokerCommandReconciliationOutcome.DurableRecoveryRequired, result.Outcome);
        Assert.Equal("broker_reconciliation_receipt_invalid", result.Code);
    }

    [Fact]
    public void AuthorizedCapabilityHasNoPublicConstructorOrHydrationFactory()
    {
        Assert.Empty(typeof(AuthorizedBrokerCommand).GetConstructors());
        Assert.DoesNotContain(
            typeof(AuthorizedBrokerCommand).GetMethods(),
            method => method.IsPublic
                && method.IsStatic
                && method.ReturnType == typeof(AuthorizedBrokerCommand));
    }

    [Fact]
    public void ApplicationBoundaryHasNoPostgresNpgsqlOrVendorDependency()
    {
        string[] references = typeof(BrokerCommandCoordinator).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain("Npgsql", references);
        Assert.DoesNotContain("YO4X.Trading.Postgres", references);
        Assert.DoesNotContain(
            references,
            reference => reference.Contains("MetaTrader", StringComparison.OrdinalIgnoreCase)
                || reference.Contains("MtApi", StringComparison.OrdinalIgnoreCase));
    }

    private static BrokerCommandCoordinator Create(
        RecordingStore store,
        IMt5Gateway gateway,
        IExecutionLeaseTrustVerifier? trust = null) => new(
            store,
            gateway,
            trust ?? new FixedLeaseTrustVerifier(),
            new BrokerCommandCoordinatorOptions(),
            new FixedTimeProvider(BrokerCommandTestFixture.Now),
            new SequenceIdentifierSource());

    private static BrokerCommandLifecycleReceipt Receipt(
        AuthorizedBrokerCommand command,
        string state,
        long commandVersion = 3) => new(
            command.Command.CommandId,
            state,
            new string('1', 64),
            commandVersion,
            BrokerCommandTestFixture.Now,
            false);

    private static BrokerReconciliationSnapshot Snapshot(
        AuthorizedBrokerCommand command,
        BrokerCommandReconciliationClaim claim)
    {
        var account = new BrokerAccountSnapshot(
            50,
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
            claim.StartedAtUtc);
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
            claim.StartedAtUtc);
        var result = new BrokerCommandReconciliation(
            command.Command.CommandId,
            BrokerReconciliationMatch.Acknowledged,
            "acknowledged",
            order.OrderId,
            null,
            claim.StartedAtUtc);
        return new BrokerReconciliationSnapshot(
            1,
            command.Exposure.SourceSequence + 1,
            command.Provenance.BrokerAccountId,
            command.Command.DeploymentId,
            command.Command.Generation,
            command.Provenance.GatewayArtifactId,
            command.Provenance.GatewayArtifactSha256,
            claim.StartedAtUtc,
            claim.StartedAtUtc,
            true,
            true,
            account,
            [],
            [order],
            [],
            [result],
            claim.StartedAtUtc);
    }

    private sealed class RecordingStore(BrokerCommandDispatchClaim dispatchClaim)
        : IBrokerCommandLifecycleStore
    {
        public BrokerCommandLifecycleReceipt? Recovery { get; init; }

        public BrokerCommandReconciliationClaim? ReconciliationClaim { get; init; }

        public Exception? RecordSubmissionException { get; init; }

        public Exception? ClaimException { get; init; }

        public Exception? CompleteReconciliationException { get; init; }

        public BrokerCommandLifecycleReceipt? SubmissionReceipt { get; init; }

        public BrokerCommandLifecycleReceipt? ReconciliationReceipt { get; init; }

        public Action? AfterClaim { get; init; }

        public int ClaimCalls { get; private set; }

        public int RecordSubmissionCalls { get; private set; }

        public int CompleteReconciliationCalls { get; private set; }

        public GatewaySendResult? Submission { get; private set; }

        public bool SubmissionTokenWasCancelled { get; private set; }

        public ValidatedBrokerCommandReconciliation? Reconciliation { get; private set; }

        public Task<BrokerCommandDispatchClaim> ClaimForDispatchAsync(
            TenantExecutionContext context,
            BrokerCommandReference reference,
            Guid claimToken,
            Guid auditEventId,
            CancellationToken cancellationToken)
        {
            ClaimCalls++;
            AfterClaim?.Invoke();
            if (ClaimException is not null)
            {
                throw ClaimException;
            }

            return Task.FromResult(dispatchClaim);
        }

        public Task<BrokerCommandLifecycleReceipt> RecordSubmissionAsync(
            TenantExecutionContext context,
            BrokerCommandDispatchClaim claim,
            GatewaySendResult result,
            Guid auditEventId,
            CancellationToken cancellationToken)
        {
            RecordSubmissionCalls++;
            Submission = result;
            SubmissionTokenWasCancelled = cancellationToken.IsCancellationRequested;
            if (RecordSubmissionException is not null)
            {
                throw RecordSubmissionException;
            }

            string state = result.Disposition switch
            {
                GatewayCommandDisposition.Accepted => "acknowledged",
                GatewayCommandDisposition.Unknown => "unknown",
                _ => "rejected"
            };
            return Task.FromResult(SubmissionReceipt ?? Receipt(claim.Command, state));
        }

        public Task<BrokerCommandLifecycleReceipt?> RecoverExpiredLifecycleAsync(
            TenantExecutionContext context,
            Guid commandId,
            string authorizationSha256,
            Guid auditEventId,
            CancellationToken cancellationToken) => Task.FromResult(Recovery);

        public Task<BrokerCommandReconciliationClaim> BeginReconciliationAsync(
            TenantExecutionContext context,
            Guid commandId,
            string authorizationSha256,
            Guid reconciliationClaimToken,
            Guid auditEventId,
            CancellationToken cancellationToken) => Task.FromResult(
                ReconciliationClaim
                ?? throw new InvalidOperationException("No reconciliation claim configured."));

        public Task<BrokerCommandLifecycleReceipt> CompleteReconciliationAsync(
            TenantExecutionContext context,
            Guid reconciliationClaimToken,
            Guid reconciliationId,
            ValidatedBrokerCommandReconciliation evidence,
            Guid auditEventId,
            CancellationToken cancellationToken)
        {
            CompleteReconciliationCalls++;
            Reconciliation = evidence;
            if (CompleteReconciliationException is not null)
            {
                throw CompleteReconciliationException;
            }

            return Task.FromResult(ReconciliationReceipt ?? Receipt(
                dispatchClaim.Command,
                evidence.IsConclusive ? "reconciled" : "unknown",
                (ReconciliationClaim?.CommandVersion ?? dispatchClaim.CommandVersion) + 1));
        }
    }

    private sealed class RecordingGateway : IMt5Gateway
    {
        public GatewayConnectionState ConnectionState => GatewayConnectionState.Connected;

        public GatewaySendResult SendResult { get; init; } = new(
            GatewayCommandDisposition.Accepted,
            "accepted",
            "request-1",
            "order-1",
            null,
            BrokerCommandTestFixture.Now);

        public Exception? SendException { get; init; }

        public BrokerReconciliationSnapshot? ReconciliationSnapshot { get; init; }

        public int SendCalls { get; private set; }

        public int ReconcileCalls { get; private set; }

        public Task<GatewaySendResult> SendAsync(
            AuthorizedBrokerCommand command,
            CancellationToken cancellationToken)
        {
            SendCalls++;
            return SendException is null
                ? Task.FromResult(SendResult)
                : Task.FromException<GatewaySendResult>(SendException);
        }

        public Task<GatewayOperationResult<BrokerReconciliationSnapshot>> ReconcileAsync(
            IReadOnlyCollection<Guid> commandIds,
            CancellationToken cancellationToken)
        {
            ReconcileCalls++;
            return Task.FromResult(
                ReconciliationSnapshot is null
                    ? new GatewayOperationResult<BrokerReconciliationSnapshot>(
                        false,
                        "unavailable",
                        null)
                    : new GatewayOperationResult<BrokerReconciliationSnapshot>(
                        true,
                        "ok",
                        ReconciliationSnapshot));
        }

        public Task<GatewayOperationResult<GatewayCapabilities>> ConnectAsync(
            GatewayConnectionRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GatewayOperationResult> DisconnectAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GatewayOperationResult<BrokerAccountSnapshot>> GetAccountAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GatewayOperationResult<IReadOnlyList<BrokerQuoteSnapshot>>> GetQuotesAsync(
            IReadOnlyCollection<string> symbols,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GatewayOperationResult<IReadOnlyList<BrokerPositionSnapshot>>> GetPositionsAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GatewayOperationResult<IReadOnlyList<BrokerOrderSnapshot>>> GetOrdersAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GatewayOperationResult<IReadOnlyList<BrokerDealSnapshot>>> GetDealsAsync(
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
