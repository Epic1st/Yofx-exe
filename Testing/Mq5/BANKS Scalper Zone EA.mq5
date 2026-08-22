//+------------------------------------------------------------------+
//|  BANKS Scalper Zone EA                                           |
//|  Converted from Pine Script "BANKS scalper zone Config pilot"    |
//|  Logic: UT Bot trailing stop + Supply/Demand zones + ATR TP/SL   |
//+------------------------------------------------------------------+
#property copyright "BANKS Scalper Zone EA"
#property version   "1.00"
#property strict

#include <Trade\Trade.mqh>
#include <Arrays\ArrayDouble.mqh>

CTrade trade;

//--- UT Bot inputs
input double   InpBuySensitivity   = 2.0;    // Buy Sensitivity (Multiplier)
input int      InpBuyATRPeriod     = 1;       // Buy ATR Period (0=disable)
input double   InpSellSensitivity  = 2.0;    // Sell Sensitivity (Multiplier)
input int      InpSellATRPeriod    = 1;       // Sell ATR Period (0=disable)

//--- TP/SL inputs
input int      InpATRLength        = 14;      // ATR Length for TP/SL
input double   InpSLMultiplier     = 1.5;    // Stop Loss Multiplier (x ATR)
input double   InpTPMultiplier1    = 2.0;    // Take Profit 1 Multiplier (x ATR)
input double   InpTPMultiplier2    = 3.5;    // Take Profit 2 Multiplier (x ATR)

//--- Supply/Demand inputs
input int      InpPivotLen         = 15;      // Pivot Length
input double   InpZoneATRMult      = 0.6;    // Zone Height ATR Multiplier
input int      InpMaxZones         = 10;      // Max Active Zones
input string   InpMitigationMode   = "Wick"; // Mitigation Mode: Wick or Close
input bool     InpRemoveMitigated  = true;   // Remove Mitigated Zones

//--- Longevity Zone inputs
input int      InpLongevityLen     = 5;       // Longevity Length

//--- Trade management
input double   InpLotSize          = 0.01;   // Lot Size
input int      InpMagicNumber      = 20250424; // Magic Number
input int      InpSlippage         = 30;      // Max Slippage (points)
input bool     InpOneTradeOnly     = true;    // Only one trade at a time

//+------------------------------------------------------------------+
//| Supply/Demand Zone structure                                      |
//+------------------------------------------------------------------+
struct SDZone
{
   double top;
   double bot;
   datetime left_time;
   bool    active;
};

//+------------------------------------------------------------------+
//| Global variables                                                  |
//+------------------------------------------------------------------+
SDZone demandZones[];
SDZone supplyZones[];
int    demandCount = 0;
int    supplyCount = 0;

// UT Bot state
double trail_buy  = 0;
double trail_sell = 0;
bool   trail_buy_init  = false;
bool   trail_sell_init = false;
int    posState = 0; // 1=long, -1=short, 0=flat

// ATR handles
int    atrHandle_buy;
int    atrHandle_sell;
int    atrHandle_tp;
int    atrHandle_zone;
int    atrHandle_longevity;

// Tracking last processed bar
datetime lastBarTime = 0;

// Split TP management: track if TP1 was hit
struct OpenTrade
{
   ulong  ticket;
   bool   tp1_hit;
   double tp1_price;
   double tp2_price;
   double entry_price;
   bool   is_buy;
};
OpenTrade openTrades[];
int openTradeCount = 0;

