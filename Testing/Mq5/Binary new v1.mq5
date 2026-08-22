//+------------------------------------------------------------------+
//|                                         BollingerReversal.mq5   |
//|         Bollinger Band Reversal — PUT & CALL with Win Rate        |
//|         Non-Repainting | 5-Min Time Expiry | Tick/Cross Results  |
//+------------------------------------------------------------------+
#property copyright "Copyright 2024"
#property version   "7.00"
#property description "Bollinger Band Reversal for Binary Options"
#property description "PUT: Red arrow above candle | CALL: Blue arrow below"
#property description "Green tick = WIN | Red cross = LOSS (5-min time expiry)"
#property description "Countdown timer persists even if indicator is removed"
#property indicator_chart_window
#property indicator_buffers 4
#property indicator_plots   4

//--- Plot 0: PUT signals (red down arrows)
#property indicator_type1   DRAW_ARROW
#property indicator_color1  clrRed
#property indicator_width1  2
#property indicator_label1  "PUT Signal"

//--- Plot 1: CALL signals (blue up arrows)
#property indicator_type2   DRAW_ARROW
#property indicator_color2  clrDodgerBlue
#property indicator_width2  2
#property indicator_label2  "CALL Signal"

//--- Plot 2: WIN tick markers (green checkmark)
#property indicator_type3   DRAW_ARROW
#property indicator_color3  clrLimeGreen
#property indicator_width3  2
#property indicator_label3  "WIN"

//--- Plot 3: LOSS cross markers (orange-red)
#property indicator_type4   DRAW_ARROW
#property indicator_color4  clrOrangeRed
#property indicator_width4  2
#property indicator_label4  "LOSS"

//=== INPUTS ===
input group "=== Bollinger Bands ==="
input int              BB_Period    = 20;
input double           BB_Deviation = 2.0;
input int              BB_Shift     = 0;
input ENUM_APPLIED_PRICE BB_Price   = PRICE_CLOSE;

input group "=== Signal Filter ==="
input int    MinCandleBodyPips  = 2;
input bool   RequireBearishBody = true;  // PUT: require bearish candle
input bool   RequireBullishBody = true;  // CALL: require bullish candle

input group "=== Stochastic Slope Filter ==="
input bool   UseStochSlope  = true; // PUT: Stoch sloping down | CALL: Stoch sloping up
input int    Stoch_K        = 5;    // %K period
input int    Stoch_D        = 3;    // %D smoothing
input int    Stoch_Slowing  = 3;    // Slowing
input int    Stoch_SlopeBars = 2;   // Bars back to compare Stoch against

input group "=== Expiry (Fixed 5 Minutes) ==="
input int    ExpirySeconds   = 300;      // Expiry in seconds (300 = 5 min)
input bool   ShowWinRate     = true;
input bool   ShowExpiry      = true;

input group "=== Result Markers ==="
input bool   ShowResultMarkers = true;   // Show tick/cross on signal candle

input group "=== Alerts ==="
input bool   EnableAlerts    = true;
input bool   EnablePush      = false;

//=== BUFFERS ===
double PutBuffer[];
double CallBuffer[];
double WinBuffer[];
double LossBuffer[];

//=== GLOBALS ===
int      g_bb_handle    = INVALID_HANDLE;
int      g_stoch_handle = INVALID_HANDLE;
datetime g_last_alert   = 0;
int      g_total_put    = 0;
int      g_win_put      = 0;
int      g_total_call   = 0;
int      g_win_call     = 0;

//--- Persistent timer
string   TIMER_DATA_OBJ = "BB_PersistData";
datetime g_timer_expiry = 0;
string   g_timer_dir    = "";

