//+------------------------------------------------------------------+
//|                                          ReverseTrailing_v3.mq5   |
//|                                                Senior Developer  |
//+------------------------------------------------------------------+
#property copyright "Senior MQL5 Developer"
#property link      ""
#property version   "3.00"
#property strict

// Standard trading library includes
#include <Trade\Trade.mqh>
#include <Trade\SymbolInfo.mqh>

CTrade        m_trade;       // Object for executing trade operations
CSymbolInfo   m_symbol;      // Object for retrieving market data

//--- Money management type
enum ENUM_MM_TYPE
  {
   MM_FIXED_LOT   = 0, // Fixed lot size
   MM_PERCENT_DEP = 1  // % of free margin
  };

//--- Distance calculation type
enum ENUM_DIST_TYPE
  {
   DIST_FIXED_POINTS = 0, // Fixed distance in points
   DIST_ATR          = 1  // Dynamic distance based on ATR
  };

//--- Input parameters
input group "=== Trading Settings ==="
input ENUM_MM_TYPE InpMMType         = MM_FIXED_LOT;   // Money management type
input double       InpLotSize        = 0.1;            // Fixed lot size (if MM_FIXED_LOT)
input double       InpRiskPercent    = 1.0;            // Risk % of free margin (if MM_PERCENT_DEP)
input ulong        InpMagicNumber    = 123456;         // Magic number

input group "=== Distance / Trailing ==="
input ENUM_DIST_TYPE InpDistType     = DIST_ATR;       // Distance calculation type
input int          InpDistancePips   = 100;            // Fixed distance in points (if DIST_FIXED_POINTS)
input int          InpATRPeriod      = 14;             // ATR period (if DIST_ATR)
input double       InpATRMultiplier  = 2.0;            // ATR multiplier for distance
input int          InpReverseBufferPips = 30;          // Extra buffer for reverse order (noise protection)

input group "=== Trend Filter ==="
input bool         InpUseTrendFilter = true;            // Use trend filter
input int          InpFastMAPeriod   = 20;              // Fast MA period
input int          InpSlowMAPeriod   = 60;              // Slow MA period
input ENUM_MA_METHOD InpMAMethod     = MODE_EMA;         // Averaging method

input group "=== Regime Filter (ADX) ==="
input bool         InpUseADXFilter   = true;             // Only trade when the market is trending
input int          InpADXPeriod      = 14;               // ADX period
input double       InpADXThreshold   = 25.0;             // Minimum ADX value to consider the market trending

input group "=== Entry Protection ==="
input double       InpMaxSpreadPips  = 30;              // Maximum spread to allow entry (points), 0 = don't check

//--- Global variables
double  m_adjusted_distance = 0;
int     m_handle_fast_ma    = INVALID_HANDLE;
int     m_handle_slow_ma    = INVALID_HANDLE;
int     m_handle_atr        = INVALID_HANDLE;
int     m_handle_adx        = INVALID_HANDLE;

//+------------------------------------------------------------------+
//| Expert initialization function                                   |
//+------------------------------------------------------------------+
int OnInit()
  {
   if(!m_symbol.Name(_Symbol))
     {
      Print("Failed to initialize symbol data.");
      return(INIT_FAILED);
     }

   m_trade.SetExpertMagicNumber(InpMagicNumber);
   m_trade.SetTypeFillingBySymbol(_Symbol);

   // Default distance (used until ATR is computed / if fixed mode is selected)
   m_adjusted_distance = InpDistancePips * _Point;

   if(InpUseTrendFilter)
     {
      m_handle_fast_ma = iMA(_Symbol, PERIOD_CURRENT, InpFastMAPeriod, 0, InpMAMethod, PRICE_CLOSE);
      m_handle_slow_ma = iMA(_Symbol, PERIOD_CURRENT, InpSlowMAPeriod, 0, InpMAMethod, PRICE_CLOSE);
      if(m_handle_fast_ma == INVALID_HANDLE || m_handle_slow_ma == INVALID_HANDLE)
        {
         Print("Failed to create MA indicators.");
         return(INIT_FAILED);
        }
     }

   if(InpDistType == DIST_ATR)
     {
      m_handle_atr = iATR(_Symbol, PERIOD_CURRENT, InpATRPeriod);
      if(m_handle_atr == INVALID_HANDLE)
        {
         Print("Failed to create ATR indicator.");
         return(INIT_FAILED);
        }
     }

   if(InpUseADXFilter)
     {
      m_handle_adx = iADX(_Symbol, PERIOD_CURRENT, InpADXPeriod);
      if(m_handle_adx == INVALID_HANDLE)
        {
         Print("Failed to create ADX indicator.");
         return(INIT_FAILED);
        }
     }

   return(INIT_SUCCEEDED);
  }

