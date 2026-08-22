//+------------------------------------------------------------------+
//|                    Breakout Retest Pro EA - v2 Visual            |
//|                    BRT Pro v2.1 - MT5 (Bugfix Release)           |
//|                    Based on BRT Pro Pine Script                  |
//|                                                                  |
//|    v2 adds: Navy dark theme + DodgerBlue/White candles           |
//|             Professional dashboard panel                         |
//|             Neckline boxes + retest zones + signal arrows        |
//|    v2.1 fixes:                                                   |
//|      - Trial expiry removed                                      |
//|      - Neckline no longer reset when breakout already active     |
//|      - TP multiplier silent x2 removed (uses input directly)     |
//|      - Bar index counter replaced (was SERIES_BARS_COUNT bug)    |
//|      - Reversal now checks confirmed bar[1] not live bar[0]      |
//|      - Dashboard Daily P/L relabelled to Floating P/L            |
//+------------------------------------------------------------------+
#property copyright "BRT Pro v2 Visual"
#property version   "2.10"
#property strict

// ── Trial expiry removed in v2.1 ──────────────────────────────────

#include <Trade\Trade.mqh>
#include <Trade\PositionInfo.mqh>

CTrade        trade;
CPositionInfo posInfo;

//+------------------------------------------------------------------+
// INPUT PARAMETERS - ASYMMETRIC
//+------------------------------------------------------------------+

// ══════════ BUY SETTINGS ══════════
input group "═══ BUY SETTINGS ═══"
input int    Buy_SwingLength    = 10;    // Buy Swing Length
input double Buy_ImpulseMult    = 1.5;   // Buy Impulse ATR Mult
input int    Buy_BreakConfirm   = 2;     // Buy Break Confirm Bars
input double Buy_RetestATR      = 0.5;   // Buy Retest Zone (ATR)
input int    Buy_RetestBars     = 40;    // Buy Max Retest Bars
input double Buy_SL_Mult        = 1.5;   // Buy SL ATR Mult
input double Buy_TP_Mult        = 4.0;   // Buy TP ATR Mult

// ══════════ SELL SETTINGS ══════════
input group "═══ SELL SETTINGS ═══"
input int    Sell_SwingLength   = 10;    // Sell Swing Length
input double Sell_ImpulseMult   = 1.5;   // Sell Impulse ATR Mult
input int    Sell_BreakConfirm  = 2;     // Sell Break Confirm Bars
input double Sell_RetestATR     = 0.5;   // Sell Retest Zone (ATR)
input int    Sell_RetestBars    = 40;    // Sell Max Retest Bars
input double Sell_SL_Mult       = 1.5;   // Sell SL ATR Mult
input double Sell_TP_Mult       = 4.0;   // Sell TP ATR Mult

// ══════════ REVERSAL DETECTION ══════════
input group "═══ REVERSAL DETECTION ═══"
input bool   Use_Reversal       = true;  // Enable Reversal Exit
input int    Rev_ConsecBars     = 4;     // Consecutive Reversal Bars
input double Rev_ATR_Move       = 1.0;   // Min ATR Move Against Trade
input bool   Use_Engulf         = true;  // Detect Engulfing Candle
input bool   Use_ThreeBar       = true;  // Detect Three Bar Reversal
input bool   Use_MomentumFlip   = true;  // Detect EMA Momentum Flip
input int    EMA_Fast           = 8;     // Fast EMA Period
input int    EMA_Slow           = 21;    // Slow EMA Period

// ══════════ RISK MANAGEMENT ══════════
input group "═══ RISK MANAGEMENT ═══"
input double LotSize            = 0.5;   // Lot Size
input bool   UseAutoLot         = true; // Use Auto Lot (% risk)
input double RiskPercent        = 1.0;   // Risk % per trade
input int    MaxPositions       = 1;     // Max open positions
input int    Slippage           = 2;     // Slippage (points)
input int    MagicNumber        = 2026; // Magic Number

// ══════════ TRADE FILTER ══════════
input group "═══ TRADE FILTERS ═══"
input bool   UseSessionFilter   = false; // Use Session Filter
input int    SessionStartHour   = 8;     // Session Start Hour (Server)
input int    SessionEndHour     = 22;    // Session End Hour (Server)
input bool   UseTrendFilter     = true;  // Use SMA50 Trend Filter
input int    TrendSMA           = 50;    // Trend SMA Period

// ══════════ VISUAL SETTINGS ══════════
input group "═══ VISUAL SETTINGS ═══"
input bool   ApplyTheme         = true;  // Apply Navy Dark Theme
input bool   ShowDashboard      = true;  // Show Dashboard Panel
input bool   ShowBoxes          = true;  // Show Neckline / Retest Boxes
input bool   ShowSignals        = true;  // Show BUY/SELL Labels

//=============================================================================
// VISUAL CONSTANTS (mirrored from SMC Master V5 style)
//=============================================================================

#define BRT            "BRT_"           // Object name prefix
#define PANEL_X        10
#define PANEL_Y        25
#define PANEL_W        290
#define PAD            10
#define ROW_H          18
#define HDR_H          30
#define SEC_H          16
#define LABEL_X        (PANEL_X+PAD)
#define VALUE_X        (PANEL_X+145)

// Panel colors
#define CLR_PANEL_BG    C'25,28,38'
#define CLR_HEADER_BG   C'20,55,85'
#define CLR_SECTION_BG  C'30,35,48'
#define CLR_BORDER      C'55,65,85'
#define CLR_DIVIDER     C'45,50,65'
#define CLR_SECTION_TXT C'100,160,220'
#define CLR_LABEL       C'160,160,175'
#define CLR_VALUE       C'230,230,240'
#define CLR_PROFIT      clrSpringGreen
#define CLR_LOSS        clrTomato
#define CLR_WARN        clrOrange
#define CLR_INFO        clrDeepSkyBlue
#define CLR_DIMMED      C'100,100,115'

// Fonts
#define FONT_TITLE      "Segoe UI"
#define FONT_SECTION    "Segoe UI"
#define FONT_BODY       "Consolas"
#define FSIZE_TITLE     11
#define FSIZE_SECTION   8
#define FSIZE_BODY      9

