---
agent_id: A10
lane: backtests-ui
scope:
  - src/Frontend/YO4X.Web/src/features/backtests/BacktestsPage.tsx
  - src/Frontend/YO4X.Web/src/features/backtests/BacktestDetail.tsx
  - src/Frontend/YO4X.Web/src/features/backtests/NewBacktestModal.tsx
status: COMPLETE
generated: 2026-08-29T08:51:00Z
counts: { P0: 0, P1: 2, P2: 3, P3: 0 }
---

# A10 — backtests-ui

## Scope audited
- `src/Frontend/YO4X.Web/src/features/backtests/BacktestsPage.tsx` (205 lines) — backtest list view, empty/loading/error states, and strategy detail navigation.
- `src/Frontend/YO4X.Web/src/features/backtests/BacktestDetail.tsx` (476 lines) — backtest detail view, equity curve geometry calculation, SVG projection, and result formatting.
- `src/Frontend/YO4X.Web/src/features/backtests/NewBacktestModal.tsx` (661 lines) — backtest creation dialog, strategy picker, input form generation, client/server validation handling, and submission lifecycle.
- `src/Frontend/YO4X.Web/src/features/backtests/backtestForm.ts` (611 lines) — reviewed for request builders, formatting utilities, and validation rule contracts.
- `src/Frontend/YO4X.Web/src/features/backtests/backtests.test.tsx` (427 lines) — reviewed for test coverage and existing regression assertions.
- `src/Frontend/YO4X.Web/src/api/contracts.ts` (2,058 lines) — reviewed for `BacktestView`, `BacktestDetailView`, and `BacktestEquityCurveView` decoders.
- `src/Frontend/YO4X.Web/src/api/controlPlaneClient.ts` (770 lines) — reviewed for backtest API client transport and signature invariants.

## Verdict
The backtest UI modules are cleanly structured and enforce strong declarative field rendering, but exhibit multiple defects in lifecycle resilience, submission synchronization, and graphical fidelity. Specifically, the modal submit button remains active during strategy input fetch errors (permitting unconfigured backtest creation), form submission lacks in-flight re-entry guards against rapid keyboard triggers, post-creation response decode errors induce duplicate submissions, non-terminal backtest states lack polling or automatic refreshes, and the equity curve SVG projection calculates horizontal coordinates from array indices rather than sample ordinals, distorting drawdown duration.

## Findings

### [P1] Submit button remains enabled when strategy inputs fail to load, permitting submission of unconfigured backtests
- **Where:** `src/Frontend/YO4X.Web/src/features/backtests/NewBacktestModal.tsx:648-655`
- **Confidence:** CONFIRMED
- **Code:**
  ```tsx
            <button
              type="submit"
              className="btn btn--primary"
              disabled={selected === null || submitting || inputsResource.state.status === 'loading'}
            >
              {submitting ? 'Submitting…' : 'Queue backtest'}
            </button>
  ```
- **Failure:** If `getStrategyInputs` fails due to network interruption, an unauthorized session, or a server error, `inputsResource.state.status` transitions to `'error'` or `'unauthorized'`. The submit button's disabled condition only checks for `'loading'`. Because `disabled` evaluates to `false`, the button remains enabled and clickable. Clicking "Queue backtest" causes `declaredInputs` to evaluate to `[]` via fallback (`inputsView?.inputs ?? []`), bypassing input validation and dispatching a `CreateBacktestRequest` with `inputs: []`. The backend creates and queues a backtest run stripped of all strategy input configuration.
- **Fix:** Change the submit button disabled predicate to verify `inputsResource.state.status !== 'ready'`.

### [P1] Double submission on rapid keyboard submission creates duplicate backtest queue entries
- **Where:** `src/Frontend/YO4X.Web/src/features/backtests/NewBacktestModal.tsx:196-206`
- **Confidence:** CONFIRMED
- **Code:**
  ```tsx
  const submit = useCallback(
    async (event: FormEvent<HTMLFormElement>) => {
      event.preventDefault();
      setAttempted(true);
      setSubmitError(null);
      setServerErrors(noServerErrors);

      if (Object.keys(validateFormValues(formValues)).length > 0
        || Object.keys(validateInputValues(declaredInputs, resolvedValues, touched)).length > 0) {
        return;
      }
  ```
- **Failure:** `submit` does not guard against execution while a submission is already in flight (`submitting` is not checked at the start of the handler and is omitted from `useCallback` dependencies). If a user presses `Enter` twice in rapid succession inside a text or numeric input (e.g. `symbol` or `timeframe`), two asynchronous `client.createBacktest(request)` invocations are dispatched concurrently. Because `createBacktest` sends a `POST /v1/backtests` request without an idempotency key, the server commits two identical backtest records to the database and queue.
- **Fix:** Guard `submit` with `if (submitting) return;`, include `submitting` in the callback dependencies (or use an in-flight ref), and supply a client-generated idempotency key header with backtest creation.

