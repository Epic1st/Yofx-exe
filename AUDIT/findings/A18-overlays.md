---
agent_id: A18
lane: overlays
scope:
  - src/Frontend/YO4X.Web/src/features/overlays/LaunchWizard.tsx
  - src/Frontend/YO4X.Web/src/features/overlays/LinkAccountModal.tsx
  - src/Frontend/YO4X.Web/src/features/overlays/ManageAccountDrawer.tsx
status: COMPLETE
generated: 2026-08-29T08:55:00Z
counts: { P0: 0, P1: 3, P2: 3, P3: 1 }
---

# A18 — overlays

## Scope audited

- `src/Frontend/YO4X.Web/src/features/overlays/LaunchWizard.tsx` (764 lines) — Multi-step wizard putting trading strategies into live execution, supporting .set parameter inspection, host target selection (Local vs Cloud), live MT5 bridge status queries, manual test order execution, and bot creation submission.
- `src/Frontend/YO4X.Web/src/features/overlays/LinkAccountModal.tsx` (393 lines) — Modal dialog for linking MT5 broker trading accounts, including debounced broker directory search, on-the-fly server approval, credential validation, and secure device-vault binding.
- `src/Frontend/YO4X.Web/src/features/overlays/ManageAccountDrawer.tsx` (346 lines) — Slide-out drawer for inspecting linked account metadata, credential ingestion state, bridge latency, bot bindings, and account lifecycle actions (reconnect, update password, set default, unlink).

## Verdict

The overlay components contain clear domain separation and strong password security invariants (passwords are strictly kept out of persistent stores, URLs, and telemetry logs). However, the overlay state machines suffer from critical submit-in-flight and lifecycle defects: `LinkAccountModal` allows duplicate concurrent submissions via Enter key press due to a missing guard in the form submit handler; `LaunchWizard` hardcodes execution host initialization to `LOCAL`, silently discarding the caller's `CLOUD` target selection; `LaunchWizard` permits deploying a live bot while an unclosed manual test order is still active in the account; and all three overlays lack focus trapping (allowing keyboard focus to escape to background workspace controls) while permitting accidental backdrop/Escape dismissal during active network mutations.

## Findings

