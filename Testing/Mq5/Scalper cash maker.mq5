//+------------------------------------------------------------------+
//|                                        Scalper Cash Maker EA.mq5 |
//|                                                       MFOREX.PRO |
//|                                           https://www.mforex.pro |
//+------------------------------------------------------------------+
#property copyright "MFOREX.PRO"
#property link      "https://www.mforex.pro"
#property version   "2.00"

#include <Trade\Trade.mqh>
CTrade trade;

//+------------------------------------------------------------------+
input double    Risk             =2;           //Риск         
input int       Step             =75;          //Минимальный шаг для второго и следуующих колен
input int       TimeStart        =2;  
input int       TimeEnd          =23; 
input bool      Info             =false;  
input color     TextColor        =clrWhite;   
input color     InfoDataColor    =clrDodgerBlue; 
input color     FonColor         =clrBlack;   
input int       FontSizeInfo     =7; 
input int       Magic            =2026;        //Уникальный номер ордеров
// [6] Spread filter
input int       MaxSpread        =30;          //Max spread in points
// [8] DD soft-stop threshold
input double    DDKillPercent    =20.0;        //DD% threshold for soft adjustments
// [5] Momentum exit threshold
input double    MomentumThresh   =0.3;         //Momentum flat threshold for exit
//+------------------------------------------------------------------+
string comment_str ="www.mforex.pro";
double   MinProfit  =0.00;
int      Dist       =5; 
double   UpLot      =2;
color    FonButtonBuy  =clrBlue;
color    FonButtonSell =clrRed;
color    TextButtonBS  =clrWhite; 
color    ButtonBorder  =clrBlue;
color    ClickButton   =clrBlack;
double   Lot        =0;
int      D;
double   PricSellLine, PricBuyLine, NewLot, NewProfProc;

