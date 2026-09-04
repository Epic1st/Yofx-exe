---
agent_id: E01
lane: mql5-lexer
scope:
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Lexer.cs
status: COMPLETE
generated: 2026-08-29T08:24:00Z
counts: { P0: 0, P1: 4, P2: 0, P3: 1 }
---

# E01 — mql5-lexer

## Scope audited
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Lexer.cs` (1193 lines)

## Verdict
The lexer implementation is well-structured and handles the majority of MQL5 lexical rules (such as hex literals, datetime components, operator maximal munch, and comment trivia attachment) robustly. However, it contains several critical divergences from the MQL5 language specification: floating literals with trailing dots and float suffixes (`1.f`) are broken into multiple invalid tokens, valid named color literals (`C'Red'`) and case-insensitive literal prefixes (`c'...'`, `d'...'`) are rejected or split, and standard backslash line continuation within string literals is incorrectly treated as an unterminated string error.

## Findings

### [P1] Floating literal with trailing dot and float suffix '1.f' splits into three invalid tokens
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Lexer.cs:646`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  private bool FractionFollows()
  {
      char next = Peek(1);
      if (char.IsAsciiDigit(next))
      {
          return true;
      }

      if (next is 'e' or 'E')
      {
          return char.IsAsciiDigit(Peek(2)) || (Peek(2) is '+' or '-' && char.IsAsciiDigit(Peek(3)));
      }

      return !IsIdentifierStart(next) && next != '.';
  }
  ```
- **Failure:** In valid MQL5 code such as `float x = 1.f;` or `float y = 0.F;`, `ReadNumber()` scans `1` and evaluates `FractionFollows()` at the `.`. Because `next` is `'f'`, `IsIdentifierStart('f')` returns `true`, making `!IsIdentifierStart(next)` evaluate to `false`. `FractionFollows()` returns `false`, causing the lexer to emit `WholeLiteral("1")`, then `Operator(".")`, and then `Identifier("f")` instead of a single `RealLiteral("1.f")`. This causes valid floating-point declarations to fail with syntax errors in the parser.
- **Fix:** Update `FractionFollows()` to check if `next is 'f' or 'F'` followed by a non-identifier character and treat it as a valid fraction suffix rather than a struct member access.

### [P1] Named colour literals C'Red' fail normalization and emit error diagnostics
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Lexer.cs:947`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  private static string? NormaliseColour(string content)
  {
      string[] parts = content.Split(ComponentSeparators);
      if (parts.Length != 3)
      {
          return null;
      }

      var components = new int[3];
  ```
- **Failure:** MQL5 standard syntax supports named color literals such as `color c = C'Red';` or `color bg = C'DarkSlateGray';`. `NormaliseColour` strictly requires exactly 3 comma-separated components (`parts.Length != 3 => return null`), returning `null` for all named color literals. Line 856 then emits diagnostic `MQL5_LEX_INVALID_COLOUR` (`"Colour literal 'Red' is not three byte components."`), causing valid strategies using standard MQL5 color literals to be falsely rejected with compilation errors.
- **Fix:** Extend `NormaliseColour` to resolve standard MQL5 web color names (using `Mql5Colors.ByName` or an equivalent lookup table) to their decimal `r,g,b` string representations.

### [P1] Lowercase literal prefixes c'...' and d'...' are mis-tokenized as identifiers
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Lexer.cs:264`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  if (current is 'C' or 'D' && Peek(1) == '\'')
  {
      ReadPrefixedLiteral(current);
      continue;
  }
  ```
- **Failure:** In the MQL5 language specification, color and datetime literal prefixes are case-insensitive (`C'...'` / `c'...'` and `D'...'` / `d'...'`). When source code contains `color c = c'255,0,0';` or `datetime t = d'2024.01.01';`, the lexer checks only uppercase `'C'` and `'D'`. It tokenizes `c` or `d` as an `Identifier` and `'255,0,0'` as a `CharacterLiteral`, causing downstream parsing to fail with unexpected token errors.
- **Fix:** Update the check to `if (current is 'C' or 'c' or 'D' or 'd' && Peek(1) == '\'')` and pass `char.ToUpperInvariant(current)` to `ReadPrefixedLiteral`.

### [P1] String literals with backslash line continuations trigger unterminated string errors
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Lexer.cs:750`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  if (IsNewLine(current))
  {
      // MQL5 has no multi-line string literals; leave the break to close the literal.
      decoded.Append('\\');
      return;
  }
  ```
- **Failure:** MQL5 allows string literals to span multiple lines using a trailing backslash line continuation (`\` before `\r\n` or `\n`). In `AppendEscape`, the lexer assumes MQL5 does not allow multi-line strings, appends `\\` without consuming the line splice, and returns to `ReadQuotedLiteral`. `ReadQuotedLiteral` sees the newline, terminates early with `closed == false`, reports diagnostic `MQL5_LEX_UNTERMINATED_STRING`, and leaves the remainder of the string on subsequent lines to be mis-parsed as code identifiers.
- **Fix:** Call `TrySkipLineSplice()` in `AppendEscape` when `\` is followed by a newline so line-spliced string literals continue uninterrupted.

### [P3] Reserved keyword 'typedef' is missing from Keywords lookup table
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Lexer.cs:156`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  // User type declarations.
  "enum", "struct", "class", "interface", "union", "template", "typename",

  // Access and storage.
  "public", "protected", "private", "virtual", "override", "final",
  ```
- **Failure:** `typedef` is an official reserved keyword in MQL5 used for declaring function pointer types (`typedef void (*Callback)(int);`). Because `"typedef"` is omitted from the `Keywords` set, `Mql5Lexer` classifies it as `Mql5TokenKind.Identifier` rather than `Mql5TokenKind.Keyword`. Any downstream component relying on `token.Kind == Mql5TokenKind.Identifier` (such as macro alias resolution in `Mql5Parser.TryReadSingleIdentifier`) will treat `typedef` as a user identifier name rather than a reserved word.
- **Fix:** Add `"typedef"` to the `Keywords` set in `Mql5Lexer.cs`.

## Referrals
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Parser.cs:3033` — `ParseDynamicCast` expects a single `>` token to close template arguments and fails on nested template types like `dynamic_cast<CFoo<CBar<int>>>(x)` because `>>` is lexed as an operator.
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Colors.cs:21` — Verify that table entries match the exact RGB palette defined by MetaTrader 5 color constants.

## Coverage gaps
- `Mql5Lexer.AppendEscape` (`line 750`): Missing unit tests for multi-line string literal splicing using trailing backslash before line breaks.
- `Mql5Lexer.NormaliseColour` (`line 947`): Missing unit tests for named color literals (`C'Red'`, `C'Blue'`).
- `Mql5Lexer.Run` (`line 264`): Missing unit tests for lowercase `c'...'` and `d'...'` literal prefixes.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 186.9s | 337107 tok | id=4894ccd6-dedf-4dc0-acc2-8084b27e739f
