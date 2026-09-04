---
agent_id: D07
lane: frontend-projections
scope:
  - src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs
status: COMPLETE
generated: 2026-08-29T08:29:35Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# D07 — frontend-projections

## Scope audited
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs` (2995 lines)

Context opened:
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/005_frontend_projections.sql` (366 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/006_strategy_inputs_and_backtests.sql` (139 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/007_broker_server_catalogue.sql` (437 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/008_backtest_queue_worker_access.sql` (84 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/009_backtest_equity_curve.sql` (147 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/010_bot_settings_and_broker_symbols.sql` (159 lines)
- `src/Application/YO4X.ControlPlane.Application/FrontendProjectionContracts.cs` (556 lines)
- `src/Apps/YO4X.ControlPlane.Api/FrontendProjectionEndpoints.cs` (308 lines)
- `tests/YO4X.ControlPlane.Postgres.Tests/FrontendProjectionSourceContractTests.cs` (464 lines)
- `tests/YO4X.Postgres.IntegrationTests/FrontendProjectionPostgresTests.cs` (1244 lines)

## Verdict
The frontend projections layer is exceptionally clean and rigorously constructed across all 21 read/write entry points. Every SQL command runs inside a tenant-bound transaction initiated by `BeginAsync` (validating active session, user, and tenant state), enforces strict parameterized tenant and user filters on all queries/subqueries/unnest inserts, clamps all client-provided pagination bounds to hard limits, uses deterministic multi-column sorting (with primary keys as tie-breakers), maps monetary and numeric fields strictly to `decimal` without precision loss, and avoids N+1 query patterns by executing grouped batch loads.

## Findings
None.

## Referrals
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Security/least_privilege_roles.sql` — projection tables in `catalog`, `bots`, `simulation`, `journal`, and `billing` schemas do not have Postgres Row Level Security (RLS) policies enabled, making application-level tenant and user predicates in `PostgresFrontendProjections.cs` the sole barrier preventing cross-tenant data leakage.

## Coverage gaps
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs:2824-2825` — `ResolveOffset` arithmetic overflow branch `offset >= int.MaxValue ? int.MaxValue : (int)offset` when a caller passes an extreme integer value for `page` (e.g. `int.MaxValue / 2`).
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs:2738-2742` — `IsColour` identifier validation branch for color constants starting with an underscore (e.g. `_CustomClr`).
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs:2663-2666` — `ValidateInputValue` fallback branch returning `VALUE_KIND_UNKNOWN` when a strategy input declaration carries an unrecognized `ValueKind`.
