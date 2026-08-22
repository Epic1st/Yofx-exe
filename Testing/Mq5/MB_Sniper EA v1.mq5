//+------------------------------------------------------------------+
//| MB_Sniper_EA.mq5                                                 |
//| SuperTrend Flip EA — 100% Matched to Indicator                  |
//| MT5 Automated Trading System                                     |
//+------------------------------------------------------------------+
#property copyright "2026, Prof. Morris"
#property link      "https://t.me/profitabletrader2362"
#property version   "2.00"
#property description "Automated EA — Signal engine is a 1:1 replica of MB Sniper Indicator"
#property description "SuperTrend Flip | Multi-TP Modes | Trailing | Telegram"

#include <Trade\Trade.mqh>
CTrade trade;

//+------------------------------------------------------------------+
//| ENUMS                                                            |
//+------------------------------------------------------------------+
enum ENUM_TP_MODE
{
   TP_MODE_1 = 0,   // Close All at TP1
   TP_MODE_2 = 1,   // Target TP2 (Partial Close at TP1)
   TP_MODE_3 = 2,   // Close All at TP2
   TP_MODE_4 = 3,   // Target TP3 (Partial at TP1 & TP2)
   TP_MODE_5 = 4,   // Close All at TP3
   TP_MODE_6 = 5    // No TP (Trail Only)
};

//+------------------------------------------------------------------+
//| INPUTS — must match MB Sniper Indicator settings                 |
//+------------------------------------------------------------------+
input group "——— SuperTrend (match Indicator settings) ———"
input int    InpAtrPeriod   = 10;    // ATR Period
input double InpAtrMult     = 3.0;   // ATR Multiplier
input double InpMinSL_ATR   = 0.5;   // Minimum SL distance (x ATR)
input int    InpLookback    = 500;   // Bars of history to compute

input group "——— Trade Management ———"
input ENUM_TP_MODE InpTPMode     = TP_MODE_1; // TP Strategy Mode
input double       InpLotSize    = 0.01;       // Fixed Lot Size
input double       InpRR1        = 1.0;        // TP1 Risk:Reward
input double       InpRR2        = 2.0;        // TP2 Risk:Reward
input double       InpRR3        = 3.0;        // TP3 Risk:Reward
input double       InpPartialPct = 50.0;       // Partial Close % (Modes 2 & 4)

input group "——— Trailing Stop (ATR Based) ———"
input bool   InpUseTrail       = true;  // Use Trailing Stop
input int    InpTrailATRPeriod = 14;    // Trailing ATR Period
input double InpTrailStartATR  = 0.7;  // Start Trailing (x ATR from open)
input double InpTrailStepATR   = 0.2;  // Minimum move before updating SL (x ATR)

input group "——— Telegram Settings ———"
input bool   InpUseTelegram = false;  // Send Telegram Alerts
input string InpBotToken    = "";     // Telegram Bot Token
input string InpChatID      = "";     // Telegram Chat ID

//+------------------------------------------------------------------+
//| GLOBALS                                                          |
//+------------------------------------------------------------------+
// Magic Number is auto-generated internally — not exposed as input
long MagicNumber;

int  hATR;        // SuperTrend ATR handle (InpAtrPeriod)
int  hATR_Trail;  // Trailing ATR handle   (InpTrailATRPeriod)

// SuperTrend internal buffers — oldest-first (index 0 = oldest bar)
double BufUp[];   // Upper band
double BufDn[];   // Lower band
double BufST[];   // ST line value (support or resistance)
int    BufDir[];  // 0 = bull, 1 = bear

int      g_CalcLimit    = 0;
datetime LastBarTime    = 0;
bool     g_PartialDone1 = false;
bool     g_PartialDone2 = false;

// Trailing Telegram tracking
ulong  TrailingActiveTicket = 0;  // ticket currently being trailed
double LastAlertedSL        = 0;  // last SL value we sent an alert for

// Close-alert dedup guard (shared between Section 0 poll & OnTradeTransaction)
ulong  g_LastClosedDealTicket = 0; // deal ticket for which TRADE CLOSED was already sent

