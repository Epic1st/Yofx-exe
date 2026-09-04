---
agent_id: A23
lane: fe-qa-scripts
scope:
  - src/Frontend/YO4X.Web/scripts/design-capture.mjs
  - src/Frontend/YO4X.Web/scripts/dom-probe.mjs
  - src/Frontend/YO4X.Web/scripts/interaction-check.mjs
  - src/Frontend/YO4X.Web/scripts/live-capture.mjs
  - src/Frontend/YO4X.Web/scripts/live-detail.mjs
  - src/Frontend/YO4X.Web/scripts/stub-api.mjs
  - src/Frontend/YO4X.Web/scripts/visual-qa.mjs
status: COMPLETE
generated: 2026-08-29T08:53:45Z
counts: { P0: 0, P1: 2, P2: 3, P3: 1 }
---

# A23 — fe-qa-scripts

## Scope audited
- `src/Frontend/YO4X.Web/scripts/design-capture.mjs` (311 lines) — Visual screenshot harness using in-memory stubbed API.
- `src/Frontend/YO4X.Web/scripts/dom-probe.mjs` (37 lines) — Strategy catalog DOM element probing script.
- `src/Frontend/YO4X.Web/scripts/interaction-check.mjs` (102 lines) — User interaction assertion script.
- `src/Frontend/YO4X.Web/scripts/live-capture.mjs` (85 lines) — Live backend authentication and multi-view screenshot capture.
- `src/Frontend/YO4X.Web/scripts/live-detail.mjs` (50 lines) — Strategy detail live capture script.
- `src/Frontend/YO4X.Web/scripts/stub-api.mjs` (248 lines) — Shared mock API router for Playwright context route interception.
- `src/Frontend/YO4X.Web/scripts/visual-qa.mjs` (198 lines) — Automated visual and fail-closed assertion harness.

## Verdict
The QA script suite contains solid Playwright infrastructure in `visual-qa.mjs` and `interaction-check.mjs`, but suffers from contract drift in `stub-api.mjs` and weak error gatekeeping in the capture/probe scripts. Specifically, `stub-api.mjs` emits invalid ISO calendar dates for bot uptime and incomplete `BrokerAccountRegistrationOption` payloads that violate `src/api/contracts.ts` runtime decoders, while `live-capture.mjs` and `dom-probe.mjs` log errors but exit 0 on failure.

## Findings

### [P1] `stub-api.mjs` returns `BrokerAccountRegistrationOption` payloads missing required contract fields
- **Where:** `src/Frontend/YO4X.Web/scripts/stub-api.mjs:160-162`
- **Confidence:** CONFIRMED
- **Code:**
  ```javascript
  '/v1/broker-account-registration-options': [
    { brokerProfileId: id(92), server: 'MetaQuotes-Demo', environment: 'DEMO' },
  ],
  ```
- **Failure:** When the frontend broker discovery hook `useBrokerAccountDiscovery` fetches `/v1/broker-account-registration-options`, `decodeBrokerAccountRegistrationOption` (`contracts.ts:494-525`) requires `directoryServerId` (nullable UUID string), `brokerCompany` (string <= 300 chars), and `approved` (boolean matching `brokerProfileId !== null`). Because all three fields are omitted in the stub response, the decoder throws `ContractViolationError('BrokerAccountRegistrationOption')`, forcing `useBrokerAccountDiscovery` into `status: 'error'` and preventing broker linking options from rendering during stubbed QA checks.
- **Fix:** Add `directoryServerId: id(93)`, `brokerCompany: 'MetaQuotes Software Corp.'`, and `approved: true` to the stub payload.

### [P1] Faulty date arithmetic in `stub-api.mjs` emits invalid calendar dates for `/v1/bots/uptime`
- **Where:** `src/Frontend/YO4X.Web/scripts/stub-api.mjs:85-90`
- **Confidence:** CONFIRMED
- **Code:**
  ```javascript
  samples: Array.from({ length: 28 }, (_, index) => ({
    ordinal: index,
    sampledOn: `2026-07-${String(28 + index).padStart(2, '0')}`.replace('2026-07-3', '2026-08-0'),
    uptimeRatio: index % 9 === 0 ? 0.62 : index % 5 === 0 ? 0.94 : 1,
    downtimeMinutes: index % 9 === 0 ? 540 : index % 5 === 0 ? 86 : 0,
  })),
  ```
- **Failure:** The string transformation generates invalid ISO calendar dates: for `index = 2`, `28 + 2 = 30` produces `'2026-08-00'` (day 0); for `index = 12..27`, `28 + 12 = 40..55`, producing unreplaced values `'2026-07-40'` through `'2026-07-55'`. When `BotsPage` invokes `client.getBotUptime(28)`, `dateOnlyField` in `contracts.ts:1060-1069` round-trips through `new Date(...)` and detects date rollover mismatches (`'2026-07-31' !== '2026-08-00'`, `'2026-08-09' !== '2026-07-40'`), throwing `ContractViolationError('BotUptimeProjection')`. The Uptime card on `BotsPage` fails to decode and cannot render its chart.
- **Fix:** Replace string replacement with UTC Date arithmetic (`new Date(Date.UTC(2026, 6, 28 + index)).toISOString().slice(0, 10)`).

### [P2] `live-capture.mjs` reports errors to stdout but exits 0, masking live runtime failures
- **Where:** `src/Frontend/YO4X.Web/scripts/live-capture.mjs:79-84`
- **Confidence:** CONFIRMED
- **Code:**
  ```javascript
  await browser.close();
  if (problems.length > 0) {
    console.log(`\n${problems.length} console/page errors:`);
    for (const problem of problems.slice(0, 10)) console.log(`  - ${problem}`);
  } else {
    console.log('\nNo console or page errors.');
  }
  ```
