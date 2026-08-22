//+------------------------------------------------------------------+
//|                       SHK_Professional_Heikin_Ashi_Signals.mq5   |
//|                              Original Heikin Ashi Signal Engine   |
//|   Pure HA engine + momentum-shift circles + confirmed arrows     |
//|   Non repaint closed bar logic | EA readable buffers             |
//+------------------------------------------------------------------+
#property copyright "SHK"
#property link      ""
#property version   "1.02"
#property description "Professional Heikin Ashi candle engine with HA color change plus MACD divergence confirmed BUY/SELL arrows. Closed bar, EA integration friendly."
#property strict

#property indicator_chart_window
#property indicator_buffers 10
#property indicator_plots   5

//--- buffer index map (visible + calc)
//  0 = HA Open
//  1 = HA High
//  2 = HA Low
//  3 = HA Close
//  4 = HA Color Index
//  5 = Buy Momentum Circle
//  6 = Sell Momentum Circle
//  7 = Buy Confirmed Arrow
//  8 = Sell Confirmed Arrow
//  9 = Visual ATR (calculation only)


enum CandleColorMode
{
   COLOR_STANDARD       = 0,
   COLOR_TREND_BASED    = 1,
   COLOR_MOMENTUM_BASED = 2
};


input int    MaxBarsToCalculate       = 3000;
input bool   ShowHeikinAshiCandles    = true;
input bool   ShowOriginalCandlesFaint = false;
input bool   EnableSignals            = true;
input bool   UseClosedBarsOnly        = true;
input CandleColorMode CandleMode      = COLOR_STANDARD;
input color  BullColor                = clrLime;
input color  BearColor                = clrRed;
input color  StrongBullColor          = clrDodgerBlue;
input color  StrongBearColor          = clrOrange;
input color  NeutralColor             = clrGray;
input int    ConfirmationCandles      = 1;       // 1 to 10
input int    SignalLookbackBars       = 1000;
input double MinBodyPercent           = 0.0;
input bool   SignalOnHAColorChange    = true;
input bool   RequireMACDDivergenceConfirmation = true;
input int    MACDFastEMA              = 12;
input int    MACDSlowEMA              = 26;
input int    MACDSignalEMA            = 5;
input int    DivergencePivotLookback  = 2;
input int    DivergenceSearchBars     = 200;
input int    DivergenceConfirmWindow  = 30;
input bool   RequireStrongCandleForCircle = false;
input bool   CancelSignalOnOppositeHA = true;
input bool   ConfirmByCloseBreak      = true;
input double ArrowOffsetATR           = 0.25;
input double CircleOffsetATR          = 0.15;
input int    VisualATRPeriod          = 14;
input bool   EnablePopupAlert         = true;
input bool   EnablePushAlert          = false;
input bool   EnableEmailAlert         = false;
input bool   EnableSoundAlert         = false;
input string BuySoundFile             = "alert.wav";
input string SellSoundFile            = "alert2.wav";
input int    AlertCooldownSeconds     = 120;
input bool   AlertOnlyRealtime        = true;

double HAOpen[];
double HAHigh[];
double HALow[];
double HAClose[];
double HAColor[];
double BuyCircle[];
double SellCircle[];
double BuyArrow[];
double SellArrow[];
double ATRBuf[];

double MacdFastBuf[];
double MacdSlowBuf[];
double MacdSignalBuf[];
double MacdHistBuf[];
bool   BullDivConfirmed[];
bool   BearDivConfirmed[];

#define COL_BULL          0
#define COL_BEAR          1
#define COL_STRONG_BULL   2
#define COL_STRONG_BEAR   3
#define COL_NEUTRAL       4

#define MOM_BODY_WINDOW   14
#define STRONG_WICK_FRAC  0.10
#define CIRCLE_WICK_FRAC  0.15
#define ARROW_BUY_CODE    233
#define ARROW_SELL_CODE   234
#define CIRCLE_CODE       108

int      g_firstBar       = 1;
int      g_confCandles    = 3;

//--- pending momentum->confirmation state machine
bool     g_buyPending     = false;
double   g_buyRefHigh      = 0.0;
int      g_buyBarsLeft     = 0;

