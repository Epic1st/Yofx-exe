//+------------------------------------------------------------------+
//|                                    MeffsMomentumBurst_v8.mq5    |
//|                   Meff's Trading System — XAUUSD M5             |
//|         Momentum Burst Strategy with Recovery & Daily P&L       |
//|         Version 8.10 | github: Meff                             |
//+------------------------------------------------------------------+
#property copyright "Meff's Trading System"
#property link      ""
#property version   "8.10"
#property strict

#include <Trade\Trade.mqh>
#include <Trade\OrderInfo.mqh>
#include <Trade\HistoryOrderInfo.mqh>
#include <Trade\DealInfo.mqh>

CTrade trade;

//+------------------------------------------------------------------+
//| Input Parameters                                                  |
//+------------------------------------------------------------------+
input string   Section1            = "=== Strategy Parameters ===";
input int      EMA_Fast            = 3;
input int      Burst_Bars          = 2;
input double   Burst_Threshold     = 0.3;
input int      ATR_Period          = 13;

input string   Section2            = "=== Risk Management ===";
input double   SL_ATR_Mult         = 0.5;
input double   TP_ATR_Mult         = 1.2;
input double   Trail_Activation    = 0.2;
input double   Trail_Distance      = 0.2;
input int      Cooldown_Bars       = 1;
input double   LotSize             = 0.1;
input int      MagicNumber         = 48382929;

input string   Section3            = "=== Trading Session ===";
input int      Session_Start       = 8;
input int      Session_End         = 20;
input bool     Use_Session_Filter  = false;

input string   Section4            = "=== Additional ===";
input int      Max_Spread_Points   = 30;
input int      Slippage            = 3;

input string   Section5            = "=== Recovery ===";
input bool     Use_Recovery        = true;
input double   Recovery_MaxLot     = 0.2;
input double   Recovery_MinProfit  = 1.0;
input int      Recovery_MaxAttempts = 10;

input string   Section6            = "=== Trailing Daily P&L ===";
input bool     Use_DailyPnL        = false;
input double   MaxDailyLoss        = 200.0;
input double   MaxDailyProfit      = 200.0;
input double   DailyTrailStep      = 50.0;

//+------------------------------------------------------------------+
//| Indicator Handles                                                 |
//+------------------------------------------------------------------+
int    hATR  = INVALID_HANDLE;
int    hEMA  = INVALID_HANDLE;

//+------------------------------------------------------------------+
//| Global Variables                                                  |
//+------------------------------------------------------------------+
double   ATR_Value;
double   EMA_Fast_Value;
int      LastSignalBar     = 0;

double   RecoveryDebt      = 0.0;
int      RecoveryCount     = 0;
bool     InRecoveryMode    = false;
ulong    LastClosedDeal    = 0;

// Daily P&L state
double   DailyPeakPnL      = 0.0;
double   DailyFloor        = 0.0;
bool     DailyTrailActive  = false;
bool     DailyPausedToday  = false;
datetime LastDayReset      = 0;

//+------------------------------------------------------------------+
//| OnInit                                                            |
//+------------------------------------------------------------------+
int OnInit()
{
    trade.SetExpertMagicNumber(MagicNumber);
    trade.SetDeviationInPoints(Slippage);
    trade.SetTypeFilling(ORDER_FILLING_FOK);

    hATR = iATR(_Symbol, PERIOD_M5, ATR_Period);
    hEMA = iMA(_Symbol,  PERIOD_M5, EMA_Fast, 0, MODE_EMA, PRICE_CLOSE);

    if(hATR == INVALID_HANDLE || hEMA == INVALID_HANDLE)
    {
        Print("ERROR: Indicator handle creation failed!");
        return INIT_FAILED;
    }

    RecoveryDebt    = 0.0;
    RecoveryCount   = 0;
    InRecoveryMode  = false;

    InitDayTracking();

    Print("=== Meff's MomentumBurst v8.10 — XAUUSD M5 (MQL5) ===");
    Print("Base Lot:           ", LotSize);
    Print("Recovery Max Lot:   ", Recovery_MaxLot);
    Print("SL = ", SL_ATR_Mult, " ATR  |  TP = ", TP_ATR_Mult, " ATR");
    Print("Max Daily Loss:     $", MaxDailyLoss);
    Print("Max Daily Profit:   $", MaxDailyProfit, " (trailing activates above this)");
    Print("Daily Trail Step:   $", DailyTrailStep);
    return INIT_SUCCEEDED;
}

