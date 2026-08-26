using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Workers.Operations;
using YO4X.ControlPlane.Workers.Outbox;
using YO4X.Persistence.Postgres;
using YO4X.RuntimeControl.Postgres;
using YO4X.Tenancy;

namespace YO4X.Postgres.IntegrationTests;

public sealed partial class PostgresFoundationTests
{
    [PostgresFact]
    public async Task ReconciliationChallengeRetiresOnlyPendingOriginalAndBuildsCanonicalEnvelope()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        TenantExecutionContext ownerContext = NewContext();
        CredentialBoundaryFixture accounts = await SeedCredentialBoundaryFixtureAsync(
            database.Application,
            ownerContext);
        BrokerOperationFixture operation = await SeedConfirmedBrokerOperationFixtureAsync(
            database,
            ownerContext,
            accounts.RotateAccountId,
            seedResults: false);

        ChallengeIssueReceipt? stillAuthorized = await IssueChallengeAsync(
            database,
            ownerContext.TenantId,
            operation,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            RandomResultCapability());
        Assert.Null(stillAuthorized);
        Assert.Equal(0L, await CountChallengesAsync(database, operation.RotateOperationId));

        await CloseOriginalResultAuthorityAsync(
            database,
            operation.RotateOperationId);
        await SetOriginalOutboxStateAsync(
            database,
            operation.RotateDispatchId,
            state: "processing");

        ChallengeIssueReceipt? ambiguousDelivery = await IssueChallengeAsync(
            database,
            ownerContext.TenantId,
            operation,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            RandomResultCapability());
        Assert.Null(ambiguousDelivery);
        Assert.Equal(
            ("processing", 0L, 0L),
            await ReadChallengeSideEffectsAsync(
                database,
                operation.RotateOperationId,
                operation.RotateDispatchId));

        await SetOriginalOutboxStateAsync(
            database,
            operation.RotateDispatchId,
            state: "pending");
        ChallengeRoute replacement = await SeedReplacementAssignmentAsync(
            database,
            ownerContext.TenantId,
            operation);
        Guid challengeId = Guid.CreateVersion7();
        Guid messageId = Guid.CreateVersion7();
        Guid auditId = Guid.CreateVersion7();
        string rawCapability = RandomResultCapability();
        ChallengeIssueReceipt issued = Assert.IsType<ChallengeIssueReceipt>(
            await IssueChallengeAsync(
                database,
                ownerContext.TenantId,
                operation,
                challengeId,
                messageId,
                auditId,
                rawCapability));

        Assert.Equal("issued", issued.Status);
        Assert.Equal(challengeId, issued.ChallengeId);
        Assert.Equal(messageId, issued.MessageId);
        Assert.Equal(replacement.DeploymentId, issued.RouteDeploymentId);
        Assert.Equal(replacement.FenceGeneration, issued.FenceGeneration);
        Assert.Equal(replacement.AssignmentId, issued.AssignmentId);
        Assert.Equal(replacement.WorkerInstanceId, issued.WorkerInstanceId);
        Assert.True(issued.IssuedAt < issued.ExpiresAt);
        Assert.True(issued.ExpiresAt - issued.IssuedAt <= TimeSpan.FromHours(24));
        Assert.Equal(
            ("dead_letter", 1L, 1L),
            await ReadChallengeSideEffectsAsync(
                database,
                operation.RotateOperationId,
                operation.RotateDispatchId));

        ChallengeIssueReceipt outstanding = Assert.IsType<ChallengeIssueReceipt>(
            await IssueChallengeAsync(
                database,
                ownerContext.TenantId,
                operation,
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                RandomResultCapability()));
        Assert.Equal("outstanding", outstanding.Status);
        Assert.Equal(issued with { Status = "outstanding" }, outstanding);
        Assert.Equal(1L, await CountChallengesAsync(database, operation.RotateOperationId));

        ChallengeIssueReceipt duplicate = Assert.IsType<ChallengeIssueReceipt>(
            await IssueChallengeAsync(
                database,
                ownerContext.TenantId,
                operation,
                challengeId,
                messageId,
                auditId,
                rawCapability));
        Assert.Equal("duplicate", duplicate.Status);
        Assert.Equal(issued with { Status = "duplicate" }, duplicate);

        ChallengeOutboxRow outbox = await ReadChallengeOutboxAsync(database, messageId);
        Assert.Equal("yo4x.user-operation.reconciliation-requested.v2", outbox.MessageType);
        var claimed = new ClaimedOutboxItem(
            messageId,
            ownerContext.TenantId,
            outbox.MessageType,
            schemaVersion: 1,
            outbox.PayloadJson,
            outbox.PayloadSha256,
            outbox.OccurredAt,
            attempt: 1);
        OutboxDeliveryEnvelope envelope = OutboxDeliveryEnvelope.Create(claimed);
        Assert.Equal(outbox.PayloadSha256, envelope.PayloadSha256);
        Assert.Equal(
            "yo4x.user-operation.reconciliation-requested.v2",
            envelope.MessageType);

        using JsonDocument payload = JsonDocument.Parse(envelope.PayloadJson);
        JsonElement root = payload.RootElement;
        Assert.Equal(2, root.GetProperty("contractVersion").GetInt32());
        Assert.True(root.GetProperty("reconciliationOnly").GetBoolean());
        Assert.Equal(challengeId, root.GetProperty("challengeId").GetGuid());
        Assert.Equal(messageId, root.GetProperty("challengeMessageId").GetGuid());
        Assert.Equal(
            operation.RotateDispatchId,
            root.GetProperty("originalDispatchMessageId").GetGuid());
        Assert.Equal(rawCapability, root.GetProperty("resultCapability").GetString());
        Assert.Equal(replacement.FenceGeneration, root.GetProperty("fenceGeneration").GetInt64());
        Assert.Equal(replacement.AssignmentId, root.GetProperty("workerAssignmentId").GetGuid());
        Assert.Equal(
            replacement.WorkerInstanceId,
            root.GetProperty("workerInstanceId").GetGuid());

