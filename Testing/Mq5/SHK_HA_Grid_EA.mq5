//+------------------------------------------------------------------+
//|                                          SHK_HA_Grid_EA.mq5      |
//|   Self-contained Expert Advisor for MT5.                         |
//|                                                                    |
//|   The Heikin Ashi + HA-color-change + MACD-divergence confirmed  |
//|   signal engine from SHK_Professional_Heikin_Ashi_Signals.mq5    |
//|   is PORTED DIRECTLY INTO THIS EA (functions prefixed "E").      |
//|   No iCustom() call, no external indicator file required - the   |
//|   EA is fully self-contained and only needs Trade.mqh (standard  |
//|   MT5 library).                                                  |
//|                                                                    |
//|   On every newly closed bar the EA recomputes the full signal    |
//|   history from scratch (equivalent to the indicator's own        |
//|   "full recalculation" pass) and checks whether a confirmed      |
//|   BUY or SELL fired on the bar that just closed. This keeps the  |
//|   logic byte-for-byte equivalent to the original indicator while |
//|   avoiding any dependency on a compiled .ex5 indicator file.     |
//|                                                                    |
//|   On top of the signal engine: SL/TP (fixed pips or ATR-based),  |
//|   risk-based or fixed lot sizing, a trailing stop, and an        |
//|   optional grid/averaging module (per-trade or basket TP/SL,     |
//|   works on netting and hedging accounts alike).                  |
//+------------------------------------------------------------------+
#property copyright "SHK"
#property link      ""
#property version   "2.00"
#property description "Self-contained EA: embedded SHK Heikin Ashi + MACD divergence signal engine, SL/TP, trailing stop and grid/averaging module."
#property strict

#include <Trade\Trade.mqh>

//====================================================================
// ENUMS
//====================================================================
enum ENUM_TRADE_DIRECTION
{
   DIR_BOTH      = 0,   // Buy and Sell
   DIR_BUY_ONLY  = 1,   // Buy only
   DIR_SELL_ONLY = 2    // Sell only
};

enum ENUM_GRID_TP_MODE
{
   GRID_TP_PER_TRADE      = 0,  // Each grid trade has its own TP
   GRID_TP_BASKET_MONEY   = 1,  // Close whole basket at target profit (account currency)
   GRID_TP_BASKET_PIPS    = 2   // Close whole basket when price reaches X pips from average entry
};

//====================================================================
// INPUTS
//====================================================================
input string I1 = "====== Signal Engine (embedded HA + MACD divergence) ======"; // ---
input int    InpMaxBarsToCalculate       = 3000;    // history window used to rebuild the signal state each bar
input bool   InpUseClosedBarsOnly        = true;    // non-repaint: only ever act on a fully closed bar
input int    InpConfirmationCandles      = 1;       // 1 to 10, consecutive same-color HA candles required
input double InpMinBodyPercent           = 0.0;
input bool   InpSignalOnHAColorChange    = true;    // true = trade on confirmed HA color flip; false = momentum/pending-breakout mode
input bool   InpRequireMACDDivergence    = true;
input int    InpMACDFastEMA              = 12;
input int    InpMACDSlowEMA              = 26;
input int    InpMACDSignalEMA            = 5;
input int    InpDivergencePivotLookback  = 2;
input int    InpDivergenceSearchBars     = 200;
input int    InpDivergenceConfirmWindow  = 30;
input bool   InpRequireStrongCandleForCircle = false; // only used in momentum mode (InpSignalOnHAColorChange = false)
input bool   InpCancelSignalOnOppositeHA = true;      // only used in momentum mode
input bool   InpConfirmByCloseBreak      = true;      // only used in momentum mode

input string I1b = "====== Trend Filter (recommended) ======"; // ---
input bool   InpUseTrendFilter           = true;    // suppress signals that fight the prevailing trend
input ENUM_TIMEFRAMES InpTrendMATimeframe= PERIOD_CURRENT; // use a higher timeframe (e.g. H1) for a smoother read
input int    InpTrendMAPeriod            = 200;
input ENUM_MA_METHOD InpTrendMAMethod    = MODE_EMA;

input string I2 = "====== Trading ======"; // ---
input ulong  InpMagic                    = 20260815;
input string InpTradeComment             = "SHK-HA-EA";
input ENUM_TRADE_DIRECTION InpTradeDirection = DIR_BOTH;
input int    InpSlippagePoints           = 30;
input double InpMaxSpreadPoints          = 0;      // 0 = disabled
input bool   InpCloseOnOppositeSignal    = true;

input string I3 = "====== Position Sizing ======"; // ---
input double InpFixedLots                = 0.01;   // used when InpRiskPercent = 0
input double InpRiskPercent              = 0.0;     // 0 = use fixed lots, otherwise % balance risk per trade based on SL distance

input string I4 = "====== Stop Loss / Take Profit ======"; // ---
input bool   InpUseATRForSLTP            = false;
input int    InpATRPeriod                = 14;
input double InpATRMultiplierSL          = 2.0;
input double InpATRMultiplierTP          = 3.0;
input double InpStopLossPips             = 50;
input double InpTakeProfitPips           = 100;

