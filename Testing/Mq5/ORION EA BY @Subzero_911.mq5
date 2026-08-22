#property copyright "CLICK HERE"
#property link      "https://t.me/paidfxtools"
#property version   "2.00"

#property description "We crack MT4 n MT5 Indicators/Robots."
#property description "We convert MT4/MT5 Indicators to Robots."
#property description "."
#property description "CRACKED BY @Subzero_911"
#property description "."
#property description "Instagram = @Subzero_911_"
#property description "."
#property description "Telegram = @Subzero_911"

//+------------------------------------------------------------------+
//| Input Parameters                                                 |
//+------------------------------------------------------------------+
input group "🎨 CHART COLORS - AUTO APPLIED";
input bool     AutoColorChart = true;       // Auto-color chart on attach?
input color    BullCandleColor = clrDodgerBlue; // Bull candle color
input color    BearCandleColor = clrOrangeRed;  // Bear candle color
input color    WickColor = clrDimGray;      // Wick color
input color    BorderColor = clrGray;       // Candle border color
input color    ChartBgColor = clrBlue;     // Chart background
input color    GridColor = clrDimGray;      // Grid color
input color    AxisColor = clrNavy;         // Axis color

input group "Strategy Core Logic";
input int      LookbackPeriod = 15;         // Lookback Period
input double   ATRMultiplier = 2.5;         // Box Width (ATR)
input double   SlopeTolerance = 0.2;        // Slope Tolerance
input int      CooldownBars = 5;            // Cooldown (Bars)
input int      MaxBoxDuration = 45;         // Max Box Duration (Bars)
input bool     CloseAtCandleClose = false;  // Close trades at candle close?
input int      CandleCloseCooldown = 0;     // Cooldown bars after candle close

input group "Trend Filter (EMA)";
input bool     UseEMAFilter = false;        // Use EMA Filter?
input int      EMALength = 150;             // EMA Length
input bool     ShowEMALine = true;          // Show EMA Line on Chart
input color    EMAColor = clrYellow;        // EMA Line Color

input group "Time Settings";
input string   TradingSession = "0000-2359"; // Trading Session (24/7 by default)
input bool     ForceCloseDay = false;       // Force Close on Friday Night?
input int      ForceCloseHour = 22;         // Force Close Hour (Friday)
input int      ForceCloseMinute = 0;        // Force Close Minute
input bool     HolidayFilter = false;       // Skip Winter Holidays?

input group "Risk Management";
input double   RiskPerTrade = 0.8;          // Risk % Per Trade
input double   RewardRatio = 1.9;           // Risk : Reward Ratio

input group "Squeeze Detection";
input bool     UseSqueezeDetection = true;  // Detect Squeeze
input bool     ShowStandardBoxes = true;    // Show Standard Boxes
input int      StdDevLength = 20;           // StdDev Length
input double   SqueezeThreshold = 0.6;      // Squeeze Threshold

input group "Trading Settings";
input double   LotSize = 0.01;              // Fixed lot size (or 0 for auto)
input int      MagicNumber = 123456;        // Expert ID
input int      Slippage = 1;                // Slippage in points

input group "📊 Dashboard Settings";
input bool     ShowDashboard = true;        // Show Trading Dashboard
input int      DashboardX = 10;             // Dashboard X Position
input int      DashboardY = 20;             // Dashboard Y Position
input color    DashboardBgColor = clrBlack; // Dashboard Background Color
input color    DashboardTextColor = clrWhite; // Dashboard Text Color

//+------------------------------------------------------------------+
//| Global Variables                                                 |
//+------------------------------------------------------------------+
// Trading variables
int      lastExitBar = -999;
bool     boxReady = false;
double   zHigh = 0;
double   zLow = 0;
int      boxStartBar = 0;
string   boxLabel = "";
string   currentBoxName = "";

bool     inTrade = false;
double   tEntry = 0;
double   tSL = 0;
double   tTP = 0;
int      tDir = 0;
double   tQty = 0;
int      tStartBar = 0;

// Time and session variables
MqlDateTime g_currentTime;
double     todayProfit = 0;
double     totalProfit = 0;
int        todayTrades = 0;
int        totalTrades = 0;
int        winningTrades = 0;
int        losingTrades = 0;
double     winRate = 0;

// Indicator handles
int ema_handle = INVALID_HANDLE;
int atr_handle = INVALID_HANDLE;
int std_handle = INVALID_HANDLE;

// Candle close variables
datetime lastCandleTime = 0;
bool     candleCloseProcessed = false;

// Dashboard state
bool     autoTradingEnabled = true;
string   customStartTime = "0000";
string   customEndTime = "2359";

// Dashboard object names
#define DASH_BG          "ORION_DASH_BG"
#define DASH_TITLE       "ORION_DASH_TITLE"
#define DASH_TIME_LABEL  "ORION_DASH_TIME_LABEL"
#define DASH_START_EDIT  "ORION_DASH_START_EDIT"
#define DASH_END_EDIT    "ORION_DASH_END_EDIT"
#define DASH_APPLY_BTN   "ORION_DASH_APPLY_BTN"
#define DASH_PROFIT      "ORION_DASH_PROFIT"
#define DASH_TRADES      "ORION_DASH_TRADES"
#define DASH_WINRATE     "ORION_DASH_WINRATE"
#define DASH_TOTALPROFIT "ORION_DASH_TOTALPROFIT"
#define DASH_SESSION     "ORION_DASH_SESSION"
#define DASH_STATUS      "ORION_DASH_STATUS"
#define DASH_CLOSE_BTN   "ORION_DASH_CLOSE_BTN"
#define DASH_TOGGLE_BTN  "ORION_DASH_TOGGLE_BTN"
#define DASH_RESET_BTN   "ORION_DASH_RESET_BTN"

//+------------------------------------------------------------------+
//| Convert Color to String (Custom name to avoid conflict)          |
//+------------------------------------------------------------------+
string ColorToName(color clr)
{
   if(clr == clrBlack) return "Black";
   if(clr == clrDodgerBlue) return "Dodger Blue";
   if(clr == clrOrangeRed) return "Orange Red";
   if(clr == clrDimGray) return "Dim Gray";
   if(clr == clrGray) return "Gray";
   if(clr == clrYellow) return "Yellow";
   if(clr == clrLime) return "Lime";
   if(clr == clrRed) return "Red";
   if(clr == clrSteelBlue) return "Steel Blue";
   if(clr == clrRoyalBlue) return "Royal Blue";
   if(clr == clrCrimson) return "Crimson";
   if(clr == clrPurple) return "Purple";
   if(clr == clrGold) return "Gold";
   if(clr == clrBlue) return "Blue";
   if(clr == clrGreen) return "Green";
   if(clr == clrDarkBlue) return "White";
   if(clr == clrAqua) return "Aqua";
   if(clr == clrSkyBlue) return "Sky Blue";
   if(clr == clrViolet) return "Violet";
   return "Custom";
}

