#ifndef HIDDENTRAIL_MT4_MQH
#define HIDDENTRAIL_MT4_MQH

//+------------------------------------------------------------------+
//|                                          HiddenTrail_MT4.mqh    |
//|                Optimized Hidden Trailing Stop (MQL4 Version)     |
//|                             Do NOT compile in MetaTrader 5       |
//+------------------------------------------------------------------+
#property strict
#property version   "2.00"
#property description "Pure MQL4 Code. Do not compile in MT5."

//+------------------------------------------------------------------+
//| Enums                                                            |
//+------------------------------------------------------------------+
enum ENUM_BASKET_MODE {
   SINGLE_TRAIL_PROFIT,     // Individual Order Trailing
   BASKET_SYMBOL,           // Current Symbol Basket
   BASKET_ACCOUNT_EQUITY    // Whole Account Basket
};

//+------------------------------------------------------------------+
//| Class: CHiddenTrailManager                                       |
//+------------------------------------------------------------------+
class CHiddenTrailManager {
private:
   //--- Settings ---
   int               m_magic;
   int               m_slippagePoints;
   int               m_atrPeriod;
   double            m_atrMultiplier;
   int               m_trailStartPips;
   int               m_timeframe;     // PERIOD_M1, PERIOD_H1 etc.
   double            m_accTrailStart;
   double            m_accTrailDrop;
   ENUM_BASKET_MODE  m_mode;

   //--- State ---
   int               m_prevTrend;
   double            m_accountPeak;
   int               m_lastOrdersTotal;
   string            m_prefix;

   //--- Helpers ---
   double GetPipValue() {
      double point = MarketInfo(Symbol(), MODE_POINT);
      int digits = (int)MarketInfo(Symbol(), MODE_DIGITS);
      return (digits == 3 || digits == 5) ? point * 10 : point;
   }

   bool IsOrderManaged(int index) {
      // OrderSelect must be called by the caller before this function
      if (OrderMagicNumber() == m_magic) return(true);
      if (m_magic == -1) return(true);               // All trades
      if (m_magic == 0 && OrderMagicNumber() == 0) return(true); // Manual trades
      return(false);
   }

   void CalculateSupertrend(double &atr, double &hl2, int &trend, double &band) {
      atr = iATR(NULL, m_timeframe, m_atrPeriod, 0);
      hl2 = (iHigh(NULL, m_timeframe, 0) + iLow(NULL, m_timeframe, 0)) / 2.0;

      double atr1   = iATR(NULL, m_timeframe, m_atrPeriod, 1);
      double close1 = iClose(NULL, m_timeframe, 1);
      double close0 = iClose(NULL, m_timeframe, 0);
      double hl2_1  = (iHigh(NULL, m_timeframe, 1) + iLow(NULL, m_timeframe, 1)) / 2.0;

      double upper0 = hl2  + m_atrMultiplier * atr;
      double lower0 = hl2  - m_atrMultiplier * atr;
      double upper1 = hl2_1 + m_atrMultiplier * atr1;
      double lower1 = hl2_1 - m_atrMultiplier * atr1;

      trend = m_prevTrend;
      if (close1 <= upper1 && close0 > upper0) trend = 1;
      else if (close1 >= lower1 && close0 < lower0) trend = -1;
      else if (trend == 0) trend = (close0 >= hl2) ? 1 : -1;

      m_prevTrend = trend;
      band = (trend == 1) ? lower0 : upper0;
   }

   void UpdateObject(string name, string type, double price, string text, color clr) {
      datetime time_anchor = iTime(NULL, 0, 10) + PeriodSeconds() * 20;
      datetime time_current = iTime(NULL, 0, 0) + PeriodSeconds() * 10;

      if (ObjectFind(name) < 0) {  // Create new
         bool created = false;
         if (type == "HLINE") {
            created = ObjectCreate(name, OBJ_HLINE, 0, 0, price);
         } else if (type == "TEXT") {
            created = ObjectCreate(name, OBJ_TEXT, 0, time_anchor, price);
         }

         if (created) {
            ObjectSet(name, OBJPROP_COLOR, clr);
            ObjectSet(name, OBJPROP_STYLE, STYLE_DASH);
            ObjectSet(name, OBJPROP_WIDTH, (type == "HLINE") ? 2 : 1);

            if (type == "TEXT") {
               ObjectSetText(name, text, 10, "Arial", clr);
            }
         } else {
            Print("ObjectCreate failed: ", name, " Error=", GetLastError());
         }
      } else {  // Update existing
         if (type == "HLINE") {
            ObjectMove(name, 0, 0, price);
         } else if (type == "TEXT") {
            ObjectMove(name, 0, time_current, price);
            if (ObjectGetString(0, name, OBJPROP_TEXT) != text) {
               ObjectSetText(name, text, 10, "Arial", clr);
            }
         }
      }
   }

