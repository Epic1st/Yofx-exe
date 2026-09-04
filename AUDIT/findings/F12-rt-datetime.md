---
agent_id: F12
lane: rt-datetime
scope:
  - src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.DateTime.cs
  - src/Runtime/YO4X.Mql5.Runtime/Mql5CalendarTypes.cs
status: COMPLETE
generated: 2026-08-29T08:26:00Z
counts: { P0: 0, P1: 2, P2: 0, P3: 0 }
---

# F12 — rt-datetime

## Scope audited
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.DateTime.cs` (142 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5CalendarTypes.cs` (146 lines)

## Verdict
The date/time runtime surface and economic-calendar record structures are cleanly designed around an integer scalar representation (`long` seconds since 1970-01-01 epoch) and correctly delegate simulated clock reads to `IMql5MarketContext`. However, the datetime conversion layer contains two significant MQL5 parity bugs: `StructToTime` conflates the valid epoch date (`1970.01.01 00:00:00`) with conversion failure, corrupting the runtime error state, while returning `0` instead of `WRONG_VALUE` (`-1`) on invalid dates; and `TimeToStruct` unconditionally returns `true` regardless of negative or invalid inputs without error reporting.

## Findings

### [P1] StructToTime treats valid epoch (0) as an error and returns 0 instead of WRONG_VALUE (-1) on invalid dates
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.DateTime.cs:131`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public long StructToTime(in Mql5DateTime moment)
  {
      long value = Mql5Time.FromStruct(moment);
      if (value == 0)
      {
          SetError(Mql5ErrorCodes.InvalidDatetime);
      }

      return value;
  }
  ```
- **Failure:** In MQL5, `datetime` is a zero-based offset where `1970.01.01 00:00:00` has timestamp `0`, while conversion errors return `WRONG_VALUE` (`(datetime)-1`). When a strategy passes `{ Year = 1970, Month = 1, Day = 1, Hour = 0, Minute = 0, Second = 0 }`, `Mql5Time.FromStruct` returns `0`. `StructToTime` checks `if (value == 0)` and triggers `SetError(Mql5ErrorCodes.InvalidDatetime)`, setting `_lastError` to `ERR_INVALID_DATETIME` (4011) despite valid input. Conversely, when an invalid date is passed (e.g., month 13), `Mql5Time.FromStruct` catches `ArgumentOutOfRangeException` and returns `0`. `StructToTime` then returns `0` (which represents `1970.01.01 00:00:00`) instead of `-1`. Any strategy verifying conversion success with `if (StructToTime(broken) == WRONG_VALUE)` or comparing expiration timestamps will treat corrupted dates as valid January 1, 1970 timestamps.
- **Fix:** Update `Mql5Time.FromStruct` to return `-1` on validation failure, and update `StructToTime` to check `if (value == -1)` before calling `SetError(Mql5ErrorCodes.InvalidDatetime)` and returning `-1`.

### [P1] TimeToStruct unconditionally returns true and fails to detect negative or invalid timestamps
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.DateTime.cs:124`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public bool TimeToStruct(long value, out Mql5DateTime moment)
  {
      Mql5Time.ToStruct(value, out moment);
      return true;
  }
  ```
- **Failure:** In MQL5, `TimeToStruct(datetime dt, MqlDateTime& dt_struct)` returns `bool` (`true` on success, `false` on failure). When given a negative value such as `-1` (`WRONG_VALUE`, an uninitialized indicator buffer, or an invalid order open time), `Mql5Time.ToStruct` silently clamps `value <= 0` to `1970.01.01 00:00:00`, and `TimeToStruct` returns `true` without setting `_lastError`. A strategy using standard error checking `if (!TimeToStruct(order_time, dt)) { /* handle invalid order */ }` will never execute its error branch, falsely believing the order was executed at `1970.01.01 00:00:00`.
- **Fix:** Check `if (value < 0 || value > 253402300799L)` in `TimeToStruct`, set `SetError(Mql5ErrorCodes.InvalidDatetime)` and return `false` when out of range.

## Referrals
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Structs.cs:177` — `Mql5Time.ToStruct` copies .NET 1-based `DateTime.DayOfYear` (where Jan 1 is 1) into `Mql5DateTime.DayOfYear`, violating MQL5's 0-based specification (where Jan 1 is 0, range 0–365).
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Structs.cs:146` — `Mql5Time.FromDateTime` converts `DateTimeKind.Local` to UTC via `ToUniversalTime()`, causing `TimeLocal` to lose local timezone offsets.
- `src/Runtime/YO4X.Mql5.Runtime/IMql5MarketContext.cs:130` — Default interface implementations route `TimeLocal`, `TimeGmt`, and `TimeTradeServer` directly to `TimeCurrent`, collapsing distinct clock domains into a single timestamp.
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.MarketData.cs:160` — `CalendarEventById` signature is `bool CalendarEventById(long eventId)` instead of accepting `out Mql5CalendarEvent` as defined in `Mql5BuiltinSignatures.cs` and the MQL5 reference.

## Coverage gaps
- `Mql5Runtime.StructToTime` (`src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.DateTime.cs:131`): Missing unit tests converting epoch `1970.01.01 00:00:00` verifying that `GetLastError()` remains `0` (Success).
- `Mql5Runtime.StructToTime` (`src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.DateTime.cs:131`): Missing unit tests asserting that invalid `Mql5DateTime` inputs return `WRONG_VALUE` (`-1`) rather than `0`.
- `Mql5Runtime.TimeToStruct` (`src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.DateTime.cs:124`): Missing unit tests asserting that negative/invalid timestamp inputs return `false` and set `ERR_INVALID_DATETIME`.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 249.4s | 355618 tok | id=2698b026-4321-4899-9886-a51a1819cd4c
