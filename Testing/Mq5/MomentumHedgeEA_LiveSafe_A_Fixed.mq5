//+------------------------------------------------------------------+
//|              MomentumHedgeEA_LiveSafe_A_Fixed.mq5                |
//|                                                                  |
//|  Core logic:                                                     |
//|   - Maintains 1 BUY + 1 SELL                                     |
//|   - Closes opposite side after clean 40-pip move                 |
//|   - Reopens missing hedge after 10-pip reversal                  |
//|   - Optional main cycle reset after large favorable move         |
//|   - Includes pause logic, HWM/DD, daily limit, float lock, panel |
//|                                                                  |
//|  FIXES:                                                          |
//|   FIX1: CloseManagedTicket не активира pending reentry           |
//|          — hedge trigger е достатъчен, pending и hedge конфликтуват|
//|   FIX2: CheckHedgeTrigger не проверява ReentryReady             |
//|          — time-based логиката блокира price-based тригера        |
//|   FIX3: CheckCycleReset преотваря позицията след reset           |
//|          и не използва CloseManagedTicket (нямаме нужда от hedge) |
//|   FIX4: g_cycleResetInProgress блокира MaintainInventory        |
//|          по време на cycle reset за да няма 3+ позиции           |
//|   FIX5: MaintainInventory — при едната страна липсва и           |
//|          тригерът не е активен → само pending reentry            |
//|   FIX6: S_CLOSING изчиства hedge и pending стейт при reset       |
//|   FIX7: CheckHedgeTrigger реактивира тригера при провал на open  |
//+------------------------------------------------------------------+
#property strict
#property version   "1.01"
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
enum EState      { S_IDLE = 0, S_ACTIVE = 1, S_CLOSING = 2 };
enum CloseSignal { CLOSE_SIGNAL_NONE = 0, CLOSE_SIGNAL_BUY = 1, CLOSE_SIGNAL_SELL = 2 };

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
   double up_base, up_peak, down_base, down_low;
};

struct HedgeTracker
{
   bool               active;
   ENUM_POSITION_TYPE closed_side, active_side;
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

// FIX4: Guard — блокира MaintainInventory по време на cycle reset
bool           g_cycleResetInProgress = false;

#define PN "MHEA_"
#define PANEL_ROWS 15

//==================================================================//
// HELPERS
//==================================================================//
string TradeSymbol()       { return _Symbol; }
string YesNo(const bool v) { return v ? "YES" : "NO"; }

long MagicForSide(const ENUM_POSITION_TYPE side)
   { return side == POSITION_TYPE_BUY ? InpMagicBuy : InpMagicSell; }

bool IsOurMagic(const long magic)
   { return magic == InpMagicBuy || magic == InpMagicSell; }

int DigitsForSymbol()
   { return (int)SymbolInfoInteger(TradeSymbol(), SYMBOL_DIGITS); }

double NormalizePrice(const double price)
   { return NormalizeDouble(price, DigitsForSymbol()); }

double EffectivePipSize()
{
   if(!InpCloseAutoPipSize) return InpClosePipSize;
   double point = 0.0;
   if(!SymbolInfoDouble(TradeSymbol(), SYMBOL_POINT, point) || point <= 0.0)
      return InpClosePipSize;
   int digits = DigitsForSymbol();
   if(digits == 2 || digits == 3 || digits == 5 || digits == 6) return point * 10.0;
   return point;
}

double PriceEpsilon()
{
   double point = 0.0;
   if(!SymbolInfoDouble(TradeSymbol(), SYMBOL_POINT, point) || point <= 0.0) point = 0.00001;
   return point * 0.5;
}

double StepFloor(const double price, const double step)
{
   if(step <= 0.0) return NormalizePrice(price);
   return NormalizePrice(MathFloor((price + 1e-12) / step) * step);
}

double StepCeil(const double price, const double step)
{
   if(step <= 0.0) return NormalizePrice(price);
   return NormalizePrice(MathCeil((price - 1e-12) / step) * step);
}

bool GetBidAsk(double &bid, double &ask)
{
   MqlTick tick;
   if(!SymbolInfoTick(TradeSymbol(), tick)) return false;
   bid = NormalizePrice(tick.bid);
   ask = NormalizePrice(tick.ask);
   return bid > 0.0 && ask > 0.0;
}

bool SpreadTooWide()
{
   if((double)SymbolInfoInteger(TradeSymbol(), SYMBOL_SPREAD) > InpMaxSpreadPoints)
   { g_status = "Spread too wide"; return true; }
   return false;
}

//==================================================================//
// RESET
//==================================================================//
void ResetPendingReentries()
{
   g_pendingBuy.active  = false; g_pendingBuy.start_time  = 0; g_pendingBuy.close_price  = 0.0;
   g_pendingSell.active = false; g_pendingSell.start_time = 0; g_pendingSell.close_price = 0.0;
}

void ResetCloseTracker(const double bid, const double ask)
{
   double pip_price  = EffectivePipSize();
   double step_price = InpCloseLevelStepPips * pip_price;
   if(step_price <= 0.0) step_price = pip_price;

   g_closeTracker.initialized = true;
   g_closeTracker.up_base     = StepFloor(ask, step_price);
   g_closeTracker.up_peak     = ask;
   g_closeTracker.down_base   = StepCeil(bid, step_price);
   g_closeTracker.down_low    = bid;

   if(InpVerboseCloseDebug) Print("Close tracker reset");
}

void ResetRuntimeState()
{
   ResetPendingReentries();
   g_mainBuyTicket  = 0; g_mainSellTicket = 0;
   g_closeTracker.initialized = false;
   g_closeTracker.up_base = g_closeTracker.up_peak = 0.0;
   g_closeTracker.down_base = g_closeTracker.down_low = 0.0;
   g_hedgeTracker.active = false;
   g_hedgeTracker.closed_side = POSITION_TYPE_BUY;
   g_hedgeTracker.active_side = POSITION_TYPE_SELL;
   g_hedgeTracker.trigger_price = 0.0;
   g_floatPeak = 0.0;
   g_cycleResetInProgress = false;  // FIX4
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
      if(ticket == 0 || !PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL) != TradeSymbol()) continue;
      if((long)PositionGetInteger(POSITION_MAGIC) != MagicForSide(side)) continue;
      if((ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE) != side) continue;
      count++;
   }
   return count;
}

