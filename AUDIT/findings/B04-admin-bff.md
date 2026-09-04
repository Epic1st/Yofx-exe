---
agent_id: B04
lane: admin-bff
scope:
  - src/Apps/YO4X.Admin.Bff/Program.cs
  - src/Apps/YO4X.Admin.Bff/AdminRoutes.cs
  - src/Apps/YO4X.Admin.Bff/AdminHttpSecurity.cs
  - src/Apps/YO4X.Admin.Bff/appsettings.json
  - src/Apps/YO4X.Admin.Bff/appsettings.Development.json
  - src/Apps/YO4X.Admin.Bff/Properties/launchSettings.json
  - src/Apps/YO4X.Admin.Bff/YO4X.Admin.Bff.csproj
status: COMPLETE
generated: 2026-08-29T08:55:00Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# B04 — admin-bff

## Scope audited
- `src/Apps/YO4X.Admin.Bff/Program.cs` (99 lines) — application bootstrap, DI registrations, database endpoint parity validation, and middleware pipeline.
- `src/Apps/YO4X.Admin.Bff/AdminRoutes.cs` (505 lines) — endpoint mappings, route authorization, mutation guards, request validation, and actor claim projection.
- `src/Apps/YO4X.Admin.Bff/AdminHttpSecurity.cs` (169 lines) — origin validation policy, origin endpoint filter, HTTPS-only enforcement, and RFC 7807 problem status mappings.
- `src/Apps/YO4X.Admin.Bff/appsettings.json` (18 lines) — production configuration for allowed origins and error base URI.
- `src/Apps/YO4X.Admin.Bff/appsettings.Development.json` (14 lines) — development configuration for local origins.
- `src/Apps/YO4X.Admin.Bff/Properties/launchSettings.json` (24 lines) — environment and port bindings.
- `src/Apps/YO4X.Admin.Bff/YO4X.Admin.Bff.csproj` (16 lines) — project references and SDK configuration.

## Verdict
The `YO4X.Admin.Bff` service exhibits outstanding security posture and architectural design for a financial trading administrative BFF. It does not use shared privileged service credentials or relay ambient tokens to downstream services; instead, it unpacks the authenticated session claims into a strongly-typed `AdminActor` and passes it directly into tenancy-isolated database transactions. The surface area is strictly narrowed to critical containment commands, two-person rule approval decisions, and purpose-audited deployment reads. Cookie settings enforce `__Host-` prefixes with `SameSite=Strict`, `Secure=Always`, and `HttpOnly=true`. State mutations require triple-layer defense (Strict SameSite cookies, explicit `X-CSRF-Token` antiforgery validation, and strict `Origin` header allowlist filtering), alongside mandatory high-entropy `Idempotency-Key` headers and optimistic concurrency `If-Match` version constraints.

## Findings

None. The audited BFF pattern implementation holds up under rigorous security inspection:
1. **No Token Relay / No Privileged Downstream Credentials:** The BFF connects directly to PostgreSQL via tenancy-isolated transactions scoped to the caller's specific `TenantExecutionContext(actor.TenantId, actor.ActorId, correlationId, actor.SessionId)`. Downstream authorization and RBAC permissions are evaluated against the caller's identity claims, preventing any privilege escalation.
2. **Narrowed Surface Area:** The BFF exposes only containment safety controls (`CloseOnly`, `StopAfterFlat`, `RevokeLease`, `ReplaceWorker` with predefined safety vectors), approval decisions with two-person rule enforcement (self-approval blocked), command status tracking, and sensitive reads requiring mandatory audited `purpose` justifications.
3. **Cookie Security:** Cookies (`__Host-yo4x-admin` and `__Host-yo4x-admin-csrf`) utilize the `__Host-` prefix, `SameSiteMode.Strict`, `CookieSecurePolicy.Always`, `HttpOnly = true`, and path `/`.
4. **CSRF & Origin Protection:** All mutation endpoints combine ASP.NET Core Antiforgery metadata (`RequireAntiforgeryTokenAttribute`), strict allowlisted `AdminOriginFilter`, and `MutationPreconditionFilter`.
5. **Authorization on Every Route:** The entire `/admin/v1` route group mandates the `"admin"` policy requiring hardware-backed phishing-resistant MFA (`hardware_key` / `webauthn`), managed device compliance (`managed_device == "true"`), and maximum session age checks.

## Referrals

None.

## Coverage gaps

None.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 83.3s | 197346 tok | id=75456a7d-fcef-4f72-bccd-a711ae8f5f7a
