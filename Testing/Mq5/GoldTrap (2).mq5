//+------------------------------------------------------------------+
//|                         GoldTrap.mq5                            |
//|              Price Trap Scalper | XAUUSD | M1                  |
//|         200pt Zone | Aggressive Trail | Dynamic Lots           |
//|                          v1.00                                  |
//+------------------------------------------------------------------+
//
//  LOGIC:
//    1. Set trap: BUY STOP above + SELL STOP below current price
//    2. Price breaks out of zone → winning order fills
//    3. Losing order cancelled immediately
//    4. Trail activates at InpStartTrailing points profit
//    5. Trail follows price, never moves backward
//    6. Exit when price reverses by InpTrailingDistance
//    7. Reset trap at new center price
//
//+------------------------------------------------------------------+
#property copyright "GoldTrap"
#property version   "1.00"
#property strict

#include <Trade\Trade.mqh>
#include <Trade\OrderInfo.mqh>
#include <Trade\PositionInfo.mqh>

CTrade        trade;
COrderInfo    orderInfo;
CPositionInfo posInfo;

//+------------------------------------------------------------------+
//| INPUTS                                                           |
//+------------------------------------------------------------------+

input group          "=== TRAP SETTINGS ==="
input int     TrapZonePoints    = 200;   // Total zone size in points
input int     HardSL_Points     = 200;   // Hard SL points from entry

input group          "=== TRAILING STOP (Aggressive) ==="
input int     InpStartTrailing  = 220;   // Points profit before trail activates
input int     InpTrailingDist   = 180;   // Trail distance in points
input int     InpTrailingStep   = 20;    // Trail moves in steps of X points

input group          "=== LOT SIZING ==="
input double  RiskPercent       = 1.0;   // Risk % of equity per trade
input double  MinLot            = 0.01;  // Minimum lot
input double  MaxLot            = 0.10;  // Maximum lot

input group          "=== BASKET ==="
input double  BasketSL_USD      = -10.0; // Close all at -$10

input group          "=== SAFETY ==="
input double  MinEquity         = 30.0;  // Hard equity floor $
input double  MarginPause       = 150.0; // Pause below this margin %
input int     MaxSpreadPts      = 80;    // Skip if spread too wide
input int     CoolDownMins      = 5;     // Cool down after SL hit
input bool    UseSession        = true;  // Session filter
input int     SessionStart      = 3;     // UTC start
input int     SessionEnd        = 20;    // UTC end

input group          "=== IDENTITY ==="
input int     MagicNumber       = 77777;
input string  EA_Comment        = "GoldTrap_v1";

//+------------------------------------------------------------------+
//| GLOBALS                                                          |
//+------------------------------------------------------------------+

double   g_pointVal      = 0;
double   g_pipSize       = 0;

// Trap state
double   g_trapCenter    = 0;     // Center price of current trap
double   g_buyStopPrice  = 0;     // Top of trap
double   g_sellStopPrice = 0;     // Bottom of trap
bool     g_trapArmed     = false; // Pending orders placed
bool     g_tradeOpen     = false; // Position is live

// Trade tracking
ulong    g_ticket        = 0;     // Active position ticket
double   g_entryPrice    = 0;     // Entry price of active trade
bool     g_isBuy         = false; // Direction of active trade

// Trailing state
bool     g_trailActive   = false; // Trail armed
double   g_trailFloor    = 0;     // Current trail floor price
double   g_peakPrice     = 0;     // Best price seen since entry

// Timing
datetime g_lastCandle    = 0;
datetime g_coolUntil     = 0;
bool     g_paused        = false;

// Stats
int      g_wins          = 0;
int      g_losses        = 0;
int      g_trailHits     = 0;

//+------------------------------------------------------------------+
//| OnInit                                                           |
//+------------------------------------------------------------------+
int OnInit()
  {
   string sym = Symbol();
   Print("GoldTrap v1.00 | ", sym,
         " | Zone:", TrapZonePoints, "pts",
         " | Trail activates:", InpStartTrailing, "pts",
         " | Trail dist:", InpTrailingDist, "pts");

   g_pointVal = SymbolInfoDouble(sym, SYMBOL_POINT);
   g_pipSize  = 10.0 * g_pointVal;

   trade.SetExpertMagicNumber(MagicNumber);
   trade.SetDeviationInPoints(30);
   trade.SetTypeFilling(GetFillMode());

   Print("✅ GoldTrap ready | Risk:", RiskPercent,
         "% | Min lot:", MinLot, " | Max lot:", MaxLot);
   return INIT_SUCCEEDED;
  }

//+------------------------------------------------------------------+
//| OnDeinit                                                         |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
  {
   Print("GoldTrap stopped | Wins:", g_wins,
         " | Losses:", g_losses,
         " | Trail hits:", g_trailHits);
  }