//+------------------------------------------------------------------+
//| Find the bar index whose candle CLOSES at or after (signal_time  |
//| + ExpirySeconds). Returns -1 if not enough history yet.          |
//| Arrays are AS-SERIES (index 0 = newest).                         |
//+------------------------------------------------------------------+
int FindExpiryBar(const datetime &t[], int rates_total, int sig_bar)
  {
   datetime expTime = t[sig_bar] + (datetime)ExpirySeconds;

   // Walk forward in time from sig_bar (= lower index numbers)
   // until we find the first bar whose open-time >= expTime
   for(int j = sig_bar - 1; j >= 0; j--)
     {
      if(t[j] >= expTime)
         return(j);          // this bar opened at or after expiry → it's the result bar
     }
   return(-1);               // expiry bar not yet formed
  }

//+------------------------------------------------------------------+
int OnInit()
  {
   g_bb_handle = iBands(_Symbol, _Period, BB_Period, BB_Shift, BB_Deviation, BB_Price);
   if(g_bb_handle == INVALID_HANDLE)
     {
      Alert("BB Reversal: Cannot create Bollinger Bands handle!");
      return(INIT_FAILED);
     }

   g_stoch_handle = iStochastic(_Symbol, _Period, Stoch_K, Stoch_D, Stoch_Slowing,
                                   MODE_SMA, STO_LOWHIGH);
   if(g_stoch_handle == INVALID_HANDLE)
     {
      Alert("BB Reversal: Cannot create Stochastic handle!");
      return(INIT_FAILED);
     }

   SetIndexBuffer(0, PutBuffer,  INDICATOR_DATA);
   SetIndexBuffer(1, CallBuffer, INDICATOR_DATA);
   SetIndexBuffer(2, WinBuffer,  INDICATOR_DATA);
   SetIndexBuffer(3, LossBuffer, INDICATOR_DATA);

   //--- PUT: down arrow (Wingdings 234)
   PlotIndexSetInteger(0, PLOT_ARROW,       234);
   PlotIndexSetInteger(0, PLOT_ARROW_SHIFT, 10);
   PlotIndexSetInteger(0, PLOT_DRAW_BEGIN,  BB_Period);
   PlotIndexSetDouble (0, PLOT_EMPTY_VALUE, EMPTY_VALUE);

   //--- CALL: up arrow (Wingdings 233)
   PlotIndexSetInteger(1, PLOT_ARROW,       233);
   PlotIndexSetInteger(1, PLOT_ARROW_SHIFT,-10);
   PlotIndexSetInteger(1, PLOT_DRAW_BEGIN,  BB_Period);
   PlotIndexSetDouble (1, PLOT_EMPTY_VALUE, EMPTY_VALUE);

   //--- WIN: checkmark (Wingdings 252) — shifted 18px beyond signal arrow
   PlotIndexSetInteger(2, PLOT_ARROW,       252);
   PlotIndexSetInteger(2, PLOT_ARROW_SHIFT, 18);
   PlotIndexSetInteger(2, PLOT_DRAW_BEGIN,  BB_Period);
   PlotIndexSetDouble (2, PLOT_EMPTY_VALUE, EMPTY_VALUE);

   //--- LOSS: cross (Wingdings 251) — same offset
   PlotIndexSetInteger(3, PLOT_ARROW,       251);
   PlotIndexSetInteger(3, PLOT_ARROW_SHIFT, 18);
   PlotIndexSetInteger(3, PLOT_DRAW_BEGIN,  BB_Period);
   PlotIndexSetDouble (3, PLOT_EMPTY_VALUE, EMPTY_VALUE);

   IndicatorSetString(INDICATOR_SHORTNAME,
      "BB Reversal(" + IntegerToString(BB_Period) + "," +
      DoubleToString(BB_Deviation,1) + ")" + (UseStochSlope ? " 5min+Stoch" : " 5min"));

   ArraySetAsSeries(PutBuffer,  true);
   ArraySetAsSeries(CallBuffer, true);
   ArraySetAsSeries(WinBuffer,  true);
   ArraySetAsSeries(LossBuffer, true);

   RestorePersistedTimer();

   if(ShowWinRate) CreateWinRatePanel();

   EventSetMillisecondTimer(500);
   return(INIT_SUCCEEDED);
  }

