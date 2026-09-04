---
agent_id: A22
lane: fe-tests
scope:
  - src/Frontend/YO4X.Web/src/api/contracts.test.ts
  - src/Frontend/YO4X.Web/src/api/controlPlaneClient.test.ts
  - src/Frontend/YO4X.Web/src/api/safeUrl.test.ts
  - src/Frontend/YO4X.Web/src/app/config/runtimeConfig.test.ts
  - src/Frontend/YO4X.Web/src/auth/AuthEntry.test.tsx
  - src/Frontend/YO4X.Web/src/auth/developmentOidc.test.ts
  - src/Frontend/YO4X.Web/src/features/backtests/backtestForm.test.ts
  - src/Frontend/YO4X.Web/src/features/backtests/backtests.test.tsx
  - src/Frontend/YO4X.Web/src/features/bots/BotSettingsModal.test.tsx
  - src/Frontend/YO4X.Web/src/features/bots/BotsPage.test.tsx
  - src/Frontend/YO4X.Web/src/features/broker-accounts/brokerRegistration.test.ts
  - src/Frontend/YO4X.Web/src/features/overlays/LinkAccountModal.test.tsx
  - src/Frontend/YO4X.Web/src/tests/setup.ts
status: COMPLETE
generated: 2026-08-29T08:55:00Z
counts: { P0: 0, P1: 0, P2: 2, P3: 2 }
---

# A22 — fe-tests

## Scope audited
- `src/Frontend/YO4X.Web/src/tests/setup.ts` (12 lines) — test environment lifecycle and global cleanup.
- `src/Frontend/YO4X.Web/src/api/safeUrl.test.ts` (113 lines) — origin parsing, same-origin path resolution, and URL length boundary tests.
- `src/Frontend/YO4X.Web/src/app/config/runtimeConfig.test.ts` (135 lines) — environment variable validation, loopback guards, and default fallbacks.
- `src/Frontend/YO4X.Web/src/auth/developmentOidc.test.ts` (198 lines) — PKCE settings, token expiration, session restore, and callback state handling.
- `src/Frontend/YO4X.Web/src/auth/AuthEntry.test.tsx` (63 lines) — auth entry action triggers, credential leakage prevention, and pending/error states.
- `src/Frontend/YO4X.Web/src/features/backtests/backtestForm.test.ts` (304 lines) — form field parsing, client validation, request assembly, and RFC 7807 error placement.
- `src/Frontend/YO4X.Web/src/features/backtests/backtests.test.tsx` (427 lines) — backtest list/detail components, creation modal integration, and equity curve rendering.
- `src/Frontend/YO4X.Web/src/features/bots/BotSettingsModal.test.tsx` (285 lines) — bot settings modal, override calculation, symbol searching, and read-only running state.
- `src/Frontend/YO4X.Web/src/features/bots/BotsPage.test.tsx` (60 lines) — bots list action buttons and modal invocation wiring.
- `src/Frontend/YO4X.Web/src/features/broker-accounts/brokerRegistration.test.ts` (67 lines) — binding derivation, SHA-256 fingerprinting, and login validation.
- `src/Frontend/YO4X.Web/src/features/overlays/LinkAccountModal.test.tsx` (244 lines) — broker server discovery, approval workflow, password masking, and security invariants.
- `src/Frontend/YO4X.Web/src/api/contracts.test.ts` (1067 lines) — contract decoding, boundary checks, duplicate rejection, and schema validation.
- `src/Frontend/YO4X.Web/src/api/controlPlaneClient.test.ts` (1106 lines) — HTTP client routing, header composition, payload serialization, and token deduplication.

## Verdict
The frontend test suites demonstrate high discipline and strict contract verification, particularly around sensitive financial inputs, URL security invariants, token memory isolation, and member-by-member payload assembly. However, several test suites rely on uniformly successful default mocks that leave UI error handling branches unasserted (such as broker account linking failures), test only server-side HTTP 422 rejections while skipping client-side risk validation, and leave several control plane endpoints and schema decoders without test coverage.

## Findings

### [P2] LinkAccountModal test suite never exercises submission failure or rejection branches
- **Where:** `src/Frontend/YO4X.Web/src/features/overlays/LinkAccountModal.test.tsx:49`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  function renderModal(
    client: ControlPlaneClient,
    onSubmit = vi.fn((
      _login: string,
      _option: BrokerAccountRegistrationOption,
      _password: string,
    ) => Promise.resolve(true)),
  )
  ```
- **Failure:** In `LinkAccountModal.tsx:180-194`, `submit` handles two distinct failure modes: (1) `onSubmit` returning `false` (which sets the error message `"The account was not linked. Check the login and password, then try again."`), and (2) `onSubmit` rejecting with an exception (which catches `linkError` and displays its message). `LinkAccountModal.test.tsx` configures `onSubmit` to unconditionally resolve `true` across all test cases. If a regression breaks error state rendering, clears credentials prematurely on failure, or causes exceptions during linking to go unhandled, the test suite remains green despite the broken failure path.
- **Fix:** Add test cases in `LinkAccountModal.test.tsx` verifying that `onSubmit` resolving `false` renders the retry error message while preserving the dialog state, and `onSubmit` rejecting with an `Error` renders the thrown error message.

### [P2] Development OIDC bridge tests never exercise account creation branch on beginLogin
- **Where:** `src/Frontend/YO4X.Web/src/auth/developmentOidc.test.ts:99`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
    await window.__YO4X_AUTH__!.beginLogin!('sign-in');

    expect(signinRedirect).toHaveBeenCalledWith();
  ```
