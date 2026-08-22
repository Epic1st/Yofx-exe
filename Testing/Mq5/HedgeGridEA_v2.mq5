//+------------------------------------------------------------------+
//|                                              HedgeGridEA_v2.mq5  |
//|                    Bidirectional Hedge Grid with Basket Close     |
//|                                                                   |
//|  LOGIC:                                                           |
//|  - On start: Open Buy + Sell (same lot)                          |
//|  - Price drops X points from last entry:                         |
//|      → Close profitable SELL → Open new Buy+Sell (next lot)      |
//|  - Price rises X points from last entry:                         |
//|      → Close profitable BUY  → Open new Buy+Sell (next lot)      |
//|  - When price reverses back:                                      |
//|      → Close all held (losing) basket at average TP              |
//+------------------------------------------------------------------+
#property copyright "HedgeGrid EA v2"
#property version   "2.00"
#property strict

//-------------------------------------------------------------------
// INPUT PARAMETERS
//-------------------------------------------------------------------
input group            "=== LOT SETTINGS ==="
input double           InpInitialLot     = 0.01;   // Initial Lot
input double           InpLotStep        = 0.01;   // Lot increment per level (linear add)
// Level 0 = InpInitialLot, Level 1 = InpInitialLot + InpLotStep, etc.
// e.g. 0.01 + 0.01 = 0.02, 0.03, 0.04 ...

input group            "=== GRID SETTINGS ==="
input int              InpGridStep       = 300;    // Grid Step (Points from last entry)
input double           InpBasketTP       = 300;    // Basket TP (Points from average price)

input group            "=== ORDER SETTINGS ==="
input int              InpMagicBuy       = 111111; // Magic Number - Buy Side
input int              InpMagicSell      = 222222; // Magic Number - Sell Side
input int              InpSlippage       = 10;     // Slippage (Points)
input string           InpComment        = "HG";   // Comment prefix

input group            "=== DISPLAY ==="
input bool             InpShowDashboard  = true;   // Show info dashboard on chart

//-------------------------------------------------------------------
// GLOBALS
//-------------------------------------------------------------------
double   g_lastEntryPrice = 0;
int      g_level          = 0;     // Current grid level (0 = first)
bool     g_holdBuy        = false; // true = currently holding BUY basket (sell was closed)
bool     g_holdSell       = false; // true = currently holding SELL basket (buy was closed)
double   g_point;
datetime g_lastBarTime    = 0;

//-------------------------------------------------------------------
// INIT
//-------------------------------------------------------------------
int OnInit()
{
   g_point = SymbolInfoDouble(_Symbol, SYMBOL_POINT);
   if(_Digits == 3 || _Digits == 5) g_point *= 10; // 5-digit broker fix

   // Gold / CFD with 2 decimals
   if(_Digits == 2) g_point = 0.01;
   if(_Digits == 1) g_point = 0.1;

   Print("EA Init. Point=", g_point);

   if(CountAll() == 0)
   {
      // Fresh start
      g_level          = 0;
      g_holdBuy        = false;
      g_holdSell       = false;
      OpenHedge(CurrentLot());
   }
   else
   {
      RecoverState();
   }

   DrawDashboard();
   return INIT_SUCCEEDED;
}

//-------------------------------------------------------------------
// DEINIT
//-------------------------------------------------------------------
void OnDeinit(const int reason)
{
   ObjectsDeleteAll(0, "HG_");
}

