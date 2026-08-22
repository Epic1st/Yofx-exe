//+------------------------------------------------------------------+
//| SMART MONEY ZONE DEVELOP WITH THE HLEP OF TJ&KAMRAN              |
//|                                                                  |
//| Sweep -> MSS -> FVG/OB retrace entry, SMT divergence filter.     |
//| Visuals (structure lines/boxes/dashboard) are kept as optional   |
//| chart drawings.                                                  |
//+------------------------------------------------------------------+
#property copyright "TJ -PINE-EA KAMI"
#property version   "1.10"
#property strict

#include <Trade\Trade.mqh>
CTrade trade;

//=====================================================================
// INPUTS
//=====================================================================
input group "===== Structure ====="
input int    InpPivLen     = 8;     // Swing pivot strength (bars each side)
input bool   InpShowBos    = true;  // Draw BOS / CHoCH lines+labels
input bool   InpShowSwing  = false; // Mark raw swing points

input group "===== Liquidity & Sweeps ====="
input bool   InpShowLiq    = true;  // Draw liquidity levels (BSL/SSL)
input bool   InpShowSweep  = true;  // Mark liquidity sweeps

input group "===== Market Structure Shift ====="
input double InpDispAtr    = 0.6;   // Displacement body (ATR x)
input int    InpMssWin     = 12;    // Sweep-to-MSS window (bars)
input int    InpEntryWin   = 20;    // MSS-to-entry window (bars)

input group "===== Entry Zones ====="
input bool   InpShowFVG    = true;  // Draw fair value gaps
input double InpFvgMinAtr  = 0.20;  // Min FVG size (ATR x)
input int    InpFvgMax     = 6;     // Max live FVGs tracked
input bool   InpShowOB     = true;  // Draw order blocks
input int    InpObMax      = 6;     // Max live order blocks tracked
input double InpZonePad    = 0.10;  // Equilibrium zone pad (ATR x)

input group "===== SMT Divergence ====="
input bool   InpUseSMT     = false; // Require SMT divergence to allow entry
input string InpSmtSymbol  = "";    // Correlated symbol (blank = disabled)
input bool   InpShowSMT    = true;  // Label SMT divergences on chart

input group "===== Session Filter (auto-adjusted from GMT0 by broker offset) ====="
input bool   InpUseSession   = false; // Only signal inside allowed sessions
input bool   InpTradeAsian   = true;  // Allow Asian session
input bool   InpTradeLondon  = true;  // Allow London session
input bool   InpTradeNY      = true;  // Allow New York session
input int    InpAsianStartGMT0  = 0;  // Asian session start (GMT0 hour)
input int    InpAsianEndGMT0    = 9;  // Asian session end (GMT0 hour)
input int    InpLondonStartGMT0 = 7;  // London session start (GMT0 hour)
input int    InpLondonEndGMT0   = 16; // London session end (GMT0 hour)
input int    InpNYStartGMT0     = 12; // New York session start (GMT0 hour)
input int    InpNYEndGMT0       = 21; // New York session end (GMT0 hour)

input group "===== Narrow Killzones (separate from broad session filter above; also GMT0-based) ====="
input bool   InpUseKillzones   = false; // Only signal inside these narrow killzones
input bool   InpKzLondonOn     = true;  // Enable London killzone
input int    InpKzLondonStartHourGMT0 = 6;  
input int    InpKzLondonStartMinGMT0  = 0;  
input int    InpKzLondonEndHourGMT0   = 9;  
input int    InpKzLondonEndMinGMT0    = 0;  
input bool   InpKzNYOn          = true; 
input int    InpKzNYStartHourGMT0 = 13; 
input int    InpKzNYStartMinGMT0  = 30; 
input int    InpKzNYEndHourGMT0   = 18; 
input int    InpKzNYEndMinGMT0    = 0;  

input group "===== Broker / GMT Offset ====="
input int    InpBrokerGMTOffset = 0; // Manual broker server GMT offset (hours) vs GMT0

input group "===== Candle Paint ====="
input bool   InpPaintCandles = false; // Recolor chart candles monochrome (cosmetic only)

input group "===== Signals & Trade ====="
input bool   InpShowBox    = true;   // Draw TP/SL boxes
input int    InpAtrLen     = 14;     // ATR length
input double InpSlPad      = 0.5;    // SL padding beyond sweep (ATR x)
input double InpTp1R       = 1.0;    // TP1 (R)
input double InpTp2R       = 2.0;    // TP2 (R)
input double InpTp3R       = 3.0;    // TP3 (R)
input int    InpMinGapBars = 10;     // Min bars between signals (0 = off)
input double InpLotTP1     = 0.01;   // Lot size for TP1 leg
input double InpLotTP2     = 0.01;   // Lot size for TP2 leg
input double InpLotTP3     = 0.01;   // Lot size for TP3 leg

input group "===== Break Even ====="
input bool   InpBreakEvenAll = true;  // Move SL to breakeven on ALL remaining legs once TP1 hits
input double InpBreakEvenBufferPoints = 20; // Buffer past entry (points) for breakeven SL

input group "===== Dashboard ====="
input bool   InpShowHUD    = true;   // Show Professional On-Chart Dashboard
input color  InpHUDColor   = clrDarkGray; // Dashboard text color

input group "===== General ====="
input ulong  InpMagic      = 990411; // Magic number
input int    InpSlippage   = 30;     // Slippage (points)
input bool   InpVerbose    = false;  // Verbose logging
input int    InpMaxMiscObjects = 150; // Max transient structure objects kept on chart

