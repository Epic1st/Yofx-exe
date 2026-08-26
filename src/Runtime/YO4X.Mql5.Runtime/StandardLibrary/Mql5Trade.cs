namespace YO4X.Mql5.Runtime;

/// <summary>
/// The MQL5 standard library <c>CTrade</c>, from <c>&lt;Trade/Trade.mqh&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>CTrade</c> is not part of the MQL5 language: it ships as source in every MetaTrader
/// installation, and most expert advisors include it rather than filling in an
/// <c>MqlTradeRequest</c> by hand. Translating a strategy that uses it therefore needs the class
/// itself, and the honest way to supply it is to write it against this runtime — the same
/// <c>OrderSend</c> a hand-written strategy reaches — rather than to special-case its methods in
/// the code generator.
/// </para>
/// <para>
/// The surface implemented here is the one the corpus actually calls. A method MetaQuotes ships
/// that nothing uses is not stubbed out to look complete: an absent member is a compile error
/// naming exactly what is missing, while a stub returning false is a strategy that silently stops
/// trading.
/// </para>
/// </remarks>
public sealed class Mql5Trade(IMql5Runtime runtime)
{
    private ulong magic;
    private ulong deviation = 10;
    private int typeFilling = Mql5TradeConstants.OrderFillingFok;
    private int marginMode;
    private bool async;
    private int logLevel = 2;

    /// <summary>The result of the last request this object sent.</summary>
    public Mql5TradeResult Result { get; } = new();

    // ------------------------------------------------------------------ settings

    /// <summary><c>SetExpertMagicNumber</c>.</summary>
    public void SetExpertMagicNumber(ulong value) => magic = value;

    /// <summary><c>SetDeviationInPoints</c>.</summary>
    public void SetDeviationInPoints(ulong value) => deviation = value;

    /// <summary><c>SetTypeFilling</c>.</summary>
    public void SetTypeFilling(int value) => typeFilling = value;

    /// <summary><c>SetMarginMode</c>: reads the account's margin mode into this object.</summary>
    /// <remarks>
    /// The shipped method takes no argument — it exists to load <c>ACCOUNT_MARGIN_MODE</c>, which
    /// is what tells the class whether the account nets positions or hedges them. An earlier
    /// signature here took a mode and defaulted it to zero, so the usual argument-free call from a
    /// strategy silently declared a netting account; on a hedging account that is the wrong model
    /// of what a close does.
    /// </remarks>
    public void SetMarginMode()
        => marginMode = (int)runtime.AccountInfoInteger(Mql5AccountConstants.MarginMode);

    /// <summary><c>SetAsyncMode</c>.</summary>
    public void SetAsyncMode(bool value) => async = value;

    /// <summary><c>LogLevel</c>.</summary>
    public void LogLevel(int value) => logLevel = value;

    /// <summary>The margin mode <see cref="SetMarginMode"/> last loaded.</summary>
    /// <remarks>
    /// The shipped class keeps this protected and exposes only <c>IsHedging</c>. It is public here
    /// because a translated strategy has no other way to reach it.
    /// </remarks>
    public int MarginMode() => marginMode;

    /// <summary>Whether the account nets positions or hedges them, as MQL5's <c>IsHedging</c>.</summary>
    public bool IsHedging() => marginMode == Mql5AccountConstants.MarginModeRetailHedging;

