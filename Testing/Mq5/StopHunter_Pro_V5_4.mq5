//+------------------------------------------------------------------+
//|                                     StopHunter_Pro_V5_4.mq5      |
//|            ADVANCED LOSS REDUCTION ALGORITHM ENGINE               |
//|     Spread Filter + Smart BE + Partial Close + Loss Counter      |
//+------------------------------------------------------------------+
#property copyright "StopHunter Pro V5.4"
#property version   "5.40"
#property strict
#property description "ADVANCED ALGO: 4 Loss Reduction Systems"
#property description "1. Spread Filter - Skip high spread"
#property description "2. Smart BE - Entry + Spread + Buffer"
#property description "3. Partial Close - Lock 50% profit early"
#property description "4. Loss Counter - Stop after consecutive losses"

#include <Trade\Trade.mqh>

//+------------------------------------------------------------------+
//| INPUTS                                                           |
//+------------------------------------------------------------------+
input group "═══════ 1. ORDER SETTINGS ═══════"
input int      InpTradesPerSide      = 5;           // ▶ Orders Per Side: How many BuyStop + SellStop orders (5 each = 10 total)
input double   InpLotSize            = 0.01;        // ▶ Lot Size: Volume per order (0.01 = micro lot for small accounts)
input int      InpTPPips             = 300;         // ▶ Take Profit: Default TP distance in pips (can be dynamic)
input int      InpSLPips             = 300;         // ▶ Stop Loss: Default SL distance in pips (can be dynamic)
input int      InpGridSpacingPips    = 10;          // ▶ Grid Spacing: Distance between each pending order in pips
input int      InpBufferPips         = 5;           // ▶ Buffer From Candle: Extra pips above/below M1 candle high/low

input group "═══════ 2. INSTANT BREAKEVEN ═══════"
input bool     InpInstantBE          = true;        // ▶ INSTANT BE: Move SL to entry at ANY profit (RECOMMENDED)
input bool     InpUseDynamicBE       = true;        // ▶ Dynamic BE: Use ATR-based BE trigger (if Instant BE is OFF)
input double   InpBE_ATR_Multi       = 0.5;         // ▶ ATR Multiplier: BE trigger = ATR × this value
input int      InpBE_ATR_Period      = 14;          // ▶ ATR Period: Bars for ATR calculation
input int      InpBE_MinPips         = 10;          // ▶ BE Min Pips: Minimum pips before BE activates
input int      InpBE_MaxPips         = 100;         // ▶ BE Max Pips: Maximum pips cap for BE trigger
input int      InpBE_LockPips        = 3;           // ▶ Lock Profit: Extra pips to lock above entry (0 = exact entry)

input group "═══════ 3. DYNAMIC TRAILING ═══════"
input bool     InpUseDynTrail        = true;        // ▶ Enable Trailing: Trail SL behind price as profit grows
input double   InpTrail_ATR_Multi    = 1.0;         // ▶ Trail Start: Begin trailing when profit = ATR × this
input double   InpTrailStep_Multi    = 0.3;         // ▶ Trail Step: Move SL by ATR × this value
input int      InpTrail_MinPips      = 20;          // ▶ Min Start Pips: Minimum pips profit before trail starts
input int      InpTrail_MaxPips      = 200;         // ▶ Max Start Pips: Cap for trail start distance

input group "═══════ 4. TP HUNT MODE ═══════"
input bool     InpTPHuntMode         = true;        // ▶ TP Hunt: Very tight trailing when deep in profit
input int      InpTPHuntTrigger      = 200;         // ▶ Hunt Trigger: Activate hunt mode at X pips profit
input double   InpTPHuntTrailMulti   = 0.15;        // ▶ Hunt Trail: Ultra-tight trail = ATR × this (locks max profit)

input group "═══════ 5. AUTO-EDIT PENDINGS ═══════"
input bool     InpAutoEditPendings   = true;        // ▶ Auto-Edit: Relocate pendings when price moves away
input int      InpMaxDistFromPrice   = 100;         // ▶ Max Distance: Relocate when pending is X pips from price
input bool     InpEditOnNewCandle    = true;        // ▶ Edit on Candle: Compound to new M1 candle high/low
input bool     InpFlipOnBiasChange   = true;        // ▶ Flip on Bias: Delete & replace orders when trend flips

input group "═══════ 6. MARKET STRUCTURE ═══════"
input int      InpStructureBars      = 5;           // ▶ Swing Bars: Bars to detect swing high/low (HH/HL/LH/LL)
input int      InpMomentumBars       = 3;           // ▶ Momentum Bars: Candles to analyze for momentum direction
input double   InpMomMinPips         = 3.0;         // ▶ Min Momentum: Minimum pips per candle to confirm momentum

input group "═══════ 7. ACCOUNT PROTECTION ═══════"
input double   InpMaxDDPercent       = 30.0;        // ▶ Max DD %: Stop trading at X% drawdown (0=off)
input double   InpMaxDDDollar        = 0;           // ▶ Max DD $: Stop at X dollar loss (0=off)
input double   InpMinEquity          = 0;           // ▶ Min Equity: Stop if equity falls below X (0=off)
input double   InpMaxMarginPct       = 50.0;        // ▶ Max Margin %: Stop if margin usage exceeds X% (0=off)
input double   InpDailyTarget        = 0;           // ▶ Daily Target $: Close all & stop when profit hits X (0=off)
input double   InpDailyLoss          = 0;           // ▶ Daily Loss $: Close all & stop when loss hits X (0=off)
input string   InpResetTime          = "00:00";     // ▶ Reset Time: Daily reset time for counters (server time)
input bool     InpCloseOnDD          = true;        // ▶ Close on DD: Auto-close all positions when protection triggers

input group "═══════ 8. GENERAL SETTINGS ═══════"
input int      InpRestartDelay       = 3;           // ▶ Restart Delay: Seconds to wait before placing new grid
input int      InpMaxGridsDay        = 0;           // ▶ Max Grids/Day: Limit grids per day (0=unlimited)
input int      InpMagic              = 7777;        // ▶ Magic Number: Unique ID for this EA's orders
input int      InpSlippage           = 50;          // ▶ Slippage: Max allowed slippage in points
input double   InpMaxLot             = 10.0;        // ▶ Max Lot: Maximum lot size cap
input bool     InpDeleteOpposite     = true;        // ▶ Delete Opposite: Remove opposite pendings when one side triggers

input group "═══════ 9. TICK FLOW SCALP ═══════"
input bool     InpTickFlow           = true;        // ▶ Enable Tick-Flow: Open market orders following tick momentum
input int      InpTickConfirm        = 3;           // ▶ Tick Confirm: Consecutive same-direction ticks to fire (2-5)
input int      InpTickFlowTP         = 30;          // ▶ Scalp TP: Short take profit for quick pips
input int      InpTickFlowSL         = 30;          // ▶ Scalp SL: Tight stop loss for protection
input int      InpMaxTickPos         = 2;           // ▶ Max Positions: Maximum tick-flow positions allowed
input int      InpTickCooldown       = 10;          // ▶ Cooldown: Seconds between tick-flow entries

input group "═══════ 10. SPREAD FILTER ═══════"
input bool     InpUseSpreadFilter    = true;        // ▶ Enable Spread Filter: Skip trading when spread too high
input int      InpMaxSpread          = 35;          // ▶ Max Spread Points: Don't trade above this spread
input int      InpSpreadBuffer       = 5;           // ▶ Spread Buffer: Extra points added to BE calculation

input group "═══════ 11. SMART BREAKEVEN ═══════"
input bool     InpSmartBE            = true;        // ▶ Smart BE: Entry + Spread + Buffer (not just entry)
input int      InpSmartBE_Buffer     = 3;           // ▶ BE Buffer Pips: Extra pips above spread for BE lock
input int      InpSmartBE_MinProfit  = 5;           // ▶ Min Profit Pips: Minimum profit before Smart BE activates

input group "═══════ 12. PARTIAL CLOSE ═══════"
input bool     InpUsePartialClose    = true;        // ▶ Enable Partial Close: Close portion at first target
input int      InpPartialTrigger     = 20;          // ▶ Partial Trigger Pips: Close partial at X pips profit
input int      InpPartialPercent     = 50;          // ▶ Partial Percent: Close X% of position (50 = half)
input bool     InpMoveSlAfterPartial = true;        // ▶ Move SL After Partial: Lock profit after partial close

input group "═══════ 13. LOSS COUNTER ═══════"
input bool     InpUseLossCounter     = true;        // ▶ Enable Loss Counter: Track consecutive losses
input int      InpMaxConsecLoss      = 3;           // ▶ Max Consecutive Losses: Stop trading after X losses in a row
input int      InpLossCooldownMin    = 30;          // ▶ Loss Cooldown Minutes: Wait X minutes after max losses
input bool     InpResetOnWin         = true;        // ▶ Reset On Win: Reset loss counter when a trade wins

//+------------------------------------------------------------------+
//| ENUMS                                                            |
//+------------------------------------------------------------------+
enum ENUM_BIAS { BIAS_NONE = 0, BIAS_BULL = 1, BIAS_BEAR = -1 };
enum ENUM_PAT  { PAT_NONE = 0, PAT_BULL_ENG = 1, PAT_BEAR_ENG = 2,
                 PAT_BULL_PIN = 3, PAT_BEAR_PIN = 4,
                 PAT_BULL_MOM = 5, PAT_BEAR_MOM = 6 };

