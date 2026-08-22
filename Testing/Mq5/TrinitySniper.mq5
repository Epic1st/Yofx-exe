//+------------------------------------------------------------------+
//|                                               TrinitySniper.mq5  |
//|                                          Trinity Traders © 2024  |
//|               Support & Resistance Hedging EA — XAUUSD M1        |
//|                     Converted to MQL5 / MT5                      |
//+------------------------------------------------------------------+
#property copyright "Trinity Traders"
#property link      ""
#property version   "1.82"

#include <Trade\Trade.mqh>
#include <Trade\PositionInfo.mqh>

CTrade trade;

//==================================================================//
//                        INPUT PARAMETERS                          //
//==================================================================//

// ─────────────────────────────────────────────────────────────────
//  SECTION 1 — CORE / LOT SIZING
//  Controls how large each trade is and how lots grow after a loss.
// ─────────────────────────────────────────────────────────────────
input string  _Sec1_           = "━━━ 1. LOT SIZING ━━━━━━━━━━━━━━━━━━━━━━━━━";
input double  InitialLotSize   = 0.01;   // Starting lot size for the first trade in every sequence
input bool    EnableFixedLot   = false;  // [Fixed Lot Mode] If ON every trade uses InitialLotSize (no martingale growth)
input double  MartingaleMultiplier = 1.075; // [Martingale Multiplier] Each new trade lot = previous lot × this value (ignored when Fixed Lot is ON)
input int     MagicNumber      = 202401; // Unique ID — keep different for each chart/pair
input int     Slippage         = 30;     // Maximum allowed slippage in points before order is rejected

// ─────────────────────────────────────────────────────────────────
//  SECTION 2 — DEGRESSIVE MARTINGALE
//  Gradually reduces the multiplier on long sequences to protect
//  against run-away lot compounding on deep drawdowns.
// ─────────────────────────────────────────────────────────────────
input string  _Sec2_                  = "━━━ 2. DEGRESSIVE MARTINGALE ━━━━━━━━━━━━";
input bool    EnableDegressive        = true;  // [Enable] Automatically reduce the multiplier on long sequences
input int     DegressiveStartTrade    = 15;    // [Start At Trade #] Reduction begins after this many trades in one sequence
input int     DegressiveStepTrades    = 5;     // [Step Interval] Multiplier drops every N additional trades
input double  DegressiveStepSize      = 0.01;  // [Step Reduction] Amount subtracted from multiplier each step (e.g. 0.01 → 1.075→1.065→1.055…)
input double  DegressiveMinMultiplier = 1.05;  // [Minimum Multiplier] Multiplier will never fall below this floor

// ─────────────────────────────────────────────────────────────────
//  SECTION 3 — ATR (AVERAGE TRUE RANGE)
//  ATR is used to set zone widths and grid spacing dynamically
//  based on current market volatility.
// ─────────────────────────────────────────────────────────────────
input string  _Sec3_              = "━━━ 3. ATR — VOLATILITY SETTINGS ━━━━━━━━━";
input int     ATR_Period          = 14;   // [ATR Period] Number of candles used to calculate average volatility
input double  ATR_ZoneMultiplier  = 0.5;  // [Zone Width] Support/Resistance zone half-width = ATR × this value
input double  ATR_GridMultiplier  = 0.3;  // [Base Grid Step] Distance between grid trades = ATR × this value (before progressive expansion)

// ─────────────────────────────────────────────────────────────────
//  SECTION 4 — SUPPORT & RESISTANCE DETECTION
//  The EA identifies zones from recent swing highs/lows and opens
//  trades when price enters those zones.
// ─────────────────────────────────────────────────────────────────
input string  _Sec4_        = "━━━ 4. SUPPORT & RESISTANCE ━━━━━━━━━━━━━━";
input int     SR_Lookback   = 30; // [Lookback Candles] How many closed candles to scan for the highest high and lowest low

// ─────────────────────────────────────────────────────────────────
//  SECTION 5 — GRID SETTINGS
//  After the initial trade, the EA adds more trades in the same
//  direction as price moves further away (dollar-cost averaging).
//  The progressive option widens the grid spacing on deeper levels
//  to reduce over-trading in fast-moving markets.
// ─────────────────────────────────────────────────────────────────
input string  _Sec5_                  = "━━━ 5. GRID SETTINGS ━━━━━━━━━━━━━━━━━━━";
input bool    EnableGrid              = true;   // [Enable Grid] Add extra trades as price moves against the position
input int     ProgressiveGridStart    = 5;      // [Progressive Start Level] Grid spacing stays fixed for levels 1→N-1, then begins expanding
input double  ProgressiveGridStep     = 0.1;    // [Expansion Per Level] Each level beyond the start adds this many × ATR to the previous step distance
input double  ProgressiveGridCapATR   = 1.0;    // [Maximum Grid Step Cap] Grid step will never exceed this many × ATR regardless of how many levels deep (prevents extreme spacing)

// ─────────────────────────────────────────────────────────────────
//  SECTION 6 — BREAK-EVEN & TRAILING STOP
//  Instead of a fixed dollar target, the EA calculates the single
//  price level where ALL open trades (buys + sells, all lot sizes)
//  would close at zero net profit, then arms a trailing stop once
//  price has moved a set distance past that break-even level.
// ─────────────────────────────────────────────────────────────────
input string  _Sec6_                   = "━━━ 6. BREAK-EVEN & TRAILING STOP ━━━━━━━";
input bool    EnableTrailingStop       = true;   // [Enable] Activate trailing stop system (replaces the old $ TP target)
input double  TrailActivationPips      = 1000.0; // [Activation Distance (pips)] How far price must move PAST break-even before the trailing stop arms
input double  TrailDistancePips        = 300.0;  // [Trail Distance (pips)] Once armed, the stop loss is kept this many pips behind the best price seen
input bool    EnableLegacyDollarTP     = false;  // [Legacy $ TP] Also keep the old dollar-profit close logic running alongside the trail (useful during testing)
input double  BaseTPDollars            = 5.0;    // [Base $ Target] Used only when Legacy $ TP is ON — dollar target for trade 1
input double  TPTradeMultiplier        = 1.1;    // [$ Target Multiplier] Used only when Legacy $ TP is ON — target grows by this factor per additional trade

