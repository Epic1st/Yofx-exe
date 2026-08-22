//+------------------------------------------------------------------+
//|                   Smart_Grid_v6_Scalper.mq5                       |
//|   XAUUSD M1 / M5 Pure Scalper — Hero EA                          |
//|   ✓ FIXED SL in $ / Points / ATR (3 modes)                       |
//|   ✓ Fixed TP in $ / Points / ATR (3 modes)                       |
//|   ✓ Dollar Daily Limits  ✓ Equity Shield                         |
//|   ✓ Emergency Close on limit ✓ Consecutive loss guard            |
//|   ✓ Peak-based Trail  ✓ BB+ADX Sideways Filter                   |
//|   ✗ No Grid  ✗ No Hedge                                          |
//+------------------------------------------------------------------+
#property copyright   "Hero EA — v6 Scalper"
#property version     "6.20"
#property strict

#include <Trade\Trade.mqh>
#include <Trade\PositionInfo.mqh>

CTrade        Trade;
CPositionInfo PosInfo;

//====================================================================
//  INPUTS
//====================================================================

input group "=== MONEY MANAGEMENT ==="
input double  InpLotSize          = 0.10;
input bool    InpAutoLot          = true;
input double  InpRiskPercent      = 1.0;
// Base Capital ($0 = use live account balance)
input double  InpBaseCapital      = 0.0;
input double  InpMaxLot           = 1.00;

input group "=== SL / TP MODE ==="
// Choose how SL and TP are calculated:
//   FIXED_DOLLARS : exact dollar loss/profit per trade (RECOMMENDED for loss control)
//   FIXED_POINTS  : exact points distance (1 point = 0.01 for XAUUSD)
//   ATR_BASED     : ATR multiplier (dynamic, can be large on news)
enum ENUM_SL_MODE { FIXED_DOLLARS=0, FIXED_POINTS=1, ATR_BASED=2 };
input ENUM_SL_MODE InpSLMode      = FIXED_DOLLARS;

// ── FIXED DOLLARS mode ───────────────────────────────────────────
// Exact max loss per trade in USD  |  M1 recommended: $5 - $10
input double  InpFixedSL_USD      = 8.0;
// Exact target profit per trade in USD  |  M1 recommended: $10 - $18
// R:R = InpFixedTP_USD / InpFixedSL_USD  →  keep >= 1.5 : 1
input double  InpFixedTP_USD      = 12.0;

// ── FIXED POINTS mode ────────────────────────────────────────────
// XAUUSD: 1 point = 0.01  |  100 points = $1 (0.01 lot)
// M1 recommended SL: 50-80 pts | TP: 80-150 pts
input int     InpFixedSL_Points   = 60;
input int     InpFixedTP_Points   = 100;

// ── ATR BASED mode ───────────────────────────────────────────────
input int     InpATRPeriod        = 14;
input double  InpATR_SL_Mult      = 1.2;
input double  InpATR_TP_Mult      = 1.8;
// If ATR-calculated SL cost > $X → skip trade (news spike protection)
input double  InpMaxSL_USD        = 12.0;

input group "=== TRAILING STOP (POINTS BASED - RELIABLE) ==="
input bool    InpUseTrail            = true;
input bool    InpUseBreakeven        = true;
// Move SL to breakeven after N points profit
// XAUUSD: 1 point = 0.01 price | M1: 30-50 pts | M5: 80-120 pts
input int     InpBreakevenPoints     = 40;
// Start trailing after N points profit
// M1: 50-80 pts | M5: 100-150 pts
input int     InpTrailActivatePoints = 60;
// SL trails this many points BEHIND price peak (high-water mark)
// M1: 30-50 pts | M5: 60-100 pts
input int     InpTrailPoints         = 40;

input group "=== EMA TREND FILTER ==="
input bool    InpUseTrendFilter   = true;
// Fast EMA  | M1: 8 or 13 | M5: 21
input int     InpFastEMA          = 13;
// Slow EMA  | M1: 50-100 | M5: 200  ← change this to test
input int     InpSlowEMA          = 50;
// Optional 3rd EMA — all 3 must align to take trade
input bool    InpUseMidEMA        = false;
// Mid EMA   | M1: 21 | M5: 100
input int     InpMidEMA           = 21;
input ENUM_TIMEFRAMES InpEMATF    = PERIOD_M1;

input group "=== SIDEWAYS FILTER — ADX + BB ==="
input bool    InpUseADX           = true;
input int     InpADXPeriod        = 14;
// Min ADX  | M1: 18-22 | M5: 20-25
input double  InpADXMinLevel      = 20.0;
input ENUM_TIMEFRAMES InpADXTF    = PERIOD_M1;
input bool    InpUseBBFilter      = true;
input int     InpBBPeriod         = 20;
input double  InpBBDeviation      = 2.0;
// Min BB width — narrow = sideways = skip  (XAUUSD: 0.20-0.50)
input double  InpBBMinWidth       = 0.30;

input group "=== RSI FILTER ==="
input bool    InpUseRSI           = true;
input int     InpRSIPeriod        = 14;
input double  InpRSIOB            = 68.0;
input double  InpRSIOS            = 32.0;

input group "=== ENTRY PROBABILITY FILTER ==="
input int     InpMinProbability   = 60;

input group "=== SESSION FILTER ==="
input bool    InpUseSession       = true;
input int     InpLondonOpen       = 7;
input int     InpLondonClose      = 16;
input int     InpNYOpen           = 13;
input int     InpNYClose          = 21;
input bool    InpTradeAsian       = true;

input group "=== LOSS PROTECTION (CRITICAL) ==="
// ── Daily dollar limits ──────────────────────────────────────────
// Daily profit target — EA stops AND closes all trades when hit
input double  InpDailyProfitUSD   = 50.0;
// Max daily loss — EA CLOSES ALL TRADES + STOPS when hit
input double  InpDailyLossUSD     = 30.0;
input bool    InpUseDailyTarget   = true;
// ── Equity drawdown shield ───────────────────────────────────────
// If equity drops MORE than this from today's HIGHEST equity → close all + stop
// Example: Equity hit $1100 during day, drops to $1080 → $20 drawdown → close all
// Recommended: same as or slightly less than InpDailyLossUSD
input bool    InpUseEquityShield  = true;
input double  InpEquityShieldUSD  = 25.0;
// ── Alerts & protection behaviour ───────────────────────────────
// Show popup alert when limit hit
input bool    InpAlertOnLimit     = true;
// Close all open trades immediately when any limit is hit
// TRUE = hard close (recommended — prevents extra losses from open trades)
input bool    InpCloseOnLimit     = true;
// Stop after N consecutive losses (0 = disabled)
input int     InpMaxConsecLoss    = 3;
// Min bars between trades
input int     InpCooldownBars     = 2;