//+------------------------------------------------------------------+
//| Fill mode detection                                              |
//+------------------------------------------------------------------+
ENUM_ORDER_TYPE_FILLING GetFillMode()
  {
   long m = SymbolInfoInteger(Symbol(), SYMBOL_FILLING_MODE);
   if((m & SYMBOL_FILLING_FOK) != 0) return ORDER_FILLING_FOK;
   if((m & SYMBOL_FILLING_IOC) != 0) return ORDER_FILLING_IOC;
   return ORDER_FILLING_RETURN;
  }

//+------------------------------------------------------------------+
//| DYNAMIC LOT CALCULATION                                          |
//+------------------------------------------------------------------+
double CalcLot()
  {
   double equity   = AccountInfoDouble(ACCOUNT_EQUITY);
   double riskAmt  = equity * RiskPercent / 100.0;
   double slPoints = HardSL_Points;
   double tickVal  = SymbolInfoDouble(Symbol(), SYMBOL_TRADE_TICK_VALUE);
   double tickSize = SymbolInfoDouble(Symbol(), SYMBOL_TRADE_TICK_SIZE);
   double ptVal    = tickVal * (g_pointVal / tickSize);

   if(ptVal <= 0) return MinLot;

   double lot  = riskAmt / (slPoints * ptVal);
   double step = SymbolInfoDouble(Symbol(), SYMBOL_VOLUME_STEP);
   lot = MathFloor(lot / step) * step;
   return NormalizeDouble(MathMax(MinLot, MathMin(MaxLot, lot)), 2);
  }

//+------------------------------------------------------------------+
//| SET THE TRAP                                                     |
//| Places BUY STOP above + SELL STOP below current price           |
//+------------------------------------------------------------------+
void SetTrap()
  {
   double ask     = SymbolInfoDouble(Symbol(), SYMBOL_ASK);
   double bid     = SymbolInfoDouble(Symbol(), SYMBOL_BID);
   double mid     = NormalizeDouble((ask + bid) / 2.0, _Digits);
   double half    = (TrapZonePoints / 2) * g_pointVal;
   double slDist  = HardSL_Points * g_pointVal;
   double minDist = (double)SymbolInfoInteger(Symbol(),
                    SYMBOL_TRADE_STOPS_LEVEL) * g_pointVal;
   double lot     = CalcLot();

   g_trapCenter    = mid;
   g_buyStopPrice  = NormalizeDouble(mid + half, _Digits);
   g_sellStopPrice = NormalizeDouble(mid - half, _Digits);

   // Validate minimum distance
   if(g_buyStopPrice - ask < minDist ||
      bid - g_sellStopPrice < minDist)
     {
      Print("⚠️ Trap too close to price — waiting");
      return;
     }

   // Cancel any old pending orders first
   CancelAllPendingOrders();

   // BUY STOP — fires if price breaks UP
   bool buyOk = trade.OrderOpen(
                   Symbol(),
                   ORDER_TYPE_BUY_STOP,
                   lot,
                   0,
                   g_buyStopPrice,
                   NormalizeDouble(g_buyStopPrice - slDist, _Digits),
                   0,   // no fixed TP — trail manages exit
                   ORDER_TIME_GTC, 0, EA_Comment);

   // SELL STOP — fires if price breaks DOWN
   bool sellOk = trade.OrderOpen(
                    Symbol(),
                    ORDER_TYPE_SELL_STOP,
                    lot,
                    0,
                    g_sellStopPrice,
                    NormalizeDouble(g_sellStopPrice + slDist, _Digits),
                    0,  // no fixed TP — trail manages exit
                    ORDER_TIME_GTC, 0, EA_Comment);

   if(buyOk && sellOk)
     {
      g_trapArmed  = true;
      g_tradeOpen  = false;
      g_trailActive= false;
      Print("🪤 TRAP SET | Center:", DoubleToString(mid, 2),
            " | BUY STOP:", DoubleToString(g_buyStopPrice, 2),
            " | SELL STOP:", DoubleToString(g_sellStopPrice, 2),
            " | Lot:", lot);
     }
   else
      Print("❌ Trap placement failed | ",
            trade.ResultRetcodeDescription());
  }

//+------------------------------------------------------------------+
//| CANCEL ALL EA PENDING ORDERS                                     |
//+------------------------------------------------------------------+
void CancelAllPendingOrders()
  {
   for(int i = OrdersTotal() - 1; i >= 0; i--)
      if(orderInfo.SelectByIndex(i) && orderInfo.Magic() == MagicNumber)
         trade.OrderDelete(orderInfo.Ticket());
  }

//+------------------------------------------------------------------+
//| CLOSE ALL EA POSITIONS                                           |
//+------------------------------------------------------------------+
void CloseAllPositions()
  {
   for(int i = PositionsTotal() - 1; i >= 0; i--)
      if(posInfo.SelectByIndex(i) && posInfo.Magic() == MagicNumber)
         trade.PositionClose(posInfo.Ticket());
  }

