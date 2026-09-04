---
agent_id: I02
lane: sweep-authz
status: clean
date: 2026-08-29
corps: [Epic1st/Yofx-exe]
---

# Lane I02: sweep-authz Report

## Executive Summary

A comprehensive cross-cutting audit of authentication (authn) and authorisation (authz) was conducted across the entire YO4X codebase. Every HTTP host application (`YO4X.ControlPlane.Api`, `YO4X.Admin.Bff`, `YO4X.EmergencySafety.Api`, `YO4X.SecretIngestion.Api`, `YO4X.DevelopmentIdentity`, `YO4X.GatewayHost`, `YO4X.Supervisor`, and `YO4X.ControlPlane.Workers`) was examined for endpoint policy enforcement, ASP.NET Core middleware ordering, tenant isolation and IDOR vulnerabilities, claim tampering/forgery, development-only bypass leakage into production, emergency command privilege escalation, and fail-open defaults.

The audit determined that authn/authz architecture across all YO4X hosts is designed with rigorous defense-in-depth, zero-trust token validation, cryptographic client certificate binding, strict tenant execution contexts in PostgreSQL, and fail-closed authorization defaults. No authorization bypasses, IDORs, unauthenticated sensitive endpoints, or middleware ordering bugs were identified.

## Scope & Methodology

The sweep covered all solution assemblies, host startup pipelines, endpoint route mappings, authentication scheme handlers, claim readers, authorization policies, endpoint filters, and data-access layers:

1. **Host Applications & Route Registrations**:
   - `src/Apps/YO4X.ControlPlane.Api/Program.cs`, `FrontendProjectionEndpoints.cs`, `BrokerAccountDiscoveryEndpoints.cs`, `DevelopmentMt5ConnectionProbe.cs`
   - `src/Apps/YO4X.Admin.Bff/Program.cs`, `AdminRoutes.cs`, `AdminHttpSecurity.cs`
   - `src/Apps/YO4X.EmergencySafety.Api/Program.cs`, `EmergencyRoutes.cs`, `EmergencyHttpSecurity.cs`
   - `src/Apps/YO4X.SecretIngestion.Api/Program.cs`, `IngestionProofReader.cs`
   - `src/Apps/YO4X.DevelopmentIdentity/Program.cs`, `DevelopmentIdentityStartupGuard.cs`, `DevelopmentIdentityRegistration.cs`, `Controllers/AccountController.cs`, `Controllers/AuthorizationController.cs`
   - `src/Runtime/YO4X.GatewayHost/Program.cs`, `GatewayHostHealthEndpoints.cs`, `GatewayUserOperationProtocolRegistration.cs`
   - `src/Runtime/YO4X.Supervisor/Program.cs`
   - `src/Apps/YO4X.ControlPlane.Workers/Program.cs`

2. **Building Blocks & Security Infrastructure**:
   - `src/BuildingBlocks/YO4X.Api/AuthenticationExtensions.cs`
   - `src/BuildingBlocks/YO4X.Api/ApiFoundation.cs`
   - `src/BuildingBlocks/YO4X.Api/ClientCertificateFilter.cs`
   - `src/BuildingBlocks/YO4X.Api/ClaimReader.cs`
   - `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresControlPlaneApplication.cs`, `PostgresControlPlaneReads.cs`, `PostgresFrontendProjections.cs`, `PostgresBrokerAccountMutations.cs`, `PostgresDeploymentMutations.cs`, `PostgresCredentialMutations.cs`, `PostgresSessionMutations.cs`, `PostgresStrategyImportMutations.cs`, `PostgresUserOperations.cs`
   - `src/Infrastructure/YO4X.Admin.Postgres/AdminPostgresApplication.cs`, `AdminSecurityRepository.cs`, `AdminPermissions.cs`, `AdminPostgresApplication.Reads.cs`, `AdminPostgresApplication.Commands.cs`, `AdminPostgresApplication.Approvals.cs`
   - `src/Infrastructure/YO4X.RuntimeControl.Postgres/PostgresRuntimeControlPlaneApplication.cs`
   - `src/Modules/SecretCoordination/YO4X.SecretCoordination/CredentialIngestionProcessor.cs`

