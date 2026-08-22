//+------------------------------------------------------------------+
//|                                                      No1_Algo_M5.mq5 |
//|                              N1 Algo NF - XAUUSD M5 Pivot Breakout |
//|                                                                    |
//+------------------------------------------------------------------+
#property copyright "N1 Algo NF"
#property link      ""
#property version   "1.00"
#property strict
#property description "Pivot breakout EA for XAUUSD M5"
#property description "BUY  - close breaks ABOVE the most recent confirmed pivot high"
#property description "SELL - close breaks BELOW the most recent confirmed pivot low"
#property description "No filters. Trades every signal."

#include <Trade\Trade.mqh>
#include <Trade\SymbolInfo.mqh>

//+------------------------------------------------------------------+
//| INPUT PARAMETERS                                                  |
//+------------------------------------------------------------------+
input string   InpSymbol         = "XAUUSD";   // Symbol (leave as XAUUSD or your broker's gold symbol)
input double   InpLotSize        = 0.01;       // Lot Size
input double   InpTP_Dollars     = 2.0;        // Take Profit ($)
input double   InpSL_Dollars     = 0.0;        // Stop Loss ($)
input bool     InpUseTP          = true;       // Enable Take Profit
input bool     InpUseSL          = false;      // Enable Stop Loss
input int      InpPivotLookback  = 5;          // Pivot Lookback Bars
input int      InpMinBars        = 300;        // Minimum History Bars
input ulong    InpMagicNumber    = 20250322;   // Magic Number
input ulong    InpSlippage       = 10;         // Slippage (points)

//+------------------------------------------------------------------+
//| GLOBAL VARIABLES                                                  |
//+------------------------------------------------------------------+
CTrade         g_trade;
CSymbolInfo    g_symbol;

double         g_up_levels[];      // Confirmed pivot high levels
int            g_up_count = 0;     // Number of active up levels
double         g_dn_levels[];      // Confirmed pivot low levels
int            g_dn_count = 0;     // Number of active down levels
const int      MAX_LEVELS = 10;    // Maximum stored levels

datetime       g_last_bar_time = 0; // Time of last processed bar
bool           g_initialized = false;
ulong          g_open_ticket = 0;
int            g_open_direction = 0; // 1=BUY, -1=SELL, 0=none

// Stats
int            g_total_trades = 0;
int            g_total_wins = 0;
int            g_total_losses = 0;
double         g_session_pnl = 0.0;

//+------------------------------------------------------------------+
//| Expert initialization function                                    |
//+------------------------------------------------------------------+
int OnInit()
{
   //--- Initialize trade object
   g_trade.SetExpertMagicNumber(InpMagicNumber);
   g_trade.SetDeviationInPoints(InpSlippage);
   g_trade.SetTypeFilling(GetFillMode());
   g_trade.SetMarginMode();

   //--- Initialize symbol info
   if(!g_symbol.Name(InpSymbol))
   {
      Print("[ERROR] Symbol '", InpSymbol, "' not found!");
      return INIT_FAILED;
   }
   g_symbol.Refresh();
   g_symbol.RefreshRates();

   //--- Resize level arrays
   ArrayResize(g_up_levels, MAX_LEVELS);
   ArrayResize(g_dn_levels, MAX_LEVELS);
   g_up_count = 0;
   g_dn_count = 0;

   //--- Warm-up: process historical bars to build pivot levels
   WarmUp();

   //--- Store the last bar time so we wait for a genuinely new bar
   g_last_bar_time = GetLastClosedBarTime();

   //--- Check for existing position from prior session
   RestoreOpenPosition();

   g_initialized = true;

   //--- Print banner
   Print("============================================================");
   Print("   N1 Algo NF - Running");
   Print("   Symbol   : ", InpSymbol);
   Print("   Lot size : ", DoubleToString(InpLotSize, 2));
   Print("   SL       : $", DoubleToString(InpSL_Dollars, 2),
         " (", (InpUseSL ? "ON" : "OFF"), ")");
   Print("   TP       : $", DoubleToString(InpTP_Dollars, 2),
         " (", (InpUseTP ? "ON" : "OFF"), ")");
   Print("   Pivot LB : ", InpPivotLookback);
   Print("   Filters  : NONE -- trades every arrow");
   Print("============================================================");

   return INIT_SUCCEEDED;
}