// Symbols
#define SYM_DIAMOND     "\x25C6"    // ◆
#define SYM_BULLET      "\x25CF"    // ●
#define SYM_UP          "\x25B2"    // ▲
#define SYM_DOWN        "\x25BC"    // ▼
#define SYM_CHECK       "\x2713"    // ✓
#define SYM_CROSS       "\x2717"    // ✗

// Drawing colors
#define CLR_BULL_BOX    C'0,191,255'   // DodgerBlue tint for bull neckline
#define CLR_BEAR_BOX    C'255,68,68'   // Red tint for bear neckline
#define CLR_BULL_SIG    C'0,255,136'
#define CLR_BEAR_SIG    C'255,68,68'

//+------------------------------------------------------------------+
// GLOBAL STATE VARIABLES
//+------------------------------------------------------------------+

// Bull state
double bull_neck      = 0;
int    bull_neck_bar  = -1;
bool   bull_broke     = false;
int    bull_broke_bar = -1;
bool   bull_waiting   = false;

// Bear state
double bear_neck      = 0;
int    bear_neck_bar  = -1;
bool   bear_broke     = false;
int    bear_broke_bar = -1;
bool   bear_waiting   = false;

// Reversal tracking
int    consec_down    = 0;
int    consec_up      = 0;

// ATR / MA handles
int    atr_handle;
int    ema_fast_handle;
int    ema_slow_handle;
int    sma50_handle;

// Dashboard / theme state
color  g_origBG, g_origFG, g_origGrid, g_origVol, g_origUp, g_origDown;
color  g_origLine, g_origBull, g_origBear, g_origBid, g_origAsk, g_origStop;

// Last signal (for dashboard display)
string lastSignal = "None";

// Bar-open counter — incremented on each new bar, used for retest expiry
// (replaces the broken SERIES_BARS_COUNT approach)
int    g_bar_count    = 0;

// Counter for unique drawing object names
int    obj_counter = 0;

//+------------------------------------------------------------------+
// THEME HELPERS
//+------------------------------------------------------------------+
void SaveOriginalChartColors()
{
   g_origBG   = (color)ChartGetInteger(0, CHART_COLOR_BACKGROUND);
   g_origFG   = (color)ChartGetInteger(0, CHART_COLOR_FOREGROUND);
   g_origGrid = (color)ChartGetInteger(0, CHART_COLOR_GRID);
   g_origVol  = (color)ChartGetInteger(0, CHART_COLOR_VOLUME);
   g_origUp   = (color)ChartGetInteger(0, CHART_COLOR_CHART_UP);
   g_origDown = (color)ChartGetInteger(0, CHART_COLOR_CHART_DOWN);
   g_origLine = (color)ChartGetInteger(0, CHART_COLOR_CHART_LINE);
   g_origBull = (color)ChartGetInteger(0, CHART_COLOR_CANDLE_BULL);
   g_origBear = (color)ChartGetInteger(0, CHART_COLOR_CANDLE_BEAR);
   g_origBid  = (color)ChartGetInteger(0, CHART_COLOR_BID);
   g_origAsk  = (color)ChartGetInteger(0, CHART_COLOR_ASK);
   g_origStop = (color)ChartGetInteger(0, CHART_COLOR_STOP_LEVEL);
}

void RestoreOriginalChartColors()
{
   ChartSetInteger(0, CHART_COLOR_BACKGROUND,  g_origBG);
   ChartSetInteger(0, CHART_COLOR_FOREGROUND,  g_origFG);
   ChartSetInteger(0, CHART_COLOR_GRID,        g_origGrid);
   ChartSetInteger(0, CHART_COLOR_VOLUME,      g_origVol);
   ChartSetInteger(0, CHART_COLOR_CHART_UP,    g_origUp);
   ChartSetInteger(0, CHART_COLOR_CHART_DOWN,  g_origDown);
   ChartSetInteger(0, CHART_COLOR_CHART_LINE,  g_origLine);
   ChartSetInteger(0, CHART_COLOR_CANDLE_BULL, g_origBull);
   ChartSetInteger(0, CHART_COLOR_CANDLE_BEAR, g_origBear);
   ChartSetInteger(0, CHART_COLOR_BID,         g_origBid);
   ChartSetInteger(0, CHART_COLOR_ASK,         g_origAsk);
   ChartSetInteger(0, CHART_COLOR_STOP_LEVEL,  g_origStop);
   ChartRedraw(0);
}

void ApplyDarkNavyTheme()
{
   ChartSetInteger(0, CHART_COLOR_BACKGROUND,  C'15,15,40');
   ChartSetInteger(0, CHART_COLOR_FOREGROUND,  C'180,180,195');
   ChartSetInteger(0, CHART_COLOR_GRID,        C'30,30,60');
   ChartSetInteger(0, CHART_COLOR_VOLUME,      C'70,130,180');
   ChartSetInteger(0, CHART_COLOR_CANDLE_BULL, clrDodgerBlue);
   ChartSetInteger(0, CHART_COLOR_CHART_UP,    clrDodgerBlue);
   ChartSetInteger(0, CHART_COLOR_CANDLE_BEAR, clrWhite);
   ChartSetInteger(0, CHART_COLOR_CHART_DOWN,  clrWhite);
   ChartSetInteger(0, CHART_COLOR_CHART_LINE,  clrDodgerBlue);
   ChartSetInteger(0, CHART_COLOR_BID,         C'0,200,200');
   ChartSetInteger(0, CHART_COLOR_ASK,         C'255,80,80');
   ChartSetInteger(0, CHART_COLOR_STOP_LEVEL,  clrOrangeRed);
   ChartRedraw(0);
}

//+------------------------------------------------------------------+
// DASHBOARD OBJECT HELPERS
//+------------------------------------------------------------------+
void MakeRect(string name, int x, int y, int w, int h, color bg, color border=clrNONE, int bw=0)
{
   if(ObjectFind(0,name)<0) ObjectCreate(0, name, OBJ_RECTANGLE_LABEL, 0, 0, 0);
   ObjectSetInteger(0, name, OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, name, OBJPROP_YDISTANCE, y);
   ObjectSetInteger(0, name, OBJPROP_XSIZE, w);
   ObjectSetInteger(0, name, OBJPROP_YSIZE, h);
   ObjectSetInteger(0, name, OBJPROP_BGCOLOR, bg);
   ObjectSetInteger(0, name, OBJPROP_BORDER_TYPE, BORDER_FLAT);
   ObjectSetInteger(0, name, OBJPROP_COLOR, border);
   ObjectSetInteger(0, name, OBJPROP_WIDTH, bw);
   ObjectSetInteger(0, name, OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetInteger(0, name, OBJPROP_BACK, false);
   ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, name, OBJPROP_HIDDEN, true);
   ObjectSetInteger(0, name, OBJPROP_ZORDER, 0);
}

