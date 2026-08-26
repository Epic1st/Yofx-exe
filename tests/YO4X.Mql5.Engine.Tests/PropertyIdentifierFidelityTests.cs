using YO4X.Mql5.Engine.Trading;
using YO4X.StrategyGovernance;

namespace YO4X.Mql5.Engine.Tests;

/// <summary>
/// Pins every engine property identifier to the value MQL5 itself assigns.
///
/// These numbers are not an internal convention the engine is free to pick. Generated code passes
/// through whatever value the strategy's own source names — <c>ACCOUNT_BALANCE</c> arrives as 37
/// because that is what the MQL5 compiler substitutes. An engine that numbered it 0 would not fail:
/// its switch would miss, fall to the default, and answer a balance query with a zero. Nothing
/// observable distinguishes that from a genuinely empty account, which is exactly why it has to be
/// a test rather than a convention.
/// </summary>
public sealed class PropertyIdentifierFidelityTests
{
    [Theory]
    // ENUM_SYMBOL_INFO_DOUBLE
    [InlineData("SYMBOL_BID", Mql5SymbolInfoDouble.Bid)]
    [InlineData("SYMBOL_ASK", Mql5SymbolInfoDouble.Ask)]
    [InlineData("SYMBOL_LAST", Mql5SymbolInfoDouble.Last)]
    [InlineData("SYMBOL_POINT", Mql5SymbolInfoDouble.Point)]
    [InlineData("SYMBOL_TRADE_TICK_VALUE", Mql5SymbolInfoDouble.TickValue)]
    [InlineData("SYMBOL_TRADE_TICK_SIZE", Mql5SymbolInfoDouble.TickSize)]
    [InlineData("SYMBOL_TRADE_CONTRACT_SIZE", Mql5SymbolInfoDouble.ContractSize)]
    [InlineData("SYMBOL_VOLUME_MIN", Mql5SymbolInfoDouble.VolumeMin)]
    [InlineData("SYMBOL_VOLUME_MAX", Mql5SymbolInfoDouble.VolumeMax)]
    [InlineData("SYMBOL_VOLUME_STEP", Mql5SymbolInfoDouble.VolumeStep)]
    [InlineData("SYMBOL_SWAP_LONG", Mql5SymbolInfoDouble.SwapLong)]
    [InlineData("SYMBOL_SWAP_SHORT", Mql5SymbolInfoDouble.SwapShort)]
    // ENUM_SYMBOL_INFO_INTEGER
    [InlineData("SYMBOL_SELECT", Mql5SymbolInfoInteger.Select)]
    [InlineData("SYMBOL_TIME", Mql5SymbolInfoInteger.Time)]
    [InlineData("SYMBOL_DIGITS", Mql5SymbolInfoInteger.Digits)]
    [InlineData("SYMBOL_SPREAD", Mql5SymbolInfoInteger.Spread)]
    [InlineData("SYMBOL_TRADE_STOPS_LEVEL", Mql5SymbolInfoInteger.StopsLevel)]
    [InlineData("SYMBOL_TRADE_FREEZE_LEVEL", Mql5SymbolInfoInteger.FreezeLevel)]
    // ENUM_ACCOUNT_INFO_DOUBLE
    [InlineData("ACCOUNT_BALANCE", Mql5AccountInfoDouble.Balance)]
    [InlineData("ACCOUNT_CREDIT", Mql5AccountInfoDouble.Credit)]
    [InlineData("ACCOUNT_PROFIT", Mql5AccountInfoDouble.Profit)]
    [InlineData("ACCOUNT_EQUITY", Mql5AccountInfoDouble.Equity)]
    [InlineData("ACCOUNT_MARGIN", Mql5AccountInfoDouble.Margin)]
    [InlineData("ACCOUNT_MARGIN_FREE", Mql5AccountInfoDouble.MarginFree)]
    [InlineData("ACCOUNT_MARGIN_LEVEL", Mql5AccountInfoDouble.MarginLevel)]
    [InlineData("ACCOUNT_MARGIN_SO_SO", Mql5AccountInfoDouble.MarginStopOut)]
    // ENUM_ACCOUNT_INFO_INTEGER
    [InlineData("ACCOUNT_LOGIN", Mql5AccountInfoInteger.Login)]
    [InlineData("ACCOUNT_LEVERAGE", Mql5AccountInfoInteger.Leverage)]
    [InlineData("ACCOUNT_MARGIN_MODE", Mql5AccountInfoInteger.MarginMode)]
    // ENUM_POSITION_PROPERTY_DOUBLE
    [InlineData("POSITION_VOLUME", Mql5PositionDouble.Volume)]
    [InlineData("POSITION_PRICE_OPEN", Mql5PositionDouble.PriceOpen)]
    [InlineData("POSITION_PRICE_CURRENT", Mql5PositionDouble.PriceCurrent)]
    [InlineData("POSITION_SL", Mql5PositionDouble.StopLoss)]
    [InlineData("POSITION_TP", Mql5PositionDouble.TakeProfit)]
    [InlineData("POSITION_COMMISSION", Mql5PositionDouble.Commission)]
    [InlineData("POSITION_SWAP", Mql5PositionDouble.Swap)]
    [InlineData("POSITION_PROFIT", Mql5PositionDouble.Profit)]
    // ENUM_POSITION_PROPERTY_INTEGER
    [InlineData("POSITION_TIME", Mql5PositionInteger.Time)]
    [InlineData("POSITION_TYPE", Mql5PositionInteger.Type)]
    [InlineData("POSITION_MAGIC", Mql5PositionInteger.Magic)]
    [InlineData("POSITION_IDENTIFIER", Mql5PositionInteger.Identifier)]
    [InlineData("POSITION_TICKET", Mql5PositionInteger.Ticket)]
    // Return and lifecycle codes.
    [InlineData("TRADE_RETCODE_DONE", Mql5TradeRetcode.Done)]
    [InlineData("TRADE_RETCODE_DONE_PARTIAL", Mql5TradeRetcode.DonePartial)]
    [InlineData("TRADE_RETCODE_REJECT", Mql5TradeRetcode.Reject)]
    [InlineData("TRADE_RETCODE_CANCEL", Mql5TradeRetcode.Cancel)]
    [InlineData("TRADE_RETCODE_ERROR", Mql5TradeRetcode.Error)]
    [InlineData("TRADE_RETCODE_INVALID", Mql5TradeRetcode.Invalid)]
    [InlineData("TRADE_RETCODE_INVALID_VOLUME", Mql5TradeRetcode.InvalidVolume)]
    [InlineData("TRADE_RETCODE_INVALID_PRICE", Mql5TradeRetcode.InvalidPrice)]
    [InlineData("TRADE_RETCODE_INVALID_STOPS", Mql5TradeRetcode.InvalidStops)]
    [InlineData("TRADE_RETCODE_NO_MONEY", Mql5TradeRetcode.NoMoney)]
    [InlineData("TRADE_RETCODE_PRICE_OFF", Mql5TradeRetcode.PriceOff)]
    [InlineData("TRADE_RETCODE_NO_CHANGES", Mql5TradeRetcode.NoChanges)]
    [InlineData("TRADE_RETCODE_LIMIT_ORDERS", Mql5TradeRetcode.LimitOrders)]
    [InlineData("TRADE_RETCODE_INVALID_CLOSE_VOLUME", Mql5TradeRetcode.InvalidCloseVolume)]
    [InlineData("TRADE_RETCODE_POSITION_CLOSED", Mql5TradeRetcode.PositionClosed)]
    [InlineData("INIT_SUCCEEDED", Mql5InitCode.Succeeded)]
    [InlineData("INIT_FAILED", Mql5InitCode.Failed)]
    [InlineData("INIT_PARAMETERS_INCORRECT", Mql5InitCode.ParametersIncorrect)]
    [InlineData("REASON_PROGRAM", Mql5DeinitReason.Program)]
    [InlineData("REASON_REMOVE", Mql5DeinitReason.Remove)]
    [InlineData("REASON_INITFAILED", Mql5DeinitReason.InitFailed)]
    [InlineData("REASON_CLOSE", Mql5DeinitReason.Close)]
    public void EngineIdentifierMatchesTheCompilerMeasuredValue(string mql5Name, int engineValue)
    {
        Assert.True(
            Mql5BuiltinConstants.TryGetValue(mql5Name, out long measured),
            mql5Name + " is absent from the measured constant catalogue.");

        Assert.Equal(measured, engineValue);
    }
}
