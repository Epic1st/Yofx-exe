//+------------------------------------------------------------------+
//| Engine.mqh                                                        |
//| Multi-Engine Pattern Processing and Trade Execution              |
//| Standalone MQL4 module                                           |
//+------------------------------------------------------------------+

#ifndef ENGINE_MQH
#define ENGINE_MQH

#property strict

#include "PatternTypes.mqh"

//+------------------------------------------------------------------+
//| Engine configuration                                             |
//+------------------------------------------------------------------+

#ifndef ENGINE_COUNT
#define ENGINE_COUNT 10
#endif

#ifndef MAX_PATTERNS
#define MAX_PATTERNS 65
#endif

#ifndef DBL_MAX
#define DBL_MAX 1.7976931348623158e+308
#endif

#ifndef PATTERNDATA_TYPE_DEFINED
#define PATTERNDATA_TYPE_DEFINED

struct PatternData
{
   bool   active;
   int    waveCount;
   int    directionSignature;
   float  fibs[16];
   float  durs[16];
   int    merrillCode;
   int    groupType;
   string name;
};

#endif // PATTERNDATA_TYPE_DEFINED

//+------------------------------------------------------------------+
//| Standalone Engine Compatibility Layer                            |
//|                                                                  |
//| Engine.mqh is designed to compile both:                         |
//|                                                                  |
//| 1. As part of the main EA                                      |
//|    IS_EA is defined and all EA-owned globals are supplied by    |
//|    the main compilation unit / other modules.                   |
//|                                                                  |
//| 2. As a standalone module                                      |
//|    IS_EA is not defined and the minimum external state required |
//|    by CEngine is supplied locally.                              |
//|                                                                  |
//| Native MQL4 functions are NOT redefined here.                   |
//| Functions such as Print(), Sleep(), Symbol(), MarketInfo(),      |
//| NormalizeDouble(), MathFloor(), IntegerToString(),              |
//| GetLastError(), PeriodSeconds(), etc. remain native MQL4 APIs.  |
//+------------------------------------------------------------------+

#ifndef IS_EA

//+------------------------------------------------------------------+
//| Standalone configuration/state                                   |
//+------------------------------------------------------------------+

bool     g_CachedTradeTime             = true;
bool     g_CachedRollover              = false;

bool     UseMomentum                   = true;
double   MinATR                        = 0.0005;

int      g_CachedSpread                = 0;
double   g_CachedClose1                = 0.0;
double   g_CachedOpen1                 = 0.0;
double   g_CachedRecentHigh10          = 0.0;
double   g_CachedRecentLow10           = 0.0;

int      LookbackBars                  = 500;

double   FibTolerance                  = 0.25;
double   MinConfidence                 = 55.0;
double   DirMatchThreshold             = 0.65;

bool     UseMerrillRank                = true;
bool     DebugPrint                    = false;

int      MaximumSimultaneousOrders     = 50;
bool     AllowMultipleEngines          = true;

string   TradeComment                  = "MW";
int      Slippage                      = 30;
double   LotSize                       = 0.01;
int      MaxSpread                     = 120;


//+------------------------------------------------------------------+
//| Shared swing-cache compatibility                                 |
//|                                                                  |
//| SwingCache.mqh is normally responsible for these arrays.        |
//| When Engine.mqh is compiled by itself, provide the minimum       |
//| compatible storage required by CEngine.                          |
//+------------------------------------------------------------------+

#ifndef SWING_CACHE_MQH

#ifndef ENGINE_SHARED_SWINGS_DEFINED
#define ENGINE_SHARED_SWINGS_DEFINED

int g_SharedSwingCount2 = 0;
int g_SharedSwingCount3 = 0;
int g_SharedSwingCount4 = 0;

int g_SharedMergedCount2 = 0;
int g_SharedMergedCount3 = 0;
int g_SharedMergedCount4 = 0;

Swing g_SharedMergedSwings2[];
Swing g_SharedMergedSwings3[];
Swing g_SharedMergedSwings4[];

#endif // ENGINE_SHARED_SWINGS_DEFINED

#endif // SWING_CACHE_MQH


//+------------------------------------------------------------------+
//| Cached EMA compatibility                                         |
//+------------------------------------------------------------------+