## Systematic Surface Enumeration

### 1. Endpoint & Policy Matrix

| Host Application | Route Pattern | HTTP Method | Required Policy / Scheme | Endpoint Filters & Security Guards | Verified Action / Purpose |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `YO4X.ControlPlane.Api` | `/health/live`, `/health/startup`, `/health/ready` | `GET` | Anonymous (`AllowAnonymous`) | Public health probe | Liveness/readiness orchestration probes |
| `YO4X.ControlPlane.Api` | `/v1/auth/refresh` | `POST` | Anonymous (`AllowAnonymous`) | Returns 503 SERVICE_UNAVAILABLE | Explicitly disabled refresh endpoint |
| `YO4X.ControlPlane.Api` | `/v1/me` | `GET` | `user` (`yo4x-user`) | Tenant Session Reader | Authenticated user identity view |
| `YO4X.ControlPlane.Api` | `/v1/sessions` | `GET` | `user` (`yo4x-user`) | Tenant Session Reader | List active/expired user sessions |
| `YO4X.ControlPlane.Api` | `/v1/sessions/{sessionId}` | `DELETE` | `user` (`yo4x-user`) | Version Precondition Filter | Revoke specific user session family |
| `YO4X.ControlPlane.Api` | `/v1/broker-accounts` | `GET` | `user` (`yo4x-user`) | Tenant Isolation Filter | List user's broker accounts |
| `YO4X.ControlPlane.Api` | `/v1/broker-accounts` | `POST` | `user` (`yo4x-user`) | `IPAddress.IsLoopback` + Verified Email | Register demo broker account |
| `YO4X.ControlPlane.Api` | `/v1/broker-accounts/{brokerAccountId}` | `GET` | `user` (`yo4x-user`) | Tenant Isolation Filter | Get broker account details |
| `YO4X.ControlPlane.Api` | `/v1/broker-accounts/{brokerAccountId}/credential-state` | `GET` | `user` (`yo4x-user`) | Tenant Isolation Filter | Get credential state |
| `YO4X.ControlPlane.Api` | `/v1/broker-accounts/{brokerAccountId}/actions/{action}` | `POST` | `user` (`yo4x-user`) | Precondition + MFA (for deletion) | Connection test, disable, or delete credentials |
| `YO4X.ControlPlane.Api` | `/v1/broker-accounts/{brokerAccountId}/credential-ingestion-sessions` | `POST` | `user` (`yo4x-user`) | MFA Assurance + Approved Origin Check | Issue one-time credential ingestion proof |
| `YO4X.ControlPlane.Api` | `/v1/deployments` | `POST` | `user` (`yo4x-user`) | Precondition + Verified Email | Create deployment with validated frozen bindings |
| `YO4X.ControlPlane.Api` | `/v1/deployments/{deploymentId}` | `GET` | `user` (`yo4x-user`) | Tenant Isolation Filter | Read deployment state |
| `YO4X.ControlPlane.Api` | `/v1/deployments/{deploymentId}/activity` | `GET` | `user` (`yo4x-user`) | Tenant Isolation Filter | Read deployment audit trail |
| `YO4X.ControlPlane.Api` | `/v1/deployments/{deploymentId}/actions/{requestedState}` | `POST` | `user` (`yo4x-user`) | Precondition + Revalidation | Trigger deployment state transition |
| `YO4X.ControlPlane.Api` | `/v1/operations/{operationId}` | `GET` | `user` (`yo4x-user`) | Tenant Isolation Filter | Track async operation progress |
| `YO4X.ControlPlane.Api` | `/v1/strategy-import-sessions` | `POST` | `user` (`yo4x-user`) | MFA Assurance + Verified Email | Create signed strategy import capability |
| `YO4X.ControlPlane.Api` | `/v1/strategy-import-sessions/{importJobId}` | `DELETE` | `user` (`yo4x-user`) | MFA Assurance + Precondition | Revoke strategy import session |
| `YO4X.ControlPlane.Api` | `/v1/broker-servers/approved` | `GET` | `user` (`yo4x-user`) | Tenant Isolation Filter | List approved broker servers |
| `YO4X.ControlPlane.Api` | `/v1/broker-servers/approved` | `POST` | `user` (`yo4x-user`) | Verified Email + Tenant Authority | Promote directory server to demo-linkable |
| `YO4X.ControlPlane.Api` | `/v1/broker-servers/directory` | `GET` | `user` (`yo4x-user`) | Query string validation | Search broker directory |
| `YO4X.ControlPlane.Api` | `/v1/broker-servers/directory/{directoryServerId}` | `GET` | `user` (`yo4x-user`) | Query validation | Get directory server details |
| `YO4X.ControlPlane.Api` | `/v1/projections/*` (30+ routes) | `GET` / `PUT` | `user` (`yo4x-user`) | Tenant Context Transaction Filter | User workspace, bots, backtests, catalog, runner |
| `YO4X.ControlPlane.Api` | `/v1/development/mt5-connection-probe` | `POST` | `user` (`yo4x-user`) | `IsDevelopment()` + Config Gate + Loopback IP | Dev-only MT5 probe |
| `YO4X.ControlPlane.Api` | `/internal/v1/workers/register` | `POST` | `workload` (`yo4x-workload`) | `ClientCertificateFilter` + Workload Claims | Worker node assignment registration |
| `YO4X.ControlPlane.Api` | `/internal/v1/workers/{workerId}/components/{component}/heartbeat` | `POST` | `workload` (`yo4x-workload`) | `ClientCertificateFilter` + Workload Claims | Component heartbeat & liveness renewal |
| `YO4X.ControlPlane.Api` | `/internal/v1/execution-leases/issue` | `POST` | `workload` (`yo4x-workload`) | `ClientCertificateFilter` + Supervisor Only | Issue signed trade execution lease |
| `YO4X.ControlPlane.Api` | `/internal/v1/execution-leases/renew` | `POST` | `workload` (`yo4x-workload`) | `ClientCertificateFilter` + Supervisor Only | Renew signed trade execution lease |
| `YO4X.ControlPlane.Api` | `/internal/v1/deployments/{deploymentId}/events` | `POST` | `workload` (`yo4x-workload`) | `ClientCertificateFilter` + Workload Claims | Append signed runtime evidence events |
| `YO4X.ControlPlane.Api` | `/internal/v1/command-targets/{targetId}/delivery-events` | `POST` | `workload` (`yo4x-workload`) | `ClientCertificateFilter` + Workload Claims | Record admin command delivery status |
| `YO4X.ControlPlane.Api` | `/internal/v1/command-targets/{targetId}/reconciliation-results` | `POST` | `workload` (`yo4x-workload`) | `ClientCertificateFilter` + Workload Claims | Report admin command execution result |
| `YO4X.ControlPlane.Api` | `/internal/v1/broker-accounts/{brokerAccountId}/operation-results` | `POST` | `workload` (`yo4x-workload`) | `ClientCertificateFilter` + Workload Claims | Record broker operation completion |
| `YO4X.ControlPlane.Api` | `/internal/v1/deployments/{deploymentId}/operation-results` | `POST` | `workload` (`yo4x-workload`) | `ClientCertificateFilter` + Workload Claims | Record deployment operation completion |
| `YO4X.Admin.Bff` | `/health/live`, `/health/startup`, `/health/ready` | `GET` | Anonymous (`AllowAnonymous`) | Public health probe | BFF health check probes |
| `YO4X.Admin.Bff` | `/admin/v1/me` | `GET` | `admin` (`yo4x-admin-session`) | `AdminOriginFilter` + Permission check | Get admin identity and active permissions |
| `YO4X.Admin.Bff` | `/admin/v1/approvals` | `GET` | `admin` (`yo4x-admin-session`) | `AdminOriginFilter` + `admin.approval.read` | List pending approval requests |
| `YO4X.Admin.Bff` | `/admin/v1/approvals/{approvalId}` | `GET` | `admin` (`yo4x-admin-session`) | `AdminOriginFilter` + `admin.approval.read` | Get approval request details |
| `YO4X.Admin.Bff` | `/admin/v1/approvals/{approvalId}/decisions` | `POST` | `admin` (`yo4x-admin-session`) | CSRF + `admin.approval.decide` + Two-Person Rule | Approve/reject containment command |
| `YO4X.Admin.Bff` | `/admin/v1/commands/{commandId}` | `GET` | `admin` (`yo4x-admin-session`) | `AdminOriginFilter` + `admin.command.read` | Read command status |
| `YO4X.Admin.Bff` | `/admin/v1/commands/{commandId}/targets` | `GET` | `admin` (`yo4x-admin-session`) | `AdminOriginFilter` + `admin.command.read` | Read command target delivery status |
| `YO4X.Admin.Bff` | `/admin/v1/commands/{commandId}/cancellations` | `POST` | `admin` (`yo4x-admin-session`) | CSRF + `admin.command.cancel` | Cancel pending undispatched command |
| `YO4X.Admin.Bff` | `/admin/v1/commands/{commandId}/compensations` | `POST` | `admin` (`yo4x-admin-session`) | CSRF + `admin.command.compensation.request` | Request 2-person compensation for dispatched command |
| `YO4X.Admin.Bff` | `/admin/v1/deployments/{deploymentId}` | `GET` | `admin` (`yo4x-admin-session`) | Sensitive Read Audit + `admin.deployment.read` | Inspect deployment details with recorded purpose |
| `YO4X.Admin.Bff` | `/admin/v1/deployments/{deploymentId}/containments/{type}` | `POST` | `admin` (`yo4x-admin-session`) | CSRF + Specific Containment Permission | Request CloseOnly, StopAfterFlat, RevokeLease, ReplaceWorker |
| `YO4X.EmergencySafety.Api` | `/health/live`, `/health/startup`, `/health/ready` | `GET` | Anonymous (`AllowAnonymous`) | Public health probe | Emergency API health probes |
| `YO4X.EmergencySafety.Api` | `/emergency/v1/actions/preview` | `POST` | `emergency-restrictive` (`yo4x-emergency`) | `ClientCertificateFilter` + Preview Digest | Generate emergency containment impact preview |
| `YO4X.EmergencySafety.Api` | `/emergency/v1/actions/submit` | `POST` | `emergency-restrictive` (`yo4x-emergency`) | `ClientCertificateFilter` + Preview SHA-256 Digest | Execute emergency containment command |
| `YO4X.EmergencySafety.Api` | `/emergency/v1/commands/{commandId}` | `GET` | `emergency-restrictive` (`yo4x-emergency`) | `ClientCertificateFilter` | Get emergency command status |
| `YO4X.EmergencySafety.Api` | `/emergency/v1/commands/{commandId}/targets` | `GET` | `emergency-restrictive` (`yo4x-emergency`) | `ClientCertificateFilter` | Get emergency target status |
| `YO4X.SecretIngestion.Api` | `/health/live`, `/health/startup`, `/health/ready` | `GET` | Anonymous (`AllowAnonymous`) | Public health probe | Secret Ingestion API health probes |
| `YO4X.SecretIngestion.Api` | `/v1/tenants/{tenantId}/credential-ingestion-grants/{grantId}/consume` | `POST` | Custom Tokenless Ingestion Proof | Bearer Hash + Nonce Hash + Origin Check | Consume one-time credential ingestion grant |
| `YO4X.DevelopmentIdentity` | `/account/theme.css` | `GET` | Anonymous (`AllowAnonymous`) | Loopback IP Filter | Static CSS stylesheet |
| `YO4X.DevelopmentIdentity` | `/account/register` | `GET` / `POST` | Anonymous (`AllowAnonymous`) | Loopback IP Filter + Antiforgery | Development user registration |
| `YO4X.DevelopmentIdentity` | `/account/sign-in` | `GET` / `POST` | Anonymous (`AllowAnonymous`) | Loopback IP Filter + Antiforgery | Development user authentication |
| `YO4X.DevelopmentIdentity` | `/connect/authorize` | `GET` | Anonymous (`[AllowAnonymous]`) | Loopback IP Filter + Cookie Auth | OpenIddict OIDC authorize endpoint |
| `YO4X.DevelopmentIdentity` | `/connect/token` | `POST` | Anonymous (`[AllowAnonymous]`) | Loopback IP Filter + PKCE Auth Code | OpenIddict OIDC token exchange |
| `YO4X.GatewayHost` | `/health/live`, `/health/startup`, `/health/ready` | `GET` | Anonymous (`AllowAnonymous`) | Public health probe | Gateway health check probes |

