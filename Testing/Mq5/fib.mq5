//+------------------------------------------------------------------+
//|                                           FibRetracementEA.mq5  |
//|                          Fibonacci 0.618 Retracement Entry EA   |
//|                                                                  |
//|  Strategy:                                                       |
//|   - Detects swing H/L using fractal logic + ATR size filter     |
//|   - Tracks up to 2 swings independently (nested FIBs)           |
//|   - Waits for price to sweep through 0.618, then confirmation   |
//|   - Confirmation = candle closes back past 0.618 in trend dir   |
//|     with minimum body % (no doji), within N bars of touch       |
//|   - SL: previous candle H/L OR fixed %                          |
//|   - Trade mgmt: Break-Even toggle, Close-on-Candle toggle       |
//+------------------------------------------------------------------+
#property copyright "FibRetracementEA"
#property version   "1.00"
#property strict

#include <Trade\Trade.mqh>
#include <Trade\PositionInfo.mqh>

CTrade         Trade;
CPositionInfo  PositionInfo;

//+------------------------------------------------------------------+
//| ENUMS                                                            |
//+------------------------------------------------------------------+
enum ENUM_SL_MODE
  {
   SL_PREV_CANDLE = 0,  // Previous Candle High/Low
   SL_FIXED_PCT   = 1   // Fixed Percentage
  };

enum ENUM_SWING_STATE
  {
   SS_IDLE        = 0,  // No active setup
   SS_TOUCHED     = 1,  // Price swept through 0.618
   SS_CONFIRMING  = 2,  // Waiting for confirmation candle
   SS_ENTERED     = 3,  // Trade is live
   SS_EXPIRED     = 4,  // Setup timed out or invalidated
  };

enum ENUM_SWING_DIR
  {
   DIR_NONE  = 0,
   DIR_UP    = 1,  // SwingLow → SwingHigh (expect long at 0.618)
   DIR_DOWN  = 2   // SwingHigh → SwingLow (expect short at 0.618)
  };

//+------------------------------------------------------------------+
//| INPUT PARAMETERS                                                 |
//+------------------------------------------------------------------+

// ── Swing Detection ──────────────────────────────────────────────
input group "=== Swing Detection ==="
input int    InpFractalBars    = 5;    // Fractal window: N bars each side
input double InpMinSwingATR    = 1.5;  // Min swing size (× ATR14) to qualify
input int    InpMaxRetraceBars = 20;   // Max bars retracement may take before momentum dies
input int    InpATRPeriod      = 14;   // ATR period for swing size filter

// ── Entry ─────────────────────────────────────────────────────────
input group "=== Entry Confirmation ==="
input int    InpMaxConfirmBars = 3;    // Max candles to wait for confirmation after touch
input double InpMinBodyPct     = 40.0; // Min confirmation candle body % of total range

// ── SL / TP ───────────────────────────────────────────────────────
input group "=== SL / TP ==="
input ENUM_SL_MODE InpSLMode  = SL_PREV_CANDLE; // SL mode
input double InpSLPct         = 0.30;  // SL % from entry (FIXED_PCT mode)
input double InpTPPct         = 0.60;  // TP % from entry (FIXED_PCT mode)

// ── Lot ───────────────────────────────────────────────────────────
input group "=== Lot Size ==="
input double InpLotSize        = 0.10; // Fixed lot size

// ── Trade Management ─────────────────────────────────────────────
input group "=== Trade Management ==="
input bool   InpBreakEven      = true;  // Enable Break-Even
input double InpBEPct          = 0.30;  // % profit to trigger BE
input bool   InpCloseOnCandle  = true;  // Close if candle closes against trade

