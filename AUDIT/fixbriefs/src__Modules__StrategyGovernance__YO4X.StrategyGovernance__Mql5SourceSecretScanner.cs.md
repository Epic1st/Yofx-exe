You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SourceSecretScanner.cs

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (2):

[1] [P1] Secret scanner fails to detect preprocessor macros, split strings, and non-whitelisted variable names
    Where:   src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SourceSecretScanner.cs:79-86
    Failure: Plaintext credentials defined using `#define` macros (e.g. `#define BROKER_PASSWORD "RealPass123"`), multiline assignments (`string password =\n "Secret";`), string concatenation (`string api_key = "sk-" + "...";`), or common variable names (`secret_key`, `master_password`, `botToken`, `auth_token`) do not match `SensitiveStringAssignment`. The scanner produces false negatives, allowing raw secret material to pass intake undetected.
    Suggested fix: Extend regex pattern matching to include `#define` directives, allow newline whitespace around `=`, expand identifier matching keywords, and evaluate concatenated string literals.

[2] [P3] Single-document secret scan interface neglects `#include` dependencies
    Where:   src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SourceSecretScanner.cs:88-95
    Failure: `Mql5SourceSecretScanner` operates strictly on single isolated documents and lacks an include-aware or corpus-wide traversal method. If caller pipelines scan an entrypoint `.mq5` document without explicitly discovering and iterating all referenced `.mqh` files, secrets residing in included files bypass detection.
    Suggested fix: Introduce an overloaded scanner accepting a complete document corpus or document that include closure resolution is caller-mandatory.

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