//+------------------------------------------------------------------+
//| Apply Beautiful Chart Colors                                     |
//+------------------------------------------------------------------+
void ApplyChartColors()
{
   if(!AutoColorChart) return;
   
   long chart_id = ChartID();
   
   // CHART BACKGROUND - NAVY (NOT OVERRIDDEN)
   ChartSetInteger(chart_id, CHART_COLOR_BACKGROUND, 0, clrNavy);
   
   // CANDLE COLORS
   ChartSetInteger(chart_id, CHART_COLOR_CANDLE_BULL, 0, BullCandleColor);
   ChartSetInteger(chart_id, CHART_COLOR_CANDLE_BEAR, 0, BearCandleColor);
   ChartSetInteger(chart_id, CHART_COLOR_CHART_UP, 0, BullCandleColor);
   ChartSetInteger(chart_id, CHART_COLOR_CHART_DOWN, 0, BearCandleColor);
   
   // WICK COLORS
   ChartSetInteger(chart_id, CHART_COLOR_CANDLE_BULL, 0, WickColor);
   ChartSetInteger(chart_id, CHART_COLOR_CANDLE_BEAR, 0, WickColor);
   
   // BORDER COLORS
   ChartSetInteger(chart_id, CHART_COLOR_CANDLE_BULL, 0, BorderColor);
   ChartSetInteger(chart_id, CHART_COLOR_CANDLE_BEAR, 0, BorderColor);
   
   // GRID - REMOVED (SHOW GRID = FALSE)
   ChartSetInteger(chart_id, CHART_SHOW_GRID, 0, false);
   
   // AXIS COLORS
   ChartSetInteger(chart_id, CHART_COLOR_BACKGROUND, 0, AxisColor);
   ChartSetInteger(chart_id, CHART_COLOR_CHART_LINE, 0, AxisColor);
   ChartSetInteger(chart_id, CHART_COLOR_FOREGROUND, 0, DashboardTextColor);
   
   // VOLUME COLORS
   ChartSetInteger(chart_id, CHART_COLOR_VOLUME, 0, clrSteelBlue);
   
   // CHART MODE - CANDLES
   ChartSetInteger(chart_id, CHART_MODE, 0, CHART_CANDLES);
   ChartSetInteger(chart_id, CHART_SHOW_OHLC, 0, true);
   ChartSetInteger(chart_id, CHART_SHOW_BID_LINE, 0, true);
   ChartSetInteger(chart_id, CHART_COLOR_BID, 0, clrLime);
   ChartSetInteger(chart_id, CHART_COLOR_ASK, 0, clrRed);
   ChartSetInteger(chart_id, CHART_SHOW_ASK_LINE, 0, true);
   
   // SCALE SETTINGS
   ChartSetInteger(chart_id, CHART_SCALE, 0, 6);
   ChartSetInteger(chart_id, CHART_SHIFT, 0, true);
   ChartSetInteger(chart_id, CHART_AUTOSCROLL, 0, true);
   
   ChartRedraw(chart_id);
   
   Print("🎨 Beautiful chart colors applied successfully!");
   Print("   Bull candles: ", ColorToName(BullCandleColor));
   Print("   Bear candles: ", ColorToName(BearCandleColor));
   Print("   Background: Navy");
   Print("   Grid: Disabled");
}

//+------------------------------------------------------------------+
//| Expert initialization function                                   |
//+------------------------------------------------------------------+
int OnInit()
{




   // Check trial expiration
   if(TimeGMT() > D'2026.02.14 00:00:00')
   {
      Alert("FREE TRIAL EXPIRED DM @Subzero_911 on Telegram");
      return(INIT_FAILED);
   }
   
   // Create watermark objects
   ObjectCreate(0, "GFF", OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, "GFF", OBJPROP_CORNER, CORNER_LEFT_LOWER);
   ObjectSetInteger(0, "GFF", OBJPROP_ANCHOR, ANCHOR_LEFT_LOWER);
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
   ObjectSetInteger(0, "RORAl", OBJPROP_CORNER, CORNER_LEFT_LOWER);
   ObjectSetInteger(0, "RORAl", OBJPROP_ANCHOR, ANCHOR_LEFT_LOWER);
   ObjectSetInteger(0, "RORAl", OBJPROP_XDISTANCE, 55);
   ObjectSetInteger(0, "RORAl", OBJPROP_YDISTANCE, 85);
   ObjectSetString(0, "RORAl", OBJPROP_TEXT, "DM US TO CONVERT INDICATOR TO ROBOT");
   ObjectSetInteger(0, "RORAl", OBJPROP_FONTSIZE, 11);
   ObjectSetString(0, "RORAl", OBJPROP_FONT, "Impact");
   ObjectSetInteger(0, "RORAl", OBJPROP_COLOR, clrWhite);
   ObjectSetInteger(0, "RORAl", OBJPROP_BACK, false);

   ObjectCreate(0, "RORAlL", OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, "RORAlL", OBJPROP_CORNER, CORNER_LEFT_LOWER);
   ObjectSetInteger(0, "RORAlL", OBJPROP_ANCHOR, ANCHOR_LEFT_LOWER);
   ObjectSetInteger(0, "RORAlL", OBJPROP_XDISTANCE, 55);
   ObjectSetInteger(0, "RORAlL", OBJPROP_YDISTANCE, 115);
   ObjectSetString(0, "RORAlL", OBJPROP_TEXT, "SOURCE CODE OF THIS FILE + EA AVAILABLE");
   ObjectSetInteger(0, "RORAlL", OBJPROP_FONTSIZE, 15);
   ObjectSetString(0, "RORAlL", OBJPROP_FONT, "Impact");
   ObjectSetInteger(0, "RORAlL", OBJPROP_COLOR, clrYellow);
   ObjectSetInteger(0, "RORAlL", OBJPROP_BACK, false);

   ObjectCreate(0, "RORAlLLll", OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, "RORAlLLll", OBJPROP_CORNER, CORNER_LEFT_LOWER);
   ObjectSetInteger(0, "RORAlLLll", OBJPROP_ANCHOR, ANCHOR_LEFT_LOWER);
   ObjectSetInteger(0, "RORAlLLll", OBJPROP_XDISTANCE, 55);
   ObjectSetInteger(0, "RORAlLLll", OBJPROP_YDISTANCE, 205);
   ObjectSetString(0, "RORAlLLll", OBJPROP_TEXT, "THIS  ROBOT WILL EXPIRE (2026.02.14////00:00:00)");
   ObjectSetInteger(0, "RORAlLLll", OBJPROP_FONTSIZE, 11);
   ObjectSetString(0, "RORAlLLll", OBJPROP_FONT, "Impact");
   ObjectSetInteger(0, "RORAlLLll", OBJPROP_COLOR, clrYellow);
   ObjectSetInteger(0, "RORAlLLll", OBJPROP_BACK, false);

   ObjectCreate(0, "ROR", OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, "ROR", OBJPROP_CORNER, CORNER_LEFT_LOWER);
   ObjectSetInteger(0, "ROR", OBJPROP_ANCHOR, ANCHOR_LEFT_LOWER);
   ObjectSetInteger(0, "ROR", OBJPROP_XDISTANCE, 13);
   ObjectSetInteger(0, "ROR", OBJPROP_YDISTANCE, 13);
   ObjectSetString(0, "ROR", OBJPROP_TEXT, "THIS INDICATOR IS 100% DEVELOPED BY @Subzero_911");
   ObjectSetInteger(0, "ROR", OBJPROP_FONTSIZE, 20);
   ObjectSetString(0, "ROR", OBJPROP_FONT, "Impact");
   ObjectSetInteger(0, "ROR", OBJPROP_COLOR, clrAqua);
   ObjectSetInteger(0, "ROR", OBJPROP_BACK, false);

















   // 🎨 APPLY BEAUTIFUL CHART COLORS IMMEDIATELY
   ApplyChartColors();
   
   // Initialize indicator handles
   if(UseEMAFilter || ShowEMALine)
   {
      ema_handle = iMA(_Symbol, _Period, EMALength, 0, MODE_EMA, PRICE_CLOSE);
   }
   
   atr_handle = iATR(_Symbol, _Period, 14);
   
   if(UseSqueezeDetection)
   {
      std_handle = iStdDev(_Symbol, _Period, StdDevLength, 0, MODE_SMA, PRICE_CLOSE);
   }
   
   if(ema_handle == INVALID_HANDLE && (UseEMAFilter || ShowEMALine))
   {
      Print("Failed to create EMA indicator");
      return INIT_FAILED;
   }
   
   if(atr_handle == INVALID_HANDLE)
   {
      Print("Failed to create ATR indicator");
      return INIT_FAILED;
   }
   
   lastCandleTime = iTime(_Symbol, _Period, 0);
   candleCloseProcessed = false;
   
   // Set default 24/7 trading
   customStartTime = "0000";
   customEndTime = "2359";
   
   // Create dashboard
   if(ShowDashboard)
   {
      CreateDashboard();
   }
   
   // Load saved statistics
   LoadStatistics();
   
   Print("╔════════════════════════════════════════════════╗");
   Print("║     ORION 24/7 TRADING - ACTIVATED            ║");
   Print("║          CREATED BY @Subzero_911              ║");
   Print("╠════════════════════════════════════════════════╣");
   Print("║  🎨 Chart Colors: APPLIED (Navy Background)   ║");
   Print("║  📊 Dashboard: ACTIVE                         ║");
   Print("║  ⏰ Trading Mode: 24/7 EVERY DAY              ║");
   Print("║  📈 Strategy: READY                           ║");
   Print("╚════════════════════════════════════════════════╝");
   
   return INIT_SUCCEEDED;
}

