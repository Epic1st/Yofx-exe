//+------------------------------------------------------------------+
//|  RUDY Price Action Concepts EA                                   |
//|  Converted from Pine Script: Price Action Concepts               |
//|  [RUDY CHOCOfxINDICATOR] v1.2.2                                  |
//|  Original © RUDYBANK INDICATOR                                   |
//|  Licensed: CC BY-NC-SA 4.0                                       |
//|                                                                  |
//|  Added features:                                                 |
//|    - Configurable lot size                                       |
//|    - Auto Break-Even                                             |
//|    - Auto Trailing Stop Loss                                     |
//+------------------------------------------------------------------+
#property copyright "© RUDYBANK INDICATOR (Pine→MQ5 Conversion)"
#property version   "1.00"
#property strict

#include <Trade\Trade.mqh>
#include <Trade\PositionInfo.mqh>

CTrade        trade;
CPositionInfo posInfo;

//=============================================================
//  ── MARKET STRUCTURE INPUTS ──
//=============================================================
input group "=== MARKET STRUCTURE ==="
input int    InternalLookback   = 5;    // Internal Structure Lookback
input int    SwingLookback      = 50;   // Swing Structure Lookback
input bool   ShowBOS            = true; // Trade on BOS signals
input bool   ShowCHoCH          = true; // Trade on CHoCH signals
input bool   ShowCHoCHPlus      = true; // Trade on CHoCH+ signals

//=============================================================
//  ── ORDER / LOT SIZE INPUTS ──
//=============================================================
input group "=== ORDER MANAGEMENT ==="
input double LotSize            = 0.10;  // Lot Size
input int    StopLossPips       = 150;   // Stop Loss (pips)
input int    TakeProfitPips     = 300;   // Take Profit (pips)
input int    MagicNumber        = 202412; // EA Magic Number
input int    MaxOpenPositions   = 1;      // Max concurrent positions

//=============================================================
//  ── AUTO BREAK-EVEN INPUTS ──
//=============================================================
input group "=== AUTO BREAK-EVEN ==="
input bool   UseBreakEven       = true;  // Enable Auto Break-Even
input int    BreakEvenPips      = 100;   // Profit in pips to trigger break-even
input int    BreakEvenBuffer    = 5;     // Buffer above entry for break-even SL (pips)

//=============================================================
//  ── AUTO TRAILING STOP INPUTS ──
//=============================================================
input group "=== AUTO TRAILING STOP ==="
input bool   UseTrailingStop    = true;  // Enable Auto Trailing Stop
input int    TrailingStartPips  = 80;    // Profit in pips to start trailing
input int    TrailingStepPips   = 20;    // Trailing step (pips)
input int    TrailingDistPips   = 50;    // Trailing distance from price (pips)

//=============================================================
//  ── ORDER BLOCK INPUTS ──
//=============================================================
input group "=== ORDER BLOCKS ==="
input bool   TradeOrderBlocks   = true;  // Enter trades from Order Block zones
input int    OB_Lookback        = 5;     // Order Block lookback bars
input string OB_Filter          = "None"; // OB Filter: None | BOS | CHoCH | CHoCH+
input string OB_Mitigation      = "Absolute"; // Mitigation: Absolute | Middle

//=============================================================
//  ── FVG INPUTS ──
//=============================================================
input group "=== FAIR VALUE GAP ==="
input bool   UseFVG             = false; // Use FVG signals
input int    FVG_ExtendBars     = 10;    // FVG extend bars

//=============================================================
//  ── DISPLAY ──
//=============================================================
input group "=== DISPLAY ==="
input color  BullishColor       = clrTeal;   // Bullish color
input color  BearishColor       = clrRed;    // Bearish color
input color  OB_BullColor       = C'0,153,129'; // Bull OB box color
input color  OB_BearColor       = clrCrimson;   // Bear OB box color

//=============================================================
//  ── INTERNAL STATE ──
//=============================================================
double  pipValue;
int     digits;

// Structure tracking
double  lastInternalHigh = 0, lastInternalLow  = 0;
double  lastSwingHigh    = 0, lastSwingLow     = 0;
int     internalTrend    = 0;  // 1=bullish, -1=bearish, 0=neutral
int     swingTrend       = 0;

