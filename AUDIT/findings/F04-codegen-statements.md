---
agent_id: F04
lane: codegen-statements
scope:
  - src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Statements.cs
status: COMPLETE
generated: 2026-08-29T11:29:10Z
counts: { P0: 0, P1: 2, P2: 0, P3: 0 }
---

# F04 — codegen-statements

## Scope audited
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Statements.cs` (421 lines)

## Verdict
The statement emitter is generally solid in its core control structures: for-loops are correctly preserved as C# `for` loops rather than lowered to `while` (retaining proper `continue` increment semantics), loops and branches are consistently enclosed in braces to eliminate dangling-else issues, and MQL5 switch fallthrough is mapped to C# `goto case` jumps. However, two P1 compiler and semantic fidelity defects exist: an empty trailing section in a `switch` statement emits an orphan switch label without statements before the switch closing brace, triggering Roslyn compilation error CS8070; and `return <expr>;` inside a `void` function silently drops the expression and any associated side effects.

## Findings

### [P1] Trailing empty switch section emits orphan case label before switch closing brace causing CS8070
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Statements.cs:352-355`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
            if (section.Statements.Count == 0)
            {
                continue;
            }
  ```
- **Failure:** When an MQL5 switch statement ends with an empty section (such as `switch(x) { case 1: break; case 2: }` or `switch(x) { case 1: break; default: }`), `EmitSwitch` outputs the label (`case 2:` or `default:`) and hits `continue;`. The loop completes and closes the switch with `}`, emitting `case 2:\n}` without any statement in the final section. In C#, a switch label cannot immediately precede the closing brace without a statement list, causing Roslyn compilation failure `CS8070: Control cannot fall out of switch from final case label`.
- **Fix:** When `section.Statements.Count == 0` and `position == selection.Sections.Count - 1`, emit a braced block containing `break;` (`{ break; }`) so the terminal switch label has a valid terminating statement before the switch block closes.

### [P1] Void function return statement with expression silently drops expression side effects
- **Where:** `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Statements.cs:245-249`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        if (statement.Value is null || _currentReturnType.Scalar == Mql5IrScalarKind.Void)
        {
            _writer.Line("return;");
            return;
        }
  ```
- **Failure:** In MQL5 (like C++), a `void` function may return a `void` expression (for example, `void LogAndExit() { return Print("Exiting"); }` or `void Cleanup() { return ReleaseHandles(); }`). When `_currentReturnType.Scalar == Mql5IrScalarKind.Void` and `statement.Value` is non-null, `EmitReturn` emits only `return;` without evaluating or emitting `statement.Value`. Any function calls, side effects, or resource cleanups in `statement.Value` are silently dropped.
- **Fix:** When `_currentReturnType.Scalar == Mql5IrScalarKind.Void` and `statement.Value is not null`, emit `statement.Value` as an expression statement before emitting `return;`.

## Referrals
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5ShadowedLocals.cs:120-129` — Switch sections are walked together in a single scope under the assumption that C# switch sections share a block scope, but `Mql5GeneratorRun.Statements.cs` emits every section within its own braces `{ ... }`, resulting in unnecessary shadow variable renaming across sibling case sections.

## Coverage gaps
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Statements.cs:278-284` — Untested diagnostic branch when `loop.Initializer` is an unsupported statement form emitting `Mql5CodeGenDeclarationDiagnosticCodes.UnsupportedStatement`.
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Statements.cs:303-312` — Untested diagnostic branch when an array is declared in a for-loop initializer emitting `UnsupportedStatement`.
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Statements.cs:409-416` — Untested diagnostic branch in `FallthroughText` when `next.Labels.Count == 0` emitting `Mql5CodeGenDiagnosticCodes.UnsupportedSwitchFallthrough`.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 149.9s | 379708 tok | id=0ca8730d-e152-45c2-9e00-94eea970c73f
