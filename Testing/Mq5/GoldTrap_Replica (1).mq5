//+------------------------------------------------------------------+
//|                                XAUUSD_Dynamic_Recovery_Grid.mq5 |
//| Developed By @manoj8901                                         |
//+------------------------------------------------------------------+
#property copyright "Developed By @manoj8901"
#property link      ""
#property version   "2.00"
#property description "Complete XAUUSD Dynamic Recovery Grid Expert Advisor for MT5"
#property description "Features dynamic lot optimization, one-side basket recovery, state reconstruction, and strict comment tagging."

#include <Trade\Trade.mqh>
#include <Trade\PositionInfo.mqh>
#include <Trade\OrderInfo.mqh>

//--- Developer Comment Constant
const string EA_COMMENT = "Developed By @manoj8901";

//+------------------------------------------------------------------+
//| INPUT PARAMETERS                                                 |
//+------------------------------------------------------------------+
enum ENUM_N1_MODE
{
   N0 = 0,   // All importance levels
   N1 = 1,   // Medium and High importance
   N2 = 2    // High importance only
};

input group "=== A ==="
input string   InpLicenseKey = "";             // License Key (GTXX-XXXX-XXXX-XXXX)

input group "=== B ==="
input double   D1 = 1.00;
input double   V1 = 0.01;

input group "=== C ==="
input int      N  = 2000;
input double   S1 = 13.0;
input double   S2 = 2.60;
input double   T1 = 40.00;
input double   E1 = 1.68;
input double   R1 = 0.0;                       // R1 (1.0=100%, 0.2=20%)
input double   P1 = 0.50;
input int      P2 = 2;
input double   H1 = 0.0;

input group "=== D ==="
input ulong    M1 = 123456;
input ulong    M2 = 123457;

input group "=== E ==="
input bool     F1 = true;
input int      T2 = 1;
input int      T3 = 15;
input int      T4 = 22;
input int      T5 = 0;
input bool     F2 = false;

input group "=== F ==="
input bool     F3 = false;
input double   DT1 = 0.0;

input group "=== G ==="
input bool     F4 = false;
input ENUM_N1_MODE InpNewsMode = N2;           // N1
input int      T6 = 30;
input int      T7 = 30;

//--- Fixed internal settings: intentionally hidden from the user Inputs tab
//    (matches the STEVE STRADDLE reference file - same values GoldTrap_Replica
//    already used, simply no longer exposed as editable inputs).
const ulong    InpSlippage               = 30;
const double   InpMaxLot                 = 1000.00;
const bool     InpUpdateBrokerTP         = true;
const int      InpMinimumDelayBetweenOrders = 1;
const bool     InpEnableLogs             = true;
const int      InpNewsLookAheadDays      = 14;
const bool     InpCancelPendingsForNews  = true;
const int      InpNewsRefreshSeconds     = 30;

enum ENUM_AFTER_MAX_DD
{
   DD_CONTINUE_TRADING = 0,   // Continue Trading
   DD_STOP_TRADING     = 1    // Stop Trading
};

const bool               InpEnableMaxDDProtection = false;
const double             InpMaxDrawdownPercent    = 20.0;
const ENUM_AFTER_MAX_DD  InpAfterMaxDD            = DD_STOP_TRADING;

//+------------------------------------------------------------------+
//| ENUMERATIONS & STATE MACHINE                                    |
//+------------------------------------------------------------------+
enum ENUM_EA_STATE
{
   STATE_INIT,                   // Initializing / Reconstructing state
   STATE_WAITING_FOR_TRIGGER,    // Waiting for Buy Stop or Sell Stop to trigger
   STATE_BUY_BASKET_ACTIVE,      // BUY basket active, managing recovery grid
   STATE_SELL_BASKET_ACTIVE,     // SELL basket active, managing recovery grid
   STATE_CLOSING_BASKET,         // Profit target reached, closing all positions
   STATE_RESETTING_CYCLE         // Resetting cycle after full closure
};

//+------------------------------------------------------------------+
//| GLOBAL VARIABLES                                                 |
//+------------------------------------------------------------------+
CTrade         m_trade;
CPositionInfo  m_position;
COrderInfo     m_order;

ENUM_EA_STATE  g_state = STATE_INIT;
datetime       g_last_order_time = 0;

bool           g_max_dd_emergency_active   = false;
bool           g_trading_stopped_by_max_dd = false;

//--- Economic-calendar cache
bool           g_news_blocked       = false;
bool           g_calendar_available = true;
datetime       g_last_news_scan     = 0;
datetime       g_next_news_time     = 0;
string         g_next_news_name     = "No scheduled high-impact USD news";
string         g_active_news_name   = "";

bool IsOurMagic(const long magic)
{
   return ((ulong)magic == M1 || (ulong)magic == M2);
}

void SetTradeMagic(const ulong magic)
{
   m_trade.SetExpertMagicNumber(magic);
}

