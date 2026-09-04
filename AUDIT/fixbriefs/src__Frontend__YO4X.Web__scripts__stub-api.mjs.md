You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Frontend/YO4X.Web/scripts/stub-api.mjs

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (3):

[1] [P1] Faulty date arithmetic in `stub-api.mjs` emits invalid calendar dates for `/v1/bots/uptime`
    Where:   src/Frontend/YO4X.Web/scripts/stub-api.mjs:85-90
    Failure: The string transformation generates invalid ISO calendar dates: for `index = 2`, `28 + 2 = 30` produces `'2026-08-00'` (day 0); for `index = 12..27`, `28 + 12 = 40..55`, producing unreplaced values `'2026-07-40'` through `'2026-07-55'`. When `BotsPage` invokes `client.getBotUptime(28)`, `dateOnlyField` in `contracts.ts:1060-1069` round-trips through `new Date(...)` and detects date rollover mismatches (`'2026-07-31' !== '2026-08-00'`, `'2026-08-09' !== '2026-07-40'`), throwing `ContractViolationError('BotUptimeProjection')`. The Uptime card on `BotsPage` fails to decode and cannot render its chart.
    Suggested fix: Replace string replacement with UTC Date arithmetic (`new Date(Date.UTC(2026, 6, 28 + index)).toISOString().slice(0, 10)`).

[2] [P1] `stub-api.mjs` returns `BrokerAccountRegistrationOption` payloads missing required contract fields
    Where:   src/Frontend/YO4X.Web/scripts/stub-api.mjs:160-162
    Failure: When the frontend broker discovery hook `useBrokerAccountDiscovery` fetches `/v1/broker-account-registration-options`, `decodeBrokerAccountRegistrationOption` (`contracts.ts:494-525`) requires `directoryServerId` (nullable UUID string), `brokerCompany` (string <= 300 chars), and `approved` (boolean matching `brokerProfileId !== null`). Because all three fields are omitted in the stub response, the decoder throws `ContractViolationError('BrokerAccountRegistrationOption')`, forcing `useBrokerAccountDiscovery` into `status: 'error'` and preventing broker linking options from rendering during stubbed QA checks.
    Suggested fix: Add `directoryServerId: id(93)`, `brokerCompany: 'MetaQuotes Software Corp.'`, and `approved: true` to the stub payload.

[3] [P2] `stub-api.mjs` greedy regular expression intercepts sub-routes under `/v1/catalog/strategies/`
    Where:   src/Frontend/YO4X.Web/scripts/stub-api.mjs:231
    Failure: The regex matches any path prefix beginning with `/v1/catalog/strategies/`, such as `/v1/catalog/strategies/:id/inputs`. Instead of returning a `StrategyInputsView` shape (`{ strategyId, strategyName, inputs }`), it serves a `StrategyDetailView` object. When `client.getStrategyInputs(strategyId)` decodes the response with `decodeStrategyInputsView`, it throws `ContractViolationError('StrategyInputsView')` due to missing `inputs` array, breaking any backtest configuration or strategy parameter editing flows.
    Suggested fix: Change the matcher to exact strategy detail paths (`/^\/v1\/catalog\/strategies\/[0-9a-f-]{36}$/u`) and add an explicit route handler for `/inputs`.

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

