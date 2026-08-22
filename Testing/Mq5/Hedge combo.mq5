//+------------------------------------------------------------------+
//|                        HedgeCycleEA_v7.mq5                       |
//|                                                                  |
//|  ALL v6 logic preserved — 2 targeted additions only:            |
//|                                                                  |
//|  FIX 5 — Pause Float Monitor: even while EA is paused,          |
//|           every tick still checks TotalAllFloatingPnl().         |
//|           If float >= pause recovery target → triggers           |
//|           S_CLOSING immediately and books the profit.            |
//|           New trades still blocked until pause lifts.            |
//|           Also handles: float peak tracking during pause,        |
//|           FloatLock still fires during pause.                    |
//|                                                                  |
//|  FIX 6 — Symmetric trend behaviour: the pause monitor uses      |
//|           the same recovery target as S_MONITOR                  |
//|           (g_totalLocked + RecoveryExtraUSD) so a downtrend     |
//|           that recovers during a pause gets booked at the        |
//|           exact same threshold as an uptrend recovery.           |
//|           A standalone PauseFloatTargetUSD covers rounds         |
//|           that paused before any target was set.                 |
//+------------------------------------------------------------------+
#property copyright "HedgeCycleEA v7"
#property version   "7.00"
#property strict

#include <Trade\Trade.mqh>
#include <Trade\PositionInfo.mqh>

//==================================================================//
//  INPUTS
//==================================================================//
input group "=== Trade ==="
input double   LotSize            = 0.11;
input int      Slippage           = 50;
input double   MaxSpreadPoints    = 30.0;
input int      MagicBase          = 90000;

input group "=== 40-Pip Rule ==="
input double   WaitPips           = 40.0;

input group "=== Profit Targets ==="
input double   FirstTargetUSD     = 15.0;
input double   RecoveryExtraUSD   = 15.0;

input group "=== Hard Limits ==="
input int      MaxCycles          = 4;
input double   EmergencyLossPips  = 200.0;

input group "=== Round Loss Cap ==="
input bool     UseRoundCap        = true;
input double   MaxRoundLossUSD    = 45.0;

input group "=== Float Profit Lock ==="
input bool     UseFloatLock       = true;
input double   FloatLockMinUSD    = 5.0;
input double   FloatLockDropUSD   = 3.0;

input group "=== Account Protection ==="
input bool     UseHWM             = true;
input double   MaxDDFromPeakPct   = 10.0;
input bool     UseDailyLimit      = true;
input double   DailyLossLimitUSD  = 30.0;

// ── FIX 5 & 6: Pause Float Monitor ───────────────────────────────
input group "=== Pause Float Monitor ==="
input bool     MonitorDuringPause   = true;
// Used when EA paused BEFORE a recovery target was set (e.g. S_WAITING or S_IDLE)
// When in S_MONITOR, the normal (g_totalLocked + RecoveryExtraUSD) target is used instead.
input double   PauseFloatTargetUSD  = 10.0;

input group "=== Panel ==="
input bool     ShowPanel          = true;
input int      PanelX             = 10;
input int      PanelY             = 30;

//==================================================================//
//  STATE MACHINE
//==================================================================//
enum EState
{
   S_IDLE    = 0,
   S_WAITING = 1,
   S_MONITOR = 2,
   S_CLOSING = 3,
};

//==================================================================//
//  PER-CYCLE STRUCT
//==================================================================//
struct SCycle
{
   int    magicBuy;
   int    magicSell;
   int    winnerMagic;
   double lossLocked;
   bool   decided;
};

//==================================================================//
//  GLOBALS
//==================================================================//
CTrade        g_trade;
CPositionInfo g_pos;

EState  g_state         = S_IDLE;
double  g_startPrice    = 0.0;
double  g_highPrice     = 0.0;
double  g_lowPrice      = 0.0;
double  g_totalLocked   = 0.0;
int     g_cycleCount    = 0;
double  g_roundStartBal = 0.0;
int     g_rounds        = 0;
double  g_totalProfit   = 0.0;
bool    g_firstRun      = true;

SCycle  g_cycles[];

double   g_initialBalance  = 0.0;
double   g_floatPeak       = 0.0;
double   g_highWaterMark   = 0.0;
double   g_dayStartBalance = 0.0;
datetime g_lastDayReset    = 0;
bool     g_paused          = false;
string   g_pauseReason     = "";

string  g_status        = "Starting...";
#define PN "HC7_"
#define PANEL_ROWS 20