//+------------------------------------------------------------------+
//| INIT                                                             |
//+------------------------------------------------------------------+
int OnInit()
{
   // Magic Number auto-generated — hidden from user settings
   MathSrand((uint)(GetTickCount() ^ (uint)AccountInfoInteger(ACCOUNT_LOGIN)));
   MagicNumber = 100000 + MathRand() % 900000;

   trade.SetExpertMagicNumber(MagicNumber);
   trade.SetDeviationInPoints(30);

   hATR       = iATR(NULL, 0, InpAtrPeriod);
   hATR_Trail = iATR(NULL, 0, InpTrailATRPeriod);

   if(hATR == INVALID_HANDLE || hATR_Trail == INVALID_HANDLE)
   {
      Print("MB_Sniper_EA: Failed to create ATR handles");
      return INIT_FAILED;
   }

   int sz = InpLookback + 10;
   ArrayResize(BufUp,  sz); ArraySetAsSeries(BufUp,  false);
   ArrayResize(BufDn,  sz); ArraySetAsSeries(BufDn,  false);
   ArrayResize(BufST,  sz); ArraySetAsSeries(BufST,  false);
   ArrayResize(BufDir, sz); ArraySetAsSeries(BufDir, false);

   CalculateSuperTrend();

   // Reset trailing tracking
   TrailingActiveTicket = 0;
   LastAlertedSL        = 0;

   Print("MB Sniper EA v2.0 | Magic: ", MagicNumber,
         " | ATR(", InpAtrPeriod, ") x ", InpAtrMult,
         " | Mode: ", EnumToString(InpTPMode));

   return INIT_SUCCEEDED;
}

//+------------------------------------------------------------------+
//| DEINIT                                                           |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   IndicatorRelease(hATR);
   IndicatorRelease(hATR_Trail);
}

//+------------------------------------------------------------------+
//| TICK                                                             |
//+------------------------------------------------------------------+
void OnTick()
{
   //--- SECTION 0: POLLING CLOSE DETECTION (Asempa logic — catches manual & auto closes)
   // Mirrors Asempa MQL4 lastHistoryTotal approach, ported to MT5 deal history.
   static int lastHistoryTotal = 0;

   datetime today_start = StringToTime(TimeToString(TimeCurrent(), TIME_DATE));
   HistorySelect(today_start, TimeCurrent());

   int currentHistoryTotal = HistoryDealsTotal();

   if(currentHistoryTotal > lastHistoryTotal)
   {
      // New deal(s) appeared — scan the tail for our closing deal
      for(int i = HistoryDealsTotal() - 1;
              i >= MathMax(0, HistoryDealsTotal() - 20); i--)
      {
         ulong deal_ticket = HistoryDealGetTicket(i);
         if(deal_ticket == 0) continue;

         if(HistoryDealGetString (deal_ticket, DEAL_SYMBOL) != Symbol())   continue;
         if(HistoryDealGetInteger(deal_ticket, DEAL_MAGIC)  != MagicNumber) continue;

         ENUM_DEAL_ENTRY de = (ENUM_DEAL_ENTRY)HistoryDealGetInteger(deal_ticket, DEAL_ENTRY);
         if(de != DEAL_ENTRY_OUT) continue;

         datetime deal_time = (datetime)HistoryDealGetInteger(deal_ticket, DEAL_TIME);

         // Only react to fresh closes (within last 10 seconds)
         if(deal_time < TimeCurrent() - 10) continue;

         static datetime lastCloseSeen = 0;
         if(deal_time <= lastCloseSeen) continue;

         lastCloseSeen = deal_time;
         Print("MB_Sniper: Position closed at ",
               TimeToString(deal_time, TIME_DATE|TIME_MINUTES),
               " (Section 0 poll detected)");

         // Telegram close alert
         SendTelegramAlert("TRADE CLOSED", deal_ticket,
                           HistoryDealGetDouble(deal_ticket, DEAL_PRICE));

         // Reset trailing tracking immediately on close
         TrailingActiveTicket = 0;
         LastAlertedSL        = 0;

         break;
      }
   }
   lastHistoryTotal = currentHistoryTotal;

   //--- Refresh SuperTrend every tick (keeps buffers current)
   CalculateSuperTrend();

   // New bar detection
   datetime currentBarTime = iTime(NULL, 0, 0);
   bool newBar = (currentBarTime != LastBarTime);
   if(newBar) LastBarTime = currentBarTime;

   // Position management runs every tick
   ManagePositions();

   // Signal check only on new bar (uses last CLOSED bar — same as indicator)
   if(!newBar) return;
   if(g_CalcLimit < 3) return;

   // ── Indicator mapping ────────────────────────────────────────────
   // Indicator bar index → buffer index (oldest-first)
   // rates_total-2 (last closed bar) = g_CalcLimit - 2 = closedIdx
   // rates_total-3 (bar before it)   = g_CalcLimit - 3 = prevIdx
   // ─────────────────────────────────────────────────────────────────
   int closedIdx = g_CalcLimit - 2;
   int prevIdx   = g_CalcLimit - 3;

   bool isBullNow  = (BufDir[closedIdx] == 0);
   bool wasBullPre = (BufDir[prevIdx]   == 0);

   // ── BUY signal: SuperTrend flips Bear → Bull ────────────────────
   if(isBullNow && !wasBullPre)  OpenTrade( 1, closedIdx);

   // ── SELL signal: SuperTrend flips Bull → Bear ───────────────────
   if(!isBullNow && wasBullPre)  OpenTrade(-1, closedIdx);
}

