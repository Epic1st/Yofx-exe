//+------------------------------------------------------------------+
//|                                                     4rexbot.mq5 |
//|          ICT / Smart Money Concept Expert Advisor                |
//|          Instruments: XAUUSD, BTCUSD, USOIL, GBPUSD             |
//|          Strategy: Order Blocks, FVG, Breaker Blocks             |
//+------------------------------------------------------------------+
#property copyright   "4rexbot — ICT Smart Money"
#property version     "1.00"
#property strict

#include <Trade\Trade.mqh>
#include <Trade\PositionInfo.mqh>

//+------------------------------------------------------------------+
//| SECTION 1 — INPUT PARAMETERS                                     |
//+------------------------------------------------------------------+

// Account Mode
enum ACCOUNT_TYPE { LIVE, PROP };
input ACCOUNT_TYPE AccountType       = PROP;          // Account Type (LIVE / PROP)
input double       LiveRiskPercent   = 10.0;          // LIVE: Risk % per trade
input double       PropRiskPercent   = 1.0;           // PROP: Risk % per trade

// Instruments
input bool  TradeXAUUSD  = "";   // Trade XAUUSD
input bool  TradeBTCUSD  = "";   // Trade BTCUSD
input bool  TradeUSOIL   = "";   // Trade USOIL
input bool  TradeGBPUSD  = "";   // Trade GBPUSD

// Strategy
input int   MagicNumber   = 202401;  // EA Magic Number
input int   Lookback4H    = 10;      // 4H candles to analyse for trend
input int   LookbackEntry = 20;      // Candles to scan for OB/FVG/BB
input double FVGMinPips   = 5.0;     // Minimum FVG size (pips)
input double OBBufferPips = 2.0;     // Buffer inside OB/FVG zone (pips)

// Trade management
input double BreakevenRR  = 2.0;    // R:R to move SL to breakeven
input double PartialCloseRR = 3.0;  // R:R to close 50% of position

// Session (UTC)
input int SessionStartHour = 7;     // Session start hour UTC (default 7)
input int SessionEndHour   = 22;    // Session end hour UTC (default 22)

// Notifications
input string TelegramBotToken = "";  // Telegram Bot Token
input string TelegramChatID   = "";  // Telegram Chat ID
input string EmailAddress     = "";  // Email for trade notifications

// Limits
input int MaxPairsOpen    = 2;      // Max simultaneous pairs open
input int MaxTradesPerPair = 2;     // Max trades per pair

//+------------------------------------------------------------------+
//| SECTION 2 — GLOBAL VARIABLES                                     |
//+------------------------------------------------------------------+

CTrade    trade;
CPositionInfo posInfo;

// Tracked symbols
string g_symbols[];
int    g_symbolCount = 0;

// Telegram polling
long   g_lastUpdateId   = 0;
datetime g_lastPollTime = 0;
bool   g_tradingPaused  = false;

// Performance stats
int    g_totalTrades    = 0;
int    g_winTrades      = 0;
int    g_lossTrades     = 0;
double g_totalPips      = 0.0;
double g_maxDrawdown    = 0.0;
double g_peakBalance    = 0.0;
datetime g_weekStart    = 0;

// Weekly report
datetime g_lastWeeklyReport = 0;

// Timer counters
int g_timerCount = 0;   // counts 5-min ticks for Telegram polling (every 30s)

// Original lots tracker (for partial close logic)
struct TradeRecord {
    ulong  ticket;
    double originalLots;
    double entryPrice;
    double slPrice;
    double tpPrice;
    bool   breakEvenDone;
    bool   partialCloseDone;
    string symbol;
};
TradeRecord g_tradeRecords[];

// POI Structures
struct PriceZone {
    bool   valid;
    double high;
    double low;
    int    direction; // 1=bullish, -1=bearish
    datetime time;
};

//+------------------------------------------------------------------+
//| SECTION 3 — OnInit()                                             |
//+------------------------------------------------------------------+
int OnInit() {
    // Build symbol list based on inputs
    g_symbolCount = 0;
    ArrayResize(g_symbols, 4);
    if (TradeXAUUSD) { g_symbols[g_symbolCount] = "XAUUSD"; g_symbolCount++; }
    if (TradeBTCUSD) { g_symbols[g_symbolCount] = "BTCUSD"; g_symbolCount++; }
    if (TradeUSOIL)  { g_symbols[g_symbolCount] = "USOIL";  g_symbolCount++; }
    if (TradeGBPUSD) { g_symbols[g_symbolCount] = "GBPUSD"; g_symbolCount++; }
    ArrayResize(g_symbols, g_symbolCount);

    // Set magic number on trade object
    trade.SetExpertMagicNumber(MagicNumber);

    // Load stats from CSV
    LoadStats();

    // Init weekly start if not set
    if (g_weekStart == 0) SetWeekStart();

    // Init peak balance
    g_peakBalance = AccountInfoDouble(ACCOUNT_BALANCE);

    // Init Telegram polling offset
    g_lastUpdateId = 0;

    // Timer: fires every 30 seconds (we manage 5-min heartbeat with a counter)
    EventSetTimer(30);

    Print("4rexbot initialized. Symbols: ", g_symbolCount,
          " | AccountType: ", (AccountType == LIVE ? "LIVE" : "PROP"),
          " | Risk: ", (AccountType == LIVE ? LiveRiskPercent : PropRiskPercent), "%");

    SendTelegram("✅ 4rexbot started on " + AccountInfoString(ACCOUNT_SERVER) +
                 " | Mode: " + (AccountType == LIVE ? "LIVE" : "PROP"));

    return INIT_SUCCEEDED;
}

