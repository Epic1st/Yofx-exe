//+------------------------------------------------------------------+
//|                    APEX_M15_Scalper.mq5                         |
//|         Gold Scalper — Built from RF Feature Importance          |
//|                                                                  |
//|  STRATEGY:                                                       |
//|  Built around the top indicators identified by the Random        |
//|  Forest model on XAUUSD H4 data:                                |
//|    #1  price_vs_ema200  — macro trend filter (most important)    |
//|    #2  atr_14 / atr_7   — volatility context                    |
//|    #3  macd_signal/hist  — momentum confirmation                 |
//|    #4  stoch_d_14        — entry timing                         |
//|    #5  ema_9_21 diff     — short term trend                     |
//|                                                                  |
//|  ENTRY LOGIC:                                                    |
//|  BUY  — Price above EMA200 + EMA9 > EMA21 + MACD bullish        |
//|          + Stoch oversold recovering + ATR confirms activity     |
//|  SELL — Price below EMA200 + EMA9 < EMA21 + MACD bearish        |
//|          + Stoch overbought falling + ATR confirms activity      |
//|                                                                  |
//|  TARGET: 2-4 trades per day on XAUUSD M15                       |
//|  TIMEFRAME: M15 (primary) with H4 trend filter                  |
//+------------------------------------------------------------------+

#property copyright "APEX RF System"
#property version   "1.00"
#property strict

#include <Trade\Trade.mqh>
CTrade trade;

//+------------------------------------------------------------------+
//| INPUT PARAMETERS                                                 |
//+------------------------------------------------------------------+

input group "=== RISK MANAGEMENT ==="
input double   RiskPercent       = 1.0;    // Risk per trade (% of balance)
input double   MaxDailyLossPct   = 3.0;    // Stop trading if daily loss exceeds this %
input double   MaxDailyTrades    = 6;      // Max trades per day
input int      MagicNumber       = 88888;  // Unique ID for this EA

input group "=== EMA SETTINGS (RF Top Feature: price_vs_ema200) ==="
input int      EMA_Fast          = 9;      // Fast EMA period
input int      EMA_Mid           = 21;     // Mid EMA period
input int      EMA_Slow          = 50;     // Slow EMA period
input int      EMA_Trend         = 200;    // Trend filter EMA (most important per RF)

input group "=== MACD SETTINGS (RF Top Feature: macd_signal/hist) ==="
input int      MACD_Fast         = 12;     // MACD fast period
input int      MACD_Slow         = 26;     // MACD slow period
input int      MACD_Signal       = 9;      // MACD signal period

input group "=== STOCHASTIC SETTINGS (RF Top Feature: stoch_d_14) ==="
input int      Stoch_K           = 14;     // Stochastic %K period
input int      Stoch_D           = 3;      // Stochastic %D period
input int      Stoch_Slowing     = 3;      // Stochastic slowing
input double   Stoch_OB          = 75.0;   // Overbought level
input double   Stoch_OS          = 25.0;   // Oversold level

input group "=== ATR SETTINGS (RF Top Feature: atr_14/atr_7) ==="
input int      ATR_Period        = 14;     // ATR period for SL/TP
input double   ATR_SL_Mult       = 1.5;   // SL = ATR × this
input double   ATR_TP_Mult       = 2.5;   // TP = ATR × this
input double   ATR_Min_Mult      = 0.8;   // Min ATR to trade (filters low volatility)
input double   ATR_Max_Mult      = 3.0;   // Max ATR to trade (filters news spikes)

input group "=== SESSION FILTER ==="
input bool     TradeLondon       = true;   // 08:00 - 16:00 GMT
input bool     TradeNewYork      = true;   // 13:00 - 21:00 GMT
input bool     TradeAsian        = false;  // 00:00 - 08:00 GMT (low gold volume)
input int      StopMinsBeforeClose = 30;   // Stop trading N mins before session end

input group "=== SIGNAL FILTERS ==="
input bool     UseH4TrendFilter  = true;   // Only trade in direction of H4 trend
input bool     RequireEMAStack   = true;   // Require full EMA alignment (9>21>50)
input int      MinBarsBetweenTrades = 4;   // Minimum M15 bars between trades
input double   MinConfluenceScore = 3.0;   // Min signal score out of 5 to trade

