//+------------------------------------------------------------------+
//|                          CRT_EA.mq5                              |
//|              Candle Range Theory Expert Advisor                   |
//|         Based on Legacy Trading - CRT Flash Cards Guide           |
//|                                                                   |
//|  STRATEGY OVERVIEW:                                               |
//|  - Power of Three (PO3): Accumulation → Manipulation → Distribution|
//|  - Turtle Soup (TS): Wick beyond range then close back inside     |
//|  - CSD: Change in State of Delivery (close above/below last OB)   |
//|  - HTF Key Levels: Confluences for high-probability entries        |
//|  - Timed entries: specific session windows for best setups        |
//+------------------------------------------------------------------+
#property copyright "CRT EA - Legacy Trading Strategy"
#property version   "2.02"
#property strict

#include <Trade\Trade.mqh>
#include <Trade\PositionInfo.mqh>
#include <Trade\OrderInfo.mqh>

CTrade        trade;
CPositionInfo posInfo;

//=== INPUT PARAMETERS ===================================================

input group "=== TIMEFRAMES ==="
input ENUM_TIMEFRAMES HTF_Period      = PERIOD_H4;   // HTF CRT Timeframe
input ENUM_TIMEFRAMES LTF_Period      = PERIOD_H1;   // LTF Entry Timeframe
input ENUM_TIMEFRAMES EntryPeriod     = PERIOD_M15;  // Entry Execution TF

input group "=== LOT SIZING OPTIONS ==="
input bool   UseFixedLots      = false; // true=Fixed Lot Size, false=Auto Risk %
input double FixedLotSize      = 0.01;  // Fixed Lot Size (if UseFixedLots is true)

input group "=== RISK MANAGEMENT ==="
input double RiskPercent        = 2.0;    // Risk % per trade (if UseFixedLots is false)
input double MaxDailyRiskPct    = 6.0;    // Max daily drawdown %
input int    MaxOpenTrades      = 3;      // Max simultaneous trades
input double TP1_RR             = 2.0;    // TP1 Risk:Reward (50% close)
input double TP2_RR             = 4.0;    // TP2 Risk:Reward (full target)
input double AggressiveMulti    = 1.5;    // Aggressive lot multiplier on confluence

input group "=== CRT DETECTION ==="
input int    CRT_Lookback       = 5;      // Candles to look back for CRT
input double TS_PipThreshold    = 3.0;    // Min pip wick beyond range for Turtle Soup
input double RangeMinPips       = 15.0;   // Minimum CRT range size (pips)
input double RangeMaxPips       = 300.0;  // Maximum CRT range size (pips)
input int    InsideBarCount     = 3;      // Min inside bars for CRT+IB confirmation

input group "=== TIMED SESSIONS (EST) ==="
input bool   UseTimedSessions   = true;   // Filter by session time
input bool   Session_1am        = true;   // 1am EST session
input bool   Session_3am        = true;   // 3am EST (Turtle Soup)
input bool   Session_5am        = true;   // 5am EST session
input bool   Session_6am        = true;   // 6am EST (Turtle Soup)
input bool   Session_9am        = true;   // 9am EST session / Turtle Soup
input bool   Session_1pm        = true;   // 1pm EST session
input bool   Session_5pm        = true;   // 5pm EST session
input bool   Session_9pm        = true;   // 9pm EST session
input int    SessionWindowMins  = 30;     // Window around each session (minutes)

input group "=== WEEKLY TIMING ==="
input bool   TradeMonday        = true;   // Trade on Monday (Week 1 CRT)
input bool   TradeFriday        = true;   // Trade on Friday
input bool   TradeWeek1Only     = false;  // Restrict to 1st week of month

input group "=== KEY LEVEL FILTERS ==="
input bool   RequireHTFLevel    = true;   // Require HTF key level confluence
input double OBLookback         = 10;     // Order Block lookback (candles)
input double KeyLevelBuffer     = 5.0;    // Key level buffer (pips)

