You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRoleCapabilityFingerprint.cs

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (6):

[1] [P0] Hardcoded 8-schema scope in privilege extraction causes false accept of broad grants in external schemas
    Where:   src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRoleCapabilityFingerprint.cs:1732-1761
    Failure: In `PostgresRoleCapabilityFingerprint.Sql`, the CTEs `actual_schema`, `actual_table`, `actual_column_rows`, and `actual_function` strictly filter `where namespace.nspname in ('identity', 'authorization', 'control', 'operations', 'governance', 'audit', 'messaging', 'readmodel')`. When migrations 005–010 introduced external schemas (`catalog`, `bots`, `simulation`, `journal`, `billing`, `brokerdirectory`), `least_privilege_roles.sql:1892-1903` granted `USAGE` and table-wide `SELECT, INSERT, UPDATE, DELETE` across all these schemas to `yo4x_control_api` and `yo4x_worker`. However, `Yo4xPostgresRoleContracts.ControlApi` and `Yo4xPostgresRoleContracts.Worker` omit these schemas from `TablePrivileges` and `SchemaPrivileges`. Because `actual_table` ignores all relations outside the 8 core schemas, `(select value from actual_table) = @table_privileges` evaluates to `true`. If an attacker or misconfigured migration grants full table CRUD or schema usage on `billing.cloud_plans`, `bots.bots`, or `simulation.backtests` to restricted roles (e.g. `yo4x_gateway_runtime` or `yo4x_supervisor_runtime`), `PostgresRoleCapabilityFingerprint.IsSatisfiedAsync` falsely accepts the role and passes readiness checks.
    Suggested fix: Remove the hardcoded 8-schema `where namespace.nspname in (...)` restriction from `actual_schema`, `actual_table`, `actual_column_rows`, and `actual_function` (filtering out only internal PostgreSQL system namespaces like `information_schema` and `^pg_`), and update all role capability contracts in `Yo4xPostgresRoleContracts` to explicitly enumerate their external schema and relation privileges.

[2] [P1] Role configuration array comparison omits C collation, causing false rejection under localized database collations
    Where:   src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRoleCapabilityFingerprint.cs:1643-1648
    Failure: In `role_identity`, `order by setting` sorts role GUC configuration settings using the database cluster's default collation (e.g. `en_US.UTF-8`), where punctuation characters like `_` and `=` have different sorting precedence than ASCII byte order. Conversely, `PostgresRoleCapabilityContract.Normalize` sorts GUC strings in C# using `StringComparer.Ordinal` (ASCII). On databases initialized with non-C collations, the ordering of configuration arrays (such as `idle_in_transaction_session_timeout=10s`, `lock_timeout=2s`, `statement_timeout=5s`) diverges between PostgreSQL and .NET. When `(select configuration from role_identity) = @role_configuration` executes at line 1834, array equality fails due to mismatched element ordering, falsely rejecting valid database roles and causing application startup failure.
    Suggested fix: Append `collate "C"` to the sorting expression in line 1644: `select array_agg(setting order by setting collate "C")`.

[3] [P1] Synchronous full-catalog SHA-256 computation inside every tenant transaction causes severe hot-path latency
    Where:   src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRoleCapabilityFingerprint.cs:2009-2018
    Failure: In `UserOperationProtocolPostgresDatabases.BeginTenantTransactionAsync` and `RuntimeEvidencePostgresDatabase.BeginTenantTransactionAsync`, every transactional operation invokes `PostgresRoleCapabilityFingerprint.IsSatisfiedAsync(transaction, contract)`. This executes `PostgresCatalogSemanticFingerprint.Sql` (1,026 lines of catalog CTEs iterating over every table, column, policy, trigger definition, and ACL across the database) and streams thousands of rows to C# to incrementally compute a SHA-256 hash. Under high user operation or worker event throughput, this incurs massive database CPU load, heavy catalog lock contention, 50–500ms transaction start overhead, and frequent `statement_timeout` aborts on high-frequency trading and operation dispatch paths.
    Suggested fix: Perform the full-catalog semantic fingerprint attestation during application startup and periodic background health checks, or cache the catalog attestation result per connection session instead of re-executing whole-catalog hashing inside per-operation transactions.