- **Failure:** When running `live-capture.mjs` against a live environment, uncaught page errors (e.g. unhandled exceptions, crashed bundles, or API 500s) are pushed into `problems`. While the errors are printed to stdout, the script never assigns `process.exitCode = 1` or throws. CI/CD test runners evaluating process return codes see exit code 0 and treat a broken run as passing.
- **Fix:** Set `process.exitCode = 1;` when `problems.length > 0`.

### [P2] `dom-probe.mjs` performs no assertions and unconditionally exits with code 0
- **Where:** `src/Frontend/YO4X.Web/scripts/dom-probe.mjs:26-36`
- **Confidence:** CONFIRMED
- **Code:**
  ```javascript
  console.log('cards        :', await page.locator('.card').count());
  console.log('chips        :', await page.locator('.chip').count());
  console.log('empty states :', await page.locator('.empty-state').count());
  console.log('skeletons    :', await page.locator('.skeleton').count());
  console.log('title        :', await page.locator('.page-title').first().innerText().catch(() => '(none)'));
  console.log('nav labels   :', JSON.stringify(await page.locator('.sidebar button').allInnerTexts()));
  const emptyText = await page.locator('.empty-state').first().innerText().catch(() => '');
  if (emptyText) console.log('empty text   :', emptyText.replace(/\s+/gu, ' ').slice(0, 160));
  console.log('errors       :', errors.length === 0 ? 'none' : errors.slice(0, 5));

  await browser.close();
  ```
- **Failure:** `dom-probe.mjs` only prints DOM element counts to console. If the strategy catalog renders 0 cards, if `errors` contains unhandled page errors, or if the page renders blank skeletons indefinitely, the script logs the values and exits 0 without failing or raising an alert.
- **Fix:** Add assertions verifying non-zero card counts and fail with `process.exitCode = 1` if `errors.length > 0` or essential elements are missing.

### [P2] `stub-api.mjs` greedy regular expression intercepts sub-routes under `/v1/catalog/strategies/`
- **Where:** `src/Frontend/YO4X.Web/scripts/stub-api.mjs:231`
- **Confidence:** CONFIRMED
- **Code:**
  ```javascript
  else if (/^\/v1\/catalog\/strategies\//u.test(pathname)) body = strategyDetail(url);
  ```
- **Failure:** The regex matches any path prefix beginning with `/v1/catalog/strategies/`, such as `/v1/catalog/strategies/:id/inputs`. Instead of returning a `StrategyInputsView` shape (`{ strategyId, strategyName, inputs }`), it serves a `StrategyDetailView` object. When `client.getStrategyInputs(strategyId)` decodes the response with `decodeStrategyInputsView`, it throws `ContractViolationError('StrategyInputsView')` due to missing `inputs` array, breaking any backtest configuration or strategy parameter editing flows.
- **Fix:** Change the matcher to exact strategy detail paths (`/^\/v1\/catalog\/strategies\/[0-9a-f-]{36}$/u`) and add an explicit route handler for `/inputs`.

### [P3] Hardcoded URLs and ports across QA scripts bypass environment configuration
- **Where:** `src/Frontend/YO4X.Web/scripts/dom-probe.mjs:23`, `src/Frontend/YO4X.Web/scripts/live-detail.mjs:26,37,41`, `src/Frontend/YO4X.Web/scripts/live-capture.mjs:48`
- **Confidence:** CONFIRMED
- **Code:**
  ```javascript
  // dom-probe.mjs:23
  await page.goto('http://127.0.0.1:4173/#strategies', { waitUntil: 'networkidle' });
  // live-detail.mjs:26, 37
  await page.goto('http://127.0.0.1:4173/', { waitUntil: 'networkidle' });
  await page.waitForURL((url) => url.origin === 'http://127.0.0.1:4173', { timeout: 30000 });
  ```
- **Failure:** While `visual-qa.mjs` and `interaction-check.mjs` read `process.env.YO4X_QA_URL`, `dom-probe.mjs` and `live-detail.mjs` hardcode `http://127.0.0.1:4173/`, and `live-capture.mjs` hardcodes port `7210` for authentication. When testing against alternative preview ports, containers, or staging hosts, these scripts attempt connections to the default local address and fail.
- **Fix:** Standardize base URL resolution to `process.env.YO4X_QA_URL ?? 'http://127.0.0.1:4173/'` across all scripts.

## Referrals
- `src/Frontend/YO4X.Web/src/api/contracts.ts` — `decodeStrategyInputView` rejects standard MetaTrader enums that declare empty local member sets.
- `src/Frontend/YO4X.Web/src/api/contracts.ts` — `decodeBacktestDetailView` rejects historical backtest records carrying the `"UNSPECIFIED"` model.

## Coverage gaps
- `src/Frontend/YO4X.Web/scripts/interaction-check.mjs:60-69` — Link Account dialog check tests only `[role="dialog"]` presence without asserting whether registration options or accounts successfully loaded vs failing in error state.
- `src/Frontend/YO4X.Web/scripts/design-capture.mjs:294-301` — Views iteration takes screenshots without verifying DOM readiness or data rendering, silently saving screenshots of blank or errored views if unhandled errors are not emitted to console.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 121.6s | 261614 tok | id=87a8193d-dee2-4c14-a056-751107be48ff
