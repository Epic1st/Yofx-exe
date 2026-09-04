---
agent_id: E05
lane: MQL5 Built-in Constants, Signatures & Catalog
scope:
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5BuiltinConstants.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5BuiltinSignatures.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5BuiltinCatalog.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5BuiltinRealConstants.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5PredefinedVariables.cs
status: COMPLETE
generated: 2026-08-29T11:26:00Z
counts: { P0: 0, P1: 1, P2: 0, P3: 1 }
---

# E05 — MQL5 Built-in Constants, Signatures & Catalog

## Scope audited
- [src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5BuiltinConstants.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5BuiltinConstants.cs) (2,049 lines)
- [src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5BuiltinSignatures.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5BuiltinSignatures.cs) (1,251 lines)
- [src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5BuiltinCatalog.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5BuiltinCatalog.cs) (272 lines)
- [src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5BuiltinRealConstants.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5BuiltinRealConstants.cs) (98 lines)
- [src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5PredefinedVariables.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5PredefinedVariables.cs) (89 lines)

Total lines reviewed: 3,759 lines.

## Verdict
The MQL5 built-in metadata catalog and signature definitions are exceptionally rigorous and comprehensive, accurately capturing sparse enumeration ordinals (e.g. non-sequential `ENUM_TIMEFRAMES`, `ENUM_TRADE_REQUEST_ACTIONS`, trade return codes, and color constants) as well as function signatures and parameter modes. One confirmed P1 defect was identified in `Mql5BuiltinConstants.cs` where `SYMBOL_CALC_MODE_EXCH_OPTIONS` is assigned an invalid duplicate ordinal (`34L` instead of `35L`), silently corrupting exchange option margin/calculation logic. One P3 quality defect was found where `Mql5BuiltinCatalog.IsKnown` omits `Mql5BuiltinRealConstants`, creating an unnecessary coupling dependency.

## Findings

### [P1] Duplicate/incorrect constant value for `SYMBOL_CALC_MODE_EXCH_OPTIONS`
- **Where:** [src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5BuiltinConstants.cs:1541-1543](file:///C:/Users/Dev23/Desktop/yo4x/src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5BuiltinConstants.cs#L1541-L1543)
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  C("SYMBOL_CALC_MODE_EXCH_FUTURES_FORTS", 34L, "ENUM_SYMBOL_CALC_MODE"),
  C("SYMBOL_CALC_MODE_EXCH_OPTIONS", 34L, "ENUM_SYMBOL_CALC_MODE"),
  C("SYMBOL_CALC_MODE_EXCH_OPTIONS_MARGIN", 36L, "ENUM_SYMBOL_CALC_MODE"),
  ```
- **Failure:** In the official MQL5 specification for `ENUM_SYMBOL_CALC_MODE`, `SYMBOL_CALC_MODE_EXCH_FUTURES_FORTS` is `34`, `SYMBOL_CALC_MODE_EXCH_OPTIONS` is `35`, and `SYMBOL_CALC_MODE_EXCH_OPTIONS_MARGIN` is `36`. In `Mql5MeasuredConstants`, `SYMBOL_CALC_MODE_EXCH_OPTIONS` is erroneously defined with ordinal `34L` (identical to `SYMBOL_CALC_MODE_EXCH_FUTURES_FORTS`). Any strategy checking `SymbolInfoInteger(symbol, SYMBOL_TRADE_CALC_MODE) == SYMBOL_CALC_MODE_EXCH_OPTIONS` or branching on option calculation modes will fold to 34, causing option symbols to fail equality comparisons and incorrectly branch into FORTS futures calculation logic.
- **Fix:** In `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5BuiltinConstants.cs:1542`, update the value from `34L` to `35L`: `C("SYMBOL_CALC_MODE_EXCH_OPTIONS", 35L, "ENUM_SYMBOL_CALC_MODE"),`.

### [P3] `Mql5BuiltinCatalog.IsKnown` omits `Mql5BuiltinRealConstants`
- **Where:** [src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5BuiltinCatalog.cs:234-240](file:///C:/Users/Dev23/Desktop/yo4x/src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5BuiltinCatalog.cs#L234-L240)
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public static bool IsKnown(string name)
  {
      ArgumentNullException.ThrowIfNull(name);
      return SignaturesByName.ContainsKey(name)
          || Mql5BuiltinConstants.IsKnown(name)
          || Mql5PredefinedVariables.IsKnown(name);
  }
  ```
- **Failure:** `Mql5BuiltinCatalog.IsKnown` checks `SignaturesByName`, `Mql5BuiltinConstants`, and `Mql5PredefinedVariables`, but omits `Mql5BuiltinRealConstants.IsKnown(name)`. While current double constants currently have null-valued stubs in `Mql5MeasuredConstants`, any new floating-point constant added to `Mql5BuiltinRealConstants` without a duplicate stub in `Mql5BuiltinConstants` will cause `Mql5BuiltinCatalog.IsKnown` to return `false`, causing the transpiler binder to reject valid MQL5 constants.
- **Fix:** Include `|| Mql5BuiltinRealConstants.IsKnown(name)` in the boolean expression of `Mql5BuiltinCatalog.IsKnown(string name)`.

## Referrals
None.

## Coverage gaps
None.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 111.8s | 286180 tok | id=d667e884-3ad9-4c2b-ab9a-2e6957b6a515
