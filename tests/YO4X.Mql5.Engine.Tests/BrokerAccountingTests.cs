using YO4X.Mql5.Engine.Hosting;
using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Tests;

/// <summary>Netting versus hedging, and the balance, equity and margin arithmetic.</summary>
public sealed class BrokerAccountingTests
{
    [Fact]
    public void HedgingModeHoldsTwoOpposingPositions()
    {
        Mql5RunOptions options = EngineTestSupport.Options(Mql5MarginMode.Hedging);
        Mql5SimulatedBroker broker = EngineTestSupport.BrokerAt(options, 1.10000);

        broker.Send(EngineTestSupport.Market(Mql5OrderType.Buy, 0.10), out _);
        broker.Send(EngineTestSupport.Market(Mql5OrderType.Sell, 0.10), out _);

        Assert.Equal(2, broker.Positions.Count);
        Assert.Equal(Mql5PositionType.Buy, broker.Positions[0].Type);
        Assert.Equal(Mql5PositionType.Sell, broker.Positions[1].Type);
        Assert.NotEqual(broker.Positions[0].Ticket, broker.Positions[1].Ticket);
        Assert.Empty(broker.ClosedTrades);

        // Both legs lock margin independently: 0.10 * 100000 * price / 100 each.
        Assert.Equal(220.01, broker.Margin, 2);
    }

    [Fact]
    public void NettingModeNetsTheOpposingDealAway()
    {
        Mql5RunOptions options = EngineTestSupport.Options(Mql5MarginMode.Netting);
        Mql5SimulatedBroker broker = EngineTestSupport.BrokerAt(options, 1.10000);

        broker.Send(EngineTestSupport.Market(Mql5OrderType.Buy, 0.10), out _);
        broker.Send(EngineTestSupport.Market(Mql5OrderType.Sell, 0.10), out _);

        Assert.Empty(broker.Positions);
        Mql5ClosedTrade trade = Assert.Single(broker.ClosedTrades);
        Assert.Equal(Mql5CloseReason.Netting, trade.Reason);

        // Bought the ask 1.10010, netted out at the bid 1.10000: the spread is the whole loss.
        Assert.Equal(-1.00, trade.GrossProfit, 2);
        Assert.Equal(9_999.00, broker.Balance, 2);
        Assert.Equal(0.0, broker.Margin, 2);
    }

    [Fact]
    public void NettingModeFlipsDirectionWhenTheOpposingDealIsLarger()
    {
        Mql5RunOptions options = EngineTestSupport.Options(Mql5MarginMode.Netting);
        Mql5SimulatedBroker broker = EngineTestSupport.BrokerAt(options, 1.10000);

        broker.Send(EngineTestSupport.Market(Mql5OrderType.Buy, 0.10), out _);
        broker.Send(EngineTestSupport.Market(Mql5OrderType.Sell, 0.30), out _);

        Mql5Position position = Assert.Single(broker.Positions);
        Assert.Equal(Mql5PositionType.Sell, position.Type);
        Assert.Equal(0.20, position.Volume, 6);
        Assert.Equal(1.10000, position.PriceOpen, 5);

        Mql5ClosedTrade trade = Assert.Single(broker.ClosedTrades);
        Assert.Equal(0.10, trade.Volume, 6);
        Assert.Equal(-1.00, trade.GrossProfit, 2);
    }

    [Fact]
    public void NettingModeAveragesTheOpenPriceWhenAddingToAPosition()
    {
        Mql5RunOptions options = EngineTestSupport.Options(Mql5MarginMode.Netting);
        Mql5SimulatedBroker broker = EngineTestSupport.BrokerAt(options, 1.10000);

        broker.Send(EngineTestSupport.Market(Mql5OrderType.Buy, 0.10), out _);   // ask 1.10010
        broker.ApplyBar(EngineTestSupport.Flat(1, 1.10100));
        broker.BeginTick();
        broker.Send(EngineTestSupport.Market(Mql5OrderType.Buy, 0.10), out _);   // ask 1.10110

        Mql5Position position = Assert.Single(broker.Positions);
        Assert.Equal(0.20, position.Volume, 6);
        Assert.Equal(1.10060, position.PriceOpen, 5);   // (1.10010 + 1.10110) / 2
        Assert.Empty(broker.ClosedTrades);
    }