bool GetManagedSide(const ENUM_POSITION_TYPE side, SidePos &p)
{
   p.ok = false; p.ticket = 0; p.open_time = 0; p.open_price = 0.0;
   p.pnl = 0.0; p.volume = 0.0; p.type = side;

   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0 || !PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL) != TradeSymbol()) continue;
      if((long)PositionGetInteger(POSITION_MAGIC) != MagicForSide(side)) continue;
      if((ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE) != side) continue;

      datetime tm = (datetime)PositionGetInteger(POSITION_TIME);
      if(!p.ok || tm < p.open_time)
      {
         p.ok = true; p.ticket = ticket; p.open_time = tm;
         p.open_price = PositionGetDouble(POSITION_PRICE_OPEN);
         p.pnl        = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
         p.volume     = PositionGetDouble(POSITION_VOLUME);
         p.type       = side;
      }
   }
   return p.ok;
}

bool GetManagedTicketInfo(const ulong ticket, SidePos &p)
{
   p.ok = false; p.ticket = 0; p.open_time = 0; p.open_price = 0.0;
   p.pnl = 0.0; p.volume = 0.0; p.type = POSITION_TYPE_BUY;

   if(ticket == 0 || !PositionSelectByTicket(ticket)) return false;
   if(PositionGetString(POSITION_SYMBOL) != TradeSymbol()) return false;
   ENUM_POSITION_TYPE side = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
   if((long)PositionGetInteger(POSITION_MAGIC) != MagicForSide(side)) return false;

   p.ok = true; p.ticket = ticket;
   p.open_time  = (datetime)PositionGetInteger(POSITION_TIME);
   p.open_price = PositionGetDouble(POSITION_PRICE_OPEN);
   p.pnl        = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
   p.volume     = PositionGetDouble(POSITION_VOLUME);
   p.type       = side;
   return true;
}

ulong FindNewestManagedTicket(const ENUM_POSITION_TYPE side)
{
   ulong newest_ticket = 0; datetime newest_time = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0 || !PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL) != TradeSymbol()) continue;
      if((long)PositionGetInteger(POSITION_MAGIC) != MagicForSide(side)) continue;
      if((ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE) != side) continue;
      datetime tm = (datetime)PositionGetInteger(POSITION_TIME);
      if(newest_ticket == 0 || tm > newest_time) { newest_ticket = ticket; newest_time = tm; }
   }
   return newest_ticket;
}

double TotalAllFloatingPnl()
{
   double total = 0.0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0 || !PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL) != TradeSymbol()) continue;
      if(!IsOurMagic((long)PositionGetInteger(POSITION_MAGIC))) continue;
      total += PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
   }
   return total;
}

bool HasAnyPosition()
   { return CountManaged(POSITION_TYPE_BUY) > 0 || CountManaged(POSITION_TYPE_SELL) > 0; }

