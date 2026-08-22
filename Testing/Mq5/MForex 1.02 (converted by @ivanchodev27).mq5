//+------------------------------------------------------------------+
//|                                        Scalper Cash Maker EA.mq5 |
//|                                           Original by MFOREX.PRO |
//|                              Modified by Matt Todorovski and z.ai|
//+------------------------------------------------------------------+

#property copyright "© Matt Todorovski 2025"
#property link      "https://www.mql5.com/en/users/bluepanther"
#property description " "
#property description "This EA is licensed for FREE Unlimited Private Use, and has been distributed at Telegram group FREE FOREX ROBOTS for members-only."
#property description " "
#property description "Risk Warning! Test this EA on Demo account first."

#include <Trade\Trade.mqh>

CTrade trade;

//--- Inputs
input double    StartLot         = 0.01;
input int       Step             = 200;
input int       TimeStart        = 2;
input int       TimeEnd          = 23;
input bool      Info             = true;
input color     TextColor        = clrWhite;
input color     InfoDataColor    = clrDodgerBlue;
input color     FonColor         = clrBlack;
input int       FontSizeInfo     = 7;
input int       Magic            = 202603030;

//--- Global Variables
string   ea_comment  = "MForex Scalper";
double   MinProfit   = 0.55;
int      Dist        = 5;

int      D;
double   PricSellLine, PricBuyLine, NewLot, NewProfProc;
double   g_LastBuyPrice, g_LastSellPrice;

//+------------------------------------------------------------------+
int OnInit()
{
   D = 1;
   if(_Digits == 5 || _Digits == 3) D = 10;

   trade.SetExpertMagicNumber(Magic);
   trade.SetDeviationInPoints(10);

   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);

   HLineCreate("LineBuy",  ask + Dist * D * _Point, clrNONE);
   HLineCreate("LineSell", bid - Dist * D * _Point, clrNONE);

   if(Info) CreateGUI();

   return(INIT_SUCCEEDED);
}

//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   ObjectsDeleteAll(0, "LineBuy");
   ObjectsDeleteAll(0, "LineSell");
   ObjectsDeleteAll(0, "INFO_");
}

