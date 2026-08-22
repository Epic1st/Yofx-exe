//+------------------------------------------------------------------+
//|                                                   PM_Scalper.mq5  |
//|                                                     Prof. Morris  |
//+------------------------------------------------------------------+
#property copyright "2026, Prof. Morris"
#property link      "https://t.me/profitabletrader2362"
#property version   "2.00"
#property strict

#include <Trade\Trade.mqh>
#include <Trade\PositionInfo.mqh>
#include <Trade\SymbolInfo.mqh>

//--- INPUT PARAMETERS
input string   Section1 = "==== TRADE SETTINGS ====";
input double   BaseLotSize         = 0.01;      // Base Lot Size
input double   StopLoss_ATR_Mult   = 2.0;       // Stop Loss Distance (ATR Mult)
input double   TakeProfit_ATR_Mult = 3.0;       // Take Profit Distance (ATR Mult)
input int      ATR_Period          = 14;        // ATR Period
input ulong    Slippage            = 3;         // Max Slippage (Points)
input string   TradeComment        = "PM_Scalper"; // Trade Comment

input string   Section1b = "==== MARTINGALE SETTINGS ====";
input bool     UseMartingale       = true;      // Enable Martingale (x2 after loss)
input double   MaxLotSize          = 1.0;       // Maximum Lot Size (Safety)
input int      MaxMartingaleLevel  = 3;         // Max Consecutive Martingale Levels

input string   Section2 = "==== TRAILING SETTINGS (Asempa) ====";
input bool     UseATRTrailing      = true;      // Enable ATR Trailing Stop
input double   TrailATR_Mult       = 0.5;       // Trail: Move SL after 0.5 ATR move
input double   TrailStart_ATR_Mult = 0.8;       // Trail: Start trailing after 0.8 ATR profit

input string   Section3 = "==== TELEGRAM SETTINGS ====";
input string   TelegramToken       = "";        // Your Bot Token
input string   TelegramChatID      = "";        // Your Chat ID

input string   Section4 = "==== SMART ENTRY FILTER ====";
input bool     UseMomentumFilter   = true;      // Wait for Pressure to Cool Down?
input int      ConfirmCandlePips   = 5;         // Min Reversal Strength (Points)

input string   Section5 = "==== SIGNAL SETTINGS ====";
input int      InpRSI_Period       = 7;         // RSI Period
input int      InpRSI_OverSold     = 35;        // RSI Oversold Level
input int      InpRSI_OverBought   = 65;        // RSI Overbought Level
input int      InpFastMACD         = 12;        // MACD Fast
input int      InpSlowMACD         = 26;        // MACD Slow
input int      InpSignalMACD       = 9;         // MACD Signal

input string   Section6 = "==== FILTERS ====";
input bool     UseTrendFilter      = true;      // Use VWAP Trend Filter

input string   Section7 = "==== TIME SETTINGS ====";
input int      StartHour           = 0;         // Start Trading Hour
input int      EndHour             = 23;        // End Trading Hour

input string   Section8 = "==== SESSION FILTER ====";
input bool     UseSessionFilter    = false;     // Enable Session Filter
input bool     TradeLondonSession  = true;      // Trade London Session (08:00-17:00 GMT)
input bool     TradeNYSession      = true;      // Trade NY Session (13:00-22:00 GMT)
input int      LondonStartGMT      = 8;         // London Session Start (GMT)
input int      LondonEndGMT        = 17;        // London Session End (GMT)
input int      NYStartGMT          = 13;        // NY Session Start (GMT)
input int      NYEndGMT            = 22;        // NY Session End (GMT)

//--- GLOBAL OBJECTS & VARIABLES
CTrade         trade;
CPositionInfo  positionInfo;
CSymbolInfo    symbolInfo;

int            MagicNumber = 0;
datetime       LastSignalBarTime = 0; 

// Indicator Handles
int            handle_rsi;
int            handle_atr;
int            handle_macd;

// State variables
int            PendingSignalType = 0; // 0=None, 1=Buy, 2=Sell
double         PendingTargetPrice = 0.0;
double         PendingATR = 0.0;

