//+------------------------------------------------------------------+
//|                                       SureForexHedging 1.09a     |
//|            Conservative Low-Drawdown Profile (FINAL FIX).mq4     |
//+------------------------------------------------------------------+
#property strict
#property copyright "© Matt Todorovski 2026"
#property link      "https://www.mql5.com/en/users/bluepanther"
#property description " "
#property description "Original Code: Anton Trefolev (anton.trefolev@gmail.com)"
#property description " "
#property description "Modified and improved using https://z.ai and ChatGPT"
#property description " "
#property description "This EA is licensed for FREE Unlimited Private Use, and has been distributed at Telegram group FREE FOREX ROBOTS for members-only."
#property description " "
#property description "Instructions: any asset, any timeframe."
#property description " "
#property description "Risk Warning! Test this EA on Demo account first. Understand how this EA operates. Trading is risky and not suitable for every investor."

// Подключаем стандартную торговую библиотеку MT5
#include <Trade\Trade.mqh>
CTrade trade;

//--- ВХОДНЫЕ ПАРАМЕТРЫ / INPUT PARAMETERS
input group "=== EMA TREND SETTINGS ===" // EMA Trend Settings
input int ema_fast_period    = 5;   // Fast Moving Average period
input int ema_slow_period    = 50;  // Slow Moving Average period

input group "=== GRID LAYERING ===" // Grid Construction Settings
input double fixed_lot       = 0.01; // Initial trading lot
input double lot_multiplier  = 1.5;  // Lot multiplier for subsequent layers
input int max_layers         = 6;    // Maximum number of open orders per side

input group "=== FIXED PROTECTION (PIPS) ===" // Fixed Protection in Pips
input int TakeProfit_Pips    = 100;   // Take Profit in pips from weighted average price
input int StopLoss_Pips      = 30;  // Stop Loss in pips from the very first order

input group "=== RISK PROTECTION ===" // Capital Drawdown Protection
input double Max_Drawdown_Percent = 15.0; // Max drawdown in % of balance to close all

input group "=== SYNTHETIC EXIT (EQUITY TRAIL) ===" // Virtual Equity Profit Trailing
input double trail_start_amount = 8.0; // Profit in USD to activate equity trailing
input double trail_dist_amount  = 3.0; // Pullback in USD from peak profit to close grid

input group "=== GENERAL ===" // General System Settings
input int initial_magic       = 202605212; // Unique Identification Number (Magic)
input string trade_comment    = "Adv-Grid MT5 Monolith"; // Position comment

//--- GLOBAL VARIABLES / ГЛОБАЛЬНЫЕ ПЕРЕМЕННЫЕ
int g_buy_layers = 0, g_sell_layers = 0;
double g_peak_profit = 0.0;
bool g_trailing_active = false;
int h_ema_fast, h_ema_slow, h_atr, h_rsi; // Хэндлы индикаторов

//+------------------------------------------------------------------+
//| EA Initialization / Инициализация советника                      |
//+------------------------------------------------------------------+
int OnInit() {
   trade.SetExpertMagicNumber(initial_magic);
   
   h_ema_fast = iMA(_Symbol, _Period, ema_fast_period, 0, MODE_EMA, PRICE_CLOSE);
   h_ema_slow = iMA(_Symbol, _Period, ema_slow_period, 0, MODE_EMA, PRICE_CLOSE);
   h_atr      = iATR(_Symbol, _Period, 14);
   h_rsi      = iRSI(_Symbol, _Period, 14, PRICE_CLOSE);
   
   if(h_ema_fast == INVALID_HANDLE || h_ema_slow == INVALID_HANDLE || h_atr == INVALID_HANDLE || h_rsi == INVALID_HANDLE) {
      Print("Error creating indicator handles.");
      return(INIT_FAILED);
   }

   string key = "PEAK_" + IntegerToString(initial_magic);
   if(GlobalVariableCheck(key)) g_peak_profit = GlobalVariableGet(key);
   return(INIT_SUCCEEDED);
}

//+------------------------------------------------------------------+
//| EA Deinitialization / Деинициализация советника                  |
//+------------------------------------------------------------------+
void OnDeinit(const int reason) {
   IndicatorRelease(h_ema_fast);
   IndicatorRelease(h_ema_slow);
   IndicatorRelease(h_atr);
   IndicatorRelease(h_rsi);
}

