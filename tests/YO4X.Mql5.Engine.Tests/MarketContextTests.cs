using YO4X.Mql5.Engine.Context;
using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Hosting;
using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Tests;

/// <summary>The bridge a translated expert advisor actually calls.</summary>
public sealed class MarketContextTests
{
    private static Mql5MarketContext Build(Mql5RunOptions options, params double[] closes)
    {
        var broker = new Mql5SimulatedBroker(options);
        var context = new Mql5MarketContext(broker, options);

        for (int index = 0; index < closes.Length; index++)
        {
            Mql5Bar bar = EngineTestSupport.Flat(index, closes[index], options.SpreadPoints);
            context.AppendBar(bar);
            broker.ApplyBar(bar);
        }

        broker.BeginTick();
        return context;
    }

    [Fact]
    public void SymbolPropertiesReportTheContractSpecification()
    {
        Mql5RunOptions options = EngineTestSupport.Options();
        Mql5MarketContext context = Build(options, 1.10000);

        Assert.Equal("EURUSD", context.Symbol);
        Assert.Equal(5, context.Digits);
        Assert.Equal(0.00001, context.Point, 10);
        Assert.Equal(EngineTestSupport.Origin, context.TimeCurrent);

        Assert.Equal(1.10000, context.SymbolInfoDouble("EURUSD", Mql5SymbolInfoDouble.Bid), 5);
        Assert.Equal(1.10010, context.SymbolInfoDouble("EURUSD", Mql5SymbolInfoDouble.Ask), 5);
        Assert.Equal(0.00001, context.SymbolInfoDouble("EURUSD", Mql5SymbolInfoDouble.Point), 10);
        Assert.Equal(1.0, context.SymbolInfoDouble("EURUSD", Mql5SymbolInfoDouble.TickValue), 10);
        Assert.Equal(100_000.0, context.SymbolInfoDouble("EURUSD", Mql5SymbolInfoDouble.ContractSize), 5);
        Assert.Equal(0.01, context.SymbolInfoDouble("EURUSD", Mql5SymbolInfoDouble.VolumeMin), 5);

        Assert.Equal(5, context.SymbolInfoInteger("EURUSD", Mql5SymbolInfoInteger.Digits));
        Assert.Equal(10, context.SymbolInfoInteger("EURUSD", Mql5SymbolInfoInteger.Spread));

        // An unknown symbol yields zero rather than an exception.
        Assert.Equal(0.0, context.SymbolInfoDouble("USDJPY", Mql5SymbolInfoDouble.Bid), 10);
        Assert.Equal(0L, context.SymbolInfoInteger("USDJPY", Mql5SymbolInfoInteger.Digits));
    }

    [Fact]
    public void AccountPropertiesFollowTheBroker()
    {
        Mql5RunOptions options = EngineTestSupport.Options(deposit: 10_000.0, leverage: 100);
        Mql5MarketContext context = Build(options, 1.10000);

        Assert.Equal(10_000.0, context.AccountInfoDouble(Mql5AccountInfoDouble.Balance), 2);
        Assert.Equal(10_000.0, context.AccountInfoDouble(Mql5AccountInfoDouble.Equity), 2);
        Assert.Equal(0.0, context.AccountInfoDouble(Mql5AccountInfoDouble.Margin), 2);
        Assert.Equal("USD", context.AccountCurrency);
        Assert.Equal(100L, context.AccountInfoInteger(Mql5AccountInfoInteger.Leverage));

        context.OrderSend(EngineTestSupport.Market(Mql5OrderType.Buy, 0.10), out _);

        Assert.Equal(110.01, context.AccountInfoDouble(Mql5AccountInfoDouble.Margin), 2);
        Assert.Equal(-1.00, context.AccountInfoDouble(Mql5AccountInfoDouble.Profit), 2);
        Assert.Equal(9_999.00, context.AccountInfoDouble(Mql5AccountInfoDouble.Equity), 2);
        Assert.Equal(9_888.99, context.AccountInfoDouble(Mql5AccountInfoDouble.MarginFree), 2);
    }

    [Fact]
    public void PositionSelectionExposesTheSelectedPositionsProperties()
    {
        Mql5RunOptions options = EngineTestSupport.Options();
        Mql5MarketContext context = Build(options, 1.10000);

        Assert.Equal(0, context.PositionsTotal());
        Assert.False(context.PositionSelect("EURUSD"));

        Mql5TradeRequest request = EngineTestSupport.Market(Mql5OrderType.Buy, 0.10, sl: 1.09900, tp: 1.10500);
        request.Magic = 4711;
        Assert.True(context.OrderSend(request, out Mql5TradeResult opened));

        Assert.Equal(1, context.PositionsTotal());
        Assert.True(context.PositionSelect("EURUSD"));

        Assert.Equal(0.10, context.PositionGetDouble(Mql5PositionDouble.Volume), 6);
        Assert.Equal(1.10010, context.PositionGetDouble(Mql5PositionDouble.PriceOpen), 5);
        Assert.Equal(1.09900, context.PositionGetDouble(Mql5PositionDouble.StopLoss), 5);
        Assert.Equal(1.10500, context.PositionGetDouble(Mql5PositionDouble.TakeProfit), 5);
        Assert.Equal(1.10000, context.PositionGetDouble(Mql5PositionDouble.PriceCurrent), 5);
        Assert.Equal(-1.00, context.PositionGetDouble(Mql5PositionDouble.Profit), 2);

        Assert.Equal(opened.Position, context.PositionGetInteger(Mql5PositionInteger.Ticket));
        Assert.Equal((long)Mql5PositionType.Buy, context.PositionGetInteger(Mql5PositionInteger.Type));
        Assert.Equal(4711L, context.PositionGetInteger(Mql5PositionInteger.Magic));
        Assert.Equal("EURUSD", context.PositionGetSymbol());
    }