// Indicator handles
int atrHandle;
int momentumHandle;
//+------------------------------------------------------------------+
int OnInit()
{
   D=1;
   if(_Digits==5 || _Digits==3) D=10;

   // Create indicator handles — [2][5]
   atrHandle      = iATR(Symbol(), PERIOD_CURRENT, 14);
   momentumHandle = iMomentum(Symbol(), PERIOD_CURRENT, 5, PRICE_CLOSE);
   if(atrHandle==INVALID_HANDLE || momentumHandle==INVALID_HANDLE)
   { Print("Failed to create indicator handles"); return(INIT_FAILED); }

   trade.SetExpertMagicNumber(Magic);

   double ask = SymbolInfoDouble(Symbol(), SYMBOL_ASK);
   double bid = SymbolInfoDouble(Symbol(), SYMBOL_BID);
   HLineCreate("LineBuy",  ask + Dist*D*_Point, clrNONE); 
   HLineCreate("LineSell", bid - Dist*D*_Point, clrNONE);  
   return(INIT_SUCCEEDED);
}
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   ObjectsDeleteAll(0, "", -1, OBJ_HLINE);
   IndicatorRelease(atrHandle);
   IndicatorRelease(momentumHandle);
}
//+------------------------------------------------------------------+
void OnTick()
{
   double ask = SymbolInfoDouble(Symbol(), SYMBOL_ASK);
   double bid = SymbolInfoDouble(Symbol(), SYMBOL_BID);

   // [2] ATR-scaled distances replace static Dist*D*Point and Step*D*Point
   double atrBuffer[1];
   if(CopyBuffer(atrHandle, 0, 0, 1, atrBuffer) < 1) return;
   double atr = atrBuffer[0];
   if(atr <= 0) atr = Step * D * _Point;
   double EntryDist = atr * 0.25;
   double GridStep  = atr * 0.8;

   // [8] DD Kill Switch — soft only: reduce lot, widen grid, tighten exits
   double balance  = AccountInfoDouble(ACCOUNT_BALANCE);
   double equity   = AccountInfoDouble(ACCOUNT_EQUITY);
   double ddRatio  = (balance > 0) ? (balance - equity) / balance * 100.0 : 0;
   double LotMult  = (ddRatio >= DDKillPercent) ? 0.5 : 1.0;
   double GridMult = (ddRatio >= DDKillPercent) ? 2.0 : 1.0;
   double ExitMult = (ddRatio >= DDKillPercent) ? 0.7 : 1.0;
   double effDist  = EntryDist * GridMult;
   double effStep  = GridStep  * GridMult;

   // [6] Spread guard
   bool SpreadOK = ((int)SymbolInfoInteger(Symbol(), SYMBOL_SPREAD) <= MaxSpread);

   double tickVal = SymbolInfoDouble(Symbol(), SYMBOL_TRADE_TICK_VALUE);
   Lot = NormalizeDouble(balance/100*Risk / (tickVal*100*D) * LotMult, 2);
   double minLot = SymbolInfoDouble(Symbol(), SYMBOL_VOLUME_MIN);
   if(Lot < minLot) Lot = minLot;

   //---Находим цены линий
   for(int i=ObjectsTotal(0)-1; i>=0; i--)
   {
      PricBuyLine  = ObjectGetDouble(0,"LineBuy",  OBJPROP_PRICE);
      PricSellLine = ObjectGetDouble(0,"LineSell", OBJPROP_PRICE);
   }

   //---Тралим линии     
   if(ask + effDist < PricBuyLine)
      HLineMove("LineBuy",  ask + effDist);
   if(bid - effDist > PricSellLine)
      HLineMove("LineSell", bid - effDist);

   //---Переносим линии если цена идет в плюс после последней
   if(ask < PricSellLine && bid - effDist > PricBuyLine)
      HLineMove("LineBuy",  ask + effDist);
   if(bid > PricBuyLine && ask + effDist < PricSellLine)
      HLineMove("LineSell", bid - effDist);

   // [1] Microstructure signals replace Open[0]>/<Close[0] candle direction
   double close1 = iClose(Symbol(), PERIOD_CURRENT, 1);
   double high2  = iHigh (Symbol(), PERIOD_CURRENT, 2);
   double low2   = iLow  (Symbol(), PERIOD_CURRENT, 2);
   bool BullStructure = (close1 > high2);
   bool BearStructure = (close1 < low2);

   // Get current hour
   MqlDateTime tm; TimeToStruct(TimeCurrent(), tm);
   int curHour = tm.hour;

   //---Открываем ордера
   if(curHour >= TimeStart && curHour < TimeEnd)
   { 
      if(SpreadOK && BearStructure && ask<=PricSellLine && LastType()!=(int)POSITION_TYPE_SELL && Count(-1)==0)
      {
         trade.Sell(Lot, Symbol(), bid, 0, 0, comment_str);
         HLineMove("LineBuy", ask + effDist);
      }
      if(SpreadOK && BullStructure && bid>=PricBuyLine && LastType()!=(int)POSITION_TYPE_BUY && Count(-1)==0)
      {
         trade.Buy(Lot, Symbol(), ask, 0, 0, comment_str);
         HLineMove("LineSell", bid - effDist);
      }
   }

   // [3] Safer 1.35x power growth replaces linear UpLot accumulation
   NewLot = NormalizeDouble(Lot * MathPow(1.35, Count(-1)), 2);
   if(NewLot < minLot) NewLot = minLot;

   if(SpreadOK && BearStructure && ask<=PricSellLine && Count((int)POSITION_TYPE_SELL)>0 && bid - effStep > SellPric())
   {
      trade.Sell(NewLot, Symbol(), bid, 0, 0, comment_str);
      HLineMove("LineBuy", ask + effDist);
   }
   if(SpreadOK && BullStructure && bid>=PricBuyLine && Count((int)POSITION_TYPE_BUY)>0 && ask + effStep < BuyPric())
   {
      trade.Buy(NewLot, Symbol(), ask, 0, 0, comment_str);
      HLineMove("LineSell", bid - effDist);
   }

   NewProfProc = (balance > 0) ? Profit(-1) / (balance/100) : 0;

   // [4] Exposure-weighted profit target
   double exposureTarget = 0.5 * TotalLots() * ExitMult;

   // [5] Momentum exit — harvest profit when price stalls near profit
   double momBuffer[1];
   if(CopyBuffer(momentumHandle, 0, 0, 1, momBuffer) >= 1)
   {
      double momentum = momBuffer[0];
      if(Profit(-1) > MinProfit && MathAbs(momentum - 100) < MomentumThresh)
         { CloseMinus(-1); ClosePlus(-1); }
   }

   // [7] SmartDDR replaces original CloseMinus/ClosePlus exit block
   if(Count((int)POSITION_TYPE_BUY)>0 && ask<=PricSellLine && Profit(-1)>=exposureTarget)
      SmartDDR(-1);
   if(Count((int)POSITION_TYPE_SELL)>0 && bid>=PricBuyLine && Profit(-1)>=exposureTarget)
      SmartDDR(-1);

   //======= Вывод информации на график
   if(Info)
   {  
      RectLabelCreate3("INFO_fon",220,20,200,225,FonColor);
      
      PutLabel("INFO_LOGO", 165,24,"WWW.MFOREX.PRO");
      PutLabel("INFO_Line", 215,27,"___________________________");
      PutLabel_("INFO_txt1",215,45,"Account information");
      PutLabel("INFO_Line2",215,47,"___________________________");
      PutLabel("INFO_txt2", 215,65,"Minimum stop:");
      PutLabel("INFO_txt3", 215,80,"Current profit percent:");
      PutLabel("INFO_txt4", 215,95,"Balanse:");
      PutLabel("INFO_txt5", 215,110,"Equity:");
      PutLabel("INFO_Line3",215,112,"___________________________");
      PutLabel_("INFO_txt6",215,130,"Profit on account");
      PutLabel("INFO_Line4",215,132,"___________________________");
      PutLabel("INFO_txt7", 215,150,"Profit on pair:");
      PutLabel("INFO_txt8", 215,165,"Total profit:");
      PutLabel("INFO_txt9", 215,180,"Profit for today:");
      PutLabel("INFO_txt10",215,195,"Profit for yesterday:");
      PutLabel("INFO_txt11",215,210,"Profit for week:");
      PutLabel("INFO_txt12",215,225,"Profit for month:");
      
      PutLabel_("INFO_txt13",85,65, DoubleToString((double)SymbolInfoInteger(Symbol(),SYMBOL_TRADE_STOPS_LEVEL),0));
      PutLabel_("INFO_txt14",85,80, DoubleToString(NewProfProc,2));
      PutLabel_("INFO_txt15",85,95, DoubleToString(balance,2));
      PutLabel_("INFO_txt16",85,110,DoubleToString(equity,2));
      PutLabel_("INFO_txt17",85,150,DoubleToString(Profit(-1),2));
      PutLabel_("INFO_txt18",85,165,DoubleToString(ProfitAll(-1),2));
      PutLabel_("INFO_txt19",85,180,DoubleToString(ProfitDey(-1),2));
      PutLabel_("INFO_txt20",85,195,DoubleToString(ProfitTuDey(-1),2));
      PutLabel_("INFO_txt21",85,210,DoubleToString(ProfitWeek(-1),2));
      PutLabel_("INFO_txt22",85,225,DoubleToString(ProfitMontag(-1),2));
      
      PutButtonBS("TRADEs_B",220,245,"BUY           " + DoubleToString(Lot,2),100,22,FonButtonBuy,TextButtonBS);
      PutButtonBS("TRADEs_S",118,245,"SELL           " + DoubleToString(Lot,2),100,22,FonButtonSell,TextButtonBS);
   }
}