    /// <summary><c>SetTypeFillingBySymbol</c>: picks a filling mode the symbol permits.</summary>
    /// <remarks>
    /// The symbol advertises its permitted modes as a bit mask in <c>SYMBOL_FILLING_MODE</c>, and
    /// FOK is preferred over IOC as the shipped class does; picking a mode the symbol forbids
    /// would have every order rejected for a reason the strategy never states.
    ///
    /// One deliberate deviation: where the shipped class returns false and leaves the mode alone
    /// when the mask names neither FOK nor IOC, this falls back to RETURN and reports success. An
    /// engine that does not answer <c>SYMBOL_FILLING_MODE</c> reports a mask of zero, and the
    /// faithful reading of that would fail every strategy whose <c>OnInit</c> checks this call.
    /// Remove the fallback once the engine reports the property.
    /// </remarks>
    public bool SetTypeFillingBySymbol(string? symbol)
    {
        long permitted = runtime.SymbolInfoInteger(symbol, Mql5TradeConstants.SymbolFillingMode);

        typeFilling = (permitted & Mql5TradeConstants.FillingFokFlag) != 0
            ? Mql5TradeConstants.OrderFillingFok
            : (permitted & Mql5TradeConstants.FillingIocFlag) != 0
                ? Mql5TradeConstants.OrderFillingIoc
                : Mql5TradeConstants.OrderFillingReturn;

        return true;
    }

    // ------------------------------------------------------------------ requests

    /// <summary>
    /// The request this object last built, which the <c>Request*</c> accessors below read.
    /// </summary>
    /// <remarks>
    /// A strategy that logs a rejection reads it out of here, because the result carries only the
    /// server's answer and not what was asked for. The structure is rebuilt on every send, so
    /// these accessors describe the most recent attempt and not a successful one.
    /// </remarks>
    public Mql5TradeRequest Request { get; } = new();

    /// <summary><c>RequestAction</c>.</summary>
    public int RequestAction() => Request.Action;

    /// <summary><c>RequestMagic</c>.</summary>
    public ulong RequestMagic() => Request.Magic;

    /// <summary><c>RequestOrder</c>.</summary>
    public ulong RequestOrder() => Request.Order;

    /// <summary><c>RequestPosition</c>.</summary>
    public ulong RequestPosition() => Request.Position;

    /// <summary><c>RequestPositionBy</c>.</summary>
    public ulong RequestPositionBy() => Request.PositionBy;

    /// <summary><c>RequestSymbol</c>.</summary>
    public string RequestSymbol() => Request.Symbol;

    /// <summary><c>RequestVolume</c>.</summary>
    public double RequestVolume() => Request.Volume;

    /// <summary><c>RequestPrice</c>.</summary>
    public double RequestPrice() => Request.Price;

    /// <summary><c>RequestStopLimit</c>.</summary>
    public double RequestStopLimit() => Request.StopLimit;

    /// <summary><c>RequestSL</c>.</summary>
    public double RequestSL() => Request.StopLoss;

    /// <summary><c>RequestTP</c>.</summary>
    public double RequestTP() => Request.TakeProfit;

    /// <summary><c>RequestDeviation</c>.</summary>
    public ulong RequestDeviation() => Request.Deviation;

    /// <summary><c>RequestType</c>.</summary>
    public int RequestType() => Request.Type;

    /// <summary><c>RequestTypeDescription</c>.</summary>
    public string RequestTypeDescription() => Mql5TradeConstants.DescribeOrderType(Request.Type);

    /// <summary><c>RequestTypeFilling</c>.</summary>
    public int RequestTypeFilling() => Request.TypeFilling;

    /// <summary><c>RequestTypeTime</c>.</summary>
    public int RequestTypeTime() => Request.TypeTime;

    /// <summary><c>RequestExpiration</c>, as seconds since 1970.</summary>
    public long RequestExpiration() => Request.Expiration;

    /// <summary><c>RequestComment</c>.</summary>
    public string RequestComment() => Request.Comment;

    // ------------------------------------------------------------------- results

    /// <summary><c>ResultRetcode</c>.</summary>
    public uint ResultRetcode() => Result.Retcode;

    /// <summary><c>ResultRetcodeExternal</c>: the reply code of an external trading system.</summary>
    /// <remarks>
    /// Only exchange gateways fill this in; a retail broker leaves it zero, and zero therefore
    /// means "not reported" rather than "accepted".
    /// </remarks>
    public int ResultRetcodeExternal() => Result.RetcodeExternal;

    /// <summary><c>ResultOrder</c>.</summary>
    public ulong ResultOrder() => Result.Order;

