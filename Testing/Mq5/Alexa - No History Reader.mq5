//+------------------------------------------------------------------+
//|                         Alexa - No History Reader.mq5             |
//| MQL5 conversion of the supplied MQL4 Expert Advisor              |
//+------------------------------------------------------------------+
#property copyright "Copyright 2020, MetaQuotes Software Corp."
#property version   "5.00"
#property strict

#include <Trade/Trade.mqh>

input string InpOrderComment = " ";
input int    InpSlippage     = 3;
input bool   InpEcnBroker    = false; // retained for preset compatibility
input long   InpMagicID      = 10101988;
input double InpLots         = 0.01;
input int    InpRisk         = 20;    // 0 = fixed lots
input double InpSL           = 200;
input double InpTP           = 50;
input bool   InpTrailing     = true;
input int    InpTrailingStop = 15;
input int    InpTrailingStep = 2;
input int    InpTrailStart   = 3;
input int    InpMaxOrders    = 20;
input double InpMaxSpread    = 20;

CTrade trade;
double g_pip=0.0;
double g_spread=0.0;

double BarOpen(const int shift)  { return iOpen(_Symbol,_Period,shift); }
double BarClose(const int shift) { return iClose(_Symbol,_Period,shift); }
double BarHigh(const int shift)  { return iHigh(_Symbol,_Period,shift); }
double BarLow(const int shift)   { return iLow(_Symbol,_Period,shift); }

int OnInit()
  {
   g_pip=_Point;
   if(_Digits==5 || _Digits==3 || _Digits==2)
      g_pip*=10.0;

   trade.SetExpertMagicNumber(InpMagicID);
   trade.SetDeviationInPoints((ulong)(InpSlippage*((_Digits==5 || _Digits==3 || _Digits==2)?10:1)));
   trade.SetTypeFillingBySymbol(_Symbol);
   ChartSetInteger(0,CHART_SHOW_GRID,false);
   return INIT_SUCCEEDED;
  }

void OnDeinit(const int reason)
  {
   Comment("");
  }

void OnTick()
  {
   if(InpTrailing)
      ApplyTrailingStop();

   static datetime previous_bar=0;
   datetime current_bar=iTime(_Symbol,_Period,0);
   if(current_bar==0 || current_bar==previous_bar)
      return;
   previous_bar=current_bar;

   if(Bars(_Symbol,_Period)<10 || Bars(_Symbol,PERIOD_H4)<2)
      return;

   if(iVolume(_Symbol,PERIOD_H4,0)>iVolume(_Symbol,PERIOD_H4,1))
      return;

   const double level=NormalizeDouble(iClose(_Symbol,PERIOD_H4,1),_Digits);
   const datetime level_time=iTime(_Symbol,PERIOD_H4,1);
   MakeLine(level);

   MqlTick tick;
   if(!SymbolInfoTick(_Symbol,tick))
      return;
   g_spread=(tick.ask-tick.bid)/g_pip;

   string side=(level>BarOpen(0)?"BUY":(level<BarOpen(0)?"SELL":"FLAT"));
   Comment("H4 ref. Time: ",TimeToString(level_time),"\n",side," - ",DoubleToString(level,_Digits));
   UpdatePanel();
   if(g_spread>InpMaxSpread || CountPositions()<InpMaxOrders)
     {
      if(g_spread>InpMaxSpread)
         return;
     }
   else
      return;

   double lots=GetLots();
   if(level>BarOpen(0) && IsBuyPinbar())
     {
      ClosePositions(POSITION_TYPE_SELL);
      if(HasMargin(ORDER_TYPE_BUY,lots,tick.ask))
        {
         double sl=(InpSL>0?NormalizeDouble(tick.ask-InpSL*g_pip,_Digits):0.0);
         double tp=(InpTP>0?NormalizeDouble(tick.ask+InpTP*g_pip,_Digits):0.0);
         if(!trade.Buy(lots,_Symbol,0.0,sl,tp,InpOrderComment))
            Print("Buy failed: ",trade.ResultRetcode()," ",trade.ResultRetcodeDescription());
        }
     }
   else if(level<BarOpen(0) && IsSellPinbar())
     {
      ClosePositions(POSITION_TYPE_BUY);
      if(HasMargin(ORDER_TYPE_SELL,lots,tick.bid))
        {
         double sl=(InpSL>0?NormalizeDouble(tick.bid+InpSL*g_pip,_Digits):0.0);
         double tp=(InpTP>0?NormalizeDouble(tick.bid-InpTP*g_pip,_Digits):0.0);
         if(!trade.Sell(lots,_Symbol,0.0,sl,tp,InpOrderComment))
            Print("Sell failed: ",trade.ResultRetcode()," ",trade.ResultRetcodeDescription());
        }
     }
  }