//+------------------------------------------------------------------+
void OnDeinit(const int reason)
  {
   EventKillTimer();

   if(g_bb_handle != INVALID_HANDLE)
     {
      IndicatorRelease(g_bb_handle);
      g_bb_handle = INVALID_HANDLE;
     }
   if(g_stoch_handle != INVALID_HANDLE)
     {
      IndicatorRelease(g_stoch_handle);
      g_stoch_handle = INVALID_HANDLE;
     }

   // Remove panel objects — timer objects intentionally left on chart
   // so countdown continues after indicator is detached
   ObjectDelete(0, "BB_BG");
   ObjectDelete(0, "BB_Title");
   ObjectDelete(0, "BB_PUT");
   ObjectDelete(0, "BB_CALL");
   ObjectDelete(0, "BB_TOT");
   ObjectsDeleteAll(0, "BB_Exp_");

   ChartRedraw(0);
  }

//+------------------------------------------------------------------+
int OnCalculate(const int rates_total,
                const int prev_calculated,
                const datetime &time[],
                const double &open[],
                const double &high[],
                const double &low[],
                const double &close[],
                const long   &tick_volume[],
                const long   &real_volume[],
                const int    &spread[])
  {
   if(rates_total < BB_Period + 3) return(0);
   if(g_bb_handle == INVALID_HANDLE) return(0);

   //--- Local series arrays (avoid modifying const OnCalculate params)
   double upper[], lower[], middle[];
   double o[], h[], l[], c[];
   datetime t[];

   ArraySetAsSeries(upper,  true);
   ArraySetAsSeries(lower,  true);
   ArraySetAsSeries(middle, true);
   ArraySetAsSeries(o, true);
   ArraySetAsSeries(h, true);
   ArraySetAsSeries(l, true);
   ArraySetAsSeries(c, true);
   ArraySetAsSeries(t, true);

   if(CopyBuffer(g_bb_handle, 1, 0, rates_total, upper)  <= 0) return(0);
   if(CopyBuffer(g_bb_handle, 2, 0, rates_total, lower)  <= 0) return(0);
   if(CopyBuffer(g_bb_handle, 0, 0, rates_total, middle) <= 0) return(0);
   if(CopyOpen  (_Symbol, _Period, 0, rates_total, o) <= 0) return(0);
   if(CopyHigh  (_Symbol, _Period, 0, rates_total, h) <= 0) return(0);
   if(CopyLow   (_Symbol, _Period, 0, rates_total, l) <= 0) return(0);
   if(CopyClose (_Symbol, _Period, 0, rates_total, c) <= 0) return(0);
   if(CopyTime  (_Symbol, _Period, 0, rates_total, t) <= 0) return(0);

   //--- Stochastic %K buffer (buffer 0) — used for slope direction filter
   double stoch[];
   ArraySetAsSeries(stoch, true);
   if(UseStochSlope && CopyBuffer(g_stoch_handle, 0, 0, rates_total, stoch) <= 0) return(0);

   if(prev_calculated == 0)
     {
      g_total_put  = 0; g_win_put  = 0;
      g_total_call = 0; g_win_call = 0;
      ArrayInitialize(PutBuffer,  EMPTY_VALUE);
      ArrayInitialize(CallBuffer, EMPTY_VALUE);
      ArrayInitialize(WinBuffer,  EMPTY_VALUE);
      ArrayInitialize(LossBuffer, EMPTY_VALUE);
     }

   double pip         = (_Digits == 3 || _Digits == 2) ? _Point : _Point * 10.0;
   double minBodySize = MinCandleBodyPips * pip;

   // Start from the oldest unprocessed bar down to bar 1
   // (bar 0 is the live forming candle — never evaluate it)
   int start = (prev_calculated == 0) ? rates_total - 2
                                       : rates_total - prev_calculated + 1;
   if(start > rates_total - 2) start = rates_total - 2;

   for(int i = start; i >= 1; i--)
     {
      if(i >= ArraySize(upper) || i >= ArraySize(lower)) continue;

      // Always reset all four buffers at this bar first
      PutBuffer[i]  = EMPTY_VALUE;
      CallBuffer[i] = EMPTY_VALUE;
      WinBuffer[i]  = EMPTY_VALUE;
      LossBuffer[i] = EMPTY_VALUE;

      double body   = MathAbs(c[i] - o[i]);
      bool   isBear = (c[i] < o[i]);
      bool   isBull = (c[i] > o[i]);

      //--- Stochastic slope: compare %K[i] vs %K[i + Stoch_SlopeBars]
      //    slopeDown = %K now is LOWER than N bars ago → momentum falling → favour PUT
      //    slopeUp   = %K now is HIGHER than N bars ago → momentum rising  → favour CALL
      bool stochSlopeDown = true;
      bool stochSlopeUp   = true;
      if(UseStochSlope && (i + Stoch_SlopeBars) < ArraySize(stoch))
        {
         stochSlopeDown = (stoch[i] < stoch[i + Stoch_SlopeBars]);
         stochSlopeUp   = (stoch[i] > stoch[i + Stoch_SlopeBars]);
        }

      //=== PUT: high wicks into upper BB, closes below it, bearish body, Stoch sloping down ===
      bool putCond = (h[i]  >= upper[i])
                  && (c[i]  <  upper[i])
                  && (body  >= minBodySize)
                  && (!RequireBearishBody || isBear)
                  && (!UseStochSlope     || stochSlopeDown);

      //=== CALL: low wicks into lower BB, closes above it, bullish body, Stoch sloping up ===
      bool callCond = (l[i]  <= lower[i])
                   && (c[i]  >  lower[i])
                   && (body  >= minBodySize)
                   && (!RequireBullishBody || isBull)
                   && (!UseStochSlope      || stochSlopeUp);

      if(putCond)
        {
         PutBuffer[i] = h[i] + 15 * _Point;

         // Find the result bar using TIME, not candle count
         // expBar is the first bar that opens at or after (signal_time + 5 min)
         int expBar = FindExpiryBar(t, rates_total, i);

         // Only draw tick/cross if expiry bar has already CLOSED
         // (expBar > 0 means there is at least one more bar after it = it is closed)
         if(ShowResultMarkers && expBar > 0)
           {
            // WIN for PUT: price at expiry is BELOW signal close
            bool putWin = (c[expBar] < c[i]);
            double mrkPrice = h[i] + 15 * _Point; // same anchor as arrow
            if(putWin)
              { WinBuffer[i]  = mrkPrice; LossBuffer[i] = EMPTY_VALUE; }
            else
              { LossBuffer[i] = mrkPrice; WinBuffer[i]  = EMPTY_VALUE; }
           }

         // Stats: count only on full recalc and only when result bar is closed
         if(expBar > 0 && prev_calculated == 0)
           {
            g_total_put++;
            if(c[expBar] < c[i]) g_win_put++;
           }

         // Expiry label on bar 1 (last completed signal)
         if(ShowExpiry && i == 1)
            DrawExpiryLabel(t[i], h[i], "PUT", ExpirySeconds / 60);

         // Alert + start persistent countdown on brand-new signal (bar 1)
         if(i == 1 && EnableAlerts && t[1] != g_last_alert)
           {
            g_last_alert = t[1];
            datetime expTime = t[1] + (datetime)ExpirySeconds;
            StartPersistentTimer(expTime, "PUT");
            Alert("PUT SIGNAL | " + _Symbol + " | Expiry: " +
                  IntegerToString(ExpirySeconds / 60) + " min");
            if(EnablePush)
               SendNotification("PUT SIGNAL | " + _Symbol + " | Expiry: " +
                  IntegerToString(ExpirySeconds / 60) + " min");
           }
        }
      else if(callCond)
        {
         CallBuffer[i] = l[i] - 15 * _Point;

         int expBar = FindExpiryBar(t, rates_total, i);

         if(ShowResultMarkers && expBar > 0)
           {
            bool callWin = (c[expBar] > c[i]);
            double mrkPrice = l[i] - 15 * _Point;
            if(callWin)
              { WinBuffer[i]  = mrkPrice; LossBuffer[i] = EMPTY_VALUE; }
            else
              { LossBuffer[i] = mrkPrice; WinBuffer[i]  = EMPTY_VALUE; }
           }

         if(expBar > 0 && prev_calculated == 0)
           {
            g_total_call++;
            if(c[expBar] > c[i]) g_win_call++;
           }

         if(ShowExpiry && i == 1)
            DrawExpiryLabel(t[i], l[i], "CALL", ExpirySeconds / 60);

         if(i == 1 && EnableAlerts && t[1] != g_last_alert)
           {
            g_last_alert = t[1];
            datetime expTime = t[1] + (datetime)ExpirySeconds;
            StartPersistentTimer(expTime, "CALL");
            Alert("CALL SIGNAL | " + _Symbol + " | Expiry: " +
                  IntegerToString(ExpirySeconds / 60) + " min");
            if(EnablePush)
               SendNotification("CALL SIGNAL | " + _Symbol + " | Expiry: " +
                  IntegerToString(ExpirySeconds / 60) + " min");
           }
        }
     }

   if(ShowWinRate) UpdateWinRatePanel();
   return(rates_total);
  }

