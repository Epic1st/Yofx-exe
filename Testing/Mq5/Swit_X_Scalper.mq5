//+------------------------------------------------------------------+
//|                                          Swit_Scalper_EA.mq5    |
//|                                                                  |
//|  STRATEGY: VWAP Rejection + Break of Structure                  |
//|  INSTRUMENT: XAUUSD M15                                         |
//|                                                                  |
//|  BUY  - BOS bullish + price pulls back to VWAP                  |
//|         Rejection candle: low touches VWAP, close above VWAP    |
//|         Entry on same candle close | SL below low | TP = 2xSL   |
//|                                                                  |
//|  SELL - BOS bearish + price bounces to VWAP                     |
//|         Rejection candle: high touches VWAP, close below VWAP   |
//|         Entry on same candle close | SL above high | TP = 2xSL  |
//+------------------------------------------------------------------+
#property copyright "2026, Prof. Morris"
#property link      "https://t.me/profitabletrader2362"
#property version   "10.00"

#include <Trade\Trade.mqh>
#include <Trade\PositionInfo.mqh>

//--- Strategy
input int    InpBOS_Lookback   = 50;   // Bars to look back for BOS
input int    InpSwing_Strength = 3;    // Bars each side for swing point
input double InpVWAP_Buffer    = 1.5;  // Max distance below/above VWAP (ATR) for rejection
input int    InpATR_Period     = 14;   // ATR Period
//--- Risk
input double LotSize           = 0.01; // Lot Size
input double InpRR             = 2.0;  // Risk:Reward ratio
input double InpSL_Buffer      = 0.3;  // SL buffer beyond wick (ATR)
input int    Slippage          = 10;   // Slippage (points)
//--- Management
input int    CooldownBars      = 3;    // Bars to wait after trade closes
input bool   UseTrailingStop   = true; // Trailing stop
input double TrailStart_ATR    = 1.0;  // Start trailing after X ATR profit
input double TrailDist_ATR     = 1.0;  // Trail distance (ATR)
input double TrailStep_ATR     = 0.3;  // Trail step (ATR)
//--- Session
input bool   InpSessionFilter  = true; // Session filter on/off
input int    InpSessionStart   = 7;    // Session start GMT hour
input int    InpSessionEnd     = 17;   // Session end GMT hour
//--- Display
input bool   InpShowDashboard  = true; // Dashboard on/off

CTrade        Trade;
CPositionInfo Pos;

int      h_ATR        = INVALID_HANDLE;
int      MagicNumber  = 0;

datetime LastTradeClose = 0;
int      PendingSignal  = 0;   // 1=BUY 2=SELL
double   PendingSL      = 0;
double   PendingTP      = 0;
datetime PendingBarTime = 0;

//+------------------------------------------------------------------+
int OnInit()
{
   //--- Expiry check
   datetime expiry = (datetime)D'2056.06.30 23:59';
   datetime now    = TimeCurrent();
   if(now > expiry)
   {
      MessageBox("This EA has expired.\nContact support: https://t.me/profitabletrader2362",
                 "Swit_Scalper - Expired", MB_OK|MB_ICONERROR);
      return(INIT_FAILED);
   }
   if(now >= expiry - 86400)
   {
      MessageBox("Licence expires tomorrow.\nRenew: https://t.me/profitabletrader2362",
                 "Swit_Scalper - Notice", MB_OK|MB_ICONWARNING);
   }

   //--- Magic number
   int hash = 0;
   for(int i = 0; i < StringLen(_Symbol); i++)
      hash += (int)StringGetCharacter(_Symbol, i);
   MagicNumber = hash + ((int)_Period * 100) + 10000;

   //--- Trade setup
   Trade.SetExpertMagicNumber(MagicNumber);
   Trade.SetDeviationInPoints(Slippage);

   //--- Auto-detect filling mode (critical for gold brokers)
   uint modes = (uint)SymbolInfoInteger(_Symbol, SYMBOL_FILLING_MODE);
   if     ((modes & SYMBOL_FILLING_FOK) != 0) Trade.SetTypeFilling(ORDER_FILLING_FOK);
   else if((modes & SYMBOL_FILLING_IOC) != 0) Trade.SetTypeFilling(ORDER_FILLING_IOC);
   else                                        Trade.SetTypeFilling(ORDER_FILLING_RETURN);

   //--- ATR handle
   h_ATR = iATR(_Symbol, _Period, InpATR_Period);
   if(h_ATR == INVALID_HANDLE)
   {
      Print("ERROR: ATR handle failed. Error=", GetLastError());
      return(INIT_FAILED);
   }

   //--- Restore last close from history
   if(HistorySelect(0, TimeCurrent()))
   {
      for(int i = HistoryDealsTotal()-1; i >= 0; i--)
      {
         ulong tk = HistoryDealGetTicket(i);
         if((long)MagicNumber != HistoryDealGetInteger(tk, DEAL_MAGIC))  continue;
         if(_Symbol           != HistoryDealGetString(tk,  DEAL_SYMBOL)) continue;
         if(DEAL_ENTRY_OUT    != HistoryDealGetInteger(tk, DEAL_ENTRY))  continue;
         datetime t = (datetime)HistoryDealGetInteger(tk, DEAL_TIME);
         if(t > LastTradeClose) LastTradeClose = t;
         break;
      }
   }

   Print("Swit_Scalper v10 ready. Magic=", MagicNumber,
         " Symbol=", _Symbol, " TF=", EnumToString(_Period));
   return(INIT_SUCCEEDED);
}