//+------------------------------------------------------------------+
//| Expert deinitialization function                                  |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   Print("[INFO] N1 Algo NF stopped. Reason: ", reason);
   Print("[STATS] Trades: ", g_total_trades,
         " | Wins: ", g_total_wins,
         " | Losses: ", g_total_losses,
         " | P&L: $", DoubleToString(g_session_pnl, 2));
}

//+------------------------------------------------------------------+
//| Expert tick function                                              |
//+------------------------------------------------------------------+
void OnTick()
{
   if(!g_initialized)
      return;

   //--- Check if open position was closed by SL/TP
   CheckExit();

   //--- Check for new bar
   if(IsNewBar())
   {
      OnNewBar();
   }
}

//+------------------------------------------------------------------+
//| Get fill mode based on symbol's filling type                      |
//+------------------------------------------------------------------+
ENUM_ORDER_TYPE_FILLING GetFillMode()
{
   uint filling = (uint)SymbolInfoInteger(InpSymbol, SYMBOL_FILLING_MODE);

   if((filling & 1) != 0)
      return ORDER_FILLING_FOK;
   if((filling & 2) != 0)
      return ORDER_FILLING_IOC;

   return ORDER_FILLING_RETURN;
}

//+------------------------------------------------------------------+
//| Convert dollar amount to price distance                           |
//+------------------------------------------------------------------+
double DollarToPrice(double dollars)
{
   double tick_value = SymbolInfoDouble(InpSymbol, SYMBOL_TRADE_TICK_VALUE);
   double tick_size  = SymbolInfoDouble(InpSymbol, SYMBOL_TRADE_TICK_SIZE);

   if(tick_size == 0 || tick_value == 0)
      return 0.0;

   double ppv = (tick_value / tick_size) * InpLotSize;
   if(ppv == 0)
      return 0.0;

   return NormalizeDouble(dollars / ppv, g_symbol.Digits());
}

//+------------------------------------------------------------------+
//| Normalize lot size to broker requirements                          |
//+------------------------------------------------------------------+
double NormalizeLot(double lot)
{
   double min_lot  = SymbolInfoDouble(InpSymbol, SYMBOL_VOLUME_MIN);
   double max_lot  = SymbolInfoDouble(InpSymbol, SYMBOL_VOLUME_MAX);
   double step     = SymbolInfoDouble(InpSymbol, SYMBOL_VOLUME_STEP);

   lot = MathMax(min_lot, MathMin(max_lot, lot));

   if(step > 0)
      lot = NormalizeDouble(MathRound(lot / step) * step, 8);

   return lot;
}

//+------------------------------------------------------------------+
//| Apply safe stop distance to SL and TP                              |
//+------------------------------------------------------------------+
void SafeStops(double price, double &sl, double &tp, int direction)
{
   long stops_level = SymbolInfoInteger(InpSymbol, SYMBOL_TRADE_STOPS_LEVEL);
   double point = SymbolInfoDouble(InpSymbol, SYMBOL_POINT);
   double min_dist = stops_level * point;

   if(min_dist <= 0)
      return;

   int digits = (int)g_symbol.Digits();

   if(sl != 0.0 && MathAbs(price - sl) < min_dist)
   {
      if(direction == 1)
         sl = NormalizeDouble(price - min_dist, digits);
      else
         sl = NormalizeDouble(price + min_dist, digits);
   }

   if(tp != 0.0 && MathAbs(price - tp) < min_dist)
   {
      if(direction == 1)
         tp = NormalizeDouble(price + min_dist, digits);
      else
         tp = NormalizeDouble(price - min_dist, digits);
   }
}

//+------------------------------------------------------------------+
//| Pivot High detection - exact match of Python logic                |
//| Returns true if bar at index i is a pivot high (>= lb bars each side)|
//+------------------------------------------------------------------+
bool IsPivotHigh(int i, int lb, const double &high[], int total_bars)
{
   // Check bounds: need lb bars on left and lb bars on right
   if(i - lb < 0 || i + lb >= total_bars)
      return false;

   double val = high[i];
   for(int j = i - lb; j <= i + lb; j++)
   {
      if(high[j] > val)
         return false;
   }
   return true;
}

