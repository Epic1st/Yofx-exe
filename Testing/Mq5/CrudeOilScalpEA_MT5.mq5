//+------------------------------------------------------------------+
//|                                   CrudeOilScalpEA_MT5.mq5       |
//|           Professional EA — CRUDE OIL BUY/SELL Scalper          |
//|           v4.0 MT5 — Full conversion from MT4 v4.00             |
//|           Based on © nicks1008 Pine Script Indicator             |
//+------------------------------------------------------------------+
#property copyright "CrudeOilScalpEA v4 MT5 — Professional Edition"
#property link      "https://mozilla.org/MPL/2.0/"
#property version   "4.00"

#include <Trade\Trade.mqh>

CTrade trade;

//+------------------------------------------------------------------+
//|  INPUTS                                                          |
//+------------------------------------------------------------------+

// ════════  SIGNAL SETTINGS  ════════
input bool   UseBuySellSignals     = true;    // Use BUY / SELL Signals
input bool   UseReversalSignals    = true;    // Use Reversal (!) Signals
input bool   CloseOnOppositeSignal = true;    // Close trades on opposite signal

// ════════  INDICATOR PARAMETERS  ════════
input int    InpRSILength  = 14;    // RSI Length
input int    InpRSIHigh    = 80;    // RSI Overbought Level
input int    InpRSILow     = 20;    // RSI Oversold Level
input int    InpSMALength  = 70;    // SMA Length

// ════════  STOP LOSS  ════════
input int    InpSLLookback = 10;    // SL Lookback Candles (swing high/low)
input int    InpSLBuffer   = 2;     // SL Buffer beyond swing high/low (points)

// ════════  TP1 SETTINGS  ════════
input bool   UseTP1        = true;  // Enable TP1
input double InpLotTP1     = 0.01;  // Lot Size — TP1
input double InpRR_TP1     = 1.0;   // RR Ratio — TP1

// ════════  TP2 SETTINGS  ════════
input bool   UseTP2        = true;  // Enable TP2
input double InpLotTP2     = 0.01;  // Lot Size — TP2
input double InpRR_TP2     = 1.5;   // RR Ratio — TP2

// ════════  TP3 SETTINGS  ════════
input bool   UseTP3        = true;  // Enable TP3
input double InpLotTP3     = 0.01;  // Lot Size — TP3
input double InpRR_TP3     = 2.0;   // RR Ratio — TP3

// ════════  BREAKEVEN  ════════
input bool   UseBreakeven       = true;  // Enable Breakeven (when TP1 hits)
input int    InpBreakevenBuffer = 2;     // Breakeven buffer in points above entry

// ════════  TRAILING STOP  ════════
input bool   UseTrailing   = true;  // Enable Trailing Stop
input int    InpTrailStart = 50;    // Points in profit to activate trailing
input int    InpTrailStep  = 20;    // Trailing distance in points from price

// ════════  SPREAD FILTER  ════════
input double InpMaxSpreadPoints = 30.0;  // Max allowed spread (points). 0 = disabled

// ════════  DAILY TARGET  ════════
input bool   UseDailyTarget    = true;    // Enable Daily Profit Target
input double InpDailyTargetUSD = 1000.0;  // Daily Profit Target (USD)

// ════════  EA SETTINGS  ════════
input int    InpGMTOffset   = 0;         // Broker GMT offset (hours)
input int    InpMagicNumber = 20260419;  // Magic Number
input int    InpSlippage    = 10;        // Slippage (points)
input int    InpMaxRetries  = 3;         // Order send retries on requote
input bool   ShowPanel      = true;      // Show Info Panel

//+------------------------------------------------------------------+
//|  GLOBALS                                                         |
//+------------------------------------------------------------------+
datetime g_LastBarTime     = 0;
datetime g_TradingDay      = 0;
double   g_DayStartBalance = 0;
bool     g_DailyTargetHit  = false;

// Indicator handles (MT5 handle-based indicators)
int h_sma = INVALID_HANDLE;
int h_rsi = INVALID_HANDLE;

struct TradeSet
  {
   ulong  ticket_tp1;   // 0 = not used / not open
   ulong  ticket_tp2;
   ulong  ticket_tp3;
   double entry_price;  // actual fill price from TP1 position
   double original_sl;
   bool   be_applied;
   int    direction;    // ORDER_TYPE_BUY (0) or ORDER_TYPE_SELL (1)
  };

#define MAX_SETS 50
TradeSet g_TradeSets[MAX_SETS];
int      g_SetCount = 0;

string PFX = "CSE_";

