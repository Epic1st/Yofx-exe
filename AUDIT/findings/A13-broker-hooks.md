---
agent_id: A13
lane: broker-hooks
scope:
  - src/Frontend/YO4X.Web/src/features/broker-accounts/hooks/useBrokerAccountConnection.ts
  - src/Frontend/YO4X.Web/src/features/broker-accounts/hooks/useBrokerAccountDiscovery.ts
  - src/Frontend/YO4X.Web/src/features/broker-accounts/hooks/useDevelopmentMt5ConnectionProbe.ts
status: COMPLETE
generated: 2026-08-29T08:51:00Z
counts: { P0: 0, P1: 1, P2: 1, P3: 0 }
---

# A13 — broker-hooks

## Scope audited
- `src/Frontend/YO4X.Web/src/features/broker-accounts/hooks/useBrokerAccountConnection.ts` (271 lines) — reviewed connection test lifecycle, polling timer loops, unmount abort controllers, idempotency tracking, and reload mechanics.
- `src/Frontend/YO4X.Web/src/features/broker-accounts/hooks/useBrokerAccountDiscovery.ts` (173 lines) — reviewed broker account list and option discovery, registration state machine, mutation deduplication, and cleanup.
- `src/Frontend/YO4X.Web/src/features/broker-accounts/hooks/useDevelopmentMt5ConnectionProbe.ts` (44 lines) — reviewed direct developer connection probe trigger, AbortController lifecycle, concurrent run protection, and production build behavior.

## Verdict
The broker account hooks demonstrate careful signal management, comprehensive unmount cancellation on `AbortController` instances, and strict client-side validation against contract models. `useBrokerAccountDiscovery` and `useDevelopmentMt5ConnectionProbe` cleanly isolate in-flight mutations and prevent concurrent requests. However, `useBrokerAccountConnection` exhibits two lifecycle defects: polling continues in the background when the hook is dynamically disabled via `enabled: false`, and switching `accountId` while polling or after an attempt fails to reset `testState` and `submissionAttempt`, causing a spurious `ContractViolationError` and stale idempotency key reuse.

## Findings

### [P1] Account ID change fails to reset connection test state, causing spurious ContractViolationError and stale submission reuse
- **Where:** `src/Frontend/YO4X.Web/src/features/broker-accounts/hooks/useBrokerAccountConnection.ts:121-129`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  useEffect(() => {
    if (!enabled) {
      setLoadState({ status: 'disabled' });
      return undefined;
    }
    if (client === null || accountId === null) {
      setLoadState({ status: 'unconfigured' });
      return undefined;
    }
  ```
- **Failure:** When `useBrokerAccountConnection` is rendered and an operation is initiated or in-flight for account `A` (`accountId = "acc-A"`), `testState` holds `{ status: 'polling', accepted: { commandId: 'cmd-A', ... } }` and `submissionAttempt.current` holds `acc-A`'s expected aggregate version and idempotency key. If the parent component changes `accountId` to account `B` (`accountId = "acc-B"`), `loadState` updates for `acc-B`, but `testState` and `submissionAttempt` are not reset. The polling effect re-triggers because `accountId` changed, polling `client.getOperation("cmd-A")` and passing the result to `requireBoundOperation(operation, "cmd-A", "acc-B")`. Because `operation.targetId` ("acc-A") does not match `accountId` ("acc-B"), line 96 throws `ContractViolationError('CloudConnectionTestOperation')`, erroneously shifting `testState` into `poll-error` for `acc-B`. Furthermore, any subsequent `submit()` attempt reuses `acc-A`'s version and idempotency key from `submissionAttempt.current`.
- **Fix:** Add an effect or update the existing account-loading effect to reset `testState` to `{ status: 'idle' }`, reset `submissionAttempt.current = null`, and abort `submissionController.current` whenever `accountId` changes.

### [P2] Background operation polling leaks when hook is disabled via enabled=false
- **Where:** `src/Frontend/YO4X.Web/src/features/broker-accounts/hooks/useBrokerAccountConnection.ts:206-210`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  const pollingCommandId = testState.status === 'polling' ? testState.accepted.commandId : null;
  useEffect(() => {
    if (client === null || accountId === null || pollingCommandId === null) {
      return undefined;
    }
  ```
- **Failure:** When a user triggers a cloud connection test via `submit()`, `testState` enters `status: 'polling'`, which starts recursive `setTimeout` polling against `/v1/operations/{commandId}`. If the parent component toggles `enabled` to `false` (e.g., when a user hides or minimizes the connection panel without unmounting the parent view), `loadState` transitions to `disabled`. However, `enabled` is omitted from the polling effect's dependency list `[accountId, client, pollAttempt, pollDelayMs, pollingCommandId]`, and `testState` remains in `status: 'polling'`. As a result, the polling effect does not clean up and continues polling the backend every 1,500ms in the background indefinitely until terminal state or failure is reached, and updates `testState` while disabled.
- **Fix:** Include `enabled` in the polling `useEffect` dependency array, check `if (!enabled || client === null || accountId === null || pollingCommandId === null) return undefined;`, and reset `testState` to `{ status: 'idle' }` when `enabled` becomes `false`.

## Referrals
None.

## Coverage gaps
- `src/Frontend/YO4X.Web/src/features/broker-accounts/hooks/useBrokerAccountConnection.ts:121-251` — No unit test coverage exists for hook lifecycle transitions: toggling `enabled` during active polling, dynamic switching of `accountId` mid-poll, error recovery via `resumePolling()`, or idempotency reset via `startOver()`.
- `src/Frontend/YO4X.Web/src/features/broker-accounts/hooks/useBrokerAccountDiscovery.ts:102-155` — No unit test coverage exists for abort cancellation during `register()`, deduplication under rapid registration clicks, or account selection preservation across `reload()`.
- `src/Frontend/YO4X.Web/src/features/broker-accounts/hooks/useDevelopmentMt5ConnectionProbe.ts:17-40` — No unit test coverage exists for loopback probe execution, 404 handling in non-development modes, or in-flight unmount cancellation.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 117.8s | 219879 tok | id=b4b290ec-abbc-4660-81e5-614c3d0e3e04
