//+------------------------------------------------------------------+
//|                                     Seventeen_Strategies_EA.mq5  |
//|  Multi-strategy Expert Advisor for MetaTrader 5                  |
//|                                                                  |
//|  Implements the 17 rule-based strategies described in Mario      |
//|  Singh's "17 Proven Currency Trading Strategies" (Wiley, 2013)   |
//|  as independent, individually switchable modules that all run    |
//|  from a single chart. Attach to ONE chart (any symbol / any      |
//|  timeframe); each module trades its own designated symbol and    |
//|  timeframe via the multi-symbol MQL5 API.                        |
//|                                                                  |
//|  MODULES (magic = MagicBase + module number)                     |
//|   1  Rapid-Fire        M1  EURUSD  SMA60 + Parabolic SAR scalp   |
//|   2  Piranha           M5  GBPUSD  BB(12,2) band-touch scalp     |
//|   3  Fade the Break    M30 any     false-break reversal at S/R   |
//|   4  Trade the Break   M15 any     S/R breakout, SL at 60% range |
//|   5  Gawk the Talk     news        Rule-of-20 positive surprise  |
//|   6  Balk the Talk     news        Rule-of-20 negative surprise  |
//|   7  Trend Rider       H4  any     EMA12/36 pullback, ADX40 exit |
//|   8  Trend Bouncer     H4  any     BB(12,2)+BB(12,4) retrace     |
//|   9  Fifth Element     H4  any     MACD histogram 5th-bar entry  |
//|  10  Power Ranger      H1  any     Stochastic 20/80 range trade  |
//|  11  The Pendulum      H4  any     10%-bounce range trade        |
//|  12  Swap & Fly        D1  any     positive-swap carry + 3 candle|
//|  13a Commodity Corr. 1 D1  CADJPY  triggered by Oil breakout     |
//|  13b Commodity Corr. 2 D1  XAUUSD  triggered by USD Index break  |
//|  14  Siamese Twins     news AUDUSD triggered by China data       |
//|  15  Guppy Burst       M5  GBPJPY  00-03h range straddle (OCO)   |
//|  16  English Bkfst Tea M15 GBPUSD  London-open mean reversion    |
//|  17  Good Morning Asia D1  USDJPY  prior-day momentum at NY close|
//|                                                                  |
//|  IMPORTANT OPERATIONAL NOTES                                     |
//|  * News modules (5, 6, 14) use the built-in MQL5 economic        |
//|    calendar: they work on live/demo charts only, NOT in the      |
//|    Strategy Tester (terminal limitation).                        |
//|  * Time-anchored modules (15, 16, 17) assume the broker's server |
//|    day starts at 5pm New York (GMT+2/+3 convention used by       |
//|    IC Markets, FXPRIMUS, etc.). Adjust the hour inputs if your   |
//|    server differs.                                               |
//|  * A hedging account is recommended. On netting accounts the EA  |
//|    automatically collapses multi-target trades into one position |
//|    and different modules trading the same symbol will interfere. |
//|  * Position sizing: percent-risk (book uses 3%/trade; default    |
//|    here is a more conservative 1%) or fixed lots.                |
//|  * Each module opens one setup at a time and never touches       |
//|    positions belonging to other modules or other EAs.            |
//+------------------------------------------------------------------+
#property copyright   "2026"
#property version     "1.11"
#property description "17 rule-based FX strategies (scalping / day / swing / position / mechanical) run as independent modules from a single chart."

#include <Trade/Trade.mqh>

//--- risk mode
enum ENUM_RISK_MODE
  {
   RISK_PERCENT     = 0,  // Percent of balance per trade
   RISK_FIXED_LOTS  = 1   // Fixed lot size per trade
  };

//============================= GLOBAL INPUTS =======================
input group "=== Global settings ==="
input ulong          InpMagicBase        = 26100;         // Magic number base (module N uses base+N)
input ENUM_RISK_MODE InpRiskMode         = RISK_PERCENT;  // Position sizing mode
input double         InpRiskPercent      = 1.0;           // Risk % of balance per trade (book uses 3.0)
input double         InpFixedLots        = 0.01;          // Fixed lots (if fixed mode)
input int            InpSlippagePoints   = 20;            // Max slippage (points)
input string         InpSymbolSuffix     = "";            // Broker symbol suffix (e.g. ".a")
input bool           InpUseTimeFilter    = false;         // Restrict intraday modules 1-4 to a session
input int            InpSessionStartHour = 7;             // Session start hour (server)
input int            InpSessionEndHour   = 21;            // Session end hour (server)
input bool           InpShowPanel        = true;          // Show status panel (chart comment)

input group "=== News-avoidance filter (live/demo only) ==="
input bool   NF_Enable         = false;   // Pause new entries around scheduled news
input int    NF_MinutesBefore  = 30;      // Block entries this many minutes before a release
input int    NF_MinutesAfter   = 30;      // ...and this many minutes after
input bool   NF_HighImpactOnly = true;    // false = medium-impact events also block

input group "=== 1. Rapid-Fire (M1 scalp, EURUSD) ==="
input bool            S1_Enable       = false;        // Enable Rapid-Fire
input string          S1_Symbol       = "EURUSD";     // Symbol ("CURRENT" = chart symbol)
input ENUM_TIMEFRAMES S1_TF           = PERIOD_M1;    // Timeframe
input int             S1_SMAPeriod    = 60;           // SMA period (trend filter)
input double          S1_SARStep      = 0.02;         // Parabolic SAR step
input double          S1_SARMax       = 0.20;         // Parabolic SAR maximum
input double          S1_SL_Pips      = 15;           // Stop loss (pips)
input double          S1_TP_Pips      = 10;           // Take profit (pips)
input double          S1_MaxSpreadPips= 1.5;          // Max spread to trade (pips, 0=off)

input group "=== 2. Piranha (M5 scalp, GBPUSD) ==="
input bool            S2_Enable        = false;       // Enable Piranha
input string          S2_Symbol        = "GBPUSD";    // Symbol
input ENUM_TIMEFRAMES S2_TF            = PERIOD_M5;   // Timeframe
input int             S2_BBPeriod      = 12;          // Bollinger period
input double          S2_BBDev         = 2.0;         // Bollinger deviation
input double          S2_SL_Pips       = 10;          // Stop loss (pips)
input double          S2_TP_Pips       = 5;           // Take profit (pips)
input double          S2_MaxSpreadPips = 2.0;         // Max spread to trade (pips, 0=off)
input bool            S2_ReverseAfterLoss = true;     // After a stop-out only allow opposite side

input group "=== 3. Fade the Break (M30 day trade) ==="
input bool            S3_Enable       = false;        // Enable Fade the Break
input string          S3_Symbol       = "CURRENT";    // Symbol
input ENUM_TIMEFRAMES S3_TF           = PERIOD_M30;   // Timeframe (M15 or M30)
input int             S3_SRLookback   = 24;           // Bars used to find support/resistance
input double          S3_SLBufferPips = 5;            // SL buffer beyond false-break wick (pips)

input group "=== 4. Trade the Break (M15 day trade) ==="
input bool            S4_Enable       = false;        // Enable Trade the Break
input string          S4_Symbol       = "CURRENT";    // Symbol
input ENUM_TIMEFRAMES S4_TF           = PERIOD_M15;   // Timeframe (M15 or M30)
input int             S4_SRLookback   = 24;           // Bars used to find the range
input double          S4_MinRangePips = 20;           // Minimum range height (pips)
input double          S4_SLPctOfRange = 60;           // SL depth into range from broken level (%)

input group "=== 5/6. Gawk & Balk the Talk (news, Rule of 20) ==="
input bool   News_EnableGawk       = false;   // Enable Gawk (trade positive surprises)
input bool   News_EnableBalk       = false;   // Enable Balk (trade negative surprises)
input double News_MinDeviationPct  = 20.0;    // Min |actual-forecast| vs forecast (%)
input double News_IR_MinAbs        = 0.20;    // Interest-rate events: min abs deviation (pts)
input double News_PMI_MinAbs       = 0.50;    // PMI events: min abs deviation (pts)
input bool   News_HighImpactOnly   = true;    // Only high-importance events
input int    News_MaxAgeSec        = 120;     // Max age of release to act on (sec)
input double News_SL_Pips          = 20;      // Stop loss (pips)
input double News_TP_Pips          = 40;      // Take profit (pips)
input string News_USDPair          = "EURUSD";// Pair used when USD is the affected currency

input group "=== 7. Trend Rider (H4 swing) ==="
input bool            S7_Enable      = true;          // Enable Trend Rider
input string          S7_Symbol      = "CURRENT";     // Symbol
input ENUM_TIMEFRAMES S7_TF          = PERIOD_H4;     // Timeframe (H1 or H4)
input int             S7_EMAFast     = 12;            // Fast EMA
input int             S7_EMASlow     = 36;            // Slow EMA
input int             S7_ADXPeriod   = 14;            // ADX period
input double          S7_ADXLevel    = 40;            // ADX exit level
input double          S7_MinSL_Pips  = 30;            // Minimum SL distance (pips)

input group "=== 8. Trend Bouncer (H4 swing) ==="
input bool            S8_Enable      = false;         // Enable Trend Bouncer
input string          S8_Symbol      = "CURRENT";     // Symbol
input ENUM_TIMEFRAMES S8_TF          = PERIOD_H4;     // Timeframe (H1 or H4)
input int             S8_BBPeriod    = 12;            // Bollinger period
input double          S8_BBDevInner  = 2.0;           // Inner band deviation (signal)
input double          S8_BBDevOuter  = 4.0;           // Outer band deviation (stop)

input group "=== 9. Fifth Element (H4 swing) ==="
input bool            S9_Enable      = false;         // Enable Fifth Element
input string          S9_Symbol      = "CURRENT";     // Symbol
input ENUM_TIMEFRAMES S9_TF          = PERIOD_H4;     // Timeframe (H1 or H4)
input int             S9_MACDFast    = 12;            // MACD fast EMA
input int             S9_MACDSlow    = 26;            // MACD slow EMA
input int             S9_MACDSignal  = 9;             // MACD signal SMA
input int             S9_ConfirmBars = 4;             // Same-sign bars before entry (entry on next)
input int             S9_SwingMaxBars= 60;            // Max lookback for histogram swing SL