//+------------------------------------------------------------------+
//| SECTION 4 — OnDeinit()                                           |
//+------------------------------------------------------------------+
void OnDeinit(const int reason) {
    EventKillTimer();
    SaveStats();
    SendTelegram("⛔ 4rexbot stopped. Reason code: " + IntegerToString(reason));
    Print("4rexbot deinitialized. Reason: ", reason);
}

//+------------------------------------------------------------------+
//| SECTION 5 — OnTick()                                             |
//+------------------------------------------------------------------+
void OnTick() {
    // Gate checks
    if (g_tradingPaused) return;
    if (!IsSessionActive()) return;

    // Manage open trades first (trailing stop, partial close)
    ManageOpenTrades();

    // Count open pairs to enforce MaxPairsOpen
    int openPairs = CountOpenPairs();
    if (openPairs >= MaxPairsOpen) return;

    // Loop through symbols
    for (int i = 0; i < g_symbolCount; i++) {
        string sym = g_symbols[i];

        // Check max trades per pair
        if (CountTradesForSymbol(sym) >= MaxTradesPerPair) continue;

        // News filter: skip if news is within 1 hour
        if (IsNewsWithin1Hour(sym)) continue;

        // Get 4H trend
        int trend = GetTrend4H(sym);
        if (trend == 0) continue; // No clear trend, skip

        // Try to find a valid Point of Interest
        double askPrice = SymbolInfoDouble(sym, SYMBOL_ASK);
        double bidPrice = SymbolInfoDouble(sym, SYMBOL_BID);
        double currentPrice = (askPrice + bidPrice) / 2.0;

        // Scan for entry on 15M/30M
        PriceZone ob  = FindOrderBlock(sym, PERIOD_M15);
        PriceZone fvg = FindFVG(sym, PERIOD_M15);
        PriceZone bb  = FindBreakerBlock(sym, PERIOD_M15);

        PriceZone ob30  = FindOrderBlock(sym, PERIOD_M30);
        PriceZone fvg30 = FindFVG(sym, PERIOD_M30);
        PriceZone bb30  = FindBreakerBlock(sym, PERIOD_M30);

        // Choose best POI (prefer 30M, fallback to 15M)
        PriceZone poi;
        poi.valid = false;

        if (ob30.valid && ob30.direction == trend) poi = ob30;
        else if (fvg30.valid && fvg30.direction == trend) poi = fvg30;
        else if (bb30.valid && bb30.direction == trend) poi = bb30;
        else if (ob.valid && ob.direction == trend) poi = ob;
        else if (fvg.valid && fvg.direction == trend) poi = fvg;
        else if (bb.valid && bb.direction == trend) poi = bb;

        if (!poi.valid) continue;

        // Check if price is inside the POI zone
        if (!IsPriceInZone(currentPrice, poi)) continue;

        // Calculate SL and TP
        double point = SymbolInfoDouble(sym, SYMBOL_POINT);
        double sl, tp, entry;

        if (trend == 1) { // BUY
            entry = askPrice;
            sl    = poi.low - OBBufferPips * point * 10;
            tp    = FindRecentResistance(sym, PERIOD_H4);
            if (tp <= entry || tp == 0) continue;
        } else { // SELL
            entry = bidPrice;
            sl    = poi.high + OBBufferPips * point * 10;
            tp    = FindRecentSupport(sym, PERIOD_H4);
            if (tp >= entry || tp == 0) continue;
        }

        // Validate SL distance
        double slDist = MathAbs(entry - sl);
        if (slDist < point * 10) continue;

        // Calculate lot size
        double lots = CalculateLotSize(sym, entry, sl);
        if (lots <= 0) continue;

        // Open trade
        bool opened = OpenTrade(sym, trend, entry, sl, tp, lots);
        if (opened) {
            // Respect MaxPairsOpen after opening
            if (CountOpenPairs() >= MaxPairsOpen) break;
        }
    }
}

//+------------------------------------------------------------------+
//| SECTION 6 — OnTimer()                                           |
//+------------------------------------------------------------------+
void OnTimer() {
    // Poll Telegram commands every 30 seconds
    PollTelegramCommands();

    // 5-minute heartbeat (every 10th 30s tick = 300s)
    g_timerCount++;
    if (g_timerCount >= 10) {
        g_timerCount = 0;
        HeartbeatCheck();
    }

    // Weekly report: Friday 16:00 UTC
    CheckWeeklyReport();

    // Update drawdown
    UpdateDrawdown();
}

//+------------------------------------------------------------------+
//| TREND DETECTION — 4H Higher Highs / Higher Lows                 |
//+------------------------------------------------------------------+
int GetTrend4H(string symbol) {
    int bars = Lookback4H + 1;
    double highs[], lows[];
    ArraySetAsSeries(highs, true);
    ArraySetAsSeries(lows,  true);

    if (CopyHigh(symbol, PERIOD_H4, 0, bars, highs) < bars) return 0;
    if (CopyLow (symbol, PERIOD_H4, 0, bars, lows)  < bars) return 0;

    int bullCount = 0, bearCount = 0;
    for (int i = 0; i < Lookback4H - 1; i++) {
        // Compare bar[i] vs bar[i+1] (i=0 is most recent)
        if (highs[i] > highs[i+1] && lows[i] > lows[i+1]) bullCount++;
        if (highs[i] < highs[i+1] && lows[i] < lows[i+1]) bearCount++;
    }

    int threshold = (Lookback4H - 1) / 2;
    if (bullCount > threshold) return 1;
    if (bearCount > threshold) return -1;
    return 0;
}

