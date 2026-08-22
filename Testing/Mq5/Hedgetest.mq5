#property strict
#property description "Original entry / re-entry preserved."
#property description "Exit replaced with: close the opposite trade after a 40-pip move with no 10-pip retracement."

#include <Trade/Trade.mqh>

input string InpSymbol                    = "XAUUSD.s";
input double InpLots                    = 0.01;
input int    InpDeviationPoints           = 50;
input long   InpMagic                    = 260320264;
input bool   InpOnlyM1                    = true;
input bool   InpVerbose                   = true;

// Re-entry behavior (kept from your original EA)
input int    InpReentryMinDelaySec        = 8;
input int    InpReentryForceSec           = 45;
input double InpReentryGapAbs             = 0.45;
input double InpReentryGapSpreadMult      = 1.25;

// New closing mechanism
// For XAUUSD quoted like 3347.43, Point is usually 0.01 and the pip size most traders use is 0.10.
// Auto mode resolves that automatically.
input double InpCloseTargetPips           = 40.0;
input double InpCloseRetracePips          = 10.0;
input double InpCloseLevelStepPips        = 10.0;
input bool   InpCloseAutoPipSize          = true;
input double InpClosePipSize              = 0.10;
input bool   InpVerboseCloseDebug         = false;

CTrade trade;

bool     g_pendingBuy = false;
bool     g_pendingSell = false;
datetime g_pendingBuyTime = 0;
datetime g_pendingSellTime = 0;
double   g_pendingBuyClosePrice = 0.0;
double   g_pendingSellClosePrice = 0.0;

struct SidePos
{
   bool     ok;
   ulong    ticket;
   datetime open_time;
   double   open_price;
   double   pnl;
   double   volume;
   ENUM_POSITION_TYPE type;
};

struct CloseTracker
{
   bool   initialized;

   // Upward run tracker: if price rises cleanly by target_price, close SELL.
   double up_base;     // active start level being tested (40, then 41, then 42...)
   double up_peak;     // highest ASK reached since initialization

   // Downward run tracker: if price falls cleanly by target_price, close BUY.
   double down_base;   // active start level being tested (40, then 39, then 38...)
   double down_low;    // lowest BID reached since initialization
};

CloseTracker g_closeTracker;

enum CloseSignal
{
   CLOSE_SIGNAL_NONE = 0,
   CLOSE_SIGNAL_BUY  = 1,  // close BUY because price moved down cleanly
   CLOSE_SIGNAL_SELL = 2   // close SELL because price moved up cleanly
};

void ResetRuntimeState()
{
   g_pendingBuy = false;
   g_pendingSell = false;
   g_pendingBuyTime = 0;
   g_pendingSellTime = 0;
   g_pendingBuyClosePrice = 0.0;
   g_pendingSellClosePrice = 0.0;

   g_closeTracker.initialized = false;
   g_closeTracker.up_base = 0.0;
   g_closeTracker.up_peak = 0.0;
   g_closeTracker.down_base = 0.0;
   g_closeTracker.down_low = 0.0;
}

int DigitsForSymbol()
{
   return (int)SymbolInfoInteger(InpSymbol, SYMBOL_DIGITS);
}

double NormalizePrice(const double price)
{
   return NormalizeDouble(price, DigitsForSymbol());
}

double EffectivePipSize()
{
   if(!InpCloseAutoPipSize)
      return InpClosePipSize;

   double point = 0.0;
   if(!SymbolInfoDouble(InpSymbol, SYMBOL_POINT, point) || point <= 0.0)
      return InpClosePipSize;

   int digits = DigitsForSymbol();

   // Common MT5 convention:
   // 5 digits EURUSD -> pip = 10 points
   // 3 digits JPY    -> pip = 10 points
   // 2 digits XAUUSD -> pip = 10 points
   if(digits == 5 || digits == 3 || digits == 2)
      return point * 10.0;

   return point;
}

double PriceEpsilon()
{
   double point = 0.0;
   if(!SymbolInfoDouble(InpSymbol, SYMBOL_POINT, point) || point <= 0.0)
      point = 0.00001;
   return point * 0.5;
}