//+------------------------------------------------------------------+
int OnInit()
{
   trade.SetExpertMagicNumber(InpMagicNumber);
   trade.SetDeviationInPoints(InpSlippage);
   trade.SetTypeFilling(ORDER_FILLING_IOC);

   atrHandle_buy      = iATR(_Symbol, PERIOD_CURRENT, MathMax(InpBuyATRPeriod,  1));
   atrHandle_sell     = iATR(_Symbol, PERIOD_CURRENT, MathMax(InpSellATRPeriod, 1));
   atrHandle_tp       = iATR(_Symbol, PERIOD_CURRENT, InpATRLength);
   atrHandle_zone     = iATR(_Symbol, PERIOD_CURRENT, 14);
   atrHandle_longevity= iATR(_Symbol, PERIOD_CURRENT, 20);

   if(atrHandle_buy == INVALID_HANDLE || atrHandle_sell == INVALID_HANDLE ||
      atrHandle_tp  == INVALID_HANDLE || atrHandle_zone == INVALID_HANDLE ||
      atrHandle_longevity == INVALID_HANDLE)
   {
      Print("ERROR: Could not create ATR handles");
      return INIT_FAILED;
   }

   ArrayResize(demandZones, InpMaxZones);
   ArrayResize(supplyZones, InpMaxZones);

   Print("BANKS Scalper Zone EA initialized on ", _Symbol);
   return INIT_SUCCEEDED;
}

//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   IndicatorRelease(atrHandle_buy);
   IndicatorRelease(atrHandle_sell);
   IndicatorRelease(atrHandle_tp);
   IndicatorRelease(atrHandle_zone);
   IndicatorRelease(atrHandle_longevity);
}

