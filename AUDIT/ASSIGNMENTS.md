# YO4X Fleet Audit — Lane Register

156 lanes, each owned by exactly one agent. Scopes are disjoint: a file belongs to one
lane, so two agents never file the same finding. Cross-cutting lanes (`I*`) sweep one
*property* across the tree and file only against that property.

Report path: `AUDIT/findings/<ID>-<slug>.md`. Rules: `AUDIT/CHARTER.md`.

## A — Frontend (React / TypeScript)

| ID | Lane | Scope | Focus |
|---|---|---|---|
| `A01` | api-client | `src/Frontend/YO4X.Web/src/api/controlPlaneClient.ts` | auth headers, retry, abort, error mapping, base URL |
| `A02` | api-contracts | `src/Frontend/YO4X.Web/src/api/contracts.ts` | runtime validation vs declared types, optional/null drift |
| `A03` | api-url-problem | `.../api/safeUrl.ts`, `.../api/problemDetails.ts` | SSRF, open redirect, RFC7807 parsing |
| `A04` | routing | `.../src/app/App.tsx`, `.../src/app/navigation.ts` | route table, deep links, unknown route, guard order |
| `A05` | app-shell | `.../src/app/shell/*.tsx` | nav state, active-route logic, keyboard, a11y |
| `A06` | data-fetching | `.../src/app/ClientContext.tsx`, `.../src/app/useResource.ts` | stale closures, races, cleanup, refetch storms |
| `A07` | runtime-config | `.../src/app/config/runtimeConfig.ts` | env parsing, prod/dev leakage, missing-var behaviour |
| `A08` | auth-frontend | `.../src/auth/AuthEntry.tsx`, `.../src/auth/developmentOidc.ts` | token storage, PKCE, dev bypass reachable in prod |
| `A09` | dashboard | `.../features/dashboard/DashboardPage.tsx` | data derivation, empty/error states, number formatting |
| `A10` | backtests-ui | `.../features/backtests/BacktestsPage.tsx`, `BacktestDetail.tsx`, `NewBacktestModal.tsx` | list/detail state, polling, equity curve rendering |
| `A11` | backtest-form | `.../features/backtests/backtestForm.ts` | validation logic, date ranges, numeric coercion |
| `A12` | bots-ui | `.../features/bots/BotsPage.tsx`, `BotSettingsModal.tsx`, `botSettingsForm.ts` | risk inputs, lot size entry, save/confirm path |
| `A13` | broker-hooks | `.../features/broker-accounts/hooks/*.ts` | polling loops, unmount cleanup, leaks, probe retry |
| `A14` | broker-registration | `.../features/broker-accounts/brokerRegistration.ts`, `model.ts` | credential handling in browser, validation |
| `A15` | strategies-ui | `.../features/strategies/CatalogPage.tsx`, `DetailPage.tsx`, `StrategyCard.tsx` | list state, detail fetch, XSS in rendered source |
| `A16` | compiler-ui | `.../features/compiler/CompilerPage.tsx` | compile status, log rendering, XSS from compiler output |
| `A17` | journal-cloud-settings | `.../features/journal/JournalPage.tsx`, `.../features/cloud/CloudPage.tsx`, `.../features/settings/SettingsPage.tsx` | state, formatting, destructive actions |
| `A18` | overlays | `.../features/overlays/*.tsx` | focus trap, escape, submit-twice, wizard state machine |
| `A19` | shared-ui | `.../src/shared/ui/*.tsx` | a11y, focus, Modal/Drawer portals, Toggle semantics |
| `A20` | styling | `.../src/app/styles/*.css`, all `features/**/*.css` | tokens, dark/light, responsive, contrast |
| `A21` | fe-build | `.../vite.config.ts`, `package.json`, `index.html`, `.env.example` | CSP, sourcemaps in prod, dep CVEs, env exposure |
| `A22` | fe-tests | `.../src/**/*.test.ts`, `.../src/**/*.test.tsx`, `.../src/tests/setup.ts` | assertion strength, false-green tests |
| `A23` | fe-qa-scripts | `.../scripts/*.mjs` | QA harness correctness, stub-api drift from real API |