input group "=== TRADE MANAGEMENT ==="
input bool   UseBreakEven       = true;   // Move SL to BE at TP1
input bool   UseTrailingStop    = true;   // Use trailing stop
input double TrailStartR        = 1.5;    // Start trailing at X:1 R
input double TrailDistPips      = 10.0;   // Trailing stop distance (pips)
input int    MagicNumber        = 202400; // EA Magic Number
input string TradeComment       = "CRT_EA";

//=== GLOBAL VARIABLES ====================================================

double PipValue;
double PointValue;
int    PipDigits;

struct CRT_Range {
   double high;
   double low;
   double range;
   int    startBar;
   bool   valid;
   bool   isTurtleSouped;
   int    soupDirection; // 1=bullish soup, -1=bearish soup
};

struct OB_Level {
   double price;
   bool   isBullish;
   bool   valid;
};

double DailyStartBalance;
int    DailyTradeCount;
datetime LastTradeDay;

//=======================================================================

int OnInit()
{
   trade.SetExpertMagicNumber(MagicNumber);
   trade.SetDeviationInPoints(20);
   
   // Set Filling Mode dynamically based on Broker
   SetCorrectFillingType();

   // Detect pip value based on symbol digits
   int digits = (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);
   if(digits == 3 || digits == 5) {
      PipDigits  = 5;
      PipValue   = 10 * _Point;
   } else {
      PipDigits  = 4;
      PipValue   = _Point;
   }
   PointValue = _Point;

   DailyStartBalance = AccountInfoDouble(ACCOUNT_BALANCE);
   LastTradeDay      = 0;
   DailyTradeCount   = 0;

   Print("CRT EA Initialized | HTF:", EnumToString(HTF_Period),
         " LTF:", EnumToString(LTF_Period),
         " Entry:", EnumToString(EntryPeriod));
   return INIT_SUCCEEDED;
}