//==================================================================//
// EXECUTION
//==================================================================//
bool OpenSide(const ENUM_POSITION_TYPE side, const string comment = "")
{
   g_trade.SetExpertMagicNumber(MagicForSide(side));
   g_trade.SetDeviationInPoints(InpDeviationPoints);
   g_trade.SetTypeFillingBySymbol(TradeSymbol());

   bool ok = side == POSITION_TYPE_BUY
      ? g_trade.Buy(InpLots,  TradeSymbol(), 0.0, 0.0, 0.0, comment)
      : g_trade.Sell(InpLots, TradeSymbol(), 0.0, 0.0, 0.0, comment);

   if(!ok && InpVerbose)
      Print("Open failed: ", EnumToString(side), " | ", g_trade.ResultRetcodeDescription());

   if(ok)
   {
      if(side == POSITION_TYPE_BUY)  g_pendingBuy.active  = false;
      else                           g_pendingSell.active = false;
   }
   return ok;
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

//------------------------------------------------------------------
// FIX1: CloseManagedTicket не активира pending reentry.
// Само RegisterHedgeTrigger се вика — price-based тригер е достатъчен.
// Pending reentry (time-based) и hedge trigger (price-based) конфликтуват:
// CheckHedgeTrigger() проверяваше ReentryReady() → блокираше 8+ сек.
//------------------------------------------------------------------
bool CloseManagedTicket(const ulong ticket)
{
   if(ticket == 0 || !PositionSelectByTicket(ticket)) return false;
   if(PositionGetString(POSITION_SYMBOL) != TradeSymbol()) return false;
   ENUM_POSITION_TYPE side = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
   if((long)PositionGetInteger(POSITION_MAGIC) != MagicForSide(side)) return false;

   if(!CloseTicket(ticket)) return false;

   if(side == POSITION_TYPE_BUY)  g_mainBuyTicket  = 0;
   if(side == POSITION_TYPE_SELL) g_mainSellTicket = 0;

   // FIX1: Само hedge trigger — без pending reentry
   RegisterHedgeTrigger(side);

   if(InpVerbose) Print("Closed managed side: ", EnumToString(side));
   return true;
}

// За cycle reset — без hedge trigger (имаме директно преотваряне)
bool CloseTicketNoTrigger(const ulong ticket)
{
   if(ticket == 0 || !PositionSelectByTicket(ticket)) return false;
   if(PositionGetString(POSITION_SYMBOL) != TradeSymbol()) return false;
   ENUM_POSITION_TYPE side = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
   if((long)PositionGetInteger(POSITION_MAGIC) != MagicForSide(side)) return false;

   if(!CloseTicket(ticket)) return false;

   if(side == POSITION_TYPE_BUY)  g_mainBuyTicket  = 0;
   if(side == POSITION_TYPE_SELL) g_mainSellTicket = 0;
   return true;
}

bool CloseManagedSide(const ENUM_POSITION_TYPE side)
{
   SidePos pos;
   return GetManagedSide(side, pos) ? CloseManagedTicket(pos.ticket) : false;
}

bool CloseAllOurs()
{
   ulong tickets[]; ArrayResize(tickets, 0);
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0 || !PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL) != TradeSymbol()) continue;
      if(!IsOurMagic((long)PositionGetInteger(POSITION_MAGIC))) continue;
      int sz = ArraySize(tickets); ArrayResize(tickets, sz + 1); tickets[sz] = ticket;
   }
   bool all_ok = true;
   for(int j = 0; j < ArraySize(tickets); j++)
      if(!CloseTicket(tickets[j])) all_ok = false;
   return all_ok;
}

//==================================================================//
// REENTRY / HEDGE
//==================================================================//
void RegisterHedgeTrigger(const ENUM_POSITION_TYPE closed_side)
{
   ENUM_POSITION_TYPE surviving =
      closed_side == POSITION_TYPE_BUY ? POSITION_TYPE_SELL : POSITION_TYPE_BUY;

   double retrace_price = InpCloseRetracePips * EffectivePipSize();
   double bid = 0.0, ask = 0.0;
   if(!GetBidAsk(bid, ask)) return;

   g_hedgeTracker.active      = true;
   g_hedgeTracker.closed_side = closed_side;
   g_hedgeTracker.active_side = surviving;
   g_hedgeTracker.trigger_price =
      surviving == POSITION_TYPE_BUY ? ask - retrace_price : bid + retrace_price;

   if(InpVerbose)
      Print("Hedge trigger armed | closed=", EnumToString(closed_side),
            " surviving=", EnumToString(surviving),
            " trigger=", DoubleToString(g_hedgeTracker.trigger_price, DigitsForSymbol()));
}

double CurrentReentryGap()
{
   double bid = 0.0, ask = 0.0;
   if(!GetBidAsk(bid, ask)) return InpReentryGapAbs;
   double gap = InpReentryGapAbs;
   double sg  = (ask - bid) * InpReentryGapSpreadMult;
   return sg > gap ? sg : gap;
}

bool ReentryReady(const ENUM_POSITION_TYPE side)
{
   double bid = 0.0, ask = 0.0;
   if(!GetBidAsk(bid, ask)) return false;
   double gap = CurrentReentryGap();

   if(side == POSITION_TYPE_BUY)
   {
      if(!g_pendingBuy.active || g_pendingBuy.start_time <= 0) return true;
      int el = (int)(TimeCurrent() - g_pendingBuy.start_time);
      if(el < InpReentryMinDelaySec) return false;
      if(g_pendingBuy.close_price - ask >= gap) return true;
      return el >= InpReentryForceSec;
   }
   if(!g_pendingSell.active || g_pendingSell.start_time <= 0) return true;
   int el = (int)(TimeCurrent() - g_pendingSell.start_time);
   if(el < InpReentryMinDelaySec) return false;
   if(bid - g_pendingSell.close_price >= gap) return true;
   return el >= InpReentryForceSec;
}

