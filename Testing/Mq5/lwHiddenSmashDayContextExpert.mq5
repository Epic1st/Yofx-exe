
//+------------------------------------------------------------------+
//|                                lwHiddenSmashDayContextExpert.mq5 |
//|          Copyright 2026, MetaQuotes Ltd. Developer is Chacha Ian |
//|                          https://www.mql5.com/en/users/chachaian |
//+------------------------------------------------------------------+

#property copyright "Copyright 2026, MetaQuotes Ltd. Developer is Chacha Ian"
#property link      "https://www.mql5.com/en/users/chachaian"
#property version   "1.00"
#resource "\\Indicators\\lwHiddenSmashDayIndicator.ex5"
#resource "\\Indicators\\supertrend.ex5"


//+------------------------------------------------------------------+
//| Custom Enumerations                                              |
//+------------------------------------------------------------------+
enum ENUM_TDW_MODE
{
   TDW_ALL_DAYS,     
   TDW_SELECTED_DAYS
};

enum ENUM_SMASH_TRADE_MODE
{
   SMASH_TRADE_BUY_ONLY,
   SMASH_TRADE_SELL_ONLY,
   SMASH_TRADE_BOTH
};

enum ENUM_STOP_LOSS_MODE
{
   SL_ATR_BASED,            
   SL_SMASH_BAR_STRUCTURE
};

enum ENUM_LOT_SIZE_INPUT_MODE 
{ 
   MODE_MANUAL, 
   MODE_AUTO 
};


//+------------------------------------------------------------------+
//| Standard Libraries                                               |
//+------------------------------------------------------------------+
#include <Trade\Trade.mqh>


//+------------------------------------------------------------------+
//| User input variables                                             |
//+------------------------------------------------------------------+
input group "Information"
input ulong magicNumber          = 254700680002;                 
input ENUM_TIMEFRAMES timeframe  = PERIOD_CURRENT;

input group "Supertrend configuration parameters"
input bool useSupertrendFilter            = false;
input ENUM_TIMEFRAMES supertrendTimeframe = PERIOD_CURRENT;
input int32_t supertrendAtrPeriod         = 10;
input double  supertrendAtrMultiplier     = 1.5;

input group "TDW filters"
input ENUM_TDW_MODE tradeDayMode = TDW_SELECTED_DAYS;
input bool tradeSunday           = false;
input bool tradeMonday           = true;
input bool tradeTuesday          = false;
input bool tradeWednesday        = false;
input bool tradeThursday         = false;
input bool tradeFriday           = false;
input bool tradeSaturday         = false;

input group "Trade and Risk Management"
input ENUM_STOP_LOSS_MODE smashStopLossMode = SL_SMASH_BAR_STRUCTURE;
input int32_t atrPeriod                     = 14;
input double  atrMultiplier                 = 2.0;
input ENUM_SMASH_TRADE_MODE smashTradeMode  = SMASH_TRADE_BOTH;
input ENUM_LOT_SIZE_INPUT_MODE lotSizeMode  = MODE_AUTO;
input double riskPerTradePercent            = 1.0;
input double positionSize                   = 0.1;
input double riskRewardRatio                = 3.0;


//+------------------------------------------------------------------+
//| Global Variables                                                 |
//+------------------------------------------------------------------+
//--- Create a CTrade object to handle trading operations
CTrade Trade;

//--- To hep track current market prices for Buying (Ask) and Selling (Bid)
double askPrice;
double bidPrice;

//--- To store current time
datetime currentTime;

//--- To help track new bar open
datetime lastBarOpenTime;

//--- Hidden Smash bar indicator handle and values
int hiddenSmashDayIndicatorHandle;
double buySmashArrowBuffer[];
double sellSmashArrowBuffer[];

//--- Supertrend indicator handle and values 
int    supertrendIndicatorHandle;
double upperBandValues[];
double lowerBandValues[];

//--- ATR Values
int atrHandle;
double atrValues [];