        await AssertChallengeSecretBoundaryAsync(
            database,
            challengeId,
            auditId,
            rawCapability);
    }

    [PostgresFact]
    public async Task ReconciliationChallengeAuthenticatesNewFenceAndPreservesOriginalDispatchProof()
    {
        // Sanctioned recording path: yo4x_runtime_evidence retains EXECUTE only
        // on control.record_user_operation_result_v5; the legacy
        // record_broker_user_operation_result capability is revoked
        // (least_privilege_roles.sql). The fence therefore runs end-to-end on
        // the invocation-v4 protocol: ambiguity challenge issued by the worker,
        // result recorded by the runtime-evidence role.
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        InvocationProtocolFixture fixture = await SeedInvocationProtocolFixtureAsync(database);

        DeliveryClaimReceipt claim = await ClaimInvocationDeliveryAsync(database, fixture);
        // Open a deliberately short invocation-authority window: the attempt
        // guard only admits mutations through protocol functions advancing the
        // state machine, so the reconciliation challenge must wait for the
        // receipt deadline to lapse naturally instead of backdating evidence.
        await BeginInvocationWithBoundaryAuthorityWindowAsync(database, fixture, claim);
        await AuthorizeProviderCallAsync(database, fixture);
        ChallengeRoute replacement = await SeedReplacementAssignmentAsync(
            database,
            fixture.TenantId,
            fixture.DeploymentId,
            fixture.WorkerAssignmentId,
            fixture.WorkerInstanceId);
        (Guid claimToken, long claimRowVersion) =
            await ClaimOperationReconcilingForBoundaryTestAsync(database, fixture);
        await AwaitInvocationAuthorityClosedForBoundaryTestAsync(database, fixture);

        Guid challengeId = Guid.CreateVersion7();
        Guid messageId = Guid.CreateVersion7();
        Guid auditId = Guid.CreateVersion7();
        string rawCapability = RandomResultCapability();
        InvocationChallengeReceipt challenge = Assert.IsType<InvocationChallengeReceipt>(
            await IssueInvocationChallengeAsync(
                database,
                fixture,
                claimToken,
                claimRowVersion,
                challengeId,
                messageId,
                auditId,
                rawCapability,
                TimeSpan.FromHours(24)));

        Assert.Equal("issued", challenge.Status);
        Assert.Equal(challengeId, challenge.ChallengeId);
        Assert.Equal(messageId, challenge.MessageId);
        Assert.Equal(replacement.DeploymentId, challenge.RouteDeploymentId);
        Assert.Equal(replacement.FenceGeneration, challenge.FenceGeneration);
        Assert.Equal(replacement.AssignmentId, challenge.AssignmentId);
        Assert.Equal(replacement.WorkerInstanceId, challenge.WorkerInstanceId);
        DateTimeOffset replacementLeaseExpiresAt = await ReadAssignmentLeaseExpiresAtAsync(
            database,
            replacement.AssignmentId);
        Assert.True(challenge.IssuedAt < challenge.ExpiresAt);
        Assert.Equal(replacementLeaseExpiresAt, challenge.ExpiresAt);

        BrokerResultCapabilityReceipt? atChallengeExpiry =
            await RecordChallengeResultV5Async(
                database,
                fixture,
                replacement,
                challenge.ChallengeId,
                challenge.MessageId,
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                rawCapability,
                "succeeded",
                challenge.ExpiresAt,
                replacement.SupervisorId);
        Assert.Null(atChallengeExpiry);
        Assert.Equal(0L, await CountInvocationResultsAsync(database, fixture));

        DateTimeOffset revokedAt = challenge.IssuedAt.AddMinutes(1);
        await SetAssignmentRevocationForBoundaryTestAsync(
            database,
            replacement.AssignmentId,
            revokedAt);
        BrokerResultCapabilityReceipt? atRevocation =
            await RecordChallengeResultV5Async(
                database,
                fixture,
                replacement,
                challenge.ChallengeId,
                challenge.MessageId,
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                rawCapability,
                "succeeded",
                revokedAt,
                replacement.SupervisorId);
        Assert.Null(atRevocation);
        Assert.Equal(0L, await CountInvocationResultsAsync(database, fixture));
        DateTimeOffset observedAt = challenge.IssuedAt.AddTicks(10);

        // A supervisor identity bound to another route never satisfies the v5
        // workload binding: insufficient privilege, before any idempotency.
        PostgresException wrongRouteActor = await Assert.ThrowsAsync<PostgresException>(
            () => RecordChallengeResultV5Async(
                database,
                fixture,
                replacement,
                challenge.ChallengeId,
                challenge.MessageId,
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                rawCapability,
                "succeeded",
                observedAt,
                fixture.SupervisorActorId));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, wrongRouteActor.SqlState);

        // Outcome validation lives inside the sanctioned function: 'failed' is
        // not an observable v5 outcome and fails closed with invalid_parameter.
        PostgresException nonObservation = await Assert.ThrowsAsync<PostgresException>(
            () => RecordChallengeResultV5Async(
                database,
                fixture,
                replacement,
                challenge.ChallengeId,
                challenge.MessageId,
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                rawCapability,
                "failed",
                observedAt,
                replacement.SupervisorId));
        Assert.Equal(PostgresErrorCodes.InvalidParameterValue, nonObservation.SqlState);
        Assert.Equal(0L, await CountInvocationResultsAsync(database, fixture));

        Guid resultId = Guid.CreateVersion7();
        Guid consumptionId = Guid.CreateVersion7();
        BrokerResultCapabilityReceipt accepted = Assert.IsType<BrokerResultCapabilityReceipt>(
            await RecordChallengeResultV5Async(
                database,
                fixture,
                replacement,
                challenge.ChallengeId,
                challenge.MessageId,
                consumptionId,
                resultId,
                rawCapability,
                "succeeded",
                observedAt,
                replacement.SupervisorId));
        Assert.Equal("accepted", accepted.Status);
        Assert.Equal(1L, await CountInvocationResultsAsync(database, fixture));

        await AssertInvocationChallengeSecretBoundaryAsync(
            database,
            fixture.TenantId,
            challenge.ChallengeId,
            auditId,
            rawCapability);

        // The persisted result keeps the ORIGINAL dispatch message id while
        // carrying the replacement route: the dispatch proof survives the
        // reconciliation handoff.
        ChallengeProofBinding binding = await ReadChallengeProofBindingAsync(database, fixture);
        Assert.Equal(fixture.DispatchMessageId, binding.OriginalDispatchMessageId);
        Assert.Equal(challenge.ChallengeId, binding.ChallengeId);
        Assert.Equal(replacement.DeploymentId, binding.ChallengeDeploymentId);
        Assert.Equal(replacement.FenceGeneration, binding.ChallengeFenceGeneration);
        Assert.Equal(replacement.AssignmentId, binding.ChallengeAssignmentId);
        Assert.Equal(replacement.WorkerInstanceId, binding.ChallengeWorkerInstanceId);
        Assert.Equal(accepted.ResultRecordId, binding.ConsumedResultRecordId);
        Assert.Equal(resultId, binding.ConsumedResultId);
        Assert.Matches("^[0-9a-f]{64}$", binding.ConsumedRequestSha256);
        Assert.Equal(
            Sha256Hex(Encoding.UTF8.GetBytes(rawCapability)),
            binding.StoredOriginalCapabilitySha256);
        Assert.NotEqual(
            binding.StoredOriginalCapabilitySha256,
            Sha256Hex(Encoding.UTF8.GetBytes(fixture.ResultCapability)));

        await ExpireChallengeForBoundaryTestAsync(database, challenge.ChallengeId);
        BrokerResultCapabilityReceipt replay = Assert.IsType<BrokerResultCapabilityReceipt>(
            await RecordChallengeResultV5Async(
                database,
                fixture,
                replacement,
                challenge.ChallengeId,
                challenge.MessageId,
                consumptionId,
                resultId,
                rawCapability,
                "succeeded",
                observedAt,
                replacement.SupervisorId));
        Assert.Equal("duplicate", replay.Status);
        Assert.Equal(accepted.ResultRecordId, replay.ResultRecordId);
        Assert.Equal(accepted.ReceivedAt, replay.ReceivedAt);

        PostgresException oldActorReplay = await Assert.ThrowsAsync<PostgresException>(
            () => RecordChallengeResultV5Async(
                database,
                fixture,
                replacement,
                challenge.ChallengeId,
                challenge.MessageId,
                consumptionId,
                resultId,
                rawCapability,
                "succeeded",
                observedAt,
                fixture.SupervisorActorId));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, oldActorReplay.SqlState);
    }

    [PostgresFact]
    public async Task DeploymentChallengeRejectsExactExpiryLeaseAndRevocationBoundaries()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        TenantExecutionContext ownerContext = NewContext();
        CredentialBoundaryFixture accounts = await SeedCredentialBoundaryFixtureAsync(
            database.Application,
            ownerContext);
        BrokerOperationFixture route = await SeedConfirmedBrokerOperationFixtureAsync(
            database,
            ownerContext,
            accounts.RotateAccountId,
            seedResults: false);
        Guid sessionId = await ReadOperationSessionIdAsync(
            database,
            route.RotateOperationId);
        var operationContext = new TenantExecutionContext(
            ownerContext.TenantId,
            ownerContext.ActorId,
            ownerContext.CorrelationId,
            sessionId);
        DeploymentOperationResultFixture operation =
            await SeedDeploymentOperationResultFixtureAsync(database, operationContext, route);
        await CloseOriginalResultAuthorityAsync(database, operation.OperationId);
        ChallengeRoute replacement = await SeedReplacementAssignmentAsync(
            database,
            ownerContext.TenantId,
            route,
            operation.SupervisorWorkloadId);
        string rawCapability = RandomResultCapability();
        ChallengeIssueReceipt challenge = Assert.IsType<ChallengeIssueReceipt>(
            await IssueChallengeAsync(
                database,
                ownerContext.TenantId,
                operation.OperationId,
                operation.CorrelationId,
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                rawCapability,
                TimeSpan.FromHours(24)));
        Assert.Equal(
            await ReadAssignmentLeaseExpiresAtAsync(database, replacement.AssignmentId),
            challenge.ExpiresAt);

        await using var runtimeEvidence = new PostgresDatabase(
            database.RuntimeEvidenceConnectionString,
            PostgresDatabaseUsage.Runtime,
            database.TenantContextCapabilityProvider,
            allowInsecureLoopbackForDevelopment: true);
        PostgresException atChallengeExpiry = await Assert.ThrowsAsync<PostgresException>(
            () => RecordDeploymentResultCapabilityAsync(
                runtimeEvidence,
                ownerContext.TenantId,
                operation,
                Guid.CreateVersion7(),
                operation.ResultId,
                rawCapability,
                RandomHexDigest(),
                observedAt: challenge.ExpiresAt));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, atChallengeExpiry.SqlState);

        DateTimeOffset revokedAt = challenge.IssuedAt.AddMinutes(1);
        await SetAssignmentRevocationForBoundaryTestAsync(
            database,
            replacement.AssignmentId,
            revokedAt);
        PostgresException atRevocation = await Assert.ThrowsAsync<PostgresException>(
            () => RecordDeploymentResultCapabilityAsync(
                runtimeEvidence,
                ownerContext.TenantId,
                operation,
                Guid.CreateVersion7(),
                operation.ResultId,
                rawCapability,
                RandomHexDigest(),
                observedAt: revokedAt));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, atRevocation.SqlState);
        Assert.Equal(0L, await CountDeploymentResultsAsync(database, operation.OperationId));
    }

    private static async Task<ChallengeIssueReceipt?> IssueChallengeAsync(
        PostgresTestDatabase database,
        Guid tenantId,
        BrokerOperationFixture operation,
        Guid challengeId,
        Guid messageId,
        Guid auditId,
        string rawCapability,
        TimeSpan? requestedLifetime = null) =>
        await IssueChallengeAsync(
            database,
            tenantId,
            operation.RotateOperationId,
            operation.CorrelationId,
            challengeId,
            messageId,
            auditId,
            rawCapability,
            requestedLifetime);

    private static async Task<ChallengeIssueReceipt?> IssueChallengeAsync(
        PostgresTestDatabase database,
        Guid tenantId,
        Guid operationId,
        Guid correlationId,
        Guid challengeId,
        Guid messageId,
        Guid auditId,
        string rawCapability,
        TimeSpan? requestedLifetime = null)
    {
        await using TenantPostgresTransaction transaction =
            await database.Worker.BeginTenantTransactionAsync(
                PostgresWorkerTenantCatalog.CreateContext(
                    tenantId,
                    correlationId));
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select issue_status, challenge_id, challenge_message_id,
                issued_at, expires_at, route_deployment_id,
                fence_generation, worker_assignment_id, worker_instance_id
            from control.issue_user_operation_reconciliation_challenge(
                @challenge_id, @message_id, @audit_id, @operation_id,
                @raw_capability, @requested_lifetime)
            """);
        command.Parameters.AddWithValue("challenge_id", NpgsqlDbType.Uuid, challengeId);
        command.Parameters.AddWithValue("message_id", NpgsqlDbType.Uuid, messageId);
        command.Parameters.AddWithValue("audit_id", NpgsqlDbType.Uuid, auditId);
        command.Parameters.AddWithValue(
            "operation_id",
            NpgsqlDbType.Uuid,
            operationId);
        command.Parameters.AddWithValue("raw_capability", NpgsqlDbType.Text, rawCapability);
        command.Parameters.AddWithValue(
            "requested_lifetime",
            NpgsqlDbType.Interval,
            requestedLifetime ?? TimeSpan.FromMinutes(5));

        ChallengeIssueReceipt? receipt = null;
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                receipt = new ChallengeIssueReceipt(
                    reader.GetString(0),
                    reader.GetGuid(1),
                    reader.GetGuid(2),
                    reader.GetFieldValue<DateTimeOffset>(3),
                    reader.GetFieldValue<DateTimeOffset>(4),
                    reader.GetGuid(5),
                    reader.GetInt64(6),
                    reader.GetGuid(7),
                    reader.GetGuid(8));
                Assert.False(await reader.ReadAsync());
            }
        }

        await transaction.CommitAsync();
        return receipt;
    }

    private static async Task<InvocationChallengeReceipt> IssueInvocationChallengeAsync(
        PostgresTestDatabase database,
        InvocationProtocolFixture fixture,
        Guid claimToken,
        long expectedRowVersion,
        Guid challengeId,
        Guid messageId,
        Guid auditId,
        string rawCapability,
        TimeSpan requestedLifetime)
    {
        var context = new TenantExecutionContext(
            fixture.TenantId,
            InvocationWorkerActorId,
            fixture.CorrelationId,
            null);
        await using TenantPostgresTransaction transaction =
            await database.Worker.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select challenge_status, challenge_id, challenge_message_id,
                original_dispatch_message_id, issued_at, expires_at,
                route_deployment_id, fence_generation, worker_assignment_id,
                worker_instance_id
            from control.issue_user_operation_invocation_reconciliation_challenge_v3(
                @operation_id, @claim_token, @expected_row_version,
                @challenge_id, @message_id, @audit_id,
                @raw_capability, @requested_lifetime)
            """);
        command.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, fixture.OperationId);
        command.Parameters.AddWithValue("claim_token", NpgsqlDbType.Uuid, claimToken);
        command.Parameters.AddWithValue(
            "expected_row_version",
            NpgsqlDbType.Bigint,
            expectedRowVersion);
        command.Parameters.AddWithValue("challenge_id", NpgsqlDbType.Uuid, challengeId);
        command.Parameters.AddWithValue("message_id", NpgsqlDbType.Uuid, messageId);
        command.Parameters.AddWithValue("audit_id", NpgsqlDbType.Uuid, auditId);
        command.Parameters.AddWithValue("raw_capability", NpgsqlDbType.Text, rawCapability);
        command.Parameters.AddWithValue(
            "requested_lifetime",
            NpgsqlDbType.Interval,
            requestedLifetime);

        InvocationChallengeReceipt receipt;
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            receipt = new InvocationChallengeReceipt(
                reader.GetString(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetGuid(6),
                reader.GetInt64(7),
                reader.GetGuid(8),
                reader.GetGuid(9));
            Assert.False(await reader.ReadAsync());
        }

        await transaction.CommitAsync();
        return receipt;
    }

    private static async Task<(Guid ClaimToken, long RowVersion)>
        ClaimOperationReconcilingForBoundaryTestAsync(
            PostgresTestDatabase database,
            InvocationProtocolFixture fixture)
    {
        var context = new TenantExecutionContext(
            fixture.TenantId,
            InvocationWorkerActorId,
            fixture.CorrelationId,
            null);
        Guid claimToken = Guid.CreateVersion7();
        await using TenantPostgresTransaction transaction =
            await database.Worker.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            update control.user_operations
            set state = 'reconciling', claimed_by = 'integration-worker',
                claim_token = @claim_token,
                claim_expires_at = clock_timestamp() + interval '2 minutes',
                row_version = row_version + 1,
                updated_at = clock_timestamp()
            where tenant_id = @tenant_id and id = @operation_id
              and state = 'propagating' and claim_token is null
            returning row_version
            """);
        command.Parameters.AddWithValue("claim_token", NpgsqlDbType.Uuid, claimToken);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, fixture.TenantId);
        command.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, fixture.OperationId);
        long rowVersion = Assert.IsType<long>(await command.ExecuteScalarAsync());
        await transaction.CommitAsync();
        return (claimToken, rowVersion);
    }

    private static async Task BeginInvocationWithBoundaryAuthorityWindowAsync(
        PostgresTestDatabase database,
        InvocationProtocolFixture fixture,
        DeliveryClaimReceipt claim)
    {
        // Identical to the canonical gateway begin, but with the minimum
        // sanctioned receipt lifetime (the function floors p_receipt_lifetime
        // at 15 seconds) so the invocation-authority window closes naturally
        // within the test: attempt rows reject direct timestamp mutation
        // (operations.guard_user_operation_invocation_attempt), and backdating
        // evidence would weaken the fence under test.
        var context = new TenantExecutionContext(
            fixture.TenantId,
            fixture.GatewayActorId,
            fixture.CorrelationId,
            null);
        await using TenantPostgresTransaction transaction =
            await database.GatewayRuntime.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select begin_status, prepared_at, redemption_capability,
                receipt_capability, credential_redemption_expires_at,
                invocation_receipt_deadline
            from control.begin_user_operation_gateway_invocation(
                @attempt_id, @delivery_claim_id, @delivery_claim_generation,
                @raw_gateway_capability,
                @invocation_id, @start_receipt_id,
                @raw_redemption_capability,
                @raw_receipt_capability,
                interval '15 seconds', @worker_instance_id, @deployment_id,
                @broker_account_id, @fence_generation, @region)
            """);
        command.Parameters.AddWithValue("attempt_id", NpgsqlDbType.Uuid, fixture.AttemptId);
        command.Parameters.AddWithValue("delivery_claim_id", NpgsqlDbType.Uuid, claim.ClaimId);
        command.Parameters.AddWithValue(
            "delivery_claim_generation",
            NpgsqlDbType.Integer,
            claim.Generation);
        command.Parameters.AddWithValue(
            "raw_gateway_capability",
            NpgsqlDbType.Text,
            fixture.GatewayCapability);
        command.Parameters.AddWithValue("invocation_id", NpgsqlDbType.Uuid, fixture.InvocationId);
        command.Parameters.AddWithValue(
            "start_receipt_id",
            NpgsqlDbType.Uuid,
            fixture.StartReceiptId);
        command.Parameters.AddWithValue(
            "raw_redemption_capability",
            NpgsqlDbType.Text,
            fixture.RedemptionCapability);
        command.Parameters.AddWithValue(
            "raw_receipt_capability",
            NpgsqlDbType.Text,
            fixture.ReceiptCapability);
        AddRouteParameters(command, fixture);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("prepared", reader.GetString(0));
        Assert.False(await reader.ReadAsync());
        await reader.CloseAsync();
        await transaction.CommitAsync();
    }

    private static async Task AwaitInvocationAuthorityClosedForBoundaryTestAsync(
        PostgresTestDatabase database,
        InvocationProtocolFixture fixture)
    {
        // The challenge issuer requires the invocation authority window to be
        // closed; wait out the remaining receipt lifetime deterministically.
        DateTimeOffset deadline;
        await using (NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync())
        await using (var read = new NpgsqlCommand(
            """
            select invocation_receipt_deadline
            from operations.user_operation_invocation_attempts
            where tenant_id = @tenant_id and id = @attempt_id
            """,
            administrator))
        {
            read.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, fixture.TenantId);
            read.Parameters.AddWithValue("attempt_id", NpgsqlDbType.Uuid, fixture.AttemptId);
            deadline = new DateTimeOffset(
                DateTime.SpecifyKind(
                    Assert.IsType<DateTime>(await read.ExecuteScalarAsync()),
                    DateTimeKind.Utc));
        }

        TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining + TimeSpan.FromMilliseconds(250));
        }
    }

    private static async Task<BrokerResultCapabilityReceipt?> RecordChallengeResultV5Async(
        PostgresTestDatabase database,
        InvocationProtocolFixture fixture,
        ChallengeRoute replacement,
        Guid challengeId,
        Guid challengeMessageId,
        Guid challengeConsumptionId,
        Guid resultId,
        string rawCapability,
        string outcome,
        DateTimeOffset observedAt,
        Guid actorId)
    {
        (string bindingSha256, string policySnapshotSha256) =
            await ReadInvocationResultBindingsAsync(database, fixture);

        // control.user_operation_protocol_sha256 and dotnet_canonical_json are
        // execute-revoked from public, so the canonical request digest is
        // computed over an administrator connection before entering the
        // runtime-evidence session.
        string requestSha256;
        await using (NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync())
        {
            await using var canonical = new NpgsqlCommand(
                $"""
                select control.user_operation_protocol_sha256(
                    {ChallengeRequestDocumentSql})
                """,
                administrator);
            BindChallengeRequestDocumentParameters(
                canonical,
                fixture,
                challengeConsumptionId,
                challengeId,
                challengeMessageId,
                resultId,
                rawCapability,
                outcome,
                observedAt,
                bindingSha256,
                policySnapshotSha256);
            requestSha256 = Assert.IsType<string>(await canonical.ExecuteScalarAsync());
        }

        await using var runtimeEvidence = new PostgresDatabase(
            database.RuntimeEvidenceConnectionString,
            PostgresDatabaseUsage.Runtime,
            database.TenantContextCapabilityProvider,
            allowInsecureLoopbackForDevelopment: true);
        await using TenantPostgresTransaction transaction =
            await runtimeEvidence.BeginTenantTransactionAsync(
                new TenantExecutionContext(
                    fixture.TenantId,
                    actorId,
                    fixture.CorrelationId));
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select acceptance_status, result_record_id, received_at
            from control.record_user_operation_result_v5(
                @result_id, @attempt_id, null::uuid, @operation_id,
                @dispatch_message_id, @start_receipt_id, @authorization_id,
                null::uuid, null::text,
                @consumption_id, @challenge_id, @challenge_message_id,
                @raw_capability, 'broker_account', @target_id,
                @target_observation, @submitted_resource_version,
                @requested_target_state, @binding_sha256,
                @policy_snapshot_sha256, @outcome, @observation_sha256,
                @observed_at, @request_sha256,
                @worker_instance_id, @deployment_id, @broker_account_id,
                @fence_generation, @region)
            """);
        command.Parameters.AddWithValue("result_id", NpgsqlDbType.Uuid, resultId);
        command.Parameters.AddWithValue("attempt_id", NpgsqlDbType.Uuid, fixture.AttemptId);
        command.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, fixture.OperationId);
        command.Parameters.AddWithValue(
            "dispatch_message_id",
            NpgsqlDbType.Uuid,
            fixture.DispatchMessageId);
        command.Parameters.AddWithValue(
            "start_receipt_id",
            NpgsqlDbType.Uuid,
            fixture.StartReceiptId);
        command.Parameters.AddWithValue(
            "authorization_id",
            NpgsqlDbType.Uuid,
            fixture.AuthorizationId);
        command.Parameters.AddWithValue(
            "consumption_id",
            NpgsqlDbType.Uuid,
            challengeConsumptionId);
        command.Parameters.AddWithValue("challenge_id", NpgsqlDbType.Uuid, challengeId);
        command.Parameters.AddWithValue(
            "challenge_message_id",
            NpgsqlDbType.Uuid,
            challengeMessageId);
        command.Parameters.AddWithValue("raw_capability", NpgsqlDbType.Text, rawCapability);
        command.Parameters.AddWithValue("target_id", NpgsqlDbType.Uuid, fixture.BrokerAccountId);
        command.Parameters.AddWithValue(
            "target_observation",
            NpgsqlDbType.Jsonb,
            fixture.TargetObservationJson);
        command.Parameters.AddWithValue(
            "submitted_resource_version",
            NpgsqlDbType.Bigint,
            fixture.SubmittedResourceVersion);
        command.Parameters.AddWithValue(
            "requested_target_state",
            NpgsqlDbType.Text,
            fixture.RequestedTargetState);
        command.Parameters.AddWithValue("binding_sha256", NpgsqlDbType.Text, bindingSha256);
        command.Parameters.AddWithValue(
            "policy_snapshot_sha256",
            NpgsqlDbType.Text,
            policySnapshotSha256);
        command.Parameters.AddWithValue("outcome", NpgsqlDbType.Text, outcome);
        command.Parameters.AddWithValue(
            "observation_sha256",
            NpgsqlDbType.Text,
            fixture.ObservationSha256);
        command.Parameters.AddWithValue(
            "observed_at",
            NpgsqlDbType.TimestampTz,
            observedAt.ToUniversalTime());
        command.Parameters.AddWithValue("request_sha256", NpgsqlDbType.Text, requestSha256);
        command.Parameters.AddWithValue(
            "worker_instance_id",
            NpgsqlDbType.Uuid,
            replacement.WorkerInstanceId);
        command.Parameters.AddWithValue("deployment_id", NpgsqlDbType.Uuid, replacement.DeploymentId);
        command.Parameters.AddWithValue(
            "broker_account_id",
            NpgsqlDbType.Uuid,
            fixture.BrokerAccountId);
        command.Parameters.AddWithValue(
            "fence_generation",
            NpgsqlDbType.Bigint,
            replacement.FenceGeneration);
        command.Parameters.AddWithValue("region", NpgsqlDbType.Text, fixture.Region);

        BrokerResultCapabilityReceipt? receipt = null;
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                receipt = new BrokerResultCapabilityReceipt(
                    reader.GetString(0),
                    reader.GetGuid(1),
                    reader.GetFieldValue<DateTimeOffset>(2));
                Assert.False(await reader.ReadAsync());
            }
        }

        await transaction.CommitAsync();
        return receipt;
    }

    private const string ChallengeRequestDocumentSql = """
        pg_catalog.jsonb_build_object(
            'attemptId', @attempt_id,
            'challengeConsumptionId', @consumption_id,
            'challengeId', @challenge_id,
            'challengeMessageId', @challenge_message_id,
            'challengeResultCapability', @raw_capability,
            'dispatchPolicySnapshotSha256', @policy_snapshot_sha256,
            'dispatchTargetBindingSha256', @binding_sha256,
            'gatewayStartReceiptId', @start_receipt_id,
            'observationSha256', @observation_sha256,
            'observedAtUtc', to_char(@observed_at at time zone 'UTC',
                'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
            'operationId', @operation_id,
            'originalDispatchMessageId', @dispatch_message_id,
            'outcome', @outcome,
            'providerCallAuthorizationReceiptId', @authorization_id,
            'requestedTargetState', @requested_target_state,
            'resultId', @result_id,
            'schemaVersion', 5,
            'submittedResourceVersion', @submitted_resource_version,
            'targetId', @target_id,
            'targetObservation', @target_observation,
            'targetType', 'broker_account')
        """;

    private static void BindChallengeRequestDocumentParameters(
        NpgsqlCommand command,
        InvocationProtocolFixture fixture,
        Guid challengeConsumptionId,
        Guid challengeId,
        Guid challengeMessageId,
        Guid resultId,
        string rawCapability,
        string outcome,
        DateTimeOffset observedAt,
        string bindingSha256,
        string policySnapshotSha256)
    {
        command.Parameters.AddWithValue("result_id", NpgsqlDbType.Uuid, resultId);
        command.Parameters.AddWithValue("attempt_id", NpgsqlDbType.Uuid, fixture.AttemptId);
        command.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, fixture.OperationId);
        command.Parameters.AddWithValue(
            "dispatch_message_id",
            NpgsqlDbType.Uuid,
            fixture.DispatchMessageId);
        command.Parameters.AddWithValue(
            "start_receipt_id",
            NpgsqlDbType.Uuid,
            fixture.StartReceiptId);
        command.Parameters.AddWithValue(
            "authorization_id",
            NpgsqlDbType.Uuid,
            fixture.AuthorizationId);
        command.Parameters.AddWithValue(
            "consumption_id",
            NpgsqlDbType.Uuid,
            challengeConsumptionId);
        command.Parameters.AddWithValue("challenge_id", NpgsqlDbType.Uuid, challengeId);
        command.Parameters.AddWithValue(
            "challenge_message_id",
            NpgsqlDbType.Uuid,
            challengeMessageId);
        command.Parameters.AddWithValue("raw_capability", NpgsqlDbType.Text, rawCapability);
        command.Parameters.AddWithValue("target_id", NpgsqlDbType.Uuid, fixture.BrokerAccountId);
        command.Parameters.AddWithValue(
            "target_observation",
            NpgsqlDbType.Jsonb,
            fixture.TargetObservationJson);
        command.Parameters.AddWithValue(
            "submitted_resource_version",
            NpgsqlDbType.Bigint,
            fixture.SubmittedResourceVersion);
        command.Parameters.AddWithValue(
            "requested_target_state",
            NpgsqlDbType.Text,
            fixture.RequestedTargetState);
        command.Parameters.AddWithValue("binding_sha256", NpgsqlDbType.Text, bindingSha256);
        command.Parameters.AddWithValue(
            "policy_snapshot_sha256",
            NpgsqlDbType.Text,
            policySnapshotSha256);
        command.Parameters.AddWithValue("outcome", NpgsqlDbType.Text, outcome);
        command.Parameters.AddWithValue(
            "observation_sha256",
            NpgsqlDbType.Text,
            fixture.ObservationSha256);
        command.Parameters.AddWithValue(
            "observed_at",
            NpgsqlDbType.TimestampTz,
            observedAt.ToUniversalTime());
    }

    private static async Task<long> CountInvocationResultsAsync(
        PostgresTestDatabase database,
        InvocationProtocolFixture fixture)
    {
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select count(*)
            from operations.user_operation_invocation_results
            where tenant_id = @tenant_id and operation_id = @operation_id
            """,
            administrator);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, fixture.TenantId);
        command.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, fixture.OperationId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static Task<ChallengeRoute> SeedReplacementAssignmentAsync(
        PostgresTestDatabase database,
        Guid tenantId,
        BrokerOperationFixture operation,
        Guid? requestedSupervisorId = null) =>
        SeedReplacementAssignmentAsync(
            database,
            tenantId,
            operation.DeploymentId,
            operation.WorkerAssignmentId,
            operation.WorkerInstanceId,
            requestedSupervisorId);

    private static async Task<ChallengeRoute> SeedReplacementAssignmentAsync(
        PostgresTestDatabase database,
        Guid tenantId,
        Guid deploymentId,
        Guid originalAssignmentId,
        Guid originalWorkerInstanceId,
        Guid? requestedSupervisorId = null)
    {
        Guid assignmentId = Guid.CreateVersion7();
        Guid workerInstanceId = Guid.CreateVersion7();
        Guid supervisorId = requestedSupervisorId ?? Guid.CreateVersion7();
        Guid strategyHostId = Guid.CreateVersion7();
        Guid gatewayHostId = Guid.CreateVersion7();
        const long fenceGeneration = 2;
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using NpgsqlTransaction transaction =
            await administrator.BeginTransactionAsync();
        await using (var replica = new NpgsqlCommand(
            "set local session_replication_role = 'replica'",
            administrator,
            transaction))
        {
            await replica.ExecuteNonQueryAsync();
        }

        await using (var command = new NpgsqlCommand(
            """
            insert into operations.worker_nodes
                (id, region, node_name, image_digest, state, capacity,
                 last_heartbeat_at, created_at, updated_at)
            select @worker_instance_id, source.region,
                'challenge-' || @worker_instance_id::text,
                source.image_digest, 'ready', source.capacity,
                clock_timestamp(), clock_timestamp(), clock_timestamp()
            from operations.worker_nodes as source
            where source.id = @original_worker_instance_id;

            update operations.worker_assignments
            set state = 'revoked',
                revoked_at = clock_timestamp(),
                row_version = row_version + 1
            where tenant_id = @tenant_id
              and id = @original_assignment_id;

            insert into operations.worker_assignments
                (id, tenant_id, deployment_id, worker_node_id,
                 supervisor_identity, strategy_host_identity,
                 gateway_host_identity, fence_generation, runtime_digest,
                 gateway_artifact_id, state, assigned_at, lease_expires_at)
            select @assignment_id, source.tenant_id, source.deployment_id,
                @worker_instance_id, @supervisor_identity,
                @strategy_host_identity, @gateway_host_identity,
                @fence_generation, source.runtime_digest,
                source.gateway_artifact_id, 'reconciliation_only',
                clock_timestamp(), clock_timestamp() + interval '4 minutes'
            from operations.worker_assignments as source
            where source.tenant_id = @tenant_id
              and source.id = @original_assignment_id;

            update operations.deployments
            set fence_generation = @fence_generation,
                row_version = row_version + 1,
                updated_at = clock_timestamp()
            where tenant_id = @tenant_id and id = @deployment_id;
            """,
            administrator,
            transaction))
        {
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
            command.Parameters.AddWithValue("deployment_id", NpgsqlDbType.Uuid, deploymentId);
            command.Parameters.AddWithValue(
                "original_assignment_id",
                NpgsqlDbType.Uuid,
                originalAssignmentId);
            command.Parameters.AddWithValue(
                "original_worker_instance_id",
                NpgsqlDbType.Uuid,
                originalWorkerInstanceId);
            command.Parameters.AddWithValue("assignment_id", NpgsqlDbType.Uuid, assignmentId);
            command.Parameters.AddWithValue(
                "worker_instance_id",
                NpgsqlDbType.Uuid,
                workerInstanceId);
            command.Parameters.AddWithValue(
                "supervisor_identity",
                NpgsqlDbType.Text,
                supervisorId.ToString("D"));
            command.Parameters.AddWithValue(
                "strategy_host_identity",
                NpgsqlDbType.Text,
                strategyHostId.ToString("D"));
            command.Parameters.AddWithValue(
                "gateway_host_identity",
                NpgsqlDbType.Text,
                gatewayHostId.ToString("D"));
            command.Parameters.AddWithValue(
                "fence_generation",
                NpgsqlDbType.Bigint,
                fenceGeneration);
            Assert.Equal(4, await command.ExecuteNonQueryAsync());
        }

        await transaction.CommitAsync();
        return new ChallengeRoute(
            deploymentId,
            fenceGeneration,
            assignmentId,
            workerInstanceId,
            supervisorId);
    }

    private static async Task SetOriginalOutboxStateAsync(
        PostgresTestDatabase database,
        Guid messageId,
        string state)
    {
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using NpgsqlTransaction transaction =
            await administrator.BeginTransactionAsync();
        await using (var replica = new NpgsqlCommand(
            "set local session_replication_role = 'replica'",
            administrator,
            transaction))
        {
            await replica.ExecuteNonQueryAsync();
        }

        await using (var command = new NpgsqlCommand(
            """
            update messaging.outbox_messages
            set state = @state,
                locked_by = case when @state = 'processing' then 'challenge-race' end,
                locked_until = case when @state = 'processing'
                    then clock_timestamp() + interval '1 minute' end,
                published_at = null,
                last_error = null
            where id = @message_id
            """,
            administrator,
            transaction))
        {
            command.Parameters.AddWithValue("state", NpgsqlDbType.Text, state);
            command.Parameters.AddWithValue("message_id", NpgsqlDbType.Uuid, messageId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        await transaction.CommitAsync();
    }

    private static async Task ExpireChallengeForBoundaryTestAsync(
        PostgresTestDatabase database,
        Guid challengeId)
    {
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using NpgsqlTransaction transaction =
            await administrator.BeginTransactionAsync();
        await using (var replica = new NpgsqlCommand(
            "set local session_replication_role = 'replica'",
            administrator,
            transaction))
        {
            await replica.ExecuteNonQueryAsync();
        }

        await using (var command = new NpgsqlCommand(
            """
            update operations.user_operation_invocation_challenges
            set issued_at = clock_timestamp() - interval '2 minutes',
                expires_at = clock_timestamp() - interval '1 minute'
            where id = @challenge_id
            """,
            administrator,
            transaction))
        {
            command.Parameters.AddWithValue("challenge_id", NpgsqlDbType.Uuid, challengeId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        await transaction.CommitAsync();
    }

    private static async Task<DateTimeOffset> ReadAssignmentLeaseExpiresAtAsync(
        PostgresTestDatabase database,
        Guid assignmentId)
    {
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select lease_expires_at
            from operations.worker_assignments
            where id = @assignment_id
            """,
            administrator);
        command.Parameters.AddWithValue("assignment_id", NpgsqlDbType.Uuid, assignmentId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        DateTimeOffset leaseExpiresAt = reader.GetFieldValue<DateTimeOffset>(0);
        Assert.False(await reader.ReadAsync());
        return leaseExpiresAt;
    }

    private static async Task<Guid> ReadOperationSessionIdAsync(
        PostgresTestDatabase database,
        Guid operationId)
    {
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select session_family_id
            from control.user_operations
            where id = @operation_id
            """,
            administrator);
        command.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, operationId);
        return Assert.IsType<Guid>(await command.ExecuteScalarAsync());
    }

    private static async Task SetAssignmentRevocationForBoundaryTestAsync(
        PostgresTestDatabase database,
        Guid assignmentId,
        DateTimeOffset revokedAt)
    {
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using NpgsqlTransaction transaction =
            await administrator.BeginTransactionAsync();
        await using (var replica = new NpgsqlCommand(
            "set local session_replication_role = 'replica'",
            administrator,
            transaction))
        {
            await replica.ExecuteNonQueryAsync();
        }

        await using (var command = new NpgsqlCommand(
            """
            update operations.worker_assignments
            set state = 'revoking',
                revoked_at = @revoked_at,
                row_version = row_version + 1
            where id = @assignment_id
            """,
            administrator,
            transaction))
        {
            command.Parameters.AddWithValue("assignment_id", NpgsqlDbType.Uuid, assignmentId);
            command.Parameters.AddWithValue(
                "revoked_at",
                NpgsqlDbType.TimestampTz,
                revokedAt.ToUniversalTime());
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        await transaction.CommitAsync();
    }

    private static async Task CloseOriginalResultAuthorityAsync(
        PostgresTestDatabase database,
        Guid operationId)
    {
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using NpgsqlTransaction transaction =
            await administrator.BeginTransactionAsync();
        await using (var replica = new NpgsqlCommand(
            "set local session_replication_role = 'replica'",
            administrator,
            transaction))
        {
            await replica.ExecuteNonQueryAsync();
        }

        await using (var command = new NpgsqlCommand(
            """
            update control.user_operations
            set result_capability_expires_at =
                    dispatched_at + interval '1 microsecond',
                dispatch_execution_deadline =
                    dispatched_at + interval '1 microsecond'
            where id = @operation_id
            """,
            administrator,
            transaction))
        {
            command.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, operationId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        await transaction.CommitAsync();
    }

    private static async Task<long> CountChallengesAsync(
        PostgresTestDatabase database,
        Guid operationId)
    {
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select count(*)
            from control.user_operation_reconciliation_challenges
            where operation_id = @operation_id
            """,
            administrator);
        command.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, operationId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<(string State, long ChallengeCount, long AuditCount)>
        ReadChallengeSideEffectsAsync(
            PostgresTestDatabase database,
            Guid operationId,
            Guid originalMessageId)
    {
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select
                (select state from messaging.outbox_messages where id = @message_id),
                (select count(*)
                 from control.user_operation_reconciliation_challenges
                 where operation_id = @operation_id),
                (select count(*)
                 from audit.audit_events
                 where target_type = 'user_operation'
                   and target_id = @operation_id::text
                   and action = 'user_operation.reconciliation_challenge_issued')
            """,
            administrator);
        command.Parameters.AddWithValue("message_id", NpgsqlDbType.Uuid, originalMessageId);
        command.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, operationId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var result = (reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2));
        Assert.False(await reader.ReadAsync());
        return result;
    }

    private static async Task<ChallengeOutboxRow> ReadChallengeOutboxAsync(
        PostgresTestDatabase database,
        Guid messageId)
    {
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select message_type, payload::text, payload_sha256, occurred_at
            from messaging.outbox_messages
            where id = @message_id
            """,
            administrator);
        command.Parameters.AddWithValue("message_id", NpgsqlDbType.Uuid, messageId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var result = new ChallengeOutboxRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetFieldValue<DateTimeOffset>(3));
        Assert.False(await reader.ReadAsync());
        return result;
    }

    private static async Task AssertChallengeSecretBoundaryAsync(
        PostgresTestDatabase database,
        Guid challengeId,
        Guid auditId,
        string rawCapability)
    {
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select
                challenge.result_capability_sha256,
                position(@raw_capability in to_jsonb(challenge)::text) = 0,
                position(@raw_capability in audit_event.payload::text) = 0
            from control.user_operation_reconciliation_challenges as challenge
            join audit.audit_events as audit_event
              on audit_event.tenant_id = challenge.tenant_id
             and audit_event.id = challenge.audit_event_id
            where challenge.id = @challenge_id
              and audit_event.id = @audit_id
            """,
            administrator);
        command.Parameters.AddWithValue("challenge_id", NpgsqlDbType.Uuid, challengeId);
        command.Parameters.AddWithValue("audit_id", NpgsqlDbType.Uuid, auditId);
        command.Parameters.AddWithValue("raw_capability", NpgsqlDbType.Text, rawCapability);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(
            Sha256Hex(Encoding.UTF8.GetBytes(rawCapability)),
            reader.GetString(0));
        Assert.True(reader.GetBoolean(1));
        Assert.True(reader.GetBoolean(2));
        Assert.False(await reader.ReadAsync());
    }

    private static async Task<ChallengeProofBinding> ReadChallengeProofBindingAsync(
        PostgresTestDatabase database,
        InvocationProtocolFixture fixture)
    {
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select result.dispatch_message_id,
                result.reconciliation_challenge_id,
                result.reconciliation_route_deployment_id,
                result.reconciliation_fence_generation,
                result.reconciliation_worker_assignment_id,
                result.reconciliation_worker_instance_id,
                consumption.result_record_id,
                consumption.result_id,
                consumption.request_sha256,
                challenge.result_capability_sha256
            from operations.user_operation_invocation_results as result
            join operations.user_operation_invocation_challenges as challenge
              on challenge.tenant_id = result.tenant_id
             and challenge.id = result.reconciliation_challenge_id
            join operations.user_operation_invocation_challenge_consumptions
                 as consumption
              on consumption.tenant_id = challenge.tenant_id
             and consumption.challenge_id = challenge.id
             and consumption.result_record_id = result.result_record_id
            where result.tenant_id = @tenant_id
              and result.operation_id = @operation_id
            """,
            administrator);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, fixture.TenantId);
        command.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, fixture.OperationId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var result = new ChallengeProofBinding(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetInt64(3),
            reader.GetGuid(4),
            reader.GetGuid(5),
            reader.GetGuid(6),
            reader.GetGuid(7),
            reader.GetString(8),
            reader.GetString(9));
        Assert.False(await reader.ReadAsync());
        return result;
    }

    private static async Task AssertInvocationChallengeSecretBoundaryAsync(
        PostgresTestDatabase database,
        Guid tenantId,
        Guid challengeId,
        Guid auditId,
        string rawCapability)
    {
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select
                challenge.result_capability_sha256,
                position(@raw_capability in to_jsonb(challenge)::text) = 0,
                position(@raw_capability in audit_event.payload::text) = 0
            from operations.user_operation_invocation_challenges as challenge
            join audit.audit_events as audit_event
              on audit_event.tenant_id = challenge.tenant_id
             and audit_event.id = challenge.audit_event_id
            where challenge.tenant_id = @tenant_id
              and challenge.id = @challenge_id
              and audit_event.id = @audit_id
            """,
            administrator);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
        command.Parameters.AddWithValue("challenge_id", NpgsqlDbType.Uuid, challengeId);
        command.Parameters.AddWithValue("audit_id", NpgsqlDbType.Uuid, auditId);
        command.Parameters.AddWithValue("raw_capability", NpgsqlDbType.Text, rawCapability);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(
            Sha256Hex(Encoding.UTF8.GetBytes(rawCapability)),
            reader.GetString(0));
        Assert.True(reader.GetBoolean(1));
        Assert.True(reader.GetBoolean(2));
        Assert.False(await reader.ReadAsync());
    }

    private sealed record InvocationChallengeReceipt(
        string Status,
        Guid ChallengeId,
        Guid MessageId,
        Guid OriginalDispatchMessageId,
        DateTimeOffset IssuedAt,
        DateTimeOffset ExpiresAt,
        Guid RouteDeploymentId,
        long FenceGeneration,
        Guid AssignmentId,
        Guid WorkerInstanceId);

    private sealed record ChallengeProofBinding(
        Guid OriginalDispatchMessageId,
        Guid ChallengeId,
        Guid ChallengeDeploymentId,
        long ChallengeFenceGeneration,
        Guid ChallengeAssignmentId,
        Guid ChallengeWorkerInstanceId,
        Guid ConsumedResultRecordId,
        Guid ConsumedResultId,
        string ConsumedRequestSha256,
        string StoredOriginalCapabilitySha256);

    private sealed record ChallengeIssueReceipt(
        string Status,
        Guid ChallengeId,
        Guid MessageId,
        DateTimeOffset IssuedAt,
        DateTimeOffset ExpiresAt,
        Guid RouteDeploymentId,
        long FenceGeneration,
        Guid AssignmentId,
        Guid WorkerInstanceId);

    private sealed record ChallengeRoute(
        Guid DeploymentId,
        long FenceGeneration,
        Guid AssignmentId,
        Guid WorkerInstanceId,
        Guid SupervisorId);

    private sealed record ChallengeOutboxRow(
        string MessageType,
        string PayloadJson,
        string PayloadSha256,
        DateTimeOffset OccurredAt);

}
