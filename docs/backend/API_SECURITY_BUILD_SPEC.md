# YO4X API and Security Build Specification

**Status:** Build-ready foundation specification. Phase U0 is the only active product phase. V1A, live trading, local mode, general MQ5 conversion, and the broad admin suite remain gated.

**Applies to:** User control plane, reduced admin safety plane, independent emergency safety control, secret-ingestion boundary, cloud runtime orchestration, and PostgreSQL persistence.

**Architecture authorities:** `USER_SIDE_ARCHITECTURE.md`, `ADMIN_SIDE_ARCHITECTURE.md`, and `PHASE_U0_EXECUTION_PLAN.md`. This specification incorporates the independent admin/security review dated 2026-08-22. If a later implementation choice weakens a safety property in those documents, record an architecture decision and obtain security/risk approval before coding it.

## 1. Build decision and staged scope

Build the first backend as an ASP.NET Core modular monolith backed by a real PostgreSQL database, with isolated worker processes and explicit external boundaries. Do not split business modules into network services merely for organizational separation. Do split processes where credentials, untrusted input, native code, emergency availability, or failure isolation require it.

The first usable foundation is deliberately small:

1. **A0 safety foundation:** staff SSO, hardware-key MFA policy, scoped authorization, JIT access records, typed commands, approvals, target results, immutable audit intent/outbox, incident linkage, and the independent restrictive emergency path.
2. **A1 read-only operations:** purpose-bound user/account lookup, deployment and three-component worker health, broker state, reconciliation status, unknown command monitoring, support cases, and sensitive-read auditing.
3. **A2 gateway governance before runtime containment:** quarantined gateway artifact registration, exact hash/provenance/SBOM/license/network evidence, compatibility tests, demo canary, approved digest promotion, rollback, and revocation.
4. **U0/A3 runtime proof and containment:** one approved demo broker/server, one exact gateway artifact, one manually reviewed strategy path, Supervisor/StrategyHost/GatewayHost isolation, close-only, stop-after-flat, lease revoke, concrete cloud-worker fencing, policy-vector kill switches, target-level propagation, and broker reconciliation.

Do not build billing administration, broad privacy automation, general source viewing, public registration, a general MQ5 converter, cost forecasting, announcements, or broad feature-flag tooling as part of this foundation. Minimal privacy intake and legal-hold-safe processing contracts still exist so data rights are not handled with ad hoc database edits.

No production or shared-demo environment may start with fabricated users, broker accounts, strategies, positions, commands, or audit rows. Integration tests use disposable real PostgreSQL instances. Local developer fixtures are explicit, non-production-only commands protected by an environment assertion; they never run from application startup or migrations.

## 2. Deployable boundaries

### 2.1 Processes

| Process | Authority | Forbidden dependencies or capabilities |
|---|---|---|
| `YO4X.ControlPlane.Api` | User identity/session, tenant-owned metadata, entitlement and deployment configuration, agent/lease orchestration | No `mt5api.dll`; no broker password plaintext; no private-source parsing/execution |
| `YO4X.Admin.Bff` | Admin session, redacted read models, typed command request/approval APIs | No vault permission; no broker route; no raw SQL/script console; no raw order model |
| `YO4X.EmergencySafety.Api` | Independently available, predefined restrictive policy commands only | No permission expansion, secret access, code/release publication, database console, or broker orders |
| `YO4X.SecretIngestion.Api` | Consume one-time credential-ingestion grants and create/update a vault secret through the secret broker | No normal application controllers, analytics, message body logging, admin sessions, or secret read-back |
| `YO4X.ControlPlane.Workers` | Transactional outbox delivery, projections, notifications, command coordination | No direct broker credentials; consumers are idempotent |
| `YO4X.Conversion.Worker` | Quarantined, disposable parsing/analysis jobs in a network-denied sandbox | No production credentials, broker access, normal control-plane execution, or arbitrary .NET output deployment |
| Cloud `Supervisor` | Lease/generation, deterministic event transaction, formal risk policy, durable journal, normalized command lifecycle, reconciliation | Does not load the vendor DLL and does not expose credentials to StrategyHost |
| Cloud `StrategyHost` | Reviewed strategy or restricted IR evaluation against normalized snapshots | No credential, gateway, network, native library, unrestricted filesystem/process/thread/reflection capability |
| Cloud `GatewayHost` | Sole loader of the approved `mt5api.dll`; temporary credential use; normalized broker I/O | Accepts only Supervisor-approved normalized commands; broker/control-plane allowlisted egress only |

Supervisor, StrategyHost, and GatewayHost are separate OS processes or containers, not merely interfaces or assemblies. Each has a unique workload identity, read-only immutable image/package layers, separate writable storage, authenticated and sequence-checked IPC, CPU/memory/time/socket limits, and independently reported health. One account-level workload serves one dedicated hedging demo account in U0/V1A.

The future local runtime uses the same three-process wording, but enforcement is cooperative: after lease expiry, an untampered official YO4X local agent will not create new exposure. YO4X cannot guarantee broker-enforced prevention against software or credentials used independently on a user-controlled device. APIs and dashboards must report authorization, official-worker observation, and broker confirmation separately and must never translate an unreachable local worker into “trading stopped.”

### 2.2 Module boundaries inside the backend

Use one assembly/project per logical module with public application ports and internal domain/persistence details:

