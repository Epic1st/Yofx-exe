//+------------------------------------------------------------------+
//|                                              PM_Scalper_Pro.mq5  |
//|                                                     Prof. Morris  |
//|                             Professional Scalping EA - Rebuilt   |
//+------------------------------------------------------------------+
#property copyright "2026, Prof. Morris"
#property link      "https://t.me/profitabletrader2362"
#property version   "3.10"
#property strict

#include <Trade\Trade.mqh>
#include <Trade\PositionInfo.mqh>
#include <Trade\SymbolInfo.mqh>

//--- INPUT PARAMETERS
input string   Section1 = "==== RISK MANAGEMENT ====";
input double   RiskPercent         = 1.5;       // Risk Per Trade (% of Balance)
input double   MaxLotSize          = 10.0;      // Maximum Lot Size
input double   MinLotSize          = 0.01;      // Minimum Lot Size
input double   StopLoss_ATR_Mult   = 1.2;       // Stop Loss (ATR Multiplier)
input double   TakeProfit1_ATR     = 2.0;       // First TP - 50% Close (ATR Mult)
input double   TakeProfit2_ATR     = 4.0;       // Final TP - 50% Close (ATR Mult)

input string   Section2 = "==== POSITION SIZING STRATEGY ====";
input bool     UseAntiMartingale   = true;      // Increase Size After Wins
input double   WinMultiplier       = 1.2;       // Multiply Lot After Win
input int      MaxWinStreak        = 3;         // Max Consecutive Win Increases
input bool     ReduceAfterLoss     = true;      // Reduce After 2 Losses
input double   LossMultiplier      = 0.7;       // Reduce Lot After 2 Losses

input string   Section3 = "==== INDICATORS & CONFIRMATIONS ====";
input int      ATR_Period          = 14;        // ATR Period
input int      RSI_Period          = 14;        // RSI Period
input int      RSI_OverSold        = 30;        // RSI Oversold
input int      RSI_OverBought      = 70;        // RSI Overbought
input int      MACD_Fast           = 12;        // MACD Fast EMA
input int      MACD_Slow           = 26;        // MACD Slow EMA
input int      MACD_Signal         = 9;         // MACD Signal
input ENUM_TIMEFRAMES HTF_Period   = PERIOD_H4; // Higher Timeframe
input int      HTF_MA_Period       = 50;        // HTF Trend MA Period

input string   Section4 = "==== ENTRY FILTERS ====";
input bool     UsePriceAction      = true;      // Require Price Action Pattern
input bool     UseHTFTrend         = true;      // Higher Timeframe Trend Filter
input bool     UseMACDConfirm      = true;      // MACD Momentum Confirmation
input bool     UseVolatilityFilter = true;      // Volatility Regime Filter
input double   MinVolatility       = 0.5;       // Min ATR/Price Ratio (%)
input double   MaxVolatility       = 3.0;       // Max ATR/Price Ratio (%)

input string   Section5 = "==== EXIT STRATEGY ====";
input bool     UseBreakeven        = true;      // Move to Breakeven
input double   Breakeven_ATR       = 1.0;       // Breakeven After X ATR Profit
input double   Breakeven_Offset    = 0.3;       // Breakeven Offset (ATR)
input bool     UseTrailing         = true;      // Trailing Stop (After TP1)
input double   Trail_ATR_Mult      = 0.8;       // Trailing Distance (ATR)

input string   Section6 = "==== SESSION & TIME FILTERS ====";
input bool     UseSessionFilter    = true;      // Enable Session Filter
input bool     TradeLondonSession  = true;      // Trade London (08:00-17:00 GMT)
input bool     TradeNYSession      = true;      // Trade NY (13:00-22:00 GMT)
input int      LondonStartGMT      = 8;         // London Start Hour
input int      LondonEndGMT        = 17;        // London End Hour
input int      NYStartGMT          = 13;        // NY Start Hour
input int      NYEndGMT            = 22;        // NY End Hour

input string   Section6B = "==== NEWS FILTER ====";
input bool     UseNewsFilter       = true;      // Enable News Pause
input int      NewsMinutesBefore   = 15;        // Pause X Minutes BEFORE News
input int      NewsMinutesAfter    = 10;        // Pause X Minutes AFTER News
input bool     PauseOnHighImpact   = true;      // Pause on High Impact News
input bool     PauseOnMediumImpact = false;     // Pause on Medium Impact News
input bool     CloseBeforeNews     = false;     // Close Open Positions Before News
input int      CloseMinutesBefore  = 5;         // Close Positions X Min Before News
input string   NewsCountries       = "USD,EUR,GBP,JPY,AUD,NZD,CAD,CHF"; // News Countries (comma-separated)

input string   Section7 = "==== TELEGRAM ====";
input string   TelegramToken       = "";        // Bot Token
input string   TelegramChatID      = "";        // Chat ID

input string   Section8 = "==== GENERAL ====";
input string   TradeComment        = "PM_Pro";  // Trade Comment
input ulong    Slippage            = 3;         // Slippage

//--- GLOBAL OBJECTS
CTrade trade;
CPositionInfo positionInfo;
CSymbolInfo symbolInfo;

//--- GLOBAL VARIABLES
int MagicNumber;
datetime LastBarTime = 0;

// Indicator handles
int handle_atr;
int handle_rsi;
int handle_macd;
int handle_htf_ma;

// Position Management
double CurrentLotSize = 0.01;
int WinStreak = 0;
int LossStreak = 0;
datetime LastTradeCloseTime = 0;

// News Filter
bool NewsPauseActive = false;
datetime NextNewsTime = 0;
string NextNewsTitle = "";

// Trade Tracking
struct TradeInfo {
   ulong ticket1;
   ulong ticket2;
   double entryPrice;
   double sl;
   double tp1;
   double tp2;
   datetime openTime;
   bool tp1Hit;
   bool beSet;
};
TradeInfo activeTrade;