bool     g_sellPending    = false;
double   g_sellRefLow      = 0.0;
int      g_sellBarsLeft    = 0;

//--- signal processing cursor (by time, robust to index shifts)
datetime g_lastClosedTime = 0;

//--- alert dedupe
datetime g_lastAlertTime  = 0;
int      g_lastAlertDir    = 0;
datetime g_lastAlertWall   = 0;

bool IsBullishHA(const int i)
{
   return(HAClose[i] > HAOpen[i]);
}

bool IsBearishHA(const int i)
{
   return(HAClose[i] < HAOpen[i]);
}

double GetBodyPercent(const int i)
{
   double range = HAHigh[i] - HALow[i];
   if(range <= 0.0)
      return(0.0);
   return(MathAbs(HAClose[i] - HAOpen[i]) / range * 100.0);
}

double AvgHABody(const int i, const int win, const int firstBar)
{
   int st = i - win;
   if(st < firstBar)
      return(0.0);
   double s = 0.0;
   int    c = 0;
   for(int k = st; k < i; k++)
   {
      s += MathAbs(HAClose[k] - HAOpen[k]);
      c++;
   }
   return(c > 0 ? s / c : 0.0);
}

int GetCandleColorIndex(const int i)
{
   double range = HAHigh[i] - HALow[i];
   bool   bull  = IsBullishHA(i);
   bool   bear  = IsBearishHA(i);

   if(!bull && !bear)
      return(COL_NEUTRAL);

   if(CandleMode == COLOR_STANDARD)
      return(bull ? COL_BULL : COL_BEAR);

   if(CandleMode == COLOR_TREND_BASED)
   {
      if(range <= 0.0)
         return(bull ? COL_BULL : COL_BEAR);
      double lowerWick = MathMin(HAOpen[i], HAClose[i]) - HALow[i];
      double upperWick = HAHigh[i] - MathMax(HAOpen[i], HAClose[i]);
      if(bull)
         return((lowerWick <= STRONG_WICK_FRAC * range) ? COL_STRONG_BULL : COL_BULL);
      else
         return((upperWick <= STRONG_WICK_FRAC * range) ? COL_STRONG_BEAR : COL_BEAR);
   }

   // COLOR_MOMENTUM_BASED
   double body    = MathAbs(HAClose[i] - HAOpen[i]);
   double avgBody = AvgHABody(i, MOM_BODY_WINDOW, g_firstBar);
   if(avgBody > 0.0 && body >= 1.2 * avgBody)
      return(bull ? COL_STRONG_BULL : COL_STRONG_BEAR);
   return(bull ? COL_BULL : COL_BEAR);
}

void ComputeHABar(const int i,
                  const double &open[], const double &high[],
                  const double &low[],  const double &close[])
{
   double haClose = (open[i] + high[i] + low[i] + close[i]) / 4.0;
   double haOpen  = (HAOpen[i - 1] + HAClose[i - 1]) / 2.0;
   double haHigh  = MathMax(high[i], MathMax(haOpen, haClose));
   double haLow   = MathMin(low[i],  MathMin(haOpen, haClose));

   HAOpen[i]  = haOpen;
   HAClose[i] = haClose;
   HAHigh[i]  = haHigh;
   HALow[i]   = haLow;
}

void InitHASeed(const int firstBar,
                const double &open[], const double &high[],
                const double &low[],  const double &close[])
{
   double haClose = (open[firstBar] + high[firstBar] + low[firstBar] + close[firstBar]) / 4.0;
   double haOpen  = (open[firstBar] + close[firstBar]) / 2.0;
   HAClose[firstBar] = haClose;
   HAOpen[firstBar]  = haOpen;
   HAHigh[firstBar]  = MathMax(high[firstBar], MathMax(haOpen, haClose));
   HALow[firstBar]   = MathMin(low[firstBar],  MathMin(haOpen, haClose));
}