- `Identity`: user credentials, verification, MFA, session families, device records, recovery.
- `AdminIdentity`: OIDC staff identities, assurance, admin sessions, managed-device claims.
- `Authorization`: tenant checks, staff permissions/scopes, ABAC, temporary grants, JIT infrastructure grants.
- `Approvals`: immutable approval requests and independent decisions bound to exact command/preview digests.
- `Commands`: typed command lifecycle, targets, results, cancellation, compensation, idempotency.
- `Policy`: versioned execution-safety vectors, lattice merge, release review, entitlement/lease decisions.
- `Tenancy`: tenant context and ownership invariants. It exposes no “set arbitrary tenant” application API.
- `BrokerAccounts`: masked account metadata, binding fingerprints, credential state read model, capability records.
- `SecretCoordination`: one-time ingestion grant metadata and typed deletion/disable/reauthentication commands; it has no secret values or vault client.
- `Deployments`: validated configuration, lifecycle, worker ownership, generations, desired/observed/reconciled state.
- `RuntimeOperations`: worker registry, three-component health, fencing evidence, reconciliation, unknown outcomes.
- `GatewayGovernance`: broker capability registry, quarantined artifact references, evidence, exact-digest releases.
- `StrategyGovernance`: immutable reviewed package/evidence metadata. General conversion remains deferred.
- `Incidents`: incident state, affected scope, restrictive action linkage.
- `Support`: cases and sanitized attachments/read access.
- `Privacy`: request intake, preview/approval/process state, holds, non-sensitive completion evidence.
- `Audit`: audit intents, redacted structured evidence, immutable-archive delivery/integrity status.
- `Outbox`: transactional messages, consumer receipts, retries, dead-letter escalation.
- `ReadModels`: explicitly non-authoritative projections for UI/search.

Dependency direction is:

```text
HTTP/IPC adapters -> Application use cases -> Domain
Infrastructure -> implements application/domain ports
Read-model projectors -> consume committed events

Admin BFF -> application ports and redacted read models only
StrategyHost -> strategy abstractions only
Supervisor -> runtime/risk/trading abstractions; never vendor adapter
GatewayHost -> trading abstractions -> MT5 vendor adapter
```

Domain projects reference neither Entity Framework/Npgsql nor HTTP, vault, message-bus, filesystem, cloud SDK, or vendor-gateway packages. A module cannot query another module's tables directly. Cross-module changes go through an application port in the same transaction where ownership permits, or through a versioned outbox event. Read models never authorize mutations.

## 3. PostgreSQL foundation

### 3.1 Required topology and conventions

- Use supported PostgreSQL with TLS, managed backups, point-in-time recovery, multi-zone deployment for production, and restore exercises. SQLite and EF in-memory providers are prohibited for runtime and integration acceptance.
- Use Npgsql and explicit migrations. Migrations are forward-compatible with the currently deployed application, have a reviewed rollback/forward-fix plan, and never create fake business data.
- Use separate schemas such as `identity`, `authorization`, `control`, `operations`, `governance`, `audit`, `messaging`, and `readmodel`. Schema ownership is assigned to migration roles; runtime roles receive only required DML/execute permissions.
- Primary identifiers are UUIDv7/ULID-like opaque values generated server-side. Human-search keys are separate and indexed. Opaque IDs do not replace authorization.
- All authoritative mutable aggregates have `version bigint`, `created_at timestamptz`, and `updated_at timestamptz`. Store UTC instants only. Monetary values, if later added, use exact decimals and immutable ledger entries.
- State values are constrained by checked text/enums; hashes and idempotency keys have unique constraints; foreign keys are not omitted for convenience.
- Secrets are not columns. A broker account stores only an opaque credential reference, secret state, masked metadata, and binding fingerprint.
- PostgreSQL roles used by applications have no `BYPASSRLS`, schema-owner, superuser, replication, extension-installation, or DDL privileges.

### 3.2 Tenant isolation

Every user-owned row carries a non-null `tenant_id`. Tenant context comes from the validated user session/worker credential; request bodies, query strings, and route values may not select or override it.

Enforce tenant ownership twice:

1. Application repositories require an explicit trusted `TenantContext` and include `tenant_id` in every key lookup, join, update, and uniqueness constraint.
2. PostgreSQL row-level security provides defense in depth. At the beginning of each transaction, the data adapter uses `SET LOCAL app.tenant_id = ...` and an allowlisted actor kind. Policies reject missing context. Pooling tests prove context is transaction-scoped and cannot bleed into the next request.

Admin access does not disable RLS globally. Admin read models contain pre-redacted operational fields, and authoritative cross-tenant access uses narrowly scoped security-definer functions or a dedicated role mapped to a permission/scope decision and audited purpose. Worker credentials are restricted to one account/deployment/generation. Background jobs carry an explicit system purpose and bounded shard/scope rather than an all-tenant repository switch.

Cross-tenant resource probing returns the same `404 RESOURCE_NOT_FOUND` contract as a nonexistent ID. Tenant IDs, email normalization, masked broker login, source object keys, and vault references are never trusted merely because the caller supplied them.

### 3.3 Real-data rule

Production migrations create schema and static code tables only. Any broker, strategy, gateway, user, or deployment must arrive through an authenticated workflow and immutable evidence. Tests provision PostgreSQL through a container or dedicated disposable database, run the exact migrations, and delete the database afterward. Test fixture builders are compiled into test projects only.

## 4. Identity, sessions, and MFA

### 4.1 User identity

- During U0/V1A, users are invited/allowlisted; public registration is not exposed.
- When password authentication is enabled, hash with current-policy Argon2id parameters, a per-user salt, and a separately managed pepper. Rehash after successful authentication when policy changes.
- Require verified email before broker-account onboarding. Use single-use, short-lived, hashed verification and recovery tokens; successful password reset revokes all refresh-token families unless a reviewed policy explicitly preserves a session.
- Access tokens are short lived (target 5–10 minutes), audience- and issuer-bound, and contain only stable identity/session identifiers and coarse assurance—not tenant permissions copied indefinitely.
- Refresh tokens are opaque, random, stored only as keyed hashes server-side, rotated on every use, and grouped in a token family. Reuse of an invalidated predecessor revokes the family and raises a security event.
- Bind a session to a registered device key when available. Store refresh tokens on Windows in Credential Manager/DPAPI, never the local database or logs.
- MFA is mandatory for cloud live eligibility and sensitive account/security changes; it is strongly encouraged for demo. Prefer WebAuthn/passkeys. TOTP may be a transitional fallback. Recovery codes are single-use and hashed. SMS is not an acceptable production-admin factor.
- Authentication, reset, verification, and MFA endpoints use per-account and per-network throttles without revealing account existence. Do not automatically unlock a security-locked user; release requires a verified recovery or authorized, audited manual review.

### 4.2 Staff identity

