//+------------------------------------------------------------------+
//|                                     SuperTrend_Quant_Pro_Elite.mq5 |
//|                                   Copyright 2026, MazenNAfe  |
//|                                            https://www.mql5.com  |
//+------------------------------------------------------------------+
#property copyright   "Copyright 2026, MazenNafe"
#property link        "https://www.mql5.com"
#property version     "5.14"
#property description "Elite Edition: Advanced UI Engine - Robust 4-Corner Positioning"
#property indicator_chart_window
#property indicator_buffers 10
#property indicator_plots   2

//--- Plot Buy Trend
#property indicator_type1   DRAW_LINE
#property indicator_color1  clrMediumSeaGreen
#property indicator_width1  3
#property indicator_label1  "Bullish Quant Trend"

//--- Plot Sell Trend
#property indicator_type2   DRAW_LINE
#property indicator_color2  clrCrimson
#property indicator_width2  3
#property indicator_label2  "Bearish Quant Trend"

//--- Input Parameters: Algorithm Logic
input group "=== QUANT ALGORITHM SETTINGS ==="
input int      InpATRPeriod    = 10;       // ATR Base Period
input double   InpBaseMult      = 3.0;      // Starting Multiplier
input int      InpStatWindow    = 20;       // Statistical Window (Z-Score)
input bool     InpUseAdaptive   = true;     // Adaptive Multiplier

input group "=== ELITE FILTERS (CONFLUENCE) ==="
input bool     InpUseVolFilter  = true;     // Volume Confirmation (Smart Money)
input int      InpVolPeriod     = 20;       // Volume SMA Period
input double   InpVolMult       = 1.5;      // Volume Spike Threshold (e.g. 1.5x)
input bool     InpUseADXRegime  = true;     // ADX Regime Detection
input int      InpADXPeriod     = 14;       // ADX Period
input int      InpADXThreshold  = 25;       // Trend Strength Threshold

input group "=== MTF SCANNER SETTINGS ==="
input string   InpTimeframes    = "M15,M30,H1,H4,D1"; 
input int      InpMTFHistory    = 1000;
input bool     InpShowMTF       = true;
input int      InpUpdateSec     = 2;

input group "=== PERFORMANCE TRACKER ==="
input int      InpEvalBars      = 500;      // Evaluation Lookback
input double   InpTargetRR      = 1.5;      // Reward-to-Risk Ratio

input group "=== SIGNALS & NOTIFICATIONS ==="
input bool     InpShowTags      = true;     // Show Buy/Sell Tags
input bool     InpAlertPopup    = true;     // Popup Alerts
input bool     InpAlertPush     = false;    // Push Notifications
input bool     InpAlertEmail    = false;    // Email Alerts

input group "=== DASHBOARD VISUALS ==="
input bool     InpShowDash      = true;
input color    InpDashColor     = clrGold;
input int      InpCorner        = 0;        // Corner: 0-TL, 1-TR, 2-BL, 3-BR
input int      InpXOffset       = 20;
input int      InpYOffset       = 40;
input color    InpBgColor       = C'15,15,15';
input int      InpDashWidth     = 280;

//--- Buffers
double g_BuffUp[], g_BuffDn[];
double g_BuffTr[], g_BuffUpB[], g_BuffDnB[];
double g_BuffZScore[], g_BuffProb[];
double g_BuffADX[], g_BuffVolRatio[], g_BuffConviction[];

//--- Global State
int    g_hATR = INVALID_HANDLE, g_hADX = INVALID_HANDLE;
string g_UI_PREFIX = "STQ_ELITE_";
datetime g_LastUpdate = 0;
int    g_WinCount = 0, g_TotalSignals = 0;
double g_CurrentProb = 0.0, g_ProfitFactor = 0.0, g_AvgWin = 0.0, g_AvgLoss = 0.0;
int    g_LastTrend = -1;

//--- MTF Globals
ENUM_TIMEFRAMES g_MTF_Periods[];
int             g_MTF_HandlesATR[];
int             g_MTF_HandlesADX[];
string          g_MTF_Names[];
int             g_MTF_Trends[];
string          g_MTF_Statuses[];
int             g_MTF_Count = 0;

