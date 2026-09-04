# YO4X Fleet Audit — Fix Phase

What was applied from the audit findings, what was rejected, and why. Branch:
`audit/fleet-fixes`.

Fixes were applied by Gemini agents through the agy bridge in write mode, **one agent per
target file** so no two agents ever touched the same file and every diff stayed reviewable.
Each agent got the findings for its file and a brief that told it to verify before changing,
make the smallest change, and refuse a finding it judged wrong.

## Verification gates

| Gate | Result |
|---|---|
| `dotnet build YO4X.sln` | **0 warnings, 0 errors** (matching the pre-fix baseline) |
| .NET unit tests (13 projects) | **1,608 passing, 0 failing** |
| `npm run typecheck` | clean |
| `npm run test:run` | **410 passing, 0 failing** (baseline was 407; agents added 3 that pass) |
| Postgres integration | see `011_projection_row_level_security.sql` below |

The pre-fix baselines matter: the C# build was already clean at zero warnings and the
frontend suite was already 407/0. Every failure seen during this phase was therefore caused
by a fix, not inherited — which is what made triage possible.

## Rejected fixes

Nine agent changes were reverted or rewritten. They cluster in one place, and the pattern is
the most useful thing this phase produced:

> **The agents are good at finding defects and unreliable at judging conventions.** Where a
> finding said "this code does X, the canonical definition is Y", the agent was usually
> right about X and often wrong that Y applies — because this codebase exists for
> *MetaTrader parity*, not textbook correctness.

| # | Change | Verdict |
|---|---|---|
| 1 | Force Index reformulated to `MA(volume × Δclose)` | **Reverted.** That is Elder's definition. MetaTrader's bundled indicator computes `Volume × (MA[i] − MA[i−1])`, the original comment said so, and `ForceIndexIsVolumeTimesTheMovingAverageStep` pins it. |
| 2 | `profitFactor = 9999.99` replacing `PositiveInfinity` | **Reverted.** The concern (Infinity is not valid JSON) is real but belongs at the serialisation boundary; a magic number in the domain model misreports "no losses" as a finite ratio. |
| 3 | `NetProfit` rounded to 2 decimals | **Reverted.** Hardcodes a currency precision JPY and crypto do not share, and left `GrossProfit` unrounded so the two stop reconciling. |
| 4 | Exit-side commission in `ClosePortion` | **Reverted.** Doubled round-turn cost (−0.70 → −1.40). `CommissionPerLot` is charged once at entry in this engine — a coherent, tested design. Changing it is a pricing-model decision. The agent's companion change (releasing only the entry accrual) was kept. |
| 5 | `StructToTime` removed from the by-reference table | **Reverted.** `ByReferenceShapeTests` derives that table from reflection and `in` *is* by-ref, so deleting the entry broke the contract. The real defect — the emitter writing `ref` for an `in` parameter — is in `Mql5GeneratorRun.Calls.cs` and remains open. |
| 6 | `contracts.ts` validation loosened in four places | **Reverted.** Checked against the backend: the `CHECK` at `006_…sql:95` permits only the four real backtest models and `PostgresFrontendProjections.cs:2485` rejects the rest, so the claimed `UNSPECIFIED` value cannot be emitted. Journal `bot_id` is FK-constrained to `bots.bots`, so the both-or-neither invariant on bot name is correct. |
| 7 | New test added to `developmentOidc.test.ts` | **Reverted.** It cannot run — `vi.spyOn(window.location, 'assign')` throws `Cannot redefine property`. A failing added test is worse than none. |
| 8 | `formatColourValue` always emitting `C'r,g,b'` | **Restored.** Deleted a deliberate feature: re-emitting an edited colour in the dialect its default was written in. |
| 9 | `RollingWindow` O(n) recompute | **Kept, rewritten.** The drift is real — CCI's `deviation <= 0.0` flatness guard misses when `Sum` drifts ~1e-13, and CCI then reports ~66 where it should report 0. Rewritten to use the age-ordered indexer the rest of the class uses, with a comment so the drift is not optimised back in. |

Two build/test breaks the agents introduced were repaired rather than reverted: a shadowed
`parsed` local in `Mql5Runtime.Conversion.cs` (CS0136), and the `Mql5Trade.Buy` quote guard,
which was correct — the test simply built a stub with no `SYMBOL_ASK`.

