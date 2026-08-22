//+------------------------------------------------------------------+
//|                 MomentumHedgeEA_LiveSafe_A.mq5                   |
//|                                                                  |
//|  Core logic:                                                     |
//|   - Maintains 1 BUY + 1 SELL                                     |
//|   - Closes opposite side after clean 40-pip move                 |
//|   - Reopens missing hedge after 10-pip reversal                  |
//|   - Optional main cycle reset after large favorable move         |
//|   - Includes pause logic, HWM/DD, daily limit, float lock, panel |
//+------------------------------------------------------------------+
#property strict
#property version   "1.00"
#property description "Momentum hedge EA with protections and panel"

#include <Trade/Trade.mqh>

//==================================================================//
// INPUTS
//==================================================================//
input group "=== Trade ==="
input double   InpLots                    = 0.01;
input int      InpDeviationPoints         = 50;
input long     InpMagicBuy                = 260320264;
input long     InpMagicSell               = 260320265;
input bool     InpOnlyM1                  = true;
input double   InpMaxSpreadPoints         = 30.0;

input group "=== Entry / Reentry ==="
input int      InpReentryMinDelaySec      = 8;
input int      InpReentryForceSec         = 45;
input double   InpReentryGapAbs           = 0.45;
input double   InpReentryGapSpreadMult    = 1.25;

input group "=== Momentum Close Logic ==="
input double   InpCloseTargetPips         = 40.0;
input double   InpCloseRetracePips        = 10.0;
input double   InpCloseLevelStepPips      = 10.0;
input bool     InpCloseAutoPipSize        = true;
input double   InpClosePipSize            = 0.10;

input group "=== Main Cycle Reset ==="
input bool     InpEnableCycleReset        = true;
input double   InpCycleResetPips          = 320.0;

input group "=== Float Protection ==="
input bool     UseFloatLock               = true;
input double   FloatLockMinUSD            = 5.0;
input double   FloatLockDropUSD           = 3.0;

input group "=== Account Protection ==="
input bool     UseHWM                     = true;
input double   MaxDDFromPeakPct           = 10.0;
input bool     UseDailyLimit              = true;
input double   DailyLossLimitUSD          = 30.0;
input bool     UseEmergencyFloatStop      = true;
input double   EmergencyFloatLossUSD      = 100.0;

input group "=== Pause Float Monitor ==="
input bool     MonitorDuringPause         = true;
input double   PauseFloatTargetUSD        = 10.0;

input group "=== Logging / Panel ==="
input bool     InpVerbose                 = true;
input bool     InpVerboseCloseDebug       = false;
input bool     ShowPanel                  = true;
input int      PanelX                     = 10;
input int      PanelY                     = 30;

//==================================================================//
// TYPES
//==================================================================//
enum EState
{
   S_IDLE    = 0,
   S_ACTIVE  = 1,
   S_CLOSING = 2
};

enum CloseSignal
{
   CLOSE_SIGNAL_NONE = 0,
   CLOSE_SIGNAL_BUY  = 1,
   CLOSE_SIGNAL_SELL = 2
};

struct SidePos
{
   bool               ok;
   ulong              ticket;
   datetime           open_time;
   double             open_price;
   double             pnl;
   double             volume;
   ENUM_POSITION_TYPE type;
};

struct CloseTracker
{
   bool   initialized;
   double up_base;
   double up_peak;
   double down_base;
   double down_low;
};

struct HedgeTracker
{
   bool               active;
   ENUM_POSITION_TYPE closed_side;
   ENUM_POSITION_TYPE active_side;
   double             trigger_price;
};

struct PendingReentry
{
   bool     active;
   datetime start_time;
   double   close_price;
};

//==================================================================//
// GLOBALS
//==================================================================//
CTrade g_trade;

EState         g_state            = S_IDLE;
CloseTracker   g_closeTracker;
HedgeTracker   g_hedgeTracker;
PendingReentry g_pendingBuy;
PendingReentry g_pendingSell;

ulong          g_mainBuyTicket    = 0;
ulong          g_mainSellTicket   = 0;

double         g_initialBalance   = 0.0;
double         g_dayStartBalance  = 0.0;
double         g_highWaterMark    = 0.0;
double         g_floatPeak        = 0.0;
datetime       g_lastDayReset     = 0;

bool           g_paused           = false;
string         g_pauseReason      = "";
string         g_status           = "Starting...";
bool           g_firstRun         = true;

#define PN "MHEA_"
#define PANEL_ROWS 15

//==================================================================//
// HELPERS
//==================================================================//
string TradeSymbol()
{
   return _Symbol;
}

string YesNo(const bool v)
{
   return v ? "YES" : "NO";
}

long MagicForSide(const ENUM_POSITION_TYPE side)
{
   return (side == POSITION_TYPE_BUY ? InpMagicBuy : InpMagicSell);
}

