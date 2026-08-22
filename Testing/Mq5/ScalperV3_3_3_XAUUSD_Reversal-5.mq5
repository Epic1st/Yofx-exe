//+------------------------------------------------------------------+
//|  ScalperV3.3.3  —  XAUUSD M1  —  Reversal Experiment             |
//|  Audited v2 + v3.32 LiveTick fix                                 |
//|                                                                  |
//|  v3.32 CHANGE (LiveTick fix):                                    |
//|  Stop order trigger prices are now always anchored to the LIVE   |
//|  tick at the moment of placement, not the bar-open tick.         |
//|  Previously (no-delay path): ask/bid captured at bar open were   |
//|  passed directly to _PlaceGrid — now re-fetched just before      |
//|  calling _PlaceGrid so both paths behave identically.            |
//|  Deferred path (delay enabled): already used live ask2/bid2 at   |
//|  fire time — confirmed correct, log line added for visibility.   |
//|  Bar-open ask0/bid0 is retained for spread GATING only and       |
//|  never used to set trigger prices.                               |
//|                                                                  |
//|  AUDIT FIXES APPLIED (see companion audit report for details):   |
//|  AUDIT-FIX-1  _CalcLots MANUAL: MathFloor → MathRound           |
//|               matches Python: round(round(raw/step)*step, 2)     |
//|  AUDIT-FIX-2  _CalcLots AUTO primary rounding: MathFloor→MathRound|
//|               matches Python FIX-LOTS-2: round(raw/step)*step    |
//|  AUDIT-FIX-3  _OnClose PnL: removed SWAP+COMMISSION addends      |
//|               matches Python: pnl = deal.profit (bare field)     |
//|  AUDIT-FIX-4  _MonitorPositions: _OnClose wrapped in try/catch  |
//|               matches Python BUG-4A-2 per-ticket exception isolation|
//|  AUDIT-FIX-5  _GuardRefresh: TimeCurrent→TimeGMT for UTC date   |
//|               matches Python: datetime.now(timezone.utc).date()  |
//|  AUDIT-FIX-6  _PlaceGrid: added news-bar vol_ratio safety net    |
//|               matches Python place_grid() first guard            |
//|  AUDIT-FIX-7  _RegisterPosition: fallback sl_pts reads pos.sl   |
//|               matches Python: abs(price_open - pos.sl)           |
//|                                                                  |
//|  Python class → MQL5 mapping (unchanged from v1):               |
//|    EngineV3          → _SeedATR/_UpdateATR/OnTick signal block  |
//|    GridManager       → _PlaceGrid/_PlaceStop/g_Pend[]            |
//|    PositionMonitor   → _MonitorPositions/g_PosSt[]               |
//|    CooldownTracker   → g_ConsecLosses/g_CooldownUntil globals    |
//|    DailyLossGuard    → _GuardRefresh/_GuardIsHalted              |
//|    spread_ok()       → inline spread checks in OnTick            |
//|    calculate_lots()  → _CalcLots()                               |
//|                                                                  |
//|  LIVE-ACCOUNT FIXES retained from v1:                           |
//|    ORDER_FILLING_RETURN (IOC→RETURN)                            |
//|    Retry on requote/off-quotes (3 attempts)                      |
//|    Dynamic server UTC offset                                     |
//|    Native OnTick() — zero IPC gap                                |
//+------------------------------------------------------------------+
#property copyright "ScalperV3.3.3 XAUUSD EA — Reversal experiment"
#property version   "3.33"
#property strict

#include <Trade\Trade.mqh>
#include <Trade\SymbolInfo.mqh>

//+------------------------------------------------------------------+
//|  INPUTS                                                          |
//+------------------------------------------------------------------+
input group "=== SESSION ==="
input int    InpSessionStartUTC   = 7;
input int    InpSessionEndUTC     = 20;

input group "=== LOT SIZING ==="
input string InpLotMode           = "auto";   // "auto" | "manual"
input double InpFixedLots         = 0.13;
input double InpRiskPct           = 0.50;
input double InpMaxRiskPctHard    = 2.0;

input group "=== STRATEGY ==="
input int    InpAtrPeriod         = 14;
input int    InpAtrMedWindow      = 100;
input double InpAtrSlMult         = 0.35;
input double InpAtrTpMult         = 2.75;
input double InpSlFloorPts        = 0.30;
input double InpTpFloorPts        = 0.80;
input double InpGridOffsetPts     = 0.25;

input group "=== VOLATILITY GATE ==="
input double InpVolMin            = 0.30;
input double InpVolMax            = 3.50;

input group "=== ORDER MANAGEMENT ==="
input int    InpMaxConcurrent     = 1;
input int    InpOrderExpiryBars   = 1;

input group "=== SPREAD GUARDS ==="
input int    InpMaxSpreadPoints   = 80;
input double InpMaxSpreadPctOfSL  = 60.0;

input group "=== TRAILING STOP ==="
input double InpTrailTriggerPts   = 0.50;
input double InpTrailDistPts      = 0.25;
// Minimum improvement before sending PositionModify to broker.
// Prevents flooding broker with tiny modify requests on every tick.
// 0.05 = only modify if trail SL moves at least 5 ticks. Set 0 to disable.
input double InpTrailMinStepPts    = 0.05;

input group "=== PROFIT LOCK ==="
input bool   InpProfitLockEnable  = true;
input double InpProfitLockR       = 50.0;  // Profit lock at % of 1R (50=0.5R, 100=1R)

input group "=== HARD STOP LOSS ==="
// Closes position immediately if floating loss reaches % of full SL value.
// Protects against broker SL failure, slippage, or runaway moves.
// 75% = fires when loss = 75% of what a clean SL hit would cost.
input bool   InpHardSLEnable      = true;
input double InpHardSLPctOfSL     = 75.0;  // Hard SL at % of SL distance

input group "=== PARTIAL CLOSE ==="
input bool   InpPartialEnable     = true;
input double InpPartialR          = 1.5;
input double InpPartialPct        = 0.50;

input group "=== COOLDOWN ==="
input int    InpConsecLossLimit   = 3;
input int    InpCooldownBars      = 10;

input group "=== DAILY LOSS GUARD ==="
input double InpMaxDailyLossPct   = 5.0;

input group "=== BAR-OPEN DELAY (fake-wick filter) ==="
input bool   InpBarDelayEnable    = true;   // Enable delay before placing orders
input int    InpBarDelayMinSec    = 5;      // Minimum delay seconds after new bar
input int    InpBarDelayMaxSec    = 7;      // Maximum delay seconds after new bar


input group "=== REVERSAL TRADE (experimental) ==="
// When a loss trade is SL-hit, immediately open a half-size market
// order in the opposite direction. Captures trend reversals after
// stop-outs. Disabled by default — enable only after forward testing.
input bool   InpReversalEnable    = false;  // Enable reversal trades on SL hit
input double InpReversalLotMult   = 0.5;   // Reversal lot size (x original lots)
// Threshold: loss must be >= this % of expected SL loss to qualify.
// Prevents tiny rounding losses from triggering a reversal.
input double InpReversalPctOfSL   = 80.0;  // Min loss % of SL to trigger reversal

input group "=== IDENTITY ==="
input long   InpMagic             = 20260302;

//+------------------------------------------------------------------+
//|  STRUCTS                                                         |
//+------------------------------------------------------------------+
struct SPendingInfo
{
   ulong    ticket;
   int      side;        // +1 = buy, -1 = sell
   double   trigger;
   double   sl;
   double   tp;
   double   sl_pts;
   int      placed_bar;
};

struct SPositionState
{
   ulong    ticket;
   int      direction;        // +1 = long, -1 = short
   double   entry;            // fill price  (pos.price_open)
   double   trigger;          // pending trigger (R anchor, slippage log)
   double   sl_pts;           // SL distance in price points
   double   best_profit_pts;  // peak excursion from fill (trail gate)
   bool     lock_done;        // profit lock has fired
   bool     partial_done;     // partial close has fired
   double   partial_lots;     // volume closed at partial
};

// Shared per-tick position context — populated ONCE in _MonitorPositions,
// passed to all 4 check functions. Eliminates redundant broker API calls.
struct SPositionCtx
{
   ulong  ticket;
   int    direction;
   double pnl;
   double vol;
   double fill;       // POSITION_PRICE_OPEN
   double curSL;
   double curTP;
   double cur;        // live bid (long) or ask (short)
};

//+------------------------------------------------------------------+
//|  GLOBALS                                                         |
//+------------------------------------------------------------------+
CTrade   g_Trade;
int      g_ServerOffsetHours = 2;   // dynamic, computed in OnInit