//------------------------------------------------------------------
// FIX2: Без ReentryReady — отваряме на точната цена, без time-based блок.
// FIX7: При провал на OpenSide реактивираме тригера за следващия тик.
//------------------------------------------------------------------
void CheckHedgeTrigger()
{
   if(!g_hedgeTracker.active) return;

   if(CountManaged(g_hedgeTracker.closed_side) > 0)
   { g_hedgeTracker.active = false; return; }

   double bid = 0.0, ask = 0.0;
   if(!GetBidAsk(bid, ask)) return;

   double retrace_price = InpCloseRetracePips * EffectivePipSize();
   bool triggered = false;

   if(g_hedgeTracker.active_side == POSITION_TYPE_BUY)
   {
      double nt = ask - retrace_price;
      if(nt > g_hedgeTracker.trigger_price) g_hedgeTracker.trigger_price = nt;
      if(bid <= g_hedgeTracker.trigger_price) triggered = true;
   }
   else
   {
      double nt = bid + retrace_price;
      if(nt < g_hedgeTracker.trigger_price) g_hedgeTracker.trigger_price = nt;
      if(ask >= g_hedgeTracker.trigger_price) triggered = true;
   }

   if(!triggered) return;

   // FIX2 + FIX7: Изключи тригера ПРЕДИ open; реактивирай при провал
   ENUM_POSITION_TYPE side_to_open = g_hedgeTracker.closed_side;
   g_hedgeTracker.active = false;

   if(InpVerbose)
      Print("Hedge trigger fired | opening=", EnumToString(side_to_open),
            " trigger=", DoubleToString(g_hedgeTracker.trigger_price, DigitsForSymbol()));

   if(OpenSide(side_to_open, "hedge_reentry"))
   {
      if(side_to_open == POSITION_TYPE_BUY  && g_mainBuyTicket  == 0)
         g_mainBuyTicket  = FindNewestManagedTicket(POSITION_TYPE_BUY);
      if(side_to_open == POSITION_TYPE_SELL && g_mainSellTicket == 0)
         g_mainSellTicket = FindNewestManagedTicket(POSITION_TYPE_SELL);
   }
   else
   {
      g_hedgeTracker.active = true;  // FIX7: реактивирай при провал
   }
}

//==================================================================//
// MAIN LOGIC
//==================================================================//
CloseSignal EvaluateCloseSignal()
{
   double pip_price     = EffectivePipSize();
   double target_price  = InpCloseTargetPips    * pip_price;
   double retrace_price = InpCloseRetracePips   * pip_price;
   double step_price    = InpCloseLevelStepPips * pip_price;
   double eps           = PriceEpsilon();

   if(target_price <= 0.0 || retrace_price <= 0.0 || step_price <= 0.0)
      return CLOSE_SIGNAL_NONE;

   double bid = 0.0, ask = 0.0;
   if(!GetBidAsk(bid, ask)) return CLOSE_SIGNAL_NONE;

   if(!g_closeTracker.initialized) { ResetCloseTracker(bid, ask); return CLOSE_SIGNAL_NONE; }

   if(ask > g_closeTracker.up_peak)  g_closeTracker.up_peak  = ask;
   if(bid < g_closeTracker.down_low) g_closeTracker.down_low = bid;

   // UP move => close SELL
   double up_run      = g_closeTracker.up_peak - g_closeTracker.up_base;
   double up_pullback = g_closeTracker.up_peak - ask;

   if(up_run + eps >= target_price)
   {
      if(InpVerbose || InpVerboseCloseDebug)
         Print("UP clean move => close SELL | move=", DoubleToString(up_run/pip_price,1), " pips");
      ResetCloseTracker(bid, ask); return CLOSE_SIGNAL_SELL;
   }

   if(up_pullback + eps >= retrace_price && up_run + eps < target_price)
   {
      double steps = MathFloor((up_run + eps) / step_price);
      if(steps >= 1.0)
      {
         double new_base = NormalizePrice(g_closeTracker.up_base + steps * step_price);
         if(new_base > g_closeTracker.up_base + eps)
         { g_closeTracker.up_base = new_base; g_closeTracker.up_peak = MathMax(ask, new_base); }
      }
   }

   if(ask < g_closeTracker.up_base - step_price - eps)
   { g_closeTracker.up_base = StepFloor(ask, step_price); g_closeTracker.up_peak = ask; }

   // DOWN move => close BUY
   double down_run      = g_closeTracker.down_base - g_closeTracker.down_low;
   double down_pullback = bid - g_closeTracker.down_low;

   if(down_run + eps >= target_price)
   {
      if(InpVerbose || InpVerboseCloseDebug)
         Print("DOWN clean move => close BUY | move=", DoubleToString(down_run/pip_price,1), " pips");
      ResetCloseTracker(bid, ask); return CLOSE_SIGNAL_BUY;
   }

   if(down_pullback + eps >= retrace_price && down_run + eps < target_price)
   {
      double steps = MathFloor((down_run + eps) / step_price);
      if(steps >= 1.0)
      {
         double new_base = NormalizePrice(g_closeTracker.down_base - steps * step_price);
         if(new_base < g_closeTracker.down_base - eps)
         { g_closeTracker.down_base = new_base; g_closeTracker.down_low = MathMin(bid, new_base); }
      }
   }

   if(bid > g_closeTracker.down_base + step_price + eps)
   { g_closeTracker.down_base = StepCeil(bid, step_price); g_closeTracker.down_low = bid; }

   return CLOSE_SIGNAL_NONE;
}

