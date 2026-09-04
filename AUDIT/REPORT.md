# YO4X Fleet Audit — Consolidated Report

_Generated: 2026-08-29T11:58:11Z · 157/156 lanes reported_

Produced by a fleet of Gemini 3.7 agents, one per lane, under `AUDIT/CHARTER.md`.
Full per-lane detail with code quotes lives in `AUDIT/findings/`.

> **These are machine-generated findings.** Every P0 and P1 needs a human to confirm
> the failure scenario before code changes rest on it. This is a prioritised list of
> places to look, not a verdict.

## Totals

| Severity | Count | Meaning |
|---|---|---|
| **P0** | 23 | Exploitable, or loses money / data / positions |
| **P1** | 194 | Wrong behaviour under reachable conditions |
| **P2** | 178 | Robustness: unhandled failure, leak, missing validation |
| **P3** | 72 | Quality that will cause a future defect |

## P0 findings (23)

| Lane | Finding | Location |
|---|---|---|
| D04-db-roles | Missing row-level security on 16 tenant projection tables permits cross-tenant data access under `yo4x_control_api` | `src/BuildingBlocks/YO4X.Persistence.Postgres/Security/least_privilege_roles.sql:1892-1897` |
| D06-role-fingerprint | Hardcoded 8-schema scope in privilege extraction causes false accept of broad grants in external schemas | `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRoleCapabilityFingerprint.cs:1732-1761` |
| E08-mql5-inventory-dossier | Preprocessor Directive Whitespace Evasion Bypasses Native DLL and Include Governance | `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5StaticInventoryAnalyzer.cs:1060-1064` |
| E16-mod-commands-outbox | Stale `BrokerReconciled` flag allows running deployments to be marked stopped before positions are flat | `src/Modules/Deployments/YO4X.Deployments/Deployment.cs:207-239` |
| F09-roslyn-host | Referencing `System.Private.CoreLib` exposes full file system, environment, reflection, and interop APIs to untrusted strategy code | `src/Runtime/YO4X.Mql5.Compilation/RoslynMql5CompilationHost.cs:127` |
| F11-rt-array-text | ArrayCopy, ArrayInsert, and ArrayRemove drop AS_SERIES flag on array reallocation | `[Mql5Runtime.Array.cs:183-188](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Array.cs#L183-L188)` |
| F11-rt-array-text | ArrayMaximum and ArrayMinimum ignore AS_SERIES flag on user arrays | `[Mql5Runtime.Array.cs:478-511](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Array.cs#L478-L511)` |
| F14-rt-marketdata-symbol | ArraySetAsSeries flag is silently destroyed when Copy* functions resize target buffer | `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.MarketData.cs:390` |
| F18-rt-stdlib-trade | PositionClosePartial does not validate volume ceiling, reversing positions on netting accounts | `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5Trade.cs:437-452` |
| F20-engine-broker-sim | Pending order activations bypass free margin validation entirely | `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:399-408` |
| F20-engine-broker-sim | Intra-bar pending order activation at swing extremes evades same-bar StopLoss | `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:282-283` |
| F20-engine-broker-sim | Intra-bar margin stop out is deferred until bar close | `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:159-165` |
| F27-backtest-live-runner | Unsynchronized multithreaded quote ingestion races with live strategy execution | `src/Runtime/YO4X.Mql5.Live/LiveStrategyRunner.cs:188-210` |
| F27-backtest-live-runner | Opposite market order heuristic closes existing positions and breaks hedging and partial scaling | `src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs:218-232` |
| F27-backtest-live-runner | Pending orders are added to `open` position list, corrupting position counts and order state | `src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs:278-281` |
| H04-tool-mt5-inspect | DemoExecutionTest accepts `--environment live` and executes real trades on live funded accounts | `src/Tools/YO4X.Mt5.DemoExecutionTest/Program.cs:41-43` |
| I05-sweep-async | Synchronous GetAwaiter().GetResult() on Async Broker Calls in Live Trading Path | `src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs:292` |
| I13-sweep-money-precision | Float truncation in `CAccountInfo.MaxLotCheck` causes 33% sizing loss and lot step divergence | `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5AccountInfo.cs:137-141` |
| I13-sweep-money-precision | Unimplemented `OrderCalcMargin` in backtesting context breaks standard library position sizing | `src/Runtime/YO4X.Mql5.Runtime/IMql5MarketContext.cs:257-261` |
| I13-sweep-money-precision | `NormalizeVolume` rounds half away from zero instead of rounding down, violating margin limits | `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SymbolSpec.cs:65-66` |
| K08-corpus-hygiene | Opaque Compiled `.ex4`/`.ex5` Executable Binaries and ZIP Archives in Test Corpus | `Testing/Mq5/Multi Sniper mq No DLL_fix.ex4:1, Testing/Mq5/The Gold Reaper 4.1 Enhanced.ex4:1, Testing/Mq5/Crude oil scalper.zip:1, Testing/Mq5/HyperGal Alpha EA.zip:1` |
| K08-corpus-hygiene | Native `user32.dll` `#import` for MetaTrader GUI Button and Process Hijacking | `Testing/Mq5/News Stopper MT5.mq5:20-27, Testing/Mq5/AutoTrading Scheduler.mqh:4-6` |
| K08-corpus-hygiene | Remote Control Channel and Unauthenticated Command Execution via Telegram `WebRequest` Polling | `Testing/Mq5/4rexbot.mq5:1007-1075` |

## P1 findings (194)