- Use a separate enterprise OIDC tenant/application and separate admin origin. No user identity token is accepted by the admin API.
- Production-capable staff require hardware-key/WebAuthn MFA, managed-device/conditional-access claims, a short admin session, and step-up authentication for approval, export, source access, role change, containment release, and production promotion.
- The Admin BFF uses `Secure`, `HttpOnly`, `SameSite=Strict` cookies, anti-CSRF tokens on mutations, strict origin checks, CSP, no tokens in browser local storage, and server-side session revocation.
- There is no standing Super Admin. Scoped assignments and production access expire. The requester cannot approve their own high-risk command.

### 4.3 Privileged infrastructure access

Portal authorization does not authorize cloud-console, database, container-host, backup, CI/CD, secret, or signing access. Govern those paths separately:

- no standing production access;
- JIT grant tied to a reason and incident/change ticket, with resource class, environment, scope, approver, start, and automatic expiry;
- hardware-key authentication through a managed bastion/access proxy;
- separate approvals for database, secret, signing, CI/CD, backup, and worker-host access;
- session recording and command capture for highly privileged sessions where legally permitted;
- export of grants, connections, and commands into the central evidence archive;
- immediate alerts for direct production access and periodic reconciliation against provider audit logs.

Application/runtime identities cannot mint JIT grants. Emergency portal authority cannot become infrastructure access.

## 5. Credential ingestion and vault separation

### 5.1 Cloud credential flow

```text
Desktop
  -> ControlPlane: request one-time ingestion session
  <- ingestion URL + single-use bearer bound to user/account/purpose/expiry
Desktop
  -> dedicated SecretIngestion origin: credential body
SecretIngestion
  -> Secret Broker/Vault: write-only create or rotate
  -> ControlPlane: signed completion containing opaque reference and state only
Assigned GatewayHost identity
  -> Secret Broker: short-lived authorized use/decryption
```

The ingestion grant expires within minutes, is stored hashed, is one-time, has strict byte/content limits, and is bound to tenant, broker-account draft, operation (`CREATE` or `ROTATE`), origin, and nonce. Consuming it and recording completion are idempotent. The secret body is never placed in a normal controller model, application database, queue, trace, exception, access log, analytics stream, APM span, crash dump, or support artifact. Responses never echo it.

Only the assigned cloud GatewayHost workload identity may request short-lived use of the actual credential. The secret broker verifies broker-account reference, deployment, worker identity, current generation, region, and credential state. Plaintext exists only in GatewayHost memory for the minimum connection operation and is cleared on best effort; memory isolation and dump restrictions are required.

### 5.2 Admin separation

The Admin BFF has no vault token, network route, SDK, or IAM permission. It reads a `SecretCredentialMetadata` projection containing only `exists`, state, last authorized worker use, rotation-request state, deletion state, and masked account binding. Admin commands are semantic operations such as:

- `REQUEST_USER_REAUTHENTICATION`;
- `DISABLE_CLOUD_USE`;
- `DELETE_CREDENTIAL_REFERENCE`.

Deletion is asynchronous and remains `DELETION_PENDING` until the vault/secret broker confirms it. No API retrieves or “shows” a password. Local credentials remain exclusively in the Windows-protected local vault and never use the cloud ingestion path.

## 6. Authorization and execution-safety policy

### 6.1 Authorization decision

Every request evaluates actor status, authentication assurance, tenant/resource ownership, permission, environment, resource scope, temporary-grant expiry, incident/case/change purpose, action risk, separation of duties, expected resource version, and current restrictive policies. Authorization is server-side at the application-use-case boundary and repeated by command consumers against authoritative state.

Never authorize from a UI button, role name alone, stale read model, client-provided tenant, email domain, masked account value, or resource identifier shape.

### 6.2 Policy-vector lattice

Containment is an immutable, signed `ExecutionSafetyPolicyVector`, not a single severity number:

```text
AllowNewDeployment
AllowStrategySignals
AllowExposureIncrease
AllowExposureReduction
AllowProtection
AllowPendingOrderCancellation
AllowEmergencyClose
LeaseMode                 NORMAL | RENEW_RESTRICTED | REVOKE
WorkerActions             set of DRAIN | FENCE | REPLACE | STOP_AFTER_FLAT
CredentialMode            NORMAL | DISABLE_NEW_USE | REVOKE_REFERENCE
PackageEligibility        ELIGIBLE | NO_NEW_ASSIGNMENT | QUARANTINED
```

Boolean permissions merge by logical AND (`false` wins). `LeaseMode`, `CredentialMode`, and `PackageEligibility` merge by their defined restrictive partial order. `WorkerActions` merge as a set, then a deterministic planner validates compatible sequencing. An incompatibility does not choose a numeric “winner”; it produces a fail-closed plan requiring reconciliation or review. For example, fencing a worker does not implicitly deny risk-reducing actions unless an alternative protected execution path is proven.

Effective policy is the meet of all applicable immutable vectors: baseline risk policy, environment, global, region, broker, gateway, runtime image, strategy/version, tenant/user, broker account, and deployment. Lower scopes may add restrictions but cannot weaken higher restrictions. Every decision stores applicable policy IDs/versions, the effective vector/hash, input snapshot hash, and rule results. Property tests must prove commutativity, associativity, idempotence, monotonic restriction, and scope-order independence.

Emergency Safety may only add restrictions. It cannot set a denied Boolean back to true, restore a credential, publish a package, or grant permission.

### 6.3 No automatic unlock or release

Policy expiry expires the operator's exceptional authority; it does not silently remove a safety restriction or resume exposure. Broad restrictions enter `EXPIRY_REVIEW_REQUIRED`. Release is a separate command with a fresh release preview, current incident/policy evaluation, independent approval where required, worker delivery, and broker reconciliation:

```text
ACTIVE -> EXPIRY_REVIEW_REQUIRED -> EXTENDED
                                -> RELEASE_APPROVED
                                -> DEACTIVATING
                                -> RECONCILING
                                -> INACTIVE
```

No deployment may increase exposure merely because a timestamp elapsed. The same no-auto-unlock principle applies to suspicious-user locks, artifact quarantine, credential disablement, and environment containment unless a narrowly defined recovery workflow explicitly proves release conditions.

## 7. Typed command lifecycle, targets, and compensation

### 7.1 Aggregate lifecycle

