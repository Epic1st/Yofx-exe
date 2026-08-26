using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlTypes;
using YO4X.ControlPlane.Application;
using YO4X.ControlPlane.Workers.Operations;
using YO4X.ControlPlane.Workers.Outbox;
using YO4X.Persistence.Postgres;
using YO4X.Runtime.Contracts;
using YO4X.RuntimeControl.Postgres;
using YO4X.Tenancy;

namespace YO4X.Postgres.IntegrationTests;

public sealed partial class PostgresFoundationTests
{
    private static readonly Guid InvocationWorkerActorId =
        Guid.Parse("21e67e5a-daec-46eb-84af-f97244508616");

    [PostgresFact]
    public async Task InvocationV4CommitsExactAuthoritiesAndProjectsCrashRecoveredObservation()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        InvocationProtocolFixture fixture = await SeedInvocationProtocolFixtureAsync(database);

        await AssertRequestedV4CanonicalEnvelopeAsync(database, fixture);
        await AssertLegacyResultIngressDeniedAsync(database);

        DeliveryClaimReceipt claim = await ClaimInvocationDeliveryAsync(database, fixture);
        await AssertStaleDeliveryClaimGenerationDeniedAsync(database, fixture, claim);
        BeginInvocationReceipt begun = await BeginInvocationAsync(
            database,
            fixture,
            claim,
            fixture.RedemptionCapability,
            fixture.ReceiptCapability);
        Assert.Equal("prepared", begun.Status);
        Assert.Equal(fixture.RedemptionCapability, begun.RedemptionCapability);
        Assert.Equal(fixture.ReceiptCapability, begun.ReceiptCapability);
        Assert.True(begun.PreparedAt < begun.RedemptionExpiresAt);
        Assert.True(begun.RedemptionExpiresAt <= begun.ReceiptDeadline);

        BeginInvocationReceipt committedReplay = await BeginInvocationAsync(
            database,
            fixture,
            claim,
            RandomResultCapability(),
            RandomResultCapability());
        Assert.Equal("committed_no_replay", committedReplay.Status);
        Assert.Null(committedReplay.RedemptionCapability);
        Assert.Null(committedReplay.ReceiptCapability);
        Assert.Equal(begun.PreparedAt, committedReplay.PreparedAt);

        ProviderAuthorizationReceipt authorization = await AuthorizeProviderCallAsync(
            database,
            fixture);
        Assert.Equal("authorized", authorization.Status);
        Assert.True(authorization.ProviderCallAuthorized);
        Assert.NotNull(authorization.CommandDescriptor);
        Assert.True(authorization.AuthorizedAt < authorization.ExecuteNotAfter);

        ProviderAuthorizationReceipt noReissue = await AuthorizeProviderCallAsync(
            database,
            fixture);
        Assert.Equal("committed_no_reissue", noReissue.Status);
        Assert.False(noReissue.ProviderCallAuthorized);
        Assert.Null(noReissue.CommandDescriptor);
        Assert.Null(noReissue.AuthorizationReceiptSha256);

        GatewayObservationReceipt observation = await RecordGatewayObservationAsync(
            database,
            fixture);
        Assert.Equal("recorded", observation.Status);
        Assert.Equal("succeeded", observation.Outcome);
        AssertJsonEquivalent(fixture.TargetObservationJson, observation.TargetObservationJson);
        await AssertGatewayObservationDuplicateAsync(database, fixture, observation);