//==================================================================//
//  INIT / DEINIT
//==================================================================//
int OnInit()
{
   g_trade.SetDeviationInPoints(Slippage);
   g_trade.SetAsyncMode(false);
   g_trade.LogLevel(LOG_LEVEL_ERRORS);

   g_initialBalance  = AccountInfoDouble(ACCOUNT_BALANCE);
   g_roundStartBal   = g_initialBalance;
   g_highWaterMark   = AccountInfoDouble(ACCOUNT_EQUITY);
   g_dayStartBalance = g_initialBalance;
   g_lastDayReset    = iTime(_Symbol, PERIOD_D1, 0);

   ArrayResize(g_cycles, 0);
   if(ShowPanel) BuildPanel();

   Print("HedgeCycleEA v7 | Lot=", LotSize,
         " | WaitPips=", WaitPips,
         " | FirstTarget=$", FirstTargetUSD,
         " | RecovExtra=$", RecoveryExtraUSD,
         " | MaxCycles=", MaxCycles,
         " | PauseFloatTarget=$", PauseFloatTargetUSD);
   return INIT_SUCCEEDED;
}

void OnDeinit(const int reason)
{
   for(int i = ObjectsTotal(0,0,-1)-1; i >= 0; i--)
   {
      string name = ObjectName(0,i,0,-1);
      if(StringFind(name,PN) == 0) ObjectDelete(0,name);
   }
}

//==================================================================//
//  MAIN TICK
//==================================================================//
void OnTick()
{
   if(SpreadTooWide()) { if(ShowPanel) UpdatePanel(); return; }

   CheckDayReset();

   // ── FIX 5 & 6: Pause block — now monitors float before returning ─
   if(g_paused)
   {
      // Always let an in-progress S_CLOSING finish cleanly
      // This handles the case where S_CLOSING was triggered from inside
      // the pause block on a previous tick
      if(g_state == S_CLOSING)
      {
         HandleClosing();
         if(ShowPanel) UpdatePanel();
         return;
      }

      // Monitor floating P&L even while paused
      if(MonitorDuringPause && HasAnyPosition())
      {
         double fl = TotalAllFloatingPnl();

         // Keep tracking the float peak so FloatLock still works during pause
         if(fl > g_floatPeak) g_floatPeak = fl;

         // FIX 6: Use the same recovery target that S_MONITOR uses when
         // there are locked losses, so downtrend recovery during pause is
         // booked at the identical threshold as a live uptrend recovery.
         double pauseTarget = (g_totalLocked > 0.0)
                              ? (g_totalLocked + RecoveryExtraUSD)
                              : PauseFloatTargetUSD;

         if(fl >= pauseTarget)
         {
            Print("v7 FIX5: PAUSED but float=", DoubleToString(fl,2),
                  " >= target=", DoubleToString(pauseTarget,2),
                  ". Booking profit now.");
            g_status = StringFormat(
               "PAUSE PROFIT: Float=%.2f >= Target=%.2f. Closing!",
               fl, pauseTarget);
            g_state = S_CLOSING;
            if(ShowPanel) UpdatePanel();
            return;
         }

         // FloatLock still fires during pause
         if(UseFloatLock &&
            g_floatPeak >= FloatLockMinUSD &&
            fl          <= g_floatPeak - FloatLockDropUSD)
         {
            Print("v7 FIX5: PAUSED FloatLock: peak=", DoubleToString(g_floatPeak,2),
                  " now=", DoubleToString(fl,2), ". Closing.");
            g_status = StringFormat(
               "PAUSE FLOATLOCK: peak=%.2f dropped to %.2f. Closing.",
               g_floatPeak, fl);
            g_state = S_CLOSING;
            if(ShowPanel) UpdatePanel();
            return;
         }

         // Update status to show live float so panel is informative during pause
         g_status = StringFormat("PAUSED: %s | Float: %+.2f | Target: %+.2f",
                                  g_pauseReason, fl, pauseTarget);
      }
      else
      {
         g_status = "PAUSED: " + g_pauseReason + " | No open positions.";
      }

      if(ShowPanel) UpdatePanel();
      return;   // Still block new trade logic while paused
   }
   // ── End pause block ───────────────────────────────────────────

   // HWM drawdown check
   if(UseHWM)
   {
      double eq = AccountInfoDouble(ACCOUNT_EQUITY);
      if(eq > g_highWaterMark) g_highWaterMark = eq;

      if(g_highWaterMark > 0)
      {
         double ddPct = (g_highWaterMark - eq) / g_highWaterMark * 100.0;
         if(ddPct >= MaxDDFromPeakPct && g_state != S_IDLE && g_state != S_CLOSING)
         {
            g_pauseReason = StringFormat("HWM DD %.1f%% hit (peak=%.2f now=%.2f). EA paused.",
                                          ddPct, g_highWaterMark, eq);
            Print("v7: ", g_pauseReason);
            g_paused = true;
            g_state  = S_CLOSING;
            if(ShowPanel) UpdatePanel();
            return;
         }
      }
   }

   // Daily loss check
   if(UseDailyLimit)
   {
      double dayPnl = AccountInfoDouble(ACCOUNT_EQUITY) - g_dayStartBalance;
      if(dayPnl <= -DailyLossLimitUSD && g_state != S_IDLE && g_state != S_CLOSING)
      {
         g_pauseReason = StringFormat("Daily loss $%.2f limit hit. Paused until tomorrow.", DailyLossLimitUSD);
         Print("v7: ", g_pauseReason);
         g_paused = true;
         g_state  = S_CLOSING;
         if(ShowPanel) UpdatePanel();
         return;
      }
   }

   // Emergency pip check
   if(g_startPrice > 0.0)
   {
      double pip     = GetPipSize();
      double rangeUp = (g_highPrice - g_startPrice) / pip;
      double rangeDn = (g_startPrice - g_lowPrice)  / pip;
      double maxMove = MathMax(rangeUp, rangeDn);
      if(maxMove >= EmergencyLossPips)
      {
         g_status = StringFormat("EMERGENCY! %.1f pips >= %.0f limit. Closing all.", maxMove, EmergencyLossPips);
         Print(g_status);
         g_state = S_CLOSING;
      }
   }

   // Round cap check
   if(UseRoundCap && g_state != S_IDLE && g_state != S_CLOSING)
   {
      double fl = TotalAllFloatingPnl();
      if(fl <= -MaxRoundLossUSD)
      {
         g_status = StringFormat("ROUND CAP: Float=%.2f <= -$%.0f. Closing round.", fl, MaxRoundLossUSD);
         Print("v7: ", g_status);
         g_state = S_CLOSING;
      }
   }

   // First-run resync
   if(g_firstRun)
   {
      g_firstRun = false;
      if(HasAnyPosition()) { ResyncState(); if(ShowPanel) UpdatePanel(); return; }
   }

   switch(g_state)
   {
      case S_IDLE:    HandleIdle();    break;
      case S_WAITING: HandleWaiting(); break;
      case S_MONITOR: HandleMonitor(); break;
      case S_CLOSING: HandleClosing(); break;
   }

   if(ShowPanel) UpdatePanel();
}