input string I5 = "====== Trailing Stop ======"; // ---
input bool   InpUseTrailingStop          = false;
input double InpTrailingStartPips        = 30;
input double InpTrailingStopPips         = 20;
input double InpTrailingStepPips         = 5;

input string I6 = "====== Grid / Averaging ======"; // ---
input bool   InpGridEnabled              = false;
input double InpGridStepPips             = 200;     // distance between grid levels
input int    InpGridMaxLevels            = 5;        // includes the initial trade
input double InpGridLotMultiplier        = 1.5;      // lot multiplier per additional level
input bool   InpGridUseIndividualSL      = true;     // safety SL on every grid trade (wide)
input double InpGridIndividualSLPips     = 300;
input ENUM_GRID_TP_MODE InpGridTPMode    = GRID_TP_BASKET_MONEY;
input double InpGridBasketTakeProfitMoney= 10.0;      // account currency, used if mode = BASKET_MONEY
input double InpGridBasketStopLossMoney  = 0.0;       // 0 = disabled, account currency
input double InpGridBasketTakeProfitPips = 50;        // pips from average entry, used if mode = BASKET_PIPS

input string I7 = "====== Alerts ======"; // ---
input bool   InpEnablePopupAlert         = true;
input bool   InpEnablePushAlert          = false;
input bool   InpEnableSoundAlert         = false;
input string InpBuySoundFile             = "alert.wav";
input string InpSellSoundFile            = "alert2.wav";
input int    InpAlertCooldownSeconds     = 120;

//====================================================================
// CONSTANTS (mirrors the original indicator)
//====================================================================
#define MOM_BODY_WINDOW   14
#define CIRCLE_WICK_FRAC  0.15

//====================================================================
// GLOBALS - trading / infrastructure
//====================================================================
CTrade   trade;

int      g_atrHandle    = INVALID_HANDLE;
int      g_trendMAHandle = INVALID_HANDLE;

double   g_point        = 0.0;
double   g_pip          = 0.0;   // pip size (10 * point on 3/5-digit symbols)

datetime g_lastBarTime   = 0;
int      g_confCandles   = 1;

// basket state (persisted via GlobalVariables so it survives EA restarts)
int      g_basketDir     = 0;     // 0 = none, 1 = buy, -1 = sell
double   g_basketEntryPx = 0.0;   // price of the very first grid entry
int      g_basketLevels  = 0;     // number of grid levels filled so far

// alert dedupe
datetime g_lastAlertBarTime = 0;
int      g_lastAlertDir     = 0;
datetime g_lastAlertWall    = 0;

//====================================================================
// GLOBALS - embedded signal engine (ported from the indicator)
//====================================================================
double   eHAOpen[];
double   eHAHigh[];
double   eHALow[];
double   eHAClose[];

double   eMacdFast[];
double   eMacdSlow[];
double   eMacdSignal[];
double   eMacdHist[];
bool     eBullDiv[];
bool     eBearDiv[];

int      eFirstBar = 1;

// pending momentum->confirmation state machine (only used when InpSignalOnHAColorChange = false)
bool     eBuyPending  = false;
double   eBuyRefHigh  = 0.0;
int      eBuyBarsLeft = 0;

bool     eSellPending = false;
double   eSellRefLow  = 0.0;
int      eSellBarsLeft= 0;

//====================================================================
// GLOBAL VARIABLE (terminal) KEYS - persist basket state across restarts
//====================================================================
string GV_Dir()    { return StringFormat("SHK_HA_EA_%s_%I64u_Dir",    _Symbol, InpMagic); }
string GV_Entry()  { return StringFormat("SHK_HA_EA_%s_%I64u_Entry",  _Symbol, InpMagic); }
string GV_Levels() { return StringFormat("SHK_HA_EA_%s_%I64u_Levels", _Symbol, InpMagic); }

void SaveBasketState()
{
   GlobalVariableSet(GV_Dir(),    (double)g_basketDir);
   GlobalVariableSet(GV_Entry(),  g_basketEntryPx);
   GlobalVariableSet(GV_Levels(), (double)g_basketLevels);
}

void LoadBasketState()
{
   g_basketDir     = GlobalVariableCheck(GV_Dir())    ? (int)GlobalVariableGet(GV_Dir())    : 0;
   g_basketEntryPx = GlobalVariableCheck(GV_Entry())  ? GlobalVariableGet(GV_Entry())        : 0.0;
   g_basketLevels  = GlobalVariableCheck(GV_Levels()) ? (int)GlobalVariableGet(GV_Levels())  : 0;
}

void ClearBasketState()
{
   g_basketDir     = 0;
   g_basketEntryPx = 0.0;
   g_basketLevels  = 0;
   if(GlobalVariableCheck(GV_Dir()))    GlobalVariableDel(GV_Dir());
   if(GlobalVariableCheck(GV_Entry()))  GlobalVariableDel(GV_Entry());
   if(GlobalVariableCheck(GV_Levels())) GlobalVariableDel(GV_Levels());
}

