using YO4X.Tenancy;
using YO4X.Trading.Abstractions;
using YO4X.Trading.Application;
using YO4X.Trading.Mt5;

namespace YO4X.Trading.Application.Tests;

public sealed class BrokerCommandCoordinatorTests
{
    [Fact]
    public void DefaultMutationTimingFitsTheDatabasePlaceAuthorityWindow()
    {
        var options = new BrokerCommandCoordinatorOptions();

        options.Validate();

        Assert.Equal(TimeSpan.FromMilliseconds(500), options.GatewaySendTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(100), options.AuthoritySafetyMargin);
        Assert.Equal(TimeSpan.FromMilliseconds(600), options.MinimumAuthorityWindow);
    }

    [Theory]
    [InlineData(GatewayCommandDisposition.Accepted)]
    [InlineData(GatewayCommandDisposition.Unknown)]
    public async Task BrokerOutcomesWithExternalCommitmentAreRecordedAndRequireReconciliation(
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
                BrokerCommandTestFixture.Now,
                true)
        };
        BrokerCommandCoordinator coordinator = Create(store, gateway);

        BrokerCommandDispatchResult result = await coordinator.DispatchAsync(
            BrokerCommandTestFixture.Context(command),
            BrokerCommandTestFixture.Reference(command),
            TestContext.Current.CancellationToken);

        Assert.Equal(BrokerCommandDispatchOutcome.ReconciliationRequired, result.Outcome);
        Assert.Equal(1, gateway.SendCalls);
        Assert.Equal(1, store.RecordSubmissionCalls);
        Assert.Equal(disposition, store.Submission?.Disposition);
        Assert.False(store.Submission?.PreInvocationNotSentProven);
        Assert.False(store.SubmissionTokenWasCancelled);
    }

    [Fact]
    public async Task ReturnedRejectedIsUnknownAndRequiresReconciliation()
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
                BrokerCommandTestFixture.Now,
                false)
        };

        BrokerCommandDispatchResult result = await Create(store, gateway).DispatchAsync(
            BrokerCommandTestFixture.Context(command),
            BrokerCommandTestFixture.Reference(command),
            TestContext.Current.CancellationToken);

        Assert.Equal(BrokerCommandDispatchOutcome.ReconciliationRequired, result.Outcome);
        Assert.Equal(GatewayCommandDisposition.Unknown, result.Disposition);
        Assert.Equal("broker_command_gateway_outcome_unproven", result.Code);
        Assert.Equal(1, gateway.SendCalls);
        Assert.False(store.Submission?.PreInvocationNotSentProven);
    }

    [Fact]
    public async Task ReturnedSubmissionDisabledIsUnknownAndRequiresReconciliation()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        var store = new RecordingStore(BrokerCommandTestFixture.DispatchClaim(command));
        var gateway = new RecordingGateway
        {
            SendResult = new GatewaySendResult(
                GatewayCommandDisposition.SubmissionDisabled,
                "gateway_disabled_after_entry",
                null,
                null,
                null,
                BrokerCommandTestFixture.Now,
                false)
        };

        BrokerCommandDispatchResult result = await Create(store, gateway).DispatchAsync(
            BrokerCommandTestFixture.Context(command),
            BrokerCommandTestFixture.Reference(command),
            TestContext.Current.CancellationToken);

        Assert.Equal(BrokerCommandDispatchOutcome.ReconciliationRequired, result.Outcome);
        Assert.Equal(GatewayCommandDisposition.Unknown, store.Submission?.Disposition);
        Assert.Equal(1, gateway.SendCalls);
        Assert.False(store.Submission?.PreInvocationNotSentProven);
    }

    [Fact]
    public async Task UndefinedGatewayDispositionIsNormalizedToUnknown()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        var store = new RecordingStore(BrokerCommandTestFixture.DispatchClaim(command));
        var gateway = new RecordingGateway
        {
            SendResult = new GatewaySendResult(
                (GatewayCommandDisposition)999,
                "undefined_disposition",
                "request-1",
                null,
                null,
                BrokerCommandTestFixture.Now,
                true)
        };

        BrokerCommandDispatchResult result = await Create(store, gateway).DispatchAsync(
            BrokerCommandTestFixture.Context(command),
            BrokerCommandTestFixture.Reference(command),
            TestContext.Current.CancellationToken);

        Assert.Equal(BrokerCommandDispatchOutcome.ReconciliationRequired, result.Outcome);
        Assert.Equal(GatewayCommandDisposition.Unknown, store.Submission?.Disposition);
        Assert.Equal("broker_command_gateway_result_invalid", store.Submission?.Code);
        Assert.False(store.Submission?.PreInvocationNotSentProven);
    }

    [Fact]
    public async Task ProofOnlyGatewayPersistsSubmissionDisabledWithoutVendorExecution()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        var store = new RecordingStore(BrokerCommandTestFixture.DispatchClaim(command));
        BrokerCommandCoordinator coordinator = Create(
            store,
            new Mt5ProofOnlyGateway(),
            submissionEnabled: false);

        BrokerCommandDispatchResult result = await coordinator.DispatchAsync(
            BrokerCommandTestFixture.Context(command),
            BrokerCommandTestFixture.Reference(command),
            TestContext.Current.CancellationToken);

        Assert.Equal(BrokerCommandDispatchOutcome.SubmissionRecorded, result.Outcome);
        Assert.Equal(GatewayCommandDisposition.SubmissionDisabled, result.Disposition);
        Assert.Equal("broker_command_gateway_entry_disabled", result.Code);
        Assert.Equal(GatewayCommandDisposition.SubmissionDisabled, store.Submission?.Disposition);
        Assert.Equal("submission_disabled", result.DurableState);
        Assert.False(result.GatewayInvoked);
        Assert.True(store.Submission?.PreInvocationNotSentProven);
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
            BrokerCommandTestFixture.Reference(command),
            TestContext.Current.CancellationToken);

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
            BrokerCommandTestFixture.Reference(command),
            TestContext.Current.CancellationToken);

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
            BrokerCommandTestFixture.Reference(command),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, gateway.SendCalls);
        Assert.Equal(GatewayCommandDisposition.Unknown, store.Submission?.Disposition);
        Assert.Equal(BrokerCommandDispatchOutcome.ReconciliationRequired, result.Outcome);
    }

    [Fact]
    public async Task FreshDatabaseAuthorityIsRecheckedImmediatelyBeforeGatewayEntry()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        var clock = new ControllableTimeProvider(BrokerCommandTestFixture.Now);
        var store = new RecordingStore(
            BrokerCommandTestFixture.DispatchClaim(
                command,
                expiresAt: BrokerCommandTestFixture.Now.AddSeconds(1)));
        var gateway = new RecordingGateway();

        BrokerCommandDispatchResult result = await Create(
                store,
                gateway,
                timeProvider: clock)
            .DispatchAsync(
                BrokerCommandTestFixture.Context(command),
                BrokerCommandTestFixture.Reference(command),
                TestContext.Current.CancellationToken);

        Assert.Equal(1, gateway.SendCalls);
        Assert.True(result.RequiresReconciliation);
        Assert.False(store.Submission?.PreInvocationNotSentProven);
    }

    [Fact]
    public async Task SubmissionEvidenceIsNormalizedOnceBeforePersistenceAndReceiptValidation()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        BrokerCommandDispatchClaim claim = BrokerCommandTestFixture.DispatchClaim(command);
        var time = new ControllableTimeProvider(BrokerCommandTestFixture.Now);
        var store = new RecordingStore(claim);
        var gateway = new RecordingGateway
        {
            SendResult = new GatewaySendResult(
                GatewayCommandDisposition.Accepted,
                "accepted",
                "request-submicro",
                "order-submicro",
                null,
                BrokerCommandTestFixture.Now.AddTicks(7),
                false),
            DuringSend = () => time.Advance(TimeSpan.FromTicks(7))
        };

        BrokerCommandDispatchResult result = await Create(
                store,
                gateway,
                timeProvider: time)
            .DispatchAsync(
                BrokerCommandTestFixture.Context(command),
                BrokerCommandTestFixture.Reference(command),
                TestContext.Current.CancellationToken);

        Assert.Equal(BrokerCommandDispatchOutcome.ReconciliationRequired, result.Outcome);
        Assert.NotNull(store.Submission);
        Assert.Equal(
            0,
            store.Submission.ObservedAtUtc.Ticks % TimeSpan.TicksPerMicrosecond);
        Assert.Equal(BrokerCommandTestFixture.Now, store.Submission.ObservedAtUtc);
    }

    [Fact]
    public async Task AuthorityThatBecomesTooShortBeforeGatewayEntryStaysUnknown()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        var clock = new ControllableTimeProvider(BrokerCommandTestFixture.Now);
        var store = new RecordingStore(
            BrokerCommandTestFixture.DispatchClaim(
                command,
                expiresAt: BrokerCommandTestFixture.Now.AddSeconds(1)))
        {
            AfterClaim = () => clock.Advance(TimeSpan.FromMilliseconds(500))
        };
        var gateway = new RecordingGateway();

        BrokerCommandDispatchResult result = await Create(
                store,
                gateway,
                timeProvider: clock)
            .DispatchAsync(
                BrokerCommandTestFixture.Context(command),
                BrokerCommandTestFixture.Reference(command),
                TestContext.Current.CancellationToken);

        Assert.Equal(0, gateway.SendCalls);
        Assert.Equal(BrokerCommandDispatchOutcome.ReconciliationRequired, result.Outcome);
        Assert.Equal(GatewayCommandDisposition.Unknown, store.Submission?.Disposition);
        Assert.False(store.Submission?.PreInvocationNotSentProven);
    }

    [Fact]
    public async Task SynchronousGatewayOverrunIsPersistedUnknownEvenWhenItReturnsAccepted()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        var clock = new ControllableTimeProvider(BrokerCommandTestFixture.Now);
        var store = new RecordingStore(
            BrokerCommandTestFixture.DispatchClaim(
                command,
                expiresAt: BrokerCommandTestFixture.Now.AddSeconds(2)));
        var gateway = new RecordingGateway
        {
            DuringSend = () => clock.Advance(TimeSpan.FromMilliseconds(600))
        };

        BrokerCommandDispatchResult result = await Create(
                store,
                gateway,
                timeProvider: clock)
            .DispatchAsync(
                BrokerCommandTestFixture.Context(command),
                BrokerCommandTestFixture.Reference(command),
                TestContext.Current.CancellationToken);

        Assert.Equal(1, gateway.SendCalls);
        Assert.Equal(BrokerCommandDispatchOutcome.ReconciliationRequired, result.Outcome);
        Assert.Equal(GatewayCommandDisposition.Unknown, store.Submission?.Disposition);
        Assert.Equal("broker_command_gateway_timeout_unknown", store.Submission?.Code);
        Assert.False(store.Submission?.PreInvocationNotSentProven);
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
            BrokerCommandTestFixture.Reference(command),
            TestContext.Current.CancellationToken);

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
                BrokerCommandTestFixture.Reference(command),
                TestContext.Current.CancellationToken);

        Assert.Equal(0, gateway.SendCalls);
        Assert.Equal(GatewayCommandDisposition.SubmissionDisabled, store.Submission?.Disposition);
        Assert.True(store.Submission?.PreInvocationNotSentProven);
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
            BrokerCommandTestFixture.Reference(command),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, gateway.SendCalls);
        Assert.Equal(GatewayCommandDisposition.Unknown, store.Submission?.Disposition);
        Assert.False(store.Submission?.PreInvocationNotSentProven);
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
            BrokerCommandTestFixture.Reference(command),
            TestContext.Current.CancellationToken);

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
            BrokerCommandTestFixture.Reference(command),
            TestContext.Current.CancellationToken);

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
            BrokerCommandTestFixture.Reference(command),
            TestContext.Current.CancellationToken);

        Assert.Equal(BrokerCommandDispatchOutcome.DurableRecoveryRequired, result.Outcome);
        Assert.Equal("broker_command_recovery_receipt_invalid", result.Code);
        Assert.Equal(0, store.ClaimCalls);
        Assert.Equal(0, gateway.SendCalls);
    }

    [Fact]
    public async Task RecoveryReceiptWithWrongWellFormedDigestIsRejected()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        var store = new RecordingStore(BrokerCommandTestFixture.DispatchClaim(command))
        {
            Recovery = Receipt(command, "unknown", evidenceSha256: new string('f', 64))
        };
        var gateway = new RecordingGateway();

        BrokerCommandDispatchResult result = await Create(store, gateway).DispatchAsync(
            BrokerCommandTestFixture.Context(command),
            BrokerCommandTestFixture.Reference(command),
            TestContext.Current.CancellationToken);

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
                BrokerCommandTestFixture.Now,
                false)
        };

        await Create(store, gateway).DispatchAsync(
            BrokerCommandTestFixture.Context(command),
            BrokerCommandTestFixture.Reference(command),
            TestContext.Current.CancellationToken);

        Assert.Equal(GatewayCommandDisposition.Unknown, store.Submission?.Disposition);
        Assert.Equal("broker_command_gateway_result_invalid", store.Submission?.Code);
    }

    [Theory]
    [InlineData("request\0id", null, null)]
    [InlineData("request-1", "order\0id", null)]
    [InlineData("request-1", null, "deal\0id")]
    public async Task GatewayReceiptWithNonPersistableBrokerIdIsDowngradedBeforeStorage(
        string? requestId,
        string? orderId,
        string? dealId)
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        var store = new RecordingStore(BrokerCommandTestFixture.DispatchClaim(command));
        var gateway = new RecordingGateway
        {
            SendResult = new GatewaySendResult(
                GatewayCommandDisposition.Accepted,
                "accepted",
                requestId,
                orderId,
                dealId,
                BrokerCommandTestFixture.Now,
                false)
        };

        BrokerCommandDispatchResult result = await Create(store, gateway).DispatchAsync(
            BrokerCommandTestFixture.Context(command),
            BrokerCommandTestFixture.Reference(command),
            TestContext.Current.CancellationToken);

        Assert.True(result.GatewayInvoked);
        Assert.Equal(GatewayCommandDisposition.Unknown, store.Submission?.Disposition);
        Assert.Equal("broker_command_gateway_result_invalid", store.Submission?.Code);
        Assert.Null(store.Submission?.BrokerRequestId);
        Assert.Null(store.Submission?.OrderId);
        Assert.Null(store.Submission?.DealId);
        Assert.Equal(1, store.RecordSubmissionCalls);
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
                BrokerCommandTestFixture.Now,
                false)
        };

        await Create(store, gateway).DispatchAsync(
            BrokerCommandTestFixture.Context(command),
            BrokerCommandTestFixture.Reference(command),
            TestContext.Current.CancellationToken);

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
            BrokerCommandTestFixture.Reference(command),
            TestContext.Current.CancellationToken);

        Assert.Equal(BrokerCommandDispatchOutcome.DurableRecoveryRequired, result.Outcome);
        Assert.Equal("broker_command_submission_receipt_invalid", result.Code);
        Assert.Equal(1, gateway.SendCalls);
        Assert.Equal(1, store.RecordSubmissionCalls);
    }

    [Fact]
    public async Task SubmissionReceiptWithWrongWellFormedDigestIsDurableAmbiguity()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        var store = new RecordingStore(BrokerCommandTestFixture.DispatchClaim(command))
        {
            SubmissionReceipt = Receipt(
                command,
                "acknowledged",
                commandVersion: 3,
                evidenceSha256: new string('f', 64))
        };
        var gateway = new RecordingGateway();

        BrokerCommandDispatchResult result = await Create(store, gateway).DispatchAsync(
            BrokerCommandTestFixture.Context(command),
            BrokerCommandTestFixture.Reference(command),
            TestContext.Current.CancellationToken);

        Assert.Equal(BrokerCommandDispatchOutcome.DurableRecoveryRequired, result.Outcome);
        Assert.Equal("broker_command_submission_receipt_invalid", result.Code);
        Assert.Equal(1, gateway.SendCalls);
        Assert.Equal(1, store.RecordSubmissionCalls);
    }

    [Fact]
    public async Task SubmissionReceiptMustBeTheExactNextVersionWithinClaimTime()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        BrokerCommandDispatchClaim claim = BrokerCommandTestFixture.DispatchClaim(command);
        var gateway = new RecordingGateway();
        GatewaySendResult normalized = BrokerCommandLifecycleEvidence.NormalizeSubmission(
            gateway.SendResult);
        var store = new RecordingStore(claim)
        {
            SubmissionReceipt = Receipt(
                command,
                "acknowledged",
                claim.CommandVersion + 2,
                BrokerCommandLifecycleEvidence.Submission(normalized).Sha256,
                claim.ClaimExpiresAtUtc.AddTicks(1))
        };

        BrokerCommandDispatchResult result = await Create(store, gateway).DispatchAsync(
            BrokerCommandTestFixture.Context(command),
            BrokerCommandTestFixture.Reference(command),
            TestContext.Current.CancellationToken);

        Assert.Equal(BrokerCommandDispatchOutcome.DurableRecoveryRequired, result.Outcome);
        Assert.Equal("broker_command_submission_receipt_invalid", result.Code);
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
                BrokerCommandTestFixture.Reference(command),
                TestContext.Current.CancellationToken);

        Assert.Equal(BrokerCommandReconciliationOutcome.InconclusiveRetryable, result.Outcome);
        Assert.Equal(BrokerReconciliationMatch.Inconclusive, store.Reconciliation?.Match);
        Assert.Null(store.Reconciliation?.Snapshot);
        Assert.Equal(1, store.CompleteReconciliationCalls);
    }

    [Fact]
    public async Task CompleteAtomicSnapshotRemainsRetryableWithoutAuthenticatedBrokerEvidence()
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
            BrokerCommandTestFixture.Reference(command),
            TestContext.Current.CancellationToken);

        Assert.Equal(BrokerCommandReconciliationOutcome.InconclusiveRetryable, result.Outcome);
        Assert.Equal(BrokerReconciliationMatch.Inconclusive, store.Reconciliation?.Match);
        Assert.Equal(
            "broker_reconciliation_terminal_authority_unavailable",
            store.Reconciliation?.ReasonCode);
        Assert.Null(store.Reconciliation?.SourceSequence);
        Assert.Null(store.Reconciliation?.Snapshot);
    }

    [Fact]
    public async Task ReconciliationSourceIsNormalizedBeforeItsDigestAndPersistenceEvidence()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        BrokerCommandReconciliationClaim claim =
            BrokerCommandTestFixture.ReconciliationClaim(command);
        BrokerReconciliationSnapshot baseline = Snapshot(command, claim);
        DateTimeOffset submicro = claim.StartedAtUtc.AddMilliseconds(1).AddTicks(7);
        BrokerReconciliationSnapshot snapshot = baseline with
        {
            QueryWindowEndUtc = submicro,
            CompletedAtUtc = submicro,
            Account = baseline.Account with { ObservedAtUtc = submicro },
            Orders = baseline.Orders
                .Select(order => order with { ObservedAtUtc = submicro })
                .ToArray(),
            CommandResults = baseline.CommandResults
                .Select(result => result with { ReconciledAtUtc = submicro })
                .ToArray()
        };
        var time = new ControllableTimeProvider(BrokerCommandTestFixture.Now);
        var store = new RecordingStore(BrokerCommandTestFixture.DispatchClaim(command))
        {
            ReconciliationClaim = claim
        };
        var gateway = new RecordingGateway
        {
            ReconciliationSnapshot = snapshot,
            DuringReconcile = () => time.Advance(TimeSpan.FromMilliseconds(2).Add(
                TimeSpan.FromTicks(7)))
        };

        BrokerCommandReconciliationResult result = await Create(
                store,
                gateway,
                timeProvider: time)
            .ReconcileAsync(
                BrokerCommandTestFixture.Context(command),
                BrokerCommandTestFixture.Reference(command),
                TestContext.Current.CancellationToken);

        Assert.Equal(BrokerCommandReconciliationOutcome.InconclusiveRetryable, result.Outcome);
        Assert.NotNull(store.Reconciliation);
        Assert.Equal(
            0,
            store.Reconciliation.ObservedAtUtc.Ticks % TimeSpan.TicksPerMicrosecond);
        Assert.Equal(store.Reconciliation.WindowEndUtc, store.Reconciliation.ObservedAtUtc);
        Assert.Equal(
            claim.StartedAtUtc.AddMilliseconds(1),
            store.Reconciliation.ObservedAtUtc);
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
                BrokerCommandTestFixture.Reference(command),
                TestContext.Current.CancellationToken);

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
            BrokerCommandTestFixture.Reference(command),
            TestContext.Current.CancellationToken);

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
                BrokerCommandTestFixture.Reference(command),
                TestContext.Current.CancellationToken);

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
                BrokerCommandTestFixture.Reference(command),
                TestContext.Current.CancellationToken);

        Assert.Equal(BrokerCommandReconciliationOutcome.DurableRecoveryRequired, result.Outcome);
        Assert.Equal("broker_reconciliation_receipt_invalid", result.Code);
    }

    [Fact]
    public async Task ReconciliationReceiptWithWrongWellFormedDigestIsDurableAmbiguity()
    {
        AuthorizedBrokerCommand command = BrokerCommandTestFixture.Authorized();
        BrokerCommandReconciliationClaim claim =
            BrokerCommandTestFixture.ReconciliationClaim(command);
        var store = new RecordingStore(BrokerCommandTestFixture.DispatchClaim(command))
        {
            ReconciliationClaim = claim,
            ReconciliationReceipt = Receipt(
                command,
                "unknown",
                claim.CommandVersion + 1,
                new string('f', 64),
                claim.StartedAtUtc)
        };

        BrokerCommandReconciliationResult result = await Create(
                store,
                new Mt5ProofOnlyGateway())
            .ReconcileAsync(
                BrokerCommandTestFixture.Context(command),
                BrokerCommandTestFixture.Reference(command),
                TestContext.Current.CancellationToken);

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
        IExecutionLeaseTrustVerifier? trust = null,
        bool submissionEnabled = true,
        TimeProvider? timeProvider = null) => new(
            store,
            gateway,
            trust ?? new FixedLeaseTrustVerifier(),
            new BrokerCommandCoordinatorOptions
            {
                SubmissionEnabled = submissionEnabled
            },
            timeProvider ?? new FixedTimeProvider(BrokerCommandTestFixture.Now),
            new SequenceIdentifierSource());

    private static BrokerCommandLifecycleReceipt Receipt(
        AuthorizedBrokerCommand command,
        string state,
        long commandVersion = 3,
        string? evidenceSha256 = null,
        DateTimeOffset? recordedAtUtc = null) => new(
            command.Command.CommandId,
            state,
            evidenceSha256 ?? command.AuthorizationSha256,
            commandVersion,
            recordedAtUtc ?? BrokerCommandTestFixture.Now,
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
                GatewayCommandDisposition.Rejected => "rejected",
                GatewayCommandDisposition.SubmissionDisabled => "submission_disabled",
                _ => throw new ArgumentOutOfRangeException(nameof(result))
            };
            return Task.FromResult(SubmissionReceipt ?? Receipt(
                claim.Command,
                state,
                claim.CommandVersion + 1,
                BrokerCommandLifecycleEvidence.Submission(result).Sha256,
                result.ObservedAtUtc));
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
                (ReconciliationClaim?.CommandVersion ?? dispatchClaim.CommandVersion) + 1,
                BrokerCommandLifecycleEvidence.Reconciliation(evidence).Sha256,
                evidence.ObservedAtUtc));
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
            BrokerCommandTestFixture.Now,
            false);

        public Exception? SendException { get; init; }

        public Action? DuringSend { get; init; }

        public Action? DuringReconcile { get; init; }

        public BrokerReconciliationSnapshot? ReconciliationSnapshot { get; init; }

        public int SendCalls { get; private set; }

        public int ReconcileCalls { get; private set; }

        public Task<GatewaySendResult> SendAsync(
            AuthorizedBrokerCommand command,
            CancellationToken cancellationToken)
        {
            SendCalls++;
            DuringSend?.Invoke();
            return SendException is null
                ? Task.FromResult(SendResult)
                : Task.FromException<GatewaySendResult>(SendException);
        }

        public Task<GatewayOperationResult<BrokerReconciliationSnapshot>> ReconcileAsync(
            IReadOnlyCollection<Guid> commandIds,
            CancellationToken cancellationToken)
        {
            ReconcileCalls++;
            DuringReconcile?.Invoke();
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