//+------------------------------------------------------------------+
//|  HELPERS — fill mode auto-detection                              |
//+------------------------------------------------------------------+
ENUM_ORDER_TYPE_FILLING GetFillMode()
  {
   uint filling = (uint)SymbolInfoInteger(_Symbol, SYMBOL_FILLING_MODE);
   if((filling & SYMBOL_FILLING_FOK) == SYMBOL_FILLING_FOK) return ORDER_FILLING_FOK;
   if((filling & SYMBOL_FILLING_IOC) == SYMBOL_FILLING_IOC) return ORDER_FILLING_IOC;
   return ORDER_FILLING_RETURN;
  }

//+------------------------------------------------------------------+
//|  INIT                                                            |
//+------------------------------------------------------------------+
int OnInit()
  {
   if(InpTrailStart <= InpTrailStep)
     {
      Alert("TrailStart (", InpTrailStart, ") must be greater than TrailStep (", InpTrailStep, ").");
      return INIT_PARAMETERS_INCORRECT;
     }

   // Create indicator handles
   h_sma = iMA(_Symbol, PERIOD_CURRENT, InpSMALength, 0, MODE_SMA, PRICE_CLOSE);
   h_rsi = iRSI(_Symbol, PERIOD_CURRENT, InpRSILength, PRICE_CLOSE);
   if(h_sma == INVALID_HANDLE || h_rsi == INVALID_HANDLE)
     {
      Print("ERROR: Failed to create indicator handles.");
      return INIT_FAILED;
     }

   // Configure CTrade object
   trade.SetExpertMagicNumber(InpMagicNumber);
   trade.SetDeviationInPoints(InpSlippage);
   trade.SetTypeFilling(GetFillMode());

   g_DayStartBalance = AccountInfoDouble(ACCOUNT_BALANCE);
   g_TradingDay      = GetDayStart();
   g_SetCount        = 0;

   if(ShowPanel) CreatePanel();

   Print("CrudeOilScalpEA v4 MT5 initialized. Magic=", InpMagicNumber,
         " TrailStart=", InpTrailStart, "pts TrailStep=", InpTrailStep, "pts");
   return INIT_SUCCEEDED;
  }

//+------------------------------------------------------------------+
void OnDeinit(const int reason)
  {
   if(h_sma != INVALID_HANDLE) { IndicatorRelease(h_sma); h_sma = INVALID_HANDLE; }
   if(h_rsi != INVALID_HANDLE) { IndicatorRelease(h_rsi); h_rsi = INVALID_HANDLE; }
   DeletePanel();
  }

//+------------------------------------------------------------------+
//|  MAIN TICK                                                       |
//+------------------------------------------------------------------+
void OnTick()
  {
   CheckNewDay();

   if(UseBreakeven) ManageBreakeven();
   if(UseTrailing)  ManageTrailing();

   CleanClosedSets();

   if(ShowPanel) UpdatePanel();

   // New-bar gate
   datetime cur_bar = iTime(_Symbol, PERIOD_CURRENT, 0);
   if(cur_bar == g_LastBarTime) return;
   g_LastBarTime = cur_bar;

   if(g_DailyTargetHit) return;

   bool isBuy = false, isSell = false;
   bool isRevBuy = false, isRevSell = false;
   double sig_sl = 0;

   GetSignal(isBuy, isSell, isRevBuy, isRevSell, sig_sl);

   bool anyBuy  = (isBuy  && UseBuySellSignals) || (isRevBuy  && UseReversalSignals);
   bool anySell = (isSell && UseBuySellSignals) || (isRevSell && UseReversalSignals);

   if(CloseOnOppositeSignal)
     {
      if(anyBuy)  CloseTradesByDirection(POSITION_TYPE_SELL);
      if(anySell) CloseTradesByDirection(POSITION_TYPE_BUY);
     }

   if(anyBuy)  OpenTrades(ORDER_TYPE_BUY,  sig_sl);
   if(anySell) OpenTrades(ORDER_TYPE_SELL, sig_sl);
  }

