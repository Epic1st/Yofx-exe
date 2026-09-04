---
agent_id: E02
lane: Mql5Parser
scope:
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Parser.cs
status: COMPLETE
generated: 2026-08-29T11:28:00Z
counts: { P0: 0, P1: 2, P2: 2, P3: 0 }
---

# E02 — Mql5Parser

## Scope audited
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Parser.cs` (3,065 lines)

## Verdict
The parser implementation demonstrates high fidelity in expression operator precedence, left/right associativity, ternary chaining, dangling-else binding, and cast vs parenthesised expression disambiguation. However, the parser contains two P1 semantic defects: local variable declarations using constructor-style initialisation are misclassified as expression statements and discarded via recovery, and multi-interface class inheritance fails to parse templated/scoped secondary bases, discarding entire class bodies. Additionally, typedefs and nested block declarations are silently dropped.

## Findings

### [P1] Local variable declarations with constructor arguments are misclassified and dropped
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Parser.cs:2148`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
                Mql5Token after = Ahead(1);
                if (!IsSymbolKind(after.Kind))
                {
                    return false;
                }

                return after.Text is ";" or "=" or "," or "[";
  ```
- **Failure:** When a local variable is declared using constructor initialisation (e.g. `CPositionInfo pos(symbol);` or `CArrayInt list(10);`), `LooksLikeLocalDeclaration` inspects the token after the identifier (`(`). Because `"("` is not included in the allowed trailing symbols, the function returns `false`. `ParseStatementCore` falls through to expression parsing, encounters the variable identifier where a semicolon was expected, reports `MQL5_PARSE_EXPECTED_SEMICOLON`, and calls `Recover()`, which discards the entire declaration and constructor invocation from the AST.
- **Fix:** Update `LooksLikeLocalDeclaration` to include `"("` in the symbol check (`after.Text is ";" or "=" or "," or "[" or "(";`), which matches constructor-style initialiser support in `ParseDeclarators`.

### [P1] Templated or scoped secondary base classes discard entire class declaration
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Parser.cs:1468-1480`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
                while (TakeSymbol(","))
                {
                    while (AtWord("public") || AtWord("protected") || AtWord("private") || AtWord("virtual"))
                    {
                        Advance();
                    }

                    if (AtName())
                    {
                        Advance();
                    }
                }
  ```
- **Failure:** When a class or struct inherits from multiple interfaces where a secondary base interface carries template arguments or scope qualifications (e.g. `class CHandler : public CObject, public IListener<CEvent> { ... };` or `class CHandler : public CObject, public NS::IListener { ... };`), the loop only consumes the first word (`IListener` or `NS`). The parser then reaches `Expect("{", CodeExpectedOpenBrace)` while the current token is `<` or `::`. This triggers an `MQL5_PARSE_EXPECTED_OPEN_BRACE` error and invokes `Recover()`, which skips past the `{ ... }` block and drops the entire class definition.
- **Fix:** In `ParseTypeDeclaration`, consume secondary base specifications using the same scope resolution and `TryScanTemplateArguments` logic used for the primary base class.

### [P2] Typedef declarations are silently discarded without diagnostic or AST representation
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Parser.cs:771-775`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
            if (AtWord("typedef"))
            {
                Recover();
                return null;
            }
  ```
- **Failure:** When source code defines a function pointer typedef (e.g. `typedef void (*ActionCallback)(int);`), `ParseDeclarationCore` invokes `Recover()` to skip to the terminating semicolon and returns `null` without recording any diagnostic. `ParseCompilationUnit` observes token index progression and treats the parse as completely clean (`Succeeded == true`, 0 diagnostics), while the type alias is silently dropped from the compilation unit's declarations list.
- **Fix:** Either model typedefs as an AST declaration node or record an informative diagnostic indicating that `typedef` constructs are unmodeled before recovering.

### [P2] Nested block-scoped type declarations are parsed and discarded into empty statements
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Parser.cs:2091-2097`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
            if (AtWord("enum") || AtWord("struct") || AtWord("class") || AtWord("union") || AtWord("template"))
            {
                Mql5Declaration? nested = ParseDeclaration(null);
                return nested is null
                    ? new Mql5EmptyStatement(start.Line, start.Column)
                    : NestedDeclarationStatement(nested, start);
            }
  ```
- **Failure:** When an `enum` or `struct` is declared locally inside a function body (e.g. `void Run() { struct Opts { int period; }; Opts o; }`), `ParseDeclaration` parses the struct declaration, but `NestedDeclarationStatement` replaces it with an `Mql5EmptyStatement`, discarding the parsed `Mql5TypeDeclaration`. The type is never added to the compilation unit or local scope, causing downstream passes to fail when resolving `Opts`.
- **Fix:** Enqueue block-scoped declarations into `pending` or attach them to the enclosing compilation unit/block AST structure instead of discarding the parsed node.

## Referrals
None.

## Coverage gaps
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Parser.cs:2148` — Untested branch for local variable declarations with constructor argument lists (`CPositionInfo pos(symbol);`).
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Parser.cs:1468-1480` — Untested branch for multi-interface inheritance where secondary interfaces use generic type parameters or namespace qualifiers (`class C : public A, public B<T>`).
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Parser.cs:771-775` — Untested branch for typedef function pointer declarations at file scope.
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Parser.cs:2091-2097` — Untested branch for block-scoped struct/enum declarations within function bodies.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 141.9s | 247423 tok | id=73077327-756c-4ada-ab4a-c7c89b0b8d64
