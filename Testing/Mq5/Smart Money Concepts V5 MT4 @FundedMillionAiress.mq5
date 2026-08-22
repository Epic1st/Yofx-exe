//+------------------------------------------------------------------+
//|  Smart Money Concepts [LuxAlgo] MQL5 Port v4 Dark                |
//|  Original: LuxAlgo (CC BY-NC-SA 4.0)                            |
//|                                                                  |
//|  ORDER BLOCK ENHANCEMENTS (v4.1):                                |
//|  • Hover tooltip: type, direction, High/Low, zone intent         |
//|  • Internal OBs : Teal (bull) / Orange (bear) — dotted border   |
//|  • Swing OBs    : Deep Blue (bull) / Deep Red (bear) — solid    |
//|                                                                  |
//|  FVG ENHANCEMENTS (v4.2):                                        |
//|  • Current TF / HTF / ITF FVG with hover tooltips               |
//|  • ITF FVGs show dotted midline                                  |
//|  • Mitigated FVGs removed automatically                          |
//|                                                                  |
//|  DASHBOARD (V5.3):                                               |
//|  • "Advanced SMC" panel — top-right corner                  |
//|  • Current Time (broker server time)                             |
//|  • Current TF : Swing CHoCH bias / Internal CHoCH bias          |
//|  • ITF        : Swing CHoCH / Internal CHoCH / Internal BOS     |
//|  • HTF        : Swing CHoCH / Internal CHoCH                    |
//+------------------------------------------------------------------+
#property copyright "LuxAlgo (CC BY-NC-SA 4.0) MQL5 Port"
#property version   "5.30"
#property indicator_chart_window
#property indicator_plots 0

//--- INPUTS
input string  InpMode             = "Historical"; // Mode: Historical | Present
input string  InpStyle            = "Colored";    // Style: Colored | Monochrome
input bool    InpColorCandles     = false;        // Color Candles

input bool    InpShowInternals    = true;         // Show Internal Structure
input string  InpInternalBull     = "All";        // Internal Bullish: All | BOS | CHoCH
input string  InpInternalBear     = "All";        // Internal Bearish: All | BOS | CHoCH
input bool    InpConfluenceFilter = false;        // Confluence Filter
input color   InpIntBullColor     = C'91,156,246'; // Internal Bullish Color
input color   InpIntBearColor     = C'255,100,100'; // Internal Bearish Color

input bool    InpShowStructure    = true;         // Show Swing Structure
input string  InpSwingBull        = "All";        // Swing Bullish: All | BOS | CHoCH
input string  InpSwingBear        = "All";        // Swing Bearish: All | BOS | CHoCH
input color   InpSwgBullColor     = C'0,220,170'; // Swing Bullish Color
input color   InpSwgBearColor     = C'255,110,110'; // Swing Bearish Color
input bool    InpShowSwingPoints  = true;        // Show HH/HL/LH/LL
input int     InpSwingsLength     = 50;           // Swing Detection Length
input bool    InpShowHighLow      = true;         // Show Strong/Weak High/Low

input bool    InpShowInternalOB   = true;         // Show Internal Order Blocks
input int     InpInternalOBCount  = 5;            // Internal OB Count
input bool    InpShowSwingOB      = true;        // Show Swing Order Blocks
input int     InpSwingOBCount     = 5;            // Swing OB Count
input string  InpOBMitigation     = "High/Low";   // OB Mitigation: Close | High/Low
// Transparency 0=opaque 255=invisible — Pine uses ~80% so 200 here
input int     InpOBAlpha          = 200;          // OB Transparency (0=solid, 255=invisible)
// Internal OB: brighter/lighter shades (quick reaction zones inside swing)
input color   InpIntBullOBColor   = C'80,80,80';   // Teal/Cyan  — Internal Bullish OB
input color   InpIntBearOBColor   = C'80,80,80';   // Orange     — Internal Bearish OB
// Swing OB: deeper/richer shades (major institutional zones)
input color   InpSwgBullOBColor   = C'30,100,255';  // Deep Blue  — Swing Bullish OB
input color   InpSwgBearOBColor   = C'210,30,60';   // Deep Red   — Swing Bearish OB

input bool    InpShowEqualHL      = true;         // Show Equal Highs/Lows
input int     InpEqualHLBars      = 3;            // EQH/EQL Bars Confirmation
input double  InpEqualHLThresh    = 0.1;          // EQH/EQL ATR Threshold

input bool    InpShowZones        = true;        // Show Premium/Discount Zones
input color   InpPremiumColor     = C'210,60,75';
input color   InpEquilColor       = C'90,100,120';
input color   InpDiscountColor    = C'0,180,140';

input bool    InpUseLookBack      = true;        // Enable Look Back Period
input int     InpLookBackBars     = 1000;          // Look Back Period (bars)

// ── TIMEFRAME SETTINGS ────────────────────────────────────────────
// These control BOTH the FVG zones and the Dashboard bias analysis.
// HTF should be higher than your chart TF (e.g. H4 on H1 chart).
// ITF should be lower than your chart TF (e.g. M15 on H1 chart).
input ENUM_TIMEFRAMES InpHTFPeriod = PERIOD_H4;   // Higher TimeFrame (HTF)
input ENUM_TIMEFRAMES InpITFPeriod = PERIOD_H1;  // Intra TimeFrame  (ITF)

// ── DASHBOARD ─────────────────────────────────────────────────────
input bool    InpShowDashboard    = true;         // Show Dashboard
input bool    InpShowHTF          = true;         // Show HTF Section in Dashboard
input bool    InpShowITF          = true;         // Show ITF Section in Dashboard
input int     InpDashX            = 5;            // Dashboard X Position
input int     InpDashY            = 430;          // Dashboard Y Position
input int     InpDashFontSize     = 9;            // Font Size

//--- CONSTANTS
#define BULL  1
#define BEAR -1

//--- STRUCTS
struct SPivot
{
   double   level;
   double   lastLevel;
   bool     crossed;
   datetime barTime;
   int      barIndex;
};

struct SOB
{
   double   hi, lo;
   datetime barTime;     // time of the OB candle itself (hi/lo source)
   datetime confirmTime; // time of the BOS/CHoCH that confirmed this OB
   int      bias;
   string   fillObj;
   string   bdrObj;
};

struct STrail
{
   double   top, bottom;
   datetime barTime;
   int      barIndex;
   datetime lastTopTime, lastBottomTime;
};

//--- GLOBALS
SPivot  g_swH, g_swL;
SPivot  g_inH, g_inL;
SPivot  g_eqH, g_eqL;
STrail  g_tr;
int     g_swTr = 0, g_inTr = 0;

// Dashboard: last swing structure signal (CHoCH or BOS) for Current TF
string  g_swLastSignal    = "None";
color   g_swLastSignalClr = C'90,95,110';

SOB     g_swOB[], g_inOB[];
double  g_pH[], g_pL[], g_hi[], g_lo[];
datetime g_t[];
int     g_bars = 0;
double  g_atr  = 0.0;
int     g_uid  = 0;

// Performance: single ATR handle + pre-copied full buffer
int     g_atrHandle = INVALID_HANDLE;
double  g_atrBuf[];

// HTF / ITF rates buffers used for dashboard bias calculation
MqlRates g_htfRates[];
MqlRates g_itfRates[];
// Dashboard: HTF/ITF structure bias (computed each tick from CopyRates data)
// Values: BULL=1, BEAR=-1, 0=NEUTRAL
int      g_htfSwingChochBias  = 0;
int      g_htfInternChochBias = 0;
string   g_htfLastSignal      = "None";
color    g_htfLastSignalClr   = C'90,95,110';
datetime g_htfLastCalc        = 0;

int      g_itfSwingChochBias  = 0;
int      g_itfInternChochBias = 0;
int      g_itfInternBosBias   = 0;
string   g_itfChochSignal     = "Calculating...";
color    g_itfChochClr        = C'90,95,110';
string   g_itfBosSignal       = "Calculating...";
color    g_itfBosClr          = C'90,95,110';
string   g_itfSwingSignal     = "Calculating...";
color    g_itfSwingClr        = C'90,95,110';
datetime g_itfLastCalc        = 0;

// Dashboard object name prefix (separate from SMC_ so ResetAll doesn't wipe it)
#define DASH_PFX  "JAFX_DB_"
#define DASH_FONT "Consolas"

//+------------------------------------------------------------------+
string UID(string p) { return p + IntegerToString(++g_uid); }

