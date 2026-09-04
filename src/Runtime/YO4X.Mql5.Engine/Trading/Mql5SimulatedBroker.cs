using System.Globalization;
using YO4X.Mql5.Engine.Feed;
using YO4X.Mql5.Engine.Hosting;

namespace YO4X.Mql5.Engine.Trading;

/// <summary>
/// A fully offline order-execution simulator.
/// </summary>
/// <remarks>
/// <para>
/// This type is a SIMULATOR. It performs no I/O of any kind: no sockets, no native interop, no
/// MetaTrader terminal, no <c>mt5api.dll</c>. Fills are computed arithmetically from the bar series
/// it is handed. The sealed live-order path elsewhere in the solution is neither referenced nor
/// reachable from here.
/// </para>
/// <para>
/// Intrabar sequencing is deterministic: a bullish bar is walked open, low, high, close and a
/// bearish bar open, high, low, close. Stop loss and take profit are evaluated before pending
/// activations, and both are evaluated in ticket order, so an identical feed always yields an
/// identical trade sequence.
/// </para>
/// </remarks>
public sealed class Mql5SimulatedBroker
{
    private const double VolumeEpsilon = 1e-8;

    private readonly Mql5RunOptions options;
    private readonly Mql5SymbolSpec spec;
    private readonly List<Mql5Position> positions = [];
    private readonly List<Mql5PendingOrder> pendingOrders = [];
    private readonly List<Mql5ClosedTrade> closedTrades = [];
    private readonly List<Mql5OrderEvent> journal = [];

    private long nextTicket = 1;
    private int ordersThisTick;
    private bool perTickCapReportedForThisTick;
    private double balance;
    private double bid;
    private int spreadPoints;
    private DateTime time;

    /// <summary>Initializes a broker from the run options.</summary>
    public Mql5SimulatedBroker(Mql5RunOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options;
        spec = options.Symbol;
        balance = Round2(options.InitialDeposit);
        spreadPoints = options.SpreadPoints;
        bid = options.InitialBid > 0.0
            ? spec.NormalizePrice(options.InitialBid)
            : 0.0;
        time = DateTime.UnixEpoch;
    }

    /// <summary>Gets the symbol specification in force.</summary>
    public Mql5SymbolSpec Spec => spec;

    /// <summary>Gets the current bid.</summary>
    public double Bid => bid;

    /// <summary>Gets the current ask, that is the bid plus the current spread.</summary>
    public double Ask => spec.NormalizePrice(bid + (spreadPoints * spec.Point));

    /// <summary>Gets the current spread in points.</summary>
    public int SpreadPoints => spreadPoints;

    /// <summary>Gets the simulated clock, which is always the current bar's time.</summary>
    public DateTime Time => time;

    /// <summary>Gets the realized balance.</summary>
    public double Balance => balance;

    /// <summary>Gets the floating profit of all open positions, commission and swap included.</summary>
    public double FloatingProfit
    {
        get
        {
            double total = 0.0;
            foreach (Mql5Position position in positions)
            {
                total += position.Profit + position.Commission + position.Swap;
            }

            return Round2(total);
        }
    }

    /// <summary>Gets balance plus floating profit.</summary>
    public double Equity => Round2(balance + FloatingProfit);

    /// <summary>Gets the total margin locked by open positions.</summary>
    public double Margin
    {
        get
        {
            double total = 0.0;
            foreach (Mql5Position position in positions)
            {
                total += position.Margin;
            }

            return Round2(total);
        }
    }

    /// <summary>Gets equity minus margin.</summary>
    public double FreeMargin => Round2(Equity - Margin);

    /// <summary>Gets the margin level in percent, or zero when nothing is open.</summary>
    public double MarginLevel
    {
        get
        {
            double margin = Margin;
            return margin <= 0.0 ? 0.0 : Round2(Equity / margin * 100.0);
        }
    }

    /// <summary>Gets the open positions in ticket order.</summary>
    public IReadOnlyList<Mql5Position> Positions => positions;

    /// <summary>Gets the resting pending orders in ticket order.</summary>
    public IReadOnlyList<Mql5PendingOrder> PendingOrders => pendingOrders;

    /// <summary>Gets every close, including partial closes, in the order they happened.</summary>
    public IReadOnlyList<Mql5ClosedTrade> ClosedTrades => closedTrades;

    /// <summary>Gets the full order journal.</summary>
    public IReadOnlyList<Mql5OrderEvent> Journal => journal;

    /// <summary>Gets a value indicating whether the per-tick order cap fired at least once.</summary>
    public bool OrdersPerTickCapTriggered { get; private set; }