input group "=== RF SIGNAL INTEGRATION ==="
input bool     UseRFFilter       = false;  // Use RF signal file as additional filter
input string   RFSignalFile      = "C:\\RF_GoldSystem\\signals\\rf_signal.txt";
input double   RFMinConfidence   = 60.0;   // Min RF confidence if using RF filter

//+------------------------------------------------------------------+
//| GLOBAL VARIABLES                                                 |
//+------------------------------------------------------------------+
int    g_ema_fast_handle, g_ema_mid_handle, g_ema_slow_handle, g_ema_trend_handle;
int    g_macd_handle, g_stoch_handle, g_atr_handle;
int    g_h4_ema_handle;   // H4 trend filter

datetime g_lastBarTime    = 0;
datetime g_lastTradeTime  = 0;
int      g_dailyTrades    = 0;
double   g_dailyStartBalance = 0;
datetime g_currentDay     = 0;
int      g_totalTrades    = 0;
int      g_winTrades      = 0;

//+------------------------------------------------------------------+
//| INITIALISE                                                       |
//+------------------------------------------------------------------+
int OnInit()
{
   // Create indicator handles
   g_ema_fast_handle  = iMA(_Symbol, PERIOD_M15, EMA_Fast,  0, MODE_EMA, PRICE_CLOSE);
   g_ema_mid_handle   = iMA(_Symbol, PERIOD_M15, EMA_Mid,   0, MODE_EMA, PRICE_CLOSE);
   g_ema_slow_handle  = iMA(_Symbol, PERIOD_M15, EMA_Slow,  0, MODE_EMA, PRICE_CLOSE);
   g_ema_trend_handle = iMA(_Symbol, PERIOD_M15, EMA_Trend, 0, MODE_EMA, PRICE_CLOSE);
   g_macd_handle      = iMACD(_Symbol, PERIOD_M15, MACD_Fast, MACD_Slow, MACD_Signal, PRICE_CLOSE);
   g_stoch_handle     = iStochastic(_Symbol, PERIOD_M15, Stoch_K, Stoch_D, Stoch_Slowing, MODE_SMA, STO_LOWHIGH);
   g_atr_handle       = iATR(_Symbol, PERIOD_M15, ATR_Period);
   g_h4_ema_handle    = iMA(_Symbol, PERIOD_H4, EMA_Trend, 0, MODE_EMA, PRICE_CLOSE);

   if(g_ema_fast_handle == INVALID_HANDLE || g_macd_handle == INVALID_HANDLE ||
      g_stoch_handle == INVALID_HANDLE || g_atr_handle == INVALID_HANDLE)
   {
      Print("❌ Failed to create indicator handles");
      return INIT_FAILED;
   }

   trade.SetExpertMagicNumber(MagicNumber);
   trade.SetDeviationInPoints(30);

   g_dailyStartBalance = AccountInfoDouble(ACCOUNT_BALANCE);
   g_currentDay        = StringToTime(TimeToString(TimeCurrent(), TIME_DATE));

   Print("╔══════════════════════════════════════╗");
   Print("║   APEX M15 Gold Scalper v1.0         ║");
   Print("║   RF-Optimised Entry Logic           ║");
   Print("╚══════════════════════════════════════╝");
   Print("Symbol: ", _Symbol, " | TF: M15");
   Print("Risk: ", RiskPercent, "% | Magic: ", MagicNumber);
   Print("Top RF features active: EMA200, ATR, MACD, Stoch");

   return INIT_SUCCEEDED;
}

//+------------------------------------------------------------------+
//| DEINIT                                                           |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   IndicatorRelease(g_ema_fast_handle);
   IndicatorRelease(g_ema_mid_handle);
   IndicatorRelease(g_ema_slow_handle);
   IndicatorRelease(g_ema_trend_handle);
   IndicatorRelease(g_macd_handle);
   IndicatorRelease(g_stoch_handle);
   IndicatorRelease(g_atr_handle);
   IndicatorRelease(g_h4_ema_handle);
   Comment("");

   double finalBalance = AccountInfoDouble(ACCOUNT_BALANCE);
   Print("EA stopped | Total trades: ", g_totalTrades,
         " | Wins: ", g_winTrades,
         " | Final balance: $", DoubleToString(finalBalance, 2));
}