//+------------------------------------------------------------------+
//|  SIGNAL LOGIC                                                    |
//+------------------------------------------------------------------+
void GetSignal(bool &isBuy, bool &isSell, bool &isRevBuy, bool &isRevSell, double &sl)
  {
   isBuy = false; isSell = false;
   isRevBuy = false; isRevSell = false;
   sl = 0;

   int needed = InpSMALength + InpRSILength + InpSLLookback + 10;
   if(Bars(_Symbol, PERIOD_CURRENT) < needed + 5) return;

   // Price data — iClose/iOpen/iHigh/iLow available natively in MT5
   double cl1 = iClose(_Symbol, PERIOD_CURRENT, 1);
   double cl2 = iClose(_Symbol, PERIOD_CURRENT, 2);
   double cl3 = iClose(_Symbol, PERIOD_CURRENT, 3);

   double op1 = iOpen(_Symbol, PERIOD_CURRENT, 1);
   double op2 = iOpen(_Symbol, PERIOD_CURRENT, 2);

   double hi1 = iHigh(_Symbol, PERIOD_CURRENT, 1);
   double hi2 = iHigh(_Symbol, PERIOD_CURRENT, 2);

   double lo1 = iLow(_Symbol, PERIOD_CURRENT, 1);
   double lo2 = iLow(_Symbol, PERIOD_CURRENT, 2);
   double lo3 = iLow(_Symbol, PERIOD_CURRENT, 3);

   // SMA and RSI from handles via CopyBuffer
   double sma_buf[], rsi_buf[];
   ArraySetAsSeries(sma_buf, true);
   ArraySetAsSeries(rsi_buf, true);

   if(CopyBuffer(h_sma, 0, 0, 5, sma_buf) < 5) return;
   if(CopyBuffer(h_rsi, 0, 0, 5, rsi_buf) < 5) return;

   double sma1 = sma_buf[1];
   double sma2 = sma_buf[2];
   double sma3 = sma_buf[3];
   double rsi1 = rsi_buf[1];
   double rsi2 = rsi_buf[2];

   // BUY: crossover close/SMA + confirmation
   bool cross_up  = (cl3 < sma3) && (cl2 >= sma2);
   bool buySignal = cross_up && (cl2 > op2) && (hi1 > hi2) && (cl1 > op1);

   // SELL: crossunder low/SMA + confirmation
   bool cross_dn   = (lo3 > sma3) && (lo2 <= sma2);
   bool sellSignal = cross_dn && (cl2 < op2) && (lo1 < lo2) && (cl1 < op1);

   // Reversals via RSI
   bool hlrev_s = (rsi2 >= InpRSIHigh) && (rsi1 < InpRSIHigh);
   bool llrev_b = (rsi2 <= InpRSILow)  && (rsi1 > InpRSILow) && (op1 < cl1);

   isBuy     = buySignal;
   isSell    = sellSignal;
   isRevSell = hlrev_s;
   isRevBuy  = llrev_b;

   // SL from swing high/low with configurable buffer
   if(buySignal || llrev_b)
     {
      double swing_low = iLow(_Symbol, PERIOD_CURRENT, 1);
      for(int k = 1; k <= InpSLLookback; k++)
         swing_low = MathMin(swing_low, iLow(_Symbol, PERIOD_CURRENT, k));
      sl = swing_low - InpSLBuffer * _Point;
     }
   else if(sellSignal || hlrev_s)
     {
      double swing_high = iHigh(_Symbol, PERIOD_CURRENT, 1);
      for(int k = 1; k <= InpSLLookback; k++)
         swing_high = MathMax(swing_high, iHigh(_Symbol, PERIOD_CURRENT, k));
      sl = swing_high + InpSLBuffer * _Point;
     }
  }

//+------------------------------------------------------------------+
//|  SAFE ORDER SEND — retries on requote/off-quotes                 |
//+------------------------------------------------------------------+
ulong SafeOrderSend(ENUM_ORDER_TYPE dir, double lots, double sl, double tp, string comment)
  {
   for(int attempt = 0; attempt < InpMaxRetries; attempt++)
     {
      double price = (dir == ORDER_TYPE_BUY)
                     ? SymbolInfoDouble(_Symbol, SYMBOL_ASK)
                     : SymbolInfoDouble(_Symbol, SYMBOL_BID);

      bool ok = (dir == ORDER_TYPE_BUY)
                ? trade.Buy (lots, _Symbol, price, sl, tp, comment)
                : trade.Sell(lots, _Symbol, price, sl, tp, comment);

      if(ok) return trade.ResultOrder();   // In hedging mode, position ticket == order ticket

      uint retcode = trade.ResultRetcode();
      if(retcode == TRADE_RETCODE_REQUOTE       ||
         retcode == TRADE_RETCODE_PRICE_CHANGED  ||
         retcode == TRADE_RETCODE_PRICE_OFF)
        {
         Print("SafeOrderSend: requote attempt ", attempt + 1, "/", InpMaxRetries);
         Sleep(250);
         continue;
        }
      Print("SafeOrderSend unrecoverable error: ", retcode,
            " (", trade.ResultRetcodeDescription(), ") comment=", comment);
      break;
     }
   return 0;
  }

