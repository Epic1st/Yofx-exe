//+------------------------------------------------------------------+
//|                                       BollingerGridHedge.mq5     |
//|                                                                  |
//|   Bollinger Bands Classic Grid + Reverse (Hedge) Grid for MT5    |
//|                                                                  |
//|   Strategy:                                                      |
//|     1. Wait for a Bollinger Bands entry signal on the chosen     |
//|        timeframe (default: candle opens outside, closes inside). |
//|     2. Open a CLASSIC grid in the signal direction:              |
//|          - market entry + (GridSize - 1) limit orders placed     |
//|            against direction at intervals of "Spacing".          |
//|          - each order has TP = Open +/- TPPrimary * Spacing.     |
//|     3. If price retraces against the primary by                  |
//|        HedgeTrigger * Spacing, open a REVERSE (hedge) grid:      |
//|          - market entry in opposite direction + (GridSize - 1)   |
//|            stop orders placed in the hedge direction at intervals|
//|            of "Spacing".                                         |
//|          - each order has TP = Open +/- TPHedge * Spacing.       |
//|                                                                  |
//|   Account must be in HEDGING mode (most brokers offer this).     |
//+------------------------------------------------------------------+
#property copyright   "BollingerGridHedge"
#property link        ""
#property version     "1.00"
#property description "Bollinger Bands Classic + Reverse Hedge Grid EA for MetaTrader 5"
#property strict

#include <Trade/Trade.mqh>
#include <Trade/PositionInfo.mqh>
#include <Trade/OrderInfo.mqh>
#include <Trade/SymbolInfo.mqh>

//==================================================================
// Enumerations
//==================================================================
enum ENUM_SPACING_TYPE
  {
   SPACING_STATIC  = 0, // Static (pips)
   SPACING_DYNAMIC = 1  // Dynamic (ATR x multiplier)
  };

enum ENUM_AUTH_ORDERS
  {
   AUTH_ALL  = 0,       // All
   AUTH_BUY  = 1,       // Buy only
   AUTH_SELL = 2        // Sell only
  };

enum ENUM_DAILY_MODE
  {
   DAILY_NO      = 0,   // No
   DAILY_DYNAMIC = 1,   // Dynamic (% of start-of-day equity)
   DAILY_FIXED   = 2    // Fixed (account currency)
  };

enum ENUM_BB_SIGNAL
  {
   BB_OPEN_OUT_CLOSE_IN = 0, // Candle Opens Outside & Closes Inside
   BB_CLOSE_OUTSIDE     = 1, // Candle Closes Outside Bands
   BB_CLOSE_INSIDE      = 2  // Candle Closes Inside (after open inside)
  };

//==================================================================
// Inputs - Step 1: Grid Setup
//==================================================================
input group "=== Step 1: Grid Setup ==="
input int               InpGridSize         = 4;        // Grid Size
input ENUM_SPACING_TYPE InpSpacingType      = SPACING_STATIC; // Spacing Type
input int               InpSpacingPips      = 500;      // Spacing (in pips)
input int               InpATRPeriod        = 14;       // Dynamic Spacing - ATR Period
input double            InpATRMultiplier    = 1.0;      // Dynamic Spacing - ATR Multiplier
input double            InpHedgeTriggerMult = 1.0;      // Hedge Trigger Level (x Spacing)
input double            InpVolumePerOrder   = 0.01;     // Volume per Order
input bool              InpMoveGrid         = true;     // Move Grid (re-anchor after profit)

//==================================================================
// Inputs - Step 2: Trade Management
//==================================================================
input group "=== Step 2: Trade Management ==="
input bool              InpUseTakeProfit    = true;     // Take Profit per Trade
input double            InpTPPrimaryMult    = 1.0;      // TP per Primary Trade (x Spacing)
input double            InpTPHedgeMult      = 0.5;      // TP per Hedge Trade   (x Spacing)
input bool              InpCloseAtMaxProfit = false;    // Close Grid at Max Profit
input double            InpMaxGridProfit    = 0.0;      // Max Grid Profit (account currency)
input ENUM_DAILY_MODE   InpMaxProfitMode    = DAILY_NO; // Max Profit Daily
input double            InpMaxProfitValue   = 0.0;      // Max Profit Daily Value (ccy or %)
input ENUM_DAILY_MODE   InpMaxLossMode      = DAILY_NO; // Max Loss Daily
input double            InpMaxLossValue     = 0.0;      // Max Loss Daily Value   (ccy or %)
input int               InpMaxGridsPerDay   = 0;        // Max Grids Per Day (0 = no limit)
input bool              InpUseTimeFilter    = false;    // Allowed Time for First Trade
input int               InpStartHour        = 0;        // Start Hour (0-23, server time)
input int               InpEndHour          = 23;       // End Hour   (0-23, server time)

//==================================================================
// Inputs - Step 3: Entry Rules
//==================================================================
input group "=== Step 3: Entry Rules ==="
input ENUM_AUTH_ORDERS  InpAuthOrders       = AUTH_ALL; // Authorized Order Types
input ENUM_TIMEFRAMES   InpBBTimeframe      = PERIOD_CURRENT; // BB Timeframe
input int               InpBBPeriod         = 20;       // BB Period
input double            InpBBDeviations     = 2.0;      // BB Deviations
input int               InpBBShift          = 0;        // BB Bands Shift
input ENUM_APPLIED_PRICE InpBBAppliedPrice  = PRICE_CLOSE; // BB Applied Price
input int               InpCandleShift      = 1;        // Signal Candle Shift (1 = last closed)
input ENUM_BB_SIGNAL    InpBBSignal         = BB_OPEN_OUT_CLOSE_IN; // BB Signal Type
input bool              InpCloseOnTrendChange = false;  // Close Orders on Trend Change
input long              InpMagicNumber      = 20260429; // Magic Number
input string            InpComment          = "BBGridHedge"; // Order Comment

//==================================================================
// Inputs - Dashboard
//==================================================================
input group "=== Dashboard ==="
input bool   InpShowDashboard    = true;       // Show On-Chart Dashboard
input int    InpDashX            = 16;         // Dashboard X (pixels)
input int    InpDashY            = 24;         // Dashboard Y (pixels)
input int    InpDashWidth        = 360;        // Dashboard Width (pixels)
input int    InpDashFontSize     = 9;          // Dashboard Font Size
input string InpDashFont         = "Consolas"; // Dashboard Font
input color  InpDashBgColor      = clrBlack;   // Dashboard Background
input color  InpDashTextColor    = clrWhite;   // Dashboard Default Text Color