## B — Backend hosts, endpoints, routing

| ID | Lane | Scope | Focus |
|---|---|---|---|
| `B01` | cp-api-endpoints | `src/Apps/YO4X.ControlPlane.Api/FrontendProjectionEndpoints.cs`, `BrokerAccountDiscoveryEndpoints.cs`, `BrokerAccountRegistrationBody.cs` | route shape, model binding, status codes, authz attributes |
| `B02` | cp-api-host | `src/Apps/YO4X.ControlPlane.Api/Program.cs`, `ControlPlanePostgresRegistration.cs`, `RuntimeControlPostgresRegistration.cs`, `TenantContextCapabilityRegistration.cs`, `ControlPlaneReadinessProbe.cs` | DI lifetimes, middleware order, CORS, readiness |
| `B03` | cp-api-credentials | `src/Apps/YO4X.ControlPlane.Api/LocalBrokerCredentialVault.cs`, `DevelopmentMt5ConnectionProbe.cs`, `WorkloadActorClaims.cs` | credential path, claim trust, dev probe in prod |
| `B04` | admin-bff | `src/Apps/YO4X.Admin.Bff/**` | BFF auth, token relay, endpoint exposure |
| `B05` | secret-ingestion-api | `src/Apps/YO4X.SecretIngestion.Api/**` | proof validation, replay, rate limit, secret at rest |
| `B06` | emergency-safety-api | `src/Apps/YO4X.EmergencySafety.Api/**` | kill-switch authz, idempotency, failure mode |
| `B07` | dev-identity | `src/Apps/YO4X.DevelopmentIdentity/**` | dev-only guard, token signing, prod reachability |
| `B08` | api-buildingblock | `src/BuildingBlocks/YO4X.Api/**` | shared middleware, problem details, validation filters |
| `B09` | worker-operations-store | `src/Apps/YO4X.ControlPlane.Workers/Operations/PostgresUserOperationWorkStore.cs` | claim/lease/retry, at-least-once, cursor loss (3830 lines) |
| `B10` | worker-operations-rest | `src/Apps/YO4X.ControlPlane.Workers/Operations/` (all except `PostgresUserOperationWorkStore.cs`) | dispatch envelope, tenant scan, policy trust, readiness |
| `B11` | worker-outbox | `src/Apps/YO4X.ControlPlane.Workers/Outbox/**` | exactly-once, retry schedule, poison messages, ordering |
| `B12` | worker-host | `src/Apps/YO4X.ControlPlane.Workers/Program.cs` and `Worker*.cs` at project root | fail-stop, health, shutdown, boundary |
| `B13` | conversion-quarantine | `src/Apps/YO4X.Conversion.Worker/Mql5QuarantineIntakeJob.cs` | intake state machine, poison input, partial progress |
| `B14` | conversion-corpus-store | `src/Apps/YO4X.Conversion.Worker/` (all except `Mql5QuarantineIntakeJob.cs`) | corpus store, dedup, hashing, transactions |
| `B15` | desktop-app | `src/Apps/YO4X.Desktop/**` | WebView host, IPC surface, local file access |
| `B16` | contract-drift | `src/Frontend/YO4X.Web/src/api/contracts.ts` **vs** `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs` and `src/Apps/YO4X.ControlPlane.Api/FrontendProjectionEndpoints.cs` | field-by-field: name, nullability, type, enum values, casing |

## C — Application layer