input group "=== 10. Power Ranger (H1 swing range) ==="
input bool            S10_Enable       = false;       // Enable Power Ranger
input string          S10_Symbol       = "CURRENT";   // Symbol
input ENUM_TIMEFRAMES S10_TF           = PERIOD_H1;   // Timeframe (H1 or H4)
input int             S10_StochK       = 10;          // Stochastic %K
input int             S10_StochD       = 3;           // Stochastic %D
input int             S10_StochSlow    = 3;           // Stochastic slowing
input double          S10_LevelLow     = 20;          // Oversold level
input double          S10_LevelHigh    = 80;          // Overbought level
input int             S10_RangeBars    = 24;          // Bars defining the range S/R
input int             S10_TrendEMA     = 50;          // EMA period for trend filter
input int             S10_SlopeBars    = 5;           // EMA slope confirmation bars
input double          S10_TP1PctRange  = 75;          // TP1 at this % of the range

input group "=== 11. The Pendulum (H4 swing range) ==="
input bool            S11_Enable       = false;       // Enable Pendulum
input string          S11_Symbol       = "CURRENT";   // Symbol
input ENUM_TIMEFRAMES S11_TF           = PERIOD_H4;   // Timeframe (H1 or H4)
input int             S11_RangeBars    = 50;          // Bars defining the range S/R
input double          S11_MinRangePips = 60;          // Minimum range height (pips)
input double          S11_TouchPct     = 3;           // Proximity to S/R that arms a bounce (% of range)
input double          S11_EntryPct     = 10;          // Bounce size that triggers entry (% of range)
input double          S11_TP1Pct       = 50;          // TP1 at % of range
input double          S11_TP2Pct       = 90;          // TP2 at % of range

input group "=== 12. Swap & Fly (D1 carry) ==="
input bool            S12_Enable       = false;       // Enable Swap & Fly
input string          S12_Symbol       = "CURRENT";   // Symbol (pick a high positive-swap pair)
input ENUM_TIMEFRAMES S12_TF           = PERIOD_D1;   // Timeframe (D1 or W1)
input bool            S12_RequireSwap  = true;        // Only trade the positive-swap side
input int             S12_SwingBars    = 20;          // Bars for the significant swing SL
input bool            S12_UseTP        = false;       // Also set a hard TP at RRx (book: optional)
input double          S12_TP_RR        = 3.0;         // TP multiple of risk when hard TP enabled

input group "=== 13a. Commodity Correlation 1 (Oil -> CADJPY) ==="
input bool            S13a_Enable      = false;       // Enable Commodity Correlation Part 1
input string          S13a_TradeSymbol = "CADJPY";    // Traded pair
input string          S13a_RefSymbol   = "XTIUSD,USOIL,WTI,SpotCrude,CL";  // Oil reference (comma-separated candidates)
input int             S13a_SRLookback  = 20;          // Ref-chart S/R lookback (bars)
input int             S13a_ATRPeriod   = 14;          // ATR period (on traded pair)
input double          S13a_ATRMult     = 2.0;         // SL = ATR x this
input double          S13a_RR          = 3.0;         // TP multiple of risk
input bool            S13a_RefUpBuysTrade = true;     // Ref breakout UP means BUY the traded symbol

input group "=== 13b. Commodity Correlation 2 (USD Index -> Gold) ==="
input bool            S13b_Enable      = false;       // Enable Commodity Correlation Part 2
input string          S13b_TradeSymbol = "XAUUSD";    // Traded symbol (spot gold)
input string          S13b_RefSymbol   = "USDX,DXY,USDIDX,USIDX,USDIndex,USDOLLAR";  // USD-index reference (comma-separated candidates)
input int             S13b_SRLookback  = 20;          // Ref-chart S/R lookback (bars)
input int             S13b_ATRPeriod   = 14;          // ATR period (on traded symbol)
input double          S13b_ATRMult     = 2.0;         // SL = ATR x this
input double          S13b_RR          = 3.0;         // TP multiple of risk
input bool            S13b_RefUpBuysTrade = false;    // Ref breakout UP means BUY the traded symbol

input group "=== 14. Siamese Twins (China news -> AUDUSD) ==="
input bool            S14_Enable       = false;       // Enable Siamese Twins
input string          S14_Symbol       = "AUDUSD";    // Traded pair
input string          S14_CountryCode  = "CN";        // Calendar country code driving the signal

input group "=== 15. Guppy Burst (M5 straddle, GBPJPY) ==="
input bool            S15_Enable        = false;      // Enable Guppy Burst
input string          S15_Symbol        = "GBPJPY";   // Symbol
input int             S15_WindowStart   = 0;          // Range window start hour (server, = 5pm NY)
input int             S15_WindowHours   = 3;          // Range window length (hours)
input double          S15_RR            = 2.0;        // TP multiple of risk
input double          S15_MinRangePips  = 10;         // Skip if window range smaller (pips)
input int             S15_CancelHour    = 23;         // Delete untriggered pendings at this hour

input group "=== 16. English Breakfast Tea (M15, GBPUSD) ==="
input bool            S16_Enable       = false;       // Enable English Breakfast Tea
input string          S16_Symbol       = "GBPUSD";    // Symbol
input int             S16_EntryHour    = 10;          // Entry hour, server time (= 08:30 London)
input int             S16_EntryMinute  = 30;          // Entry minute
input int             S16_RefHour      = 6;           // Reference candle hour (= 04:15 London)
input int             S16_RefMinute    = 15;          // Reference candle minute
input double          S16_SL_Pips      = 30;          // Stop loss (pips); TPs = 1x/2x/3x SL

input group "=== 17. Good Morning Asia (D1, USDJPY) ==="
input bool            S17_Enable       = false;       // Enable Good Morning Asia
input string          S17_Symbol       = "USDJPY";    // Symbol
input double          S17_MinSL_Pips   = 30;          // Minimum SL distance (pips); TP = SL/2

//============================= GLOBALS =============================
#define M_TOTAL 18   // internal module slots: 0..17 -> S1..S13a,S13b,S14,S15,S16,S17

// module indices
#define M_S1   0
#define M_S2   1
#define M_S3   2
#define M_S4   3
#define M_S5   4    // Gawk (news)
#define M_S6   5    // Balk (news)
#define M_S7   6
#define M_S8   7
#define M_S9   8
#define M_S10  9
#define M_S11 10
#define M_S12 11
#define M_S13A 12
#define M_S13B 13
#define M_S14 14
#define M_S15 15
#define M_S16 16
#define M_S17 17

CTrade   trade;
bool     gHedging      = true;
bool     gIsTester     = false;

bool     modEnabled[M_TOTAL];
string   modName[M_TOTAL];
string   modSym[M_TOTAL];              // resolved traded symbol ("" = disabled)
ENUM_TIMEFRAMES modTF[M_TOTAL];
datetime modLastBar[M_TOTAL];

// indicator handles
int hS1_SMA = INVALID_HANDLE, hS1_SAR = INVALID_HANDLE;
int hS2_BB  = INVALID_HANDLE;
int hS7_EMAf= INVALID_HANDLE, hS7_EMAs = INVALID_HANDLE, hS7_ADX = INVALID_HANDLE;
int hS8_BBi = INVALID_HANDLE, hS8_BBo  = INVALID_HANDLE;
int hS9_MACD= INVALID_HANDLE;
int hS10_STO= INVALID_HANDLE, hS10_EMA = INVALID_HANDLE;
int hS13a_ATR = INVALID_HANDLE;
int hS13b_ATR = INVALID_HANDLE;

// strategy state
datetime s2LastSignalBar = 0;
int      s7State = 0;                  // 0 idle | +1 wait pullback long | -1 wait pullback short
double   s7MaxADX = 0.0;               // max ADX seen while a Trend Rider position is open
int      s8Arm  = 0;                   // 0 idle | +1 armed long | -1 armed short
int      s11Arm = 0;
double   s11Sup = 0, s11Res = 0, s11Range = 0;
datetime s11ArmBar = 0;
datetime s15DayPlaced = 0;
datetime s16DayTraded = 0;
string   s13aRef = "", s13bRef = "";

// news engine
ulong    gCalChangeId = 0;
ulong    gDoneValues[];                // processed calendar value ids

// news-avoidance filter cache
datetime nfEvtTime[];                  // scheduled release times
string   nfEvtCurr[];                  // affected currency per release
datetime nfLastRefresh = 0;

//============================= UTILITIES ===========================
ulong MagicOf(const int idx) { return InpMagicBase + (ulong)(idx + 1); }

//--- resolve a symbol string ("CURRENT"/"" -> chart symbol, try suffix)
string ResolveSymbol(string s)
  {
   StringTrimLeft(s); StringTrimRight(s);
   if(s=="" || s=="CURRENT" || s=="current")
      s = _Symbol;
   string cand[2];
   cand[0] = s + InpSymbolSuffix;
   cand[1] = s;
   for(int i=0;i<2;i++)
     {
      if(cand[i]=="") continue;
      if(SymbolSelect(cand[i], true))                // known to the terminal -> ensure Market Watch
         return cand[i];
     }
   return "";
  }

//--- try each comma-separated candidate until one resolves
string ResolveSymbolList(string csv)
  {
   string parts[];
   int cnt=StringSplit(csv, ',', parts);
   for(int i=0;i<cnt;i++)
     {
      string r=ResolveSymbol(parts[i]);
      if(r!="") return r;
     }
   return "";
  }

double PipSize(const string sym)
  {
   int    d  = (int)SymbolInfoInteger(sym, SYMBOL_DIGITS);
   double pt = SymbolInfoDouble(sym, SYMBOL_POINT);
   return (d==3 || d==5) ? pt*10.0 : pt;
  }

double Pips2Price(const string sym, const double pips) { return pips * PipSize(sym); }

double SpreadPips(const string sym)
  {
   double ask = SymbolInfoDouble(sym, SYMBOL_ASK);
   double bid = SymbolInfoDouble(sym, SYMBOL_BID);
   double ps  = PipSize(sym);
   if(ps<=0) return 0;
   return (ask-bid)/ps;
  }

double NP(const string sym, const double price)   // normalize price
  {
   int d = (int)SymbolInfoInteger(sym, SYMBOL_DIGITS);
   return NormalizeDouble(price, d);
  }

double NormVol(const string sym, double vol)
  {
   double vmin  = SymbolInfoDouble(sym, SYMBOL_VOLUME_MIN);
   double vmax  = SymbolInfoDouble(sym, SYMBOL_VOLUME_MAX);
   double vstep = SymbolInfoDouble(sym, SYMBOL_VOLUME_STEP);
   if(vstep<=0) vstep = 0.01;
   vol = MathFloor(vol/vstep + 1e-9)*vstep;
   if(vol < vmin) return 0.0;
   if(vol > vmax) vol = vmax;
   return NormalizeDouble(vol, 8);
  }

