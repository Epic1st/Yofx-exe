//+------------------------------------------------------------------+
//|                                             MonsterArrows_V3.mq5 |
//|                                  Copyright 2026, MetaQuotes Ltd. |
//|                                             https://www.mql5.com |
//+------------------------------------------------------------------+


//+------------------------------------------------------------------+
//|                                             MonsterArrows_V2.mq5 |
//|                    Monster Arrows V2 - Complete Rewrite          |
//+------------------------------------------------------------------+
#property copyright   "Monster Arrows V2"
#property version     "2.0"
#property indicator_chart_window
#property indicator_buffers 2
#property indicator_plots   2

#property indicator_label1  "Buy Signal"
#property indicator_type1   DRAW_ARROW
#property indicator_color1  clrLime
#property indicator_style1  STYLE_SOLID
#property indicator_width1  4

#property indicator_label2  "Sell Signal"
#property indicator_type2   DRAW_ARROW
#property indicator_color2  clrRed
#property indicator_style2  STYLE_SOLID
#property indicator_width2  4

//=== FILTERS & CONFIRMATION ===
input group "=== FILTERS & CONFIRMATION ==="
input bool   EnableHTFFilter     = true;  // Enable Higher Timeframe Confluence Filter
input bool   RequireBothHTF      = true;  // Require BOTH HTF confirmations (Strict Mode)
input int    BarsToConfirm       = 2;     // Bars to wait to confirm signal (Non-Repaint)
input int    HTF_EMAPeriod       = 20;    // HTF EMA Period
input int    ST_RMA1Length       = 14;    // SuperTrend RMA 1 Length
input int    ST_RMA2Length       = 21;    // SuperTrend RMA 2 Length
input int    ST_ATRPeriod        = 14;    // SuperTrend ATR Period
input double ST_ATRMult          = 1.0;   // SuperTrend ATR Multiplier

//=== SMART MONEY CONCEPTS ===
input group "=== SMART MONEY CONCEPTS (SMC) ==="
input bool   UseLiquiditySweep   = true;          // Liquidity Sweep (Stop Hunt)
input bool   UseFairValueGap     = true;          // Fair Value Gap (Displacement)
input bool   ShowSMCLines        = true;          // Show SMC Lines & Boxes
input color  FVGBoxColorBuy      = clrDarkGreen;  // FVG Box color for Buy
input color  FVGBoxColorSell     = clrDarkRed;    // FVG Box color for Sell
input color  HuntLineColorBuy    = clrAqua;       // Hunt line color for Buy
input color  HuntLineColorSell   = clrMagenta;    // Hunt line color for Sell

//=== ZIGZAG SETTINGS ===
input group "=== ZIGZAG SETTINGS ==="
input int    ZZDepth             = 30;    // ZigZag Depth
input int    ZZDeviation         = 5;     // ZigZag Deviation
input int    ZZBackstep          = 3;     // ZigZag Backstep
input double FibLevel            = 0.618; // Fibonacci Retracement Level
input int    HistBars            = 1000;  // Number of historical bars to analyze

//=== DAILY ATR TARGETS ===
input group "=== DAILY ATR TARGETS ==="
input bool             ShowDailyATR  = true;       // Show Daily ATR Lines
input int              ATRPeriod     = 14;          // ATR Period
input color            ATRHighColor  = clrAqua;     // Daily ATR High Color
input color            ATRLowColor   = clrMagenta;  // Daily ATR Low Color
input ENUM_LINE_STYLE  ATRLineStyle  = STYLE_DASH;  // Daily ATR Line Style

//=== WEEKLY ATR TARGETS ===
input group "=== WEEKLY ATR TARGETS ==="
input bool             ShowWeeklyATR   = true;          // Show Weekly ATR Lines
input color            WkATRHighColor  = clrDodgerBlue; // Weekly ATR High Color
input color            WkATRLowColor   = clrOrange;     // Weekly ATR Low Color
input ENUM_LINE_STYLE  WkATRLineStyle  = STYLE_DASH;    // Weekly ATR Line Style

enum ENUM_ENTRY_REF
{
   CANDLE_A = 0,  // Candle A — Sweep candle close
   CANDLE_B = 1,  // Candle B — 1st confirmation candle close
   CANDLE_C = 2   // Candle C — 2nd confirmation candle close
};

//=== AUTO TP / SL SETTINGS ===
input group "=== AUTO TP SETTINGS ==="
input bool             DrawTPLines   = true;        // Draw TP/SL Lines for signal
input ENUM_ENTRY_REF   EntryCandle   = CANDLE_A;    // Entry Price Reference Candle
input color            EntryColor    = clrYellow;   // Entry line color
input color            SLColor       = clrCrimson;  // Stop Loss line color
input color            TP1Color      = clrLime;     // TP1 line color
input color            TP2Color      = clrOrange;   // TP2 line color
input color            TP3Color      = clrRed;      // TP3 line color
input int              LevelWidth    = 2;            // Line width
input ENUM_LINE_STYLE  LevelStyle    = STYLE_DASH;  // Line style
input double           SL_ATR_Mult   = 1.5;         // SL distance (ATR multiplier)
input double           TP1_ATR_Mult  = 1.5;         // TP1 distance (ATR multiplier)
input double           TP2_ATR_Mult  = 3.0;         // TP2 distance (ATR multiplier)
input double           TP3_ATR_Mult  = 6.0;         // TP3 distance (ATR multiplier)