//+------------------------------------------------------------------+
//| Detect allowed filling mode for current symbol                    |
//| Gold/CFDs often reject IOC; this auto-detects the right mode     |
//+------------------------------------------------------------------+
ENUM_ORDER_TYPE_FILLING GetAllowedFillingMode()
{
   uint filling = (uint)SymbolInfoInteger(Symbol(), SYMBOL_FILLING_MODE);
   
   if((filling & SYMBOL_FILLING_FOK) == SYMBOL_FILLING_FOK)
      return ORDER_FILLING_FOK;
   
   if((filling & SYMBOL_FILLING_IOC) == SYMBOL_FILLING_IOC)
      return ORDER_FILLING_IOC;
   
   return ORDER_FILLING_RETURN;
}

//+------------------------------------------------------------------+
//| Normalize price to symbol's tick size (critical for Gold/CFDs)    |
//+------------------------------------------------------------------+
double NormalizePrice(double price)
{
   double tickSize = SymbolInfoDouble(Symbol(), SYMBOL_TRADE_TICK_SIZE);
   if(tickSize == 0) return NormalizeDouble(price, _Digits);
   return NormalizeDouble(MathRound(price / tickSize) * tickSize, _Digits);
}

//+------------------------------------------------------------------+
//| Normalize lot to symbol's volume step and limits                  |
//+------------------------------------------------------------------+
double NormalizeLot(double lots)
{
   double minLot  = SymbolInfoDouble(Symbol(), SYMBOL_VOLUME_MIN);
   double maxLot  = SymbolInfoDouble(Symbol(), SYMBOL_VOLUME_MAX);
   double lotStep = SymbolInfoDouble(Symbol(), SYMBOL_VOLUME_STEP);
   
   if(lotStep == 0) lotStep = 0.01;
   
   lots = MathFloor(lots / lotStep) * lotStep;
   lots = MathMax(minLot, MathMin(maxLot, lots));
   lots = MathMax(MinLotSize, MathMin(MaxLotSize, lots));
   
   // Determine decimal places from lot step
   int digits = 0;
   double tmp = lotStep;
   while(MathAbs(MathRound(tmp) - tmp) > 0.0000001 && digits < 8)
   {
      tmp *= 10;
      digits++;
   }
   
   return NormalizeDouble(lots, digits);
}

//+------------------------------------------------------------------+
//| Expert initialization function                                     |
//+------------------------------------------------------------------+
int OnInit()
{
   if(!symbolInfo.Name(Symbol())) return(INIT_FAILED);
   
   //--- Generate hidden random Magic Number using multiple entropy sources
   MathSrand((int)(GetMicrosecondCount() ^ TimeLocal()));
   int rand1 = MathRand();
   int rand2 = MathRand();
   
   int symHash = 0;
   string sym = Symbol();
   for(int i = 0; i < StringLen(sym); i++) 
      symHash += StringGetCharacter(sym, i) * (i + 1);
   
   int tfHash = (int)Period() * 137;
   
   // Combine entropy sources into 6-digit range [100000..999999]
   MagicNumber = (int)(((long)rand1 * rand2 + symHash + tfHash) % 900000) + 100000;
   if(MagicNumber < 0) MagicNumber = -MagicNumber;
   
   trade.SetExpertMagicNumber(MagicNumber);
   trade.SetDeviationInPoints(Slippage);
   trade.SetTypeFilling(GetAllowedFillingMode());
   
   // Create indicators
   handle_atr = iATR(Symbol(), Period(), ATR_Period);
   handle_rsi = iRSI(Symbol(), Period(), RSI_Period, PRICE_CLOSE);
   handle_macd = iMACD(Symbol(), Period(), MACD_Fast, MACD_Slow, MACD_Signal, PRICE_CLOSE);
   handle_htf_ma = iMA(Symbol(), HTF_Period, HTF_MA_Period, 0, MODE_EMA, PRICE_CLOSE);
   
   if(handle_atr == INVALID_HANDLE || handle_rsi == INVALID_HANDLE || 
      handle_macd == INVALID_HANDLE || handle_htf_ma == INVALID_HANDLE)
   {
      Print("Error creating indicators");
      return(INIT_FAILED);
   }
   
   Print("=== PM_SCALPER_PRO v3.10 MT5 ===");
   Print("Symbol: ", Symbol(), " | Digits: ", _Digits, " | Point: ", _Point);
   Print("Tick Size: ", SymbolInfoDouble(Symbol(), SYMBOL_TRADE_TICK_SIZE),
         " | Tick Value: ", SymbolInfoDouble(Symbol(), SYMBOL_TRADE_TICK_VALUE));
   Print("Lot Min: ", SymbolInfoDouble(Symbol(), SYMBOL_VOLUME_MIN),
         " | Lot Step: ", SymbolInfoDouble(Symbol(), SYMBOL_VOLUME_STEP),
         " | Lot Max: ", SymbolInfoDouble(Symbol(), SYMBOL_VOLUME_MAX));
   Print("Filling Mode: ", EnumToString(GetAllowedFillingMode()));
   Print("Risk Per Trade: ", RiskPercent, "%");
   Print("Multi-Confirmation Entry Active");
   if(UseNewsFilter)
      Print("News Filter: ON (Pause ", NewsMinutesBefore, "m before / ", NewsMinutesAfter, "m after)");
   
   // Initialize trade info
   activeTrade.ticket1 = 0;
   activeTrade.ticket2 = 0;
   activeTrade.tp1Hit = false;
   activeTrade.beSet = false;
   
   // Load state
   if(GlobalVariableCheck("PMPro_Lot_" + Symbol()))
   {
      CurrentLotSize = GlobalVariableGet("PMPro_Lot_" + Symbol());
      WinStreak = (int)GlobalVariableGet("PMPro_WinStreak_" + Symbol());
      LossStreak = (int)GlobalVariableGet("PMPro_LossStreak_" + Symbol());
   }
   else
   {
      CurrentLotSize = CalculateBaseLotSize();
   }
   
   return(INIT_SUCCEEDED);
}