void MakeLabel(string name, int x, int y, string text, string font, int size, color clr)
{
   if(ObjectFind(0,name)<0) ObjectCreate(0, name, OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, name, OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, name, OBJPROP_YDISTANCE, y);
   ObjectSetInteger(0, name, OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetString(0, name, OBJPROP_TEXT, text);
   ObjectSetString(0, name, OBJPROP_FONT, font);
   ObjectSetInteger(0, name, OBJPROP_FONTSIZE, size);
   ObjectSetInteger(0, name, OBJPROP_COLOR, clr);
   ObjectSetInteger(0, name, OBJPROP_ANCHOR, ANCHOR_LEFT_UPPER);
   ObjectSetInteger(0, name, OBJPROP_BACK, false);
   ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, name, OBJPROP_HIDDEN, true);
   ObjectSetInteger(0, name, OBJPROP_ZORDER, 100);
}

bool SetText(string name, string txt, color clr=CLR_VALUE)
{
   if(ObjectFind(0,name)<0) return false;
   bool changed = false;
   if(ObjectGetString(0,name,OBJPROP_TEXT) != txt)
   {
      ObjectSetString(0, name, OBJPROP_TEXT, txt);
      changed = true;
   }
   if((color)ObjectGetInteger(0,name,OBJPROP_COLOR) != clr)
   {
      ObjectSetInteger(0, name, OBJPROP_COLOR, clr);
      changed = true;
   }
   return changed;
}

//+------------------------------------------------------------------+
// BUILD DASHBOARD
//+------------------------------------------------------------------+
void BuildDashboard()
{
   int y = PANEL_Y;
   int totalH = 400;

   MakeRect(BRT+"BG",     PANEL_X, PANEL_Y, PANEL_W, totalH, CLR_PANEL_BG, CLR_BORDER, 1);
   MakeRect(BRT+"HDR_BG", PANEL_X+1, PANEL_Y+1, PANEL_W-2, HDR_H-1, CLR_HEADER_BG, clrNONE, 0);

   y = PANEL_Y;
   MakeLabel(BRT+"LOGO", LABEL_X, y+7,  SYM_DIAMOND+" Breakout Retest Pro", FONT_TITLE, FSIZE_TITLE, clrWhite);
   MakeLabel(BRT+"VER",  PANEL_X+PANEL_W-55, y+10, "v2.1", FONT_BODY, 7, CLR_DIMMED);
   y += HDR_H + 2;

   // ── ACCOUNT ──
   MakeRect(BRT+"DIV1", PANEL_X+1, y, PANEL_W-2, 1, CLR_DIVIDER);
   y += 3;
   MakeLabel(BRT+"SEC1", LABEL_X, y, "ACCOUNT", FONT_SECTION, FSIZE_SECTION, CLR_SECTION_TXT);
   y += SEC_H;
   MakeLabel(BRT+"L_BAL", LABEL_X, y, "Balance:", FONT_BODY, FSIZE_BODY, CLR_LABEL);
   MakeLabel(BRT+"V_BAL", VALUE_X, y, "---", FONT_BODY, FSIZE_BODY, CLR_VALUE);
   y += ROW_H;
   MakeLabel(BRT+"L_EQ",  LABEL_X, y, "Equity:",  FONT_BODY, FSIZE_BODY, CLR_LABEL);
   MakeLabel(BRT+"V_EQ",  VALUE_X, y, "---", FONT_BODY, FSIZE_BODY, CLR_VALUE);
   y += ROW_H;
   MakeLabel(BRT+"L_DPL", LABEL_X, y, "Floating P/L:", FONT_BODY, FSIZE_BODY, CLR_LABEL);
   MakeLabel(BRT+"V_DPL", VALUE_X, y, "---", FONT_BODY, FSIZE_BODY, CLR_VALUE);
   y += ROW_H + 2;

   // ── MARKET ──
   MakeRect(BRT+"DIV2", PANEL_X+1, y, PANEL_W-2, 1, CLR_DIVIDER);
   y += 3;
   MakeLabel(BRT+"SEC2", LABEL_X, y, "MARKET", FONT_SECTION, FSIZE_SECTION, CLR_SECTION_TXT);
   y += SEC_H;
   MakeLabel(BRT+"L_BIAS", LABEL_X, y, "Bias:",   FONT_BODY, FSIZE_BODY, CLR_LABEL);
   MakeLabel(BRT+"V_BIAS", VALUE_X, y, "---",     FONT_BODY, FSIZE_BODY, CLR_VALUE);
   y += ROW_H;
   MakeLabel(BRT+"L_ATR",  LABEL_X, y, "ATR:",    FONT_BODY, FSIZE_BODY, CLR_LABEL);
   MakeLabel(BRT+"V_ATR",  VALUE_X, y, "---",     FONT_BODY, FSIZE_BODY, CLR_VALUE);
   y += ROW_H;
   MakeLabel(BRT+"L_SPR",  LABEL_X, y, "Spread:", FONT_BODY, FSIZE_BODY, CLR_LABEL);
   MakeLabel(BRT+"V_SPR",  VALUE_X, y, "---",     FONT_BODY, FSIZE_BODY, CLR_VALUE);
   y += ROW_H + 2;

   // ── BULL SETUP ──
   MakeRect(BRT+"DIV3", PANEL_X+1, y, PANEL_W-2, 1, CLR_DIVIDER);
   y += 3;
   MakeLabel(BRT+"SEC3", LABEL_X, y, "BULL SETUP", FONT_SECTION, FSIZE_SECTION, CLR_SECTION_TXT);
   y += SEC_H;
   MakeLabel(BRT+"L_BPHASE", LABEL_X, y, "Phase:",    FONT_BODY, FSIZE_BODY, CLR_LABEL);
   MakeLabel(BRT+"V_BPHASE", VALUE_X, y, "--",        FONT_BODY, FSIZE_BODY, CLR_VALUE);
   y += ROW_H;
   MakeLabel(BRT+"L_BNECK",  LABEL_X, y, "Neckline:", FONT_BODY, FSIZE_BODY, CLR_LABEL);
   MakeLabel(BRT+"V_BNECK",  VALUE_X, y, "--",        FONT_BODY, FSIZE_BODY, CLR_VALUE);
   y += ROW_H + 2;

   // ── BEAR SETUP ──
   MakeRect(BRT+"DIV4", PANEL_X+1, y, PANEL_W-2, 1, CLR_DIVIDER);
   y += 3;
   MakeLabel(BRT+"SEC4", LABEL_X, y, "BEAR SETUP", FONT_SECTION, FSIZE_SECTION, CLR_SECTION_TXT);
   y += SEC_H;
   MakeLabel(BRT+"L_SPHASE", LABEL_X, y, "Phase:",    FONT_BODY, FSIZE_BODY, CLR_LABEL);
   MakeLabel(BRT+"V_SPHASE", VALUE_X, y, "--",        FONT_BODY, FSIZE_BODY, CLR_VALUE);
   y += ROW_H;
   MakeLabel(BRT+"L_SNECK",  LABEL_X, y, "Neckline:", FONT_BODY, FSIZE_BODY, CLR_LABEL);
   MakeLabel(BRT+"V_SNECK",  VALUE_X, y, "--",        FONT_BODY, FSIZE_BODY, CLR_VALUE);
   y += ROW_H + 2;

   // ── POSITIONS ──
   MakeRect(BRT+"DIV5", PANEL_X+1, y, PANEL_W-2, 1, CLR_DIVIDER);
   y += 3;
   MakeLabel(BRT+"SEC5", LABEL_X, y, "POSITIONS", FONT_SECTION, FSIZE_SECTION, CLR_SECTION_TXT);
   y += SEC_H;
   MakeLabel(BRT+"L_POS", LABEL_X, y, "Open:", FONT_BODY, FSIZE_BODY, CLR_LABEL);
   MakeLabel(BRT+"V_POS", VALUE_X, y, "0",      FONT_BODY, FSIZE_BODY, CLR_VALUE);
   y += ROW_H;
   MakeLabel(BRT+"L_PNL", LABEL_X, y, "P/L:",  FONT_BODY, FSIZE_BODY, CLR_LABEL);
   MakeLabel(BRT+"V_PNL", VALUE_X, y, "$0.00", FONT_BODY, FSIZE_BODY, CLR_VALUE);
   y += ROW_H;
   MakeLabel(BRT+"L_SIG", LABEL_X, y, "Last:", FONT_BODY, FSIZE_BODY, CLR_LABEL);
   MakeLabel(BRT+"V_SIG", VALUE_X, y, SYM_BULLET+" Waiting", FONT_BODY, FSIZE_BODY, CLR_WARN);
   y += ROW_H + 2;

   // Resize background to actual content height
   ObjectSetInteger(0, BRT+"BG", OBJPROP_YSIZE, y - PANEL_Y + PAD);
   ChartRedraw(0);
}