   void ManageAccountEquity() {
      double totalProfit = 0;
      int count = 0;

      for(int i = OrdersTotal() - 1; i >= 0; i--) {
         if(OrderSelect(i, SELECT_BY_POS, MODE_TRADES)) {
            bool magicMatch = (m_magic == -1) || (m_magic == 0 && OrderMagicNumber() == 0) || (OrderMagicNumber() == m_magic);
            if(magicMatch) {
               totalProfit += OrderProfit() + OrderSwap();
               count++;
            }
         }
      }

      if(count == 0) {
         ObjectDelete(0, m_prefix + "ACC_TXT");
         m_accountPeak = 0;
         return;
      }

      string txt = m_prefix + "ACC_TXT";
      // Explicitly convert doubles to strings to avoid warnings
      string statusText = "ACCOUNT BASKET | Profit: $" + DoubleToString(totalProfit, 2) + " | Peak: $" + DoubleToString(m_accountPeak, 2);
      UpdateObject(txt, "TEXT", 0, statusText, clrGold);

      ObjectSetInteger(0, txt, OBJPROP_CORNER, CORNER_LEFT_UPPER);
      ObjectSetInteger(0, txt, OBJPROP_XDISTANCE, 10);
      ObjectSetInteger(0, txt, OBJPROP_YDISTANCE, 20);

      if(totalProfit > m_accTrailStart) {
         if(totalProfit > m_accountPeak) m_accountPeak = totalProfit;
         if(totalProfit <= (m_accountPeak - m_accTrailDrop)) {
            Print("ACCOUNT BASKET HIT. Closing All.");
            CloseManagedOrders();
         }
      }
   }

   void ManageSymbolBasket(double stBand, int stTrend) {
      double totalLots = 0;
      double weightedSumPrices = 0;

      for(int i = OrdersTotal() - 1; i >= 0; i--) {
         if(OrderSelect(i, SELECT_BY_POS, MODE_TRADES)) {
            if(OrderSymbol() == Symbol() && IsOrderManaged(i)) {
               double lots = OrderLots();
               int type = OrderType();
               double signedLots = (type == OP_BUY) ? lots : -lots;
               totalLots += signedLots;
               weightedSumPrices += OrderOpenPrice() * signedLots;
            }
         }
      }

      if(totalLots == 0) {
         ObjectDelete(0, m_prefix + "BASKET_LINE");
         ObjectDelete(0, m_prefix + "BASKET_TXT");
         return;
      }

      double avgPrice = weightedSumPrices / totalLots;
      int netType = (totalLots > 0) ? OP_BUY : OP_SELL;
      double currentPrice = (netType == OP_BUY) ? MarketInfo(Symbol(), MODE_BID) : MarketInfo(Symbol(), MODE_ASK);
      double profitPips = (netType == OP_BUY) ? (currentPrice - avgPrice) : (avgPrice - currentPrice);
      profitPips /= GetPipValue();

      if(profitPips >= m_trailStartPips) {
         UpdateObject(m_prefix + "BASKET_LINE", "HLINE", stBand, "", clrGold);
         // Explicitly convert double to string
         UpdateObject(m_prefix + "BASKET_TXT", "TEXT", stBand, " BASKET TRAIL (" + DoubleToString(totalLots, 2) + ")", clrGold);

         bool hit = (netType == OP_BUY && currentPrice <= stBand) || (netType == OP_SELL && currentPrice >= stBand);
         if(hit) {
            Print("BASKET SYMBOL HIT. Closing All.");
            CloseManagedOrders();
         }
      } else {
         ObjectDelete(0, m_prefix + "BASKET_LINE");
         ObjectDelete(0, m_prefix + "BASKET_TXT");
      }
   }