    /// <summary>Gets a value indicating whether the margin stop out fired at least once.</summary>
    public bool StopOutTriggered { get; private set; }

    /// <summary>Gets a value indicating whether any quote has been seen yet.</summary>
    public bool HasQuote => bid > 0.0;

    /// <summary>Resets the per-tick order budget. The host calls this once per tick.</summary>
    public void BeginTick()
    {
        ordersThisTick = 0;
        perTickCapReportedForThisTick = false;
    }

    /// <summary>
    /// Advances the simulation across one bar, walking the intrabar path and firing any stop,
    /// target or pending order that the path touches.
    /// </summary>
    public void ApplyBar(in Mql5Bar bar)
    {
        if (time != DateTime.UnixEpoch && bar.Time.Date > time.Date)
        {
            int days = (bar.Time.Date - time.Date).Days;
            AccrueSwap(days);
        }

        time = bar.Time;
        spreadPoints = bar.Spread > 0 ? bar.Spread : options.SpreadPoints;

        double second = bar.IsBullish ? bar.Low : bar.High;
        double third = bar.IsBullish ? bar.High : bar.Low;

        MoveTo(bar.Open, gapAllowed: true);
        MoveTo(second, gapAllowed: false);
        MoveTo(third, gapAllowed: false);
        MoveTo(bar.Close, gapAllowed: false);
    }

    /// <summary>
    /// Submits a trade request. Never throws for a malformed request: the caller receives the
    /// documented MQL5 retcode instead.
    /// </summary>
    public bool Send(Mql5TradeRequest request, out Mql5TradeResult result)
    {
        result = new Mql5TradeResult { Bid = Bid, Ask = Ask, Retcode = Mql5TradeRetcode.Invalid };

        if (request is null)
        {
            result.Comment = "null request";
            return false;
        }

        ordersThisTick++;
        if (ordersThisTick > options.MaxOrdersPerTick)
        {
            OrdersPerTickCapTriggered = true;
            if (!perTickCapReportedForThisTick)
            {
                perTickCapReportedForThisTick = true;
                Record(new Mql5OrderEvent
                {
                    Time = time,
                    Kind = Mql5OrderEventKind.OrdersPerTickCapReached,
                    Symbol = spec.Name,
                    Retcode = Mql5TradeRetcode.LimitOrders,
                    Balance = balance,
                    Detail = string.Create(
                        CultureInfo.InvariantCulture,
                        $"per-tick order cap of {options.MaxOrdersPerTick} reached; further requests on this tick are rejected"),
                });
            }

            return Fail(result, Mql5TradeRetcode.LimitOrders, "per-tick order cap reached", null);
        }

        Mql5TradeRequest req = request.Clone();
        if (string.IsNullOrEmpty(req.Symbol))
        {
            req.Symbol = spec.Name;
        }

        if (!string.Equals(req.Symbol, spec.Name, StringComparison.Ordinal))
        {
            return Fail(result, Mql5TradeRetcode.Invalid, "unknown symbol " + req.Symbol, req);
        }

        if (!HasQuote)
        {
            return Fail(result, Mql5TradeRetcode.PriceOff, "no quote available yet", req);
        }

        return req.Action switch
        {
            Mql5TradeAction.Deal => ExecuteDeal(req, result),
            Mql5TradeAction.Pending => PlacePending(req, result),
            Mql5TradeAction.Sltp => ModifyPositionStops(req, result),
            Mql5TradeAction.Modify => ModifyPending(req, result),
            Mql5TradeAction.Remove => RemovePending(req, result),
            _ => Fail(result, Mql5TradeRetcode.Invalid, "unsupported action", req),
        };
    }

    /// <summary>Closes every open position at the current market, used when the feed ends.</summary>
    public void CloseAll(Mql5CloseReason reason)
    {
        foreach (Mql5Position position in positions.ToArray())
        {
            double price = position.Type == Mql5PositionType.Buy
                ? MarketFillPrice(Mql5PositionType.Sell)
                : MarketFillPrice(Mql5PositionType.Buy);
            ClosePortion(position, position.Volume, price, reason);
        }

        Revalue();
    }

    /// <summary>Finds an open position by ticket.</summary>
    public Mql5Position? FindPosition(long ticket)
    {
        foreach (Mql5Position position in positions)
        {
            if (position.Ticket == ticket)
            {
                return position;
            }
        }

        return null;
    }

    /// <summary>Finds the first open position on the given symbol.</summary>
    public Mql5Position? FindPositionBySymbol(string symbol)
    {
        foreach (Mql5Position position in positions)
        {
            if (string.Equals(position.Symbol, symbol, StringComparison.Ordinal))
            {
                return position;
            }
        }

        return null;
    }

