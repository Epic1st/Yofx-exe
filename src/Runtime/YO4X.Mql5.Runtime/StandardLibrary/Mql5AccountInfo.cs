namespace YO4X.Mql5.Runtime;

/// <summary>
/// The MQL5 standard library <c>CAccountInfo</c>, from <c>&lt;Trade/AccountInfo.mqh&gt;</c>.
/// </summary>
/// <remarks>
/// A thin reader over the account properties. It holds no state: an account figure read a moment
/// ago is not the same figure after a fill, and every caller of this class is asking about now.
/// </remarks>
public sealed class Mql5AccountInfo(IMql5Runtime runtime)
{
    /// <summary><c>Login</c>.</summary>
    public long Login() => runtime.AccountInfoInteger(Mql5AccountConstants.Login);

    /// <summary><c>Leverage</c>.</summary>
    public long Leverage() => runtime.AccountInfoInteger(Mql5AccountConstants.Leverage);

    /// <summary><c>Balance</c>.</summary>
    public double Balance() => runtime.AccountInfoDouble(Mql5AccountConstants.Balance);

    /// <summary><c>Credit</c>.</summary>
    public double Credit() => runtime.AccountInfoDouble(Mql5AccountConstants.Credit);

    /// <summary><c>Profit</c>.</summary>
    public double Profit() => runtime.AccountInfoDouble(Mql5AccountConstants.Profit);

    /// <summary><c>Equity</c>.</summary>
    public double Equity() => runtime.AccountInfoDouble(Mql5AccountConstants.Equity);

    /// <summary><c>Margin</c>.</summary>
    public double Margin() => runtime.AccountInfoDouble(Mql5AccountConstants.Margin);

    /// <summary><c>FreeMargin</c>.</summary>
    public double FreeMargin() => runtime.AccountInfoDouble(Mql5AccountConstants.MarginFree);

    /// <summary><c>MarginLevel</c>.</summary>
    public double MarginLevel() => runtime.AccountInfoDouble(Mql5AccountConstants.MarginLevel);

    /// <summary><c>Currency</c>.</summary>
    public string Currency() => runtime.AccountInfoString(Mql5AccountConstants.Currency);

    /// <summary><c>Name</c>.</summary>
    public string Name() => runtime.AccountInfoString(Mql5AccountConstants.Name);

    /// <summary><c>Server</c>.</summary>
    public string Server() => runtime.AccountInfoString(Mql5AccountConstants.Server);

    /// <summary><c>Company</c>.</summary>
    public string Company() => runtime.AccountInfoString(Mql5AccountConstants.Company);

    /// <summary><c>TradeMode</c>: demo, contest or real.</summary>
    public int TradeMode() => (int)runtime.AccountInfoInteger(Mql5AccountConstants.TradeMode);

    /// <summary><c>TradeAllowed</c>.</summary>
    public bool TradeAllowed() => runtime.AccountInfoInteger(Mql5AccountConstants.TradeAllowed) != 0;

    /// <summary><c>TradeExpert</c>: whether the server permits automated trading.</summary>
    public bool TradeExpert() => runtime.AccountInfoInteger(Mql5AccountConstants.TradeExpert) != 0;

    /// <summary><c>LimitOrders</c>: the pending order ceiling, or zero for none.</summary>
    public int LimitOrders() => (int)runtime.AccountInfoInteger(Mql5AccountConstants.LimitOrders);

    /// <summary><c>MarginMode</c>: netting, exchange or hedging.</summary>
    public int MarginMode() => (int)runtime.AccountInfoInteger(Mql5AccountConstants.MarginMode);

    /// <summary><c>StopoutMode</c>: whether the two levels below are money or percent.</summary>
    public int StopoutMode() => (int)runtime.AccountInfoInteger(Mql5AccountConstants.MarginStopoutMode);

    /// <summary><c>MarginCall</c>.</summary>
    public double MarginCall() => runtime.AccountInfoDouble(Mql5AccountConstants.MarginCall);

    /// <summary><c>MarginStopOut</c>.</summary>
    public double MarginStopOut() => runtime.AccountInfoDouble(Mql5AccountConstants.MarginStopOut);

    /// <summary><c>MarginCheck</c>: the margin an order of this shape would require.</summary>
    /// <remarks>
    /// The figure comes from <c>OrderCalcMargin</c> and from nowhere else: margin depends on the
    /// symbol's calculation mode, its margin rates and the account leverage, and a strategy that
    /// sizes a position from an approximation here would over-commit on exactly the instruments
    /// where the approximation is worst.
    ///
    /// A failed calculation returns <c>EMPTY_VALUE</c> — MQL5's <c>DBL_MAX</c> — as the shipped
    /// class does. That is the safe direction: a caller comparing the answer against free margin
    /// refuses the trade rather than treating an unanswerable request as costing nothing.
    /// </remarks>
    public double MarginCheck(string? symbol, int orderType, double volume, double price)
        => runtime.OrderCalcMargin(orderType, symbol, volume, price, out double margin)
            ? margin
            : double.MaxValue;

    /// <summary><c>FreeMarginCheck</c>: the free margin left after an order of this shape.</summary>
    /// <remarks>
    /// Free margin less the required margin, which is how the shipped class defines it. When the
    /// margin cannot be calculated this inherits <see cref="MarginCheck"/>'s <c>EMPTY_VALUE</c>
    /// and goes deeply negative, so the usual <c>&lt;= 0</c> test still refuses the trade.
    /// </remarks>
    public double FreeMarginCheck(string? symbol, int orderType, double volume, double price)
        => FreeMargin() - MarginCheck(symbol, orderType, volume, price);