| ID | Lane | Scope | Focus |
|---|---|---|---|
| `C01` | admin-application | `src/Application/YO4X.Admin.Application/**` | use-case orchestration, authz checks, validation |
| `C02` | controlplane-application | `src/Application/YO4X.ControlPlane.Application/**` | command handling, invariants, error paths |
| `C03` | runtime-application-evidence | `src/Application/YO4X.Runtime.Application/StrategyEventEvidence.cs` | evidence chain, hashing, tamper detection |
| `C04` | runtime-application-rest | `src/Application/YO4X.Runtime.Application/` (all except `StrategyEventEvidence.cs`) | runtime state transitions |
| `C05` | trading-application | `src/Application/YO4X.Trading.Application/**` | command coordinator, order lifecycle, risk gates |
| `C06` | buildingblocks-core | `src/BuildingBlocks/YO4X.BuildingBlocks/**` | primitives, result types, guard clauses |

## D — Persistence / PostgreSQL

| ID | Lane | Scope | Focus |
|---|---|---|---|
| `D01` | migrations-early | `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/001_*.sql` through `003_*.sql` | schema, constraints, FK, nullability, index gaps |
| `D02` | migrations-mid | `.../Migrations/004_*.sql` through `007_*.sql` | same, plus catalogue and projection tables |
| `D03` | migrations-late | `.../Migrations/008_*.sql` through `010_*.sql` | queue tables, equity curve precision, bot settings |
| `D04` | db-roles | `src/BuildingBlocks/YO4X.Persistence.Postgres/Security/least_privilege_roles.sql` | grant scope, role escalation, default privileges |
| `D05` | persistence-core | `src/BuildingBlocks/YO4X.Persistence.Postgres/*.cs` (except `PostgresRoleCapabilityFingerprint.cs`) | fail-closed connection policy, pooling, retry |
| `D06` | role-fingerprint | `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRoleCapabilityFingerprint.cs` | fingerprint correctness, false accept |
| `D07` | frontend-projections | `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs` | query correctness, N+1, tenant filter on every read |
| `D08` | cp-postgres-mutations | `src/Infrastructure/YO4X.ControlPlane.Postgres/Postgres*Mutations.cs`, `PostgresMutationSupport.cs` | transaction scope, partial writes, concurrency |
| `D09` | cp-postgres-reads-proof | `src/Infrastructure/YO4X.ControlPlane.Postgres/` remaining files (reads, proof issuers, key rings, policy trust, options, validation, evaluation, application, user operations) | proof key rotation, read authz |
| `D10` | trading-postgres | `src/Infrastructure/YO4X.Trading.Postgres/**` | broker command store, idempotency keys, status machine |
| `D11` | runtime-postgres | `src/Infrastructure/YO4X.Runtime.Postgres/**` | runtime state persistence, event ordering |
| `D12` | runtimecontrol-postgres | `src/Infrastructure/YO4X.RuntimeControl.Postgres/**` | control commands, locking |
| `D13` | admin-postgres | `src/Infrastructure/YO4X.Admin.Postgres/**` | admin reads/writes, privilege boundary |

## E — Domain modules

