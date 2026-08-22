//+------------------------------------------------------------------+
//|                        SMITE EA v1                               |
//|   Safe +0.01/cycle | Trend Follow | Basket Compound $10->$10k     |
//|   All instruments | Long-lasting | Opens immediately             |
//+------------------------------------------------------------------+
#property copyright "SMITE EA v1 - Safe Build"
#property version   "1.03"
#property strict

#include <Trade/Trade.mqh>
#include <Trade/PositionInfo.mqh>

CTrade trade;
CPositionInfo pos;

//================ AUTO-DETECTED FILL MODE =================//
ENUM_ORDER_TYPE_FILLING g_fillMode = ORDER_FILLING_FOK;

//================ INPUTS =================//
input group "=== TRADE EXECUTION ==="
input int               Max_Same_Dir_Trades = 5;      // Max trades in ONE direction
input int               Magic_Number         = 777999;
input int               Seconds_Between_Trades = 3;    // Seconds between new entries

input group "=== SAFE LOT SIZING (+0.01 PER BASKET CYCLE) ==="
input double            Start_Lot            = 0.01;   // Starting lot size (always 0.01)
input double            Lot_Increment        = 0.01;   // Increase by 0.01 after each basket TP
input double            Max_Lot_Cap          = 0.10;   // Maximum lot size (safety cap)
input double            Target_Balance       = 10000;  // Target balance ($10k)

input group "=== SL-AS-PROFIT-LOCK (NEVER CAUSES A LOSS) ==="
input bool              No_Hard_SL           = true;   // NO hard SL on open (basket protects)
input int               Breakeven_Trigger    = 6;      // Move SL to entry+profit after X pips
input int               Breakeven_Plus       = 2;      // Place SL X pips above entry (profit lock)
input bool              Enable_Trailing      = true;   // Enable trailing stop
input int               Trail_Start_Pips     = 8;      // Start trailing after X pips profit
input int               Trail_Step_Pips      = 4;      // Trail step distance (pips)

input group "=== MULTI-LEVEL PROFIT LOCK (LOCKS MORE AS PRICE MOVES) ==="
input bool              Use_Level_Locks      = true;   // Enable multi-level profit locking
input int               Level1_Trigger       = 12;     // Level 1: Lock X pips at 12 pips profit
input int               Level1_Lock          = 5;      // Level 1: Lock amount (pips)
input int               Level2_Trigger       = 20;     // Level 2: Lock X pips at 20 pips profit
input int               Level2_Lock          = 12;     // Level 2: Lock amount (pips)
input int               Level3_Trigger       = 30;     // Level 3: Lock X pips at 30 pips profit
input int               Level3_Lock          = 22;     // Level 3: Lock amount (pips)

input group "=== BASKET TAKE PROFIT (COMPOUND ENGINE) ==="
input bool              Enable_Basket_TP     = true;   // Enable basket TP (close all at target)
input bool              Dynamic_Basket       = true;   // Auto-adjust basket % based on equity stage
input double            Basket_Early_Pct     = 8.0;    // Basket % when equity < $50 (aggressive)
input double            Basket_Mid_Pct       = 5.0;    // Basket % when equity $50-$500
input double            Basket_Late_Pct      = 3.0;    // Basket % when equity > $500 (protect gains)
input double            Basket_Min_Profit    = 0.10;   // Min basket profit in $ before closing

input group "=== DRAWDOWN PROTECTION ==="
input double            Max_Drawdown_Pct     = 25.0;   // Max drawdown % from equity peak (0 = disabled)

//================ GLOBAL VARIABLES =================//
double g_equityHigh     = 0;
double g_startBalance   = 0;
datetime g_lastTradeTime = 0;
bool   g_paused         = false;
int    g_basketCycles   = 0;
double g_currentLot     = 0.01;
int    g_cycleDirection = 0;   // Direction for current cycle (1=BUY, -1=SELL)

//+------------------------------------------------------------------+
//| AUTO-DETECT BROKER'S FILL MODE                                    |
//+------------------------------------------------------------------+
ENUM_ORDER_TYPE_FILLING DetectFillMode()
{
   uint filling = (uint)SymbolInfoInteger(_Symbol, SYMBOL_FILLING_MODE);
   if((filling & SYMBOL_FILLING_FOK) == SYMBOL_FILLING_FOK)
      return ORDER_FILLING_FOK;
   if((filling & SYMBOL_FILLING_IOC) == SYMBOL_FILLING_IOC)
      return ORDER_FILLING_IOC;
   return ORDER_FILLING_FOK;
}