    private static double Round2(double value)
    {
        double rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        return rounded == 0.0 ? 0.0 : rounded;   // never hand back negative zero
    }

    private void MoveTo(double newBid, bool gapAllowed)
    {
        bid = spec.NormalizePrice(newBid);
        Revalue();
        ProcessPositionStops(gapAllowed);
        ProcessPendingActivations(gapAllowed);
        Revalue();
        EnforceStopOut();
    }

    private void AccrueSwap(int days)
    {
        if (days <= 0)
        {
            return;
        }

        foreach (Mql5Position position in positions)
        {
            double rate = position.Type == Mql5PositionType.Buy ? spec.SwapLong : spec.SwapShort;
            if (rate != 0.0)
            {
                position.Swap = Round2(position.Swap + (rate * position.Volume * days));
            }
        }
    }

    private void Revalue()
    {
        foreach (Mql5Position position in positions)
        {
            double closePrice = position.Type == Mql5PositionType.Buy ? Bid : Ask;
            position.PriceCurrent = closePrice;
            double delta = position.Type == Mql5PositionType.Buy
                ? closePrice - position.PriceOpen
                : position.PriceOpen - closePrice;
            position.Profit = Round2(spec.ProfitOf(delta, position.Volume));
        }
    }

    private void ProcessPositionStops(bool gapAllowed)
    {
        foreach (Mql5Position position in positions.ToArray())
        {
            if (position.Volume <= VolumeEpsilon)
            {
                continue;
            }

            if (position.Type == Mql5PositionType.Buy)
            {
                double quote = Bid;
                if (position.StopLoss > 0.0 && quote <= position.StopLoss)
                {
                    double fill = gapAllowed ? Math.Min(position.StopLoss, quote) : position.StopLoss;
                    ClosePortion(position, position.Volume, fill, Mql5CloseReason.StopLoss);
                    continue;
                }

                if (position.TakeProfit > 0.0 && quote >= position.TakeProfit)
                {
                    double fill = gapAllowed ? Math.Max(position.TakeProfit, quote) : position.TakeProfit;
                    ClosePortion(position, position.Volume, fill, Mql5CloseReason.TakeProfit);
                }
            }
            else
            {
                double quote = Ask;
                if (position.StopLoss > 0.0 && quote >= position.StopLoss)
                {
                    double fill = gapAllowed ? Math.Max(position.StopLoss, quote) : position.StopLoss;
                    ClosePortion(position, position.Volume, fill, Mql5CloseReason.StopLoss);
                    continue;
                }

                if (position.TakeProfit > 0.0 && quote <= position.TakeProfit)
                {
                    double fill = gapAllowed ? Math.Min(position.TakeProfit, quote) : position.TakeProfit;
                    ClosePortion(position, position.Volume, fill, Mql5CloseReason.TakeProfit);
                }
            }
        }
    }

    private void ProcessPendingActivations(bool gapAllowed)
    {
        foreach (Mql5PendingOrder order in pendingOrders.ToArray())
        {
            double ask = Ask;
            double quote = Bid;
            bool touched;
            double fill;

            switch (order.Type)
            {
                case Mql5OrderType.BuyLimit:
                    touched = ask <= order.Price;
                    fill = gapAllowed ? Math.Min(order.Price, ask) : order.Price;
                    break;
                case Mql5OrderType.BuyStop:
                    touched = ask >= order.Price;
                    fill = gapAllowed ? Math.Max(order.Price, ask) : order.Price;
                    break;
                case Mql5OrderType.SellLimit:
                    touched = quote >= order.Price;
                    fill = gapAllowed ? Math.Max(order.Price, quote) : order.Price;
                    break;
                case Mql5OrderType.SellStop:
                    touched = quote <= order.Price;
                    fill = gapAllowed ? Math.Min(order.Price, quote) : order.Price;
                    break;
                default:
                    touched = false;
                    fill = 0.0;
                    break;
            }

            if (!touched)
            {
                continue;
            }

            Mql5PositionType side = order.IsBuySide ? Mql5PositionType.Buy : Mql5PositionType.Sell;
            double fillPrice = spec.NormalizePrice(fill);

            if (!HasMarginFor(side, order.Volume, fillPrice))
            {
                pendingOrders.Remove(order);
                Record(new Mql5OrderEvent
                {
                    Time = time,
                    Kind = Mql5OrderEventKind.Rejected,
                    Ticket = order.Ticket,
                    Symbol = order.Symbol,
                    Type = order.Type,
                    Volume = order.Volume,
                    Price = fillPrice,
                    Balance = balance,
                    Retcode = Mql5TradeRetcode.NoMoney,
                    Detail = "not enough free margin",
                });
                continue;
            }

            pendingOrders.Remove(order);

            Record(new Mql5OrderEvent
            {
                Time = time,
                Kind = Mql5OrderEventKind.PendingActivated,
                Ticket = order.Ticket,
                Symbol = order.Symbol,
                Type = order.Type,
                Volume = order.Volume,
                Price = fillPrice,
                Balance = balance,
                Retcode = Mql5TradeRetcode.Done,
                Detail = "pending order touched",
            });

            OpenExposure(
                side,
                order.Volume,
                fillPrice,
                order.StopLoss,
                order.TakeProfit,
                order.Magic,
                order.Comment,
                result: null);

            ProcessPositionStops(gapAllowed);
        }
    }