double g_CachedFastEMA[ENGINE_COUNT];
double g_CachedSlowEMA[ENGINE_COUNT];


//+------------------------------------------------------------------+
//| Cached order state                                                |
//+------------------------------------------------------------------+

int g_CachedEAOpenOrders = 0;
int g_CachedEngineOrders[ENGINE_COUNT];


//+------------------------------------------------------------------+
//| Standalone pattern storage                                       |
//|                                                                  |
//| The real EA normally receives g_Patterns from                   |
//| PatternDatabase.mqh.                                             |
//+------------------------------------------------------------------+

PatternData g_Patterns[MAX_PATTERNS];
int        g_PatternCount = 0;


//+------------------------------------------------------------------+
//| Standalone order-cache helpers                                   |
//+------------------------------------------------------------------+

int CountEAOpenOrders()
{
   return g_CachedEAOpenOrders;
}


bool HasOtherEngineTrade(int magic, int unused)
{
   // Standalone parser/compatibility behavior.
   // No real terminal orders are available here.
   return false;
}


//+------------------------------------------------------------------+
//| Standalone native-trade constants                                |
//|                                                                  |
//| These are only supplied when compiling outside the EA.           |
//| They allow the CEngine source to remain syntactically complete.  |
//+------------------------------------------------------------------+

#ifndef OP_BUY
#define OP_BUY 0
#endif

#ifndef OP_SELL
#define OP_SELL 1
#endif

#ifndef MODE_LOTSTEP
#define MODE_LOTSTEP 23
#endif

#ifndef MODE_MINLOT
#define MODE_MINLOT 22
#endif

#ifndef MODE_MAXLOT
#define MODE_MAXLOT 21
#endif

#ifndef MODE_DIGITS
#define MODE_DIGITS 17
#endif

#ifndef ERR_REQUOTE
#define ERR_REQUOTE 138
#endif

#ifndef ERR_OFF_QUOTES
#define ERR_OFF_QUOTES 136
#endif

#ifndef ERR_PRICE_CHANGED
#define ERR_PRICE_CHANGED 135
#endif


//+------------------------------------------------------------------+
//| Standalone market/trade placeholders                             |
//|                                                                  |
//| These are intentionally minimal. They provide deterministic      |
//| parser/runtime-safe values without attempting to emulate MT4.    |
//+------------------------------------------------------------------+

double Ask = 0.0;
double Bid = 0.0;

int Digits = 0;


//+------------------------------------------------------------------+
//| Standalone market-data functions                                 |
//+------------------------------------------------------------------+

int iBars(string symbol, int timeframe)
{
   return 0;
}


double iHigh(string symbol, int timeframe, int shift)
{
   return 0.0;
}


double iLow(string symbol, int timeframe, int shift)
{
   return 0.0;
}


//+------------------------------------------------------------------+
//| Standalone market information                                    |
//+------------------------------------------------------------------+

double MarketInfo(string symbolName, int mode)
{
   if(mode == MODE_LOTSTEP)
      return 0.01;

   if(mode == MODE_MINLOT)
      return 0.01;

   if(mode == MODE_MAXLOT)
      return 100.0;

   if(mode == MODE_DIGITS)
      return 2.0;

   return 0.0;
}


//+------------------------------------------------------------------+
//| Standalone trade-context functions                               |
//+------------------------------------------------------------------+

void RefreshRates()
{
}


bool IsTradeContextBusy()
{
   return false;
}


//+------------------------------------------------------------------+
//| Standalone OrderSend                                             |
//+------------------------------------------------------------------+

int OrderSend(
   string sym,
   int cmd,
   double vol,
   double price,
   int slip,
   double sl,
   double tp,
   string cmt,
   int magic,
   int expiry,
   int clr
)
{
   // No terminal trade execution is possible in standalone mode.
   return -1;
}

#endif // IS_EA

//+------------------------------------------------------------------+
//| Engine Class                                                     |
//+------------------------------------------------------------------+

class CEngine
{
private:

   int    m_magic;
   int    m_engineNumber;
   int    m_swingStrength;
   int    m_minWaveBars;
   double m_minWaveAtrFactor;
   int    m_fastEMA;
   int    m_slowEMA;
   double m_minADX;
   int    m_maxTrades;
   int    m_cooldownBars;