//+------------------------------------------------------------------+
//| Expert initialization function                                   |
//+------------------------------------------------------------------+
int OnInit()
{
   // 1. Reset Session Flags
   g_max_dd_emergency_active   = false;
   g_trading_stopped_by_max_dd = false;

   // 2. Verify Hedging Account Mode
   long margin_mode = AccountInfoInteger(ACCOUNT_MARGIN_MODE);
   if(margin_mode != ACCOUNT_MARGIN_MODE_RETAIL_HEDGING)
   {
      PrintFormat("[EA ERROR] Account must be in HEDGING mode! Current mode ID: %d", margin_mode);
      return INIT_FAILED;
   }

   // 3. Validate Inputs
   if(V1 <= 0.0 || D1 <= 0.0 || S1 <= 0.0 || S2 <= 0.0 || M1 == M2 ||
      T1 <= 0.0 || E1 <= 0.0 || N < 1 || P2 < 0)
   {
      Print("[EA ERROR] Invalid input parameters. Inputs must be greater than zero.");
      return INIT_PARAMETERS_INCORRECT;
   }

   if(T2 < 0 || T2 > 23 || T4 < 0 || T4 > 23 || T3 < 0 || T3 > 59 || T5 < 0 || T5 > 59 ||
      T6 < 0 || T7 < 0)
   {
      Print("[EA ERROR] Invalid session or news-filter time.");
      return INIT_PARAMETERS_INCORRECT;
   }

   if(InpEnableMaxDDProtection && (InpMaxDrawdownPercent <= 0.0 || InpMaxDrawdownPercent >= 100.0))
   {
      Print("[EA ERROR] MaxDrawdownPercent must be strictly between 0.0 and 100.0.");
      return INIT_PARAMETERS_INCORRECT;
   }

   // 4. Configure CTrade
   m_trade.SetExpertMagicNumber(M1);
   m_trade.SetDeviationInPoints(InpSlippage);
   m_trade.SetTypeFillingBySymbol(_Symbol);

   // 5. Reconstruct State from existing positions & orders
   ReconstructState();

   // 6. Start the MQL5 calendar cache.
   UpdateNewsState(true);

   LogMessage(StringFormat("EA Initialized successfully. Symbol: %s | BUY Magic: %I64u | SELL Magic: %I64u | State: %s | Max DD Protection: %s (%.2f%%)",
              _Symbol, M1, M2, EnumToString(g_state),
              InpEnableMaxDDProtection ? "ENABLED" : "DISABLED", InpMaxDrawdownPercent));

   return INIT_SUCCEEDED;
}

//+------------------------------------------------------------------+
//| Expert deinitialization function                                 |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   LogMessage(StringFormat("EA Deinitialized. Reason code: %d", reason));
}

//+------------------------------------------------------------------+
//| Expert tick function                                             |
//+------------------------------------------------------------------+
void OnTick()
{
   // 1. Session stop check (AfterMaxDD = DD_STOP_TRADING)
   if(g_trading_stopped_by_max_dd)
      return;

   // 2. Emergency liquidation in progress check
   if(g_max_dd_emergency_active)
   {
      ProcessMaxDDEmergency();
      return;
   }

   // 3. High-Priority Max Drawdown Trigger Check BEFORE any new entries or normal trading
   if(IsMaxDrawdownReached())
   {
      TriggerMaxDDEmergency();
      ProcessMaxDDEmergency();
      return;
   }

   // 4. Always inspect and maintain current state machine
   switch(g_state)
   {
      case STATE_INIT:
         ReconstructState();
         break;

      case STATE_WAITING_FOR_TRIGGER:
         ProcessWaitingForTriggerState();
         break;

      case STATE_BUY_BASKET_ACTIVE:
         ProcessBuyBasketState();
         break;

      case STATE_SELL_BASKET_ACTIVE:
         ProcessSellBasketState();
         break;

      case STATE_CLOSING_BASKET:
         CloseActiveBasket();
         break;

      case STATE_RESETTING_CYCLE:
         ProcessResettingCycleState();
         break;
   }
}

//+------------------------------------------------------------------+
//| Trade Transaction handler for fast real-time event detection    |
//+------------------------------------------------------------------+
void OnTradeTransaction(const MqlTradeTransaction &trans,
                         const MqlTradeRequest &request,
                         const MqlTradeResult &result)
{
   if(g_trading_stopped_by_max_dd) return;

   if(g_max_dd_emergency_active)
   {
      ProcessMaxDDEmergency();
      return;
   }

   if(IsMaxDrawdownReached())
   {
      TriggerMaxDDEmergency();
      ProcessMaxDDEmergency();
      return;
   }

   // Reconstruct state on order fills or position closures to react immediately
   if(trans.type == TRADE_TRANSACTION_ORDER_ADD ||
      trans.type == TRADE_TRANSACTION_ORDER_DELETE ||
      trans.type == TRADE_TRANSACTION_HISTORY_ADD)
   {
      ReconstructState();
   }
}