//+------------------------------------------------------------------+
//| SUPERTREND CALCULATION                                           |
//| Exact replica of MB_Sniper_Indicator OnCalculate logic.          |
//| Band rules:                                                       |
//|   Upper: tighten down only; reset when prev close breaks above.  |
//|   Lower: tighten up only;   reset when prev close breaks below.  |
//|   Direction: stay bull unless close breaks below lower band;      |
//|              flip bull only when close breaks above upper band.   |
//+------------------------------------------------------------------+
void CalculateSuperTrend()
{
   int totalBars = iBars(NULL, 0);
   int limit     = MathMin(InpLookback, totalBars);
   if(limit < 2) { g_CalcLimit = 0; return; }
   g_CalcLimit = limit;

   // Copy OHLC and ATR — oldest-first (index 0 = oldest)
   MqlRates rates[];
   double   atr[];
   ArraySetAsSeries(rates, false);
   ArraySetAsSeries(atr,   false);

   if(CopyRates (NULL, 0, 0, limit, rates) != limit) return;
   if(CopyBuffer(hATR, 0, 0, limit, atr)   != limit) return;

   for(int i = 0; i < limit; i++)
   {
      // ---- bootstrap first bar ----
      if(i == 0)
      {
         BufUp[0] = 0; BufDn[0] = 0;
         BufDir[0] = 0; BufST[0] = 0;
         continue;
      }

      double a = atr[i];
      if(a <= 0)
      {
         // carry forward
         BufUp[i]  = BufUp[i-1];
         BufDn[i]  = BufDn[i-1];
         BufDir[i] = BufDir[i-1];
         BufST[i]  = BufST[i-1];
         continue;
      }

      double hl2     = (rates[i].high + rates[i].low) * 0.5;
      double basicUp = hl2 + InpAtrMult * a;   // raw upper band
      double basicDn = hl2 - InpAtrMult * a;   // raw lower band

      double prevUp   = (BufUp[i-1] == 0) ? basicUp : BufUp[i-1];
      double prevDn   = (BufDn[i-1] == 0) ? basicDn : BufDn[i-1];
      double closePrev = rates[i-1].close;

      // ── Upper band: only tighten (lower); reset if prev close broke above ──
      // Matches indicator: (basicUp < prevUp || close[i-1] > prevUp)
      BufUp[i] = (basicUp < prevUp || closePrev > prevUp) ? basicUp : prevUp;

      // ── Lower band: only tighten (raise); reset if prev close broke below ──
      // Matches indicator: (basicDn > prevDn || close[i-1] < prevDn)
      BufDn[i] = (basicDn > prevDn || closePrev < prevDn) ? basicDn : prevDn;

      // ── Direction — exact replication of indicator ──
      bool wasBull = (BufDir[i-1] == 0);
      bool bull;
      if(wasBull) bull = (rates[i].close >= BufDn[i]); // stay bull unless close < lower band
      else        bull = (rates[i].close >  BufUp[i]); // flip bull only if close > upper band

      BufDir[i] = bull ? 0 : 1;
      BufST[i]  = bull ? BufDn[i] : BufUp[i]; // support when bull, resistance when bear
   }
}

