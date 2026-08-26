namespace YO4X.Mql5.Runtime;

/// <summary>
/// MQL5 trading, position and order functions. Every one is <b>EngineBound</b>.
///
/// MQL5 has exactly one way to change trading state: fill in an
/// <see cref="Mql5TradeRequest"/> and hand it to <see cref="IMql5Runtime.OrderSend(Mql5TradeRequest, Mql5TradeResult)"/>.
/// There is no <c>OrderClose</c>, no <c>OrderModify</c> and no <c>OrderDelete</c> -
/// those are MQL4 carry-overs that the corpus still calls, and they are absent here
/// deliberately so that a conversion which emits one fails to compile rather than
/// binding to something invented.
///
/// The same goes for <see cref="IMql5Runtime.OrderSelect"/>: MQL5 selects an order by
/// ticket, taking one argument. The MQL4 <c>(index, select, pool)</c> triple is a
/// dialect error for the converter to diagnose, not a signature to add.
///
/// One asymmetry worth remembering when reading converted code:
/// <c>PositionsTotal</c> counts open positions while <c>OrdersTotal</c> counts pending
/// orders, and the two are separate collections in MQL5, unlike MQL4 where they were
/// one.
/// </summary>
public partial interface IMql5Runtime
{
    /// <summary>
    /// MQL5 <c>OrderSend</c>, filling <paramref name="result"/> in place as MQL5's
    /// by-reference structure does. EngineBound.
    /// </summary>
    bool OrderSend(Mql5TradeRequest request, Mql5TradeResult result);

    /// <summary>MQL5 <c>OrderSend</c>, returning a fresh result. EngineBound.</summary>
    bool OrderSend(Mql5TradeRequest request, out Mql5TradeResult result);

    /// <summary>MQL5 <c>OrderSendAsync</c>. EngineBound.</summary>
    bool OrderSendAsync(Mql5TradeRequest request, Mql5TradeResult result);

    /// <summary>MQL5 <c>OrderCheck</c>. EngineBound.</summary>
    bool OrderCheck(Mql5TradeRequest request, Mql5TradeCheckResult result);

    /// <summary>MQL5 <c>OrderCalcMargin</c>. EngineBound.</summary>
    bool OrderCalcMargin(int action, string? symbol, double volume, double price, out double margin);

    /// <summary>MQL5 <c>OrderCalcProfit</c>. EngineBound.</summary>
    bool OrderCalcProfit(int action, string? symbol, double volume, double priceOpen, double priceClose, out double profit);

    /// <summary>MQL5 <c>PositionsTotal</c>: open positions, not pending orders. EngineBound.</summary>
    int PositionsTotal();

    /// <summary>MQL5 <c>PositionGetSymbol</c>. Selects the position as a side effect, as MQL5 does. EngineBound.</summary>
    string PositionGetSymbol(int index);

    /// <summary>MQL5 <c>PositionSelect</c>. EngineBound.</summary>
    bool PositionSelect(string? symbol);

    /// <summary>MQL5 <c>PositionSelectByTicket</c>. EngineBound.</summary>
    bool PositionSelectByTicket(ulong ticket);

    /// <summary>MQL5 <c>PositionGetTicket</c>. Selects the position as a side effect, as MQL5 does. EngineBound.</summary>
    ulong PositionGetTicket(int index);

    /// <summary>MQL5 <c>PositionGetDouble</c>, direct-return form. EngineBound.</summary>
    double PositionGetDouble(int propertyId);

    /// <summary>MQL5 <c>PositionGetDouble</c>, out-parameter form. EngineBound.</summary>
    bool PositionGetDouble(int propertyId, out double value);

    /// <summary>MQL5 <c>PositionGetInteger</c>, direct-return form. EngineBound.</summary>
    long PositionGetInteger(int propertyId);

    /// <summary>MQL5 <c>PositionGetInteger</c>, out-parameter form. EngineBound.</summary>
    bool PositionGetInteger(int propertyId, out long value);

    /// <summary>MQL5 <c>PositionGetString</c>, direct-return form. EngineBound.</summary>
    string PositionGetString(int propertyId);

    /// <summary>MQL5 <c>PositionGetString</c>, out-parameter form. EngineBound.</summary>
    bool PositionGetString(int propertyId, out string value);

    /// <summary>MQL5 <c>OrdersTotal</c>: pending orders, not open positions. EngineBound.</summary>
    int OrdersTotal();

    /// <summary>MQL5 <c>OrderGetTicket</c>. Selects the order as a side effect, as MQL5 does. EngineBound.</summary>
    ulong OrderGetTicket(int index);

