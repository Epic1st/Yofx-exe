---
agent_id: D03
lane: migrations-late
scope:
  - src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/008_backtest_queue_worker_access.sql
  - src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/009_backtest_equity_curve.sql
  - src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/010_bot_settings_and_broker_symbols.sql
status: COMPLETE
generated: 2026-08-29T11:26:30Z
counts: { P0: 0, P1: 3, P2: 2, P3: 1 }
---

# D03 — migrations-late

## Scope audited
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/008_backtest_queue_worker_access.sql` (84 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/009_backtest_equity_curve.sql` (147 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/010_bot_settings_and_broker_symbols.sql` (159 lines)

Context opened:
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/001_foundation.sql` (18915 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/005_frontend_projections.sql` (366 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/006_strategy_inputs_and_backtests.sql` (139 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/007_broker_server_catalogue.sql` (437 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/011_projection_row_level_security.sql` (88 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Security/least_privilege_roles.sql` (2000 lines)
- `src/Tools/YO4X.Backtest.Runner/Program.cs` (680 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs` (2995 lines)
- `tests/YO4X.Postgres.IntegrationTests/BacktestQueueWorkerAccessPostgresTests.cs` (260 lines)

## Verdict
Migrations 008, 009, and 010 expand the platform's projection and background worker surfaces for backtesting queues, equity curves, bot settings, and broker instrument catalogs. While the partial claim index in 008 and the decimation-tracking equity schema in 009 are cleanly structured, there are several defects: the queue claim schema lacks a lease/visibility timeout column to recover from crashed workers, broker symbol lot sizing enforces a restrictive `numeric(12,2)` precision that crashes imports for crypto/micro-lot pairs, currency code checks reject standard 4-character crypto/stablecoin codes (`USDT`/`USDC`), `bots.bots.volume` lacks an upper bound safety constraint, and `bots.bot_inputs` creates a duplicate redundant index.

## Findings

### [P1] Missing queue lease timeout mechanism in backtest queue causes permanent worker stall on crash
- **Where:** `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/008_backtest_queue_worker_access.sql:39-41`
- **Confidence:** CONFIRMED
- **Code:**
  ```sql
  create index backtests_queued_claim_idx
      on simulation.backtests (created_at, id)
      where status = 'QUEUED';
  ```
- **Failure:** When a background worker claims a backtest with `status = 'QUEUED'` via `FOR UPDATE SKIP LOCKED` and transitions it to `status = 'RUNNING'`, the row leaves the partial index `backtests_queued_claim_idx`. Migration 008 introduces no worker claim lease timestamp (such as `lease_expires_at` or `heartbeat_at`), visibility timeout, or retry attempt counter. If the worker process crashes, encounters an OOM/fatal termination, or hangs during execution, the row remains in `status = 'RUNNING'` indefinitely. Because the claim index and runner claim query only evaluate `WHERE status = 'QUEUED'`, no subsequent worker can ever detect, reclaim, or fail the abandoned run, permanently stranding the backtest in the user's dashboard.
- **Fix:** Add a `lease_expires_at timestamptz` column to `simulation.backtests` and adjust the claim/reclaim query and partial index to include timed-out running jobs (`WHERE status = 'QUEUED' OR (status = 'RUNNING' AND lease_expires_at < clock_timestamp())`).

### [P1] `volume_step` and `volume_min` `numeric(12,2)` precision causes arithmetic underflow and constraint violation for crypto and micro-lot instruments
- **Where:** `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/010_bot_settings_and_broker_symbols.sql:120-122`
- **Confidence:** CONFIRMED
- **Code:**
  ```sql
  volume_min numeric(12,2) check (volume_min > 0),
  volume_max numeric(12,2) check (volume_max > 0),
  volume_step numeric(12,2) check (volume_step > 0),
  ```
- **Failure:** MetaTrader 5 brokers offering cryptocurrency pairs (such as `BTCUSD` or `ETHUSD`) or micro-lot fractional instruments specify lot steps (`SYMBOL_VOLUME_STEP`) and minimum volumes (`SYMBOL_VOLUME_MIN`) with 3 to 5 decimal places (e.g. `0.001` or `0.0001` lots). In `bots.broker_symbols`, these columns are constrained to `numeric(12,2)`. When importing broker symbols with a volume step of `0.001`, rounding to 2 decimal places produces `0.00`, which immediately violates `CHECK (volume_step > 0)` and `CHECK (volume_min > 0)`, throwing a PostgreSQL `check_violation` (23514) error and aborting the entire broker instrument import transaction.
- **Fix:** Change `volume_min`, `volume_max`, and `volume_step` column types in `bots.broker_symbols` (and `volume` in `bots.bots`) to `numeric(18,4)` or `numeric(18,8)` to accommodate fractional volume steps.

### [P1] 3-character ISO constraint on `bots.broker_symbols.currency` rejects standard 4-character crypto and stablecoin quote currencies
- **Where:** `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/010_bot_settings_and_broker_symbols.sql:123`
- **Confidence:** CONFIRMED
- **Code:**
  ```sql
  currency char(3) check (currency ~ '^[A-Z]{3}$'),
  ```
- **Failure:** On MetaTrader 5 broker servers offering cryptocurrency contracts, instruments are frequently denominated, margined, or settled in 4-character stablecoins or tokens (such as `USDT`, `USDC`, or `FDUSD`). When the broker symbol importer attempts to insert a symbol with `currency = 'USDT'`, the `char(3)` length limit and regex `check (currency ~ '^[A-Z]{3}$')` reject the value, causing symbol catalog synchronization to fail with a `check_violation` or string length error for all Tether and USD Coin denominated trading pairs.
- **Fix:** Update `currency` in `bots.broker_symbols` to `text check (currency ~ '^[A-Z0-9]{3,10}$')` or `varchar(10)` to support standard multi-character crypto and stablecoin currency codes.

### [P2] Missing positive check constraint on `simulation.backtests.equity_initial_deposit` permits zero and negative initial capital
- **Where:** `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/009_backtest_equity_curve.sql:63`
- **Confidence:** CONFIRMED
- **Code:**
  ```sql
  alter table simulation.backtests
      add column equity_initial_deposit numeric(18,4),
      add column equity_sample_count integer check (equity_sample_count >= 0),
      add column equity_decimation_interval integer
          check (equity_decimation_interval >= 1),
  ```
- **Failure:** `equity_initial_deposit` lacks a `check (equity_initial_deposit > 0)` constraint, unlike `contract_size`, `volume`, and `equity_decimation_interval` which have explicit positive checks. An invalid payload or corrupted worker result can persist `equity_initial_deposit = 0.0000` or negative values alongside valid sample counts. When the frontend or reporting calculation computes percentage returns or drawdown baselines against `equity_initial_deposit`, division by zero or negative baseline calculations result in `NaN` / `Infinity` or reversed return metrics.
- **Fix:** Add `check (equity_initial_deposit > 0)` to `simulation.backtests.equity_initial_deposit`.

### [P2] Missing upper bound safety constraint on `bots.bots.volume` allows storing unbounded lot sizes driving live orders
- **Where:** `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/010_bot_settings_and_broker_symbols.sql:38`
- **Confidence:** CONFIRMED
- **Code:**
  ```sql
  add column volume numeric(12,2) check (volume > 0),
  ```
- **Failure:** `bots.bots.volume` represents the trade volume executed by live trading bots. While line 38 checks `volume > 0`, it enforces no upper bound (e.g. `check (volume between 0.01 and 1000.00)`). If an operator submits a typo (e.g. `50000.00` instead of `0.50`) or a corrupted API request payload is processed, the database stores the massive lot size. When the bot executes live market orders, it submits extreme position sizes directly to the broker gateway, risking account margin blowout and immediate liquidation.
- **Fix:** Add a sane upper bound check constraint to `bots.bots.volume`, such as `check (volume > 0 and volume <= 10000.00)`.

### [P3] Redundant B-Tree index on `bots.bot_inputs (tenant_id, bot_id, name)` creates duplicate index maintenance overhead
- **Where:** `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/010_bot_settings_and_broker_symbols.sql:72,77-78`
- **Confidence:** CONFIRMED
- **Code:**
  ```sql
  unique (tenant_id, bot_id, name),
  foreign key (tenant_id, bot_id) references bots.bots(tenant_id, id)
  );

  create index bot_inputs_tenant_idx on bots.bot_inputs (tenant_id);
  create index bot_inputs_tenant_bot_idx
      on bots.bot_inputs (tenant_id, bot_id, name);
  ```
- **Failure:** Line 72 defines table constraint `unique (tenant_id, bot_id, name)`, which automatically builds a backing unique B-tree index on `(tenant_id, bot_id, name)`. Lines 77-78 then explicitly execute `create index bot_inputs_tenant_bot_idx on bots.bot_inputs (tenant_id, bot_id, name)`. This creates a completely redundant second index on the exact same column tuple, adding double write and maintenance overhead on every `bot_inputs` insert, update, and delete without providing any query planner benefit.
- **Fix:** Remove the duplicate `create index bot_inputs_tenant_bot_idx` statement from `010_bot_settings_and_broker_symbols.sql`.

## Referrals
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Security/least_privilege_roles.sql` — projection schemas `catalog`, `bots`, `simulation`, `journal`, `billing` created in migrations 005-010 lack database-enforced row-level security (RLS) policies and grant blanket table permissions to `yo4x_control_api`.
- `src/Tools/YO4X.Backtest.Runner/Program.cs` — runner claim query uses `FOR UPDATE SKIP LOCKED` against `status = 'QUEUED'` without checking or setting a lease expiration or visibility timeout, stranding jobs if the runner process terminates unexpectedly during execution.
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs` — query layer assumes `volume` and broker instrument volume steps are at most 2 decimal places, causing precision loss for crypto and micro-lot instruments.

## Coverage gaps
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/009_backtest_equity_curve.sql:71-84` — `backtests_equity_curve_is_self_describing` check constraint allows `equity_sample_count = 0` while `equity_decimation_interval >= 1` and `equity_initial_deposit` is set; the scenario where a backtest generates zero equity samples but non-null decimation interval is untested against downstream curve decoders in `PostgresFrontendProjections.cs`.
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/010_bot_settings_and_broker_symbols.sql:131` — `volume_min is null or volume_max is null or volume_max >= volume_min` check constraint boundary when `volume_min` equals `volume_max` (fixed-volume instruments); untested in combination with `volume_step > volume_min`.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 99.6s | 163006 tok | id=bd61cf94-93c0-41cb-93c6-b1b8ae63cb8e
