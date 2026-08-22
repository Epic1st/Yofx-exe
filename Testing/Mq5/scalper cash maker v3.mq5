//+------------------------------------------------------------------+
//|                                    Scalper Cash Maker EA v3.mq5  |
//|                                                      MFOREX.PRO  |
//|                                          https://www.mforex.pro  |
//|              MQL5 Port — All v3 fixes & improvements included    |
//+------------------------------------------------------------------+
#property copyright   "MFOREX.PRO"
#property link        "https://www.mforex.pro"
#property version     "3.00"
#property description "ATR-Adaptive Grid Scalper with DD Kill Switch, SmartDDR, Momentum Exit"

//--- MQL5 uses CTrade for order operations
#include <Trade\Trade.mqh>
#include <Trade\PositionInfo.mqh>
#include <Trade\HistoryOrderInfo.mqh>

CTrade        trade;
CPositionInfo posInfo;

//+------------------------------------------------------------------+
//| INPUT PARAMETERS                                                  |
//+------------------------------------------------------------------+

//--- Risk & Lot Management
input group              "=== Risk & Lot Management ==="
input double  Risk            = 2.0;   // Risk % per base lot calculation
input double  MaxLot          = 5.0;   // Maximum lot size (hard cap)
input int     MaxOrders       = 5;     // Max grid orders per direction
input double  UpLot           = 1.35;  // Grid lot multiplier per level

//--- Grid & Entry
input group              "=== Grid & Entry Settings ==="
input int     Step            = 75;    // ATR fallback step (points)
input int     Dist            = 5;     // Initial line distance (ATR fallback, points)
input long    Magic           = 2026;  // Unique magic number

//--- Filters & Safety
input group              "=== Filters & Safety ==="
input int     MaxSpread       = 30;    // Max spread in points
input double  DDKillPercent   = 20.0;  // Drawdown % to trigger soft kill switch
input double  MomentumThresh  = 0.3;   // Momentum flat threshold for profit exit
input int     EmergencySL_ATR = 5;     // Emergency SL = N * ATR (0 = disabled)

//--- Direction Control
input group              "=== Direction Control ==="
input bool    AllowBuy        = true;  // Allow BUY orders
input bool    AllowSell       = true;  // Allow SELL orders

//--- Trading Hours
input group              "=== Trading Hours (Server Time) ==="
input int     TimeStart       = 2;     // Trading start hour
input int     TimeEnd         = 23;    // Trading end hour
input bool    CloseFriday     = true;  // Auto-close all positions before weekend

//--- Info Panel
input group              "=== Info Panel ==="
input bool    Info            = false;       // Show info panel on chart
input color   TextColor       = clrWhite;
input color   InfoDataColor   = clrDodgerBlue;
input color   FonColor        = clrBlack;
input int     FontSizeInfo    = 8;

//+------------------------------------------------------------------+
//| GLOBALS                                                           |
//+------------------------------------------------------------------+
string   EA_Comment    = "www.mforex.pro";
int      D             = 1;        // Digit multiplier (5/3-digit brokers)
double   Lot           = 0;
double   NewLot        = 0;
double   NewProfProc   = 0;
double   PricBuyLine   = 0;
double   PricSellLine  = 0;

// Panel button colors
color    FonButtonBuy  = clrBlue;
color    FonButtonSell = clrRed;
color    TextButtonBS  = clrWhite;
color    ButtonBorder  = clrBlue;
color    ClickButton   = clrBlack;

// Info panel cache (recalculate once per second)
datetime lastInfoCalc      = 0;
double   cachedProfitAll   = 0;
double   cachedProfitDay   = 0;
double   cachedProfitYest  = 0;
double   cachedProfitWeek  = 0;
double   cachedProfitMonth = 0;

// ATR indicator handle
int      hATR  = INVALID_HANDLE;
int      hMom  = INVALID_HANDLE;

//+------------------------------------------------------------------+
int OnInit()
{
   // 5/3-digit broker detection
   D = 1;
   if(_Digits == 5 || _Digits == 3) D = 10;

   // Set magic number on CTrade
   trade.SetExpertMagicNumber((ulong)Magic);
   trade.SetDeviationInPoints(10);
   trade.SetTypeFilling(ORDER_FILLING_FOK);

   // Create indicator handles (MQL5 uses handles, not direct calls)
   hATR = iATR(_Symbol, PERIOD_CURRENT, 14);
   hMom = iMomentum(_Symbol, PERIOD_CURRENT, 5, PRICE_CLOSE);
   if(hATR == INVALID_HANDLE || hMom == INVALID_HANDLE)
   {
      Print("Failed to create indicator handles. Error=", GetLastError());
      return INIT_FAILED;
   }

   // Delete any stale lines from previous run
   ObjectDelete(0, "LineBuy");
   ObjectDelete(0, "LineSell");

   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);

   HLineCreate("LineBuy",  ask + Dist * D * _Point, clrDodgerBlue);
   HLineCreate("LineSell", bid - Dist * D * _Point, clrRed);

   Print("Scalper Cash Maker v3.00 (MQL5) initialized on ", _Symbol);
   return INIT_SUCCEEDED;
}