//==================================================================//
//  S_IDLE
//==================================================================//
void HandleIdle()
{
   if(HasAnyPosition()) { CloseAllOurs(); return; }

   ArrayResize(g_cycles, 0);
   g_totalLocked   = 0.0;
   g_cycleCount    = 0;
   g_roundStartBal = AccountInfoDouble(ACCOUNT_BALANCE);
   g_floatPeak     = 0.0;

   if(!OpenNewCycle()) return;

   g_startPrice = MidPrice();
   g_highPrice  = g_startPrice;
   g_lowPrice   = g_startPrice;
   g_state      = S_WAITING;
   g_rounds++;

   g_status = StringFormat("Round #%d opened | Entry=%.5f | Waiting %.0f pips...",
                            g_rounds, g_startPrice, WaitPips);
   Print("v7: Round #", g_rounds, " opened. Entry=", g_startPrice);
}

//==================================================================//
//  S_WAITING
//==================================================================//
void HandleWaiting()
{
   double mid = MidPrice();
   if(mid > g_highPrice) g_highPrice = mid;
   if(mid < g_lowPrice)  g_lowPrice  = mid;

   double pip       = GetPipSize();
   double rangeUp   = (g_highPrice - g_startPrice) / pip;
   double rangeDn   = (g_startPrice - g_lowPrice)  / pip;
   double pipsMoved = MathMax(rangeUp, rangeDn);

   if(pipsMoved < WaitPips)
   {
      double buyPnl  = SidePnl(g_cycles[0].magicBuy);
      double sellPnl = SidePnl(g_cycles[0].magicSell);
      g_status = StringFormat("WAITING %.1f/%.0f pips | BUY=%+.2f SELL=%+.2f Net=%+.2f",
                               pipsMoved, WaitPips, buyPnl, sellPnl, buyPnl + sellPnl);
      return;
   }

   double buyPnl  = SidePnl(g_cycles[0].magicBuy);
   double sellPnl = SidePnl(g_cycles[0].magicSell);
   double netPnl  = buyPnl + sellPnl;

   Print("v7: 40 pips (", DoubleToString(pipsMoved,1), "). BUY=", buyPnl,
         " SELL=", sellPnl, " Net=", netPnl);

   if(netPnl >= FirstTargetUSD)
   {
      g_status = StringFormat("40-pip TARGET HIT! Net=%.2f >= $%.0f. Closing.", netPnl, FirstTargetUSD);
      Print("v7: First target hit. Closing all.");
      g_state = S_CLOSING;
      return;
   }

   if(g_cycleCount >= MaxCycles)
   {
      g_status = StringFormat("MAX %d CYCLES at first decision. Closing all.", MaxCycles);
      Print("v7: Max cycles at first decision. Closing.");
      g_state = S_CLOSING;
      return;
   }

   int    winnerMagic, loserMagic;
   double winnerPnl,   loserPnl;

   if(buyPnl >= sellPnl)
   {
      winnerMagic = g_cycles[0].magicBuy;  winnerPnl = buyPnl;
      loserMagic  = g_cycles[0].magicSell; loserPnl  = sellPnl;
   }
   else
   {
      winnerMagic = g_cycles[0].magicSell; winnerPnl = sellPnl;
      loserMagic  = g_cycles[0].magicBuy;  loserPnl  = buyPnl;
   }

   double lossAmt = MathAbs(loserPnl);
   g_totalLocked += lossAmt;

   g_cycles[0].winnerMagic = winnerMagic;
   g_cycles[0].decided     = true;

   Print("v7: Closing loser ", MagicLabel(loserMagic), " P&L=", loserPnl,
         " | Keeping ", MagicLabel(winnerMagic), " | TotalLocked=", g_totalLocked);

   CloseByMagic(loserMagic);

   if(!OpenNewCycle())
   {
      g_status = "New cycle open FAILED. Closing all.";
      g_state  = S_CLOSING;
      return;
   }

   g_state  = S_MONITOR;
   g_status = StringFormat("Loser closed (-%.2f). Winner kept (+%.2f). Cycle %d/%d.",
                            lossAmt, winnerPnl, g_cycleCount, MaxCycles);
}