//+------------------------------------------------------------------+
//|  OPEN TRADE SET (up to 3 positions)                              |
//+------------------------------------------------------------------+
void OpenTrades(ENUM_ORDER_TYPE dir, double sl_price)
  {
   if(g_DailyTargetHit) return;
   if(g_SetCount >= MAX_SETS)
     {
      Print("TradeSet array full (MAX=", MAX_SETS, "), skipping.");
      return;
     }

   // Spread filter
   if(InpMaxSpreadPoints > 0)
     {
      double cur_spread = (double)SymbolInfoInteger(_Symbol, SYMBOL_SPREAD);
      if(cur_spread > InpMaxSpreadPoints)
        {
         Print("Spread too high: ", cur_spread, " pts (max=", InpMaxSpreadPoints, "). Skipping.");
         return;
        }
     }

   double entry   = (dir == ORDER_TYPE_BUY)
                    ? SymbolInfoDouble(_Symbol, SYMBOL_ASK)
                    : SymbolInfoDouble(_Symbol, SYMBOL_BID);
   double sl_dist = MathAbs(entry - sl_price);

   // Respect broker minimum StopLevel
   double min_stops = (double)SymbolInfoInteger(_Symbol, SYMBOL_TRADE_STOPS_LEVEL) * _Point;
   double min_dist  = MathMax(min_stops, _Point * 10);
   if(sl_dist < min_dist)
     {
      Print("SL dist ", sl_dist, " < required minimum ", min_dist, ". Skipping.");
      return;
     }

   string tag  = (dir == ORDER_TYPE_BUY) ? "CSE_BUY" : "CSE_SELL";
   double sl_n = NormalizeDouble(sl_price, _Digits);

   TradeSet ts;
   ts.ticket_tp1  = 0;
   ts.ticket_tp2  = 0;
   ts.ticket_tp3  = 0;
   ts.entry_price = entry;   // provisional; updated with actual fill below
   ts.original_sl = sl_n;
   ts.be_applied  = false;
   ts.direction   = (int)dir;

   // TP1
   if(UseTP1 && InpLotTP1 > 0)
     {
      double tp1 = NormalizeDouble(
                      dir == ORDER_TYPE_BUY
                      ? entry + sl_dist * InpRR_TP1
                      : entry - sl_dist * InpRR_TP1, _Digits);
      ulong tk = SafeOrderSend(dir, NormalizeLot(InpLotTP1), sl_n, tp1, tag + "_TP1");
      if(tk > 0)
        {
         ts.ticket_tp1 = tk;
         // Actual fill price for precise BE / Trail calculations
         if(PositionSelectByTicket(tk))
            ts.entry_price = PositionGetDouble(POSITION_PRICE_OPEN);
        }
     }

   // TP2 — recalculate SL distance with actual entry
   entry  = ts.entry_price;
   sl_dist = MathAbs(entry - sl_n);

   if(UseTP2 && InpLotTP2 > 0)
     {
      double tp2 = NormalizeDouble(
                      dir == ORDER_TYPE_BUY
                      ? entry + sl_dist * InpRR_TP2
                      : entry - sl_dist * InpRR_TP2, _Digits);
      ulong tk = SafeOrderSend(dir, NormalizeLot(InpLotTP2), sl_n, tp2, tag + "_TP2");
      if(tk > 0) ts.ticket_tp2 = tk;
     }

   // TP3
   if(UseTP3 && InpLotTP3 > 0)
     {
      double tp3 = NormalizeDouble(
                      dir == ORDER_TYPE_BUY
                      ? entry + sl_dist * InpRR_TP3
                      : entry - sl_dist * InpRR_TP3, _Digits);
      ulong tk = SafeOrderSend(dir, NormalizeLot(InpLotTP3), sl_n, tp3, tag + "_TP3");
      if(tk > 0) ts.ticket_tp3 = tk;
     }

   // Register only if at least one position was opened
   if(ts.ticket_tp1 == 0 && ts.ticket_tp2 == 0 && ts.ticket_tp3 == 0)
     {
      Print("OpenTrades: no positions executed.");
      return;
     }

   g_TradeSets[g_SetCount] = ts;
   g_SetCount++;

   Print("Set opened | ", EnumToString(dir),
         " Entry=", ts.entry_price, " SL=", sl_n,
         " TP1=#", ts.ticket_tp1,
         " TP2=#", ts.ticket_tp2,
         " TP3=#", ts.ticket_tp3);
  }

//+------------------------------------------------------------------+
//|  IS POSITION OPEN                                                |
//+------------------------------------------------------------------+
bool IsOrderOpen(ulong ticket)
  {
   if(ticket == 0) return false;
   return PositionSelectByTicket(ticket);
  }

