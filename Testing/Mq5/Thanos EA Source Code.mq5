//+------------------------------------------------------------------+
//|                                              Grid_Converted.mq5  |
//|   Conversão MQL4 -> MQL5 (cmillion)                              |
//|   Mantém lógica e funcionalidades do código MQL4 fornecido       |
//+------------------------------------------------------------------+

#include <Trade/Trade.mqh>

// Define constantes para códigos de setas (equivalentes MQL4)
const int SYMBOL_RIGHTPRICE = 220;

//=========================== Inputs ===============================//
input bool  Delete_Orders=true;
input int   DeleteHour=20;
input bool  Allow_BUY = true;
input bool  Allow_SELL = true;
input bool  EA_makes_first_order = true;
input bool  Open_order_on_trend = false;
input int   First_step = 10;
input int   Minimum_price_distance = 30;
input int   Move_step = 5;
input int   Distance_between_orders = 30;
input double Maximum_allowed_loss = 100000.0;
input double Close_loss_by_drawdown = 10.0;
input double Order_lotsize = 0.1;
input double Increase_lotsize_by = 0.0;
input double Multiply_lotsize_by = 1.5;
input int    Round_lotsize_to_decimals = 2;
input double Profit_for_closing_2_directions = 10.0;
input double Profit_for_closing_1_direction = 50.0;
input int    Auto_calculated_profit = 50;
input double Loss_for_closing = 100000.0;
input string __________ = "";
input string Trailing_settings = "0-Off  1-Candles  2-Fractals  3-Points";
input int    Trailing_type = 1;
input int    Trailing_step = 0;
input int    Minimum_trailing_profit = 10;
input int    Padding_by_fractals_or_candles = 0;
input int    Timeframe_fractals_or_candles = 15;
input string ___________ = "";
input string Other_settings = "";
input int    Magic = 777;
input int    Font_size = 10;
input color  Color_information = clrLime;
input int    Stoploss = 0;
input int    Takeprofit = 0;
input string ____________ = "";
input string Indicator_settings = "RSI";
input bool   Opening_1_order_on_indicators = false;
input int    Oversold_zone = 15;
input int    Overbought_zone = 85;
input int    RSI_Period = 5;
input int    Timeframe_indicator = 0;
input string _____________  = "";
input string Trading_hours = "";
input int    StartHour = 0;
input int    EndHour = 24;

//=========================== Globais ==============================//
bool   gi_292=false;
double gd_296=0.0;     // Tick value
int    gi_304=0;       // STOPLEVEL
int    gi_308=3456;    // unused in original
int    gi_312=0;       // AccountNumber
int    gi_316=0;       // Slippage/deviation
string gs_320="";

#define OP_BUY       0
#define OP_SELL      1
#define OP_BUYLIMIT  2
#define OP_SELLLIMIT 3
#define OP_BUYSTOP   4
#define OP_SELLSTOP  5

//=========================== Helpers ==============================//
int f0_11449(bool ai_0,int ai_4,int ai_8){ return (ai_0?ai_4:ai_8); }
void f0_13961(string as_0,int ai_8){ f0_4365(); }
void f0_4365(){}

int f0_4285(int m){
   if (m>43200) return 0;
   if (m>10080) return 43200;
   if (m>1440)  return 10080;
   if (m>240)   return 1440;
   if (m>60)    return 240;
   if (m>30)    return 60;
   if (m>15)    return 30;
   if (m>5)     return 15;
   if (m>1)     return 5;
   if (m==1)    return 1;
   if (m==0)    return (int)Period();
   return 0;
}
string f0_9613(int m){
   if(m==1) return "M1";
   if(m==5) return "M5";
   if(m==15)return "M15";
   if(m==30)return "M30";
   if(m==60)return "H1";
   if(m==240)return "H4";
   if(m==1440)return "D1";
   if(m==10080)return "W1";
   if(m==43200)return "MN1";
   return "period error";
}
ENUM_TIMEFRAMES TFfromMinutes(int m){
   switch(m){
      case 0:     return PERIOD_CURRENT;
      case 1:     return PERIOD_M1;
      case 5:     return PERIOD_M5;
      case 15:    return PERIOD_M15;
      case 30:    return PERIOD_M30;
      case 60:    return PERIOD_H1;
      case 240:   return PERIOD_H4;
      case 1440:  return PERIOD_D1;
      case 10080: return PERIOD_W1;
      case 43200: return PERIOD_MN1;
      default:    return PERIOD_CURRENT;
   }
}
void f0_12282(const string name,const string txt,int x,int y,color col){
   if(ObjectFind(0,name)<0){
      ObjectCreate(0,name,OBJ_LABEL,0,0,0);
      ObjectSetInteger(0,name,OBJPROP_CORNER,1);
      ObjectSetInteger(0,name,OBJPROP_XDISTANCE,x);
      ObjectSetInteger(0,name,OBJPROP_YDISTANCE,y);
   }
   ObjectSetString(0,name,OBJPROP_TEXT,txt);
   ObjectSetInteger(0,name,OBJPROP_COLOR,col);
   ObjectSetInteger(0,name,OBJPROP_FONTSIZE,Font_size);
   ObjectSetString(0,name,OBJPROP_FONT,"Arial");
}

// RSI (MQL5)
double GetRSI(const string sym, ENUM_TIMEFRAMES tf, int period, ENUM_APPLIED_PRICE price, int shift){
   static int handle=-1;
   static string last_sym="";
   static ENUM_TIMEFRAMES last_tf=PERIOD_CURRENT;
   static int last_period=0;
   static ENUM_APPLIED_PRICE last_price=PRICE_CLOSE;

   if(handle==INVALID_HANDLE || sym!=last_sym || tf!=last_tf || period!=last_period || price!=last_price){
      if(handle!=INVALID_HANDLE) IndicatorRelease(handle);
      handle=iRSI(sym,tf,period,price);
      last_sym=sym; last_tf=tf; last_period=period; last_price=price;
   }
   if(handle==INVALID_HANDLE) return 0.0;
   double buf[];
   if(CopyBuffer(handle,0,shift,1,buf)<=0) return 0.0;
   return buf[0];
}

// Fractals (MQL5): buffer 0=UPPER, 1=LOWER
double GetFractal(const string sym, ENUM_TIMEFRAMES tf, int buffer_index, int shift){
   static int handle=INVALID_HANDLE;
   static string last_sym="";
   static ENUM_TIMEFRAMES last_tf=PERIOD_CURRENT;
   if(handle==INVALID_HANDLE || sym!=last_sym || tf!=last_tf){
      if(handle!=INVALID_HANDLE) IndicatorRelease(handle);
      handle=iFractals(sym,tf);
      last_sym=sym; last_tf=tf;
   }
   if(handle==INVALID_HANDLE) return 0.0;
   double val[];
   if(CopyBuffer(handle,buffer_index,shift,1,val)<=0) return 0.0;
   return val[0];
}

// Converter tipos MQL5 -> MQL4 like
int MT5PosTypeToMT4(int pt){
   if(pt==(int)POSITION_TYPE_BUY)  return OP_BUY;
   if(pt==(int)POSITION_TYPE_SELL) return OP_SELL;
   return -1;
}
int MT5OrderTypeToMT4(ENUM_ORDER_TYPE ot){
   switch(ot){
      case ORDER_TYPE_BUY_STOP:  return OP_BUYSTOP;
      case ORDER_TYPE_SELL_STOP: return OP_SELLSTOP;
      case ORDER_TYPE_BUY_LIMIT:  return OP_BUYLIMIT;
      case ORDER_TYPE_SELL_LIMIT: return OP_SELLLIMIT;
      default: return -1;
   }
}

