namespace YO4X.Desktop;

internal sealed record DesktopBrokerAccountSnapshot(
    string BrokerAccountId,
    bool Connected,
    string MaskedLogin,
    string Server,
    string Company,
    string Currency,
    double Balance,
    double Equity,
    double Margin,
    double FreeMargin,
    double FloatingPnL,
    double? MarginLevel,
    long Leverage,
    bool TradingEnabled,
    DateTimeOffset ObservedAt,
    IReadOnlyList<DesktopOpenTradeSnapshot> OpenTrades);

internal sealed record DesktopOpenTradeSnapshot(
    long Ticket,
    string Symbol,
    string Side,
    double Volume,
    double OpenPrice,
    double? StopLoss,
    double? TakeProfit,
    double FloatingPnL,
    string OpenedAtBrokerTime,
    string Comment);
