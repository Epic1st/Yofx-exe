---
agent_id: B16
lane: contract-drift
scope:
  - src/Frontend/YO4X.Web/src/api/contracts.ts
  - src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs
  - src/Apps/YO4X.ControlPlane.Api/FrontendProjectionEndpoints.cs
status: COMPLETE
generated: 2026-08-29T08:25:30Z
counts: { P0: 0, P1: 2, P2: 0, P3: 0 }
---

# B16 — contract-drift

## Scope audited
- `src/Frontend/YO4X.Web/src/api/contracts.ts` (2058 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs` (2995 lines)
- `src/Apps/YO4X.ControlPlane.Api/FrontendProjectionEndpoints.cs` (308 lines)

## Verdict
The contract boundary between the TypeScript frontend and C# backend projections is mostly rigorous and well-aligned across naming, camelCase property mappings, numeric boundaries, and enum representations. However, two concrete P1 contract drift regressions exist where strict frontend decoders throw `ContractViolationError` on legitimate backend projection responses: (1) historical backtest records prior to migration 006 emitting the backend's `"UNSPECIFIED"` model marker, and (2) strategy enum inputs referencing standard or external MQL5 enums without local member declarations.

## Findings

### [P1] `decodeBacktestDetailView` rejects historical backtests carrying `"UNSPECIFIED"` model
- **Where:** `src/Frontend/YO4X.Web/src/api/contracts.ts:1863`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  model: enumField(source, 'model', backtestModels, 'BacktestDetailView'),
  ```
  vs backend `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs:1264`:
  ```csharp
  model = reader.IsDBNull(15) ? UnspecifiedMarker : reader.GetString(15);
  ```
- **Failure:** When an operator views a historical backtest created prior to migration 006 where `simulation.backtests.model` is `NULL`, `PostgresFrontendProjections` intentionally emits `model: "UNSPECIFIED"` (defined as `UnspecifiedMarker = "UNSPECIFIED"` on line 104). The frontend decoder `decodeBacktestDetailView` validates `model` with `enumField(..., backtestModels, ...)` where `backtestModels` only contains `['EVERY_TICK_REAL', 'EVERY_TICK_M1', 'OHLC_M1', 'OPEN_PRICES']`. The decoder throws `ContractViolationError('BacktestDetailView')`, completely breaking the backtest detail UI for pre-migration-006 records.
- **Fix:** Include `'UNSPECIFIED'` in `backtestModels` / `BacktestModel` in `src/Frontend/YO4X.Web/src/api/contracts.ts` or allow string fallback for legacy backtests.

### [P1] `decodeStrategyInputView` rejects enum inputs with empty enum member declarations
- **Where:** `src/Frontend/YO4X.Web/src/api/contracts.ts:1685-1688`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  if (valueKind === 'ENUM') {
    if (enumTypeName === null || enumTypeName.length === 0 || enumMembers.length === 0) {
      throw new ContractViolationError('StrategyInputView');
    }
  ```
  vs backend `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs:2263-2267`:
  ```csharp
  string? enumTypeName = reader.IsDBNull(7) ? null : reader.GetString(7);
  IReadOnlyList<StrategyEnumMemberView> declaredMembers =
      enumTypeName is not null
      && members.TryGetValue(enumTypeName, out List<StrategyEnumMemberView>? candidates)
          ? candidates.AsReadOnly()
          : [];
  ```
- **Failure:** When an MQL5 strategy declares an input using a standard library or external enum (such as `ENUM_TIMEFRAMES` or `ENUM_APPLIED_PRICE`) whose members are not declared inside `catalog.strategy_enum_members`, the backend projection emits `valueKind: "ENUM"`, `enumTypeName: "<name>"`, and `enumMembers: []` (as explicitly documented in `PostgresFrontendProjections.cs:2218-2220`). The frontend decoder `decodeStrategyInputView` unconditionally rejects enum inputs with `enumMembers.length === 0` by throwing `ContractViolationError('StrategyInputView')`, causing strategy inputs dialogs and bot settings dialogs to fail to load for any strategy using standard MQL5 enums.
- **Fix:** Remove `|| enumMembers.length === 0` from `src/Frontend/YO4X.Web/src/api/contracts.ts:1686` so that enum inputs without declared members are valid representations.

## Referrals
`src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5InventoryFormatter.cs:145` — Serializer uses camelCase for enums rather than API foundation's SnakeCaseUpper standard.

## Coverage gaps
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs:1264` — Branch emitting `UnspecifiedMarker` when `model` is null in `GetBacktestDetailAsync` is untested against `decodeBacktestDetailView`.
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs:2263-2267` — Branch setting `declaredMembers = []` for undeclared enum types in `LoadStrategyInputsAsync` is untested against `decodeStrategyInputView`.