    [Fact]
    public void HedgingPositionsAreEnumerableByIndex()
    {
        Mql5RunOptions options = EngineTestSupport.Options(Mql5MarginMode.Hedging);
        Mql5MarketContext context = Build(options, 1.10000);

        context.OrderSend(EngineTestSupport.Market(Mql5OrderType.Buy, 0.10), out Mql5TradeResult first);
        context.OrderSend(EngineTestSupport.Market(Mql5OrderType.Sell, 0.20), out Mql5TradeResult second);

        Assert.Equal(2, context.PositionsTotal());
        Assert.Equal(first.Position, context.PositionGetTicket(0));
        Assert.Equal((long)Mql5PositionType.Buy, context.PositionGetInteger(Mql5PositionInteger.Type));
        Assert.Equal(second.Position, context.PositionGetTicket(1));
        Assert.Equal((long)Mql5PositionType.Sell, context.PositionGetInteger(Mql5PositionInteger.Type));
        Assert.Equal(0.20, context.PositionGetDouble(Mql5PositionDouble.Volume), 6);

        Assert.Equal(0L, context.PositionGetTicket(7));
        Assert.Equal(0.0, context.PositionGetDouble(Mql5PositionDouble.Volume), 6);
    }

    [Fact]
    public void IndicatorHandlesAreAllocatedOnceAndReused()
    {
        Mql5RunOptions options = EngineTestSupport.Options();
        Mql5MarketContext context = Build(options, 1.10000);

        int first = context.IndicatorHandle("iMA", 14, 0, Mql5MaMethod.Sma, Mql5AppliedPrice.Close);
        int again = context.IndicatorHandle("iMA", 14, 0, Mql5MaMethod.Sma, Mql5AppliedPrice.Close);
        int different = context.IndicatorHandle("iMA", 21, 0, Mql5MaMethod.Sma, Mql5AppliedPrice.Close);

        Assert.True(first > 0);
        Assert.Equal(first, again);
        Assert.NotEqual(first, different);
        Assert.Equal(-1, context.IndicatorHandle("iNotAnIndicator", 14));
    }

    [Fact]
    public void CopyBufferReturnsOldestFirstAndRefusesUnderfilledRequests()
    {
        Mql5RunOptions options = EngineTestSupport.Options();
        Mql5MarketContext context = Build(options, 1.10000, 1.10010, 1.10020, 1.10030, 1.10040);

        int handle = context.IndicatorHandle("iMA", 1, 0, Mql5MaMethod.Sma, Mql5AppliedPrice.Close);
        Assert.True(handle > 0);

        double[] buffer = new double[3];
        Assert.Equal(3, context.CopyBuffer(handle, 0, 0, 3, buffer));

        // Oldest first: index 2 is the current bar.
        Assert.Equal(1.10020, buffer[0], 5);
        Assert.Equal(1.10030, buffer[1], 5);
        Assert.Equal(1.10040, buffer[2], 5);

        // A start offset shifts the window back in time.
        Assert.Equal(2, context.CopyBuffer(handle, 0, 2, 2, buffer));
        Assert.Equal(1.10010, buffer[0], 5);
        Assert.Equal(1.10020, buffer[1], 5);

        // Not enough history, unknown handle, unknown buffer and a short target all report -1.
        Assert.Equal(-1, context.CopyBuffer(handle, 0, 0, 99, new double[99]));
        Assert.Equal(-1, context.CopyBuffer(999, 0, 0, 1, buffer));
        Assert.Equal(-1, context.CopyBuffer(handle, 4, 0, 1, buffer));
        Assert.Equal(-1, context.CopyBuffer(handle, 0, 0, 5, new double[2]));
    }

    [Fact]
    public void AnIndicatorAllocatedMidRunIsBackFilledOverTheBarsAlreadySeen()
    {
        Mql5RunOptions options = EngineTestSupport.Options();
        Mql5MarketContext context = Build(options, 10.0, 12.0, 11.0, 15.0, 14.0);

        int handle = context.IndicatorHandle("iMA", 3, 0, Mql5MaMethod.Sma, Mql5AppliedPrice.Close);
        double[] buffer = new double[1];

        Assert.Equal(1, context.CopyBuffer(handle, 0, 0, 1, buffer));
        Assert.Equal(40.0 / 3.0, buffer[0], 10);   // (11 + 15 + 14) / 3
    }

    [Fact]
    public void SeriesAccessorsIndexBackwardsFromTheCurrentBar()
    {
        Mql5RunOptions options = EngineTestSupport.Options();
        Mql5MarketContext context = Build(options, 1.10000, 1.10010, 1.10020);

        Assert.Equal(3, context.BarCount);
        Assert.Equal(1.10020, context.Close(0), 5);
        Assert.Equal(1.10010, context.Close(1), 5);
        Assert.Equal(1.10000, context.Close(2), 5);
        Assert.Equal(EngineTestSupport.Origin.AddHours(2), context.Time(0));

        // Out of range reads return a default bar instead of throwing.
        Assert.Equal(0.0, context.Close(9), 5);
    }

    [Fact]
    public void TheContextExposesTheSameQuotesAsTheBroker()
    {
        Mql5RunOptions options = EngineTestSupport.Options();
        Mql5MarketContext context = Build(options, 1.10000);

        Assert.Equal(context.Broker.Bid, context.Bid, 10);
        Assert.Equal(context.Broker.Ask, context.Ask, 10);
    }
}
