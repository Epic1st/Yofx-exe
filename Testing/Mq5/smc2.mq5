//+------------------------------------------------------------------+
//|                                  TITAN_SMC_GRID_MARTINGALE.mq5   |
//|                                      Copyright 2026, UZ_AlgoDev  |
//|                                  Channel: t.me/UZ_AlgoDev        |
//+------------------------------------------------------------------+
#property strict
#property copyright "UZ_AlgoDev"
#property link      "https://t.me/UZ_AlgoDev"
#property version   "3.10"
#property description "SMC Trap (Wick Rejection) + Grid Martingale Recovery"

#include <Trade\Trade.mqh>

CTrade trade;

//====================================================================
// SOZLAMALAR (INPUTS)
//====================================================================
input group "=== 1. SMC REAL ZONE (HTF KIRISH) ==="
input ENUM_TIMEFRAMES InpHTF           = PERIOD_H4;       // Katta Taymfreym
input int             InpLookbackBars  = 40;              // Qancha sham orqani tekshirish kerak?
input int             InpZoneThickness = 800;             // Zonaning qalinligi (800 punkt = 80 pip)
input int             InpSpreadBuffer  = 400;             // Spred uchun bufer (Zonadan sal ertaroq/kechroq kirish)
input double          InpWickToBodyRatio = 2.0;           // ⚡ SOYA TANADAN NECHA MARTA UZUN BO'LISHI KERAK? (Masalan 2.0)

input group "=== 2. GRID VA MARTINGALE (QUTQARUV TIZIMI) ==="
input double          InpLotInitial    = 0.01;            // Boshlang'ich Lot
input double          InpMartingaleMult= 1.5;             // Martingale ko'paytuvchisi (Masalan 1.5 barobar)
input int             InpGridDistance  = 1500;            // KATTA SPRED UCHUN QADAM (150 pip)
input int             InpMaxGridOrders = 10;              // Maksimal setka o'rderlar soni

input group "=== 3. FOYDA VA XAVFSIZLIK ==="
input int             InpTakeProfit    = 1500;            // O'rtacha narxdan TP (150 pip)
input double          InpMaxLossPercent= 30.0;            // Umumiy zararni cheklash (30% minusga kirsa yopish)
input int             InpMagicNumber   = 7777;            

// Global O'zgaruvchilar
double HTF_Supply = 0.0;
double HTF_Demand = 0.0;

//====================================================================
// ON INIT
//====================================================================
int OnInit() {
    Print("🚀 TITAN SMC WICK REJECTION + GRID ishga tushdi!");
    trade.SetExpertMagicNumber(InpMagicNumber);
    return(INIT_SUCCEEDED);
}

//====================================================================
// ON TICK
//====================================================================
void OnTick() {
    // XAVFSIZLIK: Equity Lock (Qattiq Prasadkadan himoya)
    double equity = AccountInfoDouble(ACCOUNT_EQUITY);
    double balance = AccountInfoDouble(ACCOUNT_BALANCE);
    if(equity <= balance * (1.0 - InpMaxLossPercent / 100.0)) {
        CloseAllPositions(); 
        Print("🛑 Max Loss Limit ishladi! Barcha o'rderlar yopildi.");
        return;
    }

    // 1. KATTA ZONALARNI ANIQLASH
    UpdateHTFZones();
    
    int buyCount = 0, sellCount = 0;
    double lastBuyPrice = 0, lastSellPrice = 0;
    
    // Ochiq o'rderlarni sanash va oxirgi narxlarni aniqlash
    for(int i=PositionsTotal()-1; i>=0; i--) {
        ulong ticket = PositionGetTicket(i);
        if(PositionSelectByTicket(ticket) && PositionGetInteger(POSITION_MAGIC) == InpMagicNumber) {
            double openPrice = PositionGetDouble(POSITION_PRICE_OPEN);
            if(PositionGetInteger(POSITION_TYPE) == POSITION_TYPE_BUY) {
                buyCount++;
                if(lastBuyPrice == 0 || openPrice < lastBuyPrice) lastBuyPrice = openPrice; // Eng pastki buy
            } else {
                sellCount++;
                if(lastSellPrice == 0 || openPrice > lastSellPrice) lastSellPrice = openPrice; // Eng tepadagi sell
            }
        }
    }

    // 2. BIRINCHI O'RDERNI OCHISH (Faqat SMC bo'yicha)
    if(buyCount == 0 && sellCount == 0) {
        CheckForSMCEntry();
    }
    
    // 3. SETKA (GRID) VA MARTINGALE LOGIKASI
    if(buyCount > 0 || sellCount > 0) {
        ManageGrid(buyCount, sellCount, lastBuyPrice, lastSellPrice);
    }
    
    UpdateDashboard(buyCount, sellCount);
}

