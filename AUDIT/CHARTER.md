# YO4X Fleet Audit — Agent Charter (binding)

**Every agent MUST read this file before writing anything.** It is the contract. An agent
that violates it produces a report that will be rejected and re-run.

## 1. Your lane is fixed

You are given an **Agent ID** (e.g. `F13`) and an **exact file scope**. You audit those
files and nothing else.

- You MAY read any file in the repo for *context* (call graphs, contracts, callers).
- You MAY ONLY file findings against files inside your scope.
- Spotted something real outside your scope? Do **not** file it. Put one line in
  `## Referrals` naming the file and the suspicion. Another agent owns that lane.

This is what stops 130 agents producing the same 8 findings.

## 2. Evidence or it does not exist

Every finding needs, without exception:

| Field | Rule |
|---|---|
| `file:line` | Real path, real line number. Verify by reading the file. |
| Code quote | 1–8 lines, copied exactly. Not paraphrased. |
| Failure scenario | Concrete input/state → concrete wrong output. Not "could be unsafe". |
| Severity | P0 / P1 / P2 / P3 (below) |
| Confidence | CONFIRMED (read the code, traced it) or PLAUSIBLE (fits, not fully traced) |
| Fix | The specific change. One or two sentences. No patches. |

If you cannot produce a failure scenario, you do not have a finding. Delete it.

## 3. Severity

- **P0** — Exploitable, or loses money / data / positions. Auth bypass, secret leak, SQL
  injection, tenant crossover, wrong trade size, lost fills, silent order duplication.
- **P1** — Wrong behaviour under reachable conditions. Bad math, race, wrong branch,
  contract mismatch frontend↔backend, transpiler emitting wrong semantics.
- **P2** — Robustness. Unhandled failure, leak, missing cancellation, missing validation
  on a reachable path.
- **P3** — Quality that will cause a future defect. Duplicated invariant, dead branch,
  misleading name on a safety-critical path.

## 4. Forbidden

- Style, formatting, naming preference, "consider adding a comment". **Not findings.**
- "Add more tests" as a standalone finding. Only cite a coverage gap when you can name
  the specific untested branch and the bug it hides.
- Speculation with no code read. No "this file probably…".
- Modifying **any** source file. This audit is read-only. You write exactly one file:
  your own report.
- Padding. **10 real findings beat 40 invented ones.** An honest report with 2 findings
  is a success. Inventing findings to look thorough is the single worst outcome here.

## 5. This is a live trading platform

Weight your attention accordingly. In order of consequence:

1. Money math — lot sizing, margin, P&L, decimal vs double, rounding.
2. Order lifecycle — duplication, loss, idempotency, partial fills, reconciliation.
3. Tenant isolation and credential handling.
4. Transpiler semantic fidelity — MQL5 in, C# out; any divergence silently changes a
   strategy's trades.
5. Time — timezone, broker server time, bar alignment, DST.

## 6. Output

Write exactly one file: `AUDIT/findings/<ID>-<slug>.md`, using `AUDIT/TEMPLATE.md`.
Overwrite it if it exists. Touch no other file.

Then return to your caller a **≤10 line digest only** — counts by severity and your top 3
one-liners. The full detail lives in your file. Do not paste the report back.
