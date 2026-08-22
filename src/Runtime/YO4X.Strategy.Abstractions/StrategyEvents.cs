using System.Text.Json.Serialization;
using YO4X.Runtime.Contracts;

namespace YO4X.Strategy.Abstractions;

public enum StrategyEventKind
{
    Initialize = 0,
    NewTick = 1,
    BarClosed = 2,
    Timer = 3,
    Execution = 4,
    AccountChanged = 5,
    Stop = 6
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$event")]
[JsonDerivedType(typeof(InitializeEvent), "initialize-v1")]
[JsonDerivedType(typeof(NewTickEvent), "new-tick-v1")]
[JsonDerivedType(typeof(BarClosedEvent), "bar-closed-v1")]
[JsonDerivedType(typeof(TimerEvent), "timer-v1")]
[JsonDerivedType(typeof(ExecutionEvent), "execution-v1")]
[JsonDerivedType(typeof(AccountChangedEvent), "account-changed-v1")]
[JsonDerivedType(typeof(StopEvent), "stop-v1")]
public abstract record StrategyEvent
{
    protected StrategyEvent(DateTimeOffset occurredAtUtc)
    {
        ContractVersion = RuntimeContractVersions.StrategyEventV1;
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
    }

    public int ContractVersion { get; }

    public abstract StrategyEventKind Kind { get; }

    public DateTimeOffset OccurredAtUtc { get; }
}

public sealed record InitializeEvent : StrategyEvent
{
    public InitializeEvent(DateTimeOffset occurredAtUtc, string reasonCode)
        : base(occurredAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        ReasonCode = reasonCode;
    }

    public override StrategyEventKind Kind => StrategyEventKind.Initialize;

    public string ReasonCode { get; }
}

public sealed record NewTickEvent : StrategyEvent
{
    public NewTickEvent(
        DateTimeOffset occurredAtUtc,
        string symbol,
        decimal bid,
        decimal ask,
        long marketDataSequence)
        : base(occurredAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bid);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ask);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(marketDataSequence);
        if (ask < bid)
        {
            throw new ArgumentException("Ask cannot be lower than bid.", nameof(ask));
        }

        Symbol = symbol;
        Bid = bid;
        Ask = ask;
        MarketDataSequence = marketDataSequence;
    }

    public override StrategyEventKind Kind => StrategyEventKind.NewTick;

    public string Symbol { get; }

    public decimal Bid { get; }

    public decimal Ask { get; }

    public long MarketDataSequence { get; }
}

public sealed record BarClosedEvent : StrategyEvent
{
    public BarClosedEvent(
        DateTimeOffset occurredAtUtc,
        string symbol,
        TimeSpan timeframe,
        DateTimeOffset openedAtUtc,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        long tickVolume,
        long marketDataSequence)
        : base(occurredAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeframe, TimeSpan.Zero);

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(open);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(high);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(low);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(close);
        ArgumentOutOfRangeException.ThrowIfNegative(tickVolume);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(marketDataSequence);
        if (low > high || open < low || open > high || close < low || close > high)
        {
            throw new ArgumentException("OHLC values do not form a valid closed bar.", nameof(high));
        }

        Symbol = symbol;
        Timeframe = timeframe;
        OpenedAtUtc = openedAtUtc.ToUniversalTime();
        Open = open;
        High = high;
        Low = low;
        Close = close;
        TickVolume = tickVolume;
        MarketDataSequence = marketDataSequence;
    }

    public override StrategyEventKind Kind => StrategyEventKind.BarClosed;

    public string Symbol { get; }

    public TimeSpan Timeframe { get; }

    public DateTimeOffset OpenedAtUtc { get; }

    public decimal Open { get; }

    public decimal High { get; }

    public decimal Low { get; }

    public decimal Close { get; }

    public long TickVolume { get; }

    public long MarketDataSequence { get; }
}

public sealed record TimerEvent : StrategyEvent
{
    public TimerEvent(DateTimeOffset occurredAtUtc, string timerId, DateTimeOffset scheduledAtUtc)
        : base(occurredAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timerId);
        TimerId = timerId;
        ScheduledAtUtc = scheduledAtUtc.ToUniversalTime();
    }

    public override StrategyEventKind Kind => StrategyEventKind.Timer;

    public string TimerId { get; }

    public DateTimeOffset ScheduledAtUtc { get; }
}

public enum StrategyExecutionEventKind
{
    Acknowledged = 0,
    PartiallyFilled = 1,
    Filled = 2,
    Cancelled = 3,
    Rejected = 4,
    Reconciled = 5
}

public sealed record ExecutionEvent : StrategyEvent
{
    public ExecutionEvent(
        DateTimeOffset occurredAtUtc,
        Guid brokerCommandId,
        string brokerEventId,
        StrategyExecutionEventKind executionKind,
        string? orderId,
        string? dealId,
        decimal filledVolume,
        decimal? fillPrice,
        string reasonCode)
        : base(occurredAtUtc)
    {
        if (brokerCommandId == Guid.Empty)
        {
            throw new ArgumentException("Broker command identifier cannot be empty.", nameof(brokerCommandId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(brokerEventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        ArgumentOutOfRangeException.ThrowIfNegative(filledVolume);
        if (fillPrice is { } normalizedFillPrice)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(normalizedFillPrice, nameof(fillPrice));
        }

        BrokerCommandId = brokerCommandId;
        BrokerEventId = brokerEventId;
        ExecutionKind = executionKind;
        OrderId = orderId;
        DealId = dealId;
        FilledVolume = filledVolume;
        FillPrice = fillPrice;
        ReasonCode = reasonCode;
    }

    public override StrategyEventKind Kind => StrategyEventKind.Execution;

    public Guid BrokerCommandId { get; }

    public string BrokerEventId { get; }

    public StrategyExecutionEventKind ExecutionKind { get; }

    public string? OrderId { get; }

    public string? DealId { get; }

    public decimal FilledVolume { get; }

    public decimal? FillPrice { get; }

    public string ReasonCode { get; }
}

public sealed record AccountChangedEvent : StrategyEvent
{
    public AccountChangedEvent(DateTimeOffset occurredAtUtc, long accountSequence, string reasonCode)
        : base(occurredAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(accountSequence);
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        AccountSequence = accountSequence;
        ReasonCode = reasonCode;
    }

    public override StrategyEventKind Kind => StrategyEventKind.AccountChanged;

    public long AccountSequence { get; }

    public string ReasonCode { get; }
}

public enum StrategyStopReason
{
    Requested = 0,
    LeaseExpired = 1,
    Revoked = 2,
    Fenced = 3,
    Faulted = 4,
    Shutdown = 5
}

public sealed record StopEvent : StrategyEvent
{
    public StopEvent(DateTimeOffset occurredAtUtc, StrategyStopReason reason)
        : base(occurredAtUtc)
    {
        Reason = reason;
    }

    public override StrategyEventKind Kind => StrategyEventKind.Stop;

    public StrategyStopReason Reason { get; }
}