//====================================================================
// KATTA ZONALARNI ANIQLASH (REAL ZONE H4/H1)
//====================================================================
void UpdateHTFZones() {
    double highArray[], lowArray[];
    
    if(CopyHigh(_Symbol, InpHTF, 1, InpLookbackBars, highArray) > 0 && 
       CopyLow(_Symbol, InpHTF, 1, InpLookbackBars, lowArray) > 0) {
        HTF_Supply = highArray[ArrayMaximum(highArray)];
        HTF_Demand = lowArray[ArrayMinimum(lowArray)];
    }
}

//====================================================================
// QOPQON VA TASDIQNI TEKSHIRISH (SMC WICK REJECTION ENTRY)
//====================================================================
void CheckForSMCEntry() {
    // XAVFSIZLIK: Bitta shamda qayta-qayta o'rder ochmaslik uchun (Muhim!)
    static datetime lastEntryBar = 0;
    datetime currentBar = iTime(_Symbol, PERIOD_CURRENT, 0);
    if(currentBar == lastEntryBar) return; 

    double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
    double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
    
    double c[], o[], h[], l[];
    if(CopyClose(_Symbol, PERIOD_CURRENT, 1, 2, c) <= 0) return;
    CopyOpen(_Symbol, PERIOD_CURRENT, 1, 2, o);
    CopyHigh(_Symbol, PERIOD_CURRENT, 1, 2, h);
    CopyLow(_Symbol, PERIOD_CURRENT, 1, 2, l);

    // --- SHAM ANATOMIYASINI HISOBLASH (Oxirgi yopilgan sham - index 1) ---
    double body = MathAbs(c[1] - o[1]); // Tananing qalinligi
    double upperWick = h[1] - MathMax(c[1], o[1]); // Tepadagi soya uzunligi
    double lowerWick = MathMin(c[1], o[1]) - l[1]; // Pastdagi soya uzunligi
    
    // --- SMART SELL (Supply Zonasida Rejection) ---
    if(h[1] >= (HTF_Supply - InpZoneThickness * _Point) && h[1] <= (HTF_Supply + InpSpreadBuffer * _Point)) {
        // Shart: Tepadagi soya tanadan kamida "InpWickToBodyRatio" marta katta va sham qizil yopilgan bo'lishi kerak
        if(upperWick > (body * InpWickToBodyRatio) && c[1] < o[1]) { 
            if(trade.Sell(InpLotInitial, _Symbol, bid, 0, 0, "SMC Sell Start")) {
                lastEntryBar = currentBar; // O'rder ochilgach shamni eslab qolamiz
                Sleep(200);
                UpdateAverageTP(POSITION_TYPE_SELL);
            }
        }
    }
    
    // --- SMART BUY (Demand Zonasida Rejection) ---
    if(l[1] <= (HTF_Demand + InpZoneThickness * _Point) && l[1] >= (HTF_Demand - InpSpreadBuffer * _Point)) {
        // Shart: Pastki soya tanadan kamida "InpWickToBodyRatio" marta katta va sham yashil yopilgan bo'lishi kerak
        if(lowerWick > (body * InpWickToBodyRatio) && c[1] > o[1]) { 
            if(trade.Buy(InpLotInitial, _Symbol, ask, 0, 0, "SMC Buy Start")) {
                lastEntryBar = currentBar; // O'rder ochilgach shamni eslab qolamiz
                Sleep(200);
                UpdateAverageTP(POSITION_TYPE_BUY);
            }
        }
    }
}