// ─────────────────────────────────────────────────────────────────
//  SECTION 7 — TRADING HOURS
//  The EA will only OPEN new sequences within the allowed window.
//  Existing open positions are managed (trailed / closed) at all
//  times regardless of session hours.
// ─────────────────────────────────────────────────────────────────
input string  _Sec7_              = "━━━ 7. TRADING HOURS (UTC) ━━━━━━━━━━━━━━";
input bool    EnableTradingHours  = true;  // [Enable Hours Filter] Restrict new sequence openings to the window below
input int     TradingHourStart    = 7;     // [Session Open Hour, UTC] New sequences allowed from this hour (0–23)
input int     TradingHourEnd      = 18;    // [Session Close Hour, UTC] No new sequences at or after this hour (0–23) — default 18:00 UTC = 20:00 SAST

// ─────────────────────────────────────────────────────────────────
//  SECTION 8 — DAILY PROFIT LIMIT
//  Once the account has gained the target % today (realised +
//  floating), no new sequences will be opened for the rest of the
//  trading day. Existing trades continue to be managed normally.
// ─────────────────────────────────────────────────────────────────
input string  _Sec8_                  = "━━━ 8. DAILY PROFIT LIMIT ━━━━━━━━━━━━━━";
input bool    EnableDailyProfitLimit  = true;  // [Enable] Stop opening new sequences once daily target is reached
input double  DailyProfitTargetPct    = 5.0;   // [Daily Target %] Stop new sequences when today's total P&L reaches this % of opening balance

// ─────────────────────────────────────────────────────────────────
//  SECTION 9 — CHART DISPLAY
// ─────────────────────────────────────────────────────────────────
input string  _Sec9_             = "━━━ 9. CHART DISPLAY ━━━━━━━━━━━━━━━━━━━━";
input color   ResistanceColor    = clrRed;         // Colour of the Resistance zone box on the chart
input color   SupportColor       = clrDodgerBlue;  // Colour of the Support zone box on the chart
input bool    ShowStats          = true;            // [Show Stats Panel] Display the live information panel top-left

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
double   g_LastGridBuyStep   = 0;
double   g_LastGridSellStep  = 0;

bool     g_ZoneLocked     = false;
int      g_ZoneLockDir    = 0;

datetime g_LastCandleTime = 0;

double   g_RealLastLot    = 0;
double   g_FixedTargetUSD = 0;   // kept for legacy path

// Break-even & trailing state
bool     g_TrailArmed        = false;  // true once price has passed BE + activation distance
double   g_TrailBestPrice    = 0;      // best (most favourable) price seen after trail armed
double   g_TrailStopPrice    = 0;      // current trailing stop level applied to all trades

// Daily profit tracking
double   g_DayStartBalance   = 0;
datetime g_LastDayChecked    = 0;
bool     g_DailyLimitReached = false;

int      g_ATR_Handle        = INVALID_HANDLE;

#define OBJ_RES_BOX    "TS_ResBox"
#define OBJ_SUP_BOX    "TS_SupBox"
#define OBJ_WATERMARK  "TS_Watermark"
#define OBJ_BE_LINE    "TS_BELine"
#define OBJ_TRAIL_LINE "TS_TrailLine"

//==================================================================//
//                    DAILY PROFIT TRACKING                         //
//==================================================================//
void UpdateDailyProfitTracking()
{
   if(!EnableDailyProfitLimit) return;

   MqlDateTime dt;
   TimeToStruct(TimeGMT(), dt);
   dt.hour = 0; dt.min = 0; dt.sec = 0;
   datetime todayMidnight = StructToTime(dt);

   if(todayMidnight != g_LastDayChecked)
   {
      g_DayStartBalance   = AccountInfoDouble(ACCOUNT_BALANCE);
      g_LastDayChecked    = todayMidnight;
      g_DailyLimitReached = false;
      Print("Trinity Sniper: New day snapshot — balance=$", DoubleToString(g_DayStartBalance, 2),
            "  daily target=", DoubleToString(DailyProfitTargetPct, 2), "%");
   }

   if(g_DailyLimitReached) return;
   if(g_DayStartBalance <= 0) return;

   double realisedPL = AccountInfoDouble(ACCOUNT_BALANCE) - g_DayStartBalance;
   double floatPL    = GetNetFloatPL();
   double totalDayPL = realisedPL + floatPL;
   double targetUSD  = g_DayStartBalance * DailyProfitTargetPct / 100.0;

   if(totalDayPL >= targetUSD)
   {
      g_DailyLimitReached = true;
      Print("Trinity Sniper: Daily profit limit reached!  P&L=$", DoubleToString(totalDayPL, 2),
            "  Target=$", DoubleToString(targetUSD, 2),
            " (", DoubleToString(DailyProfitTargetPct, 2), "%).",
            "  No new sequences will open today.");
   }
}

//==================================================================//
//              NEW SEQUENCE GUARD — hours + daily limit            //
//==================================================================//
bool IsNewSequenceAllowed()
{
   if(EnableDailyProfitLimit && g_DailyLimitReached) return(false);

   if(EnableTradingHours)
   {
      MqlDateTime dt;
      TimeToStruct(TimeGMT(), dt);
      int h = dt.hour;
      if(TradingHourStart <= TradingHourEnd)
      {
         // Normal window e.g. 07:00–18:00
         if(h < TradingHourStart || h >= TradingHourEnd) return(false);
      }
      else
      {
         // Overnight window e.g. 22:00–06:00
         if(h < TradingHourStart && h >= TradingHourEnd) return(false);
      }
   }
   return(true);
}