//+------------------------------------------------------------------+
//| GLOBALS                                                          |
//+------------------------------------------------------------------+
CTrade g_trade;

bool      g_gridActive;
datetime  g_gridCloseTime;
int       g_gridsToday;
double    g_dailyPL;
datetime  g_lastReset;
bool      g_dayDone, g_ddHit;
double    g_startBal, g_maxDD, g_peakEq, g_pipSize;

ENUM_BIAS g_structBias, g_momBias, g_candleBias, g_overallBias;
ENUM_PAT  g_pattern;

double    g_lastCandleH, g_lastCandleL;
datetime  g_lastCandleTime, g_lastM1Time;
double    g_prevBid, g_tickDir;

// Dynamic values
double    g_currentATR;
double    g_dynBETrigger;
double    g_dynTrailStart;
double    g_dynTrailStep;
double    g_dynTPHuntTrail;

// Stats
int       g_beCount, g_trailCount, g_editCount, g_flipCount;

// Tick flow
int       g_ticksUp, g_ticksDn;
datetime  g_lastTickEntry;
int       g_tickFlowCount;

// Loss reduction tracking
int       g_consecLosses;         // Consecutive losses counter
int       g_consecWins;           // Consecutive wins counter
datetime  g_lossLockoutUntil;     // Time until trading resumes after max losses
int       g_partialCloseCount;    // How many partial closes today
int       g_totalWins;            // Total wins today
int       g_totalLosses;          // Total losses today
double    g_lastGridPL;           // P/L of last completed grid

//+------------------------------------------------------------------+
//| UTILITIES                                                         |
//+------------------------------------------------------------------+
void SetPipSize()
  { g_pipSize = (_Digits == 5 || _Digits == 3) ? _Point * 10 : _Point; }

void AutoFill()
  { long f = 0; SymbolInfoInteger(_Symbol, SYMBOL_FILLING_MODE, f);
    if((f & SYMBOL_FILLING_FOK) != 0)      g_trade.SetTypeFilling(ORDER_FILLING_FOK);
    else if((f & SYMBOL_FILLING_IOC) != 0)  g_trade.SetTypeFilling(ORDER_FILLING_IOC);
    else                                     g_trade.SetTypeFilling(ORDER_FILLING_RETURN); }

double FixLot(double lot)
  { double mn = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
    double mx = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
    double st = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
    if(st <= 0) st = 0.01;
    lot = MathMax(mn, MathMin(MathMin(mx, InpMaxLot), lot));
    return NormalizeDouble(MathFloor(lot / st) * st, 2); }

double N(double v) { return NormalizeDouble(v, _Digits); }

//+------------------------------------------------------------------+
//| CALCULATE ATR for dynamic values                                  |
//+------------------------------------------------------------------+
void CalcATR()
  {
   double atrBuf[];
   ArraySetAsSeries(atrBuf, true);

   int handle = iATR(_Symbol, PERIOD_M1, InpBE_ATR_Period);
   if(handle == INVALID_HANDLE) { g_currentATR = 50 * g_pipSize; return; }

   if(CopyBuffer(handle, 0, 0, 1, atrBuf) < 1)
     { g_currentATR = 50 * g_pipSize; IndicatorRelease(handle); return; }

   g_currentATR = atrBuf[0];
   IndicatorRelease(handle);

   double atrPips = g_currentATR / g_pipSize;

   // Dynamic BE trigger
   g_dynBETrigger = atrPips * InpBE_ATR_Multi;
   g_dynBETrigger = MathMax(InpBE_MinPips, MathMin(InpBE_MaxPips, g_dynBETrigger));

   // Dynamic trail start
   g_dynTrailStart = atrPips * InpTrail_ATR_Multi;
   g_dynTrailStart = MathMax(InpTrail_MinPips, MathMin(InpTrail_MaxPips, g_dynTrailStart));

   // Dynamic trail step
   g_dynTrailStep = atrPips * InpTrailStep_Multi;
   g_dynTrailStep = MathMax(5, MathMin(100, g_dynTrailStep));

   // TP Hunt trail (very tight)
   g_dynTPHuntTrail = atrPips * InpTPHuntTrailMulti;
   g_dynTPHuntTrail = MathMax(3, MathMin(30, g_dynTPHuntTrail));
  }

//+------------------------------------------------------------------+
//| M1 CANDLE DATA                                                    |
//+------------------------------------------------------------------+
bool GetM1(double &high, double &low, double &range)
  {
   double h[2], l[2];
   if(CopyHigh(_Symbol, PERIOD_M1, 0, 2, h) < 2) return false;
   if(CopyLow(_Symbol, PERIOD_M1, 0, 2, l) < 2) return false;
   high = h[0]; low = l[0]; range = (high - low) / g_pipSize;
   if(range < 3) { high = h[1]; low = l[1]; range = (high - low) / g_pipSize; }
   return (range >= 1);
  }

//+------------------------------------------------------------------+
//| CHECK NEW M1 CANDLE                                               |
//+------------------------------------------------------------------+
bool IsNewM1()
  {
   datetime t = iTime(_Symbol, PERIOD_M1, 0);
   if(t != g_lastM1Time) { g_lastM1Time = t; return true; }
   return false;
  }

//+------------------------------------------------------------------+
//| DYNAMIC TP/SL                                                     |
//+------------------------------------------------------------------+
void DynTPSL(double &tp, double &sl)
  {
   double atrPips = g_currentATR / g_pipSize;
   if(atrPips > 3)
     { tp = MathMax(10, MathMin(InpTPPips * 2.0, atrPips * 3.0));
       sl = MathMax(10, MathMin(InpSLPips * 2.0, atrPips * 3.0)); }
   else { tp = InpTPPips; sl = InpSLPips; }
  }

//+------------------------------------------------------------------+
//| MARKET STRUCTURE                                                  |
//+------------------------------------------------------------------+
ENUM_BIAS GetStructure()
  {
   int need = InpStructureBars * 2 + 1;
   double h[], l[];
   ArraySetAsSeries(h, true); ArraySetAsSeries(l, true);
   if(CopyHigh(_Symbol, PERIOD_M1, 0, need, h) < need) return BIAS_NONE;
   if(CopyLow(_Symbol, PERIOD_M1, 0, need, l) < need) return BIAS_NONE;

   double sH[]; ArrayResize(sH, 0);
   double sL[]; ArrayResize(sL, 0);

   for(int i = 2; i < need - 2; i++)
     { if(h[i] > h[i-1] && h[i] > h[i-2] && h[i] > h[i+1] && h[i] > h[i+2])
        { int sz = ArraySize(sH); ArrayResize(sH, sz+1); sH[sz] = h[i]; }
       if(l[i] < l[i-1] && l[i] < l[i-2] && l[i] < l[i+1] && l[i] < l[i+2])
        { int sz = ArraySize(sL); ArrayResize(sL, sz+1); sL[sz] = l[i]; } }

   if(ArraySize(sH) < 2 || ArraySize(sL) < 2) return BIAS_NONE;

   bool hh = sH[0] > sH[1], hl = sL[0] > sL[1];
   bool lh = sH[0] < sH[1], ll = sL[0] < sL[1];

   if(hh && hl) return BIAS_BULL;
   if(lh && ll) return BIAS_BEAR;
   if(hh || hl) return BIAS_BULL;
   if(lh || ll) return BIAS_BEAR;
   return BIAS_NONE;
  }

//+------------------------------------------------------------------+
//| MOMENTUM                                                          |
//+------------------------------------------------------------------+
ENUM_BIAS GetMomentum()
  {
   double o[], c[];
   ArraySetAsSeries(o, true); ArraySetAsSeries(c, true);
   if(CopyOpen(_Symbol, PERIOD_M1, 1, InpMomentumBars, o) < InpMomentumBars) return BIAS_NONE;
   if(CopyClose(_Symbol, PERIOD_M1, 1, InpMomentumBars, c) < InpMomentumBars) return BIAS_NONE;

   int bull = 0, bear = 0; double bM = 0, sM = 0;
   for(int i = 0; i < InpMomentumBars; i++)
     { double b = (c[i] - o[i]) / g_pipSize;
       if(b > 0) { bull++; bM += b; } else if(b < 0) { bear++; sM += MathAbs(b); } }

   if(bull >= 2 && bM > sM * 1.3) return BIAS_BULL;
   if(bear >= 2 && sM > bM * 1.3) return BIAS_BEAR;
   return BIAS_NONE;
  }

//+------------------------------------------------------------------+
//| LIVE CANDLE BIAS                                                  |
//+------------------------------------------------------------------+
ENUM_BIAS GetCandleBias()
  {
   double o[];
   ArraySetAsSeries(o, true);
   if(CopyOpen(_Symbol, PERIOD_M1, 0, 1, o) < 1) return BIAS_NONE;
   double body = (SymbolInfoDouble(_Symbol, SYMBOL_BID) - o[0]) / g_pipSize;
   if(body > 1.5) return BIAS_BULL;
   if(body < -1.5) return BIAS_BEAR;
   return BIAS_NONE;
  }