//+------------------------------------------------------------------+
//| SWING STRUCT — one per tracked swing                             |
//+------------------------------------------------------------------+
struct SwingSetup
  {
   ENUM_SWING_DIR  dir;           // UP or DOWN
   double          swingLow;      // Swing low price
   double          swingHigh;     // Swing high price
   double          fib618;        // 0.618 retracement level
   datetime        swingTime;     // Time when swing was confirmed
   int             retraceBarCount; // Bars since swing confirmed (retracement speed)

   ENUM_SWING_STATE state;        // Current state machine state
   datetime         touchTime;    // Time of first 0.618 touch
   int              confirmBarsElapsed; // Bars elapsed since touch
   bool             priceWentThrough;   // True once price swept THROUGH 0.618

   ulong            tradeTicket;  // Open trade ticket (0 if none)
   double           entryPrice;   // Entry price of trade
   double           tradeDir;     // +1 long, -1 short
   bool             beTriggered;  // Break-even already moved

   void Reset()
     {
      dir                  = DIR_NONE;
      swingLow             = 0;
      swingHigh            = 0;
      fib618               = 0;
      swingTime            = 0;
      retraceBarCount      = 0;
      state                = SS_IDLE;
      touchTime            = 0;
      confirmBarsElapsed   = 0;
      priceWentThrough     = false;
      tradeTicket          = 0;
      entryPrice           = 0;
      tradeDir             = 0;
      beTriggered          = false;
     }
  };

//+------------------------------------------------------------------+
//| GLOBALS                                                          |
//+------------------------------------------------------------------+
SwingSetup  g_swing[2];          // Two independent swing trackers
datetime    g_lastBarTime = 0;   // Track bar open to detect new bar
int         g_atrHandle   = INVALID_HANDLE;

// Fractal detection cache
double g_fractalHigh[];
double g_fractalLow[];

// Pending fractal candidates (confirmed after InpFractalBars bars)
struct FractalCandidate
  {
   bool   valid;
   bool   isHigh;
   double price;
   datetime time;
   int    barIndex; // bar index when it was a candidate
  };

FractalCandidate g_pendingHigh;
FractalCandidate g_pendingLow;

// Most recently confirmed fractals (used to build swings)
double   g_lastFracHigh      = 0;
datetime g_lastFracHighTime  = 0;
double   g_lastFracLow       = 0;
datetime g_lastFracLowTime   = 0;
bool     g_lastWasHigh       = false; // true = last confirmed fractal was a high

//+------------------------------------------------------------------+
//| OnInit                                                           |
//+------------------------------------------------------------------+
int OnInit()
  {
   g_atrHandle = iATR(_Symbol, PERIOD_CURRENT, InpATRPeriod);
   if(g_atrHandle == INVALID_HANDLE)
     {
      Print("FibRetracementEA: Failed to create ATR handle");
      return INIT_FAILED;
     }

   for(int i = 0; i < 2; i++)
      g_swing[i].Reset();

   g_pendingHigh.valid = false;
   g_pendingLow.valid  = false;

   Trade.SetExpertMagicNumber(202600618);
   Trade.SetDeviationInPoints(30);
   Trade.SetTypeFilling(ORDER_FILLING_IOC);

   Print("FibRetracementEA v1.00 initialized on ", _Symbol, " ", EnumToString(Period()));
   return INIT_SUCCEEDED;
  }

//+------------------------------------------------------------------+
//| OnDeinit                                                         |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
  {
   if(g_atrHandle != INVALID_HANDLE)
      IndicatorRelease(g_atrHandle);
  }

//+------------------------------------------------------------------+
//| OnTick                                                           |
//+------------------------------------------------------------------+
void OnTick()
  {
   // ── New bar detection ────────────────────────────────────────
   datetime currentBarTime = iTime(_Symbol, PERIOD_CURRENT, 0);
   bool isNewBar = (currentBarTime != g_lastBarTime);
   if(isNewBar)
      g_lastBarTime = currentBarTime;

   // ── Get ATR ─────────────────────────────────────────────────
   double atrBuffer[];
   ArraySetAsSeries(atrBuffer, true);
   if(CopyBuffer(g_atrHandle, 0, 1, 3, atrBuffer) < 3)
      return;
   double atr = atrBuffer[1]; // Confirmed bar ATR
   if(atr <= 0) return;

   // ── On new bar: scan for new fractals, update swings ────────
   if(isNewBar)
     {
      ScanFractals(atr);
      UpdateSwingRetraceBars();
     }

   // ── Per-tick: manage states for both swings ──────────────────
   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   double midPrice = (ask + bid) / 2.0;

   for(int i = 0; i < 2; i++)
     {
      if(g_swing[i].state == SS_IDLE || g_swing[i].state == SS_EXPIRED)
         continue;

      if(g_swing[i].state == SS_ENTERED)
        {
         ManageTrade(i, isNewBar);
         continue;
        }

      // ── Check for swing invalidation ────────────────────────
      if(IsSwingInvalidated(i, midPrice))
        {
         Print("Swing[", i, "] INVALIDATED — price broke swing structure");
         g_swing[i].state = SS_EXPIRED;
         continue;
        }

      // ── TOUCHED state: check for sweep through 0.618 ────────
      if(g_swing[i].state == SS_TOUCHED)
        {
         if(isNewBar)
           {
            g_swing[i].confirmBarsElapsed++;
            if(g_swing[i].confirmBarsElapsed > InpMaxConfirmBars)
              {
               Print("Swing[", i, "] EXPIRED — confirmation window exceeded");
               g_swing[i].state = SS_EXPIRED;
               continue;
              }
           }
         // Check for confirmation candle on bar close
         if(isNewBar)
            CheckConfirmation(i);
        }

      // ── State: watching for first touch (price sweeps 0.618) ─
      if(g_swing[i].state == SS_CONFIRMING || g_swing[i].state == SS_TOUCHED)
         continue; // handled above

      // Default: watch for touch
      if(g_swing[i].state != SS_TOUCHED && g_swing[i].state != SS_CONFIRMING)
         CheckFor618Touch(i, midPrice);
     }
  }

