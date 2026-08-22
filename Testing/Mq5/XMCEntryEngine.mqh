//+------------------------------------------------------------------+
//|                        XMCEntryEngine.mqh                          |
//|     Trend detection, distance calculation, lot sizing, layer entry |
//+------------------------------------------------------------------+
#ifndef __XMCENTRYENGINE_MQH__
#define __XMCENTRYENGINE_MQH__

#include <OrderExecution.mqh>
#include <XMCTradeUtils.mqh>

class CXMCEntryEngine
{
private:
   string   m_symbol;
   string   m_comment;
   int      m_magic;
   datetime m_lastTradeTime;
   datetime m_lastBarTime;

public:
   //--- Initialization ---
   void Init(string symbol, string comment, int magic)
   {
      m_symbol       = symbol;
      m_comment      = comment;
      m_magic         = magic;
      m_lastTradeTime = 0;
      m_lastBarTime   = 0;
   }

   void UpdateSymbol(string symbol) { m_symbol = symbol; }

   //--- EMA trend direction: OP_BUY, OP_SELL, or -1 ---
   int GetTrend(int fastPeriod, int slowPeriod)
   {
      double fast = iMA(m_symbol, 0, fastPeriod,
                        0, MODE_EMA, PRICE_CLOSE, 0);
      double slow = iMA(m_symbol, 0, slowPeriod,
                        0, MODE_EMA, PRICE_CLOSE, 0);

      if(fast > slow)
         return OP_BUY;

      if(fast < slow)
         return OP_SELL;

      return -1;
   }

   //--- ATR-based or fixed point distance ---
   double GetDistance(double atr, bool useAtr, double atrMult, double fallbackPts)
   {
      int digits = (int)MarketInfo(m_symbol, MODE_DIGITS);

      if(useAtr && atr > 0.0)
         return NormalizeDouble(atr * atrMult, digits);

      return NormalizeDouble(fallbackPts * _Point, digits);
   }

   //--- Layer-based lot sizing ---
   double CalcLot(int layer, double fixedLot, double multiplier)
   {
      double lot = fixedLot;

      if(layer > 0)
         lot *= MathPow(multiplier, layer);

      return XMC_NormalizeLot(m_symbol, lot);
   }

   //--- Open a buy/sell layer with reliable retry ---
   bool OpenLayer(int dir, double lot, string reason)
   {
      double price;

      if(dir == OP_BUY)
         price = MarketInfo(m_symbol, MODE_ASK);
      else
         price = MarketInfo(m_symbol, MODE_BID);

      color arrowColor = (dir == OP_BUY) ? clrBlue : clrRed;

      int ticket = XMC_OrderSendReliable(
         m_symbol,
         dir,
         lot,
         price,
         30,
         0,
         0,
         m_comment + "|" + reason,
         m_magic,
         0,
         arrowColor);

      if(ticket <= 0)
         return false;

      m_lastTradeTime = TimeCurrent();
      m_lastBarTime   = iTime(m_symbol, 0, 0);

      return true;
   }

   //--- Trade cooldown enforcement ---
   bool AllowTrade(int cooldown)
   {
      datetime tradeBar = iTime(m_symbol, 0, 0);

      if(tradeBar != m_lastBarTime)
         return true;

      return (TimeCurrent() - m_lastTradeTime >= cooldown);
   }
};

#endif // __XMCENTRYENGINE_MQH__