//+------------------------------------------------------------------+
//| PRICE ACTION PATTERNS                                             |
//+------------------------------------------------------------------+
ENUM_PAT GetPattern()
  {
   double o[], c[], h[], l[];
   ArraySetAsSeries(o, true); ArraySetAsSeries(c, true);
   ArraySetAsSeries(h, true); ArraySetAsSeries(l, true);
   if(CopyOpen(_Symbol, PERIOD_M1, 1, 3, o) < 3) return PAT_NONE;
   if(CopyClose(_Symbol, PERIOD_M1, 1, 3, c) < 3) return PAT_NONE;
   if(CopyHigh(_Symbol, PERIOD_M1, 1, 3, h) < 3) return PAT_NONE;
   if(CopyLow(_Symbol, PERIOD_M1, 1, 3, l) < 3) return PAT_NONE;

   double b0 = c[0]-o[0], b1 = c[1]-o[1], r0 = h[0]-l[0];
   double uw = h[0]-MathMax(o[0],c[0]), lw = MathMin(o[0],c[0])-l[0];

   if(b1 < 0 && b0 > 0 && MathAbs(b0) > MathAbs(b1)*1.2 && c[0] > o[1]) return PAT_BULL_ENG;
   if(b1 > 0 && b0 < 0 && MathAbs(b0) > MathAbs(b1)*1.2 && c[0] < o[1]) return PAT_BEAR_ENG;
   if(r0 > 0 && lw > r0*0.6 && MathAbs(b0) < r0*0.3) return PAT_BULL_PIN;
   if(r0 > 0 && uw > r0*0.6 && MathAbs(b0) < r0*0.3) return PAT_BEAR_PIN;
   if(b0 > 0 && b0/g_pipSize > InpMomMinPips*2 && b0 > r0*0.7) return PAT_BULL_MOM;
   if(b0 < 0 && MathAbs(b0)/g_pipSize > InpMomMinPips*2 && MathAbs(b0) > r0*0.7) return PAT_BEAR_MOM;
   return PAT_NONE;
  }

//+------------------------------------------------------------------+
//| OVERALL BIAS SCORE                                                |
//+------------------------------------------------------------------+
ENUM_BIAS CalcBias()
  {
   int bull = 0, bear = 0;
   g_structBias = GetStructure();
   g_momBias = GetMomentum();
   g_candleBias = GetCandleBias();
   g_pattern = GetPattern();

   if(g_structBias == BIAS_BULL) bull += 3; if(g_structBias == BIAS_BEAR) bear += 3;
   if(g_momBias == BIAS_BULL) bull += 2;    if(g_momBias == BIAS_BEAR) bear += 2;
   if(g_candleBias == BIAS_BULL) bull += 2; if(g_candleBias == BIAS_BEAR) bear += 2;

   if(g_pattern == PAT_BULL_ENG || g_pattern == PAT_BULL_PIN || g_pattern == PAT_BULL_MOM) bull += 2;
   if(g_pattern == PAT_BEAR_ENG || g_pattern == PAT_BEAR_PIN || g_pattern == PAT_BEAR_MOM) bear += 2;

   if(g_tickDir > 0) bull += 1; if(g_tickDir < 0) bear += 1;

   if(bull >= 3 && bull > bear) return BIAS_BULL;
   if(bear >= 3 && bear > bull) return BIAS_BEAR;
   return BIAS_NONE;
  }

//+------------------------------------------------------------------+
//| SPREAD FILTER: Check if spread is acceptable                      |
//+------------------------------------------------------------------+
bool SpreadOK()
  {
   if(!InpUseSpreadFilter) return true;
   int spread = (int)SymbolInfoInteger(_Symbol, SYMBOL_SPREAD);
   if(spread > InpMaxSpread)
     {
      static datetime lastWarn = 0;
      if(TimeCurrent() - lastWarn > 60) // Warn once per minute
        {
         Print("⚠ SPREAD FILTER: Spread ", spread, " > Max ", InpMaxSpread, " - WAITING");
         lastWarn = TimeCurrent();
        }
      return false;
     }
   return true;
  }

//+------------------------------------------------------------------+
//| LOSS LOCKOUT: Check if we're in cooldown after max losses         |
//+------------------------------------------------------------------+
bool LossLockoutOK()
  {
   if(!InpUseLossCounter) return true;
   if(g_lossLockoutUntil > 0 && TimeCurrent() < g_lossLockoutUntil)
     {
      static datetime lastWarn = 0;
      if(TimeCurrent() - lastWarn > 60)
        {
         int minLeft = (int)((g_lossLockoutUntil - TimeCurrent()) / 60);
         Print("⚠ LOSS LOCKOUT: ", g_consecLosses, " consecutive losses - Wait ", minLeft, " min");
         lastWarn = TimeCurrent();
        }
      return false;
     }
   // Reset lockout if time passed
   if(g_lossLockoutUntil > 0 && TimeCurrent() >= g_lossLockoutUntil)
     {
      g_lossLockoutUntil = 0;
      g_consecLosses = 0;
      Print("✓ LOSS LOCKOUT ENDED - Trading resumed");
     }
   return true;
  }

//+------------------------------------------------------------------+
//| UPDATE LOSS COUNTER: Called when a grid completes                 |
//+------------------------------------------------------------------+
void UpdateLossCounter(double gridPL)
  {
   if(!InpUseLossCounter) return;

   g_lastGridPL = gridPL;

   if(gridPL < 0) // Loss
     {
      g_consecLosses++;
      g_consecWins = 0;
      g_totalLosses++;
      Print("✗ GRID LOSS #", g_consecLosses, " | P/L: $", DoubleToString(gridPL, 2));

      if(g_consecLosses >= InpMaxConsecLoss)
        {
         g_lossLockoutUntil = TimeCurrent() + (InpLossCooldownMin * 60);
         Print("⛔ MAX LOSSES REACHED (", g_consecLosses, ") - LOCKOUT ", InpLossCooldownMin, " minutes");
        }
     }
   else if(gridPL > 0) // Win
     {
      if(InpResetOnWin) g_consecLosses = 0;
      g_consecWins++;
      g_totalWins++;
      Print("✓ GRID WIN #", g_consecWins, " | P/L: $", DoubleToString(gridPL, 2));
     }
  }

//+------------------------------------------------------------------+
//| PLACE ORDER WITH RETRY                                            |
//+------------------------------------------------------------------+
bool PlaceOrd(bool isBuy, double entry, double sl, double tp, double lot, string tag)
  {
   entry = N(entry); sl = N(sl); tp = N(tp);
   for(int a = 0; a < 5; a++)
     {
      bool ok = isBuy ?
         g_trade.BuyStop(lot, entry, _Symbol, sl, tp, ORDER_TIME_GTC, 0, tag) :
         g_trade.SellStop(lot, entry, _Symbol, sl, tp, ORDER_TIME_GTC, 0, tag);
      if(ok)
        { uint rc = g_trade.ResultRetcode();
          if(rc == 10009 || rc == 10008) return true;
          if(rc == 10016 || rc == 10015)
            { double adj = 5*_Point;
              if(isBuy) { entry += adj; tp += adj; sl += adj; }
              else      { entry -= adj; tp -= adj; sl -= adj; }
              entry = N(entry); sl = N(sl); tp = N(tp); }
          else if(rc == 10030)
            { ENUM_ORDER_TYPE_FILLING m[] = {ORDER_FILLING_FOK, ORDER_FILLING_IOC, ORDER_FILLING_RETURN};
              g_trade.SetTypeFilling(m[a%3]); } }
      Sleep(30);
     }
   return false;
  }

