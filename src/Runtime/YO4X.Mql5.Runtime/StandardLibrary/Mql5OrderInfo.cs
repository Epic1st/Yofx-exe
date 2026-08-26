namespace YO4X.Mql5.Runtime;

/// <summary>
/// The MQL5 standard library <c>COrderInfo</c>, from <c>&lt;Trade/OrderInfo.mqh&gt;</c>.
/// </summary>
/// <remarks>
/// A reader over the currently selected pending order, on the same selected-state model as
/// <see cref="Mql5PositionInfo"/>.
/// </remarks>
public sealed class Mql5OrderInfo(IMql5Runtime runtime)
{
    // The snapshot StoreState takes. WRONG_VALUE for the two enumerated fields, so that a
    // CheckState with no prior StoreState reports a change instead of matching a never-seen order.
    private int storedType = Mql5Constants.WrongValue;
    private int storedState = Mql5Constants.WrongValue;
    private long storedExpiration;
    private double storedVolumeCurrent;
    private double storedPriceOpen;
    private double storedStopLoss;
    private double storedTakeProfit;

    /// <summary><c>Select</c>.</summary>
    public bool Select(ulong ticket) => runtime.OrderSelect(ticket);

    /// <summary><c>SelectByIndex</c>.</summary>
    public bool SelectByIndex(int index) => runtime.OrderGetTicket(index) != 0;

    /// <summary><c>Ticket</c>.</summary>
    public ulong Ticket() => (ulong)runtime.OrderGetInteger(Mql5TradeConstants.OrderTicket);

    /// <summary><c>OrderType</c>.</summary>
    public int OrderType() => (int)runtime.OrderGetInteger(Mql5TradeConstants.OrderTypeProperty);

    /// <summary><c>Type</c>, the alias the shipped class also offers.</summary>
    public int Type() => OrderType();

    /// <summary><c>Magic</c>.</summary>
    public long Magic() => runtime.OrderGetInteger(Mql5TradeConstants.OrderMagic);

    /// <summary><c>TypeDescription</c>.</summary>
    public string TypeDescription() => Mql5TradeConstants.DescribeOrderType(OrderType());

    /// <summary><c>State</c>: placed, partially filled, filled, cancelled and so on.</summary>
    public int State() => (int)runtime.OrderGetInteger(Mql5TradeConstants.OrderState);

    /// <summary><c>TypeFilling</c>.</summary>
    public int TypeFilling() => (int)runtime.OrderGetInteger(Mql5TradeConstants.OrderTypeFillingProperty);

    /// <summary><c>TypeTime</c>.</summary>
    public int TypeTime() => (int)runtime.OrderGetInteger(Mql5TradeConstants.OrderTypeTimeProperty);

    /// <summary><c>TimeSetup</c>, as seconds since 1970.</summary>
    public long TimeSetup() => runtime.OrderGetInteger(Mql5TradeConstants.OrderTimeSetup);

    /// <summary><c>TimeSetupMsc</c>, in milliseconds since 1970.</summary>
    public long TimeSetupMsc() => runtime.OrderGetInteger(Mql5TradeConstants.OrderTimeSetupMsc);

    /// <summary><c>TimeDone</c>, as seconds since 1970.</summary>
    public long TimeDone() => runtime.OrderGetInteger(Mql5TradeConstants.OrderTimeDone);

    /// <summary><c>TimeDoneMsc</c>, in milliseconds since 1970.</summary>
    public long TimeDoneMsc() => runtime.OrderGetInteger(Mql5TradeConstants.OrderTimeDoneMsc);

    /// <summary><c>TimeExpiration</c>, as seconds since 1970. Zero for a good-till-cancelled order.</summary>
    public long TimeExpiration() => runtime.OrderGetInteger(Mql5TradeConstants.OrderTimeExpiration);

    /// <summary><c>PositionId</c>: the position this order opened or will open.</summary>
    public long PositionId() => runtime.OrderGetInteger(Mql5TradeConstants.OrderPositionId);

    /// <summary><c>PositionById</c>: the opposite position in a close-by.</summary>
    public long PositionById() => runtime.OrderGetInteger(Mql5TradeConstants.OrderPositionById);

    /// <summary><c>VolumeInitial</c>: the volume the order was placed for.</summary>
    public double VolumeInitial() => runtime.OrderGetDouble(Mql5TradeConstants.OrderVolumeInitial);

    /// <summary><c>VolumeCurrent</c>: the volume still unfilled.</summary>
    public double VolumeCurrent() => runtime.OrderGetDouble(Mql5TradeConstants.OrderVolumeCurrent);

    /// <summary><c>PriceOpen</c>.</summary>
    public double PriceOpen() => runtime.OrderGetDouble(Mql5TradeConstants.OrderPriceOpen);

    /// <summary><c>PriceCurrent</c>.</summary>
    public double PriceCurrent() => runtime.OrderGetDouble(Mql5TradeConstants.OrderPriceCurrent);

    /// <summary><c>PriceStopLimit</c>: the limit price a stop-limit order places once triggered.</summary>
    public double PriceStopLimit() => runtime.OrderGetDouble(Mql5TradeConstants.OrderPriceStopLimit);

    /// <summary><c>StopLoss</c>.</summary>
    public double StopLoss() => runtime.OrderGetDouble(Mql5TradeConstants.OrderStopLoss);

    /// <summary><c>TakeProfit</c>.</summary>
    public double TakeProfit() => runtime.OrderGetDouble(Mql5TradeConstants.OrderTakeProfit);

    /// <summary><c>Symbol</c>.</summary>
    public string Symbol() => runtime.OrderGetString(Mql5TradeConstants.OrderSymbol);

    /// <summary><c>Comment</c>.</summary>
    public string Comment() => runtime.OrderGetString(Mql5TradeConstants.OrderComment);

    /// <summary><c>ExternalId</c>: the order's identifier on an external trading system.</summary>
    public string ExternalId() => runtime.OrderGetString(Mql5TradeConstants.OrderExternalId);

    /// <summary><c>StoreState</c>: remembers the selected order's shape for <see cref="CheckState"/>.</summary>
    public void StoreState()
    {
        storedType = OrderType();
        storedState = State();
        storedExpiration = TimeExpiration();
        storedVolumeCurrent = VolumeCurrent();
        storedPriceOpen = PriceOpen();
        storedStopLoss = StopLoss();
        storedTakeProfit = TakeProfit();
    }

    /// <summary><c>CheckState</c>: whether the order changed since <see cref="StoreState"/>.</summary>
    public bool CheckState()
        => storedType != OrderType()
            || storedState != State()
            || storedExpiration != TimeExpiration()
            || storedVolumeCurrent != VolumeCurrent()
            || storedPriceOpen != PriceOpen()
            || storedStopLoss != StopLoss()
            || storedTakeProfit != TakeProfit();
}
