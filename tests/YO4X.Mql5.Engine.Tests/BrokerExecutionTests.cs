using YO4X.Mql5.Engine.Hosting;
using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Tests;

/// <summary>Fill pricing, stops, targets and pending order activation.</summary>
public sealed class BrokerExecutionTests
{
    [Fact]
    public void MarketBuyFillsAtTheAskAndMarketSellAtTheBid()
    {
        Mql5RunOptions options = EngineTestSupport.Options(Mql5MarginMode.Hedging, spreadPoints: 10);
        Mql5SimulatedBroker broker = EngineTestSupport.BrokerAt(options, 1.10000);

        Assert.Equal(1.10000, broker.Bid, 5);
        Assert.Equal(1.10010, broker.Ask, 5);
        Assert.Equal(0.00010, broker.Ask - broker.Bid, 8);   // ten points of spread

        Assert.True(broker.Send(EngineTestSupport.Market(Mql5OrderType.Buy, 0.10), out Mql5TradeResult buy));
        Assert.Equal(Mql5TradeRetcode.Done, buy.Retcode);
        Assert.Equal(1.10010, buy.Price, 5);

        Assert.True(broker.Send(EngineTestSupport.Market(Mql5OrderType.Sell, 0.10), out Mql5TradeResult sell));
        Assert.Equal(Mql5TradeRetcode.Done, sell.Retcode);
        Assert.Equal(1.10000, sell.Price, 5);

        Assert.Equal(2, broker.Positions.Count);
        Assert.Equal(1.10010, broker.Positions[0].PriceOpen, 5);
        Assert.Equal(1.10000, broker.Positions[1].PriceOpen, 5);
    }

    [Fact]
    public void SpreadOnTheBarOverridesTheRunDefault()
    {
        Mql5RunOptions options = EngineTestSupport.Options(spreadPoints: 10);
        var broker = new Mql5SimulatedBroker(options);
        broker.ApplyBar(EngineTestSupport.Flat(0, 1.10000, spread: 30));

        Assert.Equal(30, broker.SpreadPoints);
        Assert.Equal(1.10030, broker.Ask, 5);
    }

    [Fact]
    public void SlippageIsAlwaysAdverse()
    {
        Mql5RunOptions options = EngineTestSupport.Options(Mql5MarginMode.Hedging, spreadPoints: 10, slippagePoints: 5);
        Mql5SimulatedBroker broker = EngineTestSupport.BrokerAt(options, 1.10000);

        broker.Send(EngineTestSupport.Market(Mql5OrderType.Buy, 0.10), out Mql5TradeResult buy);
        broker.Send(EngineTestSupport.Market(Mql5OrderType.Sell, 0.10), out Mql5TradeResult sell);

        Assert.Equal(1.10015, buy.Price, 5);    // ask 1.10010 plus five points
        Assert.Equal(1.09995, sell.Price, 5);   // bid 1.10000 minus five points
    }

    [Fact]
    public void TakeProfitClosesTheLongAtTheTargetPrice()
    {
        Mql5RunOptions options = EngineTestSupport.Options();
        Mql5SimulatedBroker broker = EngineTestSupport.BrokerAt(options, 1.10000);

        Assert.True(broker.Send(
            EngineTestSupport.Market(Mql5OrderType.Buy, 0.10, sl: 1.09900, tp: 1.10200),
            out _));

        // Bullish bar walked open, low, high, close: the high reaches through the target.
        broker.ApplyBar(EngineTestSupport.Bar(1, 1.10000, 1.10250, 1.09950, 1.10200));

        Assert.Empty(broker.Positions);
        Mql5ClosedTrade trade = Assert.Single(broker.ClosedTrades);
        Assert.Equal(Mql5CloseReason.TakeProfit, trade.Reason);
        Assert.Equal(1.10200, trade.PriceClose, 5);

        // (1.10200 - 1.10010) * 100000 * 0.10 = 19.00
        Assert.Equal(19.00, trade.GrossProfit, 2);
        Assert.Equal(10_019.00, broker.Balance, 2);
    }

    [Fact]
    public void StopLossClosesTheLongAtTheStopPrice()
    {
        Mql5RunOptions options = EngineTestSupport.Options();
        Mql5SimulatedBroker broker = EngineTestSupport.BrokerAt(options, 1.10000);

        Assert.True(broker.Send(
            EngineTestSupport.Market(Mql5OrderType.Buy, 0.10, sl: 1.09900, tp: 1.10500),
            out _));

        // Bearish bar walked open, high, low, close: the low reaches through the stop.
        broker.ApplyBar(EngineTestSupport.Bar(1, 1.10000, 1.10050, 1.09850, 1.09900));

        Assert.Empty(broker.Positions);
        Mql5ClosedTrade trade = Assert.Single(broker.ClosedTrades);
        Assert.Equal(Mql5CloseReason.StopLoss, trade.Reason);
        Assert.Equal(1.09900, trade.PriceClose, 5);

        // (1.09900 - 1.10010) * 100000 * 0.10 = -11.00
        Assert.Equal(-11.00, trade.GrossProfit, 2);
        Assert.Equal(9_989.00, broker.Balance, 2);
    }

