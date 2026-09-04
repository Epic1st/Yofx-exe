You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Frontend/YO4X.Web/scripts/dom-probe.mjs

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (2):

[1] [P2] `dom-probe.mjs` performs no assertions and unconditionally exits with code 0
    Where:   src/Frontend/YO4X.Web/scripts/dom-probe.mjs:26-36
    Failure: `dom-probe.mjs` only prints DOM element counts to console. If the strategy catalog renders 0 cards, if `errors` contains unhandled page errors, or if the page renders blank skeletons indefinitely, the script logs the values and exits 0 without failing or raising an alert.
    Suggested fix: Add assertions verifying non-zero card counts and fail with `process.exitCode = 1` if `errors.length > 0` or essential elements are missing.

[2] [P3] Hardcoded URLs and ports across QA scripts bypass environment configuration
    Where:   src/Frontend/YO4X.Web/scripts/dom-probe.mjs:23, src/Frontend/YO4X.Web/scripts/live-detail.mjs:26,37,41, src/Frontend/YO4X.Web/scripts/live-capture.mjs:48
    Failure: While `visual-qa.mjs` and `interaction-check.mjs` read `process.env.YO4X_QA_URL`, `dom-probe.mjs` and `live-detail.mjs` hardcode `http://127.0.0.1:4173/`, and `live-capture.mjs` hardcodes port `7210` for authentication. When testing against alternative preview ports, containers, or staging hosts, these scripts attempt connections to the default local address and fail.
    Suggested fix: Standardize base URL resolution to `process.env.YO4X_QA_URL ?? 'http://127.0.0.1:4173/'` across all scripts.

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

