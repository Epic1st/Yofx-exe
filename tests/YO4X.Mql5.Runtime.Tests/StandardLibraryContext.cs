using YO4X.Mql5.Runtime;

namespace YO4X.Mql5.Runtime.Tests;

/// <summary>
/// A market context for the standard library classes, which read parts of the surface
/// <see cref="FakeMarketContext"/> deliberately leaves at its defaults.
///
/// <c>COrderInfo</c>, <c>CDealInfo</c> and <c>CHistoryOrderInfo</c> read pending orders, history
/// and strings, and <c>CAccountInfo</c>'s margin checks call <c>OrderCalcMargin</c>. Overriding
/// those on the other fake would blunt the claim it exists to make - that an engine implementing
/// only the required members still runs a strategy - so the surface is filled in here instead.
/// </summary>
internal sealed class StandardLibraryContext : IMql5MarketContext
{
    public string Symbol { get; set; } = "EURUSD";

    public double Point { get; set; } = 0.00001;

    public int Digits { get; set; } = 5;

    public DateTime TimeCurrent { get; set; } = new(2024, 3, 15, 12, 30, 45, DateTimeKind.Utc);

    public Dictionary<int, double> SymbolDoubles { get; } = [];

    public Dictionary<int, long> SymbolIntegers { get; } = [];

    public Dictionary<int, string> SymbolStrings { get; } = [];

    public Dictionary<int, double> AccountDoubles { get; } = [];

    public Dictionary<int, long> AccountIntegers { get; } = [];

    public Dictionary<int, string> AccountStrings { get; } = [];

    public Dictionary<int, double> PositionDoubles { get; } = [];

    public Dictionary<int, long> PositionIntegers { get; } = [];

    public Dictionary<int, string> PositionStrings { get; } = [];

    public Dictionary<int, double> OrderDoubles { get; } = [];

    public Dictionary<int, long> OrderIntegers { get; } = [];

    public Dictionary<int, string> OrderStrings { get; } = [];

    public Dictionary<int, double> DealDoubles { get; } = [];

    public Dictionary<int, long> DealIntegers { get; } = [];

    public Dictionary<int, string> DealStrings { get; } = [];

    public Dictionary<int, double> HistoryOrderDoubles { get; } = [];

    public Dictionary<int, long> HistoryOrderIntegers { get; } = [];

    public Dictionary<int, string> HistoryOrderStrings { get; } = [];

    /// <summary>Symbols in the order <c>PositionGetSymbol</c> walks them.</summary>
    public List<string> PositionSymbols { get; } = [];

    /// <summary>The magic number each of <see cref="PositionSymbols"/> carries.</summary>
    public List<long> PositionMagics { get; } = [];

    /// <summary>Deal tickets in the order <c>HistoryDealGetTicket</c> hands them out.</summary>
    public List<ulong> DealTickets { get; } = [];

    /// <summary>The ticket the deal getters were last asked about.</summary>
    public ulong LastDealTicketRead { get; private set; }

    /// <summary>The margin <see cref="OrderCalcMargin"/> reports, or null to refuse the calculation.</summary>
    public double? Margin { get; set; }

    /// <summary>The profit <see cref="OrderCalcProfit"/> reports, or null to refuse the calculation.</summary>
    public double? Profit { get; set; }

    /// <summary>The arguments of the last <c>OrderCalcMargin</c> call.</summary>
    public (int OrderType, string Symbol, double Volume, double Price)? MarginRequest { get; private set; }

    /// <summary>Symbols passed to <c>SymbolSelect</c>, with the flag each was given.</summary>
    public List<(string Symbol, bool Select)> SymbolSelections { get; } = [];

    public int OpenPositions { get; set; }

    public double SymbolInfoDouble(string symbol, int propertyId)
        => SymbolDoubles.TryGetValue(propertyId, out double value) ? value : 0;

    public long SymbolInfoInteger(string symbol, int propertyId)
        => SymbolIntegers.TryGetValue(propertyId, out long value) ? value : 0;

    public string SymbolInfoString(string symbol, int propertyId)
        => SymbolStrings.TryGetValue(propertyId, out string? value) ? value : string.Empty;

