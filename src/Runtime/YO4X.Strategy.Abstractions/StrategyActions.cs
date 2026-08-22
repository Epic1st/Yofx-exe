using System.Text.Json.Serialization;

namespace YO4X.Strategy.Abstractions;

public enum RequestedActionKind
{
    PlaceOrder = 0,
    UpdateProtection = 1,
    CancelPendingOrder = 2,
    ClosePosition = 3
}

public enum RequestedExposureHint
{
    Increase = 0,
    Reduce = 1,
    Protect = 2,
    Cancel = 3,
    EmergencyClose = 4
}

public enum RequestedOrderSide
{
    Buy = 0,
    Sell = 1
}

public enum RequestedOrderType
{
    Market = 0,
    Limit = 1,
    Stop = 2,
    StopLimit = 3
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$action")]
[JsonDerivedType(typeof(PlaceOrderAction), "place-order-v1")]
[JsonDerivedType(typeof(UpdateProtectionAction), "update-protection-v1")]
[JsonDerivedType(typeof(CancelPendingOrderAction), "cancel-pending-order-v1")]
[JsonDerivedType(typeof(ClosePositionAction), "close-position-v1")]
public abstract record RequestedAction
{
    protected RequestedAction(
        Guid actionId,
        string idempotencyKey,
        string symbol,
        string reasonCode,
        long marketDataSequence,
        RequestedExposureHint exposureHint)
    {
        if (actionId == Guid.Empty)
        {
            throw new ArgumentException("Action identifier cannot be empty.", nameof(actionId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(marketDataSequence);

        ActionId = actionId;
        IdempotencyKey = idempotencyKey;
        Symbol = symbol;
        ReasonCode = reasonCode;
        MarketDataSequence = marketDataSequence;
        ExposureHint = exposureHint;
    }

    public abstract RequestedActionKind Kind { get; }

    public Guid ActionId { get; }

    public string IdempotencyKey { get; }

    public string Symbol { get; }

    public string ReasonCode { get; }

    public long MarketDataSequence { get; }

    public RequestedExposureHint ExposureHint { get; }
}

public sealed record PlaceOrderAction : RequestedAction
{
    public PlaceOrderAction(
        Guid actionId,
        string idempotencyKey,
        string symbol,
        string reasonCode,
        long marketDataSequence,
        RequestedExposureHint exposureHint,
        RequestedOrderSide side,
        RequestedOrderType orderType,
        decimal volume,
        decimal? requestedPrice,
        decimal stopLoss,
        decimal takeProfit,
        int maximumDeviationPoints,
        DateTimeOffset? expiresAtUtc = null)
        : base(actionId, idempotencyKey, symbol, reasonCode, marketDataSequence, exposureHint)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(volume);
        if (requestedPrice is { } normalizedRequestedPrice)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(normalizedRequestedPrice, nameof(requestedPrice));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stopLoss);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(takeProfit);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDeviationPoints);

        Side = side;
        OrderType = orderType;
        Volume = volume;
        RequestedPrice = requestedPrice;
        StopLoss = stopLoss;
        TakeProfit = takeProfit;
        MaximumDeviationPoints = maximumDeviationPoints;
        ExpiresAtUtc = expiresAtUtc?.ToUniversalTime();
    }

    public override RequestedActionKind Kind => RequestedActionKind.PlaceOrder;

    public RequestedOrderSide Side { get; }

    public RequestedOrderType OrderType { get; }

    public decimal Volume { get; }

    public decimal? RequestedPrice { get; }

    public decimal StopLoss { get; }

    public decimal TakeProfit { get; }

    public int MaximumDeviationPoints { get; }

    public DateTimeOffset? ExpiresAtUtc { get; }
}

public sealed record UpdateProtectionAction : RequestedAction
{
    public UpdateProtectionAction(
        Guid actionId,
        string idempotencyKey,
        string symbol,
        string reasonCode,
        long marketDataSequence,
        string positionId,
        decimal stopLoss,
        decimal takeProfit)
        : base(
            actionId,
            idempotencyKey,
            symbol,
            reasonCode,
            marketDataSequence,
            RequestedExposureHint.Protect)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(positionId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stopLoss);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(takeProfit);
        PositionId = positionId;
        StopLoss = stopLoss;
        TakeProfit = takeProfit;
    }

    public override RequestedActionKind Kind => RequestedActionKind.UpdateProtection;

    public string PositionId { get; }

    public decimal StopLoss { get; }

    public decimal TakeProfit { get; }
}

public sealed record CancelPendingOrderAction : RequestedAction
{
    public CancelPendingOrderAction(
        Guid actionId,
        string idempotencyKey,
        string symbol,
        string reasonCode,
        long marketDataSequence,
        string orderId)
        : base(
            actionId,
            idempotencyKey,
            symbol,
            reasonCode,
            marketDataSequence,
            RequestedExposureHint.Cancel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        OrderId = orderId;
    }

    public override RequestedActionKind Kind => RequestedActionKind.CancelPendingOrder;

    public string OrderId { get; }
}

public sealed record ClosePositionAction : RequestedAction
{
    public ClosePositionAction(
        Guid actionId,
        string idempotencyKey,
        string symbol,
        string reasonCode,
        long marketDataSequence,
        string positionId,
        decimal volume,
        RequestedExposureHint exposureHint = RequestedExposureHint.Reduce)
        : base(actionId, idempotencyKey, symbol, reasonCode, marketDataSequence, exposureHint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(positionId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(volume);
        if (exposureHint is not RequestedExposureHint.Reduce and not RequestedExposureHint.EmergencyClose)
        {
            throw new ArgumentOutOfRangeException(nameof(exposureHint));
        }

        PositionId = positionId;
        Volume = volume;
    }

    public override RequestedActionKind Kind => RequestedActionKind.ClosePosition;

    public string PositionId { get; }

    public decimal Volume { get; }
}
