---
agent_id: L06
lane: PostgreSQL Queue Patterns & Isolation Research
scope:
  - src/Apps/YO4X.ControlPlane.Workers/Operations/PostgresUserOperationWorkStore.cs
  - src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxDeliveryEnvelope.cs
  - src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxDispatchContracts.cs
  - src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxDispatchCoordinator.cs
  - src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxDispatchOptions.cs
  - src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxDispatcherBackgroundService.cs
  - src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxWorkerIdentity.cs
  - src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxWorkerReadiness.cs
  - src/Apps/YO4X.ControlPlane.Workers/Outbox/RetrySchedule.cs
  - src/Apps/YO4X.ControlPlane.Workers/Outbox/UnavailableDependencies.cs
  - src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresAuditOutboxWriter.cs
  - src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresCredentialIngestionGrantStore.cs
  - src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresDatabase.cs
  - src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresIdempotencyRepository.cs
  - src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresMigrationManifest.cs
  - src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresMigrationRunner.cs
  - src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresOutboxRepository.cs
  - src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRoleCapabilityFingerprint.cs
  - src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRuntimeConnectionPolicy.cs
  - src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresTenantContextCapabilityProvider.cs
  - src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresTenantContextRepository.cs
  - src/BuildingBlocks/YO4X.Persistence.Postgres/TenantContextCapability.cs
  - src/BuildingBlocks/YO4X.Persistence.Postgres/TenantPostgresTransaction.cs
  - src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/001_foundation.sql
  - src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/002_user_operation_invocation_protocol.sql
  - src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/011_projection_row_level_security.sql
  - src/BuildingBlocks/YO4X.Persistence.Postgres/Security/least_privilege_roles.sql
status: COMPLETE
generated: 2026-08-29T11:41:00Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# L06 — PostgreSQL Queue Patterns & Isolation Research