//+------------------------------------------------------------------+
//|   Кнопки                                                         |
//+------------------------------------------------------------------+
void OnChartEvent(const int id, const long &lparam, const double &dparam, const string &sparam)
{
   if(ObjectGetInteger(0,"TRADEs_B",OBJPROP_STATE))
      ObjectSetInteger(0,"TRADEs_B",OBJPROP_BGCOLOR,ClickButton);
   else 
      ObjectSetInteger(0,"TRADEs_B",OBJPROP_BGCOLOR,FonButtonBuy);

   if(ObjectGetInteger(0,"TRADEs_S",OBJPROP_STATE))
      ObjectSetInteger(0,"TRADEs_S",OBJPROP_BGCOLOR,ClickButton);
   else 
      ObjectSetInteger(0,"TRADEs_S",OBJPROP_BGCOLOR,FonButtonSell);

   if(id==CHARTEVENT_OBJECT_CLICK)
   {
      string clickedChartObject=sparam;
      double ask = SymbolInfoDouble(Symbol(), SYMBOL_ASK);
      double bid = SymbolInfoDouble(Symbol(), SYMBOL_BID);

      if(clickedChartObject=="TRADEs_B")
      {
         trade.Buy(Lot, Symbol(), ask, 0, 0, comment_str);
         ObjectSetInteger(0,"TRADEs_B",OBJPROP_STATE,false);
      }
      if(clickedChartObject=="TRADEs_S")
      {
         trade.Sell(Lot, Symbol(), bid, 0, 0, comment_str);
         ObjectSetInteger(0,"TRADEs_S",OBJPROP_STATE,false);
      }
      ChartRedraw();
   }
}