    /// <summary>
    /// MQL5 <c>OrderSelect</c>, which selects <b>by ticket</b>. MQL4's
    /// <c>(index, select, pool)</c> form does not exist in MQL5 and is not offered here.
    /// EngineBound.
    /// </summary>
    bool OrderSelect(ulong ticket);

    /// <summary>MQL5 <c>OrderGetDouble</c>, direct-return form. EngineBound.</summary>
    double OrderGetDouble(int propertyId);

    /// <summary>MQL5 <c>OrderGetDouble</c>, out-parameter form. EngineBound.</summary>
    bool OrderGetDouble(int propertyId, out double value);

    /// <summary>MQL5 <c>OrderGetInteger</c>, direct-return form. EngineBound.</summary>
    long OrderGetInteger(int propertyId);

    /// <summary>MQL5 <c>OrderGetInteger</c>, out-parameter form. EngineBound.</summary>
    bool OrderGetInteger(int propertyId, out long value);

    /// <summary>MQL5 <c>OrderGetString</c>, direct-return form. EngineBound.</summary>
    string OrderGetString(int propertyId);

    /// <summary>MQL5 <c>OrderGetString</c>, out-parameter form. EngineBound.</summary>
    bool OrderGetString(int propertyId, out string value);

    /// <summary>MQL5 <c>HistorySelect</c>. EngineBound.</summary>
    bool HistorySelect(long fromDate, long toDate);

    /// <summary>MQL5 <c>HistorySelectByPosition</c>. EngineBound.</summary>
    bool HistorySelectByPosition(ulong positionId);

    /// <summary>MQL5 <c>HistoryOrderSelect</c>. EngineBound.</summary>
    bool HistoryOrderSelect(ulong ticket);

    /// <summary>MQL5 <c>HistoryOrdersTotal</c>. EngineBound.</summary>
    int HistoryOrdersTotal();

    /// <summary>MQL5 <c>HistoryOrderGetTicket</c>. EngineBound.</summary>
    ulong HistoryOrderGetTicket(int index);

    /// <summary>MQL5 <c>HistoryOrderGetDouble</c>, direct-return form. EngineBound.</summary>
    double HistoryOrderGetDouble(ulong ticket, int propertyId);

    /// <summary>MQL5 <c>HistoryOrderGetDouble</c>, out-parameter form. EngineBound.</summary>
    bool HistoryOrderGetDouble(ulong ticket, int propertyId, out double value);

    /// <summary>MQL5 <c>HistoryOrderGetInteger</c>, direct-return form. EngineBound.</summary>
    long HistoryOrderGetInteger(ulong ticket, int propertyId);

    /// <summary>MQL5 <c>HistoryOrderGetInteger</c>, out-parameter form. EngineBound.</summary>
    bool HistoryOrderGetInteger(ulong ticket, int propertyId, out long value);

    /// <summary>MQL5 <c>HistoryOrderGetString</c>, direct-return form. EngineBound.</summary>
    string HistoryOrderGetString(ulong ticket, int propertyId);

    /// <summary>MQL5 <c>HistoryOrderGetString</c>, out-parameter form. EngineBound.</summary>
    bool HistoryOrderGetString(ulong ticket, int propertyId, out string value);

    /// <summary>MQL5 <c>HistoryDealSelect</c>. EngineBound.</summary>
    bool HistoryDealSelect(ulong ticket);

    /// <summary>MQL5 <c>HistoryDealsTotal</c>. EngineBound.</summary>
    int HistoryDealsTotal();

    /// <summary>MQL5 <c>HistoryDealGetTicket</c>. EngineBound.</summary>
    ulong HistoryDealGetTicket(int index);

    /// <summary>MQL5 <c>HistoryDealGetDouble</c>, direct-return form. EngineBound.</summary>
    double HistoryDealGetDouble(ulong ticket, int propertyId);

    /// <summary>MQL5 <c>HistoryDealGetDouble</c>, out-parameter form. EngineBound.</summary>
    bool HistoryDealGetDouble(ulong ticket, int propertyId, out double value);

    /// <summary>MQL5 <c>HistoryDealGetInteger</c>, direct-return form. EngineBound.</summary>
    long HistoryDealGetInteger(ulong ticket, int propertyId);

    /// <summary>MQL5 <c>HistoryDealGetInteger</c>, out-parameter form. EngineBound.</summary>
    bool HistoryDealGetInteger(ulong ticket, int propertyId, out long value);

    /// <summary>MQL5 <c>HistoryDealGetString</c>, direct-return form. EngineBound.</summary>
    string HistoryDealGetString(ulong ticket, int propertyId);

    /// <summary>MQL5 <c>HistoryDealGetString</c>, out-parameter form. EngineBound.</summary>
    bool HistoryDealGetString(ulong ticket, int propertyId, out string value);
}

