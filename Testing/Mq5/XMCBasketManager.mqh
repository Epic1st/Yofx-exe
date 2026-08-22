//+------------------------------------------------------------------+
//|                       XMCBasketManager.mqh                         |
//|    Basket state, profit tracking, synthetic trail, exit logic     |
//+------------------------------------------------------------------+
#ifndef __XMCBASKETMANAGER_MQH__
#define __XMCBASKETMANAGER_MQH__

#include <AdaptiveSuperTrendEngine.mqh>
#include <OrderExecution.mqh>
#include <XMCTradeUtils.mqh>

//+------------------------------------------------------------------+
enum ENUM_SYSTEM_STATE
{
   STATE_SCOUT,
   STATE_TREND,
   STATE_RECOVERY,
   STATE_LOCKDOWN
};

//+------------------------------------------------------------------+
class CXMCBasketManager
{
private:
   string m_symbols[6];
   int    m_count;
   int    m_magic;
   string m_tradeSymbol;

   //--- Basket state ---
   int    m_buyLayers;
   int    m_sellLayers;
   double m_lastBuyPrice;
   double m_lastSellPrice;
   double m_currentProfit;
   double m_swingProfit;
   int    m_swingCount;
   double m_peakProfit;
   bool   m_trailingActive;

public:
   //--- Initialization ---
   void Init(string &symbols[], int count, int magic)
   {
      m_count = MathMin(count, 6);
      m_magic = magic;

      for(int i = 0; i < 6; i++)
      {
         if(i < m_count)
            m_symbols[i] = symbols[i];
         else
            m_symbols[i] = "";
      }

      ResetState();
   }

   void SetTradeSymbol(string symbol) { m_tradeSymbol = symbol; }

   //--- Refresh basket state from open orders ---
   void Refresh()
   {
      m_buyLayers      = 0;
      m_sellLayers     = 0;
      m_lastBuyPrice   = 0.0;
      m_lastSellPrice  = 0.0;

      m_currentProfit  = 0.0;
      m_swingProfit    = 0.0;
      m_swingCount     = 0;

      datetime latest_buy_time  = 0;
      datetime latest_sell_time = 0;

      for(int i = OrdersTotal() - 1; i >= 0; i--)
      {
         if(!OrderSelect(i, SELECT_BY_POS))
            continue;

         if(OrderMagicNumber() != m_magic)
            continue;

         if(!XMC_IsManagedSymbol(OrderSymbol(), m_symbols, m_count))
            continue;

         double profit =
            OrderProfit() +
            OrderSwap() +
            OrderCommission();

         m_currentProfit += profit;

         if(StringFind(OrderComment(), "SWING") >= 0)
         {
            m_swingProfit += profit;
            m_swingCount++;
         }

         if(OrderSymbol() != m_tradeSymbol)
            continue;

         if(OrderType() == OP_BUY)
         {
            m_buyLayers++;

            if(OrderOpenTime() > latest_buy_time)
            {
               latest_buy_time  = OrderOpenTime();
               m_lastBuyPrice   = OrderOpenPrice();
            }
         }
         else if(OrderType() == OP_SELL)
         {
            m_sellLayers++;

            if(OrderOpenTime() > latest_sell_time)
            {
               latest_sell_time  = OrderOpenTime();
               m_lastSellPrice   = OrderOpenPrice();
            }
         }
      }
   }

   //--- Determine system state from basket + volatility ---
   //--- atrValue: pass pre-calculated ATR, or 0 to calculate internally
   ENUM_SYSTEM_STATE GetSystemState(
      int    maxLayers,
      double maxVolFactor,
      double atrValue     = 0.0,
      int    atrPeriod     = 14,
      int    atrSmoothing  = 20,
      int    adxPeriod      = 14,
      int    adxStrength    = 25)
   {
      if(m_buyLayers >= maxLayers || m_sellLayers >= maxLayers)
         return STATE_LOCKDOWN;

      double atr = atrValue;
      if(atr <= 0.0)
         atr = iATR(m_tradeSymbol, 0, atrPeriod, 0);

      double avg_atr = iATR(m_tradeSymbol, 0, atrSmoothing, 1);

      if(avg_atr > 0.0 && atr > avg_atr * maxVolFactor)
         return STATE_LOCKDOWN;

      if(m_buyLayers > 1 || m_sellLayers > 1)
         return STATE_RECOVERY;

      if(m_buyLayers == 1 || m_sellLayers == 1)
      {
         if(iADX(m_tradeSymbol, 0, adxPeriod, PRICE_CLOSE, MODE_MAIN, 0) >
            adxStrength)
         {
            return STATE_TREND;
         }
      }

      return STATE_SCOUT;
   }