//+------------------------------------------------------------------+
//| ScanFractals — called on each new bar                            |
//| Detects new fractal H/L using InpFractalBars window             |
//| Fractal is confirmed when InpFractalBars bars have elapsed       |
//+------------------------------------------------------------------+
void ScanFractals(double atr)
  {
   int totalBars = iBars(_Symbol, PERIOD_CURRENT);
   if(totalBars < InpFractalBars * 2 + 2)
      return;

   int checkBar = InpFractalBars + 1; // The bar that is now fully confirmed as fractal center

   double centerHigh = iHigh(_Symbol, PERIOD_CURRENT, checkBar);
   double centerLow  = iLow(_Symbol,  PERIOD_CURRENT, checkBar);

   // ── Check fractal high ───────────────────────────────────────
   bool isFracHigh = true;
   for(int j = 1; j <= InpFractalBars; j++)
     {
      if(iHigh(_Symbol, PERIOD_CURRENT, checkBar - j) >= centerHigh ||
         iHigh(_Symbol, PERIOD_CURRENT, checkBar + j) >= centerHigh)
        {
         isFracHigh = false;
         break;
        }
     }

   // ── Check fractal low ────────────────────────────────────────
   bool isFracLow = true;
   for(int j = 1; j <= InpFractalBars; j++)
     {
      if(iLow(_Symbol, PERIOD_CURRENT, checkBar - j) <= centerLow ||
         iLow(_Symbol, PERIOD_CURRENT, checkBar + j) <= centerLow)
        {
         isFracLow = false;
         break;
        }
     }

   datetime fracTime = iTime(_Symbol, PERIOD_CURRENT, checkBar);

   if(isFracHigh && centerHigh != g_lastFracHigh)
     {
      OnNewFractalHigh(centerHigh, fracTime, atr);
     }

   if(isFracLow && centerLow != g_lastFracLow)
     {
      OnNewFractalLow(centerLow, fracTime, atr);
     }
  }

//+------------------------------------------------------------------+
//| OnNewFractalHigh — a new fractal high was confirmed              |
//+------------------------------------------------------------------+
void OnNewFractalHigh(double price, datetime t, double atr)
  {
   // We need a preceding fractal low to form a swing
   if(g_lastFracLow > 0 && t > g_lastFracLowTime)
     {
      double swingSize = price - g_lastFracLow;
      if(swingSize >= InpMinSwingATR * atr)
        {
         // Valid swing LOW → HIGH (uptrend)
         RegisterSwing(DIR_UP, g_lastFracLow, price, g_lastFracLowTime);
        }
     }

   g_lastFracHigh     = price;
   g_lastFracHighTime = t;
   g_lastWasHigh      = true;
  }

