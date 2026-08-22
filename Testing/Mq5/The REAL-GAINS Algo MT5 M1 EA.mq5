//+------------------------------------------------------------------+
//|                                    The REAL-GAINS Algo MT5 M1 EA|
//|                                      PSAR Crossover + Dashboard |
//|                              with SL, TP, trailing stop & forced|
//+------------------------------------------------------------------+
#property copyright "Syed Ali"
#property link      ""
#property version   "1.00"
#property strict

#include <Trade\Trade.mqh>
#include <Trade\SymbolInfo.mqh>
#include <Trade\AccountInfo.mqh>

//+------------------------------------------------------------------+
//| Input parameters                                                 |
//+------------------------------------------------------------------+
input group "==== Risk Management ===="
input double   InpRiskPercent       = 1.0;         // Risk % per trade
input int      InpStopLossPts       = 50;          // Stop Loss (points, 0 = none)
input int      InpTakeProfitPts     = 150;         // Take Profit (points, 0 = none)

input group "==== PSAR Settings ===="
input double   InpPSAR_Increment    = 0.02;        // PSAR increment (step)
input double   InpPSAR_Max          = 0.2;         // PSAR maximum

input group "==== Signal Filters ===="
input bool     InpUseSMAFilter      = false;       // Use SMA200 filter
input int      InpSMA200Period      = 200;         // SMA200 period

input group "==== Trailing Stop ===="
input bool     InpUseTrailing       = true;        // Enable trailing stop
input int      InpTrailStartPts     = 30;          // Start trailing after profit (points)
input int      InpTrailDistPts      = 20;          // Trailing distance (points)

input group "==== Forced Trade (Testing) ===="
input bool     InpForceTrade        = false;       // Force trades for testing
input int      InpForceTradeEvery   = 3;           // Force a trade every N bars

input group "==== EA Settings ===="
input int      InpMagicNumber       = 202403;      // Magic number
input bool     InpPrintDebug        = false;       // Print debug messages

input group "==== Dashboard ===="
input bool     InpShowDashboard     = true;        // Show on‑chart dashboard
input int      InpCorner            = 0;            // Dashboard corner (0=top-left,1=top-right,2=bottom-left,3=bottom-right)
input color    InpDashboardBg       = clrBlack;     // Background color
input color    InpDashboardText     = clrWhite;     // Text color
input int      InpDashboardFontSize = 8;            // Font size

//+------------------------------------------------------------------+
//| Global objects and variables                                     |
//+------------------------------------------------------------------+
CTrade         trade;
CSymbolInfo    sym;
CAccountInfo   acc;

string         g_prefix = "RGA_";               // Prefix for dashboard objects
datetime       g_lastBarTime = 0;
int            g_handlePSAR;
int            g_handleSMA200;
double         g_psar[], g_sma200[];
bool           g_inPosition = false;
int            g_positionDir = 0;               // 1 = long, -1 = short
ulong          g_positionTicket = 0;
double         g_entryPrice = 0;
int            g_forceCounter = 0;
int            g_forcedDir = 1;                  // start with buy

//+------------------------------------------------------------------+
//| Expert initialization function                                   |
//+------------------------------------------------------------------+
int OnInit()
{
   sym.Name(Symbol());
   trade.SetExpertMagicNumber(InpMagicNumber);
   trade.SetDeviationInPoints(10);
   trade.SetTypeFilling(ORDER_FILLING_FOK);

   // Create indicator handles
   g_handlePSAR = iSAR(sym.Name(), PERIOD_CURRENT, InpPSAR_Increment, InpPSAR_Max);
   g_handleSMA200 = iMA(sym.Name(), PERIOD_CURRENT, InpSMA200Period, 0, MODE_SMA, PRICE_CLOSE);
   if(g_handlePSAR == INVALID_HANDLE || g_handleSMA200 == INVALID_HANDLE)
   {
      Print("Failed to create indicator handles");
      return INIT_FAILED;
   }

   ArraySetAsSeries(g_psar, true);
   ArraySetAsSeries(g_sma200, true);

   if(InpShowDashboard)
      CreateDashboard();

   return INIT_SUCCEEDED;
}

//+------------------------------------------------------------------+
//| Expert deinitialization function                                 |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   IndicatorRelease(g_handlePSAR);
   IndicatorRelease(g_handleSMA200);
   if(InpShowDashboard)
      ObjectsDeleteAll(0, g_prefix);
}

