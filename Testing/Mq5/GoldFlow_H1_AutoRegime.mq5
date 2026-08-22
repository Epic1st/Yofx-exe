//+------------------------------------------------------------------+
//|                 GoldFlow H1 Hybrid — Speed + Safety              |
//|                    Version 3.00 - MT5                            |
//|                                                                  |
//|  Strategy: BRT Entry Speed + GoldFlow Multi-TF Confirmation      |
//|  Modes: TREND (conservative) | SWING (aggressive) | HYBRID       |
//|  Optimized for: XAU/USD on H1                                    |
//+------------------------------------------------------------------+
#property copyright "GoldFlow H1 Hybrid"
#property version   "3.00"
#property strict

#include <Trade\Trade.mqh>
#include <Trade\PositionInfo.mqh>

CTrade        trade;
CPositionInfo posInfo;

//+==================================================================+
//|  INPUT PARAMETERS                                                |
//+==================================================================+

// ══════════ TRADING MODE ══════════
input group "═══ TRADING MODE ═══"
enum ENUM_TRADING_MODE
{
   MODE_TREND,    // Conservative: full multi-TF confirmation
   MODE_SWING,    // Aggressive: H1 only (like BRT Pro)
   MODE_HYBRID    // Smart: trend for direction, swing for timing
};
input ENUM_TRADING_MODE TradingMode = MODE_HYBRID;
input bool   ShowModeOnChart = true;  // Show current mode on chart

// ══════════ BRT ENTRY ENGINE (Fast) ══════════
input group "═══ BRT ENTRY ENGINE ═══"
input int    SwingLength        = 8;     // BRT: Swing lookback
input double ImpulseMult        = 1.2;   // BRT: Min impulse (× ATR)
input int    BreakConfirm       = 2;     // BRT: Bars to confirm break
input double RetestATRMult      = 0.5;   // BRT: Retest zone width
input int    RetestMaxBars      = 30;    // BRT: Max bars for retest
input double SL_ATR_Mult        = 1.2;   // BRT: SL distance
input double TP_ATR_Mult        = 3.5;   // BRT: TP distance

// ══════════ GOLDFLOW CONFIRMATION (Safe) ══════════
input group "═══ GOLDFLOW CONFIRMATION ═══"
input bool   UseH4Trend         = true;
input bool   UseD1Trend         = true;
input int    H4_SMA_Period      = 200;
input int    D1_SMA_Period      = 50;
input bool   UseADXFilter       = true;
input int    ADX_Period         = 14;
input double ADX_Min            = 18.0;
input double ADX_Max            = 65.0;
input bool   UseDXYFilter       = true;
input string DXY_Symbol         = "DX.f";
input int    DXY_SMA_Period     = 50;
input ENUM_TIMEFRAMES DXY_TF    = PERIOD_H1;
input int    DXY_ConfirmBars    = 2;

// ══════════ HYBRID LOGIC ══════════
input group "═══ HYBRID LOGIC ═══"
input double TrendBoostMult     = 1.3;   // Lot multiplier when fully aligned
input double CounterTrendReduct = 0.5;   // Lot reduction when counter-trend
input double MinAlignmentScore  = 1.0;   // Min score to trade (0-3 scale)
input bool   UseSmartTP         = true;  // Dynamic TP based on alignment
input double StrongTP_Mult      = 5.0;   // TP when fully aligned
input double WeakTP_Mult        = 2.5;   // TP when weak alignment

// ══════════ SESSION & NEWS ══════════
input group "═══ SESSION & NEWS ═══"
input bool   UseSessionFilter   = true;
input int    LondonStart        = 8;
input int    LondonEnd          = 12;
input int    NYStart            = 13;
input int    NYEnd              = 17;
input bool   TradeLondon        = true;
input bool   TradeNY            = true;
input bool   TradeOverlap       = false;
input bool   CloseBeforeWeekend = true;
input bool   UseNewsFilter      = true;
input string NewsSchedule       = "";
input int    NewsBlockBeforeMin = 30;
input int    NewsBlockAfterMin  = 60;
input int    NewsSpreadThresh   = 800;
input int    NewsEmergencyMin   = 30;

// ══════════ VOLATILITY ══════════
input group "═══ VOLATILITY ═══"
input bool   UseVolRegime       = true;
input int    VolLookback        = 50;
input double VolPercentileLow   = 20.0;
input double VolPercentileHigh  = 90.0;
input double MaxSpreadATRMult   = 0.5;

// ══════════ RISK MANAGEMENT ══════════
input group "═══ RISK MANAGEMENT ═══"
input double RiskPercent        = 1.0;
input double MaxDailyRisk       = 3.0;
input double DailyProfitTarget  = 5.0;
input int    MaxTradesPerDay    = 4;
input int    MaxTotalPositions  = 2;
input bool   AllowHedge         = false;
input int    Slippage           = 3;
input long   MagicNumber        = 3333;

// ══════════ EXIT MANAGEMENT ══════════
input group "═══ EXIT MANAGEMENT ═══"
input bool   UseBreakeven       = true;
input double BE_ATR_Mult        = 1.0;
input int    BE_OffsetPts       = 200;
input bool   UsePartialClose    = true;
input double Partial_ATR_Mult   = 2.0;
input double PartialPercent     = 50.0;
input bool   UseTrailingStop    = true;
input double Trail_ActivateMult = 3.0;
input double Trail_DistanceMult = 1.5;
input int    Trail_MinStepPts   = 150;
input bool   UseChandelierExit  = true;
input int    ChandelierPeriod   = 10;
input double ChandelierMult     = 2.0;

// ══════════ VISUALS ══════════
input group "═══ VISUALS ═══"
input bool   ShowDashboard      = true;
input bool   ShowStructure      = true;
input bool   ShowRetestZones    = true;
input bool   ShowSignals        = true;
input bool   ApplyDarkTheme     = true;

//+==================================================================+
//|  CONSTANTS                                                       |
//+==================================================================+
#define GFX_PREFIX   "GH_"
#define PANEL_X      15
#define PANEL_Y      30
#define PANEL_W      320
#define PAD          10
#define ROW_H        18
#define HDR_H        32
#define SEC_H        16

#define CLR_BG       C'18,20,30'
#define CLR_HDR      C'25,55,90'
#define CLR_BORDER   C'50,60,80'
#define CLR_TEXT     C'220,220,230'
#define CLR_LABEL    C'140,150,170'
#define CLR_BULL     C'0,230,120'
#define CLR_BEAR     C'255,80,80'
#define CLR_WARN     C'255,180,50'
#define CLR_INFO     C'80,180,255'
#define CLR_DIM      C'100,110,130'
#define CLR_MODE_T   C'0,200,255'
#define CLR_MODE_S   C'255,100,200'
#define CLR_MODE_H   C'180,120,255'

//+==================================================================+
//|  GLOBALS                                                         |
//+==================================================================+
int h_atr, h_adx, h_h4sma, h_d1sma, h_dxy_sma;

double g_necklineBuy = 0;
double g_necklineSell = 0;
bool   g_brokeBuy = false;
bool   g_brokeSell = false;
bool   g_waitingBuy = false;
bool   g_waitingSell = false;
int    g_brokeBarBuy = 0;
int    g_brokeBarSell = 0;