//=== DASHBOARD & ALERTS ===
input group "=== DASHBOARD & ALERTS ==="
input bool   ShowPanel      = true;         // Show Info Panel
input int    PanelX         = 20;           // Horizontal position
input int    PanelY         = 20;           // Vertical position
input color  DashTitleColor = clrGold;      // Title color
input color  DashTextColor  = clrWhite;     // Text color
input bool   MasterAlert    = false;         // Master Alert Switch
input bool   DoPopup        = false;         // Popup Alert
input bool   DoSound        = false;         // Sound Alert
input bool   DoEmail        = false;        // Email Alert
input bool   DoPush         = false;         // Mobile Push Notification
input string AlertSound     = "alert2.wav"; // Sound file
input int    AlertCooldown  = 60;           // Seconds between alerts

//=== ARROW VISUALS ===
input group "=== ARROW VISUALS ==="
input int    ArrowSize      = 4;     // Arrow size (1-5)
input color  BuyArrowColor  = clrLime; // Buy arrow color
input color  SellArrowColor = clrRed;  // Sell arrow color
input int    ArrowGap       = 15;    // Arrow distance from candle (points)
input int    BuyArrowCode   = 233;   // Buy arrow symbol code
input int    SellArrowCode  = 234;   // Sell arrow symbol code

//=== MISC ===
input group "=== MISC SETTINGS ==="
input int    RecentBars     = 50;    // Bars to scan for missed signals

//--- Indicator buffers (NOT as-series — forward indexed like price arrays)
double BuyBuf[];
double SellBuf[];

// Trigger type per bar: 1=Sweep, 2=FVG, 3=Both — stored parallel to signal buffers
double TriggerBuf[]; // not a plot buffer, just internal tracking

//--- Indicator handles
int hATR       = INVALID_HANDLE;
int hATRDaily  = INVALID_HANDLE;
int hATRWeekly = INVALID_HANDLE;  // Weekly ATR
int hHTF_EMA   = INVALID_HANDLE;
int hHTF_ATR   = INVALID_HANDLE;

//--- SuperTrend RMA state (computed on HTF bars)
double g_ST_rma1_hlc3v = 0, g_ST_rma1_vol = 0;
double g_ST_rma2_hlc3v = 0, g_ST_rma2_vol = 0;
bool   g_ST_initialized = false;
double g_ST_trendDir    = 1.0;   // last known SuperTrend direction on HTF

//--- State
datetime g_LastAlert = 0;

//+------------------------------------------------------------------+
int OnInit()
{
   SetIndexBuffer(0, BuyBuf,  INDICATOR_DATA);
   SetIndexBuffer(1, SellBuf, INDICATOR_DATA);

   // Keep buffers forward-indexed (NOT as-series)
   ArraySetAsSeries(BuyBuf,     false);
   ArraySetAsSeries(SellBuf,    false);
   ArraySetAsSeries(TriggerBuf, false);

   PlotIndexSetInteger(0, PLOT_ARROW,      BuyArrowCode);
   PlotIndexSetInteger(1, PLOT_ARROW,      SellArrowCode);
   PlotIndexSetInteger(0, PLOT_LINE_COLOR, BuyArrowColor);
   PlotIndexSetInteger(1, PLOT_LINE_COLOR, SellArrowColor);
   PlotIndexSetInteger(0, PLOT_LINE_WIDTH, ArrowSize);
   PlotIndexSetInteger(1, PLOT_LINE_WIDTH, ArrowSize);
   PlotIndexSetDouble(0,  PLOT_EMPTY_VALUE, 0.0);
   PlotIndexSetDouble(1,  PLOT_EMPTY_VALUE, 0.0);

   hATR       = iATR(_Symbol, _Period,      ATRPeriod);
   hATRDaily  = iATR(_Symbol, PERIOD_D1,    ATRPeriod);
   hATRWeekly = iATR(_Symbol, PERIOD_W1,    ATRPeriod);
   hHTF_EMA   = iMA (_Symbol, GetHTF(_Period), HTF_EMAPeriod, 0, MODE_EMA, PRICE_CLOSE);
   hHTF_ATR   = iATR(_Symbol, GetHTF(_Period), ST_ATRPeriod);

   if(hATR == INVALID_HANDLE || hATRDaily == INVALID_HANDLE ||
      hATRWeekly == INVALID_HANDLE ||
      hHTF_EMA == INVALID_HANDLE || hHTF_ATR == INVALID_HANDLE)
   {
      Print("ERROR: Handle creation failed");
      return INIT_FAILED;
   }

   g_ST_initialized = false;
   g_ST_trendDir    = 1.0;

   IndicatorSetString(INDICATOR_SHORTNAME, "MONSTER ARROWS V2");
   return INIT_SUCCEEDED;
}

//+------------------------------------------------------------------+
ENUM_TIMEFRAMES GetHTF(ENUM_TIMEFRAMES tf)
{
   switch(tf)
   {
      case PERIOD_M1:  return PERIOD_M15;
      case PERIOD_M5:  return PERIOD_H1;
      case PERIOD_M15: return PERIOD_H1;
      case PERIOD_M30: return PERIOD_H4;
      case PERIOD_H1:  return PERIOD_H4;
      case PERIOD_H4:  return PERIOD_D1;
      default:         return PERIOD_W1;
   }
}