input group "=== TRADE SETTINGS ==="
input int     InpMagicNumber      = 202506;
input int     InpMaxTrades        = 2;
input int     InpSlippage         = 30;

input group "=== PANEL SETTINGS ==="
input int     InpPanelX           = 20;
input int     InpPanelY           = 30;
input color   InpPanelBG          = C'10,12,22';
input color   InpColorBull        = clrLime;
input color   InpColorBear        = clrRed;
input color   InpColorNeutral     = clrDimGray;
input color   InpColorTitle       = clrGold;
input color   InpColorText        = clrWhite;
input color   InpColorWarning     = clrOrangeRed;

//====================================================================
//  GLOBALS
//====================================================================
int    hATR, hFastEMA, hSlowEMA, hMidEMA, hADX, hRSI, hBB;
double atrVal, fastEMAVal, slowEMAVal, midEMAVal;
double adxVal, adxPlus, adxMinus;
double rsiVal;
double bbUpper, bbLower, bbMiddle;

double g_BaseCapital;
double dailyStartBalance;
double g_DayHighEquity;      // highest equity reached today (for drawdown shield)
datetime lastDayCheck;
datetime lastTradeBarTime;
int    barsSinceLastTrade;
int    symbolDigits;
double pointSize;

// Stats
int    g_TotalTrades   = 0;
int    g_WinTrades     = 0;
int    g_LossTrades    = 0;
int    g_BuyTrades     = 0;
int    g_SellTrades    = 0;
int    g_ConsecLoss    = 0;
int    g_LastScore     = 0;
string g_MarketStatus  = "LOADING";
string g_LastSignal    = "---";
bool   g_DailyLimitHit = false;
bool   g_AlertSent     = false;
double g_DailyPnL      = 0;
string g_LimitReason   = "";

// Peak-based trail tracking
ulong  g_TrailTickets[];
double g_TrailPeaks[];
bool   g_TrailBEDone[];

#define PANEL_PREFIX "SG6_"

//====================================================================
//  INIT
//====================================================================
int OnInit()
{
   Trade.SetExpertMagicNumber(InpMagicNumber);
   Trade.SetDeviationInPoints(InpSlippage);
   Trade.SetTypeFilling(ORDER_FILLING_RETURN);

   symbolDigits = (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);
   pointSize    = SymbolInfoDouble(_Symbol, SYMBOL_POINT);

   hATR     = iATR(_Symbol, PERIOD_CURRENT, InpATRPeriod);
   hFastEMA = iMA(_Symbol, InpEMATF, InpFastEMA, 0, MODE_EMA, PRICE_CLOSE);
   hSlowEMA = iMA(_Symbol, InpEMATF, InpSlowEMA, 0, MODE_EMA, PRICE_CLOSE);
   hMidEMA  = iMA(_Symbol, InpEMATF, InpMidEMA,  0, MODE_EMA, PRICE_CLOSE);
   hADX     = iADX(_Symbol, InpADXTF, InpADXPeriod);
   hRSI     = iRSI(_Symbol, PERIOD_CURRENT, InpRSIPeriod, PRICE_CLOSE);
   hBB      = iBands(_Symbol, PERIOD_CURRENT, InpBBPeriod, 0, InpBBDeviation, PRICE_CLOSE);

   if(hATR  == INVALID_HANDLE || hFastEMA == INVALID_HANDLE ||
      hSlowEMA == INVALID_HANDLE || hADX == INVALID_HANDLE  ||
      hRSI == INVALID_HANDLE    || hBB   == INVALID_HANDLE)
   {
      Print("ERROR: Indicator handle creation failed");
      return INIT_FAILED;
   }

   dailyStartBalance = AccountInfoDouble(ACCOUNT_BALANCE);
   g_DayHighEquity   = AccountInfoDouble(ACCOUNT_EQUITY);
   g_BaseCapital     = (InpBaseCapital > 0) ? InpBaseCapital : dailyStartBalance;
   lastDayCheck      = 0;
   barsSinceLastTrade= 999;
   g_DailyLimitHit   = false;
   g_AlertSent       = false;

   ArrayResize(g_TrailTickets, 0);
   ArrayResize(g_TrailPeaks,   0);
   ArrayResize(g_TrailBEDone,  0);

   LoadHistoryStats();
   CreatePanel();

   Print("Smart_Grid_v6 | EMA ", InpFastEMA, "/", InpSlowEMA,
         " | MaxSL_USD $", InpMaxSL_USD,
         " | DailyLoss -$", InpDailyLossUSD,
         " | Shield -$", InpEquityShieldUSD);
   return INIT_SUCCEEDED;
}

//====================================================================
//  DEINIT
//====================================================================
void OnDeinit(const int reason)
{
   IndicatorRelease(hATR);
   IndicatorRelease(hFastEMA);
   IndicatorRelease(hSlowEMA);
   IndicatorRelease(hMidEMA);
   IndicatorRelease(hADX);
   IndicatorRelease(hRSI);
   IndicatorRelease(hBB);
   DeletePanel();
}