//+------------------------------------------------------------------+
//| Main Tick Cycle / Основной цикл обработки тиков                 |
//+------------------------------------------------------------------+
void OnTick() {
   ScanBasket();
   if(CheckEquityProtection()) return;
   if(IsTrendExhausted() || IsAccelerationPhase()) return;

   double current_profit = GetFloatingProfit();
   if(current_profit > g_peak_profit) {
      g_peak_profit = current_profit;
      GlobalVariableSet("PEAK_" + IntegerToString(initial_magic), g_peak_profit);
   }

   if(CheckSyntheticExit(current_profit)) return;

   int trend = GetTrend();
   double dist = GetGridDistance();
   double buy_anchor = GetWeightedPrice(POSITION_TYPE_BUY);
   double sell_anchor = GetWeightedPrice(POSITION_TYPE_SELL);
   
   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);

   if(trend == POSITION_TYPE_BUY) {
      if(g_buy_layers == 0 || (g_buy_layers < max_layers && bid <= buy_anchor - dist)) {
         double lot = CalcLot(g_buy_layers);
         if(lot >= SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN)) {
            if(trade.Buy(lot, _Symbol, ask, 0, 0, trade_comment)) { 
               ScanBasket(); 
               ModifyGridOrders(POSITION_TYPE_BUY); 
            }
         }
      }
   }
   else if(trend == POSITION_TYPE_SELL) {
      if(g_sell_layers == 0 || (g_sell_layers < max_layers && ask >= sell_anchor + dist)) {
         double lot = CalcLot(g_sell_layers);
         if(lot >= SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN)) {
            if(trade.Sell(lot, _Symbol, bid, 0, 0, trade_comment)) { 
               ScanBasket(); 
               ModifyGridOrders(POSITION_TYPE_SELL); 
            }
         }
      }
   }
}

//+------------------------------------------------------------------+
//| Modify Sl/TP levels / Модификация уровней SL/TP всей сетки        |
//+------------------------------------------------------------------+
void ModifyGridOrders(ENUM_POSITION_TYPE type) {
   double target_tp = 0, target_sl = 0;
   double avg_price = GetWeightedPrice(type);
   double first_price = GetFirstOrderPrice(type);
   if(avg_price == 0 || first_price == 0) return;

   if(type == POSITION_TYPE_BUY) {
      if(TakeProfit_Pips > 0) target_tp = NormalizeDouble(avg_price + TakeProfit_Pips * _Point, _Digits);
      if(StopLoss_Pips > 0)   target_sl = NormalizeDouble(first_price - StopLoss_Pips * _Point, _Digits);
   } else if(type == POSITION_TYPE_SELL) {
      if(TakeProfit_Pips > 0) target_tp = NormalizeDouble(avg_price - TakeProfit_Pips * _Point, _Digits);
      if(StopLoss_Pips > 0)   target_sl = NormalizeDouble(first_price + StopLoss_Pips * _Point, _Digits);
   }

   for(int i = PositionsTotal() - 1; i >= 0; i--) {
      if(PositionGetSymbol(i) == _Symbol && PositionGetInteger(POSITION_MAGIC) == initial_magic && PositionGetInteger(POSITION_TYPE) == type) {
         if(NormalizeDouble(PositionGetDouble(POSITION_TP), _Digits) != target_tp || NormalizeDouble(PositionGetDouble(POSITION_SL), _Digits) != target_sl) {
            trade.PositionModify(PositionGetInteger(POSITION_TICKET), target_sl, target_tp);
         }
      }
   }
}

//+------------------------------------------------------------------+
//| Technical Math Functions / Математические и технические функции  |
//+------------------------------------------------------------------+
double GetFirstOrderPrice(ENUM_POSITION_TYPE type) {
   datetime first_time = 0; double open_price = 0;
   for(int i = 0; i < PositionsTotal(); i++) {
      if(PositionGetSymbol(i) == _Symbol && PositionGetInteger(POSITION_MAGIC) == initial_magic && PositionGetInteger(POSITION_TYPE) == type) {
         datetime p_time = (datetime)PositionGetInteger(POSITION_TIME);
         if(first_time == 0 || p_time < first_time) { first_time = p_time; open_price = PositionGetDouble(POSITION_PRICE_OPEN); }
      }
   }
   return open_price;
}

double GetWeightedPrice(ENUM_POSITION_TYPE type) {
   double totalLots = 0, weighted = 0;
   for(int i = 0; i < PositionsTotal(); i++) {
      if(PositionGetSymbol(i) == _Symbol && PositionGetInteger(POSITION_MAGIC) == initial_magic && PositionGetInteger(POSITION_TYPE) == type) {
         weighted += PositionGetDouble(POSITION_PRICE_OPEN) * PositionGetDouble(POSITION_VOLUME);
         totalLots += PositionGetDouble(POSITION_VOLUME);
      }
   }
   return (totalLots == 0) ? 0 : (weighted / totalLots);
}

bool CheckEquityProtection() {
   if(Max_Drawdown_Percent <= 0) return false;
   double current_loss = GetFloatingProfit();
   if(current_loss < 0 && MathAbs(current_loss) >= (AccountInfoDouble(ACCOUNT_BALANCE) * Max_Drawdown_Percent / 100.0)) {
      Print("!!! EMERGENCY SYSTEM CLOSING (MT5) !!!"); CloseAll(); return true;
   }
   return false;
}