//+------------------------------------------------------------------+
//| Expert deinitialization function                                   |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   GlobalVariableSet("PMPro_Lot_" + Symbol(), CurrentLotSize);
   GlobalVariableSet("PMPro_WinStreak_" + Symbol(), WinStreak);
   GlobalVariableSet("PMPro_LossStreak_" + Symbol(), LossStreak);
   
   IndicatorRelease(handle_atr);
   IndicatorRelease(handle_rsi);
   IndicatorRelease(handle_macd);
   IndicatorRelease(handle_htf_ma);
   Comment("");
}

//+------------------------------------------------------------------+
//| Expert tick function                                               |
//+------------------------------------------------------------------+
void OnTick()
{
   if(!symbolInfo.RefreshRates()) return;
   
   CheckClosedTrades();
   ManageOpenPositions();
   
   //--- News filter: close positions if configured
   if(UseNewsFilter && CloseBeforeNews && CountOpenPositions() > 0)
   {
      if(IsNewsCloseWindow())
      {
         CloseAllPositions();
         SendTelegramAlert("NEWS CLOSE", 0, 0, 0);
      }
   }
   
   datetime timeArray[1];
   CopyTime(Symbol(), Period(), 0, 1, timeArray);
   
   if(timeArray[0] == LastBarTime) return;
   LastBarTime = timeArray[0];
   
   if(CountOpenPositions() > 0) return;
   
   if(UseSessionFilter && !IsInTradingSession()) return;
   
   //--- News filter: block new entries
   if(UseNewsFilter && IsNewsPauseActive())
   {
      Print("Trading paused - News event: ", NextNewsTitle);
      UpdateDashboard();
      return;
   }
   
   if(UseVolatilityFilter && !CheckVolatility()) return;
   
   int signal = AnalyzeMarket();
   
   if(signal == 1) OpenPositions(ORDER_TYPE_BUY);
   else if(signal == -1) OpenPositions(ORDER_TYPE_SELL);
   
   UpdateDashboard();
}

//+------------------------------------------------------------------+
//| NEWS FILTER - Check if trading should be paused                   |
//+------------------------------------------------------------------+
bool IsNewsPauseActive()
{
   NewsPauseActive = false;
   NextNewsTime = 0;
   NextNewsTitle = "";
   
   datetime now = TimeCurrent();
   datetime from = now - NewsMinutesAfter * 60;   // Check recent past
   datetime to   = now + NewsMinutesBefore * 60;   // Check near future
   
   //--- Parse news countries
   string countries[];
   int countryCount = StringSplit(NewsCountries, ',', countries);
   
   //--- Use MQL5 Economic Calendar
   MqlCalendarValue values[];
   
   if(CalendarValueHistory(values, from, to) > 0)
   {
      for(int i = 0; i < ArraySize(values); i++)
      {
         //--- Get event details
         MqlCalendarEvent event;
         if(!CalendarEventById(values[i].event_id, event))
            continue;
         
         //--- Check importance level
         bool relevantImpact = false;
         if(PauseOnHighImpact && event.importance == CALENDAR_IMPORTANCE_HIGH)
            relevantImpact = true;
         if(PauseOnMediumImpact && event.importance == CALENDAR_IMPORTANCE_MODERATE)
            relevantImpact = true;
         
         if(!relevantImpact)
            continue;
         
         //--- Check if event country matches our filter
         MqlCalendarCountry country;
         if(!CalendarCountryById(event.country_id, country))
            continue;
         
         bool countryMatch = false;
         for(int c = 0; c < countryCount; c++)
         {
            StringTrimLeft(countries[c]);
            StringTrimRight(countries[c]);
            if(StringFind(country.currency, countries[c]) >= 0)
            {
               countryMatch = true;
               break;
            }
         }
         
         if(!countryMatch)
            continue;
         
         //--- Check if this also matches our traded symbol's currencies
         string symBase = SymbolInfoString(Symbol(), SYMBOL_CURRENCY_BASE);
         string symQuote = SymbolInfoString(Symbol(), SYMBOL_CURRENCY_PROFIT);
         
         if(StringFind(country.currency, symBase) < 0 && 
            StringFind(country.currency, symQuote) < 0)
            continue;
         
         //--- We have a relevant news event in our window
         datetime eventTime = values[i].time;
         
         // Check if we are within the pause window
         if(now >= eventTime - NewsMinutesBefore * 60 && 
            now <= eventTime + NewsMinutesAfter * 60)
         {
            NewsPauseActive = true;
            NextNewsTime = eventTime;
            NextNewsTitle = event.name;
            
            Print("NEWS PAUSE: ", event.name, " (", country.currency, ") at ", 
                  TimeToString(eventTime, TIME_MINUTES), 
                  " Importance: ", EnumToString(event.importance));
            return true;
         }
         
         // Track the next upcoming event for dashboard
         if(eventTime > now && (NextNewsTime == 0 || eventTime < NextNewsTime))
         {
            NextNewsTime = eventTime;
            NextNewsTitle = event.name;
         }
      }
   }
   
   return false;
}

