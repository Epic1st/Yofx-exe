//+------------------------------------------------------------------+
//|                                               TrinitySniper.mq5  |
//|                                          Trinity Traders © 2024  |
//|               Support & Resistance Hedging EA — XAUUSD M1        |
//|                     Converted to MQL5 / MT5                      |
//+------------------------------------------------------------------+
#property copyright "Trinity Traders"
#property link      ""
#property version   "1.80"

#include <Trade\Trade.mqh>
#include <Trade\PositionInfo.mqh>

CTrade  trade;

//==================================================================//
//                        INPUT PARAMETERS                          //
//==================================================================//

input string  __Core__                = "=== Core Settings ===";
input double  InitialLotSize          = 0.01;
input double  MartingaleMultiplier    = 1.1;     // Applied cumulatively per step
input int     MagicNumber             = 202401;
input int     Slippage                = 30;

input string  __Degressive__          = "=== Degressive Martingale ===";
input bool    EnableDegressive        = true;
input int     DegressiveStartTrade    = 15;
input int     DegressiveStepTrades    = 5;
input double  DegressiveStepSize      = 0.01;
input double  DegressiveMinMultiplier = 1.05;

input string  __ATR__                 = "=== ATR Settings ===";
input int     ATR_Period              = 14;
input double  ATR_ZoneMultiplier      = 0.5;   // Zone half-width = ATR * 0.5
input double  ATR_GridMultiplier      = 0.3;   // Grid step = ATR * 0.3

input string  __TP__                  = "=== Take Profit ===";
input double  BaseTPDollars           = 5.0;    // Base $ target for trade 1
input double  TPTradeMultiplier       = 1.1;    // Target multiplier per trade added

input string  __SR__                  = "=== Support & Resistance ===";
input int     SR_Lookback             = 30;

input string  __Grid__                = "=== Grid Settings ===";
input bool    EnableGrid              = true;

input string  __Display__             = "=== Display Settings ===";
input color   ResistanceColor         = clrRed;
input color   SupportColor            = clrDodgerBlue;
input bool    ShowStats               = true;

//==================================================================//
//                         GLOBAL VARIABLES                         //
//==================================================================//

double   g_ResHigh        = 0;
double   g_ResLow         = 0;
double   g_SupHigh        = 0;
double   g_SupLow         = 0;
double   g_ATR            = 0;

int      g_SequenceID     = 0;
int      g_TradeCount     = 0;
bool     g_InSequence     = false;
int      g_OriginalDir    = 0;
double   g_AnchorBuy      = 0;
double   g_AnchorSell     = 0;
int      g_GridBuyLevel   = 0;
int      g_GridSellLevel  = 0;
double   g_LastGridBuyPrice  = 0;
double   g_LastGridSellPrice = 0;

bool     g_ZoneLocked     = false;
int      g_ZoneLockDir    = 0;

datetime g_LastCandleTime = 0;

double   g_RealLastLot    = 0;
double   g_FixedTargetUSD = 0;

int      g_ATR_Handle     = INVALID_HANDLE;

#define OBJ_RES_BOX   "TS_ResBox"
#define OBJ_SUP_BOX   "TS_SupBox"
#define OBJ_WATERMARK "TS_Watermark"

//==================================================================//
//               TIME FILTER — no new sequences after 20:00 SAST   //
//  SAST = UTC+2, so 20:00 SAST = 18:00 UTC (hour >= 18)           //
//==================================================================//
bool IsNewSequenceAllowed()
{
   MqlDateTime dt;
   TimeToStruct(TimeGMT(), dt);   // always UTC regardless of broker offset
   // Block new sequences from 18:00 UTC (20:00 SAST) onwards
   if(dt.hour >= 18) return(false);
   return(true);
}

