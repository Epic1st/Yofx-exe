using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.Conversion.Worker;
using YO4X.Persistence.Postgres;
using YO4X.Risk;
using YO4X.Runtime.Contracts;
using YO4X.RuntimeControl.Postgres;
using YO4X.StrategyGovernance;
using YO4X.Tenancy;
using YO4X.Trading.Abstractions;
using YO4X.Trading.Postgres;

namespace YO4X.Postgres.IntegrationTests;

[Collection(PostgresTestGroup.Name)]
public sealed partial class BrokerCommandAuthorizationPostgresTests(PostgresContainerFixture postgres)
{
    private readonly PostgresContainerFixture postgres = postgres;

    [PostgresFact]
    public async Task SignedVerificationCapabilityIsRequiredForPromotionAndStart()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        VerificationFixture fixture = await SeedVerifiedStrategyAsync(database);

        await AssertRawAdminPromotionRejectedAsync(database, fixture);
        await AssertWrongVerificationMetadataRejectedAsync(database, fixture);
        await AssertVerifierRejectsPoisonedEvidenceWithoutSideEffectsAsync(
            database,
            fixture);
        await RecordVerificationAsync(database, fixture);
        await AssertWrongBindingPromotionRejectedAsync(database, fixture);
        await PromoteAsync(database, fixture);
        await SeedRuntimeAuthorityAsync(database, fixture, desiredState: "ready");

