//+------------------------------------------------------------------+
//|                                                      ORB_EA_v1.mq5 |
//|                                  AlgoMaster - Opening Range Breakout |
//|                                         https://www.mql5.com |
//+------------------------------------------------------------------+
#property copyright "AlgoMaster"
#property link      "https://www.mql5.com"
#property version   "1.00"
#property strict

//--- Include necessary libraries
#include <Trade\Trade.mqh>

//--- Input Parameters
input group "=== Trading Settings ==="
input string   AllowedSymbols = "XAUUSD,US500";  // Allowed symbols (comma-separated)
input int      MagicNumber = 123456;              // Magic Number
input double   RiskPercent = 1.0;                 // Risk per trade (% of balance)
input bool     UseFixedLot = false;               // Use fixed lot size
input double   FixedLotSize = 0.01;               // Fixed lot size (if enabled)

input group "=== Opening Range Settings ==="
input int      ORB_StartHour = 9;                 // ORB Start Hour (EST)
input int      ORB_StartMinute = 30;              // ORB Start Minute
input int      ORB_PeriodMinutes = 15;            // ORB Period in Minutes

input group "=== Risk Management ==="
input int      ATR_Period = 14;                   // ATR Period
input double   ATR_Multiplier = 1.5;              // ATR Multiplier for SL
input double   BreakEvenRatio = 1.0;              // Break-even at xR profit
input double   TrailingStopRatio = 0.5;           // Trailing stop distance (xR)
input bool     EnableTrailingStop = true;         // Enable trailing stop

input group "=== Take Profit Settings ==="
input double   TP1_Ratio = 1.0;                   // First TP at xR (50% close)
input double   TP2_Ratio = 3.0;                   // Second TP at xR (50% close)

input group "=== Time Settings ==="
input int      TradingStartHour = 9;              // Trading start hour (EST)
input int      TradingEndHour = 16;               // Trading end hour (EST)

//--- Global Variables
CTrade trade;
int atrHandle;
double atrBuffer[];
datetime lastTradeDate = 0;
bool orbDefined = false;
double orbHigh = 0.0;
double orbLow = 0.0;
datetime orbStartTime = 0;
bool breakoutOccurred = false;
ulong position1Ticket = 0;  // First 50% position
ulong position2Ticket = 0;  // Second 50% position
bool tp1Hit = false;
bool breakEvenSet = false;

//+------------------------------------------------------------------+
//| Expert initialization function                                   |
//+------------------------------------------------------------------+
int OnInit()
{
   //--- Set magic number for trade operations
   trade.SetExpertMagicNumber(MagicNumber);
   trade.SetDeviationInPoints(50);
   trade.SetTypeFilling(ORDER_FILLING_FOK);
   
   //--- Initialize ATR indicator
   atrHandle = iATR(_Symbol, PERIOD_M15, ATR_Period);
   if(atrHandle == INVALID_HANDLE)
   {
      Print("Error creating ATR indicator: ", GetLastError());
      return(INIT_FAILED);
   }
   
   //--- Set array as series
   ArraySetAsSeries(atrBuffer, true);
   
   //--- Check if current symbol is allowed
   if(!IsSymbolAllowed(_Symbol))
   {
      Print("Warning: Current symbol ", _Symbol, " is not in the allowed list.");
      Print("Allowed symbols: ", AllowedSymbols);
   }
   
   Print("ORB EA initialized successfully on ", _Symbol);
   return(INIT_SUCCEEDED);
}

//+------------------------------------------------------------------+
//| Expert deinitialization function                                 |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   //--- Release indicator handle
   if(atrHandle != INVALID_HANDLE)
      IndicatorRelease(atrHandle);
   
   Print("ORB EA deinitialized. Reason: ", reason);
}