//==================================================================//
//                             INIT                                 //
//==================================================================//
int OnInit()
{
   trade.SetExpertMagicNumber(MagicNumber);
   trade.SetDeviationInPoints(Slippage);

   g_ATR_Handle = iATR(_Symbol, PERIOD_CURRENT, ATR_Period);
   if(g_ATR_Handle == INVALID_HANDLE)
   {
      Print("Trinity Sniper: Failed to create ATR handle. Error=", GetLastError());
      return(INIT_FAILED);
   }

   ChartSetInteger(0, CHART_COLOR_BACKGROUND,  clrSilver);
   ChartSetInteger(0, CHART_COLOR_FOREGROUND,  clrBlack);
   ChartSetInteger(0, CHART_COLOR_GRID,        C'180,180,180');
   ChartSetInteger(0, CHART_COLOR_CANDLE_BULL, clrWhite);
   ChartSetInteger(0, CHART_COLOR_CANDLE_BEAR, clrBlack);
   ChartSetInteger(0, CHART_COLOR_CHART_UP,    clrWhite);
   ChartSetInteger(0, CHART_COLOR_CHART_DOWN,  clrBlack);

   DrawWatermark();
   Print("Trinity Sniper v1.80 (MQL5) Initialized.");
   return(INIT_SUCCEEDED);
}

void OnDeinit(const int reason)
{
   if(g_ATR_Handle != INVALID_HANDLE)
      IndicatorRelease(g_ATR_Handle);

   DeletePanelRows();
   if(ObjectFind(0, OBJ_RES_BOX)   >= 0) ObjectDelete(0, OBJ_RES_BOX);
   if(ObjectFind(0, OBJ_SUP_BOX)   >= 0) ObjectDelete(0, OBJ_SUP_BOX);
   if(ObjectFind(0, OBJ_WATERMARK) >= 0) ObjectDelete(0, OBJ_WATERMARK);
}

//==================================================================//
//                          MAIN TICK                               //
//==================================================================//
void OnTick()
{
   if(g_InSequence)
   {
      CheckNetProfitTarget();
      if(EnableGrid)
         ManageGrid();
   }

   if(ShowStats) DrawStatsPanel();

   // Candle-close detection using iTime
   datetime currentCandleTime = iTime(_Symbol, PERIOD_CURRENT, 0);
   if(currentCandleTime == g_LastCandleTime) return;
   g_LastCandleTime = currentCandleTime;

   // Refresh ATR on new candle
   double atrBuf[];
   ArraySetAsSeries(atrBuf, true);
   if(CopyBuffer(g_ATR_Handle, 0, 1, 1, atrBuf) <= 0) return;
   g_ATR = atrBuf[0];
   if(g_ATR <= 0) return;

   if(!g_InSequence)
   {
      CalculateSRZones();
      DrawSRZones();
   }

   ManageSequenceState();
   CheckEntryConditions();
}

//==================================================================//
//          LOT SIZE HELPERS — correct 1.1x compounding            //
//==================================================================//

double RoundLot(double rawLot)
{
   double step   = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
   double minLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double maxLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
   if(step <= 0) step = 0.01;

   double rounded = MathFloor(rawLot / step + 0.5) * step;
   rounded = NormalizeDouble(rounded, 2);
   if(rounded < minLot) rounded = minLot;
   if(rounded > maxLot) rounded = maxLot;
   return(rounded);
}

double GetCurrentMultiplier()
{
   if(!EnableDegressive || g_TradeCount < DegressiveStartTrade)
      return(MartingaleMultiplier);
   int    steps   = (g_TradeCount - DegressiveStartTrade) / DegressiveStepTrades;
   double reduced = MartingaleMultiplier - steps * DegressiveStepSize;
   if(reduced < DegressiveMinMultiplier) reduced = DegressiveMinMultiplier;
   return(reduced);
}

double NextLot()
{
   if(g_RealLastLot <= 0)
      g_RealLastLot = InitialLotSize;
   else
      g_RealLastLot *= GetCurrentMultiplier();
   return(RoundLot(g_RealLastLot));
}

//==================================================================//
//          NET PROFIT TARGET — every tick, closes ALL together     //
//==================================================================//
void CheckNetProfitTarget()
{
   int    count = 0;
   double netPL = 0;

   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;
      if(PositionGetString(POSITION_SYMBOL)   != _Symbol)              continue;
      if((int)PositionGetInteger(POSITION_MAGIC) != MagicNumber)       continue;
      netPL += PositionGetDouble(POSITION_PROFIT)
             + PositionGetDouble(POSITION_SWAP);
      count++;
   }

   if(count == 0) return;

   double targetUSD = BaseTPDollars * MathPow(TPTradeMultiplier, g_TradeCount - 1);
   g_FixedTargetUSD = targetUSD;

   if(netPL >= targetUSD)
   {
      Print("Trinity Sniper: TP HIT  P&L=$", DoubleToString(netPL, 2),
            "  Target=$",  DoubleToString(targetUSD, 2),
            "  Trades=",   count);
      CloseAllSequenceTrades();
   }
}

