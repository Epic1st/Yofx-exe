//+------------------------------------------------------------------+
//|                                        CrudeOilScalpEA.mq5      |
//|              Professional EA — CRUDE OIL BUY/SELL Scalper       |
//|              v3.0 — Opposite Signal Close + Breakeven on TP1    |
//|              Based on © nicks1008 Pine Script Indicator          |
//+------------------------------------------------------------------+
#property copyright "CrudeOilScalpEA v3 — Professional Edition"
#property link      "https://mozilla.org/MPL/2.0/"
#property version   "3.00"
#property strict

#include <Trade\Trade.mqh>
#include <Trade\PositionInfo.mqh>

CTrade        Trade;
CPositionInfo PosInfo;

//+------------------------------------------------------------------+
//|  INPUTS                                                          |
//+------------------------------------------------------------------+

input group "════════  SIGNAL SETTINGS  ════════"
input bool   UseBuySellSignals   = true;   // Use BUY / SELL Signals
input bool   UseReversalSignals  = true;   // Use Reversal (!) Signals
input bool   CloseOnOppositeSignal = true; // Close trades on opposite signal

input group "════════  INDICATOR PARAMETERS  ════════"
input int    InpRSILength  = 14;    // RSI Length
input int    InpRSIHigh    = 80;    // RSI Overbought Level
input int    InpRSILow     = 20;    // RSI Oversold Level
input int    InpSMALength  = 70;    // SMA Length

input group "════════  STOP LOSS  ════════"
input int    InpSLLookback = 10;    // SL Lookback Candles (swing high/low)

input group "════════  TP1 SETTINGS  ════════"
input bool   UseTP1        = true;   // Enable TP1
input double InpLotTP1     = 0.10;   // Lot Size — TP1
input double InpRR_TP1     = 1.0;    // RR Ratio — TP1  (1.0 = 1:1)

input group "════════  TP2 SETTINGS  ════════"
input bool   UseTP2        = true;   // Enable TP2
input double InpLotTP2     = 0.10;   // Lot Size — TP2
input double InpRR_TP2     = 1.5;    // RR Ratio — TP2  (1.5 = 1:1.5)

input group "════════  TP3 SETTINGS  ════════"
input bool   UseTP3        = true;   // Enable TP3
input double InpLotTP3     = 0.10;   // Lot Size — TP3
input double InpRR_TP3     = 2.0;    // RR Ratio — TP3  (2.0 = 1:2)

input group "════════  BREAKEVEN  ════════"
input bool   UseBreakeven      = true;   // Enable Breakeven (activates when TP1 hits)
input int    InpBreakevenBuffer = 2;      // Breakeven buffer in points (above entry)

input group "════════  DAILY TARGET  ════════"
input bool   UseDailyTarget    = true;    // Enable Daily Profit Target
input double InpDailyTargetUSD = 100.0;  // Daily Profit Target (USD)

input group "════════  EA SETTINGS  ════════"
input int    InpMagicNumber  = 20240101;  // Magic Number
input int    InpSlippage     = 10;        // Slippage (points)
input bool   ShowPanel       = true;      // Show Info Panel

//+------------------------------------------------------------------+
//|  GLOBALS                                                         |
//+------------------------------------------------------------------+
int      h_RSI = INVALID_HANDLE;
int      h_SMA = INVALID_HANDLE;

datetime g_LastBarTime      = 0;
datetime g_TradingDay       = 0;
double   g_DayStartBalance  = 0;
bool     g_DailyTargetHit   = false;

// Track TP1 hit per trade ticket to trigger breakeven on TP2/TP3
// We store entry prices of TP2/TP3 to move SL to BE when TP1 closes
struct TradeSet
  {
   long     ticket_tp1;
   long     ticket_tp2;
   long     ticket_tp3;
   double   entry_price;
   double   original_sl;
   bool     be_applied;
   ENUM_ORDER_TYPE direction;
  };

TradeSet g_TradeSets[];  // array of active trade sets

string PFX = "CSE_";     // Panel object prefix