//+------------------------------------------------------------------+
//|  BREAKEVEN — when TP1 closes, move SL of TP2/TP3 to entry+buf   |
//+------------------------------------------------------------------+
void ManageBreakeven()
  {
   for(int i = 0; i < g_SetCount; i++)
     {
      if(g_TradeSets[i].be_applied) continue;

      // If no TP1 in this set, mark BE done so CleanClosedSets works correctly
      if(g_TradeSets[i].ticket_tp1 == 0)
        {
         g_TradeSets[i].be_applied = true;
         continue;
        }

      if(IsOrderOpen(g_TradeSets[i].ticket_tp1)) continue;  // TP1 still open

      double entry = g_TradeSets[i].entry_price;
      int    dir   = g_TradeSets[i].direction;
      double be_sl = (dir == (int)ORDER_TYPE_BUY)
                     ? NormalizeDouble(entry + InpBreakevenBuffer * _Point, _Digits)
                     : NormalizeDouble(entry - InpBreakevenBuffer * _Point, _Digits);

      bool moved  = MoveSlIfBetter(g_TradeSets[i].ticket_tp2, dir, be_sl, "BE-TP2");
      bool moved3 = MoveSlIfBetter(g_TradeSets[i].ticket_tp3, dir, be_sl, "BE-TP3");

      // Mark done only when both confirmed (or already closed)
      bool tp2_done = !IsOrderOpen(g_TradeSets[i].ticket_tp2) || moved;
      bool tp3_done = !IsOrderOpen(g_TradeSets[i].ticket_tp3) || moved3;

      if(tp2_done && tp3_done)
        {
         g_TradeSets[i].be_applied = true;
         Print("Breakeven applied set ", i, " BE_SL=", be_sl);
        }
     }
  }

//+------------------------------------------------------------------+
//|  TRAILING STOP — activates after InpTrailStart pts from entry    |
//+------------------------------------------------------------------+
void ManageTrailing()
  {
   double trail_dist     = InpTrailStep  * _Point;
   double trail_activate = InpTrailStart * _Point;

   for(int i = 0; i < g_SetCount; i++)
     {
      TrailOrder(g_TradeSets[i].ticket_tp1, g_TradeSets[i], trail_activate, trail_dist);
      TrailOrder(g_TradeSets[i].ticket_tp2, g_TradeSets[i], trail_activate, trail_dist);
      TrailOrder(g_TradeSets[i].ticket_tp3, g_TradeSets[i], trail_activate, trail_dist);
     }
  }

void TrailOrder(ulong ticket, const TradeSet &ts, double activate, double trail_dist)
  {
   if(!IsOrderOpen(ticket)) return;
   if(!PositionSelectByTicket(ticket)) return;

   int    dir    = ts.direction;
   double entry  = ts.entry_price;
   double cur_sl = PositionGetDouble(POSITION_SL);
   double cur_tp = PositionGetDouble(POSITION_TP);
   double new_sl = cur_sl;
   bool   do_move = false;

   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);

   if(dir == (int)ORDER_TYPE_BUY)
     {
      double profit = bid - entry;
      if(profit >= activate)
        {
         new_sl  = NormalizeDouble(bid - trail_dist, _Digits);
         do_move = (new_sl > cur_sl);
        }
     }
   else
     {
      double profit = entry - ask;
      if(profit >= activate)
        {
         new_sl  = NormalizeDouble(ask + trail_dist, _Digits);
         do_move = (new_sl < cur_sl || cur_sl == 0.0);
        }
     }

   if(do_move)
     {
      bool ok = trade.PositionModify(ticket, new_sl, cur_tp);
      if(!ok)
         Print("Trail modify error: ", trade.ResultRetcode(),
               " ticket=", ticket, " new_sl=", new_sl);
     }
  }

//+------------------------------------------------------------------+
//|  HELPER — move SL only if more favorable                         |
//+------------------------------------------------------------------+
bool MoveSlIfBetter(ulong ticket, int dir, double new_sl, string label)
  {
   if(!IsOrderOpen(ticket)) return true;     // already closed = OK
   if(!PositionSelectByTicket(ticket)) return false;

   double cur_sl = PositionGetDouble(POSITION_SL);
   double cur_tp = PositionGetDouble(POSITION_TP);
   bool   better = (dir == (int)ORDER_TYPE_BUY)
                   ? (new_sl > cur_sl)
                   : (new_sl < cur_sl);

   if(!better) return true;   // already at or past target

   bool ok = trade.PositionModify(ticket, new_sl, cur_tp);
   if(!ok)
      Print(label, " modify error: ", trade.ResultRetcode(), " ticket=", ticket);
   return ok;
  }

