//+------------------------------------------------------------------+
//|                                           MB_Sniper_Indicator.mq5      |
//|                        SuperTrend + CCI Pullback Sniper           |
//|                                  Built from scratch — clean logic |
//+------------------------------------------------------------------+
#property copyright   "2026, Prof. Morris"
#property link        "https://t.me/profitabletrader2362"
#property version     "1.02"
#property description "SuperTrend trend filter + CCI pullback entries"
#property description "Signal Logic: SuperTrend Flip | Multi-TP (3 Levels)"
#property indicator_chart_window
#property indicator_buffers 6
#property indicator_plots   3
//=== Plot 0: SuperTrend colored line (2 buffers: data + color) ===
#property indicator_label1  "SuperTrend"
#property indicator_type1   DRAW_COLOR_LINE
#property indicator_color1  clrLime,clrRed
#property indicator_style1  STYLE_SOLID
#property indicator_width1  2
//=== Plot 1: Buy arrows (1 buffer) ===
#property indicator_label2  "Buy"
#property indicator_type2   DRAW_ARROW
#property indicator_color2  clrDodgerBlue
#property indicator_width2  3
//=== Plot 2: Sell arrows (1 buffer) ===
#property indicator_label3  "Sell"
#property indicator_type3   DRAW_ARROW
#property indicator_color3  clrOrangeRed
#property indicator_width3  3
//+------------------------------------------------------------------+
//| INPUTS                                                            |
//+------------------------------------------------------------------+
input group          "——— SuperTrend ———"
input int            InpAtrPeriod   = 10;    // ATR Period
input double         InpAtrMult     = 3.0;   // ATR Multiplier
input group          "——— CCI ———"
input int            InpCciPeriod   = 20;    // CCI Period
input int            InpCciLevel    = 100;   // CCI Threshold (+/-)
input group          "——— Risk Management ———"
input double         InpRR1         = 1.0;   // TP1 RR Ratio
input double         InpRR2         = 2.0;   // TP2 RR Ratio
input double         InpRR3         = 3.0;   // TP3 RR Ratio
input double         InpMinSL_ATR   = 0.5;   // Minimum SL (x ATR)
input group          "——— Display ———"
input bool           InpShowLevels  = true;  // Show Entry / SL / TP lines
input color          InpClrEntry    = clrGold;
input color          InpClrSL       = clrRed;
input color          InpClrTP       = clrDeepSkyBlue;
input group          "——— Alerts ———"
input bool           InpAlertPopup  = true;
input bool           InpAlertPush   = true;
input bool           InpAlertSound  = true;
//+------------------------------------------------------------------+
//| BUFFERS                                                           |
//+------------------------------------------------------------------+
//  Index  Purpose              Mapping
//  ─────  ───────────────────  ──────────────
//  0      SuperTrend value     Plot 0 data
//  1      SuperTrend color     Plot 0 color index
//  2      Buy arrow price      Plot 1 data
//  3      Sell arrow price     Plot 2 data
//  4      Upper band (calc)    internal
//  5      Lower band (calc)    internal
double BufST[];
double BufSTClr[];
double BufBuy[];
double BufSell[];
double BufUp[];
double BufDn[];
//+------------------------------------------------------------------+
//| HANDLES & STATE                                                   |
//+------------------------------------------------------------------+
int      hATR, hCCI;
datetime gAlertTime   = 0;
string   gSigType     = "";
double   gEntry       = 0;
double   gSL          = 0;
double   gTP1         = 0;
double   gTP2         = 0;
double   gTP3         = 0;
datetime gSigTime     = 0;
//+------------------------------------------------------------------+
//| INIT                                                              |
//+------------------------------------------------------------------+
int OnInit()
{
   // ---- bind buffers ----
   SetIndexBuffer(0, BufST,    INDICATOR_DATA);
   SetIndexBuffer(1, BufSTClr, INDICATOR_COLOR_INDEX);
   SetIndexBuffer(2, BufBuy,   INDICATOR_DATA);
   SetIndexBuffer(3, BufSell,  INDICATOR_DATA);
   SetIndexBuffer(4, BufUp,    INDICATOR_CALCULATIONS);
   SetIndexBuffer(5, BufDn,    INDICATOR_CALCULATIONS);
   // ---- configure arrow plots (PLOT index, not buffer index!) ----
   PlotIndexSetInteger(1, PLOT_ARROW, 233);             // Buy  = up triangle
   PlotIndexSetInteger(2, PLOT_ARROW, 234);             // Sell = down triangle
   PlotIndexSetDouble(1,  PLOT_EMPTY_VALUE, EMPTY_VALUE);
   PlotIndexSetDouble(2,  PLOT_EMPTY_VALUE, EMPTY_VALUE);
   // ---- create indicator handles ----
   hATR = iATR(NULL, 0, InpAtrPeriod);
   hCCI = iCCI(NULL, 0, InpCciPeriod, PRICE_TYPICAL);
   if(hATR == INVALID_HANDLE || hCCI == INVALID_HANDLE)
   {
      Print("ST_CCI_Sniper: failed to create indicator handles");
      return INIT_FAILED;
   }
   IndicatorSetString(INDICATOR_SHORTNAME, "MB Sniper");
   return INIT_SUCCEEDED;
}
//+------------------------------------------------------------------+
//| DEINIT                                                            |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   ObjectsDeleteAll(0, "SCS_");
}
//+------------------------------------------------------------------+
//| MAIN CALCULATION                                                  |
//+------------------------------------------------------------------+
int OnCalculate(const int        rates_total,
                const int        prev_calculated,
                const datetime  &time[],
                const double    &open[],
                const double    &high[],
                const double    &low[],
                const double    &close[],
                const long      &tick_volume[],
                const long      &volume[],
                const int       &spread[])
{
   // ---- minimum bars required ----
   int minBars = MathMax(InpAtrPeriod, InpCciPeriod) + 2;
   if(rates_total < minBars) return 0;
   // ---- copy ATR & CCI for ALL bars ----
   double atr[], cci[];
   ArraySetAsSeries(atr, true);
   ArraySetAsSeries(cci, true);
   if(CopyBuffer(hATR, 0, 0, rates_total, atr) <= 0) return 0;
   if(CopyBuffer(hCCI, 0, 0, rates_total, cci) <= 0) return 0;
   // ---- determine start bar ----
   int start;
   if(prev_calculated == 0)
   {
      start = 1;
      ArrayInitialize(BufBuy,  EMPTY_VALUE);
      ArrayInitialize(BufSell, EMPTY_VALUE);
   }
   else
      start = prev_calculated - 1;
   // ================================================================
   //  MAIN LOOP — runs on every bar from start to end
   // ================================================================
   for(int i = start; i < rates_total; i++)
   {
      // shift converts forward-index i to reverse-index for atr[]/cci[]
      int s = rates_total - 1 - i;
      if(s < 0) continue;
      // default: no arrow on this bar
      if(i >= prev_calculated)
      {
         BufBuy[i]  = EMPTY_VALUE;
         BufSell[i] = EMPTY_VALUE;
      }
      // ============================================================
      //  1. SUPERTREND
      // ============================================================
      double a = atr[s];
      if(a <= 0) continue;
      double hl2         = (high[i] + low[i]) * 0.5;
      double basicUp     = hl2 + InpAtrMult * a;
      double basicDn     = hl2 - InpAtrMult * a;
      // --- band locking (standard SuperTrend rule) ---
      double prevUp = (i > 0) ? BufUp[i-1] : basicUp;
      double prevDn = (i > 0) ? BufDn[i-1] : basicDn;
      // Upper band: only tighten (lower), never widen while price is below
      if(basicUp < prevUp || (i > 0 && close[i-1] > prevUp))
         BufUp[i] = basicUp;
      else
         BufUp[i] = prevUp;
      // Lower band: only tighten (raise), never widen while price is above
      if(basicDn > prevDn || (i > 0 && close[i-1] < prevDn))
         BufDn[i] = basicDn;
      else
         BufDn[i] = prevDn;
      // --- trend direction ---
      bool bull;
      bool wasBull = true; // defaulting to true for i=0 case
      
      if(i == 0)
      {
         bull = true;
         wasBull = true; 
      }
      else
      {
         wasBull = (BufSTClr[i-1] == 0.0);
         if(wasBull)
            bull = (close[i] >= BufDn[i]);   // stay bull unless close breaks below lower band
         else
            bull = (close[i] >  BufUp[i]);   // flip bull only if close breaks above upper band
      }
      if(bull)
      {
         BufST[i]    = BufDn[i];      // show lower band as support
         BufSTClr[i] = 0.0;           // green
      }
      else
      {
         BufST[i]    = BufUp[i];      // show upper band as resistance
         BufSTClr[i] = 1.0;           // red
      }
      // ============================================================
      //  2. SIGNALS — on every CLOSED bar (skip the live bar)
      // ============================================================
      if(i >= rates_total - 1) continue;      // bar[rates_total-1] is still forming
      // ---- BUY: SuperTrend flips from Bear to Bull ----
      if(bull && !wasBull)
      {
         double entry  = close[i];
         double rawSL  = BufST[i];                          // SuperTrend = dynamic support
         double minDist = a * InpMinSL_ATR;
         if(entry - rawSL < minDist) rawSL = entry - minDist;  // enforce minimum SL
         double risk   = entry - rawSL;
         
         double tp1    = entry + risk * InpRR1;
         double tp2    = entry + risk * InpRR2;
         double tp3    = entry + risk * InpRR3;
         BufBuy[i] = low[i] - a * 0.15;    // arrow just below the low
         gSigType = "BUY";   gEntry = entry;
         gSL      = rawSL;   
         gTP1     = tp1;     gTP2 = tp2;    gTP3 = tp3;
         gSigTime = time[i];
         if(i == rates_total - 2) FireAlert("BUY Signal");
      }
      // ---- SELL: SuperTrend flips from Bull to Bear ----
      if(!bull && wasBull)
      {
         double entry  = close[i];
         double rawSL  = BufST[i];                          // SuperTrend = dynamic resistance
         double minDist = a * InpMinSL_ATR;
         if(rawSL - entry < minDist) rawSL = entry + minDist;
         double risk   = rawSL - entry;
         
         double tp1    = entry - risk * InpRR1;
         double tp2    = entry - risk * InpRR2;
         double tp3    = entry - risk * InpRR3;
         BufSell[i] = high[i] + a * 0.15;  // arrow just above the high
         gSigType = "SELL";  gEntry = entry;
         gSL      = rawSL;   
         gTP1     = tp1;     gTP2 = tp2;    gTP3 = tp3;
         gSigTime = time[i];
         if(i == rates_total - 2) FireAlert("SELL Signal");
      }
   }
   // ---- draw levels for the last signal ----
   if(InpShowLevels) DrawLevels(rates_total, time);
   return rates_total;
}
//+------------------------------------------------------------------+
//|  DRAW ENTRY / SL / TP LINES + LABELS                             |
//+------------------------------------------------------------------+
void DrawLevels(int total, const datetime &time[])
{
   if(gEntry == 0) return;
   datetime t1 = gSigTime;
   datetime t2 = time[total - 1] + PeriodSeconds() * 6;
   double riskPts  = MathAbs(gEntry - gSL) / _Point;
   
   // helper: delete + recreate is cleaner than move for trend lines
   CreateTrendLine("SCS_LnEntry", t1, gEntry, t2, gEntry, InpClrEntry, STYLE_SOLID, 2);
   CreateTrendLine("SCS_LnSL",   t1, gSL,    t2, gSL,    InpClrSL,    STYLE_DOT,   2);
   
   CreateTrendLine("SCS_LnTP1",  t1, gTP1,   t2, gTP1,   InpClrTP,    STYLE_DOT,   2);
   CreateTrendLine("SCS_LnTP2",  t1, gTP2,   t2, gTP2,   InpClrTP,    STYLE_DOT,   2);
   CreateTrendLine("SCS_LnTP3",  t1, gTP3,   t2, gTP3,   InpClrTP,    STYLE_DOT,   2);
   CreateTextLabel("SCS_TxEntry", t2, gEntry,
                   "  Entry: " + DoubleToString(gEntry, _Digits), InpClrEntry);
   CreateTextLabel("SCS_TxSL", t2, gSL,
                   "  SL: " + DoubleToString(gSL, _Digits) + "  (" + DoubleToString(riskPts, 0) + " pts)", InpClrSL);
                   
   CreateTextLabel("SCS_TxTP1", t2, gTP1, "  TP1", InpClrTP);
   CreateTextLabel("SCS_TxTP2", t2, gTP2, "  TP2", InpClrTP);
   CreateTextLabel("SCS_TxTP3", t2, gTP3, "  TP3", InpClrTP);
   // ---- status panel (top-left) ----
   color  panelClr = (gSigType == "BUY") ? clrLime : clrRed;
   string arrow    = (gSigType == "BUY") ? "▲" : "▼";
   CreateScreenLabel("SCS_Status", 20, 30,
      arrow + " " + gSigType + " SIGNAL [CONFIRMED]", "Arial Bold", 11, panelClr);
   CreateScreenLabel("SCS_P1", 20, 52,
      "Entry:  " + DoubleToString(gEntry, _Digits), "Consolas", 10, InpClrEntry);
   CreateScreenLabel("SCS_P2", 20, 70,
      "SL:       " + DoubleToString(gSL, _Digits) + "   (" + DoubleToString(riskPts, 0) + " pts)",
      "Consolas", 10, InpClrSL);
   CreateScreenLabel("SCS_P3", 20, 88,
      "TP1:      " + DoubleToString(gTP1, _Digits), "Consolas", 10, InpClrTP);
   CreateScreenLabel("SCS_P4", 20, 106,
      "TP2:      " + DoubleToString(gTP2, _Digits), "Consolas", 10, InpClrTP);
      
   CreateScreenLabel("SCS_P5", 20, 124,
      "TP3:      " + DoubleToString(gTP3, _Digits), "Consolas", 10, InpClrTP);
}
//+------------------------------------------------------------------+
//|  DRAWING HELPERS                                                  |
//+------------------------------------------------------------------+
void CreateTrendLine(string name, datetime t1, double p1,
                     datetime t2, double p2,
                     color clr, int style, int width)
{
   ObjectDelete(0, name);
   ObjectCreate(0, name, OBJ_TREND, 0, t1, p1, t2, p2);
   ObjectSetInteger(0, name, OBJPROP_COLOR,      clr);
   ObjectSetInteger(0, name, OBJPROP_WIDTH,      width);
   ObjectSetInteger(0, name, OBJPROP_STYLE,      style);
   ObjectSetInteger(0, name, OBJPROP_RAY_RIGHT,  false);
   ObjectSetInteger(0, name, OBJPROP_BACK,       false);
   ObjectSetInteger(0, name, OBJPROP_SELECTABLE,  false);
}
void CreateTextLabel(string name, datetime t, double price,
                     string txt, color clr)
{
   ObjectDelete(0, name);
   ObjectCreate(0, name, OBJ_TEXT, 0, t, price);
   ObjectSetString(0,  name, OBJPROP_TEXT, txt);
   ObjectSetString(0,  name, OBJPROP_FONT, "Arial Bold");
   ObjectSetInteger(0, name, OBJPROP_FONTSIZE, 9);
   ObjectSetInteger(0, name, OBJPROP_COLOR,    clr);
   ObjectSetInteger(0, name, OBJPROP_ANCHOR,   ANCHOR_LEFT);
}
void CreateScreenLabel(string name, int x, int y,
                       string txt, string font, int size, color clr)
{
   if(ObjectFind(0, name) < 0)
      ObjectCreate(0, name, OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, name, OBJPROP_CORNER,   CORNER_LEFT_UPPER);
   ObjectSetInteger(0, name, OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, name, OBJPROP_YDISTANCE, y);
   ObjectSetString(0,  name, OBJPROP_TEXT,       txt);
   ObjectSetString(0,  name, OBJPROP_FONT,       font);
   ObjectSetInteger(0, name, OBJPROP_FONTSIZE,   size);
   ObjectSetInteger(0, name, OBJPROP_COLOR,      clr);
}
//+------------------------------------------------------------------+
//|  ALERT                                                            |
//+------------------------------------------------------------------+
void FireAlert(string msg)
{
   if(gAlertTime == TimeCurrent()) return;
   gAlertTime = TimeCurrent();
   string body = "ST+CCI Sniper: " + msg + " on " + Symbol()
               + " | Entry " + DoubleToString(gEntry, _Digits)
               + " | SL " + DoubleToString(gSL, _Digits)
               + " | TP1 " + DoubleToString(gTP1, _Digits)
               + " | TP2 " + DoubleToString(gTP2, _Digits)
               + " | TP3 " + DoubleToString(gTP3, _Digits);
   if(InpAlertPopup) Alert(body);
   if(InpAlertPush)  SendNotification(body);
   if(InpAlertSound) PlaySound("alert.wav");
}
//+------------------------------------------------------------------+