//+------------------------------------------------------------------+ 
//| Функция закрытия ордеров                                         |
//+------------------------------------------------------------------+
void ClosePlus(int ot)
{
   for(int i=PositionsTotal()-1; i>=0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL)!=Symbol() || (int)PositionGetInteger(POSITION_MAGIC)!=Magic) continue;
      double profit = PositionGetDouble(POSITION_PROFIT)+PositionGetDouble(POSITION_SWAP);
      if(profit <= 0) continue;
      int ptype = (int)PositionGetInteger(POSITION_TYPE);
      if(ptype==POSITION_TYPE_BUY  && (ot==0 || ot==-1)) trade.PositionClose(ticket);
      if(ptype==POSITION_TYPE_SELL && (ot==1 || ot==-1)) trade.PositionClose(ticket);
   }
}

void CloseMinus(int ot)
{
   for(int i=PositionsTotal()-1; i>=0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL)!=Symbol() || (int)PositionGetInteger(POSITION_MAGIC)!=Magic) continue;
      double profit = PositionGetDouble(POSITION_PROFIT)+PositionGetDouble(POSITION_SWAP);
      if(profit >= 0) continue;
      int ptype = (int)PositionGetInteger(POSITION_TYPE);
      if(ptype==POSITION_TYPE_BUY  && (ot==0 || ot==-1)) trade.PositionClose(ticket);
      if(ptype==POSITION_TYPE_SELL && (ot==1 || ot==-1)) trade.PositionClose(ticket);
   }
}

//+------------------------------------------------------------------+
//| [7] SmartDDR — close best-profit + worst-profit pair             |
//+------------------------------------------------------------------+
void SmartDDR(int type)
{
   ulong  winTicket=0;   double winProfit=-DBL_MAX;
   ulong  lossTicket=0;  double lossProfit=DBL_MAX;
   for(int i=PositionsTotal()-1; i>=0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL)!=Symbol() || (int)PositionGetInteger(POSITION_MAGIC)!=Magic) continue;
      if(type!=-1 && (int)PositionGetInteger(POSITION_TYPE)!=type) continue;
      double p = PositionGetDouble(POSITION_PROFIT)+PositionGetDouble(POSITION_SWAP);
      if(p > winProfit)  { winProfit=p;   winTicket=ticket; }
      if(p < lossProfit) { lossProfit=p; lossTicket=ticket; }
   }
   if(winTicket==0 || lossTicket==0) return;
   if(winProfit + lossProfit > 0.3)
   {
      trade.PositionClose(winTicket);
      trade.PositionClose(lossTicket);
   }
}

//+------------------------------------------------------------------+
//| [4] Total open lots for exposure-weighted target                 |
//+------------------------------------------------------------------+
double TotalLots()
{
   double lots=0;
   for(int i=PositionsTotal()-1; i>=0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL)!=Symbol() || (int)PositionGetInteger(POSITION_MAGIC)!=Magic) continue;
      lots += PositionGetDouble(POSITION_VOLUME);
   }
   return lots;
}

//+------------------------------------------------------------------+ 
//| Создает горизонтальную линию                                     | 
//+------------------------------------------------------------------+ 
bool HLineCreate(string name, double price, color clr)
{
   if(!price) price=SymbolInfoDouble(Symbol(),SYMBOL_BID); 
   ResetLastError(); 
   if(!ObjectCreate(0,name,OBJ_HLINE,0,0,price)) 
   { Print(__FUNCTION__,": не удалось создать горизонтальную линию! Код ошибки = ",GetLastError()); 
     return(false); } 
   ObjectSetInteger(0,name,OBJPROP_COLOR,clr); 
   ObjectSetInteger(0,name,OBJPROP_STYLE,STYLE_SOLID); 
   ObjectSetInteger(0,name,OBJPROP_WIDTH,1); 
   ObjectSetInteger(0,name,OBJPROP_BACK,false); 
   ObjectSetInteger(0,name,OBJPROP_SELECTABLE,true); 
   ObjectSetInteger(0,name,OBJPROP_SELECTED,true); 
   ObjectSetInteger(0,name,OBJPROP_HIDDEN,false); 
   ObjectSetInteger(0,name,OBJPROP_ZORDER,0); 
   return(true);
} 

