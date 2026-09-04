---
agent_id: F25
lane: Feed Integrity & Market Data Replay
scope:
  - src/Runtime/YO4X.Mql5.Engine/Feed/IMql5MarketFeed.cs
  - src/Runtime/YO4X.Mql5.Engine/Feed/Mql5Bar.cs
  - src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs
  - src/Runtime/YO4X.Mql5.Engine/Feed/Mql5DeterministicRandom.cs
  - src/Runtime/YO4X.Mql5.Engine/Feed/Mql5SyntheticMarketFeed.cs
status: COMPLETE
generated: 2026-08-29T11:27:08Z
counts: { P0: 0, P1: 3, P2: 2, P3: 0 }
---

# F25 — Feed Integrity & Market Data Replay

## Scope audited
- [src/Runtime/YO4X.Mql5.Engine/Feed/IMql5MarketFeed.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Feed/IMql5MarketFeed.cs) (15 lines)
- [src/Runtime/YO4X.Mql5.Engine/Feed/Mql5Bar.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Feed/Mql5Bar.cs) (34 lines)
- [src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs) (167 lines)
- [src/Runtime/YO4X.Mql5.Engine/Feed/Mql5DeterministicRandom.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Feed/Mql5DeterministicRandom.cs) (52 lines)
- [src/Runtime/YO4X.Mql5.Engine/Feed/Mql5SyntheticMarketFeed.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Feed/Mql5SyntheticMarketFeed.cs) (108 lines)

## Verdict
The core deterministic generation components ([`Mql5DeterministicRandom`](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Feed/Mql5DeterministicRandom.cs) and [`Mql5SyntheticMarketFeed`](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Feed/Mql5SyntheticMarketFeed.cs)) are mathematically sound, side-effect free, and stream lazily without unbounded memory allocations. However, [`Mql5CsvMarketFeed`](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs) is fragile and lacks critical market data validation: it splits simultaneously across comma, semicolon, and tab delimiters, corrupting comma-decimal European CSV rows into massive price spikes; it omits OHLC invariant checks allowing inverted bars (`High < Low`) into the simulated broker; and it fails to enforce monotonic ascending time order or alert callers when bad rows are dropped.

## Findings