//==================================================================
// Globals
//==================================================================
CTrade        trade;
CPositionInfo posInfo;
COrderInfo    ordInfo;
CSymbolInfo   symInfo;

int      gBBHandle         = INVALID_HANDLE;
int      gATRHandle        = INVALID_HANDLE;
datetime gLastSignalBar    = 0;
datetime gCurrentDay       = 0;
double   gDailyStartEquity = 0.0;
int      gGridsOpenedToday = 0;
bool     gDailyStopHit     = false;

bool     gPrimaryActive    = false;
bool     gHedgeActive      = false;
int      gPrimaryDir       = 0;   // +1 = buy, -1 = sell
int      gHedgeDir         = 0;
double   gPrimaryAnchor    = 0.0;
double   gHedgeAnchor      = 0.0;
double   gPrimarySpacing   = 0.0;
double   gHedgeSpacing     = 0.0;

//==================================================================
// Helpers
//==================================================================
double PipSize()
  {
   double point = SymbolInfoDouble(_Symbol, SYMBOL_POINT);
   int    digits = (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);
   return ((digits == 3 || digits == 5) ? point * 10.0 : point);
  }

double PipsToPrice(const double pips)
  {
   return pips * PipSize();
  }

double NormalizePrice(const double p)
  {
   double tick = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
   if(tick <= 0.0)
      return NormalizeDouble(p, _Digits);
   return NormalizeDouble(MathRound(p / tick) * tick, _Digits);
  }

double NormalizeVolume(double v)
  {
   double minLot  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double maxLot  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
   double lotStep = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
   if(lotStep <= 0.0) lotStep = 0.01;
   v = MathMax(minLot, MathMin(maxLot, v));
   v = MathRound(v / lotStep) * lotStep;
   return NormalizeDouble(v, 2);
  }

double CurrentSpacingPrice()
  {
   if(InpSpacingType == SPACING_STATIC)
      return PipsToPrice((double)InpSpacingPips);

   if(gATRHandle == INVALID_HANDLE)
      return PipsToPrice((double)InpSpacingPips);

   double atr[];
   if(CopyBuffer(gATRHandle, 0, 1, 1, atr) <= 0)
      return PipsToPrice((double)InpSpacingPips);

   double v = atr[0] * InpATRMultiplier;
   if(v <= 0.0)
      v = PipsToPrice((double)InpSpacingPips);
   return v;
  }

bool IsNewSignalBar()
  {
   datetime t[];
   if(CopyTime(_Symbol, InpBBTimeframe, 0, 1, t) <= 0)
      return false;
   if(t[0] != gLastSignalBar)
     {
      gLastSignalBar = t[0];
      return true;
     }
   return false;
  }

bool IsTimeAllowed()
  {
   if(!InpUseTimeFilter)
      return true;
   MqlDateTime dt;
   TimeToStruct(TimeCurrent(), dt);
   int h = dt.hour;
   if(InpStartHour <= InpEndHour)
      return (h >= InpStartHour && h <= InpEndHour);
   // wraps midnight
   return (h >= InpStartHour || h <= InpEndHour);
  }

void RolloverDayIfNeeded()
  {
   MqlDateTime dt;
   TimeToStruct(TimeCurrent(), dt);
   dt.hour = 0; dt.min = 0; dt.sec = 0;
   datetime today = StructToTime(dt);
   if(today != gCurrentDay)
     {
      gCurrentDay        = today;
      gDailyStartEquity  = AccountInfoDouble(ACCOUNT_EQUITY);
      gGridsOpenedToday  = 0;
      gDailyStopHit      = false;
     }
  }

bool DailyLimitsHit()
  {
   if(gDailyStopHit)
      return true;
   double equity = AccountInfoDouble(ACCOUNT_EQUITY);
   double pnl    = equity - gDailyStartEquity;

   if(InpMaxProfitMode == DAILY_FIXED && InpMaxProfitValue > 0.0 && pnl >= InpMaxProfitValue)
      gDailyStopHit = true;
   if(InpMaxProfitMode == DAILY_DYNAMIC && InpMaxProfitValue > 0.0
      && gDailyStartEquity > 0.0
      && pnl >= gDailyStartEquity * InpMaxProfitValue / 100.0)
      gDailyStopHit = true;

   if(InpMaxLossMode == DAILY_FIXED && InpMaxLossValue > 0.0 && -pnl >= InpMaxLossValue)
      gDailyStopHit = true;
   if(InpMaxLossMode == DAILY_DYNAMIC && InpMaxLossValue > 0.0
      && gDailyStartEquity > 0.0
      && -pnl >= gDailyStartEquity * InpMaxLossValue / 100.0)
      gDailyStopHit = true;

   return gDailyStopHit;
  }

//==================================================================
// Position / order ownership
//==================================================================
bool IsOurs(const ulong ticket, const string kind)
  {
   if(kind == "POS")
     {
      if(!posInfo.SelectByTicket(ticket)) return false;
      return (posInfo.Symbol() == _Symbol && posInfo.Magic() == InpMagicNumber);
     }
   if(!ordInfo.Select(ticket)) return false;
   return (ordInfo.Symbol() == _Symbol && ordInfo.Magic() == InpMagicNumber);
  }

int CountPositions(const int dir)
  {
   int n = 0;
   for(int i = PositionsTotal() - 1; i >= 0; --i)
     {
      ulong ticket = PositionGetTicket(i);
      if(!posInfo.SelectByTicket(ticket)) continue;
      if(posInfo.Symbol() != _Symbol || posInfo.Magic() != InpMagicNumber) continue;
      int posDir = (posInfo.PositionType() == POSITION_TYPE_BUY) ? +1 : -1;
      if(dir == 0 || posDir == dir)
         n++;
     }
   return n;
  }

int CountPendingOrders(const int dir)
  {
   int n = 0;
   for(int i = OrdersTotal() - 1; i >= 0; --i)
     {
      ulong ticket = OrderGetTicket(i);
      if(!ordInfo.Select(ticket)) continue;
      if(ordInfo.Symbol() != _Symbol || ordInfo.Magic() != InpMagicNumber) continue;
      ENUM_ORDER_TYPE t = ordInfo.OrderType();
      int oDir = 0;
      if(t == ORDER_TYPE_BUY_LIMIT || t == ORDER_TYPE_BUY_STOP)  oDir = +1;
      if(t == ORDER_TYPE_SELL_LIMIT || t == ORDER_TYPE_SELL_STOP) oDir = -1;
      if(oDir == 0) continue;
      if(dir == 0 || oDir == dir)
         n++;
     }
   return n;
  }