//+------------------------------------------------------------------+
//|  INIT                                                            |
//+------------------------------------------------------------------+
int OnInit()
  {
   Trade.SetExpertMagicNumber(InpMagicNumber);
   Trade.SetDeviationInPoints(InpSlippage);
   Trade.SetTypeFilling(ORDER_FILLING_IOC);

   h_RSI = iRSI(_Symbol, PERIOD_CURRENT, InpRSILength, PRICE_CLOSE);
   h_SMA = iMA (_Symbol, PERIOD_CURRENT, InpSMALength, 0, MODE_SMA, PRICE_CLOSE);

   if(h_RSI == INVALID_HANDLE || h_SMA == INVALID_HANDLE)
     {
      Alert("CrudeOilScalpEA: Failed to create indicator handles! Error=", GetLastError());
      return INIT_FAILED;
     }

   g_DayStartBalance = AccountInfoDouble(ACCOUNT_BALANCE);
   g_TradingDay      = GetDayStart();

   ArrayResize(g_TradeSets, 0);

   if(ShowPanel) CreatePanel();

   Print("CrudeOilScalpEA v3 initialized. Magic=", InpMagicNumber);
   return INIT_SUCCEEDED;
  }

//+------------------------------------------------------------------+
void OnDeinit(const int reason)
  {
   if(h_RSI != INVALID_HANDLE) IndicatorRelease(h_RSI);
   if(h_SMA != INVALID_HANDLE) IndicatorRelease(h_SMA);
   DeletePanel();
  }

//+------------------------------------------------------------------+
//|  MAIN TICK                                                       |
//+------------------------------------------------------------------+
void OnTick()
  {
   // Always check daily target & update breakeven on every tick
   CheckNewDay();
   if(UseBreakeven) ManageBreakeven();
   if(ShowPanel)    UpdatePanel();

   // New bar gate
   datetime bars[];
   ArraySetAsSeries(bars, true);
   if(CopyTime(_Symbol, PERIOD_CURRENT, 0, 1, bars) != 1) return;
   if(bars[0] == g_LastBarTime) return;
   g_LastBarTime = bars[0];

   // Daily target pause
   if(g_DailyTargetHit) return;

   // Get signals
   SignalResult sig = GetSignal();

   bool anyBuy  = (sig.isBuy  && UseBuySellSignals) || (sig.isRevBuy  && UseReversalSignals);
   bool anySell = (sig.isSell && UseBuySellSignals) || (sig.isRevSell && UseReversalSignals);

   // Close opposite direction trades first
   if(CloseOnOppositeSignal)
     {
      if(anyBuy)  CloseTradesByDirection(ORDER_TYPE_SELL);
      if(anySell) CloseTradesByDirection(ORDER_TYPE_BUY);
     }

   // Open new trades
   if(anyBuy)  OpenTrades(ORDER_TYPE_BUY,  sig.sl);
   if(anySell) OpenTrades(ORDER_TYPE_SELL, sig.sl);
  }

//+------------------------------------------------------------------+
//|  SIGNAL STRUCTURE                                                |
//+------------------------------------------------------------------+
struct SignalResult
  {
   bool   isBuy;
   bool   isSell;
   bool   isRevBuy;
   bool   isRevSell;
   double sl;
  };