static double g_dailyStartEquity = 0;
static int    g_tradesTodayBuy = 0;
static int    g_tradesTodaySell = 0;
static int    g_lastTradeDay = 0;

static datetime g_newsPauseUntil = 0;
static datetime g_emergencyPauseUntil = 0;

int    g_objCounter = 0;
color  g_origColors[11];
string g_lastSignal = "Waiting";
double g_lastAlignmentScore = 0;

//+==================================================================+
//|  HELPERS                                                         |
//+==================================================================+
double iH(int s) { return iHigh(_Symbol, PERIOD_CURRENT, s); }
double iL(int s) { return iLow(_Symbol, PERIOD_CURRENT, s); }
double iO(int s) { return iOpen(_Symbol, PERIOD_CURRENT, s); }
double iC(int s) { return iClose(_Symbol, PERIOD_CURRENT, s); }
datetime iT(int s) { return iTime(_Symbol, PERIOD_CURRENT, s); }

double GetATR(int shift=1)
{
   double buf[]; ArraySetAsSeries(buf, true);
   if(CopyBuffer(h_atr, 0, shift, 1, buf) <= 0) return 0;
   return buf[0];
}
double GetADX(int shift=1)
{
   double buf[]; ArraySetAsSeries(buf, true);
   if(CopyBuffer(h_adx, 0, shift, 1, buf) <= 0) return 0;
   return buf[0];
}
double GetH4SMA(int shift=1)
{
   double buf[]; ArraySetAsSeries(buf, true);
   if(CopyBuffer(h_h4sma, 0, shift, 1, buf) <= 0) return 0;
   return buf[0];
}
double GetD1SMA(int shift=1)
{
   double buf[]; ArraySetAsSeries(buf, true);
   if(CopyBuffer(h_d1sma, 0, shift, 1, buf) <= 0) return 0;
   return buf[0];
}
double GetDXYClose(int shift=1)
{
   if(!UseDXYFilter || StringLen(DXY_Symbol)==0) return 0;
   double buf[]; ArraySetAsSeries(buf, true);
   if(CopyClose(DXY_Symbol, DXY_TF, shift, 1, buf) <= 0) return 0;
   return buf[0];
}
double GetDXY_SMA(int shift=1)
{
   if(h_dxy_sma == INVALID_HANDLE) return 0;
   double buf[]; ArraySetAsSeries(buf, true);
   if(CopyBuffer(h_dxy_sma, 0, shift, 1, buf) <= 0) return 0;
   return buf[0];
}

//+==================================================================+
//|  SWING DETECTION (BRT Style)                                     |
//+==================================================================+
double GetSwingHigh(int len, int shift)
{
   double pivot = iH(shift + len);
   for(int i=0; i<len; i++)
   {
      if(iH(shift+i) > pivot) return 0;
      if(iH(shift+len*2-i) > pivot) return 0;
   }
   return pivot;
}
double GetSwingLow(int len, int shift)
{
   double pivot = iL(shift + len);
   for(int i=0; i<len; i++)
   {
      if(iL(shift+i) < pivot) return 0;
      if(iL(shift+len*2-i) < pivot) return 0;
   }
   return pivot;
}

//+==================================================================+
//|  ALIGNMENT SCORING (The Hybrid Brain)                            |
//+==================================================================+
struct AlignmentData
{
   double score;      // 0-3
   string label;      // "STRONG", "MODERATE", "WEAK", "COUNTER"
   color  clr;
   bool   canTrade;
};

AlignmentData GetAlignment(bool isBuy)
{
   AlignmentData ad;
   ad.score = 0;
   ad.canTrade = false;

   double close = iC(1);

   // Score 1: H4 Trend
   if(UseH4Trend)
   {
      double h4 = GetH4SMA(1);
      if(h4 > 0)
      {
         if(isBuy && close > h4) ad.score += 1.0;
         else if(!isBuy && close < h4) ad.score += 1.0;
      }
   }
   else ad.score += 0.5; // neutral if disabled

   // Score 2: D1 Trend
   if(UseD1Trend)
   {
      double d1 = GetD1SMA(1);
      if(d1 > 0)
      {
         if(isBuy && close > d1) ad.score += 1.0;
         else if(!isBuy && close < d1) ad.score += 1.0;
      }
   }
   else ad.score += 0.5;

   // Score 3: DXY Correlation
   if(UseDXYFilter && StringLen(DXY_Symbol) > 0)
   {
      double dxyC = GetDXYClose(1);
      double dxyS = GetDXY_SMA(1);
      if(dxyC > 0 && dxyS > 0)
      {
         // For Gold BUY, want DXY bearish (below SMA)
         if(isBuy && dxyC < dxyS) ad.score += 1.0;
         else if(!isBuy && dxyC > dxyS) ad.score += 1.0;
      }
   }
   else ad.score += 0.5;

   // Determine label
   if(ad.score >= 2.5) { ad.label = "STRONG"; ad.clr = CLR_BULL; ad.canTrade = true; }
   else if(ad.score >= 1.5) { ad.label = "MODERATE"; ad.clr = CLR_INFO; ad.canTrade = true; }
   else if(ad.score >= 0.5) { ad.label = "WEAK"; ad.clr = CLR_WARN; ad.canTrade = (TradingMode == MODE_SWING); }
   else { ad.label = "COUNTER"; ad.clr = CLR_BEAR; ad.canTrade = false; }

   return ad;
}

string GetModeLabel()
{
   switch(TradingMode)
   {
      case MODE_TREND: return "TREND (Conservative)";
      case MODE_SWING: return "SWING (Aggressive)";
      case MODE_HYBRID: return "HYBRID (Smart)";
   }
   return "UNKNOWN";
}
color GetModeColor()
{
   switch(TradingMode)
   {
      case MODE_TREND: return CLR_MODE_T;
      case MODE_SWING: return CLR_MODE_S;
      case MODE_HYBRID: return CLR_MODE_H;
   }
   return CLR_TEXT;
}

//+==================================================================+
//|  FILTERS                                                         |
//+==================================================================+
bool IsTradingSession()
{
   if(!UseSessionFilter) return true;
   MqlDateTime dt; TimeToStruct(TimeCurrent(), dt);
   int h = dt.hour;
   bool london = (h >= LondonStart && h < LondonEnd);
   bool ny = (h >= NYStart && h < NYEnd);
   bool overlap = (h >= NYStart && h < LondonEnd);
   if(TradeOverlap) return overlap;
   if(TradeLondon && london) return true;
   if(TradeNY && ny) return true;
   return false;
}
bool IsFridayCloseTime()
{
   if(!CloseBeforeWeekend) return false;
   MqlDateTime dt; TimeToStruct(TimeCurrent(), dt);
   return (dt.day_of_week == 5 && dt.hour >= 21);
}
void ResetDailyCounters()
{
   MqlDateTime dt; TimeToStruct(TimeCurrent(), dt);
   if(dt.day != g_lastTradeDay)
   {
      g_tradesTodayBuy = 0; g_tradesTodaySell = 0;
      g_dailyStartEquity = AccountInfoDouble(ACCOUNT_EQUITY);
      g_lastTradeDay = dt.day;
   }
}
bool DailyCircuitBreakerOK()
{
   double eq = AccountInfoDouble(ACCOUNT_EQUITY);
   double dd = (g_dailyStartEquity - eq) / g_dailyStartEquity * 100.0;
   if(dd >= MaxDailyRisk) return false;
   if(DailyProfitTarget > 0)
   {
      double profit = (eq - g_dailyStartEquity) / g_dailyStartEquity * 100.0;
      if(profit >= DailyProfitTarget) return false;
   }
   return true;
}

