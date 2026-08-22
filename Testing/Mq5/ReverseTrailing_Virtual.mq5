//+------------------------------------------------------------------+
//|                                   ReverseTrailing_Virtual.mq5     |
//|   Fully virtual SAR management: no broker pending orders.        |
//|   EA watches price itself and confirms breaches before acting,   |
//|   filtering out micro-whipsaw wicks. One wide real stop remains  |
//|   as a disconnect/crash safety net.                              |
//+------------------------------------------------------------------+
#property copyright "Senior MQL5 Developer"
#property link      ""
#property version   "4.00"
#property strict

#include <Trade\Trade.mqh>
#include <Trade\SymbolInfo.mqh>

CTrade        m_trade;
CSymbolInfo   m_symbol;

enum ENUM_MM_TYPE
  {
   MM_FIXED_LOT   = 0, // Fixed lot
   MM_PERCENT_DEP = 1  // % of free margin
  };

input group "=== Trading Settings ==="
input ENUM_MM_TYPE InpMMType         = MM_FIXED_LOT;  // Money management type
input double       InpLotSize        = 0.1;           // Fixed lot
input double       InpRiskPercent    = 1.0;            // % of free margin
input int          InpDistancePips   = 100;            // Virtual trail distance (points)
input ulong        InpMagicNumber    = 123456;         // Magic number
input int          InpDeviationPoints = 20;             // Allowed slippage for market orders (points)

input group "=== Virtual Whipsaw Filter ==="
input int   InpDebounceSeconds        = 5;    // Price must stay beyond the virtual level this long before EA acts
input int   InpExtraPenetrationPoints = 0;    // Extra distance beyond the level required to even start the debounce timer (0 = off)

input group "=== Safety Net (recommended: keep ON) ==="
input bool  InpUseCatastrophicStop      = true;  // Attach a real broker-side disaster stop
input int   InpCatastrophicDistancePips = 350;   // Distance of the disaster stop from entry (points) - should be well beyond the virtual trail

//--- Globals
double m_adjusted_distance = 0;

double   g_virtual_sl     = 0;
datetime g_breach_start   = 0;
ulong    g_state_ticket   = 0;
string   g_gv_prefix;

//+------------------------------------------------------------------+
int OnInit()
  {
   if(!m_symbol.Name(_Symbol))
     {
      Print("Symbol init error.");
      return(INIT_FAILED);
     }

   m_trade.SetExpertMagicNumber(InpMagicNumber);
   m_trade.SetDeviationInPoints(InpDeviationPoints);
   m_trade.SetTypeFillingBySymbol(_Symbol);

   m_adjusted_distance = InpDistancePips * _Point;
   g_gv_prefix = "RTV_" + _Symbol + "_" + IntegerToString(InpMagicNumber) + "_";

   LoadVirtualState();

   if(!InpUseCatastrophicStop)
      Print("WARNING: Catastrophic backstop stop is DISABLED. A terminal crash/disconnect while a position is open will leave it completely unprotected.");

   return(INIT_SUCCEEDED);
  }

//+------------------------------------------------------------------+
void OnDeinit(const int reason)
  {
  }

//+------------------------------------------------------------------+
//| Persistence: survives EA reload / terminal restart                |
//+------------------------------------------------------------------+
void LoadVirtualState()
  {
   g_state_ticket = GlobalVariableCheck(g_gv_prefix + "ticket") ? (ulong)GlobalVariableGet(g_gv_prefix + "ticket") : 0;
   g_virtual_sl   = GlobalVariableCheck(g_gv_prefix + "vsl")    ? GlobalVariableGet(g_gv_prefix + "vsl")          : 0;
   g_breach_start = GlobalVariableCheck(g_gv_prefix + "breach") ? (datetime)GlobalVariableGet(g_gv_prefix + "breach") : 0;
  }

void SaveVirtualState()
  {
   GlobalVariableSet(g_gv_prefix + "ticket", (double)g_state_ticket);
   GlobalVariableSet(g_gv_prefix + "vsl",    g_virtual_sl);
   GlobalVariableSet(g_gv_prefix + "breach", (double)g_breach_start);
  }

void ResetVirtualState(ulong new_ticket)
  {
   g_state_ticket = new_ticket;
   g_virtual_sl   = 0;
   g_breach_start = 0;
   SaveVirtualState();
  }

//+------------------------------------------------------------------+
double CatastrophicDistance()
  {
   return InpCatastrophicDistancePips * _Point;
  }

//+------------------------------------------------------------------+
//| Open a fresh position (cold start, or after an external close    |
//| such as the catastrophic stop firing or a manual intervention).  |
//+------------------------------------------------------------------+
void OpenInitialPosition()
  {
   double lot = CalculateLot();
   double sl  = 0;

   if(InpUseCatastrophicStop)
      sl = NormalizePrice(m_symbol.Ask() - CatastrophicDistance());

   if(m_trade.Buy(lot, _Symbol, m_symbol.Ask(), sl, 0, "Initial Buy (virtual)"))
     {
      ulong ticket = m_trade.ResultOrder(); // deal/position ticket from the result
      // ResultOrder() returns the order ticket; the position ticket equals it for a simple market fill.
      ResetVirtualState(ticket);
      Print("Initial BUY opened. Virtual management active.");
     }
   else
      Print("Initial entry failed: ", m_trade.ResultRetcodeDescription());
  }

