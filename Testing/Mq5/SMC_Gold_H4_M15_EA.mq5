//+------------------------------------------------------------------+
//|                    SMC_Gold_H4_M15_EA.mq5                        |
//|              Smart Money Concepts — H4 Bias / M15 Entry          |
//|              XAUUSD Optimized — BOS + OB + FVG + OTE             |
//+------------------------------------------------------------------+
#property copyright "SMC Gold EA"
#property version   "1.00"
#property strict

#include <Trade\Trade.mqh>
CTrade trade;

//--- Input Parameters
input group "=== RISK MANAGEMENT ==="
input double   RiskPercent       = 1.0;      // Risk % per trade
input double   RR_Ratio          = 2.0;      // Reward:Risk ratio
input int      MaxOpenTrades     = 1;         // Max concurrent trades

input group "=== SMC STRUCTURE ==="
input int      SwingLookback     = 10;        // Swing High/Low lookback bars
input int      OB_Lookback       = 50;        // Order Block search range (M15 bars)
input int      FVG_Lookback      = 30;        // FVG search range (M15 bars)
input double   OTE_Min           = 0.618;     // OTE Fib minimum (61.8%)
input double   OTE_Max           = 0.79;      // OTE Fib maximum (79%)

input group "=== KILL ZONE (UTC+0) ==="
input int      LondonStartHour   = 7;         // London Kill Zone Start (UTC)
input int      LondonEndHour     = 10;        // London Kill Zone End (UTC)
input int      NYStartHour       = 12;        // NY Kill Zone Start (UTC)
input int      NYEndHour         = 15;        // NY Kill Zone End (UTC)

input group "=== ATR FILTER ==="
input int      ATR_Period        = 14;        // ATR Period
input double   ATR_Multiplier    = 1.5;       // SL = ATR x Multiplier

input group "=== SYMBOL ==="
input string   TradeSymbol       = "XAUUSD";  // Symbol to trade

//--- Global Variables
double   g_h4_bias       = 0;   // 1=Bullish, -1=Bearish, 0=Neutral
double   g_ob_high       = 0;
double   g_ob_low        = 0;
bool     g_ob_bullish    = false;
double   g_fvg_high      = 0;
double   g_fvg_low       = 0;
bool     g_fvg_bullish   = false;
double   g_swing_high    = 0;
double   g_swing_low     = 0;
datetime g_last_bar_time = 0;

//+------------------------------------------------------------------+
//| Expert initialization                                            |
//+------------------------------------------------------------------+
int OnInit()
{
   trade.SetExpertMagicNumber(20250001);
   trade.SetDeviationInPoints(30);
   Print("SMC Gold EA Initialized | Symbol: ", TradeSymbol);
   return INIT_SUCCEEDED;
}

//+------------------------------------------------------------------+
//| Expert tick function                                             |
//+------------------------------------------------------------------+
void OnTick()
{
   // Only run on new M15 bar
   datetime current_bar = iTime(TradeSymbol, PERIOD_M15, 0);
   if(current_bar == g_last_bar_time) return;
   g_last_bar_time = current_bar;

   // Skip if max trades reached
   if(CountOpenTrades() >= MaxOpenTrades) return;

   // Skip if not in kill zone
   if(!InKillZone()) return;

   // Step 1: Get H4 Bias
   GetH4Bias();
   if(g_h4_bias == 0) return;  // No clear bias

   // Step 2: Find Order Block on M15
   FindOrderBlock();

   // Step 3: Find FVG on M15
   FindFVG();

   // Step 4: Check ChoCH on M15
   bool choChBull = CheckChoCH_Bullish();
   bool choChBear = CheckChoCH_Bearish();

   // Step 5: Check OTE
   double current_price = SymbolInfoDouble(TradeSymbol, SYMBOL_BID);

   // === LONG SETUP ===
   if(g_h4_bias == 1 && choChBull)
   {
      // Check if price is in OB or FVG zone
      bool inBullOB  = (g_ob_bullish  && current_price >= g_ob_low  && current_price <= g_ob_high);
      bool inBullFVG = (g_fvg_bullish && current_price >= g_fvg_low && current_price <= g_fvg_high);

      if(inBullOB || inBullFVG)
      {
         // Check OTE level
         if(CheckOTE_Long())
         {
            ExecuteLong();
         }
      }
   }

   // === SHORT SETUP ===
   if(g_h4_bias == -1 && choChBear)
   {
      bool inBearOB  = (!g_ob_bullish  && current_price >= g_ob_low  && current_price <= g_ob_high);
      bool inBearFVG = (!g_fvg_bullish && current_price >= g_fvg_low && current_price <= g_fvg_high);

      if(inBearOB || inBearFVG)
      {
         if(CheckOTE_Short())
         {
            ExecuteShort();
         }
      }
   }
}

