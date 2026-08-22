//+------------------------------------------------------------------+
//|                                              ReverseTrailEA.mq5  |
//|                          Reverse Trail Expert Advisor for MT5    |
//|                                                                  |
//|  Strategy:                                                       |
//|   - Maintain ONE active market position and ONE opposite pending |
//|     stop order at all times.                                     |
//|   - The pending stop acts as: stop-loss, trailing stop, and      |
//|     stop-and-reverse entry.                                      |
//|   - As price moves in favor of the active trade, the opposite    |
//|     pending stop is trailed closer (never further away).         |
//|   - If the pending stop is triggered, the prior position is      |
//|     closed and the new (reversed) position is managed the same   |
//|     way with a fresh opposite pending stop.                      |
//+------------------------------------------------------------------+
#property copyright "Reverse Trail EA"
#property version   "1.00"
#property strict

#include <Trade\Trade.mqh>
#include <Trade\PositionInfo.mqh>
#include <Trade\OrderInfo.mqh>
#include <Trade\SymbolInfo.mqh>

//--- Inputs
input group               "=== General ==="
input double   InpLotSize              = 0.10;     // Lot Size
input long     InpMagic                = 20260617; // Magic Number
input ulong    InpSlippagePoints       = 20;       // Slippage (points)

input group               "=== Entry ==="
input bool     InpAutoEntry            = false;    // Automatic entry on start if no position
input bool     InpAutoEntryBuy         = true;     // Auto entry direction: true=BUY, false=SELL
input bool     InpDetectManualTrades   = true;     // Detect manually opened trades on this symbol

input group               "=== Pending / Trailing ==="
input long     InpPendingDistancePts   = 200;      // Pending Distance (points)
input long     InpTrailActivationPts   = 100;      // Trailing Activation Distance (points)
input long     InpTrailStepPts         = 10;       // Trailing Step (points)

input group               "=== Filters ==="
input long     InpMaxSpreadPts         = 50;       // Max Spread Filter (points, 0 = disabled)
input bool     InpUseHoursFilter       = false;    // Use Trading Hours Filter
input int      InpStartHour            = 0;        // Start Hour (server time, 0-23)
input int      InpEndHour              = 24;       // End Hour (server time, 1-24)

input group               "=== Dashboard ==="
input bool     InpShowDashboard        = true;     // Show on-chart dashboard
input color    InpDashColor            = clrWhite; // Dashboard text color

//--- Globals
CTrade          Trade;
CPositionInfo   PosInfo;
COrderInfo      OrderInfo;
CSymbolInfo     Sym;

int             g_reversals = 0;
ulong           g_lastPendingTicket = 0;
ulong           g_lastPositionTicket = 0;
string          g_panelName = "RevTrailEA_Panel";

//+------------------------------------------------------------------+
int OnInit()
{
   Trade.SetExpertMagicNumber(InpMagic);
   Trade.SetDeviationInPoints(InpSlippagePoints);
   Trade.SetTypeFillingBySymbol(_Symbol);
   Trade.SetAsyncMode(false);

   if(!Sym.Name(_Symbol))
   {
      Print("Failed to init symbol info");
      return INIT_FAILED;
   }

   // Recover state after restart: track existing position/pending if any
   RecoverState();

   // Auto entry if requested and nothing exists
   if(InpAutoEntry && !HasOurPosition() && !HasOurPending() && IsTradingAllowed())
   {
      OpenInitialTrade(InpAutoEntryBuy);
   }

   if(InpShowDashboard)
      EventSetTimer(1);

   return INIT_SUCCEEDED;
}

//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   EventKillTimer();
   ObjectDelete(0, g_panelName);
   Comment("");
}

//+------------------------------------------------------------------+
void OnTimer()
{
   UpdateDashboard();
}