//==================================================================//
//                   CLOSE ALL SEQUENCE TRADES                      //
//==================================================================//
void CloseAllSequenceTrades()
{
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;
      if(PositionGetString(POSITION_SYMBOL)      != _Symbol)        continue;
      if((int)PositionGetInteger(POSITION_MAGIC) != MagicNumber)    continue;

      if(!trade.PositionClose(ticket))
         Print("Trinity Sniper: Close FAILED ticket=", ticket,
               " err=", GetLastError());
   }

   ResetSequence();
   CalculateSRZones();
   DrawSRZones();
}

void ResetSequence()
{
   g_SequenceID++;
   g_InSequence        = false;
   g_TradeCount        = 0;
   g_ZoneLocked        = false;
   g_OriginalDir       = 0;
   g_AnchorBuy         = 0;
   g_AnchorSell        = 0;
   g_GridBuyLevel      = 0;
   g_GridSellLevel     = 0;
   g_RealLastLot       = 0;
   g_FixedTargetUSD    = 0;
   g_LastGridBuyPrice  = 0;
   g_LastGridSellPrice = 0;
}

//==================================================================//
//              CALCULATE SUPPORT & RESISTANCE ZONES               //
//==================================================================//
void CalculateSRZones()
{
   double highBuf[], lowBuf[];
   ArraySetAsSeries(highBuf, true);
   ArraySetAsSeries(lowBuf,  true);

   if(CopyHigh(_Symbol, PERIOD_CURRENT, 1, SR_Lookback, highBuf) <= 0) return;
   if(CopyLow (_Symbol, PERIOD_CURRENT, 1, SR_Lookback, lowBuf)  <= 0) return;

   double highestHigh = highBuf[ArrayMaximum(highBuf)];
   double lowestLow   = lowBuf [ArrayMinimum(lowBuf)];

   double hw  = g_ATR * ATR_ZoneMultiplier;
   g_ResHigh  = highestHigh + hw;
   g_ResLow   = highestHigh - hw;
   g_SupHigh  = lowestLow   + hw;
   g_SupLow   = lowestLow   - hw;
}

void DrawSRZones()
{
   datetime t1 = iTime(_Symbol, PERIOD_CURRENT, SR_Lookback);
   datetime t2 = iTime(_Symbol, PERIOD_CURRENT, 0) + PeriodSeconds() * 20;
   DrawZoneBox(OBJ_RES_BOX, t1, t2, g_ResHigh, g_ResLow, ResistanceColor);
   DrawZoneBox(OBJ_SUP_BOX, t1, t2, g_SupHigh, g_SupLow, SupportColor);
}

void DrawZoneBox(string name, datetime t1, datetime t2, double hi, double lo, color clr)
{
   if(ObjectFind(0, name) >= 0) ObjectDelete(0, name);
   ObjectCreate(0, name, OBJ_RECTANGLE, 0, t1, hi, t2, lo);
   ObjectSetInteger(0, name, OBJPROP_COLOR,      clr);
   ObjectSetInteger(0, name, OBJPROP_STYLE,      STYLE_SOLID);
   ObjectSetInteger(0, name, OBJPROP_WIDTH,      1);
   ObjectSetInteger(0, name, OBJPROP_BACK,       true);
   ObjectSetInteger(0, name, OBJPROP_FILL,       true);
   ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
}

//==================================================================//
//                   MANAGE SEQUENCE STATE                          //
//==================================================================//
void ManageSequenceState()
{
   if(g_InSequence && CountSequenceTrades() == 0)
   {
      Print("Trinity Sniper: Sequence ", g_SequenceID, " ended externally.");
      ResetSequence();
      CalculateSRZones();
      DrawSRZones();
   }
}