### [P2] Client-side response decode failure leaves modal open and induces duplicate submissions
- **Where:** `src/Frontend/YO4X.Web/src/features/backtests/NewBacktestModal.tsx:215-224`
- **Confidence:** CONFIRMED
- **Code:**
  ```tsx
      try {
        const created = await client.createBacktest(request);
        onCreated(created);
        onClose();
      } catch (error: unknown) {
        setServerErrors(serverFieldErrors(error, request.inputs.map((input) => input.name)));
        setSubmitError(userFacingProblem(error));
      } finally {
        setSubmitting(false);
      }
  ```
- **Failure:** If the backend creates the backtest record but returns a payload that fails client-side contract decoding (e.g. `ContractViolationError` thrown by `decodeBacktestView`), `createBacktest` throws. The `catch` block treats this as a general failure, skipping `onCreated` and `onClose`. The modal remains open, displays the error message, and re-enables the submit button. Because the UI appears failed, the user resubmits, creating a duplicate backtest run while the first created backtest remains orphaned in the database.
- **Fix:** Distinguish contract violation errors from API errors, invoke `onCreated` or trigger a background list reload if a decode error occurs on an accepted HTTP response, and close the modal.

### [P2] Missing polling lifecycle for running and queued backtests leaves detail and list views stale
- **Where:** `src/Frontend/YO4X.Web/src/features/backtests/BacktestDetail.tsx:136-139`
- **Confidence:** CONFIRMED
- **Code:**
  ```tsx
  const backtest = useResource((signal) => client.getBacktest(backtestId, signal), [
    client,
    backtestId,
  ]);
  ```
- **Failure:** When viewing a backtest in `QUEUED` or `RUNNING` status, `BacktestDetail` performs only a single fetch on mount via `useResource`. There is no polling timer, interval cleanup, backoff, or terminal state stop condition (`COMPLETE`/`FAILED`). When an execution runner picks up the queued backtest and completes or fails the run, the page remains permanently stuck in the initial state ("Recorded, not started" or "Executing") without ever displaying final results or the equity curve until the user manually triggers a full browser reload.
- **Fix:** Introduce a polling lifecycle hook in `BacktestDetail` that periodically refreshes the backtest state with backoff while in `QUEUED` or `RUNNING` status and automatically cancels when transitioning to `COMPLETE` or `FAILED` or on component unmount.

### [P2] Equity curve horizontal projection ignores `sourceOrdinal`, distorting drawdown duration on decimated series
- **Where:** `src/Frontend/YO4X.Web/src/features/backtests/BacktestDetail.tsx:74-77`
- **Confidence:** CONFIRMED
- **Code:**
  ```tsx
  const coordinates = points.map((point, index) => {
    const x = (index / lastIndex) * curveWidth;
    return `${x.toFixed(2)},${project(point.equity).toFixed(2)}`;
  });
  ```
- **Failure:** Decimated equity curves include regular strided samples alongside a retained final sample (`sourceOrdinal = sampleCount - 1`), creating an irregular final sample delta (e.g. interval = 100 on 1050 samples leaves the last interval spanning only 49 samples). By computing `x = (index / lastIndex) * curveWidth` from array index rather than sample ordinal, `buildCurve` allocates the final 49-sample segment the exact same horizontal width as preceding 100-sample segments. This horizontally stretches the final segment by over 2x, misrepresenting the temporal progression and duration of drawdowns and recoveries.
- **Fix:** Project the X coordinate proportionally to `point.sourceOrdinal` over `curve.sampleCount - 1` (or `lastPoint.sourceOrdinal - firstPoint.sourceOrdinal`) rather than the decimated array index.

## Referrals
- `src/Frontend/YO4X.Web/src/api/controlPlaneClient.ts:732` — `createBacktest` does not accept or transmit an `Idempotency-Key` header, unlike other mutating operations (`approveBrokerServer`, `createBrokerAccount`), preventing backend-level duplicate request suppression.
- `src/Frontend/YO4X.Web/src/features/backtests/backtestForm.ts:534` — `formatSignedAmount` fallback in the catch block omits the leading `+` sign for positive amounts when `Intl.NumberFormat` throws.

## Coverage gaps
- `src/Frontend/YO4X.Web/src/features/backtests/NewBacktestModal.tsx:651` — No test checks button disabled state when `inputsResource.state.status === 'error'`, which hides the bug allowing unconfigured backtest submissions.
- `src/Frontend/YO4X.Web/src/features/backtests/NewBacktestModal.tsx:196` — No test simulates rapid sequential Enter keypresses during form submission to verify concurrent submission prevention.
- `src/Frontend/YO4X.Web/src/features/backtests/BacktestDetail.tsx:136` — No test verifies polling or automatic UI state refresh when a backtest transitions from `QUEUED` to `COMPLETE`.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 127.3s | 288650 tok | id=20822268-0e34-4ff7-8c62-379326f3a4a3