    public bool SymbolSelect(string symbol, bool select)
    {
        SymbolSelections.Add((symbol, select));
        return true;
    }

    public double AccountInfoDouble(int propertyId)
        => AccountDoubles.TryGetValue(propertyId, out double value) ? value : 0;

    public long AccountInfoInteger(int propertyId)
        => AccountIntegers.TryGetValue(propertyId, out long value) ? value : 0;

    public string AccountInfoString(int propertyId)
        => AccountStrings.TryGetValue(propertyId, out string? value) ? value : string.Empty;

    public int PositionsTotal() => OpenPositions;

    public bool PositionSelect(string symbol) => OpenPositions > 0;

    public double PositionGetDouble(int propertyId)
        => PositionDoubles.TryGetValue(propertyId, out double value) ? value : 0;

    public long PositionGetInteger(int propertyId)
        => PositionIntegers.TryGetValue(propertyId, out long value) ? value : 0;

    public string PositionGetString(int propertyId)
        => PositionStrings.TryGetValue(propertyId, out string? value) ? value : string.Empty;

    // Selecting as a side effect is what MQL5 does, and CPositionInfo::SelectByMagic depends on
    // it: the magic it compares is read from whichever position this call just selected.
    public string PositionGetSymbol(int index)
    {
        if (index < 0 || index >= PositionSymbols.Count)
        {
            return string.Empty;
        }

        PositionIntegers[Mql5TradeConstants.PositionMagic] = PositionMagics[index];
        return PositionSymbols[index];
    }

    public double OrderGetDouble(int propertyId)
        => OrderDoubles.TryGetValue(propertyId, out double value) ? value : 0;

    public long OrderGetInteger(int propertyId)
        => OrderIntegers.TryGetValue(propertyId, out long value) ? value : 0;

    public string OrderGetString(int propertyId)
        => OrderStrings.TryGetValue(propertyId, out string? value) ? value : string.Empty;

    public ulong HistoryDealGetTicket(int index)
        => index >= 0 && index < DealTickets.Count ? DealTickets[index] : 0;

    public double HistoryDealGetDouble(ulong ticket, int propertyId)
    {
        LastDealTicketRead = ticket;
        return DealDoubles.TryGetValue(propertyId, out double value) ? value : 0;
    }

    public long HistoryDealGetInteger(ulong ticket, int propertyId)
    {
        LastDealTicketRead = ticket;
        return DealIntegers.TryGetValue(propertyId, out long value) ? value : 0;
    }

    public string HistoryDealGetString(ulong ticket, int propertyId)
    {
        LastDealTicketRead = ticket;
        return DealStrings.TryGetValue(propertyId, out string? value) ? value : string.Empty;
    }

    public double HistoryOrderGetDouble(ulong ticket, int propertyId)
        => HistoryOrderDoubles.TryGetValue(propertyId, out double value) ? value : 0;

    public long HistoryOrderGetInteger(ulong ticket, int propertyId)
        => HistoryOrderIntegers.TryGetValue(propertyId, out long value) ? value : 0;

    public string HistoryOrderGetString(ulong ticket, int propertyId)
        => HistoryOrderStrings.TryGetValue(propertyId, out string? value) ? value : string.Empty;

    public bool OrderCalcMargin(int orderType, string symbol, double volume, double price, out double margin)
    {
        MarginRequest = (orderType, symbol, volume, price);
        margin = Margin ?? 0;
        return Margin.HasValue;
    }

    public bool OrderCalcProfit(int orderType, string symbol, double volume, double priceOpen, double priceClose, out double profit)
    {
        profit = Profit ?? 0;
        return Profit.HasValue;
    }

    public bool OrderSend(Mql5TradeRequest request, out Mql5TradeResult result)
    {
        result = new Mql5TradeResult { Retcode = Mql5TradeConstants.RetcodeDone };
        return true;
    }

    public int IndicatorHandle(string name, params object[] parameters) => Mql5Constants.InvalidHandle;

    public int CopyBuffer(int handle, int bufferNum, int start, int count, double[] target) => -1;
}