//+------------------------------------------------------------------+
void OnTick()
{
   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);

   double Lot    = StartLot;
   double minLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   if(Lot < minLot) Lot = minLot;

   PricBuyLine  = ObjectGetDouble(0, "LineBuy",  OBJPROP_PRICE);
   PricSellLine = ObjectGetDouble(0, "LineSell", OBJPROP_PRICE);

   //--- Update line positions
   if(ask + Dist * D * _Point < PricBuyLine)
      HLineMove("LineBuy", ask + Dist * D * _Point);

   if(bid - Dist * D * _Point > PricSellLine)
      HLineMove("LineSell", bid - Dist * D * _Point);

   if(ask < PricSellLine && bid - Dist * D * _Point > PricBuyLine)
      HLineMove("LineBuy", ask + Dist * D * _Point);

   if(bid > PricBuyLine && ask + Dist * D * _Point < PricSellLine)
      HLineMove("LineSell", bid - Dist * D * _Point);

   //--- Trading hour check
   MqlDateTime dt;
   TimeToStruct(TimeCurrent(), dt);
   int  currentHour = dt.hour;
   bool tradingTime = (currentHour >= TimeStart && currentHour < TimeEnd);

   //--- Microstructure Detection
   bool BullStructure = false;
   bool BearStructure = false;

   if(Bars(_Symbol, PERIOD_CURRENT) > 3)
   {
      double close1 = iClose(_Symbol, PERIOD_CURRENT, 1);
      double high2  = iHigh (_Symbol, PERIOD_CURRENT, 2);
      double low2   = iLow  (_Symbol, PERIOD_CURRENT, 2);

      BullStructure = (close1 > high2);
      BearStructure = (close1 < low2);
   }

   //--- Initial entries (only when no open positions)
   if(tradingTime)
   {
      if(BearStructure && ask <= PricSellLine && LastType() != (int)POSITION_TYPE_SELL && Count(-1) == 0)
      {
         if(trade.Sell(Lot, _Symbol, bid, 0, 0, ea_comment))
            HLineMove("LineBuy", ask + Dist * D * _Point);
      }

      if(BullStructure && bid >= PricBuyLine && LastType() != (int)POSITION_TYPE_BUY && Count(-1) == 0)
      {
         if(trade.Buy(Lot, _Symbol, ask, 0, 0, ea_comment))
            HLineMove("LineSell", bid - Dist * D * _Point);
      }
   }

   //--- Grid additions
   int totalOrders = Count(-1);
   NewLot = NormalizeDouble(Lot + (totalOrders * 0.01), 2);

   UpdateLastOrderPrices();

   if(BearStructure && ask <= PricSellLine &&
      Count((int)POSITION_TYPE_SELL) > 0 &&
      bid - Step * D * _Point > g_LastSellPrice)
   {
      if(trade.Sell(NewLot, _Symbol, bid, 0, 0, ea_comment))
         HLineMove("LineBuy", ask + Dist * D * _Point);
   }

   if(BullStructure && bid >= PricBuyLine &&
      Count((int)POSITION_TYPE_BUY) > 0 &&
      ask + Step * D * _Point < g_LastBuyPrice)
   {
      if(trade.Buy(NewLot, _Symbol, ask, 0, 0, ea_comment))
         HLineMove("LineSell", bid - Dist * D * _Point);
   }

   //--- Close logic: BUY positions
   if(Count((int)POSITION_TYPE_BUY) > 0 && ask <= PricSellLine)
   {
      double totalBuyProfit = Profit((int)POSITION_TYPE_BUY);
      int    buyCount       = Count((int)POSITION_TYPE_BUY);

      if(buyCount == 1)
      {
         if(totalBuyProfit >= 0.55) CloseAll();
      }
      else if(buyCount == 2)
      {
         if(totalBuyProfit >= 1.00) CloseAll();
      }
      else
      {
         ulong firstTicket = GetOldestTicket((int)POSITION_TYPE_BUY);
         ulong lastTicket  = GetNewestTicket((int)POSITION_TYPE_BUY);

         if(firstTicket > 0 && lastTicket > 0)
         {
            double combinedProfit = GetTicketProfit(firstTicket) + GetTicketProfit(lastTicket);
            if(combinedProfit >= 0.55)
            {
               CloseTicket(firstTicket);
               CloseTicket(lastTicket);
            }
         }
      }
   }

   //--- Close logic: SELL positions
   if(Count((int)POSITION_TYPE_SELL) > 0 && bid >= PricBuyLine)
   {
      double totalSellProfit = Profit((int)POSITION_TYPE_SELL);
      int    sellCount       = Count((int)POSITION_TYPE_SELL);

      if(sellCount == 1)
      {
         if(totalSellProfit >= 0.55) CloseAll();
      }
      else if(sellCount == 2)
      {
         if(totalSellProfit >= 1.00) CloseAll();
      }
      else
      {
         ulong firstTicket = GetOldestTicket((int)POSITION_TYPE_SELL);
         ulong lastTicket  = GetNewestTicket((int)POSITION_TYPE_SELL);

         if(firstTicket > 0 && lastTicket > 0)
         {
            double combinedProfit = GetTicketProfit(firstTicket) + GetTicketProfit(lastTicket);
            if(combinedProfit >= 0.55)
            {
               CloseTicket(firstTicket);
               CloseTicket(lastTicket);
            }
         }
      }
   }

   if(Info) UpdateGUI();
}

