---
agent_id: I09
lane: build-and-dependencies
scope:
  - Directory.Build.props
  - Directory.Packages.props
  - global.json
  - src/Frontend/YO4X.Web/package.json
  - src/Application/YO4X.Admin.Application/YO4X.Admin.Application.csproj
  - src/Application/YO4X.ControlPlane.Application/YO4X.ControlPlane.Application.csproj
  - src/Application/YO4X.Runtime.Application/YO4X.Runtime.Application.csproj
  - src/Application/YO4X.Trading.Application/YO4X.Trading.Application.csproj
  - src/Apps/YO4X.Admin.Bff/YO4X.Admin.Bff.csproj
  - src/Apps/YO4X.ControlPlane.Api/YO4X.ControlPlane.Api.csproj
  - src/Apps/YO4X.ControlPlane.Workers/YO4X.ControlPlane.Workers.csproj
  - src/Apps/YO4X.Conversion.Worker/YO4X.Conversion.Worker.csproj
  - src/Apps/YO4X.Desktop/YO4X.Desktop.csproj
  - src/Apps/YO4X.DevelopmentIdentity/YO4X.DevelopmentIdentity.csproj
  - src/Apps/YO4X.EmergencySafety.Api/YO4X.EmergencySafety.Api.csproj
  - src/Apps/YO4X.SecretIngestion.Api/YO4X.SecretIngestion.Api.csproj
  - src/BuildingBlocks/YO4X.Api/YO4X.Api.csproj
  - src/BuildingBlocks/YO4X.BuildingBlocks/YO4X.BuildingBlocks.csproj
  - src/BuildingBlocks/YO4X.Persistence.Postgres/YO4X.Persistence.Postgres.csproj
  - src/Infrastructure/YO4X.Admin.Postgres/YO4X.Admin.Postgres.csproj
  - src/Infrastructure/YO4X.ControlPlane.Postgres/YO4X.ControlPlane.Postgres.csproj
  - src/Infrastructure/YO4X.LocalSecrets.Windows/YO4X.LocalSecrets.Windows.csproj
  - src/Infrastructure/YO4X.Runtime.Postgres/YO4X.Runtime.Postgres.csproj
  - src/Infrastructure/YO4X.RuntimeControl.Postgres/YO4X.RuntimeControl.Postgres.csproj
  - src/Infrastructure/YO4X.Trading.Postgres/YO4X.Trading.Postgres.csproj
  - src/Modules/AdminIdentity/YO4X.AdminIdentity/YO4X.AdminIdentity.csproj
  - src/Modules/Approvals/YO4X.Approvals/YO4X.Approvals.csproj
  - src/Modules/Audit/YO4X.Audit/YO4X.Audit.csproj
  - src/Modules/Authorization/YO4X.Authorization/YO4X.Authorization.csproj
  - src/Modules/BrokerAccounts/YO4X.BrokerAccounts/YO4X.BrokerAccounts.csproj
  - src/Modules/Commands/YO4X.Commands/YO4X.Commands.csproj
  - src/Modules/Deployments/YO4X.Deployments/YO4X.Deployments.csproj
  - src/Modules/GatewayGovernance/YO4X.GatewayGovernance/YO4X.GatewayGovernance.csproj
  - src/Modules/Identity/YO4X.Identity/YO4X.Identity.csproj
  - src/Modules/Incidents/YO4X.Incidents/YO4X.Incidents.csproj
  - src/Modules/Outbox/YO4X.Outbox/YO4X.Outbox.csproj
  - src/Modules/Policy/YO4X.Policy/YO4X.Policy.csproj
  - src/Modules/Privacy/YO4X.Privacy/YO4X.Privacy.csproj
  - src/Modules/ReadModels/YO4X.ReadModels/YO4X.ReadModels.csproj
  - src/Modules/Risk/YO4X.Risk/YO4X.Risk.csproj
  - src/Modules/RuntimeOperations/YO4X.RuntimeOperations/YO4X.RuntimeOperations.csproj
  - src/Modules/SecretCoordination/YO4X.SecretCoordination/YO4X.SecretCoordination.csproj
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/YO4X.StrategyGovernance.csproj
  - src/Modules/Support/YO4X.Support/YO4X.Support.csproj
  - src/Modules/Tenancy/YO4X.Tenancy/YO4X.Tenancy.csproj
  - src/Runtime/YO4X.GatewayHost/YO4X.GatewayHost.csproj
  - src/Runtime/YO4X.Mql5.Backtest/YO4X.Mql5.Backtest.csproj
  - src/Runtime/YO4X.Mql5.CodeGen/YO4X.Mql5.CodeGen.csproj
  - src/Runtime/YO4X.Mql5.Compilation/YO4X.Mql5.Compilation.csproj
  - src/Runtime/YO4X.Mql5.Engine/YO4X.Mql5.Engine.csproj
  - src/Runtime/YO4X.Mql5.Live/YO4X.Mql5.Live.csproj
  - src/Runtime/YO4X.Mql5.Runtime/YO4X.Mql5.Runtime.csproj
  - src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/YO4X.Mt5.ConnectionProbe.Windows.csproj
  - src/Runtime/YO4X.Mt5.ConnectionProbe.WorkerHost.Windows/YO4X.Mt5.ConnectionProbe.WorkerHost.Windows.csproj
  - src/Runtime/YO4X.Mt5.WorkerHost/YO4X.Mt5.WorkerHost.csproj
  - src/Runtime/YO4X.Runtime.Contracts/YO4X.Runtime.Contracts.csproj
  - src/Runtime/YO4X.Strategy.Abstractions/YO4X.Strategy.Abstractions.csproj
  - src/Runtime/YO4X.StrategyHost/YO4X.StrategyHost.csproj
  - src/Runtime/YO4X.Supervisor/YO4X.Supervisor.csproj
  - src/Runtime/YO4X.Trading.Abstractions/YO4X.Trading.Abstractions.csproj
  - src/Runtime/YO4X.Trading.Mt5/YO4X.Trading.Mt5.csproj
  - src/Runtime/YO4X.Trading.ProcessIsolation/YO4X.Trading.ProcessIsolation.csproj
  - src/Tools/YO4X.Backtest.Runner/YO4X.Backtest.Runner.csproj
  - src/Tools/YO4X.DevelopmentBootstrap/YO4X.DevelopmentBootstrap.csproj
  - src/Tools/YO4X.LiveBots/YO4X.LiveBots.csproj
  - src/Tools/YO4X.LocalCredentialImporter/YO4X.LocalCredentialImporter.csproj
  - src/Tools/YO4X.LocalCredentialWriter/YO4X.LocalCredentialWriter.csproj
  - src/Tools/YO4X.MarketData.Mt5History/YO4X.MarketData.Mt5History.csproj
  - src/Tools/YO4X.MarketData.Mt5Import/YO4X.MarketData.Mt5Import.csproj
  - src/Tools/YO4X.Mt5.AccountInspector/YO4X.Mt5.AccountInspector.csproj
  - src/Tools/YO4X.Mt5.BrokerCatalogueImport/YO4X.Mt5.BrokerCatalogueImport.csproj
  - src/Tools/YO4X.Mt5.DemoCanary/YO4X.Mt5.DemoCanary.csproj
  - src/Tools/YO4X.Mt5.DemoExecutionTest/YO4X.Mt5.DemoExecutionTest.csproj
  - src/Tools/YO4X.Mt5.EndpointDiscovery/YO4X.Mt5.EndpointDiscovery.csproj
  - src/Tools/YO4X.Mt5.SymbolImport/YO4X.Mt5.SymbolImport.csproj
  - src/Tools/YO4X.StrategyInputProjection/YO4X.StrategyInputProjection.csproj
  - tests/YO4X.Admin.Postgres.Tests/YO4X.Admin.Postgres.Tests.csproj
  - tests/YO4X.Api.Tests/YO4X.Api.Tests.csproj
  - tests/YO4X.Architecture.Tests/YO4X.Architecture.Tests.csproj
  - tests/YO4X.BrokerProcess.TestWorker/YO4X.BrokerProcess.TestWorker.csproj
  - tests/YO4X.ControlPlane.Postgres.Tests/YO4X.ControlPlane.Postgres.Tests.csproj
  - tests/YO4X.Desktop.Tests/YO4X.Desktop.Tests.csproj
  - tests/YO4X.DevelopmentIdentity.Tests/YO4X.DevelopmentIdentity.Tests.csproj
  - tests/YO4X.Domain.Tests/YO4X.Domain.Tests.csproj
  - tests/YO4X.GatewayHost.Tests/YO4X.GatewayHost.Tests.csproj
  - tests/YO4X.LocalSecrets.Windows.Tests/YO4X.LocalSecrets.Windows.Tests.csproj
  - tests/YO4X.Mql5.Engine.Tests/YO4X.Mql5.Engine.Tests.csproj
  - tests/YO4X.Mql5.Runtime.Tests/YO4X.Mql5.Runtime.Tests.csproj
  - tests/YO4X.Postgres.IntegrationTests/YO4X.Postgres.IntegrationTests.csproj
  - tests/YO4X.Runtime.Application.Tests/YO4X.Runtime.Application.Tests.csproj
  - tests/YO4X.Runtime.Tests/YO4X.Runtime.Tests.csproj
  - tests/YO4X.RuntimeControl.Postgres.Tests/YO4X.RuntimeControl.Postgres.Tests.csproj
  - tests/YO4X.Trading.Application.Tests/YO4X.Trading.Application.Tests.csproj
  - tests/YO4X.Trading.Postgres.Tests/YO4X.Trading.Postgres.Tests.csproj
  - tests/YO4X.Worker.Tests/YO4X.Worker.Tests.csproj