//+------------------------------------------------------------------+
//| Expert deinitialization function                                 |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   // Save statistics
   SaveStatistics();
   
   // Clean up graphical objects
   DeleteDashboard();
   ObjectsDeleteAll(0, "ORION_");
   
   // Release indicator handles
   if(ema_handle != INVALID_HANDLE) IndicatorRelease(ema_handle);
   if(atr_handle != INVALID_HANDLE) IndicatorRelease(atr_handle);
   if(std_handle != INVALID_HANDLE) IndicatorRelease(std_handle);
   
   Print("ORION EA removed - Thank you for trading!");
}

//+------------------------------------------------------------------+
//| Chart Event function                                             |
//+------------------------------------------------------------------+
void OnChartEvent(const int id, const long &lparam, const double &dparam, const string &sparam)
{
   // Handle button clicks
   if(id == CHARTEVENT_OBJECT_CLICK)
   {
      // Apply Time Button
      if(sparam == DASH_APPLY_BTN)
      {
         ObjectSetInteger(0, sparam, OBJPROP_STATE, 0);
         ApplyCustomTime();
      }
      
      // Close All Button
      if(sparam == DASH_CLOSE_BTN)
      {
         ObjectSetInteger(0, sparam, OBJPROP_STATE, 0);
         CloseAllTrades("Manual Close");
      }
      
      // Reset Stats Button
      if(sparam == DASH_RESET_BTN)
      {
         ObjectSetInteger(0, sparam, OBJPROP_STATE, 0);
         ResetStatistics();
      }
      
      // Toggle Auto Trading Button
      if(sparam == DASH_TOGGLE_BTN)
      {
         ObjectSetInteger(0, sparam, OBJPROP_STATE, 0);
         autoTradingEnabled = !autoTradingEnabled;
         UpdateDashboard();
      }
   }
   
   // Handle edit box changes
   if(id == CHARTEVENT_OBJECT_ENDEDIT)
   {
      if(sparam == DASH_START_EDIT)
      {
         customStartTime = ObjectGetString(0, sparam, OBJPROP_TEXT);
      }
      if(sparam == DASH_END_EDIT)
      {
         customEndTime = ObjectGetString(0, sparam, OBJPROP_TEXT);
      }
   }
}

