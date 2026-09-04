---
agent_id: J07
lane: Architecture Documentation vs Code Reality
scope:
  - README.md
  - docs/ADMIN_SIDE_ARCHITECTURE.md
  - docs/USER_SIDE_ARCHITECTURE.md
  - docs/PHASE_U0_EXECUTION_PLAN.md
  - docs/decisions/0001-backend-foundation.md
  - docs/decisions/0002-u0-safety-defaults.md
  - docs/frontend/DESIGN_SYSTEM.md
  - src/Frontend/YO4X.Web/README.md
status: COMPLETE
generated: 2026-08-29T11:35:00Z
counts: { P0: 0, P1: 3, P2: 4, P3: 1 }
---

# J07 — Architecture Documentation vs Code Reality

## Scope audited
- `README.md` (83 lines)
- `docs/ADMIN_SIDE_ARCHITECTURE.md` (1503 lines)
- `docs/USER_SIDE_ARCHITECTURE.md` (2082 lines)
- `docs/PHASE_U0_EXECUTION_PLAN.md` (382 lines)
- `docs/decisions/0001-backend-foundation.md` (37 lines)
- `docs/decisions/0002-u0-safety-defaults.md` (23 lines)
- `docs/frontend/DESIGN_SYSTEM.md` (138 lines)
- `src/Frontend/YO4X.Web/README.md` (62 lines)

## Verdict
The core safety boundaries, U0 execution plan, and PostgreSQL baseline policies are rigorously maintained across documentation and backend code. However, there is significant architectural drift in the frontend and solution structure specifications: `docs/frontend/DESIGN_SYSTEM.md` and `src/Frontend/YO4X.Web/README.md` describe a rejected read-only dashboard concept with phantom tokens, non-existent `src/test-fixtures`, and missing UI components, while ADR 0001 and the architecture solution maps describe obsolete direct project references (`GatewayHost` to `YO4X.Trading.Mt5`) and 29 phantom C# projects rather than the actual `BuildingBlocks`/`Modules`/`Apps`/`Runtime` structure.

## Findings

### [P1] Design System tokens and typography diverge from frontend code
- **Where:** `docs/frontend/DESIGN_SYSTEM.md:15-34`
- **Confidence:** CONFIRMED
- **Code:**
  ```markdown
  | Role | Token | Value |
  |---|---|---|
  | Primary text | `--color-text` | `#0b1b43` |
  | Cobalt | `--color-cobalt` | `#075cf0` |
  | Success | `--color-success` | `#079447` |
  | Danger | `--color-danger` | `#e31b31` |
  | Sidebar | `--sidebar-width` | `270px` |
  ```
- **Failure:** Developers building new frontend components using `docs/frontend/DESIGN_SYSTEM.md` reference CSS tokens (`--color-cobalt`, `--color-success`, `--color-danger`, `#0b1b43`, 270px sidebar, `Inter` font) that do not exist in `src/Frontend/YO4X.Web/src/app/styles/tokens.css`. The actual codebase defines `--color-accent: #0684c4`, `--color-positive: #1f9d55`, `--color-negative: #b3261e`, `--color-text: #151a22`, `--sidebar-width: 236px`, and `--font-ui: "Instrument Sans"`, leading to broken component rendering, missing style fallbacks, and layout shifts.
- **Fix:** Update `docs/frontend/DESIGN_SYSTEM.md` token tables and typography to match the active design tokens in `src/Frontend/YO4X.Web/src/app/styles/tokens.css`.

### [P1] Design system specifies nonexistent component families and routes from rejected concept
- **Where:** `docs/frontend/DESIGN_SYSTEM.md:60-70`
- **Confidence:** CONFIRMED
- **Code:**
  ```markdown
  | Family | Responsibility |
  |---|---|
  | `AppShell` | Sidebar, top bar, footer, help modal, mobile navigation state |
  | `Sidebar` | Grouped navigation, selected state, keyboard-contained mobile drawer |
  | `TopBar` | Search, environment, service notices, user context |
  | `SummaryTiles` | Five high-level facts with semantic tone and a consistent icon frame |
  | `DeploymentReadiness` | Ordered proofs, explicit proven/pending/blocked/unavailable states, evidence dialogs |
  | `StrategyCompatibility` | Searchable table and non-authorizing report summary |
  | `RecentActivity` | Time-bound ControlPlane audit/activity projection |
  | `RuntimeReadiness` | Component projection with honest not-configured/unavailable states |
  ```