//+------------------------------------------------------------------+
//|  SIGNAL LOGIC                                                    |
//+------------------------------------------------------------------+
SignalResult GetSignal()
  {
   SignalResult r;
   r.isBuy = false; r.isSell = false;
   r.isRevBuy = false; r.isRevSell = false;
   r.sl = 0;

   int needed = InpSMALength + InpRSILength + InpSLLookback + 10;

   double rsi[], sma[], op[], hi[], lo[], cl[];
   ArraySetAsSeries(rsi, true);
   ArraySetAsSeries(sma, true);
   ArraySetAsSeries(op,  true);
   ArraySetAsSeries(hi,  true);
   ArraySetAsSeries(lo,  true);
   ArraySetAsSeries(cl,  true);

   if(CopyBuffer(h_RSI, 0, 0, needed, rsi) < needed) return r;
   if(CopyBuffer(h_SMA, 0, 0, needed, sma) < needed) return r;
   if(CopyOpen  (_Symbol, PERIOD_CURRENT, 0, needed, op)  < needed) return r;
   if(CopyHigh  (_Symbol, PERIOD_CURRENT, 0, needed, hi)  < needed) return r;
   if(CopyLow   (_Symbol, PERIOD_CURRENT, 0, needed, lo)  < needed) return r;
   if(CopyClose (_Symbol, PERIOD_CURRENT, 0, needed, cl)  < needed) return r;

   // Index mapping (series = [0] is current open bar):
   // Pine bar[0] = our [1] (just closed confirmed bar)
   // Pine bar[1] = our [2], Pine bar[2] = our [3]

   // --- BUY ---
   // ta.crossover(close[1], sma): close[2]<sma[2] AND close[1]>=sma[1]  => our [3]<sma[3] and [2]>=sma[2]
   // close[1]>open[1] => [2]>[2]  |  high[0]>high[1] => [1]>[2]  |  close[0]>open[0] => [1]>[1]
   bool cross_up = (cl[3] < sma[3]) && (cl[2] >= sma[2]);
   bool isBUY    = cross_up && (cl[2] > op[2]) && (hi[1] > hi[2]) && (cl[1] > op[1]);

   // --- SELL ---
   // ta.crossunder(low[1], sma): low[2]>sma[2] AND low[1]<=sma[1]
   bool cross_dn = (lo[3] > sma[3]) && (lo[2] <= sma[2]);
   bool isSELL   = cross_dn && (cl[2] < op[2]) && (lo[1] < lo[2]) && (cl[1] < op[1]);

   // --- Reversal Down: RSI crossunder overbought level ---
   bool hlrev_s = (rsi[2] >= InpRSIHigh) && (rsi[1] < InpRSIHigh);

   // --- Reversal Up: RSI crossover oversold level + bullish bar ---
   bool llrev_b = (rsi[2] <= InpRSILow)  && (rsi[1] > InpRSILow) && (op[1] < cl[1]);

   r.isBuy     = isBUY;
   r.isSell    = isSELL;
   r.isRevSell = hlrev_s;
   r.isRevBuy  = llrev_b;

   // Compute SL from swing structure
   if(isBUY || llrev_b)
     {
      double swing_low = lo[1];
      for(int k = 1; k <= InpSLLookback && k < needed; k++)
         swing_low = MathMin(swing_low, lo[k]);
      r.sl = swing_low - 2 * _Point;
     }
   else if(isSELL || hlrev_s)
     {
      double swing_high = hi[1];
      for(int k = 1; k <= InpSLLookback && k < needed; k++)
         swing_high = MathMax(swing_high, hi[k]);
      r.sl = swing_high + 2 * _Point;
     }

   return r;
  }