//+------------------------------------------------------------------+
//| Update cached last open prices per direction                     |
//+------------------------------------------------------------------+
void UpdateLastOrderPrices()
{
   g_LastBuyPrice  = 0;
   g_LastSellPrice = 0;
   ulong maxBuyTicket  = 0;
   ulong maxSellTicket = 0;

   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;

      if(PositionGetString(POSITION_SYMBOL)        == _Symbol &&
         PositionGetInteger(POSITION_MAGIC)         == Magic)
      {
         if(PositionGetInteger(POSITION_TYPE) == POSITION_TYPE_BUY && ticket > maxBuyTicket)
         {
            maxBuyTicket   = ticket;
            g_LastBuyPrice = PositionGetDouble(POSITION_PRICE_OPEN);
         }
         if(PositionGetInteger(POSITION_TYPE) == POSITION_TYPE_SELL && ticket > maxSellTicket)
         {
            maxSellTicket   = ticket;
            g_LastSellPrice = PositionGetDouble(POSITION_PRICE_OPEN);
         }
      }
   }
}

//+------------------------------------------------------------------+
//| Return ticket of oldest open position of given type              |
//+------------------------------------------------------------------+
ulong GetOldestTicket(int type)
{
   ulong    ticket  = 0;
   datetime minTime = 0;

   for(int i = 0; i < PositionsTotal(); i++)
   {
      ulong t = PositionGetTicket(i);
      if(t == 0) continue;

      if(PositionGetString(POSITION_SYMBOL)  == _Symbol  &&
         PositionGetInteger(POSITION_MAGIC)   == Magic    &&
         (int)PositionGetInteger(POSITION_TYPE) == type)
      {
         datetime openTime = (datetime)PositionGetInteger(POSITION_TIME);
         if(ticket == 0 || openTime < minTime)
         {
            minTime = openTime;
            ticket  = t;
         }
      }
   }
   return ticket;
}

//+------------------------------------------------------------------+
//| Return ticket of newest open position of given type              |
//+------------------------------------------------------------------+
ulong GetNewestTicket(int type)
{
   ulong    ticket  = 0;
   datetime maxTime = 0;

   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong t = PositionGetTicket(i);
      if(t == 0) continue;

      if(PositionGetString(POSITION_SYMBOL)  == _Symbol  &&
         PositionGetInteger(POSITION_MAGIC)   == Magic    &&
         (int)PositionGetInteger(POSITION_TYPE) == type)
      {
         datetime openTime = (datetime)PositionGetInteger(POSITION_TIME);
         if(openTime > maxTime)
         {
            maxTime = openTime;
            ticket  = t;
         }
      }
   }
   return ticket;
}

//+------------------------------------------------------------------+
//| Return profit+swap for a given open position ticket              |
//+------------------------------------------------------------------+
double GetTicketProfit(ulong ticket)
{
   if(PositionSelectByTicket(ticket))
      return PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
   return 0.0;
}

//+------------------------------------------------------------------+
//| Close a single position by ticket                                |
//+------------------------------------------------------------------+
bool CloseTicket(ulong ticket)
{
   return trade.PositionClose(ticket, 10);
}

//+------------------------------------------------------------------+
//| Close all EA positions on this symbol                            |
//+------------------------------------------------------------------+
void CloseAll()
{
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;

      if(PositionGetString(POSITION_SYMBOL) == _Symbol &&
         PositionGetInteger(POSITION_MAGIC)  == Magic)
      {
         trade.PositionClose(ticket, 10);
      }
   }
}

//+------------------------------------------------------------------+
//| Return type of the last closed deal (POSITION_TYPE_BUY/SELL/-1) |
//+------------------------------------------------------------------+
int LastType()
{
   int      type = -1;
   datetime dt   = 0;

   if(!HistorySelect(0, TimeCurrent())) return type;

   int total = HistoryDealsTotal();
   for(int i = total - 1; i >= 0; i--)
   {
      ulong ticket = HistoryDealGetTicket(i);
      if(ticket == 0) continue;

      if(HistoryDealGetString (ticket, DEAL_SYMBOL) != _Symbol) continue;
      if(HistoryDealGetInteger(ticket, DEAL_MAGIC)  != Magic)   continue;
      if(HistoryDealGetInteger(ticket, DEAL_ENTRY)  != DEAL_ENTRY_IN) continue;

      datetime openTime = (datetime)HistoryDealGetInteger(ticket, DEAL_TIME);
      if(openTime > dt)
      {
         dt = openTime;
         long dealType = HistoryDealGetInteger(ticket, DEAL_TYPE);
         if(dealType == DEAL_TYPE_BUY)  type = (int)POSITION_TYPE_BUY;
         if(dealType == DEAL_TYPE_SELL) type = (int)POSITION_TYPE_SELL;
      }
   }
   return type;
}