//+------------------------------------------------------------------+
//| Expert initialization function                                   |
//+------------------------------------------------------------------+
int OnInit(){

   //---  Assign a unique magic number to identify trades opened by this EA
   Trade.SetExpertMagicNumber(magicNumber);
   
   //--- Initialize global variables
   lastBarOpenTime = 0;
   
   //--- Initialize the hidden smash day indicator
   hiddenSmashDayIndicatorHandle    = iCustom(_Symbol, timeframe, "::Indicators\\lwHiddenSmashDayIndicator.ex5");
   if(hiddenSmashDayIndicatorHandle == INVALID_HANDLE){
      Print("Error while initializing The Hidden Smash Day Indicator: ", GetLastError());
      return(INIT_FAILED);
   }
   
   // Initialize the Supertrend Indicator
   supertrendIndicatorHandle = iCustom(_Symbol, supertrendTimeframe, "::Indicators\\supertrend.ex5", supertrendAtrPeriod, supertrendAtrMultiplier);
   if(supertrendIndicatorHandle == INVALID_HANDLE){
      Print("Error while initializing the Supertrend indicator", GetLastError());
      return(INIT_FAILED);
   }
   
   //--- Initialize the ATR indicator
   atrHandle = iATR(_Symbol, timeframe, atrPeriod);
   if(atrHandle == INVALID_HANDLE){
      Print("Error while initializing the ATR indicator ", GetLastError());
      return(INIT_FAILED);
   }
   
   //--- Set arrays as series
   ArraySetAsSeries(buySmashArrowBuffer, true);
   ArraySetAsSeries(sellSmashArrowBuffer, true);
   ArraySetAsSeries(upperBandValues, true);
   ArraySetAsSeries(lowerBandValues, true);
   ArraySetAsSeries(atrValues, true);   
   
   //--- To configure the chart's appearance
   if(!ConfigureChartAppearance()){
      Print("Error while configuring chart appearance", GetLastError());
      return INIT_FAILED;
   }

   return(INIT_SUCCEEDED);
}


//+------------------------------------------------------------------+
//| Expert deinitialization function                                 |
//+------------------------------------------------------------------+
void OnDeinit(const int reason){

   //--- Release Supertrend
   if(hiddenSmashDayIndicatorHandle != INVALID_HANDLE){
      IndicatorRelease(hiddenSmashDayIndicatorHandle);
   }

   //--- Release Supertrend
   if(supertrendIndicatorHandle != INVALID_HANDLE){
      IndicatorRelease(supertrendIndicatorHandle);
   }
   
   //--- Release ATR
   if(atrHandle != INVALID_HANDLE){
      if(IndicatorRelease(atrHandle)){
         Print("The computer memory tracking ATR has been freed.");
      }
   }

   //--- Notify why the program stopped running
   Print("Program terminated! Reason code: ", reason);

}


//+------------------------------------------------------------------+
//| Expert tick function                                             |
//+------------------------------------------------------------------+
void OnTick(){

   //--- Retrieve current market prices for trade execution
   askPrice    = SymbolInfoDouble (_Symbol, SYMBOL_ASK);
   bidPrice    = SymbolInfoDouble (_Symbol, SYMBOL_BID);
   currentTime = TimeCurrent();
   
   if(IsNewBar(_Symbol, timeframe, lastBarOpenTime)){
      UpdateBuySmashArrowBuffer ();
      UpdateSellSmashArrowBuffer();
      UpdateSupertrendBandValues();
      UpdateATRValues();
      
      double stopLossLevel   = 0.000000;
      double takeProfitLevel = 0.000000;
      
      //--- Act on a bullish signal
      if(IsBullishHiddenSmashSignal(2)){
      
         stopLossLevel   = GetBuyStopLoss(2);
         takeProfitLevel = GetBuyTakeProfit(askPrice, stopLossLevel);
         if(!IsThereAnActiveBuyPosition(magicNumber) && !IsThereAnActiveSellPosition(magicNumber)){
            if(smashTradeMode == SMASH_TRADE_BUY_ONLY || smashTradeMode == SMASH_TRADE_BOTH){
               if(useSupertrendFilter){
                  if(IsSupertrendCurrentlyBullish()){
                     //---
                     if(tradeDayMode == TDW_SELECTED_DAYS){
                        if(IsTradingDayAllowed(currentTime)){
                           OpenBuy(askPrice, stopLossLevel, takeProfitLevel, positionSize);
                        }
                     }else{
                        OpenBuy(askPrice, stopLossLevel, takeProfitLevel, positionSize);
                     }
                  }
               }else{
                  //---
                  if(tradeDayMode == TDW_SELECTED_DAYS){
                     if(IsTradingDayAllowed(currentTime)){
                        OpenBuy(askPrice, stopLossLevel, takeProfitLevel, positionSize);
                     }
                  }else{
                     OpenBuy(askPrice, stopLossLevel, takeProfitLevel, positionSize);
                  }
               }
            }
         }
      }
      
      //--- Act on a bearish signal
      if(IsBearishHiddenSmashSignal(2)){
         stopLossLevel   = GetSellStopLoss(2);
         takeProfitLevel = GetSellTakeProfit(bidPrice, stopLossLevel);
         if(!IsThereAnActiveBuyPosition(magicNumber) && !IsThereAnActiveSellPosition(magicNumber)){
            if(smashTradeMode == SMASH_TRADE_SELL_ONLY || smashTradeMode == SMASH_TRADE_BOTH){
               if(useSupertrendFilter){
                  if(IsSupertrendCurrentlyBearish()){
                     //---
                     if(tradeDayMode == TDW_SELECTED_DAYS){
                        if(IsTradingDayAllowed(currentTime)){
                           OpenSell(bidPrice, stopLossLevel, takeProfitLevel, positionSize);
                        }
                     }else{
                        OpenSell(bidPrice, stopLossLevel, takeProfitLevel, positionSize);
                     }
                  }
               }else{
                  //---
                  if(tradeDayMode == TDW_SELECTED_DAYS){
                     if(IsTradingDayAllowed(currentTime)){
                        OpenSell(bidPrice, stopLossLevel, takeProfitLevel, positionSize);
                     }
                  }else{
                     OpenSell(bidPrice, stopLossLevel, takeProfitLevel, positionSize);
                  }
               }
            }
         }
      }
   }

}