// Order block zones
struct OBZone {
    double top;
    double bottom;
    double mid;
    bool   bull;
    bool   active;
    datetime created;
};
OBZone InternalOBs[];
OBZone SwingOBs[];

// FVG zones
struct FVGZone {
    double top;
    double bottom;
    bool   bull;
    bool   active;
    datetime created;
};
FVGZone FVGs[];

//+------------------------------------------------------------------+
//| Expert initialization                                            |
//+------------------------------------------------------------------+
int OnInit()
{
    trade.SetExpertMagicNumber(MagicNumber);
    trade.SetDeviationInPoints(10);
    trade.SetTypeFilling(ORDER_FILLING_FOK);

    digits   = (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);
    pipValue = (digits == 3 || digits == 5) ? 10.0 * _Point : _Point;

    ArrayResize(InternalOBs, 0);
    ArrayResize(SwingOBs,    0);
    ArrayResize(FVGs,        0);

    Print("RUDY Price Action EA initialized. Pip value=", pipValue,
          "  Digits=", digits);
    return INIT_SUCCEEDED;
}

//+------------------------------------------------------------------+
//| Expert deinitialization                                          |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
    ObjectsDeleteAll(0, "RUDY_");
    Comment("");
}

//+------------------------------------------------------------------+
//| Expert tick function                                             |
//+------------------------------------------------------------------+
void OnTick()
{
    //-- Only run logic on a new bar
    static datetime lastBar = 0;
    datetime currentBar = (datetime)SeriesInfoInteger(_Symbol, PERIOD_CURRENT,
                                                      SERIES_LASTBAR_DATE);
    bool newBar = (currentBar != lastBar);
    if (newBar) lastBar = currentBar;

    //-- Always manage open positions (break-even + trailing)
    ManageOpenPositions();

    //-- Heavy analysis only on new bar
    if (!newBar) return;

    //-- Detect pivots and structure
    DetectMarketStructure();

    //-- Detect FVGs
    if (UseFVG) DetectFVG();

    //-- Evaluate entry signals
    if (CountOpenPositions() < MaxOpenPositions)
        EvaluateEntries();
}

//+------------------------------------------------------------------+
//| Detect swing/internal pivot highs and lows, classify BOS/CHoCH  |
//+------------------------------------------------------------------+
void DetectMarketStructure()
{
    //-- Internal pivots
    double iH = GetPivotHigh(InternalLookback);
    double iL = GetPivotLow (InternalLookback);

    //-- Swing pivots
    double sH = GetPivotHigh(SwingLookback);
    double sL = GetPivotLow (SwingLookback);

    double close0 = iClose(_Symbol, PERIOD_CURRENT, 0);

    //-----------------------------------------------------------
    // INTERNAL BULLISH BREAK
    //-----------------------------------------------------------
    if (lastInternalHigh > 0 && close0 > lastInternalHigh)
    {
        string structType;
        bool   isCHoCH = (internalTrend < 0);

        if (!isCHoCH)
        {
            structType = "BOS";
        }
        else
        {
            // Simplified CHoCH / CHoCH+ classification
            structType = (lastInternalLow > 0) ? "CHoCH+" : "CHoCH";
        }

        bool draw = (structType == "BOS"    && ShowBOS)    ||
                    (structType == "CHoCH"  && ShowCHoCH)  ||
                    (structType == "CHoCH+" && ShowCHoCHPlus);

        if (draw)
        {
            DrawStructureLine(true, structType,
                              iBarShift(_Symbol, PERIOD_CURRENT,
                                        iTime(_Symbol, PERIOD_CURRENT, InternalLookback)),
                              lastInternalHigh);
            if (TradeOrderBlocks && FilterPass(structType))
                AddInternalOB(true);
        }

        internalTrend = 1;
        lastInternalHigh = 0; // consumed
    }

    //-----------------------------------------------------------
    // INTERNAL BEARISH BREAK
    //-----------------------------------------------------------
    if (lastInternalLow > 0 && close0 < lastInternalLow)
    {
        string structType;
        bool   isCHoCH = (internalTrend > 0);

        structType = isCHoCH ? ((lastInternalHigh > 0) ? "CHoCH+" : "CHoCH") : "BOS";

        bool draw = (structType == "BOS"    && ShowBOS)    ||
                    (structType == "CHoCH"  && ShowCHoCH)  ||
                    (structType == "CHoCH+" && ShowCHoCHPlus);

        if (draw)
        {
            DrawStructureLine(false, structType,
                              iBarShift(_Symbol, PERIOD_CURRENT,
                                        iTime(_Symbol, PERIOD_CURRENT, InternalLookback)),
                              lastInternalLow);
            if (TradeOrderBlocks && FilterPass(structType))
                AddInternalOB(false);
        }

        internalTrend = -1;
        lastInternalLow = 0;
    }

    //-----------------------------------------------------------
    // SWING BULLISH BREAK
    //-----------------------------------------------------------
    if (lastSwingHigh > 0 && close0 > lastSwingHigh)
    {
        bool isCHoCH   = (swingTrend < 0);
        string structType = isCHoCH ? "CHoCH" : "BOS";

        DrawStructureLine(true, "S-" + structType,
                          iBarShift(_Symbol, PERIOD_CURRENT,
                                    iTime(_Symbol, PERIOD_CURRENT, SwingLookback)),
                          lastSwingHigh);
        swingTrend  = 1;
        lastSwingHigh = 0;
    }

    //-----------------------------------------------------------
    // SWING BEARISH BREAK
    //-----------------------------------------------------------
    if (lastSwingLow > 0 && close0 < lastSwingLow)
    {
        bool isCHoCH   = (swingTrend > 0);
        string structType = isCHoCH ? "CHoCH" : "BOS";

        DrawStructureLine(false, "S-" + structType,
                          iBarShift(_Symbol, PERIOD_CURRENT,
                                    iTime(_Symbol, PERIOD_CURRENT, SwingLookback)),
                          lastSwingLow);
        swingTrend  = -1;
        lastSwingLow = 0;
    }

    //-- Store new pivots
    if (iH > 0) lastInternalHigh = iH;
    if (iL > 0) lastInternalLow  = iL;
    if (sH > 0) lastSwingHigh    = sH;
    if (sL > 0) lastSwingLow     = sL;
}