double FloatingPnLForDir(const int dir)
  {
   double pnl = 0.0;
   for(int i = PositionsTotal() - 1; i >= 0; --i)
     {
      ulong ticket = PositionGetTicket(i);
      if(!posInfo.SelectByTicket(ticket)) continue;
      if(posInfo.Symbol() != _Symbol || posInfo.Magic() != InpMagicNumber) continue;
      int posDir = (posInfo.PositionType() == POSITION_TYPE_BUY) ? +1 : -1;
      if(dir != 0 && posDir != dir) continue;
      pnl += posInfo.Profit() + posInfo.Swap() + posInfo.Commission();
     }
   return pnl;
  }

void DeletePendingForDir(const int dir)
  {
   for(int i = OrdersTotal() - 1; i >= 0; --i)
     {
      ulong ticket = OrderGetTicket(i);
      if(!ordInfo.Select(ticket)) continue;
      if(ordInfo.Symbol() != _Symbol || ordInfo.Magic() != InpMagicNumber) continue;
      ENUM_ORDER_TYPE t = ordInfo.OrderType();
      int oDir = 0;
      if(t == ORDER_TYPE_BUY_LIMIT || t == ORDER_TYPE_BUY_STOP)  oDir = +1;
      if(t == ORDER_TYPE_SELL_LIMIT || t == ORDER_TYPE_SELL_STOP) oDir = -1;
      if(oDir == 0) continue;
      if(dir != 0 && oDir != dir) continue;
      trade.OrderDelete(ticket);
     }
  }

void ClosePositionsForDir(const int dir)
  {
   for(int i = PositionsTotal() - 1; i >= 0; --i)
     {
      ulong ticket = PositionGetTicket(i);
      if(!posInfo.SelectByTicket(ticket)) continue;
      if(posInfo.Symbol() != _Symbol || posInfo.Magic() != InpMagicNumber) continue;
      int posDir = (posInfo.PositionType() == POSITION_TYPE_BUY) ? +1 : -1;
      if(dir != 0 && posDir != dir) continue;
      trade.PositionClose(ticket);
     }
  }

void CloseAllAndCancel()
  {
   DeletePendingForDir(0);
   ClosePositionsForDir(0);
  }

//==================================================================
// Bollinger Bands signal
//   +1 = buy signal, -1 = sell signal, 0 = none
//==================================================================
int CheckBBSignal()
  {
   if(gBBHandle == INVALID_HANDLE)
      return 0;

   int shift = MathMax(1, InpCandleShift);
   double upper[], lower[];
   if(CopyBuffer(gBBHandle, 1, shift, 1, upper) <= 0) return 0; // upper band buffer
   if(CopyBuffer(gBBHandle, 2, shift, 1, lower) <= 0) return 0; // lower band buffer

   double openP  = iOpen(_Symbol, InpBBTimeframe, shift);
   double closeP = iClose(_Symbol, InpBBTimeframe, shift);
   if(openP == 0.0 || closeP == 0.0) return 0;

   double up = upper[0];
   double lo = lower[0];

   bool buySignal  = false;
   bool sellSignal = false;

   switch(InpBBSignal)
     {
      case BB_OPEN_OUT_CLOSE_IN:
         buySignal  = (openP < lo  && closeP > lo  && closeP < up);
         sellSignal = (openP > up  && closeP < up  && closeP > lo);
         break;
      case BB_CLOSE_OUTSIDE:
         buySignal  = (closeP > up);
         sellSignal = (closeP < lo);
         break;
      case BB_CLOSE_INSIDE:
         {
            double openPrev[1], closePrev[1];
            int sh2 = shift + 1;
            double op = iOpen(_Symbol, InpBBTimeframe, sh2);
            double cp = iClose(_Symbol, InpBBTimeframe, sh2);
            buySignal  = (cp < lo  && closeP > lo  && closeP < up);
            sellSignal = (cp > up  && closeP < up  && closeP > lo);
         }
         break;
     }

   if(buySignal && (InpAuthOrders == AUTH_ALL || InpAuthOrders == AUTH_BUY))
      return +1;
   if(sellSignal && (InpAuthOrders == AUTH_ALL || InpAuthOrders == AUTH_SELL))
      return -1;
   return 0;
  }

//==================================================================
// Grid placement
//==================================================================
double ComputeTP(const int dir, const double openPrice, const double tpMult, const double spacing)
  {
   if(!InpUseTakeProfit || tpMult <= 0.0) return 0.0;
   double offset = tpMult * spacing;
   double tp = (dir > 0) ? openPrice + offset : openPrice - offset;
   return NormalizePrice(tp);
  }

bool OpenPrimaryGrid(const int dir)
  {
   if(!symInfo.RefreshRates()) return false;
   double price  = (dir > 0) ? symInfo.Ask() : symInfo.Bid();
   double spacing = CurrentSpacingPrice();
   double vol    = NormalizeVolume(InpVolumePerOrder);
   if(vol <= 0.0) return false;

   double tp = ComputeTP(dir, price, InpTPPrimaryMult, spacing);

   bool ok;
   if(dir > 0)
      ok = trade.Buy(vol, _Symbol, 0.0, 0.0, tp, InpComment + "-P0");
   else
      ok = trade.Sell(vol, _Symbol, 0.0, 0.0, tp, InpComment + "-P0");

   if(!ok)
     {
      PrintFormat("Primary market order failed: %d %s", trade.ResultRetcode(), trade.ResultRetcodeDescription());
      return false;
     }

   double anchor = trade.ResultPrice();
   if(anchor <= 0.0) anchor = price;

   gPrimaryActive  = true;
   gPrimaryDir     = dir;
   gPrimaryAnchor  = anchor;
   gPrimarySpacing = spacing;

   // Place pending averaging orders against direction (limit)
   for(int i = 1; i < InpGridSize; ++i)
     {
      double pendingPrice = (dir > 0) ? anchor - i * spacing : anchor + i * spacing;
      pendingPrice = NormalizePrice(pendingPrice);
      double pendingTP    = ComputeTP(dir, pendingPrice, InpTPPrimaryMult, spacing);
      string cmt = InpComment + "-P" + IntegerToString(i);
      bool placed;
      if(dir > 0)
         placed = trade.BuyLimit(vol, pendingPrice, _Symbol, 0.0, pendingTP, ORDER_TIME_GTC, 0, cmt);
      else
         placed = trade.SellLimit(vol, pendingPrice, _Symbol, 0.0, pendingTP, ORDER_TIME_GTC, 0, cmt);
      if(!placed)
         PrintFormat("Primary pending #%d failed: %d %s", i, trade.ResultRetcode(), trade.ResultRetcodeDescription());
     }

   gGridsOpenedToday++;
   PrintFormat("Primary grid opened. Dir=%d Anchor=%.5f Spacing=%.5f", dir, anchor, spacing);
   return true;
  }