//+------------------------------------------------------------------+
//| OnNewFractalLow — a new fractal low was confirmed                |
//+------------------------------------------------------------------+
void OnNewFractalLow(double price, datetime t, double atr)
  {
   // We need a preceding fractal high to form a swing
   if(g_lastFracHigh > 0 && t > g_lastFracHighTime)
     {
      double swingSize = g_lastFracHigh - price;
      if(swingSize >= InpMinSwingATR * atr)
        {
         // Valid swing HIGH → LOW (downtrend)
         RegisterSwing(DIR_DOWN, price, g_lastFracHigh, g_lastFracHighTime);
        }
     }

   g_lastFracLow     = price;
   g_lastFracLowTime = t;
   g_lastWasHigh     = false;
  }

//+------------------------------------------------------------------+
//| RegisterSwing — assign a new swing to an available slot          |
//+------------------------------------------------------------------+
void RegisterSwing(ENUM_SWING_DIR dir, double low, double high, datetime swingTime)
  {
   // Find a free or expired slot
   int slot = -1;
   for(int i = 0; i < 2; i++)
     {
      if(g_swing[i].state == SS_IDLE || g_swing[i].state == SS_EXPIRED)
        {
         slot = i;
         break;
        }
     }

   // If no free slot, overwrite the older one
   if(slot == -1)
     {
      slot = (g_swing[0].swingTime <= g_swing[1].swingTime) ? 0 : 1;
      // Don't overwrite an active trade
      if(g_swing[slot].state == SS_ENTERED)
        {
         slot = 1 - slot;
         if(g_swing[slot].state == SS_ENTERED)
            return; // Both slots have active trades, skip
        }
     }

   double fib618;
   if(dir == DIR_UP)
      fib618 = high - 0.618 * (high - low);  // Retracement from high
   else
      fib618 = low  + 0.618 * (high - low);  // Retracement from low (upward)

   g_swing[slot].Reset();
   g_swing[slot].dir             = dir;
   g_swing[slot].swingLow        = low;
   g_swing[slot].swingHigh       = high;
   g_swing[slot].fib618          = fib618;
   g_swing[slot].swingTime       = swingTime;
   g_swing[slot].retraceBarCount = 0;
   g_swing[slot].state           = SS_CONFIRMING; // Waiting for price to reach 0.618

   Print("Swing[", slot, "] registered | Dir=", (dir == DIR_UP ? "UP" : "DOWN"),
         " | Low=", DoubleToString(low, _Digits),
         " | High=", DoubleToString(high, _Digits),
         " | FIB0.618=", DoubleToString(fib618, _Digits));
  }

//+------------------------------------------------------------------+
//| UpdateSwingRetraceBars — track how long retracement is taking    |
//+------------------------------------------------------------------+
void UpdateSwingRetraceBars()
  {
   for(int i = 0; i < 2; i++)
     {
      if(g_swing[i].state == SS_CONFIRMING)
        {
         g_swing[i].retraceBarCount++;
         if(g_swing[i].retraceBarCount > InpMaxRetraceBars)
           {
            Print("Swing[", i, "] dead momentum — retracement took too long (", InpMaxRetraceBars, " bars)");
            g_swing[i].state = SS_EXPIRED;
           }
        }
     }
  }

//+------------------------------------------------------------------+
//| CheckFor618Touch — detect when price sweeps through 0.618       |
//+------------------------------------------------------------------+
void CheckFor618Touch(int idx, double price)
  {
   if(g_swing[idx].state != SS_CONFIRMING) return;

   double fib = g_swing[idx].fib618;
   ENUM_SWING_DIR dir = g_swing[idx].dir;

   // For uptrend: price should dip TO or BELOW fib618
   // For downtrend: price should push TO or ABOVE fib618
   bool touched = false;
   if(dir == DIR_UP   && price <= fib) touched = true;
   if(dir == DIR_DOWN && price >= fib) touched = true;

   if(touched)
     {
      g_swing[idx].state               = SS_TOUCHED;
      g_swing[idx].touchTime           = TimeCurrent();
      g_swing[idx].confirmBarsElapsed  = 0;
      g_swing[idx].priceWentThrough    = true;
      Print("Swing[", idx, "] 0.618 TOUCHED at price=", DoubleToString(price, _Digits),
            " fib=", DoubleToString(fib, _Digits));
     }
  }