```text
REQUESTED
  -> POLICY_CHECKING
  -> WAITING_APPROVAL
  -> APPROVED
  -> SCHEDULED
  -> DISPATCHING
  -> PROPAGATING
  -> RECONCILING
  -> SUCCEEDED

Before any target dispatch: CANCELLED | REJECTED | EXPIRED
Any active execution state: PARTIAL | FAILED | UNKNOWN
After dispatch: COMPENSATION_REQUESTED
             -> COMPENSATING
             -> COMPENSATED | COMPENSATION_PARTIAL | COMPENSATION_FAILED
```

`SUCCEEDED` means every required target reached its command-specific terminal proof, which may require broker reconciliation. “Accepted,” “delivered,” and “acknowledged” are not synonyms for success.

Each command has immutable `CommandTarget` rows resolved from the approved impact snapshot. Target state is one of `PENDING_DISPATCH`, `DISPATCHED`, `DELIVERED`, `ACKNOWLEDGED`, `APPLIED`, `RECONCILING`, `RECONCILED`, `NOT_APPLICABLE`, `UNREACHABLE`, `FAILED`, or `UNKNOWN`. Store target resource/version, worker/generation, attempts, delivery/ack timestamps, observed result, broker-evidence reference, and last error code. Aggregate status is derived from required target states, never hand-edited.

Cancellation is accepted only when no target has been dispatched. After dispatch the API returns `409 COMMAND_ALREADY_DISPATCHED` with the allowed compensation command types. Compensation is a new immutable command linked to the original. It never erases effects or audit history. Examples include a governed containment release, restoring an approved prior policy, rescheduling a worker, or resuming a reconciled deployment. Some effects, such as credential destruction or evidence publication, are non-compensable and declare that fact before approval.

### 7.2 Impact preview and execution revalidation

An `ImpactPreview` contains:

```text
ScopeExpression
ResolvedTargetIdsOrSnapshotReference
TargetCount
ResourceVersionWatermark
PolicyVersion
CreatedAt
ExpiresAt
Digest
ImpactSummary (users/accounts/deployments/positions/regions/versions)
```

Approval binds the exact normalized command payload, target snapshot/digest, requester, reason/ticket, expected versions, policy version, and expiry. Immediately before dispatch, re-resolve scope against authoritative state and compare target IDs, target count, relevant resource versions, policy version, and safety impact. A material change rejects dispatch with `PREVIEW_STALE_REAPPROVAL_REQUIRED`, stores both previews, and requires a new approval. “Material” is command-specific and conservative; a larger or differently exposed target set is always material.

Emergency restrictive commands may use an independently calculated degraded preview when the main read model is unavailable. The response and evidence must label it degraded, identify missing dimensions, and restrict the permitted command/scope. A stale dashboard is never authoritative and never blocks the independent emergency service from applying a predefined narrower restriction.

## 8. Idempotency, concurrency, ordering, and outbox

### 8.1 HTTP idempotency

All mutations require `Idempotency-Key` (128 random bits minimum; maximum 200 characters) and a caller-visible `X-Correlation-Id` or server-generated equivalent. Store `(actor/tenant, operation, key)`, canonical request hash, response status/body reference, resource ID, and expiry.

- First request executes.
- Same key and same canonical payload returns the original result, including `202` command ID.
- Same key with a different payload returns `409 IDEMPOTENCY_KEY_REUSED`.
- Concurrent first requests serialize through a unique constraint; no check-then-insert race.
- Keys for security/admin commands and credential deletion are retained at least as long as the audit/command record; ordinary keys follow a documented retention policy.

Gateway/broker protocols may not honor YO4X keys. Persist a broker command as `READY_TO_SEND` before I/O. A timeout/crash after send becomes `UNKNOWN`; never blind-retry. Reconcile by request, order, deal, ticket, ownership, and broker history before deciding the next action.

### 8.2 Optimistic concurrency and ordering

Mutations require `If-Match`/expected aggregate version where a stale action could alter state. PostgreSQL updates use `WHERE id = ... AND version = expected`, increment the version, and return `409 RESOURCE_VERSION_CONFLICT` on zero rows. Do not use last-write-wins for security, deployment, policy, release, approval, or credential metadata.

Runtime commands are partitioned by broker account/deployment and carry a linearizable fence generation plus monotonic per-generation sequence. Consumers reject old generations, duplicate event IDs, and non-allowed sequence transitions. Approval/read-model freshness never substitutes for this ordering authority.

### 8.3 Atomic command, audit, and outbox

No sensitive mutation commits without its local durable audit intent. Core transactions are:

1. **Request transaction:** insert idempotency record, `AdminCommand`, policy evaluation, impact preview/snapshot, approval request/binding as applicable, `CommandAuditIntent`, and outbox messages; commit together.
2. **Approval transaction:** lock command/version, validate independent approver and assurance, insert append-only decision, transition command, append audit intent and outbox; commit together.
3. **Dispatch transaction:** revalidate preview/policy/versions, create/freeze targets, transition to `DISPATCHING`, append audit intent and dispatch outbox; commit together.
4. **Target-application transaction:** idempotently apply the target domain state change, append target result and audit intent, and add downstream outbox messages; commit together in the owning database boundary.

Do not hold a database transaction open across HTTP, broker, vault, message-bus, file, email, or immutable-archive calls. There is no distributed transaction claim. Outbox consumers use stable message IDs, `FOR UPDATE SKIP LOCKED` or equivalent claiming, bounded retry with jitter, inbox/deduplication records, and visible poison-message escalation. Publishing twice must be harmless.

The remote immutable evidence archive is asynchronous so emergency containment does not depend synchronously on it. The local command/audit/outbox transaction is mandatory; if it cannot commit, sensitive mutation fails closed. Archive delivery lag raises alerts and blocks non-emergency production mutations at a defined threshold without erasing already committed restrictive commands.

## 9. Initial REST contract

All JSON APIs are versioned, UTF-8, TLS-only, and return `application/problem+json` on errors. User, admin, secret-ingestion, worker, and emergency APIs use separate audiences and preferably separate origins. Route names express state transitions; security evidence is revoked/archived, never physically deleted through a generic `DELETE`.

### 9.1 Common/user foundation (A0/U0; allowlisted users only)