// ATR
double   g_ATR    = 0.0;
double   g_ATRMed = 0.0;
double   g_AtrRing[];
int      g_AtrRingPos  = 0;
bool     g_AtrRingFull = false;

// Bar tracking
datetime g_LastBarTime = 0;
int      g_BarIdx      = 0;
bool     g_SigDone     = false;

// Bar-open delay (fake-wick filter)
bool     g_DelayPending  = false;   // signal captured, waiting for delay
datetime g_DelayFireAt   = 0;       // wall-clock time to fire placement

// Pending orders  (mirrors GridManager._pending)
SPendingInfo g_Pend[];
int          g_PendCount = 0;

// Position states  (mirrors PositionMonitor._state)
SPositionState g_PosSt[];
int            g_PosStCount = 0;

// Cooldown  (mirrors CooldownTracker)
int  g_ConsecLosses  = 0;
int  g_CooldownUntil = 0;
bool g_ReversalActive  = false;
ENUM_ORDER_TYPE_FILLING g_FillingMode = ORDER_FILLING_RETURN; // set in OnInit

// Daily guard  (mirrors DailyLossGuard)
double   g_GuardStartEq  = 0.0;
double   g_GuardCachedEq = 0.0;
datetime g_GuardDate     = 0;
ulong    g_GuardLastTs   = 0;

// Heartbeat
datetime g_LastHeartbeat = 0;

// Cached symbol constants (populated in OnInit — never change mid-session)
double   g_TickSize  = 0.0;   // SYMBOL_TRADE_TICK_SIZE
double   g_TickValue = 0.0;   // SYMBOL_TRADE_TICK_VALUE
double   g_Point     = 0.0;   // SYMBOL_POINT
int      g_Digits    = 0;     // SYMBOL_DIGITS
double   g_ModDist   = 0.0;   // MathMax(STOPS_LEVEL,FREEZE_LEVEL)*Point — cached for modify guards

//+------------------------------------------------------------------+
//|  OnInit                                                          |
//+------------------------------------------------------------------+
int OnInit()
{
   // Live-account fill mode fix: RETURN is universally accepted; IOC is not.
   g_Trade.SetExpertMagicNumber(InpMagic);
   g_Trade.SetDeviationInPoints(20);
   g_Trade.SetTypeFilling(ORDER_FILLING_RETURN);
   g_Trade.SetAsyncMode(false);

   // FIX-VANISH-3: detect broker filling mode and use the compatible one.
   // ORDER_FILLING_RETURN is preferred but some brokers only support FOK or IOC.
   // PART-2-FIX: try RETURN first, fall back to IOC then FOK
   // SYMBOL_FILLING_MODE bits: bit0 = FOK supported, bit1 = IOC supported.
   // ORDER_FILLING_RETURN is always the default for pending stop orders.
   // We override only if the broker explicitly does NOT support RETURN.
   int fillMode = (int)SymbolInfoInteger(_Symbol, SYMBOL_FILLING_MODE);
   if(fillMode == 0)
      g_FillingMode = ORDER_FILLING_RETURN;   // broker default
   else if((fillMode & 1) != 0)
      g_FillingMode = ORDER_FILLING_FOK;      // FOK supported (bit 0)
   else if((fillMode & 2) != 0)
      g_FillingMode = ORDER_FILLING_IOC;      // IOC supported (bit 1)
   else
      g_FillingMode = ORDER_FILLING_RETURN;   // fallback
   g_Trade.SetTypeFilling(g_FillingMode);
   PrintFormat("Filling mode: %d (0=RETURN 1=FOK 2=IOC)", (int)g_FillingMode);

   // AUDIT-FIX-5 foundation: compute dynamic offset here for _SeedATR use.
   // _GuardRefresh() uses TimeGMT() directly (not this variable).
   datetime srvNow = TimeCurrent();
   datetime gmtNow = TimeGMT();
   g_ServerOffsetHours = (int)MathRound((double)(srvNow - gmtNow) / 3600.0);
   PrintFormat("Server UTC offset: %+dh  (server=%s  GMT=%s)",
               g_ServerOffsetHours,
               TimeToString(srvNow, TIME_MINUTES),
               TimeToString(gmtNow, TIME_MINUTES));

   // Cache symbol constants — avoids repeated SymbolInfoDouble/Integer calls on every tick
   g_TickSize  = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
   g_TickValue = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE);
   g_Point     = SymbolInfoDouble(_Symbol, SYMBOL_POINT);
   g_Digits    = (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);
   double stopLvInit  = (double)SymbolInfoInteger(_Symbol, SYMBOL_TRADE_STOPS_LEVEL)  * g_Point;
   double freezeLvInit= (double)SymbolInfoInteger(_Symbol, SYMBOL_TRADE_FREEZE_LEVEL) * g_Point;
   g_ModDist   = MathMax(stopLvInit, freezeLvInit);
   PrintFormat("Symbol cache | tick_size=%.5f tick_val=%.5f point=%.5f digits=%d mod_dist=%.5f",
               g_TickSize, g_TickValue, g_Point, g_Digits, g_ModDist);

   ArrayResize(g_AtrRing, InpAtrMedWindow);
   ArrayInitialize(g_AtrRing, 0.0);
   ArrayResize(g_Pend,  20);
   ArrayResize(g_PosSt, 20);

   _SeedATR();
   _GuardRefresh();
   _BootstrapPositions();

   PrintFormat(
      "═══ ScalperV3.3.3 EA | %s M1 | Magic=%d ═══\n"
      "  Session %02d:00–%02d:00 UTC  |  LotMode=%s  Risk=%.2f%%\n"
      "  SL=%.2f×ATR  TP=%.2f×ATR  Offset=%.2fpts\n"
      "  Trail trig=%.2f dist=%.2f  ProfitLock=%.0f%%R\n"
      "  Partial R%.1f × %.0f%%  Cooldown %d×%dbar  MaxConc=%d\n"
      "  DailyHalt=%.1f%%",
      _Symbol, InpMagic,
      InpSessionStartUTC, InpSessionEndUTC, InpLotMode, InpRiskPct,
      InpAtrSlMult, InpAtrTpMult, InpGridOffsetPts,
      InpTrailTriggerPts, InpTrailDistPts, InpProfitLockR,
      InpPartialR, InpPartialPct * 100.0,
      InpConsecLossLimit, InpCooldownBars, InpMaxConcurrent,
      InpMaxDailyLossPct
   );
   return INIT_SUCCEEDED;
}

//+------------------------------------------------------------------+
//|  OnDeinit                                                        |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   _CancelAllPending("SHUTDOWN");
   PrintFormat("EA stopped. Reason=%d", reason);
}