//+------------------------------------------------------------------+
// UPDATE DASHBOARD
//+------------------------------------------------------------------+
void UpdateDashboard(double atr_val, double sma_val, double c1_val)
{
   bool redraw = false;
   string cur = AccountInfoString(ACCOUNT_CURRENCY);
   double bal = AccountInfoDouble(ACCOUNT_BALANCE);
   double eq  = AccountInfoDouble(ACCOUNT_EQUITY);
   double prof= AccountInfoDouble(ACCOUNT_PROFIT);
   int    spread = (int)SymbolInfoInteger(_Symbol, SYMBOL_SPREAD);

   redraw |= SetText(BRT+"V_BAL", DoubleToString(bal,2)+" "+cur, CLR_VALUE);
   redraw |= SetText(BRT+"V_EQ",  DoubleToString(eq,2)+" "+cur, (eq>=bal) ? CLR_PROFIT : CLR_LOSS);
   color  plClr  = (prof>=0) ? CLR_PROFIT : CLR_LOSS;
   string plSign = (prof>=0) ? "+" : "";
   redraw |= SetText(BRT+"V_DPL", plSign+DoubleToString(prof,2)+" "+cur, plClr);

   // Bias
   bool trend_bull = (c1_val > sma_val);
   if(trend_bull)
      redraw |= SetText(BRT+"V_BIAS", SYM_UP+" BULLISH",  CLR_PROFIT);
   else
      redraw |= SetText(BRT+"V_BIAS", SYM_DOWN+" BEARISH", CLR_LOSS);

   redraw |= SetText(BRT+"V_ATR", DoubleToString(atr_val, _Digits), CLR_VALUE);
   redraw |= SetText(BRT+"V_SPR", IntegerToString(spread)+" pts", (spread>30) ? CLR_LOSS : CLR_VALUE);

   // Bull phase
   string bphase = "--";
   color  bphClr = CLR_DIMMED;
   if(bull_neck > 0 && !bull_broke)       { bphase = "Waiting Break";    bphClr = CLR_WARN;   }
   else if(bull_neck > 0 && bull_broke)   { bphase = "⚡ Waiting Retest"; bphClr = CLR_INFO;   }
   redraw |= SetText(BRT+"V_BPHASE", bphase, bphClr);
   redraw |= SetText(BRT+"V_BNECK", bull_neck > 0 ? DoubleToString(bull_neck, _Digits) : "--", CLR_VALUE);

   // Bear phase
   string sphase = "--";
   color  sphClr = CLR_DIMMED;
   if(bear_neck > 0 && !bear_broke)       { sphase = "Waiting Break";    sphClr = CLR_WARN;   }
   else if(bear_neck > 0 && bear_broke)   { sphase = "⚡ Waiting Retest"; sphClr = CLR_INFO;   }
   redraw |= SetText(BRT+"V_SPHASE", sphase, sphClr);
   redraw |= SetText(BRT+"V_SNECK", bear_neck > 0 ? DoubleToString(bear_neck, _Digits) : "--", CLR_VALUE);

   // Positions
   int    posCnt = 0;
   double totalPnL = 0;
   for(int i = PositionsTotal()-1; i >= 0; i--)
   {
      if(posInfo.SelectByIndex(i) &&
         posInfo.Symbol() == _Symbol &&
         posInfo.Magic()  == MagicNumber)
      {
         posCnt++;
         totalPnL += posInfo.Profit() + posInfo.Swap();
      }
   }
   redraw |= SetText(BRT+"V_POS", IntegerToString(posCnt), CLR_VALUE);
   color  pnlClr  = (totalPnL >= 0) ? CLR_PROFIT : CLR_LOSS;
   string pnlSign = (totalPnL >= 0) ? "+$" : "-$";
   redraw |= SetText(BRT+"V_PNL", pnlSign+DoubleToString(MathAbs(totalPnL),2), pnlClr);

   // Last signal
   if(lastSignal != "None" && lastSignal != "")
   {
      color sigClr = (StringFind(lastSignal, "BUY") >= 0) ? CLR_PROFIT : CLR_LOSS;
      redraw |= SetText(BRT+"V_SIG", SYM_BULLET+" "+lastSignal, sigClr);
   }

   if(redraw) ChartRedraw(0);
}