//+------------------------------------------------------------------+
//| Count open positions. type=-1 counts all directions             |
//+------------------------------------------------------------------+
int Count(int type)
{
   int count = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;

      if(PositionGetString(POSITION_SYMBOL)  == _Symbol &&
         PositionGetInteger(POSITION_MAGIC)   == Magic   &&
         (type == -1 || (int)PositionGetInteger(POSITION_TYPE) == type))
         count++;
   }
   return count;
}

//+------------------------------------------------------------------+
//| Sum profit+swap for open positions. type=-1 sums all directions |
//+------------------------------------------------------------------+
double Profit(int type)
{
   double prof = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;

      if(PositionGetString(POSITION_SYMBOL)  == _Symbol &&
         PositionGetInteger(POSITION_MAGIC)   == Magic   &&
         (type == -1 || (int)PositionGetInteger(POSITION_TYPE) == type))
      {
         prof += PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
      }
   }
   return prof;
}

//+------------------------------------------------------------------+
//| Create a horizontal line object                                  |
//+------------------------------------------------------------------+
bool HLineCreate(string name, double price, color clr)
{
   if(price == 0) price = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   ResetLastError();
   if(!ObjectCreate(0, name, OBJ_HLINE, 0, 0, price))
   {
      Print(__FUNCTION__, ": failed to create line! Error code = ", GetLastError());
      return false;
   }
   ObjectSetInteger(0, name, OBJPROP_COLOR,      clr);
   ObjectSetInteger(0, name, OBJPROP_STYLE,      STYLE_SOLID);
   ObjectSetInteger(0, name, OBJPROP_WIDTH,      1);
   ObjectSetInteger(0, name, OBJPROP_BACK,       false);
   ObjectSetInteger(0, name, OBJPROP_SELECTABLE, true);
   ObjectSetInteger(0, name, OBJPROP_HIDDEN,     false);
   return true;
}

//+------------------------------------------------------------------+
//| Move a horizontal line to a new price                            |
//+------------------------------------------------------------------+
bool HLineMove(string name, double price)
{
   if(price == 0) price = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   ResetLastError();
   if(!ObjectMove(0, name, 0, 0, price))
      return false;
   return true;
}

//+------------------------------------------------------------------+
//| Profit sum of closed deals between two timestamps               |
//+------------------------------------------------------------------+
double ProfitHistory(datetime fromTime, datetime toTime)
{
   double total = 0.0;
   if(!HistorySelect(fromTime, toTime)) return total;
   int n = HistoryDealsTotal();
   for(int i = 0; i < n; i++)
   {
      ulong ticket = HistoryDealGetTicket(i);
      if(ticket == 0) continue;
      if(HistoryDealGetInteger(ticket, DEAL_ENTRY) != DEAL_ENTRY_OUT) continue;
      if(HistoryDealGetString (ticket, DEAL_SYMBOL) != _Symbol)       continue;
      total += HistoryDealGetDouble(ticket, DEAL_PROFIT)
             + HistoryDealGetDouble(ticket, DEAL_SWAP)
             + HistoryDealGetDouble(ticket, DEAL_COMMISSION);
   }
   return total;
}