//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   ObjectDelete(0, "LineBuy");
   ObjectDelete(0, "LineSell");

   // Release indicator handles
   if(hATR != INVALID_HANDLE) IndicatorRelease(hATR);
   if(hMom != INVALID_HANDLE) IndicatorRelease(hMom);

   // Clean up info panel objects on removal
   if(reason == REASON_REMOVE || reason == REASON_CHARTCLOSE)
      CleanupPanel();
}

//+------------------------------------------------------------------+
void OnTick()
{
   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);

   //--- Friday auto-close
   MqlDateTime dt;
   TimeToStruct(TimeCurrent(), dt);
   if(CloseFriday && dt.day_of_week == 5 && dt.hour >= 22)
   {
      CloseAll();
      return;
   }

   //--- Read ATR (MQL5: CopyBuffer into array)
   double atrBuf[1];
   if(CopyBuffer(hATR, 0, 0, 1, atrBuf) < 1 || atrBuf[0] <= 0)
      atrBuf[0] = Step * D * _Point;  // fallback
   double atr = atrBuf[0];

   double GridStep  = atr * 0.8;
   double EntryDist = atr * 0.25;

   //--- DD Kill Switch
   double balance = AccountInfoDouble(ACCOUNT_BALANCE);
   double equity  = AccountInfoDouble(ACCOUNT_EQUITY);
   double ddRatio = (balance > 0) ? (balance - equity) / balance * 100.0 : 0;
   bool   InDD     = (ddRatio >= DDKillPercent);
   double LotMult  = InDD ? 0.5 : 1.0;
   double GridMult = InDD ? 2.0 : 1.0;
   double ExitMult = InDD ? 0.7 : 1.0;

   double effDist = EntryDist * GridMult;
   double effStep = GridStep  * GridMult;

   //--- Base lot calculation (MQL5 uses SymbolInfoDouble for tick value)
   double tickVal = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE);
   if(tickVal <= 0) tickVal = 1;
   Lot = NormalizeDouble(balance / 100.0 * Risk / (tickVal * 100.0 * D) * LotMult, 2);
   double minLot  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double maxLot  = MathMin(MaxLot, SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX));
   double stepLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
   Lot = MathMax(minLot, MathMin(maxLot, MathRound(Lot / stepLot) * stepLot));

   //--- Spread filter (MQL5: spread in points)
   long   spreadRaw = SymbolInfoInteger(_Symbol, SYMBOL_SPREAD);
   bool   SpreadOK  = (spreadRaw <= MaxSpread);

   //--- Read line prices directly
   PricBuyLine  = ObjectGetDouble(0, "LineBuy",  OBJPROP_PRICE);
   PricSellLine = ObjectGetDouble(0, "LineSell", OBJPROP_PRICE);

   //--- Trail lines with ATR-based distance
   if(ask + effDist < PricBuyLine)   HLineMove("LineBuy",  ask + effDist);
   if(bid - effDist > PricSellLine)  HLineMove("LineSell", bid - effDist);
   if(ask < PricSellLine && bid - effDist > PricBuyLine) HLineMove("LineBuy",  ask + effDist);
   if(bid > PricBuyLine  && ask + effDist < PricSellLine) HLineMove("LineSell", bid - effDist);

   //--- Microstructure signals (MQL5: use iClose/iHigh/iLow or Bars data)
   double c1 = iClose(_Symbol, PERIOD_CURRENT, 1);
   double h2 = iHigh(_Symbol,  PERIOD_CURRENT, 2);
   double l2 = iLow(_Symbol,   PERIOD_CURRENT, 2);
   bool BullStructure = (c1 > h2);
   bool BearStructure = (c1 < l2);

   //--- Emergency SL in price
   double eSL_pts = (EmergencySL_ATR > 0) ? atr * EmergencySL_ATR : 0;

   //--- Trading hours check
   bool inHours = (dt.hour >= TimeStart && dt.hour < TimeEnd);

   //--- Open initial orders (no existing positions in any direction)
   if(inHours)
   {
      if(AllowSell && SpreadOK && BearStructure && ask <= PricSellLine
         && LastType() != POSITION_TYPE_SELL && CountPositions(-1) == 0)
      {
         double sl = (eSL_pts > 0) ? NormalizeDouble(bid + eSL_pts, _Digits) : 0;
         if(trade.Sell(Lot, _Symbol, bid, sl, 0, EA_Comment))
            HLineMove("LineBuy", ask + effDist);
         else
            Print("Sell failed: ", trade.ResultRetcodeDescription());
      }

      if(AllowBuy && SpreadOK && BullStructure && bid >= PricBuyLine
         && LastType() != POSITION_TYPE_BUY && CountPositions(-1) == 0)
      {
         double sl = (eSL_pts > 0) ? NormalizeDouble(ask - eSL_pts, _Digits) : 0;
         if(trade.Buy(Lot, _Symbol, ask, sl, 0, EA_Comment))
            HLineMove("LineSell", bid - effDist);
         else
            Print("Buy failed: ", trade.ResultRetcodeDescription());
      }
   }

   //--- Grid lot size with hard cap
   int openCount = CountPositions(-1);
   NewLot = NormalizeDouble(Lot * MathPow(UpLot, openCount), 2);
   NewLot = MathMax(minLot, MathMin(maxLot, MathRound(NewLot / stepLot) * stepLot));

   //--- Grid averaging orders
   if(AllowSell && SpreadOK && BearStructure && ask <= PricSellLine
      && CountPositions(POSITION_TYPE_SELL) > 0
      && CountPositions(POSITION_TYPE_SELL) < MaxOrders
      && bid - effStep > SellPric())
   {
      double sl = (eSL_pts > 0) ? NormalizeDouble(bid + eSL_pts, _Digits) : 0;
      if(trade.Sell(NewLot, _Symbol, bid, sl, 0, EA_Comment))
         HLineMove("LineBuy", ask + effDist);
      else
         Print("Grid Sell failed: ", trade.ResultRetcodeDescription());
   }

   if(AllowBuy && SpreadOK && BullStructure && bid >= PricBuyLine
      && CountPositions(POSITION_TYPE_BUY) > 0
      && CountPositions(POSITION_TYPE_BUY) < MaxOrders
      && ask + effStep < BuyPric())
   {
      double sl = (eSL_pts > 0) ? NormalizeDouble(ask - eSL_pts, _Digits) : 0;
      if(trade.Buy(NewLot, _Symbol, ask, sl, 0, EA_Comment))
         HLineMove("LineSell", bid - effDist);
      else
         Print("Grid Buy failed: ", trade.ResultRetcodeDescription());
   }

   //--- Exposure-weighted profit target
   double totalLots      = TotalLots();
   double exposureTarget = 0.5 * totalLots * ExitMult;
   double currentProfit  = CalcProfit(-1);
   NewProfProc = (balance > 0) ? currentProfit / (balance / 100.0) : 0;

   //--- Momentum exit — close only profitable trades when price stalls
   double momBuf[1];
   if(CopyBuffer(hMom, 0, 0, 1, momBuf) >= 1)
   {
      bool MomentumFlat = (MathAbs(momBuf[0] - 100.0) < MomentumThresh);
      if(MomentumFlat && currentProfit > 0)
         CloseProfitable();
   }

   //--- SmartDDR — close best+worst profit pair
   if(CountPositions(POSITION_TYPE_BUY) > 0 && ask <= PricSellLine && currentProfit >= exposureTarget)
      SmartDDR();
   if(CountPositions(POSITION_TYPE_SELL) > 0 && bid >= PricBuyLine && currentProfit >= exposureTarget)
      SmartDDR();

   //--- Info panel
   if(Info)
      DrawInfoPanel(ddRatio, balance, equity, ask, bid);
}