//+------------------------------------------------------------------+
void OnTick()
{
   if(!Sym.RefreshRates()) return;

   // Detect a manually opened trade if enabled
   if(InpDetectManualTrades)
      AdoptManualPosition();

   // Detect stop-and-reverse: pending got triggered -> we may now have a new opposite position
   HandleReversal();

   bool hasPos = HasOurPosition();
   bool hasPend = HasOurPending();

   if(hasPos && !hasPend && IsTradingAllowed())
   {
      // Position exists but no pending => place initial opposite pending
      PlaceOppositePending();
   }
   else if(hasPos && hasPend)
   {
      // Trail the opposite pending toward price
      TrailPending();
   }
   else if(!hasPos && hasPend)
   {
      // Orphan pending (position closed manually). Remove it.
      DeleteOurPending("Orphan pending without active position");
   }

   UpdateDashboard();
}

//+------------------------------------------------------------------+
//| Helpers: filters                                                 |
//+------------------------------------------------------------------+
bool IsTradingAllowed()
{
   if(!TerminalInfoInteger(TERMINAL_TRADE_ALLOWED)) return false;
   if(!MQLInfoInteger(MQL_TRADE_ALLOWED)) return false;

   if(InpMaxSpreadPts > 0)
   {
      long spread = (long)SymbolInfoInteger(_Symbol, SYMBOL_SPREAD);
      if(spread > InpMaxSpreadPts) return false;
   }

   if(InpUseHoursFilter)
   {
      MqlDateTime tm;
      TimeToStruct(TimeCurrent(), tm);
      if(InpStartHour <= InpEndHour)
      {
         if(tm.hour < InpStartHour || tm.hour >= InpEndHour) return false;
      }
      else // wraps midnight
      {
         if(tm.hour < InpStartHour && tm.hour >= InpEndHour) return false;
      }
   }
   return true;
}

//+------------------------------------------------------------------+
//| Helpers: existence checks                                        |
//+------------------------------------------------------------------+
bool HasOurPosition()
{
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      if(!PosInfo.SelectByIndex(i)) continue;
      if(PosInfo.Symbol() != _Symbol) continue;
      if(PosInfo.Magic()  != InpMagic) continue;
      g_lastPositionTicket = PosInfo.Ticket();
      return true;
   }
   return false;
}

bool HasOurPending()
{
   for(int i = OrdersTotal() - 1; i >= 0; i--)
   {
      if(!OrderInfo.SelectByIndex(i)) continue;
      if(OrderInfo.Symbol() != _Symbol) continue;
      if(OrderInfo.Magic()  != InpMagic) continue;
      ENUM_ORDER_TYPE t = OrderInfo.OrderType();
      if(t == ORDER_TYPE_BUY_STOP || t == ORDER_TYPE_SELL_STOP)
      {
         g_lastPendingTicket = OrderInfo.Ticket();
         return true;
      }
   }
   return false;
}

bool GetOurPosition(ENUM_POSITION_TYPE &type, double &priceOpen, double &volume, ulong &ticket)
{
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      if(!PosInfo.SelectByIndex(i)) continue;
      if(PosInfo.Symbol() != _Symbol) continue;
      if(PosInfo.Magic()  != InpMagic) continue;
      type      = (ENUM_POSITION_TYPE)PosInfo.PositionType();
      priceOpen = PosInfo.PriceOpen();
      volume    = PosInfo.Volume();
      ticket    = PosInfo.Ticket();
      return true;
   }
   return false;
}

bool GetOurPending(ENUM_ORDER_TYPE &type, double &price, ulong &ticket)
{
   for(int i = OrdersTotal() - 1; i >= 0; i--)
   {
      if(!OrderInfo.SelectByIndex(i)) continue;
      if(OrderInfo.Symbol() != _Symbol) continue;
      if(OrderInfo.Magic()  != InpMagic) continue;
      ENUM_ORDER_TYPE t = OrderInfo.OrderType();
      if(t == ORDER_TYPE_BUY_STOP || t == ORDER_TYPE_SELL_STOP)
      {
         type   = t;
         price  = OrderInfo.PriceOpen();
         ticket = OrderInfo.Ticket();
         return true;
      }
   }
   return false;
}