//+------------------------------------------------------------------+
//| RECONSTRUCT STATE ON STARTUP / REATTACH / TICK                  |
//+------------------------------------------------------------------+
void ReconstructState()
{
   int buy_pos_count  = 0;
   int sell_pos_count = 0;
   int buy_stop_count = 0;
   int sell_stop_count = 0;

   // Scan Positions
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      if(m_position.SelectByIndex(i))
      {
         if(m_position.Symbol() == _Symbol && IsOurMagic(m_position.Magic()))
         {
            if(m_position.PositionType() == POSITION_TYPE_BUY)
               buy_pos_count++;
            else if(m_position.PositionType() == POSITION_TYPE_SELL)
               sell_pos_count++;
         }
      }
   }

   // Scan Pending Orders
   for(int i = OrdersTotal() - 1; i >= 0; i--)
   {
      if(m_order.SelectByIndex(i))
      {
         if(m_order.Symbol() == _Symbol && IsOurMagic(m_order.Magic()))
         {
            if(m_order.OrderType() == ORDER_TYPE_BUY_STOP)
               buy_stop_count++;
            else if(m_order.OrderType() == ORDER_TYPE_SELL_STOP)
               sell_stop_count++;
         }
      }
   }

   // State Resolution Logic
   if(buy_pos_count > 0 && sell_pos_count == 0)
   {
      // Active BUY Basket: Cancel obsolete Sell Stop if present
      if(sell_stop_count > 0)
      {
         LogMessage("BUY Basket triggered. Deleting opposite Sell Stop order.");
         CancelPendingOrders(ORDER_TYPE_SELL_STOP);
      }
      if(g_state != STATE_CLOSING_BASKET)
         g_state = STATE_BUY_BASKET_ACTIVE;

      UpdateBrokerBasketTP(POSITION_TYPE_BUY);
   }
   else if(sell_pos_count > 0 && buy_pos_count == 0)
   {
      // Active SELL Basket: Cancel obsolete Buy Stop if present
      if(buy_stop_count > 0)
      {
         LogMessage("SELL Basket triggered. Deleting opposite Buy Stop order.");
         CancelPendingOrders(ORDER_TYPE_BUY_STOP);
      }
      if(g_state != STATE_CLOSING_BASKET)
         g_state = STATE_SELL_BASKET_ACTIVE;

      UpdateBrokerBasketTP(POSITION_TYPE_SELL);
   }
   else if(buy_pos_count == 0 && sell_pos_count == 0)
   {
      // No active positions
      if(buy_stop_count > 0 || sell_stop_count > 0)
      {
         g_state = STATE_WAITING_FOR_TRIGGER;
      }
      else
      {
         g_state = STATE_RESETTING_CYCLE;
      }
   }
   else if(buy_pos_count > 0 && sell_pos_count > 0)
   {
      Print("[EA WARNING] Hedging overlap detected (both BUY and SELL positions exist). Grid entries paused for safety.");
   }
}

//+------------------------------------------------------------------+
//| STATE 1: WAITING FOR TRIGGER                                     |
//+------------------------------------------------------------------+
void ProcessWaitingForTriggerState()
{
   int buy_stop_count = 0;
   int sell_stop_count = 0;

   for(int i = OrdersTotal() - 1; i >= 0; i--)
   {
      if(m_order.SelectByIndex(i))
      {
         if(m_order.Symbol() == _Symbol && IsOurMagic(m_order.Magic()))
         {
            if(m_order.OrderType() == ORDER_TYPE_BUY_STOP) buy_stop_count++;
            if(m_order.OrderType() == ORDER_TYPE_SELL_STOP) sell_stop_count++;
         }
      }
   }

   bool news_blocked = IsNewsWindowBlocked();
   bool entry_allowed = CanStartNewCycle();
   if(!entry_allowed)
   {
      bool must_cancel_for_news = (news_blocked && InpCancelPendingsForNews);
      if((must_cancel_for_news || F2) && GetPositionCount() == 0 && (buy_stop_count > 0 || sell_stop_count > 0))
         CancelAllPendingOrders();
      return;
   }

   // If both initial pending orders exist, wait for trigger
   if(buy_stop_count == 1 && sell_stop_count == 1)
      return;

   // If missing pending orders and 0 positions exist, place new pair
   if(GetPositionCount() == 0)
   {
      PlaceInitialPendingOrders();
   }
}

//+------------------------------------------------------------------+
//| PLACE INITIAL PENDING ORDERS                                     |
//+------------------------------------------------------------------+
void PlaceInitialPendingOrders()
{
   if(!CanStartNewCycle() || IsSpreadExceeded()) return;

   // Ensure zero stale pending orders before placing pair
   CancelAllPendingOrders();

   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   double ref_price = (ask + bid) / 2.0;

   double buy_stop_price  = NormalizePrice(ref_price + D1);
   double sell_stop_price = NormalizePrice(ref_price - D1);

   double initial_lot = GetConfiguredInitialLot();

   // Calculate Initial TP distance ($40/lot = $0.40 at 0.01 lot)
   double k_profit = GetProfitPerUnitPerLot();
   if(k_profit <= 0.0) k_profit = 100.0;
   double target_dist = T1 / k_profit;

   double buy_tp  = InpUpdateBrokerTP ? NormalizePrice(buy_stop_price + target_dist) : 0.0;
   double sell_tp = InpUpdateBrokerTP ? NormalizePrice(sell_stop_price - target_dist) : 0.0;

   LogMessage(StringFormat("Starting New Cycle. Ref Price: %.2f | Buy Stop: %.2f (TP: %.2f) | Sell Stop: %.2f (TP: %.2f) | Comment: %s",
              ref_price, buy_stop_price, buy_tp, sell_stop_price, sell_tp, EA_COMMENT));

   // Place Buy Stop
   SetTradeMagic(M1);
   if(!m_trade.BuyStop(initial_lot, buy_stop_price, _Symbol, 0.0, buy_tp, ORDER_TIME_GTC, 0, EA_COMMENT))
   {
      PrintFormat("[EA ERROR] Failed to place Buy Stop order at %.2f. Error: %d", buy_stop_price, GetLastError());
      return;
   }

   // Place Sell Stop
   SetTradeMagic(M2);
   if(!m_trade.SellStop(initial_lot, sell_stop_price, _Symbol, 0.0, sell_tp, ORDER_TIME_GTC, 0, EA_COMMENT))
   {
      PrintFormat("[EA ERROR] Failed to place Sell Stop order at %.2f. Error: %d", sell_stop_price, GetLastError());
      return;
   }

   g_state = STATE_WAITING_FOR_TRIGGER;
}