//====================================================================
// EMBEDDED SIGNAL ENGINE  (ported from SHK_Professional_Heikin_Ashi_Signals.mq5)
//====================================================================
bool EIsBullishHA(const int i) { return(eHAClose[i] > eHAOpen[i]); }
bool EIsBearishHA(const int i) { return(eHAClose[i] < eHAOpen[i]); }

double EGetBodyPercent(const int i)
{
   double range = eHAHigh[i] - eHALow[i];
   if(range <= 0.0) return(0.0);
   return(MathAbs(eHAClose[i] - eHAOpen[i]) / range * 100.0);
}

double EAvgHABody(const int i, const int win, const int firstBar)
{
   int st = i - win;
   if(st < firstBar) return(0.0);
   double s = 0.0; int c = 0;
   for(int k = st; k < i; k++) { s += MathAbs(eHAClose[k] - eHAOpen[k]); c++; }
   return(c > 0 ? s / c : 0.0);
}

void EComputeHABar(const int i, const MqlRates &r[])
{
   double haClose = (r[i].open + r[i].high + r[i].low + r[i].close) / 4.0;
   double haOpen  = (eHAOpen[i - 1] + eHAClose[i - 1]) / 2.0;
   double haHigh  = MathMax(r[i].high, MathMax(haOpen, haClose));
   double haLow   = MathMin(r[i].low,  MathMin(haOpen, haClose));
   eHAOpen[i] = haOpen; eHAClose[i] = haClose; eHAHigh[i] = haHigh; eHALow[i] = haLow;
}

void EInitHASeed(const int firstBar, const MqlRates &r[])
{
   double haClose = (r[firstBar].open + r[firstBar].high + r[firstBar].low + r[firstBar].close) / 4.0;
   double haOpen  = (r[firstBar].open + r[firstBar].close) / 2.0;
   eHAClose[firstBar] = haClose;
   eHAOpen[firstBar]  = haOpen;
   eHAHigh[firstBar]  = MathMax(r[firstBar].high, MathMax(haOpen, haClose));
   eHALow[firstBar]   = MathMin(r[firstBar].low,  MathMin(haOpen, haClose));
}

bool EIsMacdPeak(const int i, const int lookback, const int firstBar, const int lastBar)
{
   if(i - lookback < firstBar || i + lookback > lastBar) return(false);
   for(int k = 1; k <= lookback; k++)
      if(eMacdHist[i] <= eMacdHist[i - k] || eMacdHist[i] <= eMacdHist[i + k]) return(false);
   return(true);
}

bool EIsMacdTrough(const int i, const int lookback, const int firstBar, const int lastBar)
{
   if(i - lookback < firstBar || i + lookback > lastBar) return(false);
   for(int k = 1; k <= lookback; k++)
      if(eMacdHist[i] >= eMacdHist[i - k] || eMacdHist[i] >= eMacdHist[i + k]) return(false);
   return(true);
}

void EComputeMACDDivergence(const int rates_total, const int firstBar, const int lastClosed, const MqlRates &r[])
{
   ArrayResize(eMacdFast, rates_total);
   ArrayResize(eMacdSlow, rates_total);
   ArrayResize(eMacdSignal, rates_total);
   ArrayResize(eMacdHist, rates_total);
   ArrayResize(eBullDiv, rates_total);
   ArrayResize(eBearDiv, rates_total);

   for(int i = 0; i < rates_total; i++)
   {
      eMacdFast[i] = 0.0; eMacdSlow[i] = 0.0; eMacdSignal[i] = 0.0; eMacdHist[i] = 0.0;
      eBullDiv[i] = false; eBearDiv[i] = false;
   }

   if(!InpRequireMACDDivergence) return;

   int fast = InpMACDFastEMA, slow = InpMACDSlowEMA, signal = InpMACDSignalEMA;
   int pivotLookback = InpDivergencePivotLookback, searchBars = InpDivergenceSearchBars;
   if(fast < 1) fast = 1;
   if(slow < 1) slow = 1;
   if(signal < 1) signal = 1;
   if(pivotLookback < 1) pivotLookback = 1;
   if(searchBars < 1) searchBars = 1;

   if(lastClosed < firstBar + pivotLookback * 2 + 2) return;

   double fastAlpha   = 2.0 / ((double)fast + 1.0);
   double slowAlpha   = 2.0 / ((double)slow + 1.0);
   double signalAlpha = 2.0 / ((double)signal + 1.0);

   for(int i = firstBar; i <= lastClosed; i++)
   {
      double price = r[i].close;
      if(i == firstBar)
      {
         eMacdFast[i] = price; eMacdSlow[i] = price; eMacdSignal[i] = 0.0; eMacdHist[i] = 0.0;
         continue;
      }
      eMacdFast[i]   = fastAlpha * price + (1.0 - fastAlpha) * eMacdFast[i - 1];
      eMacdSlow[i]   = slowAlpha * price + (1.0 - slowAlpha) * eMacdSlow[i - 1];
      double macdMain = eMacdFast[i] - eMacdSlow[i];
      eMacdSignal[i] = signalAlpha * macdMain + (1.0 - signalAlpha) * eMacdSignal[i - 1];
      eMacdHist[i]   = macdMain - eMacdSignal[i];
   }

   int lastPivot = lastClosed - pivotLookback;
   for(int i = firstBar + pivotLookback; i <= lastPivot; i++)
   {
      if(EIsMacdTrough(i, pivotLookback, firstBar, lastClosed))
      {
         int prevTrough = -1;
         int oldest = i - searchBars;
         if(oldest < firstBar + pivotLookback) oldest = firstBar + pivotLookback;
         for(int j = i - 1; j >= oldest; j--)
            if(EIsMacdTrough(j, pivotLookback, firstBar, lastClosed)) { prevTrough = j; break; }

         if(prevTrough >= 0 && r[i].low < r[prevTrough].low && eMacdHist[i] > eMacdHist[prevTrough])
            eBullDiv[i + pivotLookback] = true;
      }

      if(EIsMacdPeak(i, pivotLookback, firstBar, lastClosed))
      {
         int prevPeak = -1;
         int oldest = i - searchBars;
         if(oldest < firstBar + pivotLookback) oldest = firstBar + pivotLookback;
         for(int j = i - 1; j >= oldest; j--)
            if(EIsMacdPeak(j, pivotLookback, firstBar, lastClosed)) { prevPeak = j; break; }

         if(prevPeak >= 0 && r[i].high > r[prevPeak].high && eMacdHist[i] < eMacdHist[prevPeak])
            eBearDiv[i + pivotLookback] = true;
      }
   }
}