// Telegram Tracking
int            TrailingActiveTicket = 0;
double         LastAlertedSL = 0;

// Martingale Variables
double         CurrentLotSize = 0.01;
int            MartingaleLevel = 0;
bool           LastTradeWasWin = true;

//+------------------------------------------------------------------+
//| Expert initialization function                                     |
//+------------------------------------------------------------------+
int OnInit()
{
   // Initialize Symbol Info
   if(!symbolInfo.Name(Symbol())) return(INIT_FAILED);
   
   // Set Magic Number (Stable Hidden Logic)
   int symHash = 0;
   string sym = Symbol();
   for(int i=0; i<StringLen(sym); i++) 
   {
      symHash += StringGetCharacter(sym, i); 
   }
   
   int tfHash = Period() * 100; 
   MathSrand(symHash + tfHash); 
   MagicNumber = MathRand() + 10000; 
   
   trade.SetExpertMagicNumber(MagicNumber);
   trade.SetDeviationInPoints(Slippage);
   trade.SetTypeFilling(ORDER_FILLING_IOC); 

   // Create Indicators
   handle_rsi = iRSI(Symbol(), Period(), InpRSI_Period, PRICE_CLOSE);
   handle_atr = iATR(Symbol(), Period(), ATR_Period);
   handle_macd = iMACD(Symbol(), Period(), InpFastMACD, InpSlowMACD, InpSignalMACD, PRICE_CLOSE);
   
   if(handle_rsi == INVALID_HANDLE || handle_atr == INVALID_HANDLE || handle_macd == INVALID_HANDLE)
   {
      Print("Error creating indicators");
      return(INIT_FAILED);
   }

   Print("PM_Scalper v2.0 MT5 Initialized with Martingale. Magic: ", MagicNumber);
   
   TrailingActiveTicket = 0;
   LastAlertedSL = 0;
   
   // Initialize Martingale
   CurrentLotSize = BaseLotSize;
   MartingaleLevel = 0;
   LastTradeWasWin = true;
   
   // Load last trade result from GlobalVariable if exists
   if(GlobalVariableCheck("PM_LastLot_" + Symbol()))
   {
      CurrentLotSize = GlobalVariableGet("PM_LastLot_" + Symbol());
      MartingaleLevel = (int)GlobalVariableGet("PM_MartLevel_" + Symbol());
      Print("Loaded Martingale state: Lot=", CurrentLotSize, " Level=", MartingaleLevel);
   }
   
   return(INIT_SUCCEEDED);
}

//+------------------------------------------------------------------+
//| Expert deinitialization function                                   |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   // Save Martingale state
   GlobalVariableSet("PM_LastLot_" + Symbol(), CurrentLotSize);
   GlobalVariableSet("PM_MartLevel_" + Symbol(), MartingaleLevel);
   
   IndicatorRelease(handle_rsi);
   IndicatorRelease(handle_atr);
   IndicatorRelease(handle_macd);
   Comment("");
}