//====================================================================
//  ON TICK
//====================================================================
void OnTick()
{
   static datetime lastBar = 0;
   datetime curBar = iTime(_Symbol, PERIOD_CURRENT, 0);
   bool isNewBar   = (curBar != lastBar);
   if(isNewBar) { lastBar = curBar; barsSinceLastTrade++; }

   // ── LOSS PROTECTION — runs EVERY TICK ────────────────────────
   // Update day high equity
   double equity = AccountInfoDouble(ACCOUNT_EQUITY);
   if(equity > g_DayHighEquity) g_DayHighEquity = equity;

   // Equity drawdown shield — check every tick
   if(InpUseEquityShield && !g_DailyLimitHit)
   {
      double drawdownFromPeak = g_DayHighEquity - equity;
      if(drawdownFromPeak >= InpEquityShieldUSD)
      {
         g_LimitReason = StringFormat("EQUITY SHIELD: -$%.2f from peak", drawdownFromPeak);
         TriggerProtection(g_LimitReason);
      }
   }

   // Daily P&L limit — also check every tick
   g_DailyPnL = equity - dailyStartBalance;
   if(InpUseDailyTarget && !g_DailyLimitHit)
   {
      if(g_DailyPnL >= InpDailyProfitUSD)
      {
         g_LimitReason = StringFormat("DAILY TARGET +$%.2f", g_DailyPnL);
         TriggerProtection(g_LimitReason);
      }
      else if(g_DailyPnL <= -InpDailyLossUSD)
      {
         g_LimitReason = StringFormat("DAILY LOSS -$%.2f", MathAbs(g_DailyPnL));
         TriggerProtection(g_LimitReason);
      }
   }

   // Trailing stop runs every tick
   if(InpUseTrail || InpUseBreakeven)
      ManageTrailingStop();

   UpdatePanel();

   if(!isNewBar) return;

   // ── New bar logic ─────────────────────────────────────────────
   CheckDailyReset();
   if(g_DailyLimitHit) return;

   if(InpMaxConsecLoss > 0 && g_ConsecLoss >= InpMaxConsecLoss)
   {
      g_LastSignal = StringFormat("CONSEC LOSS BLOCK (%d)", g_ConsecLoss);
      return;
   }

   if(!RefreshIndicators()) return;

   int openTrades = CountMyTrades();
   if(openTrades >= InpMaxTrades) return;

   if(InpUseSession && !IsInSession()) return;

   if(barsSinceLastTrade < InpCooldownBars)
   {
      g_LastSignal = StringFormat("COOLDOWN (%d/%d)", barsSinceLastTrade, InpCooldownBars);
      return;
   }

   // Sideways check — ADX
   if(InpUseADX && adxVal < InpADXMinLevel)
   {
      g_MarketStatus = "SIDEWAYS";
      g_LastSignal   = StringFormat("ADX BLOCK (%.1f<%.1f)", adxVal, InpADXMinLevel);
      return;
   }

   // Sideways check — BB Width
   if(InpUseBBFilter)
   {
      double bbWidth = bbUpper - bbLower;
      if(bbWidth < InpBBMinWidth)
      {
         g_MarketStatus = "RANGE";
         g_LastSignal   = StringFormat("BB NARROW (%.2f)", bbWidth);
         return;
      }
   }

   g_MarketStatus = "TRENDING";

   int trendDir = GetTrendDirection();
   if(trendDir == 0) { g_LastSignal = "NO TREND"; return; }

   if(InpUseRSI)
   {
      if(trendDir ==  1 && rsiVal > InpRSIOB) { g_LastSignal = "RSI OB"; return; }
      if(trendDir == -1 && rsiVal < InpRSIOS) { g_LastSignal = "RSI OS"; return; }
   }

   int score = CalcEntryProbability(trendDir);
   g_LastScore = score;
   if(score < InpMinProbability)
   {
      g_LastSignal = StringFormat("LOW PROB (%d%%)", score);
      return;
   }

   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);

   if(InpUseTrendFilter)
   {
      if(trendDir ==  1 && ask < slowEMAVal) { g_LastSignal = "BELOW SLOW EMA"; return; }
      if(trendDir == -1 && bid > slowEMAVal) { g_LastSignal = "ABOVE SLOW EMA"; return; }
   }

   // ── STEP 1: Get initial lot size (needed for FIXED_DOLLARS SL calc) ──
   // For FIXED_DOLLARS: we first get lot from base risk, then derive SL distance
   // For FIXED_POINTS / ATR: get lot from SL distance as usual
   double sl_dist = 0, tp_dist = 0, lots = 0;

   if(InpSLMode == FIXED_DOLLARS)
   {
      // Lot = risk amount / SL in USD
      // Since SL is fixed in USD, lot = base_risk_USD / InpFixedSL_USD
      double capital  = (g_BaseCapital > 0) ? g_BaseCapital : AccountInfoDouble(ACCOUNT_BALANCE);
      double riskAmt  = InpAutoLot ? capital * InpRiskPercent / 100.0
                                   : InpLotSize * InpFixedSL_USD; // approximate
      if(InpAutoLot)
         lots = NormalizeLot(riskAmt / InpFixedSL_USD);
      else
         lots = NormalizeLot(InpLotSize);

      if(lots <= 0) return;
      if(!CalcSLTP(trendDir, lots, sl_dist, tp_dist)) return;
   }
   else
   {
      // ATR or FIXED_POINTS: get distance first, then calculate lot
      if(InpSLMode == FIXED_POINTS)
      {
         sl_dist = NormalizeDouble(InpFixedSL_Points * pointSize, symbolDigits);
         tp_dist = NormalizeDouble(InpFixedTP_Points * pointSize, symbolDigits);
      }
      else // ATR
      {
         sl_dist = NormalizeDouble(atrVal * InpATR_SL_Mult, symbolDigits);
         tp_dist = NormalizeDouble(atrVal * InpATR_TP_Mult, symbolDigits);
      }
      lots = CalcLotSize(sl_dist);
      if(lots <= 0) return;
      // For ATR mode: check dollar cap on the resulting SL cost
      if(!CalcSLTP(trendDir, lots, sl_dist, tp_dist)) return;
   }

   // ── STEP 2: Verify final SL/TP are valid ──────────────────────
   if(sl_dist <= 0 || tp_dist <= 0) return;

   // ── STEP 3: Log what mode + cost will be ─────────────────────
   double tickVal2 = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE);
   double tickSz2  = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
   double slCostCheck = (tickSz2 > 0 && tickVal2 > 0) ?
                        (sl_dist / tickSz2) * tickVal2 * lots : 0;
   double tpCostCheck = (tickSz2 > 0 && tickVal2 > 0) ?
                        (tp_dist / tickSz2) * tickVal2 * lots : 0;

   string modeStr = (InpSLMode==FIXED_DOLLARS) ? "FIX$" :
                    (InpSLMode==FIXED_POINTS)  ? "FIXpts" : "ATR";

   // ── STEP 4: Execute trade ─────────────────────────────────────
   if(trendDir == 1)
   {
      double sl = NormalizeDouble(ask - sl_dist, symbolDigits);
      double tp = NormalizeDouble(ask + tp_dist, symbolDigits);
      if(Trade.Buy(lots, _Symbol, ask, sl, tp, StringFormat("SG6_BUY|%d|%s", score, modeStr)))
      {
         barsSinceLastTrade = 0;
         g_BuyTrades++;  g_TotalTrades++;
         g_LastSignal = StringFormat("BUY %s $-%.1f/+%.1f", modeStr, slCostCheck, tpCostCheck);
         // Trail registration handled automatically in ManageTrailingStop
         Print("BUY [", modeStr, "] | Lots:", lots,
               " | SL:", sl, " ($-", slCostCheck, ")",
               " | TP:", tp, " ($+", tpCostCheck, ")",
               " | Score:", score, "%");
      }
   }
   else
   {
      double sl = NormalizeDouble(bid + sl_dist, symbolDigits);
      double tp = NormalizeDouble(bid - tp_dist, symbolDigits);
      if(Trade.Sell(lots, _Symbol, bid, sl, tp, StringFormat("SG6_SELL|%d|%s", score, modeStr)))
      {
         barsSinceLastTrade = 0;
         g_SellTrades++; g_TotalTrades++;
         g_LastSignal = StringFormat("SELL %s $-%.1f/+%.1f", modeStr, slCostCheck, tpCostCheck);
         // Trail registration handled automatically in ManageTrailingStop
         Print("SELL [", modeStr, "] | Lots:", lots,
               " | SL:", sl, " ($-", slCostCheck, ")",
               " | TP:", tp, " ($+", tpCostCheck, ")",
               " | Score:", score, "%");
      }
   }
}