//=======================================================================
void OnTick()
{
   // Reset daily stats
   MqlDateTime now;
   TimeToStruct(TimeCurrent(), now);
   datetime today = StringToTime(StringFormat("%04d.%02d.%02d", now.year, now.mon, now.day));

   if(today != LastTradeDay) {
      DailyStartBalance = AccountInfoDouble(ACCOUNT_BALANCE);
      DailyTradeCount   = 0;
      LastTradeDay      = today;
   }

   // Daily risk guard
   if(IsDailyRiskBreached()) return;

   // Max open trades guard
   if(CountOpenTrades() >= MaxOpenTrades) {
      ManageOpenTrades();
      return;
   }

   // Weekly filter
   if(!IsValidTradingDay()) return;

   // Session filter
   if(UseTimedSessions && !IsValidSession()) return;

   // Detect new HTF bar (only analyze on new bar)
   static datetime lastHTFBar = 0;
   datetime currentHTFBar = iTime(_Symbol, HTF_Period, 0);
   if(currentHTFBar == lastHTFBar) {
      ManageOpenTrades();
      return;
   }
   lastHTFBar = currentHTFBar;

   // === MAIN CRT LOGIC ===
   CRT_Range htfCRT = DetectCRT(HTF_Period);
   if(!htfCRT.valid) {
      ManageOpenTrades();
      return;
   }

   // Check for Turtle Soup (manipulation candle)
   if(!htfCRT.isTurtleSouped) {
      ManageOpenTrades();
      return;
   }

   // Get HTF key level confluence
   OB_Level htfOB = FindOrderBlock(HTF_Period, htfCRT.soupDirection);

   // LTF CRT within HTF CRT
   CRT_Range ltfCRT = DetectCRT(LTF_Period);

   // Determine trade direction from soup direction
   int direction = htfCRT.soupDirection; // 1=long, -1=short

   // Confirm CSD (Change in State of Delivery)
   bool csdConfirmed = CheckCSD(LTF_Period, direction);

   // Build confluence score
   int confluenceScore = 0;
   if(htfCRT.valid)        confluenceScore++;
   if(htfOB.valid)         confluenceScore++;
   if(ltfCRT.valid)        confluenceScore++;
   if(csdConfirmed)        confluenceScore++;
   if(IsInsideBarCRT())    confluenceScore++;

   // Need at least 2 confluences for a trade
   if(confluenceScore < 2) {
      ManageOpenTrades();
      return;
   }

   // Calculate entry based on PO3 model
   // 3rd candle entry: below/above open of 3rd candle, at 50% of 2nd candle range
   double entryPrice  = 0;
   double stopLoss    = 0;
   double takeProfit1 = 0;
   double takeProfit2 = 0;

   if(!CalcPO3Entry(direction, htfCRT, ltfCRT,
                    entryPrice, stopLoss, takeProfit1, takeProfit2)) {
      ManageOpenTrades();
      return;
   }

   // === LOT SIZE CALCULATION LOGIC ===
   double baseLots = 0;
   
   if(UseFixedLots) {
      // User wants manual fixed lot size
      baseLots = FixedLotSize;
   } else {
      // User wants automatic risk-based calculation
      baseLots = CalcLotSize(MathAbs(entryPrice - stopLoss));
   }

   // Apply Aggressive Multiplier based on score
   double lots = (confluenceScore >= 4) ? baseLots * AggressiveMulti : baseLots;
   lots = NormalizeLots(lots);
   
   // Validate lot size is not zero
   if(lots <= 0) return;

   // Execute trade
   bool traded = false;
   if(direction == 1) {
      traded = trade.Buy(lots, _Symbol, 0, stopLoss, takeProfit1,
                         TradeComment + "_L_TP1");
   } else {
      traded = trade.Sell(lots, _Symbol, 0, stopLoss, takeProfit1,
                          TradeComment + "_S_TP1");
   }

   if(traded) {
      DailyTradeCount++;
      Print("CRT Trade Opened | Dir:", direction == 1 ? "LONG" : "SHORT",
            " | Lots:", lots, " | Score:", confluenceScore,
            " | Entry:", entryPrice, " | SL:", stopLoss,
            " | TP1:", takeProfit1, " | TP2:", takeProfit2);

      // Place second half order for TP2
      if(direction == 1) {
         trade.Buy(lots * 0.5, _Symbol, 0, stopLoss, takeProfit2,
                   TradeComment + "_L_TP2");
      } else {
         trade.Sell(lots * 0.5, _Symbol, 0, stopLoss, takeProfit2,
                    TradeComment + "_S_TP2");
      }
   }

   ManageOpenTrades();
}