//+------------------------------------------------------------------+
//| OnDeinit                                                          |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
    if(hATR != INVALID_HANDLE) IndicatorRelease(hATR);
    if(hEMA != INVALID_HANDLE) IndicatorRelease(hEMA);
    Comment("");
}

//+------------------------------------------------------------------+
//| InitDayTracking                                                   |
//+------------------------------------------------------------------+
void InitDayTracking()
{
    DailyPeakPnL     = 0.0;
    DailyFloor       = -MaxDailyLoss;
    DailyTrailActive = false;
    DailyPausedToday = false;
    LastDayReset     = TimeCurrent();
    Print("Day reset. Daily Floor = $", DoubleToString(DailyFloor, 2));
}

//+------------------------------------------------------------------+
//| IsNewDay                                                          |
//+------------------------------------------------------------------+
bool IsNewDay()
{
    MqlDateTime dtNow, dtLast;
    TimeToStruct(TimeCurrent(), dtNow);
    TimeToStruct(LastDayReset,  dtLast);
    return (dtNow.day  != dtLast.day  ||
            dtNow.mon  != dtLast.mon  ||
            dtNow.year != dtLast.year);
}

//+------------------------------------------------------------------+
//| GetDayStart                                                       |
//+------------------------------------------------------------------+
datetime GetDayStart()
{
    MqlDateTime dt;
    TimeToStruct(TimeCurrent(), dt);
    dt.hour = 0; dt.min = 0; dt.sec = 0;
    return StructToTime(dt);
}

//+------------------------------------------------------------------+
//| GetDailyPnL — realized (today) + open floating P&L              |
//+------------------------------------------------------------------+
double GetDailyPnL()
{
    double realized  = 0.0;
    datetime dayStart = GetDayStart();

    if(!HistorySelect(dayStart, TimeCurrent()))
        return 0.0;

    int totalDeals = HistoryDealsTotal();
    for(int i = totalDeals - 1; i >= 0; i--)
    {
        ulong ticket = HistoryDealGetTicket(i);
        if(ticket == 0) continue;
        if(HistoryDealGetString(ticket, DEAL_SYMBOL)     != _Symbol)    continue;
        if((int)HistoryDealGetInteger(ticket, DEAL_MAGIC) != MagicNumber) continue;
        ENUM_DEAL_ENTRY entry = (ENUM_DEAL_ENTRY)HistoryDealGetInteger(ticket, DEAL_ENTRY);
        if(entry != DEAL_ENTRY_OUT && entry != DEAL_ENTRY_INOUT) continue;

        realized += HistoryDealGetDouble(ticket, DEAL_PROFIT)
                  + HistoryDealGetDouble(ticket, DEAL_SWAP)
                  + HistoryDealGetDouble(ticket, DEAL_COMMISSION);
    }

    double floating = 0.0;
    int totalPos = PositionsTotal();
    for(int i = 0; i < totalPos; i++)
    {
        ulong ticket = PositionGetTicket(i);
        if(ticket == 0) continue;
        if(PositionGetString(POSITION_SYMBOL)          != _Symbol)    continue;
        if((int)PositionGetInteger(POSITION_MAGIC)      != MagicNumber) continue;
        floating += PositionGetDouble(POSITION_PROFIT)
                  + PositionGetDouble(POSITION_SWAP);
    }

    return realized + floating;
}

//+------------------------------------------------------------------+
//| CloseAllPositions                                                 |
//+------------------------------------------------------------------+
void CloseAllPositions(string reason)
{
    Print("=== CLOSE ALL: ", reason, " ===");
    for(int i = PositionsTotal() - 1; i >= 0; i--)
    {
        ulong ticket = PositionGetTicket(i);
        if(ticket == 0) continue;
        if(PositionGetString(POSITION_SYMBOL)          != _Symbol)    continue;
        if((int)PositionGetInteger(POSITION_MAGIC)      != MagicNumber) continue;

        if(trade.PositionClose(ticket))
            Print("Closed #", ticket, " — reason: ", reason);
        else
            Print("CloseAll error #", ticket, ": ", GetLastError());
    }
}