    /// <summary><c>ResultDeal</c>.</summary>
    public ulong ResultDeal() => Result.Deal;

    /// <summary><c>ResultVolume</c>.</summary>
    public double ResultVolume() => Result.Volume;

    /// <summary><c>ResultPrice</c>.</summary>
    public double ResultPrice() => Result.Price;

    /// <summary><c>ResultBid</c>.</summary>
    public double ResultBid() => Result.Bid;

    /// <summary><c>ResultAsk</c>.</summary>
    public double ResultAsk() => Result.Ask;

    /// <summary><c>ResultComment</c>.</summary>
    public string ResultComment() => Result.Comment;

    /// <summary><c>ResultRetcodeDescription</c>.</summary>
    public string ResultRetcodeDescription() => Mql5TradeConstants.Describe(Result.Retcode);

    // -------------------------------------------------------------------- orders

    /// <summary><c>Buy</c>: opens a long position at market.</summary>
    public bool Buy(
        double volume,
        string? symbol = null,
        double price = 0.0,
        double stopLoss = 0.0,
        double takeProfit = 0.0,
        string? comment = null)
    {
        string resolved = Resolve(symbol);
        double at = price > 0.0 ? price : runtime.SymbolInfoDouble(resolved, Mql5TradeConstants.SymbolAsk);
        return PositionOpen(resolved, Mql5TradeConstants.OrderTypeBuy, volume, at, stopLoss, takeProfit, comment);
    }

    /// <summary><c>Sell</c>: opens a short position at market.</summary>
    public bool Sell(
        double volume,
        string? symbol = null,
        double price = 0.0,
        double stopLoss = 0.0,
        double takeProfit = 0.0,
        string? comment = null)
    {
        string resolved = Resolve(symbol);
        double at = price > 0.0 ? price : runtime.SymbolInfoDouble(resolved, Mql5TradeConstants.SymbolBid);
        return PositionOpen(resolved, Mql5TradeConstants.OrderTypeSell, volume, at, stopLoss, takeProfit, comment);
    }

    /// <summary><c>BuyLimit</c>.</summary>
    public bool BuyLimit(
        double volume,
        double price,
        string? symbol = null,
        double stopLoss = 0.0,
        double takeProfit = 0.0,
        int typeTime = 0,
        long expiration = 0,
        string? comment = null)
        => OrderOpen(symbol, Mql5TradeConstants.OrderTypeBuyLimit, volume, 0.0, price, stopLoss, takeProfit, typeTime, expiration, comment);

    /// <summary><c>SellLimit</c>.</summary>
    public bool SellLimit(
        double volume,
        double price,
        string? symbol = null,
        double stopLoss = 0.0,
        double takeProfit = 0.0,
        int typeTime = 0,
        long expiration = 0,
        string? comment = null)
        => OrderOpen(symbol, Mql5TradeConstants.OrderTypeSellLimit, volume, 0.0, price, stopLoss, takeProfit, typeTime, expiration, comment);

    /// <summary><c>BuyStop</c>.</summary>
    public bool BuyStop(
        double volume,
        double price,
        string? symbol = null,
        double stopLoss = 0.0,
        double takeProfit = 0.0,
        int typeTime = 0,
        long expiration = 0,
        string? comment = null)
        => OrderOpen(symbol, Mql5TradeConstants.OrderTypeBuyStop, volume, 0.0, price, stopLoss, takeProfit, typeTime, expiration, comment);

    /// <summary><c>SellStop</c>.</summary>
    public bool SellStop(
        double volume,
        double price,
        string? symbol = null,
        double stopLoss = 0.0,
        double takeProfit = 0.0,
        int typeTime = 0,
        long expiration = 0,
        string? comment = null)
        => OrderOpen(symbol, Mql5TradeConstants.OrderTypeSellStop, volume, 0.0, price, stopLoss, takeProfit, typeTime, expiration, comment);