//+------------------------------------------------------------------+
//| Recovery / adoption                                              |
//+------------------------------------------------------------------+
void RecoverState()
{
   // Just refresh tracked tickets if any belong to us
   HasOurPosition();
   HasOurPending();

   // Clean duplicates: keep one position (impossible with hedging off + same magic) and one pending
   EnsureSinglePending();
   PrintFormat("RevTrailEA: recovered. PosTicket=%I64u PendTicket=%I64u",
               g_lastPositionTicket, g_lastPendingTicket);
}

void AdoptManualPosition()
{
   // If a position exists on this symbol without our magic and we don't have ours, adopt nothing
   // (we cannot change magic). Instead, only act when our magic owns a position.
   // This function is a placeholder for future adoption logic / logging.
}

void EnsureSinglePending()
{
   int count = 0;
   ulong keep = 0;
   for(int i = OrdersTotal() - 1; i >= 0; i--)
   {
      if(!OrderInfo.SelectByIndex(i)) continue;
      if(OrderInfo.Symbol() != _Symbol) continue;
      if(OrderInfo.Magic()  != InpMagic) continue;
      ENUM_ORDER_TYPE t = OrderInfo.OrderType();
      if(t != ORDER_TYPE_BUY_STOP && t != ORDER_TYPE_SELL_STOP) continue;
      count++;
      if(keep == 0) keep = OrderInfo.Ticket();
      else
      {
         Trade.OrderDelete(OrderInfo.Ticket());
         PrintFormat("RevTrailEA: deleted duplicate pending #%I64u", OrderInfo.Ticket());
      }
   }
}

//+------------------------------------------------------------------+
//| Entries                                                          |
//+------------------------------------------------------------------+
void OpenInitialTrade(bool buy)
{
   double price = buy ? Sym.Ask() : Sym.Bid();
   bool ok = buy ? Trade.Buy(InpLotSize, _Symbol, price, 0, 0, "RevTrailEA init BUY")
                 : Trade.Sell(InpLotSize, _Symbol, price, 0, 0, "RevTrailEA init SELL");
   if(ok)
      PrintFormat("RevTrailEA: opened initial %s @ %.5f", buy ? "BUY" : "SELL", price);
   else
      PrintFormat("RevTrailEA: initial trade failed retcode=%u", Trade.ResultRetcode());
}

//+------------------------------------------------------------------+
//| Pending placement                                                |
//+------------------------------------------------------------------+
void PlaceOppositePending()
{
   ENUM_POSITION_TYPE ptype;
   double openPrice, vol;
   ulong  pt;
   if(!GetOurPosition(ptype, openPrice, vol, pt)) return;

   double point = Sym.Point();
   long   stopsLvl = SymbolInfoInteger(_Symbol, SYMBOL_TRADE_STOPS_LEVEL);
   double minDist = (double)MathMax(InpPendingDistancePts, stopsLvl) * point;

   double price;
   ENUM_ORDER_TYPE otype;

   if(ptype == POSITION_TYPE_BUY)
   {
      // place SELL STOP below bid
      otype = ORDER_TYPE_SELL_STOP;
      price = Sym.Bid() - minDist;
   }
   else
   {
      otype = ORDER_TYPE_BUY_STOP;
      price = Sym.Ask() + minDist;
   }

   price = NormalizeDouble(price, Sym.Digits());

   bool ok = (otype == ORDER_TYPE_SELL_STOP)
             ? Trade.SellStop(vol, price, _Symbol, 0, 0, ORDER_TIME_GTC, 0, "RevTrailEA pending")
             : Trade.BuyStop (vol, price, _Symbol, 0, 0, ORDER_TIME_GTC, 0, "RevTrailEA pending");

   if(ok)
      PrintFormat("RevTrailEA: placed %s @ %.5f (vol %.2f)",
                  EnumToString(otype), price, vol);
   else
      PrintFormat("RevTrailEA: place pending failed retcode=%u", Trade.ResultRetcode());
}