//==================================================================//
//                   CHECK ENTRY CONDITIONS                         //
//==================================================================//
void CheckEntryConditions()
{
   double closeBuf[];
   ArraySetAsSeries(closeBuf, true);
   if(CopyClose(_Symbol, PERIOD_CURRENT, 1, 1, closeBuf) <= 0) return;
   double closePrice = closeBuf[0];

   // ---- Initial BUY off support ----
   if(!g_InSequence && !g_ZoneLocked && IsNewSequenceAllowed())
   {
      if(closePrice >= g_SupLow && closePrice <= g_SupHigh)
      {
         g_OriginalDir   = 1;
         g_InSequence    = true;
         g_ZoneLocked    = true;
         g_ZoneLockDir   = 1;
         g_GridBuyLevel  = 0;
         g_GridSellLevel = 0;
         g_TradeCount    = 0;
         g_RealLastLot   = 0;
         double lot      = NextLot();
         g_AnchorBuy     = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
         OpenTrade(ORDER_TYPE_BUY, lot, "TS_Init_Buy");
         return;
      }
   }

   // ---- Initial SELL off resistance ----
   if(!g_InSequence && !g_ZoneLocked && IsNewSequenceAllowed())
   {
      if(closePrice >= g_ResLow && closePrice <= g_ResHigh)
      {
         g_OriginalDir   = -1;
         g_InSequence    = true;
         g_ZoneLocked    = true;
         g_ZoneLockDir   = -1;
         g_GridBuyLevel  = 0;
         g_GridSellLevel = 0;
         g_TradeCount    = 0;
         g_RealLastLot   = 0;
         double lot      = NextLot();
         g_AnchorSell    = SymbolInfoDouble(_Symbol, SYMBOL_BID);
         OpenTrade(ORDER_TYPE_SELL, lot, "TS_Init_Sell");
         return;
      }
   }

   // ---- Breakout hedge trigger (candle close confirms breakout) ----
   if(g_InSequence && g_ZoneLocked)
   {
      if(g_ZoneLockDir == 1 && closePrice < g_SupLow)
      {
         g_ZoneLocked  = false;
         g_AnchorSell  = SymbolInfoDouble(_Symbol, SYMBOL_BID);
         double lot    = NextLot();
         OpenTrade(ORDER_TYPE_SELL, lot, "TS_Hedge_Sell");
      }
      else if(g_ZoneLockDir == -1 && closePrice > g_ResHigh)
      {
         g_ZoneLocked = false;
         g_AnchorBuy  = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
         double lot   = NextLot();
         OpenTrade(ORDER_TYPE_BUY, lot, "TS_Hedge_Buy");
      }
   }
}

//==================================================================//
//                        OPEN TRADE                                //
//==================================================================//
void OpenTrade(ENUM_ORDER_TYPE type, double lots, string comment)
{
   string fullComment = "Trinity Traders | " + comment + " #" + IntegerToString(g_SequenceID);
   bool   ok          = false;

   if(type == ORDER_TYPE_BUY)
      ok = trade.Buy(lots, _Symbol, 0, 0, 0, fullComment);
   else
      ok = trade.Sell(lots, _Symbol, 0, 0, 0, fullComment);

   if(!ok)
   {
      Print("Trinity Sniper: Order FAILED [", comment,
            "] lots=", lots,
            " err=",   GetLastError());
      return;
   }

   g_TradeCount++;

   double newTarget = BaseTPDollars * MathPow(TPTradeMultiplier, g_TradeCount - 1);
   Print("Trinity Sniper: Opened [", comment,
         "] lots=",         DoubleToString(lots, 2),
         " ticket=",        trade.ResultOrder(),
         " mult=",          DoubleToString(GetCurrentMultiplier(), 4),
         " NewTPTarget=$",  DoubleToString(newTarget, 2));
}