double GetVisualATR(const int i,
                    const double &high[], const double &low[], const double &close[],
                    const int firstBar)
{
   int p = VisualATRPeriod;
   if(p < 1) p = 1;
   int st = i - p + 1;
   if(st < firstBar) st = firstBar;
   if(st < 1)        st = 1;

   double sum = 0.0;
   int    n   = 0;
   for(int k = st; k <= i; k++)
   {
      double tr = high[k] - low[k];
      double a  = MathAbs(high[k] - close[k - 1]);
      double b  = MathAbs(low[k]  - close[k - 1]);
      if(a > tr) tr = a;
      if(b > tr) tr = b;
      sum += tr;
      n++;
   }
   double atr = (n > 0) ? sum / n : (high[i] - low[i]);
   ATRBuf[i] = atr;
   return(atr);
}

bool IsMacdPeak(const int i, const int lookback, const int firstBar, const int lastBar)
{
   if(i - lookback < firstBar || i + lookback > lastBar)
      return(false);

   for(int k = 1; k <= lookback; k++)
   {
      if(MacdHistBuf[i] <= MacdHistBuf[i - k] || MacdHistBuf[i] <= MacdHistBuf[i + k])
         return(false);
   }
   return(true);
}

bool IsMacdTrough(const int i, const int lookback, const int firstBar, const int lastBar)
{
   if(i - lookback < firstBar || i + lookback > lastBar)
      return(false);

   for(int k = 1; k <= lookback; k++)
   {
      if(MacdHistBuf[i] >= MacdHistBuf[i - k] || MacdHistBuf[i] >= MacdHistBuf[i + k])
         return(false);
   }
   return(true);
}

void ComputeMACDDivergence(const int rates_total,
                           const int firstBar,
                           const int lastClosed,
                           const double &high[],
                           const double &low[],
                           const double &close[])
{
   ArrayResize(MacdFastBuf, rates_total);
   ArrayResize(MacdSlowBuf, rates_total);
   ArrayResize(MacdSignalBuf, rates_total);
   ArrayResize(MacdHistBuf, rates_total);
   ArrayResize(BullDivConfirmed, rates_total);
   ArrayResize(BearDivConfirmed, rates_total);

   for(int i = 0; i < rates_total; i++)
   {
      MacdFastBuf[i]       = 0.0;
      MacdSlowBuf[i]       = 0.0;
      MacdSignalBuf[i]     = 0.0;
      MacdHistBuf[i]       = 0.0;
      BullDivConfirmed[i]  = false;
      BearDivConfirmed[i]  = false;
   }

   if(!RequireMACDDivergenceConfirmation)
      return;

   int fast = MACDFastEMA;
   int slow = MACDSlowEMA;
   int signal = MACDSignalEMA;
   int pivotLookback = DivergencePivotLookback;
   int searchBars = DivergenceSearchBars;

   if(fast < 1) fast = 1;
   if(slow < 1) slow = 1;
   if(signal < 1) signal = 1;
   if(pivotLookback < 1) pivotLookback = 1;
   if(searchBars < 1) searchBars = 1;

   if(lastClosed < firstBar + pivotLookback * 2 + 2)
      return;

   double fastAlpha = 2.0 / ((double)fast + 1.0);
   double slowAlpha = 2.0 / ((double)slow + 1.0);
   double signalAlpha = 2.0 / ((double)signal + 1.0);

   for(int i = firstBar; i <= lastClosed; i++)
   {
      double price = close[i];
      if(i == firstBar)
      {
         MacdFastBuf[i] = price;
         MacdSlowBuf[i] = price;
         MacdSignalBuf[i] = 0.0;
         MacdHistBuf[i] = 0.0;
         continue;
      }

      MacdFastBuf[i] = fastAlpha * price + (1.0 - fastAlpha) * MacdFastBuf[i - 1];
      MacdSlowBuf[i] = slowAlpha * price + (1.0 - slowAlpha) * MacdSlowBuf[i - 1];
      double macdMain = MacdFastBuf[i] - MacdSlowBuf[i];
      MacdSignalBuf[i] = signalAlpha * macdMain + (1.0 - signalAlpha) * MacdSignalBuf[i - 1];
      MacdHistBuf[i] = macdMain - MacdSignalBuf[i];
   }

   int lastPivot = lastClosed - pivotLookback;
   for(int i = firstBar + pivotLookback; i <= lastPivot; i++)
   {
      if(IsMacdTrough(i, pivotLookback, firstBar, lastClosed))
      {
         int prevTrough = -1;
         int oldest = i - searchBars;
         if(oldest < firstBar + pivotLookback)
            oldest = firstBar + pivotLookback;

         for(int j = i - 1; j >= oldest; j--)
         {
            if(IsMacdTrough(j, pivotLookback, firstBar, lastClosed))
            {
               prevTrough = j;
               break;
            }
         }

         if(prevTrough >= 0 && low[i] < low[prevTrough] && MacdHistBuf[i] > MacdHistBuf[prevTrough])
            BullDivConfirmed[i + pivotLookback] = true;
      }

      if(IsMacdPeak(i, pivotLookback, firstBar, lastClosed))
      {
         int prevPeak = -1;
         int oldest = i - searchBars;
         if(oldest < firstBar + pivotLookback)
            oldest = firstBar + pivotLookback;

         for(int j = i - 1; j >= oldest; j--)
         {
            if(IsMacdPeak(j, pivotLookback, firstBar, lastClosed))
            {
               prevPeak = j;
               break;
            }
         }

         if(prevPeak >= 0 && high[i] > high[prevPeak] && MacdHistBuf[i] < MacdHistBuf[prevPeak])
            BearDivConfirmed[i + pivotLookback] = true;
      }
   }
}