int VolumeDigits()
  {
   double step=SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_STEP);
   if(step<=0.0) return 2;
   int digits=0;
   while(digits<8 && MathAbs(step-NormalizeDouble(step,digits))>1e-10) digits++;
   return digits;
  }

double NormalizeLots(double lots)
  {
   double minlot=SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_MIN);
   double maxlot=SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_MAX);
   double step=SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_STEP);
   lots=MathMax(minlot,MathMin(maxlot,lots));
   if(step>0.0) lots=MathFloor(lots/step+1e-8)*step;
   return NormalizeDouble(lots,VolumeDigits());
  }

double GetLots()
  {
   double lots=InpLots;
   if(InpRisk!=0)
      lots=AccountInfoDouble(ACCOUNT_BALANCE)*InpRisk/100.0/10000.0;
   return NormalizeLots(lots);
  }

bool IsOurPosition(const ulong ticket)
  {
   if(!PositionSelectByTicket(ticket)) return false;
   return PositionGetString(POSITION_SYMBOL)==_Symbol &&
          PositionGetInteger(POSITION_MAGIC)==InpMagicID;
  }

int CountPositions(const int type=-1)
  {
   int count=0;
   for(int i=PositionsTotal()-1;i>=0;i--)
     {
      ulong ticket=PositionGetTicket(i);
      if(!IsOurPosition(ticket)) continue;
      if(type<0 || PositionGetInteger(POSITION_TYPE)==type) count++;
     }
   return count;
  }

void ClosePositions(const ENUM_POSITION_TYPE type)
  {
   for(int i=PositionsTotal()-1;i>=0;i--)
     {
      ulong ticket=PositionGetTicket(i);
      if(!IsOurPosition(ticket) || PositionGetInteger(POSITION_TYPE)!=type) continue;
      if(!trade.PositionClose(ticket))
         Print("Position close failed: ",trade.ResultRetcode()," ",trade.ResultRetcodeDescription());
     }
  }

bool HasMargin(const ENUM_ORDER_TYPE type,const double lots,const double price)
  {
   double margin=0.0;
   if(!OrderCalcMargin(type,_Symbol,lots,price,margin)) return false;
   return AccountInfoDouble(ACCOUNT_MARGIN_FREE)>margin;
  }

double AverageRange4()
  {
   double sum=0.0;
   int found=0,shift=1;
   while(found<4 && shift<100)
     {
      datetime t=iTime(_Symbol,_Period,shift);
      MqlDateTime dt;
      if(t>0 && TimeToStruct(t,dt) && dt.day_of_week!=0)
        {
         sum+=BarHigh(shift)-BarLow(shift);
         found++;
        }
      shift++;
     }
   return found>0?sum/found:0.0;
  }

bool IsBuyPinbar()
  {
   double op=BarOpen(1),cl=BarClose(1),hi=BarHigh(1),lo=BarLow(1);
   double range=hi-lo;
   if(range<=0.0) return false;
   if(!(cl>hi-range*0.4 && op>hi-range*0.4 && range>AverageRange4()*0.5 &&
        lo+range*0.25<BarLow(2))) return false;
   double minimum=DBL_MAX;
   for(int i=3;i<=5;i++) minimum=MathMin(minimum,BarLow(i));
   return minimum>lo;
  }

bool IsSellPinbar()
  {
   double op=BarOpen(1),cl=BarClose(1),hi=BarHigh(1),lo=BarLow(1);
   double range=hi-lo;
   if(range<=0.0) return false;
   if(!(cl<lo+range*0.4 && op<lo+range*0.4 && range>AverageRange4()*0.5 &&
        hi-range*0.25>BarHigh(2))) return false;
   double maximum=-DBL_MAX;
   for(int i=3;i<=5;i++) maximum=MathMax(maximum,BarHigh(i));
   return maximum<hi;
  }