//+------------------------------------------------------------------+
//| GET PIP VALUE FOR CURRENT SYMBOL                                  |
//+------------------------------------------------------------------+
double GetPipValue()
{
   int digits = (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);
   if(digits == 5 || digits == 3)
      return SymbolInfoDouble(_Symbol, SYMBOL_POINT) * 10;
   else
      return SymbolInfoDouble(_Symbol, SYMBOL_POINT);
}

//+------------------------------------------------------------------+
//| GET TREND DIRECTION - Read last 3 candles for the trend            |
//| Returns 1 (BUY trend) or -1 (SELL trend)                          |
//+------------------------------------------------------------------+
int GetTrendDirection()
{
   int bullCount = 0;
   int bearCount = 0;

   // Check last 3 completed candles
   for(int i = 1; i <= 3; i++)
   {
      double cOpen  = iOpen(_Symbol, _Period, i);
      double cClose = iClose(_Symbol, _Period, i);
      if(cClose > cOpen) bullCount++;
      else if(cClose < cOpen) bearCount++;
   }

   // If majority bullish -> BUY, majority bearish -> SELL
   if(bullCount > bearCount) return 1;
   if(bearCount > bullCount) return -1;

   // Equal (e.g. 2 dojis) -> use last candle
   double lastOpen  = iOpen(_Symbol, _Period, 1);
   double lastClose = iClose(_Symbol, _Period, 1);
   if(lastClose > lastOpen) return 1;
   if(lastClose < lastOpen) return -1;

   return 0; // Truly no clear direction
}

//+------------------------------------------------------------------+
//| COUNT POSITIONS BY DIRECTION                                      |
//+------------------------------------------------------------------+
int CountPositions(int direction = 0)
{
   int count = 0;
   for(int i = 0; i < PositionsTotal(); i++)
   {
      pos.SelectByIndex(i);
      if(pos.Symbol() != _Symbol || pos.Magic() != Magic_Number) continue;
      if(direction == 1  && pos.PositionType() != POSITION_TYPE_BUY)  continue;
      if(direction == -1 && pos.PositionType() != POSITION_TYPE_SELL) continue;
      count++;
   }
   return count;
}

//+------------------------------------------------------------------+
//| CALCULATE LOT SIZE - SAFE +0.01 PER CYCLE                         |
//| Lot only increases AFTER a basket TP closes in profit.           |
//| Example: Cycle 1=0.01, Cycle 2=0.02, Cycle 3=0.03 ...           |
//+------------------------------------------------------------------+
double CalculateLot()
{
   double minLot  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double maxLot  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
   double lotStep = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);

   // Use the current lot (increases by Lot_Increment after each basket TP)
   double lot = g_currentLot;

   // Respect broker limits
   lot = MathMax(minLot, lot);
   lot = MathMin(lot, MathMin(Max_Lot_Cap, maxLot));
   lot = MathFloor(lot / lotStep) * lotStep;
   return NormalizeDouble(lot, 2);
}

//+------------------------------------------------------------------+
//| OPEN A SINGLE TRADE (NO HARD SL - BASKET PROTECTS)               |
//+------------------------------------------------------------------+
bool OpenTrade(int direction)
{
   double lot = CalculateLot();
   if(lot <= 0) { Print("ERROR: Lot size is 0"); return false; }

   double pip    = GetPipValue();
   double bid    = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   double ask    = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   int    digits = (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);

   bool result = false;
   string comment = (direction == 1) ? "SMITE-Buy" : "SMITE-Sell";

   // NO hard SL when No_Hard_SL = true
   // SL = 0 means the trade is protected by breakeven/trailing/basket only
   double sl = 0;
   double tp = 0;  // No TP either - basket and trailing handle exits

   if(direction == 1) // BUY
   {
      if(!No_Hard_SL)
         sl = NormalizeDouble(ask - 50 * pip, digits); // Wide emergency SL only

      Print(">>> Opening BUY: Lot=", DoubleToString(lot, 2),
            " Ask=", DoubleToString(ask, digits),
            " SL=", (sl == 0 ? "NONE (basket protects)" : DoubleToString(sl, digits)),
            " TP=NONE (basket + trailing)");

      result = trade.Buy(lot, _Symbol, ask, sl, tp, comment);

      if(result)
         Print(">>> BUY OPENED - Ticket: ", trade.ResultOrder(),
               " | SL will be placed at breakeven once price moves ", Breakeven_Trigger, " pips");
      else
         Print(">>> BUY FAILED - Code: ", trade.ResultRetcode(),
               " | ", trade.ResultRetcodeDescription());
   }
   else // SELL
   {
      if(!No_Hard_SL)
         sl = NormalizeDouble(bid + 50 * pip, digits); // Wide emergency SL only

      Print(">>> Opening SELL: Lot=", DoubleToString(lot, 2),
            " Bid=", DoubleToString(bid, digits),
            " SL=", (sl == 0 ? "NONE (basket protects)" : DoubleToString(sl, digits)),
            " TP=NONE (basket + trailing)");

      result = trade.Sell(lot, _Symbol, bid, sl, tp, comment);

      if(result)
         Print(">>> SELL OPENED - Ticket: ", trade.ResultOrder(),
               " | SL will be placed at breakeven once price moves ", Breakeven_Trigger, " pips");
      else
         Print(">>> SELL FAILED - Code: ", trade.ResultRetcode(),
               " | ", trade.ResultRetcodeDescription());
   }

   if(result)
      g_lastTradeTime = TimeCurrent();

   return result;
}

