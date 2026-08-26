using YO4X.Mql5.Runtime;

namespace YO4X.Mql5.Runtime.Tests;

/// <summary>
/// The MQL5 standard library classes - <c>CTrade</c>, <c>CSymbolInfo</c>, <c>CPositionInfo</c>,
/// <c>COrderInfo</c>, <c>CAccountInfo</c>, <c>CDealInfo</c> and <c>CHistoryOrderInfo</c>.
///
/// The load-bearing claim is that every accessor reads the MQL5 property it is named for. A
/// reader wired to the wrong identifier still compiles, still returns a number, and returns it
/// from the wrong field - the failure a strategy notices only in its equity curve. Each test
/// therefore stocks exactly one property and demands the accessor find it.
/// </summary>
public sealed class StandardLibraryTests
{
    [Fact]
    public void SymbolTradeLevelsReadTheirMeasuredProperties()
    {
        StandardLibraryContext context = new();
        context.SymbolIntegers[31] = 15;  // SYMBOL_TRADE_STOPS_LEVEL
        context.SymbolIntegers[32] = 4;   // SYMBOL_TRADE_FREEZE_LEVEL
        context.SymbolDoubles[55] = 20.0; // SYMBOL_VOLUME_LIMIT
        Mql5SymbolInfo symbol = new(new Mql5Runtime(context));

        Assert.Equal(15, symbol.StopsLevel());
        Assert.Equal(4, symbol.FreezeLevel());
        Assert.Equal(20.0, symbol.LotsLimit());
    }

    [Fact]
    public void SymbolFillAndTimeFlagsReadTheirOwnMasks()
    {
        StandardLibraryContext context = new();
        context.SymbolIntegers[50] = 3; // SYMBOL_FILLING_MODE: FOK | IOC
        context.SymbolIntegers[49] = 1; // SYMBOL_EXPIRATION_MODE: GTC
        context.SymbolIntegers[71] = 6; // SYMBOL_ORDER_MODE
        Mql5SymbolInfo symbol = new(new Mql5Runtime(context));

        Assert.Equal(3, symbol.TradeFillFlags());
        Assert.Equal(1, symbol.TradeTimeFlags());
        Assert.Equal(6, symbol.OrderMode());
    }

    [Theory]
    [InlineData(0, "Trading on request")]
    [InlineData(1, "Trading on live streaming prices")]
    [InlineData(2, "Execution of orders on the market")]
    [InlineData(3, "Exchange execution")]
    public void TradeExecutionDescriptionNamesTheModesMql5Defines(int mode, string expected)
    {
        StandardLibraryContext context = new();
        context.SymbolIntegers[33] = mode; // SYMBOL_TRADE_EXEMODE
        Mql5SymbolInfo symbol = new(new Mql5Runtime(context));

        Assert.Equal(mode, symbol.TradeExecution());
        Assert.Equal(expected, symbol.TradeExecutionDescription());
    }

    [Fact]
    public void AnUnmodelledExecutionModeRendersAsItsNumber()
    {
        StandardLibraryContext context = new();
        context.SymbolIntegers[33] = 9;
        Mql5SymbolInfo symbol = new(new Mql5Runtime(context));

        Assert.Equal("9", symbol.TradeExecutionDescription());
    }

    [Fact]
    public void NormalizePriceRoundsToTheTickSizeNotOnlyToTheDigits()
    {
        StandardLibraryContext context = new();
        context.SymbolIntegers[17] = 2;  // SYMBOL_DIGITS
        context.SymbolDoubles[27] = 0.25; // SYMBOL_TRADE_TICK_SIZE
        Mql5SymbolInfo symbol = new(new Mql5Runtime(context));

        // 4001.31 is a price the symbol cannot quote: its ticks are a quarter apart.
        Assert.Equal(4001.25, symbol.NormalizePrice(4001.31));
        Assert.Equal(4001.5, symbol.NormalizePrice(4001.44));
    }

    [Fact]
    public void NormalizePriceFallsBackToTheDigitsWhenNoTickSizeIsPublished()
    {
        StandardLibraryContext context = new();
        context.SymbolIntegers[17] = 3;
        Mql5SymbolInfo symbol = new(new Mql5Runtime(context));

        Assert.Equal(1.235, symbol.NormalizePrice(1.23456));
    }