//=======================================================================
//  DETECT CRT RANGE (Power of Three pattern)
//=======================================================================
CRT_Range DetectCRT(ENUM_TIMEFRAMES tf)
{
   CRT_Range result;
   result.valid         = false;
   result.isTurtleSouped = false;
   result.soupDirection = 0;

   // Need at least CRT_Lookback + 3 bars
   int totalBars = iBars(_Symbol, tf);
   if(totalBars < CRT_Lookback + 5) return result;

   // Find a 3-candle PO3 structure:
   // Candle 1: Range forming (accumulation)
   // Candle 2: Turtle Soup / Manipulation (wick beyond candle 1 range)
   // Candle 3: Distribution (current or recent)

   for(int i = CRT_Lookback; i >= 2; i--) {
      double c1High  = iHigh(_Symbol, tf, i + 2);
      double c1Low   = iLow(_Symbol, tf, i + 2);
      double c1Range = c1High - c1Low;

      // Range size filter
      double rangePips = c1Range / PipValue;
      if(rangePips < RangeMinPips || rangePips > RangeMaxPips) continue;

      double c2High  = iHigh(_Symbol, tf, i + 1);
      double c2Low   = iLow(_Symbol, tf, i + 1);
      double c2Open  = iOpen(_Symbol, tf, i + 1);
      double c2Close = iClose(_Symbol, tf, i + 1);

      double c3High  = iHigh(_Symbol, tf, i);
      double c3Low   = iLow(_Symbol, tf, i);
      double c3Close = iClose(_Symbol, tf, i);

      // Check for BEARISH Turtle Soup on candle 2:
      // Wick above c1High AND close below c1High (inside or below)
      bool bearishTS = (c2High > c1High + TS_PipThreshold * PipValue) &&
                       (c2Close < c1High) &&
                       (c3Close < c2Open); // c3 confirms down move

      // Check for BULLISH Turtle Soup on candle 2:
      // Wick below c1Low AND close above c1Low
      bool bullishTS = (c2Low < c1Low - TS_PipThreshold * PipValue) &&
                       (c2Close > c1Low) &&
                       (c3Close > c2Open); // c3 confirms up move

      if(bearishTS) {
         result.high          = c1High;
         result.low           = c1Low;
         result.range         = c1Range;
         result.startBar      = i + 2;
         result.valid         = true;
         result.isTurtleSouped = true;
         result.soupDirection = -1; // Bearish: sell after soup above
         return result;
      }

      if(bullishTS) {
         result.high          = c1High;
         result.low           = c1Low;
         result.range         = c1Range;
         result.startBar      = i + 2;
         result.valid         = true;
         result.isTurtleSouped = true;
         result.soupDirection = 1;  // Bullish: buy after soup below
         return result;
      }
   }

   return result;
}

//=======================================================================
//  FIND ORDER BLOCK (HTF Key Level)
//=======================================================================
OB_Level FindOrderBlock(ENUM_TIMEFRAMES tf, int direction)
{
   OB_Level ob;
   ob.valid     = false;
   ob.isBullish = (direction == 1);

   for(int i = 1; i <= (int)OBLookback; i++) {
      double open  = iOpen(_Symbol, tf, i);
      double close = iClose(_Symbol, tf, i);
      double high  = iHigh(_Symbol, tf, i);
      double low   = iLow(_Symbol, tf, i);

      // Bullish OB: last bearish candle before a strong up move
      if(direction == 1 && close < open) {
         double nextClose = iClose(_Symbol, tf, i - 1);
         if(nextClose > high) { // Engulfed / strong move up
            ob.price    = open; // OB zone top
            ob.isBullish = true;
            ob.valid    = true;
            return ob;
         }
      }

      // Bearish OB: last bullish candle before a strong down move
      if(direction == -1 && close > open) {
         double nextClose = iClose(_Symbol, tf, i - 1);
         if(nextClose < low) { // Engulfed / strong move down
            ob.price    = open; // OB zone bottom
            ob.isBullish = false;
            ob.valid    = true;
            return ob;
         }
      }
   }
   return ob;
}

//=======================================================================
//  CHECK CSD - Change in State of Delivery
//  CSD = Close above/below last OB before reversal
//=======================================================================
bool CheckCSD(ENUM_TIMEFRAMES tf, int direction)
{
   // Find the last OB on the LTF in the opposite direction (pre-reversal)
   // Then check if price has closed above/below it (CSD confirmed)
   for(int i = 1; i <= 10; i++) {
      double open  = iOpen(_Symbol, tf, i);
      double close = iClose(_Symbol, tf, i);
      double high  = iHigh(_Symbol, tf, i);
      double low   = iLow(_Symbol, tf, i);

      if(direction == 1) {
         // Looking for bullish CSD: close above the last bearish OB
         if(close < open) { // bearish candle (the OB)
            double currentClose = iClose(_Symbol, tf, 0);
            if(currentClose > open) return true; // CSD confirmed bullish
         }
      } else {
         // Bearish CSD: close below the last bullish OB
         if(close > open) { // bullish candle (the OB)
            double currentClose = iClose(_Symbol, tf, 0);
            if(currentClose < open) return true; // CSD confirmed bearish
         }
      }
   }
   return false;
}