   datetime m_lastTradeTime;

   Swing m_swings[];
   Swing m_mergedSwings[];

   int m_mergedSwingCount;

   Wave m_waves[];

   int m_swingCount;
   int m_waveCount;

   int  m_merrillRankCache[];
   bool m_merrillRankCached[];
   int  m_merrillCacheSize;

   int m_directionSignature3[];
   int m_directionSignature4[];
   int m_directionSignature6[];

public:

   CEngine()
   {
      ArrayResize(m_swings, 0);
      ArrayResize(m_mergedSwings, 0);
      ArrayResize(m_waves, 0);

      m_swingCount = 0;
      m_mergedSwingCount = 0;
      m_waveCount = 0;
      m_lastTradeTime = 0;

      m_merrillCacheSize = 0;

      ArrayResize(m_merrillRankCache, 0);
      ArrayResize(m_merrillRankCached, 0);

      ArrayResize(m_directionSignature3, 0);
      ArrayResize(m_directionSignature4, 0);
      ArrayResize(m_directionSignature6, 0);
   }

   void Init(int engineIndex, int magic, int swingStr, int minWaveBars, double minWaveAtr, 
             int fastEMA, int slowEMA, double minADX, 
             int maxTrades, int cooldownBars) 
   {
      m_engineNumber = engineIndex;
      m_magic = magic;
      m_swingStrength = swingStr;
      m_minWaveBars = minWaveBars;
      m_minWaveAtrFactor = minWaveAtr;
      m_fastEMA = fastEMA;
      m_slowEMA = slowEMA;
      m_minADX = minADX;
      m_maxTrades = maxTrades;
      m_cooldownBars = cooldownBars;
   }

   void Process(double globalATR, double globalADX, datetime currentTime)
   {
      DetectSwings();
      BuildWaves(globalATR);

      if(m_waveCount < 3)
         return;

      if(m_lastTradeTime > 0)
      {
         if(currentTime - m_lastTradeTime < m_cooldownBars * PeriodSeconds())
            return;
      }

      if(!g_CachedTradeTime)
         return;

      if(g_CachedRollover)
         return;

      if(globalATR < MinATR)
         return;

      if(g_CachedSpread > MaxSpread)
         return;

      FindAndTrade(globalATR, globalADX, currentTime);
   }

private:
   //+------------------------------------------------------------------+
   //| Detect swings - shared caches for strengths 2/3/4                |
   //| Custom fallback retained for any other strength                 |
   //+------------------------------------------------------------------+
   void DetectSwings()
   {
      if(m_swingStrength == 2)
      {
         m_swingCount = g_SharedSwingCount2;
         return;
      }

      if(m_swingStrength == 3)
      {
         m_swingCount = g_SharedSwingCount3;
         return;
      }

      if(m_swingStrength == 4)
      {
         m_swingCount = g_SharedSwingCount4;
         return;
      }

      // Custom-strength fallback.
      m_swingCount = 0;

      int bars = iBars(_Symbol, _Period);
      int requiredSize = MathMin(bars - 1, LookbackBars);

      if(requiredSize <= m_swingStrength * 2)
         return;

      if(ArraySize(m_swings) < requiredSize)
         ArrayResize(m_swings, requiredSize);

      int limit = requiredSize;

      for(int i = m_swingStrength; i < limit - m_swingStrength; i++)
      {
         double h = iHigh(_Symbol, _Period, i);
         double l = iLow(_Symbol, _Period, i);

         bool isHigh = true;
         bool isLow  = true;

         for(int j = 1; j <= m_swingStrength; j++)
         {
            if(h <= iHigh(_Symbol, _Period, i + j) ||
               h <= iHigh(_Symbol, _Period, i - j))
            {
               isHigh = false;
            }

            if(l >= iLow(_Symbol, _Period, i + j) ||
               l >= iLow(_Symbol, _Period, i - j))
            {
               isLow = false;
            }

            if(!isHigh && !isLow)
               break;
         }

         if(isHigh)
         {
            m_swings[m_swingCount].bar   = i;
            m_swings[m_swingCount].price = h;
            m_swings[m_swingCount].type  = 1;
            m_swingCount++;
         }
         else if(isLow)
         {
            m_swings[m_swingCount].bar   = i;
            m_swings[m_swingCount].price = l;
            m_swings[m_swingCount].type  = -1;
            m_swingCount++;
         }
      }
   }