//+------------------------------------------------------------------+
//| OPEN TRADE                                                        |
//| signal: +1 = BUY, -1 = SELL                                      |
//| idx   : buffer index of the signal (closed) bar                  |
//|                                                                   |
//| SL/TP logic is a 1:1 copy of the indicator:                      |
//|   rawSL = BufST[idx]   (lower band for buy, upper for sell)      |
//|   if entry - rawSL < ATR*InpMinSL_ATR → rawSL = entry - minDist  |
//|   risk = entry - rawSL                                           |
//|   TP1/2/3 = entry + risk * RR1/2/3                               |
//+------------------------------------------------------------------+
void OpenTrade(int signal, int idx)
{
   if(CountMyPositions() > 0) return;  // one trade at a time

   // ── Values at the signal bar (mirrors indicator exactly) ──
   double stLine = BufST[idx]; // SuperTrend line = raw SL reference

   // ATR at signal bar
   double atrBuf[];
   ArraySetAsSeries(atrBuf, true);
   if(CopyBuffer(hATR, 0, 1, 1, atrBuf) <= 0) return; // bar[1] ATR
   double atr = atrBuf[0];
   if(atr <= 0) return;

   double minDist = atr * InpMinSL_ATR;

   // ── BUY ──────────────────────────────────────────────────────────
   if(signal == 1)
   {
      double ask      = SymbolInfoDouble(Symbol(), SYMBOL_ASK);
      double rawSL    = stLine;              // lower band
      double refEntry = iClose(NULL, 0, 1); // close of signal bar (bar[1])

      // Enforce minimum SL distance — identical to indicator
      if(refEntry - rawSL < minDist) rawSL = refEntry - minDist;

      double risk = refEntry - rawSL;        // risk in price units
      if(risk <= 0) { Print("OpenBuy: invalid risk"); return; }

      // Place SL at same distance below live ask
      double sl = NormalizeDouble(ask - risk, _Digits);

      // TP levels — RR ratios applied to risk, same as indicator
      double finalTP = GetFinalTP(ask, risk, 1);

      g_PartialDone1 = false;
      g_PartialDone2 = false;

      if(trade.Buy(InpLotSize, Symbol(), ask, sl, finalTP, "Mb_Sniper"))
      {
         ulong ticket = trade.ResultOrder();

         // Reset trailing tracking for the new trade
         TrailingActiveTicket = 0;
         LastAlertedSL        = 0;

         Print("BUY | Entry=", ask, " SL=", sl,
               " Risk=", DoubleToString(risk, _Digits),
               " TP=", finalTP,
               " [ATR=", DoubleToString(atr, _Digits), "]");
         SendTelegramAlert("BUY OPENED", ticket, ask);
      }
      else
         Print("BUY FAILED: ", trade.ResultRetcodeDescription());
   }

   // ── SELL ─────────────────────────────────────────────────────────
   else if(signal == -1)
   {
      double bid      = SymbolInfoDouble(Symbol(), SYMBOL_BID);
      double rawSL    = stLine;              // upper band
      double refEntry = iClose(NULL, 0, 1); // close of signal bar

      // Enforce minimum SL distance — identical to indicator
      if(rawSL - refEntry < minDist) rawSL = refEntry + minDist;

      double risk = rawSL - refEntry;
      if(risk <= 0) { Print("OpenSell: invalid risk"); return; }

      // Place SL at same distance above live bid
      double sl = NormalizeDouble(bid + risk, _Digits);

      double finalTP = GetFinalTP(bid, risk, -1);

      g_PartialDone1 = false;
      g_PartialDone2 = false;

      if(trade.Sell(InpLotSize, Symbol(), bid, sl, finalTP, "Mb_Sniper"))
      {
         ulong ticket = trade.ResultOrder();

         // Reset trailing tracking for the new trade
         TrailingActiveTicket = 0;
         LastAlertedSL        = 0;

         Print("SELL | Entry=", bid, " SL=", sl,
               " Risk=", DoubleToString(risk, _Digits),
               " TP=", finalTP,
               " [ATR=", DoubleToString(atr, _Digits), "]");
         SendTelegramAlert("SELL OPENED", ticket, bid);
      }
      else
         Print("SELL FAILED: ", trade.ResultRetcodeDescription());
   }
}

//+------------------------------------------------------------------+
//| Return the broker-side TP based on selected mode                 |
//+------------------------------------------------------------------+
double GetFinalTP(double entry, double risk, int dir)
{
   double tp = 0.0;
   switch(InpTPMode)
   {
      case TP_MODE_1: tp = entry + dir * risk * InpRR1; break;
      case TP_MODE_2: tp = entry + dir * risk * InpRR2; break; // partial at TP1, exit at TP2
      case TP_MODE_3: tp = entry + dir * risk * InpRR2; break;
      case TP_MODE_4: tp = entry + dir * risk * InpRR3; break; // partial at TP1+TP2, exit at TP3
      case TP_MODE_5: tp = entry + dir * risk * InpRR3; break;
      case TP_MODE_6: tp = 0.0;                         break; // trail only
   }
   return NormalizeDouble(tp, _Digits);
}

//+------------------------------------------------------------------+
//| Count this EA's open positions on this symbol                    |
//+------------------------------------------------------------------+
int CountMyPositions()
{
   int cnt = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      if(PositionGetTicket(i) == 0) continue;
      if(PositionGetString (POSITION_SYMBOL)  != Symbol())      continue;
      if(PositionGetInteger(POSITION_MAGIC)   != MagicNumber)   continue;
      cnt++;
   }
   return cnt;
}