---

## Findings

No findings were identified. The area is clean.

All endpoints require appropriate authentication and authorization policies; middleware order adheres to ASP.NET Core specifications; all database queries enforce tenant isolation; client claims are cryptographically verified; and emergency/development facilities are strictly guarded.

---

## Verified Invariants & Threat Protections

### 1. Middleware Pipeline Order
The middleware execution sequence across all host applications is structured to guarantee that authentication and authorization execute before any endpoint handlers.

In `src/Apps/YO4X.ControlPlane.Api/Program.cs:64-77`:
```csharp
app.UseApiFoundation();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapYo4xHealthProbes();
```
In `src/Apps/YO4X.Admin.Bff/Program.cs:51-60`:
```csharp
app.UseAdminApplicationProblems();
app.UseProblemStatusCodes();
app.UseAdminHttpsOnly();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapYo4xHealthProbes();
app.MapAdminRoutes();
```
In `src/Apps/YO4X.EmergencySafety.Api/Program.cs:38-46`:
```csharp
app.UseEmergencyApplicationProblems();
app.UseProblemStatusCodes();
app.UseEmergencyHttpsOnly();
app.UseAuthentication();
app.UseAuthorization();
app.MapYo4xHealthProbes();
app.MapEmergencyRoutes();
```
No sensitive route is mapped prior to `UseAuthentication()` or `UseAuthorization()`.

