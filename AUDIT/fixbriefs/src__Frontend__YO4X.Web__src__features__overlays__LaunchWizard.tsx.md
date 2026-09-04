You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Frontend/YO4X.Web/src/features/overlays/LaunchWizard.tsx

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (3):

[1] [P1] LaunchWizard hardcodes execution host to LOCAL on open, ignoring requested CLOUD launch host
    Where:   src/Frontend/YO4X.Web/src/features/overlays/LaunchWizard.tsx:172-173
    Failure: When an operator clicks "Run on Cloud" (`onRunCloud` in `DetailPage.tsx`), `App.tsx` sets overlay state with `host: 'CLOUD'`. However, `LaunchWizardProps` fails to accept `host` or `initialHost`, and `LaunchWizard` unconditionally initializes and resets `host` state to `'LOCAL'`. If the operator steps through the wizard without noticing that Step 2 defaulted to "This machine", `confirm()` submits `{ strategyId, host: 'LOCAL' }`. The strategy is deployed to the operator's local client instead of the cloud runner, failing to trade when the operator powers off their PC.
    Suggested fix: Accept `initialHost?: BotHost` in `LaunchWizardProps`, initialize `host` state to `initialHost ?? 'LOCAL'` upon opening, and pass `overlay.host` from `App.tsx`.

[2] [P1] Live bot launch permitted while manual test position is open on the account
    Where:   src/Frontend/YO4X.Web/src/features/overlays/LaunchWizard.tsx:751-758
    Failure: On Step 3 ("Bridge"), an operator can fire a 0.01 lot real test order (`testState.kind = 'open'` or `'sending'`). The footer "Start the bot" button remains completely enabled (`disabled={strategy === null || submitting}`). If the operator clicks "Start the bot" without first closing the test trade in their terminal, the live bot starts immediately on an account holding an unmanaged, unhedged open position, corrupting the new bot's position sizing, margin utilization, and risk rules.
    Suggested fix: Disable the primary submit button and guard `confirm()` whenever `testState.kind === 'open'` or `testState.kind === 'sending'`.

[3] [P2] Missing focus trap allows keyboard navigation to leak into background workspace controls
    Where:   src/Frontend/YO4X.Web/src/features/overlays/LaunchWizard.tsx:199-216
    Failure: In `LaunchWizard`, `LinkAccountModal`, and `ManageAccountDrawer`, focus is set to the close button on open, but no focus trap is established. Pressing `Tab` or `Shift+Tab` navigates past the dialog boundaries and focuses interactive background elements (workspace navigation links, strategy table actions, search bar) in `AppShell`. A keyboard operator can inadvertently trigger page navigations or background actions while the modal or drawer is open.
    Suggested fix: Use the shared `useDialogBehaviour` hook from `src/Frontend/YO4X.Web/src/shared/ui/Modal.tsx` across all three overlays to cycle focus strictly within the dialog surface.

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