double CalcLot(int layer_index) {
   double lot = fixed_lot * MathPow(lot_multiplier, layer_index);
   double ema_fast_val = GetMAVal(h_ema_fast, 0);
   double ema_slow_val = GetMAVal(h_ema_slow, 0);
   double trend_strength = MathAbs(ema_fast_val - ema_slow_val) / _Point;
   double trend_factor = 1.0 / (1.0 + trend_strength / 500.0);
   double total_exposure = GetTotalExposure();
   double exposure_brake = 1.0 / (1.0 + total_exposure * 2.0);
   return NormalizeLot(lot * trend_factor * exposure_brake);
}

bool IsAccelerationPhase() {
   double atr0 = GetATRVal(0);
   double atr5 = GetATRVal(5);
   return (atr0 > atr5 * 1.3);
}

double GetTotalExposure() {
   double total = 0;
   for(int i = 0; i < PositionsTotal(); i++) {
      if(PositionGetSymbol(i) == _Symbol && PositionGetInteger(POSITION_MAGIC) == initial_magic) total += PositionGetDouble(POSITION_VOLUME);
   }
   return total;
}

double GetGridDistance() {
   double atr = GetATRVal(0);
   double atr_slow = GetATRVal(10);
   double volatility_expansion = (atr_slow == 0) ? 1.0 : (atr / atr_slow);
   double ema_fast_val = GetMAVal(h_ema_fast, 0);
   double ema_slow_val = GetMAVal(h_ema_slow, 0);
   double trend_power = MathAbs(ema_fast_val - ema_slow_val) / (10.0 * _Point);
   double spacing_multiplier = 1.0 + trend_power;
   double vol_multiplier = MathMax(1.0, volatility_expansion);
   return (atr * 2.0 * spacing_multiplier * vol_multiplier);
}

bool IsTrendExhausted() {
   double rsi = GetRSIVal(0);
   double atr = GetATRVal(0);
   double atr_slow = GetATRVal(10);
   if(rsi > 75.0 || rsi < 25.0) return true;
   return (atr > atr_slow * 1.5);
}

int GetTrend() {
   double fast = GetMAVal(h_ema_fast, 0);
   double slow = GetMAVal(h_ema_slow, 0);
   double diff = fast - slow;
   if(diff > 10 * _Point) return POSITION_TYPE_BUY;
   if(diff < -10 * _Point) return POSITION_TYPE_SELL;
   return -1;
}

bool CheckSyntheticExit(double current_profit) {
   if(current_profit >= trail_start_amount) g_trailing_active = true;
   if(g_trailing_active) {
      if(current_profit < g_peak_profit - trail_dist_amount) {
         CloseAll(); g_peak_profit = 0; g_trailing_active = false;
         GlobalVariableSet("PEAK_" + IntegerToString(initial_magic), 0);
         return true;
      }
   }
   return false;
}

void ScanBasket() {
   g_buy_layers = 0; g_sell_layers = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--) {
      if(PositionGetSymbol(i) == _Symbol && PositionGetInteger(POSITION_MAGIC) == initial_magic) {
         long type = PositionGetInteger(POSITION_TYPE);
         if(type == POSITION_TYPE_BUY) g_buy_layers++;
         else if(type == POSITION_TYPE_SELL) g_sell_layers++;
      }
   }
}

double GetFloatingProfit() {
   double profit = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--) {
      if(PositionGetSymbol(i) == _Symbol && PositionGetInteger(POSITION_MAGIC) == initial_magic) {
         profit += PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
      }
   }
   return profit;
}

void CloseAll() {
   for(int i = PositionsTotal() - 1; i >= 0; i--) {
      if(PositionGetSymbol(i) == _Symbol && PositionGetInteger(POSITION_MAGIC) == initial_magic) {
         trade.PositionClose(PositionGetInteger(POSITION_TICKET));
      }
   }
}

double NormalizeLot(double lot) {
   double min = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double max = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
   double step = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
   lot = MathFloor(lot / step) * step;
   if(lot < min) lot = min; if(lot > max) lot = max;
   return lot;
}

//--- Индикаторные "обертки" для MT5 / Indicator data fetch wrappers
double GetMAVal(int handle, int index) { double buf[]; ArrayResize(buf, 1); CopyBuffer(handle, 0, index, 1, buf); return buf[0]; }
double GetATRVal(int index) { double buf[]; ArrayResize(buf, 1); CopyBuffer(h_atr, 0, index, 1, buf); return buf[0]; }
double GetRSIVal(int index) { double buf[]; ArrayResize(buf, 1); CopyBuffer(h_rsi, 0, index, 1, buf); return buf[0]; }
