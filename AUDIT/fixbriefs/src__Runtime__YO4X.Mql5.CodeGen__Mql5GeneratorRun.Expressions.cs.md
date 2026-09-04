You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Expressions.cs

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (5):

[1] [P1] Compound assignment on text, enum, and boolean expressions evaluates target twice
    Where:   src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Expressions.cs:1271-1272, 1302-1303, 1314-1315
    Failure: In MQL5 / C++, a compound assignment (`E1 += E2`, `E1 |= E2`) evaluates the target lvalue `E1` exactly once. `EmitAssignment` pre-renders `targetText = Expr(assignment.Target)` and embeds it twice in the synthesized assignment. When the target expression has side effects (such as array element indexing `arr[i++] += "suffix"`, `flags[NextIndex()] |= true`, or `enumArr[GetIndex()] |= MASK`), the target expression executes twice. For `arr[i++] += "suffix"` starting at `i = 0`, `i++` evaluates to `0` on the LHS and `1` on the RHS, overwriting `arr[0]` with `arr[1] + "suffix"` and incrementing `i` twice.
    Suggested fix: For non-trivial target expressions (such as array indexing or property/method targets), evaluate the target through an emitted reference or temporary index variable, or introduce runtime compound mutation helpers.

[2] [P1] Narrow integer binary arithmetic (`short`, `ushort`, `byte`, `sbyte`) skips narrowing cast on assignment
    Where:   src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Expressions.cs:976-977, 1637-1640, 1263
    Failure: In C# (§12.4.7), binary operators (`+`, `-`, `*`, `/`, `%`, `&`, `|`, `^`) on types narrower than `int` (`short`, `ushort`, `byte`, `sbyte`) implicitly promote both operands to `int` and produce an `int` result. When transpiling `short c = a + b;` or `c = a / b;`, `InferBinary` reports the result type as `Whole16` (`short`). On assignment, `SameShape(Whole16, Whole16)` evaluates to `true`, causing `ConvertTo` to omit the required cast and emit `(c = (a + b))`. Roslyn rejects this with `CS0266: Cannot implicitly convert type 'int' to 'short'`.
    Suggested fix: In `InferBinary`, promote arithmetic results on types smaller than 32 bits to `Whole32` (`int`) so that `ConvertTo` recognizes the type mismatch and emits the necessary narrowing cast `unchecked((short)(...))`.

[3] [P1] Relational comparison between `string` and non-string types emits invalid C# (CS0019)
    Where:   src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Expressions.cs:1188-1190, 1208-1215
    Failure: When comparing `string` with a non-string operand (e.g. `str < 10` or `0 >= str`), the expression bypasses the `leftIsText && rightIsText` guard and falls into the general relational operator branch. `CommonType(Text, Whole32)` returns `Text` (not arithmetic), so `Balanced` falls back to raw uncoerced operands `("(" + str + " < " + num + ")")`. In C#, comparing `string < int` causes compile error `CS0019: Operator '<' cannot be applied to operands of type 'string' and 'int'`.
    Suggested fix: Extend the string comparison branch in `EmitBinary` to handle mixed text/scalar comparisons using `Mql5Ops.ToText(...)` with `string.CompareOrdinal` or by coercing the text operand to double/long.

[4] [P1] Unary negation on unsigned 64-bit integer (`ulong` / `Natural64`) emits invalid C# (CS0023)
    Where:   src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Expressions.cs:1140-1142
    Failure: In MQL5 / C++, unary `-` on unsigned integers is valid modular arithmetic (two's complement negation). In C#, unary `-` is explicitly not defined for `ulong` (C# language specification §12.8.3). When transpiling `-u` where `u` is `ulong` (e.g. in hash functions, magic number masks, or bitwise algorithms), `EmitUnary` outputs `(- u)`, which fails Roslyn compilation with `CS0023: Operator '-' cannot be applied to operand of type 'ulong'`.
    Suggested fix: Check if `unary.Operator == "-"` and the operand type is `Natural64`, emitting `unchecked((ulong)(-(long)(operand)))` or `unchecked((ulong)(~operand + 1UL))`.

[5] [P3] Dead code branch in `Coerce` for uncast text-to-scalar conversion
    Where:   src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Expressions.cs:1694-1697
    Failure: Lines 1686-1692 already check `source.Scalar == Mql5IrScalarKind.Text && !source.IsArray` and unconditionally return `NarrowingCast(clr, parsed)`. Since array types are rejected at line 1627, lines 1694-1697 can never be executed under any input.
    Suggested fix: Remove the unreachable `if (source.Scalar == Mql5IrScalarKind.Text && !explicitCast)` block.

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