//-------------------------------------------------------------------
// TICK
//-------------------------------------------------------------------
void OnTick()
{
   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);

   int buyCount  = CountByMagic(InpMagicBuy);
   int sellCount = CountByMagic(InpMagicSell);

   //=== No positions → restart cycle ===
   if(buyCount == 0 && sellCount == 0)
   {
      Print("All closed. Restarting cycle.");
      g_level     = 0;
      g_holdBuy   = false;
      g_holdSell  = false;
      OpenHedge(CurrentLot());
      DrawDashboard();
      return;
   }

   //=== HOLDING BUY BASKET (sells all closed, price was going down) ===
   if(buyCount > 0 && sellCount == 0)
   {
      double avgBuy = GetAvgPrice(InpMagicBuy, POSITION_TYPE_BUY);

      // TP: price now rose above average buy price + basket TP
      if(bid >= avgBuy + InpBasketTP * g_point)
      {
         Print("BUY basket TP hit! AvgBuy=", avgBuy, " Bid=", bid);
         CloseByMagic(InpMagicBuy, POSITION_TYPE_BUY);
         DrawDashboard();
         return;
      }

      // Price dropped further → add new grid level
      if(g_lastEntryPrice > 0 && bid <= g_lastEntryPrice - InpGridStep * g_point)
      {
         Print("Price dropped further (buy-only mode). Adding level ", g_level+1);
         g_level++;
         OpenHedge(CurrentLot());
         DrawDashboard();
         return;
      }
      DrawDashboard();
      return;
   }

   //=== HOLDING SELL BASKET (buys all closed, price was going up) ===
   if(buyCount == 0 && sellCount > 0)
   {
      double avgSell = GetAvgPrice(InpMagicSell, POSITION_TYPE_SELL);

      // TP: price now dropped below average sell price - basket TP
      if(ask <= avgSell - InpBasketTP * g_point)
      {
         Print("SELL basket TP hit! AvgSell=", avgSell, " Ask=", ask);
         CloseByMagic(InpMagicSell, POSITION_TYPE_SELL);
         DrawDashboard();
         return;
      }

      // Price rose further → add new grid level
      if(g_lastEntryPrice > 0 && ask >= g_lastEntryPrice + InpGridStep * g_point)
      {
         Print("Price rose further (sell-only mode). Adding level ", g_level+1);
         g_level++;
         OpenHedge(CurrentLot());
         DrawDashboard();
         return;
      }
      DrawDashboard();
      return;
   }

   //=== BOTH BUY AND SELL OPEN ===
   if(buyCount > 0 && sellCount > 0)
   {
      if(g_lastEntryPrice <= 0)
      {
         // Safety: recover last entry price
         g_lastEntryPrice = GetLatestEntryPrice();
      }

      // --- Price DROPPED → Sell is profitable → close sell, hold buy ---
      if(bid <= g_lastEntryPrice - InpGridStep * g_point)
      {
         Print("Grid DOWN hit. LastEntry=", g_lastEntryPrice, " Bid=", bid,
               " Level=", g_level, " Closing profitable SELL.");
         CloseByMagic(InpMagicSell, POSITION_TYPE_SELL);
         g_level++;
         g_holdBuy  = true;
         g_holdSell = false;
         OpenHedge(CurrentLot());
         DrawDashboard();
         return;
      }

      // --- Price ROSE → Buy is profitable → close buy, hold sell ---
      if(ask >= g_lastEntryPrice + InpGridStep * g_point)
      {
         Print("Grid UP hit. LastEntry=", g_lastEntryPrice, " Ask=", ask,
               " Level=", g_level, " Closing profitable BUY.");
         CloseByMagic(InpMagicBuy, POSITION_TYPE_BUY);
         g_level++;
         g_holdSell = true;
         g_holdBuy  = false;
         OpenHedge(CurrentLot());
         DrawDashboard();
         return;
      }
   }

   // Refresh dashboard every tick
   static datetime lastDraw = 0;
   if(TimeCurrent() - lastDraw >= 1)
   {
      DrawDashboard();
      lastDraw = TimeCurrent();
   }
}

