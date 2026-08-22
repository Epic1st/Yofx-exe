//+------------------------------------------------------------------+
//| Ashu_Gold_EA_FINAL_V3.mq5 |
//| Strategy: EMA50+EMA200+VWAP+FVG+ATR |
//| Features: AlertMode + PartialTP + Breakeven + NewsFilter |
//| Timeframe: 15M XAUUSD | Build: 2026 |
//+------------------------------------------------------------------+
#property copyright "Ashu Strategy Final v3"
#property version "3.10"
#property strict

#include <Trade\Trade.mqh>
CTrade trade;

//--- Input Parameters
input group "=== Trading Mode ==="
input bool AlertOnlyMode = false;
input bool EnableMobileNotify = true;

input group "=== Risk Management ==="
input double RiskPercent = 1.0; // Risk per trade %
input double ATR_SL_Mult = 1.5; // SL = 1.5 * ATR
input double RR_Ratio = 2.5; // Final TP RR

input group "=== Partial TP & Breakeven ==="
input bool UsePartialTP = true;
input double PartialTP_R = 1.5; // Close 50% at 1.5R
input double PartialPercent = 50.0; // % to close
input bool UseBreakeven = true;
input double Breakeven_R = 1.0; // Move to BE at 1R
input int BE_PlusPoints = 5; // BE + 5 points lock

input group "=== News Filter ==="
input bool UseNewsFilter = true;
input int NewsMinutesBefore = 30;
input int NewsMinutesAfter = 30;
input bool FilterHighImpact = true;
input bool FilterMediumImpact = false;

input group "=== Indicator Settings ==="
input int EMA50_Period = 50;
input int EMA200_Period = 200;
input int ATR_Period = 14;
input bool UseVWAP = true;
input bool UseFVG = true;

input group "=== Session Filter ==="
input bool TradeLondon = true; // 07:00-16:00 GMT
input bool TradeNewYork = true; // 13:00-22:00 GMT
input int MaxSpreadPoints = 30; // Max spread for Gold
input int MagicNumber = 31102026; // Unique Magic

//--- Global Variables
int handleEMA50, handleEMA200, handleATR;
double ema50[], ema200[], atr[];
string gvPartialName; // Global Variable for Partial TP tracking

//+------------------------------------------------------------------+
int OnInit()
{
   handleEMA50 = iMA(_Symbol, PERIOD_CURRENT, EMA50_Period, 0, MODE_EMA, PRICE_CLOSE);
   handleEMA200 = iMA(_Symbol, PERIOD_CURRENT, EMA200_Period, 0, MODE_EMA, PRICE_CLOSE);
   handleATR = iATR(_Symbol, PERIOD_CURRENT, ATR_Period);

   if(handleEMA50 == INVALID_HANDLE  handleEMA200 == INVALID_HANDLE  handleATR == INVALID_HANDLE)
   {
      Print("Error: Indicator handles failed");
      return(INIT_FAILED);
   }

   trade.SetExpertMagicNumber(MagicNumber);
   gvPartialName = "Ashu_Partial_" + _Symbol + "_" + (string)MagicNumber;
   
   if(AlertOnlyMode) Print("EA ALERT ONLY MODE - No trades will execute");
   else Print("EA LIVE TRADE MODE - Risk: ", RiskPercent, "%");
   
   return(INIT_SUCCEEDED);
}

//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   Comment("");
}

