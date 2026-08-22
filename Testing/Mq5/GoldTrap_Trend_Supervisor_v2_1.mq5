//+------------------------------------------------------------------+
//|                 GoldTrap Trend Supervisor v2.1                    |
//|                 Personal / private use                          |
//|                                                                  |
//| PURPOSE                                                          |
//| - Does NOT replicate or modify GoldTrap's internal strategy.     |
//| - Monitors XAUUSD and blocks NEW cycles during strong/prolonged  |
//|   trends.                                                        |
//| - If GoldTrap already has an open basket, it does NOT intervene. |
//| - If GoldTrap is flat and a strong trend is detected:            |
//|      1) deletes pending orders with GoldTrap Magic Numbers       |
//|      2) closes ONLY the chart running the exact GoldTrap EA      |
//| - After the market remains normal for the configured time,       |
//|   it opens XAUUSD M5 and applies the original GoldTrap template. |
//|                                                                  |
//| SETUP                                                            |
//| 1. Open XAUUSD M5.                                               |
//| 2. Load the original GoldTrap with DEFAULT settings.             |
//| 3. Save the template as: GoldTrap_Default.tpl                    |
//| 4. Close that chart.                                             |
//| 5. Attach THIS supervisor to a different chart.                  |
//| 6. Keep Algo Trading enabled.                                    |
//|                                                                  |
//| IMPORTANT                                                        |
//| - v2.1 identifies GoldTrap by symbol + timeframe + exact EA name. |
//| - Expected EA: GoldTrap_v4.2.4_BETA_fix.                         |
//| - Trend thresholds are initial defaults: validate on DEMO.       |
//+------------------------------------------------------------------+
#property strict
#property version   "2.10"
#property description "External strong-trend supervisor for GoldTrap v4.2.4 BETA fix"
// BUILD: 2.10 - clean v2.1 build with corrected position identifiers.

#include <Trade/Trade.mqh>

CTrade trade;

//--------------------------- IDENTIFICATION -------------------------
input group "=== ORIGINAL GOLDTRAP ==="
input string GoldTrap_Symbol                 = "";                 // Empty = symbol of the supervisor chart
input ENUM_TIMEFRAMES GoldTrap_Timeframe      = PERIOD_M5;          // Timeframe where GoldTrap runs
input string GoldTrap_Expert_Name           = "GoldTrap_v4.2.4_BETA_fix"; // Exact EX5 name, without extension
input string GoldTrap_Template               = "GoldTrap_Default.tpl";
input ulong GoldTrap_BUY_Magic                = 837490;
input ulong GoldTrap_SELL_Magic               = 528101;

//------------------------ TREND FILTER -----------------------
input group "=== M15 TREND FILTER ==="
input int M15_ADX_Period                     = 14;
input double Minimum_Trend_ADX             = 28.0;               // +1 point if ADX >= this value
input int M15_Fast_EMA                      = 50;
input int M15_Slow_EMA                       = 200;
input int M15_ATR_Period                     = 14;
input double M15_EMA_Separation_ATR          = 0.80;               // +1 if |EMA50-EMA200| >= X * ATR
input int M15_EMA_Slope_Bars            = 5;
input double M15_EMA_Slope_ATR            = 0.40;               // +1 if EMA50 slope >= X * ATR
input double M15_Price_Extension_ATR         = 1.00;               // +1 if price distance from EMA50 >= X * ATR

input group "=== H1 TREND CONFIRMATION ==="
input int H1_Fast_EMA                       = 50;
input int H1_Slow_EMA                        = 200;
input int H1_ATR_Period                      = 14;
input double H1_EMA_Separation_ATR           = 0.50;               // +1 if H1 trend is aligned and separated
input int H1_EMA_Slope_Bars             = 3;
input double H1_EMA_Slope_ATR             = 0.25;               // +1 if H1 slope is aligned

input group "=== RECENT ACCELERATION ==="
input int Recent_Move_Minutes         = 30;                 // M5 displacement lookback window
input double Recent_Move_ATR_M15      = 1.50;               // +1 if displacement >= X * M15 ATR