//+------------------------------------------------------------------+
//| PLACE SMART GRID                                                  |
//| BuyStop placed at candle Close/Open (whichever is pick high)      |
//| SellStop placed at candle Close/Open (whichever is pick low)      |
//+------------------------------------------------------------------+
bool PlaceGrid()
  {
   // Get recent candle OHLC data
   double o[], c[], h[], l[];
   ArraySetAsSeries(o, true); ArraySetAsSeries(c, true);
   ArraySetAsSeries(h, true); ArraySetAsSeries(l, true);

   if(CopyOpen(_Symbol, PERIOD_M1, 1, 2, o) < 2) return false;
   if(CopyClose(_Symbol, PERIOD_M1, 1, 2, c) < 2) return false;
   if(CopyHigh(_Symbol, PERIOD_M1, 1, 2, h) < 2) return false;
   if(CopyLow(_Symbol, PERIOD_M1, 1, 2, l) < 2) return false;

   // Recent closed candle (index 0 = most recent closed)
   double candleOpen  = o[0];
   double candleClose = c[0];
   double candleHigh  = h[0];
   double candleLow   = l[0];

   // Pick point = the higher of Open/Close for BuyStop entry
   // Pick point = the lower of Open/Close for SellStop entry
   double pickHigh = MathMax(candleOpen, candleClose);  // Top of candle body
   double pickLow  = MathMin(candleOpen, candleClose);  // Bottom of candle body

   double cR = (candleHigh - candleLow) / g_pipSize;
   if(cR < 3) { // If candle too small, use previous candle
      pickHigh = MathMax(o[1], c[1]);
      pickLow  = MathMin(o[1], c[1]);
      cR = (h[1] - l[1]) / g_pipSize;
   }

   double lot = FixLot(InpLotSize);
   if(lot <= 0) return false;

   CalcATR();
   double tpP, slP; DynTPSL(tpP, slP);
   double tpD = tpP * g_pipSize, slD = slP * g_pipSize;
   double spc = InpGridSpacingPips * g_pipSize;
   double buf = InpBufferPips * g_pipSize;

   long stopLvl = SymbolInfoInteger(_Symbol, SYMBOL_TRADE_STOPS_LEVEL);
   double minD = MathMax(stopLvl * _Point, 10 * _Point);

   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);

   ENUM_BIAS bias = CalcBias();
   g_overallBias = bias;

   int placed = 0;

   Print("═══ GRID | ", (bias == BIAS_BULL ? "▲ BULL" : (bias == BIAS_BEAR ? "▼ BEAR" : "═ NEUTRAL")),
         " | ATR: ", DoubleToString(g_currentATR/g_pipSize, 1), "pip",
         " | Pick High: ", DoubleToString(pickHigh, _Digits),
         " | Pick Low: ", DoubleToString(pickLow, _Digits), " ═══");

   // === BuyStops placed at PICK HIGH (top of candle body) + buffer ===
   if(bias == BIAS_BULL || bias == BIAS_NONE)
     {
      double base = MathMax(pickHigh + buf, ask + minD + _Point);
      for(int i = 0; i < InpTradesPerSide; i++)
        {
         double e = base + (i * spc);
         if(PlaceOrd(true, e, e - slD, e + tpD, lot, "BS_" + IntegerToString(i+1)))
           { placed++; Print("  BS#", i+1, " @ ", DoubleToString(N(e), _Digits)); }
        }
     }

   // === SellStops placed at PICK LOW (bottom of candle body) - buffer ===
   if(bias == BIAS_BEAR || bias == BIAS_NONE)
     {
      double base = MathMin(pickLow - buf, bid - minD - _Point);
      for(int i = 0; i < InpTradesPerSide; i++)
        {
         double e = base - (i * spc);
         if(PlaceOrd(false, e, e + slD, e - tpD, lot, "SS_" + IntegerToString(i+1)))
           { placed++; Print("  SS#", i+1, " @ ", DoubleToString(N(e), _Digits)); }
        }
     }

   g_lastCandleH = pickHigh; g_lastCandleL = pickLow;
   g_lastCandleTime = iTime(_Symbol, PERIOD_M1, 0);

   if(placed > 0) { g_gridActive = true; g_gridsToday++; return true; }
   return false;
  }

//+------------------------------------------------------------------+
//| AUTO-EDIT PENDINGS: Relocate if too far from price                |
//+------------------------------------------------------------------+
void AutoEditPendings()
  {
   if(!InpAutoEditPendings) return;

   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   double mid = (ask + bid) / 2.0;
   double maxDist = InpMaxDistFromPrice * g_pipSize;

   bool needRebuild = false;

   // Check if any pending is too far
   for(int i = OrdersTotal() - 1; i >= 0; i--)
     {
      ulong tk = OrderGetTicket(i);
      if(tk == 0) continue;
      if(OrderGetString(ORDER_SYMBOL) != _Symbol) continue;
      if(OrderGetInteger(ORDER_MAGIC) != InpMagic) continue;

      double entry = OrderGetDouble(ORDER_PRICE_OPEN);
      double dist  = MathAbs(entry - mid);

      if(dist > maxDist)
        { needRebuild = true; break; }
     }

   if(needRebuild)
     {
      Print(">>> PENDINGS TOO FAR (>", InpMaxDistFromPrice, " pips) → RELOCATING <<<");
      g_editCount++;
      DeleteAll();
      g_gridActive = false;
      PlaceGrid();
     }
  }

//+------------------------------------------------------------------+
//| NEW CANDLE RELOCATION                                             |
//+------------------------------------------------------------------+
void NewCandleRelocate()
  {
   if(!InpEditOnNewCandle) return;
   if(CntPos() > 0) return; // Don't edit while positions open

   double cH, cL, cR;
   if(!GetM1(cH, cL, cR)) return;

   double hMove = MathAbs(cH - g_lastCandleH) / g_pipSize;
   double lMove = MathAbs(cL - g_lastCandleL) / g_pipSize;

   // Only relocate if candle moved significantly
   if(hMove < 5 && lMove < 5) return;

   // Check bias flip
   ENUM_BIAS newBias = CalcBias();
   bool flipped = (g_overallBias == BIAS_BULL && newBias == BIAS_BEAR) ||
                  (g_overallBias == BIAS_BEAR && newBias == BIAS_BULL);

   if(flipped && InpFlipOnBiasChange)
     {
      Print(">>> BIAS FLIP: ",
            (g_overallBias == BIAS_BULL ? "BULL→BEAR" : "BEAR→BULL"),
            " → REBUILDING <<<");
      g_flipCount++;
      DeleteAll();
      g_gridActive = false;
      g_overallBias = newBias;
      PlaceGrid();
      return;
     }

   // Compound to new candle
   if(hMove >= 10 || lMove >= 10)
     {
      Print(">>> NEW M1 CANDLE COMPOUND | H=", DoubleToString(cH, _Digits),
            " L=", DoubleToString(cL, _Digits), " <<<");
      g_editCount++;
      DeleteAll();
      g_gridActive = false;
      PlaceGrid();
     }
  }

//+------------------------------------------------------------------+
//| PARTIAL CLOSE: Close portion of position at target                |
//+------------------------------------------------------------------+
bool DoPartialClose(ulong ticket, double currentLot, int partialPct)
  {
   double closeLot = NormalizeDouble(currentLot * partialPct / 100.0, 2);
   double minLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double stepLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);

   if(closeLot < minLot) closeLot = minLot;
   closeLot = NormalizeDouble(MathFloor(closeLot / stepLot) * stepLot, 2);

   if(closeLot >= currentLot) return false; // Don't close entire position

   if(g_trade.PositionClosePartial(ticket, closeLot))
     {
      g_partialCloseCount++;
      Print("💰 PARTIAL CLOSE: ", DoubleToString(closeLot, 2), " lots (",
            partialPct, "%) | Remaining: ", DoubleToString(currentLot - closeLot, 2));
      return true;
     }
   return false;
  }