//+------------------------------------------------------------------+
// CHART DRAWINGS (neckline box, retest zone, signal labels)
//+------------------------------------------------------------------+
void DrawNecklineBox(bool isBull, datetime t_start, datetime t_end, double neck, double atr_val)
{
   string name = BRT + "BOX_" + (isBull ? "BULL_" : "BEAR_") + IntegerToString(obj_counter++);
   double top    = neck + atr_val * 0.1;
   double bottom = neck - atr_val * 0.1;
   color  clr    = isBull ? CLR_BULL_BOX : CLR_BEAR_BOX;

   if(ObjectFind(0, name) < 0) ObjectCreate(0, name, OBJ_RECTANGLE, 0, t_start, top, t_end, bottom);
   ObjectSetInteger(0, name, OBJPROP_COLOR, clr);
   ObjectSetInteger(0, name, OBJPROP_FILL,  true);
   ObjectSetInteger(0, name, OBJPROP_BACK,  true);
   ObjectSetInteger(0, name, OBJPROP_WIDTH, 1);
   ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, name, OBJPROP_HIDDEN, true);
}

void DrawSignalArrow(bool isBull, datetime t, double price, double sl, double tp, double atr_val)
{
   string name = BRT + "SIG_" + (isBull ? "BUY_" : "SELL_") + IntegerToString(obj_counter++);

   if(ObjectFind(0, name) < 0)
      ObjectCreate(0, name, OBJ_ARROW, 0, t, isBull ? (price - atr_val*0.3) : (price + atr_val*0.3));

   ObjectSetInteger(0, name, OBJPROP_ARROWCODE, isBull ? 233 : 234); // up/down arrow
   ObjectSetInteger(0, name, OBJPROP_COLOR,     isBull ? CLR_BULL_SIG : CLR_BEAR_SIG);
   ObjectSetInteger(0, name, OBJPROP_WIDTH,     3);
   ObjectSetInteger(0, name, OBJPROP_ANCHOR,    isBull ? ANCHOR_TOP : ANCHOR_BOTTOM);
   ObjectSetInteger(0, name, OBJPROP_HIDDEN,    true);

   // Text label with SL/TP info
   string txt_name = BRT + "SIGTXT_" + (isBull ? "BUY_" : "SELL_") + IntegerToString(obj_counter++);
   string txt = (isBull ? SYM_UP+" BUY" : SYM_DOWN+" SELL") +
                "  SL:" + DoubleToString(sl, _Digits) +
                "  TP:" + DoubleToString(tp, _Digits);
   double txt_price = isBull ? (price - atr_val*0.6) : (price + atr_val*0.6);

   if(ObjectFind(0, txt_name) < 0)
      ObjectCreate(0, txt_name, OBJ_TEXT, 0, t, txt_price);
   ObjectSetString (0, txt_name, OBJPROP_TEXT,   txt);
   ObjectSetString (0, txt_name, OBJPROP_FONT,   FONT_BODY);
   ObjectSetInteger(0, txt_name, OBJPROP_FONTSIZE, 8);
   ObjectSetInteger(0, txt_name, OBJPROP_COLOR,  isBull ? CLR_BULL_SIG : CLR_BEAR_SIG);
   ObjectSetInteger(0, txt_name, OBJPROP_ANCHOR, isBull ? ANCHOR_UPPER : ANCHOR_LOWER);
   ObjectSetInteger(0, txt_name, OBJPROP_HIDDEN, true);
}

void CleanupObjects()
{
   int total = ObjectsTotal(0, -1, -1);
   for(int i = total - 1; i >= 0; i--)
   {
      string name = ObjectName(0, i, -1, -1);
      if(StringFind(name, BRT) == 0) ObjectDelete(0, name);
   }
}

//+------------------------------------------------------------------+
// INIT
//+------------------------------------------------------------------+
int OnInit()
{
   trade.SetExpertMagicNumber(MagicNumber);
   trade.SetDeviationInPoints(Slippage);
   trade.SetTypeFilling(ORDER_FILLING_FOK);

   atr_handle      = iATR(_Symbol, PERIOD_CURRENT, 14);
   ema_fast_handle = iMA(_Symbol, PERIOD_CURRENT, EMA_Fast, 0, MODE_EMA, PRICE_CLOSE);
   ema_slow_handle = iMA(_Symbol, PERIOD_CURRENT, EMA_Slow, 0, MODE_EMA, PRICE_CLOSE);
   sma50_handle    = iMA(_Symbol, PERIOD_CURRENT, TrendSMA, 0, MODE_SMA, PRICE_CLOSE);

   if(atr_handle == INVALID_HANDLE || ema_fast_handle == INVALID_HANDLE ||
      ema_slow_handle == INVALID_HANDLE || sma50_handle == INVALID_HANDLE)
   {
       Print("ERROR: Failed to create indicator handles");
       return INIT_FAILED;
   }

   // Visuals
   if(ApplyTheme)
   {
      SaveOriginalChartColors();
      ApplyDarkNavyTheme();
   }
   if(ShowDashboard) BuildDashboard();

   Print("BRT Pro v2 Visual initialized on ", _Symbol);
   return INIT_SUCCEEDED;
}

