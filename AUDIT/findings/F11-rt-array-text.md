---
agent_id: F11
lane: Array & String Runtime
scope:
  - src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Array.cs
  - src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Text.cs
status: COMPLETE
generated: 2026-08-29T11:29:00Z
counts: { P0: 2, P1: 3, P2: 3, P3: 0 }
---

# F11 — Array & String Runtime

## Scope audited
- [Mql5Runtime.Array.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Array.cs) (513 lines)
- [Mql5Runtime.Text.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Text.cs) (360 lines)

## Verdict
The native array and string surfaces are partially sound for simple linear buffers, but suffer from critical semantic defects regarding MQL5 `AS_SERIES` lifecycle tracking and bounds handling. In particular, array reallocation in mutating functions (`ArrayCopy`, `ArrayInsert`, `ArrayRemove`) drops the timeseries flag from the internal table, and `ArrayMaximum`/`ArrayMinimum` fail to account for timeseries indexing on user arrays, causing strategies to trade on reversed or misplaced bar data (P0). In addition, `StringInit` zeroes length instead of allocating space-filled buffers, and multiple bounds checks silently clamp invalid inputs to valid indices rather than signaling errors.

## Findings

### [P0] ArrayCopy, ArrayInsert, and ArrayRemove drop AS_SERIES flag on array reallocation
- **Where:** [Mql5Runtime.Array.cs:183-188](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Array.cs#L183-L188)
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
- **Failure:** A strategy flags an indicator buffer with `ArraySetAsSeries(dest, true)`. When `ArrayCopy` (or `ArrayInsert`/`ArrayRemove`) grows or shrinks the array, `destination` is assigned a newly allocated CLR array instance. Because series flags are keyed on object reference in `ConditionalWeakTable<object, SeriesFlag>`, the new buffer loses its `AS_SERIES` flag (`ArrayGetAsSeries(dest)` becomes `false`). Subsequent calls to `CopyBuffer` or strategy bar indexing read and populate data chronologically forward rather than newest-at-0, inverting bar history and generating inverted trading signals.
- **Fix:** In `ArrayCopy`, `ArrayInsert`, and `ArrayRemove`, check `bool series = IsSeriesArray(destination)` before reallocating and call `SetSeriesArray(destination, true)` after reassigning the new array reference.

### [P0] ArrayMaximum and ArrayMinimum ignore AS_SERIES flag on user arrays
- **Where:** [Mql5Runtime.Array.cs:478-511](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Array.cs#L478-L511)
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
    private int Extremum<T>(T[]? array, int start, int count, bool wantMaximum)
        where T : IComparable<T>
    {
        if (array is null || array.Length == 0)
        {
            SetError(Mql5ErrorCodes.InvalidArray);
            return -1;
        }
  ```
- **Failure:** A strategy populates a custom calculation array `highs = [1.1000, 1.1050, 1.1020]` (oldest at index 0) and sets `ArraySetAsSeries(highs, true)`. In MQL5, `ArrayMaximum(highs, 0, 1)` checks bar 0 (`1.1020`) and returns index `0`. In YO4X, `Extremum` ignores `IsSeriesArray(array)` and scans physical index `0` (`1.1000`, bar 2), returning physical index `0`. When evaluating multi-bar windows, it searches the oldest bars instead of the newest bars and returns raw physical offsets instead of bar offsets.
- **Fix:** In `Extremum`, inspect `IsSeriesArray(array)` and map `start`, `count`, and the returned index to logical series coordinates (`array.Length - 1 - index`).

### [P1] StringInit with character=0 clears string instead of allocating space-filled buffer
- **Where:** [Mql5Runtime.Text.cs:333-334](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Text.cs#L333-L334)
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        value = length == 0 || character == 0 ? string.Empty : new string((char)character, length);
        return true;
  ```
- **Failure:** A strategy prepares a fixed-width string buffer by invoking `StringInit(ref str, 32, 0)`. In MQL5, `character=0` with `length>0` specifies creating a string of the given length filled with space characters (`' '` / 0x20). YO4X sets `value = string.Empty` (length 0). Subsequent mutations via `StringSetCharacter(ref str, 5, 'A')` fail with `Mql5ErrorCodes.StringSmallLength` because `position > value.Length`, preventing comment and order tag generation.
- **Fix:** In `StringInit`, fill with `' '` (space) when `length > 0` and `character == 0`: `value = length == 0 ? string.Empty : new string(character == 0 ? ' ' : (char)character, length);`.

### [P1] ArrayCompare returns 0 (equal) on out-of-bounds start offsets
- **Where:** [Mql5Runtime.Array.cs:346-350](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Array.cs#L346-L350)
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        int left = Math.Max(0, first.Length - start1);
        int right = Math.Max(0, second.Length - start2);
        int span = count < 0 ? Math.Max(left, right) : count;

        for (int offset = 0; offset < span; offset++)
  ```
- **Failure:** Comparing two distinct arrays `first = [1.0, 2.0]` and `second = [3.0, 4.0]` with `start1 = 10, start2 = 10, count = -1` computes `left = 0`, `right = 0`, `span = 0`. The loop is skipped and `ArrayCompare` returns `0` (indicating identical arrays). In MQL5, start indices exceeding array bounds must return `-2` and record an index error. Strategy guard logic checking if buffers differ assumes equality and skips required recalculations.
- **Fix:** Add boundary validation before computing spans: `if (start1 >= first.Length || start2 >= second.Length) { SetError(Mql5ErrorCodes.InvalidArray); return -2; }`.

### [P1] StringFind clamps negative startPosition to 0 instead of returning -1
- **Where:** [Mql5Runtime.Text.cs:142-146](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Text.cs#L142-L146)
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        int start = startPosition < 0 ? 0 : startPosition;
        if (start > value.Length)
        {
            return -1;
        }
  ```
- **Failure:** Calling `StringFind("EURUSD", "USD", -5)` clamps `start` to `0` and returns `3`. In MQL5, a negative `start_pos` is invalid and returns `-1`. The silent clamping masks negative index computation bugs in strategies and parses substring tokens at unexpected positions.
- **Fix:** Check `if (startPosition < 0) return -1;` before computing search positions.

### [P2] ArraySort executes in place on AS_SERIES arrays
- **Where:** [Mql5Runtime.Array.cs:236-239](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Array.cs#L236-L239)
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        if (array.Length > 1)
        {
            Array.Sort(array);
        }
  ```
- **Failure:** In MQL5, `ArraySort` cannot be applied to arrays with `AS_SERIES` set and returns `false` with error `4053` (`ERR_FUNCTION_NOT_ALLOWED`). In YO4X, `ArraySort` sorts the timeseries array in ascending physical order, corrupting the reverse-chronological sequence required by indicators and returning `true`.
- **Fix:** Add a check `if (IsSeriesArray(array)) { SetError(Mql5ErrorCodes.SeriesArray); return false; }` at the start of `ArraySort`.

### [P2] ArrayReverse returns false on empty arrays without error code
- **Where:** [Mql5Runtime.Array.cs:320-323](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Array.cs#L320-L323)
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        if (start >= (uint)array.Length)
        {
            return false;
        }
```
- **Failure:** Passing an allocated 0-length array `[]` to `ArrayReverse(arr, 0)` hits `0 >= 0` and returns `false`. Reversing an empty range is a valid no-op in MQL5 that returns `true`.
- **Fix:** Check `if (array.Length == 0 && start == 0) return true;` before the range check.

### [P2] ArrayIsSeries returns true for custom arrays marked with ArraySetAsSeries
- **Where:** [Mql5Runtime.Array.cs:303](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Array.cs#L303)
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
    public bool ArrayIsSeries<T>(T[]? array) => IsSeriesArray(array);
  ```
- **Failure:** In MQL5, `ArrayIsSeries` checks if an array is a predefined engine timeseries buffer (`Open[]`, `Close[]`), returning `false` for user dynamic arrays that have `ArraySetAsSeries(arr, true)`. In YO4X, `ArrayIsSeries` aliases `IsSeriesArray`, returning `true` for all user-flagged arrays and causing indicators to misidentify custom buffers as chart feeds.
- **Fix:** Track predefined system timeseries buffers separately from custom user arrays flagged with `ArraySetAsSeries`.

## Referrals
- [Mql5Runtime.MarketData.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.MarketData.cs) — `CopyBuffer`/`CopyRates` relies on physical array reversal for `IsSeriesArray`, which diverges when arrays are manipulated by other runtime array methods.
- [Mql5Runtime.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.cs) — `seriesFlags` in `ConditionalWeakTable<object, SeriesFlag>` tracks object references rather than array variable identity, causing `ArraySwap` to swap series flags along with buffers.

## Coverage gaps
- `Mql5Runtime.Array.cs:183-188` — No unit tests cover `ArrayCopy` when the destination is flagged as series and reallocates, asserting `ArrayGetAsSeries(destination)` remains `true`.
- `Mql5Runtime.Array.cs:403-408` — No unit tests cover `ArrayInsert` on series arrays or verify series flag retention after insertion.
- `Mql5Runtime.Array.cs:478-511` — No unit tests cover `ArrayMaximum` / `ArrayMinimum` on user-constructed series arrays where indexing must be calculated from the newest bar.
- `Mql5Runtime.Text.cs:333` — No unit tests verify `StringInit(ref str, 10, 0)` with `character=0` produces a space-filled buffer.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 133.8s | 173397 tok | id=986681eb-9b64-40d3-954d-edf5b5d36734