//+------------------------------------------------------------------+
//| MANAGE POSITIONS: Smart BE + Partial Close + Trailing + Hunt      |
//+------------------------------------------------------------------+
void ManagePositions()
  {
   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   int spread = (int)SymbolInfoInteger(_Symbol, SYMBOL_SPREAD);
   double spreadPips = spread * _Point / g_pipSize;

   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong tk = PositionGetTicket(i);
      if(tk == 0) continue;
      if(PositionGetString(POSITION_SYMBOL) != _Symbol) continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic) continue;

      double op    = PositionGetDouble(POSITION_PRICE_OPEN);
      double cSL   = PositionGetDouble(POSITION_SL);
      double cTP   = PositionGetDouble(POSITION_TP);
      double cLot  = PositionGetDouble(POSITION_VOLUME);
      long   type  = PositionGetInteger(POSITION_TYPE);
      string cmt   = PositionGetString(POSITION_COMMENT);

      double pips = (type == POSITION_TYPE_BUY) ?
                    (bid - op) / g_pipSize : (op - ask) / g_pipSize;

      // ═══════════════════════════════════════════════════════════════════
      // === 1. PARTIAL CLOSE: Close 50% at first target, let rest run ═════
      // ═══════════════════════════════════════════════════════════════════
      if(InpUsePartialClose && pips >= InpPartialTrigger)
        {
         // Check if not already partially closed (by checking lot size or comment)
         bool alreadyPartial = (StringFind(cmt, "_P") >= 0);

         if(!alreadyPartial && cLot > SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN))
           {
            if(DoPartialClose(tk, cLot, InpPartialPercent))
              {
               // Move SL to lock profit after partial
               if(InpMoveSlAfterPartial)
                 {
                  double lockSL = 0;
                  if(type == POSITION_TYPE_BUY)
                     lockSL = N(op + (InpPartialTrigger / 2) * g_pipSize);
                  else
                     lockSL = N(op - (InpPartialTrigger / 2) * g_pipSize);

                  // Reload position data after partial close
                  if(PositionSelectByTicket(tk))
                    {
                     cSL = PositionGetDouble(POSITION_SL);
                     cTP = PositionGetDouble(POSITION_TP);
                     if((type == POSITION_TYPE_BUY && lockSL > cSL) ||
                        (type == POSITION_TYPE_SELL && (lockSL < cSL || cSL == 0)))
                       {
                        g_trade.PositionModify(tk, lockSL, cTP);
                        Print("🔒 SL LOCKED after partial @ ", DoubleToString(lockSL, _Digits));
                       }
                    }
                 }
               continue; // Move to next position
              }
           }
        }

      // ═══════════════════════════════════════════════════════════════════
      // === 2. SMART BE: Entry + Spread + Buffer (covers spread cost) ═════
      // ═══════════════════════════════════════════════════════════════════
      if(InpSmartBE && pips >= InpSmartBE_MinProfit)
        {
         // Smart BE = Entry + Spread + Buffer
         double beBuffer = (spreadPips + InpSmartBE_Buffer) * g_pipSize;

         if(type == POSITION_TYPE_BUY)
           {
            double smartBE = N(op + beBuffer);
            if(cSL < smartBE)
              {
               if(g_trade.PositionModify(tk, smartBE, cTP))
                 {
                  g_beCount++;
                  Print("🛡 SMART BE BUY → SL=", DoubleToString(smartBE, _Digits),
                        " (Entry+", DoubleToString(spreadPips + InpSmartBE_Buffer, 1), "pip)");
                 }
              }
           }
         else // SELL
           {
            double smartBE = N(op - beBuffer);
            if(cSL > smartBE || cSL == 0)
              {
               if(g_trade.PositionModify(tk, smartBE, cTP))
                 {
                  g_beCount++;
                  Print("🛡 SMART BE SELL → SL=", DoubleToString(smartBE, _Digits),
                        " (Entry-", DoubleToString(spreadPips + InpSmartBE_Buffer, 1), "pip)");
                 }
              }
           }
        }
      // === FALLBACK: Instant BE if Smart BE is disabled ===
      else if(InpInstantBE && !InpSmartBE && pips > 0.5)
        {
         if(type == POSITION_TYPE_BUY)
           {
            double beSL = N(op + 1 * _Point);
            if(cSL < beSL)
              {
               if(g_trade.PositionModify(tk, beSL, cTP))
                 { g_beCount++; Print("⚡ INSTANT BE BUY → SL=", DoubleToString(beSL, _Digits)); }
              }
           }
         else
           {
            double beSL = N(op - 1 * _Point);
            if(cSL > beSL || cSL == 0)
              {
               if(g_trade.PositionModify(tk, beSL, cTP))
                 { g_beCount++; Print("⚡ INSTANT BE SELL → SL=", DoubleToString(beSL, _Digits)); }
              }
           }
        }

      // ═══════════════════════════════════════════════════════════════════
      // === 3. TP HUNT MODE (very tight trail when deep in profit) ════════
      // ═══════════════════════════════════════════════════════════════════
      if(InpTPHuntMode && pips >= InpTPHuntTrigger)
        {
         double huntTrail = g_dynTPHuntTrail * g_pipSize;
         if(type == POSITION_TYPE_BUY)
           {
            double newSL = N(bid - huntTrail);
            if(newSL > cSL && newSL > op)
              { g_trade.PositionModify(tk, newSL, cTP); continue; }
           }
         else
           {
            double newSL = N(ask + huntTrail);
            if((newSL < cSL || cSL == 0) && newSL < op)
              { g_trade.PositionModify(tk, newSL, cTP); continue; }
           }
        }

      // ═══════════════════════════════════════════════════════════════════
      // === 4. DYNAMIC TRAILING ═══════════════════════════════════════════
      // ═══════════════════════════════════════════════════════════════════
      if(InpUseDynTrail && pips >= g_dynTrailStart)
        {
         double step = g_dynTrailStep * g_pipSize;
         if(type == POSITION_TYPE_BUY)
           {
            double newSL = N(bid - step);
            if(newSL > cSL && newSL > op)
              { g_trade.PositionModify(tk, newSL, cTP); g_trailCount++; continue; }
           }
         else
           {
            double newSL = N(ask + step);
            if((newSL < cSL || cSL == 0) && newSL < op)
              { g_trade.PositionModify(tk, newSL, cTP); g_trailCount++; continue; }
           }
        }

      // === DYNAMIC BREAKEVEN ===
      if(InpUseDynamicBE && pips >= g_dynBETrigger)
        {
         double lock = InpBE_LockPips * g_pipSize;
         if(type == POSITION_TYPE_BUY)
           {
            double beSL = N(op + lock);
            if(cSL < beSL)
              { g_trade.PositionModify(tk, beSL, cTP); g_beCount++; }
           }
         else
           {
            double beSL = N(op - lock);
            if(cSL > beSL || cSL == 0)
              { g_trade.PositionModify(tk, beSL, cTP); g_beCount++; }
           }
        }
     }
  }

//+------------------------------------------------------------------+
//| CLEAN OPPOSITE PENDINGS                                           |
//+------------------------------------------------------------------+
void CleanOpposite()
  {
   if(!InpDeleteOpposite) return;
   bool hB = false, hS = false;
   for(int i = PositionsTotal()-1; i >= 0; i--)
     { ulong tk = PositionGetTicket(i); if(tk == 0) continue;
       if(PositionGetString(POSITION_SYMBOL) != _Symbol || PositionGetInteger(POSITION_MAGIC) != InpMagic) continue;
       if(PositionGetInteger(POSITION_TYPE) == POSITION_TYPE_BUY) hB = true; else hS = true; }
   if(hB && !hS) DelType(ORDER_TYPE_SELL_STOP);
   if(hS && !hB) DelType(ORDER_TYPE_BUY_STOP);
  }

void DelType(long t)
  { for(int i = OrdersTotal()-1; i >= 0; i--)
     { ulong tk = OrderGetTicket(i); if(tk == 0) continue;
       if(OrderGetString(ORDER_SYMBOL) == _Symbol && OrderGetInteger(ORDER_MAGIC) == InpMagic && OrderGetInteger(ORDER_TYPE) == t)
          g_trade.OrderDelete(tk); } }

//+------------------------------------------------------------------+
//| HELPERS                                                           |
//+------------------------------------------------------------------+
void CloseAll()
  { for(int r = 0; r < 10; r++) { bool ok = true;
     for(int i = PositionsTotal()-1; i >= 0; i--) { ulong tk = PositionGetTicket(i); if(tk == 0) continue;
       if(PositionGetString(POSITION_SYMBOL) == _Symbol && PositionGetInteger(POSITION_MAGIC) == InpMagic)
         if(!g_trade.PositionClose(tk, InpSlippage)) ok = false; }
     if(ok) break; Sleep(50); } }

void DeleteAll()
  { for(int r = 0; r < 10; r++) { bool ok = true;
     for(int i = OrdersTotal()-1; i >= 0; i--) { ulong tk = OrderGetTicket(i); if(tk == 0) continue;
       if(OrderGetString(ORDER_SYMBOL) == _Symbol && OrderGetInteger(ORDER_MAGIC) == InpMagic)
         if(!g_trade.OrderDelete(tk)) ok = false; }
     if(ok) break; Sleep(50); } }

int CntPend()
  { int c = 0; for(int i = OrdersTotal()-1; i >= 0; i--)
     { ulong tk = OrderGetTicket(i); if(tk == 0) continue;
       if(OrderGetString(ORDER_SYMBOL) == _Symbol && OrderGetInteger(ORDER_MAGIC) == InpMagic) c++; } return c; }

int CntPos()
  { int c = 0; for(int i = PositionsTotal()-1; i >= 0; i--)
     { ulong tk = PositionGetTicket(i); if(tk == 0) continue;
       if(PositionGetString(POSITION_SYMBOL) == _Symbol && PositionGetInteger(POSITION_MAGIC) == InpMagic) c++; } return c; }

double FloatPL()
  { double p = 0; for(int i = PositionsTotal()-1; i >= 0; i--)
     { ulong tk = PositionGetTicket(i); if(tk == 0) continue;
       if(PositionGetString(POSITION_SYMBOL) != _Symbol || PositionGetInteger(POSITION_MAGIC) != InpMagic) continue;
       p += PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP); } return p; }

bool AccSafe()
  { double eq = AccountInfoDouble(ACCOUNT_EQUITY), mg = AccountInfoDouble(ACCOUNT_MARGIN);
    if(InpMaxDDPercent > 0 && g_startBal > 0 && ((g_startBal-eq)/g_startBal)*100 >= InpMaxDDPercent) return false;
    if(InpMaxDDDollar > 0 && (g_startBal-eq) >= InpMaxDDDollar) return false;
    if(InpMinEquity > 0 && eq < InpMinEquity) return false;
    if(InpMaxMarginPct > 0 && eq > 0 && (mg/eq)*100 >= InpMaxMarginPct) return false;
    return true; }

void UpdateDPL()
  { datetime ds = StringToTime(TimeToString(TimeCurrent(), TIME_DATE));
    if(!HistorySelect(ds, TimeCurrent())) return; double pl = 0;
    for(int i = HistoryDealsTotal()-1; i >= 0; i--)
      { ulong d = HistoryDealGetTicket(i); if(d == 0) continue;
        if(HistoryDealGetString(d, DEAL_SYMBOL) != _Symbol || HistoryDealGetInteger(d, DEAL_MAGIC) != InpMagic) continue;
        if(HistoryDealGetInteger(d, DEAL_ENTRY) != DEAL_ENTRY_OUT) continue;
        pl += HistoryDealGetDouble(d, DEAL_PROFIT)+HistoryDealGetDouble(d, DEAL_SWAP)+HistoryDealGetDouble(d, DEAL_COMMISSION); }
    g_dailyPL = pl; }