//==================================================================//
//          GRID MANAGEMENT — runs every tick                       //
//==================================================================//
void ManageGrid()
{
   if(!g_InSequence) return;
   if(g_ATR <= 0)    return;

   double gridStep = g_ATR * ATR_GridMultiplier;
   if(gridStep <= 0) return;

   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);

   // ── Upward grid from AnchorBuy ────────────────────────────────
   // Uses while() so a gap at market open catches up on ALL missed levels.
   if(g_AnchorBuy > 0)
   {
      double nextBuyThresh = (g_LastGridBuyPrice > 0)
                             ? g_LastGridBuyPrice + gridStep
                             : g_AnchorBuy + gridStep;

      while(ask >= nextBuyThresh)
      {
         double lot = NextLot();
         string cmt = "Trinity Traders | TS_Grid_Buy_L" + IntegerToString(g_GridBuyLevel + 1)
                    + " #" + IntegerToString(g_SequenceID);
         if(trade.Buy(lot, _Symbol, 0, 0, 0, cmt))
         {
            g_GridBuyLevel++;
            g_LastGridBuyPrice = nextBuyThresh;   // step by gridStep, not raw ask
            g_TradeCount++;
            Print("Trinity Sniper: Grid BUY L", g_GridBuyLevel,
                  " @ ", DoubleToString(ask, _Digits),
                  " thresh=", DoubleToString(nextBuyThresh, _Digits),
                  " lots=", DoubleToString(lot, 2));
            nextBuyThresh = g_LastGridBuyPrice + gridStep;   // advance to next level
         }
         else
         {
            Print("Trinity Sniper: Grid BUY failed err=", GetLastError());
            break;   // stop looping on broker error
         }
      }
   }

   // ── Downward grid from AnchorSell ────────────────────────────
   // Uses while() so a gap at market open catches up on ALL missed levels.
   if(g_AnchorSell > 0)
   {
      double nextSellThresh = (g_LastGridSellPrice > 0)
                              ? g_LastGridSellPrice - gridStep
                              : g_AnchorSell - gridStep;

      while(bid <= nextSellThresh)
      {
         double lot = NextLot();
         string cmt = "Trinity Traders | TS_Grid_Sell_L" + IntegerToString(g_GridSellLevel + 1)
                    + " #" + IntegerToString(g_SequenceID);
         if(trade.Sell(lot, _Symbol, 0, 0, 0, cmt))
         {
            g_GridSellLevel++;
            g_LastGridSellPrice = nextSellThresh;  // step by gridStep, not raw bid
            g_TradeCount++;
            Print("Trinity Sniper: Grid SELL L", g_GridSellLevel,
                  " @ ", DoubleToString(bid, _Digits),
                  " thresh=", DoubleToString(nextSellThresh, _Digits),
                  " lots=", DoubleToString(lot, 2));
            nextSellThresh = g_LastGridSellPrice - gridStep;  // advance to next level
         }
         else
         {
            Print("Trinity Sniper: Grid SELL failed err=", GetLastError());
            break;   // stop looping on broker error
         }
      }
   }
}

//==================================================================//
//                        HELPER FUNCTIONS                          //
//==================================================================//

int CountSequenceTrades()
{
   int c = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;
      if(PositionGetString(POSITION_SYMBOL)      != _Symbol)     continue;
      if((int)PositionGetInteger(POSITION_MAGIC) != MagicNumber) continue;
      c++;
   }
   return(c);
}

double GetTotalLots()
{
   double t = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;
      if(PositionGetString(POSITION_SYMBOL)      != _Symbol)     continue;
      if((int)PositionGetInteger(POSITION_MAGIC) != MagicNumber) continue;
      t += PositionGetDouble(POSITION_VOLUME);
   }
   return(t);
}

double GetNetFloatPL()
{
   double pl = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;
      if(PositionGetString(POSITION_SYMBOL)      != _Symbol)     continue;
      if((int)PositionGetInteger(POSITION_MAGIC) != MagicNumber) continue;
      pl += PositionGetDouble(POSITION_PROFIT)
          + PositionGetDouble(POSITION_SWAP);
   }
   return(pl);
}

int GetHistoryCount()
{
   HistorySelect(0, TimeCurrent());
   int c = 0;
   int total = HistoryDealsTotal();
   for(int i = 0; i < total; i++)
   {
      ulong ticket = HistoryDealGetTicket(i);
      if(ticket == 0) continue;
      if(HistoryDealGetString(ticket, DEAL_SYMBOL)          != _Symbol)     continue;
      if((int)HistoryDealGetInteger(ticket, DEAL_MAGIC)     != MagicNumber) continue;
      if((ENUM_DEAL_ENTRY)HistoryDealGetInteger(ticket, DEAL_ENTRY) != DEAL_ENTRY_OUT) continue;
      c++;
   }
   return(c);
}

