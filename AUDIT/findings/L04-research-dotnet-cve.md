---
agent_id: L04
lane: Package Advisories & Dependency Misuse
scope:
  - Directory.Packages.props
  - Directory.Build.props
  - global.json
  - src/**/*.csproj
  - tests/**/*.csproj
status: COMPLETE
generated: 2026-08-29T11:42:00Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# L04 — Package Advisories & Dependency Misuse

## Scope audited

The audit reviewed central dependency manifests, SDK pinning files, and all project files across the solution:

- [global.json](file:///C:/Users/Dev23/Desktop/yo4x/global.json) (8 lines) — SDK version `10.0.400`, `rollForward: latestPatch`, `allowPrerelease: false`.
- [Directory.Packages.props](file:///C:/Users/Dev23/Desktop/yo4x/Directory.Packages.props) (24 lines) — Central package management with transitive pinning enabled and 15 pinned package versions.
- [Directory.Build.props](file:///C:/Users/Dev23/Desktop/yo4x/Directory.Build.props) (13 lines) — Target framework `net10.0`, `TreatWarningsAsErrors: true`, `Deterministic: true`, `AnalysisLevel: latest-recommended`.
- 83 project files (`*.csproj`) across [src/Application/](file:///C:/Users/Dev23/Desktop/yo4x/src/Application), [src/Apps/](file:///C:/Users/Dev23/Desktop/yo4x/src/Apps), [src/BuildingBlocks/](file:///C:/Users/Dev23/Desktop/yo4x/src/BuildingBlocks), [src/Infrastructure/](file:///C:/Users/Dev23/Desktop/yo4x/src/Infrastructure), [src/Modules/](file:///C:/Users/Dev23/Desktop/yo4x/src/Modules), [src/Runtime/](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime), [src/Tools/](file:///C:/Users/Dev23/Desktop/yo4x/src/Tools), and [tests/](file:///C:/Users/Dev23/Desktop/yo4x/tests).

In addition, the implementation and runtime configuration of these libraries were audited for documented misuse patterns across:
- [src/Runtime/YO4X.Mql5.Compilation/RoslynMql5CompilationHost.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Compilation/RoslynMql5CompilationHost.cs) (179 lines) — Roslyn compilation sandbox and metadata reference confinement.
- [src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresDatabase.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresDatabase.cs) (185 lines) and [PostgresRuntimeConnectionPolicy.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRuntimeConnectionPolicy.cs) (62 lines) — Npgsql connection string validation and transport policy.
- [src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresTenantContextCapabilityProvider.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresTenantContextCapabilityProvider.cs) (279 lines) — Parameterized PostgreSQL execution and role separation.
- [src/BuildingBlocks/YO4X.Api/AuthenticationExtensions.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/BuildingBlocks/YO4X.Api/AuthenticationExtensions.cs) (230 lines) — JWT Bearer and Cookie authentication validation parameters.
- [src/Apps/YO4X.DevelopmentIdentity/DevelopmentIdentityRegistration.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Apps/YO4X.DevelopmentIdentity/DevelopmentIdentityRegistration.cs) (117 lines) and [DevelopmentIdentityStartupGuard.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Apps/YO4X.DevelopmentIdentity/DevelopmentIdentityStartupGuard.cs) (31 lines) — OpenIddict and ASP.NET Identity configuration.
- [src/Apps/YO4X.Desktop/MainWindow.xaml.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Apps/YO4X.Desktop/MainWindow.xaml.cs) (279 lines) and [DesktopNavigationPolicy.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Apps/YO4X.Desktop/DesktopNavigationPolicy.cs) (31 lines) — WebView2 security flags, download handling, origin filtering, and certificate verification.
- [src/BuildingBlocks/YO4X.BuildingBlocks/CanonicalJson.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/BuildingBlocks/YO4X.BuildingBlocks/CanonicalJson.cs) (64 lines) — `System.Text.Json` usage and deterministic hashing.

## Verdict

The dependency supply chain and library configuration across the repository are sound. Package versions are strictly managed centrally via `Directory.Packages.props` with `ManagePackageVersionsCentrally` and `CentralPackageTransitivePinningEnabled` enabled; no project file introduces unpinned or divergent package versions. Comprehensive advisory searches across Npgsql, Roslyn / `Microsoft.CodeAnalysis.CSharp`, ASP.NET Core, OpenIddict, WebView2, and test infrastructure confirmed zero applicable CVEs against the pinned versions, and all libraries prone to dangerous-by-default configurations (such as Roslyn compiler options, Npgsql connection session parameters, OpenIddict development certificates, and WebView2 embedded browser capabilities) are defensively constrained with strict runtime startup guards and fail-closed policies.

## Findings

None.

The dependency versions and usage patterns hold up across all examined criteria:

1. **Npgsql (`10.0.3`)**: Historical vulnerabilities (such as CVE-2024-32655 integer overflow in `WriteBind` protocol messages, patched in `8.0.3`/`7.0.7`/`6.0.11`) do not apply to the pinned `10.0.3` release. In addition, [PostgresRuntimeConnectionPolicy.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/BuildingBlocks/YO4X.Persistence.Postgres/PostgresRuntimeConnectionPolicy.cs#L12-L36) enforces safe connection session settings (`!IncludeErrorDetail`, `!LogParameters`, `!TrustServerCertificate`, `SslMode=VerifyFull`, `!NoResetOnClose`, `!Multiplexing`, and strict ban on caller `Options`/`SearchPath`). All command execution uses strictly typed `NpgsqlParameter` instances (`NpgsqlDbType.Text`, `NpgsqlDbType.Uuid`, `NpgsqlDbType.Bytea`) with no dynamic string concatenation or unsafe JSON type mapping.
2. **Roslyn / `Microsoft.CodeAnalysis.CSharp` (`4.14.0`)**: No unpatched advisories affect `Microsoft.CodeAnalysis.CSharp` 4.14.0. In [RoslynMql5CompilationHost.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Runtime/YO4X.Mql5.Compilation/RoslynMql5CompilationHost.cs#L32-L42), `CSharpCompilationOptions` explicitly disables unsafe code (`allowUnsafe: false`), enforces determinism (`deterministic: true`), uses nullable reference type analysis, and strictly isolates metadata references to BCL primitives and `IMql5Runtime`, preventing untrusted strategy code from linking against arbitrary assemblies.
3. **OpenIddict (`7.6.0`) & ASP.NET Identity**: No CVEs exist for OpenIddict 7.6.0. Development-only certificate overrides (`AddDevelopmentEncryptionCertificate`, `AddDevelopmentSigningCertificate`, `DisableAccessTokenEncryption`) in [DevelopmentIdentityRegistration.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Apps/YO4X.DevelopmentIdentity/DevelopmentIdentityRegistration.cs#L99-L101) are guarded by [DevelopmentIdentityStartupGuard.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Apps/YO4X.DevelopmentIdentity/DevelopmentIdentityStartupGuard.cs#L10-L28), which unconditionally crashes the process if `!environment.IsDevelopment()`, if `LocalIdentity:Enabled` is false, or if endpoints are non-loopback.
4. **ASP.NET Core & JWT Bearer (`10.0.11`)**: [AuthenticationExtensions.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/BuildingBlocks/YO4X.Api/AuthenticationExtensions.cs#L148-L161) sets `RequireHttpsMetadata = true`, `ValidateIssuer = true`, `ValidateAudience = true`, `ValidateIssuerSigningKey = true`, `ValidateLifetime = true`, `ClockSkew = 30s`, `MapInboundClaims = false`, and `SaveToken = false`. Development authority pinning is strictly gated to development loopback with fixed-time SHA256 certificate validation.
5. **Microsoft Edge WebView2 (`1.0.4129.50`)**: In [MainWindow.xaml.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Apps/YO4X.Desktop/MainWindow.xaml.cs#L63-L84), WebView2 defaults that could introduce security weaknesses are explicitly disabled: `AreHostObjectsAllowed = false`, `IsWebMessageEnabled = false`, `AreDefaultScriptDialogsEnabled = false`, `IsGeneralAutofillEnabled = false`, and `IsPasswordAutosaveEnabled = false`. Downloads and permission requests are canceled/denied by default, and navigation is strictly constrained by [DesktopNavigationPolicy.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/Apps/YO4X.Desktop/DesktopNavigationPolicy.cs#L15-L21) to configured shell origins.
6. **Serialization & SDK Tools**: JSON serialization in [CanonicalJson.cs](file:///C:/Users/Dev23/Desktop/yo4x/src/BuildingBlocks/YO4X.BuildingBlocks/CanonicalJson.cs#L10-L20) relies on standard `System.Text.Json` (no insecure third-party binary formatters or vulnerable JSON deserializers). Testing packages (`Testcontainers.PostgreSql` 4.14.0, `xunit.v3` 3.2.2, `coverlet.collector` 6.0.4, `Microsoft.NET.Test.Sdk` 17.14.1) are up to date and restricted to test projects.

## Referrals

None.

## Coverage gaps

None.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 204.5s | 258873 tok | id=1bd7fd76-a110-4864-90e0-b6c62249e3c1