//==================================================================//
//                             INIT                                 //
//==================================================================//
int OnInit()
{
   trade.SetExpertMagicNumber(MagicNumber);
   trade.SetDeviationInPoints(Slippage);

   if(TradingHourStart == TradingHourEnd && EnableTradingHours)
   {
      Print("Trinity Sniper: WARNING — TradingHourStart equals TradingHourEnd. No trades will ever open. Please fix the session hours.");
      return(INIT_FAILED);
   }

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
   Print("Trinity Sniper v1.82 (MQL5) Initialized. Session UTC ", TradingHourStart, ":00 – ", TradingHourEnd, ":00");
   return(INIT_SUCCEEDED);
}

void OnDeinit(const int reason)
{
   if(g_ATR_Handle != INVALID_HANDLE) IndicatorRelease(g_ATR_Handle);
   DeletePanelRows();
   ObjectDelete(0, OBJ_RES_BOX);
   ObjectDelete(0, OBJ_SUP_BOX);
   ObjectDelete(0, OBJ_WATERMARK);
   ObjectDelete(0, OBJ_BE_LINE);
   ObjectDelete(0, OBJ_TRAIL_LINE);
}

//==================================================================//
//                          MAIN TICK                               //
//==================================================================//
void OnTick()
{
   UpdateDailyProfitTracking();

   if(g_InSequence)
   {
      // Legacy dollar-TP path (optional)
      if(EnableLegacyDollarTP) CheckNetProfitTarget();

      // New break-even + trailing path
      if(EnableTrailingStop) ManageBreakEvenTrail();

      // Grid adds
      if(EnableGrid) ManageGrid();
   }

   if(ShowStats) DrawStatsPanel();

   // ── Candle-close detection ──
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
//          LOT SIZING                                              //
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
   if(EnableFixedLot)
      return(RoundLot(InitialLotSize));

   if(g_RealLastLot <= 0)
      g_RealLastLot = InitialLotSize;
   else
      g_RealLastLot *= GetCurrentMultiplier();
   return(RoundLot(g_RealLastLot));
}

//==================================================================//
//          PROGRESSIVE GRID STEP WITH CAP                         //
//==================================================================//
double CalcNextGridStep(double lastStep, int nextLevel)
{
   double baseStep = g_ATR * ATR_GridMultiplier;
   double capStep  = g_ATR * ProgressiveGridCapATR;

   double step;
   if(nextLevel < ProgressiveGridStart)
      step = baseStep;
   else
   {
      double prevStep = (lastStep > 0) ? lastStep : baseStep;
      step = prevStep + ProgressiveGridStep * g_ATR;
   }

   if(step > capStep) step = capStep;
   if(step < baseStep) step = baseStep;  // never shrink below base
   return(step);
}

//==================================================================//
//  BREAK-EVEN PRICE — weighted average across all open positions   //
//                                                                   //
//  For a basket of buys and sells with different lot sizes, the     //
//  true break-even is the price at which the total floating P&L     //
//  of every position equals zero simultaneously. We solve this      //
//  analytically:                                                     //
//                                                                   //
//    Net P&L(price) = 0                                             //
//    ΣBuys[lots_i*(price - open_i)] + ΣSells[lots_j*(open_j - price)] = 0  //
//    price*(totalBuyLots - totalSellLots) = ΣBuys(lots_i*open_i) - ΣSells(lots_j*open_j)  //
//                                                                   //
//  If totalBuyLots == totalSellLots the equation degenerates, so   //
//  we return 0 (no meaningful single break-even).                  //
//==================================================================//
double CalcBreakEvenPrice()
{
   double totalBuyLots  = 0, totalSellLots = 0;
   double weightedBuys  = 0, weightedSells = 0;
   double tickValue     = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE);
   double tickSize      = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
   if(tickSize <= 0) return(0);

   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;
      if(PositionGetString(POSITION_SYMBOL)      != _Symbol)     continue;
      if((int)PositionGetInteger(POSITION_MAGIC) != MagicNumber) continue;

      double lots      = PositionGetDouble(POSITION_VOLUME);
      double openPrice = PositionGetDouble(POSITION_PRICE_OPEN);
      double swap      = PositionGetDouble(POSITION_SWAP);

      // Adjust open price for swap so BE includes financing cost
      // swap is in account currency; convert to price units
      double swapInPrice = 0;
      if(lots > 0 && tickValue > 0)
         swapInPrice = swap / lots / (tickValue / tickSize);

      ENUM_POSITION_TYPE pt = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
      if(pt == POSITION_TYPE_BUY)
      {
         totalBuyLots += lots;
         weightedBuys += lots * (openPrice - swapInPrice);
      }
      else
      {
         totalSellLots += lots;
         weightedSells += lots * (openPrice + swapInPrice);
      }
   }

   double netLots = totalBuyLots - totalSellLots;
   if(MathAbs(netLots) < 0.0001) return(0);  // perfectly hedged — no single BE

   // BE = (weightedBuys - weightedSells) / netLots  →  rearranged from the formula above
   // Actually:  netPL = (totalBuyLots*P - weightedBuys) - (weightedSells - totalSellLots*P)
   //                  = P*(totalBuyLots + totalSellLots) - weightedBuys - weightedSells  ... NO
   // Correct derivation:
   //   netPL = Σbuy[L*(P - O)] + Σsell[L*(O - P)]
   //         = P*(ΣbuyL - ΣsellL) - (ΣbuyL*O - ΣsellL*O)   -- wrong sign on sell
   // Rewrite:
   //   netPL = P*(ΣbuyL) - ΣbuyL*O  +  ΣsellL*O - P*ΣsellL
   //         = P*(ΣbuyL - ΣsellL)   - (ΣbuyL*O - ΣsellL*O)
   //   set = 0: P = (ΣbuyL*O - ΣsellL*O) / (ΣbuyL - ΣsellL)
   //              = (weightedBuys - weightedSells_orig) / netLots

   // Re-calc without swap for the formula (swap already baked into weighted values above)
   double be = (weightedBuys - weightedSells) / netLots;
   return(NormalizeDouble(be, _Digits));
}