//=======================================================================
//  CHECK INSIDE BAR CRT (CRT + Inside Bar pattern)
//=======================================================================
bool IsInsideBarCRT()
{
   // Check if we have multiple inside bars on entry TF (higher probability)
   double motherHigh = iHigh(_Symbol, EntryPeriod, InsideBarCount + 1);
   double motherLow  = iLow(_Symbol, EntryPeriod, InsideBarCount + 1);

   int insideCount = 0;
   for(int i = 1; i <= InsideBarCount; i++) {
      double h = iHigh(_Symbol, EntryPeriod, i);
      double l = iLow(_Symbol, EntryPeriod, i);
      if(h <= motherHigh && l >= motherLow) insideCount++;
   }

   return (insideCount >= InsideBarCount - 1);
}

//=======================================================================
//  CALCULATE PO3 ENTRY (3rd Candle Entry Model)
//  Entry: above/below 3rd candle open, at 50% of 2nd candle range
//=======================================================================
bool CalcPO3Entry(int direction, CRT_Range &htfCRT, CRT_Range &ltfCRT,
                  double &entry, double &sl, double &tp1, double &tp2)
{
   // Use the HTF CRT range for reference
   // 2nd candle = manipulation (Turtle Soup candle)
   double c2High  = iHigh(_Symbol, HTF_Period, 1);
   double c2Low   = iLow(_Symbol, HTF_Period, 1);
   double c2Range = c2High - c2Low;
   double c2Mid   = c2Low + c2Range * 0.5; // 50% level

   double c3Open  = iOpen(_Symbol, HTF_Period, 0);
   double c3High  = iHigh(_Symbol, HTF_Period, 0);
   double c3Low   = iLow(_Symbol, HTF_Period, 0);

   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);

   double slBuffer = 5.0 * PipValue; // 5 pip buffer beyond SL

   if(direction == 1) { // BULLISH: Buy below 3rd candle open / at 50% of 2nd
      entry = ask; // Market entry
      sl    = MathMin(htfCRT.low, c2Low) - slBuffer;
      tp1   = entry + (entry - sl) * TP1_RR;
      tp2   = entry + (entry - sl) * TP2_RR;

      // Validate: SL must be below current price
      if(sl >= entry - 5 * PipValue) return false;

   } else { // BEARISH: Sell above 3rd candle open / at 50% of 2nd
      entry = bid;
      sl    = MathMax(htfCRT.high, c2High) + slBuffer;
      tp1   = entry - (sl - entry) * TP1_RR;
      tp2   = entry - (sl - entry) * TP2_RR;

      // Validate: SL must be above current price
      if(sl <= entry + 5 * PipValue) return false;
   }

   // Minimum SL of 10 pips
   if(MathAbs(entry - sl) < 10 * PipValue) return false;

   // TP must be positive and valid
   if(direction == 1  && (tp1 <= entry || tp2 <= tp1)) return false;
   if(direction == -1 && (tp1 >= entry || tp2 >= tp1)) return false;

   return true;
}

//=======================================================================
//  LOT SIZE CALCULATION (fixed fractional risk)
//=======================================================================
double CalcLotSize(double slDistance)
{
   double balance    = AccountInfoDouble(ACCOUNT_BALANCE);
   double riskAmount = balance * RiskPercent / 100.0;

   double tickValue  = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE);
   double tickSize   = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
   double lotMin     = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double lotMax     = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
   double lotStep    = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);

   if(tickSize == 0 || tickValue == 0) return lotMin;

   double pipValuePerLot = (PipValue / tickSize) * tickValue;
   if(pipValuePerLot == 0) return lotMin;

   double slPips = slDistance / PipValue;
   double lots   = riskAmount / (slPips * pipValuePerLot);

   return NormalizeLots(lots);
}

