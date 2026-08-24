using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using YO4X.ControlPlane.Application;
using YO4X.Persistence.Postgres;
using YO4X.Runtime.Contracts;
using YO4X.Tenancy;

namespace YO4X.RuntimeControl.Postgres;

/// <summary>
/// Persists only canonical result.v5 evidence through the dedicated
/// runtime-evidence role. Expiry and projection decisions remain DB-owned.
/// </summary>
public sealed class PostgresUserOperationResultV5Application
    : IUserOperationResultV5Application
{
    private readonly RuntimeEvidencePostgresDatabase database;

    public PostgresUserOperationResultV5Application(
        RuntimeEvidencePostgresDatabase database)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public Task<UserOperationResultV5Acceptance> RecordGatewayResultAsync(
        WorkloadActor actor,
        UserOperationGatewayResultV5 request,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        UserOperationProtocolAdapterValidation.ValidateActor(actor, "supervisor");
        UserOperationProtocolAdapterValidation.ValidateMetadata(metadata);
        ArgumentNullException.ThrowIfNull(request);
        return RecordAsync(
            actor,
            metadata,
            ResultSubmission.FromGateway(request),
            cancellationToken);
    }

    public Task<UserOperationResultV5Acceptance> RecordReconciliationResultAsync(
        WorkloadActor actor,
        UserOperationReconciliationResultV5 request,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        UserOperationProtocolAdapterValidation.ValidateActor(actor, "supervisor");
        UserOperationProtocolAdapterValidation.ValidateMetadata(metadata);
        ArgumentNullException.ThrowIfNull(request);
        return RecordAsync(
            actor,
            metadata,
            ResultSubmission.FromReconciliation(request),
            cancellationToken);
    }

    private async Task<UserOperationResultV5Acceptance> RecordAsync(
        WorkloadActor actor,
        RequestMetadata metadata,
        ResultSubmission submission,
        CancellationToken cancellationToken)
    {
        try
        {
            await using TenantPostgresTransaction transaction = await database
                .BeginTenantTransactionAsync(
                    new TenantExecutionContext(
                        actor.TenantId,
                        actor.WorkloadId,
                        metadata.CorrelationId,
                        null),
                    cancellationToken)
                .ConfigureAwait(false);
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                select
                    acceptance_status,
                    result_id,
                    result_record_id,
                    attempt_id,
                    operation_id,
                    outcome,
                    received_at
                from control.record_user_operation_result_v5(
                    p_result_id => @result_id,
                    p_attempt_id => @attempt_id,
                    p_invocation_id => @invocation_id,
                    p_operation_id => @operation_id,
                    p_dispatch_message_id => @dispatch_message_id,
                    p_start_receipt_id => @start_receipt_id,
                    p_authorization_id => @authorization_id,
                    p_gateway_observation_receipt_id => @gateway_observation_receipt_id,
                    p_gateway_receipt_sha256 => @gateway_receipt_sha256,
                    p_challenge_consumption_id => @challenge_consumption_id,
                    p_challenge_id => @challenge_id,
                    p_challenge_message_id => @challenge_message_id,
                    p_raw_result_capability => @raw_result_capability,
                    p_target_type => @target_type,
                    p_target_id => @target_id,
                    p_target_observation => @target_observation,
                    p_submitted_resource_version => @submitted_resource_version,
                    p_requested_target_state => @requested_target_state,
                    p_dispatch_target_binding_sha256 => @dispatch_target_binding_sha256,
                    p_dispatch_policy_snapshot_sha256 => @dispatch_policy_snapshot_sha256,
                    p_outcome => @outcome,
                    p_observation_sha256 => @observation_sha256,
                    p_observed_at => @observed_at,
                    p_request_sha256 => @request_sha256,
                    p_expected_worker_instance_id => @expected_worker_instance_id,
                    p_expected_deployment_id => @expected_deployment_id,
                    p_expected_broker_account_id => @expected_broker_account_id,
                    p_expected_fence_generation => @expected_fence_generation,
                    p_expected_region => @expected_region)
                """);
            AddParameters(command, submission, actor);

            UserOperationResultV5Acceptance? acceptance = null;
            await using (NpgsqlDataReader reader = await command
                .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    string status = reader.GetString(0);
                    Guid resultId = reader.GetGuid(1);
                    Guid resultRecordId = reader.GetGuid(2);
                    Guid attemptId = reader.GetGuid(3);
                    Guid operationId = reader.GetGuid(4);
                    UserOperationObservationOutcome outcome =
                        UserOperationProtocolAdapterValidation.Outcome(reader.GetString(5));
                    DateTimeOffset receivedAt =
                        UserOperationProtocolPostgresCommand.Utc(reader, 6);
                    if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                        || status is not ("accepted" or "duplicate")
                        || resultId != submission.ResultId
                        || resultRecordId == Guid.Empty
                        || attemptId != submission.AttemptId
                        || operationId != submission.OperationId
                        || outcome != submission.Outcome
                        || receivedAt == default)
                    {
                        throw new InvalidOperationException(
                            "PostgreSQL returned invalid result-v5 acceptance evidence.");
                    }

                    acceptance = new UserOperationResultV5Acceptance(resultId, status);
                }
            }

            if (acceptance is null)
            {
                throw UserOperationInvocationPostgresErrors.Rejected();
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return acceptance;
        }
        catch (PostgresException exception)
            when (UserOperationInvocationPostgresErrors.IsExpected(exception))
        {
            throw UserOperationInvocationPostgresErrors.Map(exception, "result-v5 recording");
        }
    }

    private static void AddParameters(
        NpgsqlCommand command,
        ResultSubmission submission,
        WorkloadActor actor)
    {
        command.Parameters.AddWithValue("result_id", NpgsqlDbType.Uuid, submission.ResultId);
        command.Parameters.AddWithValue("attempt_id", NpgsqlDbType.Uuid, submission.AttemptId);
        command.Parameters.Add(
            new NpgsqlParameter("invocation_id", NpgsqlDbType.Uuid)
            {
                Value = submission.InvocationId is null
                    ? DBNull.Value
                    : submission.InvocationId.Value
            });
        command.Parameters.AddWithValue("operation_id", NpgsqlDbType.Uuid, submission.OperationId);
        command.Parameters.AddWithValue(
            "dispatch_message_id",
            NpgsqlDbType.Uuid,
            submission.DispatchMessageId);
        command.Parameters.AddWithValue(
            "start_receipt_id",
            NpgsqlDbType.Uuid,
            submission.GatewayStartReceiptId);
        command.Parameters.AddWithValue(
            "authorization_id",
            NpgsqlDbType.Uuid,
            submission.ProviderCallAuthorizationReceiptId);
        command.Parameters.Add(NullableUuid(
            "gateway_observation_receipt_id",
            submission.GatewayObservationReceiptId));
        command.Parameters.Add(NullableText(
            "gateway_receipt_sha256",
            submission.GatewayReceiptSha256));
        command.Parameters.Add(NullableUuid(
            "challenge_consumption_id",
            submission.ChallengeConsumptionId));
        command.Parameters.Add(NullableUuid("challenge_id", submission.ChallengeId));
        command.Parameters.Add(NullableUuid(
            "challenge_message_id",
            submission.ChallengeMessageId));
        command.Parameters.AddWithValue(
            "raw_result_capability",
            NpgsqlDbType.Text,
            submission.ResultCapability.DangerousGetValue());
        command.Parameters.AddWithValue("target_type", NpgsqlDbType.Text, submission.TargetType);
        command.Parameters.AddWithValue("target_id", NpgsqlDbType.Uuid, submission.TargetId);
        command.Parameters.AddWithValue(
            "target_observation",
            NpgsqlDbType.Jsonb,
            submission.TargetObservation.ToCanonicalJson());
        command.Parameters.AddWithValue(
            "submitted_resource_version",
            NpgsqlDbType.Bigint,
            submission.SubmittedResourceVersion);
        command.Parameters.AddWithValue(
            "requested_target_state",
            NpgsqlDbType.Text,
            submission.RequestedTargetState);
        command.Parameters.AddWithValue(
            "dispatch_target_binding_sha256",
            NpgsqlDbType.Text,
            submission.DispatchTargetBindingSha256);
        command.Parameters.AddWithValue(
            "dispatch_policy_snapshot_sha256",
            NpgsqlDbType.Text,
            submission.DispatchPolicySnapshotSha256);
        command.Parameters.AddWithValue(
            "outcome",
            NpgsqlDbType.Text,
            UserOperationProtocolAdapterValidation.Outcome(submission.Outcome));
        command.Parameters.AddWithValue(
            "observation_sha256",
            NpgsqlDbType.Text,
            submission.ObservationSha256);
        command.Parameters.AddWithValue(
            "observed_at",
            NpgsqlDbType.TimestampTz,
            submission.ObservedAtUtc);
        command.Parameters.AddWithValue(
            "request_sha256",
            NpgsqlDbType.Text,
            submission.RequestSha256);
        UserOperationProtocolPostgresCommand.AddActorBinding(command, actor);
    }

    private static NpgsqlParameter NullableUuid(string name, Guid? value) =>
        new(name, NpgsqlDbType.Uuid)
        {
            Value = value is null ? DBNull.Value : value.Value
        };

    private static NpgsqlParameter NullableText(string name, string? value) =>
        new(name, NpgsqlDbType.Text)
        {
            Value = value is null ? DBNull.Value : value
        };

    private sealed record ResultSubmission(
        Guid ResultId,
        Guid AttemptId,
        Guid? InvocationId,
        Guid OperationId,
        Guid DispatchMessageId,
        Guid GatewayStartReceiptId,
        Guid ProviderCallAuthorizationReceiptId,
        Guid? GatewayObservationReceiptId,
        string? GatewayReceiptSha256,
        Guid? ChallengeConsumptionId,
        Guid? ChallengeId,
        Guid? ChallengeMessageId,
        UserOperationBearer ResultCapability,
        string TargetType,
        Guid TargetId,
        UserOperationTargetObservation TargetObservation,
        long SubmittedResourceVersion,
        string RequestedTargetState,
        string DispatchTargetBindingSha256,
        string DispatchPolicySnapshotSha256,
        UserOperationObservationOutcome Outcome,
        string ObservationSha256,
        DateTimeOffset ObservedAtUtc,
        string RequestSha256)
    {
        public static ResultSubmission FromGateway(UserOperationGatewayResultV5 request) => new(
            request.ResultId,
            request.AttemptId,
            request.InvocationId,
            request.OperationId,
            request.DispatchMessageId,
            request.GatewayStartReceiptId,
            request.ProviderCallAuthorizationReceiptId,
            request.GatewayObservationReceiptId,
            request.GatewayReceiptSha256,
            null,
            null,
            null,
            request.ResultCapability,
            request.TargetType,
            request.TargetId,
            request.TargetObservation,
            request.SubmittedResourceVersion,
            request.RequestedTargetState,
            request.DispatchTargetBindingSha256,
            request.DispatchPolicySnapshotSha256,
            request.Outcome,
            request.ObservationSha256,
            request.ObservedAtUtc,
            Sha256Utf8(request.ToCanonicalJson()));

        public static ResultSubmission FromReconciliation(
            UserOperationReconciliationResultV5 request) => new(
            request.ResultId,
            request.AttemptId,
            null,
            request.OperationId,
            request.OriginalDispatchMessageId,
            request.GatewayStartReceiptId,
            request.ProviderCallAuthorizationReceiptId,
            null,
            null,
            request.ChallengeConsumptionId,
            request.ChallengeId,
            request.ChallengeMessageId,
            request.ChallengeResultCapability,
            request.TargetType,
            request.TargetId,
            request.TargetObservation,
            request.SubmittedResourceVersion,
            request.RequestedTargetState,
            request.DispatchTargetBindingSha256,
            request.DispatchPolicySnapshotSha256,
            request.Outcome,
            request.ObservationSha256,
            request.ObservedAtUtc,
            Sha256Utf8(request.ToCanonicalJson()));

        private static string Sha256Utf8(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            try
            {
                return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
    }
}