input group "=== DECISION AND RESTART ==="
input int Block_Trend_Score             = 5;                  // From 0 to 7
input int Normal_Trend_Score    = 2;                  // Score must fall to this level to begin cooldown
input int Normal_Minutes_To_Restart      = 30;                 // Continuous normal conditions required before restarting GoldTrap
input int Check_Interval_Seconds        = 2;

input group "=== DISPLAY / LOG ==="
input bool Show_Status_Panel            = true;
input bool Write_Events_To_Journal         = true;

//----------------------------- HANDLES ------------------------------
int hADX_M15     = INVALID_HANDLE;
int hEMAFast_M15 = INVALID_HANDLE;
int hEMASlow_M15 = INVALID_HANDLE;
int hATR_M15     = INVALID_HANDLE;
int hEMAFast_H1  = INVALID_HANDLE;
int hEMASlow_H1  = INVALID_HANDLE;
int hATR_H1      = INVALID_HANDLE;

string g_symbol = "";
long   g_goldtrap_chart_id = 0;
bool   g_blocked_by_trend = false;
datetime g_normal_since = 0;
string g_last_action = "Initializing";
int g_last_score = -1;

// Prevent repeated identical Journal messages
string g_last_log = "";

struct TrendSnapshot
{
   bool valid;
   int score;
   int direction; // 1 bullish, -1 bearish, 0 undefined

   double adx;
   double atr_m15;
   double atr_h1;
   double ema_fast_m15;
   double ema_slow_m15;
   double ema_fast_h1;
   double ema_slow_h1;
   double ema_sep_atr_m15;
   double ema_slope_atr_m15;
   double price_extension_atr_m15;
   double ema_sep_atr_h1;
   double ema_slope_atr_h1;
   double recent_move_atr_m15;

   bool c_adx;
   bool c_sep_m15;
   bool c_slope_m15;
   bool c_extension_m15;
   bool c_sep_h1;
   bool c_slope_h1;
   bool c_recent_move;
};

//+------------------------------------------------------------------+
//| Utilities                                                        |
//+------------------------------------------------------------------+
void LogEvent(const string msg)
{
   if(!Write_Events_To_Journal)
      return;

   if(msg == g_last_log)
      return;

   Print("[GoldTrap Supervisor] ", msg);
   g_last_log = msg;
}

bool IsGoldTrapMagic(const long magic)
{
   return ((ulong)magic == GoldTrap_BUY_Magic || (ulong)magic == GoldTrap_SELL_Magic);
}

bool GetBufferValue(const int handle,const int buffer,const int shift,double &value)
{
   double data[1];
   ResetLastError();
   int copied = CopyBuffer(handle,buffer,shift,1,data);
   if(copied != 1)
      return false;

   value = data[0];
   if(value == EMPTY_VALUE)
      return false;

   return true;
}

int SignOf(const double value)
{
   if(value > 0.0) return 1;
   if(value < 0.0) return -1;
   return 0;
}

string TrendDirectionText(const int dir)
{
   if(dir > 0) return "BULLISH";
   if(dir < 0) return "BEARISH";
   return "UNDEFINED";
}

//+------------------------------------------------------------------+
//| GoldTrap positions / pending orders                             |
//+------------------------------------------------------------------+
int CountGoldTrapPositions()
{
   int count = 0;

   for(int i=PositionsTotal()-1; i>=0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;

      if(!PositionSelectByTicket(ticket))
         continue;

      string position_symbol = "";
      long position_magic = 0;

      if(!PositionGetString(POSITION_SYMBOL, position_symbol))
         continue;

      if(!PositionGetInteger(POSITION_MAGIC, position_magic))
         continue;

      if(position_symbol != g_symbol)
         continue;

      if(IsGoldTrapMagic(position_magic))
         count++;
   }

   return count;
}

int CountGoldTrapPendingOrders()
{
   int count = 0;

   for(int i=OrdersTotal()-1; i>=0; i--)
   {
      ulong ticket = OrderGetTicket(i);
      if(ticket == 0)
         continue;

      if(OrderGetString(ORDER_SYMBOL) != g_symbol)
         continue;

      long magic = OrderGetInteger(ORDER_MAGIC);
      if(IsGoldTrapMagic(magic))
         count++;
   }

   return count;
}