//+------------------------------------------------------------------+
//| GET TOTAL BASKET PROFIT (all our positions)                        |
//+------------------------------------------------------------------+
double GetBasketProfit()
{
   double total = 0;
   for(int i = 0; i < PositionsTotal(); i++)
   {
      pos.SelectByIndex(i);
      if(pos.Symbol() == _Symbol && pos.Magic() == Magic_Number)
         total += pos.Profit() + pos.Swap() + pos.Commission();
   }
   return total;
}

//+------------------------------------------------------------------+
//| CLOSE ALL OUR POSITIONS                                           |
//+------------------------------------------------------------------+
void CloseAllPositions()
{
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      pos.SelectByIndex(i);
      if(pos.Symbol() == _Symbol && pos.Magic() == Magic_Number)
      {
         trade.PositionClose(pos.Ticket());
         Print("Closed position #", pos.Ticket());
      }
   }
}

//+------------------------------------------------------------------+
//| MANAGE POSITIONS: BREAKEVEN + TRAILING + LEVEL LOCKS              |
//| SL NEVER CAUSES A LOSS - ONLY LOCKS PROFIT                        |
//+------------------------------------------------------------------+
void ManagePositions()
{
   double pip    = GetPipValue();
   int    digits = (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);
   double bid    = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   double ask    = SymbolInfoDouble(_Symbol, SYMBOL_ASK);

   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      pos.SelectByIndex(i);
      if(pos.Symbol() != _Symbol || pos.Magic() != Magic_Number) continue;

      double openPrice = pos.PriceOpen();
      double currentSL = pos.StopLoss();
      long   posType   = pos.PositionType();

      // Calculate profit in pips
      double profitPips = 0;
      if(posType == POSITION_TYPE_BUY)
         profitPips = (bid - openPrice) / pip;
      else
         profitPips = (openPrice - ask) / pip;

      // Skip if trade is not in profit yet (SL stays at 0 or breakeven level)
      if(profitPips <= 0) continue;

      double newSL = 0;
      double bestLock = 0;  // Track the highest profit lock level

      // === STAGE 1: BREAKEVEN (first profit lock) ===
      if(profitPips >= Breakeven_Trigger)
      {
         if(posType == POSITION_TYPE_BUY)
            bestLock = openPrice + Breakeven_Plus * pip;
         else
            bestLock = openPrice - Breakeven_Plus * pip;
      }

      // === STAGE 2: MULTI-LEVEL PROFIT LOCKS ===
      if(Use_Level_Locks)
      {
         // Level 3 (highest lock)
         if(profitPips >= Level3_Trigger)
         {
            if(posType == POSITION_TYPE_BUY)
               newSL = openPrice + Level3_Lock * pip;
            else
               newSL = openPrice - Level3_Lock * pip;
            if(newSL > bestLock) bestLock = newSL;  // BUY: higher is better
            if(posType == POSITION_TYPE_SELL && (bestLock == 0 || newSL < bestLock))
               bestLock = newSL;  // SELL: lower is better
         }

         // Level 2
         if(profitPips >= Level2_Trigger)
         {
            if(posType == POSITION_TYPE_BUY)
               newSL = openPrice + Level2_Lock * pip;
            else
               newSL = openPrice - Level2_Lock * pip;
            // Only use if better than current best
            if(posType == POSITION_TYPE_BUY && newSL > bestLock)
               bestLock = newSL;
            if(posType == POSITION_TYPE_SELL && (bestLock == 0 || newSL < bestLock))
               bestLock = newSL;
         }

         // Level 1
         if(profitPips >= Level1_Trigger)
         {
            if(posType == POSITION_TYPE_BUY)
               newSL = openPrice + Level1_Lock * pip;
            else
               newSL = openPrice - Level1_Lock * pip;
            if(posType == POSITION_TYPE_BUY && newSL > bestLock)
               bestLock = newSL;
            if(posType == POSITION_TYPE_SELL && (bestLock == 0 || newSL < bestLock))
               bestLock = newSL;
         }
      }

      // === STAGE 3: TRAILING STOP (follows price, locks maximum profit) ===
      if(Enable_Trailing && profitPips >= Trail_Start_Pips)
      {
         if(posType == POSITION_TYPE_BUY)
         {
            double trailSL = bid - Trail_Step_Pips * pip;
            if(trailSL > bestLock)
               bestLock = trailSL;
         }
         else
         {
            double trailSL = ask + Trail_Step_Pips * pip;
            if(bestLock == 0 || trailSL < bestLock)
               bestLock = trailSL;
         }
      }

      // === APPLY THE BEST SL (never move SL backward) ===
      if(bestLock > 0)
      {
         bestLock = NormalizeDouble(bestLock, digits);
         bool shouldModify = false;

         if(posType == POSITION_TYPE_BUY)
         {
            // BUY: only move SL up (never down)
            if(currentSL == 0 || currentSL < bestLock)
               shouldModify = true;
         }
         else // SELL
         {
            // SELL: only move SL down (never up)
            if(currentSL == 0 || currentSL > bestLock)
               shouldModify = true;
         }

         if(shouldModify)
         {
            // No TP - let basket handle the close
            if(trade.PositionModify(pos.Ticket(), bestLock, 0))
            {
               double lockedPips = 0;
               if(posType == POSITION_TYPE_BUY)
                  lockedPips = (bestLock - openPrice) / pip;
               else
                  lockedPips = (openPrice - bestLock) / pip;

               Print(">>> PROFIT LOCK: ", (posType == POSITION_TYPE_BUY ? "BUY" : "SELL"),
                     " #", pos.Ticket(),
                     " SL -> ", DoubleToString(bestLock, digits),
                     " | Locked: +", DoubleToString(lockedPips, 1), " pips profit",
                     " | Current: +", DoubleToString(profitPips, 1), " pips");
            }
         }
      }
   }
}

