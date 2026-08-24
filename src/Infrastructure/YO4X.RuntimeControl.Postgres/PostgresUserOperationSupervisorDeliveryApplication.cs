using Npgsql;
using NpgsqlTypes;
using YO4X.ControlPlane.Application;
using YO4X.Persistence.Postgres;
using YO4X.Runtime.Contracts;
using YO4X.Tenancy;

namespace YO4X.RuntimeControl.Postgres;

public sealed class PostgresUserOperationSupervisorDeliveryApplication
    : IUserOperationSupervisorDeliveryApplication
{
    private readonly SupervisorUserOperationPostgresDatabase database;
    private readonly UserOperationInvocationPostgresOptions options;
    private readonly UserOperationProtocolSingleFlight<UserOperationGatewayDeliveryClaim>
        claimSingleFlight = new();

    public PostgresUserOperationSupervisorDeliveryApplication(
        SupervisorUserOperationPostgresDatabase database,
        UserOperationInvocationPostgresOptions options)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        options.Validate();
    }

    public async Task<UserOperationGatewayDeliveryClaim> ClaimForGatewayAsync(
        WorkloadActor actor,
        UserOperationSupervisorDeliveryClaimRequest request,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        UserOperationProtocolAdapterValidation.ValidateActor(actor, "supervisor");
        UserOperationProtocolAdapterValidation.ValidateMetadata(metadata);
        ArgumentNullException.ThrowIfNull(request);
        Guid claimId = UserOperationProtocolIdentity.Create(
            UserOperationProtocolIdentityPurpose.DeliveryClaim,
            actor,
            metadata,
            request.AttemptId,
            request.DispatchMessageId);
        string requestFingerprint =
            UserOperationProtocolIdentity.CreateBearerFingerprint(
                request.DeliveryCapability);

        return await claimSingleFlight.RunAsync(
                claimId,
                requestFingerprint,
                transitionCancellationToken => ClaimForGatewayCoreAsync(
                    actor,
                    request,
                    metadata,
                    claimId,
                    transitionCancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<UserOperationGatewayDeliveryClaim> ClaimForGatewayCoreAsync(
        WorkloadActor actor,
        UserOperationSupervisorDeliveryClaimRequest request,
        RequestMetadata metadata,
        Guid claimId,
        CancellationToken cancellationToken)
    {
        UserOperationBearer gatewayCapability =
            UserOperationProtocolIdentity.CreateBearer();

        try
        {
            await using TenantPostgresTransaction transaction = await database
                .BeginTenantTransactionAsync(Context(actor, metadata), cancellationToken)
                .ConfigureAwait(false);
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                select
                    claim_status,
                    attempt_id,
                    dispatch_message_id,
                    delivery_claim_id,
                    delivery_claim_generation,
                    state_version,
                    delivery_claimed_at,
                    gateway_capability_expires_at,
                    execute_not_after,
                    route_deployment_id,
                    fence_generation,
                    worker_assignment_id,
                    worker_instance_id
                from control.claim_user_operation_delivery(
                    p_attempt_id => @attempt_id,
                    p_raw_delivery_capability => @raw_delivery_capability,
                    p_delivery_claim_id => @delivery_claim_id,
                    p_raw_gateway_capability => @raw_gateway_capability,
                    p_requested_claim_lifetime => @requested_claim_lifetime,
                    p_expected_worker_instance_id => @expected_worker_instance_id,
                    p_expected_deployment_id => @expected_deployment_id,
                    p_expected_broker_account_id => @expected_broker_account_id,
                    p_expected_fence_generation => @expected_fence_generation,
                    p_expected_region => @expected_region)
                """);
            command.Parameters.AddWithValue("attempt_id", NpgsqlDbType.Uuid, request.AttemptId);
            command.Parameters.AddWithValue(
                "raw_delivery_capability",
                NpgsqlDbType.Text,
                request.DeliveryCapability.DangerousGetValue());
            command.Parameters.AddWithValue("delivery_claim_id", NpgsqlDbType.Uuid, claimId);
            command.Parameters.AddWithValue(
                "raw_gateway_capability",
                NpgsqlDbType.Text,
                gatewayCapability.DangerousGetValue());
            command.Parameters.AddWithValue(
                "requested_claim_lifetime",
                NpgsqlDbType.Interval,
                options.DeliveryClaimLifetime);
            UserOperationProtocolPostgresCommand.AddActorBinding(command, actor);

            UserOperationGatewayDeliveryClaim? claim = null;
            await using (NpgsqlDataReader reader = await command
                .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    string status = reader.GetString(0);
                    Guid attemptId = reader.GetGuid(1);
                    Guid dispatchMessageId = reader.GetGuid(2);
                    Guid persistedClaimId = reader.GetGuid(3);
                    int claimGeneration = reader.GetInt32(4);
                    long stateVersion = reader.GetInt64(5);
                    DateTimeOffset claimedAt = UserOperationProtocolPostgresCommand.Utc(reader, 6);
                    DateTimeOffset expiresAt = UserOperationProtocolPostgresCommand.Utc(reader, 7);
                    DateTimeOffset executeNotAfter = UserOperationProtocolPostgresCommand.Utc(reader, 8);
                    Guid deploymentId = reader.GetGuid(9);
                    long fenceGeneration = reader.GetInt64(10);
                    Guid assignmentId = reader.GetGuid(11);
                    Guid workerInstanceId = reader.GetGuid(12);
                    if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                        || status is not ("claimed" or "rotated" or "duplicate")
                        || attemptId != request.AttemptId
                        || dispatchMessageId != request.DispatchMessageId
                        || persistedClaimId != claimId
                        || claimGeneration <= 0
                        || stateVersion <= 0
                        || claimedAt >= expiresAt
                        || expiresAt > executeNotAfter
                        || deploymentId != actor.DeploymentId
                        || fenceGeneration != actor.Generation
                        || workerInstanceId != actor.WorkerInstanceId
                        || assignmentId == Guid.Empty)
                    {
                        throw new InvalidOperationException(
                            "PostgreSQL returned invalid delivery-claim evidence.");
                    }

                    claim = UserOperationGatewayDeliveryClaim.Create(
                        attemptId,
                        dispatchMessageId,
                        persistedClaimId,
                        claimGeneration,
                        gatewayCapability,
                        claimedAt,
                        expiresAt);
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return claim ?? throw UserOperationInvocationPostgresErrors.Rejected();
        }
        catch (PostgresException exception)
            when (UserOperationInvocationPostgresErrors.IsExpected(exception))
        {
            throw UserOperationInvocationPostgresErrors.Map(exception, "delivery claim");
        }
    }

    public async Task<UserOperationGatewayRejectBeforeBeginReceipt> RejectBeforeBeginAsync(
        WorkloadActor actor,
        UserOperationGatewayRejectBeforeBeginRequest request,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        UserOperationProtocolAdapterValidation.ValidateActor(actor, "supervisor");
        UserOperationProtocolAdapterValidation.ValidateMetadata(metadata);
        ArgumentNullException.ThrowIfNull(request);
        Guid receiptId = UserOperationProtocolIdentity.Create(
            UserOperationProtocolIdentityPurpose.RejectionReceipt,
            actor,
            metadata,
            request.AttemptId,
            request.DeliveryClaimId);

        try
        {
            await using TenantPostgresTransaction transaction = await database
                .BeginTenantTransactionAsync(Context(actor, metadata), cancellationToken)
                .ConfigureAwait(false);
            await using NpgsqlCommand command = transaction.CreateCommand(
                """
                select rejection_status, attempt_id, state_version,
                    not_sent_at, receipt_id, receipt_sha256
                from control.reject_user_operation_before_invocation(
                    p_attempt_id => @attempt_id,
                    p_delivery_claim_id => @delivery_claim_id,
                    p_delivery_claim_generation => @delivery_claim_generation,
                    p_raw_gateway_capability => @raw_gateway_capability,
                    p_receipt_id => @receipt_id,
                    p_reason_code => @reason_code,
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
            command.Parameters.AddWithValue("receipt_id", NpgsqlDbType.Uuid, receiptId);
            command.Parameters.AddWithValue("reason_code", NpgsqlDbType.Text, request.ReasonCode);
            UserOperationProtocolPostgresCommand.AddActorBinding(command, actor);

            UserOperationGatewayRejectBeforeBeginReceipt? receipt = null;
            await using (NpgsqlDataReader reader = await command
                .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    string status = reader.GetString(0);
                    Guid attemptId = reader.GetGuid(1);
                    long stateVersion = reader.GetInt64(2);
                    DateTimeOffset rejectedAt = UserOperationProtocolPostgresCommand.Utc(reader, 3);
                    Guid persistedReceiptId = reader.GetGuid(4);
                    string receiptSha256 = reader.GetString(5);
                    if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                        || status is not ("rejected" or "duplicate")
                        || attemptId != request.AttemptId
                        || persistedReceiptId != receiptId
                        || stateVersion <= 0
                        || rejectedAt == default
                        || !UserOperationProtocolPostgresCommand.IsSha256(receiptSha256))
                    {
                        throw new InvalidOperationException(
                            "PostgreSQL returned invalid rejection evidence.");
                    }

                    receipt = new UserOperationGatewayRejectBeforeBeginReceipt(
                        attemptId,
                        request.DeliveryClaimId,
                        persistedReceiptId,
                        rejectedAt);
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return receipt ?? throw UserOperationInvocationPostgresErrors.Rejected();
        }
        catch (PostgresException exception)
            when (UserOperationInvocationPostgresErrors.IsExpected(exception))
        {
            throw UserOperationInvocationPostgresErrors.Map(exception, "pre-invocation rejection");
        }
    }

    private static TenantExecutionContext Context(
        WorkloadActor actor,
        RequestMetadata metadata) =>
        new(actor.TenantId, actor.WorkloadId, metadata.CorrelationId, null);

}