//+------------------------------------------------------------------+
//| Expert tick function                                             |
//+------------------------------------------------------------------+
void OnTick()
{
   //--- Check if symbol is allowed
   if(!IsSymbolAllowed(_Symbol))
      return;
   
   //--- Check if we're in trading hours
   if(!IsTradingTime())
      return;
   
   //--- Get current time in EST
   datetime currentTime = TimeGMT() - 5*3600; // Convert to EST (GMT-5)
   MqlDateTime timeStruct;
   TimeToStruct(currentTime, timeStruct);
   
   //--- Check if it's a new trading day
   if(TimeCurrent() > lastTradeDate + 86400) // 86400 seconds = 1 day
   {
      ResetDailyVariables();
   }
   
   //--- Define Opening Range
   if(!orbDefined)
   {
      if(timeStruct.hour == ORB_StartHour && timeStruct.min == ORB_StartMinute)
      {
         DefineOpeningRange();
      }
      return; // Wait for ORB to be defined
   }
   
   //--- Check for breakout
   if(!breakoutOccurred)
   {
      CheckForBreakout();
   }
   
   //--- Manage open positions
   ManagePositions();
}

//+------------------------------------------------------------------+
//| Check if current symbol is in allowed list                       |
//+------------------------------------------------------------------+
bool IsSymbolAllowed(string symbol)
{
   string symbols[];
   int count = StringSplit(AllowedSymbols, ',', symbols);
   
   for(int i = 0; i < count; i++)
   {
      StringTrimLeft(symbols[i]);
      StringTrimRight(symbols[i]);
      if(symbols[i] == symbol)
         return true;
   }
   return false;
}

//+------------------------------------------------------------------+
//| Check if current time is within trading hours                    |
//+------------------------------------------------------------------+
bool IsTradingTime()
{
   datetime currentTime = TimeGMT() - 5*3600; // EST
   MqlDateTime timeStruct;
   TimeToStruct(currentTime, timeStruct);
   
   if(timeStruct.hour >= TradingStartHour && timeStruct.hour < TradingEndHour)
      return true;
   
   return false;
}

//+------------------------------------------------------------------+
//| Reset daily variables for new trading day                        |
//+------------------------------------------------------------------+
void ResetDailyVariables()
{
   orbDefined = false;
   orbHigh = 0.0;
   orbLow = 0.0;
   orbStartTime = 0;
   breakoutOccurred = false;
   position1Ticket = 0;
   position2Ticket = 0;
   tp1Hit = false;
   breakEvenSet = false;
   
   Print("Daily variables reset for new trading day");
}

//+------------------------------------------------------------------+
//| Define the Opening Range based on first 15-min candle            |
//+------------------------------------------------------------------+
void DefineOpeningRange()
{
   //--- Get the high and low of the current M15 candle
   double high[], low[];
   ArraySetAsSeries(high, true);
   ArraySetAsSeries(low, true);
   
   if(CopyHigh(_Symbol, PERIOD_M15, 0, 1, high) <= 0 ||
      CopyLow(_Symbol, PERIOD_M15, 0, 1, low) <= 0)
   {
      Print("Error copying price data: ", GetLastError());
      return;
   }
   
   orbHigh = high[0];
   orbLow = low[0];
   orbStartTime = TimeCurrent();
   orbDefined = true;
   
   Print("Opening Range Defined - High: ", orbHigh, " Low: ", orbLow);
}

//+------------------------------------------------------------------+
//| Check for breakout above or below opening range                  |
//+------------------------------------------------------------------+
void CheckForBreakout()
{
   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   
   //--- Check for breakout above ORB high (BUY signal)
   if(ask > orbHigh)
   {
      Print("Breakout above ORB High detected at ", ask);
      OpenPosition(ORDER_TYPE_BUY);
      breakoutOccurred = true;
      lastTradeDate = TimeCurrent();
   }
   //--- Check for breakout below ORB low (SELL signal)
   else if(bid < orbLow)
   {
      Print("Breakout below ORB Low detected at ", bid);
      OpenPosition(ORDER_TYPE_SELL);
      breakoutOccurred = true;
      lastTradeDate = TimeCurrent();
   }
}

