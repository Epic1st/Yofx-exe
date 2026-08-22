//+------------------------------------------------------------------+
//| Grid Scalper LDA - Fast Execution + Protection                  |
//| ⚠️ EXACT SAME TRADING LOGIC AS ORIGINAL - NEVER CHANGED ⚠️      |
//+------------------------------------------------------------------+
#property strict
#property version "2.2"

#include <Trade/Trade.mqh>
CTrade trade;

//+------------------------------------------------------------------+
//| 🎮 BASIC SETTINGS - Your Bot Identity                           |
//+------------------------------------------------------------------+
input string ___1___ = "════════ 🎮 BOT IDENTITY ════════";
input long   Magic_Base             = 333;          // 🔢 Magic Number (Your bot's unique ID)
input string EA_Comment             = "LDA_Scalper";// 📝 Trade Comment (Shows in history)

//+------------------------------------------------------------------+
//| 💰 MONEY SETTINGS - How Much to Trade                           |
//+------------------------------------------------------------------+
input string ___2___ = "════════ 💰 MONEY SETTINGS ════════";
input double ScalpingLotSize        = 0.1;          // 📦 Lot Size (0.1 = $1 per pip Gold)
input int    MaxScalpPositions      = 5;            // 🎯 Max Open Trades (How many at same time)

//+------------------------------------------------------------------+
//| 🎯 PROFIT TARGET - When to Take Profit                          |
//+------------------------------------------------------------------+
input string ___3___ = "════════ 🎯 PROFIT TARGET ════════";
input int    ScalpingTargetPips     = 3;            // 💵 Take Profit Pips (Close at X pips profit)
input int    PipMultiplier          = 10;           // 🔧 Pip Calculator (Gold=10, Don't change!)

//+------------------------------------------------------------------+
//| ⚡ SPEED SETTINGS - FAST SCALPING                                |
//+------------------------------------------------------------------+
input string ___4___ = "════════ ⚡ SPEED SETTINGS ════════";
input int    EntryDelaySeconds      = 5;            // ⏰ Seconds Between Trades (5=fast, 20=slow)
input bool   EnableScalping         = true;         // ▶️ Enable Trading (ON/OFF)

//+------------------------------------------------------------------+
//| 🛡️ ACCOUNT PROTECTION - Keep Money Safe                         |
//+------------------------------------------------------------------+
input string ___5___ = "════════ 🛡️ PROTECTION ════════";
input bool   EnableProtection       = true;         // 🛡️ Enable Protection (ON=safe, OFF=no limit)
input double MaxDrawdownPercent     = 10.0;         // 📉 Max DD % (Stop if account drops X%)
input double MaxDrawdownMoney       = 0;            // 💸 Max DD $ (Stop at $X loss. 0=off)
input double DailyProfitTarget      = 0;            // 🎉 Daily Goal $ (Stop at $X profit. 0=off)
input double DailyLossLimit         = 0;            // 😢 Daily Loss $ (Stop at $X loss. 0=off)

//+------------------------------------------------------------------+
//| ⚡ GAP PROTECTION                                                |
//+------------------------------------------------------------------+
input string ___6___ = "════════ ⚡ GAP PROTECTION ════════";
input bool   EnableGapProtection    = false;        // ⚡ Gap Protection (Pause on big jumps)
input int    MaxGapPips             = 30;           // 📊 Max Gap Pips (Pause if gap > X)
input int    GapPauseSeconds        = 30;           // ⏸️ Pause Seconds (Wait after gap)

//+------------------------------------------------------------------+
//| ⏰ TRADING HOURS                                                 |
//+------------------------------------------------------------------+
input string ___7___ = "════════ ⏰ TRADING HOURS ════════";
input bool   Bat_Bot                = true;         // 🤖 Bot ON/OFF (Master switch)
input bool   Bat_Tat_Theo_Gio       = true;         // 📅 Use Time Filter (ON=set hours only)
input string Khung1_Bat             = "01:10";      // ▶️ Session 1 Start (HH:MM)
input string Khung1_Tat             = "18:00";      // ⏹️ Session 1 End (HH:MM)
input string Khung2_Bat             = "00:00";      // ▶️ Session 2 Start (00:00=off)
input string Khung2_Tat             = "00:00";      // ⏹️ Session 2 End (00:00=off)

//+------------------------------------------------------------------+
//| 📊 DISPLAY                                                       |
//+------------------------------------------------------------------+
input string ___8___ = "════════ 📊 DISPLAY ════════";
input bool   ShowDashboard          = true;         // 📺 Show Panel (Stats on chart)