    private void EnforceStopOut()
    {
        if (options.StopOutLevelPercent <= 0.0)
        {
            return;
        }

        int guard = 0;
        while (positions.Count > 0 && Margin > 0.0 && MarginLevel < options.StopOutLevelPercent && guard < 1000)
        {
            guard++;
            Mql5Position worst = positions[0];
            foreach (Mql5Position candidate in positions)
            {
                if (candidate.Profit < worst.Profit)
                {
                    worst = candidate;
                }
            }

            StopOutTriggered = true;
            double marginLevelBefore = MarginLevel;
            long ticket = worst.Ticket;
            string symbol = worst.Symbol;
            double volume = worst.Volume;
            double price = worst.Type == Mql5PositionType.Buy ? Bid : Ask;

            ClosePortion(worst, volume, price, Mql5CloseReason.StopOut);
            Revalue();

            Record(new Mql5OrderEvent
            {
                Time = time,
                Kind = Mql5OrderEventKind.StopOut,
                Ticket = ticket,
                Symbol = symbol,
                Volume = volume,
                Price = price,
                Balance = balance,
                Retcode = Mql5TradeRetcode.Done,
                Detail = string.Create(
                    CultureInfo.InvariantCulture,
                    $"margin level {marginLevelBefore} fell below stop out {options.StopOutLevelPercent}"),
            });
        }
    }

    private bool ExecuteDeal(Mql5TradeRequest req, Mql5TradeResult result)
    {
        if (req.Position != 0)
        {
            return CloseByTicket(req, result);
        }

        if (req.Type is not (Mql5OrderType.Buy or Mql5OrderType.Sell))
        {
            return Fail(result, Mql5TradeRetcode.Invalid, "market deal requires Buy or Sell", req);
        }

        if (!TryNormalizeVolume(req.Volume, out double volume, out string volumeError))
        {
            return Fail(result, Mql5TradeRetcode.InvalidVolume, volumeError, req);
        }

        Mql5PositionType side = req.Type == Mql5OrderType.Buy ? Mql5PositionType.Buy : Mql5PositionType.Sell;
        double fill = MarketFillPrice(side);
        double reference = side == Mql5PositionType.Buy ? Bid : Ask;

        if (!ValidateStops(side, reference, req.Sl, req.Tp, out string stopsError))
        {
            return Fail(result, Mql5TradeRetcode.InvalidStops, stopsError, req);
        }

        if (!HasMarginFor(side, volume, fill))
        {
            return Fail(result, Mql5TradeRetcode.NoMoney, "not enough free margin", req);
        }

        long ticket = OpenExposure(side, volume, fill, req.Sl, req.Tp, req.Magic, req.Comment, result);
        result.Retcode = Mql5TradeRetcode.Done;
        result.Volume = volume;
        result.Price = fill;
        result.Position = ticket;
        result.Order = ticket;
        result.Deal = ticket;
        result.Comment = "done";
        return true;
    }

    private bool CloseByTicket(Mql5TradeRequest req, Mql5TradeResult result)
    {
        Mql5Position? position = FindPosition(req.Position);
        if (position is null)
        {
            return Fail(result, Mql5TradeRetcode.PositionClosed, "position not found", req);
        }

        double volume = req.Volume <= 0.0 ? position.Volume : spec.NormalizeVolume(req.Volume);
        if (double.IsNaN(volume) || volume <= 0.0)
        {
            return Fail(result, Mql5TradeRetcode.InvalidVolume, "close volume must be positive", req);
        }

        if (volume > position.Volume + VolumeEpsilon)
        {
            return Fail(result, Mql5TradeRetcode.InvalidCloseVolume, "close volume exceeds position volume", req);
        }

        double price = position.Type == Mql5PositionType.Buy
            ? MarketFillPrice(Mql5PositionType.Sell)
            : MarketFillPrice(Mql5PositionType.Buy);

        bool partial = volume < position.Volume - VolumeEpsilon;
        ClosePortion(position, volume, price, Mql5CloseReason.Strategy);
        Revalue();

        result.Retcode = partial ? Mql5TradeRetcode.DonePartial : Mql5TradeRetcode.Done;
        result.Volume = volume;
        result.Price = price;
        result.Position = req.Position;
        result.Order = req.Position;
        result.Deal = req.Position;
        result.Comment = partial ? "partial close" : "closed";
        return true;
    }