//--- risk-based lot calculation for a given SL distance (price units)
double CalcLots(const string sym, const double slDistPrice)
  {
   if(InpRiskMode == RISK_FIXED_LOTS)
      return NormVol(sym, InpFixedLots);
   if(slDistPrice <= 0) return 0.0;
   double tickVal  = SymbolInfoDouble(sym, SYMBOL_TRADE_TICK_VALUE);
   double tickSize = SymbolInfoDouble(sym, SYMBOL_TRADE_TICK_SIZE);
   if(tickVal<=0 || tickSize<=0) return NormVol(sym, InpFixedLots);
   double lossPerLot = slDistPrice / tickSize * tickVal;
   if(lossPerLot<=0) return 0.0;
   double riskMoney = AccountInfoDouble(ACCOUNT_BALANCE) * InpRiskPercent / 100.0;
   return NormVol(sym, riskMoney / lossPerLot);
  }

//--- counting helpers -----------------------------------------------
int PositionsByMagic(const ulong magic, const string sym="")
  {
   int n=0;
   for(int i=PositionsTotal()-1; i>=0; i--)
     {
      ulong tk = PositionGetTicket(i);
      if(tk==0) continue;
      if((ulong)PositionGetInteger(POSITION_MAGIC) != magic) continue;
      if(sym!="" && PositionGetString(POSITION_SYMBOL)!=sym) continue;
      n++;
     }
   return n;
  }

int OrdersByMagic(const ulong magic, const string sym="")
  {
   int n=0;
   for(int i=OrdersTotal()-1; i>=0; i--)
     {
      ulong tk = OrderGetTicket(i);
      if(tk==0) continue;
      if((ulong)OrderGetInteger(ORDER_MAGIC) != magic) continue;
      if(sym!="" && OrderGetString(ORDER_SYMBOL)!=sym) continue;
      n++;
     }
   return n;
  }

bool ModuleBusy(const int idx)
  {
   ulong m = MagicOf(idx);
   return (PositionsByMagic(m) + OrdersByMagic(m)) > 0;
  }

void DeletePendingsByMagic(const ulong magic)
  {
   for(int i=OrdersTotal()-1; i>=0; i--)
     {
      ulong tk = OrderGetTicket(i);
      if(tk==0) continue;
      if((ulong)OrderGetInteger(ORDER_MAGIC) != magic) continue;
      trade.OrderDelete(tk);
     }
  }

void ClosePositionsByMagic(const ulong magic)
  {
   for(int i=PositionsTotal()-1; i>=0; i--)
     {
      ulong tk = PositionGetTicket(i);
      if(tk==0) continue;
      if((ulong)PositionGetInteger(POSITION_MAGIC) != magic) continue;
      trade.PositionClose(tk);
     }
  }

//--- new-bar detector per module ------------------------------------
bool NewBar(const int idx)
  {
   datetime t = iTime(modSym[idx], modTF[idx], 0);
   if(t<=0) return false;
   if(t != modLastBar[idx])
     {
      modLastBar[idx] = t;
      return true;
     }
   return false;
  }

bool InSession()
  {
   if(!InpUseTimeFilter) return true;
   MqlDateTime dt; TimeToStruct(TimeCurrent(), dt);
   if(InpSessionStartHour <= InpSessionEndHour)
      return (dt.hour >= InpSessionStartHour && dt.hour < InpSessionEndHour);
   return (dt.hour >= InpSessionStartHour || dt.hour < InpSessionEndHour);
  }

//--- generic copy of one indicator buffer (series-indexed) ----------
bool GetBuf(const int handle, const int buf, const int count, double &out[])
  {
   if(handle==INVALID_HANDLE) return false;
   ArraySetAsSeries(out, true);
   return (CopyBuffer(handle, buf, 0, count, out) == count);
  }

//--- last closed deal of a module (for Piranha reversal rule) -------
bool LastClosedDeal(const ulong magic, const string sym, bool &wasBuy, double &netProfit)
  {
   if(!HistorySelect(TimeCurrent() - 21*86400, TimeCurrent() + 60)) return false;
   for(int i=HistoryDealsTotal()-1; i>=0; i--)
     {
      ulong tk = HistoryDealGetTicket(i);
      if(tk==0) continue;
      if((ulong)HistoryDealGetInteger(tk, DEAL_MAGIC) != magic) continue;
      if(HistoryDealGetString(tk, DEAL_SYMBOL) != sym) continue;
      if((ENUM_DEAL_ENTRY)HistoryDealGetInteger(tk, DEAL_ENTRY) != DEAL_ENTRY_OUT) continue;
      long dtype = HistoryDealGetInteger(tk, DEAL_TYPE);
      wasBuy     = (dtype == DEAL_TYPE_SELL);   // closing deal of a BUY position is a SELL
      netProfit  = HistoryDealGetDouble(tk, DEAL_PROFIT)
                 + HistoryDealGetDouble(tk, DEAL_SWAP)
                 + HistoryDealGetDouble(tk, DEAL_COMMISSION);
      return true;
     }
   return false;
  }

//--- open a market position, optionally split across several TPs ----
bool OpenTrade(const int idx, const string sym, const int dir,
               double sl, double &tps[], const string tag)
  {
   // news-avoidance: suppress entries near releases affecting this symbol's
   // currencies (modules 5/6/14 are exempt - trading the release is their job)
   if(idx!=M_S5 && idx!=M_S6 && idx!=M_S14 && NewsBlocked(sym))
     {
      PrintFormat("[%s] entry on %s suppressed by news filter", modName[idx], sym);
      return false;
     }
   ulong  magic = MagicOf(idx);
   double point = SymbolInfoDouble(sym, SYMBOL_POINT);
   double ask   = SymbolInfoDouble(sym, SYMBOL_ASK);
   double bid   = SymbolInfoDouble(sym, SYMBOL_BID);
   double entry = (dir>0) ? ask : bid;
   long   stopsPts = SymbolInfoInteger(sym, SYMBOL_TRADE_STOPS_LEVEL);
   double minDist  = stopsPts * point;

   // enforce broker minimum stop distance
   if(dir>0 && (entry - sl) < minDist) sl = entry - minDist;
   if(dir<0 && (sl - entry) < minDist) sl = entry + minDist;
   double dist = MathAbs(entry - sl);
   if(dist <= 0) return false;

   double lots = CalcLots(sym, dist);
   if(lots <= 0)
     {
      PrintFormat("[%s] %s: volume below broker minimum, trade skipped", modName[idx], sym);
      return false;
     }

   int n = ArraySize(tps);
   if(n < 1) return false;
   if(!gHedging) n = 1;                       // netting: single position, furthest target
   double per = NormVol(sym, lots / n);
   if(per <= 0) { n = 1; per = NormVol(sym, lots); }
   if(per <= 0) return false;

   trade.SetExpertMagicNumber(magic);
   trade.SetDeviationInPoints(InpSlippagePoints);
   trade.SetTypeFillingBySymbol(sym);

   bool any=false;
   for(int i=0;i<n;i++)
     {
      double tp = (n==1) ? tps[ArraySize(tps)-1] : tps[i];
      if(dir>0 && tp>0 && (tp-entry) < minDist) tp = entry + minDist;
      if(dir<0 && tp>0 && (entry-tp) < minDist) tp = entry - minDist;
      bool ok = (dir>0)
              ? trade.Buy (per, sym, 0.0, NP(sym,sl), NP(sym,tp), tag)
              : trade.Sell(per, sym, 0.0, NP(sym,sl), NP(sym,tp), tag);
      if(!ok)
         PrintFormat("[%s] order failed on %s: %d / %s",
                     modName[idx], sym, (int)trade.ResultRetcode(), trade.ResultRetcodeDescription());
      any = any || ok;
     }
   return any;
  }

//--- convenience for a single-target trade ---------------------------
bool OpenSingle(const int idx, const string sym, const int dir,
                const double sl, const double tp, const string tag)
  {
   double tps[1]; tps[0]=tp;
   return OpenTrade(idx, sym, dir, sl, tps, tag);
  }

//============================= INIT / DEINIT =======================
void SetupModule(const int idx, const string name, const bool enabled,
                 const string symInput, const ENUM_TIMEFRAMES tf)
  {
   modName[idx]    = name;
   modTF[idx]      = tf;
   modLastBar[idx] = 0;
   modEnabled[idx] = false;
   modSym[idx]     = "";
   if(!enabled) return;
   string s = ResolveSymbol(symInput);
   if(s=="")
     {
      PrintFormat("[%s] symbol '%s' not found - module disabled", name, symInput);
      return;
     }
   MqlRates r[];                                   // warm the history cache
   CopyRates(s, tf, 0, 10, r);
   modSym[idx]     = s;
   modEnabled[idx] = true;
  }