bool RecentMACDDivergenceConfirmed(const int i, const bool bullish)
{
   if(!RequireMACDDivergenceConfirmation)
      return(true);

   int window = DivergenceConfirmWindow;
   if(window < 0)
      window = 0;

   int first = i - window;
   if(first < g_firstBar)
      first = g_firstBar;

   for(int k = i; k >= first; k--)
   {
      if(bullish && BullDivConfirmed[k])
         return(true);
      if(!bullish && BearDivConfirmed[k])
         return(true);
   }
   return(false);
}

void ClearSignalBuffers(const int from, const int to)
{
   for(int i = from; i <= to; i++)
   {
      BuyCircle[i]  = EMPTY_VALUE;
      SellCircle[i] = EMPTY_VALUE;
      BuyArrow[i]   = EMPTY_VALUE;
      SellArrow[i]  = EMPTY_VALUE;
   }
}

void ClearAllBuffers(const int total)
{
   for(int i = 0; i < total; i++)
   {
      HAOpen[i]     = EMPTY_VALUE;
      HAHigh[i]     = EMPTY_VALUE;
      HALow[i]      = EMPTY_VALUE;
      HAClose[i]    = EMPTY_VALUE;
      HAColor[i]    = 0.0;
      ATRBuf[i]     = 0.0;
      BuyCircle[i]  = EMPTY_VALUE;
      SellCircle[i] = EMPTY_VALUE;
      BuyArrow[i]   = EMPTY_VALUE;
      SellArrow[i]  = EMPTY_VALUE;
   }
}

void ResetSignalState()
{
   g_buyPending  = false;
   g_buyRefHigh   = 0.0;
   g_buyBarsLeft  = 0;
   g_sellPending = false;
   g_sellRefLow   = 0.0;
   g_sellBarsLeft = 0;
}

void FireAlert(const int dir, const string typeText,
               const double price, const datetime candleTime)
{
   if(!(EnablePopupAlert || EnablePushAlert || EnableEmailAlert || EnableSoundAlert))
      return;

   // per-candle + direction dedupe
   if(candleTime == g_lastAlertTime && dir == g_lastAlertDir)
      return;

   // cooldown on same direction
   if(g_lastAlertWall > 0 && dir == g_lastAlertDir &&
      (TimeCurrent() - g_lastAlertWall) < AlertCooldownSeconds)
      return;

   string tf  = StringSubstr(EnumToString((ENUM_TIMEFRAMES)_Period), 7);
   string msg = StringFormat("SHK Heikin Ashi Signals | %s %s | %s | Price: %s | Time: %s",
                             _Symbol, tf, typeText,
                             DoubleToString(price, _Digits),
                             TimeToString(candleTime, TIME_DATE | TIME_MINUTES));

   if(EnablePopupAlert) Alert(msg);
   if(EnablePushAlert)  SendNotification(msg);
   if(EnableEmailAlert) SendMail("SHK Heikin Ashi Signal", msg);
   if(EnableSoundAlert) PlaySound(dir > 0 ? BuySoundFile : SellSoundFile);

   g_lastAlertTime = candleTime;
   g_lastAlertDir  = dir;
   g_lastAlertWall = TimeCurrent();
}