| ID | Lane | Scope | Focus |
|---|---|---|---|
| `E01` | mql5-lexer | `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Lexer.cs` | tokenisation: numerics, strings, escapes, comments, preprocessor |
| `E02` | mql5-parser | `.../Mql5Parser.cs` | precedence, associativity, ternary, casts, declarations (3064 lines) |
| `E03` | mql5-binder | `.../Mql5Binder.cs` | overload resolution, implicit conversion, scoping (2694 lines) |
| `E04` | mql5-lowering-ir | `.../Mql5Lowering.cs`, `Mql5IrV2.cs` | lowering fidelity, loop and branch shape, temporaries |
| `E05` | mql5-builtins | `.../Mql5BuiltinConstants.cs`, `Mql5BuiltinSignatures.cs`, `Mql5BuiltinCatalog.cs`, `Mql5BuiltinRealConstants.cs`, `Mql5PredefinedVariables.cs` | constant values vs MQL5 spec, signature arity and types |
| `E06` | mql5-compile-orchestrator | `.../Mql5IsolatedCompileOrchestrator.cs` | isolation, timeout, cleanup, error surfacing |
| `E07` | mql5-evidence | `.../Mql5ConversionEvidence.cs`, `Mql5ConversionEvidenceAnalyzer.cs`, `Mql5ConversionEvidenceFormatter.cs` | evidence completeness, false attestation |
| `E08` | mql5-inventory-dossier | `.../Mql5StaticInventory.cs`, `Mql5StaticInventoryAnalyzer.cs`, `Mql5CompilePackageDossierPlanner.cs`, `Mql5CompilePackagePlanFormatter.cs`, `Mql5InventoryFormatter.cs` | inventory accuracy, plan determinism |
| `E09` | mql5-frontend-semantic | `.../Mql5FrontEnd.cs`, `Mql5SemanticModel.cs`, `Mql5Syntax.cs`, `Mql5CompileContracts.cs` | pipeline wiring, syntax model gaps |
| `E10` | mql5-restricted-subset | `.../Mql5RestrictedSubsetCompiler.cs`, `Mql5RestrictedSubsetContracts.cs`, `Mql5RestrictedCorpusArtifact.cs`, `Mql5RestrictedCorpusArtifactFormatter.cs` | subset enforcement, escape from subset |
| `E11` | mql5-equivalence-attest | `.../Mql5SemanticEquivalenceContracts.cs`, `Mql5SemanticEquivalenceVerifier.cs`, `Mql5RunnerAttestationVerifier.cs` | equivalence proof strength, verifier bypass |
| `E12` | mql5-source-safety | `.../Mql5SourceDecoder.cs`, `Mql5SourceSecretScanner.cs`, `Mql5MarkdownEscaper.cs`, `Mql5CompilerOutputParser.cs` | encoding attacks, secret scanner false negatives, injection via output |
| `E13` | mod-risk-policy | `src/Modules/Risk/**`, `src/Modules/Policy/**` | risk limits, policy evaluation order, fail-open |
| `E14` | mod-authz-identity | `src/Modules/Authorization/**`, `src/Modules/Identity/**`, `src/Modules/AdminIdentity/**`, `src/Modules/Tenancy/**` | permission model, tenant resolution |
| `E15` | mod-governance-ops | `src/Modules/Approvals/**`, `src/Modules/Audit/**`, `src/Modules/Incidents/**`, `src/Modules/Privacy/**`, `src/Modules/Support/**` | approval bypass, audit completeness, PII |
| `E16` | mod-commands-outbox | `src/Modules/Commands/**`, `src/Modules/Outbox/**`, `src/Modules/Deployments/**`, `src/Modules/ReadModels/**` | command contract, outbox semantics |
| `E17` | mod-runtime-secrets | `src/Modules/RuntimeOperations/**`, `src/Modules/GatewayGovernance/**`, `src/Modules/SecretCoordination/**`, `src/Modules/BrokerAccounts/**` | secret coordination, gateway policy |
| `E18` | strategy-version | `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/StrategyVersion.cs`, `AssemblyInfo.cs` | version identity, comparison, hashing |

## F — MQL5 transpiler, runtime, engine