//+------------------------------------------------------------------+
//| GLOBAL VARIABLES - SAME AS ORIGINAL                             |
//+------------------------------------------------------------------+
double g_last_entry_price = 0.0;
datetime g_last_scalp_time = 0;
double g_pip_size = 0;
double g_last_bid = 0;
datetime g_gap_pause_until = 0;
double g_day_start_balance = 0;
datetime g_last_day = 0;
bool g_daily_target_hit = false;
bool g_daily_loss_hit = false;
bool g_drawdown_hit = false;

//+------------------------------------------------------------------+
//| Normalize Lot - SAME AS ORIGINAL                                |
//+------------------------------------------------------------------+
double NormalizeLot(double lot)
{
   double minLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double maxLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
   double step   = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);

   if(step <= 0) step = 0.01;
   
   lot = MathFloor(lot/step + 0.5) * step;
   lot = MathMax(minLot, MathMin(lot, maxLot));

   return NormalizeDouble(lot, 2);
}

//+------------------------------------------------------------------+
//| Get Pip Size - SAME AS ORIGINAL                                 |
//+------------------------------------------------------------------+
double GetPipSize()
{
   double point = SymbolInfoDouble(_Symbol, SYMBOL_POINT);
   return point * PipMultiplier;
}

//+------------------------------------------------------------------+
//| Get Spread Pips                                                 |
//+------------------------------------------------------------------+
double GetSpreadPips()
{
   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   return (ask - bid) / g_pip_size;
}

//+------------------------------------------------------------------+
//| Check Session - SAME AS ORIGINAL                                |
//+------------------------------------------------------------------+
bool IsSessionActive()
{
   if(!Bat_Tat_Theo_Gio) return true;
   
   datetime currentTime = TimeCurrent();
   MqlDateTime now;
   TimeToStruct(currentTime, now);
   
   int currentMinutes = now.hour * 60 + now.min;
   
   int start1 = StringToInteger(StringSubstr(Khung1_Bat, 0, 2)) * 60 + 
                StringToInteger(StringSubstr(Khung1_Bat, 3, 2));
   int end1   = StringToInteger(StringSubstr(Khung1_Tat, 0, 2)) * 60 + 
                StringToInteger(StringSubstr(Khung1_Tat, 3, 2));
   int start2 = StringToInteger(StringSubstr(Khung2_Bat, 0, 2)) * 60 + 
                StringToInteger(StringSubstr(Khung2_Bat, 3, 2));
   int end2   = StringToInteger(StringSubstr(Khung2_Tat, 0, 2)) * 60 + 
                StringToInteger(StringSubstr(Khung2_Tat, 3, 2));

   if(start1 < end1)
   {
      if(currentMinutes >= start1 && currentMinutes <= end1) return true;
   }

   if(start2 != end2 && start2 < end2)
   {
      if(currentMinutes >= start2 && currentMinutes <= end2) return true;
   }

   return false;
}

//+------------------------------------------------------------------+
//| Count Positions - SAME AS ORIGINAL                              |
//+------------------------------------------------------------------+
int CountMyPositions()
{
   int count = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;
      
      if(PositionGetString(POSITION_SYMBOL) == _Symbol && 
         PositionGetInteger(POSITION_MAGIC) == Magic_Base)
      {
         count++;
      }
   }
   return count;
}

//+------------------------------------------------------------------+
//| Get Floating P/L                                                |
//+------------------------------------------------------------------+
double GetFloatingPL()
{
   double totalPL = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;
      
      if(PositionGetString(POSITION_SYMBOL) == _Symbol && 
         PositionGetInteger(POSITION_MAGIC) == Magic_Base)
      {
         totalPL += PositionGetDouble(POSITION_PROFIT);
      }
   }
   return totalPL;
}

//+------------------------------------------------------------------+
//| Check New Day                                                   |
//+------------------------------------------------------------------+
void CheckNewDay()
{
   MqlDateTime now;
   TimeToStruct(TimeCurrent(), now);
   datetime today = StringToTime(IntegerToString(now.year) + "." + 
                                  IntegerToString(now.mon) + "." + 
                                  IntegerToString(now.day));
   
   if(today != g_last_day)
   {
      g_last_day = today;
      g_day_start_balance = AccountInfoDouble(ACCOUNT_BALANCE);
      g_daily_target_hit = false;
      g_daily_loss_hit = false;
      Print("📅 New Day! Balance: $", DoubleToString(g_day_start_balance, 2));
   }
}

//+------------------------------------------------------------------+
//| Gap Check                                                       |
//+------------------------------------------------------------------+
bool CheckForGap()
{
   if(!EnableGapProtection) return false;
   
   double currentBid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   
   if(g_last_bid > 0)
   {
      double gapPips = MathAbs(currentBid - g_last_bid) / g_pip_size;
      
      if(gapPips >= MaxGapPips)
      {
         g_gap_pause_until = TimeCurrent() + GapPauseSeconds;
         Print("⚡ GAP: ", DoubleToString(gapPips, 1), " pips!");
         g_last_bid = currentBid;
         return true;
      }
   }
   
   g_last_bid = currentBid;
   return false;
}