//+------------------------------------------------------------------+
int OnCalculate(const int rates_total,
                const int prev_calculated,
                const datetime &time[],
                const double   &open[],
                const double   &high[],
                const double   &low[],
                const double   &close[],
                const long     &tick_volume[],
                const long     &volume[],
                const int      &spread[])
{
   if(rates_total < ZZDepth + BarsToConfirm + 10)
      return 0;

   // Resize internal trigger tracking buffer
   if(ArraySize(TriggerBuf) < rates_total)
   {
      ArrayResize(TriggerBuf, rates_total);
      ArrayInitialize(TriggerBuf, 0);
   }

   // ----------------------------------------------------------------
   // Build ATR array in FORWARD order matching price arrays
   // CopyBuffer returns as-series (newest first), so we reverse it
   // ----------------------------------------------------------------
   double atr[];
   ArrayResize(atr, rates_total);
   {
      double tmp[];
      ArraySetAsSeries(tmp, true);
      int copied = CopyBuffer(hATR, 0, 0, rates_total, tmp);
      if(copied <= 0) return prev_calculated;
      // Reverse: tmp[0]=newest => atr[rates_total-1]=newest
      for(int k = 0; k < rates_total; k++)
         atr[k] = tmp[rates_total - 1 - k];
   }

   // Daily ATR for reference lines
   double dailyATR = 0;
   {
      double tmp[];
      ArraySetAsSeries(tmp, true);
      if(CopyBuffer(hATRDaily, 0, 0, 1, tmp) > 0)
         dailyATR = tmp[0];
   }

   // Daily ATR high/low lines — start at today's open time, ray right
   if(ShowDailyATR && dailyATR > 0)
   {
      double   dayOpen = iOpen (_Symbol, PERIOD_D1, 0);
      datetime dayTime = iTime (_Symbol, PERIOD_D1, 0);
      SetLevelLine("MA_ATR_High", dayTime, dayOpen + dailyATR, ATRHighColor, ATRLineStyle, 1);
      SetLevelLine("MA_ATR_Low",  dayTime, dayOpen - dailyATR, ATRLowColor,  ATRLineStyle, 1);

      // Labels near current candle
      datetime tLabel = time[rates_total-1] + (datetime)(PeriodSeconds(_Period) * 3);
      DrawLineLabel("MA_ATR_Lbl_High", tLabel, dayOpen + dailyATR, ATRHighColor, "  D-ATR High");
      DrawLineLabel("MA_ATR_Lbl_Low",  tLabel, dayOpen - dailyATR, ATRLowColor,  "  D-ATR Low");
   }

   // Weekly ATR for reference lines
   double weeklyATR = 0;
   {
      double tmp[];
      ArraySetAsSeries(tmp, true);
      if(CopyBuffer(hATRWeekly, 0, 0, 1, tmp) > 0)
         weeklyATR = tmp[0];
   }

   // Weekly ATR high/low lines — start at this week's open time, ray right
   if(ShowWeeklyATR && weeklyATR > 0)
   {
      double   weekOpen = iOpen (_Symbol, PERIOD_W1, 0);
      datetime weekTime = iTime (_Symbol, PERIOD_W1, 0);
      SetLevelLine("MA_WATR_High", weekTime, weekOpen + weeklyATR, WkATRHighColor, WkATRLineStyle, 1);
      SetLevelLine("MA_WATR_Low",  weekTime, weekOpen - weeklyATR, WkATRLowColor,  WkATRLineStyle, 1);

      // Labels near current candle
      datetime tLabel = time[rates_total-1] + (datetime)(PeriodSeconds(_Period) * 3);
      DrawLineLabel("MA_WATR_Lbl_High", tLabel, weekOpen + weeklyATR, WkATRHighColor, "  W-ATR High");
      DrawLineLabel("MA_WATR_Lbl_Low",  tLabel, weekOpen - weeklyATR, WkATRLowColor,  "  W-ATR Low");
   }

   // ----------------------------------------------------------------
   // HTF BIAS — TWO FILTERS MUST BOTH AGREE
   //
   // Filter 1: 20 EMA on HTF
   //   BULL = HTF close > EMA AND EMA rising
   //   BEAR = HTF close < EMA AND EMA falling
   //
   // Filter 2: SuperTrend Dual RMA arrow direction on HTF
   //   BULL = last SuperTrend flip was -1.0 → 1.0 (buy arrow)
   //   BEAR = last SuperTrend flip was  1.0 → -1.0 (sell arrow)
   //
   // HTF BULLISH = Filter1 Bull AND Filter2 Bull
   // HTF BEARISH = Filter1 Bear AND Filter2 Bear
   // ----------------------------------------------------------------
   bool htfBull = false, htfBear = false;
   bool ema20Bull = false, ema20Bear = false;
   bool stBull = false, stBear = false;
   if(EnableHTFFilter)
   {
      ENUM_TIMEFRAMES htfTF = GetHTF(_Period);

      // --- Filter 1: HTF 20 EMA ---
      {
         double emaBuf[];
         ArraySetAsSeries(emaBuf, true);
         double htfClose[];
         ArraySetAsSeries(htfClose, true);

         if(CopyBuffer(hHTF_EMA, 0, 0, 3, emaBuf) == 3 &&
            CopyClose(_Symbol, htfTF, 0, 3, htfClose) == 3)
         {
            ema20Bull = (htfClose[0] > emaBuf[0] && emaBuf[0] > emaBuf[1]);
            ema20Bear = (htfClose[0] < emaBuf[0] && emaBuf[0] < emaBuf[1]);
         }
      }

      // --- Filter 2: SuperTrend Dual RMA direction on HTF ---
      // Compute RMA1 & RMA2 on HTF bars, derive trendDir, track last arrow flip
      {
         int htfBars = MathMax(ST_RMA1Length, ST_RMA2Length) + ST_ATRPeriod + 50;

         double htfHigh[], htfLow[], htfClose[], htfATR[], htfVol[];
         ArraySetAsSeries(htfHigh,  true);
         ArraySetAsSeries(htfLow,   true);
         ArraySetAsSeries(htfClose, true);
         ArraySetAsSeries(htfATR,   true);

         int copied = CopyHigh (_Symbol, htfTF, 0, htfBars, htfHigh);
         copied    += CopyLow  (_Symbol, htfTF, 0, htfBars, htfLow);
         copied    += CopyClose(_Symbol, htfTF, 0, htfBars, htfClose);
         copied    += CopyBuffer(hHTF_ATR, 0, 0, htfBars, htfATR);

         if(copied == htfBars * 4)
         {
            // Reverse to forward order for RMA computation
            int n = htfBars;
            double rma1_hlc3v = 0, rma1_vol = 0;
            double rma2_hlc3v = 0, rma2_vol = 0;
            bool   rmaInit    = false;
            double prevTrend  = 1.0;
            double lastFlipDir = g_ST_trendDir;

            // Store final bar RMA values to check RMA1 vs RMA2 alignment
            double finalRMA1 = 0, finalRMA2 = 0;

            for(int k = n - 1; k >= 0; k--)
            {
               double vol   = 1.0;
               double hlc3  = (htfHigh[k] + htfLow[k] + htfClose[k]) / 3.0;
               double hlc3v = hlc3 * vol;

               if(!rmaInit)
               {
                  rma1_hlc3v = hlc3v; rma1_vol = vol;
                  rma2_hlc3v = hlc3v; rma2_vol = vol;
                  rmaInit = true;
               }
               else
               {
                  rma1_hlc3v = (rma1_hlc3v * (ST_RMA1Length - 1) + hlc3v) / ST_RMA1Length;
                  rma1_vol   = (rma1_vol   * (ST_RMA1Length - 1) + vol)   / ST_RMA1Length;
                  rma2_hlc3v = (rma2_hlc3v * (ST_RMA2Length - 1) + hlc3v) / ST_RMA2Length;
                  rma2_vol   = (rma2_vol   * (ST_RMA2Length - 1) + vol)   / ST_RMA2Length;
               }

               double rma1val  = (rma1_vol != 0) ? rma1_hlc3v / rma1_vol : hlc3;
               double rma2val  = (rma2_vol != 0) ? rma2_hlc3v / rma2_vol : hlc3;
               double basis    = (rma1val + rma2val) / 2.0;
               double atrVal   = htfATR[k];
               double upper    = basis + ST_ATRMult * atrVal;
               double lower    = basis - ST_ATRMult * atrVal;

               double trendDir;
               if(htfClose[k] > upper)       trendDir =  1.0;
               else if(htfClose[k] < lower)  trendDir = -1.0;
               else                           trendDir = prevTrend;

               // Track arrow flips
               if(k < n - 1)
               {
                  bool bullFlip = (prevTrend == -1.0 && trendDir ==  1.0);
                  bool bearFlip = (prevTrend ==  1.0 && trendDir == -1.0);
                  if(bullFlip) lastFlipDir =  1.0;
                  if(bearFlip) lastFlipDir = -1.0;
               }

               prevTrend = trendDir;

               // Capture the most recent bar's RMA values (k=0 = newest HTF bar)
               if(k == 0) { finalRMA1 = rma1val; finalRMA2 = rma2val; }
            }

            // Save flip direction for next tick
            g_ST_trendDir = lastFlipDir;

            // ST BULLISH = last arrow was BUY flip AND RMA1 currently above RMA2
            // ST BEARISH = last arrow was SELL flip AND RMA1 currently below RMA2
            bool rma1AboveRMA2 = (finalRMA1 > finalRMA2);
            bool rma1BelowRMA2 = (finalRMA1 < finalRMA2);

            stBull = (lastFlipDir ==  1.0 && rma1AboveRMA2);
            stBear = (lastFlipDir == -1.0 && rma1BelowRMA2);
         }
      }

      // --- Combine both filters ---
      htfBull = (ema20Bull && stBull);
      htfBear = (ema20Bear && stBear);
   }

   // ----------------------------------------------------------------
   // Determine which bars to recalculate
   // ----------------------------------------------------------------
   int startBar;
   if(prev_calculated <= 0)
      startBar = MathMax(ZZDepth + BarsToConfirm + 2, rates_total - HistBars);
   else
      startBar = MathMax(ZZDepth + BarsToConfirm + 2, prev_calculated - RecentBars - BarsToConfirm - 5);

   // ----------------------------------------------------------------
   // Main signal detection loop — ALL arrays are FORWARD indexed
   // index 0 = oldest bar, index rates_total-1 = newest bar
   // ----------------------------------------------------------------
   for(int i = startBar; i <= rates_total - BarsToConfirm - 2; i++)
   {
      BuyBuf[i]  = 0.0;
      SellBuf[i] = 0.0;

      if(i < ZZDepth + 2) continue;

      double curATR = atr[i];
      if(curATR <= 0) continue;

      // ----------------------------------------------------------------
      // Find the most recent CONFIRMED ZigZag pivot high and pivot low
      // before bar i — these are where stop clusters actually sit
      // A pivot low  = bar whose low  is lower  than ZZDepth bars each side
      // A pivot high = bar whose high is higher than ZZDepth bars each side
      // We only look LEFT (no future bars) so no lookahead bias
      // ----------------------------------------------------------------
      double pivotLow  = 0, pivotHigh = 0;
      int    pivotLowBar = -1, pivotHighBar = -1;

      int scanFrom = i - 1; // look left only — no lookahead
      int scanTo   = MathMax(ZZDepth + 1, i - HistBars);

      for(int k = scanFrom; k >= scanTo; k--)
      {
         // Check pivot low — lowest point within ZZDepth bars to its left
         if(pivotLowBar < 0)
         {
            bool isPivLow = true;
            int  leftLim  = MathMax(0, k - ZZDepth);
            for(int m = leftLim; m < k; m++)
               if(low[m] <= low[k]) { isPivLow = false; break; }
            if(isPivLow)
            {
               pivotLow    = low[k];
               pivotLowBar = k;
            }
         }

         // Check pivot high — highest point within ZZDepth bars to its left
         if(pivotHighBar < 0)
         {
            bool isPivHigh = true;
            int  leftLim   = MathMax(0, k - ZZDepth);
            for(int m = leftLim; m < k; m++)
               if(high[m] >= high[k]) { isPivHigh = false; break; }
            if(isPivHigh)
            {
               pivotHigh    = high[k];
               pivotHighBar = k;
            }
         }

         // Stop once we found both
         if(pivotLowBar >= 0 && pivotHighBar >= 0) break;
      }

      // Fallback to simple window max/min if no pivot found
      if(pivotLow  == 0) { int lb = MathMax(0,i-ZZDepth); pivotLow  = low[lb];  for(int k=lb+1;k<i;k++) if(low[k]  < pivotLow)  pivotLow  = low[k]; }
      if(pivotHigh == 0) { int lb = MathMax(0,i-ZZDepth); pivotHigh = high[lb]; for(int k=lb+1;k<i;k++) if(high[k] > pivotHigh) pivotHigh = high[k]; }

      // ----------------------------------------------------------------
      // Liquidity Sweep — price wicks beyond ZigZag pivot then reclaims
      // lsBuy:  wick below pivot low  + close back above = stops hunted below
      // lsSell: wick above pivot high + close back below = stops hunted above
      // ----------------------------------------------------------------
      bool lsBuy  = UseLiquiditySweep && (low[i]  < pivotLow  && close[i] > pivotLow);
      bool lsSell = UseLiquiditySweep && (high[i] > pivotHigh && close[i] < pivotHigh);

      // Hunt line drawn at the actual pivot level that was swept
      double huntLevelBuy  = pivotLow;
      double huntLevelSell = pivotHigh;

      // Fibonacci confluence — calculated from pivot high to pivot low
      double swHigh = pivotHigh, swLow = pivotLow;

      // Fair Value Gap
      bool fvgBuy = false, fvgSell = false;
      if(UseFairValueGap && i >= 2)
      {
         fvgBuy  = (low[i]  > high[i-2] && close[i-1] > open[i-1]);
         fvgSell = (high[i] < low[i-2]  && close[i-1] < open[i-1]);
      }

      // Local pivot check
      bool isLocalLow  = true;
      bool isLocalHigh = true;
      int  chk = MathMin(3, MathMin(i, rates_total - 1 - i));
      for(int k = 1; k <= chk; k++)
      {
         if(low[i]  >= low[i-k]  || low[i]  >= low[i+k])  isLocalLow  = false;
         if(high[i] <= high[i-k] || high[i] <= high[i+k]) isLocalHigh = false;
      }

      // Fibonacci confluence
      double range  = swHigh - swLow;
      bool   fibBuy = false, fibSell = false;
      if(range > 0)
      {
         double tol  = curATR * 0.5;
         fibBuy  = MathAbs(close[i] - (swHigh - FibLevel * range)) < tol;
         fibSell = MathAbs(close[i] - (swLow  + FibLevel * range)) < tol;
      }

      // Combine conditions
      bool buySignal  = (lsBuy  || fvgBuy)  && (isLocalLow  || fibBuy)  && close[i] > open[i];
      bool sellSignal = (lsSell || fvgSell) && (isLocalHigh || fibSell) && close[i] < open[i];

      // HTF filter
      if(EnableHTFFilter)
      {
         if(RequireBothHTF)
         {
            if(buySignal  && !htfBull) buySignal  = false;
            if(sellSignal && !htfBear) sellSignal = false;
         }
         else
         {
            if(buySignal  && htfBear)  buySignal  = false;
            if(sellSignal && htfBull)  sellSignal = false;
         }
      }

      // Paint arrow buffers
      if(buySignal)
      {
         BuyBuf[i] = low[i] - ArrowGap * _Point;
         // Record what triggered: 1=Sweep, 2=FVG, 3=Both
         TriggerBuf[i] = (lsBuy && fvgBuy) ? 3 : lsBuy ? 1 : 2;
         if(ShowSMCLines)
         {
            if(lsBuy)
               DrawHuntLine("MA_HL_B_" + IntegerToString((int)time[i]), huntLevelBuy, HuntLineColorBuy);
            if(fvgBuy)
               DrawFVGBox("MA_FVG_B_" + IntegerToString((int)time[i]), time, high, low, i, true);
         }
      }
      if(sellSignal)
      {
         SellBuf[i] = high[i] + ArrowGap * _Point;
         // Record what triggered: 1=Sweep, 2=FVG, 3=Both
         TriggerBuf[i] = (lsSell && fvgSell) ? 3 : lsSell ? 1 : 2;
         if(ShowSMCLines)
         {
            if(lsSell)
               DrawHuntLine("MA_HL_S_" + IntegerToString((int)time[i]), huntLevelSell, HuntLineColorSell);
            if(fvgSell)
               DrawFVGBox("MA_FVG_S_" + IntegerToString((int)time[i]), time, high, low, i, false);
         }
      }
   }

   // ================================================================
   // FIND LAST SIGNAL — any direction, no HTF filter on line drawing
   // Lines are FIXED from signal bar to current bar + 50 bars ahead
   // They do NOT move with price — entry is locked at signal close
   // ================================================================
   // ================================================================
   // ALWAYS clear ALL level objects first — prevents stale objects
   // from previous TF sticking on chart
   // ================================================================
   string levelNames[] = {
      "MA_Level_Entry","MA_Level_SL","MA_Level_TP1","MA_Level_TP2","MA_Level_TP3",
      "MA_Lbl_Entry","MA_Lbl_SL","MA_Lbl_TP1","MA_Lbl_TP2","MA_Lbl_TP3",
      "MA_ATR_Lbl_High","MA_ATR_Lbl_Low","MA_WATR_Lbl_High","MA_WATR_Lbl_Low"
   };
   for(int n = 0; n < ArraySize(levelNames); n++)
      if(ObjectFind(0, levelNames[n]) >= 0) ObjectDelete(0, levelNames[n]);

   if(DrawTPLines)
   {
      int  sigBar = -1;
      bool sigBuy = false;

      // Scan backwards — take the very last signal regardless of HTF
      for(int j = rates_total - BarsToConfirm - 2; j >= 0; j--)
      {
         if(BuyBuf[j]  > 0.0) { sigBar = j; sigBuy = true;  break; }
         if(SellBuf[j] > 0.0) { sigBar = j; sigBuy = false; break; }
      }

      if(sigBar >= 0)
      {
         // Entry price based on selected candle reference
         // Candle A = sigBar (sweep candle)
         // Candle B = sigBar + 1 (1st confirmation)
         // Candle C = sigBar + 2 (2nd confirmation)
         int entryBar = sigBar + (int)EntryCandle;
         // Safety: don't go beyond available bars
         if(entryBar >= rates_total) entryBar = rates_total - 1;

         double entry = close[entryBar];
         double a     = atr[sigBar];   // ATR always from signal bar
         if(a <= 0) a = atr[rates_total - 1];
         if(a <= 0) a = 10 * _Point;

         double dir = sigBuy ? 1.0 : -1.0;
         double sl  = entry - dir * a * SL_ATR_Mult;
         double tp1 = entry + dir * a * TP1_ATR_Mult;
         double tp2 = entry + dir * a * TP2_ATR_Mult;
         double tp3 = entry + dir * a * TP3_ATR_Mult;

         // Scan all bars AFTER signal for TP hits using high/low
         // This correctly detects hits even if price moved past and came back
         bool tp1Hit = false, tp2Hit = false, tp3Hit = false;
         for(int j = sigBar + 1; j < rates_total; j++)
         {
            if(sigBuy)
            {
               if(high[j] >= tp1) tp1Hit = true;
               if(high[j] >= tp2) tp2Hit = true;
               if(high[j] >= tp3) tp3Hit = true;
            }
            else
            {
               if(low[j] <= tp1) tp1Hit = true;
               if(low[j] <= tp2) tp2Hit = true;
               if(low[j] <= tp3) tp3Hit = true;
            }
         }

         color c1 = tp1Hit ? clrGray : TP1Color;
         color c2 = tp2Hit ? clrGray : TP2Color;
         color c3 = tp3Hit ? clrGray : TP3Color;

         // Use OBJ_HLINE — always visible regardless of TF or chart scroll
         // Extends right via RAY_RIGHT so label stays near current price
         datetime tNow   = time[rates_total - 1];
         datetime tStart = time[entryBar];   // line starts at the selected reference candle
         datetime tLabel = tNow + (datetime)(PeriodSeconds(_Period) * 3);

         SetLevelLine("MA_Level_Entry", tStart, entry, EntryColor, LevelStyle, LevelWidth);
         SetLevelLine("MA_Level_SL",    tStart, sl,    SLColor,    STYLE_DOT,  LevelWidth);
         SetLevelLine("MA_Level_TP1",   tStart, tp1,   c1,         LevelStyle, LevelWidth);
         SetLevelLine("MA_Level_TP2",   tStart, tp2,   c2,         LevelStyle, LevelWidth);
         SetLevelLine("MA_Level_TP3",   tStart, tp3,   c3,         LevelStyle, LevelWidth);

         // Labels near current candle
         DrawLineLabel("MA_Lbl_Entry", tLabel, entry, EntryColor, "  Entry");
         DrawLineLabel("MA_Lbl_SL",    tLabel, sl,    SLColor,    "  SL");
         DrawLineLabel("MA_Lbl_TP1",   tLabel, tp1,   c1,         tp1Hit ? "  TP1 ✓" : "  TP1");
         DrawLineLabel("MA_Lbl_TP2",   tLabel, tp2,   c2,         tp2Hit ? "  TP2 ✓" : "  TP2");
         DrawLineLabel("MA_Lbl_TP3",   tLabel, tp3,   c3,         tp3Hit ? "  TP3 ✓" : "  TP3");

         if(sigBar >= rates_total - BarsToConfirm - 3)
            DoAlert(sigBuy, entry, time[sigBar]);
      }
   }

   // Dashboard
   if(ShowPanel)
   {
      // Find trigger type of last signal for dashboard
      int    lastTrigBar  = -1;
      double lastTrigType = 0;
      for(int j = rates_total - BarsToConfirm - 2; j >= 0; j--)
      {
         if(BuyBuf[j] > 0 || SellBuf[j] > 0)
         { lastTrigBar = j; lastTrigType = TriggerBuf[j]; break; }
      }
      DrawDashboard(close, atr, rates_total, htfBull, htfBear, ema20Bull, ema20Bear, stBull, stBear, lastTrigType);
   }

   ChartRedraw(0);
   return rates_total;
}