- **Failure:** In `developmentOidc.ts:110-116`, `beginLogin` has two distinct branches: `intent === 'sign-in'` (calling `manager.signinRedirect()`) and `intent === 'create-account'` (calling `createAuthorizationRequest(settings)` and `window.location.assign(createRegistrationUrl(request.url, config.authority))`). `developmentOidc.test.ts` only invokes `beginLogin('sign-in')` and tests `createRegistrationUrl` in isolation. If `beginLogin` fails to invoke `createAuthorizationRequest`, fails to construct the registration URL, or encounters a runtime error, clicking "Create account" on the authentication entry page fails silently, but the test suite remains green.
- **Fix:** Add a test in `developmentOidc.test.ts` calling `window.__YO4X_AUTH__.beginLogin('create-account')` and asserting that `createAuthorizationRequest` is executed with the bridge settings and `window.location.assign` is called with the resolved registration URL.

### [P3] BotSettingsModal tests mock server 422 errors instead of asserting client-side volume bounds
- **Where:** `src/Frontend/YO4X.Web/src/features/bots/BotSettingsModal.test.tsx:251`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
    const updateBotSettings = vi.fn(() => Promise.reject(new ApiProblemError({
      status: 422,
      title: 'The bot settings were rejected.',
      errors: [{ path: '$.volume', code: 'INVALID', message: 'Below the broker minimum.' }],
    })));
  ```
- **Failure:** `BotSettingsModal.tsx` and `botSettingsForm.ts:190-194` implement client-side validation (`validateRunSettings`) against instrument broker limits (`volume < instrument.volumeMin` and `volume > instrument.volumeMax`). The test suite never asserts that entering a volume outside broker limits is blocked client-side before calling the API; instead, lines 250-268 mock a server-side 422 error response. If client-side volume bounding is broken or omitted, a user could submit invalid lot sizes to the backend without any client-side block, yet the test suite would pass.
- **Fix:** Add a test in `BotSettingsModal.test.tsx` verifying that entering a volume below `volumeMin` (e.g., `0.005` when `volumeMin` is `0.01`) renders a client-side validation error and prevents `updateBotSettings` from being invoked.

### [P3] Broker symbol test fixtures contain unescaped backslashes in instrument path strings
- **Where:** `src/Frontend/YO4X.Web/src/api/contracts.test.ts:992`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
      volumeMax: 500,
      volumeStep: 0.01,
      path: 'Forex\Majors',
      ...changes,
  ```
- **Failure:** In JavaScript string literal syntax, `\M` in `'Forex\Majors'` is an unrecognized escape sequence that evaluates to `'ForexMajors'` instead of `'Forex\Majors'`. In `contracts.test.ts:992` (and `controlPlaneClient.test.ts:1042`), the test fixture string does not contain an actual backslash. Consequently, the tests fail to verify whether symbol decoders correctly preserve backslashes in MT5 directory paths (e.g. `Forex\Majors`).
- **Fix:** Replace `'Forex\Majors'` with `'Forex\\Majors'` in `brokerSymbol` fixtures in `contracts.test.ts` and `controlPlaneClient.test.ts`.

## Referrals
- `src/Frontend/YO4X.Web/src/api/controlPlaneClient.ts:494, 496, 534, 536` — `getBrokerAccount`, `getCredentialState`, `getDeployment`, and `getDeploymentActivity` interpolate IDs without client-side UUID format validation guards (`uuidPattern.test`), unlike other entity endpoints.
- `src/Frontend/YO4X.Web/src/api/controlPlaneClient.ts:537` — `boundedLimit` in `getDeploymentActivity` does not guard against `NaN`, producing `?limit=NaN` if `limit` is not a valid number.

## Coverage gaps
- `src/Frontend/YO4X.Web/src/features/bots/BotsPage.tsx:105-120` — The bot start/stop status change workflow (`changeStatus(bot)`), including button state transitions, pending indicator (`pendingBotId`), and error alert rendering (`actionError`), is completely untested in `BotsPage.test.tsx`.
- `src/Frontend/YO4X.Web/src/features/bots/BotsPage.tsx:283-305` — The local execution window uptime bar chart rendering (`bots-uptime__bar` modifiers and sample date axis) is untested in `BotsPage.test.tsx`.
- `src/Frontend/YO4X.Web/src/features/overlays/LinkAccountModal.tsx:160-163` — Client-side numeric validation for MT5 login input (`!/^[0-9]{1,20}$/u.test(trimmed)`) is untested in `LinkAccountModal.test.tsx`.
- `src/Frontend/YO4X.Web/src/api/controlPlaneClient.ts:494-501, 534-543, 545-549` — Methods `getBrokerAccount(accountId)`, `getCredentialState(accountId)`, `getDeployment(deploymentId)`, `getDeploymentActivity(deploymentId, limit)`, and `getStrategySourceCorpora()` have zero test coverage in `controlPlaneClient.test.ts`.
- `src/Frontend/YO4X.Web/src/api/contracts.ts:551-559, 614-638, 640-643` — Decoders `decodeCredentialStateView`, `decodeActivityViews`, and `decodeHealthView` have zero unit test cases in `contracts.test.ts`.
- `src/Frontend/YO4X.Web/src/features/backtests/BacktestsPage.tsx:136-143` and `src/Frontend/YO4X.Web/src/features/backtests/BacktestDetail.tsx:180-184` — Error and retry state in `BacktestsPage` and `FAILED` backtest status rendering with `failureReason` in `BacktestDetail` are unexercised in `backtests.test.tsx`.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 224.0s | 459493 tok | id=280b0082-fe22-4eaf-aac6-682f9c32ec33
