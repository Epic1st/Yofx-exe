---
agent_id: F14
lane: rt-marketdata-symbol
scope:
  - src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.MarketData.cs
  - src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Symbol.cs
status: COMPLETE
generated: 2026-08-29T11:30:00Z
counts: { P0: 1, P1: 2, P2: 0, P3: 1 }
---

# F14 — rt-marketdata-symbol

## Scope audited
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.MarketData.cs` (433 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Symbol.cs` (221 lines)

## Verdict
The single-bar price readers (`iOpen`, `iClose`, etc.) and basic symbol dispatch correctly delegate to `IMql5MarketContext` and resolve empty/null symbols. However, there is a critical P0 defect in the `Copy*` family: resizing dynamic destination arrays wipes out their `ArraySetAsSeries` registration in `ConditionalWeakTable`, silently reversing price series orientation back to oldest-first. Additionally, the boolean out-parameter overloads of `SymbolInfo*` and `SeriesInfoInteger` unconditionally return `true`, allowing strategies to consume silent zeroes for tick sizes, volume steps, and bar counts.

## Findings

### [P0] ArraySetAsSeries flag is silently destroyed when Copy* functions resize target buffer
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.MarketData.cs:390`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  T[] buffer = target ?? [];
  if (range.Kind != Mql5CopyRangeKind.TimeRange && range.Count > 0 && buffer.Length < range.Count)
  {
      Array.Resize(ref buffer, range.Count);
  }

  int written = copy(Resolve(symbol), Timeframe(timeframe), range, ref buffer);
  target = buffer;
  return Finish(written, target);
  ```
- **Failure:** A strategy flags a dynamic array as a timeseries via `ArraySetAsSeries(rates, true)` (e.g. starting with `rates = []` or size 0) and calls `CopyClose("EURUSD", PERIOD_M1, 0, 10, ref rates)`. `Array.Resize(ref buffer, 10)` allocates a new array instance. Because `ConditionalWeakTable<object, SeriesFlag>` keys by object reference, the series flag is not transferred to the new array instance. When `Finish` checks `IsSeriesArray(target)`, it returns `false` and skips `Array.Reverse`. The caller receives chronological (oldest-first) data where `rates[0]` is the bar from 10 periods ago instead of the current bar, and `ArrayGetAsSeries(rates)` now returns `false`.
- **Fix:** Check `bool series = IsSeriesArray(buffer)` prior to `Array.Resize` in `CopySeriesCore` and `CopyRatesCore`, and call `SetSeriesArray(buffer, true)` if the original array was flagged, or delegate array resizing to `ArrayResize` which already handles flag propagation.

### [P1] SymbolInfo and SeriesInfo out-parameter overloads unconditionally return true
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Symbol.cs:127`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public bool SymbolInfoDouble(string? name, int propertyId, out double value)
  {
      value = context.SymbolInfoDouble(Resolve(name), propertyId);
      return true;
  }
  ```
- **Failure:** A strategy calls `if (SymbolInfoDouble("INVALID_SYMBOL", SYMBOL_TRADE_TICK_SIZE, out double tickSize))` expecting `false` to guard against an unavailable symbol. The method unconditionally returns `true` with `tickSize = 0.0`. Downstream code performing lot or price normalization (`price / tickSize` or `volume / volumeStep`) calculates division by zero resulting in `double.PositiveInfinity` or `NaN`, causing trade rejections or unhandled runtime exceptions. The same unconditional `return true;` is present in `SymbolInfoInteger` (line 137), `SymbolInfoString` (line 147), and `SeriesInfoInteger` (`Mql5Runtime.MarketData.cs:231`).
- **Fix:** Check whether the underlying symbol or series exists and has valid property data; return `false` and record `Mql5ErrorCodes.MarketUnknownSymbol` or `Mql5ErrorCodes.InvalidParameter` when property lookups fail.

### [P1] Dynamic destination arrays are not resized to actual copied element count
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.MarketData.cs:388`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  if (range.Kind != Mql5CopyRangeKind.TimeRange && range.Count > 0 && buffer.Length < range.Count)
  {
      Array.Resize(ref buffer, range.Count);
  }

  int written = copy(Resolve(symbol), Timeframe(timeframe), range, ref buffer);
  target = buffer;
  return Finish(written, target);
  ```
- **Failure:** A caller passes a dynamic array to `CopyRates` or `CopyClose` requesting 50 bars (`range.Count = 50`), but the broker history only contains 10 bars. `buffer` is resized to 50, 10 elements are populated, and `written = 10` is returned. `target` remains length 50 with 40 unpopulated zero/default entries. Strategies iterating over `ArraySize(target)` or checking bounds process 40 ghost bars with zero prices/timestamps, corrupting indicator computations.
- **Fix:** If `written >= 0` and `buffer.Length != written` on a dynamic array target, resize `buffer` to `written` elements before returning from `Finish`.

### [P3] Finish records indicator error code for market data timeseries failures
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.MarketData.cs:416`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  if (written < 0)
  {
      SetError(Mql5ErrorCodes.IndicatorDataNotFound);
      return written;
  }
  ```
- **Failure:** When `CopyRates`, `CopyOpen`, or `CopyTime` fails (e.g. symbol not found or history not yet loaded), `Finish` records error 4806 (`ERR_INDICATOR_DATA_NOT_FOUND`). Strategies inspecting `GetLastError()` to identify market/history synchronization issues expect history error codes (e.g. 4401 `ERR_HISTORY_NOT_FOUND` or 4301 `ERR_MARKET_UNKNOWN_SYMBOL`) and fail to match on indicator-specific error codes.
- **Fix:** Differentiate between indicator buffer copies and price series copies in `Finish` to set the appropriate market data error code.

## Referrals
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Indicator.cs:452` — `CopyBufferCore` calls `Array.Resize(ref target, range.Count)` directly, dropping the `SeriesFlag` in `ConditionalWeakTable` when resizing dynamic indicator buffers.
- `src/Runtime/YO4X.Mql5.Backtest/EngineRuntimeContext.cs:202` — `CopyRates` and `CopySeries` only support `Mql5CopyRangeKind.FromPosition`, returning -1 for `FromTime` and `TimeRange` requests.
- `src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs:445` — `CopyRates` only supports `FromPosition` and does not implement `CopyOpen`, `CopyHigh`, `CopyLow`, `CopyClose`, `CopyTime`, or `CopyTickVolume`.

## Coverage gaps
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.MarketData.cs:388-395` — Untested branch: `CopySeriesCore` resizing when `target` is pre-flagged as series (`ArraySetAsSeries(arr, true)`) with `target.Length < count`. Existing tests only test pre-allocated arrays where `target.Length == count`, which hides the series flag drop on resize.
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.MarketData.cs:401-408` — Untested branch: `CopyRatesCore` when fewer bars exist in history than requested (`written < range.Count`), leaving dynamic arrays oversized with uninitialized zero structs.
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Symbol.cs:127-152` — Untested branch: `SymbolInfoDouble`, `SymbolInfoInteger`, and `SymbolInfoString` boolean out-parameter overloads when passed unknown symbols or invalid property IDs.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 130.1s | 381577 tok | id=0b1c9c65-f45d-4960-919c-e37c0512e93c