//+------------------------------------------------------------------+
//| Update GUI labels on every tick                                  |
//+------------------------------------------------------------------+
void UpdateGUI()
{
   double balance    = AccountInfoDouble(ACCOUNT_BALANCE);
   double equity     = AccountInfoDouble(ACCOUNT_EQUITY);
   double pairProfit = Profit(-1);
   NewProfProc = (balance > 0) ? pairProfit / (balance / 100.0) : 0;
   double spread = (double)SymbolInfoInteger(_Symbol, SYMBOL_SPREAD);

   // --- Date helpers ---
   MqlDateTime now_s;
   TimeToStruct(TimeCurrent(), now_s);
   now_s.hour = 0; now_s.min = 0; now_s.sec = 0;
   datetime todayMidnight     = StructToTime(now_s);
   datetime yesterdayMidnight = todayMidnight - 86400;
   MqlDateTime week_s; TimeToStruct(TimeCurrent(), week_s);
   int dow = (int)week_s.day_of_week; if(dow == 0) dow = 7;
   datetime weekStart = todayMidnight - (dow - 1) * 86400;

   double profitToday     = ProfitHistory(todayMidnight,     TimeCurrent());
   double profitYesterday = ProfitHistory(yesterdayMidnight, todayMidnight);
   double profitWeek      = ProfitHistory(weekStart,         TimeCurrent());
   double profitTotal     = ProfitHistory(0,                 TimeCurrent());

   // --- Section 1 ---
   ObjectSetString(0, "INFO_row_stop",    OBJPROP_TEXT, "Min stop level:       " + IntegerToString((int)SymbolInfoInteger(_Symbol, SYMBOL_TRADE_STOPS_LEVEL)));
   ObjectSetString(0, "INFO_row_profpct", OBJPROP_TEXT, "Profit % actual:       " + DoubleToString(NewProfProc, 2));
   ObjectSetString(0, "INFO_row_bal",     OBJPROP_TEXT, "Balance:                   " + DoubleToString(balance, 2));
   ObjectSetString(0, "INFO_row_eq",      OBJPROP_TEXT, "Equity:                     " + DoubleToString(equity,  2));
   ObjectSetString(0, "INFO_row_spread",  OBJPROP_TEXT, "Spread:                    " + DoubleToString(spread,  1));
   ObjectSetString(0, "INFO_row_target",  OBJPROP_TEXT, "Close target:            " + DoubleToString(MinProfit, 2));

   // --- Section 2 ---
   ObjectSetString(0, "INFO_row_ppair",   OBJPROP_TEXT, "Profit on pair:          " + DoubleToString(pairProfit,      2));
   ObjectSetString(0, "INFO_row_ptotal",  OBJPROP_TEXT, "Total account profit: " + DoubleToString(profitTotal,     2));
   ObjectSetString(0, "INFO_row_today",   OBJPROP_TEXT, "Today's profit:          " + DoubleToString(profitToday,    2));
   ObjectSetString(0, "INFO_row_yest",    OBJPROP_TEXT, "Yesterday's profit:   " + DoubleToString(profitYesterday, 2));
   ObjectSetString(0, "INFO_row_week",    OBJPROP_TEXT, "Weekly profit:           " + DoubleToString(profitWeek,     2));
}

//+------------------------------------------------------------------+
//| Build the GUI panel on init                                      |
//+------------------------------------------------------------------+
void CreateGUI()
{
   int px = 230;   // X distance from right for all labels (left edge of panel)
   int pw = 225;   // Panel width

   // Background panel
   RectLabelCreate("INFO_fon", 5, 18, pw, 278, FonColor);

   // ── Title ──
   PutLabel_("INFO_LOGO",       115, 26, "WWW.MFOREX.PRO [" + _Symbol + "]");
   PutLabel ("INFO_Line0",       px, 34, "_____________________________");

   // ── Section 1 header ──
   PutLabel_("INFO_head1",       px, 48, "Account information");
   PutLabel ("INFO_Line1",       px, 51, "_____________________________");

   // Rows – single string updated in UpdateGUI()
   PutLabel("INFO_row_stop",    px,  65, "");
   PutLabel("INFO_row_profpct", px,  80, "");
   PutLabel("INFO_row_bal",     px,  95, "");
   PutLabel("INFO_row_eq",      px, 110, "");
   PutLabel("INFO_row_spread",  px, 125, "");
   PutLabel("INFO_row_target",  px, 140, "");

   PutLabel("INFO_Line2",        px, 155, "_____________________________");

   // ── Section 2 header ──
   PutLabel_("INFO_head2",       px, 169, "Pair profit");
   PutLabel ("INFO_Line3",       px, 172, "_____________________________");

   PutLabel("INFO_row_ppair",   px, 186, "");
   PutLabel("INFO_row_ptotal",  px, 201, "");
   PutLabel("INFO_row_today",   px, 216, "");
   PutLabel("INFO_row_yest",    px, 231, "");
   PutLabel("INFO_row_week",    px, 246, "");
}