//=====================================================================
// GLOBALS
//=====================================================================
int hAtr;
datetime lastBarTime = 0;
int g_digits;

// swing structure
double shPrice = EMPTY_VALUE, slPrice = EMPTY_VALUE;
datetime shTime = 0, slTime = 0;
bool shBroken = false, slBroken = false;
int bias = 0;

// SMT tracking
double lastSHp = EMPTY_VALUE, prevSHp = EMPTY_VALUE;
double lastSLp = EMPTY_VALUE, prevSLp = EMPTY_VALUE;
double lastSHc = EMPTY_VALUE, prevSHc = EMPTY_VALUE;
double lastSLc = EMPTY_VALUE, prevSLc = EMPTY_VALUE;
bool   smtHasSym = false;
bool   bullSMT = false, bearSMT = false;

// state machine
int    bStage = 0, sStage = 0;
double bSweepLo = 0, sSweepHi = 0;
datetime bStageTime = 0, sStageTime = 0;
double bZoneTop = 0, bZoneBot = 0, sZoneTop = 0, sZoneBot = 0;

datetime lastSigTime = 0;

// order blocks / FVGs
struct ZoneBox { double top; double bot; int dir; datetime t1; datetime t2; string name; };
ZoneBox obZones[];
ZoneBox fvgZones[];
int obCounter = 0, fvgCounter = 0;

// transient chart objects
string g_miscObjNames[];

// trade legs for breakeven
struct TradeLeg { ulong ticket; int tpIndex; };
TradeLeg g_legs[];
bool g_tp1Hit = false;

// stats
int wins = 0, losses = 0;

// HUD Display states
double g_conv = 0;
string g_biasT = "Neutral";
string g_stageT = "Idle - hunting sweep";
string g_smtT = "off";
string g_sigT = "Waiting";

//=====================================================================
// UTILITY
//=====================================================================
void TrackMiscObject(string name)
  {
   int n = ArraySize(g_miscObjNames);
   ArrayResize(g_miscObjNames, n + 1);
   g_miscObjNames[n] = name;
   while(ArraySize(g_miscObjNames) > InpMaxMiscObjects)
     {
      ObjectDelete(0, g_miscObjNames[0]);
      for(int k = 0; k < ArraySize(g_miscObjNames) - 1; k++) g_miscObjNames[k] = g_miscObjNames[k+1];
      ArrayResize(g_miscObjNames, ArraySize(g_miscObjNames) - 1);
     }
  }

bool IsNewBar()
  {
   datetime t = iTime(_Symbol, PERIOD_CURRENT, 0);
   if(t != lastBarTime) { lastBarTime = t; return true; }
   return false;
  }

double AtrSafe()
  {
   double buf[1];
   if(CopyBuffer(hAtr, 0, 1, 1, buf) < 1) return _Point;
   double a = buf[0];
   if(a <= 0) return SymbolInfoDouble(_Symbol, SYMBOL_POINT);
   return a;
  }

bool HourInWindow(int hour, int startH, int endH)
  {
   if(startH == endH) return true;
   if(startH < endH) return (hour >= startH && hour < endH);
   return (hour >= startH || hour < endH);
  }

bool InAllowedSession()
  {
   if(!InpUseSession) return true;
   MqlDateTime mt;
   TimeToStruct(TimeCurrent(), mt);
   int gmt0Hour = ((mt.hour - InpBrokerGMTOffset) % 24 + 24) % 24;
   bool inAsian  = InpTradeAsian  && HourInWindow(gmt0Hour, InpAsianStartGMT0,  InpAsianEndGMT0);
   bool inLondon = InpTradeLondon && HourInWindow(gmt0Hour, InpLondonStartGMT0, InpLondonEndGMT0);
   bool inNY     = InpTradeNY     && HourInWindow(gmt0Hour, InpNYStartGMT0,     InpNYEndGMT0);
   return (inAsian || inLondon || inNY);
  }

bool MinuteInWindow(int mins, int startH, int startM, int endH, int endM)
  {
   int startMins = startH*60 + startM;
   int endMins   = endH*60 + endM;
   if(startMins == endMins) return true;
   if(startMins < endMins) return (mins >= startMins && mins < endMins);
   return (mins >= startMins || mins < endMins);
  }

bool InAllowedKillzone()
  {
   if(!InpUseKillzones) return true;
   MqlDateTime mt;
   TimeToStruct(TimeCurrent(), mt);
   int gmt0Hour = ((mt.hour - InpBrokerGMTOffset) % 24 + 24) % 24;
   int gmt0Mins = gmt0Hour*60 + mt.min;

   bool inLdnKZ = InpKzLondonOn && MinuteInWindow(gmt0Mins, InpKzLondonStartHourGMT0, InpKzLondonStartMinGMT0,
                                                            InpKzLondonEndHourGMT0,   InpKzLondonEndMinGMT0);
   bool inNyKZ  = InpKzNYOn     && MinuteInWindow(gmt0Mins, InpKzNYStartHourGMT0,     InpKzNYStartMinGMT0,
                                                            InpKzNYEndHourGMT0,       InpKzNYEndMinGMT0);
   return (inLdnKZ || inNyKZ);
  }