//+------------------------------------------------------------------+
//| Level line — starts at signal bar, rays right across chart      |
//+------------------------------------------------------------------+
void SetLevelLine(string name, datetime tStart, double price, color clr,
                  ENUM_LINE_STYLE style, int width)
{
   if(ObjectFind(0, name) >= 0) ObjectDelete(0, name);
   // Use OBJ_TREND with ray right — starts at signal bar, extends to chart edge
   ObjectCreate(0, name, OBJ_TREND, 0, tStart, price, tStart + PeriodSeconds(_Period), price);
   ObjectSetInteger(0, name, OBJPROP_COLOR,      clr);
   ObjectSetInteger(0, name, OBJPROP_STYLE,      style);
   ObjectSetInteger(0, name, OBJPROP_WIDTH,      width);
   ObjectSetInteger(0, name, OBJPROP_RAY_RIGHT,  true);   // extends to right edge always
   ObjectSetInteger(0, name, OBJPROP_RAY_LEFT,   false);  // does not go left of signal
   ObjectSetInteger(0, name, OBJPROP_BACK,       false);
   ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
   ObjectSetString(0,  name, OBJPROP_TOOLTIP,    name + " @ " + DoubleToString(price, _Digits));
}

//+------------------------------------------------------------------+
//| Text label anchored to a chart time+price point                  |
//+------------------------------------------------------------------+
void DrawLineLabel(string name, datetime t, double price, color clr, string txt)
{
   if(ObjectFind(0, name) >= 0) ObjectDelete(0, name);
   ObjectCreate(0, name, OBJ_TEXT, 0, t, price);
   ObjectSetString(0,  name, OBJPROP_TEXT,      txt);
   ObjectSetInteger(0, name, OBJPROP_COLOR,     clr);
   ObjectSetInteger(0, name, OBJPROP_FONTSIZE,  8);
   ObjectSetString(0,  name, OBJPROP_FONT,      "Arial Bold");
   ObjectSetInteger(0, name, OBJPROP_ANCHOR,    ANCHOR_LEFT);
   ObjectSetInteger(0, name, OBJPROP_BACK,      false);
   ObjectSetInteger(0, name, OBJPROP_SELECTABLE,false);
}