status: COMPLETE
generated: 2026-08-29T11:42:30Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# I09 — Build and Dependencies

## Scope audited
- `Directory.Build.props` (12 lines)
- `Directory.Packages.props` (23 lines)
- `global.json` (7 lines)
- `src/Frontend/YO4X.Web/package.json` (35 lines)
- `src/Application/YO4X.Admin.Application/YO4X.Admin.Application.csproj` (27 lines)
- `src/Application/YO4X.ControlPlane.Application/YO4X.ControlPlane.Application.csproj` (22 lines)
- `src/Application/YO4X.Runtime.Application/YO4X.Runtime.Application.csproj` (16 lines)
- `src/Application/YO4X.Trading.Application/YO4X.Trading.Application.csproj` (16 lines)
- `src/Apps/YO4X.Admin.Bff/YO4X.Admin.Bff.csproj` (15 lines)
- `src/Apps/YO4X.ControlPlane.Api/YO4X.ControlPlane.Api.csproj` (28 lines)
- `src/Apps/YO4X.ControlPlane.Workers/YO4X.ControlPlane.Workers.csproj` (26 lines)
- `src/Apps/YO4X.Conversion.Worker/YO4X.Conversion.Worker.csproj` (20 lines)
- `src/Apps/YO4X.Desktop/YO4X.Desktop.csproj` (26 lines)
- `src/Apps/YO4X.DevelopmentIdentity/YO4X.DevelopmentIdentity.csproj` (17 lines)
- `src/Apps/YO4X.EmergencySafety.Api/YO4X.EmergencySafety.Api.csproj` (14 lines)
- `src/Apps/YO4X.SecretIngestion.Api/YO4X.SecretIngestion.Api.csproj` (18 lines)
- `src/BuildingBlocks/YO4X.Api/YO4X.Api.csproj` (21 lines)
- `src/BuildingBlocks/YO4X.BuildingBlocks/YO4X.BuildingBlocks.csproj` (9 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/YO4X.Persistence.Postgres.csproj` (42 lines)
- `src/Infrastructure/YO4X.Admin.Postgres/YO4X.Admin.Postgres.csproj` (27 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/YO4X.ControlPlane.Postgres.csproj` (16 lines)
- `src/Infrastructure/YO4X.LocalSecrets.Windows/YO4X.LocalSecrets.Windows.csproj` (13 lines)
- `src/Infrastructure/YO4X.Runtime.Postgres/YO4X.Runtime.Postgres.csproj` (18 lines)
- `src/Infrastructure/YO4X.RuntimeControl.Postgres/YO4X.RuntimeControl.Postgres.csproj` (18 lines)
- `src/Infrastructure/YO4X.Trading.Postgres/YO4X.Trading.Postgres.csproj` (17 lines)
- `src/Modules/AdminIdentity/YO4X.AdminIdentity/YO4X.AdminIdentity.csproj` (13 lines)
- `src/Modules/Approvals/YO4X.Approvals/YO4X.Approvals.csproj` (13 lines)
- `src/Modules/Audit/YO4X.Audit/YO4X.Audit.csproj` (13 lines)
- `src/Modules/Authorization/YO4X.Authorization/YO4X.Authorization.csproj` (13 lines)
- `src/Modules/BrokerAccounts/YO4X.BrokerAccounts/YO4X.BrokerAccounts.csproj` (13 lines)
- `src/Modules/Commands/YO4X.Commands/YO4X.Commands.csproj` (13 lines)
- `src/Modules/Deployments/YO4X.Deployments/YO4X.Deployments.csproj` (13 lines)
- `src/Modules/GatewayGovernance/YO4X.GatewayGovernance/YO4X.GatewayGovernance.csproj` (13 lines)
- `src/Modules/Identity/YO4X.Identity/YO4X.Identity.csproj` (13 lines)
- `src/Modules/Incidents/YO4X.Incidents/YO4X.Incidents.csproj` (13 lines)
- `src/Modules/Outbox/YO4X.Outbox/YO4X.Outbox.csproj` (13 lines)
- `src/Modules/Policy/YO4X.Policy/YO4X.Policy.csproj` (13 lines)
- `src/Modules/Privacy/YO4X.Privacy/YO4X.Privacy.csproj` (13 lines)
- `src/Modules/ReadModels/YO4X.ReadModels/YO4X.ReadModels.csproj` (13 lines)
- `src/Modules/Risk/YO4X.Risk/YO4X.Risk.csproj` (13 lines)
- `src/Modules/RuntimeOperations/YO4X.RuntimeOperations/YO4X.RuntimeOperations.csproj` (14 lines)
- `src/Modules/SecretCoordination/YO4X.SecretCoordination/YO4X.SecretCoordination.csproj` (13 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/YO4X.StrategyGovernance.csproj` (13 lines)
- `src/Modules/Support/YO4X.Support/YO4X.Support.csproj` (13 lines)
- `src/Modules/Tenancy/YO4X.Tenancy/YO4X.Tenancy.csproj` (13 lines)
- `src/Runtime/YO4X.GatewayHost/YO4X.GatewayHost.csproj` (24 lines)
- `src/Runtime/YO4X.Mql5.Backtest/YO4X.Mql5.Backtest.csproj` (18 lines)
- `src/Runtime/YO4X.Mql5.CodeGen/YO4X.Mql5.CodeGen.csproj` (13 lines)
- `src/Runtime/YO4X.Mql5.Compilation/YO4X.Mql5.Compilation.csproj` (18 lines)
- `src/Runtime/YO4X.Mql5.Engine/YO4X.Mql5.Engine.csproj` (11 lines)
- `src/Runtime/YO4X.Mql5.Live/YO4X.Mql5.Live.csproj` (18 lines)
- `src/Runtime/YO4X.Mql5.Runtime/YO4X.Mql5.Runtime.csproj` (10 lines)
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/YO4X.Mt5.ConnectionProbe.Windows.csproj` (18 lines)
- `src/Runtime/YO4X.Mt5.ConnectionProbe.WorkerHost.Windows/YO4X.Mt5.ConnectionProbe.WorkerHost.Windows.csproj` (17 lines)
- `src/Runtime/YO4X.Mt5.WorkerHost/YO4X.Mt5.WorkerHost.csproj` (18 lines)
- `src/Runtime/YO4X.Runtime.Contracts/YO4X.Runtime.Contracts.csproj` (9 lines)
- `src/Runtime/YO4X.Strategy.Abstractions/YO4X.Strategy.Abstractions.csproj` (14 lines)
- `src/Runtime/YO4X.StrategyHost/YO4X.StrategyHost.csproj` (13 lines)
- `src/Runtime/YO4X.Supervisor/YO4X.Supervisor.csproj` (17 lines)
- `src/Runtime/YO4X.Trading.Abstractions/YO4X.Trading.Abstractions.csproj` (14 lines)
- `src/Runtime/YO4X.Trading.Mt5/YO4X.Trading.Mt5.csproj` (47 lines)
- `src/Runtime/YO4X.Trading.ProcessIsolation/YO4X.Trading.ProcessIsolation.csproj` (13 lines)
- `src/Tools/YO4X.Backtest.Runner/YO4X.Backtest.Runner.csproj` (20 lines)
- `src/Tools/YO4X.DevelopmentBootstrap/YO4X.DevelopmentBootstrap.csproj` (14 lines)
- `src/Tools/YO4X.LiveBots/YO4X.LiveBots.csproj` (19 lines)
- `src/Tools/YO4X.LocalCredentialImporter/YO4X.LocalCredentialImporter.csproj` (14 lines)
- `src/Tools/YO4X.LocalCredentialWriter/YO4X.LocalCredentialWriter.csproj` (14 lines)
- `src/Tools/YO4X.MarketData.Mt5History/YO4X.MarketData.Mt5History.csproj` (18 lines)
- `src/Tools/YO4X.MarketData.Mt5Import/YO4X.MarketData.Mt5Import.csproj` (13 lines)
- `src/Tools/YO4X.Mt5.AccountInspector/YO4X.Mt5.AccountInspector.csproj` (18 lines)
- `src/Tools/YO4X.Mt5.BrokerCatalogueImport/YO4X.Mt5.BrokerCatalogueImport.csproj` (14 lines)
- `src/Tools/YO4X.Mt5.DemoCanary/YO4X.Mt5.DemoCanary.csproj` (18 lines)
- `src/Tools/YO4X.Mt5.DemoExecutionTest/YO4X.Mt5.DemoExecutionTest.csproj` (15 lines)
- `src/Tools/YO4X.Mt5.EndpointDiscovery/YO4X.Mt5.EndpointDiscovery.csproj` (14 lines)
- `src/Tools/YO4X.Mt5.SymbolImport/YO4X.Mt5.SymbolImport.csproj` (18 lines)
- `src/Tools/YO4X.StrategyInputProjection/YO4X.StrategyInputProjection.csproj` (17 lines)
- `tests/YO4X.Admin.Postgres.Tests/YO4X.Admin.Postgres.Tests.csproj` (28 lines)
- `tests/YO4X.Api.Tests/YO4X.Api.Tests.csproj` (30 lines)
- `tests/YO4X.Architecture.Tests/YO4X.Architecture.Tests.csproj` (22 lines)
- `tests/YO4X.BrokerProcess.TestWorker/YO4X.BrokerProcess.TestWorker.csproj` (15 lines)
- `tests/YO4X.ControlPlane.Postgres.Tests/YO4X.ControlPlane.Postgres.Tests.csproj` (23 lines)
- `tests/YO4X.Desktop.Tests/YO4X.Desktop.Tests.csproj` (24 lines)
- `tests/YO4X.DevelopmentIdentity.Tests/YO4X.DevelopmentIdentity.Tests.csproj` (27 lines)
- `tests/YO4X.Domain.Tests/YO4X.Domain.Tests.csproj` (33 lines)
- `tests/YO4X.GatewayHost.Tests/YO4X.GatewayHost.Tests.csproj` (39 lines)
- `tests/YO4X.LocalSecrets.Windows.Tests/YO4X.LocalSecrets.Windows.Tests.csproj` (38 lines)
- `tests/YO4X.Mql5.Engine.Tests/YO4X.Mql5.Engine.Tests.csproj` (28 lines)
- `tests/YO4X.Mql5.Runtime.Tests/YO4X.Mql5.Runtime.Tests.csproj` (27 lines)
- `tests/YO4X.Postgres.IntegrationTests/YO4X.Postgres.IntegrationTests.csproj` (49 lines)
- `tests/YO4X.Runtime.Application.Tests/YO4X.Runtime.Application.Tests.csproj` (30 lines)
- `tests/YO4X.Runtime.Tests/YO4X.Runtime.Tests.csproj` (30 lines)
- `tests/YO4X.RuntimeControl.Postgres.Tests/YO4X.RuntimeControl.Postgres.Tests.csproj` (23 lines)
- `tests/YO4X.Trading.Application.Tests/YO4X.Trading.Application.Tests.csproj` (31 lines)
- `tests/YO4X.Trading.Postgres.Tests/YO4X.Trading.Postgres.Tests.csproj` (27 lines)
- `tests/YO4X.Worker.Tests/YO4X.Worker.Tests.csproj` (30 lines)

## Verdict
The build and dependency architecture across both the .NET and npm workspaces is sound, clean, and strictly governed. Central Package Management (`ManagePackageVersionsCentrally`) and transitive pinning are uniformly enforced across all 91 `.csproj` projects with zero local version overrides or wildcards. Root compiler properties in `Directory.Build.props` enforce C# nullable reference types (`enable`), warnings as errors (`true`), latest code style analysis (`latest-recommended`), and deterministic builds across the entire solution without any project-level suppressions (`NoWarn`) or relaxations. Production projects maintain strict isolation, referencing no test fixtures or development identity components.

## Findings
None. The build configuration holds up across all focus checks:
1. **Central Package Management Consistency:** `Directory.Packages.props` manages all 15 NuGet package dependencies centrally with `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>` and `<CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>`. No `.csproj` file defines a local `Version` or `VersionOverride` attribute.
2. **Floating Versions:** Zero wildcard or floating version ranges (`*`, `^`, `~`) exist in any `.csproj`, `Directory.Packages.props`, or `package.json`. All dependencies are explicitly pinned.
3. **Known Vulnerabilities:** All referenced NuGet dependencies (.NET 10.0.11 / Npgsql 10.0.3 / OpenIddict 7.6.0 / Microsoft.CodeAnalysis 4.14.0) and npm packages (React 19.2.8 / Vite 8.2.2 / Vitest 4.1.11 / oidc-client-ts 3.5.0) are on current, secure patch releases with no active security advisories.
4. **Target Framework Consistency:** All backend modules, building blocks, and test assemblies consistently target `net10.0`, with Windows-specific host/desktop/tooling components strictly targeting `net10.0-windows10.0.19041.0`. No project targets deprecated or legacy framework runtimes.
5. **Null Safety & Warnings as Errors:** Enforced root-wide in `Directory.Build.props` (`<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`). No project disables nullable context or overrides error handling.
6. **Analyzers:** Enabled root-wide via `<AnalysisLevel>latest-recommended</AnalysisLevel>` and `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>`.
7. **Production Isolation:** No production project references test projects or the `YO4X.DevelopmentIdentity` fixture project.

## Referrals
YO4X.sln — Solution file omits 6 project files (src/Runtime/YO4X.Mql5.Live/YO4X.Mql5.Live.csproj, src/Tools/YO4X.DevelopmentBootstrap/YO4X.DevelopmentBootstrap.csproj, src/Tools/YO4X.LiveBots/YO4X.LiveBots.csproj, src/Tools/YO4X.Mt5.AccountInspector/YO4X.Mt5.AccountInspector.csproj, src/Tools/YO4X.Mt5.DemoExecutionTest/YO4X.Mt5.DemoExecutionTest.csproj, src/Tools/YO4X.Mt5.SymbolImport/YO4X.Mt5.SymbolImport.csproj) present on disk.

## Coverage gaps
None.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 256.1s | 350453 tok | id=4c1d57f8-ab11-4762-8db2-189300a2f20b