//-------------------------------------------------------------------
// OPEN HEDGE (Buy + Sell same lot)
//-------------------------------------------------------------------
void OpenHedge(double lot)
{
   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);

   // --- BUY ---
   MqlTradeRequest reqB = {};
   MqlTradeResult  resB = {};
   reqB.action       = TRADE_ACTION_DEAL;
   reqB.symbol       = _Symbol;
   reqB.volume       = NormLot(lot);
   reqB.type         = ORDER_TYPE_BUY;
   reqB.price        = ask;
   reqB.deviation    = InpSlippage;
   reqB.magic        = InpMagicBuy;
   reqB.comment      = InpComment + "_B_L" + IntegerToString(g_level);
   reqB.type_filling = ORDER_FILLING_IOC;

   if(!OrderSend(reqB, resB) || resB.retcode != TRADE_RETCODE_DONE)
      Print("BUY failed retcode=", resB.retcode);
   else
      Print("BUY opened lot=", lot, " ask=", ask, " level=", g_level);

   // --- SELL ---
   MqlTradeRequest reqS = {};
   MqlTradeResult  resS = {};
   reqS.action       = TRADE_ACTION_DEAL;
   reqS.symbol       = _Symbol;
   reqS.volume       = NormLot(lot);
   reqS.type         = ORDER_TYPE_SELL;
   reqS.price        = bid;
   reqS.deviation    = InpSlippage;
   reqS.magic        = InpMagicSell;
   reqS.comment      = InpComment + "_S_L" + IntegerToString(g_level);
   reqS.type_filling = ORDER_FILLING_IOC;

   if(!OrderSend(reqS, resS) || resS.retcode != TRADE_RETCODE_DONE)
      Print("SELL failed retcode=", resS.retcode);
   else
      Print("SELL opened lot=", lot, " bid=", bid, " level=", g_level);

   g_lastEntryPrice = ask;
   Print("LastEntry updated=", g_lastEntryPrice);
}

//-------------------------------------------------------------------
// CLOSE ALL by magic + type
//-------------------------------------------------------------------
void CloseByMagic(int magic, ENUM_POSITION_TYPE ptype)
{
   for(int i = PositionsTotal()-1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(!ticket) continue;
      if(PositionGetString(POSITION_SYMBOL)  != _Symbol)       continue;
      if(PositionGetInteger(POSITION_MAGIC)  != magic)         continue;
      if(PositionGetInteger(POSITION_TYPE)   != (int)ptype)    continue;

      MqlTradeRequest req = {};
      MqlTradeResult  res = {};
      req.action       = TRADE_ACTION_DEAL;
      req.symbol       = _Symbol;
      req.volume       = PositionGetDouble(POSITION_VOLUME);
      req.deviation    = InpSlippage;
      req.magic        = magic;
      req.position     = ticket;
      req.type_filling = ORDER_FILLING_IOC;

      if(ptype == POSITION_TYPE_BUY)
      {
         req.type  = ORDER_TYPE_SELL;
         req.price = SymbolInfoDouble(_Symbol, SYMBOL_BID);
      }
      else
      {
         req.type  = ORDER_TYPE_BUY;
         req.price = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
      }

      if(!OrderSend(req, res) || res.retcode != TRADE_RETCODE_DONE)
         Print("Close failed ticket=", ticket, " retcode=", res.retcode);
      else
         Print("Closed ticket=", ticket, " type=", EnumToString(ptype));
   }
}

//-------------------------------------------------------------------
// HELPERS
//-------------------------------------------------------------------
double CurrentLot()
{
   return NormLot(InpInitialLot + InpLotStep * g_level);
}

double NormLot(double lot)
{
   double mn = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double mx = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
   double st = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
   lot = MathMax(mn, MathMin(mx, lot));
   return NormalizeDouble(MathRound(lot/st)*st, 2);
}

int CountByMagic(int magic)
{
   int n = 0;
   for(int i = PositionsTotal()-1; i >= 0; i--)
   {
      ulong t = PositionGetTicket(i);
      if(!t) continue;
      if(PositionGetString(POSITION_SYMBOL) == _Symbol &&
         PositionGetInteger(POSITION_MAGIC) == magic) n++;
   }
   return n;
}