```text
GET  /v1/me
GET  /v1/me/sessions
POST /v1/me/sessions/{sessionId}/revoke
POST /v1/auth/refresh
POST /v1/auth/logout
POST /v1/auth/mfa/challenge

POST /v1/cloud-credential-ingestion-sessions
GET  /v1/broker-accounts/{brokerAccountId}
GET  /v1/broker-accounts/{brokerAccountId}/credential-state
POST /v1/broker-accounts/{brokerAccountId}/cloud-connection-tests
POST /v1/broker-accounts/{brokerAccountId}/credential-rotation-sessions
POST /v1/broker-accounts/{brokerAccountId}/disable-cloud-use
POST /v1/broker-accounts/{brokerAccountId}/credential-deletion-requests

POST /v1/deployments/validate
POST /v1/deployments
GET  /v1/deployments/{deploymentId}
POST /v1/deployments/{deploymentId}/start
POST /v1/deployments/{deploymentId}/close-only
POST /v1/deployments/{deploymentId}/stop-after-flat
GET  /v1/deployments/{deploymentId}/activity
```

`start` remains demo-only in U0/V1A and requires the one approved broker, server, dedicated hedging demo account, strategy version, gateway digest, region, broker-hosted protection, risk policy, and validation evidence. Public signup, live activation, local execution, arbitrary uploads, resume after containment, and raw order endpoints do not exist in this stage.

### 9.2 Agent/runtime foundation (mTLS/workload identity, not user tokens)

```text
POST /internal/v1/workers/register
POST /internal/v1/workers/{workerId}/components/{component}/heartbeat
POST /internal/v1/execution-leases/issue
POST /internal/v1/execution-leases/renew
POST /internal/v1/deployments/{deploymentId}/events
POST /internal/v1/command-targets/{targetId}/delivery-events
POST /internal/v1/command-targets/{targetId}/reconciliation-results
```

The component value is allowlisted to `SUPERVISOR`, `STRATEGY_HOST`, or `GATEWAY_HOST`. Worker registration binds workload identity, deployment/account, exact image/package/gateway hashes, region, and generation. Event endpoints validate event ID, generation, sequence, schema version, and account binding.

### 9.3 Reduced admin foundation (A0/A1)

```text
GET  /admin/v1/me
GET  /admin/v1/access/assignments
POST /admin/v1/access/assignments
POST /admin/v1/access/assignments/{assignmentId}/revoke
GET  /admin/v1/admin-sessions
POST /admin/v1/admin-sessions/{sessionId}/revoke

GET  /admin/v1/approvals
GET  /admin/v1/approvals/{approvalId}
POST /admin/v1/approvals/{approvalId}/approve
POST /admin/v1/approvals/{approvalId}/reject
GET  /admin/v1/commands/{commandId}
GET  /admin/v1/commands/{commandId}/targets
POST /admin/v1/commands/{commandId}/cancel
POST /admin/v1/commands/{commandId}/compensations

POST /admin/v1/user-searches
GET  /admin/v1/users/{userId}
GET  /admin/v1/deployments/{deploymentId}
GET  /admin/v1/fleet/workers/{workerId}
GET  /admin/v1/broker-commands/unknown
GET  /admin/v1/support-cases
POST /admin/v1/support-cases
GET  /admin/v1/support-cases/{caseId}
POST /admin/v1/support-cases/{caseId}/notes
```

`POST /user-searches` requires purpose, case/incident reference where applicable, explicit search type, exact or narrowly bounded criteria, result limit, and reason. It returns a redacted result set and audit receipt. There is no broad enumerable `GET /users`.

### 9.4 Gateway governance before runtime containment (A2)

```text
GET  /admin/v1/brokers
POST /admin/v1/brokers/{brokerId}/test-runs
GET  /admin/v1/gateway-artifacts
POST /admin/v1/gateway-artifacts/register-quarantined-reference
GET  /admin/v1/gateway-artifacts/{artifactId}/evidence
POST /admin/v1/gateway-artifacts/{artifactId}/approve-demo-canary
POST /admin/v1/gateway-artifacts/{artifactId}/revoke
POST /admin/v1/releases/{releaseId}/promotion-previews
POST /admin/v1/releases/{releaseId}/promotions
POST /admin/v1/releases/{releaseId}/rollback
```

The admin browser never uploads a production binary. Artifact registration accepts an immutable quarantine object reference created by the controlled intake pipeline, expected SHA-256, size, vendor identity, and provenance reference. Promotion requires target environment, exact approved artifact digest, evidence digest, preview digest, expected release version, and approval binding; it cannot mean “promote latest.”

### 9.5 Runtime containment (A3/U0)

```text
POST /admin/v1/deployments/{deploymentId}/close-only
POST /admin/v1/deployments/{deploymentId}/stop-after-flat
POST /admin/v1/deployments/{deploymentId}/revoke-lease
POST /admin/v1/deployments/{deploymentId}/replace-worker
POST /admin/v1/kill-switches/previews
POST /admin/v1/kill-switches
POST /admin/v1/kill-switches/{switchId}/extensions
POST /admin/v1/kill-switches/{switchId}/release-previews
POST /admin/v1/kill-switches/{switchId}/release-requests
```

There is no simple `deactivate` call. Release is previewed, approved, dispatched, and reconciled because it may restore exposure. Worker replacement returns a command ID and target/fencing evidence, never an immediate success Boolean.

### 9.6 Safe deferred contracts

These routes are specified now to prevent unsafe shortcuts but are not part of the reduced MVP:

```text
POST /admin/v1/privacy-requests/{requestId}/processing-previews
POST /admin/v1/privacy-requests/{requestId}/approve-processing
POST /admin/v1/privacy-requests/{requestId}/process

POST /admin/v1/source-access-requests
POST /admin/v1/source-access-requests/{requestId}/approve
POST /admin/v1/source-view-sessions
GET  /admin/v1/source-view-sessions/{sessionId}/files/{fileId}/view
```

Privacy processing is preview → independent approval where policy requires → asynchronous process → quality check → completion evidence, with legal holds enforced. It is never one broad `execute` endpoint. Source access uses the dedicated viewer described in section 14, not object-store URLs or downloads.