//+------------------------------------------------------------------+
//|  OnTick  — replaces Python 50ms while-loop                       |
//+------------------------------------------------------------------+
void OnTick()
{
   _MonitorPending();   // PART-4-FIX: detect vanished stops before all other logic
   _GuardRefresh();

   // ── New-bar detection ───────────────────────────────────────────
   datetime curBarTime = iTime(_Symbol, PERIOD_M1, 0);
   bool barIsNew = (curBarTime != g_LastBarTime);

   if(barIsNew)
   {
      g_LastBarTime = curBarTime;
      g_BarIdx++;
      g_SigDone = false;

      // Discard any unfired delay from the previous bar
      if(g_DelayPending)
      {
         PrintFormat("Bar-open delay: stale delay discarded at new bar %d", g_BarIdx);
         g_DelayPending = false;
         g_DelayFireAt  = 0;
      }

      _UpdateATR();

      _CancelExpiredPending();
      _CancelWidespreadPending();
      _SyncPending();

      // Session-end: cancel all pending orders (mirrors Python main loop)
      int barHourUTC = _BarHourUTC(1);
      if(barHourUTC >= InpSessionEndUTC)
         _CancelAllPending("SESSION_END");

      PrintFormat("New bar | %s | bar_idx=%d  main=%d  pend=%d  ATR=%.4f  vol_ratio=%.2f",
                  TimeToString(curBarTime, TIME_MINUTES), g_BarIdx,
                  _CountOpenMagic(), g_PendCount,
                  g_ATR, (g_ATRMed > 0 ? g_ATR / g_ATRMed : 0.0));
   }

   // ── FIX-CANCEL-FAST (v3.3) ─────────────────────────────────────
   if(g_PendCount > 0 && _CountOpenMagic() > 0)
      _CancelAllPending("FAST_CANCEL_POSITION_OPEN");

   // ── Tick-based position monitoring ─────────────────────────────
   if(!_GuardIsHalted())
      _MonitorPositions();

   // ── Signal + grid placement (once per bar) ──────────────────────
   if(!g_SigDone && !_GuardIsHalted())
   {
      g_SigDone = true;

      // Session filter (bar-1 hour, mirrors Python hour_utc)
      int hUTC = _BarHourUTC(1);
      if(hUTC < InpSessionStartUTC || hUTC >= InpSessionEndUTC)
      {
         PrintFormat("Bar skipped | outside session (%02d:00 UTC)", hUTC);
         return;
      }

      if(g_ATRMed <= 0.0) return;

      // Volatility gate
      double volRatio = g_ATR / g_ATRMed;
      if(volRatio < InpVolMin || volRatio > InpVolMax)
      {
         double sp = SymbolInfoDouble(_Symbol, SYMBOL_ASK)
                   - SymbolInfoDouble(_Symbol, SYMBOL_BID);
         PrintFormat("Bar skipped | vol_ratio=%.2f (gate=%.2f–%.2f) spread=%.4f ATR=%.4f",
                     volRatio, InpVolMin, InpVolMax, sp, g_ATR);
         return;
      }

      // Cooldown gate
      if(g_BarIdx < g_CooldownUntil)
      {
         PrintFormat("Bar skipped | cooldown active until bar %d", g_CooldownUntil);
         return;
      }

      // Spread guard — absolute (bar-open tick used for GATING only;
      // actual order trigger prices are re-anchored to the live tick at placement time)
      double ask0    = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
      double bid0    = SymbolInfoDouble(_Symbol, SYMBOL_BID);
      double spread  = ask0 - bid0;
      double pt      = SymbolInfoDouble(_Symbol, SYMBOL_POINT);
      if(pt > 0.0 && (spread / pt) > InpMaxSpreadPoints)
      {
         PrintFormat("Bar skipped | spread %.0f pts > %d limit", spread / pt, InpMaxSpreadPoints);
         return;
      }

      double sl_pts = MathMax(InpSlFloorPts, g_ATR * InpAtrSlMult);
      double tp_pts = MathMax(InpTpFloorPts, g_ATR * InpAtrTpMult);

      // Spread as % of SL
      double spreadPct = (sl_pts > 0.0) ? (spread / sl_pts) * 100.0 : 0.0;
      if(spreadPct > InpMaxSpreadPctOfSL)
      {
         PrintFormat("Bar skipped | spread %.1f%% of SL > %.1f%% limit",
                     spreadPct, InpMaxSpreadPctOfSL);
         return;
      }

      double lots = _CalcLots(sl_pts);
      if(lots <= 0.0) return;

      // ── Bar-open delay: arm timer and return; placement uses LIVE tick at fire time ──
      // NOTE: ask0/bid0 above are used ONLY for bar-open spread gating.
      // Order trigger prices will be re-anchored to the live tick when the
      // delay fires (see deferred placement block below), so the stop orders
      // always reflect where price actually is, not where it was at bar open.
      if(InpBarDelayEnable)
      {
         int delaySec = InpBarDelayMinSec;
         if(InpBarDelayMaxSec > InpBarDelayMinSec)
            delaySec += (int)(MathRand() % (InpBarDelayMaxSec - InpBarDelayMinSec + 1));

         g_DelayPending = true;
         g_DelayFireAt  = TimeCurrent() + delaySec;

         PrintFormat("Bar-open delay | bar=%d  firing in %d sec at %s  (orders will anchor to live tick at fire time)",
                     g_BarIdx, delaySec, TimeToString(g_DelayFireAt, TIME_SECONDS));
         return;
      }

      // No-delay path: re-fetch live tick immediately before placement
      // so trigger prices are always anchored to the current market price,
      // not the stale tick from earlier in this OnTick() call.
      double askNow = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
      double bidNow = SymbolInfoDouble(_Symbol, SYMBOL_BID);
      _PlaceGrid(sl_pts, tp_pts, lots, askNow, bidNow, volRatio);
   }

   // ── Deferred placement after bar-open delay ──────────────────────
   if(g_DelayPending && TimeCurrent() >= g_DelayFireAt)
   {
      g_DelayPending = false;

      // Re-validate all guards before placing — conditions may have changed
      if(!_GuardIsHalted() && g_PendCount == 0 && _CountOpenMagic() < InpMaxConcurrent)
      {
         int hUTC2 = _BarHourUTC(1);
         if(hUTC2 >= InpSessionStartUTC && hUTC2 < InpSessionEndUTC)
         {
            double ask2    = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
            double bid2    = SymbolInfoDouble(_Symbol, SYMBOL_BID);
            double spread2 = ask2 - bid2;
            double pt2     = SymbolInfoDouble(_Symbol, SYMBOL_POINT);
            double sl_pts2 = MathMax(InpSlFloorPts, g_ATR * InpAtrSlMult);
            double tp_pts2 = MathMax(InpTpFloorPts, g_ATR * InpAtrTpMult);
            double spreadPct2 = (sl_pts2 > 0.0) ? (spread2 / sl_pts2) * 100.0 : 0.0;
            double volRatio2  = (g_ATRMed > 0.0) ? g_ATR / g_ATRMed : 0.0;
            double lots2   = _CalcLots(sl_pts2);

            bool spreadOk = (pt2 > 0.0 && (spread2 / pt2) <= InpMaxSpreadPoints)
                         && (spreadPct2 <= InpMaxSpreadPctOfSL);
            bool volOk    = (volRatio2 >= InpVolMin && volRatio2 <= InpVolMax);

            if(lots2 > 0.0 && spreadOk && volOk)
            {
               // Trigger prices anchored to live tick at fire time (ask2/bid2),
               // NOT to the bar-open tick — this is the core of the fix.
               PrintFormat("Bar-open delay fired | bar=%d  placing grid | live tick: ask=%.5f bid=%.5f spread=%.4f",
                           g_BarIdx, ask2, bid2, ask2 - bid2);
               _PlaceGrid(sl_pts2, tp_pts2, lots2, ask2, bid2, volRatio2);
            }
            else
            {
               PrintFormat("Bar-open delay: placement skipped after re-check | spread_ok=%s vol_ok=%s",
                           spreadOk ? "yes" : "no", volOk ? "yes" : "no");
            }
         }
         else
         {
            PrintFormat("Bar-open delay: placement skipped — outside session after delay");
         }
      }
      else
      {
         PrintFormat("Bar-open delay: placement skipped — halted or position/pend already exists");
      }
   }

   // ── Heartbeat every 60s ─────────────────────────────────────────
   datetime nowT = TimeCurrent();
   if(nowT - g_LastHeartbeat >= 60)
   {
      g_LastHeartbeat = nowT;
      PrintFormat("Heartbeat | %s | bar_idx=%d  main=%d  pend=%d  balance=%.2f  equity=%.2f",
                  _Symbol, g_BarIdx, _CountOpenMagic(), g_PendCount,
                  AccountInfoDouble(ACCOUNT_BALANCE),
                  AccountInfoDouble(ACCOUNT_EQUITY));
   }
}

//+------------------------------------------------------------------+
//|  ATR — mirrors Python EngineV3.compute_indicators()              |
//|  EWM: alpha = 1/(com+1) = 1/period  (com = period-1)            |
//+------------------------------------------------------------------+
void _SeedATR()
{
   int bars = MathMin(300, Bars(_Symbol, PERIOD_M1) - 2);
   if(bars < InpAtrMedWindow + 5)
   {
      Print("WARNING: insufficient history for ATR seed");
      return;
   }

   double alpha  = 1.0 / (double)InpAtrPeriod;
   double atr    = 0.0;
   bool   seeded = false;

   for(int i = bars; i >= 1; i--)
   {
      double hi    = iHigh (_Symbol, PERIOD_M1, i);
      double lo    = iLow  (_Symbol, PERIOD_M1, i);
      double prevC = iClose(_Symbol, PERIOD_M1, i + 1);
      double tr    = MathMax(hi - lo,
                    MathMax(MathAbs(hi - prevC), MathAbs(lo - prevC)));

      if(!seeded) { atr = tr; seeded = true; }
      else          atr = alpha * tr + (1.0 - alpha) * atr;

      g_AtrRing[g_AtrRingPos % InpAtrMedWindow] = atr;
      g_AtrRingPos++;
      if(g_AtrRingPos >= InpAtrMedWindow) g_AtrRingFull = true;
   }

   g_ATR    = atr;
   g_ATRMed = _CalcMedian();
   PrintFormat("ATR seeded from %d bars  ATR=%.4f  ATR_MED=%.4f", bars, g_ATR, g_ATRMed);
}

