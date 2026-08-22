# YO4X

This repository contains the staged YO4X control plane, runtime boundaries, PostgreSQL persistence, and operational web application described in `docs/`. The backend is an ASP.NET Core modular monolith with separate executable boundaries for the control plane, admin BFF, emergency safety control, secret ingestion, Supervisor, StrategyHost, and GatewayHost. The frontend is a typed React/Vite application under `src/Frontend/YO4X.Web`.

Only the U0/A0-A3 safety foundation is active. The code does **not** authorize live trading, local execution, public registration, general semantic MQ5 conversion, arbitrary broker orders, or production gateway use. The supplied MQL5 corpus is treated as untrusted private source: static inventory and immutable persistence are supported, but a file is never described as converted, compiled, or runtime-proven without the corresponding evidence.

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

The deterministic corpus report is at `docs/backend/MQ5_COMPATIBILITY_REPORT.md`; its machine-readable per-file manifest is `docs/backend/mq5-static-manifest.v1.json`. The checked corpus contains 198 exact `.mq5`/`.mqh` files (13,100,995 bytes). All 198 remain explicitly unproven for semantic conversion, compilation, and runtime behavior.

The supplied vendor dependency is pinned from `mt5-net-api-full-binaries-main/mt5api.dll`. It is used only as a compile-time reference by the MT5 adapter, is not copied to application output, and is not executed by the build or test suite. See `docs/backend/MT5_VENDOR_ARTIFACT_U0.md` for the exact artifact inventory and unresolved release gates.

No application starts with fabricated business data. The frontend's visual fixture is development/test-only, requires an explicit query flag, and is excluded from production data loading.

The latest verified local evidence is recorded in `docs/backend/IMPLEMENTATION_STATUS.md`: a warning-free Release build, all 385 unique .NET test cases green in their required environments (including 18/18 on disposable PostgreSQL 18.6), 13/13 frontend tests, clean TypeScript and production builds, and zero known NuGet/npm audit findings. These results do not claim an MT5 login or trade; the broker gateway remains deliberately submission-disabled until the documented isolation, licence, secret-provider, semantic-parity, and runtime-risk gates are satisfied.