//+------------------------------------------------------------------+
//| Confirmed breach: close current position, flip to the opposite   |
//| side at market, re-attach a fresh catastrophic stop.             |
//+------------------------------------------------------------------+
void ExecuteVirtualReversal(ulong ticket, ENUM_POSITION_TYPE pos_type)
  {
   if(!m_trade.PositionClose(ticket))
     {
      Print("Virtual reversal: close failed, will retry next tick: ", m_trade.ResultRetcodeDescription());
      return; // has_position will still read true next tick, logic simply retries
     }

   double lot = CalculateLot();
   bool ok;

   if(pos_type == POSITION_TYPE_BUY)
     {
      double sl = InpUseCatastrophicStop ? NormalizePrice(m_symbol.Bid() + CatastrophicDistance()) : 0;
      ok = m_trade.Sell(lot, _Symbol, m_symbol.Bid(), sl, 0, "Virtual reverse -> Sell");
     }
   else
     {
      double sl = InpUseCatastrophicStop ? NormalizePrice(m_symbol.Ask() - CatastrophicDistance()) : 0;
      ok = m_trade.Buy(lot, _Symbol, m_symbol.Ask(), sl, 0, "Virtual reverse -> Buy");
     }

   if(ok)
     {
      ulong new_ticket = m_trade.ResultOrder();
      ResetVirtualState(new_ticket);
      Print("Virtual reversal executed.");
     }
   else
     {
      Print("Virtual reversal: re-entry failed: ", m_trade.ResultRetcodeDescription());
      ResetVirtualState(0); // we are flat and unmanaged; next tick's cold-start logic will re-enter
     }
  }

//+------------------------------------------------------------------+
//| Core virtual trailing + debounce-confirmed breach detection      |
//+------------------------------------------------------------------+
void ManageVirtualPosition(ulong ticket, ENUM_POSITION_TYPE pos_type)
  {
   double current_ask = m_symbol.Ask();
   double current_bid = m_symbol.Bid();
   double penetration = InpExtraPenetrationPoints * _Point;

   bool breached;

   if(pos_type == POSITION_TYPE_BUY)
     {
      double target = NormalizePrice(current_bid - m_adjusted_distance);
      if(g_virtual_sl == 0 || target > g_virtual_sl)
        {
         g_virtual_sl = target;
        }
      breached = (current_bid <= g_virtual_sl - penetration);
     }
   else
     {
      double target = NormalizePrice(current_ask + m_adjusted_distance);
      if(g_virtual_sl == 0 || target < g_virtual_sl)
        {
         g_virtual_sl = target;
        }
      breached = (current_ask >= g_virtual_sl + penetration);
     }

   if(breached)
     {
      if(g_breach_start == 0)
        {
         g_breach_start = TimeCurrent();
         SaveVirtualState();
         return; // just started watching, don't act yet
        }

      if(TimeCurrent() - g_breach_start >= InpDebounceSeconds)
        {
         ExecuteVirtualReversal(ticket, pos_type);
         return;
        }

      // still inside the debounce window: keep watching, take no action
      return;
     }
   else
     {
      if(g_breach_start != 0)
        {
         g_breach_start = 0; // price recovered before confirmation -> whipsaw filtered out
        }
      SaveVirtualState();
     }
  }

//+------------------------------------------------------------------+
void OnTick()
  {
   if(!m_symbol.RefreshRates()) return;

   bool has_position = false;
   ENUM_POSITION_TYPE pos_type = POSITION_TYPE_BUY;
   ulong pos_ticket = 0;

   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      if(PositionGetSymbol(i) == _Symbol && PositionGetInteger(POSITION_MAGIC) == InpMagicNumber)
        {
         has_position = true;
         pos_type   = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
         pos_ticket = PositionGetInteger(POSITION_TICKET);
         break;
        }
     }

   if(!has_position)
     {
      // Either a true cold start, or the position vanished outside of our own
      // virtual reversal (catastrophic stop fired, or manual close). Either way,
      // there is nothing left to manage virtually - start fresh.
      if(g_state_ticket != 0)
        {
         Print("Position closed outside virtual management (catastrophic stop or manual close). Resetting.");
         ResetVirtualState(0);
        }
      OpenInitialPosition();
      return;
     }

   // Position exists but doesn't match what we're tracking (e.g. EA just restarted
   // and this is an older position, or ticket rolled over from a fill) - resync.
   if(pos_ticket != g_state_ticket)
      ResetVirtualState(pos_ticket);

   ManageVirtualPosition(pos_ticket, pos_type);
  }

//+------------------------------------------------------------------+
double CalculateLot()
  {
   if(InpMMType == MM_FIXED_LOT)
     {
      return InpLotSize;
     }
   else
     {
      double free_margin = AccountInfoDouble(ACCOUNT_MARGIN_FREE);
      double margin_for_lot = 0;

      if(!OrderCalcMargin(ORDER_TYPE_BUY, _Symbol, 1.0, m_symbol.Ask(), margin_for_lot) || margin_for_lot <= 0)
        {
         return InpLotSize;
        }

      double calculated_lot = (free_margin * (InpRiskPercent / 100.0)) / margin_for_lot;

      double lot_step = m_symbol.LotsStep();
      calculated_lot = MathFloor(calculated_lot / lot_step) * lot_step;

      if(calculated_lot < m_symbol.LotsMin()) calculated_lot = m_symbol.LotsMin();
      if(calculated_lot > m_symbol.LotsMax()) calculated_lot = m_symbol.LotsMax();

      return calculated_lot;
     }
  }

//+------------------------------------------------------------------+
double NormalizePrice(double price)
  {
   double tick_size = m_symbol.TickSize();
   if(tick_size == 0) return price;
   return MathRound(price / tick_size) * tick_size;
  }
