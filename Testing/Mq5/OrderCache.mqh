//+------------------------------------------------------------------+
//| OrderCache.mqh                                                    |
//| Shared EA Order Cache                                             |
//+------------------------------------------------------------------+

#ifndef ORDER_CACHE_MQH
#define ORDER_CACHE_MQH

//+------------------------------------------------------------------+
//| Configuration - Override these before including if needed         |
//+------------------------------------------------------------------+
#ifndef ENGINE_COUNT
   #define ENGINE_COUNT 10
#endif

// BaseMagicInput must be defined externally as an input variable
// or define a default here:
#ifndef BASE_MAGIC_DEFAULT
   #define BASE_MAGIC_DEFAULT 1000
#endif

//+------------------------------------------------------------------+
//| MQL4/MQL5 Compatibility Macros                                   |
//+------------------------------------------------------------------+
#ifdef __MQL5__
   // MQL5 uses Positions instead of Orders for open trades
   #define ORDER_TOTAL_FUNC()     PositionsTotal()
   #define ORDER_SELECT_FUNC(i)   PositionSelectByTicket(PositionGetTicket(i))
   #define ORDER_SYMBOL_FUNC()    PositionGetString(POSITION_SYMBOL)
   #define ORDER_MAGIC_FUNC()     (int)PositionGetInteger(POSITION_MAGIC)
#else
   // MQL4
   #define ORDER_TOTAL_FUNC()     OrdersTotal()
   #define ORDER_SELECT_FUNC(i)   OrderSelect(i, SELECT_BY_POS, MODE_TRADES)
   #define ORDER_SYMBOL_FUNC()    OrderSymbol()
   #define ORDER_MAGIC_FUNC()     OrderMagicNumber()
#endif

//+------------------------------------------------------------------+
//| Shared Order Cache                                                |
//+------------------------------------------------------------------+
int g_CachedEngineOrders[ENGINE_COUNT];
int g_CachedEAOpenOrders = 0;
datetime g_CachedOrderBar = 0;

//+------------------------------------------------------------------+
//| Build Shared Order Cache                                          |
//+------------------------------------------------------------------+
void BuildSharedOrderCache(datetime currentBar, int baseMagic = BASE_MAGIC_DEFAULT)
{
   if(g_CachedOrderBar == currentBar)
      return;

   g_CachedOrderBar = currentBar;
   g_CachedEAOpenOrders = 0;

   for(int i = 0; i < ENGINE_COUNT; i++)
      g_CachedEngineOrders[i] = 0;

   const int magicLow  = baseMagic * 10;
   const int magicHigh = magicLow + 9;

   for(int i = ORDER_TOTAL_FUNC() - 1; i >= 0; i--)
   {
      if(!ORDER_SELECT_FUNC(i))
         continue;

      if(ORDER_SYMBOL_FUNC() != Symbol())
         continue;

      int magic = ORDER_MAGIC_FUNC();

      if(magic < magicLow || magic > magicHigh)
         continue;

      g_CachedEAOpenOrders++;

      int engineIndex;

      if(magic == magicLow)
         engineIndex = ENGINE_COUNT - 1;
      else
         engineIndex = magic - magicLow - 1;

      if(engineIndex >= 0 && engineIndex < ENGINE_COUNT)
         g_CachedEngineOrders[engineIndex]++;
   }
}

//+------------------------------------------------------------------+
//| EA-wide Order Helpers                                             |
//+------------------------------------------------------------------+
int CountEAOpenOrders()
{
   return g_CachedEAOpenOrders;
}

//+------------------------------------------------------------------+
//| Check Whether Another Engine Has an Open Trade                   |
//+------------------------------------------------------------------+
bool HasOtherEngineTrade(int myMagic, int baseMagic = BASE_MAGIC_DEFAULT)
{
   int engineIndex;

   if(myMagic == baseMagic * 10)
      engineIndex = ENGINE_COUNT - 1;
   else
      engineIndex = myMagic - (baseMagic * 10) - 1;

   if(engineIndex < 0 || engineIndex >= ENGINE_COUNT)
      return (g_CachedEAOpenOrders > 0);

   return (g_CachedEAOpenOrders - g_CachedEngineOrders[engineIndex]) > 0;
}

//+------------------------------------------------------------------+
//| Get cached count for specific engine                             |
//+------------------------------------------------------------------+
int GetEngineOrderCount(int engineIndex)
{
   if(engineIndex >= 0 && engineIndex < ENGINE_COUNT)
      return g_CachedEngineOrders[engineIndex];
   return 0;
}

#endif