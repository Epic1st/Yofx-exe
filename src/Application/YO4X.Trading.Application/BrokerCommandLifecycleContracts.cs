using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using YO4X.Tenancy;
using YO4X.Trading.Abstractions;

namespace YO4X.Trading.Application;

public sealed record BrokerCommandReference
{
    private static readonly Regex LowerSha256 = new(
        "^[0-9a-f]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(100));

    public BrokerCommandReference(
        Guid commandId,
        string authorizationSha256,
        string executionLeaseTokenSha256)
    {
        if (commandId == Guid.Empty)
        {
            throw new ArgumentException("A command identifier is required.", nameof(commandId));
        }

        RequireDigest(authorizationSha256, nameof(authorizationSha256));
        RequireDigest(executionLeaseTokenSha256, nameof(executionLeaseTokenSha256));
        CommandId = commandId;
        AuthorizationSha256 = authorizationSha256;
        ExecutionLeaseTokenSha256 = executionLeaseTokenSha256;
    }

    public Guid CommandId { get; }

    public string AuthorizationSha256 { get; }

    public string ExecutionLeaseTokenSha256 { get; }

    internal static bool DigestEquals(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        if (left.Length != right.Length)
        {
            return false;
        }

        byte[] leftBytes = Encoding.ASCII.GetBytes(left);
        byte[] rightBytes = Encoding.ASCII.GetBytes(right);
        try
        {
            return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(leftBytes);
            CryptographicOperations.ZeroMemory(rightBytes);
        }
    }

    internal static bool IsDigest(string? value) =>
        value is not null && LowerSha256.IsMatch(value);

    private static void RequireDigest(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (!LowerSha256.IsMatch(value))
        {
            throw new ArgumentException(
                "A lowercase SHA-256 digest is required.",
                parameterName);
        }
    }
}

public sealed record BrokerCommandDispatchClaim(
    AuthorizedBrokerCommand Command,
    Guid ClaimToken,
    DateTimeOffset ClaimExpiresAtUtc,
    long CommandVersion,
    bool Replayed);

public sealed record BrokerCommandReconciliationClaim(
    AuthorizedBrokerCommand Command,
    Guid ClaimToken,
    string ScopeSha256,
    DateTimeOffset QueryWindowStartUtc,
    DateTimeOffset MustBeginByUtc,
    DateTimeOffset MustCompleteByUtc,
    DateTimeOffset ClaimExpiresAtUtc,
    int Attempt,
    string? SendDisposition,
    string? SendResultCode,
    string? BrokerRequestId,
    string? BrokerOrderId,
    string? BrokerDealId,
    long CommandVersion,
    DateTimeOffset StartedAtUtc,
    bool Replayed);

public sealed record BrokerCommandLifecycleReceipt(
    Guid CommandId,
    string State,
    string EvidenceSha256,
    long CommandVersion,
    DateTimeOffset RecordedAtUtc,
    bool Replayed);

public sealed record BrokerCommandReconciliationObservation(
    long? SourceSequence,
    string SourceEvidenceSha256,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    BrokerReconciliationSnapshot? Snapshot);

/// <summary>
/// Evidence can only be created by the application validator. Infrastructure
/// may persist it but cannot promote an arbitrary gateway assertion to a
/// terminal reconciliation result.
/// </summary>
public sealed record ValidatedBrokerCommandReconciliation
{
    internal ValidatedBrokerCommandReconciliation(
        Guid commandId,
        string authorizationSha256,
        string scopeSha256,
        Guid brokerAccountId,
        Guid deploymentId,
        long generation,
        BrokerCommandTargetKind? targetKind,
        string? targetBrokerId,
        string ownershipTag,
        long? sourceSequence,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        BrokerReconciliationMatch match,
        string reasonCode,
        string sourceEvidenceSha256,
        string? orderId,
        string? dealId,
        DateTimeOffset observedAtUtc,
        BrokerReconciliationSnapshot? snapshot)
    {
        CommandId = commandId;
        AuthorizationSha256 = authorizationSha256;
        ScopeSha256 = scopeSha256;
        BrokerAccountId = brokerAccountId;
        DeploymentId = deploymentId;
        Generation = generation;
        TargetKind = targetKind;
        TargetBrokerId = targetBrokerId;
        OwnershipTag = ownershipTag;
        SourceSequence = sourceSequence;
        WindowStartUtc = windowStartUtc;
        WindowEndUtc = windowEndUtc;
        Match = match;
        ReasonCode = reasonCode;
        SourceEvidenceSha256 = sourceEvidenceSha256;
        OrderId = orderId;
        DealId = dealId;
        ObservedAtUtc = observedAtUtc;
        Snapshot = snapshot;
    }

    public Guid CommandId { get; }

    public string AuthorizationSha256 { get; }

    public string ScopeSha256 { get; }

    public Guid BrokerAccountId { get; }

    public Guid DeploymentId { get; }

    public long Generation { get; }

    public BrokerCommandTargetKind? TargetKind { get; }

    public string? TargetBrokerId { get; }

    public string OwnershipTag { get; }

    public long? SourceSequence { get; }

    public DateTimeOffset WindowStartUtc { get; }

    public DateTimeOffset WindowEndUtc { get; }

    public BrokerReconciliationMatch Match { get; }

    public string ReasonCode { get; }

    public string SourceEvidenceSha256 { get; }

    public string? OrderId { get; }

    public string? DealId { get; }

    public DateTimeOffset ObservedAtUtc { get; }

    public BrokerReconciliationSnapshot? Snapshot { get; }

    public bool IsConclusive => Match != BrokerReconciliationMatch.Inconclusive;
}

public interface IBrokerCommandLifecycleStore
{
    Task<BrokerCommandDispatchClaim> ClaimForDispatchAsync(
        TenantExecutionContext context,
        BrokerCommandReference reference,
        Guid claimToken,
        Guid auditEventId,
        CancellationToken cancellationToken);

    Task<BrokerCommandLifecycleReceipt> RecordSubmissionAsync(
        TenantExecutionContext context,
        BrokerCommandDispatchClaim claim,
        GatewaySendResult result,
        Guid auditEventId,
        CancellationToken cancellationToken);

    Task<BrokerCommandLifecycleReceipt?> RecoverExpiredLifecycleAsync(
        TenantExecutionContext context,
        Guid commandId,
        string authorizationSha256,
        Guid auditEventId,
        CancellationToken cancellationToken);

    Task<BrokerCommandReconciliationClaim> BeginReconciliationAsync(
        TenantExecutionContext context,
        Guid commandId,
        string authorizationSha256,
        Guid reconciliationClaimToken,
        Guid auditEventId,
        CancellationToken cancellationToken);

    Task<BrokerCommandLifecycleReceipt> CompleteReconciliationAsync(
        TenantExecutionContext context,
        Guid reconciliationClaimToken,
        Guid reconciliationId,
        ValidatedBrokerCommandReconciliation evidence,
        Guid auditEventId,
        CancellationToken cancellationToken);
}

public interface IBrokerCommandIdentifierSource
{
    Guid NewId();
}

public sealed class UuidV7BrokerCommandIdentifierSource : IBrokerCommandIdentifierSource
{
    public Guid NewId() => Guid.CreateVersion7();
}