//+------------------------------------------------------------------+
//| MAIN TICK                                                        |
//+------------------------------------------------------------------+
void OnTick()
{
   // Only run on new M15 bar close
   datetime currentBar = iTime(_Symbol, PERIOD_M15, 0);
   if(currentBar == g_lastBarTime) return;
   g_lastBarTime = currentBar;

   // Reset daily stats if new day
   ResetDailyStats();

   // Update dashboard
   UpdateDashboard();

   // Core checks
   if(!IsSessionActive())         return;
   if(IsDailyLimitReached())      return;
   if(HasOpenTrade())             return;
   if(!IsATRValid())              return;
   if(!BarsSinceLastTrade())      return;

   // Get all indicator values
   double emaFast  = GetEMA(g_ema_fast_handle);
   double emaMid   = GetEMA(g_ema_mid_handle);
   double emaSlow  = GetEMA(g_ema_slow_handle);
   double emaTrend = GetEMA(g_ema_trend_handle);
   double h4Trend  = GetEMA(g_h4_ema_handle);
   double macdMain, macdSig, macdHist;
   GetMACD(macdMain, macdSig, macdHist);
   double stochK, stochD;
   GetStoch(stochK, stochD);
   double atr      = GetATR();
   double price    = SymbolInfoDouble(_Symbol, SYMBOL_BID);

   // Calculate confluence scores
   double buyScore  = 0;
   double sellScore = 0;
   string buyReasons  = "";
   string sellReasons = "";

   // ── FACTOR 1: Price vs EMA200 (RF #1 most important — weight x2) ──
   if(price > emaTrend) { buyScore  += 2; buyReasons  += "EMA200✓ "; }
   else                 { sellScore += 2; sellReasons += "EMA200✓ "; }

   // ── FACTOR 2: H4 Trend Filter ──
   if(UseH4TrendFilter) {
      if(price > h4Trend) { buyScore  += 1; buyReasons  += "H4✓ "; }
      else                { sellScore += 1; sellReasons += "H4✓ "; }
   }

   // ── FACTOR 3: EMA Stack (9 > 21 > 50 = bullish stack) ──
   bool bullStack = (emaFast > emaMid && emaMid > emaSlow);
   bool bearStack = (emaFast < emaMid && emaMid < emaSlow);
   if(bullStack) { buyScore  += 1; buyReasons  += "EMAstack✓ "; }
   if(bearStack) { sellScore += 1; sellReasons += "EMAstack✓ "; }

   // ── FACTOR 4: MACD (RF #3 important) ──
   double prevMacdHist = GetPrevMACD();
   bool macdBull = (macdHist > 0 && macdHist > prevMacdHist);  // positive and rising
   bool macdBear = (macdHist < 0 && macdHist < prevMacdHist);  // negative and falling
   if(macdBull) { buyScore  += 1; buyReasons  += "MACD✓ "; }
   if(macdBear) { sellScore += 1; sellReasons += "MACD✓ "; }

   // ── FACTOR 5: Stochastic (RF #5 important — entry timing) ──
   // BUY: Stoch was oversold and now recovering upward
   // SELL: Stoch was overbought and now falling
   double prevStochD = GetPrevStochD();
   bool stochBuy  = (stochD < 40 && stochD > prevStochD);   // oversold, turning up
   bool stochSell = (stochD > 60 && stochD < prevStochD);   // overbought, turning down
   if(stochBuy)  { buyScore  += 1; buyReasons  += "Stoch✓ "; }
   if(stochSell) { sellScore += 1; sellReasons += "Stoch✓ "; }

   // ── FACTOR 6: EMA9/21 crossover momentum (RF #7) ──
   double prevEmaFast = GetPrevEMA(g_ema_fast_handle);
   double prevEmaMid  = GetPrevEMA(g_ema_mid_handle);
   bool freshBullCross = (emaFast > emaMid && prevEmaFast <= prevEmaMid);
   bool freshBearCross = (emaFast < emaMid && prevEmaFast >= prevEmaMid);
   if(freshBullCross) { buyScore  += 0.5; buyReasons  += "Cross✓ "; }
   if(freshBearCross) { sellScore += 0.5; sellReasons += "Cross✓ "; }

   // ── LOG SCORES ──
   Print("Bar: ", TimeToString(currentBar, TIME_DATE|TIME_MINUTES),
         " | BUY: ", DoubleToString(buyScore,1), " (", buyReasons, ")",
         " | SELL: ", DoubleToString(sellScore,1), " (", sellReasons, ")",
         " | ATR: ", DoubleToString(atr,2));

   // ── EXECUTE IF THRESHOLD MET ──
   bool rfApproves = !UseRFFilter || CheckRFSignal();

   if(buyScore >= MinConfluenceScore && buyScore > sellScore && rfApproves)
   {
      double sl = price - atr * ATR_SL_Mult;
      double tp = price + atr * ATR_TP_Mult;
      ExecuteTrade(ORDER_TYPE_BUY, sl, tp, buyScore, buyReasons);
   }
   else if(sellScore >= MinConfluenceScore && sellScore > buyScore && rfApproves)
   {
      double sl = price + atr * ATR_SL_Mult;
      double tp = price - atr * ATR_TP_Mult;
      ExecuteTrade(ORDER_TYPE_SELL, sl, tp, sellScore, sellReasons);
   }
}