//+------------------------------------------------------------------+
//| Timer fires every 500ms — updates visible countdown display      |
//+------------------------------------------------------------------+
void OnTimer()
  {
   if(g_timer_expiry == 0)
      RestorePersistedTimer();

   if(g_timer_expiry == 0) return;

   datetime now      = TimeCurrent();
   long     secsLeft = (long)g_timer_expiry - (long)now;

   if(secsLeft <= 0)
     {
      DrawTimerBox("00:00", g_timer_dir, 0, true);
      ClearTimerObjects();
      g_timer_expiry = 0;
      g_timer_dir    = "";
      return;
     }

   DrawTimerBox(FormatCountdown(secsLeft), g_timer_dir, secsLeft, false);
  }

//+------------------------------------------------------------------+
//| Encode expiry into a hidden chart object so it survives reload   |
//+------------------------------------------------------------------+
void StartPersistentTimer(datetime expiry_time, string direction)
  {
   g_timer_expiry = expiry_time;
   g_timer_dir    = direction;

   string data = IntegerToString((long)expiry_time) + "|" + direction;

   ObjectDelete(0, TIMER_DATA_OBJ);
   ObjectCreate(0, TIMER_DATA_OBJ, OBJ_TEXT, 0,
                iTime(_Symbol, _Period, 0), iClose(_Symbol, _Period, 0) * 0.0);
   ObjectSetString (0, TIMER_DATA_OBJ, OBJPROP_TEXT,       data);
   ObjectSetInteger(0, TIMER_DATA_OBJ, OBJPROP_COLOR,      clrNONE);
   ObjectSetInteger(0, TIMER_DATA_OBJ, OBJPROP_FONTSIZE,   1);
   ObjectSetInteger(0, TIMER_DATA_OBJ, OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, TIMER_DATA_OBJ, OBJPROP_HIDDEN,     true);

   CreateTimerBox();
   ChartRedraw(0);
  }