//+------------------------------------------------------------------+
void OnTick()
{
   // Only process on new confirmed bar
   datetime currentBar = iTime(_Symbol, PERIOD_CURRENT, 0);
   if(currentBar == lastBarTime) return;
   lastBarTime = currentBar;

   // Need enough bars
   int bars = iBars(_Symbol, PERIOD_CURRENT);
   int minBars = MathMax(InpPivotLen * 2 + 5, 100);
   if(bars < minBars) return;

   // Get ATR values (index 1 = last confirmed bar)
   double atrBuy[], atrSell[], atrTP[], atrZone[], atrLong[];
   if(CopyBuffer(atrHandle_buy,      0, 0, 3, atrBuy)      < 3) return;
   if(CopyBuffer(atrHandle_sell,     0, 0, 3, atrSell)     < 3) return;
   if(CopyBuffer(atrHandle_tp,       0, 0, 3, atrTP)       < 3) return;
   if(CopyBuffer(atrHandle_zone,     0, 0, 3, atrZone)     < 3) return;
   if(CopyBuffer(atrHandle_longevity,0, 0, 3, atrLong)     < 3) return;

   // Price data for last 3 confirmed bars
   double closes[], highs[], lows[], opens[];
   if(CopyClose (_Symbol, PERIOD_CURRENT, 1, 3, closes) < 3) return;
   if(CopyHigh  (_Symbol, PERIOD_CURRENT, 1, 3, highs)  < 3) return;
   if(CopyLow   (_Symbol, PERIOD_CURRENT, 1, 3, lows)   < 3) return;
   if(CopyOpen  (_Symbol, PERIOD_CURRENT, 1, 3, opens)  < 3) return;

   // closes[0] = bar -1 (most recent confirmed), closes[1] = bar -2, etc.
   double close1 = closes[0], close2 = closes[1];
   double high1  = highs[0],  high2  = highs[1];
   double low1   = lows[0],   low2   = lows[1];

   bool buyEnabled  = (InpBuyATRPeriod  > 0) && (InpBuySensitivity  > 0);
   bool sellEnabled = (InpSellATRPeriod > 0) && (InpSellSensitivity > 0);

   //--- Update Supply/Demand zones
   UpdateSDZones(atrZone);

   //--- Remove mitigated zones
   if(InpRemoveMitigated)
      RemoveMitigatedZones(close1, high1, low1);

   //--- UT Bot trailing calculations (on confirmed bar)
   double nLoss_buy  = buyEnabled  ? InpBuySensitivity  * atrBuy[1]  : 0;
   double nLoss_sell = sellEnabled ? InpSellSensitivity * atrSell[1] : 0;

   // Buy trail
   if(buyEnabled)
   {
      if(!trail_buy_init)
      {
         trail_buy = close1 - nLoss_buy;
         trail_buy_init = true;
      }
      else
      {
         double prev_trail = trail_buy;
         if(close1 > prev_trail && close2 > prev_trail)
            trail_buy = MathMax(prev_trail, close1 - nLoss_buy);
         else if(close1 < prev_trail && close2 < prev_trail)
            trail_buy = MathMin(prev_trail, close1 + nLoss_buy);
         else
            trail_buy = (close1 > prev_trail) ? close1 - nLoss_buy : close1 + nLoss_buy;
      }
   }

   // Sell trail
   if(sellEnabled)
   {
      if(!trail_sell_init)
      {
         trail_sell = close1 + nLoss_sell;
         trail_sell_init = true;
      }
      else
      {
         double prev_trail = trail_sell;
         if(close1 > prev_trail && close2 > prev_trail)
            trail_sell = MathMax(prev_trail, close1 - nLoss_sell);
         else if(close1 < prev_trail && close2 < prev_trail)
            trail_sell = MathMin(prev_trail, close1 + nLoss_sell);
         else
            trail_sell = (close1 > prev_trail) ? close1 - nLoss_sell : close1 + nLoss_sell;
      }
   }

   // Crossover detection: EMA(1) ≈ close itself
   // above_buy_cross  = close crosses above trail_buy
   // below_sell_cross = trail_sell crosses above close
   bool above_buy_cross  = buyEnabled  && (close1 > trail_buy)  && (close2 <= trail_buy);
   bool below_sell_cross = sellEnabled && (trail_sell > close1) && (trail_sell <= close2);

   bool buy_signal  = buyEnabled  && (close1 > trail_buy)  && above_buy_cross;
   bool sell_signal = sellEnabled && (close1 < trail_sell) && below_sell_cross;

   // One-signal-at-a-time state
   bool buy_confirmed  = buyEnabled  && buy_signal  && posState <= 0;
   bool sell_confirmed = sellEnabled && sell_signal && posState >= 0;

   if(buy_confirmed)  posState = 1;
   if(sell_confirmed) posState = -1;

   //--- Longevity pivot detection
   // highest/lowest over InpLongevityLen bars
   double h_highest = GetHighest(InpLongevityLen + 1);
   double l_lowest  = GetLowest (InpLongevityLen + 1);

   bool high_pivot = (high1 == GetHighest(InpLongevityLen, 1)) && (high1 > GetHighest(InpLongevityLen, 0));
   bool low_pivot  = (low1  == GetLowest (InpLongevityLen, 1)) && (low1  < GetLowest (InpLongevityLen, 0));

   //--- Check for trade entry signals
   double atrNow   = atrTP[1];
   double halfAtr  = atrLong[1] * 0.5;

   // SELL signal: high_pivot candle inside/touching Supply zone
   if(high_pivot && sellEnabled)
   {
      double pivotHigh = high1;
      double pivotLow  = low1;
      double pivotPrice = GetHighest(InpLongevityLen, 1);

      if(IsInsideSupplyZone(pivotPrice) || IsTouchingSupplyZone(pivotHigh, pivotLow))
      {
         if(!HasOpenPosition())
         {
            double entryPrice  = close1;
            double stopLoss    = high1;
            double takeProfit1 = entryPrice - (atrNow * InpTPMultiplier1);
            double takeProfit2 = entryPrice - (atrNow * InpTPMultiplier2);

            // Validate SL distance
            double minStop = SymbolInfoInteger(_Symbol, SYMBOL_TRADE_STOPS_LEVEL) * _Point;
            if((stopLoss - entryPrice) < minStop)
               stopLoss = entryPrice + minStop * 1.5;

            Print("SELL Signal | Entry:", entryPrice, " SL:", stopLoss,
                  " TP1:", takeProfit1, " TP2:", takeProfit2);

            if(trade.Sell(InpLotSize, _Symbol, 0, stopLoss, takeProfit2, "BANKS_SELL"))
            {
               ulong ticket = trade.ResultOrder();
               RegisterTrade(ticket, entryPrice, stopLoss, takeProfit1, takeProfit2, false);
            }
         }
      }
   }

   // BUY signal: low_pivot candle inside/touching Demand zone
   if(low_pivot && buyEnabled)
   {
      double pivotHigh = high1;
      double pivotLow  = low1;
      double pivotPrice = GetLowest(InpLongevityLen, 1);

      if(IsInsideDemandZone(pivotPrice) || IsTouchingDemandZone(pivotHigh, pivotLow))
      {
         if(!HasOpenPosition())
         {
            double entryPrice  = close1;
            double stopLoss    = low1;
            double takeProfit1 = entryPrice + (atrNow * InpTPMultiplier1);
            double takeProfit2 = entryPrice + (atrNow * InpTPMultiplier2);

            // Validate SL distance
            double minStop = SymbolInfoInteger(_Symbol, SYMBOL_TRADE_STOPS_LEVEL) * _Point;
            if((entryPrice - stopLoss) < minStop)
               stopLoss = entryPrice - minStop * 1.5;

            Print("BUY Signal | Entry:", entryPrice, " SL:", stopLoss,
                  " TP1:", takeProfit1, " TP2:", takeProfit2);

            if(trade.Buy(InpLotSize, _Symbol, 0, stopLoss, takeProfit2, "BANKS_BUY"))
            {
               ulong ticket = trade.ResultOrder();
               RegisterTrade(ticket, entryPrice, stopLoss, takeProfit1, takeProfit2, true);
            }
         }
      }
   }

   //--- Manage TP1 partial close
   ManageTP1();
}