//==================================================================//
//  S_MONITOR
//==================================================================//
void HandleMonitor()
{
   double totalPnl = TotalAllFloatingPnl();
   double target   = g_totalLocked + RecoveryExtraUSD;

   // FloatLock
   if(UseFloatLock)
   {
      if(totalPnl > g_floatPeak) g_floatPeak = totalPnl;

      if(g_floatPeak >= FloatLockMinUSD &&
         totalPnl    <= g_floatPeak - FloatLockDropUSD)
      {
         g_status = StringFormat(
            "FLOAT LOCK: peak=%.2f now=%.2f dropped %.2f. Closing to protect.",
            g_floatPeak, totalPnl, g_floatPeak - totalPnl);
         Print("v7: ", g_status);
         g_state = S_CLOSING;
         return;
      }
   }

   // Target hit
   if(totalPnl >= target)
   {
      Print("v7: MONITOR target hit! TotalPnl=", totalPnl, " Target=", target);
      g_status = StringFormat("RECOVERY HIT! P&L=%.2f >= %.2f. Closing ALL!", totalPnl, target);
      g_state  = S_CLOSING;
      return;
   }

   // Scan for worst loser
   double worstPnl   = -RecoveryExtraUSD;
   int    worstMagic = 0;

   for(int i = PositionsTotal()-1; i >= 0; i--)
   {
      if(!g_pos.SelectByIndex(i))              continue;
      if(g_pos.Symbol() != _Symbol)             continue;
      if(!IsOurMagic((int)g_pos.Magic()))       continue;

      double p = g_pos.Profit() + g_pos.Swap();
      if(p < worstPnl)
      {
         worstPnl   = p;
         worstMagic = (int)g_pos.Magic();
      }
   }

   if(worstMagic != 0 && totalPnl < 0.0)
   {
      if(g_cycleCount < MaxCycles)
      {
         double lossAmt = MathAbs(worstPnl);
         g_totalLocked += lossAmt;

         Print("v7: Monitor re-eval. Closing worst leg ", MagicLabel(worstMagic),
               " P&L=", worstPnl,
               " | NewLocked=", g_totalLocked,
               " | NewTarget=", g_totalLocked + RecoveryExtraUSD);

         CloseByMagic(worstMagic);

         if(!OpenNewCycle())
         {
            g_status = "Monitor recovery open FAILED. Closing all.";
            g_state  = S_CLOSING;
            return;
         }

         g_status = StringFormat("Monitor: closed worst (%.2f). New pair open. Cycle %d/%d. Target=%.2f",
                                  worstPnl, g_cycleCount, MaxCycles,
                                  g_totalLocked + RecoveryExtraUSD);
         return;
      }
      else
      {
         g_status = StringFormat("MAX %d CYCLES in monitor. Closing all.", MaxCycles);
         Print("v7: Max cycles in monitor. Closing.");
         g_state = S_CLOSING;
         return;
      }
   }

   string allStr = "";
   for(int i = 0; i < ArraySize(g_cycles); i++)
   {
      if(g_cycles[i].decided)
      {
         if(SideHasPos(g_cycles[i].winnerMagic))
            allStr += StringFormat("C%dW=%+.1f ", i+1, SidePnl(g_cycles[i].winnerMagic));
      }
      else
      {
         if(SideHasPos(g_cycles[i].magicBuy))
            allStr += StringFormat("C%dB=%+.1f ", i+1, SidePnl(g_cycles[i].magicBuy));
         if(SideHasPos(g_cycles[i].magicSell))
            allStr += StringFormat("C%dS=%+.1f ", i+1, SidePnl(g_cycles[i].magicSell));
      }
   }

   g_status = StringFormat("MONITOR | %s| Total=%+.2f / Target=+%.2f | C:%d/%d",
                            allStr, totalPnl, target, g_cycleCount, MaxCycles);
}