bool OpenHedgeGrid(const int hedgeDir)
  {
   if(!symInfo.RefreshRates()) return false;
   double price  = (hedgeDir > 0) ? symInfo.Ask() : symInfo.Bid();
   double spacing = gPrimarySpacing > 0.0 ? gPrimarySpacing : CurrentSpacingPrice();
   double vol    = NormalizeVolume(InpVolumePerOrder);
   if(vol <= 0.0) return false;

   double tp = ComputeTP(hedgeDir, price, InpTPHedgeMult, spacing);

   bool ok;
   if(hedgeDir > 0)
      ok = trade.Buy(vol, _Symbol, 0.0, 0.0, tp, InpComment + "-H0");
   else
      ok = trade.Sell(vol, _Symbol, 0.0, 0.0, tp, InpComment + "-H0");

   if(!ok)
     {
      PrintFormat("Hedge market order failed: %d %s", trade.ResultRetcode(), trade.ResultRetcodeDescription());
      return false;
     }

   double anchor = trade.ResultPrice();
   if(anchor <= 0.0) anchor = price;

   gHedgeActive  = true;
   gHedgeDir     = hedgeDir;
   gHedgeAnchor  = anchor;
   gHedgeSpacing = spacing;

   // Hedge pending orders are STOPS in the hedge direction so they fill if price keeps moving with the hedge.
   for(int i = 1; i < InpGridSize; ++i)
     {
      double pendingPrice = (hedgeDir > 0) ? anchor + i * spacing : anchor - i * spacing;
      pendingPrice = NormalizePrice(pendingPrice);
      double pendingTP    = ComputeTP(hedgeDir, pendingPrice, InpTPHedgeMult, spacing);
      string cmt = InpComment + "-H" + IntegerToString(i);
      bool placed;
      if(hedgeDir > 0)
         placed = trade.BuyStop(vol, pendingPrice, _Symbol, 0.0, pendingTP, ORDER_TIME_GTC, 0, cmt);
      else
         placed = trade.SellStop(vol, pendingPrice, _Symbol, 0.0, pendingTP, ORDER_TIME_GTC, 0, cmt);
      if(!placed)
         PrintFormat("Hedge pending #%d failed: %d %s", i, trade.ResultRetcode(), trade.ResultRetcodeDescription());
     }

   PrintFormat("Hedge grid opened. Dir=%d Anchor=%.5f Spacing=%.5f", hedgeDir, anchor, spacing);
   return true;
  }

//==================================================================
// State management - check whether grid is still alive, handle
// hedge trigger, max profit, move grid, trend change.
//==================================================================
bool HasAnyOurExposure()
  {
   return (CountPositions(0) > 0 || CountPendingOrders(0) > 0);
  }

void RefreshGridState()
  {
   // If no positions and no pending orders for primary or hedge, mark inactive.
   if(gPrimaryActive)
     {
      if(CountPositions(gPrimaryDir) == 0 && CountPendingOrders(gPrimaryDir) == 0)
        {
         gPrimaryActive = false;
         PrintFormat("Primary grid closed.");
        }
     }
   if(gHedgeActive)
     {
      if(CountPositions(gHedgeDir) == 0 && CountPendingOrders(gHedgeDir) == 0)
        {
         gHedgeActive = false;
         PrintFormat("Hedge grid closed.");
        }
     }
   if(!gPrimaryActive && !gHedgeActive)
     {
      gPrimaryDir = 0; gHedgeDir = 0;
      gPrimaryAnchor = 0.0; gHedgeAnchor = 0.0;
     }
  }

void CheckHedgeTrigger()
  {
   if(!gPrimaryActive || gHedgeActive) return;
   if(InpHedgeTriggerMult <= 0.0) return;
   if(!symInfo.RefreshRates()) return;

   double trigger = InpHedgeTriggerMult * gPrimarySpacing;
   double price   = (gPrimaryDir > 0) ? symInfo.Bid() : symInfo.Ask();
   bool   triggered = false;
   if(gPrimaryDir > 0 && price <= gPrimaryAnchor - trigger) triggered = true;
   if(gPrimaryDir < 0 && price >= gPrimaryAnchor + trigger) triggered = true;

   if(triggered)
      OpenHedgeGrid(-gPrimaryDir);
  }

void CheckMaxGridProfit()
  {
   if(!InpCloseAtMaxProfit || InpMaxGridProfit <= 0.0) return;
   double pnl = FloatingPnLForDir(0);
   if(pnl >= InpMaxGridProfit)
     {
      PrintFormat("Max grid profit reached (%.2f). Closing all.", pnl);
      CloseAllAndCancel();
     }
  }