| ID | Lane | Scope | Focus |
|---|---|---|---|
| `F01` | codegen-core | `src/Runtime/YO4X.Mql5.CodeGen/Mql5CodeGenerator.cs`, `Mql5GeneratorRun.cs` | pass structure, state leakage between runs |
| `F02` | codegen-expressions | `.../Mql5GeneratorRun.Expressions.cs` | operator semantics, integer division, promotion, short-circuit (1816 lines) |
| `F03` | codegen-declarations | `.../Mql5GeneratorRun.Declarations.cs` | default init, statics, arrays, struct layout |
| `F04` | codegen-statements | `.../Mql5GeneratorRun.Statements.cs` | loop semantics, break and continue, switch fallthrough |
| `F05` | codegen-calls | `.../Mql5GeneratorRun.Calls.cs` | by-ref params, overloads, default args |
| `F06` | codegen-types | `.../Mql5ClrTypes.cs` | MQL5 to CLR type mapping, overflow, unsigned, datetime (1399 lines) |
| `F07` | codegen-writer | `.../Mql5CSharpWriter.cs`, `Mql5EmittedHelpers.cs`, `Mql5ShadowedLocals.cs` | emitted-code correctness, identifier collision, shadowing |
| `F08` | codegen-assembly | `.../Mql5AssemblyBuilder.cs`, `Mql5RuntimeContract.cs`, `Mql5CodeGenContracts.cs`, `AssemblyInfo.cs` | assembly identity, contract drift with runtime |
| `F09` | roslyn-host | `src/Runtime/YO4X.Mql5.Compilation/RoslynMql5CompilationHost.cs` | reference set, unsafe or IO reachable from strategy, ALC unload |
| `F10` | rt-math-conversion | `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Math.cs`, `Mql5Runtime.Conversion.cs`, `Mql5Format.cs` | rounding, NormalizeDouble, string to number parity with MQL5 |
| `F11` | rt-array-text | `.../Mql5Runtime.Array.cs`, `Mql5Runtime.Text.cs` | ArrayResize and ArrayCopy semantics, as-series, string funcs, off-by-one |
| `F12` | rt-datetime | `.../Mql5Runtime.DateTime.cs`, `Mql5CalendarTypes.cs` | epoch, server time, TimeCurrent, DST, struct fields |
| `F13` | rt-trade | `.../Mql5Runtime.Trade.cs`, `Mql5TradeTypes.cs` | OrderSend semantics, request and result fields, error codes |
| `F14` | rt-marketdata-symbol | `.../Mql5Runtime.MarketData.cs`, `.../Mql5Runtime.Symbol.cs` | Copy* series direction, symbol properties, tick data |
| `F15` | rt-indicator-terminal | `.../Mql5Runtime.Indicator.cs`, `Mql5Runtime.Terminal.cs`, `Mql5Runtime.Chart.cs`, `Mql5ChartObjectStore.cs` | handle lifecycle, CopyBuffer, terminal properties |
| `F16` | rt-core-globals | `.../Mql5Runtime.cs`, `Mql5Runtime.Globals.cs`, `Mql5Runtime.Refused.cs`, `Mql5RuntimeOptions.cs`, `Mql5ZeroedInstance.cs`, `Mql5TypeInfo.cs`, `Mql5Structs.cs` | refusal completeness, global state, zero-init |
| `F17` | rt-constants-errors | `.../Mql5Constants.cs`, `Mql5ErrorCodes.cs`, `Mql5Colors.cs`, `Mql5Log.cs`, `Mql5ProgramInfo.cs`, `Mql5UnsupportedOperationException.cs`, `IMql5MarketContext.cs`, `IMql5Strategy.cs` | constant values vs MQL5 spec |
| `F18` | rt-stdlib-trade | `.../StandardLibrary/Mql5Trade.cs`, `Mql5TradeConstants.cs`, `Mql5TradeTransaction.cs` | CTrade parity, filling mode, deviation, retry |
| `F19` | rt-stdlib-info | `.../StandardLibrary/Mql5AccountInfo.cs`, `Mql5SymbolInfo.cs`, `Mql5PositionInfo.cs`, `Mql5OrderInfo.cs`, `Mql5DealInfo.cs`, `Mql5HistoryOrderInfo.cs` | selection semantics, stale state, property mapping |
| `F20` | engine-broker-sim | `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs` | fill logic, spread, slippage, margin, SL and TP trigger order (1078 lines) |
| `F21` | engine-trade-types | `src/Runtime/YO4X.Mql5.Engine/Trading/` (all except `Mql5SimulatedBroker.cs`) | position and order state, P&L math, enum parity |
| `F22` | engine-ind-a-f | `src/Runtime/YO4X.Mql5.Engine/Indicators/` — Adx, Alligator, Atr, AwesomeOscillator, Bands, Cci, DeMarker, Envelopes, ForceIndex, Fractals | formula vs MetaTrader reference, warm-up, seeding |
| `F23` | engine-ind-m-w | `src/Runtime/YO4X.Mql5.Engine/Indicators/` — Macd, Momentum, MovingAverage, OsMa, ParabolicSar, RelativeVigorIndex, Rsi, StdDev, Stochastic, WilliamsPercentRange | formula vs MetaTrader reference, warm-up, seeding |
| `F24` | engine-ind-infra | `.../Indicators/Mql5IndicatorBase.cs`, `Mql5IndicatorFactory.cs`, `RollingWindow.cs`, `MovingAverageCalculator.cs`, `IMql5Indicator.cs` | buffer indexing, ring buffer, MA mode dispatch |
| `F25` | engine-feed | `src/Runtime/YO4X.Mql5.Engine/Feed/**` | CSV parsing, bar ordering, gaps, synthetic determinism |
| `F26` | engine-hosting-context | `src/Runtime/YO4X.Mql5.Engine/Hosting/**`, `src/Runtime/YO4X.Mql5.Engine/Context/**` | tick loop, OnTick and OnInit order, run report accounting |
| `F27` | backtest-live-runner | `src/Runtime/YO4X.Mql5.Backtest/**`, `src/Runtime/YO4X.Mql5.Live/**` | backtest vs live divergence, look-ahead bias |