//+------------------------------------------------------------------+
//| Pivot Low detection - exact match of Python logic                 |
//| Returns true if bar at index i is a pivot low (<= lb bars each side)|
//+------------------------------------------------------------------+
bool IsPivotLow(int i, int lb, const double &low[], int total_bars)
{
   // Check bounds: need lb bars on left and lb bars on right
   if(i - lb < 0 || i + lb >= total_bars)
      return false;

   double val = low[i];
   for(int j = i - lb; j <= i + lb; j++)
   {
      if(low[j] < val)
         return false;
   }
   return true;
}

//+------------------------------------------------------------------+
//| Add a level to the front of the up_levels array                   |
//+------------------------------------------------------------------+
void AddUpLevel(double level)
{
   // Shift existing levels to the right
   if(g_up_count >= MAX_LEVELS)
      g_up_count = MAX_LEVELS - 1;
   for(int i = g_up_count; i > 0; i--)
      g_up_levels[i] = g_up_levels[i - 1];
   g_up_levels[0] = level;
   g_up_count++;
}

//+------------------------------------------------------------------+
//| Add a level to the front of the dn_levels array                   |
//+------------------------------------------------------------------+
void AddDnLevel(double level)
{
   // Shift existing levels to the right
   if(g_dn_count >= MAX_LEVELS)
      g_dn_count = MAX_LEVELS - 1;
   for(int i = g_dn_count; i > 0; i--)
      g_dn_levels[i] = g_dn_levels[i - 1];
   g_dn_levels[0] = level;
   g_dn_count++;
}

//+------------------------------------------------------------------+
//| Clear all up levels                                               |
//+------------------------------------------------------------------+
void ClearUpLevels()
{
   g_up_count = 0;
}

//+------------------------------------------------------------------+
//| Clear all down levels                                             |
//+------------------------------------------------------------------+
void ClearDnLevels()
{
   g_dn_count = 0;
}

//+------------------------------------------------------------------+
//| Warm-up: process historical bars without emitting signals          |
//| Builds pivot level state from history. Clearing never runs here.   |
//+------------------------------------------------------------------+
void WarmUp()
{
   int bars_needed = InpMinBars;
   double high[], low[], close[];
   datetime time[];

   ArraySetAsSeries(high, true);
   ArraySetAsSeries(low, true);
   ArraySetAsSeries(close, true);
   ArraySetAsSeries(time, true);

   if(CopyHigh(InpSymbol, PERIOD_M5, 0, bars_needed, high) <= 0) return;
   if(CopyLow(InpSymbol, PERIOD_M5, 0, bars_needed, low) <= 0) return;
   if(CopyClose(InpSymbol, PERIOD_M5, 0, bars_needed, close) <= 0) return;
   if(CopyTime(InpSymbol, PERIOD_M5, 0, bars_needed, time) <= 0) return;

   int n = ArraySize(high);
   if(n < InpPivotLookback * 2 + 2)
   {
      Print("[WARN] Not enough bars for warm-up: ", n);
      return;
   }

   int lb = InpPivotLookback;

   // Process all bars - in series mode, index 0 is most recent
   // We need oldest to newest for warm-up, so iterate in reverse
   for(int i = n - 1; i >= 0; i--)
   {
      // Check if this bar is a pivot
      // In series mode, higher index = older bar
      // i + lb is to the right (newer), i - lb is to the left (older)
      // For pivot detection: bar at position i needs lb bars on each side
      // Since array is set as series: i-lb is older, i+lb is newer
      // We need: i-lb >= 0 AND i+lb < n
      if(i - lb >= 0 && i + lb < n)
      {
         if(IsPivotHigh(i, lb, high, n))
            AddUpLevel(high[i]);
         if(IsPivotLow(i, lb, low, n))
            AddDnLevel(low[i]);
      }
   }

   Print("[INIT] Warmed up on ", n, " bars. Up levels: ", g_up_count,
         " | Dn levels: ", g_dn_count);
   Print("[INIT] Waiting for new bar before first trade.");
}

