//+------------------------------------------------------------------+
//|         TradeHistoryExporter_v3_Excel_IT_MT5.mq5                |
//+------------------------------------------------------------------+
#property strict
#property script_show_inputs

input string   OutputFileName = "TradeHistory.csv";
input string   OnlySymbol     = "";
input int      MagicFilter    = -1;
input bool     UseSemicolon   = true;
input datetime StartDate      = D'2026.01.01 00:00';   // inizio intervallo
input datetime EndDate        = 0;                     // 0 = fino ad adesso

// --- NUMERI con virgola (Excel IT)
string D(double value)
{
   string s = DoubleToString(value, 2);
   StringReplace(s, ".", ",");
   return s;
}

// --- DATA formato italiano GG/MM/AAAA
string FormatDate(datetime t)
{
   MqlDateTime dt;
   TimeToStruct(t, dt);
   return StringFormat("%02d/%02d/%04d", dt.day, dt.mon, dt.year);
}

// --- ORA Excel-friendly (solo ora)
string FormatTime(datetime t)
{
   MqlDateTime dt;
   TimeToStruct(t, dt);
   return StringFormat("%02d:%02d", dt.hour, dt.min);
}

// --- Raccoglie i PositionID unici presenti nello storico deals selezionato
int CollectPositionIDs(long &posIDs[])
{
   int total = HistoryDealsTotal();
   int count = 0;
   ArrayResize(posIDs, 0);

   for(int i = 0; i < total; i++)
   {
      ulong dealTicket = HistoryDealGetTicket(i);
      if(dealTicket == 0) continue;

      long posID = HistoryDealGetInteger(dealTicket, DEAL_POSITION_ID);
      if(posID == 0) continue;

      bool found = false;
      for(int j = 0; j < count; j++)
      {
         if(posIDs[j] == posID) { found = true; break; }
      }
      if(!found)
      {
         ArrayResize(posIDs, count + 1);
         posIDs[count] = posID;
         count++;
      }
   }
   return count;
}

