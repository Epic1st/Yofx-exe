---
agent_id: B02
lane: cp-api-host
scope:
  - src/Apps/YO4X.ControlPlane.Api/Program.cs
  - src/Apps/YO4X.ControlPlane.Api/ControlPlanePostgresRegistration.cs
  - src/Apps/YO4X.ControlPlane.Api/RuntimeControlPostgresRegistration.cs
  - src/Apps/YO4X.ControlPlane.Api/TenantContextCapabilityRegistration.cs
  - src/Apps/YO4X.ControlPlane.Api/ControlPlaneReadinessProbe.cs
status: COMPLETE
generated: 2026-08-29T08:54:30Z
counts: { P0: 0, P1: 1, P2: 3, P3: 0 }
---

# B02 — cp-api-host

## Scope audited
- `src/Apps/YO4X.ControlPlane.Api/Program.cs` (618 lines)
- `src/Apps/YO4X.ControlPlane.Api/ControlPlanePostgresRegistration.cs` (400 lines)
- `src/Apps/YO4X.ControlPlane.Api/RuntimeControlPostgresRegistration.cs` (166 lines)
- `src/Apps/YO4X.ControlPlane.Api/TenantContextCapabilityRegistration.cs` (68 lines)
- `src/Apps/YO4X.ControlPlane.Api/ControlPlaneReadinessProbe.cs` (484 lines)

## Verdict
The host composition and DI architecture are structured cleanly with proper middleware ordering, fail-closed exception handling, strict HTTPS enforcement, and zero captive scoped dependencies. However, IPv4-mapped IPv6 loopback connections on dual-stack Kestrel cause false rejections on local broker account creation and misclassified network audits. Additionally, an inconsistency between runtime registration and the readiness probe allows the host to start up in a permanently unready state if evidence PostgreSQL connection strings are omitted.

## Findings

### [P1] `IPAddress.IsLoopback` check rejects IPv4 loopback connections on dual-stack listener during broker account linking
- **Where:** `src/Apps/YO4X.ControlPlane.Api/Program.cs:178`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
      if (context.Connection.RemoteIpAddress is null
          || !IPAddress.IsLoopback(context.Connection.RemoteIpAddress))
      {
          return ApiProblems.Create(
              context,
              StatusCodes.Status403Forbidden,
              "LOCAL_CREDENTIAL_BOUNDARY_REQUIRES_LOOPBACK",
              "A broker password can be submitted only to a control plane running on this device.");
      }
  ```
- **Failure:** When Kestrel listens on dual-stack sockets (`[::]`), local clients connecting via IPv4 (`127.0.0.1`) produce an IPv4-mapped IPv6 `RemoteIpAddress` (`::ffff:127.0.0.1`). In .NET, `IPAddress.IsLoopback` returns `false` for IPv4-mapped IPv6 addresses, causing legitimate local requests to `POST /v1/broker-accounts` to be rejected with HTTP 403 `LOCAL_CREDENTIAL_BOUNDARY_REQUIRES_LOOPBACK`.
- **Fix:** Unmap IPv4-mapped IPv6 addresses before checking loopback: `IPAddress? ip = context.Connection.RemoteIpAddress; if (ip is null || !IPAddress.IsLoopback(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip))`.

### [P2] `ClassifySourceNetwork` misclassifies IPv4-mapped loopback connections as private in request metadata
- **Where:** `src/Apps/YO4X.ControlPlane.Api/Program.cs:577`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
      if (IPAddress.IsLoopback(address))
      {
          return "loopback";
      }

      if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
      {
          if (address.IsIPv4MappedToIPv6)
          {
              address = address.MapToIPv4();
          }
  ```
- **Failure:** An IPv4 loopback connection arriving on a dual-stack socket (`::ffff:127.0.0.1`) fails the initial `IPAddress.IsLoopback` check. After being unmapped to IPv4 (`127.0.0.1`), execution falls through without re-checking loopback, hitting `bytes[0] == 127` which classifies the network as `"private"` instead of `"loopback"` in audit and request metadata.
- **Fix:** Check `IPAddress.IsLoopback` again immediately after unmapping `address.MapToIPv4()`, or map to IPv4 before the initial `IPAddress.IsLoopback` check.