//+------------------------------------------------------------------+
//| POSITION MANAGEMENT — trailing + partials, called every tick     |
//+------------------------------------------------------------------+
void ManagePositions()
{
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;
      if(PositionGetString (POSITION_SYMBOL) != Symbol())    continue;
      if(PositionGetInteger(POSITION_MAGIC)  != MagicNumber) continue;

      if(InpUseTrail) CheckTrailing(ticket);

      if(InpTPMode == TP_MODE_2 || InpTPMode == TP_MODE_4)
         CheckPartials(ticket);
   }
}

//+------------------------------------------------------------------+
//| TRAILING STOP — Asempa Dynamic Stepped Logic                     |
//| Moves SL in steps of TrailStep ATR after TrailStart ATR profit.  |
//| Fires TRAILING STARTED on first move, TRAILING UPDATE thereafter.|
//+------------------------------------------------------------------+
void CheckTrailing(ulong ticket)
{
   double atrBuf[];
   ArraySetAsSeries(atrBuf, true);
   if(CopyBuffer(hATR_Trail, 0, 0, 2, atrBuf) <= 0) return;

   double currentATR = atrBuf[0];
   if(currentATR <= 0) return;

   double trailStep        = currentATR * InpTrailStepATR;
   double startTrailProfit = currentATR * InpTrailStartATR;

   if(!PositionSelectByTicket(ticket)) return;
   long   type      = PositionGetInteger(POSITION_TYPE);
   double openPrice = PositionGetDouble(POSITION_PRICE_OPEN);
   double currentSL = PositionGetDouble(POSITION_SL);
   double currentTP = PositionGetDouble(POSITION_TP);

   if(type == POSITION_TYPE_BUY)
   {
      double currentProfit = SymbolInfoDouble(Symbol(), SYMBOL_BID) - openPrice;

      if(currentProfit >= startTrailProfit)
      {
         int    steps = (int)((currentProfit - startTrailProfit) / trailStep);
         double newSL = openPrice + (trailStep * (1 + steps));

         if(newSL > currentSL + _Point)
         {
            if(trade.PositionModify(ticket, newSL, currentTP))
            {
               Print("Trail BUY updated. New SL: ", newSL);

               if(TrailingActiveTicket != ticket)
               {
                  TrailingActiveTicket = ticket;
                  LastAlertedSL        = newSL;
                  SendTelegramAlert("TRAILING STARTED", ticket, newSL);
               }
               else
               {
                  LastAlertedSL = newSL;
                  SendTelegramAlert("TRAILING UPDATE", ticket, newSL);
               }
            }
         }
      }
   }
   else if(type == POSITION_TYPE_SELL)
   {
      double currentProfit = openPrice - SymbolInfoDouble(Symbol(), SYMBOL_ASK);

      if(currentProfit >= startTrailProfit)
      {
         int    steps = (int)((currentProfit - startTrailProfit) / trailStep);
         double newSL = openPrice - (trailStep * (1 + steps));

         if(newSL < currentSL - _Point)
         {
            if(trade.PositionModify(ticket, newSL, currentTP))
            {
               Print("Trail SELL updated. New SL: ", newSL);

               if(TrailingActiveTicket != ticket)
               {
                  TrailingActiveTicket = ticket;
                  LastAlertedSL        = newSL;
                  SendTelegramAlert("TRAILING STARTED", ticket, newSL);
               }
               else
               {
                  LastAlertedSL = newSL;
                  SendTelegramAlert("TRAILING UPDATE", ticket, newSL);
               }
            }
         }
      }
   }
}

//+------------------------------------------------------------------+
//| PARTIAL CLOSES                                                    |
//| Mode 2: close InpPartialPct% at TP1; remainder exits at TP2      |
//| Mode 4: close InpPartialPct% at TP1, again at TP2; rest → TP3   |
//+------------------------------------------------------------------+
void CheckPartials(ulong ticket)
{
   if(!PositionSelectByTicket(ticket)) return;

   long   type      = PositionGetInteger(POSITION_TYPE);
   double openPrice = PositionGetDouble(POSITION_PRICE_OPEN);
   double currentSL = PositionGetDouble(POSITION_SL);
   double volume    = PositionGetDouble(POSITION_VOLUME);

   double risk = MathAbs(openPrice - currentSL);
   if(risk <= 0) return;

   double tp1Price = openPrice + ((type == POSITION_TYPE_BUY) ?  risk : -risk) * InpRR1;
   double tp2Price = openPrice + ((type == POSITION_TYPE_BUY) ?  risk : -risk) * InpRR2;

   double price = (type == POSITION_TYPE_BUY)
                  ? SymbolInfoDouble(Symbol(), SYMBOL_BID)
                  : SymbolInfoDouble(Symbol(), SYMBOL_ASK);

   // ── TP1 partial (Modes 2 & 4) ──
   if(!g_PartialDone1)
   {
      bool tp1Hit = (type == POSITION_TYPE_BUY) ? (price >= tp1Price) : (price <= tp1Price);
      if(tp1Hit && DoPartialClose(ticket, volume, InpPartialPct, "TP1"))
         g_PartialDone1 = true;
   }

   // ── TP2 partial (Mode 4 only) ──
   if(InpTPMode == TP_MODE_4 && g_PartialDone1 && !g_PartialDone2)
   {
      bool tp2Hit = (type == POSITION_TYPE_BUY) ? (price >= tp2Price) : (price <= tp2Price);
      // Re-fetch live volume after TP1 close
      if(tp2Hit && PositionSelectByTicket(ticket))
      {
         double remVol = PositionGetDouble(POSITION_VOLUME);
         if(DoPartialClose(ticket, remVol, InpPartialPct, "TP2"))
            g_PartialDone2 = true;
      }
   }
}