    private bool PlacePending(Mql5TradeRequest req, Mql5TradeResult result)
    {
        if (req.Type is not (Mql5OrderType.BuyLimit or Mql5OrderType.SellLimit or Mql5OrderType.BuyStop or Mql5OrderType.SellStop))
        {
            return Fail(result, Mql5TradeRetcode.Invalid, "pending action requires a pending order type", req);
        }

        if (pendingOrders.Count >= options.MaxPendingOrders)
        {
            return Fail(result, Mql5TradeRetcode.LimitOrders, "pending order book is full", req);
        }

        if (!TryNormalizeVolume(req.Volume, out double volume, out string volumeError))
        {
            return Fail(result, Mql5TradeRetcode.InvalidVolume, volumeError, req);
        }

        double price = spec.NormalizePrice(req.Price);
        if (double.IsNaN(price) || double.IsInfinity(price) || price <= 0.0)
        {
            return Fail(result, Mql5TradeRetcode.InvalidPrice, "pending price must be positive", req);
        }

        if (!ValidatePendingPrice(req.Type, price, out string priceError))
        {
            return Fail(result, Mql5TradeRetcode.InvalidPrice, priceError, req);
        }

        Mql5PositionType side = req.Type is Mql5OrderType.BuyLimit or Mql5OrderType.BuyStop
            ? Mql5PositionType.Buy
            : Mql5PositionType.Sell;

        if (!ValidateStops(side, price, req.Sl, req.Tp, out string stopsError))
        {
            return Fail(result, Mql5TradeRetcode.InvalidStops, stopsError, req);
        }

        var order = new Mql5PendingOrder
        {
            Ticket = nextTicket++,
            Symbol = spec.Name,
            Type = req.Type,
            Volume = volume,
            Price = price,
            StopLoss = req.Sl > 0.0 ? spec.NormalizePrice(req.Sl) : 0.0,
            TakeProfit = req.Tp > 0.0 ? spec.NormalizePrice(req.Tp) : 0.0,
            TimeSetup = time,
            Magic = req.Magic,
            Comment = req.Comment,
        };

        pendingOrders.Add(order);
        Record(new Mql5OrderEvent
        {
            Time = time,
            Kind = Mql5OrderEventKind.PendingPlaced,
            Ticket = order.Ticket,
            Symbol = order.Symbol,
            Type = order.Type,
            Volume = order.Volume,
            Price = order.Price,
            Balance = balance,
            Retcode = Mql5TradeRetcode.Done,
            Detail = "pending order placed",
        });

        result.Retcode = Mql5TradeRetcode.Done;
        result.Order = order.Ticket;
        result.Volume = volume;
        result.Price = price;
        result.Comment = "placed";
        return true;
    }

    private bool ModifyPositionStops(Mql5TradeRequest req, Mql5TradeResult result)
    {
        Mql5Position? position = req.Position != 0
            ? FindPosition(req.Position)
            : FindPositionBySymbol(req.Symbol);

        if (position is null)
        {
            return Fail(result, Mql5TradeRetcode.PositionClosed, "position not found", req);
        }

        double sl = req.Sl > 0.0 ? spec.NormalizePrice(req.Sl) : 0.0;
        double tp = req.Tp > 0.0 ? spec.NormalizePrice(req.Tp) : 0.0;

        double reference = position.Type == Mql5PositionType.Buy ? Bid : Ask;
        if (!ValidateStops(position.Type, reference, sl, tp, out string stopsError))
        {
            return Fail(result, Mql5TradeRetcode.InvalidStops, stopsError, req);
        }

        if (Math.Abs(position.StopLoss - sl) < spec.Point / 2.0 &&
            Math.Abs(position.TakeProfit - tp) < spec.Point / 2.0)
        {
            return Fail(result, Mql5TradeRetcode.NoChanges, "stops unchanged", req);
        }

        position.StopLoss = sl;
        position.TakeProfit = tp;

        Record(new Mql5OrderEvent
        {
            Time = time,
            Kind = Mql5OrderEventKind.PositionModified,
            Ticket = position.Ticket,
            Symbol = position.Symbol,
            Type = position.Type == Mql5PositionType.Buy ? Mql5OrderType.Buy : Mql5OrderType.Sell,
            Volume = position.Volume,
            Price = position.PriceCurrent,
            Balance = balance,
            Retcode = Mql5TradeRetcode.Done,
            Detail = string.Create(CultureInfo.InvariantCulture, $"sl={sl} tp={tp}"),
        });

        result.Retcode = Mql5TradeRetcode.Done;
        result.Position = position.Ticket;
        result.Order = position.Ticket;
        result.Comment = "stops modified";
        return true;
    }