   bool GetMergedSwing(
      int index,
      int &bar,
      double &price,
      char &type
   )
   {
      if(index < 0 || index >= m_mergedSwingCount)
         return false;

      if(m_swingStrength == 2)
      {
         if(index >= g_SharedMergedCount2) return false;
         bar   = g_SharedMergedSwings2[index].bar;
         price = g_SharedMergedSwings2[index].price;
         type  = g_SharedMergedSwings2[index].type;
         return true;
      }

      if(m_swingStrength == 3)
      {
         if(index >= g_SharedMergedCount3) return false;
         bar   = g_SharedMergedSwings3[index].bar;
         price = g_SharedMergedSwings3[index].price;
         type  = g_SharedMergedSwings3[index].type;
         return true;
      }

      if(m_swingStrength == 4)
      {
         if(index >= g_SharedMergedCount4) return false;
         bar   = g_SharedMergedSwings4[index].bar;
         price = g_SharedMergedSwings4[index].price;
         type  = g_SharedMergedSwings4[index].type;
         return true;
      }

      bar   = m_mergedSwings[index].bar;
      price = m_mergedSwings[index].price;
      type  = m_mergedSwings[index].type;

      return true;
   }

   void BuildDirectionSignatureCaches()
   {
      int count = m_waveCount;

      if(count < 1)
      {
         ArrayResize(m_directionSignature3, 0);
         ArrayResize(m_directionSignature4, 0);
         ArrayResize(m_directionSignature6, 0);
         return;
      }

      ArrayResize(m_directionSignature3, count);
      ArrayResize(m_directionSignature4, count);
      ArrayResize(m_directionSignature6, count);

      int sig3 = 0;
      int sig4 = 0;
      int sig6 = 0;

      for(int i = 0; i < count; i++)
      {
         int bit = (m_waves[i].direction > 0) ? 1 : 0;

         sig3 = ((sig3 << 1) | bit) & 7;
         sig4 = ((sig4 << 1) | bit) & 15;
         sig6 = ((sig6 << 1) | bit) & 63;

         m_directionSignature3[i] = 0;
         m_directionSignature4[i] = 0;
         m_directionSignature6[i] = 0;

         if(i >= 2) m_directionSignature3[i - 2] = sig3;
         if(i >= 3) m_directionSignature4[i - 3] = sig4;
         if(i >= 5) m_directionSignature6[i - 5] = sig6;
      }
   }