//+------------------------------------------------------------------+
//| Create Dashboard using basic MQL5 objects                        |
//+------------------------------------------------------------------+
void CreateDashboard()
{
   int x = DashboardX;
   int y = DashboardY;
   int width = 340;
   int height = 290;
   
   // Delete existing objects first
   DeleteDashboard();
   
   // Background rectangle
   ObjectCreate(0, DASH_BG, OBJ_RECTANGLE_LABEL, 0, 0, 0);
   ObjectSetInteger(0, DASH_BG, OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, DASH_BG, OBJPROP_YDISTANCE, y);
   ObjectSetInteger(0, DASH_BG, OBJPROP_XSIZE, width);
   ObjectSetInteger(0, DASH_BG, OBJPROP_YSIZE, height);
   ObjectSetInteger(0, DASH_BG, OBJPROP_BGCOLOR, DashboardBgColor);
   ObjectSetInteger(0, DASH_BG, OBJPROP_BORDER_COLOR, clrDodgerBlue);
   ObjectSetInteger(0, DASH_BG, OBJPROP_BORDER_TYPE, BORDER_FLAT);
   ObjectSetInteger(0, DASH_BG, OBJPROP_BACK, true);
   ObjectSetInteger(0, DASH_BG, OBJPROP_FILL, true);
   ObjectSetInteger(0, DASH_BG, OBJPROP_WIDTH, 2);
   
   // Title
   ObjectCreate(0, DASH_TITLE, OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, DASH_TITLE, OBJPROP_XDISTANCE, x + 10);
   ObjectSetInteger(0, DASH_TITLE, OBJPROP_YDISTANCE, y + 5);
   ObjectSetString(0, DASH_TITLE, OBJPROP_TEXT, "🚀 ORION EA BY @Subzero_911");
   ObjectSetInteger(0, DASH_TITLE, OBJPROP_COLOR, clrGold);
   ObjectSetString(0, DASH_TITLE, OBJPROP_FONT, "Arial");
   ObjectSetInteger(0, DASH_TITLE, OBJPROP_FONTSIZE, 11);
   ObjectSetInteger(0, DASH_TITLE, OBJPROP_FILL, true);
   
   // Time Label
   ObjectCreate(0, DASH_TIME_LABEL, OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, DASH_TIME_LABEL, OBJPROP_XDISTANCE, x + 10);
   ObjectSetInteger(0, DASH_TIME_LABEL, OBJPROP_YDISTANCE, y + 35);
   ObjectSetString(0, DASH_TIME_LABEL, OBJPROP_TEXT, "⏰ Session:");
   ObjectSetInteger(0, DASH_TIME_LABEL, OBJPROP_COLOR, clrAqua);
   ObjectSetString(0, DASH_TIME_LABEL, OBJPROP_FONT, "Arial");
   ObjectSetInteger(0, DASH_TIME_LABEL, OBJPROP_FONTSIZE, 10);
   
   // Start time edit box
   ObjectCreate(0, DASH_START_EDIT, OBJ_EDIT, 0, 0, 0);
   ObjectSetInteger(0, DASH_START_EDIT, OBJPROP_XDISTANCE, x + 100);
   ObjectSetInteger(0, DASH_START_EDIT, OBJPROP_YDISTANCE, y + 32);
   ObjectSetInteger(0, DASH_START_EDIT, OBJPROP_XSIZE, 50);
   ObjectSetInteger(0, DASH_START_EDIT, OBJPROP_YSIZE, 20);
   ObjectSetString(0, DASH_START_EDIT, OBJPROP_TEXT, customStartTime);
   ObjectSetInteger(0, DASH_START_EDIT, OBJPROP_COLOR, clrBlack);
   ObjectSetInteger(0, DASH_START_EDIT, OBJPROP_BGCOLOR, clrWhite);
   ObjectSetInteger(0, DASH_START_EDIT, OBJPROP_BORDER_COLOR, clrDodgerBlue);
   ObjectSetInteger(0, DASH_START_EDIT, OBJPROP_ALIGN, ALIGN_CENTER);
   ObjectSetInteger(0, DASH_START_EDIT, OBJPROP_FONTSIZE, 10);
   ObjectSetInteger(0, DASH_START_EDIT, OBJPROP_READONLY, false);
   
   // Separator
   ObjectCreate(0, "ORION_DASH_SEP", OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, "ORION_DASH_SEP", OBJPROP_XDISTANCE, x + 155);
   ObjectSetInteger(0, "ORION_DASH_SEP", OBJPROP_YDISTANCE, y + 35);
   ObjectSetString(0, "ORION_DASH_SEP", OBJPROP_TEXT, "➡️");
   ObjectSetInteger(0, "ORION_DASH_SEP", OBJPROP_COLOR, clrYellow);
   ObjectSetInteger(0, "ORION_DASH_SEP", OBJPROP_FONTSIZE, 12);
   
   // End time edit box
   ObjectCreate(0, DASH_END_EDIT, OBJ_EDIT, 0, 0, 0);
   ObjectSetInteger(0, DASH_END_EDIT, OBJPROP_XDISTANCE, x + 170);
   ObjectSetInteger(0, DASH_END_EDIT, OBJPROP_YDISTANCE, y + 32);
   ObjectSetInteger(0, DASH_END_EDIT, OBJPROP_XSIZE, 50);
   ObjectSetInteger(0, DASH_END_EDIT, OBJPROP_YSIZE, 20);
   ObjectSetString(0, DASH_END_EDIT, OBJPROP_TEXT, customEndTime);
   ObjectSetInteger(0, DASH_END_EDIT, OBJPROP_COLOR, clrBlack);
   ObjectSetInteger(0, DASH_END_EDIT, OBJPROP_BGCOLOR, clrWhite);
   ObjectSetInteger(0, DASH_END_EDIT, OBJPROP_BORDER_COLOR, clrOrangeRed);
   ObjectSetInteger(0, DASH_END_EDIT, OBJPROP_ALIGN, ALIGN_CENTER);
   ObjectSetInteger(0, DASH_END_EDIT, OBJPROP_FONTSIZE, 10);
   ObjectSetInteger(0, DASH_END_EDIT, OBJPROP_READONLY, false);
   
   // Apply button
   ObjectCreate(0, DASH_APPLY_BTN, OBJ_BUTTON, 0, 0, 0);
   ObjectSetInteger(0, DASH_APPLY_BTN, OBJPROP_XDISTANCE, x + 235);
   ObjectSetInteger(0, DASH_APPLY_BTN, OBJPROP_YDISTANCE, y + 32);
   ObjectSetInteger(0, DASH_APPLY_BTN, OBJPROP_XSIZE, 70);
   ObjectSetInteger(0, DASH_APPLY_BTN, OBJPROP_YSIZE, 20);
   ObjectSetString(0, DASH_APPLY_BTN, OBJPROP_TEXT, "✅ Apply");
   ObjectSetInteger(0, DASH_APPLY_BTN, OBJPROP_COLOR, clrWhite);
   ObjectSetInteger(0, DASH_APPLY_BTN, OBJPROP_BGCOLOR, clrBlue);
   ObjectSetInteger(0, DASH_APPLY_BTN, OBJPROP_BORDER_COLOR, clrWhite);
   ObjectSetInteger(0, DASH_APPLY_BTN, OBJPROP_FONTSIZE, 9);
   ObjectSetInteger(0, DASH_APPLY_BTN, OBJPROP_STATE, 0);
   
   // Profit Label
   ObjectCreate(0, DASH_PROFIT, OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, DASH_PROFIT, OBJPROP_XDISTANCE, x + 10);
   ObjectSetInteger(0, DASH_PROFIT, OBJPROP_YDISTANCE, y + 70);
   ObjectSetString(0, DASH_PROFIT, OBJPROP_TEXT, "💰 Today Profit: $0.00");
   ObjectSetInteger(0, DASH_PROFIT, OBJPROP_COLOR, clrLime);
   ObjectSetString(0, DASH_PROFIT, OBJPROP_FONT, "Arial");
   ObjectSetInteger(0, DASH_PROFIT, OBJPROP_FONTSIZE, 11);
   ObjectSetInteger(0, DASH_PROFIT, OBJPROP_FILL, true);
   
   // Trades Label
   ObjectCreate(0, DASH_TRADES, OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, DASH_TRADES, OBJPROP_XDISTANCE, x + 10);
   ObjectSetInteger(0, DASH_TRADES, OBJPROP_YDISTANCE, y + 95);
   ObjectSetString(0, DASH_TRADES, OBJPROP_TEXT, "📊 Today Trades: 0");
   ObjectSetInteger(0, DASH_TRADES, OBJPROP_COLOR, clrSkyBlue);
   ObjectSetString(0, DASH_TRADES, OBJPROP_FONT, "Arial");
   ObjectSetInteger(0, DASH_TRADES, OBJPROP_FONTSIZE, 10);
   
   // Win Rate Label
   ObjectCreate(0, DASH_WINRATE, OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, DASH_WINRATE, OBJPROP_XDISTANCE, x + 10);
   ObjectSetInteger(0, DASH_WINRATE, OBJPROP_YDISTANCE, y + 120);
   ObjectSetString(0, DASH_WINRATE, OBJPROP_TEXT, "🎯 Win Rate: 0%");
   ObjectSetInteger(0, DASH_WINRATE, OBJPROP_COLOR, clrGold);
   ObjectSetString(0, DASH_WINRATE, OBJPROP_FONT, "Arial");
   ObjectSetInteger(0, DASH_WINRATE, OBJPROP_FONTSIZE, 10);
   
   // Total Profit Label
   ObjectCreate(0, DASH_TOTALPROFIT, OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, DASH_TOTALPROFIT, OBJPROP_XDISTANCE, x + 10);
   ObjectSetInteger(0, DASH_TOTALPROFIT, OBJPROP_YDISTANCE, y + 145);
   ObjectSetString(0, DASH_TOTALPROFIT, OBJPROP_TEXT, "💎 Total Profit: $0.00");
   ObjectSetInteger(0, DASH_TOTALPROFIT, OBJPROP_COLOR, clrViolet);
   ObjectSetString(0, DASH_TOTALPROFIT, OBJPROP_FONT, "Arial");
   ObjectSetInteger(0, DASH_TOTALPROFIT, OBJPROP_FONTSIZE, 10);
   
   // Session Status Label - ALWAYS ACTIVE
   ObjectCreate(0, DASH_SESSION, OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, DASH_SESSION, OBJPROP_XDISTANCE, x + 10);
   ObjectSetInteger(0, DASH_SESSION, OBJPROP_YDISTANCE, y + 170);
   ObjectSetString(0, DASH_SESSION, OBJPROP_TEXT, "⚡ Session: 24/7 ACTIVE");
   ObjectSetInteger(0, DASH_SESSION, OBJPROP_COLOR, clrLime);
   ObjectSetString(0, DASH_SESSION, OBJPROP_FONT, "Arial");
   ObjectSetInteger(0, DASH_SESSION, OBJPROP_FONTSIZE, 10);
   ObjectSetInteger(0, DASH_SESSION, OBJPROP_FILL, true);
   
   // Trading Status Label
   ObjectCreate(0, DASH_STATUS, OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, DASH_STATUS, OBJPROP_XDISTANCE, x + 10);
   ObjectSetInteger(0, DASH_STATUS, OBJPROP_YDISTANCE, y + 195);
   ObjectSetString(0, DASH_STATUS, OBJPROP_TEXT, "🤖 Auto Trading: ON");
   ObjectSetInteger(0, DASH_STATUS, OBJPROP_COLOR, clrLime);
   ObjectSetString(0, DASH_STATUS, OBJPROP_FONT, "Arial");
   ObjectSetInteger(0, DASH_STATUS, OBJPROP_FONTSIZE, 10);
   ObjectSetInteger(0, DASH_STATUS, OBJPROP_FILL, true);
   
   // Close All Button
   ObjectCreate(0, DASH_CLOSE_BTN, OBJ_BUTTON, 0, 0, 0);
   ObjectSetInteger(0, DASH_CLOSE_BTN, OBJPROP_XDISTANCE, x + 10);
   ObjectSetInteger(0, DASH_CLOSE_BTN, OBJPROP_YDISTANCE, y + 240);
   ObjectSetInteger(0, DASH_CLOSE_BTN, OBJPROP_XSIZE, 90);
   ObjectSetInteger(0, DASH_CLOSE_BTN, OBJPROP_YSIZE, 30);
   ObjectSetString(0, DASH_CLOSE_BTN, OBJPROP_TEXT, "🔴 Close All");
   ObjectSetInteger(0, DASH_CLOSE_BTN, OBJPROP_COLOR, clrWhite);
   ObjectSetInteger(0, DASH_CLOSE_BTN, OBJPROP_BGCOLOR, clrRed);
   ObjectSetInteger(0, DASH_CLOSE_BTN, OBJPROP_BORDER_COLOR, clrWhite);
   ObjectSetInteger(0, DASH_CLOSE_BTN, OBJPROP_FONTSIZE, 10);
   ObjectSetInteger(0, DASH_CLOSE_BTN, OBJPROP_FILL, true);
   ObjectSetInteger(0, DASH_CLOSE_BTN, OBJPROP_STATE, 0);
   
   // Toggle Auto Button
   ObjectCreate(0, DASH_TOGGLE_BTN, OBJ_BUTTON, 0, 0, 0);
   ObjectSetInteger(0, DASH_TOGGLE_BTN, OBJPROP_XDISTANCE, x + 110);
   ObjectSetInteger(0, DASH_TOGGLE_BTN, OBJPROP_YDISTANCE, y + 240);
   ObjectSetInteger(0, DASH_TOGGLE_BTN, OBJPROP_XSIZE, 90);
   ObjectSetInteger(0, DASH_TOGGLE_BTN, OBJPROP_YSIZE, 30);
   ObjectSetString(0, DASH_TOGGLE_BTN, OBJPROP_TEXT, "🟢 Auto: ON");
   ObjectSetInteger(0, DASH_TOGGLE_BTN, OBJPROP_COLOR, clrWhite);
   ObjectSetInteger(0, DASH_TOGGLE_BTN, OBJPROP_BGCOLOR, clrGreen);
   ObjectSetInteger(0, DASH_TOGGLE_BTN, OBJPROP_BORDER_COLOR, clrWhite);
   ObjectSetInteger(0, DASH_TOGGLE_BTN, OBJPROP_FONTSIZE, 10);
   ObjectSetInteger(0, DASH_TOGGLE_BTN, OBJPROP_FILL, true);
   ObjectSetInteger(0, DASH_TOGGLE_BTN, OBJPROP_STATE, 0);
   
   // Reset Stats Button
   ObjectCreate(0, DASH_RESET_BTN, OBJ_BUTTON, 0, 0, 0);
   ObjectSetInteger(0, DASH_RESET_BTN, OBJPROP_XDISTANCE, x + 210);
   ObjectSetInteger(0, DASH_RESET_BTN, OBJPROP_YDISTANCE, y + 240);
   ObjectSetInteger(0, DASH_RESET_BTN, OBJPROP_XSIZE, 90);
   ObjectSetInteger(0, DASH_RESET_BTN, OBJPROP_YSIZE, 30);
   ObjectSetString(0, DASH_RESET_BTN, OBJPROP_TEXT, "🔄 Reset");
   ObjectSetInteger(0, DASH_RESET_BTN, OBJPROP_COLOR, clrWhite);
   ObjectSetInteger(0, DASH_RESET_BTN, OBJPROP_BGCOLOR, clrNavy);
   ObjectSetInteger(0, DASH_RESET_BTN, OBJPROP_BORDER_COLOR, clrWhite);
   ObjectSetInteger(0, DASH_RESET_BTN, OBJPROP_FONTSIZE, 10);
   ObjectSetInteger(0, DASH_RESET_BTN, OBJPROP_FILL, true);
   ObjectSetInteger(0, DASH_RESET_BTN, OBJPROP_STATE, 0);
   
   ChartRedraw(0);
}