   //--- Check synthetic dollar-trail and AST flip exits ---
   bool CheckSyntheticExit(
      double              trailStart,
      double              trailDist,
      bool                astExitEnabled,
      bool                astExitRequireProfit,
      double              astExitMinProfit,
      CAdaptiveSuperTrend &ast[],
      int                 astCount,
      ENUM_TIMEFRAMES     astTF)
   {
      if(m_currentProfit > m_peakProfit)
         m_peakProfit = m_currentProfit;

      if(m_currentProfit >= trailStart)
         m_trailingActive = true;

      if(m_trailingActive &&
         m_currentProfit < m_peakProfit - trailDist)
      {
         LogExit("DollarTrail");
         CloseAll();

         if(XMC_CountPortfolioOrders(m_symbols, m_count, m_magic) == 0)
            ResetState();

         return true;
      }

      if(astExitEnabled)
      {
         int astIndex = -1;

         for(int i = 0; i < astCount; i++)
         {
            if(m_symbols[i] == m_tradeSymbol)
            {
               astIndex = i;
               break;
            }
         }

         if(astIndex >= 0 && ast[astIndex].IsTrendChange(1))
         {
            int astDir = ast[astIndex].GetDirection(1);

            bool flip_against_buys  = (m_buyLayers > 0 && astDir == -1);
            bool flip_against_sells = (m_sellLayers > 0 && astDir == 1);

            if(flip_against_buys || flip_against_sells)
            {
               bool profit_ok =
                  !astExitRequireProfit ||
                  m_currentProfit >= astExitMinProfit;

               if(profit_ok || m_trailingActive)
               {
                  LogExit("ASTFlip " + EnumToString(astTF));
                  CloseAll();

                  if(XMC_CountPortfolioOrders(m_symbols, m_count, m_magic) == 0)
                     ResetState();

                  return true;
               }
            }
         }
      }

      return false;
   }

   //--- Close all managed orders ---
   void CloseAll()
   {
      for(int i = OrdersTotal() - 1; i >= 0; i--)
      {
         if(!OrderSelect(i, SELECT_BY_POS))
            continue;

         if(OrderMagicNumber() != m_magic)
            continue;

         if(!XMC_IsManagedSymbol(OrderSymbol(), m_symbols, m_count))
            continue;

         string symbol = OrderSymbol();

         double bid = MarketInfo(symbol, MODE_BID);
         double ask = MarketInfo(symbol, MODE_ASK);

         if(OrderType() == OP_BUY)
         {
            XMC_OrderCloseReliable(
               OrderTicket(),
               OrderLots(),
               bid,
               30);
         }
         else if(OrderType() == OP_SELL)
         {
            XMC_OrderCloseReliable(
               OrderTicket(),
               OrderLots(),
               ask,
               30);
         }
      }
   }

   //--- Close only swing trades ---
   void CloseSwingTrades()
   {
      for(int i = OrdersTotal() - 1; i >= 0; i--)
      {
         if(!OrderSelect(i, SELECT_BY_POS))
            continue;

         if(OrderMagicNumber() != m_magic)
            continue;

         if(StringFind(OrderComment(), "SWING") < 0)
            continue;

         if(!XMC_IsManagedSymbol(OrderSymbol(), m_symbols, m_count))
            continue;

         string symbol = OrderSymbol();

         double bid = MarketInfo(symbol, MODE_BID);
         double ask = MarketInfo(symbol, MODE_ASK);

         if(OrderType() == OP_BUY)
         {
            XMC_OrderCloseReliable(
               OrderTicket(),
               OrderLots(),
               bid,
               30);
         }
         else if(OrderType() == OP_SELL)
         {
            XMC_OrderCloseReliable(
               OrderTicket(),
               OrderLots(),
               ask,
               30);
         }
      }
   }

   //--- Log exit details ---
   void LogExit(string reason)
   {
      Print(
         "EXIT[", reason, "] ",
         "Symbol=", m_tradeSymbol,
         " Profit=", DoubleToString(m_currentProfit, 2),
         " Peak=", DoubleToString(m_peakProfit, 2),
         " Buys=", m_buyLayers,
         " Sells=", m_sellLayers);
   }

   //--- Reset all basket state ---
   void ResetState()
   {
      m_peakProfit     = 0.0;
      m_trailingActive = false;
      m_buyLayers      = 0;
      m_sellLayers     = 0;
      m_lastBuyPrice   = 0.0;
      m_lastSellPrice  = 0.0;
      m_currentProfit  = 0.0;
      m_swingProfit    = 0.0;
      m_swingCount     = 0;
   }

   //--- Accessors ---
   int    GetBuyLayers()      { return m_buyLayers; }
   int    GetSellLayers()     { return m_sellLayers; }
   double GetLastBuyPrice()   { return m_lastBuyPrice; }
   double GetLastSellPrice()  { return m_lastSellPrice; }
   double GetCurrentProfit() { return m_currentProfit; }
   double GetSwingProfit()    { return m_swingProfit; }
   int    GetSwingCount()     { return m_swingCount; }
};

#endif // __XMCBASKETMANAGER_MQH__