bool DeleteGoldTrapPendingOrders()
{
   bool all_ok = true;

   // Iterate backwards because OrdersTotal changes when deleting orders.
   for(int i=OrdersTotal()-1; i>=0; i--)
   {
      ulong ticket = OrderGetTicket(i);
      if(ticket == 0)
         continue;

      if(OrderGetString(ORDER_SYMBOL) != g_symbol)
         continue;

      long magic = OrderGetInteger(ORDER_MAGIC);
      if(!IsGoldTrapMagic(magic))
         continue;

      ResetLastError();
      if(!trade.OrderDelete(ticket))
      {
         all_ok = false;
         PrintFormat("[GoldTrap Supervisor] Could not delete pending order #%I64u. Retcode=%u (%s), Error=%d",
                     ticket,trade.ResultRetcode(),trade.ResultRetcodeDescription(),GetLastError());
      }
      else
      {
         PrintFormat("[GoldTrap Supervisor] GoldTrap pending order #%I64u deleted by trend filter.",ticket);
      }
   }

   return all_ok;
}

//+------------------------------------------------------------------+
//| Find / start / stop the GoldTrap chart                   |
//+------------------------------------------------------------------+
bool GoldTrapExpertNameMatches(const string expert_name)
{
   if(StringLen(expert_name) == 0)
      return false;

   // CHART_EXPERT_NAME normally returns the name without the extension,
   // but the .ex5 form is also accepted for safety.
   if(expert_name == GoldTrap_Expert_Name)
      return true;

   if(expert_name == GoldTrap_Expert_Name + ".ex5")
      return true;

   return false;
}

bool IsExactGoldTrapChart(const long chart_id)
{
   if(chart_id <= 0 || chart_id == ChartID())
      return false;

   if(ChartSymbol(chart_id) != g_symbol)
      return false;

   if((ENUM_TIMEFRAMES)ChartPeriod(chart_id) != GoldTrap_Timeframe)
      return false;

   string expert_name = "";
   ResetLastError();
   if(!ChartGetString(chart_id,CHART_EXPERT_NAME,expert_name))
      return false;

   return GoldTrapExpertNameMatches(expert_name);
}

long FindGoldTrapChart(int &matches)
{
   matches = 0;
   long found = 0;

   long id = ChartFirst();
   while(id >= 0)
   {
      if(IsExactGoldTrapChart(id))
      {
         matches++;
         if(found == 0)
            found = id;
      }

      id = ChartNext(id);
   }

   return found;
}

bool GoldTrapChartExists()
{
   int matches = 0;
   long id = FindGoldTrapChart(matches);

   if(matches > 1)
      LogEvent("WARNING: multiple charts have the exact GoldTrap EA loaded. Keep only one controlled instance.");

   if(id != 0)
   {
      g_goldtrap_chart_id = id;
      return true;
   }

   g_goldtrap_chart_id = 0;
   return false;
}

bool StartGoldTrapChart()
{
   if(GoldTrapChartExists())
      return true;

   ResetLastError();
   long id = ChartOpen(g_symbol,GoldTrap_Timeframe);
   if(id == 0)
   {
      PrintFormat("[GoldTrap Supervisor] ERROR ChartOpen(%s,%s). Error=%d",
                  g_symbol,EnumToString(GoldTrap_Timeframe),GetLastError());
      return false;
   }

   g_goldtrap_chart_id = id;

   ResetLastError();
   if(!ChartApplyTemplate(id,GoldTrap_Template))
   {
      int err = GetLastError();
      PrintFormat("[GoldTrap Supervisor] ERROR applying template '%s'. Error=%d",GoldTrap_Template,err);
      ChartClose(id);
      g_goldtrap_chart_id = 0;
      return false;
   }

   LogEvent("GoldTrap STARTED: chart opened and template applied.");
   return true;
}

bool StopGoldTrapChart()
{
   // First delete pending orders by Magic Number so none remain active
   // after the EA is removed from its chart.
   DeleteGoldTrapPendingOrders();

   if(!GoldTrapChartExists())
   {
      g_goldtrap_chart_id = 0;
      return true;
   }

   ResetLastError();
   if(!ChartClose(g_goldtrap_chart_id))
   {
      PrintFormat("[GoldTrap Supervisor] ERROR closing GoldTrap chart ID=%I64d. Error=%d",
                  g_goldtrap_chart_id,GetLastError());
      return false;
   }

   LogEvent("GoldTrap STOPPED because of a strong trend (no open basket).");
   g_goldtrap_chart_id = 0;
   return true;
}