//+------------------------------------------------------------------+
//| Update Supply/Demand zones from pivot highs/lows                 |
//+------------------------------------------------------------------+
void UpdateSDZones(double &atrZone[])
{
   int barsNeeded = InpPivotLen * 2 + 5;

   // Check for pivot high (supply zone)
   double ph = GetPivotHigh(InpPivotLen);
   if(ph > 0)
   {
      int pivIdx = InpPivotLen + 1; // offset: confirmed pivot is pivotLen bars back
      double baseHigh = iHigh(_Symbol, PERIOD_CURRENT, pivIdx);
      double atrVal   = atrZone[MathMin(pivIdx, 2)];
      double zoneSize = atrVal * InpZoneATRMult;

      double opn = iOpen (_Symbol, PERIOD_CURRENT, pivIdx);
      double cls = iClose(_Symbol, PERIOD_CURRENT, pivIdx);
      double bot = MathMax(opn, cls);
      double top = baseHigh;

      if((top - bot) < zoneSize) bot = top - zoneSize;

      AddSupplyZone(iTime(_Symbol, PERIOD_CURRENT, pivIdx), top, bot);
   }

   // Check for pivot low (demand zone)
   double pl = GetPivotLow(InpPivotLen);
   if(pl > 0)
   {
      int pivIdx = InpPivotLen + 1;
      double baseLow = iLow(_Symbol, PERIOD_CURRENT, pivIdx);
      double atrVal  = atrZone[MathMin(pivIdx, 2)];
      double zoneSize = atrVal * InpZoneATRMult;

      double opn = iOpen (_Symbol, PERIOD_CURRENT, pivIdx);
      double cls = iClose(_Symbol, PERIOD_CURRENT, pivIdx);
      double top = MathMin(opn, cls);
      double bot = baseLow;

      if((top - bot) < zoneSize) top = bot + zoneSize;

      AddDemandZone(iTime(_Symbol, PERIOD_CURRENT, pivIdx), top, bot);
   }
}

//+------------------------------------------------------------------+
//| Pivot High detection (returns pivot price or 0 if none)          |
//+------------------------------------------------------------------+
double GetPivotHigh(int len)
{
   int center = len + 1; // bars back
   double centerHigh = iHigh(_Symbol, PERIOD_CURRENT, center);

   for(int i = 1; i <= len; i++)
   {
      if(iHigh(_Symbol, PERIOD_CURRENT, center - i) >= centerHigh) return 0;
      if(iHigh(_Symbol, PERIOD_CURRENT, center + i) >= centerHigh) return 0;
   }
   return centerHigh;
}