//====================================================================
//  PROTECTION TRIGGER — close all trades + stop + alert
//  Called from OnTick every tick so it fires immediately
//====================================================================
void TriggerProtection(string reason)
{
   if(g_DailyLimitHit) return;  // already triggered
   g_DailyLimitHit = true;

   Print("=== PROTECTION TRIGGERED: ", reason, " ===");

   // Close all open trades immediately
   if(InpCloseOnLimit)
      CloseAllMyTrades();

   // Alert popup
   if(InpAlertOnLimit)
   {
      string msg = StringFormat("Smart Grid v6 | %s\n%s\nAll trades closed. EA stopped.", _Symbol, reason);
      Alert(msg);
   }
}

//====================================================================
//  CLOSE ALL MY TRADES (market close)
//====================================================================
void CloseAllMyTrades()
{
   int attempts = 3;
   while(attempts-- > 0)
   {
      bool anyOpen = false;
      for(int i = PositionsTotal() - 1; i >= 0; i--)
      {
         if(!PosInfo.SelectByIndex(i)) continue;
         if(PosInfo.Magic() != InpMagicNumber) continue;
         if(PosInfo.Symbol() != _Symbol) continue;
         anyOpen = true;
         ulong ticket = PosInfo.Ticket();
         bool ok = Trade.PositionClose(ticket, InpSlippage);
         if(ok)
            Print("CLOSED ticket ", ticket, " | P&L: $",
                  DoubleToString(PosInfo.Profit(), 2));
         else
            Print("Close FAILED ticket ", ticket, " | Error:", GetLastError());
      }
      if(!anyOpen) break;
      Sleep(500);
   }
}

//====================================================================
//  ON TRADE TRANSACTION
//====================================================================
void OnTradeTransaction(const MqlTradeTransaction &trans,
                        const MqlTradeRequest &req,
                        const MqlTradeResult &res)
{
   if(trans.type != TRADE_TRANSACTION_DEAL_ADD) return;
   ulong dealTicket = trans.deal;
   if(dealTicket == 0) return;
   if(!HistoryDealSelect(dealTicket)) return;
   if(HistoryDealGetInteger(dealTicket, DEAL_MAGIC) != InpMagicNumber) return;
   if(HistoryDealGetString(dealTicket, DEAL_SYMBOL) != _Symbol) return;

   ENUM_DEAL_ENTRY entry = (ENUM_DEAL_ENTRY)HistoryDealGetInteger(dealTicket, DEAL_ENTRY);
   if(entry != DEAL_ENTRY_OUT) return;

   double profit = HistoryDealGetDouble(dealTicket, DEAL_PROFIT);
   if(profit > 0) { g_WinTrades++; g_ConsecLoss = 0; }
   else           { g_LossTrades++; g_ConsecLoss++; }

   RemoveTrailTicket(HistoryDealGetInteger(dealTicket, DEAL_POSITION_ID));
}

//====================================================================
//  TRAILING STOP — POINTS BASED, PEAK TRACKED, STOPS-LEVEL SAFE
//
//  How it works:
//   BUY : track highest bid reached → SL = peak - InpTrailPoints
//   SELL: track lowest  ask reached → SL = peak + InpTrailPoints
//   SL only moves in profit direction, never backward.
//   Broker stops level enforced — no silent PositionModify failures.
//====================================================================
void ManageTrailingStop()
{
   // Minimum broker stop distance (points → price)
   int    stopsLevelPts  = (int)SymbolInfoInteger(_Symbol, SYMBOL_TRADE_STOPS_LEVEL);
   double stopsLevelDist = (stopsLevelPts + 3) * pointSize;  // +3 pts safety buffer

   // Convert input points → price distances
   double beDist       = InpBreakevenPoints     * pointSize;
   double trailActDist = InpTrailActivatePoints * pointSize;
   double trailDist    = InpTrailPoints         * pointSize;

   // Enforce trail distance >= broker stops level
   trailDist = MathMax(trailDist, stopsLevelDist);

   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      if(!PosInfo.SelectByIndex(i)) continue;
      if(PosInfo.Magic() != InpMagicNumber) continue;
      if(PosInfo.Symbol() != _Symbol) continue;

      ulong  ticket    = PosInfo.Ticket();   // position ticket (MT5 hedging)
      double openPrice = PosInfo.PriceOpen();
      double posSL     = PosInfo.StopLoss();
      double posTP     = PosInfo.TakeProfit();
      bool   isBuy     = (PosInfo.PositionType() == POSITION_TYPE_BUY);

      double curBid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
      double curAsk = SymbolInfoDouble(_Symbol, SYMBOL_ASK);

      // Find/register in peak-tracking array using POSITION TICKET
      int idx = FindTrailIndex(ticket);
      if(idx < 0)
      {
         // Register: initial peak = open price
         RegisterTrailTicket(ticket, openPrice);
         idx = FindTrailIndex(ticket);
         if(idx < 0) continue;
      }

      // ── UPDATE PEAK (HIGH-WATER MARK) ─────────────────────────
      if(isBuy)
         g_TrailPeaks[idx] = MathMax(g_TrailPeaks[idx], curBid);
      else
         g_TrailPeaks[idx] = MathMin(g_TrailPeaks[idx], curAsk);

      double peak = g_TrailPeaks[idx];

      // How far has price EVER moved in our favour (peak vs open)
      double peakProfit   = isBuy ? (peak - openPrice) : (openPrice - peak);
      // How far is price NOW in our favour
      double currentProfit= isBuy ? (curBid - openPrice) : (openPrice - curAsk);

      // ── BREAKEVEN ─────────────────────────────────────────────
      if(InpUseBreakeven && !g_TrailBEDone[idx])
      {
         if(currentProfit >= beDist)
         {
            double newBE;
            if(isBuy)
            {
               // SL to just above open price
               newBE = NormalizeDouble(openPrice + pointSize, symbolDigits);
               // Valid: above current SL, below current bid, not too close
               if(newBE > posSL && (curBid - newBE) >= stopsLevelDist)
               {
                  if(Trade.PositionModify(ticket, newBE, posTP))
                  {
                     g_TrailBEDone[idx] = true;
                     Print("BE SET BUY | ticket:", ticket,
                           " | openPrice:", openPrice, " | newBE:", newBE);
                  }
                  else
                     Print("BE MODIFY FAIL BUY | err:", GetLastError(),
                           " | newBE:", newBE, " | bid:", curBid);
               }
            }
            else // SELL
            {
               // SL to just below open price
               newBE = NormalizeDouble(openPrice - pointSize, symbolDigits);
               // Valid: below current SL (or SL=0), above current ask, not too close
               bool slOK = (posSL == 0 || newBE < posSL);
               if(slOK && (newBE - curAsk) >= stopsLevelDist)
               {
                  if(Trade.PositionModify(ticket, newBE, posTP))
                  {
                     g_TrailBEDone[idx] = true;
                     Print("BE SET SELL | ticket:", ticket,
                           " | openPrice:", openPrice, " | newBE:", newBE);
                  }
                  else
                     Print("BE MODIFY FAIL SELL | err:", GetLastError(),
                           " | newBE:", newBE, " | ask:", curAsk);
               }
            }
         }
      }

      // ── TRAILING STOP ─────────────────────────────────────────
      // Activates once PEAK profit >= trailActDist
      if(!InpUseTrail) continue;
      if(peakProfit < trailActDist) continue;

      if(isBuy)
      {
         // SL = highest bid ever - trail distance
         double newSL = NormalizeDouble(peak - trailDist, symbolDigits);

         // Conditions (all must be true):
         // 1. New SL is higher than current SL (only move forward)
         // 2. New SL is safely below current bid (not too close)
         bool c1 = (newSL > posSL);
         bool c2 = ((curBid - newSL) >= stopsLevelDist);

         if(c1 && c2)
         {
            if(Trade.PositionModify(ticket, newSL, posTP))
               Print("TRAIL BUY | ticket:", ticket,
                     " | peak:", peak, " | newSL:", newSL,
                     " | bid:", curBid,
                     " | locked: $", DoubleToString((newSL - openPrice) * 100, 1));
            else
               Print("TRAIL MODIFY FAIL BUY | err:", GetLastError(),
                     " | newSL:", newSL, " | bid:", curBid,
                     " | stopsLvl:", stopsLevelPts);
         }
      }
      else // SELL
      {
         // SL = lowest ask ever + trail distance
         double newSL = NormalizeDouble(peak + trailDist, symbolDigits);

         // Conditions:
         // 1. New SL is lower than current SL (only move forward for sell)
         // 2. New SL is safely above current ask (not too close)
         bool c1 = (posSL == 0 || newSL < posSL);
         bool c2 = ((newSL - curAsk) >= stopsLevelDist);

         if(c1 && c2)
         {
            if(Trade.PositionModify(ticket, newSL, posTP))
               Print("TRAIL SELL | ticket:", ticket,
                     " | peak:", peak, " | newSL:", newSL,
                     " | ask:", curAsk,
                     " | locked: $", DoubleToString((openPrice - newSL) * 100, 1));
            else
               Print("TRAIL MODIFY FAIL SELL | err:", GetLastError(),
                     " | newSL:", newSL, " | ask:", curAsk,
                     " | stopsLvl:", stopsLevelPts);
         }
      }
   }
}