int CountAll()
{
   return CountByMagic(InpMagicBuy) + CountByMagic(InpMagicSell);
}

double GetAvgPrice(int magic, ENUM_POSITION_TYPE ptype)
{
   double wPrice = 0, wVol = 0;
   for(int i = PositionsTotal()-1; i >= 0; i--)
   {
      ulong t = PositionGetTicket(i);
      if(!t) continue;
      if(PositionGetString(POSITION_SYMBOL)  != _Symbol)    continue;
      if(PositionGetInteger(POSITION_MAGIC)  != magic)      continue;
      if(PositionGetInteger(POSITION_TYPE)   != (int)ptype) continue;
      double v = PositionGetDouble(POSITION_VOLUME);
      wPrice  += PositionGetDouble(POSITION_PRICE_OPEN) * v;
      wVol    += v;
   }
   return (wVol > 0) ? wPrice/wVol : 0;
}

double GetTotalLot(int magic, ENUM_POSITION_TYPE ptype)
{
   double tot = 0;
   for(int i = PositionsTotal()-1; i >= 0; i--)
   {
      ulong t = PositionGetTicket(i);
      if(!t) continue;
      if(PositionGetString(POSITION_SYMBOL)  != _Symbol)    continue;
      if(PositionGetInteger(POSITION_MAGIC)  != magic)      continue;
      if(PositionGetInteger(POSITION_TYPE)   != (int)ptype) continue;
      tot += PositionGetDouble(POSITION_VOLUME);
   }
   return tot;
}

double GetTotalProfit(int magic)
{
   double tot = 0;
   for(int i = PositionsTotal()-1; i >= 0; i--)
   {
      ulong t = PositionGetTicket(i);
      if(!t) continue;
      if(PositionGetString(POSITION_SYMBOL) == _Symbol &&
         PositionGetInteger(POSITION_MAGIC) == magic)
         tot += PositionGetDouble(POSITION_PROFIT)
              + PositionGetDouble(POSITION_SWAP);
   }
   return tot;
}

double GetLatestEntryPrice()
{
   double latest = 0;
   datetime latestTime = 0;
   for(int i = PositionsTotal()-1; i >= 0; i--)
   {
      ulong t = PositionGetTicket(i);
      if(!t) continue;
      if(PositionGetString(POSITION_SYMBOL) != _Symbol) continue;
      int m = (int)PositionGetInteger(POSITION_MAGIC);
      if(m != InpMagicBuy && m != InpMagicSell) continue;
      datetime ot = (datetime)PositionGetInteger(POSITION_TIME);
      if(ot > latestTime)
      {
         latestTime = ot;
         latest     = PositionGetDouble(POSITION_PRICE_OPEN);
      }
   }
   return latest;
}

void RecoverState()
{
   // Estimate level from highest lot found
   double maxLot = 0;
   for(int i = PositionsTotal()-1; i >= 0; i--)
   {
      ulong t = PositionGetTicket(i);
      if(!t) continue;
      if(PositionGetString(POSITION_SYMBOL) != _Symbol) continue;
      int m = (int)PositionGetInteger(POSITION_MAGIC);
      if(m != InpMagicBuy && m != InpMagicSell) continue;
      double v = PositionGetDouble(POSITION_VOLUME);
      if(v > maxLot) maxLot = v;
   }
   if(InpLotStep > 0)
      g_level = (int)MathRound((maxLot - InpInitialLot) / InpLotStep);
   if(g_level < 0) g_level = 0;

   g_lastEntryPrice = GetLatestEntryPrice();
   int bc = CountByMagic(InpMagicBuy);
   int sc = CountByMagic(InpMagicSell);
   g_holdBuy  = (bc > 0 && sc == 0);
   g_holdSell = (sc > 0 && bc == 0);
   Print("Recovered: level=", g_level, " lastEntry=", g_lastEntryPrice,
         " holdBuy=", g_holdBuy, " holdSell=", g_holdSell);
}