//+------------------------------------------------------------------+
//| CheckDailyPnL                                                     |
//+------------------------------------------------------------------+
bool CheckDailyPnL()
{
    if(!Use_DailyPnL) return true;

    if(IsNewDay()) InitDayTracking();

    if(DailyPausedToday) return false;

    double pnl = GetDailyPnL();

    if(pnl > DailyPeakPnL)
    {
        DailyPeakPnL = pnl;

        if(DailyPeakPnL >= MaxDailyProfit)
        {
            DailyTrailActive = true;
            double newFloor  = DailyPeakPnL - DailyTrailStep;
            if(newFloor > DailyFloor)
            {
                DailyFloor = newFloor;
                Print("Daily trail floor raised to: $", DoubleToString(DailyFloor, 2),
                      " (Peak = $", DoubleToString(DailyPeakPnL, 2), ")");
            }
        }
    }

    if(pnl <= -MaxDailyLoss)
    {
        CloseAllPositions("MAX DAILY LOSS $" + DoubleToString(MathAbs(pnl), 2));
        DailyPausedToday = true;
        return false;
    }

    if(DailyTrailActive && pnl <= DailyFloor)
    {
        CloseAllPositions("DAILY TRAIL FLOOR $" + DoubleToString(DailyFloor, 2));
        DailyPausedToday = true;
        return false;
    }

    return true;
}

//+------------------------------------------------------------------+
//| OnTick                                                            |
//+------------------------------------------------------------------+
void OnTick()
{
    static datetime prevBarTime = 0;
    datetime curBarTime = iTime(_Symbol, PERIOD_M5, 0);
    bool isNewBar = (curBarTime != prevBarTime);

    UpdateIndicators();
    ManageTrailingStop();
    if(Use_Recovery) CheckClosedTrades();

    bool dailyOK = CheckDailyPnL();
    ShowDashboard();

    if(!dailyOK) return;

    if(!isNewBar) return;
    prevBarTime = curBarTime;

    // Spread check — MQL5: SymbolInfoInteger returns spread in points
    long spreadNow = SymbolInfoInteger(_Symbol, SYMBOL_SPREAD);
    if(spreadNow > Max_Spread_Points) return;

    if(Use_Session_Filter && !IsInSession()) return;
    if(!CheckCooldown())                     return;
    if(CountOpenPositions() > 0)             return;

    int signal = GenerateSignal();
    if(signal == 0) return;

    double tradeLot = LotSize;
    if(Use_Recovery && InRecoveryMode && RecoveryDebt > 0)
    {
        if(RecoveryCount >= Recovery_MaxAttempts)
        {
            Print("Max recovery attempts reached — resetting to base lot");
            RecoveryDebt    = 0;
            InRecoveryMode  = false;
            RecoveryCount   = 0;
        }
        else
        {
            tradeLot = CalcRecoveryLot();
            Print("RECOVERY: Debt = $", DoubleToString(RecoveryDebt, 2),
                  " | Lot = ", DoubleToString(tradeLot, 2),
                  " | Attempt #", RecoveryCount + 1);
        }
    }

    if(signal ==  1) { OpenBuyPosition(tradeLot);  LastSignalBar = Bars(_Symbol, PERIOD_M5); }
    if(signal == -1) { OpenSellPosition(tradeLot); LastSignalBar = Bars(_Symbol, PERIOD_M5); }

    ShowDashboard();
}

//+------------------------------------------------------------------+
//| Update Indicators                                                 |
//+------------------------------------------------------------------+
void UpdateIndicators()
{
    double atrBuf[1], emaBuf[1];

    if(CopyBuffer(hATR, 0, 1, 1, atrBuf) == 1)
        ATR_Value = atrBuf[0];

    if(CopyBuffer(hEMA, 0, 1, 1, emaBuf) == 1)
        EMA_Fast_Value = emaBuf[0];
}