//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   if(h_ATR != INVALID_HANDLE) { IndicatorRelease(h_ATR); h_ATR = INVALID_HANDLE; }
   Comment("");
}

//+------------------------------------------------------------------+
bool InSession()
{
   MqlDateTime dt;
   TimeToStruct(TimeCurrent(), dt);
   return (dt.hour >= InpSessionStart && dt.hour < InpSessionEnd);
}

bool IsNewBar()
{
   static datetime last = 0;
   datetime cur = iTime(_Symbol, _Period, 0);
   if(cur != last) { last = cur; return true; }
   return false;
}

bool HasPosition()
{
   for(int i = PositionsTotal()-1; i >= 0; i--)
   {
      if(!Pos.SelectByIndex(i))             continue;
      if(Pos.Magic()  != (long)MagicNumber) continue;
      if(Pos.Symbol() != _Symbol)           continue;
      return true;
   }
   return false;
}

double GetATR(int shift)
{
   double a[]; ArraySetAsSeries(a, true);
   if(CopyBuffer(h_ATR, 0, shift, 1, a) <= 0) return 0;
   return a[0];
}

//+------------------------------------------------------------------+
// VWAP: cumulative from first bar of current day to bar[shift]
//+------------------------------------------------------------------+
double CalcVWAP(int shift)
{
   int total = iBars(_Symbol, _Period);
   if(total <= 0) return 0;

   datetime ref  = iTime(_Symbol, _Period, shift);
   MqlDateTime d; TimeToStruct(ref, d);
   int ref_day = d.day_of_year, ref_yr = d.year;

   double pv = 0, vol = 0;
   for(int i = shift; i < total; i++)
   {
      MqlDateTime di; TimeToStruct(iTime(_Symbol, _Period, i), di);
      if(di.day_of_year != ref_day || di.year != ref_yr) break;
      double tp = (iHigh(_Symbol,_Period,i) + iLow(_Symbol,_Period,i) + iClose(_Symbol,_Period,i)) / 3.0;
      long   v  = iVolume(_Symbol, _Period, i);
      pv  += tp * (double)v;
      vol += (double)v;
   }
   return (vol > 0) ? pv / vol : iClose(_Symbol, _Period, shift);
}