void ApplyTrailingStop()
  {
   if(InpTrailingStop<=0) return;
   MqlTick tick;
   if(!SymbolInfoTick(_Symbol,tick)) return;
   for(int i=PositionsTotal()-1;i>=0;i--)
     {
      ulong ticket=PositionGetTicket(i);
      if(!IsOurPosition(ticket)) continue;
      ENUM_POSITION_TYPE type=(ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
      double open=PositionGetDouble(POSITION_PRICE_OPEN);
      double oldsl=PositionGetDouble(POSITION_SL);
      double tp=PositionGetDouble(POSITION_TP);
      double newsl=0.0;
      bool modify=false;
      if(type==POSITION_TYPE_BUY && tick.bid-open>=(InpTrailStart+InpTrailingStop)*g_pip)
        {
         newsl=NormalizeDouble(tick.bid-InpTrailingStop*g_pip,_Digits);
         modify=(oldsl==0.0 || newsl-oldsl>=InpTrailingStep*g_pip);
        }
      else if(type==POSITION_TYPE_SELL && open-tick.ask>=(InpTrailStart+InpTrailingStop)*g_pip)
        {
         newsl=NormalizeDouble(tick.ask+InpTrailingStop*g_pip,_Digits);
         modify=(oldsl==0.0 || oldsl-newsl>=InpTrailingStep*g_pip);
        }
      if(modify && !trade.PositionModify(ticket,newsl,tp))
         Print("Trailing modification failed: ",trade.ResultRetcode()," ",trade.ResultRetcodeDescription());
     }
  }

void MakeLine(const double price)
  {
   string name="level";
   if(ObjectFind(0,name)<0)
      ObjectCreate(0,name,OBJ_HLINE,0,0,price);
   ObjectSetDouble(0,name,OBJPROP_PRICE,price);
   ObjectSetInteger(0,name,OBJPROP_COLOR,clrAqua);
   ObjectSetInteger(0,name,OBJPROP_STYLE,STYLE_SOLID);
   ObjectSetInteger(0,name,OBJPROP_WIDTH,2);
   ObjectSetInteger(0,name,OBJPROP_BACK,true);
  }

void SetLabel(const string name,const string text,const int y,const color clr=clrGold)
  {
   if(ObjectFind(0,name)<0) ObjectCreate(0,name,OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,name,OBJPROP_CORNER,CORNER_RIGHT_UPPER);
   ObjectSetInteger(0,name,OBJPROP_XDISTANCE,10);
   ObjectSetInteger(0,name,OBJPROP_YDISTANCE,y);
   ObjectSetInteger(0,name,OBJPROP_COLOR,clr);
   ObjectSetInteger(0,name,OBJPROP_FONTSIZE,10);
   ObjectSetString(0,name,OBJPROP_FONT,"Arial");
   ObjectSetString(0,name,OBJPROP_TEXT,text);
  }

void UpdatePanel()
  {
   SetLabel("klc19","DOPE",40);
   SetLabel("klc20","Risk :: "+IntegerToString(InpRisk),80);
   SetLabel("klc21","Lots :: "+DoubleToString(GetLots(),VolumeDigits())+
                    " Free Mrg :: "+DoubleToString(AccountInfoDouble(ACCOUNT_MARGIN_FREE),2),100);
   SetLabel("klc22","Balance :: "+DoubleToString(AccountInfoDouble(ACCOUNT_BALANCE),2),120);
   SetLabel("klc23","Equity :: "+DoubleToString(AccountInfoDouble(ACCOUNT_EQUITY),2),140);
   SetLabel("klc24","Running P/L :: "+DoubleToString(AccountInfoDouble(ACCOUNT_PROFIT),2),160);
   SetLabel("klc27","Positions :: "+IntegerToString(CountPositions()),180);
   SetLabel("klc30","TP :: "+DoubleToString(InpTP,0)+" SL :: "+DoubleToString(InpSL,0)+
                    " TS :: "+IntegerToString(InpTrailingStop),200);
   SetLabel("klc01","Spread: "+DoubleToString(g_spread,1),240,clrDimGray);
  }
//+------------------------------------------------------------------+
