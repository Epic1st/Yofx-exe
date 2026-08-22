#property copyright  "decompiled by Jamo886"
#property version    "1.85"
#property strict
#property description "decompiled by Jamo886"

#define OP_BUY       0
#define OP_SELL      1
#define OP_BUYLIMIT  2
#define OP_SELLLIMIT 3
#define OP_BUYSTOP   4
#define OP_SELLSTOP  5

#define SELECT_BY_POS    0
#define SELECT_BY_TICKET 1
#define MODE_TRADES      0
#define MODE_HISTORY     1

#define MODE_BID          9
#define MODE_ASK          10
#define MODE_POINT        11
#define MODE_DIGITS       12
#define MODE_SPREAD       13
#define MODE_STOPLEVEL    14
#define MODE_LOTSIZE      15
#define MODE_TICKVALUE    16
#define MODE_TICKSIZE     17
#define MODE_SWAPLONG     18
#define MODE_SWAPSHORT    19
#define MODE_STARTING     20
#define MODE_EXPIRATION   21
#define MODE_TRADEALLOWED 22
#define MODE_MINLOT       23
#define MODE_LOTSTEP      24
#define MODE_MAXLOT       25
#define MODE_FREEZELEVEL  33

struct MQL4SelectedOrder
{
   bool     valid;
   bool     is_position;
   ulong    ticket;
   string   symbol;
   long     magic;
   int      type;
   double   lots;
   double   open_price;
   double   close_price;
   double   sl;
   double   tp;
   datetime open_time;
   datetime close_time;
   datetime expiration;
   string   comment;
   double   profit;
   double   swap;
   double   commission;
};

MQL4SelectedOrder __mql4_sel;
int __lizard_magic_base=8000;

bool __LizardManagedMagicEarly(const long magic)
{
   return (magic==__lizard_magic_base+8  ||
           magic==__lizard_magic_base+9  ||
           magic==__lizard_magic_base+12 ||
           magic==__lizard_magic_base+13 ||
           magic==__lizard_magic_base+14 ||
           magic==__lizard_magic_base+15);
}

int __Mql4TypeFromOrderType(ENUM_ORDER_TYPE t)
{
   if(t==ORDER_TYPE_BUY_LIMIT)  return OP_BUYLIMIT;
   if(t==ORDER_TYPE_SELL_LIMIT) return OP_SELLLIMIT;
   if(t==ORDER_TYPE_BUY_STOP)   return OP_BUYSTOP;
   if(t==ORDER_TYPE_SELL_STOP)  return OP_SELLSTOP;
   if(t==ORDER_TYPE_BUY_STOP_LIMIT)  return OP_BUYSTOP;
   if(t==ORDER_TYPE_SELL_STOP_LIMIT) return OP_SELLSTOP;
   return -1;
}

ENUM_ORDER_TYPE __Mql5OrderTypeFromMql4(int cmd)
{
   if(cmd==OP_BUY)       return ORDER_TYPE_BUY;
   if(cmd==OP_SELL)      return ORDER_TYPE_SELL;
   if(cmd==OP_BUYLIMIT)  return ORDER_TYPE_BUY_LIMIT;
   if(cmd==OP_SELLLIMIT) return ORDER_TYPE_SELL_LIMIT;
   if(cmd==OP_BUYSTOP)   return ORDER_TYPE_BUY_STOP;
   if(cmd==OP_SELLSTOP)  return ORDER_TYPE_SELL_STOP;
   return ORDER_TYPE_BUY;
}

bool RefreshRates(){ return true; }
double MQL4_Bid(){ MqlTick t; SymbolInfoTick(_Symbol,t); return t.bid; }
double MQL4_Ask(){ MqlTick t; SymbolInfoTick(_Symbol,t); return t.ask; }

#define Bid MQL4_Bid()
#define Ask MQL4_Ask()
#define Point _Point
#define Digits _Digits

double AccountBalance(){ return AccountInfoDouble(ACCOUNT_BALANCE); }
double AccountEquity(){ return AccountInfoDouble(ACCOUNT_EQUITY); }
double AccountFreeMargin(){ return AccountInfoDouble(ACCOUNT_MARGIN_FREE); }
string AccountCurrency(){ return AccountInfoString(ACCOUNT_CURRENCY); }

double AccountFreeMarginCheck(string symbol,int cmd,double volume)
{
   ENUM_ORDER_TYPE type=(cmd==OP_SELL ? ORDER_TYPE_SELL : ORDER_TYPE_BUY);
   double price=(cmd==OP_SELL ? SymbolInfoDouble(symbol,SYMBOL_BID) : SymbolInfoDouble(symbol,SYMBOL_ASK));
   double margin=0.0;
   if(!OrderCalcMargin(type,symbol,volume,price,margin)) return -1.0;
   return AccountInfoDouble(ACCOUNT_MARGIN_FREE)-margin;
}

bool IsTesting(){ return (bool)MQLInfoInteger(MQL_TESTER); }
bool IsDemo(){ return (AccountInfoInteger(ACCOUNT_TRADE_MODE)==ACCOUNT_TRADE_MODE_DEMO); }

ENUM_TIMEFRAMES __Mql4Timeframe(int timeframe)
{
   switch(timeframe)
   {
      case 0:     return PERIOD_CURRENT;
      case 1:     return PERIOD_M1;
      case 2:     return PERIOD_M2;
      case 3:     return PERIOD_M3;
      case 4:     return PERIOD_M4;
      case 5:     return PERIOD_M5;
      case 6:     return PERIOD_M6;
      case 10:    return PERIOD_M10;
      case 12:    return PERIOD_M12;
      case 15:    return PERIOD_M15;
      case 20:    return PERIOD_M20;
      case 30:    return PERIOD_M30;
      case 60:    return PERIOD_H1;
      case 120:   return PERIOD_H2;
      case 180:   return PERIOD_H3;
      case 240:   return PERIOD_H4;
      case 360:   return PERIOD_H6;
      case 480:   return PERIOD_H8;
      case 720:   return PERIOD_H12;
      case 1440:  return PERIOD_D1;
      case 10080: return PERIOD_W1;
      case 43200: return PERIOD_MN1;
   }
   if(timeframe>=PERIOD_H1) return (ENUM_TIMEFRAMES)timeframe;
   return PERIOD_CURRENT;
}

int __Mql4Bars()
{
   return iBars(_Symbol,PERIOD_CURRENT);
}
#define Bars __Mql4Bars()

string MQL4_StringConcatenate(int first,string second)
{
   return IntegerToString(first)+second;
}
#define StringConcatenate MQL4_StringConcatenate

int __DatePart(datetime value,int part)
{
   MqlDateTime dt;
   TimeToStruct(value,dt);
   if(part==0) return dt.year;
   if(part==1) return dt.mon;
   if(part==2) return dt.day;
   if(part==3) return dt.day_of_week;
   if(part==4) return dt.hour;
   if(part==5) return dt.min;
   if(part==6) return dt.sec;
   if(part==7) return dt.day_of_year;
   return 0;
}

int Year(){ return __DatePart(TimeCurrent(),0); }
int Month(){ return __DatePart(TimeCurrent(),1); }
int Day(){ return __DatePart(TimeCurrent(),2); }
int DayOfWeek(){ return __DatePart(TimeCurrent(),3); }
int Hour(){ return __DatePart(TimeCurrent(),4); }
int Minute(){ return __DatePart(TimeCurrent(),5); }
int Seconds(){ return __DatePart(TimeCurrent(),6); }
int TimeYear(datetime value){ return __DatePart(value,0); }
int TimeMonth(datetime value){ return __DatePart(value,1); }
int TimeDay(datetime value){ return __DatePart(value,2); }
int TimeDayOfWeek(datetime value){ return __DatePart(value,3); }
int TimeHour(datetime value){ return __DatePart(value,4); }
int TimeMinute(datetime value){ return __DatePart(value,5); }
int TimeSeconds(datetime value){ return __DatePart(value,6); }
int TimeDayOfYear(datetime value){ return __DatePart(value,7); }

int OrdersTotalMQL4Compat(){ return (int)(PositionsTotal()+OrdersTotal()); }
#define OrdersTotal OrdersTotalMQL4Compat

double MarketInfo(string symbol,int mode)
{
   MqlTick tick;
   SymbolInfoTick(symbol,tick);
   switch(mode)
   {
      case MODE_BID: return tick.bid;
      case MODE_ASK: return tick.ask;
      case MODE_POINT: return SymbolInfoDouble(symbol,SYMBOL_POINT);
      case MODE_DIGITS: return (double)SymbolInfoInteger(symbol,SYMBOL_DIGITS);
      case MODE_SPREAD: return (double)SymbolInfoInteger(symbol,SYMBOL_SPREAD);
      case MODE_STOPLEVEL: return (double)SymbolInfoInteger(symbol,SYMBOL_TRADE_STOPS_LEVEL);
      case MODE_FREEZELEVEL: return (double)SymbolInfoInteger(symbol,SYMBOL_TRADE_FREEZE_LEVEL);
      case MODE_TICKVALUE: return SymbolInfoDouble(symbol,SYMBOL_TRADE_TICK_VALUE);
      case MODE_TICKSIZE: return SymbolInfoDouble(symbol,SYMBOL_TRADE_TICK_SIZE);
      case MODE_MINLOT: return SymbolInfoDouble(symbol,SYMBOL_VOLUME_MIN);
      case MODE_LOTSTEP: return SymbolInfoDouble(symbol,SYMBOL_VOLUME_STEP);
      case MODE_MAXLOT: return SymbolInfoDouble(symbol,SYMBOL_VOLUME_MAX);
      case MODE_TRADEALLOWED: return (double)(SymbolInfoInteger(symbol,SYMBOL_TRADE_MODE)!=SYMBOL_TRADE_MODE_DISABLED);
   }
   return 0.0;
}

bool __SelectPositionByIndex(int index)
{
   if(index<0 || index>=PositionsTotal()) return false;
   ulong ticket=PositionGetTicket(index);
   if(ticket==0 || !PositionSelectByTicket(ticket)) return false;
   __mql4_sel.valid=true; __mql4_sel.is_position=true; __mql4_sel.ticket=ticket;
   __mql4_sel.symbol=PositionGetString(POSITION_SYMBOL);
   __mql4_sel.magic=PositionGetInteger(POSITION_MAGIC);
   long ptype=PositionGetInteger(POSITION_TYPE);
   __mql4_sel.type=(ptype==POSITION_TYPE_BUY ? OP_BUY : OP_SELL);
   __mql4_sel.lots=PositionGetDouble(POSITION_VOLUME);
   __mql4_sel.open_price=PositionGetDouble(POSITION_PRICE_OPEN);
   __mql4_sel.close_price=PositionGetDouble(POSITION_PRICE_CURRENT);
   __mql4_sel.sl=PositionGetDouble(POSITION_SL);
   __mql4_sel.tp=PositionGetDouble(POSITION_TP);
   __mql4_sel.open_time=(datetime)PositionGetInteger(POSITION_TIME);
   __mql4_sel.close_time=0;
   __mql4_sel.expiration=0;
   __mql4_sel.comment=PositionGetString(POSITION_COMMENT);
   __mql4_sel.profit=PositionGetDouble(POSITION_PROFIT);
   __mql4_sel.swap=PositionGetDouble(POSITION_SWAP);
   __mql4_sel.commission=0.0;
   return true;
}

bool __SelectOrderByIndex(int index)
{
   if(index<0 || index>=OrdersTotal()) return false;
   ulong ticket=OrderGetTicket(index);
   if(ticket==0 || !OrderSelect(ticket)) return false;
   __mql4_sel.valid=true; __mql4_sel.is_position=false; __mql4_sel.ticket=ticket;
   __mql4_sel.symbol=OrderGetString(ORDER_SYMBOL);
   __mql4_sel.magic=OrderGetInteger(ORDER_MAGIC);
   __mql4_sel.type=__Mql4TypeFromOrderType((ENUM_ORDER_TYPE)OrderGetInteger(ORDER_TYPE));
   __mql4_sel.lots=OrderGetDouble(ORDER_VOLUME_CURRENT);
   __mql4_sel.open_price=OrderGetDouble(ORDER_PRICE_OPEN);
   __mql4_sel.close_price=0.0;
   __mql4_sel.sl=OrderGetDouble(ORDER_SL);
   __mql4_sel.tp=OrderGetDouble(ORDER_TP);
   __mql4_sel.open_time=(datetime)OrderGetInteger(ORDER_TIME_SETUP);
   __mql4_sel.close_time=0;
   __mql4_sel.expiration=(datetime)OrderGetInteger(ORDER_TIME_EXPIRATION);
   __mql4_sel.comment=OrderGetString(ORDER_COMMENT);
   __mql4_sel.profit=0.0; __mql4_sel.swap=0.0; __mql4_sel.commission=0.0;
   return true;
}

bool __SelectHistoryDealByTicket(ulong ticket)
{
   if(ticket==0 || !HistoryDealSelect(ticket)) return false;
   long dtype=HistoryDealGetInteger(ticket,DEAL_TYPE);
   if(dtype!=DEAL_TYPE_BUY && dtype!=DEAL_TYPE_SELL) return false;
   __mql4_sel.valid=true; __mql4_sel.is_position=false; __mql4_sel.ticket=ticket;
   __mql4_sel.symbol=HistoryDealGetString(ticket,DEAL_SYMBOL);
   __mql4_sel.magic=HistoryDealGetInteger(ticket,DEAL_MAGIC);
   __mql4_sel.type=(dtype==DEAL_TYPE_BUY ? OP_BUY : OP_SELL);
   __mql4_sel.lots=HistoryDealGetDouble(ticket,DEAL_VOLUME);
   __mql4_sel.open_price=HistoryDealGetDouble(ticket,DEAL_PRICE);
   __mql4_sel.close_price=HistoryDealGetDouble(ticket,DEAL_PRICE);
   __mql4_sel.sl=0.0;
   __mql4_sel.tp=0.0;
   __mql4_sel.open_time=(datetime)HistoryDealGetInteger(ticket,DEAL_TIME);
   __mql4_sel.close_time=(datetime)HistoryDealGetInteger(ticket,DEAL_TIME);
   __mql4_sel.expiration=0;
   __mql4_sel.comment=HistoryDealGetString(ticket,DEAL_COMMENT);
   __mql4_sel.profit=HistoryDealGetDouble(ticket,DEAL_PROFIT);
   __mql4_sel.swap=HistoryDealGetDouble(ticket,DEAL_SWAP);
   __mql4_sel.commission=HistoryDealGetDouble(ticket,DEAL_COMMISSION);
   return true;
}

int HistoryTotal()
{
   HistorySelect(0,TimeCurrent()+86400);
   return (int)HistoryDealsTotal();
}

bool __SelectHistoryDealByIndex(int index)
{
   HistorySelect(0,TimeCurrent()+86400);
   int total=(int)HistoryDealsTotal();
   if(index<0 || index>=total) return false;
   ulong ticket=HistoryDealGetTicket(index);
   return __SelectHistoryDealByTicket(ticket);
}

bool MQL4_OrderSelect(long index_or_ticket,int select,int pool=MODE_TRADES)
{
   __mql4_sel.valid=false;
   if(select==SELECT_BY_POS)
   {
      if(pool==MODE_TRADES)
      {
         int pc=PositionsTotal();
         if(index_or_ticket<pc) return __SelectPositionByIndex((int)index_or_ticket);
         return __SelectOrderByIndex((int)(index_or_ticket-pc));
      }
      if(pool==MODE_HISTORY) return __SelectHistoryDealByIndex((int)index_or_ticket);
      return false;
   }
   ulong ticket=(ulong)index_or_ticket;
   for(int i=0;i<PositionsTotal();i++)
      if(PositionGetTicket(i)==ticket) return __SelectPositionByIndex(i);
   if(OrderSelect(ticket))
   {
      __mql4_sel.valid=true; __mql4_sel.is_position=false; __mql4_sel.ticket=ticket;
      __mql4_sel.symbol=OrderGetString(ORDER_SYMBOL);
      __mql4_sel.magic=OrderGetInteger(ORDER_MAGIC);
      __mql4_sel.type=__Mql4TypeFromOrderType((ENUM_ORDER_TYPE)OrderGetInteger(ORDER_TYPE));
      __mql4_sel.lots=OrderGetDouble(ORDER_VOLUME_CURRENT);
      __mql4_sel.open_price=OrderGetDouble(ORDER_PRICE_OPEN);
      __mql4_sel.close_price=0.0;
      __mql4_sel.sl=OrderGetDouble(ORDER_SL); __mql4_sel.tp=OrderGetDouble(ORDER_TP);
      __mql4_sel.open_time=(datetime)OrderGetInteger(ORDER_TIME_SETUP);
      __mql4_sel.close_time=0;
      __mql4_sel.expiration=(datetime)OrderGetInteger(ORDER_TIME_EXPIRATION);
      __mql4_sel.comment=OrderGetString(ORDER_COMMENT);
      __mql4_sel.profit=0.0; __mql4_sel.swap=0.0; __mql4_sel.commission=0.0;
      return true;
   }
   if(__SelectHistoryDealByTicket(ticket)) return true;
   return false;
}
#define OrderSelect MQL4_OrderSelect

int OrderType(){ return __mql4_sel.type; }
long OrderTicket(){ return (long)__mql4_sel.ticket; }
double OrderLots(){ return __mql4_sel.lots; }
double OrderOpenPrice(){ return __mql4_sel.open_price; }
double OrderClosePrice(){ return __mql4_sel.close_price; }
double OrderStopLoss(){ return __mql4_sel.sl; }
double OrderTakeProfit(){ return __mql4_sel.tp; }
int OrderMagicNumber(){ return (int)__mql4_sel.magic; }
string OrderSymbol(){ return __mql4_sel.symbol; }
string OrderComment(){ return __mql4_sel.comment; }
datetime OrderOpenTime(){ return __mql4_sel.open_time; }
datetime OrderCloseTime(){ return __mql4_sel.close_time; }
datetime OrderExpiration(){ return __mql4_sel.expiration; }
double OrderProfit(){ return __mql4_sel.profit; }
double OrderSwap(){ return __mql4_sel.swap; }
double OrderCommission(){ return __mql4_sel.commission; }

bool __Mql5OrderSendRaw(MqlTradeRequest &req,MqlTradeResult &res)
{
   return OrderSend(req,res);
}

ENUM_ORDER_TYPE_FILLING __ReaperBestFillingMode(string symbol)
{
   long filling=(long)SymbolInfoInteger(symbol,SYMBOL_FILLING_MODE);
   if((filling & SYMBOL_FILLING_IOC)==SYMBOL_FILLING_IOC) return ORDER_FILLING_IOC;
   if((filling & SYMBOL_FILLING_FOK)==SYMBOL_FILLING_FOK) return ORDER_FILLING_FOK;
   return ORDER_FILLING_RETURN;
}

string __ReaperTradeRetcodeText(uint code)
{
   switch(code)
   {
      case TRADE_RETCODE_REQUOTE: return "requote";
      case TRADE_RETCODE_REJECT: return "request rejected";
      case TRADE_RETCODE_CANCEL: return "request canceled";
      case TRADE_RETCODE_PLACED: return "order placed";
      case TRADE_RETCODE_DONE: return "done";
      case TRADE_RETCODE_DONE_PARTIAL: return "partially done";
      case TRADE_RETCODE_ERROR: return "common trade error";
      case TRADE_RETCODE_TIMEOUT: return "trade timeout";
      case TRADE_RETCODE_INVALID: return "invalid request";
      case TRADE_RETCODE_INVALID_VOLUME: return "invalid volume";
      case TRADE_RETCODE_INVALID_PRICE: return "invalid price";
      case TRADE_RETCODE_INVALID_STOPS: return "invalid stops";
      case TRADE_RETCODE_TRADE_DISABLED: return "trade disabled for symbol/account";
      case TRADE_RETCODE_MARKET_CLOSED: return "market closed";
      case TRADE_RETCODE_NO_MONEY: return "not enough money";
      case TRADE_RETCODE_PRICE_CHANGED: return "price changed";
      case TRADE_RETCODE_PRICE_OFF: return "no price";
      case TRADE_RETCODE_INVALID_EXPIRATION: return "invalid expiration";
      case TRADE_RETCODE_TOO_MANY_REQUESTS: return "too many requests";
      case TRADE_RETCODE_NO_CHANGES: return "no changes";
      case TRADE_RETCODE_SERVER_DISABLES_AT: return "autotrading disabled by server";
      case TRADE_RETCODE_CLIENT_DISABLES_AT: return "autotrading disabled in terminal";
      case TRADE_RETCODE_LOCKED: return "trade context locked";
      case TRADE_RETCODE_FROZEN: return "order/position frozen";
      case TRADE_RETCODE_INVALID_FILL: return "invalid filling mode";
      case TRADE_RETCODE_CONNECTION: return "no trade connection";
      case TRADE_RETCODE_LIMIT_ORDERS: return "too many pending orders";
      case TRADE_RETCODE_LIMIT_VOLUME: return "volume limit reached";
      case TRADE_RETCODE_INVALID_ORDER: return "invalid order type";
   }
   return "unknown trade retcode";
}

void __ReaperPrintTradeReject(string context,const MqlTradeRequest &req,const MqlTradeResult &res,int last_error)
{
   Print("Lizard trade ",context,
         " | retcode=",res.retcode," (",__ReaperTradeRetcodeText(res.retcode),")",
         " | last_error=",last_error,
         " | symbol=",req.symbol,
         " | action=",req.action,
         " | type=",req.type,
         " | filling=",req.type_filling,
         " | volume=",DoubleToString(req.volume,2),
         " | price=",DoubleToString(req.price,(int)SymbolInfoInteger(req.symbol,SYMBOL_DIGITS)),
         " | sl=",DoubleToString(req.sl,(int)SymbolInfoInteger(req.symbol,SYMBOL_DIGITS)),
         " | tp=",DoubleToString(req.tp,(int)SymbolInfoInteger(req.symbol,SYMBOL_DIGITS)));
}

void __ReaperCheckTradePermissions(string stage)
{
   if(!TerminalInfoInteger(TERMINAL_TRADE_ALLOWED))
      Print("Lizard ",stage,": automated trading is disabled in the MT5 terminal. Enable the Algo Trading button.");
   if(!MQLInfoInteger(MQL_TRADE_ALLOWED))
      Print("Lizard ",stage,": trading is not allowed for this EA. Check the Common tab and enable Allow Algo Trading.");
   if(!AccountInfoInteger(ACCOUNT_TRADE_ALLOWED))
      Print("Lizard ",stage,": trading is not allowed for this account.");
   if(!AccountInfoInteger(ACCOUNT_TRADE_EXPERT))
      Print("Lizard ",stage,": expert advisor trading is disabled for this account/server.");
}

ulong __FindNewestPositionTicket(string symbol,int magic,int cmd,string comment)
{
   ulong best_ticket=0;
   datetime best_time=0;
   long wanted_type=(cmd==OP_BUY ? POSITION_TYPE_BUY : POSITION_TYPE_SELL);
   for(int i=PositionsTotal()-1;i>=0;i--)
   {
      ulong ticket=PositionGetTicket(i);
      if(ticket==0 || !PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL)!=symbol) continue;
      if(PositionGetInteger(POSITION_MAGIC)!=magic) continue;
      if(PositionGetInteger(POSITION_TYPE)!=wanted_type) continue;
      datetime opened=(datetime)PositionGetInteger(POSITION_TIME);
      if(opened>=best_time)
      {
         best_time=opened;
         best_ticket=ticket;
      }
   }
   return best_ticket;
}

long MQL4_OrderSend(string symbol,int cmd,double volume,double price,int slippage,double sl,double tp,string comment=NULL,int magic=0,datetime expiration=0,color arrow_color=clrNONE)
{
   MqlTradeRequest req; MqlTradeResult res; ZeroMemory(req); ZeroMemory(res);
   req.symbol=symbol; req.volume=volume; req.magic=magic; req.comment=comment; req.sl=sl; req.tp=tp; req.deviation=(uint)slippage;
   ENUM_ORDER_TYPE type=__Mql5OrderTypeFromMql4(cmd);
   req.type=type;
   if(cmd==OP_BUY || cmd==OP_SELL)
   {
      req.action=TRADE_ACTION_DEAL;
      req.price=(cmd==OP_BUY ? SymbolInfoDouble(symbol,SYMBOL_ASK) : SymbolInfoDouble(symbol,SYMBOL_BID));
      req.type_filling=__ReaperBestFillingMode(symbol);
   }
   else
   {
      req.action=TRADE_ACTION_PENDING;
      req.price=price;
      if(__LizardManagedMagicEarly(magic)) req.tp=0.0;
      if(expiration>0){ req.type_time=ORDER_TIME_SPECIFIED; req.expiration=expiration; }
      else req.type_time=ORDER_TIME_GTC;
      req.type_filling=ORDER_FILLING_RETURN;
   }
   if(!__Mql5OrderSendRaw(req,res)) { __ReaperPrintTradeReject("send failed",req,res,GetLastError()); return -1; }
   if(res.retcode!=TRADE_RETCODE_DONE && res.retcode!=TRADE_RETCODE_PLACED && res.retcode!=TRADE_RETCODE_DONE_PARTIAL)
   { __ReaperPrintTradeReject("send rejected",req,res,GetLastError()); return -1; }
   if(cmd==OP_BUY || cmd==OP_SELL)
   {
      ulong position_ticket=__FindNewestPositionTicket(symbol,magic,cmd,comment);
      if(position_ticket>0) return (long)position_ticket;
   }
   if(res.order>0) return (long)res.order;
   if(res.deal>0)  return (long)res.deal;
   return 0;
}
#define OrderSend MQL4_OrderSend

bool MQL4_OrderDelete(long ticket,color arrow_color=clrNONE)
{
   MqlTradeRequest req; MqlTradeResult res; ZeroMemory(req); ZeroMemory(res);
   req.action=TRADE_ACTION_REMOVE; req.order=(ulong)ticket;
   if(!__Mql5OrderSendRaw(req,res)) { Print("OrderDelete failed: ",GetLastError()," retcode=",res.retcode); return false; }
   return (res.retcode==TRADE_RETCODE_DONE || res.retcode==TRADE_RETCODE_PLACED);
}
#define OrderDelete MQL4_OrderDelete

bool MQL4_OrderClose(long ticket,double lots,double price,int slippage,color arrow_color=clrNONE)
{
   if(!PositionSelectByTicket((ulong)ticket)) return MQL4_OrderDelete(ticket,arrow_color);
   string symbol=PositionGetString(POSITION_SYMBOL);
   long ptype=PositionGetInteger(POSITION_TYPE);
   MqlTradeRequest req; MqlTradeResult res; ZeroMemory(req); ZeroMemory(res);
   req.action=TRADE_ACTION_DEAL; req.position=(ulong)ticket; req.symbol=symbol; req.volume=lots; req.deviation=(uint)slippage;
   req.type=(ptype==POSITION_TYPE_BUY ? ORDER_TYPE_SELL : ORDER_TYPE_BUY);
   req.price=(ptype==POSITION_TYPE_BUY ? SymbolInfoDouble(symbol,SYMBOL_BID) : SymbolInfoDouble(symbol,SYMBOL_ASK));
   req.type_filling=__ReaperBestFillingMode(symbol);
   if(!__Mql5OrderSendRaw(req,res)) { __ReaperPrintTradeReject("close failed",req,res,GetLastError()); return false; }
   if(res.retcode!=TRADE_RETCODE_DONE && res.retcode!=TRADE_RETCODE_DONE_PARTIAL)
   { __ReaperPrintTradeReject("close rejected",req,res,GetLastError()); return false; }
   return (res.retcode==TRADE_RETCODE_DONE || res.retcode==TRADE_RETCODE_DONE_PARTIAL);
}
#define OrderClose MQL4_OrderClose

bool MQL4_OrderModify(long ticket,double price,double sl,double tp,datetime expiration,color arrow_color=clrNONE)
{
   MqlTradeRequest req; MqlTradeResult res; ZeroMemory(req); ZeroMemory(res);
   ResetLastError();
   if(PositionSelectByTicket((ulong)ticket))
   {
       req.action=TRADE_ACTION_SLTP; req.position=(ulong)ticket; req.symbol=PositionGetString(POSITION_SYMBOL); req.sl=sl; req.tp=tp;
   }
    else
    {
        req.action=TRADE_ACTION_MODIFY; req.order=(ulong)ticket; req.price=price; req.sl=sl; req.tp=tp;
        if(__mql4_sel.valid && __LizardManagedMagicEarly(__mql4_sel.magic)) req.tp=0.0;
       if(__mql4_sel.valid && __mql4_sel.ticket==(ulong)ticket)
       {
          req.symbol=__mql4_sel.symbol;
          if(__mql4_sel.expiration>0){ req.type_time=ORDER_TIME_SPECIFIED; req.expiration=__mql4_sel.expiration; }
       }
       if(expiration>0){ req.type_time=ORDER_TIME_SPECIFIED; req.expiration=expiration; }
   }
   if(!__Mql5OrderSendRaw(req,res)) { __ReaperPrintTradeReject("modify failed",req,res,GetLastError()); return false; }
   bool ok=(res.retcode==TRADE_RETCODE_DONE || res.retcode==TRADE_RETCODE_PLACED || res.retcode==TRADE_RETCODE_NO_CHANGES);
   if(!ok) { __ReaperPrintTradeReject("modify rejected",req,res,GetLastError()); return false; }
   MQL4_OrderSelect(ticket,SELECT_BY_TICKET,MODE_TRADES);
   return true;
}
#define OrderModify MQL4_OrderModify

datetime MQL4_iTime(string symbol,int timeframe,int shift)
{
   return iTime(symbol,__Mql4Timeframe(timeframe),shift);
}
#define iTime MQL4_iTime

int MQL4_iBars(string symbol,int timeframe)
{
   return iBars(symbol,__Mql4Timeframe(timeframe));
}
#define iBars MQL4_iBars

double MQL4_iOpen(string symbol,int timeframe,int shift)
{
   return iOpen(symbol,__Mql4Timeframe(timeframe),shift);
}
#define iOpen MQL4_iOpen

double MQL4_iHigh(string symbol,int timeframe,int shift)
{
   return iHigh(symbol,__Mql4Timeframe(timeframe),shift);
}
#define iHigh MQL4_iHigh

double MQL4_iLow(string symbol,int timeframe,int shift)
{
   return iLow(symbol,__Mql4Timeframe(timeframe),shift);
}
#define iLow MQL4_iLow

double MQL4_iClose(string symbol,int timeframe,int shift)
{
   return iClose(symbol,__Mql4Timeframe(timeframe),shift);
}
#define iClose MQL4_iClose

double MQL4_iFractals(string symbol,int timeframe,int mode,int shift)
{
   int buffer=(mode==1 ? 0 : 1);
   int handle=iFractals(symbol,__Mql4Timeframe(timeframe));
   if(handle==INVALID_HANDLE) return 0.0;
   double values[];
   ArraySetAsSeries(values,true);
   if(CopyBuffer(handle,buffer,shift,1,values)<=0)
   {
      IndicatorRelease(handle);
      return 0.0;
   }
   IndicatorRelease(handle);
   return values[0];
}
#define iFractals MQL4_iFractals

double MQL4_iMA(string symbol,int timeframe,int period,int ma_shift,int ma_method,int applied_price,int shift)
{
   ENUM_TIMEFRAMES tf=__Mql4Timeframe(timeframe);
   int handle=iMA(symbol,tf,period,ma_shift,(ENUM_MA_METHOD)ma_method,(ENUM_APPLIED_PRICE)applied_price);
   if(handle==INVALID_HANDLE) return 0.0;
   double buf[]; ArraySetAsSeries(buf,true);
   if(CopyBuffer(handle,0,shift,1,buf)<=0){ IndicatorRelease(handle); return 0.0; }
   IndicatorRelease(handle);
   return buf[0];
}
#define iMA MQL4_iMA


  enum enum_TradeFrequency      {Extreme_cons_Frequency = 0,//extreme conservative
                   Conservative_Frequency = 1,//conservative
                   Moderate_Frequency = 2,//moderate
                   Intens_Frequency = 3,//Intense
                   Extreme_Frequency = 4,//Extreme (high risk!)
                   Auto_Frequency = 5,//Auto (based on balance and risk)
                   Manual_Strategy_Selection = 6//Manual strategy selection
                     };
  enum e_SlippageControlMode      {SCT_1 = 1,SCT_2 = 2  };
  enum FakeoutFilters      {Filter_Off = 0,//OFF
                   Filter_Low = 1,//Low
                   Filter_Medium = 2,//Medium
                   Filter_High = 3//High
                     };
  enum e_VirtualStopMode      {VSL_OFF = 1,VSL_BASIC = 2,VSL_ADV = 3  };
  enum Select_Entry_Strategy      {Strategy_ONE = 1,Strategy_TWO = 2  };
  enum e_TimeFrame_St_ONE      {ST1_M1 = 1,ST1_M5 = 5,ST1_M15 = 15,ST1_M30 = 30,ST1_H1 = 60,ST1_H4 = 240,ST1_Daily = 1440,ST1_Chart = 0  };
  enum e_TimeFrame_Entry_Timing      {Entry_T_Tick = 0,Entry_T_M1 = 1,Entry_T_M5 = 5,Entry_T_M15 = 15,Entry_T_M30 = 30,Entry_T_H1 = 60,Entry_T_H4 = 240  };
  enum e_UseOfCompound      {no_compound = 0,one_trade = 1,Multi_trades = 2  };
  enum e_MonitorTradesFilter      {MT_all = 0,MT_PairOfChart = 1  };
  enum e_TimeFrame_Exit_Timing      {ET_Tick = 0,ET_M1 = 1,ET_M5 = 5,ET_M15 = 15,ET_M30 = 30,ET_H1 = 60  };
  enum e_Exit_HL_trailingSL_timeframe      {HLT_Chart = 0,HLT_M1 = 1,HLT_M5 = 5,HLT_M15 = 15,HLT_M30 = 30,HLT_H1 = 60,HLT_H4 = 240,HLT_D1 = 1440  };
  enum ST1_e_MagicTrail_Mode      {ST1_MT_M_O = 0,ST1_MT_M_F = 1,ST1_MT_M_B = 2  };
  enum e_Risk      {Manual_Lotsize = 0,//use StartLots
                   MaxHistoricalDD = 1234,//Max Allowed Total Drawdown
                   MaxRiskStrat = 3//Max Risk Per Strategy
                     };
  enum Performance_options      {NormalizedProfit = 2,RealProfit = 1  };
  enum RankingOptions      {ranking_profit = 1,ranking_pertrade = 2  };
  enum Reduction_choices      {Red_10 = 10,Red_20 = 20,Red_30 = 30,Red_40 = 40,Red_50 = 50,Red_60 = 60,Red_70 = 70,Red_80 = 80,Red_90 = 90  };
  enum e_factortype      {factor_type_1 = 1,factor_type_2 = 2,factor_type_3 = 3  };
  enum e_TimeSource      {TZ_GMT = 0,TZ_PC = 1,TZ_Broker = 2  };


//------------------
bool USE_CUSTOM_DASHBOARD=false;
bool STYLE_NATIVE_CANDLES=false;
bool SHOW_STYLED_CANDLES=false;
int  STYLED_CANDLES_COUNT=90;
int  VISUAL_REFRESH_SECONDS=1;
input group "======= Community & Support ========"
input string InpWarn1="!! WARNING: Do NOT use MQL VPS !!";                    // READ THIS
input string InpWarn2="MQL VPS may lose sync! EA cannot manage trades";      // Risk Notice
input string InpInfoBacktest="";                                              // Perform a Backtest before use!
input string InpInfoDiscord="https://discord.gg/sqCYfR72x9";                  // Discord
input string InpInfoSupport="www.mql5.com/en/users/zolia";                    // Support
input group "======= Main Settings ========"
input int InpPreset=1;                                                         // Preset
input bool InpAllowBuy=true;                                                   // Allow Buy Trades
input bool InpAllowSell=true;                                                  // Allow Sell Trades
input int InpFrequency=0;                                                      // Trade Frequency
input double InpMaxSpread=60.0;                                                // Max Spread (pips)
input int InpFakeout=2;                                                        // Fakeout Filter
input int Zone1_ID=1337;                                                       // Magic Number
input string Zone1_Tag="Lizard";                                               // Trade Comment
input group "============ Trailing/SL ============="
input int InpTrailMode=0;                                                      // Trailing/BE Mode
input int InpFixedBE=0;                                                        // Fixed: BE Trigger (points, 0=must set)
input int InpFixedTrail=0;                                                     // Fixed: Trail Distance (points, 0=must set)
input int InpStopLossPts=3000;                                                 // Stop Loss (points)
input int InpTrailJitter=0;                                                    // Trail Jitter (0=off, 10-50, prop frm exit diversity)
input group "========== Market Window ==========="
input int InpCloseWindow=90;                                                   // Close Window (minutes before broker close)
input int InpOpenDelay=15;                                                     // Resume Entries After Open
input bool InpCloseWeekend=false;                                              // Close Before Weekend
input group "======= News Filter ========"
input bool InpNFP_Enable=true;                                                 // NFP (Non-Farm Payrolls, offline calendar)
input bool InpNews_High=true;                                                  // High Impact (CPI, FOMC, PPI etc.)
input bool InpNews_Medium=false;                                               // Medium Impact
input bool InpNews_Low=false;                                                  // Low Impact
input bool InpNews_Holidays=true;                                              // USD Holidays (no trading entire day)
input int Inp_GMT_Winter=2;                                                    // GMT Offset Winter (Backtest + Manual Live)
input int Inp_GMT_Summer=3;                                                    // GMT Offset Summer (Backtest + Manual Live)
input bool InpGMT_AutoDetect=true;                                             // GMT Auto-Detect Live (Off = use Winter/Summer above)
input bool InpNFP_CloseOpen=true;                                              // Close Open Positions (News)
input bool InpNFP_ClosePending=true;                                           // Close Pending Orders (News)
input int InpNFP_Before=30;                                                    // Minutes Before News
input int InpNFP_After=30;                                                     // Minutes After News
input group "======= Lot Settings ========"
input string InpRiskInfo="";                                                   // Lot sizing only! Not a loss cap - use Max Daily DD for that
input int InpLotMode=1;                                                        // Lot Mode
input int InpRiskScope=0;                                                      // Risk Scope (only Risk %)
input double InpFixLot=0.01;                                                   // Fix Lot Size
input double InpRiskPct=5.0;                                                   // Risk %
input double InpDollarPer001=2500.0;                                           // $ per 0.01 Lot
input bool InpUseEquity=false;                                                 // Use Equity
input bool InpCheckMargin=true;                                                // Check Margin
input double InpMaxDailyDD=0.0;                                                // Max Daily DD value (0=off; % or $ per Unit)
input int InpMaxDDUnit=0;                                                      // Max Daily DD Unit (% of peak equity, or $)
input group "======= Zone Selection (Custom) ========"
input bool InpZoneA1=true;                                                     // A1 H1 Breakout (Base)
input bool InpZoneA2=true;                                                     // A2 H1 Breakout (Base)
input bool InpZoneA3=true;                                                     // A3 H1 Breakout (Base)
input bool InpZoneB1=true;                                                     // B1 H1 Breakout (Mid)
input bool InpZoneB2=true;                                                     // B2 H1 Breakout (Mid)
input bool InpZoneB3=true;                                                     // B3 H1 Breakout (Mid)
input group "======= Session Filter ========"
input bool InpSess_Asia=true;                                                  // Asia (00:00-08:00 GMT)
input bool InpSess_London=true;                                                // London (08:00-13:00 GMT)
input bool InpSess_Overlap=true;                                               // London+NY Overlap (13:00-17:00 GMT)
input bool InpSess_NY=true;                                                    // New York (17:00-22:00 GMT)
input group "======= Info Panel ========"
input bool InpShowPanel=true;                                                  // Show Panel (Live)
input bool InpPanelTest=false;                                                 // Show Panel (Backtest)

bool UseVariableValues=true;
bool AdjustLotsizeToVariableValues=true;
bool ShowInfoPanel=true;
double InfoPanelSizeAdjust=1.0;
bool UpdateInfoTesting=false;
string spreadfilter="";
bool AllowBuyTrades=true;
bool AllowSellTrades=true;
enum_TradeFrequency TradeFrequency=Manual_Strategy_Selection;
double MaxSpread=60.0;
bool PRINT_ENTRY_DEBUG=false;
bool UseHL_TrailingSL=false;
int FridayStopHour=25;
bool setSL_TP_After_Entry=false;
bool Virtual_expiration=true;
double Randomization=0.0;
FakeoutFilters FakeOutFilter=(FakeoutFilters)2;
int ST1_MagicNumber=1337;
string ST1_Comment="Lizard";
bool RemoveCommentSuffix=false;
string NFP_FILTER="";
bool UseNewsFilter=true;
bool EnableNFP_Filter=true;
bool AutoGMT=true;
bool UseExternalGMTSync=false;
int Broker_GMT_OFFSET_Winter=2;
int Broker_GMT_OFFSET_Summer=3;
bool NFP_CloseOpenTrades=true;
bool NFP_ClosePendingOrders=true;
int NFP_MinutesBefore=30;
int NFP_MinutesAfter=30;
string propfirmsettings="";
double AdjustEntry=0.0;
double AdjustSL=0.0;
double AdjustTP=0.0;
double AdjustTrailSL=0.0;
double AdjustTrailTP=0.0;
double AdjustBreakEven=0.0;
string LotSizeSettings="";
double ForceBalanceToUse=0.0;
e_Risk Risk=Manual_Lotsize;
double StartLots=0.01;
double StartLotsRuntime=0.01;
double MaxAllowedDD=30.0;
bool UseWeightedLots=false;
double MaxRiskPerStrategy_=1.0;
double PropFirmMaxDailyDD=0.0;
bool UseEquity=false;
bool OnlyUp=true;
bool CheckMargin=true;
string ManualStratSelect="";
string ManStratWarn="";
bool RunStrat1=false;
bool RunStrat2=false;
bool RunStrat3=true;
bool RunStrat4=false;
bool RunStrat5=true;
bool RunStrat6=true;
bool RunStrat7=true;
bool RunStrat8=true;
bool RunStrat9=true;

void LizardApplyPublicInputs()
{
   const bool panel_allowed=(!MQLInfoInteger(MQL_TESTER) || InpPanelTest);
   USE_CUSTOM_DASHBOARD=(InpShowPanel && panel_allowed);
   ShowInfoPanel=(InpShowPanel && panel_allowed);
   UpdateInfoTesting=InpPanelTest;
   AllowBuyTrades=InpAllowBuy;
   AllowSellTrades=InpAllowSell;
   MaxSpread=InpMaxSpread;
   FakeOutFilter=(FakeoutFilters)InpFakeout;
   ST1_MagicNumber=Zone1_ID;
   ST1_Comment=Zone1_Tag;
   UseNewsFilter=(InpNFP_Enable || InpNews_High || InpNews_Medium ||
                  InpNews_Low || InpNews_Holidays);
   EnableNFP_Filter=InpNFP_Enable;
   AutoGMT=InpGMT_AutoDetect;
   Broker_GMT_OFFSET_Winter=Inp_GMT_Winter;
   Broker_GMT_OFFSET_Summer=Inp_GMT_Summer;
   NFP_CloseOpenTrades=InpNFP_CloseOpen;
   NFP_ClosePendingOrders=InpNFP_ClosePending;
   NFP_MinutesBefore=InpNFP_Before;
   NFP_MinutesAfter=InpNFP_After;
   StartLots=InpFixLot;
   UseEquity=InpUseEquity;
   CheckMargin=InpCheckMargin;
   PropFirmMaxDailyDD=InpMaxDailyDD;
   RunStrat1=false;
   RunStrat2=false;
   RunStrat3=InpZoneB3;
   RunStrat4=false;
   RunStrat5=InpZoneB2;
   RunStrat6=InpZoneA1;
   RunStrat7=InpZoneA2;
   RunStrat8=InpZoneA3;
   RunStrat9=InpZoneB1;
}

bool LizardEntryWindowOpen()
{
   MqlDateTime now;
   TimeToStruct(TimeCurrent(),now);
   const int minute_of_day=now.hour*60+now.min;
   if(InpCloseWeekend && (now.day_of_week==0 || now.day_of_week==6))
      return false;
   const int start_minute=MathMax(0,50+InpOpenDelay);
   const int end_minute=MathMin(1440,1440-InpCloseWindow);
   return minute_of_day>=start_minute && minute_of_day<end_minute;
}

bool LizardBetween(const int minute_of_day,const int start_minute,const int end_minute)
{
   return minute_of_day>=start_minute && minute_of_day<end_minute;
}

bool LizardZoneSessionOpen(const long magic)
{
   MqlDateTime now;
   TimeToStruct(TimeCurrent(),now);
   const int minute_of_day=now.hour*60+now.min;
   const long zone_magic=magic-ST1_MagicNumber;

   bool public_session_open=false;
   if(minute_of_day<480)
      public_session_open=InpSess_Asia;
   else if(minute_of_day<780)
      public_session_open=InpSess_London;
   else if(minute_of_day<1020)
      public_session_open=InpSess_Overlap;
   else
      public_session_open=InpSess_NY;
   if(!public_session_open) return false;

   if(zone_magic==9)  // A1
      return LizardBetween(minute_of_day,65,660) ||
             LizardBetween(minute_of_day,720,960) ||
             LizardBetween(minute_of_day,1020,1140) ||
             LizardBetween(minute_of_day,1200,1350);
   if(zone_magic==14) // A2
      return LizardBetween(minute_of_day,75,660) ||
             LizardBetween(minute_of_day,720,1140) ||
             LizardBetween(minute_of_day,1200,1260);
   if(zone_magic==15) // A3
      return LizardBetween(minute_of_day,75,480) ||
             LizardBetween(minute_of_day,540,660) ||
             LizardBetween(minute_of_day,720,780) ||
             LizardBetween(minute_of_day,840,1020) ||
             LizardBetween(minute_of_day,1080,1350);
   if(zone_magic==13) // B1
      return now.day_of_week!=3 &&
            (LizardBetween(minute_of_day,75,240) ||
             LizardBetween(minute_of_day,300,480) ||
             LizardBetween(minute_of_day,540,660) ||
             LizardBetween(minute_of_day,720,780) ||
             LizardBetween(minute_of_day,1080,1320));
   if(zone_magic==12) // B2
      return LizardBetween(minute_of_day,75,1350);
   if(zone_magic==8)  // B3
      return LizardBetween(minute_of_day,120,1350);
   return false;
}

void LizardClearPendingForMagic(const long required_magic)
{
   for(int index=OrdersTotal()-1;index>=0;index--)
   {
      const ulong ticket=OrderGetTicket(index);
      if(ticket==0) continue;
      if(OrderGetString(ORDER_SYMBOL)!=_Symbol) continue;
      if(OrderGetInteger(ORDER_MAGIC)!=required_magic) continue;
      const ENUM_ORDER_TYPE type=(ENUM_ORDER_TYPE)OrderGetInteger(ORDER_TYPE);
      if(type==ORDER_TYPE_BUY_LIMIT || type==ORDER_TYPE_SELL_LIMIT ||
         type==ORDER_TYPE_BUY_STOP || type==ORDER_TYPE_SELL_STOP ||
         type==ORDER_TYPE_BUY_STOP_LIMIT || type==ORDER_TYPE_SELL_STOP_LIMIT)
         MQL4_OrderDelete((long)ticket);
   }
}

void LizardClearManagedPendingOrders()
{
   for(int index=OrdersTotal()-1;index>=0;index--)
   {
      const ulong ticket=OrderGetTicket(index);
      if(ticket==0) continue;
      if(OrderGetString(ORDER_SYMBOL)!=_Symbol) continue;
      const long magic=OrderGetInteger(ORDER_MAGIC);
      if(magic<ST1_MagicNumber || magic>ST1_MagicNumber+30) continue;
      const ENUM_ORDER_TYPE type=(ENUM_ORDER_TYPE)OrderGetInteger(ORDER_TYPE);
      if(type==ORDER_TYPE_BUY_LIMIT || type==ORDER_TYPE_SELL_LIMIT ||
         type==ORDER_TYPE_BUY_STOP || type==ORDER_TYPE_SELL_STOP ||
         type==ORDER_TYPE_BUY_STOP_LIMIT || type==ORDER_TYPE_SELL_STOP_LIMIT)
         MQL4_OrderDelete((long)ticket);
   }
}

void LizardEnforceZoneSessions()
{
   const long zone_magics[6]=
   {
      ST1_MagicNumber+9,
      ST1_MagicNumber+14,
      ST1_MagicNumber+15,
      ST1_MagicNumber+13,
      ST1_MagicNumber+12,
      ST1_MagicNumber+8
   };
   for(int zone=0;zone<6;zone++)
      if(!LizardZoneSessionOpen(zone_magics[zone]))
         LizardClearPendingForMagic(zone_magics[zone]);
}

  double    g_1_do_0 = 0.0;
  double    g_2_do_8 = 0.0;
  int       g_3_in_10 = 30;
  int       g_4_in_14 = 1440;
  int       g_5_in_18 = 0;
  double    g_6_do_1C_ko[];
  double    g_7_do_50 = 0.0;
  double    g_8_do_58 = 0.0;
  double    g_9_do_60 = 0.0;
  bool      g_10_bo_68 = false;
  int       g_11_in_6C = 3;
  int       g_12_in_70 = 2;
  bool      g_13_bo_74 = false;
  bool      g_14_bo_75 = false;
  int       g_15_in_78 = 0;
  string    g_16_st_80 = "------------------------------ trading filters ------------------------------";
  bool      g_17_bo_8C = false;
  string    g_18_st_90 = "EURUSD;GBPUSD;USDJPY;AUDJPY;AUDUSD;EURAUD;EURCAD;EURGBP;EURJPY;GBPJPY;USDCAD;USDCHF;";
  int       g_19_in_9C = 5;
  bool      g_20_bo_A0 = true;
  bool      g_21_bo_A1 = false;
  bool      g_22_bo_A2 = false;
  bool      g_23_bo_A3 = true;
  bool      g_24_bo_A4 = false;
  bool      g_25_bo_A5 = false;
  bool      g_26_bo_A6 = true;
  bool      g_27_bo_A7 = false;
  bool      g_28_bo_A8 = false;
  bool      g_29_bo_A9 = false;
  bool      g_30_bo_AA = false;
  bool      g_31_bo_AB = false;
  bool      g_32_bo_AC = false;
  bool      g_33_bo_AD = false;
  bool      g_34_bo_AE = false;
  bool      g_35_bo_AF = true;
  int       g_36_in_B0 = 2;
  double    g_37_do_B8 = 0.0;
  int       g_38_do_C0 = 5000;
  int       g_39_in_C8 = 1;
  double    g_40_do_D0 = 400.0;
  double    g_41_do_D8 = 100.0;
  double    g_42_do_E0 = 300.0;
  bool      g_43_bo_E8 = true;
  string    g_44_st_F0 = "------------------------------ time filters ------------------------------";
  bool      g_45_bo_FC = false;
  bool      g_46_bo_FD = false;
  bool      g_47_bo_FE = false;
  int       g_48_in_100 = 14;
  int       g_49_in_104 = 17;
  string    g_50_st_108 = "------------------------------ other filters ------------------------------";
  int       g_51_in_114 = 1;
  int       g_52_in_118 = 1;
  bool      g_53_bo_11C = false;
  int       g_54_in_120 = 5;
  bool      g_55_bo_124 = false;
  int       g_56_in_128 = 15;
  bool      g_57_bo_12C = false;
  int       g_58_in_130 = 30;
  bool      g_59_bo_134 = false;
  int       g_60_in_138 = 60;
  bool      g_61_bo_13C = false;
  bool      g_62_bo_13D = false;
  int       g_63_in_140 = 1;
  double    g_64_do_148 = 0.0;
  int       g_65_in_150 = 99;
  int       g_66_in_154 = 5;
  bool      g_67_bo_158 = false;
  int       g_68_in_15C = 5;
  int       g_69_in_160 = 1;
  string    g_70_st_168 = "------------------------------ Trade Entry management ------------------------------";
  int       g_71_in_174 = 0;
  int       g_72_in_178 = 60;
  int       g_73_in_17C = 10;
  int       g_74_in_180 = 3;
  bool      g_75_bo_184 = false;
  bool      g_76_bo_185 = false;
  int       g_77_in_188 = 120;
  int       g_78_in_18C = 0;
  int       g_79_in_190 = 0;
  double    g_80_do_198 = 30.0;
  double    g_81_do_1A0 = 0.0;
  double    g_82_do_1A8 = 25.0;
  double    g_83_do_1B0 = 0.5;
  double    g_84_do_1B8 = 0.0;
  double    g_85_do_1C0 = 0.0;
  int       g_86_in_1C8 = 1;
  int       g_87_in_1CC = 99;
  double    g_88_do_1D0 = 1.0;
  int       g_89_in_1D8 = 24;
  double    g_90_do_1E0 = 3.0;
  int       g_91_in_1E8 = 0;
  int       g_92_in_1EC = 100;
  int       g_93_in_1F0 = 0;
  string    g_94_st_1F8 = "------------------------------ Strategy 2 - Manual Trade settings ------------------------------";
  int       g_95_in_204 = 1;
  int       g_96_in_208 = 1991199118;
  string    g_97_st_210 = "";
  string    g_98_st_220 = "------------------------------ Trade Exit management ------------------------------";
  int       g_99_in_22C = 0;
  double    g_100_do_230 = 20.0;
  double    g_101_do_238 = 100.0;
  string    g_102_st_240 = "------------------------------ Trailing SL settings ------------------------------";
  double    g_103_do_250 = 10.0;
  double    g_104_do_258 = 10.0;
  double    g_105_do_260 = 100.0;
  double    g_106_do_268 = 0.1;
  double    g_107_do_270 = 0.0;
  double    g_108_do_278 = 0.0;
  double    g_109_do_280 = 0.0;
  double    g_110_do_288 = 0.0;
  double    g_111_do_290 = 0.0;
  string    g_112_st_298 = "------------------------------ Break-even SL management ------------------------------";
  double    g_113_do_2A8 = 0.0;
  double    g_114_do_2B0 = 0.0;
  string    g_115_st_2B8 = "------------------------------ HIGH/LOW Trailing SL settings ------------------------------";
  bool      g_116_bo_2C4 = false;
  int       g_117_in_2C8 = 0;
  int       g_118_in_2CC = 0;
  int       g_119_in_2D0 = 0;
  int       g_120_in_2D4 = 0;
  int       g_121_in_2D8 = 0;
  int       g_122_in_2DC = 0;
  double    g_123_do_2E0 = 2.0;
  string    g_124_st_2E8 = "------------------------------ recovery Trailing SL based on time ------------------------------";
  double    g_125_do_2F8 = 0.0;
  double    g_126_do_300 = 0.0;
  string    g_127_st_308 = "------------------------------ MagicTrail SL settings ------------------------------";
  int       g_128_in_314 = 0;
  double    g_129_do_318 = 0.1;
  int       g_130_in_320 = 1;
  double    g_131_do_328 = 0.1;
  double    g_132_do_330 = 1.0;
  int       g_133_in_338 = 0;
  double    g_134_do_340 = 0.0;
  bool      g_135_bo_348 = false;
  bool      g_136_bo_349 = false;
  int       g_137_in_34C = 2024;
  datetime  g_138_da_384_si13[13];
  bool      g_139_bo_3EC = false;
  double    g_140_do_3F0 = 5.0;
  double    g_141_do_3F8 = 99.0;
  int       g_142_in_400 = 999;
  int       g_143_in_404 = 9999;
  int       g_144_in_408 = 99999;
  int       g_145_in_40C = 600;
  double    g_146_do_410 = 1.0;
  double    g_147_do_418 = 10.0;
  double    g_148_do_420 = 2.0;
  string    g_149_st_428 = "==== Performance numbers overview ====";
  bool      g_150_bo_434 = true;
  int       g_151_in_438 = 1;
  int       g_152_in_43C = 1;
  int       g_153_in_440 = 90;
  int       g_154_in_444 = 30;
  int       g_155_in_448 = 10;
  int       g_156_in_44C = 50;
  bool      g_157_bo_450 = true;
  string    g_158_st_458 = "------------------------------ zone_recovery_settings ------------------------------";
  bool      g_159_bo_464 = false;
  double    g_160_do_468 = 50.0;
  double    g_161_do_470 = 10.0;
  double    g_162_do_478 = 5.0;
  double    g_163_do_480 = 0.0;
  int       g_164_in_488 = 1;
  double    g_165_do_490 = 2.0;
  int       g_166_in_498 = 999;
  double    g_167_do_4A0 = 100.0;
  int       g_168_in_4A8 = 900010;
  int       g_169_in_4AC = 900011;
  string    g_170_st_4B0 = "------------------------- Trading hours ST1 -------------------------";
  bool      g_171_bo_4BC = false;
  int       g_172_in_4C0 = 2;
  bool      g_173_bo_4C4 = false;
  int       g_174_in_4C8 = 0;
  int       g_175_in_4CC = 24;
  int       g_176_in_4D0 = 0;
  int       g_177_in_4D4 = 24;
  int       g_178_in_4D8 = 0;
  int       g_179_in_4DC = 24;
  int       g_180_in_4E0 = 0;
  int       g_181_in_4E4 = 24;
  int       g_182_in_4E8 = 0;
  int       g_183_in_4EC = 24;
  int       g_184_in_4F0 = 0;
  int       g_185_in_4F4 = 24;
  string    g_186_st_4F8 = "------------------------- use for backtesting only! -------------------------";
  int       g_187_in_504 = 0;
  double    g_188_do_508 = 0.0;
  double    g_189_do_510 = 0.0;
  int       g_190_in_518 = 0;
  double    g_191_do_520 = 0.0;
  int       g_192_in_528 = 0;
  int       g_193_in_52C = 0;
  bool      g_194_bo_530 = false;
  bool      g_195_bo_531 = false;
  double    g_196_do_568_si20si2[20][2];
  double    g_197_do_6DC_si100si3[100][3];
  double    g_198_do_1070_si100si2[100][2];
  int       g_199_in_16B0 = 20;
  int       g_200_in_16B4 = 100;
  double    g_201_do_16B8 = 0.0;
  double    g_202_do_16C0 = 0.0;
  double    g_203_do_16C8 = 0.0;
  double    g_204_do_16D0 = 0.0;
  double    g_205_do_16D8 = 0.0;
  double    g_206_do_16E0 = 0.0;
  bool      g_207_bo_16E8 = false;
  int       g_208_in_16EC = 10;
  double    g_209_do_16F0 = 0.0;
  double    g_210_do_16F8 = 0.0;
  double    g_211_do_1700 = 0.0;
  double    g_212_do_1708 = 0.0;
  bool      g_213_bo_1710 = false;
  int       g_214_in_1714 = 1;
  datetime  g_215_da_174C_si99[99];
  long      g_216_lo_1A68 = 0;
  int       g_217_in_1A70 = 370;
  bool      g_218_bo_1A74 = true;
  bool      g_219_bo_1A75 = false;
  int       g_220_in_1A78 = 0;
  double    g_221_do_1A80 = 4.0;
  double    g_222_do_1A88 = 0.0;
  double    g_223_do_1AC4_si99[99];
  double    g_224_do_1DE0 = 0.0;
  int       g_225_in_1DE8 = 0;
  int       g_226_in_1DEC = 0;
  double    g_227_do_1DF0 = 0.0;
  double    g_228_do_1DF8 = 0.0;
  double    g_229_do_1E00 = 0.0;
  long      g_230_in_1E08 = 0;
  bool      g_231_bo_1E0C = false;
  double    g_232_do_1E10 = 0.0;
  double    g_233_do_1E18 = 0.0;
  int       g_234_in_1E20 = 0;
  double    g_235_do_1E28 = 0.0;
  double    g_236_do_1E30 = 0.0;
  double    g_237_do_1E38 = 0.0;
  bool      g_238_bo_1E40 = false;
  bool      g_239_bo_1E41 = false;
  bool      g_240_bo_1E42 = false;
  double    g_241_do_1E78_si99[99];
  double    g_242_do_21C4_si99[99];
  double    g_243_do_24E0 = 0.0;
  double    g_244_do_24E8 = 0.0;
  double    g_245_do_24F0 = 0.0;
  double    g_246_do_24F8 = 0.0;
  double    g_247_do_2500 = 0.0;
  double    g_248_do_2508 = 0.0;
  double    g_249_do_2510 = 0.0;
  int       g_250_in_2518 = 0;
  double    g_251_do_2520 = 0.0;
  string    g_252_st_2528;
  string    g_253_st_2538;
  string    g_254_st_2548;
  string    g_255_st_2558;
  bool      g_256_bo_2564 = false;
  bool      g_257_bo_2565 = false;
  int       g_258_in_2568 = 0;
  int       g_259_in_256C = 0;
  double    g_260_do_2570 = 0.0;
  double    g_261_do_2578 = 0.0;
  double    g_262_do_2580 = 0.0;
  double    g_263_do_2588 = 0.0;
  double    g_264_do_2590 = 0.0;
  int       g_265_in_2598 = 0;
  int       g_266_in_259C = 0;
  int       g_267_in_25A0 = 0;
  double    g_268_do_25A8 = 0.0;
  double    g_269_do_25B0 = 0.0;
  double    g_270_do_25B8 = 0.0;
  double    g_271_do_25C0 = 0.0;
  double    g_272_do_25C8 = 0.0;
  double    g_273_do_25D0 = 0.0;
  int       g_274_in_25D8 = 0;
  double    g_275_do_25E0 = 0.0;
  double    g_276_do_25E8 = 0.0;
  double    g_277_do_25F0 = 0.0;
  bool      g_278_bo_25F8 = false;
  bool      g_279_bo_25F9 = false;
  bool      g_280_bo_25FA = false;
  bool      g_281_bo_25FB = false;
  bool      g_282_bo_25FC = false;
  bool      g_283_bo_25FD = false;
  double    g_284_do_2600 = 0.0;
  double    g_285_do_2608 = 0.0;
  bool      g_286_bo_2610 = false;
  double    g_287_do_2618 = 0.0;
  double    g_288_do_2620 = 0.0;
  int       g_289_in_2628 = 0;
  int       g_290_in_262C = 0;
  double    g_291_do_2664_si10[10];
  double    g_292_do_26E8_si10[10];
  double    g_293_do_276C_si10[10];
  double    g_294_do_27F0_si10[10];
  int       g_295_in_2840 = 0;
  int       g_296_in_2844 = 0;
  int       g_297_in_2848 = 0;
  int       g_298_in_284C = 0;
  string    g_299_st_2850;
  double    g_300_do_2860 = 0.0;
  double    g_301_do_2868 = 0.0;
  datetime  g_302_da_2870 = 0;
  bool      g_303_bo_2878 = false;
  int       g_304_in_287C = 0;
  bool      g_305_bo_2880 = false;
  int       g_306_in_2884 = 0;
  double    g_307_do_2888 = 0.0;
  double    g_308_do_2890 = 0.0;
  double    g_309_do_2898 = 0.0;
  double    g_310_do_28A0 = 0.0;
  double    g_311_do_28A8 = 0.0;
  bool      g_312_bo_28B0 = false;
  datetime  g_313_da_28B8 = 0;
  datetime  g_314_da_28C0 = 0;
  datetime  g_315_da_28C8 = 0;
  bool      g_316_bo_28D0 = false;
  bool      g_317_bo_28D1 = false;
  double    g_318_do_28D8 = 0.0;
  datetime  g_319_da_28E0 = 0;
  bool      g_320_bo_28E8 = false;
  int       g_321_in_2920_si99[99];
  int       g_322_in_2AE0_si99[99];
  double    g_323_do_2CA0_si30[30];
  double    g_324_do_2DC4_si30[30];
  double    g_325_do_2EE8_si30[30];
  double    g_326_do_300C_si30[30];
  int       g_327_in_30FC = 1;
  int       g_328_in_3100 = 0;
  uint      g_329_ui_3104 = DarkBlue;
  bool      g_330_bo_3108 = false;
  long      g_331_lo_3110 = 0;
  int       g_332_in_3118 = 5;
  bool      g_333_bo_311C = false;
  string    g_334_st_3120;
  bool      g_335_bo_312C = false;
  string    g_336_st_3130;
  double    g_337_do_3140 = 0.0;
  double    g_338_do_3148 = 0.0;
  int       g_339_in_3184_si99[99];
  int       g_340_in_3310 = 0;
  double    g_341_do_3348_si99[99];
  bool      g_342_bo_3694_si99[99];
  int       g_343_in_372C_si99[99];
  int       g_344_in_38EC_si99[99];
  double    g_345_do_3AAC_si99[99];
  double    g_346_do_3DF8_si99[99];
  string    g_347_st_4144_si99[99]={};
  bool      g_348_bo_461C_si99[99];
  double    g_349_do_46B4_si99[99];
  double    g_350_do_4A00_si99[99];
  double    g_351_do_4D4C_si99[99];
  double    g_352_do_5098_si99[99];
  double    g_353_do_53E4_si99[99];
  double    g_354_do_5730_si99[99];
  bool      g_355_bo_5A7C_si99[99];
  int       g_356_in_5B14_si99[99];
  bool      g_357_bo_5CA0 = false;
  double    g_358_do_5CA8 = 5.0;
  double    g_359_do_5CB0 = 10.0;
  int       g_360_in_5CB8 = 0;
  double    g_361_do_5CC0 = 0.0;
  double    g_362_do_5CC8 = 0.0;
  int       g_363_in_5CD0 = 0;
  uint      g_364_ui_5CD4 = LightSteelBlue;
  bool      g_365_bo_5CD8 = true;
  double    g_366_do_5CE0 = 12.0;
  int       g_367_in_5CE8 = 230;
  int       g_368_in_5CEC = 320;
  int       g_369_in_5CF0 = 500;
  int       g_370_in_5CF4 = 350;
  int       g_371_in_5CF8 = 2;
  int       g_372_in_5CFC = 7;
  int       g_373_in_5D00 = 10;
  int       g_374_in_5D04 = 30;
  string    g_375_st_5D3C_si4[4]={};
  double    g_376_do_5D70 = 0.45;
  double    g_377_do_5D78 = 0.6;
  int       g_378_in_5D80 = 0;
  datetime  g_379_da_5D88 = 0;
  bool      g_380_bo_5D90 = false;
  int       g_381_in_5D94 = 0;
  bool      g_382_bo_5D98 = false;
  int       g_383_in_5D9C = 0;
  double    g_384_do_5DA0 = 0.0;
  int       g_385_in_5DA8 = 200;
  int       g_386_in_5DAC = 330;
  int       g_387_in_5DB0 = 560;
  int       g_388_in_5DB4 = 810;
  int       g_389_in_5DB8 = 1150;
  datetime  g_390_da_5DC0 = 0;
  datetime  g_391_da_5DFC_si300[300];
  bool      g_392_bo_675C = false;
  bool      g_393_bo_675D = false;
  bool      g_394_bo_675E = false;
  int       g_395_in_6760 = 0;
  int       g_396_in_6764 = 0;
  double    g_397_do_6768 = 0.0;
  double    g_398_do_6770 = 0.0;
  datetime  g_399_da_6778 = 0;
  double    g_400_do_67B4_si99[99];
  double    g_401_do_6AD0 = 0.0;
  double    g_402_do_6AD8 = 0.0;


 int init()
 {
  double    local_2_do;
  double    local_3_do;
  int       local_4_in;
  int       local_5_in;
  int       local_6_in;
  int       local_7_in;
  int       local_8_in;
  int       local_9_in;
//----- -----
 bool       tmp_bo_1 = false;

 g_401_do_6AD0 = AccountInfoDouble(ACCOUNT_BALANCE) ;
 if ( UseEquity )
 {
   g_401_do_6AD0 = AccountInfoDouble(ACCOUNT_EQUITY) ;
 }
 if ( ForceBalanceToUse>0.0 )
 {
   g_401_do_6AD0 = ForceBalanceToUse ;
 }
 g_402_do_6AD8 = g_401_do_6AD0 ;
 g_392_bo_675C = false ;
 g_393_bo_675D = false ;
 g_391_da_5DFC_si300[0] = D'2026.12.04 12:30';
 g_391_da_5DFC_si300[1] = D'2026.11.06 12:30';
 g_391_da_5DFC_si300[2] = D'2026.10.02 12:30';
 g_391_da_5DFC_si300[3] = D'2026.09.04 12:30';
 g_391_da_5DFC_si300[4] = D'2026.08.07 12:30';
 g_391_da_5DFC_si300[5] = D'2026.07.02 12:30';
 g_391_da_5DFC_si300[6] = D'2026.06.05 12:30';
 g_391_da_5DFC_si300[7] = D'2026.05.08 12:30';
 g_391_da_5DFC_si300[8] = D'2026.04.03 12:30';
 g_391_da_5DFC_si300[9] = D'2026.03.06 12:30';
 g_391_da_5DFC_si300[10] = D'2026.02.11 12:30';
 g_391_da_5DFC_si300[11] = D'2026.01.09 12:30';
 g_391_da_5DFC_si300[12] = D'2025.12.16 12:30';
 g_391_da_5DFC_si300[13] = D'2025.11.07 12:30';
 g_391_da_5DFC_si300[14] = D'2025.10.03 12:30';
 g_391_da_5DFC_si300[15] = D'2025.09.05 12:30';
 g_391_da_5DFC_si300[16] = D'2025.08.01 12:30';
 g_391_da_5DFC_si300[17] = D'2025.07.03 12:30';
 g_391_da_5DFC_si300[18] = D'2025.06.06 12:30';
 g_391_da_5DFC_si300[19] = D'2025.05.02 12:30';
 g_391_da_5DFC_si300[20] = D'2025.04.04 12:30';
 g_391_da_5DFC_si300[21] = D'2025.03.07 12:30';
 g_391_da_5DFC_si300[22] = D'2025.02.07 12:30';
 g_391_da_5DFC_si300[23] = D'2025.01.10 12:30';
 g_391_da_5DFC_si300[24] = D'2024.12.06 12:30';
 g_391_da_5DFC_si300[25] = D'2024.11.01 12:30';
 g_391_da_5DFC_si300[26] = D'2024.10.04 12:30';
 g_391_da_5DFC_si300[27] = D'2024.09.06 12:30';
 g_391_da_5DFC_si300[28] = D'2024.08.02 12:30';
 g_391_da_5DFC_si300[29] = D'2024.07.05 12:30';
 g_391_da_5DFC_si300[30] = D'2024.06.07 12:30';
 g_391_da_5DFC_si300[31] = D'2024.05.03 12:30';
 g_391_da_5DFC_si300[32] = D'2024.04.05 12:30';
 g_391_da_5DFC_si300[33] = D'2024.03.08 12:30';
 g_391_da_5DFC_si300[34] = D'2024.02.02 12:30';
 g_391_da_5DFC_si300[35] = D'2024.01.05 12:30';
 g_391_da_5DFC_si300[36] = D'2023.12.08 12:30';
 g_391_da_5DFC_si300[37] = D'2023.11.03 12:30';
 g_391_da_5DFC_si300[38] = D'2023.10.06 12:30';
 g_391_da_5DFC_si300[39] = D'2023.09.01 12:30';
 g_391_da_5DFC_si300[40] = D'2023.08.04 12:30';
 g_391_da_5DFC_si300[41] = D'2023.07.07 12:30';
 g_391_da_5DFC_si300[42] = D'2023.06.02 12:30';
 g_391_da_5DFC_si300[43] = D'2023.05.05 12:30';
 g_391_da_5DFC_si300[44] = D'2023.04.07 12:30';
 g_391_da_5DFC_si300[45] = D'2023.03.10 12:30';
 g_391_da_5DFC_si300[46] = D'2023.02.03 12:30';
 g_391_da_5DFC_si300[47] = D'2023.01.06 12:30';
 g_391_da_5DFC_si300[48] = D'2022.12.02 12:30';
 g_391_da_5DFC_si300[49] = D'2022.11.04 12:30';
 g_391_da_5DFC_si300[50] = D'2022.10.07 12:30';
 g_391_da_5DFC_si300[51] = D'2022.09.02 12:30';
 g_391_da_5DFC_si300[52] = D'2022.08.05 12:30';
 g_391_da_5DFC_si300[53] = D'2022.07.08 12:30';
 g_391_da_5DFC_si300[54] = D'2022.06.03 12:30';
 g_391_da_5DFC_si300[55] = D'2022.05.06 12:30';
 g_391_da_5DFC_si300[56] = D'2022.04.01 12:30';
 g_391_da_5DFC_si300[57] = D'2022.03.04 12:30';
 g_391_da_5DFC_si300[58] = D'2022.02.04 12:30';
 g_391_da_5DFC_si300[59] = D'2022.01.07 12:30';
 g_391_da_5DFC_si300[60] = D'2021.12.03 12:30';
 g_391_da_5DFC_si300[61] = D'2021.11.05 12:30';
 g_391_da_5DFC_si300[62] = D'2021.10.08 12:30';
 g_391_da_5DFC_si300[63] = D'2021.09.03 12:30';
 g_391_da_5DFC_si300[64] = D'2021.08.06 12:30';
 g_391_da_5DFC_si300[65] = D'2021.07.02 12:30';
 g_391_da_5DFC_si300[66] = D'2021.06.04 12:30';
 g_391_da_5DFC_si300[67] = D'2021.05.07 12:30';
 g_391_da_5DFC_si300[68] = D'2021.04.02 12:30';
 g_391_da_5DFC_si300[69] = D'2021.03.05 12:30';
 g_391_da_5DFC_si300[70] = D'2021.02.05 12:30';
 g_391_da_5DFC_si300[71] = D'2021.01.08 12:30';
 g_391_da_5DFC_si300[72] = D'2020.12.04 12:30';
 g_391_da_5DFC_si300[73] = D'2020.11.06 12:30';
 g_391_da_5DFC_si300[74] = D'2020.10.02 12:30';
 g_391_da_5DFC_si300[75] = D'2020.09.04 12:30';
 g_391_da_5DFC_si300[76] = D'2020.08.07 12:30';
 g_391_da_5DFC_si300[77] = D'2020.07.02 12:30';
 g_391_da_5DFC_si300[78] = D'2020.06.05 12:30';
 g_391_da_5DFC_si300[79] = D'2020.05.08 12:30';
 g_391_da_5DFC_si300[80] = D'2020.04.03 12:30';
 g_391_da_5DFC_si300[81] = D'2020.03.06 12:30';
 g_391_da_5DFC_si300[82] = D'2020.02.07 12:30';
 g_391_da_5DFC_si300[83] = D'2020.01.10 12:30';
 g_391_da_5DFC_si300[84] = D'2019.12.06 12:30';
 g_391_da_5DFC_si300[85] = D'2019.11.01 12:30';
 g_391_da_5DFC_si300[86] = D'2019.10.04 12:30';
 g_391_da_5DFC_si300[87] = D'2019.09.06 12:30';
 g_391_da_5DFC_si300[88] = D'2019.08.02 12:30';
 g_391_da_5DFC_si300[89] = D'2019.07.05 12:30';
 g_391_da_5DFC_si300[90] = D'2019.06.07 12:30';
 g_391_da_5DFC_si300[91] = D'2019.05.03 12:30';
 g_391_da_5DFC_si300[92] = D'2019.04.05 12:30';
 g_391_da_5DFC_si300[93] = D'2019.03.08 12:30';
 g_391_da_5DFC_si300[94] = D'2019.02.01 12:30';
 g_391_da_5DFC_si300[95] = D'2019.01.04 12:30';
 g_391_da_5DFC_si300[96] = D'2018.12.07 12:30';
 g_391_da_5DFC_si300[97] = D'2018.11.02 12:30';
 g_391_da_5DFC_si300[98] = D'2018.10.05 12:30';
 g_391_da_5DFC_si300[99] = D'2018.09.07 12:30';
 g_391_da_5DFC_si300[100] = D'2018.08.03 12:30';
 g_391_da_5DFC_si300[101] = D'2018.07.06 12:30';
 g_391_da_5DFC_si300[102] = D'2018.06.01 12:30';
 g_391_da_5DFC_si300[103] = D'2018.05.04 12:30';
 g_391_da_5DFC_si300[104] = D'2018.04.06 12:30';
 g_391_da_5DFC_si300[105] = D'2018.03.09 12:30';
 g_391_da_5DFC_si300[106] = D'2018.02.02 12:30';
 g_391_da_5DFC_si300[107] = D'2018.01.05 12:30';
 g_391_da_5DFC_si300[108] = D'2017.12.08 12:30';
 g_391_da_5DFC_si300[109] = D'2017.11.03 12:30';
 g_391_da_5DFC_si300[110] = D'2017.10.06 12:30';
 g_391_da_5DFC_si300[111] = D'2017.09.01 12:30';
 g_391_da_5DFC_si300[112] = D'2017.08.04 12:30';
 g_391_da_5DFC_si300[113] = D'2017.07.07 12:30';
 g_391_da_5DFC_si300[114] = D'2017.06.02 12:30';
 g_391_da_5DFC_si300[115] = D'2017.05.05 12:30';
 g_391_da_5DFC_si300[116] = D'2017.04.07 12:30';
 g_391_da_5DFC_si300[117] = D'2017.03.10 12:30';
 g_391_da_5DFC_si300[118] = D'2017.02.03 12:30';
 g_391_da_5DFC_si300[119] = D'2017.01.06 12:30';
 g_391_da_5DFC_si300[120] = D'2016.12.02 12:30';
 g_391_da_5DFC_si300[121] = D'2016.11.04 12:30';
 g_391_da_5DFC_si300[122] = D'2016.10.07 12:30';
 g_391_da_5DFC_si300[123] = D'2016.09.02 12:30';
 g_391_da_5DFC_si300[124] = D'2016.08.05 12:30';
 g_391_da_5DFC_si300[125] = D'2016.07.08 12:30';
 g_391_da_5DFC_si300[126] = D'2016.06.03 12:30';
 g_391_da_5DFC_si300[127] = D'2016.05.06 12:30';
 g_391_da_5DFC_si300[128] = D'2016.04.01 12:30';
 g_391_da_5DFC_si300[129] = D'2016.03.04 12:30';
 g_391_da_5DFC_si300[130] = D'2016.02.05 12:30';
 g_391_da_5DFC_si300[131] = D'2016.01.08 12:30';
 g_391_da_5DFC_si300[132] = D'2015.12.04 12:30';
 g_391_da_5DFC_si300[133] = D'2015.11.06 12:30';
 g_391_da_5DFC_si300[134] = D'2015.10.02 12:30';
 g_391_da_5DFC_si300[135] = D'2015.09.04 12:30';
 g_391_da_5DFC_si300[136] = D'2015.08.07 12:30';
 g_391_da_5DFC_si300[137] = D'2015.07.02 12:30';
 g_391_da_5DFC_si300[138] = D'2015.06.05 12:30';
 g_391_da_5DFC_si300[139] = D'2015.05.08 12:30';
 g_391_da_5DFC_si300[140] = D'2015.04.03 12:30';
 g_391_da_5DFC_si300[141] = D'2015.03.06 12:30';
 g_391_da_5DFC_si300[142] = D'2015.02.06 12:30';
 g_391_da_5DFC_si300[143] = D'2015.01.09 12:30';
 g_391_da_5DFC_si300[144] = D'2014.12.05 12:30';
 g_391_da_5DFC_si300[145] = D'2014.11.07 12:30';
 g_391_da_5DFC_si300[146] = D'2014.10.03 12:30';
 g_391_da_5DFC_si300[147] = D'2014.09.05 12:30';
 g_391_da_5DFC_si300[148] = D'2014.08.01 12:30';
 g_391_da_5DFC_si300[149] = D'2014.07.03 12:30';
 g_391_da_5DFC_si300[150] = D'2014.06.06 12:30';
 g_391_da_5DFC_si300[151] = D'2014.05.02 12:30';
 g_391_da_5DFC_si300[152] = D'2014.04.04 12:30';
 g_391_da_5DFC_si300[153] = D'2014.03.07 12:30';
 g_391_da_5DFC_si300[154] = D'2014.02.07 12:30';
 g_391_da_5DFC_si300[155] = D'2014.01.10 12:30';
 g_391_da_5DFC_si300[156] = D'2013.12.06 12:30';
 g_391_da_5DFC_si300[157] = D'2013.11.08 12:30';
 g_391_da_5DFC_si300[158] = D'2013.10.22 12:30';
 g_391_da_5DFC_si300[159] = D'2013.09.06 12:30';
 g_391_da_5DFC_si300[160] = D'2013.08.02 12:30';
 g_391_da_5DFC_si300[161] = D'2013.07.05 12:30';
 g_391_da_5DFC_si300[162] = D'2013.06.07 12:30';
 g_391_da_5DFC_si300[163] = D'2013.05.03 12:30';
 g_391_da_5DFC_si300[164] = D'2013.04.05 12:30';
 g_391_da_5DFC_si300[165] = D'2013.03.08 12:30';
 g_391_da_5DFC_si300[166] = D'2013.02.01 12:30';
 g_391_da_5DFC_si300[167] = D'2013.01.04 12:30';
 g_391_da_5DFC_si300[168] = D'2012.12.07 12:30';
 g_391_da_5DFC_si300[169] = D'2012.11.02 12:30';
 g_391_da_5DFC_si300[170] = D'2012.10.05 12:30';
 g_391_da_5DFC_si300[171] = D'2012.09.07 12:30';
 g_391_da_5DFC_si300[172] = D'2012.08.03 12:30';
 g_391_da_5DFC_si300[173] = D'2012.07.06 12:30';
 g_391_da_5DFC_si300[174] = D'2012.06.01 12:30';
 g_391_da_5DFC_si300[175] = D'2012.05.04 12:30';
 g_391_da_5DFC_si300[176] = D'2012.04.06 12:30';
 g_391_da_5DFC_si300[177] = D'2012.03.09 12:30';
 g_391_da_5DFC_si300[178] = D'2012.02.03 12:30';
 g_391_da_5DFC_si300[179] = D'2012.01.06 12:30';
 g_391_da_5DFC_si300[180] = D'2011.12.02 12:30';
 g_391_da_5DFC_si300[181] = D'2011.11.04 12:30';
 g_391_da_5DFC_si300[182] = D'2011.10.07 12:30';
 g_391_da_5DFC_si300[183] = D'2011.09.02 12:30';
 g_391_da_5DFC_si300[184] = D'2011.08.05 12:30';
 g_391_da_5DFC_si300[185] = D'2011.07.08 12:30';
 g_391_da_5DFC_si300[186] = D'2011.06.03 12:30';
 g_391_da_5DFC_si300[187] = D'2011.05.06 12:30';
 g_391_da_5DFC_si300[188] = D'2011.04.01 12:30';
 g_391_da_5DFC_si300[189] = D'2011.03.04 12:30';
 g_391_da_5DFC_si300[190] = D'2011.02.04 12:30';
 g_391_da_5DFC_si300[191] = D'2011.01.07 12:30';
 g_391_da_5DFC_si300[192] = D'2010.12.03 12:30';
 g_391_da_5DFC_si300[193] = D'2010.11.05 12:30';
 g_391_da_5DFC_si300[194] = D'2010.10.08 12:30';
 g_391_da_5DFC_si300[195] = D'2010.09.03 12:30';
 g_391_da_5DFC_si300[196] = D'2010.08.06 12:30';
 g_391_da_5DFC_si300[197] = D'2010.07.02 12:30';
 g_391_da_5DFC_si300[198] = D'2010.06.04 12:30';
 g_391_da_5DFC_si300[199] = D'2010.05.07 12:30';
 g_391_da_5DFC_si300[200] = D'2010.04.02 12:30';
 g_391_da_5DFC_si300[201] = D'2010.03.05 12:30';
 g_391_da_5DFC_si300[202] = D'2010.02.05 12:30';
 g_391_da_5DFC_si300[203] = D'2010.01.08 12:30';
 g_391_da_5DFC_si300[204] = D'2009.12.04 12:30';
 g_391_da_5DFC_si300[205] = D'2009.11.06 12:30';
 g_391_da_5DFC_si300[206] = D'2009.10.02 12:30';
 g_391_da_5DFC_si300[207] = D'2009.09.04 12:30';
 g_391_da_5DFC_si300[208] = D'2009.08.07 12:30';
 g_391_da_5DFC_si300[209] = D'2009.07.02 12:30';
 g_391_da_5DFC_si300[210] = D'2009.06.05 12:30';
 g_391_da_5DFC_si300[211] = D'2009.05.08 12:30';
 g_391_da_5DFC_si300[212] = D'2009.04.03 12:30';
 g_391_da_5DFC_si300[213] = D'2009.03.06 12:30';
 g_391_da_5DFC_si300[214] = D'2009.02.06 12:30';
 g_391_da_5DFC_si300[215] = D'2009.01.09 12:30';
 g_391_da_5DFC_si300[216] = D'2008.12.05 12:30';
 g_391_da_5DFC_si300[217] = D'2008.11.07 12:30';
 g_391_da_5DFC_si300[218] = D'2008.10.03 12:30';
 g_391_da_5DFC_si300[219] = D'2008.09.05 12:30';
 g_391_da_5DFC_si300[220] = D'2008.08.01 12:30';
 g_391_da_5DFC_si300[221] = D'2008.07.03 12:30';
 g_391_da_5DFC_si300[222] = D'2008.06.06 12:30';
 g_391_da_5DFC_si300[223] = D'2008.05.02 12:30';
 g_391_da_5DFC_si300[224] = D'2008.04.04 12:30';
 g_391_da_5DFC_si300[225] = D'2008.03.07 12:30';
 g_391_da_5DFC_si300[226] = D'2008.02.01 12:30';
 g_391_da_5DFC_si300[227] = D'2008.01.04 12:30';
 g_391_da_5DFC_si300[228] = D'2007.12.07 12:30';
 g_391_da_5DFC_si300[229] = D'2007.11.02 12:30';
 g_391_da_5DFC_si300[230] = D'2007.10.05 12:30';
 g_391_da_5DFC_si300[231] = D'2007.09.07 12:30';
 g_391_da_5DFC_si300[232] = D'2007.08.03 12:30';
 g_391_da_5DFC_si300[233] = D'2007.07.06 12:30';
 g_391_da_5DFC_si300[234] = D'2007.06.01 12:30';
 g_391_da_5DFC_si300[235] = D'2007.05.04 12:30';
 g_391_da_5DFC_si300[236] = D'2007.04.06 12:30';
 g_391_da_5DFC_si300[237] = D'2007.03.09 12:30';
 g_391_da_5DFC_si300[238] = D'2007.02.02 12:30';
 g_391_da_5DFC_si300[239] = D'2007.01.05 12:30';
 if ( Risk == 1234 )
 {
   StartLotsRuntime = MarketInfo(g_336_st_3130,MODE_MINLOT) ;
 }
 if ( TradeFrequency == 5 && Risk == 1234 )
 {
   local_2_do = lizong_36(AccountInfoDouble(ACCOUNT_BALANCE)) ;
   local_3_do = MaxAllowedDD / 100.0 * local_2_do ;
   if ( local_3_do>g_388_in_5DB4 )
   {
     g_19_in_9C = 3 ;
   }
   else
   {
     if ( local_3_do>g_387_in_5DB0 )
     {
       g_19_in_9C = 2 ;
     }
     else
     {
       if ( local_3_do>g_386_in_5DAC )
       {
         g_19_in_9C = 1 ;
       }
       else
       {
         g_19_in_9C = 0 ;
       }
     }
   }
 }
 else
 {
   g_19_in_9C = TradeFrequency ;
 }
 if ( g_19_in_9C == 0 )
 {
   g_27_bo_A7 = false ;
   g_31_bo_AB = false ;
   g_28_bo_A8 = false ;
   g_33_bo_AD = false ;
   g_34_bo_AE = false ;
   g_32_bo_AC = false ;
   g_398_do_6770 = 2.4 ;
   if ( UseVariableValues )
   {
     g_398_do_6770 = 3.0 ;
   }
 }
 else
 {
   if ( g_19_in_9C == 1 )
   {
     g_27_bo_A7 = true ;
     g_31_bo_AB = true ;
     g_28_bo_A8 = false ;
     g_33_bo_AD = false ;
     g_34_bo_AE = false ;
     g_32_bo_AC = false ;
     g_398_do_6770 = 3.4 ;
     if ( UseVariableValues )
     {
       g_398_do_6770 = 4.0 ;
     }
   }
   else
   {
     if ( g_19_in_9C == 2 )
     {
       g_27_bo_A7 = true ;
       g_31_bo_AB = true ;
       g_28_bo_A8 = true ;
       g_33_bo_AD = true ;
       g_34_bo_AE = false ;
       g_32_bo_AC = false ;
       g_398_do_6770 = 4.1 ;
       if ( UseVariableValues )
       {
         g_398_do_6770 = 5.0 ;
       }
     }
     else
     {
       if ( g_19_in_9C == 3 )
       {
         g_27_bo_A7 = true ;
         g_31_bo_AB = true ;
         g_28_bo_A8 = true ;
         g_33_bo_AD = true ;
         g_34_bo_AE = true ;
         g_32_bo_AC = false ;
         g_398_do_6770 = 4.8 ;
         if ( UseVariableValues )
         {
           g_398_do_6770 = 5.6 ;
         }
       }
       else
       {
         if ( g_19_in_9C == 4 )
         {
           g_27_bo_A7 = true ;
           g_31_bo_AB = true ;
           g_28_bo_A8 = true ;
           g_33_bo_AD = true ;
           g_34_bo_AE = true ;
           g_32_bo_AC = true ;
           g_398_do_6770 = 5.1 ;
           if ( UseVariableValues )
           {
             g_398_do_6770 = 6.0 ;
           }
         }
         else
         {
           if ( g_19_in_9C == 6 )
           {
             g_20_bo_A0 = RunStrat1 ;
             g_23_bo_A3 = RunStrat2 ;
             g_26_bo_A6 = RunStrat3 ;
             g_27_bo_A7 = RunStrat4 ;
             g_31_bo_AB = RunStrat5 ;
             g_28_bo_A8 = RunStrat6 ;
             g_33_bo_AD = RunStrat7 ;
             g_34_bo_AE = RunStrat8 ;
             g_32_bo_AC = RunStrat9 ;
           }
         }
       }
     }
   }
 }
 g_334_st_3120 = ST1_Comment ;
 g_384_do_5DA0 = 0.0 ;
 g_382_bo_5D98 = false ;
 g_379_da_5D88 = 0 ;
 g_380_bo_5D90 = true ;
 g_358_do_5CA8 = 5.0 ;
 g_359_do_5CB0 = 10.0 ;
 g_93_in_1F0 = ST1_MagicNumber ;
 g_360_in_5CB8 = 300 ;
 g_361_do_5CC0 = g_372_in_5CFC * 25 * g_376_do_5D70 * InfoPanelSizeAdjust ;
 g_362_do_5CC8 = g_372_in_5CFC * 3.5 * g_377_do_5D78 * InfoPanelSizeAdjust ;
 g_363_in_5CD0 = 7 ;
 g_328_in_3100 = 0 ;
 g_336_st_3130 = Symbol() ;
 g_337_do_3140 = SymbolInfoDouble(g_336_st_3130,16) ;
 g_229_do_1E00 = g_337_do_3140 ;
 if ( ( MarketInfo(g_336_st_3130,MODE_DIGITS)==3.0 || MarketInfo(g_336_st_3130,MODE_DIGITS)==5.0 ) )
 {
   g_229_do_1E00 = g_337_do_3140 * 10.0 ;
 }
 if ( SymbolInfoInteger(g_336_st_3130,17) == 0x1 )
 {
   g_229_do_1E00 = g_337_do_3140 / 10.0 ;
 }
 g_190_in_518 = (int)MarketInfo(g_336_st_3130,MODE_DIGITS) ;
 if ( FridayStopHour <  0 )
 {
   g_45_bo_FC = false ;
 }
 else
 {
   g_45_bo_FC = true ;
 }
 g_251_do_2520 = (double)(long)TimeCurrent() ;
 g_1_do_0 = MarketInfo(g_336_st_3130,MODE_ASK) - MarketInfo(g_336_st_3130,MODE_BID) ;
  g_223_do_1AC4_si99[g_328_in_3100] = NormalizeDouble(MathFloor(StartLotsRuntime * 100.0) / 100.0,2);
 if ( MarketInfo(g_336_st_3130,MODE_LOTSTEP)==0.1 )
 {
    g_223_do_1AC4_si99[g_328_in_3100] = NormalizeDouble((MathFloor(StartLotsRuntime * 10.0)) / 10.0,1);
   if ( g_223_do_1AC4_si99[g_328_in_3100]<0.1 )
   {
     g_223_do_1AC4_si99[g_328_in_3100] = 0.1;
   }
 }
 if ( g_223_do_1AC4_si99[g_328_in_3100]<MarketInfo(g_336_st_3130,MODE_MINLOT) )
 {
   g_223_do_1AC4_si99[g_328_in_3100] = MarketInfo(g_336_st_3130,MODE_MINLOT);
 }
 if ( g_223_do_1AC4_si99[g_328_in_3100]>MarketInfo(g_336_st_3130,MODE_MAXLOT) )
 {
   g_223_do_1AC4_si99[g_328_in_3100] = MarketInfo(g_336_st_3130,MODE_MAXLOT);
 }
 g_306_in_2884 = Bars ;
 if ( g_131_do_328 * g_229_do_1E00<g_337_do_3140 )
 {
   g_131_do_328 = g_337_do_3140 / g_229_do_1E00 ;
 }
 g_307_do_2888 = AccountBalance() ;
 g_221_do_1A80 = MarketInfo(g_336_st_3130,MODE_STOPLEVEL) * g_337_do_3140 ;
 g_309_do_2898 = MarketInfo(g_336_st_3130,MODE_FREEZELEVEL) * g_337_do_3140 ;
 g_299_st_2850 = StringSubstr(Symbol(),6,10) ;
 if ( g_299_st_2850 != "" )
 {
   Print("Suffix detected: " + g_299_st_2850); 
 }
 if ( ( StringFind(Symbol(),"XAUUSD",0) >= 0 || StringFind(Symbol(),"xauusd",0) >= 0 || StringFind(Symbol(),"GOLD",0) >= 0 || StringFind(Symbol(),"gold",0) >= 0 || StringFind(Symbol(),"Gold",0) >= 0 || StringFind(Symbol(),"GLD",0) >= 0 ) )
 {
   g_336_st_3130 = Symbol() ;
   g_347_st_4144_si99[g_378_in_5D80] = Symbol();
   lizong_37(); 
   lizong_6(0); 
   g_378_in_5D80 ++;
 }
 else
 {
   g_336_st_3130 = Symbol() ;
   lizong_6(0); 
 }
 if ( !(g_380_bo_5D90) )
 {
   Print("Initialisation of pairs failed!"); 
 }
 if ( g_100_do_230<=0.0 )
 {
   g_100_do_230 = 1.0 ;
 }
 if ( g_101_do_238<=0.0 )
 {
   g_101_do_238 = 1.0 ;
 }
 if ( g_114_do_2B0>g_113_do_2A8 )
 {
   g_114_do_2B0 = g_113_do_2A8 + 0.1 ;
 }
 if ( g_36_in_B0<g_309_do_2898 / g_229_do_1E00 )
 {
   g_36_in_B0 = (int)(g_309_do_2898 / g_229_do_1E00) ;
 }
 if ( g_103_do_250!=0.0 && g_103_do_250<g_309_do_2898 / g_229_do_1E00 )
 {
   g_103_do_250 = g_309_do_2898 / g_229_do_1E00 ;
 }
 if ( g_103_do_250!=0.0 && g_103_do_250<g_221_do_1A80 / g_229_do_1E00 )
 {
   g_103_do_250 = g_221_do_1A80 / g_229_do_1E00 ;
 }
 if ( g_125_do_2F8>0.0 && g_126_do_300<g_309_do_2898 / g_229_do_1E00 )
 {
   g_126_do_300 = g_309_do_2898 / g_229_do_1E00 ;
 }
 if ( g_125_do_2F8>0.0 && g_126_do_300<g_221_do_1A80 / g_229_do_1E00 )
 {
   g_126_do_300 = g_221_do_1A80 / g_229_do_1E00 ;
 }
 if ( g_100_do_230<g_221_do_1A80 * 2.0 / g_229_do_1E00 )
 {
   g_100_do_230 = g_221_do_1A80 * 2.0 / g_229_do_1E00 ;
 }
 if ( g_101_do_238<g_221_do_1A80 * 2.0 / g_229_do_1E00 )
 {
   g_101_do_238 = g_221_do_1A80 * 2.0 / g_229_do_1E00 ;
 }
 if ( g_80_do_198<g_221_do_1A80 * 2.0 / g_229_do_1E00 )
 {
   g_80_do_198 = g_221_do_1A80 * 2.0 / g_229_do_1E00 ;
 }
 if ( g_73_in_17C <  1 )
 {
   g_73_in_17C = 1 ;
 }
 if ( g_74_in_180 <  1 )
 {
   g_74_in_180 = 1 ;
 }
 if ( g_80_do_198<0.1 )
 {
   g_80_do_198 = 0.1 ;
 }
 g_234_in_1E20=g_89_in_1D8 * 60 * 60;
 if ( g_89_in_1D8 >  0 )
 {
   g_302_da_2870=TimeCurrent() + g_234_in_1E20;
 }
 else
 {
   g_302_da_2870 = 0 ;
 }
 if ( Virtual_expiration )
 {
   g_302_da_2870 = 0 ;
 }
 g_320_bo_28E8 = false ;
 g_260_do_2570 = Seconds() ;
 g_319_da_28E0 = TimeCurrent() ;
 g_194_bo_530 = false ;
 g_195_bo_531 = false ;
 g_258_in_2568 = Month() ;
 g_313_da_28B8 = iTime(g_336_st_3130,PERIOD_W1,1) ;
 g_314_da_28C0 = iTime(g_336_st_3130,PERIOD_M1,1) ;
 g_315_da_28C8 = iTime(g_336_st_3130,PERIOD_M1,1) ;
 if ( g_37_do_B8>MaxSpread )
 {
   g_37_do_B8 = MaxSpread ;
 }
 g_257_bo_2565 = false ;
 lizong_11(g_71_in_174); 
 lizong_12(g_71_in_174); 
 g_188_do_508 = NormalizeDouble(g_262_do_2580,g_190_in_518) ;
 g_189_do_510 = NormalizeDouble(g_261_do_2578,g_190_in_518) ;
 g_250_in_2518 = 0 ;
 g_256_bo_2564 = false ;
 g_304_in_287C = (int)(g_125_do_2F8 * 60.0) ;
 g_139_bo_3EC = false ;
 g_303_bo_2878 = true ;
 g_309_do_2898 = MarketInfo(g_336_st_3130,MODE_FREEZELEVEL) * g_337_do_3140 ;
 if ( !(g_171_bo_4BC) )
 {
   g_303_bo_2878 = false ;
 }
 g_191_do_520 = 0.0 ;
 g_201_do_16B8 = 0.0 ;
 g_202_do_16C0 = 0.0 ;
 g_240_bo_1E42 = false ;
 g_299_st_2850 = StringSubstr(g_336_st_3130,6,0) ;
 if ( Risk >  0 )
 {
   g_139_bo_3EC = true ;
 }
  if ( StartLotsRuntime<0.0 )
 {
    StartLotsRuntime = 0.01 ;
 }
 if ( g_141_do_3F8>MarketInfo(g_336_st_3130,MODE_MAXLOT) )
 {
   g_141_do_3F8 = MarketInfo(g_336_st_3130,MODE_MAXLOT) ;
 }
 for (local_4_in = 0 ; local_4_in < g_199_in_16B0 ; local_4_in ++)
 {
   for (local_5_in = 0 ; local_5_in < 2 ; local_5_in ++)
   {
     g_196_do_568_si20si2[local_4_in][local_5_in] = 0.0;
   }
 }
 for (local_6_in = 0 ; local_6_in < g_200_in_16B4 ; local_6_in ++)
 {
   for (local_7_in = 0 ; local_7_in < 3 ; local_7_in ++)
   {
     g_197_do_6DC_si100si3[local_6_in][local_7_in] = 0.0;
   }
 }
 for (local_8_in = 0 ; local_8_in < 100 ; local_8_in ++)
 {
   g_197_do_6DC_si100si3[local_8_in][0] = 0.0;
   g_197_do_6DC_si100si3[local_8_in][1] = 0.0;
 }
 g_305_bo_2880 = false ;
 g_272_do_25C8 = iFractals(g_336_st_3130,0,1,1) ;
 g_273_do_25D0 = iFractals(g_336_st_3130,0,2,1) ;
 g_270_do_25B8 = g_272_do_25C8 ;
 g_271_do_25C0 = g_273_do_25D0 ;
 g_275_do_25E0 = 0.0 ;
 g_231_bo_1E0C = false ;
 g_290_in_262C = Hour() ;
 g_289_in_2628 = 0 ;
 g_252_st_2528=ST1_Comment + "B1";
 g_253_st_2538=ST1_Comment + "B2";
 g_254_st_2548=ST1_Comment + "S1";
 g_255_st_2558=ST1_Comment + "S2";
 g_297_in_2848 = 0 ;
 g_298_in_284C = 0 ;
 g_267_in_25A0 = Hour() ;
 if ( g_67_bo_158 )
 {
   g_86_in_1C8 = 1 ;
   g_278_bo_25F8 = true ;
   g_279_bo_25F9 = true ;
 }
 g_209_do_16F0 = 999.0 ;
 g_210_do_16F8 = 0.0 ;
 g_300_do_2860 = 0.0 ;
 g_301_do_2868 = 0.0 ;
 for (local_9_in = 0 ; local_9_in < 99 ; local_9_in ++)
 {
   g_322_in_2AE0_si99[local_9_in] = 0;
   g_321_in_2920_si99[local_9_in] = 0;
   g_215_da_174C_si99[local_9_in] = iTime(g_336_st_3130,g_71_in_174,1);
    if ( !(g_223_do_1AC4_si99[local_9_in]<StartLotsRuntime) )   continue;
    g_223_do_1AC4_si99[local_9_in] = StartLotsRuntime;
   
 }
 g_216_lo_1A68 = 0 ;
 g_238_bo_1E40 = false ;
 g_239_bo_1E41 = false ;
 if ( g_63_in_140 == 1 )
 {
   g_64_do_148 = 0.0 ;
 }
 g_190_in_518 = (int)MarketInfo(g_336_st_3130,MODE_DIGITS) ;
 g_312_bo_28B0 = false ;
 IsDemo(); 

 if ( tmp_bo_1 == true )
 {
   g_312_bo_28B0 = true ;
 }
 if ( ShowInfoPanel )
 {
   if ( g_152_in_43C == 1 )
   {
     lizong_33(); 
   }
   else
   {
     if ( g_152_in_43C == 2 )
     {
       lizong_34(); 
     }
   }
   lizong_24(); 
   lizong_27(); 
   lizong_29(); 
 }
 return(0); 
 }
//init <<==--------   --------
bool LizardManagedMagic(const long magic)
{
   return __LizardManagedMagicEarly(magic);
}

double LizardDailyScale()
{
   const double previous_daily_open=iOpen(_Symbol,PERIOD_D1,1);
   if(previous_daily_open<=0.0) return 1.0;
   return previous_daily_open/2000.0;
}

double LizardRiskLots()
{
   const double capital=AccountInfoDouble(InpUseEquity
                                          ? ACCOUNT_EQUITY
                                          : ACCOUNT_BALANCE);
   const double step=SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_STEP);
   const double min_lot=SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_MIN);
   const double max_lot=SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_MAX);
   double lots=InpFixLot;

   if(InpLotMode==2)
   {
      const double capital_per_step=MathMax(1.0,InpDollarPer001);
      lots=MathFloor(capital/capital_per_step)*0.01;
      if(step>0.0) lots=MathFloor((lots+1.0e-9)/step)*step;
      lots=MathMax(min_lot,MathMin(max_lot,lots));
      return NormalizeDouble(lots,2);
   }
   if(InpLotMode==0)
   {
      if(step>0.0) lots=MathFloor((lots+1.0e-9)/step)*step;
      lots=MathMax(min_lot,MathMin(max_lot,lots));
      return NormalizeDouble(lots,2);
   }

   const double risk_cash=capital*MathMax(0.0,InpRiskPct)/100.0;
   const double price=SymbolInfoDouble(_Symbol,SYMBOL_ASK);
   double one_lot_loss=0.0;
   if(price<=0.0 ||
      !OrderCalcProfit(ORDER_TYPE_BUY,_Symbol,1.0,price,
                       price-MathMax(1,InpStopLossPts)*_Point,one_lot_loss) ||
      one_lot_loss==0.0)
      return min_lot;

   lots=risk_cash/MathAbs(one_lot_loss);
   if(step>0.0) lots=MathFloor((lots+1.0e-9)/step)*step;
   lots=MathMax(min_lot,MathMin(max_lot,lots));
   return NormalizeDouble(lots,2);
}

double LizardPositionTakeProfit(const long magic,
                                const ENUM_POSITION_TYPE type,
                                const double open_price)
{
   double base_points=0.0;
   double scale_base=2000.0;
   const long suffix=magic-__lizard_magic_base;
   if(suffix==8)  { base_points=3630.0; scale_base=2000.0; }
   if(suffix==9)  { base_points=824.0;  scale_base=2400.0; }
   if(suffix==12) { base_points=927.0;  scale_base=2600.0; }
   if(suffix==13) { base_points=1485.0; scale_base=1900.0; }
   if(suffix==14) { base_points=1522.5; scale_base=2600.0; }
   if(suffix==15) { base_points=1284.0; scale_base=2800.0; }
   const double previous_daily_open=iOpen(_Symbol,PERIOD_D1,1);
   if(base_points<=0.0 || previous_daily_open<=0.0) return 0.0;
   const double distance=base_points*(previous_daily_open/scale_base)*_Point;
   return NormalizeDouble(type==POSITION_TYPE_BUY
                          ? open_price+distance
                          : open_price-distance,_Digits);
}

void LizardPrepareStrategyTrade()
{
   // Lizard uses one common risk and protection model for every enabled zone.
   g_223_do_1AC4_si99[g_328_in_3100]=LizardRiskLots();
   g_100_do_230=(double)MathMax(1,InpStopLossPts);

   // Disable the source strategy-specific management; Lizard manages it below.
   g_103_do_250=0.0;
   g_108_do_278=0.0;
   g_113_do_2A8=0.0;
   g_119_in_2D0=0;
   g_125_do_2F8=0.0;
   g_128_in_314=0;
}

void LizardManageOpenPositions()
{
   const double scale=LizardDailyScale();
   const double trigger_points=(InpTrailMode==0 || InpFixedBE<=0
                                ? 110.0*scale
                                : (double)InpFixedBE);
   const double trail_points=(InpTrailMode==0 || InpFixedTrail<=0
                              ? 121.0*scale
                              : (double)InpFixedTrail);
   const double trigger_distance=trigger_points*_Point;
   const double lock_distance=30.0*scale*_Point;
   const double trail_distance=trail_points*_Point;
   const double first_lock=10.0*_Point;
   const double epsilon=0.5*_Point;

   for(int index=PositionsTotal()-1;index>=0;index--)
   {
      const ulong ticket=PositionGetTicket(index);
      if(ticket==0 || !PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL)!=_Symbol) continue;
      if(!LizardManagedMagic(PositionGetInteger(POSITION_MAGIC))) continue;

      const ENUM_POSITION_TYPE type=(ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
      const double open_price=PositionGetDouble(POSITION_PRICE_OPEN);
      const double current_sl=PositionGetDouble(POSITION_SL);
      const double current_tp=PositionGetDouble(POSITION_TP);
      const long magic=PositionGetInteger(POSITION_MAGIC);
      const double desired_tp=LizardPositionTakeProfit(magic,type,open_price);
      const double market_price=(type==POSITION_TYPE_BUY
                                 ? SymbolInfoDouble(_Symbol,SYMBOL_BID)
                                 : SymbolInfoDouble(_Symbol,SYMBOL_ASK));
      const bool trigger=(type==POSITION_TYPE_BUY
                          ? market_price>=open_price+trigger_distance
                          : market_price<=open_price-trigger_distance);
      if(!trigger) continue;

      const double first_sl=NormalizeDouble(type==POSITION_TYPE_BUY
                                            ? open_price+first_lock
                                            : open_price-first_lock,_Digits);
      const bool first_stage=(current_tp<=0.0 ||
                              (type==POSITION_TYPE_BUY
                               ? current_sl<first_sl-epsilon
                               : current_sl>first_sl+epsilon));
      if(first_stage)
      {
         MQL4_OrderModify((long)ticket,open_price,first_sl,desired_tp,0);
         continue;
      }

      const double locked_sl=(type==POSITION_TYPE_BUY
                              ? open_price+lock_distance
                              : open_price-lock_distance);
      const double trailed_sl=(type==POSITION_TYPE_BUY
                               ? market_price-trail_distance
                               : market_price+trail_distance);
      const double desired_sl=NormalizeDouble(type==POSITION_TYPE_BUY
                                              ? MathMax(locked_sl,trailed_sl)
                                              : MathMin(locked_sl,trailed_sl),_Digits);
      const bool tighter=(type==POSITION_TYPE_BUY
                          ? desired_sl>current_sl+epsilon
                          : desired_sl<current_sl-epsilon);
      if(tighter)
         MQL4_OrderModify((long)ticket,open_price,desired_sl,current_tp,0);
   }
}

 void OnTick()
 {
  UpdateReaperVisuals();
  LizardManageOpenPositions();
  if(!LizardEntryWindowOpen())
     LizardClearManagedPendingOrders();
  LizardEnforceZoneSessions();
  bool      local_1_bo;
  double    local_2_do;
  double    local_3_do;
  bool      local_4_bo;
  MqlDateTime local_5_a_129;
  MqlDateTime local_6_a_129;
//----- -----
 bool       tmp_bo_1;
 double     tmp_do_2;
 double     tmp_do_3;
 int        tmp_in_4;
 double     tmp_do_5;
 double     tmp_do_6;
 int        tmp_in_7;
 double     tmp_do_8;
 double     tmp_do_9;
 int        tmp_in_10;
 double     tmp_do_11;
 double     tmp_do_12;
 int        tmp_in_13;
 double     tmp_do_14;
 double     tmp_do_15;
 int        tmp_in_16;
 double     tmp_do_17;
 double     tmp_do_18;
 int        tmp_in_19;
 double     tmp_do_20;
 double     tmp_do_21;
 int        tmp_in_22;
 double     tmp_do_23;
 double     tmp_do_24;
 int        tmp_in_25;
 double     tmp_do_26;
 double     tmp_do_27;
 int        tmp_in_28;

 g_401_do_6AD0 = AccountInfoDouble(ACCOUNT_BALANCE) ;
 if ( UseEquity )
 {
   g_401_do_6AD0 = AccountInfoDouble(ACCOUNT_EQUITY) ;
 }
 if ( ForceBalanceToUse>0.0 )
 {
   g_401_do_6AD0 = ForceBalanceToUse ;
 }
 if ( OnlyUp && g_402_do_6AD8>g_401_do_6AD0 )
 {
   g_401_do_6AD0 = g_402_do_6AD8 ;
 }
 if ( g_401_do_6AD0>g_402_do_6AD8 )
 {
   g_402_do_6AD8 = g_401_do_6AD0 ;
 }
 if ( FakeOutFilter == 0 )
 {
   g_53_bo_11C = false ;
   g_57_bo_12C = false ;
   g_61_bo_13C = false ;
 }
 else
 {
   if ( FakeOutFilter == 1 )
   {
     g_53_bo_11C = true ;
     g_57_bo_12C = false ;
     g_61_bo_13C = false ;
   }
   else
   {
     if ( FakeOutFilter == 2 )
     {
       g_53_bo_11C = true ;
       g_57_bo_12C = true ;
       g_61_bo_13C = false ;
     }
     else
     {
       if ( FakeOutFilter == 3 )
       {
         g_53_bo_11C = true ;
         g_57_bo_12C = true ;
         g_61_bo_13C = true ;
       }
     }
   }
 }
 local_1_bo = false ;
 if ( lizong_48() )
 {
   g_395_in_6760 = Broker_GMT_OFFSET_Summer ;
    if ( ( !(g_392_bo_675C) || !(g_394_bo_675E) ) && UseExternalGMTSync && AutoGMT && !(local_1_bo) )
   {
     g_392_bo_675C = true ;
     g_393_bo_675D = true ;
     g_396_in_6764 = lizong_47() ;
     if ( g_396_in_6764 == 999 )
     {
       Print("GMT_Offset wrongly detected.  Trying againg!"); 
       Sleep(2000); 
       g_396_in_6764 = lizong_47() ;
     }
     if ( g_396_in_6764 == 999 )
     {
       Print("GMT_Offset still wrong.  Using VPS time for GMT detection!"); 
     }
     g_394_bo_675E = true ;
     local_1_bo = true ;
     Print("DST_US on"); 
   }
 }
 else
 {
   g_395_in_6760 = Broker_GMT_OFFSET_Winter ;
    if ( ( g_392_bo_675C || !(g_394_bo_675E) ) && UseExternalGMTSync && AutoGMT && !(local_1_bo) )
   {
     g_392_bo_675C = false ;
     g_393_bo_675D = false ;
     g_396_in_6764 = lizong_47() ;
     if ( g_396_in_6764 == 999 )
     {
       Print("GMT_Offset wrongly detected.  Trying againg!"); 
       Sleep(2000); 
       g_396_in_6764 = lizong_47() ;
     }
     if ( g_396_in_6764 == 999 )
     {
       Print("GMT_Offset still wrong.  Using VPS time for GMT detection!"); 
     }
     g_394_bo_675E = true ;
     local_1_bo = true ;
     Print("DST_US off"); 
   }
 }
 TimeToStruct(StringToTime(string(TimeYear(TimeCurrent())) + ".03.31 01:00"),local_5_a_129); 
 TimeToStruct(StringToTime(string(TimeYear(TimeCurrent())) + ".10.31 02:00"),local_6_a_129); 
 if ( TimeDayOfYear(TimeCurrent()) >  TimeDayOfYear(StringToTime(string(TimeYear(TimeCurrent())) + ".03.31 01:00") - local_5_a_129.day_of_week * 86400) && TimeDayOfYear(TimeCurrent()) <  TimeDayOfYear(StringToTime(string(TimeYear(TimeCurrent())) + ".10.31 02:00") - local_6_a_129.day_of_week * 86400) )
 {
   tmp_bo_1 = true;
 }
 else
 {
   tmp_bo_1 = false;
 }
 if ( tmp_bo_1 )
 {
    if ( ( !(g_393_bo_675D) || !(g_394_bo_675E) ) && UseExternalGMTSync && AutoGMT && !(local_1_bo) )
   {
     g_393_bo_675D = true ;
     g_396_in_6764 = lizong_47() ;
     if ( g_396_in_6764 == 999 )
     {
       Print("GMT_Offset wrongly detected.  Trying againg!"); 
       Sleep(2000); 
       g_396_in_6764 = lizong_47() ;
     }
     if ( g_396_in_6764 == 999 )
     {
       Print("GMT_Offset still wrong.  Using VPS time for GMT detection!"); 
     }
     g_394_bo_675E = true ;
     local_1_bo = true ;
     Print("DST_EU on"); 
   }
 }
 else
 {
    if ( ( g_393_bo_675D || !(g_394_bo_675E) ) && UseExternalGMTSync && AutoGMT && !(local_1_bo) )
   {
     g_393_bo_675D = false ;
     g_396_in_6764 = lizong_47() ;
     if ( g_396_in_6764 == 999 )
     {
       Print("GMT_Offset wrongly detected.  Trying againg!"); 
       Sleep(2000); 
       g_396_in_6764 = lizong_47() ;
     }
     if ( g_396_in_6764 == 999 )
     {
       Print("GMT_Offset still wrong.  Using VPS time for GMT detection!"); 
     }
     g_394_bo_675E = true ;
     local_1_bo = true ;
     Print("DST_EU off"); 
   }
 }
  if ( UseExternalGMTSync && AutoGMT && MQLInfoInteger(MQL_TESTER) != 1 )
 {
   if ( g_396_in_6764 != 999 )
   {
     g_390_da_5DC0=TimeCurrent() - g_396_in_6764 * 3600;
   }
   else
   {
     g_390_da_5DC0 = TimeGMT() ;
   }
 }
 else
 {
   g_390_da_5DC0=TimeCurrent() - g_395_in_6760 * 3600;
 }
 if ( TradeFrequency == 5 && Risk == 1234 )
 {
   local_2_do = lizong_36(AccountInfoDouble(ACCOUNT_BALANCE)) ;
   local_3_do = MaxAllowedDD / 100.0 * local_2_do ;
   if ( local_3_do>g_388_in_5DB4 )
   {
     g_19_in_9C = 3 ;
   }
   else
   {
     if ( local_3_do>g_387_in_5DB0 )
     {
       g_19_in_9C = 2 ;
     }
     else
     {
       if ( local_3_do>g_386_in_5DAC )
       {
         g_19_in_9C = 1 ;
       }
       else
       {
         g_19_in_9C = 0 ;
       }
     }
   }
 }
 else
 {
   g_19_in_9C = TradeFrequency ;
 }
 if ( g_19_in_9C == 0 )
 {
   g_27_bo_A7 = false ;
   g_31_bo_AB = false ;
   g_28_bo_A8 = false ;
   g_33_bo_AD = false ;
   g_34_bo_AE = false ;
   g_32_bo_AC = false ;
   g_398_do_6770 = 2.4 ;
   if ( UseVariableValues )
   {
     g_398_do_6770 = 3.0 ;
   }
 }
 else
 {
   if ( g_19_in_9C == 1 )
   {
     g_27_bo_A7 = true ;
     g_31_bo_AB = true ;
     g_28_bo_A8 = false ;
     g_33_bo_AD = false ;
     g_34_bo_AE = false ;
     g_32_bo_AC = false ;
     g_398_do_6770 = 3.4 ;
     if ( UseVariableValues )
     {
       g_398_do_6770 = 4.0 ;
     }
   }
   else
   {
     if ( g_19_in_9C == 2 )
     {
       g_27_bo_A7 = true ;
       g_31_bo_AB = true ;
       g_28_bo_A8 = true ;
       g_33_bo_AD = true ;
       g_34_bo_AE = false ;
       g_32_bo_AC = false ;
       g_398_do_6770 = 4.1 ;
       if ( UseVariableValues )
       {
         g_398_do_6770 = 5.0 ;
       }
     }
     else
     {
       if ( g_19_in_9C == 3 )
       {
         g_27_bo_A7 = true ;
         g_31_bo_AB = true ;
         g_28_bo_A8 = true ;
         g_33_bo_AD = true ;
         g_34_bo_AE = true ;
         g_32_bo_AC = false ;
         g_398_do_6770 = 4.8 ;
         if ( UseVariableValues )
         {
           g_398_do_6770 = 5.6 ;
         }
       }
       else
       {
         if ( g_19_in_9C == 4 )
         {
           g_27_bo_A7 = true ;
           g_31_bo_AB = true ;
           g_28_bo_A8 = true ;
           g_33_bo_AD = true ;
           g_34_bo_AE = true ;
           g_32_bo_AC = true ;
           g_398_do_6770 = 5.1 ;
           if ( UseVariableValues )
           {
             g_398_do_6770 = 6.0 ;
           }
         }
         else
         {
           if ( g_19_in_9C == 6 )
           {
             g_20_bo_A0 = RunStrat1 ;
             g_23_bo_A3 = RunStrat2 ;
             g_26_bo_A6 = RunStrat3 ;
             g_27_bo_A7 = RunStrat4 ;
             g_31_bo_AB = RunStrat5 ;
             g_28_bo_A8 = RunStrat6 ;
             g_33_bo_AD = RunStrat7 ;
             g_34_bo_AE = RunStrat8 ;
             g_32_bo_AC = RunStrat9 ;
           }
         }
       }
     }
   }
 }
 if ( iBars(g_336_st_3130,PERIOD_D1) != g_383_in_5D9C )
 {
   g_383_in_5D9C = iBars(g_336_st_3130,PERIOD_D1) ;
   g_382_bo_5D98 = false ;
   g_384_do_5DA0 = 0.0 ;
 }
 if ( PropFirmMaxDailyDD>0.0 )
 {
   lizong_46(); 
 }
 if ( g_382_bo_5D98 || !(g_380_bo_5D90) )   return;
 local_4_bo = false ;
 if ( g_399_da_6778 != iTime(g_336_st_3130,PERIOD_H1,1) )
 {
   local_4_bo = true ;
   g_399_da_6778 = iTime(g_336_st_3130,PERIOD_H1,1) ;
 }
 if ( ( StringFind(Symbol(),"XAUUSD",0) >= 0 || StringFind(Symbol(),"xauusd",0) >= 0 || StringFind(Symbol(),"GOLD",0) >= 0 || StringFind(Symbol(),"GLD",0) >= 0 || StringFind(Symbol(),"gold",0) >= 0 || StringFind(Symbol(),"Gold",0) >= 0 ) )
 {
   g_336_st_3130 = Symbol() ;
   if ( g_20_bo_A0 )
   {
     lizong_37(); 
     lizong_6(0); 
     lizong_7(0); 
     if ( local_4_bo )
     {
       if ( MQLInfoInteger(MQL_TESTER) == 1 && !(UpdateInfoTesting) )
       {
         tmp_do_2 = 0.0;
       }
       else
       {
         tmp_do_3 = 0.0;
         g_343_in_372C_si99[g_328_in_3100] = 0;
         for (tmp_in_4 = HistoryTotal() ; tmp_in_4 >= 0 ; tmp_in_4=tmp_in_4 - 1)
         {
           if ( OrderSelect(tmp_in_4,0,1) != true || OrderSymbol() != g_336_st_3130 || OrderMagicNumber() != g_93_in_1F0 )   continue;
           
           if ( ( OrderType() != 0 && OrderType() != 1 ) )   continue;
           g_343_in_372C_si99[g_328_in_3100] ++;
           tmp_do_3 = tmp_do_3 + OrderProfit() + OrderSwap() + OrderCommission();
           
         }
         tmp_do_2 = tmp_do_3;
       }
       g_400_do_67B4_si99[0] = tmp_do_2;
       if ( g_400_do_67B4_si99[0]!=0.0 && g_343_in_372C_si99[0] >  0 )
       {
         g_345_do_3AAC_si99[0] = g_400_do_67B4_si99[0] / g_343_in_372C_si99[0];
       }
     }
   }
   if ( g_27_bo_A7 )
   {
     lizong_38(); 
     lizong_6(3); 
     lizong_7(3); 
     if ( local_4_bo )
     {
       if ( MQLInfoInteger(MQL_TESTER) == 1 && !(UpdateInfoTesting) )
       {
         tmp_do_5 = 0.0;
       }
       else
       {
         tmp_do_6 = 0.0;
         g_343_in_372C_si99[g_328_in_3100] = 0;
         for (tmp_in_7 = HistoryTotal() ; tmp_in_7 >= 0 ; tmp_in_7=tmp_in_7 - 1)
         {
           if ( OrderSelect(tmp_in_7,0,1) != true || OrderSymbol() != g_336_st_3130 || OrderMagicNumber() != g_93_in_1F0 )   continue;
           
           if ( ( OrderType() != 0 && OrderType() != 1 ) )   continue;
           g_343_in_372C_si99[g_328_in_3100] ++;
           tmp_do_6 = tmp_do_6 + OrderProfit() + OrderSwap() + OrderCommission();
           
         }
         tmp_do_5 = tmp_do_6;
       }
       g_400_do_67B4_si99[3] = tmp_do_5;
       if ( g_400_do_67B4_si99[3]!=0.0 && g_343_in_372C_si99[3] >  0 )
       {
         g_345_do_3AAC_si99[3] = g_400_do_67B4_si99[3] / g_343_in_372C_si99[3];
       }
     }
   }
   if ( g_23_bo_A3 )
   {
     lizong_39(); 
     lizong_6(1); 
     lizong_7(1); 
     if ( local_4_bo )
     {
       if ( MQLInfoInteger(MQL_TESTER) == 1 && !(UpdateInfoTesting) )
       {
         tmp_do_8 = 0.0;
       }
       else
       {
         tmp_do_9 = 0.0;
         g_343_in_372C_si99[g_328_in_3100] = 0;
         for (tmp_in_10 = HistoryTotal() ; tmp_in_10 >= 0 ; tmp_in_10=tmp_in_10 - 1)
         {
           if ( OrderSelect(tmp_in_10,0,1) != true || OrderSymbol() != g_336_st_3130 || OrderMagicNumber() != g_93_in_1F0 )   continue;
           
           if ( ( OrderType() != 0 && OrderType() != 1 ) )   continue;
           g_343_in_372C_si99[g_328_in_3100] ++;
           tmp_do_9 = tmp_do_9 + OrderProfit() + OrderSwap() + OrderCommission();
           
         }
         tmp_do_8 = tmp_do_9;
       }
       g_400_do_67B4_si99[1] = tmp_do_8;
       if ( g_400_do_67B4_si99[1]!=0.0 && g_343_in_372C_si99[1] >  0 )
       {
         g_345_do_3AAC_si99[1] = g_400_do_67B4_si99[1] / g_343_in_372C_si99[1];
       }
     }
   }
   if ( g_26_bo_A6 )
   {
     lizong_40(); 
     lizong_6(2); 
     lizong_7(2); 
     if ( local_4_bo )
     {
       if ( MQLInfoInteger(MQL_TESTER) == 1 && !(UpdateInfoTesting) )
       {
         tmp_do_11 = 0.0;
       }
       else
       {
         tmp_do_12 = 0.0;
         g_343_in_372C_si99[g_328_in_3100] = 0;
         for (tmp_in_13 = HistoryTotal() ; tmp_in_13 >= 0 ; tmp_in_13=tmp_in_13 - 1)
         {
           if ( OrderSelect(tmp_in_13,0,1) != true || OrderSymbol() != g_336_st_3130 || OrderMagicNumber() != g_93_in_1F0 )   continue;
           
           if ( ( OrderType() != 0 && OrderType() != 1 ) )   continue;
           g_343_in_372C_si99[g_328_in_3100] ++;
           tmp_do_12 = tmp_do_12 + OrderProfit() + OrderSwap() + OrderCommission();
           
         }
         tmp_do_11 = tmp_do_12;
       }
       g_400_do_67B4_si99[2] = tmp_do_11;
       if ( g_400_do_67B4_si99[2]!=0.0 && g_343_in_372C_si99[2] >  0 )
       {
         g_345_do_3AAC_si99[2] = g_400_do_67B4_si99[2] / g_343_in_372C_si99[2];
       }
     }
   }
   if ( g_28_bo_A8 )
   {
     lizong_41(); 
     lizong_6(5); 
     lizong_7(5); 
     if ( local_4_bo )
     {
       if ( MQLInfoInteger(MQL_TESTER) == 1 && !(UpdateInfoTesting) )
       {
         tmp_do_14 = 0.0;
       }
       else
       {
         tmp_do_15 = 0.0;
         g_343_in_372C_si99[g_328_in_3100] = 0;
         for (tmp_in_16 = HistoryTotal() ; tmp_in_16 >= 0 ; tmp_in_16=tmp_in_16 - 1)
         {
           if ( OrderSelect(tmp_in_16,0,1) != true || OrderSymbol() != g_336_st_3130 || OrderMagicNumber() != g_93_in_1F0 )   continue;
           
           if ( ( OrderType() != 0 && OrderType() != 1 ) )   continue;
           g_343_in_372C_si99[g_328_in_3100] ++;
           tmp_do_15 = tmp_do_15 + OrderProfit() + OrderSwap() + OrderCommission();
           
         }
         tmp_do_14 = tmp_do_15;
       }
       g_400_do_67B4_si99[5] = tmp_do_14;
       if ( g_400_do_67B4_si99[5]!=0.0 && g_343_in_372C_si99[5] >  0 )
       {
         g_345_do_3AAC_si99[5] = g_400_do_67B4_si99[5] / g_343_in_372C_si99[5];
       }
     }
   }
   if ( g_31_bo_AB )
   {
     lizong_42(); 
     lizong_6(4); 
     lizong_7(4); 
     if ( local_4_bo )
     {
       if ( MQLInfoInteger(MQL_TESTER) == 1 && !(UpdateInfoTesting) )
       {
         tmp_do_17 = 0.0;
       }
       else
       {
         tmp_do_18 = 0.0;
         g_343_in_372C_si99[g_328_in_3100] = 0;
         for (tmp_in_19 = HistoryTotal() ; tmp_in_19 >= 0 ; tmp_in_19=tmp_in_19 - 1)
         {
           if ( OrderSelect(tmp_in_19,0,1) != true || OrderSymbol() != g_336_st_3130 || OrderMagicNumber() != g_93_in_1F0 )   continue;
           
           if ( ( OrderType() != 0 && OrderType() != 1 ) )   continue;
           g_343_in_372C_si99[g_328_in_3100] ++;
           tmp_do_18 = tmp_do_18 + OrderProfit() + OrderSwap() + OrderCommission();
           
         }
         tmp_do_17 = tmp_do_18;
       }
       g_400_do_67B4_si99[4] = tmp_do_17;
       if ( g_400_do_67B4_si99[4]!=0.0 && g_343_in_372C_si99[4] >  0 )
       {
         g_345_do_3AAC_si99[4] = g_400_do_67B4_si99[4] / g_343_in_372C_si99[4];
       }
     }
   }
   if ( g_32_bo_AC )
   {
     lizong_43(); 
     lizong_6(8); 
     lizong_7(8); 
     if ( local_4_bo )
     {
       if ( MQLInfoInteger(MQL_TESTER) == 1 && !(UpdateInfoTesting) )
       {
         tmp_do_20 = 0.0;
       }
       else
       {
         tmp_do_21 = 0.0;
         g_343_in_372C_si99[g_328_in_3100] = 0;
         for (tmp_in_22 = HistoryTotal() ; tmp_in_22 >= 0 ; tmp_in_22=tmp_in_22 - 1)
         {
           if ( OrderSelect(tmp_in_22,0,1) != true || OrderSymbol() != g_336_st_3130 || OrderMagicNumber() != g_93_in_1F0 )   continue;
           
           if ( ( OrderType() != 0 && OrderType() != 1 ) )   continue;
           g_343_in_372C_si99[g_328_in_3100] ++;
           tmp_do_21 = tmp_do_21 + OrderProfit() + OrderSwap() + OrderCommission();
           
         }
         tmp_do_20 = tmp_do_21;
       }
       g_400_do_67B4_si99[8] = tmp_do_20;
       if ( g_400_do_67B4_si99[8]!=0.0 && g_343_in_372C_si99[8] >  0 )
       {
         g_345_do_3AAC_si99[8] = g_400_do_67B4_si99[8] / g_343_in_372C_si99[8];
       }
     }
   }
   if ( g_33_bo_AD )
   {
     lizong_44(); 
     lizong_6(6); 
     lizong_7(6); 
     if ( local_4_bo )
     {
       if ( MQLInfoInteger(MQL_TESTER) == 1 && !(UpdateInfoTesting) )
       {
         tmp_do_23 = 0.0;
       }
       else
       {
         tmp_do_24 = 0.0;
         g_343_in_372C_si99[g_328_in_3100] = 0;
         for (tmp_in_25 = HistoryTotal() ; tmp_in_25 >= 0 ; tmp_in_25=tmp_in_25 - 1)
         {
           if ( OrderSelect(tmp_in_25,0,1) != true || OrderSymbol() != g_336_st_3130 || OrderMagicNumber() != g_93_in_1F0 )   continue;
           
           if ( ( OrderType() != 0 && OrderType() != 1 ) )   continue;
           g_343_in_372C_si99[g_328_in_3100] ++;
           tmp_do_24 = tmp_do_24 + OrderProfit() + OrderSwap() + OrderCommission();
           
         }
         tmp_do_23 = tmp_do_24;
       }
       g_400_do_67B4_si99[6] = tmp_do_23;
       if ( g_400_do_67B4_si99[6]!=0.0 && g_343_in_372C_si99[6] >  0 )
       {
         g_345_do_3AAC_si99[6] = g_400_do_67B4_si99[6] / g_343_in_372C_si99[6];
       }
     }
   }
   if ( g_34_bo_AE )
   {
     lizong_45(); 
     lizong_6(7); 
     lizong_7(7); 
     if ( local_4_bo )
     {
       if ( MQLInfoInteger(MQL_TESTER) == 1 && !(UpdateInfoTesting) )
       {
         tmp_do_26 = 0.0;
       }
       else
       {
         tmp_do_27 = 0.0;
         g_343_in_372C_si99[g_328_in_3100] = 0;
         for (tmp_in_28 = HistoryTotal() ; tmp_in_28 >= 0 ; tmp_in_28=tmp_in_28 - 1)
         {
           if ( OrderSelect(tmp_in_28,0,1) != true || OrderSymbol() != g_336_st_3130 || OrderMagicNumber() != g_93_in_1F0 )   continue;
           
           if ( ( OrderType() != 0 && OrderType() != 1 ) )   continue;
           g_343_in_372C_si99[g_328_in_3100] ++;
           tmp_do_27 = tmp_do_27 + OrderProfit() + OrderSwap() + OrderCommission();
           
         }
         tmp_do_26 = tmp_do_27;
       }
       g_400_do_67B4_si99[7] = tmp_do_26;
       if ( g_400_do_67B4_si99[7]!=0.0 && g_343_in_372C_si99[7] >  0 )
       {
         g_345_do_3AAC_si99[7] = g_400_do_67B4_si99[7] / g_343_in_372C_si99[7];
       }
     }
   }
 }
 else
 {
   g_336_st_3130 = Symbol() ;
   lizong_7(0); 
 }
 lizong_27(); 
 if ( iTime(Symbol(),PERIOD_M5,1) != g_379_da_5D88 )
 {
   g_379_da_5D88 = iTime(Symbol(),PERIOD_M5,1) ;
   lizong_28(); 
   lizong_29(); 
 }
 g_381_in_5D94 ++;
 if ( g_381_in_5D94 < 2 )   return;
 g_318_do_28D8 = AccountBalance() ;
 g_381_in_5D94 = 0 ;
 }
//OnTick <<==--------   --------
 int deinit()
 {
 lizong_26(); 
 return(0); 
 }
//deinit <<==--------   --------
 void lizong_6( int arg_0_in)
 {
 g_328_in_3100 = arg_0_in ;
 g_337_do_3140 = SymbolInfoDouble(g_336_st_3130,16) ;
 g_229_do_1E00 = g_337_do_3140 ;
 if ( ( MarketInfo(g_336_st_3130,MODE_DIGITS)==3.0 || MarketInfo(g_336_st_3130,MODE_DIGITS)==5.0 ) )
 {
   g_229_do_1E00 = g_337_do_3140 * 10.0 ;
 }
 if ( SymbolInfoInteger(g_336_st_3130,17) == 0x1 )
 {
   g_229_do_1E00 = g_337_do_3140 / 10.0 ;
 }
 g_190_in_518 = (int)MarketInfo(g_336_st_3130,MODE_DIGITS) ;
 g_1_do_0 = MarketInfo(g_336_st_3130,MODE_ASK) - MarketInfo(g_336_st_3130,MODE_BID) ;
 g_221_do_1A80 = MarketInfo(g_336_st_3130,MODE_STOPLEVEL) * g_337_do_3140 ;
 g_309_do_2898 = MarketInfo(g_336_st_3130,MODE_FREEZELEVEL) * g_337_do_3140 ;
 g_234_in_1E20=g_89_in_1D8 * 60 * 60;
 if ( g_89_in_1D8 >  0 )
 {
   g_302_da_2870=TimeCurrent() + g_234_in_1E20;
 }
 else
 {
   g_302_da_2870 = 0 ;
 }
 if ( Virtual_expiration )
 {
   g_302_da_2870 = 0 ;
 }
 g_9_do_60 = 1.0 ;
 if ( !(UseVariableValues) )   return;
 
 if ( g_7_do_50>0.0 )
 {
   g_8_do_58 = iOpen(g_336_st_3130,PERIOD_D1,1) / g_7_do_50 ;
 }
 else
 {
   g_8_do_58 = 1.0 ;
 }
 if ( AdjustLotsizeToVariableValues )
 {
   g_9_do_60 = 1.0 / g_8_do_58 ;
 }
 else
 {
   g_9_do_60 = 1.0 ;
 }
 g_80_do_198 = g_80_do_198 * g_8_do_58 ;
 g_83_do_1B0 = NormalizeDouble(g_83_do_1B0 * g_8_do_58,0) ;
 g_84_do_1B8 = NormalizeDouble(g_84_do_1B8 * g_8_do_58,0) ;
 g_100_do_230 = g_100_do_230 * g_8_do_58 ;
 g_101_do_238 = g_101_do_238 * g_8_do_58 ;
 g_103_do_250 = g_103_do_250 * g_8_do_58 ;
 g_104_do_258 = g_104_do_258 * g_8_do_58 ;
 g_105_do_260 = g_105_do_260 * g_8_do_58 ;
 g_108_do_278 = g_108_do_278 * g_8_do_58 ;
 g_109_do_280 = g_109_do_280 * g_8_do_58 ;
 g_113_do_2A8 = g_113_do_2A8 * g_8_do_58 ;
 g_114_do_2B0 = g_114_do_2B0 * g_8_do_58 ;
 LizardPrepareStrategyTrade();
 }
//lizong_6 <<==--------   --------
 int lizong_7( int arg_0_in)
 {
  bool      local_2_bo;
  datetime  local_3_lo;
  int       local_4_in;
  int       local_5_in;
  string    local_6_st;
  datetime  local_7_da;
  int       local_8_in;
  int       local_9_in;
//----- -----
 int        tmp_in_1;
 int        tmp_in_2;
 int        tmp_in_3;
 int        tmp_in_4;
 int        tmp_in_5;
 int        tmp_in_6;
 int        tmp_in_7;
 int        tmp_in_8;
 int        tmp_in_9;
 int        tmp_in_10;
 int        tmp_in_11;
 int        tmp_in_12;
 int        tmp_in_13;
 int        tmp_in_14;
 int        tmp_in_15;
 int        tmp_in_16;
 int        tmp_in_17;
 int        tmp_in_18;
 int        tmp_in_19;
 int        tmp_in_20;
 int        tmp_in_21;
 int        tmp_in_22;
 int        tmp_in_23;
 int        tmp_in_24;
 int        tmp_in_25;
 int        tmp_in_26;
 int        tmp_in_27;
 int        tmp_in_28;
 int        tmp_in_29;
 int        tmp_in_30;
 int        tmp_in_31;
 int        tmp_in_32;
 int        tmp_in_33;
 int        tmp_in_34;
 int        tmp_in_35;
 int        tmp_in_36;
 int        tmp_in_37;
 int        tmp_in_38;
 int        tmp_in_39;
 int        tmp_in_40;
 int        tmp_in_41;
 int        tmp_in_42;
 int        tmp_in_43;
 int        tmp_in_44;
 int        tmp_in_45;
 int        tmp_in_46;
 int        tmp_in_47;
 int        tmp_in_48;
 int        tmp_in_49;
 int        tmp_in_50;
 int        tmp_in_51;
 int        tmp_in_52;
 int        tmp_in_53;
 int        tmp_in_54;
 int        tmp_in_55;
 int        tmp_in_56;
 int        tmp_in_57;
 int        tmp_in_58;
 int        tmp_in_59;
 int        tmp_in_60;
 int        tmp_in_61;
 int        tmp_in_62;
 int        tmp_in_63;
 int        tmp_in_64;
 int        tmp_in_65;
 int        tmp_in_66;
 int        tmp_in_67;
 int        tmp_in_68;
 int        tmp_in_69;
 int        tmp_in_70;
 int        tmp_in_71;
 int        tmp_in_72;
 int        tmp_in_73;
 int        tmp_in_74;
 int        tmp_in_75;
 int        tmp_in_76;
 int        tmp_in_77;
 int        tmp_in_78;
 int        tmp_in_79;
 int        tmp_in_80;
 int        tmp_in_81;
 int        tmp_in_82;
 int        tmp_in_83;
 int        tmp_in_84;
 int        tmp_in_85;
 int        tmp_in_86;
 int        tmp_in_87;
 int        tmp_in_88;
 int        tmp_in_89;
 double     tmp_do_90;
 long       tmp_lo_91;
 int        tmp_in_92;
 long       tmp_lo_93;
 int        tmp_in_94;
 int        tmp_in_95;
 int        tmp_in_96;
 double     tmp_do_97;
 long       tmp_lo_98;
 int        tmp_in_99;
 long       tmp_lo_100;
 int        tmp_in_101;
 int        tmp_in_102;
 int        tmp_in_103;
 int        tmp_in_104;
 int        tmp_in_105;
 bool       tmp_bo_106;
 int        tmp_in_107;
 int        tmp_in_108;
 bool       tmp_bo_109;
 int        tmp_in_110;
 long       tmp_lo_111;
 int        tmp_in_112;
 long       tmp_lo_113;
 string     tmp_st_114;
 int        tmp_in_115;
 int        tmp_in_116;
 int        tmp_in_117;
 int        tmp_in_118;

 g_328_in_3100 = arg_0_in ;
 local_2_bo = false ;
 
 if ( g_81_do_1A0>0.0 )
 {
   g_80_do_198 = g_81_do_1A0 / 100.0 * MarketInfo(g_336_st_3130,MODE_ASK) * 10.0 ;
 }
 if ( g_99_in_22C == 0 )
 {
   if ( lizong_18() )
   {
     local_2_bo = true ;
   }
   if ( lizong_19() )
   {
     local_2_bo = true ;
   }
   if ( local_2_bo )
   {
     return(0); 
   }
 }
 else
 {
   if ( g_321_in_2920_si99[g_328_in_3100] != iBars(g_336_st_3130,g_99_in_22C) )
   {
     g_321_in_2920_si99[g_328_in_3100] = iBars(g_336_st_3130,g_99_in_22C);
     if ( lizong_18() )
     {
       local_2_bo = true ;
     }
     if ( lizong_19() )
     {
       local_2_bo = true ;
     }
     if ( local_2_bo )
     {
       return(0); 
     }
   }
 }
 lizong_22(false); 
 if ( !(IsTesting()) && MarketInfo(g_336_st_3130,MODE_TRADEALLOWED)==0.0 )
 {
   if ( !(g_256_bo_2564) )
   {
     Print("Market closed... waiting to continue"); 
   }
   g_256_bo_2564 = true ;
   return(0); 
 }
 if ( g_68_in_15C >  0 && ( ( Hour() == 0 && Minute() < g_68_in_15C ) || (Hour() == 23 && g_68_in_15C >  60 - g_68_in_15C) ) )
 {
   if ( !(g_256_bo_2564) )
   {
     Print("DAYSWITCH -> Market might be closed... waiting " + string(g_68_in_15C) + " minutes before setting order.."); 
   }
   g_256_bo_2564 = true ;
   return(0); 
 }
 g_256_bo_2564 = false ;
 if ( g_171_bo_4BC )
 {
   if ( lizong_20() && g_303_bo_2878 )
   {
     if ( g_173_bo_4C4 )
     {
       lizong_8(); 
     }
     g_303_bo_2878 = false ;
   }
   if ( !(lizong_20()) && !(g_303_bo_2878) )
   {
     Print("ENTERING NON-TRADING HOURS! Closing orders..."); 
     if ( g_173_bo_4C4 )
     {
       for (tmp_in_1 = 0 ; tmp_in_1 < g_200_in_16B4 ; tmp_in_1=tmp_in_1 + 1)
       {
         for (tmp_in_2 = 0 ; tmp_in_2 < 2 ; tmp_in_2=tmp_in_2 + 1)
         {
           g_197_do_6DC_si100si3[tmp_in_1][tmp_in_2] = 0.0;
         }
       }
       tmp_in_3 = 0;
       for (tmp_in_4 = OrdersTotal() ; tmp_in_4 >= 0 ; tmp_in_4=tmp_in_4 - 1)
       {
         if ( OrderSelect(tmp_in_4,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 )   continue;
         
         if ( ( OrderType() != 4 && OrderType() != 5 ) )   continue;
         Print("Storing pending order nr " + string(OrderTicket())); 
         g_197_do_6DC_si100si3[tmp_in_3][1] = OrderType();
         g_197_do_6DC_si100si3[tmp_in_3][0] = OrderOpenPrice();
         g_197_do_6DC_si100si3[tmp_in_3][2] = OrderLots();
         tmp_in_3=tmp_in_3 + 1;
         
       }
     }
     tmp_in_5 = 1;
     for (tmp_in_6 = OrdersTotal() ; tmp_in_6 >= 0 ; tmp_in_6=tmp_in_6 - 1)
     {
       if ( OrderSelect(tmp_in_6,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 4 )   continue;
       OrderDelete(OrderTicket(),0xFFFFFFFF); 
       
     }
     if ( tmp_in_5 == 2 )
     {
       for (tmp_in_7 = OrdersTotal() ; tmp_in_7 >= 0 ; tmp_in_7=tmp_in_7 - 1)
       {
         if ( OrderSelect(tmp_in_7,0,0) != true || OrderMagicNumber() != g_96_in_208 || OrderSymbol() != g_336_st_3130 || OrderType() != 4 )   continue;
         OrderDelete(OrderTicket(),0xFFFFFFFF); 
         
       }
     }
     tmp_in_8 = 1;
     for (tmp_in_9 = OrdersTotal() ; tmp_in_9 >= 0 ; tmp_in_9=tmp_in_9 - 1)
     {
       if ( OrderSelect(tmp_in_9,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 5 )   continue;
       OrderDelete(OrderTicket(),0xFFFFFFFF); 
       
     }
     if ( tmp_in_8 == 2 )
     {
       for (tmp_in_10 = OrdersTotal() ; tmp_in_10 >= 0 ; tmp_in_10=tmp_in_10 - 1)
       {
         if ( OrderSelect(tmp_in_10,0,0) != true || OrderMagicNumber() != g_96_in_208 || OrderSymbol() != g_336_st_3130 || OrderType() != 5 )   continue;
         OrderDelete(OrderTicket(),0xFFFFFFFF); 
         
       }
     }
     tmp_in_11 = 2;
     if(1==0) //condition not met
     {
       do
       {
         if ( OrderSelect(1,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 4 )   continue;
         OrderDelete(OrderTicket(),0xFFFFFFFF); 
         
       }
       while( - 1 >= 0);
       
     }
     if ( tmp_in_11 == 2 )
     {
       for (tmp_in_12 = OrdersTotal() ; tmp_in_12 >= 0 ; tmp_in_12=tmp_in_12 - 1)
       {
         if ( OrderSelect(tmp_in_12,0,0) != true || OrderMagicNumber() != g_96_in_208 || OrderSymbol() != g_336_st_3130 || OrderType() != 4 )   continue;
         OrderDelete(OrderTicket(),0xFFFFFFFF); 
         
       }
     }
     tmp_in_13 = 2;
     if(1==0) //condition not met
     {
       do
       {
         if ( OrderSelect(1,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 5 )   continue;
         OrderDelete(OrderTicket(),0xFFFFFFFF); 
         
       }
       while( - 1 >= 0);
       
     }
     if ( tmp_in_13 == 2 )
     {
       for (tmp_in_14 = OrdersTotal() ; tmp_in_14 >= 0 ; tmp_in_14=tmp_in_14 - 1)
       {
         if ( OrderSelect(tmp_in_14,0,0) != true || OrderMagicNumber() != g_96_in_208 || OrderSymbol() != g_336_st_3130 || OrderType() != 5 )   continue;
         OrderDelete(OrderTicket(),0xFFFFFFFF); 
         
       }
     }
     g_303_bo_2878 = true ;
     return(0); 
   }
 }
  if ( UseNewsFilter && EnableNFP_Filter )
 {
   if ( Year() <= 2026 )
   {
     local_3_lo = 0 ;
     for (local_4_in = 0 ; local_4_in < 300 ; local_4_in ++)
     {
       tmp_in_15 = TimeYear(g_391_da_5DFC_si300[local_4_in]);
       if ( tmp_in_15 != Year() )   continue;
       tmp_in_16 = TimeMonth(g_391_da_5DFC_si300[local_4_in]);
       if ( tmp_in_16 != Month() )   continue;
       local_3_lo = g_391_da_5DFC_si300[local_4_in] ;
       break;
       
     }
     local_5_in = 60 ;
     if ( lizong_48() )
     {
       local_5_in = 0 ;
     }
     if ( g_390_da_5DC0 >= local_3_lo - NFP_MinutesBefore * 60 + local_5_in * 60 && g_390_da_5DC0 <= local_3_lo + NFP_MinutesAfter * 60 + local_5_in * 60 )
     {
       if ( NFP_ClosePendingOrders )
       {
         tmp_in_17 = 1;
         for (tmp_in_18 = OrdersTotal() ; tmp_in_18 >= 0 ; tmp_in_18=tmp_in_18 - 1)
         {
           if ( OrderSelect(tmp_in_18,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 4 )   continue;
           OrderDelete(OrderTicket(),0xFFFFFFFF); 
           
         }
         if ( tmp_in_17 == 2 )
         {
           for (tmp_in_19 = OrdersTotal() ; tmp_in_19 >= 0 ; tmp_in_19=tmp_in_19 - 1)
           {
             if ( OrderSelect(tmp_in_19,0,0) != true || OrderMagicNumber() != g_96_in_208 || OrderSymbol() != g_336_st_3130 || OrderType() != 4 )   continue;
             OrderDelete(OrderTicket(),0xFFFFFFFF); 
             
           }
         }
         tmp_in_20 = 1;
         for (tmp_in_21 = OrdersTotal() ; tmp_in_21 >= 0 ; tmp_in_21=tmp_in_21 - 1)
         {
           if ( OrderSelect(tmp_in_21,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 5 )   continue;
           OrderDelete(OrderTicket(),0xFFFFFFFF); 
           
         }
         if ( tmp_in_20 == 2 )
         {
           for (tmp_in_22 = OrdersTotal() ; tmp_in_22 >= 0 ; tmp_in_22=tmp_in_22 - 1)
           {
             if ( OrderSelect(tmp_in_22,0,0) != true || OrderMagicNumber() != g_96_in_208 || OrderSymbol() != g_336_st_3130 || OrderType() != 5 )   continue;
             OrderDelete(OrderTicket(),0xFFFFFFFF); 
             
           }
         }
         tmp_in_23 = 2;
         if(1==0) //condition not met
         {
           do
           {
             if ( OrderSelect(1,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 4 )   continue;
             OrderDelete(OrderTicket(),0xFFFFFFFF); 
             
           }
           while( - 1 >= 0);
           
         }
         if ( tmp_in_23 == 2 )
         {
           for (tmp_in_24 = OrdersTotal() ; tmp_in_24 >= 0 ; tmp_in_24=tmp_in_24 - 1)
           {
             if ( OrderSelect(tmp_in_24,0,0) != true || OrderMagicNumber() != g_96_in_208 || OrderSymbol() != g_336_st_3130 || OrderType() != 4 )   continue;
             OrderDelete(OrderTicket(),0xFFFFFFFF); 
             
           }
         }
         tmp_in_25 = 2;
         if(1==0) //condition not met
         {
           do
           {
             if ( OrderSelect(1,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 5 )   continue;
             OrderDelete(OrderTicket(),0xFFFFFFFF); 
             
           }
           while( - 1 >= 0);
           
         }
         if ( tmp_in_25 == 2 )
         {
           for (tmp_in_26 = OrdersTotal() ; tmp_in_26 >= 0 ; tmp_in_26=tmp_in_26 - 1)
           {
             if ( OrderSelect(tmp_in_26,0,0) != true || OrderMagicNumber() != g_96_in_208 || OrderSymbol() != g_336_st_3130 || OrderType() != 5 )   continue;
             OrderDelete(OrderTicket(),0xFFFFFFFF); 
             
           }
         }
       }
       if ( NFP_CloseOpenTrades )
       {
         for (tmp_in_27 = OrdersTotal() ; tmp_in_27 >= 0 ; tmp_in_27=tmp_in_27 - 1)
         {
           if ( OrderSelect(tmp_in_27,0,0) != true || OrderSymbol() != g_336_st_3130 )   continue;
           tmp_in_28 = OrderMagicNumber();
           tmp_in_29=ST1_MagicNumber + 1;
           if ( tmp_in_28 != tmp_in_29 )
           {
             tmp_in_29 = OrderMagicNumber();
             tmp_in_30=ST1_MagicNumber + 2;
             if ( tmp_in_29 != tmp_in_30 )
             {
               tmp_in_30 = OrderMagicNumber();
               tmp_in_31=ST1_MagicNumber + 3;
               if ( tmp_in_30 != tmp_in_31 )
               {
                 tmp_in_31 = OrderMagicNumber();
                 tmp_in_32=ST1_MagicNumber + 4;
                 if ( tmp_in_31 != tmp_in_32 )
                 {
                   tmp_in_32 = OrderMagicNumber();
                   tmp_in_33=ST1_MagicNumber + 5;
                   if ( tmp_in_32 != tmp_in_33 )
                   {
                     tmp_in_33 = OrderMagicNumber();
                     tmp_in_34=ST1_MagicNumber + 6;
                     if ( tmp_in_33 != tmp_in_34 )
                     {
                       tmp_in_34 = OrderMagicNumber();
                       tmp_in_35=ST1_MagicNumber + 7;
                       if ( tmp_in_34 != tmp_in_35 )
                       {
                         tmp_in_35 = OrderMagicNumber();
                         tmp_in_36=ST1_MagicNumber + 8;
                         if ( tmp_in_35 != tmp_in_36 )
                         {
                           tmp_in_36 = OrderMagicNumber();
                           tmp_in_37=ST1_MagicNumber + 9;
                           if ( tmp_in_36 != tmp_in_37 )
                           {
                             tmp_in_37 = OrderMagicNumber();
                             tmp_in_38=ST1_MagicNumber + 10;
                             if ( tmp_in_37 != tmp_in_38 )
                             {
                               tmp_in_38 = OrderMagicNumber();
                               tmp_in_39=ST1_MagicNumber + 11;
                               if ( tmp_in_38 != tmp_in_39 )
                               {
                                 tmp_in_39 = OrderMagicNumber();
                                 tmp_in_40=ST1_MagicNumber + 12;
                                 if ( tmp_in_39 != tmp_in_40 )
                                 {
                                   tmp_in_40 = OrderMagicNumber();
                                   tmp_in_41=ST1_MagicNumber + 13;
                                   if ( tmp_in_40 != tmp_in_41 )
                                   {
                                     tmp_in_41 = OrderMagicNumber();
                                     tmp_in_42=ST1_MagicNumber + 14;
                                     if ( tmp_in_41 != tmp_in_42 )
                                     {
                                       tmp_in_42 = OrderMagicNumber();
                                       tmp_in_43=ST1_MagicNumber + 15;
                                     if ( tmp_in_42 != tmp_in_43 )   continue;
                                     }
                                   }
                                 }
                               }
                             }
                           }
                         }
                       }
                     }
                   }
                 }
               }
             }
           }
           if ( OrderType() == 0 )
           {
             OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_BID),99999,Red); 
           }
           if ( OrderType() != 1 )   continue;
           OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_ASK),99999,Red); 
           
         }
       }
       if ( !(g_320_bo_28E8) )
       {
         Print("NFP!! deleting trades!!"); 
       }
       g_320_bo_28E8 = true ;
     }
     else
     {
       g_320_bo_28E8 = false ;
     }
   }
   else
   {
     if ( Day() <= 7 && DayOfWeek() == 5 )
     {
       local_6_st = IntegerToString(Year(),0,32) + IntegerToString(Month(),0,32) + IntegerToString(Day(),0,32) + " " + IntegerToString(0x4CE,0,32) ;
       local_7_da = StringToTime(local_6_st) ;
       if ( g_390_da_5DC0 >= local_7_da - NFP_MinutesBefore * 60 && g_390_da_5DC0 <= local_7_da + NFP_MinutesAfter * 60 )
       {
         if ( NFP_ClosePendingOrders )
         {
           tmp_in_44 = 1;
           for (tmp_in_45 = OrdersTotal() ; tmp_in_45 >= 0 ; tmp_in_45=tmp_in_45 - 1)
           {
             if ( OrderSelect(tmp_in_45,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 4 )   continue;
             OrderDelete(OrderTicket(),0xFFFFFFFF); 
             
           }
           if ( tmp_in_44 == 2 )
           {
             for (tmp_in_46 = OrdersTotal() ; tmp_in_46 >= 0 ; tmp_in_46=tmp_in_46 - 1)
             {
               if ( OrderSelect(tmp_in_46,0,0) != true || OrderMagicNumber() != g_96_in_208 || OrderSymbol() != g_336_st_3130 || OrderType() != 4 )   continue;
               OrderDelete(OrderTicket(),0xFFFFFFFF); 
               
             }
           }
           tmp_in_47 = 1;
           for (tmp_in_48 = OrdersTotal() ; tmp_in_48 >= 0 ; tmp_in_48=tmp_in_48 - 1)
           {
             if ( OrderSelect(tmp_in_48,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 5 )   continue;
             OrderDelete(OrderTicket(),0xFFFFFFFF); 
             
           }
           if ( tmp_in_47 == 2 )
           {
             for (tmp_in_49 = OrdersTotal() ; tmp_in_49 >= 0 ; tmp_in_49=tmp_in_49 - 1)
             {
               if ( OrderSelect(tmp_in_49,0,0) != true || OrderMagicNumber() != g_96_in_208 || OrderSymbol() != g_336_st_3130 || OrderType() != 5 )   continue;
               OrderDelete(OrderTicket(),0xFFFFFFFF); 
               
             }
           }
           tmp_in_50 = 2;
           if(1==0) //condition not met
           {
             do
             {
               if ( OrderSelect(1,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 4 )   continue;
               OrderDelete(OrderTicket(),0xFFFFFFFF); 
               
             }
             while( - 1 >= 0);
             
           }
           if ( tmp_in_50 == 2 )
           {
             for (tmp_in_51 = OrdersTotal() ; tmp_in_51 >= 0 ; tmp_in_51=tmp_in_51 - 1)
             {
               if ( OrderSelect(tmp_in_51,0,0) != true || OrderMagicNumber() != g_96_in_208 || OrderSymbol() != g_336_st_3130 || OrderType() != 4 )   continue;
               OrderDelete(OrderTicket(),0xFFFFFFFF); 
               
             }
           }
           tmp_in_52 = 2;
           if(1==0) //condition not met
           {
             do
             {
               if ( OrderSelect(1,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 5 )   continue;
               OrderDelete(OrderTicket(),0xFFFFFFFF); 
               
             }
             while( - 1 >= 0);
             
           }
           if ( tmp_in_52 == 2 )
           {
             for (tmp_in_53 = OrdersTotal() ; tmp_in_53 >= 0 ; tmp_in_53=tmp_in_53 - 1)
             {
               if ( OrderSelect(tmp_in_53,0,0) != true || OrderMagicNumber() != g_96_in_208 || OrderSymbol() != g_336_st_3130 || OrderType() != 5 )   continue;
               OrderDelete(OrderTicket(),0xFFFFFFFF); 
               
             }
           }
         }
         if ( NFP_CloseOpenTrades )
         {
           for (tmp_in_54 = OrdersTotal() ; tmp_in_54 >= 0 ; tmp_in_54=tmp_in_54 - 1)
           {
             if ( OrderSelect(tmp_in_54,0,0) != true || OrderSymbol() != g_336_st_3130 )   continue;
             tmp_in_55 = OrderMagicNumber();
             tmp_in_56=ST1_MagicNumber + 1;
             if ( tmp_in_55 != tmp_in_56 )
             {
               tmp_in_56 = OrderMagicNumber();
               tmp_in_57=ST1_MagicNumber + 2;
               if ( tmp_in_56 != tmp_in_57 )
               {
                 tmp_in_57 = OrderMagicNumber();
                 tmp_in_58=ST1_MagicNumber + 3;
                 if ( tmp_in_57 != tmp_in_58 )
                 {
                   tmp_in_58 = OrderMagicNumber();
                   tmp_in_59=ST1_MagicNumber + 4;
                   if ( tmp_in_58 != tmp_in_59 )
                   {
                     tmp_in_59 = OrderMagicNumber();
                     tmp_in_60=ST1_MagicNumber + 5;
                     if ( tmp_in_59 != tmp_in_60 )
                     {
                       tmp_in_60 = OrderMagicNumber();
                       tmp_in_61=ST1_MagicNumber + 6;
                       if ( tmp_in_60 != tmp_in_61 )
                       {
                         tmp_in_61 = OrderMagicNumber();
                         tmp_in_62=ST1_MagicNumber + 7;
                         if ( tmp_in_61 != tmp_in_62 )
                         {
                           tmp_in_62 = OrderMagicNumber();
                           tmp_in_63=ST1_MagicNumber + 8;
                           if ( tmp_in_62 != tmp_in_63 )
                           {
                             tmp_in_63 = OrderMagicNumber();
                             tmp_in_64=ST1_MagicNumber + 9;
                             if ( tmp_in_63 != tmp_in_64 )
                             {
                               tmp_in_64 = OrderMagicNumber();
                               tmp_in_65=ST1_MagicNumber + 10;
                               if ( tmp_in_64 != tmp_in_65 )
                               {
                                 tmp_in_65 = OrderMagicNumber();
                                 tmp_in_66=ST1_MagicNumber + 11;
                                 if ( tmp_in_65 != tmp_in_66 )
                                 {
                                   tmp_in_66 = OrderMagicNumber();
                                   tmp_in_67=ST1_MagicNumber + 12;
                                   if ( tmp_in_66 != tmp_in_67 )
                                   {
                                     tmp_in_67 = OrderMagicNumber();
                                     tmp_in_68=ST1_MagicNumber + 13;
                                     if ( tmp_in_67 != tmp_in_68 )
                                     {
                                       tmp_in_68 = OrderMagicNumber();
                                       tmp_in_69=ST1_MagicNumber + 14;
                                       if ( tmp_in_68 != tmp_in_69 )
                                       {
                                         tmp_in_69 = OrderMagicNumber();
                                         tmp_in_70=ST1_MagicNumber + 15;
                                       if ( tmp_in_69 != tmp_in_70 )   continue;
                                       }
                                     }
                                   }
                                 }
                               }
                             }
                           }
                         }
                       }
                     }
                   }
                 }
               }
             }
             if ( OrderType() == 0 )
             {
               OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_BID),99999,Red); 
             }
             if ( OrderType() != 1 )   continue;
             OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_ASK),99999,Red); 
             
           }
         }
         if ( !(g_320_bo_28E8) )
         {
           Print("NFP!! deleting trades!!"); 
         }
         g_320_bo_28E8 = true ;
       }
       else
       {
         g_320_bo_28E8 = false ;
       }
     }
   }
 }
 if ( g_320_bo_28E8 )
 {
   return(0); 
 }
 if ( g_45_bo_FC )
 {
   if ( DayOfWeek() == 5 && Hour() >= FridayStopHour && !(g_305_bo_2880) )
   {
     for (tmp_in_71 = OrdersTotal() ; tmp_in_71 >= 0 ; tmp_in_71=tmp_in_71 - 1)
     {
       if ( OrderSelect(tmp_in_71,0,0) != true || OrderSymbol() != g_336_st_3130 )   continue;
       tmp_in_72 = OrderMagicNumber();
       tmp_in_73=ST1_MagicNumber + 1;
       if ( tmp_in_72 != tmp_in_73 )
       {
         tmp_in_73 = OrderMagicNumber();
         tmp_in_74=ST1_MagicNumber + 2;
         if ( tmp_in_73 != tmp_in_74 )
         {
           tmp_in_74 = OrderMagicNumber();
           tmp_in_75=ST1_MagicNumber + 3;
           if ( tmp_in_74 != tmp_in_75 )
           {
             tmp_in_75 = OrderMagicNumber();
             tmp_in_76=ST1_MagicNumber + 4;
             if ( tmp_in_75 != tmp_in_76 )
             {
               tmp_in_76 = OrderMagicNumber();
               tmp_in_77=ST1_MagicNumber + 5;
               if ( tmp_in_76 != tmp_in_77 )
               {
                 tmp_in_77 = OrderMagicNumber();
                 tmp_in_78=ST1_MagicNumber + 6;
                 if ( tmp_in_77 != tmp_in_78 )
                 {
                   tmp_in_78 = OrderMagicNumber();
                   tmp_in_79=ST1_MagicNumber + 7;
                   if ( tmp_in_78 != tmp_in_79 )
                   {
                     tmp_in_79 = OrderMagicNumber();
                     tmp_in_80=ST1_MagicNumber + 8;
                     if ( tmp_in_79 != tmp_in_80 )
                     {
                       tmp_in_80 = OrderMagicNumber();
                       tmp_in_81=ST1_MagicNumber + 9;
                       if ( tmp_in_80 != tmp_in_81 )
                       {
                         tmp_in_81 = OrderMagicNumber();
                         tmp_in_82=ST1_MagicNumber + 10;
                         if ( tmp_in_81 != tmp_in_82 )
                         {
                           tmp_in_82 = OrderMagicNumber();
                           tmp_in_83=ST1_MagicNumber + 11;
                           if ( tmp_in_82 != tmp_in_83 )
                           {
                             tmp_in_83 = OrderMagicNumber();
                             tmp_in_84=ST1_MagicNumber + 12;
                             if ( tmp_in_83 != tmp_in_84 )
                             {
                               tmp_in_84 = OrderMagicNumber();
                               tmp_in_85=ST1_MagicNumber + 13;
                               if ( tmp_in_84 != tmp_in_85 )
                               {
                                 tmp_in_85 = OrderMagicNumber();
                                 tmp_in_86=ST1_MagicNumber + 14;
                                 if ( tmp_in_85 != tmp_in_86 )
                                 {
                                   tmp_in_86 = OrderMagicNumber();
                                   tmp_in_87=ST1_MagicNumber + 15;
                                 if ( tmp_in_86 != tmp_in_87 )   continue;
                                 }
                               }
                             }
                           }
                         }
                       }
                     }
                   }
                 }
               }
             }
           }
         }
       }
       if ( OrderType() == 0 )
       {
         OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_BID),g_38_do_C0,Red); 
       }
       if ( OrderType() == 1 )
       {
         OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_ASK),g_38_do_C0,Red); 
       }
       if ( ( OrderType() != 4 && OrderType() != 5 ) )   continue;
       OrderDelete(OrderTicket(),Red); 
       
     }
     Print("Weekend starting! closing trades.."); 
     g_305_bo_2880 = true ;
     return(0); 
   }
   if ( DayOfWeek() != 5 && g_305_bo_2880 == true )
   {
     g_305_bo_2880 = false ;
     if ( g_46_bo_FD )
     {
       lizong_8(); 
       return(0); 
     }
   }
 }
 g_1_do_0 = MarketInfo(g_336_st_3130,MODE_ASK) - MarketInfo(g_336_st_3130,MODE_BID) ;
 if ( g_35_bo_AF )
 {
   if ( g_1_do_0>MaxSpread * g_229_do_1E00 )
   {
     lizong_9(); 
     return(0); 
   }
   if ( g_1_do_0<=g_37_do_B8 * g_229_do_1E00 && ( !(g_45_bo_FC) || DayOfWeek() != 5 || Hour() <  FridayStopHour ) && ( !(g_171_bo_4BC) || lizong_20() ) )
   {
     lizong_8(); 
   }
 }
 if ( g_69_in_160 == 1 )
 {
   tmp_in_88 = 0;
   for (tmp_in_89 = OrdersTotal() ; tmp_in_89 >= 0 ; tmp_in_89=tmp_in_89 - 1)
   {
     if ( OrderSelect(tmp_in_89,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 4 )   continue;
     tmp_in_88=tmp_in_88 + 1;
     
   }
   if ( tmp_in_88 >  g_86_in_1C8 )
   {
     tmp_do_90 = 0.0;
     tmp_lo_91 = 0;
     for (tmp_in_92 = OrdersTotal() ; tmp_in_92 >= 0 ; tmp_in_92=tmp_in_92 - 1)
     {
       if ( OrderSelect(tmp_in_92,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 4 || !(OrderOpenPrice()>tmp_do_90) )   continue;
       tmp_lo_91 = OrderTicket();
       tmp_do_90 = OrderOpenPrice();
       
     }
     if ( tmp_lo_91 != 0 )
     {
       OrderDelete(tmp_lo_91,Green); 
       tmp_lo_93 = tmp_lo_91;
       for (tmp_in_94 = 0 ; tmp_in_94 < 100 ; tmp_in_94=tmp_in_94 + 1)
       {
         if ( !(g_198_do_1070_si100si2[tmp_in_94][0]==tmp_lo_93) )   continue;
         g_198_do_1070_si100si2[tmp_in_94][0] = 0.0;
         g_198_do_1070_si100si2[tmp_in_94][1] = 0.0;
         break;
         
       }
       Print("Max number of pending buy orders reached... deleting highest buystop order!"); 
     }
   }
   tmp_in_95 = 0;
   for (tmp_in_96 = OrdersTotal() ; tmp_in_96 >= 0 ; tmp_in_96=tmp_in_96 - 1)
   {
     if ( OrderSelect(tmp_in_96,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 5 )   continue;
     tmp_in_95=tmp_in_95 + 1;
     
   }
   if ( tmp_in_95 >  g_86_in_1C8 )
   {
     tmp_do_97 = 9999.0;
     tmp_lo_98 = 0;
     for (tmp_in_99 = OrdersTotal() ; tmp_in_99 >= 0 ; tmp_in_99=tmp_in_99 - 1)
     {
       if ( OrderSelect(tmp_in_99,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 5 || !(OrderOpenPrice()<tmp_do_97) )   continue;
       tmp_lo_98 = OrderTicket();
       tmp_do_97 = OrderOpenPrice();
       
     }
     if ( tmp_lo_98 != 0 )
     {
       OrderDelete(tmp_lo_98,Green); 
       tmp_lo_100 = tmp_lo_98;
       for (tmp_in_101 = 0 ; tmp_in_101 < 100 ; tmp_in_101=tmp_in_101 + 1)
       {
         if ( !(g_198_do_1070_si100si2[tmp_in_101][0]==tmp_lo_100) )   continue;
         g_198_do_1070_si100si2[tmp_in_101][0] = 0.0;
         g_198_do_1070_si100si2[tmp_in_101][1] = 0.0;
         break;
         
       }
       Print("Max number of pending sell orders reached... deleting lowest sellstop order!"); 
     }
   }
 }
 if ( !(g_305_bo_2880) && g_69_in_160 == 1 && !(g_303_bo_2878) )
 {
   if ( ( g_322_in_2AE0_si99[g_328_in_3100] != iBars(g_336_st_3130,g_72_in_178) || g_72_in_178 == 0 ) )
   {
     g_322_in_2AE0_si99[g_328_in_3100] = iBars(g_336_st_3130,g_72_in_178);
     if ( g_119_in_2D0 >  0 && g_120_in_2D4 >= 0 )
     {
       g_241_do_1E78_si99[g_328_in_3100] = g_123_do_2E0 * g_229_do_1E00 + (lizong_13(g_117_in_2C8,g_119_in_2D0,g_120_in_2D4) + g_1_do_0);
       g_242_do_21C4_si99[g_328_in_3100] = lizong_14(g_117_in_2C8,g_119_in_2D0,g_120_in_2D4) - g_123_do_2E0 * g_229_do_1E00;
     }
     if ( g_187_in_504 >  0 )
     {
       local_8_in=MathRand() * g_187_in_504 / 32768 + 1;
       g_15_in_78 = local_8_in ;
       Print("Slippage: " + (string(local_8_in))); 
     }
     if ( g_63_in_140 != 1 )
     {
       tmp_in_102 = 0;
       for (tmp_in_103 = OrdersTotal() ; tmp_in_103 >= 0 ; tmp_in_103=tmp_in_103 - 1)
       {
         if ( OrderSelect(tmp_in_103,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 0 )   continue;
         tmp_in_102=tmp_in_102 + 1;
         
       }
       if ( tmp_in_102 == 0 )
       {
         tmp_in_104 = 0;
         for (tmp_in_105 = OrdersTotal() ; tmp_in_105 >= 0 ; tmp_in_105=tmp_in_105 - 1)
         {
           if ( OrderSelect(tmp_in_105,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 1 )   continue;
           tmp_in_104=tmp_in_104 + 1;
           
         }
         if ( tmp_in_104 == 0 )
         {
           tmp_bo_106 = false;
           for (tmp_in_107 = 0 ; tmp_in_107 < g_199_in_16B0 ; tmp_in_107=tmp_in_107 + 1)
           {
             if ( !(g_196_do_568_si20si2[tmp_in_107][0]>0.0) )   continue;
             tmp_bo_106 = false;
             for (tmp_in_108 = OrdersTotal() ; tmp_in_108 >= 0 ; tmp_in_108=tmp_in_108 - 1)
             {
               if ( OrderSelect(tmp_in_108,0,0) != true )   continue;
               
               if ( ( OrderType() != 0 && OrderType() != 1 ) || !(OrderTicket()==g_196_do_568_si20si2[tmp_in_107][0]) )   continue;
               tmp_bo_106 = true;
               
             }
             if ( tmp_bo_106 )   continue;
             g_196_do_568_si20si2[tmp_in_107][0] = 0.0;
             g_196_do_568_si20si2[tmp_in_107][1] = 0.0;
             
           }
         }
       }
     }
     for (local_9_in = 0 ; local_9_in < g_86_in_1C8 ; local_9_in ++)
     {
       lizong_15(); 
     }
   }
   lizong_29(); 
   if ( g_267_in_25A0 != Hour() )
   {
     g_267_in_25A0 = Hour() ;
     tmp_bo_109 = false;
     for (tmp_in_110 = 0 ; tmp_in_110 < 100 ; tmp_in_110=tmp_in_110 + 1)
     {
       tmp_lo_111 = (long)g_198_do_1070_si100si2[tmp_in_110][0];
       tmp_bo_109 = false;
       for (tmp_in_112 = OrdersTotal() ; tmp_in_112 >= 0 ; tmp_in_112=tmp_in_112 - 1)
       {
         if ( !(OrderSelect(tmp_in_112,0,0)) )   continue;
         tmp_lo_113 = OrderTicket();
         if ( tmp_lo_111 != tmp_lo_113 )   continue;
         tmp_bo_109 = true;
         
       }
       if ( tmp_bo_109 )   continue;
       g_198_do_1070_si100si2[tmp_in_110][0] = 0.0;
       g_198_do_1070_si100si2[tmp_in_110][1] = 0.0;
       
     }
   }
 }
 if ( g_62_bo_13D )
 {
   tmp_st_114="Current spread: " + string(NormalizeDouble(g_1_do_0 / g_229_do_1E00,1)) + "\nPending Buy Order: ";
   tmp_in_115 = 0;
   for (tmp_in_116 = OrdersTotal() ; tmp_in_116 >= 0 ; tmp_in_116=tmp_in_116 - 1)
   {
     if ( OrderSelect(tmp_in_116,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 4 )   continue;
     tmp_in_115=tmp_in_115 + 1;
     
   }
   tmp_st_114=tmp_st_114 + string(tmp_in_115);
   tmp_st_114=tmp_st_114 + "\nPending Sell Orders: ";
   tmp_in_117 = 0;
   for (tmp_in_118 = OrdersTotal() ; tmp_in_118 >= 0 ; tmp_in_118=tmp_in_118 - 1)
   {
     if ( OrderSelect(tmp_in_118,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 5 )   continue;
     tmp_in_117=tmp_in_117 + 1;
     
   }
   tmp_st_114=tmp_st_114 + string(tmp_in_117);
   Comment(tmp_st_114); 
 }
 return(0); 
 }
//lizong_7 <<==--------   --------
 void lizong_8()
 {
  int       local_1_in;
//----- -----
 double     tmp_do_1;
 long       tmp_lo_2;
 int        tmp_in_3;
 double     tmp_do_4;
 long       tmp_lo_5;
 int        tmp_in_6;
 double     tmp_do_7;
 long       tmp_lo_8;
 int        tmp_in_9;
 double     tmp_do_10;
 long       tmp_lo_11;
 int        tmp_in_12;
 int        tmp_in_13;

 for (local_1_in = 0 ; local_1_in < g_200_in_16B4 ; local_1_in ++)
 {
   if ( !(g_197_do_6DC_si100si3[local_1_in][0]>0.0) )   continue;
   
   if ( g_197_do_6DC_si100si3[local_1_in][1]==4.0 && MarketInfo(g_336_st_3130,MODE_ASK)<g_197_do_6DC_si100si3[local_1_in][0] - g_221_do_1A80 )
   {
     Print("Restoring pending buy-order"); 
     g_230_in_1E08 = OrderSend(g_336_st_3130,4,g_197_do_6DC_si100si3[local_1_in][2],g_197_do_6DC_si100si3[local_1_in][0],int(g_38_do_C0 * g_229_do_1E00),g_197_do_6DC_si100si3[local_1_in][0] - (g_100_do_230 + g_64_do_148) * g_229_do_1E00,g_101_do_238 * g_229_do_1E00 + g_197_do_6DC_si100si3[local_1_in][0],g_334_st_3120,g_93_in_1F0,g_302_da_2870 + 0x2A300,Green) ;
     g_280_bo_25FA = false ;
     tmp_do_1 = g_197_do_6DC_si100si3[local_1_in][0];
     tmp_lo_2 = g_230_in_1E08;
     for (tmp_in_3 = 0 ; tmp_in_3 < 100 ; tmp_in_3=tmp_in_3 + 1)
     {
       if ( !(g_198_do_1070_si100si2[tmp_in_3][0]==0.0) )   continue;
       g_198_do_1070_si100si2[tmp_in_3][0] = (double)tmp_lo_2;
       g_198_do_1070_si100si2[tmp_in_3][1] = tmp_do_1;
       break;
       
     }
     if ( g_230_in_1E08 <= 0 )
     {
       if ( GetLastError() == 132 )
       {
         ResetLastError();
         if(1==0) //condition not met
         {
           do
           {
             Sleep(2500); 
             g_230_in_1E08 = OrderSend(g_336_st_3130,4,g_197_do_6DC_si100si3[local_1_in][2],g_197_do_6DC_si100si3[local_1_in][0],int(g_38_do_C0 * g_229_do_1E00),g_197_do_6DC_si100si3[local_1_in][0] - (g_100_do_230 + g_64_do_148) * g_229_do_1E00,g_101_do_238 * g_229_do_1E00 + g_197_do_6DC_si100si3[local_1_in][0],g_334_st_3120,g_93_in_1F0,g_302_da_2870 + 0x2A300,Green) ;
             g_280_bo_25FA = false ;
             tmp_do_4 = g_197_do_6DC_si100si3[local_1_in][0];
             tmp_lo_5 = g_230_in_1E08;
             for (tmp_in_6 = 0 ; tmp_in_6 < 100 ; tmp_in_6=tmp_in_6 + 1)
             {
               if ( !(g_198_do_1070_si100si2[tmp_in_6][0]==0.0) )   continue;
               g_198_do_1070_si100si2[tmp_in_6][0] = (double)tmp_lo_5;
               g_198_do_1070_si100si2[tmp_in_6][1] = tmp_do_4;
               break;
               
             }
           }
           while(GetLastError() == 132);
           
         }
       }
       Print("error: \'" + lizong_21(GetLastError()) + "\' when setting entry order"); 
     }
   }
   if ( !(g_197_do_6DC_si100si3[local_1_in][1]==5.0) || !(MarketInfo(g_336_st_3130,MODE_BID)>g_197_do_6DC_si100si3[local_1_in][0] + g_221_do_1A80) )   continue;
   Print("Restoring pending sell-order"); 
   g_230_in_1E08 = OrderSend(g_336_st_3130,5,g_197_do_6DC_si100si3[local_1_in][2],g_197_do_6DC_si100si3[local_1_in][0],int(g_38_do_C0 * g_229_do_1E00),(g_100_do_230 + g_64_do_148) * g_229_do_1E00 + g_197_do_6DC_si100si3[local_1_in][0],g_197_do_6DC_si100si3[local_1_in][0] - g_101_do_238 * g_229_do_1E00,g_334_st_3120,g_93_in_1F0,g_302_da_2870 + 0x2A300,Green) ;
   g_281_bo_25FB = false ;
   tmp_do_7 = g_197_do_6DC_si100si3[local_1_in][0];
   tmp_lo_8 = g_230_in_1E08;
   for (tmp_in_9 = 0 ; tmp_in_9 < 100 ; tmp_in_9=tmp_in_9 + 1)
   {
     if ( !(g_198_do_1070_si100si2[tmp_in_9][0]==0.0) )   continue;
     g_198_do_1070_si100si2[tmp_in_9][0] = (double)tmp_lo_8;
     g_198_do_1070_si100si2[tmp_in_9][1] = tmp_do_7;
     break;
     
   }
   if ( g_230_in_1E08 > 0 )   continue;
   
   if ( GetLastError() == 132 )
   {
     ResetLastError();
     if(1==0) //condition not met
     {
       do
       {
         Sleep(2500); 
         g_230_in_1E08 = OrderSend(g_336_st_3130,5,g_197_do_6DC_si100si3[local_1_in][2],g_197_do_6DC_si100si3[local_1_in][0],int(g_38_do_C0 * g_229_do_1E00),(g_100_do_230 + g_64_do_148) * g_229_do_1E00 + g_197_do_6DC_si100si3[local_1_in][0],g_197_do_6DC_si100si3[local_1_in][0] - g_101_do_238 * g_229_do_1E00,g_334_st_3120,g_93_in_1F0,g_302_da_2870 + 0x2A300,Green) ;
         g_281_bo_25FB = false ;
         tmp_do_10 = g_197_do_6DC_si100si3[local_1_in][0];
         tmp_lo_11 = g_230_in_1E08;
         for (tmp_in_12 = 0 ; tmp_in_12 < 100 ; tmp_in_12=tmp_in_12 + 1)
         {
           if ( !(g_198_do_1070_si100si2[tmp_in_12][0]==0.0) )   continue;
           g_198_do_1070_si100si2[tmp_in_12][0] = (double)tmp_lo_11;
           g_198_do_1070_si100si2[tmp_in_12][1] = tmp_do_10;
           break;
           
         }
       }
       while(GetLastError() == 132);
       
     }
   }
   Print("error: \'" + lizong_21(GetLastError()) + "\' when setting entry order"); 
   
 }
 for (tmp_in_13 = 0 ; tmp_in_13 < g_200_in_16B4 ; tmp_in_13=tmp_in_13 + 1)
 {
   g_197_do_6DC_si100si3[tmp_in_13][0] = 0.0;
   g_197_do_6DC_si100si3[tmp_in_13][1] = 0.0;
   g_197_do_6DC_si100si3[tmp_in_13][2] = 0.0;
 }
 }
//lizong_8 <<==--------   --------
 bool lizong_9()
 {
  int       local_2_in;
  int       local_3_in;
  int       local_4_in;
//----- -----
 long       tmp_lo_1;
 int        tmp_in_2;
 long       tmp_lo_3;
 int        tmp_in_4;
 double     tmp_do_5;
 double     tmp_do_6;
 long       tmp_lo_7;
 int        tmp_in_8;
 long       tmp_lo_9;
 int        tmp_in_10;

 for (local_2_in = OrdersTotal() ; local_2_in >= 0 ; local_2_in --)
 {
   if ( OrderSelect(local_2_in,0,0) != true )   continue;
   
   if ( ( OrderMagicNumber() != g_93_in_1F0 && OrderMagicNumber() != g_96_in_208 ) || OrderSymbol() != g_336_st_3130 )   continue;
   
   if ( OrderType() == 4 && OrderOpenPrice()<g_36_in_B0 * g_229_do_1E00 + MarketInfo(g_336_st_3130,MODE_ASK) && MarketInfo(g_336_st_3130,MODE_ASK)<OrderOpenPrice() - g_309_do_2898 )
   {
     if ( g_37_do_B8>0.0 )
     {
       Print("Spread too high..(" + string(g_1_do_0) + ") storing and deleting order " + string(OrderTicket())); 
       for (local_3_in = 0 ; local_3_in < g_200_in_16B4 ; local_3_in ++)
       {
         if ( g_197_do_6DC_si100si3[local_3_in][0]==0.0 )
         {
           Print("Storing pending order nr " + string(OrderTicket())); 
           g_197_do_6DC_si100si3[local_3_in][1] = OrderType();
           g_197_do_6DC_si100si3[local_3_in][0] = OrderOpenPrice();
           g_197_do_6DC_si100si3[local_3_in][2] = OrderLots();
           break;
         }
       }
       tmp_lo_1 = OrderTicket();
       for (tmp_in_2 = 0 ; tmp_in_2 < 100 ; tmp_in_2=tmp_in_2 + 1)
       {
         if ( !(g_198_do_1070_si100si2[tmp_in_2][0]==tmp_lo_1) )   continue;
         g_198_do_1070_si100si2[tmp_in_2][0] = 0.0;
         g_198_do_1070_si100si2[tmp_in_2][1] = 0.0;
         break;
         
       }
       OrderDelete(OrderTicket(),Green); 
     }
     else
     {
       Print("Spread too high..(" + string(g_1_do_0) + ") deleting order " + string(OrderTicket())); 
       tmp_lo_3 = OrderTicket();
       for (tmp_in_4 = 0 ; tmp_in_4 < 100 ; tmp_in_4=tmp_in_4 + 1)
       {
         if ( !(g_198_do_1070_si100si2[tmp_in_4][0]==tmp_lo_3) )   continue;
         g_198_do_1070_si100si2[tmp_in_4][0] = 0.0;
         g_198_do_1070_si100si2[tmp_in_4][1] = 0.0;
         break;
         
       }
       OrderDelete(OrderTicket(),Green); 
     }
   }
   if ( OrderType() != 5 )   continue;
   tmp_do_5 = OrderOpenPrice();
   if ( !(tmp_do_5>MarketInfo(g_336_st_3130,MODE_BID) - g_36_in_B0 * g_229_do_1E00) )   continue;
   tmp_do_6 = MarketInfo(g_336_st_3130,MODE_BID);
   if ( !(tmp_do_6>OrderOpenPrice() + g_309_do_2898) )   continue;
   
   if ( g_37_do_B8>0.0 )
   {
     Print("Spread too high..(" + string(g_1_do_0) + ") storing and deleting order " + string(OrderTicket())); 
     for (local_4_in = 0 ; local_4_in < g_200_in_16B4 ; local_4_in ++)
     {
       if ( g_197_do_6DC_si100si3[local_4_in][0]==0.0 )
       {
         Print("Storing pending order nr " + string(OrderTicket())); 
         g_197_do_6DC_si100si3[local_4_in][1] = OrderType();
         g_197_do_6DC_si100si3[local_4_in][0] = OrderOpenPrice();
         g_197_do_6DC_si100si3[local_4_in][2] = OrderLots();
         break;
       }
     }
     tmp_lo_7 = OrderTicket();
     for (tmp_in_8 = 0 ; tmp_in_8 < 100 ; tmp_in_8=tmp_in_8 + 1)
     {
       if ( !(g_198_do_1070_si100si2[tmp_in_8][0]==tmp_lo_7) )   continue;
       g_198_do_1070_si100si2[tmp_in_8][0] = 0.0;
       g_198_do_1070_si100si2[tmp_in_8][1] = 0.0;
       break;
       
     }
     OrderDelete(OrderTicket(),Green); 
      continue;
   }
   Print("Spread too high..(" + string(g_1_do_0) + ") deleting order " + string(OrderTicket())); 
   tmp_lo_9 = OrderTicket();
   for (tmp_in_10 = 0 ; tmp_in_10 < 100 ; tmp_in_10=tmp_in_10 + 1)
   {
     if ( !(g_198_do_1070_si100si2[tmp_in_10][0]==tmp_lo_9) )   continue;
     g_198_do_1070_si100si2[tmp_in_10][0] = 0.0;
     g_198_do_1070_si100si2[tmp_in_10][1] = 0.0;
     break;
     
   }
   OrderDelete(OrderTicket(),Green); 
   
 }
 return(false); 
 }
//lizong_9 <<==--------   --------
 void lizong_10( double arg_0_do,int arg_1_in)
 {
  double    local_1_do;
  double    local_2_do;
  double    local_3_do;
  double    local_4_do;
  double    local_5_do;
  double    local_6_do;
  double    local_7_do;
//----- -----

 local_1_do = g_223_do_1AC4_si99[g_328_in_3100] ;
 local_2_do = g_223_do_1AC4_si99[g_328_in_3100] ;
 g_401_do_6AD0 = AccountInfoDouble(ACCOUNT_BALANCE) ;
 if ( UseEquity )
 {
   g_401_do_6AD0 = AccountInfoDouble(ACCOUNT_EQUITY) ;
 }
 if ( ForceBalanceToUse>0.0 )
 {
   g_401_do_6AD0 = ForceBalanceToUse ;
 }
 if ( OnlyUp && g_402_do_6AD8>g_401_do_6AD0 )
 {
   g_401_do_6AD0 = g_402_do_6AD8 ;
 }
 if ( g_401_do_6AD0>g_402_do_6AD8 )
 {
   g_402_do_6AD8 = g_401_do_6AD0 ;
 }
 local_3_do = arg_0_do ;
 if ( ( g_190_in_518 == 2 || g_190_in_518 == 4 ) )
 {
   local_3_do = arg_0_do / 10.0 ;
 }
 if ( Risk <  999 && Risk >  0 )
 {
   local_4_do = Risk ;
   local_5_do = local_4_do / 1000.0 * g_401_do_6AD0 ;
   if ( MarketInfo(g_336_st_3130,MODE_LOTSTEP)==0.1 )
   {
     local_2_do = NormalizeDouble(arg_1_in * 0.01 * (local_5_do / (MarketInfo(g_336_st_3130,MODE_TICKVALUE) * local_3_do) * 0.1),1) ;
   }
   if ( MarketInfo(g_336_st_3130,MODE_LOTSTEP)==0.01 )
   {
     local_2_do = NormalizeDouble(arg_1_in * 0.01 * (local_5_do / (MarketInfo(g_336_st_3130,MODE_TICKVALUE) * local_3_do) * 0.1),2) ;
   }
 }
 if ( Risk == 999 )
 {
   local_6_do = g_148_do_420 / 100.0 * g_401_do_6AD0 ;
   if ( MarketInfo(g_336_st_3130,MODE_LOTSTEP)==0.1 )
   {
     local_2_do = NormalizeDouble(arg_1_in * 0.01 * (local_6_do / (MarketInfo(g_336_st_3130,MODE_TICKVALUE) * local_3_do) * 0.1),1) ;
   }
   if ( MarketInfo(g_336_st_3130,MODE_LOTSTEP)==0.01 )
   {
     local_2_do = NormalizeDouble(arg_1_in * 0.01 * (local_6_do / (MarketInfo(g_336_st_3130,MODE_TICKVALUE) * local_3_do) * 0.1),2) ;
   }
 }
 if ( Risk == 0 )
 {
   if ( MarketInfo(g_336_st_3130,MODE_LOTSTEP)==0.1 )
   {
      local_2_do = NormalizeDouble(arg_1_in * 0.01 * StartLotsRuntime,1) ;
   }
   if ( MarketInfo(g_336_st_3130,MODE_LOTSTEP)==0.01 )
   {
      local_2_do = NormalizeDouble(arg_1_in * 0.01 * StartLotsRuntime,2) ;
   }
 }
 if ( Risk == 9999 )
 {
   if ( MarketInfo(g_336_st_3130,MODE_LOTSTEP)==0.1 )
   {
     local_2_do = NormalizeDouble(arg_1_in * 0.01 * (g_401_do_6AD0 / g_145_in_40C * 0.01),1) ;
   }
   if ( MarketInfo(g_336_st_3130,MODE_LOTSTEP)==0.01 )
   {
     local_2_do = NormalizeDouble(arg_1_in * 0.01 * (g_401_do_6AD0 / g_145_in_40C * 0.01),2) ;
   }
 }
 if ( Risk == 1234 )
 {
   if ( UseWeightedLots )
   {
     if ( g_397_do_6768==0.0 )
     {
       g_397_do_6768 = 100000.0 ;
     }
     g_146_do_410 = MaxAllowedDD / g_398_do_6770 ;
     if ( SymbolInfoDouble(g_336_st_3130,36)==0.1 )
     {
       local_2_do = NormalizeDouble(g_146_do_410 / g_397_do_6768 * g_401_do_6AD0 / 100.0 * 0.01,1) ;
     }
     if ( SymbolInfoDouble(g_336_st_3130,36)==0.01 )
     {
       local_2_do = NormalizeDouble(g_146_do_410 / g_397_do_6768 * g_401_do_6AD0 / 100.0 * 0.01,2) ;
     }
   }
   else
   {
     if ( g_397_do_6768==0.0 )
     {
       g_397_do_6768 = 100000.0 ;
     }
     local_7_do = lizong_36(g_401_do_6AD0) ;
     if ( g_19_in_9C == 0 )
     {
       g_145_in_40C = (int)(g_385_in_5DA8 / (MaxAllowedDD / 100.0)) ;
     }
     if ( g_19_in_9C == 1 )
     {
       g_145_in_40C = (int)(g_386_in_5DAC / (MaxAllowedDD / 100.0)) ;
     }
     if ( g_19_in_9C == 2 )
     {
       g_145_in_40C = (int)(g_387_in_5DB0 / (MaxAllowedDD / 100.0)) ;
     }
     if ( g_19_in_9C == 3 )
     {
       g_145_in_40C = (int)(g_388_in_5DB4 / (MaxAllowedDD / 100.0)) ;
     }
     if ( g_19_in_9C == 4 )
     {
       g_145_in_40C = (int)(g_389_in_5DB8 / (MaxAllowedDD / 100.0)) ;
     }
     if ( SymbolInfoDouble(g_336_st_3130,36)==0.1 )
     {
       local_2_do = NormalizeDouble(arg_1_in * 0.01 * (local_7_do / g_145_in_40C * 0.01),1) ;
     }
     if ( SymbolInfoDouble(g_336_st_3130,36)==0.01 )
     {
       local_2_do = NormalizeDouble(arg_1_in * 0.01 * (local_7_do / g_145_in_40C * 0.01),2) ;
     }
   }
 }
 if ( Risk == 3 )
 {
   if ( SymbolInfoDouble(g_336_st_3130,36)==0.1 )
   {
     local_2_do = NormalizeDouble(MaxRiskPerStrategy_ / g_397_do_6768 * g_401_do_6AD0 / 100.0 * 0.01,1) ;
   }
   if ( SymbolInfoDouble(g_336_st_3130,36)==0.01 )
   {
     local_2_do = NormalizeDouble(MaxRiskPerStrategy_ / g_397_do_6768 * g_401_do_6AD0 / 100.0 * 0.01,2) ;
   }
 }
 local_2_do = local_2_do * g_9_do_60 ;
 if ( local_2_do<MarketInfo(g_336_st_3130,MODE_LOTSTEP) )
 {
   local_2_do = MarketInfo(g_336_st_3130,MODE_LOTSTEP) ;
 }
 if ( local_2_do>g_141_do_3F8 )
 {
   local_2_do = g_141_do_3F8 ;
 }
 if ( local_2_do<MarketInfo(g_336_st_3130,MODE_MINLOT) )
 {
   local_2_do = MarketInfo(g_336_st_3130,MODE_MINLOT) ;
 }
  if ( local_2_do>MarketInfo(g_336_st_3130,MODE_MAXLOT) && MarketInfo(g_336_st_3130,MODE_MAXLOT)!=0.0 )
  {
    local_2_do = MarketInfo(g_336_st_3130,MODE_MAXLOT) ;
  }
  if ( LizardManagedMagic(g_93_in_1F0) )
  {
    local_2_do = LizardRiskLots();
  }
  if ( MarketInfo(g_336_st_3130,MODE_LOTSTEP)==0.1 )
 {
   g_223_do_1AC4_si99[g_328_in_3100] = NormalizeDouble((MathFloor(local_2_do * 10.0)) / 10.0,1);
   return;
 }
 g_223_do_1AC4_si99[g_328_in_3100] = NormalizeDouble(MathFloor(local_2_do * 100.0) / 100.0,2);
 }
//lizong_10 <<==--------   --------
 double lizong_11( int arg_0_in)
 {
  bool      local_2_bo = false;
  bool      local_3_bo = false;
  bool      local_4_bo;
  int       local_5_in;
  int       local_6_in;
  int       local_7_in;
//----- -----
 double     tmp_do_1;
 int        tmp_in_2;
 double     tmp_do_3;
 int        tmp_in_4;
 double     tmp_do_5;
 int        tmp_in_6;
 bool       tmp_bo_7;

 local_4_bo = false ;
 local_5_in=g_74_in_180 + 1;
 do
 {
   local_3_bo = true ;
   local_4_bo = true ;
   for (local_6_in = local_5_in ; local_6_in >= local_5_in - g_74_in_180 ; local_6_in --)
   {
     if ( iHigh(g_336_st_3130,arg_0_in,local_6_in)>iHigh(g_336_st_3130,arg_0_in,local_5_in) )
     {
       local_4_bo = false ;
     }
   }
   for (local_7_in = local_5_in ; local_7_in <= local_5_in + g_73_in_17C ; local_7_in ++)
   {
     if ( iHigh(g_336_st_3130,arg_0_in,local_7_in)>iHigh(g_336_st_3130,arg_0_in,local_5_in) )
     {
       local_3_bo = false ;
     }
   }
   if ( local_4_bo && local_3_bo && iHigh(g_336_st_3130,arg_0_in,local_5_in)>g_80_do_198 * g_229_do_1E00 + MarketInfo(g_336_st_3130,MODE_ASK) )
   {
     tmp_do_1 = iHigh(g_336_st_3130,arg_0_in,local_5_in);
     tmp_in_2 = local_5_in;
     tmp_do_3 = iHigh(g_336_st_3130,g_71_in_174,0);
     for (tmp_in_4 = 1 ; tmp_in_4 <= tmp_in_2 ; tmp_in_4=tmp_in_4 + 1)
     {
       if ( iHigh(g_336_st_3130,g_71_in_174,tmp_in_4)>tmp_do_3 )
       {
         tmp_do_3 = iHigh(g_336_st_3130,g_71_in_174,tmp_in_4);
       }
     }
     if ( tmp_do_1>=tmp_do_3 )
     {
       tmp_do_5 = NormalizeDouble(iHigh(g_336_st_3130,arg_0_in,local_5_in),g_190_in_518);
       tmp_bo_7=false; 
       for (tmp_in_6 = OrdersTotal() ; tmp_in_6 >= 0 ; tmp_in_6=tmp_in_6 - 1)
       {
         if ( OrderSelect(tmp_in_6,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 4 || !(MathAbs(OrderOpenPrice() - (g_83_do_1B0 * g_229_do_1E00 + tmp_do_5))<g_88_do_1D0 * g_229_do_1E00) )   continue;
         tmp_bo_7 = true;
          break;
         
       }
       if ( !(tmp_bo_7) && ( !(g_75_bo_184) || !(iClose(g_336_st_3130,arg_0_in,local_5_in - 1)>iHigh(g_336_st_3130,arg_0_in,local_5_in) - g_80_do_198 * g_229_do_1E00) ) )
       {
         local_2_bo = true ;
         g_262_do_2580 = NormalizeDouble(iHigh(g_336_st_3130,arg_0_in,local_5_in),g_190_in_518) ;
         g_265_in_2598 = local_5_in ;
         break;
       }
     }
   }
   local_5_in ++;
   if ( local_5_in <= g_77_in_188 )   continue;
   g_262_do_2580 = 0.0 ;
   break;
   
 }
 while(!(local_2_bo));
 
 return(g_262_do_2580); 
 }
//lizong_11 <<==--------   --------
 double lizong_12( int arg_0_in)
 {
  bool      local_2_bo = false;
  bool      local_3_bo = false;
  bool      local_4_bo;
  int       local_5_in;
  int       local_6_in;
  int       local_7_in;
//----- -----
 double     tmp_do_1;
 int        tmp_in_2;
 double     tmp_do_3;
 int        tmp_in_4;
 double     tmp_do_5;
 int        tmp_in_6;
 bool       tmp_bo_7;

 local_4_bo = false ;
 local_5_in=g_74_in_180 + 1;
 do
 {
   local_3_bo = true ;
   local_4_bo = true ;
   for (local_6_in = local_5_in ; local_6_in >= local_5_in - g_74_in_180 ; local_6_in --)
   {
     if ( iLow(g_336_st_3130,arg_0_in,local_6_in)<iLow(g_336_st_3130,arg_0_in,local_5_in) )
     {
       local_4_bo = false ;
     }
   }
   for (local_7_in = local_5_in ; local_7_in <= local_5_in + g_73_in_17C ; local_7_in ++)
   {
     if ( iLow(g_336_st_3130,arg_0_in,local_7_in)<iLow(g_336_st_3130,arg_0_in,local_5_in) )
     {
       local_3_bo = false ;
     }
   }
   if ( local_4_bo && local_3_bo && iLow(g_336_st_3130,arg_0_in,local_5_in)<MarketInfo(g_336_st_3130,MODE_BID) - g_80_do_198 * g_229_do_1E00 )
   {
     tmp_do_1 = iLow(g_336_st_3130,arg_0_in,local_5_in);
     tmp_in_2 = local_5_in;
     tmp_do_3 = iLow(g_336_st_3130,g_71_in_174,0);
     for (tmp_in_4 = 1 ; tmp_in_4 <= tmp_in_2 ; tmp_in_4=tmp_in_4 + 1)
     {
       if ( iLow(g_336_st_3130,g_71_in_174,tmp_in_4)<tmp_do_3 )
       {
         tmp_do_3 = iLow(g_336_st_3130,g_71_in_174,tmp_in_4);
       }
     }
     if ( tmp_do_1<=tmp_do_3 )
     {
       tmp_do_5 = NormalizeDouble(iLow(g_336_st_3130,arg_0_in,local_5_in),g_190_in_518);
       tmp_bo_7=false; 
       for (tmp_in_6 = OrdersTotal() ; tmp_in_6 >= 0 ; tmp_in_6=tmp_in_6 - 1)
       {
         if ( OrderSelect(tmp_in_6,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 5 || !(MathAbs(OrderOpenPrice() - (tmp_do_5 - g_84_do_1B8 * g_229_do_1E00))<g_88_do_1D0 * g_229_do_1E00) )   continue;
         tmp_bo_7 = true;
          break;
         
       }
       if ( !(tmp_bo_7) && ( !(g_75_bo_184) || !(iClose(g_336_st_3130,arg_0_in,local_5_in - 1)<g_80_do_198 * g_229_do_1E00 + iLow(g_336_st_3130,arg_0_in,local_5_in)) ) )
       {
         local_2_bo = true ;
         g_261_do_2578 = NormalizeDouble(iLow(g_336_st_3130,arg_0_in,local_5_in),g_190_in_518) ;
         g_266_in_259C = local_5_in ;
         break;
       }
     }
   }
   local_5_in ++;
   if ( local_5_in <= g_77_in_188 )   continue;
   g_261_do_2578 = 0.0 ;
   break;
   
 }
 while(!(local_2_bo));
 
 return(g_261_do_2578); 
 }
//lizong_12 <<==--------   --------
 double lizong_13( int arg_0_in,int arg_1_in,int arg_2_in)
 {
  bool      local_2_bo = false;
  double    local_3_do = 0.0;
  bool      local_4_bo = false;
  bool      local_5_bo;
  int       local_6_in;
  int       local_7_in;
  int       local_8_in;
//----- -----

 local_5_bo = false ;
 local_6_in=arg_2_in + 1;
 do
 {
   local_4_bo = true ;
   local_5_bo = true ;
   for (local_7_in = local_6_in ; local_7_in >= local_6_in - arg_2_in ; local_7_in --)
   {
     if ( iHigh(g_336_st_3130,arg_0_in,local_7_in)>iHigh(g_336_st_3130,arg_0_in,local_6_in) )
     {
       local_5_bo = false ;
     }
   }
   for (local_8_in = local_6_in ; local_8_in <= local_6_in + arg_1_in ; local_8_in ++)
   {
     if ( iHigh(g_336_st_3130,arg_0_in,local_8_in)>iHigh(g_336_st_3130,arg_0_in,local_6_in) )
     {
       local_4_bo = false ;
     }
   }
   if ( local_5_bo && local_4_bo && iHigh(g_336_st_3130,arg_0_in,local_6_in)>g_221_do_1A80 * g_229_do_1E00 + MarketInfo(g_336_st_3130,MODE_ASK) )
   {
     local_2_bo = true ;
     local_3_do = NormalizeDouble(iHigh(g_336_st_3130,arg_0_in,local_6_in),g_190_in_518) ;
     break;
   }
   local_6_in ++;
   if ( local_6_in <= g_118_in_2CC )   continue;
   local_3_do = 9999.0 ;
   break;
   
 }
 while(!(local_2_bo));
 
 return(local_3_do); 
 }
//lizong_13 <<==--------   --------
 double lizong_14( int arg_0_in,int arg_1_in,int arg_2_in)
 {
  bool      local_2_bo = false;
  double    local_3_do = 0.0;
  bool      local_4_bo = false;
  bool      local_5_bo;
  int       local_6_in;
  int       local_7_in;
  int       local_8_in;
//----- -----

 local_5_bo = false ;
 local_6_in=arg_2_in + 1;
 do
 {
   local_4_bo = true ;
   local_5_bo = true ;
   for (local_7_in = local_6_in ; local_7_in >= local_6_in - arg_2_in ; local_7_in --)
   {
     if ( iLow(g_336_st_3130,arg_0_in,local_7_in)<iLow(g_336_st_3130,arg_0_in,local_6_in) )
     {
       local_5_bo = false ;
     }
   }
   for (local_8_in = local_6_in ; local_8_in <= local_6_in + arg_1_in ; local_8_in ++)
   {
     if ( iLow(g_336_st_3130,arg_0_in,local_8_in)<iLow(g_336_st_3130,arg_0_in,local_6_in) )
     {
       local_4_bo = false ;
     }
   }
   if ( local_5_bo && local_4_bo && iLow(g_336_st_3130,arg_0_in,local_6_in)<MarketInfo(g_336_st_3130,MODE_BID) - g_221_do_1A80 * g_229_do_1E00 )
   {
     local_2_bo = true ;
     local_3_do = NormalizeDouble(iLow(g_336_st_3130,arg_0_in,local_6_in),g_190_in_518) ;
     break;
   }
   local_6_in ++;
   if ( local_6_in <= g_118_in_2CC )   continue;
   local_3_do = 0.0 ;
   break;
   
 }
 while(!(local_2_bo));
 
 return(local_3_do); 
 }
//lizong_14 <<==--------   --------
 void lizong_15()
 {
  int       local_1_in;
//----- -----
 long       tmp_lo_1;
 long       tmp_lo_2;
 int        tmp_in_3;
 int        tmp_in_4;
 int        tmp_in_5;
 int        tmp_in_6;
 int        tmp_in_7;
 int        tmp_in_8;
 int        tmp_in_9;
 int        tmp_in_10;
 int        tmp_in_11;
 int        tmp_in_12;

 if ( g_213_bo_1710 )
 {
   g_268_do_25A8 = iMA(g_336_st_3130,0,g_214_in_1714,0,1,0,1) ;
   g_269_do_25B0 = iMA(g_336_st_3130,0,g_217_in_1A70,0,1,0,1) ;
 }
 lizong_10(g_100_do_230,g_92_in_1EC); 
 if ( g_223_do_1AC4_si99[g_328_in_3100]>g_141_do_3F8 )
 {
   g_223_do_1AC4_si99[g_328_in_3100] = g_141_do_3F8;
 }
 if ( g_89_in_1D8 >  0 )
 {
   g_302_da_2870=TimeCurrent() + g_234_in_1E20;
 }
 if ( Virtual_expiration )
 {
   g_302_da_2870 = 0 ;
   for (local_1_in = OrdersTotal() ; local_1_in >= 0 ; local_1_in --)
   {
     if ( OrderSelect(local_1_in,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 )   continue;
     
     if ( ( OrderType() != 4 && OrderType() != 5 ) )   continue;
     tmp_lo_1 = TimeCurrent();
     tmp_lo_2=OrderOpenTime() + g_234_in_1E20;
     if ( tmp_lo_1 < tmp_lo_2 )   continue;
     OrderDelete(OrderTicket(),Red); 
     
   }
 }
 tmp_in_3 = 0;
 for (tmp_in_4 = OrdersTotal() ; tmp_in_4 >= 0 ; tmp_in_4=tmp_in_4 - 1)
 {
   if ( OrderSelect(tmp_in_4,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 0 )   continue;
   tmp_in_3=tmp_in_3 + 1;
   
 }
 if ( tmp_in_3 <  g_87_in_1CC )
 {
   if(!lizong_16(1)) __ReaperEntryDebug("buy pending skipped");
 }
 else
 {
   tmp_in_5 = 1;
   for (tmp_in_6 = OrdersTotal() ; tmp_in_6 >= 0 ; tmp_in_6=tmp_in_6 - 1)
   {
     if ( OrderSelect(tmp_in_6,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 4 )   continue;
     OrderDelete(OrderTicket(),0xFFFFFFFF); 
     
   }
   if ( tmp_in_5 == 2 )
   {
     for (tmp_in_7 = OrdersTotal() ; tmp_in_7 >= 0 ; tmp_in_7=tmp_in_7 - 1)
     {
       if ( OrderSelect(tmp_in_7,0,0) != true || OrderMagicNumber() != g_96_in_208 || OrderSymbol() != g_336_st_3130 || OrderType() != 4 )   continue;
       OrderDelete(OrderTicket(),0xFFFFFFFF); 
       
     }
   }
 }
 tmp_in_8 = 0;
 for (tmp_in_9 = OrdersTotal() ; tmp_in_9 >= 0 ; tmp_in_9=tmp_in_9 - 1)
 {
   if ( OrderSelect(tmp_in_9,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 1 )   continue;
   tmp_in_8=tmp_in_8 + 1;
   
 }
 if ( tmp_in_8 <  g_87_in_1CC )
 {
   if(!lizong_17(1)) __ReaperEntryDebug("sell pending skipped");
   return;
 }
 tmp_in_10 = 1;
 for (tmp_in_11 = OrdersTotal() ; tmp_in_11 >= 0 ; tmp_in_11=tmp_in_11 - 1)
 {
   if ( OrderSelect(tmp_in_11,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 5 )   continue;
   OrderDelete(OrderTicket(),0xFFFFFFFF); 
   
 }
 if ( tmp_in_10 != 2 )   return;
 for (tmp_in_12 = OrdersTotal() ; tmp_in_12 >= 0 ; tmp_in_12=tmp_in_12 - 1)
 {
   if ( OrderSelect(tmp_in_12,0,0) != true || OrderMagicNumber() != g_96_in_208 || OrderSymbol() != g_336_st_3130 || OrderType() != 5 )   continue;
   OrderDelete(OrderTicket(),0xFFFFFFFF); 
   
 }
 }
//lizong_15 <<==--------   --------
 bool lizong_16( int arg_0_in)
 {
  bool      local_2_bo;
  double    local_3_do;
  double    local_4_do;
  double    local_5_do;
  double    local_6_do;
//----- -----
 bool       tmp_bo_1;
 int        tmp_in_2;
 double     tmp_do_3;
 int        tmp_in_4;
 bool       tmp_bo_5;
 int        tmp_in_6;
 int        tmp_in_7;
 double     tmp_do_8;
 int        tmp_in_9;
 double     tmp_do_10;
 int        tmp_in_11;
 bool       tmp_bo_12;
 bool       tmp_bo_13;
 int        tmp_in_14;
 bool       tmp_bo_15;
 int        tmp_in_16;
 double     tmp_do_17;
 long       tmp_lo_18;
 int        tmp_in_19;

 if ( !(LizardZoneSessionOpen(g_93_in_1F0)) )
 {
   return(false);
 }
 if ( !(AllowBuyTrades) )
 {
   return(false); 
 }
 if ( g_218_bo_1A74 )
 {
   tmp_bo_1 = false;
 }
 else
 {
   tmp_bo_1=false; 
   for (tmp_in_2 = 0 ; tmp_in_2 < OrdersTotal() ; tmp_in_2=tmp_in_2 + 1)
   {
     if ( OrderSelect(tmp_in_2,0,0) != true || OrderType() != 0 || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 )   continue;
     tmp_bo_1 = true;
      break;
     
   }
 }
 if ( tmp_bo_1 == true )
 {
   return(false); 
 }
 if ( g_213_bo_1710 && g_268_do_25A8<g_269_do_25B0 )
 {
   return(false); 
 }
 if ( arg_0_in == 1 )
 {
   lizong_11(g_71_in_174); 
   local_2_bo = false ;
   tmp_do_3 = g_262_do_2580;
   tmp_bo_5=false; 
   for (tmp_in_4 = OrdersTotal() ; tmp_in_4 >= 0 ; tmp_in_4=tmp_in_4 - 1)
   {
     if ( OrderSelect(tmp_in_4,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 4 || !(MathAbs(OrderOpenPrice() - (g_83_do_1B0 * g_229_do_1E00 + tmp_do_3))<g_88_do_1D0 * g_229_do_1E00) )   continue;
     tmp_bo_5 = true;
      break;
     
    }
    if ( !(tmp_bo_5) )
    {
      tmp_in_6 = 0;
     for (tmp_in_7 = OrdersTotal() ; tmp_in_7 >= 0 ; tmp_in_7=tmp_in_7 - 1)
     {
       if ( OrderSelect(tmp_in_7,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 4 )   continue;
       tmp_in_6=tmp_in_6 + 1;
       
     }
     if ( tmp_in_6 == g_86_in_1C8 )
     {
       tmp_do_8 = 9999.0;
       for (tmp_in_9 = OrdersTotal() ; tmp_in_9 >= 0 ; tmp_in_9=tmp_in_9 - 1)
       {
         if ( OrderSelect(tmp_in_9,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 4 || !(OrderOpenPrice()<tmp_do_8) )   continue;
         tmp_do_8 = OrderOpenPrice();
         
       }
       if ( g_262_do_2580>tmp_do_8 )
       {
         return(false); 
       }
     }
     g_264_do_2590 = g_262_do_2580 ;
     local_2_bo = true ;
     g_188_do_508 = NormalizeDouble(g_262_do_2580,g_190_in_518) ;
   }
   if ( g_188_do_508==0.0 )
   {
     return(false); 
   }
   if ( local_2_bo )
   {
     g_247_do_2500 = g_129_do_318 ;
     local_3_do = NormalizeDouble(g_83_do_1B0 * g_229_do_1E00 + g_188_do_508,g_190_in_518) ;
     tmp_do_10 = local_3_do;
     tmp_bo_12=false; 
     for (tmp_in_11 = OrdersTotal() ; tmp_in_11 >= 0 ; tmp_in_11=tmp_in_11 - 1)
     {
       if ( OrderSelect(tmp_in_11,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 4 || !(OrderOpenPrice()<=tmp_do_10) )   continue;
       tmp_bo_12 = true;
        break;
       
     }
     if ( tmp_bo_12 )
     {
       return(false); 
     }
     g_310_do_28A0 = local_3_do ;
     if ( !(g_67_bo_158) )
     {
       if ( CheckMargin && AccountFreeMarginCheck(g_336_st_3130,0,g_223_do_1AC4_si99[g_328_in_3100])<=0.0 )
       {
         Print("Free margin not sufficient for setting order with lotsize " + string(g_223_do_1AC4_si99[g_328_in_3100]) + "..."); 
         return(false); 
       }
       local_4_do = NormalizeDouble(g_15_in_78 * g_229_do_1E00 + local_3_do,g_190_in_518) ;
       local_5_do = NormalizeDouble(local_3_do - (g_100_do_230 + g_64_do_148) * g_229_do_1E00,g_190_in_518) ;
       local_6_do = NormalizeDouble(g_101_do_238 * g_229_do_1E00 + local_3_do,g_190_in_518) ;
       if ( g_223_do_1AC4_si99[g_328_in_3100]<SymbolInfoDouble(g_336_st_3130,34) )
       {
         Print("Volume is less than the minimal allowed SYMBOL_VOLUME_MIN=" + string(SymbolInfoDouble(g_336_st_3130,34))); 
         tmp_bo_13 = false;
       }
       else
       {
         if ( g_223_do_1AC4_si99[g_328_in_3100]>SymbolInfoDouble(g_336_st_3130,35) )
         {
           Print("Volume is greater than the maximal allowed SYMBOL_VOLUME_MAX=" + string(SymbolInfoDouble(g_336_st_3130,35))); 
           tmp_bo_13 = false;
         }
         else
         {
           if ( MathAbs(NormalizeDouble(g_223_do_1AC4_si99[g_328_in_3100] / SymbolInfoDouble(g_336_st_3130,36),0) * SymbolInfoDouble(g_336_st_3130,36) - g_223_do_1AC4_si99[g_328_in_3100])>0.0000001 )
           {
             Print("Volume " + string(g_223_do_1AC4_si99[g_328_in_3100]) + " is not a multiple of the minimal step SYMBOL_VOLUME_STEP=" + string(SymbolInfoDouble(g_336_st_3130,36))); 
             tmp_bo_13 = false;
           }
           else
           {
             tmp_bo_13 = true;
           }
         }
       }

       tmp_in_14 = (int)AccountInfoInteger(ACCOUNT_LIMIT_ORDERS);
       if ( tmp_in_14 == 0 )
       {
         tmp_bo_15 = true;
       }
       else
       {
         tmp_bo_15 = OrdersTotal()<tmp_in_14;
       }
       if ( ( !(tmp_bo_13) || !(tmp_bo_15) ) )
       {
         return(false); 
       }
       if ( MarketInfo(g_336_st_3130,MODE_ASK)<local_4_do - g_309_do_2898 * g_229_do_1E00 && MarketInfo(g_336_st_3130,MODE_ASK)<local_4_do - g_221_do_1A80 * g_229_do_1E00 )
       {
         if ( !(setSL_TP_After_Entry) )
         {
           g_230_in_1E08 = OrderSend(g_336_st_3130,4,g_223_do_1AC4_si99[g_328_in_3100],local_4_do,int(g_38_do_C0 * g_229_do_1E00),local_5_do,local_6_do,g_334_st_3120,g_93_in_1F0,g_302_da_2870,Green) ;
         }
         else
         {
           g_230_in_1E08 = OrderSend(g_336_st_3130,4,g_223_do_1AC4_si99[g_328_in_3100],local_4_do,int(g_38_do_C0 * g_229_do_1E00),0.0,0.0,g_334_st_3120,g_93_in_1F0,g_302_da_2870,Green) ;
         }
         g_280_bo_25FA = false ;
         if ( g_230_in_1E08 <= 0 )
         {
           tmp_in_16 = GetLastError();
           if ( tmp_in_16 == 132 )
           {
             ResetLastError();
             if(1==0) //condition not met
             {
               do
               {
                 Sleep(2500); 
                 if ( !(setSL_TP_After_Entry) )
                 {
                   tmp_in_16 = (int)(g_38_do_C0 * g_229_do_1E00);
                   g_230_in_1E08 = OrderSend(g_336_st_3130,4,g_223_do_1AC4_si99[g_328_in_3100],local_4_do,tmp_in_16,local_5_do,local_6_do,g_334_st_3120,g_93_in_1F0,g_302_da_2870,Green) ;
                 }
                 else
                 {
                   g_230_in_1E08 = OrderSend(g_336_st_3130,4,g_223_do_1AC4_si99[g_328_in_3100],local_4_do,int(g_38_do_C0 * g_229_do_1E00),0.0,0.0,g_334_st_3120,g_93_in_1F0,g_302_da_2870,Green) ;
                 }
                 g_280_bo_25FA = false ;
               }
               while(GetLastError() == 132);
               
             }
           }
           Print("error: \'" + lizong_21(GetLastError()) + "\' when setting entry order"); 
         }
         else
         {
           tmp_do_17 = local_3_do;
           tmp_lo_18 = g_230_in_1E08;
           for (tmp_in_19 = 0 ; tmp_in_19 < 100 ; tmp_in_19=tmp_in_19 + 1)
           {
             if ( !(g_198_do_1070_si100si2[tmp_in_19][0]==0.0) )   continue;
             g_198_do_1070_si100si2[tmp_in_19][0] = (double)tmp_lo_18;
             g_198_do_1070_si100si2[tmp_in_19][1] = tmp_do_17;
             break;
             
           }
         }
       }
     }
     return(true); 
   }
 }
 return(false); 
 }
//lizong_16 <<==--------   --------
 bool lizong_17( int arg_0_in)
 {
  bool      local_2_bo;
  double    local_3_do;
  double    local_4_do;
  double    local_5_do;
  double    local_6_do;
//----- -----
 bool       tmp_bo_1;
 int        tmp_in_2;
 double     tmp_do_3;
 int        tmp_in_4;
 bool       tmp_bo_5;
 int        tmp_in_6;
 int        tmp_in_7;
 double     tmp_do_8;
 int        tmp_in_9;
 double     tmp_do_10;
 int        tmp_in_11;
 bool       tmp_bo_12;
 bool       tmp_bo_13;
 int        tmp_in_14;
 bool       tmp_bo_15;
 int        tmp_in_16;
 double     tmp_do_17;
 long       tmp_lo_18;
 int        tmp_in_19;

 if ( !(LizardZoneSessionOpen(g_93_in_1F0)) )
 {
   return(false);
 }
 if ( !(AllowSellTrades) )
 {
   return(false); 
 }
 if ( g_218_bo_1A74 )
 {
   tmp_bo_1 = false;
 }
 else
 {
   tmp_bo_1=false; 
   for (tmp_in_2 = 0 ; tmp_in_2 < OrdersTotal() ; tmp_in_2=tmp_in_2 + 1)
   {
     if ( OrderSelect(tmp_in_2,0,0) != true || OrderType() != 1 || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 )   continue;
     tmp_bo_1 = true;
      break;
     
   }
 }
 if ( tmp_bo_1 == true )
 {
   return(false); 
 }
 if ( g_213_bo_1710 && g_268_do_25A8>g_269_do_25B0 )
 {
   return(false); 
 }
 if ( arg_0_in == 1 )
 {
   lizong_12(g_71_in_174); 
   local_2_bo = false ;
   tmp_do_3 = g_261_do_2578;
   tmp_bo_5=false; 
   for (tmp_in_4 = OrdersTotal() ; tmp_in_4 >= 0 ; tmp_in_4=tmp_in_4 - 1)
   {
     if ( OrderSelect(tmp_in_4,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 5 || !(MathAbs(OrderOpenPrice() - (tmp_do_3 - g_84_do_1B8 * g_229_do_1E00))<g_88_do_1D0 * g_229_do_1E00) )   continue;
     tmp_bo_5 = true;
      break;
     
    }
    if ( !(tmp_bo_5) )
    {
      tmp_in_6 = 0;
     for (tmp_in_7 = OrdersTotal() ; tmp_in_7 >= 0 ; tmp_in_7=tmp_in_7 - 1)
     {
       if ( OrderSelect(tmp_in_7,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 5 )   continue;
       tmp_in_6=tmp_in_6 + 1;
       
     }
     if ( tmp_in_6 == g_86_in_1C8 )
     {
       tmp_do_8 = 0.0;
       for (tmp_in_9 = OrdersTotal() ; tmp_in_9 >= 0 ; tmp_in_9=tmp_in_9 - 1)
       {
         if ( OrderSelect(tmp_in_9,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 5 || !(OrderOpenPrice()>tmp_do_8) )   continue;
         tmp_do_8 = OrderOpenPrice();
         
       }
       if ( g_261_do_2578<tmp_do_8 )
       {
         return(false); 
       }
     }
     g_263_do_2588 = g_261_do_2578 ;
     local_2_bo = true ;
     g_189_do_510 = NormalizeDouble(g_261_do_2578,g_190_in_518) ;
   }
   if ( g_189_do_510==0.0 )
   {
     return(false); 
   }
   if ( local_2_bo )
   {
     g_247_do_2500 = g_129_do_318 ;
     local_3_do = NormalizeDouble(g_189_do_510 - g_84_do_1B8 * g_229_do_1E00,g_190_in_518) ;
     tmp_do_10 = local_3_do;
     tmp_bo_12=false; 
     for (tmp_in_11 = OrdersTotal() ; tmp_in_11 >= 0 ; tmp_in_11=tmp_in_11 - 1)
     {
       if ( OrderSelect(tmp_in_11,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 || OrderType() != 5 || !(OrderOpenPrice()>=tmp_do_10) )   continue;
       tmp_bo_12 = true;
        break;
       
     }
     if ( tmp_bo_12 )
     {
       return(false); 
     }
     g_311_do_28A8 = local_3_do ;
     if ( !(g_67_bo_158) )
     {
       if ( CheckMargin && AccountFreeMarginCheck(g_336_st_3130,1,g_223_do_1AC4_si99[g_328_in_3100])<=0.0 )
       {
         Print("Free margin not sufficient for setting order with lotsize " + string(g_223_do_1AC4_si99[g_328_in_3100]) + "..."); 
         return(false); 
       }
       local_4_do = NormalizeDouble(local_3_do - g_15_in_78 * g_229_do_1E00,g_190_in_518) ;
       local_5_do = NormalizeDouble((g_100_do_230 + g_64_do_148) * g_229_do_1E00 + local_3_do,g_190_in_518) ;
       local_6_do = NormalizeDouble(local_3_do - g_101_do_238 * g_229_do_1E00,g_190_in_518) ;
       if ( g_223_do_1AC4_si99[g_328_in_3100]<SymbolInfoDouble(g_336_st_3130,34) )
       {
         Print("Volume is less than the minimal allowed SYMBOL_VOLUME_MIN=" + string(SymbolInfoDouble(g_336_st_3130,34))); 
         tmp_bo_13 = false;
       }
       else
       {
         if ( g_223_do_1AC4_si99[g_328_in_3100]>SymbolInfoDouble(g_336_st_3130,35) )
         {
           Print("Volume is greater than the maximal allowed SYMBOL_VOLUME_MAX=" + string(SymbolInfoDouble(g_336_st_3130,35))); 
           tmp_bo_13 = false;
         }
         else
         {
           if ( MathAbs(NormalizeDouble(g_223_do_1AC4_si99[g_328_in_3100] / SymbolInfoDouble(g_336_st_3130,36),0) * SymbolInfoDouble(g_336_st_3130,36) - g_223_do_1AC4_si99[g_328_in_3100])>0.0000001 )
           {
             Print("Volume " + string(g_223_do_1AC4_si99[g_328_in_3100]) + " is not a multiple of the minimal step SYMBOL_VOLUME_STEP=" + string(SymbolInfoDouble(g_336_st_3130,36))); 
             tmp_bo_13 = false;
           }
           else
           {
             tmp_bo_13 = true;
           }
         }
       }

       tmp_in_14 = (int)AccountInfoInteger(ACCOUNT_LIMIT_ORDERS);
       if ( tmp_in_14 == 0 )
       {
         tmp_bo_15 = true;
       }
       else
       {
         tmp_bo_15 = OrdersTotal()<tmp_in_14;
       }
       if ( ( !(tmp_bo_13) || !(tmp_bo_15) ) )
       {
         return(false); 
       }
       if ( MarketInfo(g_336_st_3130,MODE_BID)>g_309_do_2898 * g_229_do_1E00 + local_4_do && MarketInfo(g_336_st_3130,MODE_BID)>g_221_do_1A80 * g_229_do_1E00 + local_4_do )
       {
         if ( !(setSL_TP_After_Entry) )
         {
           g_230_in_1E08 = OrderSend(g_336_st_3130,5,g_223_do_1AC4_si99[g_328_in_3100],local_4_do,int(g_38_do_C0 * g_229_do_1E00),local_5_do,local_6_do,g_334_st_3120,g_93_in_1F0,g_302_da_2870,Red) ;
         }
         else
         {
           g_230_in_1E08 = OrderSend(g_336_st_3130,5,g_223_do_1AC4_si99[g_328_in_3100],local_4_do,int(g_38_do_C0 * g_229_do_1E00),0.0,0.0,g_334_st_3120,g_93_in_1F0,g_302_da_2870,Red) ;
         }
         g_281_bo_25FB = false ;
         if ( g_230_in_1E08 <= 0 )
         {
           tmp_in_16 = GetLastError();
           if ( tmp_in_16 == 132 )
           {
             ResetLastError();
             if(1==0) //condition not met
             {
               do
               {
                 Sleep(2500); 
                 if ( !(setSL_TP_After_Entry) )
                 {
                   tmp_in_16 = (int)(g_38_do_C0 * g_229_do_1E00);
                   g_230_in_1E08 = OrderSend(g_336_st_3130,5,g_223_do_1AC4_si99[g_328_in_3100],local_4_do,tmp_in_16,local_5_do,local_6_do,g_334_st_3120,g_93_in_1F0,g_302_da_2870,Red) ;
                 }
                 else
                 {
                   g_230_in_1E08 = OrderSend(g_336_st_3130,5,g_223_do_1AC4_si99[g_328_in_3100],local_4_do,int(g_38_do_C0 * g_229_do_1E00),0.0,0.0,g_334_st_3120,g_93_in_1F0,g_302_da_2870,Red) ;
                 }
                 g_281_bo_25FB = false ;
               }
               while(GetLastError() == 132);
               
             }
           }
           Print("error: \'" + lizong_21(GetLastError()) + "\' when setting entry order"); 
         }
         else
         {
           tmp_do_17 = local_3_do;
           tmp_lo_18 = g_230_in_1E08;
           for (tmp_in_19 = 0 ; tmp_in_19 < 100 ; tmp_in_19=tmp_in_19 + 1)
           {
             if ( !(g_198_do_1070_si100si2[tmp_in_19][0]==0.0) )   continue;
             g_198_do_1070_si100si2[tmp_in_19][0] = (double)tmp_lo_18;
             g_198_do_1070_si100si2[tmp_in_19][1] = tmp_do_17;
             break;
             
           }
         }
       }
     }
   }
 }
 return(false); 
 }
//lizong_17 <<==--------   --------
 bool lizong_18()
 {
  bool      local_2_bo = false;
  bool      local_3_bo = false;
  double    local_4_do;
  double    local_5_do;
  int       local_6_in;
  double    local_7_do;
  double    local_8_do;
  long      local_9_lo;
  double    local_10_do;
  string    local_11_st;
  double    local_12_do;
  datetime  local_13_da;
  int       local_14_in;
  int       local_15_in;
  string    local_16_st;
  double    local_17_do;
  double    local_18_do;
  bool      local_19_bo;
  bool      local_20_bo;
  double    local_21_do;
  bool      local_22_bo;
  double    local_23_do;
  double    local_24_do;
  double    local_25_do;
  double    local_26_do;
  double    local_27_do;
  int       local_28_in;
  double    local_29_do;
//----- -----
 int        tmp_in_1;
 long       tmp_lo_2;
 int        tmp_in_3;
 double     tmp_do_4;
 double     tmp_do_5;
 long       tmp_lo_6;
 int        tmp_in_7;
 long       tmp_lo_8;
 int        tmp_in_9;
 int        tmp_in_10;
 string     tmp_st_11;
 double     tmp_do_12;
 int        tmp_in_13;
 long       tmp_lo_14;
 double     tmp_do_15;
 int        tmp_in_16;
 long       tmp_lo_17;
 long       tmp_lo_18;
 int        tmp_in_19;
 int        tmp_in_20;
 int        tmp_in_21;
 string     tmp_st_22;
 long       tmp_lo_23;
 double     tmp_do_24;
 double     tmp_do_25;
 int        tmp_in_26;
 double     tmp_do_27;
 bool       tmp_bo_28;
 int        tmp_in_29;
 int        tmp_in_30;
 double     tmp_do_31;
 long       tmp_lo_32;
 int        tmp_in_33;
 long       tmp_lo_34;
 double     tmp_do_35;
 double     tmp_do_36;
 int        tmp_in_37;
 double     tmp_do_38;
 bool       tmp_bo_39;
 int        tmp_in_40;
 int        tmp_in_41;
 double     tmp_do_42;
 long       tmp_lo_43;
 int        tmp_in_44;

 local_4_do = 0.0 ;
 local_5_do = 0.0 ;
 for (local_6_in = 0 ; local_6_in < OrdersTotal() ; local_6_in ++)
 {
   if ( OrderSelect(local_6_in,0,0) == true )
   {
     local_2_bo = false ;
     local_7_do = NormalizeDouble(OrderStopLoss(),g_190_in_518) ;
     local_8_do = NormalizeDouble(OrderTakeProfit(),g_190_in_518) ;
     local_9_lo = OrderTicket() ;
     local_10_do = NormalizeDouble(OrderOpenPrice(),g_190_in_518) ;
     local_11_st = OrderComment() ;
     local_12_do = OrderLots() ;
     local_13_da = OrderOpenTime() ;
     local_14_in = OrderType() ;
     local_15_in = OrderMagicNumber() ;
     local_16_st = OrderSymbol() ;
     if ( ( local_14_in == 4 || local_14_in == 2 ) && g_69_in_160 == 2 && ( g_95_in_204 == 0 || (g_95_in_204 == 1 && local_16_st == g_336_st_3130) ) && ( local_15_in == g_96_in_208 || g_96_in_208 == 0 ) && ( local_11_st == g_97_st_210 || g_97_st_210 == "" ) )
     {
       if ( ( local_7_do==0.0 || local_7_do==0.0 ) )
       {
         local_7_do = NormalizeDouble(local_10_do - g_100_do_230 * g_229_do_1E00,g_190_in_518) ;
         OrderModify(local_9_lo,local_10_do,local_7_do,local_8_do,0,Green); 
       }
       if ( ( local_8_do==0.0 || local_8_do==0.0 ) )
       {
         local_8_do = NormalizeDouble(g_101_do_238 * g_229_do_1E00 + local_10_do,g_190_in_518) ;
         OrderModify(local_9_lo,local_10_do,local_7_do,local_8_do,0,Green); 
       }
     }
     if ( local_14_in == 0 && ( ( local_15_in == g_93_in_1F0 && g_69_in_160 == 1 && local_16_st == g_336_st_3130 ) || (g_69_in_160 == 2 && ( g_95_in_204 == 0 || (g_95_in_204 == 1 && local_16_st == g_336_st_3130) ) && ( local_15_in == g_96_in_208 || g_96_in_208 == 0 ) && (local_11_st == g_97_st_210 || g_97_st_210 == "")) ) )
     {
       if ( ( local_7_do==0.0 || local_7_do==0.0 ) )
       {
         local_7_do = NormalizeDouble(local_10_do - g_100_do_230 * g_229_do_1E00,g_190_in_518) ;
         OrderModify(local_9_lo,local_10_do,local_7_do,local_8_do,0,Green); 
       }
       if ( ( local_8_do==0.0 || local_8_do==0.0 ) )
       {
         local_8_do = NormalizeDouble(g_101_do_238 * g_229_do_1E00 + local_10_do,g_190_in_518) ;
         OrderModify(local_9_lo,local_10_do,local_7_do,local_8_do,0,Green); 
       }
       if ( g_53_bo_11C && iTime(g_336_st_3130,g_52_in_118,g_51_in_114) <= local_13_da && iTime(g_336_st_3130,g_52_in_118,0) >  local_13_da && iClose(g_336_st_3130,g_52_in_118,1)<iOpen(g_336_st_3130,g_52_in_118,1) && iClose(g_336_st_3130,g_52_in_118,1)<local_10_do )
       {
         OrderClose(local_9_lo,local_12_do,MarketInfo(g_336_st_3130,MODE_BID),0,Red); 
         Print("closing candle confirmation"); 
       }
       if ( g_55_bo_124 && iTime(g_336_st_3130,g_54_in_120,g_51_in_114) <= local_13_da && iTime(g_336_st_3130,g_54_in_120,0) >  local_13_da && iClose(g_336_st_3130,g_54_in_120,1)<iOpen(g_336_st_3130,g_54_in_120,1) && iClose(g_336_st_3130,g_54_in_120,1)<local_10_do )
       {
         OrderClose(local_9_lo,local_12_do,MarketInfo(g_336_st_3130,MODE_BID),0,Red); 
         Print("closing candle confirmation"); 
       }
       if ( g_57_bo_12C && iTime(g_336_st_3130,g_56_in_128,g_51_in_114) <= local_13_da && iTime(g_336_st_3130,g_56_in_128,0) >  local_13_da && iClose(g_336_st_3130,g_56_in_128,1)<iOpen(g_336_st_3130,g_56_in_128,1) && iClose(g_336_st_3130,g_56_in_128,1)<local_10_do )
       {
         OrderClose(local_9_lo,local_12_do,MarketInfo(g_336_st_3130,MODE_BID),0,Red); 
         Print("closing candle confirmation"); 
       }
       if ( g_59_bo_134 && iTime(g_336_st_3130,g_58_in_130,g_51_in_114) <= local_13_da && iTime(g_336_st_3130,g_58_in_130,0) >  local_13_da && iClose(g_336_st_3130,g_58_in_130,1)<iOpen(g_336_st_3130,g_58_in_130,1) && iClose(g_336_st_3130,g_58_in_130,1)<local_10_do )
       {
         OrderClose(local_9_lo,local_12_do,MarketInfo(g_336_st_3130,MODE_BID),0,Red); 
         Print("closing candle confirmation"); 
       }
       if ( g_61_bo_13C && iTime(g_336_st_3130,g_60_in_138,g_51_in_114) <= local_13_da && iTime(g_336_st_3130,g_60_in_138,0) >  local_13_da && iClose(g_336_st_3130,g_60_in_138,1)<iOpen(g_336_st_3130,g_60_in_138,1) && iClose(g_336_st_3130,g_60_in_138,1)<local_10_do )
       {
         OrderClose(local_9_lo,local_12_do,MarketInfo(g_336_st_3130,MODE_BID),0,Red); 
         Print("closing candle confirmation"); 
       }
       g_247_do_2500 = g_129_do_318 ;
       if ( g_133_in_338 >  0 && TimeCurrent() >  local_13_da + g_133_in_338 * 60 )
       {
         g_247_do_2500 = g_134_do_340 ;
       }
       tmp_in_1 = g_190_in_518;
       tmp_lo_2 = local_9_lo;
       for (tmp_in_3 = 0 ; tmp_in_3 < 100 ; tmp_in_3=tmp_in_3 + 1)
       {
         if ( !(g_198_do_1070_si100si2[tmp_in_3][0]==tmp_lo_2) )   continue;
         tmp_do_4 = g_198_do_1070_si100si2[tmp_in_3][1];
         break;
         
       }
       tmp_do_4 = 0.0;
       local_17_do = NormalizeDouble(tmp_do_4,tmp_in_1) ;
       if ( local_17_do==0.0 )
       {
         tmp_do_5 = local_10_do;
         tmp_lo_6 = local_9_lo;
         for (tmp_in_7 = 0 ; tmp_in_7 < 100 ; tmp_in_7=tmp_in_7 + 1)
         {
           if ( !(g_198_do_1070_si100si2[tmp_in_7][0]==0.0) )   continue;
           g_198_do_1070_si100si2[tmp_in_7][0] = (double)tmp_lo_6;
           g_198_do_1070_si100si2[tmp_in_7][1] = tmp_do_5;
           break;
           
         }
         local_17_do = local_10_do ;
       }
       else
       {
         local_17_do = local_17_do - g_85_do_1C0 * g_229_do_1E00 ;
       }
       local_18_do = local_10_do - local_17_do ;
       local_19_bo = false ;
       if ( local_17_do>0.0 - g_85_do_1C0 * g_229_do_1E00 && local_18_do>g_38_do_C0 * g_229_do_1E00 )
       {
         local_19_bo = true ;
         if ( g_39_in_C8 == 2 )
         {
           g_247_do_2500 = -1000.0 ;
           Print("SlippageMode 2 active"); 
         }
       }
       if ( g_43_bo_E8 )
       {
         local_5_do = local_17_do ;
       }
       else
       {
         local_5_do = local_10_do ;
       }
       if ( local_7_do<NormalizeDouble(local_10_do - (g_100_do_230 + g_64_do_148) * g_229_do_1E00 - g_1_do_0,g_190_in_518) )
       {
         local_7_do = NormalizeDouble(local_10_do - (g_100_do_230 + g_64_do_148) * g_229_do_1E00 - g_1_do_0,g_190_in_518) ;
         OrderModify(local_9_lo,local_10_do,local_7_do,local_8_do,0,0xFFFFFFFF); 
       }
       if ( MarketInfo(g_336_st_3130,MODE_BID)<local_10_do - (g_100_do_230 + g_64_do_148) * g_229_do_1E00 - g_1_do_0 )
       {
         RefreshRates(); 
         OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_BID),(int)g_1_do_0,Red); 
         return(true); 
       }
       local_20_bo = false ;
       if ( g_159_bo_464 )
       {
         tmp_lo_8 = local_9_lo;
         tmp_in_9 = 0;
         for (tmp_in_10 = OrdersTotal() ; tmp_in_10 >= 0 ; tmp_in_10=tmp_in_10 - 1)
         {
           if ( OrderSelect(tmp_in_10,0,0) != true || OrderMagicNumber() != g_168_in_4A8 || OrderSymbol() != g_336_st_3130 )   continue;
           tmp_st_11 = OrderComment();
           if ( tmp_st_11 != IntegerToString(tmp_lo_8,0,32) )   continue;
           tmp_in_9=tmp_in_9 + 1;
           
         }
         local_21_do = tmp_in_9 ;
         local_22_bo = false ;
         if ( !(g_194_bo_530) )
         {
           g_194_bo_530 = true ;
           g_192_in_528 = 0 ;
         }
         if ( local_21_do==0.0 )
         {
           g_192_in_528 = 0 ;
         }
         if ( MathFloor(local_21_do / 2.0)==local_21_do / 2.0 )
         {
           g_192_in_528 = 0 ;
         }
         else
         {
           g_192_in_528 = 1 ;
         }
         if ( g_194_bo_530 )
         {
           if ( local_21_do>0.0 )
           {
             tmp_do_12 = AccountEquity();
             if ( tmp_do_12>AccountBalance() + g_163_do_480 )
             {
               for (tmp_in_13 = OrdersTotal() ; tmp_in_13 >= 0 ; tmp_in_13=tmp_in_13 - 1)
               {
                 if ( OrderSelect(tmp_in_13,0,0) != true )   continue;
                 
                 if ( ( OrderMagicNumber() != g_93_in_1F0 && OrderMagicNumber() != g_169_in_4AC && OrderMagicNumber() != g_168_in_4A8 ) )   continue;
                 
                 if ( OrderType() == 0 )
                 {
                   OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_BID),g_38_do_C0,Red); 
                 }
                 if ( OrderType() != 1 )   continue;
                 OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_ASK),g_38_do_C0,Red); 
                 
               }
             }
           }
           if ( local_21_do>0.0 )
           {
             tmp_lo_14 = local_9_lo;
             tmp_do_15 = 0.0;
             for (tmp_in_16 = OrdersTotal() ; tmp_in_16 >= 0 ; tmp_in_16=tmp_in_16 - 1)
             {
               if ( OrderSelect(tmp_in_16,0,0) != true )   continue;
               tmp_lo_17 = OrderTicket();
               if ( tmp_lo_17 != tmp_lo_14 )
               {
                 tmp_st_11 = OrderComment();
               if ( tmp_st_11 != IntegerToString(tmp_lo_14,0,32) )   continue;
               }
               tmp_do_15 = tmp_do_15 + OrderProfit();
               
             }
             if ( tmp_do_15>g_163_do_480 )
             {
               Print("Closing zone"); 
               tmp_lo_18 = local_9_lo;
               for (tmp_in_19 = OrdersTotal() ; tmp_in_19 >= 0 ; tmp_in_19=tmp_in_19 - 1)
               {
                 if ( OrderSelect(tmp_in_19,0,0) != true )   continue;
                 
                 if ( OrderMagicNumber() == g_93_in_1F0 && OrderTicket() == tmp_lo_18 )
                 {
                   OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_BID),3,Red); 
                 }
                 if ( OrderMagicNumber() != g_168_in_4A8 )   continue;
                 tmp_st_11 = OrderComment();
                 if ( tmp_st_11 != IntegerToString(tmp_lo_18,0,32) )   continue;
                 
                 if ( OrderType() == 0 )
                 {
                   OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_BID),g_38_do_C0,Red); 
                 }
                 if ( OrderType() != 1 )   continue;
                 OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_ASK),g_38_do_C0,Red); 
                 
               }
               g_194_bo_530 = false ;
               local_20_bo = true ;
             }
           }
           else
           {
             local_23_do = local_12_do * g_165_do_490 ;
             if ( g_164_in_488 == 2 )
             {
               local_23_do = (local_21_do + 1.0) * local_12_do + local_12_do ;
             }
             if ( g_164_in_488 == 3 )
             {
               local_23_do = local_12_do * (MathPow(g_165_do_490,local_21_do + 1.0)) ;
             }
             if ( g_192_in_528 == 0 )
             {
               local_24_do = local_21_do * g_161_do_470 * g_229_do_1E00 + (local_17_do - g_160_do_468 * g_229_do_1E00) ;
               if ( local_24_do>local_17_do - g_162_do_478 * g_229_do_1E00 )
               {
                 local_24_do = local_17_do - g_162_do_478 * g_229_do_1E00 ;
               }
               if ( MarketInfo(g_336_st_3130,MODE_BID)<local_24_do )
               {
                 if ( local_21_do>=g_166_in_498 )
                 {
                   for (tmp_in_20 = OrdersTotal() ; tmp_in_20 >= 0 ; tmp_in_20=tmp_in_20 - 1)
                   {
                     if ( OrderSelect(tmp_in_20,0,0) != true )   continue;
                     
                     if ( OrderMagicNumber() == g_93_in_1F0 && OrderTicket() == local_9_lo )
                     {
                       OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_BID),3,Red); 
                     }
                     if ( OrderMagicNumber() != g_168_in_4A8 )   continue;
                     tmp_st_11 = OrderComment();
                     if ( tmp_st_11 != IntegerToString(local_9_lo,0,32) )   continue;
                     
                     if ( OrderType() == 0 )
                     {
                       OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_BID),g_38_do_C0,Red); 
                     }
                     if ( OrderType() != 1 )   continue;
                     OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_ASK),g_38_do_C0,Red); 
                     
                   }
                 }
                 else
                 {
                   OrderSend(g_336_st_3130,1,local_23_do,MarketInfo(g_336_st_3130,MODE_BID),g_38_do_C0,0.0,0.0,IntegerToString(local_9_lo,0,32),g_168_in_4A8,0,Green); 
                   g_192_in_528 = 1 ;
                   local_22_bo = true ;
                 }
               }
             }
             else
             {
               local_25_do = local_17_do ;
               if ( MarketInfo(g_336_st_3130,MODE_ASK)>local_17_do )
               {
                 if ( local_21_do>=g_166_in_498 )
                 {
                   for (tmp_in_21 = OrdersTotal() ; tmp_in_21 >= 0 ; tmp_in_21=tmp_in_21 - 1)
                   {
                     if ( OrderSelect(tmp_in_21,0,0) != true )   continue;
                     
                     if ( OrderMagicNumber() == g_93_in_1F0 && OrderTicket() == local_9_lo )
                     {
                       OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_BID),3,Red); 
                     }
                     if ( OrderMagicNumber() != g_168_in_4A8 )   continue;
                     tmp_st_22 = OrderComment();
                     if ( tmp_st_22 != IntegerToString(local_9_lo,0,32) )   continue;
                     
                     if ( OrderType() == 0 )
                     {
                       OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_BID),g_38_do_C0,Red); 
                     }
                     if ( OrderType() != 1 )   continue;
                     OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_ASK),g_38_do_C0,Red); 
                     
                   }
                 }
                 else
                 {
                   OrderSend(g_336_st_3130,0,local_23_do,MarketInfo(g_336_st_3130,MODE_ASK),g_38_do_C0,0.0,0.0,IntegerToString(local_9_lo,0,32),g_168_in_4A8,0,Green); 
                   g_192_in_528 = 0 ;
                   local_22_bo = true ;
                 }
               }
             }
           }
         }
         if ( ( local_21_do>0.0 || local_22_bo ) )
         {
           local_20_bo = true ;
         }
       }
       if ( !(local_20_bo) )
       {
         if ( ( g_63_in_140 == 1 || (g_63_in_140 != 3 && g_63_in_140 != 2) ) )
         {
           tmp_lo_23 = local_9_lo;
           tmp_do_24 = g_100_do_230;
           tmp_do_25 = local_10_do;
           tmp_in_26 = 1;
           tmp_do_27 = 0.0;
           tmp_bo_28 = false;
           for (tmp_in_29 = 0 ; tmp_in_29 < g_199_in_16B0 ; tmp_in_29=tmp_in_29 + 1)
           {
             if ( g_196_do_568_si20si2[tmp_in_29][0]==tmp_lo_23 )
             {
               tmp_do_27 = g_196_do_568_si20si2[tmp_in_29][1];
               tmp_bo_28 = true;
               break;
             }
           }
           if ( !(tmp_bo_28) )
           {
             if ( tmp_in_26 == 1 )
             {
               tmp_do_27 = NormalizeDouble(tmp_do_25 - tmp_do_24 * g_229_do_1E00,g_190_in_518);
             }
             if ( tmp_in_26 == 2 )
             {
               tmp_do_27 = NormalizeDouble(tmp_do_24 * g_229_do_1E00 + tmp_do_25,g_190_in_518);
             }
             for (tmp_in_30 = 0 ; tmp_in_30 < g_199_in_16B0 ; tmp_in_30=tmp_in_30 + 1)
             {
               if ( g_196_do_568_si20si2[tmp_in_30][0]==0.0 )
               {
                 g_196_do_568_si20si2[tmp_in_30][0] = (double)tmp_lo_23;
                 g_196_do_568_si20si2[tmp_in_30][1] = tmp_do_27;
                 break;
               }
             }
           }
           g_191_do_520 = tmp_do_27 ;
           local_4_do = g_191_do_520 ;
           if ( MarketInfo(g_336_st_3130,MODE_BID)<local_4_do )
           {
             Print("Closing with virtual SL"); 
             RefreshRates(); 
             OrderClose(local_9_lo,local_12_do,MarketInfo(g_336_st_3130,MODE_BID),(int)g_1_do_0,0xFFFFFFFF); 
             return(true); 
           }
           if ( g_125_do_2F8>0.0 && TimeCurrent() >= local_13_da + g_304_in_287C && MarketInfo(g_336_st_3130,MODE_BID)>NormalizeDouble(g_126_do_300 * g_229_do_1E00 + (local_7_do + g_337_do_3140),g_190_in_518) && MarketInfo(g_336_st_3130,MODE_BID)<local_8_do - g_309_do_2898 )
           {
             local_7_do = NormalizeDouble(MarketInfo(g_336_st_3130,MODE_BID) - g_126_do_300 * g_229_do_1E00,g_190_in_518) ;
             if ( local_7_do<MarketInfo(g_336_st_3130,MODE_BID) - g_221_do_1A80 )
             {
               g_230_in_1E08 = OrderModify(local_9_lo,local_10_do,local_7_do,local_8_do,0,0xFFFFFFFF) ;
               if ( g_230_in_1E08 <= 0 )
               {
                 Print("TrailStop error: \'" + lizong_21(GetLastError()) + "\' when setting trailing Exit_TrailSL_after_X_Minutes_size loss.  Trying again!"); 
               }
               local_2_bo = true ;
             }
           }
           if ( g_103_do_250>0.0 && MarketInfo(g_336_st_3130,MODE_BID)>NormalizeDouble((g_103_do_250 + g_106_do_268) * g_229_do_1E00 + (local_7_do + g_337_do_3140),g_190_in_518) && MarketInfo(g_336_st_3130,MODE_BID)>NormalizeDouble(g_104_do_258 * g_229_do_1E00 + local_5_do,g_190_in_518) && MarketInfo(g_336_st_3130,MODE_BID)<local_8_do - g_309_do_2898 && local_7_do<NormalizeDouble(g_105_do_260 * g_229_do_1E00 + local_10_do,g_190_in_518) )
           {
             local_7_do = NormalizeDouble(MarketInfo(g_336_st_3130,MODE_BID) - g_103_do_250 * g_229_do_1E00,g_190_in_518) ;
             if ( local_7_do<MarketInfo(g_336_st_3130,MODE_BID) - g_221_do_1A80 )
             {
               g_230_in_1E08 = OrderModify(local_9_lo,local_10_do,local_7_do,local_8_do,0,0xFFFFFFFF) ;
               if ( g_230_in_1E08 <= 0 )
               {
                 Print("TrailStop error: \'" + lizong_21(GetLastError()) + "\' when setting trailing Exit_stop loss.  Trying again!"); 
               }
               else
               {
                 local_26_do = NormalizeDouble(g_107_do_270 / 100.0 * g_223_do_1AC4_si99[g_328_in_3100],2) ;
                 if ( local_26_do<local_12_do && local_26_do>=MarketInfo(g_336_st_3130,MODE_LOTSTEP) )
                 {
                   OrderClose(local_9_lo,local_26_do,MarketInfo(g_336_st_3130,MODE_BID),g_38_do_C0,Red); 
                   return(true); 
                 }
               }
               local_2_bo = true ;
             }
           }
           if ( g_108_do_278>0.0 && MarketInfo(g_336_st_3130,MODE_ASK)<NormalizeDouble(local_8_do - g_337_do_3140 - g_108_do_278 * g_229_do_1E00,g_190_in_518) && MarketInfo(g_336_st_3130,MODE_ASK)<NormalizeDouble(local_5_do - g_109_do_280 * g_229_do_1E00,g_190_in_518) && MarketInfo(g_336_st_3130,MODE_BID)<local_8_do - g_309_do_2898 )
           {
             local_8_do = NormalizeDouble(MarketInfo(g_336_st_3130,MODE_BID) + g_108_do_278 * g_229_do_1E00,g_190_in_518) ;
             if ( local_8_do>MarketInfo(g_336_st_3130,MODE_ASK) + g_221_do_1A80 )
             {
               g_230_in_1E08 = OrderModify(local_9_lo,local_10_do,local_7_do,local_8_do,0,0xFFFFFFFF) ;
               if ( g_230_in_1E08 <= 0 )
               {
                 Print("TrailStop error: \'" + lizong_21(GetLastError()) + "\' when setting trailing Exit_TP.  Trying again!"); 
               }
               else
               {
                 local_27_do = NormalizeDouble(g_107_do_270 / 100.0 * g_223_do_1AC4_si99[g_328_in_3100],2) ;
                 if ( local_27_do<local_12_do && local_27_do>=SymbolInfoDouble(g_336_st_3130,34) )
                 {
                   OrderClose(local_9_lo,local_27_do,MarketInfo(g_336_st_3130,MODE_BID),g_38_do_C0,Red); 
                   return(true); 
                 }
               }
               local_2_bo = true ;
             }
           }
           if ( local_19_bo && g_39_in_C8 == 1 && g_41_do_D8>0.0 && MarketInfo(g_336_st_3130,MODE_BID)>NormalizeDouble(g_41_do_D8 * g_229_do_1E00 + (local_7_do + g_337_do_3140),g_190_in_518) && MarketInfo(g_336_st_3130,MODE_BID)>NormalizeDouble(g_40_do_D0 * g_229_do_1E00 + local_17_do,g_190_in_518) && MarketInfo(g_336_st_3130,MODE_BID)<local_8_do - g_309_do_2898 && local_7_do<NormalizeDouble(g_42_do_E0 * g_229_do_1E00 + local_10_do,g_190_in_518) )
           {
             local_7_do = NormalizeDouble(MarketInfo(g_336_st_3130,MODE_BID) - g_41_do_D8 * g_229_do_1E00,g_190_in_518) ;
             if ( local_7_do<MarketInfo(g_336_st_3130,MODE_BID) - g_221_do_1A80 )
             {
               g_230_in_1E08 = OrderModify(local_9_lo,local_10_do,local_7_do,local_8_do,0,0xFFFFFFFF) ;
               if ( g_230_in_1E08 <= 0 )
               {
                 Print("TrailStop error: \'" + lizong_21(GetLastError()) + "\' when setting Slip TL.  Trying again!"); 
               }
               else
               {
                 Print("Slippage control active"); 
               }
               local_2_bo = true ;
             }
           }
           if ( g_119_in_2D0 >  0 && g_120_in_2D4 >= 0 && UseHL_TrailingSL && g_242_do_21C4_si99[g_328_in_3100]>NormalizeDouble(local_7_do + g_221_do_1A80 + g_337_do_3140,g_190_in_518) && g_242_do_21C4_si99[g_328_in_3100]<MarketInfo(g_336_st_3130,MODE_BID) - g_121_in_2D8 * g_229_do_1E00 && ( g_242_do_21C4_si99[g_328_in_3100]<local_10_do || !(g_116_bo_2C4) ) && g_242_do_21C4_si99[g_328_in_3100]<NormalizeDouble(MarketInfo(g_336_st_3130,MODE_BID) - g_122_in_2DC * g_229_do_1E00 - g_221_do_1A80 - g_337_do_3140,g_190_in_518) && MarketInfo(g_336_st_3130,MODE_BID)<local_8_do - g_309_do_2898 )
           {
             local_7_do = NormalizeDouble(g_242_do_21C4_si99[g_328_in_3100],g_190_in_518) ;
             if ( local_7_do<MarketInfo(g_336_st_3130,MODE_BID) - g_221_do_1A80 )
             {
               g_230_in_1E08 = OrderModify(local_9_lo,local_10_do,local_7_do,local_8_do,0,0xFFFFFFFF) ;
               if ( g_230_in_1E08 <= 0 )
               {
                 Print("error: \'" + lizong_21(GetLastError()) + "\' when modifying stoploss"); 
               }
               local_2_bo = true ;
             }
           }
           if ( g_113_do_2A8>0.0 && MarketInfo(g_336_st_3130,MODE_BID)>NormalizeDouble(g_113_do_2A8 * g_229_do_1E00 + local_10_do,g_190_in_518) && NormalizeDouble(g_114_do_2B0 * g_229_do_1E00 + local_10_do,g_190_in_518)>local_7_do + g_337_do_3140 && MarketInfo(g_336_st_3130,MODE_BID)>NormalizeDouble(g_114_do_2B0 * g_229_do_1E00 + local_10_do + g_221_do_1A80,g_190_in_518) && MarketInfo(g_336_st_3130,MODE_BID)<local_8_do - g_309_do_2898 )
           {
             local_7_do = NormalizeDouble(g_114_do_2B0 * g_229_do_1E00 + local_10_do,g_190_in_518) ;
             if ( local_7_do<MarketInfo(g_336_st_3130,MODE_BID) - g_221_do_1A80 )
             {
               g_230_in_1E08 = OrderModify(local_9_lo,local_10_do,local_7_do,local_8_do,0,0xFFFFFFFF) ;
               if ( g_230_in_1E08 <= 0 )
               {
                 Print("error when setting breakeven: \'" + lizong_21(GetLastError()) + "\' ..\'Exit_BE_start\' to close to \'Exit_BE_extra_pips\' ..trying again!"); 
               }
               local_2_bo = true ;
             }
           }
           if ( !(local_2_bo) && ( g_128_in_314 == 1 || (g_128_in_314 == 2 && g_131_do_328 * g_229_do_1E00 + local_7_do<=g_132_do_330 * g_229_do_1E00 + (local_5_do + g_1_do_0)) ) )
           {
             g_250_in_2518 ++;
             if ( MarketInfo(g_336_st_3130,MODE_BID)>g_131_do_328 * g_229_do_1E00 + local_7_do + g_221_do_1A80 && MarketInfo(g_336_st_3130,MODE_BID)<local_8_do - g_309_do_2898 && ( g_129_do_318==0.0 || MarketInfo(g_336_st_3130,MODE_BID)>g_247_do_2500 * g_229_do_1E00 + local_5_do ) && g_250_in_2518 >= g_130_in_320 && NormalizeDouble(g_131_do_328 * g_229_do_1E00 + local_7_do,g_190_in_518)>local_7_do )
             {
               g_250_in_2518 = 0 ;
               local_7_do = NormalizeDouble(g_131_do_328 * g_229_do_1E00 + local_7_do,g_190_in_518) ;
               OrderModify(local_9_lo,local_10_do,local_7_do,local_8_do,0,0xFFFFFFFF); 
               local_2_bo = true ;
             }
           }
           g_191_do_520 = local_7_do ;
           if ( MarketInfo(g_336_st_3130,MODE_BID)<local_7_do )
           {
             Print("Closing with virtual SL"); 
             RefreshRates(); 
             OrderClose(local_9_lo,local_12_do,MarketInfo(g_336_st_3130,MODE_BID),(int)g_1_do_0,0xFFFFFFFF); 
             return(true); 
           }
           if ( NormalizeDouble(local_4_do,g_190_in_518)!=NormalizeDouble(g_191_do_520,g_190_in_518) )
           {
             tmp_do_31 = NormalizeDouble(g_191_do_520,g_190_in_518);
             tmp_lo_32 = local_9_lo;
             for (tmp_in_33 = 0 ; tmp_in_33 < g_199_in_16B0 ; tmp_in_33=tmp_in_33 + 1)
             {
               if ( g_196_do_568_si20si2[tmp_in_33][0]==tmp_lo_32 )
               {
                 g_196_do_568_si20si2[tmp_in_33][1] = tmp_do_31;
                 break;
               }
             }
           }
           if ( local_2_bo && g_135_bo_348 )
           {
             return(true); 
           }
         }
         if ( ( g_63_in_140 == 2 || g_63_in_140 == 3 ) )
         {
           tmp_lo_34 = local_9_lo;
           tmp_do_35 = g_100_do_230;
           tmp_do_36 = local_10_do;
           tmp_in_37 = 1;
           tmp_do_38 = 0.0;
           tmp_bo_39 = false;
           for (tmp_in_40 = 0 ; tmp_in_40 < g_199_in_16B0 ; tmp_in_40=tmp_in_40 + 1)
           {
             if ( g_196_do_568_si20si2[tmp_in_40][0]==tmp_lo_34 )
             {
               tmp_do_38 = g_196_do_568_si20si2[tmp_in_40][1];
               tmp_bo_39 = true;
               break;
             }
           }
           if ( !(tmp_bo_39) )
           {
             if ( tmp_in_37 == 1 )
             {
               tmp_do_38 = NormalizeDouble(tmp_do_36 - tmp_do_35 * g_229_do_1E00,g_190_in_518);
             }
             if ( tmp_in_37 == 2 )
             {
               tmp_do_38 = NormalizeDouble(tmp_do_35 * g_229_do_1E00 + tmp_do_36,g_190_in_518);
             }
             for (tmp_in_41 = 0 ; tmp_in_41 < g_199_in_16B0 ; tmp_in_41=tmp_in_41 + 1)
             {
               if ( g_196_do_568_si20si2[tmp_in_41][0]==0.0 )
               {
                 g_196_do_568_si20si2[tmp_in_41][0] = (double)tmp_lo_34;
                 g_196_do_568_si20si2[tmp_in_41][1] = tmp_do_38;
                 break;
               }
             }
           }
           g_191_do_520 = tmp_do_38 ;
           local_4_do = g_191_do_520 ;
           if ( MarketInfo(g_336_st_3130,MODE_BID)<=local_4_do )
           {
             RefreshRates(); 
             OrderClose(local_9_lo,local_12_do,MarketInfo(g_336_st_3130,MODE_BID),(int)g_1_do_0,0xFFFFFFFF); 
             return(true); 
           }
           local_28_in = (int)(TimeCurrent() - g_319_da_28E0) ;
           if ( local_28_in >= g_65_in_150 )
           {
             if ( NormalizeDouble(g_191_do_520,g_190_in_518)>local_7_do + g_337_do_3140 )
             {
               OrderModify(local_9_lo,local_10_do,NormalizeDouble(g_191_do_520,g_190_in_518),local_8_do,0,0xFFFFFFFF); 
             }
             g_319_da_28E0 = TimeCurrent() ;
           }
           if ( g_125_do_2F8>0.0 && TimeCurrent() >= local_13_da + g_304_in_287C && MarketInfo(g_336_st_3130,MODE_BID)>g_126_do_300 * g_229_do_1E00 + (g_191_do_520 + g_337_do_3140) && MarketInfo(g_336_st_3130,MODE_BID)<local_8_do - g_309_do_2898 )
           {
             local_2_bo = true ;
             g_191_do_520 = MarketInfo(g_336_st_3130,MODE_BID) - g_126_do_300 * g_229_do_1E00 ;
           }
           if ( g_103_do_250>0.0 && MarketInfo(g_336_st_3130,MODE_BID)>(g_103_do_250 + g_106_do_268) * g_229_do_1E00 + (g_191_do_520 + g_337_do_3140) && MarketInfo(g_336_st_3130,MODE_BID)>g_104_do_258 * g_229_do_1E00 + local_5_do && g_191_do_520<g_105_do_260 * g_229_do_1E00 + local_10_do )
           {
             local_2_bo = true ;
             g_191_do_520 = MarketInfo(g_336_st_3130,MODE_BID) - g_103_do_250 * g_229_do_1E00 ;
             local_29_do = NormalizeDouble(g_107_do_270 / 100.0 * g_223_do_1AC4_si99[g_328_in_3100],2) ;
             if ( local_29_do<local_12_do && local_29_do>=MarketInfo(g_336_st_3130,MODE_LOTSTEP) )
             {
               OrderClose(local_9_lo,local_29_do,MarketInfo(g_336_st_3130,MODE_BID),g_38_do_C0,Red); 
               return(true); 
             }
           }
           if ( local_19_bo && g_39_in_C8 == 1 && g_41_do_D8>0.0 && MarketInfo(g_336_st_3130,MODE_BID)>g_41_do_D8 * g_229_do_1E00 + (g_191_do_520 + g_337_do_3140) && MarketInfo(g_336_st_3130,MODE_BID)>g_40_do_D0 * g_229_do_1E00 + local_17_do && MarketInfo(g_336_st_3130,MODE_BID)<local_8_do - g_309_do_2898 && g_191_do_520<g_42_do_E0 * g_229_do_1E00 + local_10_do )
           {
             Print("Slippage control active"); 
             local_2_bo = true ;
             g_191_do_520 = MarketInfo(g_336_st_3130,MODE_BID) - g_41_do_D8 * g_229_do_1E00 ;
           }
           if ( g_119_in_2D0 >  0 && g_120_in_2D4 >= 0 && g_242_do_21C4_si99[g_328_in_3100]>g_191_do_520 + g_221_do_1A80 + g_337_do_3140 && ( g_242_do_21C4_si99[g_328_in_3100]<local_10_do || !(g_116_bo_2C4) ) && g_242_do_21C4_si99[g_328_in_3100]<MarketInfo(g_336_st_3130,MODE_BID) - g_122_in_2DC * g_229_do_1E00 - g_221_do_1A80 - g_337_do_3140 && MarketInfo(g_336_st_3130,MODE_BID)<local_8_do - g_309_do_2898 )
           {
             g_191_do_520 = g_242_do_21C4_si99[g_328_in_3100] ;
             local_2_bo = true ;
           }
           if ( g_113_do_2A8>0.0 && g_63_in_140 == 3 && MarketInfo(g_336_st_3130,MODE_BID)>g_113_do_2A8 * g_229_do_1E00 + local_10_do && g_114_do_2B0 * g_229_do_1E00 + local_10_do>local_7_do + g_337_do_3140 && MarketInfo(g_336_st_3130,MODE_BID)>g_114_do_2B0 * g_229_do_1E00 + local_10_do + g_221_do_1A80 && MarketInfo(g_336_st_3130,MODE_BID)<local_8_do - g_309_do_2898 && NormalizeDouble(g_114_do_2B0 * g_229_do_1E00 + local_10_do,g_190_in_518)>OrderStopLoss() )
           {
             g_191_do_520 = NormalizeDouble(g_114_do_2B0 * g_229_do_1E00 + local_10_do,g_190_in_518) ;
             g_230_in_1E08 = OrderModify(local_9_lo,local_10_do,g_191_do_520,local_8_do,0,0xFFFFFFFF) ;
             if ( g_230_in_1E08 <= 0 )
             {
               Print("error when setting breakeven: \'" + lizong_21(GetLastError()) + "\' ..\'Exit_BE_start\' to close to \'Exit_BE_extra_pips\' ..trying again!"); 
             }
             local_2_bo = true ;
           }
           if ( g_113_do_2A8>0.0 && g_63_in_140 == 2 && MarketInfo(g_336_st_3130,MODE_BID)>g_113_do_2A8 * g_229_do_1E00 + local_10_do && g_114_do_2B0 * g_229_do_1E00 + local_10_do>g_191_do_520 + g_337_do_3140 && MarketInfo(g_336_st_3130,MODE_BID)>g_114_do_2B0 * g_229_do_1E00 + local_10_do + g_221_do_1A80 && MarketInfo(g_336_st_3130,MODE_BID)<local_8_do - g_309_do_2898 )
           {
             g_191_do_520 = g_114_do_2B0 * g_229_do_1E00 + local_10_do ;
             local_2_bo = true ;
           }
           if ( !(local_2_bo) && ( g_128_in_314 == 1 || (g_128_in_314 == 2 && g_131_do_328 * g_229_do_1E00 + g_191_do_520<=g_132_do_330 * g_229_do_1E00 + (local_5_do + g_1_do_0)) ) )
           {
             g_250_in_2518 ++;
             if ( MarketInfo(g_336_st_3130,MODE_BID)>g_131_do_328 * g_229_do_1E00 + g_191_do_520 + g_221_do_1A80 && MarketInfo(g_336_st_3130,MODE_BID)<local_8_do - g_309_do_2898 && ( g_129_do_318==0.0 || MarketInfo(g_336_st_3130,MODE_BID)>g_247_do_2500 * g_229_do_1E00 + local_5_do ) && g_250_in_2518 >= g_130_in_320 )
             {
               g_250_in_2518 = 0 ;
               g_191_do_520 = g_131_do_328 * g_229_do_1E00 + g_191_do_520 ;
               local_2_bo = true ;
             }
           }
           if ( MarketInfo(g_336_st_3130,MODE_BID)<=g_191_do_520 )
           {
             RefreshRates(); 
             OrderClose(local_9_lo,local_12_do,MarketInfo(g_336_st_3130,MODE_BID),(int)g_1_do_0,0xFFFFFFFF); 
             return(true); 
           }
           if ( NormalizeDouble(local_4_do,g_190_in_518)!=NormalizeDouble(g_191_do_520,g_190_in_518) )
           {
             tmp_do_42 = NormalizeDouble(g_191_do_520,g_190_in_518);
             tmp_lo_43 = local_9_lo;
             for (tmp_in_44 = 0 ; tmp_in_44 < g_199_in_16B0 ; tmp_in_44=tmp_in_44 + 1)
             {
               if ( g_196_do_568_si20si2[tmp_in_44][0]==tmp_lo_43 )
               {
                 g_196_do_568_si20si2[tmp_in_44][1] = tmp_do_42;
                 break;
               }
             }
           }
         }
       }
     }
     if ( local_2_bo )
     {
       local_3_bo = true ;
     }
   }
   if ( local_2_bo )
   {
     local_3_bo = true ;
   }
 }
 return(local_3_bo); 
 }
//lizong_18 <<==--------   --------
 bool lizong_19()
 {
  bool      local_2_bo = false;
  bool      local_3_bo = false;
  double    local_4_do;
  double    local_5_do;
  int       local_6_in;
  double    local_7_do;
  double    local_8_do;
  long      local_9_lo;
  double    local_10_do;
  string    local_11_st;
  double    local_12_do;
  datetime  local_13_da;
  int       local_14_in;
  int       local_15_in;
  string    local_16_st;
  double    local_17_do;
  double    local_18_do;
  bool      local_19_bo;
  bool      local_20_bo;
  double    local_21_do;
  bool      local_22_bo;
  double    local_23_do;
  double    local_24_do;
  double    local_25_do;
  double    local_26_do;
  double    local_27_do;
  int       local_28_in;
  double    local_29_do;
//----- -----
 int        tmp_in_1;
 long       tmp_lo_2;
 int        tmp_in_3;
 double     tmp_do_4;
 double     tmp_do_5;
 long       tmp_lo_6;
 int        tmp_in_7;
 long       tmp_lo_8;
 int        tmp_in_9;
 int        tmp_in_10;
 string     tmp_st_11;
 double     tmp_do_12;
 int        tmp_in_13;
 long       tmp_lo_14;
 double     tmp_do_15;
 int        tmp_in_16;
 long       tmp_lo_17;
 long       tmp_lo_18;
 int        tmp_in_19;
 int        tmp_in_20;
 int        tmp_in_21;
 string     tmp_st_22;
 long       tmp_lo_23;
 double     tmp_do_24;
 double     tmp_do_25;
 int        tmp_in_26;
 double     tmp_do_27;
 bool       tmp_bo_28;
 int        tmp_in_29;
 int        tmp_in_30;
 double     tmp_do_31;
 long       tmp_lo_32;
 int        tmp_in_33;
 long       tmp_lo_34;
 double     tmp_do_35;
 double     tmp_do_36;
 int        tmp_in_37;
 double     tmp_do_38;
 bool       tmp_bo_39;
 int        tmp_in_40;
 int        tmp_in_41;
 double     tmp_do_42;
 long       tmp_lo_43;
 int        tmp_in_44;

 local_4_do = 0.0 ;
 local_5_do = 0.0 ;
 for (local_6_in = 0 ; local_6_in < OrdersTotal() ; local_6_in ++)
 {
   if ( OrderSelect(local_6_in,0,0) == true )
   {
     local_2_bo = false ;
     local_7_do = NormalizeDouble(OrderStopLoss(),g_190_in_518) ;
     local_8_do = NormalizeDouble(OrderTakeProfit(),g_190_in_518) ;
     local_9_lo = OrderTicket() ;
     local_10_do = NormalizeDouble(OrderOpenPrice(),g_190_in_518) ;
     local_11_st = OrderComment() ;
     local_12_do = OrderLots() ;
     local_13_da = OrderOpenTime() ;
     local_14_in = OrderType() ;
     local_15_in = OrderMagicNumber() ;
     local_16_st = OrderSymbol() ;
     if ( ( local_14_in == 5 || local_14_in == 3 ) && g_69_in_160 == 2 && ( g_95_in_204 == 0 || (g_95_in_204 == 1 && local_16_st == g_336_st_3130) ) && ( local_15_in == g_96_in_208 || g_96_in_208 == 0 ) && ( local_11_st == g_97_st_210 || g_97_st_210 == "" ) )
     {
       if ( ( local_7_do==0.0 || local_7_do==0.0 ) )
       {
         local_7_do = NormalizeDouble(g_100_do_230 * g_229_do_1E00 + local_10_do,g_190_in_518) ;
         OrderModify(local_9_lo,local_10_do,local_7_do,local_8_do,0,Green); 
       }
       if ( ( local_8_do==0.0 || local_8_do==0.0 ) )
       {
         local_8_do = NormalizeDouble(local_10_do - g_101_do_238 * g_229_do_1E00,g_190_in_518) ;
         OrderModify(local_9_lo,local_10_do,local_7_do,local_8_do,0,Green); 
       }
     }
     if ( local_14_in == 1 && ( ( local_15_in == g_93_in_1F0 && g_69_in_160 == 1 && local_16_st == g_336_st_3130 ) || (g_69_in_160 == 2 && ( g_95_in_204 == 0 || (g_95_in_204 == 1 && local_16_st == g_336_st_3130) ) && ( local_15_in == g_96_in_208 || g_96_in_208 == 0 ) && (local_11_st == g_97_st_210 || g_97_st_210 == "")) ) )
     {
       if ( ( local_7_do==0.0 || local_7_do==0.0 ) )
       {
         local_7_do = NormalizeDouble(g_100_do_230 * g_229_do_1E00 + local_10_do,g_190_in_518) ;
         OrderModify(local_9_lo,local_10_do,local_7_do,local_8_do,0,Green); 
       }
       if ( ( local_8_do==0.0 || local_8_do==0.0 ) )
       {
         local_8_do = NormalizeDouble(local_10_do - g_101_do_238 * g_229_do_1E00,g_190_in_518) ;
         OrderModify(local_9_lo,local_10_do,local_7_do,local_8_do,0,Green); 
       }
       if ( g_53_bo_11C && iTime(g_336_st_3130,g_52_in_118,g_51_in_114) <= local_13_da && iTime(g_336_st_3130,g_52_in_118,0) >  local_13_da && iClose(g_336_st_3130,g_52_in_118,1)>iOpen(g_336_st_3130,g_52_in_118,1) && iClose(g_336_st_3130,g_52_in_118,1)>local_10_do )
       {
         OrderClose(local_9_lo,local_12_do,MarketInfo(g_336_st_3130,MODE_ASK),0,Red); 
         Print("closing candle confirmation"); 
       }
       if ( g_55_bo_124 && iTime(g_336_st_3130,g_54_in_120,g_51_in_114) <= local_13_da && iTime(g_336_st_3130,g_54_in_120,0) >  local_13_da && iClose(g_336_st_3130,g_54_in_120,1)>iOpen(g_336_st_3130,g_54_in_120,1) && iClose(g_336_st_3130,g_54_in_120,1)>local_10_do )
       {
         OrderClose(local_9_lo,local_12_do,MarketInfo(g_336_st_3130,MODE_ASK),0,Red); 
         Print("closing candle confirmation"); 
       }
       if ( g_57_bo_12C && iTime(g_336_st_3130,g_56_in_128,g_51_in_114) <= local_13_da && iTime(g_336_st_3130,g_56_in_128,0) >  local_13_da && iClose(g_336_st_3130,g_56_in_128,1)>iOpen(g_336_st_3130,g_56_in_128,1) && iClose(g_336_st_3130,g_56_in_128,1)>local_10_do )
       {
         OrderClose(local_9_lo,local_12_do,MarketInfo(g_336_st_3130,MODE_ASK),0,Red); 
         Print("closing candle confirmation"); 
       }
       if ( g_59_bo_134 && iTime(g_336_st_3130,g_58_in_130,g_51_in_114) <= local_13_da && iTime(g_336_st_3130,g_58_in_130,0) >  local_13_da && iClose(g_336_st_3130,g_58_in_130,1)>iOpen(g_336_st_3130,g_58_in_130,1) && iClose(g_336_st_3130,g_58_in_130,1)>local_10_do )
       {
         OrderClose(local_9_lo,local_12_do,MarketInfo(g_336_st_3130,MODE_ASK),0,Red); 
         Print("closing candle confirmation"); 
       }
       if ( g_61_bo_13C && iTime(g_336_st_3130,g_60_in_138,g_51_in_114) <= local_13_da && iTime(g_336_st_3130,g_60_in_138,0) >  local_13_da && iClose(g_336_st_3130,g_60_in_138,1)>iOpen(g_336_st_3130,g_60_in_138,1) && iClose(g_336_st_3130,g_60_in_138,1)>local_10_do )
       {
         OrderClose(local_9_lo,local_12_do,MarketInfo(g_336_st_3130,MODE_ASK),0,Red); 
         Print("closing candle confirmation"); 
       }
       g_247_do_2500 = g_129_do_318 ;
       if ( g_133_in_338 >  0 && TimeCurrent() >  local_13_da + g_133_in_338 * 60 )
       {
         g_247_do_2500 = g_134_do_340 ;
       }
       tmp_in_1 = g_190_in_518;
       tmp_lo_2 = local_9_lo;
       for (tmp_in_3 = 0 ; tmp_in_3 < 100 ; tmp_in_3=tmp_in_3 + 1)
       {
         if ( !(g_198_do_1070_si100si2[tmp_in_3][0]==tmp_lo_2) )   continue;
         tmp_do_4 = g_198_do_1070_si100si2[tmp_in_3][1];
         break;
         
       }
       tmp_do_4 = 0.0;
       local_17_do = NormalizeDouble(tmp_do_4,tmp_in_1) ;
       if ( local_17_do==0.0 )
       {
         tmp_do_5 = local_10_do;
         tmp_lo_6 = local_9_lo;
         for (tmp_in_7 = 0 ; tmp_in_7 < 100 ; tmp_in_7=tmp_in_7 + 1)
         {
           if ( !(g_198_do_1070_si100si2[tmp_in_7][0]==0.0) )   continue;
           g_198_do_1070_si100si2[tmp_in_7][0] = (double)tmp_lo_6;
           g_198_do_1070_si100si2[tmp_in_7][1] = tmp_do_5;
           break;
           
         }
         local_17_do = local_10_do ;
       }
       else
       {
         local_17_do = local_17_do - g_85_do_1C0 * g_229_do_1E00 ;
       }
       local_18_do = local_17_do - local_10_do ;
       local_19_bo = false ;
       if ( local_17_do>g_85_do_1C0 * g_229_do_1E00 && local_18_do>g_38_do_C0 * g_229_do_1E00 )
       {
         local_19_bo = true ;
         if ( g_39_in_C8 == 2 )
         {
           g_247_do_2500 = -1000.0 ;
           Print("Slippage Mode 2 active"); 
         }
       }
       if ( g_43_bo_E8 )
       {
         local_5_do = local_17_do ;
       }
       else
       {
         local_5_do = local_10_do ;
       }
       if ( local_7_do>NormalizeDouble((g_100_do_230 + g_64_do_148) * g_229_do_1E00 + local_10_do + g_1_do_0,g_190_in_518) )
       {
         local_7_do = NormalizeDouble((g_100_do_230 + g_64_do_148) * g_229_do_1E00 + local_10_do + g_1_do_0,g_190_in_518) ;
         OrderModify(local_9_lo,local_10_do,local_7_do,local_8_do,0,0xFFFFFFFF); 
       }
       if ( MarketInfo(g_336_st_3130,MODE_ASK)>(g_100_do_230 + g_64_do_148) * g_229_do_1E00 + local_10_do + g_1_do_0 )
       {
         RefreshRates(); 
         OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_ASK),(int)g_1_do_0,Red); 
         return(true); 
       }
       local_20_bo = false ;
       if ( g_159_bo_464 )
       {
         tmp_lo_8 = local_9_lo;
         tmp_in_9 = 0;
         for (tmp_in_10 = OrdersTotal() ; tmp_in_10 >= 0 ; tmp_in_10=tmp_in_10 - 1)
         {
           if ( OrderSelect(tmp_in_10,0,0) != true || OrderMagicNumber() != g_169_in_4AC || OrderSymbol() != g_336_st_3130 )   continue;
           tmp_st_11 = OrderComment();
           if ( tmp_st_11 != IntegerToString(tmp_lo_8,0,32) )   continue;
           tmp_in_9=tmp_in_9 + 1;
           
         }
         local_21_do = tmp_in_9 ;
         local_22_bo = false ;
         if ( !(g_195_bo_531) )
         {
           g_195_bo_531 = true ;
           g_193_in_52C = 1 ;
         }
         if ( local_21_do==0.0 )
         {
           g_193_in_52C = 1 ;
         }
         if ( MathFloor(local_21_do / 2.0)==local_21_do / 2.0 )
         {
           g_193_in_52C = 1 ;
         }
         else
         {
           g_193_in_52C = 0 ;
         }
         if ( g_195_bo_531 )
         {
           if ( local_21_do>0.0 )
           {
             tmp_do_12 = AccountEquity();
             if ( tmp_do_12>AccountBalance() + g_163_do_480 )
             {
               for (tmp_in_13 = OrdersTotal() ; tmp_in_13 >= 0 ; tmp_in_13=tmp_in_13 - 1)
               {
                 if ( OrderSelect(tmp_in_13,0,0) != true )   continue;
                 
                 if ( ( OrderMagicNumber() != g_93_in_1F0 && OrderMagicNumber() != g_169_in_4AC && OrderMagicNumber() != g_168_in_4A8 ) )   continue;
                 
                 if ( OrderType() == 0 )
                 {
                   OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_BID),g_38_do_C0,Red); 
                 }
                 if ( OrderType() != 1 )   continue;
                 OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_ASK),g_38_do_C0,Red); 
                 
               }
             }
           }
           if ( local_21_do>0.0 )
           {
             tmp_lo_14 = local_9_lo;
             tmp_do_15 = 0.0;
             for (tmp_in_16 = OrdersTotal() ; tmp_in_16 >= 0 ; tmp_in_16=tmp_in_16 - 1)
             {
               if ( OrderSelect(tmp_in_16,0,0) != true )   continue;
               tmp_lo_17 = OrderTicket();
               if ( tmp_lo_17 != tmp_lo_14 )
               {
                 tmp_st_11 = OrderComment();
               if ( tmp_st_11 != IntegerToString(tmp_lo_14,0,32) )   continue;
               }
               tmp_do_15 = tmp_do_15 + OrderProfit();
               
             }
             if ( tmp_do_15>g_163_do_480 )
             {
               Print("Closing zone"); 
               tmp_lo_18 = local_9_lo;
               for (tmp_in_19 = OrdersTotal() ; tmp_in_19 >= 0 ; tmp_in_19=tmp_in_19 - 1)
               {
                 if ( OrderSelect(tmp_in_19,0,0) != true )   continue;
                 
                 if ( OrderMagicNumber() == g_93_in_1F0 && OrderTicket() == tmp_lo_18 )
                 {
                   OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_ASK),3,Red); 
                 }
                 if ( OrderMagicNumber() != g_169_in_4AC )   continue;
                 tmp_st_11 = OrderComment();
                 if ( tmp_st_11 != IntegerToString(tmp_lo_18,0,32) )   continue;
                 
                 if ( OrderType() == 0 )
                 {
                   OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_BID),g_38_do_C0,Red); 
                 }
                 if ( OrderType() != 1 )   continue;
                 OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_ASK),g_38_do_C0,Red); 
                 
               }
               g_195_bo_531 = false ;
               local_20_bo = true ;
             }
           }
           else
           {
             local_23_do = local_12_do * g_165_do_490 ;
             if ( g_164_in_488 == 2 )
             {
               local_23_do = (local_21_do + 1.0) * local_12_do + local_12_do ;
             }
             if ( g_164_in_488 == 3 )
             {
               local_23_do = local_12_do * (MathPow(g_165_do_490,local_21_do + 1.0)) ;
             }
             if ( g_193_in_52C == 0 )
             {
               local_24_do = local_17_do ;
               if ( MarketInfo(g_336_st_3130,MODE_BID)<local_17_do )
               {
                 if ( local_21_do>=g_166_in_498 )
                 {
                   for (tmp_in_20 = OrdersTotal() ; tmp_in_20 >= 0 ; tmp_in_20=tmp_in_20 - 1)
                   {
                     if ( OrderSelect(tmp_in_20,0,0) != true )   continue;
                     
                     if ( OrderMagicNumber() == g_93_in_1F0 && OrderTicket() == local_9_lo )
                     {
                       OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_ASK),3,Red); 
                     }
                     if ( OrderMagicNumber() != g_169_in_4AC )   continue;
                     tmp_st_11 = OrderComment();
                     if ( tmp_st_11 != IntegerToString(local_9_lo,0,32) )   continue;
                     
                     if ( OrderType() == 0 )
                     {
                       OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_BID),g_38_do_C0,Red); 
                     }
                     if ( OrderType() != 1 )   continue;
                     OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_ASK),g_38_do_C0,Red); 
                     
                   }
                 }
                 else
                 {
                   OrderSend(g_336_st_3130,1,local_23_do,MarketInfo(g_336_st_3130,MODE_BID),g_38_do_C0,0.0,0.0,IntegerToString(local_9_lo,0,32),g_169_in_4AC,0,Green); 
                   g_193_in_52C = 1 ;
                   local_22_bo = true ;
                 }
               }
             }
             else
             {
               local_25_do = g_160_do_468 * g_229_do_1E00 + local_17_do - local_21_do * g_161_do_470 * g_229_do_1E00 ;
               if ( local_25_do<g_162_do_478 * g_229_do_1E00 + local_17_do )
               {
                 local_25_do = g_162_do_478 * g_229_do_1E00 + local_17_do ;
               }
               if ( MarketInfo(g_336_st_3130,MODE_ASK)>local_25_do )
               {
                 if ( local_21_do>=g_166_in_498 )
                 {
                   for (tmp_in_21 = OrdersTotal() ; tmp_in_21 >= 0 ; tmp_in_21=tmp_in_21 - 1)
                   {
                     if ( OrderSelect(tmp_in_21,0,0) != true )   continue;
                     
                     if ( OrderMagicNumber() == g_93_in_1F0 && OrderTicket() == local_9_lo )
                     {
                       OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_ASK),3,Red); 
                     }
                     if ( OrderMagicNumber() != g_169_in_4AC )   continue;
                     tmp_st_22 = OrderComment();
                     if ( tmp_st_22 != IntegerToString(local_9_lo,0,32) )   continue;
                     
                     if ( OrderType() == 0 )
                     {
                       OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_BID),g_38_do_C0,Red); 
                     }
                     if ( OrderType() != 1 )   continue;
                     OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_ASK),g_38_do_C0,Red); 
                     
                   }
                 }
                 else
                 {
                   OrderSend(g_336_st_3130,0,local_23_do,MarketInfo(g_336_st_3130,MODE_ASK),g_38_do_C0,0.0,0.0,IntegerToString(local_9_lo,0,32),g_169_in_4AC,0,Green); 
                   g_193_in_52C = 0 ;
                   local_22_bo = true ;
                 }
               }
             }
           }
         }
         if ( ( local_21_do>0.0 || local_22_bo ) )
         {
           local_20_bo = true ;
         }
       }
       if ( !(local_20_bo) )
       {
         if ( ( g_63_in_140 == 1 || (g_63_in_140 != 2 && g_63_in_140 != 3) ) )
         {
           tmp_lo_23 = local_9_lo;
           tmp_do_24 = g_100_do_230;
           tmp_do_25 = local_10_do;
           tmp_in_26 = 2;
           tmp_do_27 = 0.0;
           tmp_bo_28 = false;
           for (tmp_in_29 = 0 ; tmp_in_29 < g_199_in_16B0 ; tmp_in_29=tmp_in_29 + 1)
           {
             if ( g_196_do_568_si20si2[tmp_in_29][0]==tmp_lo_23 )
             {
               tmp_do_27 = g_196_do_568_si20si2[tmp_in_29][1];
               tmp_bo_28 = true;
               break;
             }
           }
           if ( !(tmp_bo_28) )
           {
             if ( tmp_in_26 == 1 )
             {
               tmp_do_27 = NormalizeDouble(tmp_do_25 - tmp_do_24 * g_229_do_1E00,g_190_in_518);
             }
             if ( tmp_in_26 == 2 )
             {
               tmp_do_27 = NormalizeDouble(tmp_do_24 * g_229_do_1E00 + tmp_do_25,g_190_in_518);
             }
             for (tmp_in_30 = 0 ; tmp_in_30 < g_199_in_16B0 ; tmp_in_30=tmp_in_30 + 1)
             {
               if ( g_196_do_568_si20si2[tmp_in_30][0]==0.0 )
               {
                 g_196_do_568_si20si2[tmp_in_30][0] = (double)tmp_lo_23;
                 g_196_do_568_si20si2[tmp_in_30][1] = tmp_do_27;
                 break;
               }
             }
           }
           g_191_do_520 = tmp_do_27 ;
           local_4_do = g_191_do_520 ;
           if ( MarketInfo(g_336_st_3130,MODE_ASK)>local_4_do )
           {
             Print("Closing with virtual SL"); 
             RefreshRates(); 
             OrderClose(local_9_lo,local_12_do,MarketInfo(g_336_st_3130,MODE_ASK),(int)g_1_do_0,0xFFFFFFFF); 
             return(true); 
           }
           if ( g_125_do_2F8>0.0 && TimeCurrent() >= local_13_da + g_304_in_287C && MarketInfo(g_336_st_3130,MODE_ASK)<local_7_do - g_337_do_3140 - g_126_do_300 * g_229_do_1E00 && MarketInfo(g_336_st_3130,MODE_ASK)>local_8_do + g_309_do_2898 && NormalizeDouble(MarketInfo(g_336_st_3130,MODE_ASK) + g_126_do_300 * g_229_do_1E00,g_190_in_518)<local_7_do )
           {
             local_7_do = NormalizeDouble(MarketInfo(g_336_st_3130,MODE_ASK) + g_126_do_300 * g_229_do_1E00,g_190_in_518) ;
             if ( local_7_do>MarketInfo(g_336_st_3130,MODE_ASK) + g_221_do_1A80 )
             {
               g_230_in_1E08 = OrderModify(local_9_lo,local_10_do,local_7_do,local_8_do,0,0xFFFFFFFF) ;
               if ( g_230_in_1E08 <= 0 )
               {
                 Print("TrailStop error: \'" + lizong_21(GetLastError()) + "\' when setting trailing Exit_TrailSL_after_X_Minutes_size loss.  Trying again!"); 
               }
               local_2_bo = true ;
             }
           }
           if ( g_103_do_250>0.0 && MarketInfo(g_336_st_3130,MODE_ASK)<local_7_do - g_337_do_3140 - (g_103_do_250 + g_106_do_268) * g_229_do_1E00 && MarketInfo(g_336_st_3130,MODE_ASK)<local_5_do - g_104_do_258 * g_229_do_1E00 && MarketInfo(g_336_st_3130,MODE_ASK)>local_8_do + g_309_do_2898 && local_7_do>local_10_do - g_105_do_260 * g_229_do_1E00 && NormalizeDouble(g_103_do_250 * g_229_do_1E00 + MarketInfo(g_336_st_3130,MODE_ASK),g_190_in_518)<local_7_do )
           {
             local_7_do = NormalizeDouble(MarketInfo(g_336_st_3130,MODE_ASK) + g_103_do_250 * g_229_do_1E00,g_190_in_518) ;
             if ( local_7_do>MarketInfo(g_336_st_3130,MODE_ASK) + g_221_do_1A80 )
             {
               g_230_in_1E08 = OrderModify(local_9_lo,local_10_do,local_7_do,local_8_do,0,0xFFFFFFFF) ;
               if ( g_230_in_1E08 <= 0 )
               {
                 Print("TrailStop error: \'" + lizong_21(GetLastError()) + "\' when setting trailing Exit_stop loss.  Trying again!"); 
               }
               else
               {
                 local_26_do = NormalizeDouble(g_107_do_270 / 100.0 * g_223_do_1AC4_si99[g_328_in_3100],2) ;
                 if ( local_26_do<local_12_do && local_26_do>=MarketInfo(g_336_st_3130,MODE_LOTSTEP) )
                 {
                   OrderClose(local_9_lo,local_26_do,MarketInfo(g_336_st_3130,MODE_ASK),g_38_do_C0,Red); 
                   return(true); 
                 }
               }
               local_2_bo = true ;
             }
           }
           if ( g_108_do_278>0.0 && MarketInfo(g_336_st_3130,MODE_BID)>NormalizeDouble(g_108_do_278 * g_229_do_1E00 + (local_8_do + g_337_do_3140),g_190_in_518) && Bid>NormalizeDouble(g_109_do_280 * g_229_do_1E00 + local_5_do,g_190_in_518) && MarketInfo(g_336_st_3130,MODE_BID)>local_8_do + g_309_do_2898 )
           {
             local_8_do = NormalizeDouble(MarketInfo(g_336_st_3130,MODE_BID) - g_108_do_278 * g_229_do_1E00,g_190_in_518) ;
             if ( local_8_do<MarketInfo(g_336_st_3130,MODE_BID) - g_221_do_1A80 )
             {
               g_230_in_1E08 = OrderModify(local_9_lo,local_10_do,local_7_do,local_8_do,0,0xFFFFFFFF) ;
               if ( g_230_in_1E08 <= 0 )
               {
                 Print("TrailStop error: \'" + lizong_21(GetLastError()) + "\' when setting trailing Exit_TP.  Trying again!"); 
               }
               else
               {
                 local_27_do = NormalizeDouble(g_107_do_270 / 100.0 * g_223_do_1AC4_si99[g_328_in_3100],2) ;
                 if ( local_27_do<local_12_do && local_27_do>=SymbolInfoDouble(g_336_st_3130,34) )
                 {
                   OrderClose(local_9_lo,local_27_do,MarketInfo(g_336_st_3130,MODE_ASK),g_38_do_C0,Red); 
                   return(true); 
                 }
               }
               local_2_bo = true ;
             }
           }
           if ( local_19_bo && g_39_in_C8 == 1 && g_41_do_D8>0.0 && MarketInfo(g_336_st_3130,MODE_ASK)<local_7_do - g_337_do_3140 - g_41_do_D8 * g_229_do_1E00 && MarketInfo(g_336_st_3130,MODE_ASK)<local_17_do - g_40_do_D0 * g_229_do_1E00 && MarketInfo(g_336_st_3130,MODE_ASK)>local_8_do + g_309_do_2898 && local_7_do>local_10_do - g_42_do_E0 * g_229_do_1E00 && NormalizeDouble(MarketInfo(g_336_st_3130,MODE_ASK) + g_41_do_D8 * g_229_do_1E00,g_190_in_518)<local_7_do )
           {
             local_7_do = NormalizeDouble(MarketInfo(g_336_st_3130,MODE_ASK) + g_41_do_D8 * g_229_do_1E00,g_190_in_518) ;
             if ( local_7_do>MarketInfo(g_336_st_3130,MODE_ASK) + g_221_do_1A80 )
             {
               g_230_in_1E08 = OrderModify(local_9_lo,local_10_do,local_7_do,local_8_do,0,0xFFFFFFFF) ;
               if ( g_230_in_1E08 <= 0 )
               {
                 Print("TrailStop error: \'" + lizong_21(GetLastError()) + "\' when setting Slip TL.  Trying again!"); 
               }
               else
               {
                 Print("Slippage controle active"); 
               }
               local_2_bo = true ;
             }
           }
           if ( g_119_in_2D0 >  0 && g_120_in_2D4 >= 0 && UseHL_TrailingSL && g_241_do_1E78_si99[g_328_in_3100]<local_7_do - g_221_do_1A80 - g_337_do_3140 && g_241_do_1E78_si99[g_328_in_3100]>g_121_in_2D8 * g_229_do_1E00 + MarketInfo(g_336_st_3130,MODE_ASK) && ( g_241_do_1E78_si99[g_328_in_3100]>local_10_do || !(g_116_bo_2C4) ) && g_241_do_1E78_si99[g_328_in_3100]>g_122_in_2DC * g_229_do_1E00 + MarketInfo(g_336_st_3130,MODE_ASK) + g_221_do_1A80 + g_337_do_3140 && MarketInfo(g_336_st_3130,MODE_ASK)>local_8_do + g_309_do_2898 && NormalizeDouble(g_241_do_1E78_si99[g_328_in_3100],g_190_in_518)<local_7_do )
           {
             local_7_do = NormalizeDouble(g_241_do_1E78_si99[g_328_in_3100],g_190_in_518) ;
             if ( local_7_do>MarketInfo(g_336_st_3130,MODE_ASK) + g_221_do_1A80 )
             {
               g_230_in_1E08 = OrderModify(local_9_lo,local_10_do,local_7_do,local_8_do,0,0xFFFFFFFF) ;
               if ( g_230_in_1E08 <= 0 )
               {
                 Print("error: \'" + lizong_21(GetLastError()) + "\' when modifying stoploss"); 
               }
               local_2_bo = true ;
             }
           }
           if ( g_113_do_2A8>0.0 && MarketInfo(g_336_st_3130,MODE_ASK)<local_10_do - g_113_do_2A8 * g_229_do_1E00 && local_10_do - g_114_do_2B0 * g_229_do_1E00<local_7_do - g_337_do_3140 && MarketInfo(g_336_st_3130,MODE_ASK)<local_10_do - g_114_do_2B0 * g_229_do_1E00 - g_221_do_1A80 && MarketInfo(g_336_st_3130,MODE_ASK)>local_8_do + g_309_do_2898 && NormalizeDouble(local_10_do - g_114_do_2B0 * g_229_do_1E00,g_190_in_518)<local_7_do )
           {
             local_7_do = NormalizeDouble(local_10_do - g_114_do_2B0 * g_229_do_1E00,g_190_in_518) ;
             if ( local_7_do>MarketInfo(g_336_st_3130,MODE_ASK) + g_221_do_1A80 )
             {
               g_230_in_1E08 = OrderModify(local_9_lo,local_10_do,local_7_do,local_8_do,0,0xFFFFFFFF) ;
               if ( g_230_in_1E08 <= 0 )
               {
                 Print("error when setting breakeven: \'" + lizong_21(GetLastError()) + "\' ..\'Exit_BE_start\' to close to \'Exit_BE_extra_pips\' ..trying again!"); 
               }
               local_2_bo = true ;
             }
           }
           if ( !(local_2_bo) && ( g_128_in_314 == 1 || (g_128_in_314 == 2 && local_7_do - g_131_do_328 * g_229_do_1E00>=local_5_do - g_1_do_0 - g_132_do_330 * g_229_do_1E00) ) )
           {
             g_250_in_2518 ++;
             if ( MarketInfo(g_336_st_3130,MODE_ASK)<local_7_do - g_131_do_328 * g_229_do_1E00 - g_221_do_1A80 && MarketInfo(g_336_st_3130,MODE_ASK)>local_8_do + g_309_do_2898 && ( g_129_do_318==0.0 || MarketInfo(g_336_st_3130,MODE_ASK)<local_5_do - g_247_do_2500 * g_229_do_1E00 ) && g_250_in_2518 >= g_130_in_320 && NormalizeDouble(local_7_do - g_131_do_328 * g_229_do_1E00,g_190_in_518)<local_7_do )
             {
               g_250_in_2518 = 0 ;
               local_7_do = NormalizeDouble(local_7_do - g_131_do_328 * g_229_do_1E00,g_190_in_518) ;
               OrderModify(local_9_lo,local_10_do,local_7_do,local_8_do,0,0xFFFFFFFF); 
               local_2_bo = true ;
             }
           }
           g_191_do_520 = local_7_do ;
           if ( MarketInfo(g_336_st_3130,MODE_ASK)>local_7_do )
           {
             Print("Closing with virtual SL"); 
             RefreshRates(); 
             OrderClose(local_9_lo,local_12_do,MarketInfo(g_336_st_3130,MODE_ASK),(int)g_1_do_0,0xFFFFFFFF); 
             return(true); 
           }
           if ( NormalizeDouble(local_4_do,g_190_in_518)!=NormalizeDouble(g_191_do_520,g_190_in_518) )
           {
             tmp_do_31 = NormalizeDouble(g_191_do_520,g_190_in_518);
             tmp_lo_32 = local_9_lo;
             for (tmp_in_33 = 0 ; tmp_in_33 < g_199_in_16B0 ; tmp_in_33=tmp_in_33 + 1)
             {
               if ( g_196_do_568_si20si2[tmp_in_33][0]==tmp_lo_32 )
               {
                 g_196_do_568_si20si2[tmp_in_33][1] = tmp_do_31;
                 break;
               }
             }
           }
           if ( local_2_bo && g_135_bo_348 )
           {
             return(true); 
           }
         }
         if ( ( g_63_in_140 == 2 || g_63_in_140 == 3 ) )
         {
           tmp_lo_34 = local_9_lo;
           tmp_do_35 = g_100_do_230;
           tmp_do_36 = local_10_do;
           tmp_in_37 = 2;
           tmp_do_38 = 0.0;
           tmp_bo_39 = false;
           for (tmp_in_40 = 0 ; tmp_in_40 < g_199_in_16B0 ; tmp_in_40=tmp_in_40 + 1)
           {
             if ( g_196_do_568_si20si2[tmp_in_40][0]==tmp_lo_34 )
             {
               tmp_do_38 = g_196_do_568_si20si2[tmp_in_40][1];
               tmp_bo_39 = true;
               break;
             }
           }
           if ( !(tmp_bo_39) )
           {
             if ( tmp_in_37 == 1 )
             {
               tmp_do_38 = NormalizeDouble(tmp_do_36 - tmp_do_35 * g_229_do_1E00,g_190_in_518);
             }
             if ( tmp_in_37 == 2 )
             {
               tmp_do_38 = NormalizeDouble(tmp_do_35 * g_229_do_1E00 + tmp_do_36,g_190_in_518);
             }
             for (tmp_in_41 = 0 ; tmp_in_41 < g_199_in_16B0 ; tmp_in_41=tmp_in_41 + 1)
             {
               if ( g_196_do_568_si20si2[tmp_in_41][0]==0.0 )
               {
                 g_196_do_568_si20si2[tmp_in_41][0] = (double)tmp_lo_34;
                 g_196_do_568_si20si2[tmp_in_41][1] = tmp_do_38;
                 break;
               }
             }
           }
           g_191_do_520 = tmp_do_38 ;
           local_4_do = g_191_do_520 ;
           if ( MarketInfo(g_336_st_3130,MODE_ASK)>=local_4_do )
           {
             RefreshRates(); 
             OrderClose(local_9_lo,local_12_do,MarketInfo(g_336_st_3130,MODE_ASK),(int)g_1_do_0,0xFFFFFFFF); 
             return(true); 
           }
           local_28_in = (int)(TimeCurrent() - g_319_da_28E0) ;
           if ( local_28_in >= g_65_in_150 )
           {
             if ( NormalizeDouble(g_191_do_520,g_190_in_518)<local_7_do - g_337_do_3140 )
             {
               OrderModify(local_9_lo,local_10_do,NormalizeDouble(g_191_do_520,g_190_in_518),local_8_do,0,0xFFFFFFFF); 
             }
             g_319_da_28E0 = TimeCurrent() ;
           }
           if ( g_125_do_2F8>0.0 && TimeCurrent() >= local_13_da + g_304_in_287C && MarketInfo(g_336_st_3130,MODE_ASK)<g_191_do_520 - g_337_do_3140 - g_126_do_300 * g_229_do_1E00 && MarketInfo(g_336_st_3130,MODE_ASK)>local_8_do + g_309_do_2898 )
           {
             g_191_do_520 = MarketInfo(g_336_st_3130,MODE_ASK) + g_126_do_300 * g_229_do_1E00 ;
             local_2_bo = true ;
           }
           if ( g_103_do_250>0.0 && MarketInfo(g_336_st_3130,MODE_ASK)<g_191_do_520 - g_337_do_3140 - (g_103_do_250 + g_106_do_268) * g_229_do_1E00 && MarketInfo(g_336_st_3130,MODE_ASK)<local_5_do - g_104_do_258 * g_229_do_1E00 && g_191_do_520>local_10_do - g_105_do_260 * g_229_do_1E00 )
           {
             g_191_do_520 = g_103_do_250 * g_229_do_1E00 + MarketInfo(g_336_st_3130,MODE_ASK) ;
             local_29_do = NormalizeDouble(g_107_do_270 / 100.0 * g_223_do_1AC4_si99[g_328_in_3100],2) ;
             if ( local_29_do<local_12_do && local_29_do>=MarketInfo(g_336_st_3130,MODE_LOTSTEP) )
             {
               OrderClose(local_9_lo,local_29_do,MarketInfo(g_336_st_3130,MODE_BID),g_38_do_C0,Red); 
               return(true); 
             }
             local_2_bo = true ;
           }
           if ( local_19_bo && g_39_in_C8 == 1 && g_41_do_D8>0.0 && MarketInfo(g_336_st_3130,MODE_ASK)<g_191_do_520 - g_337_do_3140 - g_41_do_D8 * g_229_do_1E00 && MarketInfo(g_336_st_3130,MODE_ASK)<local_17_do - g_40_do_D0 * g_229_do_1E00 && MarketInfo(g_336_st_3130,MODE_ASK)>local_8_do + g_309_do_2898 && g_191_do_520>local_10_do - g_42_do_E0 * g_229_do_1E00 )
           {
             Print("Slippage controle active"); 
             local_2_bo = true ;
             g_191_do_520 = MarketInfo(g_336_st_3130,MODE_ASK) + g_41_do_D8 * g_229_do_1E00 ;
           }
           if ( g_119_in_2D0 >  0 && g_120_in_2D4 >= 0 && g_241_do_1E78_si99[g_328_in_3100]<g_191_do_520 - g_221_do_1A80 - g_337_do_3140 && ( g_241_do_1E78_si99[g_328_in_3100]>local_10_do || !(g_116_bo_2C4) ) && g_241_do_1E78_si99[g_328_in_3100]>g_122_in_2DC * g_229_do_1E00 + MarketInfo(g_336_st_3130,MODE_ASK) + g_221_do_1A80 + g_337_do_3140 && MarketInfo(g_336_st_3130,MODE_ASK)>local_8_do + g_309_do_2898 )
           {
             g_191_do_520 = g_241_do_1E78_si99[g_328_in_3100] ;
             local_2_bo = true ;
           }
           if ( g_113_do_2A8>0.0 && g_63_in_140 == 3 && MarketInfo(g_336_st_3130,MODE_ASK)<local_10_do - g_113_do_2A8 * g_229_do_1E00 && local_10_do - g_114_do_2B0 * g_229_do_1E00<local_7_do - g_337_do_3140 && MarketInfo(g_336_st_3130,MODE_ASK)<local_10_do - g_114_do_2B0 * g_229_do_1E00 - g_221_do_1A80 && MarketInfo(g_336_st_3130,MODE_ASK)>local_8_do + g_309_do_2898 && NormalizeDouble(local_10_do - g_114_do_2B0 * g_229_do_1E00,g_190_in_518)<g_191_do_520 )
           {
             g_191_do_520 = NormalizeDouble(local_10_do - g_114_do_2B0 * g_229_do_1E00,g_190_in_518) ;
             g_230_in_1E08 = OrderModify(local_9_lo,local_10_do,g_191_do_520,local_8_do,0,0xFFFFFFFF) ;
             if ( g_230_in_1E08 <= 0 )
             {
               Print("error when setting breakeven: \'" + lizong_21(GetLastError()) + "\' ..\'Exit_BE_start\' to close to \'Exit_BE_extra_pips\' ..trying again!"); 
             }
             local_2_bo = true ;
           }
           if ( g_113_do_2A8>0.0 && g_63_in_140 == 2 && MarketInfo(g_336_st_3130,MODE_ASK)<local_10_do - g_113_do_2A8 * g_229_do_1E00 && local_10_do - g_114_do_2B0 * g_229_do_1E00<g_191_do_520 - g_337_do_3140 && MarketInfo(g_336_st_3130,MODE_ASK)<local_10_do - g_114_do_2B0 * g_229_do_1E00 - g_221_do_1A80 && MarketInfo(g_336_st_3130,MODE_ASK)>local_8_do + g_309_do_2898 )
           {
             g_191_do_520 = local_10_do - g_114_do_2B0 * g_229_do_1E00 ;
             local_2_bo = true ;
           }
           if ( !(local_2_bo) && ( g_128_in_314 == 1 || (g_128_in_314 == 2 && g_191_do_520 - g_131_do_328 * g_229_do_1E00>=local_5_do - g_1_do_0 - g_132_do_330 * g_229_do_1E00) ) )
           {
             g_250_in_2518 ++;
             if ( MarketInfo(g_336_st_3130,MODE_ASK)<g_191_do_520 - g_131_do_328 * g_229_do_1E00 - g_221_do_1A80 && MarketInfo(g_336_st_3130,MODE_ASK)>local_8_do + g_309_do_2898 && ( g_129_do_318==0.0 || MarketInfo(g_336_st_3130,MODE_ASK)<local_5_do - g_247_do_2500 * g_229_do_1E00 ) && g_250_in_2518 >= g_130_in_320 )
             {
               g_250_in_2518 = 0 ;
               g_191_do_520 = g_191_do_520 - g_131_do_328 * g_229_do_1E00 ;
               local_2_bo = true ;
             }
           }
           if ( MarketInfo(g_336_st_3130,MODE_ASK)>=g_191_do_520 )
           {
             RefreshRates(); 
             OrderClose(local_9_lo,local_12_do,MarketInfo(g_336_st_3130,MODE_ASK),(int)g_1_do_0,0xFFFFFFFF); 
             return(true); 
           }
           if ( NormalizeDouble(local_4_do,g_190_in_518)!=NormalizeDouble(g_191_do_520,g_190_in_518) )
           {
             tmp_do_42 = NormalizeDouble(g_191_do_520,g_190_in_518);
             tmp_lo_43 = local_9_lo;
             for (tmp_in_44 = 0 ; tmp_in_44 < g_199_in_16B0 ; tmp_in_44=tmp_in_44 + 1)
             {
               if ( g_196_do_568_si20si2[tmp_in_44][0]==tmp_lo_43 )
               {
                 g_196_do_568_si20si2[tmp_in_44][1] = tmp_do_42;
                 break;
               }
             }
           }
         }
       }
     }
     if ( local_2_bo )
     {
       local_3_bo = true ;
     }
   }
   if ( local_2_bo )
   {
     local_3_bo = true ;
   }
 }
 return(local_3_bo); 
 }
//lizong_19 <<==--------   --------
 bool lizong_20()
 {
  bool      local_2_bo;
  datetime  local_3_da;
  int       local_4_in;
//----- -----
 bool       tmp_bo_1;
 bool       tmp_bo_2;
 bool       tmp_bo_3;
 bool       tmp_bo_4;
 bool       tmp_bo_5;
 bool       tmp_bo_6;

 if ( !(g_171_bo_4BC) )
 {
   return(true); 
 }
 local_2_bo = false ;
 local_3_da = 0 ;
 if ( g_172_in_4C0 == 2 )
 {
   local_3_da = TimeCurrent() ;
 }
 if ( g_172_in_4C0 == 0 )
 {
   TimeGMT(); 
 }
 if ( g_172_in_4C0 == 1 )
 {
   TimeLocal(); 
 }
 local_4_in = TimeHour(local_3_da) ;
 if ( TimeDayOfWeek(local_3_da) == 0 )
 {
   if ( g_174_in_4C8 <  g_175_in_4CC && ( local_4_in < g_174_in_4C8 || local_4_in >= g_175_in_4CC ) )
   {
     tmp_bo_1 = false;
   }
   else
   {
     if ( g_174_in_4C8 >  g_175_in_4CC && local_4_in <  g_174_in_4C8 && local_4_in >= g_175_in_4CC )
     {
       tmp_bo_1 = false;
     }
     else
     {
       if ( g_174_in_4C8 == g_175_in_4CC )
       {
         tmp_bo_1 = false;
       }
       else
       {
         tmp_bo_1 = true;
       }
     }
   }
   if ( tmp_bo_1 )
   {
     local_2_bo = true ;
   }
 }
 if ( TimeDayOfWeek(local_3_da) == 1 )
 {
   if ( g_176_in_4D0 <  g_177_in_4D4 && ( local_4_in < g_176_in_4D0 || local_4_in >= g_177_in_4D4 ) )
   {
     tmp_bo_2 = false;
   }
   else
   {
     if ( g_176_in_4D0 >  g_177_in_4D4 && local_4_in <  g_176_in_4D0 && local_4_in >= g_177_in_4D4 )
     {
       tmp_bo_2 = false;
     }
     else
     {
       if ( g_176_in_4D0 == g_177_in_4D4 )
       {
         tmp_bo_2 = false;
       }
       else
       {
         tmp_bo_2 = true;
       }
     }
   }
   if ( tmp_bo_2 )
   {
     local_2_bo = true ;
   }
 }
 if ( TimeDayOfWeek(local_3_da) == 2 )
 {
   if ( g_178_in_4D8 <  g_179_in_4DC && ( local_4_in < g_178_in_4D8 || local_4_in >= g_179_in_4DC ) )
   {
     tmp_bo_3 = false;
   }
   else
   {
     if ( g_178_in_4D8 >  g_179_in_4DC && local_4_in <  g_178_in_4D8 && local_4_in >= g_179_in_4DC )
     {
       tmp_bo_3 = false;
     }
     else
     {
       if ( g_178_in_4D8 == g_179_in_4DC )
       {
         tmp_bo_3 = false;
       }
       else
       {
         tmp_bo_3 = true;
       }
     }
   }
   if ( tmp_bo_3 )
   {
     local_2_bo = true ;
   }
 }
 if ( TimeDayOfWeek(local_3_da) == 3 )
 {
   if ( g_180_in_4E0 <  g_181_in_4E4 && ( local_4_in < g_180_in_4E0 || local_4_in >= g_181_in_4E4 ) )
   {
     tmp_bo_4 = false;
   }
   else
   {
     if ( g_180_in_4E0 >  g_181_in_4E4 && local_4_in <  g_180_in_4E0 && local_4_in >= g_181_in_4E4 )
     {
       tmp_bo_4 = false;
     }
     else
     {
       if ( g_180_in_4E0 == g_181_in_4E4 )
       {
         tmp_bo_4 = false;
       }
       else
       {
         tmp_bo_4 = true;
       }
     }
   }
   if ( tmp_bo_4 )
   {
     local_2_bo = true ;
   }
 }
 if ( TimeDayOfWeek(local_3_da) == 4 )
 {
   if ( g_182_in_4E8 <  g_183_in_4EC && ( local_4_in < g_182_in_4E8 || local_4_in >= g_183_in_4EC ) )
   {
     tmp_bo_5 = false;
   }
   else
   {
     if ( g_182_in_4E8 >  g_183_in_4EC && local_4_in <  g_182_in_4E8 && local_4_in >= g_183_in_4EC )
     {
       tmp_bo_5 = false;
     }
     else
     {
       if ( g_182_in_4E8 == g_183_in_4EC )
       {
         tmp_bo_5 = false;
       }
       else
       {
         tmp_bo_5 = true;
       }
     }
   }
   if ( tmp_bo_5 )
   {
     local_2_bo = true ;
   }
 }
 if ( TimeDayOfWeek(local_3_da) == 5 )
 {
   if ( g_184_in_4F0 <  g_185_in_4F4 && ( local_4_in < g_184_in_4F0 || local_4_in >= g_185_in_4F4 ) )
   {
     tmp_bo_6 = false;
   }
   else
   {
     if ( g_184_in_4F0 >  g_185_in_4F4 && local_4_in <  g_184_in_4F0 && local_4_in >= g_185_in_4F4 )
     {
       tmp_bo_6 = false;
     }
     else
     {
       if ( g_184_in_4F0 == g_185_in_4F4 )
       {
         tmp_bo_6 = false;
       }
       else
       {
         tmp_bo_6 = true;
       }
     }
   }
   if ( tmp_bo_6 )
   {
     local_2_bo = true ;
   }
 }
 return(local_2_bo); 
 }
//lizong_20 <<==--------   --------
 string lizong_21( int arg_0_in)
 {
  string    local_1_st;
//----- -----

 g_274_in_25D8 ++;
 switch(arg_0_in)
 {
   case 0 : case 1 :
   local_1_st = "no error" ;
     break;
   case 2 :
   local_1_st = "common error" ;
     break;
   case 3 :
   local_1_st = "invalid trade parameters" ;
     break;
   case 4 :
   local_1_st = "trade server is busy" ;
     break;
   case 5 :
   local_1_st = "old version of the client terminal" ;
     break;
   case 6 :
   local_1_st = "no connection with trade server" ;
     break;
   case 7 :
   local_1_st = "not enough rights" ;
     break;
   case 8 :
   local_1_st = "too frequent requests" ;
     break;
   case 9 :
   local_1_st = "malfunctional trade operation (never returned error)" ;
     break;
   case 64 :
   local_1_st = "account disabled" ;
     break;
   case 65 :
   local_1_st = "invalid account" ;
     break;
   case 128 :
   local_1_st = "trade timeout" ;
     break;
   case 129 :
   local_1_st = "invalid price" ;
     break;
   case 130 :
   local_1_st = "invalid stops" ;
     break;
   case 131 :
   local_1_st = "invalid trade volume" ;
     break;
   case 132 :
   local_1_st = "market is closed" ;
     break;
   case 133 :
   local_1_st = "trade is disabled" ;
     break;
   case 134 :
   local_1_st = "not enough money" ;
     break;
   case 135 :
   local_1_st = "price changed" ;
     break;
   case 136 :
   local_1_st = "off quotes" ;
     break;
   case 137 :
   local_1_st = "broker is busy (never returned error)" ;
     break;
   case 138 :
   local_1_st = "requote" ;
     break;
   case 139 :
   local_1_st = "order is locked" ;
     break;
   case 140 :
   local_1_st = "long positions only allowed" ;
     break;
   case 141 :
   local_1_st = "too many requests" ;
     break;
   case 145 :
   local_1_st = "modification denied because order too close to market" ;
     break;
   case 146 :
   local_1_st = "trade context is busy" ;
     break;
   case 147 :
   local_1_st = "expirations are denied by broker" ;
     break;
   case 148 :
   local_1_st = "amount of open and pending orders has reached the Exit_limit" ;
     break;
   case 149 :
   local_1_st = "hedging is prohibited" ;
     break;
   case 150 :
   local_1_st = "prohibited by FIFO rules" ;
     break;
   case 4000 :
   local_1_st = "no error (never generated code)" ;
     break;
   case 4001 :
   local_1_st = "wrong function pointer" ;
     break;
   case 4002 :
   local_1_st = "array index is out of range" ;
     break;
   case 4003 :
   local_1_st = "no memory for function call stack" ;
     break;
   case 4004 :
   local_1_st = "recursive stack overflow" ;
     break;
   case 4005 :
   local_1_st = "not enough stack for parameter" ;
     break;
   case 4006 :
   local_1_st = "no memory for parameter string" ;
     break;
   case 4007 :
   local_1_st = "no memory for temp string" ;
     break;
   case 4008 :
   local_1_st = "not initialized string" ;
     break;
   case 4009 :
   local_1_st = "not initialized string in array" ;
     break;
   case 4010 :
   local_1_st = "no memory for array\' string" ;
     break;
   case 4011 :
   local_1_st = "too long string" ;
     break;
   case 4012 :
   local_1_st = "remainder from zero divide" ;
     break;
   case 4013 :
   local_1_st = "zero divide" ;
     break;
   case 4014 :
   local_1_st = "unknown command" ;
     break;
   case 4015 :
   local_1_st = "wrong jump (never generated error)" ;
     break;
   case 4016 :
   local_1_st = "not initialized array" ;
     break;
   case 4017 :
   local_1_st = "dll calls are not allowed" ;
     break;
   case 4018 :
   local_1_st = "cannot load library" ;
     break;
   case 4019 :
   local_1_st = "cannot call function" ;
     break;
   case 4020 :
   local_1_st = "expert function calls are not allowed" ;
     break;
   case 4021 :
   local_1_st = "not enough memory for temp string returned from function" ;
     break;
   case 4022 :
   local_1_st = "system is busy (never generated error)" ;
     break;
   case 4050 :
   local_1_st = "invalid function parameters count" ;
     break;
   case 4051 :
   local_1_st = "invalid function parameter value" ;
     break;
   case 4052 :
   local_1_st = "string function internal error" ;
     break;
   case 4053 :
   local_1_st = "some array error" ;
     break;
   case 4054 :
   local_1_st = "incorrect series array using" ;
     break;
   case 4055 :
   local_1_st = "custom indicator error" ;
     break;
   case 4056 :
   local_1_st = "arrays are incompatible" ;
     break;
   case 4057 :
   local_1_st = "global variables processing error" ;
     break;
   case 4058 :
   local_1_st = "global variable not found" ;
     break;
   case 4059 :
   local_1_st = "function is not allowed in testing mode" ;
     break;
   case 4060 :
   local_1_st = "function is not confirmed" ;
     break;
   case 4061 :
   local_1_st = "send mail error" ;
     break;
   case 4062 :
   local_1_st = "string parameter expected" ;
     break;
   case 4063 :
   local_1_st = "integer parameter expected" ;
     break;
   case 4064 :
   local_1_st = "double parameter expected" ;
     break;
   case 4065 :
   local_1_st = "array as parameter expected" ;
     break;
   case 4066 :
   local_1_st = "requested history data in update state" ;
     break;
   case 4099 :
   local_1_st = "end of file" ;
     break;
   case 4100 :
   local_1_st = "some file error" ;
     break;
   case 4101 :
   local_1_st = "wrong file name" ;
     break;
   case 4102 :
   local_1_st = "too many opened files" ;
     break;
   case 4103 :
   local_1_st = "cannot open file" ;
     break;
   case 4104 :
   local_1_st = "incompatible access to a file" ;
     break;
   case 4105 :
   local_1_st = "no order selected" ;
     break;
   case 4106 :
   local_1_st = "unknown symbol" ;
     break;
   case 4107 :
   local_1_st = "invalid price parameter for trade function" ;
     break;
   case 4108 :
   local_1_st = "invalid ticket" ;
     break;
   case 4109 :
   local_1_st = "trade is not allowed in the expert properties" ;
     break;
   case 4110 :
   local_1_st = "longs are not allowed in the expert properties" ;
     break;
   case 4111 :
   local_1_st = "shorts are not allowed in the expert properties" ;
     break;
   case 4200 :
   local_1_st = "object is already exist" ;
     break;
   case 4201 :
   local_1_st = "unknown object property" ;
     break;
   case 4202 :
   local_1_st = "object is not exist" ;
     break;
   case 4203 :
   local_1_st = "unknown object type" ;
     break;
   case 4204 :
   local_1_st = "no object name" ;
     break;
   case 4205 :
   local_1_st = "object coordinates error" ;
     break;
   case 4206 :
   local_1_st = "no specified subwindow" ;
     break;
   default :
   local_1_st = "unknown error" ;
 }
 return(local_1_st);
 }
//lizong_21 <<==--------   --------
 void lizong_22( bool arg_0_bo)
 {
  double    local_1_do;
  int       local_2_in;
  int       local_3_in;
  double    local_4_do;
  long      local_5_lo;
  double    local_6_do;
  double    local_7_do;
  datetime  local_8_da;
  string    local_9_st;
  int       local_10_in;
  double    local_11_do;
  long      local_12_lo;
  double    local_13_do;
  double    local_14_do;
  datetime  local_15_da;
  string    local_16_st;
  int       local_17_in;
//----- -----
 long       tmp_lo_1;
 long       tmp_lo_2;
 int        tmp_in_3;
 long       tmp_lo_4;
 long       tmp_lo_5;
 int        tmp_in_6;

 local_1_do = g_140_do_3F0 / 100.0 + 1.0 ;
 if ( ( !(AccountBalance()!=g_318_do_28D8) && !(arg_0_bo) ) )   return;
 
 if ( ( !(AccountBalance()>g_318_do_28D8 * local_1_do) && !(AccountBalance()<g_318_do_28D8 / local_1_do) && !(arg_0_bo) ) )   return;
 lizong_10(g_100_do_230,g_92_in_1EC); 
 local_2_in = OrdersTotal() ;
 for (local_3_in = local_2_in ; local_3_in >= 0 ; local_3_in --)
 {
   if ( OrderSelect(local_3_in,0,0) != true || OrderMagicNumber() != g_93_in_1F0 || OrderSymbol() != g_336_st_3130 )   continue;
   
   if ( OrderType() == 4 && OrderLots()!=g_223_do_1AC4_si99[g_328_in_3100] )
   {
     local_4_do = OrderStopLoss() ;
     local_5_lo = OrderTicket() ;
     local_6_do = OrderTakeProfit() ;
     local_7_do = OrderOpenPrice() ;
     local_8_da = OrderExpiration() ;
     local_9_st = OrderComment() ;
     OrderDelete(local_5_lo,Red); 
     local_10_in = (int)OrderSend(g_336_st_3130,4,g_223_do_1AC4_si99[g_328_in_3100],local_7_do,g_38_do_C0,local_4_do,local_6_do,local_9_st,g_93_in_1F0,local_8_da,Green) ;
     tmp_lo_1 = local_10_in;
     tmp_lo_2 = local_5_lo;
     for (tmp_in_3 = 0 ; tmp_in_3 < 100 ; tmp_in_3=tmp_in_3 + 1)
     {
       if ( !(g_198_do_1070_si100si2[tmp_in_3][0]==tmp_lo_2) )   continue;
       g_198_do_1070_si100si2[tmp_in_3][0] = (double)tmp_lo_1;
       break;
       
     }
     Print("Lotsize changed more than " + string(g_140_do_3F0) + "%... adjusting lotsize of pending orders"); 
     Sleep(1000); 
   }
   if ( OrderType() != 5 || !(OrderLots()!=g_223_do_1AC4_si99[g_328_in_3100]) )   continue;
   local_11_do = OrderStopLoss() ;
   local_12_lo = OrderTicket() ;
   local_13_do = OrderTakeProfit() ;
   local_14_do = OrderOpenPrice() ;
   local_15_da = OrderExpiration() ;
   local_16_st = OrderComment() ;
   OrderDelete(local_12_lo,Red); 
   local_17_in = (int)OrderSend(g_336_st_3130,5,g_223_do_1AC4_si99[g_328_in_3100],local_14_do,g_38_do_C0,local_11_do,local_13_do,local_16_st,g_93_in_1F0,local_15_da,Green) ;
   tmp_lo_4 = local_17_in;
   tmp_lo_5 = local_12_lo;
   for (tmp_in_6 = 0 ; tmp_in_6 < 100 ; tmp_in_6=tmp_in_6 + 1)
   {
     if ( !(g_198_do_1070_si100si2[tmp_in_6][0]==tmp_lo_5) )   continue;
     g_198_do_1070_si100si2[tmp_in_6][0] = (double)tmp_lo_4;
     break;
     
   }
   Print("Lotsize changed more than " + string(g_140_do_3F0) + "%... adjusting lotsize of pending orders"); 
   Sleep(1000); 
   
 }
 }

 void lizong_24()
 {
  int       local_1_in = 0;
  int       local_2_in = 0;
  int       local_3_in;
  int       local_4_in;
  int       local_5_in;
  double    local_6_do;
  int       local_7_in;
  int       local_8_in;
  int       local_9_in;
  int       local_10_in;
  int       local_11_in;
  int       local_12_in;
  int       local_13_in;
  uint      local_14_ui;
  bool      local_15_bo;
  int       local_16_in;
  string    local_17_st;
  int       local_18_in;
  int       local_19_in;
  int       local_20_in;
  string    local_21_st;
  int       local_22_in;
  int       local_23_in;
  int       local_24_in;
//----- -----

 if ( USE_CUSTOM_DASHBOARD )   return; 
 
 local_3_in = 20 ;
 local_4_in = 300 ;
 local_5_in = 7 ;
 local_6_do = InfoPanelSizeAdjust ;
 local_7_in = 6 ;
 local_8_in = 4 ;
 local_9_in = 350 ;
 local_10_in = 350 ;
 local_11_in = 0 ;
 local_12_in = 5 ;
 local_13_in = 20 ;
 local_14_ui = LightSteelBlue ;
 local_15_bo = false ;
 local_16_in = 0 ;
 if ( g_17_bo_8C )
 {
   local_16_in = (int)((g_378_in_5D80 + 3) * g_362_do_5CC8) ;
 }
 ObjectCreate(0,"infopanel_rectangle",OBJ_RECTANGLE_LABEL,0,0,0.0); 
 ObjectSetInteger(0,"infopanel_rectangle",OBJPROP_XDISTANCE,local_12_in); 
 ObjectSetInteger(0,"infopanel_rectangle",OBJPROP_YDISTANCE,local_13_in); 
 ObjectSetInteger(0,"infopanel_rectangle",OBJPROP_XSIZE,long(local_9_in * InfoPanelSizeAdjust)); 
 ObjectSetInteger(0,"infopanel_rectangle",OBJPROP_YSIZE,long(local_10_in * InfoPanelSizeAdjust + local_16_in)); 
 ObjectSetInteger(0,"infopanel_rectangle",OBJPROP_CORNER,0); 
 ObjectSetInteger(0,"infopanel_rectangle",OBJPROP_COLOR,0xFF0000); 
 ObjectSetInteger(0,"infopanel_rectangle",OBJPROP_BGCOLOR,local_14_ui); 
 ObjectSetInteger(0,"infopanel_rectangle",OBJPROP_BACK,0); 
 ObjectSetInteger(0,"infopanel_rectangle",OBJPROP_BORDER_COLOR,0xFF0000); 
 ObjectSetInteger(0,"infopanel_rectangle",OBJPROP_COLOR,0xFF0000); 
 ObjectSetInteger(0,"infopanel_rectangle",OBJPROP_BORDER_TYPE,0); 
 ObjectSetInteger(0,"infopanel_rectangle",OBJPROP_STYLE,0); 
 ObjectSetInteger(0,"infopanel_rectangle",OBJPROP_WIDTH,0x2); 
 ObjectSetInteger(0,"infopanel_rectangle",OBJPROP_SELECTABLE,0); 
 ObjectCreate(0,"line1",OBJ_LABEL,0,0,0.0); 
 ObjectSetInteger(0,"line1",OBJPROP_CORNER,local_11_in); 
 ObjectSetInteger(0,"line1",OBJPROP_YDISTANCE,local_13_in + local_8_in); 
 ObjectSetInteger(0,"line1",OBJPROP_XDISTANCE,local_12_in + local_7_in); 
 if ( !(g_17_bo_8C) )
 {
   ObjectSetString(0,"line1",OBJPROP_TEXT,"Lizard 1.85"); 
 }
 else
 {
   ObjectSetString(0,"line1",OBJPROP_TEXT,"Lizard 1.85"); 
 }
 ObjectSetInteger(0,"line1",OBJPROP_COLOR,g_329_ui_3104); 
 ObjectCreate(0,"linec",OBJ_LABEL,0,0,0.0); 
 ObjectSetInteger(0,"linec",OBJPROP_CORNER,local_11_in); 
 ObjectSetInteger(0,"linec",OBJPROP_YDISTANCE,long(local_13_in + InfoPanelSizeAdjust * 20.0 + local_8_in)); 
 ObjectSetInteger(0,"linec",OBJPROP_XDISTANCE,local_12_in + local_7_in); 
 ObjectSetString(0,"linec",OBJPROP_TEXT,"EA Developed by Wim Schrynemakers - 2024"); 
 ObjectSetInteger(0,"linec",OBJPROP_COLOR,g_329_ui_3104); 
 ObjectCreate(0,"line2",OBJ_LABEL,0,0,0.0); 
 ObjectSetInteger(0,"line2",OBJPROP_CORNER,local_11_in); 
 ObjectSetInteger(0,"line2",OBJPROP_YDISTANCE,long(local_13_in + InfoPanelSizeAdjust * 32.0 + local_8_in)); 
 ObjectSetInteger(0,"line2",OBJPROP_XDISTANCE,local_12_in + local_7_in); 
 ObjectSetString(0,"line2",OBJPROP_TEXT,"------------------------------------------------------"); 
 ObjectSetInteger(0,"line2",OBJPROP_COLOR,g_329_ui_3104); 
 ObjectCreate(0,"lines",OBJ_LABEL,0,0,0.0); 
 ObjectSetInteger(0,"lines",OBJPROP_CORNER,local_11_in); 
 ObjectSetInteger(0,"lines",OBJPROP_YDISTANCE,long(local_13_in + InfoPanelSizeAdjust * 44.0 + local_8_in)); 
 ObjectSetInteger(0,"lines",OBJPROP_XDISTANCE,local_12_in + local_7_in); 
 if ( g_19_in_9C == 1 )
 {
   local_17_st = "conservative" ;
 }
 else
 {
   if ( g_19_in_9C == 2 )
   {
     local_17_st = "moderate" ;
   }
   else
   {
     if ( g_19_in_9C == 3 )
     {
       local_17_st = "intense" ;
     }
     else
     {
       if ( g_19_in_9C == 4 )
       {
         local_17_st = "extreme" ;
       }
       else
       {
         if ( g_19_in_9C == 0 )
         {
           local_17_st = "extreme conservative" ;
         }
         else
         {
           local_17_st = "manual strategy selection" ;
         }
       }
     }
   }
 }
 ObjectSetString(0,"lines",OBJPROP_TEXT,"Trade Frequency: " + local_17_st); 
 ObjectSetInteger(0,"lines",OBJPROP_COLOR,g_329_ui_3104); 
 if ( Risk == 1234 )
 {
   ObjectCreate(0,"linet",OBJ_LABEL,0,0,0.0); 
   ObjectSetInteger(0,"linet",OBJPROP_CORNER,local_11_in); 
   ObjectSetInteger(0,"linet",OBJPROP_YDISTANCE,long(local_13_in + InfoPanelSizeAdjust * 60.0 + local_8_in)); 
   ObjectSetInteger(0,"linet",OBJPROP_XDISTANCE,local_12_in + local_7_in); 
   ObjectSetString(0,"linet",OBJPROP_TEXT,"Max allowed DD: " + string(MaxAllowedDD) + "%"); 
   ObjectSetInteger(0,"linet",OBJPROP_COLOR,g_329_ui_3104); 
 }
 else
 {
   if ( Risk == 3 )
   {
     ObjectCreate(0,"linet",OBJ_LABEL,0,0,0.0); 
     ObjectSetInteger(0,"linet",OBJPROP_CORNER,local_11_in); 
     ObjectSetInteger(0,"linet",OBJPROP_YDISTANCE,long(local_13_in + InfoPanelSizeAdjust * 60.0 + local_8_in)); 
     ObjectSetInteger(0,"linet",OBJPROP_XDISTANCE,local_12_in + local_7_in); 
     ObjectSetString(0,"linet",OBJPROP_TEXT,"Max risk per strategy: " + string(MaxRiskPerStrategy_) + "%"); 
     ObjectSetInteger(0,"linet",OBJPROP_COLOR,g_329_ui_3104); 
   }
   else
   {
     ObjectCreate(0,"linet",OBJ_LABEL,0,0,0.0); 
     ObjectSetInteger(0,"linet",OBJPROP_CORNER,local_11_in); 
     ObjectSetInteger(0,"linet",OBJPROP_YDISTANCE,long(local_13_in + InfoPanelSizeAdjust * 60.0 + local_8_in)); 
     ObjectSetInteger(0,"linet",OBJPROP_XDISTANCE,local_12_in + local_7_in); 
      ObjectSetString(0,"linet",OBJPROP_TEXT,"Manual lotsize: " + string(StartLotsRuntime) + "lots"); 
     ObjectSetInteger(0,"linet",OBJPROP_COLOR,g_329_ui_3104); 
   }
 }
 ObjectCreate(0,"lineopl" + IntegerToString(0,0,32),OBJ_LABEL,0,0,0.0); 
 ObjectSetInteger(0,"lineopl" + IntegerToString(0,0,32),OBJPROP_CORNER,local_11_in); 
 ObjectSetInteger(0,"lineopl" + IntegerToString(0,0,32),OBJPROP_YDISTANCE,(long)(local_13_in + InfoPanelSizeAdjust * 76.0 + local_8_in)); 
 ObjectSetInteger(0,"lineopl" + IntegerToString(0,0,32),OBJPROP_XDISTANCE,local_12_in + local_7_in); 
 ObjectSetString(0,"lineopl" + IntegerToString(0,0,32),OBJPROP_TEXT,"Open P/L: -"); 
 ObjectSetInteger(0,"lineopl" + IntegerToString(0,0,32),OBJPROP_COLOR,g_329_ui_3104); 
 ObjectCreate(0,"linea" + IntegerToString(0,0,32),OBJ_LABEL,0,0,0.0); 
 ObjectSetInteger(0,"linea" + IntegerToString(0,0,32),OBJPROP_CORNER,local_11_in); 
 ObjectSetInteger(0,"linea" + IntegerToString(0,0,32),OBJPROP_YDISTANCE,(long)(local_13_in + InfoPanelSizeAdjust * 92.0 + local_8_in)); 
 ObjectSetInteger(0,"linea" + IntegerToString(0,0,32),OBJPROP_XDISTANCE,local_12_in + local_7_in); 
 ObjectSetString(0,"linea" + IntegerToString(0,0,32),OBJPROP_TEXT,"Account Balance: -"); 
 ObjectSetInteger(0,"linea" + IntegerToString(0,0,32),OBJPROP_COLOR,g_329_ui_3104); 
 ObjectCreate(0,"linetp" + IntegerToString(0,0,32),OBJ_LABEL,0,0,0.0); 
 ObjectSetInteger(0,"linetp" + IntegerToString(0,0,32),OBJPROP_CORNER,local_11_in); 
 ObjectSetInteger(0,"linetp" + IntegerToString(0,0,32),OBJPROP_YDISTANCE,(long)(local_13_in + InfoPanelSizeAdjust * 108.0 + local_8_in)); 
 ObjectSetInteger(0,"linetp" + IntegerToString(0,0,32),OBJPROP_XDISTANCE,local_12_in + local_7_in); 
 ObjectSetString(0,"linetp" + IntegerToString(0,0,32),OBJPROP_TEXT,"Total P/L so far: -"); 
 ObjectSetInteger(0,"linetp" + IntegerToString(0,0,32),OBJPROP_COLOR,g_329_ui_3104); 
 local_18_in = 0 ;
 local_19_in = 0 ;
 local_20_in = 0 ;
 local_22_in = local_12_in + local_7_in ;
 local_23_in = (int)(local_13_in + InfoPanelSizeAdjust * 160.0 + local_8_in) ;
 local_21_st = "Strategy" ;
 lizong_25(local_22_in,local_23_in,0,"Strategy",0,0,1,0,1.0); 
 local_18_in = 1 ;
 local_19_in = 1 ;
 local_21_st = "Closed PL" ;
 if ( g_152_in_43C == 1 )
 {
   local_21_st = "Closed PL*" ;
 }
 lizong_25(local_22_in,local_23_in,local_18_in,local_21_st,local_20_in,local_19_in,1,0,1.0); 
 local_18_in ++;
 local_19_in ++;
 local_21_st = "PL per trade" ;
 if ( g_152_in_43C == 2 )
 {
   local_21_st = "PL per trade*" ;
 }
 lizong_25(local_22_in,local_23_in,local_18_in,local_21_st,local_20_in,local_19_in,1,0,1.0); 
 local_18_in ++;
 local_19_in ++;
 local_21_st = "Lotsize" ;
 lizong_25(local_22_in,local_23_in,local_18_in,"Lotsize",local_20_in,local_19_in,1,0,1.0); 
 local_18_in ++;
 local_19_in = 0 ;
 local_20_in ++;
 g_340_in_3310 = local_18_in ;
 for (local_24_in = 0 ; local_24_in < 9 ; local_24_in ++)
 {
   local_21_st="Strategy " + IntegerToString(local_24_in + 1,0,32);
   lizong_25(local_22_in,local_23_in,local_18_in,local_21_st,local_20_in,local_19_in,1,0,1.0); 
   local_18_in ++;
   local_19_in ++;
   local_21_st = DoubleToString(NormalizeDouble(g_400_do_67B4_si99[local_24_in],2),2) ;
   lizong_25(local_22_in,local_23_in,local_18_in,local_21_st,local_20_in,local_19_in,1,0,1.0); 
   local_18_in ++;
   local_19_in ++;
   local_21_st = DoubleToString(NormalizeDouble(g_345_do_3AAC_si99[local_24_in],2),2) ;
   lizong_25(local_22_in,local_23_in,local_18_in,local_21_st,local_20_in,local_19_in,1,0,1.0); 
   local_18_in ++;
   local_19_in ++;
   local_21_st = DoubleToString(NormalizeDouble(g_223_do_1AC4_si99[local_24_in],2),2) ;
   lizong_25(local_22_in,local_23_in,local_18_in,local_21_st,local_20_in,local_19_in,1,0,1.0); 
   local_18_in ++;
   local_19_in = 0 ;
   local_20_in ++;
 }
 }
//lizong_24 <<==--------   --------
 void lizong_25( int arg_0_in,int arg_1_in,int arg_2_in,string arg_3_st,int arg_4_in,int arg_5_in,int arg_6_in,uint arg_7_ui,double arg_8_do)
 {
 ObjectCreate(0,"info_ea" + IntegerToString(arg_2_in,0,32),OBJ_EDIT,0,0,0.0); 
 ObjectSetInteger(0,"info_ea" + IntegerToString(arg_2_in,0,32),OBJPROP_XDISTANCE,(long)(arg_0_in + arg_5_in * g_361_do_5CC0)); 
 ObjectSetInteger(0,"info_ea" + IntegerToString(arg_2_in,0,32),OBJPROP_YDISTANCE,(long)(arg_1_in + arg_4_in * g_362_do_5CC8)); 
 ObjectSetString(0,"info_ea" + IntegerToString(arg_2_in,0,32),OBJPROP_TEXT,arg_3_st); 
 ObjectSetInteger(0,"info_ea" + IntegerToString(arg_2_in,0,32),OBJPROP_BACK,0); 
 ObjectSetInteger(0,"info_ea" + IntegerToString(arg_2_in,0,32),OBJPROP_COLOR,arg_7_ui); 
 ObjectSetInteger(0,"info_ea" + IntegerToString(arg_2_in,0,32),OBJPROP_BGCOLOR,g_364_ui_5CD4); 
 ObjectSetInteger(0,"info_ea" + IntegerToString(arg_2_in,0,32),OBJPROP_BORDER_COLOR,0); 
 ObjectSetInteger(0,"info_ea" + IntegerToString(arg_2_in,0,32),OBJPROP_FONTSIZE,(long)(g_372_in_5CFC * arg_8_do)); 
 ObjectSetInteger(0,"info_ea" + IntegerToString(arg_2_in,0,32),OBJPROP_READONLY,0x1); 
 ObjectSetInteger(0,"info_ea" + IntegerToString(arg_2_in,0,32),OBJPROP_YSIZE,(long)g_362_do_5CC8); 
 ObjectSetInteger(0,"info_ea" + IntegerToString(arg_2_in,0,32),OBJPROP_XSIZE,(long)g_361_do_5CC0); 
 ObjectSetInteger(0,"info_ea" + IntegerToString(arg_2_in,0,32),OBJPROP_YSIZE,(long)g_362_do_5CC8); 
 if ( arg_6_in == 0 )
 {
   ObjectSetInteger(0,"info_ea" + IntegerToString(arg_2_in,0,32),OBJPROP_ALIGN,0x1); 
 }
 if ( arg_6_in == 1 )
 {
   ObjectSetInteger(0,"info_ea" + IntegerToString(arg_2_in,0,32),OBJPROP_ALIGN,0x2); 
 }
 if ( arg_6_in != 2 )   return;
 ObjectSetInteger(0,"info_ea" + IntegerToString(arg_2_in,0,32),OBJPROP_ALIGN,0); 
 }
//lizong_25 <<==--------   --------
 void lizong_26()
 {
  int       local_1_in;
  int       local_2_in;
  int       local_3_in;
  int       local_4_in;
//----- -----

 ObjectDelete(0,"line1"); 
 ObjectDelete(0,"linec"); 
 ObjectDelete(0,"line2"); 
 ObjectDelete(0,"lines"); 
 ObjectDelete(0,"linet"); 
 ObjectDelete(0,"lineTradeStart"); 
 for (local_1_in = 0 ; local_1_in <= 99 ; local_1_in ++)
 {
   ObjectDelete(0,"lineopl" + IntegerToString(local_1_in,0,32)); 
   ObjectDelete(0,"linea" + IntegerToString(local_1_in,0,32)); 
   ObjectDelete(0,"lineto" + IntegerToString(local_1_in,0,32)); 
   ObjectDelete(0,"linetp" + IntegerToString(local_1_in,0,32)); 
   ObjectDelete(0,"linetq" + IntegerToString(local_1_in,0,32)); 
   for (local_2_in = 0 ; local_2_in < 10 ; local_2_in ++)
   {
     ObjectDelete(0,"tabel_info" + IntegerToString(local_1_in * 100 + local_2_in,0,32)); 
   }
 }
 ObjectDelete(0,"infopanel_rectangle"); 
 for (local_3_in = 0 ; local_3_in < 10 ; local_3_in ++)
 {
   ObjectDelete(0,"tabel_heading" + IntegerToString(local_3_in,0,32)); 
   ObjectDelete(0,"tabel_totals" + IntegerToString(local_3_in,0,32)); 
 }
 for (local_4_in = 0 ; local_4_in < g_360_in_5CB8 ; local_4_in ++)
 {
   ObjectDelete(0,"horizontalrect" + IntegerToString(local_4_in,0,32)); 
   ObjectDelete(0,"info_ea" + IntegerToString(local_4_in,0,32)); 
 }
 }
//lizong_26 <<==--------   --------
 void lizong_27()
 {
  string    local_1_st;
//----- -----
 double     tmp_do_1;
 double     tmp_do_2;
 int        tmp_in_3;
 int        tmp_in_4;
 int        tmp_in_5;
 int        tmp_in_6;
 int        tmp_in_7;
 int        tmp_in_8;
 int        tmp_in_9;
 int        tmp_in_10;
 int        tmp_in_11;
 int        tmp_in_12;
 int        tmp_in_13;
 int        tmp_in_14;
 int        tmp_in_15;
 int        tmp_in_16;
 int        tmp_in_17;
 int        tmp_in_18;
 int        tmp_in_19;

 if ( USE_CUSTOM_DASHBOARD )   return; 
 if ( !(ShowInfoPanel) )   return;
 
 if ( ( MQLInfoInteger(MQL_TESTER) == 1 && !(UpdateInfoTesting) ) )   return;
 
 if ( MQLInfoInteger(MQL_TESTER) == 1 && !(UpdateInfoTesting) )
 {
   tmp_do_1 = 0.0;
 }
 else
 {
   tmp_do_2 = 0.0;
   for (tmp_in_3 = OrdersTotal() ; tmp_in_3 >= 0 ; tmp_in_3=tmp_in_3 - 1)
   {
     if ( OrderSelect(tmp_in_3,0,0) != true )   continue;
     
     if ( ( OrderSymbol() != g_336_st_3130 && !(g_17_bo_8C) ) )   continue;
     tmp_in_4 = OrderMagicNumber();
     tmp_in_5=ST1_MagicNumber + 1;
     if ( tmp_in_4 != tmp_in_5 )
     {
       tmp_in_5 = OrderMagicNumber();
       tmp_in_6=ST1_MagicNumber + 2;
       if ( tmp_in_5 != tmp_in_6 )
       {
         tmp_in_6 = OrderMagicNumber();
         tmp_in_7=ST1_MagicNumber + 3;
         if ( tmp_in_6 != tmp_in_7 )
         {
           tmp_in_7 = OrderMagicNumber();
           tmp_in_8=ST1_MagicNumber + 4;
           if ( tmp_in_7 != tmp_in_8 )
           {
             tmp_in_8 = OrderMagicNumber();
             tmp_in_9=ST1_MagicNumber + 5;
             if ( tmp_in_8 != tmp_in_9 )
             {
               tmp_in_9 = OrderMagicNumber();
               tmp_in_10=ST1_MagicNumber + 6;
               if ( tmp_in_9 != tmp_in_10 )
               {
                 tmp_in_10 = OrderMagicNumber();
                 tmp_in_11=ST1_MagicNumber + 7;
                 if ( tmp_in_10 != tmp_in_11 )
                 {
                   tmp_in_11 = OrderMagicNumber();
                   tmp_in_12=ST1_MagicNumber + 8;
                   if ( tmp_in_11 != tmp_in_12 )
                   {
                     tmp_in_12 = OrderMagicNumber();
                     tmp_in_13=ST1_MagicNumber + 9;
                     if ( tmp_in_12 != tmp_in_13 )
                     {
                       tmp_in_13 = OrderMagicNumber();
                       tmp_in_14=ST1_MagicNumber + 10;
                       if ( tmp_in_13 != tmp_in_14 )
                       {
                         tmp_in_14 = OrderMagicNumber();
                         tmp_in_15=ST1_MagicNumber + 11;
                         if ( tmp_in_14 != tmp_in_15 )
                         {
                           tmp_in_15 = OrderMagicNumber();
                           tmp_in_16=ST1_MagicNumber + 12;
                           if ( tmp_in_15 != tmp_in_16 )
                           {
                             tmp_in_16 = OrderMagicNumber();
                             tmp_in_17=ST1_MagicNumber + 13;
                             if ( tmp_in_16 != tmp_in_17 )
                             {
                               tmp_in_17 = OrderMagicNumber();
                               tmp_in_18=ST1_MagicNumber + 14;
                               if ( tmp_in_17 != tmp_in_18 )
                               {
                                 tmp_in_18 = OrderMagicNumber();
                                 tmp_in_19=ST1_MagicNumber + 15;
                               if ( tmp_in_18 != tmp_in_19 )   continue;
                               }
                             }
                           }
                         }
                       }
                     }
                   }
                 }
               }
             }
           }
         }
       }
     }
     if ( ( OrderType() != 0 && OrderType() != 1 ) )   continue;
     tmp_do_2 = OrderProfit() + OrderSwap() + OrderCommission() + tmp_do_2;
     
   }
   g_323_do_2CA0_si30[g_328_in_3100] = tmp_do_2;
   tmp_do_1 = tmp_do_2;
 }
 ObjectSetString(0,"lineopl" + IntegerToString(0,0,32),OBJPROP_TEXT,"Open P/L: " + DoubleToString(tmp_do_1,2)); 
 ObjectSetString(0,"linea" + IntegerToString(0,0,32),OBJPROP_TEXT,"Account Balance: " + DoubleToString(AccountBalance(),2)); 
 if ( g_19_in_9C == 1 )
 {
   local_1_st = "conservative" ;
 }
 else
 {
   if ( g_19_in_9C == 2 )
   {
     local_1_st = "moderate" ;
   }
   else
   {
     if ( g_19_in_9C == 3 )
     {
       local_1_st = "intense" ;
     }
     else
     {
       if ( g_19_in_9C == 4 )
       {
         local_1_st = "extreme" ;
       }
       else
       {
         if ( g_19_in_9C == 0 )
         {
           local_1_st = "extreme conservative" ;
         }
         else
         {
           local_1_st = "manual strategy selection" ;
         }
       }
     }
   }
 }
 ObjectSetString(0,"lines",OBJPROP_TEXT,"Trade Frequency: " + local_1_st); 
 if ( Risk == 1234 )
 {
   ObjectSetString(0,"linet",OBJPROP_TEXT,"Max allowed DD: " + string(MaxAllowedDD) + "%"); 
 }
 else
 {
   if ( Risk == 3 )
   {
     ObjectSetString(0,"linet",OBJPROP_TEXT,"Max risk per strategy: " + string(MaxRiskPerStrategy_) + "%"); 
   }
   else
   {
      ObjectSetString(0,"linet",OBJPROP_TEXT,"Manual lotsize: " + string(StartLotsRuntime) + "lots"); 
   }
 }
 }
//lizong_27 <<==--------   --------
 void lizong_28()
 {
  int       local_1_in;
  string    local_2_st;
  int       local_3_in;
//----- -----

 if ( USE_CUSTOM_DASHBOARD )   return; 
 if ( !(ShowInfoPanel) )   return;
 
 if ( ( MQLInfoInteger(MQL_TESTER) == 1 && !(UpdateInfoTesting) ) )   return;
 local_1_in = g_340_in_3310 ;
 for (local_3_in = 0 ; local_3_in < 9 ; local_3_in ++)
 {
   local_2_st="Strategy " + IntegerToString(local_3_in + 1,0,32);
   ObjectSetString(0,"info_ea" + IntegerToString(local_1_in,0,32),OBJPROP_TEXT,local_2_st); 
   local_1_in ++;
   local_2_st = DoubleToString(NormalizeDouble(g_400_do_67B4_si99[local_3_in],2),2) ;
   ObjectSetString(0,"info_ea" + IntegerToString(local_1_in,0,32),OBJPROP_TEXT,local_2_st); 
   local_1_in ++;
   local_2_st = DoubleToString(NormalizeDouble(g_345_do_3AAC_si99[local_3_in],2),2) ;
   ObjectSetString(0,"info_ea" + IntegerToString(local_1_in,0,32),OBJPROP_TEXT,local_2_st); 
   local_1_in ++;
   local_2_st = DoubleToString(NormalizeDouble(g_223_do_1AC4_si99[local_3_in],2),2) ;
   ObjectSetString(0,"info_ea" + IntegerToString(local_1_in,0,32),OBJPROP_TEXT,local_2_st); 
   local_1_in ++;
 }
 }
//lizong_28 <<==--------   --------
 void lizong_29()
 {
 double     tmp_do_1;
 double     tmp_do_2;
 int        tmp_in_3;
 int        tmp_in_4;
 int        tmp_in_5;
 int        tmp_in_6;
 int        tmp_in_7;
 int        tmp_in_8;
 int        tmp_in_9;
 int        tmp_in_10;
 int        tmp_in_11;
 int        tmp_in_12;
 int        tmp_in_13;
 int        tmp_in_14;
 int        tmp_in_15;
 int        tmp_in_16;
 int        tmp_in_17;
 int        tmp_in_18;
 int        tmp_in_19;
 int        tmp_in_20;

 if ( USE_CUSTOM_DASHBOARD )   return; 
 if ( !(ShowInfoPanel) )   return;
 
 if ( ( MQLInfoInteger(MQL_TESTER) == 1 && !(UpdateInfoTesting) ) )   return;
 ObjectSetString(0,"lineto" + IntegerToString(0,0,32),OBJPROP_TEXT,"Total profits/losses so far: " + IntegerToString(lizong_30(0,9999999),0,32) + "/" + IntegerToString(lizong_31(0,9999999),0,32)); 
 if ( MQLInfoInteger(MQL_TESTER) == 1 && !(UpdateInfoTesting) )
 {
   tmp_do_1 = 0.0;
 }
 else
 {
   tmp_do_2 = 0.0;
   tmp_in_3 = 0;
   for (tmp_in_4 = HistoryTotal() ; tmp_in_4 >= 0 ; tmp_in_4=tmp_in_4 - 1)
   {
     if ( OrderSelect(tmp_in_4,0,1) != true )   continue;
     
     if ( ( OrderSymbol() != g_336_st_3130 && !(g_17_bo_8C) ) )   continue;
     tmp_in_5 = OrderMagicNumber();
     tmp_in_6=ST1_MagicNumber + 1;
     if ( tmp_in_5 != tmp_in_6 )
     {
       tmp_in_6 = OrderMagicNumber();
       tmp_in_7=ST1_MagicNumber + 2;
       if ( tmp_in_6 != tmp_in_7 )
       {
         tmp_in_7 = OrderMagicNumber();
         tmp_in_8=ST1_MagicNumber + 3;
         if ( tmp_in_7 != tmp_in_8 )
         {
           tmp_in_8 = OrderMagicNumber();
           tmp_in_9=ST1_MagicNumber + 4;
           if ( tmp_in_8 != tmp_in_9 )
           {
             tmp_in_9 = OrderMagicNumber();
             tmp_in_10=ST1_MagicNumber + 5;
             if ( tmp_in_9 != tmp_in_10 )
             {
               tmp_in_10 = OrderMagicNumber();
               tmp_in_11=ST1_MagicNumber + 6;
               if ( tmp_in_10 != tmp_in_11 )
               {
                 tmp_in_11 = OrderMagicNumber();
                 tmp_in_12=ST1_MagicNumber + 7;
                 if ( tmp_in_11 != tmp_in_12 )
                 {
                   tmp_in_12 = OrderMagicNumber();
                   tmp_in_13=ST1_MagicNumber + 8;
                   if ( tmp_in_12 != tmp_in_13 )
                   {
                     tmp_in_13 = OrderMagicNumber();
                     tmp_in_14=ST1_MagicNumber + 9;
                     if ( tmp_in_13 != tmp_in_14 )
                     {
                       tmp_in_14 = OrderMagicNumber();
                       tmp_in_15=ST1_MagicNumber + 10;
                       if ( tmp_in_14 != tmp_in_15 )
                       {
                         tmp_in_15 = OrderMagicNumber();
                         tmp_in_16=ST1_MagicNumber + 11;
                         if ( tmp_in_15 != tmp_in_16 )
                         {
                           tmp_in_16 = OrderMagicNumber();
                           tmp_in_17=ST1_MagicNumber + 12;
                           if ( tmp_in_16 != tmp_in_17 )
                           {
                             tmp_in_17 = OrderMagicNumber();
                             tmp_in_18=ST1_MagicNumber + 13;
                             if ( tmp_in_17 != tmp_in_18 )
                             {
                               tmp_in_18 = OrderMagicNumber();
                               tmp_in_19=ST1_MagicNumber + 14;
                               if ( tmp_in_18 != tmp_in_19 )
                               {
                                 tmp_in_19 = OrderMagicNumber();
                                 tmp_in_20=ST1_MagicNumber + 15;
                               if ( tmp_in_19 != tmp_in_20 )   continue;
                               }
                             }
                           }
                         }
                       }
                     }
                   }
                 }
               }
             }
           }
         }
       }
     }
     tmp_in_3=tmp_in_3 + 1;
     tmp_do_2 = tmp_do_2 + OrderProfit() + OrderSwap() + OrderCommission();
     if ( tmp_in_3 >= 1000 )   break;
     
   }
   g_326_do_300C_si30[g_328_in_3100] = tmp_do_2;
   tmp_do_1 = tmp_do_2;
 }
 ObjectSetString(0,"linetp" + IntegerToString(0,0,32),OBJPROP_TEXT,"Total P/L so far: " + DoubleToString(NormalizeDouble(tmp_do_1,2),2)); 
 }
//lizong_29 <<==--------   --------
 int lizong_30( int arg_0_in,int arg_1_in)
 {
  double    local_2_do;
  int       local_3_in;
  int       local_4_in;
  int       local_5_in;
//----- -----
 int        tmp_in_1;
 int        tmp_in_2;
 int        tmp_in_3;
 int        tmp_in_4;
 int        tmp_in_5;
 int        tmp_in_6;
 int        tmp_in_7;
 int        tmp_in_8;
 int        tmp_in_9;
 int        tmp_in_10;
 int        tmp_in_11;
 int        tmp_in_12;
 int        tmp_in_13;
 int        tmp_in_14;
 int        tmp_in_15;
 int        tmp_in_16;

 if ( MQLInfoInteger(MQL_TESTER) == 1 && !(UpdateInfoTesting) )
 {
   return(0); 
 }
 local_2_do = 0.0 ;
 local_3_in = 0 ;
 local_4_in = 0 ;
 for (local_5_in = HistoryTotal() ; local_5_in >= 0 ; local_5_in --)
 {
   if ( OrderSelect(local_5_in,0,1) != true )   continue;
   
   if ( ( OrderSymbol() != g_336_st_3130 && !(g_17_bo_8C) ) )   continue;
   tmp_in_1 = OrderMagicNumber();
   tmp_in_2=ST1_MagicNumber + 1;
   if ( tmp_in_1 != tmp_in_2 )
   {
     tmp_in_2 = OrderMagicNumber();
     tmp_in_3=ST1_MagicNumber + 2;
     if ( tmp_in_2 != tmp_in_3 )
     {
       tmp_in_3 = OrderMagicNumber();
       tmp_in_4=ST1_MagicNumber + 3;
       if ( tmp_in_3 != tmp_in_4 )
       {
         tmp_in_4 = OrderMagicNumber();
         tmp_in_5=ST1_MagicNumber + 4;
         if ( tmp_in_4 != tmp_in_5 )
         {
           tmp_in_5 = OrderMagicNumber();
           tmp_in_6=ST1_MagicNumber + 5;
           if ( tmp_in_5 != tmp_in_6 )
           {
             tmp_in_6 = OrderMagicNumber();
             tmp_in_7=ST1_MagicNumber + 6;
             if ( tmp_in_6 != tmp_in_7 )
             {
               tmp_in_7 = OrderMagicNumber();
               tmp_in_8=ST1_MagicNumber + 7;
               if ( tmp_in_7 != tmp_in_8 )
               {
                 tmp_in_8 = OrderMagicNumber();
                 tmp_in_9=ST1_MagicNumber + 8;
                 if ( tmp_in_8 != tmp_in_9 )
                 {
                   tmp_in_9 = OrderMagicNumber();
                   tmp_in_10=ST1_MagicNumber + 9;
                   if ( tmp_in_9 != tmp_in_10 )
                   {
                     tmp_in_10 = OrderMagicNumber();
                     tmp_in_11=ST1_MagicNumber + 10;
                     if ( tmp_in_10 != tmp_in_11 )
                     {
                       tmp_in_11 = OrderMagicNumber();
                       tmp_in_12=ST1_MagicNumber + 11;
                       if ( tmp_in_11 != tmp_in_12 )
                       {
                         tmp_in_12 = OrderMagicNumber();
                         tmp_in_13=ST1_MagicNumber + 12;
                         if ( tmp_in_12 != tmp_in_13 )
                         {
                           tmp_in_13 = OrderMagicNumber();
                           tmp_in_14=ST1_MagicNumber + 13;
                           if ( tmp_in_13 != tmp_in_14 )
                           {
                             tmp_in_14 = OrderMagicNumber();
                             tmp_in_15=ST1_MagicNumber + 14;
                             if ( tmp_in_14 != tmp_in_15 )
                             {
                               tmp_in_15 = OrderMagicNumber();
                               tmp_in_16=ST1_MagicNumber + 15;
                             if ( tmp_in_15 != tmp_in_16 )   continue;
                             }
                           }
                         }
                       }
                     }
                   }
                 }
               }
             }
           }
         }
       }
     }
   }
   local_3_in ++;
   if ( ( OrderType() == 0 || OrderType() == 1 ) )
   {
     if ( OrderType() == 0 )
     {
       local_2_do = OrderClosePrice() - OrderOpenPrice() ;
     }
     else
     {
       if ( OrderType() == 1 )
       {
         local_2_do = OrderOpenPrice() - OrderClosePrice() ;
       }
     }
     if ( local_2_do>0.0 )
     {
       local_4_in ++;
     }
   }
   if ( local_3_in >= arg_1_in )   break;
   
 }
 g_324_do_2DC4_si30[g_328_in_3100] = local_4_in;
 return(local_4_in); 
 }
//lizong_30 <<==--------   --------
 int lizong_31( int arg_0_in,int arg_1_in)
 {
  double    local_2_do;
  int       local_3_in;
  int       local_4_in;
  int       local_5_in;
//----- -----
 int        tmp_in_1;
 int        tmp_in_2;
 int        tmp_in_3;
 int        tmp_in_4;
 int        tmp_in_5;
 int        tmp_in_6;
 int        tmp_in_7;
 int        tmp_in_8;
 int        tmp_in_9;
 int        tmp_in_10;
 int        tmp_in_11;
 int        tmp_in_12;
 int        tmp_in_13;
 int        tmp_in_14;
 int        tmp_in_15;
 int        tmp_in_16;

 if ( MQLInfoInteger(MQL_TESTER) == 1 && !(UpdateInfoTesting) )
 {
   return(0); 
 }
 local_2_do = 0.0 ;
 local_3_in = 0 ;
 local_4_in = 0 ;
 for (local_5_in = HistoryTotal() ; local_5_in >= 0 ; local_5_in --)
 {
   if ( OrderSelect(local_5_in,0,1) != true )   continue;
   
   if ( ( OrderSymbol() != g_336_st_3130 && !(g_17_bo_8C) ) )   continue;
   tmp_in_1 = OrderMagicNumber();
   tmp_in_2=ST1_MagicNumber + 1;
   if ( tmp_in_1 != tmp_in_2 )
   {
     tmp_in_2 = OrderMagicNumber();
     tmp_in_3=ST1_MagicNumber + 2;
     if ( tmp_in_2 != tmp_in_3 )
     {
       tmp_in_3 = OrderMagicNumber();
       tmp_in_4=ST1_MagicNumber + 3;
       if ( tmp_in_3 != tmp_in_4 )
       {
         tmp_in_4 = OrderMagicNumber();
         tmp_in_5=ST1_MagicNumber + 4;
         if ( tmp_in_4 != tmp_in_5 )
         {
           tmp_in_5 = OrderMagicNumber();
           tmp_in_6=ST1_MagicNumber + 5;
           if ( tmp_in_5 != tmp_in_6 )
           {
             tmp_in_6 = OrderMagicNumber();
             tmp_in_7=ST1_MagicNumber + 6;
             if ( tmp_in_6 != tmp_in_7 )
             {
               tmp_in_7 = OrderMagicNumber();
               tmp_in_8=ST1_MagicNumber + 7;
               if ( tmp_in_7 != tmp_in_8 )
               {
                 tmp_in_8 = OrderMagicNumber();
                 tmp_in_9=ST1_MagicNumber + 8;
                 if ( tmp_in_8 != tmp_in_9 )
                 {
                   tmp_in_9 = OrderMagicNumber();
                   tmp_in_10=ST1_MagicNumber + 9;
                   if ( tmp_in_9 != tmp_in_10 )
                   {
                     tmp_in_10 = OrderMagicNumber();
                     tmp_in_11=ST1_MagicNumber + 10;
                     if ( tmp_in_10 != tmp_in_11 )
                     {
                       tmp_in_11 = OrderMagicNumber();
                       tmp_in_12=ST1_MagicNumber + 11;
                       if ( tmp_in_11 != tmp_in_12 )
                       {
                         tmp_in_12 = OrderMagicNumber();
                         tmp_in_13=ST1_MagicNumber + 12;
                         if ( tmp_in_12 != tmp_in_13 )
                         {
                           tmp_in_13 = OrderMagicNumber();
                           tmp_in_14=ST1_MagicNumber + 13;
                           if ( tmp_in_13 != tmp_in_14 )
                           {
                             tmp_in_14 = OrderMagicNumber();
                             tmp_in_15=ST1_MagicNumber + 14;
                             if ( tmp_in_14 != tmp_in_15 )
                             {
                               tmp_in_15 = OrderMagicNumber();
                               tmp_in_16=ST1_MagicNumber + 15;
                             if ( tmp_in_15 != tmp_in_16 )   continue;
                             }
                           }
                         }
                       }
                     }
                   }
                 }
               }
             }
           }
         }
       }
     }
   }
   local_3_in ++;
   if ( OrderType() == 0 )
   {
     local_2_do = OrderClosePrice() - OrderOpenPrice() ;
   }
   else
   {
     if ( OrderType() == 1 )
     {
       local_2_do = OrderOpenPrice() - OrderClosePrice() ;
     }
   }
   if ( local_2_do<0.0 )
   {
     local_4_in ++;
   }
   if ( local_3_in >= arg_1_in )   break;
   
 }
 g_325_do_2EE8_si30[g_328_in_3100] = local_4_in;
 return(local_4_in); 
 }
//lizong_31 <<==--------   --------
 void lizong_32()
 {
  int       local_1_in = 0;
  double    local_2_do_si99[99];
  double    local_3_do_si99[99];
  int       local_4_in;
  int       local_5_in;
  bool      local_6_bo;
  int       local_7_in;
  double    local_8_do;
  int       local_9_in;
  int       local_10_in;
//----- -----
 long       tmp_lo_1;
 long       tmp_lo_2;
 long       tmp_lo_3;
 long       tmp_lo_4;
 long       tmp_lo_5;

 if ( ( MQLInfoInteger(MQL_TESTER) == 1 && !(UpdateInfoTesting) ) )   return;
 for (local_4_in = 0 ; local_4_in < g_378_in_5D80 ; local_4_in ++)
 {
   local_2_do_si99[local_4_in] = 0.0;
   local_3_do_si99[local_4_in] = 0.0;
   g_342_bo_3694_si99[local_4_in] = false;
   g_343_in_372C_si99[local_4_in] = 0;
   g_344_in_38EC_si99[local_4_in] = 0;
 }
 for (local_5_in = HistoryTotal() ; local_5_in >= 0 ; local_5_in --)
 {
   if ( OrderSelect(local_5_in,0,1) != true || OrderMagicNumber() != g_93_in_1F0 )   continue;
   local_6_bo = true ;
   for (local_7_in = 0 ; local_7_in < g_378_in_5D80 ; local_7_in ++)
   {
     if ( !(g_342_bo_3694_si99[local_7_in]) )
     {
       local_6_bo = false ;
     }
   }
   if ( ( OrderCloseTime() <  TimeCurrent() - g_153_in_440 * 24 * 60 * 60 && local_6_bo ) )   break;
   local_8_do = OrderLots() * 100.0 ;
   if ( g_151_in_438 == 1 )
   {
     local_8_do = 1.0 ;
   }
   local_9_in = 0 ;
   if ( g_378_in_5D80 <= 0 )   continue;
   
   for ( ; local_9_in < g_378_in_5D80 ; local_9_in ++)
   {
     if ( g_347_st_4144_si99[local_9_in] != OrderSymbol() )   continue;
     
     if ( ( OrderType() != 0 && OrderType() != 1 ) )   continue;
     tmp_lo_1 = OrderCloseTime();
     tmp_lo_2=TimeCurrent() - g_153_in_440 * 24 * 60 * 60;
     if ( tmp_lo_1 <  tmp_lo_2 )
     {
       tmp_lo_2 = OrderCloseTime();
       tmp_lo_3=TimeCurrent() - g_153_in_440 * 24 * 60 * 60;
     if ( (tmp_lo_2 >= tmp_lo_3 || g_342_bo_3694_si99[local_9_in]) )   continue;
     }
     g_343_in_372C_si99[local_9_in] ++;
     if ( g_343_in_372C_si99[local_9_in] >= g_155_in_448 )
     {
       g_342_bo_3694_si99[local_9_in] = true;
     }
     local_2_do_si99[local_9_in] +=OrderProfit() / local_8_do;
     local_2_do_si99[local_9_in] +=OrderSwap() / local_8_do;
     local_2_do_si99[local_9_in] +=OrderCommission() / local_8_do;
     tmp_lo_4 = OrderCloseTime();
     tmp_lo_5=TimeCurrent() - g_154_in_444 * 24 * 60 * 60;
     if ( tmp_lo_4 < tmp_lo_5 )   continue;
     local_3_do_si99[local_9_in] +=OrderProfit() / local_8_do;
     local_3_do_si99[local_9_in] +=OrderSwap() / local_8_do;
     local_3_do_si99[local_9_in] +=OrderCommission() / local_8_do;
     g_344_in_38EC_si99[local_9_in] ++;
     
   }
   
 }
 for (local_10_in = 0 ; local_10_in < g_378_in_5D80 ; local_10_in ++)
 {
   g_349_do_46B4_si99[local_10_in] = local_2_do_si99[local_10_in];
   if ( g_343_in_372C_si99[local_10_in] >  0 )
   {
     g_345_do_3AAC_si99[local_10_in] = NormalizeDouble(local_2_do_si99[local_10_in] / g_343_in_372C_si99[local_10_in],2);
   }
   else
   {
     g_345_do_3AAC_si99[local_10_in] = 0.0;
   }
   g_350_do_4A00_si99[local_10_in] = local_3_do_si99[local_10_in];
   if ( g_344_in_38EC_si99[local_10_in] >  0 )
   {
     g_346_do_3DF8_si99[local_10_in] = NormalizeDouble(local_3_do_si99[local_10_in] / g_344_in_38EC_si99[local_10_in],2);
   }
   else
   {
     g_346_do_3DF8_si99[local_10_in] = 0.0;
   }
 }
 }
//lizong_32 <<==--------   --------
 void lizong_33()
 {
  int       local_1_in;
  double    local_2_do;
  int       local_3_in;
  int       local_4_in;
  int       local_5_in;
  int       local_6_in;
  bool      local_7_bo;
  int       local_8_in;
  int       local_9_in;
  int       local_10_in;
  int       local_11_in;
//----- -----

 lizong_32(); 
 for (local_1_in = 0 ; local_1_in < g_378_in_5D80 ; local_1_in ++)
 {
   local_2_do = g_349_do_46B4_si99[local_1_in] ;
   local_3_in = 1 ;
   for (local_4_in = 0 ; local_4_in < g_378_in_5D80 ; local_4_in ++)
   {
     if ( local_4_in == local_1_in || !(g_349_do_46B4_si99[local_4_in]>local_2_do) )   continue;
     local_3_in ++;
     
   }
   g_356_in_5B14_si99[local_1_in] = local_3_in;
 }
 for (local_5_in = 0 ; local_5_in < g_378_in_5D80 ; local_5_in ++)
 {
   local_6_in = g_356_in_5B14_si99[local_5_in] ;
   local_7_bo = true ;
   do
   {
     local_7_bo = false ;
     local_8_in = 0 ;
     if ( g_378_in_5D80 <= 0 )   continue;
     
     for ( ; local_8_in < g_378_in_5D80 ; local_8_in ++)
     {
       if ( local_8_in == local_5_in || g_356_in_5B14_si99[local_8_in] != g_356_in_5B14_si99[local_5_in] )   continue;
       g_356_in_5B14_si99[local_8_in] ++;
       local_7_bo = true ;
       
     }
     
   }
   while(local_7_bo);
   
 }
 for (local_9_in = 0 ; local_9_in < g_378_in_5D80 ; local_9_in ++)
 {
   g_354_do_5730_si99[local_9_in] = 1.0;
 }
 for (local_10_in = 1 ; local_10_in <= g_378_in_5D80 ; local_10_in ++)
 {
   for (local_11_in = 0 ; local_11_in < g_378_in_5D80 ; local_11_in ++)
   {
     if ( g_356_in_5B14_si99[local_11_in] == local_10_in )
     {
       g_339_in_3184_si99[local_10_in - 1] = local_11_in;
     }
   }
 }
 }
//lizong_33 <<==--------   --------
 void lizong_34()
 {
  int       local_1_in;
  double    local_2_do;
  int       local_3_in;
  int       local_4_in;
  int       local_5_in;
  int       local_6_in;
  bool      local_7_bo;
  int       local_8_in;
  int       local_9_in;
  int       local_10_in;
  int       local_11_in;
//----- -----

 lizong_32(); 
 for (local_1_in = 0 ; local_1_in < g_378_in_5D80 ; local_1_in ++)
 {
   local_2_do = g_345_do_3AAC_si99[local_1_in] ;
   local_3_in = 1 ;
   for (local_4_in = 0 ; local_4_in < g_378_in_5D80 ; local_4_in ++)
   {
     if ( local_4_in == local_1_in || !(g_345_do_3AAC_si99[local_4_in]>local_2_do) )   continue;
     local_3_in ++;
     
   }
   g_356_in_5B14_si99[local_1_in] = local_3_in;
 }
 for (local_5_in = 0 ; local_5_in < g_378_in_5D80 ; local_5_in ++)
 {
   local_6_in = g_356_in_5B14_si99[local_5_in] ;
   local_7_bo = true ;
   do
   {
     local_7_bo = false ;
     local_8_in = 0 ;
     if ( g_378_in_5D80 <= 0 )   continue;
     
     for ( ; local_8_in < g_378_in_5D80 ; local_8_in ++)
     {
       if ( local_8_in == local_5_in || g_356_in_5B14_si99[local_8_in] != g_356_in_5B14_si99[local_5_in] )   continue;
       g_356_in_5B14_si99[local_8_in] ++;
       local_7_bo = true ;
       
     }
     
   }
   while(local_7_bo);
   
 }
 for (local_9_in = 0 ; local_9_in < g_378_in_5D80 ; local_9_in ++)
 {
   g_354_do_5730_si99[local_9_in] = 1.0;
 }
 for (local_10_in = 1 ; local_10_in <= g_378_in_5D80 ; local_10_in ++)
 {
   for (local_11_in = 0 ; local_11_in < g_378_in_5D80 ; local_11_in ++)
   {
     if ( g_356_in_5B14_si99[local_11_in] == local_10_in )
     {
       g_339_in_3184_si99[local_10_in - 1] = local_11_in;
     }
   }
 }
 }
//lizong_34 <<==--------   --------
 double lizong_35( double arg_0_do)
 {
  double    local_2_do;
  string    local_3_st;
//----- -----

 local_2_do = arg_0_do ;
 if ( ( AccountCurrency() == "USD" || AccountCurrency() == "usd" ) )
 {
   local_2_do = arg_0_do ;
 }
 if ( ( AccountCurrency() == "EUR" || AccountCurrency() == "eur" ) )
 {
   local_3_st="EURUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do / iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountCurrency() == "GBP" || AccountCurrency() == "gbp" ) )
 {
   local_3_st="GBPUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do / iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountCurrency() == "AUD" || AccountCurrency() == "aud" ) )
 {
   local_3_st="AUDUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do / iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountCurrency() == "JPY" || AccountCurrency() == "jpy" || AccountCurrency() == "YEN" || AccountCurrency() == "yen" ) )
 {
   local_3_st="USDJPY" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do * iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountCurrency() == "CHF" || AccountCurrency() == "chf" ) )
 {
   local_3_st="USDCHF" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do * iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountCurrency() == "HKD" || AccountCurrency() == "hkd" ) )
 {
   local_3_st="USDHKD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do * iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountCurrency() == "SGD" || AccountCurrency() == "sgd" ) )
 {
   local_3_st="USDSGD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do * iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountCurrency() == "PLN" || AccountCurrency() == "pln" ) )
 {
   local_3_st="USDPLN" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do * iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountCurrency() == "RUB" || AccountCurrency() == "rub" ) )
 {
   local_3_st="USDRUB" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do * iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountCurrency() == "BTC" || AccountCurrency() == "btc" ) )
 {
   local_3_st="BTCUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do / iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountCurrency() == "ETH" || AccountCurrency() == "eth" ) )
 {
   local_3_st="ETHUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do / iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountCurrency() == "BCH" || AccountCurrency() == "bch" ) )
 {
   local_3_st="BCHUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do / iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountCurrency() == "BCC" || AccountCurrency() == "bcc" ) )
 {
   local_3_st="BCCUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do / iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountCurrency() == "XRP" || AccountCurrency() == "xrp" ) )
 {
   local_3_st="XRPUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do / iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountCurrency() == "LTC" || AccountCurrency() == "ltc" ) )
 {
   local_3_st="LTCUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do / iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountCurrency() == "XMR" || AccountCurrency() == "xmr" ) )
 {
   local_3_st="XMRUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do / iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountCurrency() == "DSH" || AccountCurrency() == "dsh" ) )
 {
   local_3_st="DSHUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do / iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountCurrency() == "EOS" || AccountCurrency() == "eos" ) )
 {
   local_3_st="EOSUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do / iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountCurrency() == "TRX" || AccountCurrency() == "trx" ) )
 {
   local_3_st="TRXUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do / iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountCurrency() == "ADA" || AccountCurrency() == "ada" ) )
 {
   local_3_st="ADAUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do / iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountCurrency() == "BSV" || AccountCurrency() == "bsv" ) )
 {
   local_3_st="BSVUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do / iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountCurrency() == "XLM" || AccountCurrency() == "xlm" ) )
 {
   local_3_st="XLMUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do / iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountCurrency() == "GLD" || AccountCurrency() == "gld" ) )
 {
   local_3_st="GLDUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do / iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountCurrency() == "ZEC" || AccountCurrency() == "zec" ) )
 {
   local_3_st="ZECUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do / iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountCurrency() == "XEM" || AccountCurrency() == "xem" ) )
 {
   local_3_st="XEMUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do / iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 return(local_2_do); 
 }
//lizong_35 <<==--------   --------
 double lizong_36( double arg_0_do)
 {
  double    local_2_do;
  string    local_3_st;
//----- -----

 local_2_do = arg_0_do ;
 if ( ( AccountInfoString(ACCOUNT_CURRENCY) == "USD" || AccountInfoString(ACCOUNT_CURRENCY) == "usd" ) )
 {
   local_2_do = arg_0_do ;
 }
 if ( ( AccountInfoString(ACCOUNT_CURRENCY) == "EUR" || AccountInfoString(ACCOUNT_CURRENCY) == "eur" ) )
 {
   local_3_st="EURUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do * iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountInfoString(ACCOUNT_CURRENCY) == "GBP" || AccountInfoString(ACCOUNT_CURRENCY) == "gbp" ) )
 {
   local_3_st="GBPUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do * iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountInfoString(ACCOUNT_CURRENCY) == "AUD" || AccountInfoString(ACCOUNT_CURRENCY) == "aud" ) )
 {
   local_3_st="AUDUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do * iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountInfoString(ACCOUNT_CURRENCY) == "JPY" || AccountInfoString(ACCOUNT_CURRENCY) == "jpy" || AccountInfoString(ACCOUNT_CURRENCY) == "YEN" || AccountInfoString(ACCOUNT_CURRENCY) == "yen" ) )
 {
   local_3_st="USDJPY" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do / iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountInfoString(ACCOUNT_CURRENCY) == "CHF" || AccountInfoString(ACCOUNT_CURRENCY) == "chf" ) )
 {
   local_3_st="USDCHF" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do / iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountInfoString(ACCOUNT_CURRENCY) == "HKD" || AccountInfoString(ACCOUNT_CURRENCY) == "hkd" ) )
 {
   local_3_st="USDHKD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do / iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountInfoString(ACCOUNT_CURRENCY) == "RUB" || AccountInfoString(ACCOUNT_CURRENCY) == "rub" ) )
 {
   local_3_st="USDRUB" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do / iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountInfoString(ACCOUNT_CURRENCY) == "CNH" || AccountInfoString(ACCOUNT_CURRENCY) == "cnh" ) )
 {
   local_3_st="USDCNH" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do / iClose(local_3_st,PERIOD_D1,1) ;
   }
   else
   {
     local_3_st="USDCNY" + g_299_st_2850;
     if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
     {
       local_2_do = arg_0_do / iClose(local_3_st,PERIOD_D1,1) ;
     }
   }
 }
 if ( ( AccountInfoString(ACCOUNT_CURRENCY) == "CNY" || AccountInfoString(ACCOUNT_CURRENCY) == "cny" ) )
 {
   local_3_st="USDCNH" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do / iClose(local_3_st,PERIOD_D1,1) ;
   }
   else
   {
     local_3_st="USDCNY" + g_299_st_2850;
     if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
     {
       local_2_do = arg_0_do / iClose(local_3_st,PERIOD_D1,1) ;
     }
   }
 }
 if ( ( AccountInfoString(ACCOUNT_CURRENCY) == "SGD" || AccountInfoString(ACCOUNT_CURRENCY) == "sgd" ) )
 {
   local_3_st="USDSGD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do / iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountInfoString(ACCOUNT_CURRENCY) == "BTC" || AccountInfoString(ACCOUNT_CURRENCY) == "btc" ) )
 {
   local_3_st="BTCUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do * iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountInfoString(ACCOUNT_CURRENCY) == "ETH" || AccountInfoString(ACCOUNT_CURRENCY) == "eth" ) )
 {
   local_3_st="ETHUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do * iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountInfoString(ACCOUNT_CURRENCY) == "BCH" || AccountInfoString(ACCOUNT_CURRENCY) == "bch" ) )
 {
   local_3_st="BCHUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do * iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountInfoString(ACCOUNT_CURRENCY) == "BCC" || AccountInfoString(ACCOUNT_CURRENCY) == "bcc" ) )
 {
   local_3_st="BCCUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do * iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountInfoString(ACCOUNT_CURRENCY) == "XRP" || AccountInfoString(ACCOUNT_CURRENCY) == "xrp" ) )
 {
   local_3_st="XRPUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do * iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountInfoString(ACCOUNT_CURRENCY) == "LTC" || AccountInfoString(ACCOUNT_CURRENCY) == "ltc" ) )
 {
   local_3_st="LTCUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do * iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountInfoString(ACCOUNT_CURRENCY) == "XMR" || AccountInfoString(ACCOUNT_CURRENCY) == "xmr" ) )
 {
   local_3_st="XMRUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do * iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountInfoString(ACCOUNT_CURRENCY) == "DSH" || AccountInfoString(ACCOUNT_CURRENCY) == "dsh" ) )
 {
   local_3_st="DSHUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do * iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountInfoString(ACCOUNT_CURRENCY) == "EOS" || AccountInfoString(ACCOUNT_CURRENCY) == "eos" ) )
 {
   local_3_st="EOSUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do * iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountInfoString(ACCOUNT_CURRENCY) == "TRX" || AccountInfoString(ACCOUNT_CURRENCY) == "trx" ) )
 {
   local_3_st="TRXUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do * iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountInfoString(ACCOUNT_CURRENCY) == "ADA" || AccountInfoString(ACCOUNT_CURRENCY) == "ada" ) )
 {
   local_3_st="ADAUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do * iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountInfoString(ACCOUNT_CURRENCY) == "BSV" || AccountInfoString(ACCOUNT_CURRENCY) == "bsv" ) )
 {
   local_3_st="BSVUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do * iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountInfoString(ACCOUNT_CURRENCY) == "XLM" || AccountInfoString(ACCOUNT_CURRENCY) == "xlm" ) )
 {
   local_3_st="XLMUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do * iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountInfoString(ACCOUNT_CURRENCY) == "GLD" || AccountInfoString(ACCOUNT_CURRENCY) == "gld" ) )
 {
   local_3_st="GLDUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do * iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountInfoString(ACCOUNT_CURRENCY) == "ZEC" || AccountInfoString(ACCOUNT_CURRENCY) == "zec" ) )
 {
   local_3_st="ZECUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do * iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 if ( ( AccountInfoString(ACCOUNT_CURRENCY) == "XEM" || AccountInfoString(ACCOUNT_CURRENCY) == "xem" ) )
 {
   local_3_st="XEMUSD" + g_299_st_2850;
   if ( iClose(local_3_st,PERIOD_D1,1)>0.0 )
   {
     local_2_do = arg_0_do * iClose(local_3_st,PERIOD_D1,1) ;
   }
 }
 return(MathRound(local_2_do)); 
 }
//lizong_36 <<==--------   --------
 void lizong_37()
 {
 double     tmp_do_1;
 double     tmp_do_2;
 double     tmp_do_3;
 double     tmp_do_4;
 double     tmp_do_5;
 double     tmp_do_6;
 double     tmp_do_7;
 double     tmp_do_8;
 double     tmp_do_9;
 double     tmp_do_10;
 double     tmp_do_11;
 double     tmp_do_12;

 g_71_in_174 = 1440 ;
 g_72_in_178 = 15 ;
 g_73_in_17C = 24 ;
 g_74_in_180 = 3 ;
 g_77_in_188 = 105 ;
 g_80_do_198 = 45.0 ;
 g_81_do_1A0 = 0.0 ;
 tmp_do_1 = AdjustEntry + -275.0;
 if ( Randomization>0.0 )
 {
   tmp_do_2 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_2 = 0.0;
 }
 g_83_do_1B0 = tmp_do_1 + tmp_do_2 ;
 tmp_do_2 = AdjustEntry + -160.0;
 if ( Randomization>0.0 )
 {
   tmp_do_3 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_3 = 0.0;
 }
 g_84_do_1B8 = tmp_do_2 + tmp_do_3 ;
 g_86_in_1C8 = 5 ;
 g_88_do_1D0 = 30.0 ;
 g_89_in_1D8 = 35 ;
 g_99_in_22C = 1 ;
 tmp_do_3 = AdjustSL + 6100.0;
 if ( Randomization>0.0 )
 {
   tmp_do_4 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_4 = 0.0;
 }
 g_100_do_230 = tmp_do_3 + tmp_do_4 ;
 tmp_do_4 = AdjustTP + 1450.0;
 if ( Randomization>0.0 )
 {
   tmp_do_5 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_5 = 0.0;
 }
 g_101_do_238 = tmp_do_4 + tmp_do_5 ;
 tmp_do_5 = AdjustTrailSL + 1800.0;
 if ( Randomization>0.0 )
 {
   tmp_do_6 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_6 = 0.0;
 }
 g_103_do_250 = tmp_do_5 + tmp_do_6 ;
 if ( Randomization>0.0 )
 {
   tmp_do_7 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_7 = 0.0;
 }
 g_104_do_258 = tmp_do_7 + 1800.0 ;
 if ( Randomization>0.0 )
 {
   tmp_do_8 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_8 = 0.0;
 }
 g_105_do_260 = tmp_do_8 + 5000.0 ;
 g_106_do_268 = 0.1 ;
 g_107_do_270 = 0.0 ;
 if ( Randomization>0.0 )
 {
   tmp_do_9 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_9 = 0.0;
 }
 g_109_do_280 = tmp_do_9 + 1600.0 ;
 tmp_do_9 = AdjustTrailTP + 700.0;
 if ( Randomization>0.0 )
 {
   tmp_do_10 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_10 = 0.0;
 }
 g_108_do_278 = tmp_do_9 + tmp_do_10 ;
 if ( Randomization>0.0 )
 {
   tmp_do_11 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_11 = 0.0;
 }
 g_113_do_2A8 = tmp_do_11 + 930.0 ;
 tmp_do_11 = AdjustBreakEven + 120.0;
 if ( Randomization>0.0 )
 {
   tmp_do_12 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_12 = 0.0;
 }
 g_114_do_2B0 = tmp_do_11 + tmp_do_12 ;
 g_117_in_2C8 = 60 ;
 g_118_in_2CC = 50 ;
 g_119_in_2D0 = 14 ;
 g_120_in_2D4 = 12 ;
 g_121_in_2D8 = 300 ;
 g_123_do_2E0 = 22.0 ;
 g_87_in_1CC = 5 ;
 if ( !(RemoveCommentSuffix) )
 {
   g_334_st_3120=ST1_Comment + "_XAUUSD_1";
 }
 g_93_in_1F0=ST1_MagicNumber + 1;
 g_397_do_6768 = lizong_35(145.0) ;
 if ( !(UseVariableValues) )   return;
 g_7_do_50 = 2000.0 ;
 g_397_do_6768 = lizong_35(60.0) ;
 }
//lizong_37 <<==--------   --------
 void lizong_38()
 {
 double     tmp_do_1;
 double     tmp_do_2;
 double     tmp_do_3;
 double     tmp_do_4;
 double     tmp_do_5;
 double     tmp_do_6;
 double     tmp_do_7;
 double     tmp_do_8;
 double     tmp_do_9;
 double     tmp_do_10;
 double     tmp_do_11;
 double     tmp_do_12;
 double     tmp_do_13;

 g_71_in_174 = 240 ;
 g_72_in_178 = 60 ;
 g_73_in_17C = 12 ;
 g_74_in_180 = 8 ;
 g_77_in_188 = 90 ;
 g_80_do_198 = 1050.0 ;
 g_81_do_1A0 = 0.0 ;
 tmp_do_1 = AdjustEntry + -40.0;
 if ( Randomization>0.0 )
 {
   tmp_do_2 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_2 = 0.0;
 }
 g_83_do_1B0 = tmp_do_1 + tmp_do_2 ;
 tmp_do_2 = AdjustEntry + -100.0;
 if ( Randomization>0.0 )
 {
   tmp_do_3 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_3 = 0.0;
 }
 g_84_do_1B8 = tmp_do_2 + tmp_do_3 ;
 g_86_in_1C8 = 2 ;
 g_88_do_1D0 = 130.0 ;
 g_89_in_1D8 = 192 ;
 g_99_in_22C = 5 ;
 if ( !(UseHL_TrailingSL) )
 {
   tmp_do_3 = AdjustSL + 700.0;
   if ( Randomization>0.0 )
   {
     tmp_do_4 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
   }
   else
   {
     tmp_do_4 = 0.0;
   }
   g_100_do_230 = tmp_do_3 + tmp_do_4 ;
 }
 else
 {
   tmp_do_4 = AdjustSL + 800.0;
   if ( Randomization>0.0 )
   {
     tmp_do_5 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
   }
   else
   {
     tmp_do_5 = 0.0;
   }
   g_100_do_230 = tmp_do_4 + tmp_do_5 ;
 }
 tmp_do_5 = AdjustTP + 4900.0;
 if ( Randomization>0.0 )
 {
   tmp_do_6 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_6 = 0.0;
 }
 g_101_do_238 = tmp_do_5 + tmp_do_6 ;
 tmp_do_6 = AdjustTrailSL + 1300.0;
 if ( Randomization>0.0 )
 {
   tmp_do_7 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_7 = 0.0;
 }
 g_103_do_250 = tmp_do_6 + tmp_do_7 ;
 if ( Randomization>0.0 )
 {
   tmp_do_8 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_8 = 0.0;
 }
 g_104_do_258 = tmp_do_8 + 1450.0 ;
 if ( Randomization>0.0 )
 {
   tmp_do_9 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_9 = 0.0;
 }
 g_105_do_260 = tmp_do_9 + 2000.0 ;
 g_106_do_268 = 0.1 ;
 g_107_do_270 = 0.0 ;
 if ( Randomization>0.0 )
 {
   tmp_do_10 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_10 = 0.0;
 }
 g_109_do_280 = tmp_do_10 + 1400.0 ;
 tmp_do_10 = AdjustTrailTP + 200.0;
 if ( Randomization>0.0 )
 {
   tmp_do_11 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_11 = 0.0;
 }
 g_108_do_278 = tmp_do_10 + tmp_do_11 ;
 if ( Randomization>0.0 )
 {
   tmp_do_12 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_12 = 0.0;
 }
 g_113_do_2A8 = tmp_do_12 + 500.0 ;
 tmp_do_12 = AdjustBreakEven + 200.0;
 if ( Randomization>0.0 )
 {
   tmp_do_13 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_13 = 0.0;
 }
 g_114_do_2B0 = tmp_do_12 + tmp_do_13 ;
 g_117_in_2C8 = 60 ;
 g_118_in_2CC = 50 ;
 g_119_in_2D0 = 14 ;
 g_120_in_2D4 = 6 ;
 g_121_in_2D8 = 400 ;
 g_123_do_2E0 = 32.0 ;
 g_87_in_1CC = 99 ;
 if ( !(RemoveCommentSuffix) )
 {
   g_334_st_3120=ST1_Comment + "_XAUUSD_4";
 }
 g_93_in_1F0=ST1_MagicNumber + 2;
 g_397_do_6768 = lizong_35(57.0) ;
 if ( !(UseVariableValues) )   return;
 g_7_do_50 = 1600.0 ;
 g_397_do_6768 = lizong_35(52.0) ;
 }
//lizong_38 <<==--------   --------
 void lizong_39()
 {
 double     tmp_do_1;
 double     tmp_do_2;
 double     tmp_do_3;
 double     tmp_do_4;
 double     tmp_do_5;
 double     tmp_do_6;
 double     tmp_do_7;
 double     tmp_do_8;
 double     tmp_do_9;
 double     tmp_do_10;
 double     tmp_do_11;
 double     tmp_do_12;

 g_71_in_174 = 1440 ;
 g_72_in_178 = 60 ;
 g_73_in_17C = 15 ;
 g_74_in_180 = 3 ;
 g_77_in_188 = 230 ;
 g_80_do_198 = 550.0 ;
 g_81_do_1A0 = 0.0 ;
 tmp_do_1 = AdjustEntry + -170.0;
 if ( Randomization>0.0 )
 {
   tmp_do_2 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_2 = 0.0;
 }
 g_83_do_1B0 = tmp_do_1 + tmp_do_2 ;
 tmp_do_2 = AdjustEntry + -70.0;
 if ( Randomization>0.0 )
 {
   tmp_do_3 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_3 = 0.0;
 }
 g_84_do_1B8 = tmp_do_2 + tmp_do_3 ;
 g_86_in_1C8 = 1 ;
 g_88_do_1D0 = 480.0 ;
 g_89_in_1D8 = 480 ;
 g_99_in_22C = 1 ;
 tmp_do_3 = AdjustSL + 1000.0;
 if ( Randomization>0.0 )
 {
   tmp_do_4 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_4 = 0.0;
 }
 g_100_do_230 = tmp_do_3 + tmp_do_4 ;
 tmp_do_4 = AdjustTP + 4100.0;
 if ( Randomization>0.0 )
 {
   tmp_do_5 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_5 = 0.0;
 }
 g_101_do_238 = tmp_do_4 + tmp_do_5 ;
 tmp_do_5 = AdjustTrailSL + 450.0;
 if ( Randomization>0.0 )
 {
   tmp_do_6 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_6 = 0.0;
 }
 g_103_do_250 = tmp_do_5 + tmp_do_6 ;
 if ( Randomization>0.0 )
 {
   tmp_do_7 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_7 = 0.0;
 }
 g_104_do_258 = tmp_do_7 + 1400.0 ;
 if ( Randomization>0.0 )
 {
   tmp_do_8 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_8 = 0.0;
 }
 g_105_do_260 = tmp_do_8 + 5000.0 ;
 g_106_do_268 = 0.1 ;
 g_107_do_270 = 0.0 ;
 if ( Randomization>0.0 )
 {
   tmp_do_9 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_9 = 0.0;
 }
 g_109_do_280 = tmp_do_9 + 1600.0 ;
 tmp_do_9 = AdjustTrailTP + 400.0;
 if ( Randomization>0.0 )
 {
   tmp_do_10 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_10 = 0.0;
 }
 g_108_do_278 = tmp_do_9 + tmp_do_10 ;
 if ( Randomization>0.0 )
 {
   tmp_do_11 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_11 = 0.0;
 }
 g_113_do_2A8 = tmp_do_11 + 500.0 ;
 tmp_do_11 = AdjustBreakEven + 100.0;
 if ( Randomization>0.0 )
 {
   tmp_do_12 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_12 = 0.0;
 }
 g_114_do_2B0 = tmp_do_11 + tmp_do_12 ;
 g_117_in_2C8 = 60 ;
 g_118_in_2CC = 50 ;
 g_119_in_2D0 = 1 ;
 g_120_in_2D4 = 5 ;
 g_121_in_2D8 = 700 ;
 g_123_do_2E0 = 22.0 ;
 g_87_in_1CC = 99 ;
 if ( !(RemoveCommentSuffix) )
 {
   g_334_st_3120=ST1_Comment + "_XAUUSD_2";
 }
 g_93_in_1F0=ST1_MagicNumber + 5;
 g_397_do_6768 = lizong_35(30.0) ;
 if ( !(UseVariableValues) )   return;
 g_7_do_50 = 2000.0 ;
 g_397_do_6768 = lizong_35(30.0) ;
 }
//lizong_39 <<==--------   --------
 void lizong_40()
 {
 double     tmp_do_1;
 double     tmp_do_2;
 double     tmp_do_3;
 double     tmp_do_4;
 double     tmp_do_5;
 double     tmp_do_6;
 double     tmp_do_7;
 double     tmp_do_8;
 double     tmp_do_9;
 double     tmp_do_10;
 double     tmp_do_11;
 double     tmp_do_12;
 double     tmp_do_13;

 g_71_in_174 = 1440 ;
 g_72_in_178 = 60 ;
 g_73_in_17C = 7 ;
 g_74_in_180 = 2 ;
 g_77_in_188 = 20 ;
 g_80_do_198 = 250.0 ;
 g_81_do_1A0 = 0.0 ;
 tmp_do_1 = AdjustEntry + -112.0;
 if ( Randomization>0.0 )
 {
   tmp_do_2 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_2 = 0.0;
 }
 g_83_do_1B0 = tmp_do_1 + tmp_do_2 ;
 tmp_do_2 = AdjustEntry + -104.0;
 if ( Randomization>0.0 )
 {
   tmp_do_3 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_3 = 0.0;
 }
 g_84_do_1B8 = tmp_do_2 + tmp_do_3 ;
 g_86_in_1C8 = 1 ;
 g_88_do_1D0 = 980.0 ;
 g_89_in_1D8 = 432 ;
 g_99_in_22C = 1 ;
 if ( !(UseHL_TrailingSL) )
 {
   tmp_do_3 = AdjustSL + 600.0;
   if ( Randomization>0.0 )
   {
     tmp_do_4 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
   }
   else
   {
     tmp_do_4 = 0.0;
   }
   g_100_do_230 = tmp_do_3 + tmp_do_4 ;
 }
 else
 {
   tmp_do_4 = AdjustSL + 700.0;
   if ( Randomization>0.0 )
   {
     tmp_do_5 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
   }
   else
   {
     tmp_do_5 = 0.0;
   }
   g_100_do_230 = tmp_do_4 + tmp_do_5 ;
 }
 tmp_do_5 = AdjustTP + 3630.0;
 if ( Randomization>0.0 )
 {
   tmp_do_6 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_6 = 0.0;
 }
 g_101_do_238 = tmp_do_5 + tmp_do_6 ;
 tmp_do_6 = AdjustTrailSL + 500.0;
 if ( Randomization>0.0 )
 {
   tmp_do_7 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_7 = 0.0;
 }
 g_103_do_250 = tmp_do_6 + tmp_do_7 ;
 if ( Randomization>0.0 )
 {
   tmp_do_8 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_8 = 0.0;
 }
 g_104_do_258 = tmp_do_8 + 400.0 ;
 if ( Randomization>0.0 )
 {
   tmp_do_9 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_9 = 0.0;
 }
 g_105_do_260 = tmp_do_9 + 5000.0 ;
 if ( Randomization>0.0 )
 {
   tmp_do_10 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_10 = 0.0;
 }
 g_109_do_280 = tmp_do_10 + 1000.0 ;
 tmp_do_10 = AdjustTrailTP + 2000.0;
 if ( Randomization>0.0 )
 {
   tmp_do_11 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_11 = 0.0;
 }
 g_108_do_278 = tmp_do_10 + tmp_do_11 ;
 g_106_do_268 = 0.1 ;
 g_107_do_270 = 0.0 ;
 if ( Randomization>0.0 )
 {
   tmp_do_12 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_12 = 0.0;
 }
 g_113_do_2A8 = tmp_do_12 + 400.0 ;
 tmp_do_12 = AdjustBreakEven;
 if ( Randomization>0.0 )
 {
   tmp_do_13 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_13 = 0.0;
 }
 g_114_do_2B0 = tmp_do_12 + tmp_do_13 ;
 g_117_in_2C8 = 60 ;
 g_118_in_2CC = 50 ;
 g_119_in_2D0 = 7 ;
 g_120_in_2D4 = 4 ;
 g_121_in_2D8 = 100 ;
 g_123_do_2E0 = 0.0 ;
 g_87_in_1CC = 99 ;
 if ( !(RemoveCommentSuffix) )
 {
   g_334_st_3120=ST1_Comment + "_B3";
 }
 g_93_in_1F0=ST1_MagicNumber + 8;
 g_397_do_6768 = lizong_35(32.0) ;
 if ( !(UseVariableValues) )   return;
 g_7_do_50 = 2000.0 ;
 g_397_do_6768 = lizong_35(35.0) ;
 }
//lizong_40 <<==--------   --------
 void lizong_41()
 {
 double     tmp_do_1;
 double     tmp_do_2;
 double     tmp_do_3;
 double     tmp_do_4;
 double     tmp_do_5;
 double     tmp_do_6;
 double     tmp_do_7;
 double     tmp_do_8;
 double     tmp_do_9;
 double     tmp_do_10;
 double     tmp_do_11;
 double     tmp_do_12;

 g_71_in_174 = 60 ;
 g_72_in_178 = 5 ;
 g_73_in_17C = 26 ;
 g_74_in_180 = 24 ;
 g_77_in_188 = 140 ;
 g_80_do_198 = 120.0 ;
 g_81_do_1A0 = 0.0 ;
 tmp_do_1 = AdjustEntry + -105.0;
 if ( Randomization>0.0 )
 {
   tmp_do_2 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_2 = 0.0;
 }
 g_83_do_1B0 = tmp_do_1 + tmp_do_2 ;
 tmp_do_2 = AdjustEntry + -130.0;
 if ( Randomization>0.0 )
 {
   tmp_do_3 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_3 = 0.0;
 }
 g_84_do_1B8 = tmp_do_2 + tmp_do_3 ;
 g_86_in_1C8 = 5 ;
 g_88_do_1D0 = 55.0 ;
 g_89_in_1D8 = 20 ;
 g_99_in_22C = 1 ;
 tmp_do_3 = AdjustSL + 10100.0;
 if ( Randomization>0.0 )
 {
   tmp_do_4 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_4 = 0.0;
 }
 g_100_do_230 = tmp_do_3 + tmp_do_4 ;
 tmp_do_4 = AdjustTP + 824.0;
 if ( Randomization>0.0 )
 {
   tmp_do_5 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_5 = 0.0;
 }
 g_101_do_238 = tmp_do_4 + tmp_do_5 ;
 tmp_do_5 = AdjustTrailSL + 500.0;
 if ( Randomization>0.0 )
 {
   tmp_do_6 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_6 = 0.0;
 }
 g_103_do_250 = tmp_do_5 + tmp_do_6 ;
 if ( Randomization>0.0 )
 {
   tmp_do_7 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_7 = 0.0;
 }
 g_104_do_258 = tmp_do_7 + 1200.0 ;
 if ( Randomization>0.0 )
 {
   tmp_do_8 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_8 = 0.0;
 }
 g_105_do_260 = tmp_do_8 + 5000.0 ;
 g_106_do_268 = 0.1 ;
 g_107_do_270 = 0.0 ;
 if ( Randomization>0.0 )
 {
   tmp_do_9 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_9 = 0.0;
 }
 g_109_do_280 = tmp_do_9 + 1950.0 ;
 tmp_do_9 = AdjustTrailTP + 350.0;
 if ( Randomization>0.0 )
 {
   tmp_do_10 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_10 = 0.0;
 }
 g_108_do_278 = tmp_do_9 + tmp_do_10 ;
 if ( Randomization>0.0 )
 {
   tmp_do_11 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_11 = 0.0;
 }
 g_113_do_2A8 = tmp_do_11 + 330.0 ;
 tmp_do_11 = AdjustBreakEven + 80.0;
 if ( Randomization>0.0 )
 {
   tmp_do_12 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_12 = 0.0;
 }
 g_114_do_2B0 = tmp_do_11 + tmp_do_12 ;
 g_117_in_2C8 = 60 ;
 g_118_in_2CC = 50 ;
 g_119_in_2D0 = 0 ;
 g_120_in_2D4 = 0 ;
 g_121_in_2D8 = 100 ;
 g_123_do_2E0 = 0.0 ;
 g_87_in_1CC = 5 ;
 if ( !(RemoveCommentSuffix) )
 {
   g_334_st_3120=ST1_Comment + "_A1";
 }
 g_93_in_1F0=ST1_MagicNumber + 9;
 g_397_do_6768 = lizong_35(348.0) ;
 if ( !(UseVariableValues) )   return;
 g_7_do_50 = 2400.0 ;
 g_397_do_6768 = lizong_35(140.0) ;
 }
//lizong_41 <<==--------   --------
 void lizong_42()
 {
 double     tmp_do_1;
 double     tmp_do_2;
 double     tmp_do_3;
 double     tmp_do_4;
 double     tmp_do_5;
 double     tmp_do_6;
 double     tmp_do_7;
 double     tmp_do_8;
 double     tmp_do_9;
 double     tmp_do_10;
 double     tmp_do_11;
 double     tmp_do_12;

 g_71_in_174 = 60 ;
 g_72_in_178 = 15 ;
 g_73_in_17C = 30 ;
 g_74_in_180 = 19 ;
 g_77_in_188 = 110 ;
 g_80_do_198 = 160.0 ;
 g_81_do_1A0 = 0.0 ;
 tmp_do_1 = AdjustEntry + -115.0;
 if ( Randomization>0.0 )
 {
   tmp_do_2 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_2 = 0.0;
 }
 g_83_do_1B0 = tmp_do_1 + tmp_do_2 ;
 tmp_do_2 = AdjustEntry + -105.0;
 if ( Randomization>0.0 )
 {
   tmp_do_3 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_3 = 0.0;
 }
 g_84_do_1B8 = tmp_do_2 + tmp_do_3 ;
 g_86_in_1C8 = 3 ;
 g_88_do_1D0 = 55.0 ;
 g_89_in_1D8 = 30 ;
 g_99_in_22C = 1 ;
 tmp_do_3 = AdjustSL + 5300.0;
 if ( Randomization>0.0 )
 {
   tmp_do_4 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_4 = 0.0;
 }
 g_100_do_230 = tmp_do_3 + tmp_do_4 ;
 tmp_do_4 = AdjustTP + 927.0;
 if ( Randomization>0.0 )
 {
   tmp_do_5 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_5 = 0.0;
 }
 g_101_do_238 = tmp_do_4 + tmp_do_5 ;
 tmp_do_5 = AdjustTrailSL + 495.0;
 if ( Randomization>0.0 )
 {
   tmp_do_6 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_6 = 0.0;
 }
 g_103_do_250 = tmp_do_5 + tmp_do_6 ;
 if ( Randomization>0.0 )
 {
   tmp_do_7 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_7 = 0.0;
 }
 g_104_do_258 = tmp_do_7 + 400.0 ;
 if ( Randomization>0.0 )
 {
   tmp_do_8 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_8 = 0.0;
 }
 g_105_do_260 = tmp_do_8 + 5000.0 ;
 g_106_do_268 = 0.1 ;
 g_107_do_270 = 0.0 ;
 if ( Randomization>0.0 )
 {
   tmp_do_9 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_9 = 0.0;
 }
 g_109_do_280 = tmp_do_9 + 1900.0 ;
 tmp_do_9 = AdjustTrailTP + 250.0;
 if ( Randomization>0.0 )
 {
   tmp_do_10 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_10 = 0.0;
 }
 g_108_do_278 = tmp_do_9 + tmp_do_10 ;
 if ( Randomization>0.0 )
 {
   tmp_do_11 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_11 = 0.0;
 }
 g_113_do_2A8 = tmp_do_11 + 260.0 ;
 tmp_do_11 = AdjustBreakEven + 80.0;
 if ( Randomization>0.0 )
 {
   tmp_do_12 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_12 = 0.0;
 }
 g_114_do_2B0 = tmp_do_11 + tmp_do_12 ;
 g_117_in_2C8 = 60 ;
 g_118_in_2CC = 50 ;
 g_119_in_2D0 = 0 ;
 g_120_in_2D4 = 0 ;
 g_121_in_2D8 = 100 ;
 g_123_do_2E0 = 0.0 ;
 g_87_in_1CC = 99 ;
 if ( !(RemoveCommentSuffix) )
 {
   g_334_st_3120=ST1_Comment + "_B2";
 }
 g_93_in_1F0=ST1_MagicNumber + 12;
 g_397_do_6768 = lizong_35(281.0) ;
 if ( !(UseVariableValues) )   return;
 g_7_do_50 = 2600.0 ;
 g_397_do_6768 = lizong_35(110.0) ;
 }
//lizong_42 <<==--------   --------
 void lizong_43()
 {
 double     tmp_do_1;
 double     tmp_do_2;
 double     tmp_do_3;
 double     tmp_do_4;
 double     tmp_do_5;
 double     tmp_do_6;
 double     tmp_do_7;
 double     tmp_do_8;
 double     tmp_do_9;
 double     tmp_do_10;
 double     tmp_do_11;
 double     tmp_do_12;

 g_71_in_174 = 60 ;
 g_72_in_178 = 15 ;
 g_73_in_17C = 7 ;
 g_74_in_180 = 5 ;
 g_77_in_188 = 200 ;
 g_80_do_198 = 40.0 ;
 g_81_do_1A0 = 0.0 ;
 tmp_do_1 = AdjustEntry + -130.0;
 if ( Randomization>0.0 )
 {
   tmp_do_2 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_2 = 0.0;
 }
 g_83_do_1B0 = tmp_do_1 + tmp_do_2 ;
 tmp_do_2 = AdjustEntry + -125.0;
 if ( Randomization>0.0 )
 {
   tmp_do_3 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_3 = 0.0;
 }
 g_84_do_1B8 = tmp_do_2 + tmp_do_3 ;
 g_86_in_1C8 = 3 ;
 g_88_do_1D0 = 5.0 ;
 g_89_in_1D8 = 15 ;
 g_99_in_22C = 1 ;
 tmp_do_3 = AdjustSL + 3900.0;
 if ( Randomization>0.0 )
 {
   tmp_do_4 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_4 = 0.0;
 }
 g_100_do_230 = tmp_do_3 + tmp_do_4 ;
 tmp_do_4 = AdjustTP + 1485.0;
 if ( Randomization>0.0 )
 {
   tmp_do_5 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_5 = 0.0;
 }
 g_101_do_238 = tmp_do_4 + tmp_do_5 ;
 tmp_do_5 = AdjustTrailSL + 445.0;
 if ( Randomization>0.0 )
 {
   tmp_do_6 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_6 = 0.0;
 }
 g_103_do_250 = tmp_do_5 + tmp_do_6 ;
 if ( Randomization>0.0 )
 {
   tmp_do_7 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_7 = 0.0;
 }
 g_104_do_258 = tmp_do_7 + 355.0 ;
 if ( Randomization>0.0 )
 {
   tmp_do_8 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_8 = 0.0;
 }
 g_105_do_260 = tmp_do_8 + 5000.0 ;
 g_106_do_268 = 0.1 ;
 g_107_do_270 = 0.0 ;
 if ( Randomization>0.0 )
 {
   tmp_do_9 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_9 = 0.0;
 }
 g_109_do_280 = tmp_do_9 + 1850.0 ;
 tmp_do_9 = AdjustTrailTP + 250.0;
 if ( Randomization>0.0 )
 {
   tmp_do_10 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_10 = 0.0;
 }
 g_108_do_278 = tmp_do_9 + tmp_do_10 ;
 if ( Randomization>0.0 )
 {
   tmp_do_11 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_11 = 0.0;
 }
 g_113_do_2A8 = tmp_do_11 + 160.0 ;
 tmp_do_11 = AdjustBreakEven + 50.0;
 if ( Randomization>0.0 )
 {
   tmp_do_12 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_12 = 0.0;
 }
 g_114_do_2B0 = tmp_do_11 + tmp_do_12 ;
 g_117_in_2C8 = 60 ;
 g_118_in_2CC = 50 ;
 g_119_in_2D0 = 1 ;
 g_120_in_2D4 = 9 ;
 g_121_in_2D8 = 1500 ;
 g_123_do_2E0 = 46.0 ;
 g_87_in_1CC = 99 ;
 if ( !(RemoveCommentSuffix) )
 {
   g_334_st_3120=ST1_Comment + "_B1";
 }
 g_93_in_1F0=ST1_MagicNumber + 13;
 g_397_do_6768 = lizong_35(968.0) ;
 if ( !(UseVariableValues) )   return;
 g_7_do_50 = 1900.0 ;
 g_397_do_6768 = lizong_35(700.0) ;
 }
//lizong_43 <<==--------   --------
 void lizong_44()
 {
 double     tmp_do_1;
 double     tmp_do_2;
 double     tmp_do_3;
 double     tmp_do_4;
 double     tmp_do_5;
 double     tmp_do_6;
 double     tmp_do_7;
 double     tmp_do_8;
 double     tmp_do_9;
 double     tmp_do_10;
 double     tmp_do_11;
 double     tmp_do_12;

 g_71_in_174 = 60 ;
 g_72_in_178 = 15 ;
 g_73_in_17C = 25 ;
 g_74_in_180 = 23 ;
 g_77_in_188 = 145 ;
 g_80_do_198 = 10.0 ;
 g_81_do_1A0 = 0.0 ;
 tmp_do_1 = AdjustEntry + -55.0;
 if ( Randomization>0.0 )
 {
   tmp_do_2 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_2 = 0.0;
 }
 g_83_do_1B0 = tmp_do_1 + tmp_do_2 ;
 tmp_do_2 = AdjustEntry + -140.0;
 if ( Randomization>0.0 )
 {
   tmp_do_3 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_3 = 0.0;
 }
 g_84_do_1B8 = tmp_do_2 + tmp_do_3 ;
 g_86_in_1C8 = 5 ;
 g_88_do_1D0 = 90.0 ;
 g_89_in_1D8 = 60 ;
 g_99_in_22C = 1 ;
 tmp_do_3 = AdjustSL + 2250.0;
 if ( Randomization>0.0 )
 {
   tmp_do_4 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_4 = 0.0;
 }
 g_100_do_230 = tmp_do_3 + tmp_do_4 ;
 tmp_do_4 = AdjustTP + 1522.5;
 if ( Randomization>0.0 )
 {
   tmp_do_5 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_5 = 0.0;
 }
 g_101_do_238 = tmp_do_4 + tmp_do_5 ;
 tmp_do_5 = AdjustTrailSL + 450.0;
 if ( Randomization>0.0 )
 {
   tmp_do_6 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_6 = 0.0;
 }
 g_103_do_250 = tmp_do_5 + tmp_do_6 ;
 if ( Randomization>0.0 )
 {
   tmp_do_7 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_7 = 0.0;
 }
 g_104_do_258 = tmp_do_7 + 900.0 ;
 if ( Randomization>0.0 )
 {
   tmp_do_8 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_8 = 0.0;
 }
 g_105_do_260 = tmp_do_8 + 5000.0 ;
 g_106_do_268 = 0.1 ;
 g_107_do_270 = 0.0 ;
 if ( Randomization>0.0 )
 {
   tmp_do_9 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_9 = 0.0;
 }
 g_109_do_280 = tmp_do_9 + 2800.0 ;
 tmp_do_9 = AdjustTrailTP + 350.0;
 if ( Randomization>0.0 )
 {
   tmp_do_10 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_10 = 0.0;
 }
 g_108_do_278 = tmp_do_9 + tmp_do_10 ;
 if ( Randomization>0.0 )
 {
   tmp_do_11 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_11 = 0.0;
 }
 g_113_do_2A8 = tmp_do_11 + 340.0 ;
 tmp_do_11 = AdjustBreakEven + 30.0;
 if ( Randomization>0.0 )
 {
   tmp_do_12 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_12 = 0.0;
 }
 g_114_do_2B0 = tmp_do_11 + tmp_do_12 ;
 g_117_in_2C8 = 60 ;
 g_118_in_2CC = 50 ;
 g_119_in_2D0 = 12 ;
 g_120_in_2D4 = 17 ;
 g_121_in_2D8 = 1000 ;
 g_123_do_2E0 = 45.0 ;
 g_87_in_1CC = 5 ;
 if ( !(RemoveCommentSuffix) )
 {
   g_334_st_3120=ST1_Comment + "_A2";
 }
 g_93_in_1F0=ST1_MagicNumber + 14;
 g_397_do_6768 = lizong_35(149.0) ;
 if ( !(UseVariableValues) )   return;
 g_7_do_50 = 2600.0 ;
 g_397_do_6768 = lizong_35(90.0) ;
 }
//lizong_44 <<==--------   --------
 void lizong_45()
 {
 double     tmp_do_1;
 double     tmp_do_2;
 double     tmp_do_3;
 double     tmp_do_4;
 double     tmp_do_5;
 double     tmp_do_6;
 double     tmp_do_7;
 double     tmp_do_8;
 double     tmp_do_9;
 double     tmp_do_10;
 double     tmp_do_11;
 double     tmp_do_12;

 g_71_in_174 = 60 ;
 g_72_in_178 = 15 ;
 g_73_in_17C = 26 ;
 g_74_in_180 = 20 ;
 g_77_in_188 = 235 ;
 g_80_do_198 = 80.0 ;
 g_81_do_1A0 = 0.0 ;
 tmp_do_1 = AdjustEntry + -140.0;
 if ( Randomization>0.0 )
 {
   tmp_do_2 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_2 = 0.0;
 }
 g_83_do_1B0 = tmp_do_1 + tmp_do_2 ;
 tmp_do_2 = AdjustEntry + -170.0;
 if ( Randomization>0.0 )
 {
   tmp_do_3 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_3 = 0.0;
 }
 g_84_do_1B8 = tmp_do_2 + tmp_do_3 ;
 g_86_in_1C8 = 5 ;
 g_88_do_1D0 = 5.0 ;
 g_89_in_1D8 = 55 ;
 g_99_in_22C = 1 ;
 tmp_do_3 = AdjustSL + 1900.0;
 if ( Randomization>0.0 )
 {
   tmp_do_4 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_4 = 0.0;
 }
 g_100_do_230 = tmp_do_3 + tmp_do_4 ;
 tmp_do_4 = AdjustTP + 1284.0;
 if ( Randomization>0.0 )
 {
   tmp_do_5 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_5 = 0.0;
 }
 g_101_do_238 = tmp_do_4 + tmp_do_5 ;
 tmp_do_5 = AdjustTrailSL + 1250.0;
 if ( Randomization>0.0 )
 {
   tmp_do_6 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_6 = 0.0;
 }
 g_103_do_250 = tmp_do_5 + tmp_do_6 ;
 if ( Randomization>0.0 )
 {
   tmp_do_7 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_7 = 0.0;
 }
 g_104_do_258 = tmp_do_7 + 650.0 ;
 if ( Randomization>0.0 )
 {
   tmp_do_8 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_8 = 0.0;
 }
 g_105_do_260 = tmp_do_8 + 5000.0 ;
 g_106_do_268 = 0.1 ;
 g_107_do_270 = 0.0 ;
 if ( Randomization>0.0 )
 {
   tmp_do_9 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_9 = 0.0;
 }
 g_109_do_280 = tmp_do_9 + 1950.0 ;
 tmp_do_9 = AdjustTrailTP + 250.0;
 if ( Randomization>0.0 )
 {
   tmp_do_10 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_10 = 0.0;
 }
 g_108_do_278 = tmp_do_9 + tmp_do_10 ;
 if ( Randomization>0.0 )
 {
   tmp_do_11 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_11 = 0.0;
 }
 g_113_do_2A8 = tmp_do_11 + 270.0 ;
 tmp_do_11 = AdjustBreakEven;
 if ( Randomization>0.0 )
 {
   tmp_do_12 = Randomization * 2.0 * MathRand() / 32768.0 + (0.0 - Randomization);
 }
 else
 {
   tmp_do_12 = 0.0;
 }
 g_114_do_2B0 = tmp_do_11 + tmp_do_12 ;
 g_117_in_2C8 = 60 ;
 g_118_in_2CC = 50 ;
 g_119_in_2D0 = 15 ;
 g_120_in_2D4 = 3 ;
 g_121_in_2D8 = 1200 ;
 g_123_do_2E0 = 16.0 ;
 g_87_in_1CC = 20 ;
 if ( !(RemoveCommentSuffix) )
 {
   g_334_st_3120=ST1_Comment + "_A3";
 }
 g_93_in_1F0=ST1_MagicNumber + 15;
 g_397_do_6768 = lizong_35(276.0) ;
 if ( !(UseVariableValues) )   return;
 g_7_do_50 = 2800.0 ;
 g_397_do_6768 = lizong_35(130.0) ;
 }
//lizong_45 <<==--------   --------
 void lizong_46()
 {
  double    local_1_do;
  int       local_2_in;
  double    local_3_do;
  double    local_4_do;
  double    local_5_do;
//----- -----
 double     tmp_do_1;
 long       tmp_lo_2;
 int        tmp_in_3;
 int        tmp_in_4;
 int        tmp_in_5;
 int        tmp_in_6;
 int        tmp_in_7;
 int        tmp_in_8;
 int        tmp_in_9;
 int        tmp_in_10;
 int        tmp_in_11;
 int        tmp_in_12;
 int        tmp_in_13;
 int        tmp_in_14;
 int        tmp_in_15;
 int        tmp_in_16;
 int        tmp_in_17;
 int        tmp_in_18;
 int        tmp_in_19;

 tmp_do_1 = AccountEquity();
 if ( tmp_do_1==AccountBalance() )   return;
 local_1_do = 0.0 ;
 if ( AccountEquity()>g_384_do_5DA0 )
 {
   g_384_do_5DA0 = AccountEquity() ;
 }
 for (local_2_in = HistoryTotal() ; local_2_in >= 0 ; local_2_in --)
 {
   if ( OrderSelect(local_2_in,0,1) != true )   continue;
   tmp_lo_2 = OrderCloseTime();
   if ( tmp_lo_2 < iTime(g_336_st_3130,PERIOD_D1,0) )   continue;
   local_3_do = OrderProfit() + OrderSwap() + OrderCommission() ;
   local_1_do = local_3_do + local_1_do ;
   
 }
 local_4_do = AccountEquity() - AccountBalance() ;
 local_5_do = local_4_do + local_1_do ;
 if ( !( -(local_5_do)>g_384_do_5DA0 * PropFirmMaxDailyDD / 100.0) )   return;
 
 if ( !(g_382_bo_5D98) )
 {
   Print("Max Daily Drawdown reached, closing trades and skipping rest of the day"); 
 }
 for (tmp_in_3 = OrdersTotal() ; tmp_in_3 >= 0 ; tmp_in_3=tmp_in_3 - 1)
 {
   if ( OrderSelect(tmp_in_3,0,0) != true || OrderSymbol() != g_336_st_3130 )   continue;
   tmp_in_4 = OrderMagicNumber();
   tmp_in_5=ST1_MagicNumber + 1;
   if ( tmp_in_4 != tmp_in_5 )
   {
     tmp_in_5 = OrderMagicNumber();
     tmp_in_6=ST1_MagicNumber + 2;
     if ( tmp_in_5 != tmp_in_6 )
     {
       tmp_in_6 = OrderMagicNumber();
       tmp_in_7=ST1_MagicNumber + 3;
       if ( tmp_in_6 != tmp_in_7 )
       {
         tmp_in_7 = OrderMagicNumber();
         tmp_in_8=ST1_MagicNumber + 4;
         if ( tmp_in_7 != tmp_in_8 )
         {
           tmp_in_8 = OrderMagicNumber();
           tmp_in_9=ST1_MagicNumber + 5;
           if ( tmp_in_8 != tmp_in_9 )
           {
             tmp_in_9 = OrderMagicNumber();
             tmp_in_10=ST1_MagicNumber + 6;
             if ( tmp_in_9 != tmp_in_10 )
             {
               tmp_in_10 = OrderMagicNumber();
               tmp_in_11=ST1_MagicNumber + 7;
               if ( tmp_in_10 != tmp_in_11 )
               {
                 tmp_in_11 = OrderMagicNumber();
                 tmp_in_12=ST1_MagicNumber + 8;
                 if ( tmp_in_11 != tmp_in_12 )
                 {
                   tmp_in_12 = OrderMagicNumber();
                   tmp_in_13=ST1_MagicNumber + 9;
                   if ( tmp_in_12 != tmp_in_13 )
                   {
                     tmp_in_13 = OrderMagicNumber();
                     tmp_in_14=ST1_MagicNumber + 10;
                     if ( tmp_in_13 != tmp_in_14 )
                     {
                       tmp_in_14 = OrderMagicNumber();
                       tmp_in_15=ST1_MagicNumber + 11;
                       if ( tmp_in_14 != tmp_in_15 )
                       {
                         tmp_in_15 = OrderMagicNumber();
                         tmp_in_16=ST1_MagicNumber + 12;
                         if ( tmp_in_15 != tmp_in_16 )
                         {
                           tmp_in_16 = OrderMagicNumber();
                           tmp_in_17=ST1_MagicNumber + 13;
                           if ( tmp_in_16 != tmp_in_17 )
                           {
                             tmp_in_17 = OrderMagicNumber();
                             tmp_in_18=ST1_MagicNumber + 14;
                             if ( tmp_in_17 != tmp_in_18 )
                             {
                               tmp_in_18 = OrderMagicNumber();
                               tmp_in_19=ST1_MagicNumber + 15;
                             if ( tmp_in_18 != tmp_in_19 )   continue;
                             }
                           }
                         }
                       }
                     }
                   }
                 }
               }
             }
           }
         }
       }
     }
   }
   if ( OrderType() == 0 )
   {
     OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_BID),g_38_do_C0,Red); 
   }
   if ( OrderType() == 1 )
   {
     OrderClose(OrderTicket(),OrderLots(),MarketInfo(g_336_st_3130,MODE_ASK),g_38_do_C0,Red); 
   }
   if ( ( OrderType() != 4 && OrderType() != 5 ) )   continue;
   OrderDelete(OrderTicket(),Red); 
   
 }
 g_382_bo_5D98 = true ;
 g_384_do_5DA0 = 0.0 ;
 }
//lizong_46 <<==--------   --------
 int lizong_47()
 {
  string    local_2_st;
  int       local_3_in;
  string    local_4_st;
  long      local_5_lo;
  int       local_6_in;
  char      local_7_ch_ko[];
  char      local_8_ch_ko[];
//----- -----
 string     tmp_st_1;
 string     tmp_st_2;

 ResetLastError();
 if ( WebRequest("GET","https://www.worldtimeserver.com/time-zones/utc/",NULL,NULL,10000,local_7_ch_ko,0,local_8_ch_ko,tmp_st_1) == -1 )
 {
   Print("Error when reading GMT URL. Error code  =",GetLastError()); 
   MessageBox("Add the address \'https://www.worldtimeserver.com/\' in the list of allowed URLs on tab \'Expert Advisors\'","Error",64); 
   tmp_st_2 = "999";
 }
 else
 {
   tmp_st_2 = CharArrayToString(local_8_ch_ko,0,0,0);
 }
 local_2_st = tmp_st_2 ;
 if ( local_2_st == "999" )
 {
   return(999); 
 }
 local_3_in = StringFind(local_2_st,"\"serverTimeStamp\" value=",0) ;
 local_4_st = StringSubstr(local_2_st,local_3_in + 25,10) ;
 local_5_lo = (long)ulong(local_4_st) ;
 Print("GMT time = ",local_5_lo); 
 Print("Broker time = ",TimeCurrent()); 
 local_6_in=TimeHour(TimeCurrent()) - TimeHour(local_5_lo);
 if ( local_6_in <  -12 )
 {
   local_6_in +=24;
 }
 if ( local_6_in >  12 )
 {
   local_6_in -=24;
 }
 Print("GMT_Offset detected: " + string(local_6_in)); 
 if ( ( local_6_in < -12 || local_6_in >  12 ) )
 {
   Print("Error in detecting GMT offset with URL"); 
   return(999); 
 }
 if ( local_5_lo <  TimeCurrent() - 0x15180 )
 {
   Print("Error in detecting GMT time with URL"); 
   return(999); 
 }
 return(local_6_in); 
 }
//lizong_47 <<==--------   --------
 bool lizong_48()
 {
  int       local_2_in;
  datetime  local_3_da;
  datetime  local_4_da;
  int       local_5_in;
  int       local_6_in;
//----- -----

 local_2_in = TimeYear(TimeCurrent()) ;
 local_3_da = 0 ;
 local_4_da = 0 ;
 if ( local_2_in <  1987 )
 {
   Print("AmericanDST(): Invalid year."); 
   return(false); 
 }
 local_5_in = 0 ;
 local_6_in = 0 ;
 if ( local_2_in >= 1987 && local_2_in <= 2006 )
 {
   local_5_in = (int)(MathMod(local_2_in * 6 + 2 - local_2_in / 4,7.0) + 1.0) ;
   local_6_in = (int)(31.0 - (MathMod(local_2_in * 5 / 4 + 1,7.0))) ;
   local_3_da=StringToTime(StringConcatenate(local_2_in,".04.01")) + (local_5_in - 1) * 86400 + 0x1C20;
   local_4_da=StringToTime(StringConcatenate(local_2_in,".10.01")) + (local_6_in - 1) * 86400 + 0x1C20;
 }
 else
 {
   if ( local_2_in >= 2007 )
   {
     local_5_in = (int)(14.0 - (MathMod(local_2_in * 5 / 4 + 1,7.0))) ;
     local_6_in = (int)(7.0 - (MathMod(local_2_in * 5 / 4 + 1,7.0))) ;
     local_3_da=StringToTime(StringConcatenate(local_2_in,".03.01")) + (local_5_in - 1) * 86400 + 0x1C20;
     local_4_da=StringToTime(StringConcatenate(local_2_in,".11.01")) + (local_6_in - 1) * 86400 + 0x1C20;
   }
 }
 if ( TimeDayOfYear(TimeCurrent()) >  TimeDayOfYear(local_3_da) && TimeDayOfYear(TimeCurrent()) <  TimeDayOfYear(local_4_da) )
 {
   return(true); 
 }
 return(false); 
 }
//<<==lizong_48 <<==



//==================== Visuals ====================
datetime g_reaperLastVisualUpdate=0;
datetime g_reaperLastStatScan=0;

struct ReaperStrategyStats
{
   int wins;
   int losses;
   double grossProfit;
   double grossLoss;
};

ReaperStrategyStats g_reaperStats[6];

#define REAPER_X           6
#define REAPER_Y          20
#define REAPER_W         315
#define REAPER_HDR_H      42
#define REAPER_IMG_H       0
#define REAPER_INFO_H    206
#define REAPER_COL_H      18
#define REAPER_ROW_H      16
#define REAPER_SUM_H       0
#define REAPER_FOOT_H      0
#define REAPER_CORNER  CORNER_LEFT_UPPER
#define FC_BG0        C'12,16,22'
#define FC_BG1        C'18,22,30'
#define FC_BG2        C'24,29,39'
#define FC_BG3        C'20,25,34'
#define FC_HDR        C'11,38,29'
#define FC_HDR2       C'18,22,30'
#define FC_COLHDR     C'18,22,30'
#define FC_BORDER     C'32,40,51'
#define FC_ACCENT     C'0,232,151'
#define FC_GOLD       C'255,117,55'
#define FC_WHITE      C'225,232,241'
#define FC_GREY       C'121,138,157'
#define FC_DIM        C'75,91,108'
#define FC_WIN        C'0,238,166'
#define FC_LOSS       C'255,78,59'
#define FC_WARN       C'255,126,55'

string REAPER_STRATEGY_NAMES[6] =
{
   "A1", "A2", "A3", "B1", "B2", "B3"
};

int ReaperDashboardHeight()
{
   return 365;
}

void ReaperDeletePrefix(string prefix)
{
   for(int i=ObjectsTotal(0)-1;i>=0;i--)
   {
      string name=ObjectName(0,i);
      if(StringFind(name,prefix)==0) ObjectDelete(0,name);
   }
}

void ReaperLabel(string name,string text,int x,int y,int fontSize,color clr,string font="Arial Bold",uint anchor=ANCHOR_LEFT_UPPER)
{
   if(ObjectFind(0,name)<0)
   {
      ObjectCreate(0,name,OBJ_LABEL,0,0,0);
      ObjectSetInteger(0,name,OBJPROP_CORNER,REAPER_CORNER);
      ObjectSetInteger(0,name,OBJPROP_SELECTABLE,false);
      ObjectSetInteger(0,name,OBJPROP_HIDDEN,true);
      ObjectSetInteger(0,name,OBJPROP_ZORDER,30);
   }
   ObjectSetInteger(0,name,OBJPROP_XDISTANCE,x);
   ObjectSetInteger(0,name,OBJPROP_YDISTANCE,y);
   ObjectSetString(0,name,OBJPROP_TEXT,text);
   ObjectSetString(0,name,OBJPROP_FONT,font);
   ObjectSetInteger(0,name,OBJPROP_FONTSIZE,fontSize);
   ObjectSetInteger(0,name,OBJPROP_COLOR,clr);
   ObjectSetInteger(0,name,OBJPROP_ANCHOR,anchor);
}

void ReaperRect(string name,int x,int y,int w,int h,color bg,color border=clrNONE)
{
   if(ObjectFind(0,name)<0)
   {
      ObjectCreate(0,name,OBJ_RECTANGLE_LABEL,0,0,0);
      ObjectSetInteger(0,name,OBJPROP_CORNER,REAPER_CORNER);
      ObjectSetInteger(0,name,OBJPROP_SELECTABLE,false);
      ObjectSetInteger(0,name,OBJPROP_HIDDEN,true);
      ObjectSetInteger(0,name,OBJPROP_ZORDER,20);
   }
   ObjectSetInteger(0,name,OBJPROP_XDISTANCE,x);
   ObjectSetInteger(0,name,OBJPROP_YDISTANCE,y);
   ObjectSetInteger(0,name,OBJPROP_XSIZE,w);
   ObjectSetInteger(0,name,OBJPROP_YSIZE,h);
   ObjectSetInteger(0,name,OBJPROP_BGCOLOR,bg);
   if(border!=clrNONE)
   {
      ObjectSetInteger(0,name,OBJPROP_BORDER_TYPE,BORDER_FLAT);
      ObjectSetInteger(0,name,OBJPROP_COLOR,border);
      ObjectSetInteger(0,name,OBJPROP_WIDTH,1);
   }
}

void ReaperBitmap(string name,int x,int y,int w,int h)
{
   uint pixels[];
   uint imgW=0,imgH=0;
   string resPath="";

   if(ObjectFind(0,name)<0)
   {
      ObjectCreate(0,name,OBJ_BITMAP_LABEL,0,0,0);
      ObjectSetInteger(0,name,OBJPROP_CORNER,REAPER_CORNER);
      ObjectSetInteger(0,name,OBJPROP_SELECTABLE,false);
      ObjectSetInteger(0,name,OBJPROP_HIDDEN,true);
      ObjectSetInteger(0,name,OBJPROP_ZORDER,25);
   }
   ObjectSetInteger(0,name,OBJPROP_XDISTANCE,x);
   ObjectSetInteger(0,name,OBJPROP_YDISTANCE,y);
   ObjectSetInteger(0,name,OBJPROP_XSIZE,w);
   ObjectSetInteger(0,name,OBJPROP_YSIZE,h);

   if(ResourceReadImage(resPath,pixels,imgW,imgH))
   {
      uint scaled[];
      ArrayResize(scaled,w*h);
      for(int py=0;py<h;py++)
      {
         for(int px=0;px<w;px++)
         {
            int srcX=(int)((double)px*imgW/w);
            int srcY=(int)((double)py*imgH/h);
            if(srcX>=(int)imgW) srcX=(int)imgW-1;
            if(srcY>=(int)imgH) srcY=(int)imgH-1;
            scaled[py*w+px]=pixels[srcY*(int)imgW+srcX];
         }
      }
      string dynRes="::reaper_dash_img";
      ResourceCreate(dynRes,scaled,w,h,0,0,w,COLOR_FORMAT_XRGB_NOALPHA);
      ObjectSetString(0,name,OBJPROP_BMPFILE,dynRes);
   }
}

void ReaperSetLabel(string name,string text,color clr)
{
   ObjectSetString(0,name,OBJPROP_TEXT,text);
   ObjectSetInteger(0,name,OBJPROP_COLOR,clr);
}

bool ReaperStrategyEnabled(int idx)
{
   if(idx==0) return InpZoneA1;
   if(idx==1) return InpZoneA2;
   if(idx==2) return InpZoneA3;
   if(idx==3) return InpZoneB1;
   if(idx==4) return InpZoneB2;
   if(idx==5) return InpZoneB3;
   return false;
}

int ReaperMagicIndex(long magic)
{
   const long suffix=magic-ST1_MagicNumber;
   if(suffix==9)  return 0;
   if(suffix==14) return 1;
   if(suffix==15) return 2;
   if(suffix==13) return 3;
   if(suffix==12) return 4;
   if(suffix==8)  return 5;
   return -1;
}

int ReaperCountOrders(bool positions)
{
   int count=0;
   int total=OrdersTotal();
   for(int i=0;i<total;i++)
   {
      if(!OrderSelect(i,SELECT_BY_POS,MODE_TRADES)) continue;
      if(OrderSymbol()!=Symbol()) continue;
      if(ReaperMagicIndex(OrderMagicNumber())<0) continue;
      int type=OrderType();
      if(positions && (type==OP_BUY || type==OP_SELL)) count++;
      if(!positions && type>=OP_BUYLIMIT && type<=OP_SELLSTOP) count++;
   }
   return count;
}

void ReaperApplyChartTheme()
{
   if(!STYLE_NATIVE_CANDLES) return;

   color bg=C'6,10,16';
   color axis=C'218,229,242';
   color grid=C'20,29,40';
   color muted=C'60,78,100';
   color bull=C'0,188,245';
   color bear=C'255,104,72';

   ChartSetInteger(0,CHART_MODE,CHART_CANDLES);
   ChartSetInteger(0,CHART_SHOW_GRID,false);
   ChartSetInteger(0,CHART_COLOR_BACKGROUND,bg);
   ChartSetInteger(0,CHART_COLOR_FOREGROUND,axis);
   ChartSetInteger(0,CHART_COLOR_GRID,grid);
   ChartSetInteger(0,CHART_COLOR_VOLUME,muted);

   ChartSetInteger(0,CHART_COLOR_CHART_UP,bull);
   ChartSetInteger(0,CHART_COLOR_CHART_DOWN,bear);
   ChartSetInteger(0,CHART_COLOR_CANDLE_BULL,bull);
   ChartSetInteger(0,CHART_COLOR_CANDLE_BEAR,bear);
   ChartSetInteger(0,CHART_COLOR_CHART_LINE,bull);
}

void ReaperDrawStyledCandles()
{
   int requested=MathMax(10,STYLED_CANDLES_COUNT);
   long visibleBars=ChartGetInteger(0,CHART_VISIBLE_BARS,0);
   if(visibleBars>requested) requested=(int)MathMin(visibleBars+10,180);
   int n=MathMax(10,MathMin(requested,180));

   MqlRates rates[];
   ArraySetAsSeries(rates,true);
   int copied=CopyRates(_Symbol,_Period,0,n,rates);
   if(copied<=0) return;
   int sec=PeriodSeconds(_Period);
   if(sec<=0) sec=60;

   for(int i=0;i<copied;i++)
   {
      string base="REAPER_CANDLE_"+IntegerToString(i);
      datetime t1=rates[i].time+(datetime)(sec*0.18);
      datetime t2=rates[i].time+(datetime)(sec*0.82);
      datetime s1=rates[i].time+(datetime)(sec*0.15);
      datetime s2=rates[i].time+(datetime)(sec*0.85);
      double top=MathMax(rates[i].open,rates[i].close);
      double bot=MathMin(rates[i].open,rates[i].close);
      double bodyRange=MathAbs(top-bot);
      if(bodyRange<_Point*3.0)
      {
         top+=_Point*1.5;
         bot-=_Point*1.5;
         bodyRange=top-bot;
      }
      double pad=MathMax(_Point*2.0,bodyRange*0.10);
      bool bull=(rates[i].close>=rates[i].open);
      color body=(bull?C'0,188,245':C'255,104,72');
      color border=(bull?C'97,228,255':C'255,160,128');
      color wick=(bull?C'0,204,255':C'255,120,82');
      color shadow=C'0,1,4';
      color highlight=(bull?C'125,235,255':C'255,185,155');

      if(ObjectFind(0,base+"_BODY_SHADOW")<0) ObjectCreate(0,base+"_BODY_SHADOW",OBJ_RECTANGLE,0,s1,top+pad,s2,bot-pad);
      ObjectMove(0,base+"_BODY_SHADOW",0,s1,top+pad);
      ObjectMove(0,base+"_BODY_SHADOW",1,s2,bot-pad);
      ObjectSetInteger(0,base+"_BODY_SHADOW",OBJPROP_COLOR,shadow);
      ObjectSetInteger(0,base+"_BODY_SHADOW",OBJPROP_BGCOLOR,shadow);
      ObjectSetInteger(0,base+"_BODY_SHADOW",OBJPROP_FILL,true);
      ObjectSetInteger(0,base+"_BODY_SHADOW",OBJPROP_BACK,false);
      ObjectSetInteger(0,base+"_BODY_SHADOW",OBJPROP_SELECTABLE,false);
      ObjectSetInteger(0,base+"_BODY_SHADOW",OBJPROP_HIDDEN,true);
      ObjectSetInteger(0,base+"_BODY_SHADOW",OBJPROP_ZORDER,25);

      datetime tc=rates[i].time+(datetime)(sec*0.50);
      if(ObjectFind(0,base+"_WICK_SHADOW")<0) ObjectCreate(0,base+"_WICK_SHADOW",OBJ_TREND,0,tc,rates[i].high,tc,rates[i].low);
      ObjectMove(0,base+"_WICK_SHADOW",0,tc,rates[i].high);
      ObjectMove(0,base+"_WICK_SHADOW",1,tc,rates[i].low);
      ObjectSetInteger(0,base+"_WICK_SHADOW",OBJPROP_COLOR,shadow);
      ObjectSetInteger(0,base+"_WICK_SHADOW",OBJPROP_WIDTH,5);
      ObjectSetInteger(0,base+"_WICK_SHADOW",OBJPROP_RAY_RIGHT,false);
      ObjectSetInteger(0,base+"_WICK_SHADOW",OBJPROP_RAY_LEFT,false);
      ObjectSetInteger(0,base+"_WICK_SHADOW",OBJPROP_SELECTABLE,false);
      ObjectSetInteger(0,base+"_WICK_SHADOW",OBJPROP_HIDDEN,true);
      ObjectSetInteger(0,base+"_WICK_SHADOW",OBJPROP_ZORDER,26);

      if(ObjectFind(0,base+"_WICK")<0) ObjectCreate(0,base+"_WICK",OBJ_TREND,0,tc,rates[i].high,tc,rates[i].low);
      ObjectMove(0,base+"_WICK",0,tc,rates[i].high);
      ObjectMove(0,base+"_WICK",1,tc,rates[i].low);
      ObjectSetInteger(0,base+"_WICK",OBJPROP_COLOR,wick);
      ObjectSetInteger(0,base+"_WICK",OBJPROP_WIDTH,2);
      ObjectSetInteger(0,base+"_WICK",OBJPROP_RAY_RIGHT,false);
      ObjectSetInteger(0,base+"_WICK",OBJPROP_RAY_LEFT,false);
      ObjectSetInteger(0,base+"_WICK",OBJPROP_SELECTABLE,false);
      ObjectSetInteger(0,base+"_WICK",OBJPROP_HIDDEN,true);
      ObjectSetInteger(0,base+"_WICK",OBJPROP_ZORDER,27);

      if(ObjectFind(0,base+"_BODY")<0) ObjectCreate(0,base+"_BODY",OBJ_RECTANGLE,0,t1,top,t2,bot);
      ObjectMove(0,base+"_BODY",0,t1,top);
      ObjectMove(0,base+"_BODY",1,t2,bot);
      ObjectSetInteger(0,base+"_BODY",OBJPROP_COLOR,border);
      ObjectSetInteger(0,base+"_BODY",OBJPROP_BGCOLOR,body);
      ObjectSetInteger(0,base+"_BODY",OBJPROP_FILL,true);
      ObjectSetInteger(0,base+"_BODY",OBJPROP_BACK,false);
      ObjectSetInteger(0,base+"_BODY",OBJPROP_WIDTH,2);
      ObjectSetInteger(0,base+"_BODY",OBJPROP_SELECTABLE,false);
      ObjectSetInteger(0,base+"_BODY",OBJPROP_HIDDEN,true);
      ObjectSetInteger(0,base+"_BODY",OBJPROP_ZORDER,28);

      datetime ht=rates[i].time+(datetime)(sec*0.32);
      if(ObjectFind(0,base+"_HIGHLIGHT")<0) ObjectCreate(0,base+"_HIGHLIGHT",OBJ_TREND,0,ht,top-pad*0.35,ht,bot+pad*0.35);
      ObjectMove(0,base+"_HIGHLIGHT",0,ht,top-pad*0.35);
      ObjectMove(0,base+"_HIGHLIGHT",1,ht,bot+pad*0.35);
      ObjectSetInteger(0,base+"_HIGHLIGHT",OBJPROP_COLOR,highlight);
      ObjectSetInteger(0,base+"_HIGHLIGHT",OBJPROP_WIDTH,1);
      ObjectSetInteger(0,base+"_HIGHLIGHT",OBJPROP_RAY_RIGHT,false);
      ObjectSetInteger(0,base+"_HIGHLIGHT",OBJPROP_RAY_LEFT,false);
      ObjectSetInteger(0,base+"_HIGHLIGHT",OBJPROP_SELECTABLE,false);
      ObjectSetInteger(0,base+"_HIGHLIGHT",OBJPROP_HIDDEN,true);
      ObjectSetInteger(0,base+"_HIGHLIGHT",OBJPROP_ZORDER,29);
   }
}

void ReaperCreateDashboard()
{
   const int x=REAPER_X;
   const int y=REAPER_Y;
   ReaperRect("REAPER_DASH_BG",x,y,REAPER_W,ReaperDashboardHeight(),FC_BG0,FC_BORDER);
   ReaperRect("REAPER_DASH_HDR",x,y,REAPER_W,REAPER_HDR_H,FC_HDR);
   ReaperRect("REAPER_DASH_ACCENT",x,y,3,REAPER_HDR_H,FC_ACCENT);
   ReaperLabel("REAPER_DASH_TITLE","LIZARD",x+14,y+7,11,FC_ACCENT,"Consolas");
   ReaperLabel("REAPER_DASH_VERSION","v1.85",x+84,y+10,7,FC_DIM,"Consolas");
   ReaperLabel("REAPER_DASH_SUB","NEOBull  -  "+Symbol(),x+14,y+27,6,FC_GREY,"Consolas");
   ReaperRect("REAPER_DASH_BADGE",x+239,y+8,65,22,C'14,55,39');
   ReaperLabel("REAPER_DASH_BADGE_TXT","NORMAL",x+271,y+13,7,FC_ACCENT,"Consolas",ANCHOR_CENTER);

   ReaperRect("REAPER_TILE_RISK",x+10,y+48,143,34,FC_BG2);
   ReaperRect("REAPER_TILE_SL",x+159,y+48,146,34,FC_BG2);
   ReaperRect("REAPER_TILE_TRAIL",x+10,y+87,143,34,FC_BG2);
   ReaperRect("REAPER_TILE_FREQ",x+159,y+87,146,34,FC_BG2);
   ReaperLabel("REAPER_RISK_L","RISK",x+19,y+53,5,FC_DIM,"Consolas");
   ReaperLabel("REAPER_RISK_V","---",x+19,y+67,9,FC_WHITE,"Consolas");
   ReaperLabel("REAPER_SL_L","SL",x+168,y+53,5,FC_DIM,"Consolas");
   ReaperLabel("REAPER_SL_V","---",x+168,y+67,9,FC_WHITE,"Consolas");
   ReaperLabel("REAPER_TRAIL_L","TRAIL",x+19,y+92,5,FC_DIM,"Consolas");
   ReaperLabel("REAPER_TRAIL_V","---",x+19,y+106,8,FC_WHITE,"Consolas");
   ReaperLabel("REAPER_FREQ_L","FREQ",x+168,y+92,5,FC_DIM,"Consolas");
   ReaperLabel("REAPER_FREQ_V","---",x+168,y+106,8,FC_WHITE,"Consolas");

   ReaperLabel("REAPER_OPEN_L","Open P/L",x+12,y+132,7,FC_GREY,"Consolas");
   ReaperLabel("REAPER_OPEN_V","0.00",x+300,y+132,7,FC_WHITE,"Consolas",ANCHOR_RIGHT_UPPER);
   ReaperLabel("REAPER_BAL_L","Balance",x+12,y+149,7,FC_GREY,"Consolas");
   ReaperLabel("REAPER_BAL_V","0.00",x+300,y+149,7,FC_WHITE,"Consolas",ANCHOR_RIGHT_UPPER);
   ReaperRect("REAPER_DASH_DIV1",x+10,y+168,295,1,FC_BORDER);
   ReaperLabel("REAPER_TOTAL_L","Total",x+12,y+178,7,FC_GREY,"Consolas");
   ReaperLabel("REAPER_TOTAL_V","0.00",x+300,y+178,7,FC_WIN,"Consolas",ANCHOR_RIGHT_UPPER);
   ReaperLabel("REAPER_MONTH_L","Monthly",x+12,y+195,7,FC_GREY,"Consolas");
   ReaperLabel("REAPER_MONTH_V","0.00",x+300,y+195,7,FC_WIN,"Consolas",ANCHOR_RIGHT_UPPER);
   ReaperLabel("REAPER_WEEK_L","Weekly",x+12,y+212,7,FC_GREY,"Consolas");
   ReaperLabel("REAPER_WEEK_V","0.00",x+300,y+212,7,FC_WHITE,"Consolas",ANCHOR_RIGHT_UPPER);
   ReaperRect("REAPER_DASH_DIV2",x,y+231,REAPER_W,1,FC_WARN);

   ReaperLabel("REAPER_ZONES_L","ZONES",x+12,y+239,6,FC_DIM,"Consolas");
   ReaperLabel("REAPER_ZONES_V","6 active",x+300,y+239,6,FC_GREY,"Consolas",ANCHOR_RIGHT_UPPER);
   ReaperLabel("REAPER_CH_ZONE","Zone",x+12,y+258,6,FC_DIM,"Consolas");
   ReaperLabel("REAPER_CH_PL","P/L",x+150,y+258,6,FC_DIM,"Consolas",ANCHOR_RIGHT_UPPER);
   ReaperLabel("REAPER_CH_LOT","Lot",x+230,y+258,6,FC_DIM,"Consolas",ANCHOR_RIGHT_UPPER);

   for(int i=0;i<6;i++)
   {
      const int rowY=y+276+i*REAPER_ROW_H;
      ReaperLabel("REAPER_SNAME"+IntegerToString(i),REAPER_STRATEGY_NAMES[i],
                  x+12,rowY,7,FC_WHITE,"Consolas");
      ReaperLabel("REAPER_PL"+IntegerToString(i),"0.00",
                  x+184,rowY,7,FC_WHITE,"Consolas",ANCHOR_RIGHT_UPPER);
      ReaperLabel("REAPER_LOT"+IntegerToString(i),"0.00",
                  x+232,rowY,7,FC_WHITE,"Consolas",ANCHOR_RIGHT_UPPER);
      ReaperLabel("REAPER_EN"+IntegerToString(i),"*",
                  x+276,rowY-1,9,FC_WIN,"Consolas");
   }
}

void ReaperScanHistoryStats()
{
   datetime now=TimeCurrent();
   int scanEvery=MathMax(1,VISUAL_REFRESH_SECONDS);
   if(g_reaperLastStatScan!=0 && (now-g_reaperLastStatScan)<scanEvery) return;
   g_reaperLastStatScan=now;

   for(int i=0;i<6;i++)
   {
      g_reaperStats[i].wins=0;
      g_reaperStats[i].losses=0;
      g_reaperStats[i].grossProfit=0.0;
      g_reaperStats[i].grossLoss=0.0;
   }

   if(!HistorySelect(0,now)) return;
   int total=HistoryDealsTotal();
   for(int d=0;d<total;d++)
   {
      ulong ticket=HistoryDealGetTicket(d);
      if(ticket==0) continue;
      if(HistoryDealGetString(ticket,DEAL_SYMBOL)!=Symbol()) continue;
      int idx=ReaperMagicIndex((long)HistoryDealGetInteger(ticket,DEAL_MAGIC));
      if(idx<0) continue;
      long entry=HistoryDealGetInteger(ticket,DEAL_ENTRY);
      if(entry!=DEAL_ENTRY_OUT && entry!=DEAL_ENTRY_INOUT) continue;
      double pnl=HistoryDealGetDouble(ticket,DEAL_PROFIT)+HistoryDealGetDouble(ticket,DEAL_SWAP)+HistoryDealGetDouble(ticket,DEAL_COMMISSION);
      if(pnl>=0.0)
      {
         g_reaperStats[idx].wins++;
         g_reaperStats[idx].grossProfit+=pnl;
      }
      else
      {
         g_reaperStats[idx].losses++;
         g_reaperStats[idx].grossLoss+=pnl;
      }
   }
}

double ReaperOpenProfit()
{
   double result=0.0;
   for(int i=PositionsTotal()-1;i>=0;i--)
   {
      const ulong ticket=PositionGetTicket(i);
      if(ticket==0 || !PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL)!=Symbol()) continue;
      if(ReaperMagicIndex(PositionGetInteger(POSITION_MAGIC))<0) continue;
      result+=PositionGetDouble(POSITION_PROFIT)+PositionGetDouble(POSITION_SWAP);
   }
   return result;
}

double ReaperHistoryProfit(const datetime from_time)
{
   const datetime now=TimeCurrent();
   if(!HistorySelect(from_time,now)) return 0.0;
   double result=0.0;
   const int total=HistoryDealsTotal();
   for(int i=0;i<total;i++)
   {
      const ulong ticket=HistoryDealGetTicket(i);
      if(ticket==0) continue;
      if(HistoryDealGetString(ticket,DEAL_SYMBOL)!=Symbol()) continue;
      if(ReaperMagicIndex(HistoryDealGetInteger(ticket,DEAL_MAGIC))<0) continue;
      result+=HistoryDealGetDouble(ticket,DEAL_PROFIT);
      result+=HistoryDealGetDouble(ticket,DEAL_SWAP);
      result+=HistoryDealGetDouble(ticket,DEAL_COMMISSION);
   }
   return result;
}

string ReaperSignedMoney(const double value)
{
   if(value>0.004) return "+"+DoubleToString(value,2);
   return DoubleToString(value,2);
}

void ReaperUpdateDashboard()
{
   if(ObjectFind(0,"REAPER_DASH_BG")<0) ReaperCreateDashboard();
   ReaperScanHistoryStats();

   const double balance=AccountInfoDouble(ACCOUNT_BALANCE);
   const double open_profit=ReaperOpenProfit();
   const long spread=SymbolInfoInteger(Symbol(),SYMBOL_SPREAD);
   const bool spread_ok=(spread<=(long)InpMaxSpread);
   ReaperSetLabel("REAPER_DASH_BADGE_TXT",spread_ok?"NORMAL":"SPREAD",
                  spread_ok?FC_ACCENT:FC_LOSS);
   ObjectSetInteger(0,"REAPER_DASH_BADGE",OBJPROP_BGCOLOR,
                    spread_ok?C'14,55,39':C'62,26,27');

   string risk_text="";
   if(InpLotMode==0)
      risk_text="Fixed";
   else if(InpLotMode==2)
      risk_text="Balance";
   else
      risk_text=DoubleToString(InpRiskPct,1)+"%";
   ReaperSetLabel("REAPER_RISK_V",risk_text,FC_WHITE);
   ReaperSetLabel("REAPER_SL_V",IntegerToString(InpStopLossPts)+" pts",FC_WHITE);
   ReaperSetLabel("REAPER_TRAIL_V",InpTrailMode==0?"Dyn High":"Fixed",FC_WHITE);

   int active_a=(InpZoneA1?1:0)+(InpZoneA2?1:0)+(InpZoneA3?1:0);
   int active_b=(InpZoneB1?1:0)+(InpZoneB2?1:0)+(InpZoneB3?1:0);
   string frequency=(active_a>0 && active_b>0)?"A + B":(active_a>0?"A":(active_b>0?"B":"OFF"));
   ReaperSetLabel("REAPER_FREQ_V",frequency,FC_WHITE);
   ReaperSetLabel("REAPER_OPEN_V",ReaperSignedMoney(open_profit),
                  open_profit>0.004?FC_WIN:(open_profit<-0.004?FC_LOSS:FC_WHITE));
   ReaperSetLabel("REAPER_BAL_V",DoubleToString(balance,2),FC_WHITE);

   MqlDateTime stamp;
   TimeToStruct(TimeCurrent(),stamp);
   stamp.hour=0;
   stamp.min=0;
   stamp.sec=0;
   MqlDateTime month_stamp=stamp;
   month_stamp.day=1;
   const int days_from_monday=(stamp.day_of_week==0?6:stamp.day_of_week-1);
   const datetime month_start=StructToTime(month_stamp);
   const datetime week_start=StructToTime(stamp)-days_from_monday*86400;
   const double total_profit=ReaperHistoryProfit(0);
   const double month_profit=ReaperHistoryProfit(month_start);
   const double week_profit=ReaperHistoryProfit(week_start);
   ReaperSetLabel("REAPER_TOTAL_V",ReaperSignedMoney(total_profit),
                  total_profit>0.004?FC_WIN:(total_profit<-0.004?FC_LOSS:FC_WHITE));
   ReaperSetLabel("REAPER_MONTH_V",ReaperSignedMoney(month_profit),
                  month_profit>0.004?FC_WIN:(month_profit<-0.004?FC_LOSS:FC_WHITE));
   ReaperSetLabel("REAPER_WEEK_V",ReaperSignedMoney(week_profit),
                  week_profit>0.004?FC_WIN:(week_profit<-0.004?FC_LOSS:FC_WHITE));

   const int active_total=active_a+active_b;
   ReaperSetLabel("REAPER_ZONES_V",IntegerToString(active_total)+" active",FC_GREY);
   const double current_lot=LizardRiskLots();

   for(int i=0;i<6;i++)
   {
      const bool enabled=ReaperStrategyEnabled(i);
      const double net=g_reaperStats[i].grossProfit+g_reaperStats[i].grossLoss;
      ObjectSetInteger(0,"REAPER_SNAME"+IntegerToString(i),OBJPROP_COLOR,enabled?FC_WHITE:FC_DIM);
      ReaperSetLabel("REAPER_PL"+IntegerToString(i),ReaperSignedMoney(net),
                     net>0.004?FC_WIN:(net<-0.004?FC_LOSS:FC_WHITE));
      ReaperSetLabel("REAPER_LOT"+IntegerToString(i),
                     enabled?DoubleToString(current_lot,2):"--",enabled?FC_WHITE:FC_DIM);
      ReaperSetLabel("REAPER_EN"+IntegerToString(i),enabled?"*":".",
                     enabled?FC_WIN:FC_DIM);
   }
}

void InitReaperVisuals()
{
   if(!USE_CUSTOM_DASHBOARD && !SHOW_STYLED_CANDLES && !STYLE_NATIVE_CANDLES) return;
   ReaperApplyChartTheme();
   ReaperDeletePrefix("REAPER_CANDLE_");
   ReaperDeletePrefix("REAPER_DASH_");
   ReaperDeletePrefix("REAPER_IMG_");
   ReaperDeletePrefix("REAPER_ROW");
   ReaperDeletePrefix("REAPER_");
   if(USE_CUSTOM_DASHBOARD) ReaperCreateDashboard();
   if(SHOW_STYLED_CANDLES) ReaperDrawStyledCandles();
   ChartRedraw(0);
}

void UpdateReaperVisuals()
{
   datetime now=TimeCurrent();
   if(g_reaperLastVisualUpdate!=0 && (now-g_reaperLastVisualUpdate)<MathMax(1,VISUAL_REFRESH_SECONDS)) return;
   g_reaperLastVisualUpdate=now;
   ReaperApplyChartTheme();
   if(SHOW_STYLED_CANDLES) ReaperDrawStyledCandles();
   if(USE_CUSTOM_DASHBOARD) ReaperUpdateDashboard();
   ChartRedraw(0);
}

void DeinitReaperVisuals()
{
   ReaperDeletePrefix("REAPER_CANDLE_");
   ReaperDeletePrefix("REAPER_DASH_");
   ReaperDeletePrefix("REAPER_IMG_");
   ReaperDeletePrefix("REAPER_ROW");
   ReaperDeletePrefix("REAPER_");
}

datetime g_reaperLastEntryDebug=0;

void __ReaperEntryDebug(string reason)
{
   if(!PRINT_ENTRY_DEBUG) return;
   datetime now=TimeCurrent();
   if(g_reaperLastEntryDebug!=0 && (now-g_reaperLastEntryDebug)<30) return;
   g_reaperLastEntryDebug=now;
   MqlTick tick;
   SymbolInfoTick(Symbol(),tick);
   Print("Lizard entry debug | ",reason,
         " | chart_tf=",IntegerToString(Period()),
         " | bars_current=",IntegerToString(iBars(Symbol(),0)),
         " | bars_m5=",IntegerToString(iBars(Symbol(),5)),
         " | bars_h1=",IntegerToString(iBars(Symbol(),60)),
         " | bars_h4=",IntegerToString(iBars(Symbol(),240)),
         " | bars_d1=",IntegerToString(iBars(Symbol(),1440)),
         " | spread_points=",IntegerToString((int)SymbolInfoInteger(Symbol(),SYMBOL_SPREAD)),
         " | max_spread=",DoubleToString(MaxSpread,1),
         " | bid=",DoubleToString(tick.bid,_Digits),
         " | ask=",DoubleToString(tick.ask,_Digits),
         " | min_lot=",DoubleToString(SymbolInfoDouble(Symbol(),SYMBOL_VOLUME_MIN),2),
         " | lot_step=",DoubleToString(SymbolInfoDouble(Symbol(),SYMBOL_VOLUME_STEP),2));
}

void __ReaperPrintStartupState()
{
   if(!PRINT_ENTRY_DEBUG) return;
   Print("Lizard loaded",
         " | chart_tf=",IntegerToString(Period()),
         " | legacy_H1_bars=",IntegerToString(iBars(Symbol(),60)),
         " | legacy_H4_bars=",IntegerToString(iBars(Symbol(),240)),
         " | legacy_D1_bars=",IntegerToString(iBars(Symbol(),1440)));
}

// MT5 lifecycle wrappers for old MT4-style init/deinit functions
int OnInit()
{
   LizardApplyPublicInputs();
   __lizard_magic_base=ST1_MagicNumber;
   StartLotsRuntime=StartLots;
   int initResult=init();
   InitReaperVisuals();
   __ReaperCheckTradePermissions("init");
   __ReaperPrintStartupState();
   return initResult;
}

void OnDeinit(const int reason)
{
   deinit();
   DeinitReaperVisuals();
}