//+------------------------------------------------------------------+
//| Detect 3-candle Fair Value Gaps                                  |
//+------------------------------------------------------------------+
void DetectFVG()
{
    double h2 = iHigh (_Symbol, PERIOD_CURRENT, 2);
    double l2 = iLow  (_Symbol, PERIOD_CURRENT, 2);
    double h0 = iHigh (_Symbol, PERIOD_CURRENT, 0);
    double l0 = iLow  (_Symbol, PERIOD_CURRENT, 0);

    // Bullish FVG: candle[2] high < candle[0] low → gap between them
    if (l0 > h2)
    {
        FVGZone fvg;
        fvg.top     = l0;
        fvg.bottom  = h2;
        fvg.bull    = true;
        fvg.active  = true;
        fvg.created = TimeCurrent();
        int sz = ArraySize(FVGs);
        ArrayResize(FVGs, sz + 1);
        FVGs[sz] = fvg;
        DrawFVGBox(fvg, sz);
    }

    // Bearish FVG: candle[2] low > candle[0] high → gap between them
    if (h0 < l2)
    {
        FVGZone fvg;
        fvg.top     = l2;
        fvg.bottom  = h0;
        fvg.bull    = false;
        fvg.active  = true;
        fvg.created = TimeCurrent();
        int sz = ArraySize(FVGs);
        ArrayResize(FVGs, sz + 1);
        FVGs[sz] = fvg;
        DrawFVGBox(fvg, sz);
    }

    // Invalidate FVGs that price has passed through
    double close0 = iClose(_Symbol, PERIOD_CURRENT, 0);
    for (int i = 0; i < ArraySize(FVGs); i++)
    {
        if (!FVGs[i].active) continue;
        if ( FVGs[i].bull  && close0 < FVGs[i].bottom) FVGs[i].active = false;
        if (!FVGs[i].bull  && close0 > FVGs[i].top   ) FVGs[i].active = false;
    }
}