    [Fact]
    public void SymbolSelectionGoesThroughToTheEngine()
    {
        StandardLibraryContext context = new() { Symbol = "GBPUSD" };
        context.SymbolIntegers[0] = 1; // SYMBOL_SELECT
        Mql5SymbolInfo symbol = new(new Mql5Runtime(context));

        Assert.True(symbol.Select());
        Assert.True(symbol.Select(true));
        Assert.Equal(("GBPUSD", true), Assert.Single(context.SymbolSelections));
    }

    [Fact]
    public void SymbolTextPropertiesReadTheirMeasuredIdentifiers()
    {
        StandardLibraryContext context = new();
        context.SymbolStrings[22] = "EUR";              // SYMBOL_CURRENCY_BASE
        context.SymbolStrings[23] = "USD";              // SYMBOL_CURRENCY_PROFIT
        context.SymbolStrings[24] = "EUR";              // SYMBOL_CURRENCY_MARGIN
        context.SymbolStrings[20] = "Euro vs Dollar";   // SYMBOL_DESCRIPTION
        context.SymbolStrings[21] = "Forex\\EURUSD";    // SYMBOL_PATH
        Mql5SymbolInfo symbol = new(new Mql5Runtime(context));

        Assert.Equal("EUR", symbol.CurrencyBase());
        Assert.Equal("USD", symbol.CurrencyProfit());
        Assert.Equal("EUR", symbol.CurrencyMargin());
        Assert.Equal("Euro vs Dollar", symbol.Description());
        Assert.Equal("Forex\\EURUSD", symbol.Path());
    }

    [Fact]
    public void PositionCommissionAndIdentifiersReadTheirMeasuredProperties()
    {
        StandardLibraryContext context = new() { OpenPositions = 1 };
        context.PositionDoubles[8] = -7.5;      // POSITION_COMMISSION
        context.PositionIntegers[13] = 90_210;  // POSITION_IDENTIFIER
        context.PositionIntegers[15] = 1_700_000_100; // POSITION_TIME_UPDATE
        Mql5PositionInfo position = new(new Mql5Runtime(context));

        Assert.Equal(-7.5, position.Commission());
        Assert.Equal(90_210, position.Identifier());
        Assert.Equal(1_700_000_100, position.TimeUpdate());
    }

    [Fact]
    public void PositionCommentReadsTheCommentAndNotTheUpdateTime()
    {
        StandardLibraryContext context = new() { OpenPositions = 1 };
        context.PositionStrings[11] = "scale-in";       // POSITION_COMMENT
        context.PositionIntegers[15] = 1_700_000_100;   // POSITION_TIME_UPDATE, the neighbour
        Mql5PositionInfo position = new(new Mql5Runtime(context));

        Assert.Equal("scale-in", position.Comment());
    }

    [Theory]
    [InlineData(0, "buy")]
    [InlineData(1, "sell")]
    [InlineData(4, "unknown position type 4")]
    public void PositionTypeDescriptionRefusesToGuessAnUnmodelledType(int type, string expected)
    {
        StandardLibraryContext context = new() { OpenPositions = 1 };
        context.PositionIntegers[2] = type; // POSITION_TYPE
        Mql5PositionInfo position = new(new Mql5Runtime(context));

        Assert.Equal(expected, position.TypeDescription());
    }

    [Fact]
    public void SelectByMagicLeavesTheMatchingPositionSelected()
    {
        StandardLibraryContext context = new() { OpenPositions = 3 };
        context.PositionSymbols.AddRange(["EURUSD", "GBPUSD", "EURUSD"]);
        context.PositionMagics.AddRange([111, 222, 333]);
        Mql5PositionInfo position = new(new Mql5Runtime(context));

        Assert.True(position.SelectByMagic("EURUSD", 333));
        Assert.Equal(333, position.Magic());

        Assert.False(position.SelectByMagic("EURUSD", 222));
    }

    [Fact]
    public void CheckStateReportsAChangeOnlyAfterTheStoredShapeMoves()
    {
        StandardLibraryContext context = new() { OpenPositions = 1 };
        context.PositionIntegers[2] = 0;  // POSITION_TYPE
        context.PositionDoubles[3] = 0.5; // POSITION_VOLUME
        context.PositionDoubles[6] = 1.1; // POSITION_SL
        Mql5PositionInfo position = new(new Mql5Runtime(context));

        // Nothing stored yet: the position has never been seen, so it counts as changed.
        Assert.True(position.CheckState());

        position.StoreState();
        Assert.False(position.CheckState());

        context.PositionDoubles[6] = 1.10001;
        Assert.True(position.CheckState());
    }