//+------------------------------------------------------------------+
//| STATE 2: PROCESS BUY BASKET STATE                                |
//+------------------------------------------------------------------+
void ProcessBuyBasketState()
{
   // 1. Check Basket TP Target
   if(CheckBasketProfitTarget(POSITION_TYPE_BUY))
   {
      g_state = STATE_CLOSING_BASKET;
      CloseActiveBasket();
      return;
   }

   // 2. Check Grid Entry
   int buy_pos_count = GetPositionCount(POSITION_TYPE_BUY);
   if(buy_pos_count < N)
   {
      double lowest_buy_price = GetLowestEntryPrice(POSITION_TYPE_BUY);
      if(lowest_buy_price > 0.0)
      {
         double next_grid_price = NormalizePrice(lowest_buy_price - GetGridDistancePrice(buy_pos_count));
         double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);

         // Check if price reached or crossed next grid level
         if(ask <= next_grid_price)
         {
            if(!IsSpreadExceeded() && (TimeCurrent() - g_last_order_time >= InpMinimumDelayBetweenOrders))
            {
               // Calculate Next Grid Lot (LotMultiplier progression)
               double next_lot = CalculateNextGridLot(POSITION_TYPE_BUY, next_grid_price);
               if(next_lot > 0.0)
               {
                  SetTradeMagic(M1);
                  if(m_trade.Buy(next_lot, _Symbol, 0.0, 0.0, 0.0, EA_COMMENT))
                  {
                     g_last_order_time = TimeCurrent();
                     UpdateBrokerBasketTP(POSITION_TYPE_BUY);
                  }
                  else
                  {
                     PrintFormat("[EA ERROR] Failed to open BUY grid trade. Error: %d", GetLastError());
                  }
               }
               else
               {
                  Print("[EA WARNING] Next lot calculation returned 0.0 (MaxLot reached or invalid lot). Grid trade skipped.");
               }
            }
         }
      }
   }

   // 3. Update physical TP on broker server for all BUY positions
   UpdateBrokerBasketTP(POSITION_TYPE_BUY);
}

//+------------------------------------------------------------------+
//| STATE 3: PROCESS SELL BASKET STATE                               |
//+------------------------------------------------------------------+
void ProcessSellBasketState()
{
   // 1. Check Basket TP Target
   if(CheckBasketProfitTarget(POSITION_TYPE_SELL))
   {
      g_state = STATE_CLOSING_BASKET;
      CloseActiveBasket();
      return;
   }

   // 2. Check Grid Entry
   int sell_pos_count = GetPositionCount(POSITION_TYPE_SELL);
   if(sell_pos_count < N)
   {
      double highest_sell_price = GetHighestEntryPrice(POSITION_TYPE_SELL);
      if(highest_sell_price > 0.0)
      {
         double next_grid_price = NormalizePrice(highest_sell_price + GetGridDistancePrice(sell_pos_count));
         double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);

         // Check if price reached or crossed next grid level
         if(bid >= next_grid_price)
         {
            if(!IsSpreadExceeded() && (TimeCurrent() - g_last_order_time >= InpMinimumDelayBetweenOrders))
            {
               // Calculate Next Grid Lot (LotMultiplier progression)
               double next_lot = CalculateNextGridLot(POSITION_TYPE_SELL, next_grid_price);
               if(next_lot > 0.0)
               {
                  SetTradeMagic(M2);
                  if(m_trade.Sell(next_lot, _Symbol, 0.0, 0.0, 0.0, EA_COMMENT))
                  {
                     g_last_order_time = TimeCurrent();
                     UpdateBrokerBasketTP(POSITION_TYPE_SELL);
                  }
                  else
                  {
                     PrintFormat("[EA ERROR] Failed to open SELL grid trade. Error: %d", GetLastError());
                  }
               }
               else
               {
                  Print("[EA WARNING] Next lot calculation returned 0.0 (MaxLot reached or invalid lot). Grid trade skipped.");
               }
            }
         }
      }
   }

   // 3. Update physical TP on broker server for all SELL positions
   UpdateBrokerBasketTP(POSITION_TYPE_SELL);
}

//+------------------------------------------------------------------+
//| FIXED MULTIPLIER LOT SIZE CALCULATOR (e.g. 1.68x)                |
//+------------------------------------------------------------------+
double CalculateNextGridLot(ENUM_POSITION_TYPE type, double grid_entry_price)
{
   double last_lot = 0.0;
   datetime latest_time = 0;
   int pos_count = 0;
   double total_basket_lots = 0.0;
   double current_floating_profit = 0.0;

   // 1. Gather positions in active basket and find latest position volume
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      if(m_position.SelectByIndex(i))
      {
         if(m_position.Symbol() == _Symbol && IsOurMagic(m_position.Magic()) &&
            m_position.PositionType() == type)
         {
            pos_count++;
            double vol = m_position.Volume();
            total_basket_lots += vol;
            current_floating_profit += m_position.Profit() + m_position.Swap();

            datetime pos_time = m_position.Time();
            if(pos_time >= latest_time)
            {
               latest_time = pos_time;
               last_lot = vol;
            }
         }
      }
   }

   // If no active position found, return InitialLot
   if(last_lot <= 0.0)
      return GetConfiguredInitialLot();

   // 2. Recursive progression: Previous actual lot * LotMultiplier
   double raw_next_lot = last_lot * E1;

   // 3. Normalize to nearest broker step
   double normalized_next_lot = NormalizeVolume(raw_next_lot);

   // 4. Verify against MaxLot and broker maximum volume
   double max_broker_vol = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
   if(normalized_next_lot > InpMaxLot || normalized_next_lot > max_broker_vol)
   {
      PrintFormat("[EA WARNING] Calculated lot (%.2f) exceeds MaxLot limit (%.2f). Grid trade blocked.",
                  normalized_next_lot, InpMaxLot);
      return 0.0;
   }

   // 5. Logging details
   if(InpEnableLogs)
   {
      double basket_target_profit = (total_basket_lots + normalized_next_lot) * T1;
      PrintFormat("---------------- GRID TRADE PROGRESSION (#%d) ----------------", pos_count + 1);
      PrintFormat("Previous Lot: %.2f | Lot Multiplier: %.2f", last_lot, E1);
      PrintFormat("Raw Next Lot: %.4f | Normalized Lot: %.2f", raw_next_lot, normalized_next_lot);
      PrintFormat("Grid Entry: %.2f | Direction: %s", grid_entry_price, EnumToString(type));
      PrintFormat("Total Basket Lots (After): %.2f | Basket Positions (After): %d",
                  total_basket_lots + normalized_next_lot, pos_count + 1);
      PrintFormat("Current Floating Profit: $%.2f | Basket Target Profit: $%.2f",
                  current_floating_profit, basket_target_profit);
      PrintFormat("---------------------------------------------------------------");
   }

   return normalized_next_lot;
}