### 9.7 Independent emergency API

```text
POST /emergency/v1/restrictive-command-previews
POST /emergency/v1/restrictive-commands
GET  /emergency/v1/restrictive-commands/{commandId}
GET  /emergency/v1/restrictive-commands/{commandId}/targets
```

The command type is an allowlisted restrictive template only: block new exposure, block new deployments, close-only for a defined scope, quarantine an exact gateway digest, or revoke/fence a cloud worker assignment. Release, permission changes, code promotion, secret operations, arbitrary scope languages, and raw broker actions are absent.

## 10. Validation and error contract

Use allowlisted request DTOs; never bind persistence entities. Reject unknown security-sensitive enum values and unexpected properties on command/policy/credential DTOs. Apply endpoint-specific body/file limits, bounded strings/collections, Unicode normalization where identifiers require it, invariant numeric parsing, and strict URI/object-reference schemes. SQL is always parameterized. External URLs are not fetched without SSRF-safe allowlisting and egress policy.

Problem response:

```json
{
  "type": "https://errors.yo4x.example/resource-version-conflict",
  "title": "The resource changed before this command was applied.",
  "status": 409,
  "code": "RESOURCE_VERSION_CONFLICT",
  "correlationId": "01...",
  "errors": [
    { "path": "/expectedVersion", "code": "STALE", "message": "Refresh and preview the action again." }
  ]
}
```

The `type` base is configuration, not a hard-coded production hostname. Messages never include SQL, stack traces, token contents, passwords, vault references, raw source, internal paths, broker responses containing credentials, or existence hints across tenants.

Status conventions:

- `400`: malformed JSON/header/query or syntactic validation.
- `401`: missing/invalid/expired authentication; no account-existence disclosure.
- `403`: authenticated actor lacks a known global permission or assurance requirement.
- `404`: resource absent or outside tenant/scope.
- `409`: idempotency mismatch, expected-version conflict, invalid state transition, stale preview, already-dispatched cancellation, or duplicate active binding.
- `422`: well-formed command fails domain/safety validation.
- `428`: required `If-Match`, idempotency, reason, or preview precondition missing.
- `429`: throttled; include bounded `Retry-After`.
- `503`: service cannot safely accept the operation; retryability is explicit.

Successful long-running mutations return `202 Accepted` with command ID, status URL, submitted aggregate version, and correlation ID. Do not return `200 success` for propagation or broker reconciliation that has not happened.

## 11. Transaction boundaries and domain invariants

Use one PostgreSQL transaction per aggregate command plus its audit/outbox records. Isolation defaults to `READ COMMITTED` with row-version predicates; use row locks or `SERIALIZABLE` only for proven invariants such as ownership generation issuance, one active deployment per broker account, token-family rotation, and one-time grant consumption. Retry serialization/deadlock failures only before any external side effect and with the same idempotency record.

Required atomic invariants include:

- session refresh rotation + predecessor invalidation + security audit;
- one-time ingestion grant consumption + opaque secret completion metadata (never plaintext);
- one active deployment/worker ownership record per broker account in V1A;
- generation G+1 issuance only after G is invalid/released under the defined rule;
- deterministic strategy event N + state V+1 + requested actions + execution outbox;
- risk decision + normalized broker command `READY_TO_SEND` + audit/outbox before broker I/O;
- admin command/policy evaluation/approval binding/audit intent/outbox as described in section 8.3;
- containment policy activation/release state + effective-policy invalidation event + audit/outbox;
- release assignment to an exact digest + evidence/approval binding + audit/outbox;
- revocation as append-only event/status transition, never destructive deletion of evidence.

Broker, vault, email, object storage, immutable archive, and worker calls occur after commit through outbox delivery. Responses expose pending/unknown states honestly. A compensating workflow repairs cross-boundary failure; code must not attempt an unbounded synchronous rollback across systems.

## 12. Cloud fencing and replacement

A higher generation number alone is not sufficient evidence. Replacing a cloud worker executes and records all applicable controls:

1. atomically revoke the old workload assignment/lease in the linearizable ownership store;
2. revoke or disable the old workload identity;
3. deny old GatewayHost access at the secret broker;
4. apply network policy blocking its broker/control-plane egress;
5. terminate or quarantine its process/container and node assignment;
6. invalidate its command/IPC channel and reject its generation at receivers;
7. attempt broker-session disconnect where supported, without claiming success if unconfirmed;
8. wait for evidence that the previous generation is invalid plus the clock-skew/safety interval;
9. start the replacement in reconciliation-only mode with a new identity/generation;
10. reconcile positions, pending orders, deals, unknown commands, ownership, and protection;
11. permit new exposure only when the effective policy allows it and fencing evidence is sufficient.

Each step is a target result with timestamps and evidence. Failure produces `PARTIAL` or `UNKNOWN`, keeps the replacement reconciliation-only, and blocks new exposure. Local workers receive cooperative lease/policy controls only; the API reports `YO4X_AUTHORIZATION_EXPIRED`, `OFFICIAL_WORKER_UNREACHABLE`, `BROKER_STATE_UNKNOWN`, and `HARD_BROKER_STOP_NOT_CONFIRMED` where appropriate.

## 13. Health, readiness, and degradation

Each process exposes separate endpoints on an infrastructure-only listener:

- `/health/live`: process event loop is responsive. It does not query PostgreSQL, vault, broker, or message bus.
- `/health/startup`: required configuration loaded, migrations compatible, cryptographic material/identity available, and process-specific static checks passed.
- `/health/ready`: the process can safely accept its next unit of work.

Readiness is role-specific:

- Control/Admin APIs require PostgreSQL read/write and ability to atomically append audit/outbox. Admin readiness never probes the vault.
- Secret ingestion requires the grant store and write-only secret-broker/vault path; if either is unavailable it refuses new credential bodies.
- Outbox workers require PostgreSQL and their destination; they stop claiming new work when the destination is unavailable but preserve existing rows.
- Supervisor readiness requires valid assignment/lease, journal, current effective policy, component IPC, and sufficient reconciled broker state for the claimed action class.
- StrategyHost readiness never depends on credentials or broker network.
- GatewayHost readiness requires approved artifact hash, workload identity, allowlisted egress, and broker connection for trading readiness; connection loss blocks increased exposure.
- Emergency Safety readiness is tested against its independent identity/policy store and multi-region durable command publication, not the normal Admin BFF.