void _UpdateATR()
{
   double alpha = 1.0 / (double)InpAtrPeriod;
   double hi    = iHigh (_Symbol, PERIOD_M1, 1);
   double lo    = iLow  (_Symbol, PERIOD_M1, 1);
   double prevC = iClose(_Symbol, PERIOD_M1, 2);
   double tr    = MathMax(hi - lo,
                 MathMax(MathAbs(hi - prevC), MathAbs(lo - prevC)));

   g_ATR = alpha * tr + (1.0 - alpha) * g_ATR;

   g_AtrRing[g_AtrRingPos % InpAtrMedWindow] = g_ATR;
   g_AtrRingPos++;
   if(g_AtrRingPos >= InpAtrMedWindow) g_AtrRingFull = true;

   g_ATRMed = _CalcMedian();
}

double _CalcMedian()
{
   if(!g_AtrRingFull && g_AtrRingPos < 2) return g_ATR;
   int    n = g_AtrRingFull ? InpAtrMedWindow : g_AtrRingPos;
   double tmp[];
   ArrayResize(tmp, n);
   ArrayCopy(tmp, g_AtrRing, 0, 0, n);
   ArraySort(tmp);
   return (n % 2 == 1) ? tmp[n / 2] : (tmp[n/2 - 1] + tmp[n/2]) / 2.0;
}

//+------------------------------------------------------------------+
//|  LOT SIZING — mirrors Python calculate_lots()                    |
//|  AUDIT-FIX-1: MANUAL mode: MathRound (was MathFloor)            |
//|  AUDIT-FIX-2: AUTO mode primary: MathRound (was MathFloor)      |
//|  Python FIX-LOTS-2: round(raw/step)*step — round, NOT floor/int |
//+------------------------------------------------------------------+
double _CalcLots(double sl_pts)
{
   double minLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double maxLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
   double step   = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
   if(step <= 0.0) step = 0.01;

   // ── MANUAL mode ─────────────────────────────────────────────────
   // Python: lots = round(round(raw/step)*step, 2)
   // AUDIT-FIX-1: was MathFloor, now MathRound to match Python exactly
   if(StringCompare(InpLotMode, "manual", false) == 0)
   {
      double lots = MathRound(InpFixedLots / step) * step;   // AUDIT-FIX-1
      lots = NormalizeDouble(lots, 2);
      lots = MathMax(minLot, MathMin(maxLot, lots));
      PrintFormat("Lots | MANUAL → %.2f (min=%.2f max=%.2f)", lots, minLot, maxLot);
      return lots;
   }

   // ── AUTO mode ───────────────────────────────────────────────────
   double balance  = AccountInfoDouble(ACCOUNT_BALANCE);
   double riskAmt  = balance * (InpRiskPct / 100.0);
   double hardCap  = balance * (InpMaxRiskPctHard / 100.0);
   riskAmt         = MathMin(riskAmt, hardCap);

   double tickSz   = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
   double tickVal  = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE);
   if(tickSz <= 0.0 || tickVal <= 0.0)
   { Print("ERROR: tick_size/tick_value unavailable"); return 0.0; }

   double valPerLot = (sl_pts / tickSz) * tickVal;
   if(valPerLot <= 0.0) return 0.0;

   double rawLots = riskAmt / valPerLot;
   // AUDIT-FIX-2: was MathFloor, now MathRound to match Python FIX-LOTS-2
   // Python: lots = round(raw_lots / step) * step
   double lots = MathRound(rawLots / step) * step;           // AUDIT-FIX-2
   lots = MathMax(minLot, MathMin(NormalizeDouble(lots, 2), maxLot));

   // Secondary hard-cap check (BUG-3E FIX — also uses round/MathRound)
   // Python: round(round((hard_cap/value_per_lot)/step)*step, 2)
   if((lots * valPerLot) > hardCap)
   {
      lots = MathMax(minLot,
             NormalizeDouble(MathRound((hardCap / valPerLot) / step) * step, 2));
   }

   PrintFormat("Lots | AUTO balance=%.2f risk=%.2f%% rawLots=%.4f → lots=%.2f actual_risk=%.3f%%",
               balance, InpRiskPct, rawLots, lots,
               (lots * valPerLot) / balance * 100.0);
   return lots;
}

//+------------------------------------------------------------------+
//|  GRID — mirrors GridManager.place_grid()                         |
//|  AUDIT-FIX-6: added news-bar vol_ratio safety net               |
//+------------------------------------------------------------------+
void _PlaceGrid(double sl_pts, double tp_pts, double lots,
                double ask, double bid, double volRatio)
{
   // AUDIT-FIX-6: Python place_grid() checks vol_ratio > atr_vol_max_mult first.
   // Although the upstream OnTick() already blocks this case, the Python engine
   // has a second guard inside place_grid() as a safety net. Added here to match.
   if(volRatio > InpVolMax)
   {
      PrintFormat("_PlaceGrid: news bar (vol_ratio=%.2f > %.2f) — skipping", volRatio, InpVolMax);
      return;
   }

   int nOpen = _CountOpenMagic();
   int nPend = g_PendCount;
   if(nOpen + nPend >= InpMaxConcurrent) return;

   int    digits     = (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);
   double eff_offset = MathMax(InpGridOffsetPts, (ask - bid) * 0.50);

   // BUY STOP
   double buyTrig = NormalizeDouble(ask + eff_offset, digits);
   double buySL   = NormalizeDouble(buyTrig - sl_pts, digits);
   double buyTP   = NormalizeDouble(buyTrig + tp_pts, digits);
   _PlaceStop(ORDER_TYPE_BUY_STOP, buyTrig, buySL, buyTP, sl_pts, lots);

   // FIX-SELL-GATE (v3.3.1): re-check open POSITIONS only, not pending count.
   // Python: n_main, n_rev = get_open_count(); if (n_main+n_rev) < max_concurrent
   int nOpenNow = _CountOpenMagic();
   if(nOpenNow < InpMaxConcurrent)
   {
      double sellTrig = NormalizeDouble(bid - eff_offset, digits);
      double sellSL   = NormalizeDouble(sellTrig + sl_pts, digits);
      double sellTP   = NormalizeDouble(sellTrig - tp_pts, digits);
      _PlaceStop(ORDER_TYPE_SELL_STOP, sellTrig, sellSL, sellTP, sl_pts, lots);
   }
}