bool ERecentMACDDivergenceConfirmed(const int i, const bool bullish)
{
   if(!InpRequireMACDDivergence) return(true);
   int window = InpDivergenceConfirmWindow;
   if(window < 0) window = 0;
   int first = i - window;
   if(first < eFirstBar) first = eFirstBar;
   for(int k = i; k >= first; k--)
   {
      if(bullish && eBullDiv[k])  return(true);
      if(!bullish && eBearDiv[k]) return(true);
   }
   return(false);
}

bool EHasConfirmedHAColorChange(const int i, const bool bullish)
{
   int candles = g_confCandles;
   if(candles < 1) candles = 1;
   int runStart = i - candles + 1;
   int previous = runStart - 1;
   if(runStart < eFirstBar + 1 || previous < eFirstBar) return(false);

   for(int k = runStart; k <= i; k++)
   {
      if(bullish && !EIsBullishHA(k)) return(false);
      if(!bullish && !EIsBearishHA(k)) return(false);
      if(EGetBodyPercent(k) < InpMinBodyPercent) return(false);
   }
   if(bullish) return(EIsBearishHA(previous));
   return(EIsBullishHA(previous));
}

// returns +1 = buy, -1 = sell, 0 = none
int EProcessColorChangeArrow(const int i)
{
   if(EHasConfirmedHAColorChange(i, true) && ERecentMACDDivergenceConfirmed(i, true))
      return(+1);
   if(EHasConfirmedHAColorChange(i, false) && ERecentMACDDivergenceConfirmed(i, false))
      return(-1);
   return(0);
}

// returns +1 = buy fired, -1 = sell fired, 0 = none (momentum / pending-breakout mode)
int EProcessMomentumBranch(const int i)
{
   bool curBull  = EIsBullishHA(i);
   bool curBear  = EIsBearishHA(i);
   bool prevBull = EIsBullishHA(i - 1);
   bool prevBear = EIsBearishHA(i - 1);

   bool placedBuyArrow = false, placedSellArrow = false;
   int  fired = 0;

   //--- 1) resolve pending BUY confirmation against bar i
   if(eBuyPending)
   {
      if(InpCancelSignalOnOppositeHA && curBear)
      {
         eBuyPending = false;
      }
      else
      {
         bool brk = InpConfirmByCloseBreak ? (eHAClose[i] > eBuyRefHigh) : (eHAHigh[i] > eBuyRefHigh);
         if(brk)
         {
            placedBuyArrow = true;
            eBuyPending = false;
            fired = +1;
         }
         else
         {
            eBuyBarsLeft--;
            if(eBuyBarsLeft <= 0) eBuyPending = false;
         }
      }
   }

   //--- 2) resolve pending SELL confirmation against bar i
   if(eSellPending)
   {
      if(InpCancelSignalOnOppositeHA && curBull)
      {
         eSellPending = false;
      }
      else
      {
         bool brk = InpConfirmByCloseBreak ? (eHAClose[i] < eSellRefLow) : (eHALow[i] < eSellRefLow);
         if(brk && !placedBuyArrow)
         {
            placedSellArrow = true;
            eSellPending = false;
            fired = -1;
         }
         else
         {
            eSellBarsLeft--;
            if(eSellBarsLeft <= 0) eSellPending = false;
         }
      }
   }

   //--- 3) detect a NEW momentum shift on bar i (arms a pending confirmation, does not itself trade)
   double bodyPct = EGetBodyPercent(i);
   double avgBody = EAvgHABody(i, MOM_BODY_WINDOW, eFirstBar);
   double range   = eHAHigh[i] - eHALow[i];
   double body    = MathAbs(eHAClose[i] - eHAOpen[i]);

   if(prevBear && curBull && !placedSellArrow)
   {
      bool ok = (bodyPct >= InpMinBodyPercent);
      if(ok && InpRequireStrongCandleForCircle)
      {
         double lowerWick = MathMin(eHAOpen[i], eHAClose[i]) - eHALow[i];
         bool strong = (range > 0.0 && lowerWick <= CIRCLE_WICK_FRAC * range) ||
                       (avgBody > 0.0 && body >= avgBody);
         ok = strong;
      }
      if(ok)
      {
         eBuyPending  = true;
         eBuyRefHigh  = eHAHigh[i];
         eBuyBarsLeft = g_confCandles;
      }
   }
   else if(prevBull && curBear && !placedBuyArrow)
   {
      bool ok = (bodyPct >= InpMinBodyPercent);
      if(ok && InpRequireStrongCandleForCircle)
      {
         double upperWick = eHAHigh[i] - MathMax(eHAOpen[i], eHAClose[i]);
         bool strong = (range > 0.0 && upperWick <= CIRCLE_WICK_FRAC * range) ||
                       (avgBody > 0.0 && body >= avgBody);
         ok = strong;
      }
      if(ok)
      {
         eSellPending  = true;
         eSellRefLow   = eHALow[i];
         eSellBarsLeft = g_confCandles;
      }
   }

   return(fired);
}