//+------------------------------------------------------------------+
//| Signal Generation                                                 |
//+------------------------------------------------------------------+
int GenerateSignal()
{
    if(ATR_Value <= 0) return 0;

    double close[];
    ArraySetAsSeries(close, true);
    if(CopyClose(_Symbol, PERIOD_M5, 0, 3 + Burst_Bars, close) < 3 + Burst_Bars)
        return 0;

    // close[0]=current, close[1]=prev closed bar, close[1+Burst_Bars]=N bars back
    double priceMove = close[1] - close[1 + Burst_Bars];
    double moveInATR = priceMove / ATR_Value;

    if(moveInATR > Burst_Threshold)
        if(close[1] > EMA_Fast_Value && close[1] > close[2])
            return 1;

    if(moveInATR < -Burst_Threshold)
        if(close[1] < EMA_Fast_Value && close[1] < close[2])
            return -1;

    return 0;
}

//+------------------------------------------------------------------+
//| Open Buy Position                                                 |
//+------------------------------------------------------------------+
void OpenBuyPosition(double lot)
{
    double entry   = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
    double point   = SymbolInfoDouble(_Symbol, SYMBOL_POINT);
    long   digits  = SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);
    double minStop = SymbolInfoInteger(_Symbol, SYMBOL_TRADE_STOPS_LEVEL) * point;

    double sl = NormalizeDouble(entry - ATR_Value * SL_ATR_Mult, (int)digits);
    double tp = NormalizeDouble(entry + ATR_Value * TP_ATR_Mult, (int)digits);

    if(entry - sl < minStop) sl = NormalizeDouble(entry - minStop, (int)digits);
    if(tp - entry < minStop) tp = NormalizeDouble(entry + minStop, (int)digits);

    string comment = InRecoveryMode ? "Meff_MomBurst BUY [REC]" : "Meff_MomBurst BUY";

    if(trade.Buy(lot, _Symbol, entry, sl, tp, comment))
        Print("BUY #", trade.ResultOrder(),
              " | Lot=", lot,
              " | SL=", DoubleToString(sl, (int)digits),
              " | TP=", DoubleToString(tp, (int)digits));
    else
        Print("BUY error: ", trade.ResultRetcodeDescription());
}

//+------------------------------------------------------------------+
//| Open Sell Position                                                |
//+------------------------------------------------------------------+
void OpenSellPosition(double lot)
{
    double entry   = SymbolInfoDouble(_Symbol, SYMBOL_BID);
    double point   = SymbolInfoDouble(_Symbol, SYMBOL_POINT);
    long   digits  = SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);
    double minStop = SymbolInfoInteger(_Symbol, SYMBOL_TRADE_STOPS_LEVEL) * point;

    double sl = NormalizeDouble(entry + ATR_Value * SL_ATR_Mult, (int)digits);
    double tp = NormalizeDouble(entry - ATR_Value * TP_ATR_Mult, (int)digits);

    if(sl - entry < minStop) sl = NormalizeDouble(entry + minStop, (int)digits);
    if(entry - tp < minStop) tp = NormalizeDouble(entry - minStop, (int)digits);

    string comment = InRecoveryMode ? "Meff_MomBurst SELL [REC]" : "Meff_MomBurst SELL";

    if(trade.Sell(lot, _Symbol, entry, sl, tp, comment))
        Print("SELL #", trade.ResultOrder(),
              " | Lot=", lot,
              " | SL=", DoubleToString(sl, (int)digits),
              " | TP=", DoubleToString(tp, (int)digits));
    else
        Print("SELL error: ", trade.ResultRetcodeDescription());
}

