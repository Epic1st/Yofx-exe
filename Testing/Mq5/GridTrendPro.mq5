//+------------------------------------------------------------------+
//|                      GridTrendPro EA.mq5                         |
//|           Grid + Trend + Price Action + Risk Management          |
//+------------------------------------------------------------------+
#property strict

//--- Input Parameters
input double   FixedLot            = 0.01;              // Fixed lot size
input double   RiskPercent         = 0;                 // 0 = off, 5 = 5% dari equity
input bool     UseMartingale       = false;             // Martingale: lot x2 tiap posisi
input double   MartingaleMultiplier = 1.5;              // Faktor martingale
input int      MaxPositionsPerCycle = 15;               // Maks posisi per siklus
input int      EMA_Period          = 20;                // EMA untuk tren
input int      ADX_Period          = 14;                // ADX untuk kekuatan tren
input double   ADX_Threshold       = 25;                // Min ADX untuk trading
input int      ATR_Period          = 14;                // ATR untuk jarak grid
input double   ATR_Multiplier      = 1.0;               // Jarak grid = ATR * multiplier
input int      ProfitTargetPips    = 5;                 // Target profit rata-rata (pips)
input int      TrailingStopPips    = 10;                // Trailing stop global (pips)
input double   MaxDrawdownPercent  = 10.0;              // Hentikan jika drawdown > X%
input int      Slippage            = 3;                 // Slippage maksimal
input bool     EnableNewsFilter    = true;              // Hindari trading saat news
input bool     SendEmailAlerts     = true;              // Kirim email saat open/close
input bool     PlaySoundAlerts     = true;              // Suara saat aktivitas

//--- Variabel Global
datetime      lastCandleTime = 0;
int           totalTradesInCycle = 0;
double        initialEquity;
double        peakEquity;
bool          inNewCycle = true;
int           magicNumber;

//+------------------------------------------------------------------+
//| Expert initialization function                                   |
//+------------------------------------------------------------------+
int OnInit()
{
   magicNumber = SymbolInfoInteger(_Symbol, SYMBOL_TICKER) + (int)TimeCurrent();
   initialEquity = AccountInfoDouble(ACCOUNT_EQUITY);
   peakEquity = initialEquity;

   EventSetTimer(60); // Cek tiap 60 detik
   Print("GridTrendPro EA dimulai. Magic: ", magicNumber);
   return(INIT_SUCCEEDED);
}

//+------------------------------------------------------------------+
//| Expert deinitialization function                                 |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   EventKillTimer();
   Print("GridTrendPro EA berhenti. Alasan: ", GetReason(reason));
}

string GetReason(int code)
{
   switch(code)
   {
      case REASON_CLOSE:      return "Terminal ditutup";
      case REASON_CHARTCLOSE: return "Chart ditutup";
      default:                return "Manual/Unknown";
   }
}

//+------------------------------------------------------------------+
//| Timer: Cek drawdown & trailing stop                              |
//+------------------------------------------------------------------+
void OnTimer()
{
   CheckDrawdown();
   ApplyGlobalTrailingStop();
}

//+------------------------------------------------------------------+
//| Expert tick function                                             |
//+------------------------------------------------------------------+
void OnTick()
{
   // Cek news filter
   if (EnableNewsFilter && IsHighImpactNewsTime())
      return;

   // Cek candle baru
   datetime currentCandleTime = iTime(_Symbol, PERIOD_M1, 0);
   if (currentCandleTime == lastCandleTime)
      return;
   lastCandleTime = currentCandleTime;

   // Cek apakah perlu close semua karena profit
   if (CheckAverageProfitAndClose())
   {
      inNewCycle = true;
      totalTradesInCycle = 0;
      Sleep(1000);
   }

   // Buka posisi jika dalam siklus dan belum capai max
   if (inNewCycle && totalTradesInCycle < MaxPositionsPerCycle)
   {
      if (IsValidTrend() && IsPriceActionSignal())
      {
         double lot = CalculateLotSize();
         double price = (PositionGetInteger(POSITION_TYPE) == POSITION_TYPE_BUY) ? Ask : Bid;
         int type = (iEMA(NULL, PERIOD_M1, EMA_Period, 0, MODE_MAIN, 0) > iEMA(NULL, PERIOD_M1, EMA_Period, 0, MODE_MAIN, 1)) ?
                    ORDER_TYPE_BUY : ORDER_TYPE_SELL;

         if (ExecuteOrder(type, price, lot, Slippage))
         {
            totalTradesInCycle++;
            LogActivity("OPEN", type, price, lot);
         }
      }
   }
}