//+------------------------------------------------------------------+ 
//| Перемещение горизонтальной линии                                 | 
//+------------------------------------------------------------------+ 
bool HLineMove(string name, double price)
{
   if(!price) price=SymbolInfoDouble(Symbol(),SYMBOL_BID); 
   ResetLastError(); 
   if(!ObjectMove(0,name,0,0,price)) 
   { Print(__FUNCTION__,": не удалось переместить горизонтальную линию! Код ошибки = ",GetLastError()); 
     return(false); } 
   return(true);
} 

//+------------------------------------------------------------------+ 
//| Определяем тип последнего ордера (из истории сделок)            | 
//+------------------------------------------------------------------+ 
int LastType()
{
   int type=-1;
   datetime dt=0;
   HistorySelect(0, TimeCurrent());
   for(int i=(int)HistoryDealsTotal()-1; i>=0; i--)
   {
      ulong ticket = HistoryDealGetTicket(i);
      if(HistoryDealGetString(ticket, DEAL_SYMBOL)!=Symbol()) continue;
      if((int)HistoryDealGetInteger(ticket, DEAL_MAGIC)!=Magic) continue;
      if((ENUM_DEAL_ENTRY)HistoryDealGetInteger(ticket, DEAL_ENTRY)!=DEAL_ENTRY_IN) continue;
      datetime openTime = (datetime)HistoryDealGetInteger(ticket, DEAL_TIME);
      if(openTime > dt)
      {
         dt   = openTime;
         ENUM_DEAL_TYPE dtype = (ENUM_DEAL_TYPE)HistoryDealGetInteger(ticket, DEAL_TYPE);
         type = (dtype==DEAL_TYPE_BUY) ? (int)POSITION_TYPE_BUY : (int)POSITION_TYPE_SELL;
      }
   }
   return type;
}

//+------------------------------------------------------------------+ 
//| Определяем цену последнего ордера бай                            | 
//+------------------------------------------------------------------+ 
double BuyPric()
{
   double price=0;
   ulong  bestTicket=0;
   for(int i=PositionsTotal()-1; i>=0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL)!=Symbol() || (int)PositionGetInteger(POSITION_MAGIC)!=Magic) continue;
      if((int)PositionGetInteger(POSITION_TYPE)!=POSITION_TYPE_BUY) continue;
      if(ticket > bestTicket) { bestTicket=ticket; price=PositionGetDouble(POSITION_PRICE_OPEN); }
   }
   return price;
}

//+------------------------------------------------------------------+ 
//| Определяем цену последнего ордера селл                           | 
//+------------------------------------------------------------------+ 
double SellPric()
{
   double price=0;
   ulong  bestTicket=0;
   for(int i=PositionsTotal()-1; i>=0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL)!=Symbol() || (int)PositionGetInteger(POSITION_MAGIC)!=Magic) continue;
      if((int)PositionGetInteger(POSITION_TYPE)!=POSITION_TYPE_SELL) continue;
      if(ticket > bestTicket) { bestTicket=ticket; price=PositionGetDouble(POSITION_PRICE_OPEN); }
   }
   return price;
}

//+------------------------------------------------------------------+ 
//| Считаем количество ордеров по типу                               | 
//+------------------------------------------------------------------+ 
int Count(int type)
{
   int count=0;
   for(int i=PositionsTotal()-1; i>=0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL)!=Symbol() || (int)PositionGetInteger(POSITION_MAGIC)!=Magic) continue;
      if(type==-1 || (int)PositionGetInteger(POSITION_TYPE)==type) count++;
   }
   return count;
}