//+------------------------------------------------------------------+
//| Initialization                                                   |
//+------------------------------------------------------------------+
int OnInit()
{
   DeleteUIPrefix(g_UI_PREFIX);

   SetIndexBuffer(0, g_BuffUp,      INDICATOR_DATA);
   SetIndexBuffer(1, g_BuffDn,      INDICATOR_DATA);
   SetIndexBuffer(2, g_BuffTr,      INDICATOR_CALCULATIONS);
   SetIndexBuffer(3, g_BuffUpB,     INDICATOR_CALCULATIONS);
   SetIndexBuffer(4, g_BuffDnB,     INDICATOR_CALCULATIONS);
   SetIndexBuffer(5, g_BuffZScore,  INDICATOR_CALCULATIONS);
   SetIndexBuffer(6, g_BuffProb,    INDICATOR_CALCULATIONS);
   SetIndexBuffer(7, g_BuffADX,     INDICATOR_CALCULATIONS);
   SetIndexBuffer(8, g_BuffVolRatio,INDICATOR_CALCULATIONS);
   SetIndexBuffer(9, g_BuffConviction, INDICATOR_CALCULATIONS);

   PlotIndexSetDouble(0, PLOT_EMPTY_VALUE, EMPTY_VALUE);
   PlotIndexSetDouble(1, PLOT_EMPTY_VALUE, EMPTY_VALUE);

   g_hATR = iATR(_Symbol, _Period, InpATRPeriod);
   g_hADX = iADX(_Symbol, _Period, InpADXPeriod);

   if(InpShowMTF || InpShowDash)
   {
      if(InpShowMTF) InitializeMTFScanner();
      EventSetTimer(InpUpdateSec);
   }

   IndicatorSetString(INDICATOR_SHORTNAME, "SuperTrend Quant ELITE");
   return(INIT_SUCCEEDED);
}

void OnDeinit(const int reason) { EventKillTimer(); DeleteUIPrefix(g_UI_PREFIX); }