bool IsOurMagic(const long magic)
{
   return (magic == InpMagicBuy || magic == InpMagicSell);
}

int DigitsForSymbol()
{
   return (int)SymbolInfoInteger(TradeSymbol(), SYMBOL_DIGITS);
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
   if(!SymbolInfoDouble(TradeSymbol(), SYMBOL_POINT, point) || point <= 0.0)
      return InpClosePipSize;

   int digits = DigitsForSymbol();

   if(digits == 2 || digits == 3 || digits == 5 || digits == 6)
      return point * 10.0;

   return point;
}

double PriceEpsilon()
{
   double point = 0.0;
   if(!SymbolInfoDouble(TradeSymbol(), SYMBOL_POINT, point) || point <= 0.0)
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
   if(!SymbolInfoTick(TradeSymbol(), tick))
      return false;

   bid = NormalizePrice(tick.bid);
   ask = NormalizePrice(tick.ask);

   return (bid > 0.0 && ask > 0.0);
}

bool SpreadTooWide()
{
   double spread = (double)SymbolInfoInteger(TradeSymbol(), SYMBOL_SPREAD);
   if(spread > InpMaxSpreadPoints)
   {
      g_status = "Spread too wide";
      return true;
   }
   return false;
}

//==================================================================//
// RESET
//==================================================================//
void ResetPendingReentries()
{
   g_pendingBuy.active      = false;
   g_pendingBuy.start_time  = 0;
   g_pendingBuy.close_price = 0.0;

   g_pendingSell.active      = false;
   g_pendingSell.start_time  = 0;
   g_pendingSell.close_price = 0.0;
}

void ResetCloseTracker(const double bid, const double ask)
{
   double pip_price  = EffectivePipSize();
   double step_price = InpCloseLevelStepPips * pip_price;
   if(step_price <= 0.0)
      step_price = pip_price;

   g_closeTracker.initialized = true;
   g_closeTracker.up_base     = StepFloor(ask, step_price);
   g_closeTracker.up_peak     = ask;
   g_closeTracker.down_base   = StepCeil(bid, step_price);
   g_closeTracker.down_low    = bid;

   if(InpVerboseCloseDebug)
      Print("Close tracker reset");
}

void ResetRuntimeState()
{
   ResetPendingReentries();

   g_mainBuyTicket  = 0;
   g_mainSellTicket = 0;

   g_closeTracker.initialized = false;
   g_closeTracker.up_base     = 0.0;
   g_closeTracker.up_peak     = 0.0;
   g_closeTracker.down_base   = 0.0;
   g_closeTracker.down_low    = 0.0;

   g_hedgeTracker.active        = false;
   g_hedgeTracker.closed_side   = POSITION_TYPE_BUY;
   g_hedgeTracker.active_side   = POSITION_TYPE_SELL;
   g_hedgeTracker.trigger_price = 0.0;

   g_floatPeak = 0.0;
}

//==================================================================//
// POSITION HELPERS
//==================================================================//
int CountManaged(const ENUM_POSITION_TYPE side)
{
   int count = 0;

   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL) != TradeSymbol()) continue;
      if((long)PositionGetInteger(POSITION_MAGIC) != MagicForSide(side)) continue;
      if((ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE) != side) continue;

      count++;
   }

   return count;
}

bool GetManagedSide(const ENUM_POSITION_TYPE side, SidePos &out_pos)
{
   out_pos.ok         = false;
   out_pos.ticket     = 0;
   out_pos.open_time  = 0;
   out_pos.open_price = 0.0;
   out_pos.pnl        = 0.0;
   out_pos.volume     = 0.0;
   out_pos.type       = side;

   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL) != TradeSymbol()) continue;
      if((long)PositionGetInteger(POSITION_MAGIC) != MagicForSide(side)) continue;
      if((ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE) != side) continue;

      datetime tm = (datetime)PositionGetInteger(POSITION_TIME);

      if(!out_pos.ok || tm < out_pos.open_time)
      {
         out_pos.ok         = true;
         out_pos.ticket     = ticket;
         out_pos.open_time  = tm;
         out_pos.open_price = PositionGetDouble(POSITION_PRICE_OPEN);
         out_pos.pnl        = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
         out_pos.volume     = PositionGetDouble(POSITION_VOLUME);
         out_pos.type       = side;
      }
   }

   return out_pos.ok;
}