//+------------------------------------------------------------------+
//| Expert tick function                                               |
//+------------------------------------------------------------------+
void OnTick()
{
   // Update Symbol Info
   if(!symbolInfo.RefreshRates()) return;
   
   //--- 0. TIME FILTER
   MqlDateTime dt;
   TimeCurrent(dt);
   int currentHour = dt.hour;
   
   bool isTimeToTrade = false;
   if(StartHour < EndHour) {
      if(currentHour >= StartHour && currentHour < EndHour) isTimeToTrade = true;
   } else {
      if(currentHour >= StartHour || currentHour < EndHour) isTimeToTrade = true;
   }
   if(!isTimeToTrade) return;
   
   //--- 0b. SESSION FILTER
   if(UseSessionFilter)
   {
      if(!IsInTradingSession())
      {
         return;
      }
   }

   //--- 1. CHECK CLOSED TRADES FOR TELEGRAM & MARTINGALE
   CheckClosedTradesForTelegram();

   //--- 2. MANAGE OPEN POSITIONS (ASEMPA TRAILING)
   if(UseATRTrailing) ManageTrailing();

   //--- 3. CHECK EXISTING POSITIONS
   if(positionInfo.Select(Symbol())) return; 

   //--- 4. DETECT NEW SIGNALS
   datetime timeArray[1];
   CopyTime(Symbol(), Period(), 0, 1, timeArray);
   datetime currentBarTime = timeArray[0];
   
   if(currentBarTime != LastSignalBarTime)
   {
      LastSignalBarTime = currentBarTime;
      
      // Get Data
      double rsi_buf[2], atr_buf[1];
      double close_buf[2], open_buf[2];
      long volume_buf[2]; 
      
      // Copy Indicators & Price
      if(CopyBuffer(handle_rsi, 0, 1, 2, rsi_buf) < 0 || 
         CopyBuffer(handle_atr, 0, 1, 1, atr_buf) < 0 ||
         CopyClose(Symbol(), Period(), 1, 2, close_buf) < 0 ||
         CopyOpen(Symbol(), Period(), 1, 2, open_buf) < 0 ||
         CopyTickVolume(Symbol(), Period(), 1, 2, volume_buf) < 0) return;
      
      double rsi_bar1 = rsi_buf[0]; 
      double rsi_bar2 = rsi_buf[1];
      
      double atr_val = atr_buf[0]; 
      double close_val = close_buf[0]; 
      double open_val = open_buf[0]; 
      double delta = (close_val - open_val) * (double)volume_buf[0]; 
      
      // VWAP Calculation
      double vwap_val = CalculateVWAP();
      
      bool trend_up = close_val > vwap_val;
      bool trend_dn = close_val < vwap_val;
      
      //--- BUY SIGNAL
      bool is_oversold = (rsi_bar1 < InpRSI_OverSold);
      bool is_recovering = (rsi_bar2 < InpRSI_OverSold && rsi_bar1 > rsi_bar2);
      
      if(trend_up && (is_oversold || is_recovering) && delta > 0)
      {
         PendingSignalType = 1; 
         PendingTargetPrice = close_val - (atr_val * StopLoss_ATR_Mult);
         PendingATR = atr_val;
         Print(">>> NEW BUY SIGNAL. Target: ", PendingTargetPrice, " Lot: ", CurrentLotSize);
      }

      //--- SELL SIGNAL
      bool is_overbought = (rsi_bar1 > InpRSI_OverBought);
      bool is_falling = (rsi_bar2 > InpRSI_OverBought && rsi_bar1 < rsi_bar2);

      if(trend_dn && (is_overbought || is_falling) && delta < 0)
      {
         PendingSignalType = 2;
         PendingTargetPrice = close_val + (atr_val * StopLoss_ATR_Mult);
         PendingATR = atr_val;
         Print(">>> NEW SELL SIGNAL. Target: ", PendingTargetPrice, " Lot: ", CurrentLotSize);
      }
   }

   //--- 5. EXECUTE LOGIC
   if(PendingSignalType > 0)
   {
      CheckAndEnter(PendingSignalType, PendingTargetPrice, PendingATR);
   }
   
   UpdateDashboard();
}

//+------------------------------------------------------------------+
//| Check if current time is within trading session                   |
//+------------------------------------------------------------------+
bool IsInTradingSession()
{
   datetime gmtTime = TimeGMT();
   MqlDateTime dt;
   TimeToStruct(gmtTime, dt);
   int currentHourGMT = dt.hour;
   
   bool inLondon = false;
   bool inNY = false;
   
   // Check London session
   if(TradeLondonSession)
   {
      if(LondonStartGMT < LondonEndGMT)
      {
         if(currentHourGMT >= LondonStartGMT && currentHourGMT < LondonEndGMT)
            inLondon = true;
      }
      else // Wraps around midnight
      {
         if(currentHourGMT >= LondonStartGMT || currentHourGMT < LondonEndGMT)
            inLondon = true;
      }
   }
   
   // Check NY session
   if(TradeNYSession)
   {
      if(NYStartGMT < NYEndGMT)
      {
         if(currentHourGMT >= NYStartGMT && currentHourGMT < NYEndGMT)
            inNY = true;
      }
      else // Wraps around midnight
      {
         if(currentHourGMT >= NYStartGMT || currentHourGMT < NYEndGMT)
            inNY = true;
      }
   }
   
   // Return true if in either session
   return (inLondon || inNY);
}