double NormalizeLots(double lots)
{
   double lotMin  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double lotMax  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
   double lotStep = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);

   lots = MathFloor(lots / lotStep) * lotStep;
   lots = MathMax(lotMin, MathMin(lotMax, lots));
   
   // Determine lot digits (usually 2)
   int digits = (int)MathCeil(-MathLog10(lotStep));
   return NormalizeDouble(lots, digits);
}

//=======================================================================
//  TRADE MANAGEMENT: Break-Even + Trailing Stop
//=======================================================================
void ManageOpenTrades()
{
   for(int i = PositionsTotal() - 1; i >= 0; i--) {
      if(!posInfo.SelectByIndex(i)) continue;
      if(posInfo.Magic() != MagicNumber) continue;
      if(posInfo.Symbol() != _Symbol) continue;

      double openPrice = posInfo.PriceOpen();
      double sl        = posInfo.StopLoss();
      double tp        = posInfo.TakeProfit();
      double currentSL = sl;
      double bid       = SymbolInfoDouble(_Symbol, SYMBOL_BID);
      double ask       = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
      double slDist    = MathAbs(openPrice - sl);

      if(slDist == 0) continue;

      // --- Break-Even ---
      if(UseBreakEven) {
         if(posInfo.PositionType() == POSITION_TYPE_BUY) {
            double beLevel = openPrice + slDist * TrailStartR;
            if(bid >= beLevel && sl < openPrice + 2 * PipValue) {
               trade.PositionModify(posInfo.Ticket(),
                                    openPrice + 2 * PipValue, tp);
               continue;
            }
         } else {
            double beLevel = openPrice - slDist * TrailStartR;
            if(ask <= beLevel && sl > openPrice - 2 * PipValue) {
               trade.PositionModify(posInfo.Ticket(),
                                    openPrice - 2 * PipValue, tp);
               continue;
            }
         }
      }

      // --- Trailing Stop ---
      if(UseTrailingStop) {
         double trailDist = TrailDistPips * PipValue;

         if(posInfo.PositionType() == POSITION_TYPE_BUY) {
            double newSL = bid - trailDist;
            if(newSL > sl + PipValue && newSL > openPrice) {
               trade.PositionModify(posInfo.Ticket(), newSL, tp);
            }
         } else {
            double newSL = ask + trailDist;
            if(newSL < sl - PipValue && newSL < openPrice) {
               trade.PositionModify(posInfo.Ticket(), newSL, tp);
            }
         }
      }
   }
}

//=======================================================================
//  SESSION FILTER (EST Times converted to broker time)
//=======================================================================
bool IsValidSession()
{
   datetime brokerTime = TimeCurrent();
   MqlDateTime dt;
   TimeToStruct(brokerTime, dt);

   int hour = dt.hour;
   int min  = dt.min;

   // Convert broker time to EST (approximate - adjust offset if needed)
   // Assuming broker is UTC+2 (EET), EST = UTC-5, so offset = -7
   // Users should adjust BrokerToEST_Offset if needed
   int estHour = (hour - 7 + 24) % 24;
   int windowH = SessionWindowMins / 60;
   int windowM = SessionWindowMins % 60;

   // Session times in EST
   int sessions[][2] = {
      {1,  0},  // 1am
      {3,  0},  // 3am  (Turtle Soup)
      {5,  0},  // 5am
      {6,  0},  // 6am  (Turtle Soup)
      {9,  0},  // 9am  (Turtle Soup)
      {13, 0},  // 1pm
      {17, 0},  // 5pm
      {21, 0}   // 9pm
   };

   bool sessionActive[] = {
      Session_1am,
      Session_3am,
      Session_5am,
      Session_6am,
      Session_9am,
      Session_1pm,
      Session_5pm,
      Session_9pm
   };

   for(int s = 0; s < 8; s++) {
      if(!sessionActive[s]) continue;
      int sHour = sessions[s][0];
      int totalMinNow  = estHour * 60 + min;
      int totalMinSess = sHour * 60;

      if(MathAbs(totalMinNow - totalMinSess) <= SessionWindowMins) return true;
   }

   return false;
}