int EProcessBar(const int i)
{
   if(i < eFirstBar + 1) return(0);
   if(InpSignalOnHAColorChange)
      return(EProcessColorChangeArrow(i));
   return(EProcessMomentumBranch(i));
}

//--- master driver: rebuilds the full signal history (equivalent to the indicator's
//    full-recalculation pass) and returns the direction fired on the last closed bar.
bool RecalcSignal(int &outDir)
{
   outDir = 0;

   MqlRates r[];
   ArraySetAsSeries(r, false);
   int need = (InpMaxBarsToCalculate > 0) ? InpMaxBarsToCalculate : 3000;
   int copied = CopyRates(_Symbol, _Period, 0, need, r);
   if(copied < 50) return(false); // not enough history yet

   int rates_total = copied;
   int firstBar = rates_total - need;
   if(firstBar < 1) firstBar = 1;
   eFirstBar = firstBar;

   ArrayResize(eHAOpen,  rates_total);
   ArrayResize(eHAHigh,  rates_total);
   ArrayResize(eHALow,   rates_total);
   ArrayResize(eHAClose, rates_total);

   EInitHASeed(firstBar, r);
   for(int i = firstBar + 1; i < rates_total; i++)
      EComputeHABar(i, r);

   int lastClosed = InpUseClosedBarsOnly ? rates_total - 2 : rates_total - 1;
   if(lastClosed < firstBar + 1) return(false);

   EComputeMACDDivergence(rates_total, firstBar, lastClosed, r);

   // replay the state machine from the start every time - deterministic and
   // avoids any incremental-update drift
   eBuyPending = false; eBuyRefHigh = 0.0; eBuyBarsLeft = 0;
   eSellPending = false; eSellRefLow = 0.0; eSellBarsLeft = 0;

   int fired = 0;
   for(int i = firstBar + 1; i <= lastClosed; i++)
      fired = EProcessBar(i);

   outDir = fired;
   return(true);
}

//====================================================================
// TRADING HELPERS
//====================================================================
double PipSize() { return g_pip; }

double NormalizeLot(double lots)
{
   double minLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double maxLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
   double step   = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
   if(step <= 0) step = 0.01;

   lots = MathRound(lots / step) * step;
   if(lots < minLot) lots = minLot;
   if(lots > maxLot) lots = maxLot;
   return NormalizeDouble(lots, 2);
}

double CalcLot(double slPips)
{
   if(InpRiskPercent <= 0.0 || slPips <= 0.0)
      return NormalizeLot(InpFixedLots);

   double balance   = AccountInfoDouble(ACCOUNT_BALANCE);
   double riskMoney = balance * InpRiskPercent / 100.0;

   double tickValue = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE);
   double tickSize  = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
   if(tickValue <= 0 || tickSize <= 0)
      return NormalizeLot(InpFixedLots);

   double moneyPerPipPerLot = (PipSize() / tickSize) * tickValue;
   if(moneyPerPipPerLot <= 0)
      return NormalizeLot(InpFixedLots);

   double lots = riskMoney / (slPips * moneyPerPipPerLot);
   return NormalizeLot(lots);
}

bool IsNewBar()
{
   datetime t0 = iTime(_Symbol, _Period, 0);
   if(t0 != g_lastBarTime)
   {
      g_lastBarTime = t0;
      return(true);
   }
   return(false);
}

bool SpreadOK()
{
   if(InpMaxSpreadPoints <= 0) return(true);
   long spread = SymbolInfoInteger(_Symbol, SYMBOL_SPREAD);
   return(spread <= (long)InpMaxSpreadPoints);
}