//+------------------------------------------------------------------+
//|  CLEAN TRADE SETS — remove fully closed sets                     |
//+------------------------------------------------------------------+
void CleanClosedSets()
  {
   int write = 0;
   for(int i = 0; i < g_SetCount; i++)
     {
      bool any_open = IsOrderOpen(g_TradeSets[i].ticket_tp1)
                   || IsOrderOpen(g_TradeSets[i].ticket_tp2)
                   || IsOrderOpen(g_TradeSets[i].ticket_tp3);

      // Keep if any position open OR breakeven not yet processed
      if(any_open || !g_TradeSets[i].be_applied)
        {
         if(write != i) g_TradeSets[write] = g_TradeSets[i];
         write++;
        }
     }
   g_SetCount = write;
  }

//+------------------------------------------------------------------+
//|  CLOSE BY DIRECTION (opposite signal)                            |
//+------------------------------------------------------------------+
void CloseTradesByDirection(ENUM_POSITION_TYPE type_to_close)
  {
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagicNumber) continue;
      if(PositionGetString(POSITION_SYMBOL) != _Symbol)        continue;
      if((ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE) != type_to_close) continue;

      bool ok = trade.PositionClose(ticket);
      if(!ok)
         Print("CloseByDir error: ", trade.ResultRetcode(), " ticket=", ticket);
      else
         Print("Closed #", ticket, " on opposite signal.");
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
      // Do NOT reset g_SetCount — orders from previous day may still be open
      CleanClosedSets();
      Print("New trading day. Balance=", g_DayStartBalance, " Active sets=", g_SetCount);
     }

   if(UseDailyTarget && !g_DailyTargetHit)
     {
      if(GetDailyPnL() >= InpDailyTargetUSD)
        {
         g_DailyTargetHit = true;
         Print("Daily target reached! PnL=", GetDailyPnL(), " Closing all trades.");
         CloseAllTrades();
        }
     }
  }

void CloseAllTrades()
  {
   int closed = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagicNumber) continue;
      if(PositionGetString(POSITION_SYMBOL) != _Symbol)        continue;

      bool ok = trade.PositionClose(ticket);
      if(!ok)
         Print("CloseAll error: ", trade.ResultRetcode(), " ticket=", ticket);
      else
         closed++;
     }
   Print("CloseAllTrades: closed ", closed, " positions.");
  }

//+------------------------------------------------------------------+
//|  UTILITY                                                         |
//+------------------------------------------------------------------+
datetime GetDayStart()
  {
   datetime t = TimeCurrent() + InpGMTOffset * 3600;
   return (datetime)(t - t % 86400);
  }

double GetDailyPnL()
  {
   return (AccountInfoDouble(ACCOUNT_BALANCE) - g_DayStartBalance) + GetFloatingPnL();
  }

double GetFloatingPnL()
  {
   double pnl = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagicNumber) continue;
      if(PositionGetString(POSITION_SYMBOL) != _Symbol)        continue;
      pnl += PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
     }
   return pnl;
  }

int CountOpenTrades()
  {
   int cnt = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagicNumber) continue;
      if(PositionGetString(POSITION_SYMBOL) != _Symbol)        continue;
      cnt++;
     }
   return cnt;
  }

int CountBuys()
  {
   int cnt = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagicNumber) continue;
      if(PositionGetString(POSITION_SYMBOL) != _Symbol)        continue;
      if((ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE) == POSITION_TYPE_BUY) cnt++;
     }
   return cnt;
  }

int CountSells()
  {
   int cnt = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(!PositionSelectByTicket(ticket)) continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagicNumber) continue;
      if(PositionGetString(POSITION_SYMBOL) != _Symbol)        continue;
      if((ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE) == POSITION_TYPE_SELL) cnt++;
     }
   return cnt;
  }