void CheckMoveGrid()
  {
   if(!InpMoveGrid) return;
   if(!gPrimaryActive) return;

   // Re-anchor primary if price has moved 1 spacing in primary direction beyond anchor
   // and there are no pending primary orders left (i.e. all averaging slots used or
   // primary has been "running" with profit).
   if(!symInfo.RefreshRates()) return;
   double price = (gPrimaryDir > 0) ? symInfo.Bid() : symInfo.Ask();
   double moved = (gPrimaryDir > 0) ? price - gPrimaryAnchor : gPrimaryAnchor - price;
   if(moved < gPrimarySpacing) return;
   if(CountPendingOrders(gPrimaryDir) > 0) return;

   // Cancel any leftover primary pendings (none here) and place a fresh ladder
   // anchored at current price for the remaining grid slots, sized to GridSize - existing positions.
   int existing = CountPositions(gPrimaryDir);
   int remaining = InpGridSize - existing;
   if(remaining <= 0) return;

   double newAnchor = price;
   double vol = NormalizeVolume(InpVolumePerOrder);
   for(int i = 1; i <= remaining; ++i)
     {
      double pendingPrice = (gPrimaryDir > 0) ? newAnchor - i * gPrimarySpacing : newAnchor + i * gPrimarySpacing;
      pendingPrice = NormalizePrice(pendingPrice);
      double pendingTP   = ComputeTP(gPrimaryDir, pendingPrice, InpTPPrimaryMult, gPrimarySpacing);
      string cmt = InpComment + "-PM" + IntegerToString(i);
      bool placed;
      if(gPrimaryDir > 0)
         placed = trade.BuyLimit(vol, pendingPrice, _Symbol, 0.0, pendingTP, ORDER_TIME_GTC, 0, cmt);
      else
         placed = trade.SellLimit(vol, pendingPrice, _Symbol, 0.0, pendingTP, ORDER_TIME_GTC, 0, cmt);
      if(!placed)
         PrintFormat("MoveGrid pending #%d failed: %d %s", i, trade.ResultRetcode(), trade.ResultRetcodeDescription());
     }
   gPrimaryAnchor = newAnchor;
   PrintFormat("Grid moved. New anchor=%.5f", newAnchor);
  }

void CheckTrendChange(const int newSignal)
  {
   if(!InpCloseOnTrendChange) return;
   if(newSignal == 0) return;
   if(gPrimaryActive && newSignal != gPrimaryDir)
     {
      PrintFormat("Trend change detected (signal=%d, primary=%d). Closing all.", newSignal, gPrimaryDir);
      CloseAllAndCancel();
     }
  }

//==================================================================
// Dashboard
//==================================================================
#define DASH_PREFIX "BBGH_DASH_"

int    gDashRowCount = 0;
string gLastSignalText = "-";
datetime gLastSignalTime = 0;

void DashDeleteAll()
  {
   ObjectsDeleteAll(0, DASH_PREFIX);
   ChartRedraw(0);
  }

void DashCreateRect(const string name, const int x, const int y,
                    const int width, const int height, const color bg)
  {
   if(ObjectFind(0, name) < 0)
      ObjectCreate(0, name, OBJ_RECTANGLE_LABEL, 0, 0, 0);
   ObjectSetInteger(0, name, OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetInteger(0, name, OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, name, OBJPROP_YDISTANCE, y);
   ObjectSetInteger(0, name, OBJPROP_XSIZE, width);
   ObjectSetInteger(0, name, OBJPROP_YSIZE, height);
   ObjectSetInteger(0, name, OBJPROP_BGCOLOR, bg);
   ObjectSetInteger(0, name, OBJPROP_BORDER_TYPE, BORDER_FLAT);
   ObjectSetInteger(0, name, OBJPROP_COLOR, bg);
   ObjectSetInteger(0, name, OBJPROP_STYLE, STYLE_SOLID);
   ObjectSetInteger(0, name, OBJPROP_WIDTH, 1);
   ObjectSetInteger(0, name, OBJPROP_BACK, false);
   ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, name, OBJPROP_HIDDEN, true);
   ObjectSetInteger(0, name, OBJPROP_ZORDER, 0);
  }

void DashCreateLabel(const string name, const int x, const int y,
                     const string text, const color clr,
                     const int fontSize, const string font,
                     const int anchor = ANCHOR_LEFT_UPPER)
  {
   if(ObjectFind(0, name) < 0)
      ObjectCreate(0, name, OBJ_LABEL, 0, 0, 0);
   ObjectSetInteger(0, name, OBJPROP_CORNER, CORNER_LEFT_UPPER);
   ObjectSetInteger(0, name, OBJPROP_ANCHOR, anchor);
   ObjectSetInteger(0, name, OBJPROP_XDISTANCE, x);
   ObjectSetInteger(0, name, OBJPROP_YDISTANCE, y);
   ObjectSetString (0, name, OBJPROP_TEXT, text);
   ObjectSetInteger(0, name, OBJPROP_COLOR, clr);
   ObjectSetInteger(0, name, OBJPROP_FONTSIZE, fontSize);
   ObjectSetString (0, name, OBJPROP_FONT, font);
   ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
   ObjectSetInteger(0, name, OBJPROP_HIDDEN, true);
   ObjectSetInteger(0, name, OBJPROP_ZORDER, 1);
  }

void DashUpdateLabel(const string name, const string text, const color clr)
  {
   if(ObjectFind(0, name) < 0) return;
   ObjectSetString (0, name, OBJPROP_TEXT,  text);
   ObjectSetInteger(0, name, OBJPROP_COLOR, clr);
  }

color ProfitColor(const double v)
  {
   if(v > 0.0) return clrLime;
   if(v < 0.0) return clrTomato;
   return InpDashTextColor;
  }

color DirColor(const int dir)
  {
   if(dir > 0) return clrDeepSkyBlue;
   if(dir < 0) return clrHotPink;
   return clrSilver;
  }

string DirText(const int dir)
  {
   if(dir > 0) return "BUY";
   if(dir < 0) return "SELL";
   return "-";
  }

string TfToString(const ENUM_TIMEFRAMES tf)
  {
   switch(tf)
     {
      case PERIOD_M1:  return "M1";
      case PERIOD_M5:  return "M5";
      case PERIOD_M15: return "M15";
      case PERIOD_M30: return "M30";
      case PERIOD_H1:  return "H1";
      case PERIOD_H4:  return "H4";
      case PERIOD_D1:  return "D1";
      case PERIOD_W1:  return "W1";
      case PERIOD_MN1: return "MN1";
      default: return EnumToString(tf);
     }
  }

void DashSection(int &y, const string title, const color headerColor,
                 const int rows, const int rowHeight,
                 const int titleHeight, const string id)
  {
   int x  = InpDashX;
   int w  = InpDashWidth;
   int h  = titleHeight + rows * rowHeight + 6;
   DashCreateRect(DASH_PREFIX + id + "_BG", x, y, w, h, InpDashBgColor);
   DashCreateRect(DASH_PREFIX + id + "_HDR", x, y, w, titleHeight, headerColor);
   DashCreateLabel(DASH_PREFIX + id + "_TTL", x + 8, y + 3, title,
                   clrWhite, InpDashFontSize + 1, InpDashFont);
   y += titleHeight + 4;
  }