bool GetManagedTicketInfo(const ulong ticket, SidePos &out_pos)
{
   out_pos.ok         = false;
   out_pos.ticket     = 0;
   out_pos.open_time  = 0;
   out_pos.open_price = 0.0;
   out_pos.pnl        = 0.0;
   out_pos.volume     = 0.0;
   out_pos.type       = POSITION_TYPE_BUY;

   if(ticket == 0) return false;
   if(!PositionSelectByTicket(ticket)) return false;
   if(PositionGetString(POSITION_SYMBOL) != TradeSymbol()) return false;

   ENUM_POSITION_TYPE side = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
   if((long)PositionGetInteger(POSITION_MAGIC) != MagicForSide(side)) return false;

   out_pos.ok         = true;
   out_pos.ticket     = ticket;
   out_pos.open_time  = (datetime)PositionGetInteger(POSITION_TIME);
   out_pos.open_price = PositionGetDouble(POSITION_PRICE_OPEN);
   out_pos.pnl        = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
   out_pos.volume     = PositionGetDouble(POSITION_VOLUME);
   out_pos.type       = side;

   return true;
}

ulong FindNewestManagedTicket(const ENUM_POSITION_TYPE side)
{
   ulong newest_ticket = 0;
   datetime newest_time = 0;

   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL) != TradeSymbol()) continue;
      if((long)PositionGetInteger(POSITION_MAGIC) != MagicForSide(side)) continue;
      if((ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE) != side) continue;

      datetime tm = (datetime)PositionGetInteger(POSITION_TIME);
      if(newest_ticket == 0 || tm > newest_time)
      {
         newest_ticket = ticket;
         newest_time   = tm;
      }
   }

   return newest_ticket;
}

double TotalAllFloatingPnl()
{
   double total = 0.0;

   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL) != TradeSymbol()) continue;

      long mg = (long)PositionGetInteger(POSITION_MAGIC);
      if(!IsOurMagic(mg)) continue;

      total += PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
   }

   return total;
}

bool HasAnyPosition()
{
   return (CountManaged(POSITION_TYPE_BUY) > 0 || CountManaged(POSITION_TYPE_SELL) > 0);
}

//==================================================================//
// EXECUTION
//==================================================================//
bool OpenSide(const ENUM_POSITION_TYPE side, const string comment = "")
{
   g_trade.SetExpertMagicNumber(MagicForSide(side));
   g_trade.SetDeviationInPoints(InpDeviationPoints);
   g_trade.SetTypeFillingBySymbol(TradeSymbol());

   bool ok = false;

   if(side == POSITION_TYPE_BUY)
      ok = g_trade.Buy(InpLots, TradeSymbol(), 0.0, 0.0, 0.0, comment);
   else
      ok = g_trade.Sell(InpLots, TradeSymbol(), 0.0, 0.0, 0.0, comment);

   if(!ok)
   {
      if(InpVerbose)
         Print("Open failed: ", EnumToString(side), " | ", g_trade.ResultRetcodeDescription());
      return false;
   }

   if(side == POSITION_TYPE_BUY)
      g_pendingBuy.active = false;
   else
      g_pendingSell.active = false;

   return true;
}

bool CloseTicket(const ulong ticket)
{
   if(ticket == 0) return false;

   g_trade.SetDeviationInPoints(InpDeviationPoints);

   bool ok = g_trade.PositionClose(ticket, InpDeviationPoints);
   if(!ok && InpVerbose)
      Print("Close failed ticket=", ticket, " | ", g_trade.ResultRetcodeDescription());

   return ok;
}

bool CloseManagedTicket(const ulong ticket)
{
   if(ticket == 0) return false;
   if(!PositionSelectByTicket(ticket)) return false;
   if(PositionGetString(POSITION_SYMBOL) != TradeSymbol()) return false;

   ENUM_POSITION_TYPE side = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
   long mg = (long)PositionGetInteger(POSITION_MAGIC);
   if(mg != MagicForSide(side)) return false;

   double close_price = 0.0;
   if(side == POSITION_TYPE_BUY)
      SymbolInfoDouble(TradeSymbol(), SYMBOL_BID, close_price);
   else
      SymbolInfoDouble(TradeSymbol(), SYMBOL_ASK, close_price);

   bool ok = CloseTicket(ticket);
   if(!ok) return false;

   if(side == POSITION_TYPE_BUY)
   {
      g_mainBuyTicket = 0;
      g_pendingBuy.active      = true;
      g_pendingBuy.start_time  = TimeCurrent();
      g_pendingBuy.close_price = close_price;
   }
   else
   {
      g_mainSellTicket = 0;
      g_pendingSell.active      = true;
      g_pendingSell.start_time  = TimeCurrent();
      g_pendingSell.close_price = close_price;
   }

   // arm hedge trigger
   g_hedgeTracker.active      = true;
   g_hedgeTracker.closed_side = side;
   g_hedgeTracker.active_side = (side == POSITION_TYPE_BUY ? POSITION_TYPE_SELL : POSITION_TYPE_BUY);

   double bid = 0.0, ask = 0.0;
   if(GetBidAsk(bid, ask))
   {
      double retrace_price = InpCloseRetracePips * EffectivePipSize();
      if(g_hedgeTracker.active_side == POSITION_TYPE_BUY)
         g_hedgeTracker.trigger_price = ask - retrace_price;
      else
         g_hedgeTracker.trigger_price = bid + retrace_price;
   }

   if(InpVerbose)
      Print("Closed managed side: ", EnumToString(side));

   return true;
}