//+------------------------------------------------------------------+
//| GET DYNAMIC BASKET TARGET                                         |
//+------------------------------------------------------------------+
double GetBasketTarget()
{
   double equity = AccountInfoDouble(ACCOUNT_EQUITY);

   if(Dynamic_Basket)
   {
      if(equity < 50)
         return MathMax(equity * (Basket_Early_Pct / 100.0), Basket_Min_Profit);
      else if(equity < 500)
         return MathMax(equity * (Basket_Mid_Pct / 100.0), Basket_Min_Profit);
      else
         return MathMax(equity * (Basket_Late_Pct / 100.0), Basket_Min_Profit);
   }

   return Basket_Min_Profit;
}

//+------------------------------------------------------------------+
//| CHECK BASKET TP (CLOSE ALL IN PROFIT)                             |
//+------------------------------------------------------------------+
void CheckBasketTP()
{
   if(!Enable_Basket_TP) return;

   double equity       = AccountInfoDouble(ACCOUNT_EQUITY);
   double basketProfit = GetBasketProfit();
   double target       = GetBasketTarget();

   if(basketProfit >= target)
   {
      double balanceAfter = equity + basketProfit;
      double growthPct    = ((balanceAfter - g_startBalance) / MathMax(g_startBalance, 0.01)) * 100.0;

      // Record old lot before increasing
      double oldLot = g_currentLot;

      Print("==================================================");
      Print(">>> BASKET TP HIT! Cycle #", g_basketCycles + 1);
      Print(">>> Basket Profit:  $", DoubleToString(basketProfit, 2));
      Print(">>> Target was:     $", DoubleToString(target, 2));
      Print(">>> Balance after:  $", DoubleToString(balanceAfter, 2));
      Print(">>> Total growth:   ", DoubleToString(growthPct, 1), "%");
      Print(">>> Progress:       ", DoubleToString((balanceAfter / Target_Balance) * 100, 1), "% to $10k");
      Print(">>> Lot was:        ", DoubleToString(oldLot, 2));

      CloseAllPositions();
      g_lastTradeTime = TimeCurrent();
      g_basketCycles++;

      // SAFE LOT INCREASE: +0.01 after each successful basket TP
      g_currentLot = oldLot + Lot_Increment;
      double minLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
      double maxLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
      g_currentLot = MathMax(minLot, g_currentLot);
      g_currentLot = MathMin(g_currentLot, MathMin(Max_Lot_Cap, maxLot));

      double lotStep = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
      g_currentLot = MathFloor(g_currentLot / lotStep) * lotStep;
      g_currentLot = NormalizeDouble(g_currentLot, 2);

      Print(">>> Lot NOW:        ", DoubleToString(g_currentLot, 2),
            " (increased by ", DoubleToString(Lot_Increment, 2), ")");
      Print(">>> Starting next cycle... Lot: ", DoubleToString(g_currentLot, 2));
      Print("==================================================");

      // RESET cycle direction for next cycle (will pick fresh trend)
      g_cycleDirection = 0;
   }
}