//======= Счетчик текущего профита по паре
double Profit(int type) 
{
   double profit=0;
   for(int i=PositionsTotal()-1; i>=0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL)!=Symbol() || (int)PositionGetInteger(POSITION_MAGIC)!=Magic) continue;
      if(type==-1 || (int)PositionGetInteger(POSITION_TYPE)==type)
         profit += PositionGetDouble(POSITION_PROFIT)+PositionGetDouble(POSITION_SWAP);
   }
   return profit;
}

//======= Счетчик текущего профита по счету
double ProfitAll(int type) 
{
   double profit=0;
   for(int i=PositionsTotal()-1; i>=0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(!PositionSelectByTicket(ticket)) continue;
      if((int)PositionGetInteger(POSITION_MAGIC)!=Magic) continue;
      if(type==-1 || (int)PositionGetInteger(POSITION_TYPE)==type)
         profit += PositionGetDouble(POSITION_PROFIT)+PositionGetDouble(POSITION_SWAP);
   }
   return profit;
}

//======= Счетчик зафиксированой прибыли за сегодня 
double ProfitDey(int type) 
{
   double profit=0;
   datetime from = iTime(Symbol(), PERIOD_D1, 0);
   HistorySelect(from, TimeCurrent());
   for(int i=(int)HistoryDealsTotal()-1; i>=0; i--)
   {
      ulong ticket = HistoryDealGetTicket(i);
      if((int)HistoryDealGetInteger(ticket,DEAL_MAGIC)!=Magic) continue;
      if((ENUM_DEAL_ENTRY)HistoryDealGetInteger(ticket,DEAL_ENTRY)!=DEAL_ENTRY_OUT) continue;
      ENUM_DEAL_TYPE dtype=(ENUM_DEAL_TYPE)HistoryDealGetInteger(ticket,DEAL_TYPE);
      int dint=(dtype==DEAL_TYPE_BUY)?(int)POSITION_TYPE_BUY:(int)POSITION_TYPE_SELL;
      if(type==-1 || dint==type) profit += HistoryDealGetDouble(ticket,DEAL_PROFIT)+HistoryDealGetDouble(ticket,DEAL_SWAP);
   }
   return profit;
}

//======= Счетчик зафиксированой прибыли за вчера     
double ProfitTuDey(int type) 
{
   double profit=0;
   datetime from = iTime(Symbol(), PERIOD_D1, 1);
   datetime to   = iTime(Symbol(), PERIOD_D1, 0);
   HistorySelect(from, to);
   for(int i=(int)HistoryDealsTotal()-1; i>=0; i--)
   {
      ulong ticket = HistoryDealGetTicket(i);
      if((int)HistoryDealGetInteger(ticket,DEAL_MAGIC)!=Magic) continue;
      if((ENUM_DEAL_ENTRY)HistoryDealGetInteger(ticket,DEAL_ENTRY)!=DEAL_ENTRY_OUT) continue;
      ENUM_DEAL_TYPE dtype=(ENUM_DEAL_TYPE)HistoryDealGetInteger(ticket,DEAL_TYPE);
      int dint=(dtype==DEAL_TYPE_BUY)?(int)POSITION_TYPE_BUY:(int)POSITION_TYPE_SELL;
      if(type==-1 || dint==type) profit += HistoryDealGetDouble(ticket,DEAL_PROFIT)+HistoryDealGetDouble(ticket,DEAL_SWAP);
   }
   return profit;
}

//======= Счетчик зафиксированой прибыли за неделю  
double ProfitWeek(int type) 
{
   double profit=0;
   datetime from = iTime(Symbol(), PERIOD_W1, 0);
   HistorySelect(from, TimeCurrent());
   for(int i=(int)HistoryDealsTotal()-1; i>=0; i--)
   {
      ulong ticket = HistoryDealGetTicket(i);
      if((int)HistoryDealGetInteger(ticket,DEAL_MAGIC)!=Magic) continue;
      if((ENUM_DEAL_ENTRY)HistoryDealGetInteger(ticket,DEAL_ENTRY)!=DEAL_ENTRY_OUT) continue;
      ENUM_DEAL_TYPE dtype=(ENUM_DEAL_TYPE)HistoryDealGetInteger(ticket,DEAL_TYPE);
      int dint=(dtype==DEAL_TYPE_BUY)?(int)POSITION_TYPE_BUY:(int)POSITION_TYPE_SELL;
      if(type==-1 || dint==type) profit += HistoryDealGetDouble(ticket,DEAL_PROFIT)+HistoryDealGetDouble(ticket,DEAL_SWAP);
   }
   return profit;
}

