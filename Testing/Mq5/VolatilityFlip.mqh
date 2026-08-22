//+------------------------------------------------------------------+
//| VolatilityFlip.mqh                                               |
//| Independent Volatility Shock Direction Flip Module               |
//+------------------------------------------------------------------+
#ifndef VOLATILITY_FLIP_MQH
#define VOLATILITY_FLIP_MQH

//============================== INPUTS ==============================
input bool     UseVolatilityFlip        = true;
input int      VolatilityBaselineBars   = 20;
input double   MaxRangeExpansionRatio   = 1.80;
input int      VolatilityFlipPeriodBars = 3;
input bool     VolatilityFlipDebug      = false;

//========================== STATE ===================================
datetime g_vf_lastShockBarTime          = 0;
bool     g_vf_volatilityFlip            = false;
int      g_vf_flipBarsRemaining         = 0;

//+------------------------------------------------------------------+
//| Initialize module                                                |
//+------------------------------------------------------------------+
void VolatilityFlip_Init()
{
   g_vf_volatilityFlip    = false;
   g_vf_flipBarsRemaining = 0;
   g_vf_lastShockBarTime  = 0;
}

//+------------------------------------------------------------------+
//| Calculate baseline range excluding a specific bar                |
//+------------------------------------------------------------------+
double VolatilityFlip_CalculateBaselineRange(int excludeBar)
{
   double sum = 0.0;
   int counted = 0;

   for(int i = 1; i <= 1 + VolatilityBaselineBars && i < Bars; i++)
   {
      if(i == excludeBar)
         continue;

      sum += High[i] - Low[i];
      counted++;
   }

   return (counted > 0) ? (sum / counted) : 0.0;
}

//+------------------------------------------------------------------+
//| Check if a specific bar is a volatility shock                    |
//+------------------------------------------------------------------+
bool VolatilityFlip_IsBarAShock(int barIndex, bool logOutput = true)
{
   if(!UseVolatilityFlip)
      return false;

   if(barIndex < 0 || barIndex >= Bars)
      return false;

   double candleRange   = High[barIndex] - Low[barIndex];
   double baselineRange = VolatilityFlip_CalculateBaselineRange(barIndex);

   if(baselineRange <= 0.0)
      return false;

   double shockRatio = candleRange / baselineRange;
   bool isShock      = (shockRatio >= MaxRangeExpansionRatio);

   if(logOutput && VolatilityFlipDebug)
   {
      Print("  [SHOCK] Bar[", barIndex, "] at ",
            TimeToStr(Time[barIndex]),
            " Range=", DoubleToString(candleRange, Digits),
            " Baseline=", DoubleToString(baselineRange, Digits),
            " Ratio=", DoubleToString(shockRatio, 2),
            isShock ? " ** SHOCK **" : " normal");
   }

   return isShock;
}

//+------------------------------------------------------------------+
//| Detect newly completed oversized candle                          |
//+------------------------------------------------------------------+
bool VolatilityFlip_Detect()
{
   if(!UseVolatilityFlip)
      return false;

   if(Bars < VolatilityBaselineBars + 3)
      return false;

   if(!VolatilityFlip_IsBarAShock(1, true))
      return false;

   g_vf_lastShockBarTime  = Time[1];
   g_vf_volatilityFlip    = true;
   g_vf_flipBarsRemaining = VolatilityFlipPeriodBars;

   Print("============================================================");
   Print(">>> VOLATILITY DIRECTION FLIP ACTIVATED <<<");
   Print("    Shock Bar: ", TimeToStr(Time[1]));
   Print("    Flip Period: ", VolatilityFlipPeriodBars, " bars");
   Print("    Normal BUY  -> SELL");
   Print("    Normal SELL -> BUY");
   Print("============================================================");

   return true;
}

//+------------------------------------------------------------------+
//| Update temporary volatility flip period                          |
//+------------------------------------------------------------------+
void VolatilityFlip_Update()
{
   if(!g_vf_volatilityFlip)
      return;

   g_vf_flipBarsRemaining--;

   if(g_vf_flipBarsRemaining <= 0)
   {
      g_vf_volatilityFlip    = false;
      g_vf_flipBarsRemaining = 0;

      Print(">>> VOLATILITY DIRECTION FLIP EXPIRED <<<");
      Print("    Returning to normal BUY/SELL operation");
   }
}

//+------------------------------------------------------------------+
//| Return current flip state                                        |
//+------------------------------------------------------------------+
bool IsVolatilityFlipped()
{
   return g_vf_volatilityFlip;
}

#endif
//+------------------------------------------------------------------+