//+------------------------------------------------------------------+
//|  OPEN TRADE SET (up to 3 orders)                                 |
//+------------------------------------------------------------------+
void OpenTrades(ENUM_ORDER_TYPE dir, double sl_price)
  {
   if(g_DailyTargetHit) return;

   double ask   = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double bid   = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   double entry = (dir == ORDER_TYPE_BUY) ? ask : bid;

   double sl_dist = MathAbs(entry - sl_price);
   if(sl_dist < _Point * 5)
     {
      Print("SL distance too small (", sl_dist, "), skipping.");
      return;
     }

   string tag    = (dir == ORDER_TYPE_BUY) ? "CSE_BUY" : "CSE_SELL";
   double sl_n   = NormalizeDouble(sl_price, _Digits);

   TradeSet ts;
   ts.ticket_tp1  = -1;
   ts.ticket_tp2  = -1;
   ts.ticket_tp3  = -1;
   ts.entry_price = entry;
   ts.original_sl = sl_n;
   ts.be_applied  = false;
   ts.direction   = dir;

   // --- TP1 ---
   if(UseTP1 && InpLotTP1 > 0)
     {
      double tp1 = NormalizeDouble(
                      dir == ORDER_TYPE_BUY
                      ? entry + sl_dist * InpRR_TP1
                      : entry - sl_dist * InpRR_TP1,
                      _Digits);
      if(dir == ORDER_TYPE_BUY)
         Trade.Buy (NormalizeLot(InpLotTP1), _Symbol, 0, sl_n, tp1, tag+"_TP1");
      else
         Trade.Sell(NormalizeLot(InpLotTP1), _Symbol, 0, sl_n, tp1, tag+"_TP1");
      ts.ticket_tp1 = Trade.ResultOrder();
     }

   // --- TP2 ---
   if(UseTP2 && InpLotTP2 > 0)
     {
      double tp2 = NormalizeDouble(
                      dir == ORDER_TYPE_BUY
                      ? entry + sl_dist * InpRR_TP2
                      : entry - sl_dist * InpRR_TP2,
                      _Digits);
      if(dir == ORDER_TYPE_BUY)
         Trade.Buy (NormalizeLot(InpLotTP2), _Symbol, 0, sl_n, tp2, tag+"_TP2");
      else
         Trade.Sell(NormalizeLot(InpLotTP2), _Symbol, 0, sl_n, tp2, tag+"_TP2");
      ts.ticket_tp2 = Trade.ResultOrder();
     }

   // --- TP3 ---
   if(UseTP3 && InpLotTP3 > 0)
     {
      double tp3 = NormalizeDouble(
                      dir == ORDER_TYPE_BUY
                      ? entry + sl_dist * InpRR_TP3
                      : entry - sl_dist * InpRR_TP3,
                      _Digits);
      if(dir == ORDER_TYPE_BUY)
         Trade.Buy (NormalizeLot(InpLotTP3), _Symbol, 0, sl_n, tp3, tag+"_TP3");
      else
         Trade.Sell(NormalizeLot(InpLotTP3), _Symbol, 0, sl_n, tp3, tag+"_TP3");
      ts.ticket_tp3 = Trade.ResultOrder();
     }

   // Register the trade set
   int sz = ArraySize(g_TradeSets);
   ArrayResize(g_TradeSets, sz + 1);
   g_TradeSets[sz] = ts;

   Print("Trade set opened | Dir=", EnumToString(dir),
         " Entry=", entry, " SL=", sl_n,
         " TP1=", ts.ticket_tp1, " TP2=", ts.ticket_tp2, " TP3=", ts.ticket_tp3);
  }

//+------------------------------------------------------------------+
//|  BREAKEVEN MANAGEMENT (called every tick)                        |
//|  Logic: when TP1 ticket is no longer open (hit or closed),       |
//|  move SL of TP2 and TP3 to entry + buffer                        |
//+------------------------------------------------------------------+
void ManageBreakeven()
  {
   if(!UseBreakeven) return;

   int sz = ArraySize(g_TradeSets);
   for(int i = 0; i < sz; i++)
     {
      if(g_TradeSets[i].be_applied) continue;

      // Check if TP1 ticket still exists as an open position
      bool tp1_still_open = PositionSelectByTicket(g_TradeSets[i].ticket_tp1);

      // TP1 was placed (ticket != -1) and is now closed/hit
      if(g_TradeSets[i].ticket_tp1 != -1 && !tp1_still_open)
        {
         double entry = g_TradeSets[i].entry_price;
         ENUM_ORDER_TYPE dir = g_TradeSets[i].direction;
         double be_sl;

         if(dir == ORDER_TYPE_BUY)
            be_sl = NormalizeDouble(entry + InpBreakevenBuffer * _Point, _Digits);
         else
            be_sl = NormalizeDouble(entry - InpBreakevenBuffer * _Point, _Digits);

         bool moved = false;

         // Move TP2 SL to BE
         if(g_TradeSets[i].ticket_tp2 != -1 && PositionSelectByTicket(g_TradeSets[i].ticket_tp2))
           {
            double cur_tp = PositionGetDouble(POSITION_TP);
            Trade.PositionModify(g_TradeSets[i].ticket_tp2, be_sl, cur_tp);
            moved = true;
           }

         // Move TP3 SL to BE
         if(g_TradeSets[i].ticket_tp3 != -1 && PositionSelectByTicket(g_TradeSets[i].ticket_tp3))
           {
            double cur_tp = PositionGetDouble(POSITION_TP);
            Trade.PositionModify(g_TradeSets[i].ticket_tp3, be_sl, cur_tp);
            moved = true;
           }

         if(moved)
           {
            g_TradeSets[i].be_applied = true;
            Print("Breakeven applied for set ", i, " | BE SL = ", be_sl);
           }
         else
           {
            // Both TP2 and TP3 already closed too — mark done
            g_TradeSets[i].be_applied = true;
           }
        }
     }

   // Clean up fully closed sets to keep array tidy
   CleanClosedSets();
  }