//+------------------------------------------------------------------+
//| Evaluate entry signals from OBs and FVGs                         |
//+------------------------------------------------------------------+
void EvaluateEntries()
{
    double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
    double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);

    //--- Order Block entries
    if (TradeOrderBlocks)
    {
        for (int i = 0; i < ArraySize(InternalOBs); i++)
        {
            if (!InternalOBs[i].active) continue;

            //-- Bullish OB: price retraces into OB zone → buy
            if (InternalOBs[i].bull && bid <= InternalOBs[i].top &&
                bid >= InternalOBs[i].bottom)
            {
                double sl = InternalOBs[i].bottom - StopLossPips * pipValue;
                double tp = ask + TakeProfitPips  * pipValue;
                if (OpenBuy(sl, tp))
                {
                    InternalOBs[i].active = false;
                    Print("BUY from Bullish OB zone @ ", ask);
                }
                return;
            }

            //-- Bearish OB: price retraces into OB zone → sell
            if (!InternalOBs[i].bull && ask >= InternalOBs[i].bottom &&
                ask <= InternalOBs[i].top)
            {
                double sl = InternalOBs[i].top + StopLossPips * pipValue;
                double tp = bid - TakeProfitPips * pipValue;
                if (OpenSell(sl, tp))
                {
                    InternalOBs[i].active = false;
                    Print("SELL from Bearish OB zone @ ", bid);
                }
                return;
            }
        }
    }

    //--- FVG entries
    if (UseFVG)
    {
        for (int i = 0; i < ArraySize(FVGs); i++)
        {
            if (!FVGs[i].active) continue;

            if (FVGs[i].bull && bid <= FVGs[i].top && bid >= FVGs[i].bottom)
            {
                double sl = FVGs[i].bottom - StopLossPips * pipValue;
                double tp = ask + TakeProfitPips * pipValue;
                if (OpenBuy(sl, tp))
                {
                    FVGs[i].active = false;
                    Print("BUY from Bullish FVG @ ", ask);
                }
                return;
            }

            if (!FVGs[i].bull && ask >= FVGs[i].bottom && ask <= FVGs[i].top)
            {
                double sl = FVGs[i].top + StopLossPips * pipValue;
                double tp = bid - TakeProfitPips * pipValue;
                if (OpenSell(sl, tp))
                {
                    FVGs[i].active = false;
                    Print("SELL from Bearish FVG @ ", bid);
                }
                return;
            }
        }
    }
}

//+------------------------------------------------------------------+
//| Manage open positions: Break-Even + Trailing Stop                |
//+------------------------------------------------------------------+
void ManageOpenPositions()
{
    for (int i = PositionsTotal() - 1; i >= 0; i--)
    {
        if (!posInfo.SelectByIndex(i)) continue;
        if (posInfo.Magic()  != MagicNumber) continue;
        if (posInfo.Symbol() != _Symbol)     continue;

        double entryPrice = posInfo.PriceOpen();
        double currentSL  = posInfo.StopLoss();
        double currentTP  = posInfo.TakeProfit();
        double bid        = SymbolInfoDouble(_Symbol, SYMBOL_BID);
        double ask        = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
        double newSL      = currentSL;
        bool   isBuy      = (posInfo.PositionType() == POSITION_TYPE_BUY);

        //==========================================================
        //  AUTO BREAK-EVEN
        //  Move SL to entry (+buffer) once price travels BE pips
        //==========================================================
        if (UseBreakEven)
        {
            double beLevel   = BreakEvenPips   * pipValue;
            double beBuffer  = BreakEvenBuffer * pipValue;

            if (isBuy)
            {
                // Only move SL up to break-even, never down
                double beSL = entryPrice + beBuffer;
                if (bid >= entryPrice + beLevel && currentSL < beSL)
                    newSL = beSL;
            }
            else
            {
                double beSL = entryPrice - beBuffer;
                if (ask <= entryPrice - beLevel && (currentSL > beSL || currentSL == 0))
                    newSL = beSL;
            }
        }

        //==========================================================
        //  AUTO TRAILING STOP
        //  Once profit ≥ TrailingStartPips, trail SL behind price
        //  by TrailingDistPips, moving in steps of TrailingStepPips
        //==========================================================
        if (UseTrailingStop)
        {
            double trailStart = TrailingStartPips * pipValue;
            double trailDist  = TrailingDistPips  * pipValue;
            double trailStep  = TrailingStepPips  * pipValue;

            if (isBuy)
            {
                if (bid >= entryPrice + trailStart)
                {
                    double desired = bid - trailDist;
                    // Advance SL only in steps, and never below break-even SL
                    if (desired > newSL + trailStep)
                        newSL = desired;
                }
            }
            else
            {
                if (ask <= entryPrice - trailStart)
                {
                    double desired = ask + trailDist;
                    if (desired < newSL - trailStep || newSL == 0)
                        newSL = desired;
                }
            }
        }

        //-- Apply new SL if it changed meaningfully
        if (MathAbs(newSL - currentSL) >= _Point)
        {
            newSL = NormalizeDouble(newSL, digits);
            if (!trade.PositionModify(posInfo.Ticket(), newSL, currentTP))
                Print("SL modify failed: ", trade.ResultRetcode(),
                      " - ", trade.ResultRetcodeDescription());
        }
    }
}