bool IsNewsBlocked()
{
   if(!UseNewsFilter) return false;
   datetime now = TimeCurrent();
   if(now < g_emergencyPauseUntil) return true;

   // Parse simple schedule: "MM.DD HH:MM,MM.DD HH:MM"
   if(StringLen(NewsSchedule) > 0)
   {
      string rem = NewsSchedule;
      while(StringLen(rem) > 0)
      {
         int comma = StringFind(rem, ",");
         string ev = (comma < 0) ? rem : StringSubstr(rem, 0, comma);
         int sp = StringFind(ev, " ");
         if(sp > 0)
         {
            string dp = StringSubstr(ev, 0, sp);
            string tp = StringSubstr(ev, sp+1);
            int dot = StringFind(dp, ".");
            int col = StringFind(tp, ":");
            if(dot > 0 && col > 0)
            {
               int mo = (int)StringToInteger(StringSubstr(dp, 0, dot));
               int da = (int)StringToInteger(StringSubstr(dp, dot+1));
               int hr = (int)StringToInteger(StringSubstr(tp, 0, col));
               int mn = (int)StringToInteger(StringSubstr(tp, col+1));
               MqlDateTime edt; TimeToStruct(now, edt);
               edt.mon = mo; edt.day = da; edt.hour = hr; edt.min = mn; edt.sec = 0;
               datetime et = StructToTime(edt);
               if(now >= et - NewsBlockBeforeMin*60 && now <= et + NewsBlockAfterMin*60)
                  return true;
            }
         }
         if(comma < 0) break;
         rem = StringSubstr(rem, comma+1);
      }
   }

   int spread = (int)SymbolInfoInteger(_Symbol, SYMBOL_SPREAD);
   if(spread > NewsSpreadThresh && NewsSpreadThresh > 0)
   {
      g_emergencyPauseUntil = now + NewsEmergencyMin*60;
      Print("GH | EMERGENCY PAUSE | Spread:", spread);
      return true;
   }
   return false;
}

bool VolatilityOK(double atr)
{
   if(!UseVolRegime) return true;
   if(atr <= 0) return false;
   double vals[]; ArrayResize(vals, VolLookback); ArraySetAsSeries(vals, true);
   for(int i=0; i<VolLookback; i++)
   {
      double b[]; ArraySetAsSeries(b, true);
      if(CopyBuffer(h_atr, 0, i+1, 1, b) <= 0) return true;
      vals[i] = b[0];
   }
   ArraySort(vals);
   int il = (int)MathFloor(VolLookback * VolPercentileLow / 100.0);
   int ih = (int)MathFloor(VolLookback * VolPercentileHigh / 100.0);
   il = MathMax(0, MathMin(VolLookback-1, il));
   ih = MathMax(0, MathMin(VolLookback-1, ih));
   return (atr >= vals[il] && atr <= vals[ih]);
}
bool SpreadOK(double atr)
{
   if(atr <= 0) return true;
   int sp = (int)SymbolInfoInteger(_Symbol, SYMBOL_SPREAD);
   double pt = SymbolInfoDouble(_Symbol, SYMBOL_POINT);
   return (sp * pt <= atr * MaxSpreadATRMult);
}
bool ADXFilterOK()
{
   if(!UseADXFilter) return true;
   double adx = GetADX(1);
   return (adx >= ADX_Min && adx <= ADX_Max);
}

//+==================================================================+
//|  POSITION HELPERS                                                |
//+==================================================================+
int CountPositions(int type=-1)
{
   int cnt = 0;
   for(int i=PositionsTotal()-1; i>=0; i--)
   {
      if(posInfo.SelectByIndex(i))
      {
         if(posInfo.Symbol()==_Symbol && posInfo.Magic()==MagicNumber)
         {
            if(type==-1 || posInfo.PositionType()==type) cnt++;
         }
      }
   }
   return cnt;
}
bool HedgeOK(int newType)
{
   if(AllowHedge) return true;
   if(newType==POSITION_TYPE_BUY && CountPositions(POSITION_TYPE_SELL)>0) return false;
   if(newType==POSITION_TYPE_SELL && CountPositions(POSITION_TYPE_BUY)>0) return false;
   return true;
}

//+==================================================================+
//|  LOT CALCULATION (Smart Sizing)                                  |
//+==================================================================+
double CalcLot(double slDistance, double alignmentScore)
{
   if(slDistance <= 0) return 0.01;

   double balance = AccountInfoDouble(ACCOUNT_BALANCE);
   double riskAmt = balance * RiskPercent / 100.0;
   double tickVal = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE);
   double tickSize = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);

   if(tickVal <= 0 || tickSize <= 0) return 0.01;

   double slTicks = slDistance / tickSize;
   double lot = riskAmt / (slTicks * tickVal);

   // Apply alignment multiplier
   if(TradingMode == MODE_HYBRID)
   {
      if(alignmentScore >= 2.5) lot *= TrendBoostMult;
      else if(alignmentScore < 1.0) lot *= CounterTrendReduct;
   }

   double minLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double maxLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
   double lotStep = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);

   lot = MathFloor(lot / lotStep) * lotStep;
   lot = MathMax(minLot, MathMin(maxLot, lot));
   return NormalizeDouble(lot, 2);
}