//+------------------------------------------------------------------+
//| EXECUTE TRADE                                                    |
//+------------------------------------------------------------------+
void ExecuteTrade(ENUM_ORDER_TYPE type, double sl, double tp,
                  double score, string reasons)
{
   double price   = (type==ORDER_TYPE_BUY) ?
                    SymbolInfoDouble(_Symbol, SYMBOL_ASK) :
                    SymbolInfoDouble(_Symbol, SYMBOL_BID);
   double pipSize = SymbolInfoDouble(_Symbol, SYMBOL_POINT) * 10;
   double slPips  = MathAbs(price - sl) / pipSize;
   double lots    = CalculateLots(slPips);

   if(lots <= 0) { Print("❌ Invalid lot size"); return; }

   // Validate SL/TP
   if(type==ORDER_TYPE_BUY  && (sl >= price || tp <= price)) { Print("❌ Invalid BUY levels");  return; }
   if(type==ORDER_TYPE_SELL && (sl <= price || tp >= price)) { Print("❌ Invalid SELL levels"); return; }

   string comment = StringFormat("APEX_M15|Score:%.1f|%s", score, reasons);
   bool result;

   if(type == ORDER_TYPE_BUY)
      result = trade.Buy(lots, _Symbol, price, sl, tp, comment);
   else
      result = trade.Sell(lots, _Symbol, price, sl, tp, comment);

   if(result)
   {
      g_dailyTrades++;
      g_totalTrades++;
      g_lastTradeTime = iTime(_Symbol, PERIOD_M15, 0);

      string dir = (type==ORDER_TYPE_BUY) ? "▲ BUY" : "▼ SELL";
      Print("✅ ", dir, " | Lots: ", DoubleToString(lots,2),
            " | Entry: $", DoubleToString(price,2),
            " | SL: $", DoubleToString(sl,2),
            " | TP: $", DoubleToString(tp,2),
            " | Score: ", DoubleToString(score,1),
            " | ", reasons);
   }
   else
   {
      Print("❌ Trade failed: ", trade.ResultRetcodeDescription());
   }
}

//+------------------------------------------------------------------+
//| INDICATOR HELPERS                                                |
//+------------------------------------------------------------------+
double GetEMA(int handle)
{
   double buf[]; ArraySetAsSeries(buf, true);
   if(CopyBuffer(handle, 0, 0, 3, buf) < 3) return 0;
   return buf[1];  // Last closed bar
}

double GetPrevEMA(int handle)
{
   double buf[]; ArraySetAsSeries(buf, true);
   if(CopyBuffer(handle, 0, 0, 3, buf) < 3) return 0;
   return buf[2];
}

void GetMACD(double &main, double &sig, double &hist)
{
   double mainBuf[], sigBuf[];
   ArraySetAsSeries(mainBuf, true); ArraySetAsSeries(sigBuf, true);
   if(CopyBuffer(g_macd_handle, 0, 0, 3, mainBuf) < 3) { main=sig=hist=0; return; }
   if(CopyBuffer(g_macd_handle, 1, 0, 3, sigBuf)  < 3) { main=sig=hist=0; return; }
   main = mainBuf[1]; sig = sigBuf[1]; hist = main - sig;
}

