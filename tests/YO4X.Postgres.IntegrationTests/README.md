# PostgreSQL integration tests

These tests execute the embedded production migration against a real PostgreSQL
server. They support either Docker/Testcontainers or the workspace-local
Windows harness.

The optional Testcontainers fallback uses the multi-architecture Docker
Official Image `postgres:18.6-alpine3.23` pinned by manifest digest. Both paths
reject servers outside PostgreSQL major version 18 before creating a test
database.

On Windows without Docker, run from the repository root:

```powershell
.\scripts\Test-PostgresIntegration.ps1
```

Use `-Filter` for a focused run, for example:

```powershell
.\scripts\Test-PostgresIntegration.ps1 `
  -Filter 'FullyQualifiedName~StrategyImportPostgresTests'
```

The harness:

- downloads the PostgreSQL 18.6-1 x64 ZIP linked by PostgreSQL.org from EDB;
- verifies the pinned archive length and SHA-256 before extraction;
- verifies hashes for `postgres`, `initdb`, `pg_ctl`, and `psql` before every run;
- uses only Microsoft-signed Visual C++ runtime DLLs already present on the host;
- initializes a new loopback-only cluster with a random administrator password;
- passes the administrator connection to the test process through
  `YO4X_POSTGRES_INTEGRATION_ADMIN` without printing it; and
- stops and permanently removes the disposable data cluster in `finally`.

The downloaded archive and verified binaries remain in the ignored `.tools`
cache. No Windows service, registry entry, machine PATH, or system PostgreSQL
installation is created.

`YO4X_POSTGRES_INTEGRATION_ADMIN` is intentionally restricted by the fixture to
password-authenticated loopback servers running PostgreSQL major version 18.
Do not point it at a shared or production database: the suite creates databases
and login roles.

Artifact provenance and executable hashes are pinned in
`scripts/postgresql-windows-x64.lock.json`.

The strategy-import integration coverage uses the real
`PostgresMql5CorpusStore` and an exact `yo4x_conversion_worker` login. It checks
session-user authorization, immutable replay, deterministic concurrent replay,
transaction rollback, enum-shaped manifest evidence, audit/outbox evidence,
and a static-only persistence pass over all 198 supplied MQL5 source files.
These tests never execute MQL5 or connect to MetaTrader.

Credential lifecycle coverage connects as the exact control, secret-ingestion,
and worker roles. It verifies execute-only reservation/completion, stable opaque
references during rotation, database-clock expiry recovery for create and
rotate grants, exact cleanup replay, forged-authority rejection, and confirmed
broker delete/rotation projections without granting raw credential or account
mutation authority to runtime roles.

Latest verified run (2026-08-22 UTC): PostgreSQL 18.6, 18 passed, 0 failed,
0 skipped, with zero disposable run directories remaining after cleanup.