[4] [P1] Unregistered database roles and grantees in external schemas bypass catalog semantic fingerprint
    Where:   src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRoleCapabilityFingerprint.cs:536-547
    Failure: In `PostgresCatalogSemanticFingerprint.Sql`, catalog ACL queries for non-core schemas (`external-schema-runtime-acl`, `external-relation-runtime-acl`, `external-function-runtime-acl`) join against `named_yo4x_role`, which hardcodes exactly 16 role names. Furthermore, role existence entries are only collected for roles in `named_yo4x_role`. If a rogue or unmanaged role (e.g. `yo4x_temp`, `backtest_runner`, or an attacker-created user) is created and granted `SELECT, INSERT, UPDATE, DELETE` on `catalog.strategies`, `bots.bots`, or `simulation.backtests`, the catalog fingerprint query completely ignores the role and its grants. The computed SHA-256 continues to match `ExpectedSha256`, masking unauthorized database privileges in external schemas.
    Suggested fix: In `PostgresCatalogSemanticFingerprint.Sql`, collect entries for all non-system database roles in `pg_roles` and extract runtime ACLs for all non-internal grantees across external schemas rather than filtering by the static 16-role list.

[5] [P2] Role capability contracts omit type, domain, language, and tablespace capability checks
    Where:   src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRoleCapabilityFingerprint.cs:1703-1808
    Failure: `PostgresRoleCapabilityFingerprint.Sql` verifies privileges only across databases, schemas, tables, columns, and functions. It does not extract or assert role grants for `pg_type` (`USAGE ON TYPE/DOMAIN`), `pg_language` (`USAGE ON LANGUAGE`, e.g. `plpgsql`, `c`, `plpython3u`), `pg_tablespace`, or `pg_parameter_acl`. If a runtime role is granted `USAGE ON LANGUAGE` or permissions on custom types/domains, `PostgresRoleCapabilityFingerprint` cannot detect or reject the privilege drift, allowing roles to accumulate untracked non-relation capabilities.
    Suggested fix: Extend `PostgresRoleCapabilityContract` and `PostgresRoleCapabilityFingerprint.Sql` to include `actual_type` and `actual_language` checks against declarative contract manifests.

[6] [P3] Absence of defensive statement, lock, and idle timeouts in ControlApi, Worker, and AdminBff contracts
    Where:   src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRoleCapabilityFingerprint.cs:38-48
    Failure: `BaseRoleConfiguration` sets only `transaction_timeout=2min`. While runtime service contracts (`SupervisorRuntime`, `GatewayRuntime`, `CredentialRuntime`, `LocalIdentity`, `ContextIssuer`) explicitly configure `idle_in_transaction_session_timeout=10s`, `lock_timeout=2s`, and `statement_timeout=5s`, the three broadest roles (`ControlApi`, `AdminBff`, and `Worker`) omit these three settings. A hung client connection, unindexed slow query, or lock acquisition contention on `yo4x_control_api` or `yo4x_worker` can hold database locks for up to 2 minutes, blocking transaction throughput across the engine.
    Suggested fix: Add `idle_in_transaction_session_timeout`, `lock_timeout`, and `statement_timeout` to `BaseRoleConfiguration` or explicitly attach them to `ControlApi`, `AdminBff`, and `Worker` contracts.

HOW TO WORK:

1. Verify each finding against the actual code BEFORE changing anything. Line numbers may
   have shifted. If a finding is WRONG, or was already fixed, or the suggested fix would
   itself introduce a bug - do NOT apply it. Say so in your summary and move on. A refused
   bad fix is a good outcome; applying a wrong fix to a trading system is not.

2. Make the SMALLEST change that actually fixes the defect. Do not refactor, rename,
   reorder, reformat, restyle, or "improve" anything you were not asked about. Do not
   reflow existing lines. The diff must contain only the fix.

3. Match the surrounding code exactly - its naming, its comment density and voice, its
   error-handling idiom, its use of existing helpers. Read enough of the file to know what
   that is. Where the file already has a helper for what you need, use it rather than
   writing a new one.

4. Preserve public API and behaviour that was not identified as defective. If a correct
   fix would require changing a public signature, a database schema, a serialised contract,
   or shared behaviour outside this file, DO NOT do it - report it as needing a wider
   change instead.

5. This code decides real trades. For anything touching money, volume, price, margin, order
   state or time: be conservative, prefer failing closed over guessing, and preserve
   existing rounding/normalisation conventions unless the finding is specifically that the
   convention is wrong.

6. The project builds clean with zero warnings. Keep it that way - no unused variables, no
   unreachable code, no nullable warnings.

AFTER EDITING, output a short plain-text summary (no code fences), one line per finding:
  [n] APPLIED  - <what you changed, in a few words>
  [n] SKIPPED  - <why the finding was wrong or the fix unsafe>
Then a final line: FILES CHANGED: <the one path you edited, or NONE>

