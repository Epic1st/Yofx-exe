using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace YO4X.Runtime.Contracts;

[Flags]
public enum LeaseActionClass
{
    None = 0,
    Increase = 1 << 0,
    Reduce = 1 << 1,
    Protect = 1 << 2,
    Cancel = 1 << 3,
    EmergencyClose = 1 << 4
}

public enum ExecutionMode
{
    CloudDemo = 0,
    CloudLive = 1,
    Local = 2
}

public sealed record ExecutionLeaseActionPolicy(
    LeaseActionClass Active,
    LeaseActionClass Grace,
    LeaseActionClass Expired,
    LeaseActionClass Revoked);

public sealed record ExecutionLeaseBinding(
    Guid TenantId,
    Guid EntitlementId,
    Guid UserId,
    Guid DeploymentId,
    Guid BrokerAccountId,
    string BrokerAccountBindingSha256,
    Guid StrategyId,
    Guid StrategyVersionId,
    int StrategyVersion,
    string StrategyPackageSha256,
    ExecutionMode ExecutionMode,
    Guid SafetyPolicyVersionId,
    string SafetyPolicySha256,
    Guid WorkerAssignmentId,
    Guid WorkerInstanceId,
    Guid SupervisorWorkloadId,
    Guid StrategyHostWorkloadId,
    Guid GatewayHostWorkloadId,
    long Generation,
    string Region);

public sealed record ExecutionLeaseClaims(
    int ContractVersion,
    Guid LeaseId,
    ExecutionLeaseBinding Binding,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset NotBeforeUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset GraceExpiresAtUtc,
    ExecutionLeaseActionPolicy ActionPolicy);

public sealed record SignedExecutionLease(
    ExecutionLeaseClaims Claims,
    string PayloadSha256,
    string SignatureAlgorithm,
    string SigningKeyId,
    string SignatureBase64Url)
{
    public override string ToString() =>
        $"SignedExecutionLease {{ LeaseId = {Claims.LeaseId}, Signature = [REDACTED] }}";
}

public static class ExecutionLeaseCanonicalizer
{
    public static byte[] Serialize(ExecutionLeaseClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(claims.Binding);
        ArgumentNullException.ThrowIfNull(claims.ActionPolicy);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            ExecutionLeaseBinding binding = claims.Binding;
            writer.WriteStartObject();
            writer.WriteNumber("contractVersion", claims.ContractVersion);
            WriteGuid(writer, "leaseId", claims.LeaseId);
            writer.WriteStartObject("binding");
            WriteGuid(writer, "tenantId", binding.TenantId);
            WriteGuid(writer, "entitlementId", binding.EntitlementId);
            WriteGuid(writer, "userId", binding.UserId);
            WriteGuid(writer, "deploymentId", binding.DeploymentId);
            WriteGuid(writer, "brokerAccountId", binding.BrokerAccountId);
            writer.WriteString("brokerAccountBindingSha256", binding.BrokerAccountBindingSha256);
            WriteGuid(writer, "strategyId", binding.StrategyId);
            WriteGuid(writer, "strategyVersionId", binding.StrategyVersionId);
            writer.WriteNumber("strategyVersion", binding.StrategyVersion);
            writer.WriteString("strategyPackageSha256", binding.StrategyPackageSha256);
            writer.WriteNumber("executionMode", (int)binding.ExecutionMode);
            WriteGuid(writer, "safetyPolicyVersionId", binding.SafetyPolicyVersionId);
            writer.WriteString("safetyPolicySha256", binding.SafetyPolicySha256);
            WriteGuid(writer, "workerAssignmentId", binding.WorkerAssignmentId);
            WriteGuid(writer, "workerInstanceId", binding.WorkerInstanceId);
            WriteGuid(writer, "supervisorWorkloadId", binding.SupervisorWorkloadId);
            WriteGuid(writer, "strategyHostWorkloadId", binding.StrategyHostWorkloadId);
            WriteGuid(writer, "gatewayHostWorkloadId", binding.GatewayHostWorkloadId);
            writer.WriteNumber("generation", binding.Generation);
            writer.WriteString("region", binding.Region);
            writer.WriteEndObject();
            WriteTimestamp(writer, "issuedAtUtc", claims.IssuedAtUtc);
            WriteTimestamp(writer, "notBeforeUtc", claims.NotBeforeUtc);
            WriteTimestamp(writer, "expiresAtUtc", claims.ExpiresAtUtc);
            WriteTimestamp(writer, "graceExpiresAtUtc", claims.GraceExpiresAtUtc);
            writer.WriteStartObject("actionPolicy");
            writer.WriteNumber("active", (int)claims.ActionPolicy.Active);
            writer.WriteNumber("grace", (int)claims.ActionPolicy.Grace);
            writer.WriteNumber("expired", (int)claims.ActionPolicy.Expired);
            writer.WriteNumber("revoked", (int)claims.ActionPolicy.Revoked);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    public static string Sha256(ExecutionLeaseClaims claims)
    {
        byte[] payload = Serialize(claims);
        try
        {
            return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private static void WriteGuid(Utf8JsonWriter writer, string propertyName, Guid value) =>
        writer.WriteString(propertyName, value.ToString("D", CultureInfo.InvariantCulture));

    private static void WriteTimestamp(
        Utf8JsonWriter writer,
        string propertyName,
        DateTimeOffset value) =>
        writer.WriteString(propertyName, value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
}

public static class ExecutionLeaseEnvelopeDigest
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static string Sha256(SignedExecutionLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        JsonNode? node = JsonSerializer.SerializeToNode(lease, SerializerOptions);
        JsonNode? normalized = Normalize(node);
        byte[] content = Encoding.UTF8.GetBytes(
            normalized?.ToJsonString(SerializerOptions) ?? "null");
        try
        {
            return Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(content);
        }
    }

    public static string SignatureSha256(SignedExecutionLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentException.ThrowIfNullOrWhiteSpace(lease.SignatureBase64Url);

        byte[] signatureText = Encoding.ASCII.GetBytes(lease.SignatureBase64Url);
        try
        {
            return Convert.ToHexString(SHA256.HashData(signatureText)).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signatureText);
        }
    }

    private static JsonNode? Normalize(JsonNode? node) => node switch
    {
        JsonObject value => NormalizeObject(value),
        JsonArray value => NormalizeArray(value),
        _ => node?.DeepClone()
    };

    private static JsonObject NormalizeObject(JsonObject value)
    {
        var normalized = new JsonObject();
        foreach ((string name, JsonNode? child) in value.OrderBy(
            property => property.Key,
            StringComparer.Ordinal))
        {
            normalized.Add(name, Normalize(child));
        }

        return normalized;
    }

    private static JsonArray NormalizeArray(JsonArray value)
    {
        var normalized = new JsonArray();
        foreach (JsonNode? child in value)
        {
            normalized.Add(Normalize(child));
        }

        return normalized;
    }
}

public enum ExecutionLeaseValidationCode
{
    Valid = 0,
    InvalidSignature = 1,
    UnsupportedVersion = 2,
    InvalidIdentity = 3,
    WrongDeployment = 4,
    WrongWorker = 5,
    WrongGeneration = 6,
    NotYetValid = 7,
    Expired = 8,
    ActionNotPermitted = 9,
    OwnershipNotHeld = 10,
    WrongBinding = 11
}

public sealed record ExecutionLeaseValidation(
    ExecutionLeaseValidationCode Code,
    string ReasonCode)
{
    public bool IsValid => Code == ExecutionLeaseValidationCode.Valid;
}