//+------------------------------------------------------------------+
//| Chart button event handler                                        |
//+------------------------------------------------------------------+
void OnChartEvent(const int id, const long &lparam, const double &dparam, const string &sparam)
{
   // Visual button state feedback
   ObjectSetInteger(0, "TRADEs_B", OBJPROP_BGCOLOR,
      ObjectGetInteger(0, "TRADEs_B", OBJPROP_STATE) ? ClickButton : FonButtonBuy);
   ObjectSetInteger(0, "TRADEs_S", OBJPROP_BGCOLOR,
      ObjectGetInteger(0, "TRADEs_S", OBJPROP_STATE) ? ClickButton : FonButtonSell);

   if(id == CHARTEVENT_OBJECT_CLICK)
   {
      // Spread guard on manual trades
      long spread = SymbolInfoInteger(_Symbol, SYMBOL_SPREAD);
      if(spread > MaxSpread)
      {
         Print("Manual trade blocked: spread ", spread, " > MaxSpread ", MaxSpread);
         ChartRedraw();
         return;
      }

      double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
      double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);

      if(sparam == "TRADEs_B" && AllowBuy)
      {
         if(!trade.Buy(Lot, _Symbol, ask, 0, 0, EA_Comment))
            Print("Manual Buy failed: ", trade.ResultRetcodeDescription());
         ObjectSetInteger(0, "TRADEs_B", OBJPROP_STATE, false);
      }
      if(sparam == "TRADEs_S" && AllowSell)
      {
         if(!trade.Sell(Lot, _Symbol, bid, 0, 0, EA_Comment))
            Print("Manual Sell failed: ", trade.ResultRetcodeDescription());
         ObjectSetInteger(0, "TRADEs_S", OBJPROP_STATE, false);
      }
      ChartRedraw();
   }
}

