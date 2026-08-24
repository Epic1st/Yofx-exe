using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.Persistence.Postgres;
using YO4X.Runtime.Contracts;
using YO4X.Tenancy;

namespace YO4X.RuntimeControl.Postgres;

public sealed class PostgresUserOperationGatewayApplication
    : IUserOperationGatewayBeginApplication,
      IUserOperationGatewayObservationApplication
{
    private readonly GatewayUserOperationPostgresDatabase database;
    private readonly UserOperationInvocationPostgresOptions options;
    private readonly UserOperationProtocolSingleFlight<UserOperationGatewayBeginAuthority>
        beginSingleFlight = new();

    public PostgresUserOperationGatewayApplication(
        GatewayUserOperationPostgresDatabase database,
        UserOperationInvocationPostgresOptions options)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        options.Validate();
    }

    public async Task<UserOperationGatewayBeginAuthority> BeginAsync(
        WorkloadActor actor,
        UserOperationGatewayBeginRequest request,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        UserOperationProtocolAdapterValidation.ValidateActor(actor, "gateway_host");
        UserOperationProtocolAdapterValidation.ValidateMetadata(metadata);
        ArgumentNullException.ThrowIfNull(request);
        Guid invocationId = UserOperationProtocolIdentity.Create(
            UserOperationProtocolIdentityPurpose.Invocation,
            actor,
            metadata,
            request.AttemptId,
            request.DispatchMessageId,
            request.DeliveryClaimId);
        Guid startReceiptId = UserOperationProtocolIdentity.Create(
            UserOperationProtocolIdentityPurpose.StartReceipt,
            actor,
            metadata,
            request.AttemptId,
            request.DispatchMessageId,
            request.DeliveryClaimId);
        string requestFingerprint =
            UserOperationProtocolIdentity.CreateDeliveryClaimFingerprint(
                request.GatewayCapability,
                request.DeliveryClaimGeneration);

        return await beginSingleFlight.RunAsync(
                invocationId,
                requestFingerprint,
                transitionCancellationToken => BeginCoreAsync(
                    actor,
                    request,
                    metadata,
                    invocationId,
                    startReceiptId,
                    transitionCancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<UserOperationGatewayBeginAuthority> BeginCoreAsync(
        WorkloadActor actor,
        UserOperationGatewayBeginRequest request,
        RequestMetadata metadata,
        Guid invocationId,
        Guid startReceiptId,
        CancellationToken cancellationToken)
    {
        UserOperationBearer redemption = CreateDistinctBearer(request.GatewayCapability);
        UserOperationBearer observation = CreateDistinctBearer(
            request.GatewayCapability,
            redemption);

        try
        {
            await using TenantPostgresTransaction transaction = await database
                .BeginTenantTransactionAsync(Context(actor, metadata), cancellationToken)
                .ConfigureAwait(false);
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                select
                    begin_status,
                    attempt_id,
                    invocation_id,
                    start_receipt_id,
                    state_version,
                    prepared_at,
                    redemption_capability,
                    receipt_capability,
                    credential_redemption_expires_at,
                    invocation_receipt_deadline
                from control.begin_user_operation_gateway_invocation(
                    p_attempt_id => @attempt_id,
                    p_delivery_claim_id => @delivery_claim_id,
                    p_delivery_claim_generation => @delivery_claim_generation,
                    p_raw_gateway_capability => @raw_gateway_capability,
                    p_invocation_id => @invocation_id,
                    p_start_receipt_id => @start_receipt_id,
                    p_raw_redemption_capability => @raw_redemption_capability,
                    p_raw_receipt_capability => @raw_receipt_capability,
                    p_receipt_lifetime => @receipt_lifetime,
                    p_expected_worker_instance_id => @expected_worker_instance_id,
                    p_expected_deployment_id => @expected_deployment_id,
                    p_expected_broker_account_id => @expected_broker_account_id,
                    p_expected_fence_generation => @expected_fence_generation,
                    p_expected_region => @expected_region)
                """);
            command.Parameters.AddWithValue("attempt_id", NpgsqlDbType.Uuid, request.AttemptId);
            command.Parameters.AddWithValue(
                "delivery_claim_id",
                NpgsqlDbType.Uuid,
                request.DeliveryClaimId);
            command.Parameters.AddWithValue(
                "delivery_claim_generation",
                NpgsqlDbType.Integer,
                request.DeliveryClaimGeneration);
            command.Parameters.AddWithValue(
                "raw_gateway_capability",
                NpgsqlDbType.Text,
                request.GatewayCapability.DangerousGetValue());
            command.Parameters.AddWithValue("invocation_id", NpgsqlDbType.Uuid, invocationId);
            command.Parameters.AddWithValue("start_receipt_id", NpgsqlDbType.Uuid, startReceiptId);
            command.Parameters.AddWithValue(
                "raw_redemption_capability",
                NpgsqlDbType.Text,
                redemption.DangerousGetValue());
            command.Parameters.AddWithValue(
                "raw_receipt_capability",
                NpgsqlDbType.Text,
                observation.DangerousGetValue());
            command.Parameters.AddWithValue(
                "receipt_lifetime",
                NpgsqlDbType.Interval,
                options.GatewayReceiptLifetime);
            UserOperationProtocolPostgresCommand.AddActorBinding(command, actor);

            UserOperationGatewayBeginAuthority? authority = null;
            bool alreadyCommitted = false;
            await using (NpgsqlDataReader reader = await command
                .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    string status = reader.GetString(0);
                    Guid attemptId = reader.GetGuid(1);
                    Guid persistedInvocationId = reader.GetGuid(2);
                    Guid persistedStartReceiptId = reader.GetGuid(3);
                    long stateVersion = reader.GetInt64(4);
                    DateTimeOffset preparedAt = UserOperationProtocolPostgresCommand.Utc(reader, 5);
                    string? returnedRedemption = reader.IsDBNull(6) ? null : reader.GetString(6);
                    string? returnedObservation = reader.IsDBNull(7) ? null : reader.GetString(7);
                    DateTimeOffset redemptionExpiresAt = UserOperationProtocolPostgresCommand.Utc(reader, 8);
                    DateTimeOffset observationExpiresAt = UserOperationProtocolPostgresCommand.Utc(reader, 9);
                    bool prepared = status == "prepared";
                    alreadyCommitted = status == "committed_no_replay";
                    if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                        || !prepared && !alreadyCommitted
                        || attemptId != request.AttemptId
                        || persistedInvocationId != invocationId
                        || persistedStartReceiptId != startReceiptId
                        || stateVersion <= 0
                        || preparedAt >= redemptionExpiresAt
                        || preparedAt >= observationExpiresAt
                        || redemptionExpiresAt > observationExpiresAt
                        || prepared && (!string.Equals(
                                returnedRedemption,
                                redemption.DangerousGetValue(),
                                StringComparison.Ordinal)
                            || !string.Equals(
                                returnedObservation,
                                observation.DangerousGetValue(),
                                StringComparison.Ordinal))
                        || alreadyCommitted
                            && (returnedRedemption is not null
                                || returnedObservation is not null))
                    {
                        throw new InvalidOperationException(
                            "PostgreSQL returned invalid gateway-begin evidence.");
                    }

                    if (prepared)
                    {
                        authority = UserOperationGatewayBeginAuthority.Create(
                            attemptId,
                            persistedInvocationId,
                            persistedStartReceiptId,
                            UserOperationBearer.Create(returnedRedemption!),
                            UserOperationBearer.Create(returnedObservation!),
                            preparedAt,
                            redemptionExpiresAt,
                            observationExpiresAt);
                    }
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            if (alreadyCommitted)
            {
                throw new UserOperationAuthorityAlreadyCommittedException(
                    UserOperationCommittedAuthorityPhase.Begin);
            }

            return authority ?? throw UserOperationInvocationPostgresErrors.Rejected();
        }
        catch (PostgresException exception)
            when (UserOperationInvocationPostgresErrors.IsExpected(exception))
        {
            throw UserOperationInvocationPostgresErrors.Map(exception, "gateway begin");
        }
    }

    public async Task<UserOperationGatewayObservationReceipt> RecordObservationAsync(
        WorkloadActor actor,
        UserOperationGatewayObservationRequest request,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        UserOperationProtocolAdapterValidation.ValidateActor(actor, "gateway_host");
        UserOperationProtocolAdapterValidation.ValidateMetadata(metadata);
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            await using TenantPostgresTransaction transaction = await database
                .BeginTenantTransactionAsync(Context(actor, metadata), cancellationToken)
                .ConfigureAwait(false);
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                select
                    observation_status,
                    attempt_id,
                    invocation_id,
                    gateway_observation_receipt_id,
                    authorization_id,
                    outcome,
                    observation_receipt_sha256,
                    target_observation,
                    observed_at,
                    received_at,
                    state_version
                from control.record_user_operation_gateway_observation_v5(
                    p_attempt_id => @attempt_id,
                    p_invocation_id => @invocation_id,
                    p_start_receipt_id => @start_receipt_id,
                    p_authorization_id => @authorization_id,
                    p_raw_receipt_capability => @raw_receipt_capability,
                    p_outcome => @outcome,
                    p_observation_sha256 => @observation_sha256,
                    p_target_observation => @target_observation,
                    p_observed_at => @observed_at,
                    p_expected_worker_instance_id => @expected_worker_instance_id,
                    p_expected_deployment_id => @expected_deployment_id,
                    p_expected_broker_account_id => @expected_broker_account_id,
                    p_expected_fence_generation => @expected_fence_generation,
                    p_expected_region => @expected_region)
                """);
            command.Parameters.AddWithValue("attempt_id", NpgsqlDbType.Uuid, request.AttemptId);
            command.Parameters.AddWithValue("invocation_id", NpgsqlDbType.Uuid, request.InvocationId);
            command.Parameters.AddWithValue(
                "start_receipt_id",
                NpgsqlDbType.Uuid,
                request.GatewayStartReceiptId);
            command.Parameters.AddWithValue(
                "authorization_id",
                NpgsqlDbType.Uuid,
                request.ProviderCallAuthorizationReceiptId);
            command.Parameters.AddWithValue(
                "raw_receipt_capability",
                NpgsqlDbType.Text,
                request.GatewayObservationReceiptBearer.DangerousGetValue());
            command.Parameters.AddWithValue(
                "outcome",
                NpgsqlDbType.Text,
                UserOperationProtocolAdapterValidation.Outcome(request.Outcome));
            command.Parameters.AddWithValue(
                "observation_sha256",
                NpgsqlDbType.Text,
                request.ObservationSha256);
            command.Parameters.AddWithValue(
                "observed_at",
                NpgsqlDbType.TimestampTz,
                request.ObservedAtUtc);
            command.Parameters.AddWithValue(
                "target_observation",
                NpgsqlDbType.Jsonb,
                request.TargetObservation.ToCanonicalJson());
            UserOperationProtocolPostgresCommand.AddActorBinding(command, actor);

            UserOperationGatewayObservationReceipt? receipt = null;
            await using (NpgsqlDataReader reader = await command
                .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    string status = reader.GetString(0);
                    Guid attemptId = reader.GetGuid(1);
                    Guid invocationId = reader.GetGuid(2);
                    Guid receiptId = reader.GetGuid(3);
                    Guid authorizationId = reader.GetGuid(4);
                    UserOperationObservationOutcome outcome =
                        UserOperationProtocolAdapterValidation.Outcome(reader.GetString(5));
                    string receiptSha256 = reader.GetString(6);
                    string returnedTargetObservationJson = reader.GetString(7);
                    DateTimeOffset observedAt = UserOperationProtocolPostgresCommand.Utc(reader, 8);
                    DateTimeOffset receivedAt = UserOperationProtocolPostgresCommand.Utc(reader, 9);
                    long stateVersion = reader.GetInt64(10);
                    if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                        || status is not ("recorded" or "duplicate")
                        || attemptId != request.AttemptId
                        || invocationId != request.InvocationId
                        || authorizationId != request.ProviderCallAuthorizationReceiptId
                        || receiptId == Guid.Empty
                         || outcome != request.Outcome
                         || !UserOperationProtocolPostgresCommand.IsSha256(receiptSha256)
                         || !IsExactReturnedTargetObservation(
                             returnedTargetObservationJson,
                             request.TargetObservation)
                         || observedAt != request.ObservedAtUtc
                         || receivedAt == default
                         || stateVersion <= 0)
                    {
                        throw new InvalidOperationException(
                            "PostgreSQL returned invalid gateway-observation evidence.");
                    }

                    receipt = UserOperationGatewayObservationReceipt.Create(
                        attemptId,
                        invocationId,
                        receiptId,
                        authorizationId,
                        outcome,
                        request.TargetObservation,
                        receiptSha256,
                        observedAt);
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return receipt ?? throw UserOperationInvocationPostgresErrors.Rejected();
        }
        catch (PostgresException exception)
            when (UserOperationInvocationPostgresErrors.IsExpected(exception))
        {
            throw UserOperationInvocationPostgresErrors.Map(exception, "gateway observation");
        }
    }

    private static UserOperationBearer CreateDistinctBearer(
        params UserOperationBearer[] excluded)
    {
        while (true)
        {
            UserOperationBearer candidate = UserOperationProtocolIdentity.CreateBearer();
            if (excluded.All(value => !string.Equals(
                    value.DangerousGetValue(),
                    candidate.DangerousGetValue(),
                    StringComparison.Ordinal)))
            {
                return candidate;
            }
        }
    }

    internal static bool IsExactReturnedTargetObservation(
        string returnedJson,
        UserOperationTargetObservation expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (string.IsNullOrWhiteSpace(returnedJson))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(
                returnedJson,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 8
                });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            string returnedCanonical = CanonicalJson.Serialize(document.RootElement);
            return string.Equals(
                returnedCanonical,
                expected.ToCanonicalJson(),
                StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static TenantExecutionContext Context(
        WorkloadActor actor,
        RequestMetadata metadata) =>
        new(actor.TenantId, actor.WorkloadId, metadata.CorrelationId, null);

}