//======= Счетчик зафиксированой прибыли за месяц          
double ProfitMontag(int type) 
{
   double profit=0;
   datetime from = iTime(Symbol(), PERIOD_MN1, 0);
   HistorySelect(from, TimeCurrent());
   for(int i=(int)HistoryDealsTotal()-1; i>=0; i--)
   {
      ulong ticket = HistoryDealGetTicket(i);
      if((int)HistoryDealGetInteger(ticket,DEAL_MAGIC)!=Magic) continue;
      if((ENUM_DEAL_ENTRY)HistoryDealGetInteger(ticket,DEAL_ENTRY)!=DEAL_ENTRY_OUT) continue;
      ENUM_DEAL_TYPE dtype=(ENUM_DEAL_TYPE)HistoryDealGetInteger(ticket,DEAL_TYPE);
      int dint=(dtype==DEAL_TYPE_BUY)?(int)POSITION_TYPE_BUY:(int)POSITION_TYPE_SELL;
      if(type==-1 || dint==type) profit += HistoryDealGetDouble(ticket,DEAL_PROFIT)+HistoryDealGetDouble(ticket,DEAL_SWAP);
   }
   return profit;
}

//======= Создаем текстовую метку 
void PutLabel(string name,int x,int y,string text)
{
   ObjectCreate(0,name,OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,name,OBJPROP_XDISTANCE,x);
   ObjectSetInteger(0,name,OBJPROP_YDISTANCE,y);
   ObjectSetInteger(0,name,OBJPROP_CORNER,1);
   ObjectSetString(0,name,OBJPROP_TEXT,text);
   ObjectSetString(0,name,OBJPROP_FONT,"Arial");
   ObjectSetInteger(0,name,OBJPROP_FONTSIZE,FontSizeInfo);
   ObjectSetInteger(0,name,OBJPROP_COLOR,TextColor);
   ObjectSetInteger(0,name,OBJPROP_HIDDEN,false);
   ObjectSetInteger(0,name,OBJPROP_BACK,false);
}

//======= Создаем вторую текстовую метку
void PutLabel_(string name,int x,int y,string text)
{
   ObjectCreate(0,name,OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,name,OBJPROP_XDISTANCE,x);
   ObjectSetInteger(0,name,OBJPROP_YDISTANCE,y);
   ObjectSetInteger(0,name,OBJPROP_CORNER,1);
   ObjectSetString(0,name,OBJPROP_TEXT,text);
   ObjectSetString(0,name,OBJPROP_FONT,"Arial");
   ObjectSetInteger(0,name,OBJPROP_FONTSIZE,FontSizeInfo);
   ObjectSetInteger(0,name,OBJPROP_COLOR,InfoDataColor);
   ObjectSetInteger(0,name,OBJPROP_HIDDEN,false);
   ObjectSetInteger(0,name,OBJPROP_BACK,false);
}

//======= Создаем прямоугольник
bool RectLabelCreate3(string name, int x, int y, int width, int height, color back_clr)
{
   ResetLastError(); 
   if(!ObjectCreate(0,name,OBJ_RECTANGLE_LABEL,0,0,0)) return(false); 
   ObjectSetInteger(0,name,OBJPROP_XDISTANCE,x); 
   ObjectSetInteger(0,name,OBJPROP_YDISTANCE,y); 
   ObjectSetInteger(0,name,OBJPROP_XSIZE,width); 
   ObjectSetInteger(0,name,OBJPROP_YSIZE,height); 
   ObjectSetInteger(0,name,OBJPROP_BGCOLOR,back_clr); 
   ObjectSetInteger(0,name,OBJPROP_BORDER_TYPE,BORDER_SUNKEN); 
   ObjectSetInteger(0,name,OBJPROP_CORNER,1); 
   ObjectSetInteger(0,name,OBJPROP_COLOR,clrBlue); 
   ObjectSetInteger(0,name,OBJPROP_WIDTH,1); 
   ObjectSetInteger(0,name,OBJPROP_BACK,false); 
   ObjectSetInteger(0,name,OBJPROP_HIDDEN,false); 
   return(true);
} 