//==================================================================//
//  S_CLOSING
//==================================================================//
void HandleClosing()
{
   CloseAllOurs();

   if(HasAnyPosition())
   {
      g_status = StringFormat("Closing... (%d remain)", PositionsTotal());
      return;
   }

   double result     = AccountInfoDouble(ACCOUNT_BALANCE) - g_roundStartBal;
   double sessionNet = AccountInfoDouble(ACCOUNT_BALANCE) - g_initialBalance;
   g_totalProfit    += result;

   Print("v7: Round done. Result=", DoubleToString(result,2),
         " | SessionNet=", DoubleToString(sessionNet,2),
         " | AllTimeProfit=", DoubleToString(g_totalProfit,2),
         " | WasPaused=", g_paused);

   g_status = StringFormat("Round done. Result=%+.2f | Session=%+.2f | AllTime=%+.2f | Restarting...",
                            result, sessionNet, g_totalProfit);

   // FullReset sets g_state = S_IDLE.
   // If still paused, next tick the pause block intercepts before S_IDLE can open new trades.
   FullReset();
}

//==================================================================//
//  Daily reset
//==================================================================//
void CheckDayReset()
{
   if(!UseDailyLimit && !UseHWM) return;
   datetime todayBar = iTime(_Symbol, PERIOD_D1, 0);
   if(todayBar != g_lastDayReset)
   {
      g_lastDayReset    = todayBar;
      g_dayStartBalance = AccountInfoDouble(ACCOUNT_BALANCE);
      if(g_paused && StringFind(g_pauseReason, "Daily") >= 0)
      {
         g_paused      = false;
         g_pauseReason = "";
         Print("v7: New trading day — daily pause lifted. Resuming.");
      }
   }
}

//==================================================================//
//  OPEN NEW BUY+SELL CYCLE
//==================================================================//
bool OpenNewCycle()
{
   if(g_cycleCount >= MaxCycles)
   {
      Print("v7: MaxCycles (", MaxCycles, ") reached. Cannot open more.");
      return false;
   }

   g_cycleCount++;
   int mBuy  = MagicBase + (g_cycleCount * 2) - 1;
   int mSell = MagicBase + (g_cycleCount * 2);

   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);

   g_trade.SetExpertMagicNumber(mBuy);
   if(!g_trade.Buy(NormLots(LotSize), _Symbol, ask, 0, 0,
                   "HC_BUY_C" + IntegerToString(g_cycleCount)))
   {
      Print("v7: BUY FAILED cycle=", g_cycleCount, " err=", GetLastError());
      g_cycleCount--;
      return false;
   }

   g_trade.SetExpertMagicNumber(mSell);
   if(!g_trade.Sell(NormLots(LotSize), _Symbol, bid, 0, 0,
                    "HC_SELL_C" + IntegerToString(g_cycleCount)))
   {
      Print("v7: SELL FAILED cycle=", g_cycleCount, " err=", GetLastError());
      CloseByMagic(mBuy);
      g_cycleCount--;
      return false;
   }

   int idx = ArraySize(g_cycles);
   ArrayResize(g_cycles, idx + 1);
   g_cycles[idx].magicBuy    = mBuy;
   g_cycles[idx].magicSell   = mSell;
   g_cycles[idx].winnerMagic = 0;
   g_cycles[idx].lossLocked  = 0.0;
   g_cycles[idx].decided     = false;

   Print("v7: Cycle ", g_cycleCount, " | Buy=", mBuy, " Sell=", mSell,
         " | Ask=", DoubleToString(ask,_Digits),
         " Bid=", DoubleToString(bid,_Digits));
   return true;
}