//+------------------------------------------------------------------+
//| CheckConfirmation — called on new bar close after touch         |
//| Looks at the just-closed bar (index 1)                          |
//+------------------------------------------------------------------+
void CheckConfirmation(int idx)
  {
   if(g_swing[idx].state != SS_TOUCHED) return;

   double fib  = g_swing[idx].fib618;
   ENUM_SWING_DIR dir = g_swing[idx].dir;

   double barOpen  = iOpen (_Symbol, PERIOD_CURRENT, 1);
   double barClose = iClose(_Symbol, PERIOD_CURRENT, 1);
   double barHigh  = iHigh (_Symbol, PERIOD_CURRENT, 1);
   double barLow   = iLow  (_Symbol, PERIOD_CURRENT, 1);
   double barRange = barHigh - barLow;

   if(barRange <= 0) return;

   double bodySize = MathAbs(barClose - barOpen);
   double bodyPct  = (bodySize / barRange) * 100.0;

   bool isBullish = (barClose > barOpen);
   bool isBearish = (barClose < barOpen);

   // Body filter
   if(bodyPct < InpMinBodyPct)
     {
      Print("Swing[", idx, "] Candle body too small (", DoubleToString(bodyPct, 1), "% < ",
            DoubleToString(InpMinBodyPct, 1), "%) — waiting");
      return;
     }

   bool confirmed = false;

   if(dir == DIR_UP)
     {
      // Need: bullish candle, closed ABOVE fib618, high above fib618
      if(isBullish && barClose > fib && barHigh > fib)
         confirmed = true;
     }
   else if(dir == DIR_DOWN)
     {
      // Need: bearish candle, closed BELOW fib618, low below fib618
      if(isBearish && barClose < fib && barLow < fib)
         confirmed = true;
     }

   if(confirmed)
     {
      Print("Swing[", idx, "] CONFIRMED — entering trade");
      EnterTrade(idx);
     }
  }

//+------------------------------------------------------------------+
//| EnterTrade — execute market order for confirmed setup            |
//+------------------------------------------------------------------+
void EnterTrade(int idx)
  {
   ENUM_SWING_DIR dir = g_swing[idx].dir;
   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);

   double entryPrice = (dir == DIR_UP) ? ask : bid;
   double sl = 0, tp = 0;

   double prevHigh = iHigh(_Symbol, PERIOD_CURRENT, 1);
   double prevLow  = iLow (_Symbol, PERIOD_CURRENT, 1);

   if(InpSLMode == SL_PREV_CANDLE)
     {
      if(dir == DIR_UP)
        {
         sl = prevLow;
         tp = 0; // TP not used in prev candle mode — user manages or close on candle
        }
      else
        {
         sl = prevHigh;
         tp = 0;
        }
     }
   else // FIXED_PCT
     {
      if(dir == DIR_UP)
        {
         sl = entryPrice * (1.0 - InpSLPct / 100.0);
         tp = entryPrice * (1.0 + InpTPPct / 100.0);
        }
      else
        {
         sl = entryPrice * (1.0 + InpSLPct / 100.0);
         tp = entryPrice * (1.0 - InpTPPct / 100.0);
        }
     }

   // Normalize prices
   int digits = (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);
   sl = NormalizeDouble(sl, digits);
   tp = NormalizeDouble(tp, digits);

   bool result = false;
   if(dir == DIR_UP)
      result = Trade.Buy(InpLotSize, _Symbol, entryPrice, sl, tp,
                         "FibEA_Long_Swing" + IntegerToString(idx));
   else
      result = Trade.Sell(InpLotSize, _Symbol, entryPrice, sl, tp,
                          "FibEA_Short_Swing" + IntegerToString(idx));

   if(result)
     {
      g_swing[idx].tradeTicket = Trade.ResultOrder();
      g_swing[idx].entryPrice  = entryPrice;
      g_swing[idx].tradeDir    = (dir == DIR_UP) ? 1.0 : -1.0;
      g_swing[idx].beTriggered = false;
      g_swing[idx].state       = SS_ENTERED;
      Print("Swing[", idx, "] Trade ENTERED | Ticket=", g_swing[idx].tradeTicket,
            " | Entry=", DoubleToString(entryPrice, digits),
            " | SL=", DoubleToString(sl, digits),
            " | TP=", DoubleToString(tp, digits));
     }
   else
     {
      Print("Swing[", idx, "] Trade FAILED | Error=", GetLastError(),
            " | Retcode=", Trade.ResultRetcode(), " ", Trade.ResultRetcodeDescription());
      g_swing[idx].state = SS_EXPIRED;
     }
  }

