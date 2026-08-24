# YO4X

This repository contains the staged YO4X control plane, runtime boundaries, PostgreSQL persistence, and operational web application described in `docs/`. The backend is an ASP.NET Core modular monolith with separate executable boundaries for the control plane, admin BFF, emergency safety control, secret ingestion, Supervisor, StrategyHost, and GatewayHost. The frontend is a typed React/Vite application under `src/Frontend/YO4X.Web`.

Only the U0/A0-A3 safety foundation is active. The code does **not** authorize live trading, local execution, public registration, general semantic MQ5 conversion, arbitrary broker orders, or production gateway use. The supplied MQL5 corpus is treated as untrusted private source: static inventory and immutable persistence are supported, but a file is never described as converted, compiled, or runtime-proven without the corresponding evidence.

PostgreSQL currently uses an explicit pre-release greenfield baseline. See
[`docs/backend/POSTGRESQL_BASELINE_POLICY.md`](docs/backend/POSTGRESQL_BASELINE_POLICY.md)
for checksum immutability, fail-closed startup, and operator backup/reset or
staged-upgrade requirements.

## Prerequisites

- .NET SDK 10.0.400 or a compatible 10.0 patch.
- PostgreSQL 18 for runtime persistence. Docker/Compose is optional because the repository also includes a hash-pinned, disposable Windows PostgreSQL 18 test harness.
- Node.js 22.22.2 or later for the frontend.

## Verify

```powershell
dotnet restore YO4X.sln
dotnet build YO4X.sln --no-restore
dotnet test YO4X.sln --no-build
```

Run the integration suite against a disposable, loopback-only PostgreSQL 18 instance on Windows with:

```powershell
.\scripts\Test-PostgresIntegration.ps1
```

The harness verifies the PostgreSQL archive and executable hashes, uses SCRAM authentication with an ephemeral password, verifies the exact server version, and removes only its validated per-run child directory after the server stops.

Verify the frontend with:

```powershell
Set-Location src\Frontend\YO4X.Web
npm ci
npm run typecheck
npm run test:run
npm run build
```

Persistence integration tests use a real PostgreSQL server. They never substitute SQLite or an in-memory database. To use Compose instead of the portable Windows harness:

```powershell
Copy-Item .env.example .env
# Replace the placeholder password in .env before continuing.
docker compose up -d postgres
```

The local Compose database is bound to loopback and does not enable TLS. It is a developer-only dependency; production PostgreSQL must use TLS, managed backups, point-in-time recovery, multi-zone placement, separate migration/runtime roles, and tested restores.

## Layout

```text
src/BuildingBlocks/   Cross-cutting domain, HTTP, and PostgreSQL adapters
src/Modules/          One project per logical backend module
src/Apps/             Independently deployable control-plane boundaries
src/Runtime/          Separate Supervisor, StrategyHost, and GatewayHost processes
src/Frontend/         Read-only React/Vite operational web application
tests/                Domain, architecture, API, runtime, and PostgreSQL tests
docs/decisions/       Implementation decisions and unresolved provider choices
```

The deterministic corpus report is at `docs/backend/MQ5_COMPATIBILITY_REPORT.md`; its machine-readable per-file manifest is `docs/backend/mq5-static-manifest.v1.json`. The checked corpus contains 198 exact byte-preserved `.mq5`/`.mqh` files (166 programs and 32 headers; 12,979,438 bytes) under corpus SHA-256 `9a53e844cfd3ffe5dfcf28544bb4909ce69741ac6a373e80b139f8227779dd47`. Static dispositions are 68 `NeedsSemanticValidation`, 3 `NeedsSource`, and 127 `Unsupported`. The separate conversion-evidence pass classifies 30 as awaiting isolated type-check, 1 as all-NUL source, 1 as binary source, 37 as blocked on external dependency snapshots, 2 as invalid syntax, 6 as missing dependencies, and 121 as unsupported semantics. High-confidence secrets are rejected before analysis, artifact generation, compilation planning, or persistence. Full grammar, type-check, restricted-IR lowering, semantic conversion, MetaEditor compilation, reference-parity, and runtime proof counts are all zero.

The supplied vendor dependency is pinned from `mt5-net-api-full-binaries-main/mt5api.dll`. It is used only as a compile-time reference by the MT5 adapter, is not copied to application output, and is not executed by the build or test suite. The credential-bearing vendor example is deleted from the current tree, but its historical credentials remain an external rotation/history-remediation release blocker. The current v4 host probe is an unsigned, non-executing observation and reports `isolated_runner_not_configured`; it does not authorize compilation or supplied-MQL execution. See `docs/backend/MT5_VENDOR_ARTIFACT_U0.md` for the exact artifact inventory and unresolved release gates.

The local Windows credential boundary can import explicitly approved demo credentials into a user-bound DPAPI vault and emit v3 destination/tool-bound evidence, but GatewayHost cannot consume that vault. The ignored host-local replay evidence is not broker-login evidence and never authorizes a command. See `docs/backend/LOCAL_MT5_CREDENTIAL_BOUNDARY.md`.

The durable broker-command schema, least-privilege gateway lifecycle role, coordinator, one-shot GatewayHost composition, and authenticated one-request child-process transport are implemented as a proof-only boundary. GatewayHost does not reference the MT5 adapter; the child currently composes only a no-vendor-call proof executor. Production broker-command authorization is hard-disabled, GatewayHost seals `SubmissionEnabled` false, and production reconciliation persists no conclusive terminal broker outcome. See `docs/backend/DURABLE_BROKER_COMMAND_PIPELINE.md`.

No application starts with fabricated business data. The frontend's visual fixture is development/test-only, requires an explicit query flag, and is excluded from production data loading.

Fresh full-tree verification (2026-08-23, this working tree): the Release solution builds with zero warnings and zero errors; the thirteen xUnit v3 suites pass 1,101 tests; a fresh disposable PostgreSQL 18 integration pass runs 100 tests; frontend TypeScript checks, vitest, production build, and npm audit are all green. Direct and transitive NuGet audits report no known vulnerable or deprecated packages. The complete evidence table, refreshed baseline pins, and the open external blocker list are recorded in `docs/backend/CLOSURE_LEDGER_2026-08-23.md`.

Frontend verification independently passes TypeScript, 6 test files with 89 tests, the 43-module production build, and a zero-vulnerability npm audit. None of this evidence claims an MT5 login, strategy execution, or trade; the broker gateway remains deliberately submission-disabled until the documented isolation, licence, secret-provider, semantic-parity, trusted-risk-authority, authenticated-reconciliation, and runtime-containment gates are satisfied.