    private bool ModifyPending(Mql5TradeRequest req, Mql5TradeResult result)
    {
        Mql5PendingOrder? order = FindPending(req.Order);
        if (order is null)
        {
            return Fail(result, Mql5TradeRetcode.Invalid, "pending order not found", req);
        }

        double price = order.Price;
        if (req.Price > 0.0)
        {
            if (double.IsNaN(req.Price) || double.IsInfinity(req.Price))
            {
                return Fail(result, Mql5TradeRetcode.InvalidPrice, "pending price is not a number", req);
            }

            double requestedPrice = spec.NormalizePrice(req.Price);
            if (Math.Abs(requestedPrice - order.Price) > spec.Point / 2.0)
            {
                if (!ValidatePendingPrice(order.Type, requestedPrice, out string priceError))
                {
                    return Fail(result, Mql5TradeRetcode.InvalidPrice, priceError, req);
                }

                price = requestedPrice;
            }
        }

        Mql5PositionType side = order.IsBuySide ? Mql5PositionType.Buy : Mql5PositionType.Sell;
        if (!ValidateStops(side, price, req.Sl, req.Tp, out string stopsError))
        {
            return Fail(result, Mql5TradeRetcode.InvalidStops, stopsError, req);
        }

        order.Price = price;
        order.StopLoss = req.Sl > 0.0 ? spec.NormalizePrice(req.Sl) : 0.0;
        order.TakeProfit = req.Tp > 0.0 ? spec.NormalizePrice(req.Tp) : 0.0;

        Record(new Mql5OrderEvent
        {
            Time = time,
            Kind = Mql5OrderEventKind.PendingModified,
            Ticket = order.Ticket,
            Symbol = order.Symbol,
            Type = order.Type,
            Volume = order.Volume,
            Price = order.Price,
            Balance = balance,
            Retcode = Mql5TradeRetcode.Done,
            Detail = "pending order modified",
        });

        result.Retcode = Mql5TradeRetcode.Done;
        result.Order = order.Ticket;
        result.Price = order.Price;
        result.Comment = "modified";
        return true;
    }

    private bool RemovePending(Mql5TradeRequest req, Mql5TradeResult result)
    {
        Mql5PendingOrder? order = FindPending(req.Order);
        if (order is null)
        {
            return Fail(result, Mql5TradeRetcode.Invalid, "pending order not found", req);
        }

        pendingOrders.Remove(order);
        Record(new Mql5OrderEvent
        {
            Time = time,
            Kind = Mql5OrderEventKind.PendingRemoved,
            Ticket = order.Ticket,
            Symbol = order.Symbol,
            Type = order.Type,
            Volume = order.Volume,
            Price = order.Price,
            Balance = balance,
            Retcode = Mql5TradeRetcode.Done,
            Detail = "pending order removed",
        });

        result.Retcode = Mql5TradeRetcode.Done;
        result.Order = order.Ticket;
        result.Comment = "removed";
        return true;
    }

    private Mql5PendingOrder? FindPending(long ticket)
    {
        foreach (Mql5PendingOrder order in pendingOrders)
        {
            if (order.Ticket == ticket)
            {
                return order;
            }
        }

        return null;
    }

    private double MarketFillPrice(Mql5PositionType side)
    {
        double slippage = options.SlippagePoints * spec.Point;
        return side == Mql5PositionType.Buy
            ? spec.NormalizePrice(Ask + slippage)
            : spec.NormalizePrice(Bid - slippage);
    }

    private bool TryNormalizeVolume(double requested, out double volume, out string error)
    {
        volume = 0.0;
        error = string.Empty;

        if (double.IsNaN(requested) || double.IsInfinity(requested))
        {
            error = "volume is not a number";
            return false;
        }

        if (requested <= 0.0)
        {
            error = "volume must be positive";
            return false;
        }

        if (requested < spec.VolumeMin - VolumeEpsilon)
        {
            error = string.Create(CultureInfo.InvariantCulture, $"volume {requested} below minimum {spec.VolumeMin}");
            return false;
        }

        if (requested > spec.VolumeMax + VolumeEpsilon)
        {
            error = string.Create(CultureInfo.InvariantCulture, $"volume {requested} above maximum {spec.VolumeMax}");
            return false;
        }

        if (spec.VolumeStep > 0.0)
        {
            double steps = requested / spec.VolumeStep;
            if (Math.Abs(steps - Math.Round(steps)) > 1e-6)
            {
                error = string.Create(CultureInfo.InvariantCulture, $"volume {requested} is not a multiple of step {spec.VolumeStep}");
                return false;
            }
        }

        volume = spec.NormalizeVolume(requested);
        return true;
    }