void SyncMainTickets()
{
   if(CountManaged(POSITION_TYPE_BUY)  == 0) g_mainBuyTicket  = 0;
   else if(g_mainBuyTicket  == 0) g_mainBuyTicket  = FindNewestManagedTicket(POSITION_TYPE_BUY);
   if(CountManaged(POSITION_TYPE_SELL) == 0) g_mainSellTicket = 0;
   else if(g_mainSellTicket == 0) g_mainSellTicket = FindNewestManagedTicket(POSITION_TYPE_SELL);
}

//------------------------------------------------------------------
// FIX3: CheckCycleReset преотваря позицията веднага след reset.
// FIX4: g_cycleResetInProgress блокира MaintainInventory.
// Оригинален проблем: само затваряше чрез CloseManagedTicket (→ hedge),
// без да преотвори; MaintainInventory после откриваше без reset логика.
//------------------------------------------------------------------
bool CheckCycleReset()
{
   if(!InpEnableCycleReset) return false;

   double pip_price   = EffectivePipSize();
   double reset_price = InpCycleResetPips * pip_price;
   double eps         = PriceEpsilon();
   if(reset_price <= 0.0) return false;

   double bid = 0.0, ask = 0.0;
   if(!GetBidAsk(bid, ask)) return false;

   SidePos mb, ms;

   if(GetManagedTicketInfo(g_mainBuyTicket, mb))
   {
      if(mb.open_price > 0.0 && (bid - mb.open_price) + eps >= reset_price)
      {
         if(InpVerbose) Print("Cycle reset MAIN BUY | move=",
            DoubleToString((bid - mb.open_price)/pip_price, 1), " pips");
         ulong old = g_mainBuyTicket; g_mainBuyTicket = 0;
         g_cycleResetInProgress = true;            // FIX4
         if(!CloseTicketNoTrigger(old)) { g_cycleResetInProgress = false; return false; }
         if(OpenSide(POSITION_TYPE_BUY, "reset_buy"))  // FIX3
            g_mainBuyTicket = FindNewestManagedTicket(POSITION_TYPE_BUY);
         g_cycleResetInProgress = false;            // FIX4
         return true;
      }
   }
   else g_mainBuyTicket = 0;

   if(GetManagedTicketInfo(g_mainSellTicket, ms))
   {
      if(ms.open_price > 0.0 && (ms.open_price - ask) + eps >= reset_price)
      {
         if(InpVerbose) Print("Cycle reset MAIN SELL | move=",
            DoubleToString((ms.open_price - ask)/pip_price, 1), " pips");
         ulong old = g_mainSellTicket; g_mainSellTicket = 0;
         g_cycleResetInProgress = true;             // FIX4
         if(!CloseTicketNoTrigger(old)) { g_cycleResetInProgress = false; return false; }
         if(OpenSide(POSITION_TYPE_SELL, "reset_sell")) // FIX3
            g_mainSellTicket = FindNewestManagedTicket(POSITION_TYPE_SELL);
         g_cycleResetInProgress = false;             // FIX4
         return true;
      }
   }
   else g_mainSellTicket = 0;

   return false;
}

