---
agent_id: J04
lane: api-gateway-architecture-tests
scope:
  - tests/YO4X.Api.Tests/**
  - tests/YO4X.GatewayHost.Tests/**
  - tests/YO4X.Desktop.Tests/**
  - tests/YO4X.Architecture.Tests/**
  - tests/YO4X.DevelopmentIdentity.Tests/**
status: COMPLETE
generated: 2026-08-29T11:35:00Z
counts: { P0: 0, P1: 0, P2: 4, P3: 0 }
---

# J04 — API, GatewayHost, Desktop, Architecture & DevelopmentIdentity Tests

## Scope audited
- `tests/YO4X.Architecture.Tests/ArchitectureBoundaryTests.cs` (316 lines)
- `tests/YO4X.Architecture.Tests/StrategyTransactionBoundaryTests.cs` (163 lines)
- `tests/YO4X.Architecture.Tests/YO4X.Architecture.Tests.csproj` (23 lines)
- `tests/YO4X.Desktop.Tests/DesktopLaunchOptionsTests.cs` (135 lines)
- `tests/YO4X.Desktop.Tests/YO4X.Desktop.Tests.csproj` (25 lines)
- `tests/YO4X.DevelopmentIdentity.Tests/DevelopmentIdentityIntegrationTests.cs` (232 lines)
- `tests/YO4X.DevelopmentIdentity.Tests/DevelopmentIdentitySecurityTests.cs` (122 lines)
- `tests/YO4X.DevelopmentIdentity.Tests/YO4X.DevelopmentIdentity.Tests.csproj` (28 lines)
- `tests/YO4X.GatewayHost.Tests/BrokerCommandOneShotCompositionTests.cs` (227 lines)
- `tests/YO4X.GatewayHost.Tests/BrokerCommandOneShotWorkerTests.cs` (368 lines)
- `tests/YO4X.GatewayHost.Tests/BrokerConnectionProbeProtocolTests.cs` (324 lines)
- `tests/YO4X.GatewayHost.Tests/DedicatedConnectionProbeWorkerServerTests.cs` (174 lines)
- `tests/YO4X.GatewayHost.Tests/GatewayHostHealthEndpointTests.cs` (89 lines)
- `tests/YO4X.GatewayHost.Tests/IsolatedBrokerProcessClientTests.cs` (637 lines)
- `tests/YO4X.GatewayHost.Tests/UserOperationProtocolHostCompositionTests.cs` (199 lines)
- `tests/YO4X.GatewayHost.Tests/YO4X.GatewayHost.Tests.csproj` (40 lines)
- `tests/YO4X.Api.Tests/ApiFoundationTests.cs` (332 lines)
- `tests/YO4X.Api.Tests/BrokerAccountDiscoveryBoundaryTests.cs` (144 lines)
- `tests/YO4X.Api.Tests/BrokerAccountDiscoveryHttpTests.cs` (280 lines)
- `tests/YO4X.Api.Tests/BrokerAccountLinkCredentialTests.cs` (179 lines)
- `tests/YO4X.Api.Tests/BrokerServerApprovalHttpTests.cs` (296 lines)
- `tests/YO4X.Api.Tests/ClientCertificateFilterTests.cs` (117 lines)
- `tests/YO4X.Api.Tests/ControlPlaneBoundaryTests.cs` (1079 lines)
- `tests/YO4X.Api.Tests/DevelopmentMt5ConnectionProbeHttpTests.cs` (193 lines)
- `tests/YO4X.Api.Tests/FrontendProjectionBoundaryTests.cs` (609 lines)
- `tests/YO4X.Api.Tests/RuntimeControlRegistrationTests.cs` (289 lines)
- `tests/YO4X.Api.Tests/SecretIngestionBoundaryTests.cs` (293 lines)
- `tests/YO4X.Api.Tests/YO4X.Api.Tests.csproj` (31 lines)

## Verdict
The test suite demonstrates robust verification around process isolation, DPAPI credential parsing, protocol serialization, and anonymous route rejection. However, the architecture tests contain blind spots where dependency rules check only raw NuGet package names instead of project references and examine single hardcoded files rather than assembly directories. Furthermore, several critical control-plane HTTP boundaries rely on source code string slicing rather than live HTTP pipeline execution, leaving multi-tenant isolation and mutation failure branches untested against running endpoints.

## Findings

### [P2] Domain module architecture rule fails to detect ProjectReferences to Infrastructure projects
- **Where:** `tests/YO4X.Architecture.Tests/ArchitectureBoundaryTests.cs:16-26`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        string modulesRoot = Path.Combine(RepositoryRoot, "src", "Modules");
        string[] forbidden = ["Npgsql", "Microsoft.EntityFrameworkCore", "AspNetCore", "mt5api.dll"];

        foreach (string projectFile in Directory.EnumerateFiles(modulesRoot, "*.csproj", SearchOption.AllDirectories))
        {
            string xml = File.ReadAllText(projectFile);
            foreach (string dependency in forbidden)
            {
                Assert.DoesNotContain(dependency, xml, StringComparison.OrdinalIgnoreCase);
            }
        }
  ```
- **Failure:** A domain module in `src/Modules/` introduces a direct dependency on an infrastructure project via `<ProjectReference Include="..\..\Infrastructure\YO4X.Persistence.Postgres\YO4X.Persistence.Postgres.csproj" />`. Because the project file XML does not contain the literal strings `"Npgsql"`, `"Microsoft.EntityFrameworkCore"`, `"AspNetCore"`, or `"mt5api.dll"`, `DomainModulesDoNotReferenceInfrastructurePackages` passes green despite a direct architectural violation coupling domain modules to database infrastructure.
- **Fix:** Parse project XML using `XDocument` and assert that domain module `.csproj` files contain zero `<ProjectReference>` elements targeting `src/Infrastructure` or `src/Apps` paths.

### [P2] Read-only vendor call architecture check inspects only one hardcoded file instead of the assembly
- **Where:** `tests/YO4X.Architecture.Tests/ArchitectureBoundaryTests.cs:238-264`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        string mapperPath = Path.Combine(
            RepositoryRoot,
            "src",
            "Runtime",
            "YO4X.Trading.Mt5",
            "Mt5VendorReadOnlyMapper.cs");
        string source = File.ReadAllText(mapperPath);
        string[] forbiddenCalls =
        [
            "new MT5API",
            ".Connect(",
            ".Disconnect(",
            ".Subscribe(",
            ".Unsubscribe(",
            ".GetQuote(",
            ".GetOpenedOrders(",
            ".RequestOrderHistory(",
            ".DownloadOrderHistory(",
            ".OrderSend",
            ".OrderClose",
            ".OrderModify"
        ];

        foreach (string forbiddenCall in forbiddenCalls)
        {
            Assert.DoesNotContain(forbiddenCall, source, StringComparison.Ordinal);
        }
  ```
- **Failure:** An active trading or network connection call (such as `.OrderSend(...)` or `.Connect(...)`) is added to `src/Runtime/YO4X.Trading.Mt5/Mt5ProofOnlyGateway.cs` or `Mt5ProofOnlyBrokerWorkerExecutor.cs`. `VendorBindingContainsNoActiveNetworkHistoryOrTradeCalls` checks only `Mt5VendorReadOnlyMapper.cs`, leaving all other C# files in `YO4X.Trading.Mt5` uninspected and passing green while active vendor network calls enter the codebase.
- **Fix:** Enumerate and inspect all `.cs` files across `src/Runtime/YO4X.Trading.Mt5/` (excluding `bin` and `obj`) rather than evaluating a single file path.

### [P2] Build output exclusion assertion passes vacuously on clean test checkouts
- **Where:** `tests/YO4X.Architecture.Tests/ArchitectureBoundaryTests.cs:306-313`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        string[] matches = roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories))
            .Where(path => path.Split(Path.DirectorySeparatorChar)
                .Any(segment => string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.Empty(matches);
  ```
- **Failure:** In a clean test environment or CI execution running without pre-existing local compilation directories, `Directory.EnumerateFiles` traverses non-existent or empty `bin` subdirectories. `VendorAssemblyNeverEntersApplicationOrTestBuildOutputs` evaluates to an empty array and passes unconditionally without proving that build output rules actually isolate `mt5api.dll`.
- **Fix:** Validate MSBuild reference properties (`Private=false`, `CopyToOutputDirectory=Never`) on the project object model or inspect target build manifests instead of relying on filesystem scanning of ephemeral `bin` folders.

### [P2] Control plane boundary tests substitute source code string scraping for HTTP endpoint execution
- **Where:** `tests/YO4X.Api.Tests/ControlPlaneBoundaryTests.cs:749-758`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
        string program = ReadRepositoryFile(
            "src",
            "Apps",
            "YO4X.ControlPlane.Api",
            "Program.cs");
        string endpoint = Slice(
            program,
            "user.MapPost(\"/broker-accounts\"",
            "user.MapGet(\"/broker-accounts/{brokerAccountId:guid}\"");
  ```
- **Failure:** Route configuration in `Program.cs` is modified or endpoint filter order is altered such that the runtime HTTP pipeline fails or drops authorization. `BrokerAccountRegistrationIsAuthenticatedIdempotentAndCredentialFree`, `DeploymentOperationResultEndpointUsesTheAuthenticatedWorkloadBoundary`, and `CredentialSessionResponsesDisableCaching` pass because the raw string patterns exist in source text, while the running HTTP endpoints remain untested.
- **Fix:** Replace static source text slicing with integration tests using `TestServer` or `WebApplicationFactory<Program>` that send real HTTP requests and verify HTTP status codes, security headers, and endpoint filter execution.

## Referrals
- `src/Apps/YO4X.ControlPlane.Api/Program.cs:178-187` — Remote IP loopback validation folds IPv4-mapped IPv6 addresses but does not account for reverse proxy headers when deployed behind an edge gateway.
- `src/Runtime/YO4X.Trading.Mt5/Mt5ProofOnlyGateway.cs:1-110` — Verify that proof-only gateway throws fail-closed exceptions on mutation operations without touching vendor memory.

## Coverage gaps
- `tests/YO4X.Api.Tests/FrontendProjectionBoundaryTests.cs`: All 21 projection routes test only a single tenant identifier (`11111111-0000-0000-0000-000000000001`) and anonymous rejection; none test cross-tenant isolation (e.g. an authenticated user from Tenant A attempting `GET /v1/bots/{tenantB_botId}` or `PUT /v1/bots/{tenantB_botId}/settings`), leaving tenant crossover vulnerabilities undetected.
- `tests/YO4X.Api.Tests/ControlPlaneBoundaryTests.cs`: `POST /v1/broker-accounts` in `Program.cs:164-220` is never exercised over HTTP; the non-loopback IP rejection branch (`LOCAL_CREDENTIAL_BOUNDARY_REQUIRES_LOOPBACK`), missing `Idempotency-Key` precondition branch (428 Precondition Required), and credential cleanup filter on failure branches are entirely unexecuted.
- `tests/YO4X.DevelopmentIdentity.Tests/DevelopmentIdentityIntegrationTests.cs`: OpenIddict token issuance (`POST /connect/token`) with invalid/expired authorization codes, mismatched PKCE code verifiers, or untrusted clients is not tested over HTTP.
- `tests/YO4X.Api.Tests/ControlPlaneBoundaryTests.cs`: `POST /internal/v1/deployments/{deploymentId}/operation-results` lacks HTTP test coverage for missing client certificates, untrusted certificate SHA-256 fingerprints, expired workload claims, or mismatched deployment IDs.
- `tests/YO4X.Api.Tests/BrokerServerApprovalHttpTests.cs`: `POST /v1/broker-server-approvals` lacks HTTP test coverage for invalid directory server IDs (`Guid.Empty`), cross-tenant approval requests, and database concurrency conflict handling.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 242.3s | 340534 tok | id=46150c7a-c213-47c8-a4bf-9b8ada7db0e0