## G — Trading runtime, MT5 integration, hosts

| ID | Lane | Scope | Focus |
|---|---|---|---|
| `G01` | trading-mt5 | `src/Runtime/YO4X.Trading.Mt5/**` | MT5 adapter, order mapping, error translation |
| `G02` | process-isolation-server | `src/Runtime/YO4X.Trading.ProcessIsolation/AuthenticatedBrokerWorkerServer.cs`, `AuthenticatedBrokerConnectionProbeWorkerServer.cs`, `BrokerProcessProtocol.cs`, `BrokerProcessClient.cs` | auth on IPC, protocol framing, deserialization trust |
| `G03` | process-isolation-launch | `src/Runtime/YO4X.Trading.ProcessIsolation/` remaining (contracts, validator, path policy, launch manifest, gateway, options, probe client) | path traversal, launch arg injection, contract validation |
| `G04` | trading-abstractions | `src/Runtime/YO4X.Trading.Abstractions/**`, `src/Runtime/YO4X.Strategy.Abstractions/**` | contract shape, nullability, enum completeness |
| `G05` | runtime-contracts | `src/Runtime/YO4X.Runtime.Contracts/**` | serialization compatibility, versioning |
| `G06` | gateway-host | `src/Runtime/YO4X.GatewayHost/**` | gateway routing, backpressure, auth |
| `G07` | strategy-host-supervisor | `src/Runtime/YO4X.StrategyHost/**`, `src/Runtime/YO4X.Supervisor/**` | restart storms, crash handling, state after restart |
| `G08` | connection-probe | `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/**`, `src/Runtime/YO4X.Mt5.ConnectionProbe.WorkerHost.Windows/**` | probe correctness, credential exposure, timeout |
| `G09` | mt5-workerhost | `src/Runtime/YO4X.Mt5.WorkerHost/**` | host lifecycle, isolation boundary |
| `G10` | dpapi-vault | `src/Infrastructure/YO4X.LocalSecrets.Windows/**` | DPAPI scope, key handling, plaintext residue, ACLs |
| `G11` | vendor-binaries | `mt5-net-api-full-binaries-main/**` | supply chain: what is it, is it pinned and verified, what does it expose |

## H — Tools