//+------------------------------------------------------------------+
//| Trend evaluation                                          |
//+------------------------------------------------------------------+
bool EvaluateTrend(TrendSnapshot &s)
{
   s.valid = false;
   s.score = 0;
   s.direction = 0;

   double adx=0.0;
   double emaFast15=0.0, emaSlow15=0.0, emaFast15Past=0.0;
   double atr15=0.0;
   double emaFastH1=0.0, emaSlowH1=0.0, emaFastH1Past=0.0;
   double atrH1=0.0;

   // Always use CLOSED bars (shift 1) to avoid intrabar noise.
   if(!GetBufferValue(hADX_M15,0,1,adx)) return false;
   if(!GetBufferValue(hEMAFast_M15,0,1,emaFast15)) return false;
   if(!GetBufferValue(hEMASlow_M15,0,1,emaSlow15)) return false;
   if(!GetBufferValue(hEMAFast_M15,0,1+M15_EMA_Slope_Bars,emaFast15Past)) return false;
   if(!GetBufferValue(hATR_M15,0,1,atr15)) return false;

   if(!GetBufferValue(hEMAFast_H1,0,1,emaFastH1)) return false;
   if(!GetBufferValue(hEMASlow_H1,0,1,emaSlowH1)) return false;
   if(!GetBufferValue(hEMAFast_H1,0,1+H1_EMA_Slope_Bars,emaFastH1Past)) return false;
   if(!GetBufferValue(hATR_H1,0,1,atrH1)) return false;

   if(atr15 <= 0.0 || atrH1 <= 0.0)
      return false;

   double closeM15 = iClose(g_symbol,PERIOD_M15,1);
   if(closeM15 <= 0.0)
      return false;

   // Base direction from the M15 EMA structure.
   int dir = SignOf(emaFast15 - emaSlow15);
   if(dir == 0)
      return false;

   double sep15 = MathAbs(emaFast15 - emaSlow15) / atr15;
   double slope15 = (emaFast15 - emaFast15Past) / atr15;
   double extension15 = (closeM15 - emaFast15) / atr15;

   int h1Dir = SignOf(emaFastH1 - emaSlowH1);
   double sepH1 = MathAbs(emaFastH1 - emaSlowH1) / atrH1;
   double slopeH1 = (emaFastH1 - emaFastH1Past) / atrH1;

   // Recent M5 move, normalized by M15 ATR.
   int barsM5 = (int)MathMax(1,MathRound((double)Recent_Move_Minutes / 5.0));
   double recentClose = iClose(g_symbol,PERIOD_M5,1);
   double oldClose = iClose(g_symbol,PERIOD_M5,1+barsM5);
   if(recentClose <= 0.0 || oldClose <= 0.0)
      return false;

   double recentMove = (recentClose - oldClose) / atr15;

   s.c_adx = (adx >= Minimum_Trend_ADX);
   s.c_sep_m15 = (sep15 >= M15_EMA_Separation_ATR);
   s.c_slope_m15 = (SignOf(slope15) == dir && MathAbs(slope15) >= M15_EMA_Slope_ATR);
   s.c_extension_m15 = (SignOf(extension15) == dir && MathAbs(extension15) >= M15_Price_Extension_ATR);
   s.c_sep_h1 = (h1Dir == dir && sepH1 >= H1_EMA_Separation_ATR);
   s.c_slope_h1 = (SignOf(slopeH1) == dir && MathAbs(slopeH1) >= H1_EMA_Slope_ATR);
   s.c_recent_move = (SignOf(recentMove) == dir && MathAbs(recentMove) >= Recent_Move_ATR_M15);

   if(s.c_adx)           s.score++;
   if(s.c_sep_m15)       s.score++;
   if(s.c_slope_m15)     s.score++;
   if(s.c_extension_m15) s.score++;
   if(s.c_sep_h1)        s.score++;
   if(s.c_slope_h1)      s.score++;
   if(s.c_recent_move)   s.score++;

   s.direction = dir;
   s.adx = adx;
   s.atr_m15 = atr15;
   s.atr_h1 = atrH1;
   s.ema_fast_m15 = emaFast15;
   s.ema_slow_m15 = emaSlow15;
   s.ema_fast_h1 = emaFastH1;
   s.ema_slow_h1 = emaSlowH1;
   s.ema_sep_atr_m15 = sep15;
   s.ema_slope_atr_m15 = slope15;
   s.price_extension_atr_m15 = extension15;
   s.ema_sep_atr_h1 = sepH1;
   s.ema_slope_atr_h1 = slopeH1;
   s.recent_move_atr_m15 = recentMove;
   s.valid = true;
   return true;
}