//+------------------------------------------------------------------+
//| Label helper – uses TextColor                                    |
//+------------------------------------------------------------------+
void PutLabel(string name, int x, int y, string text)
{
   if(ObjectFind(0, name) < 0)
      ObjectCreate(0, name, OBJ_LABEL, 0, 0, 0);

   ObjectSetInteger(0, name, OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, name, OBJPROP_YDISTANCE, y);
   ObjectSetInteger(0, name, OBJPROP_CORNER,    CORNER_RIGHT_UPPER);
   ObjectSetString (0, name, OBJPROP_TEXT,      text);
   ObjectSetString (0, name, OBJPROP_FONT,      "Arial");
   ObjectSetInteger(0, name, OBJPROP_FONTSIZE,  FontSizeInfo);
   ObjectSetInteger(0, name, OBJPROP_COLOR,     TextColor);
   ObjectSetInteger(0, name, OBJPROP_BACK,      false);
}

//+------------------------------------------------------------------+
//| Label helper – uses InfoDataColor (highlighted labels)          |
//+------------------------------------------------------------------+
void PutLabel_(string name, int x, int y, string text)
{
   if(ObjectFind(0, name) < 0)
      ObjectCreate(0, name, OBJ_LABEL, 0, 0, 0);

   ObjectSetInteger(0, name, OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, name, OBJPROP_YDISTANCE, y);
   ObjectSetInteger(0, name, OBJPROP_CORNER,    CORNER_RIGHT_UPPER);
   ObjectSetString (0, name, OBJPROP_TEXT,      text);
   ObjectSetString (0, name, OBJPROP_FONT,      "Arial");
   ObjectSetInteger(0, name, OBJPROP_FONTSIZE,  FontSizeInfo);
   ObjectSetInteger(0, name, OBJPROP_COLOR,     InfoDataColor);
   ObjectSetInteger(0, name, OBJPROP_BACK,      false);
}

//+------------------------------------------------------------------+
//| Background rectangle panel                                       |
//+------------------------------------------------------------------+
bool RectLabelCreate(string name, int x, int y, int width, int height, color back_clr)
{
   if(ObjectFind(0, name) < 0)
      if(!ObjectCreate(0, name, OBJ_RECTANGLE_LABEL, 0, 0, 0)) return false;

   ObjectSetInteger(0, name, OBJPROP_XDISTANCE,   x);
   ObjectSetInteger(0, name, OBJPROP_YDISTANCE,   y);
   ObjectSetInteger(0, name, OBJPROP_XSIZE,       width);
   ObjectSetInteger(0, name, OBJPROP_YSIZE,       height);
   ObjectSetInteger(0, name, OBJPROP_BGCOLOR,     back_clr);
   ObjectSetInteger(0, name, OBJPROP_BORDER_TYPE, BORDER_SUNKEN);
   ObjectSetInteger(0, name, OBJPROP_CORNER,      CORNER_RIGHT_UPPER);
   ObjectSetInteger(0, name, OBJPROP_COLOR,       clrBlue);
   ObjectSetInteger(0, name, OBJPROP_WIDTH,       1);
   ObjectSetInteger(0, name, OBJPROP_BACK,        false);
   return true;
}
//+------------------------------------------------------------------+