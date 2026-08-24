using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.Persistence.Postgres;
using YO4X.Runtime.Contracts;
using YO4X.Tenancy;

namespace YO4X.RuntimeControl.Postgres;

/// <summary>
/// Owns the irreversible credential-runtime boundary. PostgreSQL authorization
/// is committed and the connection is closed before any provider invocation.
/// </summary>
public sealed class PostgresUserOperationCredentialBoundaryApplication
    : IUserOperationCredentialBoundaryApplication
{
    private const string AmbiguityReason = "provider_call_completion_unknown";

    private readonly CredentialUserOperationPostgresDatabase database;
    private readonly IUserOperationProviderCallInvoker providerInvoker;
    private readonly UserOperationInvocationPostgresOptions options;

    public PostgresUserOperationCredentialBoundaryApplication(
        CredentialUserOperationPostgresDatabase database,
        IUserOperationProviderCallInvoker providerInvoker,
        UserOperationInvocationPostgresOptions options)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
        this.providerInvoker = providerInvoker
            ?? throw new ArgumentNullException(nameof(providerInvoker));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        options.Validate();
    }

    public async Task<UserOperationProviderCallExecutionReceipt> ExecuteProviderCallOnceAsync(
        WorkloadActor actor,
        UserOperationProviderCallExecutionRequest request,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        UserOperationProtocolAdapterValidation.ValidateActor(actor, "gateway_host");
        UserOperationProtocolAdapterValidation.ValidateMetadata(metadata);
        ArgumentNullException.ThrowIfNull(request);

        Guid authorizationId = UserOperationProtocolIdentity.Create(
            UserOperationProtocolIdentityPurpose.ProviderAuthorization,
            actor,
            metadata,
            request.AttemptId,
            request.InvocationId,
            request.GatewayStartReceiptId);
        ProviderAuthorization authority = await AuthorizeAndCommitAsync(
                actor,
                request,
                metadata,
                authorizationId,
                cancellationToken)
            .ConfigureAwait(false);

        if (!authority.ProviderCallAuthorized)
        {
            throw new UserOperationAuthorityAlreadyCommittedException(
                UserOperationCommittedAuthorityPhase.ProviderAuthorization);
        }

        try
        {
            UserOperationProviderInvocationObservation observation =
                await providerInvoker.InvokeOnceAsync(
                        authority.Command!,
                        cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "The provider invoker returned no observation.");
            ValidateObservation(authority, observation);
            return UserOperationProviderCallObservedReceipt.Create(
                request.AttemptId,
                request.InvocationId,
                request.GatewayStartReceiptId,
                authorizationId,
                observation.Outcome,
                observation.TargetObservation,
                observation.ObservedAtUtc);
        }
        catch (Exception)
        {
            try
            {
                using var persistenceDeadline =
                    new CancellationTokenSource(options.AmbiguityPersistenceTimeout);
                return await RecordAmbiguityAsync(
                        actor,
                        request,
                        metadata,
                        authorizationId,
                        persistenceDeadline.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                throw new UserOperationProviderCallCompletionUncertainException();
            }
        }
    }

    private async Task<ProviderAuthorization> AuthorizeAndCommitAsync(
        WorkloadActor actor,
        UserOperationProviderCallExecutionRequest request,
        RequestMetadata metadata,
        Guid authorizationId,
        CancellationToken cancellationToken)
    {
        bool authorizationTransitionObserved = false;
        try
        {
            ProviderAuthorization? authority = null;
            await using (TenantPostgresTransaction transaction = await database
                .BeginTenantTransactionAsync(Context(actor, metadata), cancellationToken)
                .ConfigureAwait(false))
            {
                await using NpgsqlCommand command = transaction.CreateCommand(
                    """
                    select
                        authorization_status,
                        provider_call_authorized,
                        attempt_id,
                        invocation_id,
                        authorization_id,
                        provider_call_authorized_at,
                        execute_not_after,
                        operation_id,
                        operation_type,
                        target_type,
                        target_id,
                        broker_account_id,
                        command_sha256,
                        command_descriptor,
                        authorization_receipt_sha256,
                        invocation_receipt_deadline
                    from control.authorize_user_operation_provider_call(
                        p_attempt_id => @attempt_id,
                        p_invocation_id => @invocation_id,
                        p_start_receipt_id => @start_receipt_id,
                        p_authorization_id => @authorization_id,
                        p_raw_redemption_capability => @raw_redemption_capability,
                        p_expected_worker_instance_id => @expected_worker_instance_id,
                        p_expected_deployment_id => @expected_deployment_id,
                        p_expected_broker_account_id => @expected_broker_account_id,
                        p_expected_fence_generation => @expected_fence_generation,
                        p_expected_region => @expected_region)
                    """);
                command.Parameters.AddWithValue("attempt_id", NpgsqlDbType.Uuid, request.AttemptId);
                command.Parameters.AddWithValue(
                    "invocation_id",
                    NpgsqlDbType.Uuid,
                    request.InvocationId);
                command.Parameters.AddWithValue(
                    "start_receipt_id",
                    NpgsqlDbType.Uuid,
                    request.GatewayStartReceiptId);
                command.Parameters.AddWithValue(
                    "authorization_id",
                    NpgsqlDbType.Uuid,
                    authorizationId);
                command.Parameters.AddWithValue(
                    "raw_redemption_capability",
                    NpgsqlDbType.Text,
                    request.RedemptionNonce.DangerousGetValue());
                UserOperationProtocolPostgresCommand.AddActorBinding(command, actor);

                await using (NpgsqlDataReader reader = await command
                    .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        authorizationTransitionObserved = true;
                        authority = ParseAuthorization(
                            reader,
                            actor,
                            request,
                            authorizationId);
                        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            throw new InvalidOperationException(
                                "PostgreSQL returned multiple provider authorizations.");
                        }
                    }
                }

                if (authority is null)
                {
                    throw UserOperationInvocationPostgresErrors.Rejected();
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            return authority;
        }
        catch (UserOperationProviderAuthorizationCommitUncertainException)
        {
            throw;
        }
        catch (PostgresException exception)
            when (!authorizationTransitionObserved
                  && UserOperationInvocationPostgresErrors.IsExpected(exception))
        {
            throw UserOperationInvocationPostgresErrors.Map(
                exception,
                "provider authorization");
        }
        catch (Exception) when (authorizationTransitionObserved)
        {
            throw new UserOperationProviderAuthorizationCommitUncertainException();
        }
    }

    private async Task<UserOperationProviderCallAmbiguousReceipt> RecordAmbiguityAsync(
        WorkloadActor actor,
        UserOperationProviderCallExecutionRequest request,
        RequestMetadata metadata,
        Guid authorizationId,
        CancellationToken cancellationToken)
    {
        await using TenantPostgresTransaction transaction = await database
            .BeginTenantTransactionAsync(Context(actor, metadata), cancellationToken)
            .ConfigureAwait(false);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select
                ambiguity_status,
                attempt_id,
                invocation_id,
                start_receipt_id,
                authorization_id,
                ambiguity_receipt_id,
                state_version,
                ambiguous_at,
                ambiguity_receipt_sha256
            from control.record_user_operation_provider_call_ambiguity(
                p_attempt_id => @attempt_id,
                p_invocation_id => @invocation_id,
                p_start_receipt_id => @start_receipt_id,
                p_authorization_id => @authorization_id,
                p_reason_code => @reason_code,
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
        command.Parameters.AddWithValue("authorization_id", NpgsqlDbType.Uuid, authorizationId);
        command.Parameters.AddWithValue("reason_code", NpgsqlDbType.Text, AmbiguityReason);
        UserOperationProtocolPostgresCommand.AddActorBinding(command, actor);

        UserOperationProviderCallAmbiguousReceipt? receipt = null;
        await using (NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                string status = reader.GetString(0);
                Guid attemptId = reader.GetGuid(1);
                Guid invocationId = reader.GetGuid(2);
                Guid startReceiptId = reader.GetGuid(3);
                Guid returnedAuthorizationId = reader.GetGuid(4);
                Guid ambiguityReceiptId = reader.GetGuid(5);
                long stateVersion = reader.GetInt64(6);
                DateTimeOffset ambiguousAt =
                    UserOperationProtocolPostgresCommand.Utc(reader, 7);
                string receiptSha256 = reader.GetString(8);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                    || status is not ("recorded" or "duplicate")
                    || attemptId != request.AttemptId
                    || invocationId != request.InvocationId
                    || startReceiptId != request.GatewayStartReceiptId
                    || returnedAuthorizationId != authorizationId
                    || ambiguityReceiptId == Guid.Empty
                    || stateVersion <= 0
                    || ambiguousAt == default
                    || !UserOperationProtocolPostgresCommand.IsSha256(receiptSha256))
                {
                    throw new InvalidOperationException(
                        "PostgreSQL returned invalid provider-ambiguity evidence.");
                }

                receipt = UserOperationProviderCallAmbiguousReceipt.Create(
                    attemptId,
                    invocationId,
                    startReceiptId,
                    returnedAuthorizationId,
                    ambiguityReceiptId,
                    ambiguousAt);
            }
        }

        if (receipt is null)
        {
            throw UserOperationInvocationPostgresErrors.Rejected();
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return receipt;
    }

    private static ProviderAuthorization ParseAuthorization(
        NpgsqlDataReader reader,
        WorkloadActor actor,
        UserOperationProviderCallExecutionRequest request,
        Guid expectedAuthorizationId)
    {
        string status = reader.GetString(0);
        bool authorized = reader.GetBoolean(1);
        Guid attemptId = reader.GetGuid(2);
        Guid invocationId = reader.GetGuid(3);
        Guid authorizationId = reader.GetGuid(4);
        if (attemptId != request.AttemptId
            || invocationId != request.InvocationId
            || authorizationId != expectedAuthorizationId)
        {
            throw new InvalidOperationException(
                "PostgreSQL returned a mismatched provider authorization.");
        }

        if (status == "committed_no_reissue")
        {
            if (authorized
                || !reader.IsDBNull(13)
                || !reader.IsDBNull(14))
            {
                throw new InvalidOperationException(
                    "PostgreSQL reissued provider-call authority.");
            }

            return new ProviderAuthorization(false, null, default, default);
        }

        if (status != "authorized"
            || !authorized
            || reader.IsDBNull(5)
            || reader.IsDBNull(6)
            || reader.IsDBNull(7)
            || reader.IsDBNull(8)
            || reader.IsDBNull(9)
            || reader.IsDBNull(10)
            || reader.IsDBNull(11)
            || reader.IsDBNull(12)
            || reader.IsDBNull(13)
            || reader.IsDBNull(14)
            || reader.IsDBNull(15))
        {
            throw new InvalidOperationException(
                "PostgreSQL returned incomplete provider-call authority.");
        }

        DateTimeOffset authorizedAt = UserOperationProtocolPostgresCommand.Utc(reader, 5);
        DateTimeOffset executeNotAfter = UserOperationProtocolPostgresCommand.Utc(reader, 6);
        Guid operationId = reader.GetGuid(7);
        string operationType = reader.GetString(8);
        string targetType = reader.GetString(9);
        Guid targetId = reader.GetGuid(10);
        Guid brokerAccountId = reader.GetGuid(11);
        string commandSha256 = reader.GetString(12);
        string commandDescriptor = reader.GetString(13);
        string authorizationReceiptSha256 = reader.GetString(14);
        DateTimeOffset receiptDeadline = UserOperationProtocolPostgresCommand.Utc(reader, 15);
        if (authorizedAt == default
            || executeNotAfter <= authorizedAt
            || receiptDeadline <= authorizedAt
            || operationId == Guid.Empty
            || targetId == Guid.Empty
            || brokerAccountId != actor.BrokerAccountId
            || targetType == "deployment" && targetId != actor.DeploymentId
            || targetType == "broker_account" && targetId != actor.BrokerAccountId
            || !UserOperationProtocolPostgresCommand.IsSha256(commandSha256)
            || !UserOperationProtocolPostgresCommand.IsSha256(authorizationReceiptSha256))
        {
            throw new InvalidOperationException(
                "PostgreSQL returned invalid provider-call authority.");
        }

        UserOperationProviderCommand providerCommand =
            UserOperationProviderCommandDescriptor.Parse(
                commandDescriptor,
                actor.TenantId,
                operationId,
                operationType,
                targetType,
                targetId,
                brokerAccountId,
                commandSha256,
                executeNotAfter);
        return new ProviderAuthorization(
            true,
            providerCommand,
            authorizedAt,
            receiptDeadline);
    }

    private static void ValidateObservation(
        ProviderAuthorization authority,
        UserOperationProviderInvocationObservation observation)
    {
        UserOperationProviderCommand command = authority.Command!;
        observation.TargetObservation.ValidateResultConsistency(
            command.TargetType,
            command.RequestedTargetState,
            command.TargetBindingSha256,
            observation.Outcome);
        if (observation.ObservedAtUtc < authority.AuthorizedAtUtc
            || observation.ObservedAtUtc >= authority.ReceiptDeadlineUtc)
        {
            throw new InvalidOperationException(
                "The provider observation is outside its authorized evidence window.");
        }
    }

    private static TenantExecutionContext Context(
        WorkloadActor actor,
        RequestMetadata metadata) =>
        new(actor.TenantId, actor.WorkloadId, metadata.CorrelationId, null);

    private sealed record ProviderAuthorization(
        bool ProviderCallAuthorized,
        UserOperationProviderCommand? Command,
        DateTimeOffset AuthorizedAtUtc,
        DateTimeOffset ReceiptDeadlineUtc);
}