//+------------------------------------------------------------------+
//| Execute a partial close; returns true on success                  |
//+------------------------------------------------------------------+
bool DoPartialClose(ulong ticket, double volume, double pct, string label)
{
   double volStep  = SymbolInfoDouble(Symbol(), SYMBOL_VOLUME_STEP);
   double volMin   = SymbolInfoDouble(Symbol(), SYMBOL_VOLUME_MIN);
   double closeVol = MathFloor(volume * pct / 100.0 / volStep) * volStep;
   closeVol = NormalizeDouble(closeVol, 2);

   if(closeVol < volMin)   closeVol = volMin;
   if(closeVol >= volume)  return false; // can't close everything here

   if(trade.PositionClosePartial(ticket, closeVol))
   {
      Print(label, " partial: closed ", closeVol, " lots (", pct, "% of ", volume, ")");
      return true;
   }
   return false;
}

//+------------------------------------------------------------------+
//| TELEGRAM — full detail alerts                                    |
//+------------------------------------------------------------------+
double CalcPips(double priceDiff)
{
   double pipSize = (_Digits == 3 || _Digits == 5) ? _Point * 10 : _Point;
   if(pipSize == 0) return 0;
   return priceDiff / pipSize;
}

string ErrorDescription(int errorCode)
{
   switch(errorCode)
   {
      case 0:     return "No error";
      case 4014:  return "DLLs not allowed";
      case 10004: return "Requote";
      case 10006: return "Request rejected";
      case 10009: return "Request completed";
      case 10013: return "Invalid request";
      case 10019: return "Not enough money";
      case 10024: return "Too frequent requests";
      case 10031: return "No connection";
      default:    return "Error " + IntegerToString(errorCode);
   }
}