//+------------------------------------------------------------------+
//| Core signal detection - processes from last known bar to current   |
//| Returns: +1 (BUY), -1 (SELL), 0 (no signal)                       |
//+------------------------------------------------------------------+
int DetectSignal()
{
   double high[], low[], close[];
   datetime time[];

   ArraySetAsSeries(high, true);
   ArraySetAsSeries(low, true);
   ArraySetAsSeries(close, true);
   ArraySetAsSeries(time, true);

   if(CopyHigh(InpSymbol, PERIOD_M5, 0, InpMinBars, high) <= 0) return 0;
   if(CopyLow(InpSymbol, PERIOD_M5, 0, InpMinBars, low) <= 0) return 0;
   if(CopyClose(InpSymbol, PERIOD_M5, 0, InpMinBars, close) <= 0) return 0;
   if(CopyTime(InpSymbol, PERIOD_M5, 0, InpMinBars, time) <= 0) return 0;

   int n = ArraySize(high);
   if(n < InpPivotLookback * 2 + 2)
      return 0;

   int lb = InpPivotLookback;

   // Inject newly confirmable pivot:
   // Position (lb + 1) from the end in series mode = n - lb - 2 (0-based)
   // This is the bar that just became confirmable with lb bars on each side
   int new_piv_idx = lb + 1; // In series mode (0=newest, n-1=oldest)
   if(new_piv_idx < n && new_piv_idx - lb >= 0 && new_piv_idx + lb < n)
   {
      if(IsPivotHigh(new_piv_idx, lb, high, n))
         AddUpLevel(high[new_piv_idx]);
      if(IsPivotLow(new_piv_idx, lb, low, n))
         AddDnLevel(low[new_piv_idx]);
   }

   int signal = 0;

   // Process the most recent completed bar (index 1 in series mode = last closed bar)
   // and the current forming bar (index 0)
   // We compare the close of bar[1] (prev) with bar[0] current close

   double prev_close = close[1];
   double cur_close = close[0];

   // BUY: close crosses above most recent pivot high
   if(g_up_count > 0)
   {
      double lvl = g_up_levels[0];
      if(prev_close <= lvl && lvl < cur_close)
      {
         ClearUpLevels();
         signal = 1;
      }
   }

   // SELL: close crosses below most recent pivot low
   if(g_dn_count > 0)
   {
      double lvl = g_dn_levels[0];
      if(prev_close >= lvl && lvl > cur_close)
      {
         ClearDnLevels();
         signal = -1;
      }
   }

   // Also update pivot levels from the recently confirmed bars
   // (bars that now have enough history on both sides)
   for(int i = lb + 2; i < n - lb; i++)
   {
      if(i - lb >= 0 && i + lb < n)
      {
         if(IsPivotHigh(i, lb, high, n))
         {
            // Check if this level already exists
            bool exists = false;
            for(int k = 0; k < g_up_count; k++)
            {
               if(MathAbs(g_up_levels[k] - high[i]) < g_symbol.Point() * 0.5)
               {
                  exists = true;
                  break;
               }
            }
            if(!exists)
               AddUpLevel(high[i]);
         }
         if(IsPivotLow(i, lb, low, n))
         {
            bool exists = false;
            for(int k = 0; k < g_dn_count; k++)
            {
               if(MathAbs(g_dn_levels[k] - low[i]) < g_symbol.Point() * 0.5)
               {
                  exists = true;
                  break;
               }
            }
            if(!exists)
               AddDnLevel(low[i]);
         }
      }
   }

   return signal;
}

//+------------------------------------------------------------------+
//| Get time of the last closed (completed) bar                       |
//+------------------------------------------------------------------+
datetime GetLastClosedBarTime()
{
   datetime time[];
   ArraySetAsSeries(time, true);
   if(CopyTime(InpSymbol, PERIOD_M5, 0, 3, time) < 2)
      return 0;
   return time[1]; // Index 1 = last closed bar in series mode
}

//+------------------------------------------------------------------+
//| Check if a new bar has formed                                     |
//+------------------------------------------------------------------+
bool IsNewBar()
{
   datetime last_closed = GetLastClosedBarTime();
   if(last_closed == 0)
      return false;
   if(last_closed != g_last_bar_time)
   {
      g_last_bar_time = last_closed;
      return true;
   }
   return false;
}