//+==================================================================+
//|  EXIT MANAGEMENT                                                 |
//+==================================================================+
void ManageExits(double atr)
{
   if(atr <= 0) return;
   double point = SymbolInfoDouble(_Symbol, SYMBOL_POINT);
   if(point <= 0) return;

   for(int i=PositionsTotal()-1; i>=0; i--)
   {
      if(!posInfo.SelectByIndex(i)) continue;
      if(posInfo.Symbol() != _Symbol || posInfo.Magic() != MagicNumber) continue;

      double entry = posInfo.PriceOpen();
      double currSL = posInfo.StopLoss();
      double currTP = posInfo.TakeProfit();
      double vol = posInfo.Volume();
      string comment = posInfo.Comment();
      bool isPartial = (StringFind(comment, "PC:1") >= 0);

      if(posInfo.PositionType() == POSITION_TYPE_BUY)
      {
         double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
         double profitDist = bid - entry;

         // Breakeven
         if(UseBreakeven && profitDist >= atr * BE_ATR_Mult)
         {
            double beSL = entry + BE_OffsetPts * point;
            beSL = NormalizeDouble(beSL, _Digits);
            if(currSL == 0 || beSL > currSL + point/2)
               trade.PositionModify(posInfo.Ticket(), beSL, currTP);
         }
         // Partial close
         if(UsePartialClose && !isPartial && profitDist >= atr * Partial_ATR_Mult)
         {
            double closeVol = NormalizeDouble(vol * PartialPercent / 100.0, 2);
            double minLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
            double lotStep = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
            closeVol = MathFloor(closeVol / lotStep) * lotStep;
            closeVol = MathMax(closeVol, minLot);
            if(closeVol >= minLot && (vol - closeVol) >= minLot)
               trade.PositionClosePartial(posInfo.Ticket(), closeVol);
         }
         // Trailing
         if(UseTrailingStop && profitDist >= atr * Trail_ActivateMult)
         {
            double newSL = bid - atr * Trail_DistanceMult;
            newSL = NormalizeDouble(newSL, _Digits);
            double minStep = Trail_MinStepPts * point;
            if(currSL == 0 || newSL > currSL + minStep)
               trade.PositionModify(posInfo.Ticket(), newSL, currTP);
         }
         // Chandelier
         if(UseChandelierExit && isPartial)
         {
            double highest = iHighest(_Symbol, PERIOD_CURRENT, MODE_HIGH, ChandelierPeriod, 1);
            double chSL = highest - atr * ChandelierMult;
            chSL = NormalizeDouble(chSL, _Digits);
            if(currSL == 0 || chSL > currSL + point)
               trade.PositionModify(posInfo.Ticket(), chSL, currTP);
         }
      }
      else // SELL
      {
         double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
         double profitDist = entry - ask;

         if(UseBreakeven && profitDist >= atr * BE_ATR_Mult)
         {
            double beSL = entry - BE_OffsetPts * point;
            beSL = NormalizeDouble(beSL, _Digits);
            if(currSL == 0 || beSL < currSL - point/2)
               trade.PositionModify(posInfo.Ticket(), beSL, currTP);
         }
         if(UsePartialClose && !isPartial && profitDist >= atr * Partial_ATR_Mult)
         {
            double closeVol = NormalizeDouble(vol * PartialPercent / 100.0, 2);
            double minLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
            double lotStep = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
            closeVol = MathFloor(closeVol / lotStep) * lotStep;
            closeVol = MathMax(closeVol, minLot);
            if(closeVol >= minLot && (vol - closeVol) >= minLot)
               trade.PositionClosePartial(posInfo.Ticket(), closeVol);
         }
         if(UseTrailingStop && profitDist >= atr * Trail_ActivateMult)
         {
            double newSL = ask + atr * Trail_DistanceMult;
            newSL = NormalizeDouble(newSL, _Digits);
            double minStep = Trail_MinStepPts * point;
            if(currSL == 0 || newSL < currSL - minStep)
               trade.PositionModify(posInfo.Ticket(), newSL, currTP);
         }
         if(UseChandelierExit && isPartial)
         {
            double lowest = iLowest(_Symbol, PERIOD_CURRENT, MODE_LOW, ChandelierPeriod, 1);
            double chSL = lowest + atr * ChandelierMult;
            chSL = NormalizeDouble(chSL, _Digits);
            if(currSL == 0 || chSL < currSL - point)
               trade.PositionModify(posInfo.Ticket(), chSL, currTP);
         }
      }
   }
}

void CloseAllPositions(string reason)
{
   for(int i=PositionsTotal()-1; i>=0; i--)
   {
      if(posInfo.SelectByIndex(i))
      {
         if(posInfo.Symbol()==_Symbol && posInfo.Magic()==MagicNumber)
         {
            trade.PositionClose(posInfo.Ticket());
            Print("GH | CloseAll | ", reason, " | #", posInfo.Ticket());
         }
      }
   }
}

//+==================================================================+
//|  VISUALS                                                         |
//+==================================================================+
void DrawNecklineBox(bool isBull, datetime t1, datetime t2, double neck, double atr)
{
   if(!ShowRetestZones) return;
   string name = GFX_PREFIX + "BOX_" + (isBull?"B":"S") + IntegerToString(g_objCounter++);
   double top = neck + atr * 0.1;
   double bot = neck - atr * 0.1;
   color clr = isBull ? C'0,150,255' : C'255,80,80';
   if(ObjectFind(0,name)<0) ObjectCreate(0,name,OBJ_RECTANGLE,0,t1,top,t2,bot);
   ObjectSetInteger(0,name,OBJPROP_COLOR,clr);
   ObjectSetInteger(0,name,OBJPROP_FILL,true);
   ObjectSetInteger(0,name,OBJPROP_BACK,true);
   ObjectSetInteger(0,name,OBJPROP_SELECTABLE,false);
}
void DrawSignal(bool isBuy, datetime t, double price, double sl, double tp)
{
   if(!ShowSignals) return;
   string name = GFX_PREFIX + "SIG_" + (isBuy?"B":"S") + IntegerToString(g_objCounter++);
   if(ObjectFind(0,name)<0) ObjectCreate(0,name,OBJ_ARROW,0,t,price);
   ObjectSetInteger(0,name,OBJPROP_ARROWCODE,isBuy?233:234);
   ObjectSetInteger(0,name,OBJPROP_COLOR,isBuy?CLR_BULL:CLR_BEAR);
   ObjectSetInteger(0,name,OBJPROP_WIDTH,3);

   string slN = name+"_SL"; if(ObjectFind(0,slN)<0) ObjectCreate(0,slN,OBJ_HLINE,0,0,sl);
   ObjectSetInteger(0,slN,OBJPROP_COLOR,CLR_BEAR); ObjectSetInteger(0,slN,OBJPROP_STYLE,STYLE_DASH);
   string tpN = name+"_TP"; if(ObjectFind(0,tpN)<0) ObjectCreate(0,tpN,OBJ_HLINE,0,0,tp);
   ObjectSetInteger(0,tpN,OBJPROP_COLOR,CLR_BULL); ObjectSetInteger(0,tpN,OBJPROP_STYLE,STYLE_DASH);
}
void DrawModeLabel()
{
   if(!ShowModeOnChart) return;
   string name = GFX_PREFIX + "MODE";
   if(ObjectFind(0,name)<0) ObjectCreate(0,name,OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,name,OBJPROP_CORNER,CORNER_RIGHT_UPPER);
   ObjectSetInteger(0,name,OBJPROP_XDISTANCE,20);
   ObjectSetInteger(0,name,OBJPROP_YDISTANCE,30);
   ObjectSetString(0,name,OBJPROP_TEXT,GetModeLabel());
   ObjectSetString(0,name,OBJPROP_FONT,"Segoe UI Bold");
   ObjectSetInteger(0,name,OBJPROP_FONTSIZE,11);
   ObjectSetInteger(0,name,OBJPROP_COLOR,GetModeColor());
   ObjectSetInteger(0,name,OBJPROP_ANCHOR,ANCHOR_RIGHT_UPPER);
}
void CleanupGFX()
{
   int total = ObjectsTotal(0,-1,-1);
   for(int i=total-1; i>=0; i--)
   {
      string name = ObjectName(0,i,-1,-1);
      if(StringFind(name,GFX_PREFIX)==0) ObjectDelete(0,name);
   }
}