//+------------------------------------------------------------------+
//| Update Martingale after trade closes                              |
//+------------------------------------------------------------------+
void UpdateMartingaleAfterTrade(bool wasWin)
{
   if(!UseMartingale)
   {
      CurrentLotSize = BaseLotSize;
      MartingaleLevel = 0;
      return;
   }
   
   if(wasWin)
   {
      // Reset to base lot after a win
      CurrentLotSize = BaseLotSize;
      MartingaleLevel = 0;
      LastTradeWasWin = true;
      Print("Martingale: WIN - Reset to base lot ", BaseLotSize);
   }
   else
   {
      // Double the lot after a loss
      if(MartingaleLevel < MaxMartingaleLevel)
      {
         CurrentLotSize = CurrentLotSize * 2.0;
         if(CurrentLotSize > MaxLotSize)
         {
            CurrentLotSize = MaxLotSize;
            Print("Martingale: Hit max lot size ", MaxLotSize);
         }
         MartingaleLevel++;
         LastTradeWasWin = false;
         Print("Martingale: LOSS - Doubling lot to ", CurrentLotSize, " Level: ", MartingaleLevel);
      }
      else
      {
         // Reset if max level reached
         CurrentLotSize = BaseLotSize;
         MartingaleLevel = 0;
         Print("Martingale: Max level reached, resetting to base lot");
      }
   }
   
   // Save state
   GlobalVariableSet("PM_LastLot_" + Symbol(), CurrentLotSize);
   GlobalVariableSet("PM_MartLevel_" + Symbol(), MartingaleLevel);
}

//+------------------------------------------------------------------+
//| Check Entry Conditions                                            |
//+------------------------------------------------------------------+
void CheckAndEnter(int type, double target_price, double atr)
{
   double Ask = symbolInfo.Ask();
   double Bid = symbolInfo.Bid();
   bool price_reached = false;
   double buffer = atr * 0.1; 
   
   if(type == 1 && Ask <= (target_price + buffer)) price_reached = true; 
   if(type == 2 && Bid >= (target_price - buffer)) price_reached = true; 
   
   if(!price_reached) return; 
   
   bool pressure_cooled = false;
   
   if(UseMomentumFilter)
   {
      // Get current bar data (Bar 0)
      double close0[], open0[], rsi_buf[2];
      
      if(CopyClose(Symbol(), Period(), 0, 1, close0) < 0 || 
         CopyOpen(Symbol(), Period(), 0, 1, open0) < 0 ||
         CopyBuffer(handle_rsi, 0, 0, 2, rsi_buf) < 0) return; 
      
      if(type == 1) // BUY
      {
         bool is_bullish = (close0[0] > open0[0]);
         bool rsi_turning = (rsi_buf[0] > rsi_buf[1]);
         double low0[];
         CopyLow(Symbol(), Period(), 0, 1, low0);
         
         bool moving_up = (Ask > low0[0] + (ConfirmCandlePips * _Point));
         
         if(is_bullish || (rsi_turning && moving_up)) pressure_cooled = true;
      }
      else if(type == 2) // SELL
      {
         bool is_bearish = (close0[0] < open0[0]);
         bool rsi_turning = (rsi_buf[0] < rsi_buf[1]);
         double high0[];
         CopyHigh(Symbol(), Period(), 0, 1, high0);
         bool moving_dn = (Bid < high0[0] - (ConfirmCandlePips * _Point));
         
         if(is_bearish || (rsi_turning && moving_dn)) pressure_cooled = true;
      }
   }
   else { pressure_cooled = true; }

   if(price_reached && pressure_cooled)
   {
      if(type == 1) OpenTrade(ORDER_TYPE_BUY, Ask, Ask - (atr * StopLoss_ATR_Mult), Ask + (atr * TakeProfit_ATR_Mult));
      if(type == 2) OpenTrade(ORDER_TYPE_SELL, Bid, Bid + (atr * StopLoss_ATR_Mult), Bid - (atr * TakeProfit_ATR_Mult));
   }
}