string GetLiveSession()
  {
   MqlDateTime mt;
   TimeToStruct(TimeCurrent(), mt);
   int gmt0Hour = ((mt.hour - InpBrokerGMTOffset) % 24 + 24) % 24;
   string session = "";
   if(HourInWindow(gmt0Hour, InpAsianStartGMT0, InpAsianEndGMT0)) session += "Asian ";
   if(HourInWindow(gmt0Hour, InpLondonStartGMT0, InpLondonEndGMT0)) session += "London ";
   if(HourInWindow(gmt0Hour, InpNYStartGMT0, InpNYEndGMT0)) session += "NewYork ";
   if(session == "") return "Between Sessions";
   return session;
  }

//=====================================================================
// PIVOT DETECTION 
//=====================================================================
bool CheckPivotHigh(double &price, datetime &t)
  {
   int mid = InpPivLen + 1;
   double midHigh = iHigh(_Symbol, PERIOD_CURRENT, mid);
   for(int i = 1; i <= InpPivLen; i++)
     {
      if(iHigh(_Symbol, PERIOD_CURRENT, mid - i) >= midHigh) return false; 
      if(iHigh(_Symbol, PERIOD_CURRENT, mid + i) >= midHigh) return false; 
     }
   price = midHigh;
   t = iTime(_Symbol, PERIOD_CURRENT, mid);
   return true;
  }

bool CheckPivotLow(double &price, datetime &t)
  {
   int mid = InpPivLen + 1;
   double midLow = iLow(_Symbol, PERIOD_CURRENT, mid);
   for(int i = 1; i <= InpPivLen; i++)
     {
      if(iLow(_Symbol, PERIOD_CURRENT, mid - i) <= midLow) return false;
      if(iLow(_Symbol, PERIOD_CURRENT, mid + i) <= midLow) return false;
     }
   price = midLow;
   t = iTime(_Symbol, PERIOD_CURRENT, mid);
   return true;
  }

//=====================================================================
// DRAWING HELPERS 
//=====================================================================
void DrawBosLine(bool bull, double lvl, datetime x1, string tag)
  {
   if(!InpShowBos) return;
   string ln = "QCE_BOS_LN_" + TimeToString(TimeCurrent(), TIME_SECONDS) + "_" + IntegerToString(MathRand());
   string lb = "QCE_BOS_LB_" + ln;
   datetime x2 = TimeCurrent();
   ObjectCreate(0, ln, OBJ_TREND, 0, x1, lvl, x2, lvl);
   ObjectSetInteger(0, ln, OBJPROP_COLOR, bull ? clrSeaGreen : clrFireBrick);
   ObjectSetInteger(0, ln, OBJPROP_STYLE, STYLE_DASH);
   ObjectSetInteger(0, ln, OBJPROP_RAY_RIGHT, false);
   ObjectCreate(0, lb, OBJ_TEXT, 0, x2, lvl);
   ObjectSetString(0, lb, OBJPROP_TEXT, tag);
   ObjectSetInteger(0, lb, OBJPROP_COLOR, bull ? clrSeaGreen : clrFireBrick);
   TrackMiscObject(ln);
   TrackMiscObject(lb);
  }

void DrawSweepLabel(bool up, double p)
  {
   if(!InpShowSweep) return;
   string nm = "QCE_SWEEP_" + TimeToString(TimeCurrent(), TIME_SECONDS) + "_" + IntegerToString(MathRand());
   ObjectCreate(0, nm, OBJ_TEXT, 0, TimeCurrent(), p);
   ObjectSetString(0, nm, OBJPROP_TEXT, "sweep");
   ObjectSetInteger(0, nm, OBJPROP_COLOR, up ? clrFireBrick : clrSeaGreen);
   TrackMiscObject(nm);
  }

void DrawSmtLabel(bool bull, double p)
  {
   if(!InpShowSMT) return;
   string nm = "QCE_SMT_" + TimeToString(TimeCurrent(), TIME_SECONDS) + "_" + IntegerToString(MathRand());
   ObjectCreate(0, nm, OBJ_TEXT, 0, TimeCurrent(), p);
   ObjectSetString(0, nm, OBJPROP_TEXT, "SMT");
   ObjectSetInteger(0, nm, OBJPROP_COLOR, bull ? clrSeaGreen : clrFireBrick);
   TrackMiscObject(nm);
  }

void DrawZoneBox(string name, datetime t1, datetime t2, double top, double bot, bool bull, string label)
  {
   ObjectCreate(0, name, OBJ_RECTANGLE, 0, t1, top, t2, bot);
   ObjectSetInteger(0, name, OBJPROP_COLOR, bull ? clrSeaGreen : clrFireBrick);
   ObjectSetInteger(0, name, OBJPROP_FILL, true);
   ObjectSetInteger(0, name, OBJPROP_BACK, true);
   if(label != "")
     {
      string lbName = name + "_lbl";
      ObjectCreate(0, lbName, OBJ_TEXT, 0, t1, top);
      ObjectSetString(0, lbName, OBJPROP_TEXT, label);
      ObjectSetInteger(0, lbName, OBJPROP_COLOR, bull ? clrSeaGreen : clrFireBrick);
      TrackMiscObject(lbName);
     }
  }

void DrawSwingMarker(double p, datetime t)
  {
   if(!InpShowSwing) return;
   string nm = "QCE_SWING_" + TimeToString(TimeCurrent(), TIME_SECONDS) + "_" + IntegerToString(MathRand());
   ObjectCreate(0, nm, OBJ_TEXT, 0, t, p);
   ObjectSetString(0, nm, OBJPROP_TEXT, ".");
   ObjectSetInteger(0, nm, OBJPROP_COLOR, clrGray);
   TrackMiscObject(nm);
  }