void SendTelegramAlert(string type, ulong ticket, double price)
{
   if(!InpUseTelegram || InpBotToken == "" || InpChatID == "") return;

   string message = "";

   // ── BUY / SELL OPENED ─────────────────────────────────────────────
   if(type == "BUY OPENED" || type == "SELL OPENED")
   {
      if(!PositionSelectByTicket(ticket)) return;
      string dir = (type == "BUY OPENED") ? "BUY" : "SELL";
      double entry = PositionGetDouble(POSITION_PRICE_OPEN);
      double tp    = PositionGetDouble(POSITION_TP);
      double sl    = PositionGetDouble(POSITION_SL);
      string modeNames[] = {"Close All@TP1","Partial@TP1->TP2","Close All@TP2",
                             "Partial@TP1&TP2->TP3","Close All@TP3","Trail Only"};
      message  = "** MB SNIPER SIGNAL **\n";
      message += "---------------\n";
      message += "Symbol: "    + PositionGetString(POSITION_SYMBOL) + "\n";
      message += "TF: "        + EnumToString(Period())             + "\n";
      message += "Direction: " + dir                                + "\n";
      message += "Mode: "      + modeNames[InpTPMode]               + "\n";
      message += "Entry: "     + DoubleToString(entry, _Digits)     + "\n";
      message += "TP: "        + DoubleToString(tp,    _Digits)     + "\n";
      message += "SL: "        + DoubleToString(sl,    _Digits)     + "\n";
      message += "---------------\n";
      message += "Time: " + TimeToString((datetime)PositionGetInteger(POSITION_TIME), TIME_DATE|TIME_MINUTES);
   }
   // ── TRAILING STARTED ──────────────────────────────────────────────
   else if(type == "TRAILING STARTED")
   {
      if(!PositionSelectByTicket(ticket)) return;
      ENUM_POSITION_TYPE posType = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
      string dir       = (posType == POSITION_TYPE_BUY) ? "BUY" : "SELL";
      double openPrice = PositionGetDouble(POSITION_PRICE_OPEN);
      double curPrice  = (posType == POSITION_TYPE_BUY)
                         ? SymbolInfoDouble(Symbol(), SYMBOL_BID)
                         : SymbolInfoDouble(Symbol(), SYMBOL_ASK);
      double pips      = (posType == POSITION_TYPE_BUY)
                         ? CalcPips(curPrice - openPrice)
                         : CalcPips(openPrice - curPrice);
      message  = ">> TRAILING STARTED <<\n";
      message += "---------------\n";
      message += "Symbol: "    + PositionGetString(POSITION_SYMBOL) + "\n";
      message += "Direction: " + dir                                + "\n";
      message += "Entry: "     + DoubleToString(openPrice, _Digits) + "\n";
      message += "New SL: "    + DoubleToString(price,     _Digits) + "\n";
      message += "Current: "   + DoubleToString(curPrice,  _Digits) + "\n";
      message += "Pips: +"     + DoubleToString(pips, 1)            + "\n";
      message += "---------------\n";
      message += "Profit locked in";
   }
   // ── TRAILING UPDATE ───────────────────────────────────────────────
   else if(type == "TRAILING UPDATE")
   {
      if(!PositionSelectByTicket(ticket)) return;
      ENUM_POSITION_TYPE posType = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
      string dir       = (posType == POSITION_TYPE_BUY) ? "BUY" : "SELL";
      double openPrice = PositionGetDouble(POSITION_PRICE_OPEN);
      double curPrice  = (posType == POSITION_TYPE_BUY)
                         ? SymbolInfoDouble(Symbol(), SYMBOL_BID)
                         : SymbolInfoDouble(Symbol(), SYMBOL_ASK);
      double pips      = (posType == POSITION_TYPE_BUY)
                         ? CalcPips(curPrice - openPrice)
                         : CalcPips(openPrice - curPrice);
      message  = ">> TRADE PROGRESS <<\n";
      message += "---------------\n";
      message += "Symbol: "    + PositionGetString(POSITION_SYMBOL) + "\n";
      message += "Direction: " + dir                                + "\n";
      message += "Entry: "     + DoubleToString(openPrice, _Digits) + "\n";
      message += "New SL: "    + DoubleToString(price,     _Digits) + "\n";
      message += "Current: "   + DoubleToString(curPrice,  _Digits) + "\n";
      message += "Pips: +"     + DoubleToString(pips, 1)            + "\n";
      message += "---------------\n";
      message += "SL moved to lock more profit";
   }
   // ── TRADE CLOSED ──────────────────────────────────────────────────
   else if(type == "TRADE CLOSED")
   {
      HistorySelect(0, TimeCurrent());
      if(!HistoryDealSelect(ticket)) return;

      double exitPrice   = HistoryDealGetDouble (ticket, DEAL_PRICE);
      double profit      = HistoryDealGetDouble (ticket, DEAL_PROFIT);
      double swap        = HistoryDealGetDouble (ticket, DEAL_SWAP);
      double commission  = HistoryDealGetDouble (ticket, DEAL_COMMISSION);
      double totalPnL    = profit + swap + commission;
      string symbolName  = HistoryDealGetString (ticket, DEAL_SYMBOL);
      long   reason      = HistoryDealGetInteger(ticket, DEAL_REASON);
      string reasonStr   = (reason == DEAL_REASON_SL) ? " [SL HIT]"
                         : (reason == DEAL_REASON_TP) ? " [TP HIT]" : "";

      ulong  posID       = (ulong)HistoryDealGetInteger(ticket, DEAL_POSITION_ID);
      double entryPrice  = 0;
      string direction   = "";

      for(int j = 0; j < HistoryDealsTotal(); j++)
      {
         ulong dt = HistoryDealGetTicket(j);
         if(dt > 0 && (ulong)HistoryDealGetInteger(dt, DEAL_POSITION_ID) == posID)
         {
            if((ENUM_DEAL_ENTRY)HistoryDealGetInteger(dt, DEAL_ENTRY) == DEAL_ENTRY_IN)
            {
               entryPrice = HistoryDealGetDouble(dt, DEAL_PRICE);
               ENUM_DEAL_TYPE et = (ENUM_DEAL_TYPE)HistoryDealGetInteger(dt, DEAL_TYPE);
               direction = (et == DEAL_TYPE_BUY) ? "BUY" : "SELL";
               break;
            }
         }
      }

      if(entryPrice == 0)
      {
         ENUM_DEAL_TYPE dt2 = (ENUM_DEAL_TYPE)HistoryDealGetInteger(ticket, DEAL_TYPE);
         direction  = (dt2 == DEAL_TYPE_SELL) ? "BUY" : "SELL";
         entryPrice = exitPrice;
      }

      double pips      = (direction == "BUY") ? CalcPips(exitPrice - entryPrice)
                                               : CalcPips(entryPrice - exitPrice);
      string resultTag = (pips >= 0) ? "[WIN]" : "[LOSS]";
      string pipsSign  = (pips >= 0) ? "+" : "";

      message  = "TRADE CLOSED" + reasonStr + " - " + resultTag + "\n";
      message += "---------------\n";
      message += "Symbol: "    + symbolName                      + "\n";
      message += "Direction: " + direction                       + "\n";
      message += "Entry: "     + DoubleToString(entryPrice, _Digits) + "\n";
      message += "Exit: "      + DoubleToString(exitPrice,  _Digits) + "\n";
      message += "Pips: "      + pipsSign + DoubleToString(pips, 1)  + "\n";
      message += "P/L: $"      + DoubleToString(totalPnL, 2)         + "\n";
      message += "---------------\n";
      message += "Time: " + TimeToString((datetime)HistoryDealGetInteger(ticket, DEAL_TIME), TIME_DATE|TIME_MINUTES);
   }

   // ── SEND via URL-encoded POST ─────────────────────────────────────
   string url     = "https://api.telegram.org/bot" + InpBotToken + "/sendMessage";
   string headers = "Content-Type: application/x-www-form-urlencoded\r\n";
   string encoded = "";

   for(int i = 0; i < StringLen(message); i++)
   {
      ushort ch = StringGetCharacter(message, i);
      if((ch>='A'&&ch<='Z')||(ch>='a'&&ch<='z')||(ch>='0'&&ch<='9'))
         encoded += ShortToString(ch);
      else if(ch == ' ')  encoded += "+";
      else if(ch == '\n') encoded += "%0A";
      else if(ch == ':')  encoded += "%3A";
      else if(ch == '#')  encoded += "%23";
      else if(ch == '$')  encoded += "%24";
      else if(ch == '.')  encoded += ".";
      else if(ch == '-')  encoded += "-";
      else if(ch == '+')  encoded += "%2B";
      else if(ch == '/')  encoded += "%2F";
      else if(ch == '*')  encoded += "%2A";
      else if(ch == '>')  encoded += "%3E";
      else if(ch == '<')  encoded += "%3C";
      else if(ch == '[')  encoded += "%5B";
      else if(ch == ']')  encoded += "%5D";
      else { if(ch < 128) encoded += ShortToString(ch); }
   }

   string postData = "chat_id=" + InpChatID + "&text=" + encoded;
   char   post[];
   char   result[];
   string resultHeaders;

   int postLen = StringToCharArray(postData, post, 0, WHOLE_ARRAY, CP_UTF8);
   ArrayResize(post, postLen - 1);

   int res = WebRequest("POST", url, headers, 5000, post, result, resultHeaders);

   if(res == -1)
   {
      int err = GetLastError();
      Print("Telegram Error: ", err, " - ", ErrorDescription(err));
      Print("Add https://api.telegram.org to: Tools > Options > Expert Advisors > Allow WebRequest");
   }
   else if(res == 200)
      Print("Telegram sent: ", type);
   else
      Print("Telegram HTTP ", res, " — ", CharArrayToString(result, 0, WHOLE_ARRAY, CP_UTF8));
}

//+------------------------------------------------------------------+
//| TRADE TRANSACTION — Telegram on close + reset trailing tracking  |
//+------------------------------------------------------------------+
void OnTradeTransaction(const MqlTradeTransaction &trans,
                        const MqlTradeRequest     &request,
                        const MqlTradeResult      &result)
{
   if(trans.type != TRADE_TRANSACTION_DEAL_ADD) return;

   ulong dealTicket = trans.deal;
   if(!HistoryDealSelect(dealTicket)) return;
   if(HistoryDealGetInteger(dealTicket, DEAL_MAGIC) != MagicNumber) return;

   ENUM_DEAL_ENTRY e = (ENUM_DEAL_ENTRY)HistoryDealGetInteger(dealTicket, DEAL_ENTRY);
   if(e != DEAL_ENTRY_OUT) return;

   // Reset trailing tracking since the position is now closed
   TrailingActiveTicket = 0;
   LastAlertedSL        = 0;

   SendTelegramAlert("TRADE CLOSED", dealTicket,
                     HistoryDealGetDouble(dealTicket, DEAL_PRICE));
}
//+------------------------------------------------------------------+