---
agent_id: D04
lane: db-roles
scope:
  - src/BuildingBlocks/YO4X.Persistence.Postgres/Security/least_privilege_roles.sql
status: COMPLETE
generated: 2026-08-29T08:20:28Z
counts: { P0: 1, P1: 1, P2: 2, P3: 1 }
---

# D04 — db-roles

## Scope audited
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Security/least_privilege_roles.sql` (2000 lines)

Context opened:
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/001_foundation.sql` (18915 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/002_user_operation_invocation_protocol.sql` (6746 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/003_pending_demo_broker_account_registration.sql` (67 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/004_local_development_identity_provisioning.sql` (144 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/005_frontend_projections.sql` (366 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/006_strategy_inputs_and_backtests.sql` (139 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/007_broker_server_catalogue.sql` (437 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/008_backtest_queue_worker_access.sql` (84 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/009_backtest_equity_curve.sql` (147 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/010_bot_settings_and_broker_symbols.sql` (159 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRoleCapabilityFingerprint.cs` (2057 lines)

## Verdict
The core 8-schema boundary (`identity`, `authorization`, `control`, `operations`, `governance`, `audit`, `messaging`, `readmodel`) is rigorously guarded with subtractive sweeps, explicit column grants, and forced RLS policies. However, the projection schemas added in migrations 005, 006, 009, and 010 (`catalog`, `bots`, `simulation`, `journal`, `billing`) represent a major isolation breakdown: 16 tenant-scoped tables lack RLS completely, blanket CRUD permissions are granted to `yo4x_control_api` (and broad worker write access on simulation), system-wide pricing/region tables can be truncated by the web API, and schema ownership/default privilege normalization excludes all external projection schemas.

## Findings

### [P0] Missing row-level security on 16 tenant projection tables permits cross-tenant data access under `yo4x_control_api`
- **Where:** `src/BuildingBlocks/YO4X.Persistence.Postgres/Security/least_privilege_roles.sql:1892-1897`
- **Confidence:** CONFIRMED
- **Code:**
  ```sql
  grant usage on schema catalog to yo4x_control_api;
  grant select, insert, update, delete
      on all tables in schema catalog to yo4x_control_api;
  grant usage on schema bots to yo4x_control_api;
  grant select, insert, update, delete
      on all tables in schema bots to yo4x_control_api;
  ```
- **Failure:** Across migrations 005, 006, 009, and 010, 16 tenant-scoped tables (`catalog.strategies`, `catalog.strategy_performance`, `catalog.strategy_equity_points`, `catalog.strategy_reviews`, `catalog.strategy_inputs`, `catalog.strategy_enum_members`, `bots.bots`, `bots.bot_metrics`, `bots.uptime_samples`, `bots.bot_inputs`, `bots.broker_symbols`, `simulation.backtests`, `simulation.backtest_inputs`, `simulation.backtest_equity_points`, `billing.cloud_runners`, `journal.trades`) are created without `ENABLE ROW LEVEL SECURITY`, `FORCE ROW LEVEL SECURITY`, or tenant isolation policies. `least_privilege_roles.sql` then grants unconstrained `SELECT, INSERT, UPDATE, DELETE` across all these schemas to `yo4x_control_api` (and `yo4x_worker` receives table-wide write access on `simulation.backtest_equity_points`). Any control-plane endpoint query or background runner task that fails to explicitly predicate `WHERE tenant_id = @tenant_id` reads, modifies, or deletes records belonging to other tenants without database-level enforcement, directly violating the platform tenant boundary.
- **Fix:** Add `ENABLE ROW LEVEL SECURITY`, `FORCE ROW LEVEL SECURITY`, and restrictive tenant policies (`tenant_id = (select control.current_tenant_id())`) to all 16 projection tables in `least_privilege_roles.sql` or their respective migrations.

### [P1] Unrestricted table-level CRUD on global billing configuration tables granted to web API role
- **Where:** `src/BuildingBlocks/YO4X.Persistence.Postgres/Security/least_privilege_roles.sql:1901-1903`
- **Confidence:** CONFIRMED
- **Code:**
  ```sql
  grant usage on schema billing to yo4x_control_api;
  grant select, insert, update, delete
      on all tables in schema billing to yo4x_control_api;
  ```
- **Failure:** In `005_frontend_projections.sql:240-279`, `billing.cloud_regions`, `billing.cloud_plans`, and `billing.cloud_plan_features` define catalogue-wide cloud regions and global plan pricing tiers (where `tenant_id` is null or nonexistent). Granting blanket `SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA billing` allows the runtime web API login (`yo4x_control_api`) to execute arbitrary `UPDATE` or `DELETE` statements against global pricing plans and cloud region catalogs, risking platform-wide billing data tampering from a compromised user-facing API instance.
- **Fix:** Revoke table-wide write grants on `billing.cloud_regions`, `billing.cloud_plans`, and `billing.cloud_plan_features` from `yo4x_control_api`, granting `SELECT` only on catalogue definitions and restricting `INSERT, UPDATE, DELETE` exclusively to `billing.cloud_runners`.

### [P2] Ownership normalization hardcodes core 8 schemas, leaving projection and directory objects owned by bootstrap superuser
- **Where:** `src/BuildingBlocks/YO4X.Persistence.Postgres/Security/least_privilege_roles.sql:1049-1059`
- **Confidence:** CONFIRMED
- **Code:**
  ```sql
      for protected_schema in
          select namespace.nspname as schema_name
          from pg_catalog.pg_namespace as namespace
          where namespace.nspname in
              ('identity', 'authorization', 'control', 'operations',
               'governance', 'audit', 'messaging', 'readmodel')
      loop
          execute format(
              'alter schema %I owner to yo4x_migrator',
              protected_schema.schema_name);
      end loop;
  ```
- **Failure:** The ownership normalization loop in lines 1049–1160 only iterates over the initial 8 schemas. The schemas and relations added in migrations 005 and 007 (`catalog`, `bots`, `simulation`, `journal`, `billing`, `brokerdirectory`) are never transferred to `yo4x_migrator` and remain owned by the offline superuser (`postgres`). This breaks the architectural guarantee that all application schemas and relations are owned by the non-login `yo4x_migrator` role, leaving object-level DDL ownership attached to the superuser identity.
- **Fix:** Expand the schema filter array in lines 1053 and 1069 to include `'catalog', 'bots', 'simulation', 'journal', 'billing', 'brokerdirectory'` so all application objects are normalized to `yo4x_migrator`.

### [P2] `ALTER DEFAULT PRIVILEGES` lacks coverage for objects created by migration superuser in external schemas
- **Where:** `src/BuildingBlocks/YO4X.Persistence.Postgres/Security/least_privilege_roles.sql:1227-1232`
- **Confidence:** CONFIRMED
- **Code:**
  ```sql
  alter default privileges for role yo4x_migrator
      revoke all privileges on tables from public;
  alter default privileges for role yo4x_migrator
      revoke all privileges on sequences from public;
  alter default privileges for role yo4x_migrator
      revoke all privileges on functions from public;
  ```
- **Failure:** `least_privilege_roles.sql` only configures `ALTER DEFAULT PRIVILEGES FOR ROLE yo4x_migrator` and `yo4x_context_authority`. Because migrations run under an offline superuser session (e.g. `postgres`), future objects created during migrations do not inherit default privilege revocations from the migrator role. Furthermore, no default privilege rules grant access to `yo4x_control_api` or `yo4x_worker` in projection schemas, forcing every newly added table to either duplicate explicit grants in `least_privilege_roles.sql` or fail closed with 42501 permission denied upon deployment.
- **Fix:** Execute `ALTER DEFAULT PRIVILEGES REVOKE ALL ON TABLES/SEQUENCES/FUNCTIONS FROM PUBLIC` without role qualification during the superuser deployment session, or enforce `SET ROLE yo4x_migrator` within DDL migrations.

### [P3] Absence of granular transaction, lock, and statement timeouts for `yo4x_control_api` and `yo4x_worker`
- **Where:** `src/BuildingBlocks/YO4X.Persistence.Postgres/Security/least_privilege_roles.sql:1858-1863`
- **Confidence:** CONFIRMED
- **Code:**
  ```sql
  alter role yo4x_trade_authorizer set statement_timeout = '5s';
  alter role yo4x_trade_authorizer set lock_timeout = '2s';
  alter role yo4x_trade_authorizer set idle_in_transaction_session_timeout = '10s';
  alter role yo4x_gateway_runtime set statement_timeout = '5s';
  alter role yo4x_gateway_runtime set lock_timeout = '2s';
  alter role yo4x_gateway_runtime set idle_in_transaction_session_timeout = '10s';
  ```
- **Failure:** While specialized runtime roles (`yo4x_supervisor_runtime`, `yo4x_trade_authorizer`, `yo4x_gateway_runtime`, `yo4x_credential_runtime`, `yo4x_context_issuer`, `yo4x_local_identity`) receive explicit short timeouts (`statement_timeout = '5s'`, `lock_timeout = '2s'`, `idle_in_transaction_session_timeout = '10s'`), the primary high-throughput roles `yo4x_control_api` and `yo4x_worker` only receive the base `transaction_timeout = '2min'` from line 188. A blocked lock attempt on `operations.deployments` or `messaging.outbox_messages` by `yo4x_worker` or `yo4x_control_api` can hold client connections and locks for up to 2 minutes before failing.
- **Fix:** Apply role-level `statement_timeout = '15s'`, `lock_timeout = '5s'`, and `idle_in_transaction_session_timeout = '15s'` to `yo4x_control_api` and `yo4x_worker`.

## Referrals
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs` — query layer must manually maintain strict `tenant_id` WHERE clauses because underlying Postgres tables lack database-enforced RLS.
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/005_frontend_projections.sql` — projection tables created without `ENABLE/FORCE ROW LEVEL SECURITY` or tenant isolation policies.
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/007_broker_server_catalogue.sql` — directory relations created under bootstrap superuser without transferring ownership to `yo4x_migrator`.

## Coverage gaps
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Security/least_privilege_roles.sql:819-862` — untraced dynamic ownership remediation branch when a runtime role already owns an object in an external schema prior to migration script execution; `ALTER ... OWNER TO yo4x_migrator` executes dynamically without verifying whether the target schema owner accepts migration role ownership.
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Security/least_privilege_roles.sql:1985-1997` — untraced permission branch where `operations.enforce_pending_demo_broker_account_creation` executes as `yo4x_migrator` against `brokerdirectory.servers` and `brokerdirectory.catalogue_snapshots` (only `catalogue_broker_profiles` and `tenant_demo_approvals` were granted to `yo4x_migrator`).


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 205.4s | 390705 tok | id=ab1530ca-ab9d-4fe5-b599-6b53d60ec5a1
