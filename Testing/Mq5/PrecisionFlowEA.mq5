//+------------------------------------------------------------------+
//|  PrecisionGrid EA v1.3                                           |
//|  4-Level Grid · Basket Trailing TP · Basket SL                  |
//|  Optimiert für XAUUSD M15 / H1                                  |
//|  Changelog v1.3 (nur Bug-Fixes, keine Logik-Änderung):          |
//|   1. Cooldown nach Trail-TP-Close (sofort-Re-Entry verhindert)   |
//|   2. Per-Position-SL entfernt (kollidierte mit Basket-Logik)     |
//|   3. openPositions wird nach jedem Close neu gelesen             |
//+------------------------------------------------------------------+
#property copyright "Meff Trading Systems"
#property version   "1.30"
#property strict

#include <Trade\Trade.mqh>
#include <Trade\PositionInfo.mqh>

CTrade        trade;
CPositionInfo posInfo;

//=== INPUT PARAMETER ===================================================

input group "=== Grid Settings ==="
input double  BaseLot          = 0.01;    // Basis-Lot (Level 1)
input double  LotMultiplier    = 1.5;     // Lot-Multiplikator je Level
input int     MaxLevels        = 4;       // Max Grid-Levels (fest 4)
input double  GridATRMulti     = 1.2;     // Grid-Abstand = ATR × dieser Faktor
input int     ATR_Period       = 14;      // ATR Periode

input group "=== Smart Entry ==="
input int     EMA_Fast         = 21;
input int     EMA_Slow         = 55;
input int     RSI_Period       = 14;
input double  RSI_Oversold     = 42.0;
input double  RSI_Overbought   = 58.0;
input double  ATR_MinMulti     = 0.8;

input group "=== Basket Trailing TP ==="
input double  BasketTPPips     = 150.0;   // (reserviert, Trail steuert den Exit)
input double  TrailStepATRMult = 0.35;    // Trail-Schritt = ATR × Faktor
input double  TrailStartATRMult= 0.5;     // Trail aktiviert wenn Profit >= ATR × Faktor × Levels × 10

input group "=== Basket SL ==="
input double  BasketSL_USD     = 50.0;    // Hard SL in USD (alle Positionen zusammen)

input group "=== Recovery ==="
input int     CooldownBars     = 3;       // Pause nach Grid-Close (in Bars)
input bool    AllowShort       = true;

input group "=== System ==="
input int     MagicNumber      = 20240601;
input string  TradeComment     = "PrecisionGrid";
input int     Slippage         = 30;

//=== GLOBALE VARIABLEN =================================================

enum GRID_DIR { GRID_NONE, GRID_LONG, GRID_SHORT };

GRID_DIR  g_dir         = GRID_NONE;
int       g_level       = 0;
double    g_gridPrices[5];
datetime  g_cooldownEnd = 0;
double    g_trailHigh   = -DBL_MAX;
bool      g_trailing    = false;

int h_atr, h_ema_fast, h_ema_slow, h_rsi;

//+------------------------------------------------------------------+
//| OnInit                                                           |
//+------------------------------------------------------------------+
int OnInit()
{
   trade.SetExpertMagicNumber(MagicNumber);
   trade.SetDeviationInPoints(Slippage);
   trade.SetTypeFilling(ORDER_FILLING_IOC);

   h_atr      = iATR(_Symbol, PERIOD_CURRENT, ATR_Period);
   h_ema_fast = iMA(_Symbol, PERIOD_CURRENT, EMA_Fast, 0, MODE_EMA, PRICE_CLOSE);
   h_ema_slow = iMA(_Symbol, PERIOD_CURRENT, EMA_Slow, 0, MODE_EMA, PRICE_CLOSE);
   h_rsi      = iRSI(_Symbol, PERIOD_CURRENT, RSI_Period, PRICE_CLOSE);

   if(h_atr==INVALID_HANDLE || h_ema_fast==INVALID_HANDLE ||
      h_ema_slow==INVALID_HANDLE || h_rsi==INVALID_HANDLE)
   { Print("Handle Fehler!"); return INIT_FAILED; }

   ArrayInitialize(g_gridPrices, 0.0);
   Print("PrecisionGrid EA v1.3 | Magic:", MagicNumber);
   return INIT_SUCCEEDED;
}

//+------------------------------------------------------------------+
//| OnDeinit                                                         |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   IndicatorRelease(h_atr);
   IndicatorRelease(h_ema_fast);
   IndicatorRelease(h_ema_slow);
   IndicatorRelease(h_rsi);
}