//==================================================================//
//  RESYNC
//==================================================================//
void ResyncState()
{
   Print("v7: Resyncing from open positions...");
   ArrayResize(g_cycles, 0);
   g_cycleCount = 0;

   for(int c = 1; c <= MaxCycles; c++)
   {
      int  mBuy    = MagicBase + (c * 2) - 1;
      int  mSell   = MagicBase + (c * 2);
      bool hasBuy  = SideHasPos(mBuy);
      bool hasSell = SideHasPos(mSell);
      if(!hasBuy && !hasSell) continue;

      g_cycleCount++;
      int idx = ArraySize(g_cycles);
      ArrayResize(g_cycles, idx + 1);
      g_cycles[idx].magicBuy    = mBuy;
      g_cycles[idx].magicSell   = mSell;
      g_cycles[idx].winnerMagic = 0;
      g_cycles[idx].lossLocked  = 0.0;
      g_cycles[idx].decided     = (!hasBuy || !hasSell);
   }

   if(g_cycleCount == 0) { g_state = S_IDLE; g_status = "Resynced: IDLE"; return; }

   if(g_cycleCount == 1 && !g_cycles[0].decided)
   {
      g_startPrice = MidPrice();
      g_highPrice  = g_startPrice;
      g_lowPrice   = g_startPrice;
      g_state      = S_WAITING;
      g_status     = "Resynced: WAITING";
   }
   else
   {
      g_state  = S_MONITOR;
      g_status = StringFormat("Resynced: MONITOR | %d cycles", g_cycleCount);
   }

   Print("v7: Resynced. State=", StateStr(), " Cycles=", g_cycleCount);
}

//==================================================================//
//  POSITION UTILITIES
//==================================================================//
bool HasAnyPosition()
{
   for(int c = 1; c <= MaxCycles; c++)
   {
      if(SideHasPos(MagicBase + (c*2) - 1)) return true;
      if(SideHasPos(MagicBase + (c*2)))      return true;
   }
   return false;
}

bool SideHasPos(int magic) { return (CountPos(magic) > 0); }

int CountPos(int magic)
{
   int cnt = 0;
   for(int i = PositionsTotal()-1; i >= 0; i--)
   {
      if(!g_pos.SelectByIndex(i))     continue;
      if(g_pos.Symbol() != _Symbol)   continue;
      if((int)g_pos.Magic() != magic) continue;
      cnt++;
   }
   return cnt;
}

double SidePnl(int magic)
{
   double pnl = 0.0;
   if(magic == 0) return pnl;
   for(int i = PositionsTotal()-1; i >= 0; i--)
   {
      if(!g_pos.SelectByIndex(i))     continue;
      if(g_pos.Symbol() != _Symbol)   continue;
      if((int)g_pos.Magic() != magic) continue;
      pnl += g_pos.Profit() + g_pos.Swap();
   }
   return pnl;
}

double TotalAllFloatingPnl()
{
   double total = 0.0;
   for(int i = PositionsTotal()-1; i >= 0; i--)
   {
      if(!g_pos.SelectByIndex(i))        continue;
      if(g_pos.Symbol() != _Symbol)       continue;
      if(!IsOurMagic((int)g_pos.Magic())) continue;
      total += g_pos.Profit() + g_pos.Swap();
   }
   return total;
}

bool IsOurMagic(int m)
{
   for(int c = 1; c <= MaxCycles; c++)
   {
      if(m == MagicBase + (c*2) - 1) return true;
      if(m == MagicBase + (c*2))      return true;
   }
   return false;
}

void CloseByMagic(int magic)
{
   for(int i = PositionsTotal()-1; i >= 0; i--)
   {
      if(!g_pos.SelectByIndex(i))     continue;
      if(g_pos.Symbol() != _Symbol)   continue;
      if((int)g_pos.Magic() != magic) continue;
      g_trade.PositionClose(g_pos.Ticket(), Slippage);
   }
}

void CloseAllOurs()
{
   for(int c = 1; c <= MaxCycles; c++)
   {
      CloseByMagic(MagicBase + (c*2) - 1);
      CloseByMagic(MagicBase + (c*2));
   }
}

void FullReset()
{
   g_state       = S_IDLE;
   g_cycleCount  = 0;
   g_totalLocked = 0.0;
   g_startPrice  = 0.0;
   g_highPrice   = 0.0;
   g_lowPrice    = 0.0;
   g_floatPeak   = 0.0;
   ArrayResize(g_cycles, 0);
}

//==================================================================//
//  HELPERS
//==================================================================//
double MidPrice()
{
   return (SymbolInfoDouble(_Symbol, SYMBOL_ASK)
         + SymbolInfoDouble(_Symbol, SYMBOL_BID)) * 0.5;
}