//+------------------------------------------------------------------+
//| Open a BUY order                                                 |
//+------------------------------------------------------------------+
bool OpenBuy(double sl, double tp)
{
    double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
    sl = NormalizeDouble(sl, digits);
    tp = NormalizeDouble(tp, digits);

    if (!trade.Buy(LotSize, _Symbol, ask, sl, tp,
                   "RUDY EA - BUY"))
    {
        Print("Buy failed: ", trade.ResultRetcode(),
              " - ", trade.ResultRetcodeDescription());
        return false;
    }
    return true;
}

//+------------------------------------------------------------------+
//| Open a SELL order                                                 |
//+------------------------------------------------------------------+
bool OpenSell(double sl, double tp)
{
    double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
    sl = NormalizeDouble(sl, digits);
    tp = NormalizeDouble(tp, digits);

    if (!trade.Sell(LotSize, _Symbol, bid, sl, tp,
                    "RUDY EA - SELL"))
    {
        Print("Sell failed: ", trade.ResultRetcode(),
              " - ", trade.ResultRetcodeDescription());
        return false;
    }
    return true;
}

//+------------------------------------------------------------------+
//| Count open positions managed by this EA                          |
//+------------------------------------------------------------------+
int CountOpenPositions()
{
    int count = 0;
    for (int i = PositionsTotal() - 1; i >= 0; i--)
    {
        if (posInfo.SelectByIndex(i) &&
            posInfo.Magic()  == MagicNumber &&
            posInfo.Symbol() == _Symbol)
            count++;
    }
    return count;
}

//+------------------------------------------------------------------+
//| Get pivot high: highest high in [lookback+1 .. 2*lookback] vs    |
//| the pivot bar (bar index lookback)                               |
//+------------------------------------------------------------------+
double GetPivotHigh(int len)
{
    double pivotHigh = iHigh(_Symbol, PERIOD_CURRENT, len);
    for (int i = 1; i <= len; i++)
    {
        if (iHigh(_Symbol, PERIOD_CURRENT, i) > pivotHigh ||
            iHigh(_Symbol, PERIOD_CURRENT, len + i) > pivotHigh)
            return 0; // not a pivot high
    }
    return pivotHigh;
}

//+------------------------------------------------------------------+
//| Get pivot low                                                     |
//+------------------------------------------------------------------+
double GetPivotLow(int len)
{
    double pivotLow = iLow(_Symbol, PERIOD_CURRENT, len);
    for (int i = 1; i <= len; i++)
    {
        if (iLow(_Symbol, PERIOD_CURRENT, i) < pivotLow ||
            iLow(_Symbol, PERIOD_CURRENT, len + i) < pivotLow)
            return 0;
    }
    return pivotLow;
}

//+------------------------------------------------------------------+
//| Check if structure type passes the OB filter                     |
//+------------------------------------------------------------------+
bool FilterPass(string structType)
{
    if (OB_Filter == "None")   return true;
    if (OB_Filter == structType) return true;
    return false;
}

//+------------------------------------------------------------------+
//| Add an internal order block zone                                 |
//+------------------------------------------------------------------+
void AddInternalOB(bool bull)
{
    OBZone ob;
    int bar = InternalLookback;

    double h = iHigh (_Symbol, PERIOD_CURRENT, bar);
    double l = iLow  (_Symbol, PERIOD_CURRENT, bar);
    double mid = (h + l) / 2.0;

    ob.top     = (OB_Mitigation == "Middle") ? mid : h;
    ob.bottom  = (OB_Mitigation == "Middle") ? mid : l;
    ob.mid     = mid;
    ob.bull    = bull;
    ob.active  = true;
    ob.created = TimeCurrent();

    int sz = ArraySize(InternalOBs);
    ArrayResize(InternalOBs, sz + 1);
    InternalOBs[sz] = ob;

    DrawOBBox(ob, sz);
}