color cSwBull() { return InpStyle=="Monochrome" ? C'160,165,175' : InpSwgBullColor; }
color cSwBear() { return InpStyle=="Monochrome" ? C'110,115,130'   : InpSwgBearColor; }
color cInBull() { return InpStyle=="Monochrome" ? C'160,165,175' : InpIntBullColor; }
color cInBear() { return InpStyle=="Monochrome" ? C'110,115,130'   : InpIntBearColor; }

// Build ARGB color with alpha for semi-transparent OB fill
color OBFillColor(bool internal, int bias)
{
   color base;
   if(InpStyle=="Monochrome")
      base = bias==BULL ? C'160,165,175' : C'110,115,130';
   else if(internal)
      base = bias==BULL ? InpIntBullOBColor : InpIntBearOBColor;
   else
      base = bias==BULL ? InpSwgBullOBColor : InpSwgBearOBColor;

   // Apply alpha transparency via ColorToARGB
   int alpha = MathMax(0, MathMin(255, InpOBAlpha));
   return (color)ColorToARGB(base, (uchar)(255 - alpha));
}

color OBBorderColor(bool internal, int bias)
{
   if(internal) return clrNONE;
   if(InpStyle=="Monochrome") return bias==BULL ? C'160,165,175' : C'110,115,130';
   return bias==BULL ? InpSwgBullOBColor : InpSwgBearOBColor;
}

// Returns ATR from pre-cached buffer (index = bar index, NOT shift)
// Call RefreshATRBuffer() once per OnCalculate before using this.
double GetATR(int barIdx)
{
   int sz = ArraySize(g_atrBuf);
   if(sz > 0 && barIdx >= 0 && barIdx < sz) return g_atrBuf[barIdx];
   return _Point * 100;
}

// Copy the full ATR series once per OnCalculate tick
void RefreshATRBuffer(int rates_total)
{
   if(g_atrHandle == INVALID_HANDLE)
      g_atrHandle = iATR(_Symbol, PERIOD_CURRENT, 200);
   if(g_atrHandle == INVALID_HANDLE) return;

   ArraySetAsSeries(g_atrBuf, false);
   ArrayResize(g_atrBuf, rates_total);
   // CopyBuffer with shift=0, count=rates_total fills oldest→newest
   double tmp[];
   ArraySetAsSeries(tmp, false);
   if(CopyBuffer(g_atrHandle, 0, 0, rates_total, tmp) == rates_total)
      ArrayCopy(g_atrBuf, tmp);
}

//+------------------------------------------------------------------+
void DrawStruct(SPivot &p, string tag, color clr, bool dashed, datetime endTime)
{
   if(p.level==EMPTY_VALUE) return;
   string ln=UID("SMC_SL"), lb=UID("SMC_ST");

   ObjectCreate(0,ln,OBJ_TREND,0,p.barTime,p.level,endTime,p.level);
   ObjectSetInteger(0,ln,OBJPROP_COLOR,    clr);
   ObjectSetInteger(0,ln,OBJPROP_STYLE,    dashed?STYLE_DASH:STYLE_SOLID);
   ObjectSetInteger(0,ln,OBJPROP_WIDTH,    1);
   ObjectSetInteger(0,ln,OBJPROP_RAY_RIGHT,false);
   ObjectSetInteger(0,ln,OBJPROP_BACK,     true);

   datetime mid=(datetime)(((long)p.barTime+(long)endTime)/2);
   ObjectCreate(0,lb,OBJ_TEXT,0,mid,p.level);
   ObjectSetString(0, lb,OBJPROP_TEXT,    tag);
   ObjectSetInteger(0,lb,OBJPROP_COLOR,   clr);
   ObjectSetInteger(0,lb,OBJPROP_FONTSIZE,7);
   ObjectSetInteger(0,lb,OBJPROP_ANCHOR,  ANCHOR_LOWER);
}

void DrawSwPt(datetime t, double price, string tag, color clr, bool above)
{
   string nm=UID("SMC_SP");
   ObjectCreate(0,nm,OBJ_TEXT,0,t,price);
   ObjectSetString(0, nm,OBJPROP_TEXT,    tag);
   ObjectSetInteger(0,nm,OBJPROP_COLOR,   clr);
   ObjectSetInteger(0,nm,OBJPROP_FONTSIZE,7);
   ObjectSetInteger(0,nm,OBJPROP_ANCHOR,  above?ANCHOR_UPPER:ANCHOR_LOWER);
}

void DrawEQL(SPivot &p, double level, datetime toTime, bool isHigh)
{
   string ln=isHigh?"SMC_EQH_L":"SMC_EQL_L";
   string lb=isHigh?"SMC_EQH_T":"SMC_EQL_T";
   if(InpMode=="Present"){ObjectDelete(0,ln);ObjectDelete(0,lb);}
   color clr=isHigh?cSwBear():cSwBull();

   ObjectCreate(0,ln,OBJ_TREND,0,p.barTime,p.level,toTime,level);
   ObjectSetInteger(0,ln,OBJPROP_COLOR,    clr);
   ObjectSetInteger(0,ln,OBJPROP_STYLE,    STYLE_DOT);
   ObjectSetInteger(0,ln,OBJPROP_WIDTH,    1);
   ObjectSetInteger(0,ln,OBJPROP_RAY_RIGHT,false);

   datetime mid=(datetime)(((long)p.barTime+(long)toTime)/2);
   ObjectCreate(0,lb,OBJ_TEXT,0,mid,level);
   ObjectSetString(0, lb,OBJPROP_TEXT,    isHigh?"EQH":"EQL");
   ObjectSetInteger(0,lb,OBJPROP_COLOR,   clr);
   ObjectSetInteger(0,lb,OBJPROP_FONTSIZE,7);
   ObjectSetInteger(0,lb,OBJPROP_ANCHOR,  isHigh?ANCHOR_UPPER:ANCHOR_LOWER);
}

//+------------------------------------------------------------------+
// Draw order block rectangle — key fix: OBJPROP_BACK=true + ARGB color
//+------------------------------------------------------------------+
void DrawOBRect(SOB &ob, bool internal)
{
   // Clean old objects
   if(ob.fillObj!="") { ObjectDelete(0,ob.fillObj); ob.fillObj=""; }
   if(ob.bdrObj !="") { ObjectDelete(0,ob.bdrObj);  ob.bdrObj=""; }

   datetime rt = TimeCurrent()+(datetime)(PeriodSeconds()*50);

   // --- Build descriptive tooltip shown on mouse hover ---
   string obType   = internal ? "Internal Order Block" : "Swing Order Block";
   string obBias   = (ob.bias==BULL) ? "Bullish" : "Bearish";
   string obAction = (ob.bias==BULL) ? "Demand Zone — expect price to react UP"
                                     : "Supply Zone — expect price to react DOWN";
   string tooltip  = obType + " | " + obBias + "\n"
                   + "High : " + DoubleToString(ob.hi, _Digits) + "\n"
                   + "Low  : " + DoubleToString(ob.lo, _Digits) + "\n"
                   + obAction;

   // --- Filled background rectangle (BACK=true so candles appear on top) ---
   ob.fillObj = UID("SMC_OBF");
   color fillClr = OBFillColor(internal, ob.bias);

   // Left edge = BOS/CHoCH confirmation bar (not the OB candle)
   // This ensures no zone appears before structure is broken
   datetime leftEdge = (ob.confirmTime > 0) ? ob.confirmTime : ob.barTime;
   ObjectCreate(0,ob.fillObj,OBJ_RECTANGLE,0,leftEdge,ob.hi,rt,ob.lo);
   ObjectSetInteger(0,ob.fillObj,OBJPROP_COLOR,      fillClr);
   ObjectSetInteger(0,ob.fillObj,OBJPROP_BGCOLOR,     fillClr);
   ObjectSetInteger(0,ob.fillObj,OBJPROP_FILL,       true);
   ObjectSetInteger(0,ob.fillObj,OBJPROP_BACK,       true);
   ObjectSetInteger(0,ob.fillObj,OBJPROP_WIDTH,      1);
   ObjectSetInteger(0,ob.fillObj,OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0,ob.fillObj,OBJPROP_SELECTED,   false);
   ObjectSetString(0, ob.fillObj,OBJPROP_TOOLTIP,    tooltip);  // ← hover description

   // --- Border rectangle (BACK=false so border is visible on top) ---
   // Both Internal AND Swing get a border — Internal gets a thinner dashed-style border
   ob.bdrObj = UID("SMC_OBB");
   color bdrClr = internal ? OBFillColor(internal, ob.bias)   // subtler for internal
                           : OBBorderColor(internal, ob.bias); // solid for swing
   int   bdrW   = internal ? 1 : 2;                           // thinner for internal

   ObjectCreate(0,ob.bdrObj,OBJ_RECTANGLE,0,leftEdge,ob.hi,rt,ob.lo);
   ObjectSetInteger(0,ob.bdrObj,OBJPROP_COLOR,      bdrClr);
   ObjectSetInteger(0,ob.bdrObj,OBJPROP_FILL,       false);
   ObjectSetInteger(0,ob.bdrObj,OBJPROP_BACK,       false);
   ObjectSetInteger(0,ob.bdrObj,OBJPROP_WIDTH,      bdrW);
   ObjectSetInteger(0,ob.bdrObj,OBJPROP_STYLE,      internal ? STYLE_DOT : STYLE_SOLID);
   ObjectSetInteger(0,ob.bdrObj,OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0,ob.bdrObj,OBJPROP_SELECTED,   false);
   ObjectSetString(0, ob.bdrObj,OBJPROP_TOOLTIP,    tooltip);  // ← hover description on border too
}