bool CloseManagedSide(const ENUM_POSITION_TYPE side)
{
   SidePos pos;
   if(!GetManagedSide(side, pos))
      return false;

   return CloseManagedTicket(pos.ticket);
}

bool CloseAllOurs()
{
   bool all_ok = true;

   ulong tickets[];
   ArrayResize(tickets, 0);

   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL) != TradeSymbol()) continue;

      long mg = (long)PositionGetInteger(POSITION_MAGIC);
      if(!IsOurMagic(mg)) continue;

      int sz = ArraySize(tickets);
      ArrayResize(tickets, sz + 1);
      tickets[sz] = ticket;
   }

   for(int j = 0; j < ArraySize(tickets); j++)
   {
      if(!CloseTicket(tickets[j]))
         all_ok = false;
   }

   return all_ok;
}

//==================================================================//
// REENTRY / HEDGE
//==================================================================//
double CurrentReentryGap()
{
   double bid = 0.0, ask = 0.0;
   if(!GetBidAsk(bid, ask))
      return InpReentryGapAbs;

   double spread     = ask - bid;
   double gap        = InpReentryGapAbs;
   double spread_gap = spread * InpReentryGapSpreadMult;

   if(spread_gap > gap)
      gap = spread_gap;

   return gap;
}

bool ReentryReady(const ENUM_POSITION_TYPE side)
{
   double bid = 0.0, ask = 0.0;
   if(!GetBidAsk(bid, ask))
      return false;

   double gap = CurrentReentryGap();

   if(side == POSITION_TYPE_BUY)
   {
      if(!g_pendingBuy.active || g_pendingBuy.start_time <= 0)
         return true;

      int elapsed = (int)(TimeCurrent() - g_pendingBuy.start_time);
      if(elapsed < InpReentryMinDelaySec) return false;

      double improve = g_pendingBuy.close_price - ask;
      if(improve >= gap) return true;
      if(elapsed >= InpReentryForceSec) return true;
      return false;
   }

   if(!g_pendingSell.active || g_pendingSell.start_time <= 0)
      return true;

   int elapsed = (int)(TimeCurrent() - g_pendingSell.start_time);
   if(elapsed < InpReentryMinDelaySec) return false;

   double improve = bid - g_pendingSell.close_price;
   if(improve >= gap) return true;
   if(elapsed >= InpReentryForceSec) return true;
   return false;
}

void CheckHedgeTrigger()
{
   if(!g_hedgeTracker.active)
      return;

   if(g_hedgeTracker.closed_side == POSITION_TYPE_BUY && CountManaged(POSITION_TYPE_BUY) > 0)
   {
      g_hedgeTracker.active = false;
      return;
   }

   if(g_hedgeTracker.closed_side == POSITION_TYPE_SELL && CountManaged(POSITION_TYPE_SELL) > 0)
   {
      g_hedgeTracker.active = false;
      return;
   }

   double bid = 0.0, ask = 0.0;
   if(!GetBidAsk(bid, ask))
      return;

   double retrace_price = InpCloseRetracePips * EffectivePipSize();
   bool triggered = false;

   if(g_hedgeTracker.active_side == POSITION_TYPE_BUY)
   {
      double new_trigger = ask - retrace_price;
      if(new_trigger > g_hedgeTracker.trigger_price)
         g_hedgeTracker.trigger_price = new_trigger;

      if(bid <= g_hedgeTracker.trigger_price)
         triggered = true;
   }
   else
   {
      double new_trigger = bid + retrace_price;
      if(new_trigger < g_hedgeTracker.trigger_price)
         g_hedgeTracker.trigger_price = new_trigger;

      if(ask >= g_hedgeTracker.trigger_price)
         triggered = true;
   }

   if(!triggered)
      return;

   if(!ReentryReady(g_hedgeTracker.closed_side))
      return;

   if(OpenSide(g_hedgeTracker.closed_side, "hedge_reentry"))
   {
      if(g_hedgeTracker.closed_side == POSITION_TYPE_BUY)
         g_mainBuyTicket = FindNewestManagedTicket(POSITION_TYPE_BUY);
      else
         g_mainSellTicket = FindNewestManagedTicket(POSITION_TYPE_SELL);

      g_hedgeTracker.active = false;

      if(InpVerbose)
         Print("Hedge reentry opened: ", EnumToString(g_hedgeTracker.closed_side));
   }
}