//====================================================================
//  TRAIL REGISTRY
//====================================================================
void RegisterTrailTicket(ulong ticket, double openPrice)
{
   if(FindTrailIndex(ticket) >= 0) return;
   int n = ArraySize(g_TrailTickets);
   ArrayResize(g_TrailTickets, n + 1);
   ArrayResize(g_TrailPeaks,   n + 1);
   ArrayResize(g_TrailBEDone,  n + 1);
   g_TrailTickets[n] = ticket;
   g_TrailPeaks[n]   = openPrice;
   g_TrailBEDone[n]  = false;
}

int FindTrailIndex(ulong ticket)
{
   for(int i = 0; i < ArraySize(g_TrailTickets); i++)
      if(g_TrailTickets[i] == ticket) return i;
   return -1;
}

void RemoveTrailTicket(ulong positionId)
{
   int idx = FindTrailIndex(positionId);
   if(idx < 0) return;
   int n = ArraySize(g_TrailTickets);
   for(int i = idx; i < n - 1; i++)
   {
      g_TrailTickets[i] = g_TrailTickets[i + 1];
      g_TrailPeaks[i]   = g_TrailPeaks[i + 1];
      g_TrailBEDone[i]  = g_TrailBEDone[i + 1];
   }
   ArrayResize(g_TrailTickets, n - 1);
   ArrayResize(g_TrailPeaks,   n - 1);
   ArrayResize(g_TrailBEDone,  n - 1);
}

//====================================================================
//  ENTRY PROBABILITY SCORE (0–100)
//====================================================================
int CalcEntryProbability(int dir)
{
   int score = 0;

   // 1. EMA alignment — 20 pts
   if(InpUseTrendFilter)
   {
      if(dir ==  1 && fastEMAVal > slowEMAVal) score += 20;
      if(dir == -1 && fastEMAVal < slowEMAVal) score += 20;
   }
   else score += 20;

   if(InpUseMidEMA)
   {
      score -= 10;
      if(dir ==  1 && fastEMAVal > midEMAVal && midEMAVal > slowEMAVal) score += 10;
      if(dir == -1 && fastEMAVal < midEMAVal && midEMAVal < slowEMAVal) score += 10;
   }

   // 2. ADX strength — 20 pts
   if     (adxVal >= 35) score += 20;
   else if(adxVal >= 28) score += 15;
   else if(adxVal >= 22) score += 10;
   else                  score += 5;

   // 3. DI confirms direction — 15 pts
   if(dir ==  1 && adxPlus  > adxMinus) score += 15;
   if(dir == -1 && adxMinus > adxPlus)  score += 15;

   // 4. RSI zone — 15 pts
   if(dir == 1) {
      if(rsiVal >= 45 && rsiVal <= 65) score += 15;
      else if(rsiVal >= 40 && rsiVal <= 70) score += 8;
   } else {
      if(rsiVal >= 35 && rsiVal <= 55) score += 15;
      else if(rsiVal >= 30 && rsiVal <= 60) score += 8;
   }

   // 5. Candle direction — 10 pts
   double c1c = iClose(_Symbol, PERIOD_CURRENT, 1);
   double c1o = iOpen( _Symbol, PERIOD_CURRENT, 1);
   double c1h = iHigh( _Symbol, PERIOD_CURRENT, 1);
   double c1l = iLow(  _Symbol, PERIOD_CURRENT, 1);
   double body = MathAbs(c1c - c1o), range = c1h - c1l;
   if(dir ==  1 && c1c > c1o) score += 10;
   if(dir == -1 && c1c < c1o) score += 10;

   // 6. Strong body ratio — 5 pts
   if(range > 0 && (body / range) >= 0.6) score += 5;

   // 7. Previous candle — 5 pts
   double c2c = iClose(_Symbol, PERIOD_CURRENT, 2);
   double c2o = iOpen( _Symbol, PERIOD_CURRENT, 2);
   if(dir ==  1 && c2c > c2o) score += 5;
   if(dir == -1 && c2c < c2o) score += 5;

   // 8. Not overextended from fast EMA — 10 pts
   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   double mx  = atrVal * 1.5;
   if(dir == 1 && (ask - fastEMAVal) >= 0 && (ask - fastEMAVal) <= mx) score += 10;
   if(dir == -1 && (fastEMAVal - bid) >= 0 && (fastEMAVal - bid) <= mx) score += 10;

   // 9. DI crossover penalty — -15 pts
   double pDIPlus[1], pDIMinus[1];
   if(CopyBuffer(hADX, 1, 2, 1, pDIPlus)  == 1 &&
      CopyBuffer(hADX, 2, 2, 1, pDIMinus) == 1)
   {
      if(dir ==  1 && pDIPlus[0]  > pDIMinus[0] && adxPlus  < adxMinus) score -= 15;
      if(dir == -1 && pDIMinus[0] > pDIPlus[0]  && adxMinus < adxPlus)  score -= 15;
   }

   return MathMax(0, MathMin(100, score));
}