//+------------------------------------------------------------------+
//| Status panel                                                             |
//+------------------------------------------------------------------+
string YesNoText(const bool v)
{
   return v ? "YES" : "no";
}

void DrawPanel(const TrendSnapshot &s,const int positions,const int pendings,const bool chart_exists)
{
   if(!Show_Status_Panel)
   {
      Comment("");
      return;
   }

   string txt = "GOLDTRAP TREND SUPERVISOR v1.1\n";
   txt += "----------------------------------------\n";
   txt += "Symbol: " + g_symbol + " | GoldTrap: " + (chart_exists ? "RUNNING" : "STOPPED") + "\n";
   txt += "Controlled EA: " + GoldTrap_Expert_Name + "\n";
   txt += "GoldTrap positions: " + IntegerToString(positions) + " | Pending orders: " + IntegerToString(pendings) + "\n";

   if(!s.valid)
   {
      txt += "\nFILTER: INSUFFICIENT DATA\n";
      txt += "Action: current state is unchanged.\n";
   }
   else
   {
      txt += "\nTrend: " + TrendDirectionText(s.direction) + "\n";
      txt += "Trend Score: " + IntegerToString(s.score) + "/7";
      txt += "  (block >= " + IntegerToString(Block_Trend_Score) + ")\n";
      txt += "ADX M15: " + DoubleToString(s.adx,1) + " | condition: " + YesNoText(s.c_adx) + "\n";
      txt += "M15 EMA separation/ATR: " + DoubleToString(s.ema_sep_atr_m15,2) + " | " + YesNoText(s.c_sep_m15) + "\n";
      txt += "M15 EMA slope/ATR: " + DoubleToString(s.ema_slope_atr_m15,2) + " | " + YesNoText(s.c_slope_m15) + "\n";
      txt += "M15 price extension/ATR: " + DoubleToString(s.price_extension_atr_m15,2) + " | " + YesNoText(s.c_extension_m15) + "\n";
      txt += "H1 EMA separation/ATR: " + DoubleToString(s.ema_sep_atr_h1,2) + " | " + YesNoText(s.c_sep_h1) + "\n";
      txt += "H1 EMA slope/ATR: " + DoubleToString(s.ema_slope_atr_h1,2) + " | " + YesNoText(s.c_slope_h1) + "\n";
      txt += "Recent move/M15 ATR: " + DoubleToString(s.recent_move_atr_m15,2) + " | " + YesNoText(s.c_recent_move) + "\n";

      txt += "\nFilter state: " + (g_blocked_by_trend ? "BLOCKED" : "NORMAL") + "\n";

      if(g_blocked_by_trend && g_normal_since > 0)
      {
         int elapsed = (int)(TimeCurrent() - g_normal_since);
         int required = Normal_Minutes_To_Restart * 60;
         int remain = MathMax(0,required-elapsed);
         txt += "Normal time before restart: " + IntegerToString(elapsed/60) + "/" + IntegerToString(Normal_Minutes_To_Restart) + " min";
         if(remain > 0)
            txt += " (remaining " + IntegerToString((remain+59)/60) + " min)";
         txt += "\n";
      }
   }

   txt += "\nACTION: " + g_last_action + "\n";
   txt += "----------------------------------------\n";
   txt += "The supervisor NEVER closes GoldTrap positions.";

   Comment(txt);
}

