---
agent_id: F02
lane: codegen-expressions
scope:
  - src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Expressions.cs
status: COMPLETE
generated: 2026-08-29T08:26:00Z
counts: { P0: 0, P1: 4, P2: 0, P3: 1 }
---

# F02 — codegen-expressions

## Scope audited
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Expressions.cs` (1817 lines) — primary audit scope.
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5EmittedHelpers.cs` (143 lines) — reviewed for `Mql5Ops` helper parity and conversion mechanics.
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5ClrTypes.cs` (1400 lines) — reviewed for CLR type mapping, rank hierarchy, and scalar conversions.
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs` (1170 lines) — reviewed for array creation and declaration context.

## Verdict
The expression emission pipeline correctly handles most literal forms, truthiness coercions, datetime epoch arithmetic, and balanced numeric promotions across standard 32/64-bit scalar types. However, four P1 semantic and compilation defects exist: compound assignment expands target expressions twice on text, boolean, and enum types (corrupting array elements and index variables with side effects); unary negation on `ulong` emits invalid C# (`CS0023`); mixed string-to-numeric relational comparisons emit illegal C# (`CS0019`); and narrow integer arithmetic (`short`, `ushort`, `byte`, `sbyte`) bypasses narrowing casts upon assignment due to `SameShape` false-equality, emitting uncompilable C# (`CS0266`).

## Findings

### [P1] Compound assignment on text, enum, and boolean expressions evaluates target twice
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Expressions.cs:1271-1272`, `1302-1303`, `1314-1315`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  // Lines 1271-1272:
  return "(" + targetText + " = Mql5Ops.Concat(" + targetText + ", "
      + Expr(assignment.Value, depth + 1) + "))";
  // Lines 1302-1303:
  return "(" + targetText + " = unchecked((" + (clr ?? PoisonToken) + ")((long)(" + targetText + ") "
      + core + " " + Arith(assignment.Value, depth + 1) + ")))";
  // Lines 1314-1315:
  return "(" + targetText + " = Mql5Ops.Truth(Mql5Ops.Num(" + targetText + ") "
      + core + " " + Arith(assignment.Value, depth + 1) + "))";
  ```
- **Failure:** In MQL5 / C++, a compound assignment (`E1 += E2`, `E1 |= E2`) evaluates the target lvalue `E1` exactly once. `EmitAssignment` pre-renders `targetText = Expr(assignment.Target)` and embeds it twice in the synthesized assignment. When the target expression has side effects (such as array element indexing `arr[i++] += "suffix"`, `flags[NextIndex()] |= true`, or `enumArr[GetIndex()] |= MASK`), the target expression executes twice. For `arr[i++] += "suffix"` starting at `i = 0`, `i++` evaluates to `0` on the LHS and `1` on the RHS, overwriting `arr[0]` with `arr[1] + "suffix"` and incrementing `i` twice.
- **Fix:** For non-trivial target expressions (such as array indexing or property/method targets), evaluate the target through an emitted reference or temporary index variable, or introduce runtime compound mutation helpers.

### [P1] Unary negation on unsigned 64-bit integer (`ulong` / `Natural64`) emits invalid C# (CS0023)
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Expressions.cs:1140-1142`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  case "-":
  case "+":
      return "(" + unary.Operator + " " + Arith(unary.Operand, depth + 1) + ")";
  ```