//+------------------------------------------------------------------+
//| Trailing Stop Management                                          |
//+------------------------------------------------------------------+
void ManageTrailingStop()
{
    double curATRBuf[1];
    if(CopyBuffer(hATR, 0, 0, 1, curATRBuf) != 1) return;
    double curATR  = curATRBuf[0];
    if(curATR <= 0) return;

    double trail   = Trail_Distance * curATR;
    double point   = SymbolInfoDouble(_Symbol, SYMBOL_POINT);
    long   digits  = SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);
    double minStop = SymbolInfoInteger(_Symbol, SYMBOL_TRADE_STOPS_LEVEL) * point;

    for(int i = PositionsTotal() - 1; i >= 0; i--)
    {
        ulong ticket = PositionGetTicket(i);
        if(ticket == 0) continue;
        if(PositionGetString(POSITION_SYMBOL)          != _Symbol)    continue;
        if((int)PositionGetInteger(POSITION_MAGIC)      != MagicNumber) continue;

        double openPrice = PositionGetDouble(POSITION_PRICE_OPEN);
        double curSL     = PositionGetDouble(POSITION_SL);
        double curTP     = PositionGetDouble(POSITION_TP);
        ENUM_POSITION_TYPE type = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);

        if(type == POSITION_TYPE_BUY)
        {
            double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
            if((bid - openPrice) / curATR >= Trail_Activation)
            {
                double newSL = NormalizeDouble(bid - trail, (int)digits);
                if((newSL > curSL || curSL == 0) && (bid - newSL >= minStop))
                    trade.PositionModify(ticket, newSL, curTP);
            }
        }
        else if(type == POSITION_TYPE_SELL)
        {
            double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
            if((openPrice - ask) / curATR >= Trail_Activation)
            {
                double newSL = NormalizeDouble(ask + trail, (int)digits);
                if((newSL < curSL || curSL == 0) && (newSL - ask >= minStop))
                    trade.PositionModify(ticket, newSL, curTP);
            }
        }
    }
}

//+------------------------------------------------------------------+
//| Calculate Recovery Lot                                            |
//+------------------------------------------------------------------+
double CalcRecoveryLot()
{
    double tpDist      = TP_ATR_Mult * ATR_Value;
    double tickVal     = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE);
    double tickSize    = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
    double profitPer01 = (tpDist / tickSize) * tickVal * 0.1;

    if(profitPer01 <= 0) return LotSize;

    double needed  = RecoveryDebt + Recovery_MinProfit;
    double rawLot  = (needed / profitPer01) * 0.1;

    double step    = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
    double minLot  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
    rawLot = MathCeil(rawLot / step) * step;
    rawLot = MathMax(rawLot, minLot);
    rawLot = MathMax(rawLot, LotSize);
    rawLot = MathMin(rawLot, Recovery_MaxLot);

    return NormalizeDouble(rawLot, 2);
}

//+------------------------------------------------------------------+
//| Check Closed Trades for Recovery Logic                           |
//| In MQL5 we scan deal history (DEAL_ENTRY_OUT = closed position)  |
//+------------------------------------------------------------------+
void CheckClosedTrades()
{
    datetime dayStart = iTime(_Symbol, PERIOD_D1, 0);
    // Widen history a bit so we catch recent closes
    if(!HistorySelect(TimeCurrent() - 7 * 86400, TimeCurrent()))
        return;

    int total = HistoryDealsTotal();
    for(int i = total - 1; i >= 0; i--)
    {
        ulong ticket = HistoryDealGetTicket(i);
        if(ticket == 0) continue;
        if(HistoryDealGetString(ticket, DEAL_SYMBOL)       != _Symbol)    continue;
        if((int)HistoryDealGetInteger(ticket, DEAL_MAGIC)  != MagicNumber) continue;
        if(ticket == LastClosedDeal) break;

        ENUM_DEAL_ENTRY entry = (ENUM_DEAL_ENTRY)HistoryDealGetInteger(ticket, DEAL_ENTRY);
        if(entry != DEAL_ENTRY_OUT && entry != DEAL_ENTRY_INOUT) continue;

        LastClosedDeal = ticket;
        double profit  = HistoryDealGetDouble(ticket, DEAL_PROFIT)
                       + HistoryDealGetDouble(ticket, DEAL_SWAP)
                       + HistoryDealGetDouble(ticket, DEAL_COMMISSION);

        if(profit < 0)
        {
            RecoveryDebt   += MathAbs(profit);
            InRecoveryMode  = true;
            RecoveryCount++;
            Print("LOSS: $", DoubleToString(profit, 2),
                  " | Debt: $", DoubleToString(RecoveryDebt, 2),
                  " | Attempt: ", RecoveryCount);
        }
        else if(profit > 0 && InRecoveryMode)
        {
            RecoveryDebt -= profit;
            if(RecoveryDebt <= 0)
            {
                RecoveryDebt    = 0;
                InRecoveryMode  = false;
                RecoveryCount   = 0;
                Print("RECOVERY COMPLETE");
            }
        }
        break;
    }
}