//+------------------------------------------------------------------+
//| Helper: Manage Trailing Stop                                     |
//+------------------------------------------------------------------+
void ManageTrailing()
{
   if(!positionInfo.Select(Symbol())) return;
   
   double atr_buf[1];
   if(CopyBuffer(handle_atr, 0, 0, 1, atr_buf) < 0) return; 
   double currentATR = atr_buf[0];
   
   double trailStep = currentATR * TrailATR_Mult;
   double startTrailProfit = currentATR * TrailStart_ATR_Mult;
   
   ulong ticket = positionInfo.Ticket();
   double openPrice = positionInfo.PriceOpen();
   double currentSL = positionInfo.StopLoss();
   
   double profitDist = 0;
   if(positionInfo.PositionType() == POSITION_TYPE_BUY)
   {
      profitDist = symbolInfo.Bid() - openPrice;
      if(profitDist >= startTrailProfit)
      {
         int steps = (int)((profitDist - startTrailProfit) / trailStep);
         double newSL = openPrice + (trailStep * (1 + steps));
         
         if(newSL > currentSL + _Point)
         {
            if(trade.PositionModify(ticket, newSL, positionInfo.TakeProfit()))
            {
               Print("Trail BUY updated. New SL: ", newSL);
               if(TrailingActiveTicket != (int)ticket) SendTelegramAlert("TRAILING STARTED", (int)ticket, newSL);
               else SendTelegramAlert("TRAILING UPDATE", (int)ticket, newSL);
               TrailingActiveTicket = (int)ticket;
               LastAlertedSL = newSL;
            }
         }
      }
   }
   else if(positionInfo.PositionType() == POSITION_TYPE_SELL)
   {
      profitDist = openPrice - symbolInfo.Ask();
      if(profitDist >= startTrailProfit)
      {
         int steps = (int)((profitDist - startTrailProfit) / trailStep);
         double newSL = openPrice - (trailStep * (1 + steps));
         
         if(newSL < currentSL - _Point || currentSL == 0)
         {
            if(trade.PositionModify(ticket, newSL, positionInfo.TakeProfit()))
            {
               Print("Trail SELL updated. New SL: ", newSL);
               if(TrailingActiveTicket != (int)ticket) SendTelegramAlert("TRAILING STARTED", (int)ticket, newSL);
               else SendTelegramAlert("TRAILING UPDATE", (int)ticket, newSL);
               TrailingActiveTicket = (int)ticket;
               LastAlertedSL = newSL;
            }
         }
      }
   }
}

//+------------------------------------------------------------------+
//| Helper: Calculate VWAP                                           |
//+------------------------------------------------------------------+
double CalculateVWAP()
{
   MqlRates rates[];
   int count = CopyRates(Symbol(), Period(), 0, Bars(Symbol(), Period()), rates);
   if(count <= 0) return 0;
   
   int current_day = 0;
   MqlDateTime dt;
   TimeCurrent(dt);
   current_day = dt.day;
   
   double sum_vol_price = 0;
   double sum_vol = 0;
   
   for(int i = 0; i < count; i++)
   {
      MqlDateTime barTime;
      TimeToStruct(rates[i].time, barTime);
      
      if(barTime.day != current_day) break;
      
      double typical = (rates[i].high + rates[i].low + rates[i].close) / 3.0;
      sum_vol_price += typical * (double)rates[i].tick_volume;
      sum_vol += (double)rates[i].tick_volume;
   }
   
   if(sum_vol == 0) return symbolInfo.Bid();
   return sum_vol_price / sum_vol;
}

