---
agent_id: V01
lane: orchestrator-verification
scope:
  - spot-checks of agent-reported P0 findings, performed directly by the orchestrator
status: COMPLETE
generated: 2026-08-29T09:05:00Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# V01 — orchestrator verification

Not an audit lane. These are agent-reported P0 claims I re-verified against source myself,
to measure how far the fleet's output can be trusted.

## Scope audited
Direct source verification of six agent-reported P0 findings across `F20`, `F09`, `I13`,
`D04`, `D07`.

## Verdict
Five of six P0 claims verified exactly as written, including line numbers and code quotes.
One (`F09`) is factually correct but over-rated. No agent finding was found to be fabricated.

**This file previously contained a P0 of my own — "no row-level security exists anywhere" —
which was wrong. It has been retracted. See the retraction below; it is kept rather than
deleted because the way it happened is worth knowing.**

## Findings

None. This lane raises no findings of its own.

## Verification results on the sampled P0 claims

| Claim | Lane | Result |
|---|---|---|
| `HasMarginFor` never called on pending-order activation | F20 | **CONFIRMED** — sole call site is line 478 in `ExecuteDeal`; `ProcessPendingActivations` calls `OpenExposure` at 399 with no margin check |
| `ProcessPositionStops` runs before `ProcessPendingActivations` in `MoveTo` | F20 | **CONFIRMED** — lines 281-282; a position opened by the activation is never stop-checked at that price |
| `EnforceStopOut()` deferred to bar close | F20 | **CONFIRMED** — single call site at line 165, after all four intra-bar `MoveTo` steps |
| `NormalizeVolume` rounds half away from zero | I13 | **CONFIRMED** — `Mql5SymbolSpec.cs:65` uses `MidpointRounding.AwayFromZero`, so a lot size can round **up** past available margin |
| 16 tenant projection tables lack RLS | D04 | **CONFIRMED** — 17 tenant-scoped tables across migrations 005/006/009/010 have no `enable row level security` and no policy. D04's count of 16 is within one of the exact figure. |
| `System.Private.CoreLib` referenced for untrusted strategy code | F09 | **Correct but over-rated — now settled as P2.** The reference is real (`RoslynMql5CompilationHost.cs:127`). But strategies are MQL5 put through a transpiler that emits only calls into `IMql5Runtime`, and every outward channel on that surface throws. The refusals are split across two files, which is why a search of `Mql5Runtime.Refused.cs` alone looks incomplete: it covers the file and folder surface, while `Mql5Runtime.Terminal.cs:386-407` refuses `WebRequest` (both overloads), `SendMail`, `SendNotification` and `SendFtp`, each via `throw Refuse(...)`, and `TerminalDllsAllowed` answers 0. Reaching `System.IO.File` would require the code generator to emit a type reference it has no path to emit. Residual risk is a codegen question, not a reference-set one. |

## Retracted: "no row-level security exists anywhere"

I raised this as a P0 and it was false. RLS is implemented, and substantially so:

| Migration | Tables | `enable`/`force` RLS | Policies |
|---|---|---|---|
| 001_foundation | 68 | 4 explicit + a dynamic `execute format('alter table %s enable row level security', target_table)` loop at line 18663 | 32 |
| 002_user_operation_invocation_protocol | 9 | 9 / 9 | 20 |
| 007_broker_server_catalogue | 4 | 1 / 1 | 2 |
| **005_frontend_projections** | 13 | **0** | **0** |
| **006_strategy_inputs_and_backtests** | 3 | **0** | **0** |
| **009_backtest_equity_curve** | 1 | **0** | **0** |
| **010_bot_settings_and_broker_symbols** | 2 | **0** | **0** |

So the true position is exactly what `D04` reported and `D07` referred: the core protocol
schemas are RLS-protected; the later projection schemas are not. The 17 unprotected
tenant-scoped tables are `catalog.strategies`, `catalog.strategy_performance`,
`catalog.strategy_equity_points`, `catalog.strategy_reviews`, `catalog.strategy_inputs`,
`catalog.strategy_enum_members`, `bots.bots`, `bots.bot_metrics`, `bots.uptime_samples`,
`bots.bot_inputs`, `bots.broker_symbols`, `simulation.backtests`,
`simulation.backtest_inputs`, `simulation.backtest_equity_points`, `billing.cloud_plans`,
`billing.cloud_runners`, and `journal.trades`.

**How the error happened.** I searched the tree with
`grep -rn -i 'row level security|create policy|...' . | grep -v ... | head -20`. Every one
of the first 20 hits came from bundled pgAdmin templates under `.tools/`, which sorts before
`src/`. `head -20` cut the output before a single project hit appeared, and I read an empty
result as evidence of absence. The lesson is narrow and worth keeping: **a truncated search
is not a negative result** — count matches before concluding something does not exist.

The irony is not lost. I flagged `D04` for taking a README claim as evidence of
implementation, and then made a worse version of the same mistake — asserting absence from
a search I had truncated myself. `D04`'s supporting description of the core schemas was
right; my correction of it was wrong.

## Referrals
- `F16` (rt-core-globals) was lost to quota and never ran. It owns `Mql5Runtime.Refused`,
  which determines the true severity of the `F09` finding. Re-run it before acting on F09.
- 105 of 156 lanes were lost to the Gemini quota limit and hold no result. The findings set
  is therefore partial, and covers roughly a third of the planned surface.

## Coverage gaps
No test asserts that a tenant-scoped query against the projection schemas fails without its
predicate. Since those 17 tables have no RLS backstop, such a test is the only mechanism
that would catch an omitted `WHERE tenant_id` before production.