//+------------------------------------------------------------------+
//| Expert deinitialization function                                 |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
  {
   if(m_handle_fast_ma != INVALID_HANDLE) IndicatorRelease(m_handle_fast_ma);
   if(m_handle_slow_ma != INVALID_HANDLE) IndicatorRelease(m_handle_slow_ma);
   if(m_handle_atr     != INVALID_HANDLE) IndicatorRelease(m_handle_atr);
   if(m_handle_adx     != INVALID_HANDLE) IndicatorRelease(m_handle_adx);
  }

//+------------------------------------------------------------------+
//| Returns current trend direction: 1=up, -1=down, 0=none/flat      |
//+------------------------------------------------------------------+
int GetTrendDirection()
  {
   if(!InpUseTrendFilter)
      return 0; // filter disabled - don't restrict direction

   double fast[2], slow[2];
   if(CopyBuffer(m_handle_fast_ma, 0, 0, 2, fast) < 2) return 0;
   if(CopyBuffer(m_handle_slow_ma, 0, 0, 2, slow) < 2) return 0;

   if(fast[0] > slow[0])
      return 1;   // uptrend
   if(fast[0] < slow[0])
      return -1;  // downtrend

   return 0;
  }

//+------------------------------------------------------------------+
//| Returns true if the market is currently trending (ADX filter)    |
//+------------------------------------------------------------------+
bool IsMarketTrending()
  {
   if(!InpUseADXFilter)
      return true; // filter disabled - always allow

   double adx[1];
   if(CopyBuffer(m_handle_adx, 0, 0, 1, adx) < 1)
      return false; // fail safe: no data -> treat as not trending

   return adx[0] >= InpADXThreshold;
  }

//+------------------------------------------------------------------+
//| Returns the current distance for SL / trailing                  |
//+------------------------------------------------------------------+
double GetDistance()
  {
   if(InpDistType == DIST_FIXED_POINTS)
      return InpDistancePips * _Point;

   double atr[1];
   if(CopyBuffer(m_handle_atr, 0, 0, 1, atr) < 1 || atr[0] <= 0)
      return m_adjusted_distance > 0 ? m_adjusted_distance : InpDistancePips * _Point; // fallback

   double dist = atr[0] * InpATRMultiplier;
   m_adjusted_distance = dist;
   return dist;
  }

//+------------------------------------------------------------------+
//| Spread check                                                     |
//+------------------------------------------------------------------+
bool SpreadOk()
  {
   if(InpMaxSpreadPips <= 0) return true;
   double spread_pips = (m_symbol.Ask() - m_symbol.Bid()) / _Point;
   return spread_pips <= InpMaxSpreadPips;
  }

//+------------------------------------------------------------------+
//| Logs failed trade operations                                     |
//+------------------------------------------------------------------+
void CheckResult(bool ok, const string action)
  {
   if(!ok)
     {
      PrintFormat("Trade operation failed [%s]: retcode=%d, comment=%s",
                  action, m_trade.ResultRetcode(), m_trade.ResultComment());
     }
  }

