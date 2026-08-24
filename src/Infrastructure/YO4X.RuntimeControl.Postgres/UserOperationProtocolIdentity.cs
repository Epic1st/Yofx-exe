using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.Runtime.Contracts;

namespace YO4X.RuntimeControl.Postgres;

internal enum UserOperationProtocolIdentityPurpose
{
    DeliveryClaim = 0,
    RejectionReceipt = 1,
    Invocation = 2,
    StartReceipt = 3,
    ProviderAuthorization = 4
}

internal static class UserOperationProtocolIdentity
{
    public static Guid Create(
        UserOperationProtocolIdentityPurpose purpose,
        WorkloadActor actor,
        RequestMetadata metadata,
        params Guid[] phaseBindings)
    {
        UserOperationProtocolAdapterValidation.ValidateActor(actor);
        UserOperationProtocolAdapterValidation.ValidateMetadata(metadata);
        if (!Enum.IsDefined(purpose)
            || phaseBindings is null
            || phaseBindings.Length == 0
            || phaseBindings.Any(static value => value == Guid.Empty))
        {
            throw new ArgumentException("The protocol identity binding is invalid.");
        }

        string canonical = CanonicalJson.Serialize(new
        {
            Contract = "yo4x.user-operation.protocol-identity.v1",
            Purpose = Purpose(purpose),
            TenantId = actor.TenantId.ToString("D"),
            WorkloadId = actor.WorkloadId.ToString("D"),
            WorkerInstanceId = actor.WorkerInstanceId.ToString("D"),
            DeploymentId = actor.DeploymentId.ToString("D"),
            BrokerAccountId = actor.BrokerAccountId.ToString("D"),
            actor.Generation,
            actor.Region,
            actor.Component,
            metadata.IdempotencyKey,
            PhaseBindings = phaseBindings.Select(static value => value.ToString("D")).ToArray()
        });
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        try
        {
            // UUIDv8 marks an application-defined, name-derived identifier.
            digest[6] = (byte)((digest[6] & 0x0f) | 0x80);
            digest[8] = (byte)((digest[8] & 0x3f) | 0x80);
            return new Guid(digest.AsSpan(0, 16), bigEndian: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(digest);
        }
    }

    public static UserOperationBearer CreateBearer()
    {
        byte[] randomBytes = RandomNumberGenerator.GetBytes(32);
        try
        {
            return UserOperationBearer.Create(CanonicalBase64Url.Encode(randomBytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(randomBytes);
        }
    }

    public static string CreateBearerFingerprint(UserOperationBearer bearer)
    {
        ArgumentNullException.ThrowIfNull(bearer);
        byte[] bearerBytes = Encoding.ASCII.GetBytes(bearer.DangerousGetValue());
        byte[] digest = SHA256.HashData(bearerBytes);
        try
        {
            return Convert.ToHexString(digest).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bearerBytes);
            CryptographicOperations.ZeroMemory(digest);
        }
    }

    public static string CreateDeliveryClaimFingerprint(
        UserOperationBearer bearer,
        int deliveryClaimGeneration)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deliveryClaimGeneration);
        return CanonicalJson.Sha256(new
        {
            Contract = "yo4x.user-operation.gateway-begin-single-flight.v1",
            DeliveryClaimGeneration = deliveryClaimGeneration,
            GatewayCapabilitySha256 = CreateBearerFingerprint(bearer)
        });
    }

    private static string Purpose(UserOperationProtocolIdentityPurpose purpose) => purpose switch
    {
        UserOperationProtocolIdentityPurpose.DeliveryClaim => "delivery_claim",
        UserOperationProtocolIdentityPurpose.RejectionReceipt => "rejection_receipt",
        UserOperationProtocolIdentityPurpose.Invocation => "invocation",
        UserOperationProtocolIdentityPurpose.StartReceipt => "start_receipt",
        UserOperationProtocolIdentityPurpose.ProviderAuthorization => "provider_authorization",
        _ => throw new ArgumentOutOfRangeException(nameof(purpose))
    };
}

internal static class UserOperationProtocolPostgresCommand
{
    public static void AddActorBinding(NpgsqlCommand command, WorkloadActor actor)
    {
        command.Parameters.AddWithValue(
            "expected_worker_instance_id",
            NpgsqlDbType.Uuid,
            actor.WorkerInstanceId);
        command.Parameters.AddWithValue(
            "expected_deployment_id",
            NpgsqlDbType.Uuid,
            actor.DeploymentId);
        command.Parameters.AddWithValue(
            "expected_broker_account_id",
            NpgsqlDbType.Uuid,
            actor.BrokerAccountId);
        command.Parameters.AddWithValue(
            "expected_fence_generation",
            NpgsqlDbType.Bigint,
            actor.Generation);
        command.Parameters.AddWithValue("expected_region", NpgsqlDbType.Text, actor.Region);
    }

    public static DateTimeOffset Utc(NpgsqlDataReader reader, int ordinal) =>
        reader.GetFieldValue<DateTimeOffset>(ordinal).ToUniversalTime();

    public static bool IsSha256(string? value) =>
        value is { Length: 64 }
        && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

internal static class UserOperationProtocolAdapterValidation
{
    public static void ValidateActor(WorkloadActor actor, string? requiredComponent = null)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.TenantId == Guid.Empty
            || actor.WorkloadId == Guid.Empty
            || actor.WorkerInstanceId == Guid.Empty
            || actor.DeploymentId == Guid.Empty
            || actor.BrokerAccountId == Guid.Empty
            || actor.Generation <= 0
            || string.IsNullOrWhiteSpace(actor.Region)
            || actor.Region.Length > 100
            || actor.Component is not ("supervisor" or "strategy_host" or "gateway_host"))
        {
            throw new UnauthorizedAccessException("The workload identity binding is invalid.");
        }

        if (requiredComponent is not null
            && !string.Equals(actor.Component, requiredComponent, StringComparison.Ordinal))
        {
            throw new AuthorizationDeniedException(
                "USER_OPERATION_WORKLOAD_ROLE_REQUIRED",
                "The user-operation protocol requires the assigned workload role.");
        }
    }

    public static void ValidateMetadata(RequestMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (metadata.CorrelationId == Guid.Empty
            || string.IsNullOrWhiteSpace(metadata.IdempotencyKey)
            || metadata.IdempotencyKey.Length > 200
            || metadata.Reason?.Length > 2000)
        {
            throw new ArgumentException("The request metadata is invalid.", nameof(metadata));
        }
    }

    public static string Outcome(UserOperationObservationOutcome outcome) => outcome switch
    {
        UserOperationObservationOutcome.Succeeded => "succeeded",
        UserOperationObservationOutcome.Diverged => "diverged",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome))
    };

    public static UserOperationObservationOutcome Outcome(string outcome) => outcome switch
    {
        "succeeded" => UserOperationObservationOutcome.Succeeded,
        "diverged" => UserOperationObservationOutcome.Diverged,
        _ => throw new InvalidOperationException("PostgreSQL returned an invalid observation outcome.")
    };
}
