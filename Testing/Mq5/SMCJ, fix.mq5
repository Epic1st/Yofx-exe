//+------------------------------------------------------------------+
//|                                                          SMC.mq5 |
//|                              Smart Money Concepts Trading System |
//+------------------------------------------------------------------+
#property copyright "SMC v2.0"
#property version   "2.00"
#property strict

#include <Trade\Trade.mqh>
CTrade trade;

#define MAGIC_NUMBER 200001
#define EXPIRY_DATE D'2026.03.10'

//=============================================================================
// INPUTS
//=============================================================================
input group "=== POSITIONS ==="
input double Lot_P1 = 0.01;
input double Lot_P2 = 0.01;
input double Lot_P3 = 0.03;
input double Lot_P4 = 0.03;

input group "=== TREND SYSTEM ==="
input double Sensitivity = 1.0;
input int ATR_Period = 10;
input int Factor = 7;

input group "=== MOVING AVERAGES ==="
input int MA_Fast = 8;
input int MA_Slow = 9;

input group "=== RISK ==="
input double Risk = 3.0;
input int ATR_SL = 14;
input double TP1 = 1.0;
input double TP2 = 2.0;
input double TP3 = 3.0;

input group "=== DISPLAY ==="
input bool ShowPanel = true;

//=============================================================================
// GLOBALS
//=============================================================================
double prevTrend = 0, currTrend = 0;
int trendDir = 0;
double ma1 = 0, ma2 = 0;
double prevClose = 0;
datetime lastTime = 0;
int trades = 0, wins = 0, losses = 0;
string action = "Waiting";
bool expired = false;

static double sPrevUpper = 0;
static double sPrevLower = 0;

//=============================================================================
// INIT
//=============================================================================
int OnInit()
{
    if(TimeCurrent() > EXPIRY_DATE)
    {
        expired = true;
        Alert("License Expired");
        Comment("LICENSE EXPIRED\n" + TimeToString(EXPIRY_DATE, TIME_DATE));
        return INIT_FAILED;
    }
    
    trade.SetExpertMagicNumber(MAGIC_NUMBER);
    trade.SetDeviationInPoints(10);
    trade.SetTypeFilling(ORDER_FILLING_FOK);
    trade.SetAsyncMode(false);
    
    sPrevUpper = 0;
    sPrevLower = 0;
    prevTrend = 0;
    currTrend = 0;
    
    Print("SMC v2.0 Started | License: ", TimeToString(EXPIRY_DATE, TIME_DATE));
    
    return INIT_SUCCEEDED;
}

//=============================================================================
// DEINIT
//=============================================================================
void OnDeinit(const int reason)
{
    Comment("");
    Print("SMC Stopped | Trades: ", trades, " | Wins: ", wins, " | Losses: ", losses);
}

//=============================================================================
// TICK
//=============================================================================
void OnTick()
{
    if(TimeCurrent() > EXPIRY_DATE)
    {
        if(!expired)
        {
            expired = true;
            CloseAll();
            Comment("LICENSE EXPIRED");
        }
        return;
    }
    
    MqlRates r[];
    ArraySetAsSeries(r, true);
    if(CopyRates(_Symbol, PERIOD_CURRENT, 0, 2, r) < 2) return;
    
    double close = r[0].close;
    prevClose = r[1].close;
    
    if(!UpdateTrend()) return;
    if(!UpdateMAs()) return;
    
    if(CountPos() == 0)
        CheckSignals(close);
    
    if(ShowPanel)
        DrawPanel();
}

//=============================================================================
// TREND
//=============================================================================
bool UpdateTrend()
{
    double atr[];
    ArraySetAsSeries(atr, true);
    int h = iATR(_Symbol, PERIOD_CURRENT, ATR_Period);
    if(h == INVALID_HANDLE) return false;
    if(CopyBuffer(h, 0, 0, 3, atr) < 3) return false;
    
    double atrVal = atr[0];
    double mult = Sensitivity * Factor;
    
    MqlRates r[];
    ArraySetAsSeries(r, true);
    if(CopyRates(_Symbol, PERIOD_CURRENT, 0, 3, r) < 3) return false;
    
    double c = r[0].close;
    double pc = r[1].close;
    
    double upper = c + mult * atrVal;
    double lower = c - mult * atrVal;
    
    if(sPrevLower == 0) sPrevLower = lower;
    if(sPrevUpper == 0) sPrevUpper = upper;
    
    lower = (lower > sPrevLower || pc < sPrevLower) ? lower : sPrevLower;
    upper = (upper < sPrevUpper || pc > sPrevUpper) ? upper : sPrevUpper;
    
    prevTrend = currTrend;
    
    if(prevTrend == sPrevUpper)
        trendDir = c > upper ? -1 : 1;
    else
        trendDir = c < lower ? 1 : -1;
    
    currTrend = (trendDir == -1) ? lower : upper;
    
    sPrevUpper = upper;
    sPrevLower = lower;
    
    return true;
}