double GetATR()
{
   if(g_atrHandle == INVALID_HANDLE) return(0.0);
   double buf[];
   ArraySetAsSeries(buf, true);
   if(CopyBuffer(g_atrHandle, 0, 1, 1, buf) < 1) return(0.0);
   return(buf[0]);
}

void FireTradeAlert(int dir, string typeText, double price)
{
   if(!(InpEnablePopupAlert || InpEnablePushAlert || InpEnableSoundAlert)) return;

   datetime barTime = iTime(_Symbol, _Period, 1);
   if(barTime == g_lastAlertBarTime && dir == g_lastAlertDir) return;
   if(g_lastAlertWall > 0 && dir == g_lastAlertDir &&
      (TimeCurrent() - g_lastAlertWall) < InpAlertCooldownSeconds) return;

   string tf  = StringSubstr(EnumToString((ENUM_TIMEFRAMES)_Period), 7);
   string msg = StringFormat("SHK HA Grid EA | %s %s | %s | Price: %s",
                              _Symbol, tf, typeText, DoubleToString(price, _Digits));

   if(InpEnablePopupAlert) Alert(msg);
   if(InpEnablePushAlert)  SendNotification(msg);
   if(InpEnableSoundAlert) PlaySound(dir > 0 ? InpBuySoundFile : InpSellSoundFile);

   g_lastAlertBarTime = barTime;
   g_lastAlertDir      = dir;
   g_lastAlertWall      = TimeCurrent();
}

//--- collect basket stats for our magic/symbol/direction (works for netting - single
//    merged position - and hedging - many tickets - accounts alike)
void GetBasketStats(int dir, double &totalVolume, double &avgPrice, double &totalProfit, int &count)
{
   totalVolume = 0.0; avgPrice = 0.0; totalProfit = 0.0; count = 0;
   double volPriceSum = 0.0;

   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL) != _Symbol) continue;
      if((long)PositionGetInteger(POSITION_MAGIC) != (long)InpMagic) continue;

      long ptype = PositionGetInteger(POSITION_TYPE);
      bool isBuy = (ptype == POSITION_TYPE_BUY);
      if(dir == 1 && !isBuy) continue;
      if(dir == -1 && isBuy) continue;

      double vol    = PositionGetDouble(POSITION_VOLUME);
      double price  = PositionGetDouble(POSITION_PRICE_OPEN);
      double profit = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);

      totalVolume += vol;
      volPriceSum += vol * price;
      totalProfit += profit;
      count++;
   }

   if(totalVolume > 0.0) avgPrice = volPriceSum / totalVolume;
}

void CloseBasket(int dir)
{
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL) != _Symbol) continue;
      if((long)PositionGetInteger(POSITION_MAGIC) != (long)InpMagic) continue;

      long ptype = PositionGetInteger(POSITION_TYPE);
      bool isBuy = (ptype == POSITION_TYPE_BUY);
      if(dir == 1 && !isBuy) continue;
      if(dir == -1 && isBuy) continue;

      trade.PositionClose(ticket, (ulong)InpSlippagePoints);
   }
   ClearBasketState();
}

//====================================================================
// TRADING ACTIONS
//====================================================================
void OpenInitialTrade(int dir)
{
   double atr = InpUseATRForSLTP ? GetATR() : 0.0;
   double lot = CalcLot(InpUseATRForSLTP ? (atr / PipSize()) * InpATRMultiplierSL : InpStopLossPips);
   if(lot <= 0) lot = NormalizeLot(InpFixedLots);

   double price = (dir == 1) ? SymbolInfoDouble(_Symbol, SYMBOL_ASK) : SymbolInfoDouble(_Symbol, SYMBOL_BID);

   double sl = 0.0, tp = 0.0;
   if(InpUseATRForSLTP && atr > 0.0)
   {
      sl = (dir == 1) ? price - atr * InpATRMultiplierSL : price + atr * InpATRMultiplierSL;
      tp = (dir == 1) ? price + atr * InpATRMultiplierTP : price - atr * InpATRMultiplierTP;
   }
   else
   {
      if(InpStopLossPips > 0)
         sl = (dir == 1) ? price - InpStopLossPips * PipSize() : price + InpStopLossPips * PipSize();
      if(InpTakeProfitPips > 0)
         tp = (dir == 1) ? price + InpTakeProfitPips * PipSize() : price - InpTakeProfitPips * PipSize();
   }

   if(InpGridEnabled && InpGridTPMode != GRID_TP_PER_TRADE)
      tp = 0.0; // basket modes manage the exit themselves

   trade.SetExpertMagicNumber(InpMagic);
   trade.SetDeviationInPoints(InpSlippagePoints);

   string cmt = StringFormat("%s-%s-L1", InpTradeComment, dir == 1 ? "BUY" : "SELL");
   bool ok = (dir == 1)
             ? trade.Buy(lot, _Symbol, price, sl, tp, cmt)
             : trade.Sell(lot, _Symbol, price, sl, tp, cmt);

   if(ok)
   {
      g_basketDir = dir; g_basketEntryPx = price; g_basketLevels = 1;
      SaveBasketState();
      FireTradeAlert(dir, dir == 1 ? "BUY signal confirmed" : "SELL signal confirmed", price);
   }
   else
   {
      PrintFormat("SHK_HA_Grid_EA: initial %s order failed. Error=%d (%s)",
                  dir == 1 ? "BUY" : "SELL", GetLastError(), trade.ResultRetcodeDescription());
   }
}