## Scope audited
- `src/Apps/YO4X.ControlPlane.Workers/Operations/PostgresUserOperationWorkStore.cs` (3,831 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxDeliveryEnvelope.cs` (57 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxDispatchContracts.cs` (63 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxDispatchCoordinator.cs` (445 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxDispatchOptions.cs` (68 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxDispatcherBackgroundService.cs` (78 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxWorkerIdentity.cs` (27 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxWorkerReadiness.cs` (69 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Outbox/RetrySchedule.cs` (54 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Outbox/UnavailableDependencies.cs` (42 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresAuditOutboxWriter.cs` (72 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresCredentialIngestionGrantStore.cs` (186 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresDatabase.cs` (185 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresIdempotencyRepository.cs` (178 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresMigrationManifest.cs` (49 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresMigrationRunner.cs` (152 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresOutboxRepository.cs` (198 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRoleCapabilityFingerprint.cs` (520 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRuntimeConnectionPolicy.cs` (62 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresTenantContextCapabilityProvider.cs` (310 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresTenantContextRepository.cs` (144 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/TenantContextCapability.cs` (88 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/TenantPostgresTransaction.cs` (223 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/001_foundation.sql` (18,915 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/002_user_operation_invocation_protocol.sql` (6,746 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/011_projection_row_level_security.sql` (88 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Security/least_privilege_roles.sql` (2,150 lines)

## Verdict
The repository's PostgreSQL persistence layer strictly adheres to canonical PostgreSQL documentation and best practices across concurrency control, queuing, isolation, and security boundaries. Outbox message claiming implements the standard atomic `WITH ... SELECT ... FOR UPDATE SKIP LOCKED ... UPDATE ... RETURNING` pattern, while user operation lifecycle transitions combine tenant-level transaction advisory locks (`pg_advisory_xact_lock`) with atomic row-version Compare-And-Swap updates. All Row-Level Security policies mandate `FORCE ROW LEVEL SECURITY` to prevent table-owner bypass, advisory locks are exclusively transaction-scoped to prevent connection-pool leaks, and tenant context isolation avoids session-variable pollution by using cryptographically verified capability records bound to backend PIDs and transaction IDs.

## Findings
None.

### Canonical PostgreSQL Research and Repository Comparison

#### 1. SELECT FOR UPDATE SKIP LOCKED Queue Pattern
- **Documented Guidance:** Official PostgreSQL documentation (Chapter 13 *Concurrency Control*, Section 13.3.2 *The Locking Clause*) specifies `FOR UPDATE SKIP LOCKED` for implementing high-concurrency queues. A plain `SELECT` followed by an `UPDATE` under `Read Committed` isolation is unsafe: concurrent workers execute the `SELECT` simultaneously and observe the same available rows without locking them. When both workers attempt to update the row, they either cause duplicate task execution or block on the first worker's row lock, leading to thread contention, lock convoying, and serialization anomalies. With `FOR UPDATE SKIP LOCKED`, PostgreSQL locks eligible rows immediately during selection and skips any rows locked by concurrent transactions.
- **Repository Implementation:** `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresOutboxRepository.cs:9-33` implements the canonical CTE pattern:
  ```csharp
  private const string ClaimSql = """
      with claimable as
      (
          select message.id
          from messaging.outbox_messages as message
          where message.tenant_id = @tenant_id
            and
            (
                (message.state = 'pending' and message.available_at <= @claimed_at)
                or
                (message.state = 'processing' and message.locked_until <= @claimed_at)
            )
          order by message.available_at, message.occurred_at, message.id
          for update skip locked
          limit @batch_size
      )
      update messaging.outbox_messages as message
      set state = 'processing',
          attempts = message.attempts + 1,
          locked_by = @worker_id,
          locked_until = @locked_until,
          last_error = null
      from claimable
      where message.id = claimable.id
      returning
  ```
  Additionally, capability cleanup in `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/001_foundation.sql:533-545` utilizes `FOR UPDATE SKIP LOCKED` inside a `WITH cleanup_candidate AS (...) DELETE ... USING cleanup_candidate` statement.
  In `src/Apps/YO4X.ControlPlane.Workers/Operations/PostgresUserOperationWorkStore.cs:472-529`, candidate retrieval performs a non-locking scan (`ReadCandidatesAsync`), but all subsequent claims (`ClaimForDispatchAsync` and `ClaimOpenAsync`) acquire tenant-level advisory locks (`control.acquire_u0_authority_lock()`) and enforce an atomic CAS condition (`where row_version = @expected_version`), ensuring mutual exclusion per tenant.
- **Concrete Failure Prevented:** If `PostgresOutboxRepository` had used a two-statement `SELECT` then `UPDATE` pattern, two concurrent outbox background dispatcher instances would select the same pending message concurrently, mark it as `processing`, and emit duplicate execution messages across the broker gateway.

#### 2. Transaction Isolation Levels and Anomalies
- **Documented Guidance:** PostgreSQL documentation (Section 13.2 *Transaction Isolation*) specifies that `Read Committed` (the default) executes each query against a fresh snapshot taken at the start of that query. It prevents dirty reads but permits non-repeatable reads, phantom reads, and serialization anomalies. `Repeatable Read` takes a snapshot at the start of the transaction and prevents non-repeatable and phantom reads, but raises `40001 (serialization_failure)` if concurrent transactions modify the same target row. `Serializable` enforces full serializable snapshot isolation (SSI) to eliminate write skew and read-only anomalies, but requires client application code to catch SQLSTATE `40001` on any statement or `COMMIT` and retry the entire transaction.
- **Repository Implementation:** `TenantPostgresTransaction.cs` and `PostgresDatabase.cs:145` execute transactions under the default `Read Committed` isolation level. Critical paths do not rely on implicit transaction-level isolation or unhandled `Serializable` retries; instead, they explicitly resolve concurrency anomalies using:
  1. Transaction-scoped advisory locks for tenant authority serialization (`pg_advisory_xact_lock`).
  2. CTE `FOR UPDATE SKIP LOCKED` for queue claims.
  3. Row-version Compare-And-Swap predicates (`row_version = @expected_version`).
  4. In `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresIdempotencyRepository.cs:99-123`, the repository relies directly on `Read Committed` snapshot semantics:
  ```csharp
  // ON CONFLICT waits for a concurrent inserter. Under READ COMMITTED this
  // follow-up statement receives a fresh snapshot and returns that row.
  await using NpgsqlCommand existing = transaction.CreateCommand(
      """
      select id, request_sha256
      from control.idempotency_records
      where tenant_id = @tenant_id
        and actor_id = @actor_id
        and operation = @operation
        and idempotency_key = @idempotency_key
        and retired_at is null
        and expires_at > @created_at
      """);
  ```
- **Concrete Failure Prevented:** Under `Repeatable Read`, after an `INSERT ... ON CONFLICT DO NOTHING` returned null due to waiting for a concurrent transaction's commit, the follow-up `SELECT` in the same transaction would evaluate against the original transaction snapshot, fail to observe the committed record, and throw `InvalidOperationException("The conflicting idempotency record could not be loaded.")`.

#### 3. Advisory Locks: Session vs Transaction Scope and Connection Pool Leaks
- **Documented Guidance:** PostgreSQL documentation (Section 9.27.10 *Advisory Lock Functions*) defines two scopes:
  - Session-scoped (`pg_advisory_lock`): Lock is held across transaction boundaries until explicitly released with `pg_advisory_unlock` or when the physical TCP backend connection closes. In connection-pooled applications (e.g. Npgsql pool, PgBouncer), unhandled exceptions, task cancellations, or missing unlock calls leave the connection in a "dirty" state holding the lock. Subsequent operations borrowing that connection inherit or block on leaked locks.
  - Transaction-scoped (`pg_advisory_xact_lock`, `pg_advisory_xact_lock_shared`): Automatically released by the database engine on `COMMIT` or `ROLLBACK`.
- **Repository Implementation:** Across all migrations and C# source code, the repository exclusively invokes transaction-scoped advisory locks:
  - `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresMigrationRunner.cs:34`:
    ```csharp
    await using (var advisoryLock = new NpgsqlCommand(
        "select pg_advisory_xact_lock(@lock_id)",
        connection,
        transaction))
    ```
  - `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/001_foundation.sql:8968-8970`:
    ```sql
    perform pg_catalog.pg_advisory_xact_lock_shared(1498897460, 1);
    perform pg_catalog.pg_advisory_xact_lock(
        pg_catalog.hashtextextended('yo4x:u0:tenant:' || target_tenant_id::text, 0));
    ```
  - Role security definitions in `src/BuildingBlocks/YO4X.Persistence.Postgres/Security/least_privilege_roles.sql:735-737` grant execution only on `pg_advisory_xact_lock*` functions and never grant session-level `pg_advisory_lock`.
- **Concrete Failure Prevented:** If `PostgresMigrationRunner.cs` or `acquire_u0_authority_lock()` had used `pg_advisory_lock`, a thread cancellation or timeout during schema migration or worker dispatch would return a physical connection holding lock `9079040001000001` or the tenant hash to the Npgsql pool, permanently blocking all subsequent migration runs and worker operations on other connections.

#### 4. Row-Level Security: ENABLE vs FORCE and Table Owner Bypass
- **Documented Guidance:** PostgreSQL documentation (Section 5.8 *Row Security Policies* and `ALTER TABLE`) states that `ENABLE ROW LEVEL SECURITY` applies policies to normal database users, but the **table owner** and superusers bypass RLS by default. To enforce RLS policies against the table owner role, `FORCE ROW LEVEL SECURITY` must be explicitly executed (`ALTER TABLE table_name FORCE ROW LEVEL SECURITY`).
- **Repository Implementation:** Every tenant-isolated table across the migrations applies both `ENABLE` and `FORCE ROW LEVEL SECURITY`:
  - `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/001_foundation.sql:18663-18664`:
    ```sql
    execute format('alter table %s enable row level security', target_table);
    execute format('alter table %s force row level security', target_table);
    ```
  - `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/002_user_operation_invocation_protocol.sql:734-735, 2181-2243`:
    ```sql
    alter table control.user_operation_workload_identities enable row level security;
    alter table control.user_operation_workload_identities force row level security;
    ```
  - `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/011_projection_row_level_security.sql:53-55`:
    ```sql
    execute format('alter table %s enable row level security', target_table);
    execute format('alter table %s force row level security', target_table);
    ```
- **Concrete Failure Prevented:** If `FORCE ROW LEVEL SECURITY` were omitted, application services executing queries under the table owner role (such as during migration execution or direct backend maintenance queries) would bypass the `control.current_tenant_id()` policy check, resulting in cross-tenant data leaks whenever a query lacked an explicit `tenant_id` WHERE clause.

#### 5. Session State Interaction with Connection Pooling
- **Documented Guidance:** Standard PostgreSQL session-level commands (e.g. `SET var = val`) mutate state on the physical connection backend. In connection pools, this state leaks to subsequent transactions unless explicitly reset via `DISCARD ALL` or scoped to the transaction using `SET LOCAL var = val` / `set_config(name, val, true)`.
- **Repository Implementation:**
  1. The repository avoids using PostgreSQL GUC session configuration variables for tenancy entirely. Instead, `src/BuildingBlocks/YO4X.Persistence.Postgres/TenantPostgresTransaction.cs:112-139` activates a short-lived capability record in `control.tenant_context_capabilities` bound to `pg_catalog.pg_backend_pid()` and `pg_catalog.pg_current_xact_id()`.
  2. `control.current_tenant_id()` (`001_foundation.sql:145-169`) dynamically resolves the tenant ID using `pg_catalog.pg_current_xact_id_if_assigned()`:
     ```sql
     create or replace function control.current_tenant_id()
     returns uuid
     language sql
     stable
     security definer
     parallel restricted
     set search_path = ''
     as $$
         select capability.tenant_id
         from control.tenant_context_capabilities as capability
         where capability.database_oid =
                 (select database.oid
                  from pg_catalog.pg_database as database
                  where database.datname = current_database())
           and capability.database_name = current_database()
           and capability.runtime_role = session_user
           and capability.runtime_role_oid =
                 (select role.oid
                  from pg_catalog.pg_roles as role
                  where role.rolname = session_user)
           and capability.backend_pid = pg_catalog.pg_backend_pid()
           and capability.transaction_id = pg_catalog.pg_current_xact_id_if_assigned()
           and capability.activated_at is not null
           and capability.expires_at > statement_timestamp()
     $$;
     ```
  3. When the transaction commits or rolls back, the transaction ID ceases to exist and `current_tenant_id()` immediately returns NULL.
  4. In `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRuntimeConnectionPolicy.cs:12-25`, the runtime policy explicitly validates connection strings to ensure `!options.NoResetOnClose` (guaranteeing Npgsql issues `DISCARD ALL` on connection return) and forbids `options.Multiplexing`, custom GUC `Options`, and non-empty `SearchPath`.
- **Concrete Failure Prevented:** Bypassing `DISCARD ALL` (e.g. configuring `No Reset On Close=true`) while using session GUCs would allow a pooled connection previously used by Tenant A to retain Tenant A's context when checked out by a subsequent request for Tenant B. Binding context validation to `pg_current_xact_id_if_assigned()` and enforcing `NoResetOnClose=false` eliminates this vulnerability.

## Referrals
None.

## Coverage gaps
None.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 168.4s | 334286 tok | id=a186e71c-cb49-4ffd-aaa5-8fc0acdb2758
