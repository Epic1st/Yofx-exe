You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Lexer.cs

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (5):

[1] [P1] Floating literal with trailing dot and float suffix '1.f' splits into three invalid tokens
    Where:   src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Lexer.cs:646
    Failure: In valid MQL5 code such as `float x = 1.f;` or `float y = 0.F;`, `ReadNumber()` scans `1` and evaluates `FractionFollows()` at the `.`. Because `next` is `'f'`, `IsIdentifierStart('f')` returns `true`, making `!IsIdentifierStart(next)` evaluate to `false`. `FractionFollows()` returns `false`, causing the lexer to emit `WholeLiteral("1")`, then `Operator(".")`, and then `Identifier("f")` instead of a single `RealLiteral("1.f")`. This causes valid floating-point declarations to fail with syntax errors in the parser.
    Suggested fix: Update `FractionFollows()` to check if `next is 'f' or 'F'` followed by a non-identifier character and treat it as a valid fraction suffix rather than a struct member access.

[2] [P1] Lowercase literal prefixes c'...' and d'...' are mis-tokenized as identifiers
    Where:   src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Lexer.cs:264
    Failure: In the MQL5 language specification, color and datetime literal prefixes are case-insensitive (`C'...'` / `c'...'` and `D'...'` / `d'...'`). When source code contains `color c = c'255,0,0';` or `datetime t = d'2024.01.01';`, the lexer checks only uppercase `'C'` and `'D'`. It tokenizes `c` or `d` as an `Identifier` and `'255,0,0'` as a `CharacterLiteral`, causing downstream parsing to fail with unexpected token errors.
    Suggested fix: Update the check to `if (current is 'C' or 'c' or 'D' or 'd' && Peek(1) == '\'')` and pass `char.ToUpperInvariant(current)` to `ReadPrefixedLiteral`.

[3] [P1] Named colour literals C'Red' fail normalization and emit error diagnostics
    Where:   src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Lexer.cs:947
    Failure: MQL5 standard syntax supports named color literals such as `color c = C'Red';` or `color bg = C'DarkSlateGray';`. `NormaliseColour` strictly requires exactly 3 comma-separated components (`parts.Length != 3 => return null`), returning `null` for all named color literals. Line 856 then emits diagnostic `MQL5_LEX_INVALID_COLOUR` (`"Colour literal 'Red' is not three byte components."`), causing valid strategies using standard MQL5 color literals to be falsely rejected with compilation errors.
    Suggested fix: Extend `NormaliseColour` to resolve standard MQL5 web color names (using `Mql5Colors.ByName` or an equivalent lookup table) to their decimal `r,g,b` string representations.

[4] [P1] String literals with backslash line continuations trigger unterminated string errors
    Where:   src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Lexer.cs:750
    Failure: MQL5 allows string literals to span multiple lines using a trailing backslash line continuation (`\` before `\r\n` or `\n`). In `AppendEscape`, the lexer assumes MQL5 does not allow multi-line strings, appends `\\` without consuming the line splice, and returns to `ReadQuotedLiteral`. `ReadQuotedLiteral` sees the newline, terminates early with `closed == false`, reports diagnostic `MQL5_LEX_UNTERMINATED_STRING`, and leaves the remainder of the string on subsequent lines to be mis-parsed as code identifiers.
    Suggested fix: Call `TrySkipLineSplice()` in `AppendEscape` when `\` is followed by a newline so line-spliced string literals continue uninterrupted.

[5] [P3] Reserved keyword 'typedef' is missing from Keywords lookup table
    Where:   src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Lexer.cs:156
    Failure: `typedef` is an official reserved keyword in MQL5 used for declaring function pointer types (`typedef void (*Callback)(int);`). Because `"typedef"` is omitted from the `Keywords` set, `Mql5Lexer` classifies it as `Mql5TokenKind.Identifier` rather than `Mql5TokenKind.Keyword`. Any downstream component relying on `token.Kind == Mql5TokenKind.Identifier` (such as macro alias resolution in `Mql5Parser.TryReadSingleIdentifier`) will treat `typedef` as a user identifier name rather than a reserved word.
    Suggested fix: Add `"typedef"` to the `Keywords` set in `Mql5Lexer.cs`.

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