void DashRow(int &y, const string id, const string label, const string value,
             const color valueColor, const int rowHeight)
  {
   int x = InpDashX;
   int w = InpDashWidth;
   DashCreateLabel(DASH_PREFIX + id + "_L", x + 10,        y, label,
                   clrLightGray, InpDashFontSize, InpDashFont);
   DashCreateLabel(DASH_PREFIX + id + "_V", x + w - 10,    y, value,
                   valueColor, InpDashFontSize, InpDashFont, ANCHOR_RIGHT_UPPER);
   y += rowHeight;
  }

void DashboardInit()
  {
   if(!InpShowDashboard) return;
   DashDeleteAll();

   const int rowH   = InpDashFontSize + 8;
   const int titleH = InpDashFontSize + 10;
   int y = InpDashY;

   // Header bar
   DashCreateRect(DASH_PREFIX + "MAIN_HDR", InpDashX, y, InpDashWidth, titleH + 6, clrRoyalBlue);
   DashCreateLabel(DASH_PREFIX + "MAIN_TTL", InpDashX + 10, y + 4,
                   "BOLLINGER GRID HEDGE  -  " + _Symbol + " " + TfToString(InpBBTimeframe),
                   clrWhite, InpDashFontSize + 2, InpDashFont);
   y += titleH + 10;

   // Sections (placeholder rows; updated each tick)
   DashSection(y, "ACCOUNT",      clrTeal,        4, rowH, titleH, "ACC");
   DashRow(y, "ACC_BAL",  "Balance",     "-", InpDashTextColor, rowH);
   DashRow(y, "ACC_EQ",   "Equity",      "-", InpDashTextColor, rowH);
   DashRow(y, "ACC_MGN",  "Margin",      "-", InpDashTextColor, rowH);
   DashRow(y, "ACC_FREE", "Free Margin", "-", InpDashTextColor, rowH);
   y += 6;

   DashSection(y, "DAILY STATS",  clrPurple,      5, rowH, titleH, "DAY");
   DashRow(y, "DAY_START", "Start Equity",  "-", InpDashTextColor, rowH);
   DashRow(y, "DAY_PNL",   "Today PnL",     "-", InpDashTextColor, rowH);
   DashRow(y, "DAY_GRIDS", "Grids Today",   "-", InpDashTextColor, rowH);
   DashRow(y, "DAY_LIM",   "Profit / Loss Cap", "-", InpDashTextColor, rowH);
   DashRow(y, "DAY_STOP",  "Daily Stop",    "-", InpDashTextColor, rowH);
   y += 6;

   DashSection(y, "SIGNAL",       clrDarkOrange,  6, rowH, titleH, "SIG");
   DashRow(y, "SIG_BIDASK", "Bid / Ask",     "-", InpDashTextColor, rowH);
   DashRow(y, "SIG_SPREAD", "Spread (pips)", "-", InpDashTextColor, rowH);
   DashRow(y, "SIG_BBU",    "BB Upper",      "-", InpDashTextColor, rowH);
   DashRow(y, "SIG_BBM",    "BB Middle",     "-", InpDashTextColor, rowH);
   DashRow(y, "SIG_BBL",    "BB Lower",      "-", InpDashTextColor, rowH);
   DashRow(y, "SIG_LAST",   "Last Signal",   "-", InpDashTextColor, rowH);
   y += 6;

   DashSection(y, "PRIMARY GRID", clrSeaGreen,    7, rowH, titleH, "PRI");
   DashRow(y, "PRI_ACT",  "Status",       "-", InpDashTextColor, rowH);
   DashRow(y, "PRI_DIR",  "Direction",    "-", InpDashTextColor, rowH);
   DashRow(y, "PRI_ANC",  "Anchor",       "-", InpDashTextColor, rowH);
   DashRow(y, "PRI_SP",   "Spacing",      "-", InpDashTextColor, rowH);
   DashRow(y, "PRI_POS",  "Open Lots",    "-", InpDashTextColor, rowH);
   DashRow(y, "PRI_PEN",  "Pending Orders","-", InpDashTextColor, rowH);
   DashRow(y, "PRI_PNL",  "Floating PnL", "-", InpDashTextColor, rowH);
   y += 6;

   DashSection(y, "HEDGE GRID",   clrCrimson,     7, rowH, titleH, "HED");
   DashRow(y, "HED_ACT",  "Status",        "-", InpDashTextColor, rowH);
   DashRow(y, "HED_DIR",  "Direction",     "-", InpDashTextColor, rowH);
   DashRow(y, "HED_ANC",  "Anchor",        "-", InpDashTextColor, rowH);
   DashRow(y, "HED_TRG",  "Trigger Price", "-", InpDashTextColor, rowH);
   DashRow(y, "HED_POS",  "Open Lots",     "-", InpDashTextColor, rowH);
   DashRow(y, "HED_PEN",  "Pending Orders","-", InpDashTextColor, rowH);
   DashRow(y, "HED_PNL",  "Floating PnL",  "-", InpDashTextColor, rowH);
   y += 6;

   DashSection(y, "SETTINGS",     clrSlateGray,   7, rowH, titleH, "SET");
   DashRow(y, "SET_GS",  "Grid Size",         IntegerToString(InpGridSize), InpDashTextColor, rowH);
   DashRow(y, "SET_SP",  "Spacing",
           (InpSpacingType == SPACING_STATIC ?
            IntegerToString(InpSpacingPips) + " pips (Static)" :
            "ATR x " + DoubleToString(InpATRMultiplier, 2) + " (Dynamic)"),
           InpDashTextColor, rowH);
   DashRow(y, "SET_HT",  "Hedge Trigger",     DoubleToString(InpHedgeTriggerMult, 2) + " x Spacing",
           InpDashTextColor, rowH);
   DashRow(y, "SET_TPP", "TP Primary",
           InpUseTakeProfit ? DoubleToString(InpTPPrimaryMult, 2) + " x Spacing" : "off",
           InpDashTextColor, rowH);
   DashRow(y, "SET_TPH", "TP Hedge",
           InpUseTakeProfit ? DoubleToString(InpTPHedgeMult, 2) + " x Spacing" : "off",
           InpDashTextColor, rowH);
   DashRow(y, "SET_VOL", "Volume / Order",    DoubleToString(InpVolumePerOrder, 2),
           InpDashTextColor, rowH);
   DashRow(y, "SET_MV",  "Move Grid / Time",
           (InpMoveGrid ? "ON / " : "OFF / ") +
           (InpUseTimeFilter ? IntegerToString(InpStartHour) + "-" + IntegerToString(InpEndHour) : "any"),
           InpDashTextColor, rowH);

   ChartRedraw(0);
  }