//------------------------------------------------------------------
// FIX5: При едната страна липсва и тригерът не е активен → само
// pending reentry. Предотвратява 3+ позиции.
//------------------------------------------------------------------
void MaintainInventory()
{
   if(g_cycleResetInProgress) return;  // FIX4

   SyncMainTickets();
   int buy_count  = CountManaged(POSITION_TYPE_BUY);
   int sell_count = CountManaged(POSITION_TYPE_SELL);

   if(buy_count == 0 && sell_count == 0)
   {
      g_mainBuyTicket = g_mainSellTicket = 0;
      g_hedgeTracker.active = false;
      bool okB = OpenSide(POSITION_TYPE_BUY,  "initial_buy");
      bool okS = OpenSide(POSITION_TYPE_SELL, "initial_sell");
      if(okB) g_mainBuyTicket  = FindNewestManagedTicket(POSITION_TYPE_BUY);
      if(okS) g_mainSellTicket = FindNewestManagedTicket(POSITION_TYPE_SELL);
      double bid=0.0, ask=0.0;
      if(GetBidAsk(bid, ask)) ResetCloseTracker(bid, ask);
      return;
   }

   if(buy_count  == 0) g_mainBuyTicket  = 0;
   if(sell_count == 0) g_mainSellTicket = 0;

   if(buy_count  == 0 && g_hedgeTracker.active && g_hedgeTracker.closed_side == POSITION_TYPE_BUY)  return;
   if(sell_count == 0 && g_hedgeTracker.active && g_hedgeTracker.closed_side == POSITION_TYPE_SELL) return;

   // FIX5: само pending reentry при липсваща страна без активен тригер
   if(buy_count == 0 && sell_count > 0)
   {
      if(ReentryReady(POSITION_TYPE_BUY) && OpenSide(POSITION_TYPE_BUY, "rebuild_buy"))
         g_mainBuyTicket = FindNewestManagedTicket(POSITION_TYPE_BUY);
      return;
   }
   if(sell_count == 0 && buy_count > 0)
   {
      if(ReentryReady(POSITION_TYPE_SELL) && OpenSide(POSITION_TYPE_SELL, "rebuild_sell"))
         g_mainSellTicket = FindNewestManagedTicket(POSITION_TYPE_SELL);
      return;
   }
}

//==================================================================//
// PROTECTION
//==================================================================//
void CheckDayReset()
{
   if(!UseDailyLimit && !UseHWM) return;
   datetime todayBar = iTime(TradeSymbol(), PERIOD_D1, 0);
   if(todayBar != g_lastDayReset)
   {
      g_lastDayReset    = todayBar;
      g_dayStartBalance = AccountInfoDouble(ACCOUNT_BALANCE);
      if(g_paused && StringFind(g_pauseReason, "Daily") >= 0)
      { g_paused = false; g_pauseReason = ""; if(InpVerbose) Print("Daily pause lifted."); }
   }
}

//==================================================================//
// PANEL
//==================================================================//
void BuildPanel()
{
   for(int i = ObjectsTotal(0,0,-1)-1; i >= 0; i--)
   { string nm = ObjectName(0,i,0,-1); if(StringFind(nm,PN)==0) ObjectDelete(0,nm); }

   ObjectCreate(0, PN+"bg", OBJ_RECTANGLE_LABEL, 0,0,0);
   ObjectSetInteger(0,PN+"bg",OBJPROP_CORNER,CORNER_LEFT_UPPER);
   ObjectSetInteger(0,PN+"bg",OBJPROP_XDISTANCE,PanelX-5);
   ObjectSetInteger(0,PN+"bg",OBJPROP_YDISTANCE,PanelY-5);
   ObjectSetInteger(0,PN+"bg",OBJPROP_XSIZE,470);
   ObjectSetInteger(0,PN+"bg",OBJPROP_YSIZE,PANEL_ROWS*21+10);
   ObjectSetInteger(0,PN+"bg",OBJPROP_BGCOLOR,C'8,12,30');
   ObjectSetInteger(0,PN+"bg",OBJPROP_BORDER_TYPE,BORDER_FLAT);
   ObjectSetInteger(0,PN+"bg",OBJPROP_COLOR,C'0,160,100');
   ObjectSetInteger(0,PN+"bg",OBJPROP_BACK,true);

   for(int i = 0; i < PANEL_ROWS; i++)
   {
      string nm = PN+"r"+IntegerToString(i);
      ObjectCreate(0,nm,OBJ_LABEL,0,0,0);
      ObjectSetInteger(0,nm,OBJPROP_CORNER,CORNER_LEFT_UPPER);
      ObjectSetInteger(0,nm,OBJPROP_XDISTANCE,PanelX+4);
      ObjectSetInteger(0,nm,OBJPROP_YDISTANCE,PanelY+i*21);
      ObjectSetString(0,nm,OBJPROP_FONT,"Consolas");
      ObjectSetInteger(0,nm,OBJPROP_FONTSIZE,8);
      ObjectSetInteger(0,nm,OBJPROP_COLOR,clrWhite);
      ObjectSetString(0,nm,OBJPROP_TEXT,"");
   }
}

void PanelRow(const int row, const string txt, const color clr)
{
   if(row < 0 || row >= PANEL_ROWS) return;
   string key = PN+"r"+IntegerToString(row);
   if(ObjectFind(0,key) < 0) return;
   ObjectSetString(0,key,OBJPROP_TEXT,txt);
   ObjectSetInteger(0,key,OBJPROP_COLOR,clr);
}