//+------------------------------------------------------------------+
//| Calculation Loop                                                 |
//+------------------------------------------------------------------+
int OnCalculate(const int rates_total, const int prev_calculated,
                const datetime &time[], const double &open[], const double &high[],
                const double &low[], const double &close[], const long &tick_volume[],
                const long &volume[], const int &spread[])
{
   if(rates_total < MathMax(InpStatWindow, InpVolPeriod) + 20) return(0);

   double atr[], adx_val[];
   if(CopyBuffer(g_hATR, 0, 0, rates_total, atr) < rates_total) return(0);
   if(CopyBuffer(g_hADX, 0, 0, rates_total, adx_val) < rates_total) return(0);

   int start = (prev_calculated > 0) ? prev_calculated - 1 : InpStatWindow;

   for(int i = start; i < rates_total; i++)
   {
      // 1. Z-Score Adaptive Multiplier
      double sum = 0, sum_sq = 0;
      for(int j = 0; j < InpStatWindow; j++) { double v = close[i-j]; sum += v; sum_sq += v*v; }
      double mean = sum/InpStatWindow;
      double std = MathSqrt(MathAbs((sum_sq/InpStatWindow) - mean*mean));
      g_BuffZScore[i] = (std > 0) ? (close[i] - mean)/std : 0;
      
      double mult = InpBaseMult;
      if(InpUseAdaptive) mult *= (1.0 + (MathAbs(g_BuffZScore[i])/10.0));

      // 2. Volume Analysis
      double vol_sum = 0;
      for(int v_j = 1; v_j <= InpVolPeriod; v_j++) vol_sum += (double)tick_volume[i-v_j];
      double vol_avg = vol_sum / InpVolPeriod;
      g_BuffVolRatio[i] = (vol_avg > 0) ? (double)tick_volume[i] / vol_avg : 1.0;

      // 3. SuperTrend Recursive Ratchet
      double mid = (high[i] + low[i]) / 2.0;
      double upB = mid + (mult * atr[i]);
      double dnB = mid - (mult * atr[i]);

      if(i == InpStatWindow) { g_BuffUpB[i] = upB; g_BuffDnB[i] = dnB; g_BuffTr[i] = 1; }
      else 
      {
         g_BuffUpB[i] = (upB < g_BuffUpB[i-1] || close[i-1] > g_BuffUpB[i-1]) ? upB : g_BuffUpB[i-1];
         g_BuffDnB[i] = (dnB > g_BuffDnB[i-1] || close[i-1] < g_BuffDnB[i-1]) ? dnB : g_BuffDnB[i-1];
         
         int trend = (int)g_BuffTr[i-1];
         if(close[i] > g_BuffUpB[i]) trend = 0; 
         else if(close[i] < g_BuffDnB[i]) trend = 1;
         g_BuffTr[i] = (double)trend;
      }

      // 4. Regime Styling
      bool is_trending = (adx_val[i] >= InpADXThreshold);
      bool is_confirmed = (g_BuffVolRatio[i] >= InpVolMult);
      
      g_BuffConviction[i] = (is_trending ? 1.0 : 0.0) + (is_confirmed ? 1.0 : 0.0);

      if(g_BuffTr[i] == 0) { 
         g_BuffUp[i] = g_BuffDnB[i]; g_BuffDn[i] = EMPTY_VALUE;
         PlotIndexSetInteger(0, PLOT_LINE_WIDTH, (g_BuffConviction[i] > 1 ? 4 : 2));
         PlotIndexSetInteger(0, PLOT_LINE_STYLE, (is_trending ? STYLE_SOLID : STYLE_DOT));
      } 
      else { 
         g_BuffDn[i] = g_BuffUpB[i]; g_BuffUp[i] = EMPTY_VALUE;
         PlotIndexSetInteger(1, PLOT_LINE_WIDTH, (g_BuffConviction[i] > 1 ? 4 : 2));
         PlotIndexSetInteger(1, PLOT_LINE_STYLE, (is_trending ? STYLE_SOLID : STYLE_DOT));
      }
      
      // Trigger Alerts on New Bar Signal
      if(i == rates_total - 1 && g_BuffTr[i] != g_LastTrend) {
         TriggerSignalEvent((int)g_BuffTr[i], time[i], close[i], is_confirmed);
         g_LastTrend = (int)g_BuffTr[i];
      }
   }

   if(InpShowDash && (TimeCurrent() - g_LastUpdate > 1)) {
      UpdateEliteMetrics(rates_total, close);
      UpdateQuantDashboard((int)g_BuffTr[rates_total-1]);
      g_LastUpdate = TimeCurrent();
   }

   return(rates_total);
}

//+------------------------------------------------------------------+
//| Performance Tracking                                             |
//+------------------------------------------------------------------+
void UpdateEliteMetrics(int tot, const double &c[])
{
   g_TotalSignals = 0; g_WinCount = 0;
   double total_wins = 0, total_losses = 0;
   int win_n = 0, loss_n = 0;

   int lim = MathMax(tot - InpEvalBars, InpStatWindow + 1);
   for(int i = tot - 2; i > lim; i--) {
      if(g_BuffTr[i] != g_BuffTr[i-1]) {
         g_TotalSignals++;
         int s = (int)g_BuffTr[i]; double ent = c[i];
         for(int k = 1; k <= 30 && (i+k) < tot; k++) {
            double diff = (s == 0 ? c[i+k] - ent : ent - c[i+k]);
            double risk = MathAbs(ent - (s == 0 ? g_BuffDnB[i] : g_BuffUpB[i]));
            if(risk <= 0) break;
            if(diff >= risk * InpTargetRR) { 
               g_WinCount++; total_wins += diff; win_n++; break; 
            }
            if(diff <= -risk) { 
               total_losses += MathAbs(diff); loss_n++; break; 
            }
         }
      }
   }
   g_CurrentProb = (g_TotalSignals > 0) ? ((double)g_WinCount/g_TotalSignals)*100.0 : 0.0;
   g_AvgWin = (win_n > 0) ? total_wins/win_n : 0;
   g_AvgLoss = (loss_n > 0) ? total_losses/loss_n : 0;
   g_ProfitFactor = (total_losses > 0) ? total_wins/total_losses : total_wins;
}