//+------------------------------------------------------------------+
//| HELPER: CALCULATE THEORETICAL BASKET TP PRICE                    |
//+------------------------------------------------------------------+
double CalculateTheoreticalTPPrice(ENUM_POSITION_TYPE type, double new_entry_price, double candidate_lot, double k_profit)
{
   double sum_volume_price = candidate_lot * new_entry_price;
   double total_volume = candidate_lot;

   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      if(m_position.SelectByIndex(i))
      {
         if(m_position.Symbol() == _Symbol && IsOurMagic(m_position.Magic()) &&
            m_position.PositionType() == type)
         {
            sum_volume_price += m_position.Volume() * m_position.PriceOpen();
            total_volume += m_position.Volume();
         }
      }
   }

   if(total_volume <= 0.0) return 0.0;

   double break_even_price = sum_volume_price / total_volume;
   double target_dist = T1 / k_profit;

   if(type == POSITION_TYPE_BUY)
      return NormalizePrice(break_even_price + target_dist);
   else
      return NormalizePrice(break_even_price - target_dist);
}

//+------------------------------------------------------------------+
//| HELPER: UPDATE PHYSICAL BROKER TP ON ALL BASKET POSITIONS        |
//+------------------------------------------------------------------+
void UpdateBrokerBasketTP(ENUM_POSITION_TYPE type)
{
   if(!InpUpdateBrokerTP) return;

   int count = GetPositionCount(type);
   if(count == 0) return;

   double k_profit = GetProfitPerUnitPerLot();
   if(k_profit <= 0.0) k_profit = 100.0;

   // Calculate theoretical TP for current basket positions
   double theoretical_tp = CalculateTheoreticalTPPrice(type, 0.0, 0.0, k_profit);
   if(theoretical_tp <= 0.0) return;

   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      if(m_position.SelectByIndex(i))
      {
         if(m_position.Symbol() == _Symbol && IsOurMagic(m_position.Magic()) &&
            m_position.PositionType() == type)
         {
            double pos_tp = m_position.TakeProfit();
            if(MathAbs(pos_tp - theoretical_tp) > _Point)
            {
               ulong ticket = m_position.Ticket();
               double sl = m_position.StopLoss();
               if(m_trade.PositionModify(ticket, sl, theoretical_tp))
               {
                  LogMessage(StringFormat("Updated Broker TP for Position #%I64u to %.2f", ticket, theoretical_tp));
               }
            }
         }
      }
   }
}

//+------------------------------------------------------------------+
//| CHECK COMBINED BASKET PROFIT TARGET                              |
//+------------------------------------------------------------------+
bool CheckBasketProfitTarget(ENUM_POSITION_TYPE type)
{
   double total_profit = 0.0;
   double total_volume = 0.0;
   int pos_count = 0;

   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      if(m_position.SelectByIndex(i))
      {
         if(m_position.Symbol() == _Symbol && IsOurMagic(m_position.Magic()) &&
            m_position.PositionType() == type)
         {
            total_profit += m_position.Profit() + m_position.Swap();
            total_volume += m_position.Volume();
            pos_count++;
         }
      }
   }

   if(pos_count == 0) return false;

   double required_profit = total_volume * T1;

   if(total_profit >= required_profit)
   {
      LogMessage(StringFormat("BASKET TARGET REACHED! Floating Profit: $%.2f | Required: $%.2f | Total Lots: %.2f",
                 total_profit, required_profit, total_volume));
      return true;
   }

   return false;
}

//+------------------------------------------------------------------+
//| CLOSE ENTIRE ACTIVE BASKET                                       |
//+------------------------------------------------------------------+
void CloseActiveBasket()
{
   LogMessage("Closing all positions in active basket...");

   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      if(m_position.SelectByIndex(i))
      {
         if(m_position.Symbol() == _Symbol && IsOurMagic(m_position.Magic()))
         {
            ulong ticket = m_position.Ticket();
            if(!m_trade.PositionClose(ticket))
            {
               PrintFormat("[EA ERROR] Failed to close position #%I64u. Error: %d", ticket, GetLastError());
            }
         }
      }
   }

   // Cancel any pending orders
   CancelAllPendingOrders();

   g_state = STATE_RESETTING_CYCLE;
}

