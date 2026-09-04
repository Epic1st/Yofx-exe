using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Runtime;
using YO4X.Mt5.ConnectionProbe.Windows;

namespace YO4X.Mql5.Live;

/// <summary>
/// Presents a live broker session to a translated MQL5 strategy.
///
/// <para>
/// This is the same seam as the backtest bridge, pointed at a real account: bars come from the
/// live series rather than a replayed file, and <c>OrderSend</c> reaches a broker rather than a
/// simulator. Everything the strategy can see is what the broker reported; nothing is modelled.
/// </para>
///
/// <para>
/// The strategy cannot widen its own limits through this class. Volume, symbol, account type
/// and the operator enable file are enforced inside the trade client on every instruction, so
/// a strategy asking for ten lots is refused there — not negotiated down to something
/// acceptable, which would have it trading a position it never chose.
/// </para>
/// </summary>
public sealed class LiveBrokerContext : IMql5MarketContext, IMql5DelayContext
{
    private readonly LiveBarSeries series;
    private readonly IMt5TradeGateway broker;
    private readonly Action<string> journal;
    private readonly int digits;
    private readonly Mt5LiveSymbolSnapshot? symbolSpec;
    private readonly List<Mt5DemoOrderReceipt> open = [];
    private readonly List<Mt5DemoOrderReceipt> pendingOrders = [];
    private readonly Dictionary<long, (double StopLoss, double TakeProfit, long Magic)> positionInfo = [];
    private Mt5DemoOrderReceipt? selected;
    private Mt5DemoOrderReceipt? selectedOrder;

    /// <summary>Joins a strategy to one live account and one bar series.</summary>
    /// <param name="series">The live bars, seeded from history.</param>
    /// <param name="broker">The guarded trade client for this account.</param>
    /// <param name="digits">The symbol's price precision.</param>
    /// <param name="journal">Receives a line for anything refused or unsupported.</param>
    public LiveBrokerContext(
        LiveBarSeries series,
        IMt5TradeGateway broker,
        int digits,
        Action<string> journal)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(digits);