    [Fact]
    public void ShortStopsAreEvaluatedAgainstTheAsk()
    {
        Mql5RunOptions options = EngineTestSupport.Options();
        Mql5SimulatedBroker broker = EngineTestSupport.BrokerAt(options, 1.10000);

        // Sold at the bid of 1.10000 with a stop 100 points above.
        Assert.True(broker.Send(EngineTestSupport.Market(Mql5OrderType.Sell, 0.10, sl: 1.10100), out _));

        // The bid only reaches 1.10095, but the ask is ten points higher, so the stop is touched.
        broker.ApplyBar(EngineTestSupport.Bar(1, 1.10000, 1.10095, 1.09990, 1.10050));

        Mql5ClosedTrade trade = Assert.Single(broker.ClosedTrades);
        Assert.Equal(Mql5CloseReason.StopLoss, trade.Reason);
        Assert.Equal(1.10100, trade.PriceClose, 5);

        // (1.10000 - 1.10100) * 100000 * 0.10 = -10.00
        Assert.Equal(-10.00, trade.GrossProfit, 2);
    }

    [Fact]
    public void StopsThatAreNotTouchedLeaveThePositionOpen()
    {
        Mql5RunOptions options = EngineTestSupport.Options();
        Mql5SimulatedBroker broker = EngineTestSupport.BrokerAt(options, 1.10000);

        broker.Send(EngineTestSupport.Market(Mql5OrderType.Buy, 0.10, sl: 1.09000, tp: 1.11000), out _);
        broker.ApplyBar(EngineTestSupport.Bar(1, 1.10000, 1.10500, 1.09500, 1.10100));

        Assert.Single(broker.Positions);
        Assert.Empty(broker.ClosedTrades);
    }

    [Fact]
    public void PendingOrdersActivateOnlyWhenTheirPriceIsTouched()
    {
        Mql5RunOptions options = EngineTestSupport.Options();
        Mql5SimulatedBroker broker = EngineTestSupport.BrokerAt(options, 1.10000);

        Assert.True(broker.Send(
            EngineTestSupport.Pending(Mql5OrderType.BuyStop, 0.10, 1.10110),
            out Mql5TradeResult placed));
        Assert.Equal(Mql5TradeRetcode.Done, placed.Retcode);
        Assert.Single(broker.PendingOrders);

        // The ask peaks at 1.10060, still below the trigger.
        broker.ApplyBar(EngineTestSupport.Bar(1, 1.10000, 1.10050, 1.09960, 1.10000));
        Assert.Single(broker.PendingOrders);
        Assert.Empty(broker.Positions);

        // Now the ask reaches 1.10210 and the order fills at its own price.
        broker.ApplyBar(EngineTestSupport.Bar(2, 1.10000, 1.10200, 1.09990, 1.10150));
        Assert.Empty(broker.PendingOrders);

        Mql5Position position = Assert.Single(broker.Positions);
        Assert.Equal(Mql5PositionType.Buy, position.Type);
        Assert.Equal(1.10110, position.PriceOpen, 5);
    }

    [Fact]
    public void BuyLimitFillsWhenTheAskFallsToIt()
    {
        Mql5RunOptions options = EngineTestSupport.Options();
        Mql5SimulatedBroker broker = EngineTestSupport.BrokerAt(options, 1.10000);

        broker.Send(EngineTestSupport.Pending(Mql5OrderType.BuyLimit, 0.10, 1.09900), out _);

        broker.ApplyBar(EngineTestSupport.Bar(1, 1.10000, 1.10010, 1.09950, 1.09960));
        Assert.Single(broker.PendingOrders);

        broker.ApplyBar(EngineTestSupport.Bar(2, 1.09960, 1.09970, 1.09880, 1.09900));
        Mql5Position position = Assert.Single(broker.Positions);
        Assert.Equal(1.09900, position.PriceOpen, 5);
    }