//+------------------------------------------------------------------+
//| Cek tren kuat dengan EMA + ADX                                   |
//+------------------------------------------------------------------+
bool IsValidTrend()
{
   double emaCur = iEMA(NULL, PERIOD_M1, EMA_Period, 0, MODE_MAIN, 0);
   double emaPrev = iEMA(NULL, PERIOD_M1, EMA_Period, 0, MODE_MAIN, 1);
   double adx = iADX(NULL, PERIOD_M1, ADX_Period, 0, MODE_MAIN, 0);

   bool uptrend = (emaCur > emaPrev && adx >= ADX_Threshold);
   bool downtrend = (emaCur < emaPrev && adx >= ADX_Threshold);

   return uptrend || downtrend;
}

//+------------------------------------------------------------------+
//| Deteksi pola price action sederhana                              |
//+------------------------------------------------------------------+
bool IsPriceActionSignal()
{
   double body = MathAbs(Open[0] - Close[0]);
   double range = High[0] - Low[0];
   double upper = MathMax(Open[0], Close[0]);
   double lower = MathMin(Open[0], Close[0]);

   // Bullish Engulfing
   if (Close[0] > Open[0] && Open[0] < Close[1] && Close[0] > Open[1] && body/range > 0.6)
      return true;

   // Bearish Engulfing
   if (Close[0] < Open[0] && Open[0] > Close[1] && Close[0] < Open[1] && body/range > 0.6)
      return true;

   // Pinbar (ekor panjang)
   double upperWick = High[0] - upper;
   double lowerWick = lower - Low[0];
   if ((lowerWick > 2 * body && body/range < 0.35) || (upperWick > 2 * body && body/range < 0.35))
      return true;

   return false;
}

//+------------------------------------------------------------------+
//| Hitung lot dinamis                                               |
//+------------------------------------------------------------------+
double CalculateLotSize()
{
   if (UseMartingale)
   {
      double base = RiskPercent > 0 ? AccountInfoDouble(ACCOUNT_EQUITY) * RiskPercent / 10000 : FixedLot;
      return MathMax(base * MathPow(MartingaleMultiplier, totalTradesInCycle), SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN));
   }

   if (RiskPercent > 0)
   {
      double equity = AccountInfoDouble(ACCOUNT_EQUITY);
      double risk = equity * RiskPercent / 100;
      double tickValue = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE);
      double tickSize = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
      double lot = risk / (ATR_Multiplier * iATR(NULL, PERIOD_M1, ATR_Period, 1) / tickSize * tickValue);
      return MathMax(lot, SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN));
   }

   return FixedLot;
}

//+------------------------------------------------------------------+
//| Eksekusi order dengan proteksi                                   |
//+------------------------------------------------------------------+
bool ExecuteOrder(int type, double price, double lot, int slippage)
{
   MqlTradeRequest req;
   MqlTradeResult  res;
   ZeroMemory(req); ZeroMemory(res);

   req.action       = TRADE_ACTION_DEAL;
   req.symbol       = _Symbol;
   req.volume       = NormalizeDouble(lot, 2);
   req.type         = type;
   req.price        = price;
   req.deviation    = slippage;
   req.magic        = magicNumber;
   req.comment      = "GridTrendPro";
   req.type_filling = ORDER_FILLING_IOC;

   if (!OrderSend(req, res))
   {
      Print("OrderSend gagal: ", GetLastError());
      return false;
   }

   if (res.retcode != TRADE_RETCODE_DONE)
   {
      Print("Order gagal: ", res.retcode, " | ", res.comment);
      return false;
   }

   return true;
}

//+------------------------------------------------------------------+
//| Cek rata-rata profit dan tutup semua                             |
//+------------------------------------------------------------------+
bool CheckAverageProfitAndClose()
{
   int total = PositionsTotal();
   if (total == 0) return false;

   double totalProfit = 0;
   int count = 0;
   double point = _Point;

   for (int i = 0; i < total; i++)
   {
      if (PositionGetSymbol(i) != _Symbol || PositionGetInteger(POSITION_MAGIC) != magicNumber)
         continue;
      totalProfit += PositionGetDouble(POSITION_PROFIT);
      count++;
   }

   if (count == 0) return false;

   double avgProfitPips = (totalProfit / count) / point;
   if (avgProfitPips >= ProfitTargetPips)
   {
      CloseAllPositions();
      LogActivity("CLOSE_ALL", -1, 0, 0, "Avg Profit: " + DoubleToString(avgProfitPips, 2) + " pips");
      return true;
   }

   return false;
}