//+------------------------------------------------------------------+
//| Delete Dashboard                                                 |
//+------------------------------------------------------------------+
void DeleteDashboard()
{
   ObjectDelete(0, DASH_BG);
   ObjectDelete(0, DASH_TITLE);
   ObjectDelete(0, DASH_TIME_LABEL);
   ObjectDelete(0, DASH_START_EDIT);
   ObjectDelete(0, DASH_END_EDIT);
   ObjectDelete(0, DASH_APPLY_BTN);
   ObjectDelete(0, DASH_PROFIT);
   ObjectDelete(0, DASH_TRADES);
   ObjectDelete(0, DASH_WINRATE);
   ObjectDelete(0, DASH_TOTALPROFIT);
   ObjectDelete(0, DASH_SESSION);
   ObjectDelete(0, DASH_STATUS);
   ObjectDelete(0, DASH_CLOSE_BTN);
   ObjectDelete(0, DASH_TOGGLE_BTN);
   ObjectDelete(0, DASH_RESET_BTN);
   ObjectDelete(0, "ORION_DASH_SEP");
}

//+------------------------------------------------------------------+
//| Update Dashboard                                                 |
//+------------------------------------------------------------------+
void UpdateDashboard()
{
   if(!ShowDashboard) return;
   
   // Update profit
   string profitText = "💰 Today Profit: $" + DoubleToString(todayProfit, 2);
   ObjectSetString(0, DASH_PROFIT, OBJPROP_TEXT, profitText);
   
   // Color profit based on value
   color profitColor = (todayProfit >= 0) ? clrLime : clrRed;
   ObjectSetInteger(0, DASH_PROFIT, OBJPROP_COLOR, profitColor);
   
   // Update trades
   string tradesText = "📊 Today Trades: " + IntegerToString(todayTrades);
   ObjectSetString(0, DASH_TRADES, OBJPROP_TEXT, tradesText);
   
   // Update win rate
   winRate = (totalTrades > 0) ? (double)winningTrades / totalTrades * 100 : 0;
   string winRateText = "🎯 Win Rate: " + DoubleToString(winRate, 1) + "%";
   ObjectSetString(0, DASH_WINRATE, OBJPROP_TEXT, winRateText);
   
   // Update total profit
   string totalProfitText = "💎 Total Profit: $" + DoubleToString(totalProfit, 2);
   ObjectSetString(0, DASH_TOTALPROFIT, OBJPROP_TEXT, totalProfitText);
   color totalProfitColor = (totalProfit >= 0) ? clrViolet : clrRed;
   ObjectSetInteger(0, DASH_TOTALPROFIT, OBJPROP_COLOR, totalProfitColor);
   
   // Session is ALWAYS active - 24/7
   ObjectSetString(0, DASH_SESSION, OBJPROP_TEXT, "⚡ Session: 24/7 ACTIVE");
   ObjectSetInteger(0, DASH_SESSION, OBJPROP_COLOR, clrLime);
   
   // Update trading status
   string statusText = "🤖 Auto Trading: " + (autoTradingEnabled ? "ON" : "OFF");
   ObjectSetString(0, DASH_STATUS, OBJPROP_TEXT, statusText);
   color statusColor = autoTradingEnabled ? clrLime : clrRed;
   ObjectSetInteger(0, DASH_STATUS, OBJPROP_COLOR, statusColor);
   
   // Update toggle button
   string toggleText = (autoTradingEnabled ? "🟢 Auto: ON" : "🔴 Auto: OFF");
   ObjectSetString(0, DASH_TOGGLE_BTN, OBJPROP_TEXT, toggleText);
   color btnColor = autoTradingEnabled ? clrGreen : clrRed;
   ObjectSetInteger(0, DASH_TOGGLE_BTN, OBJPROP_BGCOLOR, btnColor);
   
   ChartRedraw(0);
}