//+------------------------------------------------------------------+
void OnTick()
{
   //--- Manage positions every tick for BE & Partial
   ManageOpenPositions();
   
   //--- New bar check for entries
   static datetime lastBarTime = 0;
   datetime currentBarTime = iTime(_Symbol, PERIOD_CURRENT, 0);
   if(lastBarTime == currentBarTime) return;
   lastBarTime = currentBarTime;

   if(!GetIndicatorData()) return;
   if(!CanTrade()) return;
   if(HasOpenPosition()) return;
   
   string newsEvent = "";
   if(UseNewsFilter && IsNewsTime(newsEvent))
   {
      Comment("News Filter Active: ", newsEvent, " | Trading Paused");
      return;
   }
   else Comment("");

   //--- Signal Check
   if(CheckBuySetup())
   {
      if(AlertOnlyMode) SendAlert("BUY SIGNAL", "XAUUSD 15M | EMA+VWAP+FVG Bullish");
      else ExecuteBuy();
   }

   if(CheckSellSetup())
   {
      if(AlertOnlyMode) SendAlert("SELL SIGNAL", "XAUUSD 15M | EMA+VWAP+FVG Bearish");
      else ExecuteSell();
   }
}

//+------------------------------------------------------------------+
void ManageOpenPositions()
{
   if(!PositionSelect(_Symbol)) 
   {
      GlobalVariableDel(gvPartialName); // Reset when no position
      return;
   }
   if(PositionGetInteger(POSITION_MAGIC)!= MagicNumber) return;
double openPrice = PositionGetDouble(POSITION_PRICE_OPEN);
   double currentSL = PositionGetDouble(POSITION_SL);
   double currentTP = PositionGetDouble(POSITION_TP);
   double currentPrice = PositionGetDouble(POSITION_PRICE_CURRENT);
   long type = PositionGetInteger(POSITION_TYPE);
   double lotSize = PositionGetDouble(POSITION_VOLUME);
   double riskDistance = MathAbs(openPrice - currentSL);
   if(riskDistance < 10 * _Point) return; // Avoid zero division

   //--- 1. Breakeven Logic
   if(UseBreakeven)
   {
      double beLevel = openPrice;
      if(type == POSITION_TYPE_BUY)
      {
         beLevel += BE_PlusPoints * _Point * 10; // Gold: 1 point = 0.10
         if(currentPrice >= openPrice + riskDistance * Breakeven_R && currentSL < beLevel - 1 * _Point)
         {
            if(trade.PositionModify(_Symbol, NormalizeDouble(beLevel, _Digits), currentTP))
               Print("Breakeven moved to: ", beLevel);
         }
      }
      else
      {
         beLevel -= BE_PlusPoints * _Point * 10;
         if(currentPrice <= openPrice - riskDistance * Breakeven_R && currentSL > beLevel + 1 * _Point)
         {
            if(trade.PositionModify(_Symbol, NormalizeDouble(beLevel, _Digits), currentTP))
               Print("Breakeven moved to: ", beLevel);
         }
      }
   }

   //--- 2. Partial TP Logic - Only once per trade
   if(UsePartialTP && GlobalVariableGet(gvPartialName) == 0)
   {
      double partialTPLevel = 0;
      bool shouldPartial = false;
      
      if(type == POSITION_TYPE_BUY)
      {
         partialTPLevel = openPrice + riskDistance * PartialTP_R;
         shouldPartial = currentPrice >= partialTPLevel;
      }
      else
      {
         partialTPLevel = openPrice - riskDistance * PartialTP_R;
         shouldPartial = currentPrice <= partialTPLevel;
      }
      
      if(shouldPartial)
      {
         double closeLots = NormalizeDouble(lotSize * PartialPercent / 100.0, 2);
         closeLots = MathMax(SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN), closeLots);
         closeLots = MathMin(lotSize, closeLots);
         
         if(trade.PositionClosePartial(_Symbol, closeLots))
         {
            GlobalVariableSet(gvPartialName, 1); // Mark partial done
            Print("Partial TP Executed: ", PartialPercent, "% closed at ", PartialTP_R, "R");
         }
      }
   }
}