- **Failure:** In MQL5 / C++, unary `-` on unsigned integers is valid modular arithmetic (two's complement negation). In C#, unary `-` is explicitly not defined for `ulong` (C# language specification §12.8.3). When transpiling `-u` where `u` is `ulong` (e.g. in hash functions, magic number masks, or bitwise algorithms), `EmitUnary` outputs `(- u)`, which fails Roslyn compilation with `CS0023: Operator '-' cannot be applied to operand of type 'ulong'`.
- **Fix:** Check if `unary.Operator == "-"` and the operand type is `Natural64`, emitting `unchecked((ulong)(-(long)(operand)))` or `unchecked((ulong)(~operand + 1UL))`.

### [P1] Relational comparison between `string` and non-string types emits invalid C# (CS0019)
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Expressions.cs:1188-1190`, `1208-1215`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  case "<" or ">" or "<=" or ">=" when leftIsText && rightIsText:
      return "(string.CompareOrdinal(" + Expr(binary.Left, depth + 1) + ", "
          + Expr(binary.Right, depth + 1) + ") " + op + " 0)";
  ...
  case "<":
  case ">":
  case "<=":
  case ">=":
  {
      (string balancedLeft, string balancedRight) = Balanced(binary, depth + 1);
      return "(" + balancedLeft + " " + op + " " + balancedRight + ")";
  }
  ```
- **Failure:** When comparing `string` with a non-string operand (e.g. `str < 10` or `0 >= str`), the expression bypasses the `leftIsText && rightIsText` guard and falls into the general relational operator branch. `CommonType(Text, Whole32)` returns `Text` (not arithmetic), so `Balanced` falls back to raw uncoerced operands `("(" + str + " < " + num + ")")`. In C#, comparing `string < int` causes compile error `CS0019: Operator '<' cannot be applied to operands of type 'string' and 'int'`.
- **Fix:** Extend the string comparison branch in `EmitBinary` to handle mixed text/scalar comparisons using `Mql5Ops.ToText(...)` with `string.CompareOrdinal` or by coercing the text operand to double/long.

### [P1] Narrow integer binary arithmetic (`short`, `ushort`, `byte`, `sbyte`) skips narrowing cast on assignment
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Expressions.cs:976-977`, `1637-1640`, `1263`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  default:
      return CommonType(TypeOf(binary.Left), TypeOf(binary.Right));
  ...
  if (!explicitCast && SameShape(target, source))
  {
      return text;
  }
  ```
- **Failure:** In C# (§12.4.7), binary operators (`+`, `-`, `*`, `/`, `%`, `&`, `|`, `^`) on types narrower than `int` (`short`, `ushort`, `byte`, `sbyte`) implicitly promote both operands to `int` and produce an `int` result. When transpiling `short c = a + b;` or `c = a / b;`, `InferBinary` reports the result type as `Whole16` (`short`). On assignment, `SameShape(Whole16, Whole16)` evaluates to `true`, causing `ConvertTo` to omit the required cast and emit `(c = (a + b))`. Roslyn rejects this with `CS0266: Cannot implicitly convert type 'int' to 'short'`.
- **Fix:** In `InferBinary`, promote arithmetic results on types smaller than 32 bits to `Whole32` (`int`) so that `ConvertTo` recognizes the type mismatch and emits the necessary narrowing cast `unchecked((short)(...))`.

### [P3] Dead code branch in `Coerce` for uncast text-to-scalar conversion
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Expressions.cs:1694-1697`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  if (source.Scalar == Mql5IrScalarKind.Text && !explicitCast)
  {
      return text;
  }
  ```
- **Failure:** Lines 1686-1692 already check `source.Scalar == Mql5IrScalarKind.Text && !source.IsArray` and unconditionally return `NarrowingCast(clr, parsed)`. Since array types are rejected at line 1627, lines 1694-1697 can never be executed under any input.
- **Fix:** Remove the unreachable `if (source.Scalar == Mql5IrScalarKind.Text && !explicitCast)` block.

## Referrals
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5EmittedHelpers.cs:78` — `Mql5Ops.ToLong` uses standard `long.TryParse`, which returns 0 on strings with trailing non-digits like `"123abc"`, whereas MQL5 `StringToInteger` parses prefix digits up to the first non-digit character (returning 123). Owned by F07 / F10.

## Coverage gaps
- None. Expression forms, binary operator combinations, and literal decoders in `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Expressions.cs` are thoroughly covered in unit tests; the identified bugs represent compiler semantic divergence on specific operand type combinations.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 112.2s | 210079 tok | id=1ce95275-4cc9-45c8-bb9d-ed3523b75eab