//==================================================================//
//   APPLY REAL STOP LOSS TO ALL SEQUENCE POSITIONS                 //
//                                                                   //
//   Calls PositionModify() on every open position belonging to     //
//   this EA to set a hard broker-side SL at stopPrice.             //
//   For buys  the SL must be below current price → use bid.        //
//   For sells the SL must be above current price → use ask.        //
//   Any position whose SL is already at (or better than) the new  //
//   level is skipped to avoid redundant modify calls.              //
//==================================================================//
void ApplyStopLossToAll(double stopPrice, bool isNetLong)
{
   double minDist = SymbolInfoInteger(_Symbol, SYMBOL_TRADE_STOPS_LEVEL) *
                    SymbolInfoDouble(_Symbol, SYMBOL_POINT);

   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;
      if(PositionGetString(POSITION_SYMBOL)      != _Symbol)     continue;
      if((int)PositionGetInteger(POSITION_MAGIC) != MagicNumber) continue;

      ENUM_POSITION_TYPE pt        = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
      double             currentSL = PositionGetDouble(POSITION_SL);
      double             currentTP = PositionGetDouble(POSITION_TP);   // preserve existing TP
      double             openPrice = PositionGetDouble(POSITION_PRICE_OPEN);

      double newSL = NormalizeDouble(stopPrice, _Digits);

      // Validate the SL is on the correct side of the current price + broker minimum distance
      if(pt == POSITION_TYPE_BUY)
      {
         double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
         // SL must be below bid by at least minDist
         if(newSL >= bid - minDist)
         {
            Print("Trinity Sniper: SL modify skipped (BUY) — SL too close to bid. ticket=", ticket,
                  " SL=", DoubleToString(newSL, _Digits),
                  " bid=", DoubleToString(bid,   _Digits));
            continue;
         }
         // Only move SL upward (never lower a buy SL)
         if(newSL <= currentSL && currentSL > 0) continue;
      }
      else // SELL
      {
         double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
         // SL must be above ask by at least minDist
         if(newSL <= ask + minDist)
         {
            Print("Trinity Sniper: SL modify skipped (SELL) — SL too close to ask. ticket=", ticket,
                  " SL=", DoubleToString(newSL, _Digits),
                  " ask=", DoubleToString(ask,   _Digits));
            continue;
         }
         // Only move SL downward (never raise a sell SL)
         if(newSL >= currentSL && currentSL > 0) continue;
      }

      if(!trade.PositionModify(ticket, newSL, currentTP))
         Print("Trinity Sniper: PositionModify FAILED ticket=", ticket,
               " newSL=", DoubleToString(newSL, _Digits),
               " err=",   GetLastError());
      else
         Print("Trinity Sniper: SL set ticket=", ticket,
               " type=",  (pt == POSITION_TYPE_BUY ? "BUY" : "SELL"),
               " SL=",    DoubleToString(newSL, _Digits));
   }
}

//==================================================================//
//   BREAK-EVEN & TRAILING STOP MANAGER — called every tick         //
//                                                                   //
//   Once armed, the trailing stop is written as a real broker SL   //
//   on every open position via PositionModify(). The broker will   //
//   close trades automatically if price hits the SL even when the  //
//   EA is disconnected. The EA also monitors the SL level itself   //
//   and calls CloseAllSequenceTrades() as a belt-and-braces        //
//   fallback should any position lack an SL (e.g. broker rejects). //
//==================================================================//
void ManageBreakEvenTrail()
{
   if(!g_InSequence) return;

   double bePrice = CalcBreakEvenPrice();
   if(bePrice <= 0) return;   // perfectly hedged basket — no meaningful single BE

   double point          = SymbolInfoDouble(_Symbol, SYMBOL_POINT);
   double activationDist = TrailActivationPips * point;
   double trailDist      = TrailDistancePips   * point;
   double ask            = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double bid            = SymbolInfoDouble(_Symbol, SYMBOL_BID);

   // ── Determine net direction (dominant side by lot volume) ─────
   double totalBuyLots = 0, totalSellLots = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;
      if(PositionGetString(POSITION_SYMBOL)      != _Symbol)     continue;
      if((int)PositionGetInteger(POSITION_MAGIC) != MagicNumber) continue;
      double lots = PositionGetDouble(POSITION_VOLUME);
      if((ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE) == POSITION_TYPE_BUY)
         totalBuyLots  += lots;
      else
         totalSellLots += lots;
   }
   bool netLong = (totalBuyLots >= totalSellLots);

   // ── Phase 1: waiting for activation ───────────────────────────
   if(!g_TrailArmed)
   {
      bool activated = netLong ? (bid >= bePrice + activationDist)
                               : (ask <= bePrice - activationDist);
      if(activated)
      {
         g_TrailArmed     = true;
         g_TrailBestPrice = netLong ? bid : ask;
         g_TrailStopPrice = netLong ? (g_TrailBestPrice - trailDist)
                                    : (g_TrailBestPrice + trailDist);
         g_TrailStopPrice = NormalizeDouble(g_TrailStopPrice, _Digits);

         Print("Trinity Sniper: Trail ARMED — BE=",    DoubleToString(bePrice,          _Digits),
               "  BestPrice=",                          DoubleToString(g_TrailBestPrice, _Digits),
               "  InitialSL=",                          DoubleToString(g_TrailStopPrice, _Digits));

         // Write the initial real SL to every open position immediately
         ApplyStopLossToAll(g_TrailStopPrice, netLong);
      }
      DrawBEAndTrailLines(bePrice, 0);
      return;
   }

   // ── Phase 2: trail is armed — update SL as price improves ─────
   double currentPrice = netLong ? bid : ask;
   bool   improved     = netLong ? (currentPrice > g_TrailBestPrice)
                                 : (currentPrice < g_TrailBestPrice);
   if(improved)
   {
      g_TrailBestPrice = currentPrice;
      double newStop   = netLong ? (g_TrailBestPrice - trailDist)
                                 : (g_TrailBestPrice + trailDist);
      newStop = NormalizeDouble(newStop, _Digits);

      bool slMoved = netLong  ? (newStop > g_TrailStopPrice)
                              : (newStop < g_TrailStopPrice);
      if(slMoved)
      {
         g_TrailStopPrice = newStop;
         // Push the updated SL to all open positions on the broker
         ApplyStopLossToAll(g_TrailStopPrice, netLong);
      }
   }

   DrawBEAndTrailLines(bePrice, g_TrailStopPrice);

   // ── Belt-and-braces: if broker SL was rejected, close manually ─
   // (virtual check so a broker-rejected modify doesn't leave us exposed)
   bool stopHit = netLong ? (bid <= g_TrailStopPrice)
                          : (ask >= g_TrailStopPrice);
   if(stopHit)
   {
      double netPL = GetNetFloatPL();
      Print("Trinity Sniper: Trail stop price hit (fallback close). NetPL=$",
            DoubleToString(netPL, 2),
            "  Stop=",  DoubleToString(g_TrailStopPrice, _Digits),
            "  Price=", DoubleToString(currentPrice,     _Digits));
      CloseAllSequenceTrades();
   }
}