//+------------------------------------------------------------------+
//| NEWS FILTER - Check if we should close before news                |
//+------------------------------------------------------------------+
bool IsNewsCloseWindow()
{
   if(!CloseBeforeNews) return false;
   
   datetime now = TimeCurrent();
   datetime from = now;
   datetime to   = now + CloseMinutesBefore * 60;
   
   string countries[];
   int countryCount = StringSplit(NewsCountries, ',', countries);
   
   MqlCalendarValue values[];
   
   if(CalendarValueHistory(values, from, to) > 0)
   {
      for(int i = 0; i < ArraySize(values); i++)
      {
         MqlCalendarEvent event;
         if(!CalendarEventById(values[i].event_id, event))
            continue;
         
         if(event.importance != CALENDAR_IMPORTANCE_HIGH)
            continue;
         
         MqlCalendarCountry country;
         if(!CalendarCountryById(event.country_id, country))
            continue;
         
         // Check country relevance
         bool countryMatch = false;
         for(int c = 0; c < countryCount; c++)
         {
            StringTrimLeft(countries[c]);
            StringTrimRight(countries[c]);
            if(StringFind(country.currency, countries[c]) >= 0)
            {
               countryMatch = true;
               break;
            }
         }
         if(!countryMatch) continue;
         
         // Check symbol relevance
         string symBase = SymbolInfoString(Symbol(), SYMBOL_CURRENCY_BASE);
         string symQuote = SymbolInfoString(Symbol(), SYMBOL_CURRENCY_PROFIT);
         
         if(StringFind(country.currency, symBase) >= 0 || 
            StringFind(country.currency, symQuote) >= 0)
         {
            datetime eventTime = values[i].time;
            int minutesUntil = (int)(eventTime - now) / 60;
            
            if(minutesUntil <= CloseMinutesBefore && minutesUntil >= 0)
            {
               Print("CLOSING before news: ", event.name, " in ", minutesUntil, " minutes");
               return true;
            }
         }
      }
   }
   
   return false;
}

//+------------------------------------------------------------------+
//| Close All Positions for this EA                                   |
//+------------------------------------------------------------------+
void CloseAllPositions()
{
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      if(positionInfo.SelectByIndex(i))
      {
         if(positionInfo.Symbol() == Symbol() && positionInfo.Magic() == MagicNumber)
         {
            trade.PositionClose(positionInfo.Ticket());
         }
      }
   }
   
   activeTrade.ticket1 = 0;
   activeTrade.ticket2 = 0;
   activeTrade.tp1Hit = false;
   activeTrade.beSet = false;
}

//+------------------------------------------------------------------+
//| Analyze Market                                                    |
//+------------------------------------------------------------------+
int AnalyzeMarket()
{
   double atr_buf[1], rsi_buf[1];
   double macd_main[2], macd_signal[1];
   
   if(CopyBuffer(handle_atr, 0, 1, 1, atr_buf) < 0 ||
      CopyBuffer(handle_rsi, 0, 1, 1, rsi_buf) < 0 ||
      CopyBuffer(handle_macd, 0, 1, 2, macd_main) < 0 ||
      CopyBuffer(handle_macd, 1, 1, 1, macd_signal) < 0)
   {
      Print("Failed to copy indicator buffers");
      return 0;
   }
   
   double atr = atr_buf[0];
   double rsi = rsi_buf[0];
   
   if(atr == 0) 
   {
      Print("ATR is zero");
      return 0;
   }
   
   int htfTrend = 0;
   if(UseHTFTrend)
   {
      htfTrend = GetHTFTrend();
      if(htfTrend == 0) 
      {
         Print("No clear HTF trend");
         return 0;
      }
   }
   else
   {
      htfTrend = 1;
   }
   
   int priceAction = 0;
   if(UsePriceAction)
   {
      priceAction = DetectPriceAction();
      if(priceAction == 0) 
      {
         Print("No price action pattern detected");
         return 0;
      }
   }
   else
   {
      MqlRates rates[];
      if(CopyRates(Symbol(), Period(), 1, 2, rates) >= 2)
      {
         if(rates[1].close > rates[1].open) priceAction = 1;
         else if(rates[1].close < rates[1].open) priceAction = -1;
      }
   }
   
   double close_buf[2];
   if(CopyClose(Symbol(), Period(), 1, 2, close_buf) < 0)
   {
      Print("Failed to copy close prices");
      return 0;
   }
   double srLevel = FindNearestSupportResistance(close_buf[0]);
   
   // BUY SIGNAL
   bool buySignal = true;
   int buyBlockers = 0;
   
   if(UseHTFTrend && htfTrend != 1) 
   {
      buySignal = false;
      buyBlockers++;
      Print("BUY blocked: HTF trend not bullish");
   }
   
   if(UsePriceAction && priceAction != 1) 
   {
      buySignal = false;
      buyBlockers++;
      Print("BUY blocked: Price action not bullish");
   }
   
   if(rsi > RSI_OverBought) 
   {
      buySignal = false;
      buyBlockers++;
      Print("BUY blocked: RSI overbought (", rsi, ")");
   }
   
   if(UseMACDConfirm)
   {
      bool macdBullish = (macd_main[0] > macd_signal[0]) || (macd_main[0] > macd_main[1] && macd_main[0] > 0);
      if(!macdBullish) 
      {
         buySignal = false;
         buyBlockers++;
         Print("BUY blocked: MACD not bullish");
      }
   }
   
   bool nearSupport = (MathAbs(close_buf[0] - srLevel) < atr * 1.5) && (close_buf[0] >= srLevel - atr * 0.5);
   if(!nearSupport) 
   {
      buySignal = false;
      buyBlockers++;
      Print("BUY blocked: Not near support (Distance: ", MathAbs(close_buf[0] - srLevel), " ATR: ", atr, ")");
   }
   
   if(buySignal) 
   {
      Print("BUY SIGNAL CONFIRMED! RSI: ", rsi, " MACD: ", macd_main[0], " HTF: ", htfTrend);
      return 1;
   }
   else
   {
      if(buyBlockers == 0) Print("BUY signal had ", buyBlockers, " blockers but still rejected");
   }
   
   // SELL SIGNAL
   bool sellSignal = true;
   int sellBlockers = 0;
   
   if(UseHTFTrend && htfTrend != -1) 
   {
      sellSignal = false;
      sellBlockers++;
      Print("SELL blocked: HTF trend not bearish");
   }
   
   if(UsePriceAction && priceAction != -1) 
   {
      sellSignal = false;
      sellBlockers++;
      Print("SELL blocked: Price action not bearish");
   }
   
   if(rsi < RSI_OverSold) 
   {
      sellSignal = false;
      sellBlockers++;
      Print("SELL blocked: RSI oversold (", rsi, ")");
   }
   
   if(UseMACDConfirm)
   {
      bool macdBearish = (macd_main[0] < macd_signal[0]) || (macd_main[0] < macd_main[1] && macd_main[0] < 0);
      if(!macdBearish) 
      {
         sellSignal = false;
         sellBlockers++;
         Print("SELL blocked: MACD not bearish");
      }
   }
   
   bool nearResistance = (MathAbs(close_buf[0] - srLevel) < atr * 1.5) && (close_buf[0] <= srLevel + atr * 0.5);
   if(!nearResistance) 
   {
      sellSignal = false;
      sellBlockers++;
      Print("SELL blocked: Not near resistance");
   }
   
   if(sellSignal) 
   {
      Print("SELL SIGNAL CONFIRMED! RSI: ", rsi, " MACD: ", macd_main[0], " HTF: ", htfTrend);
      return -1;
   }
   
   return 0;
}