void UpdatePanel()
{
   double bal = AccountInfoDouble(ACCOUNT_BALANCE);
   double eq  = AccountInfoDouble(ACCOUNT_EQUITY);
   double fp  = TotalAllFloatingPnl();
   double dp  = eq - g_dayStartBalance;
   double dd  = g_highWaterMark > 0.0 ? (g_highWaterMark-eq)/g_highWaterMark*100.0 : 0.0;
   double bid=0.0, ask=0.0; GetBidAsk(bid, ask);
   SidePos buy, sell;
   bool hb = GetManagedSide(POSITION_TYPE_BUY, buy);
   bool hs = GetManagedSide(POSITION_TYPE_SELL, sell);

   color sc = clrCyan;
   if(g_state==S_ACTIVE)  sc = clrLightGreen;
   if(g_state==S_CLOSING) sc = clrOrange;
   if(g_paused)           sc = clrRed;

   PanelRow(0,  "=== Momentum Hedge EA LiveSafe A Fixed ===", clrCyan);
   PanelRow(1,  StringFormat("State=%d Paused=%s ResetProg=%s",
                             (int)g_state, YesNo(g_paused), YesNo(g_cycleResetInProgress)), sc);
   PanelRow(2,  StringFormat("BUY cnt=%d main=%I64u | SELL cnt=%d main=%I64u",
                             CountManaged(POSITION_TYPE_BUY), g_mainBuyTicket,
                             CountManaged(POSITION_TYPE_SELL), g_mainSellTicket), clrWhite);
   PanelRow(3,  StringFormat("BUY pnl=%+.2f | SELL pnl=%+.2f",
                             hb ? buy.pnl : 0.0, hs ? sell.pnl : 0.0), clrWhite);
   PanelRow(4,  StringFormat("Float=%+.2f | Peak=%+.2f", fp, g_floatPeak),
                             fp >= 0 ? clrLime : clrTomato);
   PanelRow(5,  StringFormat("Balance=%.2f | Equity=%.2f", bal, eq),
                             eq >= bal ? clrLime : clrOrange);
   PanelRow(6,  StringFormat("DayPnL=%+.2f | DailyLimit=-%.2f", dp, DailyLossLimitUSD),
                             dp >= 0 ? clrLime : clrOrange);
   PanelRow(7,  StringFormat("HWM=%.2f | DD=%.2f%% / Max=%.2f%%", g_highWaterMark, dd, MaxDDFromPeakPct),
                             dd < MaxDDFromPeakPct ? clrWhite : clrRed);
   PanelRow(8,  StringFormat("Bid=%.*f | Ask=%.*f", _Digits, bid, _Digits, ask), clrDimGray);
   PanelRow(9,  StringFormat("CloseTrack=%s | HedgeTrig=%s | %s → %.*f",
                             YesNo(g_closeTracker.initialized), YesNo(g_hedgeTracker.active),
                             EnumToString(g_hedgeTracker.closed_side),
                             _Digits, g_hedgeTracker.trigger_price),
                             g_hedgeTracker.active ? clrYellow : clrDimGray);
   PanelRow(10, StringFormat("PendingBuy=%s | PendingSell=%s",
                             YesNo(g_pendingBuy.active), YesNo(g_pendingSell.active)), clrDimGray);
   PanelRow(11, StringFormat("MainReset=%s | ResetPips=%.1f",
                             YesNo(InpEnableCycleReset), InpCycleResetPips), clrDimGray);
   PanelRow(12, StringFormat("FloatLock=%s | Min=%.2f | Drop=%.2f",
                             YesNo(UseFloatLock), FloatLockMinUSD, FloatLockDropUSD), clrDimGray);
   PanelRow(13, StringFormat("PauseReason=%s", g_paused ? g_pauseReason : "-"),
                             g_paused ? clrRed : clrDimGray);
   PanelRow(14, StringFormat(">> %.52s", g_status), g_paused ? clrRed : clrDimGray);
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

   g_initialBalance = g_dayStartBalance = AccountInfoDouble(ACCOUNT_BALANCE);
   g_highWaterMark  = AccountInfoDouble(ACCOUNT_EQUITY);
   g_lastDayReset   = iTime(TradeSymbol(), PERIOD_D1, 0);

   if(ShowPanel) BuildPanel();
   if(InpOnlyM1 && Period() != PERIOD_M1) Print("Warning: EA is designed for M1.");
   Print("Init OK | Symbol=", TradeSymbol(),
         " | Digits=", DigitsForSymbol(),
         " | PipSize=", DoubleToString(EffectivePipSize(), DigitsForSymbol()));
   g_state = S_IDLE;
   return INIT_SUCCEEDED;
}

void OnDeinit(const int reason)
{
   for(int i = ObjectsTotal(0,0,-1)-1; i >= 0; i--)
   { string nm = ObjectName(0,i,0,-1); if(StringFind(nm,PN)==0) ObjectDelete(0,nm); }
}