//+------------------------------------------------------------------+
//| COUNT TICK-FLOW POSITIONS                                         |
//+------------------------------------------------------------------+
int CntTickPos()
  {
   int c = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong tk = PositionGetTicket(i);
      if(tk == 0) continue;
      if(PositionGetString(POSITION_SYMBOL) != _Symbol) continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagic) continue;
      string cmt = PositionGetString(POSITION_COMMENT);
      if(StringFind(cmt, "TF_") >= 0) c++;
     }
   return c;
  }

//+------------------------------------------------------------------+
//| TICK FLOW: Open market order following tick momentum               |
//|                                                                   |
//| Simple logic:                                                     |
//| - Count consecutive ticks in same direction                       |
//| - When confirmed (default 3) + bias agrees → market order        |
//| - Short TP to grab quick pips, done                               |
//| - All V5 management (dyn BE, trail, hunt) applies to these too   |
//+------------------------------------------------------------------+
void TickFlow()
  {
   if(!InpTickFlow) return;

   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   if(g_prevBid <= 0 || bid <= 0) return;

   // Track consecutive ticks
   if(bid > g_prevBid)      { g_ticksUp++; g_ticksDn = 0; }
   else if(bid < g_prevBid) { g_ticksDn++; g_ticksUp = 0; }

   // Not enough ticks yet
   int need = MathMax(2, InpTickConfirm);
   if(g_ticksUp < need && g_ticksDn < need) return;

   // Check limits
   if(CntTickPos() >= InpMaxTickPos) return;
   if((TimeCurrent() - g_lastTickEntry) < InpTickCooldown) return;

   // Bias must agree (uses V5's existing CalcBias)
   ENUM_BIAS bias = CalcBias();

   double lot = FixLot(InpLotSize);
   if(lot <= 0) return;

   // Dynamic TP/SL from ATR or fixed
   double tpPips = InpTickFlowTP;
   double slPips = InpTickFlowSL;
   if(g_currentATR > 0)
     {
      double atrP = g_currentATR / g_pipSize;
      if(atrP > 3)
        {
         double dynTP = atrP * 0.5;
         double dynSL = atrP * 0.5;
         tpPips = MathMax(5, MathMin(InpTickFlowTP, dynTP));
         slPips = MathMax(5, MathMin(InpTickFlowSL, dynSL));
        }
     }

   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);

   // === TICK FLOW BUY ===
   if(g_ticksUp >= need && (bias == BIAS_BULL || bias == BIAS_NONE))
     {
      double tp = N(ask + tpPips * g_pipSize);
      double sl = N(ask - slPips * g_pipSize);

      for(int a = 0; a < 3; a++)
        {
         if(g_trade.Buy(lot, _Symbol, 0, sl, tp, "TF_B_" + IntegerToString(g_ticksUp)))
           {
            uint rc = g_trade.ResultRetcode();
            if(rc == 10009 || rc == 10008)
              {
               Print("→ TICK FLOW BUY x", g_ticksUp, " ticks @ ", DoubleToString(ask, _Digits),
                     " TP=", DoubleToString(tpPips, 0), " SL=", DoubleToString(slPips, 0));
               g_lastTickEntry = TimeCurrent();
               g_tickFlowCount++;
               g_ticksUp = 0;
               return;
              }
           }
         ENUM_ORDER_TYPE_FILLING m[] = {ORDER_FILLING_FOK, ORDER_FILLING_IOC, ORDER_FILLING_RETURN};
         g_trade.SetTypeFilling(m[a % 3]);
         Sleep(20);
        }
      g_ticksUp = 0; // reset even if failed
     }

   // === TICK FLOW SELL ===
   if(g_ticksDn >= need && (bias == BIAS_BEAR || bias == BIAS_NONE))
     {
      double tp = N(bid - tpPips * g_pipSize);
      double sl = N(bid + slPips * g_pipSize);

      for(int a = 0; a < 3; a++)
        {
         if(g_trade.Sell(lot, _Symbol, 0, sl, tp, "TF_S_" + IntegerToString(g_ticksDn)))
           {
            uint rc = g_trade.ResultRetcode();
            if(rc == 10009 || rc == 10008)
              {
               Print("→ TICK FLOW SELL x", g_ticksDn, " ticks @ ", DoubleToString(bid, _Digits),
                     " TP=", DoubleToString(tpPips, 0), " SL=", DoubleToString(slPips, 0));
               g_lastTickEntry = TimeCurrent();
               g_tickFlowCount++;
               g_ticksDn = 0;
               return;
              }
           }
         ENUM_ORDER_TYPE_FILLING m[] = {ORDER_FILLING_FOK, ORDER_FILLING_IOC, ORDER_FILLING_RETURN};
         g_trade.SetTypeFilling(m[a % 3]);
         Sleep(20);
        }
      g_ticksDn = 0;
     }
  }

void DReset()
  { MqlDateTime dt; TimeToStruct(TimeCurrent(), dt);
    datetime td = StringToTime(IntegerToString(dt.year)+"."+IntegerToString(dt.mon)+"."+IntegerToString(dt.day)+" "+InpResetTime);
    if(TimeCurrent() >= td && g_lastReset < td)
      { g_dailyPL = 0; g_gridsToday = 0; g_dayDone = false; g_ddHit = false;
        g_startBal = AccountInfoDouble(ACCOUNT_BALANCE); g_lastReset = td;
        g_beCount = 0; g_trailCount = 0; g_editCount = 0; g_flipCount = 0;
        g_tickFlowCount = 0;
        g_consecLosses = 0; g_consecWins = 0; g_lossLockoutUntil = 0;
        g_partialCloseCount = 0; g_totalWins = 0; g_totalLosses = 0; g_lastGridPL = 0; } }

//+------------------------------------------------------------------+
//| OnInit                                                            |
//+------------------------------------------------------------------+
int OnInit()
  {
   g_trade.SetExpertMagicNumber(InpMagic);
   g_trade.SetDeviationInPoints(InpSlippage);
   g_trade.SetAsyncMode(false);
   AutoFill(); SetPipSize();

   g_gridActive = false; g_gridCloseTime = 0; g_gridsToday = 0;
   g_dailyPL = 0; g_lastReset = 0; g_dayDone = false; g_ddHit = false;
   g_startBal = AccountInfoDouble(ACCOUNT_BALANCE);
   g_peakEq = AccountInfoDouble(ACCOUNT_EQUITY); g_maxDD = 0;
   g_prevBid = 0; g_tickDir = 0;
   g_lastCandleH = 0; g_lastCandleL = 0; g_lastCandleTime = 0; g_lastM1Time = 0;
   g_overallBias = BIAS_NONE; g_currentATR = 50 * g_pipSize;
   g_beCount = 0; g_trailCount = 0; g_editCount = 0; g_flipCount = 0;
   g_ticksUp = 0; g_ticksDn = 0; g_lastTickEntry = 0; g_tickFlowCount = 0;
   g_consecLosses = 0; g_consecWins = 0; g_lossLockoutUntil = 0;
   g_partialCloseCount = 0; g_totalWins = 0; g_totalLosses = 0; g_lastGridPL = 0;

   if(!TerminalInfoInteger(TERMINAL_TRADE_ALLOWED) || !MQLInfoInteger(MQL_TRADE_ALLOWED))
     { Alert("ENABLE ALGO TRADING!"); return INIT_FAILED; }

   Print("═══════════════════════════════════════════════════════════");
   Print("   STOPHUNTER PRO V5.4 - ADVANCED LOSS REDUCTION ALGO");
   Print("═══════════════════════════════════════════════════════════");

   if(CntPend() > 0 || CntPos() > 0) g_gridActive = true;
   EventSetMillisecondTimer(500);
   return INIT_SUCCEEDED;
  }

void OnDeinit(const int reason) { EventKillTimer(); DeletePanel(); Comment(""); }
void OnTick()  { Engine(); }
void OnTimer() { Engine(); }

//+------------------------------------------------------------------+
//| CORE ENGINE                                                       |
//+------------------------------------------------------------------+
void Engine()
  {
   DReset();

   double eq = AccountInfoDouble(ACCOUNT_EQUITY);
   if(eq > g_peakEq) g_peakEq = eq;
   double dd = g_peakEq - eq; if(dd > g_maxDD) g_maxDD = dd;

   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   if(g_prevBid > 0) { if(bid > g_prevBid) g_tickDir = 1; else if(bid < g_prevBid) g_tickDir = -1; }
   g_prevBid = bid;

   if(g_dayDone || g_ddHit) { Panel(); return; }

   CalcATR();

   if(!AccSafe())
     { if(InpCloseOnDD) { CloseAll(); DeleteAll(); }
       g_ddHit = true; g_gridActive = false; Panel(); return; }

   UpdateDPL();
   double fl = FloatPL(); double tot = g_dailyPL + fl;
   if(InpDailyTarget > 0 && tot >= InpDailyTarget)
     { CloseAll(); DeleteAll(); g_dayDone = true; g_gridActive = false; Panel(); return; }
   if(InpDailyLoss > 0 && tot <= -InpDailyLoss)
     { CloseAll(); DeleteAll(); g_dayDone = true; g_gridActive = false; Panel(); return; }

   int pend = CntPend(); int pos = CntPos();

   // Manage open positions
   if(pos > 0)
     { ManagePositions(); CleanOpposite(); }

   // Tick-flow: open market order following momentum
   TickFlow();

   // Auto-edit pendings far from price
   if(g_gridActive && pend > 0 && pos == 0)
     {
      AutoEditPendings();
      // Also check on new candle
      if(IsNewM1()) NewCandleRelocate();
     }

   // Grid complete - track P/L for loss counter
   if(g_gridActive && pend == 0 && pos == 0)
     {
      g_gridActive = false;
      g_gridCloseTime = TimeCurrent();

      // Calculate this grid's P/L and update loss counter
      static double gridStartPL = 0;
      double gridEndPL = g_dailyPL;
      double thisGridPL = gridEndPL - gridStartPL;
      gridStartPL = gridEndPL;
      UpdateLossCounter(thisGridPL);
     }

   // Place new grid - with spread filter and loss lockout
   if(!g_gridActive)
     {
      bool can = true;
      if(InpMaxGridsDay > 0 && g_gridsToday >= InpMaxGridsDay) can = false;
      if(g_gridCloseTime > 0 && (TimeCurrent() - g_gridCloseTime) < InpRestartDelay) can = false;
      if(SymbolInfoDouble(_Symbol, SYMBOL_ASK) <= 0) can = false;

      // === SPREAD FILTER ===
      if(!SpreadOK()) can = false;

      // === LOSS LOCKOUT ===
      if(!LossLockoutOK()) can = false;

      if(can) PlaceGrid();
     }

   Panel();
  }