//====================================================================
// GRID VA MARTINGALE (XATONI TO'G'RILASH TIZIMI)
//====================================================================
void ManageGrid(int buyCount, int sellCount, double lastBuyPrice, double lastSellPrice) {
    double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
    double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
    bool isNewOrderOpened = false;

    // BUY GRID (Narx tushib ketganda qo'shimcha o'rder ochish)
    if(buyCount > 0 && buyCount < InpMaxGridOrders) {
        if(ask <= lastBuyPrice - InpGridDistance * _Point) {
            double nextLot = NormalizeDouble(InpLotInitial * MathPow(InpMartingaleMult, buyCount), 2);
            if(trade.Buy(nextLot, _Symbol, ask, 0, 0, "Grid Buy")) {
                isNewOrderOpened = true;
                Sleep(200);
            }
        }
    }

    // SELL GRID (Narx ko'tarilib ketganda qo'shimcha o'rder ochish)
    if(sellCount > 0 && sellCount < InpMaxGridOrders) {
        if(bid >= lastSellPrice + InpGridDistance * _Point) {
            double nextLot = NormalizeDouble(InpLotInitial * MathPow(InpMartingaleMult, sellCount), 2);
            if(trade.Sell(nextLot, _Symbol, bid, 0, 0, "Grid Sell")) {
                isNewOrderOpened = true;
                Sleep(200);
            }
        }
    }

    // Agar yangi o'rder ochilgan bo'lsa, o'rtacha TP ni yangilaymiz
    if(isNewOrderOpened) {
        if(buyCount > 0) UpdateAverageTP(POSITION_TYPE_BUY);
        if(sellCount > 0) UpdateAverageTP(POSITION_TYPE_SELL);
    }
}

//====================================================================
// O'RTACHA TAKE PROFITNI HISOBLASH (BASKET TP)
//====================================================================
void UpdateAverageTP(ENUM_POSITION_TYPE type) {
    double totalLot = 0;
    double totalWeightPrice = 0;
    
    for(int i=PositionsTotal()-1; i>=0; i--) {
        ulong ticket = PositionGetTicket(i);
        if(PositionSelectByTicket(ticket) && PositionGetInteger(POSITION_MAGIC) == InpMagicNumber && PositionGetInteger(POSITION_TYPE) == type) {
            double lot = PositionGetDouble(POSITION_VOLUME);
            double openPrice = PositionGetDouble(POSITION_PRICE_OPEN);
            totalLot += lot;
            totalWeightPrice += openPrice * lot;
        }
    }
    
    if(totalLot > 0) {
        double avgPrice = totalWeightPrice / totalLot;
        double newTP = 0;
        
        if(type == POSITION_TYPE_BUY) newTP = NormalizeDouble(avgPrice + InpTakeProfit * _Point, _Digits);
        if(type == POSITION_TYPE_SELL) newTP = NormalizeDouble(avgPrice - InpTakeProfit * _Point, _Digits);
        
        // Hamma o'rderlarning TP sini yangi hisoblangan o'rtacha TP ga to'g'rilash
        for(int i=PositionsTotal()-1; i>=0; i--) {
            ulong ticket = PositionGetTicket(i);
            if(PositionSelectByTicket(ticket) && PositionGetInteger(POSITION_MAGIC) == InpMagicNumber && PositionGetInteger(POSITION_TYPE) == type) {
                double currentTP = PositionGetDouble(POSITION_TP);
                if(MathAbs(currentTP - newTP) > _Point) {
                    trade.PositionModify(ticket, PositionGetDouble(POSITION_SL), newTP);
                }
            }
        }
    }
}

//====================================================================
// BARCHA O'RDERLARNI YOPISH
//====================================================================
void CloseAllPositions() {
    for(int i=PositionsTotal()-1; i>=0; i--) {
        ulong ticket = PositionGetTicket(i);
        if(PositionSelectByTicket(ticket) && PositionGetInteger(POSITION_MAGIC) == InpMagicNumber) {
            trade.PositionClose(ticket);
        }
    }
}

//====================================================================
// EKRAN MA'LUMOTLARI
//====================================================================
void UpdateDashboard(int buyCount, int sellCount) {
    string status = (buyCount > 0 || sellCount > 0) ? "🎯 GRID ISHLAMOQDA" : "⏳ SMC QOPQONNI KUTMOQDA";
    double bal = AccountInfoDouble(ACCOUNT_BALANCE);
    double eq = AccountInfoDouble(ACCOUNT_EQUITY);
    double dd = ((bal - eq) / bal) * 100.0;
    
    Comment("========================================\n",
            "   🔭 TITAN SMC WICK REJECTION + GRID\n",
            "========================================\n",
            "   Holat: ", status, "\n",
            "   HTF Supply (Sotish Zonasi): ", DoubleToString(HTF_Supply, 2), "\n",
            "   HTF Demand (Olish Zonasi): ", DoubleToString(HTF_Demand, 2), "\n",
            "   Ochiq O'rderlar: ", buyCount + sellCount, " ta\n",
            "   Prasadka: ", DoubleToString(dd, 2), "%\n",
            "========================================");
}