//+------------------------------------------------------------------+
//| SmartDDR: close best-profit + worst-profit position pair         |
//+------------------------------------------------------------------+
void SmartDDR()
{
   ulong winTicket  = GetBestProfitTicket();
   ulong lossTicket = GetWorstProfitTicket();

   if(winTicket == 0 || lossTicket == 0) return;
   if(winTicket == lossTicket) return;  // Guard same-ticket close

   double winP  = GetPositionProfit(winTicket);
   double lossP = GetPositionProfit(lossTicket);
   if(winP + lossP > 0.3)
   {
      CloseByTicket(winTicket);
      CloseByTicket(lossTicket);
   }
}

ulong GetBestProfitTicket()
{
   ulong  best   = 0;
   double bestP  = -DBL_MAX;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL) != _Symbol) continue;
      if((ulong)PositionGetInteger(POSITION_MAGIC) != (ulong)Magic) continue;
      double p = PositionGetDouble(POSITION_PROFIT)
               + PositionGetDouble(POSITION_SWAP);
      if(p > bestP) { bestP = p; best = ticket; }
   }
   return best;
}

ulong GetWorstProfitTicket()
{
   ulong  worst  = 0;
   double worstP = DBL_MAX;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL) != _Symbol) continue;
      if((ulong)PositionGetInteger(POSITION_MAGIC) != (ulong)Magic) continue;
      double p = PositionGetDouble(POSITION_PROFIT)
               + PositionGetDouble(POSITION_SWAP);
      if(p < worstP) { worstP = p; worst = ticket; }
   }
   return worst;
}

double GetPositionProfit(ulong ticket)
{
   if(!PositionSelectByTicket(ticket)) return 0;
   return PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
}

void CloseByTicket(ulong ticket)
{
   if(!PositionSelectByTicket(ticket)) return;
   if(!trade.PositionClose(ticket, 10))
      Print("CloseByTicket failed: ", ticket, " ", trade.ResultRetcodeDescription());
}

//+------------------------------------------------------------------+
//| Close all EA positions on this symbol                            |
//+------------------------------------------------------------------+
void CloseAll()
{
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL) != _Symbol) continue;
      if((ulong)PositionGetInteger(POSITION_MAGIC) != (ulong)Magic) continue;
      if(!trade.PositionClose(ticket, 10))
         Print("CloseAll failed: ", ticket, " ", trade.ResultRetcodeDescription());
   }
}

//+------------------------------------------------------------------+
//| Close only profitable positions                                   |
//+------------------------------------------------------------------+
void CloseProfitable()
{
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL) != _Symbol) continue;
      if((ulong)PositionGetInteger(POSITION_MAGIC) != (ulong)Magic) continue;
      double p = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
      if(p > 0)
      {
         if(!trade.PositionClose(ticket, 10))
            Print("CloseProfitable failed: ", ticket, " ", trade.ResultRetcodeDescription());
      }
   }
}

//+------------------------------------------------------------------+
//| Count open positions: type = -1 (all), POSITION_TYPE_BUY/SELL   |
//+------------------------------------------------------------------+
int CountPositions(int type)
{
   int count = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL) != _Symbol) continue;
      if((ulong)PositionGetInteger(POSITION_MAGIC) != (ulong)Magic) continue;
      if(type == -1 || (int)PositionGetInteger(POSITION_TYPE) == type) count++;
   }
   return count;
}