double StepFloor(const double price, const double step)
{
   if(step <= 0.0)
      return NormalizePrice(price);
   return NormalizePrice(MathFloor((price + 1e-12) / step) * step);
}

double StepCeil(const double price, const double step)
{
   if(step <= 0.0)
      return NormalizePrice(price);
   return NormalizePrice(MathCeil((price - 1e-12) / step) * step);
}

bool GetBidAsk(double &bid, double &ask)
{
   MqlTick tick;
   if(!SymbolInfoTick(InpSymbol, tick))
      return false;

   bid = NormalizePrice(tick.bid);
   ask = NormalizePrice(tick.ask);
   return (bid > 0.0 && ask > 0.0);
}

void ResetCloseTracker(const double bid, const double ask)
{
   double pip_price = EffectivePipSize();
   double step_price = InpCloseLevelStepPips * pip_price;
   if(step_price <= 0.0)
      step_price = pip_price;

   g_closeTracker.initialized = true;
   g_closeTracker.up_base = StepFloor(ask, step_price);
   g_closeTracker.up_peak = ask;
   g_closeTracker.down_base = StepCeil(bid, step_price);
   g_closeTracker.down_low = bid;

   if(InpVerbose || InpVerboseCloseDebug)
   {
      Print("Close tracker reset | bid=", DoubleToString(bid, DigitsForSymbol()),
            " ask=", DoubleToString(ask, DigitsForSymbol()),
            " up_base=", DoubleToString(g_closeTracker.up_base, DigitsForSymbol()),
            " down_base=", DoubleToString(g_closeTracker.down_base, DigitsForSymbol()));
   }
}

int CountManaged(const ENUM_POSITION_TYPE side)
{
   int count = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;
      if(!PositionSelectByTicket(ticket))
         continue;
      if(PositionGetString(POSITION_SYMBOL) != InpSymbol)
         continue;
      if((long)PositionGetInteger(POSITION_MAGIC) != InpMagic)
         continue;
      if((ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE) != side)
         continue;
      count++;
   }
   return count;
}

bool GetManagedSide(const ENUM_POSITION_TYPE side, SidePos &out_pos)
{
   out_pos.ok = false;
   out_pos.ticket = 0;
   out_pos.open_time = 0;
   out_pos.open_price = 0.0;
   out_pos.pnl = 0.0;
   out_pos.volume = 0.0;
   out_pos.type = side;

   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;
      if(!PositionSelectByTicket(ticket))
         continue;
      if(PositionGetString(POSITION_SYMBOL) != InpSymbol)
         continue;
      if((long)PositionGetInteger(POSITION_MAGIC) != InpMagic)
         continue;
      if((ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE) != side)
         continue;

      datetime tm = (datetime)PositionGetInteger(POSITION_TIME);
      if(!out_pos.ok || tm < out_pos.open_time)
      {
         out_pos.ok = true;
         out_pos.ticket = ticket;
         out_pos.open_time = tm;
         out_pos.open_price = PositionGetDouble(POSITION_PRICE_OPEN);
         out_pos.pnl = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
         out_pos.volume = PositionGetDouble(POSITION_VOLUME);
         out_pos.type = side;
      }
   }

   return out_pos.ok;
}

void RegisterPendingReentry(const ENUM_POSITION_TYPE side, const double close_price)
{
   if(side == POSITION_TYPE_BUY)
   {
      g_pendingBuy = true;
      g_pendingBuyTime = TimeCurrent();
      g_pendingBuyClosePrice = close_price;
   }
   else if(side == POSITION_TYPE_SELL)
   {
      g_pendingSell = true;
      g_pendingSellTime = TimeCurrent();
      g_pendingSellClosePrice = close_price;
   }
}