- **Failure:** Developers attempting to compose or automate tests against components documented in `DESIGN_SYSTEM.md` (`SummaryTiles`, `DeploymentReadiness`, `StrategyCompatibility`, `RuntimeReadiness`) fail because none of these components exist in `src/Frontend/YO4X.Web/src/shared/ui/` or `src/features/`. The codebase implements the "Bot Dashboard" hierarchy (`Dashboard`, `Strategies`, `My bots`, `Backtests`, `Compiler`, `Cloud runners`, `Journal`, `Settings`), causing automated browser QA (`scripts/visual-qa.mjs`) targeting the documented components to crash with selector timeouts.
- **Fix:** Update `docs/frontend/DESIGN_SYSTEM.md` to document the actual component library in `src/Frontend/YO4X.Web/src/shared/ui/` and feature routing in `src/Frontend/YO4X.Web/src/app/navigation.ts`.

### [P1] ADR 0001 asserts GatewayHost directly references `YO4X.Trading.Mt5` when code enforces process isolation
- **Where:** `docs/decisions/0001-backend-foundation.md:22`
- **Confidence:** CONFIRMED
- **Code:**
  ```markdown
  The runtime trio are actual OS processes/containers. StrategyHost has no credential, network, native-library, or trading-adapter dependency. Only `YO4X.Trading.Mt5` may reference `mt5api.dll`, and only GatewayHost may reference that adapter.
  ```
- **Failure:** An engineer referencing ADR 0001 adds code assuming `GatewayHost` directly references `YO4X.Trading.Mt5`. This causes architecture boundary tests (`ArchitectureBoundaryTests.cs:184-185`: `Assert.DoesNotContain("YO4X.Trading.Mt5", projectReferences)`) to fail and break CI, because `GatewayHost` references `YO4X.Trading.ProcessIsolation`, which spawns `YO4X.Mt5.WorkerHost` as an isolated child process over redirected stdin/stdout pipes.
- **Fix:** Update ADR 0001 to document that `GatewayHost` references `YO4X.Trading.ProcessIsolation`, and only the worker process `YO4X.Mt5.WorkerHost` references `YO4X.Trading.Mt5`.

### [P2] Frontend documentation mandates nonexistent `src/test-fixtures` directory and `?fixture=dashboard` flag
- **Where:** `src/Frontend/YO4X.Web/README.md:55-57`
- **Confidence:** CONFIRMED
- **Code:**
  ```markdown
  Representative design data is isolated in `src/test-fixtures`. It is reachable only in Vite development/test mode and only with the explicit query `?fixture=dashboard`. Outside those modes the query flag is ignored, a production build tree-shakes the fixture branch, and the application never falls back to fixture data after an API error.
  ```
- **Failure:** A developer following `src/Frontend/YO4X.Web/README.md` runs `npm run qa:browser` or attempts to launch Vite with `?fixture=dashboard`. Because `src/test-fixtures` does not exist and `App.tsx` contains no fixture branching logic, `scripts/visual-qa.mjs` times out waiting for `[data-dashboard-source="fixture"]`, halting local verification.
- **Fix:** Remove the phantom `src/test-fixtures` claims from `src/Frontend/YO4X.Web/README.md` (and `README.md:78`), and align `scripts/visual-qa.mjs` with the real API-driven mock and loading states.

### [P2] Desktop client architecture claims native WPF MVVM implementation when code is a WebView2 web wrapper
- **Where:** `docs/USER_SIDE_ARCHITECTURE.md:230-235`
- **Confidence:** CONFIRMED
- **Code:**
  ```markdown
  Recommended implementation:

  - .NET 10 LTS.
  - WPF with MVVM for a mature Windows-only desktop stack.
  - Dependency injection and async commands.
  - Self-contained x64 deployment.
  - Signed installer and signed updates. MSIX is the preferred candidate, but the installer spike must validate background-agent/service behavior, elevation, minimum Windows version, and update safety. A signed WiX/MSI installer remains the fallback.
  ```
- **Failure:** An engineer planning desktop UI changes writes native WPF views and view models against .NET backend services based on `USER_SIDE_ARCHITECTURE.md`. In reality, `src/Apps/YO4X.Desktop` contains only a minimal `MainWindow.xaml` hosting `Microsoft.Web.WebView2` pointing to the React frontend, meaning UI development in C#/WPF is dead-on-arrival and must be done in React/TypeScript.
- **Fix:** Clarify in `docs/USER_SIDE_ARCHITECTURE.md` Section 6.1 that `YO4X.Desktop` is implemented as a thin WPF shell hosting Microsoft.Web.WebView2 over the React frontend.

### [P2] Solution structure in User-Side Architecture lists 29 phantom projects that conflict with actual modular monolith structure
- **Where:** `docs/USER_SIDE_ARCHITECTURE.md:1860-1867`
- **Confidence:** CONFIRMED
- **Code:**
  ```markdown
  src/
  ├── YO4X.Desktop/                  WPF views, view models, desktop composition
  ├── YO4X.Agent/                    Local Runtime Supervisor and durable journal
  ├── YO4X.StrategyHost/             Restricted strategy process/container host
  ├── YO4X.GatewayHost/              Credential-bearing broker gateway host
  ├── YO4X.Runtime.Ipc/              Authenticated versioned host messages
  ├── YO4X.Application/              User use cases and orchestration contracts
  ├── YO4X.Domain/                   Core domain, state machines, risk concepts
  ├── YO4X.Infrastructure/           Storage, HTTP, IPC, vault adapters
  ```