//+------------------------------------------------------------------+
//| Get current open position with our magic number                   |
//+------------------------------------------------------------------+
bool GetOpenPosition(ulong &ticket, int &direction)
{
   ticket = 0;
   direction = 0;

   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong pos_ticket = PositionGetTicket(i);
      if(pos_ticket == 0)
         continue;

      if(PositionGetInteger(POSITION_MAGIC) != (long)InpMagicNumber)
         continue;

      if(PositionGetString(POSITION_SYMBOL) != InpSymbol)
         continue;

      ticket = pos_ticket;
      direction = (PositionGetInteger(POSITION_TYPE) == POSITION_TYPE_BUY) ? 1 : -1;
      return true;
   }

   return false;
}

//+------------------------------------------------------------------+
//| Restore open position from a prior session                        |
//+------------------------------------------------------------------+
void RestoreOpenPosition()
{
   ulong ticket;
   int direction;

   if(GetOpenPosition(ticket, direction))
   {
      g_open_ticket = ticket;
      g_open_direction = direction;
      Print("[INIT] Restored ", (direction == 1 ? "BUY" : "SELL"),
            " position ticket=", ticket, " from prior session");
   }
}

//+------------------------------------------------------------------+
//| Check if our open position was closed by SL/TP                    |
//+------------------------------------------------------------------+
void CheckExit()
{
   if(g_open_ticket == 0)
      return;

   // Check if position still exists
   ulong ticket;
   int direction;
   if(GetOpenPosition(ticket, direction))
   {
      // Position still open - check if it's our ticket
      if(ticket == g_open_ticket)
         return;
   }

   // Position is gone - check history for reason
   if(SelectHistoryDealByPosition(g_open_ticket))
   {
      ENUM_DEAL_ENTRY deal_entry = (ENUM_DEAL_ENTRY)HistoryDealGetInteger(g_open_ticket, DEAL_ENTRY);
      ENUM_DEAL_REASON deal_reason = (ENUM_DEAL_REASON)HistoryDealGetInteger(g_open_ticket, DEAL_REASON);

      string reason = "";
      if(deal_reason == DEAL_REASON_SL)
         reason = "SL";
      else if(deal_reason == DEAL_REASON_TP)
         reason = "TP";

      if(reason != "")
      {
         Print("[", reason, " HIT] ticket=", g_open_ticket,
               " - ready for next signal");

         // Record P&L
         double deal_profit = HistoryDealGetDouble(g_open_ticket, DEAL_PROFIT);
         double deal_swap = HistoryDealGetDouble(g_open_ticket, DEAL_SWAP);
         double deal_comm = HistoryDealGetDouble(g_open_ticket, DEAL_COMMISSION);
         double pnl = deal_profit + deal_swap + deal_comm;

         // Fallback when history hasn't propagated yet
         if(pnl == 0.0)
         {
            if(reason == "TP" && InpUseTP && InpTP_Dollars > 0)
               pnl = InpTP_Dollars;
            else if(reason == "SL" && InpUseSL && InpSL_Dollars > 0)
               pnl = -InpSL_Dollars;
         }

         RecordPnL(pnl);
      }
   }

   g_open_ticket = 0;
   g_open_direction = 0;
}

//+------------------------------------------------------------------+
//| Select history deal by position ticket                            |
//+------------------------------------------------------------------+
bool SelectHistoryDealByPosition(ulong pos_ticket)
{
   datetime from = 0;
   datetime to = TimeCurrent();

   if(!HistorySelect(from, to))
      return false;

   int total = HistoryDealsTotal();
   for(int i = total - 1; i >= 0; i--)
   {
      ulong deal_ticket = HistoryDealGetTicket(i);
      if(deal_ticket == 0)
         continue;

      if(HistoryDealGetInteger(deal_ticket, DEAL_POSITION_ID) == (long)pos_ticket)
      {
         ENUM_DEAL_ENTRY entry = (ENUM_DEAL_ENTRY)HistoryDealGetInteger(deal_ticket, DEAL_ENTRY);
         if(entry == DEAL_ENTRY_OUT || entry == DEAL_ENTRY_INOUT)
            return true;
      }
   }
   return false;
}

//+------------------------------------------------------------------+
//| Record P&L for a closed trade                                     |
//+------------------------------------------------------------------+
void RecordPnL(double pnl)
{
   g_session_pnl += pnl;
   if(pnl > 0)
      g_total_wins++;
   else if(pnl < 0)
      g_total_losses++;
}

