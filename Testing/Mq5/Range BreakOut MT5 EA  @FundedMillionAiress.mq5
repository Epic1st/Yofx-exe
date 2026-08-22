//+------------------------------------------------------------------+
//|                                        RANGE_BREAKOUT_EURUSD.mq5 |
//|                                  Copyright 2025, MetaQuotes Ltd. |
//|                                             https://www.mql5.com |
//+------------------------------------------------------------------+
#property copyright "Copyright 2025, MetaQuotes Ltd."
#property link      "https://www.mql5.com"
#property version   "1.01"
//+------------------------------------------------------------------+
//| Expert initialization function                                   |
//+------------------------------------------------------------------+
#include <Trade/Trade.mqh>       // Library for taking trades

input int RangeStartHour = 1;          // Range start hour
input int RangeStartMin = 0;           // Range start minute
input int RangeEndHour = 6;            // Range end hour
input int RangeEndMin = 0;             // Range end minute

input int TradingEndHour = 22;   // Trading end hour
input int TradingEndMin = 0;     // Trading end minute

datetime RangeTimeStart;         // Variable that store the start time of the range
datetime RangeTimeEnd;           // Variable that store the end time of the range
datetime TrandingEndTime;        // Variable that store the end time of trading

double rangeHigh;                // Top part of the range
double rangeLow;                 // Bottom part of the range

double minLot = SymbolInfoDouble(Symbol(), SYMBOL_VOLUME_MIN); //Mimal possible lots

//+------------------------------------------------------------------+
//|                                                                  |
//+------------------------------------------------------------------+
CTrade trade;                    // Create the object for trading (used when putting positions)
bool isTrade;                    // bool variable for activating or deactivating trading

//+------------------------------------------------------------------+
//|                                                                  |
//+------------------------------------------------------------------+
int OnInit()
  {
//+---------------------------------------------------+
//|             COLORS FOR THE GRAPHS                 |
//+---------------------------------------------------+

   ChartSetInteger(0, CHART_COLOR_BACKGROUND, clrBlack);    // Color for the Background (Black in this case)
   ChartSetInteger(0, CHART_COLOR_GRID, false);             // Color for the grid (false = no color = no grid)
   ChartSetInteger(0, CHART_COLOR_CANDLE_BULL, clrGreen);   // Color for the bullish candles (Green = upward movement)
   ChartSetInteger(0, CHART_COLOR_CANDLE_BEAR, clrRed);     // Color for the bearish candles (Red = downward movement)
   ChartSetInteger(0, CHART_COLOR_CHART_UP, clrGreen);      // Color for the line chart when price goes up
   ChartSetInteger(0, CHART_COLOR_CHART_DOWN, clrRed);      // Color for the line chart when price goes down
   ChartSetInteger(0, CHART_COLOR_FOREGROUND, clrWhite);    // Color for axis, scales, and text
   ChartSetInteger(0, CHART_COLOR_CHART_LINE, clrGray);     // Color for the line chart itself
   ChartSetInteger(0, CHART_COLOR_BID, clrBisque);          // Color for the Bid price line
   ChartSetInteger(0, CHART_COLOR_LAST, clrAquamarine);     // Color for the Last price line
   ChartSetInteger(0, CHART_COLOR_STOP_LEVEL, clrLightGray);// Color for Stop Levels

//---
   return(INIT_SUCCEEDED);
  }
//+------------------------------------------------------------------+
//| Expert deinitialization function                                 |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
  {
//---

  }