void DrawLiquidityLines()
  {
   if(!InpShowLiq) return;
   if(shPrice != EMPTY_VALUE)
     {
      ObjectCreate(0, "QCE_BSL", OBJ_HLINE, 0, 0, shPrice);
      ObjectSetInteger(0, "QCE_BSL", OBJPROP_COLOR, clrGray);
      ObjectSetInteger(0, "QCE_BSL", OBJPROP_STYLE, STYLE_DOT);
     }
   if(slPrice != EMPTY_VALUE)
     {
      ObjectCreate(0, "QCE_SSL", OBJ_HLINE, 0, 0, slPrice);
      ObjectSetInteger(0, "QCE_SSL", OBJPROP_COLOR, clrGray);
      ObjectSetInteger(0, "QCE_SSL", OBJPROP_STYLE, STYLE_DOT);
     }
  }

//=====================================================================
// SMT: pull correlated symbol's high/low
//=====================================================================
void GetCorrelatedHL(datetime t, double &cHigh, double &cLow)
  {
   cHigh = EMPTY_VALUE; cLow = EMPTY_VALUE;
   if(!smtHasSym) return;
   int idx = iBarShift(InpSmtSymbol, PERIOD_CURRENT, t, true);
   if(idx < 0) return;
   cHigh = iHigh(InpSmtSymbol, PERIOD_CURRENT, idx);
   cLow  = iLow(InpSmtSymbol, PERIOD_CURRENT, idx);
  }

//=====================================================================
// DASHBOARD LOGIC (Professional GUI)
//=====================================================================
void DrawHUDLabel(string name, string text, int x, int y, int fontSize, color clr, ENUM_ANCHOR_POINT anchor = ANCHOR_RIGHT_UPPER)
  {
   if(ObjectFind(0, name) < 0)
     {
      ObjectCreate(0, name, OBJ_LABEL, 0, 0, 0);
      ObjectSetInteger(0, name, OBJPROP_CORNER, CORNER_RIGHT_UPPER);
      ObjectSetInteger(0, name, OBJPROP_ANCHOR, anchor);
      ObjectSetString(0, name, OBJPROP_FONT, "Trebuchet MS");
      ObjectSetInteger(0, name, OBJPROP_FONTSIZE, fontSize);
      ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
      ObjectSetInteger(0, name, OBJPROP_HIDDEN, true);
     }
   ObjectSetString(0, name, OBJPROP_TEXT, text);
   ObjectSetInteger(0, name, OBJPROP_COLOR, clr);
   ObjectSetInteger(0, name, OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, name, OBJPROP_YDISTANCE, y);
  }

void RefreshDashboard()
  {
   if(!InpShowHUD) return;
   
   int resolved = wins + losses;
   string winRatio = (resolved == 0) ? "0%" : IntegerToString((int)MathRound((double)wins/resolved*100.0)) + "%";
   int spread = (int)SymbolInfoInteger(_Symbol, SYMBOL_SPREAD);
   string currentSession = GetLiveSession();
   
   int x = 20;
   color tClr = InpHUDColor;
   color tClrVal = clrDeepSkyBlue; 
   color titleClr = clrGoldenrod;

   // Title
   DrawHUDLabel("HUD_TITLE", "SMART MONEY ZONE TJ&KAMI", x, 20, 11, titleClr);
   
   // Row 1: Pair & Spread
   DrawHUDLabel("HUD_PAIR_LBL", "Pair:", x+150, 45, 9, tClr);
   DrawHUDLabel("HUD_PAIR_VAL", _Symbol, x, 45, 9, tClrVal);
   
   DrawHUDLabel("HUD_SPREAD_LBL", "Live Spread:", x+150, 60, 9, tClr);
   DrawHUDLabel("HUD_SPREAD_VAL", IntegerToString(spread) + " pts", x, 60, 9, tClrVal);
   
   // Row 2: Session
   DrawHUDLabel("HUD_SESS_LBL", "Live Session:", x+150, 75, 9, tClr);
   DrawHUDLabel("HUD_SESS_VAL", currentSession, x, 75, 9, tClrVal);
   
   // Row 3: Stats
   DrawHUDLabel("HUD_WINS_LBL", "Wins | Losses:", x+150, 95, 9, tClr);
   DrawHUDLabel("HUD_WINS_VAL", IntegerToString(wins) + " | " + IntegerToString(losses), x, 95, 9, clrMediumSeaGreen);
   
   DrawHUDLabel("HUD_RATIO_LBL", "Win Ratio:", x+150, 110, 9, tClr);
   DrawHUDLabel("HUD_RATIO_VAL", winRatio, x, 110, 9, clrMediumSeaGreen);

   // Row 4: Strategy Status
   DrawHUDLabel("HUD_BIAS_LBL", "Bias:", x+150, 130, 9, tClr);
   DrawHUDLabel("HUD_BIAS_VAL", g_biasT, x, 130, 9, (g_biasT=="Bullish"?clrSeaGreen:(g_biasT=="Bearish"?clrFireBrick:tClrVal)));
   
   DrawHUDLabel("HUD_STAGE_LBL", "Stage:", x+150, 145, 9, tClr);
   DrawHUDLabel("HUD_STAGE_VAL", g_stageT, x, 145, 9, tClrVal);
   
   DrawHUDLabel("HUD_CONV_LBL", "Conviction:", x+150, 160, 9, tClr);
   DrawHUDLabel("HUD_CONV_VAL", IntegerToString((int)g_conv) + "%", x, 160, 9, tClrVal);
  }

void CleanDashboard()
  {
   ObjectsDeleteAll(0, "HUD_");
   Comment(""); 
  }