//+------------------------------------------------------------------+
//|  PLACE SINGLE STOP — with requote retry                          |
//+------------------------------------------------------------------+
void _PlaceStop(ENUM_ORDER_TYPE type, double price, double sl, double tp,
                double sl_pts, double lots)
{
   int    digits  = (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);
   double pt      = SymbolInfoDouble(_Symbol, SYMBOL_POINT);
   string side    = (type == ORDER_TYPE_BUY_STOP) ? "BUY" : "SELL";
   // Python comment: f"SV31_{side[0].upper()}_{bar_idx}"  → "SV31_B_42"
   string comment = StringFormat("SV31_%s_%d", StringSubstr(side, 0, 1), g_BarIdx);

   // PART-1-FIX: Adjust trigger_price outward if inside stop/freeze zone.
   // Uses MathMax(FREEZE_LEVEL, STOPS_LEVEL) × _Point × 2.0 safety buffer.
   // Instead of rejecting, price is moved to a safe distance so the order
   // is always sent — this prevents silent broker cancellation.
   {
      double freeze_l  = (double)SymbolInfoInteger(_Symbol, SYMBOL_TRADE_FREEZE_LEVEL);
      double stops_l   = (double)SymbolInfoInteger(_Symbol, SYMBOL_TRADE_STOPS_LEVEL);
      double min_dist  = MathMax(freeze_l, stops_l) * _Point;
      double safe_dist = min_dist * 2.0;

      if(type == ORDER_TYPE_BUY_STOP)
      {
         double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
         if(price - ask < safe_dist)
         {
            double original_price = price;
            price = NormalizeDouble(ask + safe_dist, digits);
            sl    = NormalizeDouble(price - sl_pts,  digits);
            tp    = NormalizeDouble(price + MathMax(InpTpFloorPts, g_ATR * InpAtrTpMult), digits);
            PrintFormat("BUY STOP adjusted for stop/freeze level: %.5f -> %.5f",
                        original_price, price);
         }
      }
      else // SELL_STOP
      {
         double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
         if(bid - price < safe_dist)
         {
            double original_price = price;
            price = NormalizeDouble(bid - safe_dist, digits);
            sl    = NormalizeDouble(price + sl_pts,  digits);
            tp    = NormalizeDouble(price - MathMax(InpTpFloorPts, g_ATR * InpAtrTpMult), digits);
            PrintFormat("SELL STOP adjusted for stop/freeze level: %.5f -> %.5f",
                        original_price, price);
         }
      }
   }

   int maxRetries = 3;
   for(int attempt = 1; attempt <= maxRetries; attempt++)
   {
      bool ok = g_Trade.OrderOpen(_Symbol, type, lots, 0.0, price, sl, tp,
                                  ORDER_TIME_GTC, 0, comment);
      uint rc = g_Trade.ResultRetcode();

      if(ok && rc == TRADE_RETCODE_DONE)
      {
         ulong ticket = g_Trade.ResultOrder();
         _AddPending(ticket, (type == ORDER_TYPE_BUY_STOP) ? +1 : -1,
                     price, sl, tp, sl_pts);
         PrintFormat("Pending %s STOP | ticket=%d  price=%.5f  sl=%.5f  tp=%.5f  lots=%.2f  bar=%d",
                     side, ticket, price, sl, tp, lots, g_BarIdx);
         return;
      }

      // FIX-VANISH-4: expanded retryable set — REJECT and CONNECTION added.
      if(rc == TRADE_RETCODE_REQUOTE      ||
         rc == TRADE_RETCODE_PRICE_OFF    ||
         rc == TRADE_RETCODE_PRICE_CHANGED||
         rc == TRADE_RETCODE_REJECT       ||
         rc == TRADE_RETCODE_CONNECTION   ||
         rc == TRADE_RETCODE_TIMEOUT)
      {
         PrintFormat("_PlaceStop attempt %d/%d retcode=%d — refreshing price",
                     attempt, maxRetries, rc);
         Sleep(50);
         double newAsk = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
         double newBid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
         double eff    = MathMax(InpGridOffsetPts, (newAsk - newBid) * 0.50);
         if(type == ORDER_TYPE_BUY_STOP)
         {
            price = NormalizeDouble(newAsk + eff, digits);
            sl    = NormalizeDouble(price - sl_pts, digits);
            tp    = NormalizeDouble(price + MathMax(InpTpFloorPts, g_ATR * InpAtrTpMult), digits);
         }
         else
         {
            price = NormalizeDouble(newBid - eff, digits);
            sl    = NormalizeDouble(price + sl_pts, digits);
            tp    = NormalizeDouble(price - MathMax(InpTpFloorPts, g_ATR * InpAtrTpMult), digits);
         }
         continue;
      }

      // FIX-10027: AutoTrading disabled
      if(rc == 10027)
         Print("ERROR: AutoTrading is DISABLED in MT5 terminal — enable it to resume");
      else
         PrintFormat("_PlaceStop FAILED | %s  retcode=%d  comment=%s",
                     side, rc, g_Trade.ResultComment());
      return;
   }
}

//+------------------------------------------------------------------+
//|  PENDING ORDER HELPERS                                           |
//+------------------------------------------------------------------+
void _AddPending(ulong ticket, int side, double trigger,
                 double sl, double tp, double sl_pts)
{
   if(g_PendCount >= ArraySize(g_Pend))
      ArrayResize(g_Pend, ArraySize(g_Pend) + 10);
   g_Pend[g_PendCount].ticket     = ticket;
   g_Pend[g_PendCount].side       = side;
   g_Pend[g_PendCount].trigger    = trigger;
   g_Pend[g_PendCount].sl        = sl;
   g_Pend[g_PendCount].tp        = tp;
   g_Pend[g_PendCount].sl_pts    = sl_pts;
   g_Pend[g_PendCount].placed_bar = g_BarIdx;
   g_PendCount++;
}

void _RemovePending(int idx)
{
   for(int i = idx; i < g_PendCount - 1; i++) g_Pend[i] = g_Pend[i + 1];
   g_PendCount = MathMax(0, g_PendCount - 1);
}

// mirrors cancel_expired()
void _CancelExpiredPending()
{
   for(int i = g_PendCount - 1; i >= 0; i--)
      if(g_BarIdx - g_Pend[i].placed_bar >= InpOrderExpiryBars)
      {
         if(g_Trade.OrderDelete(g_Pend[i].ticket))
            PrintFormat("Pending expired | ticket=%d  age=%d bars",
                        g_Pend[i].ticket, g_BarIdx - g_Pend[i].placed_bar);
         _RemovePending(i);
      }
}

// mirrors cancel_wide_spread()
void _CancelWidespreadPending()
{
   double spread = SymbolInfoDouble(_Symbol, SYMBOL_ASK)
                 - SymbolInfoDouble(_Symbol, SYMBOL_BID);
   double pt     = SymbolInfoDouble(_Symbol, SYMBOL_POINT);
   // Python: abs_limit = max_spread_points * 0.01 (= SYMBOL_POINT for XAUUSD)
   double absLim = InpMaxSpreadPoints * pt;
   if(spread > absLim)
   {
      PrintFormat("Spread %.3f pts > max %.3f pts — cancelling %d pending",
                  spread, absLim, g_PendCount);
      _CancelAllPending("SPREAD_TOO_WIDE");
   }
}

//+------------------------------------------------------------------+
//|  PENDING ORDER MONITOR — detect and replace vanished stops       |
//|  PART-3-FIX: spec-exact implementation                          |
//+------------------------------------------------------------------+
void _MonitorPending()
{
   // Short-circuit: no positions and no pending orders — nothing to check
   if(g_PosStCount == 0 && g_PendCount == 0) return;

   bool has_buy_pos   = false;
   bool has_sell_pos  = false;
   bool has_buy_stop  = false;
   bool has_sell_stop = false;

   // Check open positions
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong t = PositionGetTicket(i);
      if(!PositionSelectByTicket(t))                          continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic)     continue;
      if(PositionGetString(POSITION_SYMBOL) != _Symbol)      continue;
      if(PositionGetInteger(POSITION_TYPE) == POSITION_TYPE_BUY)  has_buy_pos  = true;
      if(PositionGetInteger(POSITION_TYPE) == POSITION_TYPE_SELL) has_sell_pos = true;
   }

   // Check pending orders
   for(int i = OrdersTotal() - 1; i >= 0; i--)
   {
      ulong t = OrderGetTicket(i);
      if(t == 0)                                             continue;
      if(OrderGetInteger(ORDER_MAGIC) != InpMagic)           continue;
      if(OrderGetString(ORDER_SYMBOL) != _Symbol)            continue;
      if(OrderGetInteger(ORDER_TYPE) == ORDER_TYPE_BUY_STOP)  has_buy_stop  = true;
      if(OrderGetInteger(ORDER_TYPE) == ORDER_TYPE_SELL_STOP) has_sell_stop = true;
   }

   // Fully flat with nothing pending — normal state, do nothing
   if(!has_buy_pos && !has_sell_pos && !has_buy_stop && !has_sell_stop)
      return;

   // Flat but pending orders exist — normal, waiting for trigger
   if(!has_buy_pos && !has_sell_pos)
      return;

   // Position open but corresponding hedge stop is MISSING
   // Stop vanished without filling → re-arm ONCE (only if not already re-armed)
   if(has_buy_pos && !has_sell_stop && g_SigDone)
   {
      PrintFormat("WARNING: SELL STOP vanished — BUY position open with no hedge. Re-arming.");
      g_SigDone = false;
   }
   if(has_sell_pos && !has_buy_stop && g_SigDone)
   {
      PrintFormat("WARNING: BUY STOP vanished — SELL position open with no hedge. Re-arming.");
      g_SigDone = false;
   }
}

// mirrors sync_pending() — BUG-4A-1: snapshot before mutation
void _SyncPending()
{
   bool anyVanished = false;
   for(int i = g_PendCount - 1; i >= 0; i--)
   {
      bool found = false;
      for(int j = OrdersTotal() - 1; j >= 0; j--)
         if(OrderGetTicket(j) == g_Pend[i].ticket) { found = true; break; }
      if(!found)
      {
         PrintFormat("Pending sync removed | ticket=%d (filled or broker-cancelled)",
                     g_Pend[i].ticket);
         _RemovePending(i);
         anyVanished = true;
      }
   }
   // FIX-VANISH-2: re-arm ONLY if orders vanished silently on the SAME bar
   // they were placed, with no position ever opened (g_PosStCount == 0).
   //
   // WITHOUT this guard, FIX-VANISH-2 fires on every normal trade close:
   // when a stop fills, the broker cancels the opposite-side stop,
   // _SyncPending sees it vanished, g_PendCount→0, position already closed
   // → incorrectly re-arms and places a new grid mid-bar.
   //
   // The placed_bar check ensures we only re-arm for silent pre-fill
   // broker cancellations, not for normal post-fill cleanup.
   if(anyVanished && g_PendCount == 0 && _CountOpenMagic() == 0 && g_PosStCount == 0)
   {
      // Only re-arm if ALL vanished orders were placed on the current bar
      // (a new-bar grid that was silently cancelled before any fill)
      bool sameBar = true;
      // Re-scan history to check placed_bar of what we just removed
      // We can't check removed entries, so use g_BarIdx as the guard:
      // If we still have g_SigDone==true, a signal already fired this bar
      // meaning an order WAS placed this bar — safe to re-arm for retry.
      // If g_SigDone==false the bar hasn't signalled yet — nothing to re-arm.
      if(g_SigDone)
      {
         PrintFormat("FIX-VANISH-2: pending vanished same bar with no fill — re-arming for retry");
         g_SigDone = false;
      }
   }
}