//+------------------------------------------------------------------+
//| Trailing logic                                                   |
//+------------------------------------------------------------------+
void TrailPending()
{
   ENUM_POSITION_TYPE ptype;
   double openPrice, vol;
   ulong  posTicket;
   if(!GetOurPosition(ptype, openPrice, vol, posTicket)) return;

   ENUM_ORDER_TYPE otype;
   double curPendPrice;
   ulong  pendTicket;
   if(!GetOurPending(otype, curPendPrice, pendTicket)) return;

   double point = Sym.Point();
   long   stopsLvl = SymbolInfoInteger(_Symbol, SYMBOL_TRADE_STOPS_LEVEL);
   double pendDist = (double)MathMax(InpPendingDistancePts, stopsLvl) * point;
   double activate = (double)InpTrailActivationPts * point;
   double step     = (double)InpTrailStepPts * point;

   double newPrice = 0.0;
   bool   shouldModify = false;

   if(ptype == POSITION_TYPE_BUY && otype == ORDER_TYPE_SELL_STOP)
   {
      double bid = Sym.Bid();
      double profit = bid - openPrice;
      if(profit < activate) return;

      double target = bid - pendDist;
      target = NormalizeDouble(target, Sym.Digits());

      // Only move UP (closer to price, locking more profit). Never down.
      if(target - curPendPrice >= step)
      {
         newPrice = target;
         shouldModify = true;
      }
   }
   else if(ptype == POSITION_TYPE_SELL && otype == ORDER_TYPE_BUY_STOP)
   {
      double ask = Sym.Ask();
      double profit = openPrice - ask;
      if(profit < activate) return;

      double target = ask + pendDist;
      target = NormalizeDouble(target, Sym.Digits());

      // Only move DOWN (closer to price). Never up.
      if(curPendPrice - target >= step)
      {
         newPrice = target;
         shouldModify = true;
      }
   }
   else
   {
      // Pending side doesn't match position direction -> fix it
      DeleteOurPending("Pending side mismatch");
      return;
   }

   if(shouldModify)
   {
      if(Trade.OrderModify(pendTicket, newPrice, 0, 0, ORDER_TIME_GTC, 0))
         PrintFormat("RevTrailEA: trailed pending #%I64u -> %.5f", pendTicket, newPrice);
      else
         PrintFormat("RevTrailEA: trail modify failed retcode=%u", Trade.ResultRetcode());
   }
}

//+------------------------------------------------------------------+
//| Stop & Reverse handling                                          |
//+------------------------------------------------------------------+
void HandleReversal()
{
   // If we previously had both a position and a pending, and now the pending
   // is gone while a position exists in the OPPOSITE direction of the prior
   // position -> the pending was triggered (reversal).
   //
   // The triggered stop opens a new position; the prior position may still
   // be open (netting can occur on a netting account where it auto-closes).
   // On a hedging account we must explicitly close any remaining opposite
   // position from our magic.

   // Collect our positions
   int countBuy = 0, countSell = 0;
   ulong tBuy = 0, tSell = 0;
   double volBuy = 0, volSell = 0;

   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      if(!PosInfo.SelectByIndex(i)) continue;
      if(PosInfo.Symbol() != _Symbol) continue;
      if(PosInfo.Magic()  != InpMagic) continue;
      if(PosInfo.PositionType() == POSITION_TYPE_BUY)
      { countBuy++; tBuy = PosInfo.Ticket(); volBuy = PosInfo.Volume(); }
      else
      { countSell++; tSell = PosInfo.Ticket(); volSell = PosInfo.Volume(); }
   }

   // If both directions exist (hedging account post-trigger), close the older one
   if(countBuy > 0 && countSell > 0)
   {
      // Determine which is newer: the one matching the absent pending's direction
      // Simpler: close the one whose ticket equals g_lastPositionTicket (the prior).
      ulong toClose = (g_lastPositionTicket == tBuy) ? tBuy :
                      (g_lastPositionTicket == tSell) ? tSell : tBuy;
      if(Trade.PositionClose(toClose))
      {
         g_reversals++;
         PrintFormat("RevTrailEA: REVERSAL — closed prior position #%I64u (total reversals: %d)",
                     toClose, g_reversals);
      }
      else
      {
         PrintFormat("RevTrailEA: failed to close prior position #%I64u retcode=%u",
                     toClose, Trade.ResultRetcode());
      }
   }
   else
   {
      // Detect reversal via change of position ticket while pending disappeared
      ulong curTicket = 0;
      ENUM_POSITION_TYPE pt; double po, pv;
      if(GetOurPosition(pt, po, pv, curTicket))
      {
         if(g_lastPositionTicket != 0 && curTicket != g_lastPositionTicket && !HasOurPending())
         {
            g_reversals++;
            PrintFormat("RevTrailEA: REVERSAL detected (netting). New position #%I64u. Total reversals: %d",
                        curTicket, g_reversals);
         }
         g_lastPositionTicket = curTicket;
      }
   }
}