double NormalizeLot(double lot)
  {
   double min_lot  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double max_lot  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
   double lot_step = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
   if(lot_step <= 0) lot_step = 0.01;
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
   int x = 15, y = 25, w = 280, h = 430;

   CreateRect(PFX+"bg",    x, y, w, h,    C'10,12,28',  false);
   CreateRect(PFX+"bdr",   x, y, w, h,    C'45,90,170', true);
   CreateRect(PFX+"hdr",   x, y, w, 28,   C'20,50,120', false);
   CreateRect(PFX+"hdrln", x, y+28, w, 2, C'45,90,170', false);

   CreateLabel(PFX+"title", x+10, y+7, "  CRUDE OIL SCALPER  v4 MT5", "Arial Bold", 10, clrWhite);

   int r = y+36, sp = 19;

   CreateLabel(PFX+"sym_l",  x+10,  r, "Symbol",       "Arial", 9, C'110,140,190');
   CreateLabel(PFX+"sym_v",  x+145, r, "---",           "Arial Bold", 9, clrWhite);   r+=sp;
   CreateLabel(PFX+"tf_l",   x+10,  r, "Timeframe",    "Arial", 9, C'110,140,190');
   CreateLabel(PFX+"tf_v",   x+145, r, "---",           "Arial Bold", 9, clrWhite);   r+=sp;

   CreateSep(PFX+"s1", x+8, r, w-16); r+=6;

   CreateLabel(PFX+"bal_l",  x+10,  r, "Balance",      "Arial", 9, C'110,140,190');
   CreateLabel(PFX+"bal_v",  x+145, r, "---",           "Arial Bold", 9, clrWhite);   r+=sp;
   CreateLabel(PFX+"eq_l",   x+10,  r, "Equity",       "Arial", 9, C'110,140,190');
   CreateLabel(PFX+"eq_v",   x+145, r, "---",           "Arial Bold", 9, clrWhite);   r+=sp;
   CreateLabel(PFX+"fl_l",   x+10,  r, "Floating P&L", "Arial", 9, C'110,140,190');
   CreateLabel(PFX+"fl_v",   x+145, r, "---",           "Arial Bold", 9, clrGray);    r+=sp;
   CreateLabel(PFX+"dp_l",   x+10,  r, "Daily P&L",    "Arial", 9, C'110,140,190');
   CreateLabel(PFX+"dp_v",   x+145, r, "---",           "Arial Bold", 9, clrGray);    r+=sp;
   CreateLabel(PFX+"tg_l",   x+10,  r, "Daily Target", "Arial", 9, C'110,140,190');
   CreateLabel(PFX+"tg_v",   x+145, r, "---",           "Arial Bold", 9, clrGray);    r+=sp;

   CreateSep(PFX+"s2", x+8, r, w-16); r+=6;

   CreateLabel(PFX+"ot_l",  x+10,  r, "Open Trades",  "Arial", 9, C'110,140,190');
   CreateLabel(PFX+"ot_v",  x+145, r, "0",             "Arial Bold", 9, clrWhite);    r+=sp;
   CreateLabel(PFX+"by_l",  x+10,  r, "  Buys",        "Arial", 8, C'80,190,140');
   CreateLabel(PFX+"by_v",  x+145, r, "0",             "Arial Bold", 8, C'80,190,140'); r+=sp;
   CreateLabel(PFX+"sl_l",  x+10,  r, "  Sells",       "Arial", 8, C'210,80,80');
   CreateLabel(PFX+"sl_v",  x+145, r, "0",             "Arial Bold", 8, C'210,80,80'); r+=sp;

   CreateSep(PFX+"s3", x+8, r, w-16); r+=6;

   CreateLabel(PFX+"sbs_l", x+10,  r, "BuySell Sigs",  "Arial", 9, C'110,140,190');
   CreateLabel(PFX+"sbs_v", x+145, r, UseBuySellSignals  ? "ON" : "OFF",
               "Arial Bold", 9, UseBuySellSignals  ? clrLime : clrOrangeRed); r+=sp;
   CreateLabel(PFX+"rev_l", x+10,  r, "Reversal Sigs", "Arial", 9, C'110,140,190');
   CreateLabel(PFX+"rev_v", x+145, r, UseReversalSignals ? "ON" : "OFF",
               "Arial Bold", 9, UseReversalSignals ? clrLime : clrOrangeRed); r+=sp;
   CreateLabel(PFX+"opp_l", x+10,  r, "Opp. Close",    "Arial", 9, C'110,140,190');
   CreateLabel(PFX+"opp_v", x+145, r, CloseOnOppositeSignal ? "ON" : "OFF",
               "Arial Bold", 9, CloseOnOppositeSignal ? clrLime : clrOrangeRed); r+=sp;
   CreateLabel(PFX+"be_l",  x+10,  r, "Breakeven",     "Arial", 9, C'110,140,190');
   CreateLabel(PFX+"be_v",  x+145, r, UseBreakeven ? "ON" : "OFF",
               "Arial Bold", 9, UseBreakeven ? clrLime : clrOrangeRed); r+=sp;
   CreateLabel(PFX+"tr_l",  x+10,  r, "Trailing",      "Arial", 9, C'110,140,190');
   CreateLabel(PFX+"tr_v",  x+145, r, UseTrailing ? "ON" : "OFF",
               "Arial Bold", 9, UseTrailing ? clrLime : clrOrangeRed); r+=sp;

   CreateSep(PFX+"s4", x+8, r, w-16); r+=6;

   CreateLabel(PFX+"stat_l", x+10,  r, "EA Status",    "Arial", 9, C'110,140,190');
   CreateLabel(PFX+"stat_v", x+145, r, "ACTIVE",        "Arial Bold", 9, clrLime); r+=sp;

   r += 3;
   CreateLabel(PFX+"cfg", x+10, r,
      StringFormat("SL:%db/%dpt  BE:%dpt  Trail:%d/%dpt",
                   InpSLLookback, InpSLBuffer,
                   InpBreakevenBuffer,
                   InpTrailStart, InpTrailStep),
      "Arial", 8, C'70,100,150');

   ChartRedraw();
  }