//+------------------------------------------------------------------+
//| OnTick                                                           |
//+------------------------------------------------------------------+
void OnTick()
{
   if(TimeCurrent() < g_cooldownEnd) return;

   double atr     = GetBuf(h_atr, 1);
   double emaFast = GetBuf(h_ema_fast, 1);
   double emaSlow = GetBuf(h_ema_slow, 1);
   double rsi     = GetBuf(h_rsi, 1);
   if(atr <= 0) return;

   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);

   // === BASKET TRAILING TP ===
   // Immer frisch lesen vor dem Check
   int    pos = CountPos();
   double pnl = BasketPnL();

   if(pos > 0 && g_dir != GRID_NONE)
   {
      ManageBasketTrailing(atr, pnl, pos);
      // BUG-FIX 3: nach möglichem Close g_dir prüfen und sofort raus
      // verhindert Re-Entry auf demselben Tick
      if(g_dir == GRID_NONE) return;
   }

   // === BASKET HARD SL ===
   // BUG-FIX 3: pos/pnl nochmal frisch lesen
   pos = CountPos();
   pnl = BasketPnL();
   if(pos > 0 && pnl <= -BasketSL_USD)
   {
      Print("BASKET SL HIT! PnL=", DoubleToString(pnl,2));
      CloseAll();
      ResetGrid();
      g_cooldownEnd = TimeCurrent() + CooldownBars * PeriodSeconds(PERIOD_CURRENT);
      return;
   }

   // === GRID NACHZUG ===
   // BUG-FIX 3: pos nochmal frisch lesen nach den möglichen Closes oben
   pos = CountPos();
   if(pos > 0 && pos < MaxLevels && g_dir != GRID_NONE)
      CheckAndAddGridLevel(bid, ask, atr);

   // === ERSTER ENTRY ===
   // BUG-FIX 3: pos nochmal frisch – kein gecachter Wert
   pos = CountPos();
   if(pos == 0 && g_dir == GRID_NONE)
      CheckForEntry(bid, ask, atr, emaFast, emaSlow, rsi);
}

//+------------------------------------------------------------------+
//| Smart Entry (identisch v1.0, nur SL=0 statt per-Position-SL)    |
//+------------------------------------------------------------------+
void CheckForEntry(double bid, double ask, double atr,
                   double emaFast, double emaSlow, double rsi)
{
   if(emaFast > emaSlow && rsi < RSI_Overbought && rsi > 30.0)
   {
      if(ask > emaFast * 0.999)
      {
         // BUG-FIX 2: sl=0 – per-Position-SL entfernt, Basket SL übernimmt
         if(trade.Buy(NormLot(BaseLot), _Symbol, ask, 0, 0, TradeComment+"_L1"))
         {
            g_dir=GRID_LONG; g_level=1; g_gridPrices[1]=ask;
            g_trailing=false; g_trailHigh=-DBL_MAX;
            Print("LONG Entry L1 | Ask=",ask);
         }
      }
   }
   else if(AllowShort && emaFast < emaSlow && rsi > RSI_Oversold && rsi < 70.0)
   {
      if(bid < emaFast * 1.001)
      {
         // BUG-FIX 2: sl=0 – per-Position-SL entfernt
         if(trade.Sell(NormLot(BaseLot), _Symbol, bid, 0, 0, TradeComment+"_L1"))
         {
            g_dir=GRID_SHORT; g_level=1; g_gridPrices[1]=bid;
            g_trailing=false; g_trailHigh=-DBL_MAX;
            Print("SHORT Entry L1 | Bid=",bid);
         }
      }
   }
}

//+------------------------------------------------------------------+
//| Grid-Level nachziehen (identisch v1.0, sl=0)                    |
//+------------------------------------------------------------------+
void CheckAndAddGridLevel(double bid, double ask, double atr)
{
   int next = CountPos() + 1;
   if(next > MaxLevels) return;

   double gap  = atr * GridATRMulti;
   double last = g_gridPrices[next - 1];
   if(last <= 0) return;

   double lot = NormLot(BaseLot * MathPow(LotMultiplier, next - 1));

   if(g_dir == GRID_LONG && ask <= last - gap)
   {
      // BUG-FIX 2: sl=0
      if(trade.Buy(lot, _Symbol, ask, 0, 0, TradeComment+"_L"+IntegerToString(next)))
      {
         g_level=next; g_gridPrices[next]=ask;
         Print("LONG Grid L",next," | Ask=",ask," Lot=",lot);
      }
   }
   else if(g_dir == GRID_SHORT && bid >= last + gap)
   {
      // BUG-FIX 2: sl=0
      if(trade.Sell(lot, _Symbol, bid, 0, 0, TradeComment+"_L"+IntegerToString(next)))
      {
         g_level=next; g_gridPrices[next]=bid;
         Print("SHORT Grid L",next," | Bid=",bid," Lot=",lot);
      }
   }
}

