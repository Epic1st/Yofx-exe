using YO4X.Mql5.Runtime;

namespace YO4X.Mql5.Runtime.Tests;

/// <summary>
/// A deterministic stand-in for the engine.
///
/// It implements only the members <see cref="IMql5MarketContext"/> declares abstract,
/// which is the point of the test: an engine that supplies the required surface and
/// nothing else must still let a strategy run, with every remaining built-in answering
/// MQL5's documented "no data" value rather than throwing.
/// </summary>
internal sealed class FakeMarketContext : IMql5MarketContext
{
    public string Symbol { get; set; } = "EURUSD";

    public double Point { get; set; } = 0.00001;

    public int Digits { get; set; } = 5;

    public DateTime TimeCurrent { get; set; } = new(2024, 3, 15, 12, 30, 45, DateTimeKind.Utc);

    public Dictionary<int, double> SymbolDoubles { get; } = [];

    public Dictionary<int, long> SymbolIntegers { get; } = [];

    public Dictionary<int, double> AccountDoubles { get; } = [];

    public Dictionary<int, double> PositionDoubles { get; } = [];

    public Dictionary<int, long> PositionIntegers { get; } = [];

    public List<string> SelectedSymbols { get; } = [];

    public List<Mql5TradeRequest> SentRequests { get; } = [];

    public List<string> HandleRequests { get; } = [];

    public double[] BufferValues { get; set; } = [];

    public int OpenPositions { get; set; }

    public uint NextRetcode { get; set; } = (uint)Mql5Constants.TradeRetcode.Done;

    public bool AcceptOrders { get; set; } = true;

    private int nextHandle = 100;

    public double SymbolInfoDouble(string symbol, int propertyId)
        => SymbolDoubles.TryGetValue(propertyId, out double value) ? value : 0;

    public long SymbolInfoInteger(string symbol, int propertyId)
        => SymbolIntegers.TryGetValue(propertyId, out long value) ? value : 0;

    public double AccountInfoDouble(int propertyId)
        => AccountDoubles.TryGetValue(propertyId, out double value) ? value : 0;

    public int PositionsTotal() => OpenPositions;

    public bool PositionSelect(string symbol)
    {
        SelectedSymbols.Add(symbol);
        return OpenPositions > 0;
    }

    public double PositionGetDouble(int propertyId)
        => PositionDoubles.TryGetValue(propertyId, out double value) ? value : 0;

    public long PositionGetInteger(int propertyId)
        => PositionIntegers.TryGetValue(propertyId, out long value) ? value : 0;

    public bool OrderSend(Mql5TradeRequest request, out Mql5TradeResult result)
    {
        SentRequests.Add(request);
        result = new Mql5TradeResult
        {
            Retcode = NextRetcode,
            Order = (ulong)SentRequests.Count,
            Volume = request.Volume,
            Price = request.Price
        };

        return AcceptOrders;
    }

    public int IndicatorHandle(string name, params object[] parameters)
    {
        HandleRequests.Add(name + ":" + string.Join(",", parameters));
        return nextHandle++;
    }

    public int CopyBuffer(int handle, int bufferNum, int start, int count, double[] target)
    {
        int written = Math.Min(count, BufferValues.Length);
        Array.Copy(BufferValues, 0, target, 0, written);
        return written;
    }

    // The one optional member the tests override, so that the series-reversal contract
    // can be exercised: the engine fills oldest-first and the runtime reverses for a
    // target the strategy flagged with ArraySetAsSeries.
    public int CopyClose(string symbol, int timeframe, Mql5CopyRange range, ref double[] target)
    {
        double[] source = [1.0, 2.0, 3.0, 4.0];
        if (target.Length < source.Length)
        {
            Array.Resize(ref target, source.Length);
        }

        Array.Copy(source, target, source.Length);
        return source.Length;
    }
}
