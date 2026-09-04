---
agent_id: I01
lane: sweep-secrets
scope:
  - src/Apps/YO4X.ControlPlane.Api/LocalBrokerCredentialVault.cs
  - src/Apps/YO4X.ControlPlane.Api/DevelopmentMt5ConnectionProbe.cs
  - src/Apps/YO4X.SecretIngestion.Api/SecretBodyReader.cs
  - src/Apps/YO4X.SecretIngestion.Api/Program.cs
  - src/Apps/YO4X.DevelopmentIdentity/DevelopmentIdentityRegistration.cs
  - src/Apps/YO4X.DevelopmentIdentity/DevelopmentIdentityStartupGuard.cs
  - src/BuildingBlocks/YO4X.Api/AuthenticationExtensions.cs
  - src/BuildingBlocks/YO4X.Api/ApiFoundation.cs
  - src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRuntimeConnectionPolicy.cs
  - src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresDatabase.cs
  - src/BuildingBlocks/YO4X.Persistence.Postgres/Security/least_privilege_roles.sql
  - src/Infrastructure/YO4X.LocalSecrets.Windows/DpapiLocalMt5CredentialVault.cs
  - src/Infrastructure/YO4X.LocalSecrets.Windows/LocalMt5Credential.cs
  - src/Infrastructure/YO4X.LocalSecrets.Windows/Mt5CredentialFileParser.cs
  - src/Modules/SecretCoordination/YO4X.SecretCoordination/CredentialIngestion.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SourceSecretScanner.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5StaticInventoryAnalyzer.cs
  - src/Modules/Support/YO4X.Support/SupportCase.cs
  - src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/VaultBackedBrokerConnectionProbeExecutor.cs
  - src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiConnectionOnlyTransport.cs
  - src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiDemoTradeClient.cs
  - src/Runtime/YO4X.Trading.ProcessIsolation/BrokerProcessClient.cs
  - src/Runtime/YO4X.Trading.ProcessIsolation/BrokerProcessProtocol.cs
  - src/Tools/YO4X.DevelopmentBootstrap/Program.cs
  - src/Tools/YO4X.LocalCredentialWriter/Program.cs
  - src/Tools/YO4X.LocalCredentialImporter/Program.cs
  - src/Tools/YO4X.Mt5.DemoCanary/Program.cs
  - src/Tools/YO4X.Mt5.AccountInspector/Program.cs
  - src/Tools/YO4X.Mt5.DemoExecutionTest/Program.cs
  - src/Tools/YO4X.MarketData.Mt5History/Program.cs
  - src/Tools/YO4X.LiveBots/Program.cs
  - src/Frontend/YO4X.Web/src/features/broker-accounts/brokerRegistration.ts
  - scripts/Start-YO4XDevelopment.ps1
  - scripts/Test-BrokerAccountLink.ps1
  - .env.example
  - .gitignore
  - compose.yaml
status: COMPLETE
generated: 2026-08-29T08:33:00Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# I01 — sweep-secrets

## Scope audited

