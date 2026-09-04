You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Frontend/YO4X.Web/src/api/contracts.ts

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (9):

[1] [P1] `botMagicNumberBound` in `decodeBotSettingsView` rejects valid unsigned 32-bit and 64-bit magic numbers
    Where:   src/Frontend/YO4X.Web/src/api/contracts.ts:1940
    Failure: In MetaTrader 5 and PostgreSQL (`010_bot_settings_and_broker_symbols.sql:40`), `magic_number` is a 64-bit unsigned `bigint check (magic_number >= 0)` (C# `long`). Many MetaTrader EAs use hash codes or 32-bit unsigned integers (e.g. `2_271_560_481` or `3_000_000_000`). The backend `RequireMagicNumber` accepts any non-negative integer. When a bot is configured with a magic number greater than `2,147,483,647`, `decodeBotSettingsView` throws `ContractViolationError('BotSettingsView')` at line 1989, crashing the Bot Settings dialog.
    Suggested fix: Change `botMagicNumberBound` from `2_147_483_647` to `Number.MAX_SAFE_INTEGER` (`9_007_199_254_740_991`).

[2] [P1] `decodeBacktestDetailView` rejects historical backtests emitting `"UNSPECIFIED"` model
    Where:   src/Frontend/YO4X.Web/src/api/contracts.ts:1863
    Failure: For historical backtests recorded before migration 006 where `simulation.backtests.model` is `NULL`, `PostgresFrontendProjections.cs:1264` returns `model: "UNSPECIFIED"` via `UnspecifiedMarker`. Because `backtestModels` only contains `['EVERY_TICK_REAL', 'EVERY_TICK_M1', 'OHLC_M1', 'OPEN_PRICES']`, `enumField` throws `ContractViolationError('BacktestDetailView')`, crashing the Backtest Detail page whenever an operator views a pre-migration-006 backtest.
    Suggested fix: Add `'UNSPECIFIED'` to `backtestModels` (and `BacktestModel` type definition) so fallback server values pass runtime validation.

[3] [P1] `decodeStrategyInputView` rejects legitimate enum inputs declaring no local members
    Where:   src/Frontend/YO4X.Web/src/api/contracts.ts:1685-1688
    Failure: When an MQL5 strategy declares an `input` using a standard built-in enumeration (e.g. `ENUM_TIMEFRAMES`, `ENUM_APPLIED_PRICE`) or an unextracted header enum, the backend (`PostgresFrontendProjections.cs:2263-2278`) returns `valueKind: "ENUM"`, `enumTypeName: "..."`, and `enumMembers: []` as designed in `FrontendProjectionContracts.cs:248-251`. `decodeStrategyInputView` unconditionally asserts `enumMembers.length === 0` is invalid and throws `ContractViolationError('StrategyInputView')`. This fails `decodeStrategyInputsView` and `decodeBotSettingsView`, breaking the New Backtest modal and Bot Settings modal for any strategy containing standard MT5 enum parameters.
    Suggested fix: Remove `enumMembers.length === 0` from the violation check in `decodeStrategyInputView`, and only call `requireUniqueIdentities` when `enumMembers.length > 0`.

[4] [P1] `decodeBacktestDetailView` rejects historical backtests carrying `"UNSPECIFIED"` model
    Where:   src/Frontend/YO4X.Web/src/api/contracts.ts:1863
    Failure: When an operator views a historical backtest created prior to migration 006 where `simulation.backtests.model` is `NULL`, `PostgresFrontendProjections` intentionally emits `model: "UNSPECIFIED"` (defined as `UnspecifiedMarker = "UNSPECIFIED"` on line 104). The frontend decoder `decodeBacktestDetailView` validates `model` with `enumField(..., backtestModels, ...)` where `backtestModels` only contains `['EVERY_TICK_REAL', 'EVERY_TICK_M1', 'OHLC_M1', 'OPEN_PRICES']`. The decoder throws `ContractViolationError('BacktestDetailView')`, completely breaking the backtest detail UI for pre-migration-006 records.
    Suggested fix: Include `'UNSPECIFIED'` in `backtestModels` / `BacktestModel` in `src/Frontend/YO4X.Web/src/api/contracts.ts` or allow string fallback for legacy backtests.

[5] [P1] `decodeStrategyInputView` rejects enum inputs with empty enum member declarations
    Where:   src/Frontend/YO4X.Web/src/api/contracts.ts:1685-1688
    Failure: When an MQL5 strategy declares an input using a standard library or external enum (such as `ENUM_TIMEFRAMES` or `ENUM_APPLIED_PRICE`) whose members are not declared inside `catalog.strategy_enum_members`, the backend projection emits `valueKind: "ENUM"`, `enumTypeName: "<name>"`, and `enumMembers: []` (as explicitly documented in `PostgresFrontendProjections.cs:2218-2220`). The frontend decoder `decodeStrategyInputView` unconditionally rejects enum inputs with `enumMembers.length === 0` by throwing `ContractViolationError('StrategyInputView')`, causing strategy inputs dialogs and bot settings dialogs to fail to load for any strategy using standard MQL5 enums.
    Suggested fix: Remove `|| enumMembers.length === 0` from `src/Frontend/YO4X.Web/src/api/contracts.ts:1686` so that enum inputs without declared members are valid representations.

[6] [P2] `decodeCloudPlanViews` tag length bound of 64 rejects valid 100-character database tags
    Where:   src/Frontend/YO4X.Web/src/api/contracts.ts:1374
    Failure: In PostgreSQL (`005_frontend_projections.sql:256`), `billing.cloud_plans.tag` is defined with `check (length(btrim(tag)) between 1 and 100)`. If a cloud plan carries a tag between 65 and 100 characters in length, `nullableBoundedStringField` throws `ContractViolationError('CloudPlanView')`, causing the Cloud Plans listing page to fail.
    Suggested fix: Update the maximum length argument from `64` to `100` in `decodeCloudPlanViews`.

[7] [P2] `decodeJournalPage` rejects valid trades where `botId` is set but `botName` is null
    Where:   src/Frontend/YO4X.Web/src/api/contracts.ts:1430-1433
    Failure: The backend journal query (`PostgresFrontendProjections.cs:1767-1800`) executes a `left join bots.bots as bot on ... bot.id = trade.bot_id`. If a bot was removed after executing trades, `trade.bot_id` is present but `bot.name` is `NULL`. The backend returns `{ botId: "...", botName: null }`. `decodeJournalPage` enforces `(botId === null) === (botName === null)`, evaluates `(false !== true)` as true, and throws `ContractViolationError('JournalEntryView')`. This crashes the Trade Journal page for any user who traded with a bot that was later deleted.
    Suggested fix: Remove `(botId === null) !== (botName === null)` and allow `botName` to be null when `botId` is present.

[8] [P2] Web frontend restricts EA magic numbers to signed 32-bit `2_147_483_647` instead of `ulong`
    Where:   src/Frontend/YO4X.Web/src/api/contracts.ts:1940
    Failure: In MetaTrader 5, `magic_number` is a 64-bit unsigned integer (`ulong`), frequently constructed from hash codes (e.g. `0x87654321` = `2,271,560,481`). Inputting any magic number greater than `2,147,483,647` into the bot settings form triggers validation failure (`"Enter a whole magic number between 0 and 2147483647"`), preventing users from configuring valid EAs.
    Suggested fix: Update `botMagicNumberBound` to `Number.MAX_SAFE_INTEGER` (`9_007_199_254_740_991`) in frontend contracts and form validation.

[9] [P3] Declared TypeScript interface type drift vs runtime enum validation in `BrokerAccountView` and `BacktestDetailView`
    Where:   src/Frontend/YO4X.Web/src/api/contracts.ts:59
    Failure: `BrokerAccountView.capabilityState` and `BacktestDetailView.model` are declared as loose primitive `string` types in TypeScript interfaces (lines 59 and 1584), but their runtime decoders (lines 471 and 1863) enforce strict closed enum sets (`capabilityStates` and `backtestModels`). Consumers of the TypeScript interfaces receive no compile-time typing for permitted values.
    Suggested fix: Declare explicit union types for `CapabilityState` and `BacktestModel` and use them in `BrokerAccountView` and `BacktestDetailView`.

HOW TO WORK:

1. Verify each finding against the actual code BEFORE changing anything. Line numbers may
   have shifted. If a finding is WRONG, or was already fixed, or the suggested fix would
   itself introduce a bug - do NOT apply it. Say so in your summary and move on. A refused
   bad fix is a good outcome; applying a wrong fix to a trading system is not.

2. Make the SMALLEST change that actually fixes the defect. Do not refactor, rename,
   reorder, reformat, restyle, or "improve" anything you were not asked about. Do not
   reflow existing lines. The diff must contain only the fix.

3. Match the surrounding code exactly - its naming, its comment density and voice, its
   error-handling idiom, its use of existing helpers. Read enough of the file to know what
   that is. Where the file already has a helper for what you need, use it rather than
   writing a new one.

4. Preserve public API and behaviour that was not identified as defective. If a correct
   fix would require changing a public signature, a database schema, a serialised contract,
   or shared behaviour outside this file, DO NOT do it - report it as needing a wider
   change instead.

5. This code decides real trades. For anything touching money, volume, price, margin, order
   state or time: be conservative, prefer failing closed over guessing, and preserve
   existing rounding/normalisation conventions unless the finding is specifically that the
   convention is wrong.

6. The project builds clean with zero warnings. Keep it that way - no unused variables, no
   unreachable code, no nullable warnings.

AFTER EDITING, output a short plain-text summary (no code fences), one line per finding:
  [n] APPLIED  - <what you changed, in a few words>
  [n] SKIPPED  - <why the finding was wrong or the fix unsafe>
Then a final line: FILES CHANGED: <the one path you edited, or NONE>