//+------------------------------------------------------------------+
//| Apply Custom Time                                                |
//+------------------------------------------------------------------+
void ApplyCustomTime()
{
   Print("Trading session updated to: ", customStartTime, "-", customEndTime);
   UpdateDashboard();
}

//+------------------------------------------------------------------+
//| Update Trade Statistics                                          |
//+------------------------------------------------------------------+
void UpdateTradeStats(double profit, bool isWin)
{
   todayProfit += profit;
   totalProfit += profit;
   todayTrades++;
   totalTrades++;
   
   if(isWin)
   {
      winningTrades++;
   }
   else
   {
      losingTrades++;
   }
   
   winRate = (totalTrades > 0) ? (double)winningTrades / totalTrades * 100 : 0;
   UpdateDashboard();
}

//+------------------------------------------------------------------+
//| Reset Statistics                                                 |
//+------------------------------------------------------------------+
void ResetStatistics()
{
   todayProfit = 0;
   totalProfit = 0;
   todayTrades = 0;
   totalTrades = 0;
   winningTrades = 0;
   losingTrades = 0;
   winRate = 0;
   
   UpdateDashboard();
   Print("Statistics reset successfully");
}

//+------------------------------------------------------------------+
//| Save Statistics to Global Variables                              |
//+------------------------------------------------------------------+
void SaveStatistics()
{
   GlobalVariableSet("ORION_TotalProfit", totalProfit);
   GlobalVariableSet("ORION_TotalTrades", totalTrades);
   GlobalVariableSet("ORION_WinningTrades", winningTrades);
   GlobalVariableSet("ORION_LosingTrades", losingTrades);
}

//+------------------------------------------------------------------+
//| Load Statistics from Global Variables                            |
//+------------------------------------------------------------------+
void LoadStatistics()
{
   totalProfit = GlobalVariableGet("ORION_TotalProfit");
   totalTrades = (int)GlobalVariableGet("ORION_TotalTrades");
   winningTrades = (int)GlobalVariableGet("ORION_WinningTrades");
   losingTrades = (int)GlobalVariableGet("ORION_LosingTrades");
   
   // Reset today's stats
   todayProfit = 0;
   todayTrades = 0;
}

//+------------------------------------------------------------------+
//| Check for candle close event                                     |
//+------------------------------------------------------------------+
bool IsCandleCloseEvent()
{
   datetime currentCandleTime = iTime(_Symbol, _Period, 0);
   
   if(currentCandleTime != lastCandleTime)
   {
      lastCandleTime = currentCandleTime;
      candleCloseProcessed = false;
      return true;
   }
   return false;
}

//+------------------------------------------------------------------+
//| Expert tick function                                             |
//+------------------------------------------------------------------+
void OnTick()
{
   // Get current time
   TimeToStruct(TimeCurrent(), g_currentTime);
   
   // Skip if not enough bars
   if(Bars(_Symbol, _Period) < 100)
      return;
   
   // Check for candle close event
   bool candleJustClosed = IsCandleCloseEvent();
   
   // CLOSE TRADES AT CANDLE CLOSE - DISABLED BY DEFAULT
   if(CloseAtCandleClose && candleJustClosed && !candleCloseProcessed && autoTradingEnabled)
   {
      if(inTrade || PositionsTotal() > 0)
      {
         CloseAllTrades("Candle Close");
         candleCloseProcessed = true;
      }
   }
   
   // Calculate indicators
   double emaValue = 0;
   double atrValue = 0;
   double stdDevValue = 0;
   CalculateIndicators(emaValue, atrValue, stdDevValue);
   
   // Check if we can trade - ALWAYS TRUE for 24/7 trading
   if(!autoTradingEnabled)
   {
      UpdateDashboard();
      return;
   }
   
   // Manage existing trade
   if(inTrade)
   {
      ManageTrade();
   }
   
   // Check for entry opportunities
   if(!inTrade && boxReady)
   {
      int currentBar = (int)Bars(_Symbol, _Period);
      if((currentBar - boxStartBar) > MaxBoxDuration)
      {
         DeleteBox();
         boxReady = false;
      }
      else
      {
         CheckForEntry(emaValue);
      }
   }
   
   // Look for new box formation - ALWAYS looking for trades
   if(!inTrade && !boxReady)
   {
      CheckForBoxFormation(atrValue, stdDevValue);
   }
   
   // Update dashboard
   UpdateDashboard();
}