bool OpenSide(const ENUM_POSITION_TYPE side)
{
   trade.SetExpertMagicNumber(InpMagic);
   trade.SetDeviationInPoints(InpDeviationPoints);

   bool ok = false;
   if(side == POSITION_TYPE_BUY)
      ok = trade.Buy(InpLots, InpSymbol, 0.0, 0.0, 0.0, "hedge_buy");
   else
      ok = trade.Sell(InpLots, InpSymbol, 0.0, 0.0, 0.0, "hedge_sell");

   if(!ok)
   {
      if(InpVerbose)
         Print("OpenSide failed side=", EnumToString(side),
               " retcode=", trade.ResultRetcode(),
               " desc=", trade.ResultRetcodeDescription());
      return false;
   }

   if(side == POSITION_TYPE_BUY)
   {
      g_pendingBuy = false;
      g_pendingBuyTime = 0;
      g_pendingBuyClosePrice = 0.0;
   }
   else
   {
      g_pendingSell = false;
      g_pendingSellTime = 0;
      g_pendingSellClosePrice = 0.0;
   }

   if(InpVerbose)
      Print("Opened ", EnumToString(side),
            " deal=", trade.ResultDeal(),
            " order=", trade.ResultOrder(),
            " ret=", trade.ResultRetcodeDescription());

   return true;
}

bool CloseManagedTicket(const ulong ticket)
{
   if(ticket == 0)
      return false;
   if(!PositionSelectByTicket(ticket))
      return false;
   if(PositionGetString(POSITION_SYMBOL) != InpSymbol)
      return false;
   if((long)PositionGetInteger(POSITION_MAGIC) != InpMagic)
      return false;

   ENUM_POSITION_TYPE side = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);

   double close_price = 0.0;
   if(side == POSITION_TYPE_BUY)
      SymbolInfoDouble(InpSymbol, SYMBOL_BID, close_price);
   else
      SymbolInfoDouble(InpSymbol, SYMBOL_ASK, close_price);

   trade.SetExpertMagicNumber(InpMagic);
   trade.SetDeviationInPoints(InpDeviationPoints);

   bool ok = trade.PositionClose(ticket, InpDeviationPoints);
   if(!ok)
   {
      if(InpVerbose)
         Print("CloseManagedTicket failed ticket=", ticket,
               " retcode=", trade.ResultRetcode(),
               " desc=", trade.ResultRetcodeDescription());
      return false;
   }

   RegisterPendingReentry(side, close_price);

   // Reset the close tracker so it starts fresh after a position is closed.
   double bid2 = 0.0, ask2 = 0.0;
   if(GetBidAsk(bid2, ask2))
      ResetCloseTracker(bid2, ask2);
   else
      g_closeTracker.initialized = false;

   if(InpVerbose)
      Print("Closed ticket=", ticket,
            " side=", EnumToString(side),
            " close_price=", DoubleToString(close_price, DigitsForSymbol()));

   return true;
}

double CurrentReentryGap()
{
   double bid = 0.0, ask = 0.0;
   SymbolInfoDouble(InpSymbol, SYMBOL_BID, bid);
   SymbolInfoDouble(InpSymbol, SYMBOL_ASK, ask);
   double spread = ask - bid;
   double gap = InpReentryGapAbs;
   double spread_gap = spread * InpReentryGapSpreadMult;
   if(spread_gap > gap)
      gap = spread_gap;
   return gap;
}

bool ReentryReady(const ENUM_POSITION_TYPE side)
{
   double bid = 0.0, ask = 0.0;
   SymbolInfoDouble(InpSymbol, SYMBOL_BID, bid);
   SymbolInfoDouble(InpSymbol, SYMBOL_ASK, ask);

   double gap = CurrentReentryGap();

   if(side == POSITION_TYPE_BUY)
   {
      if(!g_pendingBuy)
         return true;

      int elapsed = (int)(TimeCurrent() - g_pendingBuyTime);
      if(elapsed < InpReentryMinDelaySec)
         return false;

      double improve = g_pendingBuyClosePrice - ask;
      if(improve >= gap)
         return true;
      if(elapsed >= InpReentryForceSec)
         return true;
      return false;
   }

   if(!g_pendingSell)
      return true;

   int elapsed = (int)(TimeCurrent() - g_pendingSellTime);
   if(elapsed < InpReentryMinDelaySec)
      return false;

   double improve = bid - g_pendingSellClosePrice;
   if(improve >= gap)
      return true;
   if(elapsed >= InpReentryForceSec)
      return true;
   return false;
}

