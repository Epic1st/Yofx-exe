using YO4X.Runtime.Contracts;

namespace YO4X.Trading.Abstractions;

public enum GatewayConnectionState
{
    Disconnected = 0,
    Resolving = 1,
    Connecting = 2,
    Authenticating = 3,
    Connected = 4,
    Degraded = 5,
    Reconnecting = 6,
    AuthenticationFailed = 7,
    Suspended = 8
}

public enum BrokerAccountMode
{
    Unknown = 0,
    Hedging = 1,
    Netting = 2,
    Exchange = 3
}

public enum BrokerEnvironment
{
    Unknown = 0,
    Demo = 1,
    Live = 2,
    Contest = 3,
    Archived = 4
}

public enum BrokerTradingAccess
{
    Unknown = 0,
    ReadOnly = 1,
    TradingAllowed = 2,
    TradingBlocked = 3
}

public enum BrokerOrderSide
{
    Buy = 0,
    Sell = 1
}

public enum BrokerOrderType
{
    Market = 0,
    Limit = 1,
    Stop = 2,
    StopLimit = 3
}

public enum BrokerCommandAction
{
    Place = 0,
    ModifyProtection = 1,
    Cancel = 2,
    Close = 3
}

public enum BrokerCommandTargetKind
{
    Position = 0,
    PendingOrder = 1
}

public sealed record BrokerServerIdentity(string BrokerCompany, string ServerName);

public sealed record GatewayConnectionRequest(
    int ContractVersion,
    Guid BrokerAccountId,
    BrokerServerIdentity Server,
    long Login,
    Guid CredentialHandleId,
    TimeSpan Timeout);

public sealed record GatewayCapabilities(
    int ContractVersion,
    string GatewayVersion,
    string GatewayArtifactHash,
    BrokerAccountMode AccountMode,
    BrokerEnvironment Environment,
    BrokerTradingAccess TradingAccess,
    bool SupportsPartialFills,
    bool SupportsBrokerHostedStopLoss,
    bool SupportsBrokerHostedTakeProfit,
    IReadOnlyList<string> Symbols);

public sealed record BrokerAccountSnapshot(
    long Sequence,
    string MaskedLogin,
    string BrokerCompany,
    string ServerName,
    BrokerAccountMode AccountMode,
    BrokerEnvironment Environment,
    BrokerTradingAccess TradingAccess,
    string Currency,
    decimal Balance,
    decimal Equity,
    decimal FreeMargin,
    DateTimeOffset ObservedAtUtc);

public sealed record BrokerQuoteSnapshot(
    long Sequence,
    string Symbol,
    decimal Bid,
    decimal Ask,
    DateTimeOffset BrokerTimestampUtc,
    DateTimeOffset ReceivedAtUtc);

public sealed record BrokerPositionSnapshot(
    string PositionId,
    string Symbol,
    BrokerOrderSide Side,
    decimal Volume,
    decimal OpenPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    string OwnershipTag,
    DateTimeOffset ObservedAtUtc);

public sealed record BrokerOrderSnapshot(
    string OrderId,
    string Symbol,
    BrokerOrderSide Side,
    BrokerOrderType OrderType,
    decimal RequestedVolume,
    decimal RemainingVolume,
    decimal? RequestedPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    string Status,
    string OwnershipTag,
    DateTimeOffset ObservedAtUtc);

public sealed record BrokerDealSnapshot(
    string DealId,
    string OrderId,
    string Symbol,
    BrokerOrderSide Side,
    decimal Volume,
    decimal Price,
    DateTimeOffset BrokerTimestampUtc);

public sealed record NormalizedBrokerCommand(
    int ContractVersion,
    Guid CommandId,
    Guid IntentId,
    Guid DeploymentId,
    long Generation,
    string IdempotencyKey,
    BrokerCommandAction Action,
    string Symbol,
    BrokerOrderSide Side,
    BrokerOrderType OrderType,
    decimal Volume,
    decimal? RequestedPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    int MaximumDeviationPoints,
    string OwnershipTag,
    BrokerCommandTargetKind? TargetKind,
    string? TargetBrokerId,
    decimal? ExpectedTargetVolume,
    string? ExpectedTargetStatus,
    decimal? ExpectedTargetStopLoss,
    decimal? ExpectedTargetTakeProfit,
    DateTimeOffset CreatedAtUtc);

public enum GatewayCommandDisposition
{
    Accepted = 0,
    Rejected = 1,
    Unknown = 2,
    SubmissionDisabled = 3
}

public sealed record GatewaySendResult(
    GatewayCommandDisposition Disposition,
    string Code,
    string? BrokerRequestId,
    string? OrderId,
    string? DealId,
    DateTimeOffset ObservedAtUtc,
    bool PreInvocationNotSentProven);

public enum BrokerReconciliationMatch
{
    Inconclusive = 0,
    Acknowledged = 1,
    PartiallyFilled = 2,
    Filled = 3,
    Cancelled = 4,
    Rejected = 5,
    NotSent = 6
}

public sealed record BrokerCommandReconciliation(
    Guid CommandId,
    BrokerReconciliationMatch Match,
    string ReasonCode,
    string? OrderId,
    string? DealId,
    DateTimeOffset ReconciledAtUtc);

public sealed record BrokerReconciliationSnapshot(
    int ContractVersion,
    long SourceSequence,
    Guid BrokerAccountId,
    Guid DeploymentId,
    long Generation,
    Guid GatewayArtifactId,
    string GatewayArtifactSha256,
    DateTimeOffset QueryWindowStartUtc,
    DateTimeOffset QueryWindowEndUtc,
    bool IsAtomicCut,
    bool IsComplete,
    BrokerAccountSnapshot Account,
    IReadOnlyList<BrokerPositionSnapshot> Positions,
    IReadOnlyList<BrokerOrderSnapshot> Orders,
    IReadOnlyList<BrokerDealSnapshot> Deals,
    IReadOnlyList<BrokerCommandReconciliation> CommandResults,
    DateTimeOffset CompletedAtUtc);

public sealed record GatewayOperationResult(bool IsSuccess, string Code)
{
    public static GatewayOperationResult Success(string code = "gateway_operation_succeeded") => new(true, code);

    public static GatewayOperationResult Failure(string code) => new(false, code);
}

public sealed record GatewayOperationResult<T>(bool IsSuccess, string Code, T? Value)
    where T : class;