//+------------------------------------------------------------------+
//| Main logic                                                  |
//+------------------------------------------------------------------+
void SupervisorCycle()
{
   TrendSnapshot s;
   bool trend_ok = EvaluateTrend(s);

   int positions = CountGoldTrapPositions();
   int pendings = CountGoldTrapPendingOrders();
   bool chart_exists = GoldTrapChartExists();

   if(!trend_ok)
   {
      g_last_action = "Insufficient indicator data: no intervention";
      DrawPanel(s,positions,pendings,chart_exists);
      return;
   }

   g_last_score = s.score;
   datetime now = TimeCurrent();

   // 1) If a strong trend appears, activate the block immediately.
   if(s.score >= Block_Trend_Score)
   {
      if(!g_blocked_by_trend)
         LogEvent("STRONG TREND detected. Score=" + IntegerToString(s.score) + "/7, direction=" + TrendDirectionText(s.direction));

      g_blocked_by_trend = true;
      g_normal_since = 0;
   }
   // 2) Once blocked, start the restart timer only when
   //    the score clearly falls to the NORMAL threshold (hysteresis).
   else if(g_blocked_by_trend)
   {
      if(s.score <= Normal_Trend_Score)
      {
         if(g_normal_since == 0)
         {
            g_normal_since = now;
            LogEvent("Market returned to NORMAL zone. Restart confirmation period started.");
         }

         if((now - g_normal_since) >= (Normal_Minutes_To_Restart * 60))
         {
            g_blocked_by_trend = false;
            g_normal_since = 0;
            LogEvent("Normal-market confirmation completed. GoldTrap may trade again.");
         }
      }
      else
      {
         // If the score rebounds into the intermediate zone before cooldown completes,
         // require continuous normal conditions again.
         g_normal_since = 0;
      }
   }

   // 3) Absolute rule: if an open basket exists, DO NOT touch GoldTrap.
   if(positions > 0)
   {
      if(!chart_exists)
      {
         // If the GoldTrap chart is missing for any reason, restore it so
         // GoldTrap can continue managing the existing basket.
         if(StartGoldTrapChart())
            g_last_action = "Active basket: GoldTrap restored so it can continue managing it";
         else
            g_last_action = "ALERT: active basket but GoldTrap could not be restored";
      }
      else if(g_blocked_by_trend)
      {
         g_last_action = "Strong trend, BUT a basket is active: wait for GoldTrap to finish";
      }
      else
      {
         g_last_action = "Active basket: GoldTrap manages it normally";
      }

      pendings = CountGoldTrapPendingOrders();
      chart_exists = GoldTrapChartExists();
      DrawPanel(s,positions,pendings,chart_exists);
      return;
   }

   // 4) No open positions: GoldTrap may now be stopped or started.
   if(g_blocked_by_trend)
   {
      // Delete pending orders even if the GoldTrap chart is already gone.
      DeleteGoldTrapPendingOrders();

      if(chart_exists)
      {
         if(StopGoldTrapChart())
            g_last_action = "GoldTrap STOPPED: strong trend and no open positions";
         else
            g_last_action = "ERROR while trying to stop GoldTrap";
      }
      else
      {
         g_last_action = "GoldTrap STOPPED: waiting for trend normalization";
      }
   }
   else
   {
      if(!chart_exists)
      {
         if(StartGoldTrapChart())
            g_last_action = "Normal market: GoldTrap STARTED";
         else
            g_last_action = "ERROR starting GoldTrap / check template";
      }
      else
      {
         g_last_action = "Normal market: GoldTrap running";
      }
   }

   pendings = CountGoldTrapPendingOrders();
   chart_exists = GoldTrapChartExists();
   DrawPanel(s,positions,pendings,chart_exists);
}

