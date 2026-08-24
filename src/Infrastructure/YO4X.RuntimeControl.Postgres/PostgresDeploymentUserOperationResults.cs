using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.Persistence.Postgres;
using YO4X.Tenancy;

namespace YO4X.RuntimeControl.Postgres;

public sealed partial class PostgresRuntimeControlPlaneApplication
{
    public async Task<DeploymentUserOperationResultAcceptance> RecordDeploymentUserOperationResultAsync(
        WorkloadActor actor,
        Guid deploymentId,
        DeploymentUserOperationResultInput request,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireSupervisor(actor);

        string requestSha256 = CanonicalJson.Sha256(request);
        await using TenantPostgresTransaction transaction = await BeginDeploymentEvidenceAsync(
                actor,
                metadata,
                cancellationToken)
            .ConfigureAwait(false);
        DateTimeOffset authorizationNow = await ReadDatabaseClockAsync(
                transaction,
                cancellationToken)
            .ConfigureAwait(false);
        ValidateDeploymentResultEnvelope(actor, deploymentId, request, authorizationNow, options);

        Guid proposedReconciliationId = Guid.CreateVersion7();
        DeploymentResultWrite write;
        try
        {
            write = await RecordDeploymentResultAsync(
                    transaction,
                    proposedReconciliationId,
                    deploymentId,
                    request,
                    requestSha256,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (PostgresException exception) when (
            UserOperationResultPostgresErrors.IsExpectedRecorderRejection(exception))
        {
            throw UserOperationResultPostgresErrors.Deployment(exception);
        }

        if (string.Equals(write.AcceptanceStatus, "duplicate", StringComparison.Ordinal))
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new DeploymentUserOperationResultAcceptance(request.ResultId, "duplicate");
        }

        if (!string.Equals(write.AcceptanceStatus, "accepted", StringComparison.Ordinal)
            || write.ReconciliationId != proposedReconciliationId)
        {
            throw new InvalidOperationException(
                "The deployment-result capability returned an invalid acceptance record.");
        }

        await AppendEvidenceAsync(
            transaction,
            "runtime.deployment_user_operation_result_accepted",
            "user_operation",
            request.OperationId,
            metadata,
            request.ResultId,
            new
            {
                ReconciliationId = write.ReconciliationId,
                request.ResultId,
                request.OperationId,
                request.DispatchMessageId,
                DeploymentId = deploymentId,
                actor.Generation,
                actor.WorkerInstanceId,
                request.SubmittedResourceVersion,
                request.RequestedTargetState,
                request.PolicySnapshotSha256,
                request.DispatchTargetBindingSha256,
                request.Outcome,
                request.PreInvocationNotSentProven,
                request.GatewayInvoked,
                request.ObservedState,
                request.ObservedDigest,
                request.RuntimeEvidenceSha256,
                request.BrokerConfirmed,
                request.BrokerDigest,
                request.BrokerExecutionState,
                request.BrokerPositionState,
                request.ErrorCode,
                requestSha256,
                request.ObservedAt,
                ReceivedAt = write.ReceivedAt
            },
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new DeploymentUserOperationResultAcceptance(request.ResultId, "accepted");
    }

    internal static void ValidateDeploymentResultEnvelope(
        WorkloadActor actor,
        Guid deploymentId,
        DeploymentUserOperationResultInput request,
        DateTimeOffset now,
        RuntimeControlPostgresOptions options)
    {
        bool brokerShapeValid = request.BrokerConfirmed
            ? IsLowerSha256(request.BrokerDigest)
                && request.BrokerExecutionState is "running" or "close_only" or "stopped" or "unknown"
                && request.BrokerPositionState is "open" or "flat" or "unknown"
            : request.BrokerDigest is null
                && request.BrokerExecutionState is null
                && request.BrokerPositionState is null;
        if (deploymentId == Guid.Empty
            || deploymentId != actor.DeploymentId
            || request.SchemaVersion != 4
            || request.ResultId == Guid.Empty
            || request.OperationId == Guid.Empty
            || request.DispatchMessageId == Guid.Empty
            || request.SubmittedResourceVersion < 0
            || request.RequestedTargetState is not ("running" or "close_only" or "stopped")
            || !IsLowerSha256(request.PolicySnapshotSha256)
            || !IsLowerSha256(request.DispatchTargetBindingSha256)
            || !IsResultCapability(request.ResultCapability)
            || request.Outcome is not ("succeeded" or "diverged")
            || request.PreInvocationNotSentProven
            || !request.GatewayInvoked
            || request.ObservedState is not null
                && request.ObservedState is not ("running" or "close_only" or "stopped" or "faulted" or "unknown")
            || request.ObservedDigest is not null && !IsLowerSha256(request.ObservedDigest)
            || !IsLowerSha256(request.RuntimeEvidenceSha256)
            || !brokerShapeValid
            || request.ErrorCode is not null && !IsBoundedTrimmed(request.ErrorCode, 200)
            || request.ObservedAt == default
            || request.ObservedAt.ToUniversalTime() > now + options.MaximumFutureClockSkew)
        {
            throw InvalidDeploymentResult();
        }

        bool exactRuntimeState = string.Equals(
            request.ObservedState,
            request.RequestedTargetState,
            StringComparison.Ordinal);
        bool exactRuntimeDigest = request.ObservedDigest is not null
            && FixedTimeEquals(request.ObservedDigest, request.DispatchTargetBindingSha256);
        bool exactBrokerState = string.Equals(
            request.BrokerExecutionState,
            request.RequestedTargetState,
            StringComparison.Ordinal);
        bool exactStoppedPosition = request.RequestedTargetState != "stopped"
            || string.Equals(request.BrokerPositionState, "flat", StringComparison.Ordinal);

        switch (request.Outcome)
        {
            case "succeeded" when request.ErrorCode is null
                && !request.PreInvocationNotSentProven
                && request.GatewayInvoked
                && request.BrokerConfirmed
                && exactRuntimeState
                && exactRuntimeDigest
                && exactBrokerState
                && exactStoppedPosition:
                return;
            case "diverged" when request.ErrorCode is not null
                && !request.PreInvocationNotSentProven
                && request.GatewayInvoked
                && request.BrokerConfirmed
                && request.ObservedState is not null
                && request.ObservedDigest is not null
                && !(exactRuntimeState && exactRuntimeDigest && exactBrokerState && exactStoppedPosition):
                return;
            default:
                throw new DomainException(
                    "DEPLOYMENT_OPERATION_RESULT_NOT_FINAL",
                    "The deployment-operation result does not contain conclusive terminal evidence.");
        }
    }

    private async ValueTask<TenantPostgresTransaction> BeginDeploymentEvidenceAsync(
        WorkloadActor actor,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        UserOperationProtocolAdapterValidation.ValidateActor(actor);
        UserOperationProtocolAdapterValidation.ValidateMetadata(metadata);
        if (evidenceDatabase is null)
        {
            throw new BackendCapabilityUnavailableException("runtime_deployment_evidence_postgres");
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

    private static async Task<DeploymentResultWrite> RecordDeploymentResultAsync(
        TenantPostgresTransaction transaction,
        Guid proposedReconciliationId,
        Guid deploymentId,
        DeploymentUserOperationResultInput request,
        string requestSha256,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select acceptance_status, reconciliation_id, received_at
            from control.record_deployment_user_operation_result(
                @reconciliation_id, @result_id, @operation_id, @dispatch_message_id,
                @raw_result_capability, @deployment_id,
                @submitted_resource_version, @requested_target_state,
                @policy_snapshot_sha256, @dispatch_target_binding_sha256,
                @outcome, @pre_invocation_not_sent_proven, @gateway_invoked,
                @observed_state, @observed_digest,
                @runtime_evidence_sha256, @broker_confirmed, @broker_digest,
                @broker_execution_state, @broker_position_state, @error_code,
                @request_sha256, @observed_at)
            """);
        AddUuid(command, "reconciliation_id", proposedReconciliationId);
        AddUuid(command, "result_id", request.ResultId);
        AddUuid(command, "operation_id", request.OperationId);
        AddUuid(command, "dispatch_message_id", request.DispatchMessageId);
        command.Parameters.AddWithValue(
            "raw_result_capability",
            NpgsqlDbType.Text,
            request.ResultCapability);
        AddUuid(command, "deployment_id", deploymentId);
        command.Parameters.AddWithValue(
            "submitted_resource_version",
            NpgsqlDbType.Bigint,
            request.SubmittedResourceVersion);
        command.Parameters.AddWithValue(
            "requested_target_state",
            NpgsqlDbType.Text,
            request.RequestedTargetState);
        command.Parameters.AddWithValue(
            "policy_snapshot_sha256",
            NpgsqlDbType.Text,
            request.PolicySnapshotSha256);
        command.Parameters.AddWithValue(
            "dispatch_target_binding_sha256",
            NpgsqlDbType.Text,
            request.DispatchTargetBindingSha256);
        command.Parameters.AddWithValue("outcome", NpgsqlDbType.Text, request.Outcome);
        command.Parameters.AddWithValue(
            "pre_invocation_not_sent_proven",
            NpgsqlDbType.Boolean,
            request.PreInvocationNotSentProven);
        command.Parameters.AddWithValue(
            "gateway_invoked",
            NpgsqlDbType.Boolean,
            request.GatewayInvoked);
        command.Parameters.AddWithValue(
            "observed_state",
            NpgsqlDbType.Text,
            request.ObservedState is null ? DBNull.Value : request.ObservedState);
        command.Parameters.AddWithValue(
            "observed_digest",
            NpgsqlDbType.Text,
            request.ObservedDigest is null ? DBNull.Value : request.ObservedDigest);
        command.Parameters.AddWithValue(
            "runtime_evidence_sha256",
            NpgsqlDbType.Text,
            request.RuntimeEvidenceSha256);
        command.Parameters.AddWithValue(
            "broker_confirmed",
            NpgsqlDbType.Boolean,
            request.BrokerConfirmed);
        command.Parameters.AddWithValue(
            "broker_digest",
            NpgsqlDbType.Text,
            request.BrokerDigest is null ? DBNull.Value : request.BrokerDigest);
        command.Parameters.AddWithValue(
            "broker_execution_state",
            NpgsqlDbType.Text,
            request.BrokerExecutionState is null ? DBNull.Value : request.BrokerExecutionState);
        command.Parameters.AddWithValue(
            "broker_position_state",
            NpgsqlDbType.Text,
            request.BrokerPositionState is null ? DBNull.Value : request.BrokerPositionState);
        command.Parameters.AddWithValue(
            "error_code",
            NpgsqlDbType.Text,
            request.ErrorCode is null ? DBNull.Value : request.ErrorCode);
        command.Parameters.AddWithValue("request_sha256", NpgsqlDbType.Text, requestSha256);
        command.Parameters.AddWithValue(
            "observed_at",
            NpgsqlDbType.TimestampTz,
            request.ObservedAt.ToUniversalTime());

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            || reader.IsDBNull(0)
            || reader.IsDBNull(1)
            || reader.IsDBNull(2))
        {
            throw new InvalidOperationException(
                "The deployment-result capability did not return one complete acceptance record.");
        }

        var result = new DeploymentResultWrite(
            reader.GetString(0),
            reader.GetGuid(1),
            reader.GetFieldValue<DateTimeOffset>(2).ToUniversalTime());
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "The deployment-result capability returned more than one acceptance record.");
        }

        if (result.ReconciliationId == Guid.Empty
            || result.ReceivedAt == default
            || result.AcceptanceStatus is not ("accepted" or "duplicate"))
        {
            throw new InvalidOperationException(
                "The deployment-result capability returned an invalid acceptance record.");
        }

        return result;
    }

    private static DomainException InvalidDeploymentResult() => new(
        "DEPLOYMENT_OPERATION_RESULT_INVALID",
        "The deployment-operation result envelope is invalid.");

    private sealed record DeploymentResultWrite(
        string AcceptanceStatus,
        Guid ReconciliationId,
        DateTimeOffset ReceivedAt);
}
