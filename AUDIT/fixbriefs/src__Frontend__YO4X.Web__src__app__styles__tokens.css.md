You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Frontend/YO4X.Web/src/app/styles/tokens.css

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (2):

[1] [P1] WCAG AA contrast failure (2.37:1) on `--color-text-faint` across footnotes, disclosures, and pricing terms
    Where:   src/Frontend/YO4X.Web/src/app/styles/tokens.css:38
    Failure: `--color-text-faint` (`#a3a8b2`) has relative luminance $L = 0.3935$. Against `--color-surface` (`#ffffff`), its contrast ratio is **2.37:1**; against `--color-surface-raised` (`#fafbfc`), it is **2.27:1**. WCAG 2.1 AA (SC 1.4.3) mandates a minimum contrast ratio of 4.5:1 for text. UI text styled with this token—including legal footnotes in `auth/auth.css:148` (`.auth-entry__card small`), strategy pricing terms in `features/strategies/catalog.css:95` (`.strategy-card__price-note`), brand descriptors in `auth/auth.css:207` (`.brand-mark__descriptor`), and empty backtest cells in `features/backtests/backtests.css:77` (`.backtests-absent`)—fails contrast thresholds and is unreadable for users with low vision or on low-contrast displays.
    Suggested fix: Darken `--color-text-faint` to `#656c78` ($L \le 0.155$, contrast $\ge 4.5:1$) or replace text usages with `--color-text-tertiary`.

[2] [P2] WCAG AA contrast failure (3.02:1 - 3.84:1) for table headers, subtitles, and form hints
    Where:   src/Frontend/YO4X.Web/src/app/styles/tokens.css:36-37
    Failure: `--color-text-muted` (`#8b9199`, $L = 0.2834$) produces a **3.15:1** contrast ratio against `#ffffff` and **3.02:1** on `--color-surface-raised` (`#fafbfc`). `--color-text-quaternary` (`#7b8290`, $L = 0.2232$) yields a **3.84:1** contrast ratio. Both fall below the WCAG AA 4.5:1 requirement for standard text. Consequently, table column headers across the entire platform (`.table__head` in `global.css:358`), page subtitles (`.page-subtitle` in `global.css:235`), empty state details (`.empty-state__detail` in `global.css:720`), bot account numbers (`.bots-bot__account` in `bots.css:19`), and form input hints (`.nb-hint` in `backtests.css:213`) fail accessibility compliance.
    Suggested fix: Adjust `--color-text-quaternary` to `#5e6573` (4.6:1) and `--color-text-muted` to `#606775` (4.5:1) to ensure all descriptive labels satisfy WCAG AA on light surfaces.

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