//+------------------------------------------------------------------+
//| Helper: Open Trade                                               |
//+------------------------------------------------------------------+
void OpenTrade(ENUM_ORDER_TYPE type, double price, double sl, double tp)
{
   // Use current martingale lot size
   double lotToUse = NormalizeDouble(CurrentLotSize, 2);
   
   if(trade.PositionOpen(Symbol(), type, lotToUse, price, sl, tp, TradeComment))
   {
      Print("Trade Opened. Ticket: ", trade.ResultOrder(), " Lot: ", lotToUse, " Martingale Level: ", MartingaleLevel);
      PendingSignalType = 0;
      TrailingActiveTicket = 0;
      LastAlertedSL = 0;
      
      int ticket = (int)trade.ResultOrder();
      SendTelegramAlert((type == ORDER_TYPE_BUY ? "BUY OPENED" : "SELL OPENED"), ticket, price);
   }
   else
   {
      Print("Trade Failed: ", trade.ResultRetcode(), " ", trade.ResultRetcodeDescription());
   }
}

//+------------------------------------------------------------------+
//| Helper: Check Closed Trades (Telegram & Martingale)              |
//+------------------------------------------------------------------+
void CheckClosedTradesForTelegram()
{
   static int lastDealsTotal = 0;
   
   if(!HistorySelect(0, TimeCurrent())) return;
   int currentDeals = HistoryDealsTotal();
   
   if(currentDeals > lastDealsTotal)
   {
      for(int i = currentDeals - 1; i >= MathMax(0, currentDeals - 5); i--)
      {
         ulong dealTicket = HistoryDealGetTicket(i);
         if(dealTicket == 0) continue;
         
         long entryType = HistoryDealGetInteger(dealTicket, DEAL_ENTRY);
         long dealMagic = HistoryDealGetInteger(dealTicket, DEAL_MAGIC);
         string dealSymbol = HistoryDealGetString(dealTicket, DEAL_SYMBOL);
         
         if(dealMagic == MagicNumber && dealSymbol == Symbol() && entryType == DEAL_ENTRY_OUT) 
         {
            // Calculate if it was a win or loss
            double profit = HistoryDealGetDouble(dealTicket, DEAL_PROFIT);
            double swap = HistoryDealGetDouble(dealTicket, DEAL_SWAP);
            double commission = HistoryDealGetDouble(dealTicket, DEAL_COMMISSION);
            double totalProfit = profit + swap + commission;
            
            bool wasWin = (totalProfit > 0);
            
            // Update Martingale logic
            UpdateMartingaleAfterTrade(wasWin);
            
            double closePrice = HistoryDealGetDouble(dealTicket, DEAL_PRICE);
            SendTelegramAlert("TRADE CLOSED", (int)dealTicket, closePrice);
            
            TrailingActiveTicket = 0;
            LastAlertedSL = 0;
            break;
         }
      }
   }
   lastDealsTotal = currentDeals;
}

//+------------------------------------------------------------------+
//| Helper: Dashboard                                                 |
//+------------------------------------------------------------------+
void UpdateDashboard()
{
   string status = "SCANNING";
   if(PendingSignalType == 1) status = "WAITING BUY (Pullback)";
   if(PendingSignalType == 2) status = "WAITING SELL (Pullback)";
   
   string sessionStatus = "N/A";
   if(UseSessionFilter)
   {
      if(IsInTradingSession())
         sessionStatus = "IN SESSION";
      else
         sessionStatus = "OUT OF SESSION";
   }
   
   string text = "=== PM_SCALPER v2.0 MT5 ===\n";
   text += "Magic: " + IntegerToString(MagicNumber) + "\n";
   text += "Status: " + status + "\n";
   
   if(UseMartingale)
   {
      text += "\n--- MARTINGALE ---\n";
      text += "Current Lot: " + DoubleToString(CurrentLotSize, 2) + "\n";
      text += "Base Lot: " + DoubleToString(BaseLotSize, 2) + "\n";
      text += "Level: " + IntegerToString(MartingaleLevel) + " / " + IntegerToString(MaxMartingaleLevel) + "\n";
   }
   
   if(UseSessionFilter)
   {
      text += "\n--- SESSION ---\n";
      text += "Filter: " + sessionStatus + "\n";
      MqlDateTime dtGMT;
      TimeToStruct(TimeGMT(), dtGMT);
      text += "GMT Hour: " + IntegerToString(dtGMT.hour) + "\n";
   }
   
   if(PendingSignalType > 0) {
      text += "\n--- PENDING ---\n";
      text += "Target: " + DoubleToString(PendingTargetPrice, (int)SymbolInfoInteger(Symbol(), SYMBOL_DIGITS)) + "\n";
      text += "Price: " + DoubleToString(symbolInfo.Bid(), (int)SymbolInfoInteger(Symbol(), SYMBOL_DIGITS)) + "\n";
   }
   Comment(text);
}