//+------------------------------------------------------------------+
//| Expert tick function                                             |
//+------------------------------------------------------------------+
void OnTick()
  {
   if(!m_symbol.RefreshRates()) return;

   double distance = GetDistance();
   bool   trending = IsMarketTrending();

   // Check for open positions with our Magic number
   bool has_position = false;
   ENUM_POSITION_TYPE pos_type = POSITION_TYPE_BUY;
   double pos_open = 0;
   double pos_sl = 0;
   ulong  pos_ticket = 0;

   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket > 0 && PositionGetString(POSITION_SYMBOL) == _Symbol &&
         PositionGetInteger(POSITION_MAGIC) == InpMagicNumber)
        {
         has_position = true;
         pos_type   = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
         pos_open   = PositionGetDouble(POSITION_PRICE_OPEN);
         pos_sl     = PositionGetDouble(POSITION_SL);
         pos_ticket = ticket;
         break; // We only manage one primary position
        }
     }

   // Check for pending orders with our Magic number
   bool has_stop_order = false;
   ulong order_ticket = 0;
   ENUM_ORDER_TYPE ord_type = ORDER_TYPE_BUY_STOP;

   for(int i = OrdersTotal() - 1; i >= 0; i--)
     {
      ulong ticket = OrderGetTicket(i);
      if(ticket > 0 && OrderGetString(ORDER_SYMBOL) == _Symbol &&
         OrderGetInteger(ORDER_MAGIC) == InpMagicNumber)
        {
         has_stop_order = true;
         order_ticket   = ticket;
         ord_type       = (ENUM_ORDER_TYPE)OrderGetInteger(ORDER_TYPE);
         break;
        }
     }

   //--- LOGIC 1: System start (no position and no order)
   if(!has_position && !has_stop_order)
     {
      if(!SpreadOk()) return;      // spread too wide - skip
      if(!trending) return;        // ADX filter: market is ranging - stand aside

      int trend = GetTrendDirection();
      if(InpUseTrendFilter && trend == 0)
         return; // trend undetermined - wait

      double lot = CalculateLot();

      if(!InpUseTrendFilter || trend == 1)
        {
         double sl = NormalizePrice(m_symbol.Ask() - distance); // protective SL set immediately on entry
         if(m_trade.Buy(lot, _Symbol, m_symbol.Ask(), sl, 0, "Initial Buy"))
            Print("Initial BUY position opened, SL=", sl);
         else
            CheckResult(false, "Initial Buy");
        }
      else if(trend == -1)
        {
         double sl = NormalizePrice(m_symbol.Bid() + distance);
         if(m_trade.Sell(lot, _Symbol, m_symbol.Bid(), sl, 0, "Initial Sell"))
            Print("Initial SELL position opened, SL=", sl);
         else
            CheckResult(false, "Initial Sell");
        }
      return;
     }

   //--- LOGIC 2: Position closed, pending reverse order still active
   if(!has_position && has_stop_order)
     {
      // If the market has stopped trending, or the trend has flipped against the
      // pending order's direction, cancel it and wait for a fresh signal instead
      // of blindly re-entering into a range or against the new trend.
      bool cancel = false;
      string reason = "";

      if(InpUseADXFilter && !trending)
        {
         cancel = true;
         reason = "market no longer trending (ADX)";
        }
      else if(InpUseTrendFilter)
        {
         int trend = GetTrendDirection();
         bool order_is_buy = (ord_type == ORDER_TYPE_BUY_STOP);
         bool mismatch = (order_is_buy && trend == -1) || (!order_is_buy && trend == 1);
         if(mismatch)
           {
            cancel = true;
            reason = "direction against trend";
           }
        }

      if(cancel)
        {
         if(m_trade.OrderDelete(order_ticket))
            Print("Reverse order cancelled: ", reason);
         else
            CheckResult(false, "OrderDelete (cancel)");
        }
      return;
     }

   //--- LOGIC 3: Manage open position and its synchronized stop order
   if(has_position)
     {
      // If the market has stopped trending while we're in a position, we still
      // manage the trailing stop (protect existing profit), but we stop
      // maintaining/placing the reverse order - no new trend trade should start
      // from a range.
      double current_ask = m_symbol.Ask();
      double current_bid = m_symbol.Bid();
      double lot = CalculateLot();
      double reverse_buffer = InpReverseBufferPips * _Point;
      bool allow_reverse_order = trending;

      if(pos_type == POSITION_TYPE_BUY)
        {
         double target_sl = NormalizePrice(current_bid - distance);

         // Trailing only moves UP, following price, and never lowers
         if(target_sl > pos_open && (pos_sl == 0 || target_sl > pos_sl))
           {
            if(target_sl != pos_sl)
               CheckResult(m_trade.PositionModify(pos_ticket, target_sl, 0), "PositionModify BUY");

            // Reverse SELL STOP is placed BELOW the SL level by the buffer amount,
            // so a single noise spike / spread doesn't instantly reverse the position.
            double reverse_price = NormalizePrice(target_sl - reverse_buffer);

            if(allow_reverse_order)
              {
               if(!has_stop_order)
                 {
                  CheckResult(m_trade.SellStop(lot, reverse_price, _Symbol, 0, 0, ORDER_TIME_GTC, 0, "Reverse SellStop"),
                              "SellStop create");
                 }
               else if(ord_type == ORDER_TYPE_SELL_STOP)
                 {
                  if(NormalizePrice(OrderGetDouble(ORDER_PRICE_OPEN)) != reverse_price)
                     CheckResult(m_trade.OrderModify(order_ticket, reverse_price, 0, 0, ORDER_TIME_GTC, 0),
                                 "OrderModify SellStop");
                 }
              }
            else if(has_stop_order)
              {
               // Market stopped trending - remove the pending reversal, keep only the trailing stop
               CheckResult(m_trade.OrderDelete(order_ticket), "OrderDelete (regime change)");
              }
           }
        }
      else if(pos_type == POSITION_TYPE_SELL)
        {
         double target_sl = NormalizePrice(current_ask + distance);

         if(target_sl < pos_open && (pos_sl == 0 || target_sl < pos_sl))
           {
            if(target_sl != pos_sl)
               CheckResult(m_trade.PositionModify(pos_ticket, target_sl, 0), "PositionModify SELL");

            double reverse_price = NormalizePrice(target_sl + reverse_buffer);

            if(allow_reverse_order)
              {
               if(!has_stop_order)
                 {
                  CheckResult(m_trade.BuyStop(lot, reverse_price, _Symbol, 0, 0, ORDER_TIME_GTC, 0, "Reverse BuyStop"),
                              "BuyStop create");
                 }
               else if(ord_type == ORDER_TYPE_BUY_STOP)
                 {
                  if(NormalizePrice(OrderGetDouble(ORDER_PRICE_OPEN)) != reverse_price)
                     CheckResult(m_trade.OrderModify(order_ticket, reverse_price, 0, 0, ORDER_TIME_GTC, 0),
                                 "OrderModify BuyStop");
                 }
              }
            else if(has_stop_order)
              {
               CheckResult(m_trade.OrderDelete(order_ticket), "OrderDelete (regime change)");
              }
           }
        }
     }
  }