//+------------------------------------------------------------------+
//| Re-read persisted expiry data when indicator is reloaded         |
//+------------------------------------------------------------------+
void RestorePersistedTimer()
  {
   if(ObjectFind(0, TIMER_DATA_OBJ) < 0) return;

   string data = ObjectGetString(0, TIMER_DATA_OBJ, OBJPROP_TEXT);
   if(StringLen(data) == 0) return;

   string parts[];
   if(StringSplit(data, '|', parts) != 2) return;

   g_timer_expiry = (datetime)StringToInteger(parts[0]);
   g_timer_dir    = parts[1];

   if(g_timer_expiry <= TimeCurrent())
     {
      ClearTimerObjects();
      g_timer_expiry = 0;
      g_timer_dir    = "";
      return;
     }

   CreateTimerBox();
  }

//+------------------------------------------------------------------+
string FormatCountdown(long secs)
  {
   long m = secs / 60;
   long s = secs % 60;
   return((m < 10 ? "0" : "") + IntegerToString((int)m) + ":" +
          (s < 10 ? "0" : "") + IntegerToString((int)s));
  }

//+------------------------------------------------------------------+
void CreateTimerBox()
  {
   if(ObjectFind(0, "BB_TmrBG") < 0)
     {
      ObjectCreate(0, "BB_TmrBG", OBJ_RECTANGLE_LABEL, 0, 0, 0);
      ObjectSetInteger(0, "BB_TmrBG", OBJPROP_CORNER,      CORNER_RIGHT_UPPER);
      ObjectSetInteger(0, "BB_TmrBG", OBJPROP_XDISTANCE,   8);
      ObjectSetInteger(0, "BB_TmrBG", OBJPROP_YDISTANCE,   28);
      ObjectSetInteger(0, "BB_TmrBG", OBJPROP_XSIZE,       160);
      ObjectSetInteger(0, "BB_TmrBG", OBJPROP_YSIZE,       72);
      ObjectSetInteger(0, "BB_TmrBG", OBJPROP_BGCOLOR,     (color)C'10,10,20');
      ObjectSetInteger(0, "BB_TmrBG", OBJPROP_BORDER_TYPE, BORDER_FLAT);
      ObjectSetInteger(0, "BB_TmrBG", OBJPROP_COLOR,       clrDimGray);
      ObjectSetInteger(0, "BB_TmrBG", OBJPROP_SELECTABLE,  false);
      ObjectSetInteger(0, "BB_TmrBG", OBJPROP_ZORDER,      10);
     }

   if(ObjectFind(0, "BB_TmrDir") < 0)
     {
      ObjectCreate(0, "BB_TmrDir", OBJ_LABEL, 0, 0, 0);
      ObjectSetInteger(0, "BB_TmrDir", OBJPROP_CORNER,     CORNER_RIGHT_UPPER);
      ObjectSetInteger(0, "BB_TmrDir", OBJPROP_XDISTANCE,  155);
      ObjectSetInteger(0, "BB_TmrDir", OBJPROP_YDISTANCE,  38);
      ObjectSetString (0, "BB_TmrDir", OBJPROP_FONT,       "Consolas");
      ObjectSetInteger(0, "BB_TmrDir", OBJPROP_FONTSIZE,   10);
      ObjectSetInteger(0, "BB_TmrDir", OBJPROP_SELECTABLE, false);
      ObjectSetInteger(0, "BB_TmrDir", OBJPROP_ANCHOR,     ANCHOR_RIGHT_UPPER);
      ObjectSetInteger(0, "BB_TmrDir", OBJPROP_ZORDER,     11);
     }

   if(ObjectFind(0, "BB_TmrCnt") < 0)
     {
      ObjectCreate(0, "BB_TmrCnt", OBJ_LABEL, 0, 0, 0);
      ObjectSetInteger(0, "BB_TmrCnt", OBJPROP_CORNER,     CORNER_RIGHT_UPPER);
      ObjectSetInteger(0, "BB_TmrCnt", OBJPROP_XDISTANCE,  155);
      ObjectSetInteger(0, "BB_TmrCnt", OBJPROP_YDISTANCE,  58);
      ObjectSetString (0, "BB_TmrCnt", OBJPROP_FONT,       "Consolas");
      ObjectSetInteger(0, "BB_TmrCnt", OBJPROP_FONTSIZE,   16);
      ObjectSetInteger(0, "BB_TmrCnt", OBJPROP_SELECTABLE, false);
      ObjectSetInteger(0, "BB_TmrCnt", OBJPROP_ANCHOR,     ANCHOR_RIGHT_UPPER);
      ObjectSetInteger(0, "BB_TmrCnt", OBJPROP_ZORDER,     11);
     }
  }