        // Simulate a gateway crash after the observation commit: no result-v5 row
        // is submitted. The worker must recover and project from the immutable
        // observation receipt without reissuing any invocation authority.
        ReconciliationReceipt reconciled = await ReconcileObservedAttemptAsync(
            database,
            fixture);
        Assert.Equal("conclusive_projected_result", reconciled.Status);
        Assert.Equal("gateway_observation_receipt", reconciled.ProofSource);
        Assert.Equal("succeeded", reconciled.Outcome);
        Assert.Equal("projected", reconciled.ProjectionStatus);
        AssertJsonEquivalent(fixture.TargetObservationJson, reconciled.TargetObservationJson);

        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using var verify = new NpgsqlCommand(
            """
            select account.state, account.credential_state,
                account.row_version = @projected_row_version,
                (select count(*)
                 from operations.user_operation_invocation_results as result
                 where result.tenant_id = @tenant_id
                   and result.attempt_id = @attempt_id),
                (select count(*)
                 from operations.user_operation_invocation_projections as projection
                 where projection.tenant_id = @tenant_id
                   and projection.attempt_id = @attempt_id
                   and projection.observation_receipt_id = @receipt_id)
            from operations.broker_accounts as account
            where account.tenant_id = @tenant_id and account.id = @account_id
            """,
            administrator);
        verify.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, fixture.TenantId);
        verify.Parameters.AddWithValue("attempt_id", NpgsqlDbType.Uuid, fixture.AttemptId);
        verify.Parameters.AddWithValue("receipt_id", NpgsqlDbType.Uuid, observation.ReceiptId);
        verify.Parameters.AddWithValue("account_id", NpgsqlDbType.Uuid, fixture.BrokerAccountId);
        verify.Parameters.AddWithValue(
            "projected_row_version",
            NpgsqlDbType.Bigint,
            Assert.IsType<long>(reconciled.ProjectedTargetRowVersion));
        await using NpgsqlDataReader reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("active", reader.GetString(0));
        Assert.Equal("ready", reader.GetString(1));
        Assert.True(reader.GetBoolean(2));
        Assert.Equal(0L, reader.GetInt64(3));
        Assert.Equal(1L, reader.GetInt64(4));
        Assert.False(await reader.ReadAsync());
    }

    [PostgresFact]
    public async Task InvocationV4ProductionWorkStoreCommitsProjectionAndTerminalEvidence()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        InvocationProtocolFixture fixture = await SeedInvocationProtocolFixtureAsync(database);

        DeliveryClaimReceipt claim = await ClaimInvocationDeliveryAsync(database, fixture);
        _ = await BeginInvocationAsync(
            database,
            fixture,
            claim,
            fixture.RedemptionCapability,
            fixture.ReceiptCapability);
        _ = await AuthorizeProviderCallAsync(database, fixture);
        GatewayObservationReceipt observation = await RecordGatewayObservationAsync(
            database,
            fixture);
        Assert.Equal("recorded", observation.Status);

        await PostgresProductionReadinessFixture.RemoveBroadActorGrantsAsync(database);
        ControlWorkCycleResult cycle = await RunProductionInvocationCycleAsync(
            database,
            "invocation-v4-production-path");
        Assert.True(cycle.ItemsExamined >= 1);
        Assert.True(cycle.ItemsChanged >= 1);
        Assert.Equal(0, cycle.ItemsFailed);
        ControlWorkCycleResult replayCycle = await RunProductionInvocationCycleAsync(
            database,
            "invocation-v4-production-replay");
        Assert.Equal(0, replayCycle.ItemsChanged);
        Assert.Equal(0, replayCycle.ItemsFailed);

        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using var verify = new NpgsqlCommand(
            """
            select operation.state,
                operation.last_error_code,
                operation.result_reference,
                operation.completed_at is not null,
                operation.claim_token is null,
                operation.reconciliation_route_deployment_id,
                operation.reconciliation_fence_generation,
                operation.reconciliation_worker_assignment_id,
                operation.reconciliation_worker_instance_id,
                account.state,
                account.credential_state,
                (select count(*)
                 from operations.user_operation_invocation_projections as projection
                 where projection.tenant_id = @tenant_id
                   and projection.attempt_id = @attempt_id
                   and projection.observation_receipt_id = @receipt_id),
                (select count(*)
                 from audit.audit_events as event
                 where event.tenant_id = @tenant_id
                   and event.causation_id = @operation_id
                   and event.action = 'user_operation.succeeded'
                   and event.payload ->> 'resultReference' = @result_reference
                   and event.payload ->> 'routeDeploymentId' = @deployment_id_text
                   and (event.payload ->> 'fenceGeneration')::bigint = @fence_generation),
                (select count(*)
                 from messaging.outbox_messages as message
                 where message.tenant_id = @tenant_id
                   and message.causation_id = @operation_id
                   and message.message_type = 'user_operation.succeeded.v1'
                   and message.payload ->> 'resultReference' = @result_reference)
            from control.user_operations as operation
            join operations.broker_accounts as account
              on account.tenant_id = operation.tenant_id
             and account.id = operation.target_id
            where operation.tenant_id = @tenant_id
              and operation.id = @operation_id
            """,
            administrator);
        string resultReference = $"invocation-observation/{fixture.AttemptId:D}";
        verify.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, fixture.TenantId);
        verify.Parameters.AddWithValue("attempt_id", NpgsqlDbType.Uuid, fixture.AttemptId);
        verify.Parameters.AddWithValue("receipt_id", NpgsqlDbType.Uuid, observation.ReceiptId);
        verify.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, fixture.OperationId);
        verify.Parameters.AddWithValue("result_reference", NpgsqlDbType.Text, resultReference);
        verify.Parameters.AddWithValue(
            "deployment_id_text",
            NpgsqlDbType.Text,
            fixture.DeploymentId.ToString("D"));
        verify.Parameters.AddWithValue(
            "fence_generation",
            NpgsqlDbType.Bigint,
            fixture.FenceGeneration);
        await using NpgsqlDataReader reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("succeeded", reader.GetString(0));
        Assert.True(reader.IsDBNull(1));
        Assert.Equal(resultReference, reader.GetString(2));
        Assert.True(reader.GetBoolean(3));
        Assert.True(reader.GetBoolean(4));
        Assert.Equal(fixture.DeploymentId, reader.GetGuid(5));
        Assert.Equal(fixture.FenceGeneration, reader.GetInt64(6));
        Assert.Equal(fixture.WorkerAssignmentId, reader.GetGuid(7));
        Assert.Equal(fixture.WorkerInstanceId, reader.GetGuid(8));
        Assert.Equal("active", reader.GetString(9));
        Assert.Equal("ready", reader.GetString(10));
        Assert.Equal(1L, reader.GetInt64(11));
        Assert.Equal(1L, reader.GetInt64(12));
        Assert.Equal(1L, reader.GetInt64(13));
        Assert.False(await reader.ReadAsync());
    }

    [PostgresFact]
    public async Task InvocationV4ProductionWorkStoreAtomicallyProjectsDeploymentJsonbAndReplaysIdempotently()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        InvocationProtocolFixture fixture = await SeedInvocationProtocolFixtureAsync(
            database,
            requestedTargetState: "close_only",
            operationType: "deployment.close_only",
            targetType: "deployment");

        Assert.Equal(fixture.DeploymentId, fixture.TargetId);
        await AssertRequestedV4CanonicalEnvelopeAsync(database, fixture);
        DeliveryClaimReceipt claim = await ClaimInvocationDeliveryAsync(database, fixture);
        _ = await BeginInvocationAsync(
            database,
            fixture,
            claim,
            fixture.RedemptionCapability,
            fixture.ReceiptCapability);
        _ = await AuthorizeProviderCallAsync(database, fixture);
        GatewayObservationReceipt observation = await RecordGatewayObservationAsync(
            database,
            fixture);
        Assert.Equal("recorded", observation.Status);
        Assert.Equal("succeeded", observation.Outcome);
        Assert.Matches("^[0-9a-f]{64}$", observation.ReceiptSha256);
        AssertJsonEquivalent(
            fixture.TargetObservationJson,
            observation.TargetObservationJson);

        DeploymentProjectionSnapshot beforeProjection =
            await ReadDeploymentProjectionSnapshotAsync(
                database,
                fixture.TenantId,
                fixture.DeploymentId);
        Assert.Equal(fixture.SubmittedResourceVersion, beforeProjection.RowVersion);
        Assert.Equal("running", beforeProjection.DesiredState);
        Assert.Equal("running", beforeProjection.ObservedState);
        Assert.Null(beforeProjection.LastReconciledAt);

        await PostgresProductionReadinessFixture.RemoveBroadActorGrantsAsync(database);
        ControlWorkCycleResult cycle = await RunProductionInvocationCycleAsync(
            database,
            "invocation-v4-deployment-projection");
        Assert.True(cycle.ItemsExamined >= 1);
        Assert.True(cycle.ItemsChanged >= 1);
        Assert.Equal(0, cycle.ItemsFailed);

        DeploymentProjectionSnapshot projected =
            await ReadDeploymentProjectionSnapshotAsync(
                database,
                fixture.TenantId,
                fixture.DeploymentId);
        AssertJsonEquivalent(
            beforeProjection.NonProjectionFieldsJson,
            projected.NonProjectionFieldsJson);
        Assert.Equal(beforeProjection.DesiredState, projected.DesiredState);
        Assert.Equal("close_only", projected.ObservedState);
        Assert.Equal(beforeProjection.RowVersion + 1, projected.RowVersion);
        Assert.Equal(observation.ObservedAt, projected.LastReconciledAt);
        Assert.True(projected.UpdatedAt >= beforeProjection.UpdatedAt);

        string resultReference = $"invocation-observation/{fixture.AttemptId:D}";
        long terminalOperationVersion;
        await using (NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync())
        await using (var verify = new NpgsqlCommand(
            """
            select operation.state,
                operation.last_error_code,
                operation.result_reference,
                operation.completed_at is not null,
                operation.claim_token is null,
                operation.reconciliation_route_deployment_id,
                operation.reconciliation_fence_generation,
                operation.reconciliation_worker_assignment_id,
                operation.reconciliation_worker_instance_id,
                operation.row_version,
                attempt.dispatch_policy_snapshot_sha256,
                projection.target_type,
                projection.target_id,
                projection.submitted_resource_version,
                projection.requested_target_state,
                projection.dispatch_target_binding_sha256,
                projection.target_observation::text,
                projection.outcome,
                projection.observation_sha256,
                projection.observed_at,
                projection.prior_target_row_version,
                projection.projected_target_row_version,
                projection.observation_receipt_id,
                projection.observation_receipt_kind,
                projection.observation_receipt_sha256,
                projection.result_record_id,
                projection.result_id,
                projection.invocation_id,
                projection.projected_at >= projection.observed_at,
                event.target_type,
                event.target_id,
                event.outcome,
                event.reason,
                event.resource_version_before,
                event.resource_version_after,
                event.payload::text,
                message.schema_version,
                message.aggregate_type,
                message.aggregate_id,
                message.payload::text,
                event.occurred_at = message.occurred_at
            from control.user_operations as operation
            join operations.user_operation_invocation_attempts as attempt
              on attempt.tenant_id = operation.tenant_id
             and attempt.id = operation.current_invocation_attempt_id
             and attempt.id = @attempt_id
            join operations.user_operation_invocation_projections as projection
              on projection.tenant_id = operation.tenant_id
             and projection.operation_id = operation.id
             and projection.attempt_id = @attempt_id
            join audit.audit_events as event
              on event.tenant_id = operation.tenant_id
             and event.causation_id = operation.id
             and event.action = 'user_operation.succeeded'
            join messaging.outbox_messages as message
              on message.tenant_id = operation.tenant_id
             and message.causation_id = operation.id
             and message.message_type = 'user_operation.succeeded.v1'
            where operation.tenant_id = @tenant_id
              and operation.id = @operation_id
            """,
            administrator))
        {
            verify.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, fixture.TenantId);
            verify.Parameters.AddWithValue("attempt_id", NpgsqlDbType.Uuid, fixture.AttemptId);
            verify.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, fixture.OperationId);
            await using NpgsqlDataReader reader = await verify.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("succeeded", reader.GetString(0));
            Assert.True(reader.IsDBNull(1));
            Assert.Equal(resultReference, reader.GetString(2));
            Assert.True(reader.GetBoolean(3));
            Assert.True(reader.GetBoolean(4));
            Assert.Equal(fixture.DeploymentId, reader.GetGuid(5));
            Assert.Equal(fixture.FenceGeneration, reader.GetInt64(6));
            Assert.Equal(fixture.WorkerAssignmentId, reader.GetGuid(7));
            Assert.Equal(fixture.WorkerInstanceId, reader.GetGuid(8));
            terminalOperationVersion = reader.GetInt64(9);
            string dispatchPolicySnapshotSha256 = reader.GetString(10);
            Assert.Matches("^[0-9a-f]{64}$", dispatchPolicySnapshotSha256);
            Assert.Equal("deployment", reader.GetString(11));
            Assert.Equal(fixture.DeploymentId, reader.GetGuid(12));
            Assert.Equal(fixture.SubmittedResourceVersion, reader.GetInt64(13));
            Assert.Equal("close_only", reader.GetString(14));
            Assert.Equal(fixture.DispatchTargetBindingSha256, reader.GetString(15));
            AssertJsonEquivalent(fixture.TargetObservationJson, reader.GetString(16));
            Assert.Equal("succeeded", reader.GetString(17));
            Assert.Equal(fixture.ObservationSha256, reader.GetString(18));
            Assert.Equal(observation.ObservedAt, reader.GetFieldValue<DateTimeOffset>(19));
            Assert.Equal(beforeProjection.RowVersion, reader.GetInt64(20));
            Assert.Equal(projected.RowVersion, reader.GetInt64(21));
            Assert.Equal(observation.ReceiptId, reader.GetGuid(22));
            Assert.Equal("gateway_observation_succeeded", reader.GetString(23));
            Assert.Equal(observation.ReceiptSha256, reader.GetString(24));
            Assert.True(reader.IsDBNull(25));
            Assert.True(reader.IsDBNull(26));
            Assert.Equal(fixture.InvocationId, reader.GetGuid(27));
            Assert.True(reader.GetBoolean(28));
            Assert.Equal("deployment", reader.GetString(29));
            Assert.Equal(fixture.DeploymentId.ToString("D"), reader.GetString(30));
            Assert.Equal("succeeded", reader.GetString(31));
            Assert.True(reader.IsDBNull(32));
            Assert.Equal(terminalOperationVersion - 1, reader.GetInt64(33));
            Assert.Equal(terminalOperationVersion, reader.GetInt64(34));
            string auditPayloadJson = reader.GetString(35);
            Assert.Equal(1, reader.GetInt16(36));
            Assert.Equal("user_operation", reader.GetString(37));
            Assert.Equal(fixture.OperationId.ToString("D"), reader.GetString(38));
            string outboxPayloadJson = reader.GetString(39);
            AssertJsonEquivalent(auditPayloadJson, outboxPayloadJson);
            JsonObject terminalPayload = Assert.IsType<JsonObject>(
                JsonNode.Parse(auditPayloadJson));
            Assert.Equal(12, terminalPayload.Count);
            Assert.Null(terminalPayload["dispatchPolicySnapshotSha256"]);
            Assert.Null(terminalPayload["errorCode"]);
            Assert.Equal(
                fixture.FenceGeneration,
                terminalPayload["fenceGeneration"]?.GetValue<long>());
            Assert.Equal(
                fixture.OperationId,
                terminalPayload["operationId"]?.GetValue<Guid>());
            Assert.Equal(
                "deployment.close_only",
                terminalPayload["operationType"]?.GetValue<string>());
            Assert.Equal(
                resultReference,
                terminalPayload["resultReference"]?.GetValue<string>());
            Assert.Equal(
                fixture.DeploymentId,
                terminalPayload["routeDeploymentId"]?.GetValue<Guid>());
            Assert.Equal("succeeded", terminalPayload["state"]?.GetValue<string>());
            Assert.Equal(
                fixture.DeploymentId,
                terminalPayload["targetId"]?.GetValue<Guid>());
            Assert.Equal("deployment", terminalPayload["targetType"]?.GetValue<string>());
            Assert.Equal(
                fixture.WorkerAssignmentId,
                terminalPayload["workerAssignmentId"]?.GetValue<Guid>());
            Assert.Equal(
                fixture.WorkerInstanceId,
                terminalPayload["workerInstanceId"]?.GetValue<Guid>());
            Assert.True(reader.GetBoolean(40));
            Assert.False(await reader.ReadAsync());
        }

        ControlWorkCycleResult replayCycle = await RunProductionInvocationCycleAsync(
            database,
            "invocation-v4-deployment-projection-replay");
        Assert.Equal(0, replayCycle.ItemsChanged);
        Assert.Equal(0, replayCycle.ItemsFailed);
        DeploymentProjectionSnapshot replayed =
            await ReadDeploymentProjectionSnapshotAsync(
                database,
                fixture.TenantId,
                fixture.DeploymentId);
        Assert.Equal(projected, replayed);

        await using NpgsqlConnection replayAdministrator =
            await database.Administrator.OpenConnectionAsync();
        await using var replayVerify = new NpgsqlCommand(
            """
            select operation.row_version,
                (select count(*)
                 from operations.user_operation_invocation_projections as projection
                 where projection.tenant_id = @tenant_id
                   and projection.attempt_id = @attempt_id),
                (select count(*)
                 from audit.audit_events as event
                 where event.tenant_id = @tenant_id
                   and event.causation_id = @operation_id
                   and event.action = 'user_operation.succeeded'),
                (select count(*)
                 from messaging.outbox_messages as message
                 where message.tenant_id = @tenant_id
                   and message.causation_id = @operation_id
                   and message.message_type = 'user_operation.succeeded.v1')
            from control.user_operations as operation
            where operation.tenant_id = @tenant_id
              and operation.id = @operation_id
            """,
            replayAdministrator);
        replayVerify.Parameters.AddWithValue(
            "tenant_id",
            NpgsqlDbType.Uuid,
            fixture.TenantId);
        replayVerify.Parameters.AddWithValue(
            "attempt_id",
            NpgsqlDbType.Uuid,
            fixture.AttemptId);
        replayVerify.Parameters.AddWithValue(
            "operation_id",
            NpgsqlDbType.Uuid,
            fixture.OperationId);
        await using NpgsqlDataReader replayReader = await replayVerify.ExecuteReaderAsync();
        Assert.True(await replayReader.ReadAsync());
        Assert.Equal(terminalOperationVersion, replayReader.GetInt64(0));
        Assert.Equal(1L, replayReader.GetInt64(1));
        Assert.Equal(1L, replayReader.GetInt64(2));
        Assert.Equal(1L, replayReader.GetInt64(3));
        Assert.False(await replayReader.ReadAsync());
    }

    [PostgresFact]
    public async Task InvocationV4ProductionWorkStoreTerminalizesDivergedEvidenceWithoutProjection()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        InvocationProtocolFixture fixture = await SeedInvocationProtocolFixtureAsync(
            database,
            requestedTargetState: "disabled:ready",
            operationType: "broker_account.disable");

        DeliveryClaimReceipt claim = await ClaimInvocationDeliveryAsync(database, fixture);
        _ = await BeginInvocationAsync(
            database,
            fixture,
            claim,
            fixture.RedemptionCapability,
            fixture.ReceiptCapability);
        _ = await AuthorizeProviderCallAsync(database, fixture);
        GatewayObservationReceipt observation = await RecordGatewayObservationAsync(
            database,
            fixture,
            outcome: "diverged");
        Assert.Equal("recorded", observation.Status);
        Assert.Equal("diverged", observation.Outcome);

        await PostgresProductionReadinessFixture.RemoveBroadActorGrantsAsync(database);
        ControlWorkCycleResult cycle = await RunProductionInvocationCycleAsync(
            database,
            "invocation-v4-diverged-path");
        Assert.True(cycle.ItemsChanged >= 1);
        Assert.Equal(0, cycle.ItemsFailed);

        string resultReference = $"invocation-observation/{fixture.AttemptId:D}";
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using var verify = new NpgsqlCommand(
            """
            select operation.state,
                operation.last_error_code,
                operation.result_reference,
                operation.completed_at is not null,
                account.state,
                account.credential_state,
                (select count(*)
                 from operations.user_operation_invocation_projections as projection
                 where projection.tenant_id = @tenant_id
                   and projection.attempt_id = @attempt_id),
                (select count(*)
                 from audit.audit_events as event
                 where event.tenant_id = @tenant_id
                   and event.causation_id = @operation_id
                   and event.action = 'user_operation.partial'
                   and event.reason = 'runtime_reconciliation_diverged'
                   and event.payload ->> 'resultReference' = @result_reference),
                (select count(*)
                 from messaging.outbox_messages as message
                 where message.tenant_id = @tenant_id
                   and message.causation_id = @operation_id
                   and message.message_type = 'user_operation.partial.v1'
                   and message.payload ->> 'errorCode' =
                       'runtime_reconciliation_diverged')
            from control.user_operations as operation
            join operations.broker_accounts as account
              on account.tenant_id = operation.tenant_id
             and account.id = operation.target_id
            where operation.tenant_id = @tenant_id
              and operation.id = @operation_id
            """,
            administrator);
        verify.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, fixture.TenantId);
        verify.Parameters.AddWithValue("attempt_id", NpgsqlDbType.Uuid, fixture.AttemptId);
        verify.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, fixture.OperationId);
        verify.Parameters.AddWithValue("result_reference", NpgsqlDbType.Text, resultReference);
        await using NpgsqlDataReader reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("partial", reader.GetString(0));
        Assert.Equal("runtime_reconciliation_diverged", reader.GetString(1));
        Assert.Equal(resultReference, reader.GetString(2));
        Assert.True(reader.GetBoolean(3));
        Assert.Equal("active", reader.GetString(4));
        Assert.Equal("rotation_pending", reader.GetString(5));
        Assert.Equal(0L, reader.GetInt64(6));
        Assert.Equal(1L, reader.GetInt64(7));
        Assert.Equal(1L, reader.GetInt64(8));
        Assert.False(await reader.ReadAsync());
    }

    [PostgresFact]
    public async Task InvocationV4ProductionWorkStoreTerminalizesBlockedProjectionWithoutOverwrite()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        InvocationProtocolFixture fixture = await SeedInvocationProtocolFixtureAsync(database);

        DeliveryClaimReceipt claim = await ClaimInvocationDeliveryAsync(database, fixture);
        _ = await BeginInvocationAsync(
            database,
            fixture,
            claim,
            fixture.RedemptionCapability,
            fixture.ReceiptCapability);
        _ = await AuthorizeProviderCallAsync(database, fixture);
        GatewayObservationReceipt observation = await RecordGatewayObservationAsync(
            database,
            fixture);
        Assert.Equal("succeeded", observation.Outcome);

        // Model a superseding durable account transition after the immutable
        // broker observation but before worker projection. This disposable
        // fixture bypasses row triggers only for the exact drift injection;
        // the production worker must preserve the newer state and fail closed.
        await using (NpgsqlConnection drift =
            await database.Administrator.OpenConnectionAsync())
        await using (NpgsqlTransaction transaction = await drift.BeginTransactionAsync())
        {
            await using (var replica = new NpgsqlCommand(
                "set local session_replication_role = replica",
                drift,
                transaction))
            {
                await replica.ExecuteNonQueryAsync();
            }

            await using (var supersede = new NpgsqlCommand(
                """
                update operations.broker_accounts
                set state = 'disabled',
                    credential_state = 'disabled',
                    row_version = row_version + 1,
                    updated_at = clock_timestamp()
                where tenant_id = @tenant_id and id = @account_id
                """,
                drift,
                transaction))
            {
                supersede.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, fixture.TenantId);
                supersede.Parameters.AddWithValue("account_id", NpgsqlDbType.Uuid, fixture.BrokerAccountId);
                Assert.Equal(1, await supersede.ExecuteNonQueryAsync());
            }

            await transaction.CommitAsync();
        }

        await PostgresProductionReadinessFixture.RemoveBroadActorGrantsAsync(database);
        ControlWorkCycleResult cycle = await RunProductionInvocationCycleAsync(
            database,
            "invocation-v4-projection-blocked-path");
        Assert.True(cycle.ItemsChanged >= 1);
        Assert.Equal(0, cycle.ItemsFailed);

        string resultReference = $"invocation-observation/{fixture.AttemptId:D}";
        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using var verify = new NpgsqlCommand(
            """
            select operation.state,
                operation.last_error_code,
                operation.result_reference,
                operation.completed_at is not null,
                account.state,
                account.credential_state,
                (select count(*)
                 from operations.user_operation_invocation_projections as projection
                 where projection.tenant_id = @tenant_id
                   and projection.attempt_id = @attempt_id),
                (select count(*)
                 from audit.audit_events as event
                 where event.tenant_id = @tenant_id
                   and event.causation_id = @operation_id
                   and event.action = 'user_operation.partial'
                   and event.reason = 'invocation_projection_blocked'),
                (select count(*)
                 from messaging.outbox_messages as message
                 where message.tenant_id = @tenant_id
                   and message.causation_id = @operation_id
                   and message.message_type = 'user_operation.partial.v1'
                   and message.payload ->> 'errorCode' =
                       'invocation_projection_blocked')
            from control.user_operations as operation
            join operations.broker_accounts as account
              on account.tenant_id = operation.tenant_id
             and account.id = operation.target_id
            where operation.tenant_id = @tenant_id
              and operation.id = @operation_id
            """,
            administrator);
        verify.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, fixture.TenantId);
        verify.Parameters.AddWithValue("attempt_id", NpgsqlDbType.Uuid, fixture.AttemptId);
        verify.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, fixture.OperationId);
        await using NpgsqlDataReader reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("partial", reader.GetString(0));
        Assert.Equal("invocation_projection_blocked", reader.GetString(1));
        Assert.Equal(resultReference, reader.GetString(2));
        Assert.True(reader.GetBoolean(3));
        Assert.Equal("disabled", reader.GetString(4));
        Assert.Equal("disabled", reader.GetString(5));
        Assert.Equal(0L, reader.GetInt64(6));
        Assert.Equal(1L, reader.GetInt64(7));
        Assert.Equal(1L, reader.GetInt64(8));
        Assert.False(await reader.ReadAsync());
    }

    [PostgresFact]
    public async Task InvocationV4ReconciliationRejectsWrongWorkerActorAcrossEveryEvidenceBranch()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();

        await AssertReconciliationAuthorityAcrossEveryBranchAsync(
            database,
            ReconciliationAuthorityVariant.WrongActor);
    }

    [PostgresFact]
    public async Task InvocationV4ReconciliationHidesEveryEvidenceBranchFromWrongCorrelation()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();

        await AssertReconciliationAuthorityAcrossEveryBranchAsync(
            database,
            ReconciliationAuthorityVariant.WrongCorrelation);
    }

    [PostgresFact]
    public async Task InvocationV4FlowsThroughEveryRoleSpecificApplicationAdapter()
    {
        _postgres.RequireAvailable();
        await using PostgresTestDatabase database = await _postgres.CreateDatabaseAsync();
        InvocationProtocolFixture fixture = await SeedInvocationProtocolFixtureAsync(database);
        await PostgresProductionReadinessFixture.RemoveBroadActorGrantsAsync(database);

        var options = new UserOperationInvocationPostgresOptions();
        await using var supervisorDatabase = new SupervisorUserOperationPostgresDatabase(
            database.SupervisorRuntimeConnectionString,
            database.TenantContextCapabilityProvider,
            allowInsecureLoopbackForDevelopment: true);
        await using var gatewayDatabase = new GatewayUserOperationPostgresDatabase(
            database.GatewayRuntimeConnectionString,
            database.TenantContextCapabilityProvider,
            allowInsecureLoopbackForDevelopment: true);
        await using var credentialDatabase = new CredentialUserOperationPostgresDatabase(
            database.CredentialRuntimeConnectionString,
            database.TenantContextCapabilityProvider,
            allowInsecureLoopbackForDevelopment: true);
        await using var evidenceDatabase = new RuntimeEvidencePostgresDatabase(
            database.RuntimeEvidenceConnectionString,
            database.TenantContextCapabilityProvider,
            allowInsecureLoopbackForDevelopment: true);

        var supervisor = new PostgresUserOperationSupervisorDeliveryApplication(
            supervisorDatabase,
            options);
        var gateway = new PostgresUserOperationGatewayApplication(gatewayDatabase, options);
        var provider = new ConclusiveBrokerProviderInvoker();
        var credential = new PostgresUserOperationCredentialBoundaryApplication(
            credentialDatabase,
            provider,
            options);
        var results = new PostgresUserOperationResultV5Application(evidenceDatabase);

        WorkloadActor supervisorActor = Actor(fixture, fixture.SupervisorActorId, "supervisor");
        WorkloadActor gatewayActor = Actor(fixture, fixture.GatewayActorId, "gateway_host");
        var metadata = new RequestMetadata(
            "invocation-v4-adapter-happy-path",
            fixture.CorrelationId,
            null,
            "postgres integration fact");

        UserOperationGatewayDeliveryClaim claim = await supervisor.ClaimForGatewayAsync(
            supervisorActor,
            UserOperationSupervisorDeliveryClaimRequest.Create(
                fixture.AttemptId,
                fixture.DispatchMessageId,
                UserOperationBearer.Create(fixture.DeliveryCapability)),
            metadata,
            CancellationToken.None);
        Assert.Equal(1, claim.DeliveryClaimGeneration);

        UserOperationGatewayBeginAuthority begun = await gateway.BeginAsync(
            gatewayActor,
            UserOperationGatewayBeginRequest.Create(
                fixture.AttemptId,
                fixture.DispatchMessageId,
                claim.DeliveryClaimId,
                claim.DeliveryClaimGeneration,
                claim.GatewayCapability),
            metadata,
            CancellationToken.None);
        Assert.Equal(UserOperationInvocationAttemptState.Prepared, begun.State);

        UserOperationProviderCallExecutionReceipt providerReceipt =
            await credential.ExecuteProviderCallOnceAsync(
                gatewayActor,
                UserOperationProviderCallExecutionRequest.Create(
                    begun.AttemptId,
                    begun.InvocationId,
                    begun.GatewayStartReceiptId,
                    begun.RedemptionNonce),
                metadata,
                CancellationToken.None);
        UserOperationProviderCallObservedReceipt observed =
            Assert.IsType<UserOperationProviderCallObservedReceipt>(providerReceipt);
        Assert.Equal(1, provider.CallCount);

        UserOperationGatewayObservationReceipt observation =
            await gateway.RecordObservationAsync(
                gatewayActor,
                UserOperationGatewayObservationRequest.Create(
                    begun.AttemptId,
                    begun.InvocationId,
                    begun.GatewayStartReceiptId,
                    observed.ProviderCallAuthorizationReceiptId,
                    begun.GatewayObservationReceiptBearer,
                    observed.Outcome,
                    observed.TargetObservation,
                    observed.ObservedAtUtc),
                metadata,
                CancellationToken.None);
        Assert.Equal(observed.TargetObservation.ComputeCanonicalSha256(), observation.ObservationSha256);

        (string targetBindingSha256, string policySnapshotSha256) =
            await ReadInvocationResultBindingsAsync(database, fixture);
        Guid resultId = Guid.CreateVersion7();
        UserOperationGatewayResultV5 result = UserOperationGatewayResultV5.Create(
            resultId,
            fixture.AttemptId,
            begun.InvocationId,
            fixture.OperationId,
            fixture.DispatchMessageId,
            begun.GatewayStartReceiptId,
            observation.GatewayObservationReceiptId,
            observed.ProviderCallAuthorizationReceiptId,
            observation.ObservationReceiptSha256,
            "broker_account",
            fixture.BrokerAccountId,
            observation.TargetObservation,
            fixture.SubmittedResourceVersion,
            "active:ready",
            targetBindingSha256,
            policySnapshotSha256,
            UserOperationBearer.Create(fixture.ResultCapability),
            observation.Outcome,
            observation.ObservationSha256,
            observation.ObservedAtUtc);
        UserOperationResultV5Acceptance accepted = await results.RecordGatewayResultAsync(
            supervisorActor,
            result,
            metadata,
            CancellationToken.None);
        Assert.Equal(resultId, accepted.ResultId);
        Assert.Equal("accepted", accepted.State);

        UserOperationResultV5Acceptance duplicate = await results.RecordGatewayResultAsync(
            supervisorActor,
            result,
            metadata,
            CancellationToken.None);
        Assert.Equal(resultId, duplicate.ResultId);
        Assert.Equal("duplicate", duplicate.State);

        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using var verify = new NpgsqlCommand(
            """
            select result.result_record_id,
                result.target_observation::text,
                result.observation_sha256,
                result.gateway_observation_receipt_id,
                result.received_at >= result.observed_at
            from operations.user_operation_invocation_results as result
            where result.tenant_id = @tenant_id
              and result.result_id = @result_id
            """,
            administrator);
        verify.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, fixture.TenantId);
        verify.Parameters.AddWithValue("result_id", NpgsqlDbType.Uuid, resultId);
        await using NpgsqlDataReader reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Guid resultRecordId = reader.GetGuid(0);
        AssertJsonEquivalent(fixture.TargetObservationJson, reader.GetString(1));
        Assert.Equal(fixture.ObservationSha256, reader.GetString(2));
        Assert.Equal(observation.GatewayObservationReceiptId, reader.GetGuid(3));
        Assert.True(reader.GetBoolean(4));
        Assert.False(await reader.ReadAsync());
        await reader.CloseAsync();

        ControlWorkCycleResult cycle = await RunProductionInvocationCycleAsync(
            database,
            "invocation-v4-result-v5-path");
        Assert.True(cycle.ItemsChanged >= 1);
        Assert.Equal(0, cycle.ItemsFailed);

        string resultReference = $"invocation-result/{resultRecordId:D}";
        await using var terminal = new NpgsqlCommand(
            """
            select operation.state,
                operation.last_error_code,
                operation.result_reference,
                operation.completed_at is not null,
                (select count(*)
                 from operations.user_operation_invocation_projections as projection
                 where projection.tenant_id = @tenant_id
                   and projection.attempt_id = @attempt_id
                   and projection.result_record_id = @result_record_id),
                (select count(*)
                 from audit.audit_events as event
                 where event.tenant_id = @tenant_id
                   and event.causation_id = @operation_id
                   and event.action = 'user_operation.succeeded'
                   and event.payload ->> 'resultReference' = @result_reference),
                (select count(*)
                 from messaging.outbox_messages as message
                 where message.tenant_id = @tenant_id
                   and message.causation_id = @operation_id
                   and message.message_type = 'user_operation.succeeded.v1'
                   and message.payload ->> 'resultReference' = @result_reference)
            from control.user_operations as operation
            where operation.tenant_id = @tenant_id
              and operation.id = @operation_id
            """,
            administrator);
        terminal.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, fixture.TenantId);
        terminal.Parameters.AddWithValue("attempt_id", NpgsqlDbType.Uuid, fixture.AttemptId);
        terminal.Parameters.AddWithValue("result_record_id", NpgsqlDbType.Uuid, resultRecordId);
        terminal.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, fixture.OperationId);
        terminal.Parameters.AddWithValue("result_reference", NpgsqlDbType.Text, resultReference);
        await using NpgsqlDataReader terminalReader = await terminal.ExecuteReaderAsync();
        Assert.True(await terminalReader.ReadAsync());
        Assert.Equal("succeeded", terminalReader.GetString(0));
        Assert.True(terminalReader.IsDBNull(1));
        Assert.Equal(resultReference, terminalReader.GetString(2));
        Assert.True(terminalReader.GetBoolean(3));
        Assert.Equal(1L, terminalReader.GetInt64(4));
        Assert.Equal(1L, terminalReader.GetInt64(5));
        Assert.Equal(1L, terminalReader.GetInt64(6));
        Assert.False(await terminalReader.ReadAsync());
    }

    private static WorkloadActor Actor(
        InvocationProtocolFixture fixture,
        Guid workloadId,
        string component) => new(
            fixture.TenantId,
            workloadId,
            fixture.WorkerInstanceId,
            fixture.DeploymentId,
            fixture.BrokerAccountId,
            fixture.FenceGeneration,
            fixture.Region,
            component);

    private static async Task<(string TargetBindingSha256, string PolicySnapshotSha256)>
        ReadInvocationResultBindingsAsync(
            PostgresTestDatabase database,
            InvocationProtocolFixture fixture)
    {
        await using NpgsqlConnection connection =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select dispatch_target_binding_sha256, dispatch_policy_snapshot_sha256
            from operations.user_operation_invocation_attempts
            where tenant_id = @tenant_id and id = @attempt_id
            """,
            connection);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, fixture.TenantId);
        command.Parameters.AddWithValue("attempt_id", NpgsqlDbType.Uuid, fixture.AttemptId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var result = (reader.GetString(0), reader.GetString(1));
        Assert.False(await reader.ReadAsync());
        return result;
    }

    private static async Task AssertReconciliationAuthorityAcrossEveryBranchAsync(
        PostgresTestDatabase database,
        ReconciliationAuthorityVariant authorityVariant)
    {
        foreach (ReconciliationEvidenceBranch branch in
            Enum.GetValues<ReconciliationEvidenceBranch>())
        {
            ReconciliationAuthorityScenario scenario =
                await SeedReconciliationAuthorityScenarioAsync(database, branch);
            ReconciliationAuthoritySnapshot before =
                await ReadReconciliationAuthoritySnapshotAsync(
                    database,
                    scenario.Fixture);

            ReconciliationBoundaryReceipt exact =
                await ProbeReconciliationBoundaryAsync(
                    database,
                    scenario.Fixture,
                    scenario.Claim,
                    InvocationWorkerActorId,
                    scenario.Fixture.CorrelationId);
            Assert.Equal(scenario.Expected, exact);
            Assert.Equal(
                before,
                await ReadReconciliationAuthoritySnapshotAsync(
                    database,
                    scenario.Fixture));

            if (authorityVariant == ReconciliationAuthorityVariant.WrongActor
                && branch == ReconciliationEvidenceBranch.NotSent)
            {
                await AssertReconciliationAuthorityValidationOrderAsync(
                    database,
                    scenario.Fixture);
                Assert.Equal(
                    before,
                    await ReadReconciliationAuthoritySnapshotAsync(
                        database,
                        scenario.Fixture));
            }

            if (authorityVariant == ReconciliationAuthorityVariant.WrongActor)
            {
                await AssertWrongReconciliationActorDeniedAsync(
                    database,
                    scenario.Fixture,
                    scenario.Claim);
            }
            else
            {
                await AssertWrongReconciliationCorrelationHiddenAsync(
                    database,
                    scenario.Fixture,
                    scenario.Claim);
            }

            ReconciliationAuthoritySnapshot after =
                await ReadReconciliationAuthoritySnapshotAsync(
                    database,
                    scenario.Fixture);
            Assert.True(
                before == after,
                $"Reconciliation authority probe mutated the {branch} branch.");
        }
    }

    private static async Task<ReconciliationAuthorityScenario>
        SeedReconciliationAuthorityScenarioAsync(
            PostgresTestDatabase database,
            ReconciliationEvidenceBranch branch)
    {
        InvocationProtocolFixture fixture;
        ReconciliationBoundaryReceipt expected;
        ReconciliationClaim? preparedClaim = null;
        switch (branch)
        {
            case ReconciliationEvidenceBranch.NotSent:
            {
                fixture = await SeedInvocationProtocolFixtureAsync(database);
                DeliveryClaimReceipt delivery =
                    await ClaimInvocationDeliveryAsync(database, fixture);
                await RejectInvocationBeforeProviderCallAsync(
                    database,
                    fixture,
                    delivery);
                expected = new ReconciliationBoundaryReceipt(
                    "not_sent", null, null, null);
                break;
            }
            case ReconciliationEvidenceBranch.PersistedDivergedResult:
            {
                fixture = await SeedInvocationProtocolFixtureAsync(
                    database,
                    requestedTargetState: "disabled:ready",
                    operationType: "broker_account.disable");
                GatewayObservationReceipt observation =
                    await RecordInvocationObservationForAuthorityScenarioAsync(
                        database,
                        fixture,
                        "diverged");
                await RecordGatewayResultV5Async(database, fixture, observation);
                expected = new ReconciliationBoundaryReceipt(
                    "conclusive_diverged_result",
                    "gateway_result_v5",
                    "diverged",
                    "not_applicable");
                break;
            }
            case ReconciliationEvidenceBranch.ObservationOnlyDiverged:
            {
                fixture = await SeedInvocationProtocolFixtureAsync(
                    database,
                    requestedTargetState: "disabled:ready",
                    operationType: "broker_account.disable");
                _ = await RecordInvocationObservationForAuthorityScenarioAsync(
                    database,
                    fixture,
                    "diverged");
                expected = new ReconciliationBoundaryReceipt(
                    "conclusive_diverged_result",
                    "gateway_observation_receipt",
                    "diverged",
                    "not_applicable");
                break;
            }
            case ReconciliationEvidenceBranch.AwaitingEvidence:
            {
                fixture = await SeedInvocationProtocolFixtureAsync(database);
                DeliveryClaimReceipt delivery =
                    await ClaimInvocationDeliveryAsync(database, fixture);
                _ = await BeginInvocationAsync(
                    database,
                    fixture,
                    delivery,
                    fixture.RedemptionCapability,
                    fixture.ReceiptCapability);
                _ = await AuthorizeProviderCallAsync(database, fixture);
                expected = new ReconciliationBoundaryReceipt(
                    "awaiting_evidence", null, null, null);
                break;
            }
            case ReconciliationEvidenceBranch.ChallengeOutstanding:
            {
                fixture = await SeedInvocationProtocolFixtureAsync(
                    database,
                    requestedInvocationWindow: TimeSpan.FromSeconds(15));
                DeliveryClaimReceipt delivery =
                    await ClaimInvocationDeliveryAsync(database, fixture);
                BeginInvocationReceipt begun = await BeginInvocationAsync(
                    database,
                    fixture,
                    delivery,
                    fixture.RedemptionCapability,
                    fixture.ReceiptCapability);
                _ = await AuthorizeProviderCallAsync(database, fixture);
                await WaitForDatabaseDeadlineAsync(begun.ReceiptDeadline);
                _ = await IssueOutstandingInvocationChallengeAsync(database, fixture);
                expected = new ReconciliationBoundaryReceipt(
                    "challenge_outstanding", null, null, null);
                break;
            }
            case ReconciliationEvidenceBranch.ReconciliationResultDiverged:
            {
                fixture = await SeedInvocationProtocolFixtureAsync(
                    database,
                    requestedTargetState: "disabled:ready",
                    operationType: "broker_account.disable",
                    requestedInvocationWindow: TimeSpan.FromSeconds(15));
                DeliveryClaimReceipt delivery =
                    await ClaimInvocationDeliveryAsync(database, fixture);
                BeginInvocationReceipt begun = await BeginInvocationAsync(
                    database,
                    fixture,
                    delivery,
                    fixture.RedemptionCapability,
                    fixture.ReceiptCapability);
                _ = await AuthorizeProviderCallAsync(database, fixture);
                await WaitForDatabaseDeadlineAsync(begun.ReceiptDeadline);
                IssuedAuthorityChallenge issued =
                    await IssueOutstandingInvocationChallengeAsync(database, fixture);
                BrokerResultCapabilityReceipt accepted =
                    Assert.IsType<BrokerResultCapabilityReceipt>(
                        await RecordChallengeResultV5Async(
                            database,
                            fixture,
                            issued.Route,
                            issued.Receipt.ChallengeId,
                            issued.Receipt.MessageId,
                            Guid.CreateVersion7(),
                            Guid.CreateVersion7(),
                            issued.RawCapability,
                            "diverged",
                            issued.Receipt.IssuedAt.AddTicks(10),
                            issued.Route.SupervisorId));
                Assert.Equal("accepted", accepted.Status);
                expected = new ReconciliationBoundaryReceipt(
                    "conclusive_diverged_result",
                    "reconciliation_result_v5",
                    "diverged",
                    "not_applicable");
                break;
            }
            case ReconciliationEvidenceBranch.SucceededProjection:
            {
                fixture = await SeedInvocationProtocolFixtureAsync(database);
                _ = await RecordInvocationObservationForAuthorityScenarioAsync(
                    database,
                    fixture,
                    "succeeded");
                expected = new ReconciliationBoundaryReceipt(
                    "conclusive_projected_result",
                    "gateway_observation_receipt",
                    "succeeded",
                    "projected");
                break;
            }
            case ReconciliationEvidenceBranch.AlreadyProjectedSuccess:
            {
                fixture = await SeedInvocationProtocolFixtureAsync(database);
                _ = await RecordInvocationObservationForAuthorityScenarioAsync(
                    database,
                    fixture,
                    "succeeded");
                preparedClaim =
                    await ClaimOperationForReconciliationAsync(database, fixture);
                ReconciliationBoundaryReceipt projected =
                    await ProbeReconciliationBoundaryAsync(
                        database,
                        fixture,
                        preparedClaim,
                        InvocationWorkerActorId,
                        fixture.CorrelationId,
                        commit: true);
                Assert.Equal(
                    new ReconciliationBoundaryReceipt(
                        "conclusive_projected_result",
                        "gateway_observation_receipt",
                        "succeeded",
                        "projected"),
                    projected);
                expected = new ReconciliationBoundaryReceipt(
                    "conclusive_projected_result",
                    "gateway_observation_receipt",
                    "succeeded",
                    "already_projected");
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(branch), branch, null);
        }

        ReconciliationClaim claim = preparedClaim
            ?? await ClaimOperationForReconciliationAsync(database, fixture);
        return new ReconciliationAuthorityScenario(fixture, claim, expected);
    }

    private static async Task<GatewayObservationReceipt>
        RecordInvocationObservationForAuthorityScenarioAsync(
            PostgresTestDatabase database,
            InvocationProtocolFixture fixture,
            string outcome)
    {
        DeliveryClaimReceipt delivery =
            await ClaimInvocationDeliveryAsync(database, fixture);
        _ = await BeginInvocationAsync(
            database,
            fixture,
            delivery,
            fixture.RedemptionCapability,
            fixture.ReceiptCapability);
        _ = await AuthorizeProviderCallAsync(database, fixture);
        return await RecordGatewayObservationAsync(database, fixture, outcome);
    }

    private static async Task RejectInvocationBeforeProviderCallAsync(
        PostgresTestDatabase database,
        InvocationProtocolFixture fixture,
        DeliveryClaimReceipt claim)
    {
        var context = new TenantExecutionContext(
            fixture.TenantId,
            fixture.SupervisorActorId,
            fixture.CorrelationId,
            null);
        await using TenantPostgresTransaction transaction =
            await database.SupervisorRuntime.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select rejection_status
            from control.reject_user_operation_before_invocation(
                @attempt_id, @delivery_claim_id, @delivery_claim_generation,
                @raw_gateway_capability, @receipt_id,
                'supervisor_rejected_before_invocation',
                @worker_instance_id, @deployment_id, @broker_account_id,
                @fence_generation, @region)
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
        command.Parameters.AddWithValue("receipt_id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
        AddRouteParameters(command, fixture);
        Assert.Equal("rejected", Assert.IsType<string>(await command.ExecuteScalarAsync()));
        await transaction.CommitAsync();
    }

    private static async Task RecordGatewayResultV5Async(
        PostgresTestDatabase database,
        InvocationProtocolFixture fixture,
        GatewayObservationReceipt observation)
    {
        await PostgresProductionReadinessFixture.RemoveBroadActorGrantsAsync(database);
        try
        {
            await using var evidenceDatabase = new RuntimeEvidencePostgresDatabase(
                database.RuntimeEvidenceConnectionString,
                database.TenantContextCapabilityProvider,
                allowInsecureLoopbackForDevelopment: true);
            var results = new PostgresUserOperationResultV5Application(evidenceDatabase);
            (string targetBindingSha256, string policySnapshotSha256) =
                await ReadInvocationResultBindingsAsync(database, fixture);
            UserOperationTargetObservation targetObservation =
                UserOperationTargetObservation.ParseDatabaseJson(
                    fixture.TargetType,
                    observation.TargetObservationJson);
            UserOperationObservationOutcome outcome = observation.Outcome switch
            {
                "succeeded" => UserOperationObservationOutcome.Succeeded,
                "diverged" => UserOperationObservationOutcome.Diverged,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(observation),
                    observation.Outcome,
                    "The authority fixture observation outcome is unsupported.")
            };
            Guid resultId = Guid.CreateVersion7();
            UserOperationGatewayResultV5 result = UserOperationGatewayResultV5.Create(
                resultId,
                fixture.AttemptId,
                fixture.InvocationId,
                fixture.OperationId,
                fixture.DispatchMessageId,
                fixture.StartReceiptId,
                observation.ReceiptId,
                fixture.AuthorizationId,
                observation.ReceiptSha256,
                fixture.TargetType,
                fixture.TargetId,
                targetObservation,
                fixture.SubmittedResourceVersion,
                fixture.RequestedTargetState,
                targetBindingSha256,
                policySnapshotSha256,
                UserOperationBearer.Create(fixture.ResultCapability),
                outcome,
                fixture.ObservationSha256,
                observation.ObservedAt);
            var metadata = new RequestMetadata(
                "invocation-v4-reconciliation-authority",
                fixture.CorrelationId,
                null,
                "postgres integration authority boundary");
            UserOperationResultV5Acceptance accepted = await results.RecordGatewayResultAsync(
                Actor(fixture, fixture.SupervisorActorId, "supervisor"),
                result,
                metadata,
                CancellationToken.None);
            Assert.Equal(resultId, accepted.ResultId);
            Assert.Equal("accepted", accepted.State);
        }
        finally
        {
            await PostgresProductionReadinessFixture.RestoreBroadActorGrantsAsync(database);
        }
    }

    private static async Task WaitForDatabaseDeadlineAsync(
        DateTimeOffset deadline)
    {
        TimeSpan remaining = deadline - DateTimeOffset.UtcNow
            + TimeSpan.FromMilliseconds(250);
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining);
        }
    }

    private static async Task<IssuedAuthorityChallenge>
        IssueOutstandingInvocationChallengeAsync(
        PostgresTestDatabase database,
        InvocationProtocolFixture fixture)
    {
        ReconciliationClaim claim =
            await ClaimOperationForReconciliationAsync(database, fixture);
        Guid challengeId = Guid.CreateVersion7();
        Guid challengeMessageId = Guid.CreateVersion7();
        Guid auditEventId = Guid.CreateVersion7();
        string rawResultCapability = RandomResultCapability();
        var context = new TenantExecutionContext(
            fixture.TenantId,
            InvocationWorkerActorId,
            fixture.CorrelationId,
            null);
        await using TenantPostgresTransaction transaction =
            await database.Worker.BeginTenantTransactionAsync(context);
        InvocationChallengeReceipt challenge;
        await using (NpgsqlCommand issue = transaction.CreateCommand(
            """
            select challenge_status, challenge_id, challenge_message_id,
                original_dispatch_message_id, issued_at, expires_at,
                route_deployment_id, fence_generation, worker_assignment_id,
                worker_instance_id
            from control.issue_user_operation_invocation_reconciliation_challenge_v3(
                @operation_id, @claim_token, @expected_row_version,
                @challenge_id, @challenge_message_id, @audit_event_id,
                @raw_result_capability, interval '2 minutes')
            """))
        {
            issue.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, fixture.OperationId);
            issue.Parameters.AddWithValue("claim_token", NpgsqlDbType.Uuid, claim.ClaimToken);
            issue.Parameters.AddWithValue("expected_row_version", NpgsqlDbType.Bigint, claim.RowVersion);
            issue.Parameters.AddWithValue("challenge_id", NpgsqlDbType.Uuid, challengeId);
            issue.Parameters.AddWithValue("challenge_message_id", NpgsqlDbType.Uuid, challengeMessageId);
            issue.Parameters.AddWithValue("audit_event_id", NpgsqlDbType.Uuid, auditEventId);
            issue.Parameters.AddWithValue("raw_result_capability", NpgsqlDbType.Text, rawResultCapability);
            await using NpgsqlDataReader reader = await issue.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            challenge = new InvocationChallengeReceipt(
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
            Assert.Equal("issued", challenge.Status);
            Assert.False(await reader.ReadAsync());
        }

        DateTimeOffset nextProcessingAt;
        await using (NpgsqlCommand defer = transaction.CreateCommand(
            """
            select next_processing_at
            from control.defer_user_operation(
                @operation_id, @claim_token, @expected_row_version,
                'reconciling', null::text)
            """))
        {
            defer.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, fixture.OperationId);
            defer.Parameters.AddWithValue("claim_token", NpgsqlDbType.Uuid, claim.ClaimToken);
            defer.Parameters.AddWithValue("expected_row_version", NpgsqlDbType.Bigint, claim.RowVersion);
            await using NpgsqlDataReader reader = await defer.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            nextProcessingAt = reader.GetFieldValue<DateTimeOffset>(0);
            Assert.False(await reader.ReadAsync());
        }
        await transaction.CommitAsync();
        await WaitForDatabaseDeadlineAsync(nextProcessingAt);
        return new IssuedAuthorityChallenge(
            challenge,
            rawResultCapability,
            new ChallengeRoute(
                challenge.RouteDeploymentId,
                challenge.FenceGeneration,
                challenge.AssignmentId,
                challenge.WorkerInstanceId,
                fixture.SupervisorActorId));
    }

    private static async Task<ReconciliationClaim> ClaimOperationForReconciliationAsync(
        PostgresTestDatabase database,
        InvocationProtocolFixture fixture)
    {
        Guid claimToken = Guid.CreateVersion7();
        var context = new TenantExecutionContext(
            fixture.TenantId,
            InvocationWorkerActorId,
            fixture.CorrelationId,
            null);
        await using TenantPostgresTransaction transaction =
            await database.Worker.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            update control.user_operations
            set state = 'reconciling', claimed_by = 'authority-boundary-worker',
                claim_token = @claim_token,
                claim_expires_at = clock_timestamp() + interval '2 minutes',
                row_version = row_version + 1,
                updated_at = clock_timestamp()
            where tenant_id = @tenant_id and id = @operation_id
              and state in ('propagating', 'reconciling', 'unknown')
              and claim_token is null
            returning row_version
            """);
        command.Parameters.AddWithValue("claim_token", NpgsqlDbType.Uuid, claimToken);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, fixture.TenantId);
        command.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, fixture.OperationId);
        long rowVersion = Assert.IsType<long>(await command.ExecuteScalarAsync());
        await transaction.CommitAsync();
        return new ReconciliationClaim(claimToken, rowVersion);
    }

    private static async Task<ReconciliationBoundaryReceipt>
        ProbeReconciliationBoundaryAsync(
            PostgresTestDatabase database,
            InvocationProtocolFixture fixture,
            ReconciliationClaim claim,
            Guid actorId,
            Guid correlationId,
            bool commit = false)
    {
        var context = new TenantExecutionContext(
            fixture.TenantId,
            actorId,
            correlationId,
            null);
        await using TenantPostgresTransaction transaction =
            await database.Worker.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = ReconciliationBoundaryCommand(
            transaction,
            fixture,
            claim);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var result = new ReconciliationBoundaryReceipt(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
        Assert.False(await reader.ReadAsync());
        await reader.CloseAsync();
        if (commit)
        {
            await transaction.CommitAsync();
        }
        else
        {
            await transaction.RollbackAsync();
        }
        return result;
    }

    private static async Task AssertWrongReconciliationActorDeniedAsync(
        PostgresTestDatabase database,
        InvocationProtocolFixture fixture,
        ReconciliationClaim claim)
    {
        Guid wrongActor = Guid.CreateVersion7();
        Assert.NotEqual(InvocationWorkerActorId, wrongActor);
        var context = new TenantExecutionContext(
            fixture.TenantId,
            wrongActor,
            fixture.CorrelationId,
            null);
        await using TenantPostgresTransaction transaction =
            await database.Worker.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = ReconciliationBoundaryCommand(
            transaction,
            fixture,
            claim);
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(
            async () =>
            {
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
                _ = await reader.ReadAsync();
            });
        Assert.Equal("42501", exception.SqlState);
        Assert.Equal(
            "Invocation reconciliation requires exact worker tenant authority.",
            exception.MessageText);
        Assert.DoesNotContain(
            fixture.OperationId.ToString("D"),
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            fixture.AttemptId.ToString("D"),
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            fixture.ObservationSha256,
            exception.Message,
            StringComparison.Ordinal);
        await transaction.RollbackAsync();
    }

    private static async Task AssertWrongReconciliationCorrelationHiddenAsync(
        PostgresTestDatabase database,
        InvocationProtocolFixture fixture,
        ReconciliationClaim claim)
    {
        Guid wrongCorrelation = Guid.CreateVersion7();
        Assert.NotEqual(fixture.CorrelationId, wrongCorrelation);
        var context = new TenantExecutionContext(
            fixture.TenantId,
            InvocationWorkerActorId,
            wrongCorrelation,
            null);
        await using TenantPostgresTransaction transaction =
            await database.Worker.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = ReconciliationBoundaryCommand(
            transaction,
            fixture,
            claim);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.False(await reader.ReadAsync());
        await reader.CloseAsync();

        await using NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync();
        await using NpgsqlTransaction administratorTransaction =
            await administrator.BeginTransactionAsync();
        await using (var proveNoRowLock = new NpgsqlCommand(
            """
            select id
            from control.user_operations
            where tenant_id = @tenant_id and id = @operation_id
            for update nowait
            """,
            administrator,
            administratorTransaction))
        {
            proveNoRowLock.Parameters.AddWithValue(
                "tenant_id",
                NpgsqlDbType.Uuid,
                fixture.TenantId);
            proveNoRowLock.Parameters.AddWithValue(
                "operation_id",
                NpgsqlDbType.Uuid,
                fixture.OperationId);
            Assert.Equal(
                fixture.OperationId,
                Assert.IsType<Guid>(await proveNoRowLock.ExecuteScalarAsync()));
        }
        await administratorTransaction.RollbackAsync();
        await transaction.CommitAsync();
    }

    private static async Task AssertReconciliationAuthorityValidationOrderAsync(
        PostgresTestDatabase database,
        InvocationProtocolFixture fixture)
    {
        var wrongActorContext = new TenantExecutionContext(
            fixture.TenantId,
            Guid.CreateVersion7(),
            fixture.CorrelationId,
            null);
        await using (TenantPostgresTransaction transaction =
            await database.Worker.BeginTenantTransactionAsync(wrongActorContext))
        await using (NpgsqlCommand command = transaction.CreateCommand(
            "select * from control.reconcile_user_operation_invocation_attempt(null, null, -1)"))
        {
            PostgresException exception = await Assert.ThrowsAsync<PostgresException>(
                async () =>
                {
                    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
                    _ = await reader.ReadAsync();
                });
            Assert.Equal("42501", exception.SqlState);
            Assert.Equal(
                "Invocation reconciliation requires exact worker tenant authority.",
                exception.MessageText);
            await transaction.RollbackAsync();
        }

        var exactContext = new TenantExecutionContext(
            fixture.TenantId,
            InvocationWorkerActorId,
            fixture.CorrelationId,
            null);
        await using (TenantPostgresTransaction transaction =
            await database.Worker.BeginTenantTransactionAsync(exactContext))
        await using (NpgsqlCommand command = transaction.CreateCommand(
            "select * from control.reconcile_user_operation_invocation_attempt(null, null, -1)"))
        {
            PostgresException exception = await Assert.ThrowsAsync<PostgresException>(
                async () =>
                {
                    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
                    _ = await reader.ReadAsync();
                });
            Assert.Equal("22023", exception.SqlState);
            Assert.Equal(
                "Invocation reconciliation evidence is invalid.",
                exception.MessageText);
            await transaction.RollbackAsync();
        }
    }

    private static NpgsqlCommand ReconciliationBoundaryCommand(
        TenantPostgresTransaction transaction,
        InvocationProtocolFixture fixture,
        ReconciliationClaim claim)
    {
        NpgsqlCommand command = transaction.CreateCommand(
            """
            select reconciliation_status, proof_source, outcome,
                projection_status
            from control.reconcile_user_operation_invocation_attempt(
                @operation_id, @claim_token, @expected_row_version)
            """);
        command.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, fixture.OperationId);
        command.Parameters.AddWithValue("claim_token", NpgsqlDbType.Uuid, claim.ClaimToken);
        command.Parameters.AddWithValue("expected_row_version", NpgsqlDbType.Bigint, claim.RowVersion);
        return command;
    }

    private static async Task<ReconciliationAuthoritySnapshot>
        ReadReconciliationAuthoritySnapshotAsync(
            PostgresTestDatabase database,
            InvocationProtocolFixture fixture)
    {
        await using NpgsqlConnection connection =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select jsonb_build_object(
                       'state', operation.state,
                       'rowVersion', operation.row_version,
                       'claimedBy', operation.claimed_by,
                       'claimToken', operation.claim_token,
                       'claimExpiresAt', operation.claim_expires_at,
                       'resultReference', operation.result_reference,
                       'lastErrorCode', operation.last_error_code,
                       'completedAt', operation.completed_at)::text,
                   jsonb_build_object(
                       'state', attempt.state,
                       'stateVersion', attempt.state_version,
                       'terminalReason', attempt.terminal_reason,
                       'gatewayObservationReceiptId',
                           attempt.gateway_observation_receipt_id,
                       'completedAt', attempt.completed_at)::text,
                   case operation.target_type
                       when 'broker_account' then
                           (select jsonb_build_object(
                               'state', account.state,
                               'credentialState', account.credential_state,
                               'rowVersion', account.row_version,
                               'updatedAt', account.updated_at)::text
                            from operations.broker_accounts as account
                            where account.tenant_id = operation.tenant_id
                              and account.id = operation.target_id)
                       when 'deployment' then
                           (select jsonb_build_object(
                               'desiredState', deployment.desired_state,
                               'observedState', deployment.observed_state,
                               'rowVersion', deployment.row_version,
                               'lastReconciledAt', deployment.last_reconciled_at,
                               'updatedAt', deployment.updated_at)::text
                            from operations.deployments as deployment
                            where deployment.tenant_id = operation.tenant_id
                              and deployment.id = operation.target_id)
                   end,
                   (select count(*)
                    from operations.user_operation_invocation_receipts as receipt
                    where receipt.tenant_id = operation.tenant_id
                      and receipt.attempt_id = attempt.id),
                   (select count(*)
                    from operations.user_operation_invocation_results as result
                    where result.tenant_id = operation.tenant_id
                      and result.attempt_id = attempt.id),
                   (select count(*)
                    from operations.user_operation_invocation_projections as projection
                    where projection.tenant_id = operation.tenant_id
                      and projection.attempt_id = attempt.id),
                   (select count(*)
                    from operations.user_operation_invocation_challenges as challenge
                    where challenge.tenant_id = operation.tenant_id
                      and challenge.attempt_id = attempt.id),
                   (select count(*)
                    from audit.audit_events as event
                    where event.tenant_id = operation.tenant_id
                      and event.causation_id = operation.id),
                   (select count(*)
                    from messaging.outbox_messages as message
                    where message.tenant_id = operation.tenant_id
                      and message.causation_id = operation.id)
            from control.user_operations as operation
            join operations.user_operation_invocation_attempts as attempt
              on attempt.tenant_id = operation.tenant_id
             and attempt.id = operation.current_invocation_attempt_id
            where operation.tenant_id = @tenant_id
              and operation.id = @operation_id
            """,
            connection);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, fixture.TenantId);
        command.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, fixture.OperationId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var snapshot = new ReconciliationAuthoritySnapshot(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            reader.GetInt64(7),
            reader.GetInt64(8));
        Assert.False(await reader.ReadAsync());
        return snapshot;
    }

    private static async Task<InvocationProtocolFixture> SeedInvocationProtocolFixtureAsync(
        PostgresTestDatabase database,
        string requestedTargetState = "active:ready",
        string operationType = "broker_account.credential_rotation",
        string targetType = "broker_account",
        TimeSpan? requestedInvocationWindow = null)
    {
        if (targetType is not ("broker_account" or "deployment"))
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetType),
                targetType,
                "The invocation fixture target type is unsupported.");
        }

        TenantExecutionContext ownerContext = NewContext();
        CredentialBoundaryFixture accounts = await SeedCredentialBoundaryFixtureAsync(
            database.Application,
            ownerContext);
        BrokerOperationFixture route = await SeedConfirmedBrokerOperationFixtureAsync(
            database,
            ownerContext,
            accounts.RotateAccountId,
            seedResults: false);

        Guid operationId = Guid.CreateVersion7();
        Guid idempotencyId = Guid.CreateVersion7();
        Guid claimToken = Guid.CreateVersion7();
        Guid attemptId = Guid.CreateVersion7();
        Guid dispatchMessageId = Guid.CreateVersion7();
        Guid auditEventId = Guid.CreateVersion7();
        Guid invocationId = Guid.CreateVersion7();
        Guid startReceiptId = Guid.CreateVersion7();
        Guid authorizationId = Guid.CreateVersion7();
        string resultCapability = RandomResultCapability();
        string deliveryCapability = RandomResultCapability();
        string gatewayCapability = RandomResultCapability();
        string redemptionCapability = RandomResultCapability();
        string receiptCapability = RandomResultCapability();

        Guid userId;
        Guid sessionId;
        Guid correlationId;
        long submittedResourceVersion;
        Guid gatewayActorId;
        Guid brokerAccountId;
        string region;
        long fenceGeneration;
        await using (NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync())
        await using (var source = new NpgsqlCommand(
            """
            select operation.user_id, operation.session_family_id,
                operation.correlation_id, account.row_version,
                deployment.row_version,
                assignment.gateway_host_identity::uuid,
                deployment.broker_account_id, deployment.region,
                deployment.fence_generation
            from control.user_operations as operation
            join operations.worker_assignments as assignment
              on assignment.tenant_id = operation.tenant_id
             and assignment.id = @assignment_id
            join operations.deployments as deployment
              on deployment.tenant_id = assignment.tenant_id
             and deployment.id = assignment.deployment_id
            join operations.broker_accounts as account
              on account.tenant_id = deployment.tenant_id
             and account.id = deployment.broker_account_id
            where operation.tenant_id = @tenant_id
              and operation.id = @source_operation_id
            """,
            administrator))
        {
            source.Parameters.AddWithValue("assignment_id", NpgsqlDbType.Uuid, route.WorkerAssignmentId);
            source.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, ownerContext.TenantId);
            source.Parameters.AddWithValue("source_operation_id", NpgsqlDbType.Uuid, route.RotateOperationId);
            await using NpgsqlDataReader reader = await source.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            userId = reader.GetGuid(0);
            sessionId = reader.GetGuid(1);
            correlationId = reader.GetGuid(2);
            submittedResourceVersion = targetType == "deployment"
                ? reader.GetInt64(4)
                : reader.GetInt64(3);
            gatewayActorId = reader.GetGuid(5);
            brokerAccountId = reader.GetGuid(6);
            region = reader.GetString(7);
            fenceGeneration = reader.GetInt64(8);
            Assert.False(await reader.ReadAsync());
        }
        Guid targetId = targetType == "deployment"
            ? route.DeploymentId
            : brokerAccountId;

        var operationContext = new TenantExecutionContext(
            ownerContext.TenantId,
            userId,
            correlationId,
            sessionId);
        await using (TenantPostgresTransaction transaction =
            await database.Application.BeginTenantTransactionAsync(operationContext))
        {
            await using NpgsqlCommand seed = transaction.CreateCommand(
                """
                insert into control.execution_safety_policies
                    (id, tenant_id, policy_version, scope_type, scope_id,
                     allow_new_deployment, allow_strategy_signals,
                     allow_exposure_increase, allow_exposure_reduction,
                     allow_protection, allow_pending_order_cancellation,
                     allow_emergency_close, lease_mode, worker_actions,
                     credential_mode, package_eligibility, reason, owner_id,
                     review_deadline, policy_digest, signature_algorithm,
                     signature_bytes, signature_sha256, signing_key_id, state)
                values
                    (@policy_id, @tenant_id, 1, 'global', null,
                     false, false, false, true, true, true, true,
                     'NORMAL', array[]::text[], 'NORMAL', 'ELIGIBLE',
                     'restrictive integration baseline', @user_id,
                     clock_timestamp() + interval '1 hour', @policy_digest,
                     'ECDSA_P256_SHA256_DER', @signature_bytes,
                     encode(sha256(@signature_bytes), 'hex'),
                     'integration-v4-key', 'active');

                insert into control.idempotency_records
                    (id, tenant_id, actor_id, operation, idempotency_key,
                     request_sha256, state, created_at, expires_at)
                values
                    (@idempotency_id, @tenant_id, @user_id,
                     @operation_type, @idempotency_key,
                     @request_sha256, 'processing', clock_timestamp(),
                     clock_timestamp() + interval '1 hour');

                insert into control.user_operations
                    (id, tenant_id, user_id, session_family_id, operation_type,
                     target_type, target_id, state, idempotency_record_id,
                     expected_resource_version, submitted_resource_version,
                     requested_target_state, reason, correlation_id,
                     claimed_by, claim_token, claim_expires_at,
                     created_at, updated_at)
                values
                    (@operation_id, @tenant_id, @user_id, @session_id,
                     @operation_type, @target_type,
                     @target_id, 'dispatching', @idempotency_id,
                     @submitted_resource_version, @submitted_resource_version,
                     @requested_target_state, 'invocation-v4 integration fact',
                     @correlation_id, 'integration-worker', @claim_token,
                     clock_timestamp() + interval '5 minutes',
                     clock_timestamp(), clock_timestamp());
                """);
            seed.Parameters.AddWithValue("policy_id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
            seed.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, ownerContext.TenantId);
            seed.Parameters.AddWithValue("user_id", NpgsqlDbType.Uuid, userId);
            seed.Parameters.AddWithValue("operation_type", NpgsqlDbType.Text, operationType);
            seed.Parameters.AddWithValue("policy_digest", NpgsqlDbType.Text, RandomHexDigest());
            seed.Parameters.AddWithValue("signature_bytes", NpgsqlDbType.Bytea, new byte[64]);
            seed.Parameters.AddWithValue("idempotency_id", NpgsqlDbType.Uuid, idempotencyId);
            seed.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Text, $"v4-{operationId:N}");
            seed.Parameters.AddWithValue("request_sha256", NpgsqlDbType.Text, RandomHexDigest());
            seed.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, operationId);
            seed.Parameters.AddWithValue("session_id", NpgsqlDbType.Uuid, sessionId);
            seed.Parameters.AddWithValue("target_type", NpgsqlDbType.Text, targetType);
            seed.Parameters.AddWithValue("target_id", NpgsqlDbType.Uuid, targetId);
            seed.Parameters.AddWithValue(
                "submitted_resource_version",
                NpgsqlDbType.Bigint,
                submittedResourceVersion);
            seed.Parameters.AddWithValue(
                "requested_target_state",
                NpgsqlDbType.Text,
                requestedTargetState);
            seed.Parameters.AddWithValue("correlation_id", NpgsqlDbType.Uuid, correlationId);
            seed.Parameters.AddWithValue("claim_token", NpgsqlDbType.Uuid, claimToken);
            Assert.Equal(3, await seed.ExecuteNonQueryAsync());
            await transaction.CommitAsync();
        }

        var workerContext = new TenantExecutionContext(
            ownerContext.TenantId,
            InvocationWorkerActorId,
            correlationId,
            null);
        CreationReceipt creation;
        await using (TenantPostgresTransaction transaction =
            await database.Worker.BeginTenantTransactionAsync(workerContext))
        {
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                select creation_status, attempt_id, dispatch_message_id,
                    attempt_number, command_sha256, execute_not_after,
                    result_capability_expires_at, route_deployment_id,
                    fence_generation, worker_assignment_id, worker_instance_id
                from control.create_user_operation_invocation_attempt(
                    @attempt_id, @operation_id, @claim_token, 0,
                    @dispatch_message_id, @audit_event_id,
                    @raw_result_capability, @raw_delivery_capability,
                    @requested_invocation_window, interval '10 minutes',
                    interval '5 seconds')
                """);
            command.Parameters.AddWithValue("attempt_id", NpgsqlDbType.Uuid, attemptId);
            command.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, operationId);
            command.Parameters.AddWithValue("claim_token", NpgsqlDbType.Uuid, claimToken);
            command.Parameters.AddWithValue("dispatch_message_id", NpgsqlDbType.Uuid, dispatchMessageId);
            command.Parameters.AddWithValue("audit_event_id", NpgsqlDbType.Uuid, auditEventId);
            command.Parameters.AddWithValue("raw_result_capability", NpgsqlDbType.Text, resultCapability);
            command.Parameters.AddWithValue("raw_delivery_capability", NpgsqlDbType.Text, deliveryCapability);
            command.Parameters.AddWithValue(
                "requested_invocation_window",
                NpgsqlDbType.Interval,
                requestedInvocationWindow ?? TimeSpan.FromMinutes(2));
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            creation = new CreationReceipt(
                reader.GetString(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetFieldValue<DateTimeOffset>(6),
                reader.GetGuid(7),
                reader.GetInt64(8),
                reader.GetGuid(9),
                reader.GetGuid(10));
            Assert.False(await reader.ReadAsync());
            await reader.CloseAsync();
            await transaction.CommitAsync();
        }
        Assert.Equal("created", creation.Status);
        Assert.Equal(attemptId, creation.AttemptId);
        Assert.Equal(dispatchMessageId, creation.DispatchMessageId);
        Assert.Equal(1, creation.AttemptNumber);
        Assert.Equal(route.DeploymentId, creation.RouteDeploymentId);
        Assert.Equal(fenceGeneration, creation.FenceGeneration);
        Assert.Equal(route.WorkerAssignmentId, creation.WorkerAssignmentId);
        Assert.Equal(route.WorkerInstanceId, creation.WorkerInstanceId);

        string dispatchTargetBindingSha256;
        await using (NpgsqlConnection administrator =
            await database.Administrator.OpenConnectionAsync())
        await using (var binding = new NpgsqlCommand(
            """
            select dispatch_target_binding_sha256
            from operations.user_operation_invocation_attempts
            where tenant_id = @tenant_id and id = @attempt_id
            """,
            administrator))
        {
            binding.Parameters.AddWithValue(
                "tenant_id",
                NpgsqlDbType.Uuid,
                ownerContext.TenantId);
            binding.Parameters.AddWithValue("attempt_id", NpgsqlDbType.Uuid, attemptId);
            dispatchTargetBindingSha256 = Assert.IsType<string>(
                await binding.ExecuteScalarAsync());
        }

        UserOperationTargetObservation targetObservation = targetType == "deployment"
            ? UserOperationDeploymentTargetObservation.Create(
                requestedTargetState,
                dispatchTargetBindingSha256,
                RandomHexDigest(),
                brokerConfirmed: true,
                RandomHexDigest(),
                requestedTargetState,
                requestedTargetState == "stopped" ? "flat" : "open")
            : UserOperationBrokerTargetObservation.Create(
                "active",
                "ready",
                brokerConfirmed: true);
        string targetObservationJson = targetObservation.ToCanonicalJson();
        string observationSha256 = targetObservation.ComputeCanonicalSha256();

        return new InvocationProtocolFixture(
            ownerContext.TenantId,
            userId,
            sessionId,
            correlationId,
            operationId,
            attemptId,
            dispatchMessageId,
            invocationId,
            startReceiptId,
            authorizationId,
            operationType,
            targetType,
            targetId,
            requestedTargetState,
            brokerAccountId,
            route.DeploymentId,
            route.WorkerAssignmentId,
            route.WorkerInstanceId,
            route.SupervisorWorkloadId,
            gatewayActorId,
            fenceGeneration,
            region,
            submittedResourceVersion,
            resultCapability,
            deliveryCapability,
            gatewayCapability,
            redemptionCapability,
            receiptCapability,
            targetObservationJson,
            observationSha256,
            creation.CommandSha256,
            dispatchTargetBindingSha256,
            creation.ExecuteNotAfter,
            creation.ResultCapabilityExpiresAt);
    }

    private static async Task AssertRequestedV4CanonicalEnvelopeAsync(
        PostgresTestDatabase database,
        InvocationProtocolFixture fixture)
    {
        await using NpgsqlConnection connection =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select message_type, schema_version, payload::text,
                payload_sha256, occurred_at
            from messaging.outbox_messages
            where tenant_id = @tenant_id and id = @message_id
            """,
            connection);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, fixture.TenantId);
        command.Parameters.AddWithValue("message_id", NpgsqlDbType.Uuid, fixture.DispatchMessageId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        string messageType = reader.GetString(0);
        int schemaVersion = reader.GetInt32(1);
        string payload = reader.GetString(2);
        string payloadSha256 = reader.GetString(3);
        DateTimeOffset occurredAt = reader.GetFieldValue<DateTimeOffset>(4);
        Assert.False(await reader.ReadAsync());

        var claimed = new ClaimedOutboxItem(
            fixture.DispatchMessageId,
            fixture.TenantId,
            messageType,
            schemaVersion,
            payload,
            payloadSha256,
            occurredAt,
            1);
        OutboxDeliveryEnvelope envelope = OutboxDeliveryEnvelope.Create(claimed);
        UserOperationDeliveryRequestedV4 parsed =
            UserOperationDeliveryRequestedV4.ParseCanonical(
                envelope.MessageType,
                envelope.PayloadJson);
        Assert.Equal(4, envelope.SchemaVersion);
        Assert.Equal(fixture.AttemptId, parsed.AttemptId);
        Assert.Equal(fixture.OperationId, parsed.OperationId);
        Assert.Equal(fixture.DispatchMessageId, parsed.DispatchMessageId);
        Assert.Equal(fixture.OperationType, parsed.OperationType);
        Assert.Equal(fixture.TargetType, parsed.TargetType);
        Assert.Equal(fixture.TargetId, parsed.TargetId);
        Assert.Equal(fixture.SubmittedResourceVersion, parsed.SubmittedResourceVersion);
        Assert.Equal(fixture.RequestedTargetState, parsed.RequestedTargetState);
        Assert.Equal(
            fixture.DispatchTargetBindingSha256,
            parsed.DispatchTargetBindingSha256);
        Assert.Equal(fixture.DeliveryCapability, parsed.DeliveryCapability.DangerousGetValue());
        Assert.Equal(fixture.ResultCapability, parsed.ResultCapability.DangerousGetValue());
        Assert.Equal(fixture.ExecuteNotAfter, parsed.ExecuteNotAfterUtc);
        Assert.Equal(fixture.ResultCapabilityExpiresAt, parsed.ResultCapabilityExpiresAtUtc);
        Assert.Equal(envelope.PayloadJson, parsed.ToCanonicalJson());
    }

    private static async Task AssertLegacyResultIngressDeniedAsync(
        PostgresTestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.RuntimeEvidenceConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select
                not has_function_privilege(current_user,
                    'control.record_broker_user_operation_result(uuid,uuid,uuid,uuid,text,uuid,bigint,text,text,text,text,boolean,boolean,boolean,text,text,text,text,text,timestamptz)',
                    'EXECUTE'),
                not has_function_privilege(current_user,
                    'control.record_deployment_user_operation_result(uuid,uuid,uuid,uuid,text,uuid,bigint,text,text,text,text,boolean,boolean,text,text,text,boolean,text,text,text,text,text,timestamptz)',
                    'EXECUTE'),
                has_function_privilege(current_user,
                    'control.record_user_operation_result_v5(uuid,uuid,uuid,uuid,uuid,uuid,uuid,uuid,text,uuid,uuid,uuid,text,text,uuid,jsonb,bigint,text,text,text,text,text,timestamptz,text,uuid,uuid,uuid,bigint,text)',
                    'EXECUTE'),
                not has_function_privilege(current_user,
                    'control.acquire_u0_authority_lock()', 'EXECUTE'),
                not has_schema_privilege(current_user, 'audit', 'USAGE'),
                not has_schema_privilege(current_user, 'messaging', 'USAGE'),
                not has_schema_privilege(current_user, 'operations', 'USAGE'),
                not has_table_privilege(current_user,
                    (select relation.oid
                     from pg_catalog.pg_class as relation
                     join pg_catalog.pg_namespace as namespace
                       on namespace.oid = relation.relnamespace
                     where namespace.nspname = 'audit'
                       and relation.relname = 'audit_events'), 'INSERT'),
                not has_any_column_privilege(current_user,
                    (select relation.oid
                     from pg_catalog.pg_class as relation
                     join pg_catalog.pg_namespace as namespace
                       on namespace.oid = relation.relnamespace
                     where namespace.nspname = 'audit'
                       and relation.relname = 'audit_events'), 'INSERT'),
                not has_table_privilege(current_user,
                    (select relation.oid
                     from pg_catalog.pg_class as relation
                     join pg_catalog.pg_namespace as namespace
                       on namespace.oid = relation.relnamespace
                     where namespace.nspname = 'messaging'
                       and relation.relname = 'outbox_messages'), 'INSERT'),
                not has_any_column_privilege(current_user,
                    (select relation.oid
                     from pg_catalog.pg_class as relation
                     join pg_catalog.pg_namespace as namespace
                       on namespace.oid = relation.relnamespace
                     where namespace.nspname = 'messaging'
                       and relation.relname = 'outbox_messages'), 'INSERT')
            """,
            connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0));
        Assert.True(reader.GetBoolean(1));
        Assert.True(reader.GetBoolean(2));
        Assert.True(reader.GetBoolean(3));
        Assert.True(reader.GetBoolean(4));
        Assert.True(reader.GetBoolean(5));
        Assert.True(reader.GetBoolean(6));
        Assert.True(reader.GetBoolean(7));
        Assert.True(reader.GetBoolean(8));
        Assert.True(reader.GetBoolean(9));
        Assert.True(reader.GetBoolean(10));
        Assert.False(await reader.ReadAsync());
    }

    private static async Task<DeliveryClaimReceipt> ClaimInvocationDeliveryAsync(
        PostgresTestDatabase database,
        InvocationProtocolFixture fixture)
    {
        var context = new TenantExecutionContext(
            fixture.TenantId,
            fixture.SupervisorActorId,
            fixture.CorrelationId,
            null);
        await using TenantPostgresTransaction transaction =
            await database.SupervisorRuntime.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select claim_status, delivery_claim_id, delivery_claim_generation,
                delivery_claimed_at, gateway_capability_expires_at,
                execute_not_after
            from control.claim_user_operation_delivery(
                @attempt_id, @raw_delivery_capability, @delivery_claim_id,
                @raw_gateway_capability, interval '90 seconds',
                @worker_instance_id, @deployment_id, @broker_account_id,
                @fence_generation, @region)
            """);
        Guid claimId = Guid.CreateVersion7();
        command.Parameters.AddWithValue("attempt_id", NpgsqlDbType.Uuid, fixture.AttemptId);
        command.Parameters.AddWithValue("raw_delivery_capability", NpgsqlDbType.Text, fixture.DeliveryCapability);
        command.Parameters.AddWithValue("delivery_claim_id", NpgsqlDbType.Uuid, claimId);
        command.Parameters.AddWithValue("raw_gateway_capability", NpgsqlDbType.Text, fixture.GatewayCapability);
        AddRouteParameters(command, fixture);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var result = new DeliveryClaimReceipt(
            reader.GetString(0),
            reader.GetGuid(1),
            reader.GetInt32(2),
            reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetFieldValue<DateTimeOffset>(5));
        Assert.False(await reader.ReadAsync());
        await reader.CloseAsync();
        await transaction.CommitAsync();
        Assert.Equal("claimed", result.Status);
        Assert.Equal(claimId, result.ClaimId);
        Assert.Equal(1, result.Generation);
        Assert.True(result.ClaimedAt < result.GatewayExpiresAt);
        Assert.True(result.GatewayExpiresAt <= result.ExecuteNotAfter);
        return result;
    }

    private static async Task<BeginInvocationReceipt> BeginInvocationAsync(
        PostgresTestDatabase database,
        InvocationProtocolFixture fixture,
        DeliveryClaimReceipt claim,
        string proposedRedemptionCapability,
        string proposedReceiptCapability)
    {
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
                @raw_redemption_capability, @raw_receipt_capability,
                interval '2 minutes', @worker_instance_id, @deployment_id,
                @broker_account_id, @fence_generation, @region)
            """);
        command.Parameters.AddWithValue("attempt_id", NpgsqlDbType.Uuid, fixture.AttemptId);
        command.Parameters.AddWithValue("delivery_claim_id", NpgsqlDbType.Uuid, claim.ClaimId);
        command.Parameters.AddWithValue(
            "delivery_claim_generation",
            NpgsqlDbType.Integer,
            claim.Generation);
        command.Parameters.AddWithValue("raw_gateway_capability", NpgsqlDbType.Text, fixture.GatewayCapability);
        command.Parameters.AddWithValue("invocation_id", NpgsqlDbType.Uuid, fixture.InvocationId);
        command.Parameters.AddWithValue("start_receipt_id", NpgsqlDbType.Uuid, fixture.StartReceiptId);
        command.Parameters.AddWithValue("raw_redemption_capability", NpgsqlDbType.Text, proposedRedemptionCapability);
        command.Parameters.AddWithValue("raw_receipt_capability", NpgsqlDbType.Text, proposedReceiptCapability);
        AddRouteParameters(command, fixture);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var result = new BeginInvocationReceipt(
            reader.GetString(0),
            reader.GetFieldValue<DateTimeOffset>(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetFieldValue<DateTimeOffset>(5));
        Assert.False(await reader.ReadAsync());
        await reader.CloseAsync();
        await transaction.CommitAsync();
        return result;
    }

    private static async Task AssertStaleDeliveryClaimGenerationDeniedAsync(
        PostgresTestDatabase database,
        InvocationProtocolFixture fixture,
        DeliveryClaimReceipt claim)
    {
        var supervisorContext = new TenantExecutionContext(
            fixture.TenantId,
            fixture.SupervisorActorId,
            fixture.CorrelationId,
            null);
        await using (TenantPostgresTransaction transaction =
            await database.SupervisorRuntime.BeginTenantTransactionAsync(supervisorContext))
        {
            await using NpgsqlCommand reject = transaction.CreateCommand(
                """
                select rejection_status
                from control.reject_user_operation_before_invocation(
                    @attempt_id, @delivery_claim_id, @stale_generation,
                    @raw_gateway_capability, @receipt_id,
                    'supervisor_rejected_before_invocation',
                    @worker_instance_id, @deployment_id, @broker_account_id,
                    @fence_generation, @region)
                """);
            reject.Parameters.AddWithValue("attempt_id", NpgsqlDbType.Uuid, fixture.AttemptId);
            reject.Parameters.AddWithValue("delivery_claim_id", NpgsqlDbType.Uuid, claim.ClaimId);
            reject.Parameters.AddWithValue("stale_generation", NpgsqlDbType.Integer, claim.Generation + 1);
            reject.Parameters.AddWithValue("raw_gateway_capability", NpgsqlDbType.Text, fixture.GatewayCapability);
            reject.Parameters.AddWithValue("receipt_id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
            AddRouteParameters(reject, fixture);
            Assert.Null(await reject.ExecuteScalarAsync());
            await transaction.RollbackAsync();
        }

        var gatewayContext = new TenantExecutionContext(
            fixture.TenantId,
            fixture.GatewayActorId,
            fixture.CorrelationId,
            null);
        await using (TenantPostgresTransaction transaction =
            await database.GatewayRuntime.BeginTenantTransactionAsync(gatewayContext))
        {
            await using NpgsqlCommand begin = transaction.CreateCommand(
                """
                select begin_status
                from control.begin_user_operation_gateway_invocation(
                    @attempt_id, @delivery_claim_id, @stale_generation,
                    @raw_gateway_capability, @invocation_id, @start_receipt_id,
                    @raw_redemption_capability, @raw_receipt_capability,
                    interval '2 minutes', @worker_instance_id, @deployment_id,
                    @broker_account_id, @fence_generation, @region)
                """);
            begin.Parameters.AddWithValue("attempt_id", NpgsqlDbType.Uuid, fixture.AttemptId);
            begin.Parameters.AddWithValue("delivery_claim_id", NpgsqlDbType.Uuid, claim.ClaimId);
            begin.Parameters.AddWithValue("stale_generation", NpgsqlDbType.Integer, claim.Generation + 1);
            begin.Parameters.AddWithValue("raw_gateway_capability", NpgsqlDbType.Text, fixture.GatewayCapability);
            begin.Parameters.AddWithValue("invocation_id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
            begin.Parameters.AddWithValue("start_receipt_id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
            begin.Parameters.AddWithValue("raw_redemption_capability", NpgsqlDbType.Text, RandomResultCapability());
            begin.Parameters.AddWithValue("raw_receipt_capability", NpgsqlDbType.Text, RandomResultCapability());
            AddRouteParameters(begin, fixture);
            Assert.Null(await begin.ExecuteScalarAsync());
            await transaction.RollbackAsync();
        }
    }

    private static async Task<ProviderAuthorizationReceipt> AuthorizeProviderCallAsync(
        PostgresTestDatabase database,
        InvocationProtocolFixture fixture)
    {
        var context = new TenantExecutionContext(
            fixture.TenantId,
            fixture.GatewayActorId,
            fixture.CorrelationId,
            null);
        await using TenantPostgresTransaction transaction =
            await database.CredentialRuntime.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select authorization_status, provider_call_authorized,
                provider_call_authorized_at, execute_not_after,
                command_descriptor::text, authorization_receipt_sha256
            from control.authorize_user_operation_provider_call(
                @attempt_id, @invocation_id, @start_receipt_id,
                @authorization_id, @raw_redemption_capability,
                @worker_instance_id, @deployment_id, @broker_account_id,
                @fence_generation, @region)
            """);
        command.Parameters.AddWithValue("attempt_id", NpgsqlDbType.Uuid, fixture.AttemptId);
        command.Parameters.AddWithValue("invocation_id", NpgsqlDbType.Uuid, fixture.InvocationId);
        command.Parameters.AddWithValue("start_receipt_id", NpgsqlDbType.Uuid, fixture.StartReceiptId);
        command.Parameters.AddWithValue("authorization_id", NpgsqlDbType.Uuid, fixture.AuthorizationId);
        command.Parameters.AddWithValue("raw_redemption_capability", NpgsqlDbType.Text, fixture.RedemptionCapability);
        AddRouteParameters(command, fixture);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var result = new ProviderAuthorizationReceipt(
            reader.GetString(0),
            reader.GetBoolean(1),
            reader.GetFieldValue<DateTimeOffset>(2),
            reader.GetFieldValue<DateTimeOffset>(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5));
        Assert.False(await reader.ReadAsync());
        await reader.CloseAsync();
        await transaction.CommitAsync();
        return result;
    }

    private static async Task<GatewayObservationReceipt> RecordGatewayObservationAsync(
        PostgresTestDatabase database,
        InvocationProtocolFixture fixture,
        string outcome = "succeeded")
    {
        var context = new TenantExecutionContext(
            fixture.TenantId,
            fixture.GatewayActorId,
            fixture.CorrelationId,
            null);
        await using TenantPostgresTransaction transaction =
            await database.GatewayRuntime.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select observation_status, gateway_observation_receipt_id,
                outcome, observation_receipt_sha256, target_observation::text,
                observed_at, received_at, state_version
            from control.record_user_operation_gateway_observation_v5(
                p_attempt_id => @attempt_id,
                p_invocation_id => @invocation_id,
                p_start_receipt_id => @start_receipt_id,
                p_authorization_id => @authorization_id,
                p_raw_receipt_capability => @raw_receipt_capability,
                p_outcome => @outcome,
                p_observation_sha256 => @observation_sha256,
                p_observed_at => clock_timestamp(),
                p_target_observation => @target_observation,
                p_expected_worker_instance_id => @worker_instance_id,
                p_expected_deployment_id => @deployment_id,
                p_expected_broker_account_id => @broker_account_id,
                p_expected_fence_generation => @fence_generation,
                p_expected_region => @region)
            """);
        command.Parameters.AddWithValue("attempt_id", NpgsqlDbType.Uuid, fixture.AttemptId);
        command.Parameters.AddWithValue("invocation_id", NpgsqlDbType.Uuid, fixture.InvocationId);
        command.Parameters.AddWithValue("start_receipt_id", NpgsqlDbType.Uuid, fixture.StartReceiptId);
        command.Parameters.AddWithValue("authorization_id", NpgsqlDbType.Uuid, fixture.AuthorizationId);
        command.Parameters.AddWithValue("raw_receipt_capability", NpgsqlDbType.Text, fixture.ReceiptCapability);
        command.Parameters.AddWithValue("outcome", NpgsqlDbType.Text, outcome);
        command.Parameters.AddWithValue("observation_sha256", NpgsqlDbType.Text, fixture.ObservationSha256);
        command.Parameters.AddWithValue("target_observation", NpgsqlDbType.Jsonb, fixture.TargetObservationJson);
        AddRouteParameters(command, fixture);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var result = new GatewayObservationReceipt(
            reader.GetString(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetInt64(7));
        Assert.False(await reader.ReadAsync());
        await reader.CloseAsync();
        await transaction.CommitAsync();
        return result;
    }

    private static async Task AssertGatewayObservationDuplicateAsync(
        PostgresTestDatabase database,
        InvocationProtocolFixture fixture,
        GatewayObservationReceipt recorded)
    {
        var context = new TenantExecutionContext(
            fixture.TenantId,
            fixture.GatewayActorId,
            fixture.CorrelationId,
            null);
        await using TenantPostgresTransaction transaction =
            await database.GatewayRuntime.BeginTenantTransactionAsync(context);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select observation_status, gateway_observation_receipt_id,
                outcome, observation_receipt_sha256, target_observation::text,
                observed_at, received_at, state_version
            from control.record_user_operation_gateway_observation_v5(
                p_attempt_id => @attempt_id,
                p_invocation_id => @invocation_id,
                p_start_receipt_id => @start_receipt_id,
                p_authorization_id => @authorization_id,
                p_raw_receipt_capability => @raw_receipt_capability,
                p_outcome => @outcome,
                p_observation_sha256 => @observation_sha256,
                p_observed_at => @observed_at,
                p_target_observation => @target_observation,
                p_expected_worker_instance_id => @worker_instance_id,
                p_expected_deployment_id => @deployment_id,
                p_expected_broker_account_id => @broker_account_id,
                p_expected_fence_generation => @fence_generation,
                p_expected_region => @region)
            """);
        command.Parameters.AddWithValue("attempt_id", NpgsqlDbType.Uuid, fixture.AttemptId);
        command.Parameters.AddWithValue("invocation_id", NpgsqlDbType.Uuid, fixture.InvocationId);
        command.Parameters.AddWithValue("start_receipt_id", NpgsqlDbType.Uuid, fixture.StartReceiptId);
        command.Parameters.AddWithValue("authorization_id", NpgsqlDbType.Uuid, fixture.AuthorizationId);
        command.Parameters.AddWithValue("raw_receipt_capability", NpgsqlDbType.Text, fixture.ReceiptCapability);
        command.Parameters.AddWithValue("outcome", NpgsqlDbType.Text, recorded.Outcome);
        command.Parameters.AddWithValue("observation_sha256", NpgsqlDbType.Text, fixture.ObservationSha256);
        command.Parameters.AddWithValue("observed_at", NpgsqlDbType.TimestampTz, recorded.ObservedAt);
        command.Parameters.AddWithValue("target_observation", NpgsqlDbType.Jsonb, fixture.TargetObservationJson);
        AddRouteParameters(command, fixture);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("duplicate", reader.GetString(0));
        Assert.Equal(recorded.ReceiptId, reader.GetGuid(1));
        Assert.Equal(recorded.Outcome, reader.GetString(2));
        Assert.Equal(recorded.ReceiptSha256, reader.GetString(3));
        AssertJsonEquivalent(recorded.TargetObservationJson, reader.GetString(4));
        Assert.Equal(recorded.ObservedAt, reader.GetFieldValue<DateTimeOffset>(5));
        Assert.Equal(recorded.ReceivedAt, reader.GetFieldValue<DateTimeOffset>(6));
        Assert.Equal(recorded.StateVersion, reader.GetInt64(7));
        Assert.False(await reader.ReadAsync());
        await reader.CloseAsync();
        await transaction.CommitAsync();
    }

    private static async Task<ReconciliationReceipt> ReconcileObservedAttemptAsync(
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
        long rowVersion;
        await using (NpgsqlCommand claim = transaction.CreateCommand(
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
            """))
        {
            claim.Parameters.AddWithValue("claim_token", NpgsqlDbType.Uuid, claimToken);
            claim.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, fixture.TenantId);
            claim.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, fixture.OperationId);
            rowVersion = Assert.IsType<long>(await claim.ExecuteScalarAsync());
        }

        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select reconciliation_status, proof_source, outcome,
                observation_sha256, target_observation::text,
                projection_status, projected_target_row_version
            from control.reconcile_user_operation_invocation_attempt(
                @operation_id, @claim_token, @expected_row_version)
            """);
        command.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, fixture.OperationId);
        command.Parameters.AddWithValue("claim_token", NpgsqlDbType.Uuid, claimToken);
        command.Parameters.AddWithValue("expected_row_version", NpgsqlDbType.Bigint, rowVersion);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var result = new ReconciliationReceipt(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetInt64(6));
        Assert.False(await reader.ReadAsync());
        await reader.CloseAsync();
        await transaction.CommitAsync();
        Assert.Equal(fixture.ObservationSha256, result.ObservationSha256);
        return result;
    }

    private static void AddRouteParameters(
        NpgsqlCommand command,
        InvocationProtocolFixture fixture)
    {
        command.Parameters.AddWithValue("worker_instance_id", NpgsqlDbType.Uuid, fixture.WorkerInstanceId);
        command.Parameters.AddWithValue("deployment_id", NpgsqlDbType.Uuid, fixture.DeploymentId);
        command.Parameters.AddWithValue("broker_account_id", NpgsqlDbType.Uuid, fixture.BrokerAccountId);
        command.Parameters.AddWithValue("fence_generation", NpgsqlDbType.Bigint, fixture.FenceGeneration);
        command.Parameters.AddWithValue("region", NpgsqlDbType.Text, fixture.Region);
    }

    private static void AssertJsonEquivalent(string expected, string actual) =>
        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual)),
            $"JSON values differ. Expected: {expected}; Actual: {actual}");

    private static async Task WaitForInvocationWorkerReadinessAsync(
        PostgresWorkerReadiness readiness)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(90);
        while (!await readiness.IsReadyAsync(CancellationToken.None))
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                Assert.Fail(
                    "The invocation worker readiness probe did not become ready within 90 seconds.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
    }

    private static async Task<ControlWorkCycleResult> RunProductionInvocationCycleAsync(
        PostgresTestDatabase database,
        string workerIdentity)
    {
        var options = new ControlWorkOptions
        {
            TenantBatchSize = 16,
            OperationBatchSizePerTenant = 16,
            DependencyTimeout = TimeSpan.FromSeconds(10),
            OperationTimeout = TimeSpan.FromSeconds(30)
        };
        options.Validate();
        var readiness = new PostgresWorkerReadiness(database.Worker);
        await WaitForInvocationWorkerReadinessAsync(readiness);
        var catalog = new PostgresWorkerTenantCatalog(
            database.Worker,
            readiness,
            options);

        using ECDsa policyKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] publicKey = policyKey.ExportSubjectPublicKeyInfo();
        try
        {
            using var trustStore = new WorkerPolicySignatureTrustStore(
                new Dictionary<string, byte[]>
                {
                    ["integration-v4-key"] = publicKey
                });
            var store = new PostgresUserOperationWorkStore(
                database.Worker,
                readiness,
                catalog,
                options,
                OutboxWorkerIdentity.Create(workerIdentity),
                trustStore,
                TimeProvider.System);
            return await store.RunCycleAsync(
                DateTimeOffset.UtcNow,
                CancellationToken.None);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(publicKey);
        }
    }

    private static async Task<DeploymentProjectionSnapshot>
        ReadDeploymentProjectionSnapshotAsync(
            PostgresTestDatabase database,
            Guid tenantId,
            Guid deploymentId)
    {
        await using NpgsqlConnection connection =
            await database.Administrator.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            select (to_jsonb(deployment)
                        - 'observed_state'
                        - 'last_reconciled_at'
                        - 'row_version'
                        - 'updated_at')::text,
                deployment.desired_state,
                deployment.observed_state,
                deployment.row_version,
                deployment.last_reconciled_at,
                deployment.updated_at
            from operations.deployments as deployment
            where deployment.tenant_id = @tenant_id
              and deployment.id = @deployment_id
            """,
            connection);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
        command.Parameters.AddWithValue("deployment_id", NpgsqlDbType.Uuid, deploymentId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var snapshot = new DeploymentProjectionSnapshot(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetFieldValue<DateTimeOffset>(5));
        Assert.False(await reader.ReadAsync());
        return snapshot;
    }

    private sealed record InvocationProtocolFixture(
        Guid TenantId,
        Guid UserId,
        Guid SessionId,
        Guid CorrelationId,
        Guid OperationId,
        Guid AttemptId,
        Guid DispatchMessageId,
        Guid InvocationId,
        Guid StartReceiptId,
        Guid AuthorizationId,
        string OperationType,
        string TargetType,
        Guid TargetId,
        string RequestedTargetState,
        Guid BrokerAccountId,
        Guid DeploymentId,
        Guid WorkerAssignmentId,
        Guid WorkerInstanceId,
        Guid SupervisorActorId,
        Guid GatewayActorId,
        long FenceGeneration,
        string Region,
        long SubmittedResourceVersion,
        string ResultCapability,
        string DeliveryCapability,
        string GatewayCapability,
        string RedemptionCapability,
        string ReceiptCapability,
        string TargetObservationJson,
        string ObservationSha256,
        string CommandSha256,
        string DispatchTargetBindingSha256,
        DateTimeOffset ExecuteNotAfter,
        DateTimeOffset ResultCapabilityExpiresAt);

    private sealed record DeploymentProjectionSnapshot(
        string NonProjectionFieldsJson,
        string DesiredState,
        string ObservedState,
        long RowVersion,
        DateTimeOffset? LastReconciledAt,
        DateTimeOffset UpdatedAt);

    private enum ReconciliationAuthorityVariant
    {
        WrongActor,
        WrongCorrelation
    }

    private enum ReconciliationEvidenceBranch
    {
        NotSent,
        PersistedDivergedResult,
        ObservationOnlyDiverged,
        AwaitingEvidence,
        ChallengeOutstanding,
        ReconciliationResultDiverged,
        SucceededProjection,
        AlreadyProjectedSuccess
    }

    private sealed record ReconciliationClaim(Guid ClaimToken, long RowVersion);

    private sealed record ReconciliationBoundaryReceipt(
        string Status,
        string? ProofSource,
        string? Outcome,
        string? ProjectionStatus);

    private sealed record ReconciliationAuthorityScenario(
        InvocationProtocolFixture Fixture,
        ReconciliationClaim Claim,
        ReconciliationBoundaryReceipt Expected);

    private sealed record IssuedAuthorityChallenge(
        InvocationChallengeReceipt Receipt,
        string RawCapability,
        ChallengeRoute Route);

    private sealed record ReconciliationAuthoritySnapshot(
        string OperationJson,
        string AttemptJson,
        string TargetJson,
        long ReceiptCount,
        long ResultCount,
        long ProjectionCount,
        long ChallengeCount,
        long AuditCount,
        long OutboxCount);

    private sealed record CreationReceipt(
        string Status,
        Guid AttemptId,
        Guid DispatchMessageId,
        int AttemptNumber,
        string CommandSha256,
        DateTimeOffset ExecuteNotAfter,
        DateTimeOffset ResultCapabilityExpiresAt,
        Guid RouteDeploymentId,
        long FenceGeneration,
        Guid WorkerAssignmentId,
        Guid WorkerInstanceId);

    private sealed record DeliveryClaimReceipt(
        string Status,
        Guid ClaimId,
        int Generation,
        DateTimeOffset ClaimedAt,
        DateTimeOffset GatewayExpiresAt,
        DateTimeOffset ExecuteNotAfter);

    private sealed record BeginInvocationReceipt(
        string Status,
        DateTimeOffset PreparedAt,
        string? RedemptionCapability,
        string? ReceiptCapability,
        DateTimeOffset RedemptionExpiresAt,
        DateTimeOffset ReceiptDeadline);

    private sealed record ProviderAuthorizationReceipt(
        string Status,
        bool ProviderCallAuthorized,
        DateTimeOffset AuthorizedAt,
        DateTimeOffset ExecuteNotAfter,
        string? CommandDescriptor,
        string? AuthorizationReceiptSha256);

    private sealed record GatewayObservationReceipt(
        string Status,
        Guid ReceiptId,
        string Outcome,
        string ReceiptSha256,
        string TargetObservationJson,
        DateTimeOffset ObservedAt,
        DateTimeOffset ReceivedAt,
        long StateVersion);

    private sealed record ReconciliationReceipt(
        string Status,
        string ProofSource,
        string Outcome,
        string ObservationSha256,
        string TargetObservationJson,
        string ProjectionStatus,
        long? ProjectedTargetRowVersion);

    private sealed class ConclusiveBrokerProviderInvoker : IUserOperationProviderCallInvoker
    {
        public int CallCount { get; private set; }

        public Task<UserOperationProviderInvocationObservation> InvokeOnceAsync(
            UserOperationProviderCommand command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            var observedAt = new DateTimeOffset(
                DateTimeOffset.UtcNow.Ticks / 10 * 10,
                TimeSpan.Zero);
            return Task.FromResult(UserOperationProviderInvocationObservation.Create(
                command,
                UserOperationObservationOutcome.Succeeded,
                UserOperationBrokerTargetObservation.Create("active", "ready", true),
                observedAt));
        }
    }
}