//==================================================================//
// MAIN LOGIC
//==================================================================//
CloseSignal EvaluateCloseSignal()
{
   double pip_price     = EffectivePipSize();
   double target_price  = InpCloseTargetPips * pip_price;
   double retrace_price = InpCloseRetracePips * pip_price;
   double step_price    = InpCloseLevelStepPips * pip_price;
   double eps           = PriceEpsilon();

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

   if(ask > g_closeTracker.up_peak)
      g_closeTracker.up_peak = ask;
   if(bid < g_closeTracker.down_low)
      g_closeTracker.down_low = bid;

   // UP move => close SELL
   double up_run      = g_closeTracker.up_peak - g_closeTracker.up_base;
   double up_pullback = g_closeTracker.up_peak - ask;

   if(up_run + eps >= target_price)
   {
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
            g_closeTracker.up_base = new_up_base;
            g_closeTracker.up_peak = MathMax(ask, g_closeTracker.up_base);
         }
      }
   }

   if(ask < g_closeTracker.up_base - step_price - eps)
   {
      g_closeTracker.up_base = StepFloor(ask, step_price);
      g_closeTracker.up_peak = ask;
   }

   // DOWN move => close BUY
   double down_run      = g_closeTracker.down_base - g_closeTracker.down_low;
   double down_pullback = bid - g_closeTracker.down_low;

   if(down_run + eps >= target_price)
   {
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
            g_closeTracker.down_base = new_down_base;
            g_closeTracker.down_low  = MathMin(bid, g_closeTracker.down_base);
         }
      }
   }

   if(bid > g_closeTracker.down_base + step_price + eps)
   {
      g_closeTracker.down_base = StepCeil(bid, step_price);
      g_closeTracker.down_low  = bid;
   }

   return CLOSE_SIGNAL_NONE;
}

void SyncMainTickets()
{
   if(CountManaged(POSITION_TYPE_BUY) == 0)
      g_mainBuyTicket = 0;
   else if(g_mainBuyTicket == 0)
      g_mainBuyTicket = FindNewestManagedTicket(POSITION_TYPE_BUY);

   if(CountManaged(POSITION_TYPE_SELL) == 0)
      g_mainSellTicket = 0;
   else if(g_mainSellTicket == 0)
      g_mainSellTicket = FindNewestManagedTicket(POSITION_TYPE_SELL);
}

bool CheckCycleReset()
{
   if(!InpEnableCycleReset)
      return false;

   double pip_price   = EffectivePipSize();
   double reset_price = InpCycleResetPips * pip_price;
   double eps         = PriceEpsilon();

   if(reset_price <= 0.0)
      return false;

   double bid = 0.0, ask = 0.0;
   if(!GetBidAsk(bid, ask))
      return false;

   SidePos main_buy, main_sell;

   if(GetManagedTicketInfo(g_mainBuyTicket, main_buy))
   {
      double buy_move = bid - main_buy.open_price;
      if(buy_move + eps >= reset_price)
      {
         if(InpVerbose)
            Print("Cycle reset MAIN BUY");
         g_mainBuyTicket = 0;
         return CloseManagedTicket(main_buy.ticket);
      }
   }
   else
      g_mainBuyTicket = 0;

   if(GetManagedTicketInfo(g_mainSellTicket, main_sell))
   {
      double sell_move = main_sell.open_price - ask;
      if(sell_move + eps >= reset_price)
      {
         if(InpVerbose)
            Print("Cycle reset MAIN SELL");
         g_mainSellTicket = 0;
         return CloseManagedTicket(main_sell.ticket);
      }
   }
   else
      g_mainSellTicket = 0;

   return false;
}

void MaintainInventory()
{
   SyncMainTickets();

   int buy_count  = CountManaged(POSITION_TYPE_BUY);
   int sell_count = CountManaged(POSITION_TYPE_SELL);

   if(buy_count == 0 && sell_count == 0)
   {
      g_mainBuyTicket  = 0;
      g_mainSellTicket = 0;
      g_hedgeTracker.active = false;

      bool okBuy  = OpenSide(POSITION_TYPE_BUY, "initial_buy");
      bool okSell = OpenSide(POSITION_TYPE_SELL, "initial_sell");

      if(okBuy)  g_mainBuyTicket  = FindNewestManagedTicket(POSITION_TYPE_BUY);
      if(okSell) g_mainSellTicket = FindNewestManagedTicket(POSITION_TYPE_SELL);

      double bid = 0.0, ask = 0.0;
      if(GetBidAsk(bid, ask))
         ResetCloseTracker(bid, ask);

      return;
   }

   if(buy_count == 0)
      g_mainBuyTicket = 0;
   if(sell_count == 0)
      g_mainSellTicket = 0;

   if(buy_count == 0 && g_hedgeTracker.active && g_hedgeTracker.closed_side == POSITION_TYPE_BUY)
      return;

   if(sell_count == 0 && g_hedgeTracker.active && g_hedgeTracker.closed_side == POSITION_TYPE_SELL)
      return;

   if(buy_count == 0 && ReentryReady(POSITION_TYPE_BUY))
   {
      if(OpenSide(POSITION_TYPE_BUY, "rebuild_buy"))
         g_mainBuyTicket = FindNewestManagedTicket(POSITION_TYPE_BUY);
   }

   if(sell_count == 0 && ReentryReady(POSITION_TYPE_SELL))
   {
      if(OpenSide(POSITION_TYPE_SELL, "rebuild_sell"))
         g_mainSellTicket = FindNewestManagedTicket(POSITION_TYPE_SELL);
   }
}