//+------------------------------------------------------------------+
//| Total open lots for this EA on this symbol                       |
//+------------------------------------------------------------------+
double TotalLots()
{
   double lots = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL) != _Symbol) continue;
      if((ulong)PositionGetInteger(POSITION_MAGIC) != (ulong)Magic) continue;
      lots += PositionGetDouble(POSITION_VOLUME);
   }
   return lots;
}

//+------------------------------------------------------------------+
//| Current floating profit for this EA on this symbol               |
//+------------------------------------------------------------------+
double CalcProfit(int type)
{
   double total = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL) != _Symbol) continue;
      if((ulong)PositionGetInteger(POSITION_MAGIC) != (ulong)Magic) continue;
      if(type == -1 || (int)PositionGetInteger(POSITION_TYPE) == type)
         total += PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
   }
   return total;
}

//+------------------------------------------------------------------+
//| Current floating profit across ALL symbols for this EA           |
//+------------------------------------------------------------------+
double CalcProfitAll()
{
   double total = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(!PositionSelectByTicket(ticket)) continue;
      if((ulong)PositionGetInteger(POSITION_MAGIC) != (ulong)Magic) continue;
      total += PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
   }
   return total;
}

//+------------------------------------------------------------------+
//| Last position type opened by this EA (open first, then history)  |
//+------------------------------------------------------------------+
int LastType()
{
   int      type = -1;
   datetime dt   = 0;

   // Check open positions first
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL) != _Symbol) continue;
      if((ulong)PositionGetInteger(POSITION_MAGIC) != (ulong)Magic) continue;
      datetime openTime = (datetime)PositionGetInteger(POSITION_TIME);
      if(openTime > dt)
      {
         dt   = openTime;
         type = (int)PositionGetInteger(POSITION_TYPE);
      }
   }
   if(type != -1) return type;

   // Fall back to history (MQL5: select history deals)
   HistorySelect(0, TimeCurrent());
   for(int i = HistoryDealsTotal() - 1; i >= 0; i--)
   {
      ulong dealTicket = HistoryDealGetTicket(i);
      if(HistoryDealGetString(dealTicket, DEAL_SYMBOL) != _Symbol) continue;
      if((ulong)HistoryDealGetInteger(dealTicket, DEAL_MAGIC) != (ulong)Magic) continue;
      if(HistoryDealGetInteger(dealTicket, DEAL_ENTRY) != DEAL_ENTRY_IN) continue;
      datetime dealTime = (datetime)HistoryDealGetInteger(dealTicket, DEAL_TIME);
      if(dealTime > dt)
      {
         dt = dealTime;
         long dealType = HistoryDealGetInteger(dealTicket, DEAL_TYPE);
         type = (dealType == DEAL_TYPE_BUY) ? POSITION_TYPE_BUY : POSITION_TYPE_SELL;
      }
   }
   return type;
}

//+------------------------------------------------------------------+
//| Most recent BUY position open price                              |
//+------------------------------------------------------------------+
double BuyPric()
{
   double price  = 0;
   ulong  newest = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL) != _Symbol) continue;
      if((ulong)PositionGetInteger(POSITION_MAGIC) != (ulong)Magic) continue;
      if((int)PositionGetInteger(POSITION_TYPE) != POSITION_TYPE_BUY) continue;
      if(ticket > newest)
      {
         newest = ticket;
         price  = PositionGetDouble(POSITION_PRICE_OPEN);
      }
   }
   return price;
}

//+------------------------------------------------------------------+
//| Most recent SELL position open price                             |
//+------------------------------------------------------------------+
double SellPric()
{
   double price  = 0;
   ulong  newest = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetString(POSITION_SYMBOL) != _Symbol) continue;
      if((ulong)PositionGetInteger(POSITION_MAGIC) != (ulong)Magic) continue;
      if((int)PositionGetInteger(POSITION_TYPE) != POSITION_TYPE_SELL) continue;
      if(ticket > newest)
      {
         newest = ticket;
         price  = PositionGetDouble(POSITION_PRICE_OPEN);
      }
   }
   return price;
}

