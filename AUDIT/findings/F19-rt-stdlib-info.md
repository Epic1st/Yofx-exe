---
agent_id: F19
lane: standard-library-info-wrappers
scope:
  - src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5AccountInfo.cs
  - src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5SymbolInfo.cs
  - src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5PositionInfo.cs
  - src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5OrderInfo.cs
  - src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5DealInfo.cs
  - src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5HistoryOrderInfo.cs
status: COMPLETE
generated: 2026-08-29T11:28:00Z
counts: { P0: 0, P1: 1, P2: 0, P3: 0 }
---

# F19 — standard-library-info-wrappers

## Scope audited
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5AccountInfo.cs` (228 lines)
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5SymbolInfo.cs` (246 lines)
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5PositionInfo.cs` (145 lines)
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5OrderInfo.cs` (125 lines)
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5DealInfo.cs` (129 lines)
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5HistoryOrderInfo.cs` (109 lines)

## Verdict
The standard library information wrappers are largely clean, correctly structured, and faithful to MQL5 semantics. Selection state is strictly per-instance and delegates directly to the underlying `IMql5Runtime` rather than caching stale entity data across queries, preventing stale trade reads upon selection failure. Property ID constants match the official MQL5 property enumerations across all classes. One P1 defect was identified in `Mql5AccountInfo.MaxLotCheck`, which incorrectly rejects fractional risk percentages under 1.0% (such as 0.5% risk).

## Findings

### [P1] MaxLotCheck rejects valid fractional margin percentages under 1.0%
- **Where:** `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5AccountInfo.cs:118`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  if (string.IsNullOrEmpty(symbol) || price <= 0.0 || percent < 1.0 || percent > 100.0)
  {
      runtime.Print("CAccountInfo::MaxLotCheck invalid parameters");
      return 0.0;
  }
  ```
- **Failure:** When an EA or money management algorithm requests maximum lot sizing for a fractional percentage of free margin (e.g. `percent = 0.5` for 0.5% risk or `percent = 0.25` for 0.25% risk), `MaxLotCheck` evaluates `percent < 1.0` as true, prints `"CAccountInfo::MaxLotCheck invalid parameters"`, and returns `0.0` instead of computing the affordable volume.
- **Fix:** Change `percent < 1.0` to `percent <= 0.0` in [Mql5AccountInfo.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5AccountInfo.cs#L118) to allow valid risk percentages between 0 and 1.

## Referrals
None.

## Coverage gaps
- `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5AccountInfo.cs:118` — `MaxLotCheck` parameter boundary validation branch for fractional `percent` values in the range `(0.0, 1.0)`.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 114.7s | 208855 tok | id=d84ca6f1-1781-442f-9b83-a5567ae78095