//--- Кнопки торговой панели (Бай/Селл)
void PutButtonBS(string name,int x,int y,string text,int BWidth2,int BHeigh2,color FonButtonBS,color TXTButtonBS)
{
   ObjectCreate(0,name,OBJ_BUTTON,0,0,0);
   ObjectSetInteger(0,name,OBJPROP_XDISTANCE,x);
   ObjectSetInteger(0,name,OBJPROP_YDISTANCE,y);
   ObjectSetInteger(0,name,OBJPROP_XSIZE,BWidth2);
   ObjectSetInteger(0,name,OBJPROP_YSIZE,BHeigh2);
   ObjectSetInteger(0,name,OBJPROP_CORNER,1);
   ObjectSetString(0,name,OBJPROP_TEXT,text);
   ObjectSetString(0,name,OBJPROP_FONT,"Arial");
   ObjectSetInteger(0,name,OBJPROP_FONTSIZE,FontSizeInfo);
   ObjectSetInteger(0,name,OBJPROP_COLOR,TXTButtonBS);
   ObjectSetInteger(0,name,OBJPROP_BGCOLOR,FonButtonBS);
   ObjectSetInteger(0,name,OBJPROP_BORDER_COLOR,ButtonBorder);
   ObjectSetInteger(0,name,OBJPROP_HIDDEN,false);
   ObjectSetInteger(0,name,OBJPROP_BACK,false);
}

void PutLabelT(string name,int x,int y,string text, int space)
{
   ObjectCreate(0,name,OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,name,OBJPROP_XDISTANCE,x);
   ObjectSetInteger(0,name,OBJPROP_YDISTANCE,y);
   ObjectSetInteger(0,name,OBJPROP_CORNER,space);
   ObjectSetString(0,name,OBJPROP_TEXT,text);
   ObjectSetString(0,name,OBJPROP_FONT,"Arial");
   ObjectSetInteger(0,name,OBJPROP_FONTSIZE,FontSizeInfo);
   ObjectSetInteger(0,name,OBJPROP_COLOR,TextColor);
   ObjectSetInteger(0,name,OBJPROP_HIDDEN,false);
   ObjectSetInteger(0,name,OBJPROP_BACK,false);
}

//--- Цвет текста данных
void PutLabelD(string name,int x,int y,string data,int space)
{
   ObjectCreate(0,name,OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,name,OBJPROP_XDISTANCE,x);
   ObjectSetInteger(0,name,OBJPROP_YDISTANCE,y);
   ObjectSetInteger(0,name,OBJPROP_CORNER,space);
   ObjectSetString(0,name,OBJPROP_TEXT,data);
   ObjectSetString(0,name,OBJPROP_FONT,"Arial");
   ObjectSetInteger(0,name,OBJPROP_FONTSIZE,FontSizeInfo);
   ObjectSetInteger(0,name,OBJPROP_COLOR,InfoDataColor);
   ObjectSetInteger(0,name,OBJPROP_HIDDEN,false);
   ObjectSetInteger(0,name,OBJPROP_BACK,false);
}

//--- Создаем прямоугольную метку (фон)
bool RectLabelCreate(string name, int x, int y, int width, int height, color back_clr, int space)
{
   ResetLastError(); 
   if(!ObjectCreate(0,name,OBJ_RECTANGLE_LABEL,0,0,0)) return(false); 
   ObjectSetInteger(0,name,OBJPROP_XDISTANCE,x); 
   ObjectSetInteger(0,name,OBJPROP_YDISTANCE,y); 
   ObjectSetInteger(0,name,OBJPROP_XSIZE,width); 
   ObjectSetInteger(0,name,OBJPROP_YSIZE,height); 
   ObjectSetInteger(0,name,OBJPROP_BGCOLOR,back_clr); 
   ObjectSetInteger(0,name,OBJPROP_BORDER_TYPE,BORDER_SUNKEN); 
   ObjectSetInteger(0,name,OBJPROP_CORNER,space); 
   ObjectSetInteger(0,name,OBJPROP_COLOR,clrBlue); 
   ObjectSetInteger(0,name,OBJPROP_WIDTH,1); 
   ObjectSetInteger(0,name,OBJPROP_BACK,false); 
   ObjectSetInteger(0,name,OBJPROP_HIDDEN,false); 
   return(true);
}