//+==================================================================+
//|  DASHBOARD                                                       |
//+==================================================================+
void MakePanel(string name, int x, int y, int w, int h, color bg, color border=clrNONE)
{
   if(ObjectFind(0,name)<0) ObjectCreate(0,name,OBJ_RECTANGLE_LABEL,0,0,0);
   ObjectSetInteger(0,name,OBJPROP_XDISTANCE,x);
   ObjectSetInteger(0,name,OBJPROP_YDISTANCE,y);
   ObjectSetInteger(0,name,OBJPROP_XSIZE,w);
   ObjectSetInteger(0,name,OBJPROP_YSIZE,h);
   ObjectSetInteger(0,name,OBJPROP_BGCOLOR,bg);
   ObjectSetInteger(0,name,OBJPROP_BORDER_TYPE,BORDER_FLAT);
   ObjectSetInteger(0,name,OBJPROP_COLOR,border);
   ObjectSetInteger(0,name,OBJPROP_CORNER,CORNER_LEFT_UPPER);
   ObjectSetInteger(0,name,OBJPROP_BACK,false);
   ObjectSetInteger(0,name,OBJPROP_SELECTABLE,false);
   ObjectSetInteger(0,name,OBJPROP_HIDDEN,true);
}
void MakeTxt(string name, int x, int y, string text, int size, color clr, bool bold=false)
{
   if(ObjectFind(0,name)<0) ObjectCreate(0,name,OBJ_LABEL,0,0,0);
   ObjectSetInteger(0,name,OBJPROP_XDISTANCE,x);
   ObjectSetInteger(0,name,OBJPROP_YDISTANCE,y);
   ObjectSetInteger(0,name,OBJPROP_CORNER,CORNER_LEFT_UPPER);
   ObjectSetString(0,name,OBJPROP_TEXT,text);
   ObjectSetString(0,name,OBJPROP_FONT,bold?"Segoe UI Bold":"Segoe UI");
   ObjectSetInteger(0,name,OBJPROP_FONTSIZE,size);
   ObjectSetInteger(0,name,OBJPROP_COLOR,clr);
   ObjectSetInteger(0,name,OBJPROP_ANCHOR,ANCHOR_LEFT_UPPER);
   ObjectSetInteger(0,name,OBJPROP_BACK,false);
   ObjectSetInteger(0,name,OBJPROP_SELECTABLE,false);
   ObjectSetInteger(0,name,OBJPROP_HIDDEN,true);
}

void BuildDashboard()
{
   int y = PANEL_Y;
   int h = 480;
   MakePanel(GFX_PREFIX+"BG", PANEL_X, PANEL_Y, PANEL_W, h, CLR_BG, CLR_BORDER);
   MakePanel(GFX_PREFIX+"HDR", PANEL_X+1, PANEL_Y+1, PANEL_W-2, HDR_H-1, CLR_HDR);

   y += 8;
   MakeTxt(GFX_PREFIX+"TITLE", PANEL_X+PAD, y, "◆ GoldFlow H1 Hybrid", 12, clrWhite, true);
   MakeTxt(GFX_PREFIX+"VER", PANEL_X+PANEL_W-55, y+3, "v3.0", 8, CLR_DIM);
   y += HDR_H + 4;

   // Mode
   MakeTxt(GFX_PREFIX+"S_MODE", PANEL_X+PAD, y, "MODE", 8, CLR_INFO);
   y += SEC_H;
   MakeTxt(GFX_PREFIX+"V_MODE", PANEL_X+PAD, y, GetModeLabel(), 10, GetModeColor(), true);
   y += ROW_H + 4;

   // Account
   MakeTxt(GFX_PREFIX+"S1", PANEL_X+PAD, y, "ACCOUNT", 8, CLR_INFO);
   y += SEC_H;
   MakeTxt(GFX_PREFIX+"L_BAL", PANEL_X+PAD, y, "Balance:", 9, CLR_LABEL);
   MakeTxt(GFX_PREFIX+"V_BAL", PANEL_X+150, y, "---", 9, CLR_TEXT);
   y += ROW_H;
   MakeTxt(GFX_PREFIX+"L_EQ", PANEL_X+PAD, y, "Equity:", 9, CLR_LABEL);
   MakeTxt(GFX_PREFIX+"V_EQ", PANEL_X+150, y, "---", 9, CLR_TEXT);
   y += ROW_H;
   MakeTxt(GFX_PREFIX+"L_DD", PANEL_X+PAD, y, "Daily DD:", 9, CLR_LABEL);
   MakeTxt(GFX_PREFIX+"V_DD", PANEL_X+150, y, "0.0%", 9, CLR_TEXT);
   y += ROW_H + 4;

   // Market
   MakeTxt(GFX_PREFIX+"S2", PANEL_X+PAD, y, "MARKET", 8, CLR_INFO);
   y += SEC_H;
   MakeTxt(GFX_PREFIX+"L_ATR", PANEL_X+PAD, y, "ATR:", 9, CLR_LABEL);
   MakeTxt(GFX_PREFIX+"V_ATR", PANEL_X+150, y, "---", 9, CLR_TEXT);
   y += ROW_H;
   MakeTxt(GFX_PREFIX+"L_ADX", PANEL_X+PAD, y, "ADX:", 9, CLR_LABEL);
   MakeTxt(GFX_PREFIX+"V_ADX", PANEL_X+150, y, "---", 9, CLR_TEXT);
   y += ROW_H;
   MakeTxt(GFX_PREFIX+"L_SPR", PANEL_X+PAD, y, "Spread:", 9, CLR_LABEL);
   MakeTxt(GFX_PREFIX+"V_SPR", PANEL_X+150, y, "---", 9, CLR_TEXT);
   y += ROW_H + 4;

   // Alignment
   MakeTxt(GFX_PREFIX+"S3", PANEL_X+PAD, y, "ALIGNMENT", 8, CLR_INFO);
   y += SEC_H;
   MakeTxt(GFX_PREFIX+"L_BALN", PANEL_X+PAD, y, "Buy Align:", 9, CLR_LABEL);
   MakeTxt(GFX_PREFIX+"V_BALN", PANEL_X+150, y, "--", 9, CLR_TEXT);
   y += ROW_H;
   MakeTxt(GFX_PREFIX+"L_SALN", PANEL_X+PAD, y, "Sell Align:", 9, CLR_LABEL);
   MakeTxt(GFX_PREFIX+"V_SALN", PANEL_X+150, y, "--", 9, CLR_TEXT);
   y += ROW_H + 4;

   // Setups
   MakeTxt(GFX_PREFIX+"S4", PANEL_X+PAD, y, "BULL SETUP", 8, CLR_INFO);
   y += SEC_H;
   MakeTxt(GFX_PREFIX+"L_BPH", PANEL_X+PAD, y, "Phase:", 9, CLR_LABEL);
   MakeTxt(GFX_PREFIX+"V_BPH", PANEL_X+150, y, "--", 9, CLR_TEXT);
   y += ROW_H;
   MakeTxt(GFX_PREFIX+"L_BNECK", PANEL_X+PAD, y, "Neckline:", 9, CLR_LABEL);
   MakeTxt(GFX_PREFIX+"V_BNECK", PANEL_X+150, y, "--", 9, CLR_TEXT);
   y += ROW_H + 4;

   MakeTxt(GFX_PREFIX+"S5", PANEL_X+PAD, y, "BEAR SETUP", 8, CLR_INFO);
   y += SEC_H;
   MakeTxt(GFX_PREFIX+"L_SPH", PANEL_X+PAD, y, "Phase:", 9, CLR_LABEL);
   MakeTxt(GFX_PREFIX+"V_SPH", PANEL_X+150, y, "--", 9, CLR_TEXT);
   y += ROW_H;
   MakeTxt(GFX_PREFIX+"L_SNECK", PANEL_X+PAD, y, "Neckline:", 9, CLR_LABEL);
   MakeTxt(GFX_PREFIX+"V_SNECK", PANEL_X+150, y, "--", 9, CLR_TEXT);
   y += ROW_H + 4;

   // Positions
   MakeTxt(GFX_PREFIX+"S6", PANEL_X+PAD, y, "POSITIONS", 8, CLR_INFO);
   y += SEC_H;
   MakeTxt(GFX_PREFIX+"L_POS", PANEL_X+PAD, y, "Open:", 9, CLR_LABEL);
   MakeTxt(GFX_PREFIX+"V_POS", PANEL_X+150, y, "0", 9, CLR_TEXT);
   y += ROW_H;
   MakeTxt(GFX_PREFIX+"L_PNL", PANEL_X+PAD, y, "P/L:", 9, CLR_LABEL);
   MakeTxt(GFX_PREFIX+"V_PNL", PANEL_X+150, y, "$0.00", 9, CLR_TEXT);
   y += ROW_H;
   MakeTxt(GFX_PREFIX+"L_SIG", PANEL_X+PAD, y, "Signal:", 9, CLR_LABEL);
   MakeTxt(GFX_PREFIX+"V_SIG", PANEL_X+150, y, "● Waiting", 9, CLR_WARN);

   ChartRedraw(0);
}

