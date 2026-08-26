namespace YO4X.Mql5.Runtime;

/// <summary>
/// The MQL5 standard library <c>CHistoryOrderInfo</c>, from <c>&lt;Trade/HistoryOrderInfo.mqh&gt;</c>.
/// </summary>
/// <remarks>
/// Reads one order out of the history selection a prior <c>HistorySelect</c> established.
/// </remarks>
public sealed class Mql5HistoryOrderInfo(IMql5Runtime runtime)
{
    private ulong ticket;

    /// <summary><c>SelectByIndex</c>.</summary>
    public bool SelectByIndex(int index)
    {
        ticket = runtime.HistoryOrderGetTicket(index);
        return ticket != 0;
    }

    /// <summary><c>Ticket</c>: reads the selected ticket.</summary>
    public ulong Ticket() => ticket;

    /// <summary><c>Ticket</c>: selects an order by ticket.</summary>
    public void Ticket(ulong value) => ticket = value;

    /// <summary><c>OrderType</c>.</summary>
    public int OrderType() => (int)runtime.HistoryOrderGetInteger(ticket, Mql5TradeConstants.OrderTypeProperty);

    /// <summary><c>Type</c>, the alias the shipped class also offers.</summary>
    public int Type() => OrderType();

    /// <summary><c>Magic</c>.</summary>
    public long Magic() => runtime.HistoryOrderGetInteger(ticket, Mql5TradeConstants.OrderMagic);

    /// <summary><c>TypeDescription</c>.</summary>
    public string TypeDescription() => Mql5TradeConstants.DescribeOrderType(OrderType());

    /// <summary><c>State</c>: how the order ended — filled, cancelled, rejected or expired.</summary>
    public int State() => (int)runtime.HistoryOrderGetInteger(ticket, Mql5TradeConstants.OrderState);

    /// <summary><c>TypeFilling</c>.</summary>
    public int TypeFilling()
        => (int)runtime.HistoryOrderGetInteger(ticket, Mql5TradeConstants.OrderTypeFillingProperty);

    /// <summary><c>TypeTime</c>.</summary>
    public int TypeTime()
        => (int)runtime.HistoryOrderGetInteger(ticket, Mql5TradeConstants.OrderTypeTimeProperty);

    /// <summary><c>TimeSetup</c>, as seconds since 1970.</summary>
    public long TimeSetup() => runtime.HistoryOrderGetInteger(ticket, Mql5TradeConstants.OrderTimeSetup);

    /// <summary><c>TimeSetupMsc</c>, in milliseconds since 1970.</summary>
    public long TimeSetupMsc()
        => runtime.HistoryOrderGetInteger(ticket, Mql5TradeConstants.OrderTimeSetupMsc);

    /// <summary><c>TimeDone</c>, as seconds since 1970.</summary>
    public long TimeDone() => runtime.HistoryOrderGetInteger(ticket, Mql5TradeConstants.OrderTimeDone);

    /// <summary><c>TimeDoneMsc</c>, in milliseconds since 1970.</summary>
    public long TimeDoneMsc()
        => runtime.HistoryOrderGetInteger(ticket, Mql5TradeConstants.OrderTimeDoneMsc);

    /// <summary><c>TimeExpiration</c>, as seconds since 1970.</summary>
    public long TimeExpiration()
        => runtime.HistoryOrderGetInteger(ticket, Mql5TradeConstants.OrderTimeExpiration);

    /// <summary><c>PositionId</c>.</summary>
    public long PositionId() => runtime.HistoryOrderGetInteger(ticket, Mql5TradeConstants.OrderPositionId);

    /// <summary><c>PositionById</c>: the opposite position in a close-by.</summary>
    public long PositionById()
        => runtime.HistoryOrderGetInteger(ticket, Mql5TradeConstants.OrderPositionById);

    /// <summary><c>VolumeInitial</c>.</summary>
    public double VolumeInitial()
        => runtime.HistoryOrderGetDouble(ticket, Mql5TradeConstants.OrderVolumeInitial);

    /// <summary><c>VolumeCurrent</c>: what was left unfilled when the order ended.</summary>
    public double VolumeCurrent()
        => runtime.HistoryOrderGetDouble(ticket, Mql5TradeConstants.OrderVolumeCurrent);

    /// <summary><c>PriceOpen</c>.</summary>
    public double PriceOpen() => runtime.HistoryOrderGetDouble(ticket, Mql5TradeConstants.OrderPriceOpen);

    /// <summary><c>PriceCurrent</c>.</summary>
    public double PriceCurrent()
        => runtime.HistoryOrderGetDouble(ticket, Mql5TradeConstants.OrderPriceCurrent);

    /// <summary><c>PriceStopLimit</c>.</summary>
    public double PriceStopLimit()
        => runtime.HistoryOrderGetDouble(ticket, Mql5TradeConstants.OrderPriceStopLimit);

    /// <summary><c>StopLoss</c>.</summary>
    public double StopLoss() => runtime.HistoryOrderGetDouble(ticket, Mql5TradeConstants.OrderStopLoss);

    /// <summary><c>TakeProfit</c>.</summary>
    public double TakeProfit() => runtime.HistoryOrderGetDouble(ticket, Mql5TradeConstants.OrderTakeProfit);

    /// <summary><c>Symbol</c>.</summary>
    public string Symbol() => runtime.HistoryOrderGetString(ticket, Mql5TradeConstants.OrderSymbol);

    /// <summary><c>Comment</c>.</summary>
    public string Comment() => runtime.HistoryOrderGetString(ticket, Mql5TradeConstants.OrderComment);

    /// <summary><c>ExternalId</c>.</summary>
    public string ExternalId()
        => runtime.HistoryOrderGetString(ticket, Mql5TradeConstants.OrderExternalId);
}