//+------------------------------------------------------------------+
//| COUNT EA POSITIONS                                               |
//+------------------------------------------------------------------+
int CountPositions()
  {
   int n = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
      if(posInfo.SelectByIndex(i) && posInfo.Magic() == MagicNumber) n++;
   return n;
  }

int CountPendingOrders()
  {
   int n = 0;
   for(int i = OrdersTotal() - 1; i >= 0; i--)
      if(orderInfo.SelectByIndex(i) && orderInfo.Magic() == MagicNumber) n++;
   return n;
  }

//+------------------------------------------------------------------+
//| DETECT TRAP TRIGGER                                              |
//| Check if one of the stops filled — cancel the other             |
//+------------------------------------------------------------------+
void CheckTrapTrigger()
  {
   if(!g_trapArmed || g_tradeOpen) return;
   if(CountPositions() == 0) return;

   // A position opened — trap triggered
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      if(posInfo.SelectByIndex(i) && posInfo.Magic() == MagicNumber)
        {
         g_ticket     = posInfo.Ticket();
         g_entryPrice = posInfo.PriceOpen();
         g_isBuy      = (posInfo.PositionType() == POSITION_TYPE_BUY);
         g_tradeOpen  = true;
         g_trailActive= false;
         g_peakPrice  = g_entryPrice;

         // Cancel the unfilled opposite order
         CancelAllPendingOrders();

         Print("🚀 TRAP TRIGGERED | ",
               g_isBuy ? "BUY" : "SELL",
               " @ ", DoubleToString(g_entryPrice, 2),
               " | Ticket:", g_ticket);
         break;
        }
     }
  }

//+------------------------------------------------------------------+
//| TRAILING STOP MANAGEMENT                                         |
//| Called every tick when trade is open                            |
//+------------------------------------------------------------------+
void ManageTrail()
  {
   if(!g_tradeOpen) return;

   // Confirm position still open
   if(!posInfo.SelectByTicket(g_ticket))
     {
      // Position closed externally (SL hit)
      Print("📋 Position closed | SL hit");
      g_tradeOpen  = false;
      g_trapArmed  = false;
      g_trailActive= false;
      g_losses++;
      g_coolUntil  = TimeCurrent() + CoolDownMins * 60;
      g_paused     = true;
      return;
     }

   double bid      = SymbolInfoDouble(Symbol(), SYMBOL_BID);
   double ask      = SymbolInfoDouble(Symbol(), SYMBOL_ASK);
   double curPrice = g_isBuy ? bid : ask;
   double profitPts= g_isBuy
                     ? (curPrice - g_entryPrice) / g_pointVal
                     : (g_entryPrice - curPrice) / g_pointVal;

   // Update peak price
   if(g_isBuy && curPrice > g_peakPrice) g_peakPrice = curPrice;
   if(!g_isBuy && curPrice < g_peakPrice) g_peakPrice = curPrice;

   // Activate trail when profit reaches InpStartTrailing
   if(!g_trailActive && profitPts >= InpStartTrailing)
     {
      g_trailActive = true;
      g_trailFloor  = g_isBuy
                      ? g_peakPrice - InpTrailingDist * g_pointVal
                      : g_peakPrice + InpTrailingDist * g_pointVal;
      Print("🔒 TRAIL ARMED | Profit:", DoubleToString(profitPts,1),
            "pts | Floor:", DoubleToString(g_trailFloor,2));
     }

   if(!g_trailActive) return;

   // Update trail floor as price moves in our favor
   if(g_isBuy)
     {
      double newFloor = g_peakPrice - InpTrailingDist * g_pointVal;
      // Only move floor up, never down, in steps
      if(newFloor > g_trailFloor + InpTrailingStep * g_pointVal)
        {
         g_trailFloor = NormalizeDouble(newFloor, _Digits);
         Print("📈 Trail floor: ", DoubleToString(g_trailFloor,2));
        }

      // Check if price dropped below floor
      if(bid <= g_trailFloor)
        {
         Print("🎯 TRAIL HIT | Exit:", DoubleToString(bid,2),
               " | Floor:", DoubleToString(g_trailFloor,2),
               " | Profit:", DoubleToString(profitPts,1), "pts");
         trade.PositionClose(g_ticket);
         g_tradeOpen   = false;
         g_trapArmed   = false;
         g_trailActive = false;
         g_trailHits++;
         g_wins++;
        }
     }
   else // SELL
     {
      double newFloor = g_peakPrice + InpTrailingDist * g_pointVal;
      // Only move floor down, never up, in steps
      if(newFloor < g_trailFloor - InpTrailingStep * g_pointVal)
        {
         g_trailFloor = NormalizeDouble(newFloor, _Digits);
         Print("📉 Trail floor: ", DoubleToString(g_trailFloor,2));
        }

      // Check if price rose above floor
      if(ask >= g_trailFloor)
        {
         Print("🎯 TRAIL HIT | Exit:", DoubleToString(ask,2),
               " | Floor:", DoubleToString(g_trailFloor,2),
               " | Profit:", DoubleToString(profitPts,1), "pts");
         trade.PositionClose(g_ticket);
         g_tradeOpen   = false;
         g_trapArmed   = false;
         g_trailActive = false;
         g_trailHits++;
         g_wins++;
        }
     }
  }