//+------------------------------------------------------------------+
bool IsNewsTime(string &eventName)
{
   MqlCalendarValue values[];
   datetime timeNow = TimeCurrent();
   datetime timeStart = timeNow - NewsMinutesAfter * 60;
   datetime timeEnd = timeNow + NewsMinutesBefore * 60;

   if(CalendarValueHistory(values, timeStart, timeEnd, NULL, "USD"))
   {
      for(int i = 0; i < ArraySize(values); i++)
      {
         MqlCalendarEvent event;
         CalendarEventById(values[i].event_id, event);
         
         if(FilterHighImpact && event.importance == CALENDAR_IMPORTANCE_HIGH) 
         {
            eventName = event.name;
            return true;
         }
         if(FilterMediumImpact && event.importance == CALENDAR_IMPORTANCE_MODERATE)
         {
            eventName = event.name;
            return true;
         }
      }
   }
   return false;
}

//+------------------------------------------------------------------+
void SendAlert(string title, string message)
{
   string fullMsg = title + " | " + _Symbol + " | " + message;
   Alert(fullMsg);
   if(EnableMobileNotify) SendNotification(fullMsg);
   Print(fullMsg);
}

//+------------------------------------------------------------------+
bool HasOpenPosition()
{
   if(PositionSelect(_Symbol))
      return PositionGetInteger(POSITION_MAGIC) == MagicNumber;
   return false;
}

//+------------------------------------------------------------------+
bool GetIndicatorData()
{
   ArraySetAsSeries(ema50, true); ArraySetAsSeries(ema200, true); ArraySetAsSeries(atr, true);
   if(CopyBuffer(handleEMA50, 0, 0, 3, ema50) <= 0) return false;
   if(CopyBuffer(handleEMA200, 0, 0, 3, ema200) <= 0) return false;
   if(CopyBuffer(handleATR, 0, 0, 3, atr) <= 0) return false;
   return true;
}

//+------------------------------------------------------------------+
bool CanTrade()
{
   if(SymbolInfoInteger(_Symbol, SYMBOL_SPREAD) > MaxSpreadPoints) 
   {
      Comment("Spread too high: ", SymbolInfoInteger(_Symbol, SYMBOL_SPREAD));
      return false;
   }
   
   MqlDateTime dt; TimeToStruct(TimeCurrent(), dt); int hour = dt.hour;
   bool londonSession = (hour >= 7 && hour < 16);
   bool nySession = (hour >= 13 && hour < 22);
   
   if(TradeLondon && londonSession) return true;
   if(TradeNewYork && nySession) return true;
   
   Comment("Outside Trading Sessions");
   return false;
}

//+------------------------------------------------------------------+
double CalculateVWAP(int shift)
{
   double sumPV = 0, sumV = 0;
   datetime dayStart = StringToTime(TimeToString(iTime(_Symbol, PERIOD_CURRENT, shift), TIME_DATE));
   
   for(int i = shift; i < Bars(_Symbol, PERIOD_CURRENT); i++)
   {
      if(iTime(_Symbol, PERIOD_CURRENT, i) < dayStart) break;
      double typical = (iHigh(_Symbol, PERIOD_CURRENT, i) + iLow(_Symbol, PERIOD_CURRENT, i) + iClose(_Symbol, PERIOD_CURRENT, i)) / 3.0;
      long vol = iTickVolume(_Symbol, PERIOD_CURRENT, i);
      sumPV += typical * vol; sumV += vol;
   }
   return sumV > 0? sumPV / sumV : iClose(_Symbol, PERIOD_CURRENT, shift);
}

//+------------------------------------------------------------------+
bool CheckBullishFVG(int shift)
{
   if(!UseFVG) return true;
   double low1 = iLow(_Symbol, PERIOD_CURRENT, shift + 1);
   double high3 = iHigh(_Symbol, PERIOD_CURRENT, shift + 3);
   double currentClose = iClose(_Symbol, PERIOD_CURRENT, shift);
   return (low1 > high3 && currentClose <= low1 && currentClose >= high3);
}

//+------------------------------------------------------------------+
bool CheckBearishFVG(int shift)
{
   if(!UseFVG) return true;
   double high1 = iHigh(_Symbol, PERIOD_CURRENT, shift + 1);
   double low3 = iLow(_Symbol, PERIOD_CURRENT, shift + 3);
   double currentClose = iClose(_Symbol, PERIOD_CURRENT, shift);
   return (high1 < low3 && currentClose >= high1 && currentClose <= low3);
}