//+------------------------------------------------------------------+
//| FIND ORDER BLOCK                                                 |
//| Bullish OB: last bearish candle before a significant up move     |
//| Bearish OB: last bullish candle before a significant down move   |
//+------------------------------------------------------------------+
PriceZone FindOrderBlock(string symbol, ENUM_TIMEFRAMES tf) {
    PriceZone result;
    result.valid = false;

    int bars = LookbackEntry + 3;
    double opens[], closes[], highs[], lows[];
    ArraySetAsSeries(opens,  true);
    ArraySetAsSeries(closes, true);
    ArraySetAsSeries(highs,  true);
    ArraySetAsSeries(lows,   true);
    datetime times[];
    ArraySetAsSeries(times, true);

    if (CopyOpen  (symbol, tf, 0, bars, opens)  < bars) return result;
    if (CopyClose (symbol, tf, 0, bars, closes) < bars) return result;
    if (CopyHigh  (symbol, tf, 0, bars, highs)  < bars) return result;
    if (CopyLow   (symbol, tf, 0, bars, lows)   < bars) return result;
    if (CopyTime  (symbol, tf, 0, bars, times)  < bars) return result;

    double point = SymbolInfoDouble(symbol, SYMBOL_POINT);
    double minMove = 10.0 * point * 10; // minimum impulse to qualify

    // Scan from recent to older
    for (int i = 1; i < LookbackEntry - 1; i++) {
        // Bullish OB: bearish candle[i] followed by strong bullish move candle[i-1]
        bool isBearish_i = (closes[i] < opens[i]);
        bool isBullish_prev = (closes[i-1] > opens[i-1]) &&
                               (closes[i-1] - opens[i-1]) > minMove;
        if (isBearish_i && isBullish_prev) {
            result.valid     = true;
            result.high      = highs[i];
            result.low       = lows[i];
            result.direction = 1;
            result.time      = times[i];
            return result;
        }

        // Bearish OB: bullish candle[i] followed by strong bearish move candle[i-1]
        bool isBullish_i  = (closes[i] > opens[i]);
        bool isBearish_prev = (closes[i-1] < opens[i-1]) &&
                               (opens[i-1] - closes[i-1]) > minMove;
        if (isBullish_i && isBearish_prev) {
            result.valid     = true;
            result.high      = highs[i];
            result.low       = lows[i];
            result.direction = -1;
            result.time      = times[i];
            return result;
        }
    }
    return result;
}

//+------------------------------------------------------------------+
//| FIND FAIR VALUE GAP                                              |
//| Bullish FVG: candle[0].low > candle[2].high                     |
//| Bearish FVG: candle[0].high < candle[2].low                     |
//+------------------------------------------------------------------+
PriceZone FindFVG(string symbol, ENUM_TIMEFRAMES tf) {
    PriceZone result;
    result.valid = false;

    int bars = LookbackEntry + 3;
    double highs[], lows[];
    ArraySetAsSeries(highs, true);
    ArraySetAsSeries(lows,  true);
    datetime times[];
    ArraySetAsSeries(times, true);

    if (CopyHigh(symbol, tf, 0, bars, highs) < bars) return result;
    if (CopyLow (symbol, tf, 0, bars, lows)  < bars) return result;
    if (CopyTime(symbol, tf, 0, bars, times) < bars) return result;

    double point    = SymbolInfoDouble(symbol, SYMBOL_POINT);
    double minSize  = FVGMinPips * point * 10;

    for (int i = 1; i < LookbackEntry - 1; i++) {
        // Bullish FVG: gap between lows[i-1] and highs[i+1]
        if (lows[i-1] > highs[i+1] && (lows[i-1] - highs[i+1]) >= minSize) {
            result.valid     = true;
            result.low       = highs[i+1];
            result.high      = lows[i-1];
            result.direction = 1;
            result.time      = times[i];
            return result;
        }
        // Bearish FVG: gap between highs[i-1] and lows[i+1]
        if (highs[i-1] < lows[i+1] && (lows[i+1] - highs[i-1]) >= minSize) {
            result.valid     = true;
            result.low       = highs[i-1];
            result.high      = lows[i+1];
            result.direction = -1;
            result.time      = times[i];
            return result;
        }
    }
    return result;
}