//+------------------------------------------------------------------+
//| Helper Functions                                                  |
//+------------------------------------------------------------------+
bool IsInSession()
{
    MqlDateTime dt;
    TimeToStruct(TimeCurrent(), dt);
    int h = dt.hour;
    return (Session_Start < Session_End)
           ? (h >= Session_Start && h < Session_End)
           : (h >= Session_Start || h < Session_End);
}

bool CheckCooldown()
{
    if(LastSignalBar == 0) return true;
    return (Bars(_Symbol, PERIOD_M5) - LastSignalBar >= Cooldown_Bars);
}

int CountOpenPositions()
{
    int n = 0;
    for(int i = 0; i < PositionsTotal(); i++)
    {
        ulong ticket = PositionGetTicket(i);
        if(ticket == 0) continue;
        if(PositionGetString(POSITION_SYMBOL)          != _Symbol)    continue;
        if((int)PositionGetInteger(POSITION_MAGIC)      != MagicNumber) continue;
        n++;
    }
    return n;
}

//+------------------------------------------------------------------+
//| Dashboard                                                         |
//+------------------------------------------------------------------+
void ShowDashboard()
{
    double pnl    = Use_DailyPnL ? GetDailyPnL() : 0.0;
    string mode   = InRecoveryMode
                    ? "RECOVERY ($" + DoubleToString(RecoveryDebt, 2) + " open)"
                    : "NORMAL";
    double recLot = InRecoveryMode ? CalcRecoveryLot() : LotSize;

    string t  = "\n=== Meff's MomentumBurst v8.10 — XAUUSD M5 (MQL5) ===";
    t += "\nMode      : " + mode;
    t += "\nATR       : " + DoubleToString(ATR_Value, 2);
    t += "\nEMA("  + IntegerToString(EMA_Fast) + ")  : " + DoubleToString(EMA_Fast_Value, 2);
    t += "\nSession   : " + (IsInSession() ? "ACTIVE" : "INACTIVE");

    if(Use_DailyPnL)
    {
        t += "\n--- Trailing Daily P&L ---";
        t += "\nDaily P&L    : $" + DoubleToString(pnl, 2);
        t += "\nPeak today   : $" + DoubleToString(DailyPeakPnL, 2);
        t += "\nTrail Floor  : $" + DoubleToString(DailyFloor, 2);
        t += "\nTrail Mode   : " + (DailyTrailActive
                                    ? "ACTIVE"
                                    : "waiting for $" + DoubleToString(MaxDailyProfit, 0));
        t += "\nMax Loss     : -$" + DoubleToString(MaxDailyLoss, 2);
        t += "\nEA today     : " + (DailyPausedToday ? "PAUSED" : "ACTIVE");
    }

    t += "\n--- Parameters ---";
    t += "\nSL : " + DoubleToString(SL_ATR_Mult, 1) + " ATR";
    t += "\nTP : " + DoubleToString(TP_ATR_Mult, 1) + " ATR";
    t += "\n--- Recovery ---";
    t += "\nBase Lot      : " + DoubleToString(LotSize, 2);
    t += "\nNext Lot      : " + DoubleToString(recLot, 2);
    if(InRecoveryMode)
    {
        t += "\nDebt          : $" + DoubleToString(RecoveryDebt, 2);
        t += "\nAttempts      : " + IntegerToString(RecoveryCount)
              + " / " + IntegerToString(Recovery_MaxAttempts);
    }
    Comment(t);
}
//+------------------------------------------------------------------+