| Lane | Finding | Location |
|---|---|---|
| A02-api-contracts | `decodeStrategyInputView` rejects legitimate enum inputs declaring no local members | `src/Frontend/YO4X.Web/src/api/contracts.ts:1685-1688` |
| A02-api-contracts | `decodeBacktestDetailView` rejects historical backtests emitting `"UNSPECIFIED"` model | `src/Frontend/YO4X.Web/src/api/contracts.ts:1863` |
| A02-api-contracts | `botMagicNumberBound` in `decodeBotSettingsView` rejects valid unsigned 32-bit and 64-bit magic numbers | `src/Frontend/YO4X.Web/src/api/contracts.ts:1940` |
| A09-dashboard | `formatMoney` fallback prepends positive `+` sign to zero P/L (`+0.00`) for non-ISO currencies | `src/Frontend/YO4X.Web/src/features/dashboard/DashboardPage.tsx:90` |
| A10-backtests-ui | Submit button remains enabled when strategy inputs fail to load, permitting submission of unconfigured backtests | `src/Frontend/YO4X.Web/src/features/backtests/NewBacktestModal.tsx:648-655` |
| A10-backtests-ui | Double submission on rapid keyboard submission creates duplicate backtest queue entries | `src/Frontend/YO4X.Web/src/features/backtests/NewBacktestModal.tsx:196-206` |
| A11-backtest-form | `formatColourValue` emits CSS hex colors rejected by backend `IsColour` validator | `src/Frontend/YO4X.Web/src/features/backtests/backtestForm.ts:106-119` |
| A12-bots-ui | `serverForBot` falls back to `accounts[0]` when `bot.brokerAccountId` is unlinked or missing | `src/Frontend/YO4X.Web/src/features/bots/BotSettingsModal.tsx:73-76` |
| A12-bots-ui | Search term clearing invalidates `instrument` cache, bypassing broker volume bounds in `validateRunSettings` | `src/Frontend/YO4X.Web/src/features/bots/BotSettingsModal.tsx:135-149` |
| A12-bots-ui | `validateRunSettings` omits `instrument.volumeStep` validation, allowing invalid lot increments | `src/Frontend/YO4X.Web/src/features/bots/botSettingsForm.ts:184-195` |
| A12-bots-ui | `botMagicNumberBound` rejects valid unsigned 32-bit and 64-bit MT5 magic numbers | `src/Frontend/YO4X.Web/src/features/bots/botSettingsForm.ts:196-199` |
| A13-broker-hooks | Account ID change fails to reset connection test state, causing spurious ContractViolationError and stale submission reuse | `src/Frontend/YO4X.Web/src/features/broker-accounts/hooks/useBrokerAccountConnection.ts:121-129` |
| A18-overlays | LinkAccountModal form submit does not guard against submit-in-flight, permitting duplicate account registrations via Enter key | `src/Frontend/YO4X.Web/src/features/overlays/LinkAccountModal.tsx:152-177` |
| A18-overlays | LaunchWizard hardcodes execution host to LOCAL on open, ignoring requested CLOUD launch host | `src/Frontend/YO4X.Web/src/features/overlays/LaunchWizard.tsx:172-173` |
| A18-overlays | Live bot launch permitted while manual test position is open on the account | `src/Frontend/YO4X.Web/src/features/overlays/LaunchWizard.tsx:751-758` |
| A20-styling | `.backtests-profit` statically hardcodes profit green, displaying net backtest losses in green | `src/Frontend/YO4X.Web/src/features/backtests/backtests.css:61-65` |
| A20-styling | WCAG AA contrast failure (2.37:1) on `--color-text-faint` across footnotes, disclosures, and pricing terms | `src/Frontend/YO4X.Web/src/app/styles/tokens.css:38` |
| A23-fe-qa-scripts | `stub-api.mjs` returns `BrokerAccountRegistrationOption` payloads missing required contract fields | `src/Frontend/YO4X.Web/scripts/stub-api.mjs:160-162` |
| A23-fe-qa-scripts | Faulty date arithmetic in `stub-api.mjs` emits invalid calendar dates for `/v1/bots/uptime` | `src/Frontend/YO4X.Web/scripts/stub-api.mjs:85-90` |
| B02-cp-api-host | `IPAddress.IsLoopback` check rejects IPv4 loopback connections on dual-stack listener during broker account linking | `src/Apps/YO4X.ControlPlane.Api/Program.cs:178` |
| B12-worker-host | Liveness probe unconditionally reports healthy when worker workstreams are terminally stopped | `src/Apps/YO4X.ControlPlane.Workers/WorkerReadiness.cs:62` |
| B16-contract-drift | `decodeBacktestDetailView` rejects historical backtests carrying `"UNSPECIFIED"` model | `src/Frontend/YO4X.Web/src/api/contracts.ts:1863` |
| B16-contract-drift | `decodeStrategyInputView` rejects enum inputs with empty enum member declarations | `src/Frontend/YO4X.Web/src/api/contracts.ts:1685-1688` |
| C06-buildingblocks-core | OperationResult.Failure with empty params evaluates to IsSuccess == true | `src/BuildingBlocks/YO4X.BuildingBlocks/OperationResult.cs:14` |
| D03-migrations-late | Missing queue lease timeout mechanism in backtest queue causes permanent worker stall on crash | `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/008_backtest_queue_worker_access.sql:39-41` |
| D03-migrations-late | `volume_step` and `volume_min` `numeric(12,2)` precision causes arithmetic underflow and constraint violation for crypto and micro-lot instruments | `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/010_bot_settings_and_broker_symbols.sql:120-122` |
| D03-migrations-late | 3-character ISO constraint on `bots.broker_symbols.currency` rejects standard 4-character crypto and stablecoin quote currencies | `src/BuildingBlocks/YO4X.Persistence.Postgres/Migrations/010_bot_settings_and_broker_symbols.sql:123` |
| D04-db-roles | Unrestricted table-level CRUD on global billing configuration tables granted to web API role | `src/BuildingBlocks/YO4X.Persistence.Postgres/Security/least_privilege_roles.sql:1901-1903` |
| D06-role-fingerprint | Synchronous full-catalog SHA-256 computation inside every tenant transaction causes severe hot-path latency | `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRoleCapabilityFingerprint.cs:2009-2018` |
| D06-role-fingerprint | Role configuration array comparison omits C collation, causing false rejection under localized database collations | `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRoleCapabilityFingerprint.cs:1643-1648` |
| D06-role-fingerprint | Unregistered database roles and grantees in external schemas bypass catalog semantic fingerprint | `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRoleCapabilityFingerprint.cs:536-547` |
| D12-runtimecontrol-postgres | Failed or non-applied control command delivery events are unconditionally recorded and reported as applied | `src/Infrastructure/YO4X.RuntimeControl.Postgres/PostgresRuntimeEvents.cs:188` |
| D12-runtimecontrol-postgres | Broker and deployment user operation result ingress executes U0 authority lock and recorder queries on restricted evidence database | `src/Infrastructure/YO4X.RuntimeControl.Postgres/PostgresBrokerUserOperationResults.cs:186` |
| E01-mql5-lexer | Floating literal with trailing dot and float suffix '1.f' splits into three invalid tokens | `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Lexer.cs:646` |
| E01-mql5-lexer | Named colour literals C'Red' fail normalization and emit error diagnostics | `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Lexer.cs:947` |
| E01-mql5-lexer | Lowercase literal prefixes c'...' and d'...' are mis-tokenized as identifiers | `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Lexer.cs:264` |
| E01-mql5-lexer | String literals with backslash line continuations trigger unterminated string errors | `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Lexer.cs:750` |
| E02-mql5-parser | Local variable declarations with constructor arguments are misclassified and dropped | `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Parser.cs:2148` |
| E02-mql5-parser | Templated or scoped secondary base classes discard entire class declaration | `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Parser.cs:1468-1480` |
| E03-mql5-binder | Real-valued built-in constants (EMPTY_VALUE, DBL_MAX, M_PI) silently resolve as 32-bit integers with value 0 | `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Binder.cs:2167` |
| E03-mql5-binder | Overload resolution ignores parameter types and picks first declaration on ambiguous arity matches | `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Binder.cs:1877` |
| E03-mql5-binder | NULL constant resolves as Whole32 integer instead of NullLiteral | `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Binder.cs:1408` |
| E05-mql5-builtins | Duplicate/incorrect constant value for `SYMBOL_CALC_MODE_EXCH_OPTIONS` | `[src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5BuiltinConstants.cs:1541-1543](file:///C:/Users/Dev23/Desktop/yo4x/src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5BuiltinConstants.cs#L1541-L1543)` |
| E07-mql5-evidence | Multiline unescaped string literals are parsed without error and swallow intermediate code tokens | `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5ConversionEvidenceAnalyzer.cs:545-554` |
| E08-mql5-inventory-dossier | Missed Plural Global Variable Built-ins in Terminal Globals Detection | `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5StaticInventoryAnalyzer.cs:1027-1028` |
| E08-mql5-inventory-dossier | Missed `Folder*` File System APIs in File IO Detection | `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5StaticInventoryAnalyzer.cs:1012-1013` |
| E08-mql5-inventory-dossier | Missed `SendFTP`, `SendMail`, and `SendNotification` in Network IO Detection | `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5StaticInventoryAnalyzer.cs:1015-1016` |
| E08-mql5-inventory-dossier | WinAPI Sub-Headers Under-Reported as `NeedsSource` Instead of `Unsupported` OS Include | `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5StaticInventoryAnalyzer.cs:590-595` |
| E11-mql5-equivalence-attest | Semantic Equivalence Verifier Claims Parity "Proven" on Finite Non-Adversarial Sample Traces | `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SemanticEquivalenceVerifier.cs:356` |
| E11-mql5-equivalence-attest | Dual-Limit Numeric Tolerance Comparison Causes False Parity Rejections Near Zero | `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SemanticEquivalenceVerifier.cs:303-306` |
| E12-mql5-source-safety | BOM-less UTF-16LE sources shorter than 64 bytes misclassified as binary | `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SourceDecoder.cs:248-251` |
| E12-mql5-source-safety | Secret scanner fails to detect preprocessor macros, split strings, and non-whitelisted variable names | `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SourceSecretScanner.cs:79-86` |
| E14-mod-authz-identity | UserIdentity.VerifyEmail bypasses lock and recovery states when email is unverified | `src/Modules/Identity/YO4X.Identity/UserIdentity.cs:58` |
| E16-mod-commands-outbox | `DeploymentConfiguration.ConfigurationHash` property causes infinite recursive serialization and `StackOverflowException` | `src/Modules/Deployments/YO4X.Deployments/Deployment.cs:42` |
| E16-mod-commands-outbox | `TypedCommand.BeginDispatch` bypasses resource version watermark checks for referenced impact previews | `src/Modules/Commands/YO4X.Commands/TypedCommand.cs:244-261` |
| E16-mod-commands-outbox | `Deployment.ConfirmReconciled` unconditionally forces state to `Running`, overriding restrictive states | `src/Modules/Deployments/YO4X.Deployments/Deployment.cs:195-204` |
| F01-codegen-core | Nested type and enum registration overwrites lookup entries keyed by unqualified names | `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.cs:406-414` |
| F01-codegen-core | Static local variable field generator produces invalid C# identifier names for out-of-line functions | `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.cs:481-482` |
| F01-codegen-core | Static local variable hoisting collides and produces duplicate field declarations for overloads and inner blocks | `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.cs:430-435` |
| F01-codegen-core | Nested class out-of-line method definitions fail to resolve and are dropped | `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.cs:75-81` |
| F01-codegen-core | Static local variables declared inside inline class methods are never collected | `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.cs:373-379` |
| F02-codegen-expressions | Compound assignment on text, enum, and boolean expressions evaluates target twice | `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Expressions.cs:1271-1272, 1302-1303, 1314-1315` |
| F02-codegen-expressions | Unary negation on unsigned 64-bit integer (`ulong` / `Natural64`) emits invalid C# (CS0023) | `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Expressions.cs:1140-1142` |
| F02-codegen-expressions | Relational comparison between `string` and non-string types emits invalid C# (CS0019) | `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Expressions.cs:1188-1190, 1208-1215` |
| F02-codegen-expressions | Narrow integer binary arithmetic (`short`, `ushort`, `byte`, `sbyte`) skips narrowing cast on assignment | `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Expressions.cs:976-977, 1637-1640, 1263` |
| F03-codegen-declarations | Static local structures and class instances are not instantiated in constructor | `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs:821-845` |
| F03-codegen-declarations | Struct and class member fields omit default initialization for object, structure, and string types | `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs:548-575` |
| F03-codegen-declarations | OutOfLineBody matches overloads by parameter count only, dropping distinct signatures | `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs:365-373` |
| F03-codegen-declarations | Generic base class lookup in EmitTypeDeclaration shadows runtime and owner fields | `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs:440-442` |
| F03-codegen-declarations | Prefix matching in EmitMethodCore corrupts static local symbol rewriting across functions | `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs:975-983` |
| F04-codegen-statements | Trailing empty switch section emits orphan case label before switch closing brace causing CS8070 | `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Statements.cs:352-355` |
| F04-codegen-statements | Void function return statement with expression silently drops expression side effects | `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Statements.cs:245-249` |
| F05-codegen-calls | Member, sibling, and qualified calls emit plain arguments without `ref` for user methods with by-reference parameters | `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Calls.cs:57-60` |
| F05-codegen-calls | Module-declared enumeration functional casts are omitted in `EmitNamedCall` and rejected as uncallable | `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Calls.cs:156-174` |
| F05-codegen-calls | `EmitModuleCall` selects overloads by argument count alone, converting arguments to wrong parameter types | `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Calls.cs:207-216` |
| F06-codegen-types | Predefined variable `_UninitReason` maps to nonexistent property `UninitReason` | `src/Runtime/YO4X.Mql5.CodeGen/Mql5ClrTypes.cs:86` |
| F06-codegen-types | Predefined variables `_RandomSeed`, `_IsX64`, and `_AppliedTo` map to nonexistent `IMql5Runtime` members | `src/Runtime/YO4X.Mql5.CodeGen/Mql5ClrTypes.cs:84` |
| F06-codegen-types | `retcode_external` in `RuntimeMemberClrTypes` emits invalid `(uint)` cast on signed `int` property | `src/Runtime/YO4X.Mql5.CodeGen/Mql5ClrTypes.cs:259` |
| F06-codegen-types | `StructToTime` marked with `r` in `RuntimeByReferenceParameters` emits illegal `ref` for `in` parameter | `src/Runtime/YO4X.Mql5.CodeGen/Mql5ClrTypes.cs:1180` |
| F07-codegen-writer | String-to-number helpers fail on numeric prefixes with trailing non-numeric characters | `src/Runtime/YO4X.Mql5.CodeGen/Mql5EmittedHelpers.cs:78-85` |
| F09-roslyn-host | Compiler warnings indicating broken code generation are demoted to `Information` and ignored | `src/Runtime/YO4X.Mql5.Compilation/RoslynMql5CompilationHost.cs:108-110` |
| F10-rt-math-conversion | StringToInteger fails to parse hexadecimal strings and returns 0 | `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Conversion.cs:263` |
| F10-rt-math-conversion | ToUInt64 in Mql5Format omits signed integer types causing 64-bit sign extension under %u, %x, %X, %o | `src/Runtime/YO4X.Mql5.Runtime/Mql5Format.cs:305` |
| F10-rt-math-conversion | StringToTime synthesizes missing date components using host machine's wall-clock date | `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Conversion.cs:313` |
| F11-rt-array-text | StringInit with character=0 clears string instead of allocating space-filled buffer | `[Mql5Runtime.Text.cs:333-334](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Text.cs#L333-L334)` |
| F11-rt-array-text | ArrayCompare returns 0 (equal) on out-of-bounds start offsets | `[Mql5Runtime.Array.cs:346-350](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Array.cs#L346-L350)` |
| F11-rt-array-text | StringFind clamps negative startPosition to 0 instead of returning -1 | `[Mql5Runtime.Text.cs:142-146](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Text.cs#L142-L146)` |
| F12-rt-datetime | StructToTime treats valid epoch (0) as an error and returns 0 instead of WRONG_VALUE (-1) on invalid dates | `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.DateTime.cs:131` |
| F12-rt-datetime | TimeToStruct unconditionally returns true and fails to detect negative or invalid timestamps | `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.DateTime.cs:124` |
| F13-rt-trade | Out-parameter entity property getters unconditionally return true when no entity is selected | `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Trade.cs:289` |
| F14-rt-marketdata-symbol | SymbolInfo and SeriesInfo out-parameter overloads unconditionally return true | `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Symbol.cs:127` |
| F14-rt-marketdata-symbol | Dynamic destination arrays are not resized to actual copied element count | `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.MarketData.cs:388` |
| F15-rt-indicator-terminal | `IndicatorRelease` does not evict handle from runtime cache, returning dead handles on re-creation | `[Mql5Runtime.Indicator.cs:369](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Indicator.cs#L369)` |
| F15-rt-indicator-terminal | `IndicatorCreate` drops `symbol` argument and passes misaligned arguments to market context | `[Mql5Runtime.Indicator.cs:349-366](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Indicator.cs#L349-L366)` |
| F15-rt-indicator-terminal | `CopyBufferCore` drops `ArraySetAsSeries` flag on buffer resize, leaving timeseries data unreversed | `[Mql5Runtime.Indicator.cs:449-458](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Indicator.cs#L449-L458)` |
| F16-rt-core-globals | Mql5Time.ToStruct sets 1-based DayOfYear diverging from MQL5 0-based specification | `src/Runtime/YO4X.Mql5.Runtime/Mql5Structs.cs:177` |
| F17-rt-constants-errors | `Mql5Colors.Name` aliases `ColorNone` (-1) to `"White"`, corrupting `clrNONE` string conversion | `src/Runtime/YO4X.Mql5.Runtime/Mql5Colors.cs:59` |
| F17-rt-constants-errors | `Mql5Colors.TryParse` fails to recognize `"clrNone"`, `"clrNONE"`, and `"None"` | `src/Runtime/YO4X.Mql5.Runtime/Mql5Colors.cs:74-83` |
| F18-rt-stdlib-trade | Constructor omits SetMarginMode initialization, causing IsHedging to evaluate false on hedging accounts | `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5Trade.cs:22-27` |
| F18-rt-stdlib-trade | Hardcoded default OrderFillingFok causes immediate order rejections on Market Execution symbols | `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5Trade.cs:26` |
| F18-rt-stdlib-trade | PositionClose and PositionModify by symbol operate only on a single position in Hedging mode | `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5Trade.cs:298-304` |
| F19-rt-stdlib-info | MaxLotCheck rejects valid fractional margin percentages under 1.0% | `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5AccountInfo.cs:118` |
| F20-engine-broker-sim | Stop loss validation in `ExecuteDeal` evaluates against entry price instead of closing quote | `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:471-476` |
| F20-engine-broker-sim | Netting position reduction and addition overwrites or deletes surviving position stops | `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:901-902` |
| F20-engine-broker-sim | Exit side commission is never charged on position close | `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:998-1002` |
| F20-engine-broker-sim | Rollover swap is never accrued on open positions across multi-day bars | `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:950` |
| F21-engine-trade-types | `Mql5MarginMode.Hedging` assigns ordinal 1 instead of 2, misidentifying hedging accounts as exchange netting | `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5Enums.cs:55-62` |
| F22-engine-ind-a-f | Force Index smooths price instead of volume-price product, discarding historical volume weighting | `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5ForceIndexIndicator.cs:67` |
| F24-engine-ind-infra | RollingWindow ring-buffer index arithmetic reads uninitialized slots before window is full | `src/Runtime/YO4X.Mql5.Engine/Indicators/RollingWindow.cs:23` |
| F24-engine-ind-infra | Mql5IndicatorFactory.CreateAtr uses arity 2 instead of 1, corrupting ATR period with timeframe constant | `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5IndicatorFactory.cs:125` |
| F25-engine-feed | Multi-character separator split corrupts comma-decimal European CSV rows into invalid price spikes | `[src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs:13](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs#L13-L13) and [src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs:82](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs#L82-L82)` |
| F25-engine-feed | Lack of OHLC invariant and positive price validation allows impossible bar geometries into the engine | `[src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs:98-104](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs#L98-L104)` |
| F25-engine-feed | CsvMarketFeed fails to enforce monotonic ascending time order and duplicate timestamp rejection | `[src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs:53-62](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Engine/Feed/Mql5CsvMarketFeed.cs#L53-L62)` |
| F26-engine-hosting-context | MaxDrawdownPercent is overwritten by absolute drawdown instead of tracking peak relative decline | `src/Runtime/YO4X.Mql5.Engine/Hosting/Mql5StrategyHost.cs:217-226` |
| F27-backtest-live-runner | Stop loss / take profit modifications (`TRADE_ACTION_SLTP` and `TRADE_ACTION_MODIFY`) are refused live | `src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs:189-201` |
| F27-backtest-live-runner | `PositionGetDouble` for StopLoss/TakeProfit and `PositionGetInteger` for Magic number return 0 in Live | `src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs:149-166` |
| F27-backtest-live-runner | `PositionGetSymbol(int index)` fails to select position in `LiveBrokerContext` | `src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs:145-146` |
| F27-backtest-live-runner | `LiveBarSeries.Publish` hardcodes bar spread to 0, corrupting `CopyRates` spread live | `src/Runtime/YO4X.Mql5.Live/LiveBarSeries.cs:138-146` |
| G04-trading-abstractions | `PlaceOrderAction` and `UpdateProtectionAction` prohibit zero StopLoss and TakeProfit, rejecting unhedged entries and preventing protection removal | `src/Runtime/YO4X.Strategy.Abstractions/StrategyActions.cs:138-139` |
| G04-trading-abstractions | Default enum values across trading and strategy domain map to active execution members (`Buy`, `Market`, `Place`, `Accepted`) rather than `Unknown` | `src/Runtime/YO4X.Trading.Abstractions/GatewayModels.cs:43-47` |
| G04-trading-abstractions | `BrokerCommandLifecycle` marks `Reconciled` as terminal, permanently blocking subsequent fills for active reconciled orders | `src/Runtime/YO4X.Trading.Abstractions/BrokerCommandLifecycle.cs:49-53` |
| G04-trading-abstractions | `PlaceOrderAction` permits `RequestedOrderType.Limit`, `Stop`, and `StopLimit` with `requestedPrice = null`, emitting pending orders without trigger prices | `src/Runtime/YO4X.Strategy.Abstractions/StrategyActions.cs:133-136` |
| G04-trading-abstractions | `AuthorizedBrokerCommand.HasValidTargetShape` does not validate positive volume for `Place` or `Close` actions | `src/Runtime/YO4X.Trading.Abstractions/AuthorizedBrokerCommand.cs:479-503` |
| G07-strategy-host-supervisor | Uncaught exceptions in strategy execution bypass bounded validation and crash host | `src/Runtime/YO4X.StrategyHost/StrategyExecutionCoordinator.cs:21-25` |
| G07-strategy-host-supervisor | Supervisor host lacks process supervision, restart backoff, and crash loop limits | `src/Runtime/YO4X.Supervisor/Program.cs:4-16` |
| G08-connection-probe | Connection probe transport fabricates Demo environment without verifying broker account group | `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiConnectionOnlyTransport.cs:286-294` |
| H01-tool-backtest-runner | Unguarded double-to-decimal cast throws unhandled `OverflowException`, stranding claimed backtest in RUNNING status | `[src/Tools/YO4X.Backtest.Runner/Program.cs:257-266](file:///C:/Users/Dev23/Desktop/yo4x/src/Tools/YO4X.Backtest.Runner/Program.cs#L257-L266)` |
| H01-tool-backtest-runner | Weekend day deduction in data quality coverage underflows to 0% for partial-span backtests | `[src/Tools/YO4X.Backtest.Runner/Program.cs:367-379](file:///C:/Users/Dev23/Desktop/yo4x/src/Tools/YO4X.Backtest.Runner/Program.cs#L367-L379)` |
| H04-tool-mt5-inspect | Unhandled failure during position lifecycle leaves test positions open and pending orders active on broker | `src/Tools/YO4X.Mt5.DemoExecutionTest/Program.cs:83-91` |
| H05-tool-discovery-bots | `YO4X.LiveBots` queries backtests without tenant isolation, selecting and running other tenants' strategies | `src/Tools/YO4X.LiveBots/Program.cs:164-179` |
| H05-tool-discovery-bots | `YO4X.LiveBots` hardcodes price precision to 2 or 5 decimals, causing incorrect point calculations on non-forex and JPY pairs | `src/Tools/YO4X.LiveBots/Program.cs:136-142` |
| H05-tool-discovery-bots | `YO4X.Mt5.SymbolImport` passes `NpgsqlDbType.Char` for 3-letter currency codes, causing database insert errors | `src/Tools/YO4X.Mt5.SymbolImport/Program.cs:119-120` |
| I04-sweep-exceptions | LiveBrokerContext OrderSend exception filter allows transport failures to crash live runner | `src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs:267` |
| I04-sweep-exceptions | Mql5Binder catches and swallows catalog exceptions to resurrect legacy MQL4 functions | `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Binder.cs:2201` |
| I05-sweep-async | Expired Single-Flight Probe Returns Stale Result and Prevents Future Probes on Hung Dependency | `src/BuildingBlocks/YO4X.BuildingBlocks/BoundedBooleanProbe.cs:94` |
| I07-sweep-injection | Path traversal in `YO4X.Backtest.Runner` via unvalidated database `request.Symbol` | `src/Tools/YO4X.Backtest.Runner/Program.cs:193` |
| I10-sweep-scripts | Unscoped `DELETE` in `project-corpus-to-catalog.sql` deletes all tenant performance figures | `scripts/project-corpus-to-catalog.sql:101` |
| I12-sweep-datetime | `IMql5MarketContext` defaults `TimeGmt` to `TimeCurrent` and `TimeGmtOffset` to zero | `src/Runtime/YO4X.Mql5.Runtime/IMql5MarketContext.cs:134-143` |
| I12-sweep-datetime | `Mql5Time.ToStruct` sets 1-based .NET `DayOfYear` instead of MQL5 0-based `day_of_year` | `src/Runtime/YO4X.Mql5.Runtime/Mql5Structs.cs:176-177` |
| I12-sweep-datetime | `Mt5NetApiDemoTradeClient` falls back to `DateTime.UtcNow` when quote timestamp is missing | `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiDemoTradeClient.cs:658-659` |
| I12-sweep-datetime | Transpiler parses time-only datetime literals with host machine's current date | `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Expressions.cs:349-361` |
| I12-sweep-datetime | `Mt5TickExportReader` applies a single fixed UTC offset across historical tick data, ignoring DST | `src/Tools/YO4X.MarketData.Mt5Import/Mt5TickExportReader.cs:184-186` |
| I12-sweep-datetime | `Mql5SimulatedBroker` accrues weekend swap based on bar calendar date deltas | `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:153-157` |
| I13-sweep-money-precision | Hardcoded 2-decimal rounding in `CAccountInfo.MaxLotCheck` breaks fractional and crypto lots | `src/Runtime/YO4X.Mql5.Runtime/StandardLibrary/Mql5AccountInfo.cs:135` |
| I13-sweep-money-precision | PostgreSQL `numeric(12,2)` volume columns and projection validator reject fractional lot sizes | `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs:2351-2355` |
| I13-sweep-money-precision | `NormalizePrice` ignores symbol `TickSize` and `TickSize` is hardcoded to `Point` | `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SymbolSpec.cs:19, 55` |
| I13-sweep-money-precision | Floating-point cancellation drift in `RollingWindow.Add` causes indicator numerical drift | `src/Runtime/YO4X.Mql5.Engine/Indicators/RollingWindow.cs:27-38` |
| I13-sweep-money-precision | `Mql5RelativeVigorIndexIndicator` produces dimensional unit error on zero denominator | `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5RelativeVigorIndexIndicator.cs:64` |
| I15-sweep-nullability | Unchecked `DispatchMessageId!.Value` in worker proof readers crashes reconciliation loop | `src/Apps/YO4X.ControlPlane.Workers/Operations/PostgresUserOperationWorkStore.cs:2536` |
| I15-sweep-nullability | Transpiled module types leave `_runtime` and `__owner` references as `null!` in arrays and struct locals | `src/Runtime/YO4X.Mql5.CodeGen/Mql5GeneratorRun.Declarations.cs:455-459` |
| I15-sweep-nullability | `ZeroMemory` sets `string` variables to `null!` causing `NullReferenceException` in subsequent string operations | `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Terminal.cs:258-261` |
| J01-tests-postgres | Backtest queue claim and outcome tests execute against empty table and `where false` predicate | `tests/YO4X.Postgres.IntegrationTests/BacktestQueueWorkerAccessPostgresTests.cs:148` |
| J01-tests-postgres | Capability login test omits four security-critical roles from attestation filter | `tests/YO4X.Postgres.IntegrationTests/AdminReadinessPostgresTests.cs:137` |
| J06-docs-backend-drift | Baseline catalog fingerprint and least-privilege role script SHA-256 drift in closure ledger | `docs/backend/CLOSURE_LEDGER_2026-08-23.md:29-30` |
| J07-docs-arch-drift | Design System tokens and typography diverge from frontend code | `docs/frontend/DESIGN_SYSTEM.md:15-34` |
| J07-docs-arch-drift | Design system specifies nonexistent component families and routes from rejected concept | `docs/frontend/DESIGN_SYSTEM.md:60-70` |
| J07-docs-arch-drift | ADR 0001 asserts GatewayHost directly references `YO4X.Trading.Mt5` when code enforces process isolation | `docs/decisions/0001-backend-foundation.md:22` |
| K01-corpus-survey-1 | Inverted EMA crossover logic and unselected position trailing in `BTC_EMA_Crossover_TSL_EA_Hedging.mq5` | `Testing/Mq5/BTC_EMA_Crossover_TSL_EA_Hedging.mq5:40-49` |
| K01-corpus-survey-1 | Broken position counting in `9od10leporadi.mq5` repeatedly evaluates the first symbol position and bypasses max trade limits | `Testing/Mq5/9od10leporadi.mq5:36-47` |
| K01-corpus-survey-1 | Missing logical OR operators in `Ashu_Gold_EA_FINAL_V3.mq5` causes syntax error and compilation failure | `Testing/Mq5/Ashu_Gold_EA_FINAL_V3.mq5:58-62` |
| K01-corpus-survey-1 | Undeclared identifier `MAGIC_NUMBER` and legacy MQL4 trading functions in `Breakout_EA (1).mq5` | `Testing/Mq5/Breakout_EA (1).mq5:55-65` |
| K02-corpus-survey-2 | Reverse buffer indexing in `CRUDE_OIL_EMA_Crossover_TSL_EA.mq5` inverts buy and sell signals | `Testing/Mq5/CRUDE_OIL_EMA_Crossover_TSL_EA.mq5:41-50` |
| K02-corpus-survey-2 | Deal history inspection in `free_money_expert_robot_bot_hide_sl_and_tp_fixed_for_backtest.mq5` inspects entry deals and breaks Martingale lot sizing | `Testing/Mq5/free_money_expert_robot_bot_hide_sl_and_tp_fixed_for_backtest.mq5:66-88` |
| K02-corpus-survey-2 | Inverted TakeProfit recalculation in `cm_SL-NL-TP.mq5` overwrites TakeProfit with Stoploss distance | `Testing/Mq5/cm_SL-NL-TP.mq5:58` |
| K02-corpus-survey-2 | Synchronous WebRequest in `EA-MT5-OPENAI.mq5` blocks tick processing for up to 10 seconds and crashes in strategy tester | `Testing/Mq5/EA-MT5-OPENAI.mq5:2134-2136` |
| K03-corpus-survey-3 | Reverse buffer indexing in `GOLD_EMA_Crossover_TSL_EA.mq5` inverts buy and sell signals | `Testing/Mq5/GOLD_EMA_Crossover_TSL_EA.mq5:41-50` |
| K03-corpus-survey-3 | MQL4 `iMA` invocation signature in `mt DanielScalper.mq5` causes compilation failure and corrupted trend evaluation | `Testing/Mq5/mt DanielScalper.mq5:40-42` |
| K03-corpus-survey-3 | Hardcoded expiration date kill-switch in `heaven and hell.mq5` permanently aborts initialization | `Testing/Mq5/heaven and hell.mq5:44-59` |
| K03-corpus-survey-3 | Synchronous AI/Cloud `WebRequest` calls freeze tick execution and fail in strategy tester | `Testing/Mq5/GoldScalpingEA-1.mq5:605` |
| K03-corpus-survey-3 | Reverse buffer indexing in `GOLD_EMA_Crossover_TSL_EA.mq5` inverts buy and sell signals | `Testing/Mq5/GOLD_EMA_Crossover_TSL_EA.mq5:41-50` |
| K03-corpus-survey-3 | MQL4 `iMA` invocation signature in `mt DanielScalper.mq5` causes compilation failure and corrupted trend evaluation | `Testing/Mq5/mt DanielScalper.mq5:40-42` |
| K03-corpus-survey-3 | Hardcoded expiration date kill-switch in `heaven and hell.mq5` permanently aborts initialization | `Testing/Mq5/heaven and hell.mq5:44-59` |
| K03-corpus-survey-3 | Synchronous AI/Cloud `WebRequest` calls freeze tick execution and fail in strategy tester | `Testing/Mq5/GoldScalpingEA-1.mq5:605` |
| K04-corpus-survey-4 | All-NUL corrupted file in test corpus (Simple_Classic_Trailing.mq5) | `Testing/Mq5/Simple_Classic_Trailing.mq5:1` |
| K04-corpus-survey-4 | Unhandled C++ template class declarations and BOM-less UTF-16 in visual EA (Prop-Firm Expert.mq5) | `Testing/Mq5/Prop-Firm Expert.mq5:543` |
| K04-corpus-survey-4 | Out-of-sandbox Win32 DLL import via #import (News Stopper MT5.mq5) | `Testing/Mq5/News Stopper MT5.mq5:23` |
| K05-corpus-survey-5 | Missing include files prevent compilation of multiple strategies | `Testing/Mq5/Trailing Stop on Profit.mq5:11` |
| K05-corpus-survey-5 | Division by zero in THEHFT lot sizing during zero-stoploss execution | `Testing/Mq5/THEHFT.mq5:539` |
| K05-corpus-survey-5 | Unhandled zero tick size causes division by zero in lot size and pip value calculations | `Testing/Mq5/The REAL-GAINS Algo MT5 M1 EA.mq5:186` |
| K05-corpus-survey-5 | Synchronous external WebRequest and modal UI dialog halt execution and corrupt time calculations | `Testing/Mq5/The Gold Reaper v4.1 MT5.mq5:9908` |
| K05-corpus-survey-5 | Compiled MT4 binary artifact in MQL5 source corpus | `Testing/Mq5/The Gold Reaper 4.1 Enhanced.ex4:1` |
| K06-corpus-feature-gap | Local declaration lookahead rejects constructor-style initialization | `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5Parser.cs:2148` |
| K08-corpus-hygiene | Cleartext Account Number and Broker Identifier Exfiltration in Trailing Stop Notifications | `Testing/Mq5/Trailing Stop on Profit.mq5:190-202` |
| K08-corpus-hygiene | Hardcoded Expiration Timebomb Disabling Backtests and Live Execution Past April 2026 | `Testing/Mq5/7.mq5:13-71, Testing/Mq5/8.mq5:14-82, Testing/Mq5/titan v1.3.mq5:13-67` |
| K08-corpus-hygiene | Shared Common File System Trial Lock Escape via `FILE_COMMON` | `Testing/Mq5/Quantum Queen X 4.3.mq5:236-276` |
| L01-research-mql5-spec | `ArrayCopy`, `ArrayInsert`, and `ArrayRemove` discard the as-series indexing flag upon array reallocation | `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Array.cs:183` |
| L01-research-mql5-spec | `StringFind` clamps negative `startPosition` to 0 instead of returning -1 failure | `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.Text.cs:142` |
| L02-research-mt5-trading | Stop-out liquidation ignores accrued swap and commission when selecting worst position | `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:468-475` |
| L02-research-mt5-trading | Swap accrual lacks triple-swap rollover and overcharges weekend bar gaps | `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:153-157` |
| L02-research-mt5-trading | Freeze level (`SYMBOL_TRADE_FREEZE_LEVEL`) constraints are completely ignored | `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:658-676` |
| L02-research-mt5-trading | Slippage `Deviation` parameter in trade request is unchecked | `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:522-524` |
| L02-research-mt5-trading | Intrabar stop activations ignore price jumps and grant zero-slippage perfect fills | `src/Runtime/YO4X.Mql5.Engine/Trading/Mql5SimulatedBroker.cs:382-385` |
| L03-research-indicators | Parameter truncation in `Mql5IndicatorFactory.Numeric` misaligns arguments when timeframe is passed with omitted trailing parameters | `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5IndicatorFactory.cs:318-322` |
| L03-research-indicators | `iADX` incorrectly computes SMMA (Wilder) smoothing instead of canonical MetaTrader 5 EMA smoothing | `src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5IndicatorFactory.cs:65-66 and src/Runtime/YO4X.Mql5.Engine/Indicators/Mql5AdxIndicator.cs:46-49` |