int OnStart()
{
   datetime endSel = (EndDate == 0) ? TimeCurrent() : EndDate;

   if(!HistorySelect(StartDate, endSel))
   {
      Print("Errore HistorySelect: ", GetLastError());
      return 0;
   }

   int handle = FileOpen(OutputFileName, FILE_WRITE | FILE_TXT | FILE_ANSI);
   if(handle == INVALID_HANDLE)
   {
      Print("Errore apertura file: ", GetLastError());
      return 0;
   }

   string sep = UseSemicolon ? ";" : ",";

   // --- META
   FileWriteString(handle, "Broker" + sep + AccountInfoString(ACCOUNT_SERVER) + "\r\n");
   FileWriteString(handle, "Account" + sep + IntegerToString((int)AccountInfoInteger(ACCOUNT_LOGIN)) + "\r\n");
   FileWriteString(handle, "Currency" + sep + AccountInfoString(ACCOUNT_CURRENCY) + "\r\n");
   FileWriteString(handle, "ExportDate" + sep + FormatDate(TimeCurrent()) + " " + FormatTime(TimeCurrent()) + "\r\n");
   FileWriteString(handle, "RangeFrom" + sep + FormatDate(StartDate) + " " + FormatTime(StartDate) + "\r\n");
   FileWriteString(handle, "RangeTo" + sep + FormatDate(endSel) + " " + FormatTime(endSel) + "\r\n");
   FileWriteString(handle, "\r\n");

   // --- HEADER
   FileWriteString(handle,
      "Ticket" + sep + "Symbol" + sep + "Type" + sep + "Lots" + sep +
      "OpenDate" + sep + "OpenTime" + sep + "OpenPrice" + sep +
      "CloseDate" + sep + "CloseTime" + sep + "ClosePrice" + sep +
      "SL" + sep + "TP" + sep +
      "Profit" + sep + "Commission" + sep + "Swap" + sep +
      "NetProfit" + sep + "Magic" + sep + "Comment" + sep +
      "DurationMin\r\n"
   );

   long posIDs[];
   int posCount = CollectPositionIDs(posIDs);
   int exported = 0;

   for(int p = 0; p < posCount; p++)
   {
      long posID = posIDs[p];
      if(!HistorySelectByPosition(posID))
         continue;

      int dealsInPos = HistoryDealsTotal();
      if(dealsInPos <= 0) continue;

      datetime openTime = 0, closeTime = 0;
      double   openPrice = 0, closePrice = 0;
      double   lots = 0;
      double   sumProfit = 0, sumCommission = 0, sumSwap = 0;
      string   symbol = "";
      long     magic = 0;
      int      dealType = -1;
      string   comment = "";
      ulong    entryOrderTicket = 0;
      bool     hasOut = false;

      for(int d = 0; d < dealsInPos; d++)
      {
         ulong dealTicket = HistoryDealGetTicket(d);
         if(dealTicket == 0) continue;

         long entry = HistoryDealGetInteger(dealTicket, DEAL_ENTRY);

         sumProfit     += HistoryDealGetDouble(dealTicket, DEAL_PROFIT);
         sumCommission += HistoryDealGetDouble(dealTicket, DEAL_COMMISSION);
         sumSwap       += HistoryDealGetDouble(dealTicket, DEAL_SWAP);

         if(entry == DEAL_ENTRY_IN)
         {
            openTime         = (datetime)HistoryDealGetInteger(dealTicket, DEAL_TIME);
            openPrice        = HistoryDealGetDouble(dealTicket, DEAL_PRICE);
            lots             = HistoryDealGetDouble(dealTicket, DEAL_VOLUME);
            symbol           = HistoryDealGetString(dealTicket, DEAL_SYMBOL);
            magic            = HistoryDealGetInteger(dealTicket, DEAL_MAGIC);
            dealType         = (int)HistoryDealGetInteger(dealTicket, DEAL_TYPE);
            entryOrderTicket = HistoryDealGetInteger(dealTicket, DEAL_ORDER);
         }
         else if(entry == DEAL_ENTRY_OUT || entry == DEAL_ENTRY_OUT_BY)
         {
            // in caso di chiusure parziali, tiene l'ultimo deal di uscita
            closeTime  = (datetime)HistoryDealGetInteger(dealTicket, DEAL_TIME);
            closePrice = HistoryDealGetDouble(dealTicket, DEAL_PRICE);
            comment    = HistoryDealGetString(dealTicket, DEAL_COMMENT);
            hasOut = true;
         }
      }

      if(!hasOut) continue; // posizione ancora aperta, salta

      // Filtro rigoroso: la chiusura deve cadere dentro il range richiesto
      if(closeTime < StartDate || closeTime > endSel) continue;

      if(OnlySymbol != "" && symbol != OnlySymbol) continue;
      if(MagicFilter != -1 && magic != MagicFilter) continue;

      string typeStr = (dealType == DEAL_TYPE_BUY) ? "BUY" :
                        (dealType == DEAL_TYPE_SELL) ? "SELL" : "OTHER";

      double duration = (double)(closeTime - openTime) / 60.0;

      double sl = 0, tp = 0;
      if(entryOrderTicket != 0 && HistoryOrderSelect(entryOrderTicket))
      {
         sl = HistoryOrderGetDouble(entryOrderTicket, ORDER_SL);
         tp = HistoryOrderGetDouble(entryOrderTicket, ORDER_TP);
      }

      string line =
         IntegerToString((long)posID) + sep +
         symbol + sep +
         typeStr + sep +
         D(lots) + sep +
         FormatDate(openTime) + sep +
         FormatTime(openTime) + sep +
         D(openPrice) + sep +
         FormatDate(closeTime) + sep +
         FormatTime(closeTime) + sep +
         D(closePrice) + sep +
         D(sl) + sep +
         D(tp) + sep +
         D(sumProfit) + sep +
         D(sumCommission) + sep +
         D(sumSwap) + sep +
         D(sumProfit + sumSwap + sumCommission) + sep +
         IntegerToString((int)magic) + sep +
         comment + sep +
         D(duration) + "\r\n";

      FileWriteString(handle, line);
      exported++;
   }

   FileClose(handle);
   Print("EXPORT COMPLETATO: ", exported);
   Print("File: MQL5/Files/", OutputFileName);
   Alert("Export completato: ", exported, " operazioni esportate in ", OutputFileName);
   return 0;
}