//==================================================================//
// PROTECTION
//==================================================================//
void CheckDayReset()
{
   if(!UseDailyLimit && !UseHWM)
      return;

   datetime todayBar = iTime(TradeSymbol(), PERIOD_D1, 0);
   if(todayBar != g_lastDayReset)
   {
      g_lastDayReset    = todayBar;
      g_dayStartBalance = AccountInfoDouble(ACCOUNT_BALANCE);

      if(g_paused && StringFind(g_pauseReason, "Daily") >= 0)
      {
         g_paused      = false;
         g_pauseReason = "";
         if(InpVerbose)
            Print("Daily pause lifted.");
      }
   }
}

//==================================================================//
// PANEL
//==================================================================//
void BuildPanel()
{
   for(int i = ObjectsTotal(0, 0, -1) - 1; i >= 0; i--)
   {
      string nm = ObjectName(0, i, 0, -1);
      if(StringFind(nm, PN) == 0)
         ObjectDelete(0, nm);
   }

   ObjectCreate(0, PN + "bg", OBJ_RECTANGLE_LABEL, 0, 0, 0);
   ObjectSetInteger(0, PN + "bg", OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetInteger(0, PN + "bg", OBJPROP_XDISTANCE, PanelX - 5);
   ObjectSetInteger(0, PN + "bg", OBJPROP_YDISTANCE, PanelY - 5);
   ObjectSetInteger(0, PN + "bg", OBJPROP_XSIZE, 470);
   ObjectSetInteger(0, PN + "bg", OBJPROP_YSIZE, PANEL_ROWS * 21 + 10);
   ObjectSetInteger(0, PN + "bg", OBJPROP_BGCOLOR, C'8,12,30');
   ObjectSetInteger(0, PN + "bg", OBJPROP_BORDER_TYPE, BORDER_FLAT);
   ObjectSetInteger(0, PN + "bg", OBJPROP_COLOR, C'0,160,100');
   ObjectSetInteger(0, PN + "bg", OBJPROP_BACK, true);

   for(int i = 0; i < PANEL_ROWS; i++)
   {
      string nm = PN + "r" + IntegerToString(i);
      ObjectCreate(0, nm, OBJ_LABEL, 0, 0, 0);
      ObjectSetInteger(0, nm, OBJPROP_CORNER, CORNER_LEFT_UPPER);
      ObjectSetInteger(0, nm, OBJPROP_XDISTANCE, PanelX + 4);
      ObjectSetInteger(0, nm, OBJPROP_YDISTANCE, PanelY + i * 21);
      ObjectSetString(0, nm, OBJPROP_FONT, "Consolas");
      ObjectSetInteger(0, nm, OBJPROP_FONTSIZE, 8);
      ObjectSetInteger(0, nm, OBJPROP_COLOR, clrWhite);
      ObjectSetString(0, nm, OBJPROP_TEXT, "");
   }
}

void PanelRow(const int row, const string txt, const color clr)
{
   if(row < 0 || row >= PANEL_ROWS)
      return;

   string key = PN + "r" + IntegerToString(row);
   if(ObjectFind(0, key) < 0)
      return;

   ObjectSetString(0, key, OBJPROP_TEXT, txt);
   ObjectSetInteger(0, key, OBJPROP_COLOR, clr);
}

void UpdatePanel()
{
   double bal      = AccountInfoDouble(ACCOUNT_BALANCE);
   double eq       = AccountInfoDouble(ACCOUNT_EQUITY);
   double floatPnl = TotalAllFloatingPnl();
   double dayPnl   = eq - g_dayStartBalance;
   double ddPct    = (g_highWaterMark > 0.0 ? (g_highWaterMark - eq) / g_highWaterMark * 100.0 : 0.0);

   double bid = 0.0, ask = 0.0;
   GetBidAsk(bid, ask);

   SidePos buy, sell;
   bool have_buy  = GetManagedSide(POSITION_TYPE_BUY, buy);
   bool have_sell = GetManagedSide(POSITION_TYPE_SELL, sell);

   color stateClr = clrCyan;
   if(g_state == S_ACTIVE)  stateClr = clrLightGreen;
   if(g_state == S_CLOSING) stateClr = clrOrange;
   if(g_paused)             stateClr = clrRed;

   PanelRow(0,  "=== Momentum Hedge EA LiveSafe A ===", clrCyan);
   PanelRow(1,  "State=" + IntegerToString((int)g_state) + " | Paused=" + YesNo(g_paused), stateClr);
   PanelRow(2,  "BUY count=" + IntegerToString(CountManaged(POSITION_TYPE_BUY)) +
                " | SELL count=" + IntegerToString(CountManaged(POSITION_TYPE_SELL)), clrWhite);
   PanelRow(3,  "BUY pnl=" + DoubleToString(have_buy ? buy.pnl : 0.0, 2) +
                " | SELL pnl=" + DoubleToString(have_sell ? sell.pnl : 0.0, 2), clrWhite);
   PanelRow(4,  "Float=" + DoubleToString(floatPnl, 2) +
                " | Peak=" + DoubleToString(g_floatPeak, 2), floatPnl >= 0 ? clrLime : clrTomato);
   PanelRow(5,  "Balance=" + DoubleToString(bal, 2) +
                " | Equity=" + DoubleToString(eq, 2), eq >= bal ? clrLime : clrOrange);
   PanelRow(6,  "DayPnL=" + DoubleToString(dayPnl, 2) +
                " | DailyLimit=-" + DoubleToString(DailyLossLimitUSD, 2), clrDimGray);
   PanelRow(7,  "HWM=" + DoubleToString(g_highWaterMark, 2) +
                " | DD=" + DoubleToString(ddPct, 2) + "%", ddPct < MaxDDFromPeakPct ? clrWhite : clrRed);
   PanelRow(8,  "Bid=" + DoubleToString(bid, _Digits) +
                " | Ask=" + DoubleToString(ask, _Digits), clrDimGray);
   PanelRow(9,  "CloseTracker=" + YesNo(g_closeTracker.initialized) +
                " | HedgeTrigger=" + YesNo(g_hedgeTracker.active), clrDimGray);
   PanelRow(10, "PendingBuy=" + YesNo(g_pendingBuy.active) +
                " | PendingSell=" + YesNo(g_pendingSell.active), clrDimGray);
   PanelRow(11, "MainReset=" + YesNo(InpEnableCycleReset) +
                " | ResetPips=" + DoubleToString(InpCycleResetPips, 1), clrDimGray);
   PanelRow(12, "FloatLock=" + YesNo(UseFloatLock) +
                " | Min=" + DoubleToString(FloatLockMinUSD, 2) +
                " | Drop=" + DoubleToString(FloatLockDropUSD, 2), clrDimGray);
   PanelRow(13, "PauseReason=" + (g_paused ? g_pauseReason : "-"), g_paused ? clrRed : clrDimGray);
   PanelRow(14, ">> " + g_status, g_paused ? clrRed : clrDimGray);
}

//==================================================================//
// INIT / DEINIT
//==================================================================//
int OnInit()
{
   ResetRuntimeState();

   g_trade.SetDeviationInPoints(InpDeviationPoints);
   g_trade.SetAsyncMode(false);
   g_trade.SetTypeFillingBySymbol(TradeSymbol());

   g_initialBalance  = AccountInfoDouble(ACCOUNT_BALANCE);
   g_dayStartBalance = g_initialBalance;
   g_highWaterMark   = AccountInfoDouble(ACCOUNT_EQUITY);
   g_lastDayReset    = iTime(TradeSymbol(), PERIOD_D1, 0);

   if(ShowPanel)
      BuildPanel();

   if(InpOnlyM1 && Period() != PERIOD_M1)
      Print("Warning: EA is designed for M1.");

   Print("Init OK | Symbol=", TradeSymbol(),
         " | Digits=", DigitsForSymbol(),
         " | PipSize=", DoubleToString(EffectivePipSize(), DigitsForSymbol()));

   g_state = S_IDLE;
   return INIT_SUCCEEDED;
}

void OnDeinit(const int reason)
{
   for(int i = ObjectsTotal(0, 0, -1) - 1; i >= 0; i--)
   {
      string nm = ObjectName(0, i, 0, -1);
      if(StringFind(nm, PN) == 0)
         ObjectDelete(0, nm);
   }
}

//==================================================================//
// MAIN TICK
//==================================================================//
void OnTick()
{
   if(InpOnlyM1 && Period() != PERIOD_M1)
      return;

   CheckDayReset();

   if(SpreadTooWide())
   {
      if(ShowPanel) UpdatePanel();
      return;
   }

   // paused mode
   if(g_paused)
   {
      if(g_state == S_CLOSING)
      {
         CloseAllOurs();

         if(!HasAnyPosition())
         {
            g_state = S_IDLE;
            g_status = "Paused: all closed";
         }

         if(ShowPanel) UpdatePanel();
         return;
      }

      if(MonitorDuringPause && HasAnyPosition())
      {
         double fl = TotalAllFloatingPnl();

         if(fl > g_floatPeak)
            g_floatPeak = fl;

         if(fl >= PauseFloatTargetUSD)
         {
            g_status = "Paused profit target reached";
            g_state = S_CLOSING;
         }
         else if(UseFloatLock &&
                 g_floatPeak >= FloatLockMinUSD &&
                 fl <= g_floatPeak - FloatLockDropUSD)
         {
            g_status = "Paused float lock hit";
            g_state = S_CLOSING;
         }
      }

      if(ShowPanel) UpdatePanel();
      return;
   }

   // protections
   double eq = AccountInfoDouble(ACCOUNT_EQUITY);
   if(eq > g_highWaterMark)
      g_highWaterMark = eq;

   if(UseHWM && g_highWaterMark > 0.0)
   {
      double ddPct = (g_highWaterMark - eq) / g_highWaterMark * 100.0;
      if(ddPct >= MaxDDFromPeakPct && HasAnyPosition())
      {
         g_paused = true;
         g_pauseReason = "HWM DD hit";
         g_state = S_CLOSING;
         if(ShowPanel) UpdatePanel();
         return;
      }
   }

   if(UseDailyLimit)
   {
      double dayPnl = eq - g_dayStartBalance;
      if(dayPnl <= -DailyLossLimitUSD && HasAnyPosition())
      {
         g_paused = true;
         g_pauseReason = "Daily loss hit";
         g_state = S_CLOSING;
         if(ShowPanel) UpdatePanel();
         return;
      }
   }

   if(UseEmergencyFloatStop)
   {
      double fl = TotalAllFloatingPnl();
      if(fl <= -EmergencyFloatLossUSD && HasAnyPosition())
      {
         g_paused = true;
         g_pauseReason = "Emergency float stop";
         g_state = S_CLOSING;
         if(ShowPanel) UpdatePanel();
         return;
      }
   }

   // float lock
   double totalPnl = TotalAllFloatingPnl();
   if(totalPnl > g_floatPeak)
      g_floatPeak = totalPnl;

   if(UseFloatLock &&
      g_floatPeak >= FloatLockMinUSD &&
      totalPnl <= g_floatPeak - FloatLockDropUSD &&
      HasAnyPosition())
   {
      g_status = "Float lock hit";
      g_state = S_CLOSING;
   }

   // first run sync
   if(g_firstRun)
   {
      g_firstRun = false;
      if(HasAnyPosition())
      {
         if(CountManaged(POSITION_TYPE_BUY) > 0)
            g_mainBuyTicket = FindNewestManagedTicket(POSITION_TYPE_BUY);
         if(CountManaged(POSITION_TYPE_SELL) > 0)
            g_mainSellTicket = FindNewestManagedTicket(POSITION_TYPE_SELL);
      }
   }

   if(g_state == S_IDLE)
   {
      MaintainInventory();

      if(HasAnyPosition())
      {
         double bid = 0.0, ask = 0.0;
         if(GetBidAsk(bid, ask))
            ResetCloseTracker(bid, ask);

         g_state = S_ACTIVE;
         g_status = "Initial inventory ready";
      }

      if(ShowPanel) UpdatePanel();
      return;
   }

   if(g_state == S_CLOSING)
   {
      CloseAllOurs();

      if(!HasAnyPosition())
      {
         ResetRuntimeState();
         g_state = S_IDLE;
         g_status = "Closed all";
      }

      if(ShowPanel) UpdatePanel();
      return;
   }

   // ACTIVE
   CheckHedgeTrigger();

   bool changed = CheckCycleReset();

   if(!changed)
   {
      CloseSignal sig = EvaluateCloseSignal();

      if(sig == CLOSE_SIGNAL_BUY)
      {
         if(CloseManagedSide(POSITION_TYPE_BUY))
         {
            g_status = "Momentum close BUY";
            changed = true;
         }
      }
      else if(sig == CLOSE_SIGNAL_SELL)
      {
         if(CloseManagedSide(POSITION_TYPE_SELL))
         {
            g_status = "Momentum close SELL";
            changed = true;
         }
      }
   }

   MaintainInventory();

   if(changed)
      MaintainInventory();

   if(!HasAnyPosition())
      g_state = S_IDLE;
   else
      g_state = S_ACTIVE;

   if(g_status == "Starting..." || g_status == "")
      g_status = "ACTIVE";

   if(ShowPanel) UpdatePanel();
}
//+------------------------------------------------------------------+