double GetPrevMACD()
{
   double mainBuf[], sigBuf[];
   ArraySetAsSeries(mainBuf, true); ArraySetAsSeries(sigBuf, true);
   if(CopyBuffer(g_macd_handle, 0, 0, 3, mainBuf) < 3) return 0;
   if(CopyBuffer(g_macd_handle, 1, 0, 3, sigBuf)  < 3) return 0;
   return mainBuf[2] - sigBuf[2];
}

void GetStoch(double &k, double &d)
{
   double kBuf[], dBuf[];
   ArraySetAsSeries(kBuf, true); ArraySetAsSeries(dBuf, true);
   if(CopyBuffer(g_stoch_handle, 0, 0, 3, kBuf) < 3) { k=d=50; return; }
   if(CopyBuffer(g_stoch_handle, 1, 0, 3, dBuf) < 3) { k=d=50; return; }
   k = kBuf[1]; d = dBuf[1];
}

double GetPrevStochD()
{
   double dBuf[]; ArraySetAsSeries(dBuf, true);
   if(CopyBuffer(g_stoch_handle, 1, 0, 3, dBuf) < 3) return 50;
   return dBuf[2];
}

double GetATR()
{
   double buf[]; ArraySetAsSeries(buf, true);
   if(CopyBuffer(g_atr_handle, 0, 0, 3, buf) < 3) return 0;
   return buf[1];
}

//+------------------------------------------------------------------+
//| LOT SIZE CALCULATION                                             |
//+------------------------------------------------------------------+
double CalculateLots(double slPips)
{
   if(slPips <= 0) return 0;
   double balance    = AccountInfoDouble(ACCOUNT_BALANCE);
   double riskAmount = balance * RiskPercent / 100.0;
   double tickValue  = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE);
   double tickSize   = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
   double pipValue   = tickValue * (SymbolInfoDouble(_Symbol, SYMBOL_POINT)*10) / tickSize;
   double lots       = riskAmount / (slPips * pipValue);
   double lotStep    = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
   lots = MathFloor(lots / lotStep) * lotStep;
   double minLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double maxLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
   return MathMax(minLot, MathMin(maxLot, lots));
}

//+------------------------------------------------------------------+
//| FILTER FUNCTIONS                                                 |
//+------------------------------------------------------------------+
bool IsSessionActive()
{
   MqlDateTime dt; TimeToStruct(TimeCurrent(), dt);
   int hour = dt.hour;
   bool asian  = (hour >= 0  && hour < 8);
   bool london = (hour >= 8  && hour < 16);
   bool ny     = (hour >= 13 && hour < 21);
   if(asian  && TradeAsian)   return true;
   if(london && TradeLondon)  return true;
   if(ny     && TradeNewYork) return true;
   return false;
}

bool IsDailyLimitReached()
{
   if(g_dailyTrades >= MaxDailyTrades)
   {
      // Silent — don't spam log
      return true;
   }
   double balance     = AccountInfoDouble(ACCOUNT_BALANCE);
   double dailyPnL    = balance - g_dailyStartBalance;
   double dailyLossPct = (dailyPnL / g_dailyStartBalance) * 100;
   if(dailyLossPct <= -MaxDailyLossPct)
   {
      Print("⛔ Daily loss limit reached: ", DoubleToString(dailyLossPct,2), "%");
      return true;
   }
   return false;
}

bool HasOpenTrade()
{
   for(int i=0; i<PositionsTotal(); i++)
      if(PositionGetInteger(POSITION_MAGIC)==MagicNumber &&
         PositionGetString(POSITION_SYMBOL)==_Symbol) return true;
   return false;
}

bool IsATRValid()
{
   double atr   = GetATR();
   double price = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   if(atr <= 0) return false;
   double atrPct = atr / price * 100;
   // Filter: too quiet (< 0.05%) or too wild (> 0.4%) — likely news spike
   return (atrPct >= 0.05 && atrPct <= 0.4);
}