//+------------------------------------------------------------------+
void DrawTimerBox(string countdown, string dir, long secsLeft, bool expired)
  {
   color dirColor = (dir == "PUT") ? clrRed : clrDodgerBlue;
   color cntColor = expired         ? clrOrangeRed :
                   (secsLeft <= 10) ? clrOrangeRed :
                   (secsLeft <= 30) ? clrYellow    : clrWhite;

   string dirText = expired ? "-- EXPIRED --"
                            : (dir == "PUT" ? "v  PUT  SIGNAL" : "^  CALL SIGNAL");

   ObjectSetString (0, "BB_TmrDir", OBJPROP_TEXT,  dirText);
   ObjectSetInteger(0, "BB_TmrDir", OBJPROP_COLOR, expired ? clrOrangeRed : dirColor);
   ObjectSetString (0, "BB_TmrCnt", OBJPROP_TEXT,  countdown);
   ObjectSetInteger(0, "BB_TmrCnt", OBJPROP_COLOR, cntColor);

   // Flash background red in the last 10 seconds
   if(!expired && secsLeft <= 10)
     {
      static bool flash = false;
      flash = !flash;
      ObjectSetInteger(0, "BB_TmrBG", OBJPROP_BGCOLOR,
         flash ? (color)C'45,5,5' : (color)C'10,10,20');
     }
   else
      ObjectSetInteger(0, "BB_TmrBG", OBJPROP_BGCOLOR, (color)C'10,10,20');

   ChartRedraw(0);
  }