    [Fact]
    public void SellStopFillsWhenTheBidFallsToIt()
    {
        Mql5RunOptions options = EngineTestSupport.Options();
        Mql5SimulatedBroker broker = EngineTestSupport.BrokerAt(options, 1.10000);

        broker.Send(EngineTestSupport.Pending(Mql5OrderType.SellStop, 0.10, 1.09900), out _);
        broker.ApplyBar(EngineTestSupport.Bar(1, 1.10000, 1.10010, 1.09850, 1.09950));

        Mql5Position position = Assert.Single(broker.Positions);
        Assert.Equal(Mql5PositionType.Sell, position.Type);
        Assert.Equal(1.09900, position.PriceOpen, 5);
    }

    [Fact]
    public void PendingOrdersOnTheWrongSideOfTheMarketAreRejected()
    {
        Mql5RunOptions options = EngineTestSupport.Options();
        Mql5SimulatedBroker broker = EngineTestSupport.BrokerAt(options, 1.10000);

        // A buy stop must sit above the ask, not below it.
        Assert.False(broker.Send(EngineTestSupport.Pending(Mql5OrderType.BuyStop, 0.10, 1.09900), out Mql5TradeResult result));
        Assert.Equal(Mql5TradeRetcode.InvalidPrice, result.Retcode);
        Assert.Empty(broker.PendingOrders);
    }

    [Fact]
    public void PendingOrdersCanBeModifiedAndRemoved()
    {
        Mql5RunOptions options = EngineTestSupport.Options();
        Mql5SimulatedBroker broker = EngineTestSupport.BrokerAt(options, 1.10000);

        broker.Send(EngineTestSupport.Pending(Mql5OrderType.BuyStop, 0.10, 1.10110), out Mql5TradeResult placed);
        long ticket = placed.Order;

        Assert.True(broker.Send(
            new Mql5TradeRequest { Action = Mql5TradeAction.Modify, Order = ticket, Price = 1.10200 },
            out _));
        Assert.Equal(1.10200, broker.PendingOrders[0].Price, 5);

        Assert.True(broker.Send(
            new Mql5TradeRequest { Action = Mql5TradeAction.Remove, Order = ticket },
            out _));
        Assert.Empty(broker.PendingOrders);
    }

    [Fact]
    public void PositionStopsCanBeModifiedAfterTheFact()
    {
        Mql5RunOptions options = EngineTestSupport.Options();
        Mql5SimulatedBroker broker = EngineTestSupport.BrokerAt(options, 1.10000);

        broker.Send(EngineTestSupport.Market(Mql5OrderType.Buy, 0.10), out Mql5TradeResult opened);

        Assert.True(broker.Send(
            new Mql5TradeRequest
            {
                Action = Mql5TradeAction.Sltp,
                Position = opened.Position,
                Sl = 1.09800,
                Tp = 1.10400,
            },
            out _));

        Assert.Equal(1.09800, broker.Positions[0].StopLoss, 5);
        Assert.Equal(1.10400, broker.Positions[0].TakeProfit, 5);
    }

    [Fact]
    public void PartialCloseLeavesTheRemainderOpen()
    {
        Mql5RunOptions options = EngineTestSupport.Options();
        Mql5SimulatedBroker broker = EngineTestSupport.BrokerAt(options, 1.10000);

        broker.Send(EngineTestSupport.Market(Mql5OrderType.Buy, 0.30), out Mql5TradeResult opened);
        broker.ApplyBar(EngineTestSupport.Flat(1, 1.10100));
        broker.BeginTick();

        Assert.True(broker.Send(EngineTestSupport.ClosePosition(opened.Position, 0.10), out Mql5TradeResult closed));
        Assert.Equal(Mql5TradeRetcode.DonePartial, closed.Retcode);

        Mql5Position position = Assert.Single(broker.Positions);
        Assert.Equal(0.20, position.Volume, 6);

        Mql5ClosedTrade trade = Assert.Single(broker.ClosedTrades);
        Assert.Equal(0.10, trade.Volume, 6);

        // (1.10100 - 1.10010) * 100000 * 0.10 = 9.00
        Assert.Equal(9.00, trade.GrossProfit, 2);
    }

    [Fact]
    public void ClosingMoreThanIsOpenIsRejectedWithTheDocumentedRetcode()
    {
        Mql5RunOptions options = EngineTestSupport.Options();
        Mql5SimulatedBroker broker = EngineTestSupport.BrokerAt(options, 1.10000);

        broker.Send(EngineTestSupport.Market(Mql5OrderType.Buy, 0.10), out Mql5TradeResult opened);

        Assert.False(broker.Send(EngineTestSupport.ClosePosition(opened.Position, 0.50), out Mql5TradeResult result));
        Assert.Equal(Mql5TradeRetcode.InvalidCloseVolume, result.Retcode);
        Assert.Single(broker.Positions);
    }
}