int OnInit()
  {
   gIsTester = (bool)MQLInfoInteger(MQL_TESTER);
   gHedging  = ((ENUM_ACCOUNT_MARGIN_MODE)AccountInfoInteger(ACCOUNT_MARGIN_MODE)
                == ACCOUNT_MARGIN_MODE_RETAIL_HEDGING);
   if(!gHedging)
      Print("Netting account detected: multi-target trades are collapsed to a single position; "
            "avoid running several modules on the same symbol.");

   SetupModule(M_S1,  "S01 RapidFire",   S1_Enable,  S1_Symbol,  S1_TF);
   SetupModule(M_S2,  "S02 Piranha",     S2_Enable,  S2_Symbol,  S2_TF);
   SetupModule(M_S3,  "S03 FadeBreak",   S3_Enable,  S3_Symbol,  S3_TF);
   SetupModule(M_S4,  "S04 TradeBreak",  S4_Enable,  S4_Symbol,  S4_TF);
   SetupModule(M_S5,  "S05 GawkTalk",    News_EnableGawk && !gIsTester, News_USDPair, PERIOD_M15);
   SetupModule(M_S6,  "S06 BalkTalk",    News_EnableBalk && !gIsTester, News_USDPair, PERIOD_M15);
   SetupModule(M_S7,  "S07 TrendRider",  S7_Enable,  S7_Symbol,  S7_TF);
   SetupModule(M_S8,  "S08 TrendBounce", S8_Enable,  S8_Symbol,  S8_TF);
   SetupModule(M_S9,  "S09 FifthElem",   S9_Enable,  S9_Symbol,  S9_TF);
   SetupModule(M_S10, "S10 PowerRanger", S10_Enable, S10_Symbol, S10_TF);
   SetupModule(M_S11, "S11 Pendulum",    S11_Enable, S11_Symbol, S11_TF);
   SetupModule(M_S12, "S12 SwapFly",     S12_Enable, S12_Symbol, S12_TF);
   SetupModule(M_S13A,"S13a OilCADJPY",  S13a_Enable,S13a_TradeSymbol, PERIOD_D1);
   SetupModule(M_S13B,"S13b DXYGold",    S13b_Enable,S13b_TradeSymbol, PERIOD_D1);
   SetupModule(M_S14, "S14 SiameseTwin", S14_Enable && !gIsTester, S14_Symbol, PERIOD_D1);
   SetupModule(M_S15, "S15 GuppyBurst",  S15_Enable, S15_Symbol, PERIOD_M5);
   SetupModule(M_S16, "S16 EngBkfstTea", S16_Enable, S16_Symbol, PERIOD_M15);
   SetupModule(M_S17, "S17 GoodMorning", S17_Enable, S17_Symbol, PERIOD_D1);

   if((News_EnableGawk || News_EnableBalk || S14_Enable) && gIsTester)
      Print("News modules (5/6/14) are inactive in the Strategy Tester - the MQL5 economic calendar is live-only.");
   if(NF_Enable && gIsTester)
      Print("News-avoidance filter is inactive in the Strategy Tester (calendar is live-only) - the spread caps remain the guard there.");

   // reference symbols for the correlation modules
   if(modEnabled[M_S13A])
     {
      s13aRef = ResolveSymbolList(S13a_RefSymbol);
      if(s13aRef=="")
        { PrintFormat("[%s] reference symbol '%s' not found - module disabled", modName[M_S13A], S13a_RefSymbol);
          modEnabled[M_S13A]=false; }
      else
        {
         MqlRates r[]; CopyRates(s13aRef, PERIOD_D1, 0, 10, r);
         PrintFormat("[%s] reference resolved: %s", modName[M_S13A], s13aRef);
        }
     }
   if(modEnabled[M_S13B])
     {
      s13bRef = ResolveSymbolList(S13b_RefSymbol);
      if(s13bRef=="")
        { PrintFormat("[%s] reference symbol '%s' not found - module disabled", modName[M_S13B], S13b_RefSymbol);
          modEnabled[M_S13B]=false; }
      else
        {
         MqlRates r[]; CopyRates(s13bRef, PERIOD_D1, 0, 10, r);
         PrintFormat("[%s] reference resolved: %s", modName[M_S13B], s13bRef);
        }
     }

   // indicator handles
   if(modEnabled[M_S1])
     {
      hS1_SMA = iMA (modSym[M_S1], modTF[M_S1], S1_SMAPeriod, 0, MODE_SMA, PRICE_CLOSE);
      hS1_SAR = iSAR(modSym[M_S1], modTF[M_S1], S1_SARStep, S1_SARMax);
      if(hS1_SMA==INVALID_HANDLE || hS1_SAR==INVALID_HANDLE) modEnabled[M_S1]=false;
     }
   if(modEnabled[M_S2])
     {
      hS2_BB = iBands(modSym[M_S2], modTF[M_S2], S2_BBPeriod, 0, S2_BBDev, PRICE_CLOSE);
      if(hS2_BB==INVALID_HANDLE) modEnabled[M_S2]=false;
     }
   if(modEnabled[M_S7])
     {
      hS7_EMAf = iMA (modSym[M_S7], modTF[M_S7], S7_EMAFast, 0, MODE_EMA, PRICE_CLOSE);
      hS7_EMAs = iMA (modSym[M_S7], modTF[M_S7], S7_EMASlow, 0, MODE_EMA, PRICE_CLOSE);
      hS7_ADX  = iADX(modSym[M_S7], modTF[M_S7], S7_ADXPeriod);
      if(hS7_EMAf==INVALID_HANDLE || hS7_EMAs==INVALID_HANDLE || hS7_ADX==INVALID_HANDLE)
         modEnabled[M_S7]=false;
     }
   if(modEnabled[M_S8])
     {
      hS8_BBi = iBands(modSym[M_S8], modTF[M_S8], S8_BBPeriod, 0, S8_BBDevInner, PRICE_CLOSE);
      hS8_BBo = iBands(modSym[M_S8], modTF[M_S8], S8_BBPeriod, 0, S8_BBDevOuter, PRICE_CLOSE);
      if(hS8_BBi==INVALID_HANDLE || hS8_BBo==INVALID_HANDLE) modEnabled[M_S8]=false;
     }
   if(modEnabled[M_S9])
     {
      hS9_MACD = iMACD(modSym[M_S9], modTF[M_S9], S9_MACDFast, S9_MACDSlow, S9_MACDSignal, PRICE_CLOSE);
      if(hS9_MACD==INVALID_HANDLE) modEnabled[M_S9]=false;
     }
   if(modEnabled[M_S10])
     {
      hS10_STO = iStochastic(modSym[M_S10], modTF[M_S10], S10_StochK, S10_StochD, S10_StochSlow,
                             MODE_SMA, STO_LOWHIGH);
      hS10_EMA = iMA(modSym[M_S10], modTF[M_S10], S10_TrendEMA, 0, MODE_EMA, PRICE_CLOSE);
      if(hS10_STO==INVALID_HANDLE || hS10_EMA==INVALID_HANDLE) modEnabled[M_S10]=false;
     }
   if(modEnabled[M_S13A])
     {
      hS13a_ATR = iATR(modSym[M_S13A], PERIOD_D1, S13a_ATRPeriod);
      if(hS13a_ATR==INVALID_HANDLE) modEnabled[M_S13A]=false;
     }
   if(modEnabled[M_S13B])
     {
      hS13b_ATR = iATR(modSym[M_S13B], PERIOD_D1, S13b_ATRPeriod);
      if(hS13b_ATR==INVALID_HANDLE) modEnabled[M_S13B]=false;
     }

   ArrayResize(gDoneValues, 0);
   EventSetTimer(15);
   Print("Seventeen_Strategies_EA v1.11 initialised.");
   return INIT_SUCCEEDED;
  }

void OnDeinit(const int reason)
  {
   EventKillTimer();
   int handles[12];
   handles[0]=hS1_SMA;  handles[1]=hS1_SAR;  handles[2]=hS2_BB;
   handles[3]=hS7_EMAf; handles[4]=hS7_EMAs; handles[5]=hS7_ADX;
   handles[6]=hS8_BBi;  handles[7]=hS8_BBo;  handles[8]=hS9_MACD;
   handles[9]=hS10_STO; handles[10]=hS10_EMA; handles[11]=hS13a_ATR;
   for(int i=0;i<12;i++) if(handles[i]!=INVALID_HANDLE) IndicatorRelease(handles[i]);
   if(hS13b_ATR!=INVALID_HANDLE) IndicatorRelease(hS13b_ATR);
   Comment("");
  }

//============================= MAIN LOOP ===========================
void OnTick()
  {
   //--- ongoing position management (tick resolution)
   Manage_TrendRider();
   Manage_SwapFly();
   Manage_GuppyBurst();

   //--- entries
   if(modEnabled[M_S1])   Run_S1_RapidFire();
   if(modEnabled[M_S2])   Run_S2_Piranha();
   if(modEnabled[M_S3])   Run_S3_FadeBreak();
   if(modEnabled[M_S4])   Run_S4_TradeBreak();
   if(modEnabled[M_S7])   Run_S7_TrendRider();
   if(modEnabled[M_S8])   Run_S8_TrendBouncer();
   if(modEnabled[M_S9])   Run_S9_FifthElement();
   if(modEnabled[M_S10])  Run_S10_PowerRanger();
   if(modEnabled[M_S11])  Run_S11_Pendulum();
   if(modEnabled[M_S12])  Run_S12_SwapFly();
   if(modEnabled[M_S13A]) Run_S13a_OilCadjpy();
   if(modEnabled[M_S13B]) Run_S13b_DxyGold();
   if(modEnabled[M_S15])  Run_S15_GuppyBurst();
   if(modEnabled[M_S16])  Run_S16_BreakfastTea();
   if(modEnabled[M_S17])  Run_S17_GoodMorningAsia();

   UpdatePanel();
  }

void OnTimer()
  {
   if(gIsTester) return;
   RefreshNewsCache();
   if(modEnabled[M_S5] || modEnabled[M_S6] || modEnabled[M_S14])
      PollCalendar();
  }

//============================= MANAGEMENT ==========================
//--- S7: no fixed TP; exit when ADX pushed above the level and falls back below
void Manage_TrendRider()
  {
   if(!modEnabled[M_S7]) return;
   ulong magic = MagicOf(M_S7);
   if(PositionsByMagic(magic, modSym[M_S7]) == 0)
     {
      s7MaxADX = 0.0;
      return;
     }
   // evaluate once per bar
   static datetime lastEval = 0;
   datetime bt = iTime(modSym[M_S7], modTF[M_S7], 0);
   if(bt == lastEval || bt <= 0) return;
   lastEval = bt;

   double adx[];
   if(!GetBuf(hS7_ADX, 0, 3, adx)) return;
   s7MaxADX = MathMax(s7MaxADX, adx[1]);
   if(s7MaxADX > S7_ADXLevel && adx[1] < S7_ADXLevel)
     {
      PrintFormat("[%s] ADX rolled back below %.0f - closing", modName[M_S7], S7_ADXLevel);
      ClosePositionsByMagic(magic);
      s7MaxADX = 0.0;
     }
  }

//--- S12: shift SL to break-even once open profit reaches 1R
void Manage_SwapFly()
  {
   if(!modEnabled[M_S12]) return;
   ulong magic = MagicOf(M_S12);
   for(int i=PositionsTotal()-1; i>=0; i--)
     {
      ulong tk = PositionGetTicket(i);
      if(tk==0) continue;
      if((ulong)PositionGetInteger(POSITION_MAGIC) != magic) continue;
      string sym   = PositionGetString(POSITION_SYMBOL);
      double open  = PositionGetDouble(POSITION_PRICE_OPEN);
      double sl    = PositionGetDouble(POSITION_SL);
      double tp    = PositionGetDouble(POSITION_TP);
      double cur   = PositionGetDouble(POSITION_PRICE_CURRENT);
      long   ptype = PositionGetInteger(POSITION_TYPE);
      if(sl<=0) continue;
      double risk = MathAbs(open - sl);
      if(risk<=0) continue;
      bool  atBE  = (MathAbs(sl - open) < SymbolInfoDouble(sym, SYMBOL_POINT));
      if(atBE) continue;
      bool  moved = false;
      if(ptype==POSITION_TYPE_BUY  && (cur - open) >= risk) moved = true;
      if(ptype==POSITION_TYPE_SELL && (open - cur) >= risk) moved = true;
      if(moved)
        {
         if(trade.PositionModify(tk, NP(sym, open), tp))
            PrintFormat("[%s] %s stop moved to break-even", modName[M_S12], sym);
        }
     }
  }