bool HasConfirmedHAColorChange(const int i, const bool bullish)
{
   int candles = g_confCandles;
   if(candles < 1)
      candles = 1;

   int runStart = i - candles + 1;
   int previous = runStart - 1;
   if(runStart < g_firstBar + 1 || previous < g_firstBar)
      return(false);

   for(int k = runStart; k <= i; k++)
   {
      if(bullish && !IsBullishHA(k))
         return(false);
      if(!bullish && !IsBearishHA(k))
         return(false);
      if(GetBodyPercent(k) < MinBodyPercent)
         return(false);
   }

   if(bullish)
      return(IsBearishHA(previous));
   return(IsBullishHA(previous));
}

void ProcessColorChangeArrow(const int i, const datetime &time[], const double atr, const bool allowAlert)
{
   if(HasConfirmedHAColorChange(i, true) && RecentMACDDivergenceConfirmed(i, true))
   {
      BuyArrow[i] = HALow[i] - ArrowOffsetATR * atr;
      if(allowAlert)
         FireAlert(+1, "STRONG BUY HA + MACD Divergence", HAClose[i], time[i]);
      return;
   }

   if(HasConfirmedHAColorChange(i, false) && RecentMACDDivergenceConfirmed(i, false))
   {
      SellArrow[i] = HAHigh[i] + ArrowOffsetATR * atr;
      if(allowAlert)
         FireAlert(-1, "STRONG SELL HA + MACD Divergence", HAClose[i], time[i]);
   }
}