//+------------------------------------------------------------------+
//| Open a position with proper risk management                      |
//+------------------------------------------------------------------+
void OpenPosition(ENUM_ORDER_TYPE orderType)
{
   //--- Get current ATR value
   if(CopyBuffer(atrHandle, 0, 0, 1, atrBuffer) <= 0)
   {
      Print("Error copying ATR buffer: ", GetLastError());
      return;
   }
   
   double atr = atrBuffer[0];
   double slDistance = atr * ATR_Multiplier;
   
   //--- Calculate entry price, SL, and TPs
   double entryPrice = (orderType == ORDER_TYPE_BUY) ? 
                       SymbolInfoDouble(_Symbol, SYMBOL_ASK) : 
                       SymbolInfoDouble(_Symbol, SYMBOL_BID);
   
   double sl = (orderType == ORDER_TYPE_BUY) ? 
               entryPrice - slDistance : 
               entryPrice + slDistance;
   
   double tp1 = (orderType == ORDER_TYPE_BUY) ? 
                entryPrice + (slDistance * TP1_Ratio) : 
                entryPrice - (slDistance * TP1_Ratio);
   
   double tp2 = (orderType == ORDER_TYPE_BUY) ? 
                entryPrice + (slDistance * TP2_Ratio) : 
                entryPrice - (slDistance * TP2_Ratio);
   
   //--- Calculate lot size
   double lotSize = CalculateLotSize(slDistance);
   double halfLot = NormalizeLot(lotSize / 2.0);
   
   //--- Normalize prices
   int digits = (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);
   entryPrice = NormalizeDouble(entryPrice, digits);
   sl = NormalizeDouble(sl, digits);
   tp1 = NormalizeDouble(tp1, digits);
   tp2 = NormalizeDouble(tp2, digits);
   
   Print("Opening Position - Type: ", EnumToString(orderType), 
         " Entry: ", entryPrice, " SL: ", sl, " TP1: ", tp1, " TP2: ", tp2,
         " Lot: ", halfLot, " ATR: ", atr);
   
   //--- Open first 50% position with TP1
   if(trade.PositionOpen(_Symbol, orderType, halfLot, entryPrice, sl, tp1, 
                         "ORB_50%_TP1"))
   {
      position1Ticket = trade.ResultOrder();
      Print("First position opened successfully. Ticket: ", position1Ticket);
   }
   else
   {
      Print("Error opening first position: ", trade.ResultRetcodeDescription());
      return;
   }
   
   //--- Open second 50% position with TP2
   if(trade.PositionOpen(_Symbol, orderType, halfLot, entryPrice, sl, tp2, 
                         "ORB_50%_TP2"))
   {
      position2Ticket = trade.ResultOrder();
      Print("Second position opened successfully. Ticket: ", position2Ticket);
   }
   else
   {
      Print("Error opening second position: ", trade.ResultRetcodeDescription());
   }
}

//+------------------------------------------------------------------+
//| Calculate lot size based on risk percentage                      |
//+------------------------------------------------------------------+
double CalculateLotSize(double slDistance)
{
   if(UseFixedLot)
      return FixedLotSize;
   
   double accountBalance = AccountInfoDouble(ACCOUNT_BALANCE);
   double riskAmount = accountBalance * (RiskPercent / 100.0);
   
   double tickValue = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE);
   double tickSize = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
   
   double lotSize = (riskAmount / slDistance) * (tickSize / tickValue);
   
   //--- Normalize lot size
   return NormalizeLot(lotSize);
}

//+------------------------------------------------------------------+
//| Normalize lot size according to symbol requirements              |
//+------------------------------------------------------------------+
double NormalizeLot(double lot)
{
   double minLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double maxLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
   double lotStep = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
   
   if(lot < minLot)
      lot = minLot;
   if(lot > maxLot)
      lot = maxLot;
   
   lot = MathFloor(lot / lotStep) * lotStep;
   
   return NormalizeDouble(lot, 2);
}