//+------------------------------------------------------------------+
//| Check for new bar                                                |
//+------------------------------------------------------------------+
bool IsNewBar()
{
   datetime cur = iTime(sym.Name(), PERIOD_CURRENT, 0);
   if(cur != g_lastBarTime)
   {
      g_lastBarTime = cur;
      return true;
   }
   return false;
}

//+------------------------------------------------------------------+
//| Update position status                                           |
//+------------------------------------------------------------------+
void UpdatePositionStatus()
{
   if(g_positionTicket == 0)
   {
      g_inPosition = false;
      return;
   }
   if(!PositionSelectByTicket(g_positionTicket))
   {
      g_inPosition = false;
      g_positionTicket = 0;
      g_positionDir = 0;
      g_entryPrice = 0;
   }
}

//+------------------------------------------------------------------+
//| Entry signal detection                                           |
//+------------------------------------------------------------------+
bool CheckEntrySignals(int &direction)
{
   double close = iClose(sym.Name(), PERIOD_CURRENT, 1);
   double high  = iHigh(sym.Name(), PERIOD_CURRENT, 1);
   double low   = iLow(sym.Name(), PERIOD_CURRENT, 1);

   // Get PSAR lagging
   if(CopyBuffer(g_handlePSAR, 0, 1, 1, g_psar) != 1) return false;
   double psar = g_psar[0];

   // SMA filter
   bool smaOk = true;
   if(InpUseSMAFilter)
   {
      if(CopyBuffer(g_handleSMA200, 0, 1, 1, g_sma200) == 1)
         smaOk = (close > g_sma200[0]);
   }

   // Buy: high > psar
   bool buy = (high > psar) && smaOk;
   // Sell: low < psar
   bool sell = (low < psar) && smaOk;

   if(InpPrintDebug)
      Print("Entry check: buy=", buy, " sell=", sell);

   if(buy) { direction = 1; return true; }
   if(sell) { direction = -1; return true; }
   return false;
}

//+------------------------------------------------------------------+
//| Calculate lot size based on risk                                 |
//+------------------------------------------------------------------+
double CalculateLotSize(double stopDistance)
{
   if(stopDistance <= 0) return 0.01; // fallback
   double balance = acc.Balance();
   double riskAmount = balance * (InpRiskPercent / 100.0);
   double tickValue = sym.TickValue();
   double tickSize  = sym.TickSize();
   double lossPerLot = (stopDistance / tickSize) * tickValue;
   if(lossPerLot <= 0) return 0.01;

   double rawLots = riskAmount / lossPerLot;
   double lotStep = sym.LotsStep();
   double minLot  = sym.LotsMin();
   double maxLot  = sym.LotsMax();

   rawLots = MathFloor(rawLots / lotStep) * lotStep;
   rawLots = MathMax(minLot, MathMin(maxLot, rawLots));
   return rawLots;
}

//+------------------------------------------------------------------+
//| Enter trade                                                      |
//+------------------------------------------------------------------+
void EnterTrade(int direction)
{
   double ask = sym.Ask();
   double bid = sym.Bid();
   double entry = (direction == 1) ? ask : bid;
   double sl = 0, tp = 0;

   if(InpStopLossPts > 0)
   {
      sl = (direction == 1) ? entry - InpStopLossPts * sym.Point()
                            : entry + InpStopLossPts * sym.Point();
   }
   if(InpTakeProfitPts > 0)
   {
      tp = (direction == 1) ? entry + InpTakeProfitPts * sym.Point()
                            : entry - InpTakeProfitPts * sym.Point();
   }
   sl = NormalizeDouble(sl, sym.Digits());
   tp = NormalizeDouble(tp, sym.Digits());

   // Check minimum stop distance
   double minDist = sym.StopsLevel() * sym.Point();
   if(sl > 0 && ((direction == 1 && (entry - sl) < minDist) ||
                 (direction == -1 && (sl - entry) < minDist)))
   {
      if(InpPrintDebug) Print("Stop loss too close, entry skipped.");
      return;
   }

   double stopDist = (direction == 1) ? (entry - sl) : (sl - entry);
   double lot = CalculateLotSize(stopDist);

   bool res = (direction == 1) ? trade.Buy(lot, sym.Name(), 0, sl, tp, "RGA Buy")
                               : trade.Sell(lot, sym.Name(), 0, sl, tp, "RGA Sell");
   if(res)
   {
      g_positionTicket = trade.ResultOrder();
      g_inPosition = true;
      g_positionDir = direction;
      g_entryPrice = entry;
      if(InpPrintDebug) Print("Trade entered: ", (direction==1?"BUY":"SELL"), " lot=", lot);
   }
   else
   {
      if(InpPrintDebug) Print("Trade failed: ", trade.ResultRetcodeDescription());
   }
}