//+------------------------------------------------------------------+
// BOS: scan last InpBOS_Lookback bars for swing high/low breaks
// Returns 1=bullish BOS active, 2=bearish BOS active, 0=none
// Uses the MOST RECENT break to determine current bias
//+------------------------------------------------------------------+
int GetBOS()
{
   int total = iBars(_Symbol, _Period);
   int s     = InpSwing_Strength;
   int limit = MathMin(total - s - 1, InpBOS_Lookback);

   datetime last_bull_time = 0, last_bear_time = 0;

   for(int i = s; i < limit; i++)
   {
      double hi = iHigh(_Symbol, _Period, i);
      double lo = iLow (_Symbol, _Period, i);
      bool   is_sh = true, is_sl = true;

      for(int k = 1; k <= s; k++)
      {
         if(iHigh(_Symbol,_Period,i-k) >= hi || iHigh(_Symbol,_Period,i+k) >= hi) is_sh = false;
         if(iLow (_Symbol,_Period,i-k) <= lo || iLow (_Symbol,_Period,i+k) <= lo) is_sl = false;
      }

      if(is_sh)
      {
         // Look left (bars 1..i-1) for a close above this swing high
         for(int j = 1; j < i; j++)
         {
            if(iClose(_Symbol,_Period,j) > hi)
            {
               datetime bt = iTime(_Symbol,_Period,j);
               if(bt > last_bull_time) last_bull_time = bt;
               break;
            }
         }
      }
      if(is_sl)
      {
         // Look left for a close below this swing low
         for(int j = 1; j < i; j++)
         {
            if(iClose(_Symbol,_Period,j) < lo)
            {
               datetime bt = iTime(_Symbol,_Period,j);
               if(bt > last_bear_time) last_bear_time = bt;
               break;
            }
         }
      }
   }

   if(last_bull_time == 0 && last_bear_time == 0) return 0;
   if(last_bull_time > last_bear_time) return 1;
   if(last_bear_time > last_bull_time) return 2;
   return 0;
}

//+------------------------------------------------------------------+
// VWAP Rejection on bar[shift]
// BUY:  low at or below VWAP (touched it), close above VWAP,
//       bullish body, low not more than InpVWAP_Buffer*ATR below VWAP
// SELL: high at or above VWAP (touched it), close below VWAP,
//       bearish body, high not more than InpVWAP_Buffer*ATR above VWAP
//+------------------------------------------------------------------+
int VWAPRejection(int shift, double vwap, double atr)
{
   double lo = iLow  (_Symbol, _Period, shift);
   double hi = iHigh (_Symbol, _Period, shift);
   double cl = iClose(_Symbol, _Period, shift);
   double op = iOpen (_Symbol, _Period, shift);
   double buffer = atr * InpVWAP_Buffer;

   // BUY rejection: candle dipped to/below VWAP but recovered above it
   if(lo <= vwap                  // low touched or pierced VWAP
      && lo >= vwap - buffer      // but not too far below
      && cl > vwap                // closed back above VWAP
      && cl > op)                 // bullish body (buyers won)
      return 1;

   // SELL rejection: candle spiked to/above VWAP but fell back below it
   if(hi >= vwap                  // high touched or pierced VWAP
      && hi <= vwap + buffer      // but not too far above
      && cl < vwap                // closed back below VWAP
      && cl < op)                 // bearish body (sellers won)
      return 2;

   return 0;
}

//+------------------------------------------------------------------+
// Count bars elapsed since a given datetime
// Returns -1 if the time is older than the lookback
//+------------------------------------------------------------------+
int BarsSince(datetime t, int lookback = 100)
{
   for(int i = 0; i < lookback; i++)
   {
      if(iTime(_Symbol, _Period, i) <= t) return i;
   }
   return -1;
}