//--- S15: one-cancels-other + end-of-day cleanup for the straddle
void Manage_GuppyBurst()
  {
   if(!modEnabled[M_S15]) return;
   ulong magic = MagicOf(M_S15);
   int pend = OrdersByMagic(magic, modSym[M_S15]);
   if(pend>0 && PositionsByMagic(magic, modSym[M_S15])>0)
     {
      DeletePendingsByMagic(magic);            // one side triggered -> cancel the other
      return;
     }
   if(pend>0)
     {
      MqlDateTime dt; TimeToStruct(TimeCurrent(), dt);
      if(dt.hour >= S15_CancelHour)
         DeletePendingsByMagic(magic);         // untriggered by late session -> stand down
     }
  }

//============================= STRATEGIES ==========================
//--- S1: Rapid-Fire - SMA60 trend filter + Parabolic SAR flip (M1)
void Run_S1_RapidFire()
  {
   int idx=M_S1; string sym=modSym[idx];
   if(!NewBar(idx)) return;
   if(!InSession() || ModuleBusy(idx)) return;
   if(S1_MaxSpreadPips>0 && SpreadPips(sym)>S1_MaxSpreadPips) return;

   double sma[], sar[];
   if(!GetBuf(hS1_SMA,0,4,sma) || !GetBuf(hS1_SAR,0,4,sar)) return;
   double c1=iClose(sym,modTF[idx],1), c2=iClose(sym,modTF[idx],2);
   if(c1<=0 || c2<=0) return;
   double ps=PipSize(sym);

   if(c1>sma[1] && sar[1]<c1 && sar[2]>c2)                 // SAR dot flipped below price in an up-move
     {
      double ask=SymbolInfoDouble(sym,SYMBOL_ASK);
      OpenSingle(idx,sym,+1, ask-S1_SL_Pips*ps, ask+S1_TP_Pips*ps, modName[idx]);
     }
   else if(c1<sma[1] && sar[1]>c1 && sar[2]<c2)            // SAR dot flipped above price in a down-move
     {
      double bid=SymbolInfoDouble(sym,SYMBOL_BID);
      OpenSingle(idx,sym,-1, bid+S1_SL_Pips*ps, bid-S1_TP_Pips*ps, modName[idx]);
     }
  }

//--- S2: Piranha - Bollinger(12,2) band-touch fade (M5, tick-level)
void Run_S2_Piranha()
  {
   int idx=M_S2; string sym=modSym[idx];
   if(!InSession() || ModuleBusy(idx)) return;
   datetime bt=iTime(sym,modTF[idx],0);
   if(bt<=0 || bt==s2LastSignalBar) return;               // one attempt per bar
   if(S2_MaxSpreadPips>0 && SpreadPips(sym)>S2_MaxSpreadPips) return;

   double up[], lo[];
   if(!GetBuf(hS2_BB,1,2,up) || !GetBuf(hS2_BB,2,2,lo)) return;
   double bid=SymbolInfoDouble(sym,SYMBOL_BID);
   double ps=PipSize(sym);
   int dir=0;
   if(bid<=lo[0]) dir=+1;                                  // touch of lower band -> long
   else if(bid>=up[0]) dir=-1;                             // touch of upper band -> short
   if(dir==0) return;

   if(S2_ReverseAfterLoss)                                 // after a stop-out, only the opposite side
     {
      bool wasBuy=false; double pf=0;
      if(LastClosedDeal(MagicOf(idx),sym,wasBuy,pf))
         if(pf<0 && ((wasBuy && dir>0) || (!wasBuy && dir<0))) return;
     }
   s2LastSignalBar=bt;
   if(dir>0)
     {
      double ask=SymbolInfoDouble(sym,SYMBOL_ASK);
      OpenSingle(idx,sym,+1, ask-S2_SL_Pips*ps, ask+S2_TP_Pips*ps, modName[idx]);
     }
   else
      OpenSingle(idx,sym,-1, bid+S2_SL_Pips*ps, bid-S2_TP_Pips*ps, modName[idx]);
  }

//--- S3: Fade the Break - false break of S/R closing back inside
void Run_S3_FadeBreak()
  {
   int idx=M_S3; string sym=modSym[idx]; ENUM_TIMEFRAMES tf=modTF[idx];
   if(!NewBar(idx)) return;
   if(!InSession() || ModuleBusy(idx)) return;
   if(Bars(sym,tf) < S3_SRLookback+4) return;

   int loIdx=iLowest (sym,tf,MODE_LOW, S3_SRLookback,2);
   int hiIdx=iHighest(sym,tf,MODE_HIGH,S3_SRLookback,2);
   if(loIdx<0 || hiIdx<0) return;
   double sup=iLow(sym,tf,loIdx), res=iHigh(sym,tf,hiIdx);
   double o1=iOpen(sym,tf,1), c1=iClose(sym,tf,1);
   double h1=iHigh(sym,tf,1), l1=iLow(sym,tf,1);
   double ps=PipSize(sym);

   if(l1<sup && c1>sup && c1>o1)                           // wick below support, bull close back above
     {
      double ask=SymbolInfoDouble(sym,SYMBOL_ASK);
      double sl=l1 - S3_SLBufferPips*ps;
      double risk=ask-sl; if(risk<=0) return;
      double tps[2]; tps[0]=ask+risk; tps[1]=ask+2.0*risk;
      OpenTrade(idx,sym,+1,sl,tps,modName[idx]);
     }
   else if(h1>res && c1<res && c1<o1)                      // wick above resistance, bear close back below
     {
      double bid=SymbolInfoDouble(sym,SYMBOL_BID);
      double sl=h1 + S3_SLBufferPips*ps;
      double risk=sl-bid; if(risk<=0) return;
      double tps[2]; tps[0]=bid-risk; tps[1]=bid-2.0*risk;
      OpenTrade(idx,sym,-1,sl,tps,modName[idx]);
     }
  }

//--- S4: Trade the Break - close beyond S/R, SL at 60% of the range
void Run_S4_TradeBreak()
  {
   int idx=M_S4; string sym=modSym[idx]; ENUM_TIMEFRAMES tf=modTF[idx];
   if(!NewBar(idx)) return;
   if(!InSession() || ModuleBusy(idx)) return;
   if(Bars(sym,tf) < S4_SRLookback+4) return;

   int loIdx=iLowest (sym,tf,MODE_LOW, S4_SRLookback,2);
   int hiIdx=iHighest(sym,tf,MODE_HIGH,S4_SRLookback,2);
   if(loIdx<0 || hiIdx<0) return;
   double sup=iLow(sym,tf,loIdx), res=iHigh(sym,tf,hiIdx);
   double range=res-sup;
   double ps=PipSize(sym);
   if(range < S4_MinRangePips*ps) return;
   double c1=iClose(sym,tf,1), c2=iClose(sym,tf,2);

   if(c1>res && c2<=res)                                   // breakout candle closed above resistance
     {
      double ask=SymbolInfoDouble(sym,SYMBOL_ASK);
      double sl=res - S4_SLPctOfRange/100.0*range;
      double risk=ask-sl; if(risk<=0) return;
      double tps[2]; tps[0]=ask+risk; tps[1]=ask+2.0*risk;
      OpenTrade(idx,sym,+1,sl,tps,modName[idx]);
     }
   else if(c1<sup && c2>=sup)                              // breakout candle closed below support
     {
      double bid=SymbolInfoDouble(sym,SYMBOL_BID);
      double sl=sup + S4_SLPctOfRange/100.0*range;
      double risk=sl-bid; if(risk<=0) return;
      double tps[2]; tps[0]=bid-risk; tps[1]=bid-2.0*risk;
      OpenTrade(idx,sym,-1,sl,tps,modName[idx]);
     }
  }

//--- S7: Trend Rider - EMA12/36 cross, pullback entry, ADX-based exit
void Run_S7_TrendRider()
  {
   int idx=M_S7; string sym=modSym[idx]; ENUM_TIMEFRAMES tf=modTF[idx];
   if(!NewBar(idx)) return;

   double ef[], es[];
   if(!GetBuf(hS7_EMAf,0,3,ef) || !GetBuf(hS7_EMAs,0,3,es)) return;

   if(ef[1]>es[1] && ef[2]<=es[2]) s7State=+1;             // fresh bullish cross: wait for pullback
   if(ef[1]<es[1] && ef[2]>=es[2]) s7State=-1;             // fresh bearish cross
   if(s7State==+1 && ef[1]<es[1]) s7State=0;               // context invalidated
   if(s7State==-1 && ef[1]>es[1]) s7State=0;
   if(s7State==0 || ModuleBusy(idx)) return;

   double ps=PipSize(sym);
   double l1=iLow(sym,tf,1), h1=iHigh(sym,tf,1);

   if(s7State==+1 && l1<=ef[1])                            // price came back to touch EMA12
     {
      double ask=SymbolInfoDouble(sym,SYMBOL_ASK);
      double sl=es[1];
      if(ask-sl < S7_MinSL_Pips*ps) sl=ask - S7_MinSL_Pips*ps;
      if(OpenSingle(idx,sym,+1,sl,0.0,modName[idx]))       // no fixed TP: ADX manages the exit
        { s7State=0; s7MaxADX=0.0; }
     }
   else if(s7State==-1 && h1>=ef[1])
     {
      double bid=SymbolInfoDouble(sym,SYMBOL_BID);
      double sl=es[1];
      if(sl-bid < S7_MinSL_Pips*ps) sl=bid + S7_MinSL_Pips*ps;
      if(OpenSingle(idx,sym,-1,sl,0.0,modName[idx]))
        { s7State=0; s7MaxADX=0.0; }
     }
  }