   //+------------------------------------------------------------------+
   //| Build waves from merged swings                                  |
   //| Invalidates Merrill cache when waves are rebuilt               |
   //+------------------------------------------------------------------+
   void BuildWaves(double atr)
   {
      int sourceCount = m_swingCount;

      if(m_swingStrength == 2)
      {
         m_mergedSwingCount = g_SharedMergedCount2;
      }
      else if(m_swingStrength == 3)
      {
         m_mergedSwingCount = g_SharedMergedCount3;
      }
      else if(m_swingStrength == 4)
      {
         m_mergedSwingCount = g_SharedMergedCount4;
      }
      else
      {
         if(sourceCount < 2)
         {
            m_mergedSwingCount = 0;
            m_waveCount = 0;
            m_merrillCacheSize = 0;
            ArrayResize(m_merrillRankCache, 0);
            ArrayResize(m_merrillRankCached, 0);
            ArrayResize(m_directionSignature3, 0);
            ArrayResize(m_directionSignature4, 0);
            ArrayResize(m_directionSignature6, 0);
            return;
         }

         m_mergedSwingCount = 0;

         if(ArraySize(m_mergedSwings) < sourceCount)
            ArrayResize(m_mergedSwings, sourceCount);

         int i = 0;

         while(i < sourceCount)
         {
            Swing current = m_swings[i];

            while(i + 1 < sourceCount)
            {
               Swing next = m_swings[i + 1];

               if(next.type != current.type)
                  break;

               if(current.type == 1)
               {
                  if(next.price > current.price)
                     current = next;
               }
               else
               {
                  if(next.price < current.price)
                     current = next;
               }

               i++;
            }

            m_mergedSwings[m_mergedSwingCount] = current;
            m_mergedSwingCount++;
            i++;
         }
      }

      if(m_mergedSwingCount < 2)
      {
         m_waveCount = 0;
         m_merrillCacheSize = 0;
         ArrayResize(m_merrillRankCache, 0);
         ArrayResize(m_merrillRankCached, 0);
         ArrayResize(m_directionSignature3, 0);
         ArrayResize(m_directionSignature4, 0);
         ArrayResize(m_directionSignature6, 0);
         return;
      }

      int requiredWaves = m_mergedSwingCount - 1;

      if(ArraySize(m_waves) < requiredWaves)
         ArrayResize(m_waves, requiredWaves);

      m_waveCount = 0;

      for(int k = requiredWaves - 1; k >= 0; k--)
      {
         int idx1 = k + 1;
         int idx2 = k;

         int bar1, bar2;
         double price1, price2;
         char type1, type2;

         if(!GetMergedSwing(idx1, bar1, price1, type1))
            continue;

         if(!GetMergedSwing(idx2, bar2, price2, type2))
            continue;

         int dur = (int)MathAbs(bar2 - bar1);

         if(dur < m_minWaveBars)
            continue;

         double amp = MathAbs(price2 - price1);

         if(amp < atr * m_minWaveAtrFactor)
            continue;

         m_waves[m_waveCount].amplitude = amp;
         m_waves[m_waveCount].duration  = dur;
         m_waves[m_waveCount].direction = price2 > price1 ? 1 : -1;
         m_waves[m_waveCount].startPrice = price1;
         m_waves[m_waveCount].endPrice   = price2;
         m_waves[m_waveCount].startBar = bar1;
         m_waves[m_waveCount].endBar   = bar2;

         m_waveCount++;
      }

      BuildDirectionSignatureCaches();

      // Invalidate Merrill cache - waves have changed
      m_merrillCacheSize = 0;
      ArrayResize(m_merrillRankCache, 0);
      ArrayResize(m_merrillRankCached, 0);
   }

   int GetMerrillRank(int startWave)
   {
      int baseIdx = m_mergedSwingCount - 1 - startWave;

      if(baseIdx - 4 < 0)
         return 0;

      double p[5] = {0, 0, 0, 0, 0};

      for(int i = 0; i < 5; i++)
      {
         int bar;
         char type;

         if(!GetMergedSwing(baseIdx - i, bar, p[i], type))
            return 0;
      }

      int rank[5] = {0, 0, 0, 0, 0};
      bool used[5] = {false, false, false, false, false};

      for(int r = 1; r <= 5; r++)
      {
         int bestIdx = -1;
         double bestPrice = -DBL_MAX;

         for(int i = 0; i < 5; i++)
         {
            if(!used[i] && p[i] > bestPrice)
            {
               bestPrice = p[i];
               bestIdx = i;
            }
         }

         if(bestIdx >= 0)
         {
            rank[bestIdx] = r;
            used[bestIdx] = true;
         }
      }

      return rank[0] * 10000 + rank[1] * 1000 + rank[2] * 100 + rank[3] * 10 + rank[4];
   }

