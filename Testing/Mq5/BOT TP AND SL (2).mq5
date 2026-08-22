#property strict
#property version "1.40"

#include <Trade/Trade.mqh>
#include <ChartObjects\ChartObjectsTxtControls.mqh>

CTrade trade;

// === Inputs ===
input double Risk_Dollars = 5; // SL DOLLAR
input double Reward_Dollars = 10; // TP DOLLAR

input bool OnlyCurrentSymbol = true;

// === Features ===
input bool UseStopLoss = false;
input bool UseTakeProfit = false;

input bool UseTrailingStop = false;
input bool UseTrailingStart = false;
input bool UseBreakEven = true;
input bool UseLockProfit50Cents = true;

// === Settings ===
input double Trailing_Dollars = 2;
input double Trailing_Start_Dollars = 2;

input double BreakEven_Dollars = 5;
input double LockProfit_Dollars = 0.5;

// === Button ===
bool EA_Enabled = true;
string button_name = "";

//+------------------------------------------------------------------+
int OnInit()
{
   CreateButton();
   return(INIT_SUCCEEDED);
}

//+------------------------------------------------------------------+
void CreateButton()
{
   ObjectCreate(0,button_name,OBJ_BUTTON,0,0,0);
   ObjectSetInteger(0,button_name,OBJPROP_XDISTANCE,20);
   ObjectSetInteger(0,button_name,OBJPROP_YDISTANCE,20);
   ObjectSetInteger(0,button_name,OBJPROP_XSIZE,120);
   ObjectSetInteger(0,button_name,OBJPROP_YSIZE,30);
   ObjectSetInteger(0,button_name,OBJPROP_CORNER,CORNER_LEFT_UPPER);

   UpdateButton();
}

//+------------------------------------------------------------------+
void UpdateButton()
{
   if(EA_Enabled)
      ObjectSetString(0,button_name,OBJPROP_TEXT,"EA ON");
   else
      ObjectSetString(0,button_name,OBJPROP_TEXT,"EA OFF");
}

//+------------------------------------------------------------------+
void OnChartEvent(const int id,const long &lparam,const double &dparam,const string &sparam)
{
   if(id==CHARTEVENT_OBJECT_CLICK && sparam==button_name)
   {
      EA_Enabled = !EA_Enabled;
      UpdateButton();
   }
}

//+------------------------------------------------------------------+
void OnTick()
{
   if(!EA_Enabled)
      return;

   int total = PositionsTotal();

   for(int i=0;i<total;i++)
   {
      ulong ticket = PositionGetTicket(i);

      if(!PositionSelectByTicket(ticket))
         continue;

      string symbol = PositionGetString(POSITION_SYMBOL);

      if(OnlyCurrentSymbol && symbol!=_Symbol)
         continue;

      double open_price = PositionGetDouble(POSITION_PRICE_OPEN);
      double volume = PositionGetDouble(POSITION_VOLUME);

      long type = PositionGetInteger(POSITION_TYPE);

      double sl = PositionGetDouble(POSITION_SL);
      double tp = PositionGetDouble(POSITION_TP);

      double tick_size = SymbolInfoDouble(symbol,SYMBOL_TRADE_TICK_SIZE);
      double tick_value = SymbolInfoDouble(symbol,SYMBOL_TRADE_TICK_VALUE);
      int digits = (int)SymbolInfoInteger(symbol,SYMBOL_DIGITS);

      double profit = PositionGetDouble(POSITION_PROFIT);

      // === SL / TP ===
      double price_distance_sl = (Risk_Dollars / (tick_value * volume)) * tick_size;
      double price_distance_tp = (Reward_Dollars / (tick_value * volume)) * tick_size;

      double new_sl = sl;
      double new_tp = tp;

      // === تعيين SL و TP مبدئي ===
      if((sl == 0 || tp == 0))
      {
         if(type==POSITION_TYPE_BUY)
         {
            if(UseStopLoss)
               new_sl = NormalizeDouble(open_price - price_distance_sl,digits);

            if(UseTakeProfit)
               new_tp = NormalizeDouble(open_price + price_distance_tp,digits);
         }

         if(type==POSITION_TYPE_SELL)
         {
            if(UseStopLoss)
               new_sl = NormalizeDouble(open_price + price_distance_sl,digits);

            if(UseTakeProfit)
               new_tp = NormalizeDouble(open_price - price_distance_tp,digits);
         }
      }

      // === distances ===
      double trail_distance = (Trailing_Dollars / (tick_value * volume)) * tick_size;
      double lock_distance = (LockProfit_Dollars / (tick_value * volume)) * tick_size;

      // === BUY ===
      if(type==POSITION_TYPE_BUY)
      {
         double bid = SymbolInfoDouble(symbol,SYMBOL_BID);

         // BreakEven
         if(UseBreakEven && profit >= BreakEven_Dollars && UseStopLoss)
         {
            double be_price = NormalizeDouble(open_price,digits);
            if(new_sl < be_price)
               new_sl = be_price;
         }

         // Lock profit
         if(UseLockProfit50Cents && profit >= LockProfit_Dollars && UseStopLoss)
         {
            double lock_price = NormalizeDouble(open_price + lock_distance,digits);
            if(new_sl < lock_price)
               new_sl = lock_price;
         }

         // Trailing (مع أو بدون Start)
         if(UseTrailingStop && UseStopLoss &&
            ( (UseTrailingStart && profit >= Trailing_Start_Dollars) || !UseTrailingStart ))
         {
            double trail_sl = NormalizeDouble(bid - trail_distance,digits);
            if(trail_sl > new_sl)
               new_sl = trail_sl;
         }
      }

      // === SELL ===
      if(type==POSITION_TYPE_SELL)
      {
         double ask = SymbolInfoDouble(symbol,SYMBOL_ASK);

         // BreakEven
         if(UseBreakEven && profit >= BreakEven_Dollars && UseStopLoss)
         {
            double be_price = NormalizeDouble(open_price,digits);
            if(new_sl > be_price || new_sl==0)
               new_sl = be_price;
         }

         // Lock profit
         if(UseLockProfit50Cents && profit >= LockProfit_Dollars && UseStopLoss)
         {
            double lock_price = NormalizeDouble(open_price - lock_distance,digits);
            if(new_sl > lock_price || new_sl==0)
               new_sl = lock_price;
         }

         // Trailing (مع أو بدون Start)
         if(UseTrailingStop && UseStopLoss &&
            ( (UseTrailingStart && profit >= Trailing_Start_Dollars) || !UseTrailingStart ))
         {
            double trail_sl = NormalizeDouble(ask + trail_distance,digits);
            if(trail_sl < new_sl || new_sl==0)
               new_sl = trail_sl;
         }
      }

      // === تنفيذ ===
      trade.PositionModify(symbol,new_sl,new_tp);
   }
}