//+------------------------------------------------------------------+
//| Expert tick function                                             |
//+------------------------------------------------------------------+
void OnTick()
  {
//---
   calcTimes(); // function for calculating times     ↓↓    (bottom of the code)
   clcRange();  // function for calculating the range ↓↓    (bottom of the code)

   datetime time_current = TimeCurrent() ;

   if(time_current > RangeTimeEnd) // check if the range have finished in order to take a trade
     {
      int d = 0;
      if(isTrade)                                                 // checking if trading is enable or not (if isTrade is true)
        {
         if(rangeHigh > 0 && rangeLow > 0)                        // checking if range High and range low are differente from zero (from function clcRange ↓↓)
           {
            if(close(1) > rangeHigh && time(1) != RangeTimeEnd)   // check if the close of the past bar is higher than the top part of the range (close function ↓↓) and the time is different from the end time
              {
               if(CheckMoneyForTrade(Symbol(), minLot, ORDER_TYPE_BUY))  // Checking if there is enough money
                 {
                  double ask = SymbolInfoDouble(Symbol(), SYMBOL_ASK);        // Get symbol ask
                  double tp = rangeHigh + (rangeHigh - rangeLow);             // calculating the tp for the buy: rangehigh + size of the range
                  tp = Round2Ticksize(tp);
                  rangeLow = Round2Ticksize(rangeLow);
                  if(rangeLow < ask && tp > ask && ask-rangeLow > SYMBOL_TRADE_STOPS_LEVEL*_Point)                              // Validte sl and tp
                    {
                     trade.Buy(minLot, Symbol(), 0, rangeLow, tp);            // entering a buy position
                     isTrade = false;                                         // deactivating the possibility to trade (only 1 trade a day)
                    }
                  else
                    {
                     Print("SL or TP incorrect");
                    }
                 }
               else
                 {
                  Print("Not enough margin for Buy trade");
                  isTrade = false;                                      // deactivating the possibility to trade (only 1 trade a day)
                 }
              }
            if(close(1) < rangeLow && time(1) != RangeTimeEnd)       // check if the close of the past bar is lower than the bottom part of the range (close function ↓↓) and the time is different from the end time
              {
               if(CheckMoneyForTrade(Symbol(), minLot, ORDER_TYPE_SELL)) // Checking if there is enough money
                 {
                  double bid = SymbolInfoDouble(Symbol(), SYMBOL_BID);        // Get symbol bid
                  double tp = rangeLow - (rangeHigh - rangeLow);              // calculating the tp for the sell: rangelow - size of the range
                  tp = Round2Ticksize(tp);
                  rangeHigh = Round2Ticksize(rangeHigh);
                  if(rangeHigh > bid && tp < bid && rangeHigh-bid > SYMBOL_TRADE_STOPS_LEVEL*_Point)
                    {
                     trade.Sell(minLot, Symbol(), 0, rangeHigh, tp);             // entering a sell position
                     isTrade = false;                                            // deactivating the possibility to trade (only 1 trade a day)
                    }
                  else
                    {
                     Print("SL or TP incorrect");
                    }
                 }
               else
                 {
                  Print("Not enough margin for Sell trade");
                  isTrade = false;                                      // deactivating the possibility to trade (only 1 trade a day)
                 }
              }
           }
        }
     }
   if(time_current > TrandingEndTime)                            // Check if it the time is higher than the close time
     {
      for(int i = PositionsTotal() - 1; i >= 0; i--)              // for cicle for getting all the positions avilable
        {
         CPositionInfo pos;                                       // Getting information each of the positions
         if(pos.SelectByIndex(i))                                 // Checking each initial position
           {
            trade.PositionClose(pos.Ticket());                    // Closing each position
           }
        }
     }
  }
//+-----------------------------------------------------------------------------+
//|Function for transforming the value of the hours and minuted into time format|
//+-----------------------------------------------------------------------------+
void calcTimes()
  {
   MqlDateTime dt;  //formatting the date into mql structure
   TimeCurrent(dt); //getting the actual time
   dt.sec = 0;      //setting the second parameter to 0, letting the day, hour and minute different from zero

   dt.hour = RangeStartHour;  // setting the hour of the start of the range
   dt.min = RangeStartMin;    // setting the minute of the start of the range

   datetime range_time_start = StructToTime(dt);

   if(RangeTimeStart != range_time_start) //checking if the day have changed, if it have, the option to trade will be activated and the rangeHigh as well as the rangeLow will be set to 0
     {
      isTrade = true;
      rangeHigh = 0;
      rangeLow = 0;
     }

   RangeTimeStart = range_time_start;     //Set our RangeTimeStart to the dt formated to datetime

   dt.hour = RangeEndHour;                //Setting our range End time to be compatible with datetime format using our variables
   dt.min = RangeEndMin;
   RangeTimeEnd = StructToTime(dt);

   dt.hour = TradingEndHour;              //Setting our trading End time to be compatible with datetime format using our variables
   dt.min = TradingEndMin;
   TrandingEndTime = StructToTime(dt);
  }