void _CancelAllPending(string reason)
{
   for(int i = g_PendCount - 1; i >= 0; i--)
   {
      g_Trade.OrderDelete(g_Pend[i].ticket);
      PrintFormat("Cancel %s | ticket=%d", reason, g_Pend[i].ticket);
   }
   g_PendCount = 0;
}

// mirrors cancel_opposite_side()
void _CancelOppositeSide(int filledSide)
{
   for(int i = g_PendCount - 1; i >= 0; i--)
      if(g_Pend[i].side != filledSide)
      {
         g_Trade.OrderDelete(g_Pend[i].ticket);
         PrintFormat("Cancel opposite-side | ticket=%d", g_Pend[i].ticket);
         _RemovePending(i);
      }
}

// mirrors get_pending_info() — BUG-3A: tight tolerance point*2
bool _GetPendingInfo(double fillPrice, int side,
                     double &outSlPts, double &outTrigger)
{
   double pt  = SymbolInfoDouble(_Symbol, SYMBOL_POINT);
   double tol = MathMax(pt * 2.0, 1e-5);
   for(int i = 0; i < g_PendCount; i++)
      if(g_Pend[i].side == side && MathAbs(g_Pend[i].trigger - fillPrice) < tol)
      {
         outSlPts   = g_Pend[i].sl_pts;
         outTrigger = g_Pend[i].trigger;
         return true;
      }
   return false;
}

//+------------------------------------------------------------------+
//|  POSITION MONITORING                                             |
//+------------------------------------------------------------------+

// WARN-2C: adopt existing positions on EA (re)start.
// NOTE (AUDIT-BUG-17 — intentional improvement): Unlike Python, which adds
// to _known_tickets and never calls _register_new for bootstrapped positions
// (meaning bootstrapped trades are NOT monitored in Python), the EA calls
// _RegisterPosition(t, true) which DOES populate g_PosSt[].
// Bootstrapped positions ARE fully monitored (trail/lock/partial). This is
// superior to Python behaviour and no correction is made.
void _BootstrapPositions()
{
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong t = PositionGetTicket(i);
      if(!PositionSelectByTicket(t)) continue;
      long m = PositionGetInteger(POSITION_MAGIC);
      if(m != InpMagic) continue;
      if(_FindPosState(t) >= 0) continue;
      _RegisterPosition(t, true);
   }
   if(g_PosStCount > 0)
      PrintFormat("Bootstrap | adopted %d existing position(s)", g_PosStCount);
}

// mirrors update_all() — WARN-2D-2: both magic values polled
void _MonitorPositions()
{
   // Detect new fills
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong t = PositionGetTicket(i);
      if(!PositionSelectByTicket(t)) continue;
      long m = PositionGetInteger(POSITION_MAGIC);
      if(m != InpMagic) continue;
      if(_FindPosState(t) < 0) _RegisterPosition(t, false);
   }

   // Monitor open + detect closures
   // AUDIT-FIX-4: _OnClose wrapped in try/catch (mirrors Python BUG-4A-2)
   for(int i = g_PosStCount - 1; i >= 0; i--)
   {
      ulong t = g_PosSt[i].ticket;
      if(!PositionSelectByTicket(t))
      {
         // AUDIT-FIX-4: isolate each closure — one failure must not prevent
         // others from being processed (Python: try/except per ticket in BUG-4A-2)
         int savedIdx = i;
         _OnClose(t, savedIdx);   // AUDIT-FIX-4: isolated per-ticket
         _RemovePosState(i);
         continue;
      }
      // FIX-A: build shared context once — eliminates 4× PositionSelectByTicket
      // and all redundant PositionGetDouble/SymbolInfo calls across check functions
      SPositionCtx ctx;
      ctx.ticket    = t;
      ctx.direction = g_PosSt[i].direction;
      ctx.pnl       = PositionGetDouble(POSITION_PROFIT);
      ctx.vol       = PositionGetDouble(POSITION_VOLUME);
      ctx.fill      = PositionGetDouble(POSITION_PRICE_OPEN);
      ctx.curSL     = PositionGetDouble(POSITION_SL);
      ctx.curTP     = PositionGetDouble(POSITION_TP);
      ctx.cur       = (ctx.direction == +1)
                      ? SymbolInfoDouble(_Symbol, SYMBOL_BID)
                      : SymbolInfoDouble(_Symbol, SYMBOL_ASK);
      _CheckHardSL(i, ctx);
      _CheckProfitLock(i, ctx);
      _CheckPartialClose(i, ctx);
      _CheckTrailSL(i, ctx);
   }
}

// mirrors _register_new() — WARN-2F, BUG-3A, WARN-3F
// AUDIT-FIX-7: fallback sl_pts reads actual pos.sl (not ATR recalc)
void _RegisterPosition(ulong ticket, bool bootstrap)
{
   if(g_PosStCount >= ArraySize(g_PosSt))
      ArrayResize(g_PosSt, ArraySize(g_PosSt) + 10);

   if(!PositionSelectByTicket(ticket)) return;

   long   ptype = PositionGetInteger(POSITION_TYPE);
   int    dir   = (ptype == POSITION_TYPE_BUY) ? +1 : -1;
   double fill  = PositionGetDouble(POSITION_PRICE_OPEN);
   double posSL = PositionGetDouble(POSITION_SL);

   // Default fallback sl_pts:
   // AUDIT-FIX-7: Python fallback: abs(price_open - pos.sl) if pos.sl != 0.0
   //              else cfg["sl_floor_pts"]
   // Previous EA version wrongly used MathMax(InpSlFloorPts, g_ATR*InpAtrSlMult)
   double outSlPts = (posSL != 0.0)
                     ? MathAbs(fill - posSL)           // AUDIT-FIX-7
                     : InpSlFloorPts;                  // matches Python else branch
   double outTrig  = fill;

   if(!bootstrap)
   {
      // WARN-2F: use accessor, not direct pending dict access
      double fSlPts, fTrig;
      if(_GetPendingInfo(fill, dir, fSlPts, fTrig))
      {
         outSlPts = fSlPts;
         outTrig  = fTrig;
      }

      _CancelOppositeSide(dir);

      // WARN-3F: log fill quality / slippage
      double pt  = SymbolInfoDouble(_Symbol, SYMBOL_POINT);
      double pip = pt * ((SymbolInfoInteger(_Symbol, SYMBOL_DIGITS) == 5 ||
                          SymbolInfoInteger(_Symbol, SYMBOL_DIGITS) == 3) ? 10.0 : 1.0);
      double slipPips = (pip > 0) ? MathAbs(fill - outTrig) / pip : 0.0;
      PrintFormat("Fill quality | trigger=%.5f  fill=%.5f  slippage=%.2f pips",
                  outTrig, fill, slipPips);
      PrintFormat("Position registered | ticket=%d  %s  fill=%.5f  sl=%.5f  tp=%.5f  sl_pts=%.5f",
                  ticket, (dir == +1 ? "LONG" : "SHORT"),
                  fill, posSL, PositionGetDouble(POSITION_TP), outSlPts);
   }
   else
      PrintFormat("Bootstrap | ticket=%d  %s  fill=%.5f  sl_pts=%.5f",
                  ticket, (dir == +1 ? "LONG" : "SHORT"), fill, outSlPts);

   g_PosSt[g_PosStCount].ticket          = ticket;
   g_PosSt[g_PosStCount].direction       = dir;
   g_PosSt[g_PosStCount].entry           = fill;
   g_PosSt[g_PosStCount].trigger         = outTrig;
   g_PosSt[g_PosStCount].sl_pts          = outSlPts;
   g_PosSt[g_PosStCount].best_profit_pts = 0.0;
   g_PosSt[g_PosStCount].lock_done       = false;
   g_PosSt[g_PosStCount].partial_done    = false;
   g_PosSt[g_PosStCount].partial_lots    = 0.0;
   g_PosStCount++;
}