    /// <summary><c>PositionOpen</c>.</summary>
    public bool PositionOpen(
        string? symbol,
        int orderType,
        double volume,
        double price,
        double stopLoss,
        double takeProfit,
        string? comment = null)
    {
        Prepare(Mql5TradeConstants.TradeActionDeal, Resolve(symbol));
        Request.Type = orderType;
        Request.Volume = volume;
        Request.Price = price;
        Request.StopLoss = stopLoss;
        Request.TakeProfit = takeProfit;
        Request.Comment = comment ?? string.Empty;
        return Send();
    }

    /// <summary><c>PositionModify</c> by symbol.</summary>
    public bool PositionModify(string? symbol, double stopLoss, double takeProfit)
    {
        string resolved = Resolve(symbol);
        return runtime.PositionSelect(resolved)
            ? ModifySelected(resolved, stopLoss, takeProfit)
            : Reject(Mql5TradeConstants.RetcodeInvalid, "no open position on " + resolved);
    }

    /// <summary><c>PositionModify</c> by ticket.</summary>
    public bool PositionModify(ulong ticket, double stopLoss, double takeProfit)
    {
        if (!runtime.PositionSelectByTicket(ticket))
        {
            return Reject(Mql5TradeConstants.RetcodeInvalid, "no position with ticket " + ticket);
        }

        return ModifySelected(
            runtime.PositionGetString(Mql5TradeConstants.PositionSymbol),
            stopLoss,
            takeProfit,
            ticket);
    }

    /// <summary><c>PositionClose</c> by symbol.</summary>
    public bool PositionClose(string? symbol, ulong slippage = ulong.MaxValue)
    {
        string resolved = Resolve(symbol);
        return runtime.PositionSelect(resolved)
            ? CloseSelected(resolved, runtime.PositionGetDouble(Mql5TradeConstants.PositionVolume), slippage)
            : Reject(Mql5TradeConstants.RetcodeInvalid, "no open position on " + resolved);
    }

    /// <summary><c>PositionClose</c> by ticket.</summary>
    public bool PositionClose(ulong ticket, ulong slippage = ulong.MaxValue)
    {
        if (!runtime.PositionSelectByTicket(ticket))
        {
            return Reject(Mql5TradeConstants.RetcodeInvalid, "no position with ticket " + ticket);
        }

        return CloseSelected(
            runtime.PositionGetString(Mql5TradeConstants.PositionSymbol),
            runtime.PositionGetDouble(Mql5TradeConstants.PositionVolume),
            slippage,
            ticket);
    }

    /// <summary><c>PositionClosePartial</c> by symbol.</summary>
    public bool PositionClosePartial(string? symbol, double volume, ulong slippage = ulong.MaxValue)
    {
        string resolved = Resolve(symbol);
        return runtime.PositionSelect(resolved)
            ? CloseSelected(resolved, volume, slippage)
            : Reject(Mql5TradeConstants.RetcodeInvalid, "no open position on " + resolved);
    }

    /// <summary><c>PositionClosePartial</c> by ticket.</summary>
    public bool PositionClosePartial(ulong ticket, double volume, ulong slippage = ulong.MaxValue)
    {
        if (!runtime.PositionSelectByTicket(ticket))
        {
            return Reject(Mql5TradeConstants.RetcodeInvalid, "no position with ticket " + ticket);
        }

        return CloseSelected(
            runtime.PositionGetString(Mql5TradeConstants.PositionSymbol),
            volume,
            slippage,
            ticket);
    }

    /// <summary><c>OrderOpen</c>: places a pending order.</summary>
    public bool OrderOpen(
        string? symbol,
        int orderType,
        double volume,
        double limitPrice,
        double price,
        double stopLoss,
        double takeProfit,
        int typeTime = 0,
        long expiration = 0,
        string? comment = null)
    {
        Prepare(Mql5TradeConstants.TradeActionPending, Resolve(symbol));
        Request.Type = orderType;
        Request.Volume = volume;
        Request.Price = price;
        Request.StopLimit = limitPrice;
        Request.StopLoss = stopLoss;
        Request.TakeProfit = takeProfit;
        Request.TypeTime = typeTime;
        Request.Expiration = expiration;
        Request.Comment = comment ?? string.Empty;
        return Send();
    }