//+------------------------------------------------------------------+
//| Kept for ATR lines (still uses full HLINE)                       |
//+------------------------------------------------------------------+
void SetHLine(string name, double price, color clr,
              ENUM_LINE_STYLE style, int width, string label)
{
   if(ObjectFind(0, name) >= 0) ObjectDelete(0, name);
   ObjectCreate(0, name, OBJ_HLINE, 0, 0, price);
   ObjectSetInteger(0, name, OBJPROP_COLOR,     clr);
   ObjectSetInteger(0, name, OBJPROP_STYLE,     style);
   ObjectSetInteger(0, name, OBJPROP_WIDTH,     width);
   ObjectSetString(0,  name, OBJPROP_TEXT,      label);
   ObjectSetString(0,  name, OBJPROP_TOOLTIP,   label);
   ObjectSetInteger(0, name, OBJPROP_BACK,      false);
   ObjectSetInteger(0, name, OBJPROP_SELECTABLE,false);
}

//+------------------------------------------------------------------+
void DrawHuntLine(string name, double price, color clr)
{
   if(ObjectFind(0, name) >= 0) return;
   ObjectCreate(0, name, OBJ_HLINE, 0, 0, price);
   ObjectSetInteger(0, name, OBJPROP_COLOR, clr);
   ObjectSetInteger(0, name, OBJPROP_STYLE, STYLE_DOT);
   ObjectSetInteger(0, name, OBJPROP_WIDTH, 1);
   ObjectSetInteger(0, name, OBJPROP_BACK,  true);
}