//+------------------------------------------------------------------+
//|  PANEL — UPDATE                                                  |
//+------------------------------------------------------------------+
void UpdatePanel()
  {
   if(!ShowPanel) return;

   double bal   = AccountInfoDouble(ACCOUNT_BALANCE);
   double eq    = AccountInfoDouble(ACCOUNT_EQUITY);
   double fl    = GetFloatingPnL();
   double daily = GetDailyPnL();
   int    total = CountOpenTrades();
   int    buys  = CountBuys();
   int    sells = CountSells();
   double pct   = (InpDailyTargetUSD > 0) ? daily / InpDailyTargetUSD * 100.0 : 0;

   ObjTxt(PFX+"sym_v",  _Symbol);
   ObjTxt(PFX+"tf_v",   PeriodToStr(Period()));
   ObjTxt(PFX+"bal_v",  StringFormat("%.2f",  bal));
   ObjTxt(PFX+"eq_v",   StringFormat("%.2f",  eq));
   ObjTxt(PFX+"fl_v",   StringFormat("%+.2f", fl));
   ObjTxt(PFX+"dp_v",   StringFormat("%+.2f", daily));
   ObjTxt(PFX+"tg_v",   UseDailyTarget
                        ? StringFormat("%.2f  (%.0f%%)", InpDailyTargetUSD, pct)
                        : "OFF");
   ObjTxt(PFX+"ot_v",   IntegerToString(total));
   ObjTxt(PFX+"by_v",   IntegerToString(buys));
   ObjTxt(PFX+"sl_v",   IntegerToString(sells));
   ObjTxt(PFX+"tr_v",   UseTrailing
                        ? StringFormat("ON  %d/%dpt", InpTrailStart, InpTrailStep)
                        : "OFF");
   ObjTxt(PFX+"stat_v", g_DailyTargetHit ? "TARGET HIT - PAUSED" : "ACTIVE");

   ObjCol(PFX+"fl_v",   fl    >= 0 ? clrLime : clrOrangeRed);
   ObjCol(PFX+"dp_v",   daily >= 0 ? clrLime : clrOrangeRed);
   ObjCol(PFX+"tg_v",   (UseDailyTarget && daily >= InpDailyTargetUSD) ? clrGold : C'110,140,190');
   ObjCol(PFX+"stat_v", g_DailyTargetHit ? clrGold : clrLime);

   ChartRedraw();
  }

void DeletePanel()  { ObjectsDeleteAll(0, PFX); ChartRedraw(); }
void ObjTxt(string n, string t) { ObjectSetString (0, n, OBJPROP_TEXT,  t); }
void ObjCol(string n, color  c) { ObjectSetInteger(0, n, OBJPROP_COLOR, (long)c); }

//+------------------------------------------------------------------+
//|  PANEL DRAW HELPERS                                              |
//+------------------------------------------------------------------+
void CreateRect(string name, int x, int y, int w, int h, color clr, bool border)
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
   ObjectSetInteger(0, name, OBJPROP_BORDER_TYPE, BORDER_FLAT);
   if(border)
     {
      ObjectSetInteger(0, name, OBJPROP_COLOR,   (long)clr);
      ObjectSetInteger(0, name, OBJPROP_BGCOLOR,  C'10,12,28');
     }
   else
     {
      ObjectSetInteger(0, name, OBJPROP_BGCOLOR, (long)clr);
      ObjectSetInteger(0, name, OBJPROP_COLOR,   (long)clr);
     }
  }

void CreateSep(string name, int x, int y, int w)
  {
   CreateRect(name, x, y, w, 1, C'40,70,140', false);
  }

void CreateLabel(string name, int x, int y, string text,
                 string font, int size, color clr)
  {
   ObjectCreate(0, name, OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, name, OBJPROP_XDISTANCE,  x);
   ObjectSetInteger(0, name, OBJPROP_YDISTANCE,  y);
   ObjectSetInteger(0, name, OBJPROP_CORNER,     CORNER_LEFT_UPPER);
   ObjectSetString (0, name, OBJPROP_TEXT,       text);
   ObjectSetString (0, name, OBJPROP_FONT,       font);
   ObjectSetInteger(0, name, OBJPROP_FONTSIZE,   size);
   ObjectSetInteger(0, name, OBJPROP_COLOR,      (long)clr);
   ObjectSetInteger(0, name, OBJPROP_BACK,       false);
   ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, name, OBJPROP_HIDDEN,     true);
  }
//+------------------------------------------------------------------+