//+------------------------------------------------------------------+
//| Drawdown Check                                                  |
//+------------------------------------------------------------------+
bool IsDrawdownSafe()
{
   if(!EnableProtection) return true;
   
   double balance = AccountInfoDouble(ACCOUNT_BALANCE);
   double equity = AccountInfoDouble(ACCOUNT_EQUITY);
   
   if(MaxDrawdownPercent > 0 && balance > 0)
   {
      double ddPercent = ((balance - equity) / balance) * 100.0;
      if(ddPercent >= MaxDrawdownPercent)
      {
         if(!g_drawdown_hit) { Print("🛑 DD LIMIT: ", DoubleToString(ddPercent, 1), "%"); g_drawdown_hit = true; }
         return false;
      }
   }
   
   if(MaxDrawdownMoney > 0)
   {
      double ddMoney = balance - equity;
      if(ddMoney >= MaxDrawdownMoney)
      {
         if(!g_drawdown_hit) { Print("🛑 DD LIMIT: $", DoubleToString(ddMoney, 2)); g_drawdown_hit = true; }
         return false;
      }
   }
   
   g_drawdown_hit = false;
   return true;
}

//+------------------------------------------------------------------+
//| Daily Limit Check                                               |
//+------------------------------------------------------------------+
bool IsDailyLimitOK()
{
   if(!EnableProtection) return true;
   
   double balance = AccountInfoDouble(ACCOUNT_BALANCE);
   double todayPL = balance - g_day_start_balance;
   
   if(DailyProfitTarget > 0 && todayPL >= DailyProfitTarget)
   {
      if(!g_daily_target_hit) { Print("🎉 TARGET: +$", DoubleToString(todayPL, 2)); g_daily_target_hit = true; }
      return false;
   }
   
   if(DailyLossLimit > 0 && todayPL <= -DailyLossLimit)
   {
      if(!g_daily_loss_hit) { Print("😢 LOSS: -$", DoubleToString(MathAbs(todayPL), 2)); g_daily_loss_hit = true; }
      return false;
   }
   
   return true;
}

//+------------------------------------------------------------------+
//| ⚠️ FAST SCALPING - EXACT SAME LOGIC AS ORIGINAL ⚠️               |
//+------------------------------------------------------------------+
void FastScalping()
{
   if(!EnableScalping || !IsSessionActive()) return;

   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   int digits = (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);
   
   double spread = (ask - bid) / g_pip_size;

   // ⚡ FAST TIMING - Uses input parameter (default 5 seconds)
   if(TimeCurrent() - g_last_scalp_time < EntryDelaySeconds) return;
   
   int currentScalpPositions = CountMyPositions();
   if(currentScalpPositions >= MaxScalpPositions) return;

   double targetDistance = ScalpingTargetPips * g_pip_size;
   double lot = NormalizeLot(ScalpingLotSize);

   //+------------------------------------------------------------------+
   //| ⚠️ EXACT SAME ENTRY LOGIC - NEVER CHANGED ⚠️                     |
   //+------------------------------------------------------------------+
   
   // BUY - SAME AS ORIGINAL (g_last_entry_price == 0 for first trade!)
   if(bid > g_last_entry_price + targetDistance || g_last_entry_price == 0)
   {
      double tp = NormalizeDouble(ask + targetDistance, digits);
      
      if(trade.Buy(lot, _Symbol, ask, 0, tp, EA_Comment))
      {
         g_last_entry_price = bid;
         g_last_scalp_time = TimeCurrent();
         Print("🟢 BUY: Lot=", lot, " Entry=", ask, " TP=", tp, " Spread=", DoubleToString(spread, 1));
      }
   }

   // SELL - SAME AS ORIGINAL
   if(bid < g_last_entry_price - targetDistance)
   {
      double tp = NormalizeDouble(bid - targetDistance, digits);
      
      if(trade.Sell(lot, _Symbol, bid, 0, tp, EA_Comment))
      {
         g_last_entry_price = bid;
         g_last_scalp_time = TimeCurrent();
         Print("🔴 SELL: Lot=", lot, " Entry=", bid, " TP=", tp, " Spread=", DoubleToString(spread, 1));
      }
   }
}

