---
agent_id: I03
lane: sweep-logging-pii
scope:
  - whole tree - CROSS-CUTTING sweep. File findings ONLY about logging and information disclosure.
status: COMPLETE
generated: 2026-08-29T11:35:00Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# I03 — sweep-logging-pii

## Scope audited
Reviewed the full codebase across backend services, background workers, security boundary tools, runtime engines, and frontend clients for logging, console output, exception messages, and HTTP disclosure:
- `src/BuildingBlocks/YO4X.Api/ApiFoundation.cs` (207 lines)
- `src/BuildingBlocks/YO4X.Api/ApiProblem.cs` (41 lines)
- `src/BuildingBlocks/YO4X.Api/CorrelationIdMiddleware.cs` (27 lines)
- `src/BuildingBlocks/YO4X.Api/HttpsOnlyMiddleware.cs` (29 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRuntimeConnectionPolicy.cs` (62 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresDatabase.cs` (185 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresCredentialIngestionGrantStore.cs` (442 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresAuditOutboxWriter.cs` (217 lines)
- `src/Infrastructure/YO4X.LocalSecrets.Windows/LocalMt5Credential.cs` (262 lines)
- `src/Infrastructure/YO4X.LocalSecrets.Windows/LocalCredentialImportEvidence.cs` (202 lines)
- `src/Tools/YO4X.LocalCredentialWriter/Program.cs` (248 lines)
- `src/Tools/YO4X.LocalCredentialImporter/Program.cs` (199 lines)
- `src/Tools/YO4X.Mt5.AccountInspector/Program.cs` (185 lines)
- `src/Tools/YO4X.Mt5.AccountInspector/ConnectivitySweep.cs` (472 lines)
- `src/Tools/YO4X.Mt5.DemoExecutionTest/Program.cs` (149 lines)
- `src/Tools/YO4X.LiveBots/Program.cs` (294 lines)
- `src/Tools/YO4X.Mt5.SymbolImport/Program.cs` (153 lines)
- `src/Tools/YO4X.MarketData.Mt5History/Program.cs` (335 lines)
- `src/Tools/YO4X.Mt5.EndpointDiscovery/Program.cs` (262 lines)
- `src/Tools/YO4X.Mt5.BrokerCatalogueImport/Program.cs` (650 lines)
- `src/Tools/YO4X.StrategyInputProjection/StrategyInputProjectionCommand.cs` (549 lines)
- `src/Apps/YO4X.ControlPlane.Api/Program.cs` (617 lines)
- `src/Apps/YO4X.ControlPlane.Api/DevelopmentMt5ConnectionProbe.cs` (475 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Operations/ControlWorkBackgroundService.cs` (267 lines)
- `src/Apps/YO4X.ControlPlane.Workers/Outbox/OutboxDispatcherBackgroundService.cs` (78 lines)
- `src/Apps/YO4X.DevelopmentIdentity/Controllers/AccountController.cs` (161 lines)
- `src/Apps/YO4X.DevelopmentIdentity/LocalIdentityProvisioner.cs` (93 lines)
- `src/Apps/YO4X.DevelopmentIdentity/AuthenticatedAccountFormRecoveryMiddleware.cs` (23 lines)
- `src/Apps/YO4X.SecretIngestion.Api/Program.cs` (99 lines)
- `src/Apps/YO4X.EmergencySafety.Api/EmergencyRoutes.cs` (356 lines)
- `src/Apps/YO4X.Admin.Bff/AdminRoutes.cs` (505 lines)
- `src/Apps/YO4X.Conversion.Worker/ConversionInventoryCommand.cs` (404 lines)
- `src/Apps/YO4X.Conversion.Worker/Mql5QuarantineIntakeCommand.cs` (111 lines)
- `src/Apps/YO4X.Desktop/MainWindow.xaml.cs` (279 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Runtime.cs` (133 lines)
- `src/Runtime/YO4X.Mql5.Runtime/Mql5Log.cs` (107 lines)
- `src/Runtime/YO4X.Mql5.Live/LiveStrategyRunner.cs` (262 lines)
- `src/Runtime/YO4X.Mql5.Live/LiveBrokerContext.cs` (517 lines)
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiDemoTradeClient.cs` (774 lines)
- `src/Runtime/YO4X.GatewayHost/BrokerCommandOneShotWorker.cs` (233 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SourceSecretScanner.cs` (226 lines)
- `src/Frontend/YO4X.Web/src/features/broker-accounts/brokerRegistration.ts` (102 lines)
- `src/Frontend/YO4X.Web/src/api/controlPlaneClient.ts` (770 lines)

## Verdict
The logging and information disclosure controls across the repository are exceptionally sound and rigorously implemented. Broker passwords, database credentials, cryptographic material, and session tokens are strictly bounded in memory, zeroed with `CryptographicOperations.ZeroMemory` upon completion or disposal, and explicitly redacted in `ToString()` implementations. Structured logging never destructures credential-bearing objects, Npgsql connection policies enforce `IncludeErrorDetail = false` and `LogParameters = false`, background service loggers record only non-sensitive exception type names, HTTP exception handlers sanitize problem details without leaking stack traces or SQL text, and per-tick strategy logging is strictly bounded.

## Findings
None. The cross-cutting audit found that information disclosure boundaries are upheld across all layers:
- **Credential Protection in Logging and Errors**: `LocalMt5Credential`, `LocalMt5CredentialDescriptor`, `WriteOptions`, and `DevelopmentMt5ConnectionProbeOptions` override `ToString()` to emit `[REDACTED]` or masked logins (`*12`). In bounded helper tools (`LocalCredentialWriter`, `LocalCredentialImporter`, `BrokerCatalogueImport`, `EndpointDiscovery`), catch blocks intentionally discard exception messages to prevent leaking dynamic values and instead emit fixed fail-closed diagnostic codes to `Console.Error`.
- **Structured Logging & Background Workers**: Background services (`ControlWorkBackgroundService`, `OutboxDispatcherBackgroundService`) use compile-time source-generated `[LoggerMessage]` methods logging only `exception.GetType().Name` rather than message contents or stack traces. No Serilog structured object destructuring (`{@...}`) is used.
- **HTTP Exception and Problem Handling**: `Yo4xExceptionHandler` in `YO4X.Api` logs unhandled exceptions strictly on 500 errors while avoiding request body logging. HTTP error responses sanitize internal exceptions into kebab-case problem codes, remove tracing identifiers (`traceId`), and return generic messages for unhandled 500 errors.
- **Database Connection Security**: `PostgresRuntimeConnectionPolicy` and connection initializers strictly enforce `IncludeErrorDetail = false` and `LogParameters = false`, preventing SQL query text or parameter values from reaching logs or error responses.
- **Tight-Loop / Per-Tick Bounding**: In `YO4X.Mql5.Runtime`, MQL5 log output (`Print`, `Comment`, `Alert`) is directed through `IMql5LogSink` (defaulting to `NullMql5LogSink`). `Mql5LogRecorder` enforces a hard capacity cap (default 4096 entries) with automatic dequeue of oldest entries to prevent storage flooding or heap exhaustion.

## Referrals
None.

## Coverage gaps
None.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 354.8s | 444506 tok | id=62f8c796-1f72-4c2a-b0d4-817e96020a29