//+------------------------------------------------------------------+
//| Function to check if there's a new bar on a given chart timeframe|
//+------------------------------------------------------------------+
bool IsNewBar(string symbol, ENUM_TIMEFRAMES tf, datetime &lastTm){

   datetime currentTm = iTime(symbol, tf, 0);
   if(currentTm != lastTm){
      lastTm       = currentTm;
      return true;
   }  
   return false;
   
}


//+------------------------------------------------------------------+
//| Updates the buySmashArrowBuffer with latest buy arrow values     |
//+------------------------------------------------------------------+
void UpdateBuySmashArrowBuffer()
{
   int copied = CopyBuffer(hiddenSmashDayIndicatorHandle,
                           0,          // Buy buffer index
                           0,          // Start from most recent bar
                           5,          // Number of values to copy
                           buySmashArrowBuffer);

   if(copied <= 0)
   {
      Print("Failed to copy buy smash arrow buffer. Error: ", GetLastError());
   }
}


//+------------------------------------------------------------------+
//| Updates the sellSmashArrowBuffer with latest sell arrow values   |
//+------------------------------------------------------------------+
void UpdateSellSmashArrowBuffer()
{
   int copied = CopyBuffer(hiddenSmashDayIndicatorHandle,
                           1,          // Sell buffer index
                           0,          // Start from most recent bar
                           5,          // Number of values to copy
                           sellSmashArrowBuffer);

   if(copied <= 0)
   {
      Print("Failed to copy sell smash arrow buffer. Error: ", GetLastError());
   }
}


//+------------------------------------------------------------------+
//| Returns true if a bullish Hidden Smash signal exists at index    |
//+------------------------------------------------------------------+
bool IsBullishHiddenSmashSignal(int index)
{
   if(index < 0)
      return false;

   if(buySmashArrowBuffer[index] != EMPTY_VALUE)
      return true;

   return false;
}


//+------------------------------------------------------------------+
//| Returns true if a bearish Hidden Smash signal exists at index    |
//+------------------------------------------------------------------+
bool IsBearishHiddenSmashSignal(int index)
{
   if(index < 0)
      return false;

   if(sellSmashArrowBuffer[index] != EMPTY_VALUE)
      return true;

   return false;
}


//+------------------------------------------------------------------+
//| Fetches recent Supertrend upper and lower band values            |
//+------------------------------------------------------------------+
void UpdateSupertrendBandValues(){

   //--- Get a few Supertrend upper band values
   int copiedUpper = CopyBuffer(supertrendIndicatorHandle, 5, 0, 5, upperBandValues);
   if(copiedUpper == -1)
   {
      Print("Error while copying Supertrend upper band values: ", GetLastError());
      return;
   }

   //--- Get a few Supertrend lower band values
   int copiedLower = CopyBuffer(supertrendIndicatorHandle, 6, 0, 5, lowerBandValues);
   if(copiedLower == -1)
   {
      Print("Error while copying Supertrend lower band values: ", GetLastError());
      return;
   }
   
   if(copiedUpper < 5 || copiedLower < 5){
      Print("Insufficient Supertrend indicator data!");
      return;
   }
   
}


//+------------------------------------------------------------------+
//| Returns true if Supertrend is currently in a bullish trend state |
//+------------------------------------------------------------------+
bool IsSupertrendCurrentlyBullish(){

   if(lowerBandValues[1] != EMPTY_VALUE){
      return true;
   }

   return false;
}


//+------------------------------------------------------------------+
//| Returns true if Supertrend is currently in a bearish trend state |
//+------------------------------------------------------------------+
bool IsSupertrendCurrentlyBearish(){

   if(upperBandValues[1] != EMPTY_VALUE){
      return true;
   }

   return false;
}