### [P1] Multi-character separator split corrupts comma-decimal European CSV rows into invalid price spikes
- **Where:** [`src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs:13`](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs#L13-L13) and [`src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs:82`](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs#L82-L82)
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  private static readonly char[] Separators = [',', ';', '\t'];
  ```
  ```csharp
  string[] fields = trimmed.Split(Separators, StringSplitOptions.TrimEntries);
  ```
- **Failure:** When parsing a European MetaTrader CSV export formatted with semicolon delimiters and comma decimal separators (e.g. `2024.01.01 00:00;1,10000;1,10120;1,09980;1,10050;321;12`), `Split(Separators)` splits simultaneously on `,` and `;`. This decomposes price numbers across separate tokens (`fields[1]="1"`, `fields[2]="10000"`, `fields[3]="1"`, `fields[4]="10120"`), resulting in Open=1.0, High=10000.0, Low=1.0, Close=10120.0. `TryParseBar` returns `true`, silently injecting 10,000x price spikes and corrupted spread/volume data into the engine rather than parsing properly or failing.
- **Fix:** Detect or specify the column delimiter per feed/header rather than splitting on all separators at once, and parse floating-point tokens only after column boundaries are isolated.

### [P1] Lack of OHLC invariant and positive price validation allows impossible bar geometries into the engine
- **Where:** [`src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs:98-104`](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs#L98-L104)
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  if (!TryParseDouble(fields[priceOffset], out double open) ||
      !TryParseDouble(fields[priceOffset + 1], out double high) ||
      !TryParseDouble(fields[priceOffset + 2], out double low) ||
      !TryParseDouble(fields[priceOffset + 3], out double close))
  {
      return false;
  }
  ```
- **Failure:** `TryParseBar` verifies only that the 4 price fields parse as `double`, never checking the fundamental OHLC invariants (`High >= Open`, `High >= Close`, `Low <= Open`, `Low <= Close`, `High >= Low`, or `Low > 0`). Input such as `2024.01.01 00:00,1.1000,1.0500,1.1500,1.0800` (where High is lower than Low) is accepted as a valid `Mql5Bar`. When passed to `Mql5SimulatedBroker.ApplyBar`, the intrabar step simulation walks through an inverted path (`Open(1.10) -> Low(1.15) -> High(1.05) -> Close(1.08)`), triggering limit orders and stop-losses at impossible fills.
- **Fix:** Validate that `open > 0`, `high >= Math.Max(open, close)`, `low <= Math.Min(open, close)`, and `low > 0` before returning `true` in `TryParseBar`.

### [P1] CsvMarketFeed fails to enforce monotonic ascending time order and duplicate timestamp rejection
- **Where:** [`src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs:53-62`](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs#L53-L62)
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public IEnumerable<Mql5Bar> ReadBars()
  {
      foreach (string line in lineSource())
      {
          if (TryParseBar(line, DefaultSpreadPoints, out Mql5Bar bar))
          {
              yield return bar;
          }
      }
  }
  ```
- **Failure:** `ReadBars()` maintains no timestamp state between yielded bars. When given an unsorted CSV file or a file containing duplicate timestamps (e.g. `2024.01.05 00:00` followed by `2024.01.02 00:00`), bars are yielded in file order, violating `IMql5MarketFeed`'s contract ("Enumerates the bars in ascending time order"). This causes `Mql5SimulatedBroker.ApplyBar` to miscalculate swap accrual (`bar.Time.Date > time.Date`), corrupts indicator history series in `Mql5MarketContext`, and breaks backtest determinism.
- **Fix:** Track `previousTime` in `ReadBars()` and reject or skip bars where `bar.Time <= previousTime` (or sort during feed construction).

### [P2] Silent omission of unparseable CSV rows masks incompatible formats and corrupt datasets
- **Where:** [`src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs:55-61`](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs#L55-L61)
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  foreach (string line in lineSource())
  {
      if (TryParseBar(line, DefaultSpreadPoints, out Mql5Bar bar))
      {
          yield return bar;
      }
  }
  ```
- **Failure:** Any unparseable row (unsupported date format such as `yyyy/MM/dd`, non-numeric price token, or corrupted columns) returns `false` from `TryParseBar` and is silently dropped. If a user provides an export with an unrecognized timestamp layout, `ReadBars()` yields 0 bars without logging or error. The backtest runner finishes cleanly with 0 ticks and 0 trades, misleading the user into believing the backtest ran successfully on valid empty market data.
- **Fix:** Provide a configurable parse failure mode or track and report skipped row counts so that malformed data files produce actionable errors or warnings.

### [P2] Missing bounds validation on synthetic feed initialization properties enables non-advancing time
- **Where:** [`src/Runtime/YO4X.Mql5.Engine/Feed/Mql5SyntheticMarketFeed.cs:47`](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Feed/Mql5SyntheticMarketFeed.cs#L47-L47)
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  /// <summary>Gets the bar period in minutes.</summary>
  public int PeriodMinutes { get; init; } = 60;
  ```
- **Failure:** `PeriodMinutes`, `Point`, `StartPrice`, and `MinimumPrice` have unvalidated public `init` setters. If a caller sets `PeriodMinutes = 0` (e.g. `new Mql5SyntheticMarketFeed("EURUSD", 1, 100) { PeriodMinutes = 0 }`), `time = time.AddMinutes(PeriodMinutes)` in line 94 never advances, generating 100 bars with identical timestamps and violating feed monotonicity. If `Point <= 0`, price step calculations collapse.
- **Fix:** Add validation ensuring `PeriodMinutes > 0`, `Point > 0`, `StartPrice > 0`, and `MinimumPrice > 0`.

## Referrals
- `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs` — `ApplyBar` (line 151) assumes monotonic timestamps and valid OHLC bounds without defensive validation, causing corrupted feed bars to produce erroneous swap accruals and invalid fills.
- `src/Tools/YO4X.Backtest.Runner/Program.cs` — Line 219 consumes `feed.ReadBars()` without verifying if any bars were produced or if the CSV source was silently dropped due to parse failures.

## Coverage gaps
- `src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs:128-134` — Branch handling separate Date and Time columns (Layout B) when column 1 contains numeric-looking timestamps vs non-numeric time strings.
- `src/Runtime/YO4X.Mql5.Engine/Feed/Mql5SyntheticMarketFeed.cs:72-75` — Sub-step reflection branch (`walking < MinimumPrice`) when walking prices drop below the minimum price threshold.
- `src/Runtime/YO4X.Mql5.Engine/Feed/Mql5DeterministicRandom.cs:43-46` — Branch in `NextInt32` where `maxExclusive <= minInclusive`.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 75.9s | 111345 tok | id=9dce5dc7-c343-40a5-b826-3a72391ed94f