//+------------------------------------------------------------------+
//| CHECK DRAWDOWN PROTECTION                                         |
//+------------------------------------------------------------------+
void CheckDrawdown()
{
   double equity = AccountInfoDouble(ACCOUNT_EQUITY);
   if(equity > g_equityHigh) g_equityHigh = equity;

   double drawdownPct = ((g_equityHigh - equity) / g_equityHigh) * 100.0;

   if(Max_Drawdown_Pct > 0 && drawdownPct >= Max_Drawdown_Pct)
   {
      Print(">>> DRAWDOWN ALERT: ", DoubleToString(drawdownPct, 1),
            "% (max: ", Max_Drawdown_Pct, "%)");
      Print(">>> Resetting lot to ", DoubleToString(Start_Lot, 2), " for safety");
      g_currentLot = Start_Lot;
      g_cycleDirection = 0;  // Reset direction - fresh start after recovery
      CloseAllPositions();
      g_equityHigh = equity;
      g_paused = true;
      Print(">>> TRADING PAUSED. Will resume when equity recovers.");
   }
}

//+------------------------------------------------------------------+
//| TRY UNPAUSE                                                       |
//+------------------------------------------------------------------+
bool TryUnpause()
{
   if(!g_paused) return true;
   double equity = AccountInfoDouble(ACCOUNT_EQUITY);
   if(equity >= g_equityHigh * 0.90)
   {
      g_paused = false;
      Print(">>> TRADING RESUMED - Equity: $", DoubleToString(equity, 2));
      return true;
   }
   return false;
}

//+------------------------------------------------------------------+
//| INITIALIZATION                                                    |
//+------------------------------------------------------------------+
int OnInit()
{
   g_fillMode = DetectFillMode();

   trade.SetExpertMagicNumber(Magic_Number);
   trade.SetDeviationInPoints(50);
   trade.SetTypeFilling(g_fillMode);

   g_equityHigh   = AccountInfoDouble(ACCOUNT_EQUITY);
   g_startBalance = AccountInfoDouble(ACCOUNT_BALANCE);
   g_currentLot   = Start_Lot;  // Initialize lot from input

   Print("====================================================");
   Print("      SMITE EA v1 - SAFE BUILD");
   Print("====================================================");
   Print("Balance:       $", DoubleToString(g_startBalance, 2));
   Print("Equity:        $", DoubleToString(g_equityHigh, 2));
   Print("Target:        $", DoubleToString(Target_Balance, 2));
   Print("Starting Lot:  ", DoubleToString(g_currentLot, 2));
   Print("Lot Increase:  +", DoubleToString(Lot_Increment, 2), " per basket cycle");
   Print("Max Lot Cap:   ", DoubleToString(Max_Lot_Cap, 2));
   Print("Hard SL:       NONE (basket + profit-lock protects)");
   Print("Breakeven:     ", Breakeven_Trigger, " pips trigger, +", Breakeven_Plus, " pips lock");
   Print("Trailing:      ", Enable_Trailing ? "Yes (" + IntegerToString(Trail_Start_Pips) + " pip start)" : "No");
   Print("Level Locks:   ", Use_Level_Locks ? "L1=" + IntegerToString(Level1_Lock) + " L2=" + IntegerToString(Level2_Lock) + " L3=" + IntegerToString(Level3_Lock) + " pips" : "No");
   Print("Max Trades:     ", Max_Same_Dir_Trades, " in ONE direction per cycle");
   Print("Basket TP:     DYNAMIC");
   Print("  Early(<$50):    ", Basket_Early_Pct, "%");
   Print("  Mid($50-$500):  ", Basket_Mid_Pct, "%");
   Print("  Late(>$500):    ", Basket_Late_Pct, "%");
   Print("Basket SL:      REMOVED (no loss-locking)");
   Print("Fill Mode:     ", EnumToString(g_fillMode));
   Print("Instrument:    ", _Symbol, " (", _Period, ")");
   Print("====================================================");
   Print(">>> SL NEVER CAUSES A LOSS - ONLY LOCKS PROFIT");
   Print(">>> FOLLOWS TREND: Opens in ONE direction per cycle");
   Print(">>> BASKET CLOSES ALL WHEN GROUP PROFIT HITS TARGET");
   Print(">>> NO BASKET SL - TRADES GIVEN ROOM TO RECOVER");
   Print(">>> SAFE LOT: +0.01 PER CYCLE (MAX CAP 0.10)");
   Print(">>> READY - FIRST CYCLE STARTS ON NEXT TICK");
   Print("====================================================");

   return INIT_SUCCEEDED;
}