void DrawZoneBox(string nm, datetime lt, double top, double bot, color clr, string lbl, bool above)
{
   ObjectDelete(0,nm); ObjectDelete(0,nm+"_l");
   datetime rt=TimeCurrent()+(datetime)(PeriodSeconds()*50);
   color fillClr=(color)ColorToARGB(clr,(uchar)180);

   ObjectCreate(0,nm,OBJ_RECTANGLE,0,lt,top,rt,bot);
   ObjectSetInteger(0,nm,OBJPROP_COLOR,   fillClr);
   ObjectSetInteger(0,nm,OBJPROP_BGCOLOR,  fillClr);
   ObjectSetInteger(0,nm,OBJPROP_FILL,    true);
   ObjectSetInteger(0,nm,OBJPROP_BACK,    true);

   ObjectCreate(0,nm+"_l",OBJ_TEXT,0,rt,above?top:bot);
   ObjectSetString(0, nm+"_l",OBJPROP_TEXT,    lbl);
   ObjectSetInteger(0,nm+"_l",OBJPROP_COLOR,   clr);
   ObjectSetInteger(0,nm+"_l",OBJPROP_FONTSIZE,8);
   ObjectSetInteger(0,nm+"_l",OBJPROP_ANCHOR,  above?ANCHOR_UPPER:ANCHOR_LOWER);
}

//+------------------------------------------------------------------+
void StoreOB(SPivot &p, bool internal, int bias, int bosBarIndex)
{
   if( internal&&!InpShowInternalOB) return;
   if(!internal&&!InpShowSwingOB)    return;

   // Search only between the pivot bar and the BOS/CHoCH bar (exclusive)
   // This ensures the OB candle is always BEFORE the confirming structure break
   int si=p.barIndex, ei=bosBarIndex;
   if(si<0||si>=ei) return;

   int best_i=si; double best=(bias==BEAR)?-DBL_MAX:DBL_MAX;
   int arrSz=ArraySize(g_pH);
   for(int i=si; i<ei&&i<arrSz; i++)
   {
      if(bias==BEAR&&g_pH[i]>best){best=g_pH[i];best_i=i;}
      if(bias==BULL&&g_pL[i]<best){best=g_pL[i];best_i=i;}
   }
   if(best_i<0||best_i>=arrSz) return;

   SOB ob;
   ob.hi=g_hi[best_i]; ob.lo=g_lo[best_i];
   ob.barTime=g_t[best_i];
   // confirmTime = the BOS/CHoCH bar — rectangle will start here, not at the OB candle
   ob.confirmTime=(bosBarIndex<ArraySize(g_t))?g_t[bosBarIndex]:g_t[best_i];
   ob.bias=bias;
   ob.fillObj=""; ob.bdrObj="";

   if(internal)
   {
      int sz=ArraySize(g_inOB);
      if(sz>=100)
      {
         ObjectDelete(0,g_inOB[sz-1].fillObj);
         ObjectDelete(0,g_inOB[sz-1].bdrObj);
         ArrayRemove(g_inOB,sz-1,1); sz--;
      }
      ArrayResize(g_inOB,sz+1);
      for(int i=sz;i>0;i--) g_inOB[i]=g_inOB[i-1];
      g_inOB[0]=ob;
   }
   else
   {
      int sz=ArraySize(g_swOB);
      if(sz>=100)
      {
         ObjectDelete(0,g_swOB[sz-1].fillObj);
         ObjectDelete(0,g_swOB[sz-1].bdrObj);
         ArrayRemove(g_swOB,sz-1,1); sz--;
      }
      ArrayResize(g_swOB,sz+1);
      for(int i=sz;i>0;i--) g_swOB[i]=g_swOB[i-1];
      g_swOB[0]=ob;
   }
}

void MitigateOBs(bool internal, double c, double h, double l)
{
   double bearSrc=(InpOBMitigation=="Close")?c:h;
   double bullSrc=(InpOBMitigation=="Close")?c:l;
   int sz=internal?ArraySize(g_inOB):ArraySize(g_swOB);
   for(int i=sz-1;i>=0;i--)
   {
      bool mit=false;
      if(internal)
      {
         if(g_inOB[i].bias==BEAR&&bearSrc>g_inOB[i].hi) mit=true;
         if(g_inOB[i].bias==BULL&&bullSrc<g_inOB[i].lo) mit=true;
         if(mit)
         {
            ObjectDelete(0,g_inOB[i].fillObj);
            ObjectDelete(0,g_inOB[i].bdrObj);
            ArrayRemove(g_inOB,i,1);
         }
      }
      else
      {
         if(g_swOB[i].bias==BEAR&&bearSrc>g_swOB[i].hi) mit=true;
         if(g_swOB[i].bias==BULL&&bullSrc<g_swOB[i].lo) mit=true;
         if(mit)
         {
            ObjectDelete(0,g_swOB[i].fillObj);
            ObjectDelete(0,g_swOB[i].bdrObj);
            ArrayRemove(g_swOB,i,1);
         }
      }
   }
}

void RedrawOBs(bool internal)
{
   int maxOB = internal?InpInternalOBCount:InpSwingOBCount;
   int sz    = internal?ArraySize(g_inOB):ArraySize(g_swOB);
   int lim   = MathMin(sz,maxOB);
   for(int i=0;i<lim;i++)
   {
      if(internal) DrawOBRect(g_inOB[i],true);
      else         DrawOBRect(g_swOB[i],false);
   }
}

//+------------------------------------------------------------------+
// Helper: check if a BOS/CHoCH tag qualifies under the user's filter setting
bool StructQualifies(bool internal, int bias, const string &tag)
{
   if(bias==BULL)
   {
      if(internal)
      {
         if(!InpShowInternals) return false;
         return (InpInternalBull=="All")
             || (InpInternalBull=="BOS"   && tag=="BOS")
             || (InpInternalBull=="CHoCH" && tag=="CHoCH");
      }
      else
      {
         if(!InpShowStructure) return false;
         return (InpSwingBull=="All")
             || (InpSwingBull=="BOS"   && tag=="BOS")
             || (InpSwingBull=="CHoCH" && tag=="CHoCH");
      }
   }
   else // BEAR
   {
      if(internal)
      {
         if(!InpShowInternals) return false;
         return (InpInternalBear=="All")
             || (InpInternalBear=="BOS"   && tag=="BOS")
             || (InpInternalBear=="CHoCH" && tag=="CHoCH");
      }
      else
      {
         if(!InpShowStructure) return false;
         return (InpSwingBear=="All")
             || (InpSwingBear=="BOS"   && tag=="BOS")
             || (InpSwingBear=="CHoCH" && tag=="CHoCH");
      }
   }
}