//+------------------------------------------------------------------+
void DeleteOurPending(const string reason)
{
   for(int i = OrdersTotal() - 1; i >= 0; i--)
   {
      if(!OrderInfo.SelectByIndex(i)) continue;
      if(OrderInfo.Symbol() != _Symbol) continue;
      if(OrderInfo.Magic()  != InpMagic) continue;
      ENUM_ORDER_TYPE t = OrderInfo.OrderType();
      if(t == ORDER_TYPE_BUY_STOP || t == ORDER_TYPE_SELL_STOP)
      {
         if(Trade.OrderDelete(OrderInfo.Ticket()))
            PrintFormat("RevTrailEA: deleted pending #%I64u (%s)", OrderInfo.Ticket(), reason);
      }
   }
}

//+------------------------------------------------------------------+
//| Dashboard                                                        |
//+------------------------------------------------------------------+
void UpdateDashboard()
{
   if(!InpShowDashboard) { Comment(""); return; }

   string dir = "FLAT";
   double posOpen = 0, posVol = 0, posProfit = 0;
   ulong  posT = 0;
   ENUM_POSITION_TYPE pt;
   bool hasPos = GetOurPosition(pt, posOpen, posVol, posT);
   if(hasPos)
   {
      dir = (pt == POSITION_TYPE_BUY) ? "BUY" : "SELL";
      if(PosInfo.SelectByTicket(posT))
         posProfit = PosInfo.Profit() + PosInfo.Swap() + PosInfo.Commission();
   }

   ENUM_ORDER_TYPE ot;
   double pendPrice = 0;
   ulong  pendT = 0;
   bool hasPend = GetOurPending(ot, pendPrice, pendT);

   double pendDistPts = 0;
   if(hasPend)
   {
      double ref = (ot == ORDER_TYPE_SELL_STOP) ? Sym.Bid() : Sym.Ask();
      pendDistPts = MathAbs(ref - pendPrice) / Sym.Point();
   }

   string s = StringFormat(
      "── Reverse Trail EA ──\n"
      "Symbol:           %s\n"
      "Direction:        %s\n"
      "Position Vol:     %.2f\n"
      "Position Open:    %.*f\n"
      "Floating P/L:     %.2f %s\n"
      "Pending:          %s @ %.*f\n"
      "Pending Dist:     %.0f pts\n"
      "Reversals:        %d\n"
      "Spread:           %d pts\n"
      "Trading Allowed:  %s\n",
      _Symbol,
      dir,
      posVol,
      Sym.Digits(), posOpen,
      posProfit, AccountInfoString(ACCOUNT_CURRENCY),
      hasPend ? EnumToString(ot) : "—",
      Sym.Digits(), hasPend ? pendPrice : 0.0,
      pendDistPts,
      g_reversals,
      (int)SymbolInfoInteger(_Symbol, SYMBOL_SPREAD),
      IsTradingAllowed() ? "YES" : "NO"
   );

   Comment(s);
}
//+------------------------------------------------------------------+
