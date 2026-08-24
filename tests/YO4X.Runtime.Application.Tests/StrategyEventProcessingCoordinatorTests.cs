using YO4X.Runtime.Application;
using YO4X.Strategy.Abstractions;

namespace YO4X.Runtime.Application.Tests;

public sealed class StrategyEventProcessingCoordinatorTests
{
    [Fact]
    public async Task ClaimFailureNeverEvaluatesOrCommits()
    {
        var store = new RecordingStrategyStore
        {
            ClaimException = new IOException("database unavailable")
        };
        var host = new RecordingStrategyHost();

        StrategyEventProcessingResult result = await Create(store, host).ProcessAsync(
            StrategyRuntimeFixture.Context(),
            StrategyRuntimeFixture.Input().Reference,
            TestContext.Current.CancellationToken);

        Assert.Equal(StrategyEventProcessingOutcome.ClaimRecoveryRequired, result.Outcome);
        Assert.Equal(0, host.Calls);
        Assert.Equal(0, store.CommitCalls);
    }

    [Fact]
    public async Task NoWorkNeverEvaluatesOrCommits()
    {
        var store = new RecordingStrategyStore
        {
            ClaimHandler = (_, _) => StrategyEventClaimResult.NoWork()
        };
        var host = new RecordingStrategyHost();

        StrategyEventProcessingResult result = await Create(store, host).ProcessAsync(
            StrategyRuntimeFixture.Context(),
            StrategyRuntimeFixture.Input().Reference,
            TestContext.Current.CancellationToken);

        Assert.Equal(StrategyEventProcessingOutcome.NoWork, result.Outcome);
        Assert.Equal(0, host.Calls);
        Assert.Equal(0, store.CommitCalls);
    }

    [Fact]
    public async Task StoreRehydratedClaimIsTheOnlyEvaluationInput()
    {
        StrategyEventInputEvidence persisted = StrategyRuntimeFixture.Input();
        var store = new RecordingStrategyStore();
        var host = new RecordingStrategyHost();

        StrategyEventProcessingResult result = await Create(store, host).ProcessAsync(
            StrategyRuntimeFixture.Context(),
            persisted.Reference,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsCommitted);
        StrategyHostEvaluationRequest request = Assert.Single(host.Requests);
        Assert.Equal(persisted.Envelope.Payload, request.Event);
        Assert.Equal(persisted.Reference.EventSha256, request.EventSha256);
        Assert.Equal(persisted.Reference.SnapshotSha256, request.SnapshotSha256);
    }

    [Fact]
    public async Task InvalidStoreClaimNeverCrossesHostBoundary()
    {
        StrategyEventInputEvidence input = StrategyRuntimeFixture.Input();
        var store = new RecordingStrategyStore
        {
            ClaimHandler = (reference, token) =>
            {
                ClaimedStrategyEvent valid = StrategyRuntimeFixture.Claim(reference, token);
                return StrategyEventClaimResult.Claimed(valid with
                {
                    EventJson = valid.EventJson + " "
                });
            }
        };
        var host = new RecordingStrategyHost();

        StrategyEventProcessingResult result = await Create(store, host).ProcessAsync(
            StrategyRuntimeFixture.Context(),
            input.Reference,
            TestContext.Current.CancellationToken);

        Assert.Equal(StrategyEventProcessingOutcome.InvalidClaim, result.Outcome);
        Assert.Equal(0, host.Calls);
        Assert.Equal(0, store.CommitCalls);
    }