//+------------------------------------------------------------------+
void ProcessStruct(bool internal, datetime t, double c, int barIdx)
{
   SPivot pH=internal?g_inH:g_swH;
   SPivot pL=internal?g_inL:g_swL;
   int    tr=internal?g_inTr:g_swTr;

   color bullClr=internal?cInBull():cSwBull();
   color bearClr=internal?cInBear():cSwBear();

   if(pH.level!=EMPTY_VALUE&&!pH.crossed)
   {
      bool ex=internal?(g_inH.level!=g_swH.level):true;
      if(c>pH.level&&ex)
      {
         string tag=(tr==BEAR)?"CHoCH":"BOS";
         pH.crossed=true; tr=BULL;

         bool show=StructQualifies(internal,BULL,tag);
         if(show) DrawStruct(pH,tag,bullClr,internal,t);

         // Track last swing CHoCH for dashboard
         if(!internal && tag=="CHoCH")
            { g_swLastSignal="CHoCH ▲ Bullish"; g_swLastSignalClr=bullClr; }

         // Pass barIdx so StoreOB search is bounded to [pivot..BOS/CHoCH bar)
         // OB candle is guaranteed to be BEFORE the confirming break
         if(show) StoreOB(pH,internal,BULL,barIdx);
      }
   }

   if(pL.level!=EMPTY_VALUE&&!pL.crossed)
   {
      bool ex=internal?(g_inL.level!=g_swL.level):true;
      if(c<pL.level&&ex)
      {
         string tag=(tr==BULL)?"CHoCH":"BOS";
         pL.crossed=true; tr=BEAR;

         bool show=StructQualifies(internal,BEAR,tag);
         if(show) DrawStruct(pL,tag,bearClr,internal,t);

         // Track last swing CHoCH for dashboard
         if(!internal && tag=="CHoCH")
            { g_swLastSignal="CHoCH ▼ Bearish"; g_swLastSignalClr=bearClr; }

         if(show) StoreOB(pL,internal,BEAR,barIdx);
      }
   }

   if(internal){g_inH=pH;g_inL=pL;g_inTr=tr;}
   else        {g_swH=pH;g_swL=pL;g_swTr=tr;}
}

//+------------------------------------------------------------------+
// Symmetric left+right pivot confirmation
void UpdatePivots(int size, bool eqHL, bool internal, int barIdx, datetime t)
{
   int pivIdx=barIdx-size;
   if(pivIdx<size)         return;
   if(pivIdx+size>=g_bars) return;

   double pivH=g_hi[pivIdx], pivL=g_lo[pivIdx];
   bool isH=true, isL=true;

   for(int i=pivIdx-size; i<pivIdx; i++)
   {
      if(g_hi[i]>=pivH) isH=false;
      if(g_lo[i]<=pivL) isL=false;
   }
   for(int i=pivIdx+1; i<=pivIdx+size; i++)
   {
      if(g_hi[i]>=pivH) isH=false;
      if(g_lo[i]<=pivL) isL=false;
   }
   if(!isH&&!isL) return;

   SPivot pHl=eqHL?g_eqH:(internal?g_inH:g_swH);
   SPivot pLl=eqHL?g_eqL:(internal?g_inL:g_swL);

   if(isL)
   {
      if(eqHL&&pLl.level!=EMPTY_VALUE)
         if(MathAbs(pLl.level-pivL)<InpEqualHLThresh*g_atr)
            DrawEQL(pLl,pivL,t,false);

      pLl.lastLevel=pLl.level;
      pLl.level    =pivL;
      pLl.crossed  =false;
      pLl.barTime  =g_t[pivIdx];
      pLl.barIndex =pivIdx;

      if(!eqHL&&!internal)
      {
         // g_tr.bottom kept for legacy lookback boundary only
      }
      if(InpShowSwingPoints&&!internal&&!eqHL&&pLl.lastLevel!=EMPTY_VALUE)
         DrawSwPt(g_t[pivIdx],pivL,pivL<pLl.lastLevel?"LL":"HL",cSwBull(),false);
   }

   if(isH)
   {
      if(eqHL&&pHl.level!=EMPTY_VALUE)
         if(MathAbs(pHl.level-pivH)<InpEqualHLThresh*g_atr)
            DrawEQL(pHl,pivH,t,true);

      pHl.lastLevel=pHl.level;
      pHl.level    =pivH;
      pHl.crossed  =false;
      pHl.barTime  =g_t[pivIdx];
      pHl.barIndex =pivIdx;

      if(!eqHL&&!internal)
      {
         // g_tr.top kept for legacy lookback boundary only
      }
      if(InpShowSwingPoints&&!internal&&!eqHL&&pHl.lastLevel!=EMPTY_VALUE)
         DrawSwPt(g_t[pivIdx],pivH,pivH>pHl.lastLevel?"HH":"LH",cSwBear(),true);
   }

   if(eqHL)        {g_eqH=pHl;g_eqL=pLl;}
   else if(internal){g_inH=pHl;g_inL=pLl;}
   else             {g_swH=pHl;g_swL=pLl;}
}

//+------------------------------------------------------------------+
void DrawHighLow()
{
   // Use the last confirmed swing high and low directly.
   // These are set by UpdatePivots and always reflect the most recent
   // confirmed symmetric pivot — which IS the correct Strong/Weak level.
   if(g_swH.level==EMPTY_VALUE || g_swL.level==EMPTY_VALUE) return;
   if(g_swH.barTime==0         || g_swL.barTime==0)         return;

   // SMC rule:
   //   If trend is BULLISH → the swing HIGH ahead hasn't been swept → "Weak High"
   //                          the swing LOW that held defines support   → "Strong Low"
   //   If trend is BEARISH → the swing LOW ahead hasn't been swept     → "Weak Low"
   //                          the swing HIGH that held defines resistance→ "Strong High"
   string hl = (g_swTr==BEAR) ? "Strong High" : "Weak High";
   string ll = (g_swTr==BULL) ? "Strong Low"  : "Weak Low";
   color  hc = cSwBear();
   color  lc = cSwBull();

   // ── High line ──────────────────────────────────────────────────
   ObjectDelete(0,"SMC_HLine");
   ObjectDelete(0,"SMC_HLine_l");

   ObjectCreate(0,"SMC_HLine",OBJ_TREND,0,
                g_swH.barTime, g_swH.level,
                g_swH.barTime, g_swH.level);
   ObjectSetInteger(0,"SMC_HLine",OBJPROP_COLOR,     hc);
   ObjectSetInteger(0,"SMC_HLine",OBJPROP_STYLE,     STYLE_DOT);
   ObjectSetInteger(0,"SMC_HLine",OBJPROP_WIDTH,     1);
   ObjectSetInteger(0,"SMC_HLine",OBJPROP_RAY_RIGHT, true);
   ObjectSetInteger(0,"SMC_HLine",OBJPROP_SELECTABLE,false);

   ObjectCreate(0,"SMC_HLine_l",OBJ_TEXT,0, g_swH.barTime, g_swH.level);
   ObjectSetString(0, "SMC_HLine_l",OBJPROP_TEXT,     " " + hl);
   ObjectSetInteger(0,"SMC_HLine_l",OBJPROP_COLOR,    hc);
   ObjectSetInteger(0,"SMC_HLine_l",OBJPROP_FONTSIZE, 8);
   ObjectSetInteger(0,"SMC_HLine_l",OBJPROP_ANCHOR,   ANCHOR_LEFT_LOWER);
   ObjectSetInteger(0,"SMC_HLine_l",OBJPROP_SELECTABLE,false);

   // ── Low line ───────────────────────────────────────────────────
   ObjectDelete(0,"SMC_LLine");
   ObjectDelete(0,"SMC_LLine_l");

   ObjectCreate(0,"SMC_LLine",OBJ_TREND,0,
                g_swL.barTime, g_swL.level,
                g_swL.barTime, g_swL.level);
   ObjectSetInteger(0,"SMC_LLine",OBJPROP_COLOR,     lc);
   ObjectSetInteger(0,"SMC_LLine",OBJPROP_STYLE,     STYLE_DOT);
   ObjectSetInteger(0,"SMC_LLine",OBJPROP_WIDTH,     1);
   ObjectSetInteger(0,"SMC_LLine",OBJPROP_RAY_RIGHT, true);
   ObjectSetInteger(0,"SMC_LLine",OBJPROP_SELECTABLE,false);

   ObjectCreate(0,"SMC_LLine_l",OBJ_TEXT,0, g_swL.barTime, g_swL.level);
   ObjectSetString(0, "SMC_LLine_l",OBJPROP_TEXT,     " " + ll);
   ObjectSetInteger(0,"SMC_LLine_l",OBJPROP_COLOR,    lc);
   ObjectSetInteger(0,"SMC_LLine_l",OBJPROP_FONTSIZE, 8);
   ObjectSetInteger(0,"SMC_LLine_l",OBJPROP_ANCHOR,   ANCHOR_LEFT_UPPER);
   ObjectSetInteger(0,"SMC_LLine_l",OBJPROP_SELECTABLE,false);
}