    /// <summary><c>OrderModify</c>.</summary>
    public bool OrderModify(
        ulong ticket,
        double price,
        double stopLoss,
        double takeProfit,
        int typeTime,
        long expiration,
        double stopLimit = 0.0)
    {
        Prepare(Mql5TradeConstants.TradeActionModify, string.Empty);
        Request.Order = ticket;
        Request.Price = price;
        Request.StopLoss = stopLoss;
        Request.TakeProfit = takeProfit;
        Request.TypeTime = typeTime;
        Request.Expiration = expiration;
        Request.StopLimit = stopLimit;
        return Send();
    }

    /// <summary><c>OrderDelete</c>.</summary>
    public bool OrderDelete(ulong ticket)
    {
        Prepare(Mql5TradeConstants.TradeActionRemove, string.Empty);
        Request.Order = ticket;
        return Send();
    }

    // ------------------------------------------------------------------ internals

    private bool ModifySelected(string? symbol, double stopLoss, double takeProfit, ulong ticket = 0)
    {
        Prepare(Mql5TradeConstants.TradeActionSltp, Resolve(symbol));
        Request.StopLoss = stopLoss;
        Request.TakeProfit = takeProfit;
        Request.Position = ticket != 0
            ? ticket
            : (ulong)runtime.PositionGetInteger(Mql5TradeConstants.PositionTicket);
        return Send();
    }

    private bool CloseSelected(string? symbol, double volume, ulong slippage, ulong ticket = 0)
    {
        string resolved = Resolve(symbol);
        bool isLong = runtime.PositionGetInteger(Mql5TradeConstants.PositionType)
            == Mql5TradeConstants.PositionTypeBuy;

        Prepare(Mql5TradeConstants.TradeActionDeal, resolved);

        // Closing is an opposing deal: a long closes with a sell and a short with a buy, filled at
        // the price the opposing side of the book offers.
        Request.Type = isLong ? Mql5TradeConstants.OrderTypeSell : Mql5TradeConstants.OrderTypeBuy;
        Request.Price = runtime.SymbolInfoDouble(
            resolved,
            isLong ? Mql5TradeConstants.SymbolBid : Mql5TradeConstants.SymbolAsk);
        Request.Volume = volume;
        Request.Position = ticket != 0
            ? ticket
            : (ulong)runtime.PositionGetInteger(Mql5TradeConstants.PositionTicket);

        if (slippage != ulong.MaxValue)
        {
            Request.Deviation = slippage;
        }

        return Send();
    }

    private void Prepare(int action, string symbol)
    {
        Request.Clear();
        Request.Action = action;
        Request.Symbol = symbol;
        Request.Magic = magic;
        Request.Deviation = deviation;
        Request.TypeFilling = typeFilling;
    }

    private string Resolve(string? symbol) => string.IsNullOrEmpty(symbol) ? runtime.Symbol() : symbol;

    private bool Send()
    {
        bool ok = async
            ? runtime.OrderSendAsync(Request, Result)
            : runtime.OrderSend(Request, Result);

        if (!ok && logLevel > 0)
        {
            runtime.Print(
                "CTrade: request failed, retcode ",
                Result.Retcode,
                " ",
                Mql5TradeConstants.Describe(Result.Retcode));
        }

        return ok;
    }

    /// <summary>Records a failure this object detected before reaching the server.</summary>
    /// <remarks>
    /// The shipped class leaves the previous result in place when it refuses to send, which makes
    /// <c>ResultRetcode()</c> report the outcome of an unrelated earlier Request. Clearing and
    /// stamping the reason keeps the accessor answering about the call that just happened.
    /// </remarks>
    private bool Reject(uint retcode, string reason)
    {
        Result.Clear();
        Result.Retcode = retcode;
        Result.Comment = reason;

        if (logLevel > 0)
        {
            runtime.Print("CTrade: ", reason);
        }

        return false;
    }
}