//=====================================================================
// INIT / DEINIT
//=====================================================================
int OnInit()
  {
   hAtr = iATR(_Symbol, PERIOD_CURRENT, InpAtrLen);
   if(hAtr == INVALID_HANDLE) { Print("TJRSmartMoneyEA: ATR handle failed"); return(INIT_FAILED); }

   smtHasSym = (InpSmtSymbol != "");
   if(smtHasSym) SymbolSelect(InpSmtSymbol, true);

   g_digits = (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);

   trade.SetExpertMagicNumber(InpMagic);
   trade.SetDeviationInPoints(InpSlippage);
   trade.SetTypeFillingBySymbol(_Symbol);

   if(InpPaintCandles)
     {
      ChartSetInteger(0, CHART_MODE, CHART_CANDLES);
      ChartSetInteger(0, CHART_COLOR_CANDLE_BULL, clrWhiteSmoke);
      ChartSetInteger(0, CHART_COLOR_CANDLE_BEAR, clrGray);
      ChartSetInteger(0, CHART_COLOR_CHART_UP, clrWhiteSmoke);
      ChartSetInteger(0, CHART_COLOR_CHART_DOWN, clrGray);
     }

   ArrayResize(g_legs, 0);
   ArrayResize(obZones, 0);
   ArrayResize(fvgZones, 0);
   return(INIT_SUCCEEDED);
  }

void OnDeinit(const int reason)
  {
   IndicatorRelease(hAtr);
   CleanDashboard();
   ObjectsDeleteAll(0, "QCE_");
  }

//=====================================================================
// ORDER BLOCK / FVG MAINTENANCE 
//=====================================================================
void MaintainObZones(double closeNow)
  {
   for(int i = ArraySize(obZones) - 1; i >= 0; i--)
     {
      bool mitig = (obZones[i].dir == 1) ? (closeNow < obZones[i].bot) : (closeNow > obZones[i].top);
      if(mitig)
        {
         if(InpShowOB) { ObjectDelete(0, obZones[i].name); ObjectDelete(0, obZones[i].name + "_lbl"); }
         for(int k = i; k < ArraySize(obZones) - 1; k++) obZones[k] = obZones[k+1];
         ArrayResize(obZones, ArraySize(obZones) - 1);
        }
     }
   while(ArraySize(obZones) > InpObMax)
     {
      if(InpShowOB) { ObjectDelete(0, obZones[0].name); ObjectDelete(0, obZones[0].name + "_lbl"); }
      for(int k = 0; k < ArraySize(obZones) - 1; k++) obZones[k] = obZones[k+1];
      ArrayResize(obZones, ArraySize(obZones) - 1);
     }
  }

void MaintainFvgZones(double closeNow)
  {
   for(int i = ArraySize(fvgZones) - 1; i >= 0; i--)
     {
      bool filled = (fvgZones[i].dir == 1) ? (closeNow < fvgZones[i].bot) : (closeNow > fvgZones[i].top);
      if(filled)
        {
         if(InpShowFVG) ObjectDelete(0, fvgZones[i].name);
         for(int k = i; k < ArraySize(fvgZones) - 1; k++) fvgZones[k] = fvgZones[k+1];
         ArrayResize(fvgZones, ArraySize(fvgZones) - 1);
        }
     }
   while(ArraySize(fvgZones) > InpFvgMax)
     {
      if(InpShowFVG) ObjectDelete(0, fvgZones[0].name);
      for(int k = 0; k < ArraySize(fvgZones) - 1; k++) fvgZones[k] = fvgZones[k+1];
      ArrayResize(fvgZones, ArraySize(fvgZones) - 1);
     }
  }

bool LatestFVG(int dir, double &top, double &bot)
  {
   for(int i = ArraySize(fvgZones) - 1; i >= 0; i--)
      if(fvgZones[i].dir == dir) { top = fvgZones[i].top; bot = fvgZones[i].bot; return true; }
   return false;
  }

//=====================================================================
// TRADE / BREAKEVEN HELPERS 
//=====================================================================
double NormalizeLot(double lot)
  {
   double minLot  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double maxLot  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
   double stepLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
   lot = MathRound(lot / stepLot) * stepLot;
   if(lot < minLot) lot = minLot;
   if(lot > maxLot) lot = maxLot;
   return lot;
  }

void ClearLegs() { ArrayResize(g_legs, 0); }
void AddLeg(ulong ticket, int tpIndex)
  {
   int n = ArraySize(g_legs);
   ArrayResize(g_legs, n + 1);
   g_legs[n].ticket = ticket;
   g_legs[n].tpIndex = tpIndex;
  }

void CloseAllPositions()
  {
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetInteger(POSITION_MAGIC) != (long)InpMagic) continue;
      if(PositionGetString(POSITION_SYMBOL) != _Symbol) continue;
      trade.PositionClose(ticket);
     }
   ClearLegs();
   g_tp1Hit = false;
  }

bool WasLegProfitable(ulong ticket)
  {
   if(!HistorySelectByPosition(ticket)) return false;
   int total = HistoryDealsTotal();
   double netProfit = 0.0;
   bool foundExit = false;
   for(int d = 0; d < total; d++)
     {
      ulong dealTicket = HistoryDealGetTicket(d);
      if(dealTicket == 0) continue;
      if((long)HistoryDealGetInteger(dealTicket, DEAL_ENTRY) != DEAL_ENTRY_OUT) continue;
      foundExit = true;
      netProfit += HistoryDealGetDouble(dealTicket, DEAL_PROFIT)
                 + HistoryDealGetDouble(dealTicket, DEAL_SWAP)
                 + HistoryDealGetDouble(dealTicket, DEAL_COMMISSION);
     }
   return foundExit && netProfit > 0;
  }