//--- S8: Trend Bouncer - inner-band momentum, entry on mid-line retrace
void Run_S8_TrendBouncer()
  {
   int idx=M_S8; string sym=modSym[idx]; ENUM_TIMEFRAMES tf=modTF[idx];
   if(!NewBar(idx)) return;

   double upI[], loI[], mid[], upO[], loO[];
   if(!GetBuf(hS8_BBi,1,3,upI) || !GetBuf(hS8_BBi,2,3,loI) || !GetBuf(hS8_BBi,0,3,mid)) return;
   if(!GetBuf(hS8_BBo,1,3,upO) || !GetBuf(hS8_BBo,2,3,loO)) return;
   double h1=iHigh(sym,tf,1), l1=iLow(sym,tf,1);

   if(h1>=upI[1]) s8Arm=+1;                                // upward momentum leg
   if(l1<=loI[1]) s8Arm=-1;                                // downward momentum leg
   if(s8Arm==0 || ModuleBusy(idx)) return;

   if(s8Arm==+1 && l1<=mid[1] && h1<upI[1])                // retrace to the MA12 mid-line
     {
      double ask=SymbolInfoDouble(sym,SYMBOL_ASK);
      double sl=loO[1];                                    // outer BB(12,4) lower band
      double risk=ask-sl; if(risk<=0) return;
      double tps[3]; tps[0]=ask+risk; tps[1]=ask+2.0*risk; tps[2]=ask+3.0*risk;
      if(OpenTrade(idx,sym,+1,sl,tps,modName[idx])) s8Arm=0;
     }
   else if(s8Arm==-1 && h1>=mid[1] && l1>loI[1])
     {
      double bid=SymbolInfoDouble(sym,SYMBOL_BID);
      double sl=upO[1];                                    // outer BB(12,4) upper band
      double risk=sl-bid; if(risk<=0) return;
      double tps[3]; tps[0]=bid-risk; tps[1]=bid-2.0*risk; tps[2]=bid-3.0*risk;
      if(OpenTrade(idx,sym,-1,sl,tps,modName[idx])) s8Arm=0;
     }
  }

//--- S9: Fifth Element - MACD histogram sign flip, entry on the 5th bar
void Run_S9_FifthElement()
  {
   int idx=M_S9; string sym=modSym[idx]; ENUM_TIMEFRAMES tf=modTF[idx];
   if(!NewBar(idx)) return;
   if(ModuleBusy(idx)) return;

   int need=S9_ConfirmBars + S9_SwingMaxBars + 3;
   double hist[];
   if(!GetBuf(hS9_MACD,0,need,hist)) return;               // MAIN buffer = EMA12-EMA26 (MT4-style histogram)

   bool allPos=true, allNeg=true;
   for(int i=1;i<=S9_ConfirmBars;i++)
     {
      if(hist[i]<=0) allPos=false;
      if(hist[i]>=0) allNeg=false;
     }

   if(allPos && hist[S9_ConfirmBars+1]<=0)                 // flip to positive, 4 bars confirmed -> enter on 5th
     {
      double sl=DBL_MAX;
      for(int i=S9_ConfirmBars+1; i<need-1 && hist[i]<=0; i++)
         sl=MathMin(sl, iLow(sym,tf,i));                   // low of the preceding negative phase
      if(sl==DBL_MAX)
        {
         int k=iLowest(sym,tf,MODE_LOW,20,1);
         if(k<0) return;
         sl=iLow(sym,tf,k);
        }
      double ask=SymbolInfoDouble(sym,SYMBOL_ASK);
      double risk=ask-sl; if(risk<=0) return;
      double tps[2]; tps[0]=ask+risk; tps[1]=ask+2.0*risk;
      OpenTrade(idx,sym,+1,sl,tps,modName[idx]);
     }
   else if(allNeg && hist[S9_ConfirmBars+1]>=0)            // flip to negative
     {
      double sl=-DBL_MAX;
      for(int i=S9_ConfirmBars+1; i<need-1 && hist[i]>=0; i++)
         sl=MathMax(sl, iHigh(sym,tf,i));                  // high of the preceding positive phase
      if(sl==-DBL_MAX)
        {
         int k=iHighest(sym,tf,MODE_HIGH,20,1);
         if(k<0) return;
         sl=iHigh(sym,tf,k);
        }
      double bid=SymbolInfoDouble(sym,SYMBOL_BID);
      double risk=sl-bid; if(risk<=0) return;
      double tps[2]; tps[0]=bid-risk; tps[1]=bid-2.0*risk;
      OpenTrade(idx,sym,-1,sl,tps,modName[idx]);
     }
  }

//--- S10: Power Ranger - stochastic 20/80 re-cross inside a forming range
void Run_S10_PowerRanger()
  {
   int idx=M_S10; string sym=modSym[idx]; ENUM_TIMEFRAMES tf=modTF[idx];
   if(!NewBar(idx)) return;
   if(ModuleBusy(idx)) return;
   if(Bars(sym,tf) < S10_RangeBars + S10_TrendEMA + 4) return;

   double K[], D[], ema[];
   if(!GetBuf(hS10_STO,0,4,K) || !GetBuf(hS10_STO,1,4,D)) return;
   if(!GetBuf(hS10_EMA,0,S10_SlopeBars+3,ema)) return;

   int loIdx=iLowest (sym,tf,MODE_LOW, S10_RangeBars,2);
   int hiIdx=iHighest(sym,tf,MODE_HIGH,S10_RangeBars,2);
   if(loIdx<0 || hiIdx<0) return;
   double sup=iLow(sym,tf,loIdx), res=iHigh(sym,tf,hiIdx);
   double range=res-sup; if(range<=0) return;
   double c1=iClose(sym,tf,1);
   bool up = (c1>ema[1] && ema[1]>ema[1+S10_SlopeBars]);
   bool dn = (c1<ema[1] && ema[1]<ema[1+S10_SlopeBars]);

   if(up && K[2]<S10_LevelLow && D[2]<S10_LevelLow && K[1]>=S10_LevelLow)
     {                                                     // oversold in an uptrend, %K back above 20
      double ask=SymbolInfoDouble(sym,SYMBOL_ASK);
      double tp1=sup + S10_TP1PctRange/100.0*range;
      if(tp1<=ask) return;
      double risk=tp1-ask;
      double sl=ask-risk;
      if(sl>=sup) return;                                  // SL must sit below the support, else invalid
      double tps[2]; tps[0]=tp1; tps[1]=ask+2.0*risk;
      OpenTrade(idx,sym,+1,sl,tps,modName[idx]);
     }
   else if(dn && K[2]>S10_LevelHigh && D[2]>S10_LevelHigh && K[1]<=S10_LevelHigh)
     {                                                     // overbought in a downtrend, %K back below 80
      double bid=SymbolInfoDouble(sym,SYMBOL_BID);
      double tp1=res - S10_TP1PctRange/100.0*range;
      if(tp1>=bid) return;
      double risk=bid-tp1;
      double sl=bid+risk;
      if(sl<=res) return;                                  // SL must sit above the resistance
      double tps[2]; tps[0]=tp1; tps[1]=bid-2.0*risk;
      OpenTrade(idx,sym,-1,sl,tps,modName[idx]);
     }
  }

//--- S11: The Pendulum - 10% bounce off range S/R, targets at 50%/90%
void Run_S11_Pendulum()
  {
   int idx=M_S11; string sym=modSym[idx]; ENUM_TIMEFRAMES tf=modTF[idx];
   if(!NewBar(idx)) return;
   double ps=PipSize(sym);

   if(s11Arm!=0 && (TimeCurrent()-s11ArmBar) > (long)PeriodSeconds(tf)*S11_RangeBars)
      s11Arm=0;                                            // bounce setup expired

   if(s11Arm==0)
     {
      if(Bars(sym,tf) < S11_RangeBars+4) return;
      int loIdx=iLowest (sym,tf,MODE_LOW, S11_RangeBars,2);
      int hiIdx=iHighest(sym,tf,MODE_HIGH,S11_RangeBars,2);
      if(loIdx<0 || hiIdx<0) return;
      double sup=iLow(sym,tf,loIdx), res=iHigh(sym,tf,hiIdx);
      double range=res-sup;
      if(range < S11_MinRangePips*ps) return;
      double tol=S11_TouchPct/100.0*range;
      double l1=iLow(sym,tf,1), h1=iHigh(sym,tf,1);
      if(l1 <= sup+tol)                                    // pendulum reached the support side
        { s11Arm=+1; s11Sup=sup; s11Res=res; s11Range=range; s11ArmBar=TimeCurrent(); }
      else if(h1 >= res-tol)                               // pendulum reached the resistance side
        { s11Arm=-1; s11Sup=sup; s11Res=res; s11Range=range; s11ArmBar=TimeCurrent(); }
      return;
     }

   if(ModuleBusy(idx)) return;
   double c1=iClose(sym,tf,1);

   if(s11Arm==+1)
     {
      if(c1 < s11Sup) { s11Arm=0; return; }                // support snapped: stand down
      if(c1 >= s11Sup + S11_EntryPct/100.0*s11Range)
        {
         double ask=SymbolInfoDouble(sym,SYMBOL_ASK);
         double tp1=s11Sup + S11_TP1Pct/100.0*s11Range;
         double tp2=s11Sup + S11_TP2Pct/100.0*s11Range;
         if(tp1<=ask) { s11Arm=0; return; }                // bounced too far already
         double risk=tp1-ask;
         double sl=ask-risk;
         double tps[2]; tps[0]=tp1; tps[1]=tp2;
         if(OpenTrade(idx,sym,+1,sl,tps,modName[idx])) s11Arm=0;
        }
     }
   else if(s11Arm==-1)
     {
      if(c1 > s11Res) { s11Arm=0; return; }                // resistance snapped
      if(c1 <= s11Res - S11_EntryPct/100.0*s11Range)
        {
         double bid=SymbolInfoDouble(sym,SYMBOL_BID);
         double tp1=s11Res - S11_TP1Pct/100.0*s11Range;
         double tp2=s11Res - S11_TP2Pct/100.0*s11Range;
         if(tp1>=bid) { s11Arm=0; return; }
         double risk=bid-tp1;
         double sl=bid+risk;
         double tps[2]; tps[0]=tp1; tps[1]=tp2;
         if(OpenTrade(idx,sym,-1,sl,tps,modName[idx])) s11Arm=0;
        }
     }
  }