//+------------------------------------------------------------------+
// DEINIT
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   IndicatorRelease(atr_handle);
   IndicatorRelease(ema_fast_handle);
   IndicatorRelease(ema_slow_handle);
   IndicatorRelease(sma50_handle);

   CleanupObjects();
   if(ApplyTheme) RestoreOriginalChartColors();
}

//+------------------------------------------------------------------+
// HELPER: Get indicator values
//+------------------------------------------------------------------+
double GetATR(int shift = 1)
{
   double buf[];
   ArraySetAsSeries(buf, true);
   if(CopyBuffer(atr_handle, 0, shift, 1, buf) <= 0) return 0;
   return buf[0];
}
double GetEMAFast(int shift = 0)
{
   double buf[];
   ArraySetAsSeries(buf, true);
   if(CopyBuffer(ema_fast_handle, 0, shift, 1, buf) <= 0) return 0;
   return buf[0];
}
double GetEMASlow(int shift = 0)
{
   double buf[];
   ArraySetAsSeries(buf, true);
   if(CopyBuffer(ema_slow_handle, 0, shift, 1, buf) <= 0) return 0;
   return buf[0];
}
double GetSMA50(int shift = 0)
{
   double buf[];
   ArraySetAsSeries(buf, true);
   if(CopyBuffer(sma50_handle, 0, shift, 1, buf) <= 0) return 0;
   return buf[0];
}

//+------------------------------------------------------------------+
// HELPER: Get OHLC
//+------------------------------------------------------------------+
double GetHigh(int shift)  { return iHigh(_Symbol,  PERIOD_CURRENT, shift); }
double GetLow(int shift)   { return iLow(_Symbol,   PERIOD_CURRENT, shift); }
double GetOpen(int shift)  { return iOpen(_Symbol,  PERIOD_CURRENT, shift); }
double GetClose(int shift) { return iClose(_Symbol, PERIOD_CURRENT, shift); }
// NOTE: GetBarIndex() removed — it used SERIES_BARS_COUNT which returns total
// history depth, not the current bar index. Bar counting is now handled
// by g_bar_count incremented on each new bar open in OnTick().

//+------------------------------------------------------------------+
// HELPER: Swing High / Low detection
//+------------------------------------------------------------------+
double GetSwingHigh(int length, int shift)
{
   double pivot = GetHigh(shift + length);
   for(int i = 0; i < length; i++)
   {
       if(GetHigh(shift + i) > pivot) return 0;
       if(GetHigh(shift + length * 2 - i) > pivot) return 0;
   }
   return pivot;
}

double GetSwingLow(int length, int shift)
{
   double pivot = GetLow(shift + length);
   for(int i = 0; i < length; i++)
   {
       if(GetLow(shift + i) < pivot) return 0;
       if(GetLow(shift + length * 2 - i) < pivot) return 0;
   }
   return pivot;
}

//+------------------------------------------------------------------+
// HELPER: Count positions by magic
//+------------------------------------------------------------------+
int CountPositions(int type = -1)
{
   int count = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
       if(posInfo.SelectByIndex(i))
       {
           if(posInfo.Symbol() == _Symbol && posInfo.Magic() == MagicNumber)
           {
               if(type == -1 || posInfo.PositionType() == type)
                   count++;
           }
       }
   }
   return count;
}

//+------------------------------------------------------------------+
// HELPER: Calculate lot size
//+------------------------------------------------------------------+
double CalcLotSize(double sl_distance)
{
   if(!UseAutoLot) return NormalizeDouble(LotSize, 2);

   double account_balance = AccountInfoDouble(ACCOUNT_BALANCE);
   double risk_amount     = account_balance * RiskPercent / 100.0;
   double tick_value      = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE);
   double tick_size       = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);

   if(sl_distance <= 0 || tick_value <= 0) return NormalizeDouble(LotSize, 2);

   double sl_ticks = sl_distance / tick_size;
   double lot      = risk_amount / (sl_ticks * tick_value);

   double min_lot  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double max_lot  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
   double lot_step = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);

   lot = MathFloor(lot / lot_step) * lot_step;
   lot = MathMax(min_lot, MathMin(max_lot, lot));

   return NormalizeDouble(lot, 2);
}

//+------------------------------------------------------------------+
// HELPER: Session filter
//+------------------------------------------------------------------+
bool IsSessionActive()
{
   if(!UseSessionFilter) return true;
   MqlDateTime dt;
   TimeToStruct(TimeCurrent(), dt);
   return (dt.hour >= SessionStartHour && dt.hour < SessionEndHour);
}

//+------------------------------------------------------------------+
// HELPER: Close positions
//+------------------------------------------------------------------+
void ClosePositionsByType(ENUM_POSITION_TYPE ptype, string reason)
{
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
       if(posInfo.SelectByIndex(i))
       {
           if(posInfo.Symbol() == _Symbol &&
              posInfo.Magic()  == MagicNumber &&
              posInfo.PositionType() == ptype)
           {
               trade.PositionClose(posInfo.Ticket());
               Print("BRT Pro | REVERSAL EXIT #", posInfo.Ticket(),
                     " | Reason: ", reason,
                     " @ ", DoubleToString(SymbolInfoDouble(_Symbol, SYMBOL_BID), _Digits));
           }
       }
   }
}

//+------------------------------------------------------------------+
// REVERSAL DETECTION
//+------------------------------------------------------------------+
bool DetectReversalAgainstBull(double atr_val)
{
   if(!Use_Reversal) return false;

   // FIX: Use bar[1], bar[2], bar[3] — all fully closed candles.
   // Original code used bar[0] (live/forming candle) which made pattern
   // detection unreliable at bar open.
   double c1 = GetClose(1), o1 = GetOpen(1);
   double c2 = GetClose(2), o2 = GetOpen(2);
   double c3 = GetClose(3), o3 = GetOpen(3);
   double h2 = GetHigh(2);
   double h1 = GetHigh(1);
   double l2 = GetLow(2);

   bool engulf    = Use_Engulf   && (c1 < o1) && (o1 >= h2) && (c1 <= l2);
   bool three_bar = Use_ThreeBar && (c1 < o1) && (c2 < o2) && (c3 < o3);

   bool ema_flip = false;
   if(Use_MomentumFlip)
   {
       double ef1 = GetEMAFast(1), es1 = GetEMASlow(1);
       double ef2 = GetEMAFast(2), es2 = GetEMASlow(2);
       ema_flip = (ef2 >= es2) && (ef1 < es1);
   }

   bool consec_atr = (consec_down >= Rev_ConsecBars) && ((h1 - c1) >= atr_val * Rev_ATR_Move);
   return engulf || three_bar || ema_flip || consec_atr;
}

