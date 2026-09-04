You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Frontend/YO4X.Web/src/features/bots/botSettingsForm.ts

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (2):

[1] [P1] `botMagicNumberBound` rejects valid unsigned 32-bit and 64-bit MT5 magic numbers
    Where:   src/Frontend/YO4X.Web/src/features/bots/botSettingsForm.ts:196-199
    Failure: `botMagicNumberBound` restricts magic numbers to signed 32-bit `2_147_483_647`. In MT5, magic numbers are unsigned integers (`0` to `4_294_967_295` or `ulong` up to `2^64 - 1`). Entering a standard EA magic number such as `3000000000` is rejected with `Enter a whole magic number between 0 and 2147483647.`, preventing operators from setting their strategy's configured identifier.
    Suggested fix: Adjust `botMagicNumberBound` and validation pattern to accommodate full unsigned 32-bit (`4_294_967_295`) and 64-bit integers.

[2] [P1] `validateRunSettings` omits `instrument.volumeStep` validation, allowing invalid lot increments
    Where:   src/Frontend/YO4X.Web/src/features/bots/botSettingsForm.ts:184-195
    Failure: When an instrument enforces a volume step (e.g. `volumeMin: 0.1`, `volumeStep: 0.1`), an operator entering `0.15` lots passes validation because `validateRunSettings` only checks `volumeMin` and `volumeMax`. The setting saves successfully, but live MetaTrader 5 execution rejects subsequent orders with `TRADE_RETCODE_INVALID_VOLUME`.
    Suggested fix: Validate that `(volume - (instrument.volumeMin ?? 0))` is an exact integer multiple of `instrument.volumeStep` (accounting for floating-point epsilon), reporting an error on step mismatch.

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