void AddGridLevel(int dir)
{
   int level = g_basketLevels;
   double lot = NormalizeLot(CalcLot(InpStopLossPips) * MathPow(InpGridLotMultiplier, level));

   double price = (dir == 1) ? SymbolInfoDouble(_Symbol, SYMBOL_ASK) : SymbolInfoDouble(_Symbol, SYMBOL_BID);

   double sl = 0.0, tp = 0.0;
   if(InpGridUseIndividualSL && InpGridIndividualSLPips > 0)
      sl = (dir == 1) ? price - InpGridIndividualSLPips * PipSize() : price + InpGridIndividualSLPips * PipSize();
   if(InpGridTPMode == GRID_TP_PER_TRADE && InpTakeProfitPips > 0)
      tp = (dir == 1) ? price + InpTakeProfitPips * PipSize() : price - InpTakeProfitPips * PipSize();

   trade.SetExpertMagicNumber(InpMagic);
   trade.SetDeviationInPoints(InpSlippagePoints);

   string cmt = StringFormat("%s-%s-L%d", InpTradeComment, dir == 1 ? "BUY" : "SELL", g_basketLevels + 1);
   bool ok = (dir == 1)
             ? trade.Buy(lot, _Symbol, price, sl, tp, cmt)
             : trade.Sell(lot, _Symbol, price, sl, tp, cmt);

   if(ok)
   {
      g_basketLevels++;
      SaveBasketState();
   }
   else
   {
      PrintFormat("SHK_HA_Grid_EA: grid level %d %s order failed. Error=%d (%s)",
                  g_basketLevels + 1, dir == 1 ? "BUY" : "SELL", GetLastError(), trade.ResultRetcodeDescription());
   }
}

//====================================================================
// MANAGEMENT (runs every tick)
//====================================================================
void ManageTrailingStops()
{
   if(!InpUseTrailingStop) return;

   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL) != _Symbol) continue;
      if((long)PositionGetInteger(POSITION_MAGIC) != (long)InpMagic) continue;

      long   ptype  = PositionGetInteger(POSITION_TYPE);
      double openPx = PositionGetDouble(POSITION_PRICE_OPEN);
      double curSL  = PositionGetDouble(POSITION_SL);
      double curTP  = PositionGetDouble(POSITION_TP);

      if(ptype == POSITION_TYPE_BUY)
      {
         double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
         double profitPips = (bid - openPx) / PipSize();
         if(profitPips >= InpTrailingStartPips)
         {
            double newSL = bid - InpTrailingStopPips * PipSize();
            if(newSL > curSL + InpTrailingStepPips * PipSize() || curSL == 0.0)
               trade.PositionModify(ticket, NormalizeDouble(newSL, _Digits), curTP);
         }
      }
      else if(ptype == POSITION_TYPE_SELL)
      {
         double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
         double profitPips = (openPx - ask) / PipSize();
         if(profitPips >= InpTrailingStartPips)
         {
            double newSL = ask + InpTrailingStopPips * PipSize();
            if((newSL < curSL - InpTrailingStepPips * PipSize()) || curSL == 0.0)
               trade.PositionModify(ticket, NormalizeDouble(newSL, _Digits), curTP);
         }
      }
   }
}

void ManageGridAdditions()
{
   if(g_basketDir == 0 || g_basketLevels <= 0) return;
   if(g_basketLevels >= InpGridMaxLevels) return;

   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);

   if(g_basketDir == 1)
   {
      double nextTrigger = g_basketEntryPx - g_basketLevels * InpGridStepPips * PipSize();
      if(bid <= nextTrigger) AddGridLevel(1);
   }
   else if(g_basketDir == -1)
   {
      double nextTrigger = g_basketEntryPx + g_basketLevels * InpGridStepPips * PipSize();
      if(ask >= nextTrigger) AddGridLevel(-1);
   }
}

void ManageBasketExit()
{
   if(g_basketDir == 0) return;
   if(InpGridTPMode == GRID_TP_PER_TRADE) return;

   double totalVolume, avgPrice, totalProfit; int count;
   GetBasketStats(g_basketDir, totalVolume, avgPrice, totalProfit, count);

   if(count == 0) { ClearBasketState(); return; }

   if(InpGridTPMode == GRID_TP_BASKET_MONEY)
   {
      if(InpGridBasketTakeProfitMoney > 0 && totalProfit >= InpGridBasketTakeProfitMoney)
      { CloseBasket(g_basketDir); return; }
      if(InpGridBasketStopLossMoney > 0 && totalProfit <= -InpGridBasketStopLossMoney)
      { CloseBasket(g_basketDir); return; }
   }
   else if(InpGridTPMode == GRID_TP_BASKET_PIPS)
   {
      double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
      double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
      if(g_basketDir == 1 && bid >= avgPrice + InpGridBasketTakeProfitPips * PipSize())
      { CloseBasket(g_basketDir); return; }
      if(g_basketDir == -1 && ask <= avgPrice - InpGridBasketTakeProfitPips * PipSize())
      { CloseBasket(g_basketDir); return; }
   }
}