//+------------------------------------------------------------------+
//| Calculate all needed indicators                                  |
//+------------------------------------------------------------------+
void CalculateIndicators(double &emaVal, double &atrVal, double &stdDevVal)
{
   if((UseEMAFilter || ShowEMALine) && ema_handle != INVALID_HANDLE)
   {
      double emaArray[3];
      if(CopyBuffer(ema_handle, 0, 0, 3, emaArray) == 3)
         emaVal = emaArray[0];
   }
   
   if(atr_handle != INVALID_HANDLE)
   {
      double atrArray[3];
      if(CopyBuffer(atr_handle, 0, 0, 3, atrArray) == 3)
         atrVal = atrArray[0];
   }
   
   if(UseSqueezeDetection && std_handle != INVALID_HANDLE)
   {
      double stdDevArray[3];
      if(CopyBuffer(std_handle, 0, 0, 3, stdDevArray) == 3)
         stdDevVal = stdDevArray[0];
   }
}

//+------------------------------------------------------------------+
//| Check for box formation                                          |
//+------------------------------------------------------------------+
void CheckForBoxFormation(double atrValue, double stdDevValue)
{
   // NO RESTRICTIONS - Always check for boxes
   
   int highestIndex = iHighest(_Symbol, _Period, MODE_HIGH, LookbackPeriod, 1);
   int lowestIndex = iLowest(_Symbol, _Period, MODE_LOW, LookbackPeriod, 1);
   
   if(highestIndex == -1 || lowestIndex == -1)
      return;
   
   double highestHigh = iHigh(_Symbol, _Period, highestIndex);
   double lowestLow = iLow(_Symbol, _Period, lowestIndex);
   
   double boxHeight = highestHigh - lowestLow;
   
   if(boxHeight < (atrValue * ATRMultiplier))
   {
      double currentPrice = iClose(_Symbol, _Period, 0);
      double priceLookback = iClose(_Symbol, _Period, LookbackPeriod);
      double slope = MathAbs((currentPrice - priceLookback) / currentPrice) * 1000;
      
      if(slope < SlopeTolerance)
      {
         boxReady = true;
         boxStartBar = (int)Bars(_Symbol, _Period);
         zHigh = highestHigh;
         zLow = lowestLow;
         
         DrawBox(zHigh, zLow, LookbackPeriod, atrValue, stdDevValue);
      }
   }
}

//+------------------------------------------------------------------+
//| Draw box on chart                                                |
//+------------------------------------------------------------------+
void DrawBox(double highPrice, double lowPrice, int lookback, double atrValue, double stdDevValue)
{
   datetime currentTime = TimeCurrent();
   datetime boxStartTime = iTime(_Symbol, _Period, lookback);
   
   currentBoxName = "ORION_Box_" + IntegerToString((int)Bars(_Symbol, _Period));
   ObjectCreate(0, currentBoxName, OBJ_RECTANGLE, 0, boxStartTime, lowPrice, currentTime, highPrice);
   
   color boxColor = clrYellow;
   if(UseSqueezeDetection)
   {
      bool isSqueeze = stdDevValue < (atrValue * SqueezeThreshold);
      if(isSqueeze)
         boxColor = clrGold;
   }
   
   ObjectSetInteger(0, currentBoxName, OBJPROP_COLOR, boxColor);
   ObjectSetInteger(0, currentBoxName, OBJPROP_STYLE, STYLE_SOLID);
   ObjectSetInteger(0, currentBoxName, OBJPROP_WIDTH, 1);
   ObjectSetInteger(0, currentBoxName, OBJPROP_BACK, true);
   ObjectSetInteger(0, currentBoxName, OBJPROP_FILL, true);
   ObjectSetInteger(0, currentBoxName, OBJPROP_BGCOLOR, boxColor);
   
   // Add label
   boxLabel = "ORION_Label_" + IntegerToString((int)Bars(_Symbol, _Period));
   ObjectCreate(0, boxLabel, OBJ_TEXT, 0, boxStartTime + (currentTime - boxStartTime) / 2, highPrice);
   ObjectSetString(0, boxLabel, OBJPROP_TEXT, "📦 BOX");
   ObjectSetInteger(0, boxLabel, OBJPROP_COLOR, boxColor);
   ObjectSetInteger(0, boxLabel, OBJPROP_FONTSIZE, 8);
   ObjectSetInteger(0, boxLabel, OBJPROP_FILL, true);
}

//+------------------------------------------------------------------+
//| Delete box from chart                                            |
//+------------------------------------------------------------------+
void DeleteBox()
{
   ObjectsDeleteAll(0, "ORION_Box_");
   ObjectsDeleteAll(0, "ORION_Label_");
   
   boxLabel = "";
   currentBoxName = "";
}

//+------------------------------------------------------------------+
//| Check for entry signal                                           |
//+------------------------------------------------------------------+
void CheckForEntry(double emaValue)
{
   // NO RESTRICTIONS - Always check for entries
   
   double currentClose = iClose(_Symbol, _Period, 0);
   
   if(currentClose <= zHigh && currentClose >= zLow)
   {
      string boxName = "ORION_Box_" + IntegerToString(boxStartBar);
      if(ObjectFind(0, boxName) >= 0)
      {
         
      }
      return;
   }
   
   bool breakUp = currentClose > zHigh;
   bool breakDown = currentClose < zLow;
   
   // EMA Filter - DISABLED by default
   if(UseEMAFilter)
   {
      if(breakUp && currentClose <= emaValue)
         breakUp = false;
      if(breakDown && currentClose >= emaValue)
         breakDown = false;
   }
   
   if(breakUp || breakDown)
   {
      boxReady = false;
      lastExitBar = (int)Bars(_Symbol, _Period);
      inTrade = true;
      
      string boxName = "ORION_Box_" + IntegerToString(boxStartBar);
      if(ObjectFind(0, boxName) >= 0)
      {
         ObjectSetInteger(0, boxName, OBJPROP_COLOR, clrBlue);
         ObjectSetInteger(0, boxName, OBJPROP_BGCOLOR, clrBlue);
         
         // Update label
         string labelName = "ORION_Label_" + IntegerToString(boxStartBar);
         if(ObjectFind(0, labelName) >= 0)
         {
            ObjectSetString(0, labelName, OBJPROP_TEXT, "⚡ ACTIVE");
         }
      }
      
      tEntry = currentClose;
      tStartBar = (int)Bars(_Symbol, _Period);
      
      if(breakUp)
      {
         tDir = 1;
         tSL = zLow;
         double risk = MathAbs(tEntry - tSL);
         tTP = tEntry + (risk * RewardRatio);
         
         double tickSize = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
         double tickValue = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE);
         double riskAmount = AccountInfoDouble(ACCOUNT_EQUITY) * (RiskPerTrade / 100);
         tQty = (riskAmount / risk) / (tickValue / tickSize);
         
         if(LotSize > 0)
            tQty = LotSize;
         
         double minLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
         double maxLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
         double lotStep = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
         
         if(tQty < minLot) tQty = minLot;
         if(tQty > maxLot) tQty = maxLot;
         tQty = MathFloor(tQty / lotStep) * lotStep;
         
         OpenOrder(ORDER_TYPE_BUY, tQty);
         Print("🟢 LONG entry signal detected - Opening BUY order");
      }
      else if(breakDown)
      {
         tDir = -1;
         tSL = zHigh;
         double risk = MathAbs(tSL - tEntry);
         tTP = tEntry - (risk * RewardRatio);
         
         double tickSize = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
         double tickValue = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE);
         double riskAmount = AccountInfoDouble(ACCOUNT_EQUITY) * (RiskPerTrade / 100);
         tQty = (riskAmount / risk) / (tickValue / tickSize);
         
         if(LotSize > 0)
            tQty = LotSize;
         
         double minLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
         double maxLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
         double lotStep = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
         
         if(tQty < minLot) tQty = minLot;
         if(tQty > maxLot) tQty = maxLot;
         tQty = MathFloor(tQty / lotStep) * lotStep;
         
         OpenOrder(ORDER_TYPE_SELL, tQty);
         Print("🔴 SHORT entry signal detected - Opening SELL order");
      }
      
      DrawTradeLines();
   }
   else
   {
      DeleteBox();
      boxReady = false;
   }
}