//====================================================================
//  TREND DIRECTION
//====================================================================
int GetTrendDirection()
{
   if(InpUseTrendFilter)
   {
      if(InpUseMidEMA)
      {
         if(fastEMAVal > midEMAVal && midEMAVal > slowEMAVal) return  1;
         if(fastEMAVal < midEMAVal && midEMAVal < slowEMAVal) return -1;
         return 0;
      }
      if(fastEMAVal > slowEMAVal) return  1;
      if(fastEMAVal < slowEMAVal) return -1;
      return 0;
   }
   double c1 = iClose(_Symbol, PERIOD_CURRENT, 1);
   double c2 = iClose(_Symbol, PERIOD_CURRENT, 2);
   double c3 = iClose(_Symbol, PERIOD_CURRENT, 3);
   if(c1 > c2 && c2 > c3) return  1;
   if(c1 < c2 && c2 < c3) return -1;
   return 0;
}

//====================================================================
//  REFRESH INDICATORS
//====================================================================
bool RefreshIndicators()
{
   double aBuf[1],fBuf[1],sBuf[1],mBuf[1];
   double adxBuf[1],diPlus[1],diMinus[1],rsiBuf[1];
   double bbUp[1],bbLow[1],bbMid[1];

   if(CopyBuffer(hATR,     0, 1, 1, aBuf)    < 1) return false;
   if(CopyBuffer(hFastEMA, 0, 0, 1, fBuf)    < 1) return false;
   if(CopyBuffer(hSlowEMA, 0, 0, 1, sBuf)    < 1) return false;
   if(CopyBuffer(hMidEMA,  0, 0, 1, mBuf)    < 1) return false;
   if(CopyBuffer(hADX,     0, 1, 1, adxBuf)  < 1) return false;
   if(CopyBuffer(hADX,     1, 1, 1, diPlus)  < 1) return false;
   if(CopyBuffer(hADX,     2, 1, 1, diMinus) < 1) return false;
   if(CopyBuffer(hRSI,     0, 1, 1, rsiBuf)  < 1) return false;
   if(CopyBuffer(hBB,      1, 0, 1, bbUp)    < 1) return false;
   if(CopyBuffer(hBB,      2, 0, 1, bbLow)   < 1) return false;
   if(CopyBuffer(hBB,      0, 0, 1, bbMid)   < 1) return false;

   atrVal    = aBuf[0];   fastEMAVal = fBuf[0];
   slowEMAVal= sBuf[0];   midEMAVal  = mBuf[0];
   adxVal    = adxBuf[0]; adxPlus    = diPlus[0]; adxMinus = diMinus[0];
   rsiVal    = rsiBuf[0];
   bbUpper   = bbUp[0];   bbLower    = bbLow[0];  bbMiddle = bbMid[0];

   if(atrVal <= 0) return false;
   return true;
}

//====================================================================
//  SL / TP CALCULATOR — returns price-distance for SL and TP
//  Works for all 3 modes. lot is needed only for FIXED_DOLLARS.
//====================================================================
bool CalcSLTP(int dir, double lots, double &sl_dist, double &tp_dist)
{
   double tickValue = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE);
   double tickSize  = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);

   if(InpSLMode == FIXED_DOLLARS)
   {
      // Convert USD target → price distance
      // price_dist = USD / (lots * tickValue / tickSize)
      if(lots <= 0 || tickValue <= 0 || tickSize <= 0) return false;
      double usdPerPrice = lots * tickValue / tickSize;
      if(usdPerPrice <= 0) return false;
      sl_dist = NormalizeDouble(InpFixedSL_USD / usdPerPrice, symbolDigits);
      tp_dist = NormalizeDouble(InpFixedTP_USD / usdPerPrice, symbolDigits);
   }
   else if(InpSLMode == FIXED_POINTS)
   {
      sl_dist = NormalizeDouble(InpFixedSL_Points * pointSize, symbolDigits);
      tp_dist = NormalizeDouble(InpFixedTP_Points * pointSize, symbolDigits);
   }
   else // ATR_BASED
   {
      sl_dist = NormalizeDouble(atrVal * InpATR_SL_Mult, symbolDigits);
      tp_dist = NormalizeDouble(atrVal * InpATR_TP_Mult, symbolDigits);
      // ATR mode: check dollar cap
      if(InpMaxSL_USD > 0 && lots > 0 && tickValue > 0 && tickSize > 0)
      {
         double slCostUSD = (sl_dist / tickSize) * tickValue * lots;
         if(slCostUSD > InpMaxSL_USD)
         {
            g_LastSignal = StringFormat("ATR SL TOO BIG ($%.1f>$%.1f)", slCostUSD, InpMaxSL_USD);
            return false;
         }
      }
   }

   if(sl_dist <= 0 || tp_dist <= 0) return false;
   return true;
}

//====================================================================
//  LOT SIZE (ATR / FIXED_POINTS mode)
//====================================================================
double CalcLotSize(double slDistance)
{
   double lots = InpLotSize;
   if(InpAutoLot && slDistance > 0)
   {
      double capital   = (g_BaseCapital > 0) ? g_BaseCapital : AccountInfoDouble(ACCOUNT_BALANCE);
      double tickValue = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE);
      double tickSize  = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
      double riskAmt   = capital * InpRiskPercent / 100.0;
      if(tickSize > 0 && tickValue > 0)
         lots = riskAmt / ((slDistance / tickSize) * tickValue);
   }
   return NormalizeLot(lots);
}

//  Normalize lot to broker limits
double NormalizeLot(double lots)
{
   double step   = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
   double minLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double maxLot = MathMin(InpMaxLot, SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX));
   lots = MathFloor(lots / step) * step;
   return NormalizeDouble(MathMax(minLot, MathMin(maxLot, lots)), 2);
}