   //+------------------------------------------------------------------+
   //| Find matching patterns and execute trades                       |
   //| Merrill cache is lazily populated - no clearing loop needed     |
   //+------------------------------------------------------------------+
   void FindAndTrade(double atr, double adx, datetime currentTime) {
      // Lazy cache growth - ArrayResize zero-initializes new bool entries to false
      if(m_merrillCacheSize < m_waveCount)
      {
         ArrayResize(m_merrillRankCache, m_waveCount);
         ArrayResize(m_merrillRankCached, m_waveCount);
         m_merrillCacheSize = m_waveCount;
      }

      int bestBuyIdx = -1, bestSellIdx = -1;
      int bestBuyConf = 0, bestSellConf = 0;
      int buyStartWave = -1, sellStartWave = -1;
      int buyMerrill = 0, sellMerrill = 0;
      
      double dynConf = MinConfidence;
      if(adx < m_minADX) dynConf -= 5.0; 
      if(dynConf < 40.0) dynConf = 40.0;
      
      for(int p = 0; p < g_PatternCount; p++) {
         if(!g_Patterns[p].active) continue;
         int wc = g_Patterns[p].waveCount;
         if(m_waveCount < wc) continue;
         
         int searchDepth = 25;
         if(searchDepth > m_waveCount - wc)
            searchDepth = m_waveCount - wc;

         int required = (int)(wc * DirMatchThreshold);
         if(required < 2) required = 2;

         int firstStart = m_waveCount - wc;
         int lastStart  = m_waveCount - searchDepth - wc;

         for(int s = firstStart; s >= lastStart; s--)
         {
            if(s < 0) break;

            int directionSignature = 0;

            if(wc == 3)
               directionSignature = m_directionSignature3[s];
            else if(wc == 4)
               directionSignature = m_directionSignature4[s];
            else if(wc == 6)
               directionSignature = m_directionSignature6[s];
            else
            {
               for(int di = 0; di < wc; di++)
               {
                  directionSignature <<= 1;
                  if(m_waves[s+di].direction > 0)
                     directionSignature |= 1;
               }
            }

            int directionXor = directionSignature ^ g_Patterns[p].directionSignature;
            int mismatches = 0;

            while(directionXor > 0)
            {
               directionXor &= (directionXor - 1);
               mismatches++;
            }

            int match = wc - mismatches;
            if(match < required) continue;

            double baseAmp = m_waves[s].amplitude;
            double baseDur = (double)m_waves[s].duration;

            int fibMatch = 0;
            int durMatch = 0;

            for(int i = 0; i < wc; i++)
            {
               float expF = g_Patterns[p].fibs[i];
               if(expF > 0.0)
               {
                  double expectedAmp = baseAmp * (double)expF;
                  double fibLow  = expectedAmp * (1.0 - FibTolerance);
                  double fibHigh = expectedAmp * (1.0 + FibTolerance);
                  double actualAmp = m_waves[s+i].amplitude;
                  if(actualAmp >= fibLow && actualAmp <= fibHigh)
                     fibMatch++;
               }

               float expD = g_Patterns[p].durs[i];
               if(expD > 0.0)
               {
                  double expectedDur = baseDur * (double)expD;
                  double durLow  = expectedDur * 0.4;
                  double durHigh = expectedDur * 1.6;
                  double actualDur = (double)m_waves[s+i].duration;
                  if(actualDur >= durLow && actualDur <= durHigh)
                     durMatch++;
               }
            }
            
            int merrillMatch = 0;
            int merrillRank = 0;

            bool patternUsesMerrill = UseMerrillRank && g_Patterns[p].merrillCode > 0 && wc >= 5;

            if(patternUsesMerrill)
            {
               if(s >= 0 && s < m_merrillCacheSize)
               {
                  if(!m_merrillRankCached[s])
                  {
                     m_merrillRankCache[s] = GetMerrillRank(s);
                     m_merrillRankCached[s] = true;
                  }
                  merrillRank = m_merrillRankCache[s];
               }

               if(merrillRank == g_Patterns[p].merrillCode)
                  merrillMatch = 1;
            }
            
            int conf;

            if(patternUsesMerrill)
               conf = (int)(fibMatch * 25.0 / wc + match * 25.0 / wc + durMatch * 15.0 / wc + merrillMatch * 35.0);
            else
               conf = (int)(fibMatch * 40.0 / wc + match * 40.0 / wc + durMatch * 20.0 / wc);
            
            bool isSell = g_Patterns[p].groupType >= 9;
            
            if(isSell && conf > bestSellConf) {
               bestSellConf = conf; bestSellIdx = p; sellStartWave = s;
               sellMerrill = merrillRank;
            }
            else if(!isSell && conf > bestBuyConf) {
               bestBuyConf = conf; bestBuyIdx = p; buyStartWave = s;
               buyMerrill = merrillRank;
            }
         }
      }
      
      if(DebugPrint) {
         if(bestBuyIdx >= 0 && bestBuyConf >= (int)dynConf) {
            Print("Engine ", m_engineNumber, " BUY Pattern: ", g_Patterns[bestBuyIdx].name, 
                  " Conf: ", bestBuyConf, "%");
            if(g_Patterns[bestBuyIdx].merrillCode > 0) {
               Print("  Merrill Rank: ", buyMerrill, " (Expected: ", g_Patterns[bestBuyIdx].merrillCode, ")",
                     buyMerrill == g_Patterns[bestBuyIdx].merrillCode ? " MATCH" : " NO-MATCH");
            }
         }
         
         if(bestSellIdx >= 0 && bestSellConf >= (int)dynConf) {
            Print("Engine ", m_engineNumber, " SELL Pattern: ", g_Patterns[bestSellIdx].name, 
                  " Conf: ", bestSellConf, "%");
            if(g_Patterns[bestSellIdx].merrillCode > 0) {
               Print("  Merrill Rank: ", sellMerrill, " (Expected: ", g_Patterns[bestSellIdx].merrillCode, ")",
                     sellMerrill == g_Patterns[bestSellIdx].merrillCode ? " MATCH" : " NO-MATCH");
            }
         }
      }
      
      bool buyOk = (bestBuyIdx >= 0 && bestBuyConf >= (int)dynConf);
      bool sellOk = (bestSellIdx >= 0 && bestSellConf >= (int)dynConf);

      if(buyOk) {
         if(!CheckBOS(true, bestBuyIdx, buyStartWave)) buyOk = false;
         if(UseMomentum && !CheckMomentum(true)) buyOk = false;
         if(!CheckTrend(true)) buyOk = false;
      }
      
      if(sellOk) {
         if(!CheckBOS(false, bestSellIdx, sellStartWave)) sellOk = false;
         if(UseMomentum && !CheckMomentum(false)) sellOk = false;
         if(!CheckTrend(false)) sellOk = false;
      }

      if(buyOk && sellOk) {
         if(bestBuyConf >= bestSellConf) sellOk = false;
         else buyOk = false;
      }
      
      if(CountEAOpenOrders() >= MaximumSimultaneousOrders)
         return;

      if(!AllowMultipleEngines && HasOtherEngineTrade(m_magic, 0))
         return;
      
      if(buyOk && CanTrade()) {
         m_lastTradeTime = currentTime;
         ExecBuy(); 
      }
      else if(sellOk && CanTrade()) {
         m_lastTradeTime = currentTime;
         ExecSell();
      }
   }