//+------------------------------------------------------------------+
void OnTick()
{
   if(BarsCalculated(h_ATR) < InpATR_Period + 5) return;

   bool in_pos = HasPosition();

   //--- Detect closed trade (for cooldown)
   if(!in_pos && HistorySelect(0, TimeCurrent()))
   {
      for(int i = HistoryDealsTotal()-1; i >= 0; i--)
      {
         ulong tk = HistoryDealGetTicket(i);
         if((long)MagicNumber != HistoryDealGetInteger(tk, DEAL_MAGIC))  continue;
         if(_Symbol           != HistoryDealGetString(tk,  DEAL_SYMBOL)) continue;
         if(DEAL_ENTRY_OUT    != HistoryDealGetInteger(tk, DEAL_ENTRY))  continue;
         datetime t = (datetime)HistoryDealGetInteger(tk, DEAL_TIME);
         if(t > LastTradeClose) { LastTradeClose = t; PendingSignal = 0; }
         break;
      }
   }

   //--- Trailing stop (every tick)
   if(in_pos && UseTrailingStop)
   {
      double atr = GetATR(1);
      if(atr > 0)
      {
         for(int i = PositionsTotal()-1; i >= 0; i--)
         {
            if(!Pos.SelectByIndex(i))             continue;
            if(Pos.Magic()  != (long)MagicNumber) continue;
            if(Pos.Symbol() != _Symbol)           continue;

            double td  = atr * TrailDist_ATR;
            double ts  = atr * TrailStep_ATR;
            double sd  = atr * TrailStart_ATR;
            double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
            double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);

            if(Pos.PositionType() == POSITION_TYPE_BUY)
            {
               if(bid - Pos.PriceOpen() >= sd)
               {
                  double nsl = NormalizeDouble(bid - td, _Digits);
                  if(nsl > Pos.StopLoss() + ts)
                     Trade.PositionModify(Pos.Ticket(), nsl, Pos.TakeProfit());
               }
            }
            else if(Pos.PositionType() == POSITION_TYPE_SELL)
            {
               if(Pos.PriceOpen() - ask >= sd)
               {
                  double nsl = NormalizeDouble(ask + td, _Digits);
                  if(Pos.StopLoss() == 0 || nsl < Pos.StopLoss() - ts)
                     Trade.PositionModify(Pos.Ticket(), nsl, Pos.TakeProfit());
               }
            }
         }
      }
   }

   //--- One trade at a time
   if(in_pos) { ShowDashboard(); return; }

   //--- Cooldown after trade close
   if(LastTradeClose > 0)
   {
      int bs = BarsSince(LastTradeClose);
      if(bs >= 0 && bs < CooldownBars) { ShowDashboard(); return; }
   }

   //--- Session filter
   if(InpSessionFilter && !InSession()) { ShowDashboard(); return; }

   //--- Signal detection: once per bar on bar[1] (last closed bar)
   if(IsNewBar())
   {
      PendingSignal = 0;

      double atr  = GetATR(1);
      double vwap = CalcVWAP(1);
      int    bos  = GetBOS();

      if(atr <= 0 || vwap <= 0 || bos == 0)
      {
         ShowDashboard(); return;
      }

      int rej = VWAPRejection(1, vwap, atr);

      Print("Bar[1] scan: BOS=",bos," REJ=",rej,
            " VWAP=",DoubleToString(vwap,_Digits),
            " ATR=",DoubleToString(atr,_Digits),
            " Lo=",DoubleToString(iLow(_Symbol,_Period,1),_Digits),
            " Hi=",DoubleToString(iHigh(_Symbol,_Period,1),_Digits),
            " Cl=",DoubleToString(iClose(_Symbol,_Period,1),_Digits));

      if(bos == 1 && rej == 1)
      {
         double lo1      = iLow(_Symbol, _Period, 1);
         double sl_price = NormalizeDouble(lo1 - atr * InpSL_Buffer, _Digits);
         double ask      = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
         double sl_dist  = ask - sl_price;

         if(sl_dist > 0)
         {
            // Enforce broker minimum stop level
            double pt  = SymbolInfoDouble(_Symbol, SYMBOL_POINT);
            double min = (double)SymbolInfoInteger(_Symbol, SYMBOL_TRADE_STOPS_LEVEL) * pt;
            if(sl_dist < min * 1.1) sl_dist = min * 1.1;

            PendingSignal  = 1;
            PendingSL      = NormalizeDouble(ask - sl_dist, _Digits);
            PendingTP      = NormalizeDouble(ask + sl_dist * InpRR, _Digits);
            PendingBarTime = iTime(_Symbol, _Period, 1);
            Print("BUY signal set: SL=",PendingSL," TP=",PendingTP);
         }
      }
      else if(bos == 2 && rej == 2)
      {
         double hi1      = iHigh(_Symbol, _Period, 1);
         double sl_price = NormalizeDouble(hi1 + atr * InpSL_Buffer, _Digits);
         double bid      = SymbolInfoDouble(_Symbol, SYMBOL_BID);
         double sl_dist  = sl_price - bid;

         if(sl_dist > 0)
         {
            double pt  = SymbolInfoDouble(_Symbol, SYMBOL_POINT);
            double min = (double)SymbolInfoInteger(_Symbol, SYMBOL_TRADE_STOPS_LEVEL) * pt;
            if(sl_dist < min * 1.1) sl_dist = min * 1.1;

            PendingSignal  = 2;
            PendingSL      = NormalizeDouble(bid + sl_dist, _Digits);
            PendingTP      = NormalizeDouble(bid - sl_dist * InpRR, _Digits);
            PendingBarTime = iTime(_Symbol, _Period, 1);
            Print("SELL signal set: SL=",PendingSL," TP=",PendingTP);
         }
      }

      ShowDashboard(); return;
   }

   //--- Execute pending signal
   //    Valid from the bar AFTER the signal bar, up to 2 bars
   if(PendingSignal != 0)
   {
      int bs = BarsSince(PendingBarTime);
      // bs=1 means current bar[0] is 1 bar after signal bar - correct window
      // bs=2 means 2 bars after - still acceptable
      // bs=0 means we are still on the signal bar itself - wait
      // bs=-1 or >2 means too old - cancel
      if(bs <= 0 || bs > 2)
      {
         if(bs > 2) { Print("Signal expired. Cancelling."); PendingSignal = 0; }
         ShowDashboard(); return;
      }

      double pt  = SymbolInfoDouble(_Symbol, SYMBOL_POINT);
      double min = (double)SymbolInfoInteger(_Symbol, SYMBOL_TRADE_STOPS_LEVEL) * pt;

      if(PendingSignal == 1)
      {
         double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
         double sl  = PendingSL;
         double tp  = PendingTP;
         if(ask - sl < min * 1.1) sl = NormalizeDouble(ask - min * 1.1, _Digits);
         if(tp - ask < min * 1.1) tp = NormalizeDouble(ask + min * 2.2, _Digits);
         Print("BUY execute: ask=",ask," sl=",sl," tp=",tp);
         if(Trade.Buy(LotSize, _Symbol, ask, sl, tp, "Swit_BUY v10"))
            PendingSignal = 0;
         else
            Print("BUY FAILED: code=",Trade.ResultRetcode()," comment=",Trade.ResultComment());
      }
      else if(PendingSignal == 2)
      {
         double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
         double sl  = PendingSL;
         double tp  = PendingTP;
         if(sl - bid < min * 1.1) sl = NormalizeDouble(bid + min * 1.1, _Digits);
         if(bid - tp < min * 1.1) tp = NormalizeDouble(bid - min * 2.2, _Digits);
         Print("SELL execute: bid=",bid," sl=",sl," tp=",tp);
         if(Trade.Sell(LotSize, _Symbol, bid, sl, tp, "Swit_SELL v10"))
            PendingSignal = 0;
         else
            Print("SELL FAILED: code=",Trade.ResultRetcode()," comment=",Trade.ResultComment());
      }
   }

   ShowDashboard();
}

//+------------------------------------------------------------------+
void ShowDashboard()
{
   if(!InpShowDashboard) return;

   double vwap = CalcVWAP(0);
   int    bos  = GetBOS();
   double cl   = iClose(_Symbol, _Period, 0);

   string bos_str  = (bos==1) ? "BULL - structure up" : (bos==2) ? "BEAR - structure down" : "No BOS";
   string bias_str = (cl > vwap) ? "Above VWAP" : "Below VWAP";
   string sig_str  = (PendingSignal==1) ? "BUY pending" : (PendingSignal==2) ? "SELL pending" : "Scanning...";
   string ses_str  = (!InpSessionFilter || InSession()) ? "ACTIVE" : "CLOSED";

   string txt = "=== Swit_Scalper v10 (Gold) ===\n";
   txt += "VWAP:    " + DoubleToString(vwap, _Digits) + "  (" + bias_str + ")\n";
   txt += "BOS:     " + bos_str + "\n";
   txt += "Session: " + ses_str + "\n";
   txt += "Signal:  " + sig_str + "\n";
   txt += "Magic:   " + IntegerToString(MagicNumber) + "\n";
   Comment(txt);
}