    [Fact]
    public void OrderVolumesAndStopsReadTheirMeasuredProperties()
    {
        StandardLibraryContext context = new();
        context.OrderDoubles[7] = 1.0;   // ORDER_VOLUME_INITIAL
        context.OrderDoubles[8] = 0.4;   // ORDER_VOLUME_CURRENT
        context.OrderDoubles[12] = 1.05; // ORDER_SL
        context.OrderDoubles[13] = 1.15; // ORDER_TP
        context.OrderDoubles[11] = 1.09; // ORDER_PRICE_STOPLIMIT
        context.OrderIntegers[14] = 3;   // ORDER_STATE
        Mql5OrderInfo order = new(new Mql5Runtime(context));

        Assert.Equal(1.0, order.VolumeInitial());
        Assert.Equal(0.4, order.VolumeCurrent());
        Assert.Equal(1.05, order.StopLoss());
        Assert.Equal(1.15, order.TakeProfit());
        Assert.Equal(1.09, order.PriceStopLimit());
        Assert.Equal(3, order.State());
    }

    [Fact]
    public void OrderTypeDescriptionCoversThePendingTypesAndNamesTheRest()
    {
        StandardLibraryContext context = new();
        context.OrderIntegers[4] = 4; // ORDER_TYPE: buy stop
        Mql5OrderInfo order = new(new Mql5Runtime(context));

        Assert.Equal("buy stop", order.TypeDescription());

        context.OrderIntegers[4] = 12;
        Assert.Equal("unknown order type 12", order.TypeDescription());
    }

    [Fact]
    public void DealAccessorsReadTheSelectedTicketAndTheirOwnProperties()
    {
        StandardLibraryContext context = new();
        context.DealTickets.Add(5150);
        context.DealIntegers[1] = 4400;  // DEAL_ORDER
        context.DealIntegers[12] = 3300; // DEAL_POSITION_ID
        context.DealDoubles[7] = -2.5;   // DEAL_COMMISSION
        context.DealDoubles[8] = -0.75;  // DEAL_SWAP
        context.DealStrings[10] = "sl";  // DEAL_COMMENT
        Mql5DealInfo deal = new(new Mql5Runtime(context));

        Assert.True(deal.SelectByIndex(0));
        Assert.Equal(5150UL, deal.Ticket());
        Assert.Equal(4400, deal.Order());
        Assert.Equal(3300, deal.PositionId());
        Assert.Equal(-2.5, deal.Commission());
        Assert.Equal(-0.75, deal.Swap());
        Assert.Equal("sl", deal.Comment());
        Assert.Equal(5150UL, context.LastDealTicketRead);
    }

    [Fact]
    public void SettingADealTicketDirectlyPointsTheReaderAtIt()
    {
        StandardLibraryContext context = new();
        context.DealDoubles[9] = 12.25; // DEAL_PROFIT
        Mql5DealInfo deal = new(new Mql5Runtime(context));

        deal.Ticket(8080);

        Assert.Equal(8080UL, deal.Ticket());
        Assert.Equal(12.25, deal.Profit());
        Assert.Equal(8080UL, context.LastDealTicketRead);
    }

    [Fact]
    public void HistoryOrderAccessorsReadTheOrderProperties()
    {
        StandardLibraryContext context = new();
        context.HistoryOrderIntegers[14] = 4;  // ORDER_STATE: filled
        context.HistoryOrderIntegers[3] = 1_700_000_500; // ORDER_TIME_DONE
        context.HistoryOrderDoubles[7] = 0.2;  // ORDER_VOLUME_INITIAL
        context.HistoryOrderDoubles[13] = 1.3; // ORDER_TP
        context.HistoryOrderStrings[16] = "tp hit"; // ORDER_COMMENT
        Mql5HistoryOrderInfo order = new(new Mql5Runtime(context));

        Assert.Equal(4, order.State());
        Assert.Equal(1_700_000_500, order.TimeDone());
        Assert.Equal(0.2, order.VolumeInitial());
        Assert.Equal(1.3, order.TakeProfit());
        Assert.Equal("tp hit", order.Comment());
    }