//+------------------------------------------------------------------+
//| Closed trade profit within a time window (for panel display)     |
//+------------------------------------------------------------------+
double ClosedProfitSince(datetime since)
{
   double total = 0;
   HistorySelect(since, TimeCurrent());
   for(int i = HistoryDealsTotal() - 1; i >= 0; i--)
   {
      ulong dealTicket = HistoryDealGetTicket(i);
      if((ulong)HistoryDealGetInteger(dealTicket, DEAL_MAGIC) != (ulong)Magic) continue;
      if(HistoryDealGetInteger(dealTicket, DEAL_ENTRY) != DEAL_ENTRY_OUT) continue;
      total += HistoryDealGetDouble(dealTicket, DEAL_PROFIT)
             + HistoryDealGetDouble(dealTicket, DEAL_SWAP)
             + HistoryDealGetDouble(dealTicket, DEAL_COMMISSION);
   }
   return total;
}

//+------------------------------------------------------------------+
//| Info panel draw / update                                         |
//+------------------------------------------------------------------+
void DrawInfoPanel(double ddRatio, double balance, double equity, double ask, double bid)
{
   // Throttle expensive history scans to once per second
   if(TimeCurrent() - lastInfoCalc >= 1)
   {
      MqlDateTime mdt;
      TimeToStruct(TimeCurrent(), mdt);
      // Build midnight of today
      MqlDateTime midnightToday = mdt;
      midnightToday.hour = 0; midnightToday.min = 0; midnightToday.sec = 0;
      datetime today     = StructToTime(midnightToday);
      MqlDateTime midYest = midnightToday;
      midYest.day -= 1;
      datetime yesterday = StructToTime(midYest);
      MqlDateTime midWeek = midnightToday;
      midWeek.day -= (mdt.day_of_week == 0 ? 6 : mdt.day_of_week - 1);
      datetime weekStart = StructToTime(midWeek);
      MqlDateTime midMonth = midnightToday;
      midMonth.day = 1;
      datetime monthStart = StructToTime(midMonth);

      cachedProfitAll   = CalcProfitAll();
      cachedProfitDay   = ClosedProfitSince(today);
      cachedProfitYest  = ClosedProfitSince(yesterday) - cachedProfitDay;
      cachedProfitWeek  = ClosedProfitSince(weekStart);
      cachedProfitMonth = ClosedProfitSince(monthStart);
      lastInfoCalc = TimeCurrent();
   }

   // Background panel
   RectLabelCreate("INFO_fon", 220, 20, 210, 255, FonColor);

   // Static labels (ObjectCreate returns false if already exists — harmless)
   PutLabel("INFO_LOGO",  165, 24,  "SCALPER CASH MAKER v3");
   PutLabel("INFO_Sep1",  215, 27,  "___________________________");
   PutLabel_("INFO_h1",   215, 45,  "Account Information");
   PutLabel("INFO_Sep2",  215, 47,  "___________________________");
   PutLabel("INFO_L1",    215, 65,  "Min Stop Level:");
   PutLabel("INFO_L2",    215, 80,  "Profit % Balance:");
   PutLabel("INFO_L3",    215, 95,  "Balance:");
   PutLabel("INFO_L4",    215, 110, "Equity:");
   PutLabel("INFO_L5",    215, 125, "Drawdown %:");
   PutLabel("INFO_Sep3",  215, 127, "___________________________");
   PutLabel_("INFO_h2",   215, 145, "Profit Summary");
   PutLabel("INFO_Sep4",  215, 147, "___________________________");
   PutLabel("INFO_L6",    215, 162, "Pair (open):");
   PutLabel("INFO_L7",    215, 177, "All pairs (open):");
   PutLabel("INFO_L8",    215, 192, "Today (closed):");
   PutLabel("INFO_L9",    215, 207, "Yesterday:");
   PutLabel("INFO_L10",   215, 222, "This week:");
   PutLabel("INFO_L11",   215, 237, "This month:");

   // Dynamic data — update text
   long stopLvl = SymbolInfoInteger(_Symbol, SYMBOL_TRADE_STOPS_LEVEL);
   ObjectSetString(0, "INFO_D1",  OBJPROP_TEXT, IntegerToString(stopLvl));
   ObjectSetString(0, "INFO_D2",  OBJPROP_TEXT, DoubleToString(NewProfProc, 2) + "%");
   ObjectSetString(0, "INFO_D3",  OBJPROP_TEXT, DoubleToString(balance, 2));
   ObjectSetString(0, "INFO_D4",  OBJPROP_TEXT, DoubleToString(equity, 2));
   ObjectSetString(0, "INFO_D5",  OBJPROP_TEXT, DoubleToString(ddRatio, 2) + "%");
   ObjectSetString(0, "INFO_D6",  OBJPROP_TEXT, DoubleToString(CalcProfit(-1), 2));
   ObjectSetString(0, "INFO_D7",  OBJPROP_TEXT, DoubleToString(cachedProfitAll, 2));
   ObjectSetString(0, "INFO_D8",  OBJPROP_TEXT, DoubleToString(cachedProfitDay, 2));
   ObjectSetString(0, "INFO_D9",  OBJPROP_TEXT, DoubleToString(cachedProfitYest, 2));
   ObjectSetString(0, "INFO_D10", OBJPROP_TEXT, DoubleToString(cachedProfitWeek, 2));
   ObjectSetString(0, "INFO_D11", OBJPROP_TEXT, DoubleToString(cachedProfitMonth, 2));

   // Create data labels (first call only; subsequent calls update via SetString above)
   PutLabel_("INFO_D1",  85, 65,  IntegerToString(stopLvl));
   PutLabel_("INFO_D2",  85, 80,  DoubleToString(NewProfProc, 2) + "%");
   PutLabel_("INFO_D3",  85, 95,  DoubleToString(balance, 2));
   PutLabel_("INFO_D4",  85, 110, DoubleToString(equity, 2));
   PutLabel_("INFO_D5",  85, 125, DoubleToString(ddRatio, 2) + "%");
   PutLabel_("INFO_D6",  85, 162, DoubleToString(CalcProfit(-1), 2));
   PutLabel_("INFO_D7",  85, 177, DoubleToString(cachedProfitAll, 2));
   PutLabel_("INFO_D8",  85, 192, DoubleToString(cachedProfitDay, 2));
   PutLabel_("INFO_D9",  85, 207, DoubleToString(cachedProfitYest, 2));
   PutLabel_("INFO_D10", 85, 222, DoubleToString(cachedProfitWeek, 2));
   PutLabel_("INFO_D11", 85, 237, DoubleToString(cachedProfitMonth, 2));

   // Manual trade buttons
   PutButton("TRADEs_B", 220, 257, "BUY  " + DoubleToString(Lot, 2),  105, 22, FonButtonBuy,  TextButtonBS);
   PutButton("TRADEs_S", 113, 257, "SELL " + DoubleToString(Lot, 2),  105, 22, FonButtonSell, TextButtonBS);
}