void ApplyBreakEvenToRemaining()
  {
   double p = SymbolInfoDouble(_Symbol, SYMBOL_POINT);
   for(int i = 0; i < ArraySize(g_legs); i++)
     {
      if(g_legs[i].tpIndex == 1) continue;
      ulong ticket = g_legs[i].ticket;
      if(!PositionSelectByTicket(ticket)) continue;
      long type = PositionGetInteger(POSITION_TYPE);
      double openPrice = PositionGetDouble(POSITION_PRICE_OPEN);
      double curTP = PositionGetDouble(POSITION_TP);
      double newSL = (type == POSITION_TYPE_BUY)
                     ? NormalizeDouble(openPrice + InpBreakEvenBufferPoints*p, g_digits)
                     : NormalizeDouble(openPrice - InpBreakEvenBufferPoints*p, g_digits);
      if(!trade.PositionModify(ticket, newSL, curTP))
         PrintFormat("TJR: breakeven modify FAILED ticket=%I64u retcode=%d (%s)",
                     ticket, trade.ResultRetcode(), trade.ResultRetcodeDescription());
     }
   if(InpVerbose) Print("TJR: breakeven applied to remaining legs");
  }

void InvalidateRemainingLegs()
  {
   for(int i = 0; i < ArraySize(g_legs); i++)
     {
      ulong ticket = g_legs[i].ticket;
      if(PositionSelectByTicket(ticket)) trade.PositionClose(ticket);
     }
   if(InpVerbose) Print("TJR: TP1 leg stopped out — closing remaining legs");
  }

void MonitorLegs()
  {
   for(int i = ArraySize(g_legs) - 1; i >= 0; i--)
     {
      ulong ticket = g_legs[i].ticket;
      if(PositionSelectByTicket(ticket)) continue; 

      bool profitable = WasLegProfitable(ticket);
      if(profitable) wins++; else losses++;

      if(g_legs[i].tpIndex == 1 && !g_tp1Hit)
        {
         g_tp1Hit = true;
         if(InpBreakEvenAll)
           {
            if(profitable) ApplyBreakEvenToRemaining();
            else InvalidateRemainingLegs();
           }
        }

      for(int k = i; k < ArraySize(g_legs) - 1; k++) g_legs[k] = g_legs[k+1];
      ArrayResize(g_legs, ArraySize(g_legs) - 1);
     }
  }

void OpenTradeSet(bool isBuy, double entry, double sl, double tp1, double tp2, double tp3)
  {
   double lot1 = NormalizeLot(InpLotTP1);
   double lot2 = NormalizeLot(InpLotTP2);
   double lot3 = NormalizeLot(InpLotTP3);

   ClearLegs();
   g_tp1Hit = false;

   bool ok1, ok2, ok3;
   if(isBuy)
     {
      ok1 = trade.Buy(lot1, _Symbol, 0.0, sl, tp1, "TJR-TP1");
      ok2 = trade.Buy(lot2, _Symbol, 0.0, sl, tp2, "TJR-TP2");
      ok3 = trade.Buy(lot3, _Symbol, 0.0, sl, tp3, "TJR-TP3");
     }
   else
     {
      ok1 = trade.Sell(lot1, _Symbol, 0.0, sl, tp1, "TJR-TP1");
      ok2 = trade.Sell(lot2, _Symbol, 0.0, sl, tp2, "TJR-TP2");
      ok3 = trade.Sell(lot3, _Symbol, 0.0, sl, tp3, "TJR-TP3");
     }

   if(ok1) AddLeg(trade.ResultOrder(), 1);
   if(ok2) AddLeg(trade.ResultOrder(), 2);
   if(ok3) AddLeg(trade.ResultOrder(), 3);
  }