//--- S12: Swap & Fly - carry direction + three-soldiers/crows trigger
void Run_S12_SwapFly()
  {
   int idx=M_S12; string sym=modSym[idx]; ENUM_TIMEFRAMES tf=modTF[idx];
   if(!NewBar(idx)) return;
   if(ModuleBusy(idx)) return;
   if(Bars(sym,tf) < S12_SwingBars+5) return;

   double o1=iOpen(sym,tf,1), c1=iClose(sym,tf,1);
   double o2=iOpen(sym,tf,2), c2=iClose(sym,tf,2);
   double o3=iOpen(sym,tf,3), c3=iClose(sym,tf,3);
   bool soldiers = (c1>o1 && c2>o2 && c3>o3 && c1>c2 && c2>c3);
   bool crows    = (c1<o1 && c2<o2 && c3<o3 && c1<c2 && c2<c3);
   double swapL=SymbolInfoDouble(sym,SYMBOL_SWAP_LONG);
   double swapS=SymbolInfoDouble(sym,SYMBOL_SWAP_SHORT);

   if(soldiers && (!S12_RequireSwap || swapL>0))
     {
      int k=iLowest(sym,tf,MODE_LOW,S12_SwingBars,1);
      if(k<0) return;
      double sl=iLow(sym,tf,k);
      double ask=SymbolInfoDouble(sym,SYMBOL_ASK);
      if(sl>=ask) return;
      double tp=S12_UseTP ? ask + S12_TP_RR*(ask-sl) : 0.0;
      OpenSingle(idx,sym,+1,sl,tp,modName[idx]);
     }
   else if(crows && (!S12_RequireSwap || swapS>0))
     {
      int k=iHighest(sym,tf,MODE_HIGH,S12_SwingBars,1);
      if(k<0) return;
      double sl=iHigh(sym,tf,k);
      double bid=SymbolInfoDouble(sym,SYMBOL_BID);
      if(sl<=bid) return;
      double tp=S12_UseTP ? bid - S12_TP_RR*(sl-bid) : 0.0;
      OpenSingle(idx,sym,-1,sl,tp,modName[idx]);
     }
  }

//--- shared engine for the two commodity-correlation modules
void RunCorrelation(const int idx, const string ref, const int lookback,
                    const int hATR, const double atrMult, const double rr,
                    const bool refBreakUpMeansBuy)
  {
   string sym=modSym[idx];
   if(!NewBar(idx)) return;
   if(ModuleBusy(idx)) return;
   if(Bars(ref,PERIOD_D1) < lookback+4) return;

   int loIdx=iLowest (ref,PERIOD_D1,MODE_LOW, lookback,2);
   int hiIdx=iHighest(ref,PERIOD_D1,MODE_HIGH,lookback,2);
   if(loIdx<0 || hiIdx<0) return;
   double sup=iLow(ref,PERIOD_D1,loIdx), res=iHigh(ref,PERIOD_D1,hiIdx);
   double rc1=iClose(ref,PERIOD_D1,1), rc2=iClose(ref,PERIOD_D1,2);
   if(rc1<=0 || rc2<=0) return;

   double atr[];
   if(!GetBuf(hATR,0,3,atr)) return;
   double slDist=atrMult*atr[1];
   if(slDist<=0) return;

   bool refUp = (rc1>res && rc2<=res);                     // reference chart broke out upward
   bool refDn = (rc1<sup && rc2>=sup);                     // reference chart broke down
   if(!refUp && !refDn) return;

   int dir = refUp ? (refBreakUpMeansBuy ? +1 : -1)
                   : (refBreakUpMeansBuy ? -1 : +1);
   if(dir>0)
     {
      double ask=SymbolInfoDouble(sym,SYMBOL_ASK);
      OpenSingle(idx,sym,+1, ask-slDist, ask+rr*slDist, modName[idx]);
     }
   else
     {
      double bid=SymbolInfoDouble(sym,SYMBOL_BID);
      OpenSingle(idx,sym,-1, bid+slDist, bid-rr*slDist, modName[idx]);
     }
  }

//--- S13a: oil breakout -> CADJPY in the same direction
void Run_S13a_OilCadjpy()
  {
   RunCorrelation(M_S13A, s13aRef, S13a_SRLookback, hS13a_ATR, S13a_ATRMult, S13a_RR, S13a_RefUpBuysTrade);
  }

//--- S13b: USD-index breakout -> gold in the opposite direction
void Run_S13b_DxyGold()
  {
   RunCorrelation(M_S13B, s13bRef, S13b_SRLookback, hS13b_ATR, S13b_ATRMult, S13b_RR, S13b_RefUpBuysTrade);
  }

//--- S15: Guppy Burst - straddle of the post-NY-close 3-hour range
void Run_S15_GuppyBurst()
  {
   int idx=M_S15; string sym=modSym[idx];
   if(!NewBar(idx)) return;
   MqlDateTime dt; TimeToStruct(iTime(sym,PERIOD_M5,0),dt);
   int endHour=(S15_WindowStart+S15_WindowHours)%24;
   if(dt.hour!=endHour || dt.min!=0) return;               // fire once, right as the window closes

   datetime day0=iTime(sym,PERIOD_D1,0);
   if(day0==s15DayPlaced) return;
   if(ModuleBusy(idx)) { s15DayPlaced=day0; return; }
   if(NewsBlocked(sym)) return;                            // blackout window: no straddle today

   int count=S15_WindowHours*12;
   if(Bars(sym,PERIOD_M5) < count+2) return;
   int hiIdx=iHighest(sym,PERIOD_M5,MODE_HIGH,count,1);
   int loIdx=iLowest (sym,PERIOD_M5,MODE_LOW, count,1);
   if(hiIdx<0 || loIdx<0) return;
   double hh=iHigh(sym,PERIOD_M5,hiIdx), ll=iLow(sym,PERIOD_M5,loIdx);
   double range=hh-ll, ps=PipSize(sym);
   if(range < S15_MinRangePips*ps) { s15DayPlaced=day0; return; }

   double lots=CalcLots(sym,range);
   if(lots<=0) { s15DayPlaced=day0; return; }
   double point=SymbolInfoDouble(sym,SYMBOL_POINT);
   double minDist=SymbolInfoInteger(sym,SYMBOL_TRADE_STOPS_LEVEL)*point;
   double ask=SymbolInfoDouble(sym,SYMBOL_ASK), bid=SymbolInfoDouble(sym,SYMBOL_BID);

   trade.SetExpertMagicNumber(MagicOf(idx));
   trade.SetDeviationInPoints(InpSlippagePoints);
   trade.SetTypeFillingBySymbol(sym);

   bool placed=false;
   if(hh-ask > minDist)                                    // buy stop at the window high
     {
      if(trade.BuyStop(lots, NP(sym,hh), sym, NP(sym,ll), NP(sym,hh+S15_RR*range),
                       ORDER_TIME_GTC, 0, modName[idx]))
         placed=true;
     }
   if(bid-ll > minDist)                                    // sell stop at the window low
     {
      if(trade.SellStop(lots, NP(sym,ll), sym, NP(sym,hh), NP(sym,ll-S15_RR*range),
                        ORDER_TIME_GTC, 0, modName[idx]))
         placed=true;
     }
   if(placed) s15DayPlaced=day0;
  }

//--- S16: English Breakfast Tea - London-open contrarian entry
void Run_S16_BreakfastTea()
  {
   int idx=M_S16; string sym=modSym[idx];
   if(!NewBar(idx)) return;
   datetime bt=iTime(sym,PERIOD_M15,0);
   MqlDateTime dt; TimeToStruct(bt,dt);
   if(dt.hour!=S16_EntryHour || dt.min!=S16_EntryMinute) return;

   datetime day0=iTime(sym,PERIOD_D1,0);
   if(day0==s16DayTraded) return;
   if(ModuleBusy(idx)) { s16DayTraded=day0; return; }

   MqlDateTime rt=dt; rt.hour=S16_RefHour; rt.min=S16_RefMinute; rt.sec=0;
   datetime refOpen=StructToTime(rt);
   int shift=iBarShift(sym,PERIOD_M15,refOpen,false);
   if(shift<1) return;
   double closeRef=iClose(sym,PERIOD_M15,shift);           // close of the early-morning candle
   double closeNow=iClose(sym,PERIOD_M15,1);               // close of the candle just before entry
   if(closeRef<=0 || closeNow<=0) return;

   double ps=PipSize(sym);
   s16DayTraded=day0;
   if(closeNow < closeRef)                                 // fell into the London open -> fade it long
     {
      double ask=SymbolInfoDouble(sym,SYMBOL_ASK);
      double tps[3];
      tps[0]=ask+1.0*S16_SL_Pips*ps; tps[1]=ask+2.0*S16_SL_Pips*ps; tps[2]=ask+3.0*S16_SL_Pips*ps;
      OpenTrade(idx,sym,+1, ask-S16_SL_Pips*ps, tps, modName[idx]);
     }
   else if(closeNow > closeRef)                            // rose into the London open -> fade it short
     {
      double bid=SymbolInfoDouble(sym,SYMBOL_BID);
      double tps[3];
      tps[0]=bid-1.0*S16_SL_Pips*ps; tps[1]=bid-2.0*S16_SL_Pips*ps; tps[2]=bid-3.0*S16_SL_Pips*ps;
      OpenTrade(idx,sym,-1, bid+S16_SL_Pips*ps, tps, modName[idx]);
     }
  }

//--- S17: Good Morning Asia - ride the prior daily candle at the NY close
void Run_S17_GoodMorningAsia()
  {
   int idx=M_S17; string sym=modSym[idx];
   if(!NewBar(idx)) return;                                // fires at the daily open (5pm NY servers)
   if(ModuleBusy(idx)) return;

   double o1=iOpen(sym,PERIOD_D1,1), c1=iClose(sym,PERIOD_D1,1);
   double h1=iHigh(sym,PERIOD_D1,1), l1=iLow(sym,PERIOD_D1,1);
   if(o1<=0 || c1<=0) return;
   double ps=PipSize(sym);

   if(c1>o1)                                               // bull day -> long into Asia
     {
      double ask=SymbolInfoDouble(sym,SYMBOL_ASK);
      double slDist=MathMax(ask-l1, S17_MinSL_Pips*ps);
      OpenSingle(idx,sym,+1, ask-slDist, ask+slDist/2.0, modName[idx]);
     }
   else if(c1<o1)                                          // bear day -> short into Asia
     {
      double bid=SymbolInfoDouble(sym,SYMBOL_BID);
      double slDist=MathMax(h1-bid, S17_MinSL_Pips*ps);
      OpenSingle(idx,sym,-1, bid+slDist, bid-slDist/2.0, modName[idx]);
     }
  }

//============================= NEWS ENGINE =========================
// Shared by S5 Gawk the Talk, S6 Balk the Talk and S14 Siamese Twins.
// Uses the terminal's built-in economic calendar (live/demo only).

