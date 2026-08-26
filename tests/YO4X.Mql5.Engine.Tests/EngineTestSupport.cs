using YO4X.Mql5.Engine.Context;
using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Hosting;
using YO4X.Mql5.Engine.Trading;

namespace YO4X.Mql5.Engine.Tests;

/// <summary>Fixtures shared by the engine tests. Every price here is hand-checked.</summary>
internal static class EngineTestSupport
{
    internal static readonly DateTime Origin = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    internal static Mql5SymbolSpec Eurusd() => new()
    {
        Name = "EURUSD",
        Digits = 5,
        ContractSize = 100_000.0,
        VolumeMin = 0.01,
        VolumeMax = 500.0,
        VolumeStep = 0.01,
        StopsLevelPoints = 0,
    };

    internal static Mql5RunOptions Options(
        Mql5MarginMode mode = Mql5MarginMode.Netting,
        int spreadPoints = 10,
        int slippagePoints = 0,
        double commissionPerLot = 0.0,
        double deposit = 10_000.0,
        int leverage = 100,
        int maxOrdersPerTick = 32,
        int maxTicks = 1_000_000,
        double stopOutPercent = 50.0,
        bool closeAtEnd = true) => new()
        {
            Symbol = Eurusd(),
            InitialDeposit = deposit,
            DepositCurrency = "USD",
            Leverage = leverage,
            MarginMode = mode,
            SpreadPoints = spreadPoints,
            SlippagePoints = slippagePoints,
            CommissionPerLot = commissionPerLot,
            StopOutLevelPercent = stopOutPercent,
            MaxOrdersPerTick = maxOrdersPerTick,
            MaxTicks = maxTicks,
            CloseOpenPositionsAtEnd = closeAtEnd,
        };

    internal static Mql5Bar Flat(int index, double price, int spread = 10) =>
        new(Origin.AddHours(index), price, price, price, price, 100, spread);

    internal static Mql5Bar Bar(int index, double open, double high, double low, double close, int spread = 10) =>
        new(Origin.AddHours(index), open, high, low, close, 100, spread);

    /// <summary>
    /// A rising series: close of bar i is 1.10000 + i * 0.00010, every bar bullish and with a
    /// five-point wick either side so no stop can be touched accidentally.
    /// </summary>
    internal static List<Mql5Bar> Ramp(int count)
    {
        var bars = new List<Mql5Bar>(count);
        for (int index = 0; index < count; index++)
        {
            double close = Math.Round(1.10000 + (index * 0.00010), 5);
            double open = Math.Round(close - 0.00010, 5);
            double high = Math.Round(close + 0.00005, 5);
            double low = Math.Round(open - 0.00005, 5);
            bars.Add(Bar(index, open, high, low, close));
        }

        return bars;
    }

    internal static Mql5TradeRequest Market(Mql5OrderType type, double volume, double sl = 0.0, double tp = 0.0) => new()
    {
        Action = Mql5TradeAction.Deal,
        Symbol = "EURUSD",
        Type = type,
        Volume = volume,
        Sl = sl,
        Tp = tp,
    };

    internal static Mql5TradeRequest ClosePosition(long ticket, double volume = 0.0) => new()
    {
        Action = Mql5TradeAction.Deal,
        Symbol = "EURUSD",
        Position = ticket,
        Volume = volume,
    };

    internal static Mql5TradeRequest Pending(Mql5OrderType type, double volume, double price, double sl = 0.0, double tp = 0.0) => new()
    {
        Action = Mql5TradeAction.Pending,
        Symbol = "EURUSD",
        Type = type,
        Volume = volume,
        Price = price,
        Sl = sl,
        Tp = tp,
    };

    /// <summary>Primes a broker with one flat bar so a quote exists.</summary>
    internal static Mql5SimulatedBroker BrokerAt(Mql5RunOptions options, double price, int index = 0)
    {
        var broker = new Mql5SimulatedBroker(options);
        broker.ApplyBar(Flat(index, price, options.SpreadPoints));
        broker.BeginTick();
        return broker;
    }
}

/// <summary>Replays a fixed list of bars.</summary>
internal sealed class ListMarketFeed(string symbol, IEnumerable<Mql5Bar> bars) : IMql5MarketFeed
{
    private readonly List<Mql5Bar> bars = [.. bars];

    public string Symbol { get; } = symbol;

    public IEnumerable<Mql5Bar> ReadBars() => bars;
}

/// <summary>Runs a caller-supplied action on each tick, with the tick index.</summary>
internal sealed class ScriptedStrategy(Action<IMql5MarketContext, int> onTick) : IMql5Strategy
{
    private int tick;

    public int InitCalls { get; private set; }

    public int DeinitCalls { get; private set; }

    public int LastDeinitReason { get; private set; } = -1;

    public int Ticks => tick;

    public int OnInit(IMql5MarketContext context)
    {
        InitCalls++;
        return Mql5InitCode.Succeeded;
    }

    public void OnTick(IMql5MarketContext context)
    {
        onTick(context, tick);
        tick++;
    }

    public void OnDeinit(IMql5MarketContext context, int reason)
    {
        DeinitCalls++;
        LastDeinitReason = reason;
    }
}