//+------------------------------------------------------------------+
//|  CLEAN TRADE SETS ARRAY — remove sets where all positions closed |
//+------------------------------------------------------------------+
void CleanClosedSets()
  {
   int sz = ArraySize(g_TradeSets);
   int new_sz = 0;
   TradeSet temp[];
   ArrayResize(temp, sz);

   for(int i = 0; i < sz; i++)
     {
      bool tp1_open = PositionSelectByTicket(g_TradeSets[i].ticket_tp1);
      bool tp2_open = PositionSelectByTicket(g_TradeSets[i].ticket_tp2);
      bool tp3_open = PositionSelectByTicket(g_TradeSets[i].ticket_tp3);

      bool any_open = tp1_open || tp2_open || tp3_open;

      // Also keep sets where TP1 closed but BE not yet applied
      if(any_open || !g_TradeSets[i].be_applied)
        {
         temp[new_sz] = g_TradeSets[i];
         new_sz++;
        }
     }

   ArrayResize(g_TradeSets, new_sz);
   for(int i = 0; i < new_sz; i++)
      g_TradeSets[i] = temp[i];
  }

//+------------------------------------------------------------------+
//|  CLOSE TRADES BY DIRECTION (opposite signal)                     |
//+------------------------------------------------------------------+
void CloseTradesByDirection(ENUM_ORDER_TYPE dir_to_close)
  {
   ENUM_POSITION_TYPE pos_type = (dir_to_close == ORDER_TYPE_BUY)
                                 ? POSITION_TYPE_BUY
                                 : POSITION_TYPE_SELL;

   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      if(PosInfo.SelectByIndex(i))
        {
         if(PosInfo.Magic()  == InpMagicNumber &&
            PosInfo.Symbol() == _Symbol &&
            PosInfo.PositionType() == pos_type)
           {
            Trade.PositionClose(PosInfo.Ticket());
            Print("Closed position #", PosInfo.Ticket(), " on opposite signal.");
           }
        }
     }
  }

//+------------------------------------------------------------------+
//|  DAILY MANAGEMENT                                                |
//+------------------------------------------------------------------+
void CheckNewDay()
  {
   datetime today = GetDayStart();
   if(today != g_TradingDay)
     {
      g_TradingDay      = today;
      g_DayStartBalance = AccountInfoDouble(ACCOUNT_BALANCE);
      g_DailyTargetHit  = false;
      ArrayResize(g_TradeSets, 0);
      Print("New trading day. Balance=", g_DayStartBalance);
     }

   if(UseDailyTarget && !g_DailyTargetHit)
     {
      if(GetDailyPnL() >= InpDailyTargetUSD)
        {
         g_DailyTargetHit = true;
         Print("Daily target hit! PnL=", GetDailyPnL(), " Closing all trades.");
         CloseAllTrades();
        }
     }
  }

void CloseAllTrades()
  {
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      if(PosInfo.SelectByIndex(i))
         if(PosInfo.Magic() == InpMagicNumber && PosInfo.Symbol() == _Symbol)
            Trade.PositionClose(PosInfo.Ticket());
     }
  }

//+------------------------------------------------------------------+
//|  UTILITY                                                         |
//+------------------------------------------------------------------+
datetime GetDayStart()
  {
   MqlDateTime dt;
   TimeToStruct(TimeCurrent(), dt);
   dt.hour = 0; dt.min = 0; dt.sec = 0;
   return StructToTime(dt);
  }

double GetDailyPnL()
  {
   double balance  = AccountInfoDouble(ACCOUNT_BALANCE);
   double floating = GetFloatingPnL();
   return (balance - g_DayStartBalance) + floating;
  }

double GetFloatingPnL()
  {
   double pnl = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
      if(PosInfo.SelectByIndex(i))
         if(PosInfo.Magic() == InpMagicNumber && PosInfo.Symbol() == _Symbol)
            pnl += PosInfo.Profit() + PosInfo.Swap();
   return pnl;
  }

int CountOpenTrades()
  {
   int cnt = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
      if(PosInfo.SelectByIndex(i))
         if(PosInfo.Magic() == InpMagicNumber && PosInfo.Symbol() == _Symbol)
            cnt++;
   return cnt;
  }

