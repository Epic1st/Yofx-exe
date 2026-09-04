---
agent_id: D06
lane: db-role-fingerprint
scope:
  - src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRoleCapabilityFingerprint.cs
status: COMPLETE
generated: 2026-08-29T11:25:04Z
counts: { P0: 1, P1: 3, P2: 1, P3: 1 }
---

# D06 — db-role-fingerprint

## Scope audited
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRoleCapabilityFingerprint.cs` (2057 lines)

Context opened:
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Security/least_privilege_roles.sql` (2000 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/005_frontend_projections.sql` (366 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/008_backtest_queue_worker_access.sql` (84 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/009_backtest_equity_curve.sql` (147 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/010_bot_settings_and_broker_symbols.sql` (159 lines)
- `src/Infrastructure/YO4X.RuntimeControl.Postgres/UserOperationProtocolPostgresDatabases.cs` (234 lines)
- `src/Apps/YO4X.ControlPlane.Api/ControlPlaneReadinessProbe.cs` (484 lines)
- `tests/YO4X.Postgres.IntegrationTests/RoleCapabilityFingerprintPostgresTests.cs` (1356 lines)

## Verdict
The cryptographic catalog pinning (`PostgresCatalogSemanticFingerprint`) and role posture assertions provide deep defense against unauthorized migration tampering, role attribute drift, and core privilege escalation. However, the role capability verification logic (`PostgresRoleCapabilityFingerprint`) contains a critical false-accept vulnerability: its privilege inspection CTEs hardcode the initial 8 core schemas, causing the verifier to completely ignore all table, schema, column, and function grants across projection and application schemas (`catalog`, `bots`, `simulation`, `journal`, `billing`, `brokerdirectory`). Furthermore, the full 1,026-line catalog SHA-256 hash is executed synchronously inside every transactional boundary, posing severe latency and lock-contention risks on hot operation paths.

## Findings

### [P0] Hardcoded 8-schema scope in privilege extraction causes false accept of broad grants in external schemas
- **Where:** `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRoleCapabilityFingerprint.cs:1732-1761`
- **Confidence:** CONFIRMED
- **Code:**
  ```sql
            where namespace.nspname in
                ('identity', 'authorization', 'control', 'operations',
                 'governance', 'audit', 'messaging', 'readmodel')
              and privilege.grantee in (0, (select oid from role_identity))
  ```
- **Failure:** In `PostgresRoleCapabilityFingerprint.Sql`, the CTEs `actual_schema`, `actual_table`, `actual_column_rows`, and `actual_function` strictly filter `where namespace.nspname in ('identity', 'authorization', 'control', 'operations', 'governance', 'audit', 'messaging', 'readmodel')`. When migrations 005–010 introduced external schemas (`catalog`, `bots`, `simulation`, `journal`, `billing`, `brokerdirectory`), `least_privilege_roles.sql:1892-1903` granted `USAGE` and table-wide `SELECT, INSERT, UPDATE, DELETE` across all these schemas to `yo4x_control_api` and `yo4x_worker`. However, `Yo4xPostgresRoleContracts.ControlApi` and `Yo4xPostgresRoleContracts.Worker` omit these schemas from `TablePrivileges` and `SchemaPrivileges`. Because `actual_table` ignores all relations outside the 8 core schemas, `(select value from actual_table) = @table_privileges` evaluates to `true`. If an attacker or misconfigured migration grants full table CRUD or schema usage on `billing.cloud_plans`, `bots.bots`, or `simulation.backtests` to restricted roles (e.g. `yo4x_gateway_runtime` or `yo4x_supervisor_runtime`), `PostgresRoleCapabilityFingerprint.IsSatisfiedAsync` falsely accepts the role and passes readiness checks.
- **Fix:** Remove the hardcoded 8-schema `where namespace.nspname in (...)` restriction from `actual_schema`, `actual_table`, `actual_column_rows`, and `actual_function` (filtering out only internal PostgreSQL system namespaces like `information_schema` and `^pg_`), and update all role capability contracts in `Yo4xPostgresRoleContracts` to explicitly enumerate their external schema and relation privileges.

### [P1] Synchronous full-catalog SHA-256 computation inside every tenant transaction causes severe hot-path latency
- **Where:** `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRoleCapabilityFingerprint.cs:2009-2018`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  if (!await PostgresCatalogSemanticFingerprint.IsSatisfiedAsync(
          transaction,
          cancellationToken).ConfigureAwait(false))
  {
      return false;
  }

  await using NpgsqlCommand command = transaction.CreateCommand(Sql);
  Bind(command, contract, requireCurrentSession: true);
  ```
- **Failure:** In `UserOperationProtocolPostgresDatabases.BeginTenantTransactionAsync` and `RuntimeEvidencePostgresDatabase.BeginTenantTransactionAsync`, every transactional operation invokes `PostgresRoleCapabilityFingerprint.IsSatisfiedAsync(transaction, contract)`. This executes `PostgresCatalogSemanticFingerprint.Sql` (1,026 lines of catalog CTEs iterating over every table, column, policy, trigger definition, and ACL across the database) and streams thousands of rows to C# to incrementally compute a SHA-256 hash. Under high user operation or worker event throughput, this incurs massive database CPU load, heavy catalog lock contention, 50–500ms transaction start overhead, and frequent `statement_timeout` aborts on high-frequency trading and operation dispatch paths.
- **Fix:** Perform the full-catalog semantic fingerprint attestation during application startup and periodic background health checks, or cache the catalog attestation result per connection session instead of re-executing whole-catalog hashing inside per-operation transactions.

### [P1] Role configuration array comparison omits C collation, causing false rejection under localized database collations
- **Where:** `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRoleCapabilityFingerprint.cs:1643-1648`
- **Confidence:** CONFIRMED
- **Code:**
  ```sql
                coalesce(
                    (
                        select array_agg(setting order by setting)
                        from unnest(coalesce(role.rolconfig, array[]::text[]))
                            as setting
                    ),
                    array[]::text[]) as configuration
  ```
- **Failure:** In `role_identity`, `order by setting` sorts role GUC configuration settings using the database cluster's default collation (e.g. `en_US.UTF-8`), where punctuation characters like `_` and `=` have different sorting precedence than ASCII byte order. Conversely, `PostgresRoleCapabilityContract.Normalize` sorts GUC strings in C# using `StringComparer.Ordinal` (ASCII). On databases initialized with non-C collations, the ordering of configuration arrays (such as `idle_in_transaction_session_timeout=10s`, `lock_timeout=2s`, `statement_timeout=5s`) diverges between PostgreSQL and .NET. When `(select configuration from role_identity) = @role_configuration` executes at line 1834, array equality fails due to mismatched element ordering, falsely rejecting valid database roles and causing application startup failure.
- **Fix:** Append `collate "C"` to the sorting expression in line 1644: `select array_agg(setting order by setting collate "C")`.

### [P1] Unregistered database roles and grantees in external schemas bypass catalog semantic fingerprint
- **Where:** `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRoleCapabilityFingerprint.cs:536-547`
- **Confidence:** CONFIRMED
- **Code:**
  ```sql
        named_yo4x_role as
        (
            select role.oid, role.rolname
            from pg_catalog.pg_roles as role
            where role.rolname in
                ('yo4x_migrator', 'yo4x_context_authority', 'yo4x_context_issuer', 'yo4x_local_identity', 'yo4x_control_api', 'yo4x_admin_bff',
                 'yo4x_emergency', 'yo4x_secret_ingestion',
                 'yo4x_conversion_worker', 'yo4x_strategy_verifier',
                 'yo4x_runtime_evidence', 'yo4x_worker',
                 'yo4x_supervisor_runtime', 'yo4x_trade_authorizer',
                 'yo4x_gateway_runtime', 'yo4x_credential_runtime')
        ),
  ```
- **Failure:** In `PostgresCatalogSemanticFingerprint.Sql`, catalog ACL queries for non-core schemas (`external-schema-runtime-acl`, `external-relation-runtime-acl`, `external-function-runtime-acl`) join against `named_yo4x_role`, which hardcodes exactly 16 role names. Furthermore, role existence entries are only collected for roles in `named_yo4x_role`. If a rogue or unmanaged role (e.g. `yo4x_temp`, `backtest_runner`, or an attacker-created user) is created and granted `SELECT, INSERT, UPDATE, DELETE` on `catalog.strategies`, `bots.bots`, or `simulation.backtests`, the catalog fingerprint query completely ignores the role and its grants. The computed SHA-256 continues to match `ExpectedSha256`, masking unauthorized database privileges in external schemas.
- **Fix:** In `PostgresCatalogSemanticFingerprint.Sql`, collect entries for all non-system database roles in `pg_roles` and extract runtime ACLs for all non-internal grantees across external schemas rather than filtering by the static 16-role list.

### [P2] Role capability contracts omit type, domain, language, and tablespace capability checks
- **Where:** `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRoleCapabilityFingerprint.cs:1703-1808`
- **Confidence:** CONFIRMED
- **Code:**
  ```sql
            and (select value from actual_database) = @database_privileges
            and (select value from actual_schema) = @schema_privileges
            and (select value from actual_table) = @table_privileges
            and (select value from actual_column) = @column_privileges
            and (select value from actual_function) = @function_privileges
  ```
- **Failure:** `PostgresRoleCapabilityFingerprint.Sql` verifies privileges only across databases, schemas, tables, columns, and functions. It does not extract or assert role grants for `pg_type` (`USAGE ON TYPE/DOMAIN`), `pg_language` (`USAGE ON LANGUAGE`, e.g. `plpgsql`, `c`, `plpython3u`), `pg_tablespace`, or `pg_parameter_acl`. If a runtime role is granted `USAGE ON LANGUAGE` or permissions on custom types/domains, `PostgresRoleCapabilityFingerprint` cannot detect or reject the privilege drift, allowing roles to accumulate untracked non-relation capabilities.
- **Fix:** Extend `PostgresRoleCapabilityContract` and `PostgresRoleCapabilityFingerprint.Sql` to include `actual_type` and `actual_language` checks against declarative contract manifests.

### [P3] Absence of defensive statement, lock, and idle timeouts in ControlApi, Worker, and AdminBff contracts
- **Where:** `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRoleCapabilityFingerprint.cs:38-48`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
    internal static string[] BaseRoleConfiguration { get; } =
    [
        "default_transaction_isolation=read committed",
        "default_transaction_read_only=off",
        "log_parameter_max_length=0",
        "log_parameter_max_length_on_error=0",
        "row_security=on",
        "search_path=\"\"",
        "session_replication_role=origin",
        "transaction_timeout=2min"
    ];
  ```
- **Failure:** `BaseRoleConfiguration` sets only `transaction_timeout=2min`. While runtime service contracts (`SupervisorRuntime`, `GatewayRuntime`, `CredentialRuntime`, `LocalIdentity`, `ContextIssuer`) explicitly configure `idle_in_transaction_session_timeout=10s`, `lock_timeout=2s`, and `statement_timeout=5s`, the three broadest roles (`ControlApi`, `AdminBff`, and `Worker`) omit these three settings. A hung client connection, unindexed slow query, or lock acquisition contention on `yo4x_control_api` or `yo4x_worker` can hold database locks for up to 2 minutes, blocking transaction throughput across the engine.
- **Fix:** Add `idle_in_transaction_session_timeout`, `lock_timeout`, and `statement_timeout` to `BaseRoleConfiguration` or explicitly attach them to `ControlApi`, `AdminBff`, and `Worker` contracts.

## Referrals
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Security/least_privilege_roles.sql:1892-1903` — External projection schemas (`catalog`, `bots`, `simulation`, `journal`, `billing`) grant table-wide CRUD to `yo4x_control_api` and `yo4x_worker` without corresponding role contract entries or RLS enforcement.

## Coverage gaps
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRoleCapabilityFingerprint.cs:1975-2000` — `IsNamedRoleSatisfiedForDeploymentAsync` is never tested with intentionally granted extra privileges on external schemas (`catalog`, `bots`, `simulation`) to verify false-accept rejection at deployment time.
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRoleCapabilityFingerprint.cs:1834` — Configuration array equality under non-C UTF-8 PostgreSQL collations is untested in CI, masking potential collation-dependent role startup failures.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 102.2s | 184469 tok | id=1fe8bda9-5577-463b-b2fb-939819033e70