void MaintainInventory()
{
   int buy_count = CountManaged(POSITION_TYPE_BUY);
   int sell_count = CountManaged(POSITION_TYPE_SELL);

   if(buy_count == 0 && sell_count == 0)
   {
      OpenSide(POSITION_TYPE_BUY);
      OpenSide(POSITION_TYPE_SELL);
      return;
   }

   if(buy_count == 0 && ReentryReady(POSITION_TYPE_BUY))
      OpenSide(POSITION_TYPE_BUY);

   if(sell_count == 0 && ReentryReady(POSITION_TYPE_SELL))
      OpenSide(POSITION_TYPE_SELL);
}

CloseSignal EvaluateCloseSignal()
{
   double pip_price    = EffectivePipSize();
   double target_price = InpCloseTargetPips * pip_price;
   double retrace_price = InpCloseRetracePips * pip_price;
   double step_price   = InpCloseLevelStepPips * pip_price;
   double eps          = PriceEpsilon();

   if(target_price <= 0.0 || retrace_price <= 0.0 || step_price <= 0.0)
      return CLOSE_SIGNAL_NONE;

   double bid = 0.0, ask = 0.0;
   if(!GetBidAsk(bid, ask))
      return CLOSE_SIGNAL_NONE;

   if(!g_closeTracker.initialized)
   {
      ResetCloseTracker(bid, ask);
      return CLOSE_SIGNAL_NONE;
   }

   // Pure tick-by-tick tracking of extremes.
   if(ask > g_closeTracker.up_peak)
      g_closeTracker.up_peak = ask;

   if(bid < g_closeTracker.down_low)
      g_closeTracker.down_low = bid;

   double up_run = g_closeTracker.up_peak - g_closeTracker.up_base;
   double up_pullback = g_closeTracker.up_peak - ask;

   if(up_run + eps >= target_price)
   {
      if(InpVerbose || InpVerboseCloseDebug)
      {
         Print("UP clean move detected | base=", DoubleToString(g_closeTracker.up_base, DigitsForSymbol()),
               " peak=", DoubleToString(g_closeTracker.up_peak, DigitsForSymbol()),
               " ask=", DoubleToString(ask, DigitsForSymbol()),
               " move=", DoubleToString(up_run, DigitsForSymbol()),
               " -> close SELL");
      }

      ResetCloseTracker(bid, ask);
      return CLOSE_SIGNAL_SELL;
   }

   if(up_pullback + eps >= retrace_price && up_run + eps < target_price)
   {
      double reached_steps = MathFloor((up_run + eps) / step_price);
      if(reached_steps >= 1.0)
      {
         double new_up_base = NormalizePrice(g_closeTracker.up_base + reached_steps * step_price);
         if(new_up_base > g_closeTracker.up_base + eps)
         {
            if(InpVerbose || InpVerboseCloseDebug)
            {
               Print("UP retracement detected | old_base=", DoubleToString(g_closeTracker.up_base, DigitsForSymbol()),
                    " new_base=", DoubleToString(new_up_base, DigitsForSymbol()),
                    " up_peak=", DoubleToString(g_closeTracker.up_peak, DigitsForSymbol()),
                    " ask=", DoubleToString(ask, DigitsForSymbol()));
            }

            g_closeTracker.up_base = new_up_base;
            g_closeTracker.up_peak = MathMax(ask, g_closeTracker.up_base);
         }
      }
   }

   if(ask < g_closeTracker.up_base - step_price - eps)
   {
      g_closeTracker.up_base = StepFloor(ask, step_price);
      g_closeTracker.up_peak = ask;
      if(InpVerboseCloseDebug)
         Print("UP tracker re-armed | up_base=", DoubleToString(g_closeTracker.up_base, DigitsForSymbol()));
   }

   double down_run = g_closeTracker.down_base - g_closeTracker.down_low;
   double down_pullback = bid - g_closeTracker.down_low;

   if(down_run + eps >= target_price)
   {
      if(InpVerbose || InpVerboseCloseDebug)
      {
         Print("DOWN clean move detected | base=", DoubleToString(g_closeTracker.down_base, DigitsForSymbol()),
               " low=", DoubleToString(g_closeTracker.down_low, DigitsForSymbol()),
               " bid=", DoubleToString(bid, DigitsForSymbol()),
               " move=", DoubleToString(down_run, DigitsForSymbol()),
               " -> close BUY");
      }

      ResetCloseTracker(bid, ask);
      return CLOSE_SIGNAL_BUY;
   }

   if(down_pullback + eps >= retrace_price && down_run + eps < target_price)
   {
      double reached_steps = MathFloor((down_run + eps) / step_price);
      if(reached_steps >= 1.0)
      {
         double new_down_base = NormalizePrice(g_closeTracker.down_base - reached_steps * step_price);
         if(new_down_base < g_closeTracker.down_base - eps)
         {
            if(InpVerbose || InpVerboseCloseDebug)
            {
               Print("DOWN retracement detected | old_base=", DoubleToString(g_closeTracker.down_base, DigitsForSymbol()),
                    " new_base=", DoubleToString(new_down_base, DigitsForSymbol()),
                    " down_low=", DoubleToString(g_closeTracker.down_low, DigitsForSymbol()),
                    " bid=", DoubleToString(bid, DigitsForSymbol()));
            }

            g_closeTracker.down_base = new_down_base;
            g_closeTracker.down_low = MathMin(bid, g_closeTracker.down_base);
         }
      }
   }

   if(bid > g_closeTracker.down_base + step_price + eps)
   {
      g_closeTracker.down_base = StepCeil(bid, step_price);
      g_closeTracker.down_low = bid;
      if(InpVerboseCloseDebug)
         Print("DOWN tracker re-armed | down_base=", DoubleToString(g_closeTracker.down_base, DigitsForSymbol()));
   }

   return CLOSE_SIGNAL_NONE;
}