int CountBuys()
  {
   int cnt = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
      if(PosInfo.SelectByIndex(i))
         if(PosInfo.Magic() == InpMagicNumber && PosInfo.Symbol() == _Symbol
            && PosInfo.PositionType() == POSITION_TYPE_BUY)
            cnt++;
   return cnt;
  }

int CountSells()
  {
   int cnt = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
      if(PosInfo.SelectByIndex(i))
         if(PosInfo.Magic() == InpMagicNumber && PosInfo.Symbol() == _Symbol
            && PosInfo.PositionType() == POSITION_TYPE_SELL)
            cnt++;
   return cnt;
  }

double NormalizeLot(double lot)
  {
   double min_lot  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double max_lot  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
   double lot_step = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
   lot = MathRound(lot / lot_step) * lot_step;
   return MathMax(min_lot, MathMin(max_lot, lot));
  }

string PeriodToStr(ENUM_TIMEFRAMES tf)
  {
   switch(tf)
     {
      case PERIOD_M1:  return "M1";
      case PERIOD_M3:  return "M3";
      case PERIOD_M5:  return "M5";
      case PERIOD_M15: return "M15";
      case PERIOD_M30: return "M30";
      case PERIOD_H1:  return "H1";
      case PERIOD_H4:  return "H4";
      case PERIOD_D1:  return "D1";
      default:         return "Custom";
     }
  }