### 2. Tenant Isolation & IDOR Protection
Every database query in `PostgresControlPlaneReads.cs`, `PostgresFrontendProjections.cs`, and `PostgresRuntimeControlPlaneApplication.cs` enforces tenant and user isolation at both the session context and query predicate level:

1. **Transaction Initialization Context**:
   In `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresControlPlaneApplication.cs:62-69`:
   ```csharp
   var executionContext = new TenantExecutionContext(
       actor.TenantId,
       actor.UserId,
       correlationId,
       actor.SessionId);
   TenantPostgresTransaction transaction = await database
       .BeginTenantTransactionAsync(executionContext, cancellationToken)
       .ConfigureAwait(false);
   ```
2. **Authoritative Session Liveness Verification**:
   In `PostgresControlPlaneApplication.cs:80-109`, the transaction executes a query verifying that the calling `identity.user_identities`, `identity.tenants`, and `identity.user_session_families` records are currently active and not expired before fulfilling the request.
3. **Explicit Query Predicates**:
   Every query filtering by resource identifier (`broker_account_id`, `deployment_id`, `operation_id`, `strategy_import_job_id`) includes:
   `WHERE tenant_id = @tenant_id AND user_id = @user_id AND id = @resource_id`.
   Cross-tenant and cross-user data access is structurally impossible.