bool AlreadyDone(const ulong id)
  {
   for(int i=ArraySize(gDoneValues)-1;i>=0;i--)
      if(gDoneValues[i]==id) return true;
   return false;
  }

void MarkDone(const ulong id)
  {
   int n=ArraySize(gDoneValues);
   if(n>=512) { ArrayRemove(gDoneValues,0,256); n=ArraySize(gDoneValues); }
   ArrayResize(gDoneValues,n+1);
   gDoneValues[n]=id;
  }

//--- refresh the cached release schedule (at most every 5 minutes)
void RefreshNewsCache()
  {
   if(!NF_Enable || gIsTester) return;
   datetime now=TimeCurrent();
   if(nfLastRefresh>0 && now-nfLastRefresh<300) return;
   nfLastRefresh=now;

   ArrayResize(nfEvtTime,0);
   ArrayResize(nfEvtCurr,0);
   MqlCalendarValue vals[];
   datetime from=now - NF_MinutesAfter*60 - 3600;
   datetime to  =now + NF_MinutesBefore*60 + 6*3600;      // few hours of look-ahead
   if(!CalendarValueHistory(vals,from,to)) return;

   ENUM_CALENDAR_EVENT_IMPORTANCE minImp =
      NF_HighImpactOnly ? CALENDAR_IMPORTANCE_HIGH : CALENDAR_IMPORTANCE_MODERATE;
   int total=ArraySize(vals);
   for(int i=0;i<total && ArraySize(nfEvtTime)<300;i++)
     {
      MqlCalendarEvent ev;
      if(!CalendarEventById(vals[i].event_id,ev)) continue;
      if(ev.type==CALENDAR_TYPE_HOLIDAY) continue;
      if(ev.importance<minImp) continue;
      MqlCalendarCountry cn;
      if(!CalendarCountryById(ev.country_id,cn)) continue;
      int n=ArraySize(nfEvtTime);
      ArrayResize(nfEvtTime,n+1);
      ArrayResize(nfEvtCurr,n+1);
      nfEvtTime[n]=vals[i].time;
      nfEvtCurr[n]=cn.currency;
     }
  }

//--- is 'now' inside a blackout window for this symbol's currencies?
bool NewsBlocked(const string sym)
  {
   if(!NF_Enable || gIsTester) return false;
   string b=SymbolInfoString(sym,SYMBOL_CURRENCY_BASE);
   string q=SymbolInfoString(sym,SYMBOL_CURRENCY_PROFIT);
   datetime now=TimeCurrent();
   for(int i=ArraySize(nfEvtTime)-1;i>=0;i--)
     {
      if(nfEvtCurr[i]!=b && nfEvtCurr[i]!=q) continue;
      if(now >= nfEvtTime[i]-NF_MinutesBefore*60 &&
         now <= nfEvtTime[i]+NF_MinutesAfter*60)
         return true;
     }
   return false;
  }

//--- map an affected currency to the pair and direction to trade
bool MapCurrencyTrade(const string ccy, const bool strong, string &sym, int &dir)
  {
   string base=""; bool ccyIsBase=true;
   if     (ccy=="EUR") base="EURUSD";
   else if(ccy=="GBP") base="GBPUSD";
   else if(ccy=="AUD") base="AUDUSD";
   else if(ccy=="NZD") base="NZDUSD";
   else if(ccy=="JPY") { base="USDJPY"; ccyIsBase=false; }
   else if(ccy=="CHF") { base="USDCHF"; ccyIsBase=false; }
   else if(ccy=="CAD") { base="USDCAD"; ccyIsBase=false; }
   else if(ccy=="USD")
     {
      sym=ResolveSymbol(News_USDPair);
      if(sym=="") return false;
      bool usdBase=(StringFind(sym,"USD")==0);             // e.g. USDJPY vs EURUSD
      dir=(strong==usdBase) ? +1 : -1;
      return true;
     }
   else return false;

   sym=ResolveSymbol(base);
   if(sym=="") return false;
   dir = ccyIsBase ? (strong ? +1 : -1) : (strong ? -1 : +1);
   return true;
  }

//--- Siamese Twins: China surprise -> AUDUSD, SL at previous D1 extreme
void FireSiamese(const bool strong, const string evName)
  {
   int idx=M_S14;
   if(!modEnabled[idx] || ModuleBusy(idx)) return;
   string sym=modSym[idx];
   if(strong)
     {
      double lo=iLow(sym,PERIOD_D1,1);
      double ask=SymbolInfoDouble(sym,SYMBOL_ASK);
      if(lo<=0 || lo>=ask) return;
      double risk=ask-lo;
      double tps[2]; tps[0]=ask+risk; tps[1]=ask+2.0*risk;
      if(OpenTrade(idx,sym,+1,lo,tps,modName[idx]))
         PrintFormat("[%s] long %s on China release: %s", modName[idx], sym, evName);
     }
   else
     {
      double hi=iHigh(sym,PERIOD_D1,1);
      double bid=SymbolInfoDouble(sym,SYMBOL_BID);
      if(hi<=0 || hi<=bid) return;
      double risk=hi-bid;
      double tps[2]; tps[0]=bid-risk; tps[1]=bid-2.0*risk;
      if(OpenTrade(idx,sym,-1,hi,tps,modName[idx]))
         PrintFormat("[%s] short %s on China release: %s", modName[idx], sym, evName);
     }
  }

//--- poll the economic calendar and act on qualifying surprises
void PollCalendar()
  {
   MqlCalendarValue vals[];
   ulong prev=gCalChangeId;
   int n=CalendarValueLast(gCalChangeId, vals);
   if(prev==0) return;                                     // first call only primes the change id
   if(n<=0) return;

   datetime now=TimeCurrent();
   for(int i=0;i<n;i++)
     {
      MqlCalendarValue v=vals[i];
      if(AlreadyDone(v.id)) continue;
      if(v.actual_value==LONG_MIN) continue;               // release not out yet
      if(v.time>now || (now-v.time)>News_MaxAgeSec)        // stale or scheduled ahead
        { MarkDone(v.id); continue; }

      MqlCalendarEvent ev;
      if(!CalendarEventById(v.event_id, ev)) { MarkDone(v.id); continue; }
      if(ev.type==CALENDAR_TYPE_HOLIDAY)     { MarkDone(v.id); continue; }
      if(News_HighImpactOnly && ev.importance!=CALENDAR_IMPORTANCE_HIGH)
        { MarkDone(v.id); continue; }
      if(v.forecast_value==LONG_MIN)         { MarkDone(v.id); continue; }

      MqlCalendarCountry cn;
      if(!CalendarCountryById(ev.country_id, cn)) { MarkDone(v.id); continue; }

      double actual  =v.actual_value  /1000000.0;
      double forecast=v.forecast_value/1000000.0;
      double dev=actual-forecast;
      string nameU=ev.name; StringToUpper(nameU);

      bool qualifies=false;                                // Rule of 20 + its two special cases
      if(StringFind(nameU,"PMI")>=0)
         qualifies=(MathAbs(dev)>=News_PMI_MinAbs);
      else if(StringFind(nameU,"INTEREST RATE")>=0 || StringFind(nameU,"RATE DECISION")>=0 ||
              StringFind(nameU,"CASH RATE")>=0     || StringFind(nameU,"REFINANCING RATE")>=0)
         qualifies=(MathAbs(dev)>=News_IR_MinAbs);
      else
        {
         if(forecast==0.0) { MarkDone(v.id); continue; }
         qualifies=(MathAbs(dev)/MathAbs(forecast)*100.0 >= News_MinDeviationPct);
        }
      MarkDone(v.id);
      if(!qualifies) continue;

      bool strong=(dev>0);                                 // bigger-than-forecast = currency positive...
      if(StringFind(nameU,"UNEMPLOYMENT")>=0 || StringFind(nameU,"JOBLESS")>=0 ||
         StringFind(nameU,"CLAIMS")>=0)
         strong=!strong;                                   // ...except inverse indicators

      if(cn.code==S14_CountryCode)                         // China feeds Siamese Twins
        {
         FireSiamese(strong, ev.name);
         continue;
        }

      int idx = strong ? M_S5 : M_S6;                      // Gawk on strength, Balk on weakness
      if(!modEnabled[idx] || ModuleBusy(idx)) continue;

      string sym=""; int dir=0;
      if(!MapCurrencyTrade(cn.currency, strong, sym, dir)) continue;
      double ps=PipSize(sym);
      if(dir>0)
        {
         double ask=SymbolInfoDouble(sym,SYMBOL_ASK);
         if(OpenSingle(idx,sym,+1, ask-News_SL_Pips*ps, ask+News_TP_Pips*ps,
                       modName[idx]))
            PrintFormat("[%s] long %s on %s %s (act %.2f / fc %.2f)",
                        modName[idx], sym, cn.currency, ev.name, actual, forecast);
        }
      else
        {
         double bid=SymbolInfoDouble(sym,SYMBOL_BID);
         if(OpenSingle(idx,sym,-1, bid+News_SL_Pips*ps, bid-News_TP_Pips*ps,
                       modName[idx]))
            PrintFormat("[%s] short %s on %s %s (act %.2f / fc %.2f)",
                        modName[idx], sym, cn.currency, ev.name, actual, forecast);
        }
     }
  }

//============================= PANEL ===============================
string TFName(const ENUM_TIMEFRAMES tf)
  {
   string s=EnumToString(tf);
   StringReplace(s,"PERIOD_","");
   return s;
  }

void UpdatePanel()
  {
   if(!InpShowPanel) return;
   static datetime last=0;
   datetime now=TimeCurrent();
   if(now==last) return;
   last=now;

   string s="Seventeen Strategies EA v1.11  |  "+(gHedging?"hedging":"NETTING")+"  |  ";
   s+=(InpRiskMode==RISK_PERCENT ? StringFormat("risk %.2f%%",InpRiskPercent)
                                 : StringFormat("fixed %.2f lots",InpFixedLots));
   if(NF_Enable && !gIsTester) s+="  |  news filter ON";
   s+="\n------------------------------------------------------------\n";
   int active=0;
   for(int i=0;i<M_TOTAL;i++)
     {
      if(!modEnabled[i]) continue;
      active++;
      ulong m=MagicOf(i);
      s+=StringFormat("%-16s %-10s %-4s  pos:%d ord:%d\n",
                      modName[i], modSym[i], TFName(modTF[i]),
                      PositionsByMagic(m), OrdersByMagic(m));
     }
   if(active==0) s+="(no modules enabled - switch them on in the inputs)\n";
   Comment(s);
  }
//+------------------------------------------------------------------+