Do not place a process in a restart loop merely because a downstream broker is unavailable. Report degraded/not-trading-ready separately from liveness. Public health responses contain only status and correlation; versions, topology, database/vault/broker names, exception text, and counts are available only through authenticated operations telemetry. Read-model lag and audit-archive lag are explicit metrics, not hidden behind green readiness.

## 14. Audit, redaction, source viewing, and emergency independence

### 14.1 Audit/evidence

Audit at creation time, not only at display:

- authentication, MFA, recovery, session/device revocation;
- permission, temporary/JIT grant, access review, break-glass use;
- purpose-bound search, sensitive read, export, support/source access;
- credential ingestion session creation/consumption metadata, authorized worker use, disable/delete state (never secret value);
- command request, preview, policy evaluation, approval, target delivery/result, reconciliation, cancellation, compensation;
- deployment, worker, generation, gateway, release, quarantine, containment, privacy, and incident actions.

Each event has stable event ID, actor/session/device/assurance, tenant or admin scope, action, target, purpose/reason/ticket, correlation/causation IDs, policy/effective-vector version, redacted before/after hashes or values, result, source IP classification, and UTC time. Sensitive reads record fields/categories accessed, not their secret contents.

Redact or omit passwords, tokens, cookies, authorization headers, MFA seeds/recovery codes, private keys, credential/vault references, raw MQ5/MQH source, complete broker login, user-provided secret-like strings, signed upload URLs, and raw vendor/broker payloads. Use allowlisted structured log fields. Redaction tests cover logs, traces, metrics labels, exceptions, problem responses, audit, exports, crash reports, and support bundles.

Deliver redacted evidence through the transactional outbox to an independently controlled archive account with retention lock/write-once controls, signed or hash-chained batches, redundant copies, periodic integrity verification, independently audited access, and no application delete permission. Legal holds and retention are policy-driven; developers do not invent periods.

### 14.2 Secure private-source viewer (deferred UI, fixed boundary)

Private source is never served through a general object-storage link or downloaded by the Admin BFF. An approved, case-bound, file-scoped, short-lived `TemporaryAccessGrant` with step-up MFA and separate approver creates a dedicated source-view session. The viewer performs short-lived server-side decryption, exposes no object-store credential to the browser, disables application caching/download/print where practical, watermarks each view with actor/case/time, audits every file open, and terminates automatically.

These controls provide narrow authorization, deterrence, attribution, and evidence; they cannot truthfully guarantee that a staff member will never photograph or manually copy visible source. The viewer is deferred beyond the reduced MVP. Until it exists, private source access is not available through admin tooling.

### 14.3 Independent Emergency Safety Control Service

Run the emergency service in a separate failure domain/origin with its own minimal deployment, hardware-key authentication, narrowly scoped authorization, immutable template catalogue, local audit/outbox transaction, and multi-region durable publication. It shares the same policy-vector semantics and command evidence format but not the Admin Web/BFF availability dependency.

It can only add predefined restrictions. It cannot access databases interactively, publish code, reveal/delete secrets, submit orders, expand permissions, release containment, or accept arbitrary scripts/scopes. Test it during normal-admin outage, primary-region outage, read-model outage, and evidence-archive delay. Any later containment release uses the normal governed release workflow or a separately approved recovery service—not automatic expiry.

## 15. Minimum integration and security acceptance tests

All tests that exercise persistence run against the real PostgreSQL engine with production-like migrations and constraints. Passing unit tests alone is insufficient.

### 15.1 Database, tenancy, and API

- Fresh database migrates from zero; restart creates no mock business rows; previous compatible application version works during rollout.
- RLS and repository filters deny cross-tenant read/update/delete for every tenant entity, including guessed IDs, joins, exports, and object references.
- Connection-pool reuse cannot leak `app.tenant_id`; missing tenant context fails closed.
- Admin scoped read succeeds only for the authorized purpose/scope and emits sensitive-read evidence; broad user enumeration is impossible.
- DTO over-posting, unknown enums, mass assignment, malformed JSON, oversized bodies, injection, SSRF, XSS, CSRF, and CSV formula payloads fail safely.
- Problem responses and `404` behavior do not reveal tenant/resource/account existence or secrets.

### 15.2 Identity and privileged access

- Refresh rotation is atomic; replay revokes the family; concurrent refresh has one winner; logout/revoke takes effect.
- MFA assurance, session age, managed-device, expired grant, environment scope, and requester/approver separation are enforced server-side.
- Password/recovery flows resist enumeration and throttling bypass; password reset and suspicious-lock release follow policy with no automatic unlock.
- JIT infrastructure grant requires hardware key, ticket, independent approval, bounded scope/expiry; expiry removes access and provider audit reconciliation detects bypass.

### 15.3 Credential boundary

- Normal and admin APIs have no vault route/permission and cannot deserialize a password field.
- Ingestion grants are tenant/account/purpose-bound, single-use, short-lived, hashed, idempotent, and reject replay/cross-binding.
- Credential bodies are absent from PostgreSQL, queues, logs, traces, metrics, errors, crash dumps, audit, and support bundles.
- Only the assigned current-generation GatewayHost identity can use the secret; wrong deployment/region/generation/component is denied and audited.
- Disable/delete/rotation are asynchronous, idempotent, and never return plaintext; vault outage leaves truthful pending state.

### 15.4 Commands, policy, and audit

- Duplicate same-key requests return one command; changed payload with same key conflicts; concurrent duplicates create one row/outbox set.
- Command, policy evaluation, approval binding, audit intent, and outbox survive crash atomically—either all commit or none do.
- Requester cannot self-approve; edited payload, expired approval, assurance downgrade, or digest mismatch invalidates approval.
- Preview revalidation rejects materially changed target sets/versions/policies and records old/new snapshots.
- Cancellation succeeds only before dispatch; after dispatch it requires a typed compensation; irreversible commands declare non-compensability.
- Target delivery/ack/application/reconciliation states aggregate correctly to propagating, partial, unknown, or success; no early success claim.
- Policy lattice property tests prove AND/restrictive merge laws and scenarios combining block deployment, close-only, reduction/protection, drain, lease revoke, credential disable, and package quarantine.
- Expiry never re-enables exposure. Release requires preview, approval, policy/incident recheck, propagation, and reconciliation.
- Audit archive delivery can retry/duplicate safely; tamper/integrity verification detects mutation; sensitive mutation fails closed if local audit/outbox cannot commit.