//+------------------------------------------------------------------+
//| Tutup semua posisi dengan magic number                           |
//+------------------------------------------------------------------+
void CloseAllPositions()
{
   for (int i = PositionsTotal() - 1; i >= 0; i--)
   {
      if (PositionSelectByIndex(i) && PositionGetInteger(POSITION_MAGIC) == magicNumber)
      {
         int type = PositionGetInteger(POSITION_TYPE);
         double price = (type == POSITION_TYPE_BUY) ? Bid : Ask;
         double volume = PositionGetDouble(POSITION_VOLUME);
         ulong ticket = PositionGetInteger(POSITION_TICKET);

         MqlTradeRequest req; MqlTradeResult res;
         ZeroMemory(req); ZeroMemory(res);

         req.action = TRADE_ACTION_DEAL;
         req.symbol = _Symbol;
         req.volume = volume;
         req.type = (type == POSITION_TYPE_BUY) ? ORDER_TYPE_SELL : ORDER_TYPE_BUY;
         req.price = price;
         req.deviation = Slippage;
         req.position = ticket;
         req.magic = magicNumber;
         req.type_filling = ORDER_FILLING_IOC;

         OrderSend(req, res);
      }
   }
}

//+------------------------------------------------------------------+
//| Terapkan trailing stop global                                  |
//+------------------------------------------------------------------+
void ApplyGlobalTrailingStop()
{
   if (TrailingStopPips <= 0) return;
   int total = PositionsTotal();
   double trailDist = TrailingStopPips * _Point;

   for (int i = 0; i < total; i++)
   {
      if (PositionSelectByIndex(i) && PositionGetInteger(POSITION_MAGIC) == magicNumber)
      {
         double open = PositionGetDouble(POSITION_PRICE_OPEN);
         double curr = PositionGetDouble(POSITION_TYPE) == POSITION_TYPE_BUY ? Bid : Ask;
         double sl = PositionGetDouble(POSITION_SL);

         if (PositionGetInteger(POSITION_TYPE) == POSITION_TYPE_BUY)
         {
            if (curr - open > trailDist && (sl < curr - trailDist || sl == 0))
            {
               MqlTradeRequest req; MqlTradeResult res;
               ZeroMemory(req); ZeroMemory(res);
               req.action = TRADE_ACTION_SLTP;
               req.symbol = _Symbol;
               req.position = PositionGetInteger(POSITION_TICKET);
               req.sl = curr - trailDist;
               req.magic = magicNumber;
               OrderSend(req, res);
            }
         }
         else
         {
            if (open - curr > trailDist && (sl > curr + trailDist || sl == 0))
            {
               MqlTradeRequest req; MqlTradeResult res;
               ZeroMemory(req); ZeroMemory(res);
               req.action = TRADE_ACTION_SLTP;
               req.symbol = _Symbol;
               req.position = PositionGetInteger(POSITION_TICKET);
               req.sl = curr + trailDist;
               req.magic = magicNumber;
               OrderSend(req, res);
            }
         }
      }
   }
}

//+------------------------------------------------------------------+
//| Cek drawdown dan hentikan jika melebihi batas                    |
//+------------------------------------------------------------------+
void CheckDrawdown()
{
   double equity = AccountInfoDouble(ACCOUNT_EQUITY);
   if (equity > peakEquity) peakEquity = equity;

   double drawdown = (peakEquity - equity) / peakEquity * 100;
   if (drawdown >= MaxDrawdownPercent)
   {
      CloseAllPositions();
      Alert("DRAWDOWN LIMIT TERLAMPAU: ", DoubleToString(drawdown, 2), "%. Semua posisi ditutup.");
      Print("DRAWDOWN LIMIT TERLAMPAU. Trading dihentikan.");
      ExpertRemove(); // Hentikan EA
   }
}

//+------------------------------------------------------------------+
//| Filter waktu high-impact news (contoh: EUR, USD)                   |
//+------------------------------------------------------------------+
bool IsHighImpactNewsTime()
{
   // Anda bisa integrasikan dengan calendar API atau file CSV
   // Untuk demo, kita skip
   return false;
}

//+------------------------------------------------------------------+
//| Log aktivitas ke file dan kirim notifikasi                       |
//+------------------------------------------------------------------+
void LogActivity(string action, int type, double price, double lot, string comment = "")
{
   string log = TimeToString(TimeCurrent()) + " | " + action;
   if (price > 0) log += " | Harga: " + DoubleToString(price, _Digits);
   if (lot > 0)   log += " | Lot: " + DoubleToString(lot, 2);
   if (comment != "") log += " | " + comment;

   Print(log);
   FileWriter("GridTrendPro_Log.txt", log);

   if (PlaySoundAlerts)
      Alert("GridTrendPro: " + action);

   if (SendEmailAlerts)
      SendMail("GridTrendPro Alert", log);
}

//+------------------------------------------------------------------+
//| Tulis log ke file                                                |
//+------------------------------------------------------------------+
void FileWriter(string filename, string text)
{
   int handle = FileOpen(filename, FILE_WRITE|FILE_TXT|FILE_ANSI);
   if (handle != INVALID_HANDLE)
   {
      FileSeek(handle, 0, SEEK_END);
      FileWrite(handle, text);
      FileClose(handle);
   }
}