//+------------------------------------------------------------------+
//| Remove all panel objects on deinit                               |
//+------------------------------------------------------------------+
void CleanupPanel()
{
   string names[] = {
      "INFO_fon","INFO_LOGO","INFO_Sep1","INFO_h1","INFO_Sep2",
      "INFO_L1","INFO_L2","INFO_L3","INFO_L4","INFO_L5",
      "INFO_Sep3","INFO_h2","INFO_Sep4",
      "INFO_L6","INFO_L7","INFO_L8","INFO_L9","INFO_L10","INFO_L11",
      "INFO_D1","INFO_D2","INFO_D3","INFO_D4","INFO_D5",
      "INFO_D6","INFO_D7","INFO_D8","INFO_D9","INFO_D10","INFO_D11",
      "TRADEs_B","TRADEs_S"
   };
   for(int i = 0; i < ArraySize(names); i++)
      ObjectDelete(0, names[i]);
}

//+------------------------------------------------------------------+
//| Create horizontal line                                           |
//+------------------------------------------------------------------+
bool HLineCreate(string name, double price, color clr)
{
   if(price == 0) price = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   ObjectDelete(0, name);
   ResetLastError();
   if(!ObjectCreate(0, name, OBJ_HLINE, 0, 0, price))
   {
      Print(__FUNCTION__, ": Failed. Error=", GetLastError());
      return false;
   }
   ObjectSetInteger(0, name, OBJPROP_COLOR,     clr);
   ObjectSetInteger(0, name, OBJPROP_STYLE,     STYLE_SOLID);
   ObjectSetInteger(0, name, OBJPROP_WIDTH,     2);
   ObjectSetInteger(0, name, OBJPROP_BACK,      false);
   ObjectSetInteger(0, name, OBJPROP_SELECTABLE,true);
   ObjectSetInteger(0, name, OBJPROP_SELECTED,  true);
   ObjectSetInteger(0, name, OBJPROP_HIDDEN,    false);
   return true;
}

//+------------------------------------------------------------------+
//| Move horizontal line                                             |
//+------------------------------------------------------------------+
bool HLineMove(string name, double price)
{
   if(price == 0) price = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   ResetLastError();
   if(!ObjectMove(0, name, 0, 0, price))
   {
      Print(__FUNCTION__, ": Failed. Error=", GetLastError());
      return false;
   }
   return true;
}