//=============================================================================
// MAs
//=============================================================================
bool UpdateMAs()
{
    double m1[], m2[];
    ArraySetAsSeries(m1, true);
    ArraySetAsSeries(m2, true);
    
    int h1 = iMA(_Symbol, PERIOD_CURRENT, MA_Fast, 0, MODE_SMA, PRICE_CLOSE);
    int h2 = iMA(_Symbol, PERIOD_CURRENT, MA_Slow, 0, MODE_SMA, PRICE_CLOSE);
    
    if(h1 == INVALID_HANDLE || h2 == INVALID_HANDLE) return false;
    if(CopyBuffer(h1, 0, 0, 1, m1) < 1) return false;
    if(CopyBuffer(h2, 0, 0, 1, m2) < 1) return false;
    
    ma1 = m1[0];
    ma2 = m2[0];
    
    return true;
}

//=============================================================================
// SIGNALS
//=============================================================================
void CheckSignals(double close)
{
    datetime t = iTime(_Symbol, PERIOD_CURRENT, 0);
    if(t == lastTime) return;
    
    bool buy = (close > currTrend && prevClose <= prevTrend);
    bool sell = (close < currTrend && prevClose >= prevTrend);
    
    if(buy)
    {
        Print("BUY | MA: ", ma1 >= ma2 ? "OK" : "NO");
        if(Open(ORDER_TYPE_BUY))
        {
            action = "BUY";
            lastTime = t;
        }
    }
    else if(sell)
    {
        Print("SELL | MA: ", ma1 <= ma2 ? "OK" : "NO");
        if(Open(ORDER_TYPE_SELL))
        {
            action = "SELL";
            lastTime = t;
        }
    }
}

//=============================================================================
// OPEN
//=============================================================================
bool Open(ENUM_ORDER_TYPE type)
{
    double p = (type == ORDER_TYPE_BUY) ? 
        SymbolInfoDouble(_Symbol, SYMBOL_ASK) : 
        SymbolInfoDouble(_Symbol, SYMBOL_BID);
    
    int d = (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);
    
    double atr[];
    ArraySetAsSeries(atr, true);
    int h = iATR(_Symbol, PERIOD_CURRENT, ATR_SL);
    if(h == INVALID_HANDLE) return false;
    if(CopyBuffer(h, 0, 0, 1, atr) < 1) return false;
    
    double band = atr[0] * Risk;
    
    double sl, t1, t2, t3;
    
    if(type == ORDER_TYPE_BUY)
    {
        sl = NormalizeDouble(p - band, d);
        double dist = p - sl;
        t1 = NormalizeDouble(p + dist * TP1, d);
        t2 = NormalizeDouble(p + dist * TP2, d);
        t3 = NormalizeDouble(p + dist * TP3, d);
    }
    else
    {
        sl = NormalizeDouble(p + band, d);
        double dist = sl - p;
        t1 = NormalizeDouble(p - dist * TP1, d);
        t2 = NormalizeDouble(p - dist * TP2, d);
        t3 = NormalizeDouble(p - dist * TP3, d);
    }
    
    int cnt = 0;
    
    if(trade.PositionOpen(_Symbol, type, Lot_P1, p, sl, t1, "P1")) cnt++;
    if(trade.PositionOpen(_Symbol, type, Lot_P2, p, sl, t2, "P2")) cnt++;
    if(trade.PositionOpen(_Symbol, type, Lot_P3, p, sl, t3, "P3")) cnt++;
    if(trade.PositionOpen(_Symbol, type, Lot_P4, p, sl, t3, "P4")) cnt++;
    
    if(cnt > 0)
    {
        trades++;
        Print("Opened ", cnt, "/4");
        return true;
    }
    
    return false;
}

//=============================================================================
// COUNT
//=============================================================================
int CountPos()
{
    int c = 0;
    for(int i = PositionsTotal() - 1; i >= 0; i--)
    {
        ulong t = PositionGetTicket(i);
        if(PositionSelectByTicket(t))
            if(PositionGetString(POSITION_SYMBOL) == _Symbol)
                if(PositionGetInteger(POSITION_MAGIC) == MAGIC_NUMBER)
                    c++;
    }
    return c;
}

//=============================================================================
// CLOSE
//=============================================================================
void CloseAll()
{
    for(int i = PositionsTotal() - 1; i >= 0; i--)
    {
        ulong t = PositionGetTicket(i);
        if(PositionSelectByTicket(t))
            if(PositionGetString(POSITION_SYMBOL) == _Symbol)
                if(PositionGetInteger(POSITION_MAGIC) == MAGIC_NUMBER)
                    trade.PositionClose(t);
    }
}

//=============================================================================
// PANEL
//=============================================================================
void DrawPanel()
{
    int d = (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);
    double price = SymbolInfoDouble(_Symbol, SYMBOL_BID);
    
    datetime left = EXPIRY_DATE - TimeCurrent();
    int days = (int)(left / 86400);
    
    string s = "\n";
    s += "══════════════════════════════\n";
    s += " SMC v2.0\n";
    s += "══════════════════════════════\n";
    s += "License: " + IntegerToString(days) + " days\n\n";
    s += "Price: " + DoubleToString(price, d) + "\n";
    s += "Trend: " + (trendDir == 1 ? "BULL" : "BEAR") + "\n";
    s += "MA: " + (ma1 >= ma2 ? "BULL" : "BEAR") + "\n\n";
    s += "Positions: " + IntegerToString(CountPos()) + "\n";
    s += "Trades: " + IntegerToString(trades) + "\n";
    if(trades > 0)
        s += "Win%: " + DoubleToString(100.0 * wins / trades, 1) + "\n";
    s += "\n" + action + "\n";
    s += "══════════════════════════════\n";
    
    Comment(s);
}
//+------------------------------------------------------------------+