//+------------------------------------------------------------------+
//| H4 BIAS — Higher Highs / Higher Lows or Lower Highs / Lower Lows|
//+------------------------------------------------------------------+
void GetH4Bias()
{
   double hh1 = iHigh(TradeSymbol, PERIOD_H4, 1);
   double hh2 = iHigh(TradeSymbol, PERIOD_H4, 2);
   double hh3 = iHigh(TradeSymbol, PERIOD_H4, 3);
   double ll1 = iLow(TradeSymbol,  PERIOD_H4, 1);
   double ll2 = iLow(TradeSymbol,  PERIOD_H4, 2);
   double ll3 = iLow(TradeSymbol,  PERIOD_H4, 3);

   // Bullish: Higher Highs + Higher Lows
   if(hh1 > hh2 && hh2 > hh3 && ll1 > ll2 && ll2 > ll3)
      g_h4_bias = 1;
   // Bearish: Lower Highs + Lower Lows
   else if(hh1 < hh2 && hh2 < hh3 && ll1 < ll2 && ll2 < ll3)
      g_h4_bias = -1;
   else
      g_h4_bias = 0;
}

//+------------------------------------------------------------------+
//| FIND ORDER BLOCK on M15                                          |
//| Last bearish candle before bullish impulse (bull OB)             |
//| Last bullish candle before bearish impulse (bear OB)             |
//+------------------------------------------------------------------+
void FindOrderBlock()
{
   g_ob_high = 0; g_ob_low = 0;

   for(int i = 2; i < OB_Lookback; i++)
   {
      double open_i  = iOpen(TradeSymbol,  PERIOD_M15, i);
      double close_i = iClose(TradeSymbol, PERIOD_M15, i);
      double high_i  = iHigh(TradeSymbol,  PERIOD_M15, i);
      double low_i   = iLow(TradeSymbol,   PERIOD_M15, i);
      double close_1 = iClose(TradeSymbol, PERIOD_M15, i-1);
      double close_2 = iClose(TradeSymbol, PERIOD_M15, i-2);

      // Bullish OB: bearish candle followed by 2 bullish candles
      if(g_h4_bias == 1 && close_i < open_i &&
         close_1 > iOpen(TradeSymbol, PERIOD_M15, i-1) &&
         close_2 > iOpen(TradeSymbol, PERIOD_M15, i-2))
      {
         g_ob_high    = high_i;
         g_ob_low     = low_i;
         g_ob_bullish = true;
         break;
      }

      // Bearish OB: bullish candle followed by 2 bearish candles
      if(g_h4_bias == -1 && close_i > open_i &&
         close_1 < iOpen(TradeSymbol, PERIOD_M15, i-1) &&
         close_2 < iOpen(TradeSymbol, PERIOD_M15, i-2))
      {
         g_ob_high    = high_i;
         g_ob_low     = low_i;
         g_ob_bullish = false;
         break;
      }
   }
}

