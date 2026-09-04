You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Runtime/YO4X.Mql5.Runtime/Mql5Colors.cs

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (2):

[1] [P1] `Mql5Colors.Name` aliases `ColorNone` (-1) to `"White"`, corrupting `clrNONE` string conversion
    Where:   src/Runtime/YO4X.Mql5.Runtime/Mql5Colors.cs:59
    Failure: When `Mql5Colors.Name` is called with `Mql5Constants.ColorNone` (`-1` / `0xFFFFFFFF`), `color & 0xFFFFFF` masks the value to `0x00FFFFFF`. Because `Table` maps `White` to `0xFFFFFF`, `ByValue` returns `"White"`. Consequently, converting `clrNONE` with `ColorToString(clrNONE, true)` emits `"clrWhite"` instead of `"clrNONE"` / empty, turning transparent indicator plots or invisible chart objects into opaque solid white.
    Suggested fix: Explicitly guard against `color == Mql5Constants.ColorNone` (or `color < 0`) before querying `ByValue`, returning `null` or handling `clrNONE` explicitly.

[2] [P1] `Mql5Colors.TryParse` fails to recognize `"clrNone"`, `"clrNONE"`, and `"None"`
    Where:   src/Runtime/YO4X.Mql5.Runtime/Mql5Colors.cs:74-83
    Failure: `ByName` is built strictly from `Table`, which does not include `None`. When parsing valid MQL5 color literals `"clrNone"`, `"clrNONE"`, or `"None"`, `TryParse` fails and returns `false`. When called through `StringToColor("clrNone")`, this failure sets `ERR_INVALID_PARAMETER` (4003) on `GetLastError()`, corrupting strategy error handling for standard MQL5 code.
    Suggested fix: Add `"None"` / `Mql5Constants.ColorNone` to the color name dictionary or explicitly check for `None` in `TryParse` and return `Mql5Constants.ColorNone`.

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

