---
agent_id: D02
lane: migrations-projections-and-catalogue
scope:
  - src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/004_local_development_identity_provisioning.sql
  - src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/005_frontend_projections.sql
  - src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/006_strategy_inputs_and_backtests.sql
  - src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/007_broker_server_catalogue.sql
status: COMPLETE
generated: 2026-08-29T11:26:00Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# D02 — migrations-projections-and-catalogue

## Scope audited
All 4 migration files in the assigned scope were fully read and audited:
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/004_local_development_identity_provisioning.sql` (221 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/005_frontend_projections.sql` (366 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/006_strategy_inputs_and_backtests.sql` (139 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/007_broker_server_catalogue.sql` (437 lines)

## Verdict
The audited migrations are robust, secure, and rigorously designed. Migration `004` establishes fail-closed, procedural development identity provisioning that creates no persistent production credentials or global accounts, restricted strictly to the non-production `yo4x_local_identity` role and a fixed development tenant UUID under forced RLS. Migrations `005`, `006`, and `007` maintain airtight tenant isolation through composite foreign keys, exhaustive non-null constraints, strict domain CHECK constraints, explicit `timestamptz` types, exact monetary precision (`numeric`/integer cents), and comprehensive index coverage for all projection and directory queries.

## Findings
None.

The audited migrations hold up against all audit criteria:

1. **Development Identity Isolation (`004_local_development_identity_provisioning.sql`):**
   - No default accounts, roles, or static credential rows are seeded into the database at migration time.
   - The provisioning function `identity.provision_local_development_identity` (`lines 109-215`) is protected by `SECURITY DEFINER set search_path = ''` and explicitly fails closed unless `session_user = 'yo4x_local_identity'` and `target_tenant_id = '019c8d27-763d-7000-8000-000000000001'::uuid`.
   - Dedicated RLS policies (`lines 70-107`) on `identity.tenants`, `identity.user_identities`, and `identity.user_session_families` confine `yo4x_local_identity` exclusively to the fixed development tenant UUID.
   - Direct table DML privileges are never granted to `yo4x_local_identity`; the role receives only EXECUTE on the provisioning function (`lines 217-220`).

2. **Tenant Isolation & Foreign Keys (`005`, `006`, `007`):**
   - All tenant-owned tables (`catalog.strategies`, `catalog.strategy_performance`, `catalog.strategy_equity_points`, `catalog.strategy_reviews`, `bots.bots`, `bots.bot_metrics`, `bots.uptime_samples`, `simulation.backtests`, `billing.cloud_runners`, `journal.trades`, `catalog.strategy_inputs`, `catalog.strategy_enum_members`, `simulation.backtest_inputs`, `brokerdirectory.tenant_demo_approvals`) declare `tenant_id uuid not null references identity.tenants(id)` and composite uniqueness constraints on `(tenant_id, id)`.
   - Child tables enforce composite foreign keys spanning `(tenant_id, parent_id)` or `(tenant_id, bot_id, user_id)` (e.g., `journal.trades:331`, `billing.cloud_runners:301`, `simulation.backtest_inputs:119`), precluding cross-tenant entity references.
   - `brokerdirectory.tenant_demo_approvals` enforces enabled and forced RLS (`007:99-104`) requiring `tenant_id = (select control.current_tenant_id())`.

3. **Money, Metrics, and Numeric Precision (`005`, `006`):**
   - Monetary balances, prices, and P&L amounts are represented exclusively using exact decimal numerics or integer cents: `pl_amount numeric(18,2)`, `net_profit_amount numeric(18,2)`, `result_amount numeric(18,2)`, `equity numeric(18,4)`, `volume numeric(12,2)`, `entry_price numeric(18,5)`, `exit_price numeric(18,5)`, and integer amounts (`cloud_price_monthly_cents`, `cloud_price_yearly_cents`, `monthly_price_cents`).
   - Ratios, percentages, and metrics enforce bounded numeric types and CHECK constraints: `rating_average numeric(3,2)` (`0..5`), `uptime_ratio numeric(5,4)` (`0..1`), `max_drawdown_percent numeric(6,2)` (`>= 0`), `profit_factor numeric(8,2)` (`>= 0`), and `data_quality_percent numeric(5,2)` (`0..100`).

4. **Timestamps & Temporal Integrity (`004`, `005`, `006`, `007`):**
   - All temporal fields across all tables and functions (`created_at`, `updated_at`, `completed_at`, `opened_at`, `closed_at`, `next_invoice_at`, `requested_at`, `fetched_at`, `imported_at`, `approved_at`, `target_session_expires_at`) use `timestamptz`.
   - Temporal consistency CHECK constraints are enforced (e.g., `check (period_end >= period_start)` in `simulation.backtests:227`, `check (closed_at is null or closed_at >= opened_at)` in `journal.trades:332`, and `check (fetched_at <= imported_at)` in `brokerdirectory.catalogue_snapshots:38`).

5. **Index Coverage (`005`, `006`, `007`):**
   - All tenant filter and sorting projection query patterns are indexed with leading `(tenant_id, ...)` or composite expressions (e.g., `strategies_tenant_active_users_idx`, `bots_tenant_user_updated_idx`, `backtests_tenant_user_created_idx`, `trades_tenant_user_opened_idx`, `strategy_inputs_tenant_strategy_idx`, `servers_search_key_idx`).

6. **Catalog Approval & Security Governance (`007`):**
   - `brokerdirectory.approve_demo_server` (`lines 123-300`) runs with `SECURITY DEFINER set search_path = '' set row_security = on`, strictly validating caller role (`yo4x_control_api`), active non-null context variables, and session/tenant liveness.
   - Profile promotions into `governance.broker_profiles` enforce demo limitations (`demo` support only, `trading: false`, non-tradeable cloud rules) and proper lock acquisition ordering (global governance mutation preceding tenant authority lock acquisition).
   - Trigger function `operations.enforce_pending_demo_broker_account_creation` (`lines 312-425`) ensures no directory-sourced profile can be used for broker account registration without an explicit approval record in `brokerdirectory.tenant_demo_approvals`.

## Referrals
None.

## Coverage gaps
None.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 75.9s | 169174 tok | id=203bd060-2ae3-46df-9b5c-3bb79fd0b77d