//+------------------------------------------------------------------+
//| ⚠️ POSITION MANAGEMENT - EXACT SAME AS ORIGINAL ⚠️               |
//+------------------------------------------------------------------+
void ManagePositions()
{
   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double targetDistance = ScalpingTargetPips * g_pip_size;
   
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;
      
      if(PositionGetString(POSITION_SYMBOL) != _Symbol || 
         PositionGetInteger(POSITION_MAGIC) != Magic_Base) continue;

      double entryPrice = PositionGetDouble(POSITION_PRICE_OPEN);
      long posType = PositionGetInteger(POSITION_TYPE);
      double profit = PositionGetDouble(POSITION_PROFIT);

      // SAME CLOSE LOGIC AS ORIGINAL
      if(posType == POSITION_TYPE_BUY)
      {
         double priceDiff = bid - entryPrice;
         if(priceDiff >= targetDistance)
         {
            if(trade.PositionClose(ticket))
               Print("✅ BUY Closed: $", DoubleToString(profit, 2));
         }
      }
      else if(posType == POSITION_TYPE_SELL)
      {
         double priceDiff = entryPrice - ask;
         if(priceDiff >= targetDistance)
         {
            if(trade.PositionClose(ticket))
               Print("✅ SELL Closed: $", DoubleToString(profit, 2));
         }
      }
   }
}

//+------------------------------------------------------------------+
//| Dashboard                                                       |
//+------------------------------------------------------------------+
void DrawDashboard()
{
   if(!ShowDashboard) return;
   
   double balance = AccountInfoDouble(ACCOUNT_BALANCE);
   double equity = AccountInfoDouble(ACCOUNT_EQUITY);
   double floatingPL = GetFloatingPL();
   double todayPL = balance - g_day_start_balance;
   double ddPercent = (balance > 0) ? ((balance - equity) / balance) * 100.0 : 0;
   int positions = CountMyPositions();
   
   string status = "🟢 ACTIVE";
   if(!Bat_Bot) status = "🔴 OFF";
   else if(g_daily_target_hit) status = "🎉 TARGET";
   else if(g_daily_loss_hit) status = "😢 LOSS";
   else if(g_drawdown_hit) status = "🛑 DD";
   else if(!IsSessionActive()) status = "💤 SLEEP";
   
   string info = "";
   info += "═══════════════════════\n";
   info += "  LDA SCALPER v2.2\n";
   info += "═══════════════════════\n";
   info += "  " + status + "\n";
   info += "  Balance: $" + DoubleToString(balance, 2) + "\n";
   info += "  Equity: $" + DoubleToString(equity, 2) + "\n";
   info += "  Float: $" + DoubleToString(floatingPL, 2) + "\n";
   info += "  Today: $" + DoubleToString(todayPL, 2) + "\n";
   info += "  DD: " + DoubleToString(ddPercent, 1) + "%\n";
   info += "───────────────────────\n";
   info += "  Pos: " + IntegerToString(positions) + "/" + IntegerToString(MaxScalpPositions) + "\n";
   info += "  Lot: " + DoubleToString(ScalpingLotSize, 2) + "\n";
   info += "  TP: " + IntegerToString(ScalpingTargetPips) + " pips\n";
   info += "  Speed: " + IntegerToString(EntryDelaySeconds) + "s\n";
   info += "═══════════════════════\n";
   
   Comment(info);
}

//+------------------------------------------------------------------+
//| Init                                                            |
//+------------------------------------------------------------------+
int OnInit()
{
   trade.SetExpertMagicNumber(Magic_Base);
   
   g_pip_size = GetPipSize();
   g_last_bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   g_day_start_balance = AccountInfoDouble(ACCOUNT_BALANCE);
   
   MqlDateTime now;
   TimeToStruct(TimeCurrent(), now);
   g_last_day = StringToTime(IntegerToString(now.year) + "." + 
                              IntegerToString(now.mon) + "." + 
                              IntegerToString(now.day));
   
   Print("═══════════════════════════════════");
   Print("  LDA SCALPER v2.2 STARTED");
   Print("  Lot: ", ScalpingLotSize, " | TP: ", ScalpingTargetPips, " pips");
   Print("  Speed: ", EntryDelaySeconds, " seconds");
   Print("  Max Positions: ", MaxScalpPositions);
   Print("═══════════════════════════════════");
   
   return INIT_SUCCEEDED;
}

//+------------------------------------------------------------------+
//| Deinit                                                          |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   Comment("");
   Print("🔴 Stopped. Reason: ", reason);
}

//+------------------------------------------------------------------+
//| OnTick - SAME FLOW AS ORIGINAL                                  |
//+------------------------------------------------------------------+
void OnTick()
{
   DrawDashboard();
   
   if(!Bat_Bot) return;
   
   CheckNewDay();
   
   // Optional protections
   if(EnableGapProtection)
   {
      if(CheckForGap()) return;
      if(TimeCurrent() < g_gap_pause_until) return;
   }
   
   if(EnableProtection)
   {
      if(!IsDrawdownSafe()) return;
      if(!IsDailyLimitOK()) return;
   }
   
   // ⚠️ SAME AS ORIGINAL - Session check inside FastScalping
   FastScalping();
   ManagePositions();
}
//+------------------------------------------------------------------+