double GetHistoryProfit()
{
   HistorySelect(0, TimeCurrent());
   double p = 0;
   int total = HistoryDealsTotal();
   for(int i = 0; i < total; i++)
   {
      ulong ticket = HistoryDealGetTicket(i);
      if(ticket == 0) continue;
      if(HistoryDealGetString(ticket, DEAL_SYMBOL)          != _Symbol)     continue;
      if((int)HistoryDealGetInteger(ticket, DEAL_MAGIC)     != MagicNumber) continue;
      p += HistoryDealGetDouble(ticket, DEAL_PROFIT)
         + HistoryDealGetDouble(ticket, DEAL_SWAP)
         + HistoryDealGetDouble(ticket, DEAL_COMMISSION);
   }
   return(p);
}

//==================================================================//
//                          WATERMARK                               //
//==================================================================//
void DrawWatermark()
{
   if(ObjectFind(0, OBJ_WATERMARK) >= 0) ObjectDelete(0, OBJ_WATERMARK);
   ObjectCreate(0, OBJ_WATERMARK, OBJ_LABEL, 0, 0, 0);
   ObjectSetString(0,  OBJ_WATERMARK, OBJPROP_TEXT,       "TRINITY TRADERS");
   ObjectSetInteger(0, OBJ_WATERMARK, OBJPROP_CORNER,     CORNER_LEFT_UPPER);
   ObjectSetInteger(0, OBJ_WATERMARK, OBJPROP_XDISTANCE,  200);
   ObjectSetInteger(0, OBJ_WATERMARK, OBJPROP_YDISTANCE,  220);
   ObjectSetInteger(0, OBJ_WATERMARK, OBJPROP_FONTSIZE,   48);
   ObjectSetString(0,  OBJ_WATERMARK, OBJPROP_FONT,       "Arial Bold");
   ObjectSetInteger(0, OBJ_WATERMARK, OBJPROP_COLOR,      C'50,50,50');
   ObjectSetInteger(0, OBJ_WATERMARK, OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, OBJ_WATERMARK, OBJPROP_BACK,       true);
}

//==================================================================//
//                         STATS PANEL                              //
//==================================================================//
void DeletePanelRows()
{
   for(int i = 0; i < 40; i++)
   {
      string nm = "TS_Row_" + IntegerToString(i);
      if(ObjectFind(0, nm) >= 0) ObjectDelete(0, nm);
   }
}

void DrawRow(int row, string txt, color clr)
{
   string nm = "TS_Row_" + IntegerToString(row);
   if(ObjectFind(0, nm) >= 0) ObjectDelete(0, nm);
   ObjectCreate(0, nm, OBJ_LABEL, 0, 0, 0);
   ObjectSetString(0,  nm, OBJPROP_TEXT,       txt);
   ObjectSetInteger(0, nm, OBJPROP_CORNER,     CORNER_LEFT_UPPER);
   ObjectSetInteger(0, nm, OBJPROP_XDISTANCE,  10);
   ObjectSetInteger(0, nm, OBJPROP_YDISTANCE,  15 + row * 13);
   ObjectSetInteger(0, nm, OBJPROP_FONTSIZE,   8);
   ObjectSetString(0,  nm, OBJPROP_FONT,       "Courier New");
   ObjectSetInteger(0, nm, OBJPROP_COLOR,      clr);
   ObjectSetInteger(0, nm, OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, nm, OBJPROP_BACK,       false);
}