        this.series = series;
        this.broker = broker;
        this.digits = digits;
        symbolSpec = broker.ReadSymbolSnapshot();
        this.journal = journal;
    }

    /// <summary>Positions this context opened and has not yet closed.</summary>
    public IReadOnlyList<Mt5DemoOrderReceipt> OpenPositions => open;

    /// <inheritdoc />
    public string Symbol => series.Symbol;

    /// <inheritdoc />
    public double Point => 1.0 / Math.Pow(10.0, digits);

    /// <inheritdoc />
    public int Digits => digits;

    /// <inheritdoc />
    public DateTime TimeCurrent => series.LastQuoteTime;

    /// <summary>The chart period this strategy is running on.</summary>
    public int Period => LivePeriods.Identifier(series.PeriodMinutes);

    /// <inheritdoc />
    public double SymbolInfoDouble(string symbol, int propertyId) =>
        !IsRunSymbol(symbol) ? 0.0 : propertyId switch
        {
            1 => series.Bid,
            4 => series.Ask,
            7 => series.Bid,
            16 => Point,
            26 => TickValue,
            27 => TickSize,
            28 => ContractSize,
            34 => VolumeMin,
            35 => VolumeMax,
            36 => VolumeStep,
            53 => TickValue,
            54 => TickValue,
            _ => 0.0,
        };

    /// <inheritdoc />
    public long SymbolInfoInteger(string symbol, int propertyId) =>
        !IsRunSymbol(symbol) ? 0L : propertyId switch
        {
            0 => 1,
            17 => digits,
            18 => series.Ask > series.Bid
                ? (long)Math.Round((series.Ask - series.Bid) / Point)
                : 0L,
            // SYMBOL_TRADE_MODE_FULL. A missing vendor snapshot used to report 0
            // (disabled), so pending-grid experts such as Straddle never left CanTrade().
            30 => symbolSpec is { TradeMode: > 0 } ? symbolSpec.TradeMode : 4L,
            33 => 2,
            49 => 1,
            50 => 1 | 2,
            71 => 127,
            _ => 0L,
        };

    private double TickSize =>
        symbolSpec is { TickSize: > 0 } ? symbolSpec.TickSize : Point;

    private double ContractSize =>
        symbolSpec is { ContractSize: > 0 }
            ? symbolSpec.ContractSize
            : Symbol.Contains("XAU", StringComparison.OrdinalIgnoreCase) ? 100.0 : 100_000.0;

    private double TickValue =>
        symbolSpec is { TickValue: > 0 } ? symbolSpec.TickValue : ContractSize * TickSize;

    private double VolumeMin =>
        symbolSpec is { VolumeMin: > 0 } ? symbolSpec.VolumeMin : 0.01;

    private double VolumeMax =>
        symbolSpec is { VolumeMax: > 0 } ? symbolSpec.VolumeMax : 10.0;

    private double VolumeStep =>
        symbolSpec is { VolumeStep: > 0 } ? symbolSpec.VolumeStep : 0.01;

    /// <inheritdoc />
    public bool SymbolSelect(string symbol, bool selectFlag) => IsRunSymbol(symbol);

    /// <inheritdoc />
    public bool SymbolInfoTick(string symbol, out Mql5Tick tick)
    {
        if (!IsRunSymbol(symbol) || series.Bid <= 0.0 || series.Ask <= 0.0)
        {
            tick = default;
            return false;
        }
        DateTime time = series.LastQuoteTime == default ? DateTime.UtcNow : series.LastQuoteTime;
        long seconds = new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc)).ToUnixTimeSeconds();
        tick = new Mql5Tick
        {
            Time = seconds,
            TimeMsc = seconds * 1_000,
            Bid = series.Bid,
            Ask = series.Ask,
            Last = series.Bid,
            Flags = 6
        };
        return true;
    }

    /// <inheritdoc />
    public long MqlInfoInteger(int propertyId) => propertyId switch
    {
        3 => 0,
        4 => 1,
        5 => 0,
        6 => 0,
        7 => 0,
        8 => 0,
        14 => 0,
        _ => 0,
    };

    /// <summary>
    /// Account figures are not served to a live strategy.
    ///
    /// <para>
    /// Reading balance or equity means a broker round trip, and doing that inside a tick
    /// handler would put ~80ms of network on the decision path several times per bar. A
    /// strategy that sizes from equity would size from a stale number either way, so the
    /// honest answer is zero — MQL5's own value for "unavailable" — rather than a figure that
    /// looks current and is not.
    /// </para>
    /// </summary>
    public double AccountInfoDouble(int propertyId)
    {
        Mt5LiveAccountSnapshot account = broker.ReadAccountSnapshot();
        double equity = account.Equity > 0.0 ? account.Equity : Math.Max(0.0, account.Balance + account.Profit);
        double balance = account.Balance > 0.0 ? account.Balance : Math.Max(0.0, equity - account.Profit);
        double freeMargin = account.FreeMargin > 0.0 ? account.FreeMargin : Math.Max(0.0, equity - account.Margin);
        return propertyId switch
        {
            37 => balance,
            38 => 0.0,
            39 => account.Profit,
            40 => equity,
            41 => account.Margin,
            42 => freeMargin,
            43 => account.Margin > 0.0 ? equity / account.Margin * 100.0 : 0.0,
            _ => 0.0,
        };
    }

    public bool OrderCalcMargin(int orderType, string symbol, double volume, double price, out double margin)
    {
        margin = 0.0;
        if (!IsRunSymbol(symbol)
            || orderType is < 0 or > 7
            || !double.IsFinite(volume) || volume <= 0.0
            || !double.IsFinite(price) || price <= 0.0)
        {
            return false;
        }

        Mt5LiveAccountSnapshot account = broker.ReadAccountSnapshot();
        double leverage = account.Leverage > 0 ? account.Leverage : 100.0;
        margin = volume * ContractSize * price / leverage;
        return double.IsFinite(margin) && margin >= 0.0;
    }

    public bool OrderCheck(Mql5TradeRequest request, out Mql5TradeCheckResult result)
    {
        result = new Mql5TradeCheckResult();
        if (request is null
            || (!string.IsNullOrEmpty(request.Symbol) && !IsRunSymbol(request.Symbol))
            || !double.IsFinite(request.Volume) || request.Volume <= 0.0)
        {
            result.Retcode = Mql5Constants.TradeRetcode.Invalid;
            result.Comment = "invalid live trade request";
            return false;
        }

        double price = request.Price > 0.0
            ? request.Price
            : (request.Type is 0 or 2 or 4 ? series.Ask : series.Bid);
        if (!OrderCalcMargin(request.Type, Symbol, request.Volume, price, out double required)
            || required < 0.0)
        {
            result.Retcode = Mql5Constants.TradeRetcode.Invalid;
            result.Comment = "invalid live margin inputs";
            return false;
        }

        Mt5LiveAccountSnapshot account = broker.ReadAccountSnapshot();
        double equity = account.Equity > 0.0 ? account.Equity : Math.Max(0.0, account.Balance + account.Profit);
        double balance = account.Balance > 0.0 ? account.Balance : Math.Max(0.0, equity - account.Profit);
        double projectedMargin = account.Margin + required;
        double projectedFree = equity - projectedMargin;
        result.Balance = balance;
        result.Equity = equity;
        result.Margin = projectedMargin;
        result.MarginFree = projectedFree;
        result.MarginLevel = projectedMargin > 0.0 ? equity / projectedMargin * 100.0 : 0.0;
        if (projectedFree < 0.0)
        {
            result.Retcode = 10019;
            result.Comment = "not enough money";
            return false;
        }

        result.Retcode = 0;
        result.Comment = "done";
        return true;
    }

    /// <inheritdoc />
    public long AccountInfoInteger(int propertyId)
    {
        Mt5LiveAccountSnapshot account = broker.ReadAccountSnapshot();
        return propertyId switch
        {
            0 => checked((long)account.Login),
            32 => account.Environment == Mt5TradingEnvironment.Demo ? 0L : 2L,
            33 or 34 => 1L,
            35 => account.Leverage,
            53 => (long)account.MarginMode,
            54 => 2L,
            56 => account.MarginMode == Mt5AccountMarginMode.RetailHedging ? 1L : 0L,
            _ => 0L,
        };
    }

    /// <inheritdoc />
    public string AccountInfoString(int propertyId)
    {
        Mt5LiveAccountSnapshot account = broker.ReadAccountSnapshot();
        return propertyId switch
        {
            1 => account.Login.ToString(System.Globalization.CultureInfo.InvariantCulture),
            2 => account.Company,
            3 => account.Server,
            36 => account.Currency,
            _ => string.Empty,
        };
    }

    /// <inheritdoc />
    public void Delay(int milliseconds)
    {
        if (milliseconds is < 0 or > 5_000)
            throw new ArgumentOutOfRangeException(nameof(milliseconds), "Live strategy delays must be between 0 and 5000 milliseconds.");
        Thread.Sleep(milliseconds);
    }

    /// <inheritdoc />
    public int PositionsTotal() => open.Count;

    /// <inheritdoc />
    public bool PositionSelect(string symbol)
    {
        if (!IsRunSymbol(symbol))
        {
            return false;
        }

        selected = open.Count > 0 ? open[^1] : null;
        return selected is not null;
    }

    /// <inheritdoc />
    public bool PositionSelectByTicket(ulong ticket)
    {
        selected = open.Find(position => (ulong)position.Ticket == ticket);
        return selected is not null;
    }

    /// <inheritdoc />
    public ulong PositionGetTicket(int index)
    {
        if (index < 0 || index >= open.Count)
        {
            selected = null;
            return 0UL;
        }

        selected = open[index];
        return (ulong)selected.Ticket;
    }

    /// <inheritdoc />
    public string PositionGetSymbol(int index)
    {
        selected = index >= 0 && index < open.Count ? open[index] : null;
        return selected?.Symbol ?? string.Empty;
    }

    /// <inheritdoc />
    public double PositionGetDouble(int propertyId) => selected is not { } position ? 0.0 : propertyId switch
    {
        3 => position.Volume,
        4 => position.Price,
        5 => position.Side == Mt5DemoSide.Buy ? series.Bid : series.Ask,
        6 => positionInfo.TryGetValue(position.Ticket, out var info) ? info.StopLoss : 0.0,
        7 => positionInfo.TryGetValue(position.Ticket, out var info) ? info.TakeProfit : 0.0,
        10 => position.Profit,
        _ => 0.0,
    };

    /// <inheritdoc />
    public long PositionGetInteger(int propertyId) => selected is not { } position ? 0L : propertyId switch
    {
        1 => new DateTimeOffset(position.OpenTime, TimeSpan.Zero).ToUnixTimeSeconds(),
        2 => position.Side == Mt5DemoSide.Buy ? 0L : 1L,
        12 => positionInfo.TryGetValue(position.Ticket, out var info) ? info.Magic : 0L,
        13 => position.Ticket,
        17 => position.Ticket,
        _ => 0L,
    };

    /// <inheritdoc />
    public string PositionGetString(int propertyId) => selected is not { } position ? string.Empty : propertyId switch
    {
        0 => position.Symbol,
        11 => string.Empty,
        _ => string.Empty,
    };

    /// <inheritdoc />
    public int OrdersTotal() => pendingOrders.Count;

    /// <inheritdoc />
    public ulong OrderGetTicket(int index)
    {
        if (index < 0 || index >= pendingOrders.Count)
        {
            selectedOrder = null;
            return 0UL;
        }

        selectedOrder = pendingOrders[index];
        return (ulong)selectedOrder.Ticket;
    }

    /// <inheritdoc />
    public bool OrderSelect(ulong ticket)
    {
        selectedOrder = pendingOrders.Find(order => (ulong)order.Ticket == ticket);
        return selectedOrder is not null;
    }

    /// <inheritdoc />
    public double OrderGetDouble(int propertyId) => selectedOrder is not { } order ? 0.0 : propertyId switch
    {
        7 => order.Volume,
        8 => order.Volume,
        9 => order.Price,
        12 => positionInfo.TryGetValue(order.Ticket, out var info) ? info.StopLoss : 0.0,
        13 => positionInfo.TryGetValue(order.Ticket, out var info) ? info.TakeProfit : 0.0,
        _ => 0.0,
    };

    /// <inheritdoc />
    public long OrderGetInteger(int propertyId) => selectedOrder is not { } order ? 0L : propertyId switch
    {
        1 => new DateTimeOffset(order.OpenTime, TimeSpan.Zero).ToUnixTimeSeconds(),
        4 => (long)order.Side,
        15 => positionInfo.TryGetValue(order.Ticket, out var info) ? info.Magic : 0L,
        22 => order.Ticket,
        _ => 0L,
    };

    /// <inheritdoc />
    public string OrderGetString(int propertyId) => selectedOrder is not { } order ? string.Empty : propertyId switch
    {
        0 => order.Symbol,
        _ => string.Empty,
    };

    /// <summary>
    /// Sends the strategy's order to the broker.
    ///
    /// <para>
    /// The call is synchronous because MQL5's own <c>OrderSend</c> is: a strategy expects the
    /// result before its next statement, and returning early would let it act on a fill that
    /// has not happened. The wait is the broker's round trip, and it is the strategy's own
    /// semantics that require paying it.
    /// </para>
    /// </summary>
    public bool OrderSend(Mql5TradeRequest request, out Mql5TradeResult result)
    {
        result = new Mql5TradeResult();
        if (request is null)
        {
            result.Retcode = Mql5Constants.TradeRetcode.Invalid;
            return false;
        }

        try
        {
            switch (request.Action)
            {
                case 1 when request.Type is 0 or 1:
                    return Open(request, result);
                case 5:
                    return Place(request, result);
                case 6:
                case 7:
                    return Modify(request, result);
                case 8:
                    return Remove(request, result);
                default:
                    journal($"refused action {request.Action} type {request.Type}: not supported live");
                    result.Retcode = Mql5Constants.TradeRetcode.Invalid;
                    return false;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or ArgumentOutOfRangeException
            or InvalidDataException
            or TimeoutException)
        {
            // A refusal is the broker's answer, not a crash. The strategy is told the order
            // failed, exactly as MQL5 would, and the reason is recorded for the operator.
            journal("order refused: " + exception.Message);
            result.Retcode = Mql5Constants.TradeRetcode.Reject;
            return false;
        }
    }

    private bool Open(Mql5TradeRequest request, Mql5TradeResult result)
    {
        ulong positionTicket = request.Position != 0 ? request.Position : request.Order;
        if (positionTicket != 0)
        {
            Mt5DemoOrderReceipt? target = open.Find(position => (ulong)position.Ticket == positionTicket);
            if (target is null)
            {
                result.Retcode = Mql5Constants.TradeRetcode.Invalid;
                return false;
            }

            Mt5DemoOrderReceipt closed = broker.CloseAsync(target).GetAwaiter().GetResult();
            open.Remove(target);
            positionInfo.Remove(target.Ticket);
            if (selected == target)
            {
                selected = null;
            }

            result.Retcode = Mql5Constants.TradeRetcode.Done;
            result.Order = (ulong)closed.Ticket;
            result.Deal = (ulong)closed.Ticket;
            result.Price = closed.Price;
            result.Volume = closed.Volume;
            return true;
        }

        Mt5DemoOrderReceipt opened = broker
            .SendAsync(
                request.Type == 0 ? Mt5DemoSide.Buy : Mt5DemoSide.Sell,
                request.Volume,
                0,
                request.StopLoss,
                request.TakeProfit,
                request.Comment)
            .GetAwaiter()
            .GetResult();
        if (opened.Ticket == 0)
        {
            result.Retcode = Mql5Constants.TradeRetcode.Reject;
            return false;
        }

        open.Add(opened);
        positionInfo[opened.Ticket] = (request.StopLoss, request.TakeProfit, (long)request.Magic);
        result.Retcode = Mql5Constants.TradeRetcode.Done;
        result.Order = (ulong)opened.Ticket;
        result.Deal = (ulong)opened.Ticket;
        result.Price = opened.Price;
        result.Volume = opened.Volume;
        return true;
    }

    private bool Place(Mql5TradeRequest request, Mql5TradeResult result)
    {
        Mt5DemoSide side = request.Type switch
        {
            2 => Mt5DemoSide.BuyLimit,
            3 => Mt5DemoSide.SellLimit,
            4 => Mt5DemoSide.BuyStop,
            5 => Mt5DemoSide.SellStop,
            _ => Mt5DemoSide.Buy,
        };
        Mt5DemoOrderReceipt placed = broker
            .SendAsync(side, request.Volume, request.Price, request.StopLoss, request.TakeProfit, request.Comment)
            .GetAwaiter()
            .GetResult();
        if (placed.Ticket == 0)
        {
            result.Retcode = Mql5Constants.TradeRetcode.Reject;
            return false;
        }

        pendingOrders.Add(placed);
        positionInfo[placed.Ticket] = (request.StopLoss, request.TakeProfit, (long)request.Magic);
        result.Retcode = Mql5Constants.TradeRetcode.Placed;
        result.Order = (ulong)placed.Ticket;
        return true;
    }

    private bool Modify(Mql5TradeRequest request, Mql5TradeResult result)
    {
        ulong ticket = request.Position != 0 ? request.Position : request.Order;
        Mt5DemoOrderReceipt? target = open.Find(position => (ulong)position.Ticket == ticket)
            ?? pendingOrders.Find(order => (ulong)order.Ticket == ticket);
        if (target is null)
        {
            result.Retcode = Mql5Constants.TradeRetcode.Invalid;
            return false;
        }

        broker.ModifyAsync(target, request.StopLoss, request.TakeProfit).GetAwaiter().GetResult();
        long magic = positionInfo.TryGetValue(target.Ticket, out var info) ? info.Magic : (long)request.Magic;
        positionInfo[target.Ticket] = (request.StopLoss, request.TakeProfit, magic);
        result.Retcode = Mql5Constants.TradeRetcode.Done;
        result.Order = (ulong)target.Ticket;
        return true;
    }

    private bool Remove(Mql5TradeRequest request, Mql5TradeResult result)
    {
        ulong ticket = request.Order != 0 ? request.Order : request.Position;
        Mt5DemoOrderReceipt? target = pendingOrders.Find(order => (ulong)order.Ticket == ticket);
        if (target is null)
        {
            result.Retcode = Mql5Constants.TradeRetcode.Invalid;
            return false;
        }

        broker.CancelAsync(target).GetAwaiter().GetResult();
        pendingOrders.Remove(target);
        positionInfo.Remove(target.Ticket);
        if (selectedOrder == target)
        {
            selectedOrder = null;
        }

        result.Retcode = Mql5Constants.TradeRetcode.Done;
        return true;
    }

    /// <inheritdoc />
    public int IndicatorHandle(string name, params object[] parameters) =>
        series.ResolveIndicator(name, parameters);

    /// <inheritdoc />
    public int CopyBuffer(int handle, int bufferNum, int start, int count, double[] target) =>
        series.CopyBuffer(handle, bufferNum, start, count, target);

    /// <inheritdoc />
    public int Bars(string symbol, int timeframe) => Serves(symbol, timeframe) ? series.Count : 0;

    /// <inheritdoc />
    public int BarCount(string symbol, int timeframe) => Bars(symbol, timeframe);

    /// <inheritdoc />
    public double BarOpen(string symbol, int timeframe, int shift) =>
        Serves(symbol, timeframe) ? series.At(shift).Open : 0.0;

    /// <inheritdoc />
    public double BarHigh(string symbol, int timeframe, int shift) =>
        Serves(symbol, timeframe) ? series.At(shift).High : 0.0;

    /// <inheritdoc />
    public double BarLow(string symbol, int timeframe, int shift) =>
        Serves(symbol, timeframe) ? series.At(shift).Low : 0.0;

    /// <inheritdoc />
    public double BarClose(string symbol, int timeframe, int shift) =>
        Serves(symbol, timeframe) ? series.At(shift).Close : 0.0;

    /// <inheritdoc />
    public long BarTime(string symbol, int timeframe, int shift) =>
        Serves(symbol, timeframe) ? Mql5Time.FromDateTime(series.At(shift).Time) : 0L;

    /// <inheritdoc />
    public long BarTickVolume(string symbol, int timeframe, int shift) =>
        Serves(symbol, timeframe) ? series.At(shift).TickVolume : 0L;

    /// <inheritdoc />
    public int CopyRates(string symbol, int timeframe, Mql5CopyRange range, ref Mql5Rates[] target)
    {
        if (!Serves(symbol, timeframe) || range.Kind != Mql5CopyRangeKind.FromPosition)
        {
            return -1;
        }

        int count = Available(range);
        if (count <= 0)
        {
            return count;
        }

        if (target.Length < count)
        {
            Array.Resize(ref target, count);
        }

        for (int index = 0; index < count; index++)
        {
            Mql5Bar bar = series.At(range.StartPosition + count - 1 - index);
            target[index] = new Mql5Rates
            {
                Time = Mql5Time.FromDateTime(bar.Time),
                Open = bar.Open,
                High = bar.High,
                Low = bar.Low,
                Close = bar.Close,
                TickVolume = bar.TickVolume,
                Spread = bar.Spread,
                RealVolume = 0,
            };
        }

        return count;
    }

    private int Available(Mql5CopyRange range)
    {
        if (range.Count <= 0 || range.StartPosition < 0)
        {
            return 0;
        }

        int reachable = series.Count - range.StartPosition;
        return reachable <= 0 ? 0 : Math.Min(range.Count, reachable);
    }

    private bool Serves(string symbol, int timeframe) =>
        IsRunSymbol(symbol)
        && (timeframe == Mql5Constants.Timeframes.Current || timeframe == Period);

    private bool IsRunSymbol(string? symbol) =>
        string.IsNullOrEmpty(symbol)
        || string.Equals(symbol, series.Symbol, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Maps whole minutes onto MQL5's timeframe identifiers.</summary>
public static class LivePeriods
{
    /// <summary>The MQL5 timeframe identifier for a period given in minutes.</summary>
    /// <param name="minutes">The period length.</param>
    public static int Identifier(int minutes) => minutes switch
    {
        1 => Mql5Constants.Timeframes.M1,
        5 => Mql5Constants.Timeframes.M5,
        15 => Mql5Constants.Timeframes.M15,
        30 => Mql5Constants.Timeframes.M30,
        60 => Mql5Constants.Timeframes.H1,
        240 => Mql5Constants.Timeframes.H4,
        1440 => Mql5Constants.Timeframes.D1,
        10080 => Mql5Constants.Timeframes.W1,
        _ => Mql5Constants.Timeframes.Current,
    };
}
