---
agent_id: F07
lane: codegen-writer
scope:
  - src/Runtime/YO4X.Mql5.CodeGen/Mql5CSharpWriter.cs
  - src/Runtime/YO4X.Mql5.CodeGen/Mql5EmittedHelpers.cs
  - src/Runtime/YO4X.Mql5.CodeGen/Mql5ShadowedLocals.cs
status: COMPLETE
generated: 2026-08-29T11:30:00Z
counts: { P0: 0, P1: 1, P2: 2, P3: 1 }
---

# F07 — Codegen Writer, Emitted Helpers & Shadowed Locals

## Scope audited
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5CSharpWriter.cs` (136 lines) — verified indentation, LF normalization, `#line` deduplication, and source path sanitization.
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5EmittedHelpers.cs` (143 lines) — audited `Mql5Ops` runtime helper routines for truthiness, datetime arithmetic, string concatenation, numeric parsing, and multidimensional jagged array allocation.
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5ShadowedLocals.cs` (214 lines) — audited AST scoping pre-pass, nested frame collection, parameter shadowing, switch section handling, and renaming map resolution.

## Verdict
The code generation infrastructure in this lane is largely well-structured and deterministic: `Mql5CSharpWriter` guarantees LF output and invariant culture formatting, while `Mql5ShadowedLocals` handles multi-level lexical scoping and hoisted parameters cleanly. However, `Mql5Ops.ToLong` and `Mql5Ops.ToDouble` in `Mql5EmittedHelpers` diverge semantically from MQL5 by failing to parse numeric prefixes with trailing non-digits or decimal points on integer casts, returning zero instead of the prefix value. Additionally, `Mql5ShadowedLocals` enforces a hardcoded depth limit of 64 (below the generator run's limit of 200) and does not guard against synthetic shadow name collisions with user identifiers.

## Findings

### [P1] String-to-number helpers fail on numeric prefixes with trailing non-numeric characters
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5EmittedHelpers.cs:78-85`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
      public static long ToLong(string? value) =>
          long.TryParse(value, System.Globalization.NumberStyles.Integer,
              System.Globalization.CultureInfo.InvariantCulture, out long parsed) ? parsed : 0L;

      public static double ToDouble(string? value) =>
          double.TryParse(value, System.Globalization.NumberStyles.Float,
              System.Globalization.CultureInfo.InvariantCulture, out double parsed) ? parsed : 0D;
  ```
- **Failure:** In MQL5, string-to-number casts (and `StringToInteger`/`StringToDouble`) parse leading numeric prefixes up to the first invalid character (e.g. `(long)"123.45"` yields `123L`, `(double)"100 USD"` yields `100.0D`, and `(long)"42abc"` yields `42L`). In the emitted `Mql5Ops` helpers, `long.TryParse` and `double.TryParse` require the entire string to be valid; when passed `"123.45"` or `"100 USD"`, they fail and return `0L` / `0D`. Strategies converting chart comments, lot size strings, or inputs with units receive `0` instead of the numeric value, causing bad order sizing or dropped calculations.
- **Fix:** Replace strict `long.TryParse` / `double.TryParse` with leading numeric prefix parsers that match MQL5 `StringToInteger` and `StringToDouble` semantics.

---

### [P2] Shadowed locals analysis terminates at depth 64 while generator allows depth 200
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5ShadowedLocals.cs:71-74`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          if (depth > 64)
          {
              return;
          }
  ```
- **Failure:** `Mql5GeneratorRun` defines `MaxDepth = 200` and allows statement generation up to 200 nesting levels. If a strategy contains deeply nested control flow (e.g. nested conditionals, switch cases, or loops at depths 65–200), `Mql5ShadowedLocals.Walk` silently returns early without recording shadow renames. `Mql5GeneratorRun` continues emitting declarations at depths 65–200 without renamed identifiers, producing invalid C# that fails Roslyn compilation with `CS0136` or causes incorrect variable capture.
- **Fix:** Synchronize the recursion depth limit in `Mql5ShadowedLocals.Walk` with `Mql5GeneratorRun.MaxDepth` (200).

---

### [P2] Synthetic shadow names can collide with user identifiers declared in outer scopes
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5ShadowedLocals.cs:206-209`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
              int ordinal = counters.GetValueOrDefault(name) + 1;
              counters[name] = ordinal;
              renamed[(line, column)] = Mql5ClrTypes.ShadowName(name, ordinal);
          }
  ```
- **Failure:** When an inner variable `foo` shadows an outer `foo`, `Mql5ShadowedLocals` generates the synthetic name `foo__1`. If the MQL5 source code already declared a variable named `foo__1` in an enclosing scope (e.g. `int foo__1 = 10; { int foo = 1; { int foo = 2; } }`), the renamed inner variable becomes `foo__1`, colliding with the existing enclosing variable and causing C# compilation failure `CS0136`.
- **Fix:** Verify generated shadow names against all identifiers present in enclosing scopes and increment the ordinal until an unused identifier is found.

---

### [P3] Line directive deduplication suppresses directives when lines advance between statements
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5CSharpWriter.cs:79-84`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          if (line <= 0 || line == _lastDirectiveLine)
          {
              return;
          }

          _lastDirectiveLine = line;
  ```
- **Failure:** When `#line N` is emitted in C#, the C# compiler treats the immediate next line as line `N` and increments its internal line counter on each subsequent emitted newline. When multiple statements or multi-line constructs originate from the same MQL5 source line `N`, the writer suppresses `#line N` for subsequent statements because `line == _lastDirectiveLine`. However, because previous lines advanced the C# compiler's line counter, the subsequent statements are compiled under virtual line `N + 1`, `N + 2`, etc., resulting in offset stack traces and diagnostic line numbers.
- **Fix:** Clear or update `_lastDirectiveLine` whenever lines are written to `_builder`, or track the actual active compiler line rather than only the last requested directive line.

## Referrals
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5ClrTypes.cs:1346` — `Mql5ClrTypes.ShadowName` unconditionally formats names with `__<ordinal>` without awareness of existing identifier tables; owned by F01/F03.
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Expressions.cs:1699-1700` — Relies on `Mql5Ops.ToDouble` and `Mql5Ops.ToLong` for type casts; owned by F02.

## Coverage gaps
- Untested branch in `src/Runtime/YO4X.Mql5.CodeGen/Mql5ShadowedLocals.cs:71`: statement trees with nesting depth between 65 and 200 are untested and silently drop shadowing metadata.
- Untested conversions in `src/Runtime/YO4X.Mql5.CodeGen/Mql5EmittedHelpers.cs:78-85`: `Mql5Ops.ToLong` and `Mql5Ops.ToDouble` with trailing alphanumeric units or decimal points in string-to-integer conversions.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 212.6s | 355905 tok | id=ca73cc77-9c6b-4c80-98b3-ea09b97ccb03