// Trade helpers (envio, modificação, remoção)
bool ModifyPositionByTicket(long ticket, double sl, double tp){
   MqlTradeRequest req; MqlTradeResult res; ZeroMemory(req); ZeroMemory(res);
   req.action  = TRADE_ACTION_SLTP;
   req.position= ticket;
   req.sl      = sl;
   req.tp      = tp;
   req.symbol  = _Symbol;
   bool ok = OrderSend(req,res);
   if(!ok || res.retcode!=TRADE_RETCODE_DONE){
      PrintFormat("Error PositionModify ticket=%I64d sl=%.5f tp=%.5f ret=%d",ticket,sl,tp,res.retcode);
      return false;
   }
   return true;
}
bool ModifyPendingOrderPrice(long order_ticket, double new_price){
   MqlTradeRequest req; MqlTradeResult res; ZeroMemory(req); ZeroMemory(res);
   req.action = TRADE_ACTION_MODIFY;
   req.order  = order_ticket;
   double sl=0,tp=0;
   // Keep current SL/TP
   if(OrderSelect(order_ticket)){
      sl = OrderGetDouble(ORDER_SL);
      tp = OrderGetDouble(ORDER_TP);
   }
   req.price = new_price;
   req.sl    = sl;
   req.tp    = tp;
   req.deviation = gi_316;
   bool ok = OrderSend(req,res);
   if(!ok || res.retcode!=TRADE_RETCODE_DONE){
      PrintFormat("Error OrderModify ticket=%I64d price=%.5f ret=%d",order_ticket,new_price,res.retcode);
      return false;
   }
   return true;
}
bool DeletePendingOrder(long order_ticket){
   MqlTradeRequest req; MqlTradeResult res; ZeroMemory(req); ZeroMemory(res);
   req.action = TRADE_ACTION_REMOVE;
   req.order  = order_ticket;
   bool ok=OrderSend(req,res);
   if(!ok || res.retcode!=TRADE_RETCODE_DONE){
      PrintFormat("Error OrderDelete ticket=%I64d ret=%d",order_ticket,res.retcode);
      return false;
   }
   return true;
}
bool ClosePositionByTicket(long ticket, double volume, int direction/*1 buy, -1 sell*/){
   // Envia uma ordem de mercado oposta ligada à posição
   MqlTradeRequest req; MqlTradeResult res; ZeroMemory(req); ZeroMemory(res);
   req.action   = TRADE_ACTION_DEAL;
   req.position = ticket;
   req.symbol   = _Symbol;
   req.volume   = volume;
   req.deviation= gi_316;

   // Descobre tipo da posição
   if(!PositionSelectByTicket(ticket)) return false;
   ENUM_POSITION_TYPE ptype=(ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
   if(ptype==POSITION_TYPE_BUY){
      req.type = ORDER_TYPE_SELL;
      req.price= SymbolInfoDouble(_Symbol,SYMBOL_BID);
   }else{
      req.type = ORDER_TYPE_BUY;
      req.price= SymbolInfoDouble(_Symbol,SYMBOL_ASK);
   }
   bool ok=OrderSend(req,res);
   if(!ok || (res.retcode!=TRADE_RETCODE_DONE && res.retcode!=TRADE_RETCODE_PLACED)){
      PrintFormat("Error ClosePosition ticket=%I64d ret=%d",ticket,res.retcode);
      return false;
   }
   return true;
}
bool SendPending(ENUM_ORDER_TYPE type,double lot,double price,int deviation,const string comment,int magic){
   MqlTradeRequest req; MqlTradeResult res; ZeroMemory(req); ZeroMemory(res);
   req.action      = TRADE_ACTION_PENDING;
   req.symbol      = _Symbol;
   req.type        = type;
   req.volume      = lot;
   req.price       = price;
   req.deviation   = deviation;
   req.type_time   = ORDER_TIME_GTC;
   req.type_filling= ORDER_FILLING_RETURN;
   req.magic       = magic;
   req.comment     = comment;

   bool ok = OrderSend(req,res);
   if(!ok || res.retcode!=TRADE_RETCODE_DONE){
      PrintFormat("OrderSend pending failed: type=%d lot=%.2f price=%.5f ret=%d",type,lot,price,res.retcode);
      return false;
   }
   return true;
}

// função de trailing/cálculo de nível
double f0_833(int ai_0,double ad_4,double ad_12){
   // ai_0: 1 (compra), -1 (venda)
   // ad_12: >2 pontos, 2 fractal, 1 candle
   double ld_20=0.0;
   int stopLevelPts=gi_304;
   int tfm = tf_fractals; // Use local var
   ENUM_TIMEFRAMES tf = TFfromMinutes(tfm);

   if(ad_12>2.0){
      if(ai_0==1) ld_20 = NormalizeDouble(ad_4 - ad_12*_Point, _Digits);
      else        ld_20 = NormalizeDouble(ad_4 + ad_12*_Point, _Digits);
   }else{
      if(ad_12==2.0){
         if(ai_0==1){
            int li_32=1;
            for(; li_32<100; li_32++){
               ld_20 = GetFractal(_Symbol, tf, 1/*LOWER*/, li_32);
               if(ld_20!=0.0){
                  ld_20 -= NormalizeDouble(Padding_by_fractals_or_candles*_Point,_Digits);
                  if(ad_4 - stopLevelPts*_Point > ld_20) break;
               }else ld_20=0;
            }
            ObjectDelete(0,"FR Buy");
            ObjectCreate(0,"FR Buy",OBJ_ARROW,0,iTime(_Symbol,PERIOD_CURRENT,0),ld_20+_Point);
            ObjectSetInteger(0,"FR Buy",OBJPROP_ARROWCODE,218);
            ObjectSetInteger(0,"FR Buy",OBJPROP_COLOR,clrRed);
         }
         if(ai_0==-1){
            int li_28=1;
            for(; li_28<100; li_28++){
               ld_20 = GetFractal(_Symbol, tf, 0/*UPPER*/, li_28);
               if(ld_20!=0.0){
                  ld_20 += NormalizeDouble(Padding_by_fractals_or_candles*_Point,_Digits);
                  if(ad_4 + stopLevelPts*_Point < ld_20) break;
               }else ld_20=0;
            }
            ObjectDelete(0,"FR Sell");
            ObjectCreate(0,"FR Sell",OBJ_ARROW,0,iTime(_Symbol,PERIOD_CURRENT,0),ld_20);
            ObjectSetInteger(0,"FR Sell",OBJPROP_ARROWCODE,217);
            ObjectSetInteger(0,"FR Sell",OBJPROP_COLOR,clrRed);
         }
      }
      if(ad_12==1.0){
         if(ai_0==1){
            int li_32=1;
            for(; li_32<500; li_32++){
               ld_20 = NormalizeDouble(iLow(_Symbol, tf, li_32) - Padding_by_fractals_or_candles*_Point,_Digits);
               if(ld_20!=0.0){
                  if(ad_4 - stopLevelPts*_Point > ld_20) break;
                  ld_20=0;
               }
            }
            ObjectDelete(0,"FR Buy");
            ObjectCreate(0,"FR Buy",OBJ_ARROW,0,iTime(_Symbol,tf,li_32),ld_20+_Point);
            ObjectSetInteger(0,"FR Buy",OBJPROP_ARROWCODE,159);
            ObjectSetInteger(0,"FR Buy",OBJPROP_COLOR,clrRed);
         }
         if(ai_0==-1){
            int li_28=1;
            for(; li_28<500; li_28++){
               ld_20 = NormalizeDouble(iHigh(_Symbol, tf, li_28) + Padding_by_fractals_or_candles*_Point,_Digits);
               if(ld_20!=0.0){
                  if(ad_4 + stopLevelPts*_Point < ld_20) break;
                  ld_20=0;
               }
            }
            ObjectDelete(0,"FR Sell");
            ObjectCreate(0,"FR Sell",OBJ_ARROW,0,iTime(_Symbol,tf,li_28),ld_20);
            ObjectSetInteger(0,"FR Sell",OBJPROP_ARROWCODE,159);
            ObjectSetInteger(0,"FR Sell",OBJPROP_COLOR,clrRed);
         }
      }
   }

   // Desenhos auxiliares
   if(ai_0==1){
      if(ld_20!=0.0){
         ObjectDelete(0,"SL Buy");
         ObjectCreate(0,"SL Buy",OBJ_ARROW,0,iTime(_Symbol,PERIOD_CURRENT,0)+60*Period(), ld_20);
         ObjectSetInteger(0,"SL Buy",OBJPROP_ARROWCODE,SYMBOL_RIGHTPRICE);
         ObjectSetInteger(0,"SL Buy",OBJPROP_COLOR,clrBlue);
      }
      if(gi_304>0){
         ObjectDelete(0,"STOPLEVEL-");
         ObjectCreate(0,"STOPLEVEL-",OBJ_ARROW,0,iTime(_Symbol,PERIOD_CURRENT,0)+60*Period(), ad_4 - gi_304*_Point);
         ObjectSetInteger(0,"STOPLEVEL-",OBJPROP_ARROWCODE,4);
         ObjectSetInteger(0,"STOPLEVEL-",OBJPROP_COLOR,clrBlue);
      }
   }
   if(ai_0==-1){
      if(ld_20!=0.0){
         ObjectDelete(0,"SL Sell");
         ObjectCreate(0,"SL Sell",OBJ_ARROW,0,iTime(_Symbol,PERIOD_CURRENT,0)+60*Period(), ld_20);
         ObjectSetInteger(0,"SL Sell",OBJPROP_ARROWCODE,SYMBOL_RIGHTPRICE);
         ObjectSetInteger(0,"SL Sell",OBJPROP_COLOR,clrPink);
      }
      if(gi_304>0){
         ObjectDelete(0,"STOPLEVEL+");
         ObjectCreate(0,"STOPLEVEL+",OBJ_ARROW,0,iTime(_Symbol,PERIOD_CURRENT,0)+60*Period(), ad_4 + gi_304*_Point);
         ObjectSetInteger(0,"STOPLEVEL+",OBJPROP_ARROWCODE,4);
         ObjectSetInteger(0,"STOPLEVEL+",OBJPROP_COLOR,clrPink);
      }
   }
   return ld_20;
}

// Horários de negociação
bool TradingHours(){
   datetime now = TimeCurrent();
   MqlDateTime dt;
   TimeToStruct(now, dt);
   int currenthour = dt.hour;
   int sh = StartHour, eh = EndHour;
   if(sh==0) sh=24;
   if(eh==0) eh=24;
   if(currenthour==0) currenthour=24;

   if(sh<eh){
      if(currenthour<sh || currenthour>=eh) return false;
   }else if(sh>eh){
      if(currenthour<sh && currenthour>=eh) return false;
   }
   return true;
}

// Apagar ordens pendentes (como no MQL4)
void DeleteOrders(int type=-1){
   for(int i=OrdersTotal()-1;i>=0;i--){
      ulong ticket = OrderGetTicket(i);
      if(ticket > 0){
         string sym=OrderGetString(ORDER_SYMBOL);
         long magic=OrderGetInteger(ORDER_MAGIC);
         ENUM_ORDER_TYPE ot=(ENUM_ORDER_TYPE)OrderGetInteger(ORDER_TYPE);
         if(sym==_Symbol && magic==Magic){
            if(ot==ORDER_TYPE_BUY_STOP || ot==ORDER_TYPE_SELL_STOP || ot==ORDER_TYPE_BUY_LIMIT || ot==ORDER_TYPE_SELL_LIMIT){
               DeletePendingOrder((long)ticket);
            }
         }
      }
   }
}

// Variáveis locais para inputs modificáveis
int tf_fractals;
int dist_between;
int first_step;

//=========================== OnInit ===============================//
int OnInit(){
   gs_320 = " " + AccountInfoString(ACCOUNT_CURRENCY);
   SymbolInfoDouble(_Symbol,SYMBOL_TRADE_TICK_VALUE,gd_296);
   tf_fractals = f0_4285(Timeframe_fractals_or_candles);
   if(_Digits==5 || _Digits==3) gi_316=30;

   Comment("Grid");
   gi_312 = (int)AccountInfoInteger(ACCOUNT_LOGIN);
   gi_292 = (AccountInfoInteger(ACCOUNT_TRADE_MODE)!=ACCOUNT_TRADE_MODE_DEMO) && (MQLInfoInteger(MQL_TESTER)==0);

   long stoplevel;
   SymbolInfoInteger(_Symbol,SYMBOL_TRADE_STOPS_LEVEL,stoplevel);
   gi_304 = (int)stoplevel;

   dist_between = Distance_between_orders;
   if(dist_between < gi_304){
      Alert("Distance_between_orders less STOPLEVEL, changed to ",gi_304);
      dist_between = gi_304;
   }
   first_step = First_step;
   if(first_step < gi_304){
      Alert("First_step less STOPLEVEL, changed to ",gi_304);
      first_step = gi_304;
   }

   int li_0 = Font_size + Font_size/2;
   // Labels
   ObjectCreate(0,"Balance",OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,"Balance",OBJPROP_CORNER,1);
   ObjectSetInteger(0,"Balance",OBJPROP_XDISTANCE,5);
   ObjectSetInteger(0,"Balance",OBJPROP_YDISTANCE,li_0);
   li_0 += Font_size*2;

   ObjectCreate(0,"Equity",OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,"Equity",OBJPROP_CORNER,1);
   ObjectSetInteger(0,"Equity",OBJPROP_XDISTANCE,5);
   ObjectSetInteger(0,"Equity",OBJPROP_YDISTANCE,li_0);
   li_0 += Font_size*2;

   ObjectCreate(0,"FreeMargin",OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,"FreeMargin",OBJPROP_CORNER,1);
   ObjectSetInteger(0,"FreeMargin",OBJPROP_XDISTANCE,5);
   ObjectSetInteger(0,"FreeMargin",OBJPROP_YDISTANCE,li_0);
   li_0 += Font_size*2;

   ObjectCreate(0,"ProfitB",OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,"ProfitB",OBJPROP_CORNER,1);
   ObjectSetInteger(0,"ProfitB",OBJPROP_XDISTANCE,5);
   ObjectSetInteger(0,"ProfitB",OBJPROP_YDISTANCE,li_0);
   li_0 += Font_size*2;

   ObjectCreate(0,"ProfitS",OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,"ProfitS",OBJPROP_CORNER,1);
   ObjectSetInteger(0,"ProfitS",OBJPROP_XDISTANCE,5);
   ObjectSetInteger(0,"ProfitS",OBJPROP_YDISTANCE,li_0);
   li_0 += Font_size*2;

   ObjectCreate(0,"Profit",OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,"Profit",OBJPROP_CORNER,1);
   ObjectSetInteger(0,"Profit",OBJPROP_XDISTANCE,5);
   ObjectSetInteger(0,"Profit",OBJPROP_YDISTANCE,li_0);
   li_0 += 3*Font_size;

   int li_4=0;
   string ls_8="Param"+(string)li_4;
   ObjectCreate(0,ls_8,OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,ls_8,OBJPROP_CORNER,1);
   ObjectSetInteger(0,ls_8,OBJPROP_XDISTANCE,5);
   ObjectSetInteger(0,ls_8,OBJPROP_YDISTANCE,li_0);
   li_0 += Font_size*2;
   ObjectSetString(0,ls_8,OBJPROP_TEXT,"--------------------------------------------------------");
   ObjectSetInteger(0,ls_8,OBJPROP_FONTSIZE,Font_size);
   ObjectSetString(0,ls_8,OBJPROP_FONT,"Arial");
   ObjectSetInteger(0,ls_8,OBJPROP_COLOR,clrAqua);

   li_4++;
   ls_8="Param"+(string)li_4;
   ObjectCreate(0,ls_8,OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,ls_8,OBJPROP_CORNER,1);
   ObjectSetInteger(0,ls_8,OBJPROP_XDISTANCE,5);
   ObjectSetInteger(0,ls_8,OBJPROP_YDISTANCE,li_0);
   li_0 += 3*Font_size;
   ObjectSetString(0,ls_8,OBJPROP_TEXT,"Parameter set");
   ObjectSetInteger(0,ls_8,OBJPROP_FONTSIZE,Font_size+2);
   ObjectSetString(0,ls_8,OBJPROP_FONT,"Arial");
   ObjectSetInteger(0,ls_8,OBJPROP_COLOR,clrAqua);

   li_4++;
   ls_8="Param"+(string)li_4;
   string ls_16="";
   if(Allow_BUY)  ls_16="Buy ";
   if(Allow_SELL) ls_16=ls_16+"Sell ";
   ObjectCreate(0,ls_8,OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,ls_8,OBJPROP_CORNER,1);
   ObjectSetInteger(0,ls_8,OBJPROP_XDISTANCE,5);
   ObjectSetInteger(0,ls_8,OBJPROP_YDISTANCE,li_0);
   li_0 += Font_size*2;
   ObjectSetString(0,ls_8,OBJPROP_TEXT,"Allowed "+ls_16);
   ObjectSetInteger(0,ls_8,OBJPROP_FONTSIZE,Font_size);
   ObjectSetString(0,ls_8,OBJPROP_FONT,"Arial");
   ObjectSetInteger(0,ls_8,OBJPROP_COLOR,Color_information);

   li_4++;
   ls_8="Object"+(string)li_4;
   if(!Open_order_on_trend){
      ObjectCreate(0,ls_8,OBJ_LABEL,0,0,0);
      ObjectSetInteger(0,ls_8,OBJPROP_CORNER,1);
      ObjectSetInteger(0,ls_8,OBJPROP_XDISTANCE,5);
      ObjectSetInteger(0,ls_8,OBJPROP_YDISTANCE,li_0);
      li_0 += Font_size*2;
      ObjectSetString(0,ls_8,OBJPROP_TEXT,"Do not open orders on a trend ");
      ObjectSetInteger(0,ls_8,OBJPROP_FONTSIZE,Font_size);
      ObjectSetString(0,ls_8,OBJPROP_FONT,"Arial");
      ObjectSetInteger(0,ls_8,OBJPROP_COLOR,Color_information);
      li_4++;
      ls_8="Object"+(string)li_4;
   }
   ObjectCreate(0,ls_8,OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,ls_8,OBJPROP_CORNER,1);
   ObjectSetInteger(0,ls_8,OBJPROP_XDISTANCE,5);
   ObjectSetInteger(0,ls_8,OBJPROP_YDISTANCE,li_0);
   li_0 += 2*Font_size;
   ObjectSetString(0,ls_8,OBJPROP_TEXT, EA_makes_first_order ? "Expert advisor initiate first order" : "Expert advisor await first order");
   ObjectSetInteger(0,ls_8,OBJPROP_FONTSIZE,Font_size);
   ObjectSetString(0,ls_8,OBJPROP_FONT,"Arial");
   ObjectSetInteger(0,ls_8,OBJPROP_COLOR,Color_information);

   li_4++;
   ls_8="Param"+(string)li_4;
   ObjectCreate(0,ls_8,OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,ls_8,OBJPROP_CORNER,1);
   ObjectSetInteger(0,ls_8,OBJPROP_XDISTANCE,5);
   ObjectSetInteger(0,ls_8,OBJPROP_YDISTANCE,li_0);
   li_0 += Font_size*2;
   ObjectSetString(0,ls_8,OBJPROP_TEXT, StringFormat("The first step %d p",first_step));
   ObjectSetInteger(0,ls_8,OBJPROP_FONTSIZE,Font_size);
   ObjectSetString(0,ls_8,OBJPROP_FONT,"Arial");
   ObjectSetInteger(0,ls_8,OBJPROP_COLOR,Color_information);

   li_4++;
   ls_8="Param"+(string)li_4;
   ObjectCreate(0,ls_8,OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,ls_8,OBJPROP_CORNER,1);
   ObjectSetInteger(0,ls_8,OBJPROP_XDISTANCE,5);
   ObjectSetInteger(0,ls_8,OBJPROP_YDISTANCE,li_0);
   li_0 += Font_size*2;
   ObjectSetString(0,ls_8,OBJPROP_TEXT, StringFormat("The minimum distance to the price %d p",Minimum_price_distance));
   ObjectSetInteger(0,ls_8,OBJPROP_FONTSIZE,Font_size);
   ObjectSetString(0,ls_8,OBJPROP_FONT,"Arial");
   ObjectSetInteger(0,ls_8,OBJPROP_COLOR,Color_information);

   li_4++;
   ls_8="Param"+(string)li_4;
   ObjectCreate(0,ls_8,OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,ls_8,OBJPROP_CORNER,1);
   ObjectSetInteger(0,ls_8,OBJPROP_XDISTANCE,5);
   ObjectSetInteger(0,ls_8,OBJPROP_YDISTANCE,li_0);
   li_0 += Font_size*2;
   ObjectSetString(0,ls_8,OBJPROP_TEXT, StringFormat("Step change orders %d p",Move_step));
   ObjectSetInteger(0,ls_8,OBJPROP_FONTSIZE,Font_size);
   ObjectSetString(0,ls_8,OBJPROP_FONT,"Arial");
   ObjectSetInteger(0,ls_8,OBJPROP_COLOR,Color_information);

   li_4++;
   ls_8="Param"+(string)li_4;
   ObjectCreate(0,ls_8,OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,ls_8,OBJPROP_CORNER,1);
   ObjectSetInteger(0,ls_8,OBJPROP_XDISTANCE,5);
   ObjectSetInteger(0,ls_8,OBJPROP_YDISTANCE,li_0);
   li_0 += Font_size*2;
   ObjectSetString(0,ls_8,OBJPROP_TEXT, StringFormat("Step between orders %d p",dist_between));
   ObjectSetInteger(0,ls_8,OBJPROP_FONTSIZE,Font_size);
   ObjectSetString(0,ls_8,OBJPROP_FONT,"Arial");
   ObjectSetInteger(0,ls_8,OBJPROP_COLOR,Color_information);

   li_4++;
   ls_8="Param"+(string)li_4;
   ObjectCreate(0,ls_8,OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,ls_8,OBJPROP_CORNER,1);
   ObjectSetInteger(0,ls_8,OBJPROP_XDISTANCE,5);
   ObjectSetInteger(0,ls_8,OBJPROP_YDISTANCE,li_0);
   li_0 += Font_size*2;
   ObjectSetString(0,ls_8,OBJPROP_TEXT,"--------------------------------------------------------");
   ObjectSetInteger(0,ls_8,OBJPROP_FONTSIZE,Font_size);
   ObjectSetString(0,ls_8,OBJPROP_FONT,"Arial");
   ObjectSetInteger(0,ls_8,OBJPROP_COLOR,clrAqua);

   li_4++;
   ls_8="Param"+(string)li_4;
   ObjectCreate(0,ls_8,OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,ls_8,OBJPROP_CORNER,1);
   ObjectSetInteger(0,ls_8,OBJPROP_XDISTANCE,35);
   ObjectSetInteger(0,ls_8,OBJPROP_YDISTANCE,li_0);
   ObjectSetString(0,ls_8,OBJPROP_TEXT,"Do not open order in this direction");
   ObjectSetInteger(0,ls_8,OBJPROP_FONTSIZE,Font_size);
   ObjectSetString(0,ls_8,OBJPROP_FONT,"Arial");
   ObjectSetInteger(0,ls_8,OBJPROP_COLOR,Color_information);

   li_4++;
   ls_8="Param"+(string)li_4;
   ObjectCreate(0,"Char.b",OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,"Char.b",OBJPROP_CORNER,1);
   ObjectSetInteger(0,"Char.b",OBJPROP_XDISTANCE,5);
   ObjectSetInteger(0,"Char.b",OBJPROP_YDISTANCE,li_0);
   li_0 += Font_size*2;
   ObjectSetString(0,"Char.b",OBJPROP_TEXT,(string)(char)233);
   ObjectSetInteger(0,"Char.b",OBJPROP_FONTSIZE,Font_size);
   ObjectSetString(0,"Char.b",OBJPROP_FONT,"Wingdings");
   ObjectSetInteger(0,"Char.b",OBJPROP_COLOR,clrLime);

   ObjectCreate(0,ls_8,OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,ls_8,OBJPROP_CORNER,1);
   ObjectSetInteger(0,ls_8,OBJPROP_XDISTANCE,35);
   ObjectSetInteger(0,ls_8,OBJPROP_YDISTANCE,li_0);
   ObjectSetString(0,ls_8,OBJPROP_TEXT, StringFormat("when the loss %.2f%s",Maximum_allowed_loss,gs_320));
   ObjectSetInteger(0,ls_8,OBJPROP_FONTSIZE,Font_size);
   ObjectSetString(0,ls_8,OBJPROP_FONT,"Arial");
   ObjectSetInteger(0,ls_8,OBJPROP_COLOR,Color_information);

   li_4++;
   ls_8="Param"+(string)li_4;
   ObjectCreate(0,"Char.s",OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,"Char.s",OBJPROP_CORNER,1);
   ObjectSetInteger(0,"Char.s",OBJPROP_XDISTANCE,5);
   ObjectSetInteger(0,"Char.s",OBJPROP_YDISTANCE,li_0);
   li_0 += Font_size*2;
   ObjectSetString(0,"Char.s",OBJPROP_TEXT,(string)(char)234);
   ObjectSetInteger(0,"Char.s",OBJPROP_FONTSIZE,Font_size);
   ObjectSetString(0,"Char.s",OBJPROP_FONT,"Wingdings");
   ObjectSetInteger(0,"Char.s",OBJPROP_COLOR,clrLime);

   ObjectCreate(0,ls_8,OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,ls_8,OBJPROP_CORNER,1);
   ObjectSetInteger(0,ls_8,OBJPROP_XDISTANCE,5);
   ObjectSetInteger(0,ls_8,OBJPROP_YDISTANCE,li_0);
   li_0 += Font_size*2;
   ObjectSetString(0,ls_8,OBJPROP_TEXT,"--------------------------------------------------------");
   ObjectSetInteger(0,ls_8,OBJPROP_FONTSIZE,Font_size);
   ObjectSetString(0,ls_8,OBJPROP_FONT,"Arial");
   ObjectSetInteger(0,ls_8,OBJPROP_COLOR,clrAqua);

   li_4++;
   ls_8="Param"+(string)li_4;
   ObjectCreate(0,ls_8,OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,ls_8,OBJPROP_CORNER,1);
   ObjectSetInteger(0,ls_8,OBJPROP_XDISTANCE,35);
   ObjectSetInteger(0,ls_8,OBJPROP_YDISTANCE,li_0);
   li_0 += Font_size;
   ObjectSetString(0,ls_8,OBJPROP_TEXT,"closing of the general profit of");
   ObjectSetInteger(0,ls_8,OBJPROP_FONTSIZE,Font_size);
   ObjectSetString(0,ls_8,OBJPROP_FONT,"Arial");
   ObjectSetInteger(0,ls_8,OBJPROP_COLOR,Color_information);

   li_4++;
   ls_8="Param"+(string)li_4;
   ObjectCreate(0,"Char.op",OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,"Char.op",OBJPROP_CORNER,1);
   ObjectSetInteger(0,"Char.op",OBJPROP_XDISTANCE,5);
   ObjectSetInteger(0,"Char.op",OBJPROP_YDISTANCE,li_0);
   li_0 += Font_size;
   ObjectSetString(0,"Char.op",OBJPROP_TEXT,(string)(char)75);
   ObjectSetInteger(0,"Char.op",OBJPROP_FONTSIZE,Font_size+2);
   ObjectSetString(0,"Char.op",OBJPROP_FONT,"Wingdings");
   ObjectSetInteger(0,"Char.op",OBJPROP_COLOR,clrSilver);

   ObjectCreate(0,ls_8,OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,ls_8,OBJPROP_CORNER,1);
   ObjectSetInteger(0,ls_8,OBJPROP_XDISTANCE,35);
   ObjectSetInteger(0,ls_8,OBJPROP_YDISTANCE,li_0);
   li_0 += Font_size*2;
   ObjectSetString(0,ls_8,OBJPROP_TEXT, StringFormat("at drawdown %.2f%s",Close_loss_by_drawdown,gs_320));
   ObjectSetInteger(0,ls_8,OBJPROP_FONTSIZE,Font_size);
   ObjectSetString(0,ls_8,OBJPROP_FONT,"Arial");
   ObjectSetInteger(0,ls_8,OBJPROP_COLOR,Color_information);

   li_4++;
   ls_8="Param"+(string)li_4;
   ObjectCreate(0,ls_8,OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,ls_8,OBJPROP_CORNER,1);
   ObjectSetInteger(0,ls_8,OBJPROP_XDISTANCE,5);
   ObjectSetInteger(0,ls_8,OBJPROP_YDISTANCE,li_0);
   li_0 += Font_size*2;
   ObjectSetString(0,ls_8,OBJPROP_TEXT,"--------------------------------------------------------");
   ObjectSetInteger(0,ls_8,OBJPROP_FONTSIZE,Font_size);
   ObjectSetString(0,ls_8,OBJPROP_FONT,"Arial");
   ObjectSetInteger(0,ls_8,OBJPROP_COLOR,clrAqua);

   li_4++;
   ls_8="Param"+(string)li_4;
   ObjectCreate(0,ls_8,OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,ls_8,OBJPROP_CORNER,1);
   ObjectSetInteger(0,ls_8,OBJPROP_XDISTANCE,5);
   ObjectSetInteger(0,ls_8,OBJPROP_YDISTANCE,li_0);
   li_0 += 2*Font_size;
   ObjectSetString(0,ls_8,OBJPROP_TEXT, StringFormat("Startiong lot %.2f + %.2f x %.2f",Order_lotsize,Increase_lotsize_by,Multiply_lotsize_by));
   ObjectSetInteger(0,ls_8,OBJPROP_FONTSIZE,Font_size);
   ObjectSetString(0,ls_8,OBJPROP_FONT,"Arial");
   ObjectSetInteger(0,ls_8,OBJPROP_COLOR,Color_information);

   li_4++;
   ls_8="Param"+(string)li_4;
   ObjectCreate(0,ls_8,OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,ls_8,OBJPROP_CORNER,1);
   ObjectSetInteger(0,ls_8,OBJPROP_XDISTANCE,5);
   ObjectSetInteger(0,ls_8,OBJPROP_YDISTANCE,li_0);
   li_0 += 2*Font_size;
   ObjectSetString(0,ls_8,OBJPROP_TEXT, StringFormat("Profit for closing all %.2f%s",Profit_for_closing_2_directions,gs_320));
   ObjectSetInteger(0,ls_8,OBJPROP_FONTSIZE,Font_size);
   ObjectSetString(0,ls_8,OBJPROP_FONT,"Arial");
   ObjectSetInteger(0,ls_8,OBJPROP_COLOR,Color_information);

   li_4++;
   ls_8="Param"+(string)li_4;
   if(Auto_calculated_profit==0){
      f0_12282("Profit_for_closing_1_direction", StringFormat("Profit closing directions %.2f%s",Profit_for_closing_1_direction,gs_320),5,li_0,Color_information);
   }else{
      f0_12282("StopProfit1", StringFormat("Auto Profit closing %d",Auto_calculated_profit),5,li_0,Color_information);
      li_0 += Font_size*2;
      f0_12282("StopProfit2", StringFormat("Auto Profit closing %d",Auto_calculated_profit),5,li_0,Color_information);
   }
   li_0 += 2*Font_size;

   li_4++;
   ls_8="Param"+(string)li_4;
   ObjectCreate(0,ls_8,OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,ls_8,OBJPROP_CORNER,1);
   ObjectSetInteger(0,ls_8,OBJPROP_XDISTANCE,5);
   ObjectSetInteger(0,ls_8,OBJPROP_YDISTANCE,li_0);
   li_0 += 2*Font_size;
   ObjectSetString(0,ls_8,OBJPROP_TEXT, StringFormat("Close loss buy/sell %.2f%s",Loss_for_closing,gs_320));
   ObjectSetInteger(0,ls_8,OBJPROP_FONTSIZE,Font_size);
   ObjectSetString(0,ls_8,OBJPROP_FONT,"Arial");
   ObjectSetInteger(0,ls_8,OBJPROP_COLOR,Color_information);

   li_4++;
   ls_8="Param"+(string)li_4;
   if(Trailing_type!=0){
      string ttxt="";
      if(Trailing_type==1) ttxt = StringFormat("The candles %s +- %d", f0_9613(tf_fractals), Padding_by_fractals_or_candles);
      if(Trailing_type==2) ttxt = StringFormat("The fractals %s %s +- %d", f0_9613(tf_fractals), f0_9613(tf_fractals), Padding_by_fractals_or_candles);
      if(Trailing_type>2)  ttxt = StringFormat("Pips %d p", Trailing_type);
      if(Trailing_step>0)  ttxt = StringFormat("%s  increment %d p  minimum profit trail %d p", ttxt, Trailing_step, Minimum_trailing_profit);

      ObjectCreate(0,ls_8,OBJ_LABEL,0,0,0);
      ObjectSetInteger(0,ls_8,OBJPROP_CORNER,1);
      ObjectSetInteger(0,ls_8,OBJPROP_XDISTANCE,5);
      ObjectSetInteger(0,ls_8,OBJPROP_YDISTANCE,li_0);
      li_0 += 2*Font_size;
      ObjectSetString(0,ls_8,OBJPROP_TEXT, "Trailing_type "+ttxt);
      ObjectSetInteger(0,ls_8,OBJPROP_FONTSIZE,Font_size);
      ObjectSetString(0,ls_8,OBJPROP_FONT,"Arial");
      ObjectSetInteger(0,ls_8,OBJPROP_COLOR,Color_information);

      li_4++;
      ls_8="Param"+(string)li_4;
   }
   ObjectCreate(0,ls_8,OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,ls_8,OBJPROP_CORNER,1);
   ObjectSetInteger(0,ls_8,OBJPROP_XDISTANCE,5);
   ObjectSetInteger(0,ls_8,OBJPROP_YDISTANCE,li_0);
   li_0 += Font_size*2;
   ObjectSetString(0,ls_8,OBJPROP_TEXT,"--------------------------------------------------------");
   ObjectSetInteger(0,ls_8,OBJPROP_FONTSIZE,Font_size);
   ObjectSetString(0,ls_8,OBJPROP_FONT,"Arial");
   ObjectSetInteger(0,ls_8,OBJPROP_COLOR,clrAqua);

   // Negativos conforme original
   // (no código original, torna negativos para comparar com perda/drawdown)
   // Faremos no OnTick (pois Inputs são const no MQL5)
   return(INIT_SUCCEEDED);
}

void OnDeinit(const int reason){
   ObjectsDeleteAll(0,0,-1);
}

//=========================== Fechamento (f0_1880) =================//
int f0_1880(int ai_0){
   // ai_0: 1 fecha buys, -1 fecha sells, 0 fecha tudo
   int li_8=0, li_12=0, li_16=0, li_24=0;

   while(true){
      // Fechar posições
      for(int i=PositionsTotal()-1;i>=0;i--){
         ulong ticket = PositionGetTicket(i);
         if(ticket > 0){
            string sym = PositionGetString(POSITION_SYMBOL);
            long magic = PositionGetInteger(POSITION_MAGIC);
            if(sym==_Symbol && magic==Magic){
               ENUM_POSITION_TYPE ptype=(ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
               double vol  = PositionGetDouble(POSITION_VOLUME);
               if((ptype==POSITION_TYPE_BUY  && ai_0==1) || ai_0==0){
                  if(ClosePositionByTicket(ticket, vol, 1)){
                     Comment(StringFormat("Closed order N %I64d  profit %.2f     %s",ticket, PositionGetDouble(POSITION_PROFIT), TimeToString(TimeCurrent(),TIME_SECONDS)));
                  }
               }
               if((ptype==POSITION_TYPE_SELL && ai_0==-1) || ai_0==0){
                  if(ClosePositionByTicket(ticket, vol, -1)){
                     Comment(StringFormat("Closed order N %I64d  profit %.2f     %s",ticket, PositionGetDouble(POSITION_PROFIT), TimeToString(TimeCurrent(),TIME_SECONDS)));
                  }
               }
            }
         }
      }
      // Deletar pendentes
      for(int i=OrdersTotal()-1;i>=0;i--){
         ulong tk = OrderGetTicket(i);
         if(tk > 0){
            string sym=OrderGetString(ORDER_SYMBOL);
            long magic=OrderGetInteger(ORDER_MAGIC);
            ENUM_ORDER_TYPE ot=(ENUM_ORDER_TYPE)OrderGetInteger(ORDER_TYPE);
            if(sym==_Symbol && magic==Magic){
               if( (ot==ORDER_TYPE_BUY_STOP  && (ai_0==1 || ai_0==0)) ||
                   (ot==ORDER_TYPE_SELL_STOP && (ai_0==-1|| ai_0==0)) ){
                  DeletePendingOrder((long)tk);
               }
            }
         }
      }

      li_24=0;
      // Conta remanescente
      for(int i=0;i<PositionsTotal();i++){
         ulong ticket = PositionGetTicket(i);
         if(ticket > 0){
            string sym = PositionGetString(POSITION_SYMBOL);
            long magic = PositionGetInteger(POSITION_MAGIC);
            if(sym==_Symbol && magic==Magic){
               ENUM_POSITION_TYPE ptype=(ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
               if( (ptype==POSITION_TYPE_BUY  && (ai_0==1 || ai_0==0)) ||
                   (ptype==POSITION_TYPE_SELL && (ai_0==-1|| ai_0==0)) ) li_24++;
            }
         }
      }
      for(int i=0;i<OrdersTotal();i++){
         ulong tk = OrderGetTicket(i);
         if(tk > 0){
            string sym=OrderGetString(ORDER_SYMBOL);
            long magic=OrderGetInteger(ORDER_MAGIC);
            ENUM_ORDER_TYPE ot=(ENUM_ORDER_TYPE)OrderGetInteger(ORDER_TYPE);
            if(sym==_Symbol && magic==Magic){
               if( (ot==ORDER_TYPE_BUY_STOP  && (ai_0==1|| ai_0==0)) ||
                   (ot==ORDER_TYPE_SELL_STOP && (ai_0==-1|| ai_0==0)) ) li_24++;
            }
         }
      }
      if(li_24==0) break;
      li_12++;
      if(li_12>10){
         Alert(_Symbol," Failed to close all trades, there are still ",li_24);
         return 0;
      }
      Sleep(1000);
   }
   return 1;
}

//=========================== OnTick ===============================//
void OnTick(){
   datetime now = TimeCurrent();
   MqlDateTime dt;
   TimeToStruct(now, dt);
   if(Delete_Orders && dt.hour == DeleteHour) DeleteOrders();

   // Transformar limites como no init do MQL4
   double Close_loss_by_drawdown_neg = -1.0 * Close_loss_by_drawdown;
   double Maximum_allowed_loss_neg   = -1.0 * Maximum_allowed_loss;
   double Loss_for_closing_neg       = -1.0 * Loss_for_closing;

   // Leitura de preços
   double Bid = SymbolInfoDouble(_Symbol,SYMBOL_BID);
   double Ask = SymbolInfoDouble(_Symbol,SYMBOL_ASK);

   // Variáveis
   double ld_0=0,ld_16=0,ld_24=0,ld_32=0,ld_40=0,ld_48=0,ld_56=0,ld_64=0,ld_72=0,ld_80=0;
   int li_88=0,li_92=0,li_96=0,li_100=0,li_104=0,li_108=0,li_112=0;
   double ld_116=0,ld_124=0,ld_132=0,ld_140=0,ld_148=0,ld_156=0,ld_164=0,ld_172=0,ld_180=0,ld_188=0,ld_200=0,ld_208=0,ld_216=0,ld_224=0,ld_232=0,ld_240=0;

   // Percorrer posições e pendentes
   // Posições
   for(int i=0;i<PositionsTotal();i++){
      ulong ticket = PositionGetTicket(i);
      if(ticket > 0){
         string sym=PositionGetString(POSITION_SYMBOL);
         long magic=PositionGetInteger(POSITION_MAGIC);
         if(sym==_Symbol && magic==Magic){
            int type = MT5PosTypeToMT4((int)PositionGetInteger(POSITION_TYPE));
            double lots=PositionGetDouble(POSITION_VOLUME);
            double price_open = NormalizeDouble(PositionGetDouble(POSITION_PRICE_OPEN),_Digits);
            double sl = NormalizeDouble(PositionGetDouble(POSITION_SL),_Digits);
            double tp = NormalizeDouble(PositionGetDouble(POSITION_TP),_Digits);

            if(type==OP_BUY){
               li_88++;
               ld_32 += lots;
               ld_172 += price_open*lots;
               if(ld_116<price_open || ld_116==0.0) ld_116=price_open;
               if(ld_124>price_open || ld_124==0.0) ld_124=price_open;
               double pos_profit = PositionGetDouble(POSITION_PROFIT);
               ld_24 += pos_profit;

               if(sl==0.0 && Stoploss>=gi_304 && Stoploss!=0) ld_56 = NormalizeDouble(price_open - Stoploss*_Point,_Digits);
               else ld_56=sl;
               if(tp==0.0 && Takeprofit>=gi_304 && Takeprofit!=0) ld_64 = NormalizeDouble(price_open + Takeprofit*_Point,_Digits);
               else ld_64=tp;

               if(ld_56>sl || ld_64!=tp){
                  ModifyPositionByTicket(ticket, ld_56, ld_64);
               }
            }
            if(type==OP_SELL){
               li_92++;
               ld_40 += lots;
               ld_164 += price_open*lots;
               if(ld_140>price_open || ld_140==0.0) ld_140=price_open;
               if(ld_132<price_open || ld_132==0.0) ld_132=price_open;
               double pos_profit = PositionGetDouble(POSITION_PROFIT);
               ld_16 += pos_profit;

               if(sl==0.0 && Stoploss>=gi_304 && Stoploss!=0) ld_56 = NormalizeDouble(price_open + Stoploss*_Point,_Digits);
               else ld_56=sl;
               if(tp==0.0 && Takeprofit>=gi_304 && Takeprofit!=0) ld_64 = NormalizeDouble(price_open - Takeprofit*_Point,_Digits);
               else ld_64=tp;

               if(ld_56<sl || (sl==0.0 && ld_56!=0.0) || ld_64!=tp){
                  ModifyPositionByTicket(ticket, ld_56, ld_64);
               }
            }
         }
      }
   }

   // Ordens pendentes
   for(int i=0;i<OrdersTotal();i++){
      ulong ticket = OrderGetTicket(i);
      if(ticket > 0){
         string sym=OrderGetString(ORDER_SYMBOL);
         long magic=OrderGetInteger(ORDER_MAGIC);
         if(sym==_Symbol && magic==Magic){
            ENUM_ORDER_TYPE ot=(ENUM_ORDER_TYPE)OrderGetInteger(ORDER_TYPE);
            int type = MT5OrderTypeToMT4(ot);
            double price=NormalizeDouble(OrderGetDouble(ORDER_PRICE_OPEN),_Digits);
            double sl   =NormalizeDouble(OrderGetDouble(ORDER_SL),_Digits);
            double tp   =NormalizeDouble(OrderGetDouble(ORDER_TP),_Digits);

            if(type==OP_BUYSTOP){
               li_96++;
               if(ld_116<price || ld_116==0.0) ld_116=price;
               li_108=(int)ticket;
               ld_148=price;
            }
            if(type==OP_SELLSTOP){
               li_100++;
               if(ld_140>price || ld_140==0.0) ld_140=price;
               li_112=(int)ticket;
               ld_156=price;
            }
         }
      }
   }

   // Médias preço e marcadores
   ObjectDelete(0,"SLb");
   ObjectDelete(0,"SLs");
   if(li_88>0){
      ld_180 = NormalizeDouble(ld_172/ld_32,_Digits);
      ObjectCreate(0,"SLb",OBJ_ARROW,0,iTime(_Symbol,PERIOD_CURRENT,0),ld_180);
      ObjectSetInteger(0,"SLb",OBJPROP_ARROWCODE,SYMBOL_RIGHTPRICE);
      ObjectSetInteger(0,"SLb",OBJPROP_COLOR,clrBlue);
   }
   if(li_92>0){
      ld_188 = NormalizeDouble(ld_164/ld_40,_Digits);
      ObjectCreate(0,"SLs",OBJ_ARROW,0,iTime(_Symbol,PERIOD_CURRENT,0),ld_188);
      ObjectSetInteger(0,"SLs",OBJPROP_ARROWCODE,SYMBOL_RIGHTPRICE);
      ObjectSetInteger(0,"SLs",OBJPROP_COLOR,clrRed);
   }

   // Trailing
   if(Trailing_type!=0){
      for(int i=0;i<PositionsTotal();i++){
         ulong ticket = PositionGetTicket(i);
         if(ticket > 0){
            string sym=PositionGetString(POSITION_SYMBOL);
            long magic=PositionGetInteger(POSITION_MAGIC);
            if(sym==_Symbol && magic==Magic){
               int type = MT5PosTypeToMT4((int)PositionGetInteger(POSITION_TYPE));
               double sl  = NormalizeDouble(PositionGetDouble(POSITION_SL),_Digits);
               double oop = NormalizeDouble(PositionGetDouble(POSITION_PRICE_OPEN),_Digits);

               double newSL=sl;
               if(type==OP_BUY){
                  double level = f0_833(1, Bid, Trailing_type);
                  if(level>=ld_180 + Minimum_trailing_profit*_Point && level>sl + Trailing_step*_Point && (Bid - level)/_Point > gi_304){
                     newSL=level;
                  }
                  if(newSL>sl){
                     ModifyPositionByTicket(ticket, newSL, PositionGetDouble(POSITION_TP));
                  }
               }
               if(type==OP_SELL){
                  double level = f0_833(-1, Ask, Trailing_type);
                  if( (level<=ld_188 - Minimum_trailing_profit*_Point && level<sl - Trailing_step*_Point) ||
                      (sl==0.0 && (level - Ask)/_Point > gi_304) ){
                     newSL=level;
                  }
                  if(newSL<sl || (sl==0.0 && newSL!=0.0)){
                     ModifyPositionByTicket(ticket, newSL, PositionGetDouble(POSITION_TP));
                  }
               }
            }
         }
      }
   }

   // Auto lucro por direção
   if(Auto_calculated_profit==0){
      ld_208 = Profit_for_closing_1_direction;
      ld_216 = Profit_for_closing_1_direction;
   }else{
      ld_208 = (ld_32==0.0 ? Order_lotsize : ld_32) * Auto_calculated_profit * gd_296;
      ld_216 = (ld_40==0.0 ? Order_lotsize : ld_40) * Auto_calculated_profit * gd_296;
      f0_12282("StopProfit1", StringFormat("Auto Profit closing Buy %.2f",ld_208),5,0,Color_information);
      f0_12282("StopProfit2", StringFormat("Auto Profit closing Sell %.2f",ld_216),5,0,Color_information);
   }

   // Lógica de fechamento
   if(ld_24 > Close_loss_by_drawdown_neg && ld_16 > Close_loss_by_drawdown_neg){
      ObjectSetString(0,"Char.op",OBJPROP_TEXT,(string)(char)251);
      ObjectSetInteger(0,"Char.op",OBJPROP_FONTSIZE,Font_size+2);
      if(ld_24 >= ld_208){
         Print("Closure of Buy on Profit ",ld_24);
         f0_1880(1);
         return;
      }
      if(ld_16 >= ld_216){
         Print("Closure of Sell on Profit ",ld_16);
         f0_1880(-1);
         return;
      }
   }else{
      ObjectSetString(0,"Char.op",OBJPROP_TEXT,(string)(char)74);
      ObjectSetInteger(0,"Char.op",OBJPROP_FONTSIZE,Font_size+2);
      ObjectSetInteger(0,"Char.op",OBJPROP_COLOR,clrRed);
      if(ld_24 + ld_16 >= Profit_for_closing_2_directions){
         Print("Closing all orders in 2 directions ", ld_24+ld_16);
         f0_1880(0);
         return;
      }
   }
   if(ld_24 <= Loss_for_closing_neg){
      Print("Closure of Buy on Loss ", ld_24);
      f0_1880(1);
      return;
   }
   if(ld_16 <= Loss_for_closing_neg){
      Print("Closure of Sell on Loss ", ld_16);
      f0_1880(-1);
      return;
   }

   // Sinalizações de permissão
   if(ld_24 <= Maximum_allowed_loss_neg){
      Comment("Do not open the Buy");
      ObjectSetString(0,"Char.b",OBJPROP_TEXT,(string)(char)225 + (string)(char)251);
      ObjectSetInteger(0,"Char.b",OBJPROP_COLOR,clrRed);
   }else{
      ObjectSetString(0,"Char.b",OBJPROP_TEXT,(string)(char)233);
      ObjectSetInteger(0,"Char.b",OBJPROP_COLOR,clrLime);
   }
   if(ld_16 <= Maximum_allowed_loss_neg){
      Comment("Do not open Sell");
      ObjectSetString(0,"Char.s",OBJPROP_TEXT,(string)(char)226 + (string)(char)251);
      ObjectSetInteger(0,"Char.s",OBJPROP_COLOR,clrRed);
   }else{
      ObjectSetString(0,"Char.s",OBJPROP_TEXT,(string)(char)234);
      ObjectSetInteger(0,"Char.s",OBJPROP_COLOR,clrLime);
   }

   // RSI se necessário
   if(li_88==0 || li_92==0){
      ENUM_TIMEFRAMES rtf = TFfromMinutes(Timeframe_indicator==0 ? (int)Period() : Timeframe_indicator);
      ld_240 = GetRSI(_Symbol, rtf, RSI_Period, PRICE_CLOSE, 0);
   }

   // Abrir BUYSTOP
   if(li_96==0 && ld_24>Maximum_allowed_loss_neg && Allow_BUY){
      if(li_88==0){
         if((ld_240 < Oversold_zone || (!Opening_1_order_on_indicators))) ld_224 = NormalizeDouble(Ask + first_step*_Point,_Digits);
         else ld_224 = 0;
      }else{
         ld_224 = NormalizeDouble(Ask + Minimum_price_distance*_Point,_Digits);
         if(ld_224 < NormalizeDouble(ld_124 - dist_between*_Point,_Digits))
            ld_224 = NormalizeDouble(Ask + dist_between*_Point,_Digits);
      }
      if(ld_224 != 0.0 && (li_88==0 || (ld_116!=0.0 && ld_224>=NormalizeDouble(ld_116 + dist_between*_Point,_Digits) && Open_order_on_trend) || 
      (ld_124!=0.0 && ld_224<=NormalizeDouble(ld_124 - dist_between*_Point,_Digits)))){
         if(li_88==0) ld_232 = Order_lotsize;
         else ld_232 = NormalizeDouble(Order_lotsize*MathPow(Multiply_lotsize_by, li_88) + li_88*Increase_lotsize_by, Round_lotsize_to_decimals);

         double margin=0;
         if(OrderCalcMargin(ORDER_TYPE_BUY, _Symbol, ld_232, Ask, margin) && margin > 0){
            if( ((margin <= AccountInfoDouble(ACCOUNT_MARGIN_FREE) && li_88>0) || EA_makes_first_order) ){
               if(TradingHours()){
                  if(!SendPending(ORDER_TYPE_BUY_STOP, ld_232, ld_224, gi_316, "cm_EA_TSO", Magic))
                     PrintFormat("Impossible to place a BUYSTOP order with Lot %.2f Price %.5f Ask %.5f",ld_232,ld_224,Ask);
               }else{
                  Comment("BUYSTOP order stopped since time is outside of trading hours!");
               }
            }else Comment(StringFormat("Impossible to set Lot %.2f",ld_232));
         }
      }
   }

   // Abrir SELLSTOP
   if(li_100==0 && ld_16>Maximum_allowed_loss_neg && Allow_SELL){
      if(li_92==0){
         if((ld_240 > Overbought_zone || (!Opening_1_order_on_indicators))) ld_224 = NormalizeDouble(Bid - first_step*_Point,_Digits);
         else ld_224 = 0;
      }else{
         ld_224 = NormalizeDouble(Bid - Minimum_price_distance*_Point,_Digits);
         if(ld_224 < NormalizeDouble(ld_132 + dist_between*_Point,_Digits))
            ld_224 = NormalizeDouble(Bid - dist_between*_Point,_Digits);
      }
      if(ld_224 != 0.0 && (li_92==0 || (ld_140!=0.0 && ld_224<=NormalizeDouble(ld_140 - dist_between*_Point,_Digits) && Open_order_on_trend) ||
      (ld_132!=0.0 && ld_224>=NormalizeDouble(ld_132 + dist_between*_Point,_Digits)))){
         if(li_92==0) ld_232 = Order_lotsize;
         else ld_232 = NormalizeDouble(Order_lotsize*MathPow(Multiply_lotsize_by, li_92) + li_92*Increase_lotsize_by, Round_lotsize_to_decimals);

         double margin=0;
         if(OrderCalcMargin(ORDER_TYPE_SELL, _Symbol, ld_232, Bid, margin) && margin > 0){
            if( ((margin <= AccountInfoDouble(ACCOUNT_MARGIN_FREE) && li_92>0) || EA_makes_first_order) ){
               if(TradingHours()){
                  if(!SendPending(ORDER_TYPE_SELL_STOP, ld_232, ld_224, gi_316, "cm_EA_TSO", Magic))
                     PrintFormat("Impossible to place a SELLSTOP order with Lot %.2f Price %.5f Bid %.5f",ld_232,ld_224,Bid);
               }else{
                  Comment("SLLSTOP order stopped since time is outside of trading hours!");
               }
            }else Comment(StringFormat("Impossible to set Lot %.2f",ld_232));
         }
      }
   }

   // Infos
   ObjectSetString(0,"Balance",OBJPROP_TEXT, StringFormat("Balance %.2f",AccountInfoDouble(ACCOUNT_BALANCE)));
   ObjectSetInteger(0,"Balance",OBJPROP_COLOR,Color_information);
   ObjectSetInteger(0,"Balance",OBJPROP_FONTSIZE,Font_size);
   ObjectSetString(0,"Balance",OBJPROP_FONT,"Arial");

   ObjectSetString(0,"Equity",OBJPROP_TEXT, StringFormat("Equity %.2f",AccountInfoDouble(ACCOUNT_EQUITY)));
   ObjectSetInteger(0,"Equity",OBJPROP_COLOR,Color_information);
   ObjectSetInteger(0,"Equity",OBJPROP_FONTSIZE,Font_size);
   ObjectSetString(0,"Equity",OBJPROP_FONT,"Arial");

   ObjectSetString(0,"FreeMargin",OBJPROP_TEXT, StringFormat("Free Margin %.2f",AccountInfoDouble(ACCOUNT_MARGIN_FREE)));
   ObjectSetInteger(0,"FreeMargin",OBJPROP_COLOR,Color_information);
   ObjectSetInteger(0,"FreeMargin",OBJPROP_FONTSIZE,Font_size);
   ObjectSetString(0,"FreeMargin",OBJPROP_FONT,"Arial");

   double ld_8 = ld_24 + ld_16;
   if(ld_32>0.0){
      ObjectSetString(0,"ProfitB",OBJPROP_TEXT, StringFormat("Buy %d   Profit %.2f  Lot = %.2f",li_88,ld_24,ld_32));
      ObjectSetInteger(0,"ProfitB",OBJPROP_COLOR, (color)f0_11449(ld_24>0.0,65280,255));
      ObjectSetInteger(0,"ProfitB",OBJPROP_FONTSIZE,Font_size);
      ObjectSetString(0,"ProfitB",OBJPROP_FONT,"Arial");
   }else{
      ObjectSetString(0,"ProfitB",OBJPROP_TEXT,"");
      ObjectSetInteger(0,"ProfitB",OBJPROP_COLOR,clrGray);
   }
   if(ld_40>0.0){
      ObjectSetString(0,"ProfitS",OBJPROP_TEXT, StringFormat("Sell %d   Profit %.2f  Lot = %.2f",li_92,ld_16,ld_40));
      ObjectSetInteger(0,"ProfitS",OBJPROP_COLOR, (color)f0_11449(ld_16>0.0,65280,255));
      ObjectSetInteger(0,"ProfitS",OBJPROP_FONTSIZE,Font_size);
      ObjectSetString(0,"ProfitS",OBJPROP_FONT,"Arial");
   }else{
      ObjectSetString(0,"ProfitS",OBJPROP_TEXT,"");
      ObjectSetInteger(0,"ProfitS",OBJPROP_COLOR,clrGray);
   }
   if(ld_40+ld_32>0.0){
      ObjectSetString(0,"Profit",OBJPROP_TEXT, StringFormat("Profit All %.2f",ld_8));
      ObjectSetInteger(0,"Profit",OBJPROP_COLOR, (color)f0_11449(ld_8>=0.0,32768,255));
      ObjectSetInteger(0,"Profit",OBJPROP_FONTSIZE,Font_size);
      ObjectSetString(0,"Profit",OBJPROP_FONT,"Arial");
   }else{
      ObjectSetString(0,"Profit",OBJPROP_TEXT,"");
      ObjectSetInteger(0,"Profit",OBJPROP_COLOR,clrGray);
   }

   // Move pendentes conforme lógica
   if(ld_148!=0.0 && Allow_BUY && li_108>0){
      if(li_88==0) ld_224 = NormalizeDouble(Ask + first_step*_Point,_Digits);
      else         ld_224 = NormalizeDouble(Ask + Minimum_price_distance*_Point,_Digits);
      if( NormalizeDouble(ld_148 - Move_step*_Point,_Digits) > ld_224 &&
          (ld_224 <= NormalizeDouble(ld_124 - dist_between*_Point,_Digits) || ld_124==0.0 ||
          (Open_order_on_trend && li_88==0) ||
          ld_224 >= NormalizeDouble(ld_116 + dist_between*_Point,_Digits) ||
          ld_224 <= NormalizeDouble(ld_124 - dist_between*_Point,_Digits)) ){
         if(!ModifyPendingOrderPrice(li_108, ld_224))
            PrintFormat("Error Order Modify Buy   OOP %.5f -> %.5f",ld_148,ld_224);
         else
            PrintFormat("Order Buy Modify OOP %.5f -> %.5f",ld_148,ld_224);
      }
   }
   if(ld_156!=0.0 && Allow_SELL && li_112>0){
      if(li_92==0) ld_224 = NormalizeDouble(Bid - first_step*_Point,_Digits);
      else         ld_224 = NormalizeDouble(Bid - Minimum_price_distance*_Point,_Digits);
      if( NormalizeDouble(ld_156 + Move_step*_Point,_Digits) < ld_224 &&
          (ld_224 >= NormalizeDouble(ld_132 + dist_between*_Point,_Digits) || ld_132==0.0 ||
          (Open_order_on_trend && li_92==0) ||
          ld_224 <= NormalizeDouble(ld_140 - dist_between*_Point,_Digits) ||
          ld_224 >= NormalizeDouble(ld_132 + dist_between*_Point,_Digits)) ){
         if(!ModifyPendingOrderPrice(li_112, ld_224))
            PrintFormat("Error Order Modify Sell  OOP %.5f -> %.5f",ld_156,ld_224);
         else
            PrintFormat("Order Sell Modify OOP %.5f -> %.5f",ld_156,ld_224);
      }
   }
}