//+------------------------------------------------------------------+
//| FIND BREAKER BLOCK                                               |
//| A previous OB that price has broken through and is retesting     |
//+------------------------------------------------------------------+
PriceZone FindBreakerBlock(string symbol, ENUM_TIMEFRAMES tf) {
    PriceZone result;
    result.valid = false;

    double currentAsk = SymbolInfoDouble(symbol, SYMBOL_ASK);
    double currentBid = SymbolInfoDouble(symbol, SYMBOL_BID);
    double currentPrice = (currentAsk + currentBid) / 2.0;

    int bars = LookbackEntry * 2 + 3;
    double opens[], closes[], highs[], lows[];
    ArraySetAsSeries(opens,  true);
    ArraySetAsSeries(closes, true);
    ArraySetAsSeries(highs,  true);
    ArraySetAsSeries(lows,   true);
    datetime times[];
    ArraySetAsSeries(times, true);

    if (CopyOpen  (symbol, tf, 0, bars, opens)  < bars) return result;
    if (CopyClose (symbol, tf, 0, bars, closes) < bars) return result;
    if (CopyHigh  (symbol, tf, 0, bars, highs)  < bars) return result;
    if (CopyLow   (symbol, tf, 0, bars, lows)   < bars) return result;
    if (CopyTime  (symbol, tf, 0, bars, times)  < bars) return result;

    double point   = SymbolInfoDouble(symbol, SYMBOL_POINT);
    double minMove = 10.0 * point * 10;

    // Find historical OBs and check if price has broken through them
    for (int i = LookbackEntry; i < bars - 2; i++) {
        // Was it a bullish OB (bearish candle before big bull move)?
        bool isBearish_i     = (closes[i] < opens[i]);
        bool isBullish_prev  = (closes[i-1] > opens[i-1]) &&
                                (closes[i-1] - opens[i-1]) > minMove;

        if (isBearish_i && isBullish_prev) {
            double obHigh = highs[i];
            double obLow  = lows[i];
            // Check if current price has broken BELOW this OB (turning it into bearish breaker)
            if (currentPrice < obLow) {
                // Price retesting from below — bearish breaker
                result.valid     = true;
                result.high      = obHigh;
                result.low       = obLow;
                result.direction = -1;
                result.time      = times[i];
                return result;
            }
        }

        // Was it a bearish OB (bullish candle before big bear move)?
        bool isBullish_i    = (closes[i] > opens[i]);
        bool isBearish_prev = (closes[i-1] < opens[i-1]) &&
                               (opens[i-1] - closes[i-1]) > minMove;

        if (isBullish_i && isBearish_prev) {
            double obHigh = highs[i];
            double obLow  = lows[i];
            // Check if current price has broken ABOVE this OB (turning it into bullish breaker)
            if (currentPrice > obHigh) {
                // Price retesting from above — bullish breaker
                result.valid     = true;
                result.high      = obHigh;
                result.low       = obLow;
                result.direction = 1;
                result.time      = times[i];
                return result;
            }
        }
    }
    return result;
}

//+------------------------------------------------------------------+
//| IsPriceInZone — check if price is inside a POI zone              |
//+------------------------------------------------------------------+
bool IsPriceInZone(double price, PriceZone &zone) {
    return (price >= zone.low && price <= zone.high);
}

//+------------------------------------------------------------------+
//| FIND RECENT RESISTANCE (for TP in uptrend)                       |
//+------------------------------------------------------------------+
double FindRecentResistance(string symbol, ENUM_TIMEFRAMES tf) {
    int bars = 50;
    double highs[];
    ArraySetAsSeries(highs, true);
    if (CopyHigh(symbol, tf, 1, bars, highs) < bars) return 0;

    double currentAsk = SymbolInfoDouble(symbol, SYMBOL_ASK);
    double bestResist = 0;

    for (int i = 0; i < bars - 2; i++) {
        // Swing high: bar[i] higher than neighbours
        if (highs[i] > highs[i+1] && highs[i] > highs[i-1 < 0 ? 0 : i-1]) {
            if (highs[i] > currentAsk && (bestResist == 0 || highs[i] < bestResist)) {
                bestResist = highs[i];
            }
        }
    }
    return bestResist;
}

//+------------------------------------------------------------------+
//| FIND RECENT SUPPORT (for TP in downtrend)                        |
//+------------------------------------------------------------------+
double FindRecentSupport(string symbol, ENUM_TIMEFRAMES tf) {
    int bars = 50;
    double lows[];
    ArraySetAsSeries(lows, true);
    if (CopyLow(symbol, tf, 1, bars, lows) < bars) return 0;

    double currentBid = SymbolInfoDouble(symbol, SYMBOL_BID);
    double bestSupport = 0;

    for (int i = 0; i < bars - 2; i++) {
        // Swing low: bar[i] lower than neighbours
        if (lows[i] < lows[i+1] && lows[i] < lows[i-1 < 0 ? 0 : i-1]) {
            if (lows[i] < currentBid && (bestSupport == 0 || lows[i] > bestSupport)) {
                bestSupport = lows[i];
            }
        }
    }
    return bestSupport;
}

//+------------------------------------------------------------------+
//| IsSessionActive — exclude Sydney (22:00-07:00 UTC) + weekends    |
//+------------------------------------------------------------------+
bool IsSessionActive() {
    MqlDateTime dt;
    TimeToStruct(TimeGMT(), dt);
    int hour = dt.hour;
    int dow  = dt.day_of_week;

    if (dow == 0 || dow == 6) return false; // Sunday=0, Saturday=6
    if (hour >= SessionEndHour || hour < SessionStartHour) return false;
    return true;
}

//+------------------------------------------------------------------+
//| IsNewsWithin1Hour — checks MT5 calendar for high-impact events   |
//+------------------------------------------------------------------+
bool IsNewsWithin1Hour(string symbol) {
    datetime now       = TimeGMT();
    datetime rangeFrom = now - 3600;  // 1h back
    datetime rangeTo   = now + 3600;  // 1h forward

    // Determine currency from symbol
    string cur1 = "", cur2 = "";
    if (symbol == "XAUUSD") { cur1 = "XAU"; cur2 = "USD"; }
    else if (symbol == "BTCUSD") { cur1 = "BTC"; cur2 = "USD"; }
    else if (symbol == "USOIL")  { cur1 = "USD"; cur2 = "USD"; }
    else if (symbol == "GBPUSD") { cur1 = "GBP"; cur2 = "USD"; }
    else {
        cur1 = StringSubstr(symbol, 0, 3);
        cur2 = StringSubstr(symbol, 3, 3);
    }

    MqlCalendarValue values[];
    if (CalendarValueHistory(values, rangeFrom, rangeTo) > 0) {
        for (int i = 0; i < ArraySize(values); i++) {
            MqlCalendarEvent ev;
            if (!CalendarEventById(values[i].event_id, ev)) continue;
            if (ev.importance != CALENDAR_IMPORTANCE_HIGH) continue;

            MqlCalendarCountry country;
            if (!CalendarCountryById(ev.country_id, country)) continue;

            string evCur = country.currency;
            if (evCur == cur1 || evCur == cur2) {
                // If no valid entry was found before the event, skip
                // (We're being conservative — if we're checking now and news
                //  is within 1h either side, block trading)
                return true;
            }
        }
    }
    return false;
}