//+------------------------------------------------------------------+
//| FIND FAIR VALUE GAP (FVG) on M15                                 |
//| 3-candle pattern: gap between candle[i].high and candle[i+2].low |
//+------------------------------------------------------------------+
void FindFVG()
{
   g_fvg_high = 0; g_fvg_low = 0;

   for(int i = 3; i < FVG_Lookback; i++)
   {
      double high_prev = iHigh(TradeSymbol, PERIOD_M15, i);    // candle i
      double low_next  = iLow(TradeSymbol,  PERIOD_M15, i-2);  // candle i-2
      double low_prev  = iLow(TradeSymbol,  PERIOD_M15, i);
      double high_next = iHigh(TradeSymbol, PERIOD_M15, i-2);

      // Bullish FVG: gap up (candle[i].high < candle[i-2].low)
      if(g_h4_bias == 1 && high_prev < low_next)
      {
         g_fvg_low     = high_prev;
         g_fvg_high    = low_next;
         g_fvg_bullish = true;
         break;
      }

      // Bearish FVG: gap down (candle[i].low > candle[i-2].high)
      if(g_h4_bias == -1 && low_prev > high_next)
      {
         g_fvg_low     = high_next;
         g_fvg_high    = low_prev;
         g_fvg_bullish = false;
         break;
      }
   }
}

//+------------------------------------------------------------------+
//| ChoCH — Change of Character (Bullish)                            |
//| Price sweeps a swing low then closes above swing high            |
//+------------------------------------------------------------------+
bool CheckChoCH_Bullish()
{
   double prev_low  = iLow(TradeSymbol,  PERIOD_M15, 2);
   double prev_high = iHigh(TradeSymbol, PERIOD_M15, 2);
   double curr_close = iClose(TradeSymbol, PERIOD_M15, 1);
   double curr_low   = iLow(TradeSymbol,  PERIOD_M15, 1);

   // Wicked below previous low then closed above it = bullish ChoCH
   if(curr_low < prev_low && curr_close > prev_low) return true;
   return false;
}

//+------------------------------------------------------------------+
//| ChoCH — Change of Character (Bearish)                            |
//+------------------------------------------------------------------+
bool CheckChoCH_Bearish()
{
   double prev_high  = iHigh(TradeSymbol, PERIOD_M15, 2);
   double curr_close = iClose(TradeSymbol, PERIOD_M15, 1);
   double curr_high  = iHigh(TradeSymbol, PERIOD_M15, 1);

   // Wicked above previous high then closed below it = bearish ChoCH
   if(curr_high > prev_high && curr_close < prev_high) return true;
   return false;
}

//+------------------------------------------------------------------+
//| OTE CHECK — Long (price retraced to 61.8–79% of last swing)      |
//+------------------------------------------------------------------+
bool CheckOTE_Long()
{
   // Find recent swing low and swing high
   double swHigh = 0, swLow = 999999;
   for(int i = 1; i <= SwingLookback; i++)
   {
      double h = iHigh(TradeSymbol, PERIOD_M15, i);
      double l = iLow(TradeSymbol,  PERIOD_M15, i);
      if(h > swHigh) swHigh = h;
      if(l < swLow)  swLow  = l;
   }
   double range = swHigh - swLow;
   if(range <= 0) return false;

   double price  = SymbolInfoDouble(TradeSymbol, SYMBOL_BID);
   double retrace = (swHigh - price) / range;

   return (retrace >= OTE_Min && retrace <= OTE_Max);
}

//+------------------------------------------------------------------+
//| OTE CHECK — Short                                                |
//+------------------------------------------------------------------+
bool CheckOTE_Short()
{
   double swHigh = 0, swLow = 999999;
   for(int i = 1; i <= SwingLookback; i++)
   {
      double h = iHigh(TradeSymbol, PERIOD_M15, i);
      double l = iLow(TradeSymbol,  PERIOD_M15, i);
      if(h > swHigh) swHigh = h;
      if(l < swLow)  swLow  = l;
   }
   double range = swHigh - swLow;
   if(range <= 0) return false;

   double price  = SymbolInfoDouble(TradeSymbol, SYMBOL_ASK);
   double retrace = (price - swLow) / range;

   return (retrace >= OTE_Min && retrace <= OTE_Max);
}

//+------------------------------------------------------------------+
//| KILL ZONE CHECK                                                  |
//+------------------------------------------------------------------+
bool InKillZone()
{
   MqlDateTime dt;
   TimeToStruct(TimeGMT(), dt);
   int hour = dt.hour;

   bool london = (hour >= LondonStartHour && hour < LondonEndHour);
   bool ny     = (hour >= NYStartHour     && hour < NYEndHour);
   return (london || ny);
}