//==================================================================//
// MAIN TICK
//==================================================================//
void OnTick()
{
   if(InpOnlyM1 && Period() != PERIOD_M1) return;
   CheckDayReset();
   if(SpreadTooWide()) { if(ShowPanel) UpdatePanel(); return; }

   // Pause
   if(g_paused)
   {
      if(g_state == S_CLOSING)
      {
         CloseAllOurs();
         if(!HasAnyPosition())
         {
            // FIX6: Изчистваме hedge и pending при пълен reset
            g_hedgeTracker.active = false;
            ResetPendingReentries();
            g_state = S_IDLE; g_status = "Paused: all closed";
         }
         if(ShowPanel) UpdatePanel(); return;
      }
      if(MonitorDuringPause && HasAnyPosition())
      {
         double fl = TotalAllFloatingPnl();
         if(fl > g_floatPeak) g_floatPeak = fl;
         if(fl >= PauseFloatTargetUSD)
            { g_status = "Paused profit target reached"; g_state = S_CLOSING; }
         else if(UseFloatLock && g_floatPeak >= FloatLockMinUSD && fl <= g_floatPeak - FloatLockDropUSD)
            { g_status = "Paused float lock hit"; g_state = S_CLOSING; }
      }
      if(ShowPanel) UpdatePanel(); return;
   }

   // Protections
   double eq = AccountInfoDouble(ACCOUNT_EQUITY);
   if(eq > g_highWaterMark) g_highWaterMark = eq;

   if(UseHWM && g_highWaterMark > 0.0)
   {
      double dd = (g_highWaterMark - eq) / g_highWaterMark * 100.0;
      if(dd >= MaxDDFromPeakPct && HasAnyPosition())
      { g_paused = true; g_pauseReason = "HWM DD hit"; g_state = S_CLOSING; if(ShowPanel) UpdatePanel(); return; }
   }
   if(UseDailyLimit && (eq - g_dayStartBalance) <= -DailyLossLimitUSD && HasAnyPosition())
   { g_paused = true; g_pauseReason = "Daily loss hit"; g_state = S_CLOSING; if(ShowPanel) UpdatePanel(); return; }
   if(UseEmergencyFloatStop && TotalAllFloatingPnl() <= -EmergencyFloatLossUSD && HasAnyPosition())
   { g_paused = true; g_pauseReason = "Emergency float stop"; g_state = S_CLOSING; if(ShowPanel) UpdatePanel(); return; }

   double totalPnl = TotalAllFloatingPnl();
   if(totalPnl > g_floatPeak) g_floatPeak = totalPnl;
   if(UseFloatLock && g_floatPeak >= FloatLockMinUSD && totalPnl <= g_floatPeak - FloatLockDropUSD && HasAnyPosition())
   { g_status = "Float lock hit"; g_state = S_CLOSING; }

   // First run
   if(g_firstRun)
   {
      g_firstRun = false;
      if(CountManaged(POSITION_TYPE_BUY)  > 0) g_mainBuyTicket  = FindNewestManagedTicket(POSITION_TYPE_BUY);
      if(CountManaged(POSITION_TYPE_SELL) > 0) g_mainSellTicket = FindNewestManagedTicket(POSITION_TYPE_SELL);
   }

   // States
   if(g_state == S_IDLE)
   {
      MaintainInventory();
      if(HasAnyPosition())
      {
         double bid=0.0, ask=0.0;
         if(GetBidAsk(bid, ask)) ResetCloseTracker(bid, ask);
         g_state = S_ACTIVE; g_status = "Initial inventory ready";
      }
      if(ShowPanel) UpdatePanel(); return;
   }

   if(g_state == S_CLOSING)
   {
      CloseAllOurs();
      if(!HasAnyPosition())
      {
         // FIX6: Изчистваме hedge и pending при пълен reset
         g_hedgeTracker.active = false;
         ResetPendingReentries();
         ResetRuntimeState();
         g_state = S_IDLE; g_status = "Closed all";
      }
      if(ShowPanel) UpdatePanel(); return;
   }

   // S_ACTIVE
   CheckHedgeTrigger();                    // 1. Price-based, без закъснение
   bool changed = CheckCycleReset();       // 2. Cycle reset

   if(!changed)
   {
      CloseSignal sig = EvaluateCloseSignal();
      if(sig == CLOSE_SIGNAL_BUY && CloseManagedSide(POSITION_TYPE_BUY))
         { g_status = "Momentum close BUY"; changed = true; }
      else if(sig == CLOSE_SIGNAL_SELL && CloseManagedSide(POSITION_TYPE_SELL))
         { g_status = "Momentum close SELL"; changed = true; }
   }

   MaintainInventory();                    // 3. Поддържай инвентара (FIX4: блокирана при reset)
   if(changed) MaintainInventory();

   g_state = HasAnyPosition() ? S_ACTIVE : S_IDLE;
   if(g_status == "Starting..." || g_status == "") g_status = "ACTIVE";
   if(ShowPanel) UpdatePanel();
}
//+------------------------------------------------------------------+