- **Failure:** Developers attempting to add or modify components according to Section 24 look for non-existent projects (`YO4X.Agent`, `YO4X.Runtime.Ipc`, `YO4X.Strategy.IR`, `YO4X.Strategy.Interpreter`, `YO4X.Mql5.Parser`, `YO4X.SetFiles`, `YO4X.HistoricalData`, `YO4X.Conversion.Api`, `YO4X.CloudRuntime`). In reality, the codebase is partitioned into `src/BuildingBlocks`, `src/Modules/*`, `src/Apps/*`, `src/Application/*`, `src/Infrastructure/*`, and `src/Runtime/*` (where MQL5 runtime is under `YO4X.Mql5.Compilation`, `YO4X.Mql5.Engine`, `YO4X.Mql5.CodeGen`, `YO4X.Mql5.Runtime`), causing confusion and wrong dependency placement.
- **Fix:** Update Section 24 of `docs/USER_SIDE_ARCHITECTURE.md` to reflect the actual repository layout (`BuildingBlocks`, `Modules`, `Apps`, `Infrastructure`, `Runtime`, `Application`).

### [P2] Admin-Side Architecture recommends a 16-project solution and non-existent `YO4X.Admin.Web` portal
- **Where:** `docs/ADMIN_SIDE_ARCHITECTURE.md:1316-1323`
- **Confidence:** CONFIRMED
- **Code:**
  ```markdown
  src/
  ├── YO4X.Admin.Web/                 React/TypeScript admin portal
  ├── YO4X.Admin.Bff/                 Admin session, redaction, read models, typed API
  ├── YO4X.Admin.Application/         Admin use cases and command policies
  ├── YO4X.Admin.Domain/              Approvals, roles, cases, incidents, releases
  ├── YO4X.Admin.Infrastructure/      SSO, audit archive, notification adapters
  ├── YO4X.Authorization/             Permissions, scopes, ABAC and access reviews
  ├── YO4X.Approvals/                 Two-person approval workflow
  ```
- **Failure:** Infrastructure and backend engineers attempting to configure deployments look for `src/YO4X.Admin.Web`, `YO4X.EmergencyControl`, `YO4X.PrivilegedAccess`, `YO4X.SecretMetadata`, `YO4X.SecureSourceReview`, and `YO4X.Operations`. None of these exist as standalone projects; emergency controls reside in `src/Apps/YO4X.EmergencySafety.Api`, and modules are under `src/Modules/*`.
- **Fix:** Update Section 30 of `docs/ADMIN_SIDE_ARCHITECTURE.md` to align with the actual project layout and remove references to phantom projects.

### [P3] Admin API specification defines `/compensation-requests` route whereas implementation maps `/compensations`
- **Where:** `docs/ADMIN_SIDE_ARCHITECTURE.md:894`
- **Confidence:** CONFIRMED
- **Code:**
  ```markdown
  POST /admin/v1/commands/{commandId}/compensation-requests
  ```
- **Failure:** An admin client or automated script issuing a compensation request to `POST /admin/v1/commands/{commandId}/compensation-requests` based on documentation receives an HTTP 404 Not Found response, because `src/Apps/YO4X.Admin.Bff/AdminRoutes.cs:138` maps `POST /admin/v1/commands/{commandId:guid}/compensations`.
- **Fix:** Update `docs/ADMIN_SIDE_ARCHITECTURE.md:894` to `POST /admin/v1/commands/{commandId}/compensations`.

## Referrals
- `src/Frontend/YO4X.Web/scripts/visual-qa.mjs` — test runner assumes obsolete fixture data and selectors (`#deployment-readiness`, `#strategy-compatibility`) from the rejected dashboard concept, which will fail if executed.
- `compose.yaml:5` — pinned PostgreSQL 18.6 alpine image digest should be verified for parity against local Windows test harness pins.

## Coverage gaps
- `docs/frontend/DESIGN_SYSTEM.md`: No automated documentation-to-code token validation test exists to prevent CSS variable drift between `DESIGN_SYSTEM.md` and `tokens.css`.
- `docs/ADMIN_SIDE_ARCHITECTURE.md:874-990`: No route parity test exists between the documented `/admin/v1` routes in section 21 and the endpoints mapped in `src/Apps/YO4X.Admin.Bff/AdminRoutes.cs`.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 277.3s | 438806 tok | id=b4d5dd59-17b6-4d4c-aae3-e2961266335e
