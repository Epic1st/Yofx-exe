namespace YO4X.Mql5.Runtime;

/// <summary>
/// The MQL5 standard library <c>CDealInfo</c>, from <c>&lt;Trade/DealInfo.mqh&gt;</c>.
/// </summary>
/// <remarks>
/// Reads one deal out of the history selection a prior <c>HistorySelect</c> established. As with
/// the other readers, selection is global MQL5 state and is read through rather than copied.
/// </remarks>
public sealed class Mql5DealInfo(IMql5Runtime runtime)
{
    private ulong ticket;

    /// <summary><c>SelectByIndex</c>: selects the deal at an index within the current history.</summary>
    public bool SelectByIndex(int index)
    {
        ticket = runtime.HistoryDealGetTicket(index);
        return ticket != 0;
    }

    /// <summary><c>Ticket</c>.</summary>
    public ulong Ticket() => ticket;

    /// <summary><c>Ticket</c>: points this reader at a deal by ticket.</summary>
    /// <remarks>
    /// The shipped class stores the ticket and checks nothing. It is a setter, not a selection:
    /// the deal still has to be inside the history a prior <c>HistorySelect</c> established, and a
    /// ticket outside it reads back as zeroes rather than as a failure.
    /// </remarks>
    public void Ticket(ulong value) => ticket = value;

    /// <summary><c>DealType</c>.</summary>
    public int DealType() => (int)runtime.HistoryDealGetInteger(ticket, Mql5DealConstants.Type);

    /// <summary><c>Entry</c>.</summary>
    public int Entry() => (int)runtime.HistoryDealGetInteger(ticket, Mql5DealConstants.Entry);

    /// <summary><c>Magic</c>.</summary>
    public long Magic() => runtime.HistoryDealGetInteger(ticket, Mql5DealConstants.Magic);

    /// <summary><c>Time</c>, as seconds since 1970.</summary>
    public long Time() => runtime.HistoryDealGetInteger(ticket, Mql5DealConstants.Time);

    /// <summary><c>Volume</c>.</summary>
    public double Volume() => runtime.HistoryDealGetDouble(ticket, Mql5DealConstants.Volume);

    /// <summary><c>Price</c>.</summary>
    public double Price() => runtime.HistoryDealGetDouble(ticket, Mql5DealConstants.Price);

    /// <summary><c>Profit</c>.</summary>
    public double Profit() => runtime.HistoryDealGetDouble(ticket, Mql5DealConstants.Profit);

    /// <summary><c>Order</c>: the ticket of the order this deal came from.</summary>
    public long Order() => runtime.HistoryDealGetInteger(ticket, Mql5DealConstants.Order);

    /// <summary><c>PositionId</c>: the position this deal opened, changed or closed.</summary>
    public long PositionId() => runtime.HistoryDealGetInteger(ticket, Mql5DealConstants.PositionId);

    /// <summary><c>TimeMsc</c>, in milliseconds since 1970.</summary>
    public long TimeMsc() => runtime.HistoryDealGetInteger(ticket, Mql5DealConstants.TimeMsc);

    /// <summary><c>Commission</c>.</summary>
    public double Commission() => runtime.HistoryDealGetDouble(ticket, Mql5DealConstants.Commission);

    /// <summary><c>Swap</c>: the swap charged when this deal closed a position.</summary>
    public double Swap() => runtime.HistoryDealGetDouble(ticket, Mql5DealConstants.Swap);

    /// <summary><c>Symbol</c>.</summary>
    public string Symbol() => runtime.HistoryDealGetString(ticket, Mql5DealConstants.Symbol);

    /// <summary><c>Comment</c>.</summary>
    public string Comment() => runtime.HistoryDealGetString(ticket, Mql5DealConstants.Comment);

    /// <summary><c>ExternalId</c>: the deal's identifier on an external trading system.</summary>
    public string ExternalId() => runtime.HistoryDealGetString(ticket, Mql5DealConstants.ExternalId);
}

/// <summary>Deal property identifiers, measured from the MQL5 compiler.</summary>
public static class Mql5DealConstants
{
    /// <summary><c>DEAL_SYMBOL</c>.</summary>
    public const int Symbol = 0;

    /// <summary><c>DEAL_ORDER</c>.</summary>
    public const int Order = 1;

    /// <summary><c>DEAL_TIME</c>.</summary>
    public const int Time = 2;

    /// <summary><c>DEAL_TYPE</c>.</summary>
    public const int Type = 3;

    /// <summary><c>DEAL_ENTRY</c>.</summary>
    public const int Entry = 4;

    /// <summary><c>DEAL_VOLUME</c>.</summary>
    public const int Volume = 5;

    /// <summary><c>DEAL_PRICE</c>.</summary>
    public const int Price = 6;

    /// <summary><c>DEAL_COMMISSION</c>.</summary>
    public const int Commission = 7;

    /// <summary><c>DEAL_SWAP</c>.</summary>
    public const int Swap = 8;

    /// <summary><c>DEAL_PROFIT</c>.</summary>
    public const int Profit = 9;

    /// <summary><c>DEAL_COMMENT</c>.</summary>
    public const int Comment = 10;

    /// <summary><c>DEAL_MAGIC</c>.</summary>
    public const int Magic = 11;

    /// <summary><c>DEAL_POSITION_ID</c>.</summary>
    public const int PositionId = 12;

    /// <summary><c>DEAL_TIME_MSC</c>.</summary>
    public const int TimeMsc = 13;

    /// <summary><c>DEAL_EXTERNAL_ID</c>.</summary>
    public const int ExternalId = 14;

    /// <summary><c>DEAL_TICKET</c>.</summary>
    public const int Ticket = 15;
}