double SumLotsForDir(const int dir)
  {
   double v = 0.0;
   for(int i = PositionsTotal() - 1; i >= 0; --i)
     {
      ulong ticket = PositionGetTicket(i);
      if(!posInfo.SelectByTicket(ticket)) continue;
      if(posInfo.Symbol() != _Symbol || posInfo.Magic() != InpMagicNumber) continue;
      int posDir = (posInfo.PositionType() == POSITION_TYPE_BUY) ? +1 : -1;
      if(dir != 0 && posDir != dir) continue;
      v += posInfo.Volume();
     }
   return v;
  }

void DashboardUpdate()
  {
   if(!InpShowDashboard) return;

   const int rowH = InpDashFontSize + 8;

   // Account
   double bal  = AccountInfoDouble(ACCOUNT_BALANCE);
   double eq   = AccountInfoDouble(ACCOUNT_EQUITY);
   double mgn  = AccountInfoDouble(ACCOUNT_MARGIN);
   double free = AccountInfoDouble(ACCOUNT_FREEMARGIN);
   string ccy  = AccountInfoString(ACCOUNT_CURRENCY);
   DashUpdateLabel(DASH_PREFIX + "ACC_BAL_V",  DoubleToString(bal, 2)  + " " + ccy, InpDashTextColor);
   DashUpdateLabel(DASH_PREFIX + "ACC_EQ_V",   DoubleToString(eq, 2)   + " " + ccy, ProfitColor(eq - bal));
   DashUpdateLabel(DASH_PREFIX + "ACC_MGN_V",  DoubleToString(mgn, 2)  + " " + ccy, InpDashTextColor);
   DashUpdateLabel(DASH_PREFIX + "ACC_FREE_V", DoubleToString(free, 2) + " " + ccy, InpDashTextColor);

   // Daily
   double dayPnL = eq - gDailyStartEquity;
   DashUpdateLabel(DASH_PREFIX + "DAY_START_V", DoubleToString(gDailyStartEquity, 2) + " " + ccy, InpDashTextColor);
   DashUpdateLabel(DASH_PREFIX + "DAY_PNL_V",   DoubleToString(dayPnL, 2) + " " + ccy, ProfitColor(dayPnL));
   string gridsTxt = IntegerToString(gGridsOpenedToday);
   if(InpMaxGridsPerDay > 0) gridsTxt += " / " + IntegerToString(InpMaxGridsPerDay);
   DashUpdateLabel(DASH_PREFIX + "DAY_GRIDS_V", gridsTxt, InpDashTextColor);

   string profitCap = (InpMaxProfitMode == DAILY_NO ? "off" :
                      (InpMaxProfitMode == DAILY_FIXED ? DoubleToString(InpMaxProfitValue, 2) + " " + ccy
                                                       : DoubleToString(InpMaxProfitValue, 2) + " %"));
   string lossCap   = (InpMaxLossMode   == DAILY_NO ? "off" :
                      (InpMaxLossMode   == DAILY_FIXED ? DoubleToString(InpMaxLossValue, 2) + " " + ccy
                                                       : DoubleToString(InpMaxLossValue, 2) + " %"));
   DashUpdateLabel(DASH_PREFIX + "DAY_LIM_V",  profitCap + "  /  " + lossCap, InpDashTextColor);
   DashUpdateLabel(DASH_PREFIX + "DAY_STOP_V",
                   gDailyStopHit ? "HIT - paused" : "active",
                   gDailyStopHit ? clrTomato : clrLime);

   // Signal
   if(symInfo.RefreshRates())
     {
      double bid = symInfo.Bid();
      double ask = symInfo.Ask();
      double spreadPips = (ask - bid) / PipSize();
      DashUpdateLabel(DASH_PREFIX + "SIG_BIDASK_V",
                      DoubleToString(bid, _Digits) + " / " + DoubleToString(ask, _Digits),
                      InpDashTextColor);
      DashUpdateLabel(DASH_PREFIX + "SIG_SPREAD_V", DoubleToString(spreadPips, 1), InpDashTextColor);
     }

   double up[1], mid[1], lo[1];
   if(gBBHandle != INVALID_HANDLE
      && CopyBuffer(gBBHandle, 1, MathMax(1, InpCandleShift), 1, up)  > 0
      && CopyBuffer(gBBHandle, 0, MathMax(1, InpCandleShift), 1, mid) > 0
      && CopyBuffer(gBBHandle, 2, MathMax(1, InpCandleShift), 1, lo)  > 0)
     {
      DashUpdateLabel(DASH_PREFIX + "SIG_BBU_V", DoubleToString(up[0],  _Digits), clrLightSkyBlue);
      DashUpdateLabel(DASH_PREFIX + "SIG_BBM_V", DoubleToString(mid[0], _Digits), clrGold);
      DashUpdateLabel(DASH_PREFIX + "SIG_BBL_V", DoubleToString(lo[0],  _Digits), clrLightPink);
     }

   string lastSig = gLastSignalText;
   if(gLastSignalTime > 0)
      lastSig += "  @ " + TimeToString(gLastSignalTime, TIME_DATE | TIME_MINUTES);
   color lastClr = (StringFind(gLastSignalText, "BUY")  >= 0) ? clrDeepSkyBlue :
                   (StringFind(gLastSignalText, "SELL") >= 0) ? clrHotPink     : clrSilver;
   DashUpdateLabel(DASH_PREFIX + "SIG_LAST_V", lastSig, lastClr);

   // Primary
   DashUpdateLabel(DASH_PREFIX + "PRI_ACT_V",
                   gPrimaryActive ? "ACTIVE" : "idle",
                   gPrimaryActive ? clrLime : clrSilver);
   DashUpdateLabel(DASH_PREFIX + "PRI_DIR_V", DirText(gPrimaryDir), DirColor(gPrimaryDir));
   DashUpdateLabel(DASH_PREFIX + "PRI_ANC_V",
                   gPrimaryAnchor > 0.0 ? DoubleToString(gPrimaryAnchor, _Digits) : "-",
                   InpDashTextColor);
   DashUpdateLabel(DASH_PREFIX + "PRI_SP_V",
                   gPrimarySpacing > 0.0 ? DoubleToString(gPrimarySpacing / PipSize(), 1) + " pips"
                                         : DoubleToString((double)InpSpacingPips, 1) + " pips",
                   InpDashTextColor);
   double priLots = SumLotsForDir(gPrimaryDir);
   DashUpdateLabel(DASH_PREFIX + "PRI_POS_V",
                   IntegerToString(CountPositions(gPrimaryDir)) + " (" + DoubleToString(priLots, 2) + " lots)",
                   InpDashTextColor);
   DashUpdateLabel(DASH_PREFIX + "PRI_PEN_V", IntegerToString(CountPendingOrders(gPrimaryDir)), InpDashTextColor);
   double priPnL = FloatingPnLForDir(gPrimaryDir);
   DashUpdateLabel(DASH_PREFIX + "PRI_PNL_V", DoubleToString(priPnL, 2) + " " + ccy, ProfitColor(priPnL));

   // Hedge
   DashUpdateLabel(DASH_PREFIX + "HED_ACT_V",
                   gHedgeActive ? "ACTIVE" : "idle",
                   gHedgeActive ? clrLime : clrSilver);
   DashUpdateLabel(DASH_PREFIX + "HED_DIR_V", DirText(gHedgeDir), DirColor(gHedgeDir));
   DashUpdateLabel(DASH_PREFIX + "HED_ANC_V",
                   gHedgeAnchor > 0.0 ? DoubleToString(gHedgeAnchor, _Digits) : "-",
                   InpDashTextColor);

   string trgTxt = "-";
   if(gPrimaryActive && gPrimarySpacing > 0.0 && InpHedgeTriggerMult > 0.0)
     {
      double trgPrice = (gPrimaryDir > 0)
                        ? gPrimaryAnchor - InpHedgeTriggerMult * gPrimarySpacing
                        : gPrimaryAnchor + InpHedgeTriggerMult * gPrimarySpacing;
      trgTxt = DoubleToString(trgPrice, _Digits);
     }
   DashUpdateLabel(DASH_PREFIX + "HED_TRG_V", trgTxt, clrOrange);
   double hedLots = SumLotsForDir(gHedgeDir);
   DashUpdateLabel(DASH_PREFIX + "HED_POS_V",
                   IntegerToString(CountPositions(gHedgeDir)) + " (" + DoubleToString(hedLots, 2) + " lots)",
                   InpDashTextColor);
   DashUpdateLabel(DASH_PREFIX + "HED_PEN_V", IntegerToString(CountPendingOrders(gHedgeDir)), InpDashTextColor);
   double hedPnL = FloatingPnLForDir(gHedgeDir);
   DashUpdateLabel(DASH_PREFIX + "HED_PNL_V", DoubleToString(hedPnL, 2) + " " + ccy, ProfitColor(hedPnL));

   ChartRedraw(0);
  }

