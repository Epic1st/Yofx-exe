---
agent_id: A02
lane: api-contracts
scope:
  - src/Frontend/YO4X.Web/src/api/contracts.ts
status: COMPLETE
generated: 2026-08-29T08:44:45Z
counts: { P0: 0, P1: 3, P2: 2, P3: 1 }
---

# A02 — api-contracts

## Scope audited
- `src/Frontend/YO4X.Web/src/api/contracts.ts` (2058 lines) — Full review of runtime decoders, primitive type validators, boundary constraints, enum sets, null/optional handling, and declared TypeScript interfaces.

## Verdict
The contract layer is strictly written with robust defense-in-depth decoding, but contains critical contract mismatches where decoders reject legitimate server responses. Specifically, `decodeStrategyInputView` rejects enum inputs with empty member sets (standard MetaTrader enums), `decodeBacktestDetailView` rejects pre-migration backtests emitting `"UNSPECIFIED"` models, and `decodeBotSettingsView` rejects valid unsigned 32/64-bit magic numbers by capping at `int.MaxValue`.

## Findings

### [P1] `decodeStrategyInputView` rejects legitimate enum inputs declaring no local members
- **Where:** `src/Frontend/YO4X.Web/src/api/contracts.ts:1685-1688`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  if (valueKind === 'ENUM') {
    if (enumTypeName === null || enumTypeName.length === 0 || enumMembers.length === 0) {
      throw new ContractViolationError('StrategyInputView');
    }
    requireUniqueIdentities(enumMembers.map((member) => member.name), 'StrategyEnumMemberView[]');
  } else if (enumTypeName !== null || enumMembers.length > 0) {
  ```
- **Failure:** When an MQL5 strategy declares an `input` using a standard built-in enumeration (e.g. `ENUM_TIMEFRAMES`, `ENUM_APPLIED_PRICE`) or an unextracted header enum, the backend (`PostgresFrontendProjections.cs:2263-2278`) returns `valueKind: "ENUM"`, `enumTypeName: "..."`, and `enumMembers: []` as designed in `FrontendProjectionContracts.cs:248-251`. `decodeStrategyInputView` unconditionally asserts `enumMembers.length === 0` is invalid and throws `ContractViolationError('StrategyInputView')`. This fails `decodeStrategyInputsView` and `decodeBotSettingsView`, breaking the New Backtest modal and Bot Settings modal for any strategy containing standard MT5 enum parameters.
- **Fix:** Remove `enumMembers.length === 0` from the violation check in `decodeStrategyInputView`, and only call `requireUniqueIdentities` when `enumMembers.length > 0`.

### [P1] `decodeBacktestDetailView` rejects historical backtests emitting `"UNSPECIFIED"` model
- **Where:** `src/Frontend/YO4X.Web/src/api/contracts.ts:1863`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  model: enumField(source, 'model', backtestModels, 'BacktestDetailView'),
  ```
- **Failure:** For historical backtests recorded before migration 006 where `simulation.backtests.model` is `NULL`, `PostgresFrontendProjections.cs:1264` returns `model: "UNSPECIFIED"` via `UnspecifiedMarker`. Because `backtestModels` only contains `['EVERY_TICK_REAL', 'EVERY_TICK_M1', 'OHLC_M1', 'OPEN_PRICES']`, `enumField` throws `ContractViolationError('BacktestDetailView')`, crashing the Backtest Detail page whenever an operator views a pre-migration-006 backtest.
- **Fix:** Add `'UNSPECIFIED'` to `backtestModels` (and `BacktestModel` type definition) so fallback server values pass runtime validation.

### [P1] `botMagicNumberBound` in `decodeBotSettingsView` rejects valid unsigned 32-bit and 64-bit magic numbers
- **Where:** `src/Frontend/YO4X.Web/src/api/contracts.ts:1940`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  export const botMagicNumberBound = 2_147_483_647;
  ```
- **Failure:** In MetaTrader 5 and PostgreSQL (`010_bot_settings_and_broker_symbols.sql:40`), `magic_number` is a 64-bit unsigned `bigint check (magic_number >= 0)` (C# `long`). Many MetaTrader EAs use hash codes or 32-bit unsigned integers (e.g. `2_271_560_481` or `3_000_000_000`). The backend `RequireMagicNumber` accepts any non-negative integer. When a bot is configured with a magic number greater than `2,147,483,647`, `decodeBotSettingsView` throws `ContractViolationError('BotSettingsView')` at line 1989, crashing the Bot Settings dialog.
- **Fix:** Change `botMagicNumberBound` from `2_147_483_647` to `Number.MAX_SAFE_INTEGER` (`9_007_199_254_740_991`).

### [P2] `decodeJournalPage` rejects valid trades where `botId` is set but `botName` is null
- **Where:** `src/Frontend/YO4X.Web/src/api/contracts.ts:1430-1433`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  if ((botId === null) !== (botName === null)
    || (closedAt !== null && Date.parse(closedAt) < Date.parse(openedAt))) {
    throw new ContractViolationError('JournalEntryView');
  }
  ```
- **Failure:** The backend journal query (`PostgresFrontendProjections.cs:1767-1800`) executes a `left join bots.bots as bot on ... bot.id = trade.bot_id`. If a bot was removed after executing trades, `trade.bot_id` is present but `bot.name` is `NULL`. The backend returns `{ botId: "...", botName: null }`. `decodeJournalPage` enforces `(botId === null) === (botName === null)`, evaluates `(false !== true)` as true, and throws `ContractViolationError('JournalEntryView')`. This crashes the Trade Journal page for any user who traded with a bot that was later deleted.
- **Fix:** Remove `(botId === null) !== (botName === null)` and allow `botName` to be null when `botId` is present.

### [P2] `decodeCloudPlanViews` tag length bound of 64 rejects valid 100-character database tags
- **Where:** `src/Frontend/YO4X.Web/src/api/contracts.ts:1374`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  tag: nullableBoundedStringField(source, 'tag', 'CloudPlanView', 64),
  ```
- **Failure:** In PostgreSQL (`005_frontend_projections.sql:256`), `billing.cloud_plans.tag` is defined with `check (length(btrim(tag)) between 1 and 100)`. If a cloud plan carries a tag between 65 and 100 characters in length, `nullableBoundedStringField` throws `ContractViolationError('CloudPlanView')`, causing the Cloud Plans listing page to fail.
- **Fix:** Update the maximum length argument from `64` to `100` in `decodeCloudPlanViews`.

### [P3] Declared TypeScript interface type drift vs runtime enum validation in `BrokerAccountView` and `BacktestDetailView`
- **Where:** `src/Frontend/YO4X.Web/src/api/contracts.ts:59`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  export interface BrokerAccountView {
    readonly id: string;
    readonly brokerId: string;
    readonly server: string;
    readonly maskedLogin: string;
    readonly environment: BrokerAccountEnvironment;
    readonly accountMode: BrokerAccountMode | null;
    readonly capabilityState: string;
  ```
- **Failure:** `BrokerAccountView.capabilityState` and `BacktestDetailView.model` are declared as loose primitive `string` types in TypeScript interfaces (lines 59 and 1584), but their runtime decoders (lines 471 and 1863) enforce strict closed enum sets (`capabilityStates` and `backtestModels`). Consumers of the TypeScript interfaces receive no compile-time typing for permitted values.
- **Fix:** Declare explicit union types for `CapabilityState` and `BacktestModel` and use them in `BrokerAccountView` and `BacktestDetailView`.

## Referrals
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs:1264` — Emits `UnspecifiedMarker = "UNSPECIFIED"` for pre-migration-006 records, which triggers decoder failure unless the frontend contract is updated.
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs:2375` — `RequireMagicNumber` allows full 64-bit `long` magic numbers, highlighting the frontend's overly restrictive 32-bit signed integer boundary.

## Coverage gaps
- `src/Frontend/YO4X.Web/src/api/contracts.ts:1685` — Branch where `valueKind === 'ENUM'` and `enumMembers: []` is untested in `contracts.test.ts` (all test enum fixtures provide non-empty members).
- `src/Frontend/YO4X.Web/src/api/contracts.ts:1863` — Handling of `model: "UNSPECIFIED"` in `decodeBacktestDetailView` is untested.
- `src/Frontend/YO4X.Web/src/api/contracts.ts:1430` — Journal entry with non-null `botId` and `null` `botName` is untested.
- `src/Frontend/YO4X.Web/src/api/contracts.ts:1989` — Bot settings with `magicNumber > 2_147_483_647` is untested.
- `src/Frontend/YO4X.Web/src/api/contracts.ts:1374` — Cloud plan with `tag` length between 65 and 100 characters is untested.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 233.2s | 507521 tok | id=fffcaa92-0798-4a40-8f2a-9afd7286ab82