//+------------------------------------------------------------------+
//| CalculateLotSize — dynamic risk-based position sizing            |
//+------------------------------------------------------------------+
double CalculateLotSize(string symbol, double entry, double sl) {
    double riskPercent = (AccountType == LIVE) ? LiveRiskPercent : PropRiskPercent;
    double balance     = AccountInfoDouble(ACCOUNT_BALANCE);
    double riskAmount  = balance * riskPercent / 100.0;

    double point    = SymbolInfoDouble(symbol, SYMBOL_POINT);
    double slPips   = MathAbs(entry - sl) / point;
    double pipValue = SymbolInfoDouble(symbol, SYMBOL_TRADE_TICK_VALUE);

    if (slPips <= 0 || pipValue <= 0) return 0;

    double lots    = riskAmount / (slPips * pipValue);
    double minLot  = SymbolInfoDouble(symbol, SYMBOL_VOLUME_MIN);
    double maxLot  = SymbolInfoDouble(symbol, SYMBOL_VOLUME_MAX);
    double lotStep = SymbolInfoDouble(symbol, SYMBOL_VOLUME_STEP);

    lots = MathFloor(lots / lotStep) * lotStep;
    return MathMax(minLot, MathMin(maxLot, lots));
}

//+------------------------------------------------------------------+
//| OpenTrade — place a market order with SL/TP                      |
//+------------------------------------------------------------------+
bool OpenTrade(string symbol, int direction, double entry,
               double sl, double tp, double lots) {

    int digits = (int)SymbolInfoInteger(symbol, SYMBOL_DIGITS);
    sl  = NormalizeDouble(sl,  digits);
    tp  = NormalizeDouble(tp,  digits);
    entry = NormalizeDouble(entry, digits);

    bool result = false;
    ulong ticket = 0;

    if (direction == 1) {
        result = trade.Buy(lots, symbol, entry, sl, tp,
                           "4rexbot OB/FVG/BB BUY");
    } else {
        result = trade.Sell(lots, symbol, entry, sl, tp,
                            "4rexbot OB/FVG/BB SELL");
    }

    if (result) {
        ticket = trade.ResultOrder();
        string dir = (direction == 1) ? "BUY" : "SELL";
        string msg = "📈 NEW TRADE\n" +
                     "Symbol: " + symbol + "\n" +
                     "Direction: " + dir + "\n" +
                     "Lots: " + DoubleToString(lots, 2) + "\n" +
                     "Entry: " + DoubleToString(entry, digits) + "\n" +
                     "SL: " + DoubleToString(sl, digits) + "\n" +
                     "TP: " + DoubleToString(tp, digits) + "\n" +
                     "Ticket: #" + IntegerToString((long)ticket);

        SendTelegram(msg);
        if (EmailAddress != "")
            SendMail("4rexbot — New Trade " + symbol, msg);

        // Record trade
        int sz = ArraySize(g_tradeRecords);
        ArrayResize(g_tradeRecords, sz + 1);
        g_tradeRecords[sz].ticket           = ticket;
        g_tradeRecords[sz].originalLots     = lots;
        g_tradeRecords[sz].entryPrice       = entry;
        g_tradeRecords[sz].slPrice          = sl;
        g_tradeRecords[sz].tpPrice          = tp;
        g_tradeRecords[sz].breakEvenDone    = false;
        g_tradeRecords[sz].partialCloseDone = false;
        g_tradeRecords[sz].symbol           = symbol;

        g_totalTrades++;
        SaveStats();
    }
    return result;
}