    [Fact]
    public void HedgingModeClosesOnlyTheTicketItIsGiven()
    {
        Mql5RunOptions options = EngineTestSupport.Options(Mql5MarginMode.Hedging);
        Mql5SimulatedBroker broker = EngineTestSupport.BrokerAt(options, 1.10000);

        broker.Send(EngineTestSupport.Market(Mql5OrderType.Buy, 0.10), out Mql5TradeResult first);
        broker.Send(EngineTestSupport.Market(Mql5OrderType.Buy, 0.20), out Mql5TradeResult second);

        Assert.True(broker.Send(EngineTestSupport.ClosePosition(first.Position), out _));

        Mql5Position remaining = Assert.Single(broker.Positions);
        Assert.Equal(second.Position, remaining.Ticket);
        Assert.Equal(0.20, remaining.Volume, 6);
    }

    [Fact]
    public void BalanceEquityAndMarginTrackARoundTrip()
    {
        Mql5RunOptions options = EngineTestSupport.Options(deposit: 10_000.0, leverage: 100);
        Mql5SimulatedBroker broker = EngineTestSupport.BrokerAt(options, 1.10000);

        Assert.Equal(10_000.00, broker.Balance, 2);
        Assert.Equal(10_000.00, broker.Equity, 2);
        Assert.Equal(0.00, broker.Margin, 2);
        Assert.Equal(10_000.00, broker.FreeMargin, 2);

        broker.Send(EngineTestSupport.Market(Mql5OrderType.Buy, 0.10), out Mql5TradeResult opened);
        Assert.Equal(1.10010, opened.Price, 5);

        // Margin = 0.10 * 100000 * 1.10010 / 100 = 110.01
        Assert.Equal(110.01, broker.Margin, 2);

        // Still marked at the bid, so the position is down the spread: -0.00010 * 100000 * 0.10.
        Assert.Equal(10_000.00, broker.Balance, 2);
        Assert.Equal(-1.00, broker.FloatingProfit, 2);
        Assert.Equal(9_999.00, broker.Equity, 2);
        Assert.Equal(9_888.99, broker.FreeMargin, 2);

        // Ninety points in our favour: (1.10100 - 1.10010) * 100000 * 0.10 = 9.00
        broker.ApplyBar(EngineTestSupport.Flat(1, 1.10100));
        broker.BeginTick();
        Assert.Equal(9.00, broker.FloatingProfit, 2);
        Assert.Equal(10_009.00, broker.Equity, 2);
        Assert.Equal(110.01, broker.Margin, 2);      // margin is fixed at the open price
        Assert.Equal(9_898.99, broker.FreeMargin, 2);

        Assert.True(broker.Send(EngineTestSupport.ClosePosition(opened.Position), out Mql5TradeResult closed));
        Assert.Equal(Mql5TradeRetcode.Done, closed.Retcode);

        Assert.Equal(10_009.00, broker.Balance, 2);
        Assert.Equal(10_009.00, broker.Equity, 2);
        Assert.Equal(0.00, broker.Margin, 2);
        Assert.Equal(10_009.00, broker.FreeMargin, 2);
        Assert.Equal(0.00, broker.FloatingProfit, 2);
    }

    [Fact]
    public void MarginLevelIsEquityOverMarginInPercent()
    {
        Mql5RunOptions options = EngineTestSupport.Options(deposit: 10_000.0, leverage: 100);
        Mql5SimulatedBroker broker = EngineTestSupport.BrokerAt(options, 1.10000);

        broker.Send(EngineTestSupport.Market(Mql5OrderType.Buy, 0.10), out _);

        Assert.Equal(Math.Round(9_999.00 / 110.01 * 100.0, 2), broker.MarginLevel, 2);
    }