//+------------------------------------------------------------------+
//| Open order                                                       |
//+------------------------------------------------------------------+
void OpenOrder(ENUM_ORDER_TYPE orderType, double volume)
{
   MqlTradeRequest request = {};
   MqlTradeResult result = {};
   
   request.action = TRADE_ACTION_DEAL;
   request.symbol = _Symbol;
   request.volume = NormalizeDouble(volume, 2);
   request.type = orderType;
   request.price = SymbolInfoDouble(_Symbol, (orderType == ORDER_TYPE_BUY) ? SYMBOL_ASK : SYMBOL_BID);
   request.sl = NormalizeDouble(tSL, _Digits);
   request.tp = NormalizeDouble(tTP, _Digits);
   request.deviation = Slippage;
   request.magic = MagicNumber;
   request.comment = "ORION Strategy";
   
   bool success = OrderSend(request, result);
   if(!success)
   {
      Print("❌ OrderSend failed: ", GetLastError());
      inTrade = false;
   }
   else
   {
      Print("✅ Order opened successfully: ", result.order, " Type: ", EnumToString(orderType), " Volume: ", volume);
   }
}

//+------------------------------------------------------------------+
//| Manage existing trade                                            |
//+------------------------------------------------------------------+
void ManageTrade()
{
   if(PositionsTotal() == 0)
   {
      inTrade = false;
      RemoveTradeLines();
      return;
   }
   
   UpdateTradeLines();
   
   bool positionFound = false;
   for(int i = 0; i < PositionsTotal(); i++)
   {
      ulong ticket = PositionGetTicket(i);
      if(PositionSelectByTicket(ticket))
      {
         if(PositionGetString(POSITION_SYMBOL) == _Symbol && 
            PositionGetInteger(POSITION_MAGIC) == MagicNumber)
         {
            positionFound = true;
            
            double profit = PositionGetDouble(POSITION_PROFIT);
            static double lastProfit = 0;
            
            if(profit != lastProfit)
            {
               todayProfit += (profit - lastProfit);
               totalProfit += (profit - lastProfit);
               lastProfit = profit;
               UpdateDashboard();
            }
            break;
         }
      }
   }
   
   if(!positionFound)
   {
      inTrade = false;
      RemoveTradeLines();
   }
}

//+------------------------------------------------------------------+
//| Close all trades                                                 |
//+------------------------------------------------------------------+
void CloseAllTrades(string comment)
{
   int closedCount = 0;
   double totalClosedProfit = 0;
   
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(PositionSelectByTicket(ticket))
      {
         if(PositionGetString(POSITION_SYMBOL) == _Symbol && 
            PositionGetInteger(POSITION_MAGIC) == MagicNumber)
         {
            double positionProfit = PositionGetDouble(POSITION_PROFIT);
            totalClosedProfit += positionProfit;
            
            MqlTradeRequest request = {};
            MqlTradeResult result = {};
            
            request.action = TRADE_ACTION_DEAL;
            request.symbol = _Symbol;
            request.volume = PositionGetDouble(POSITION_VOLUME);
            request.type = (PositionGetInteger(POSITION_TYPE) == POSITION_TYPE_BUY) ? ORDER_TYPE_SELL : ORDER_TYPE_BUY;
            request.price = SymbolInfoDouble(_Symbol, (request.type == ORDER_TYPE_BUY) ? SYMBOL_ASK : SYMBOL_BID);
            request.deviation = Slippage;
            request.magic = MagicNumber;
            request.comment = comment;
            
            bool success = OrderSend(request, result);
            if(success)
            {
               closedCount++;
               
               bool isWin = positionProfit > 0;
               UpdateTradeStats(positionProfit, isWin);
               
               Print("🔒 Position closed: ", ticket, " Profit: $", positionProfit);
            }
         }
      }
   }
   
   if(closedCount > 0)
   {
      inTrade = false;
      RemoveTradeLines();
      Print("📊 Closed ", closedCount, " positions. Total profit: $", totalClosedProfit);
   }
   
   UpdateDashboard();
}

//+------------------------------------------------------------------+
//| Draw trade lines                                                 |
//+------------------------------------------------------------------+
void DrawTradeLines()
{
   string slName = "ORION_SL_" + IntegerToString(tStartBar);
   ObjectCreate(0, slName, OBJ_HLINE, 0, 0, tSL);
   ObjectSetInteger(0, slName, OBJPROP_COLOR, clrRed);
   ObjectSetInteger(0, slName, OBJPROP_STYLE, STYLE_DASH);
   ObjectSetInteger(0, slName, OBJPROP_WIDTH, 2);
   
   string tpName = "ORION_TP_" + IntegerToString(tStartBar);
   ObjectCreate(0, tpName, OBJ_HLINE, 0, 0, tTP);
   ObjectSetInteger(0, tpName, OBJPROP_COLOR, clrGreen);
   ObjectSetInteger(0, tpName, OBJPROP_STYLE, STYLE_DASH);
   ObjectSetInteger(0, tpName, OBJPROP_WIDTH, 2);
}

//+------------------------------------------------------------------+
//| Update trade lines                                               |
//+------------------------------------------------------------------+
void UpdateTradeLines()
{
   string slName = "ORION_SL_" + IntegerToString(tStartBar);
   if(ObjectFind(0, slName) >= 0)
   {
      ObjectSetDouble(0, slName, OBJPROP_PRICE, tSL);
   }
   
   string tpName = "ORION_TP_" + IntegerToString(tStartBar);
   if(ObjectFind(0, tpName) >= 0)
   {
      ObjectSetDouble(0, tpName, OBJPROP_PRICE, tTP);
   }
}

//+------------------------------------------------------------------+
//| Remove trade lines                                               |
//+------------------------------------------------------------------+
void RemoveTradeLines()
{
   ObjectsDeleteAll(0, "ORION_SL_");
   ObjectsDeleteAll(0, "ORION_TP_");
}
//+------------------------------------------------------------------+