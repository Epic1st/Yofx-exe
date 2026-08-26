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

The deterministic corpus report is at `docs/backend/MQ5_COMPATIBILITY_REPORT.md`; its machine-readable per-file manifest is `docs/backend/mq5-static-manifest.v1.json`. The checked security-sanitized current corpus contains 198 exact byte-preserved `.mq5`/`.mqh` files (166 programs and 32 headers; exactly 12,979,438 bytes) under corpus SHA-256 `9a53e844cfd3ffe5dfcf28544bb4909ce69741ac6a373e80b139f8227779dd47`. All 198 files are transactionally persisted through the production PostgreSQL store with exact inventory evidence. The restricted-subset compiler attempted the 30 isolated-type-check candidates: 2 data-only translation units lowered and 28 failed closed. No executable strategy, MetaEditor compile proof, reference-parity proof, or runtime strategy proof exists.

The supplied vendor dependency is SHA-pinned from `mt5-net-api-full-binaries-main/mt5api.dll`. Ordinary application builds/tests and the mutation GatewayHost do not load it. A separate manifest-pinned Windows worker may load it only for bounded connect/identity-read/disconnect probing; that path exposes no order or strategy-execution method. The credential-bearing vendor example is deleted from the current tree, but its historical credentials remain an external rotation/history-remediation release blocker. See `docs/backend/MT5_VENDOR_ARTIFACT_U0.md` for the exact artifact inventory and unresolved release gates.

The local Windows credential boundary imports explicitly approved demo credentials into a user-bound DPAPI vault and emits v3 destination/tool-bound evidence. GatewayHost cannot consume that vault; only the dedicated connection-probe child may open one opaque binding for a bounded canary. On 2026-08-24 that backend canary authenticated to `VantageMarkets-Demo` through a `search.mtapi.io`-resolved access node using the exact pinned DLL, read bounded account metadata, and confirmed disconnect. No plaintext credential was rendered and no order was sent. Redacted evidence is at `artifacts/verification/mt5/vantage-demo-connection-canary.v1.json`.

The MetaTrader 5 broker-server directory is imported offline, never at request time. `YO4X.Mt5.BrokerCatalogueImport fetch` sweeps every two-character company substring of `search.mtapi.io/Search?mt5=true` (the vendor exposes no list-everything route) into one canonical digest-named artifact; `... import` verifies that digest and loads it into the `brokerdirectory` schema that `007_broker_server_catalogue.sql` creates. The 2026-08-25 snapshot holds 3,237 companies and 4,506 servers under artifact SHA-256 `d815646f09be02e3b133ef1758e00c7aedd2986417492ee52ff17d21469fafc8`. The ControlPlane API never calls the vendor: it serves `GET /v1/broker-account-registration-options?query=` from PostgreSQL.

Directory rows are reference data, not approval. A server becomes linkable only when a signed-in user approves it for their own tenant through `POST /v1/broker-server-approvals`, which the API can perform solely by calling the `brokerdirectory.approve_demo_server` SECURITY DEFINER capability; the `yo4x_control_api` role holds no write grant on `governance.broker_profiles` or on any directory table. A profile promoted that way is demo-only, carries `trading: false` and no passed compatibility run, so deployment validation still refuses it, and `007` tightens the pending broker-account guard so such a profile additionally requires that tenant’s own approval row.

The durable broker-command schema, least-privilege gateway lifecycle role, coordinator, one-shot GatewayHost composition, and authenticated one-request child-process transport are implemented as a proof-only boundary. Requested-v4/result-v5 and reconciliation-challenge database/C# scaffolding, including production `PostgresUserOperationWorkStore` integration, is also implemented and tested with a non-broker provider. GatewayHost does not reference the MT5 adapter; the child currently composes only a no-vendor-call proof executor. Production broker-command authorization is hard-disabled, GatewayHost seals `SubmissionEnabled` false, and production reconciliation persists no conclusive terminal broker outcome. Authenticated requested-v4 transport/consumer/provider/restart wiring and a trusted risk authority remain open implementation blockers. See `docs/backend/DURABLE_BROKER_COMMAND_PIPELINE.md` and `docs/backend/USER_OPERATION_INVOCATION_PROTOCOL.md`.

No application starts with fabricated business data. The frontend's visual fixture is development/test-only and requires an explicit query flag there. Outside development/test mode the flag is ignored and the fixture branch is excluded; a correctly configured HTTPS production build uses the normal typed API data source and issues ordinary API requests.

Fresh full-tree verification (2026-08-24, this working tree): the Release solution builds with zero warnings and zero errors; the thirteen non-PostgreSQL xUnit v3 suites report 1,102 passed / 0 failed / 0 skipped, including 159 in `YO4X.Domain.Tests`; a fresh disposable PostgreSQL 18 integration pass reports 106 passed / 0 failed / 0 skipped in 11m38s; frontend TypeScript checks, 89 vitest tests across 6 files, the 43-module production build, desktop/mobile browser QA, and the npm audit are all green. Direct and transitive NuGet audits report no known vulnerable or deprecated packages. The complete evidence table, refreshed baseline pins, and the open implementation and external blocker list are recorded in `docs/backend/CLOSURE_LEDGER_2026-08-23.md`.

Frontend verification independently passes TypeScript, 6 test files with 89 tests, the 43-module production build, and a zero-vulnerability npm audit. The separate connection-only backend canary proves one demo login and confirmed disconnect; it does not prove strategy execution or a trade. The mutation broker gateway remains submission-disabled until the documented isolation, licence, secret-provider, semantic-parity, trusted-risk-authority, authenticated requested-v4 transport/consumer/provider/restart wiring, authenticated reconciliation, and runtime-containment gates are satisfied.
