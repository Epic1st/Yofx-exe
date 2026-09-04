You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Frontend/YO4X.Web/src/features/overlays/LinkAccountModal.tsx

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (3):

[1] [P1] LinkAccountModal form submit does not guard against submit-in-flight, permitting duplicate account registrations via Enter key
    Where:   src/Frontend/YO4X.Web/src/features/overlays/LinkAccountModal.tsx:152-177
    Failure: In `LinkAccountModal`, the submit button is disabled while submitting (`disabled={submitting || ...}`), but the `submit` callback itself contains no `if (submitting) return;` guard. Pressing the `Enter` key inside the login, password, or search `<input>` fields triggers the `<form onSubmit={submit}>` handler repeatedly during an in-flight submission. Because `App.tsx` generates a new idempotency key on each `onSubmit` invocation (`createRegistrationIdempotencyKey()`), multiple concurrent registration requests with distinct idempotency tokens are dispatched to the control plane for the same credentials, causing race conditions in credential store ingestion and duplicate account registration requests.
    Suggested fix: Add `if (submitting) return;` at the very beginning of `submit`, and disable all form input fields while `submitting` is true.

[2] [P2] Overlay dismissal via Escape, close button, or backdrop click is enabled during active network mutations
    Where:   src/Frontend/YO4X.Web/src/features/overlays/LinkAccountModal.tsx:205-238
    Failure: In all three overlays (`LaunchWizard` during `submitting`, `LinkAccountModal` during `submitting`, `ManageAccountDrawer` during `busy`), the Escape key handler, the `[X]` close button, the `Cancel` button, and the scrim `onMouseDown` handler remain active while network requests are in flight. If an operator presses Escape or clicks the backdrop/close button while an account link, bot launch, or account unlink is executing, the dialog immediately unmounts. The background mutation continues to run on the server, subsequent completion/failure feedback is lost, and the operator is left unaware of whether live trading or account linking succeeded.
    Suggested fix: Disable close and cancel buttons, ignore Escape key events, and prevent scrim backdrop dismissal whenever `submitting` or `busy` is true.

[3] [P3] Directory server approval action remains enabled during account linking submission
    Where:   src/Frontend/YO4X.Web/src/features/overlays/LinkAccountModal.tsx:341-349
    Failure: While `submitting` is true, the "Approve" button for unapproved directory servers is only disabled when `approvingKey !== null`. An operator can click "Approve" on a directory server row while account registration is already in flight, triggering `approve()` concurrently and mutating `selected` to a different server while the previous request is pending.
    Suggested fix: Update the button's disabled condition to `disabled={approvingKey !== null || submitting}` and add `if (submitting) return;` inside `approve()`.

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