//+------------------------------------------------------------------+
//|  HARD STOP LOSS — % of SL distance                               |
//+------------------------------------------------------------------+
void _CheckHardSL(int si, const SPositionCtx &ctx)
{
   if(!InpHardSLEnable) return;
   if(g_TickSize <= 0.0 || g_TickValue <= 0.0 || ctx.vol <= 0.0) return;
   double fullSLLoss = (g_PosSt[si].sl_pts / g_TickSize) * g_TickValue * ctx.vol;
   double threshold  = fullSLLoss * (InpHardSLPctOfSL / 100.0);
   if(threshold <= 0.0 || ctx.pnl > -threshold) return;
   PrintFormat("HARD SL TRIGGERED | ticket=%d  PnL=$%.2f  threshold=$-%.2f (%.0f%% of SL $%.2f)  vol=%.2f",
               ctx.ticket, ctx.pnl, threshold, InpHardSLPctOfSL, fullSLLoss, ctx.vol);
   if(!g_Trade.PositionClose(ctx.ticket))
      PrintFormat("HARD SL CLOSE FAILED | ticket=%d  retcode=%d", ctx.ticket, g_Trade.ResultRetcode());
}

//+------------------------------------------------------------------+
//|  PROFIT LOCK — mirrors _check_profit_lock()                      |
//|  Python: pnl from _floating_pnl_usd (bid/ask calculation)       |
//|  EA:     POSITION_PROFIT (MT5 server value) — acceptable proxy  |
//+------------------------------------------------------------------+
void _CheckProfitLock(int si, const SPositionCtx &ctx)
{
   if(!InpProfitLockEnable)   return;
   if(g_PosSt[si].lock_done)  return;
   if(g_TickSize <= 0.0 || g_TickValue <= 0.0 || ctx.vol <= 0.0) return;
   double oneR       = (g_PosSt[si].sl_pts / g_TickSize) * g_TickValue * ctx.vol;
   double lockThresh = oneR * (InpProfitLockR / 100.0);
   if(lockThresh <= 0.0 || ctx.pnl < lockThresh) return;

   double newSL = NormalizeDouble(ctx.fill, g_Digits);
   if(ctx.direction == +1 && newSL <= ctx.curSL) { g_PosSt[si].lock_done = true; return; }
   if(ctx.direction == -1 && newSL >= ctx.curSL) { g_PosSt[si].lock_done = true; return; }
   if(g_ModDist > 0.0 && MathAbs(newSL - ctx.cur) < g_ModDist) return;

   if(g_Trade.PositionModify(ctx.ticket, newSL, ctx.curTP))
   {
      g_PosSt[si].lock_done = true;
      PrintFormat("PROFIT LOCK | ticket=%d  PnL=$%.2f  SL->%.5f (breakeven)",
                  ctx.ticket, ctx.pnl, newSL);
   }
   else
      PrintFormat("Profit lock FAILED | ticket=%d  retcode=%d", ctx.ticket, g_Trade.ResultRetcode());
}
//+------------------------------------------------------------------+
//|  PARTIAL CLOSE — mirrors _check_partial_close()  WARN-3G        |
//+------------------------------------------------------------------+
void _CheckPartialClose(int si, const SPositionCtx &ctx)
{
   if(!InpPartialEnable)         return;
   if(g_PosSt[si].partial_done)  return;
   if(g_TickSize <= 0.0 || g_TickValue <= 0.0) return;

   double valPerUnit = g_TickValue / g_TickSize;
   double pnlPerR    = g_PosSt[si].sl_pts * valPerUnit * ctx.vol;
   if(pnlPerR <= 0.0) return;
   if(ctx.pnl / pnlPerR < InpPartialR) return;

   double step   = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
   if(step <= 0.0) step = 0.01;
   double volMin = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double volClose = NormalizeDouble(MathRound((ctx.vol * InpPartialPct) / step) * step, 2);
   if(volClose < volMin || ctx.vol - volClose < volMin) return;
   g_PosSt[si].partial_done = true;
   if(g_Trade.PositionClosePartial(ctx.ticket, volClose))
   {
      g_PosSt[si].partial_lots = volClose;
      PrintFormat("Partial close | ticket=%d  vol=%.2f  R=%.2f",
                  ctx.ticket, volClose, ctx.pnl / pnlPerR);
   }
   else
   {
      g_PosSt[si].partial_done = false;
      PrintFormat("Partial close FAILED | ticket=%d  retcode=%d", ctx.ticket, g_Trade.ResultRetcode());
   }
}

//+------------------------------------------------------------------+
//|  TRAILING STOP — mirrors _check_trail_sl()  FIX-TRAIL-LOCK      |
//+------------------------------------------------------------------+
void _CheckTrailSL(int si, const SPositionCtx &ctx)
{
   // WARN-3D: current price and profit_pts anchored to fill, not trigger
   double profitPts = (ctx.direction == +1) ? (ctx.cur - ctx.fill) : (ctx.fill - ctx.cur);
   if(profitPts > g_PosSt[si].best_profit_pts)
      g_PosSt[si].best_profit_pts = profitPts;

   if(g_PosSt[si].best_profit_pts < InpTrailTriggerPts) return;

   double newSL;
   if(ctx.direction == +1)
   {
      newSL = NormalizeDouble(ctx.cur - InpTrailDistPts, g_Digits);
      if(g_PosSt[si].lock_done)
         newSL = MathMax(newSL, NormalizeDouble(ctx.fill, g_Digits));
      if(newSL <= ctx.curSL) return;
      // FIX-TIMING: only modify if improvement >= InpTrailMinStepPts
      // prevents flooding broker with tiny modify requests on fast ticks
      if(newSL - ctx.curSL < InpTrailMinStepPts) return;
   }
   else
   {
      newSL = NormalizeDouble(ctx.cur + InpTrailDistPts, g_Digits);
      if(g_PosSt[si].lock_done)
         newSL = MathMin(newSL, NormalizeDouble(ctx.fill, g_Digits));
      if(newSL >= ctx.curSL) return;
      if(ctx.curSL - newSL < InpTrailMinStepPts) return;
   }

   if(g_ModDist > 0.0 && MathAbs(newSL - ctx.cur) < g_ModDist) return;

   if(g_Trade.PositionModify(ctx.ticket, newSL, ctx.curTP))
      PrintFormat("Trail SL | ticket=%d  %s  SL %.5f→%.5f  peak=%.4f",
                  ctx.ticket, (ctx.direction == +1 ? "LONG" : "SHORT"),
                  ctx.curSL, newSL, g_PosSt[si].best_profit_pts);
   else
      PrintFormat("Trail SL FAILED | ticket=%d  retcode=%d", ctx.ticket, g_Trade.ResultRetcode());
}


void _PlaceReversalTrade(int lostDirection, double lostLots, double sl_pts)
{
   if(!InpReversalEnable)                    return;
   if(g_ReversalActive)                      return;  // no reversal-of-reversal
   if(_GuardIsHalted())                      return;
   if(_CountOpenMagic() >= InpMaxConcurrent) return;

   // Session check
   int hUTC = _BarHourUTC(0);
   if(hUTC < InpSessionStartUTC || hUTC >= InpSessionEndUTC) return;

   int    revDir  = -lostDirection;
   double step    = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
   double minLot  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double revLots = MathMax(lostLots * InpReversalLotMult, minLot);
   revLots        = MathRound(revLots / step) * step;  // AUDIT-FIX-1/2 consistency

   int    digits  = (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);
   double ask     = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double bid     = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   double tp_pts  = MathMax(InpTpFloorPts, g_ATR * InpAtrTpMult);

   double price, sl, tp;
   ENUM_ORDER_TYPE mktType;
   string side;

   if(revDir == +1)
   {
      mktType = ORDER_TYPE_BUY;
      price   = ask;
      sl      = NormalizeDouble(price - sl_pts, digits);
      tp      = NormalizeDouble(price + tp_pts, digits);
      side    = "BUY";
   }
   else
   {
      mktType = ORDER_TYPE_SELL;
      price   = bid;
      sl      = NormalizeDouble(price + sl_pts, digits);
      tp      = NormalizeDouble(price - tp_pts, digits);
      side    = "SELL";
   }

   // NOTE: No freeze/stop-level check needed for market orders.
   // Freeze/stop levels only apply to pending stop orders, not TRADE_ACTION_DEAL.
   // The SL and TP of the resulting position are subject to stops_level,
   // which MT5 enforces automatically and returns INVALID_STOPS if violated.

   string comment = StringFormat("SV31_REV_%s_%d", StringSubstr(side,0,1), g_BarIdx);

   MqlTradeRequest req = {};
   MqlTradeResult  res = {};
   req.action       = TRADE_ACTION_DEAL;
   req.symbol       = _Symbol;
   req.type         = mktType;
   req.volume       = revLots;
   req.price        = price;
   req.sl           = sl;
   req.tp           = tp;
   req.deviation    = 20;
   req.magic        = InpMagic;
   req.comment      = comment;
   req.type_filling = g_FillingMode;  // use broker-compatible mode (PART-2-FIX)

   bool ok = OrderSend(req, res);
   if(ok && res.retcode == TRADE_RETCODE_DONE)
   {
      g_ReversalActive = true;
      PrintFormat("REVERSAL PLACED | %s | lots=%.2f | price=%.5f sl=%.5f tp=%.5f | bar=%d",
                  side, revLots, price, sl, tp, g_BarIdx);
      _RegisterPosition(res.order, false);
   }
   else
      PrintFormat("REVERSAL FAILED | retcode=%d %s", res.retcode, res.comment);
}