   void ManageSingleTrails(double stBand, int stTrend) {
      int currTotal = OrdersTotal();
      if(currTotal != m_lastOrdersTotal) {
         ObjectsDeleteAll(0, m_prefix);  // prefix cleanup (Corrected args)
         m_lastOrdersTotal = currTotal;
      }

      double adjPoint = GetPipValue();

      for(int i = OrdersTotal() - 1; i >= 0; i--) {
         if(OrderSelect(i, SELECT_BY_POS, MODE_TRADES)) {
            if(OrderSymbol() != Symbol()) continue;
            if(!IsOrderManaged(i)) continue;

            int type = OrderType();
            int ticket = OrderTicket();
            double openPrice = OrderOpenPrice();
            double currentPrice = (type == OP_BUY) ? MarketInfo(Symbol(), MODE_BID) : MarketInfo(Symbol(), MODE_ASK);

            double profitPips = (type == OP_BUY) ? (currentPrice - openPrice) : (openPrice - currentPrice);
            profitPips /= adjPoint;

            if(profitPips >= m_trailStartPips) {
               // Explicitly convert ticket (int) to string
               UpdateObject(m_prefix + "LINE_" + IntegerToString(ticket), "HLINE", stBand, "", clrRed);
               UpdateObject(m_prefix + "TXT_" + IntegerToString(ticket), "TEXT", stBand, " #" + IntegerToString(ticket), clrRed);

               bool hit = (type == OP_BUY && currentPrice <= stBand) || (type == OP_SELL && currentPrice >= stBand);
               if(hit) {
                  Print("HIDDEN TRAIL HIT: Ticket ", ticket);
                  bool closed = OrderClose(ticket, OrderLots(), currentPrice, m_slippagePoints, clrRed);
                  if(!closed) {
                     Print("OrderClose failed, ticket=", ticket, ", Error=", GetLastError());
                  }
                  ObjectDelete(0, m_prefix + "LINE_" + IntegerToString(ticket));
                  ObjectDelete(0, m_prefix + "TXT_" + IntegerToString(ticket));
               }
            } else {
               ObjectDelete(0, m_prefix + "LINE_" + IntegerToString(ticket));
               ObjectDelete(0, m_prefix + "TXT_" + IntegerToString(ticket));
            }
         }
      }
   }

   void CloseManagedOrders() {
      for(int i = OrdersTotal() - 1; i >= 0; i--) {
         if(OrderSelect(i, SELECT_BY_POS, MODE_TRADES)) {
            bool magicMatch = (m_magic == -1) || (m_magic == 0 && OrderMagicNumber() == 0) || (OrderMagicNumber() == m_magic);
            bool symbolMatch = (m_mode == BASKET_ACCOUNT_EQUITY) || (OrderSymbol() == Symbol());

            if(magicMatch && symbolMatch) {
               double clsPrice = (OrderType() == OP_BUY) ? MarketInfo(OrderSymbol(), MODE_BID) : MarketInfo(OrderSymbol(), MODE_ASK);
               bool closed = OrderClose(OrderTicket(), OrderLots(), clsPrice, m_slippagePoints, clrRed);
               if(!closed) {
                  Print("Close failed, ticket=", OrderTicket(), ", Error=", GetLastError());
               }
            }
         }
      }
      ObjectsDeleteAll(0, m_prefix); // Corrected args
   }

public:
   CHiddenTrailManager(string pref = "VSL_") {
      m_prefix = pref;
      Reset();
   }

   void Reset() {
      m_prevTrend = 0;
      m_accountPeak = 0;
      m_lastOrdersTotal = 0;
      ObjectsDeleteAll(0, m_prefix); // Corrected args
   }

   void SetSettings(int magic, int slippage, int atrPeriod, double atrMult, int trailStart,
                    int tf, double accStart, double accDrop, ENUM_BASKET_MODE mode) {
      if(m_atrPeriod != atrPeriod || m_atrMultiplier != atrMult || m_timeframe != tf || m_mode != mode) {
         m_prevTrend = 0;
         m_accountPeak = 0;
      }

      m_magic = magic;
      m_slippagePoints = slippage;
      m_atrPeriod = atrPeriod;
      m_atrMultiplier = atrMult;
      m_trailStartPips = trailStart;
      m_timeframe = tf;
      m_accTrailStart = accStart;
      m_accTrailDrop = accDrop;
      m_mode = mode;
   }

   void Update() {
      double atr, hl2, band;
      int trend;
      CalculateSupertrend(atr, hl2, trend, band);

      switch(m_mode) {
         case BASKET_ACCOUNT_EQUITY:
            ManageAccountEquity();
            break;
         case BASKET_SYMBOL:
            ManageSymbolBasket(band, trend);
            break;
         case SINGLE_TRAIL_PROFIT:
         default:
            ManageSingleTrails(band, trend);
            break;
      }
   }

   void Deinit() {
      ObjectsDeleteAll(0, m_prefix); // Corrected args
   }
};
//+------------------------------------------------------------------+
#endif // HIDDENTRAIL_MT4_MQH
//+------------------------------------------------------------------+