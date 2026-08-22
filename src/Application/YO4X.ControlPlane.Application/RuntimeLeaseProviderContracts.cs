using YO4X.Runtime.Contracts;

namespace YO4X.ControlPlane.Application;

public sealed record ExecutionEntitlementRequest(
    Guid TenantId,
    Guid UserId,
    Guid DeploymentId,
    Guid BrokerAccountId,
    Guid StrategyId,
    Guid StrategyVersionId,
    int StrategyVersion,
    string StrategyPackageSha256,
    ExecutionMode ExecutionMode,
    DateTimeOffset RequestedAtUtc);

public sealed record ExecutionEntitlementGrant(
    Guid EntitlementId,
    DateTimeOffset NotBeforeUtc,
    DateTimeOffset ExpiresAtUtc,
    ExecutionLeaseActionPolicy ActionPolicy);

public interface IExecutionEntitlementProvider
{
    ValueTask<ExecutionEntitlementGrant?> ResolveAsync(
        ExecutionEntitlementRequest request,
        CancellationToken cancellationToken);
}

public sealed record ExecutionLeaseSignature(
    string Algorithm,
    string KeyId,
    string SignatureBase64Url);

public interface IExecutionLeaseSigningProvider
{
    ValueTask<ExecutionLeaseSignature> SignAsync(
        ReadOnlyMemory<byte> canonicalLeasePayload,
        CancellationToken cancellationToken);
}