- `src/Apps/YO4X.ControlPlane.Api/LocalBrokerCredentialVault.cs` (486 lines)
- `src/Apps/YO4X.ControlPlane.Api/DevelopmentMt5ConnectionProbe.cs` (475 lines)
- `src/Apps/YO4X.SecretIngestion.Api/SecretBodyReader.cs` (74 lines)
- `src/Apps/YO4X.SecretIngestion.Api/Program.cs` (99 lines)
- `src/Apps/YO4X.DevelopmentIdentity/DevelopmentIdentityRegistration.cs` (117 lines)
- `src/Apps/YO4X.DevelopmentIdentity/DevelopmentIdentityStartupGuard.cs` (31 lines)
- `src/BuildingBlocks/YO4X.Api/AuthenticationExtensions.cs` (230 lines)
- `src/BuildingBlocks/YO4X.Api/ApiFoundation.cs` (207 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRuntimeConnectionPolicy.cs` (62 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresDatabase.cs` (185 lines)
- `src/BuildingBlocks/YO4X.Persistence.Postgres/Security/least_privilege_roles.sql` (547 lines)
- `src/Infrastructure/YO4X.LocalSecrets.Windows/DpapiLocalMt5CredentialVault.cs` (1308 lines)
- `src/Infrastructure/YO4X.LocalSecrets.Windows/LocalMt5Credential.cs` (262 lines)
- `src/Infrastructure/YO4X.LocalSecrets.Windows/Mt5CredentialFileParser.cs` (465 lines)
- `src/Modules/SecretCoordination/YO4X.SecretCoordination/CredentialIngestion.cs` (602 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5SourceSecretScanner.cs` (226 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5StaticInventoryAnalyzer.cs` (1069 lines)
- `src/Modules/Support/YO4X.Support/SupportCase.cs` (76 lines)
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/VaultBackedBrokerConnectionProbeExecutor.cs` (196 lines)
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiConnectionOnlyTransport.cs` (361 lines)
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiDemoTradeClient.cs` (774 lines)
- `src/Runtime/YO4X.Trading.ProcessIsolation/BrokerProcessClient.cs` (423 lines)
- `src/Runtime/YO4X.Trading.ProcessIsolation/BrokerProcessProtocol.cs` (302 lines)
- `src/Tools/YO4X.DevelopmentBootstrap/Program.cs` (335 lines)
- `src/Tools/YO4X.LocalCredentialWriter/Program.cs` (248 lines)
- `src/Tools/YO4X.LocalCredentialImporter/Program.cs` (199 lines)
- `src/Tools/YO4X.Mt5.DemoCanary/Program.cs` (41 lines)
- `src/Tools/YO4X.Mt5.AccountInspector/Program.cs` (185 lines)
- `src/Tools/YO4X.Mt5.DemoExecutionTest/Program.cs` (149 lines)
- `src/Tools/YO4X.MarketData.Mt5History/Program.cs` (335 lines)
- `src/Tools/YO4X.LiveBots/Program.cs` (294 lines)
- `src/Frontend/YO4X.Web/src/features/broker-accounts/brokerRegistration.ts` (102 lines)
- `scripts/Start-YO4XDevelopment.ps1` (483 lines)
- `scripts/Test-BrokerAccountLink.ps1` (156 lines)
- `.env.example` (6 lines)
- `.gitignore` (55 lines)
- `compose.yaml` (26 lines)
- Configuration files (`appsettings*.json` across 9 host projects: 18 files, 804 total lines)
- `launchSettings.json` files across 9 projects (180 total lines)
- MQL5 corpus under `Testing/Mq5` (213 files scanned for embedded secrets, licence keys, and tokens)

## Verdict

The secret-handling architecture across YO4X is clean, sound, and strictly adheres to defense-in-depth principles across backend, frontend, tools, scripts, and MQL5 governance. Plaintext credentials (broker passwords, database passwords, tokens) are never persisted in source control, configuration files, or logs; in-memory byte buffers are zeroed using `CryptographicOperations.ZeroMemory`; passwords are never passed via command-line arguments; and MQL5 strategy intake enforces automated high-confidence secret scanning. Obvious test fixtures in unit tests (e.g. `Password=test-only`) and placeholders in vendor EAs (e.g. `"YOUR OPENAI HERE"`, `""`) are properly isolated and do not represent real secret leaks.

## Findings

None. The codebase enforces fail-closed secret hygiene across every architectural boundary:
1. **At-rest encryption:** Local broker passwords are encrypted using Windows DPAPI (`DataProtectionScope.CurrentUser`) with SHA-256 domain-separated entropy (`DpapiLocalMt5CredentialVault.cs:770-776`), while cloud passwords route through dedicated ingestion envelopes (`SecretMaterial.cs:352-381`).
2. **In-memory protection:** Sensitive byte arrays are zeroed in `finally` blocks using `CryptographicOperations.ZeroMemory` across `Utf8Secret`, `LocalMt5Credential`, `SecretBodyReader`, `Mt5CredentialFileParser`, `BrokerProcessProtocol`, and `BrokerProcessClient`.
3. **No command-line exposure:** The control plane delegates DPAPI writes to `LocalCredentialWriter` strictly over standard input with SHA-256 integrity verification (`ReadBoundedStandardInputAsync`), never passing passwords in process arguments (`LocalBrokerCredentialVault.cs:356-364`).
4. **Log and serialization suppression:** All credential models override `ToString()` with `[REDACTED]` (e.g. `Utf8Secret.cs:73`, `LocalMt5Credential.cs:157`, `SecretMaterial.cs:380`, `ParsedMt5CredentialFile.cs:454`), and custom converters throw `NotSupportedException` on serialization attempts (`Utf8SecretJsonConverter.cs:118-120`). Exception handlers log no request bodies, and PostgreSQL connection builders disable `IncludeErrorDetail` and `LogParameters` (`PostgresRuntimeConnectionPolicy.cs:18-24`).
5. **Configuration and repository exclusion:** Committed `appsettings.json` and `launchSettings.json` files contain no credentials; `compose.yaml` requires mandatory environment variables (`YO4X_POSTGRES_PASSWORD:?Set...`); and `.gitignore` comprehensively excludes `.local/`, `.env`, `.env.*`, `secrets/`, `artifacts/verification/credentials/`, `*.yo4xcred*`, `*.pfx`, `*.key`, and certificate bundles.
6. **MQL5 corpus hygiene:** All MQL5 vendor samples in `Testing/Mq5` use empty string defaults or explicit placeholders, and the intake pipeline rejects files with high-confidence keys or tokens via `Mql5SourceSecretScanner.cs`.

## Referrals

None.

## Coverage gaps

None.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 358.4s | 869817 tok | id=e03f0755-c86f-40cc-9414-fa481904a341