void DrawZones()
{
   // Premium / Equilibrium / Discount zones are defined by the
   // last confirmed swing HIGH and swing LOW — same levels as Strong/Weak High/Low.
   if(g_swH.level==EMPTY_VALUE || g_swL.level==EMPTY_VALUE) return;
   if(g_swH.barTime==0         || g_swL.barTime==0)         return;

   double top = g_swH.level;
   double bot = g_swL.level;
   if(top <= bot) return;

   // Zone left anchor = whichever swing point came first on the chart
   datetime zoneStart = MathMin(g_swH.barTime, g_swL.barTime);

   // Premium   = top 5% of range  (upper quarter near the high)
   // Equil     = middle 5% band   (50% level ± 2.5%)
   // Discount  = bottom 5% of range (lower quarter near the low)
   double rng = top - bot;
   DrawZoneBox("SMC_Premium",  zoneStart, top,              top  - rng*0.05, InpPremiumColor,  "Premium",     true);
   DrawZoneBox("SMC_Equil",    zoneStart, bot  + rng*0.525, bot  + rng*0.475,InpEquilColor,    "Equilibrium", false);
   DrawZoneBox("SMC_Discount", zoneStart, bot  + rng*0.05,  bot,             InpDiscountColor, "Discount",    false);
}

//+------------------------------------------------------------------+
// DASHBOARD — exact ZULU style
//+------------------------------------------------------------------+

// DL — create/update a label
void DL(const string id, const int x, const int y,
        const string text, const color clr, const int fontSize)
{
   string name = DASH_PFX + id;
   if(ObjectFind(0, name) < 0)
   {
      ObjectCreate(0, name, OBJ_LABEL, 0, 0, 0);
      ObjectSetInteger(0, name, OBJPROP_CORNER,     CORNER_LEFT_UPPER);
      ObjectSetInteger(0, name, OBJPROP_ANCHOR,     ANCHOR_LEFT_UPPER);
      ObjectSetString( 0, name, OBJPROP_FONT,       DASH_FONT);
      ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
      ObjectSetInteger(0, name, OBJPROP_BACK,       false);
   }
   ObjectSetInteger(0, name, OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, name, OBJPROP_YDISTANCE, y);
   ObjectSetString( 0, name, OBJPROP_TEXT,      text);
   ObjectSetInteger(0, name, OBJPROP_COLOR,     clr);
   ObjectSetInteger(0, name, OBJPROP_FONTSIZE,  fontSize);
}

// DS — update text + color only
void DS(const string id, const string text, const color clr)
{
   string name = DASH_PFX + id;
   ObjectSetString( 0, name, OBJPROP_TEXT,  text);
   ObjectSetInteger(0, name, OBJPROP_COLOR, clr);
}

//+------------------------------------------------------------------+
void DashCreate()
{
   string bg = DASH_PFX + "BG";
   ObjectCreate(0, bg, OBJ_RECTANGLE_LABEL, 0, 0, 0);
   ObjectSetInteger(0, bg, OBJPROP_XDISTANCE,   InpDashX);
   ObjectSetInteger(0, bg, OBJPROP_YDISTANCE,   InpDashY);
   ObjectSetInteger(0, bg, OBJPROP_XSIZE,       260);
   ObjectSetInteger(0, bg, OBJPROP_YSIZE,       10);
   ObjectSetInteger(0, bg, OBJPROP_BGCOLOR,     C'8,10,18');
   ObjectSetInteger(0, bg, OBJPROP_BORDER_COLOR,C'30,34,46');
   ObjectSetInteger(0, bg, OBJPROP_BORDER_TYPE, BORDER_FLAT);
   ObjectSetInteger(0, bg, OBJPROP_CORNER,      CORNER_LEFT_UPPER);
   ObjectSetInteger(0, bg, OBJPROP_BACK,        false);
   ObjectSetInteger(0, bg, OBJPROP_SELECTABLE,  false);

   int x0 = InpDashX + 12;
   int y0 = InpDashY + 10;
   int fs = InpDashFontSize;
   int lh = fs + 9;
   int xV = InpDashX + 150;   // value column X

   // Helper: clean TF name
   string curTF = EnumToString(PERIOD_CURRENT); StringReplace(curTF, "PERIOD_", "");
   string itfStr = EnumToString(InpITFPeriod);  StringReplace(itfStr, "PERIOD_", "");
   string htfStr = EnumToString(InpHTFPeriod);  StringReplace(htfStr, "PERIOD_", "");

   // ── Title + time ──────────────────────────────────────────────
   DL("Title", x0, y0, "JAFX ADVANCED SMC", C'90,180,220', fs + 1); y0 += lh + 2;
   DL("Time",  x0, y0, TimeToString(TimeCurrent(), TIME_DATE|TIME_MINUTES), C'90,95,110', fs - 1); y0 += lh - 2;

   // TF overview row: ITF | Current | HTF
   DL("TFRow", x0, y0, itfStr + "  <  " + curTF + "  <  " + htfStr, C'60,160,100', fs - 1); y0 += lh - 1;

   // Warning row (created now, updated in DashUpdate)
   DL("Sep0", x0, y0, "──────────────────────────────", C'30,34,46', fs - 2); y0 += lh - 3;

   // ── Current TF ────────────────────────────────────────────────
   DL("CurTitle", x0, y0, "◈ " + curTF + " ANALYSIS", C'180,185,220', fs); y0 += lh;
   DL("SwBLbl",   x0, y0, "Swing Bias:",    C'90,95,110', fs);
   DL("SwBVal",   xV, y0, "---",            C'160,168,180', fs); y0 += lh;
   DL("IntBLbl",  x0, y0, "Internal Bias:", C'90,95,110', fs);
   DL("IntBVal",  xV, y0, "---",            C'160,168,180', fs); y0 += lh;
   DL("LastLbl",  x0, y0, "Last Signal:",   C'90,95,110', fs);
   DL("LastVal",  xV, y0, "---",            C'160,168,180', fs); y0 += lh + 2;

   // ── ITF Section ───────────────────────────────────────────────
   if(InpShowITF)
   {
      y0 += lh;
      DL("ITFSep",      x0, y0, "──────────────────────────────", C'30,34,46', fs - 2); y0 += lh - 3;
      DL("ITFTitle",    x0, y0, "◈ " + itfStr + " ITF ANALYSIS", C'180,140,220', fs); y0 += lh;
      DL("ITFSwLbl",    x0, y0, "ITF Swing Bias:",  C'90,95,110', fs);
      DL("ITFSwVal",    xV, y0, "---",              C'160,168,180', fs); y0 += lh;
      DL("ITFChochLbl", x0, y0, "ITF CHoCH:",       C'90,95,110', fs);
      DL("ITFChochVal", xV, y0, "---",              C'160,168,180', fs); y0 += lh;
      DL("ITFBosLbl",   x0, y0, "ITF BOS:",         C'90,95,110', fs);
      DL("ITFBosVal",   xV, y0, "---",              C'160,168,180', fs); y0 += lh;
      DL("ITFIntLbl",   x0, y0, "ITF Int. Bias:",   C'90,95,110', fs);
      DL("ITFIntVal",   xV, y0, "---",              C'160,168,180', fs); y0 += lh + 2;
   }

   // ── HTF Section ───────────────────────────────────────────────
   if(InpShowHTF)
   {
      y0 += lh;
      DL("HTFSep",    x0, y0, "──────────────────────────────", C'30,34,46', fs - 2); y0 += lh - 3;
      DL("HTFTitle",  x0, y0, "⬆ " + htfStr + " HTF ANALYSIS", C'90,180,220', fs); y0 += lh;
      DL("HTFSwBLbl", x0, y0, "HTF Swing Bias:",  C'90,95,110', fs);
      DL("HTFSwBVal", xV, y0, "---",              C'160,168,180', fs); y0 += lh;
      DL("HTFIntLbl", x0, y0, "HTF Int. Bias:",   C'90,95,110', fs);
      DL("HTFIntVal", xV, y0, "---",              C'160,168,180', fs); y0 += lh;
      DL("HTFSigLbl", x0, y0, "HTF Last Signal:", C'90,95,110', fs);
      DL("HTFSigVal", xV, y0, "---",              C'160,168,180', fs); y0 += lh + 2;
   }

   ObjectSetInteger(0, bg, OBJPROP_YSIZE, (y0 + lh + 2) - InpDashY);
   ChartRedraw(0);
}

