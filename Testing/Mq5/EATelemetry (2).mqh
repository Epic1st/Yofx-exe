//+------------------------------------------------------------------+
//|                                               EATelemetry.mqh    |
//|                              Generic EA Telemetry Library         |
//+------------------------------------------------------------------+
#property strict
#ifndef EATELEMETRY_MQH
#define EATELEMETRY_MQH

//--- Internal Structure for Dynamic Metric Tracking
struct TelemetryMetric {
   string name;
   int count;
};

//--- Internal State
TelemetryMetric g_attempts[];
TelemetryMetric g_triggers[];
int g_totalTrades = 0;

string g_statsFile = "EA_Stats.csv";
string g_summaryFile = "EA_Summary.csv";

//+------------------------------------------------------------------+
//| Internal: Find or Create Metric                                  |
//+------------------------------------------------------------------+
int FindOrAddMetric(TelemetryMetric &arr[], string name)
{
   for(int i=0; i<ArraySize(arr); i++) {
      if(arr[i].name == name) return i;
   }
   int s = ArraySize(arr);
   ArrayResize(arr, s + 1);
   arr[s].name = name;
   arr[s].count = 0;
   return s;
}

//+------------------------------------------------------------------+
//| TelemetryInit: Call in OnInit()                                  |
//+------------------------------------------------------------------+
void TelemetryInit()
{
   int handle = FileOpen(g_statsFile, FILE_CSV|FILE_READ|FILE_WRITE|FILE_SHARE_READ, ',');
   if(handle != INVALID_HANDLE)
   {
      if(FileSize(handle)==0)
      {
         FileWrite(handle, "Time", "Symbol", "Magic", "Ticket", "Event", "Type", "Price", "Profit", "Duration", "Strategy", "Comment");
      }
      FileClose(handle);
   }
}

//+------------------------------------------------------------------+
//| TelemetryTradeOpened: Log trade entry                            |
//+------------------------------------------------------------------+
void TelemetryTradeOpened(int ticket, string symbol, int magic, string type, double lots, double price, string strategy)
{
   g_totalTrades++;
   int handle = FileOpen(g_statsFile, FILE_CSV|FILE_READ|FILE_WRITE|FILE_SHARE_READ, ',');
   if(handle == INVALID_HANDLE) return;
   
   FileSeek(handle, 0, SEEK_END);
   FileWrite(handle, TimeToString(TimeCurrent(), TIME_DATE|TIME_SECONDS), symbol, magic, ticket, "TRADE_OPEN", type, DoubleToString(price, (int)MarketInfo(symbol, MODE_DIGITS)), "0.00", "0", strategy, "");
   FileClose(handle);
}

//+------------------------------------------------------------------+
//| TelemetryTradeClosed: Log trade exit                             |
//+------------------------------------------------------------------+
void TelemetryTradeClosed(int ticket, string symbol, int magic, string type, double price, double profit, int duration, string reason, string strategy)
{
   int handle = FileOpen(g_statsFile, FILE_CSV|FILE_READ|FILE_WRITE|FILE_SHARE_READ, ',');
   if(handle == INVALID_HANDLE) return;
   
   FileSeek(handle, 0, SEEK_END);
   FileWrite(handle, TimeToString(TimeCurrent(), TIME_DATE|TIME_SECONDS), symbol, magic, ticket, reason, type, DoubleToString(price, (int)MarketInfo(symbol, MODE_DIGITS)), DoubleToString(profit, 2), duration, strategy, "");
   FileClose(handle);
}

//+------------------------------------------------------------------+
//| TelemetryEvent: Log state changes (BE trigger, SL adjustments)   |
//+------------------------------------------------------------------+
void TelemetryEvent(string eventName,
                    string symbol,
                    int magic,
                    int ticket,
                    string type,
                    double price,
                    double profit,
                    int duration,
                    string strategy)
{
   int handle = FileOpen(g_statsFile, FILE_CSV|FILE_READ|FILE_WRITE|FILE_SHARE_READ, ',');
   if(handle == INVALID_HANDLE) return;
   
   FileSeek(handle, 0, SEEK_END);
   FileWrite(handle,
      TimeToString(TimeCurrent(), TIME_DATE|TIME_SECONDS),
      symbol,
      magic,
      ticket,
      eventName,
      type,
      DoubleToString(price, (int)MarketInfo(symbol, MODE_DIGITS)),
      DoubleToString(profit, 2),
      duration,
      strategy,
      "");
   FileClose(handle);
}