| ID | Lane | Scope | Focus |
|---|---|---|---|
| `H01` | tool-backtest-runner | `src/Tools/YO4X.Backtest.Runner/**`, `src/Tools/YO4X.StrategyInputProjection/**` | arg handling, determinism, result reporting |
| `H02` | tool-credentials | `src/Tools/YO4X.LocalCredentialImporter/**`, `src/Tools/YO4X.LocalCredentialWriter/**`, `src/Tools/YO4X.DevelopmentBootstrap/**` | secrets on disk, CLI, or history; bootstrap defaults |
| `H03` | tool-marketdata | `src/Tools/YO4X.MarketData.Mt5History/**`, `src/Tools/YO4X.MarketData.Mt5Import/**` | data integrity, gap handling, duplicate bars |
| `H04` | tool-mt5-inspect | `src/Tools/YO4X.Mt5.AccountInspector/**`, `src/Tools/YO4X.Mt5.BrokerCatalogueImport/**`, `src/Tools/YO4X.Mt5.DemoCanary/**`, `src/Tools/YO4X.Mt5.DemoExecutionTest/**` | live-account safety, demo and live confusion |
| `H05` | tool-discovery-bots | `src/Tools/YO4X.Mt5.EndpointDiscovery/**`, `src/Tools/YO4X.Mt5.SymbolImport/**`, `src/Tools/YO4X.LiveBots/**` | endpoint trust, symbol mapping, live bot guards |

## I — Cross-cutting property sweeps

Each sweeps the **whole tree** for one property, and files findings only for that property.

| ID | Lane | Scope | Focus |
|---|---|---|---|
| `I01` | sweep-secrets | whole tree | hardcoded secrets, keys in logs, config, tests; `.env`; connection strings |
| `I02` | sweep-authz | whole tree | every endpoint and handler: is authz enforced, fail-open defaults, missing tenant check |
| `I03` | sweep-logging-pii | whole tree | credentials, PII, account numbers in logs; exception detail returned to client |
| `I04` | sweep-exceptions | whole tree | swallowed exceptions, empty catch, catch-all hiding money-path failures |
| `I05` | sweep-async | whole tree | `.Result` and `.Wait()`, async void, missing CancellationToken, sync-over-async |
| `I06` | sweep-disposal | whole tree | undisposed connections, streams, processes, timers; missing `using` |
| `I07` | sweep-injection | whole tree | SQL string concatenation, command injection, path traversal, unsafe deserialization |
| `I08` | sweep-config | appsettings `*.json`, `launchSettings.json`, `compose.yaml`, `.env.example`, `global.json` | insecure defaults, prod and dev bleed, missing required config |
| `I09` | sweep-build-deps | `*.csproj`, `Directory.Packages.props`, `Directory.Build.props`, `package.json` | version pinning, known-CVE packages, TFM mismatch, missing analyzers |
| `I10` | sweep-scripts | `scripts/*.ps1`, `scripts/*.mjs`, `scripts/*.sql` | injection, secret echo, destructive ops without guard |
| `I11` | sweep-concurrency | workers, stores, hosts | races, lost updates, non-atomic check-then-act, lock ordering |
| `I12` | sweep-datetime | whole tree | `DateTime.Now` vs `UtcNow`, unspecified Kind, broker-time assumptions, DST |
| `I13` | sweep-money-precision | whole tree | double used for money, rounding direction, lot normalisation, tick size |
| `I14` | sweep-idempotency | command, outbox, and broker paths | duplicate submission, missing idempotency key, retry without dedup |
| `I15` | sweep-nullability | whole tree | nullable annotation gaps on public contracts, `!` suppressions hiding NREs |
| `I16` | sweep-resource-limits | whole tree | unbounded collections, queries, allocations; missing paging; DoS via input size |

## J — Tests and documentation