### [P1] LinkAccountModal form submit does not guard against submit-in-flight, permitting duplicate account registrations via Enter key
- **Where:** `src/Frontend/YO4X.Web/src/features/overlays/LinkAccountModal.tsx:152-177`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  const submit = useCallback(
    async (event: FormEvent<HTMLFormElement>) => {
      event.preventDefault();
      if (selected === null || !selected.approved) {
        setError('Choose an approved broker server first.');
        return;
      }
      const trimmed = login.trim();
  ```
- **Failure:** In `LinkAccountModal`, the submit button is disabled while submitting (`disabled={submitting || ...}`), but the `submit` callback itself contains no `if (submitting) return;` guard. Pressing the `Enter` key inside the login, password, or search `<input>` fields triggers the `<form onSubmit={submit}>` handler repeatedly during an in-flight submission. Because `App.tsx` generates a new idempotency key on each `onSubmit` invocation (`createRegistrationIdempotencyKey()`), multiple concurrent registration requests with distinct idempotency tokens are dispatched to the control plane for the same credentials, causing race conditions in credential store ingestion and duplicate account registration requests.
- **Fix:** Add `if (submitting) return;` at the very beginning of `submit`, and disable all form input fields while `submitting` is true.

### [P1] LaunchWizard hardcodes execution host to LOCAL on open, ignoring requested CLOUD launch host
- **Where:** `src/Frontend/YO4X.Web/src/features/overlays/LaunchWizard.tsx:172-173`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  const [step, setStep] = useState<StepNumber>(1);
  const [mode, setMode] = useState<InputMode>('defaults');
  const [host, setHost] = useState<BotHost>('LOCAL');
  ```
  and `src/Frontend/YO4X.Web/src/features/overlays/LaunchWizard.tsx:220-224`:
  ```typescript
  useEffect(() => {
    if (open) {
      setStep(1);
      setMode('defaults');
      setHost('LOCAL');
  ```
- **Failure:** When an operator clicks "Run on Cloud" (`onRunCloud` in `DetailPage.tsx`), `App.tsx` sets overlay state with `host: 'CLOUD'`. However, `LaunchWizardProps` fails to accept `host` or `initialHost`, and `LaunchWizard` unconditionally initializes and resets `host` state to `'LOCAL'`. If the operator steps through the wizard without noticing that Step 2 defaulted to "This machine", `confirm()` submits `{ strategyId, host: 'LOCAL' }`. The strategy is deployed to the operator's local client instead of the cloud runner, failing to trade when the operator powers off their PC.
- **Fix:** Accept `initialHost?: BotHost` in `LaunchWizardProps`, initialize `host` state to `initialHost ?? 'LOCAL'` upon opening, and pass `overlay.host` from `App.tsx`.

### [P1] Live bot launch permitted while manual test position is open on the account
- **Where:** `src/Frontend/YO4X.Web/src/features/overlays/LaunchWizard.tsx:751-758`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  <button
    type="button"
    className="btn btn--primary"
    disabled={strategy === null || submitting}
    onClick={onNext}
  >
    {submitting ? 'Starting…' : nextLabel}
  </button>
  ```
- **Failure:** On Step 3 ("Bridge"), an operator can fire a 0.01 lot real test order (`testState.kind = 'open'` or `'sending'`). The footer "Start the bot" button remains completely enabled (`disabled={strategy === null || submitting}`). If the operator clicks "Start the bot" without first closing the test trade in their terminal, the live bot starts immediately on an account holding an unmanaged, unhedged open position, corrupting the new bot's position sizing, margin utilization, and risk rules.
- **Fix:** Disable the primary submit button and guard `confirm()` whenever `testState.kind === 'open'` or `testState.kind === 'sending'`.

### [P2] Missing focus trap allows keyboard navigation to leak into background workspace controls
- **Where:** `src/Frontend/YO4X.Web/src/features/overlays/LaunchWizard.tsx:199-216`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  useEffect(() => {
    if (!open) {
      return undefined;
    }
    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    closeRef.current?.focus();
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.stopPropagation();
        onClose();
      }
    };
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('keydown', onKeyDown);
      previous?.focus();
    };
  }, [open, onClose]);
  ```
- **Failure:** In `LaunchWizard`, `LinkAccountModal`, and `ManageAccountDrawer`, focus is set to the close button on open, but no focus trap is established. Pressing `Tab` or `Shift+Tab` navigates past the dialog boundaries and focuses interactive background elements (workspace navigation links, strategy table actions, search bar) in `AppShell`. A keyboard operator can inadvertently trigger page navigations or background actions while the modal or drawer is open.
- **Fix:** Use the shared `useDialogBehaviour` hook from `src/Frontend/YO4X.Web/src/shared/ui/Modal.tsx` across all three overlays to cycle focus strictly within the dialog surface.

### [P2] Overlay dismissal via Escape, close button, or backdrop click is enabled during active network mutations
- **Where:** `src/Frontend/YO4X.Web/src/features/overlays/LinkAccountModal.tsx:205-238`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  const onKeyDown = (event: KeyboardEvent) => {
    if (event.key === 'Escape') {
      event.stopPropagation();
      onClose();
    }
  };
  document.addEventListener('keydown', onKeyDown);
  ```
  and `src/Frontend/YO4X.Web/src/features/overlays/LinkAccountModal.tsx:213`:
  ```typescript
  <div className="scrim scrim--center" role="presentation" onMouseDown={onClose}>
  ```
- **Failure:** In all three overlays (`LaunchWizard` during `submitting`, `LinkAccountModal` during `submitting`, `ManageAccountDrawer` during `busy`), the Escape key handler, the `[X]` close button, the `Cancel` button, and the scrim `onMouseDown` handler remain active while network requests are in flight. If an operator presses Escape or clicks the backdrop/close button while an account link, bot launch, or account unlink is executing, the dialog immediately unmounts. The background mutation continues to run on the server, subsequent completion/failure feedback is lost, and the operator is left unaware of whether live trading or account linking succeeded.
- **Fix:** Disable close and cancel buttons, ignore Escape key events, and prevent scrim backdrop dismissal whenever `submitting` or `busy` is true.

### [P2] Credential state error in ManageAccountDrawer displays perpetual 'Loading…' with no error or retry state
- **Where:** `src/Frontend/YO4X.Web/src/features/overlays/ManageAccountDrawer.tsx:180-192`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  {
    label: 'Credential',
    value:
      credentialValue === null
        ? 'Loading…'
        : credentialValue.exists
          ? credentialValue.state
          : 'Not ingested',
  },
  {
    label: 'Last worker use',
    value:
      credentialValue === null ? 'Loading…' : formatMoment(credentialValue.lastAuthorizedWorkerUse),
  },
  ```
- **Failure:** When `client.getCredentialState` rejects (e.g. server error or unauthorized session), `credential.state.status` becomes `'error'` and `credentialValue` is `null`. The `figures` mapping checks only `credentialValue === null` and renders `'Loading…'`, permanently showing the operator that credential inspection is loading rather than reporting an error or offering a reload trigger.
- **Fix:** Check `credential.state.status === 'error'` in `figures` and display `'Failed to load'` alongside a retry button.

### [P3] Directory server approval action remains enabled during account linking submission
- **Where:** `src/Frontend/YO4X.Web/src/features/overlays/LinkAccountModal.tsx:341-349`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  <button
    type="button"
    className="btn btn--secondary server-picker__approve"
    disabled={approvingKey !== null}
    onClick={() => void approve(option)}
  >
    {approvingKey === key ? 'Approving…' : 'Approve'}
  </button>
  ```
- **Failure:** While `submitting` is true, the "Approve" button for unapproved directory servers is only disabled when `approvingKey !== null`. An operator can click "Approve" on a directory server row while account registration is already in flight, triggering `approve()` concurrently and mutating `selected` to a different server while the previous request is pending.
- **Fix:** Update the button's disabled condition to `disabled={approvingKey !== null || submitting}` and add `if (submitting) return;` inside `approve()`.

## Referrals

- `src/Frontend/YO4X.Web/src/app/App.tsx:363-374` — `<LaunchWizard>` invocation in `AppShell` omits passing the selected `host` from `overlay.host`, causing `LaunchWizard` to always launch on `LOCAL`.
- `src/Frontend/YO4X.Web/src/app/App.tsx:251-259` — `submitLink` generates a new idempotency key on every execution instead of preserving an idempotency key per modal session.

## Coverage gaps

- `src/Frontend/YO4X.Web/src/features/overlays/LaunchWizard.tsx:160-763` — Missing component unit tests (`LaunchWizard.test.tsx`) for step transitions (1 -> 2 -> 3 -> 1), `.set` file parsing/validation errors, test order lifecycle state transitions (`idle` -> `sending` -> `open` -> `closed`), bridge offline/down banner rendering, and submission error handling.
- `src/Frontend/YO4X.Web/src/features/overlays/LinkAccountModal.tsx:37-392` — Missing component unit tests (`LinkAccountModal.test.tsx`) for search debouncing, server approval action dispatch, input validation (numeric login, password whitespace/length limits), and submission double-click/in-flight disabling.
- `src/Frontend/YO4X.Web/src/features/overlays/ManageAccountDrawer.tsx:86-345` — Missing component unit tests (`ManageAccountDrawer.test.tsx`) for projection loading states (bridge status, credential state error handling, bot bindings list), and drawer action dispatching with `busy` state lock.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 123.6s | 199809 tok | id=2138ac2b-98c7-4ddc-ad72-7514f1caa4af