//+------------------------------------------------------------------+
//| Trailing stop                                                    |
//+------------------------------------------------------------------+
void ApplyTrailingStop()
{
   if(!InpUseTrailing || g_positionTicket == 0) return;
   if(!PositionSelectByTicket(g_positionTicket)) return;

   double open = PositionGetDouble(POSITION_PRICE_OPEN);
   double sl   = PositionGetDouble(POSITION_SL);
   double tp   = PositionGetDouble(POSITION_TP);
   ENUM_POSITION_TYPE type = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);

   double bid = sym.Bid();
   double ask = sym.Ask();

   double profitPoints = (type == POSITION_TYPE_BUY) ? (bid - open) / sym.Point()
                                                     : (open - ask) / sym.Point();
   if(profitPoints < InpTrailStartPts) return;

   double newSL = (type == POSITION_TYPE_BUY) ? bid - InpTrailDistPts * sym.Point()
                                              : ask + InpTrailDistPts * sym.Point();
   newSL = NormalizeDouble(newSL, sym.Digits());

   bool improve = (type == POSITION_TYPE_BUY) ? (newSL > sl) : (newSL < sl);
   if(improve)
   {
      double minDist = sym.StopsLevel() * sym.Point();
      bool valid = (type == POSITION_TYPE_BUY) ? (bid - newSL) >= minDist
                                               : (newSL - ask) >= minDist;
      if(valid)
      {
         trade.PositionModify(g_positionTicket, newSL, tp);
         if(InpPrintDebug) Print("Trailing stop moved to ", newSL);
      }
   }
}

//+------------------------------------------------------------------+
//| Dashboard creation                                               |
//+------------------------------------------------------------------+
void CreateDashboard()
{
   string obj;
   int x = (InpCorner == 0 || InpCorner == 2) ? 10 : 300;
   int y = (InpCorner == 0 || InpCorner == 1) ? 20 : 200;
   int w = 280, h = 180;

   // Background rectangle
   obj = g_prefix + "bg";
   ObjectCreate(0, obj, OBJ_RECTANGLE_LABEL, 0, 0, 0);
   ObjectSetInteger(0, obj, OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, obj, OBJPROP_YDISTANCE, y);
   ObjectSetInteger(0, obj, OBJPROP_XSIZE, w);
   ObjectSetInteger(0, obj, OBJPROP_YSIZE, h);
   ObjectSetInteger(0, obj, OBJPROP_BGCOLOR, InpDashboardBg);
   ObjectSetInteger(0, obj, OBJPROP_BORDER_TYPE, BORDER_FLAT);
   ObjectSetInteger(0, obj, OBJPROP_CORNER, InpCorner);
   ObjectSetInteger(0, obj, OBJPROP_COLOR, clrNONE);
   ObjectSetInteger(0, obj, OBJPROP_BACK, true);

   // Title
   obj = g_prefix + "title";
   ObjectCreate(0, obj, OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, obj, OBJPROP_XDISTANCE, x+5);
   ObjectSetInteger(0, obj, OBJPROP_YDISTANCE, y+5);
   ObjectSetString(0, obj, OBJPROP_TEXT, "THE REAL-GAINS ALGO");
   ObjectSetInteger(0, obj, OBJPROP_COLOR, clrYellow);
   ObjectSetInteger(0, obj, OBJPROP_FONTSIZE, InpDashboardFontSize+2);
   ObjectSetString(0, obj, OBJPROP_FONT, "Arial Bold");
   ObjectSetInteger(0, obj, OBJPROP_CORNER, InpCorner);

   // Lines
   int row = y + 30;
   int colL = x + 5;
   int colR = x + 150;

   AddLabel("Symbol", colL, row, "Symbol:");
   AddLabelVal("SymVal", colR, row, sym.Name());
   row += 18;

   AddLabel("Balance", colL, row, "Balance:");
   AddLabelVal("BalVal", colR, row, "");
   row += 18;

   AddLabel("Equity", colL, row, "Equity:");
   AddLabelVal("EqVal", colR, row, "");
   row += 18;

   AddLabel("Spread", colL, row, "Spread:");
   AddLabelVal("SprVal", colR, row, "");
   row += 18;

   AddLabel("PSAR", colL, row, "PSAR:");
   AddLabelVal("PsarVal", colR, row, "");
   row += 18;

   AddLabel("Signal", colL, row, "Signal:");
   AddLabelVal("SigVal", colR, row, "---");
   row += 18;

   AddLabel("Pos", colL, row, "Position:");
   AddLabelVal("PosVal", colR, row, "---");
}