//+------------------------------------------------------------------+
//| Draw a structure break line on the chart                         |
//+------------------------------------------------------------------+
void DrawStructureLine(bool bull, string label, int barIdx, double price)
{
    string name = "RUDY_STR_" + label + "_" + IntegerToString(TimeCurrent());
    color  clr  = bull ? BullishColor : BearishColor;
    int    fromBar = barIdx + 5; // small padding left

    ObjectCreate(0, name, OBJ_TREND, 0,
                 iTime(_Symbol, PERIOD_CURRENT, fromBar), price,
                 iTime(_Symbol, PERIOD_CURRENT, 0),       price);
    ObjectSetInteger(0, name, OBJPROP_COLOR,   clr);
    ObjectSetInteger(0, name, OBJPROP_STYLE,   bull ? STYLE_SOLID : STYLE_DASH);
    ObjectSetInteger(0, name, OBJPROP_WIDTH,   1);
    ObjectSetInteger(0, name, OBJPROP_RAY_RIGHT, false);

    string lblName = name + "_LBL";
    ObjectCreate(0, lblName, OBJ_TEXT, 0,
                 iTime(_Symbol, PERIOD_CURRENT, 0), price);
    ObjectSetString (0, lblName, OBJPROP_TEXT,      label);
    ObjectSetInteger(0, lblName, OBJPROP_COLOR,     clr);
    ObjectSetInteger(0, lblName, OBJPROP_FONTSIZE,  8);
}

//+------------------------------------------------------------------+
//| Draw an Order Block rectangle                                    |
//+------------------------------------------------------------------+
void DrawOBBox(OBZone &ob, int idx)
{
    string name = "RUDY_OB_" + (ob.bull ? "B" : "S") +
                  "_" + IntegerToString(idx) + "_" + IntegerToString(ob.created);
    color  clr  = ob.bull ? OB_BullColor : OB_BearColor;

    ObjectCreate(0, name, OBJ_RECTANGLE, 0,
                 ob.created, ob.top,
                 TimeCurrent() + PeriodSeconds() * 50, ob.bottom);
    ObjectSetInteger(0, name, OBJPROP_COLOR,   clr);
    ObjectSetInteger(0, name, OBJPROP_BGCOLOR, clr);
    ObjectSetInteger(0, name, OBJPROP_BACK,    true);
    ObjectSetInteger(0, name, OBJPROP_FILL,    true);
    ObjectSetInteger(0, name, OBJPROP_STYLE,   STYLE_SOLID);

    // Mid-line
    string mlName = name + "_MID";
    ObjectCreate(0, mlName, OBJ_TREND, 0,
                 ob.created, ob.mid,
                 TimeCurrent() + PeriodSeconds() * 50, ob.mid);
    ObjectSetInteger(0, mlName, OBJPROP_COLOR, clr);
    ObjectSetInteger(0, mlName, OBJPROP_STYLE, STYLE_DASH);
    ObjectSetInteger(0, mlName, OBJPROP_WIDTH, 1);
}

//+------------------------------------------------------------------+
//| Draw a Fair Value Gap rectangle                                  |
//+------------------------------------------------------------------+
void DrawFVGBox(FVGZone &fvg, int idx)
{
    string name = "RUDY_FVG_" + (fvg.bull ? "B" : "S") +
                  "_" + IntegerToString(idx) + "_" + IntegerToString(fvg.created);
    color clr = fvg.bull ? OB_BullColor : OB_BearColor;

    ObjectCreate(0, name, OBJ_RECTANGLE, 0,
                 fvg.created, fvg.top,
                 TimeCurrent() + PeriodSeconds() * FVG_ExtendBars, fvg.bottom);
    ObjectSetInteger(0, name, OBJPROP_COLOR,   clr);
    ObjectSetInteger(0, name, OBJPROP_BGCOLOR, clr);
    ObjectSetInteger(0, name, OBJPROP_BACK,    true);
    ObjectSetInteger(0, name, OBJPROP_FILL,    true);
}
//+------------------------------------------------------------------+
//| END OF FILE                                                      |
//+------------------------------------------------------------------+
