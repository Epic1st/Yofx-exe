---
agent_id: B07
lane: dev-identity
scope:
  - src/Apps/YO4X.DevelopmentIdentity/AuthenticatedAccountFormRecoveryMiddleware.cs
  - src/Apps/YO4X.DevelopmentIdentity/Controllers/AccountController.cs
  - src/Apps/YO4X.DevelopmentIdentity/Controllers/AuthorizationController.cs
  - src/Apps/YO4X.DevelopmentIdentity/DevelopmentIdentityInitializer.cs
  - src/Apps/YO4X.DevelopmentIdentity/DevelopmentIdentityRegistration.cs
  - src/Apps/YO4X.DevelopmentIdentity/DevelopmentIdentityStartupGuard.cs
  - src/Apps/YO4X.DevelopmentIdentity/IdentityModels.cs
  - src/Apps/YO4X.DevelopmentIdentity/LocalIdentityContract.cs
  - src/Apps/YO4X.DevelopmentIdentity/LocalIdentityProvisioner.cs
  - src/Apps/YO4X.DevelopmentIdentity/Program.cs
  - src/Apps/YO4X.DevelopmentIdentity/README.md
  - src/Apps/YO4X.DevelopmentIdentity/YO4X.DevelopmentIdentity.csproj
  - src/Apps/YO4X.DevelopmentIdentity/appsettings.Development.json
  - src/Apps/YO4X.DevelopmentIdentity/appsettings.json
status: COMPLETE
generated: 2026-08-29T11:25:00Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# B07 — dev-identity

## Scope audited
All 14 files in the assigned scope were fully read and analyzed:
- `src/Apps/YO4X.DevelopmentIdentity/AuthenticatedAccountFormRecoveryMiddleware.cs` (23 lines)
- `src/Apps/YO4X.DevelopmentIdentity/Controllers/AccountController.cs` (161 lines)
- `src/Apps/YO4X.DevelopmentIdentity/Controllers/AuthorizationController.cs` (134 lines)
- `src/Apps/YO4X.DevelopmentIdentity/DevelopmentIdentityInitializer.cs` (52 lines)
- `src/Apps/YO4X.DevelopmentIdentity/DevelopmentIdentityRegistration.cs` (117 lines)
- `src/Apps/YO4X.DevelopmentIdentity/DevelopmentIdentityStartupGuard.cs` (31 lines)
- `src/Apps/YO4X.DevelopmentIdentity/IdentityModels.cs` (25 lines)
- `src/Apps/YO4X.DevelopmentIdentity/LocalIdentityContract.cs` (14 lines)
- `src/Apps/YO4X.DevelopmentIdentity/LocalIdentityProvisioner.cs` (93 lines)
- `src/Apps/YO4X.DevelopmentIdentity/Program.cs` (40 lines)
- `src/Apps/YO4X.DevelopmentIdentity/README.md` (45 lines)
- `src/Apps/YO4X.DevelopmentIdentity/YO4X.DevelopmentIdentity.csproj` (18 lines)
- `src/Apps/YO4X.DevelopmentIdentity/appsettings.Development.json` (7 lines)
- `src/Apps/YO4X.DevelopmentIdentity/appsettings.json` (10 lines)

## Verdict
The local development identity provider is exceptionally sound and enforces comprehensive defense-in-depth across its boundary. It is completely isolated from production hosts, strictly guarded by fail-closed multi-variable startup validation, protected at runtime by loopback IP filtering, and constrained to an execute-only loopback PostgreSQL database role. Tokens cannot be minted with elevated or administrative claims, signing certificates are dynamically generated local artifacts without hardcoded keys, and token lifetimes are strictly bounded.

## Findings
None.

The implementation holds up against all audit criteria:
1. **Production Reachability & Startup Guards:**
   - In `src/Apps/YO4X.DevelopmentIdentity/DevelopmentIdentityStartupGuard.cs:10-29`, startup unconditionally aborts unless `environment.IsDevelopment()` is true, `LocalIdentity:Enabled` is explicitly configured as `true` (defaulting to `false` in `appsettings.json:3`), and `LocalIdentityPostgres` connection options pass strict validation.
   - In `src/Apps/YO4X.DevelopmentIdentity/Program.cs:13-20`, runtime middleware inspects `context.Connection.RemoteIpAddress` on every HTTP request and immediately issues `403 Forbidden` if the remote IP is not loopback (`!IPAddress.IsLoopback(remote)`).
   - In `src/Apps/YO4X.DevelopmentIdentity/LocalIdentityProvisioner.cs:24-34`, the PostgreSQL connection requires user `yo4x_local_identity` on a loopback host (`127.0.0.1`, `::1`, `localhost`). In PostgreSQL (`004_local_development_identity_provisioning.sql:126-144`), the stored procedure `identity.provision_local_development_identity` is executable exclusively by `yo4x_local_identity` and rejects any `target_tenant_id` distinct from the fixed local tenant ID (`019c8d27-763d-7000-8000-000000000001`).
2. **Project References:**
   - `YO4X.DevelopmentIdentity` is an independent standalone web executable (`Microsoft.NET.Sdk.Web`). It is not referenced by any production host, worker, API, or gateway project in `YO4X.sln` (referenced only by unit/integration test suites `YO4X.DevelopmentIdentity.Tests` and `YO4X.Postgres.IntegrationTests`).
3. **Signing Keys & Consuming Services:**
   - In `src/Apps/YO4X.DevelopmentIdentity/DevelopmentIdentityRegistration.cs:99-100`, OpenIddict is configured with `AddDevelopmentEncryptionCertificate()` and `AddDevelopmentSigningCertificate()`, which create self-signed ephemeral keys stored locally in `.local/data-protection`. There are no hardcoded keys or static default secrets in code or repository configuration.
   - Downstream services (in `src/BuildingBlocks/YO4X.Api/AuthenticationExtensions.cs:166-174`) only permit development authority certificate pinning when running under `Development` on loopback HTTPS, preventing production services from accepting tokens signed by the local development identity provider.
4. **Claims & Privilege Escalation:**
   - In `src/Apps/YO4X.DevelopmentIdentity/Controllers/AuthorizationController.cs:115-131`, `CreatePrincipalAsync` hardcodes the exact claim set: `sub`, `email`, `email_verified`, `tenant_id` (fixed to `LocalIdentityContract.TenantId`), and `session_id`. OpenIddict scopes are restricted to `email` and `profile`.
   - Callers cannot mint admin tokens: administrative endpoints throughout YO4X enforce `AuthenticationSchemes.Admin` (cookie-based `__Host-yo4x-admin` with MFA/WebAuthn and managed device claims), which does not accept OIDC JWT bearer tokens.
5. **Token Lifetimes:**
   - Access token lifetime is set to 10 minutes (`DevelopmentIdentityRegistration.cs:97`).
   - Authorization code lifetime is set to 2 minutes (`DevelopmentIdentityRegistration.cs:98`).
   - Application cookie expires in 8 hours without sliding expiration (`DevelopmentIdentityRegistration.cs:71`).
   - Provisioned PostgreSQL database session expires in at most 30 minutes (`LocalIdentityProvisioner.cs:89`).

## Referrals
None.

## Coverage gaps
None. (Existing tests in `tests/YO4X.DevelopmentIdentity.Tests/` cover startup validation under non-development/disabled configurations, loopback URI constraints, public client PKCE enforcement, OIDC discovery, secure cookie attributes, antiforgery recovery, and silent OIDC prompt=none authentication failures).


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 73.5s | 219418 tok | id=13bd2661-df54-4aaf-8627-1e971931e9c3
