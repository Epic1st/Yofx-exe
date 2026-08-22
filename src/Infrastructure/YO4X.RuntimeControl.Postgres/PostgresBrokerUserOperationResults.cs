using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.Persistence.Postgres;
using YO4X.Tenancy;

namespace YO4X.RuntimeControl.Postgres;

public sealed partial class PostgresRuntimeControlPlaneApplication
{
    public async Task<BrokerUserOperationResultAcceptance> RecordBrokerUserOperationResultAsync(
        WorkloadActor actor,
        Guid brokerAccountId,
        BrokerUserOperationResultInput request,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireSupervisor(actor);

        string requestSha256 = CanonicalJson.Sha256(request);
        await using TenantPostgresTransaction transaction = await BeginBrokerEvidenceAsync(
                actor,
                metadata,
                cancellationToken)
            .ConfigureAwait(false);
        BrokerEvidenceBinding binding = await LoadBrokerEvidenceBindingAsync(
                transaction,
                actor,
                cancellationToken)
            .ConfigureAwait(false);
        DateTimeOffset authorizationNow = binding.AuthorizationNow;
        ValidateBrokerResultEnvelope(actor, brokerAccountId, request, authorizationNow, options);
        if (binding.AssignmentState is not ("reconciliation_only" or "active")
            || binding.AssignmentExpiresAt <= authorizationNow)
        {
            throw new ResourceConflictException(
                "WORKER_ASSIGNMENT_INACTIVE",
                "The current worker assignment cannot submit broker-operation evidence.");
        }

        BrokerOperationBinding operation = await LoadBrokerOperationBindingAsync(
                transaction,
                actor,
                brokerAccountId,
                request.OperationId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ResourceNotFoundException();
        ValidateBrokerOperationBinding(actor, request, binding, operation);

        AcceptedBrokerResult? acceptedOperationResult = await ReadAcceptedOperationResultAsync(
                transaction,
                actor.TenantId,
                request.OperationId,
                request.DispatchMessageId,
                cancellationToken)
            .ConfigureAwait(false);
        if (acceptedOperationResult is not null)
        {
            if (acceptedOperationResult.ResultId != request.ResultId
                || !FixedTimeEquals(acceptedOperationResult.RequestSha256, requestSha256))
            {
                throw new ResourceConflictException(
                    "BROKER_OPERATION_RESULT_ALREADY_RECORDED",
                    "A terminal result was already accepted for this dispatched operation.");
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new BrokerUserOperationResultAcceptance(request.ResultId, "duplicate");
        }

        AcceptedBrokerResult? replay = await ReadBrokerResultReplayAsync(
                transaction,
                actor.TenantId,
                request.ResultId,
                cancellationToken)
            .ConfigureAwait(false);
        if (replay is not null)
        {
            throw new ResourceConflictException(
                "BROKER_OPERATION_RESULT_ID_REUSED",
                "The result identifier was already used for a different dispatched operation.");
        }

        string proofKind = ExpectedProofKind(operation.OperationType);
        Guid resultRecordId = Guid.CreateVersion7();
        await using (NpgsqlCommand insert = transaction.CreateCommand(
            """
            insert into operations.user_operation_results
            (
                id, tenant_id, result_id, operation_id, dispatch_message_id,
                broker_account_id, route_deployment_id, generation,
                worker_assignment_id, worker_instance_id, operation_type,
                submitted_resource_version, requested_target_state,
                policy_snapshot_sha256, proof_kind, outcome, broker_confirmed,
                account_state, credential_state, evidence_sha256, error_code,
                request_sha256, observed_at, received_at
            )
            values
            (
                @id, @tenant_id, @result_id, @operation_id, @dispatch_message_id,
                @broker_account_id, @route_deployment_id, @generation,
                @worker_assignment_id, @worker_instance_id, @operation_type,
                @submitted_resource_version, @requested_target_state,
                @policy_snapshot_sha256, @proof_kind, @outcome, @broker_confirmed,
                @account_state, @credential_state, @evidence_sha256, @error_code,
                @request_sha256, @observed_at, @received_at
            )
            """))
        {
            AddUuid(insert, "id", resultRecordId);
            AddUuid(insert, "tenant_id", actor.TenantId);
            AddUuid(insert, "result_id", request.ResultId);
            AddUuid(insert, "operation_id", request.OperationId);
            AddUuid(insert, "dispatch_message_id", request.DispatchMessageId);
            AddUuid(insert, "broker_account_id", brokerAccountId);
            AddUuid(insert, "route_deployment_id", actor.DeploymentId);
            insert.Parameters.AddWithValue("generation", NpgsqlDbType.Bigint, actor.Generation);
            AddUuid(insert, "worker_assignment_id", binding.AssignmentId);
            AddUuid(insert, "worker_instance_id", actor.WorkerInstanceId);
            insert.Parameters.AddWithValue("operation_type", NpgsqlDbType.Text, operation.OperationType);
            insert.Parameters.AddWithValue(
                "submitted_resource_version",
                NpgsqlDbType.Bigint,
                request.SubmittedResourceVersion);
            insert.Parameters.AddWithValue(
                "requested_target_state",
                NpgsqlDbType.Text,
                request.RequestedTargetState);
            insert.Parameters.AddWithValue(
                "policy_snapshot_sha256",
                NpgsqlDbType.Text,
                request.PolicySnapshotSha256);
            insert.Parameters.AddWithValue("proof_kind", NpgsqlDbType.Text, proofKind);
            insert.Parameters.AddWithValue("outcome", NpgsqlDbType.Text, request.Outcome);
            insert.Parameters.AddWithValue("broker_confirmed", NpgsqlDbType.Boolean, request.BrokerConfirmed);
            insert.Parameters.AddWithValue("account_state", NpgsqlDbType.Text, request.AccountState);
            insert.Parameters.AddWithValue("credential_state", NpgsqlDbType.Text, request.CredentialState);
            insert.Parameters.AddWithValue("evidence_sha256", NpgsqlDbType.Text, request.EvidenceSha256);
            insert.Parameters.AddWithValue(
                "error_code",
                NpgsqlDbType.Text,
                request.ErrorCode is null ? DBNull.Value : request.ErrorCode);
            insert.Parameters.AddWithValue("request_sha256", NpgsqlDbType.Text, requestSha256);
            insert.Parameters.AddWithValue(
                "observed_at",
                NpgsqlDbType.TimestampTz,
                request.ObservedAt.ToUniversalTime());
            insert.Parameters.AddWithValue("received_at", NpgsqlDbType.TimestampTz, authorizationNow);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await AppendEvidenceAsync(
            transaction,
            "runtime.broker_user_operation_result_accepted",
            "user_operation",
            request.OperationId,
            metadata,
            request.ResultId,
            new
            {
                request.ResultId,
                request.OperationId,
                request.DispatchMessageId,
                BrokerAccountId = brokerAccountId,
                RouteDeploymentId = actor.DeploymentId,
                Generation = actor.Generation,
                WorkerAssignmentId = binding.AssignmentId,
                actor.WorkerInstanceId,
                operation.OperationType,
                request.SubmittedResourceVersion,
                request.RequestedTargetState,
                request.PolicySnapshotSha256,
                ProofKind = proofKind,
                request.Outcome,
                request.BrokerConfirmed,
                request.AccountState,
                request.CredentialState,
                request.EvidenceSha256,
                request.ErrorCode,
                requestSha256,
                request.ObservedAt
            },
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new BrokerUserOperationResultAcceptance(request.ResultId, "accepted");
    }

    internal static void ValidateBrokerResultEnvelope(
        WorkloadActor actor,
        Guid brokerAccountId,
        BrokerUserOperationResultInput request,
        DateTimeOffset now,
        RuntimeControlPostgresOptions options)
    {
        if (brokerAccountId == Guid.Empty
            || brokerAccountId != actor.BrokerAccountId
            || request.SchemaVersion != 1
            || request.ResultId == Guid.Empty
            || request.OperationId == Guid.Empty
            || request.DispatchMessageId == Guid.Empty
            || request.SubmittedResourceVersion < 0
            || !IsLowerSha256(request.PolicySnapshotSha256)
            || !IsLowerSha256(request.EvidenceSha256)
            || request.Outcome is not ("succeeded" or "failed")
            || request.AccountState is not ("active" or "disabled")
            || request.CredentialState is not ("absent" or "ready" or "disabled" or "rotation_pending" or "deletion_pending" or "deleted")
            || string.IsNullOrWhiteSpace(request.RequestedTargetState)
            || request.RequestedTargetState.Length > 200
            || request.ErrorCode?.Length > 200
            || request.Outcome == "failed" && string.IsNullOrWhiteSpace(request.ErrorCode)
            || request.Outcome == "succeeded" && !request.BrokerConfirmed
            || request.ObservedAt.ToUniversalTime() < now - options.MaximumEvidenceAge
            || request.ObservedAt.ToUniversalTime() > now + options.MaximumFutureClockSkew)
        {
            throw new DomainException(
                "BROKER_OPERATION_RESULT_INVALID",
                "The broker-operation result envelope is invalid.");
        }

        string observedTargetState = $"{request.AccountState}:{request.CredentialState}";
        if (request.Outcome == "succeeded"
            && !string.Equals(observedTargetState, request.RequestedTargetState, StringComparison.Ordinal))
        {
            throw new DomainException(
                "BROKER_OPERATION_RESULT_NOT_FINAL",
                "Successful evidence must prove the exact requested broker-account state.");
        }
    }

    private async ValueTask<TenantPostgresTransaction> BeginBrokerEvidenceAsync(
        WorkloadActor actor,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        ValidateActor(actor);
        ValidateMetadata(metadata);
        if (evidenceDatabase is null)
        {
            throw new BackendCapabilityUnavailableException("runtime_broker_evidence_postgres");
        }

        TenantPostgresTransaction transaction = await evidenceDatabase
            .BeginTenantTransactionAsync(
                new TenantExecutionContext(actor.TenantId, actor.WorkloadId, metadata.CorrelationId, null),
                cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await using NpgsqlCommand command = transaction.CreateCommand(
                "select control.acquire_u0_authority_lock()");
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<BrokerEvidenceBinding> LoadBrokerEvidenceBindingAsync(
        TenantPostgresTransaction transaction,
        WorkloadActor actor,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select
                assignment.id, assignment.supervisor_identity,
                assignment.state, assignment.lease_expires_at,
                clock_timestamp() as authorization_now
            from operations.worker_assignments as assignment
            join operations.deployments as deployment
              on deployment.tenant_id = assignment.tenant_id
             and deployment.id = assignment.deployment_id
            join operations.broker_accounts as account
              on account.tenant_id = deployment.tenant_id
             and account.id = deployment.broker_account_id
            where assignment.tenant_id = @tenant_id
              and assignment.deployment_id = @deployment_id
              and assignment.worker_node_id = @worker_instance_id
              and assignment.fence_generation = @generation
              and deployment.fence_generation = @generation
              and deployment.broker_account_id = @broker_account_id
              and deployment.region = @region
            for update of assignment, deployment
            """);
        AddUuid(command, "tenant_id", actor.TenantId);
        AddUuid(command, "deployment_id", actor.DeploymentId);
        AddUuid(command, "worker_instance_id", actor.WorkerInstanceId);
        AddUuid(command, "broker_account_id", actor.BrokerAccountId);
        command.Parameters.AddWithValue("generation", NpgsqlDbType.Bigint, actor.Generation);
        command.Parameters.AddWithValue("region", NpgsqlDbType.Text, actor.Region);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            || ParseStoredWorkloadId(reader.GetString(1)) != actor.WorkloadId)
        {
            throw WrongRuntimeBinding();
        }

        return new BrokerEvidenceBinding(
            reader.GetGuid(0),
            reader.GetString(2),
            reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetFieldValue<DateTimeOffset>(4));
    }

    private static void ValidateBrokerOperationBinding(
        WorkloadActor actor,
        BrokerUserOperationResultInput request,
        BrokerEvidenceBinding binding,
        BrokerOperationBinding operation)
    {
        if (operation.State is not ("propagating" or "reconciling" or "unknown")
            || operation.DispatchState != "published"
            || operation.DispatchMessageId != request.DispatchMessageId
            || operation.SubmittedResourceVersion != request.SubmittedResourceVersion
            || !string.Equals(operation.RequestedTargetState, request.RequestedTargetState, StringComparison.Ordinal)
            || !FixedTimeEquals(operation.PolicySnapshotSha256, request.PolicySnapshotSha256)
            || operation.RouteDeploymentId != actor.DeploymentId
            || operation.Generation != actor.Generation
            || operation.WorkerAssignmentId != binding.AssignmentId
            || operation.WorkerInstanceId != actor.WorkerInstanceId)
        {
            throw new ResourceConflictException(
                "BROKER_OPERATION_BINDING_MISMATCH",
                "The result does not match the current persisted dispatch binding.");
        }
    }

    private static async Task<BrokerOperationBinding?> LoadBrokerOperationBindingAsync(
        TenantPostgresTransaction transaction,
        WorkloadActor actor,
        Guid brokerAccountId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select
                operation.operation_type, operation.state,
                operation.dispatch_message_id, operation.submitted_resource_version,
                operation.requested_target_state,
                operation.dispatch_policy_snapshot_sha256,
                operation.dispatch_route_deployment_id,
                operation.dispatch_fence_generation,
                operation.dispatch_worker_assignment_id,
                operation.dispatch_worker_instance_id,
                outbox.state
            from control.user_operations as operation
            join messaging.outbox_messages as outbox
              on outbox.tenant_id = operation.tenant_id
             and outbox.id = operation.dispatch_message_id
             and outbox.aggregate_type = 'user_operation'
             and outbox.aggregate_id = operation.id::text
             and outbox.causation_id = operation.id
             and outbox.correlation_id = operation.correlation_id
             and outbox.message_type = 'yo4x.' || replace(operation.operation_type, '_', '-') || '.requested.v1'
            where operation.tenant_id = @tenant_id
              and operation.id = @operation_id
              and operation.target_type = 'broker_account'
              and operation.target_id = @broker_account_id
            for share of operation
            """);
        AddUuid(command, "tenant_id", actor.TenantId);
        AddUuid(command, "operation_id", operationId);
        AddUuid(command, "broker_account_id", brokerAccountId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new BrokerOperationBinding(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetGuid(2),
            reader.GetInt64(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetGuid(6),
            reader.GetInt64(7),
            reader.GetGuid(8),
            reader.GetGuid(9),
            reader.GetString(10));
    }

    private static async Task<AcceptedBrokerResult?> ReadAcceptedOperationResultAsync(
        TenantPostgresTransaction transaction,
        Guid tenantId,
        Guid operationId,
        Guid dispatchMessageId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select result_id, request_sha256
            from operations.user_operation_results
            where tenant_id = @tenant_id
              and operation_id = @operation_id
              and dispatch_message_id = @dispatch_message_id
            """);
        AddUuid(command, "tenant_id", tenantId);
        AddUuid(command, "operation_id", operationId);
        AddUuid(command, "dispatch_message_id", dispatchMessageId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new AcceptedBrokerResult(reader.GetGuid(0), reader.GetString(1))
            : null;
    }

    private static async Task<AcceptedBrokerResult?> ReadBrokerResultReplayAsync(
        TenantPostgresTransaction transaction,
        Guid tenantId,
        Guid resultId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select result_id, request_sha256
            from operations.user_operation_results
            where tenant_id = @tenant_id and result_id = @result_id
            """);
        AddUuid(command, "tenant_id", tenantId);
        AddUuid(command, "result_id", resultId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new AcceptedBrokerResult(reader.GetGuid(0), reader.GetString(1))
            : null;
    }

    private static string ExpectedProofKind(string operationType) => operationType switch
    {
        "broker_account.connection_test" => "connection_verified",
        "broker_account.credential_rotation" => "credential_rotated",
        "broker_account.disable" => "account_disabled",
        "broker_account.delete" => "credential_deleted",
        _ => throw new InvalidOperationException("The persisted broker operation type is invalid.")
    };

    private static bool IsLowerSha256(string value) => value.Length == 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private sealed record BrokerOperationBinding(
        string OperationType,
        string State,
        Guid DispatchMessageId,
        long SubmittedResourceVersion,
        string RequestedTargetState,
        string PolicySnapshotSha256,
        Guid RouteDeploymentId,
        long Generation,
        Guid WorkerAssignmentId,
        Guid WorkerInstanceId,
        string DispatchState);

    private sealed record BrokerEvidenceBinding(
        Guid AssignmentId,
        string AssignmentState,
        DateTimeOffset AssignmentExpiresAt,
        DateTimeOffset AuthorizationNow);

    private sealed record AcceptedBrokerResult(Guid ResultId, string RequestSha256);
}