//-------------------------------------------------------------------
// DASHBOARD
//-------------------------------------------------------------------
void DrawDashboard()
{
   if(!InpShowDashboard) return;

   double bid     = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   int    bc      = CountByMagic(InpMagicBuy);
   int    sc      = CountByMagic(InpMagicSell);
   double avgB    = GetAvgPrice(InpMagicBuy,  POSITION_TYPE_BUY);
   double avgS    = GetAvgPrice(InpMagicSell, POSITION_TYPE_SELL);
   double lotB    = GetTotalLot(InpMagicBuy,  POSITION_TYPE_BUY);
   double lotS    = GetTotalLot(InpMagicSell, POSITION_TYPE_SELL);
   double profB   = GetTotalProfit(InpMagicBuy);
   double profS   = GetTotalProfit(InpMagicSell);
   double nextLot = NormLot(InpInitialLot + InpLotStep * (g_level+1));

   string state = "BOTH";
   if(bc > 0 && sc == 0) state = "HOLDING BUY";
   if(sc > 0 && bc == 0) state = "HOLDING SELL";
   if(bc == 0 && sc == 0) state = "WAITING...";

   string lines[];
   ArrayResize(lines, 11);
   lines[0]  = "╔══ HEDGE GRID EA ══════════╗";
   lines[1]  = "║ State  : " + state;
   lines[2]  = "║ Level  : " + IntegerToString(g_level);
   lines[3]  = "║ Next Lot: " + DoubleToString(nextLot,2);
   lines[4]  = "║ Last Entry: " + DoubleToString(g_lastEntryPrice,_Digits);
   lines[5]  = "║ Grid Step: " + IntegerToString(InpGridStep) + " pts";
   lines[6]  = "║ BUY  (" + IntegerToString(bc) + ")  Lot:" + DoubleToString(lotB,2) +
               "  Avg:" + DoubleToString(avgB,_Digits) + "  P:" + DoubleToString(profB,2);
   lines[7]  = "║ SELL (" + IntegerToString(sc) + ")  Lot:" + DoubleToString(lotS,2) +
               "  Avg:" + DoubleToString(avgS,_Digits) + "  P:" + DoubleToString(profS,2);
   lines[8]  = "║ Total P/L: " + DoubleToString(profB+profS,2);
   lines[9]  = "║ Basket TP: " + IntegerToString((int)InpBasketTP) + " pts from avg";
   lines[10] = "╚═══════════════════════════╝";

   for(int i = 0; i < ArraySize(lines); i++)
   {
      string objName = "HG_dash_" + IntegerToString(i);
      if(ObjectFind(0, objName) < 0)
         ObjectCreate(0, objName, OBJ_LABEL, 0, 0, 0);
      ObjectSetInteger(0, objName, OBJPROP_XDISTANCE, 10);
      ObjectSetInteger(0, objName, OBJPROP_YDISTANCE, 15 + i*16);
      ObjectSetInteger(0, objName, OBJPROP_CORNER, CORNER_LEFT_UPPER);
      ObjectSetString(0,  objName, OBJPROP_TEXT, lines[i]);
      ObjectSetString(0,  objName, OBJPROP_FONT, "Courier New");
      ObjectSetInteger(0, objName, OBJPROP_FONTSIZE, 9);
      color clr = clrWhite;
      if(i == 6) clr = clrAqua;
      if(i == 7) clr = clrOrange;
      if(i == 8) clr = (profB+profS >= 0) ? clrLime : clrRed;
      ObjectSetInteger(0, objName, OBJPROP_COLOR, clr);
      ObjectSetInteger(0, objName, OBJPROP_BACK, false);
      ObjectSetInteger(0, objName, OBJPROP_SELECTABLE, false);
   }
   ChartRedraw(0);
}
//+------------------------------------------------------------------+