    [Fact]
    public async Task OversizedStorePriorStateNeverCrossesHostBoundary()
    {
        StrategyEventInputEvidence input = StrategyRuntimeFixture.Input();
        var store = new RecordingStrategyStore
        {
            ClaimHandler = (reference, token) =>
            {
                ClaimedStrategyEvent valid = StrategyRuntimeFixture.Claim(reference, token);
                return StrategyEventClaimResult.Claimed(valid with
                {
                    PriorStateJson = new string(
                        's',
                        StrategyDurableEvidenceLimits.MaximumStateBytes + 1)
                });
            }
        };
        var host = new RecordingStrategyHost();

        StrategyEventProcessingResult result = await Create(store, host).ProcessAsync(
            StrategyRuntimeFixture.Context(),
            input.Reference,
            TestContext.Current.CancellationToken);

        Assert.Equal(StrategyEventProcessingOutcome.InvalidClaim, result.Outcome);
        Assert.Equal(0, host.Calls);
        Assert.Equal(0, store.CommitCalls);
    }

    [Fact]
    public async Task UndeclaredStoreEventSubtypeNeverEscapesClaimValidation()
    {
        StrategyEventInputEvidence input = StrategyRuntimeFixture.Input();
        var store = new RecordingStrategyStore
        {
            ClaimHandler = (reference, token) =>
            {
                ClaimedStrategyEvent valid = StrategyRuntimeFixture.Claim(reference, token);
                return StrategyEventClaimResult.Claimed(valid with
                {
                    Envelope = valid.Envelope with
                    {
                        Payload = new UndeclaredStrategyEvent(StrategyRuntimeFixture.Now)
                    }
                });
            }
        };
        var host = new RecordingStrategyHost();

        StrategyEventProcessingResult result = await Create(store, host).ProcessAsync(
            StrategyRuntimeFixture.Context(),
            input.Reference,
            TestContext.Current.CancellationToken);

        Assert.Equal(StrategyEventProcessingOutcome.InvalidClaim, result.Outcome);
        Assert.Equal(0, host.Calls);
        Assert.Equal(0, store.CommitCalls);
    }

    [Fact]
    public async Task StrategyHostExceptionNeverCommits()
    {
        var store = new RecordingStrategyStore();
        var host = new RecordingStrategyHost
        {
            Handler = (_, _) => Task.FromException<StrategyResult?>(
                new InvalidOperationException("fixture fault"))
        };

        StrategyEventProcessingResult result = await Create(store, host).ProcessAsync(
            StrategyRuntimeFixture.Context(),
            StrategyRuntimeFixture.Input().Reference,
            TestContext.Current.CancellationToken);

        Assert.Equal(StrategyEventProcessingOutcome.EvaluationFaulted, result.Outcome);
        Assert.Equal(0, store.CommitCalls);
    }

    [Fact]
    public async Task StrategyHostTimeoutNeverCommits()
    {
        var store = new RecordingStrategyStore();
        var host = new RecordingStrategyHost
        {
            Handler = (_, _) => Task.FromException<StrategyResult?>(
                new TimeoutException("isolated host deadline"))
        };

        StrategyEventProcessingResult result = await Create(store, host).ProcessAsync(
            StrategyRuntimeFixture.Context(),
            StrategyRuntimeFixture.Input().Reference,
            TestContext.Current.CancellationToken);

        Assert.Equal(StrategyEventProcessingOutcome.EvaluationTimedOut, result.Outcome);
        Assert.Equal(0, store.CommitCalls);
    }