//+------------------------------------------------------------------+
//| Detect Price Action Patterns                                      |
//+------------------------------------------------------------------+
int DetectPriceAction()
{
   MqlRates rates[];
   if(CopyRates(Symbol(), Period(), 1, 3, rates) < 3) return 0;
   
   double body1 = MathAbs(rates[1].close - rates[1].open);
   double body2 = MathAbs(rates[0].close - rates[0].open);
   double upperWick1 = rates[1].high - MathMax(rates[1].close, rates[1].open);
   double lowerWick1 = MathMin(rates[1].close, rates[1].open) - rates[1].low;
   
   double atr_buf[1];
   CopyBuffer(handle_atr, 0, 1, 1, atr_buf);
   double atr = atr_buf[0];
   
   // Bullish Engulfing
   if(rates[1].close > rates[1].open && rates[0].close < rates[0].open)
   {
      if(body1 > body2 * 1.2 && rates[1].close > rates[0].open && rates[1].open < rates[0].close)
         return 1;
   }
   
   // Bullish Pin Bar
   if(lowerWick1 > body1 * 2 && upperWick1 < body1)
   {
      if(rates[1].close > rates[1].open && lowerWick1 > atr * 0.5)
         return 1;
   }
   
   // Bullish Inside Bar Breakout
   if(rates[1].high > rates[0].high && rates[1].low > rates[0].low && rates[1].close > rates[1].open)
   {
      if(body1 > atr * 0.3)
         return 1;
   }
   
   // Bearish Engulfing
   if(rates[1].close < rates[1].open && rates[0].close > rates[0].open)
   {
      if(body1 > body2 * 1.2 && rates[1].close < rates[0].open && rates[1].open > rates[0].close)
         return -1;
   }
   
   // Bearish Pin Bar
   if(upperWick1 > body1 * 2 && lowerWick1 < body1)
   {
      if(rates[1].close < rates[1].open && upperWick1 > atr * 0.5)
         return -1;
   }
   
   // Bearish Inside Bar Breakout
   if(rates[1].high < rates[0].high && rates[1].low < rates[0].low && rates[1].close < rates[1].open)
   {
      if(body1 > atr * 0.3)
         return -1;
   }
   
   return 0;
}

//+------------------------------------------------------------------+
//| Get HTF Trend                                                     |
//+------------------------------------------------------------------+
int GetHTFTrend()
{
   double htf_ma[2], htf_close[2];
   
   if(CopyBuffer(handle_htf_ma, 0, 0, 2, htf_ma) < 2 ||
      CopyClose(Symbol(), HTF_Period, 0, 2, htf_close) < 2)
      return 0;
   
   if(htf_close[0] > htf_ma[0] && htf_ma[0] > htf_ma[1]) return 1;
   if(htf_close[0] < htf_ma[0] && htf_ma[0] < htf_ma[1]) return -1;
   
   return 0;
}

//+------------------------------------------------------------------+
//| Find Nearest Support/Resistance                                   |
//+------------------------------------------------------------------+
double FindNearestSupportResistance(double price)
{
   MqlRates rates[];
   int copied = CopyRates(Symbol(), Period(), 2, 50, rates);
   if(copied < 5) return price;
   
   double levels[];
   ArrayResize(levels, 0);
   
   for(int i = 2; i < copied - 2; i++)
   {
      if(rates[i].high > rates[i-1].high && rates[i].high > rates[i+1].high && 
         rates[i].high > rates[i-2].high && rates[i].high > rates[i+2].high)
      {
         int size = ArraySize(levels);
         ArrayResize(levels, size + 1);
         levels[size] = rates[i].high;
      }
      
      if(rates[i].low < rates[i-1].low && rates[i].low < rates[i+1].low && 
         rates[i].low < rates[i-2].low && rates[i].low < rates[i+2].low)
      {
         int size = ArraySize(levels);
         ArrayResize(levels, size + 1);
         levels[size] = rates[i].low;
      }
   }
   
   double nearestLevel = price;
   double minDist = 999999;
   
   for(int i = 0; i < ArraySize(levels); i++)
   {
      double dist = MathAbs(price - levels[i]);
      if(dist < minDist)
      {
         minDist = dist;
         nearestLevel = levels[i];
      }
   }
   
   return nearestLevel;
}