### 15.5 Runtime and broker safety

- StrategyHost cannot access credentials, vendor DLL, broker/control network, unrestricted filesystem, native code, process creation, or raw broker commands.
- Only GatewayHost loads the allowlisted exact gateway digest; unexpected network destinations are denied.
- Killing Supervisor, StrategyHost, or GatewayHost before/after event commit and broker send releases no partial strategy action and causes no blind retry.
- `READY_TO_SEND` crash, lost acknowledgement, timeout, partial fill, duplicate/reordered broker event, and restart produce broker-reconciled results or visible `UNKNOWN`.
- Worker replacement proves identity revocation, secret denial, egress block, process isolation/termination, channel invalidation, generation expiry, reconciliation-only start, and broker reconciliation before exposure.
- Failure of any fencing step holds new exposure and reports partial/unknown evidence. Local status wording never claims hard broker stop without confirmation.
- Effective risk decision derives exposure classification independently and pins policy/input hashes; dedicated hedging demo-account restrictions, broker-hosted SL/TP, freshness, no manual/external trades, and one deployment/account are enforced.

### 15.6 Governance, privacy, source, emergency, and health

- Admin browser can register only a quarantined artifact reference; exact hash/evidence approval is required before demo canary; promotion is bound to target environment and digest.
- Revocation preserves immutable assignment/session/artifact history; no security endpoint physically deletes evidence.
- Privacy process enforces preview, approval, legal hold, async processing, quality check, and completion evidence.
- Source viewer exposes no object-store credential, watermarks/audits every view, honors file scope/expiry, and is unavailable until the dedicated service exists.
- Emergency service applies predefined restrictions while Admin BFF, normal read models, or the primary control-plane region are unavailable; it cannot release restrictions or reach secrets/code/orders.
- Liveness stays healthy during downstream outages; readiness fails only for process-specific unsafe dependencies; no endpoint leaks topology or secret details.

## 16. P0 implementation mistakes to avoid

Any of the following blocks U0/V1A exit:

1. Using SQLite, EF in-memory, or production mock/seed data to claim backend readiness.
2. Treating an interface or assembly as the isolation boundary instead of separate Supervisor, StrategyHost, and GatewayHost processes/containers.
3. Letting StrategyHost reference the vendor gateway, credentials, network, native code, or raw broker commands.
4. Passing cloud broker passwords through the normal Control Plane/Admin BFF, database, queues, logs, analytics, or support systems.
5. Giving the Admin BFF any vault route or permission, even “metadata-only”; metadata comes from a separate read model.
6. Authorizing by role name, UI visibility, client tenant ID, guessed opaque ID, or stale read model without authoritative scope and tenant checks.
7. Disabling PostgreSQL RLS with an all-powerful application/admin role or leaking tenant session variables through pooling.
8. Reporting an async command successful at acceptance, delivery, or acknowledgement instead of target-level reconciliation.
9. Allowing cancellation after dispatch without a separate compensation command and immutable history.
10. Treating kill-switch effects as a single severity ordering instead of a tested policy-vector lattice.
11. Automatically removing containment, unlocking accounts, restoring credentials/packages, or resuming exposure when a timer expires.
12. Reusing an approved preview after target population, resource version, policy, environment, or impact changes.
13. Issuing a new cloud worker generation without revoking old identity/secret/network/process/channel authority and reconciling broker state.
14. Claiming YO4X lease/fence tokens hard-stop a modified local client or credentials used outside YO4X.
15. Blindly retrying a broker command after timeout/crash or claiming mathematically exactly-once broker execution.
16. Committing a sensitive state change without its audit intent and outbox in the same local PostgreSQL transaction.
17. Making emergency restriction depend on the normal Admin Web/BFF, primary-region read model, or synchronous remote evidence archive.
18. Serving private MQ5/MQH through object-storage links, ordinary downloads, support attachments, or a general admin page.
19. Using destructive `DELETE`, broad user enumeration, direct privacy “execute,” unbound “promote latest,” or simple kill-switch deactivate semantics.
20. Building runtime containment before gateway rights, exact artifact provenance, network/credential-path evidence, compatibility tests, and rollback/revocation governance.
21. Enabling live accounts, local execution, public registration, general MQ5 uploads, netting, manual/external trades, virtual stops, or multiple active strategies during U0/V1A.

## 17. Build completion and assumptions

The foundation is build-complete only when migrations, modules, endpoints, authorization matrix, target-level command lifecycle, policy lattice, secret boundary, audit/outbox, health behavior, and the minimum acceptance suite are implemented and evidenced on real PostgreSQL. That completion does not authorize live trading.

Assumptions requiring explicit confirmation before their dependent work:

- hosting/cloud provider, regions, managed PostgreSQL, message transport, secret broker/vault, KMS/HSM, and immutable archive products are not yet selected;
- staff identity provider, managed-device platform, JIT access proxy, and session-recording policy are not yet selected;
- written gateway local/cloud/SaaS/redistribution rights and proof that the exact artifact is production-approved remain external gates;
- the representative MQ5/MQH/SET package, first numeric risk/freshness policy, and broker capability evidence are still required by U0;
- legal retention, privacy jurisdictions, data residency, and source-viewing employment/privacy controls require legal approval;
- U0 uses one allowlisted broker/server, one exact gateway digest, one region, one manually reviewed strategy path, and one dedicated hedging demo account with broker-hosted protection;
- the chosen message bus and secret system do not change the PostgreSQL atomic-outbox boundary or permit secret values in events;
- local execution remains deferred and is always described as cooperative on a user-controlled host.

Any assumption change must preserve tenant isolation, no-secret visibility, target-level truthfulness, monotonic containment, broker reconciliation, three-process runtime isolation, and gateway-first governance.