bool ManageCloseMechanism()
{
   SidePos buy, sell;
   bool have_buy = GetManagedSide(POSITION_TYPE_BUY, buy);
   bool have_sell = GetManagedSide(POSITION_TYPE_SELL, sell);

   // Always advance the tracker so it stays current even in single-position state.
   // Only act on the signal when both sides are present (hedged state).
   CloseSignal sig = EvaluateCloseSignal();

   if(!have_buy || !have_sell)
      return false;

   if(sig == CLOSE_SIGNAL_SELL)
      return CloseManagedTicket(sell.ticket);

   if(sig == CLOSE_SIGNAL_BUY)
      return CloseManagedTicket(buy.ticket);

   return false;
}

int OnInit()
{
   ResetRuntimeState();

   trade.SetExpertMagicNumber(InpMagic);
   trade.SetDeviationInPoints(InpDeviationPoints);
   trade.SetTypeFillingBySymbol(InpSymbol);

   if(InpOnlyM1 && Period() != PERIOD_M1)
      Print("Warning: this EA was designed to run on M1.");

   double point = 0.0;
   SymbolInfoDouble(InpSymbol, SYMBOL_POINT, point);
   double pip = EffectivePipSize();

   if(pip <= 0.0)
      Print("Warning: resolved close pip size must be > 0.");

   Print("Close settings | symbol=", InpSymbol,
         " digits=", DigitsForSymbol(),
         " point=", DoubleToString(point, DigitsForSymbol()),
         " pip_size=", DoubleToString(pip, DigitsForSymbol()),
         " target_price=", DoubleToString(InpCloseTargetPips * pip, DigitsForSymbol()),
         " retrace_price=", DoubleToString(InpCloseRetracePips * pip, DigitsForSymbol()));

   return(INIT_SUCCEEDED);
}

void OnTick()
{
   if(InpOnlyM1 && Period() != PERIOD_M1)
      return;
   if(Symbol() != InpSymbol)
      return;

   ManageCloseMechanism();

   // Keep the original entry / re-entry logic, but do it after the close check
   // so the exit can fire on the first qualifying tick.
   MaintainInventory();
}