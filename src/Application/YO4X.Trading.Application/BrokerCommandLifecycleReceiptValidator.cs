using YO4X.Trading.Abstractions;

namespace YO4X.Trading.Application;

internal static class BrokerCommandLifecycleReceiptValidator
{
    public static bool IsExpiredLifecycleRecovery(
        BrokerCommandLifecycleReceipt? receipt,
        BrokerCommandReference reference) =>
        IsCommonReceipt(receipt, reference.CommandId, minimumVersion: 1)
        && receipt!.State == "unknown";

    public static bool IsSubmissionReceipt(
        BrokerCommandLifecycleReceipt? receipt,
        BrokerCommandDispatchClaim claim,
        GatewaySendResult result)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(result);

        string expectedState = result.Disposition switch
        {
            GatewayCommandDisposition.Accepted => "acknowledged",
            GatewayCommandDisposition.Unknown => "unknown",
            GatewayCommandDisposition.Rejected or GatewayCommandDisposition.SubmissionDisabled =>
                "rejected",
            _ => string.Empty
        };
        return expectedState.Length != 0
            && IsCommonReceipt(
                receipt,
                claim.Command.Command.CommandId,
                claim.CommandVersion)
            && receipt!.State == expectedState;
    }

    public static bool IsReconciliationReceipt(
        BrokerCommandLifecycleReceipt? receipt,
        BrokerCommandReconciliationClaim claim,
        ValidatedBrokerCommandReconciliation evidence)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(evidence);

        string expectedState = evidence.IsConclusive ? "reconciled" : "unknown";
        return IsCommonReceipt(
                receipt,
                claim.Command.Command.CommandId,
                claim.CommandVersion)
            && receipt!.State == expectedState
            && receipt.RecordedAtUtc <= claim.ClaimExpiresAtUtc
            && receipt.RecordedAtUtc <= claim.MustCompleteByUtc;
    }

    private static bool IsCommonReceipt(
        BrokerCommandLifecycleReceipt? receipt,
        Guid expectedCommandId,
        long minimumVersion) =>
        receipt is not null
        && receipt.CommandId == expectedCommandId
        && receipt.CommandVersion >= minimumVersion
        && receipt.CommandVersion > 0
        && receipt.RecordedAtUtc != default
        && receipt.RecordedAtUtc.Offset == TimeSpan.Zero
        && BrokerCommandReference.IsDigest(receipt.EvidenceSha256)
        && receipt.State is { Length: >= 1 and <= 64 }
        && receipt.State == receipt.State.Trim();
}