//+------------------------------------------------------------------+
//| COLORED GRAPHICAL DASHBOARD                                       |
//+------------------------------------------------------------------+
#define PANEL_X      10
#define PANEL_Y      30
#define PANEL_W      280
#define LINE_H       18
#define FONT_SIZE    9
#define FONT_NAME    "Consolas"

void DeletePanel()
  {
   ObjectsDeleteAll(0, "SHP_");
  }

void CreateLabel(string name, int x, int y, string txt, color clr, int size = FONT_SIZE)
  {
   string n = "SHP_" + name;
   if(ObjectFind(0, n) < 0)
     {
      ObjectCreate(0, n, OBJ_LABEL, 0, 0, 0);
      ObjectSetInteger(0, n, OBJPROP_CORNER, CORNER_LEFT_UPPER);
      ObjectSetInteger(0, n, OBJPROP_ANCHOR, ANCHOR_LEFT_UPPER);
      ObjectSetString(0, n, OBJPROP_FONT, FONT_NAME);
      ObjectSetInteger(0, n, OBJPROP_FONTSIZE, size);
     }
   ObjectSetInteger(0, n, OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, n, OBJPROP_YDISTANCE, y);
   ObjectSetString(0, n, OBJPROP_TEXT, txt);
   ObjectSetInteger(0, n, OBJPROP_COLOR, clr);
  }

void CreateBox(string name, int x, int y, int w, int h, color clr, color border = clrNONE)
  {
   string n = "SHP_" + name;
   if(ObjectFind(0, n) < 0)
     {
      ObjectCreate(0, n, OBJ_RECTANGLE_LABEL, 0, 0, 0);
      ObjectSetInteger(0, n, OBJPROP_CORNER, CORNER_LEFT_UPPER);
     }
   ObjectSetInteger(0, n, OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, n, OBJPROP_YDISTANCE, y);
   ObjectSetInteger(0, n, OBJPROP_XSIZE, w);
   ObjectSetInteger(0, n, OBJPROP_YSIZE, h);
   ObjectSetInteger(0, n, OBJPROP_BGCOLOR, clr);
   ObjectSetInteger(0, n, OBJPROP_BORDER_TYPE, BORDER_FLAT);
   ObjectSetInteger(0, n, OBJPROP_COLOR, border != clrNONE ? border : clr);
   ObjectSetInteger(0, n, OBJPROP_WIDTH, 1);
   ObjectSetInteger(0, n, OBJPROP_BACK, false);
  }