    /// <summary><c>OrderProfitCheck</c>: the profit a round trip at these prices would make.</summary>
    /// <remarks>Returns <c>EMPTY_VALUE</c> on a failed calculation, as the shipped class does.</remarks>
    public double OrderProfitCheck(string? symbol, int orderType, double volume, double priceOpen, double priceClose)
        => runtime.OrderCalcProfit(orderType, symbol, volume, priceOpen, priceClose, out double profit)
            ? profit
            : double.MaxValue;

    /// <summary><c>MaxLotCheck</c>: the largest volume <paramref name="percent"/> of the free
    /// margin can carry, normalized to the symbol's volume step and limits.</summary>
    /// <remarks>
    /// Unlike the two checks above this one answers 0 rather than <c>EMPTY_VALUE</c> when it
    /// cannot compute, because 0 is already the "no volume is affordable" answer and a caller
    /// passing <c>DBL_MAX</c> to <c>OrderSend</c> would be worse. A zero margin for one lot means
    /// the instrument charges none — pending orders on some venues — so the symbol's maximum
    /// volume is the only constraint left.
    /// </remarks>
    public double MaxLotCheck(string? symbol, int orderType, double price, double percent = 100.0)
    {
        if (string.IsNullOrEmpty(symbol) || price <= 0.0 || percent < 1.0 || percent > 100.0)
        {
            runtime.Print("CAccountInfo::MaxLotCheck invalid parameters");
            return 0.0;
        }

        if (!runtime.OrderCalcMargin(orderType, symbol, 1.0, price, out double margin) || margin < 0.0)
        {
            runtime.Print("CAccountInfo::MaxLotCheck margin calculation failed");
            return 0.0;
        }

        if (margin == 0.0)
        {
            return runtime.SymbolInfoDouble(symbol, Mql5TradeConstants.SymbolVolumeMax);
        }

        double volume = FreeMargin() * percent / 100.0 / margin;

        double step = runtime.SymbolInfoDouble(symbol, Mql5TradeConstants.SymbolVolumeStep);
        if (step > 0.0)
        {
            volume = Math.Round(step * Math.Floor((volume / step) + 1e-9), 8, MidpointRounding.AwayFromZero);
        }

        if (volume < runtime.SymbolInfoDouble(symbol, Mql5TradeConstants.SymbolVolumeMin))
        {
            return 0.0;
        }

        double maximum = runtime.SymbolInfoDouble(symbol, Mql5TradeConstants.SymbolVolumeMax);
        return volume > maximum ? maximum : volume;
    }
}

/// <summary>Account property identifiers, measured from the MQL5 compiler.</summary>
public static class Mql5AccountConstants
{
    /// <summary><c>ACCOUNT_LOGIN</c>.</summary>
    public const int Login = 0;

    /// <summary><c>ACCOUNT_NAME</c>.</summary>
    public const int Name = 1;

    /// <summary><c>ACCOUNT_COMPANY</c>.</summary>
    public const int Company = 2;

    /// <summary><c>ACCOUNT_SERVER</c>.</summary>
    public const int Server = 3;

    /// <summary><c>ACCOUNT_TRADE_MODE</c>.</summary>
    public const int TradeMode = 32;

    /// <summary><c>ACCOUNT_TRADE_ALLOWED</c>.</summary>
    public const int TradeAllowed = 33;

    /// <summary><c>ACCOUNT_TRADE_EXPERT</c>.</summary>
    public const int TradeExpert = 34;

    /// <summary><c>ACCOUNT_LEVERAGE</c>.</summary>
    public const int Leverage = 35;

    /// <summary><c>ACCOUNT_CURRENCY</c>.</summary>
    public const int Currency = 36;

    /// <summary><c>ACCOUNT_BALANCE</c>.</summary>
    public const int Balance = 37;

    /// <summary><c>ACCOUNT_CREDIT</c>.</summary>
    public const int Credit = 38;

    /// <summary><c>ACCOUNT_PROFIT</c>.</summary>
    public const int Profit = 39;

    /// <summary><c>ACCOUNT_EQUITY</c>.</summary>
    public const int Equity = 40;

    /// <summary><c>ACCOUNT_MARGIN</c>.</summary>
    public const int Margin = 41;

    /// <summary><c>ACCOUNT_MARGIN_FREE</c>.</summary>
    public const int MarginFree = 42;

    /// <summary><c>ACCOUNT_MARGIN_LEVEL</c>.</summary>
    public const int MarginLevel = 43;

    /// <summary><c>ACCOUNT_MARGIN_SO_MODE</c>: whether the two levels below are money or percent.</summary>
    public const int MarginStopoutMode = 44;

    /// <summary><c>ACCOUNT_MARGIN_SO_CALL</c>: the margin call level.</summary>
    public const int MarginCall = 45;

    /// <summary><c>ACCOUNT_MARGIN_SO_SO</c>: the stop out level.</summary>
    public const int MarginStopOut = 46;

    /// <summary><c>ACCOUNT_LIMIT_ORDERS</c>.</summary>
    public const int LimitOrders = 47;

    /// <summary><c>ACCOUNT_MARGIN_MODE</c>: netting, exchange or hedging.</summary>
    public const int MarginMode = 53;

    /// <summary><c>ACCOUNT_MARGIN_MODE_RETAIL_NETTING</c>.</summary>
    public const int MarginModeRetailNetting = 0;

    /// <summary><c>ACCOUNT_MARGIN_MODE_EXCHANGE</c>.</summary>
    public const int MarginModeExchange = 1;

    /// <summary><c>ACCOUNT_MARGIN_MODE_RETAIL_HEDGING</c>.</summary>
    public const int MarginModeRetailHedging = 2;
}