//+------------------------------------------------------------------+
//| Helper: Send Telegram Alert (MT5 WebRequest)                     |
//+------------------------------------------------------------------+
void SendTelegramAlert(string type, int ticket, double price)
{
   if(TelegramToken == "" || TelegramChatID == "") return;
   string message = "";
   
   //--- TRADE OPENED ---
   if(type == "BUY OPENED" || type == "SELL OPENED")
   {
      if(!HistorySelectByPosition(ticket) && !positionInfo.SelectByTicket(ticket)) return;
      
      string direction = (type == "BUY OPENED") ? "BUY" : "SELL";
      double lots = 0;
      double sl = 0;
      double tp = 0;
      
      if(positionInfo.SelectByTicket(ticket))
      {
         lots = positionInfo.Volume();
         sl = positionInfo.StopLoss();
         tp = positionInfo.TakeProfit();
      }
      
      message = "** NEW SIGNAL **\n";
      message += "---------------\n";
      message += "Symbol: " + Symbol() + "\n";
      message += "Direction: " + direction + "\n";
      message += "Entry: " + DoubleToString(price, (int)SymbolInfoInteger(Symbol(), SYMBOL_DIGITS)) + "\n";
      message += "Lot: " + DoubleToString(lots, 2) + "\n";
      if(UseMartingale && MartingaleLevel > 0)
         message += "Martingale Level: " + IntegerToString(MartingaleLevel) + "\n";
      message += "TP: " + DoubleToString(tp, (int)SymbolInfoInteger(Symbol(), SYMBOL_DIGITS)) + "\n";
      message += "SL: " + DoubleToString(sl, (int)SymbolInfoInteger(Symbol(), SYMBOL_DIGITS)) + "\n";
      message += "---------------\n";
      message += "Time: " + TimeToString(TimeCurrent(), TIME_DATE|TIME_MINUTES);
   }
   //--- TRAILING STARTED ---
   else if(type == "TRAILING STARTED")
   {
      if(!positionInfo.SelectByTicket(ticket)) return;
      
      string direction = (positionInfo.PositionType() == POSITION_TYPE_BUY) ? "BUY" : "SELL";
      double currentPrice = (positionInfo.PositionType() == POSITION_TYPE_BUY) ? symbolInfo.Bid() : symbolInfo.Ask();
      
      message = ">> TRAILING STARTED <<\n";
      message += "---------------\n";
      message += "Symbol: " + Symbol() + "\n";
      message += "Direction: " + direction + "\n";
      message += "Entry: " + DoubleToString(positionInfo.PriceOpen(), (int)SymbolInfoInteger(Symbol(), SYMBOL_DIGITS)) + "\n";
      message += "New SL: " + DoubleToString(price, (int)SymbolInfoInteger(Symbol(), SYMBOL_DIGITS)) + "\n";
      message += "Current: " + DoubleToString(currentPrice, (int)SymbolInfoInteger(Symbol(), SYMBOL_DIGITS)) + "\n";
      message += "---------------\n";
      message += "Profit locked in";
   }
   //--- TRAILING UPDATE ---
   else if(type == "TRAILING UPDATE")
   {
      if(!positionInfo.SelectByTicket(ticket)) return;
      
      string direction = (positionInfo.PositionType() == POSITION_TYPE_BUY) ? "BUY" : "SELL";
      double currentPrice = (positionInfo.PositionType() == POSITION_TYPE_BUY) ? symbolInfo.Bid() : symbolInfo.Ask();
      
      message = ">> TRADE PROGRESS <<\n";
      message += "---------------\n";
      message += "Symbol: " + Symbol() + "\n";
      message += "Direction: " + direction + "\n";
      message += "Entry: " + DoubleToString(positionInfo.PriceOpen(), (int)SymbolInfoInteger(Symbol(), SYMBOL_DIGITS)) + "\n";
      message += "New SL: " + DoubleToString(price, (int)SymbolInfoInteger(Symbol(), SYMBOL_DIGITS)) + "\n";
      message += "Current: " + DoubleToString(currentPrice, (int)SymbolInfoInteger(Symbol(), SYMBOL_DIGITS)) + "\n";
      message += "---------------\n";
      message += "SL moved to lock more profit";
   }
   //--- TRADE CLOSED ---
   else if(type == "TRADE CLOSED")
   {
      if(!HistorySelectByPosition(ticket)) return;
      
      // Find the deal
      for(int i = HistoryDealsTotal() - 1; i >= 0; i--)
      {
         ulong dealTicket = HistoryDealGetTicket(i);
         if(dealTicket == 0) continue;
         
         long entryType = HistoryDealGetInteger(dealTicket, DEAL_ENTRY);
         if(entryType == DEAL_ENTRY_OUT)
         {
            long dealType = HistoryDealGetInteger(dealTicket, DEAL_TYPE);
            string direction = (dealType == DEAL_TYPE_BUY) ? "SELL" : "BUY"; // Opposite because it's closing
            
            double profit = HistoryDealGetDouble(dealTicket, DEAL_PROFIT);
            double swap = HistoryDealGetDouble(dealTicket, DEAL_SWAP);
            double commission = HistoryDealGetDouble(dealTicket, DEAL_COMMISSION);
            double totalProfit = profit + swap + commission;
            double lots = HistoryDealGetDouble(dealTicket, DEAL_VOLUME);
            
            string resultTag = (totalProfit >= 0) ? "[WIN]" : "[LOSS]";
            
            message = "TRADE CLOSED - " + resultTag + "\n";
            message += "---------------\n";
            message += "Symbol: " + Symbol() + "\n";
            message += "Direction: " + direction + "\n";
            message += "Lot: " + DoubleToString(lots, 2) + "\n";
            message += "Exit: " + DoubleToString(price, (int)SymbolInfoInteger(Symbol(), SYMBOL_DIGITS)) + "\n";
            message += "P/L: $" + DoubleToString(totalProfit, 2) + "\n";
            if(UseMartingale)
               message += "Next Lot: " + DoubleToString(CurrentLotSize, 2) + " (Level " + IntegerToString(MartingaleLevel) + ")\n";
            message += "---------------\n";
            message += "Time: " + TimeToString(TimeCurrent(), TIME_DATE|TIME_MINUTES);
            break;
         }
      }
   }
   
   string encoded_message = "";
   for(int i = 0; i < StringLen(message); i++) {
       ushort ch = StringGetCharacter(message, i);
       if(ch == ' ') encoded_message += "+";
       else if(ch == '\n') encoded_message += "%0A";
       else if(ch == ':') encoded_message += "%3A";
       else if(ch == '#') encoded_message += "%23";
       else if(ch == '$') encoded_message += "%24";
       else if(ch == '.') encoded_message += ".";
       else if(ch == '-') encoded_message += "-";
       else if(ch == '+') encoded_message += "%2B";
       else if(ch == '/') encoded_message += "%2F";
       else if(ch == '*') encoded_message += "%2A";
       else if(ch == '>') encoded_message += "%3E";
       else if(ch == '<') encoded_message += "%3C";
       else if(ch == '[') encoded_message += "%5B";
       else if(ch == ']') encoded_message += "%5D";
       else encoded_message += CharToString((uchar)ch);
   }
   
   string url = "https://api.telegram.org/bot" + TelegramToken + "/sendMessage";
   string postData = "chat_id=" + TelegramChatID + "&text=" + encoded_message;
   
   char postArray[], resultArray[];
   string resultHeaders;
   
   StringToCharArray(postData, postArray, 0, StringLen(postData), CP_UTF8);
   
   int res = WebRequest("POST", url, "Content-Type: application/x-www-form-urlencoded\r\n", 5000, postArray, resultArray, resultHeaders);
   
   if(res == -1) Print("Telegram Error. Check URL Permissions.");
   else Print("Telegram Alert sent: ", type);
}