//+------------------------------------------------------------------+
//| STATE 6: PROCESS RESETTING CYCLE STATE                           |
//+------------------------------------------------------------------+
void ProcessResettingCycleState()
{
   int pos_count = GetPositionCount();
   int ord_count = GetPendingOrderCount();

   // Confirm zero positions and zero orders before initiating next cycle
   if(pos_count == 0 && ord_count == 0)
   {
      g_state = STATE_WAITING_FOR_TRIGGER;
      if(CanStartNewCycle())
      {
         LogMessage("Cycle reset verified. Starting a new allowed cycle.");
         PlaceInitialPendingOrders();
      }
   }
}

//+------------------------------------------------------------------+
//| UTILITY & HELPERS                                               |
//+------------------------------------------------------------------+
int GetPositionCount(ENUM_POSITION_TYPE type = -1)
{
   int count = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      if(m_position.SelectByIndex(i))
      {
         if(m_position.Symbol() == _Symbol && IsOurMagic(m_position.Magic()))
         {
            if(type == -1 || m_position.PositionType() == type)
               count++;
         }
      }
   }
   return count;
}

int GetPendingOrderCount()
{
   int count = 0;
   for(int i = OrdersTotal() - 1; i >= 0; i--)
   {
      if(m_order.SelectByIndex(i))
      {
         if(m_order.Symbol() == _Symbol && IsOurMagic(m_order.Magic()))
            count++;
      }
   }
   return count;
}

double GetLowestEntryPrice(ENUM_POSITION_TYPE type)
{
   double lowest = 999999.0;
   bool found = false;

   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      if(m_position.SelectByIndex(i))
      {
         if(m_position.Symbol() == _Symbol && IsOurMagic(m_position.Magic()) &&
            m_position.PositionType() == type)
         {
            if(m_position.PriceOpen() < lowest)
            {
               lowest = m_position.PriceOpen();
               found = true;
            }
         }
      }
   }
   return found ? lowest : 0.0;
}

double GetHighestEntryPrice(ENUM_POSITION_TYPE type)
{
   double highest = 0.0;
   bool found = false;

   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      if(m_position.SelectByIndex(i))
      {
         if(m_position.Symbol() == _Symbol && IsOurMagic(m_position.Magic()) &&
            m_position.PositionType() == type)
         {
            if(m_position.PriceOpen() > highest)
            {
               highest = m_position.PriceOpen();
               found = true;
            }
         }
      }
   }
   return found ? highest : 0.0;
}

void CancelPendingOrders(ENUM_ORDER_TYPE order_type)
{
   for(int i = OrdersTotal() - 1; i >= 0; i--)
   {
      if(m_order.SelectByIndex(i))
      {
         if(m_order.Symbol() == _Symbol && IsOurMagic(m_order.Magic()) &&
            m_order.OrderType() == order_type)
         {
            ulong ticket = m_order.Ticket();
            m_trade.OrderDelete(ticket);
         }
      }
   }
}

void CancelAllPendingOrders()
{
   for(int i = OrdersTotal() - 1; i >= 0; i--)
   {
      if(m_order.SelectByIndex(i))
      {
         if(m_order.Symbol() == _Symbol && IsOurMagic(m_order.Magic()))
         {
            ulong ticket = m_order.Ticket();
            m_trade.OrderDelete(ticket);
         }
      }
   }
}

double GetProfitPerUnitPerLot()
{
   double profit = 0.0;
   double price1 = 1000.0;
   double price2 = 1001.0;

   if(OrderCalcProfit(ORDER_TYPE_BUY, _Symbol, 1.0, price1, price2, profit) && profit > 0.0)
   {
      return profit;
   }

   double tick_val  = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE);
   double tick_size = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
   if(tick_size > 0.0 && tick_val > 0.0)
   {
      return (1.0 / tick_size) * tick_val;
   }

   return 100.0;
}

bool IsSpreadExceeded()
{
   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   double current_spread = ask - bid;

   if(current_spread > P1)
   {
      if(InpEnableLogs)
         PrintFormat("[EA SPREAD FILTER] Current spread (%.2f) exceeds MaxSpread limit (%.2f). Entry delayed.",
                     current_spread, P1);
      return true;
   }
   return false;
}

double NormalizePrice(double price)
{
   double tick_size = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
   if(tick_size <= 0.0) tick_size = _Point;
   return MathRound(price / tick_size) * tick_size;
}

double NormalizeVolume(double volume)
{
   double min_vol  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double max_vol  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
   double step_vol = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
   if(step_vol <= 0.0) step_vol = 0.01;

   double normalized = MathRound((volume - min_vol) / step_vol) * step_vol + min_vol;
   if(normalized < min_vol) normalized = min_vol;
   if(normalized > max_vol) normalized = max_vol;
   if(normalized > InpMaxLot) normalized = InpMaxLot;

   return MathRound(normalized / step_vol) * step_vol;
}

double GetConfiguredInitialLot()
{
   double lot = V1;
   if(R1 > 0.0)
      lot *= R1;
   return NormalizeVolume(lot);
}

// P2 defines how many recovery additions use S1. Later additions use S2.
double GetGridDistancePrice(const int current_position_count)
{
   int additions_already_open = (current_position_count > 1 ? current_position_count - 1 : 0);
   return (additions_already_open < P2 ? S1 : S2);
}

void LogMessage(string message)
{
   if(InpEnableLogs)
   {
      PrintFormat("[XAUUSD EA] %s", message);
   }
}