//+------------------------------------------------------------------+
//| ManageTrade — break-even and close-on-candle                    |
//+------------------------------------------------------------------+
void ManageTrade(int idx, bool isNewBar)
  {
   ulong ticket = g_swing[idx].tradeTicket;
   if(ticket == 0) return;

   // Check if position still open
   if(!PositionSelectByTicket(ticket))
     {
      Print("Swing[", idx, "] Position closed (external or SL/TP) — resetting slot");
      g_swing[idx].state = SS_EXPIRED;
      return;
     }

   double entryPrice = g_swing[idx].entryPrice;
   double tradeDir   = g_swing[idx].tradeDir;
   double currentSL  = PositionGetDouble(POSITION_SL);
   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   double currentPrice = (tradeDir > 0) ? bid : ask; // Bid for long, ask for short
   int digits = (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);

   // ── Break-Even ──────────────────────────────────────────────
   if(InpBreakEven && !g_swing[idx].beTriggered)
     {
      double profitPct = (tradeDir > 0)
                         ? ((currentPrice - entryPrice) / entryPrice) * 100.0
                         : ((entryPrice - currentPrice) / entryPrice) * 100.0;

      if(profitPct >= InpBEPct)
        {
         double newSL = NormalizeDouble(entryPrice, digits);
         // Only move SL if it improves position
         bool slImproves = (tradeDir > 0) ? (newSL > currentSL) : (newSL < currentSL);
         if(slImproves)
           {
            double currentTP = PositionGetDouble(POSITION_TP);
            if(Trade.PositionModify(ticket, newSL, currentTP))
              {
               g_swing[idx].beTriggered = true;
               Print("Swing[", idx, "] Break-Even triggered at entry=", DoubleToString(entryPrice, digits));
              }
           }
        }
     }

   // ── Close on Candle Close (against trade direction) ─────────
   if(InpCloseOnCandle && isNewBar)
     {
      double prevOpen  = iOpen (_Symbol, PERIOD_CURRENT, 1);
      double prevClose = iClose(_Symbol, PERIOD_CURRENT, 1);

      bool closeAgainstLong  = (tradeDir > 0 && prevClose < prevOpen); // bearish candle on long
      bool closeAgainstShort = (tradeDir < 0 && prevClose > prevOpen); // bullish candle on short

      if(closeAgainstLong || closeAgainstShort)
        {
         Print("Swing[", idx, "] Candle closed against trade — closing position");
         if(Trade.PositionClose(ticket))
            g_swing[idx].state = SS_EXPIRED;
        }
     }
  }

//+------------------------------------------------------------------+
//| IsSwingInvalidated — price broke beyond swing structure          |
//+------------------------------------------------------------------+
bool IsSwingInvalidated(int idx, double price)
  {
   if(g_swing[idx].state == SS_IDLE || g_swing[idx].state == SS_EXPIRED)
      return false;

   ENUM_SWING_DIR dir = g_swing[idx].dir;

   // For uptrend setup: if price breaks BELOW swing low, structure is broken
   if(dir == DIR_UP && price < g_swing[idx].swingLow)
      return true;

   // For downtrend setup: if price breaks ABOVE swing high, structure is broken
   if(dir == DIR_DOWN && price > g_swing[idx].swingHigh)
      return true;

   return false;
  }

//+------------------------------------------------------------------+
//| OnTradeTransaction — detect external position close              |
//+------------------------------------------------------------------+
void OnTradeTransaction(const MqlTradeTransaction& trans,
                        const MqlTradeRequest&     request,
                        const MqlTradeResult&      result)
  {
   if(trans.type == TRADE_TRANSACTION_DEAL_ADD)
     {
      for(int i = 0; i < 2; i++)
        {
         if(g_swing[i].state == SS_ENTERED && g_swing[i].tradeTicket != 0)
           {
            // If the position no longer exists, mark as expired
            if(!PositionSelectByTicket(g_swing[i].tradeTicket))
              {
               Print("Swing[", i, "] Position closed via transaction — slot reset");
               g_swing[i].state = SS_EXPIRED;
              }
           }
        }
     }
  }
//+------------------------------------------------------------------+