//+------------------------------------------------------------------+
//| Dashboard UI Engine                                              |
//+------------------------------------------------------------------+
void UpdateQuantDashboard(int tr)
{
   int x = InpXOffset, y = InpYOffset, w = InpDashWidth;
   int h = 180 + (InpShowMTF ? (g_MTF_Count * 18) + 25 : 0);

   RenderBG("BG", x-5, y-5, w, h);

   // Determine orientation
   bool isBottom = (InpCorner == 2 || InpCorner == 3);
   int dir = isBottom ? -1 : 1; // -1 means draw "Up" relative to the bottom corner
   
   // startY is the offset from the anchor corner to the first line (Title)
   // Top corners: startY is 10 pixels down from corner.
   // Bottom corners: startY is (h - 20) pixels up from corner.
   int startY = isBottom ? (h - 20) : 10; 

   RenderLbl("T1", "💎 SUPER-TREND QUANT ELITE", x, y + startY, InpDashColor, 10);
   
   string vol_stat = (g_BuffVolRatio[ArraySize(g_BuffTr)-1] > InpVolMult) ? " (High Vol)" : " (Low Vol)";
   RenderLbl("T2", "Trend: " + (tr == 0 ? "🟢 BULLISH" : "🔴 BEARISH") + vol_stat, x, y + startY + (dir * 25), (tr==0?clrLime:clrRed), 9);
   
   RenderLbl("P1", "Edge Probability: " + DoubleToString(g_CurrentProb, 1) + "%", x, y + startY + (dir * 50), clrWhite, 9);
   RenderLbl("P2", "Profit Factor: " + DoubleToString(g_ProfitFactor, 2), x, y + startY + (dir * 68), (g_ProfitFactor > 1.2 ? clrSpringGreen : clrOrange), 9);
   RenderLbl("P3", "Avg Win/Loss: " + DoubleToString(g_AvgWin, _Digits) + " / " + DoubleToString(g_AvgLoss, _Digits), x, y + startY + (dir * 86), clrGray, 8);
   
   bool strong = (g_BuffConviction[ArraySize(g_BuffTr)-1] >= 1);
   RenderLbl("R1", "Market Regime: " + (strong ? "🔥 TRENDING" : "💤 CHOPPY"), x, y + startY + (dir * 110), (strong?clrCyan:clrGold), 9);

   if(InpShowMTF) {
      RenderLbl("MH", "---------------------------------", x, y + startY + (dir * 130), clrDimGray, 8);
      for(int i = 0; i < g_MTF_Count; i++) {
         color c = (g_MTF_Trends[i] == 0) ? clrLime : (g_MTF_Trends[i] == 1 ? clrCrimson : clrGray);
         RenderLbl("M"+(string)i, g_MTF_Names[i] + ": " + g_MTF_Statuses[i], x, y + startY + (dir * 150) + (dir * i * 18), c, 9);
      }
   }
   ChartRedraw(0);
}

//+------------------------------------------------------------------+
//| Helpers & Alerts                                                 |
//+------------------------------------------------------------------+
void TriggerSignalEvent(int trend, datetime t, double p, bool confirmed) {
   string msg = (trend == 0 ? "BULLISH 🟢" : "BEARISH 🔴") + (confirmed ? " [CONFIRMED]" : " [UNCONFIRMED]") + " " + _Symbol + " @ " + DoubleToString(p, _Digits);
   
   if(InpAlertPopup) Alert(msg);
   if(InpAlertPush)  SendNotification(msg);
   if(InpAlertEmail) SendMail("STQ Elite Signal", msg);
   
   if(InpShowTags) {
      string name = g_UI_PREFIX+"TAG_"+TimeToString(t);
      ObjectDelete(0, name);
      if(ObjectCreate(0, name, OBJ_TEXT, 0, t, p)) {
         ObjectSetString(0, name, OBJPROP_TEXT, (trend == 0 ? "▲ BUY" : "▼ SELL") + (confirmed ? " ★" : ""));
         ObjectSetInteger(0, name, OBJPROP_COLOR, (trend == 0 ? clrLime : clrRed));
         ObjectSetInteger(0, name, OBJPROP_ANCHOR, (trend == 0 ? ANCHOR_TOP : ANCHOR_BOTTOM));
         ObjectSetInteger(0, name, OBJPROP_FONTSIZE, 10);
      }
   }
}