void DrawStatsPanel()
{
   int    openTrades  = CountSequenceTrades();
   double floatPL     = GetNetFloatPL();
   double totalLots   = GetTotalLots();
   int    histTrades  = GetHistoryCount();
   double histPL      = GetHistoryProfit();
   double equity      = AccountInfoDouble(ACCOUNT_EQUITY);
   double balance     = AccountInfoDouble(ACCOUNT_BALANCE);
   double curMult     = GetCurrentMultiplier();

   double pointSize   = SymbolInfoDouble(_Symbol, SYMBOL_POINT);
   double tickVal     = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE);
   double tickSz      = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);

   double atrPips     = (g_ATR > 0 && pointSize > 0) ? g_ATR / pointSize : 0;
   double gridPips    = atrPips * ATR_GridMultiplier;
   double zonePips    = atrPips * ATR_ZoneMultiplier;

   int    tc          = (g_TradeCount > 0) ? g_TradeCount : 1;
   double tpTargetUSD = BaseTPDollars * MathPow(TPTradeMultiplier, tc - 1);
   double tpRemaining = tpTargetUSD - floatPL;

   double nextLotDisplay = RoundLot(g_RealLastLot > 0 ? g_RealLastLot * GetCurrentMultiplier() : InitialLotSize);

   color cH  = C'20,20,20';
   color cD  = clrBlack;
   color cDv = C'90,90,90';
   color cG  = C'0,130,0';
   color cR  = clrDarkRed;
   color cY  = C'160,100,0';

   DeletePanelRows();
   int r = 0;
   DrawRow(r++, "-- TRINITY SNIPER v1.80 (MT5) --",                      cH);
   DrawRow(r++, "Sequence ID   : " + IntegerToString(g_SequenceID),       cD);
   DrawRow(r++, "Active Trades : " + IntegerToString(openTrades),          cD);
   DrawRow(r++, "Trade Count   : " + IntegerToString(g_TradeCount),        cD);
   DrawRow(r++, "Total Lots    : " + DoubleToString(totalLots, 2),         cD);
   DrawRow(r++, "--------------------------",                               cDv);
   DrawRow(r++, "Float P/L ($) : " + DoubleToString(floatPL,     2),      (floatPL  >= 0 ? cG : cR));
   DrawRow(r++, "TP Target ($) : " + DoubleToString(tpTargetUSD, 2),       cY);
   DrawRow(r++, "TP Remain ($) : " + DoubleToString(tpRemaining, 2),      (tpRemaining <= 0 ? cG : cR));
   DrawRow(r++, "--------------------------",                               cDv);
   DrawRow(r++, "ATR (pips)    : " + DoubleToString(atrPips,  1),          cD);
   DrawRow(r++, "Zone (pips)   : " + DoubleToString(zonePips, 1),          cD);
   DrawRow(r++, "Grid Stp(pips): " + DoubleToString(gridPips, 1),          cD);
   DrawRow(r++, "Res Zone Hi   : " + DoubleToString(g_ResHigh, _Digits),   cD);
   DrawRow(r++, "Res Zone Lo   : " + DoubleToString(g_ResLow,  _Digits),   cD);
   DrawRow(r++, "Sup Zone Hi   : " + DoubleToString(g_SupHigh, _Digits),   cD);
   DrawRow(r++, "Sup Zone Lo   : " + DoubleToString(g_SupLow,  _Digits),   cD);
   DrawRow(r++, "--------------------------",                               cDv);
   DrawRow(r++, "Lot Mult      : " + DoubleToString(curMult,   4),         cD);
   DrawRow(r++, "Next Lot      : " + DoubleToString(nextLotDisplay, 2),    cD);
   DrawRow(r++, "Degress. At   : " + IntegerToString(DegressiveStartTrade), cD);
   DrawRow(r++, "Min Mult      : " + DoubleToString(DegressiveMinMultiplier, 2), cD);
   DrawRow(r++, "Grid Buy Lvl  : " + IntegerToString(g_GridBuyLevel),      cD);
   DrawRow(r++, "Grid Sell Lvl : " + IntegerToString(g_GridSellLevel),     cD);
   DrawRow(r++, "--------------------------",                               cDv);
   DrawRow(r++, "Hist Trades   : " + IntegerToString(histTrades),           cD);
   DrawRow(r++, "Hist P/L ($)  : " + DoubleToString(histPL,  2),           (histPL  >= 0 ? cG : cR));
   DrawRow(r++, "Balance ($)   : " + DoubleToString(balance, 2),            cD);
   DrawRow(r++, "Equity ($)    : " + DoubleToString(equity,  2),            cD);
   DrawRow(r++, "--------------------------",                               cDv);
}

//+------------------------------------------------------------------+
//                          END OF FILE                              //
//+------------------------------------------------------------------+