   bool CheckBOS(bool isBuy, int patIdx, int startWave)
   {
      double pH = 0.0;
      double pL = DBL_MAX;

      int wc = g_Patterns[patIdx].waveCount;
      int endWave = startWave + wc;

      if(endWave > m_waveCount)
         endWave = m_waveCount;

      for(int i = startWave; i < endWave; i++)
      {
         double maxPrice = m_waves[i].startPrice;
         if(m_waves[i].endPrice > maxPrice)
            maxPrice = m_waves[i].endPrice;
         if(maxPrice > pH)
            pH = maxPrice;

         double minPrice = m_waves[i].startPrice;
         if(m_waves[i].endPrice < minPrice)
            minPrice = m_waves[i].endPrice;
         if(minPrice < pL)
            pL = minPrice;
      }

      if(isBuy)
      {
         if(g_CachedClose1 > pH)
            return true;
         if(g_CachedClose1 > g_CachedRecentHigh10)
            return true;
      }
      else
      {
         if(g_CachedClose1 < pL)
            return true;
         if(g_CachedClose1 < g_CachedRecentLow10)
            return true;
      }

      return false;
   }

   //+------------------------------------------------------------------+
   //| Trend check - uses cached EMA values from MarketCache           |
   //+------------------------------------------------------------------+
   bool CheckTrend(bool isBuy)
   {
      int engineIndex = m_engineNumber - 1;

      if(engineIndex < 0 || engineIndex >= ENGINE_COUNT)
         return false;

      double fEMA = g_CachedFastEMA[engineIndex];
      double sEMA = g_CachedSlowEMA[engineIndex];

      if(fEMA <= 0.0 || sEMA <= 0.0)
         return false;

      if(isBuy)
         return (fEMA > sEMA);

      return (fEMA < sEMA);
   }