//====================================================================
//  COUNT OPEN TRADES
//====================================================================
int CountMyTrades()
{
   int count = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
      if(PosInfo.SelectByIndex(i))
         if(PosInfo.Magic() == InpMagicNumber && PosInfo.Symbol() == _Symbol)
            count++;
   return count;
}

//====================================================================
//  SESSION
//====================================================================
bool IsInSession()
{
   MqlDateTime dt;
   TimeToStruct(TimeGMT(), dt);
   int h = dt.hour;
   bool inLondon = (h >= InpLondonOpen && h < InpLondonClose);
   bool inNY     = (h >= InpNYOpen     && h < InpNYClose);
   if(inLondon || inNY) return true;
   if(InpTradeAsian && !inLondon && !inNY) return true;
   return false;
}

//====================================================================
//  DAILY RESET
//====================================================================
void CheckDailyReset()
{
   MqlDateTime dt;
   TimeToStruct(TimeCurrent(), dt);
   datetime dayStart = StringToTime(StringFormat("%04d.%02d.%02d 00:00",
                                    dt.year, dt.mon, dt.day));
   if(dayStart != lastDayCheck)
   {
      dailyStartBalance = AccountInfoDouble(ACCOUNT_BALANCE);
      g_DayHighEquity   = AccountInfoDouble(ACCOUNT_EQUITY);
      lastDayCheck      = dayStart;
      g_DailyLimitHit   = false;
      g_AlertSent       = false;
      g_LimitReason     = "";
      g_ConsecLoss      = 0;
      Print("Daily reset | Balance: $", DoubleToString(dailyStartBalance, 2));
   }
}

//====================================================================
//  LOAD HISTORY STATS
//====================================================================
void LoadHistoryStats()
{
   HistorySelect(0, TimeCurrent());
   int total = HistoryDealsTotal();
   for(int i = 0; i < total; i++)
   {
      ulong t = HistoryDealGetTicket(i);
      if(HistoryDealGetInteger(t, DEAL_MAGIC)  != InpMagicNumber) continue;
      if(HistoryDealGetString(t,  DEAL_SYMBOL) != _Symbol)        continue;
      ENUM_DEAL_ENTRY e = (ENUM_DEAL_ENTRY)HistoryDealGetInteger(t, DEAL_ENTRY);
      if(e != DEAL_ENTRY_OUT) continue;
      double p = HistoryDealGetDouble(t, DEAL_PROFIT);
      if(p > 0) { g_WinTrades++;  g_ConsecLoss = 0; }
      else      { g_LossTrades++; g_ConsecLoss++; }
      ENUM_DEAL_TYPE dt2 = (ENUM_DEAL_TYPE)HistoryDealGetInteger(t, DEAL_TYPE);
      if(dt2 == DEAL_TYPE_BUY)  g_BuyTrades++;
      if(dt2 == DEAL_TYPE_SELL) g_SellTrades++;
   }
   g_TotalTrades = g_WinTrades + g_LossTrades;
}

//====================================================================
//  PANEL — CREATE
//====================================================================
void CreatePanel()
{
   string bg = PANEL_PREFIX + "BG";
   ObjectCreate(0, bg, OBJ_RECTANGLE_LABEL, 0, 0, 0);
   ObjectSetInteger(0, bg, OBJPROP_XDISTANCE,  InpPanelX);
   ObjectSetInteger(0, bg, OBJPROP_YDISTANCE,  InpPanelY);
   ObjectSetInteger(0, bg, OBJPROP_XSIZE,      280);
   ObjectSetInteger(0, bg, OBJPROP_YSIZE,      490);
   ObjectSetInteger(0, bg, OBJPROP_BGCOLOR,    InpPanelBG);
   ObjectSetInteger(0, bg, OBJPROP_BORDER_TYPE,BORDER_FLAT);
   ObjectSetInteger(0, bg, OBJPROP_COLOR,      clrGold);
   ObjectSetInteger(0, bg, OBJPROP_CORNER,     CORNER_LEFT_UPPER);
   ObjectSetInteger(0, bg, OBJPROP_BACK,       false);
   ObjectSetInteger(0, bg, OBJPROP_SELECTABLE, false);

   string rows[] = {
      "TITLE","D1",
      "MARKET","TREND","ADX","BB","RSI","SESSION","D2",
      "SCORE","SIGNAL","D3",
      "OPENTRADES","BUY_CNT","SELL_CNT","D4",
      "WINRATE","WINS","LOSSES","CONSEC","D5",
      "DAILY_PNL","DAILY_LIMITS","PEAK_EQUITY","D6",
      "TRAIL_INFO","BASE_CAP","SHIELD_INFO","STATUS"
   };

   for(int i = 0; i < ArraySize(rows); i++)
   {
      string name = PANEL_PREFIX + rows[i];
      ObjectCreate(0, name, OBJ_LABEL, 0, 0, 0);
      ObjectSetInteger(0, name, OBJPROP_XDISTANCE,  InpPanelX + 8);
      ObjectSetInteger(0, name, OBJPROP_YDISTANCE,  InpPanelY + 8 + i * 17);
      ObjectSetInteger(0, name, OBJPROP_CORNER,     CORNER_LEFT_UPPER);
      ObjectSetInteger(0, name, OBJPROP_FONTSIZE,   8);
      ObjectSetString( 0, name, OBJPROP_FONT,       "Consolas");
      ObjectSetInteger(0, name, OBJPROP_COLOR,      InpColorText);
      ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
      ObjectSetInteger(0, name, OBJPROP_BACK,       false);
   }
   ChartRedraw(0);
}