//+------------------------------------------------------------------+
//| Open a new trade                                                   |
//+------------------------------------------------------------------+
ulong OpenTrade(int direction)
{
   g_symbol.Refresh();
   g_symbol.RefreshRates();

   double price;
   ENUM_ORDER_TYPE order_type;

   if(direction == 1)
   {
      price = g_symbol.Ask();
      order_type = ORDER_TYPE_BUY;
   }
   else
   {
      price = g_symbol.Bid();
      order_type = ORDER_TYPE_SELL;
   }

   if(price == 0.0)
   {
      Print("[ERROR] No price for ", InpSymbol);
      return 0;
   }

   double sl_p = 0.0;
   double tp_p = 0.0;
   int digits = (int)g_symbol.Digits();

   // Calculate SL in price terms
   if(InpUseSL && InpSL_Dollars > 0)
   {
      double d = DollarToPrice(InpSL_Dollars);
      if(d > 0)
      {
         if(direction == 1)
            sl_p = NormalizeDouble(price - d, digits);
         else
            sl_p = NormalizeDouble(price + d, digits);
      }
   }

   // Calculate TP in price terms
   if(InpUseTP && InpTP_Dollars > 0)
   {
      double d = DollarToPrice(InpTP_Dollars);
      if(d > 0)
      {
         if(direction == 1)
            tp_p = NormalizeDouble(price + d, digits);
         else
            tp_p = NormalizeDouble(price - d, digits);
      }
   }

   // Apply safe stops
   SafeStops(price, sl_p, tp_p, direction);

   double lot = NormalizeLot(InpLotSize);
   string label = (direction == 1) ? "BUY" : "SELL";

   if(!g_trade.PositionOpen(InpSymbol, order_type, lot, price, sl_p, tp_p, "N1AlgoNF"))
   {
      Print("[ERROR] Order failed [", label, "] retcode=", g_trade.ResultRetcode(),
            " | ", g_trade.ResultRetcodeDescription());
      return 0;
   }

   ulong ticket = g_trade.ResultOrder();
   Print("[TRADE] ", label, " opened | ticket=", ticket,
         " | price=", DoubleToString(price, digits),
         " | TP=", DoubleToString(tp_p, digits),
         " | lot=", DoubleToString(lot, 2));

   return ticket;
}

//+------------------------------------------------------------------+
//| Close an existing trade                                            |
//+------------------------------------------------------------------+
bool CloseTrade(ulong ticket)
{
   g_symbol.Refresh();
   g_symbol.RefreshRates();

   if(!PositionSelectByTicket(ticket))
   {
      Print("[WARN] Ticket ", ticket, " not found in open positions");
      return false;
   }

   ENUM_POSITION_TYPE pos_type = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
   double volume = PositionGetDouble(POSITION_VOLUME);

   ENUM_ORDER_TYPE close_type;
   if(pos_type == POSITION_TYPE_BUY)
   {
      close_type = ORDER_TYPE_SELL;
   }
   else
   {
      close_type = ORDER_TYPE_BUY;
   }

   if(!g_trade.PositionClose(ticket, InpSlippage))
   {
      Print("[ERROR] Close failed ticket=", ticket,
            " retcode=", g_trade.ResultRetcode(),
            " | ", g_trade.ResultRetcodeDescription());
      return false;
   }

   Print("[TRADE] Closed ticket=", ticket,
         " | price=", DoubleToString(g_trade.ResultPrice(), g_symbol.Digits()));
   return true;
}

//+------------------------------------------------------------------+
//| Get total P&L from history deals for a position                   |
//+------------------------------------------------------------------+
double GetDealPnL(ulong pos_ticket)
{
   datetime from = 0;
   datetime to = TimeCurrent();

   if(!HistorySelect(from, to))
      return 0.0;

   double pnl = 0.0;
   int total = HistoryDealsTotal();
   for(int i = total - 1; i >= 0; i--)
   {
      ulong deal_ticket = HistoryDealGetTicket(i);
      if(deal_ticket == 0)
         continue;

      if(HistoryDealGetInteger(deal_ticket, DEAL_POSITION_ID) == (long)pos_ticket)
      {
         double deal_profit = HistoryDealGetDouble(deal_ticket, DEAL_PROFIT);
         double deal_swap = HistoryDealGetDouble(deal_ticket, DEAL_SWAP);
         double deal_comm = HistoryDealGetDouble(deal_ticket, DEAL_COMMISSION);
         double deal_pnl = deal_profit + deal_swap + deal_comm;
         if(deal_pnl != 0.0)
            pnl += deal_pnl;
      }
   }
   return pnl;
}

