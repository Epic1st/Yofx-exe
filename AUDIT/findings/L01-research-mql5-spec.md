---
agent_id: L01
lane: MQL5 Language & Runtime Equivalence
scope:
  - src/Runtime/YO4X.Mql5.Runtime/
status: COMPLETE
generated: 2026-08-29T11:41:00Z
counts: { P0: 0, P1: 2, P2: 3, P3: 0 }
---

# L01 — MQL5 Language & Runtime Equivalence

## Scope audited
Reviewed all 39 source files in `src/Runtime/YO4X.Mql5.Runtime/`:
- `src/Runtime/YO4X.Mql5.Runtime/IMql5MarketContext.cs` (484 lines)
- `src/Runtime/YO4X.Mql5.Runtime/IMql5Strategy.cs` (28 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5CalendarTypes.cs` (146 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5ChartObjectStore.cs` (360 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Colors.cs` (108 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Constants.cs` (238 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5ErrorCodes.cs` (92 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Format.cs` (646 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Log.cs` (107 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5ProgramInfo.cs` (170 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Array.cs` (513 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Chart.cs` (693 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Conversion.cs` (619 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.DateTime.cs` (158 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Globals.cs` (354 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Indicator.cs` (505 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.MarketData.cs` (433 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Math.cs` (450 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Refused.cs` (259 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Symbol.cs` (221 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Terminal.cs` (470 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Text.cs` (360 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Trade.cs` (510 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.cs` (133 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5RuntimeOptions.cs` (43 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Structs.cs` (200 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5TradeTypes.cs` (189 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5TypeInfo.cs` (189 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5UnsupportedOperationException.cs` (52 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5ZeroedInstance.cs` (48 lines)
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5AccountInfo.cs` (228 lines)
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5DealInfo.cs` (129 lines)
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5HistoryOrderInfo.cs` (109 lines)
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5OrderInfo.cs` (125 lines)
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5PositionInfo.cs` (145 lines)
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5SymbolInfo.cs` (246 lines)
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5Trade.cs` (597 lines)
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5TradeConstants.cs` (514 lines)
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5TradeTransaction.cs` (63 lines)

## Verdict
The MQL5 runtime implementation is largely disciplined and faithful to canonical MQL5 specification, with strict deterministic math semantics (`MathRound` and `NormalizeDouble` rounding half away from zero, culture-invariant formatting, and accurate `ENUM_TIMEFRAMES` numeric values). However, several critical semantic edge-case divergences exist around array mutation and as-series tracking, out-of-bounds parameter validation in string routines, missing standard constants (`EMPTY_VALUE`), and indicator vs. market error code attribution.

## Findings

### [P1] `ArrayCopy`, `ArrayInsert`, and `ArrayRemove` discard the as-series indexing flag upon array reallocation
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Array.cs:183`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        if (destination.Length < required)
        {
            T[] grown = destination;
            Array.Resize(ref grown, required);
            destination = grown;
        }
  ```
- **Failure:** An array instance `arr` flagged with `ArraySetAsSeries(arr, true)` is passed to `ArrayCopy(ref arr, source, 0, 0, count)`. If `arr` must be resized, a new array instance `grown` is allocated. Unlike `ArrayResize` (lines 147–150), `ArrayCopy` (and similarly `ArrayInsert` at line 407 and `ArrayRemove` at line 437) does not re-register the new reference in `seriesFlags`. Subsequent calls to `ArrayGetAsSeries(arr)` return `false`, and `CopyRates`/`CopyBuffer` cease reversing the target buffer for series access.
- **Fix:** Record `bool series = IsSeriesArray(destination);` prior to resizing and invoke `SetSeriesArray(destination, true);` after reassigning the reallocated buffer (apply identical updates to `ArrayInsert` and `ArrayRemove`).

### [P1] `StringFind` clamps negative `startPosition` to 0 instead of returning -1 failure
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Text.cs:142`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        int start = startPosition < 0 ? 0 : startPosition;
        if (start > value.Length)
        {
            return -1;
        }
  ```
- **Failure:** In canonical MQL5, `StringFind(string_value, match_substring, start_pos)` specifies `start_pos` within `[0, StringLen - 1]`. A negative `start_pos` represents an invalid parameter and returns `-1`. In YO4X, calling `StringFind("EURUSD", "EUR", -5)` clamps `start` to `0` and incorrectly returns `0` (match found).
- **Fix:** Reject negative positions immediately: `if (startPosition < 0 || startPosition > value.Length) return -1;`.

### [P2] Standard constant `EMPTY_VALUE` is missing from `Mql5Constants`
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/Mql5Constants.cs:18`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
public static class Mql5Constants
{
    /// <summary>An invalid indicator or file handle.</summary>
    public const int InvalidHandle = -1;
  ```
- **Failure:** Canonical MQL5 defines `EMPTY_VALUE` as `DBL_MAX` (`1.7976931348623158e+308`) for empty indicator buffers and uncalculated metrics. `Mql5Constants` omits `EmptyValue` entirely, forcing standard library components (e.g. `Mql5AccountInfo.cs:89`) to open-code `double.MaxValue` and causing transpiled MQL5 strategies referencing `EMPTY_VALUE` or `Mql5Constants.EmptyValue` to fail compilation.
- **Fix:** Declare `public const double EmptyValue = double.MaxValue;` in `Mql5Constants`.

### [P2] `Finish` sets `ERR_INDICATOR_DATA_NOT_FOUND` (4806) for market price and history copy failures
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.MarketData.cs:416`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
    private int Finish<T>(int written, T[]? target)
    {
        if (written < 0)
        {
            SetError(Mql5ErrorCodes.IndicatorDataNotFound);
            return written;
        }
  ```
- **Failure:** When market history access built-ins (`CopyRates`, `CopyTime`, `CopyOpen`, `CopyHigh`, `CopyLow`, `CopyClose`, `CopySpread`, `CopyTickVolume`, `CopyRealVolume`) fail due to unavailable price history, `Finish` records error `4806` (`ERR_INDICATOR_DATA_NOT_FOUND`). In canonical MQL5, history access built-ins set history error codes such as `4401` (`ERR_HISTORY_NOT_FOUND`). Strategies inspecting `GetLastError()` to branch on missing history data receive an indicator error instead.
- **Fix:** Distinguish indicator buffer queries from market data queries so history copies set `Mql5ErrorCodes.MarketNotSelected` or a dedicated history error code on failure.

### [P2] `IMql5MarketContext` defaults `TimeGmt` directly to `TimeCurrent` with zero GMT offset
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/IMql5MarketContext.cs:133`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
    /// <summary>MQL5 <c>TimeGMT</c>. Defaults to the trade server clock.</summary>
    DateTime TimeGmt => TimeCurrent;
  ```
- **Failure:** In canonical MQL5, `TimeGMT()` reports UTC while `TimeCurrent()` reports broker server time (standard broker feeds operate on UTC+2 or UTC+3). Under default context implementations where `TimeGmt => TimeCurrent` and `TimeGmtOffset => 0`, trading strategies executing session filters against GMT (e.g., London or NY fixes) trigger at the wrong time of day by the broker's UTC offset.
- **Fix:** Compute default `TimeGmt` using `TimeCurrent.AddSeconds(-TimeGmtOffset)` or mandate that implementations supply a real UTC clock alongside server time.

## Referrals
None.

## Coverage gaps
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Conversion.cs:484`: Untested branch in `StringToCharArray` when `count > 0` and `count > text.Length`, where trailing null terminators are omitted.
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Format.cs:382`: Untested branch in `SkipLengthModifier` for Win32 length modifiers `'I32'` and `'I64'` combined with floating-point conversions.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 242.2s | 420921 tok | id=f1b2326d-89c5-479a-a380-61069eee2d5f