//+------------------------------------------------------------------+
//| To update ATR values                                             |
//+------------------------------------------------------------------+
void UpdateATRValues(){
   
   //--- Get some ATR Values
   int numberOfCopiedATRValues = CopyBuffer(atrHandle, 0, 0, 5, atrValues);
   if(numberOfCopiedATRValues == -1){
      Print("Error while copying ATR values: ", GetLastError());
   }

}


//+------------------------------------------------------------------+
//| Returns the day of the week (0 = Sunday, 6 = Saturday) for the given datetime value|                               
//+------------------------------------------------------------------+
int TimeDayOfWeek(datetime time){
   MqlDateTime timeStruct = {};
   if(!TimeToStruct(time, timeStruct)){
      Print("TimeDayOfWeek: TimeToStruct failed");
      return -1;
   }      
   return timeStruct.day_of_week;
}


//+------------------------------------------------------------------+
//| Determines whether trading is permitted for the given datetime based on the selected trade-day mode |                               
//+------------------------------------------------------------------+
bool IsTradingDayAllowed(datetime time)
{
   // Baseline mode: no filtering
   if(tradeDayMode == TDW_ALL_DAYS){
      return true;
   }

   int day = TimeDayOfWeek(time);

   switch(day)
   {
      case 0: return tradeSunday;
      case 1: return tradeMonday;
      case 2: return tradeTuesday;
      case 3: return tradeWednesday;
      case 4: return tradeThursday;
      case 5: return tradeFriday;
      case 6: return tradeSaturday;
   }

   return false;
}


//+------------------------------------------------------------------+
//| To verify whether this EA currently has an active buy position.  |                                 |
//+------------------------------------------------------------------+
bool IsThereAnActiveBuyPosition(ulong magic){
   
   for(int i = PositionsTotal() - 1; i >= 0; i--){
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0){
         Print("Error while fetching position ticket ", _LastError);
         continue;
      }else{
         if(PositionGetInteger(POSITION_MAGIC) == magic && PositionGetInteger(POSITION_TYPE) == POSITION_TYPE_BUY){
            return true;
         }
      }
   }
   
   return false;
}


//+------------------------------------------------------------------+
//| To verify whether this EA currently has an active sell position. |                                 |
//+------------------------------------------------------------------+
bool IsThereAnActiveSellPosition(ulong magic){
   
   for(int i = PositionsTotal() - 1; i >= 0; i--){
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0){
         Print("Error while fetching position ticket ", _LastError);
         continue;
      }else{
         if(PositionGetInteger(POSITION_MAGIC) == magic && PositionGetInteger(POSITION_TYPE) == POSITION_TYPE_SELL){
            return true;
         }
      }
   }
   
   return false;
}


//+------------------------------------------------------------------+
//| Calculates position size using OrderCalcProfit for accuracy      |
//+------------------------------------------------------------------+
double CalculatePositionSizeByRisk(ENUM_ORDER_TYPE orderType, double entryPrice, double stopLossPrice){

   //--- Amount willing to risk
   double amountAtRisk = (riskPerTradePercent / 100.0) *
                         AccountInfoDouble(ACCOUNT_BALANCE);

   //--- Calculate loss for 1 lot
   double lossPerLot = 0.0;

   if(!OrderCalcProfit(orderType,
                       _Symbol,
                       1.0,              // 1 lot
                       entryPrice,
                       stopLossPrice,
                       lossPerLot))
   {
      Print("OrderCalcProfit failed: ", GetLastError());
      return 0.0;
   }

   // Loss will be negative for losing scenario
   lossPerLot = MathAbs(lossPerLot);

   if(lossPerLot <= 0.0)
      return 0.0;

   //--- Raw volume
   double volume = amountAtRisk / lossPerLot;

   //--- Apply broker constraints
   double minLot   = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double maxLot   = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
   double lotStep  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);

   // Normalize to step
   volume = MathFloor(volume / lotStep) * lotStep;

   if(volume < minLot)
      volume = minLot;

   if(volume > maxLot)
      volume = maxLot;

   return NormalizeDouble(volume, 2);
}


//+------------------------------------------------------------------+
//| Computes the bullish stop loss level based on the index of the smash bar |
//+------------------------------------------------------------------+
double GetBuyStopLoss(int index){

   if(smashStopLossMode == SL_ATR_BASED){
      double atrValue = atrValues[1] * atrMultiplier;
      double slLevel  = askPrice - atrValue;
      return NormalizeDouble(slLevel, Digits());
   }else{
      return NormalizeDouble(iLow(_Symbol, timeframe, index), Digits());
   }
   
}