//====================================================================
//  PANEL — UPDATE
//====================================================================
void UpdatePanel()
{
   double equity  = AccountInfoDouble(ACCOUNT_EQUITY);
   g_DailyPnL     = equity - dailyStartBalance;
   double peakDD  = g_DayHighEquity - equity;  // current drawdown from peak
   int    openPos = CountMyTrades();
   double winRate = (g_TotalTrades > 0) ? (double)g_WinTrades / g_TotalTrades * 100.0 : 0.0;
   double bbWidth = bbUpper - bbLower;

   string sessionStr = "---";
   if(InpUseSession) {
      MqlDateTime dt; TimeToStruct(TimeGMT(), dt); int h = dt.hour;
      if(h >= InpLondonOpen && h < InpLondonClose)     sessionStr = "LONDON";
      else if(h >= InpNYOpen && h < InpNYClose)        sessionStr = "NEW YORK";
      else if(InpTradeAsian)                           sessionStr = "ASIAN";
      else                                             sessionStr = "OFF";
   }

   color marketColor = (g_MarketStatus == "TRENDING") ? InpColorBull : InpColorBear;
   color adxColor    = (adxVal >= InpADXMinLevel) ? InpColorBull : InpColorBear;
   color bbColor     = (bbWidth >= InpBBMinWidth) ? InpColorBull : InpColorBear;
   color trendColor  = (fastEMAVal > slowEMAVal) ? InpColorBull : InpColorBear;
   color rsiColor    = clrDodgerBlue;
   if(rsiVal > InpRSIOB) rsiColor = InpColorBear;
   if(rsiVal < InpRSIOS) rsiColor = InpColorBull;
   color scoreColor  = InpColorBear;
   if(g_LastScore >= 75) scoreColor = InpColorBull;
   else if(g_LastScore >= InpMinProbability) scoreColor = clrYellow;
   color dailyColor  = (g_DailyPnL >= 0) ? InpColorBull : InpColorBear;
   color shieldColor = (peakDD >= InpEquityShieldUSD * 0.7) ? InpColorWarning : InpColorNeutral;
   color consecColor = (g_ConsecLoss >= InpMaxConsecLoss && InpMaxConsecLoss > 0)
                       ? InpColorWarning : InpColorText;

   string statusStr  = g_DailyLimitHit
                       ? StringFormat("■ STOPPED: %s", g_LimitReason)
                       : "● ACTIVE — trading";
   color  statusColor= g_DailyLimitHit ? InpColorWarning : InpColorBull;

   SetLabel("TITLE",       "┌── SMART GRID v6 SCALPER ───┐", InpColorTitle);
   SetLabel("D1",          "├── MARKET ──────────────────┤", InpColorNeutral);
   SetLabel("MARKET",      StringFormat("  Status : %-10s", g_MarketStatus), marketColor);
   SetLabel("TREND",       StringFormat("  EMA %d/%d : %s", InpFastEMA, InpSlowEMA,
                              (fastEMAVal > slowEMAVal) ? "▲ BULL" : "▼ BEAR"), trendColor);
   SetLabel("ADX",         StringFormat("  ADX   : %.1f (min %.1f) %s",
                              adxVal, InpADXMinLevel,
                              (adxVal >= InpADXMinLevel) ? "✓" : "✗"), adxColor);
   SetLabel("BB",          StringFormat("  BBw   : %.2f (min %.2f) %s",
                              bbWidth, InpBBMinWidth,
                              (bbWidth >= InpBBMinWidth) ? "✓" : "✗"), bbColor);
   SetLabel("RSI",         StringFormat("  RSI   : %.1f  %s", rsiVal,
                              (rsiVal > InpRSIOB) ? "OB" :
                              (rsiVal < InpRSIOS) ? "OS" : "OK"), rsiColor);
   SetLabel("SESSION",     StringFormat("  Sess  : %s", sessionStr), InpColorText);
   SetLabel("D2",          "├── SIGNAL ───────────────────┤", InpColorNeutral);
   SetLabel("SCORE",       StringFormat("  Prob  : %d%%  [min %d%%]",
                              g_LastScore, InpMinProbability), scoreColor);
   SetLabel("SIGNAL",      StringFormat("  Last  : %s", g_LastSignal), InpColorText);
   SetLabel("D3",          "├── POSITIONS ────────────────┤", InpColorNeutral);
   SetLabel("OPENTRADES",  StringFormat("  Open  : %d / %d", openPos, InpMaxTrades), InpColorText);
   SetLabel("BUY_CNT",     StringFormat("  Buys  : %d", g_BuyTrades), InpColorBull);
   SetLabel("SELL_CNT",    StringFormat("  Sells : %d", g_SellTrades), InpColorBear);
   SetLabel("D4",          "├── STATISTICS ───────────────┤", InpColorNeutral);
   SetLabel("WINRATE",     StringFormat("  WR    : %.1f%%  (%d trades)",
                              winRate, g_TotalTrades), scoreColor);
   SetLabel("WINS",        StringFormat("  Wins  : %d", g_WinTrades), InpColorBull);
   SetLabel("LOSSES",      StringFormat("  Losses: %d", g_LossTrades), InpColorBear);
   SetLabel("CONSEC",      StringFormat("  Consec: %d loss%s", g_ConsecLoss,
                              (InpMaxConsecLoss > 0) ?
                              StringFormat(" / %d limit", InpMaxConsecLoss) : ""), consecColor);
   SetLabel("D5",          "├── LOSS PROTECTION ($) ──────┤", InpColorNeutral);
   SetLabel("DAILY_PNL",   StringFormat("  P&L   : %+.2f$  today", g_DailyPnL), dailyColor);
   SetLabel("DAILY_LIMITS",StringFormat("  Limits: +$%.0f / -$%.0f",
                              InpDailyProfitUSD, InpDailyLossUSD), InpColorNeutral);
   SetLabel("PEAK_EQUITY", StringFormat("  Drawdn: -$%.2f  (shield -$%.0f)",
                              peakDD, InpEquityShieldUSD), shieldColor);
   SetLabel("D6",          "├── SETTINGS ─────────────────┤", InpColorNeutral);
   SetLabel("TRAIL_INFO",  StringFormat("  Trail : BE@%dpt | Act@%dpt | Step@%dpt",
                              InpBreakevenPoints, InpTrailActivatePoints, InpTrailPoints),
                           clrDodgerBlue);
   SetLabel("BASE_CAP",    StringFormat("  Base  : $%.0f  Risk:%.1f%%",
                              g_BaseCapital, InpRiskPercent), InpColorNeutral);
   string slModeStr = (InpSLMode==FIXED_DOLLARS) ?
                      StringFormat("FIX$ SL:-$%.0f TP:+$%.0f", InpFixedSL_USD, InpFixedTP_USD) :
                      (InpSLMode==FIXED_POINTS) ?
                      StringFormat("FIXpts SL:%dpt TP:%dpt", InpFixedSL_Points, InpFixedTP_Points) :
                      StringFormat("ATR x%.1f/%.1f cap$%.0f", InpATR_SL_Mult, InpATR_TP_Mult, InpMaxSL_USD);
   SetLabel("SHIELD_INFO", StringFormat("  SL    : %s", slModeStr), clrCyan);
   SetLabel("STATUS",      StringFormat("  %s", statusStr), statusColor);

   ChartRedraw(0);
}

//====================================================================
//  PANEL HELPERS
//====================================================================
void SetLabel(string key, string text, color clr)
{
   string name = PANEL_PREFIX + key;
   ObjectSetString( 0, name, OBJPROP_TEXT,  text);
   ObjectSetInteger(0, name, OBJPROP_COLOR, clr);
}

void DeletePanel()
{
   ObjectsDeleteAll(0, PANEL_PREFIX);
}

//====================================================================
//  END OF FILE
//====================================================================