//+------------------------------------------------------------------+
//| TelemetryAttempt: Increment condition checks (e.g. eligible)     |
//+------------------------------------------------------------------+
void TelemetryAttempt(string metricName)
{
   int idx = FindOrAddMetric(g_attempts, metricName);
   g_attempts[idx].count++;
}

//+------------------------------------------------------------------+
//| TelemetryTrigger: Increment event executions                     |
//+------------------------------------------------------------------+
void TelemetryTrigger(string metricName)
{
   int idx = FindOrAddMetric(g_triggers, metricName);
   g_triggers[idx].count++;
}

//+------------------------------------------------------------------+
//| TelemetryValue: Log numeric diagnostic values                    |
//+------------------------------------------------------------------+
void TelemetryValue(string symbol,
                    int magic,
                    int ticket,
                    string metric,
                    double value,
                    string strategy)
{
   int handle = FileOpen(g_statsFile,
                         FILE_CSV|FILE_READ|FILE_WRITE|FILE_SHARE_READ,
                         ',');

   if(handle == INVALID_HANDLE)
      return;

   FileSeek(handle, 0, SEEK_END);

   FileWrite(handle,
      TimeToString(TimeCurrent(), TIME_DATE|TIME_SECONDS),
      symbol,
      magic,
      ticket,
      "VALUE",
      metric,
      DoubleToString(value, 6),
      "",
      "",
      strategy,
      "");

   FileClose(handle);
}

//+------------------------------------------------------------------+
//| TelemetryError: Log execution failures                           |
//+------------------------------------------------------------------+
void TelemetryError(string symbol,
                    int magic,
                    int ticket,
                    string type,
                    double price,
                    double profit,
                    string errorDesc)
{
   int handle = FileOpen(g_statsFile, FILE_CSV|FILE_READ|FILE_WRITE|FILE_SHARE_READ, ',');
   if(handle == INVALID_HANDLE) return;
   
   FileSeek(handle, 0, SEEK_END);
   FileWrite(handle,
      TimeToString(TimeCurrent(), TIME_DATE|TIME_SECONDS),
      symbol,
      magic,
      ticket,
      "ERROR",
      type,
      DoubleToString(price, (int)MarketInfo(symbol, MODE_DIGITS)),
      DoubleToString(profit, 2),
      "0",
      "System",
      errorDesc);
   FileClose(handle);
}

//+------------------------------------------------------------------+
//| TelemetrySummary: Generate clean summary file                    |
//+------------------------------------------------------------------+
void TelemetrySummary()
{
   int handle = FileOpen(g_summaryFile, FILE_CSV|FILE_WRITE|FILE_SHARE_READ, ',');
   if(handle == INVALID_HANDLE) return;
   
   FileWrite(handle, "Metric", "Count");
   FileWrite(handle, "TOTAL_TRADES", g_totalTrades);
   
   for(int i=0; i<ArraySize(g_triggers); i++) {
      FileWrite(handle, g_triggers[i].name, g_triggers[i].count);
   }
   
   FileWrite(handle, "", "");
   FileWrite(handle, "ATTEMPT_METRICS", "Count");
   
   for(int i=0; i<ArraySize(g_attempts); i++) {
      FileWrite(handle, g_attempts[i].name, g_attempts[i].count);
   }
   
   FileClose(handle);
}

//+------------------------------------------------------------------+
//| TelemetryShutdown: Call in OnDeinit()                           |
//+------------------------------------------------------------------+
void TelemetryShutdown()
{
   TelemetrySummary();
}

#endif // EATELEMETRY_MQH