double GetPipSize()
{
   int digits = (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);
   if(digits == 2 || digits == 3) return 0.10;
   if(digits == 5 || digits == 6) return 0.00010;
   if(digits == 4)                return 0.0010;
   return SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE) * 10.0;
}

double NormLots(double lots)
{
   double mn   = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double mx   = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
   double step = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
   if(step > 0.0)
      lots = MathFloor(lots / step) * step;
   return NormalizeDouble(MathMin(MathMax(lots, mn), mx), 2);
}

bool SpreadTooWide()
{
   double spread = (double)SymbolInfoInteger(_Symbol, SYMBOL_SPREAD);
   if(spread > MaxSpreadPoints)
   {
      g_status = StringFormat("Spread %.0f pts > max %.0f. Waiting...", spread, MaxSpreadPoints);
      return true;
   }
   return false;
}

string MagicLabel(int m)
{
   for(int c = 1; c <= MaxCycles; c++)
   {
      if(m == MagicBase + (c*2) - 1) return StringFormat("C%dBUY",  c);
      if(m == MagicBase + (c*2))      return StringFormat("C%dSELL", c);
   }
   return "UNKNOWN";
}

string StateStr()
{
   switch(g_state)
   {
      case S_IDLE:    return "IDLE";
      case S_WAITING: return "WAITING";
      case S_MONITOR: return "MONITOR";
      case S_CLOSING: return "CLOSING";
      default:        return "?";
   }
}

//==================================================================//
//  PANEL — 20 rows (row 19 added for pause float info)
//==================================================================//
void BuildPanel()
{
   for(int i = ObjectsTotal(0,0,-1)-1; i >= 0; i--)
   {
      string nm = ObjectName(0,i,0,-1);
      if(StringFind(nm,PN) == 0) ObjectDelete(0,nm);
   }

   ObjectCreate(0, PN+"bg", OBJ_RECTANGLE_LABEL, 0, 0, 0);
   ObjectSetInteger(0, PN+"bg", OBJPROP_CORNER,      CORNER_LEFT_UPPER);
   ObjectSetInteger(0, PN+"bg", OBJPROP_XDISTANCE,   PanelX - 5);
   ObjectSetInteger(0, PN+"bg", OBJPROP_YDISTANCE,   PanelY - 5);
   ObjectSetInteger(0, PN+"bg", OBJPROP_XSIZE,       390);
   ObjectSetInteger(0, PN+"bg", OBJPROP_YSIZE,       PANEL_ROWS * 21 + 10);
   ObjectSetInteger(0, PN+"bg", OBJPROP_BGCOLOR,     C'8,12,30');
   ObjectSetInteger(0, PN+"bg", OBJPROP_BORDER_TYPE, BORDER_FLAT);
   ObjectSetInteger(0, PN+"bg", OBJPROP_COLOR,       C'0,160,100');
   ObjectSetInteger(0, PN+"bg", OBJPROP_BACK,        true);

   for(int i = 0; i < PANEL_ROWS; i++)
   {
      string nm = PN+"r"+IntegerToString(i);
      ObjectCreate(0, nm, OBJ_LABEL, 0, 0, 0);
      ObjectSetInteger(0, nm, OBJPROP_CORNER,    CORNER_LEFT_UPPER);
      ObjectSetInteger(0, nm, OBJPROP_XDISTANCE, PanelX + 4);
      ObjectSetInteger(0, nm, OBJPROP_YDISTANCE, PanelY + i * 21);
      ObjectSetString (0, nm, OBJPROP_FONT,      "Consolas");
      ObjectSetInteger(0, nm, OBJPROP_FONTSIZE,  8);
      ObjectSetInteger(0, nm, OBJPROP_COLOR,     clrWhite);
      ObjectSetString (0, nm, OBJPROP_TEXT,      "");
   }
}

void PanelRow(int row, string txt, color clr)
{
   if(row < 0 || row >= PANEL_ROWS) return;
   string key = PN+"r"+IntegerToString(row);
   if(ObjectFind(0,key) < 0) return;
   ObjectSetString (0, key, OBJPROP_TEXT,  txt);
   ObjectSetInteger(0, key, OBJPROP_COLOR, clr);
}