//=====================================================================
// MAIN PER-BAR LOGIC
//=====================================================================
void ProcessBar()
  {
   double atrSafe = AtrSafe();

   double o1 = iOpen(_Symbol, PERIOD_CURRENT, 1);
   double c1 = iClose(_Symbol, PERIOD_CURRENT, 1);
   double h1 = iHigh(_Symbol, PERIOD_CURRENT, 1);
   double l1 = iLow(_Symbol, PERIOD_CURRENT, 1);
   datetime t1 = iTime(_Symbol, PERIOD_CURRENT, 1);

   // ---- 1. Pivots ----
   double ph, pl; datetime pht, plt;
   bool gotPH = CheckPivotHigh(ph, pht);
   bool gotPL = CheckPivotLow(pl, plt);

   if(gotPH)
     {
      shPrice = ph; shTime = pht; shBroken = false;
      prevSHp = lastSHp; lastSHp = ph;
      double cH, cL; GetCorrelatedHL(pht, cH, cL);
      prevSHc = lastSHc; lastSHc = cH;
      if(InpShowSwing) DrawSwingMarker(ph, pht);
     }
   if(gotPL)
     {
      slPrice = pl; slTime = plt; slBroken = false;
      prevSLp = lastSLp; lastSLp = pl;
      double cH, cL; GetCorrelatedHL(plt, cH, cL);
      prevSLc = lastSLc; lastSLc = cL;
      if(InpShowSwing) DrawSwingMarker(pl, plt);
     }

   bearSMT = smtHasSym && prevSHp != EMPTY_VALUE && lastSHp != EMPTY_VALUE && lastSHp > prevSHp && lastSHc <= prevSHc;
   bullSMT = smtHasSym && prevSLp != EMPTY_VALUE && lastSLp != EMPTY_VALUE && lastSLp < prevSLp && lastSLc >= prevSLc;
   if(gotPH && bearSMT) DrawSmtLabel(false, ph);
   if(gotPL && bullSMT) DrawSmtLabel(true, pl);

   // ---- 2. Structure break (BOS/CHoCH) ----
   bool bullBreak = (shPrice != EMPTY_VALUE) && !shBroken && (c1 > shPrice);
   bool bearBreak = (slPrice != EMPTY_VALUE) && !slBroken && (c1 < slPrice);
   bool choChUp = false, choChDn = false;

   if(bullBreak) { choChUp = (bias < 0); shBroken = true; bias = 1; DrawBosLine(true, shPrice, shTime, choChUp?"CHoCH":"BOS"); }
   if(bearBreak) { choChDn = (bias > 0); slBroken = true; bias = -1; DrawBosLine(false, slPrice, slTime, choChDn?"CHoCH":"BOS"); }

   bool dispUp = (c1 - o1) > InpDispAtr * atrSafe;
   bool dispDn = (o1 - c1) > InpDispAtr * atrSafe;

   // ---- 3. Liquidity sweeps ----
   bool sweepUp = (shPrice != EMPTY_VALUE) && (h1 > shPrice) && (c1 < shPrice);
   bool sweepDn = (slPrice != EMPTY_VALUE) && (l1 < slPrice) && (c1 > slPrice);
   if(sweepUp) DrawSweepLabel(true, h1);
   if(sweepDn) DrawSweepLabel(false, l1);
   DrawLiquidityLines();

   // ---- 4. Order blocks ----
   double o2 = iOpen(_Symbol, PERIOD_CURRENT, 2);
   double c2 = iClose(_Symbol, PERIOD_CURRENT, 2);
   double h2 = iHigh(_Symbol, PERIOD_CURRENT, 2);
   double l2 = iLow(_Symbol, PERIOD_CURRENT, 2);
   datetime t2 = iTime(_Symbol, PERIOD_CURRENT, 2);

   bool bullOB = dispUp && (c2 < o2);
   bool bearOB = dispDn && (c2 > o2);
   if(bullOB)
     {
      ZoneBox z; z.top = MathMax(o2,c2); z.bot = l2; z.dir = 1; z.t1 = t2; z.t2 = t1;
      z.name = "QCE_OB_" + IntegerToString(obCounter++);
      int n = ArraySize(obZones); ArrayResize(obZones, n+1); obZones[n] = z;
      if(InpShowOB) DrawZoneBox(z.name, z.t1, z.t2, z.top, z.bot, true, "Demand");
     }
   if(bearOB)
     {
      ZoneBox z; z.top = h2; z.bot = MathMin(o2,c2); z.dir = -1; z.t1 = t2; z.t2 = t1;
      z.name = "QCE_OB_" + IntegerToString(obCounter++);
      int n = ArraySize(obZones); ArrayResize(obZones, n+1); obZones[n] = z;
      if(InpShowOB) DrawZoneBox(z.name, z.t1, z.t2, z.top, z.bot, false, "Supply");
     }
   MaintainObZones(c1);

   // ---- 5. Fair value gaps ----
   double h3 = iHigh(_Symbol, PERIOD_CURRENT, 3);
   double l3 = iLow(_Symbol, PERIOD_CURRENT, 3);
   datetime t3 = iTime(_Symbol, PERIOD_CURRENT, 3);

   bool bullFVG = (l1 > h3) && (l1 - h3) > InpFvgMinAtr * atrSafe;
   bool bearFVG = (h1 < l3) && (l3 - h1) > InpFvgMinAtr * atrSafe;
   if(bullFVG)
     {
      ZoneBox z; z.top = l1; z.bot = h3; z.dir = 1; z.t1 = t3; z.t2 = t1;
      z.name = "QCE_FVG_" + IntegerToString(fvgCounter++);
      int n = ArraySize(fvgZones); ArrayResize(fvgZones, n+1); fvgZones[n] = z;
      if(InpShowFVG) DrawZoneBox(z.name, z.t1, z.t2, z.top, z.bot, true, "");
     }
   if(bearFVG)
     {
      ZoneBox z; z.top = l3; z.bot = h1; z.dir = -1; z.t1 = t3; z.t2 = t1;
      z.name = "QCE_FVG_" + IntegerToString(fvgCounter++);
      int n = ArraySize(fvgZones); ArrayResize(fvgZones, n+1); fvgZones[n] = z;
      if(InpShowFVG) DrawZoneBox(z.name, z.t1, z.t2, z.top, z.bot, false, "");
     }
   MaintainFvgZones(c1);

   // ---- 6. State machine ----
   if(sweepDn) { bStage = 1; bSweepLo = l1; bStageTime = t1; }
   if(sweepUp) { sStage = 1; sSweepHi = h1; sStageTime = t1; }

   bool mssUp = bullBreak && dispUp;
   bool mssDn = bearBreak && dispDn;

   int barsSinceB = (int)MathRound((double)(t1 - bStageTime) / PeriodSeconds());
   int barsSinceS = (int)MathRound((double)(t1 - sStageTime) / PeriodSeconds());

   if(bStage == 1 && mssUp && barsSinceB <= InpMssWin)
     {
      bStage = 2; bStageTime = t1;
      double eq = (h1 + bSweepLo) / 2.0;
      double fTop, fBot;
      if(bullFVG) { bZoneTop = l1; bZoneBot = h3; }
      else if(LatestFVG(1, fTop, fBot)) { bZoneTop = fTop; bZoneBot = fBot; }
      else { bZoneTop = eq + InpZonePad*atrSafe; bZoneBot = eq - InpZonePad*atrSafe; }
     }
   if(sStage == 1 && mssDn && barsSinceS <= InpMssWin)
     {
      sStage = 2; sStageTime = t1;
      double eq = (l1 + sSweepHi) / 2.0;
      double fTop, fBot;
      if(bearFVG) { sZoneTop = l3; sZoneBot = h1; }
      else if(LatestFVG(-1, fTop, fBot)) { sZoneTop = fTop; sZoneBot = fBot; }
      else { sZoneTop = eq + InpZonePad*atrSafe; sZoneBot = eq - InpZonePad*atrSafe; }
     }

   if(bStage == 1 && barsSinceB > InpMssWin) bStage = 0;
   if(sStage == 1 && barsSinceS > InpMssWin) sStage = 0;
   if(bStage == 2 && barsSinceB > InpEntryWin) bStage = 0;
   if(sStage == 2 && barsSinceS > InpEntryWin) sStage = 0;

   // ---- 7. Entry trigger ----
   bool bullTap = (bStage == 2) && (l1 <= bZoneTop) && (c1 >= bZoneBot) && (c1 >= o1);
   bool bearTap = (sStage == 2) && (h1 >= sZoneBot) && (c1 <= sZoneTop) && (c1 <= o1);

   bool sessionOK = InAllowedSession() && InAllowedKillzone();
   bool buy  = bullTap && sessionOK && (!InpUseSMT || bullSMT);
   bool sell = bearTap && sessionOK && (!InpUseSMT || bearSMT);

   if(buy)  bStage = 0;
   if(sell) sStage = 0;

   sell = sell && !buy;

   int barsSinceSig = (lastSigTime == 0) ? INT_MAX : (int)MathRound((double)(t1 - lastSigTime) / PeriodSeconds());
   bool okGap = (InpMinGapBars <= 0) || (lastSigTime == 0) || (barsSinceSig >= InpMinGapBars);
   buy  = buy  && okGap;
   sell = sell && okGap;
   bool sig = buy || sell;
   if(sig) lastSigTime = t1;

   // ---- 8. Trade execution ----
   if(sig)
     {
      double slBase = buy ? ((bSweepLo>0)?bSweepLo:l1) - InpSlPad*atrSafe
                           : ((sSweepHi>0)?sSweepHi:h1) + InpSlPad*atrSafe;
      double entry = buy ? SymbolInfoDouble(_Symbol, SYMBOL_ASK) : SymbolInfoDouble(_Symbol, SYMBOL_BID);
      double risk = MathAbs(entry - slBase);
      if(risk <= 0) risk = atrSafe;

      double sl  = buy ? entry - risk : entry + risk;
      double tp1 = buy ? entry + risk*InpTp1R : entry - risk*InpTp1R;
      double tp2 = buy ? entry + risk*InpTp2R : entry - risk*InpTp2R;
      double tp3 = buy ? entry + risk*InpTp3R : entry - risk*InpTp3R;

      sl  = NormalizeDouble(sl, g_digits);
      tp1 = NormalizeDouble(tp1, g_digits);
      tp2 = NormalizeDouble(tp2, g_digits);
      tp3 = NormalizeDouble(tp3, g_digits);

      CloseAllPositions();
      OpenTradeSet(buy, entry, sl, tp1, tp2, tp3);

      if(InpShowBox)
        {
         ObjectDelete(0, "QCE_TPSL_risk");
         ObjectDelete(0, "QCE_TPSL_rwd");
         DrawZoneBox("QCE_TPSL_risk", t1, t1 + 24*PeriodSeconds(), entry, sl, !buy, "");
         DrawZoneBox("QCE_TPSL_rwd",  t1, t1 + 24*PeriodSeconds(), entry, tp3, buy, "");
        }
     }

   // ---- 9. Cache HUD Variables ----
   g_biasT = bias == 1 ? "Bullish" : bias == -1 ? "Bearish" : "Neutral";
   int stageN = MathMax(bStage, sStage);
   string stageDir = bStage >= sStage ? "long" : "short";
   g_stageT = stageN == 2 ? "MSS - await entry (" + stageDir + ")" : stageN == 1 ? "Swept - await MSS (" + stageDir + ")" : "Idle - hunting sweep";
   g_smtT = !smtHasSym ? "off" : bullSMT ? "bullish" : bearSMT ? "bearish" : "none";
   g_sigT = buy ? "BUY" : sell ? "SELL" : "Waiting";
   
   double cStage = stageN / 2.0;
   double cSmt = (bullSMT || bearSMT) ? 1.0 : 0.0;
   double cKz  = sessionOK ? 1.0 : 0.0;
   double cSig = sig ? 1.0 : 0.0;
   g_conv = MathMin(100.0, 100.0*(0.40*cStage + 0.25*cSmt + 0.15*cKz + 0.20*cSig));
  }

//=====================================================================
// ONTICK
//=====================================================================
void OnTick()
  {
   MonitorLegs();
   RefreshDashboard(); // HUD updates every tick to capture Live Spread and Session accurately.
   if(!IsNewBar()) return;
   ProcessBar();
  }
//+------------------------------------------------------------------+