//+------------------------------------------------------------------+
//| DEINITIALIZATION                                                  |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   double equity = AccountInfoDouble(ACCOUNT_EQUITY);
   double growth = ((equity - g_startBalance) / MathMax(g_startBalance, 0.01)) * 100.0;
   Print(">>> SMITE EA v1 REMOVED | Equity: $",
         DoubleToString(equity, 2), " | Growth: ",
         DoubleToString(growth, 1), "% | Cycles: ", g_basketCycles);
}

//+------------------------------------------------------------------+
//| TRADE EVENT LOG                                                   |
//+------------------------------------------------------------------+
void OnTradeTransaction(const MqlTradeTransaction& trans,
                        const MqlTradeRequest& request,
                        const MqlTradeResult& result)
{
   if(trans.type == TRADE_TRANSACTION_DEAL_ADD)
   {
      double equity = AccountInfoDouble(ACCOUNT_EQUITY);
      double basket = GetBasketProfit();

      Print("=== TRADE EVENT ===");
      Print("Balance:  $", DoubleToString(AccountInfoDouble(ACCOUNT_BALANCE), 2));
      Print("Equity:   $", DoubleToString(equity, 2),
            " (", DoubleToString(((equity - g_startBalance) / MathMax(g_startBalance, 0.01)) * 100, 1), "% from start)");
      Print("Basket:   $", DoubleToString(basket, 2), " floating");
      Print("Open:     ", CountPositions(), " trades");
      Print("Cycles:   ", g_basketCycles, " completed");
      Print("Target:   ", DoubleToString((equity / Target_Balance) * 100, 1), "% complete");
      Print("==================");

      if(equity >= Target_Balance)
      {
         Print("*************************************************");
         Print("  *** $10,000 TARGET REACHED! ***");
         Print("  Final Equity: $", DoubleToString(equity, 2));
         Print("  Cycles used:  ", g_basketCycles);
         Print("*************************************************");
      }
   }
}

//+------------------------------------------------------------------+
//| MAIN TICK FUNCTION                                                |
//+------------------------------------------------------------------+
void OnTick()
{
   if(g_paused)
   {
      TryUnpause();
      return;
   }

   // Step 1: Basket TP - close all if group profit hits target
   CheckBasketTP();

   // Step 2: Drawdown protection
   CheckDrawdown();

   // Step 3: Manage positions (breakeven + level locks + trailing)
   ManagePositions();

   // Step 4: Check if we can open more trades in this cycle's direction
   if(CountPositions(g_cycleDirection) >= Max_Same_Dir_Trades) return;

   // Step 5: Time gate
   if(Seconds_Between_Trades > 0)
   {
      if((int)(TimeCurrent() - g_lastTradeTime) < Seconds_Between_Trades)
         return;
   }

   // Step 6: Determine cycle direction (only once per cycle)
   if(g_cycleDirection == 0)
   {
      int trend = GetTrendDirection();
      if(trend != 0)
      {
         g_cycleDirection = trend;
         Print(">>> CYCLE #", g_basketCycles + 1, " DIRECTION: ",
               (trend == 1 ? "BUY (bullish trend)" : "SELL (bearish trend)"));
      }
   }

   // Step 7: Open trades ONLY in the cycle direction
   if(g_cycleDirection != 0)
   {
      OpenTrade(g_cycleDirection);
   }
}
//+------------------------------------------------------------------+