void ProcessMomentumAndConfirmation(const int i, const datetime &time[], const bool allowAlert)
{
   // fresh signal slots for this bar
   BuyCircle[i]  = EMPTY_VALUE;
   SellCircle[i] = EMPTY_VALUE;
   BuyArrow[i]   = EMPTY_VALUE;
   SellArrow[i]  = EMPTY_VALUE;

   if(i < g_firstBar + 1)
      return;

   double atr = (ATRBuf[i] != EMPTY_VALUE && ATRBuf[i] > 0.0)
                ? ATRBuf[i] : (HAHigh[i] - HALow[i]);

   bool curBull  = IsBullishHA(i);
   bool curBear  = IsBearishHA(i);
   bool prevBull = IsBullishHA(i - 1);
   bool prevBear = IsBearishHA(i - 1);

   if(SignalOnHAColorChange)
   {
      ProcessColorChangeArrow(i, time, atr, allowAlert);
      return;
   }

   bool placedBuyArrow  = false;
   bool placedSellArrow = false;

   //--- 1) Resolve pending BUY confirmation against bar i -----------
   if(g_buyPending)
   {
      if(CancelSignalOnOppositeHA && curBear)
      {
         g_buyPending = false;
      }
      else
      {
         bool brk = ConfirmByCloseBreak ? (HAClose[i] > g_buyRefHigh)
                                        : (HAHigh[i]  > g_buyRefHigh);
         if(brk)
         {
            BuyArrow[i]  = HALow[i] - ArrowOffsetATR * atr;
            placedBuyArrow = true;
            g_buyPending = false;
            if(allowAlert)
               FireAlert(+1, "BUY Confirmed", HAClose[i], time[i]);
         }
         else
         {
            g_buyBarsLeft--;
            if(g_buyBarsLeft <= 0)
               g_buyPending = false;
         }
      }
   }

   //--- 2) Resolve pending SELL confirmation against bar i ----------
   if(g_sellPending)
   {
      if(CancelSignalOnOppositeHA && curBull)
      {
         g_sellPending = false;
      }
      else
      {
         bool brk = ConfirmByCloseBreak ? (HAClose[i] < g_sellRefLow)
                                        : (HALow[i]   < g_sellRefLow);
         if(brk && !placedBuyArrow) // never both directions on the same candle
         {
            SellArrow[i] = HAHigh[i] + ArrowOffsetATR * atr;
            placedSellArrow = true;
            g_sellPending = false;
            if(allowAlert)
               FireAlert(-1, "SELL Confirmed", HAClose[i], time[i]);
         }
         else
         {
            g_sellBarsLeft--;
            if(g_sellBarsLeft <= 0)
               g_sellPending = false;
         }
      }
   }

   //--- 3) Detect a NEW momentum circle on bar i --------------------
   double bodyPct = GetBodyPercent(i);
   double avgBody = AvgHABody(i, MOM_BODY_WINDOW, g_firstBar);
   double range   = HAHigh[i] - HALow[i];
   double body    = MathAbs(HAClose[i] - HAOpen[i]);

   // Bullish momentum shift: prev bearish -> current bullish
   if(prevBear && curBull && !placedSellArrow)
   {
      bool ok = (bodyPct >= MinBodyPercent);
      if(ok && RequireStrongCandleForCircle)
      {
         double lowerWick = MathMin(HAOpen[i], HAClose[i]) - HALow[i];
         bool strong = (range > 0.0 && lowerWick <= CIRCLE_WICK_FRAC * range) ||
                       (avgBody > 0.0 && body >= avgBody);
         ok = strong;
      }
      if(ok)
      {
         BuyCircle[i]  = HALow[i] - CircleOffsetATR * atr;
         g_buyPending  = true;
         g_buyRefHigh   = HAHigh[i];
         g_buyBarsLeft  = g_confCandles;
         if(allowAlert)
            FireAlert(+1, "BUY Momentum", HAClose[i], time[i]);
      }
   }
   // Bearish momentum shift: prev bullish -> current bearish
   else if(prevBull && curBear && !placedBuyArrow)
   {
      bool ok = (bodyPct >= MinBodyPercent);
      if(ok && RequireStrongCandleForCircle)
      {
         double upperWick = HAHigh[i] - MathMax(HAOpen[i], HAClose[i]);
         bool strong = (range > 0.0 && upperWick <= CIRCLE_WICK_FRAC * range) ||
                       (avgBody > 0.0 && body >= avgBody);
         ok = strong;
      }
      if(ok)
      {
         SellCircle[i] = HAHigh[i] + CircleOffsetATR * atr;
         g_sellPending = true;
         g_sellRefLow   = HALow[i];
         g_sellBarsLeft = g_confCandles;
         if(allowAlert)
            FireAlert(-1, "SELL Momentum", HAClose[i], time[i]);
      }
   }
}