//+------------------------------------------------------------------+
//|  PANEL — CREATE                                                  |
//+------------------------------------------------------------------+
void CreatePanel()
  {
   int x=15, y=25, w=272, h=390;

   // Layers
   CreateRect(PFX+"bg",     x,   y,   w,   h,   C'10,12,28',  255);
   CreateRect(PFX+"bdr",    x,   y,   w,   h,   C'45,90,170', 0, true);
   CreateRect(PFX+"hdr",    x,   y,   w,   28,  C'20,50,120', 255);
   CreateRect(PFX+"hdrln",  x,   y+28,w,   2,   C'45,90,170', 255);

   // Title
   CreateLabel(PFX+"title", x+10, y+7, "⚡  CRUDE OIL SCALPER  v3", "Arial Bold", 10, clrWhite);

   int r = y+36, sp = 19;

   // --- Account ---
   CreateLabel(PFX+"sym_l",  x+10, r,      "Symbol",        "Arial", 9, C'110,140,190'); 
   CreateLabel(PFX+"sym_v",  x+140,r,       "---",           "Arial Bold", 9, clrWhite);   r+=sp;
   CreateLabel(PFX+"tf_l",   x+10, r,      "Timeframe",     "Arial", 9, C'110,140,190');
   CreateLabel(PFX+"tf_v",   x+140,r,       "---",           "Arial Bold", 9, clrWhite);   r+=sp;

   CreateRect(PFX+"s1", x+8, r, w-16, 1, C'40,70,140', 255); r+=6;

   CreateLabel(PFX+"bal_l",  x+10, r,      "Balance",       "Arial", 9, C'110,140,190');
   CreateLabel(PFX+"bal_v",  x+140,r,       "---",           "Arial Bold", 9, clrWhite);   r+=sp;
   CreateLabel(PFX+"eq_l",   x+10, r,      "Equity",        "Arial", 9, C'110,140,190');
   CreateLabel(PFX+"eq_v",   x+140,r,       "---",           "Arial Bold", 9, clrWhite);   r+=sp;
   CreateLabel(PFX+"fl_l",   x+10, r,      "Floating P&L",  "Arial", 9, C'110,140,190');
   CreateLabel(PFX+"fl_v",   x+140,r,       "---",           "Arial Bold", 9, clrGray);    r+=sp;
   CreateLabel(PFX+"dp_l",   x+10, r,      "Daily P&L",     "Arial", 9, C'110,140,190');
   CreateLabel(PFX+"dp_v",   x+140,r,       "---",           "Arial Bold", 9, clrGray);    r+=sp;
   CreateLabel(PFX+"tg_l",   x+10, r,      "Daily Target",  "Arial", 9, C'110,140,190');
   CreateLabel(PFX+"tg_v",   x+140,r,       "---",           "Arial Bold", 9, clrGray);    r+=sp;

   CreateRect(PFX+"s2", x+8, r, w-16, 1, C'40,70,140', 255); r+=6;

   // --- Trades ---
   CreateLabel(PFX+"ot_l",   x+10, r,      "Open Trades",   "Arial", 9, C'110,140,190');
   CreateLabel(PFX+"ot_v",   x+140,r,       "0",             "Arial Bold", 9, clrWhite);   r+=sp;
   CreateLabel(PFX+"by_l",   x+10, r,      "  └ Buys",      "Arial", 8, C'80,190,140');
   CreateLabel(PFX+"by_v",   x+140,r,       "0",             "Arial Bold", 8, C'80,190,140'); r+=sp;
   CreateLabel(PFX+"sl_l",   x+10, r,      "  └ Sells",     "Arial", 8, C'210,80,80');
   CreateLabel(PFX+"sl_v",   x+140,r,       "0",             "Arial Bold", 8, C'210,80,80'); r+=sp;

   CreateRect(PFX+"s3", x+8, r, w-16, 1, C'40,70,140', 255); r+=6;

   // --- Signal Filters ---
   CreateLabel(PFX+"sbs_l",  x+10, r, "BuySell Signals",  "Arial", 9, C'110,140,190');
   CreateLabel(PFX+"sbs_v",  x+140,r,  UseBuySellSignals  ? "ENABLED" : "DISABLED",
               "Arial Bold", 9, UseBuySellSignals  ? clrLime : clrOrangeRed);  r+=sp;
   CreateLabel(PFX+"rev_l",  x+10, r, "Reversal Signals", "Arial", 9, C'110,140,190');
   CreateLabel(PFX+"rev_v",  x+140,r,  UseReversalSignals ? "ENABLED" : "DISABLED",
               "Arial Bold", 9, UseReversalSignals ? clrLime : clrOrangeRed);  r+=sp;
   CreateLabel(PFX+"opp_l",  x+10, r, "Opp. Close",       "Arial", 9, C'110,140,190');
   CreateLabel(PFX+"opp_v",  x+140,r,  CloseOnOppositeSignal ? "ON" : "OFF",
               "Arial Bold", 9, CloseOnOppositeSignal ? clrLime : clrOrangeRed); r+=sp;
   CreateLabel(PFX+"be_l",   x+10, r, "Breakeven",        "Arial", 9, C'110,140,190');
   CreateLabel(PFX+"be_v",   x+140,r,  UseBreakeven ? "ON (after TP1)" : "OFF",
               "Arial Bold", 9, UseBreakeven ? clrLime : clrOrangeRed);  r+=sp;

   CreateRect(PFX+"s4", x+8, r, w-16, 1, C'40,70,140', 255); r+=6;

   // --- Status ---
   CreateLabel(PFX+"stat_l", x+10, r, "EA Status",        "Arial", 9, C'110,140,190');
   CreateLabel(PFX+"stat_v", x+140,r,  "ACTIVE",           "Arial Bold", 9, clrLime); r+=sp;

   // --- Config footer ---
   r += 3;
   CreateLabel(PFX+"cfg",  x+10, r,
      StringFormat("SL:%dbars  TP1:%.1f  TP2:%.1f  TP3:%.1f",
                   InpSLLookback, InpRR_TP1, InpRR_TP2, InpRR_TP3),
      "Arial", 8, C'70,100,150');

   ChartRedraw();
  }

