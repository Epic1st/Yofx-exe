---
agent_id: B06
lane: emergency-safety-api
scope:
  - src/Apps/YO4X.EmergencySafety.Api/EmergencyHttpSecurity.cs
  - src/Apps/YO4X.EmergencySafety.Api/EmergencyRoutes.cs
  - src/Apps/YO4X.EmergencySafety.Api/Program.cs
  - src/Apps/YO4X.EmergencySafety.Api/Properties/launchSettings.json
  - src/Apps/YO4X.EmergencySafety.Api/YO4X.EmergencySafety.Api.csproj
  - src/Apps/YO4X.EmergencySafety.Api/appsettings.Development.json
  - src/Apps/YO4X.EmergencySafety.Api/appsettings.json
status: COMPLETE
generated: 2026-08-29T08:55:00Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# B06 — emergency-safety-api

## Scope audited

- `src/Apps/YO4X.EmergencySafety.Api/EmergencyHttpSecurity.cs` (59 lines) — HTTPS-only enforcement on emergency route prefixes and RFC 7807 problem status code mapping.
- `src/Apps/YO4X.EmergencySafety.Api/EmergencyRoutes.cs` (356 lines) — emergency route group configuration, preview and submission handlers, command status/targets endpoints, strict scope/reason/digest normalization, and `AdminActor` claim transformation.
- `src/Apps/YO4X.EmergencySafety.Api/Program.cs` (59 lines) — application bootstrap, Kestrel payload bounds (64 KiB), client certificate TLS mode, dedicated `AuthenticationSchemes.Emergency` registration, `emergency-restrictive` authorization policy, fail-closed fallback registration, and readiness health probes.
- `src/Apps/YO4X.EmergencySafety.Api/Properties/launchSettings.json` (24 lines) — independent port bindings (HTTPS 7278 / HTTP 5085) and development environment profile.
- `src/Apps/YO4X.EmergencySafety.Api/YO4X.EmergencySafety.Api.csproj` (15 lines) — project dependencies and compilation properties.
- `src/Apps/YO4X.EmergencySafety.Api/appsettings.Development.json` (15 lines) — development authority and audience configuration.
- `src/Apps/YO4X.EmergencySafety.Api/appsettings.json` (19 lines) — production configuration for error type base and emergency authentication authority/audience.

## Verdict

The `YO4X.EmergencySafety.Api` kill switch subsystem is sound, robust, and correctly engineered to operate under severe crisis conditions. It is hosted as an independent microservice isolated from the primary control plane, with dedicated port bindings, its own authentication scheme (`AuthenticationSchemes.Emergency`), independent audience (`yo4x-emergency`), and standalone health probes. Triggering restrictive commands mandates hardware-backed phishing-resistant MFA (`mfa=hardware_key|webauthn`), explicit `authority=restrict_only` claims, and mTLS client certificate thumbprint validation via constant-time SHA-256 matching. State mutations require high-entropy idempotency keys and two-phase preview digest verification. Crucially, the API is strictly monotonic and containment-only: there is no un-trigger or release path, preventing unauthorized recovery or restriction relaxation through the emergency interface.

## Findings

None. The area is clean and adheres to all emergency safety invariants:

1. **Strict Restrictive-Only Authorization & Certificate Binding:** In `Program.cs:18-30` and `EmergencyRoutes.cs:27-30`, all endpoints require the `emergency-restrictive` authorization policy. The policy mandates `AuthenticationSchemes.Emergency`, phishing-resistant MFA (`hardware_key` or `webauthn`), restrict-only authority (`authority=restrict_only`), and full identity claims (`sub`, `tenant_id`, `session_id`, `environment`, `auth_time`). Furthermore, `ClientCertificateFilter` enforces mutual TLS certificate validation by verifying that the connecting client certificate SHA-256 digest matches the token's `certificate_sha256` claim via `CryptographicOperations.FixedTimeEquals`.
2. **Strict Idempotency & Concurrency Guards:** In `EmergencyRoutes.cs:51,111`, all mutation routes (`/restrictive-command-previews` and `/restrictive-commands`) attach `MutationPreconditionFilter()`, which mandates a high-entropy `Idempotency-Key` header matching `^[A-Fa-f0-9]{32,200}$` or `^[A-Za-z0-9_-]{22,200}$` (`MutationPreconditionFilter.cs:61-71`). Re-issuing the command with the same idempotency key is safe and idempotent.
3. **Confirmed Two-Phase Execution & Synchronous Status Tracking:** In `EmergencyRoutes.cs:31-110`, emergency commands follow a two-phase preview-and-commit pattern. `POST /restrictive-command-previews` computes a `RestrictivePreview` with target counts, degraded status flags, missing dimensions, and a SHA-256 `Digest`. `POST /restrictive-commands` requires matching `PreviewId` and `PreviewDigest` before issuing the containment command, returning `202 Accepted` with a `StatusUrl` header. Command execution status and impacted targets can be queried synchronously via `GET /emergency/v1/restrictive-commands/{commandId}` and `GET /emergency/v1/restrictive-commands/{commandId}/targets`.
4. **Fail-Safe Dependency Isolation & Health Probing:** In `Program.cs:31,47-56`, `IEmergencySafetyApplication` defaults to `UnavailableAdminApplication`, failing closed with `BackendCapabilityUnavailableException` (rendered as RFC 7807 503 Problem Details) if backing capabilities are absent. The readiness probe (`IsEmergencyReady`) monitors backend binding state independently. Downstream execution workers fail safe (cease trade execution) if leases expire or control communication drops.
5. **No Un-Trigger Path / Monotonic Containment:** The emergency surface area is strictly limited to restrictive containment templates (`BlockNewExposure`, `BlockNewDeployments`, `CloseOnly`, `QuarantineExactGatewayDigest`, `RevokeCloudWorker`). There are no endpoints or templates for releasing holds, resuming trading, or clearing quarantines; restriction release requires multi-party approval workflows through the main administrative control plane.
6. **Independent Control Plane Reachability:** `YO4X.EmergencySafety.Api` operates as a separate ASP.NET Core host with dedicated Kestrel limits (64 KiB body limit, HTTPS-only via `EmergencyHttpSecurity.cs:7-27`), independent port configuration, and distinct token audiences, allowing emergency operators to halt trading even if the primary admin BFF or control plane API is degraded or unreachable.

## Referrals

None.

## Coverage gaps

None.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 98.5s | 228034 tok | id=a4c158bc-cae0-4a90-80bc-3f4f9f6c1141