//+------------------------------------------------------------------+
void ClearTimerObjects()
  {
   Sleep(1500); // Brief pause so user reads EXPIRED
   ObjectDelete(0, "BB_TmrBG");
   ObjectDelete(0, "BB_TmrDir");
   ObjectDelete(0, "BB_TmrCnt");
   ObjectDelete(0, TIMER_DATA_OBJ);
   ChartRedraw(0);
  }

//+------------------------------------------------------------------+
void DrawExpiryLabel(datetime bar_time, double price, string dir, int exp_min)
  {
   string name   = "BB_Exp_" + TimeToString(bar_time, TIME_MINUTES);
   color  clr    = (dir == "PUT") ? clrRed : clrDodgerBlue;
   double offset = (dir == "PUT") ? price + 35*_Point : price - 35*_Point;

   ObjectDelete(0, name);
   ObjectCreate(0, name, OBJ_TEXT, 0, bar_time, offset);
   ObjectSetString (0, name, OBJPROP_TEXT,
      dir + "  " + IntegerToString(exp_min) + " min");
   ObjectSetInteger(0, name, OBJPROP_COLOR,    clr);
   ObjectSetInteger(0, name, OBJPROP_FONTSIZE, 8);
   ObjectSetString (0, name, OBJPROP_FONT,     "Arial Bold");
   ObjectSetInteger(0, name, OBJPROP_ANCHOR,
      (ENUM_ANCHOR_POINT)((dir == "PUT") ? ANCHOR_LOWER : ANCHOR_UPPER));
   ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, name, OBJPROP_HIDDEN,     true);
  }