//+------------------------------------------------------------------+
//| Lot size calculation based on money management settings          |
//+------------------------------------------------------------------+
double CalculateLot()
  {
   if(InpMMType == MM_FIXED_LOT)
     {
      return InpLotSize;
     }
   else
     {
      double free_margin = AccountInfoDouble(ACCOUNT_MARGIN_FREE);
      double margin_for_lot = 0;

      if(!OrderCalcMargin(ORDER_TYPE_BUY, _Symbol, 1.0, m_symbol.Ask(), margin_for_lot) || margin_for_lot <= 0)
        {
         return InpLotSize; // Safe fallback to default value on error
        }

      double calculated_lot = (free_margin * (InpRiskPercent / 100.0)) / margin_for_lot;

      double lot_step = m_symbol.LotsStep();
      calculated_lot = MathFloor(calculated_lot / lot_step) * lot_step;

      if(calculated_lot < m_symbol.LotsMin()) calculated_lot = m_symbol.LotsMin();
      if(calculated_lot > m_symbol.LotsMax()) calculated_lot = m_symbol.LotsMax();

      return calculated_lot;
     }
  }

//+------------------------------------------------------------------+
//| Normalize price to the trading server's requirements             |
//+------------------------------------------------------------------+
double NormalizePrice(double price)
  {
   double tick_size = m_symbol.TickSize();
   if(tick_size == 0) return price;
   return MathRound(price / tick_size) * tick_size;
  }