    [Fact]
    public void CommissionIsChargedPerLotAndRealizedOnClose()
    {
        Mql5RunOptions options = EngineTestSupport.Options(commissionPerLot: 7.0);
        Mql5SimulatedBroker broker = EngineTestSupport.BrokerAt(options, 1.10000);

        broker.Send(EngineTestSupport.Market(Mql5OrderType.Buy, 0.10), out Mql5TradeResult opened);

        // 7.00 per lot on 0.10 lots.
        Assert.Equal(-0.70, broker.Positions[0].Commission, 2);
        Assert.Equal(10_000.00, broker.Balance, 2);
        Assert.Equal(9_998.30, broker.Equity, 2);   // 10000 - 1.00 floating - 0.70 commission

        broker.ApplyBar(EngineTestSupport.Flat(1, 1.10100));
        broker.BeginTick();
        broker.Send(EngineTestSupport.ClosePosition(opened.Position), out _);

        Mql5ClosedTrade trade = Assert.Single(broker.ClosedTrades);
        Assert.Equal(9.00, trade.GrossProfit, 2);
        Assert.Equal(-0.70, trade.Commission, 2);
        Assert.Equal(8.30, trade.NetProfit, 2);
        Assert.Equal(10_008.30, broker.Balance, 2);
    }

    [Fact]
    public void InsufficientFreeMarginIsRejectedRatherThanThrown()
    {
        Mql5RunOptions options = EngineTestSupport.Options(deposit: 100.0, leverage: 100);
        Mql5SimulatedBroker broker = EngineTestSupport.BrokerAt(options, 1.10000);

        // One lot needs 1100.10 of margin against a 100.00 deposit.
        Assert.False(broker.Send(EngineTestSupport.Market(Mql5OrderType.Buy, 1.00), out Mql5TradeResult result));
        Assert.Equal(Mql5TradeRetcode.NoMoney, result.Retcode);
        Assert.Empty(broker.Positions);
    }

    [Fact]
    public void StopOutForceClosesThePositionWhenTheMarginLevelCollapses()
    {
        Mql5RunOptions options = EngineTestSupport.Options(deposit: 1_000.0, leverage: 500, stopOutPercent: 50.0);
        Mql5SimulatedBroker broker = EngineTestSupport.BrokerAt(options, 1.10000);

        Assert.True(broker.Send(EngineTestSupport.Market(Mql5OrderType.Buy, 1.00), out _));
        Assert.Equal(220.02, broker.Margin, 2);

        broker.ApplyBar(EngineTestSupport.Flat(1, 1.09000));

        Assert.True(broker.StopOutTriggered);
        Assert.Empty(broker.Positions);
        Mql5ClosedTrade trade = Assert.Single(broker.ClosedTrades);
        Assert.Equal(Mql5CloseReason.StopOut, trade.Reason);
    }

    [Fact]
    public void InvalidVolumesReceiveMql5RetcodesInsteadOfExceptions()
    {
        Mql5RunOptions options = EngineTestSupport.Options();
        Mql5SimulatedBroker broker = EngineTestSupport.BrokerAt(options, 1.10000);

        Assert.False(broker.Send(EngineTestSupport.Market(Mql5OrderType.Buy, 0.0), out Mql5TradeResult zero));
        Assert.Equal(Mql5TradeRetcode.InvalidVolume, zero.Retcode);

        Assert.False(broker.Send(EngineTestSupport.Market(Mql5OrderType.Buy, -1.0), out Mql5TradeResult negative));
        Assert.Equal(Mql5TradeRetcode.InvalidVolume, negative.Retcode);

        Assert.False(broker.Send(EngineTestSupport.Market(Mql5OrderType.Buy, 0.005), out Mql5TradeResult tooSmall));
        Assert.Equal(Mql5TradeRetcode.InvalidVolume, tooSmall.Retcode);

        Assert.False(broker.Send(EngineTestSupport.Market(Mql5OrderType.Buy, 5_000.0), out Mql5TradeResult tooBig));
        Assert.Equal(Mql5TradeRetcode.InvalidVolume, tooBig.Retcode);

        Assert.False(broker.Send(EngineTestSupport.Market(Mql5OrderType.Buy, 0.015), out Mql5TradeResult offStep));
        Assert.Equal(Mql5TradeRetcode.InvalidVolume, offStep.Retcode);

        Assert.False(broker.Send(EngineTestSupport.Market(Mql5OrderType.Buy, double.NaN), out Mql5TradeResult notANumber));
        Assert.Equal(Mql5TradeRetcode.InvalidVolume, notANumber.Retcode);

        Assert.Empty(broker.Positions);
    }