//+------------------------------------------------------------------+
void DashUpdate()
{
   // ── Current time ──────────────────────────────────────────────
   DS("Time", TimeToString(TimeCurrent(), TIME_DATE|TIME_MINUTES), C'90,95,110');

   // ── Current TF bias ───────────────────────────────────────────
   color bullClr = cSwBull();
   color bearClr = cSwBear();

   string swB = (g_swTr==BULL) ? "▲ BULLISH" : (g_swTr==BEAR) ? "▼ BEARISH" : "— NEUTRAL";
   color  swC = (g_swTr==BULL) ? bullClr     : (g_swTr==BEAR) ? bearClr     : C'90,95,110';
   DS("SwBVal", swB, swC);

   string intB = (g_inTr==BULL) ? "▲ BULLISH" : (g_inTr==BEAR) ? "▼ BEARISH" : "— NEUTRAL";
   color  intC = (g_inTr==BULL) ? cInBull()   : (g_inTr==BEAR) ? cInBear()   : C'90,95,110';
   DS("IntBVal", intB, intC);

   // Last signal = most recent Swing CHoCH on current TF (not BOS)
   DS("LastVal", g_swLastSignal, g_swLastSignalClr);

   // ── ITF Section ───────────────────────────────────────────────
   if(InpShowITF)
   {
      CalcITFBias();
      DS("ITFSwVal",    g_itfSwingSignal, g_itfSwingClr);
      DS("ITFChochVal", g_itfChochSignal, g_itfChochClr);
      DS("ITFBosVal",   g_itfBosSignal,   g_itfBosClr);

      string itfIntB = (g_itfInternChochBias==BULL) ? "▲ BULLISH" :
                       (g_itfInternChochBias==BEAR) ? "▼ BEARISH" : "— NEUTRAL";
      color  itfIntC = (g_itfInternChochBias==BULL) ? cInBull() :
                       (g_itfInternChochBias==BEAR) ? cInBear() : C'90,95,110';
      DS("ITFIntVal", itfIntB, itfIntC);
   }

   // ── HTF Section ───────────────────────────────────────────────
   if(InpShowHTF)
   {
      CalcHTFBias();

      string htfSwB = (g_htfSwingChochBias==BULL) ? "▲ BULLISH" :
                      (g_htfSwingChochBias==BEAR) ? "▼ BEARISH" : "— NEUTRAL";
      color  htfSwC = (g_htfSwingChochBias==BULL) ? bullClr :
                      (g_htfSwingChochBias==BEAR) ? bearClr : C'90,95,110';
      DS("HTFSwBVal", htfSwB, htfSwC);

      string htfIntB = (g_htfInternChochBias==BULL) ? "▲ BULLISH" :
                       (g_htfInternChochBias==BEAR) ? "▼ BEARISH" : "— NEUTRAL";
      color  htfIntC = (g_htfInternChochBias==BULL) ? cInBull() :
                       (g_htfInternChochBias==BEAR) ? cInBear() : C'90,95,110';
      DS("HTFIntVal", htfIntB, htfIntC);
      DS("HTFSigVal", g_htfLastSignal, g_htfLastSignalClr);
   }

   ChartRedraw(0);
}

