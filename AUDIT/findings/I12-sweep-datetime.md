---
agent_id: I12
lane: sweep-datetime
scope:
  - src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.DateTime.cs
  - src/Runtime/YO4X.Mql5.Runtime/Mql5Structs.cs
  - src/Runtime/YO4X.Mql5.Runtime/IMql5MarketContext.cs
  - src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Conversion.cs
  - src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Symbol.cs
  - src/Runtime/YO4X.Mql5.Engine/Context/Mql5MarketContext.cs
  - src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs
  - src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs
  - src/Runtime/YO4X.Mql5.Backtest/EngineRuntimeContext.cs
  - src/Runtime/YO4X.Mql5.Live/LiveBarSeries.cs
  - src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs
  - src/Runtime/YO4X.Mql5.Live/LiveStrategyRunner.cs
  - src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiDemoTradeClient.cs
  - src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Expressions.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Lexer.cs
  - src/Modules/Risk/YO4X.Risk/NumericRiskEvaluation.cs
  - src/Tools/YO4X.MarketData.Mt5Import/Mt5TickExportReader.cs
  - src/Tools/YO4X.MarketData.Mt5History/Program.cs
  - src/Tools/YO4X.Backtest.Runner/Program.cs
  - src/Runtime/YO4X.Runtime.Contracts/UserOperationProtocolPrimitives.cs
  - src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs
status: COMPLETE
generated: 2026-08-29T11:36:30Z
counts: { P0: 0, P1: 6, P2: 1, P3: 0 }
---

# I12 — sweep-datetime