void InitializeMTFScanner() {
   string tfs[]; int count = StringSplit(InpTimeframes, ',', tfs);
   g_MTF_Count = MathMin(count, 10);
   ArrayResize(g_MTF_Periods, g_MTF_Count); ArrayResize(g_MTF_HandlesATR, g_MTF_Count);
   ArrayResize(g_MTF_HandlesADX, g_MTF_Count);
   ArrayResize(g_MTF_Names, g_MTF_Count); ArrayResize(g_MTF_Trends, g_MTF_Count); ArrayResize(g_MTF_Statuses, g_MTF_Count);
   for(int i=0; i<g_MTF_Count; i++) {
      string s = tfs[i]; StringTrimLeft(s); StringTrimRight(s);
      g_MTF_Periods[i] = StringToTimeframe(s); g_MTF_Names[i] = s;
      g_MTF_HandlesATR[i] = iATR(_Symbol, g_MTF_Periods[i], InpATRPeriod);
      g_MTF_HandlesADX[i] = iADX(_Symbol, g_MTF_Periods[i], InpADXPeriod);
      g_MTF_Statuses[i] = "...";
   }
}

void OnTimer() {
   if(InpShowMTF) { for(int i=0; i<g_MTF_Count; i++) g_MTF_Trends[i] = SynchronizeAndGetTrend(i); }
   if(InpShowDash) UpdateQuantDashboard((int)g_BuffTr[ArraySize(g_BuffTr)-1]);
}

int SynchronizeAndGetTrend(int idx) {
   ENUM_TIMEFRAMES tf = g_MTF_Periods[idx]; 
   int h_atr = g_MTF_HandlesATR[idx];
   int h_adx = g_MTF_HandlesADX[idx];
   
   if(!SeriesInfoInteger(_Symbol, tf, SERIES_SYNCHRONIZED)) return -1;
   int bars = (int)SeriesInfoInteger(_Symbol, tf, SERIES_BARS_COUNT);
   int depth = MathMin(bars-InpStatWindow-5, InpMTFHistory);
   
   double c[], hg[], lw[], at[], adx_mtf[];
   if(CopyClose(_Symbol, tf, 0, depth+InpStatWindow, c) < depth) return -1;
   if(CopyHigh(_Symbol, tf, 0, depth+InpStatWindow, hg) < depth) return -1;
   if(CopyLow(_Symbol, tf, 0, depth+InpStatWindow, lw) < depth) return -1;
   if(CopyBuffer(h_atr, 0, 0, depth+InpStatWindow, at) < depth) return -1;
   if(CopyBuffer(h_adx, 0, 0, 1, adx_mtf) < 1) return -1;

   ArraySetAsSeries(c,true); ArraySetAsSeries(hg,true); ArraySetAsSeries(lw,true); ArraySetAsSeries(at,true);
   
   // MTF Trend Logic
   double uB=0, dB=0; int tr=(c[depth]> (hg[depth]+lw[depth])/2)?0:1;
   for(int i=depth; i>=0; i--) {
      double mid=(hg[i]+lw[i])/2.0; double up=mid+(InpBaseMult*at[i]), dn=mid-(InpBaseMult*at[i]);
      if(i==depth) { uB=up; dB=dn; } else {
         uB=(up<uB || c[i+1]>uB)?up:uB; dB=(dn>dB || c[i+1]<dB)?dn:dB;
         if(c[i]>uB) tr=0; else if(c[i]<dB) tr=1;
      }
   }
   
   // Market Regime Logic for MTF
   bool trending = (adx_mtf[0] >= InpADXThreshold);
   string regime_icon = trending ? " 🔥" : " 💤";
   string trend_text = (tr==0?"🟢 BULL":"🔴 BEAR");
   
   g_MTF_Statuses[idx] = trend_text + regime_icon; 
   return tr;
}