    private bool ValidateStops(Mql5PositionType side, double reference, double sl, double tp, out string error)
    {
        error = string.Empty;
        double minimum = spec.StopsLevelPoints * spec.Point;
        double tolerance = spec.Point / 2.0;

        if (double.IsNaN(sl) || double.IsInfinity(sl) || double.IsNaN(tp) || double.IsInfinity(tp))
        {
            error = "stop price is not a number";
            return false;
        }

        if (side == Mql5PositionType.Buy)
        {
            if (sl > 0.0 && reference - sl < minimum - tolerance)
            {
                error = "buy stop loss must sit below the market by the stops level";
                return false;
            }

            if (tp > 0.0 && tp - reference < minimum - tolerance)
            {
                error = "buy take profit must sit above the market by the stops level";
                return false;
            }
        }
        else
        {
            if (sl > 0.0 && sl - reference < minimum - tolerance)
            {
                error = "sell stop loss must sit above the market by the stops level";
                return false;
            }

            if (tp > 0.0 && reference - tp < minimum - tolerance)
            {
                error = "sell take profit must sit below the market by the stops level";
                return false;
            }
        }

        return true;
    }

    private bool ValidatePendingPrice(Mql5OrderType type, double price, out string error)
    {
        error = string.Empty;
        double minimum = spec.StopsLevelPoints * spec.Point;
        double tolerance = spec.Point / 2.0;
        double ask = Ask;
        double quote = Bid;

        bool ok = type switch
        {
            Mql5OrderType.BuyLimit => ask - price >= minimum - tolerance,
            Mql5OrderType.BuyStop => price - ask >= minimum - tolerance,
            Mql5OrderType.SellLimit => price - quote >= minimum - tolerance,
            Mql5OrderType.SellStop => quote - price >= minimum - tolerance,
            _ => false,
        };

        if (!ok)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"{type} at {price} is on the wrong side of the market (bid={quote} ask={ask})");
        }

        return ok;
    }

    private bool HasMarginFor(Mql5PositionType side, double volume, double price)
    {
        double required = spec.MarginOf(volume, price, options.Leverage);

        if (options.MarginMode == Mql5MarginMode.Netting)
        {
            Mql5Position? existing = FindPositionBySymbol(spec.Name);
            if (existing is not null && existing.Type != side)
            {
                double offset = Math.Min(existing.Volume, volume);
                required = spec.MarginOf(Math.Max(volume - offset, 0.0), price, options.Leverage);
            }
        }

        return required <= FreeMargin + 1e-6;
    }

    private long OpenExposure(
        Mql5PositionType side,
        double volume,
        double price,
        double sl,
        double tp,
        long magic,
        string comment,
        Mql5TradeResult? result)
    {
        double normalizedSl = sl > 0.0 ? spec.NormalizePrice(sl) : 0.0;
        double normalizedTp = tp > 0.0 ? spec.NormalizePrice(tp) : 0.0;
        double commission = -Round2(options.CommissionPerLot * volume);

        if (options.MarginMode == Mql5MarginMode.Netting)
        {
            Mql5Position? existing = FindPositionBySymbol(spec.Name);
            if (existing is not null)
            {
                if (existing.Type == side)
                {
                    double merged = existing.Volume + volume;
                    existing.PriceOpen = spec.NormalizePrice(
                        ((existing.PriceOpen * existing.Volume) + (price * volume)) / merged);
                    existing.Volume = spec.NormalizeVolume(merged);
                    existing.Commission += commission;
                    if (normalizedSl > 0.0)
                    {
                        existing.StopLoss = normalizedSl;
                    }

                    if (normalizedTp > 0.0)
                    {
                        existing.TakeProfit = normalizedTp;
                    }

                    existing.Margin = Round2(spec.MarginOf(existing.Volume, existing.PriceOpen, options.Leverage));
                    Revalue();

                    RecordOpen(existing, volume, price, "netting: volume added");
                    if (result is not null)
                    {
                        result.Position = existing.Ticket;
                    }

                    return existing.Ticket;
                }

                double offset = Math.Min(existing.Volume, volume);
                ClosePortion(existing, offset, price, Mql5CloseReason.Netting);
                Revalue();

                double remainder = spec.NormalizeVolume(volume - offset);
                if (remainder <= VolumeEpsilon)
                {
                    Mql5Position? survivor = FindPositionBySymbol(spec.Name);
                    return survivor?.Ticket ?? 0;
                }

                volume = remainder;
                commission = -Round2(options.CommissionPerLot * volume);
            }
        }

        var position = new Mql5Position
        {
            Ticket = nextTicket++,
            Symbol = spec.Name,
            Type = side,
            Volume = volume,
            PriceOpen = spec.NormalizePrice(price),
            StopLoss = normalizedSl,
            TakeProfit = normalizedTp,
            TimeOpen = time,
            Magic = magic,
            Comment = comment,
            Commission = commission,
            Swap = 0.0,
            PriceCurrent = spec.NormalizePrice(price),
            Profit = 0.0,
            Margin = Round2(spec.MarginOf(volume, price, options.Leverage)),
        };

        positions.Add(position);
        Revalue();
        RecordOpen(position, volume, position.PriceOpen, "position opened");

        if (result is not null)
        {
            result.Position = position.Ticket;
        }

        return position.Ticket;
    }

    private void RecordOpen(Mql5Position position, double volume, double price, string detail) =>
        Record(new Mql5OrderEvent
        {
            Time = time,
            Kind = Mql5OrderEventKind.PositionOpened,
            Ticket = position.Ticket,
            Symbol = position.Symbol,
            Type = position.Type == Mql5PositionType.Buy ? Mql5OrderType.Buy : Mql5OrderType.Sell,
            Volume = volume,
            Price = price,
            Balance = balance,
            Retcode = Mql5TradeRetcode.Done,
            Detail = detail,
        });

    private void ClosePortion(Mql5Position position, double volume, double price, Mql5CloseReason reason)
    {
        if (position.Volume <= VolumeEpsilon)
        {
            return;
        }

        double closeVolume = Math.Min(volume, position.Volume);
        double fraction = closeVolume / position.Volume;
        double closePrice = spec.NormalizePrice(price);

        double delta = position.Type == Mql5PositionType.Buy
            ? closePrice - position.PriceOpen
            : position.PriceOpen - closePrice;

        double gross = Round2(spec.ProfitOf(delta, closeVolume));
        double commission = Round2(position.Commission * fraction);
        double swap = Round2(position.Swap * fraction);

        balance = Round2(balance + gross + commission + swap);

        closedTrades.Add(new Mql5ClosedTrade
        {
            Ticket = position.Ticket,
            Symbol = position.Symbol,
            Type = position.Type,
            Volume = closeVolume,
            PriceOpen = position.PriceOpen,
            PriceClose = closePrice,
            TimeOpen = position.TimeOpen,
            TimeClose = time,
            GrossProfit = gross,
            Commission = commission,
            Swap = swap,
            Reason = reason,
            Magic = position.Magic,
            Comment = position.Comment,
        });

        position.Commission = Round2(position.Commission - commission);
        position.Swap = Round2(position.Swap - swap);
        position.Volume = spec.NormalizeVolume(position.Volume - closeVolume);

        if (position.Volume <= VolumeEpsilon)
        {
            position.Volume = 0.0;
            position.Margin = 0.0;
            positions.Remove(position);
        }
        else
        {
            position.Margin = Round2(spec.MarginOf(position.Volume, position.PriceOpen, options.Leverage));
        }

        Record(new Mql5OrderEvent
        {
            Time = time,
            Kind = Mql5OrderEventKind.PositionClosed,
            Ticket = position.Ticket,
            Symbol = position.Symbol,
            Type = position.Type == Mql5PositionType.Buy ? Mql5OrderType.Buy : Mql5OrderType.Sell,
            Volume = closeVolume,
            Price = closePrice,
            Profit = Round2(gross + commission + swap),
            Balance = balance,
            Retcode = Mql5TradeRetcode.Done,
            Detail = reason.ToString(),
        });
    }

    private bool Fail(Mql5TradeResult result, int retcode, string detail, Mql5TradeRequest? req)
    {
        result.Retcode = retcode;
        result.Comment = detail;
        result.Bid = Bid;
        result.Ask = Ask;

        Record(new Mql5OrderEvent
        {
            Time = time,
            Kind = Mql5OrderEventKind.Rejected,
            Ticket = req?.Position ?? 0,
            Symbol = req?.Symbol ?? spec.Name,
            Type = req?.Type,
            Volume = req?.Volume ?? 0.0,
            Price = req?.Price ?? 0.0,
            Balance = balance,
            Retcode = retcode,
            Detail = detail,
        });

        return false;
    }

    private void Record(Mql5OrderEvent orderEvent) => journal.Add(orderEvent);
}