//+------------------------------------------------------------------+
//| Handle new bar - check signal and manage trades                    |
//+------------------------------------------------------------------+
void OnNewBar()
{
   int signal = DetectSignal();

   if(signal == 0)
      return;

   string label = (signal == 1) ? "BUY" : "SELL";

   ulong existing_ticket;
   int existing_dir;

   bool has_position = GetOpenPosition(existing_ticket, existing_dir);

   if(!has_position)
   {
      // No open trade - enter on signal
      g_open_ticket = 0;
      g_open_direction = 0;
      Print("[SIGNAL] ", label, " - Entering trade");

      ulong ticket = OpenTrade(signal);
      if(ticket > 0)
      {
         g_open_ticket = ticket;
         g_open_direction = signal;
         g_total_trades++;
      }
   }
   else
   {
      // Any signal closes current trade and re-enters
      string old_label = (existing_dir == 1) ? "BUY" : "SELL";
      ulong old_ticket = existing_ticket;

      Print("[SIGNAL] ", label, " - Closing ", old_label, ", opening ", label);

      bool closed = CloseTrade(old_ticket);
      g_open_ticket = 0;
      g_open_direction = 0;

      if(closed)
      {
         // Record P&L from the closed position
         double pnl = 0.0;
         // Retry up to 3 times (MT5 may not have written the deal immediately)
         for(int retry = 0; retry < 3; retry++)
         {
            Sleep(250);
            pnl = GetDealPnL(old_ticket);
            if(pnl != 0.0)
               break;
         }
         RecordPnL(pnl);

         Sleep(500);

         ulong new_ticket = OpenTrade(signal);
         if(new_ticket > 0)
         {
            g_open_ticket = new_ticket;
            g_open_direction = signal;
            g_total_trades++;
         }
      }
      else
      {
         Print("[ERROR] Failed to close ticket=", old_ticket, " -- skipping entry");
      }
   }
}

//+------------------------------------------------------------------+
//| OnTradeTransaction - detect position changes                       |
//+------------------------------------------------------------------+
void OnTradeTransaction(const MqlTradeTransaction &trans,
                        const MqlTradeRequest &request,
                        const MqlTradeResult &result)
{
   if(trans.type == TRADE_TRANSACTION_DEAL_ADD)
   {
      if(g_open_ticket == 0)
         return;

      // Check if this deal relates to our position
      if(HistoryDealSelect(trans.deal))
      {
         ulong pos_id = HistoryDealGetInteger(trans.deal, DEAL_POSITION_ID);
         if(pos_id != g_open_ticket)
            return;

         ENUM_DEAL_ENTRY entry = (ENUM_DEAL_ENTRY)HistoryDealGetInteger(trans.deal, DEAL_ENTRY);
         if(entry != DEAL_ENTRY_OUT && entry != DEAL_ENTRY_INOUT)
            return;

         ENUM_DEAL_REASON reason = (ENUM_DEAL_REASON)HistoryDealGetInteger(trans.deal, DEAL_REASON);

         if(reason == DEAL_REASON_SL || reason == DEAL_REASON_TP)
         {
            string reason_str = (reason == DEAL_REASON_SL) ? "SL" : "TP";
            Print("[", reason_str, " HIT] ticket=", g_open_ticket,
                  " - ready for next signal");

            double deal_profit = HistoryDealGetDouble(trans.deal, DEAL_PROFIT);
            double deal_swap = HistoryDealGetDouble(trans.deal, DEAL_SWAP);
            double deal_comm = HistoryDealGetDouble(trans.deal, DEAL_COMMISSION);
            double pnl = deal_profit + deal_swap + deal_comm;

            // Fallback when history hasn't propagated
            if(pnl == 0.0)
            {
               if(reason_str == "TP" && InpUseTP && InpTP_Dollars > 0)
                  pnl = InpTP_Dollars;
               else if(reason_str == "SL" && InpUseSL && InpSL_Dollars > 0)
                  pnl = -InpSL_Dollars;
            }

            RecordPnL(pnl);
            g_open_ticket = 0;
            g_open_direction = 0;
         }
      }
   }
}
//+------------------------------------------------------------------+