   //+------------------------------------------------------------------+
   //| Momentum check                                                   |
   //+------------------------------------------------------------------+
   bool CheckMomentum(bool isBuy)
   {
      if(isBuy)
         return (g_CachedClose1 > g_CachedOpen1);

      return (g_CachedClose1 < g_CachedOpen1);
   }

   //+------------------------------------------------------------------+
   //| Trade permission                                                 |
   //+------------------------------------------------------------------+
   bool CanTrade()
   {
      int engineIndex = m_engineNumber - 1;

      if(engineIndex < 0 || engineIndex >= ENGINE_COUNT)
         return false;

      return (g_CachedEngineOrders[engineIndex] < m_maxTrades);
   }

   //+------------------------------------------------------------------+
   //| Normalize lot size                                               |
   //| Native MQL4 implementation                                      |
   //+------------------------------------------------------------------+
   double NormLots(double lots)
   {
      string symbolName = Symbol();

      double step   = MarketInfo(symbolName, MODE_LOTSTEP);
      double minLot = MarketInfo(symbolName, MODE_MINLOT);
      double maxLot = MarketInfo(symbolName, MODE_MAXLOT);

      if(step <= 0.0)
         step = 0.01;

      if(minLot <= 0.0)
         minLot = step;

      if(maxLot <= 0.0)
         maxLot = lots;

      lots = MathFloor(lots / step + 0.0000001) * step;

      if(lots < minLot)
         lots = minLot;

      if(lots > maxLot)
         lots = maxLot;

      return NormalizeDouble(lots, 2);
   }

   //+------------------------------------------------------------------+
   //| Send market order                                                |
   //| Native MQL4 implementation                                      |
   //+------------------------------------------------------------------+
   bool SendOrder(int type)
   {
      string symbolName = Symbol();

      RefreshRates();

      double entryPrice;

      if(type == OP_BUY)
         entryPrice = Ask;
      else if(type == OP_SELL)
         entryPrice = Bid;
      else
         return false;

      int priceDigits = (int)MarketInfo(symbolName, MODE_DIGITS);

      if(priceDigits < 0)
         priceDigits = Digits;

      entryPrice = NormalizeDouble(entryPrice, priceDigits);

      double lots = NormLots(LotSize);

      if(lots <= 0.0)
         return false;

      string comment =
         TradeComment + " [" +
         IntegerToString(m_engineNumber) + "]";

      int slip = Slippage;

      if(priceDigits == 3 || priceDigits == 5)
         slip *= 10;

      int orderColor = 0x008000; // clrGreen

      if(type == OP_SELL)
         orderColor = 0xFF0000; // clrRed

      for(int r = 0; r < 3; r++)
      {
         while(IsTradeContextBusy())
         {
            Sleep(100);
            RefreshRates();
         }

         if(type == OP_BUY)
            entryPrice = NormalizeDouble(Ask, priceDigits);
         else
            entryPrice = NormalizeDouble(Bid, priceDigits);

         ResetLastError();

         int ticket = OrderSend(
            symbolName,
            type,
            lots,
            entryPrice,
            slip,
            0,
            0,
            comment,
            m_magic,
            0,
            orderColor
         );

         if(ticket > 0)
         {
            g_CachedEAOpenOrders++;

            int engineIndex = m_engineNumber - 1;

            if(engineIndex >= 0 &&
               engineIndex < ENGINE_COUNT)
            {
               g_CachedEngineOrders[engineIndex]++;
            }

            return true;
         }

         int err = GetLastError();

         if(err == ERR_REQUOTE ||
            err == ERR_OFF_QUOTES ||
            err == ERR_PRICE_CHANGED)
         {
            Sleep(200);
            RefreshRates();
            continue;
         }

         Print("OrderSend error: ", err);
         return false;
      }

      return false;
   }

   //+------------------------------------------------------------------+
   //| Execution wrappers                                               |
   //+------------------------------------------------------------------+
   bool ExecBuy()
   {
      return SendOrder(OP_BUY);
   }

   bool ExecSell()
   {
      return SendOrder(OP_SELL);
   }
};

#endif // ENGINE_MQH