//+------------------------------------------------------------------+
// CalcHTFBias
//+------------------------------------------------------------------+
void CalcHTFBias()
{
   ENUM_TIMEFRAMES htfTF = InpHTFPeriod;
   datetime htfBarTime   = (datetime)iTime(_Symbol, htfTF, 0);
   if(htfBarTime == g_htfLastCalc && g_htfLastCalc != 0) return;
   g_htfLastCalc = htfBarTime;

   int barsNeeded = MathMax(InpSwingsLength, 5) * 4 + 10;
   int available  = Bars(_Symbol, htfTF);
   if(available < barsNeeded) barsNeeded = available;
   if(barsNeeded < InpSwingsLength * 2 + 2) return;

   // Non-series: index 0=oldest, barsNeeded-1=newest for correct pivot left/right
   double h[], l[], c[];
   ArraySetAsSeries(h, false); ArraySetAsSeries(l, false); ArraySetAsSeries(c, false);
   if(CopyHigh (_Symbol, htfTF, barsNeeded, 0, h) <= 0)
      if(CopyHigh (_Symbol, htfTF, 0, barsNeeded, h) <= 0) return;
   if(CopyLow  (_Symbol, htfTF, barsNeeded, 0, l) <= 0)
      if(CopyLow  (_Symbol, htfTF, 0, barsNeeded, l) <= 0) return;
   if(CopyClose(_Symbol, htfTF, barsNeeded, 0, c) <= 0)
      if(CopyClose(_Symbol, htfTF, 0, barsNeeded, c) <= 0) return;

   int total = ArraySize(h);

   //--- Swing CHoCH scan (oldest to newest)
   int    swBias = 0;
   double swPivH = 0, swPivL = 0;
   bool   swPivHCrossed = false, swPivLCrossed = false;
   int    pl = InpSwingsLength;

   for(int i = pl; i < total - pl; i++)
   {
      bool isH = true;
      for(int j = 1; j <= pl && isH; j++)
         if(h[i-j] >= h[i] || h[i+j] >= h[i]) isH = false;
      if(isH) { swPivH = h[i]; swPivHCrossed = false; }

      bool isL = true;
      for(int j = 1; j <= pl && isL; j++)
         if(l[i-j] <= l[i] || l[i+j] <= l[i]) isL = false;
      if(isL) { swPivL = l[i]; swPivLCrossed = false; }

      if(swPivH > 0 && c[i] > swPivH && !swPivHCrossed)
      {
         swPivHCrossed = true;
         if(swBias == BEAR) { g_htfSwingChochBias = BULL; g_htfLastSignal = "CHoCH ▲ Bullish"; g_htfLastSignalClr = cSwBull(); }
         swBias = BULL;
      }
      if(swPivL > 0 && c[i] < swPivL && !swPivLCrossed)
      {
         swPivLCrossed = true;
         if(swBias == BULL) { g_htfSwingChochBias = BEAR; g_htfLastSignal = "CHoCH ▼ Bearish"; g_htfLastSignalClr = cSwBear(); }
         swBias = BEAR;
      }
   }
   if(swBias != 0 && g_htfSwingChochBias == 0) g_htfSwingChochBias = swBias;

   //--- Internal CHoCH scan — matches ProcessStruct exactly
   //    CHoCH = reversal break; BOS = continuation break
   //    ex-guard: internal pivot must differ from swing pivot level
   int    intBias      = 0;
   int    ipl          = 5;
   double intPivH      = 0, intPivL      = 0;
   double intSwPivH_ex = 0, intSwPivL_ex = 0;   // parallel swing pivots for ex-guard
   bool   intPivHCrossed = false, intPivLCrossed = false;

   for(int i = ipl; i < total - ipl; i++)
   {
      // Internal pivot high (length 5)
      bool isH = true;
      for(int j = 1; j <= ipl && isH; j++)
         if(h[i-j] >= h[i] || h[i+j] >= h[i]) isH = false;
      if(isH) { intPivH = h[i]; intPivHCrossed = false; }

      // Internal pivot low (length 5)
      bool isL = true;
      for(int j = 1; j <= ipl && isL; j++)
         if(l[i-j] <= l[i] || l[i+j] <= l[i]) isL = false;
      if(isL) { intPivL = l[i]; intPivLCrossed = false; }

      // Parallel swing pivot for ex-guard
      int spl = InpSwingsLength;
      if(i >= spl && i + spl < total)
      {
         bool isSwH = true;
         for(int j = 1; j <= spl && isSwH; j++)
            if(h[i-j] >= h[i] || h[i+j] >= h[i]) isSwH = false;
         if(isSwH) intSwPivH_ex = h[i];

         bool isSwL = true;
         for(int j = 1; j <= spl && isSwL; j++)
            if(l[i-j] <= l[i] || l[i+j] <= l[i]) isSwL = false;
         if(isSwL) intSwPivL_ex = l[i];
      }

      // Break above internal pivot high (ex-guard applied)
      if(intPivH > 0 && c[i] > intPivH && !intPivHCrossed && intPivH != intSwPivH_ex)
      {
         intPivHCrossed = true;
         if(intBias == BEAR) { g_htfInternChochBias = BULL; }   // reversal = CHoCH
         intBias = BULL;
      }

      // Break below internal pivot low (ex-guard applied)
      if(intPivL > 0 && c[i] < intPivL && !intPivLCrossed && intPivL != intSwPivL_ex)
      {
         intPivLCrossed = true;
         if(intBias == BULL) { g_htfInternChochBias = BEAR; }   // reversal = CHoCH
         intBias = BEAR;
      }
   }
   if(intBias != 0 && g_htfInternChochBias == 0) g_htfInternChochBias = intBias;
}
//+------------------------------------------------------------------+
// CalcITFBias — mirrors ZULU CalcITFBias exactly
//+------------------------------------------------------------------+
void CalcITFBias()
{
   ENUM_TIMEFRAMES itfTF = InpITFPeriod;
   datetime itfBarTime   = (datetime)iTime(_Symbol, itfTF, 0);
   if(itfBarTime == g_itfLastCalc && g_itfLastCalc != 0) return;
   g_itfLastCalc = itfBarTime;

   int barsNeeded = MathMin(Bars(_Symbol, itfTF), InpLookBackBars > 0 ? InpLookBackBars : 1000);
   if(barsNeeded < 5 * 2 + 2) return;

   // Use NON-series arrays: index 0 = oldest, barsNeeded-1 = newest
   // This ensures pivot left/right sides are correct (left=lower index, right=higher index)
   double h[], l[], c[];
   ArraySetAsSeries(h, false); ArraySetAsSeries(l, false); ArraySetAsSeries(c, false);
   if(CopyHigh (_Symbol, itfTF, barsNeeded, 0, h) <= 0)
      if(CopyHigh (_Symbol, itfTF, 0, barsNeeded, h) <= 0) return;
   if(CopyLow  (_Symbol, itfTF, barsNeeded, 0, l) <= 0)
      if(CopyLow  (_Symbol, itfTF, 0, barsNeeded, l) <= 0) return;
   if(CopyClose(_Symbol, itfTF, barsNeeded, 0, c) <= 0)
      if(CopyClose(_Symbol, itfTF, 0, barsNeeded, c) <= 0) return;

   int total = ArraySize(h);

   // Internal CHoCH / BOS scan — mirrors ProcessStruct exactly:
   //   pivot length = 5 (same as chart internal)
   //   CHoCH only on reversal (bias was opposite); BOS on continuation
   //   ex-guard: internal pivot must differ from parallel swing pivot level
   int    pl          = 5;
   int    spl_ex      = InpSwingsLength;   // parallel swing pivot for ex-guard
   int    bias        = 0;
   int    lastChoch   = 0;
   int    lastBos     = 0;
   double pivHLevel   = 0, pivLLevel   = 0;
   double swPivH_ex   = 0, swPivL_ex   = 0;
   bool   pivHCrossed = false, pivLCrossed = false;

   for(int p = pl; p < total - pl; p++)
   {
      // Internal pivot high (length 5)
      bool isH = true;
      for(int j = 1; j <= pl && isH; j++)
         if(h[p-j] >= h[p] || h[p+j] >= h[p]) isH = false;
      if(isH) { pivHLevel = h[p]; pivHCrossed = false; }

      // Internal pivot low (length 5)
      bool isL = true;
      for(int j = 1; j <= pl && isL; j++)
         if(l[p-j] <= l[p] || l[p+j] <= l[p]) isL = false;
      if(isL) { pivLLevel = l[p]; pivLCrossed = false; }

      // Parallel swing pivot tracking for ex-guard
      if(p >= spl_ex && p + spl_ex < total)
      {
         bool isSwH = true;
         for(int j = 1; j <= spl_ex && isSwH; j++)
            if(h[p-j] >= h[p] || h[p+j] >= h[p]) isSwH = false;
         if(isSwH) swPivH_ex = h[p];

         bool isSwL = true;
         for(int j = 1; j <= spl_ex && isSwL; j++)
            if(l[p-j] <= l[p] || l[p+j] <= l[p]) isSwL = false;
         if(isSwL) swPivL_ex = l[p];
      }

      // Break above internal pivot high (ex-guard: must differ from swing pivot)
      if(pivHLevel > 0 && c[p] > pivHLevel && !pivHCrossed && pivHLevel != swPivH_ex)
      {
         pivHCrossed = true;
         if(bias == BEAR) lastChoch = BULL;   // reversal = CHoCH
         else             lastBos   = BULL;   // continuation = BOS
         bias = BULL;
      }

      // Break below internal pivot low
      if(pivLLevel > 0 && c[p] < pivLLevel && !pivLCrossed && pivLLevel != swPivL_ex)
      {
         pivLCrossed = true;
         if(bias == BULL) lastChoch = BEAR;
         else             lastBos   = BEAR;
         bias = BEAR;
      }
   }

   g_itfInternChochBias = (lastChoch != 0) ? lastChoch : 0;
   g_itfInternBosBias   = lastBos;

   if(lastChoch==BULL)      { g_itfChochSignal="CHoCH ▲ Bullish"; g_itfChochClr=cInBull(); }
   else if(lastChoch==BEAR) { g_itfChochSignal="CHoCH ▼ Bearish"; g_itfChochClr=cInBear(); }
   else                     { g_itfChochSignal="CHoCH: —";         g_itfChochClr=C'90,95,110'; }

   if(lastBos==BULL)        { g_itfBosSignal="BOS ▲ Bullish";  g_itfBosClr=cInBull(); }
   else if(lastBos==BEAR)   { g_itfBosSignal="BOS ▼ Bearish";  g_itfBosClr=cInBear(); }
   else                     { g_itfBosSignal="BOS: —";          g_itfBosClr=C'90,95,110'; }

   //--- Swing CHoCH scan (pivot length = InpSwingsLength)
   int    spl           = InpSwingsLength;
   int    swBarsNeeded  = MathMin(Bars(_Symbol, itfTF), MathMax(total, spl * 4 + 10));
   double sh[], sl[], sc[];
   ArraySetAsSeries(sh, false); ArraySetAsSeries(sl, false); ArraySetAsSeries(sc, false);
   bool swOk = true;
   if(CopyHigh (_Symbol, itfTF, swBarsNeeded, 0, sh) <= 0) swOk = false;
   if(CopyLow  (_Symbol, itfTF, swBarsNeeded, 0, sl) <= 0) swOk = false;
   if(CopyClose(_Symbol, itfTF, swBarsNeeded, 0, sc) <= 0) swOk = false;
   if(!swOk) { ArrayCopy(sh,h); ArrayCopy(sl,l); ArrayCopy(sc,c); swBarsNeeded=total; }

   int    swTotal     = ArraySize(sh);
   int    swBias      = 0, swLastChoch = 0;
   double swPivH      = 0, swPivL = 0;
   bool   swPivHCrossed = false, swPivLCrossed = false;

   for(int p = spl; p < swTotal - spl; p++)
   {
      bool isH = true;
      for(int j = 1; j <= spl && isH; j++)
         if(sh[p-j] >= sh[p] || sh[p+j] >= sh[p]) isH = false;
      if(isH) { swPivH = sh[p]; swPivHCrossed = false; }

      bool isL = true;
      for(int j = 1; j <= spl && isL; j++)
         if(sl[p-j] <= sl[p] || sl[p+j] <= sl[p]) isL = false;
      if(isL) { swPivL = sl[p]; swPivLCrossed = false; }

      if(swPivH > 0 && sc[p] > swPivH && !swPivHCrossed)
         { swPivHCrossed=true; if(swBias==BEAR) swLastChoch=BULL; swBias=BULL; }
      if(swPivL > 0 && sc[p] < swPivL && !swPivLCrossed)
         { swPivLCrossed=true; if(swBias==BULL) swLastChoch=BEAR; swBias=BEAR; }
   }
   if(swLastChoch == 0 && swBias != 0) swLastChoch = swBias;
   g_itfSwingChochBias = swLastChoch;

   if(swLastChoch==BULL)      { g_itfSwingSignal="CHoCH ▲ Bullish"; g_itfSwingClr=cSwBull(); }
   else if(swLastChoch==BEAR) { g_itfSwingSignal="CHoCH ▼ Bearish"; g_itfSwingClr=cSwBear(); }
   else                       { g_itfSwingSignal="CHoCH: —";         g_itfSwingClr=C'90,95,110'; }
}