//+------------------------------------------------------------------+
//| UI helpers                                                        |
//+------------------------------------------------------------------+
void PutLabel(string name, int x, int y, string text)
{
   if(ObjectFind(0, name) < 0)
   {
      ObjectCreate(0, name, OBJ_LABEL, 0, 0, 0);
      ObjectSetInteger(0, name, OBJPROP_CORNER,   CORNER_RIGHT_UPPER);
      ObjectSetString(0,  name, OBJPROP_FONT,     "Arial");
      ObjectSetInteger(0, name, OBJPROP_FONTSIZE, FontSizeInfo);
      ObjectSetInteger(0, name, OBJPROP_COLOR,    TextColor);
      ObjectSetInteger(0, name, OBJPROP_HIDDEN,   false);
      ObjectSetInteger(0, name, OBJPROP_BACK,     false);
   }
   ObjectSetInteger(0, name, OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, name, OBJPROP_YDISTANCE, y);
   ObjectSetString(0,  name, OBJPROP_TEXT,      text);
}

void PutLabel_(string name, int x, int y, string text)
{
   if(ObjectFind(0, name) < 0)
   {
      ObjectCreate(0, name, OBJ_LABEL, 0, 0, 0);
      ObjectSetInteger(0, name, OBJPROP_CORNER,   CORNER_RIGHT_UPPER);
      ObjectSetString(0,  name, OBJPROP_FONT,     "Arial");
      ObjectSetInteger(0, name, OBJPROP_FONTSIZE, FontSizeInfo);
      ObjectSetInteger(0, name, OBJPROP_COLOR,    InfoDataColor);
      ObjectSetInteger(0, name, OBJPROP_HIDDEN,   false);
      ObjectSetInteger(0, name, OBJPROP_BACK,     false);
   }
   ObjectSetInteger(0, name, OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, name, OBJPROP_YDISTANCE, y);
   ObjectSetString(0,  name, OBJPROP_TEXT,      text);
}

void RectLabelCreate(string name, int x, int y, int w, int h, color bg)
{
   if(ObjectFind(0, name) < 0)
      ObjectCreate(0, name, OBJ_RECTANGLE_LABEL, 0, 0, 0);
   ObjectSetInteger(0, name, OBJPROP_XDISTANCE,  x);
   ObjectSetInteger(0, name, OBJPROP_YDISTANCE,  y);
   ObjectSetInteger(0, name, OBJPROP_XSIZE,      w);
   ObjectSetInteger(0, name, OBJPROP_YSIZE,      h);
   ObjectSetInteger(0, name, OBJPROP_BGCOLOR,    bg);
   ObjectSetInteger(0, name, OBJPROP_BORDER_TYPE,BORDER_SUNKEN);
   ObjectSetInteger(0, name, OBJPROP_CORNER,     CORNER_RIGHT_UPPER);
   ObjectSetInteger(0, name, OBJPROP_COLOR,      clrBlue);
   ObjectSetInteger(0, name, OBJPROP_WIDTH,      1);
   ObjectSetInteger(0, name, OBJPROP_BACK,       false);
   ObjectSetInteger(0, name, OBJPROP_HIDDEN,     false);
}

void PutButton(string name, int x, int y, string text, int bw, int bh, color bg, color fg)
{
   if(ObjectFind(0, name) < 0)
      ObjectCreate(0, name, OBJ_BUTTON, 0, 0, 0);
   ObjectSetInteger(0, name, OBJPROP_XDISTANCE,   x);
   ObjectSetInteger(0, name, OBJPROP_YDISTANCE,   y);
   ObjectSetInteger(0, name, OBJPROP_XSIZE,       bw);
   ObjectSetInteger(0, name, OBJPROP_YSIZE,       bh);
   ObjectSetInteger(0, name, OBJPROP_CORNER,      CORNER_RIGHT_UPPER);
   ObjectSetString(0,  name, OBJPROP_TEXT,        text);
   ObjectSetString(0,  name, OBJPROP_FONT,        "Arial");
   ObjectSetInteger(0, name, OBJPROP_FONTSIZE,    FontSizeInfo);
   ObjectSetInteger(0, name, OBJPROP_COLOR,       fg);
   ObjectSetInteger(0, name, OBJPROP_BGCOLOR,     bg);
   ObjectSetInteger(0, name, OBJPROP_BORDER_COLOR,ButtonBorder);
   ObjectSetInteger(0, name, OBJPROP_HIDDEN,      false);
   ObjectSetInteger(0, name, OBJPROP_BACK,        false);
}