//+------------------------------------------------------------------+
//| Check Volatility                                                  |
//+------------------------------------------------------------------+
bool CheckVolatility()
{
   double atr_buf[1], close_buf[1];
   
   if(CopyBuffer(handle_atr, 0, 1, 1, atr_buf) < 0 ||
      CopyClose(Symbol(), Period(), 1, 1, close_buf) < 0)
      return false;
   
   double atrPercent = (atr_buf[0] / close_buf[0]) * 100;
   
   if(atrPercent < MinVolatility || atrPercent > MaxVolatility)
   {
      Print("Volatility filtered: ATR%=", DoubleToString(atrPercent, 4), 
            " (Range: ", MinVolatility, "-", MaxVolatility, "%)");
      return false;
   }
   
   return true;
}

//+------------------------------------------------------------------+
//| Calculate Base Lot Size                                           |
//+------------------------------------------------------------------+
double CalculateBaseLotSize()
{
   double balance = AccountInfoDouble(ACCOUNT_BALANCE);
   double riskAmount = balance * RiskPercent / 100.0;
   
   double atr_buf[1];
   if(CopyBuffer(handle_atr, 0, 1, 1, atr_buf) < 0 || atr_buf[0] == 0)
      return MinLotSize;
   double atr = atr_buf[0];
   
   double slDistance = atr * StopLoss_ATR_Mult;
   
   double tickValue = SymbolInfoDouble(Symbol(), SYMBOL_TRADE_TICK_VALUE);
   double tickSize = SymbolInfoDouble(Symbol(), SYMBOL_TRADE_TICK_SIZE);
   
   if(tickValue <= 0 || tickSize <= 0 || slDistance <= 0)
   {
      Print("LotCalc: Invalid tick data - TV:", tickValue, " TS:", tickSize, " SL:", slDistance);
      return MinLotSize;
   }
   
   double slTicks = slDistance / tickSize;
   double lotSize = riskAmount / (slTicks * tickValue);
   
   return NormalizeLot(lotSize);
}

//+------------------------------------------------------------------+
//| Open Positions                                                    |
//+------------------------------------------------------------------+
void OpenPositions(ENUM_ORDER_TYPE type)
{
   double atr_buf[1];
   CopyBuffer(handle_atr, 0, 1, 1, atr_buf);
   double atr = atr_buf[0];
   
   // Refresh rates immediately before opening
   if(!symbolInfo.RefreshRates())
   {
      Print("OpenPositions: Failed to refresh rates");
      return;
   }
   
   double price = (type == ORDER_TYPE_BUY) ? symbolInfo.Ask() : symbolInfo.Bid();
   double sl, tp1, tp2;
   
   if(type == ORDER_TYPE_BUY)
   {
      sl = price - (atr * StopLoss_ATR_Mult);
      tp1 = price + (atr * TakeProfit1_ATR);
      tp2 = price + (atr * TakeProfit2_ATR);
   }
   else
   {
      sl = price + (atr * StopLoss_ATR_Mult);
      tp1 = price - (atr * TakeProfit1_ATR);
      tp2 = price - (atr * TakeProfit2_ATR);
   }
   
   // Normalize all prices to symbol tick size (critical for Gold/CFDs)
   price = NormalizePrice(price);
   sl    = NormalizePrice(sl);
   tp1   = NormalizePrice(tp1);
   tp2   = NormalizePrice(tp2);
   
   // Normalize lot to symbol's volume step
   double halfLot = NormalizeLot(CurrentLotSize / 2.0);
   
   // Validate stops distance
   int stopsLevel = (int)SymbolInfoInteger(Symbol(), SYMBOL_TRADE_STOPS_LEVEL);
   double minStopDist = stopsLevel * _Point;
   double slDist = MathAbs(price - sl);
   
   if(slDist < minStopDist)
   {
      Print("OpenPositions: SL too close. Distance: ", slDist, " Min: ", minStopDist);
      sl = (type == ORDER_TYPE_BUY) ? price - minStopDist - _Point : price + minStopDist + _Point;
      sl = NormalizePrice(sl);
   }
   
   Print("Opening: ", EnumToString(type), " Price:", price, " SL:", sl, 
         " TP1:", tp1, " TP2:", tp2, " Lot:", halfLot,
         " Spread:", symbolInfo.Spread());
   
   // Open first position
   if(trade.PositionOpen(Symbol(), type, halfLot, price, sl, tp1, TradeComment + "_1"))
   {
      activeTrade.ticket1 = trade.ResultOrder();
      Print("Position 1 opened: ", activeTrade.ticket1);
   }
   else
   {
      Print("Position 1 FAILED: ", trade.ResultRetcodeDescription(), 
            " (Code:", trade.ResultRetcode(), ")");
   }
   
   // Refresh price for second order (may have moved)
   symbolInfo.RefreshRates();
   price = (type == ORDER_TYPE_BUY) ? symbolInfo.Ask() : symbolInfo.Bid();
   price = NormalizePrice(price);
   
   // Open second position
   if(trade.PositionOpen(Symbol(), type, halfLot, price, sl, tp2, TradeComment + "_2"))
   {
      activeTrade.ticket2 = trade.ResultOrder();
      Print("Position 2 opened: ", activeTrade.ticket2);
   }
   else
   {
      Print("Position 2 FAILED: ", trade.ResultRetcodeDescription(),
            " (Code:", trade.ResultRetcode(), ")");
   }
   
   if(activeTrade.ticket1 > 0 && activeTrade.ticket2 > 0)
   {
      activeTrade.entryPrice = price;
      activeTrade.sl = sl;
      activeTrade.tp1 = tp1;
      activeTrade.tp2 = tp2;
      activeTrade.openTime = TimeCurrent();
      activeTrade.tp1Hit = false;
      activeTrade.beSet = false;
      
      SendTelegramAlert("TRADE OPENED", type, price, CurrentLotSize);
   }
}

