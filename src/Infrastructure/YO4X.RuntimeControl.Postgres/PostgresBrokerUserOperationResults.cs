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
        DateTimeOffset authorizationNow = await ReadDatabaseClockAsync(
                transaction,
                cancellationToken)
            .ConfigureAwait(false);
        ValidateBrokerResultEnvelope(actor, brokerAccountId, request, authorizationNow, options);

        Guid proposedRecordId = Guid.CreateVersion7();
        BrokerResultWrite write;
        try
        {
            write = await RecordBrokerResultAsync(
                    transaction,
                    proposedRecordId,
                    brokerAccountId,
                    request,
                    requestSha256,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (PostgresException exception) when (
            UserOperationResultPostgresErrors.IsExpectedRecorderRejection(exception))
        {
            throw UserOperationResultPostgresErrors.Broker(exception);
        }

        if (string.Equals(write.AcceptanceStatus, "duplicate", StringComparison.Ordinal))
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new BrokerUserOperationResultAcceptance(request.ResultId, "duplicate");
        }

        if (!string.Equals(write.AcceptanceStatus, "accepted", StringComparison.Ordinal)
            || write.ResultRecordId != proposedRecordId)
        {
            throw new InvalidOperationException(
                "The broker-result capability returned an invalid acceptance record.");
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
                ResultRecordId = write.ResultRecordId,
                request.ResultId,
                request.OperationId,
                request.DispatchMessageId,
                BrokerAccountId = brokerAccountId,
                RouteDeploymentId = actor.DeploymentId,
                Generation = actor.Generation,
                actor.WorkerInstanceId,
                request.SubmittedResourceVersion,
                request.RequestedTargetState,
                request.PolicySnapshotSha256,
                request.DispatchTargetBindingSha256,
                request.Outcome,
                request.PreInvocationNotSentProven,
                request.GatewayInvoked,
                request.BrokerConfirmed,
                request.AccountState,
                request.CredentialState,
                request.EvidenceSha256,
                request.ErrorCode,
                requestSha256,
                request.ObservedAt,
                ReceivedAt = write.ReceivedAt
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
            || request.SchemaVersion != 4
            || request.ResultId == Guid.Empty
            || request.OperationId == Guid.Empty
            || request.DispatchMessageId == Guid.Empty
            || request.SubmittedResourceVersion < 0
            || !IsBoundedTrimmed(request.RequestedTargetState, 200)
            || !IsLowerSha256(request.PolicySnapshotSha256)
            || !IsLowerSha256(request.DispatchTargetBindingSha256)
            || !IsResultCapability(request.ResultCapability)
            || !IsLowerSha256(request.EvidenceSha256)
            || request.Outcome is not ("succeeded" or "diverged")
            || request.PreInvocationNotSentProven
            || !request.GatewayInvoked
            || request.AccountState is not null
                && request.AccountState is not ("active" or "disabled")
            || request.CredentialState is not null
                && request.CredentialState is not ("absent" or "ready" or "disabled" or "rotation_pending" or "deletion_pending" or "deleted")
            || request.ErrorCode is not null && !IsBoundedTrimmed(request.ErrorCode, 200)
            || request.Outcome == "succeeded"
                && (!request.BrokerConfirmed
                    || request.AccountState is null
                    || request.CredentialState is null
                    || request.ErrorCode is not null)
            || request.Outcome == "diverged"
                && (!request.BrokerConfirmed
                    || request.AccountState is null
                    || request.CredentialState is null
                    || request.ErrorCode is null)
            || request.ObservedAt == default
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

        if (request.Outcome == "diverged"
            && string.Equals(observedTargetState, request.RequestedTargetState, StringComparison.Ordinal))
        {
            throw new DomainException(
                "BROKER_OPERATION_RESULT_NOT_DIVERGED",
                "Diverged evidence must prove a broker-account state different from the requested state.");
        }
    }

    private async ValueTask<TenantPostgresTransaction> BeginBrokerEvidenceAsync(
        WorkloadActor actor,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        UserOperationProtocolAdapterValidation.ValidateActor(actor);
        UserOperationProtocolAdapterValidation.ValidateMetadata(metadata);
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

    private static async Task<DateTimeOffset> ReadDatabaseClockAsync(
        TenantPostgresTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand("select clock_timestamp()");
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is DateTimeOffset databaseNow
            ? databaseNow.ToUniversalTime()
            : throw new InvalidOperationException("PostgreSQL returned an invalid authority clock.");
    }

    private static async Task<BrokerResultWrite> RecordBrokerResultAsync(
        TenantPostgresTransaction transaction,
        Guid proposedRecordId,
        Guid brokerAccountId,
        BrokerUserOperationResultInput request,
        string requestSha256,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select acceptance_status, result_record_id, received_at
            from control.record_broker_user_operation_result(
                @result_record_id, @result_id, @operation_id, @dispatch_message_id,
                @raw_result_capability, @broker_account_id,
                @submitted_resource_version, @requested_target_state,
                @policy_snapshot_sha256, @dispatch_target_binding_sha256,
                @outcome, @pre_invocation_not_sent_proven, @gateway_invoked,
                @broker_confirmed, @account_state, @credential_state,
                @evidence_sha256, @error_code, @request_sha256, @observed_at)
            """);
        AddUuid(command, "result_record_id", proposedRecordId);
        AddUuid(command, "result_id", request.ResultId);
        AddUuid(command, "operation_id", request.OperationId);
        AddUuid(command, "dispatch_message_id", request.DispatchMessageId);
        command.Parameters.AddWithValue(
            "raw_result_capability",
            NpgsqlDbType.Text,
            request.ResultCapability);
        AddUuid(command, "broker_account_id", brokerAccountId);
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
            "broker_confirmed",
            NpgsqlDbType.Boolean,
            request.BrokerConfirmed);
        command.Parameters.AddWithValue(
            "account_state",
            NpgsqlDbType.Text,
            request.AccountState is null ? DBNull.Value : request.AccountState);
        command.Parameters.AddWithValue(
            "credential_state",
            NpgsqlDbType.Text,
            request.CredentialState is null ? DBNull.Value : request.CredentialState);
        command.Parameters.AddWithValue("evidence_sha256", NpgsqlDbType.Text, request.EvidenceSha256);
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
                "The broker-result capability did not return one complete acceptance record.");
        }

        var result = new BrokerResultWrite(
            reader.GetString(0),
            reader.GetGuid(1),
            reader.GetFieldValue<DateTimeOffset>(2).ToUniversalTime());
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "The broker-result capability returned more than one acceptance record.");
        }

        if (result.ResultRecordId == Guid.Empty
            || result.ReceivedAt == default
            || result.AcceptanceStatus is not ("accepted" or "duplicate"))
        {
            throw new InvalidOperationException(
                "The broker-result capability returned an invalid acceptance record.");
        }

        return result;
    }

    private static bool IsLowerSha256(string? value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsResultCapability(string? value) =>
        CanonicalBase64Url.IsEncodedByteCount(value, 32);

    private static bool IsBoundedTrimmed(string? value, int maximumLength) =>
        value is not null
        && value.Length is > 0
        && value.Length <= maximumLength
        && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private sealed record BrokerResultWrite(
        string AcceptanceStatus,
        Guid ResultRecordId,
        DateTimeOffset ReceivedAt);
}
