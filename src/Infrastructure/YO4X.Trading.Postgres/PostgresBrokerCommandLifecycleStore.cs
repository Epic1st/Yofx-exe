using YO4X.Tenancy;
using YO4X.Trading.Abstractions;
using ApplicationDispatchClaim = YO4X.Trading.Application.BrokerCommandDispatchClaim;
using ApplicationLifecycleReceipt = YO4X.Trading.Application.BrokerCommandLifecycleReceipt;
using ApplicationReconciliationClaim = YO4X.Trading.Application.BrokerCommandReconciliationClaim;
using ApplicationReference = YO4X.Trading.Application.BrokerCommandReference;
using ApplicationValidatedReconciliation =
    YO4X.Trading.Application.ValidatedBrokerCommandReconciliation;
using DurableDispatchClaim = YO4X.Trading.Postgres.BrokerCommandDispatchClaim;
using DurableDispatchReference = YO4X.Trading.Postgres.BrokerCommandDispatchReference;
using DurableReconciliationClaim = YO4X.Trading.Postgres.BrokerCommandReconciliationClaim;

namespace YO4X.Trading.Postgres;

/// <summary>
/// Maps the database-agnostic application lifecycle port to the durable
/// PostgreSQL implementation. The inner store remains the only capability
/// hydrator; caller-provided records are references, never authorization.
/// </summary>
public sealed class PostgresBrokerCommandLifecycleStore(
    PostgresBrokerCommandStore store)
    : YO4X.Trading.Application.IBrokerCommandLifecycleStore
{
    private readonly PostgresBrokerCommandStore store = store
        ?? throw new ArgumentNullException(nameof(store));

    public async Task<ApplicationDispatchClaim> ClaimForDispatchAsync(
        TenantExecutionContext context,
        ApplicationReference reference,
        Guid claimToken,
        Guid auditEventId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        DurableDispatchClaim claim = await store.ClaimForDispatchAsync(
                context,
                new DurableDispatchReference(
                    reference.CommandId,
                    reference.AuthorizationSha256,
                    reference.ExecutionLeaseTokenSha256),
                claimToken,
                auditEventId,
                cancellationToken)
            .ConfigureAwait(false);
        return new ApplicationDispatchClaim(
            claim.Command,
            claim.ClaimToken,
            claim.ClaimExpiresAtUtc,
            claim.CommandVersion,
            claim.Replayed);
    }

    public async Task<ApplicationLifecycleReceipt> RecordSubmissionAsync(
        TenantExecutionContext context,
        ApplicationDispatchClaim claim,
        GatewaySendResult result,
        Guid auditEventId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        BrokerCommandMutationReceipt receipt = await store.RecordSubmissionAsync(
                context,
                new DurableDispatchClaim(
                    claim.Command,
                    claim.ClaimToken,
                    claim.ClaimExpiresAtUtc,
                    claim.CommandVersion,
                    claim.Replayed),
                result,
                auditEventId,
                cancellationToken)
            .ConfigureAwait(false);
        return Map(receipt);
    }

    public async Task<ApplicationLifecycleReceipt?> RecoverExpiredLifecycleAsync(
        TenantExecutionContext context,
        Guid commandId,
        string authorizationSha256,
        Guid auditEventId,
        CancellationToken cancellationToken = default)
    {
        BrokerCommandMutationReceipt? receipt = await store.RecoverExpiredLifecycleAsync(
                context,
                commandId,
                authorizationSha256,
                auditEventId,
                cancellationToken)
            .ConfigureAwait(false);
        return receipt is null ? null : Map(receipt);
    }

    public async Task<ApplicationReconciliationClaim> BeginReconciliationAsync(
        TenantExecutionContext context,
        Guid commandId,
        string authorizationSha256,
        Guid reconciliationClaimToken,
        Guid auditEventId,
        CancellationToken cancellationToken = default)
    {
        DurableReconciliationClaim claim = await store.BeginReconciliationAsync(
                context,
                commandId,
                authorizationSha256,
                reconciliationClaimToken,
                auditEventId,
                cancellationToken)
            .ConfigureAwait(false);
        return new ApplicationReconciliationClaim(
            claim.Command,
            claim.ClaimToken,
            claim.ScopeSha256,
            claim.QueryWindowStartUtc,
            claim.MustBeginByUtc,
            claim.MustCompleteByUtc,
            claim.ClaimExpiresAtUtc,
            claim.Attempt,
            claim.SendDisposition,
            claim.SendResultCode,
            claim.BrokerRequestId,
            claim.BrokerOrderId,
            claim.BrokerDealId,
            claim.CommandVersion,
            claim.StartedAtUtc,
            claim.Replayed);
    }

    public async Task<ApplicationLifecycleReceipt> CompleteReconciliationAsync(
        TenantExecutionContext context,
        Guid reconciliationClaimToken,
        Guid reconciliationId,
        ApplicationValidatedReconciliation evidence,
        Guid auditEventId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var document = new BrokerCommandReconciliationEvidenceDocument(
            evidence.CommandId,
            evidence.AuthorizationSha256,
            evidence.ScopeSha256,
            evidence.BrokerAccountId,
            evidence.DeploymentId,
            evidence.Generation,
            evidence.TargetKind,
            evidence.TargetBrokerId,
            evidence.OwnershipTag,
            evidence.SourceSequence,
            evidence.WindowStartUtc,
            evidence.WindowEndUtc,
            ToStorage(evidence.Match),
            evidence.ReasonCode,
            evidence.SourceEvidenceSha256,
            evidence.OrderId,
            evidence.DealId,
            evidence.ObservedAtUtc,
            evidence.Snapshot);
        BrokerCommandMutationReceipt receipt = await store.CompleteReconciliationAsync(
                context,
                evidence.AuthorizationSha256,
                reconciliationClaimToken,
                reconciliationId,
                document,
                auditEventId,
                cancellationToken)
            .ConfigureAwait(false);
        return Map(receipt);
    }

    private static ApplicationLifecycleReceipt Map(BrokerCommandMutationReceipt receipt) => new(
        receipt.CommandId,
        receipt.State,
        receipt.EvidenceSha256,
        receipt.CommandVersion,
        receipt.RecordedAtUtc,
        receipt.Replayed);

    private static string ToStorage(BrokerReconciliationMatch match) => match switch
    {
        BrokerReconciliationMatch.Inconclusive => "inconclusive",
        BrokerReconciliationMatch.Acknowledged => "acknowledged",
        BrokerReconciliationMatch.PartiallyFilled => "partially_filled",
        BrokerReconciliationMatch.Filled => "filled",
        BrokerReconciliationMatch.Cancelled => "cancelled",
        BrokerReconciliationMatch.Rejected => "rejected",
        BrokerReconciliationMatch.NotSent => "not_sent",
        _ => throw new ArgumentOutOfRangeException(nameof(match))
    };
}