bool BarsSinceLastTrade()
{
   if(g_lastTradeTime == 0) return true;
   datetime current = iTime(_Symbol, PERIOD_M15, 0);
   int barsSince = (int)((current - g_lastTradeTime) / (15 * 60));
   return (barsSince >= MinBarsBetweenTrades);
}

bool CheckRFSignal()
{
   int handle = FileOpen(RFSignalFile,
                FILE_READ|FILE_TXT|FILE_ANSI|FILE_SHARE_READ|FILE_SHARE_WRITE);
   if(handle == INVALID_HANDLE) return false;
   string content = FileReadString(handle);
   FileClose(handle);
   string parts[]; int n = StringSplit(content, '|', parts);
   if(n < 2) return false;
   double conf = StringToDouble(parts[1]);
   return (conf >= RFMinConfidence && parts[0] != "HOLD");
}

void ResetDailyStats()
{
   datetime today = StringToTime(TimeToString(TimeCurrent(), TIME_DATE));
   if(today != g_currentDay)
   {
      g_currentDay        = today;
      g_dailyTrades       = 0;
      g_dailyStartBalance = AccountInfoDouble(ACCOUNT_BALANCE);
      Print("📅 New trading day — stats reset");
   }
}

//+------------------------------------------------------------------+
//| ON TRADE TRANSACTION — track wins                                |
//+------------------------------------------------------------------+
void OnTradeTransaction(const MqlTradeTransaction& trans,
                        const MqlTradeRequest& request,
                        const MqlTradeResult& result)
{
   if(trans.type == TRADE_TRANSACTION_DEAL_ADD)
   {
      if(HistoryDealSelect(trans.deal))
      {
         long magic  = HistoryDealGetInteger(trans.deal, DEAL_MAGIC);
         double profit = HistoryDealGetDouble(trans.deal, DEAL_PROFIT);
         if(magic == MagicNumber && profit != 0)
         {
            if(profit > 0) g_winTrades++;
            double winRate = g_totalTrades > 0 ? (double)g_winTrades/g_totalTrades*100 : 0;
            Print(profit > 0 ? "✅ WIN" : "❌ LOSS",
                  " | P&L: $", DoubleToString(profit,2),
                  " | Session win rate: ", DoubleToString(winRate,1), "%",
                  " | Today: ", g_dailyTrades, " trades");
         }
      }
   }
}

//+------------------------------------------------------------------+
//| DASHBOARD                                                        |
//+------------------------------------------------------------------+
void UpdateDashboard()
{
   double price    = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   double emaTrend = GetEMA(g_ema_trend_handle);
   double emaFast  = GetEMA(g_ema_fast_handle);
   double emaMid   = GetEMA(g_ema_mid_handle);
   double atr      = GetATR();
   double stochK, stochD; GetStoch(stochK, stochD);
   double macdMain, macdSig, macdHist; GetMACD(macdMain, macdSig, macdHist);
   double balance  = AccountInfoDouble(ACCOUNT_BALANCE);
   double dailyPnL = balance - g_dailyStartBalance;

   string trend    = price > emaTrend ? "▲ BULL" : "▼ BEAR";
   string session  = IsSessionActive() ? "ACTIVE" : "CLOSED";
   string winRate  = g_totalTrades > 0 ?
                     DoubleToString((double)g_winTrades/g_totalTrades*100,1)+"%" : "—";

   string info = StringFormat(
      "╔══ APEX M15 GOLD SCALPER ════════╗\n"
      "║ Price:    $%.2f\n"
      "║ EMA200:   $%.2f  [%s]\n"
      "║ EMA9/21:  %.2f / %.2f\n"
      "║ MACD:     %.4f\n"
      "║ Stoch%%D:  %.1f\n"
      "║ ATR:      %.2f\n"
      "╠═════════════════════════════════╣\n"
      "║ Session:  %s\n"
      "║ Today:    %d / %.0f trades\n"
      "║ Daily P&L: $%.2f\n"
      "║ Win rate: %s (%d/%d)\n"
      "╚═════════════════════════════════╝",
      price, emaTrend, trend,
      emaFast, emaMid,
      macdHist,
      stochD,
      atr,
      session,
      g_dailyTrades, MaxDailyTrades,
      dailyPnL,
      winRate, g_winTrades, g_totalTrades
   );
   Comment(info);
}