//=======================================================================
//  DAY OF WEEK FILTER
//=======================================================================
bool IsValidTradingDay()
{
   MqlDateTime dt;
   TimeToStruct(TimeCurrent(), dt);

   // Skip weekends
   if(dt.day_of_week == 0 || dt.day_of_week == 6) return false;

   // Monday filter
   if(dt.day_of_week == 1 && !TradeMonday) return false;

   // Friday filter
   if(dt.day_of_week == 5 && !TradeFriday) return false;

   // First week of month filter
   if(TradeWeek1Only && dt.day > 7) return false;

   return true;
}

//=======================================================================
//  DAILY RISK BREACH CHECK
//=======================================================================
bool IsDailyRiskBreached()
{
   double balance = AccountInfoDouble(ACCOUNT_BALANCE);
   double equity  = AccountInfoDouble(ACCOUNT_EQUITY);
   double maxLoss = DailyStartBalance * MaxDailyRiskPct / 100.0;

   if((DailyStartBalance - equity) >= maxLoss) {
      static datetime lastWarn = 0;
      if(TimeCurrent() - lastWarn > 3600) {
         Print("CRT EA: Daily risk limit reached. No new trades today.");
         lastWarn = TimeCurrent();
      }
      return true;
   }
   return false;
}

//=======================================================================
//  COUNT OPEN TRADES
//=======================================================================
int CountOpenTrades()
{
   int count = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--) {
      if(posInfo.SelectByIndex(i)) {
         if(posInfo.Magic() == MagicNumber && posInfo.Symbol() == _Symbol)
            count++;
      }
   }
   return count;
}

// Helper: Set Correct Filling Type
void SetCorrectFillingType()
{
   long fillingMode = SymbolInfoInteger(_Symbol, SYMBOL_FILLING_MODE);
   if((fillingMode & SYMBOL_FILLING_FOK) == SYMBOL_FILLING_FOK) 
      trade.SetTypeFilling(ORDER_FILLING_FOK);
   else if((fillingMode & SYMBOL_FILLING_IOC) == SYMBOL_FILLING_IOC) 
      trade.SetTypeFilling(ORDER_FILLING_IOC);
   else 
      trade.SetTypeFilling(ORDER_FILLING_RETURN);
}

//=======================================================================
//  ON TRADE EVENT (partial close at TP1)
//=======================================================================
void OnTradeTransaction(const MqlTradeTransaction &trans,
                        const MqlTradeRequest &request,
                        const MqlTradeResult &result)
{
   // Handled by dual-lot approach: TP1 closes first lot, TP2 closes second
}

//=======================================================================
//  CHART DISPLAY
//=======================================================================
void OnChartEvent(const int id, const long &lparam,
                  const double &dparam, const string &sparam)
{
   Comment("=== CRT EA Active ===\n",
           "HTF: ", EnumToString(HTF_Period), " | Entry: ", EnumToString(EntryPeriod), "\n",
           "Open Trades: ", CountOpenTrades(), "/", MaxOpenTrades, "\n",
           "Daily P&L: $", DoubleToString(AccountInfoDouble(ACCOUNT_EQUITY) - DailyStartBalance, 2), "\n",
           "Risk/Trade: ", RiskPercent, "% | Max Daily: ", MaxDailyRiskPct, "%\n",
           "Session Active: ", IsValidSession() ? "YES" : "NO", "\n",
           "Valid Day: ", IsValidTradingDay() ? "YES" : "NO");
}

void OnDeinit(const int reason)
{
   Comment("");
   Print("CRT EA Deinitialized. Reason: ", reason);
}
//+------------------------------------------------------------------+