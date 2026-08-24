using YO4X.Trading.Abstractions;

namespace YO4X.Trading.Application;

internal static class BrokerCommandLifecycleReceiptValidator
{
    public static bool IsExpiredLifecycleRecovery(
        BrokerCommandLifecycleReceipt? receipt,
        BrokerCommandReference reference) =>
        IsCommonReceipt(receipt, reference.CommandId)
        && receipt!.State == "unknown"
        && BrokerCommandReference.DigestEquals(
            receipt.EvidenceSha256,
            reference.AuthorizationSha256);

    public static bool IsSubmissionReceipt(
        BrokerCommandLifecycleReceipt? receipt,
        BrokerCommandDispatchClaim claim,
        GatewaySendResult result)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(result);

        try
        {
            string expectedState = result.Disposition switch
            {
                GatewayCommandDisposition.Accepted => "acknowledged",
                GatewayCommandDisposition.Unknown => "unknown",
                GatewayCommandDisposition.Rejected => "rejected",
                GatewayCommandDisposition.SubmissionDisabled => "submission_disabled",
                _ => string.Empty
            };
            return expectedState.Length != 0
                && HasExactNextVersion(receipt, claim.CommandVersion)
                && IsCommonReceipt(receipt, claim.Command.Command.CommandId)
                && receipt!.State == expectedState
                && receipt.RecordedAtUtc >= claim.AuthorityNowUtc
                && (result.Disposition != GatewayCommandDisposition.Accepted
                    || receipt.RecordedAtUtc <= claim.ClaimExpiresAtUtc)
                && BrokerCommandReference.DigestEquals(
                    receipt.EvidenceSha256,
                    BrokerCommandLifecycleEvidence.Submission(result).Sha256);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static bool IsReconciliationReceipt(
        BrokerCommandLifecycleReceipt? receipt,
        BrokerCommandReconciliationClaim claim,
        ValidatedBrokerCommandReconciliation evidence)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(evidence);

        try
        {
            string expectedState = evidence.IsConclusive ? "reconciled" : "unknown";
            return HasExactNextVersion(receipt, claim.CommandVersion)
                && IsCommonReceipt(receipt, claim.Command.Command.CommandId)
                && receipt!.State == expectedState
                && receipt.RecordedAtUtc >= claim.AuthorityNowUtc
                && receipt.RecordedAtUtc <= claim.ClaimExpiresAtUtc
                && receipt.RecordedAtUtc <= claim.MustCompleteByUtc
                && BrokerCommandReference.DigestEquals(
                    receipt.EvidenceSha256,
                    BrokerCommandLifecycleEvidence.Reconciliation(evidence).Sha256);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsCommonReceipt(
        BrokerCommandLifecycleReceipt? receipt,
        Guid expectedCommandId) =>
        receipt is not null
        && receipt.CommandId == expectedCommandId
        && receipt.CommandVersion > 0
        && receipt.RecordedAtUtc != default
        && receipt.RecordedAtUtc.Offset == TimeSpan.Zero
        && BrokerCommandReference.IsDigest(receipt.EvidenceSha256)
        && receipt.State is { Length: >= 1 and <= 64 }
        && receipt.State == receipt.State.Trim();

    private static bool HasExactNextVersion(
        BrokerCommandLifecycleReceipt? receipt,
        long currentVersion) =>
        receipt is not null
        && currentVersion is > 0 and < long.MaxValue
        && receipt.CommandVersion == currentVersion + 1;
}