bool UpdTxt(string name, string txt, color clr=CLR_TEXT)
{
   if(ObjectFind(0,name)<0) return false;
   bool chg = false;
   if(ObjectGetString(0,name,OBJPROP_TEXT) != txt)
   { ObjectSetString(0,name,OBJPROP_TEXT,txt); chg=true; }
   if((color)ObjectGetInteger(0,name,OBJPROP_COLOR) != clr)
   { ObjectSetInteger(0,name,OBJPROP_COLOR,clr); chg=true; }
   return chg;
}

void UpdateDashboard(double atr, double adx)
{
   if(!ShowDashboard) return;
   bool redraw = false;

   string cur = AccountInfoString(ACCOUNT_CURRENCY);
   double bal = AccountInfoDouble(ACCOUNT_BALANCE);
   double eq  = AccountInfoDouble(ACCOUNT_EQUITY);
   double prof = AccountInfoDouble(ACCOUNT_PROFIT);
   int spread = (int)SymbolInfoInteger(_Symbol, SYMBOL_SPREAD);

   redraw |= UpdTxt(GFX_PREFIX+"V_MODE", GetModeLabel(), GetModeColor());
   redraw |= UpdTxt(GFX_PREFIX+"V_BAL", DoubleToString(bal,2)+" "+cur);
   redraw |= UpdTxt(GFX_PREFIX+"V_EQ", DoubleToString(eq,2)+" "+cur, eq>=bal?CLR_BULL:CLR_BEAR);

   double dd = (g_dailyStartEquity>0) ? (g_dailyStartEquity-eq)/g_dailyStartEquity*100.0 : 0;
   redraw |= UpdTxt(GFX_PREFIX+"V_DD", DoubleToString(dd,1)+"%", dd>MaxDailyRisk/2?CLR_WARN:CLR_TEXT);

   redraw |= UpdTxt(GFX_PREFIX+"V_ATR", DoubleToString(atr,_Digits));
   redraw |= UpdTxt(GFX_PREFIX+"V_ADX", DoubleToString(adx,1), adx<ADX_Min||adx>ADX_Max?CLR_WARN:CLR_BULL);
   redraw |= UpdTxt(GFX_PREFIX+"V_SPR", IntegerToString(spread)+" pts", spread>500?CLR_WARN:CLR_TEXT);

   // Alignment scores
   AlignmentData ba = GetAlignment(true);
   AlignmentData sa = GetAlignment(false);
   g_lastAlignmentScore = MathMax(ba.score, sa.score);

   redraw |= UpdTxt(GFX_PREFIX+"V_BALN", ba.label+" ("+DoubleToString(ba.score,1)+")", ba.clr);
   redraw |= UpdTxt(GFX_PREFIX+"V_SALN", sa.label+" ("+DoubleToString(sa.score,1)+")", sa.clr);

   // Bull setup
   string bph="--"; color bpc=CLR_DIM;
   if(g_necklineBuy>0 && !g_brokeBuy) { bph="Waiting Break"; bpc=CLR_WARN; }
   else if(g_necklineBuy>0 && g_brokeBuy && g_waitingBuy) { bph="⚡ Retest"; bpc=CLR_BULL; }
   redraw |= UpdTxt(GFX_PREFIX+"V_BPH", bph, bpc);
   redraw |= UpdTxt(GFX_PREFIX+"V_BNECK", g_necklineBuy>0?DoubleToString(g_necklineBuy,_Digits):"--");

   // Bear setup
   string sph="--"; color spc=CLR_DIM;
   if(g_necklineSell>0 && !g_brokeSell) { sph="Waiting Break"; spc=CLR_WARN; }
   else if(g_necklineSell>0 && g_brokeSell && g_waitingSell) { sph="⚡ Retest"; spc=CLR_BEAR; }
   redraw |= UpdTxt(GFX_PREFIX+"V_SPH", sph, spc);
   redraw |= UpdTxt(GFX_PREFIX+"V_SNECK", g_necklineSell>0?DoubleToString(g_necklineSell,_Digits):"--");

   // Positions
   int posCnt=0; double totalPnL=0;
   for(int i=PositionsTotal()-1; i>=0; i--)
   {
      if(posInfo.SelectByIndex(i) && posInfo.Symbol()==_Symbol && posInfo.Magic()==MagicNumber)
      { posCnt++; totalPnL += posInfo.Profit()+posInfo.Swap(); }
   }
   redraw |= UpdTxt(GFX_PREFIX+"V_POS", IntegerToString(posCnt)+"/"+IntegerToString(MaxTotalPositions));
   redraw |= UpdTxt(GFX_PREFIX+"V_PNL", (totalPnL>=0?"+$":"-$")+DoubleToString(MathAbs(totalPnL),2), totalPnL>=0?CLR_BULL:CLR_BEAR);

   if(g_lastSignal != "Waiting")
   {
      color sClr = StringFind(g_lastSignal,"BUY")>=0 ? CLR_BULL : CLR_BEAR;
      redraw |= UpdTxt(GFX_PREFIX+"V_SIG", "● "+g_lastSignal, sClr);
   }

   if(redraw) ChartRedraw(0);
}