//+------------------------------------------------------------------+
//| Computes the bearish stop loss level based on the index of the smash bar |
//+------------------------------------------------------------------+
double GetSellStopLoss(int index){

   if(smashStopLossMode == SL_ATR_BASED){
      double atrValue = atrValues[1] * atrMultiplier;
      double slLevel  = bidPrice + atrValue;
      return NormalizeDouble(slLevel, Digits());
   }else{
      return NormalizeDouble(iHigh(_Symbol, timeframe, index), Digits());
   }
   
}


//+------------------------------------------------------------------+
//| Computes the bullish take profit level based on entry price, stop loss, and risk to reward ratio |
//+------------------------------------------------------------------+
double GetBuyTakeProfit(double entryPrice, double stopLoss){
   double riskDistance = entryPrice - stopLoss;
   double rewardDistance = riskDistance * riskRewardRatio;
   rewardDistance = MathAbs(rewardDistance);
   return NormalizeDouble((entryPrice + rewardDistance), Digits());
}


//+------------------------------------------------------------------+
//| Computes the bearish take profit level based on entry price, stop loss, and risk to reward ratio |
//+------------------------------------------------------------------+
double GetSellTakeProfit(double entryPrice, double stopLoss){
   double riskDistance = stopLoss - entryPrice;
   double rewardDistance = riskDistance * riskRewardRatio;
   rewardDistance = MathAbs(rewardDistance);
   return NormalizeDouble((entryPrice - rewardDistance), Digits());
}


//+------------------------------------------------------------------+
//| Function to open a market buy position                           |
//+------------------------------------------------------------------+
bool OpenBuy(double entryPrice, double stopLoss, double takeProfit, double lotSize){
   
   if(lotSizeMode == MODE_AUTO){
      lotSize = CalculatePositionSizeByRisk(ORDER_TYPE_BUY, entryPrice, stopLoss);
   }
   
   if(!Trade.Buy(lotSize, _Symbol, entryPrice, stopLoss, takeProfit)){
      Print("Error while executing a market buy order: ", GetLastError());
      Print(Trade.ResultRetcode());
      Print(Trade.ResultComment());
      return false;
   }
   return true;
}


//+------------------------------------------------------------------+
//| Function to open a market sell position                          |
//+------------------------------------------------------------------+
bool OpenSell(double entryPrice, double stopLoss, double takeProfit, double lotSize){
   
   if(lotSizeMode == MODE_AUTO){
      lotSize = CalculatePositionSizeByRisk(ORDER_TYPE_SELL, entryPrice, stopLoss);
   }
   
   if(!Trade.Sell(lotSize, _Symbol, entryPrice, stopLoss, takeProfit)){
      Print("Error while executing a market sell order: ", GetLastError());
      Print(Trade.ResultRetcode());
      Print(Trade.ResultComment());
      return false;
   }
   return true;
}


//+------------------------------------------------------------------+
//| This function configures the chart's appearance.                 |
//+------------------------------------------------------------------+
bool ConfigureChartAppearance()
{
   if(!ChartSetInteger(0, CHART_COLOR_BACKGROUND, clrWhite)){
      Print("Error while setting chart background, ", GetLastError());
      return false;
   }
   
   if(!ChartSetInteger(0, CHART_SHOW_GRID, false)){
      Print("Error while setting chart grid, ", GetLastError());
      return false;
   }
   
   if(!ChartSetInteger(0, CHART_MODE, CHART_CANDLES)){
      Print("Error while setting chart mode, ", GetLastError());
      return false;
   }

   if(!ChartSetInteger(0, CHART_COLOR_FOREGROUND, clrBlack)){
      Print("Error while setting chart foreground, ", GetLastError());
      return false;
   }

   if(!ChartSetInteger(0, CHART_COLOR_CANDLE_BULL, clrSeaGreen)){
      Print("Error while setting bullish candles color, ", GetLastError());
      return false;
   }
      
   if(!ChartSetInteger(0, CHART_COLOR_CANDLE_BEAR, clrBlack)){
      Print("Error while setting bearish candles color, ", GetLastError());
      return false;
   }
   
   if(!ChartSetInteger(0, CHART_COLOR_CHART_UP, clrSeaGreen)){
      Print("Error while setting bearish candles color, ", GetLastError());
      return false;
   }
   
   if(!ChartSetInteger(0, CHART_COLOR_CHART_DOWN, clrBlack)){
      Print("Error while setting bearish candles color, ", GetLastError());
      return false;
   }
   
   return true;
}
 
//+------------------------------------------------------------------+