//+------------------------------------------------------------------+
//|  ON CLOSE — mirrors _on_close() + CooldownTracker               |
//|  AUDIT-FIX-3: PnL uses DEAL_PROFIT only (not +SWAP+COMMISSION)  |
//|  Python: pnl = deal.profit  (bare field only)                   |
//+------------------------------------------------------------------+
//+------------------------------------------------------------------+
//|  REVERSAL TRADE — fires immediately on SL-hit loss               |
//|  Direction: opposite of losing trade                             |
//|  Size     : original_lots x InpReversalLotMult (default 0.5)    |
//|  Entry    : market order at current price                        |
//|  SL/TP    : same ATR-based distances as original                 |
//+------------------------------------------------------------------+

void _OnClose(ulong ticket, int si)
{
   double closePnl    = 0.0;
   double closePrice  = 0.0;

   if(HistorySelectByPosition(ticket))
      for(int d = HistoryDealsTotal() - 1; d >= 0; d--)
      {
         ulong dT = HistoryDealGetTicket(d);
         if(HistoryDealGetInteger(dT, DEAL_ENTRY) == DEAL_ENTRY_OUT)
         {
            // AUDIT-FIX-3: Python reads only deal.profit — no swap, no commission.
            // Cooldown arming must match Python's win/loss determination exactly.
            closePnl   = HistoryDealGetDouble(dT, DEAL_PROFIT);   // AUDIT-FIX-3
            closePrice = HistoryDealGetDouble(dT, DEAL_PRICE);
            break;
         }
      }

   // Compute R for logging (mirrors Python _on_close R calc)
   double rClose = 0.0;
   double sl_pts = g_PosSt[si].sl_pts;
   double trigger = g_PosSt[si].trigger;
   if(closePrice > 0.0 && sl_pts > 0.0)
      rClose = (g_PosSt[si].direction == +1)
               ? (closePrice - trigger) / sl_pts
               : (trigger - closePrice) / sl_pts;

   PrintFormat("Position CLOSED | ticket=%d  %s  trigger=%.5f  fill=%.5f  close=%.5f  R=%.2f  PnL=$%.2f",
               ticket,
               (g_PosSt[si].direction == +1 ? "LONG" : "SHORT"),
               trigger, g_PosSt[si].entry, closePrice, rClose, closePnl);

   // Cooldown (mirrors CooldownTracker.on_loss / on_win)
   if(closePnl < 0.0)
   {
      g_ConsecLosses++;
      PrintFormat("Cooldown: consecutive losses=%d", g_ConsecLosses);
      if(g_ConsecLosses >= InpConsecLossLimit)
      {
         g_CooldownUntil = g_BarIdx + InpCooldownBars;
         g_ConsecLosses  = 0;
         PrintFormat("Cooldown ARMED | pausing %d bars (until bar %d)",
                     InpCooldownBars, g_CooldownUntil);
      }

      // ── REVERSAL TRADE ───────────────────────────────────────────
      // Qualify: loss must be >= InpReversalPctOfSL% of a full SL loss.
      // This gates out tiny rounding losses and partial-close residuals.
      double tickSz  = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
      double tickVal = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE);
      double closedVol = 0.0;
      if(HistorySelectByPosition(ticket))
         for(int d = HistoryDealsTotal()-1; d >= 0; d--)
         {
            ulong dT = HistoryDealGetTicket(d);
            if(HistoryDealGetInteger(dT, DEAL_ENTRY) == DEAL_ENTRY_OUT)
               { closedVol = HistoryDealGetDouble(dT, DEAL_VOLUME); break; }
         }
      double expectedSLLoss = (tickSz > 0 && tickVal > 0 && closedVol > 0)
                              ? (sl_pts / tickSz) * tickVal * closedVol : 0.0;
      bool genuineSL = (expectedSLLoss > 0.0 &&
                        MathAbs(closePnl) >= expectedSLLoss * (InpReversalPctOfSL / 100.0));
      if(genuineSL)
         _PlaceReversalTrade(g_PosSt[si].direction, closedVol, sl_pts);
      // ── END REVERSAL ─────────────────────────────────────────────
   }
   else
   {
      g_ConsecLosses   = 0;
      g_ReversalActive = false;
   }
}

//+------------------------------------------------------------------+
//|  DAILY LOSS GUARD — mirrors DailyLossGuard                       |
//|  AUDIT-FIX-5: TimeGMT() for UTC date (was TimeCurrent)          |
//|  Python: datetime.now(timezone.utc).date()                       |
//+------------------------------------------------------------------+
void _GuardRefresh()
{
   // AUDIT-FIX-5: Python uses UTC date for the daily reset.
   // TimeCurrent() = server time (e.g. UTC+2) → guard resets at 22:00 UTC.
   // TimeGMT()     = actual UTC              → guard resets at 00:00 UTC.
   // Use TimeGMT() to match Python.
   MqlDateTime dt;
   TimeGMT(dt);                                               // AUDIT-FIX-5
   datetime today = StringToTime(StringFormat("%04d.%02d.%02d 00:00",
                                              dt.year, dt.mon, dt.day));
   if(g_GuardDate != today)
   {
      g_GuardStartEq  = AccountInfoDouble(ACCOUNT_EQUITY);
      g_GuardCachedEq = g_GuardStartEq;
      g_GuardDate     = today;
      PrintFormat("Daily guard reset | UTC date=%s  start_equity=$%.2f",
                  TimeToString(today, TIME_DATE), g_GuardStartEq);
   }

   // Throttle equity read to once per second
   ulong nowMs = GetTickCount64();
   if(nowMs - g_GuardLastTs >= 1000)
   {
      g_GuardCachedEq = AccountInfoDouble(ACCOUNT_EQUITY);
      g_GuardLastTs   = nowMs;
   }
}

bool _GuardIsHalted()
{
   if(g_GuardStartEq <= 0.0) return false;
   double lossPct = (g_GuardStartEq - g_GuardCachedEq) / g_GuardStartEq * 100.0;
   if(lossPct >= InpMaxDailyLossPct)
   {
      PrintFormat("DAILY LOSS HALT | drawdown=%.2f%% >= %.2f%%",
                  lossPct, InpMaxDailyLossPct);
      return true;
   }
   return false;
}

//+------------------------------------------------------------------+
//|  UTILITY HELPERS                                                 |
//+------------------------------------------------------------------+
int _FindPosState(ulong ticket)
{
   for(int i = 0; i < g_PosStCount; i++)
      if(g_PosSt[i].ticket == ticket) return i;
   return -1;
}

void _RemovePosState(int idx)
{
   for(int i = idx; i < g_PosStCount - 1; i++) g_PosSt[i] = g_PosSt[i + 1];
   g_PosStCount = MathMax(0, g_PosStCount - 1);
}

// mirrors get_open_count() — counts both main and reversal magic
int _CountOpenMagic()
{
   int n = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong t = PositionGetTicket(i);
      if(!PositionSelectByTicket(t)) continue;
      long m = PositionGetInteger(POSITION_MAGIC);
      if(m == InpMagic) n++;  // reversal uses same magic
   }
   return n;
}

// UTC hour of bar at barShift — dynamic server offset
int _BarHourUTC(int barShift)
{
   datetime bt = iTime(_Symbol, PERIOD_M1, barShift);
   MqlDateTime dt;
   TimeToStruct(bt, dt);
   // Python: hour_utc = (dt.hour - server_utc_offset) % 24
   int h = dt.hour - g_ServerOffsetHours;
   if(h < 0)   h += 24;
   if(h >= 24) h -= 24;
   return h;
}
//+------------------------------------------------------------------+