bool DetectReversalAgainstBear(double atr_val)
{
   if(!Use_Reversal) return false;

   // FIX: Use bar[1], bar[2], bar[3] — all fully closed candles.
   double c1 = GetClose(1), o1 = GetOpen(1);
   double c2 = GetClose(2), o2 = GetOpen(2);
   double c3 = GetClose(3), o3 = GetOpen(3);
   double h2 = GetHigh(2);
   double l2 = GetLow(2),  l1 = GetLow(1);

   bool engulf    = Use_Engulf   && (c1 > o1) && (o1 <= l2) && (c1 >= h2);
   bool three_bar = Use_ThreeBar && (c1 > o1) && (c2 > o2) && (c3 > o3);

   bool ema_flip = false;
   if(Use_MomentumFlip)
   {
       double ef1 = GetEMAFast(1), es1 = GetEMASlow(1);
       double ef2 = GetEMAFast(2), es2 = GetEMASlow(2);
       ema_flip = (ef2 <= es2) && (ef1 > es1);
   }

   bool consec_atr = (consec_up >= Rev_ConsecBars) && ((c1 - l1) >= atr_val * Rev_ATR_Move);
   return engulf || three_bar || ema_flip || consec_atr;
}

//+------------------------------------------------------------------+
// MAIN TICK
//+------------------------------------------------------------------+
void OnTick()
{
   // Refresh dashboard on every tick (cheap — SetText only redraws on change)
   if(ShowDashboard)
   {
      double atr_dash = GetATR(1);
      double sma_dash = GetSMA50(1);
      double c1_dash  = GetClose(1);
      UpdateDashboard(atr_dash, sma_dash, c1_dash);
   }

   // Rest of logic runs on new bar only
   static datetime last_bar_time = 0;
   datetime current_bar = iTime(_Symbol, PERIOD_CURRENT, 0);
   if(current_bar == last_bar_time) return;
   last_bar_time = current_bar;

   // Increment our own bar counter (used for retest expiry instead of
   // the broken SERIES_BARS_COUNT approach)
   g_bar_count++;

   if(!IsSessionActive()) return;

   double atr = GetATR(1);
   if(atr <= 0) return;

   double c1 = GetClose(1), o1 = GetOpen(1);
   double h1 = GetHigh(1),  l1 = GetLow(1);

   // Update consecutive bar counters
   if(c1 < o1)      { consec_down++; consec_up = 0; }
   else if(c1 > o1) { consec_up++;   consec_down = 0; }
   else             { consec_down = 0; consec_up = 0; }

   // Use our own g_bar_count as current bar index
   int current_bar_idx = g_bar_count;

   // Trend filter
   double sma50        = GetSMA50(1);
   bool   trend_ok_buy  = !UseTrendFilter || (c1 > sma50);
   bool   trend_ok_sell = !UseTrendFilter || (c1 < sma50);

   //══════════════════════════════════════════════════
   // REVERSAL CHECK — close existing positions
   //══════════════════════════════════════════════════
   if(CountPositions(POSITION_TYPE_BUY) > 0)
   {
       if(DetectReversalAgainstBull(atr))
       {
           ClosePositionsByType(POSITION_TYPE_BUY, "Reversal Exit - Secure Profit");
           bull_broke   = false;
           bull_waiting = false;
           bull_neck    = 0;
       }
   }

   if(CountPositions(POSITION_TYPE_SELL) > 0)
   {
       if(DetectReversalAgainstBear(atr))
       {
           ClosePositionsByType(POSITION_TYPE_SELL, "Reversal Exit - Secure Profit");
           bear_broke   = false;
           bear_waiting = false;
           bear_neck    = 0;
       }
   }

   //══════════════════════════════════════════════════
   // BULLISH SETUP DETECTION
   //══════════════════════════════════════════════════
   // FIX: Only scan for a new neckline if we are NOT already in a confirmed
   // breakout/retest phase — prevents overwriting an active setup.
   if(!bull_broke && !bull_waiting)
   {
   double sh = GetSwingHigh(Buy_SwingLength, 1);
   if(sh > 0)
   {
       double impulse = sh - GetLow(1 + Buy_SwingLength);
       if(impulse >= atr * Buy_ImpulseMult)
       {
           bull_neck     = sh;
           bull_neck_bar = current_bar_idx - Buy_SwingLength;
           bull_broke    = false;
           bull_waiting  = false;
           Print("BRT Pro | Bull neckline set: ", DoubleToString(bull_neck, _Digits));

           // DRAW neckline box
           if(ShowBoxes)
           {
              datetime t_start = iTime(_Symbol, PERIOD_CURRENT, Buy_SwingLength);
              datetime t_end   = current_bar + PeriodSeconds(PERIOD_CURRENT) * Buy_RetestBars;
              DrawNecklineBox(true, t_start, t_end, bull_neck, atr);
           }
       }
   }
   } // end neckline scan guard

   if(bull_neck > 0 && !bull_broke)
   {
       int closes_above = 0;
       for(int k = 1; k <= Buy_BreakConfirm; k++)
           if(GetClose(k) > bull_neck) closes_above++;

       if(closes_above >= Buy_BreakConfirm)
       {
           bull_broke     = true;
           bull_broke_bar = current_bar_idx;
           bull_waiting   = true;
           Print("BRT Pro | Bull breakout confirmed above: ", DoubleToString(bull_neck, _Digits));
       }
   }

   if(bull_waiting && bull_neck > 0 && trend_ok_buy)
   {
       int bars_since = current_bar_idx - bull_broke_bar;
       bool in_zone   = (l1 <= bull_neck + atr * Buy_RetestATR) && (l1 >= bull_neck - atr * Buy_RetestATR);
       bool bouncing  = (c1 > bull_neck) && (c1 > o1);

       if(in_zone && bouncing && bars_since <= Buy_RetestBars)
       {
           if(CountPositions() < MaxPositions)
           {
               double entry   = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
               double sl      = entry - atr * Buy_SL_Mult;
               double tp      = entry + atr * Buy_TP_Mult;   // FIX: removed erroneous *2.0
               double sl_dist = entry - sl;
               double lot     = CalcLotSize(sl_dist);

               sl = NormalizeDouble(sl, _Digits);
               tp = NormalizeDouble(tp, _Digits);

               if(trade.Buy(lot, _Symbol, entry, sl, tp, "BRT Pro BUY"))
               {
                   Print("BRT Pro | BUY opened | Entry: ", DoubleToString(entry, _Digits),
                         " | SL: ", DoubleToString(sl, _Digits),
                         " | TP: ", DoubleToString(tp, _Digits),
                         " | Lot: ", DoubleToString(lot, 2));
                   lastSignal = "BUY @ " + DoubleToString(entry, _Digits);

                   if(ShowSignals)
                      DrawSignalArrow(true, iTime(_Symbol, PERIOD_CURRENT, 1), l1, sl, tp, atr);

                   bull_waiting = false;
                   bull_neck    = 0;
               }
           }
       }

       if(bars_since > Buy_RetestBars)
       {
           bull_waiting = false;
           bull_neck    = 0;
           Print("BRT Pro | Bull retest expired");
       }
   }

   //══════════════════════════════════════════════════
   // BEARISH SETUP DETECTION
   //══════════════════════════════════════════════════
   // FIX: Only scan for a new neckline if we are NOT already in a confirmed
   // breakout/retest phase — prevents overwriting an active setup.
   if(!bear_broke && !bear_waiting)
   {
   double sl_pivot = GetSwingLow(Sell_SwingLength, 1);
   if(sl_pivot > 0)
   {
       double impulse = GetHigh(1 + Sell_SwingLength) - sl_pivot;
       if(impulse >= atr * Sell_ImpulseMult)
       {
           bear_neck     = sl_pivot;
           bear_neck_bar = current_bar_idx - Sell_SwingLength;
           bear_broke    = false;
           bear_waiting  = false;
           Print("BRT Pro | Bear neckline set: ", DoubleToString(bear_neck, _Digits));

           if(ShowBoxes)
           {
              datetime t_start = iTime(_Symbol, PERIOD_CURRENT, Sell_SwingLength);
              datetime t_end   = current_bar + PeriodSeconds(PERIOD_CURRENT) * Sell_RetestBars;
              DrawNecklineBox(false, t_start, t_end, bear_neck, atr);
           }
       }
   }
   } // end neckline scan guard

   if(bear_neck > 0 && !bear_broke)
   {
       int closes_below = 0;
       for(int k = 1; k <= Sell_BreakConfirm; k++)
           if(GetClose(k) < bear_neck) closes_below++;

       if(closes_below >= Sell_BreakConfirm)
       {
           bear_broke     = true;
           bear_broke_bar = current_bar_idx;
           bear_waiting   = true;
           Print("BRT Pro | Bear breakout confirmed below: ", DoubleToString(bear_neck, _Digits));
       }
   }

   if(bear_waiting && bear_neck > 0 && trend_ok_sell)
   {
       int bars_since = current_bar_idx - bear_broke_bar;
       bool in_zone   = (h1 >= bear_neck - atr * Sell_RetestATR) && (h1 <= bear_neck + atr * Sell_RetestATR);
       bool bouncing  = (c1 < bear_neck) && (c1 < o1);

       if(in_zone && bouncing && bars_since <= Sell_RetestBars)
       {
           if(CountPositions() < MaxPositions)
           {
               double entry    = SymbolInfoDouble(_Symbol, SYMBOL_BID);
               double sl_price = entry + atr * Sell_SL_Mult;
               double tp_price = entry - atr * Sell_TP_Mult;   // FIX: removed erroneous *2.0
               double sl_dist  = sl_price - entry;
               double lot      = CalcLotSize(sl_dist);

               sl_price = NormalizeDouble(sl_price, _Digits);
               tp_price = NormalizeDouble(tp_price, _Digits);

               if(trade.Sell(lot, _Symbol, entry, sl_price, tp_price, "BRT Pro SELL"))
               {
                   Print("BRT Pro | SELL opened | Entry: ", DoubleToString(entry, _Digits),
                         " | SL: ", DoubleToString(sl_price, _Digits),
                         " | TP: ", DoubleToString(tp_price, _Digits),
                         " | Lot: ", DoubleToString(lot, 2));
                   lastSignal = "SELL @ " + DoubleToString(entry, _Digits);

                   if(ShowSignals)
                      DrawSignalArrow(false, iTime(_Symbol, PERIOD_CURRENT, 1), h1, sl_price, tp_price, atr);

                   bear_waiting = false;
                   bear_neck    = 0;
               }
           }
       }

       if(bars_since > Sell_RetestBars)
       {
           bear_waiting = false;
           bear_neck    = 0;
           Print("BRT Pro | Bear retest expired");
       }
   }
}

//+------------------------------------------------------------------+
// TRADE EVENT — log results
//+------------------------------------------------------------------+
void OnTradeTransaction(const MqlTradeTransaction& trans,
                        const MqlTradeRequest&     request,
                        const MqlTradeResult&      result)
{
   if(trans.type == TRADE_TRANSACTION_DEAL_ADD)
   {
       if(trans.deal_type == DEAL_TYPE_BUY || trans.deal_type == DEAL_TYPE_SELL)
       {
           double profit = HistoryDealGetDouble(trans.deal, DEAL_PROFIT);
           if(profit != 0)
           {
               string result_str = profit > 0 ? SYM_CHECK+" WIN" : SYM_CROSS+" LOSS";
               Print("BRT Pro | DEAL CLOSED | ", result_str,
                     " | Profit: ", DoubleToString(profit, 2),
                     " | Ticket: ", trans.deal);
           }
       }
   }
}
//+------------------------------------------------------------------+

//+------------------------------------------------------------------+