void UpdatePanel()
{
   double bal        = AccountInfoDouble(ACCOUNT_BALANCE);
   double eq         = AccountInfoDouble(ACCOUNT_EQUITY);
   double totalPnl   = TotalAllFloatingPnl();
   double target     = g_totalLocked + RecoveryExtraUSD;
   double sessionNet = bal - g_initialBalance;
   double dayPnl     = eq  - g_dayStartBalance;
   double ddPct      = (g_highWaterMark > 0)
                       ? (g_highWaterMark - eq) / g_highWaterMark * 100.0 : 0.0;

   double pip      = GetPipSize();
   double rangeUp  = (g_highPrice > 0.0) ? (g_highPrice - g_startPrice) / pip : 0.0;
   double rangeDn  = (g_lowPrice  > 0.0) ? (g_startPrice - g_lowPrice)  / pip : 0.0;
   double pipsDone = MathMax(rangeUp, rangeDn);

   // Pause target shown in panel
   double pauseTarget = (g_totalLocked > 0.0)
                        ? (g_totalLocked + RecoveryExtraUSD)
                        : PauseFloatTargetUSD;

   color stateClr = clrCyan;
   if(g_state == S_WAITING) stateClr = clrYellow;
   if(g_state == S_MONITOR) stateClr = clrLightGreen;
   if(g_state == S_CLOSING) stateClr = clrOrange;
   if(g_paused)             stateClr = clrRed;

   PanelRow(0,  "=== Hedge Cycle EA v7 ===",                           clrCyan);
   PanelRow(1,  StringFormat("State: %-8s  Round#%d  Cycles:%d/%d",
                StateStr(), g_rounds, g_cycleCount, MaxCycles),         stateClr);
   PanelRow(2,  StringFormat("Entry:%.5f  Pips:%.1f/%.0f  Locked:$%.2f",
                g_startPrice, pipsDone, WaitPips, g_totalLocked),
                pipsDone >= WaitPips ? clrLime : clrYellow);
   PanelRow(3,  "--- Positions ---",                                    clrDimGray);

   int row = 4;
   for(int i = 0; i < ArraySize(g_cycles) && row < 12; i++)
   {
      if(g_cycles[i].decided)
      {
         int    wm   = g_cycles[i].winnerMagic;
         double wPnl = SidePnl(wm);
         PanelRow(row++,
            StringFormat("Cy%d WINNER %-10s : %+.2f", i+1, MagicLabel(wm), wPnl),
            wPnl >= 0.0 ? clrLime : clrTomato);
      }
      else
      {
         double bPnl = SidePnl(g_cycles[i].magicBuy);
         double sPnl = SidePnl(g_cycles[i].magicSell);
         PanelRow(row++,
            StringFormat("Cy%d B:%+.2f  S:%+.2f  Net:%+.2f",
               i+1, bPnl, sPnl, bPnl+sPnl),
            (bPnl+sPnl) >= 0.0 ? clrDeepSkyBlue : clrLightCoral);
      }
   }
   while(row < 12) PanelRow(row++, "", clrDimGray);

   PanelRow(12, StringFormat("Float: %+.2f  |  Peak: %+.2f  |  Target: %+.2f",
               totalPnl, g_floatPeak, target),
               totalPnl >= target ? clrLime : clrTomato);
   PanelRow(13, StringFormat("Session: %+.2f  |  AllTime: %+.2f",
               sessionNet, g_totalProfit),
               sessionNet >= 0 ? clrLime : clrTomato);
   PanelRow(14, StringFormat("Day P&L: %+.2f / limit:-$%.0f",
               dayPnl, DailyLossLimitUSD),
               dayPnl >= 0 ? clrLime
                           : MathAbs(dayPnl) >= DailyLossLimitUSD*0.8 ? clrOrange : clrDimGray);
   PanelRow(15, StringFormat("HWM: %.2f  DD: %.1f%% / max:%.0f%%",
               g_highWaterMark, ddPct, MaxDDFromPeakPct),
               ddPct >= MaxDDFromPeakPct*0.8 ? clrOrange : clrDimGray);
   PanelRow(16, StringFormat("Balance:%.2f  Equity:%.2f",              bal, eq),
               eq >= bal ? clrLime : clrOrange);
   PanelRow(17, StringFormat("Round cap:-$%.0f  | Float:%+.2f",
               MaxRoundLossUSD, totalPnl),
               totalPnl <= -MaxRoundLossUSD*0.8 ? clrOrange : clrDimGray);
   // Row 18: pause monitor status — shows live float vs pause target when paused
   if(g_paused)
      PanelRow(18, StringFormat("PAUSED | Float:%+.2f | PauseTarget:+%.2f",
                  totalPnl, pauseTarget),
                  totalPnl >= pauseTarget*0.8 ? clrLime : clrOrange);
   else
      PanelRow(18, StringFormat("PauseMonitor ON | Target:+%.2f",
                  pauseTarget), clrDimGray);
   PanelRow(19, StringFormat(">> %.46s",
               g_paused ? ("PAUSED: "+g_pauseReason) : g_status),
               g_paused ? clrRed : clrDimGray);
}
//+------------------------------------------------------------------+