### [P2] Missing `RuntimeEvidencePostgres` is tolerated during registration but permanently fails the readiness probe
- **Where:** `src/Apps/YO4X.ControlPlane.Api/RuntimeControlPostgresRegistration.cs:86`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          bool allowInsecureLoopbackForDevelopment = environment.IsDevelopment();
          if (TryReadRuntimeConnectionString(
                  configuration.GetConnectionString("RuntimeEvidencePostgres"),
                  "yo4x_runtime_evidence",
                  allowInsecureLoopbackForDevelopment,
                  out string evidenceConnectionString)
              && PostgresDatabaseEndpoint.TryParse(
                  evidenceConnectionString,
                  out PostgresDatabaseEndpoint? evidenceEndpoint)
              && TenantContextCapabilityRegistration.TryAdd(
                  services,
                  configuration,
                  evidenceEndpoint!))
          {
              services.TryAddSingleton(serviceProvider => new RuntimeEvidencePostgresDatabase(
                  evidenceConnectionString,
                  serviceProvider.GetRequiredService<ITenantContextCapabilityProvider>(),
                  allowInsecureLoopbackForDevelopment));
          }

          services.TryAddScoped<IRuntimeControlPlaneApplication, PostgresRuntimeControlPlaneApplication>();
          return services;
  ```
- **Failure:** If `ConnectionStrings:RuntimeEvidencePostgres` is omitted or invalid, `TryAddRuntimeControlPostgres` skips registering `RuntimeEvidencePostgresDatabase` but continues to register `PostgresRuntimeControlPlaneApplication`. The host starts without errors, but `ControlPlaneReadinessProbe.IsReadyAsync` requires `RuntimeEvidencePostgresDatabase` in DI, causing `/health/ready` to permanently return 503 Service Unavailable.
- **Fix:** Make `RuntimeEvidencePostgresDatabase` registration mandatory in `TryAddRuntimeControlPostgres` (returning `services` early without registering `PostgresRuntimeControlPlaneApplication` if evidence database configuration fails) so the host falls back to `UnavailableRuntimeControlPlaneApplication`.

### [P2] Untracked pre-registered `ITenantContextCapabilityProvider` bypasses endpoint validation in `TenantContextCapabilityRegistration`
- **Where:** `src/Apps/YO4X.ControlPlane.Api/TenantContextCapabilityRegistration.cs:30`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          ServiceDescriptor? existingProvider = services.FirstOrDefault(static descriptor =>
              descriptor.ServiceType == typeof(ITenantContextCapabilityProvider));
          if (existingProvider?.ImplementationInstance is ITenantContextCapabilityProvider provider)
          {
              return provider.Endpoint == requiredEndpoint;
          }

          if (existingProvider is not null)
          {
              return true;
          }
  ```
- **Failure:** If `ITenantContextCapabilityProvider` is registered via factory or type descriptor without a `TenantContextCapabilityEndpointRegistration`, `existingProvider.ImplementationInstance` is `null`. `TryAdd` returns `true` unconditionally on line 39 without verifying that the pre-registered provider's endpoint matches `requiredEndpoint`, allowing database pools targeting different endpoints to share an invalid issuer provider.
- **Fix:** Reject or require explicit endpoint verification when a pre-existing provider descriptor is present without an inspectable instance or endpoint registration.

## Referrals
- `src/Apps/YO4X.ControlPlane.Api/DevelopmentMt5ConnectionProbe.cs:84` — Uses `!IPAddress.IsLoopback(context.Connection.RemoteIpAddress)` without unmapping IPv4-mapped IPv6 addresses on dual-stack sockets.

## Coverage gaps
- `src/Apps/YO4X.ControlPlane.Api/Program.cs:178` — No integration test submits broker credentials over dual-stack IPv6 sockets (`::ffff:127.0.0.1`) to verify loopback boundary enforcement.
- `src/Apps/YO4X.ControlPlane.Api/RuntimeControlPostgresRegistration.cs:86` — No test exercises the branch where `RuntimePostgres` is configured but `RuntimeEvidencePostgres` is omitted to verify fail-closed host registration behavior.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 157.7s | 323568 tok | id=8a36548b-37f3-4a99-8ae0-43aeddc7c3fe