### 3. Claim Trust & Cryptographic Mutual Authentication
All authentication policies enforce strict token validation and mTLS certificate thumbprint binding:

1. **User Policy (`yo4x-user`)**:
   Enforces JWT validation with issuer, audience, lifetime, and signing key checks. Requires `session_id` and `tenant_id` claims (`src/BuildingBlocks/YO4X.Api/AuthenticationExtensions.cs:33-40`).
2. **Workload Policy (`yo4x-workload`)**:
   Requires `tenant_id`, `workload_id`, `worker_instance_id`, `deployment_id`, `broker_account_id`, `generation`, `region`, `component`, and `certificate_sha256` claims (`AuthenticationExtensions.cs:68-87`).
   Enforces `ClientCertificateFilter` (`src/BuildingBlocks/YO4X.Api/ClientCertificateFilter.cs:20-33`), matching the client certificate SHA-256 thumbprint with the token's `certificate_sha256` claim.
3. **Admin Session Policy (`yo4x-admin-session`)**:
   Cookie-based session requiring phishing-resistant MFA (`mfa` claim in `hardware_key` or `webauthn`), managed device (`managed_device == "true"`), and step-up authentication timestamp verification against the PostgreSQL `identity.admin_sessions` store (`src/Infrastructure/YO4X.Admin.Postgres/AdminSecurityRepository.cs:116-129`).
