You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Text.cs

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (3):

[1] [P1] StringFind clamps negative startPosition to 0 instead of returning -1
    Where:   [Mql5Runtime.Text.cs:142-146](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Text.cs#L142-L146)
    Failure: Calling `StringFind("EURUSD", "USD", -5)` clamps `start` to `0` and returns `3`. In MQL5, a negative `start_pos` is invalid and returns `-1`. The silent clamping masks negative index computation bugs in strategies and parses substring tokens at unexpected positions.
    Suggested fix: Check `if (startPosition < 0) return -1;` before computing search positions.

[2] [P1] StringInit with character=0 clears string instead of allocating space-filled buffer
    Where:   [Mql5Runtime.Text.cs:333-334](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Text.cs#L333-L334)
    Failure: A strategy prepares a fixed-width string buffer by invoking `StringInit(ref str, 32, 0)`. In MQL5, `character=0` with `length>0` specifies creating a string of the given length filled with space characters (`' '` / 0x20). YO4X sets `value = string.Empty` (length 0). Subsequent mutations via `StringSetCharacter(ref str, 5, 'A')` fail with `Mql5ErrorCodes.StringSmallLength` because `position > value.Length`, preventing comment and order tag generation.
    Suggested fix: In `StringInit`, fill with `' '` (space) when `length > 0` and `character == 0`: `value = length == 0 ? string.Empty : new string(character == 0 ? ' ' : (char)character, length);`.

[3] [P1] `StringFind` clamps negative `startPosition` to 0 instead of returning -1 failure
    Where:   src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Text.cs:142
    Failure: In canonical MQL5, `StringFind(string_value, match_substring, start_pos)` specifies `start_pos` within `[0, StringLen - 1]`. A negative `start_pos` represents an invalid parameter and returns `-1`. In YO4X, calling `StringFind("EURUSD", "EUR", -5)` clamps `start` to `0` and incorrectly returns `0` (match found).
    Suggested fix: Reject negative positions immediately: `if (startPosition < 0 || startPosition > value.Length) return -1;`.

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