//==================================================================//
//  DRAW BREAK-EVEN & TRAIL LINES ON CHART                          //
//==================================================================//
void DrawBEAndTrailLines(double bePrice, double trailStop)
{
   // Break-even horizontal line
   if(ObjectFind(0, OBJ_BE_LINE) < 0)
   {
      ObjectCreate(0, OBJ_BE_LINE, OBJ_HLINE, 0, 0, bePrice);
      ObjectSetInteger(0, OBJ_BE_LINE, OBJPROP_COLOR,      clrGold);
      ObjectSetInteger(0, OBJ_BE_LINE, OBJPROP_STYLE,      STYLE_DASH);
      ObjectSetInteger(0, OBJ_BE_LINE, OBJPROP_WIDTH,      1);
      ObjectSetInteger(0, OBJ_BE_LINE, OBJPROP_SELECTABLE, false);
      ObjectSetString(0,  OBJ_BE_LINE, OBJPROP_TOOLTIP,    "Break-Even Price");
   }
   else
      ObjectSetDouble(0, OBJ_BE_LINE, OBJPROP_PRICE, bePrice);

   // Trail stop horizontal line (only when armed)
   if(trailStop > 0)
   {
      if(ObjectFind(0, OBJ_TRAIL_LINE) < 0)
      {
         ObjectCreate(0, OBJ_TRAIL_LINE, OBJ_HLINE, 0, 0, trailStop);
         ObjectSetInteger(0, OBJ_TRAIL_LINE, OBJPROP_COLOR,      clrOrangeRed);
         ObjectSetInteger(0, OBJ_TRAIL_LINE, OBJPROP_STYLE,      STYLE_SOLID);
         ObjectSetInteger(0, OBJ_TRAIL_LINE, OBJPROP_WIDTH,      2);
         ObjectSetInteger(0, OBJ_TRAIL_LINE, OBJPROP_SELECTABLE, false);
         ObjectSetString(0,  OBJ_TRAIL_LINE, OBJPROP_TOOLTIP,    "Trailing Stop");
      }
      else
         ObjectSetDouble(0, OBJ_TRAIL_LINE, OBJPROP_PRICE, trailStop);
   }
}

//==================================================================//
//          LEGACY NET PROFIT TARGET (dollar-based)                 //
//==================================================================//
void CheckNetProfitTarget()
{
   int    count = 0;
   double netPL = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;
      if(PositionGetString(POSITION_SYMBOL)      != _Symbol)     continue;
      if((int)PositionGetInteger(POSITION_MAGIC) != MagicNumber) continue;
      netPL += PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
      count++;
   }
   if(count == 0) return;

   double targetUSD = BaseTPDollars * MathPow(TPTradeMultiplier, g_TradeCount - 1);
   g_FixedTargetUSD = targetUSD;
   if(netPL >= targetUSD)
   {
      Print("Trinity Sniper: Legacy $ TP HIT  P&L=$", DoubleToString(netPL, 2),
            "  Target=$", DoubleToString(targetUSD, 2), "  Trades=", count);
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
      if(PositionGetString(POSITION_SYMBOL)      != _Symbol)     continue;
      if((int)PositionGetInteger(POSITION_MAGIC) != MagicNumber) continue;
      if(!trade.PositionClose(ticket))
         Print("Trinity Sniper: Close FAILED ticket=", ticket, " err=", GetLastError());
   }
   ResetSequence();
   CalculateSRZones();
   DrawSRZones();
}