//+------------------------------------------------------------------+
//| ManageOpenTrades — trailing stop + partial close logic           |
//+------------------------------------------------------------------+
void ManageOpenTrades() {
    for (int i = PositionsTotal() - 1; i >= 0; i--) {
        if (!posInfo.SelectByIndex(i)) continue;
        if (posInfo.Magic() != MagicNumber) continue;

        ulong  ticket     = posInfo.Ticket();
        string sym        = posInfo.Symbol();
        double openPrice  = posInfo.PriceOpen();
        double currentSL  = posInfo.StopLoss();
        double currentTP  = posInfo.TakeProfit();
        double currentBid = SymbolInfoDouble(sym, SYMBOL_BID);
        double currentAsk = SymbolInfoDouble(sym, SYMBOL_ASK);
        ENUM_POSITION_TYPE posType = posInfo.PositionType();

        // Find our record
        int recIdx = -1;
        for (int r = 0; r < ArraySize(g_tradeRecords); r++) {
            if (g_tradeRecords[r].ticket == ticket) {
                recIdx = r;
                break;
            }
        }
        if (recIdx < 0) continue;

        double entryPrice   = g_tradeRecords[recIdx].entryPrice;
        double slPrice      = g_tradeRecords[recIdx].slPrice;
        double riskDist     = MathAbs(entryPrice - slPrice);
        if (riskDist <= 0) continue;

        double currentPrice = (posType == POSITION_TYPE_BUY) ? currentBid : currentAsk;
        double profit       = (posType == POSITION_TYPE_BUY)
                              ? (currentPrice - entryPrice)
                              : (entryPrice - currentPrice);
        double rr           = (riskDist > 0) ? profit / riskDist : 0;

        // Move SL to breakeven at 2:1 R:R
        if (rr >= BreakevenRR && !g_tradeRecords[recIdx].breakEvenDone) {
            int digits = (int)SymbolInfoInteger(sym, SYMBOL_DIGITS);
            double newSL = NormalizeDouble(entryPrice, digits);
            if (posType == POSITION_TYPE_BUY && newSL > currentSL) {
                if (trade.PositionModify(ticket, newSL, currentTP)) {
                    g_tradeRecords[recIdx].breakEvenDone = true;
                    Print("Breakeven set for #", ticket, " on ", sym);
                }
            } else if (posType == POSITION_TYPE_SELL && (currentSL == 0 || newSL < currentSL)) {
                if (trade.PositionModify(ticket, newSL, currentTP)) {
                    g_tradeRecords[recIdx].breakEvenDone = true;
                    Print("Breakeven set for #", ticket, " on ", sym);
                }
            }
        }

        // Close 50% at 3:1 R:R
        if (rr >= PartialCloseRR && !g_tradeRecords[recIdx].partialCloseDone) {
            double currentVolume = posInfo.Volume();
            double halfLots = NormalizeDouble(currentVolume / 2.0,
                              (int)MathLog10(1.0 / SymbolInfoDouble(sym, SYMBOL_VOLUME_STEP)));
            double minLot = SymbolInfoDouble(sym, SYMBOL_VOLUME_MIN);
            if (halfLots >= minLot) {
                if (trade.PositionClosePartial(ticket, halfLots)) {
                    g_tradeRecords[recIdx].partialCloseDone = true;
                    Print("Partial close (50%) for #", ticket, " on ", sym);
                    SendTelegram("📊 PARTIAL CLOSE (50%) — #" + IntegerToString((long)ticket) +
                                 " | " + sym + " | R:R reached " +
                                 DoubleToString(rr, 1) + ":1");
                }
            }
        }
    }

    // Detect closed trades and update stats
    CheckClosedTrades();
}

//+------------------------------------------------------------------+
//| CheckClosedTrades — scan history for newly closed positions      |
//+------------------------------------------------------------------+
void CheckClosedTrades() {
    int total = ArraySize(g_tradeRecords);
    for (int r = total - 1; r >= 0; r--) {
        ulong ticket = g_tradeRecords[r].ticket;
        if (!posInfo.SelectByTicket(ticket)) {
            // Position no longer open — check history
            if (HistoryDealSelect(ticket)) {
                double profit = HistoryDealGetDouble(ticket, DEAL_PROFIT);
                string sym    = g_tradeRecords[r].symbol;
                int digits    = (int)SymbolInfoInteger(sym, SYMBOL_DIGITS);
                double entryPr = g_tradeRecords[r].entryPrice;
                double closePr = HistoryDealGetDouble(ticket, DEAL_PRICE);
                double pips   = MathAbs(closePr - entryPr) /
                                SymbolInfoDouble(sym, SYMBOL_POINT) / 10.0;

                if (profit > 0) {
                    g_winTrades++;
                    g_totalPips += pips;
                } else {
                    g_lossTrades++;
                    g_totalPips -= pips;
                }

                string result_str = (profit > 0) ? "✅ WIN" : "❌ LOSS";
                string msg = "🔒 TRADE CLOSED\n" +
                             "Ticket: #" + IntegerToString((long)ticket) + "\n" +
                             "Symbol: " + sym + "\n" +
                             "Result: " + result_str + "\n" +
                             "Profit: " + DoubleToString(profit, 2) + " " +
                             AccountInfoString(ACCOUNT_CURRENCY) + "\n" +
                             "Pips: " + DoubleToString(pips, 1);

                SendTelegram(msg);
                if (EmailAddress != "")
                    SendMail("4rexbot — Trade Closed " + sym, msg);

                SaveStats();

                // Remove from records
                ArrayRemove(g_tradeRecords, r, 1);
            }
        }
    }
}

//+------------------------------------------------------------------+
//| CountOpenPairs — how many unique symbols have open positions      |
//+------------------------------------------------------------------+
int CountOpenPairs() {
    string openSymbols[];
    int count = 0;
    for (int i = PositionsTotal() - 1; i >= 0; i--) {
        if (!posInfo.SelectByIndex(i)) continue;
        if (posInfo.Magic() != MagicNumber) continue;
        string sym = posInfo.Symbol();
        bool found = false;
        for (int j = 0; j < count; j++) {
            if (openSymbols[j] == sym) { found = true; break; }
        }
        if (!found) {
            ArrayResize(openSymbols, count + 1);
            openSymbols[count] = sym;
            count++;
        }
    }
    return count;
}

//+------------------------------------------------------------------+
//| CountTradesForSymbol                                             |
//+------------------------------------------------------------------+
int CountTradesForSymbol(string symbol) {
    int count = 0;
    for (int i = PositionsTotal() - 1; i >= 0; i--) {
        if (!posInfo.SelectByIndex(i)) continue;
        if (posInfo.Magic() != MagicNumber) continue;
        if (posInfo.Symbol() == symbol) count++;
    }
    return count;
}

//+------------------------------------------------------------------+
//| UpdateDrawdown                                                   |
//+------------------------------------------------------------------+
void UpdateDrawdown() {
    double balance = AccountInfoDouble(ACCOUNT_BALANCE);
    if (balance > g_peakBalance) g_peakBalance = balance;
    if (g_peakBalance > 0) {
        double dd = (g_peakBalance - balance) / g_peakBalance * 100.0;
        if (dd > g_maxDrawdown) g_maxDrawdown = dd;
    }
}