//+------------------------------------------------------------------+
//| BASKET MONITOR                                                   |
//+------------------------------------------------------------------+
double GetBasketProfit()
  {
   double total = 0;
   for(int i = PositionsTotal()-1; i >= 0; i--)
      if(posInfo.SelectByIndex(i) && posInfo.Magic() == MagicNumber)
         total += posInfo.Profit() + posInfo.Swap() + posInfo.Commission();
   return total;
  }

bool CheckBasket()
  {
   if(CountPositions() == 0) return false;
   double profit = GetBasketProfit();

   if(profit <= BasketSL_USD)
     {
      Print("🛑 BASKET SL $", DoubleToString(profit,2));
      CloseAllPositions();
      CancelAllPendingOrders();
      g_tradeOpen   = false;
      g_trapArmed   = false;
      g_trailActive = false;
      g_losses++;
      g_paused    = true;
      g_coolUntil = TimeCurrent() + CoolDownMins * 60;
      return true;
     }
   return false;
  }

//+------------------------------------------------------------------+
//| SAFETY CHECK                                                     |
//+------------------------------------------------------------------+
bool SafetyCheck()
  {
   // Cool down
   if(TimeCurrent() < g_coolUntil) return false;
   if(g_paused && TimeCurrent() >= g_coolUntil)
     {
      Print("▶️ Cool down over — resuming");
      g_paused = false;
     }

   // Equity floor
   if(AccountInfoDouble(ACCOUNT_EQUITY) < MinEquity)
     {
      Print("🚨 EQUITY FLOOR");
      CloseAllPositions();
      CancelAllPendingOrders();
      g_paused    = true;
      g_coolUntil = TimeCurrent() + 999999;
      return false;
     }

   // Margin level
   double ml = AccountInfoDouble(ACCOUNT_MARGIN_LEVEL);
   if(ml > 0 && ml < MarginPause)
     {
      CancelAllPendingOrders();
      return false;
     }

   // Spread
   if((int)SymbolInfoInteger(Symbol(), SYMBOL_SPREAD) > MaxSpreadPts)
      return false;

   // Session
   if(UseSession)
     {
      MqlDateTime dt;
      TimeToStruct(TimeCurrent(), dt);
      if(dt.hour < SessionStart || dt.hour >= SessionEnd)
         return false;
     }

   return true;
  }

//+------------------------------------------------------------------+
//| OnTick — Main loop                                               |
//+------------------------------------------------------------------+
void OnTick()
  {
   // ── 1. SAFETY ────────────────────────────────────────────────
   if(!SafetyCheck()) return;
   if(g_paused) return;

   // ── 2. BASKET MONITOR (every tick) ──────────────────────────
   if(CheckBasket()) return;

   // ── 3. CHECK IF TRAP TRIGGERED (every tick) ─────────────────
   if(g_trapArmed && !g_tradeOpen)
      CheckTrapTrigger();

   // ── 4. MANAGE TRAILING STOP (every tick) ────────────────────
   if(g_tradeOpen)
      ManageTrail();

   // ── 5. PER CANDLE — reset trap if no trade open ─────────────
   datetime candle = iTime(Symbol(), PERIOD_M1, 0);
   if(candle != g_lastCandle)
     {
      g_lastCandle = candle;

      // No trade and no pending orders → set new trap
      if(!g_tradeOpen && CountPendingOrders() == 0)
        {
         Print("🔄 New candle — resetting trap");
         SetTrap();
        }

      // Trap is armed but price moved far from center
      // Recenter the trap
      if(g_trapArmed && !g_tradeOpen)
        {
         double mid = (SymbolInfoDouble(Symbol(), SYMBOL_ASK) +
                       SymbolInfoDouble(Symbol(), SYMBOL_BID)) / 2.0;
         double drift = MathAbs(mid - g_trapCenter);
         double halfZone = (TrapZonePoints / 2) * g_pointVal;

         if(drift > halfZone * 1.5)
           {
            Print("↺ Trap drifted — recentering");
            CancelAllPendingOrders();
            SetTrap();
           }
        }
     }
  }

//+------------------------------------------------------------------+
//| END GoldTrap v1.00                                               |
//+------------------------------------------------------------------+