        await SuspendStrategyAsync(database, fixture);
        await using TenantPostgresTransaction transaction =
            await database.Application.BeginTenantTransactionAsync(fixture.UserContext);
        await using NpgsqlCommand start = transaction.CreateCommand(
            """
            update operations.deployments
            set desired_state = 'starting', fence_generation = 1,
                row_version = row_version + 1, updated_at = clock_timestamp()
            where tenant_id = @tenant_id and id = @deployment_id
            """);
        AddUuid(start, "tenant_id", fixture.TenantId);
        AddUuid(start, "deployment_id", fixture.DeploymentId);
        PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
            async () => await start.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, rejected.SqlState);
    }

    [PostgresFact]
    public async Task ProofOnlyAuthorizationReplayIsExactAndIdempotent()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        VerificationFixture fixture = await SeedVerifiedStrategyAsync(database);
        await RecordVerificationAsync(database, fixture);
        await PromoteAsync(database, fixture);
        LeaseFixture leaseFixture = await SeedRuntimeAuthorityAsync(
            database,
            fixture,
            desiredState: "running");
        await GrantProofOnlyAuthorizationForDisposableTestAsync(database);
        PostgresBrokerCommandStore store = CreateStore(database, leaseFixture);
        BrokerCommandAuthorizationRequest request = CreateAuthorizationRequest(
            fixture,
            leaseFixture.Lease,
            BrokerCommandAction.ModifyProtection);
        var context = new TenantExecutionContext(
            fixture.TenantId,
            fixture.StrategyHostWorkloadId,
            request.Command.CommandId);

        DomainException unavailable = await Assert.ThrowsAsync<DomainException>(
            () => store.AuthorizeAsync(context, request));
        Assert.Equal("BROKER_COMMAND_RISK_AUTHORITY_UNAVAILABLE", unavailable.Code);
        await AssertTradeAuthorizerRejectsPoisonedEvidenceWithoutSideEffectsAsync(
            database,
            fixture,
            leaseFixture,
            context,
            request);
        request = CreateAuthorizationRequest(
            fixture,
            leaseFixture.Lease,
            BrokerCommandAction.ModifyProtection);
        context = new TenantExecutionContext(
            fixture.TenantId,
            fixture.StrategyHostWorkloadId,
            request.Command.CommandId);
        BrokerCommandAuthorizationReceipt authorization =
            await store.AuthorizeProofOnlyForIntegrationAsync(context, request);
        BrokerCommandAuthorizationReceipt replay =
            await store.AuthorizeProofOnlyForIntegrationAsync(context, request);

        Assert.False(authorization.Replayed);
        Assert.True(replay.Replayed);
        Assert.Equal(authorization.AuthorizationSha256, replay.AuthorizationSha256);
        Assert.Equal(authorization.CommandVersion, replay.CommandVersion);
    }

    [PostgresFact]
    public async Task DurableAuthorizationDispatchAndReconciliationAreAtomicAndFailClosed()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        VerificationFixture fixture = await SeedVerifiedStrategyAsync(database);
        await RecordVerificationAsync(database, fixture);
        await PromoteAsync(database, fixture);
        LeaseFixture leaseFixture = await SeedRuntimeAuthorityAsync(
            database,
            fixture,
            desiredState: "running");
        SignedExecutionLease lease = leaseFixture.Lease;
        await GrantProofOnlyAuthorizationForDisposableTestAsync(database);

        BrokerCommandAuthorizationRequest request = CreateAuthorizationRequest(fixture, lease);
        string leaseDigest = ExecutionLeaseEnvelopeDigest.Sha256(lease);
        var store = new PostgresBrokerCommandStore(
            database.TradeAuthorizer,
            database.GatewayRuntime,
            new P256ExecutionLeaseTrustVerifier(
                new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal)
                {
                    [lease.SigningKeyId] = leaseFixture.SubjectPublicKeyInfo
                }));
        var lifecycle = new PostgresBrokerCommandLifecycleStore(store);
        var authorizerContext = new TenantExecutionContext(
            fixture.TenantId,
            fixture.StrategyHostWorkloadId,
            request.Command.CommandId);
        var gatewayContext = new TenantExecutionContext(
            fixture.TenantId,
            fixture.GatewayHostWorkloadId,
            request.Command.CommandId);
        Guid dispatchClaimToken = Guid.CreateVersion7();

        BrokerCommandAuthorizationReceipt authorization =
            await store.AuthorizeProofOnlyForIntegrationAsync(
            authorizerContext,
            request);
        Assert.False(authorization.Replayed);
        var dispatchReference = new YO4X.Trading.Application.BrokerCommandReference(
            request.Command.CommandId,
            authorization.AuthorizationSha256,
            leaseDigest);
        YO4X.Trading.Application.BrokerCommandDispatchClaim claim =
            await lifecycle.ClaimForDispatchAsync(
                gatewayContext,
                dispatchReference,
                dispatchClaimToken,
                Guid.CreateVersion7());
        Assert.False(claim.Replayed);

        YO4X.Trading.Application.BrokerCommandDispatchClaim claimReplay =
            await lifecycle.ClaimForDispatchAsync(
                gatewayContext,
                dispatchReference,
                dispatchClaimToken,
                Guid.CreateVersion7());
        Assert.True(claimReplay.Replayed);

        var unknown = new GatewaySendResult(
            GatewayCommandDisposition.Unknown,
            "transport_outcome_unknown",
            "request-1",
            "order-1",
            null,
            UtcNow(),
            false);
        YO4X.Trading.Application.BrokerCommandLifecycleReceipt submission =
            await lifecycle.RecordSubmissionAsync(
                gatewayContext,
                claim,
                unknown,
                Guid.CreateVersion7());
        Assert.Equal("unknown", submission.State);
        Assert.Equal(
            YO4X.Trading.Application.BrokerCommandLifecycleEvidence
                .Submission(unknown).Sha256,
            submission.EvidenceSha256);

        await RenewExecutionLeaseAsync(database, fixture, lease);
        store = new PostgresBrokerCommandStore(
            database.TradeAuthorizer,
            database.GatewayRuntime,
            new P256ExecutionLeaseTrustVerifier(
                new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal)
                {
                    [lease.SigningKeyId] = leaseFixture.SubjectPublicKeyInfo
                }));
        lifecycle = new PostgresBrokerCommandLifecycleStore(store);

        Guid reconciliationClaim = Guid.CreateVersion7();
        YO4X.Trading.Application.BrokerCommandReconciliationClaim reconciliation =
            await lifecycle.BeginReconciliationAsync(
                gatewayContext,
                request.Command.CommandId,
                authorization.AuthorizationSha256,
                reconciliationClaim,
                Guid.CreateVersion7());
        Assert.True(reconciliation.ClaimExpiresAtUtc > reconciliation.StartedAtUtc);

        DateTimeOffset unavailableAt = UtcNow();
        var unavailableObservation =
            new YO4X.Trading.Application.BrokerCommandReconciliationObservation(
                null,
                Digest("reconciliation-attempt-unavailable"),
                reconciliation.QueryWindowStartUtc,
                unavailableAt,
                null);
        YO4X.Trading.Application.ValidatedBrokerCommandReconciliation inconclusive =
            YO4X.Trading.Application.BrokerCommandReconciliationValidator.Validate(
                reconciliation,
                unavailableObservation,
                unavailableAt);
        Assert.False(inconclusive.IsConclusive);
        Assert.Null(inconclusive.SourceSequence);
        Assert.Null(inconclusive.Snapshot);
        YO4X.Trading.Application.BrokerCommandLifecycleReceipt inconclusiveReceipt =
            await lifecycle.CompleteReconciliationAsync(
                gatewayContext,
                reconciliationClaim,
                Guid.CreateVersion7(),
                inconclusive,
                Guid.CreateVersion7());
        Assert.Equal("unknown", inconclusiveReceipt.State);
        Assert.Equal(
            YO4X.Trading.Application.BrokerCommandLifecycleEvidence
                .Reconciliation(inconclusive).Sha256,
            inconclusiveReceipt.EvidenceSha256);

        reconciliationClaim = Guid.CreateVersion7();
        reconciliation = await lifecycle.BeginReconciliationAsync(
            gatewayContext,
            request.Command.CommandId,
            authorization.AuthorizationSha256,
            reconciliationClaim,
            Guid.CreateVersion7());
        Assert.Equal(2, reconciliation.Attempt);
        Assert.Equal(
            unavailableObservation.WindowStartUtc,
            reconciliation.QueryWindowStartUtc);

        DateTimeOffset snapshotCompletedAt = reconciliation.StartedAtUtc;
        DateTimeOffset receivedAt = snapshotCompletedAt.AddMilliseconds(1);
        var snapshot = new BrokerReconciliationSnapshot(
            1,
            request.Exposure.SourceSequence + 1,
            fixture.BrokerAccountId,
            fixture.DeploymentId,
            request.Command.Generation,
            fixture.GatewayArtifactId,
            request.Provenance.GatewayArtifactSha256,
            reconciliation.QueryWindowStartUtc,
            snapshotCompletedAt,
            true,
            true,
            request.Exposure.Account with
            {
                Sequence = request.Exposure.SourceSequence + 1,
                ObservedAtUtc = snapshotCompletedAt
            },
            [],
            [
                new BrokerOrderSnapshot(
                    "order-1",
                    request.Command.Symbol,
                    request.Command.Side,
                    request.Command.OrderType,
                    request.Command.Volume,
                    0m,
                    request.Command.RequestedPrice,
                    request.Command.StopLoss,
                    request.Command.TakeProfit,
                    "filled",
                    request.Command.OwnershipTag,
                    snapshotCompletedAt)
            ],
            [
                new BrokerDealSnapshot(
                    "deal-1",
                    "order-1",
                    request.Command.Symbol,
                    request.Command.Side,
                    request.Command.Volume,
                    1.1m,
                    snapshotCompletedAt)
            ],
            [
                new BrokerCommandReconciliation(
                    request.Command.CommandId,
                    BrokerReconciliationMatch.Filled,
                    "deal_history_match",
                    "order-1",
                    "deal-1",
                    snapshotCompletedAt)
            ],
            snapshotCompletedAt);
        var sourceDocument = new YO4X.Trading.Application
            .BrokerCommandReconciliationValidator.BrokerReconciliationSourceDocument(
                snapshot.SourceSequence,
                snapshot.QueryWindowStartUtc,
                snapshot.QueryWindowEndUtc,
                snapshot);
        var observation = new YO4X.Trading.Application.BrokerCommandReconciliationObservation(
            snapshot.SourceSequence,
            CanonicalJson.Sha256(sourceDocument),
            snapshot.QueryWindowStartUtc,
            snapshot.QueryWindowEndUtc,
            snapshot);
        var attemptedTerminal = new BrokerCommandReconciliationEvidenceDocument(
            request.Command.CommandId,
            reconciliation.Command.AuthorizationSha256,
            reconciliation.ScopeSha256,
            fixture.BrokerAccountId,
            fixture.DeploymentId,
            request.Command.Generation,
            request.Command.TargetKind,
            request.Command.TargetBrokerId,
            request.Command.OwnershipTag,
            snapshot.SourceSequence,
            snapshot.QueryWindowStartUtc,
            snapshot.QueryWindowEndUtc,
            "filled",
            "attempted_terminal_assertion",
            observation.SourceEvidenceSha256,
            "order-1",
            "deal-1",
            snapshotCompletedAt,
            snapshot);
        PostgresException terminalRejected = await Assert.ThrowsAsync<PostgresException>(
            () => store.CompleteReconciliationAsync(
                gatewayContext,
                authorization.AuthorizationSha256,
                reconciliationClaim,
                Guid.CreateVersion7(),
                attemptedTerminal,
                Guid.CreateVersion7()));
        Assert.Equal(PostgresErrorCodes.InvalidParameterValue, terminalRejected.SqlState);

        YO4X.Trading.Application.ValidatedBrokerCommandReconciliation evidence =
            YO4X.Trading.Application.BrokerCommandReconciliationValidator.Validate(
                reconciliation,
                observation,
                receivedAt);
        Assert.False(evidence.IsConclusive);
        Assert.Equal(BrokerReconciliationMatch.Inconclusive, evidence.Match);
        Assert.Equal(
            "broker_reconciliation_terminal_authority_unavailable",
            evidence.ReasonCode);
        Assert.Equal(snapshotCompletedAt, evidence.WindowEndUtc);
        Assert.Equal(snapshotCompletedAt, evidence.ObservedAtUtc);
        YO4X.Trading.Application.BrokerCommandLifecycleReceipt completed =
            await lifecycle.CompleteReconciliationAsync(
                gatewayContext,
                reconciliationClaim,
                Guid.CreateVersion7(),
                evidence,
                Guid.CreateVersion7());
        Assert.Equal("unknown", completed.State);
        Assert.Equal(
            YO4X.Trading.Application.BrokerCommandLifecycleEvidence
                .Reconciliation(evidence).Sha256,
            completed.EvidenceSha256);

        await AssertDurableRowsAsync(database, fixture, request.Command.CommandId);
        await AssertRawAuthorizerTableReadRejectedAsync(database, authorizerContext);

        await SuspendStrategyAsync(database, fixture);
        BrokerCommandAuthorizationRequest afterSuspension = CreateAuthorizationRequest(fixture, lease);
        var suspendedContext = new TenantExecutionContext(
            fixture.TenantId,
            fixture.StrategyHostWorkloadId,
            afterSuspension.Command.CommandId);
        await Assert.ThrowsAnyAsync<Exception>(
            () => store.AuthorizeProofOnlyForIntegrationAsync(
                suspendedContext,
                afterSuspension));
    }

    [PostgresFact]
    public async Task ProductionRolesCannotAcquireOrDispatchAcrossCapabilityBoundariesAfterReapply()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        await using NpgsqlConnection connection =
            await database.Administrator.OpenConnectionAsync();

        await using (var staleGrant = new NpgsqlCommand(
            """
            do $body$
            declare
                authorize_function regprocedure;
                claim_function regprocedure;
            begin
                select function.oid::regprocedure into strict authorize_function
                from pg_proc as function
                join pg_namespace as namespace on namespace.oid = function.pronamespace
                where namespace.nspname = 'control'
                  and function.proname = 'authorize_broker_command';
                select function.oid::regprocedure into strict claim_function
                from pg_proc as function
                join pg_namespace as namespace on namespace.oid = function.pronamespace
                where namespace.nspname = 'control'
                  and function.proname = 'claim_authorized_broker_command';
                execute format(
                    'grant execute on function %s to yo4x_trade_authorizer, yo4x_gateway_runtime',
                    authorize_function);
                execute format(
                    'grant execute on function %s to yo4x_trade_authorizer',
                    claim_function);
            end
            $body$;
            """,
            connection))
        {
            await staleGrant.ExecuteNonQueryAsync();
        }

        await PostgresContainerFixture.ApplyLeastPrivilegeRoleScriptAsync(connection);
        await using var verify = new NpgsqlCommand(
            """
            select
                has_function_privilege(
                    'yo4x_trade_authorizer', authorize.oid, 'EXECUTE'),
                has_function_privilege(
                    'yo4x_gateway_runtime', authorize.oid, 'EXECUTE'),
                has_function_privilege(
                    'yo4x_trade_authorizer', claim.oid, 'EXECUTE'),
                has_function_privilege(
                    'yo4x_gateway_runtime', claim.oid, 'EXECUTE')
            from pg_proc as authorize
            join pg_namespace as authorize_namespace
              on authorize_namespace.oid = authorize.pronamespace
            cross join pg_proc as claim
            join pg_namespace as claim_namespace
              on claim_namespace.oid = claim.pronamespace
            where authorize_namespace.nspname = 'control'
              and authorize.proname = 'authorize_broker_command'
              and claim_namespace.nspname = 'control'
              and claim.proname = 'claim_authorized_broker_command'
            """,
            connection);
        await using NpgsqlDataReader reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.False(reader.GetBoolean(0));
        Assert.False(reader.GetBoolean(1));
        Assert.False(reader.GetBoolean(2));
        Assert.True(reader.GetBoolean(3));
        Assert.False(await reader.ReadAsync());
    }

    [PostgresFact]
    public async Task GatewayLifecycleRejectsNullCapabilitiesAndMalformedEvidenceEnvelopes()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        VerificationFixture fixture = await SeedVerifiedStrategyAsync(database);
        await RecordVerificationAsync(database, fixture);
        await PromoteAsync(database, fixture);
        LeaseFixture leaseFixture = await SeedRuntimeAuthorityAsync(
            database,
            fixture,
            desiredState: "running");
        await GrantProofOnlyAuthorizationForDisposableTestAsync(database);
        PostgresBrokerCommandStore store = CreateStore(database, leaseFixture);
        var lifecycle = new PostgresBrokerCommandLifecycleStore(store);
        BrokerCommandAuthorizationRequest request = CreateAuthorizationRequest(
            fixture,
            leaseFixture.Lease,
            BrokerCommandAction.ModifyProtection);
        var authorizerContext = new TenantExecutionContext(
            fixture.TenantId,
            fixture.StrategyHostWorkloadId,
            request.Command.CommandId);
        BrokerCommandAuthorizationReceipt authorization =
            await store.AuthorizeProofOnlyForIntegrationAsync(authorizerContext, request);
        var gatewayContext = new TenantExecutionContext(
            fixture.TenantId,
            fixture.GatewayHostWorkloadId,
            request.Command.CommandId);
        string leaseDigest = ExecutionLeaseEnvelopeDigest.Sha256(leaseFixture.Lease);

        const string ClaimSql =
            """
            select count(*)
            from control.claim_authorized_broker_command(
                @command_id, @authorization_sha256, @lease_sha256,
                @claim_token, @audit_event_id)
            """;
        Guid dispatchClaimToken = Guid.CreateVersion7();
        Assert.Equal(
            0,
            await ExecuteGatewayCountAsync(database, gatewayContext, ClaimSql, command =>
            {
                AddUuid(command, "command_id", request.Command.CommandId);
                AddNullableText(command, "authorization_sha256", null);
                AddNullableText(command, "lease_sha256", leaseDigest);
                AddUuid(command, "claim_token", dispatchClaimToken);
                AddUuid(command, "audit_event_id", Guid.CreateVersion7());
            }));
        Assert.Equal(
            0,
            await ExecuteGatewayCountAsync(database, gatewayContext, ClaimSql, command =>
            {
                AddUuid(command, "command_id", request.Command.CommandId);
                AddNullableText(command, "authorization_sha256", authorization.AuthorizationSha256);
                AddNullableText(command, "lease_sha256", null);
                AddUuid(command, "claim_token", dispatchClaimToken);
                AddUuid(command, "audit_event_id", Guid.CreateVersion7());
            }));

        var reference = new YO4X.Trading.Application.BrokerCommandReference(
            request.Command.CommandId,
            authorization.AuthorizationSha256,
            leaseDigest);
        YO4X.Trading.Application.BrokerCommandDispatchClaim dispatchClaim =
            await lifecycle.ClaimForDispatchAsync(
                gatewayContext,
                reference,
                dispatchClaimToken,
                Guid.CreateVersion7());
        DateTimeOffset submittedAt = UtcNow().AddTicks(7);
        Assert.NotEqual(
            0,
            submittedAt.Ticks % TimeSpan.TicksPerMicrosecond);
        GatewaySendResult submission = YO4X.Trading.Application
            .BrokerCommandLifecycleEvidence.NormalizeSubmission(new GatewaySendResult(
            GatewayCommandDisposition.Unknown,
            "transport_outcome_unknown",
            "request-null-shape-proof",
            null,
            null,
            submittedAt,
            false));
        YO4X.Trading.Application.BrokerCommandCanonicalEvidence expectedSubmissionEvidence =
            YO4X.Trading.Application.BrokerCommandLifecycleEvidence.Submission(submission);
        byte[] submissionContent = Encoding.UTF8.GetBytes(CanonicalJson.Serialize(
            new BrokerGatewaySubmissionDocument(
                "unknown",
                submission.Code,
                submission.BrokerRequestId,
                submission.OrderId,
                submission.DealId,
                submission.ObservedAtUtc,
                submission.PreInvocationNotSentProven)));
        const string RecordSql =
            """
            select count(*)
            from control.record_broker_command_submission(
                @command_id, @authorization_sha256, @claim_token, @disposition,
                @pre_invocation_not_sent_proven, @result_code, @broker_request_id,
                @broker_order_id, @broker_deal_id, @result_content, @observed_at,
                @audit_event_id)
            """;
        Assert.Equal(
            0,
            await ExecuteGatewayCountAsync(database, gatewayContext, RecordSql, command =>
                BindRawSubmission(
                    command,
                    request.Command.CommandId,
                    null,
                    dispatchClaimToken,
                    submission,
                    submissionContent)));
        byte[] replacedSubmissionContent = Encoding.UTF8.GetBytes(CanonicalJson.Serialize(new
        {
            disposition = "unknown",
            code = submission.Code,
            brokerRequestId = submission.BrokerRequestId,
            orderId = (string?)null,
            dealId = (string?)null,
            observedAtUtc = submission.ObservedAtUtc,
            unexpected = false
        }));
        await AssertGatewayInvalidParameterAsync(
            database,
            gatewayContext,
            RecordSql,
            command => BindRawSubmission(
                command,
                request.Command.CommandId,
                authorization.AuthorizationSha256,
                dispatchClaimToken,
                submission,
                replacedSubmissionContent));
        DateTimeOffset recordStartedAt = await ReadDatabaseClockAsync(database);
        TimeSpan authorityWindowAtRecord = dispatchClaim.ClaimExpiresAtUtc - recordStartedAt;
        Assert.True(
            authorityWindowAtRecord > TimeSpan.FromSeconds(2),
            $"Canonical broker record started with only {authorityWindowAtRecord.TotalMilliseconds:F3} ms of authority.");
        YO4X.Trading.Application.BrokerCommandLifecycleReceipt submissionReceipt =
            await lifecycle.RecordSubmissionAsync(
                gatewayContext,
                dispatchClaim,
                submission,
                Guid.CreateVersion7());
        DateTimeOffset recordCompletedAt = await ReadDatabaseClockAsync(database);
        TimeSpan serverElapsedToReceipt = submissionReceipt.RecordedAtUtc - recordStartedAt;
        TimeSpan serverRoundTripElapsed = recordCompletedAt - recordStartedAt;
        Console.WriteLine(
            "Canonical broker record server timing: authority={0:F3}ms, receipt={1:F3}ms, roundtrip={2:F3}ms.",
            authorityWindowAtRecord.TotalMilliseconds,
            serverElapsedToReceipt.TotalMilliseconds,
            serverRoundTripElapsed.TotalMilliseconds);
        Assert.InRange(serverElapsedToReceipt, TimeSpan.Zero, authorityWindowAtRecord);
        Assert.Equal("unknown", submissionReceipt.State);
        Assert.Equal(expectedSubmissionEvidence.Sha256, submissionReceipt.EvidenceSha256);

        const string RecoverSql =
            """
            select count(*)
            from control.recover_expired_broker_command_lifecycle(
                @command_id, @authorization_sha256, @audit_event_id)
            """;
        Assert.Equal(
            0,
            await ExecuteGatewayCountAsync(database, gatewayContext, RecoverSql, command =>
            {
                AddUuid(command, "command_id", request.Command.CommandId);
                AddNullableText(command, "authorization_sha256", null);
                AddUuid(command, "audit_event_id", Guid.CreateVersion7());
            }));

        const string BeginSql =
            """
            select count(*)
            from control.begin_broker_command_reconciliation(
                @command_id, @authorization_sha256, @claim_token, @audit_event_id)
            """;
        Guid reconciliationClaimToken = Guid.CreateVersion7();
        Assert.Equal(
            0,
            await ExecuteGatewayCountAsync(database, gatewayContext, BeginSql, command =>
            {
                AddUuid(command, "command_id", request.Command.CommandId);
                AddNullableText(command, "authorization_sha256", null);
                AddUuid(command, "claim_token", reconciliationClaimToken);
                AddUuid(command, "audit_event_id", Guid.CreateVersion7());
            }));
        YO4X.Trading.Application.BrokerCommandReconciliationClaim reconciliationClaim =
            await lifecycle.BeginReconciliationAsync(
                gatewayContext,
                request.Command.CommandId,
                authorization.AuthorizationSha256,
                reconciliationClaimToken,
                Guid.CreateVersion7());
        DateTimeOffset rawReconciliationObservedAt = UtcNow().AddTicks(7);
        Assert.NotEqual(
            0,
            rawReconciliationObservedAt.Ticks % TimeSpan.TicksPerMicrosecond);
        DateTimeOffset reconciliationObservedAt = YO4X.Trading.Application
            .BrokerCommandLifecycleEvidence.NormalizeUtcTimestamp(
                rawReconciliationObservedAt);
        var unavailableObservation =
            new YO4X.Trading.Application.BrokerCommandReconciliationObservation(
                null,
                Digest("malformed-evidence-negative-source"),
                reconciliationClaim.QueryWindowStartUtc,
                reconciliationObservedAt,
                null);
        YO4X.Trading.Application.ValidatedBrokerCommandReconciliation evidence =
            YO4X.Trading.Application.BrokerCommandReconciliationValidator.Validate(
                reconciliationClaim,
                unavailableObservation,
                reconciliationObservedAt);
        Assert.False(evidence.IsConclusive);
        YO4X.Trading.Application.BrokerCommandCanonicalEvidence expectedReconciliationEvidence =
            YO4X.Trading.Application.BrokerCommandLifecycleEvidence.Reconciliation(evidence);
        var evidenceDocument = new BrokerCommandReconciliationEvidenceDocument(
            evidence.CommandId,
            evidence.AuthorizationSha256,
            evidence.ScopeSha256,
            evidence.BrokerAccountId,
            evidence.DeploymentId,
            evidence.Generation,
            evidence.TargetKind,
            evidence.TargetBrokerId,
            evidence.OwnershipTag,
            evidence.SourceSequence,
            evidence.WindowStartUtc,
            evidence.WindowEndUtc,
            "inconclusive",
            evidence.ReasonCode,
            evidence.SourceEvidenceSha256,
            evidence.OrderId,
            evidence.DealId,
            evidence.ObservedAtUtc,
            evidence.Snapshot);
        byte[] evidenceContent = Encoding.UTF8.GetBytes(CanonicalJson.Serialize(evidenceDocument));
        string replacedEvidenceJson = CanonicalJson.Serialize(evidenceDocument)
            .Replace("\"snapshot\":null", "\"unexpected\":null", StringComparison.Ordinal);
        Assert.DoesNotContain("\"snapshot\":", replacedEvidenceJson, StringComparison.Ordinal);
        byte[] replacedEvidenceContent = Encoding.UTF8.GetBytes(replacedEvidenceJson);
        string poisonedSequenceJson = CanonicalJson.Serialize(evidenceDocument)
            .Replace(
                "\"sourceSequence\":null",
                "\"sourceSequence\":\"garbage\"",
                StringComparison.Ordinal);
        Assert.Contains(
            "\"sourceSequence\":\"garbage\"",
            poisonedSequenceJson,
            StringComparison.Ordinal);
        byte[] poisonedSequenceContent = Encoding.UTF8.GetBytes(poisonedSequenceJson);
        const string CompleteSql =
            """
            select count(*)
            from control.complete_broker_command_reconciliation(
                @command_id, @authorization_sha256, @claim_token,
                @reconciliation_id, @match, @reason_code, @source_evidence_sha256,
                @result_content, @broker_order_id, @broker_deal_id, @observed_at,
                @audit_event_id)
            """;
        Assert.Equal(
            0,
            await ExecuteGatewayCountAsync(database, gatewayContext, CompleteSql, command =>
                BindRawReconciliation(
                    command,
                    evidenceDocument,
                    null,
                    reconciliationClaimToken,
                    Guid.CreateVersion7(),
                    evidenceContent)));
        await AssertGatewayInvalidParameterAsync(
            database,
            gatewayContext,
            CompleteSql,
            command => BindRawReconciliation(
                command,
                evidenceDocument,
                authorization.AuthorizationSha256,
                reconciliationClaimToken,
                Guid.CreateVersion7(),
                replacedEvidenceContent));
        await AssertGatewayInvalidParameterAsync(
            database,
            gatewayContext,
            CompleteSql,
            command => BindRawReconciliation(
                command,
                evidenceDocument,
                authorization.AuthorizationSha256,
                reconciliationClaimToken,
                Guid.CreateVersion7(),
                poisonedSequenceContent));
        YO4X.Trading.Application.BrokerCommandLifecycleReceipt reconciliationReceipt =
            await lifecycle.CompleteReconciliationAsync(
                gatewayContext,
                reconciliationClaimToken,
                Guid.CreateVersion7(),
                evidence,
                Guid.CreateVersion7());
        Assert.Equal("unknown", reconciliationReceipt.State);
        Assert.Equal(
            expectedReconciliationEvidence.Sha256,
            reconciliationReceipt.EvidenceSha256);

        CryptographicOperations.ZeroMemory(submissionContent);
        CryptographicOperations.ZeroMemory(replacedSubmissionContent);
        CryptographicOperations.ZeroMemory(evidenceContent);
        CryptographicOperations.ZeroMemory(replacedEvidenceContent);
        CryptographicOperations.ZeroMemory(poisonedSequenceContent);
    }

    [PostgresFact]
    public async Task GatewayRoleRejectsNonCanonicalAndMistypedLifecycleEvidenceWithoutSideEffects()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        VerificationFixture fixture = await SeedVerifiedStrategyAsync(database);
        await RecordVerificationAsync(database, fixture);
        await PromoteAsync(database, fixture);
        LeaseFixture leaseFixture = await SeedRuntimeAuthorityAsync(
            database,
            fixture,
            desiredState: "running");
        await GrantProofOnlyAuthorizationForDisposableTestAsync(database);
        PostgresBrokerCommandStore store = CreateStore(database, leaseFixture);
        var lifecycle = new PostgresBrokerCommandLifecycleStore(store);

        BrokerCommandAuthorizationRequest submissionRequest = CreateAuthorizationRequest(
            fixture,
            leaseFixture.Lease,
            BrokerCommandAction.ModifyProtection,
            sourceSequence: 81);
        var authorizerContext = new TenantExecutionContext(
            fixture.TenantId,
            fixture.StrategyHostWorkloadId,
            submissionRequest.Command.CommandId);
        BrokerCommandAuthorizationReceipt submissionAuthorization =
            await store.AuthorizeProofOnlyForIntegrationAsync(
                authorizerContext,
                submissionRequest);
        var submissionGatewayContext = new TenantExecutionContext(
            fixture.TenantId,
            fixture.GatewayHostWorkloadId,
            submissionRequest.Command.CommandId);
        Guid submissionClaimToken = Guid.CreateVersion7();
        var submissionReference = new YO4X.Trading.Application.BrokerCommandReference(
            submissionRequest.Command.CommandId,
            submissionAuthorization.AuthorizationSha256,
            ExecutionLeaseEnvelopeDigest.Sha256(leaseFixture.Lease));
        await lifecycle.ClaimForDispatchAsync(
            submissionGatewayContext,
            submissionReference,
            submissionClaimToken,
            Guid.CreateVersion7());
        GatewaySendResult submission = YO4X.Trading.Application
            .BrokerCommandLifecycleEvidence.NormalizeSubmission(new GatewaySendResult(
                GatewayCommandDisposition.Unknown,
                "7",
                "canonical-boundary-request",
                null,
                null,
                UtcNow(),
                false));
        string canonicalSubmission = YO4X.Trading.Application
            .BrokerCommandLifecycleEvidence.Submission(submission).CanonicalJson;
        string[] poisonedSubmissionDocuments =
        [
            canonicalSubmission.Insert(1, " "),
            ReverseRootPropertyOrder(canonicalSubmission),
            canonicalSubmission.Insert(1, "\"code\":\"7\","),
            ReplaceExactly(canonicalSubmission, "\"code\":\"7\"", "\"code\":7"),
            ReplaceExactly(
                canonicalSubmission,
                "\"brokerRequestId\":\"canonical-boundary-request\"",
                "\"brokerRequestId\":{\"value\":1,\"value\":1}")
        ];
        const string RecordSql =
            """
            select count(*)
            from control.record_broker_command_submission(
                @command_id, @authorization_sha256, @claim_token, @disposition,
                @pre_invocation_not_sent_proven, @result_code, @broker_request_id,
                @broker_order_id, @broker_deal_id, @result_content, @observed_at,
                @audit_event_id)
            """;
        foreach (string poisonedSubmission in poisonedSubmissionDocuments)
        {
            byte[] content = Encoding.UTF8.GetBytes(poisonedSubmission);
            try
            {
                await AssertGatewayInvalidParameterAsync(
                    database,
                    submissionGatewayContext,
                    RecordSql,
                    command => BindRawSubmission(
                        command,
                        submissionRequest.Command.CommandId,
                        submissionAuthorization.AuthorizationSha256,
                        submissionClaimToken,
                        submission,
                        content));
                await AssertPendingDispatchHasNoResultAsync(
                    database,
                    submissionRequest.Command.CommandId);
                Assert.Equal(
                    0,
                    await ReadLifecycleEvidenceCountAsync(
                        database,
                        submissionRequest.Command.CommandId,
                        "broker_command.submission_recorded"));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(content);
            }
        }

        GatewaySendResult[] invalidSubmissionArguments =
        [
            submission with { Code = " 7 " },
            submission with { Code = "invalid/code" },
            submission with { BrokerRequestId = "request\u200Bidentifier" }
        ];
        foreach (GatewaySendResult invalidSubmission in invalidSubmissionArguments)
        {
            byte[] invalidSubmissionContent = Encoding.UTF8.GetBytes(CanonicalJson.Serialize(
                new BrokerGatewaySubmissionDocument(
                    "unknown",
                    invalidSubmission.Code,
                    invalidSubmission.BrokerRequestId,
                    invalidSubmission.OrderId,
                    invalidSubmission.DealId,
                    invalidSubmission.ObservedAtUtc,
                    invalidSubmission.PreInvocationNotSentProven)));
            try
            {
                Assert.Equal(
                    0,
                    await ExecuteGatewayCountAsync(
                        database,
                        submissionGatewayContext,
                        RecordSql,
                        command => BindRawSubmission(
                            command,
                            submissionRequest.Command.CommandId,
                            submissionAuthorization.AuthorizationSha256,
                            submissionClaimToken,
                            invalidSubmission,
                            invalidSubmissionContent)));
                await AssertPendingDispatchHasNoResultAsync(
                    database,
                    submissionRequest.Command.CommandId);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(invalidSubmissionContent);
            }
        }

        (BrokerCommandAuthorizationRequest reconciliationRequest,
            BrokerCommandAuthorizationReceipt reconciliationAuthorization,
            TenantExecutionContext reconciliationGatewayContext) =
            await AuthorizeAndSubmitUnknownAsync(
                store,
                lifecycle,
                fixture,
                leaseFixture.Lease,
                BrokerCommandAction.ModifyProtection,
                brokerOrderId: null,
                sourceSequence: 82);
        await RenewExecutionLeaseAsync(database, fixture, leaseFixture.Lease);
        Guid reconciliationClaimToken = Guid.CreateVersion7();
        YO4X.Trading.Application.BrokerCommandReconciliationClaim reconciliationClaim =
            await lifecycle.BeginReconciliationAsync(
                reconciliationGatewayContext,
                reconciliationRequest.Command.CommandId,
                reconciliationAuthorization.AuthorizationSha256,
                reconciliationClaimToken,
                Guid.CreateVersion7());
        DateTimeOffset reconciliationObservedAt = UtcNow();
        var observation =
            new YO4X.Trading.Application.BrokerCommandReconciliationObservation(
                null,
                Digest("canonical-boundary-reconciliation"),
                reconciliationClaim.QueryWindowStartUtc,
                reconciliationObservedAt,
                null);
        YO4X.Trading.Application.ValidatedBrokerCommandReconciliation validated =
            YO4X.Trading.Application.BrokerCommandReconciliationValidator.Validate(
                reconciliationClaim,
                observation,
                reconciliationObservedAt);
        var reconciliationDocument = new BrokerCommandReconciliationEvidenceDocument(
            validated.CommandId,
            validated.AuthorizationSha256,
            validated.ScopeSha256,
            validated.BrokerAccountId,
            validated.DeploymentId,
            validated.Generation,
            validated.TargetKind,
            validated.TargetBrokerId,
            validated.OwnershipTag,
            validated.SourceSequence,
            validated.WindowStartUtc,
            validated.WindowEndUtc,
            "inconclusive",
            "7",
            validated.SourceEvidenceSha256,
            validated.OrderId,
            validated.DealId,
            validated.ObservedAtUtc,
            validated.Snapshot);
        string canonicalReconciliation = CanonicalJson.Serialize(reconciliationDocument);
        string[] poisonedReconciliationDocuments =
        [
            canonicalReconciliation.Insert(1, " "),
            ReverseRootPropertyOrder(canonicalReconciliation),
            canonicalReconciliation.Insert(1, "\"reasonCode\":\"7\","),
            ReplaceExactly(
                canonicalReconciliation,
                "\"reasonCode\":\"7\"",
                "\"reasonCode\":7"),
            ReplaceExactly(
                canonicalReconciliation,
                "\"generation\":1",
                "\"generation\":1e0"),
            ReplaceExactly(
                canonicalReconciliation,
                "\"snapshot\":null",
                "\"snapshot\":{\"value\":1,\"value\":1}")
        ];
        const string CompleteSql =
            """
            select count(*)
            from control.complete_broker_command_reconciliation(
                @command_id, @authorization_sha256, @claim_token,
                @reconciliation_id, @match, @reason_code, @source_evidence_sha256,
                @result_content, @broker_order_id, @broker_deal_id, @observed_at,
                @audit_event_id)
            """;
        foreach (string poisonedReconciliation in poisonedReconciliationDocuments)
        {
            byte[] content = Encoding.UTF8.GetBytes(poisonedReconciliation);
            try
            {
                await AssertGatewayInvalidParameterAsync(
                    database,
                    reconciliationGatewayContext,
                    CompleteSql,
                    command => BindRawReconciliation(
                        command,
                        reconciliationDocument,
                        reconciliationAuthorization.AuthorizationSha256,
                        reconciliationClaimToken,
                        Guid.CreateVersion7(),
                        content));
                await AssertPendingReconciliationHasNoEvidenceAsync(
                    database,
                    reconciliationRequest.Command.CommandId);
                Assert.Equal(
                    0,
                    await ReadLifecycleEvidenceCountAsync(
                        database,
                        reconciliationRequest.Command.CommandId,
                        "broker_command.reconciliation_completed"));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(content);
            }
        }


        BrokerCommandReconciliationEvidenceDocument[] invalidReconciliationArguments =
        [
            reconciliationDocument with { ReasonCode = " 7 " },
            reconciliationDocument with { ReasonCode = "reason\u200Bcode" }
        ];
        foreach (BrokerCommandReconciliationEvidenceDocument invalidReconciliation
            in invalidReconciliationArguments)
        {
            byte[] invalidReconciliationContent = Encoding.UTF8.GetBytes(
                CanonicalJson.Serialize(invalidReconciliation));
            try
            {
                Assert.Equal(
                    0,
                    await ExecuteGatewayCountAsync(
                        database,
                        reconciliationGatewayContext,
                        CompleteSql,
                        command => BindRawReconciliation(
                            command,
                            invalidReconciliation,
                            reconciliationAuthorization.AuthorizationSha256,
                            reconciliationClaimToken,
                            Guid.CreateVersion7(),
                            invalidReconciliationContent)));
                await AssertPendingReconciliationHasNoEvidenceAsync(
                    database,
                    reconciliationRequest.Command.CommandId);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(invalidReconciliationContent);
            }
        }
    }

    [PostgresFact]
    public async Task ExpiredDispatchClaimsCannotRecordNewSubmissionAndRecoveryOwnsUnknown()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        VerificationFixture fixture = await SeedVerifiedStrategyAsync(database);
        await RecordVerificationAsync(database, fixture);
        await PromoteAsync(database, fixture);
        LeaseFixture leaseFixture = await SeedRuntimeAuthorityAsync(
            database,
            fixture,
            desiredState: "running");
        await GrantProofOnlyAuthorizationForDisposableTestAsync(database);
        PostgresBrokerCommandStore store = CreateStore(database, leaseFixture);
        var lifecycle = new PostgresBrokerCommandLifecycleStore(store);

        (GatewayCommandDisposition Disposition, string Code, string RequestId)[] cases =
        [
            (GatewayCommandDisposition.Accepted, "accepted_after_claim_expiry", "late-accepted"),
            (GatewayCommandDisposition.Unknown, "unknown_after_claim_expiry", "late-unknown")
        ];
        for (int index = 0; index < cases.Length; index++)
        {
            (GatewayCommandDisposition disposition, string code, string requestId) = cases[index];
            BrokerCommandAuthorizationRequest request = CreateAuthorizationRequest(
                fixture,
                leaseFixture.Lease,
                BrokerCommandAction.Place,
                sourceSequence: 40 + index);
            var authorizerContext = new TenantExecutionContext(
                fixture.TenantId,
                fixture.StrategyHostWorkloadId,
                request.Command.CommandId);
            BrokerCommandAuthorizationReceipt authorization =
                await store.AuthorizeProofOnlyForIntegrationAsync(authorizerContext, request);
            var gatewayContext = new TenantExecutionContext(
                fixture.TenantId,
                fixture.GatewayHostWorkloadId,
                request.Command.CommandId);
            var reference = new YO4X.Trading.Application.BrokerCommandReference(
                request.Command.CommandId,
                authorization.AuthorizationSha256,
                ExecutionLeaseEnvelopeDigest.Sha256(leaseFixture.Lease));
            Guid claimToken = Guid.CreateVersion7();
            YO4X.Trading.Application.BrokerCommandDispatchClaim claim =
                await lifecycle.ClaimForDispatchAsync(
                    gatewayContext,
                    reference,
                    claimToken,
                    Guid.CreateVersion7());

            await WaitUntilDatabaseTimeAfterAsync(database, claim.ClaimExpiresAtUtc);
            GatewaySendResult submission = YO4X.Trading.Application
                .BrokerCommandLifecycleEvidence.NormalizeSubmission(new GatewaySendResult(
                    disposition,
                    code,
                    requestId,
                    null,
                    null,
                    UtcNow().AddTicks(7),
                    false));
            YO4X.Trading.Application.BrokerCommandCanonicalEvidence canonical =
                YO4X.Trading.Application.BrokerCommandLifecycleEvidence.Submission(submission);
            byte[] content = Encoding.UTF8.GetBytes(canonical.CanonicalJson);
            try
            {
                const string RecordSql =
                    """
                    select count(*)
                    from control.record_broker_command_submission(
                        @command_id, @authorization_sha256, @claim_token, @disposition,
                        @pre_invocation_not_sent_proven, @result_code, @broker_request_id,
                        @broker_order_id, @broker_deal_id, @result_content, @observed_at,
                        @audit_event_id)
                    """;
                Assert.Equal(
                    0,
                    await ExecuteGatewayCountAsync(
                        database,
                        gatewayContext,
                        RecordSql,
                        command => BindRawSubmission(
                            command,
                            request.Command.CommandId,
                            authorization.AuthorizationSha256,
                            claimToken,
                            submission,
                            content)));
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => lifecycle.RecordSubmissionAsync(
                        gatewayContext,
                        claim,
                        submission,
                        Guid.CreateVersion7()));
                await AssertPendingDispatchHasNoResultAsync(
                    database,
                    request.Command.CommandId);

                YO4X.Trading.Application.BrokerCommandLifecycleReceipt? recovery =
                    await lifecycle.RecoverExpiredLifecycleAsync(
                        gatewayContext,
                        request.Command.CommandId,
                        authorization.AuthorizationSha256,
                        Guid.CreateVersion7());
                Assert.NotNull(recovery);
                Assert.Equal("unknown", recovery.State);
                Assert.Equal(
                    "unknown",
                    await ReadBrokerCommandStateAsync(database, request.Command.CommandId));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(content);
            }
        }
    }

    [PostgresFact]
    public async Task RestartedAcknowledgedCommandCannotRedispatchAndBeginsReconciliation()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        VerificationFixture fixture = await SeedVerifiedStrategyAsync(database);
        await RecordVerificationAsync(database, fixture);
        await PromoteAsync(database, fixture);
        LeaseFixture leaseFixture = await SeedRuntimeAuthorityAsync(
            database,
            fixture,
            desiredState: "running");
        await GrantProofOnlyAuthorizationForDisposableTestAsync(database);

        PostgresBrokerCommandStore firstProcessStore = CreateStore(database, leaseFixture);
        var firstProcessLifecycle = new PostgresBrokerCommandLifecycleStore(firstProcessStore);
        BrokerCommandAuthorizationRequest request = CreateAuthorizationRequest(
            fixture,
            leaseFixture.Lease,
            BrokerCommandAction.Place,
            sourceSequence: 49);
        var authorizerContext = new TenantExecutionContext(
            fixture.TenantId,
            fixture.StrategyHostWorkloadId,
            request.Command.CommandId);
        BrokerCommandAuthorizationReceipt authorization =
            await firstProcessStore.AuthorizeProofOnlyForIntegrationAsync(
                authorizerContext,
                request);
        var gatewayContext = new TenantExecutionContext(
            fixture.TenantId,
            fixture.GatewayHostWorkloadId,
            request.Command.CommandId);
        var reference = new YO4X.Trading.Application.BrokerCommandReference(
            request.Command.CommandId,
            authorization.AuthorizationSha256,
            ExecutionLeaseEnvelopeDigest.Sha256(leaseFixture.Lease));
        YO4X.Trading.Application.BrokerCommandDispatchClaim dispatchClaim =
            await firstProcessLifecycle.ClaimForDispatchAsync(
                gatewayContext,
                reference,
                Guid.CreateVersion7(),
                Guid.CreateVersion7());
        GatewaySendResult accepted = YO4X.Trading.Application
            .BrokerCommandLifecycleEvidence.NormalizeSubmission(new GatewaySendResult(
                GatewayCommandDisposition.Accepted,
                "accepted",
                "restart-request-1",
                "restart-order-1",
                null,
                UtcNow(),
                false));
        YO4X.Trading.Application.BrokerCommandLifecycleReceipt submission =
            await firstProcessLifecycle.RecordSubmissionAsync(
                gatewayContext,
                dispatchClaim,
                accepted,
                Guid.CreateVersion7());
        Assert.Equal("acknowledged", submission.State);
        Assert.Equal(
            "acknowledged",
            await ReadBrokerCommandStateAsync(database, request.Command.CommandId));

        // A fresh adapter instance models a process restart. PostgreSQL must
        // refuse a second dispatch claim but allow reconciliation to recover
        // directly from the immutable acknowledged submission evidence.
        PostgresBrokerCommandStore restartedStore = CreateStore(database, leaseFixture);
        var restartedLifecycle = new PostgresBrokerCommandLifecycleStore(restartedStore);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => restartedLifecycle.ClaimForDispatchAsync(
                gatewayContext,
                reference,
                Guid.CreateVersion7(),
                Guid.CreateVersion7()));

        YO4X.Trading.Application.BrokerCommandReconciliationClaim reconciliation =
            await restartedLifecycle.BeginReconciliationAsync(
                gatewayContext,
                request.Command.CommandId,
                authorization.AuthorizationSha256,
                Guid.CreateVersion7(),
                Guid.CreateVersion7());

        Assert.False(reconciliation.Replayed);
        Assert.Equal("accepted", reconciliation.SendDisposition);
        Assert.Equal("accepted", reconciliation.SendResultCode);
        Assert.Equal("restart-request-1", reconciliation.BrokerRequestId);
        Assert.Equal("restart-order-1", reconciliation.BrokerOrderId);
        Assert.Equal(
            "reconciliation_pending",
            await ReadBrokerCommandStateAsync(database, request.Command.CommandId));
        Assert.Equal(
            1,
            await ReadLifecycleEvidenceCountAsync(
                database,
                request.Command.CommandId,
                "broker_command.submission_recorded"));
    }

    [PostgresFact]
    public async Task RestartBeforeUnrecordedDispatchClaimExpiryWaitsThenRecoversWithoutRedispatch()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        VerificationFixture fixture = await SeedVerifiedStrategyAsync(database);
        await RecordVerificationAsync(database, fixture);
        await PromoteAsync(database, fixture);
        LeaseFixture leaseFixture = await SeedRuntimeAuthorityAsync(
            database,
            fixture,
            desiredState: "running");
        await GrantProofOnlyAuthorizationForDisposableTestAsync(database);

        PostgresBrokerCommandStore firstProcessStore = CreateStore(database, leaseFixture);
        var firstProcessLifecycle = new PostgresBrokerCommandLifecycleStore(firstProcessStore);
        BrokerCommandAuthorizationRequest request = CreateAuthorizationRequest(
            fixture,
            leaseFixture.Lease,
            BrokerCommandAction.Place,
            sourceSequence: 50);
        var authorizerContext = new TenantExecutionContext(
            fixture.TenantId,
            fixture.StrategyHostWorkloadId,
            request.Command.CommandId);
        BrokerCommandAuthorizationReceipt authorization =
            await firstProcessStore.AuthorizeProofOnlyForIntegrationAsync(
                authorizerContext,
                request);
        var gatewayContext = new TenantExecutionContext(
            fixture.TenantId,
            fixture.GatewayHostWorkloadId,
            request.Command.CommandId);
        var reference = new YO4X.Trading.Application.BrokerCommandReference(
            request.Command.CommandId,
            authorization.AuthorizationSha256,
            ExecutionLeaseEnvelopeDigest.Sha256(leaseFixture.Lease));

        // Model a crash after the send_in_progress marker committed and before
        // any gateway result or submission evidence was durably recorded.
        YO4X.Trading.Application.BrokerCommandDispatchClaim abandonedClaim =
            await firstProcessLifecycle.ClaimForDispatchAsync(
                gatewayContext,
                reference,
                Guid.CreateVersion7(),
                Guid.CreateVersion7());
        Assert.Equal(
            "send_in_progress",
            await ReadBrokerCommandStateAsync(database, request.Command.CommandId));
        Assert.Equal(
            0,
            await ReadLifecycleEvidenceCountAsync(
                database,
                request.Command.CommandId,
                "broker_command.submission_recorded"));

        PostgresBrokerCommandStore restartedStore = CreateStore(database, leaseFixture);
        var restartedLifecycle = new PostgresBrokerCommandLifecycleStore(restartedStore);

        // Before expiry a restarted process can neither dispatch nor reconcile.
        // This is the durable at-most-once-send fence the one-shot worker polls.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => restartedLifecycle.ClaimForDispatchAsync(
                gatewayContext,
                reference,
                Guid.CreateVersion7(),
                Guid.CreateVersion7()));
        Assert.Null(await restartedLifecycle.RecoverExpiredLifecycleAsync(
            gatewayContext,
            request.Command.CommandId,
            authorization.AuthorizationSha256,
            Guid.CreateVersion7()));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => restartedLifecycle.BeginReconciliationAsync(
                gatewayContext,
                request.Command.CommandId,
                authorization.AuthorizationSha256,
                Guid.CreateVersion7(),
                Guid.CreateVersion7()));
        Assert.Equal(
            "send_in_progress",
            await ReadBrokerCommandStateAsync(database, request.Command.CommandId));

        await WaitUntilDatabaseTimeAfterAsync(database, abandonedClaim.ClaimExpiresAtUtc);
        YO4X.Trading.Application.BrokerCommandLifecycleReceipt? recovery =
            await restartedLifecycle.RecoverExpiredLifecycleAsync(
                gatewayContext,
                request.Command.CommandId,
                authorization.AuthorizationSha256,
                Guid.CreateVersion7());
        Assert.NotNull(recovery);
        Assert.Equal("unknown", recovery.State);

        // Recovery never restores Ready, so a new gateway dispatch remains
        // impossible. Only the independently authorized reconciliation lane opens.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => restartedLifecycle.ClaimForDispatchAsync(
                gatewayContext,
                reference,
                Guid.CreateVersion7(),
                Guid.CreateVersion7()));
        YO4X.Trading.Application.BrokerCommandReconciliationClaim reconciliation =
            await restartedLifecycle.BeginReconciliationAsync(
                gatewayContext,
                request.Command.CommandId,
                authorization.AuthorizationSha256,
                Guid.CreateVersion7(),
                Guid.CreateVersion7());
        // Recovery durably records send_disposition = 'unknown' for the
        // ambiguous send, and the reconciliation claim must surface that exact
        // disposition (the application guard accepts only 'accepted' or
        // 'unknown'). Redispatch stays impossible; only this claim carries it.
        Assert.Equal("unknown", reconciliation.SendDisposition);
        Assert.Null(reconciliation.BrokerRequestId);
        Assert.Equal(
            "reconciliation_pending",
            await ReadBrokerCommandStateAsync(database, request.Command.CommandId));
        Assert.Equal(
            0,
            await ReadLifecycleEvidenceCountAsync(
                database,
                request.Command.CommandId,
                "broker_command.submission_recorded"));
    }

    [PostgresFact]
    public async Task SameIdLegacyTerminalReconciliationCannotReplayAsCurrentAuthority()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        VerificationFixture fixture = await SeedVerifiedStrategyAsync(database);
        await RecordVerificationAsync(database, fixture);
        await PromoteAsync(database, fixture);
        LeaseFixture leaseFixture = await SeedRuntimeAuthorityAsync(
            database,
            fixture,
            desiredState: "running");
        await GrantProofOnlyAuthorizationForDisposableTestAsync(database);
        PostgresBrokerCommandStore store = CreateStore(database, leaseFixture);
        var lifecycle = new PostgresBrokerCommandLifecycleStore(store);
        (BrokerCommandAuthorizationRequest request,
            BrokerCommandAuthorizationReceipt authorization,
            TenantExecutionContext gatewayContext) = await AuthorizeAndSubmitUnknownAsync(
                store,
                lifecycle,
                fixture,
                leaseFixture.Lease,
                BrokerCommandAction.Place,
                brokerOrderId: "shape-only-order");
        Guid claimToken = Guid.CreateVersion7();
        YO4X.Trading.Application.BrokerCommandReconciliationClaim claim =
            await lifecycle.BeginReconciliationAsync(
                gatewayContext,
                request.Command.CommandId,
                authorization.AuthorizationSha256,
                claimToken,
                Guid.CreateVersion7());
        (_, BrokerCommandReconciliationEvidenceDocument attemptedTerminal) =
            CreateConclusiveLookingObservation(request, claim, fixture);
        byte[] terminalContent = Encoding.UTF8.GetBytes(
            CanonicalJson.Serialize(attemptedTerminal));
        string terminalDigest = Digest(terminalContent);
        Guid reconciliationId = Guid.CreateVersion7();
        try
        {
            await using NpgsqlConnection connection =
                await database.Administrator.OpenConnectionAsync();
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
            await using var seedLegacy = new NpgsqlCommand(
                """
                set local session_replication_role = replica;
                insert into operations.broker_command_reconciliations
                (
                    id, tenant_id, command_id, authorization_sha256, attempt,
                    match, reason_code, source_evidence_sha256, result,
                    result_content, result_sha256, broker_order_id,
                    broker_deal_id, observed_at, received_at
                )
                values
                (
                    @reconciliation_id, @tenant_id, @command_id,
                    @authorization_sha256, 1, 'filled', @reason_code,
                    @source_evidence_sha256,
                    convert_from(@result_content, 'UTF8')::jsonb,
                    @result_content, @result_sha256, @broker_order_id,
                    @broker_deal_id, @observed_at, @observed_at
                );
                update operations.broker_commands
                set state = 'reconciled', reconciliation_match = 'filled',
                    reconciliation_result_sha256 = @result_sha256,
                    reconciliation_completed_at = @observed_at,
                    row_version = row_version + 1,
                    updated_at = greatest(updated_at, @observed_at)
                where tenant_id = @tenant_id and id = @command_id;
                """,
                connection,
                transaction);
            AddUuid(seedLegacy, "reconciliation_id", reconciliationId);
            AddUuid(seedLegacy, "tenant_id", fixture.TenantId);
            AddUuid(seedLegacy, "command_id", request.Command.CommandId);
            AddText(seedLegacy, "authorization_sha256", authorization.AuthorizationSha256);
            AddText(seedLegacy, "reason_code", attemptedTerminal.ReasonCode);
            AddText(
                seedLegacy,
                "source_evidence_sha256",
                attemptedTerminal.SourceEvidenceSha256);
            seedLegacy.Parameters.AddWithValue(
                "result_content",
                NpgsqlDbType.Bytea,
                terminalContent);
            AddText(seedLegacy, "result_sha256", terminalDigest);
            AddNullableText(seedLegacy, "broker_order_id", attemptedTerminal.OrderId);
            AddNullableText(seedLegacy, "broker_deal_id", attemptedTerminal.DealId);
            AddTimestamp(seedLegacy, "observed_at", attemptedTerminal.ObservedAtUtc);
            Assert.Equal(2, await seedLegacy.ExecuteNonQueryAsync());
            await transaction.CommitAsync();

            PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
                () => store.CompleteReconciliationAsync(
                    gatewayContext,
                    authorization.AuthorizationSha256,
                    claimToken,
                    reconciliationId,
                    attemptedTerminal,
                    Guid.CreateVersion7()));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, rejected.SqlState);
            Assert.Equal(
                "reconciled",
                await ReadBrokerCommandStateAsync(database, request.Command.CommandId));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(terminalContent);
        }
    }

    [PostgresFact]
    public async Task NonPlaceActionsRemainInconclusiveAndCannotBypassDurableSemantics()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        VerificationFixture fixture = await SeedVerifiedStrategyAsync(database);
        await RecordVerificationAsync(database, fixture);
        await PromoteAsync(database, fixture);
        LeaseFixture leaseFixture = await SeedRuntimeAuthorityAsync(
            database,
            fixture,
            desiredState: "running");
        await GrantProofOnlyAuthorizationForDisposableTestAsync(database);

        var store = CreateStore(database, leaseFixture);
        var lifecycle = new PostgresBrokerCommandLifecycleStore(store);
        foreach (BrokerCommandAction action in new[]
        {
            BrokerCommandAction.ModifyProtection,
            BrokerCommandAction.Cancel,
            BrokerCommandAction.Close
        })
        {
            (BrokerCommandAuthorizationRequest request,
                BrokerCommandAuthorizationReceipt authorization,
                TenantExecutionContext gatewayContext) =
                await AuthorizeAndSubmitUnknownAsync(
                    store,
                    lifecycle,
                    fixture,
                    leaseFixture.Lease,
                    action,
                    brokerOrderId: null,
                    sourceSequence: 20 + (int)action);
            Guid reconciliationClaimToken = Guid.CreateVersion7();
            YO4X.Trading.Application.BrokerCommandReconciliationClaim claim =
                await lifecycle.BeginReconciliationAsync(
                    gatewayContext,
                    request.Command.CommandId,
                    authorization.AuthorizationSha256,
                    reconciliationClaimToken,
                    Guid.CreateVersion7());
            (YO4X.Trading.Application.BrokerCommandReconciliationObservation observation,
                BrokerCommandReconciliationEvidenceDocument attemptedTerminal) =
                CreateConclusiveLookingObservation(request, claim, fixture);

            PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
                () => store.CompleteReconciliationAsync(
                    gatewayContext,
                    authorization.AuthorizationSha256,
                    reconciliationClaimToken,
                    Guid.CreateVersion7(),
                    attemptedTerminal,
                    Guid.CreateVersion7()));
            Assert.Equal(PostgresErrorCodes.InvalidParameterValue, rejected.SqlState);

            YO4X.Trading.Application.ValidatedBrokerCommandReconciliation evidence =
                YO4X.Trading.Application.BrokerCommandReconciliationValidator.Validate(
                    claim,
                    observation,
                    observation.WindowEndUtc);
            Assert.False(evidence.IsConclusive);
            Assert.Equal(BrokerReconciliationMatch.Inconclusive, evidence.Match);
            Assert.Null(evidence.SourceSequence);
            Assert.Null(evidence.Snapshot);
            YO4X.Trading.Application.BrokerCommandLifecycleReceipt receipt =
                await lifecycle.CompleteReconciliationAsync(
                    gatewayContext,
                    reconciliationClaimToken,
                    Guid.CreateVersion7(),
                    evidence,
                    Guid.CreateVersion7());
            Assert.Equal("unknown", receipt.State);
        }
    }

    [PostgresFact]
    public async Task PlaceCannotConcludeFromShapeWithoutPersistedExactBrokerOrderIdentity()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        VerificationFixture fixture = await SeedVerifiedStrategyAsync(database);
        await RecordVerificationAsync(database, fixture);
        await PromoteAsync(database, fixture);
        LeaseFixture leaseFixture = await SeedRuntimeAuthorityAsync(
            database,
            fixture,
            desiredState: "running");
        await GrantProofOnlyAuthorizationForDisposableTestAsync(database);
        var store = CreateStore(database, leaseFixture);
        var lifecycle = new PostgresBrokerCommandLifecycleStore(store);
        (BrokerCommandAuthorizationRequest request,
            BrokerCommandAuthorizationReceipt authorization,
            TenantExecutionContext gatewayContext) = await AuthorizeAndSubmitUnknownAsync(
                store,
                lifecycle,
                fixture,
                leaseFixture.Lease,
                BrokerCommandAction.Place,
                brokerOrderId: null);
        Guid reconciliationClaimToken = Guid.CreateVersion7();
        YO4X.Trading.Application.BrokerCommandReconciliationClaim claim =
            await lifecycle.BeginReconciliationAsync(
                gatewayContext,
                request.Command.CommandId,
                authorization.AuthorizationSha256,
                reconciliationClaimToken,
                Guid.CreateVersion7());
        (YO4X.Trading.Application.BrokerCommandReconciliationObservation observation,
            BrokerCommandReconciliationEvidenceDocument attemptedTerminal) =
            CreateConclusiveLookingObservation(request, claim, fixture);

        PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
            () => store.CompleteReconciliationAsync(
                gatewayContext,
                authorization.AuthorizationSha256,
                reconciliationClaimToken,
                Guid.CreateVersion7(),
                attemptedTerminal,
                Guid.CreateVersion7()));
        Assert.Equal(PostgresErrorCodes.InvalidParameterValue, rejected.SqlState);

        YO4X.Trading.Application.ValidatedBrokerCommandReconciliation evidence =
            YO4X.Trading.Application.BrokerCommandReconciliationValidator.Validate(
                claim,
                observation,
                observation.WindowEndUtc);
        Assert.False(evidence.IsConclusive);
        Assert.Equal(
            "broker_reconciliation_place_order_correlation_not_proven",
            evidence.ReasonCode);
        YO4X.Trading.Application.BrokerCommandLifecycleReceipt receipt =
            await lifecycle.CompleteReconciliationAsync(
                gatewayContext,
                reconciliationClaimToken,
                Guid.CreateVersion7(),
                evidence,
                Guid.CreateVersion7());
        Assert.Equal("unknown", receipt.State);
    }

    [PostgresFact]
    public async Task RevokedGatewayArtifactBlocksReconciliationAtBeginAndCompletion()
    {
        postgres.RequireAvailable();
        foreach (bool revokeAfterBegin in new[] { false, true })
        {
            await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
            VerificationFixture fixture = await SeedVerifiedStrategyAsync(database);
            await RecordVerificationAsync(database, fixture);
            await PromoteAsync(database, fixture);
            LeaseFixture leaseFixture = await SeedRuntimeAuthorityAsync(
                database,
                fixture,
                desiredState: "running");
            await GrantProofOnlyAuthorizationForDisposableTestAsync(database);
            var store = CreateStore(database, leaseFixture);
            var lifecycle = new PostgresBrokerCommandLifecycleStore(store);
            (BrokerCommandAuthorizationRequest request,
                BrokerCommandAuthorizationReceipt authorization,
                TenantExecutionContext gatewayContext) =
                await AuthorizeAndSubmitUnknownAsync(
                    store,
                    lifecycle,
                    fixture,
                    leaseFixture.Lease,
                    BrokerCommandAction.Place,
                    brokerOrderId: "shape-only-order");
            Guid claimToken = Guid.CreateVersion7();
            if (!revokeAfterBegin)
            {
                await RevokeGatewayArtifactAsync(database, fixture.GatewayArtifactId);
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => lifecycle.BeginReconciliationAsync(
                        gatewayContext,
                        request.Command.CommandId,
                        authorization.AuthorizationSha256,
                        claimToken,
                        Guid.CreateVersion7()));
                continue;
            }

            YO4X.Trading.Application.BrokerCommandReconciliationClaim claim =
                await lifecycle.BeginReconciliationAsync(
                    gatewayContext,
                    request.Command.CommandId,
                    authorization.AuthorizationSha256,
                    claimToken,
                    Guid.CreateVersion7());
            (YO4X.Trading.Application.BrokerCommandReconciliationObservation observation, _) =
                CreateConclusiveLookingObservation(request, claim, fixture);
            YO4X.Trading.Application.ValidatedBrokerCommandReconciliation evidence =
                YO4X.Trading.Application.BrokerCommandReconciliationValidator.Validate(
                    claim,
                    observation,
                    observation.WindowEndUtc);
            Assert.False(evidence.IsConclusive);
            Assert.Equal(
                "broker_reconciliation_terminal_authority_unavailable",
                evidence.ReasonCode);
            await RevokeGatewayArtifactAsync(database, fixture.GatewayArtifactId);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => lifecycle.CompleteReconciliationAsync(
                    gatewayContext,
                    claimToken,
                    Guid.CreateVersion7(),
                    evidence,
                    Guid.CreateVersion7()));
        }
    }

    [PostgresFact]
    public async Task DefaultCoordinatorSettlesProofOnlyDispatchAsTerminalPreInvocationNotSent()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        VerificationFixture fixture = await SeedVerifiedStrategyAsync(database);
        await RecordVerificationAsync(database, fixture);
        await PromoteAsync(database, fixture);
        LeaseFixture leaseFixture = await SeedRuntimeAuthorityAsync(
            database,
            fixture,
            desiredState: "running");
        await GrantProofOnlyAuthorizationForDisposableTestAsync(database);
        var store = CreateStore(database, leaseFixture);
        var lifecycle = new PostgresBrokerCommandLifecycleStore(store);
        BrokerCommandAuthorizationRequest request = CreateAuthorizationRequest(
            fixture,
            leaseFixture.Lease,
            BrokerCommandAction.ModifyProtection,
            sourceSequence: 30,
            mustBeginAfter: TimeSpan.FromSeconds(2));
        var authorizerContext = new TenantExecutionContext(
            fixture.TenantId,
            fixture.StrategyHostWorkloadId,
            request.Command.CommandId);
        BrokerCommandAuthorizationReceipt authorization =
            await store.AuthorizeProofOnlyForIntegrationAsync(authorizerContext, request);
        var reference = new YO4X.Trading.Application.BrokerCommandReference(
            request.Command.CommandId,
            authorization.AuthorizationSha256,
            ExecutionLeaseEnvelopeDigest.Sha256(leaseFixture.Lease));
        var gatewayContext = new TenantExecutionContext(
            fixture.TenantId,
            fixture.GatewayHostWorkloadId,
            request.Command.CommandId);
        var options = new YO4X.Trading.Application.BrokerCommandCoordinatorOptions();
        Assert.False(options.SubmissionEnabled);
        Assert.Equal(TimeSpan.FromMilliseconds(500), options.GatewaySendTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(100), options.AuthoritySafetyMargin);
        Assert.Equal(TimeSpan.FromMilliseconds(600), options.MinimumAuthorityWindow);
        var dispatchTrust = new P256ExecutionLeaseTrustVerifier(
            new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal)
            {
                [leaseFixture.Lease.SigningKeyId] = leaseFixture.SubjectPublicKeyInfo
            });
        var recordingLifecycle = new RecordingLifecycleStore(lifecycle);
        var coordinator = new YO4X.Trading.Application.BrokerCommandCoordinator(
            recordingLifecycle,
            new YO4X.Trading.Mt5.Mt5ProofOnlyGateway(),
            dispatchTrust,
            options,
            TimeProvider.System);

        YO4X.Trading.Application.BrokerCommandDispatchResult result =
            await coordinator.DispatchAsync(gatewayContext, reference);
        Assert.True(
            result.Outcome ==
                YO4X.Trading.Application.BrokerCommandDispatchOutcome.SubmissionRecorded,
            $"Unexpected coordinator result: {result}; durable error: "
            + recordingLifecycle.LastSubmissionException);
        Assert.False(result.GatewayInvoked);
        Assert.Equal(GatewayCommandDisposition.SubmissionDisabled, result.Disposition);
        Assert.Equal("broker_command_gateway_entry_disabled", result.Code);
        Assert.Equal("submission_disabled", result.DurableState);

        TimeSpan untilPastBegin = request.Reconciliation.MustBeginByUtc - UtcNow()
            + TimeSpan.FromMilliseconds(100);
        if (untilPastBegin > TimeSpan.Zero)
        {
            await Task.Delay(untilPastBegin);
        }

        YO4X.Trading.Application.BrokerCommandLifecycleReceipt? recovery =
            await lifecycle.RecoverExpiredLifecycleAsync(
                gatewayContext,
                request.Command.CommandId,
                authorization.AuthorizationSha256,
                Guid.CreateVersion7());
        Assert.Null(recovery);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => lifecycle.BeginReconciliationAsync(
                gatewayContext,
                request.Command.CommandId,
                authorization.AuthorizationSha256,
                Guid.CreateVersion7(),
                Guid.CreateVersion7()));
        await AssertSubmissionDisabledEvidenceAsync(database, request.Command.CommandId);
    }

    [PostgresFact]
    public async Task RuntimeEventExactReplayCanonicalizesSubmicrosecondObservedAt()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        VerificationFixture fixture = await SeedVerifiedStrategyAsync(database);
        await RecordVerificationAsync(database, fixture);
        await PromoteAsync(database, fixture);
        _ = await SeedRuntimeAuthorityAsync(database, fixture, desiredState: "running");

        await using var runtimeDatabase = new RuntimePostgresDatabase(
            database.WorkerConnectionString,
            database.TenantContextCapabilityProvider,
            allowInsecureLoopbackForDevelopment: true);
        var application = new PostgresRuntimeControlPlaneApplication(
            runtimeDatabase,
            new RuntimeControlPostgresOptions
            {
                ApprovedRuntimeImageDigest = $"sha256:{Digest("runtime-image")}"
            },
            SystemClock.Instance);
        var actor = new WorkloadActor(
            fixture.TenantId,
            fixture.SupervisorWorkloadId,
            fixture.WorkerNodeId,
            fixture.DeploymentId,
            fixture.BrokerAccountId,
            1,
            "test-region",
            "supervisor");
        Guid eventId = Guid.CreateVersion7();
        DateTimeOffset rawObservedAt = UtcNow().AddTicks(7);
        Assert.Equal(7, rawObservedAt.Ticks % TimeSpan.TicksPerMicrosecond);
        DateTimeOffset canonicalObservedAt = new(
            rawObservedAt.Ticks - (rawObservedAt.Ticks % TimeSpan.TicksPerMicrosecond),
            TimeSpan.Zero);
        JsonElement payload = JsonSerializer.SerializeToElement(new
        {
            state = "running",
            source = "submicrosecond-replay-regression"
        });
        var request = new RuntimeEventInput(
            1,
            eventId,
            1,
            1,
            rawObservedAt,
            payload);
        var metadata = new RequestMetadata(
            $"runtime-event-{eventId:N}",
            Guid.CreateVersion7(),
            null,
            "PostgreSQL timestamp precision replay regression");

        RuntimeAcceptance accepted = await application.RecordDeploymentEventAsync(
            actor,
            fixture.DeploymentId,
            request,
            metadata,
            CancellationToken.None);
        RuntimeAcceptance replayed = await application.RecordDeploymentEventAsync(
            actor,
            fixture.DeploymentId,
            request,
            metadata,
            CancellationToken.None);

        Assert.Equal(new RuntimeAcceptance(eventId, "accepted", 2), accepted);
        Assert.Equal(new RuntimeAcceptance(eventId, "duplicate", 2), replayed);
        await using NpgsqlConnection connection = await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select inbox.observed_at,
                   inbox.payload_sha256,
                   (select count(*)
                      from operations.runtime_event_inbox as duplicate_guard
                     where duplicate_guard.tenant_id = @tenant_id
                       and duplicate_guard.deployment_id = @deployment_id
                       and duplicate_guard.event_id = @event_id),
                   (select last_accepted_sequence
                      from operations.runtime_event_cursors as cursor
                     where cursor.tenant_id = @tenant_id
                       and cursor.deployment_id = @deployment_id
                       and cursor.target_id is null
                       and cursor.generation = 1),
                   (select count(*)
                      from audit.audit_events as evidence
                     where evidence.tenant_id = @tenant_id
                       and evidence.action = 'runtime.deployment_event_accepted'
                       and evidence.causation_id = @event_id)
              from operations.runtime_event_inbox as inbox
             where inbox.tenant_id = @tenant_id
               and inbox.deployment_id = @deployment_id
               and inbox.event_id = @event_id
            """,
            connection);
        AddUuid(command, "tenant_id", fixture.TenantId);
        AddUuid(command, "deployment_id", fixture.DeploymentId);
        AddUuid(command, "event_id", eventId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(canonicalObservedAt, reader.GetFieldValue<DateTimeOffset>(0));
        Assert.Equal(Digest(CanonicalJson.Serialize(payload)), reader.GetString(1));
        Assert.Equal(1L, reader.GetInt64(2));
        Assert.Equal(1L, reader.GetInt64(3));
        Assert.Equal(1L, reader.GetInt64(4));
        Assert.False(await reader.ReadAsync());
    }

    [PostgresFact]
    public async Task RuntimeLeaseRenewalReadsExactlyOneSnapshotBeforeCommit()
    {
        postgres.RequireAvailable();
        await using PostgresTestDatabase database = await postgres.CreateDatabaseAsync();
        VerificationFixture fixture = await SeedVerifiedStrategyAsync(database);
        await RecordVerificationAsync(database, fixture);
        await PromoteAsync(database, fixture);
        LeaseFixture leaseFixture = await SeedRuntimeAuthorityAsync(
            database,
            fixture,
            desiredState: "running");
        await AssertWorkerRejectsMistypedSignedLeaseWithoutMutationAsync(
            database,
            fixture,
            leaseFixture.Lease);
        await using var runtimeDatabase = new RuntimePostgresDatabase(
            database.WorkerConnectionString,
            database.TenantContextCapabilityProvider,
            allowInsecureLoopbackForDevelopment: true);
        using var signingProvider = new IntegrationLeaseSigningProvider();
        var options = new RuntimeControlPostgresOptions
        {
            ApprovedRuntimeImageDigest = $"sha256:{Digest("runtime-image")}",
            MaximumLeaseLifetime = TimeSpan.FromMinutes(5),
            MaximumLeaseGracePeriod = TimeSpan.FromMinutes(1)
        };
        var application = new PostgresRuntimeControlPlaneApplication(
            runtimeDatabase,
            options,
            SystemClock.Instance,
            new IntegrationEntitlementProvider(
                fixture.EntitlementId,
                leaseFixture.Lease.Claims.ActionPolicy),
            signingProvider);
        var actor = new WorkloadActor(
            fixture.TenantId,
            fixture.SupervisorWorkloadId,
            fixture.WorkerNodeId,
            fixture.DeploymentId,
            fixture.BrokerAccountId,
            1,
            "test-region",
            "supervisor");
        var metadata = new RequestMetadata(
            $"renew-{fixture.LeaseId:N}",
            Guid.CreateVersion7(),
            0,
            "fresh PostgreSQL renewal proof");

        SignedExecutionLease renewed = await application.RenewLeaseAsync(
            actor,
            new RenewExecutionLease(
                fixture.LeaseId,
                1,
                LeaseActionClass.Reduce | LeaseActionClass.Protect),
            metadata,
            CancellationToken.None);
        Assert.Equal(fixture.LeaseId, renewed.Claims.LeaseId);
        Assert.Equal(P256ExecutionLeaseTrustVerifier.SignatureAlgorithm, renewed.SignatureAlgorithm);
        Assert.Equal(1, await ReadExecutionLeaseRowVersionAsync(database, fixture.LeaseId));
    }

    private static async Task AssertWorkerRejectsMistypedSignedLeaseWithoutMutationAsync(
        PostgresTestDatabase database,
        VerificationFixture fixture,
        SignedExecutionLease lease)
    {
        string canonical = CanonicalJson.Serialize(lease);
        JsonObject digestDrift = JsonNode.Parse(canonical)!.AsObject();
        digestDrift["payloadSha256"] = new string('0', 64);
        string[] poisonedEnvelopes =
        [
            $" {canonical}",
            ReverseRootPropertyOrder(canonical),
            canonical.Insert(1, "\"claims\":{},"),
            canonical.Replace(
                "\"claims\":{",
                "\"claims\":{\"contractVersion\":1,",
                StringComparison.Ordinal),
            ReplaceExactly(canonical, "\"contractVersion\":1", "\"contractVersion\":1.0"),
            canonical.Insert(canonical.Length - 1, ",\"unexpected\":true"),
            CanonicalJson.Serialize(digestDrift)
        ];
        var context = new TenantExecutionContext(
            fixture.TenantId,
            fixture.SupervisorWorkloadId,
            fixture.LeaseId);
        foreach (string poisonedEnvelope in poisonedEnvelopes)
        {
            await using TenantPostgresTransaction transaction =
                await database.Worker.BeginTenantTransactionAsync(context);
            await using NpgsqlCommand command = transaction.CreateCommand(
                "select * from control.persist_signed_execution_lease(@content, 0)");
            command.Parameters.AddWithValue(
                "content",
                NpgsqlDbType.Bytea,
                Encoding.UTF8.GetBytes(poisonedEnvelope));
            PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
                () => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InvalidParameterValue, rejected.SqlState);
            Assert.Equal(0, await ReadExecutionLeaseRowVersionAsync(database, fixture.LeaseId));
        }

        await using TenantPostgresTransaction helperTransaction =
            await database.Worker.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand helper = helperTransaction.CreateCommand(
            "select control.signed_execution_lease_has_typed_shape('{}'::json)");
        PostgresException helperDenied = await Assert.ThrowsAsync<PostgresException>(
            () => helper.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, helperDenied.SqlState);
    }

    private static PostgresBrokerCommandStore CreateStore(
        PostgresTestDatabase database,
        LeaseFixture leaseFixture) => new(
            database.TradeAuthorizer,
            database.GatewayRuntime,
            new P256ExecutionLeaseTrustVerifier(
                new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal)
                {
                    [leaseFixture.Lease.SigningKeyId] = leaseFixture.SubjectPublicKeyInfo
                }));

    private static async Task AssertTradeAuthorizerRejectsPoisonedEvidenceWithoutSideEffectsAsync(
        PostgresTestDatabase database,
        VerificationFixture fixture,
        LeaseFixture leaseFixture,
        TenantExecutionContext context,
        BrokerCommandAuthorizationRequest request)
    {
        RawAuthorizationDocuments canonical = CreateRawAuthorizationDocuments(
            leaseFixture,
            request);
        string[] poisonedCommands =
        [
            $" {canonical.Command}",
            ReplaceExactly(
                canonical.Command,
                "\"contractVersion\":1",
                "\"contractVersion\":1e0")
        ];
        var poisonedDocuments = new List<RawAuthorizationDocuments>(
            poisonedCommands.Select(value => canonical with { Command = value }))
        {
            canonical with
            {
                Exposure = canonical.Exposure.Replace(
                    "\"account\":{",
                    "\"account\":{\"sequence\":11,",
                    StringComparison.Ordinal)
            },
            canonical with
            {
                RiskInput = ReplaceExactly(
                    canonical.RiskInput,
                    "\"actionClass\":2",
                    "\"actionClass\":2e0")
            },
            canonical with
            {
                RiskDecision = ReplaceExactly(
                    canonical.RiskDecision,
                    "\"isAllowed\":true",
                    "\"isAllowed\":1")
            },
            canonical with
            {
                Reconciliation = ReplaceExactly(
                    canonical.Reconciliation,
                    "\"contractVersion\":1",
                    "\"contractVersion\":1.0")
            },
            canonical with
            {
                Authorization = ReplaceExactly(
                    canonical.Authorization,
                    "\"generation\":1",
                    "\"generation\":1e0")
            },
            canonical with { Authorization = ReverseRootPropertyOrder(canonical.Authorization) },
            canonical with
            {
                Authorization = canonical.Authorization.Insert(
                    1,
                    $"\"brokerAccountId\":\"{fixture.BrokerAccountId}\",")
            }
        };

        foreach (RawAuthorizationDocuments poisoned in poisonedDocuments)
        {
            await using TenantPostgresTransaction transaction =
                await database.TradeAuthorizer.BeginTenantTransactionAsync(context);
            await using NpgsqlCommand command = CreateRawAuthorizationCommand(
                transaction,
                leaseFixture,
                request,
                poisoned);
            PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
                () => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InvalidParameterValue, rejected.SqlState);
            Assert.Equal(
                (0L, 0L, 0L, 0L),
                await ReadAuthorizationSideEffectsAsync(database, request));
        }

        await using TenantPostgresTransaction helperTransaction =
            await database.TradeAuthorizer.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand helper = helperTransaction.CreateCommand(
            """
            select control.broker_authorization_evidence_has_typed_shape(
                '{}'::json, '{}'::json, '{}'::json,
                '{}'::json, '{}'::json, '{}'::json)
            """);
        PostgresException helperDenied = await Assert.ThrowsAsync<PostgresException>(
            () => helper.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, helperDenied.SqlState);
    }

    private static RawAuthorizationDocuments CreateRawAuthorizationDocuments(
        LeaseFixture leaseFixture,
        BrokerCommandAuthorizationRequest request)
    {
        string command = CanonicalJson.Serialize(request.Command);
        string exposure = CanonicalJson.Serialize(request.Exposure);
        string riskInput = CanonicalJson.Serialize(request.RiskInput);
        string riskDecision = CanonicalJson.Serialize(request.RiskDecision);
        string reconciliationDocument = CanonicalJson.Serialize(request.Reconciliation);
        DateTimeOffset oldestObservedAt = new[]
        {
            request.Exposure.QuoteAsOfUtc,
            request.Exposure.AccountAsOfUtc,
            request.Exposure.PositionAsOfUtc,
            request.Exposure.OrderAsOfUtc,
            request.Exposure.SymbolAsOfUtc,
            request.Exposure.ConversionRateAsOfUtc,
            request.Exposure.RiskDayAsOfUtc,
            request.Exposure.OrderRateAsOfUtc
        }.Min();
        var exposureAuthorization = new BrokerExposureAuthorization(
            request.Exposure.ContractVersion,
            request.Exposure.SnapshotId,
            Digest(exposure),
            request.Exposure.SourceKind,
            request.Exposure.SourceSequence,
            request.Exposure.SourceEvidenceSha256,
            oldestObservedAt,
            request.RiskInput.EvaluatedAtUtc,
            request.RiskInput.EvaluatedAtUtc.AddSeconds(1));
        var riskAuthorization = new NumericRiskAuthorization(
            request.RiskDecisionId,
            request.ExecutionLease.Claims.Binding.SafetyPolicyVersionId,
            request.RiskDecision.PolicyDigest,
            ToRiskActionStorage(request.RiskDecision.ActionClass),
            request.RiskDecision.InputDigest,
            request.RiskDecision.DecisionDigest,
            request.RiskInput.EvaluatedAtUtc,
            request.RiskDecision.IsAllowed);
        var leaseAuthorization = new ExecutionLeaseAuthorization(
            request.ExecutionLease,
            ExecutionLeaseEnvelopeDigest.Sha256(request.ExecutionLease),
            request.ExecutionLease.PayloadSha256,
            ExecutionLeaseEnvelopeDigest.SignatureSha256(request.ExecutionLease),
            Digest(leaseFixture.SubjectPublicKeyInfo));
        var reconciliation = new BrokerReconciliationCommitment(
            request.Reconciliation.ContractVersion,
            request.Reconciliation.CommandId,
            request.Reconciliation.Method,
            request.Reconciliation.ScopeSha256,
            request.Reconciliation.MustBeginByUtc,
            request.Reconciliation.MustCompleteByUtc,
            Digest(reconciliationDocument));
        BrokerCommandAuthorizationDocument authorization =
            AuthorizedBrokerCommand.CreateDocument(
                request.Command,
                request.Provenance,
                riskAuthorization,
                exposureAuthorization,
                request.ExecutionSafety,
                leaseAuthorization,
                reconciliation);
        return new RawAuthorizationDocuments(
            command,
            exposure,
            riskInput,
            riskDecision,
            reconciliationDocument,
            CanonicalJson.Serialize(authorization));
    }

    private static NpgsqlCommand CreateRawAuthorizationCommand(
        TenantPostgresTransaction transaction,
        LeaseFixture leaseFixture,
        BrokerCommandAuthorizationRequest request,
        RawAuthorizationDocuments documents)
    {
        NpgsqlCommand command = transaction.CreateCommand(
            """
            select * from control.authorize_broker_command(
                @command_id, @intent_id, @broker_account_id, @deployment_id,
                @generation, @source_binding_id, @exposure_id, @risk_decision_id,
                @lease_id, @lease_token_sha256, @lease_payload_sha256,
                @lease_signature_sha256, @lease_signature_algorithm,
                @lease_signing_key_id, @lease_trusted_verification_key_sha256,
                @idempotency_key, @action_class, @execution_safety_overlay_sha256,
                @execution_safety_policy_version_watermark,
                @command_content, @exposure_content, @exposure_source_kind,
                @exposure_source_sequence, @exposure_source_evidence_sha256,
                @quote_as_of, @account_as_of, @position_as_of, @order_as_of,
                @symbol_as_of, @conversion_rate_as_of, @risk_day_as_of,
                @order_rate_as_of, @risk_input_content, @risk_decision_content,
                @risk_evaluated_at, @reconciliation_content,
                @reconciliation_scope_sha256, @reconciliation_must_begin_by,
                @reconciliation_must_complete_by, @authorization_content,
                @audit_event_id)
            """);
        AddUuid(command, "command_id", request.Command.CommandId);
        AddUuid(command, "intent_id", request.Command.IntentId);
        AddUuid(command, "broker_account_id", request.Provenance.BrokerAccountId);
        AddUuid(command, "deployment_id", request.Command.DeploymentId);
        command.Parameters.AddWithValue("generation", NpgsqlDbType.Bigint, request.Command.Generation);
        AddUuid(command, "source_binding_id", request.Provenance.StrategySourceBindingId);
        AddUuid(command, "exposure_id", request.Exposure.SnapshotId);
        AddUuid(command, "risk_decision_id", request.RiskDecisionId);
        AddUuid(command, "lease_id", request.ExecutionLease.Claims.LeaseId);
        AddText(
            command,
            "lease_token_sha256",
            ExecutionLeaseEnvelopeDigest.Sha256(request.ExecutionLease));
        AddText(command, "lease_payload_sha256", request.ExecutionLease.PayloadSha256);
        AddText(
            command,
            "lease_signature_sha256",
            ExecutionLeaseEnvelopeDigest.SignatureSha256(request.ExecutionLease));
        AddText(command, "lease_signature_algorithm", request.ExecutionLease.SignatureAlgorithm);
        AddText(command, "lease_signing_key_id", request.ExecutionLease.SigningKeyId);
        AddText(
            command,
            "lease_trusted_verification_key_sha256",
            Digest(leaseFixture.SubjectPublicKeyInfo));
        AddText(command, "idempotency_key", request.Command.IdempotencyKey);
        AddText(command, "action_class", ToRiskActionStorage(request.RiskDecision.ActionClass));
        AddText(
            command,
            "execution_safety_overlay_sha256",
            request.ExecutionSafety.EffectiveOverlaySha256);
        command.Parameters.AddWithValue(
            "execution_safety_policy_version_watermark",
            NpgsqlDbType.Bigint,
            request.ExecutionSafety.PolicyVersionWatermark);
        command.Parameters.AddWithValue(
            "command_content", NpgsqlDbType.Bytea, Encoding.UTF8.GetBytes(documents.Command));
        command.Parameters.AddWithValue(
            "exposure_content", NpgsqlDbType.Bytea, Encoding.UTF8.GetBytes(documents.Exposure));
        AddText(command, "exposure_source_kind", request.Exposure.SourceKind);
        command.Parameters.AddWithValue(
            "exposure_source_sequence", NpgsqlDbType.Bigint, request.Exposure.SourceSequence);
        AddText(
            command,
            "exposure_source_evidence_sha256",
            request.Exposure.SourceEvidenceSha256);
        AddTimestamp(command, "quote_as_of", request.Exposure.QuoteAsOfUtc);
        AddTimestamp(command, "account_as_of", request.Exposure.AccountAsOfUtc);
        AddTimestamp(command, "position_as_of", request.Exposure.PositionAsOfUtc);
        AddTimestamp(command, "order_as_of", request.Exposure.OrderAsOfUtc);
        AddTimestamp(command, "symbol_as_of", request.Exposure.SymbolAsOfUtc);
        AddTimestamp(command, "conversion_rate_as_of", request.Exposure.ConversionRateAsOfUtc);
        AddTimestamp(command, "risk_day_as_of", request.Exposure.RiskDayAsOfUtc);
        AddTimestamp(command, "order_rate_as_of", request.Exposure.OrderRateAsOfUtc);
        command.Parameters.AddWithValue(
            "risk_input_content", NpgsqlDbType.Bytea, Encoding.UTF8.GetBytes(documents.RiskInput));
        command.Parameters.AddWithValue(
            "risk_decision_content",
            NpgsqlDbType.Bytea,
            Encoding.UTF8.GetBytes(documents.RiskDecision));
        AddTimestamp(command, "risk_evaluated_at", request.RiskInput.EvaluatedAtUtc);
        command.Parameters.AddWithValue(
            "reconciliation_content",
            NpgsqlDbType.Bytea,
            Encoding.UTF8.GetBytes(documents.Reconciliation));
        AddText(command, "reconciliation_scope_sha256", request.Reconciliation.ScopeSha256);
        AddTimestamp(
            command,
            "reconciliation_must_begin_by",
            request.Reconciliation.MustBeginByUtc);
        AddTimestamp(
            command,
            "reconciliation_must_complete_by",
            request.Reconciliation.MustCompleteByUtc);
        command.Parameters.AddWithValue(
            "authorization_content",
            NpgsqlDbType.Bytea,
            Encoding.UTF8.GetBytes(documents.Authorization));
        AddUuid(command, "audit_event_id", Guid.CreateVersion7());
        return command;
    }

    private static async Task<(long Commands, long Exposures, long RiskDecisions, long AuditEvents)>
        ReadAuthorizationSideEffectsAsync(
            PostgresTestDatabase database,
            BrokerCommandAuthorizationRequest request)
    {
        await using NpgsqlConnection connection =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select
                (select count(*) from operations.broker_commands
                 where id = @command_id),
                (select count(*) from operations.broker_exposure_snapshots
                 where id = @exposure_id),
                (select count(*) from operations.broker_command_risk_decisions
                 where id = @risk_decision_id),
                (select count(*) from audit.audit_events
                 where target_type = 'broker_command'
                   and target_id = @command_text)
            """,
            connection);
        AddUuid(command, "command_id", request.Command.CommandId);
        AddUuid(command, "exposure_id", request.Exposure.SnapshotId);
        AddUuid(command, "risk_decision_id", request.RiskDecisionId);
        AddText(command, "command_text", request.Command.CommandId.ToString());
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var result = (
            reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
        Assert.False(await reader.ReadAsync());
        return result;
    }

    private static string ToRiskActionStorage(RiskActionClass actionClass) => actionClass switch
    {
        RiskActionClass.ExposureIncrease => "exposure_increase",
        RiskActionClass.ExposureReduction => "exposure_reduction",
        RiskActionClass.Protection => "protection",
        RiskActionClass.PendingOrderCancellation => "pending_order_cancellation",
        RiskActionClass.EmergencyClose => "emergency_close",
        _ => throw new ArgumentOutOfRangeException(nameof(actionClass))
    };

    private static async Task RevokeGatewayArtifactAsync(
        PostgresTestDatabase database,
        Guid gatewayArtifactId)
    {
        await using NpgsqlConnection connection =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            update governance.gateway_artifacts
            set state = 'revoked', row_version = row_version + 1,
                updated_at = clock_timestamp()
            where id = @gateway_artifact_id
            """,
            connection);
        AddUuid(command, "gateway_artifact_id", gatewayArtifactId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task AssertSubmissionDisabledEvidenceAsync(
        PostgresTestDatabase database,
        Guid commandId)
    {
        await using NpgsqlConnection connection =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select state, send_result ->> 'preInvocationNotSentProven',
                   broker_request_id, broker_order_id, broker_deal_id
            from operations.broker_commands
            where id = @command_id
            """,
            connection);
        AddUuid(command, "command_id", commandId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("submission_disabled", reader.GetString(0));
        Assert.Equal("true", reader.GetString(1));
        Assert.True(reader.IsDBNull(2));
        Assert.True(reader.IsDBNull(3));
        Assert.True(reader.IsDBNull(4));
        Assert.False(await reader.ReadAsync());
    }

    private static async Task<long> ReadExecutionLeaseRowVersionAsync(
        PostgresTestDatabase database,
        Guid leaseId)
    {
        await using NpgsqlConnection connection =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "select row_version from operations.execution_leases where id = @lease_id",
            connection);
        AddUuid(command, "lease_id", leaseId);
        return Assert.IsType<long>(await command.ExecuteScalarAsync());
    }

    private static async Task<(BrokerCommandAuthorizationRequest Request,
        BrokerCommandAuthorizationReceipt Authorization,
        TenantExecutionContext GatewayContext)> AuthorizeAndSubmitUnknownAsync(
        PostgresBrokerCommandStore store,
        PostgresBrokerCommandLifecycleStore lifecycle,
        VerificationFixture fixture,
        SignedExecutionLease lease,
        BrokerCommandAction action,
        string? brokerOrderId,
        long sourceSequence = 12)
    {
        string leaseDigest = ExecutionLeaseEnvelopeDigest.Sha256(lease);
        Guid claimToken = Guid.CreateVersion7();
        Guid claimAuditEventId = Guid.CreateVersion7();
        BrokerCommandAuthorizationRequest request =
            CreateAuthorizationRequest(fixture, lease, action, sourceSequence);
        var authorizerContext = new TenantExecutionContext(
            fixture.TenantId,
            fixture.StrategyHostWorkloadId,
            request.Command.CommandId);
        BrokerCommandAuthorizationReceipt authorization =
            await store.AuthorizeProofOnlyForIntegrationAsync(authorizerContext, request);
        var gatewayContext = new TenantExecutionContext(
            fixture.TenantId,
            fixture.GatewayHostWorkloadId,
            request.Command.CommandId);
        var reference = new YO4X.Trading.Application.BrokerCommandReference(
            request.Command.CommandId,
            authorization.AuthorizationSha256,
            leaseDigest);
        YO4X.Trading.Application.BrokerCommandDispatchClaim claim =
            await lifecycle.ClaimForDispatchAsync(
                gatewayContext,
                reference,
                claimToken,
                claimAuditEventId);
        var unknown = new GatewaySendResult(
            GatewayCommandDisposition.Unknown,
            "transport_outcome_unknown",
            "request-unknown",
            brokerOrderId,
            null,
            UtcNow(),
            false);
        YO4X.Trading.Application.BrokerCommandLifecycleReceipt submission =
            await lifecycle.RecordSubmissionAsync(
                gatewayContext,
                claim,
                unknown,
                Guid.CreateVersion7());
        Assert.Equal("unknown", submission.State);
        return (request, authorization, gatewayContext);
    }

    private static (YO4X.Trading.Application.BrokerCommandReconciliationObservation Observation,
        BrokerCommandReconciliationEvidenceDocument AttemptedTerminal)
        CreateConclusiveLookingObservation(
            BrokerCommandAuthorizationRequest request,
            YO4X.Trading.Application.BrokerCommandReconciliationClaim claim,
            VerificationFixture fixture)
    {
        DateTimeOffset observedAt = UtcNow();
        if (observedAt <= claim.StartedAtUtc)
        {
            observedAt = claim.StartedAtUtc.AddMilliseconds(1);
        }

        long sourceSequence = request.Exposure.SourceSequence + 1;
        BrokerReconciliationMatch reportedMatch;
        string? reportedOrderId;
        string? reportedDealId;
        IReadOnlyList<BrokerPositionSnapshot> positions;
        IReadOnlyList<BrokerOrderSnapshot> orders;
        IReadOnlyList<BrokerDealSnapshot> deals;
        switch (request.Command.Action)
        {
            case BrokerCommandAction.Place:
                reportedMatch = BrokerReconciliationMatch.Filled;
                reportedOrderId = "shape-only-order";
                reportedDealId = "shape-only-deal";
                positions = [];
                orders =
                [
                    new BrokerOrderSnapshot(
                        reportedOrderId,
                        request.Command.Symbol,
                        request.Command.Side,
                        request.Command.OrderType,
                        request.Command.Volume,
                        0m,
                        request.Command.RequestedPrice,
                        request.Command.StopLoss,
                        request.Command.TakeProfit,
                        "filled",
                        request.Command.OwnershipTag,
                        observedAt)
                ];
                deals =
                [
                    new BrokerDealSnapshot(
                        reportedDealId,
                        reportedOrderId,
                        request.Command.Symbol,
                        request.Command.Side,
                        request.Command.Volume,
                        1.1m,
                        observedAt)
                ];
                break;
            case BrokerCommandAction.ModifyProtection:
                reportedMatch = BrokerReconciliationMatch.Acknowledged;
                reportedOrderId = null;
                reportedDealId = null;
                positions =
                [
                    new BrokerPositionSnapshot(
                        request.Command.TargetBrokerId!,
                        request.Command.Symbol,
                        request.Command.Side,
                        request.Command.Volume,
                        1.1m,
                        request.Command.StopLoss,
                        request.Command.TakeProfit,
                        request.Command.OwnershipTag,
                        observedAt)
                ];
                orders = [];
                deals = [];
                break;
            case BrokerCommandAction.Cancel:
                reportedMatch = BrokerReconciliationMatch.Cancelled;
                reportedOrderId = request.Command.TargetBrokerId;
                reportedDealId = null;
                positions = [];
                orders = [];
                deals = [];
                break;
            case BrokerCommandAction.Close:
                reportedMatch = BrokerReconciliationMatch.Filled;
                reportedOrderId = "close-order";
                reportedDealId = "close-deal";
                positions = [];
                orders = [];
                deals =
                [
                    new BrokerDealSnapshot(
                        reportedDealId,
                        reportedOrderId,
                        request.Command.Symbol,
                        request.Command.Side,
                        request.Command.Volume,
                        1.1m,
                        observedAt)
                ];
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request));
        }

        var account = new BrokerAccountSnapshot(
            sourceSequence,
            "***4242",
            "YO4X Test Broker",
            "Demo-1",
            YO4X.Trading.Abstractions.BrokerAccountMode.Hedging,
            BrokerEnvironment.Demo,
            BrokerTradingAccess.TradingAllowed,
            "USD",
            10_000m,
            10_000m,
            9_000m,
            observedAt);
        var snapshot = new BrokerReconciliationSnapshot(
            1,
            sourceSequence,
            fixture.BrokerAccountId,
            fixture.DeploymentId,
            request.Command.Generation,
            fixture.GatewayArtifactId,
            request.Provenance.GatewayArtifactSha256,
            claim.QueryWindowStartUtc,
            observedAt,
            true,
            true,
            account,
            positions,
            orders,
            deals,
            [
                new BrokerCommandReconciliation(
                    request.Command.CommandId,
                    reportedMatch,
                    "conclusive_looking_gateway_report",
                    reportedOrderId,
                    reportedDealId,
                    observedAt)
            ],
            observedAt);
        var sourceDocument = new YO4X.Trading.Application
            .BrokerCommandReconciliationValidator.BrokerReconciliationSourceDocument(
                sourceSequence,
                claim.QueryWindowStartUtc,
                observedAt,
                snapshot);
        string sourceSha256 = CanonicalJson.Sha256(sourceDocument);
        var observation = new YO4X.Trading.Application.BrokerCommandReconciliationObservation(
            sourceSequence,
            sourceSha256,
            claim.QueryWindowStartUtc,
            observedAt,
            snapshot);
        var attemptedTerminal = new BrokerCommandReconciliationEvidenceDocument(
            request.Command.CommandId,
            claim.Command.AuthorizationSha256,
            claim.ScopeSha256,
            fixture.BrokerAccountId,
            fixture.DeploymentId,
            request.Command.Generation,
            request.Command.TargetKind,
            request.Command.TargetBrokerId,
            request.Command.OwnershipTag,
            sourceSequence,
            claim.QueryWindowStartUtc,
            observedAt,
            reportedMatch switch
            {
                BrokerReconciliationMatch.Acknowledged => "acknowledged",
                BrokerReconciliationMatch.Filled => "filled",
                BrokerReconciliationMatch.Cancelled => "cancelled",
                _ => throw new InvalidOperationException("Unsupported test match.")
            },
            "attempted_terminal_assertion",
            sourceSha256,
            reportedOrderId,
            reportedDealId,
            observedAt,
            snapshot);
        return (observation, attemptedTerminal);
    }

    private static async Task GrantProofOnlyAuthorizationForDisposableTestAsync(
        PostgresTestDatabase database)
    {
        await using NpgsqlConnection connection =
            await database.Administrator.OpenConnectionAsync();
        await using var grant = new NpgsqlCommand(
            """
            do $body$
            declare
                authorize_function regprocedure;
            begin
                select function.oid::regprocedure into strict authorize_function
                from pg_proc as function
                join pg_namespace as namespace on namespace.oid = function.pronamespace
                where namespace.nspname = 'control'
                  and function.proname = 'authorize_broker_command';
                execute format(
                    'grant execute on function %s to yo4x_trade_authorizer',
                    authorize_function);
            end
            $body$;
            """,
            connection);
        await grant.ExecuteNonQueryAsync();
    }

    private static async Task<VerificationFixture> SeedVerifiedStrategyAsync(
        PostgresTestDatabase database)
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();
        Guid importJobId = Guid.CreateVersion7();
        Guid importCorrelationId = Guid.CreateVersion7();
        byte[] capability = RandomNumberGenerator.GetBytes(32);
        byte[] capabilityDigest = SHA256.HashData(capability);
        DateTimeOffset now = UtcNow();
        try
        {
            var importContext = new TenantExecutionContext(
                tenantId,
                userId,
                importCorrelationId);
            await using (TenantPostgresTransaction seed =
                await database.Application.BeginTenantTransactionAsync(importContext))
            {
                await using NpgsqlCommand command = seed.CreateCommand(
                    """
                    insert into identity.tenants (id, slug, display_name)
                    values (@tenant_id, @slug, 'Durable authorization tenant');
                    insert into identity.user_identities
                        (id, tenant_id, normalized_email, security_state,
                         email_verified_at, created_at, updated_at)
                    values
                        (@user_id, @tenant_id, @email, 'active', @now, @now, @now);
                    """);
                AddUuid(command, "tenant_id", tenantId);
                AddUuid(command, "user_id", userId);
                command.Parameters.AddWithValue("slug", NpgsqlDbType.Text, $"tenant-{tenantId:N}");
                command.Parameters.AddWithValue(
                    "email",
                    NpgsqlDbType.Text,
                    $"user-{userId:N}@example.test");
                AddTimestamp(command, "now", now);
                await command.ExecuteNonQueryAsync();
                await seed.CommitAsync();
            }

            await using (TenantPostgresTransaction control =
                await database.ControlApi.BeginTenantTransactionAsync(importContext))
            {
                await using NpgsqlCommand command = control.CreateCommand(
                    """
                    insert into control.strategy_import_jobs
                        (id, tenant_id, user_id, correlation_id, source_label,
                         capability_sha256, proof_key_id, expires_at)
                    values
                        (@id, @tenant_id, @user_id, @correlation_id,
                         'durable-auth-ea', @capability_sha256, repeat('a', 64),
                         statement_timestamp() + interval '20 minutes')
                    """);
                AddUuid(command, "id", importJobId);
                AddUuid(command, "tenant_id", tenantId);
                AddUuid(command, "user_id", userId);
                AddUuid(command, "correlation_id", importCorrelationId);
                command.Parameters.AddWithValue(
                    "capability_sha256",
                    NpgsqlDbType.Bytea,
                    capabilityDigest);
                await command.ExecuteNonQueryAsync();
                await control.CommitAsync();
            }

            byte[] source = Encoding.UTF8.GetBytes(
                "void OnTick(){ MqlTradeRequest r={}; MqlTradeResult x={}; OrderSend(r,x); }");
            using var corpus = new Mql5AnalyzedCorpus(
                new Mql5StaticInventoryAnalyzer().Analyze(
                    [new Mql5SourceDocument("Experts/Durable.mq5", source)]),
                [new Mql5SourceDocument("Experts/Durable.mq5", source.ToArray())]);
            using var persistenceRequest = new Mql5CorpusPersistenceRequest(
                importJobId,
                capability);
            Mql5CorpusPersistenceResult persisted = await new PostgresMql5CorpusStore(
                database.ConversionWorker).PersistAsync(persistenceRequest, corpus);
            (string reportSha256, DateTimeOffset corpusCreatedAt) =
                await ReadPersistedCorpusBindingAsync(database, importJobId, persisted);

            Guid strategyId = Guid.CreateVersion7();
            Guid strategyVersionId = Guid.CreateVersion7();
            Guid bindingId = Guid.CreateVersion7();
            Guid verifierId = Guid.CreateVersion7();
            string packageSha256 = Digest("strategy-package");
            string signingKeyId = "strategy-verifier-key-1";
            var fixture = new VerificationFixture(
                tenantId,
                userId,
                strategyId,
                strategyVersionId,
                bindingId,
                importJobId,
                packageSha256,
                persisted.CorpusSha256,
                persisted.ManifestSha256,
                reportSha256,
                Digest("compiled-artifact"),
                Digest("compiler-artifact"),
                Digest("parse-typecheck-proof"),
                Digest("compile-proof"),
                Digest("semantic-conversion-proof"),
                Digest("reference-parity-proof"),
                Digest("demo-runtime-proof"),
                verifierId,
                signingKeyId,
                [],
                corpusCreatedAt,
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7());

            byte[] verificationEvidence = VerificationEvidence(fixture, signingKeyId);
            try
            {
                using ECDsa verifierKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                fixture = fixture with
                {
                    VerificationSignature = verifierKey.SignData(
                        verificationEvidence,
                        HashAlgorithmName.SHA256,
                        DSASignatureFormat.Rfc3279DerSequence)
                };
            }
            finally
            {
                CryptographicOperations.ZeroMemory(verificationEvidence);
            }

            await using TenantPostgresTransaction governance =
                await database.Application.BeginTenantTransactionAsync(fixture.UserContext);
            await using NpgsqlCommand strategy = governance.CreateCommand(
                """
                insert into governance.strategy_versions
                    (id, tenant_id, strategy_id, version_number, package_sha256,
                     manifest_sha256, schema_sha256, provenance, evidence, state,
                     created_at, updated_at)
                values
                    (@id, @tenant_id, @strategy_id, 1, @package_sha256,
                     @manifest_sha256, @schema_sha256,
                     '{"source":"verified-corpus"}'::jsonb, '{}'::jsonb,
                     'simulation_review', @now, @now)
                """);
            AddUuid(strategy, "id", strategyVersionId);
            AddUuid(strategy, "tenant_id", tenantId);
            AddUuid(strategy, "strategy_id", strategyId);
            strategy.Parameters.AddWithValue("package_sha256", NpgsqlDbType.Text, packageSha256);
            strategy.Parameters.AddWithValue("manifest_sha256", NpgsqlDbType.Text, Digest("manifest"));
            strategy.Parameters.AddWithValue("schema_sha256", NpgsqlDbType.Text, Digest("schema"));
            AddTimestamp(strategy, "now", UtcNow());
            await strategy.ExecuteNonQueryAsync();
            await governance.CommitAsync();
            return fixture;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(capability);
            CryptographicOperations.ZeroMemory(capabilityDigest);
        }
    }

    private static async Task AssertRawAdminPromotionRejectedAsync(
        PostgresTestDatabase database,
        VerificationFixture fixture)
    {
        await using TenantPostgresTransaction transaction =
            await database.AdminBff.BeginTenantTransactionAsync(fixture.AdminContext);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            update governance.strategy_versions
            set state = 'demo_approved', evidence = '{"forged":true}'::jsonb,
                row_version = row_version + 1, updated_at = clock_timestamp()
            where tenant_id = @tenant_id and id = @id
            """);
        AddUuid(command, "tenant_id", fixture.TenantId);
        AddUuid(command, "id", fixture.StrategyVersionId);
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(
            async () => await command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    private static async Task<(string ReportSha256, DateTimeOffset CreatedAt)>
        ReadPersistedCorpusBindingAsync(
            PostgresTestDatabase database,
            Guid corpusId,
            Mql5CorpusPersistenceResult persisted)
    {
        await using NpgsqlConnection connection = await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select corpus_sha256, manifest_sha256, report_sha256, state, created_at
            from governance.strategy_source_corpora
            where id = @corpus_id
            """,
            connection);
        AddUuid(command, "corpus_id", corpusId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(persisted.CorpusSha256, reader.GetString(0));
        Assert.Equal(persisted.ManifestSha256, reader.GetString(1));
        Assert.Equal("static_analyzed", reader.GetString(3));
        string reportSha256 = reader.GetString(2);
        DateTimeOffset createdAt = reader.GetFieldValue<DateTimeOffset>(4);
        Assert.False(await reader.ReadAsync());
        return (reportSha256, createdAt);
    }

    private static async Task AssertWrongVerificationMetadataRejectedAsync(
        PostgresTestDatabase database,
        VerificationFixture fixture)
    {
        byte[] wrongEvidence = VerificationEvidence(fixture, "wrong-signing-key");
        try
        {
            await using TenantPostgresTransaction transaction =
                await database.StrategyVerifier.BeginTenantTransactionAsync(fixture.VerifierContext);
            await using NpgsqlCommand command = VerificationCommand(transaction, fixture, wrongEvidence);
            PostgresException exception = await Assert.ThrowsAsync<PostgresException>(
                async () => await command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrongEvidence);
        }
    }

    private static async Task AssertVerifierRejectsPoisonedEvidenceWithoutSideEffectsAsync(
        PostgresTestDatabase database,
        VerificationFixture fixture)
    {
        byte[] evidence = VerificationEvidence(fixture, fixture.SigningKeyId);
        try
        {
            string canonical = Encoding.UTF8.GetString(evidence);
            JsonObject extra = JsonNode.Parse(canonical)!.AsObject();
            extra["unexpected"] = true;
            string[] poisonedDocuments =
            [
                $" {canonical}",
                ReverseRootPropertyOrder(canonical),
                canonical.Insert(1, "\"contractVersion\":1,"),
                ReplaceExactly(canonical, "\"contractVersion\":1", "\"contractVersion\":1.0"),
                CanonicalJson.Serialize(extra)
            ];

            foreach (string poisonedDocument in poisonedDocuments)
            {
                await using TenantPostgresTransaction transaction =
                    await database.StrategyVerifier.BeginTenantTransactionAsync(
                        fixture.VerifierContext);
                await using NpgsqlCommand command = VerificationCommand(
                    transaction,
                    fixture,
                    Encoding.UTF8.GetBytes(poisonedDocument));
                PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
                    () => command.ExecuteNonQueryAsync());
                Assert.Equal(PostgresErrorCodes.InvalidParameterValue, rejected.SqlState);
                Assert.Equal(
                    (0L, 0L),
                    await ReadVerificationSideEffectsAsync(database, fixture.BindingId));
            }

            await using TenantPostgresTransaction helperTransaction =
                await database.StrategyVerifier.BeginTenantTransactionAsync(
                    fixture.VerifierContext);
            await using NpgsqlCommand helper = helperTransaction.CreateCommand(
                "select control.is_dotnet_canonical_json('{}')");
            PostgresException helperDenied = await Assert.ThrowsAsync<PostgresException>(
                () => helper.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, helperDenied.SqlState);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(evidence);
        }
    }

    private static async Task<(long Bindings, long AuditEvents)>
        ReadVerificationSideEffectsAsync(
            PostgresTestDatabase database,
            Guid bindingId)
    {
        await using NpgsqlConnection connection =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select
                (select count(*) from governance.strategy_version_source_bindings
                 where id = @binding_id),
                (select count(*) from audit.audit_events
                 where target_type = 'strategy_source_binding'
                   and target_id = @binding_text)
            """,
            connection);
        AddUuid(command, "binding_id", bindingId);
        AddText(command, "binding_text", bindingId.ToString());
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var result = (reader.GetInt64(0), reader.GetInt64(1));
        Assert.False(await reader.ReadAsync());
        return result;
    }

    private static async Task RecordVerificationAsync(
        PostgresTestDatabase database,
        VerificationFixture fixture)
    {
        await AssertVerificationEligibilityAsync(database, fixture);
        byte[] evidence = VerificationEvidence(fixture, fixture.SigningKeyId);
        try
        {
            await using TenantPostgresTransaction transaction =
                await database.StrategyVerifier.BeginTenantTransactionAsync(fixture.VerifierContext);
            await using NpgsqlCommand command = VerificationCommand(transaction, fixture, evidence);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(fixture.BindingId, reader.GetGuid(0));
            Assert.Equal(Digest(evidence), reader.GetString(1));
            Assert.Equal(Digest(fixture.VerificationSignature), reader.GetString(2));
            Assert.False(reader.GetBoolean(4));
            Assert.False(await reader.ReadAsync());
            await reader.DisposeAsync();
            await transaction.CommitAsync();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(evidence);
        }
    }

    private static async Task AssertVerificationEligibilityAsync(
        PostgresTestDatabase database,
        VerificationFixture fixture)
    {
        await using NpgsqlConnection connection = await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select strategy.state, strategy.package_sha256,
                   corpus.state, corpus.corpus_sha256, corpus.manifest_sha256,
                   corpus.report_sha256, corpus.created_at
            from governance.strategy_versions as strategy
            cross join governance.strategy_source_corpora as corpus
            where strategy.tenant_id = @tenant_id
              and strategy.id = @strategy_version_id
              and corpus.tenant_id = @tenant_id
              and corpus.id = @corpus_id
            """,
            connection);
        AddUuid(command, "tenant_id", fixture.TenantId);
        AddUuid(command, "strategy_version_id", fixture.StrategyVersionId);
        AddUuid(command, "corpus_id", fixture.CorpusId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("simulation_review", reader.GetString(0));
        Assert.Equal(fixture.PackageSha256, reader.GetString(1));
        Assert.Equal("static_analyzed", reader.GetString(2));
        Assert.Equal(fixture.CorpusSha256, reader.GetString(3));
        Assert.Equal(fixture.SourceManifestSha256, reader.GetString(4));
        Assert.Equal(fixture.SourceReportSha256, reader.GetString(5));
        Assert.True(fixture.VerifiedAtUtc >= reader.GetFieldValue<DateTimeOffset>(6));
        Assert.False(await reader.ReadAsync());
    }

    private static NpgsqlCommand VerificationCommand(
        TenantPostgresTransaction transaction,
        VerificationFixture fixture,
        byte[] evidence)
    {
        NpgsqlCommand command = transaction.CreateCommand(
            """
            select * from control.record_strategy_version_source_binding(
                @binding_id, @strategy_version_id, @corpus_id,
                @package_sha256, @corpus_sha256, @manifest_sha256,
                @report_sha256, @compiled_sha256, @compiler_sha256,
                @parse_sha256, @compile_sha256, @semantic_sha256,
                @parity_sha256, @demo_sha256, @evidence_content,
                @signature_bytes, @signing_key_id, @verified_at, @audit_id)
            """);
        AddUuid(command, "binding_id", fixture.BindingId);
        AddUuid(command, "strategy_version_id", fixture.StrategyVersionId);
        AddUuid(command, "corpus_id", fixture.CorpusId);
        AddText(command, "package_sha256", fixture.PackageSha256);
        AddText(command, "corpus_sha256", fixture.CorpusSha256);
        AddText(command, "manifest_sha256", fixture.SourceManifestSha256);
        AddText(command, "report_sha256", fixture.SourceReportSha256);
        AddText(command, "compiled_sha256", fixture.CompiledArtifactSha256);
        AddText(command, "compiler_sha256", fixture.CompilerArtifactSha256);
        AddText(command, "parse_sha256", fixture.ParseProofSha256);
        AddText(command, "compile_sha256", fixture.CompileProofSha256);
        AddText(command, "semantic_sha256", fixture.SemanticProofSha256);
        AddText(command, "parity_sha256", fixture.ParityProofSha256);
        AddText(command, "demo_sha256", fixture.DemoProofSha256);
        command.Parameters.AddWithValue("evidence_content", NpgsqlDbType.Bytea, evidence);
        command.Parameters.AddWithValue(
            "signature_bytes",
            NpgsqlDbType.Bytea,
            fixture.VerificationSignature);
        AddText(command, "signing_key_id", fixture.SigningKeyId);
        AddTimestamp(command, "verified_at", fixture.VerifiedAtUtc);
        AddUuid(command, "audit_id", Guid.CreateVersion7());
        return command;
    }

    private static async Task AssertWrongBindingPromotionRejectedAsync(
        PostgresTestDatabase database,
        VerificationFixture fixture)
    {
        await using TenantPostgresTransaction transaction =
            await database.AdminBff.BeginTenantTransactionAsync(fixture.AdminContext);
        await using NpgsqlCommand command = transaction.CreateCommand(
            "select * from control.promote_strategy_version_to_demo_approved(@strategy_id, @binding_id, 0, @audit_id)");
        AddUuid(command, "strategy_id", fixture.StrategyVersionId);
        AddUuid(command, "binding_id", Guid.CreateVersion7());
        AddUuid(command, "audit_id", Guid.CreateVersion7());
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(
            async () => await command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    private static async Task PromoteAsync(
        PostgresTestDatabase database,
        VerificationFixture fixture)
    {
        await using TenantPostgresTransaction transaction =
            await database.AdminBff.BeginTenantTransactionAsync(fixture.AdminContext);
        await using NpgsqlCommand command = transaction.CreateCommand(
            "select * from control.promote_strategy_version_to_demo_approved(@strategy_id, @binding_id, 0, @audit_id)");
        AddUuid(command, "strategy_id", fixture.StrategyVersionId);
        AddUuid(command, "binding_id", fixture.BindingId);
        AddUuid(command, "audit_id", Guid.CreateVersion7());
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("demo_approved", reader.GetString(2));
        Assert.Equal(1L, reader.GetInt64(3));
        await reader.DisposeAsync();
        await transaction.CommitAsync();
    }

    private static async Task<LeaseFixture> SeedRuntimeAuthorityAsync(
        PostgresTestDatabase database,
        VerificationFixture fixture,
        string desiredState)
    {
        DateTimeOffset now = UtcNow();
        string gatewaySha256 = Digest("gateway-artifact");
        string policySha256 = Digest("risk-policy");
        string accountBindingSha256 = Digest("broker-account-binding");
        var claims = new ExecutionLeaseClaims(
                RuntimeContractVersions.ExecutionLeaseV1,
                fixture.LeaseId,
                new ExecutionLeaseBinding(
                    fixture.TenantId,
                    fixture.EntitlementId,
                    fixture.UserId,
                    fixture.DeploymentId,
                    fixture.BrokerAccountId,
                    accountBindingSha256,
                    fixture.StrategyId,
                    fixture.StrategyVersionId,
                    1,
                    fixture.PackageSha256,
                    ExecutionMode.CloudDemo,
                    fixture.RiskPolicyVersionId,
                    policySha256,
                    fixture.WorkerAssignmentId,
                    fixture.WorkerNodeId,
                    fixture.SupervisorWorkloadId,
                    fixture.StrategyHostWorkloadId,
                    fixture.GatewayHostWorkloadId,
                    1,
                    "test-region"),
                now,
                now,
                now.AddMinutes(5),
                now.AddMinutes(10),
                new ExecutionLeaseActionPolicy(
                    LeaseActionClass.Increase | LeaseActionClass.Reduce |
                        LeaseActionClass.Protect | LeaseActionClass.Cancel |
                        LeaseActionClass.EmergencyClose,
                    LeaseActionClass.Reduce | LeaseActionClass.EmergencyClose,
                    LeaseActionClass.None,
                    LeaseActionClass.None));
        using ECDsa leaseSigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] canonicalLeasePayload = ExecutionLeaseCanonicalizer.Serialize(claims);
        byte[] leaseSignature = leaseSigningKey.SignData(
            canonicalLeasePayload,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        var lease = new SignedExecutionLease(
            claims,
            Digest(canonicalLeasePayload),
            P256ExecutionLeaseTrustVerifier.SignatureAlgorithm,
            "execution-lease-key-1",
            Convert.ToBase64String(leaseSignature)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_'));
        byte[] leasePublicKey = leaseSigningKey.ExportSubjectPublicKeyInfo();
        CryptographicOperations.ZeroMemory(canonicalLeasePayload);
        CryptographicOperations.ZeroMemory(leaseSignature);
        byte[] riskSignature = new byte[72];
        riskSignature[0] = 0x30;

        byte[] verificationEvidence = VerificationEvidence(fixture, fixture.SigningKeyId);
        string verificationEvidenceSha256 = Digest(verificationEvidence);
        CryptographicOperations.ZeroMemory(verificationEvidence);
        string verificationSignatureSha256 = Digest(fixture.VerificationSignature);

        await using TenantPostgresTransaction transaction =
            await database.Application.BeginTenantTransactionAsync(fixture.UserContext);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into governance.broker_profiles
                (id, broker_id, profile_version, broker_company, server_name,
                 environment_support, capabilities, cloud_rules, limitations,
                 evidence_sha256, tested_at, state)
            values
                (@profile_id, @broker_id, 1, 'YO4X Test Broker', 'Demo-1',
                 array['demo'], '{"hedging":true}'::jsonb,
                 '{"dedicated":true}'::jsonb, '{}'::jsonb,
                 @profile_evidence, @now, 'approved');

            insert into governance.gateway_artifacts
                (id, vendor_name, vendor_version, sha256, signature_state,
                 quarantine_reference, provenance, licence_evidence,
                 sbom_reference, network_evidence, state, created_at, updated_at)
            values
                (@gateway_id, 'YO4X', 'proof-only', @gateway_sha256, 'valid',
                 'quarantine://gateway/proof-only', '{"build":"verified"}'::jsonb,
                 '{"licence":"test"}'::jsonb, 'sbom://gateway/proof-only',
                 '{"egress":"allowlisted"}'::jsonb, 'approved', @now, @now);

            insert into governance.risk_policy_versions
                (id, tenant_id, policy_id, version_number, normalized_policy,
                 policy_digest, signature_algorithm, signature_bytes,
                 signature_sha256, signing_key_id, state, effective_at,
                 created_at, updated_at)
            values
                (@policy_version_id, @tenant_id, @policy_id, 1,
                 '{"kind":"numeric"}'::jsonb, @policy_sha256,
                 'ECDSA_P256_SHA256_DER', @risk_signature,
                 @risk_signature_sha256, 'risk-key-1', 'active', @now, @now, @now);

            insert into operations.broker_accounts
                (id, tenant_id, user_id, broker_id, broker_profile_id, server,
                 masked_login, binding_fingerprint, environment, account_mode,
                 dedicated_cloud_use, manual_or_external_trading_detected,
                 trading_allowed, broker_hosted_stop_loss,
                 broker_hosted_take_profit, supports_position_query,
                 supports_order_query, supports_deal_history,
                 capability_observed_at, capability_valid_until,
                 capability_evidence_sha256, credential_reference,
                 credential_state, state, created_at, updated_at)
            values
                (@account_id, @tenant_id, @user_id, @broker_id, @profile_id,
                 'Demo-1', '***4242', @account_binding, 'demo', 'hedging',
                 true, false, true, true, true, true, true, true,
                 @now, @now + interval '10 minutes', @account_capability,
                 'vault://yo4x/test-account', 'ready', 'active', @now, @now);

            insert into operations.deployments
                (id, tenant_id, user_id, broker_account_id, strategy_version_id,
                 strategy_source_binding_id,
                 strategy_verification_evidence_sha256,
                 strategy_verification_signature_sha256,
                 strategy_verification_signing_key_id,
                 risk_policy_version_id, risk_policy_digest,
                 gateway_artifact_id, gateway_digest, runtime_digest,
                 strategy_package_digest, region, dedicated_account,
                 hedging_account, broker_hosted_stop_loss,
                 broker_hosted_take_profit, manual_or_external_trading_detected,
                 binding_evidence, binding_evidence_sha256,
                 creation_effective_policy_digest,
                 creation_policy_version_watermark, creation_policy_input_sha256,
                 configuration_sha256, environment, desired_state, observed_state,
                 fence_generation, created_at, updated_at)
            values
                (@deployment_id, @tenant_id, @user_id, @account_id,
                 @strategy_version_id, @binding_id,
                 @verification_evidence_sha256, @verification_signature_sha256,
                 @verification_signing_key_id, @policy_version_id, @policy_sha256,
                 @gateway_id, @gateway_sha256, @runtime_sha256,
                 @package_sha256, 'test-region', true, true, true, true, false,
                 '{"verification":"exact"}'::jsonb, @binding_evidence_sha256,
                 @effective_policy_sha256, @policy_watermark_sha256,
                 @policy_input_sha256, @configuration_sha256, 'demo',
                 @desired_state, @observed_state, @generation, @now, @now);

            insert into operations.worker_nodes
                (id, region, node_name, image_digest, state, capacity,
                 last_heartbeat_at, created_at, updated_at)
            values
                (@worker_id, 'test-region', @worker_name, @runtime_sha256,
                 'ready', '{"slots":1}'::jsonb, @now, @now, @now);

            insert into operations.worker_assignments
                (id, tenant_id, deployment_id, worker_node_id,
                 supervisor_identity, strategy_host_identity,
                 gateway_host_identity, fence_generation, runtime_digest,
                 gateway_artifact_id, state, assigned_at, lease_expires_at)
            values
                (@assignment_id, @tenant_id, @deployment_id, @worker_id,
                 @supervisor_id, @strategy_host_id, @gateway_host_id,
                 @generation, @runtime_sha256, @gateway_id, 'active', @now,
                 @now + interval '10 minutes');

            insert into operations.execution_leases
                (id, tenant_id, entitlement_id, user_id, deployment_id,
                 broker_account_id, broker_binding_sha256, strategy_id,
                 strategy_version_id, strategy_version_number,
                 strategy_package_sha256, execution_mode,
                 risk_policy_version_id, risk_policy_sha256,
                 worker_assignment_id, worker_instance_id,
                 supervisor_workload_id, strategy_host_workload_id,
                 gateway_host_workload_id, region, generation, contract_version,
                 active_actions, grace_actions, expired_actions, revoked_actions,
                 signature_algorithm, signing_key_id, lease_token_sha256,
                  lease_payload_sha256, lease_signature_sha256,
                  signed_envelope, signed_envelope_content, state,
                 issued_at, not_before, expires_at, grace_expires_at,
                 created_at, updated_at)
            values
                (@lease_id, @tenant_id, @entitlement_id, @user_id,
                 @deployment_id, @account_id, @account_binding, @strategy_id,
                 @strategy_version_id, 1, @package_sha256, 'cloud_demo',
                 @policy_version_id, @policy_sha256, @assignment_id, @worker_id,
                 @supervisor_id, @strategy_host_id, @gateway_host_id,
                 'test-region', @generation, @lease_contract_version,
                 @active_actions, @grace_actions, 0, 0, @lease_algorithm,
                 @lease_signing_key, @lease_token_sha256, @lease_payload_sha256,
                  @lease_signature_sha256,
                  convert_from(@signed_envelope_content, 'UTF8')::jsonb,
                  @signed_envelope_content, 'active', @issued_at, @not_before,
                 @expires_at, @grace_expires_at, @now, @now);
            """);
        AddUuid(command, "tenant_id", fixture.TenantId);
        AddUuid(command, "user_id", fixture.UserId);
        AddUuid(command, "profile_id", fixture.BrokerProfileId);
        AddUuid(command, "broker_id", fixture.BrokerId);
        AddUuid(command, "gateway_id", fixture.GatewayArtifactId);
        AddUuid(command, "policy_version_id", fixture.RiskPolicyVersionId);
        AddUuid(command, "policy_id", Guid.CreateVersion7());
        AddUuid(command, "account_id", fixture.BrokerAccountId);
        AddUuid(command, "deployment_id", fixture.DeploymentId);
        AddUuid(command, "strategy_version_id", fixture.StrategyVersionId);
        AddUuid(command, "binding_id", fixture.BindingId);
        AddUuid(command, "worker_id", fixture.WorkerNodeId);
        AddUuid(command, "assignment_id", fixture.WorkerAssignmentId);
        AddUuid(command, "lease_id", fixture.LeaseId);
        AddUuid(command, "entitlement_id", fixture.EntitlementId);
        AddUuid(command, "strategy_id", fixture.StrategyId);
        AddUuid(command, "supervisor_id", fixture.SupervisorWorkloadId);
        AddUuid(command, "strategy_host_id", fixture.StrategyHostWorkloadId);
        AddUuid(command, "gateway_host_id", fixture.GatewayHostWorkloadId);
        AddText(command, "profile_evidence", Digest("profile-evidence"));
        AddText(command, "gateway_sha256", gatewaySha256);
        AddText(command, "policy_sha256", policySha256);
        command.Parameters.AddWithValue("risk_signature", NpgsqlDbType.Bytea, riskSignature);
        AddText(command, "risk_signature_sha256", Digest(riskSignature));
        AddText(command, "account_binding", accountBindingSha256);
        AddText(command, "account_capability", Digest("account-capability"));
        AddText(command, "verification_evidence_sha256", verificationEvidenceSha256);
        AddText(command, "verification_signature_sha256", verificationSignatureSha256);
        AddText(command, "verification_signing_key_id", fixture.SigningKeyId);
        AddText(command, "runtime_sha256", $"sha256:{Digest("runtime-image")}");
        AddText(command, "package_sha256", fixture.PackageSha256);
        AddText(command, "binding_evidence_sha256", Digest("binding-evidence"));
        AddText(command, "effective_policy_sha256", Digest("effective-policy"));
        AddText(command, "policy_watermark_sha256", Digest("policy-watermark"));
        AddText(command, "policy_input_sha256", Digest("policy-input"));
        AddText(command, "configuration_sha256", Digest("configuration"));
        AddText(command, "desired_state", desiredState);
        AddText(command, "observed_state", desiredState == "running" ? "running" : "unknown");
        command.Parameters.AddWithValue("generation", NpgsqlDbType.Bigint, 1L);
        AddText(command, "worker_name", $"worker-{fixture.WorkerNodeId:N}");
        command.Parameters.AddWithValue(
            "lease_contract_version",
            NpgsqlDbType.Integer,
            lease.Claims.ContractVersion);
        command.Parameters.AddWithValue(
            "active_actions",
            NpgsqlDbType.Integer,
            (int)lease.Claims.ActionPolicy.Active);
        command.Parameters.AddWithValue(
            "grace_actions",
            NpgsqlDbType.Integer,
            (int)lease.Claims.ActionPolicy.Grace);
        AddText(command, "lease_algorithm", lease.SignatureAlgorithm);
        AddText(command, "lease_signing_key", lease.SigningKeyId);
        AddText(command, "lease_token_sha256", ExecutionLeaseEnvelopeDigest.Sha256(lease));
        AddText(command, "lease_payload_sha256", lease.PayloadSha256);
        AddText(command, "lease_signature_sha256", ExecutionLeaseEnvelopeDigest.SignatureSha256(lease));
        command.Parameters.AddWithValue(
            "signed_envelope_content",
            NpgsqlDbType.Bytea,
            Encoding.UTF8.GetBytes(CanonicalJson.Serialize(lease)));
        AddTimestamp(command, "issued_at", lease.Claims.IssuedAtUtc);
        AddTimestamp(command, "not_before", lease.Claims.NotBeforeUtc);
        AddTimestamp(command, "expires_at", lease.Claims.ExpiresAtUtc);
        AddTimestamp(command, "grace_expires_at", lease.Claims.GraceExpiresAtUtc);
        AddTimestamp(command, "now", now);
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return new LeaseFixture(lease, leasePublicKey);
    }

    private static async Task RenewExecutionLeaseAsync(
        PostgresTestDatabase database,
        VerificationFixture fixture,
        SignedExecutionLease originalLease)
    {
        DateTimeOffset issuedAt = UtcNow();
        ExecutionLeaseClaims renewedClaims = originalLease.Claims with
        {
            IssuedAtUtc = issuedAt,
            NotBeforeUtc = issuedAt,
            ExpiresAtUtc = issuedAt.AddMinutes(5),
            GraceExpiresAtUtc = issuedAt.AddMinutes(8)
        };
        using ECDsa renewalKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] payload = ExecutionLeaseCanonicalizer.Serialize(renewedClaims);
        byte[] signature = renewalKey.SignData(
            payload,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        var renewedLease = new SignedExecutionLease(
            renewedClaims,
            Digest(payload),
            P256ExecutionLeaseTrustVerifier.SignatureAlgorithm,
            "execution-lease-key-2",
            Convert.ToBase64String(signature)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_'));
        byte[] envelope = Encoding.UTF8.GetBytes(CanonicalJson.Serialize(renewedLease));
        try
        {
            var context = new TenantExecutionContext(
                fixture.TenantId,
                fixture.SupervisorWorkloadId,
                fixture.LeaseId);
            await using TenantPostgresTransaction transaction =
                await database.Worker.BeginTenantTransactionAsync(context);
            await using NpgsqlCommand command = transaction.CreateCommand(
                "select * from control.persist_signed_execution_lease(@content, 0)");
            command.Parameters.AddWithValue("content", NpgsqlDbType.Bytea, envelope);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(fixture.LeaseId, reader.GetGuid(0));
            Assert.Equal(1L, reader.GetInt64(1));
            Assert.True(reader.GetBoolean(3));
            Assert.False(await reader.ReadAsync());
            await reader.DisposeAsync();
            await transaction.CommitAsync();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
            CryptographicOperations.ZeroMemory(signature);
            CryptographicOperations.ZeroMemory(envelope);
        }
    }

    private static BrokerCommandAuthorizationRequest CreateAuthorizationRequest(
        VerificationFixture fixture,
        SignedExecutionLease lease,
        BrokerCommandAction action = BrokerCommandAction.Place,
        long sourceSequence = 12,
        TimeSpan? mustBeginAfter = null)
    {
        DateTimeOffset evaluatedAt = UtcNow();
        DateTimeOffset observedAt = evaluatedAt;
        Guid commandId = Guid.CreateVersion7();
        string gatewaySha256 = Digest("gateway-artifact");
        RiskActionClass riskAction = action switch
        {
            BrokerCommandAction.Place => RiskActionClass.ExposureIncrease,
            BrokerCommandAction.ModifyProtection => RiskActionClass.Protection,
            BrokerCommandAction.Cancel => RiskActionClass.PendingOrderCancellation,
            BrokerCommandAction.Close => RiskActionClass.ExposureReduction,
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        BrokerOrderSide side = action == BrokerCommandAction.Close
            ? BrokerOrderSide.Sell
            : BrokerOrderSide.Buy;
        BrokerOrderType orderType = action == BrokerCommandAction.Cancel
            ? BrokerOrderType.Limit
            : BrokerOrderType.Market;
        BrokerCommandTargetKind? targetKind = action switch
        {
            BrokerCommandAction.Place => null,
            BrokerCommandAction.Cancel => BrokerCommandTargetKind.PendingOrder,
            _ => BrokerCommandTargetKind.Position
        };
        string? targetBrokerId = action switch
        {
            BrokerCommandAction.Place => null,
            BrokerCommandAction.Cancel => "pending-1",
            _ => "position-1"
        };
        decimal? expectedTargetVolume = action == BrokerCommandAction.Place ? null : 0.10m;
        string? expectedTargetStatus = action == BrokerCommandAction.Cancel ? "pending" : null;
        decimal? expectedTargetStopLoss = action == BrokerCommandAction.Place ? null : 1.0800m;
        decimal? expectedTargetTakeProfit = action == BrokerCommandAction.Place ? null : 1.1200m;
        var command = new NormalizedBrokerCommand(
            1,
            commandId,
            Guid.CreateVersion7(),
            fixture.DeploymentId,
            1,
            $"{action.ToString().ToLowerInvariant()}-{commandId:N}",
            action,
            "EURUSD",
            side,
            orderType,
            0.10m,
            null,
            action is BrokerCommandAction.Place or BrokerCommandAction.ModifyProtection
                ? 1.0900m
                : null,
            action == BrokerCommandAction.ModifyProtection ? 1.1250m : null,
            10,
            "yo4x-owned-position",
            targetKind,
            targetBrokerId,
            expectedTargetVolume,
            expectedTargetStatus,
            expectedTargetStopLoss,
            expectedTargetTakeProfit,
            evaluatedAt);
        var account = new BrokerAccountSnapshot(
            11,
            "***4242",
            "YO4X Test Broker",
            "Demo-1",
            YO4X.Trading.Abstractions.BrokerAccountMode.Hedging,
            BrokerEnvironment.Demo,
            BrokerTradingAccess.TradingAllowed,
            "USD",
            10_000m,
            10_000m,
            9_000m,
            observedAt);
        var exposure = new BrokerExposureSnapshotDocument(
            BrokerCommandAuthorizationContractVersions.ExposureSnapshotV1,
            Guid.CreateVersion7(),
            fixture.TenantId,
            fixture.BrokerAccountId,
            fixture.DeploymentId,
            1,
            fixture.WorkerAssignmentId,
            fixture.WorkerNodeId,
            fixture.GatewayArtifactId,
            gatewaySha256,
            "gateway_reconciliation",
            sourceSequence,
            Digest("gateway-exposure-source"),
            observedAt,
            observedAt,
            observedAt,
            observedAt,
            observedAt,
            observedAt,
            observedAt,
            observedAt,
            account,
            [],
            action is BrokerCommandAction.ModifyProtection or BrokerCommandAction.Close
                ?
                [
                    new BrokerPositionSnapshot(
                        "position-1",
                        "EURUSD",
                        BrokerOrderSide.Buy,
                        0.10m,
                        1.1000m,
                        1.0800m,
                        1.1200m,
                        "yo4x-owned-position",
                        observedAt)
                ]
                : [],
            action == BrokerCommandAction.Cancel
                ?
                [
                    new BrokerOrderSnapshot(
                        "pending-1",
                        "EURUSD",
                        BrokerOrderSide.Buy,
                        BrokerOrderType.Limit,
                        0.10m,
                        0.10m,
                        null,
                        1.0800m,
                        1.1200m,
                        "pending",
                        "yo4x-owned-position",
                        observedAt)
                ]
                : [],
            []);
        var riskInput = new NumericRiskEvaluationInput(
            evaluatedAt,
            riskAction,
            new RiskSnapshotTimestamps(
                observedAt,
                observedAt,
                observedAt,
                observedAt,
                observedAt,
                observedAt),
            new MarketRiskSnapshot(1m, 1m, true, true, 0m),
            new AccountRiskSnapshot(
                BrokerAccountEnvironment.Demo,
                YO4X.Risk.BrokerAccountMode.Hedging,
                10_000m,
                true,
                false,
                true),
            new ExposureRiskSnapshot(0.10m, 0m, 0m, 0, 0, 0, observedAt, observedAt),
            new ProtectionRiskSnapshot(true, 100m, false, null, false, false),
            new RiskDayStateSnapshot("2026-08-22", observedAt, 10_000m, 10_000m, 0m, 0m));
        string riskInputSha256 = CanonicalJson.Sha256(riskInput);
        string policySha256 = Digest("risk-policy");
        var riskDecision = new NumericRiskDecision(
            NumericRiskDecisionDisposition.Allowed,
            riskAction,
            policySha256,
            riskInputSha256,
            Digest($"risk-decision-{commandId:N}"),
            "2026-08-22",
            10_000m,
            10_000m,
            0m,
            0m,
            [new NumericRiskRuleResult("risk.input.complete", RiskRuleOutcome.Passed, "true", "true")]);
        byte[] verificationEvidence = VerificationEvidence(fixture, fixture.SigningKeyId);
        string verificationEvidenceSha256 = Digest(verificationEvidence);
        CryptographicOperations.ZeroMemory(verificationEvidence);
        var provenance = new BrokerCommandProvenance(
            fixture.TenantId,
            fixture.BrokerAccountId,
            fixture.StrategyId,
            fixture.StrategyVersionId,
            1,
            fixture.PackageSha256,
            fixture.BindingId,
            fixture.CorpusId,
            fixture.CorpusSha256,
            fixture.SourceManifestSha256,
            fixture.SourceReportSha256,
            fixture.CompiledArtifactSha256,
            fixture.CompilerArtifactSha256,
            fixture.ParseProofSha256,
            fixture.CompileProofSha256,
            fixture.SemanticProofSha256,
            fixture.ParityProofSha256,
            fixture.DemoProofSha256,
            verificationEvidenceSha256,
            Digest(fixture.VerificationSignature),
            P256ExecutionLeaseTrustVerifier.SignatureAlgorithm,
            fixture.SigningKeyId,
            fixture.VerifierWorkloadId,
            fixture.VerifiedAtUtc,
            true,
            fixture.GatewayArtifactId,
            gatewaySha256);
        return new BrokerCommandAuthorizationRequest(
            command,
            provenance,
            exposure,
            riskInput,
            riskDecision,
            lease,
            new ExecutionSafetyAuthorization(Digest("[]"), 0),
            new BrokerReconciliationCommitmentDocument(
                BrokerCommandAuthorizationContractVersions.ReconciliationV1,
                commandId,
                "orders_positions_deals",
                Digest($"reconciliation-scope-{commandId:N}"),
                evaluatedAt.Add(mustBeginAfter ?? TimeSpan.FromMinutes(1)),
                evaluatedAt.AddMinutes(2)),
            Guid.CreateVersion7(),
            Guid.CreateVersion7());
    }

    private static async Task AssertDurableRowsAsync(
        PostgresTestDatabase database,
        VerificationFixture fixture,
        Guid commandId)
    {
        await using NpgsqlConnection connection = await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select broker_command.state,
                (select count(*) from operations.broker_exposure_snapshots
                 where tenant_id = @tenant_id and deployment_id = @deployment_id),
                (select count(*) from operations.broker_command_risk_decisions
                 where tenant_id = @tenant_id and deployment_id = @deployment_id),
                (select count(*) from operations.broker_command_reconciliations
                 where tenant_id = @tenant_id and command_id = @command_id),
                (select count(*) from audit.audit_events
                 where tenant_id = @tenant_id and target_id = @command_id::text)
            from operations.broker_commands as broker_command
            where broker_command.tenant_id = @tenant_id
              and broker_command.id = @command_id
            """,
            connection);
        AddUuid(command, "tenant_id", fixture.TenantId);
        AddUuid(command, "deployment_id", fixture.DeploymentId);
        AddUuid(command, "command_id", commandId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("unknown", reader.GetString(0));
        Assert.Equal(1L, reader.GetInt64(1));
        Assert.Equal(1L, reader.GetInt64(2));
        Assert.Equal(2L, reader.GetInt64(3));
        Assert.Equal(7L, reader.GetInt64(4));
    }

    private static async Task AssertRawAuthorizerTableReadRejectedAsync(
        PostgresTestDatabase database,
        TenantExecutionContext context)
    {
        await using TenantPostgresTransaction transaction =
            await database.TradeAuthorizer.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            "select count(*) from operations.broker_commands");
        PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
            async () => await command.ExecuteScalarAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, rejected.SqlState);
    }

    private static async Task SuspendStrategyAsync(
        PostgresTestDatabase database,
        VerificationFixture fixture)
    {
        await using TenantPostgresTransaction transaction =
            await database.AdminBff.BeginTenantTransactionAsync(fixture.AdminContext);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            update governance.strategy_versions
            set state = 'suspended', row_version = row_version + 1,
                updated_at = clock_timestamp()
            where tenant_id = @tenant_id and id = @strategy_id
            """);
        AddUuid(command, "tenant_id", fixture.TenantId);
        AddUuid(command, "strategy_id", fixture.StrategyVersionId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        await transaction.CommitAsync();
    }

    private static byte[] VerificationEvidence(
        VerificationFixture fixture,
        string signingKeyId) =>
        Encoding.UTF8.GetBytes(CanonicalJson.Serialize(new
        {
            contractVersion = 1,
            strategyVersionId = fixture.StrategyVersionId,
            strategyPackageSha256 = fixture.PackageSha256,
            sourceCorpusId = fixture.CorpusId,
            sourceCorpusSha256 = fixture.CorpusSha256,
            sourceManifestSha256 = fixture.SourceManifestSha256,
            sourceReportSha256 = fixture.SourceReportSha256,
            compiledArtifactSha256 = fixture.CompiledArtifactSha256,
            compilerArtifactSha256 = fixture.CompilerArtifactSha256,
            parseTypecheckProofSha256 = fixture.ParseProofSha256,
            compileProofSha256 = fixture.CompileProofSha256,
            semanticConversionProofSha256 = fixture.SemanticProofSha256,
            referenceParityProofSha256 = fixture.ParityProofSha256,
            demoRuntimeProofSha256 = fixture.DemoProofSha256,
            verifiedByWorkloadId = fixture.VerifierWorkloadId,
            verificationSignatureAlgorithm = P256ExecutionLeaseTrustVerifier.SignatureAlgorithm,
            verificationSigningKeyId = signingKeyId,
            signatureCryptographicallyVerified = true,
            parsedAndTypeChecked = true,
            metaEditorCompileProven = true,
            semanticConversionProven = true,
            referenceParityProven = true,
            demoRuntimeProven = true
        }));

    private static DateTimeOffset UtcNow() =>
        DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    private static async Task<DateTimeOffset> ReadDatabaseClockAsync(
        PostgresTestDatabase database)
    {
        await using NpgsqlConnection connection =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("select clock_timestamp()", connection);
        object? scalar = await command.ExecuteScalarAsync();
        Assert.NotNull(scalar);
        object value = scalar;
        return value switch
        {
            DateTimeOffset dateTimeOffset => dateTimeOffset,
            DateTime dateTime => new DateTimeOffset(
                DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException(
                $"PostgreSQL returned an unsupported clock type '{value.GetType().FullName}'.")
        };
    }

    private static string Digest(string value) => Digest(Encoding.UTF8.GetBytes(value));

    private static string Digest(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static string ReverseRootPropertyOrder(string canonicalJson)
    {
        JsonObject source = JsonNode.Parse(canonicalJson)!.AsObject();
        var reordered = new JsonObject();
        foreach ((string name, JsonNode? value) in source.Reverse())
        {
            reordered.Add(name, value?.DeepClone());
        }

        return reordered.ToJsonString();
    }

    private static string ReplaceExactly(string source, string oldValue, string newValue)
    {
        int firstIndex = source.IndexOf(oldValue, StringComparison.Ordinal);
        Assert.True(firstIndex >= 0, $"Canonical JSON omitted marker {oldValue}.");
        Assert.Equal(
            -1,
            source.IndexOf(oldValue, firstIndex + oldValue.Length, StringComparison.Ordinal));
        return source.Replace(oldValue, newValue, StringComparison.Ordinal);
    }

    private static async Task<long> ExecuteGatewayCountAsync(
        PostgresTestDatabase database,
        TenantExecutionContext context,
        string sql,
        Action<NpgsqlCommand> bind)
    {
        await using TenantPostgresTransaction transaction =
            await database.GatewayRuntime.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(sql);
        bind(command);
        long count = Convert.ToInt64(
            await command.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture);
        await transaction.CommitAsync();
        return count;
    }

    private static async Task<string> ReadBrokerCommandStateAsync(
        PostgresTestDatabase database,
        Guid commandId)
    {
        await using NpgsqlConnection connection =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "select state from operations.broker_commands where id = @command_id",
            connection);
        AddUuid(command, "command_id", commandId);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task AssertPendingDispatchHasNoResultAsync(
        PostgresTestDatabase database,
        Guid commandId)
    {
        await using NpgsqlConnection connection =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select state, send_result is null, send_completed_at is null
            from operations.broker_commands
            where id = @command_id
            """,
            connection);
        AddUuid(command, "command_id", commandId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("send_in_progress", reader.GetString(0));
        Assert.True(reader.GetBoolean(1));
        Assert.True(reader.GetBoolean(2));
        Assert.False(await reader.ReadAsync());
    }

    private static async Task AssertPendingReconciliationHasNoEvidenceAsync(
        PostgresTestDatabase database,
        Guid commandId)
    {
        await using NpgsqlConnection connection =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select broker_command.state,
                   broker_command.reconciliation_result_sha256 is null,
                   broker_command.reconciliation_completed_at is null,
                   (select count(*)
                    from operations.broker_command_reconciliations as evidence
                    where evidence.command_id = broker_command.id)
            from operations.broker_commands as broker_command
            where broker_command.id = @command_id
            """,
            connection);
        AddUuid(command, "command_id", commandId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("reconciliation_pending", reader.GetString(0));
        Assert.True(reader.GetBoolean(1));
        Assert.True(reader.GetBoolean(2));
        Assert.Equal(0, reader.GetInt64(3));
        Assert.False(await reader.ReadAsync());
    }

    private static async Task<long> ReadLifecycleEvidenceCountAsync(
        PostgresTestDatabase database,
        Guid commandId,
        string action)
    {
        await using NpgsqlConnection connection =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select count(*)
            from audit.audit_events
            where target_type = 'broker_command'
              and target_id = @command_id
              and action = @action
            """,
            connection);
        AddText(command, "command_id", commandId.ToString());
        AddText(command, "action", action);
        return Assert.IsType<long>(await command.ExecuteScalarAsync());
    }

    private static async Task WaitUntilDatabaseTimeAfterAsync(
        PostgresTestDatabase database,
        DateTimeOffset expiresAtUtc)
    {
        await using NpgsqlConnection connection =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select pg_sleep(greatest(
                0::double precision,
                extract(epoch from (@expires_at - clock_timestamp()))::double precision
                    + 0.05))
            """,
            connection)
        {
            CommandTimeout = 5
        };
        AddTimestamp(command, "expires_at", expiresAtUtc);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertGatewayInvalidParameterAsync(
        PostgresTestDatabase database,
        TenantExecutionContext context,
        string sql,
        Action<NpgsqlCommand> bind)
    {
        await using TenantPostgresTransaction transaction =
            await database.GatewayRuntime.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(sql);
        bind(command);
        PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await command.ExecuteScalarAsync();
        });
        Assert.Equal(PostgresErrorCodes.InvalidParameterValue, rejected.SqlState);
    }

    private static void BindRawSubmission(
        NpgsqlCommand command,
        Guid commandId,
        string? authorizationSha256,
        Guid claimToken,
        GatewaySendResult submission,
        byte[] content)
    {
        AddUuid(command, "command_id", commandId);
        AddNullableText(command, "authorization_sha256", authorizationSha256);
        AddUuid(command, "claim_token", claimToken);
        string disposition = submission.Disposition switch
        {
            GatewayCommandDisposition.Accepted => "accepted",
            GatewayCommandDisposition.Rejected => "rejected",
            GatewayCommandDisposition.Unknown => "unknown",
            GatewayCommandDisposition.SubmissionDisabled => "submission_disabled",
            _ => throw new ArgumentOutOfRangeException(nameof(submission))
        };
        AddText(command, "disposition", disposition);
        command.Parameters.AddWithValue(
            "pre_invocation_not_sent_proven",
            NpgsqlDbType.Boolean,
            submission.PreInvocationNotSentProven);
        AddText(command, "result_code", submission.Code);
        AddNullableText(command, "broker_request_id", submission.BrokerRequestId);
        AddNullableText(command, "broker_order_id", submission.OrderId);
        AddNullableText(command, "broker_deal_id", submission.DealId);
        command.Parameters.AddWithValue("result_content", NpgsqlDbType.Bytea, content);
        AddTimestamp(command, "observed_at", submission.ObservedAtUtc);
        AddUuid(command, "audit_event_id", Guid.CreateVersion7());
    }

    private static void BindRawReconciliation(
        NpgsqlCommand command,
        BrokerCommandReconciliationEvidenceDocument evidence,
        string? authorizationSha256,
        Guid claimToken,
        Guid reconciliationId,
        byte[] content)
    {
        AddUuid(command, "command_id", evidence.CommandId);
        AddNullableText(command, "authorization_sha256", authorizationSha256);
        AddUuid(command, "claim_token", claimToken);
        AddUuid(command, "reconciliation_id", reconciliationId);
        AddText(command, "match", evidence.Match);
        AddText(command, "reason_code", evidence.ReasonCode);
        AddText(command, "source_evidence_sha256", evidence.SourceEvidenceSha256);
        command.Parameters.AddWithValue("result_content", NpgsqlDbType.Bytea, content);
        AddNullableText(command, "broker_order_id", evidence.OrderId);
        AddNullableText(command, "broker_deal_id", evidence.DealId);
        AddTimestamp(command, "observed_at", evidence.ObservedAtUtc);
        AddUuid(command, "audit_event_id", Guid.CreateVersion7());
    }

    private static void AddUuid(NpgsqlCommand command, string name, Guid value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Uuid, value);

    private static void AddText(NpgsqlCommand command, string name, string value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Text, value);

    private static void AddNullableText(NpgsqlCommand command, string name, string? value) =>
        command.Parameters.AddWithValue(
            name,
            NpgsqlDbType.Text,
            value is null ? DBNull.Value : value);

    private static void AddTimestamp(
        NpgsqlCommand command,
        string name,
        DateTimeOffset value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.TimestampTz, value.ToUniversalTime());

    private sealed class RecordingLifecycleStore(
        YO4X.Trading.Application.IBrokerCommandLifecycleStore inner)
        : YO4X.Trading.Application.IBrokerCommandLifecycleStore
    {
        public Exception? LastSubmissionException { get; private set; }

        public Task<YO4X.Trading.Application.BrokerCommandDispatchClaim> ClaimForDispatchAsync(
            TenantExecutionContext context,
            YO4X.Trading.Application.BrokerCommandReference reference,
            Guid claimToken,
            Guid auditEventId,
            CancellationToken cancellationToken) =>
            inner.ClaimForDispatchAsync(
                context,
                reference,
                claimToken,
                auditEventId,
                cancellationToken);

        public async Task<YO4X.Trading.Application.BrokerCommandLifecycleReceipt>
            RecordSubmissionAsync(
                TenantExecutionContext context,
                YO4X.Trading.Application.BrokerCommandDispatchClaim claim,
                GatewaySendResult result,
                Guid auditEventId,
                CancellationToken cancellationToken)
        {
            try
            {
                return await inner.RecordSubmissionAsync(
                    context,
                    claim,
                    result,
                    auditEventId,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                LastSubmissionException = exception;
                throw;
            }
        }

        public Task<YO4X.Trading.Application.BrokerCommandLifecycleReceipt?>
            RecoverExpiredLifecycleAsync(
                TenantExecutionContext context,
                Guid commandId,
                string authorizationSha256,
                Guid auditEventId,
                CancellationToken cancellationToken) =>
            inner.RecoverExpiredLifecycleAsync(
                context,
                commandId,
                authorizationSha256,
                auditEventId,
                cancellationToken);

        public Task<YO4X.Trading.Application.BrokerCommandReconciliationClaim>
            BeginReconciliationAsync(
                TenantExecutionContext context,
                Guid commandId,
                string authorizationSha256,
                Guid reconciliationClaimToken,
                Guid auditEventId,
                CancellationToken cancellationToken) =>
            inner.BeginReconciliationAsync(
                context,
                commandId,
                authorizationSha256,
                reconciliationClaimToken,
                auditEventId,
                cancellationToken);

        public Task<YO4X.Trading.Application.BrokerCommandLifecycleReceipt>
            CompleteReconciliationAsync(
                TenantExecutionContext context,
                Guid reconciliationClaimToken,
                Guid reconciliationId,
                YO4X.Trading.Application.ValidatedBrokerCommandReconciliation evidence,
                Guid auditEventId,
                CancellationToken cancellationToken) =>
            inner.CompleteReconciliationAsync(
                context,
                reconciliationClaimToken,
                reconciliationId,
                evidence,
                auditEventId,
                cancellationToken);
    }

    private sealed record RawAuthorizationDocuments(
        string Command,
        string Exposure,
        string RiskInput,
        string RiskDecision,
        string Reconciliation,
        string Authorization);

    private sealed record VerificationFixture(
        Guid TenantId,
        Guid UserId,
        Guid StrategyId,
        Guid StrategyVersionId,
        Guid BindingId,
        Guid CorpusId,
        string PackageSha256,
        string CorpusSha256,
        string SourceManifestSha256,
        string SourceReportSha256,
        string CompiledArtifactSha256,
        string CompilerArtifactSha256,
        string ParseProofSha256,
        string CompileProofSha256,
        string SemanticProofSha256,
        string ParityProofSha256,
        string DemoProofSha256,
        Guid VerifierWorkloadId,
        string SigningKeyId,
        byte[] VerificationSignature,
        DateTimeOffset VerifiedAtUtc,
        Guid BrokerId,
        Guid BrokerProfileId,
        Guid GatewayArtifactId,
        Guid BrokerAccountId,
        Guid RiskPolicyVersionId,
        Guid DeploymentId,
        Guid WorkerNodeId,
        Guid WorkerAssignmentId,
        Guid EntitlementId,
        Guid LeaseId,
        Guid SupervisorWorkloadId,
        Guid StrategyHostWorkloadId,
        Guid GatewayHostWorkloadId)
    {
        public TenantExecutionContext UserContext => new(
            TenantId,
            UserId,
            Guid.CreateVersion7());

        public TenantExecutionContext VerifierContext => new(
            TenantId,
            VerifierWorkloadId,
            BindingId);

        public TenantExecutionContext AdminContext => new(
            TenantId,
            Guid.Parse("27b342e1-2ea3-46fd-8700-ff7de98cb3c6"),
            Guid.CreateVersion7());
    }

    private sealed record LeaseFixture(
        SignedExecutionLease Lease,
        byte[] SubjectPublicKeyInfo);

    private sealed class IntegrationEntitlementProvider(
        Guid entitlementId,
        ExecutionLeaseActionPolicy actionPolicy) : IExecutionEntitlementProvider
    {
        public ValueTask<ExecutionEntitlementGrant?> ResolveAsync(
            ExecutionEntitlementRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ExecutionEntitlementGrant?>(new(
                entitlementId,
                request.RequestedAtUtc,
                request.RequestedAtUtc.AddMinutes(5),
                actionPolicy));
        }
    }

    private sealed class IntegrationLeaseSigningProvider :
        IExecutionLeaseSigningProvider,
        IDisposable
    {
        private readonly ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public ValueTask<ExecutionLeaseSignature> SignAsync(
            ReadOnlyMemory<byte> canonicalLeasePayload,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] signature = signingKey.SignData(
                canonicalLeasePayload.Span,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence);
            try
            {
                return ValueTask.FromResult(new ExecutionLeaseSignature(
                    P256ExecutionLeaseTrustVerifier.SignatureAlgorithm,
                    "integration-runtime-renewal-key",
                    Convert.ToBase64String(signature)
                        .TrimEnd('=')
                        .Replace('+', '-')
                        .Replace('/', '_')));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(signature);
            }
        }

        public void Dispose() => signingKey.Dispose();
    }
}