//+------------------------------------------------------------------+
//|          FUNCTION FOR CALCULATING OUR TRADING RANGE              |
//+------------------------------------------------------------------+
void clcRange()
  {
   double highs[]; //array for storing highs
   CopyHigh(Symbol(), PERIOD_M1, RangeTimeStart, RangeTimeEnd, highs); //getting the highs in an specific range of time and store in the highs array

   double lows[];  //array for storing lows
   CopyLow(Symbol(), PERIOD_M1, RangeTimeStart, RangeTimeEnd, lows);   //getting the low in an specific range of time and store in the highs lows

   if(ArraySize(highs) < 1 || ArraySize(lows) < 1)                    //Check if the array is not empty
      return;

   int indexHighest = ArrayMaximum(highs);                            //Get the index of the highest of the highs
   int indexLowest =  ArrayMinimum(lows);                             //Get the index of the lowest of the lows

   rangeHigh =highs[indexHighest];                                   //Get the highest of the highs
   rangeLow = lows[indexLowest];                                      //Get the lowest of the lows
   

//Only drawing on the symbol that we want to
   string objName = "Range" + TimeToString(RangeTimeStart, TIME_DATE); //Setting the name for the box of the range
   if(ObjectFind(0, objName) < 0)                                     //Checking if there exists an actual box
     {
      ObjectCreate(0, objName, OBJ_RECTANGLE, 0, RangeTimeStart, rangeLow, RangeTimeEnd, rangeHigh);  //Setting the four points of the box
      ObjectSetInteger(0, objName, OBJPROP_FILL, true);                                               //Fill the rectangle with color
      ObjectSetInteger(0, objName, OBJPROP_COLOR, clrYellow);
     }
   else                                                                                               //Updating the box
     {
      ObjectSetDouble(0, objName, OBJPROP_PRICE, 0, rangeLow);                                        //update bottom edge
      ObjectSetDouble(0, objName, OBJPROP_PRICE, 1, rangeHigh);                                       //update top edge
     }
  }
//+------------------------------------------------------------------+
//|            FUNCTION FOR GETTING THE HIGH OF A CANDLE             |
//+------------------------------------------------------------------+
double high(int index)
  {
   return (iHigh(Symbol(), PERIOD_M15, index));
  }
//+------------------------------------------------------------------+
//|            FUNCTION FOR GETTING THE LOW OF A CANDLE              |
//+------------------------------------------------------------------+
double  low(int index)
  {
   return (iLow(Symbol(), PERIOD_M15, index));
  }
//+------------------------------------------------------------------+
//|            FUNCTION FOR GETTING THE CLOSE OF A CANDLE            |
//+------------------------------------------------------------------+
double close(int index)
  {
   return (iClose(Symbol(), PERIOD_M15, index));
  }
//+------------------------------------------------------------------+
//|            FUNCTION FOR GETTING THE TIME OF A CANDLE             |
//+------------------------------------------------------------------+
datetime time(int index)
  {
   return (iTime(Symbol(), PERIOD_M15, index));
  }
//+------------------------------------------------------------------+
//|    FUNCTION FOR CHECKING IF THERE IS ENOUGHT MARGIN TO TRADE     |
//+------------------------------------------------------------------+
bool CheckMoneyForTrade(string symbol, double lots, ENUM_ORDER_TYPE type)
  {
// Get the current price (Ask for Buy, Bid for Sell)
   double price = (type == ORDER_TYPE_SELL)
                  ? SymbolInfoDouble(symbol, SYMBOL_BID)
                  : SymbolInfoDouble(symbol, SYMBOL_ASK);


   double margin;   // Calculate required margin
   if(!OrderCalcMargin(type, symbol, lots, price, margin))
      return false; // if calculation fails, return false


   double freeMargin = AccountInfoDouble(ACCOUNT_MARGIN_FREE); // Get margin available


   if(margin > freeMargin)             // Compare required vs marginavailable
      return false;                    // not enough money

   return true;                        // enough money
  }
//+------------------------------------------------------------------+
double Round2Ticksize(double price)
  {
   double tick_size = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
   return(round(price / tick_size) * tick_size);
  }
//+------------------------------------------------------------------+