//+==================================================================+
//|  THEME                                                           |
//+==================================================================+
void SaveTheme()
{
   g_origColors[0] = (color)ChartGetInteger(0, CHART_COLOR_BACKGROUND);
   g_origColors[1] = (color)ChartGetInteger(0, CHART_COLOR_FOREGROUND);
   g_origColors[2] = (color)ChartGetInteger(0, CHART_COLOR_GRID);
   g_origColors[3] = (color)ChartGetInteger(0, CHART_COLOR_VOLUME);
   g_origColors[4] = (color)ChartGetInteger(0, CHART_COLOR_CHART_UP);
   g_origColors[5] = (color)ChartGetInteger(0, CHART_COLOR_CHART_DOWN);
   g_origColors[6] = (color)ChartGetInteger(0, CHART_COLOR_CANDLE_BULL);
   g_origColors[7] = (color)ChartGetInteger(0, CHART_COLOR_CANDLE_BEAR);
   g_origColors[8] = (color)ChartGetInteger(0, CHART_COLOR_BID);
   g_origColors[9] = (color)ChartGetInteger(0, CHART_COLOR_ASK);
   g_origColors[10]= (color)ChartGetInteger(0, CHART_COLOR_STOP_LEVEL);
}
void ApplyTheme()
{
   ChartSetInteger(0, CHART_COLOR_BACKGROUND, C'12,12,30');
   ChartSetInteger(0, CHART_COLOR_FOREGROUND, C'180,180,200');
   ChartSetInteger(0, CHART_COLOR_GRID, C'25,25,50');
   ChartSetInteger(0, CHART_COLOR_VOLUME, C'60,120,180');
   ChartSetInteger(0, CHART_COLOR_CHART_UP, C'0,200,255');
   ChartSetInteger(0, CHART_COLOR_CHART_DOWN, C'255,90,90');
   ChartSetInteger(0, CHART_COLOR_CANDLE_BULL, C'0,200,255');
   ChartSetInteger(0, CHART_COLOR_CANDLE_BEAR, C'255,90,90');
   ChartSetInteger(0, CHART_COLOR_BID, C'0,220,220');
   ChartSetInteger(0, CHART_COLOR_ASK, C'255,70,70');
   ChartSetInteger(0, CHART_COLOR_STOP_LEVEL, C'255,140,0');
   ChartRedraw(0);
}
void RestoreTheme()
{
   for(int i=0; i<11; i++)
   {
      int prop = CHART_COLOR_BACKGROUND + i;
      ChartSetInteger(0, prop, g_origColors[i]);
   }
   ChartRedraw(0);
}

//+==================================================================+
//|  INIT / DEINIT                                                   |
//+==================================================================+
int OnInit()
{
   trade.SetExpertMagicNumber(MagicNumber);
   trade.SetDeviationInPoints(Slippage);
   trade.SetTypeFilling(ORDER_FILLING_FOK);
   trade.SetAsyncMode(false);

   h_atr = iATR(_Symbol, PERIOD_CURRENT, 14);
   h_adx = iADX(_Symbol, PERIOD_CURRENT, ADX_Period);
   h_h4sma = iMA(_Symbol, PERIOD_H4, H4_SMA_Period, 0, MODE_SMA, PRICE_CLOSE);
   h_d1sma = iMA(_Symbol, PERIOD_D1, D1_SMA_Period, 0, MODE_SMA, PRICE_CLOSE);

   if(UseDXYFilter && StringLen(DXY_Symbol) > 0)
      h_dxy_sma = iMA(DXY_Symbol, DXY_TF, DXY_SMA_Period, 0, MODE_SMA, PRICE_CLOSE);
   else
      h_dxy_sma = INVALID_HANDLE;

   if(h_atr==INVALID_HANDLE || h_adx==INVALID_HANDLE || h_h4sma==INVALID_HANDLE || h_d1sma==INVALID_HANDLE)
   {
      Print("GH ERROR: Failed to create indicator handles");
      return INIT_FAILED;
   }

   g_necklineBuy = 0; g_necklineSell = 0;
   g_brokeBuy = false; g_brokeSell = false;
   g_waitingBuy = false; g_waitingSell = false;

   ResetDailyCounters();

   if(ApplyDarkTheme) { SaveTheme(); ApplyTheme(); }
   if(ShowDashboard) BuildDashboard();
   if(ShowModeOnChart) DrawModeLabel();

   Print("GoldFlow H1 Hybrid initialized | Mode: ", GetModeLabel());
   return INIT_SUCCEEDED;
}

void OnDeinit(const int reason)
{
   IndicatorRelease(h_atr); IndicatorRelease(h_adx);
   IndicatorRelease(h_h4sma); IndicatorRelease(h_d1sma);
   if(h_dxy_sma != INVALID_HANDLE) IndicatorRelease(h_dxy_sma);
   CleanupGFX();
   if(ApplyDarkTheme) RestoreTheme();
}