| ID | Lane | Scope | Focus |
|---|---|---|---|
| `J01` | tests-postgres | `tests/YO4X.Postgres.IntegrationTests/**` | do they assert real invariants or merely that nothing threw |
| `J02` | tests-mql5 | `tests/YO4X.Mql5.Engine.Tests/**`, `tests/YO4X.Mql5.Runtime.Tests/**` | oracle strength: compared against what reference |
| `J03` | tests-trading-runtime | `tests/YO4X.Trading.Application.Tests/**`, `tests/YO4X.Runtime.Tests/**`, `tests/YO4X.Runtime.Application.Tests/**`, `tests/YO4X.Domain.Tests/**` | money-path coverage |
| `J04` | tests-api-infra | `tests/YO4X.Api.Tests/**`, `tests/YO4X.GatewayHost.Tests/**`, `tests/YO4X.Desktop.Tests/**`, `tests/YO4X.Architecture.Tests/**`, `tests/YO4X.DevelopmentIdentity.Tests/**` | boundary tests; are architecture rules actually enforced |
| `J05` | tests-postgres-modules | `tests/YO4X.Admin.Postgres.Tests/**`, `tests/YO4X.ControlPlane.Postgres.Tests/**`, `tests/YO4X.Trading.Postgres.Tests/**`, `tests/YO4X.RuntimeControl.Postgres.Tests/**`, `tests/YO4X.LocalSecrets.Windows.Tests/**`, `tests/YO4X.Worker.Tests/**`, `tests/YO4X.BrokerProcess.TestWorker/**` | assertion strength |
| `J06` | docs-backend-drift | `docs/backend/**` | claims vs actual implementation, citing both sides |
| `J07` | docs-arch-drift | `docs/*.md`, `docs/decisions/**`, `docs/frontend/**`, `README.md`, `src/Frontend/YO4X.Web/README.md` | architecture docs vs code reality |

## K — MQL5 corpus (`Testing/Mq5`, 213 files)

| ID | Lane | Scope | Focus |
|---|---|---|---|
| `K01` | corpus-survey-1 | `Testing/Mq5` files whose name starts with a digit or A-B | language features used; which ones the transpiler cannot handle |
| `K02` | corpus-survey-2 | `Testing/Mq5` files starting C-F | same |
| `K03` | corpus-survey-3 | `Testing/Mq5` files starting G-M | same |
| `K04` | corpus-survey-4 | `Testing/Mq5` files starting N-S | same |
| `K05` | corpus-survey-5 | `Testing/Mq5` files starting T-Z, plus all `.mqh` includes | same, plus include resolution |
| `K06` | corpus-feature-gap | corpus **vs** `Mql5Parser.cs` and `Mql5BuiltinSignatures.cs` | rank the top unsupported constructs by corpus frequency |
| `K07` | corpus-manifest | `docs/backend/mq5-static-manifest.v1.json`, `mql5-quarantine-intake.v2.json`, `MQ5_COMPATIBILITY_REPORT.md`, `MQL5_NONCANONICAL_INTAKE_REPORT.md` | manifest vs actual corpus state |
| `K08` | corpus-hygiene | `Testing/Mq5` as a whole | secrets, licence keys, or account numbers embedded in vendor EAs; odd filenames |

## L — External research (web)

These agents research the internet, then compare what they find against this repo.

| ID | Lane | Scope | Focus |
|---|---|---|---|
| `L01` | research-mql5-spec | MQL5 official documentation | canonical semantics for the areas `F10`-`F19` implement; list concrete divergences |
| `L02` | research-mt5-trading | MT5 order, margin, and swap documentation | filling modes, stop levels, margin calculation; compare to `F20` |
| `L03` | research-indicators | MetaTrader indicator reference | canonical formulas; compare to `F22`-`F24` |
| `L04` | research-dotnet-cve | NuGet advisories, .NET, Npgsql, Roslyn guidance | CVEs and misuse patterns for the pinned versions |
| `L05` | research-frontend-cve | npm advisories, React and Vite guidance | CVEs and misuse patterns for the pinned versions |
| `L06` | research-postgres | PostgreSQL documentation | isolation levels, advisory locks, `SKIP LOCKED` queue patterns vs `B09` and `B11` |