    [Fact]
    public void StopsOnTheWrongSideOfTheFillAreRejected()
    {
        Mql5RunOptions options = EngineTestSupport.Options();
        Mql5SimulatedBroker broker = EngineTestSupport.BrokerAt(options, 1.10000);

        Assert.False(broker.Send(
            EngineTestSupport.Market(Mql5OrderType.Buy, 0.10, sl: 1.10500),
            out Mql5TradeResult stopAbove));
        Assert.Equal(Mql5TradeRetcode.InvalidStops, stopAbove.Retcode);

        Assert.False(broker.Send(
            EngineTestSupport.Market(Mql5OrderType.Buy, 0.10, tp: 1.09500),
            out Mql5TradeResult targetBelow));
        Assert.Equal(Mql5TradeRetcode.InvalidStops, targetBelow.Retcode);

        Assert.Empty(broker.Positions);
    }

    [Fact]
    public void UnknownSymbolsAndMissingQuotesAreRejected()
    {
        Mql5RunOptions options = EngineTestSupport.Options();

        var cold = new Mql5SimulatedBroker(options);
        Assert.False(cold.Send(EngineTestSupport.Market(Mql5OrderType.Buy, 0.10), out Mql5TradeResult noQuote));
        Assert.Equal(Mql5TradeRetcode.PriceOff, noQuote.Retcode);

        Mql5SimulatedBroker broker = EngineTestSupport.BrokerAt(options, 1.10000);
        Mql5TradeRequest wrongSymbol = EngineTestSupport.Market(Mql5OrderType.Buy, 0.10);
        wrongSymbol.Symbol = "GBPUSD";
        Assert.False(broker.Send(wrongSymbol, out Mql5TradeResult result));
        Assert.Equal(Mql5TradeRetcode.Invalid, result.Retcode);
    }

    [Fact]
    public void ClosingAnUnknownTicketReportsPositionClosed()
    {
        Mql5RunOptions options = EngineTestSupport.Options();
        Mql5SimulatedBroker broker = EngineTestSupport.BrokerAt(options, 1.10000);

        Assert.False(broker.Send(EngineTestSupport.ClosePosition(4242), out Mql5TradeResult result));
        Assert.Equal(Mql5TradeRetcode.PositionClosed, result.Retcode);
    }

    [Fact]
    public void PerTickOrderCapRejectsFurtherRequestsAndIsReported()
    {
        Mql5RunOptions options = EngineTestSupport.Options(Mql5MarginMode.Hedging, maxOrdersPerTick: 3);
        Mql5SimulatedBroker broker = EngineTestSupport.BrokerAt(options, 1.10000);

        for (int index = 0; index < 3; index++)
        {
            Assert.True(broker.Send(EngineTestSupport.Market(Mql5OrderType.Buy, 0.01), out _));
        }

        Assert.False(broker.Send(EngineTestSupport.Market(Mql5OrderType.Buy, 0.01), out Mql5TradeResult capped));
        Assert.Equal(Mql5TradeRetcode.LimitOrders, capped.Retcode);
        Assert.True(broker.OrdersPerTickCapTriggered);
        Assert.Contains(broker.Journal, e => e.Kind == Mql5OrderEventKind.OrdersPerTickCapReached);

        // The budget resets on the next tick.
        broker.BeginTick();
        Assert.True(broker.Send(EngineTestSupport.Market(Mql5OrderType.Buy, 0.01), out _));
    }
}