//+------------------------------------------------------------------+
void CreateWinRatePanel()
  {
   ObjectCreate(0, "BB_BG", OBJ_RECTANGLE_LABEL, 0, 0, 0);
   ObjectSetInteger(0, "BB_BG", OBJPROP_CORNER,      CORNER_LEFT_UPPER);
   ObjectSetInteger(0, "BB_BG", OBJPROP_XDISTANCE,   8);
   ObjectSetInteger(0, "BB_BG", OBJPROP_YDISTANCE,   28);
   ObjectSetInteger(0, "BB_BG", OBJPROP_XSIZE,       215);
   ObjectSetInteger(0, "BB_BG", OBJPROP_YSIZE,       100);
   ObjectSetInteger(0, "BB_BG", OBJPROP_BGCOLOR,     (color)C'15,15,25');
   ObjectSetInteger(0, "BB_BG", OBJPROP_BORDER_TYPE, BORDER_FLAT);
   ObjectSetInteger(0, "BB_BG", OBJPROP_COLOR,       clrDimGray);
   ObjectSetInteger(0, "BB_BG", OBJPROP_SELECTABLE,  false);
   ObjectSetInteger(0, "BB_BG", OBJPROP_ZORDER,      0);

   MakeLabel("BB_Title", "  BB REVERSAL v7 | STOCH",  14, 34, clrSilver,    8);
   MakeLabel("BB_PUT",   "  PUT  : --/-- (---%)",    14, 52, clrRed,       9);
   MakeLabel("BB_CALL",  "  CALL : --/-- (---%)",    14, 70, clrDodgerBlue,9);
   MakeLabel("BB_TOT",   "  TOTAL: --/-- (---%)",    14, 90, clrYellow,    9);

   ChartRedraw(0);
  }

void MakeLabel(string name, string txt, int x, int y, color clr, int sz)
  {
   ObjectCreate(0, name, OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, name, OBJPROP_CORNER,     CORNER_LEFT_UPPER);
   ObjectSetInteger(0, name, OBJPROP_XDISTANCE,  x);
   ObjectSetInteger(0, name, OBJPROP_YDISTANCE,  y);
   ObjectSetString (0, name, OBJPROP_TEXT,       txt);
   ObjectSetInteger(0, name, OBJPROP_COLOR,      clr);
   ObjectSetInteger(0, name, OBJPROP_FONTSIZE,   sz);
   ObjectSetString (0, name, OBJPROP_FONT,       "Consolas");
   ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, name, OBJPROP_ZORDER,     1);
  }

//+------------------------------------------------------------------+
void UpdateWinRatePanel()
  {
   int    total = g_total_put + g_total_call;
   int    wins  = g_win_put   + g_win_call;

   double wr_p = (g_total_put  > 0) ? (double)g_win_put  / g_total_put  * 100.0 : 0.0;
   double wr_c = (g_total_call > 0) ? (double)g_win_call / g_total_call * 100.0 : 0.0;
   double wr_t = (total        > 0) ? (double)wins        / total        * 100.0 : 0.0;

   ObjectSetString(0, "BB_PUT",  OBJPROP_TEXT,
      "  PUT  : " + IntegerToString(g_win_put)  + "/" +
      IntegerToString(g_total_put)  + "  (" + DoubleToString(wr_p,1) + "%)");

   ObjectSetString(0, "BB_CALL", OBJPROP_TEXT,
      "  CALL : " + IntegerToString(g_win_call) + "/" +
      IntegerToString(g_total_call) + "  (" + DoubleToString(wr_c,1) + "%)");

   ObjectSetString(0, "BB_TOT",  OBJPROP_TEXT,
      "  TOTAL: " + IntegerToString(wins) + "/" +
      IntegerToString(total) + "  (" + DoubleToString(wr_t,1) + "%)");

   color tc = (wr_t >= 60.0) ? clrLimeGreen :
              (wr_t >= 50.0) ? clrYellow    : clrOrangeRed;
   ObjectSetInteger(0, "BB_TOT", OBJPROP_COLOR, tc);

   ChartRedraw(0);
  }
//+------------------------------------------------------------------+