int OnInit()
{
   SetIndexBuffer(0, HAOpen,     INDICATOR_DATA);
   SetIndexBuffer(1, HAHigh,     INDICATOR_DATA);
   SetIndexBuffer(2, HALow,      INDICATOR_DATA);
   SetIndexBuffer(3, HAClose,    INDICATOR_DATA);
   SetIndexBuffer(4, HAColor,    INDICATOR_COLOR_INDEX);
   SetIndexBuffer(5, BuyCircle,  INDICATOR_DATA);
   SetIndexBuffer(6, SellCircle, INDICATOR_DATA);
   SetIndexBuffer(7, BuyArrow,   INDICATOR_DATA);
   SetIndexBuffer(8, SellArrow,  INDICATOR_DATA);
   SetIndexBuffer(9, ATRBuf,     INDICATOR_CALCULATIONS);

   //--- Plot 0: Heikin Ashi color candles
   PlotIndexSetInteger(0, PLOT_DRAW_TYPE, ShowHeikinAshiCandles ? DRAW_COLOR_CANDLES : DRAW_NONE);
   PlotIndexSetInteger(0, PLOT_COLOR_INDEXES, 5);
   PlotIndexSetInteger(0, PLOT_LINE_COLOR, COL_BULL,        BullColor);
   PlotIndexSetInteger(0, PLOT_LINE_COLOR, COL_BEAR,        BearColor);
   PlotIndexSetInteger(0, PLOT_LINE_COLOR, COL_STRONG_BULL, StrongBullColor);
   PlotIndexSetInteger(0, PLOT_LINE_COLOR, COL_STRONG_BEAR, StrongBearColor);
   PlotIndexSetInteger(0, PLOT_LINE_COLOR, COL_NEUTRAL,     NeutralColor);
   PlotIndexSetString(0, PLOT_LABEL, "HA Open;HA High;HA Low;HA Close");
   PlotIndexSetDouble(0, PLOT_EMPTY_VALUE, EMPTY_VALUE);

   //--- Plot 1: Buy Momentum Circle
   PlotIndexSetInteger(1, PLOT_DRAW_TYPE,  DRAW_ARROW);
   PlotIndexSetInteger(1, PLOT_ARROW,      CIRCLE_CODE);
   PlotIndexSetInteger(1, PLOT_LINE_COLOR, StrongBullColor);
   PlotIndexSetInteger(1, PLOT_LINE_WIDTH, 2);
   PlotIndexSetString(1,  PLOT_LABEL, "Buy Momentum Circle");
   PlotIndexSetDouble(1,  PLOT_EMPTY_VALUE, EMPTY_VALUE);

   //--- Plot 2: Sell Momentum Circle
   PlotIndexSetInteger(2, PLOT_DRAW_TYPE,  DRAW_ARROW);
   PlotIndexSetInteger(2, PLOT_ARROW,      CIRCLE_CODE);
   PlotIndexSetInteger(2, PLOT_LINE_COLOR, StrongBearColor);
   PlotIndexSetInteger(2, PLOT_LINE_WIDTH, 2);
   PlotIndexSetString(2,  PLOT_LABEL, "Sell Momentum Circle");
   PlotIndexSetDouble(2,  PLOT_EMPTY_VALUE, EMPTY_VALUE);

   //--- Plot 3: Buy Confirmed Arrow
   PlotIndexSetInteger(3, PLOT_DRAW_TYPE,  DRAW_ARROW);
   PlotIndexSetInteger(3, PLOT_ARROW,      ARROW_BUY_CODE);
   PlotIndexSetInteger(3, PLOT_LINE_COLOR, BullColor);
   PlotIndexSetInteger(3, PLOT_LINE_WIDTH, 3);
   PlotIndexSetString(3,  PLOT_LABEL, "Buy Confirmed Arrow");
   PlotIndexSetDouble(3,  PLOT_EMPTY_VALUE, EMPTY_VALUE);

   //--- Plot 4: Sell Confirmed Arrow
   PlotIndexSetInteger(4, PLOT_DRAW_TYPE,  DRAW_ARROW);
   PlotIndexSetInteger(4, PLOT_ARROW,      ARROW_SELL_CODE);
   PlotIndexSetInteger(4, PLOT_LINE_COLOR, BearColor);
   PlotIndexSetInteger(4, PLOT_LINE_WIDTH, 3);
   PlotIndexSetString(4,  PLOT_LABEL, "Sell Confirmed Arrow");
   PlotIndexSetDouble(4,  PLOT_EMPTY_VALUE, EMPTY_VALUE);

   //--- digits / name
   IndicatorSetInteger(INDICATOR_DIGITS, _Digits);
   IndicatorSetString(INDICATOR_SHORTNAME, "SHK Pro Heikin Ashi Signals");

   //--- clamp confirmation candles 1..10
   g_confCandles = ConfirmationCandles;
   if(g_confCandles < 1)  g_confCandles = 1;
   if(g_confCandles > 10) g_confCandles = 10;

   //--- original candle visibility
   if(!ShowOriginalCandlesFaint)
   {
      ChartSetInteger(0, CHART_COLOR_CHART_UP,   clrNONE);
      ChartSetInteger(0, CHART_COLOR_CHART_DOWN, clrNONE);
      ChartSetInteger(0, CHART_COLOR_CANDLE_BULL, clrNONE);
      ChartSetInteger(0, CHART_COLOR_CANDLE_BEAR, clrNONE);
   }
   else
   {
      ChartSetInteger(0, CHART_COLOR_CHART_UP,    clrDimGray);
      ChartSetInteger(0, CHART_COLOR_CHART_DOWN,  clrDimGray);
      ChartSetInteger(0, CHART_COLOR_CANDLE_BULL, clrDimGray);
      ChartSetInteger(0, CHART_COLOR_CANDLE_BEAR, clrDimGray);
   }

   ResetSignalState();
   g_lastClosedTime = 0;
   g_lastAlertTime  = 0;
   g_lastAlertDir   = 0;
   g_lastAlertWall  = 0;

   return(INIT_SUCCEEDED);
}