//--- returns +1 = uptrend, -1 = downtrend, 0 = filter disabled / not enough data (no opinion)
int GetTrendDirection()
{
   if(!InpUseTrendFilter) return(0);
   if(g_trendMAHandle == INVALID_HANDLE) return(0);

   double maBuf[1];
   if(CopyBuffer(g_trendMAHandle, 0, 1, 1, maBuf) < 1) return(0);

   double refPrice = iClose(_Symbol, InpTrendMATimeframe, 1);
   if(refPrice <= 0.0) return(0);

   if(refPrice > maBuf[0]) return(+1);
   if(refPrice < maBuf[0]) return(-1);
   return(0);
}

//====================================================================
// SIGNAL CHECK (runs once per new closed bar)
//====================================================================
void CheckForNewSignal()
{
   if(!SpreadOK()) return;

   int dir = 0;
   if(!RecalcSignal(dir)) return;
   if(dir == 0) return;

   int trend = GetTrendDirection();
   if(InpUseTrendFilter && trend != 0 && trend != dir)
   {
      // signal fights the prevailing trend (e.g. a sell arrow during an uptrend) - skip it
      return;
   }

   if(dir == 1 && InpTradeDirection != DIR_SELL_ONLY)
   {
      if(g_basketDir == -1 && InpCloseOnOppositeSignal) CloseBasket(-1);
      if(g_basketDir != 1) OpenInitialTrade(1);
   }
   else if(dir == -1 && InpTradeDirection != DIR_BUY_ONLY)
   {
      if(g_basketDir == 1 && InpCloseOnOppositeSignal) CloseBasket(1);
      if(g_basketDir != -1) OpenInitialTrade(-1);
   }
}

//====================================================================
// EXPERT EVENTS
//====================================================================
int OnInit()
{
   g_point = SymbolInfoDouble(_Symbol, SYMBOL_POINT);
   int digits = (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);
   g_pip = (digits == 3 || digits == 5) ? g_point * 10.0 : g_point;

   g_confCandles = InpConfirmationCandles;
   if(g_confCandles < 1)  g_confCandles = 1;
   if(g_confCandles > 10) g_confCandles = 10;

   if(InpUseATRForSLTP)
   {
      g_atrHandle = iATR(_Symbol, _Period, InpATRPeriod);
      if(g_atrHandle == INVALID_HANDLE)
      {
         PrintFormat("SHK_HA_Grid_EA: failed to create ATR handle. Error=%d", GetLastError());
         return(INIT_FAILED);
      }
   }

   if(InpUseTrendFilter)
   {
      g_trendMAHandle = iMA(_Symbol, InpTrendMATimeframe, InpTrendMAPeriod, 0, InpTrendMAMethod, PRICE_CLOSE);
      if(g_trendMAHandle == INVALID_HANDLE)
      {
         PrintFormat("SHK_HA_Grid_EA: failed to create trend MA handle. Error=%d", GetLastError());
         return(INIT_FAILED);
      }
   }

   trade.SetExpertMagicNumber(InpMagic);
   trade.SetDeviationInPoints(InpSlippagePoints);
   trade.SetTypeFillingBySymbol(_Symbol);

   LoadBasketState();

   // reconcile with reality in case positions exist but global vars were lost
   // (e.g. terminal reinstalled) or vice versa
   double vol, avgPx, profit; int count;
   GetBasketStats(1, vol, avgPx, profit, count);
   if(count > 0 && g_basketDir != 1)
   {
      g_basketDir = 1; g_basketEntryPx = avgPx; g_basketLevels = MathMax(count, 1);
      SaveBasketState();
   }
   else
   {
      GetBasketStats(-1, vol, avgPx, profit, count);
      if(count > 0 && g_basketDir != -1)
      {
         g_basketDir = -1; g_basketEntryPx = avgPx; g_basketLevels = MathMax(count, 1);
         SaveBasketState();
      }
   }

   g_lastBarTime = iTime(_Symbol, _Period, 0);

   return(INIT_SUCCEEDED);
}

void OnDeinit(const int reason)
{
   if(g_atrHandle != INVALID_HANDLE)     IndicatorRelease(g_atrHandle);
   if(g_trendMAHandle != INVALID_HANDLE) IndicatorRelease(g_trendMAHandle);
}

void OnTick()
{
   // trailing stop and grid bookkeeping run every tick; the signal engine is
   // only re-evaluated once a bar has fully closed (non-repaint)
   ManageTrailingStops();

   if(InpGridEnabled)
   {
      ManageGridAdditions();
      ManageBasketExit();
   }

   if(IsNewBar())
      CheckForNewSignal();
}
//+------------------------------------------------------------------+