//+------------------------------------------------------------------+
//| Manage Open Positions                                             |
//+------------------------------------------------------------------+
void ManageOpenPositions()
{
   if(activeTrade.ticket1 == 0 && activeTrade.ticket2 == 0) return;
   
   bool pos1Exists = positionInfo.SelectByTicket(activeTrade.ticket1);
   bool pos2Exists = positionInfo.SelectByTicket(activeTrade.ticket2);
   
   if(!pos1Exists && !pos2Exists)
   {
      activeTrade.ticket1 = 0;
      activeTrade.ticket2 = 0;
      return;
   }
   
   double atr_buf[1];
   CopyBuffer(handle_atr, 0, 0, 1, atr_buf);
   double atr = atr_buf[0];
   
   ENUM_POSITION_TYPE posType = POSITION_TYPE_BUY;
   double currentPrice = 0;
   
   if(pos1Exists)
   {
      positionInfo.SelectByTicket(activeTrade.ticket1);
      posType = positionInfo.PositionType();
      currentPrice = (posType == POSITION_TYPE_BUY) ? symbolInfo.Bid() : symbolInfo.Ask();
   }
   else if(pos2Exists)
   {
      positionInfo.SelectByTicket(activeTrade.ticket2);
      posType = positionInfo.PositionType();
      currentPrice = (posType == POSITION_TYPE_BUY) ? symbolInfo.Bid() : symbolInfo.Ask();
   }
   else
   {
      return;
   }
   
   double profit = 0;
   if(posType == POSITION_TYPE_BUY)
      profit = currentPrice - activeTrade.entryPrice;
   else
      profit = activeTrade.entryPrice - currentPrice;
   
   // Breakeven
   if(UseBreakeven && !activeTrade.beSet && profit >= atr * Breakeven_ATR)
   {
      double newSL = NormalizePrice(activeTrade.entryPrice + ((posType == POSITION_TYPE_BUY ? 1 : -1) * atr * Breakeven_Offset));
      
      if(pos1Exists)
      {
         positionInfo.SelectByTicket(activeTrade.ticket1);
         trade.PositionModify(activeTrade.ticket1, newSL, positionInfo.TakeProfit());
      }
      if(pos2Exists)
      {
         positionInfo.SelectByTicket(activeTrade.ticket2);
         trade.PositionModify(activeTrade.ticket2, newSL, positionInfo.TakeProfit());
      }
      
      activeTrade.beSet = true;
      activeTrade.sl = newSL;
      SendTelegramAlert("BREAKEVEN", (int)posType, newSL, 0);
   }
   
   // Trailing
   if(UseTrailing && !pos1Exists && pos2Exists && activeTrade.tp1Hit)
   {
      positionInfo.SelectByTicket(activeTrade.ticket2);
      double currentSL = positionInfo.StopLoss();
      
      double trailDistance = atr * Trail_ATR_Mult;
      double newSL = 0;
      
      if(posType == POSITION_TYPE_BUY)
      {
         newSL = NormalizePrice(currentPrice - trailDistance);
         if(newSL > currentSL + _Point)
         {
            trade.PositionModify(activeTrade.ticket2, newSL, positionInfo.TakeProfit());
            activeTrade.sl = newSL;
         }
      }
      else
      {
         newSL = NormalizePrice(currentPrice + trailDistance);
         if(newSL < currentSL - _Point || currentSL == 0)
         {
            trade.PositionModify(activeTrade.ticket2, newSL, positionInfo.TakeProfit());
            activeTrade.sl = newSL;
         }
      }
   }
}

//+------------------------------------------------------------------+
//| Check Closed Trades                                               |
//+------------------------------------------------------------------+
void CheckClosedTrades()
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
         datetime dealTime = (datetime)HistoryDealGetInteger(dealTicket, DEAL_TIME);
         
         if(dealMagic == MagicNumber && dealSymbol == Symbol() && entryType == DEAL_ENTRY_OUT)
         {
            if(dealTime > LastTradeCloseTime)
            {
               LastTradeCloseTime = dealTime;
               
               double profit = HistoryDealGetDouble(dealTicket, DEAL_PROFIT);
               double swap = HistoryDealGetDouble(dealTicket, DEAL_SWAP);
               double commission = HistoryDealGetDouble(dealTicket, DEAL_COMMISSION);
               double totalProfit = profit + swap + commission;
               
               bool wasWin = (totalProfit > 0);
               string comment = HistoryDealGetString(dealTicket, DEAL_COMMENT);
               
               if(StringFind(comment, "_1") >= 0 && wasWin)
               {
                  activeTrade.tp1Hit = true;
                  SendTelegramAlert("TP1 HIT", 0, 0, totalProfit);
               }
               
               if(CountOpenPositions() == 0)
               {
                  UpdatePositionSizing(wasWin);
                  SendTelegramAlert("TRADE CLOSED", 0, 0, totalProfit);
               }
            }
         }
      }
   }
   lastDealsTotal = currentDeals;
}

//+------------------------------------------------------------------+
//| Update Position Sizing                                            |
//+------------------------------------------------------------------+
void UpdatePositionSizing(bool wasWin)
{
   double baseLot = CalculateBaseLotSize();
   
   if(wasWin)
   {
      LossStreak = 0;
      
      if(UseAntiMartingale && WinStreak < MaxWinStreak)
      {
         WinStreak++;
         CurrentLotSize = NormalizeLot(CurrentLotSize * WinMultiplier);
      }
   }
   else
   {
      WinStreak = 0;
      LossStreak++;
      
      if(ReduceAfterLoss && LossStreak >= 2)
      {
         CurrentLotSize = NormalizeLot(baseLot * LossMultiplier);
      }
      else
      {
         CurrentLotSize = baseLot;
      }
   }
   
   GlobalVariableSet("PMPro_Lot_" + Symbol(), CurrentLotSize);
   GlobalVariableSet("PMPro_WinStreak_" + Symbol(), WinStreak);
   GlobalVariableSet("PMPro_LossStreak_" + Symbol(), LossStreak);
}