//+------------------------------------------------------------------+
void ResetAll()
{
   ObjectsDeleteAll(0,"SMC_",-1,-1);
   g_swH.level=EMPTY_VALUE; g_swH.lastLevel=EMPTY_VALUE; g_swH.crossed=false; g_swH.barIndex=-1;
   g_swL.level=EMPTY_VALUE; g_swL.lastLevel=EMPTY_VALUE; g_swL.crossed=false; g_swL.barIndex=-1;
   g_inH.level=EMPTY_VALUE; g_inH.lastLevel=EMPTY_VALUE; g_inH.crossed=false; g_inH.barIndex=-1;
   g_inL.level=EMPTY_VALUE; g_inL.lastLevel=EMPTY_VALUE; g_inL.crossed=false; g_inL.barIndex=-1;
   g_eqH.level=EMPTY_VALUE; g_eqH.lastLevel=EMPTY_VALUE; g_eqH.crossed=false; g_eqH.barIndex=-1;
   g_eqL.level=EMPTY_VALUE; g_eqL.lastLevel=EMPTY_VALUE; g_eqL.crossed=false; g_eqL.barIndex=-1;
   g_tr.top=0; g_tr.bottom=0; g_tr.barTime=0;
   g_tr.barIndex=0; g_tr.lastTopTime=0; g_tr.lastBottomTime=0;
   g_swTr=0; g_inTr=0;
   g_swLastSignal="None"; g_swLastSignalClr=C'90,95,110';
   ArrayResize(g_swOB,0); ArrayResize(g_inOB,0);
   g_uid=0;
}

// Returns the first bar index to process, respecting the LookBack setting.
// When InpUseLookBack is true, analysis is limited to the most recent
// InpLookBackBars bars so old structures outside the window are ignored.
int LookBackStart(int rates_total)
{
   if(!InpUseLookBack || InpLookBackBars <= 0) return 0;
   int start = rates_total - InpLookBackBars;
   return MathMax(0, start);
}

//+------------------------------------------------------------------+
int OnInit()
{
   ResetAll();
   ArrayResize(g_pH,0); ArrayResize(g_pL,0);
   ArrayResize(g_hi,0); ArrayResize(g_lo,0); ArrayResize(g_t,0);
   if(InpShowDashboard) DashCreate();
   return INIT_SUCCEEDED;
}

void OnDeinit(const int reason)
{
   ObjectsDeleteAll(0,"SMC_",-1,-1);
   ObjectsDeleteAll(0, DASH_PFX, -1, -1);
   ObjectDelete(0,"SMC_LookBackLine");
   ObjectDelete(0,"SMC_LookBackLabel");
   // Release ATR indicator handle to free memory
   if(g_atrHandle != INVALID_HANDLE)
   {
      IndicatorRelease(g_atrHandle);
      g_atrHandle = INVALID_HANDLE;
   }
}

//+------------------------------------------------------------------+
int OnCalculate(const int rates_total, const int prev_calculated,
                const datetime &time[], const double &open[],
                const double &high[],   const double &low[],
                const double &close[],  const long &tick_volume[],
                const long &volume[],   const int &spread[])
{
   // --- FIX 1: Refresh ATR buffer ONCE for all bars (not per-bar CopyBuffer) ---
   RefreshATRBuffer(rates_total);

   if(prev_calculated==0)
   {
      ResetAll();
      ArrayResize(g_pH,0); ArrayResize(g_pL,0);
      ArrayResize(g_hi,0); ArrayResize(g_lo,0); ArrayResize(g_t,0);
   }
   else if(InpUseLookBack && InpLookBackBars > 0)
   {
      // Lookback active: g_swH/g_swL will be re-populated during the scan
   }

   int startBar=(prev_calculated==0)?0:prev_calculated-1;

   // --- LOOK BACK PERIOD: clamp start bar to the user-defined window ---
   // When enabled, structures/OBs outside the window are ignored entirely.
   // A full reset is forced on every parameter change (prev_calculated==0).
   int lbStart = LookBackStart(rates_total);
   if(startBar < lbStart) startBar = lbStart;

   ArrayResize(g_hi,rates_total); ArrayResize(g_lo,rates_total);
   ArrayResize(g_pH,rates_total); ArrayResize(g_pL,rates_total);
   ArrayResize(g_t, rates_total);
   g_bars=rates_total;

   for(int i=startBar; i<rates_total; i++)
   {
      // FIX 2: GetATR now reads from pre-cached buffer — O(1) per bar
      double atr=GetATR(i);
      bool   hv =(high[i]-low[i])>=2.0*atr;
      g_hi[i]=high[i]; g_lo[i]=low[i];
      g_pH[i]=hv?low[i] :high[i];
      g_pL[i]=hv?high[i]:low[i];
      g_t[i] =time[i];
   }
   g_atr=GetATR(rates_total-1);

   // --- FIX 3: During history pass suppress all chart object drawing ---
   // Only draw on the LAST bar (live/backtest current candle).
   // Calculation still runs bar-by-bar for accuracy; only visuals are deferred.
   bool isLastBar = (rates_total - prev_calculated <= 1);

   for(int i=startBar; i<rates_total; i++)
   {
      UpdatePivots(InpSwingsLength, false, false, i, time[i]);
      UpdatePivots(5,               false, true,  i, time[i]);
      if(InpShowEqualHL) UpdatePivots(InpEqualHLBars, true, false, i, time[i]);

      if(InpShowInternals||InpShowInternalOB||InpColorCandles)
         ProcessStruct(true,  time[i], close[i], i);
      if(InpShowStructure||InpShowSwingOB||InpShowHighLow)
         ProcessStruct(false, time[i], close[i], i);

      if(InpShowInternalOB) MitigateOBs(true,  close[i], high[i], low[i]);
      if(InpShowSwingOB)    MitigateOBs(false, close[i], high[i], low[i]);
   }

   // FIX 3 cont: only redraw objects on the final bar, not every history bar
   if(isLastBar)
   {
      if(InpShowInternalOB) RedrawOBs(true);
      if(InpShowSwingOB)    RedrawOBs(false);
      if(InpShowHighLow)    DrawHighLow();
      if(InpShowZones)      DrawZones();

      // --- DASHBOARD ---
      if(InpShowDashboard) DashUpdate();

      // --- LOOK BACK BOUNDARY LINE ---
      // When the look-back period is active, draw a subtle vertical line at
      // the start of the analysis window so the user can see where history
      // is cut off.
      ObjectDelete(0,"SMC_LookBackLine");
      ObjectDelete(0,"SMC_LookBackLabel");
      if(InpUseLookBack && InpLookBackBars > 0 && lbStart > 0 && lbStart < rates_total)
      {
         datetime lbTime = time[lbStart];
         ObjectCreate(0,"SMC_LookBackLine",OBJ_VLINE,0,lbTime,0);
         ObjectSetInteger(0,"SMC_LookBackLine",OBJPROP_COLOR,  C'80,85,100');
         ObjectSetInteger(0,"SMC_LookBackLine",OBJPROP_STYLE,  STYLE_DOT);
         ObjectSetInteger(0,"SMC_LookBackLine",OBJPROP_WIDTH,  1);
         ObjectSetInteger(0,"SMC_LookBackLine",OBJPROP_BACK,   true);
         ObjectSetInteger(0,"SMC_LookBackLine",OBJPROP_SELECTABLE,false);
         ObjectSetString(0, "SMC_LookBackLine",OBJPROP_TOOLTIP,
                         "SMC Look Back Start (" + IntegerToString(InpLookBackBars) + " bars)");

         // Small text label at the top of the boundary line
         double chartTop = ChartGetDouble(0,CHART_PRICE_MAX);
         ObjectCreate(0,"SMC_LookBackLabel",OBJ_TEXT,0,lbTime,chartTop);
         ObjectSetString(0, "SMC_LookBackLabel",OBJPROP_TEXT,    "◀ LB");
         ObjectSetInteger(0,"SMC_LookBackLabel",OBJPROP_COLOR,   C'100,105,125');
         ObjectSetInteger(0,"SMC_LookBackLabel",OBJPROP_FONTSIZE,7);
         ObjectSetInteger(0,"SMC_LookBackLabel",OBJPROP_ANCHOR,  ANCHOR_LEFT_UPPER);
         ObjectSetInteger(0,"SMC_LookBackLabel",OBJPROP_BACK,    false);
         ObjectSetInteger(0,"SMC_LookBackLabel",OBJPROP_SELECTABLE,false);
      }

      // FIX 4: ChartRedraw only when needed, not every tick
      ChartRedraw(0);
   }

   // Update dashboard time on every tick regardless of bar state
   if(InpShowDashboard) DashUpdate();

   return rates_total;
}
//+------------------------------------------------------------------+