//+------------------------------------------------------------------+
//| HeartbeatCheck — 5-min connectivity/state check                  |
//+------------------------------------------------------------------+
void HeartbeatCheck() {
    if (!TerminalInfoInteger(TERMINAL_CONNECTED)) {
        Print("WARNING: Terminal not connected. Waiting for reconnect...");
    }
    UpdateDrawdown();
}

//+------------------------------------------------------------------+
//| CheckWeeklyReport — Friday 16:00 UTC                             |
//+------------------------------------------------------------------+
void CheckWeeklyReport() {
    MqlDateTime dt;
    TimeToStruct(TimeGMT(), dt);

    // Friday = day_of_week 5
    if (dt.day_of_week == 5 && dt.hour == 16 && dt.min < 1) {
        datetime now = TimeGMT();
        // Avoid sending more than once per hour
        if (now - g_lastWeeklyReport > 3600) {
            g_lastWeeklyReport = now;
            string report = GenerateWeeklyReport();
            SendTelegram(report);
            if (EmailAddress != "")
                SendMail("4rexbot — Weekly Report", report);
            // Reset weekly stats
            ResetWeeklyStats();
        }
    }
}

//+------------------------------------------------------------------+
//| GenerateWeeklyReport                                             |
//+------------------------------------------------------------------+
string GenerateWeeklyReport() {
    int    total   = g_totalTrades;
    int    wins    = g_winTrades;
    int    losses  = g_lossTrades;
    double winRate = (total > 0) ? (double)wins / total * 100.0 : 0.0;
    double balance = AccountInfoDouble(ACCOUNT_BALANCE);
    double equity  = AccountInfoDouble(ACCOUNT_EQUITY);
    string currency = AccountInfoString(ACCOUNT_CURRENCY);

    string report =
        "📊 WEEKLY REPORT — 4rexbot\n" +
        "================================\n" +
        "Period: " + TimeToString(g_weekStart) + " → " + TimeToString(TimeGMT()) + "\n\n" +
        "Total Trades: " + IntegerToString(total) + "\n" +
        "Wins: " + IntegerToString(wins) + "\n" +
        "Losses: " + IntegerToString(losses) + "\n" +
        "Win Rate: " + DoubleToString(winRate, 1) + "%\n" +
        "Total Pips: " + DoubleToString(g_totalPips, 1) + "\n" +
        "Max Drawdown: " + DoubleToString(g_maxDrawdown, 2) + "%\n\n" +
        "Balance: " + DoubleToString(balance, 2) + " " + currency + "\n" +
        "Equity: " + DoubleToString(equity, 2) + " " + currency;

    return report;
}

//+------------------------------------------------------------------+
//| ResetWeeklyStats                                                 |
//+------------------------------------------------------------------+
void ResetWeeklyStats() {
    g_totalTrades = 0;
    g_winTrades   = 0;
    g_lossTrades  = 0;
    g_totalPips   = 0.0;
    g_maxDrawdown = 0.0;
    g_peakBalance = AccountInfoDouble(ACCOUNT_BALANCE);
    SetWeekStart();
    SaveStats();
}

//+------------------------------------------------------------------+
//| SetWeekStart — set to most recent Monday 00:00                   |
//+------------------------------------------------------------------+
void SetWeekStart() {
    MqlDateTime dt;
    TimeToStruct(TimeGMT(), dt);
    int daysFromMon = dt.day_of_week == 0 ? 6 : dt.day_of_week - 1;
    g_weekStart = TimeGMT() - daysFromMon * 86400 -
                  dt.hour * 3600 - dt.min * 60 - dt.sec;
}

//+------------------------------------------------------------------+
//| SaveStats — write stats to CSV                                   |
//+------------------------------------------------------------------+
void SaveStats() {
    int fileHandle = FileOpen("4rexbot_stats.csv",
                              FILE_WRITE | FILE_CSV | FILE_ANSI);
    if (fileHandle == INVALID_HANDLE) {
        Print("Failed to open 4rexbot_stats.csv for writing");
        return;
    }
    FileWrite(fileHandle, "TotalTrades", "Wins", "Losses", "WinRate",
              "TotalPips", "MaxDrawdown", "WeekStart");
    double winRate = (g_totalTrades > 0) ?
                     (double)g_winTrades / g_totalTrades * 100.0 : 0.0;
    FileWrite(fileHandle,
              IntegerToString(g_totalTrades),
              IntegerToString(g_winTrades),
              IntegerToString(g_lossTrades),
              DoubleToString(winRate, 2),
              DoubleToString(g_totalPips, 1),
              DoubleToString(g_maxDrawdown, 2),
              TimeToString(g_weekStart));
    FileClose(fileHandle);
}

//+------------------------------------------------------------------+
//| LoadStats — read stats from CSV                                  |
//+------------------------------------------------------------------+
void LoadStats() {
    if (!FileIsExist("4rexbot_stats.csv")) return;

    int fileHandle = FileOpen("4rexbot_stats.csv",
                              FILE_READ | FILE_CSV | FILE_ANSI);
    if (fileHandle == INVALID_HANDLE) return;

    // Skip header row
    if (!FileIsEnding(fileHandle)) {
        string h1 = FileReadString(fileHandle);
        string h2 = FileReadString(fileHandle);
        string h3 = FileReadString(fileHandle);
        string h4 = FileReadString(fileHandle);
        string h5 = FileReadString(fileHandle);
        string h6 = FileReadString(fileHandle);
        string h7 = FileReadString(fileHandle);
    }

    if (!FileIsEnding(fileHandle)) {
        g_totalTrades = (int)StringToInteger(FileReadString(fileHandle));
        g_winTrades   = (int)StringToInteger(FileReadString(fileHandle));
        g_lossTrades  = (int)StringToInteger(FileReadString(fileHandle));
        // Skip win rate (calculated)
        FileReadString(fileHandle);
        g_totalPips   = StringToDouble(FileReadString(fileHandle));
        g_maxDrawdown = StringToDouble(FileReadString(fileHandle));
        string wsStr  = FileReadString(fileHandle);
        g_weekStart   = StringToTime(wsStr);
    }

    FileClose(fileHandle);
    Print("Stats loaded: Trades=", g_totalTrades, " Wins=", g_winTrades,
          " Losses=", g_lossTrades);
}