//+------------------------------------------------------------------+
//| EXECUTE LONG TRADE                                               |
//+------------------------------------------------------------------+
void ExecuteLong()
{
   double ask = SymbolInfoDouble(TradeSymbol, SYMBOL_ASK);
   double atr = GetATR();
   double sl  = ask - (atr * ATR_Multiplier);
   double tp  = ask + (atr * ATR_Multiplier * RR_Ratio);
   double lot = CalcLotSize(ask - sl);

   if(lot <= 0) return;

   sl = NormalizeDouble(sl, (int)SymbolInfoInteger(TradeSymbol, SYMBOL_DIGITS));
   tp = NormalizeDouble(tp, (int)SymbolInfoInteger(TradeSymbol, SYMBOL_DIGITS));

   if(trade.Buy(lot, TradeSymbol, ask, sl, tp, "SMC_Long_H4M15"))
      Print("LONG executed | Lot:", lot, " SL:", sl, " TP:", tp);
   else
      Print("LONG failed | Error:", GetLastError());
}

//+------------------------------------------------------------------+
//| EXECUTE SHORT TRADE                                              |
//+------------------------------------------------------------------+
void ExecuteShort()
{
   double bid = SymbolInfoDouble(TradeSymbol, SYMBOL_BID);
   double atr = GetATR();
   double sl  = bid + (atr * ATR_Multiplier);
   double tp  = bid - (atr * ATR_Multiplier * RR_Ratio);
   double lot = CalcLotSize(sl - bid);

   if(lot <= 0) return;

   sl = NormalizeDouble(sl, (int)SymbolInfoInteger(TradeSymbol, SYMBOL_DIGITS));
   tp = NormalizeDouble(tp, (int)SymbolInfoInteger(TradeSymbol, SYMBOL_DIGITS));

   if(trade.Sell(lot, TradeSymbol, bid, sl, tp, "SMC_Short_H4M15"))
      Print("SHORT executed | Lot:", lot, " SL:", sl, " TP:", tp);
   else
      Print("SHORT failed | Error:", GetLastError());
}

//+------------------------------------------------------------------+
//| ATR VALUE                                                        |
//+------------------------------------------------------------------+
double GetATR()
{
   int handle = iATR(TradeSymbol, PERIOD_M15, ATR_Period);
   if(handle == INVALID_HANDLE) return 1.0;
   double atr[1];
   if(CopyBuffer(handle, 0, 1, 1, atr) < 1) return 1.0;
   IndicatorRelease(handle);
   return atr[0];
}

//+------------------------------------------------------------------+
//| LOT SIZE CALCULATOR                                              |
//+------------------------------------------------------------------+
double CalcLotSize(double sl_distance)
{
   if(sl_distance <= 0) return 0;

   double balance    = AccountInfoDouble(ACCOUNT_BALANCE);
   double risk_money = balance * (RiskPercent / 100.0);
   double tick_value = SymbolInfoDouble(TradeSymbol, SYMBOL_TRADE_TICK_VALUE);
   double tick_size  = SymbolInfoDouble(TradeSymbol, SYMBOL_TRADE_TICK_SIZE);

   if(tick_size == 0 || tick_value == 0) return 0;

   double lot = risk_money / ((sl_distance / tick_size) * tick_value);

   double min_lot  = SymbolInfoDouble(TradeSymbol, SYMBOL_VOLUME_MIN);
   double max_lot  = SymbolInfoDouble(TradeSymbol, SYMBOL_VOLUME_MAX);
   double lot_step = SymbolInfoDouble(TradeSymbol, SYMBOL_VOLUME_STEP);

   lot = MathMax(min_lot, MathMin(max_lot, MathRound(lot / lot_step) * lot_step));
   return lot;
}

//+------------------------------------------------------------------+
//| COUNT OPEN TRADES                                                |
//+------------------------------------------------------------------+
int CountOpenTrades()
{
   int count = 0;
   for(int i = 0; i < PositionsTotal(); i++)
   {
      if(PositionGetSymbol(i) == TradeSymbol &&
         PositionGetInteger(POSITION_MAGIC) == 20250001)
         count++;
   }
   return count;
}
//+------------------------------------------------------------------+
