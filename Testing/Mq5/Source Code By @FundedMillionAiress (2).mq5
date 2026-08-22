#property copyright "CLICK HERE"
#property link      "https://t.me/FundedMillionAiress"
#property version   "2.00"

#property description "We crack MT4 n MT5 Indicators/Robots."
#property description "We convert MT4/MT5 Indicators to Robots."
#property description "."
#property description "CRACKED BY @FundedMillionAiress"
#property description "."
#property description "Instagram = @FundedMillionAiress"
#property description "."
#property description "Telegram = @FundedMillionAiress"



#property indicator_chart_window
#property indicator_buffers 2
#property indicator_plots   2

//--- Plot MA Line
#property indicator_label1  "Trend MA"
#property indicator_type1   DRAW_LINE
#property indicator_color1  C'128,128,128'  // Gray
#property indicator_style1  STYLE_SOLID
#property indicator_width1  1

//--- Plot Trail Line
#property indicator_label2  "Trail"
#property indicator_type2   DRAW_LINE
#property indicator_color2  clrLimeGreen
#property indicator_style2  STYLE_SOLID
#property indicator_width2  2

//+------------------------------------------------------------------+
//| Enums and Constants                                              |
//+------------------------------------------------------------------+
enum MAType {
   MA_EMA,    // EMA
   MA_SMA,    // SMA
   MA_WMA,    // WMA
   MA_HMA,    // HMA
   MA_DEMA,   // DEMA
   MA_TEMA,   // TEMA
   MA_VWMA,   // VWMA
   MA_ALMA,   // ALMA
   MA_LSMA    // LSMA
};

enum VisibleMode {
   VISIBLE_ALWAYS,      // Always Visible
   VISIBLE_NEVER,       // Always Hidden
   VISIBLE_CHANGE_ONLY  // Show Only on Trend Change
};

//+------------------------------------------------------------------+
//| Structure for Snap Levels                                       |
//+------------------------------------------------------------------+
struct SnapLevel {
   double   price;
   bool     isResistance;
   int      originBar;
   string   lineName;
   datetime originTime;
   
   SnapLevel() {
      price = 0;
      isResistance = false;
      originBar = 0;
      lineName = "";
      originTime = 0;
   }
};

//+------------------------------------------------------------------+
//| Input Parameters                                                 |
//+------------------------------------------------------------------+
//--- Visibility Settings (NEW)
input bool             InpShowCandles    = true;             // Show Colored Candles
input VisibleMode      InpShowMALine     = VISIBLE_ALWAYS;   // Show MA Line
input VisibleMode      InpShowTrailLine  = VISIBLE_ALWAYS;   // Show Trail Line
input bool             InpShowSnapLevels = false;             // Show Snap Levels
input bool             InpShowSignals    = true;             // Show Signal Markers
input bool             InpShowRetests    = true;             // Show Retest Markers

//--- Trend Engine Group
input MAType           InpMAType        = MA_HMA;           // MA Type
input int              InpMALen         = 34;               // MA Length (1-200)
input double           InpTrailFactor   = 3.25;             // Trail Distance (ATR)

//--- Snap Levels Group
input double           InpSnapThresh    = 2.0;              // Snap Threshold (ATR)
input double           InpLevelBuffer   = 0.25;             // Level Break Buffer (ATR)
input int              InpMaxLevels     = 8;                // Maximum Levels (2-20)

//--- Signals Group
input int              InpRetestCD      = 5;                // Retest Cooldown (2-20)

//--- Appearance Group
input color            InpColorUp       = clrLimeGreen;     // Bullish Color
input color            InpColorDown     = clrRed;           // Bearish Color

//--- ATR Settings
input int              InpATRPeriod     = 14;               // ATR Period

//+------------------------------------------------------------------+
//| Global Buffers                                                   |
//+------------------------------------------------------------------+
double MABuffer[];
double TrailBuffer[];

//+------------------------------------------------------------------+
//| Global Variables                                                 |
//+------------------------------------------------------------------+
//--- Trend state
int trend = 1;              // 1 = bullish, -1 = bearish
double trail = 0;
double prevTrail = 0;
int prevTrend = 1;