//+------------------------------------------------------------------+
bool CheckBuySetup()
{
   double close1 = iClose(_Symbol, PERIOD_CURRENT, 1); 
   double vwap = CalculateVWAP(1);
   bool trendUp = close1 > ema50[1] && ema50[1] > ema200[1];
   bool aboveVWAP =!UseVWAP || close1 > vwap;
   bool inFVG = CheckBullishFVG(1);
   bool bullCandle = iClose(_Symbol, PERIOD_CURRENT, 1) > iOpen(_Symbol, PERIOD_CURRENT, 1);
   
   if(trendUp && aboveVWAP && inFVG && bullCandle)
   {
      Print("BUY Setup: EMA=", ema50[1], ">", ema200[1], " VWAP=", vwap, " FVG=OK");
      return true;
   }
   return false;
}

//+------------------------------------------------------------------+
bool CheckSellSetup()
{
   double close1 = iClose(_Symbol, PERIOD_CURRENT, 1); 
   double vwap = CalculateVWAP(1);
   bool trendDown = close1 < ema50[1] && ema50[1] < ema200[1];
   bool belowVWAP =!UseVWAP || close1 < vwap;
   bool inFVG = CheckBearishFVG(1);
   bool bearCandle = iClose(_Symbol, PERIOD_CURRENT, 1) < iOpen(_Symbol, PERIOD_CURRENT, 1);
   
   if(trendDown && belowVWAP && inFVG && bearCandle)
   {
      Print("SELL Setup: EMA=", ema50[1], "<", ema200[1], " VWAP=", vwap, " FVG=OK");
      return true;
   }
   return false;
}

//+------------------------------------------------------------------+
void ExecuteBuy()
{
   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double sl = ask - (atr[1] * ATR_SL_Mult * _Point * 10);
double tp = ask + ((ask - sl) * RR_Ratio);
   double lotSize = CalculateLotSize(ask - sl);
   
   if(lotSize <= 0) { Print("Lot size calculation failed"); return; }
   
   if(trade.Buy(lotSize, _Symbol, ask, NormalizeDouble(sl, _Digits), NormalizeDouble(tp, _Digits), "Ashu v3 Buy"))
      Print("BUY Executed: Lot=", lotSize, " SL=", sl, " TP=", tp);
}

//+------------------------------------------------------------------+
void ExecuteSell()
{
   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   double sl = bid + (atr[1] * ATR_SL_Mult * _Point * 10);
   double tp = bid - ((sl - bid) * RR_Ratio);
   double lotSize = CalculateLotSize(sl - bid);
   
   if(lotSize <= 0) { Print("Lot size calculation failed"); return; }
   
   if(trade.Sell(lotSize, _Symbol, bid, NormalizeDouble(sl, _Digits), NormalizeDouble(tp, _Digits), "Ashu v3 Sell"))
      Print("SELL Executed: Lot=", lotSize, " SL=", sl, " TP=", tp);
}

//+------------------------------------------------------------------+
double CalculateLotSize(double slDistance)
{
   if(slDistance < 10 * _Point) return 0;
   
   double accountBalance = AccountInfoDouble(ACCOUNT_BALANCE);
   double riskAmount = accountBalance * RiskPercent / 100.0;
   double tickValue = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE);
   double tickSize = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
   
   if(tickValue == 0 || tickSize == 0) return 0;
   
   double lotSize = riskAmount / (slDistance / tickSize * tickValue);
   double minLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double maxLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
   double lotStep = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
   
   lotSize = MathFloor(lotSize / lotStep) * lotStep;
   lotSize = MathMax(minLot, MathMin(maxLot, lotSize));
   
   return NormalizeDouble(lotSize, 2);
}
//+------------------------------------------------------------------+