//+------------------------------------------------------------------+
//| Count Open Positions                                              |
//+------------------------------------------------------------------+
int CountOpenPositions()
{
   int count = 0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      if(positionInfo.SelectByIndex(i))
      {
         if(positionInfo.Symbol() == Symbol() && positionInfo.Magic() == MagicNumber)
            count++;
      }
   }
   return count;
}

//+------------------------------------------------------------------+
//| Check Trading Session                                             |
//+------------------------------------------------------------------+
bool IsInTradingSession()
{
   MqlDateTime dtGMT;
   TimeToStruct(TimeGMT(), dtGMT);
   int currentHourGMT = dtGMT.hour;
   
   bool inLondon = false;
   bool inNY = false;
   
   if(TradeLondonSession)
   {
      if(currentHourGMT >= LondonStartGMT && currentHourGMT < LondonEndGMT)
         inLondon = true;
   }
   
   if(TradeNYSession)
   {
      if(currentHourGMT >= NYStartGMT && currentHourGMT < NYEndGMT)
         inNY = true;
   }
   
   return (inLondon || inNY);
}

//+------------------------------------------------------------------+
//| Dashboard                                                         |
//+------------------------------------------------------------------+
void UpdateDashboard()
{
   string text = "=== PM_SCALPER_PRO v3.10 MT5 ===\n";
   text += "Account: $" + DoubleToString(AccountInfoDouble(ACCOUNT_BALANCE), 2) + "\n\n";
   
   text += "--- POSITION SIZING ---\n";
   text += "Current Lot: " + DoubleToString(CurrentLotSize, 2) + "\n";
   text += "Win Streak: " + IntegerToString(WinStreak) + "\n";
   text += "Loss Streak: " + IntegerToString(LossStreak) + "\n\n";
   
   //--- News filter status
   if(UseNewsFilter)
   {
      text += "--- NEWS FILTER ---\n";
      if(NewsPauseActive)
      {
         text += "!! PAUSED - " + NextNewsTitle + " !!\n";
         text += "Event: " + TimeToString(NextNewsTime, TIME_MINUTES) + "\n";
      }
      else if(NextNewsTime > 0)
      {
         int minsUntil = (int)(NextNewsTime - TimeCurrent()) / 60;
         text += "Next: " + NextNewsTitle + " (" + IntegerToString(minsUntil) + "m)\n";
      }
      else
      {
         text += "No upcoming events\n";
      }
      text += "\n";
   }
   
   if(CountOpenPositions() > 0)
   {
      text += "--- ACTIVE TRADE ---\n";
      text += "Positions: " + IntegerToString(CountOpenPositions()) + "\n";
      if(activeTrade.tp1Hit) text += "Status: TP1 HIT\n";
      if(activeTrade.beSet) text += "Status: Breakeven\n";
   }
   else
   {
      if(NewsPauseActive)
         text += "Status: NEWS PAUSE\n";
      else
         text += "Status: SCANNING\n";
   }
   
   Comment(text);
}

//+------------------------------------------------------------------+
//| Send Telegram Alert                                               |
//+------------------------------------------------------------------+
void SendTelegramAlert(string type, int orderType, double price, double value)
{
   if(TelegramToken == "" || TelegramChatID == "") return;
   
   string message = "";
   string direction = (orderType == ORDER_TYPE_BUY || orderType == POSITION_TYPE_BUY) ? "BUY" : "SELL";
   
   if(type == "TRADE OPENED")
   {
      message = "** NEW TRADE **\nDirection: " + direction + "\nEntry: " + DoubleToString(price, _Digits) + 
                "\nLot: " + DoubleToString(value, 2) + "\nStrategy: Multi-Confirm";
   }
   else if(type == "TP1 HIT")
   {
      message = ">> TP1 HIT <<\n50% Closed\nProfit: $" + DoubleToString(value, 2) + "\nTrailing remaining";
   }
   else if(type == "BREAKEVEN")
   {
      message = ">> BREAKEVEN SET <<\nNew SL: " + DoubleToString(price, _Digits) + "\nRisk-free";
   }
   else if(type == "TRADE CLOSED")
   {
      string result = (value > 0) ? "[WIN]" : "[LOSS]";
      message = "TRADE CLOSED " + result + "\nP/L: $" + DoubleToString(value, 2) + 
                "\nNext Lot: " + DoubleToString(CurrentLotSize, 2);
   }
   else if(type == "NEWS CLOSE")
   {
      message = ">> POSITIONS CLOSED <<\nReason: Upcoming news event\n" + NextNewsTitle;
   }
   
   string encoded_message = "";
   for(int i = 0; i < StringLen(message); i++)
   {
      ushort ch = StringGetCharacter(message, i);
      if(ch == ' ') encoded_message += "+";
      else if(ch == '\n') encoded_message += "%0A";
      else if(ch == ':') encoded_message += "%3A";
      else if(ch == '$') encoded_message += "%24";
      else encoded_message += CharToString((uchar)ch);
   }
   
   string url = "https://api.telegram.org/bot" + TelegramToken + "/sendMessage";
   string postData = "chat_id=" + TelegramChatID + "&text=" + encoded_message;
   
   char postArray[], resultArray[];
   string resultHeaders;
   
   StringToCharArray(postData, postArray, 0, StringLen(postData), CP_UTF8);
   WebRequest("POST", url, "Content-Type: application/x-www-form-urlencoded\r\n", 
              5000, postArray, resultArray, resultHeaders);
}