void DeleteUIPrefix(string p) { for(int i=ObjectsTotal(0)-1; i>=0; i--) { string n=ObjectName(0,i); if(StringFind(n,p)==0) ObjectDelete(0,n); } }

//+------------------------------------------------------------------+
//| UI Rendering Primitive: Background                               |
//+------------------------------------------------------------------+
void RenderBG(string n, int x, int y, int w, int h) {
   string obj = g_UI_PREFIX+n; if(ObjectFind(0,obj)<0) ObjectCreate(0,obj,OBJ_RECTANGLE_LABEL,0,0,0);
   
   // DYNAMIC ANCHOR MAPPING
   ENUM_ANCHOR_POINT anchor = (InpCorner == 1) ? ANCHOR_RIGHT_UPPER : 
                              (InpCorner == 2) ? ANCHOR_LEFT_LOWER : 
                              (InpCorner == 3) ? ANCHOR_RIGHT_LOWER : ANCHOR_LEFT_UPPER;

   ObjectSetInteger(0,obj,OBJPROP_CORNER,InpCorner); 
   ObjectSetInteger(0,obj,OBJPROP_ANCHOR,anchor);
   ObjectSetInteger(0,obj,OBJPROP_XDISTANCE,x);
   ObjectSetInteger(0,obj,OBJPROP_YDISTANCE,y); 
   ObjectSetInteger(0,obj,OBJPROP_XSIZE,w);
   ObjectSetInteger(0,obj,OBJPROP_YSIZE,h); 
   ObjectSetInteger(0,obj,OBJPROP_BGCOLOR,InpBgColor);
   ObjectSetInteger(0,obj,OBJPROP_BORDER_TYPE,BORDER_FLAT); 
   ObjectSetInteger(0,obj,OBJPROP_BACK,true);
}

//+------------------------------------------------------------------+
//| UI Rendering Primitive: Label                                    |
//+------------------------------------------------------------------+
void RenderLbl(string n, string t, int x, int y, color c, int s) {
   string obj = g_UI_PREFIX+n; if(ObjectFind(0,obj)<0) ObjectCreate(0,obj,OBJ_LABEL,0,0,0);
   
   // DYNAMIC ANCHOR MAPPING
   // Labels in Right-corners MUST be right-anchored to keep them inside the box as they grow.
   // Labels in Bottom-corners MUST be top-anchored so they draw downward from their Y-coordinate.
   ENUM_ANCHOR_POINT anchor = ANCHOR_LEFT_UPPER;
   if(InpCorner == 1) anchor = ANCHOR_RIGHT_UPPER;
   if(InpCorner == 2) anchor = ANCHOR_LEFT_UPPER; // Keep Left, but coordinate will be higher
   if(InpCorner == 3) anchor = ANCHOR_RIGHT_UPPER;

   ObjectSetInteger(0,obj,OBJPROP_CORNER,InpCorner); 
   ObjectSetInteger(0,obj,OBJPROP_ANCHOR,anchor);
   ObjectSetInteger(0,obj,OBJPROP_XDISTANCE,x);
   ObjectSetInteger(0,obj,OBJPROP_YDISTANCE,y); 
   ObjectSetString(0,obj,OBJPROP_TEXT,t);
   ObjectSetInteger(0,obj,OBJPROP_COLOR,c); 
   ObjectSetInteger(0,obj,OBJPROP_FONTSIZE,s);
   ObjectSetInteger(0,obj,OBJPROP_ZORDER,1); // Force text on top layer
}

ENUM_TIMEFRAMES StringToTimeframe(string s) {
   if(s=="M1") return PERIOD_M1; if(s=="M5") return PERIOD_M5; if(s=="M15") return PERIOD_M15;
   if(s=="M30") return PERIOD_M30; if(s=="H1") return PERIOD_H1; if(s=="H4") return PERIOD_H4;
   if(s=="D1") return PERIOD_D1; return _Period;
}