//+------------------------------------------------------------------+
void DrawFVGBox(string name, const datetime &time[], const double &high[],
                const double &low[], int bar, bool isBuy)
{
   if(bar < 2 || ObjectFind(0, name) >= 0) return;
   double top    = isBuy ? low[bar]    : low[bar-2];
   double bottom = isBuy ? high[bar-2] : high[bar];
   if(top <= bottom) return;
   datetime t2 = time[bar] + (datetime)(PeriodSeconds(_Period) * 8);
   ObjectCreate(0, name, OBJ_RECTANGLE, 0, time[bar], top, t2, bottom);
   color c = isBuy ? FVGBoxColorBuy : FVGBoxColorSell;
   ObjectSetInteger(0, name, OBJPROP_COLOR,  c);
   ObjectSetInteger(0, name, OBJPROP_BGCOLOR,c);
   ObjectSetInteger(0, name, OBJPROP_FILL,   true);
   ObjectSetInteger(0, name, OBJPROP_BACK,   true);
   ObjectSetInteger(0, name, OBJPROP_STYLE,  STYLE_SOLID);
}

//+------------------------------------------------------------------+
void DoAlert(bool isBuy, double price, datetime t)
{
   if(!MasterAlert) return;
   if((int)(TimeCurrent() - g_LastAlert) < AlertCooldown) return;
   g_LastAlert = TimeCurrent();
   string msg = "Monster Arrows V2 | " + _Symbol + " " + EnumToString(_Period) +
                " | " + (isBuy ? "BUY" : "SELL") + " @ " + DoubleToString(price, _Digits);
   if(DoPopup) Alert(msg);
   if(DoSound) PlaySound(AlertSound);
   if(DoEmail) SendMail("Monster Arrows V2", msg);
   if(DoPush)  SendNotification(msg);
}