//+------------------------------------------------------------------+
//| SendTelegram — send message via Telegram Bot API                 |
//+------------------------------------------------------------------+
void SendTelegram(string msg) {
    if (TelegramBotToken == "" || TelegramChatID == "") return;

    // Escape backslashes and quotes in msg
    StringReplace(msg, "\\", "\\\\");
    StringReplace(msg, "\"", "\\\"");
    StringReplace(msg, "\n", "\\n");

    string url  = "https://api.telegram.org/bot" + TelegramBotToken + "/sendMessage";
    string body = "{\"chat_id\":\"" + TelegramChatID +
                  "\",\"text\":\"" + msg +
                  "\",\"parse_mode\":\"HTML\"}";

    char   data[], result[];
    string headers = "Content-Type: application/json\r\n";
    int    resCode  = 0;

    StringToCharArray(body, data, 0, StringLen(body));

    string responseHeaders;
    resCode = WebRequest("POST", url, headers, 5000,
                         data, result, responseHeaders);

    if (resCode != 200) {
        Print("Telegram send failed. HTTP: ", resCode);
    }
}

//+------------------------------------------------------------------+
//| PollTelegramCommands — check getUpdates every 30 seconds         |
//+------------------------------------------------------------------+
void PollTelegramCommands() {
    if (TelegramBotToken == "" || TelegramChatID == "") return;

    string url = "https://api.telegram.org/bot" + TelegramBotToken +
                 "/getUpdates?offset=" + IntegerToString(g_lastUpdateId + 1) +
                 "&limit=10&timeout=0";

    char   data[], result[];
    string responseHeaders;
    string headers = "";

    int resCode = WebRequest("GET", url, headers, 5000,
                             data, result, responseHeaders);
    if (resCode != 200) return;

    string response = CharArrayToString(result);
    ParseTelegramUpdates(response);
}

//+------------------------------------------------------------------+
//| ParseTelegramUpdates — minimal JSON parser for commands          |
//+------------------------------------------------------------------+
void ParseTelegramUpdates(string json) {
    // Find each "update_id" occurrence
    int pos = 0;
    while (true) {
        int uidPos = StringFind(json, "\"update_id\":", pos);
        if (uidPos < 0) break;

        // Extract update_id
        int numStart = uidPos + 12;
        int numEnd   = StringFind(json, ",", numStart);
        if (numEnd < 0) break;
        long updateId = StringToInteger(StringSubstr(json, numStart, numEnd - numStart));
        if (updateId > g_lastUpdateId) g_lastUpdateId = updateId;

        // Extract chat_id from this update
        int chatPos = StringFind(json, "\"id\":", uidPos);
        if (chatPos < 0) { pos = numEnd; continue; }
        int chatStart = chatPos + 5;
        int chatEnd   = StringFind(json, ",", chatStart);
        if (chatEnd < 0) { pos = numEnd; continue; }
        string chatId = StringSubstr(json, chatStart, chatEnd - chatStart);
        StringTrimRight(chatId);
        StringTrimLeft(chatId);

        // Only accept commands from our configured chat
        if (chatId != TelegramChatID) { pos = numEnd; continue; }

        // Extract text
        int textPos = StringFind(json, "\"text\":\"", uidPos);
        if (textPos < 0) { pos = numEnd; continue; }
        int textStart = textPos + 8;
        int textEnd   = StringFind(json, "\"", textStart);
        if (textEnd < 0) { pos = numEnd; continue; }
        string text = StringSubstr(json, textStart, textEnd - textStart);

        // Process commands
        if (text == "/pause") {
            g_tradingPaused = true;
            SendTelegram("⏸ 4rexbot paused. No new trades will be opened.");
            Print("EA paused via Telegram command.");
        } else if (text == "/resume") {
            g_tradingPaused = false;
            SendTelegram("▶️ 4rexbot resumed. Trading is active.");
            Print("EA resumed via Telegram command.");
        } else if (text == "/status") {
            string status = "📊 4rexbot Status\n" +
                "Mode: " + (AccountType == LIVE ? "LIVE" : "PROP") + "\n" +
                "Paused: " + (g_tradingPaused ? "YES" : "NO") + "\n" +
                "Session Active: " + (IsSessionActive() ? "YES" : "NO") + "\n" +
                "Open Positions: " + IntegerToString(PositionsTotal()) + "\n" +
                "Balance: " + DoubleToString(AccountInfoDouble(ACCOUNT_BALANCE), 2) + "\n" +
                "Equity: " + DoubleToString(AccountInfoDouble(ACCOUNT_EQUITY), 2);
            SendTelegram(status);
        } else if (text == "/report") {
            SendTelegram(GenerateWeeklyReport());
        }

        pos = numEnd;
    }
}

//+------------------------------------------------------------------+
//| END OF 4rexbot.mq5                                              |
//+------------------------------------------------------------------+