//+------------------------------------------------------------------+
//| Helper to add a static label                                     |
//+------------------------------------------------------------------+
void AddLabel(string name, int x, int y, string text)
{
   string obj = g_prefix + name;
   ObjectCreate(0, obj, OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, obj, OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, obj, OBJPROP_YDISTANCE, y);
   ObjectSetString(0, obj, OBJPROP_TEXT, text);
   ObjectSetInteger(0, obj, OBJPROP_COLOR, InpDashboardText);
   ObjectSetInteger(0, obj, OBJPROP_FONTSIZE, InpDashboardFontSize);
   ObjectSetString(0, obj, OBJPROP_FONT, "Arial");
   ObjectSetInteger(0, obj, OBJPROP_CORNER, InpCorner);
}

//+------------------------------------------------------------------+
//| Helper to add a value label (will be updated)                   |
//+------------------------------------------------------------------+
void AddLabelVal(string name, int x, int y, string text)
{
   string obj = g_prefix + name;
   ObjectCreate(0, obj, OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, obj, OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, obj, OBJPROP_YDISTANCE, y);
   ObjectSetString(0, obj, OBJPROP_TEXT, text);
   ObjectSetInteger(0, obj, OBJPROP_COLOR, InpDashboardText);
   ObjectSetInteger(0, obj, OBJPROP_FONTSIZE, InpDashboardFontSize);
   ObjectSetString(0, obj, OBJPROP_FONT, "Arial");
   ObjectSetInteger(0, obj, OBJPROP_CORNER, InpCorner);
}

//+------------------------------------------------------------------+
//| Update dashboard values                                          |
//+------------------------------------------------------------------+
void UpdateDashboard()
{
   if(!InpShowDashboard) return;

   sym.RefreshRates();

   // Balance / Equity
   ObjectSetString(0, g_prefix+"BalVal", OBJPROP_TEXT, DoubleToString(acc.Balance(), 2));
   ObjectSetString(0, g_prefix+"EqVal", OBJPROP_TEXT, DoubleToString(acc.Equity(), 2));

   // Spread
   int spread = (int)((sym.Ask() - sym.Bid()) / sym.Point());
   ObjectSetString(0, g_prefix+"SprVal", OBJPROP_TEXT, IntegerToString(spread));

   // PSAR (current)
   if(CopyBuffer(g_handlePSAR, 0, 0, 1, g_psar) == 1)
      ObjectSetString(0, g_prefix+"PsarVal", OBJPROP_TEXT, DoubleToString(g_psar[0], sym.Digits()));
   else
      ObjectSetString(0, g_prefix+"PsarVal", OBJPROP_TEXT, "---");

   // Signal status (last bar)
   int dir = 0;
   bool hasSig = CheckEntrySignals(dir);
   string sigText = "---";
   if(hasSig)
      sigText = (dir == 1) ? "BUY" : "SELL";
   ObjectSetString(0, g_prefix+"SigVal", OBJPROP_TEXT, sigText);

   // Position
   string posText = "---";
   if(g_inPosition)
      posText = (g_positionDir == 1) ? "LONG" : "SHORT";
   ObjectSetString(0, g_prefix+"PosVal", OBJPROP_TEXT, posText);

   ChartRedraw();
}

//+------------------------------------------------------------------+
//| Expert tick function                                             |
//+------------------------------------------------------------------+
void OnTick()
{
   sym.RefreshRates();
   UpdatePositionStatus();

   // Update dashboard (every tick)
   if(InpShowDashboard)
      UpdateDashboard();

   // Entry logic (only if flat)
   if(!g_inPosition)
   {
      // Forced trade testing
      if(InpForceTrade && IsNewBar())
      {
         g_forceCounter++;
         if(g_forceCounter >= InpForceTradeEvery)
         {
            g_forceCounter = 0;
            g_forcedDir = -g_forcedDir;   // alternate
            if(InpPrintDebug) Print("Forced trade, direction=", g_forcedDir);
            EnterTrade(g_forcedDir);
            return;
         }
      }

      // Normal signal on new bar
      if(IsNewBar())
      {
         int dir = 0;
         if(CheckEntrySignals(dir))
            EnterTrade(dir);
      }
   }

   // Trailing stop
   if(g_inPosition)
      ApplyTrailingStop();
}
//+------------------------------------------------------------------+