## Scope audited
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.DateTime.cs` (158 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Structs.cs` (200 lines)
- `src/Runtime/YO4X.Mql5.Runtime/IMql5MarketContext.cs` (484 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Conversion.cs` (619 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Symbol.cs` (215 lines)
- `src/Runtime/YO4X.Mql5.Engine/Context/Mql5MarketContext.cs` (320 lines)
- `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs` (1,153 lines)
- `src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs` (167 lines)
- `src/Runtime/YO4X.Mql5.Backtest/EngineRuntimeContext.cs` (357 lines)
- `src/Runtime/YO4X.Mql5.Live/LiveBarSeries.cs` (279 lines)
- `src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs` (517 lines)
- `src/Runtime/YO4X.Mql5.Live/LiveStrategyRunner.cs` (262 lines)
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiDemoTradeClient.cs` (774 lines)
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Expressions.cs` (1,822 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Lexer.cs` (1,235 lines)
- `src/Modules/Risk/YO4X.Risk/NumericRiskEvaluation.cs` (713 lines)
- `src/Tools/YO4X.MarketData.Mt5Import/Mt5TickExportReader.cs` (303 lines)
- `src/Tools/YO4X.MarketData.Mt5History/Program.cs` (335 lines)
- `src/Tools/YO4X.Backtest.Runner/Program.cs` (680 lines)
- `src/Runtime/YO4X.Runtime.Contracts/UserOperationProtocolPrimitives.cs` (438 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs` (2,995 lines)

## Verdict
The core control plane and database infrastructure handle date and time properly with strict UTC, canonical ISO-8601 strings, and Postgres `timestamptz`. However, cross-cutting date and time handling across the MQL5 runtime, code generator, and market data ingest has notable defects. Specifically, `TimeGMT` and `TimeGMTOffset` conflate broker server time with UTC, broken-down day-of-year indexing has a 1-based off-by-one divergence from MQL5, time-only literals bake host compilation dates into emitted binaries, quote streaming mixes fallback UTC with broker server time, fixed offset imports ignore broker DST transitions, and weekend swap accounting double-charges rollovers.

## Findings

### [P1] `IMql5MarketContext` defaults `TimeGmt` to `TimeCurrent` and `TimeGmtOffset` to zero
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/IMql5MarketContext.cs:134-143`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
    /// <summary>MQL5 <c>TimeGMT</c>. Defaults to the trade server clock.</summary>
    DateTime TimeGmt => TimeCurrent;

    /// <summary>MQL5 <c>TimeTradeServer</c>. Defaults to the trade server clock.</summary>
    DateTime TimeTradeServer => TimeCurrent;

    /// <summary>MQL5 <c>TimeGMTOffset</c>, in seconds.</summary>
    int TimeGmtOffset => 0;

    /// <summary>MQL5 <c>TimeDaylightSavings</c>, in seconds.</summary>
    int TimeDaylightSavings => 0;
  ```
- **Failure:** In MetaTrader 5, `TimeCurrent()` returns broker server time (typically UTC+2 in winter / UTC+3 in summer), while `TimeGMT()` returns true UTC and `TimeGMTOffset()` returns the broker's offset from UTC in seconds (e.g., 7200 or 10800). Because `EngineRuntimeContext` and `LiveBrokerContext` leave these defaults unoverridden, any strategy calling `TimeGMT()` receives broker server time instead of GMT/UTC. On a UTC+2 broker, a trading filter checking for London open (07:00 GMT) or Asian session hours evaluates against server time (07:00 server = 05:00 GMT), firing trades 2 hours too early. Furthermore, strategies using the standard MQL5 idiom `TimeCurrent() - TimeGMTOffset()` get offset `0` and fail to convert server time to UTC.
- **Fix:** Allow `IMql5MarketContext` implementations to receive the broker's server UTC offset and DST status, and implement `TimeGmt` as `TimeCurrent - TimeSpan.FromSeconds(TimeGmtOffset)` with `TimeGmtOffset` reflecting the active server offset.

### [P1] `Mql5Time.ToStruct` sets 1-based .NET `DayOfYear` instead of MQL5 0-based `day_of_year`
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/Mql5Structs.cs:176-177`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
            DayOfWeek = (int)moment.DayOfWeek,
            DayOfYear = moment.DayOfYear
        };
  ```
- **Failure:** MQL5 documentation explicitly specifies `MqlDateTime.day_of_year` as 0-indexed (range `0..365`, where January 1 is `0`). .NET `DateTime.DayOfYear` is 1-indexed (range `1..366`, where January 1 is `1`). Calling `TimeToStruct(0, out MqlDateTime dt)` for 1970-01-01 produces `dt.DayOfYear = 1` instead of `0`. On March 15, 2024 (a leap year), `dt.DayOfYear` is set to `75` instead of `74` (baked erroneously into `Mql5ConversionTests.cs:193`). Any MQL5 strategy with calendar day-of-year calculations or seasonal lookups runs shifted by +1 day.
- **Fix:** Subtract 1 when populating `DayOfYear`: `DayOfYear = moment.DayOfYear - 1`.

### [P1] `Mt5NetApiDemoTradeClient` falls back to `DateTime.UtcNow` when quote timestamp is missing
- **Where:** `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiDemoTradeClient.cs:658-659`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
                DateTime stamp = Read<DateTime>(type, quote, "Time");
                observer(stamp == default ? DateTime.UtcNow : stamp, bid, ask);
  ```
- **Failure:** The MT5 vendor quote's `Time` property represents broker trade server time (e.g. 14:00:00 on a UTC+2 broker). When `stamp` is `default`, the method substitutes `DateTime.UtcNow` (12:00:00). In `LiveStrategyRunner.cs:207`, these quotes feed directly into `LiveBarSeries.Accept(quote.Time, quote.Bid, quote.Ask)`. If an un-timestamped quote arrives between two server-timestamped quotes, `LiveBarSeries` receives timestamps jump backwards from 14:00:00 to 12:00:00 and then back to 14:00:01, causing `FloorToPeriod` to evaluate `slot < formingOpenTime`, corrupting bar accumulation and tick sequencing.
- **Fix:** Instead of falling back to raw `DateTime.UtcNow`, maintain the last observed quote timestamp or apply the known broker server offset to `DateTime.UtcNow`.

### [P1] Transpiler parses time-only datetime literals with host machine's current date
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Expressions.cs:349-361`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
            "H:mm:ss", "H:mm"
        ];
        if (!DateTime.TryParseExact(
                body,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime parsed))
        {
            return false;
        }

        seconds = (long)(parsed - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
  ```
- **Failure:** When an MQL5 strategy contains a time-only literal such as `D'12:30:00'`, `DateTime.TryParseExact` matches `"H:mm:ss"`. In .NET, parsing a time string without date components defaults the date portion to `DateTime.Today` on the compilation machine. `seconds` is calculated against `1970-01-01`, emitting a hardcoded scalar integer containing the build day's year/month/day (e.g., 2026-08-29 12:30:00). When the strategy is later backtested on historical data from 2020, comparing bar time against `D'12:30:00'` compares a 2020 timestamp against a 2026 timestamp, causing the condition to never match.
- **Fix:** Use `DateTimeStyles.NoCurrentDateDefault` or normalize time-only literals to the MQL5 epoch date (1970-01-01) before parsing.

### [P1] `Mt5TickExportReader` applies a single fixed UTC offset across historical tick data, ignoring DST
- **Where:** `src/Tools/YO4X.MarketData.Mt5Import/Mt5TickExportReader.cs:184-186`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        // The export carries broker-server wall-clock time. The caller states the offset; this tool
        // never infers it, and applies that single fixed offset to every row.
        timestampUtc = DateTime.SpecifyKind(serverTime - serverUtcOffset, DateTimeKind.Utc);
  ```
- **Failure:** MetaTrader 5 broker servers operate on Eastern European Time (EET/EEST, UTC+2 in winter and UTC+3 in summer) so that daily candle close coincides with 5:00 PM New York time. Applying a single caller-provided fixed `serverUtcOffset` across an exported dataset covering multiple seasons causes all ticks during the opposing DST period to be shifted by exactly ±1 hour (3600 seconds) from their true UTC instant, corrupting economic news alignments and multi-source tick synchronization.
- **Fix:** Support timezone identifiers (e.g. `Europe/Athens` / `EET`) with historical DST transition rules in `Mt5TickExportReader` to dynamically resolve the UTC offset for each tick timestamp.

### [P1] `Mql5SimulatedBroker` accrues weekend swap based on bar calendar date deltas
- **Where:** `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:153-157`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        if (time != DateTime.UnixEpoch && bar.Time.Date > time.Date)
        {
            int days = (bar.Time.Date - time.Date).Days;
            AccrueSwap(days);
        }
  ```
- **Failure:** When the market closes Friday evening (e.g., 2024-03-15) and reopens Sunday evening / Monday morning (2024-03-18), `(bar.Time.Date - time.Date).Days` evaluates to 3 days, invoking `AccrueSwap(3)`. In MetaTrader 5 and standard Forex market mechanics, 3-day swap (triple rollover) is charged during the Wednesday-to-Thursday rollover to settle weekend interest while markets are closed. Accruing 3 days of swap over the weekend on top of Wednesday's daily swap double-charges weekend rollover costs (9 days of swap charged per 7-day week), distorting balance and backtest P&L for swing trading strategies.
- **Fix:** Track trading session rollovers and implement standard 3-day rollover on Wednesday (or the symbol's designated triple-swap day) while ignoring non-trading weekend gap days.

### [P2] `MeasureCoverage` in `YO4X.Backtest.Runner` deducts 24 hours per weekend calendar day
- **Where:** `src/Tools/YO4X.Backtest.Runner/Program.cs:367-376`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        int weekendDays = 0;
        for (DateTime day = window[0].Time.Date; day <= window[^1].Time.Date; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                weekendDays++;
            }
        }

        double expected = (spanMinutes - (weekendDays * 1440.0)) / periodMinutes;
  ```
- **Failure:** `spanMinutes` is computed from the exact start and end timestamps `(window[^1].Time - window[0].Time).TotalMinutes`. However, the loop counts every touched calendar day that is Saturday or Sunday and subtracts full 24-hour blocks (`1440.0` minutes). If a dataset starts Sunday at 22:00 (market open) and ends Monday at 04:00 (span = 6 hours = 360 minutes), the loop identifies Sunday as 1 weekend day and subtracts 1440 minutes: `expected = (360 - 1440) / period = -1080 / period`. Because `expected <= 0`, `MeasureCoverage` returns `0%` coverage despite 100% of available trading bars being present.
- **Fix:** Compute weekend exclusion using exact trading session boundaries (e.g., Friday 22:00 UTC to Sunday 22:00 UTC) rather than multiplying whole calendar dates by 1440 minutes.

## Referrals
None.

## Coverage gaps
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Conversion.cs:337-346`: Untested path when `StringToTime("HH:mm")` is called with time-only strings where `context.TimeCurrent` is unset (`DateTime.UnixEpoch`), potentially creating sub-epoch timestamps.
- `src/Runtime/YO4X.Mql5.Live/LiveBarSeries.cs:194-198`: Untested behavior in `FloorToPeriod` when quote timestamps cross a leap second or daylight saving transition in local time mode.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 437.1s | 380987 tok | id=7b0d711a-39c3-4a37-9f48-07e6e8ad9aa4