//+------------------------------------------------------------------+
void DrawDashboard(const double &close[], const double &atr[],
                   int total, bool htfBull, bool htfBear,
                   bool emaBull, bool emaBear, bool stBull, bool stBear,
                   double trigType)
{
   int x = PanelX, y = PanelY;

   string bg = "MA_Dash_BG";
   if(ObjectFind(0, bg) < 0) ObjectCreate(0, bg, OBJ_RECTANGLE_LABEL, 0, 0, 0);
   ObjectSetInteger(0, bg, OBJPROP_XDISTANCE,  x - 8);
   ObjectSetInteger(0, bg, OBJPROP_YDISTANCE,  y - 8);
   ObjectSetInteger(0, bg, OBJPROP_XSIZE,       230);
   ObjectSetInteger(0, bg, OBJPROP_YSIZE,       215);  // taller to fit new row
   ObjectSetInteger(0, bg, OBJPROP_BGCOLOR,     C'15,15,25');
   ObjectSetInteger(0, bg, OBJPROP_BORDER_COLOR,DashTitleColor);
   ObjectSetInteger(0, bg, OBJPROP_BORDER_TYPE, BORDER_FLAT);
   ObjectSetInteger(0, bg, OBJPROP_CORNER,      CORNER_LEFT_UPPER);
   ObjectSetInteger(0, bg, OBJPROP_BACK,        false);

   DashLabel("MA_D_Title", x, y,    "MONSTER ARROWS V2", DashTitleColor, 9, true);
   DashLabel("MA_D_Sym",   x, y+20, _Symbol + "  |  " + EnumToString(_Period), DashTextColor, 8, false);

   // --- EMA label ---
   string emaTxt = emaBull ? "BULL" : emaBear ? "BEAR" : "NEUTRAL";
   color  emaClr = emaBull ? clrLime : emaBear ? clrRed : clrGray;
   DashLabel("MA_D_EMA", x, y+40, "EMA: " + emaTxt, emaClr, 8, false);

   // --- ST label ---
   string stTxt = stBull ? "BULL" : stBear ? "BEAR" : "NEUTRAL";
   color  stClr = stBull ? clrLime : stBear ? clrRed : clrGray;
   DashLabel("MA_D_ST",  x, y+60, "ST:  " + stTxt, stClr, 8, false);

   // --- Combined HTF ---
   string htfTxt = !EnableHTFFilter ? "OFF" : htfBull ? "BULLISH" : htfBear ? "BEARISH" : "NEUTRAL";
   color  htfClr = !EnableHTFFilter ? clrGray : htfBull ? clrLime : htfBear ? clrRed : clrGray;
   DashLabel("MA_D_HTF", x, y+80, "HTF: " + htfTxt, htfClr, 8, true);

   // --- Signal Trigger Type ---
   string trigTxt = "Trigger: None";
   color  trigClr = clrGray;
   if(trigType == 1) { trigTxt = "Trigger: SWEEP";    trigClr = clrAqua; }
   if(trigType == 2) { trigTxt = "Trigger: FVG";      trigClr = clrViolet; }
   if(trigType == 3) { trigTxt = "Trigger: SWEEP+FVG";trigClr = clrGold; }
   DashLabel("MA_D_Trig", x, y+100, trigTxt, trigClr, 8, false);

   string smcTxt = "SMC: " + (UseLiquiditySweep ? "Sweep " : "") + (UseFairValueGap ? "FVG" : "");
   DashLabel("MA_D_SMC",  x, y+120, smcTxt, DashTextColor, 8, false);

   string atrTxt = "ATR: " + (total > 0 ? DoubleToString(atr[total-1], _Digits) : "N/A");
   DashLabel("MA_D_ATR",  x, y+140, atrTxt, DashTextColor, 8, false);

   string lastTxt  = "Last: None";
   color  lastColor = DashTextColor;
   for(int i = total - 1; i >= MathMax(0, total - 500); i--)
   {
      if(BuyBuf[i]  > 0) { lastTxt = "Last: BUY @ "  + DoubleToString(close[i], _Digits); lastColor = clrLime; break; }
      if(SellBuf[i] > 0) { lastTxt = "Last: SELL @ " + DoubleToString(close[i], _Digits); lastColor = clrRed;  break; }
   }
   DashLabel("MA_D_Last",  x, y+160, lastTxt, lastColor, 8, false);
   DashLabel("MA_D_Alert", x, y+180,
             "Alerts: "+(MasterAlert?"ON":"OFF")+"  Push: "+(DoPush?"ON":"OFF"),
             DashTextColor, 8, false);
}