//+------------------------------------------------------------------+
double GetPivotLow(int len)
{
   int center = len + 1;
   double centerLow = iLow(_Symbol, PERIOD_CURRENT, center);

   for(int i = 1; i <= len; i++)
   {
      if(iLow(_Symbol, PERIOD_CURRENT, center - i) <= centerLow) return 0;
      if(iLow(_Symbol, PERIOD_CURRENT, center + i) <= centerLow) return 0;
   }
   return centerLow;
}

//+------------------------------------------------------------------+
void AddDemandZone(datetime t, double top, double bot)
{
   // Check for duplicate (same time)
   for(int i = 0; i < demandCount; i++)
      if(demandZones[i].left_time == t) return;

   if(demandCount >= InpMaxZones)
   {
      // Remove oldest (shift left)
      for(int i = 0; i < demandCount - 1; i++)
         demandZones[i] = demandZones[i + 1];
      demandCount--;
   }
   demandZones[demandCount].top        = top;
   demandZones[demandCount].bot        = bot;
   demandZones[demandCount].left_time  = t;
   demandZones[demandCount].active     = true;
   demandCount++;
}

//+------------------------------------------------------------------+
void AddSupplyZone(datetime t, double top, double bot)
{
   for(int i = 0; i < supplyCount; i++)
      if(supplyZones[i].left_time == t) return;

   if(supplyCount >= InpMaxZones)
   {
      for(int i = 0; i < supplyCount - 1; i++)
         supplyZones[i] = supplyZones[i + 1];
      supplyCount--;
   }
   supplyZones[supplyCount].top        = top;
   supplyZones[supplyCount].bot        = bot;
   supplyZones[supplyCount].left_time  = t;
   supplyZones[supplyCount].active     = true;
   supplyCount++;
}

//+------------------------------------------------------------------+
void RemoveMitigatedZones(double close1, double high1, double low1)
{
   double demandTarget = (InpMitigationMode == "Close") ? close1 : low1;
   double supplyTarget = (InpMitigationMode == "Close") ? close1 : high1;

   // Remove mitigated demand zones (price broke below zone bottom)
   for(int i = demandCount - 1; i >= 0; i--)
   {
      if(demandTarget < demandZones[i].bot)
      {
         for(int j = i; j < demandCount - 1; j++)
            demandZones[j] = demandZones[j + 1];
         demandCount--;
      }
   }

   // Remove mitigated supply zones (price broke above zone top)
   for(int i = supplyCount - 1; i >= 0; i--)
   {
      if(supplyTarget > supplyZones[i].top)
      {
         for(int j = i; j < supplyCount - 1; j++)
            supplyZones[j] = supplyZones[j + 1];
         supplyCount--;
      }
   }
}

//+------------------------------------------------------------------+
bool IsInsideDemandZone(double price)
{
   for(int i = 0; i < demandCount; i++)
      if(demandZones[i].active && price >= demandZones[i].bot && price <= demandZones[i].top)
         return true;
   return false;
}

bool IsInsideSupplyZone(double price)
{
   for(int i = 0; i < supplyCount; i++)
      if(supplyZones[i].active && price >= supplyZones[i].bot && price <= supplyZones[i].top)
         return true;
   return false;
}

bool IsTouchingDemandZone(double candleHigh, double candleLow)
{
   for(int i = 0; i < demandCount; i++)
      if(demandZones[i].active && candleHigh >= demandZones[i].bot && candleLow <= demandZones[i].top)
         return true;
   return false;
}

bool IsTouchingSupplyZone(double candleHigh, double candleLow)
{
   for(int i = 0; i < supplyCount; i++)
      if(supplyZones[i].active && candleHigh >= supplyZones[i].bot && candleLow <= supplyZones[i].top)
         return true;
   return false;
}

//+------------------------------------------------------------------+
double GetHighest(int period, int startBar = 1)
{
   double val = iHigh(_Symbol, PERIOD_CURRENT, startBar);
   for(int i = startBar + 1; i <= startBar + period - 1; i++)
   {
      double h = iHigh(_Symbol, PERIOD_CURRENT, i);
      if(h > val) val = h;
   }
   return val;
}