internal static class UserOperationProviderCommandDescriptor
{
    private static readonly HashSet<string> ExactProperties =
        new(StringComparer.Ordinal)
        {
            "operationId",
            "operationType",
            "requestedTargetState",
            "submittedResourceVersion",
            "targetBindingSha256",
            "targetId",
            "targetType",
            "tenantId"
        };

    public static UserOperationProviderCommand Parse(
        string descriptorJson,
        Guid expectedTenantId,
        Guid expectedOperationId,
        string expectedOperationType,
        string expectedTargetType,
        Guid expectedTargetId,
        Guid brokerAccountId,
        string expectedCommandSha256,
        DateTimeOffset executeNotAfterUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptorJson);
        using JsonDocument document = JsonDocument.Parse(
            descriptorJson,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8
            });
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "The provider command descriptor is not an object.");
        }

        JsonProperty[] properties = root.EnumerateObject().ToArray();
        if (properties.Length != ExactProperties.Count
            || properties.Select(static property => property.Name)
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(ExactProperties) is false)
        {
            throw new InvalidOperationException(
                "The provider command descriptor shape is invalid.");
        }

        Guid tenantId = ReadGuid(root, "tenantId");
        Guid operationId = ReadGuid(root, "operationId");
        string operationType = ReadString(root, "operationType");
        string requestedTargetState = ReadString(root, "requestedTargetState");
        long submittedResourceVersion = ReadInt64(root, "submittedResourceVersion");
        string targetBindingSha256 = ReadString(root, "targetBindingSha256");
        Guid targetId = ReadGuid(root, "targetId");
        string targetType = ReadString(root, "targetType");
        if (tenantId != expectedTenantId
            || operationId != expectedOperationId
            || !string.Equals(operationType, expectedOperationType, StringComparison.Ordinal)
            || !string.Equals(targetType, expectedTargetType, StringComparison.Ordinal)
            || targetId != expectedTargetId)
        {
            throw new InvalidOperationException(
                "The provider command descriptor binding is invalid.");
        }

        string actualCommandSha256 = CanonicalJson.Sha256(new
        {
            operationId = operationId.ToString("D"),
            operationType,
            requestedTargetState,
            submittedResourceVersion,
            targetBindingSha256,
            targetId = targetId.ToString("D"),
            targetType,
            tenantId = tenantId.ToString("D")
        });
        if (!string.Equals(
                actualCommandSha256,
                expectedCommandSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The provider command descriptor digest is invalid.");
        }

        return UserOperationProviderCommand.Create(
            tenantId,
            operationId,
            operationType,
            targetType,
            targetId,
            brokerAccountId,
            submittedResourceVersion,
            requestedTargetState,
            targetBindingSha256,
            expectedCommandSha256,
            executeNotAfterUtc);
    }

    private static Guid ReadGuid(JsonElement root, string name)
    {
        string value = ReadString(root, name);
        if (!Guid.TryParseExact(value, "D", out Guid parsed)
            || !string.Equals(parsed.ToString("D"), value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The provider command descriptor identifier is invalid.");
        }

        return parsed;
    }

    private static string ReadString(JsonElement root, string name)
    {
        JsonElement value = root.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                "The provider command descriptor contains an invalid string.");
        }

        return value.GetString()!;
    }

    private static long ReadInt64(JsonElement root, string name)
    {
        JsonElement value = root.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long parsed))
        {
            throw new InvalidOperationException(
                "The provider command descriptor contains an invalid integer.");
        }

        return parsed;
    }
}
