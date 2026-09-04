---
agent_id: I07
lane: injection-sweep
scope:
  - whole tree (cross-cutting sweep: SQL injection, command/process injection, path traversal, unsafe deserialization, regex/ReDoS, XML/LDAP injection)
status: COMPLETE
generated: 2026-08-29T11:44:00Z
counts: { P0: 0, P1: 1, P2: 2, P3: 0 }
---

# I07 — Injection Cross-Cutting Sweep

## Scope audited
- `src/Tools/YO4X.Backtest.Runner/Program.cs` (680 lines)
- `src/Tools/YO4X.Backtest.Runner/StrategySourceResolver.cs` (126 lines)
- `src/Tools/YO4X.LiveBots/Program.cs` (294 lines)
- `src/Tools/YO4X.MarketData.Mt5Import/Mt5TickImportCommand.cs` (350 lines)
- `src/Tools/YO4X.MarketData.Mt5Import/Mt5CommandLine.cs` (140 lines)
- `src/Tools/YO4X.MarketData.Mt5Import/LeanTickZipWriter.cs` (119 lines)
- `src/Tools/YO4X.MarketData.Mt5History/Program.cs` (335 lines)
- `src/Tools/YO4X.Mt5.EndpointDiscovery/Program.cs` (262 lines)
- `src/Apps/YO4X.Conversion.Worker/Mql5QuarantineIntakeJob.cs` (1,272 lines)
- `src/Apps/YO4X.Conversion.Worker/Mql5ArtifactOutputGuard.cs` (296 lines)
- `src/Apps/YO4X.ControlPlane.Api/FrontendProjectionEndpoints.cs` (308 lines)
- `src/Apps/YO4X.ControlPlane.Api/BrokerAccountDiscoveryEndpoints.cs` (54 lines)
- `src/Apps/YO4X.ControlPlane.Api/DevelopmentMt5ConnectionProbe.cs` (475 lines)
- `src/Apps/YO4X.ControlPlane.Api/LocalBrokerCredentialVault.cs` (486 lines)
- `src/Apps/YO4X.ControlPlane.Api/ControlPlaneReadinessProbe.cs` (488 lines)
- `src/Apps/YO4X.Desktop/MainWindow.xaml.cs` (279 lines)
- `src/Apps/YO4X.Desktop/DesktopNavigationPolicy.cs` (31 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs` (2,995 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresBrokerAccountDiscoveryReads.cs` (238 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresUserOperations.cs` (582 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresControlPlaneReads.cs` (440 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresBrokerAccountMutations.cs` (260 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresCredentialMutations.cs` (420 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresDeploymentMutations.cs` (160 lines)
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresStrategyImportMutations.cs` (270 lines)
- `src/Infrastructure/YO4X.Admin.Postgres/AdminReadRepository.cs` (470 lines)
- `src/Infrastructure/YO4X.Admin.Postgres/AdminMutationRepository.cs` (310 lines)
- `src/Infrastructure/YO4X.Admin.Postgres/AdminPolicyRepository.cs` (300 lines)
- `src/Infrastructure/YO4X.LocalSecrets.Windows/DpapiLocalMt5CredentialVault.cs` (1,308 lines)
- `src/Infrastructure/YO4X.LocalSecrets.Windows/Mt5CredentialFileParser.cs` (458 lines)
- `src/Infrastructure/YO4X.LocalSecrets.Windows/LocalSecretPathPolicy.cs` (145 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresDatabase.cs` (185 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/TenantPostgresTransaction.cs` (223 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresMigrationRunner.cs` (152 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Security/least_privilege_roles.sql` (2,000 lines)
- `src/Runtime/YO4X.Trading.ProcessIsolation/BrokerWorkerLaunchManifest.cs` (341 lines)
- `src/Runtime/YO4X.Trading.ProcessIsolation/BrokerProcessClient.cs` (423 lines)
- `src/Runtime/YO4X.Trading.ProcessIsolation/BrokerProcessProtocol.cs` (110 lines)
- `src/Runtime/YO4X.Mql5.Compilation/RoslynMql5CompilationHost.cs` (179 lines)
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5ClrTypes.cs` (1,401 lines)
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5CSharpWriter.cs` (136 lines)
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs` (1,170 lines)
- `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Expressions.cs` (1,822 lines)
- `src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs` (167 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5StaticInventoryAnalyzer.cs` (1,069 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Lexer.cs` (1,235 lines)
- `src/Modules/Support/YO4X.Support/SupportCase.cs` (76 lines)
- `scripts/Import-Mql5Corpus.ps1` (185 lines)
- `scripts/New-BrokerWorkerLaunchManifest.ps1` (72 lines)
- `scripts/build-catalog-sql.mjs` (140 lines)

## Verdict
The database, process boundary, and transpiler layers show disciplined resistance to injection: SQL statements across `NpgsqlCommand` repositories are strictly parameterized, PL/pgSQL dynamic statements use identifier quoting (`%I`), process invocations rely on explicit `ProcessStartInfo.ArgumentList` collections rather than shell strings, Roslyn compiles in memory against an isolated reference set, and deserialization uses non-polymorphic `System.Text.Json`. However, path traversal defenses are inconsistent across background workers and tools: while quarantine and launch manifest verifiers implement robust canonical containment checks, `YO4X.Backtest.Runner` and `YO4X.LiveBots` directly pass uncontained database and CLI path segments into `Path.Combine`, allowing path escape when resolving market data and corpus sources.

## Findings

### [P1] Path traversal in `YO4X.Backtest.Runner` via unvalidated database `request.Symbol`
- **Where:** `src/Tools/YO4X.Backtest.Runner/Program.cs:193`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  string csv = Path.Combine(dataRoot, server, request.Symbol, request.Timeframe + ".csv");
  if (!File.Exists(csv))
  {
      return BacktestOutcome.Refused(
          $"No market data for {request.Symbol} {request.Timeframe} on this machine. "
          + $"Download it first; expected {csv}.");
  }
  ```
- **Failure:** When a user creates a backtest via `POST /v1/backtests`, `PostgresFrontendProjections.cs:1384` validates `request.Symbol` using `RequireBoundedText`, which enforces length and non-empty checks but permits directory traversal sequences (such as `../../other_dir/ticks`). When `YO4X.Backtest.Runner` claims the queued job, `Path.Combine(dataRoot, server, request.Symbol, request.Timeframe + ".csv")` resolves outside `dataRoot`. If the targeted file exists, `Mql5CsvMarketFeed` reads arbitrary CSV files from the filesystem; if absent, the full resolved host path is written into `simulation.backtests.failure_reason`, leaking runner filesystem layout to the frontend.
- **Fix:** Validate that `request.Symbol` matches a strict alphanumeric/symbol pattern, and assert that `Path.GetFullPath(csv)` starts with `Path.GetFullPath(dataRoot) + Path.DirectorySeparatorChar`.

### [P2] Missing canonical containment check on `corpusRoot` and `dataRoot` in `YO4X.LiveBots`
- **Where:** `src/Tools/YO4X.LiveBots/Program.cs:87` and `src/Tools/YO4X.LiveBots/Program.cs:110`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  string csv = Path.Combine(dataRoot, server, symbol, timeframe + ".csv");
  if (!File.Exists(csv))
  ...
  Selection run = chosen[0];
  string sourcePath = Path.Combine(corpusRoot, run.Name);
  if (!File.Exists(sourcePath))
  ```
- **Failure:** `YO4X.LiveBots` accepts `--symbol` and `--server` arguments and reads `run.Name` from `strategy.name` in `catalog.strategies`, then joins them via `Path.Combine` without canonical path containment checks. A database record or option with directory traversal components (`..`) resolves to paths outside the intended `dataRoot` and `corpusRoot` directories.
- **Fix:** Sanitize `symbol`, `server`, and `run.Name`, and verify that `Path.GetFullPath(sourcePath)` and `Path.GetFullPath(csv)` are strictly prefixed by `corpusRoot` and `dataRoot` directory boundaries.

### [P2] Missing directory containment verification on manifest relative paths in `StrategySourceResolver`
- **Where:** `src/Tools/YO4X.Backtest.Runner/StrategySourceResolver.cs:81`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  file = found;
  string path = Path.Combine(corpusRoot, found.RelativePath);
  if (!File.Exists(path))
  {
      refusal = "The corpus file named by the manifest is not on disk: " + found.RelativePath;
      return false;
  }
  ```
- **Failure:** Unlike `StrategyInputProjectionCommand.cs:89-90` (which verifies `path.StartsWith(options.SourceRoot)`), `StrategySourceResolver.TryRead` joins `found.RelativePath` directly to `corpusRoot`. If a manifest file contains relative paths with traversal tokens (`../../file.mq5`), the resolver accesses files outside `corpusRoot`.
- **Fix:** Verify that `Path.GetFullPath(path).StartsWith(Path.GetFullPath(corpusRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)` before reading or verifying file contents.

## Referrals
None.

## Coverage gaps
- `src/Tools/YO4X.Backtest.Runner/Program.cs:193-199`: No integration test exercises `ClaimAsync` with a `request.Symbol` containing path traversal characters (`..\\` or `../`) to assert fail-closed rejection before file reading.
- `src/Tools/YO4X.LiveBots/Program.cs:110-115`: Untested path where `catalog.strategies.name` contains directory separators and attempts to resolve outside `corpusRoot`.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 321.7s | 604166 tok | id=ea54d02b-f92c-4a73-8539-d390d57f012f