    [Fact]
    public void MarginCheckAsksTheEngineAndPassesTheRequestThrough()
    {
        StandardLibraryContext context = new() { Margin = 132.5 };
        Mql5AccountInfo account = new(new Mql5Runtime(context));

        Assert.Equal(132.5, account.MarginCheck("GBPUSD", Mql5TradeConstants.OrderTypeBuy, 0.3, 1.27));
        Assert.Equal((Mql5TradeConstants.OrderTypeBuy, "GBPUSD", 0.3, 1.27), context.MarginRequest);
    }

    [Fact]
    public void AMarginCalculationTheEngineRefusesReportsEmptyValueNotZero()
    {
        StandardLibraryContext context = new() { Margin = null };
        Mql5AccountInfo account = new(new Mql5Runtime(context));

        // EMPTY_VALUE is DBL_MAX. Zero would read as "this order costs nothing to open".
        Assert.Equal(double.MaxValue, account.MarginCheck("EURUSD", Mql5TradeConstants.OrderTypeSell, 1.0, 1.1));
        Assert.True(account.FreeMarginCheck("EURUSD", Mql5TradeConstants.OrderTypeSell, 1.0, 1.1) < 0);
    }

    [Fact]
    public void FreeMarginCheckSubtractsTheRequiredMarginFromTheFreeMargin()
    {
        StandardLibraryContext context = new() { Margin = 400.0 };
        context.AccountDoubles[42] = 1_000.0; // ACCOUNT_MARGIN_FREE
        Mql5AccountInfo account = new(new Mql5Runtime(context));

        Assert.Equal(600.0, account.FreeMarginCheck("EURUSD", Mql5TradeConstants.OrderTypeBuy, 1.0, 1.1));
    }

    [Fact]
    public void MaxLotCheckNormalizesToTheVolumeStepAndTheSymbolLimits()
    {
        StandardLibraryContext context = new() { Margin = 100.0 };
        context.AccountDoubles[42] = 1_000.0; // ACCOUNT_MARGIN_FREE
        context.SymbolDoubles[36] = 0.1;      // SYMBOL_VOLUME_STEP
        context.SymbolDoubles[34] = 0.01;     // SYMBOL_VOLUME_MIN
        context.SymbolDoubles[35] = 5.0;      // SYMBOL_VOLUME_MAX
        Mql5AccountInfo account = new(new Mql5Runtime(context));

        // 1000 free margin at 100 per lot buys ten lots, which the symbol caps at five.
        Assert.Equal(5.0, account.MaxLotCheck("EURUSD", Mql5TradeConstants.OrderTypeBuy, 1.1));

        // Half the free margin buys five, and the step leaves it there.
        Assert.Equal(5.0, account.MaxLotCheck("EURUSD", Mql5TradeConstants.OrderTypeBuy, 1.1, 50.0));
    }

    [Fact]
    public void MaxLotCheckAnswersZeroRatherThanAVolumeItCannotJustify()
    {
        StandardLibraryContext context = new() { Margin = null };
        Mql5AccountInfo account = new(new Mql5Runtime(context));

        Assert.Equal(0.0, account.MaxLotCheck("EURUSD", Mql5TradeConstants.OrderTypeBuy, 1.1));
        Assert.Equal(0.0, account.MaxLotCheck("EURUSD", Mql5TradeConstants.OrderTypeBuy, 0.0));
        Assert.Equal(0.0, account.MaxLotCheck(string.Empty, Mql5TradeConstants.OrderTypeBuy, 1.1));
    }

