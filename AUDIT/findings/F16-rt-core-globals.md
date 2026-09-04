---
agent_id: F16
lane: rt-core-globals
scope:
  - src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.cs
  - src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Globals.cs
  - src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Refused.cs
  - src/Runtime/YO4X.Mql5.Runtime/Mql5RuntimeOptions.cs
  - src/Runtime/YO4X.Mql5.Runtime/Mql5ZeroedInstance.cs
  - src/Runtime/YO4X.Mql5.Runtime/Mql5TypeInfo.cs
  - src/Runtime/YO4X.Mql5.Runtime/Mql5Structs.cs
status: COMPLETE
generated: 2026-08-29T11:23:00Z
counts: { P0: 0, P1: 1, P2: 0, P3: 0 }
---

# F16 — rt-core-globals

## Scope audited

- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.cs` (133 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Globals.cs` (354 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Refused.cs` (259 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5RuntimeOptions.cs` (43 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5ZeroedInstance.cs` (48 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5TypeInfo.cs` (189 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Structs.cs` (200 lines)

## Verdict

The core runtime sandbox, terminal globals, and security refusal boundaries are solidly structured and strictly isolated. The 34 file and directory operations in `Mql5Runtime.Refused.cs` fail loudly by throwing `Mql5UnsupportedOperationException` rather than returning benign default values, preventing silent sandbox escapes. Terminal global variables are strictly scoped to individual `Mql5Runtime` instances per strategy run with simulated clock timestamps, ensuring deterministic replay without cross-strategy contamination or host process state leakage. However, one semantic fidelity bug was identified: `Mql5Time.ToStruct` populates `Mql5DateTime.DayOfYear` with .NET's 1-based `DateTime.DayOfYear` instead of MQL5's 0-based specification (0 to 365), causing an off-by-one divergence for calendar day-of-year calculations.

## Findings

### [P1] Mql5Time.ToStruct sets 1-based DayOfYear diverging from MQL5 0-based specification
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/Mql5Structs.cs:177`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
              DayOfWeek = (int)moment.DayOfWeek,
              DayOfYear = moment.DayOfYear
  ```
- **Failure:** MQL5 documentation for `struct MqlDateTime` explicitly defines `day_of_year` as 0-indexed: `"Day of the year (0 - 365, 0 corresponds to January 1 of the current year)"`. In contrast, .NET's `DateTime.DayOfYear` is 1-indexed (January 1 is `1`). `Mql5Time.ToStruct` copies `moment.DayOfYear` directly into `Mql5DateTime.DayOfYear` (reinforced by an erroneous doc comment on line 33 claiming MQL5 is 1-based). Consequently, converted MQL5 strategies calling `TimeToStruct` or `TimeCurrent(MqlDateTime&)` on January 1 receive `DayOfYear = 1` instead of `0`. Seasonal filters, periodic rebalancing rules, or holiday calculations checking `day_of_year` evaluate off by one day.
- **Fix:** Subtract 1 from `moment.DayOfYear` when assigning `target.DayOfYear` in `Mql5Time.ToStruct`: `DayOfYear = moment.DayOfYear - 1`.

## Referrals

- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Terminal.cs:323` — `TerminalInfoInteger` throws `Mql5UnsupportedOperationException` for all unlisted property IDs rather than distinguishing between queryable terminal status properties and unsupported machine diagnostics.

## Coverage gaps

- `Mql5Time.ToStruct` (`src/Runtime/YO4X.Mql5.Runtime/Mql5Structs.cs:165`): Missing test asserting that `Mql5DateTime.DayOfYear` evaluates to `0` for January 1 timestamps.
- `Mql5ZeroedInstance<T>` (`src/Runtime/YO4X.Mql5.Runtime/Mql5ZeroedInstance.cs:24`): Missing unit test verifying that `Mql5UnsupportedOperationException` is thrown when attempting to zero an abstract class or interface type.
- `Mql5Runtime.GlobalVariablesDeleteAll` (`src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Globals.cs:295`): Missing test coverage for deleting global variables with combined name prefix and non-zero `limitData` timestamp filters.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 104.1s | 270823 tok | id=8e73bbdc-2c54-426d-9956-e37b7ff80758