//+------------------------------------------------------------------+
//| TRADING SESSION FILTER (F1 / T2 / T3 / T4 / T5)                  |
//+------------------------------------------------------------------+
bool IsTradingSessionOpen()
{
   if(!F1)
      return true;

   MqlDateTime tm;
   TimeToStruct(TimeCurrent(), tm);
   int current_minutes = tm.hour * 60 + tm.min;
   int start_minutes   = T2 * 60 + T3;
   int end_minutes     = T4 * 60 + T5;

   if(start_minutes == end_minutes)
      return true;

   if(start_minutes < end_minutes)
      return (current_minutes >= start_minutes && current_minutes <= end_minutes);

   // Session crossing midnight
   return (current_minutes >= start_minutes || current_minutes <= end_minutes);
}

//+------------------------------------------------------------------+
//| DAILY REALIZED-PROFIT TARGET (F3 / DT1)                          |
//+------------------------------------------------------------------+
double GetTodayRealizedProfit()
{
   datetime now = TimeCurrent();
   MqlDateTime tm;
   TimeToStruct(now, tm);
   tm.hour = 0;
   tm.min  = 0;
   tm.sec  = 0;
   datetime day_start = StructToTime(tm);

   if(!HistorySelect(day_start, now))
      return 0.0;

   double result = 0.0;
   int deals = HistoryDealsTotal();
   for(int i = 0; i < deals; i++)
   {
      ulong ticket = HistoryDealGetTicket(i);
      if(ticket == 0)
         continue;

      long magic = HistoryDealGetInteger(ticket, DEAL_MAGIC);
      if(!IsOurMagic(magic))
         continue;

      string symbol = HistoryDealGetString(ticket, DEAL_SYMBOL);
      if(symbol != _Symbol)
         continue;

      long entry = HistoryDealGetInteger(ticket, DEAL_ENTRY);
      if(entry != DEAL_ENTRY_OUT && entry != DEAL_ENTRY_INOUT && entry != DEAL_ENTRY_OUT_BY)
         continue;

      result += HistoryDealGetDouble(ticket, DEAL_PROFIT);
      result += HistoryDealGetDouble(ticket, DEAL_SWAP);
      result += HistoryDealGetDouble(ticket, DEAL_COMMISSION);
   }
   return result;
}

bool IsDailyTargetReached()
{
   if(!F3 || DT1 <= 0.0)
      return false;
   return (GetTodayRealizedProfit() >= DT1);
}

//+------------------------------------------------------------------+
//| HIGH-IMPACT NEWS FILTER (F4 / InpNewsMode / T6 / T7)             |
//| Uses MQL5's native economic calendar - no external service.      |
//+------------------------------------------------------------------+
datetime GetCalendarServerTime()
{
   datetime now = TimeTradeServer();
   if(now <= 0)
      now = TimeCurrent();
   return now;
}

void ResetNewsCache(const bool calendar_available = true)
{
   g_news_blocked       = false;
   g_calendar_available  = calendar_available;
   g_next_news_time      = 0;
   g_next_news_name      = calendar_available ? "No scheduled high-impact USD news" : "MQL5 calendar unavailable";
   g_active_news_name    = "";
}

// N2 = high impact only; N1 = medium and high; N0 = all importance levels.
bool IsSelectedNewsImportance(const int importance)
{
   if(InpNewsMode == N2)
      return (importance == CALENDAR_IMPORTANCE_HIGH);
   if(InpNewsMode == N1)
      return (importance == CALENDAR_IMPORTANCE_MODERATE || importance == CALENDAR_IMPORTANCE_HIGH);
   return true;
}

void UpdateNewsState(const bool force_refresh = false)
{
   if(!F4)
   {
      ResetNewsCache(true);
      g_next_news_name = "News filter disabled";
      return;
   }

   datetime now = GetCalendarServerTime();
   int refresh_seconds = (InpNewsRefreshSeconds < 5 ? 5 : InpNewsRefreshSeconds);
   int before_minutes = (T6 < 0 ? 0 : T6);
   int after_minutes  = (T7 < 0 ? 0 : T7);
   int lookahead_days = (InpNewsLookAheadDays < 1 ? 1 : InpNewsLookAheadDays);

   if(!force_refresh && g_next_news_time > 0)
   {
      bool cached_window = (now >= g_next_news_time - before_minutes * 60 &&
                            now <= g_next_news_time + after_minutes * 60);
      g_news_blocked = cached_window;
      g_active_news_name = cached_window ? g_next_news_name : "";

      if(now <= g_next_news_time + after_minutes * 60 &&
         g_last_news_scan > 0 && (now - g_last_news_scan) < refresh_seconds)
         return;
   }
   else if(!force_refresh && g_last_news_scan > 0 && (now - g_last_news_scan) < refresh_seconds)
   {
      return;
   }

   g_last_news_scan = now;
   ResetNewsCache(true);

   datetime from_time = now - after_minutes * 60;
   datetime to_time   = now + lookahead_days * 86400;

   MqlCalendarValue values[];
   ResetLastError();
   int count = CalendarValueHistory(values, from_time, to_time, NULL, "USD");
   if(count < 0)
   {
      ResetNewsCache(false);
      if(InpEnableLogs)
         PrintFormat("[XAUUSD EA] MQL5 calendar query failed. Error: %d", GetLastError());
      return;
   }

   datetime nearest_upcoming_time = 0;
   string nearest_upcoming_name = "";
   datetime active_event_time = 0;
   string active_event_name = "";

   for(int i = 0; i < count; i++)
   {
      MqlCalendarEvent event;
      if(!CalendarEventById(values[i].event_id, event))
         continue;

      if(!IsSelectedNewsImportance(event.importance))
         continue;

      datetime event_time = values[i].time;
      if(event_time <= 0)
         continue;

      bool inside_block_window = (now >= event_time - before_minutes * 60 &&
                                  now <= event_time + after_minutes * 60);

      if(inside_block_window)
      {
         long event_distance = (event_time >= now ? (long)(event_time - now) : (long)(now - event_time));
         long active_distance = 0;
         if(active_event_time > 0)
            active_distance = (active_event_time >= now ? (long)(active_event_time - now) : (long)(now - active_event_time));

         if(active_event_time == 0 || event_distance < active_distance)
         {
            active_event_time = event_time;
            active_event_name = event.name;
         }
      }

      if(event_time > now && (nearest_upcoming_time == 0 || event_time < nearest_upcoming_time))
      {
         nearest_upcoming_time = event_time;
         nearest_upcoming_name = event.name;
      }
   }

   if(active_event_time > 0)
   {
      g_news_blocked     = true;
      g_active_news_name = active_event_name;
      g_next_news_time    = active_event_time;
      g_next_news_name    = active_event_name;
   }
   else if(nearest_upcoming_time > 0)
   {
      g_next_news_time = nearest_upcoming_time;
      g_next_news_name = nearest_upcoming_name;
   }
}