public sealed partial class Mql5Runtime
{
    /// <inheritdoc />
    public bool OrderSend(Mql5TradeRequest request, Mql5TradeResult result)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        bool accepted = context.OrderSend(request, out Mql5TradeResult produced);
        CopyInto(produced, result);

        if (!accepted)
        {
            SetError(Mql5ErrorCodes.TradeSendFailed);
        }

        return accepted;
    }

    /// <inheritdoc />
    public bool OrderSend(Mql5TradeRequest request, out Mql5TradeResult result)
    {
        ArgumentNullException.ThrowIfNull(request);

        bool accepted = context.OrderSend(request, out result);
        result ??= new Mql5TradeResult();

        if (!accepted)
        {
            SetError(Mql5ErrorCodes.TradeSendFailed);
        }

        return accepted;
    }

    /// <inheritdoc />
    public bool OrderSendAsync(Mql5TradeRequest request, Mql5TradeResult result)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        bool accepted = context.OrderSendAsync(request, out Mql5TradeResult produced);
        CopyInto(produced, result);

        if (!accepted)
        {
            SetError(Mql5ErrorCodes.TradeSendFailed);
        }

        return accepted;
    }

    /// <inheritdoc />
    public bool OrderCheck(Mql5TradeRequest request, Mql5TradeCheckResult result)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        bool ok = context.OrderCheck(request, out Mql5TradeCheckResult produced);
        if (produced is not null)
        {
            result.Retcode = produced.Retcode;
            result.Balance = produced.Balance;
            result.Equity = produced.Equity;
            result.Profit = produced.Profit;
            result.Margin = produced.Margin;
            result.MarginFree = produced.MarginFree;
            result.MarginLevel = produced.MarginLevel;
            result.Comment = produced.Comment;
        }

        return ok;
    }

    /// <inheritdoc />
    public bool OrderCalcMargin(int action, string? symbol, double volume, double price, out double margin)
        => context.OrderCalcMargin(action, Resolve(symbol), volume, price, out margin);

    /// <inheritdoc />
    public bool OrderCalcProfit(int action, string? symbol, double volume, double priceOpen, double priceClose, out double profit)
        => context.OrderCalcProfit(action, Resolve(symbol), volume, priceOpen, priceClose, out profit);

    /// <inheritdoc />
    public int PositionsTotal() => context.PositionsTotal();

    /// <inheritdoc />
    public string PositionGetSymbol(int index) => context.PositionGetSymbol(index);

    /// <inheritdoc />
    public bool PositionSelect(string? symbol)
    {
        bool selected = context.PositionSelect(Resolve(symbol));
        if (!selected)
        {
            SetError(Mql5ErrorCodes.TradePositionNotFound);
        }

        return selected;
    }

    /// <inheritdoc />
    public bool PositionSelectByTicket(ulong ticket)
    {
        bool selected = context.PositionSelectByTicket(ticket);
        if (!selected)
        {
            SetError(Mql5ErrorCodes.TradePositionNotFound);
        }

        return selected;
    }

    /// <inheritdoc />
    public ulong PositionGetTicket(int index) => context.PositionGetTicket(index);

    /// <inheritdoc />
    public double PositionGetDouble(int propertyId) => context.PositionGetDouble(propertyId);

    /// <inheritdoc />
    public bool PositionGetDouble(int propertyId, out double value)
    {
        value = context.PositionGetDouble(propertyId);
        return true;
    }

    /// <inheritdoc />
    public long PositionGetInteger(int propertyId) => context.PositionGetInteger(propertyId);

    /// <inheritdoc />
    public bool PositionGetInteger(int propertyId, out long value)
    {
        value = context.PositionGetInteger(propertyId);
        return true;
    }

    /// <inheritdoc />
    public string PositionGetString(int propertyId) => context.PositionGetString(propertyId);

    /// <inheritdoc />
    public bool PositionGetString(int propertyId, out string value)
    {
        value = context.PositionGetString(propertyId);
        return true;
    }

    /// <inheritdoc />
    public int OrdersTotal() => context.OrdersTotal();

    /// <inheritdoc />
    public ulong OrderGetTicket(int index) => context.OrderGetTicket(index);

    /// <inheritdoc />
    public bool OrderSelect(ulong ticket)
    {
        bool selected = context.OrderSelect(ticket);
        if (!selected)
        {
            SetError(Mql5ErrorCodes.TradeOrderNotFound);
        }

        return selected;
    }

    /// <inheritdoc />
    public double OrderGetDouble(int propertyId) => context.OrderGetDouble(propertyId);

    /// <inheritdoc />
    public bool OrderGetDouble(int propertyId, out double value)
    {
        value = context.OrderGetDouble(propertyId);
        return true;
    }

    /// <inheritdoc />
    public long OrderGetInteger(int propertyId) => context.OrderGetInteger(propertyId);

    /// <inheritdoc />
    public bool OrderGetInteger(int propertyId, out long value)
    {
        value = context.OrderGetInteger(propertyId);
        return true;
    }

    /// <inheritdoc />
    public string OrderGetString(int propertyId) => context.OrderGetString(propertyId);

    /// <inheritdoc />
    public bool OrderGetString(int propertyId, out string value)
    {
        value = context.OrderGetString(propertyId);
        return true;
    }

    /// <inheritdoc />
    public bool HistorySelect(long fromDate, long toDate) => context.HistorySelect(fromDate, toDate);

    /// <inheritdoc />
    public bool HistorySelectByPosition(ulong positionId) => context.HistorySelectByPosition(positionId);

    /// <inheritdoc />
    public bool HistoryOrderSelect(ulong ticket)
    {
        bool selected = context.HistoryOrderSelect(ticket);
        if (!selected)
        {
            SetError(Mql5ErrorCodes.TradeOrderNotFound);
        }

        return selected;
    }

    /// <inheritdoc />
    public int HistoryOrdersTotal() => context.HistoryOrdersTotal();

    /// <inheritdoc />
    public ulong HistoryOrderGetTicket(int index) => context.HistoryOrderGetTicket(index);

    /// <inheritdoc />
    public double HistoryOrderGetDouble(ulong ticket, int propertyId) => context.HistoryOrderGetDouble(ticket, propertyId);

    /// <inheritdoc />
    public bool HistoryOrderGetDouble(ulong ticket, int propertyId, out double value)
    {
        value = context.HistoryOrderGetDouble(ticket, propertyId);
        return true;
    }

    /// <inheritdoc />
    public long HistoryOrderGetInteger(ulong ticket, int propertyId) => context.HistoryOrderGetInteger(ticket, propertyId);

    /// <inheritdoc />
    public bool HistoryOrderGetInteger(ulong ticket, int propertyId, out long value)
    {
        value = context.HistoryOrderGetInteger(ticket, propertyId);
        return true;
    }

    /// <inheritdoc />
    public string HistoryOrderGetString(ulong ticket, int propertyId) => context.HistoryOrderGetString(ticket, propertyId);

    /// <inheritdoc />
    public bool HistoryOrderGetString(ulong ticket, int propertyId, out string value)
    {
        value = context.HistoryOrderGetString(ticket, propertyId);
        return true;
    }

    /// <inheritdoc />
    public bool HistoryDealSelect(ulong ticket)
    {
        bool selected = context.HistoryDealSelect(ticket);
        if (!selected)
        {
            SetError(Mql5ErrorCodes.TradeDealNotFound);
        }

        return selected;
    }

    /// <inheritdoc />
    public int HistoryDealsTotal() => context.HistoryDealsTotal();

    /// <inheritdoc />
    public ulong HistoryDealGetTicket(int index) => context.HistoryDealGetTicket(index);

    /// <inheritdoc />
    public double HistoryDealGetDouble(ulong ticket, int propertyId) => context.HistoryDealGetDouble(ticket, propertyId);

    /// <inheritdoc />
    public bool HistoryDealGetDouble(ulong ticket, int propertyId, out double value)
    {
        value = context.HistoryDealGetDouble(ticket, propertyId);
        return true;
    }

    /// <inheritdoc />
    public long HistoryDealGetInteger(ulong ticket, int propertyId) => context.HistoryDealGetInteger(ticket, propertyId);

    /// <inheritdoc />
    public bool HistoryDealGetInteger(ulong ticket, int propertyId, out long value)
    {
        value = context.HistoryDealGetInteger(ticket, propertyId);
        return true;
    }

    /// <inheritdoc />
    public string HistoryDealGetString(ulong ticket, int propertyId) => context.HistoryDealGetString(ticket, propertyId);

    /// <inheritdoc />
    public bool HistoryDealGetString(ulong ticket, int propertyId, out string value)
    {
        value = context.HistoryDealGetString(ticket, propertyId);
        return true;
    }

    private static void CopyInto(Mql5TradeResult? source, Mql5TradeResult target)
    {
        if (source is null)
        {
            target.Clear();
            return;
        }

        target.Retcode = source.Retcode;
        target.Deal = source.Deal;
        target.Order = source.Order;
        target.Volume = source.Volume;
        target.Price = source.Price;
        target.Bid = source.Bid;
        target.Ask = source.Ask;
        target.Comment = source.Comment;
        target.RequestId = source.RequestId;
        target.RetcodeExternal = source.RetcodeExternal;
    }
}