//+------------------------------------------------------------------+
//| Manage open positions (Break-even, Trailing Stop)                |
//+------------------------------------------------------------------+
void ManagePositions()
{
   //--- Check if first position still exists
   if(position1Ticket > 0 && !PositionSelectByTicket(position1Ticket))
   {
      Print("First position (TP1) has been closed");
      position1Ticket = 0;
      tp1Hit = true;
   }
   
   //--- Check if second position still exists
   if(position2Ticket > 0 && PositionSelectByTicket(position2Ticket))
   {
      double positionProfit = PositionGetDouble(POSITION_PROFIT);
      double positionOpenPrice = PositionGetDouble(POSITION_PRICE_OPEN);
      double positionSL = PositionGetDouble(POSITION_SL);
      double positionType = PositionGetInteger(POSITION_TYPE);
      
      double currentPrice = (positionType == POSITION_TYPE_BUY) ? 
                            SymbolInfoDouble(_Symbol, SYMBOL_BID) : 
                            SymbolInfoDouble(_Symbol, SYMBOL_ASK);
      
      double slDistance = MathAbs(positionOpenPrice - positionSL);
      double currentProfit = (positionType == POSITION_TYPE_BUY) ? 
                             (currentPrice - positionOpenPrice) : 
                             (positionOpenPrice - currentPrice);
      
      //--- Move to break-even at 1R
      if(!breakEvenSet && currentProfit >= slDistance * BreakEvenRatio)
      {
         MoveToBreakEven(position2Ticket, positionOpenPrice, positionType);
      }
      
      //--- Apply trailing stop after break-even
      if(breakEvenSet && EnableTrailingStop)
      {
         ApplyTrailingStop(position2Ticket, positionOpenPrice, positionSL, 
                          positionType, slDistance);
      }
   }
   else if(position2Ticket > 0)
   {
      Print("Second position (TP2) has been closed");
      position2Ticket = 0;
   }
}

//+------------------------------------------------------------------+
//| Move stop loss to break-even                                     |
//+------------------------------------------------------------------+
void MoveToBreakEven(ulong ticket, double openPrice, long posType)
{
   int digits = (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);
   double newSL = NormalizeDouble(openPrice, digits);
   
   if(trade.PositionModify(ticket, newSL, PositionGetDouble(POSITION_TP)))
   {
      Print("Position moved to break-even. Ticket: ", ticket, " New SL: ", newSL);
      breakEvenSet = true;
   }
   else
   {
      Print("Error moving to break-even: ", trade.ResultRetcodeDescription());
   }
}

//+------------------------------------------------------------------+
//| Apply trailing stop to position                                  |
//+------------------------------------------------------------------+
void ApplyTrailingStop(ulong ticket, double openPrice, double currentSL, 
                       long posType, double slDistance)
{
   double currentPrice = (posType == POSITION_TYPE_BUY) ? 
                         SymbolInfoDouble(_Symbol, SYMBOL_BID) : 
                         SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   
   double trailDistance = slDistance * TrailingStopRatio;
   int digits = (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);
   
   double newSL;
   
   if(posType == POSITION_TYPE_BUY)
   {
      newSL = NormalizeDouble(currentPrice - trailDistance, digits);
      if(newSL > currentSL && newSL < currentPrice)
      {
         if(trade.PositionModify(ticket, newSL, PositionGetDouble(POSITION_TP)))
         {
            Print("Trailing stop updated for BUY. Ticket: ", ticket, 
                  " New SL: ", newSL);
         }
      }
   }
   else // SELL
   {
      newSL = NormalizeDouble(currentPrice + trailDistance, digits);
      if(newSL < currentSL && newSL > currentPrice)
      {
         if(trade.PositionModify(ticket, newSL, PositionGetDouble(POSITION_TP)))
         {
            Print("Trailing stop updated for SELL. Ticket: ", ticket, 
                  " New SL: ", newSL);
         }
      }
   }
}
//+------------------------------------------------------------------+