//+------------------------------------------------------------------+
//|  PANEL — UPDATE (called every tick)                              |
//+------------------------------------------------------------------+
void UpdatePanel()
  {
   if(!ShowPanel) return;

   double balance  = AccountInfoDouble(ACCOUNT_BALANCE);
   double equity   = AccountInfoDouble(ACCOUNT_EQUITY);
   double floating = GetFloatingPnL();
   double daily    = GetDailyPnL();
   int    total    = CountOpenTrades();
   int    buys     = CountBuys();
   int    sells    = CountSells();
   double target   = InpDailyTargetUSD;
   double pct      = (target > 0) ? daily / target * 100.0 : 0;

   ObjTxt(PFX+"sym_v",  _Symbol);
   ObjTxt(PFX+"tf_v",   PeriodToStr(PERIOD_CURRENT));
   ObjTxt(PFX+"bal_v",  StringFormat("%.2f",  balance));
   ObjTxt(PFX+"eq_v",   StringFormat("%.2f",  equity));
   ObjTxt(PFX+"fl_v",   StringFormat("%+.2f", floating));
   ObjTxt(PFX+"dp_v",   StringFormat("%+.2f", daily));
   ObjTxt(PFX+"tg_v",   UseDailyTarget
                        ? StringFormat("%.2f  (%.0f%%)", target, pct)
                        : "OFF");
   ObjTxt(PFX+"ot_v",   IntegerToString(total));
   ObjTxt(PFX+"by_v",   IntegerToString(buys));
   ObjTxt(PFX+"sl_v",   IntegerToString(sells));
   ObjTxt(PFX+"stat_v", g_DailyTargetHit ? "TARGET HIT — PAUSED" : "ACTIVE");

   // Dynamic colours
   ObjCol(PFX+"fl_v",   floating >= 0 ? clrLime : clrOrangeRed);
   ObjCol(PFX+"dp_v",   daily    >= 0 ? clrLime : clrOrangeRed);
   ObjCol(PFX+"tg_v",   (UseDailyTarget && daily >= target) ? clrGold : C'110,140,190');
   ObjCol(PFX+"stat_v", g_DailyTargetHit ? clrGold : clrLime);

   ChartRedraw();
  }

void DeletePanel()  { ObjectsDeleteAll(0, PFX); ChartRedraw(); }
void ObjTxt(string n, string t) { ObjectSetString (0, n, OBJPROP_TEXT,  t); }
void ObjCol(string n, color  c) { ObjectSetInteger(0, n, OBJPROP_COLOR, c); }

//+------------------------------------------------------------------+
//|  PANEL DRAW HELPERS                                              |
//+------------------------------------------------------------------+
void CreateRect(string name, int x, int y, int w, int h,
                color clr, int alpha, bool border=false)
  {
   ObjectCreate(0, name, OBJ_RECTANGLE_LABEL, 0, 0, 0);
   ObjectSetInteger(0, name, OBJPROP_XDISTANCE,  x);
   ObjectSetInteger(0, name, OBJPROP_YDISTANCE,  y);
   ObjectSetInteger(0, name, OBJPROP_XSIZE,      w);
   ObjectSetInteger(0, name, OBJPROP_YSIZE,      h);
   ObjectSetInteger(0, name, OBJPROP_CORNER,     CORNER_LEFT_UPPER);
   ObjectSetInteger(0, name, OBJPROP_BACK,       false);
   ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, name, OBJPROP_HIDDEN,     true);
   if(border)
     {
      ObjectSetInteger(0, name, OBJPROP_BORDER_TYPE,  BORDER_FLAT);
      ObjectSetInteger(0, name, OBJPROP_COLOR,         clr);
      ObjectSetInteger(0, name, OBJPROP_BGCOLOR,       C'10,12,28');
     }
   else
     {
      ObjectSetInteger(0, name, OBJPROP_BGCOLOR,      clr);
      ObjectSetInteger(0, name, OBJPROP_COLOR,         clr);
      ObjectSetInteger(0, name, OBJPROP_BORDER_TYPE,   BORDER_FLAT);
     }
  }

void CreateLabel(string name, int x, int y, string text,
                 string font, int size, color clr)
  {
   ObjectCreate(0, name, OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, name, OBJPROP_XDISTANCE,  x);
   ObjectSetInteger(0, name, OBJPROP_YDISTANCE,  y);
   ObjectSetInteger(0, name, OBJPROP_CORNER,     CORNER_LEFT_UPPER);
   ObjectSetString (0, name, OBJPROP_TEXT,        text);
   ObjectSetString (0, name, OBJPROP_FONT,        font);
   ObjectSetInteger(0, name, OBJPROP_FONTSIZE,    size);
   ObjectSetInteger(0, name, OBJPROP_COLOR,       clr);
   ObjectSetInteger(0, name, OBJPROP_BACK,        false);
   ObjectSetInteger(0, name, OBJPROP_SELECTABLE,  false);
   ObjectSetInteger(0, name, OBJPROP_HIDDEN,      true);
  }
//+------------------------------------------------------------------+