    [Fact]
    public void AccountFlagsAndLevelsReadTheirMeasuredProperties()
    {
        StandardLibraryContext context = new();
        context.AccountIntegers[32] = 2;  // ACCOUNT_TRADE_MODE: real
        context.AccountIntegers[33] = 1;  // ACCOUNT_TRADE_ALLOWED
        context.AccountIntegers[34] = 0;  // ACCOUNT_TRADE_EXPERT
        context.AccountIntegers[47] = 200; // ACCOUNT_LIMIT_ORDERS
        context.AccountIntegers[53] = 2;  // ACCOUNT_MARGIN_MODE: hedging
        context.AccountDoubles[45] = 100.0; // ACCOUNT_MARGIN_SO_CALL
        context.AccountDoubles[46] = 50.0;  // ACCOUNT_MARGIN_SO_SO
        context.AccountStrings[2] = "Broker Ltd"; // ACCOUNT_COMPANY
        Mql5AccountInfo account = new(new Mql5Runtime(context));

        Assert.Equal(2, account.TradeMode());
        Assert.True(account.TradeAllowed());
        Assert.False(account.TradeExpert());
        Assert.Equal(200, account.LimitOrders());
        Assert.Equal(2, account.MarginMode());
        Assert.Equal(100.0, account.MarginCall());
        Assert.Equal(50.0, account.MarginStopOut());
        Assert.Equal("Broker Ltd", account.Company());
    }

    [Fact]
    public void SetMarginModeLoadsTheAccountsModeRatherThanAssumingNetting()
    {
        StandardLibraryContext context = new();
        context.AccountIntegers[53] = 2; // ACCOUNT_MARGIN_MODE: retail hedging
        Mql5Trade trade = new(new Mql5Runtime(context));

        trade.SetMarginMode();

        Assert.Equal(2, trade.MarginMode());
        Assert.True(trade.IsHedging());
    }

    [Fact]
    public void RequestAccessorsDescribeTheOrderThatWasActuallySent()
    {
        StandardLibraryContext context = new();
        context.SymbolDoubles[4] = 1.2345; // SYMBOL_ASK
        Mql5Trade trade = new(new Mql5Runtime(context));
        trade.SetExpertMagicNumber(4242);
        trade.SetDeviationInPoints(25);

        Assert.True(trade.Buy(0.5, "EURUSD", 0.0, 1.20, 1.30, "entry"));

        Assert.Equal(4242UL, trade.RequestMagic());
        Assert.Equal(Mql5TradeConstants.TradeActionDeal, trade.RequestAction());
        Assert.Equal("EURUSD", trade.RequestSymbol());
        Assert.Equal(0.5, trade.RequestVolume());
        Assert.Equal(1.2345, trade.RequestPrice());
        Assert.Equal(1.20, trade.RequestSL());
        Assert.Equal(1.30, trade.RequestTP());
        Assert.Equal(25UL, trade.RequestDeviation());
        Assert.Equal(Mql5TradeConstants.OrderTypeBuy, trade.RequestType());
        Assert.Equal("buy", trade.RequestTypeDescription());
        Assert.Equal("entry", trade.RequestComment());
    }

    [Fact]
    public void ResultRetcodeExternalStartsAtZeroBecauseNoGatewayAnswered()
    {
        StandardLibraryContext context = new();
        Mql5Trade trade = new(new Mql5Runtime(context));

        Assert.True(trade.Buy(0.1));
        Assert.Equal(0, trade.ResultRetcodeExternal());
    }

    [Fact]
    public void TheStandardLibraryConstantsMatchTheMeasuredCatalogue()
    {
        // Spot checks on the identifiers this suite passes through the runtime, so that a
        // renumbering shows up here rather than as a wrong figure inside a strategy.
        Assert.Equal(31, Mql5TradeConstants.SymbolStopsLevel);
        Assert.Equal(32, Mql5TradeConstants.SymbolFreezeLevel);
        Assert.Equal(55, Mql5TradeConstants.SymbolVolumeLimit);
        Assert.Equal(33, Mql5TradeConstants.SymbolTradeExecutionMode);
        Assert.Equal(50, Mql5TradeConstants.SymbolFillingMode);
        Assert.Equal(8, Mql5TradeConstants.PositionCommission);
        Assert.Equal(11, Mql5TradeConstants.PositionComment);
        Assert.Equal(12, Mql5TradeConstants.PositionMagic);
        Assert.Equal(8, Mql5TradeConstants.OrderVolumeCurrent);
        Assert.Equal(12, Mql5TradeConstants.OrderStopLoss);
        Assert.Equal(13, Mql5TradeConstants.OrderTakeProfit);
        Assert.Equal(1, Mql5DealConstants.Order);
        Assert.Equal(7, Mql5DealConstants.Commission);
        Assert.Equal(8, Mql5DealConstants.Swap);
        Assert.Equal(53, Mql5AccountConstants.MarginMode);
        Assert.Equal(45, Mql5AccountConstants.MarginCall);
    }
}