double GetLowest(int period, int startBar = 1)
{
   double val = iLow(_Symbol, PERIOD_CURRENT, startBar);
   for(int i = startBar + 1; i <= startBar + period - 1; i++)
   {
      double l = iLow(_Symbol, PERIOD_CURRENT, i);
      if(l < val) val = l;
   }
   return val;
}

//+------------------------------------------------------------------+
bool HasOpenPosition()
{
   if(!InpOneTradeOnly) return false;
   for(int i = 0; i < PositionsTotal(); i++)
   {
      ulong ticket = PositionGetTicket(i);
      if(PositionSelectByTicket(ticket))
         if(PositionGetString(POSITION_SYMBOL) == _Symbol &&
            PositionGetInteger(POSITION_MAGIC) == InpMagicNumber)
            return true;
   }
   return false;
}

//+------------------------------------------------------------------+
void RegisterTrade(ulong ticket, double entry, double sl,
                   double tp1, double tp2, bool isBuy)
{
   if(openTradeCount >= ArraySize(openTrades))
      ArrayResize(openTrades, openTradeCount + 10);
   openTrades[openTradeCount].ticket      = ticket;
   openTrades[openTradeCount].tp1_hit     = false;
   openTrades[openTradeCount].tp1_price   = tp1;
   openTrades[openTradeCount].tp2_price   = tp2;
   openTrades[openTradeCount].entry_price = entry;
   openTrades[openTradeCount].is_buy      = isBuy;
   openTradeCount++;
}

//+------------------------------------------------------------------+
//| Manage TP1: partial close at TP1, move SL to breakeven           |
//+------------------------------------------------------------------+
void ManageTP1()
{
   double currentPrice = iClose(_Symbol, PERIOD_CURRENT, 1);

   for(int i = openTradeCount - 1; i >= 0; i--)
   {
      if(openTrades[i].tp1_hit) continue;

      ulong ticket = openTrades[i].ticket;
      if(!PositionSelectByTicket(ticket))
      {
         // Position closed — remove from list
         for(int j = i; j < openTradeCount - 1; j++)
            openTrades[j] = openTrades[j + 1];
         openTradeCount--;
         continue;
      }

      bool isBuy = openTrades[i].is_buy;
      double tp1 = openTrades[i].tp1_price;
      double entry = openTrades[i].entry_price;

      bool tp1_reached = isBuy ? (currentPrice >= tp1) : (currentPrice <= tp1);

      if(tp1_reached)
      {
         double volume = PositionGetDouble(POSITION_VOLUME);
         double closeVol = NormalizeDouble(volume * 0.5, 2);
         if(closeVol < SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN))
            closeVol = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);

         // Close half position at market
         if(isBuy)
            trade.Sell(closeVol, _Symbol, 0, 0, 0, "TP1_PARTIAL");
         else
            trade.Buy(closeVol, _Symbol, 0, 0, 0, "TP1_PARTIAL");

         // Move SL to breakeven
         if(PositionSelectByTicket(ticket))
         {
            double sl_new = entry;
            double tp2 = openTrades[i].tp2_price;
            trade.PositionModify(ticket, sl_new, tp2);
         }

         openTrades[i].tp1_hit = true;
         Print("TP1 hit for ticket ", ticket, " — partial close + SL moved to breakeven");
      }
   }
}

//+------------------------------------------------------------------+
void OnTradeTransaction(const MqlTradeTransaction& trans,
                        const MqlTradeRequest& request,
                        const MqlTradeResult& result)
{
   // Clean up closed positions from tracking array
   if(trans.type == TRADE_TRANSACTION_DEAL_ADD)
   {
      for(int i = openTradeCount - 1; i >= 0; i--)
      {
         if(!PositionSelectByTicket(openTrades[i].ticket))
         {
            for(int j = i; j < openTradeCount - 1; j++)
               openTrades[j] = openTrades[j + 1];
            openTradeCount--;
         }
      }
   }
}
//+------------------------------------------------------------------+