//+------------------------------------------------------------------+
void DashLabel(string name, int x, int y, string txt, color clr, int sz, bool bold)
{
   if(ObjectFind(0, name) < 0) ObjectCreate(0, name, OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, name, OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, name, OBJPROP_YDISTANCE, y);
   ObjectSetInteger(0, name, OBJPROP_CORNER,    CORNER_LEFT_UPPER);
   ObjectSetString(0,  name, OBJPROP_TEXT,      txt);
   ObjectSetInteger(0, name, OBJPROP_COLOR,     clr);
   ObjectSetInteger(0, name, OBJPROP_FONTSIZE,  sz);
   ObjectSetString(0,  name, OBJPROP_FONT,      bold ? "Arial Bold" : "Arial");
   ObjectSetInteger(0, name, OBJPROP_BACK,      false);
}

//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   ObjectsDeleteAll(0, "MA_");
   if(hATR       != INVALID_HANDLE) IndicatorRelease(hATR);
   if(hATRDaily  != INVALID_HANDLE) IndicatorRelease(hATRDaily);
   if(hATRWeekly != INVALID_HANDLE) IndicatorRelease(hATRWeekly);
   if(hHTF_EMA   != INVALID_HANDLE) IndicatorRelease(hHTF_EMA);
   if(hHTF_ATR   != INVALID_HANDLE) IndicatorRelease(hHTF_ATR);
}
//+------------------------------------------------------------------+