bool IsNewsWindowBlocked()
{
   UpdateNewsState(false);
   return (F4 && g_news_blocked);
}

//+------------------------------------------------------------------+
//| COMBINED ENTRY GATE                                              |
//+------------------------------------------------------------------+
bool CanStartNewCycle()
{
   if(!IsTradingSessionOpen())
      return false;
   if(IsDailyTargetReached())
      return false;
   if(IsNewsWindowBlocked())
      return false;
   return true;
}

//+------------------------------------------------------------------+
//| MAXIMUM DRAWDOWN PROTECTION HELPERS                              |
//+------------------------------------------------------------------+
double GetCurrentDrawdownPercent()
{
   double balance = AccountInfoDouble(ACCOUNT_BALANCE);
   double equity  = AccountInfoDouble(ACCOUNT_EQUITY);

   if(balance <= 0.0 || equity >= balance)
      return 0.0;

   return ((balance - equity) / balance) * 100.0;
}

bool IsMaxDrawdownReached()
{
   if(!InpEnableMaxDDProtection)
      return false;

   double current_dd = GetCurrentDrawdownPercent();
   return (current_dd >= InpMaxDrawdownPercent);
}

void TriggerMaxDDEmergency()
{
   if(g_max_dd_emergency_active) return;

   g_max_dd_emergency_active = true;

   double balance = AccountInfoDouble(ACCOUNT_BALANCE);
   double equity  = AccountInfoDouble(ACCOUNT_EQUITY);
   double dd_money = (balance > equity) ? (balance - equity) : 0.0;
   double dd_pct   = GetCurrentDrawdownPercent();

   Print("===============================================================");
   Print("         MAX DRAWDOWN PROTECTION TRIGGERED!                    ");
   PrintFormat("Account Balance: $%.2f | Account Equity: $%.2f", balance, equity);
   PrintFormat("Drawdown Amount: $%.2f | Drawdown Percent: %.2f%% (Limit: %.2f%%)",
               dd_money, dd_pct, InpMaxDrawdownPercent);
   Print("Action: Blocking entries, deleting EA orders, closing EA positions.");
   Print("===============================================================");
}

void ProcessMaxDDEmergency()
{
   // 1. Delete all EA pending orders
   CancelAllPendingOrders();

   // 2. Close all EA positions
   CloseAllEAPositions();

   // 3. Verify zero EA positions and zero EA orders remain
   int open_positions = GetPositionCount();
   int pending_orders = GetPendingOrderCount();

   if(open_positions == 0 && pending_orders == 0)
   {
      g_max_dd_emergency_active = false;

      if(InpAfterMaxDD == DD_CONTINUE_TRADING)
      {
         LogMessage("===============================================================");
         LogMessage("Max DD liquidation completed. AfterMaxDD: CONTINUE TRADING.");
         LogMessage("Old basket cleared. Lot progression reset. Starting fresh trading cycle.");
         LogMessage("===============================================================");
         g_state = STATE_RESETTING_CYCLE;
      }
      else // DD_STOP_TRADING
      {
         g_trading_stopped_by_max_dd = true;
         LogMessage("===============================================================");
         LogMessage("Max DD liquidation completed. AfterMaxDD: STOP TRADING.");
         LogMessage("EA trading is DISABLED for this session.");
         LogMessage("Detach and attach the EA again to resume trading.");
         LogMessage("===============================================================");
      }
   }
   else
   {
      if(InpEnableLogs)
         PrintFormat("[MAX DD EMERGENCY] Liquidation in progress... Remaining EA positions: %d, orders: %d",
                     open_positions, pending_orders);
   }
}

void CloseAllEAPositions()
{
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      if(m_position.SelectByIndex(i))
      {
         if(m_position.Symbol() == _Symbol && IsOurMagic(m_position.Magic()))
         {
            ulong ticket = m_position.Ticket();
            if(!m_trade.PositionClose(ticket))
            {
               PrintFormat("[EA ERROR] Emergency close failed for position #%I64u. Error: %d",
                           ticket, GetLastError());
            }
         }
      }
   }
}
//+------------------------------------------------------------------+