//+==================================================================+
//|  MAIN TICK                                                       |
//+==================================================================+
void OnTick()
{
   double atr = GetATR(1);
   double adx = GetADX(1);
   double close = iC(1);

   if(ShowDashboard) UpdateDashboard(atr, adx);
   if(ShowModeOnChart) DrawModeLabel();
   if(atr > 0) ManageExits(atr);

   ResetDailyCounters();
   if(!DailyCircuitBreakerOK()) return;
   if(IsFridayCloseTime()) { CloseAllPositions("Weekend close"); return; }
   if(IsNewsBlocked()) return;

   static datetime lastBar = 0;
   datetime currBar = iT(0);
   if(currBar == lastBar) return;
   lastBar = currBar;

   if(!IsTradingSession()) return;
   if(atr <= 0) return;
   if(!SpreadOK(atr)) return;
   if(!VolatilityOK(atr)) return;
   if(!ADXFilterOK()) return;

   int currIdx = iBarShift(_Symbol, PERIOD_CURRENT, currBar);

   // Get alignments
   AlignmentData buyAlign = GetAlignment(true);
   AlignmentData sellAlign = GetAlignment(false);

   //═══════════════════════════════════════════════════════
   // BULLISH SETUP (BRT Entry Engine)
   //═══════════════════════════════════════════════════════

   // Phase 0: Detect swing high → set neckline
   double sh = GetSwingHigh(SwingLength, 1);
   if(sh > 0)
   {
      double impulse = sh - iL(1 + SwingLength);
      if(impulse >= atr * ImpulseMult)
      {
         g_necklineBuy = sh;
         g_brokeBuy = false;
         g_waitingBuy = false;
         Print("GH | Bull neckline: ", DoubleToString(sh, _Digits));

         if(ShowRetestZones)
         {
            datetime t1 = iT(SwingLength);
            datetime t2 = currBar + PeriodSeconds(PERIOD_CURRENT) * RetestMaxBars;
            DrawNecklineBox(true, t1, t2, sh, atr);
         }
      }
   }

   // Phase 1: Confirm break
   if(g_necklineBuy > 0 && !g_brokeBuy)
   {
      int closesAbove = 0;
      for(int k=1; k<=BreakConfirm; k++)
         if(iC(k) > g_necklineBuy) closesAbove++;

      if(closesAbove >= BreakConfirm)
      {
         g_brokeBuy = true;
         g_brokeBarBuy = currIdx;
         g_waitingBuy = true;
         Print("GH | Bull break confirmed");
      }
   }

   // Phase 2: Wait for retest → ENTER
   if(g_waitingBuy && g_necklineBuy > 0)
   {
      int barsSince = currIdx - g_brokeBarBuy;
      double low1 = iL(1);
      double close1 = iC(1);
      double open1 = iO(1);

      bool inZone = (low1 <= g_necklineBuy + atr * RetestATRMult) && (low1 >= g_necklineBuy - atr * RetestATRMult);
      bool bullishCandle = (close1 > g_necklineBuy) && (close1 > open1);
      bool withinTime = barsSince <= RetestMaxBars;
      bool underLimit = CountPositions() < MaxTotalPositions && g_tradesTodayBuy < MaxTradesPerDay;
      bool hedgeOK = HedgeOK(POSITION_TYPE_BUY);
      bool newsOK = !IsNewsBlocked();

      // MODE-SPECIFIC LOGIC
      bool alignmentOK = false;
      double dynamicTP_Mult = TP_ATR_Mult;

      if(TradingMode == MODE_TREND)
      {
         alignmentOK = buyAlign.canTrade && buyAlign.score >= 2.0;
         dynamicTP_Mult = StrongTP_Mult;
      }
      else if(TradingMode == MODE_SWING)
      {
         alignmentOK = true; // No alignment check
         dynamicTP_Mult = TP_ATR_Mult;
      }
      else // MODE_HYBRID
      {
         alignmentOK = buyAlign.canTrade && buyAlign.score >= MinAlignmentScore;
         // Dynamic TP based on alignment strength
         if(buyAlign.score >= 2.5) dynamicTP_Mult = StrongTP_Mult;
         else if(buyAlign.score >= 1.5) dynamicTP_Mult = TP_ATR_Mult;
         else dynamicTP_Mult = WeakTP_Mult;
      }

      if(inZone && bullishCandle && withinTime && underLimit && hedgeOK && newsOK && alignmentOK)
      {
         double entry = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
         double sl = entry - atr * SL_ATR_Mult;
         double tp = entry + atr * dynamicTP_Mult;

         sl = NormalizeDouble(sl, _Digits);
         tp = NormalizeDouble(tp, _Digits);
         double slDist = entry - sl;
         double lot = CalcLot(slDist, buyAlign.score);

         if(lot > 0)
         {
            string comment = "GH|BUY|A:" + DoubleToString(buyAlign.score,1) + "|M:" + IntegerToString(TradingMode);
            if(trade.Buy(lot, _Symbol, entry, sl, tp, comment))
            {
               g_tradesTodayBuy++;
               g_lastSignal = "BUY @ " + DoubleToString(entry, _Digits) + " [" + buyAlign.label + "]";
               Print("GH | BUY | Entry:", entry, " SL:", sl, " TP:", tp, " Lot:", lot, " Align:", buyAlign.score);
               DrawSignal(true, iT(1), low1, sl, tp);
               g_waitingBuy = false;
               g_necklineBuy = 0;
            }
         }
      }

      if(!withinTime)
      {
         g_waitingBuy = false;
         g_necklineBuy = 0;
         Print("GH | Bull retest expired");
      }
   }

   //═══════════════════════════════════════════════════════
   // BEARISH SETUP (BRT Entry Engine)
   //═══════════════════════════════════════════════════════

   double sl_pivot = GetSwingLow(SwingLength, 1);
   if(sl_pivot > 0)
   {
      double impulse = iH(1 + SwingLength) - sl_pivot;
      if(impulse >= atr * ImpulseMult)
      {
         g_necklineSell = sl_pivot;
         g_brokeSell = false;
         g_waitingSell = false;
         Print("GH | Bear neckline: ", DoubleToString(sl_pivot, _Digits));

         if(ShowRetestZones)
         {
            datetime t1 = iT(SwingLength);
            datetime t2 = currBar + PeriodSeconds(PERIOD_CURRENT) * RetestMaxBars;
            DrawNecklineBox(false, t1, t2, sl_pivot, atr);
         }
      }
   }

   if(g_necklineSell > 0 && !g_brokeSell)
   {
      int closesBelow = 0;
      for(int k=1; k<=BreakConfirm; k++)
         if(iC(k) < g_necklineSell) closesBelow++;

      if(closesBelow >= BreakConfirm)
      {
         g_brokeSell = true;
         g_brokeBarSell = currIdx;
         g_waitingSell = true;
         Print("GH | Bear break confirmed");
      }
   }

   if(g_waitingSell && g_necklineSell > 0)
   {
      int barsSince = currIdx - g_brokeBarSell;
      double high1 = iH(1);
      double close1 = iC(1);
      double open1 = iO(1);

      bool inZone = (high1 >= g_necklineSell - atr * RetestATRMult) && (high1 <= g_necklineSell + atr * RetestATRMult);
      
      bool bearishCandle = (close1 < g_necklineSell) && (close1 < open1);
      bool withinTime = barsSince <= RetestMaxBars;
      bool underLimit = CountPositions() < MaxTotalPositions && g_tradesTodaySell < MaxTradesPerDay;
      bool hedgeOK = HedgeOK(POSITION_TYPE_SELL);
      bool newsOK = !IsNewsBlocked();

      bool alignmentOK = false;
      double dynamicTP_Mult = TP_ATR_Mult;

      if(g_currentMode == MODE_TREND)
      {
         alignmentOK = sellAlign.canTrade && sellAlign.score >= 2.0;
         dynamicTP_Mult = StrongTP_Mult;
      }
      else if(g_currentMode == MODE_SWING)
      {
         alignmentOK = true;
         dynamicTP_Mult = TP_ATR_Mult;
      }
      else
      {
         alignmentOK = sellAlign.canTrade && sellAlign.score >= MinAlignmentScore;
         if(sellAlign.score >= 2.5) dynamicTP_Mult = StrongTP_Mult;
         else if(sellAlign.score >= 1.5) dynamicTP_Mult = TP_ATR_Mult;
         else dynamicTP_Mult = WeakTP_Mult;
      }

      if(inZone && bearishCandle && withinTime && underLimit && hedgeOK && newsOK && alignmentOK)
      {
         double entry = SymbolInfoDouble(_Symbol, SYMBOL_BID);
         double sl = entry + atr * SL_ATR_Mult;
         double tp = entry - atr * dynamicTP_Mult;

         sl = NormalizeDouble(sl, _Digits);
         tp = NormalizeDouble(tp, _Digits);
         double slDist = sl - entry;
         double lot = CalcLot(slDist, sellAlign.score);

         if(lot > 0)
         {
            string comment = "GA|SELL|A:" + DoubleToString(sellAlign.score,1) + "|M:" + GetModeName(g_currentMode);
            if(trade.Sell(lot, _Symbol, entry, sl, tp, comment))
            {
               g_tradesTodaySell++;
               g_tradesInCurrentMode++;
               g_lastSignal = "SELL @ " + DoubleToString(entry, _Digits) + " [" + sellAlign.label + "]";
               Print("GA | SELL | Entry:", entry, " SL:", sl, " TP:", tp, " Lot:", lot, " Mode:", GetModeName(g_currentMode));
               DrawSignal(false, iT(1), high1, sl, tp);
               g_waitingSell = false;
               g_necklineSell = 0;
            }
         }
      }

      if(!withinTime)
      {
         g_waitingSell = false;
         g_necklineSell = 0;
         Print("GA | Bear retest expired");
      }
   }
}

//+==================================================================+
//|  TRADE TRANSACTION                                               |
//+==================================================================+
void OnTradeTransaction(const MqlTradeTransaction& trans,
                        const MqlTradeRequest&     request,
                        const MqlTradeResult&      result)
{
   if(trans.type == TRADE_TRANSACTION_DEAL_ADD)
   {
      double profit = HistoryDealGetDouble(trans.deal, DEAL_PROFIT);
      if(profit != 0)
      {
         string outcome = profit > 0 ? "✓ WIN" : "✗ LOSS";
         Print("GA | DEAL CLOSED | ", outcome,
               " | Profit: $", DoubleToString(profit, 2),
               " | Ticket: ", trans.deal);
         RecordTradeResult(profit);
      }
   }
}
//+------------------------------------------------------------------+