void Panel()
  {
   double bal = AccountInfoDouble(ACCOUNT_BALANCE);
   double equ = AccountInfoDouble(ACCOUNT_EQUITY);
   double mg = AccountInfoDouble(ACCOUNT_MARGIN);
   int sp = (int)SymbolInfoInteger(_Symbol, SYMBOL_SPREAD);

   int bP = 0, sP = 0, bO = 0, sO = 0;
   double bPnl = 0, sPnl = 0, bL = 0, sL = 0;
   for(int i = PositionsTotal()-1; i >= 0; i--)
     { ulong tk = PositionGetTicket(i); if(tk == 0) continue;
       if(PositionGetString(POSITION_SYMBOL) != _Symbol || PositionGetInteger(POSITION_MAGIC) != InpMagic) continue;
       double pf = PositionGetDouble(POSITION_PROFIT)+PositionGetDouble(POSITION_SWAP); double v = PositionGetDouble(POSITION_VOLUME);
       if(PositionGetInteger(POSITION_TYPE) == POSITION_TYPE_BUY) { bP++; bPnl += pf; bL += v; } else { sP++; sPnl += pf; sL += v; } }
   for(int i = OrdersTotal()-1; i >= 0; i--)
     { ulong tk = OrderGetTicket(i); if(tk == 0) continue;
       if(OrderGetString(ORDER_SYMBOL) != _Symbol || OrderGetInteger(ORDER_MAGIC) != InpMagic) continue;
       if(OrderGetInteger(ORDER_TYPE) == ORDER_TYPE_BUY_STOP) bO++; else if(OrderGetInteger(ORDER_TYPE) == ORDER_TYPE_SELL_STOP) sO++; }

   double fl = bPnl + sPnl, tot = g_dailyPL + fl;
   double ddP = (g_startBal > 0) ? ((g_startBal-equ)/g_startBal)*100 : 0;
   double mgP = (equ > 0) ? (mg/equ)*100 : 0;

   // Colors
   color cBG      = C'25,25,35';
   color cBorder  = C'60,60,80';
   color cTitle   = clrGold;
   color cLabel   = C'180,180,200';
   color cValue   = clrWhite;
   color cGreen   = clrLime;
   color cRed     = clrRed;
   color cYellow  = clrYellow;
   color cCyan    = clrCyan;
   color cOrange  = clrOrange;

   int y = PANEL_Y;
   int x = PANEL_X;

   // Main background - increased height for new section
   CreateBox("BG", x, y, PANEL_W, LINE_H * 32, cBG, cBorder);

   y += 5;

   // === HEADER ===
   CreateLabel("T1", x+10, y, "═══ STOPHUNTER PRO V5.4 ═══", cTitle, 10); y += LINE_H + 2;
   CreateLabel("T2", x+10, y, "  LOSS REDUCTION ALGORITHM", cCyan, 9); y += LINE_H + 5;

   // === ACCOUNT ===
   CreateBox("AccBG", x+5, y, PANEL_W-10, LINE_H*2+5, C'35,35,50', cBorder);
   CreateLabel("Bal", x+10, y+3, "BAL: $" + DoubleToString(bal, 2), cValue);
   CreateLabel("Eq", x+145, y+3, "EQ: $" + DoubleToString(equ, 2), equ >= bal ? cGreen : cRed);
   y += LINE_H;

   // Spread with filter status
   color spClr = (sp <= InpMaxSpread) ? cGreen : cRed;
   string spStat = (sp <= InpMaxSpread) ? " ✓" : " ⛔";
   CreateLabel("Sp", x+10, y+3, "Spread: " + IntegerToString(sp) + spStat, spClr);

   // Tick direction with color
   color tickClr = cLabel;
   string tickStr = "—";
   if(g_tickDir > 0) { tickStr = "▲▲▲"; tickClr = cGreen; }
   if(g_tickDir < 0) { tickStr = "▼▼▼"; tickClr = cRed; }
   CreateLabel("Tick", x+120, y+3, "Tick: " + tickStr, tickClr);

   // Tick flow counter
   color tfClr = (g_ticksUp >= InpTickConfirm || g_ticksDn >= InpTickConfirm) ? cGreen : cLabel;
   CreateLabel("TF", x+200, y+3, "▲" + IntegerToString(g_ticksUp) + " ▼" + IntegerToString(g_ticksDn), tfClr);
   y += LINE_H + 8;

   // === LOSS REDUCTION STATUS ===
   color lrBG = (g_consecLosses == 0) ? C'0,40,0' : (g_consecLosses < InpMaxConsecLoss ? C'60,40,0' : C'60,0,0');
   color lrBorder = (g_consecLosses == 0) ? cGreen : (g_consecLosses < InpMaxConsecLoss ? cOrange : cRed);
   CreateBox("LRBG", x+5, y, PANEL_W-10, LINE_H+4, lrBG, lrBorder);
   string lrStr = "W:" + IntegerToString(g_totalWins) + " L:" + IntegerToString(g_totalLosses) +
                  " | Streak: " + (g_consecLosses > 0 ? "-" + IntegerToString(g_consecLosses) : "+" + IntegerToString(g_consecWins));
   CreateLabel("LR", x+10, y+2, lrStr, g_consecLosses >= InpMaxConsecLoss ? cRed : cValue);
   if(g_lossLockoutUntil > TimeCurrent())
     {
      int minLeft = (int)((g_lossLockoutUntil - TimeCurrent()) / 60);
      CreateLabel("LRLock", x+180, y+2, "⏱" + IntegerToString(minLeft) + "m", cRed);
     }
   else
      CreateLabel("LRLock", x+200, y+2, "Part:" + IntegerToString(g_partialCloseCount), cCyan);
   y += LINE_H + 8;

   // === BIAS ===
   color biasClr = cYellow;
   string biasStr = "═ NEUTRAL";
   if(g_overallBias == BIAS_BULL) { biasStr = "▲▲ BULLISH ▲▲"; biasClr = cGreen; }
   if(g_overallBias == BIAS_BEAR) { biasStr = "▼▼ BEARISH ▼▼"; biasClr = cRed; }
   CreateBox("BiasBG", x+5, y, PANEL_W-10, LINE_H+4, g_overallBias == BIAS_BULL ? C'0,50,0' : (g_overallBias == BIAS_BEAR ? C'50,0,0' : C'50,50,0'), biasClr);
   CreateLabel("Bias", x+80, y+2, biasStr, biasClr, 10);
   y += LINE_H + 8;

   // === STRUCTURE ===
   CreateLabel("StrL", x+10, y, "Structure:", cLabel);
   CreateLabel("StrV", x+85, y, g_structBias == BIAS_BULL ? "HH/HL ▲" : (g_structBias == BIAS_BEAR ? "LH/LL ▼" : "—"), g_structBias == BIAS_BULL ? cGreen : (g_structBias == BIAS_BEAR ? cRed : cLabel));
   CreateLabel("MomL", x+150, y, "Mom:", cLabel);
   CreateLabel("MomV", x+185, y, g_momBias == BIAS_BULL ? "UP ▲" : (g_momBias == BIAS_BEAR ? "DN ▼" : "—"), g_momBias == BIAS_BULL ? cGreen : (g_momBias == BIAS_BEAR ? cRed : cLabel));
   y += LINE_H;

   CreateLabel("CdlL", x+10, y, "Candle:", cLabel);
   CreateLabel("CdlV", x+70, y, g_candleBias == BIAS_BULL ? "GREEN ▲" : (g_candleBias == BIAS_BEAR ? "RED ▼" : "—"), g_candleBias == BIAS_BULL ? cGreen : (g_candleBias == BIAS_BEAR ? cRed : cLabel));

   string patS = "—";
   color patC = cLabel;
   if(g_pattern == PAT_BULL_ENG) { patS = "Engulf▲"; patC = cGreen; }
   if(g_pattern == PAT_BEAR_ENG) { patS = "Engulf▼"; patC = cRed; }
   if(g_pattern == PAT_BULL_PIN) { patS = "Pin▲"; patC = cGreen; }
   if(g_pattern == PAT_BEAR_PIN) { patS = "Pin▼"; patC = cRed; }
   if(g_pattern == PAT_BULL_MOM) { patS = "Mom▲▲"; patC = cGreen; }
   if(g_pattern == PAT_BEAR_MOM) { patS = "Mom▼▼"; patC = cRed; }
   CreateLabel("PatL", x+150, y, "Pat:", cLabel);
   CreateLabel("PatV", x+180, y, patS, patC);
   y += LINE_H + 5;

   // === DYNAMIC VALUES ===
   CreateLabel("DynH", x+10, y, "─── DYNAMIC ───", cCyan); y += LINE_H;
   CreateLabel("ATR", x+10, y, "ATR: " + DoubleToString(g_currentATR/g_pipSize, 1) + "p", cValue);
   CreateLabel("BET", x+80, y, "BE@" + DoubleToString(g_dynBETrigger, 0) + "p", cOrange);
   CreateLabel("TRL", x+140, y, "Trail@" + DoubleToString(g_dynTrailStart, 0) + "p", cCyan);
   CreateLabel("HNT", x+215, y, "Hunt@" + DoubleToString(g_dynTPHuntTrail, 0) + "p", cYellow);
   y += LINE_H + 5;

   // === PROTECTION ===
   color ddClr = ddP < InpMaxDDPercent * 0.5 ? cGreen : (ddP < InpMaxDDPercent * 0.8 ? cOrange : cRed);
   color mgClr = mgP < InpMaxMarginPct * 0.5 ? cGreen : (mgP < InpMaxMarginPct * 0.8 ? cOrange : cRed);
   CreateLabel("ProtH", x+10, y, "─── PROTECTION ───", g_ddHit ? cRed : cGreen); y += LINE_H;
   CreateLabel("DD", x+10, y, "DD: " + DoubleToString(ddP, 1) + "%/" + DoubleToString(InpMaxDDPercent, 0) + "%", ddClr);
   CreateLabel("MG", x+120, y, "Margin: " + DoubleToString(mgP, 1) + "%", mgClr);
   CreateLabel("Safe", x+210, y, g_ddHit ? "⛔ HIT" : "✓ OK", g_ddHit ? cRed : cGreen);
   y += LINE_H + 5;

   // === ORDERS ===
   CreateLabel("OrdH", x+10, y, "─── ORDERS ───", cCyan); y += LINE_H;
   CreateLabel("Pend", x+10, y, "Pending: BS=" + IntegerToString(bO) + " SS=" + IntegerToString(sO), cLabel);
   y += LINE_H;
   CreateLabel("BuyP", x+10, y, "BUY: " + IntegerToString(bP) + " @ " + DoubleToString(bL, 2) + "L", cGreen);
   CreateLabel("BuyPL", x+150, y, "$" + DoubleToString(bPnl, 2), bPnl >= 0 ? cGreen : cRed);
   y += LINE_H;
   CreateLabel("SelP", x+10, y, "SELL: " + IntegerToString(sP) + " @ " + DoubleToString(sL, 2) + "L", cRed);
   CreateLabel("SelPL", x+150, y, "$" + DoubleToString(sPnl, 2), sPnl >= 0 ? cGreen : cRed);
   y += LINE_H;
   CreateLabel("Float", x+10, y, "FLOATING:", cLabel);
   CreateLabel("FloatV", x+90, y, "$" + DoubleToString(fl, 2), fl >= 0 ? cGreen : cRed, 10);
   y += LINE_H + 5;

   // === SESSION ===
   color plClr = tot >= 0 ? cGreen : cRed;
   CreateBox("SessBG", x+5, y, PANEL_W-10, LINE_H*2+8, tot >= 0 ? C'0,40,0' : C'40,0,0', tot >= 0 ? cGreen : cRed);
   CreateLabel("PL", x+10, y+3, "TODAY P/L: $" + DoubleToString(tot, 2), plClr, 11);
   CreateLabel("Grids", x+180, y+3, "Grids: " + IntegerToString(g_gridsToday), cValue);
   y += LINE_H;
   CreateLabel("Stats", x+10, y+3, "BE:" + IntegerToString(g_beCount) + " Tr:" + IntegerToString(g_trailCount) + " Ed:" + IntegerToString(g_editCount) + " Fl:" + IntegerToString(g_flipCount), cLabel);
   CreateLabel("TFSt", x+180, y+3, "TF:" + IntegerToString(g_tickFlowCount), cCyan);
   y += LINE_H + 8;

   // === STATUS ===
   color statClr = g_gridActive ? cGreen : cYellow;
   string statStr = g_gridActive ? "● GRID ACTIVE" : "○ WAITING...";
   if(g_lossLockoutUntil > TimeCurrent()) { statStr = "⏱ LOSS COOLDOWN"; statClr = cOrange; }
   if(InpUseSpreadFilter && !SpreadOK()) { statStr = "⛔ SPREAD HIGH"; statClr = cRed; }
   if(g_dayDone) { statStr = "★ DAY COMPLETE ★"; statClr = clrGold; }
   if(g_ddHit) { statStr = "⛔ DD PROTECTION ⛔"; statClr = cRed; }
   CreateBox("StatBG", x+5, y, PANEL_W-10, LINE_H+4, g_gridActive ? C'0,60,0' : C'60,60,0', statClr);
   CreateLabel("Stat", x+70, y+2, statStr, statClr, 10);
   y += LINE_H + 5;

   // Loss reduction features status
   string feat1 = InpSmartBE ? "🛡SmartBE" : (InpInstantBE ? "⚡InstBE" : "BE:OFF");
   string feat2 = InpUsePartialClose ? "💰Part" + IntegerToString(InpPartialPercent) + "%" : "Part:OFF";
   string feat3 = InpUseSpreadFilter ? "📊Sp<" + IntegerToString(InpMaxSpread) : "Sp:OFF";
   string feat4 = InpUseLossCounter ? "🔢L<" + IntegerToString(InpMaxConsecLoss) : "LC:OFF";
   CreateLabel("Feat1", x+10, y, feat1, InpSmartBE || InpInstantBE ? cGreen : cLabel);
   CreateLabel("Feat2", x+85, y, feat2, InpUsePartialClose ? cCyan : cLabel);
   CreateLabel("Feat3", x+155, y, feat3, InpUseSpreadFilter ? cOrange : cLabel);
   CreateLabel("Feat4", x+215, y, feat4, InpUseLossCounter ? cYellow : cLabel);

   ChartRedraw(0);
  }

//+------------------------------------------------------------------+