//+------------------------------------------------------------------+
//| Basket Trailing TP (identisch v1.0 Logik)                       |
//+------------------------------------------------------------------+
void ManageBasketTrailing(double atr, double pnl, int positions)
{
   double trailActivate = atr * TrailStartATRMult * positions * 10;
   double trailStep     = atr * TrailStepATRMult * 10;

   if(!g_trailing && pnl >= trailActivate)
   {
      g_trailing  = true;
      g_trailHigh = pnl;
      Print("TRAIL AKTIVIERT | PnL=",DoubleToString(pnl,2),
            " | Min=",DoubleToString(trailActivate,2));
   }

   if(g_trailing)
   {
      if(pnl > g_trailHigh) g_trailHigh = pnl;

      if(pnl <= g_trailHigh - trailStep && g_trailHigh > 0)
      {
         Print("TRAIL TP HIT | Peak=",DoubleToString(g_trailHigh,2),
               " | Jetzt=",DoubleToString(pnl,2));
         CloseAll();
         ResetGrid();
         // BUG-FIX 1: Cooldown auch nach Trail-TP
         g_cooldownEnd = TimeCurrent() + CooldownBars * PeriodSeconds(PERIOD_CURRENT);
      }
   }
}

//+------------------------------------------------------------------+
//| Alle Positionen schließen                                        |
//+------------------------------------------------------------------+
void CloseAll()
{
   for(int i = PositionsTotal()-1; i >= 0; i--)
      if(posInfo.SelectByIndex(i))
         if(posInfo.Magic()==MagicNumber && posInfo.Symbol()==_Symbol)
            trade.PositionClose(posInfo.Ticket());
}

//+------------------------------------------------------------------+
//| Grid zurücksetzen                                                |
//+------------------------------------------------------------------+
void ResetGrid()
{
   g_dir=GRID_NONE; g_level=0;
   g_trailing=false; g_trailHigh=-DBL_MAX;
   ArrayInitialize(g_gridPrices, 0.0);
   Print("Grid reset.");
}

//+------------------------------------------------------------------+
//| Basket P&L                                                       |
//+------------------------------------------------------------------+
double BasketPnL()
{
   double v=0;
   for(int i=0; i<PositionsTotal(); i++)
      if(posInfo.SelectByIndex(i))
         if(posInfo.Magic()==MagicNumber && posInfo.Symbol()==_Symbol)
            v += posInfo.Profit()+posInfo.Swap()+posInfo.Commission();
   return v;
}

//+------------------------------------------------------------------+
//| Basket Volumen                                                   |
//+------------------------------------------------------------------+
double BasketVol()
{
   double v=0;
   for(int i=0; i<PositionsTotal(); i++)
      if(posInfo.SelectByIndex(i))
         if(posInfo.Magic()==MagicNumber && posInfo.Symbol()==_Symbol)
            v += posInfo.Volume();
   return v;
}

//+------------------------------------------------------------------+
//| Offene Positionen zählen                                         |
//+------------------------------------------------------------------+
int CountPos()
{
   int n=0;
   for(int i=0; i<PositionsTotal(); i++)
      if(posInfo.SelectByIndex(i))
         if(posInfo.Magic()==MagicNumber && posInfo.Symbol()==_Symbol)
            n++;
   return n;
}

//+------------------------------------------------------------------+
//| Indikator-Buffer                                                 |
//+------------------------------------------------------------------+
double GetBuf(int handle, int shift)
{
   double buf[1];
   ArraySetAsSeries(buf,true);
   if(CopyBuffer(handle,0,shift,1,buf)<=0) return 0;
   return buf[0];
}

//+------------------------------------------------------------------+
//| Lot normalisieren                                                |
//+------------------------------------------------------------------+
double NormLot(double lot)
{
   double mn=SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_MIN);
   double mx=SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_MAX);
   double st=SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_STEP);
   return MathMax(mn,MathMin(mx,MathRound(lot/st)*st));
}
//+------------------------------------------------------------------+