## Per-lane summary

| Lane | P0 | P1 | P2 | P3 |
|---|---|---|---|---|
| [A02-api-contracts](findings/A02-api-contracts.md) | 0 | 3 | 2 | 1 |
| [A04-routing](findings/A04-routing.md) | 0 | 0 | 1 | 0 |
| [A05-app-shell](findings/A05-app-shell.md) | 0 | 0 | 1 | 1 |
| [A09-dashboard](findings/A09-dashboard.md) | 0 | 1 | 1 | 1 |
| [A10-backtests-ui](findings/A10-backtests-ui.md) | 0 | 2 | 3 | 0 |
| [A11-backtest-form](findings/A11-backtest-form.md) | 0 | 1 | 2 | 2 |
| [A12-bots-ui](findings/A12-bots-ui.md) | 0 | 4 | 2 | 1 |
| [A13-broker-hooks](findings/A13-broker-hooks.md) | 0 | 1 | 1 | 0 |
| [A15-strategies-ui](findings/A15-strategies-ui.md) | 0 | 0 | 2 | 0 |
| [A18-overlays](findings/A18-overlays.md) | 0 | 3 | 3 | 1 |
| [A19-shared-ui](findings/A19-shared-ui.md) | 0 | 0 | 1 | 0 |
| [A20-styling](findings/A20-styling.md) | 0 | 2 | 4 | 1 |
| [A21-fe-build](findings/A21-fe-build.md) | 0 | 0 | 2 | 0 |
| [A22-fe-tests](findings/A22-fe-tests.md) | 0 | 0 | 2 | 2 |
| [A23-fe-qa-scripts](findings/A23-fe-qa-scripts.md) | 0 | 2 | 3 | 1 |
| [B01-cp-api-endpoints](findings/B01-cp-api-endpoints.md) | 0 | 0 | 1 | 1 |
| [B02-cp-api-host](findings/B02-cp-api-host.md) | 0 | 1 | 3 | 0 |
| [B08-api-buildingblock](findings/B08-api-buildingblock.md) | 0 | 0 | 4 | 1 |
| [B10-worker-operations-rest](findings/B10-worker-operations-rest.md) | 0 | 0 | 1 | 1 |
| [B12-worker-host](findings/B12-worker-host.md) | 0 | 1 | 0 | 0 |
| [B13-conversion-quarantine](findings/B13-conversion-quarantine.md) | 0 | 0 | 1 | 0 |
| [B16-contract-drift](findings/B16-contract-drift.md) | 0 | 2 | 0 | 0 |
| [C06-buildingblocks-core](findings/C06-buildingblocks-core.md) | 0 | 1 | 1 | 1 |
| [D03-migrations-late](findings/D03-migrations-late.md) | 0 | 3 | 2 | 1 |
| [D04-db-roles](findings/D04-db-roles.md) | 1 | 1 | 2 | 1 |
| [D06-role-fingerprint](findings/D06-role-fingerprint.md) | 1 | 3 | 1 | 1 |
| [D09-cp-postgres-reads-proof](findings/D09-cp-postgres-reads-proof.md) | 0 | 0 | 1 | 0 |
| [D12-runtimecontrol-postgres](findings/D12-runtimecontrol-postgres.md) | 0 | 2 | 0 | 0 |
| [E01-mql5-lexer](findings/E01-mql5-lexer.md) | 0 | 4 | 0 | 1 |
| [E02-mql5-parser](findings/E02-mql5-parser.md) | 0 | 2 | 2 | 0 |
| [E03-mql5-binder](findings/E03-mql5-binder.md) | 0 | 3 | 4 | 0 |
| [E05-mql5-builtins](findings/E05-mql5-builtins.md) | 0 | 1 | 0 | 1 |
| [E07-mql5-evidence](findings/E07-mql5-evidence.md) | 0 | 1 | 2 | 1 |
| [E08-mql5-inventory-dossier](findings/E08-mql5-inventory-dossier.md) | 1 | 4 | 2 | 0 |
| [E11-mql5-equivalence-attest](findings/E11-mql5-equivalence-attest.md) | 0 | 2 | 2 | 1 |
| [E12-mql5-source-safety](findings/E12-mql5-source-safety.md) | 0 | 2 | 3 | 2 |
| [E14-mod-authz-identity](findings/E14-mod-authz-identity.md) | 0 | 1 | 1 | 1 |
| [E16-mod-commands-outbox](findings/E16-mod-commands-outbox.md) | 1 | 3 | 1 | 0 |
| [E18-strategy-version](findings/E18-strategy-version.md) | 0 | 0 | 2 | 1 |
| [F01-codegen-core](findings/F01-codegen-core.md) | 0 | 5 | 1 | 0 |
| [F02-codegen-expressions](findings/F02-codegen-expressions.md) | 0 | 4 | 0 | 1 |
| [F03-codegen-declarations](findings/F03-codegen-declarations.md) | 0 | 5 | 1 | 0 |
| [F04-codegen-statements](findings/F04-codegen-statements.md) | 0 | 2 | 0 | 0 |
| [F05-codegen-calls](findings/F05-codegen-calls.md) | 0 | 3 | 0 | 1 |
| [F06-codegen-types](findings/F06-codegen-types.md) | 0 | 4 | 1 | 1 |
| [F07-codegen-writer](findings/F07-codegen-writer.md) | 0 | 1 | 2 | 1 |
| [F08-codegen-assembly](findings/F08-codegen-assembly.md) | 0 | 0 | 2 | 1 |
| [F09-roslyn-host](findings/F09-roslyn-host.md) | 1 | 1 | 2 | 1 |
| [F10-rt-math-conversion](findings/F10-rt-math-conversion.md) | 0 | 3 | 3 | 1 |
| [F11-rt-array-text](findings/F11-rt-array-text.md) | 2 | 3 | 3 | 0 |
| [F12-rt-datetime](findings/F12-rt-datetime.md) | 0 | 2 | 0 | 0 |
| [F13-rt-trade](findings/F13-rt-trade.md) | 0 | 1 | 2 | 1 |
| [F14-rt-marketdata-symbol](findings/F14-rt-marketdata-symbol.md) | 1 | 2 | 0 | 1 |
| [F15-rt-indicator-terminal](findings/F15-rt-indicator-terminal.md) | 0 | 3 | 3 | 0 |
| [F16-rt-core-globals](findings/F16-rt-core-globals.md) | 0 | 1 | 0 | 0 |
| [F17-rt-constants-errors](findings/F17-rt-constants-errors.md) | 0 | 2 | 1 | 1 |
| [F18-rt-stdlib-trade](findings/F18-rt-stdlib-trade.md) | 1 | 3 | 2 | 0 |
| [F19-rt-stdlib-info](findings/F19-rt-stdlib-info.md) | 0 | 1 | 0 | 0 |
| [F20-engine-broker-sim](findings/F20-engine-broker-sim.md) | 3 | 4 | 1 | 2 |
| [F21-engine-trade-types](findings/F21-engine-trade-types.md) | 0 | 1 | 1 | 2 |
| [F22-engine-ind-a-f](findings/F22-engine-ind-a-f.md) | 0 | 1 | 0 | 0 |
| [F24-engine-ind-infra](findings/F24-engine-ind-infra.md) | 0 | 2 | 0 | 0 |
| [F25-engine-feed](findings/F25-engine-feed.md) | 0 | 3 | 2 | 0 |
| [F26-engine-hosting-context](findings/F26-engine-hosting-context.md) | 0 | 1 | 1 | 1 |
| [F27-backtest-live-runner](findings/F27-backtest-live-runner.md) | 3 | 4 | 3 | 1 |
| [G04-trading-abstractions](findings/G04-trading-abstractions.md) | 0 | 5 | 3 | 2 |
| [G07-strategy-host-supervisor](findings/G07-strategy-host-supervisor.md) | 0 | 2 | 2 | 1 |
| [G08-connection-probe](findings/G08-connection-probe.md) | 0 | 1 | 2 | 0 |
| [G11-vendor-binaries](findings/G11-vendor-binaries.md) | 0 | 0 | 2 | 0 |
| [H01-tool-backtest-runner](findings/H01-tool-backtest-runner.md) | 0 | 2 | 3 | 2 |
| [H03-tool-marketdata](findings/H03-tool-marketdata.md) | 0 | 0 | 2 | 1 |
| [H04-tool-mt5-inspect](findings/H04-tool-mt5-inspect.md) | 1 | 1 | 1 | 1 |
| [H05-tool-discovery-bots](findings/H05-tool-discovery-bots.md) | 0 | 3 | 2 | 0 |
| [I04-sweep-exceptions](findings/I04-sweep-exceptions.md) | 0 | 2 | 2 | 0 |
| [I05-sweep-async](findings/I05-sweep-async.md) | 1 | 1 | 1 | 1 |
| [I06-sweep-disposal](findings/I06-sweep-disposal.md) | 0 | 0 | 3 | 2 |
| [I07-sweep-injection](findings/I07-sweep-injection.md) | 0 | 1 | 2 | 0 |
| [I10-sweep-scripts](findings/I10-sweep-scripts.md) | 0 | 1 | 3 | 3 |
| [I12-sweep-datetime](findings/I12-sweep-datetime.md) | 0 | 6 | 1 | 0 |
| [I13-sweep-money-precision](findings/I13-sweep-money-precision.md) | 3 | 5 | 4 | 2 |
| [I15-sweep-nullability](findings/I15-sweep-nullability.md) | 0 | 3 | 1 | 0 |
| [J01-tests-postgres](findings/J01-tests-postgres.md) | 0 | 2 | 3 | 0 |
| [J02-tests-mql5](findings/J02-tests-mql5.md) | 0 | 0 | 2 | 1 |
| [J04-tests-api-infra](findings/J04-tests-api-infra.md) | 0 | 0 | 4 | 0 |
| [J06-docs-backend-drift](findings/J06-docs-backend-drift.md) | 0 | 1 | 1 | 0 |
| [J07-docs-arch-drift](findings/J07-docs-arch-drift.md) | 0 | 3 | 4 | 1 |
| [K01-corpus-survey-1](findings/K01-corpus-survey-1.md) | 0 | 4 | 4 | 1 |
| [K02-corpus-survey-2](findings/K02-corpus-survey-2.md) | 0 | 4 | 5 | 2 |
| [K03-corpus-survey-3](findings/K03-corpus-survey-3.md) | 0 | 8 | 6 | 4 |
| [K04-corpus-survey-4](findings/K04-corpus-survey-4.md) | 0 | 3 | 3 | 0 |
| [K05-corpus-survey-5](findings/K05-corpus-survey-5.md) | 0 | 5 | 4 | 3 |
| [K06-corpus-feature-gap](findings/K06-corpus-feature-gap.md) | 0 | 1 | 0 | 0 |
| [K07-corpus-manifest](findings/K07-corpus-manifest.md) | 0 | 0 | 0 | 1 |
| [K08-corpus-hygiene](findings/K08-corpus-hygiene.md) | 3 | 3 | 2 | 2 |
| [L01-research-mql5-spec](findings/L01-research-mql5-spec.md) | 0 | 2 | 3 | 0 |
| [L02-research-mt5-trading](findings/L02-research-mt5-trading.md) | 0 | 5 | 2 | 0 |
| [L03-research-indicators](findings/L03-research-indicators.md) | 0 | 2 | 1 | 0 |
| [L05-research-frontend-cve](findings/L05-research-frontend-cve.md) | 0 | 0 | 3 | 0 |