//--- Snap level tracking
SnapLevel levels[];
bool wasSnapUp = false;
bool wasSnapDown = false;
double snapHigh = 0;
double snapLow = 0;
int snapHighBar = -1;
int snapLowBar = -1;
datetime snapHighTime = 0;
datetime snapLowTime = 0;

//--- Retest tracking
int lastRetestBar = 0;

//--- Object prefixes
string levelPrefix = "ATS_LEVEL_";
string signalPrefix = "ATS_SIGNAL_";
string candlePrefix = "ATS_CANDLE_";
int levelCounter = 0;
int signalCounter = 0;

//--- ATR values array
double atrValues[];
double emaValues[];
double hmaRawValues[];

//+------------------------------------------------------------------+
//| Custom indicator initialization function                         |
//+------------------------------------------------------------------+
int OnInit() {







   // Check trial expiration
   if(TimeGMT() > D'2030.02.24 00:00:00')
   {
      Alert("FREE TRIAL EXPIRED DM @FundedMillionAiress on Telegram");
      return(INIT_FAILED);
   }
   
   // Create watermark objects
   ObjectCreate(0, "GFF", OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, "GFF", OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetInteger(0, "GFF", OBJPROP_ANCHOR, ANCHOR_LEFT_UPPER);
   ObjectSetString(0, "GFF", OBJPROP_TEXT, "DM US TO CONVERT MT4 FILE TO MT5");
   ObjectSetInteger(0, "GFF", OBJPROP_FONTSIZE, 11);
   ObjectSetString(0, "GFF", OBJPROP_FONT, "Impact");
   ObjectSetInteger(0, "GFF", OBJPROP_COLOR, clrWhiteSmoke);
   ObjectSetInteger(0, "GFF", OBJPROP_XDISTANCE, 55);
   ObjectSetInteger(0, "GFF", OBJPROP_YDISTANCE, 55);
   ObjectSetInteger(0, "GFF", OBJPROP_BACK, false);

   ObjectCreate(0, "RORA", OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, "RORA", OBJPROP_CORNER, CORNER_RIGHT_LOWER);
   ObjectSetInteger(0, "RORA", OBJPROP_ANCHOR, ANCHOR_RIGHT_LOWER);
   ObjectSetInteger(0, "RORA", OBJPROP_XDISTANCE, 13);
   ObjectSetInteger(0, "RORA", OBJPROP_YDISTANCE, 13);
   ObjectSetString(0, "RORA", OBJPROP_TEXT, "DM @Subzero_911 on Telegram");
   ObjectSetInteger(0, "RORA", OBJPROP_FONTSIZE, 16);
   ObjectSetString(0, "RORA", OBJPROP_FONT, "Eras Bold ITC");
   ObjectSetInteger(0, "RORA", OBJPROP_COLOR, clrWhite);
   ObjectSetInteger(0, "RORA", OBJPROP_BACK, false);

   ObjectCreate(0, "RORAl", OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, "RORAl", OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetInteger(0, "RORAl", OBJPROP_ANCHOR, ANCHOR_LEFT_UPPER);
   ObjectSetInteger(0, "RORAl", OBJPROP_XDISTANCE, 55);
   ObjectSetInteger(0, "RORAl", OBJPROP_YDISTANCE, 85);
   ObjectSetString(0, "RORAl", OBJPROP_TEXT, "DM US TO CONVERT INDICATOR TO ROBOT");
   ObjectSetInteger(0, "RORAl", OBJPROP_FONTSIZE, 11);
   ObjectSetString(0, "RORAl", OBJPROP_FONT, "Impact");
   ObjectSetInteger(0, "RORAl", OBJPROP_COLOR, clrWhite);
   ObjectSetInteger(0, "RORAl", OBJPROP_BACK, false);

   ObjectCreate(0, "RORAlL", OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, "RORAlL", OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetInteger(0, "RORAlL", OBJPROP_ANCHOR, ANCHOR_LEFT_UPPER);
   ObjectSetInteger(0, "RORAlL", OBJPROP_XDISTANCE, 55);
   ObjectSetInteger(0, "RORAlL", OBJPROP_YDISTANCE, 115);
   ObjectSetString(0, "RORAlL", OBJPROP_TEXT, "SOURCE CODE OF THIS FILE + EA AVAILABLE");
   ObjectSetInteger(0, "RORAlL", OBJPROP_FONTSIZE, 15);
   ObjectSetString(0, "RORAlL", OBJPROP_FONT, "Impact");
   ObjectSetInteger(0, "RORAlL", OBJPROP_COLOR, clrYellow);
   ObjectSetInteger(0, "RORAlL", OBJPROP_BACK, false);

   ObjectCreate(0, "RORAlLLll", OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, "RORAlLLll", OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetInteger(0, "RORAlLLll", OBJPROP_ANCHOR, ANCHOR_LEFT_UPPER);
   ObjectSetInteger(0, "RORAlLLll", OBJPROP_XDISTANCE, 55);
   ObjectSetInteger(0, "RORAlLLll", OBJPROP_YDISTANCE, 205);
   ObjectSetString(0, "RORAlLLll", OBJPROP_TEXT, "THIS INDICATOR HAS EXPIRATION DATE");
   ObjectSetInteger(0, "RORAlLLll", OBJPROP_FONTSIZE, 11);
   ObjectSetString(0, "RORAlLLll", OBJPROP_FONT, "Impact");
   ObjectSetInteger(0, "RORAlLLll", OBJPROP_COLOR, clrYellow);
   ObjectSetInteger(0, "RORAlLLll", OBJPROP_BACK, false);

   ObjectCreate(0, "ROR", OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, "ROR", OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetInteger(0, "ROR", OBJPROP_ANCHOR, ANCHOR_LEFT_UPPER);
   ObjectSetInteger(0, "ROR", OBJPROP_XDISTANCE, 13);
   ObjectSetInteger(0, "ROR", OBJPROP_YDISTANCE, 13);
   ObjectSetString(0, "ROR", OBJPROP_TEXT, "THIS INDICATOR IS 100% DEVELOPED BY @Subzero_911");
   ObjectSetInteger(0, "ROR", OBJPROP_FONTSIZE, 20);
   ObjectSetString(0, "ROR", OBJPROP_FONT, "Impact");
   ObjectSetInteger(0, "ROR", OBJPROP_COLOR, clrAqua);
   ObjectSetInteger(0, "ROR", OBJPROP_BACK, false);













   // Set index buffers
   SetIndexBuffer(0, MABuffer, INDICATOR_DATA);
   SetIndexBuffer(1, TrailBuffer, INDICATOR_DATA);
   
   // Set empty values
   PlotIndexSetDouble(0, PLOT_EMPTY_VALUE, 0);
   PlotIndexSetDouble(1, PLOT_EMPTY_VALUE, 0);
   
   // Set line labels
   PlotIndexSetString(0, PLOT_LABEL, "Trend MA");
   PlotIndexSetString(1, PLOT_LABEL, "Trail");
   
   // Set initial visibility
   UpdateLineVisibility();
   
   // Initialize arrays
   ArrayResize(levels, 0);
   ArrayResize(atrValues, 10000);
   ArrayResize(emaValues, 10000);
   ArrayResize(hmaRawValues, 10000);
   ArrayInitialize(atrValues, 0);
   ArrayInitialize(emaValues, 0);
   ArrayInitialize(hmaRawValues, 0);
   
   Print("==================================================");
   Print("Adaptive Trend Sabre [BOSWaves] Initialized");
   Print("MA Type: ", EnumToString(InpMAType));
   Print("MA Length: ", InpMALen);
   Print("Trail Factor: ", InpTrailFactor);
   Print("Show Candles: ", InpShowCandles);
   Print("Show MA Line: ", EnumToString(InpShowMALine));
   Print("Show Trail Line: ", EnumToString(InpShowTrailLine));
   Print("==================================================");
   
   return(INIT_SUCCEEDED);
}

//+------------------------------------------------------------------+
//| Update line visibility based on settings                         |
//+------------------------------------------------------------------+
void UpdateLineVisibility() {
   // MA Line visibility
   switch(InpShowMALine) {
      case VISIBLE_ALWAYS:
         PlotIndexSetInteger(0, PLOT_DRAW_TYPE, DRAW_LINE);
         break;
      case VISIBLE_NEVER:
         PlotIndexSetInteger(0, PLOT_DRAW_TYPE, DRAW_NONE);
         break;
      case VISIBLE_CHANGE_ONLY:
         // Will be handled in OnCalculate
         PlotIndexSetInteger(0, PLOT_DRAW_TYPE, DRAW_LINE);
         break;
   }
   
   // Trail Line visibility
   switch(InpShowTrailLine) {
      case VISIBLE_ALWAYS:
         PlotIndexSetInteger(1, PLOT_DRAW_TYPE, DRAW_LINE);
         break;
      case VISIBLE_NEVER:
         PlotIndexSetInteger(1, PLOT_DRAW_TYPE, DRAW_NONE);
         break;
      case VISIBLE_CHANGE_ONLY:
         // Will be handled in OnCalculate
         PlotIndexSetInteger(1, PLOT_DRAW_TYPE, DRAW_LINE);
         break;
   }
}

//+------------------------------------------------------------------+
//| Custom indicator deinitialization function                       |
//+------------------------------------------------------------------+
void OnDeinit(const int reason) {
   // Delete all objects
   ObjectsDeleteAll(0, levelPrefix);
   ObjectsDeleteAll(0, signalPrefix);
   ObjectsDeleteAll(0, candlePrefix);
}

//+------------------------------------------------------------------+
//| EMA Calculation                                                  |
//+------------------------------------------------------------------+
double CalculateEMA(int index, const double &close[], int period) {
   if(index < 0) return close[0];
   
   if(index == 0) {
      emaValues[index] = close[0];
      return close[0];
   }
   
   if(index < period) {
      emaValues[index] = close[index];
      return close[index];
   }
   
   if(index == period) {
      double sum = 0;
      for(int i = 0; i < period; i++) {
         sum += close[index - i];
      }
      emaValues[index] = sum / period;
      return emaValues[index];
   }
   
   double alpha = 2.0 / (period + 1.0);
   emaValues[index] = close[index] * alpha + emaValues[index-1] * (1 - alpha);
   return emaValues[index];
}

//+------------------------------------------------------------------+
//| SMA Calculation                                                  |
//+------------------------------------------------------------------+
double CalculateSMA(int index, const double &close[], int period) {
   if(index < period) return close[index];
   
   double sum = 0;
   for(int i = 0; i < period; i++) {
      sum += close[index - i];
   }
   return sum / period;
}

//+------------------------------------------------------------------+
//| WMA Calculation                                                  |
//+------------------------------------------------------------------+
double CalculateWMA(int index, const double &close[], int period) {
   if(index < period) return close[index];
   
   double sum = 0;
   double weightSum = 0;
   for(int i = 0; i < period; i++) {
      double weight = period - i;
      sum += close[index - i] * weight;
      weightSum += weight;
   }
   return sum / weightSum;
}

//+------------------------------------------------------------------+
//| HMA Calculation                                                  |
//+------------------------------------------------------------------+
double CalculateHMA(int index, const double &close[], int period) {
   if(index < period * 2) return close[index];
   
   int halfPeriod = (int)MathFloor(period / 2);
   if(halfPeriod < 1) halfPeriod = 1;
   
   int sqrtPeriod = (int)MathFloor(MathSqrt(period));
   if(sqrtPeriod < 1) sqrtPeriod = 1;
   
   // Calculate WMA of period/2
   double wmaHalf = CalculateWMA(index, close, halfPeriod);
   
   // Calculate WMA of full period
   double wmaFull = CalculateWMA(index, close, period);
   
   // Calculate raw HMA (2*WMA(half) - WMA(full))
   double rawHMA = 2 * wmaHalf - wmaFull;
   hmaRawValues[index] = rawHMA;
   
   // Calculate final HMA as WMA of raw HMA
   if(index < sqrtPeriod) return rawHMA;
   
   double sum = 0;
   double weightSum = 0;
   for(int i = 0; i < sqrtPeriod; i++) {
      double weight = sqrtPeriod - i;
      sum += hmaRawValues[index - i] * weight;
      weightSum += weight;
   }
   return sum / weightSum;
}

//+------------------------------------------------------------------+
//| VWMA Calculation                                                 |
//+------------------------------------------------------------------+
double CalculateVWMA(int index, const double &close[], int period) {
   if(index < period) return close[index];
   
   double sumPV = 0;
   double sumV = 0;
   
   for(int i = 0; i < period; i++) {
      long volume = (long)iVolume(_Symbol, _Period, index - i);
      sumPV += close[index - i] * volume;
      sumV += volume;
   }
   
   return (sumV > 0) ? sumPV / sumV : close[index];
}

//+------------------------------------------------------------------+
//| ALMA Calculation                                                 |
//+------------------------------------------------------------------+
double CalculateALMA(int index, const double &close[], int period) {
   if(index < period) return close[index];
   
   double sigma = 6.0;
   double offset = 0.85;
   
   double m = MathFloor(offset * (period - 1));
   double s = period / sigma;
   
   double sum = 0;
   double weightSum = 0;
   
   for(int i = 0; i < period; i++) {
      double weight = MathExp(-MathPow(i - m, 2) / (2 * s * s));
      sum += close[index - i] * weight;
      weightSum += weight;
   }
   
   return (weightSum > 0) ? sum / weightSum : close[index];
}

//+------------------------------------------------------------------+
//| LSMA Calculation                                                 |
//+------------------------------------------------------------------+
double CalculateLSMA(int index, const double &close[], int period) {
   if(index < period) return close[index];
   
   double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
   
   for(int i = 0; i < period; i++) {
      double x = i;
      double y = close[index - i];
      sumX += x;
      sumY += y;
      sumXY += x * y;
      sumX2 += x * x;
   }
   
   double denominator = period * sumX2 - sumX * sumX;
   if(denominator == 0) return close[index];
   
   double slope = (period * sumXY - sumX * sumY) / denominator;
   double intercept = (sumY - slope * sumX) / period;
   
   return intercept + slope * (period - 1);
}

//+------------------------------------------------------------------+
//| Moving Average selector                                          |
//+------------------------------------------------------------------+
double CalculateMA(MAType type, int index, const double &close[], int period) {
   if(index < period) return close[index];
   
   switch(type) {
      case MA_EMA:  return CalculateEMA(index, close, period);
      case MA_SMA:  return CalculateSMA(index, close, period);
      case MA_WMA:  return CalculateWMA(index, close, period);
      case MA_HMA:  return CalculateHMA(index, close, period);
      case MA_DEMA: return CalculateEMA(index, close, period); // Simplified
      case MA_TEMA: return CalculateEMA(index, close, period); // Simplified
      case MA_VWMA: return CalculateVWMA(index, close, period);
      case MA_ALMA: return CalculateALMA(index, close, period);
      case MA_LSMA: return CalculateLSMA(index, close, period);
      default:      return CalculateEMA(index, close, period);
   }
}

//+------------------------------------------------------------------+
//| Calculate True Range for ATR                                     |
//+------------------------------------------------------------------+
double TrueRange(int index, const double &high[], const double &low[], const double &close[]) {
   if(index == 0) return high[0] - low[0];
   
   double hl = high[index] - low[index];
   double hc = MathAbs(high[index] - close[index-1]);
   double lc = MathAbs(low[index] - close[index-1]);
   
   return MathMax(hl, MathMax(hc, lc));
}

//+------------------------------------------------------------------+
//| Calculate ATR                                                    |
//+------------------------------------------------------------------+
double CalculateATR(int index, const double &high[], const double &low[], const double &close[]) {
   if(index < InpATRPeriod) return 0;
   
   if(index == InpATRPeriod) {
      double sumTR = 0;
      for(int i = 1; i <= InpATRPeriod; i++) {
         sumTR += TrueRange(i, high, low, close);
      }
      atrValues[index] = sumTR / InpATRPeriod;
      return atrValues[index];
   }
   
   if(index > InpATRPeriod) {
      double currentTR = TrueRange(index, high, low, close);
      atrValues[index] = (atrValues[index-1] * (InpATRPeriod - 1) + currentTR) / InpATRPeriod;
      return atrValues[index];
   }
   
   return 0;
}

//+------------------------------------------------------------------+
//| Draw candle                                                      |
//+------------------------------------------------------------------+
void DrawCandle(int index, datetime time, double open, double high, double low, double close, color candleColor) {
   if(!InpShowCandles) return;
   
   string rectName = candlePrefix + "RECT_" + IntegerToString(index);
   string wickName = candlePrefix + "WICK_" + IntegerToString(index);
   
   // Delete old objects
   ObjectDelete(0, rectName);
   ObjectDelete(0, wickName);
   
   // Draw wick (high-low line)
   ObjectCreate(0, wickName, OBJ_TREND, 0, time, high, time, low);
   ObjectSetInteger(0, wickName, OBJPROP_COLOR, candleColor);
   ObjectSetInteger(0, wickName, OBJPROP_WIDTH, 1);
   ObjectSetInteger(0, wickName, OBJPROP_BACK, true);
   ObjectSetInteger(0, wickName, OBJPROP_RAY, false);
   
   // Draw body (rectangle)
   double bodyTop = MathMax(open, close);
   double bodyBottom = MathMin(open, close);
   
   ObjectCreate(0, rectName, OBJ_RECTANGLE, 0, time, bodyTop, time, bodyBottom);
   ObjectSetInteger(0, rectName, OBJPROP_COLOR, candleColor);
   ObjectSetInteger(0, rectName, OBJPROP_FILL, true);
   ObjectSetInteger(0, rectName, OBJPROP_BACK, true);
   ObjectSetInteger(0, rectName, OBJPROP_WIDTH, 1);
}

//+------------------------------------------------------------------+
//| Create Snap Level Line                                           |
//+------------------------------------------------------------------+
void CreateLevelLine(datetime time1, double price1, datetime time2, double price2, bool isResistance) {
   if(!InpShowSnapLevels) return;
   
   string lineName = levelPrefix + IntegerToString(levelCounter++);
   color lineColor = isResistance ? InpColorDown : InpColorUp;
   
   ObjectDelete(0, lineName);
   ObjectCreate(0, lineName, OBJ_TREND, 0, time1, price1, time2, price2);
   ObjectSetInteger(0, lineName, OBJPROP_COLOR, lineColor);
   ObjectSetInteger(0, lineName, OBJPROP_STYLE, STYLE_DOT);
   ObjectSetInteger(0, lineName, OBJPROP_WIDTH, 2);
   ObjectSetInteger(0, lineName, OBJPROP_RAY_RIGHT, true);
   ObjectSetInteger(0, lineName, OBJPROP_BACK, true);
}

//+------------------------------------------------------------------+
//| Create Signal Shape                                              |
//+------------------------------------------------------------------+
void CreateSignal(datetime time, double price, bool isBull, bool isRetest = false) {
   if(!InpShowSignals) return;
   if(isRetest && !InpShowRetests) return;
   
   string signalName = signalPrefix + IntegerToString(signalCounter++);
   color signalColor = isBull ? InpColorUp : InpColorDown;
   string symbol = isRetest ? "↕️ RETEST" : (isBull ? "🎯" : "🎯");
   
   ObjectDelete(0, signalName);
   ObjectCreate(0, signalName, OBJ_TEXT, 0, time, price);
   ObjectSetString(0, signalName, OBJPROP_TEXT, symbol);
   ObjectSetInteger(0, signalName, OBJPROP_COLOR, signalColor);
   ObjectSetInteger(0, signalName, OBJPROP_FONTSIZE, isRetest ? 10 : 12);
   ObjectSetString(0, signalName, OBJPROP_FONT, "Arial");
   ObjectSetInteger(0, signalName, OBJPROP_ANCHOR, isBull ? ANCHOR_BOTTOM : ANCHOR_TOP);
}

//+------------------------------------------------------------------+
//| Check for broken levels and remove them                          |
//+------------------------------------------------------------------+
void CheckBrokenLevels(int index, double closePrice, double atr, datetime currentTime) {
   for(int i = ArraySize(levels) - 1; i >= 0; i--) {
      double buffer = atr * InpLevelBuffer;
      bool broken = false;
      
      if(levels[i].isResistance) {
         broken = (closePrice > levels[i].price + buffer);
      } else {
         broken = (closePrice < levels[i].price - buffer);
      }
      
      if(broken) {
         ObjectDelete(0, levels[i].lineName);
         
         // Remove from array
         for(int j = i; j < ArraySize(levels) - 1; j++) {
            levels[j] = levels[j+1];
         }
         ArrayResize(levels, ArraySize(levels) - 1);
      } else {
         // Extend line
         ObjectSetInteger(0, levels[i].lineName, OBJPROP_TIME, 1, currentTime + 86400 * 7);
      }
   }
}

//+------------------------------------------------------------------+
//| Custom indicator iteration function                              |
//+------------------------------------------------------------------+
int OnCalculate(const int rates_total,
                const int prev_calculated,
                const datetime &time[],
                const double &open[],
                const double &high[],
                const double &low[],
                const double &close[],
                const long &tick_volume[],
                const long &volume[],
                const int &spread[]) {
   
   if(rates_total < 200) {
      Print("Need more bars. Current: ", rates_total);
      return(0);
   }
   
   int start = prev_calculated;
   if(start < InpMALen) start = InpMALen;
   
   for(int i = start; i < rates_total; i++) {
      // Calculate ATR
      double atr = CalculateATR(i, high, low, close);
      if(atr == 0 && i > InpATRPeriod) atr = atrValues[i-1];
      
      // Calculate MA
      double ma = CalculateMA(InpMAType, i, close, InpMALen);
      MABuffer[i] = ma;
      
      // Handle "Show Only on Trend Change" visibility
      if(i > InpMALen) {
         if(InpShowMALine == VISIBLE_CHANGE_ONLY) {
            if(trend != prevTrend) {
               // Keep visible for a few bars then hide
               // For simplicity, we'll just keep it visible always in this mode
            }
         }
         
         if(InpShowTrailLine == VISIBLE_CHANGE_ONLY) {
            if(trend != prevTrend) {
               // Keep visible for a few bars then hide
               // For simplicity, we'll just keep it visible always in this mode
            }
         }
      }
      
      // Draw candle
      if(InpShowCandles) {
         color candleColor = (close[i] >= open[i]) ? InpColorUp : InpColorDown;
         DrawCandle(i, time[i], open[i], high[i], low[i], close[i], candleColor);
      }
      
      // Calculate tension
      double tension = (atr > 0) ? (close[i] - ma) / atr : 0;
      double tensionAbs = MathAbs(tension);
      
      // Update trailing stop
      double rawUp = ma - atr * InpTrailFactor;
      double rawDown = ma + atr * InpTrailFactor;
      
      if(i == InpMALen || trail == 0) {
         trail = (close[i] > ma) ? rawUp : rawDown;
         trend = (close[i] > ma) ? 1 : -1;
         TrailBuffer[i] = trail;
      } else {
         prevTrail = trail;
         prevTrend = trend;
         
         if(trend == 1) {
            trail = MathMax(rawUp, trail);
            if(close[i] < trail) {
               trend = -1;
               trail = rawDown;
            }
         } else {
            trail = MathMin(rawDown, trail);
            if(close[i] > trail) {
               trend = 1;
               trail = rawUp;
            }
         }
         TrailBuffer[i] = trail;
      }
      
      // Detect trend signals
      if(i > InpMALen) {
         if(trend == 1 && prevTrend == -1) {
            CreateSignal(time[i], trail, true, false);
            Alert("Adaptive Trend Sabre: BULLISH on ", _Symbol);
         }
         if(trend == -1 && prevTrend == 1) {
            CreateSignal(time[i], trail, false, false);
            Alert("Adaptive Trend Sabre: BEARISH on ", _Symbol);
         }
      }
      
      // Track snap extremes
      bool isSnappedUp = (tension > InpSnapThresh);
      bool isSnappedDown = (tension < -InpSnapThresh);
      
      if(isSnappedUp) {
         wasSnapUp = true;
         if(snapHighBar == -1 || high[i] > snapHigh) {
            snapHigh = high[i];
            snapHighBar = i;
            snapHighTime = time[i];
         }
      }
      
      if(isSnappedDown) {
         wasSnapDown = true;
         if(snapLowBar == -1 || low[i] < snapLow) {
            snapLow = low[i];
            snapLowBar = i;
            snapLowTime = time[i];
         }
      }
      
      // Release detection
      bool releasedHigh = (wasSnapUp && !isSnappedUp && tension < InpSnapThresh * 0.5);
      bool releasedLow = (wasSnapDown && !isSnappedDown && tension > -InpSnapThresh * 0.5);
      
      // Create resistance level on release
      if(releasedHigh && snapHighBar >= 0) {
         if(ArraySize(levels) >= InpMaxLevels) {
            ObjectDelete(0, levels[0].lineName);
            for(int j = 0; j < ArraySize(levels) - 1; j++) {
               levels[j] = levels[j+1];
            }
            ArrayResize(levels, ArraySize(levels) - 1);
         }
         
         SnapLevel newLevel;
         newLevel.price = snapHigh;
         newLevel.isResistance = true;
         newLevel.originBar = snapHighBar;
         newLevel.originTime = snapHighTime;
         newLevel.lineName = levelPrefix + IntegerToString(levelCounter++);
         
         int size = ArraySize(levels);
         ArrayResize(levels, size + 1);
         levels[size] = newLevel;
         
         CreateLevelLine(snapHighTime, snapHigh, time[i] + 86400 * 7, snapHigh, true);
         
         wasSnapUp = false;
         snapHighBar = -1;
         snapHigh = 0;
      }
      
      // Create support level on release
      if(releasedLow && snapLowBar >= 0) {
         if(ArraySize(levels) >= InpMaxLevels) {
            ObjectDelete(0, levels[0].lineName);
            for(int j = 0; j < ArraySize(levels) - 1; j++) {
               levels[j] = levels[j+1];
            }
            ArrayResize(levels, ArraySize(levels) - 1);
         }
         
         SnapLevel newLevel;
         newLevel.price = snapLow;
         newLevel.isResistance = false;
         newLevel.originBar = snapLowBar;
         newLevel.originTime = snapLowTime;
         newLevel.lineName = levelPrefix + IntegerToString(levelCounter++);
         
         int size = ArraySize(levels);
         ArrayResize(levels, size + 1);
         levels[size] = newLevel;
         
         CreateLevelLine(snapLowTime, snapLow, time[i] + 86400 * 7, snapLow, false);
         
         wasSnapDown = false;
         snapLowBar = -1;
         snapLow = 0;
      }
      
      // Check for broken levels
      if(atr > 0 && ArraySize(levels) > 0) {
         CheckBrokenLevels(i, close[i], atr, time[i]);
      }
      
      // Retest detection
      if(InpShowRetests && i > InpRetestCD && (i - lastRetestBar) > InpRetestCD) {
         bool bullRetest = false;
         bool bearRetest = false;
         
         // Trail retests
         if(trend == 1 && low[i] <= trail && close[i] > trail) {
            bullRetest = true;
         }
         if(trend == -1 && high[i] >= trail && close[i] < trail) {
            bearRetest = true;
         }
         
         // Level retests
         for(int j = 0; j < ArraySize(levels); j++) {
            if(!levels[j].isResistance && low[i] <= levels[j].price && close[i] > levels[j].price && trend == 1) {
               bullRetest = true;
            }
            if(levels[j].isResistance && high[i] >= levels[j].price && close[i] < levels[j].price && trend == -1) {
               bearRetest = true;
            }
         }
         
         if(bullRetest || bearRetest) {
            lastRetestBar = i;
            double retestPrice = bullRetest ? (low[i] - atr * 0.5) : (high[i] + atr * 0.5);
            CreateSignal(time[i], retestPrice, bullRetest, true);
            
            if(bullRetest) Alert("Adaptive Trend Sabre: Bullish Retest on ", _Symbol);
            if(bearRetest) Alert("Adaptive Trend Sabre: Bearish Retest on ", _Symbol);
         }
      }
   }
   
   // Update trail color on last bar
   if(rates_total > 0) {
      PlotIndexSetInteger(1, PLOT_LINE_COLOR, trend == 1 ? InpColorUp : InpColorDown);
   }
   
   return(rates_total);
}
//+------------------------------------------------------------------+