void ResetSequence()
{
   g_SequenceID++;
   g_InSequence         = false;
   g_TradeCount         = 0;
   g_ZoneLocked         = false;
   g_OriginalDir        = 0;
   g_AnchorBuy          = 0;
   g_AnchorSell         = 0;
   g_GridBuyLevel       = 0;
   g_GridSellLevel      = 0;
   g_RealLastLot        = 0;
   g_FixedTargetUSD     = 0;
   g_LastGridBuyPrice   = 0;
   g_LastGridSellPrice  = 0;
   g_LastGridBuyStep    = 0;
   g_LastGridSellStep   = 0;
   g_TrailArmed         = false;
   g_TrailBestPrice     = 0;
   g_TrailStopPrice     = 0;
   ObjectDelete(0, OBJ_BE_LINE);
   ObjectDelete(0, OBJ_TRAIL_LINE);
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
   double hw          = g_ATR * ATR_ZoneMultiplier;
   g_ResHigh = highestHigh + hw;
   g_ResLow  = highestHigh - hw;
   g_SupHigh = lowestLow   + hw;
   g_SupLow  = lowestLow   - hw;
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

   // ── Initial BUY off support ──
   if(!g_InSequence && !g_ZoneLocked && IsNewSequenceAllowed())
   {
      if(closePrice >= g_SupLow && closePrice <= g_SupHigh)
      {
         g_OriginalDir       = 1;
         g_InSequence        = true;
         g_ZoneLocked        = true;
         g_ZoneLockDir       = 1;
         g_GridBuyLevel      = 0;
         g_GridSellLevel     = 0;
         g_TradeCount        = 0;
         g_RealLastLot       = 0;
         g_LastGridBuyStep   = 0;
         g_LastGridSellStep  = 0;
         g_TrailArmed        = false;
         g_TrailBestPrice    = 0;
         g_TrailStopPrice    = 0;
         double lot          = NextLot();
         g_AnchorBuy         = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
         g_AnchorSell        = 0;
         g_LastGridBuyPrice  = 0;
         g_LastGridSellPrice = 0;
         OpenTrade(ORDER_TYPE_BUY, lot, "TS_Init_Buy");
         return;
      }
   }

   // ── Initial SELL off resistance ──
   if(!g_InSequence && !g_ZoneLocked && IsNewSequenceAllowed())
   {
      if(closePrice >= g_ResLow && closePrice <= g_ResHigh)
      {
         g_OriginalDir       = -1;
         g_InSequence        = true;
         g_ZoneLocked        = true;
         g_ZoneLockDir       = -1;
         g_GridBuyLevel      = 0;
         g_GridSellLevel     = 0;
         g_TradeCount        = 0;
         g_RealLastLot       = 0;
         g_LastGridBuyStep   = 0;
         g_LastGridSellStep  = 0;
         g_TrailArmed        = false;
         g_TrailBestPrice    = 0;
         g_TrailStopPrice    = 0;
         double lot          = NextLot();
         g_AnchorSell        = SymbolInfoDouble(_Symbol, SYMBOL_BID);
         g_AnchorBuy         = 0;
         g_LastGridBuyPrice  = 0;
         g_LastGridSellPrice = 0;
         OpenTrade(ORDER_TYPE_SELL, lot, "TS_Init_Sell");
         return;
      }
   }

   // ── Breakout hedge trigger ──
   if(g_InSequence && g_ZoneLocked)
   {
      if(g_ZoneLockDir == 1 && closePrice < g_SupLow)
      {
         g_ZoneLocked        = false;
         g_AnchorSell        = SymbolInfoDouble(_Symbol, SYMBOL_BID);
         g_LastGridSellPrice = 0;
         g_LastGridSellStep  = 0;
         g_GridSellLevel     = 0;
         double lot          = NextLot();
         OpenTrade(ORDER_TYPE_SELL, lot, "TS_Hedge_Sell");
      }
      else if(g_ZoneLockDir == -1 && closePrice > g_ResHigh)
      {
         g_ZoneLocked        = false;
         g_AnchorBuy         = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
         g_LastGridBuyPrice  = 0;
         g_LastGridBuyStep   = 0;
         g_GridBuyLevel      = 0;
         double lot          = NextLot();
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
      ok = trade.Buy(lots,  _Symbol, 0, 0, 0, fullComment);
   else
      ok = trade.Sell(lots, _Symbol, 0, 0, 0, fullComment);

   if(!ok)
   {
      Print("Trinity Sniper: Order FAILED [", comment, "] lots=", lots, " err=", GetLastError());
      return;
   }
   g_TradeCount++;
   Print("Trinity Sniper: Opened [", comment, "] lots=", DoubleToString(lots, 2),
         " ticket=", trade.ResultOrder(),
         " mult=",   DoubleToString(GetCurrentMultiplier(), 4));

   // If the trail is already armed, immediately apply the current SL
   // to the brand-new position so it is protected from the first tick.
   if(EnableTrailingStop && g_TrailArmed && g_TrailStopPrice > 0)
   {
      ulong newTicket = trade.ResultOrder();
      if(newTicket > 0)
      {
         // Brief wait for position to appear in the terminal (may take 1 tick on some brokers)
         // We use the helper which iterates all positions — the new one will be there.
         bool isLong = (type == ORDER_TYPE_BUY);
         double minDist = SymbolInfoInteger(_Symbol, SYMBOL_TRADE_STOPS_LEVEL) *
                          SymbolInfoDouble(_Symbol, SYMBOL_POINT);
         double newSL   = NormalizeDouble(g_TrailStopPrice, _Digits);
         double refPrice = isLong ? SymbolInfoDouble(_Symbol, SYMBOL_BID)
                                  : SymbolInfoDouble(_Symbol, SYMBOL_ASK);
         bool   slValid  = isLong ? (newSL < refPrice - minDist)
                                  : (newSL > refPrice + minDist);
         if(slValid)
         {
            if(!trade.PositionModify(newTicket, newSL, 0))
               Print("Trinity Sniper: New-trade SL FAILED ticket=", newTicket,
                     " SL=", DoubleToString(newSL, _Digits),
                     " err=", GetLastError());
            else
               Print("Trinity Sniper: New-trade SL applied ticket=", newTicket,
                     " SL=", DoubleToString(newSL, _Digits));
         }
      }
   }
}

//==================================================================//
//          GRID MANAGEMENT — progressive + capped spacing          //
//==================================================================//
void ManageGrid()
{
   if(!g_InSequence) return;
   if(g_ATR <= 0)    return;

   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);

   // ── Upward buy grid ──────────────────────────────────────────
   if(g_AnchorBuy > 0)
   {
      double nextBuyStep, nextBuyThresh;
      if(g_LastGridBuyPrice <= 0)
      {
         nextBuyStep   = CalcNextGridStep(0, 1);
         nextBuyThresh = g_AnchorBuy + nextBuyStep;
      }
      else
      {
         nextBuyStep   = CalcNextGridStep(g_LastGridBuyStep, g_GridBuyLevel + 1);
         nextBuyThresh = g_LastGridBuyPrice + nextBuyStep;
      }

      while(ask >= nextBuyThresh)
      {
         double lot = NextLot();
         string cmt = "Trinity Traders | TS_Grid_Buy_L" + IntegerToString(g_GridBuyLevel + 1)
                    + " #" + IntegerToString(g_SequenceID);
         if(trade.Buy(lot, _Symbol, 0, 0, 0, cmt))
         {
            g_GridBuyLevel++;
            g_LastGridBuyPrice = nextBuyThresh;
            g_LastGridBuyStep  = nextBuyStep;
            g_TradeCount++;
            Print("Trinity Sniper: Grid BUY L", g_GridBuyLevel,
                  " @ ", DoubleToString(ask, _Digits),
                  " thresh=", DoubleToString(nextBuyThresh, _Digits),
                  " step=", DoubleToString(nextBuyStep, _Digits),
                  " lots=", DoubleToString(lot, 2));
            nextBuyStep   = CalcNextGridStep(g_LastGridBuyStep, g_GridBuyLevel + 1);
            nextBuyThresh = g_LastGridBuyPrice + nextBuyStep;
         }
         else { Print("Trinity Sniper: Grid BUY failed err=", GetLastError()); break; }
      }
   }

   // ── Downward sell grid ───────────────────────────────────────
   if(g_AnchorSell > 0)
   {
      double nextSellStep, nextSellThresh;
      if(g_LastGridSellPrice <= 0)
      {
         nextSellStep   = CalcNextGridStep(0, 1);
         nextSellThresh = g_AnchorSell - nextSellStep;
      }
      else
      {
         nextSellStep   = CalcNextGridStep(g_LastGridSellStep, g_GridSellLevel + 1);
         nextSellThresh = g_LastGridSellPrice - nextSellStep;
      }

      while(bid <= nextSellThresh)
      {
         double lot = NextLot();
         string cmt = "Trinity Traders | TS_Grid_Sell_L" + IntegerToString(g_GridSellLevel + 1)
                    + " #" + IntegerToString(g_SequenceID);
         if(trade.Sell(lot, _Symbol, 0, 0, 0, cmt))
         {
            g_GridSellLevel++;
            g_LastGridSellPrice = nextSellThresh;
            g_LastGridSellStep  = nextSellStep;
            g_TradeCount++;
            Print("Trinity Sniper: Grid SELL L", g_GridSellLevel,
                  " @ ", DoubleToString(bid, _Digits),
                  " thresh=", DoubleToString(nextSellThresh, _Digits),
                  " step=", DoubleToString(nextSellStep, _Digits),
                  " lots=", DoubleToString(lot, 2));
            nextSellStep   = CalcNextGridStep(g_LastGridSellStep, g_GridSellLevel + 1);
            nextSellThresh = g_LastGridSellPrice - nextSellStep;
         }
         else { Print("Trinity Sniper: Grid SELL failed err=", GetLastError()); break; }
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
      pl += PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
   }
   return(pl);
}

int GetHistoryCount()
{
   HistorySelect(0, TimeCurrent());
   int c = 0, total = HistoryDealsTotal();
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
   for(int i = 0; i < 50; i++)
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
   double curMult     = EnableFixedLot ? 1.0 : GetCurrentMultiplier();
   double point       = SymbolInfoDouble(_Symbol, SYMBOL_POINT);

   double atrPips  = (g_ATR > 0 && point > 0) ? g_ATR / point : 0;
   double gridPips = atrPips * ATR_GridMultiplier;
   double capPips  = atrPips * ProgressiveGridCapATR;
   double zonePips = atrPips * ATR_ZoneMultiplier;

   int    nextBuyLvl   = g_GridBuyLevel + 1;
   double nextBuyStep  = CalcNextGridStep(g_LastGridBuyStep,  nextBuyLvl);
   double nextBuyPips  = (point > 0) ? nextBuyStep  / point : 0;
   int    nextSellLvl  = g_GridSellLevel + 1;
   double nextSellStep = CalcNextGridStep(g_LastGridSellStep, nextSellLvl);
   double nextSellPips = (point > 0) ? nextSellStep / point : 0;

   double bePrice      = CalcBreakEvenPrice();
   double bePips       = (bePrice > 0 && point > 0)
                         ? MathAbs(SymbolInfoDouble(_Symbol, SYMBOL_BID) - bePrice) / point
                         : 0;

   double nextLotDisp  = EnableFixedLot ? InitialLotSize
                       : RoundLot(g_RealLastLot > 0 ? g_RealLastLot * GetCurrentMultiplier() : InitialLotSize);

   double dayRealisedPL = (g_DayStartBalance > 0) ? balance - g_DayStartBalance : 0;
   double dayTotalPL    = dayRealisedPL + floatPL;
   double dayTargetUSD  = (g_DayStartBalance > 0) ? g_DayStartBalance * DailyProfitTargetPct / 100.0 : 0;
   double dayPct        = (g_DayStartBalance > 0) ? (dayTotalPL / g_DayStartBalance) * 100.0 : 0;

   // Session info
   MqlDateTime utcNow; TimeToStruct(TimeGMT(), utcNow);
   string sessionStr = (EnableTradingHours)
                     ? (IntegerToString(TradingHourStart) + ":00–" + IntegerToString(TradingHourEnd) + ":00 UTC")
                     : "Unrestricted";
   bool   inSession  = IsNewSequenceAllowed();

   color cH  = C'20,20,20';
   color cD  = clrBlack;
   color cDv = C'90,90,90';
   color cG  = C'0,130,0';
   color cR  = clrDarkRed;
   color cY  = C'160,100,0';
   color cB  = C'0,70,160';

   DeletePanelRows();
   int r = 0;
   DrawRow(r++, "━━ TRINITY SNIPER v1.82 (MT5) ━━",                       cH);
   DrawRow(r++, "Sequence ID   : " + IntegerToString(g_SequenceID),        cD);
   DrawRow(r++, "Active Trades : " + IntegerToString(openTrades),           cD);
   DrawRow(r++, "Trade Count   : " + IntegerToString(g_TradeCount),         cD);
   DrawRow(r++, "Total Lots    : " + DoubleToString(totalLots, 2),          cD);
   DrawRow(r++, "Lot Mode      : " + (EnableFixedLot ? "FIXED 0.01" : "Martingale x" + DoubleToString(curMult, 3)), cD);
   DrawRow(r++, "Next Lot      : " + DoubleToString(nextLotDisp, 2),        cD);
   DrawRow(r++, "─────────────────────────────",                            cDv);
   DrawRow(r++, "Float P/L ($) : " + DoubleToString(floatPL, 2),           (floatPL >= 0 ? cG : cR));

   // Break-even & trail
   if(EnableTrailingStop)
   {
      string beStr   = (bePrice > 0) ? DoubleToString(bePrice, _Digits) + " (" + DoubleToString(bePips, 0) + "p away)" : "N/A (hedged)";
      string trlStr  = g_TrailArmed  ? "ARMED @ " + DoubleToString(g_TrailStopPrice, _Digits) : "Waiting for BE+" + DoubleToString(TrailActivationPips, 0) + "p";
      DrawRow(r++, "Break-Even    : " + beStr,                              cY);
      DrawRow(r++, "Trail Stop    : " + trlStr,                             (g_TrailArmed ? cG : cDv));
   }
   else if(EnableLegacyDollarTP)
   {
      int    tc      = (g_TradeCount > 0) ? g_TradeCount : 1;
      double tpUSD   = BaseTPDollars * MathPow(TPTradeMultiplier, tc - 1);
      DrawRow(r++, "$ TP Target   : " + DoubleToString(tpUSD,          2), cY);
      DrawRow(r++, "$ TP Remain   : " + DoubleToString(tpUSD - floatPL,2),(tpUSD - floatPL <= 0 ? cG : cR));
   }

   DrawRow(r++, "─────────────────────────────",                            cDv);
   DrawRow(r++, "━━ DAILY LIMIT ━━",                                        cH);
   DrawRow(r++, "Day P/L ($)   : " + DoubleToString(dayTotalPL, 2),        (dayTotalPL >= 0 ? cG : cR));
   DrawRow(r++, "Day P/L (%)   : " + DoubleToString(dayPct,     2) + "%",  (dayPct     >= 0 ? cG : cR));
   DrawRow(r++, "Day Target($) : " + DoubleToString(dayTargetUSD,2) + "  (" + DoubleToString(DailyProfitTargetPct,1) + "%)", cY);
   DrawRow(r++, "Limit Reached : " + (g_DailyLimitReached ? "YES — NO NEW SEQ" : "No"),
               (g_DailyLimitReached ? cR : cG));
   DrawRow(r++, "─────────────────────────────",                            cDv);
   DrawRow(r++, "━━ SESSION ━━",                                            cH);
   DrawRow(r++, "Session Hours : " + sessionStr,                            cD);
   DrawRow(r++, "UTC Hour Now  : " + IntegerToString(utcNow.hour) + ":xx", cD);
   DrawRow(r++, "New Seq OK?   : " + (inSession ? "YES" : "NO — Outside hours/limit"),
               (inSession ? cG : cR));
   DrawRow(r++, "─────────────────────────────",                            cDv);
   DrawRow(r++, "━━ ATR & ZONES ━━",                                        cH);
   DrawRow(r++, "ATR (pips)    : " + DoubleToString(atrPips,  1),           cD);
   DrawRow(r++, "Zone Width(p) : " + DoubleToString(zonePips, 1),           cD);
   DrawRow(r++, "Grid Base(p)  : " + DoubleToString(gridPips, 1),           cD);
   DrawRow(r++, "Grid Cap (p)  : " + DoubleToString(capPips,  1),           cD);
   DrawRow(r++, "Prog.Start Lv : L" + IntegerToString(ProgressiveGridStart),cD);
   DrawRow(r++, "Buy  NextStp  : " + DoubleToString(nextBuyPips,  1) + "p (L" + IntegerToString(nextBuyLvl)  + ")", cD);
   DrawRow(r++, "Sell NextStp  : " + DoubleToString(nextSellPips, 1) + "p (L" + IntegerToString(nextSellLvl) + ")", cD);
   DrawRow(r++, "Res Zone Hi   : " + DoubleToString(g_ResHigh, _Digits),    cD);
   DrawRow(r++, "Res Zone Lo   : " + DoubleToString(g_ResLow,  _Digits),    cD);
   DrawRow(r++, "Sup Zone Hi   : " + DoubleToString(g_SupHigh, _Digits),    cD);
   DrawRow(r++, "Sup Zone Lo   : " + DoubleToString(g_SupLow,  _Digits),    cD);
   DrawRow(r++, "Grid Buy Lvl  : " + IntegerToString(g_GridBuyLevel),       cD);
   DrawRow(r++, "Grid Sell Lvl : " + IntegerToString(g_GridSellLevel),      cD);
   DrawRow(r++, "─────────────────────────────",                            cDv);
   DrawRow(r++, "━━ HISTORY ━━",                                            cH);
   DrawRow(r++, "Hist Trades   : " + IntegerToString(histTrades),            cD);
   DrawRow(r++, "Hist P/L ($)  : " + DoubleToString(histPL,  2),            (histPL  >= 0 ? cG : cR));
   DrawRow(r++, "Balance ($)   : " + DoubleToString(balance, 2),             cD);
   DrawRow(r++, "Equity ($)    : " + DoubleToString(equity,  2),             cD);
   DrawRow(r++, "─────────────────────────────",                            cDv);
}

//+------------------------------------------------------------------+
//                          END OF FILE                              //
//+------------------------------------------------------------------+
