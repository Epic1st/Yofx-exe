using YO4X.Mql5.Live;
using YO4X.Mt5.ConnectionProbe.Windows;

namespace YO4X.Desktop.Tests;

public sealed class LiveBrokerRecoveryTests
{
    [Fact]
    public void StartupAdoptsPositionsAndPendingOrdersForResolvedRunSymbolOnly()
    {
        var gateway = new RecoveryGateway([
            new Mt5OpenOrder(11, "XAUUSDm", "Buy", 0.01, 4400, 4300, 4500, 2.5, DateTime.UtcNow, "live"),
            new Mt5OpenOrder(12, "XAUUSDm", "ORDER_TYPE_SELL_STOP", 0.02, 4200, 4250, 4100, 0, DateTime.UtcNow, "pending"),
            new Mt5OpenOrder(13, "BTCUSDm", "Buy", 0.01, 100000, 0, 0, 5, DateTime.UtcNow, "other bot"),
        ]);
        var series = new LiveBarSeries("XAUUSDm", 1, []);
        var journal = new List<string>();

        var context = new LiveBrokerContext(series, gateway, 2, journal.Add);

        Assert.Equal(1, context.PositionsTotal());
        Assert.Equal<ulong>(11, context.PositionGetTicket(0));
        Assert.Equal(1, context.OrdersTotal());
        Assert.Equal<ulong>(12, context.OrderGetTicket(0));
        Assert.Contains(journal, line => line.Contains("1 position(s), 1 pending order(s)", StringComparison.Ordinal));
    }

    private sealed class RecoveryGateway(IReadOnlyList<Mt5OpenOrder> orders) : IMt5TradeGateway
    {
        public string Symbol => "XAUUSDm";
        public Action<DateTime, double, double>? QuoteObserver { get; set; }
        public IReadOnlyList<Mt5OpenOrder> ReadOpenOrders() => orders;
        public Mt5LiveSymbolSnapshot? ReadSymbolSnapshot() => null;
        public Mt5LiveAccountSnapshot ReadAccountSnapshot() => new(
            89, "Broker", "USD", "Demo", 1000, 1000, 0, 1000, 0, 100,
            Mt5TradingEnvironment.Demo, Mt5AccountMarginMode.RetailHedging, true);
        public Task<Mt5DemoOrderReceipt> SendAsync(Mt5DemoSide side, double volume, double price,
            double stopLoss, double takeProfit, string comment, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Mt5ExecutionLatency> ModifyAsync(Mt5DemoOrderReceipt receipt, double stopLoss,
            double takeProfit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Mt5DemoOrderReceipt> CloseAsync(Mt5DemoOrderReceipt receipt,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Mt5ExecutionLatency> CancelAsync(Mt5DemoOrderReceipt receipt,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