    [Fact]
    public async Task EvaluationCancellationAfterClaimNeverCommits()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new RecordingStrategyStore();
        var host = new RecordingStrategyHost
        {
            Handler = (_, token) =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<StrategyResult?>(token);
            }
        };

        StrategyEventProcessingResult result = await Create(store, host).ProcessAsync(
            StrategyRuntimeFixture.Context(),
            StrategyRuntimeFixture.Input().Reference,
            cancellation.Token);

        Assert.Equal(StrategyEventProcessingOutcome.EvaluationCancelled, result.Outcome);
        Assert.Equal(0, store.CommitCalls);
    }

    [Fact]
    public async Task NonCooperativeHostLateFaultIsObservedAfterDeadline()
    {
        var completion = new TaskCompletionSource<StrategyResult?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new RecordingStrategyStore();
        var host = new RecordingStrategyHost
        {
            Handler = (request, token) =>
            {
                _ = token.Register(() => throw new InvalidOperationException(
                    "host cancellation callback fault"));
                Assert.NotNull(request);
                return completion.Task;
            }
        };
        StrategyEventProcessingCoordinator coordinator = Create(
            store,
            host,
            StrategyRuntimeFixture.Options(TimeSpan.FromMilliseconds(25)),
            TimeProvider.System);

        StrategyEventProcessingResult result = await coordinator.ProcessAsync(
            StrategyRuntimeFixture.Context(),
            StrategyRuntimeFixture.Input().Reference,
            TestContext.Current.CancellationToken);
        completion.SetException(new InvalidOperationException("late isolated-host fault"));
        await Task.Yield();

        Assert.Equal(StrategyEventProcessingOutcome.EvaluationTimedOut, result.Outcome);
        Assert.Equal(0, store.CommitCalls);
    }

    [Fact]
    public async Task BlockingCancellationCallbackCannotExtendEvaluationDeadline()
    {
        var callbackEntered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var evaluation = new TaskCompletionSource<StrategyResult?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new RecordingStrategyStore();
        var host = new RecordingStrategyHost
        {
            Handler = (request, token) =>
            {
                token.Register(() =>
                {
                    callbackEntered.TrySetResult(true);
                    releaseCallback.Task.GetAwaiter().GetResult();
                });
                Assert.NotNull(request);
                return evaluation.Task;
            }
        };
        StrategyEventProcessingCoordinator coordinator = Create(
            store,
            host,
            StrategyRuntimeFixture.Options(TimeSpan.FromMilliseconds(25)),
            TimeProvider.System);

        try
        {
            StrategyEventProcessingResult result = await coordinator.ProcessAsync(
                    StrategyRuntimeFixture.Context(),
                    StrategyRuntimeFixture.Input().Reference,
                    TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            await callbackEntered.Task.WaitAsync(
                TimeSpan.FromSeconds(1),
                TestContext.Current.CancellationToken);

            Assert.Equal(StrategyEventProcessingOutcome.EvaluationTimedOut, result.Outcome);
            Assert.Equal(0, store.CommitCalls);
        }
        finally
        {
            releaseCallback.TrySetResult(true);
            evaluation.TrySetCanceled(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task SynchronouslyBlockingHostClientInvocationCannotPinCoordinator()
    {
        using var gate = new ManualResetEventSlim(initialState: false);
        var store = new RecordingStrategyStore();
        var host = new RecordingStrategyHost
        {
            Handler = (_, _) =>
            {
                gate.Wait(CancellationToken.None);
                return Task.FromResult<StrategyResult?>(StrategyRuntimeFixture.ValidResult());
            }
        };
        StrategyEventProcessingCoordinator coordinator = Create(
            store,
            host,
            StrategyRuntimeFixture.Options(TimeSpan.FromMilliseconds(25)),
            TimeProvider.System);

        StrategyEventProcessingResult result;
        try
        {
            result = await coordinator.ProcessAsync(
                StrategyRuntimeFixture.Context(),
                StrategyRuntimeFixture.Input().Reference,
                TestContext.Current.CancellationToken);
        }
        finally
        {
            gate.Set();
        }

        Assert.Equal(StrategyEventProcessingOutcome.EvaluationTimedOut, result.Outcome);
        Assert.Equal(0, store.CommitCalls);
    }

    [Fact]
    public async Task StuckHostInvocationRetainsCapacityAndPreventsWorkerAccumulation()
    {
        using var releaseHost = new ManualResetEventSlim(initialState: false);
        var hostEntered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new RecordingStrategyStore();
        var host = new RecordingStrategyHost
        {
            Handler = (_, _) =>
            {
                hostEntered.TrySetResult(true);
                releaseHost.Wait(CancellationToken.None);
                return Task.FromResult<StrategyResult?>(StrategyRuntimeFixture.ValidResult());
            }
        };
        var options = new StrategyEventProcessingOptions
        {
            ResultBounds = StrategyResultBounds.Create(
                4096,
                8,
                8192,
                TimeSpan.FromMilliseconds(25)),
            MaximumConcurrentHostEvaluations = 1
        };
        StrategyEventProcessingCoordinator coordinator = Create(
            store,
            host,
            options,
            TimeProvider.System);
        StrategyEventReference firstReference = StrategyRuntimeFixture.Input().Reference;
        var secondReference = new StrategyEventReference(
            Guid.Parse("91000000-0000-0000-0000-000000000099"),
            firstReference.WorkerInstanceId,
            firstReference.Generation,
            checked(firstReference.Sequence + 1),
            Guid.Parse("92000000-0000-0000-0000-000000000099"),
            firstReference.EventKind,
            firstReference.EventContractVersion,
            firstReference.EventSha256,
            firstReference.SnapshotSequence,
            firstReference.SnapshotContractVersion,
            firstReference.SnapshotSha256);

        Task<StrategyEventProcessingResult> first = coordinator.ProcessAsync(
            StrategyRuntimeFixture.Context(),
            firstReference,
            TestContext.Current.CancellationToken);
        try
        {
            await hostEntered.Task.WaitAsync(
                TimeSpan.FromSeconds(1),
                TestContext.Current.CancellationToken);
            StrategyEventProcessingResult second = await coordinator.ProcessAsync(
                    StrategyRuntimeFixture.Context(),
                    secondReference,
                    TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            StrategyEventProcessingResult timedOut = await first.WaitAsync(
                TimeSpan.FromSeconds(1),
                TestContext.Current.CancellationToken);

            Assert.Equal(StrategyEventProcessingOutcome.EvaluationFaulted, second.Outcome);
            Assert.Equal("strategy_host_evaluation_capacity_exhausted", second.Code);
            Assert.Equal(StrategyEventProcessingOutcome.EvaluationTimedOut, timedOut.Outcome);
            Assert.Equal(1, host.Calls);
            Assert.Equal(1, store.ClaimCalls);
            Assert.Equal(0, store.CommitCalls);
        }
        finally
        {
            releaseHost.Set();
        }
    }

    [Theory]
    [InlineData(1_048_577, 256, 4_194_304)]
    [InlineData(1_048_576, 257, 4_194_304)]
    [InlineData(1_048_576, 256, 1)]
    [InlineData(1_048_576, 256, 4_194_305)]
    public void ProcessingOptionsRejectBoundsOutsideDurableStoreLimits(
        int maximumStateBytes,
        int maximumActionCount,
        int maximumCombinedActionBytes)
    {
        var options = new StrategyEventProcessingOptions
        {
            ResultBounds = StrategyResultBounds.Create(
                maximumStateBytes,
                maximumActionCount,
                maximumCombinedActionBytes,
                TimeSpan.FromSeconds(1))
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => Create(
            new RecordingStrategyStore(),
            new RecordingStrategyHost(),
            options));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(33)]
    public void ProcessingOptionsRejectUnsafeHostEvaluationConcurrency(int maximumConcurrency)
    {
        var options = new StrategyEventProcessingOptions
        {
            ResultBounds = StrategyRuntimeFixture.Options().ResultBounds,
            MaximumConcurrentHostEvaluations = maximumConcurrency
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => Create(
            new RecordingStrategyStore(),
            new RecordingStrategyHost(),
            options));
    }

    [Theory]
    [InlineData(0, 0, 2, 1)]
    [InlineData(1, -1, 2, 1)]
    [InlineData(1, 0, 1, 1)]
    [InlineData(1, 0, 2, 0)]
    [InlineData(1, 0, 2, -1)]
    public void ProcessingOptionsRejectMalformedDirectResultBounds(
        int maximumStateBytes,
        int maximumActionCount,
        int maximumCombinedActionBytes,
        long maximumWallTimeTicks)
    {
        var options = new StrategyEventProcessingOptions
        {
            ResultBounds = new StrategyResultBounds(
                maximumStateBytes,
                maximumActionCount,
                maximumCombinedActionBytes,
                TimeSpan.FromTicks(maximumWallTimeTicks))
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => Create(
            new RecordingStrategyStore(),
            new RecordingStrategyHost(),
            options));
    }

    [Fact]
    public void ProcessingOptionsAcceptExactDurableStoreLimits()
    {
        var options = new StrategyEventProcessingOptions
        {
            ResultBounds = StrategyResultBounds.Create(
                StrategyDurableEvidenceLimits.MaximumStateBytes,
                StrategyDurableEvidenceLimits.MaximumActionCount,
                StrategyDurableEvidenceLimits.MaximumCombinedActionBytes,
                TimeSpan.FromSeconds(1))
        };

        StrategyEventProcessingCoordinator coordinator = Create(
            new RecordingStrategyStore(),
            new RecordingStrategyHost(),
            options);

        Assert.NotNull(coordinator);
    }

    [Fact]
    public async Task InvalidStateVersionNeverCommits()
    {
        var store = new RecordingStrategyStore();
        var host = new RecordingStrategyHost
        {
            Handler = (_, _) => Task.FromResult<StrategyResult?>(
                new StrategyResult(StrategyState.FromJson(2, "{}")))
        };

        StrategyEventProcessingResult result = await Create(store, host).ProcessAsync(
            StrategyRuntimeFixture.Context(),
            StrategyRuntimeFixture.Input().Reference,
            TestContext.Current.CancellationToken);

        Assert.Equal(StrategyEventProcessingOutcome.InvalidResult, result.Outcome);
        Assert.Equal(StrategyResultValidationCode.InvalidStateVersion, result.ValidationCode);
        Assert.Equal(0, store.CommitCalls);
    }

    [Fact]
    public async Task MalformedNullActionIsRejectedWithoutEscapingCoordinator()
    {
        var store = new RecordingStrategyStore();
        var host = new RecordingStrategyHost
        {
            Handler = (_, _) => Task.FromResult<StrategyResult?>(
                new StrategyResult(
                    StrategyState.FromJson(1, "{}"),
                    [null!]))
        };

        StrategyEventProcessingResult result = await Create(store, host).ProcessAsync(
            StrategyRuntimeFixture.Context(),
            StrategyRuntimeFixture.Input().Reference,
            TestContext.Current.CancellationToken);

        Assert.Equal(StrategyEventProcessingOutcome.InvalidResult, result.Outcome);
        Assert.Equal(StrategyResultValidationCode.StrategyFaulted, result.ValidationCode);
        Assert.Equal(0, store.CommitCalls);
    }

    [Fact]
    public async Task UndeclaredActionSubtypeIsRejectedWithoutEscapingCoordinator()
    {
        var store = new RecordingStrategyStore();
        var host = new RecordingStrategyHost
        {
            Handler = (_, _) => Task.FromResult<StrategyResult?>(
                new StrategyResult(
                    StrategyState.FromJson(1, "{}"),
                    [new UndeclaredRequestedAction()]))
        };

        StrategyEventProcessingResult result = await Create(store, host).ProcessAsync(
            StrategyRuntimeFixture.Context(),
            StrategyRuntimeFixture.Input().Reference,
            TestContext.Current.CancellationToken);

        Assert.Equal(StrategyEventProcessingOutcome.InvalidResult, result.Outcome);
        Assert.Equal(StrategyResultValidationCode.StrategyFaulted, result.ValidationCode);
        Assert.Equal("strategy_result_serialization_invalid", result.Code);
        Assert.Equal(0, store.CommitCalls);
    }

    [Fact]
    public async Task DuplicateActionIdentityNeverCommits()
    {
        Guid duplicate = Guid.Parse("83000000-0000-0000-0000-000000000001");
        var store = new RecordingStrategyStore();
        var host = new RecordingStrategyHost
        {
            Handler = (_, _) => Task.FromResult<StrategyResult?>(
                StrategyRuntimeFixture.ValidResult(
                    StrategyRuntimeFixture.Place(duplicate, "entry-a"),
                    StrategyRuntimeFixture.Place(duplicate, "entry-b")))
        };

        StrategyEventProcessingResult result = await Create(store, host).ProcessAsync(
            StrategyRuntimeFixture.Context(),
            StrategyRuntimeFixture.Input().Reference,
            TestContext.Current.CancellationToken);

        Assert.Equal(StrategyEventProcessingOutcome.InvalidResult, result.Outcome);
        Assert.Equal(StrategyResultValidationCode.DuplicateActionId, result.ValidationCode);
        Assert.Equal(0, store.CommitCalls);
    }

    [Fact]
    public async Task CommitPreservesActionOrderIdentityAndRiskOnlyOutbox()
    {
        PlaceOrderAction place = StrategyRuntimeFixture.Place();
        ClosePositionAction close = StrategyRuntimeFixture.Close();
        var store = new RecordingStrategyStore();
        var host = new RecordingStrategyHost
        {
            Handler = (_, _) => Task.FromResult<StrategyResult?>(
                StrategyRuntimeFixture.ValidResult(place, close))
        };

        StrategyEventProcessingResult result = await Create(store, host).ProcessAsync(
            StrategyRuntimeFixture.Context(),
            StrategyRuntimeFixture.Input().Reference,
            TestContext.Current.CancellationToken);

        Assert.Equal(StrategyEventProcessingOutcome.Committed, result.Outcome);
        StrategyEventCommitRequest request = Assert.Single(store.CommitRequests);
        Assert.Equal([0, 1], request.Evidence.Document.Actions.Select(value => value.Ordinal));
        Assert.Equal(
            [place.ActionId, close.ActionId],
            request.Evidence.Document.Actions.Select(value => value.ActionId));
        Assert.Equal(
            [place.IdempotencyKey, close.IdempotencyKey],
            request.Evidence.Document.Actions.Select(value => value.IdempotencyKey));
        Assert.All(
            request.Evidence.Document.Actions,
            action => Assert.Equal(
                "strategy.action.risk-evaluation-requested.v1",
                action.OutboxTopic));
        Assert.DoesNotContain(
            typeof(StrategyEventProcessingCoordinator).Assembly.GetReferencedAssemblies(),
            reference => reference.Name is "YO4X.Trading.Abstractions" or "YO4X.Trading.Mt5"
                || string.Equals(reference.Name, "Npgsql", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BehindLocalClockClampsCommitTimeToDatabaseAuthority()
    {
        var store = new RecordingStrategyStore();
        var host = new RecordingStrategyHost();

        StrategyEventProcessingResult result = await Create(
                store,
                host,
                timeProvider: new FixedRuntimeTimeProvider(
                    StrategyRuntimeFixture.Now.AddMinutes(-1)))
            .ProcessAsync(
                StrategyRuntimeFixture.Context(),
                StrategyRuntimeFixture.Input().Reference,
                TestContext.Current.CancellationToken);

        Assert.Equal(StrategyEventProcessingOutcome.Committed, result.Outcome);
        Assert.Equal(
            StrategyRuntimeFixture.Now,
            Assert.Single(store.CommitRequests).Evidence.Document.PreparedAtUtc);
    }

    [Fact]
    public async Task LocalClockAtClaimExpiryNeverAttemptsCommit()
    {
        var store = new RecordingStrategyStore();
        var host = new RecordingStrategyHost();

        StrategyEventProcessingResult result = await Create(
                store,
                host,
                timeProvider: new FixedRuntimeTimeProvider(
                    StrategyRuntimeFixture.Now.AddSeconds(5)))
            .ProcessAsync(
                StrategyRuntimeFixture.Context(),
                StrategyRuntimeFixture.Input().Reference,
                TestContext.Current.CancellationToken);

        Assert.Equal(StrategyEventProcessingOutcome.EvaluationTimedOut, result.Outcome);
        Assert.Equal("strategy_event_claim_expired_before_commit", result.Code);
        Assert.Equal(0, store.CommitCalls);
    }

    [Fact]
    public async Task FailureBeforeAtomicCommitExhaustsExactRetriesWithoutSuccessReceipt()
    {
        var store = new RecordingStrategyStore
        {
            CommitHandler = (_, _) => throw new IOException("commit rejected before write")
        };
        var host = new RecordingStrategyHost();

        StrategyEventProcessingResult result = await Create(store, host).ProcessAsync(
            StrategyRuntimeFixture.Context(),
            StrategyRuntimeFixture.Input().Reference,
            TestContext.Current.CancellationToken);

        Assert.Equal(StrategyEventProcessingOutcome.CommitRecoveryRequired, result.Outcome);
        Assert.Equal(2, store.CommitCalls);
        Assert.Null(store.DurableReceipt);
        Assert.Equal(
            store.CommitRequests[0].Evidence.Sha256,
            store.CommitRequests[1].Evidence.Sha256);
    }

    [Fact]
    public async Task LostCommitAcknowledgementRecoversByExactCommitReplay()
    {
        var store = new RecordingStrategyStore();
        store.CommitHandler = (request, call) =>
        {
            if (call == 1)
            {
                store.DurableReceipt = new StrategyEventCommitReceipt(
                    request.Evidence,
                    StrategyRuntimeFixture.Now,
                    false);
                throw new IOException("acknowledgement lost after commit");
            }

            Assert.NotNull(store.DurableReceipt);
            Assert.Equal(store.DurableReceipt.Evidence.Sha256, request.Evidence.Sha256);
            return new StrategyEventCommitReceipt(
                request.Evidence,
                StrategyRuntimeFixture.Now,
                true);
        };
        var host = new RecordingStrategyHost();

        StrategyEventProcessingResult result = await Create(store, host).ProcessAsync(
            StrategyRuntimeFixture.Context(),
            StrategyRuntimeFixture.Input().Reference,
            TestContext.Current.CancellationToken);

        Assert.Equal(StrategyEventProcessingOutcome.AlreadyCommitted, result.Outcome);
        Assert.Equal(1, host.Calls);
        Assert.Equal(2, store.CommitCalls);
        Assert.Same(store.CommitRequests[0], store.CommitRequests[1]);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(50_000_000)]
    public async Task CommitReceiptOutsideClaimWindowIsRejected(long recordedAtOffsetTicks)
    {
        var store = new RecordingStrategyStore
        {
            CommitHandler = (request, _) => new StrategyEventCommitReceipt(
                request.Evidence,
                StrategyRuntimeFixture.Now.AddTicks(recordedAtOffsetTicks),
                false)
        };
        var host = new RecordingStrategyHost();

        StrategyEventProcessingResult result = await Create(store, host).ProcessAsync(
            StrategyRuntimeFixture.Context(),
            StrategyRuntimeFixture.Input().Reference,
            TestContext.Current.CancellationToken);

        Assert.Equal(StrategyEventProcessingOutcome.InvalidCommitReceipt, result.Outcome);
        Assert.Equal(1, store.CommitCalls);
    }

    [Fact]
    public async Task CommitReceiptRejectsPreparedTimeBeyondDatabaseClockTolerance()
    {
        var store = new RecordingStrategyStore();
        var host = new RecordingStrategyHost();

        StrategyEventProcessingResult result = await Create(
                store,
                host,
                timeProvider: new FixedRuntimeTimeProvider(
                    StrategyRuntimeFixture.Now.AddSeconds(1).AddMicroseconds(1)))
            .ProcessAsync(
                StrategyRuntimeFixture.Context(),
                StrategyRuntimeFixture.Input().Reference,
                TestContext.Current.CancellationToken);

        Assert.Equal(StrategyEventProcessingOutcome.InvalidCommitReceipt, result.Outcome);
        Assert.Equal(1, store.CommitCalls);
    }

    [Fact]
    public async Task AlreadyCommittedClaimNeverReevaluatesEvent()
    {
        var store = new RecordingStrategyStore();
        var host = new RecordingStrategyHost();
        StrategyEventProcessingCoordinator coordinator = Create(store, host);
        StrategyEventReference reference = StrategyRuntimeFixture.Input().Reference;

        StrategyEventProcessingResult first = await coordinator.ProcessAsync(
            StrategyRuntimeFixture.Context(),
            reference,
            TestContext.Current.CancellationToken);
        Assert.Equal(StrategyEventProcessingOutcome.Committed, first.Outcome);
        StrategyEventCommitReceipt committed = Assert.IsType<StrategyEventCommitReceipt>(
            first.Receipt);
        store.ClaimHandler = (_, _) => StrategyEventClaimResult.AlreadyCommitted(
            committed with { Replayed = true });

        StrategyEventProcessingResult replay = await coordinator.ProcessAsync(
            StrategyRuntimeFixture.Context(),
            reference,
            TestContext.Current.CancellationToken);

        Assert.Equal(StrategyEventProcessingOutcome.AlreadyCommitted, replay.Outcome);
        Assert.Equal(1, host.Calls);
        Assert.Equal(1, store.CommitCalls);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(50_000_000)]
    public async Task ReplayedCommitOutsideEvidenceClaimWindowIsRejected(
        long recordedAtOffsetTicks)
    {
        var store = new RecordingStrategyStore();
        var host = new RecordingStrategyHost();
        StrategyEventProcessingCoordinator coordinator = Create(store, host);
        StrategyEventReference reference = StrategyRuntimeFixture.Input().Reference;
        StrategyEventProcessingResult first = await coordinator.ProcessAsync(
            StrategyRuntimeFixture.Context(),
            reference,
            TestContext.Current.CancellationToken);
        StrategyEventCommitReceipt committed = Assert.IsType<StrategyEventCommitReceipt>(
            first.Receipt);
        store.ClaimHandler = (_, _) => StrategyEventClaimResult.AlreadyCommitted(
            committed with
            {
                RecordedAtUtc = StrategyRuntimeFixture.Now.AddTicks(recordedAtOffsetTicks),
                Replayed = true
            });

        StrategyEventProcessingResult replay = await coordinator.ProcessAsync(
            StrategyRuntimeFixture.Context(),
            reference,
            TestContext.Current.CancellationToken);

        Assert.Equal(StrategyEventProcessingOutcome.InvalidCommitReceipt, replay.Outcome);
        Assert.Equal(1, host.Calls);
        Assert.Equal(1, store.CommitCalls);
    }

    private static StrategyEventProcessingCoordinator Create(
        RecordingStrategyStore store,
        RecordingStrategyHost host,
        StrategyEventProcessingOptions? options = null,
        TimeProvider? timeProvider = null) => new(
        store,
        host,
        options ?? StrategyRuntimeFixture.Options(),
        timeProvider ?? new FixedRuntimeTimeProvider(StrategyRuntimeFixture.Now),
        new SequenceStrategyIdentifiers());

    private sealed record UndeclaredStrategyEvent : StrategyEvent
    {
        public UndeclaredStrategyEvent(DateTimeOffset occurredAtUtc)
            : base(occurredAtUtc)
        {
        }

        public override StrategyEventKind Kind => StrategyEventKind.NewTick;
    }

    private sealed record UndeclaredRequestedAction : RequestedAction
    {
        public UndeclaredRequestedAction()
            : base(
                Guid.Parse("83000000-0000-0000-0000-000000000099"),
                "undeclared-action",
                "EURUSD",
                "malformed_host_result",
                42,
                RequestedExposureHint.Increase)
        {
        }

        public override RequestedActionKind Kind => RequestedActionKind.PlaceOrder;
    }
}