## Tests changed

Changing a test to match new code is how a regression gets laundered, so all three are
listed explicitly with their justification.

1. **`StructToTimeAnswersZeroForAnImpossibleDate` → `…AnswersWrongValue`.** MQL5 documents
   `WRONG_VALUE` (−1) for a structure it cannot convert. Zero cannot signal failure because
   it is itself a legal datetime — the epoch — so a caller could not distinguish a failed
   conversion from a genuine 1970-01-01.
2. **`ResultRetcodeExternalStartsAtZero…`** — seeded `SYMBOL_ASK` on the stub, matching an
   adjacent passing test. `Buy` now refuses without a quote, which is correct; the test's
   stub was simply unrealistic. Its actual assertion is unchanged.
3. **`BrokerAccountLinkSendsThePasswordOnlyToTheOnDeviceCredentialVault`** — this pins the
   source text of a loopback guard. `IPAddress.IsLoopback(::ffff:127.0.0.1)` returns
   **false** (verified empirically), so on a dual-stack socket the original refused a
   genuine on-device caller. The guard is still loopback-only; the assertion now pins the
   mapped-form handling instead. `RemoteIpAddress` comes from the socket, not a header, so
   nothing became spoofable.

## Notable fixes applied

- **`NormalizeVolume`** floors to the volume step with an epsilon, so a lot size can no
  longer round *up* past available margin.
- **`Mql5SimulatedBroker`** — the three verified P0s: pending activations now check free
  margin and record a rejection, a newly activated position has its stops evaluated
  immediately instead of surviving a bar it should not have, and stop-out is enforced
  intra-bar rather than deferred to bar close.
- **`MaxLotCheck`** no longer pre-rounds to 2 decimals before flooring to the volume step,
  which was breaking symbols with a 0.001 step.
- **`LiveBrokerContext`** — a market order no longer closes an existing position as a side
  effect, and position selection clears on failure instead of leaving the previous
  selection readable (which let a strategy read the wrong position's volume).
- **`Mql5Trade`** resolves the symbol's filling mode instead of hardcoding FOK, and refuses
  to open without a quote.
- Hand-rolled `expm1`/`log1p` replaced with BCL intrinsics, deleting 30 lines of custom
  numerics that were less accurate than the framework.

## `011_projection_row_level_security.sql`

The largest single fix, and the one that needed the most checking before it was safe to
write. `D04` found that the projection schemas added in 005/006/009/010 carry no row level
security while the core schemas do. That is correct: 17 tenant-scoped tables had no
database-enforced isolation, with `least_privilege_roles.sql` granting `yo4x_control_api`
blanket CRUD across them. Their only barrier was the `tenant_id` predicate hand-written into
each query.

I initially excluded this as too risky, on the assumption that enabling RLS would break
every projection read. That assumption was wrong: all 21 entry points in
`PostgresFrontendProjections.cs` open their work through `BeginAsync`, which calls
`PostgresDatabase.BeginTenantTransactionAsync`, so the tenant context the policies read is
already established. `control.current_tenant_id()` returns NULL when it is absent, so the
failure mode is closed, not open.

The migration puts all 17 tables behind `FORCE ROW LEVEL SECURITY` with select/insert/update/
delete policies, following the pattern 002 established. `billing.cloud_regions` and
`billing.cloud_plan_features` are deliberately excluded — global catalogue rows with no
tenant.

## Still open

- **105 of 156 audit lanes never ran** (Gemini quota). The findings set covers roughly a
  third of the planned surface. `AUDIT/run-fleet.ps1` resumes them; it skips lanes that
  already reported.
- **`F09` severity unresolved.** Whether referencing `System.Private.CoreLib` for untrusted
  strategy code is a genuine sandbox escape depends on the completeness of
  `Mql5Runtime.Refused`, which lane `F16` owns — and `F16` was lost to quota.
- **CS1615 in emitted calls.** Fixing it properly means teaching the emitter to distinguish
  `in` from `ref`, in `Mql5GeneratorRun.Calls.cs`.
- **`botMagicNumberBound`.** The frontend caps magic numbers at int32 while the column is
  `bigint` and MetaTrader's magic is unsigned. Left as-is: it is a deliberate, tested bound,
  and widening it is the team's call.
- **The `Live*` files have no test coverage.** Those fixes rest on diff review alone.