4. **Emergency Restrictive Policy (`yo4x-emergency`)**:
   JWT scheme requiring `mfa` (HardwareKey / WebAuthn), `authority=restrict_only`, `tenant_id`, `session_id`, `auth_time`, and `ClientCertificateFilter` certificate binding.

### 4. Admin Two-Person Rule & Immutable Containment Proofs
Admin containment commands enforce separation of duties:
1. **Self-Approval Denial**:
   In `src/Infrastructure/YO4X.Admin.Postgres/AdminPostgresApplication.Approvals.cs:89-93`:
   ```csharp
   if (approval.RequesterId == actor.ActorId)
   {
       throw new AdminAuthorizationDeniedException(
           "APPROVAL_SELF_DECISION_FORBIDDEN",
           "The requester cannot approve or reject their own command.");
   }
   ```
2. **Preview Digest Validation**:
   The approver must submit the exact `BindingDigest` computed over the command digest, impact preview digest, command row version, and restriction vector digest (`AdminPostgresApplication.Approvals.cs:310-363`).
3. **Freshness & Step-Up Enforcement**:
   Approval requires an active admin session satisfying the minimum assurance age bound (`EnsureApprovalSessionRequirement`).

### 5. Development Path Safeguards
Development-specific identity and probe endpoints cannot be exploited in production:
1. **Development Identity Host (`YO4X.DevelopmentIdentity`)**:
   `DevelopmentIdentityStartupGuard.Validate(...)` asserts `environment.IsDevelopment()` at startup (`src/Apps/YO4X.DevelopmentIdentity/DevelopmentIdentityStartupGuard.cs:17-26`). If executed in non-development environments, startup aborts with `InvalidOperationException`.
   The `UseDevelopmentIdentityLoopbackGuard` middleware rejects any incoming request whose `RemoteIpAddress` is not loopback (`IPAddress.IsLoopback`).
2. **MT5 Direct Connection Probe**:
   `AddDevelopmentMt5ConnectionProbe` and `MapDevelopmentMt5ConnectionProbe` (`src/Apps/YO4X.ControlPlane.Api/DevelopmentMt5ConnectionProbe.cs:51-88`) verify `environment.IsDevelopment()` and `DevelopmentMt5ConnectionProbe:Enabled`. The endpoint handler further verifies `IPAddress.IsLoopback(context.Connection.RemoteIpAddress)`.

### 6. Fail-Closed Default Behavior
1. Missing or unconfigured policies fail closed.
2. In `src/Apps/YO4X.ControlPlane.Api/Program.cs:32`, `UnavailableRuntimeControlPlaneApplication` is registered as the default fallback for `IRuntimeControlPlaneApplication`, throwing `BackendCapabilityUnavailableException` on any attempt to invoke runtime endpoints when postgres runtime control is not configured.
3. In `src/Apps/YO4X.EmergencySafety.Api/Program.cs:31`, `UnavailableAdminApplication` is registered as the fallback for `IEmergencySafetyApplication`.
4. In `src/Apps/YO4X.ControlPlane.Api/LocalBrokerCredentialVault.cs:144-153`, `UnavailableLocalBrokerCredentialVault` is the fallback implementation.

---

## Coverage Gaps

1. **Dynamic Policy Registration Mocking**: The audit verified all static C# policy registrations and runtime attributes; end-to-end HTTP integration tests under live TLS proxy termination (e.g., Envoy/NGINX mTLS header propagation) were not run in this static sweep.
2. **Database Role Privileges (Cross-Lane Reference)**: Database-level table grant enforcement is owned by Lane D04 (`db-roles`); this report focused on application-level and tenant context query isolation.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 206.9s | 807089 tok | id=c6890a5b-9260-4551-b027-67b6fbc85895
