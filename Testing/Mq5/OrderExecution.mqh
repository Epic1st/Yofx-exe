//+------------------------------------------------------------------+
//|                        OrderExecution.mqh                         |
//|              Generic MT4 order send/close with retry              |
//+------------------------------------------------------------------+
#ifndef __ORDEREXECUTION_MQH__
#define __ORDEREXECUTION_MQH__

//+------------------------------------------------------------------+
//| OrderSendReliable - send order with retry on transient errors    |
//+------------------------------------------------------------------+
int XMC_OrderSendReliable(
   string   symbol,
   int      cmd,
   double   volume,
   double   price,
   int      slippage,
   double   stoploss,
   double   takeprofit,
   string   comment,
   int      magic,
   datetime expiration,
   color    arrow_color)
{
   int maxAttempts = 5;
   int lastError   = 0;

   for(int attempt = 0; attempt < maxAttempts; attempt++)
   {
      int ticket = OrderSend(
         symbol,
         cmd,
         volume,
         price,
         slippage,
         stoploss,
         takeprofit,
         comment,
         magic,
         expiration,
         arrow_color);

      if(ticket > 0)
         return ticket;

      lastError = GetLastError();

      switch(lastError)
      {
         case 4:
         case 128:
         case 136:
         case 137:
         case 138:
         case 146:
            Sleep(500 + (attempt + 1) * 200);
            RefreshRates();

            if(cmd == OP_BUY)
               price = MarketInfo(symbol, MODE_ASK);
            else if(cmd == OP_SELL)
               price = MarketInfo(symbol, MODE_BID);

            break;

         default:
            Print("OrderSendReliable: Fatal error ", lastError);
            return -1;
      }
   }

   Print("OrderSendReliable: Failed after ",
         maxAttempts,
         " attempts. Last error: ",
         lastError);

   return -1;
}

//+------------------------------------------------------------------+
//| OrderCloseReliable - close order with retry on transient errors   |
//+------------------------------------------------------------------+
void XMC_OrderCloseReliable(
   int    ticket,
   double lots,
   double price,
   int    slippage)
{
   if(!OrderSelect(ticket, SELECT_BY_TICKET))
      return;

   int maxAttempts = 5;
   int lastError   = 0;

   string symbol = OrderSymbol();
   int    cmd    = OrderType();

   for(int attempt = 0; attempt < maxAttempts; attempt++)
   {
      if(OrderClose(ticket, lots, price, slippage, clrNONE))
         return;

      lastError = GetLastError();

      switch(lastError)
      {
         case 4:
         case 128:
         case 136:
         case 137:
         case 138:
         case 146:
            Sleep(500 + (attempt + 1) * 200);
            RefreshRates();

            if(cmd == OP_BUY)
               price = MarketInfo(symbol, MODE_BID);
            else if(cmd == OP_SELL)
               price = MarketInfo(symbol, MODE_ASK);

            break;

         default:
            Print("OrderCloseReliable: Fatal error ",
                  lastError,
                  " on ticket ",
                  ticket);
            return;
      }
   }

   Print("OrderCloseReliable: Failed after ",
         maxAttempts,
         " attempts on ticket ",
         ticket,
         ". Last error: ",
         lastError);
}

#endif // __ORDEREXECUTION_MQH__