//==================================================================
// MQL5 lifecycle
//==================================================================
int OnInit()
  {
   trade.SetExpertMagicNumber((ulong)InpMagicNumber);
   trade.SetTypeFillingBySymbol(_Symbol);
   trade.SetMarginMode();
   trade.SetDeviationInPoints(20);

   if(!symInfo.Name(_Symbol))
     {
      Print("Failed to attach symbol info");
      return INIT_FAILED;
     }

   gBBHandle = iBands(_Symbol, InpBBTimeframe, InpBBPeriod, InpBBShift, InpBBDeviations, InpBBAppliedPrice);
   if(gBBHandle == INVALID_HANDLE)
     {
      Print("Failed to create Bollinger Bands handle");
      return INIT_FAILED;
     }

   if(InpSpacingType == SPACING_DYNAMIC)
     {
      gATRHandle = iATR(_Symbol, InpBBTimeframe, InpATRPeriod);
      if(gATRHandle == INVALID_HANDLE)
        {
         Print("Failed to create ATR handle");
         return INIT_FAILED;
        }
     }

   RolloverDayIfNeeded();

   if(InpGridSize < 1)
     {
      Print("Grid Size must be >= 1");
      return INIT_PARAMETERS_INCORRECT;
     }
   if(InpVolumePerOrder <= 0.0)
     {
      Print("Volume per Order must be > 0");
      return INIT_PARAMETERS_INCORRECT;
     }

   DashboardInit();
   DashboardUpdate();
   EventSetTimer(1);

   PrintFormat("BollingerGridHedge initialised. Symbol=%s Pip=%.5f", _Symbol, PipSize());
   return INIT_SUCCEEDED;
  }

void OnDeinit(const int reason)
  {
   EventKillTimer();
   DashDeleteAll();
   if(gBBHandle  != INVALID_HANDLE) IndicatorRelease(gBBHandle);
   if(gATRHandle != INVALID_HANDLE) IndicatorRelease(gATRHandle);
  }

void OnTimer()
  {
   DashboardUpdate();
  }

void OnTick()
  {
   RolloverDayIfNeeded();
   RefreshGridState();

   // 1. Manage existing exposure: hedge trigger, move grid, max profit
   if(gPrimaryActive)
     {
      CheckHedgeTrigger();
      CheckMoveGrid();
     }
   CheckMaxGridProfit();

   // 2. Daily PnL stop
   if(DailyLimitsHit())
     {
      if(HasAnyOurExposure())
         CloseAllAndCancel();
      DashboardUpdate();
      return;
     }

   // 3. New entry only on a freshly closed bar
   if(!IsNewSignalBar())
     {
      DashboardUpdate();
      return;
     }

   int signal = CheckBBSignal();
   if(signal != 0)
     {
      gLastSignalText = (signal > 0 ? "BUY" : "SELL");
      gLastSignalTime = TimeCurrent();
     }
   if(signal == 0)
     {
      DashboardUpdate();
      return;
     }

   CheckTrendChange(signal);

   // No new primary while one is still active
   if(gPrimaryActive)
     {
      DashboardUpdate();
      return;
     }

   if(!IsTimeAllowed())
     {
      DashboardUpdate();
      return;
     }
   if(InpMaxGridsPerDay > 0 && gGridsOpenedToday >= InpMaxGridsPerDay)
     {
      DashboardUpdate();
      return;
     }

   OpenPrimaryGrid(signal);
   DashboardUpdate();
  }
//+------------------------------------------------------------------+