int OnCalculate(const int rates_total,
                const int prev_calculated,
                const datetime &time[],
                const double &open[],
                const double &high[],
                const double &low[],
                const double &close[],
                const long &tick_volume[],
                const long &volume[],
                const int &spread[])
{
   if(rates_total < 5)
      return(0);

   //--- non-series (chronological) indexing for everything
   int maxBars = (MaxBarsToCalculate > 0) ? MaxBarsToCalculate : rates_total;
   int firstBar = rates_total - maxBars;
   if(firstBar < 1) firstBar = 1;
   g_firstBar = firstBar;

   int haStart;

   if(prev_calculated == 0)
   {
      //--- full recalculation
      ClearAllBuffers(rates_total);
      ResetSignalState();
      g_lastClosedTime = 0;

      InitHASeed(firstBar, open, high, low, close);
      GetVisualATR(firstBar, high, low, close, firstBar);
      HAColor[firstBar] = (double)GetCandleColorIndex(firstBar);

      haStart = firstBar + 1;
   }
   else
   {
      //--- incremental: recompute from last (possibly updated) bar
      haStart = prev_calculated - 1;
      if(haStart < firstBar + 1) haStart = firstBar + 1;
   }

   //--- HA engine + visual ATR + colors (includes forming bar for visualization)
   for(int i = haStart; i < rates_total; i++)
   {
      ComputeHABar(i, open, high, low, close);
      GetVisualATR(i, high, low, close, firstBar);
      HAColor[i] = (double)GetCandleColorIndex(i);
   }

   if(!EnableSignals)
      return(rates_total);

   //--- last bar eligible for signals (closed-bar logic by default)
   int lastClosed = (UseClosedBarsOnly ? rates_total - 2 : rates_total - 1);
   if(lastClosed < firstBar + 1)
      return(rates_total);

   ComputeMACDDivergence(rates_total, firstBar, lastClosed, high, low, close);

   //--- determine signal scan start
   int sStart;
   if(prev_calculated == 0)
   {
      sStart = firstBar + 1;
      int lb = (rates_total - 1) - SignalLookbackBars;
      if(lb > sStart) sStart = lb;
   }
   else
   {
      sStart = lastClosed;
      while(sStart > firstBar + 1 && time[sStart - 1] > g_lastClosedTime)
         sStart--;
   }

   //--- process new closed bars in chronological order (deterministic, non-repaint)
   for(int i = sStart; i <= lastClosed; i++)
   {
      if(prev_calculated != 0 && time[i] <= g_lastClosedTime)
         continue;

      bool realtime   = (prev_calculated != 0);
      bool allowAlert  = realtime && (i == lastClosed) &&
                         (!AlertOnlyRealtime || realtime);

      ProcessMomentumAndConfirmation(i, time, allowAlert);
      g_lastClosedTime = time[i];
   }

   //--- keep the forming bar's signal slots empty (no signal until close)
   if(UseClosedBarsOnly && rates_total - 1 >= 0)
   {
      BuyCircle[rates_total - 1]  = EMPTY_VALUE;
      SellCircle[rates_total - 1] = EMPTY_VALUE;
      BuyArrow[rates_total - 1]   = EMPTY_VALUE;
      SellArrow[rates_total - 1]  = EMPTY_VALUE;
   }

   return(rates_total);
}