//+------------------------------------------------------------------+
//| Events                                                           |
//+------------------------------------------------------------------+
int OnInit()
{
   g_symbol = (StringLen(GoldTrap_Symbol) == 0 ? _Symbol : GoldTrap_Symbol);

   if(!SymbolSelect(g_symbol,true))
   {
      Print("[GoldTrap Supervisor] Could not select symbol: ",g_symbol);
      return INIT_FAILED;
   }

   if(Block_Trend_Score < 1 || Block_Trend_Score > 7)
   {
      Print("[GoldTrap Supervisor] Block_Trend_Score must be between 1 and 7.");
      return INIT_PARAMETERS_INCORRECT;
   }

   if(Normal_Trend_Score < 0 || Normal_Trend_Score >= Block_Trend_Score)
   {
      Print("[GoldTrap Supervisor] Normal score must be lower than block score.");
      return INIT_PARAMETERS_INCORRECT;
   }

   hADX_M15     = iADX(g_symbol,PERIOD_M15,M15_ADX_Period);
   hEMAFast_M15 = iMA(g_symbol,PERIOD_M15,M15_Fast_EMA,0,MODE_EMA,PRICE_CLOSE);
   hEMASlow_M15 = iMA(g_symbol,PERIOD_M15,M15_Slow_EMA,0,MODE_EMA,PRICE_CLOSE);
   hATR_M15     = iATR(g_symbol,PERIOD_M15,M15_ATR_Period);

   hEMAFast_H1  = iMA(g_symbol,PERIOD_H1,H1_Fast_EMA,0,MODE_EMA,PRICE_CLOSE);
   hEMASlow_H1  = iMA(g_symbol,PERIOD_H1,H1_Slow_EMA,0,MODE_EMA,PRICE_CLOSE);
   hATR_H1      = iATR(g_symbol,PERIOD_H1,H1_ATR_Period);

   if(hADX_M15 == INVALID_HANDLE || hEMAFast_M15 == INVALID_HANDLE ||
      hEMASlow_M15 == INVALID_HANDLE || hATR_M15 == INVALID_HANDLE ||
      hEMAFast_H1 == INVALID_HANDLE || hEMASlow_H1 == INVALID_HANDLE ||
      hATR_H1 == INVALID_HANDLE)
   {
      Print("[GoldTrap Supervisor] ERROR creating indicators. Error=",GetLastError());
      return INIT_FAILED;
   }

   trade.SetAsyncMode(false);

   int timer_sec = MathMax(1,Check_Interval_Seconds);
   EventSetTimer(timer_sec);

   Print("============================================================");
   Print("GoldTrap Trend Supervisor v2.1 started");
   Print("Monitored symbol: ",g_symbol);
   Print("GoldTrap timeframe: ",EnumToString(GoldTrap_Timeframe));
   Print("Expected GoldTrap EA: ",GoldTrap_Expert_Name);
   Print("Template: ",GoldTrap_Template);
   Print("BUY Magic: ",GoldTrap_BUY_Magic," | SELL Magic: ",GoldTrap_SELL_Magic);
   Print("IMPORTANT: the supervisor never closes GoldTrap positions.");
   Print("============================================================");

   if(!MQLInfoInteger(MQL_TRADE_ALLOWED))
      Print("[GoldTrap Supervisor] WARNING: enable 'Allow Algo Trading' for this supervisor. It is required to delete pending orders and so the EA loaded by template keeps trading permissions.");

   // Run the first check immediately without waiting for the timer.
   SupervisorCycle();
   return INIT_SUCCEEDED;
}

void OnDeinit(const int reason)
{
   EventKillTimer();

   if(hADX_M15 != INVALID_HANDLE)     IndicatorRelease(hADX_M15);
   if(hEMAFast_M15 != INVALID_HANDLE) IndicatorRelease(hEMAFast_M15);
   if(hEMASlow_M15 != INVALID_HANDLE) IndicatorRelease(hEMASlow_M15);
   if(hATR_M15 != INVALID_HANDLE)     IndicatorRelease(hATR_M15);
   if(hEMAFast_H1 != INVALID_HANDLE)  IndicatorRelease(hEMAFast_H1);
   if(hEMASlow_H1 != INVALID_HANDLE)  IndicatorRelease(hEMASlow_H1);
   if(hATR_H1 != INVALID_HANDLE)      IndicatorRelease(hATR_H1);

   Comment("");
}

void OnTimer()
{
   SupervisorCycle();
}

void OnTick()
{
   // Logic runs on a timer so it still reacts when there are few ticks
   // and avoids recalculating several times per second in very active markets.
}
//+------------------------------------------------------------------+