## Lanes reporting no findings

These areas were audited and reported clean. That is a result, not a gap —
but a clean report on a high-risk area is worth spot-checking.

- A01-api-client
- A03-api-url-problem
- A06-data-fetching
- A07-runtime-config
- A08-auth-frontend
- A14-broker-registration
- A16-compiler-ui
- A17-journal-cloud-settings
- B03-cp-api-credentials
- B04-admin-bff
- B05-secret-ingestion-api
- B06-emergency-safety-api
- B07-dev-identity
- B09-worker-operations-store
- B11-worker-outbox
- B14-conversion-corpus-store
- B15-desktop-app
- C01-admin-application
- C02-controlplane-application
- C03-runtime-application-evidence
- C04-runtime-application-rest
- C05-trading-application
- D01-migrations-early
- D02-migrations-mid
- D05-persistence-core
- D07-frontend-projections
- D08-cp-postgres-mutations
- D10-trading-postgres
- D11-runtime-postgres
- D13-admin-postgres
- E04-mql5-lowering-ir
- E06-mql5-compile-orchestrator
- E09-mql5-frontend-semantic
- E10-mql5-restricted-subset
- E13-mod-risk-policy
- E15-mod-governance-ops
- E17-mod-runtime-secrets
- F23-engine-ind-m-w
- G01-trading-mt5
- G02-process-isolation-server
- G03-process-isolation-launch
- G05-runtime-contracts
- G06-gateway-host
- G09-mt5-workerhost
- G10-dpapi-vault
- H02-tool-credentials
- I01-sweep-secrets
- I02-sweep-authz
- I03-sweep-logging-pii
- I08-sweep-config
- I09-sweep-build-deps
- I11-sweep-concurrency
- I14-sweep-idempotency
- I16-sweep-resource-limits
- J03-tests-trading-runtime
- J05-tests-postgres-modules
- L04-research-dotnet-cve
- L06-research-postgres
- V01-orchestrator-verification

## Lanes not yet reported

_All lanes reported._
