You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Frontend/YO4X.Web/src/features/broker-accounts/hooks/useBrokerAccountConnection.ts

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (2):

[1] [P1] Account ID change fails to reset connection test state, causing spurious ContractViolationError and stale submission reuse
    Where:   src/Frontend/YO4X.Web/src/features/broker-accounts/hooks/useBrokerAccountConnection.ts:121-129
    Failure: When `useBrokerAccountConnection` is rendered and an operation is initiated or in-flight for account `A` (`accountId = "acc-A"`), `testState` holds `{ status: 'polling', accepted: { commandId: 'cmd-A', ... } }` and `submissionAttempt.current` holds `acc-A`'s expected aggregate version and idempotency key. If the parent component changes `accountId` to account `B` (`accountId = "acc-B"`), `loadState` updates for `acc-B`, but `testState` and `submissionAttempt` are not reset. The polling effect re-triggers because `accountId` changed, polling `client.getOperation("cmd-A")` and passing the result to `requireBoundOperation(operation, "cmd-A", "acc-B")`. Because `operation.targetId` ("acc-A") does not match `accountId` ("acc-B"), line 96 throws `ContractViolationError('CloudConnectionTestOperation')`, erroneously shifting `testState` into `poll-error` for `acc-B`. Furthermore, any subsequent `submit()` attempt reuses `acc-A`'s version and idempotency key from `submissionAttempt.current`.
    Suggested fix: Add an effect or update the existing account-loading effect to reset `testState` to `{ status: 'idle' }`, reset `submissionAttempt.current = null`, and abort `submissionController.current` whenever `accountId` changes.

[2] [P2] Background operation polling leaks when hook is disabled via enabled=false
    Where:   src/Frontend/YO4X.Web/src/features/broker-accounts/hooks/useBrokerAccountConnection.ts:206-210
    Failure: When a user triggers a cloud connection test via `submit()`, `testState` enters `status: 'polling'`, which starts recursive `setTimeout` polling against `/v1/operations/{commandId}`. If the parent component toggles `enabled` to `false` (e.g., when a user hides or minimizes the connection panel without unmounting the parent view), `loadState` transitions to `disabled`. However, `enabled` is omitted from the polling effect's dependency list `[accountId, client, pollAttempt, pollDelayMs, pollingCommandId]`, and `testState` remains in `status: 'polling'`. As a result, the polling effect does not clean up and continues polling the backend every 1,500ms in the background indefinitely until terminal state or failure is reached, and updates `testState` while disabled.
    Suggested fix: Include `enabled` in the polling `useEffect` dependency array, check `if (!enabled || client === null || accountId === null || pollingCommandId === null) return undefined;`, and reset `testState` to `{ status: 'idle' }` when `enabled` becomes `false`.

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

