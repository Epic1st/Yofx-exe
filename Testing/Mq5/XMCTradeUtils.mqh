//+------------------------------------------------------------------+
//|                          XMCTradeUtils.mqh                         |
//|            Reusable trade utility functions (no EA globals)      |
//+------------------------------------------------------------------+
#ifndef __XMCTRADEUTILS_MQH__
#define __XMCTRADEUTILS_MQH__

//+------------------------------------------------------------------+
//| XMC_IsManagedSymbol - check if symbol is in the managed list     |
//+------------------------------------------------------------------+
bool XMC_IsManagedSymbol(string symbol, string &symbols[], int count)
{
   for(int i = 0; i < count; i++)
   {
      if(symbols[i] == "")
         continue;

      if(symbols[i] == symbol)
         return true;
   }

   return false;
}

//+------------------------------------------------------------------+
//| XMC_NormalizeLot - clamp lot to broker min/step/max              |
//+------------------------------------------------------------------+
double XMC_NormalizeLot(string symbol, double lot)
{
   double step = MarketInfo(symbol, MODE_LOTSTEP);
   double min  = MarketInfo(symbol, MODE_MINLOT);
   double max  = MarketInfo(symbol, MODE_MAXLOT);

   if(step > 0.0)
      lot = MathFloor(lot / step) * step;

   if(lot < min)
      lot = min;

   if(lot > max)
      lot = max;

   return lot;
}

//+------------------------------------------------------------------+
//| XMC_IsSpreadTooHigh - check if spread exceeds threshold          |
//+------------------------------------------------------------------+
bool XMC_IsSpreadTooHigh(string symbol, int maxSpreadPoints)
{
   return (MarketInfo(symbol, MODE_SPREAD) > maxSpreadPoints);
}

//+------------------------------------------------------------------+
//| XMC_CountOrders - count open orders for symbol+magic             |
//+------------------------------------------------------------------+
int XMC_CountOrders(string symbol, int magic)
{
   int count = 0;

   for(int i = OrdersTotal() - 1; i >= 0; i--)
   {
      if(OrderSelect(i, SELECT_BY_POS) &&
         OrderSymbol() == symbol &&
         OrderMagicNumber() == magic)
      {
         count++;
      }
   }

   return count;
}

//+------------------------------------------------------------------+
//| XMC_CountPortfolioOrders - count all managed open orders        |
//+------------------------------------------------------------------+
int XMC_CountPortfolioOrders(string &symbols[], int count, int magic)
{
   int total = 0;

   for(int i = OrdersTotal() - 1; i >= 0; i--)
   {
      if(!OrderSelect(i, SELECT_BY_POS))
         continue;

      if(OrderMagicNumber() != magic)
         continue;

      if(XMC_IsManagedSymbol(OrderSymbol(), symbols, count))
         total++;
   }

   return total;
}

#endif // __XMCTRADEUTILS_MQH__
