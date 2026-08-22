# YO4X Admin-Side Architecture

**Status:** Target-state architecture; only the minimum Phase U0/V1A operational slice is authorized first  
**Scope:** Internal YO4X administration, operations, support, strategy approval, cloud control, billing operations, security, privacy, and audit  
**Companion document:** [YO4X User-Side Architecture](./USER_SIDE_ARCHITECTURE.md)  
**Active execution plan:** [YO4X Phase U0 Execution Plan](./PHASE_U0_EXECUTION_PLAN.md)  

## 1. Executive decision

The YO4X admin side will be a private web application for authorized YO4X staff. It will use a separate admin API and a separate administrator identity system.

The admin side controls the platform, but it is not a trading terminal. It must never:

- Display or export an MT5 trading password.
- Send a raw broker order from an admin browser.
- Edit production data directly in the database.
- Change a signed strategy package after publication.
- Run uploaded MQ5, EX5, DLL, or generated arbitrary C# code.
- Let one employee silently publish a live strategy or hide an audit event.

Administrative actions become typed commands. The backend validates authorization, scope, approval, reason, current version, and safety policy before executing them.

High-risk controls use two-person approval. Emergency containment can be fast, but it must be narrow, time-limited, visible, and audited.

The admin product will start as:

- A React and TypeScript internal web portal.
- A separate ASP.NET Core Admin API/BFF.
- Shared YO4X application/domain services, not direct database writes.
- Enterprise SSO with phishing-resistant MFA.
- Role- and scope-based access control.
- An immutable audit/evidence pipeline.

Production live trading remains blocked until the gates in the user-side architecture are passed.

### 1.1 Admin production gates

The admin portal cannot control production or live deployments until all of these pass:

1. Separate staff SSO, strong MFA, scoped authorization, and staff offboarding are tested.
2. Every sensitive read and mutation reaches the immutable audit archive.
3. Two-person approval and separation-of-duty rules are proven server-side.
4. Kill-switch impact preview, target propagation, expiry review, governed release, and reconciliation are failure-tested without unsafe automatic unlock.
5. Admin UI, API, logs, exports, and support tools are proven unable to reveal broker passwords.
6. Strategy and gateway publication require exact hashes, validation evidence, signed artifacts, canary, and rollback.
7. Private-source access, support impersonation limits, privacy, and retention controls are approved.
8. Billing expiry and dispute behavior cannot cause unsafe position handling.
9. Incident, broker outage, duplicate-order, region-loss, and key-compromise runbooks are exercised.
10. Independent security review, backup restore, access review, and controlled demo soak pass.
11. Command, policy evaluation, audit intent, and outbox persistence are atomic and failure-tested.
12. Privileged infrastructure access is just-in-time, separately approved, recorded, and archived.
13. The restrictive Emergency Safety Control service works independently of the normal Admin Web/BFF path.

## 2. Admin architecture principles

1. **Separate control from execution:** The admin portal changes policy and lifecycle state. Only fenced trading workers communicate with brokers.
2. **No secret visibility:** Admin staff can see credential status and masked account metadata, never plaintext broker credentials.
3. **Least privilege:** Access is granted by job role, environment, region, and resource scope for the shortest useful period.
4. **Two-person control:** The requester cannot approve their own high-risk action.
5. **Typed commands only:** No generic SQL console, arbitrary script box, or unrestricted production shell in the portal.
6. **Immutable releases:** Published strategy, converter, runtime, gateway, and policy versions are never edited in place.
7. **Fail safe:** Uncertain state blocks new exposure while preserving approved protection and reduction behavior.
8. **Impact before action:** A dangerous command shows affected users, deployments, accounts, positions, regions, and versions before confirmation.
9. **Evidence by default:** Every view and command has actor, time, reason, ticket, correlation ID, and redacted before/after state.
10. **Private source stays private:** User MQ5/MQH source is inaccessible to normal support and operations roles.
11. **One source of truth:** Admin and user APIs use the same domain rules and state machines.
12. **No hidden dependencies:** Gateway artifacts keep vendor identity, license evidence, hashes, SBOM, test evidence, and rollout history.
13. **Reversible where possible:** Suspend, drain, close-only, and rollback are preferred to destructive deletion.
14. **Honest status:** Admin dashboards separate requested, accepted, propagated, broker-confirmed, and uncertain states.

## 3. Scope

### 3.1 Target admin scope across staged delivery

- Secure staff sign-in, MFA, sessions, roles, scopes, and access reviews.
- System-health and safety dashboard.
- User lookup, account security, sessions, consents, and support cases.
- Catalogue strategy intake, review, validation, publication, suspension, and retirement.
- Private strategy conversion operations without general source access.
- Demo and live eligibility approval.
- Deployment, worker, broker, and cloud-fleet monitoring.
- Scoped kill switches and execution-lease revocation.
- Gateway artifact approval, compatibility testing, rollout, and rollback.
- Plans, subscriptions, invoices, usage, refunds, disputes, and entitlement projections.
- Privacy export/deletion and rights/takedown workflows.
- Incidents, maintenance, announcements, and user notifications.
- Immutable administrative, security, release, and trading-control audit evidence.
- Feature flags and versioned operational policies.

### 3.2 Not included in the staged admin roadmap

- Broker Manager API functions.
- Direct manual trading for a user.
- Custody, deposits, withdrawals, or movement of user money.
- Public marketplace and author revenue sharing.
- Automatic approval of arbitrary MQ5 source.
- Support-agent access to broker passwords or cloud-vault plaintext.
- Silent user impersonation.
- Editing a user's strategy inputs while a deployment is running.
- A universal force-close button.
- Arbitrary database editing or production scripting through the portal.

## 4. Roles and separation of duties

Access is permission-based. Role names are convenient bundles, not hard-coded authorization shortcuts.

| Role | Main responsibility | Explicit limits |
|---|---|---|
| Customer Support | Accounts, sessions, common product issues, cases | No source, secrets, publishing, billing adjustment, or runtime policy |
| Strategy Analyst | Compatibility findings and functional strategy review | Cannot approve own build for live use or operate production workers |
| Risk Reviewer | Risk behavior, exposure rules, demo/live evidence | Cannot build/sign the package being approved |
| Trading Operations | Deployment health, brokers, leases, scoped containment | Cannot view passwords or publish unreviewed strategy versions |
| Cloud Operations | Worker fleet, regions, capacity, rollout, drain/replace | No user source, billing, or strategy business approval |
| Release Manager | Approved releases and progressive rollout | Cannot generate or export production signing keys |
| Finance Operations | Subscriptions, invoices, refunds, disputes | Cannot control trades, workers, or strategy approval |
| Security Operations | Sessions, access, threats, break-glass containment | Source/secret access remains separately controlled |
| Privacy Officer | Export, deletion, retention hold, rights requests | No trading controls or billing changes beyond case evidence |
| Auditor | Read-only audit and evidence review | No mutations or secret/source download |
| Platform Owner | Policy ownership and final business approvals | Still subject to two-person approval and audit |

There is no normal, unlimited Super Admin role. A break-glass identity exists only for a documented emergency. It is disabled by default, hardware-key protected, time-limited, heavily alerted, and reviewed after use.

### 4.1 Permission model

Each authorization decision evaluates:

- Actor identity and active staff status.
- Permission, such as strategy.publish.live.
- Environment: development, demo, pilot, or production.
- Scope: global, region, broker, strategy, version, user, or deployment.
- Current MFA strength and session age.
- Requested command risk level.
- Whether independent approval is required.
- Temporary-access expiry.
- Conflict-of-interest and separation-of-duty rules.

Role changes, production access, and source-access grants expire automatically unless reviewed and renewed.

### 4.2 Privileged infrastructure access

Admin-portal RBAC does not govern direct cloud, database, container, backup, CI/CD, signing, or host access. Those paths use a separate privileged-access architecture:

- No standing production access.
- Just-in-time, task-scoped grants with automatic expiry.
- Hardware-key authentication and managed-device checks.
- Managed bastion/access proxy; no direct public management endpoints.
- Incident/change ticket and written reason.
- Independent approval for database, vault, signing, CI/CD, worker-host, and backup scopes.
- Session recording or command capture for highly privileged access.
- Direct-access alerts and export to the central evidence archive.
- Separate workload/service identities; no shared human accounts.
- Break-glass grants reviewed after use and followed by credential rotation when required.

Portal restrictions do not replace cloud-provider IAM, database roles, Kubernetes/container authorization, operating-system policy, or CI/CD governance.

## 5. System context

~~~mermaid
flowchart LR
    Staff[Authorized YO4X Staff] --> AdminWeb[YO4X Admin Web]
    AdminWeb --> WAF[Private Access Gateway / WAF]
    WAF --> AdminBFF[Admin API / BFF]
    AdminBFF --> AdminIdentity[Admin SSO and MFA]
    AdminBFF --> Policy[Authorization and Approval Policy]
    AdminBFF --> Cases[Support and Case Module]
    AdminBFF --> StrategyOps[Strategy and Conversion Operations]
    AdminBFF --> RuntimeOps[Runtime and Fleet Operations]
    AdminBFF --> Finance[Billing Operations]
    AdminBFF --> Privacy[Privacy Operations]
    AdminBFF --> Audit[Immutable Audit Pipeline]
    AdminBFF --> SecretMetadata[Secret Metadata Read Model]
    StrategyOps --> ControlPlane[YO4X Control Plane Services]
    RuntimeOps --> ControlPlane
    Finance --> ControlPlane
    Privacy --> ControlPlane
    ControlPlane --> CommandBus[Durable Command Bus]
    CommandBus --> Orchestrator[Deployment Orchestrator]
    Orchestrator --> CloudWorkers[Cloud Trading Workers]
    Orchestrator --> LocalLeases[Local Execution Leases]
    CloudWorkers --> Broker[MT5 Broker Servers]
    CloudWorkers --> Vault[Managed Credential Vault]
    Emergency[Independent Emergency Safety Control] --> CommandBus
    Emergency --> Audit
~~~

The Admin BFF has no vault token, route, or permission. It reads a redacted Secret Metadata Read Model containing only existence, credential state, last authorized worker use, and deletion state. Only an assigned GatewayHost workload identity reaches the credential-decryption boundary. Admin actions request reauthentication, disable cloud use, or delete a credential reference through typed control-plane commands.

## 6. Deployment topology

Use separate security boundaries for:

- Admin web origin.
- Admin API/BFF.
- User control-plane API.
- Conversion workers.
- Trading workers.
- Secret vault.
- Audit archive.
- Build and signing pipeline.
- Emergency Safety Control service and multi-region command publication.
- Privileged infrastructure access proxy/bastion.

Recommended topology:

~~~text
Internet or company device
  -> Zero Trust access gateway
  -> Admin web and Admin API
  -> application services and read models
  -> command bus
  -> existing control-plane orchestrators

Admin web has no route to broker networks.
Admin API has no permission to decrypt broker passwords.
Trading workers have no access to admin sessions or billing data.
Emergency control exposes predefined restrictive policies only.
~~~

The initial backend may remain a modular monolith, but conversion and trading workers remain isolated processes or containers. The admin portal must not create a second copy of domain logic.

## 7. Main admin components

### 7.1 YO4X Admin Web

Responsibilities:

- Task-oriented internal screens.
- Search, filtering, saved operational views, and exports with policy checks.
- Impact previews and before/after differences.
- Approval inbox and command progress.
- Real-time health streams with REST fallback.
- Strong confirmation for dangerous actions.

It stores no long-lived access token in browser local storage. Sessions use secure, HTTP-only, SameSite cookies through the Admin BFF.

### 7.2 Admin API/BFF

Responsibilities:

- Authenticate administrator sessions.
- Enforce permission, scope, step-up MFA, and approval policy.
- Build admin-specific read models from domain data.
- Accept typed, idempotent admin commands.
- Redact sensitive fields before they reach the browser.
- Write audit events before and after state changes.
- Correlate every action with a support case, incident, release, or reason.

The BFF calls application services. It does not modify control-plane tables directly.

### 7.3 Authorization and approval service

This module holds:

- Roles and permissions.
- Scoped and temporary assignments.
- Action risk classifications.
- Two-person approval rules.
- Step-up MFA rules.
- Break-glass rules.
- Access-review schedules.
- Policy versions and effective dates.

Approval decisions are server-side. Hiding a button is not authorization.

### 7.4 Admin command service

Every mutation is represented as an immutable request containing:

- Command type and requested scope.
- Actor, session, IP/device context, and MFA assurance.
- Reason code and written reason.
- Case, incident, or change-ticket reference.
- Idempotency key.
- Expected resource version for optimistic concurrency.
- Impact preview snapshot reference and digest.
- Required approval policy and approver decisions.
- Requested execution time and expiry.
- Result, partial failures, and correlation IDs.

Every command stores one `CommandTarget` per resolved deployment, worker, gateway, package, user, account, or region target. A target independently advances through dispatched, delivered, acknowledged, reconciled, unreachable, failed, or unknown state.

Impact previews contain:

- Scope expression.
- Resolved target IDs or immutable target snapshot reference.
- Target count and resource-version watermark.
- Policy version, creation/expiry timestamps, and digest.

Immediately before dispatch, the backend re-resolves the scope and compares the target set, versions, and policy. Material change rejects the command and requires a new preview and approval. Emergency containment may use a clearly labelled degraded preview, but records both the requested scope and the eventually resolved targets.

Long-running commands return Accepted and are processed asynchronously. The UI shows policy checking, approval, dispatch, propagation, target acknowledgement, broker reconciliation, partial/unknown results, and compensation without pretending the action was instant.

Cancellation is allowed only before irreversible dispatch. After dispatch, reversal requires a new typed compensating command bound to the original command, such as deactivate a restriction, restore an approved policy, reschedule a worker, or resume a deployment. Compensation never erases the original effect or evidence.

### 7.5 Audit and evidence service

Audit records are append-only and copied to an immutable evidence archive. Records include:

- Who, when, where, and under which role/scope.
- What was viewed, exported, requested, approved, or changed.
- Redacted before/after values.
- Reason, ticket, affected-resource count, and policy version.
- Command, trace, release, package, and worker identifiers.
- Result and reconciliation evidence.

Application operators cannot delete or rewrite audit history. Retention and legal-hold policies are applied by a separate evidence process.

Sensitive mutations use one local database transaction:

```text
create AdminCommand
store PolicyEvaluation and approval binding
store CommandAuditIntent
store resolved ImpactPreview reference
create CommandOutboxMessage
commit
```

The outbox consumer publishes the command, records target delivery/reconciliation events, and copies redacted evidence to the immutable archive. Emergency execution does not synchronously depend on the remote archive, but if the durable local command/audit/outbox transaction cannot be written, the sensitive mutation fails closed.

The immutable evidence archive uses a separately controlled account, retention lock/write-once capability, signed or hash-chained batches, redundant copies, periodic integrity verification, independently audited access, and no application-level delete permission.

### 7.6 Emergency Safety Control service

This service is independent of the normal Admin Web/BFF and primary-region read models:

```text
Hardware-key operator
  -> independent emergency endpoint/CLI
  -> predefined restrictive policy command
  -> multi-region durable publication
  -> same command/audit/target reconciliation authority
```

It permits only block new exposure/deployments, close-only, quarantine an exact gateway/package version, and revoke a cloud worker assignment. It cannot expand permissions, publish code, access databases or secrets, place raw orders, or weaken a restriction. It is failure-tested independently.

## 8. Admin navigation and screens

### 8.1 Operations home

Show only actionable summaries:

- Active local and cloud deployments by state.
- Stale workers and disconnected brokers.
- Unknown or unreconciled broker commands.
- Strategies with elevated errors or risk rejections.
- Active kill switches and degraded regions.
- Conversion backlog and quarantined uploads.
- Payment and entitlement projector health.
- Open security, privacy, and trading incidents.
- Pending high-risk approvals.

Revenue, customer support, and system safety are separate panels so a financial metric does not hide a safety incident.

### 8.2 Users

- Find by user ID, normalized email, masked MT5 login, deployment ID, invoice ID, or case ID.
- View verification, MFA, sessions, subscriptions, entitlements, strategies, broker metadata, deployments, consents, and notifications.
- Revoke a session, lock an account, resend verification, or begin a verified MFA-recovery case.
- View security and support history.

The page never displays broker passwords. Admins cannot set a password for a user. Password reset and MFA recovery use verified user workflows.

### 8.3 Support cases

- Case owner, status, priority, category, and SLA timer.
- User-provided attachments in quarantine.
- Linked user, strategy, deployment, invoice, and incident.
- Internal notes separated from user-visible messages.
- Approved temporary read access where necessary.
- Resolution code and evidence.

Read-only view-as-user may be added later with a permanent banner, user consent or approved case basis, and disabled trading/security mutations. Silent impersonation is prohibited.

### 8.4 Strategies and catalogue

- Strategy identity, owner/licensor, visibility, and commercial terms.
- Immutable versions and package hashes.
- Parameter schemas and SET compatibility.
- Analyzer, simulation, parity, demo, and risk evidence.
- Broker/account compatibility.
- Publication regions, plans, modes, demo/live eligibility, and rollout percentage.
- Error rate, deployment count, and current incidents.
- Publish, suspend, retire, or revoke through governed commands.

### 8.5 Conversion operations

- Queue health, job state, resource use, and converter version.
- Sanitized compatibility and security findings.
- Sandbox failure and parser crash details.
- Quarantine, cancel, or safe retry.
- Reference-validation and demo-validation results.

Normal operators see metadata and redacted findings, not user source. Source access requires a named case, narrow files, short expiry, step-up MFA, separate approval, and a full access audit.

Approved source access opens only in a dedicated secure review service:

- Short-lived decryption inside the review environment.
- No object-store or decryption credential reaches the browser.
- Actor/case/time watermark on every view.
- No general download URL, browser cache, clipboard integration, or local persistence.
- Every file open and search is audited.
- Automatic termination when the TemporaryAccessGrant expires.

These controls provide narrow authorization, deterrence, attribution, and evidence; they cannot guarantee that an authorized reviewer will never manually copy or photograph source.

### 8.6 Deployments and trading operations

- Mode, state, strategy version, masked account, broker, region, worker, lease, fencing generation, and last reconciliation.
- Quote freshness, connection health, positions/orders summary, risk state, and recent activity.
- Pause, close-only, stop-after-flat, lease revoke, worker replace, or quarantine controls as permitted.
- Broker-confirmed result and uncertain-state warnings.

There is no free-form order ticket. Staff cannot change volume, direction, stop loss, take profit, or strategy inputs from this page.

### 8.7 Cloud fleet

- Regions, nodes, worker slots, queue depth, CPU, memory, network, restarts, and saturation.
- Worker identity, image/runtime/gateway versions, fence, account binding, and heartbeat.
- Drain node, replace worker, quarantine image, or change approved capacity policy.
- Forecasted capacity and unit cost by tested workload class.

### 8.8 Brokers and gateway versions

- Broker/server directory and aliases.
- Demo/live support status and account-mode capabilities.
- Authentication variants, symbol rules, known limitations, and cloud-origin rules.
- Approved gateway artifact hash, signature status, SBOM, vendor license evidence, and network behavior.
- Compatibility-test matrix, canary rollout, error trends, and rollback.

### 8.9 Billing and plans

- Plans, features, quotas, prices, subscriptions, invoices, payments, refunds, disputes, and usage.
- Cloud worker minutes and transparent metered units.
- Payment-provider webhook status and reconciliation.
- Entitlement projection state and inconsistencies.
- Manual adjustment workflow with reason and approval.

YO4X does not store full card data. Use a hosted payment flow and provider tokens. Billing status changes do not bypass risk-safe expiry behavior for open positions.

### 8.10 Incidents and communications

- Incident severity, owner, affected scopes, timeline, commands, evidence, and status.
- Internal updates and user-safe updates.
- Targeted notifications by affected user, strategy, broker, region, gateway, or deployment.
- Maintenance windows and service-status publishing.
- Post-incident actions and review.

### 8.11 Security, privacy, and audit

- Admin roles, assignments, temporary grants, sessions, devices, and access reviews.
- Security events, break-glass use, exports, and source-access history.
- Privacy export, deletion, restriction, legal hold, and rights/takedown cases.
- Searchable audit evidence and signed export packages.
- Key/version status without exposing private key material.

### 8.12 Releases, policies, and feature flags

- Desktop, agent, worker, runtime, converter, IR, bar builder, gateway, strategy, and policy versions.
- Environment promotion evidence.
- Canary cohorts, rollout state, health gates, pause, and rollback.
- Versioned risk and eligibility policies.
- Feature flags with owner, purpose, scope, expiry, and cleanup date.

Feature flags cannot weaken mandatory risk checks, package signatures, tenant authorization, or credential protection.

## 9. High-risk action policy

| Action | Default approval | Safety behavior |
|---|---|---|
| Revoke a user session | One authorized support/security operator | Ends application session only |
| Lock a suspicious user account | One security operator; immediate | Blocks login and new exposure through entitlement policy |
| Grant/refund billing credit | Finance limits; second approval above threshold | Reprojects entitlement; cannot send trades |
| Publish catalogue strategy to demo | Strategy reviewer plus required evidence | Progressive demo rollout |
| Approve strategy for live use | Independent strategy and risk approvals | Feature-flagged pilot first |
| Suspend a strategy version | Trading/Risk operator; emergency single actor allowed | Blocks new exposure, preserves approved reduction/protection |
| Revoke a gateway version | Trading/Security operator; emergency single actor allowed | Stops new assignments and applies scoped close-only policy |
| Start global kill switch | One trained emergency operator | Fast policy containment; operator authority expires into mandatory review while the restriction remains active |
| Extend or broaden global kill switch | Two-person approval | New impact preview required |
| Change production risk policy | Risk owner plus independent approver | New signed policy version, canary rollout |
| View private user source | Case-bound requester plus separate approver | Read-only, narrow scope, expiring access |
| Export audit or user data | Authorized role plus reason; approval by sensitivity | Watermarked, encrypted, time-limited artifact |
| Force-close user positions | Not a normal staged-release feature | Only future pre-authorized legal/risk policy with two-person control |

Emergency single-actor actions may contain risk quickly, but cannot silently expand privileges, publish code, expose data, or erase evidence.

## 10. Kill-switch and containment model

Kill switches are policies, not raw broker commands.

Supported scopes:

- Global.
- Environment.
- Region.
- Broker/server.
- Gateway version.
- Worker image/runtime version.
- Strategy or strategy version.
- User.
- Broker account.
- Deployment.

Containment compiles scopes into a versioned policy vector rather than one severity number:

```text
ExecutionSafetyPolicy
{
    AllowNewDeployment
    AllowStrategySignals
    AllowExposureIncrease
    AllowExposureReduction
    AllowProtection
    AllowPendingOrderCancellation
    AllowEmergencyClose

    LeaseMode
    WorkerAction
    CredentialAction
    PackageEligibility
}
```

Each field merges independently:

- Boolean permissions combine by intersection: any applicable deny wins for that capability.
- Reduction, protection, cancellation, and emergency close remain independent from exposure increase.
- LeaseMode, WorkerAction, CredentialAction, and PackageEligibility use versioned field-specific merge tables, not one global severity order.
- No lower scope may re-enable a capability denied by a broader policy.
- Conflicting non-Boolean actions fail closed to an explicitly tested safe resolution and create an operator finding.

Every activation requires scope, reason, incident/ticket, owner, start, operator-authority expiry, review deadline, and user-communication choice. Expiry of emergency authority never silently removes the restriction or restores exposure.

Propagation rules:

- Online cloud workers receive a durable command and a new policy/fence generation.
- Local workers receive the policy through the control plane and short-lived lease renewal.
- After lease expiry, an untampered official YO4X local agent will not create new exposure. YO4X cannot provide broker-enforced prevention against software or credentials used independently on a user-controlled device.
- Lease generations come from a linearizable ownership store. A new generation is not issued while the previous lease remains valid.
- A replacement starts in reconciliation-only mode and waits for broker reconciliation before new exposure.
- Local-to-cloud movement requires acknowledged local shutdown or full lease expiry plus the documented clock-skew/safety interval.
- YO4X fencing tokens govern official components; MT5 brokers do not enforce them. A modified local client may ignore them.
- The dashboard distinguishes accepted, delivered, acknowledged, and broker-reconciled states.
- No command is reported complete while affected deployments remain unknown.

Cloud worker replacement collects concrete fencing evidence:

1. Revoke the old workload identity.
2. Remove its secret-broker authorization.
3. Block its network egress.
4. Terminate or isolate its process/container.
5. Invalidate its command channel.
6. Attempt broker-session disconnect where supported.
7. Wait for the old lease/generation validity interval to end.
8. Start replacement in reconciliation-only mode.
9. Permit new exposure only when policy-defined fencing evidence and broker reconciliation pass.

Fleet screens report Supervisor, StrategyHost, and GatewayHost health separately. Local status displays YO4X authorization, official-worker reachability, broker-state certainty, and whether a hard broker-level stop was confirmed. It never displays “Trading stopped” from lease expiry alone.

Stopping all worker processing can also stop virtual protection. Therefore the safe default is block-increase or close-only, not kill the process blindly.

## 11. Strategy governance

### 11.1 Catalogue strategy lifecycle

~~~text
DRAFT
  -> SOURCE_REVIEW
  -> BUILDING
  -> SECURITY_REVIEW
  -> SIMULATION_REVIEW
  -> DEMO_APPROVED
  -> LIVE_CANDIDATE
  -> LIVE_APPROVED
  -> PUBLISHED
  -> SUSPENDED
  -> RETIRED

Any unsafe immutable version -> REVOKED
~~~

Each version records:

- Ownership and commercial-use evidence.
- Source/build commit and reproducible build identity.
- Package, manifest, schema, and resource hashes.
- Runtime, IR, converter, indicator, bar-builder, and dataset versions.
- Security, compatibility, simulation, reference, and demo evidence.
- Supported brokers, account modes, symbols, timeframes, and execution modes.
- Resource and event-rate limits.
- Known limitations and user disclosure version.
- Requester, reviewers, approvals, release, and rollback data.

Publishing creates a catalogue release pointing to an immutable version. It never edits a package.

### 11.2 User-owned private strategies

A user's private strategy remains private and user-owned. Admin operations may:

- See job metadata and sanitized findings.
- Enforce quotas and security quarantine.
- Approve platform/demo/live eligibility based on evidence.
- Suspend execution when a security or trading-safety risk exists.
- Process a rights complaint through a documented case.

Admin operations may not make the strategy public, resell it, claim ownership, or inspect its source without a valid gated purpose.

### 11.3 Validation levels

Keep these labels separate:

- **Semantic validation:** Converted package behaves consistently with the declared YO4X semantics.
- **Reference validation:** Converted output is compared against original MQ5 evidence from an identified MT5 build and dataset.
- **Demo validation:** Package operates safely against an approved demo broker account.
- **Live eligibility:** Business, legal, risk, gateway, and technical gates are all approved for a controlled live cohort.

No admin can change a label to parity without matching evidence.

## 12. Conversion operations

The admin portal controls jobs through the conversion orchestrator. It does not parse source in the web/API process.

Workflow:

1. Upload enters quarantine storage.
2. Malware, archive, type, size, depth, and secret checks run.
3. A disposable, network-denied sandbox parses and analyzes it.
4. Supported constructs lower into typed restricted Strategy IR.
5. The IR verifier and metering checks run.
6. Deterministic simulation and optional MQ5 reference comparison run.
7. Demo validation runs when required.
8. An approved signing service signs the exact package digest.

Operators may cancel, quarantine, or retry using a new job. They cannot alter the previous job result. A retry records the converter/runtime versions and reason.

## 13. Runtime and deployment operations

Admin runtime state is broker-reconciled. It must show:

- Desired deployment state.
- Observed worker state.
- Lease state and fence generation.
- Broker connection state.
- Last quote, event, order, deal, and reconciliation time.
- Open exposure and protective-order status at a summary level.
- Unknown broker outcomes requiring reconciliation.
- Active policies and restrictions.

Operational actions:

- Pause signal processing while preserving approved management behavior.
- Set close-only.
- Stop after flat.
- Revoke lease.
- Replace a cloud worker using a higher fencing generation.
- Quarantine a worker image, runtime, gateway, or strategy version.
- Ask the user to reauthenticate a failed credential without revealing it.

After crash or timeout, the orchestrator reconciles broker state before issuing another command. Unknown results are never blindly retried.

## 14. Cloud fleet architecture

V1A uses one isolated account-level cloud workload per active broker account:

```text
Runtime Supervisor  -> lease, generation, event transaction, risk, journal
StrategyHost        -> strategy only; no credential, gateway, or network access
GatewayHost         -> mt5api.dll, temporary credential access, broker-only egress
```

The three components use separate strongly isolated processes or containers. An interface alone is not the security boundary.

Fleet components:

- **Scheduler:** Places eligible deployments by region, capacity, gateway, broker endpoint, and policy.
- **Worker registry:** Tracks each component identity, image, version, fence, heartbeat, and assignment.
- **Capacity manager:** Maintains spare capacity and autoscaling limits.
- **Node agent:** Reports resource state and performs approved drain/replace commands.
- **Egress policy:** Allows only approved control-plane and broker endpoints.
- **Secret broker:** Gives only the assigned GatewayHost short-lived access to its vault reference.
- **Reconciler:** Compares desired, observed, and broker state continuously.

Required isolation:

- CPU, memory, disk, process, time, socket, and event quotas.
- No shared writable volume between customer workers.
- Unique workload identity and fence generation.
- Network-denied StrategyHost egress and broker/control-plane-only GatewayHost egress.
- Read-only immutable runtime/package layers.
- Automatic worker replacement with broker reconciliation.

Capacity planning uses measured workload classes, not a guessed RAM number. Track simple, multi-symbol, indicator-heavy, high-tick-rate, reconnecting, and history-heavy profiles before setting price or density.

## 15. Broker and gateway operations

### 15.1 Broker capability registry

Maintain versioned evidence for:

- Broker company, server name, aliases, endpoints, and regions.
- Demo/live availability.
- Netting/hedging behavior.
- Order, fill, expiration, stop, freeze, symbol, volume, and session rules.
- Main/investor password, OTP, PFX/certificate, proxy, and password-change flows.
- Cloud-origin or static-IP requirements.
- Known errors, maintenance, protocol build, and test date.

### 15.2 Gateway artifact registry

For every mt5api.dll version record:

- Original vendor name and version.
- SHA-256 and signature status.
- License, redistribution, cloud/SaaS, update, and support evidence.
- SBOM and vulnerability review.
- Observed network destinations and credential path.
- Supported broker/server-build matrix.
- Contract, integration, resilience, and demo-smoke results.
- Approval, canary, rollout, rollback, retirement, and revocation state.

The vendor DLL is never renamed for stealth. YO4X.Trading.dll remains the internal facade.

### 15.3 Progressive release

~~~text
REGISTERED -> SCANNED -> TESTED -> DEMO_CANARY -> PILOT -> APPROVED
APPROVED -> DRAINING -> RETIRED
Any unsafe state -> REVOKED
~~~

Assignments pin an exact artifact hash. Rollback changes future assignments and safely replaces workers; it does not mutate running binaries in place.

## 16. Billing and entitlement architecture

Keep these separate:

- **Product catalogue:** Plans, prices, included modes, quotas, and effective dates.
- **Payment provider:** Checkout, payment method token, invoice, payment, refund, and dispute.
- **Subscription ledger:** Provider-independent normalized billing events.
- **Usage ledger:** Immutable billable usage with source and time window.
- **Entitlement projector:** Converts paid/grace/cancelled states into platform execution rights.
- **Invoice read model:** User/admin display and reconciliation.

Provider webhooks are signature-verified, stored, deduplicated, and processed idempotently. Manual adjustments create ledger entries; they never edit a paid total in place.

Suggested billable units:

- Platform subscription period.
- Cloud deployment worker-minutes or a simple included-hours tier.
- Conversion quota or assisted conversion service.
- Storage/simulation above plan allowance, if later required.

Do not bill directly from noisy raw CPU samples. Aggregate signed usage windows and reconcile them against worker assignments.

Billing expiry behavior follows the execution safety policy:

- New exposure is blocked when the entitlement ends.
- Approved reduce/protect/cancel behavior may continue for the defined period.
- Open positions are not blindly closed because of a failed payment.
- The user receives warnings before expiry when possible.

## 17. User support and account security

Allowed support actions:

- Resend email verification.
- Revoke user sessions/devices.
- Lock/unlock after documented verification.
- Start password reset; never choose the password.
- Start MFA recovery under a verified recovery workflow.
- Explain conversion, entitlement, deployment, and billing states.
- Request sanitized diagnostics from the desktop.

Not allowed:

- Show or change an MT5 password.
- Disable MFA without verified recovery evidence.
- Trade, change risk limits, or edit strategy inputs for a user.
- Download private source as routine troubleshooting.
- Ask a user to send credentials through chat/email.
- Hide or delete a security/trading event.

Support bundles are user-approved, time-limited, encrypted, malware-scanned, and redacted before access.

## 18. Secret and key management

### 18.1 Broker credentials

- Cloud credentials remain in a KMS/HSM-backed vault.
- The database and admin views store only a vault reference and masked metadata.
- Only the assigned worker workload identity may request temporary decryption.
- Secret access is logged independently.
- Admins can request deletion or user reauthentication, never plaintext retrieval.

### 18.2 Signing keys

- Strategy, lease, installer, and update keys use separate identities and rotation schedules.
- Private signing keys stay in managed key/HSM services.
- The portal submits an approved digest to a signing workflow; it never receives the key.
- Signing requires build provenance and policy evidence.
- Revocation and trust-store rollout are tested before production.

### 18.3 Admin credentials

- Enterprise OIDC/SSO.
- Hardware-key/WebAuthn MFA for production-capable roles.
- Short admin sessions and step-up MFA for sensitive actions.
- Managed devices and conditional access where possible.
- No shared accounts or API keys.
- Service identities are workload-bound and non-interactive.

## 19. Incident management

Suggested severities:

| Severity | Example | Initial behavior |
|---|---|---|
| SEV-1 | Unauthorized trading risk, secret compromise, broad duplicate orders | Page incident team, contain affected scope, preserve evidence, user communication |
| SEV-2 | Region/broker outage, many stale workers, gateway regression | Stop new affected deployments, reconcile, canary rollback |
| SEV-3 | Limited strategy/conversion/billing issue | Quarantine scope, create case, planned repair |
| SEV-4 | Minor defect or operational request | Normal backlog |

Incident workflow:

~~~text
DETECTED -> TRIAGED -> CONTAINING -> STABILIZED -> RECOVERING
         -> MONITORING -> RESOLVED -> REVIEWED
~~~

Every major incident has:

- Named incident commander and technical owners.
- Affected-scope calculation.
- Timeline and command correlation.
- Evidence preservation and legal/privacy check.
- User-safe updates with no secrets.
- Recovery and broker reconciliation proof.
- Post-incident review and owned corrective actions.

## 20. Privacy, retention, and rights requests

Classify data before implementation:

- Identity and contact data.
- Authentication/security data.
- Broker-account metadata and credentials.
- Strategy source and intellectual property.
- Trading activity and financial-like records.
- Billing and tax records.
- Product analytics and diagnostics.
- Administrative audit evidence.

Privacy workflow:

~~~text
REQUESTED -> IDENTITY_VERIFIED -> SCOPED -> APPROVED
          -> PROCESSING -> QUALITY_CHECK -> DELIVERED/CLOSED
          -> BLOCKED_BY_LEGAL_HOLD
~~~

Deletion is a workflow, not a direct database button. It first stops deployments safely, removes entitlements and credential references, observes legal/audit retention, deletes eligible objects, and creates non-sensitive completion evidence.

Source export/download requires strong reauthentication, ownership verification, encryption, expiry, and full audit. Rights complaints and takedowns preserve evidence while restricting affected publication/execution according to policy.

Final retention periods require legal and jurisdiction review; they must not be invented by developers.

## 21. Administrative API surface

All endpoints are under a separate admin origin and /admin/v1 namespace. Commands require an idempotency key, reason, expected version, and correlation ID. Sensitive commands may return an approval request rather than execute immediately.

### 21.1 Identity and access

~~~text
GET    /admin/v1/me
GET    /admin/v1/access/roles
GET    /admin/v1/access/assignments
POST   /admin/v1/access/assignments
POST   /admin/v1/access/assignments/{assignmentId}/revoke
GET    /admin/v1/access/reviews
POST   /admin/v1/access/reviews/{reviewId}/decisions
GET    /admin/v1/admin-sessions
POST   /admin/v1/admin-sessions/{sessionId}/revoke
~~~

### 21.2 Approvals and commands

~~~text
GET  /admin/v1/approvals
GET  /admin/v1/approvals/{approvalId}
POST /admin/v1/approvals/{approvalId}/approve
POST /admin/v1/approvals/{approvalId}/reject
GET  /admin/v1/commands/{commandId}
POST /admin/v1/commands/{commandId}/cancel
POST /admin/v1/commands/{commandId}/compensation-requests
~~~

Cancel succeeds only before dispatch. Once any target is dispatched, the API returns a conflict and requires a typed compensation request.

### 21.3 Users and support

~~~text
POST /admin/v1/users/search
GET  /admin/v1/users/{userId}
POST /admin/v1/users/{userId}/lock
POST /admin/v1/users/{userId}/unlock
POST /admin/v1/users/{userId}/sessions/{sessionId}/revoke
POST /admin/v1/users/{userId}/verification/resend
POST /admin/v1/users/{userId}/mfa-recovery-cases
GET  /admin/v1/support-cases
POST /admin/v1/support-cases
GET  /admin/v1/support-cases/{caseId}
POST /admin/v1/support-cases/{caseId}/notes
POST /admin/v1/support-cases/{caseId}/close
~~~

User search requires a purpose, case/incident where applicable, narrow filters, result limit, and sensitive-read audit. A previous authorized search/case context is required before loading a user detail; broad enumeration and unbounded export are unavailable.

### 21.4 Strategies and conversion

~~~text
GET  /admin/v1/strategies
GET  /admin/v1/strategy-versions/{versionId}
POST /admin/v1/strategy-versions/{versionId}/reviews
POST /admin/v1/strategy-versions/{versionId}/approve-demo
POST /admin/v1/strategy-versions/{versionId}/approve-live
POST /admin/v1/strategy-versions/{versionId}/publish
POST /admin/v1/strategy-versions/{versionId}/suspend
POST /admin/v1/strategy-versions/{versionId}/retire
GET  /admin/v1/conversion-jobs
GET  /admin/v1/conversion-jobs/{jobId}
POST /admin/v1/conversion-jobs/{jobId}/quarantine
POST /admin/v1/conversion-jobs/{jobId}/retry
POST /admin/v1/source-access-requests
~~~

### 21.5 Runtime, fleet, and kill switches

~~~text
GET  /admin/v1/deployments
GET  /admin/v1/deployments/{deploymentId}
POST /admin/v1/deployments/{deploymentId}/close-only
POST /admin/v1/deployments/{deploymentId}/stop-after-flat
POST /admin/v1/deployments/{deploymentId}/revoke-lease
POST /admin/v1/deployments/{deploymentId}/replace-worker
GET  /admin/v1/fleet/regions
GET  /admin/v1/fleet/workers
POST /admin/v1/fleet/nodes/{nodeId}/drain
GET  /admin/v1/kill-switches
POST /admin/v1/kill-switches/preview
POST /admin/v1/kill-switches
POST /admin/v1/kill-switches/{switchId}/extend
POST /admin/v1/kill-switches/{switchId}/release-previews
POST /admin/v1/kill-switches/{switchId}/release-requests
~~~

### 21.6 Brokers, gateway, and releases

~~~text
GET  /admin/v1/brokers
POST /admin/v1/brokers/{brokerId}/test-runs
GET  /admin/v1/gateway-artifacts
POST /admin/v1/gateway-artifact-registrations
POST /admin/v1/gateway-artifacts/{artifactId}/approve-canary
POST /admin/v1/gateway-artifacts/{artifactId}/rollout
POST /admin/v1/gateway-artifacts/{artifactId}/rollback
POST /admin/v1/gateway-artifacts/{artifactId}/revoke
GET  /admin/v1/releases
POST /admin/v1/release-promotions
POST /admin/v1/releases/{releaseId}/rollback
~~~

Gateway registration accepts only a quarantined object reference, declared digest, provenance, and evidence references; the Admin browser is not a production binary upload path. A release-promotion request binds the exact release/artifact digest, target environment, evidence digest, rollout policy, requester, and approval.

### 21.7 Billing, privacy, incidents, and audit

~~~text
GET  /admin/v1/subscriptions
GET  /admin/v1/invoices
POST /admin/v1/billing-adjustments
POST /admin/v1/refunds
GET  /admin/v1/privacy-requests
POST /admin/v1/privacy-requests/{requestId}/previews
POST /admin/v1/privacy-requests/{requestId}/approve
POST /admin/v1/privacy-requests/{requestId}/process
GET  /admin/v1/incidents
POST /admin/v1/incidents
POST /admin/v1/incidents/{incidentId}/updates
POST /admin/v1/announcements
GET  /admin/v1/audit-events
POST /admin/v1/audit-exports
~~~

There is intentionally no endpoint to retrieve a broker password, upload arbitrary production code, run SQL, or place a broker order.

## 22. Admin data model

These entities extend the user-side domain model.

| Entity | Purpose | Important fields |
|---|---|---|
| AdminIdentity | Staff identity | Staff ID, SSO subject, status, assurance requirements |
| AdminRole | Permission bundle | Name, permissions, environment restrictions |
| AdminRoleAssignment | Scoped access | Identity, role, resource scope, start, expiry, approver |
| AdminSession | Staff session | Device, MFA level, issued, last activity, expiry, revoked |
| AccessReview | Periodic certification | Scope, reviewer, due date, findings, decisions |
| AdminCommand | Typed mutation request | Type, actor, scope, reason, expected version, lifecycle state, original/compensation link |
| CommandTarget | Per-target delivery truth | Command, target type/ID/version, delivery, acknowledgement, reconciliation, error |
| PolicyEvaluation | Authorization/safety decision | Command, actor/scope inputs, policy versions, decision, evidence hash |
| ImpactPreview | Frozen command impact | Scope expression, target snapshot/IDs, count, version watermark, policy, digest, expiry |
| ApprovalRequest | Independent decision | Command, policy, approvers, decision, timestamp |
| CommandAuditIntent | Atomic audit commitment | Command, event type, redacted payload hash, actor, correlation |
| CommandOutboxMessage | Durable publication | Command/target, sequence, payload hash, attempt and delivery state |
| AdminAuditEvent | Immutable evidence | Actor, action, target, before/after, reason, correlation, time |
| SupportCase | Controlled support work | User, category, priority, status, owner, links, resolution |
| TemporaryAccessGrant | Gated sensitive access | Case, resource scope, purpose, expiry, requester, approver |
| PrivilegedInfrastructureGrant | JIT non-portal access | Identity, system/scope, ticket, approver, start/expiry, session evidence |
| SecureSourceViewSession | Gated review environment | Grant, file scope, watermark, open events, start/expiry/termination |
| SecretMetadataProjection | Vault-free admin read model | Credential reference ID, state, last authorized worker use, deletion state |
| StrategyReview | Review evidence | Version, type, reviewer, findings, outcome, evidence hashes |
| CatalogueRelease | Published offer | Strategy version, plans, modes, regions, cohort, state |
| EligibilityPolicy | Demo/live rules | Version, evidence requirements, effective dates, signature |
| ExecutionSafetyPolicy | Scoped policy vector | Scope, independent permissions/actions, version, reason, review deadline, state |
| EmergencySafetyCommand | Restricted independent path | Actor, predefined action, scope, targets, incident, command/audit links |
| WorkerNode | Fleet host | Region, image, capacity, health, drain state |
| WorkerAssignment | Deployment placement | Worker, account/deployment, fence, versions, timestamps |
| GatewayArtifact | Vendor binary evidence | Version, hash, rights, SBOM, test and rollout state |
| BrokerProfile | Versioned broker support | Servers, capabilities, auth, cloud rules, limitations |
| CompatibilityTestRun | Broker/gateway evidence | Versions, endpoint, test suite, result, artifacts |
| ProductPlan | Commercial definition | Features, quotas, prices, effective dates |
| BillingEvent | Normalized ledger input | Provider, external ID, type, amount, currency, time |
| UsageWindow | Billable usage evidence | User/deployment, unit, quantity, source, start/end, hash |
| BillingAdjustment | Manual ledger change | Subscription/invoice, amount, reason, requester, approver |
| PrivacyRequest | Data-right workflow | User, type, verification, scope, status, hold, completion |
| Incident | Coordinated response | Severity, scope, owner, status, timeline, communications |
| Announcement | Targeted communication | Audience query, template, approval, schedule, delivery |
| ReleaseRecord | Immutable promotion | Component, artifact hashes, evidence, environment, rollout |
| FeatureFlag | Temporary product control | Owner, scope, value, reason, expiry, cleanup date |

All mutable entities use row versions for optimistic concurrency. Sensitive object references use separate authorization and retention policies.

## 23. State machines

### 23.1 Admin command

~~~text
REQUESTED
  -> POLICY_CHECKING
  -> WAITING_APPROVAL
  -> APPROVED
  -> SCHEDULED
  -> DISPATCHING
  -> PROPAGATING
  -> RECONCILING
  -> SUCCEEDED

POLICY_CHECKING / WAITING_APPROVAL -> REJECTED
Any pre-dispatch state -> CANCELLED | EXPIRED
DISPATCHING / PROPAGATING / RECONCILING -> PARTIAL | FAILED | UNKNOWN
Any post-dispatch state
  -> COMPENSATION_REQUESTED
  -> COMPENSATED | COMPENSATION_PARTIAL | COMPENSATION_FAILED
~~~

Per-target lifecycle:

~~~text
RESOLVED -> DISPATCHED -> DELIVERED -> ACKNOWLEDGED
          -> RECONCILING -> RECONCILED
Any delivered state -> UNREACHABLE | FAILED | UNKNOWN
~~~

### 23.2 Kill switch

~~~text
DRAFT -> ACTIVE -> EXPIRY_REVIEW_REQUIRED
EXPIRY_REVIEW_REQUIRED -> EXTENDED -> ACTIVE
EXPIRY_REVIEW_REQUIRED -> SAFE_TO_RELEASE
ACTIVE -> SAFE_TO_RELEASE
SAFE_TO_RELEASE -> DEACTIVATING -> RECONCILING -> INACTIVE
Any propagation/reconciliation gap -> PARTIAL with alert; restriction remains effective
~~~

Expiry removes temporary operator authority, not the safety restriction. Release requires a fresh impact preview, active-incident/policy evaluation, approval where required, propagation, and worker/broker reconciliation before exposure may resume.

### 23.3 Effective safety policy

Each policy-vector field is projected independently. Boolean permissions use intersection. Non-Boolean action fields use versioned merge tables with exhaustive conflict/property tests. The effective-policy digest is stored with every target command and worker acknowledgement.

### 23.4 Strategy release

~~~text
DRAFT -> REVIEWING -> DEMO_ELIGIBLE -> LIVE_CANDIDATE
LIVE_CANDIDATE -> LIVE_APPROVED -> CANARY -> PUBLISHED
PUBLISHED -> SUSPENDED -> PUBLISHED or RETIRED
Any unsafe version -> REVOKED
~~~

### 23.5 Gateway release

~~~text
REGISTERED -> SCANNED -> TESTING -> DEMO_CANARY
DEMO_CANARY -> PILOT -> APPROVED -> DRAINING -> RETIRED
Any unsafe artifact -> REVOKED
~~~

### 23.6 Support case

~~~text
NEW -> TRIAGED -> IN_PROGRESS -> WAITING_USER/WAITING_INTERNAL
    -> RESOLVED -> CLOSED
CLOSED -> REOPENED when justified
~~~

## 24. Security architecture

### 24.1 Primary threats

- Stolen or shared administrator account.
- Privilege escalation or stale staff access.
- One insider publishing unsafe code or hiding evidence.
- Support impersonation used to change trading/security state.
- Cross-user data or source-code access.
- Broker-password leakage through logs, UI, export, memory, or support tools.
- Kill-switch abuse or incorrect broad scope.
- Direct database change bypassing state machines.
- Supply-chain compromise in admin UI, backend, worker, gateway, or update pipeline.
- Replay or duplicate administrative command.
- Audit deletion or tampering.
- Malicious file in case attachment or conversion upload.
- Billing webhook forgery or duplicate processing.
- Feature flag used to bypass a mandatory control.

### 24.2 Required controls

- Separate admin identity tenant and origin.
- Enterprise SSO, hardware-backed MFA, short sessions, and managed-device policy.
- Server-side RBAC plus scoped ABAC.
- Time-limited grants and recurring access reviews.
- Two-person approval and separation of duties.
- Step-up MFA for sensitive commands and exports.
- Private access gateway, WAF/rate limits, CSRF protection, secure cookies, and strict CSP.
- Typed APIs with input validation, idempotency, replay windows, and optimistic concurrency.
- No direct production database access through the portal.
- Field-level redaction and export controls.
- KMS/HSM-backed secrets and signing.
- Immutable append-only audit archive.
- Signed releases, SBOM, dependency scanning, provenance, and rollback.
- Sandboxed uploads and support attachments.
- Alerting for break-glass use, bulk reads, exports, source access, permission change, and containment actions.
- Regular authorization, tenant-isolation, abuse-case, and incident exercises.

### 24.3 Break-glass procedure

1. Declare or link an incident.
2. Activate the disabled emergency identity using strong MFA.
3. Grant the narrowest scope for a short fixed time.
4. Alert security and platform owners immediately.
5. Record every read and command.
6. Remove access automatically at expiry.
7. Rotate affected credentials if necessary.
8. Complete independent review within the incident process.

Break-glass does not provide broker-password retrieval or audit deletion.

## 25. Audit, evidence, and exports

Admin event categories:

- Authentication and session.
- Permission and access review.
- Sensitive read and export.
- User security/support action.
- Strategy review, signing, publication, suspension, and revocation.
- Conversion quarantine and source access.
- Runtime, lease, worker, gateway, broker, and kill-switch action.
- Billing and entitlement adjustment.
- Privacy, takedown, and legal hold.
- Release, feature flag, key, and policy change.
- Incident and communication.

Audit payloads are structured and redacted at creation. Evidence exports are encrypted, watermarked with requester/case, time-limited, checksummed, and logged. CSV spreadsheet-formula injection must be neutralized in exports.

## 26. Reliability and recovery

- Admin commands use a durable outbox and idempotent consumers.
- Read models may lag, so command policy checks authoritative domain state again.
- Commands carry expected versions to stop stale-browser overwrites.
- Approval is bound to an exact command and impact digest; edits require new approval.
- Partial propagation remains visible until reconciled.
- Kill switches and lease policies are cached in redundant control-plane locations.
- Trading workers keep enforcing the last valid signed safety policy during control-plane degradation.
- Database, object storage, audit archive, and configuration are backed up and restore-tested.
- Restore exercises prove tenant authorization, package hashes, commands, and audit continuity.
- Region recovery assigns a higher fencing generation before replacement workers connect.
- The independent Emergency Safety Control service can publish predefined restrictive commands when normal Admin Web/BFF or its primary region is unavailable.

Lease/fence ownership, committed command, journal, user-data, artifact, and cache recovery use the RPO/RTO objectives defined in section 18.5 of the user-side architecture. Admin read models are rebuildable and never become the ownership authority.

The admin UI being unavailable must not disable worker risk checks or broker-side SL/TP protection.

## 27. Observability and service targets

Track:

- Admin authentication failures and risky sessions.
- Authorization denies and approval latency.
- Command queue age, execution latency, partial failure, and reconciliation age.
- Kill-switch propagation by online cloud, online local, and offline local population.
- Worker heartbeat, restart, CPU, memory, event lag, quote freshness, and broker latency.
- Unknown broker-command count and oldest age.
- Conversion queue, parser failures, quarantine, and sandbox resource limits.
- Gateway/broker errors by exact version and server.
- Billing webhook delay, duplicates, projection lag, and reconciliation differences.
- Audit delivery lag and archive integrity checks.
- Notification delivery failures.

Initial operational targets:

- 100% of admin mutations produce correlated audit evidence.
- A global containment command is accepted within seconds when the control plane is healthy.
- Online cloud-worker propagation target: under 30 seconds, then broker reconciliation.
- Online official local agents update on command/lease contact. For an unreachable local agent, YO4X reports authorization expiry and broker-state uncertainty; it does not claim a broker-enforced stop.
- Unknown broker outcomes page immediately and are never blindly retried.
- Admin read-model delay is visible in the UI.

These are engineering targets, not promises of broker execution time.

## 28. Testing strategy

### 28.1 Authorization and security

- Permission and scope matrix tests for every endpoint.
- Requester-cannot-approve-own-command tests.
- Expired grant, stale session, step-up MFA, and environment-boundary tests.
- JIT privileged infrastructure grant, approval, session-recording, expiry, and direct-access alert tests.
- Cross-user and cross-source isolation tests.
- Broker-secret non-disclosure tests across UI, API, logs, exports, crashes, and support bundles.
- CSRF, XSS, injection, SSRF, path/archive, replay, and mass-assignment tests.
- Bulk-read, export, and break-glass alert tests.
- Secure source-view watermark, file-open audit, no object-store credential, expiry, and cache/download restriction tests.
- Audit tamper and restore verification.

### 28.2 Command and safety tests

- Idempotent duplicate requests.
- Stale/grown target-set impact preview, resource-watermark, re-resolution, and optimistic-concurrency rejection.
- Approval digest mismatch.
- Full command/target lifecycle, pre-dispatch cancellation, post-dispatch compensation, partial, failed, and unknown tests.
- Atomic command, policy evaluation, approval binding, audit intent, and outbox rollback/fail-closed tests.
- Policy-vector lattice merge/property tests for every independent capability and action field.
- Expiry-review tests proving containment never silently restores exposure.
- Online/offline worker propagation.
- Close-only behavior with open positions and pending orders.
- Cloud fencing tests for workload identity, secret access, egress, process, command channel, broker disconnect, lease validity, and reconciliation-only replacement.
- Cooperative local-fencing display tests that never claim “Trading stopped” from lease expiry alone.
- Unknown broker outcome and reconciliation.
- Gateway/strategy/runtime quarantine and rollback.
- Proof that admin cannot construct a raw broker order.

### 28.3 Strategy and conversion tests

- Immutable package and version enforcement.
- Required evidence before demo/live approval.
- Independent reviewer rules.
- Source-access request scope/expiry/audit.
- Dedicated secure source-review environment and per-file access evidence.
- Malformed source and attachment sandboxing.
- Retry creates new evidence and preserves old result.
- Publication cohort, suspension, retirement, and revocation behavior.

### 28.4 Billing and privacy tests

- Forged, duplicate, delayed, and out-of-order webhook events.
- Refund/dispute and entitlement projection.
- Failed payment with open positions uses safe expiry policy.
- Usage-window deduplication and invoice reconciliation.
- Privacy identity verification, legal hold, deletion, and completion evidence.
- Export encryption, expiry, watermark, authorization, and formula-injection protection.

### 28.5 Operational exercises

- Broker outage.
- Gateway regression.
- Region loss and worker relocation.
- Compromised admin identity.
- Compromised signing/gateway artifact.
- Duplicate-order incident.
- Conversion parser attack.
- Audit store delay.
- Normal Admin Web/BFF and primary-region outage while the independent emergency restrictive path is exercised.
- Emergency service attempts to expand access, reach secrets/database, publish code, or place an order and is denied.
- Immutable evidence archive integrity/retention-lock verification and local durable-audit failure.
- Payment-provider outage.
- Restore from backup with audit continuity.

## 29. Release and environment policy

Use separate development, test, demo, pilot, and production environments. Identities, secrets, databases, vaults, brokers, signing keys, and audit archives are separated.

Promotion requires:

- Reviewed code and passing CI.
- SBOM and dependency/security scan.
- Database migration compatibility and rollback plan.
- Authorization matrix and audit tests.
- Demo integration and failure-injection evidence.
- Signed immutable artifacts.
- Change record and independent production approval.
- Canary health gates and automatic/manual rollback.

Admin frontend and backend versions are compatible through versioned APIs. A UI release cannot require direct table access or bypass older safe commands.

## 30. Recommended solution structure

~~~text
src/
├── YO4X.Admin.Web/                 React/TypeScript admin portal
├── YO4X.Admin.Bff/                 Admin session, redaction, read models, typed API
├── YO4X.Admin.Application/         Admin use cases and command policies
├── YO4X.Admin.Domain/              Approvals, roles, cases, incidents, releases
├── YO4X.Admin.Infrastructure/      SSO, audit archive, notification adapters
├── YO4X.Authorization/             Permissions, scopes, ABAC and access reviews
├── YO4X.Approvals/                 Two-person approval workflow
├── YO4X.Audit/                     Append-only audit and evidence export
├── YO4X.EmergencyControl/          Independent restrictive emergency API/CLI
├── YO4X.PrivilegedAccess/          JIT infrastructure-access evidence and policy
├── YO4X.SecretMetadata/            Vault-free credential metadata projection
├── YO4X.SecureSourceReview/        Isolated gated private-source viewer
├── YO4X.Operations/                Kill switches, runtime and fleet operations
├── YO4X.Billing/                   Plans, ledger, usage and entitlement projection
├── YO4X.Privacy/                   Export, deletion, holds and rights workflows
├── YO4X.Incidents/                 Incident state and targeted communications
└── existing shared YO4X Domain/Application modules

tests/
├── YO4X.Admin.Authorization.Tests/
├── YO4X.Admin.Api.Tests/
├── YO4X.Admin.Security.Tests/
├── YO4X.Admin.Command.Tests/
├── YO4X.Admin.Audit.Tests/
├── YO4X.Admin.Operations.Tests/
├── YO4X.EmergencyControl.Tests/
├── YO4X.PrivilegedAccess.Tests/
├── YO4X.Admin.Billing.Tests/
├── YO4X.Admin.Privacy.Tests/
└── YO4X.Admin.EndToEnd.Tests/

docs/
├── USER_SIDE_ARCHITECTURE.md
├── ADMIN_SIDE_ARCHITECTURE.md
├── runbooks/
├── security/
├── strategy-conversion/
└── decisions/
~~~

Dependency direction:

~~~text
Admin Web -> Admin BFF -> Admin Application -> shared Domain/Application
Admin Infrastructure implements ports; domain projects do not reference infrastructure.
Admin components never reference mt5api.dll.
Only the MT5 gateway adapter inside fenced workers references the vendor DLL.
EmergencyControl publishes only predefined restrictive commands and does not depend on Admin Web/BFF.
Admin BFF reads SecretMetadata only and has no dependency on the vault client.
~~~

## 31. Delivery plan

Only the minimum A0–A3 capabilities needed for the one-broker/one-strategy cloud demo are built before V1A. A polished full admin suite, billing, local-mode operations, general conversion operations, automated privacy, source viewing, cost forecasting, announcements, and broad feature-flag tooling do not run ahead of the safety plane.

### First operational MVP boundary

The first backend milestone contains only:

1. Staff identity boundary, strong MFA policy, roles, scopes, and JIT privileged access records.
2. Read-only deployment, Supervisor/StrategyHost/GatewayHost, account, broker, and reconciliation health.
3. Immutable audit/outbox foundation and support cases.
4. Exact gateway and strategy version/provenance governance.
5. Close-only, stop-after-flat, cloud lease revoke, and cloud-worker replacement.
6. Policy-vector block-new-exposure/deployment containment.
7. Basic incident management and the independent Emergency Safety Control path.
8. Unknown-result monitoring and broker reconciliation evidence.

Production trading mutations remain disabled until these capabilities and their failure tests pass.

### Phase A0 — Safety foundation

- Separate admin identity application, SSO, hardware-key MFA, managed-device, and session policy.
- Permission/scope model, JIT privileged infrastructure access, and access review.
- Typed command/target lifecycle, approval binding, preview revalidation, idempotency, atomic audit intent/outbox, and compensation foundations.
- Break-glass, basic incident workflow, and independent Emergency Safety Control service.
- Separate admin origin and deployment boundary.
- No production mutations until authorization tests pass.

### Phase A1 — Read-only operations and support

- Operations dashboard and health read models.
- User, subscription, entitlement, broker metadata, and deployment views.
- Support cases and safe session/account actions.
- Redaction, sensitive-read audit, and sanitized diagnostics.

### Phase A2 — Broker and gateway governance

- Broker capability registry.
- Quarantined gateway artifact registration, licence/provenance/SBOM/network evidence, and exact hash pinning.
- Compatibility matrix and repeatable broker demo tests.
- Demo canary, rollback, retirement, and revocation.

This phase aligns with user-side Phase U0 and precedes runtime containment work.

### Phase A3 — Runtime safety and cloud fleet

- Deployment reconciliation view.
- Close-only, stop-after-flat, lease revoke, and worker replacement.
- Policy-vector containment, impact revalidation, target-level propagation, expiry review, governed release, and compensation.
- Concrete workload identity/secret/egress/process/command-channel fencing and reconciliation-only replacement.
- Fleet component health, node drain, egress, and workload identity monitoring.

The narrow V1A subset of this phase must be complete before even allowlisted external users depend on cloud demo execution. The full phase must pass before cloud 24/7 live execution.

### Phase A4 — Strategy and conversion governance

- Catalogue version/review screens.
- Conversion queue, quarantine, retry, and secure gated source-review service.
- Validation evidence and demo/live approval workflows.
- Immutable publication, suspension, retirement, and revocation.

### Phase A5 — Billing and entitlements

- Provider-neutral plans, subscriptions, balanced immutable monetary ledger, and usage ledger.
- Webhook verification/idempotency.
- Entitlement projection and reconciliation.
- Refund, dispute, and manual adjustment approvals.

### Phase A6 — Security, privacy, and incidents

- Expanded privacy/rights workflows, preview/approval/process stages, holds, and gated exports.
- Recurring access reviews and privileged-session evidence review.
- Expanded incident, maintenance, targeted communication, and evidence export.
- Security exercises, backup restore, and audit-integrity review.

### Phase A7 — Production hardening

- Complete security and authorization review.
- Load, resilience, region-loss, and failure-injection tests.
- Runbooks, on-call ownership, escalation, and training.
- Demo soak and controlled live-pilot approval.

## 32. Definition of target admin platform complete

The target admin platform is complete when authorized YO4X staff can:

1. Sign in through separate SSO with strong MFA and scoped access.
2. Support a user without seeing broker passwords or silently impersonating them.
3. Review and publish an immutable strategy version with independent evidence and approvals.
4. Operate private conversions without broad access to user source.
5. Approve demo/live eligibility without making unsupported parity claims.
6. See desired, worker-observed, lease, and broker-reconciled deployment state.
7. Contain risk by deployment, user, strategy, broker, gateway, region, or global scope.
8. Prove command delivery, acknowledgement, and reconciliation instead of claiming instant success.
9. Drain/replace cloud workers under the linearizable generation and reconciliation-only protocol without claiming absolute broker-enforced fencing for local clients.
10. Approve, canary, roll back, retire, or revoke exact gateway artifact hashes.
11. Manage plans, subscriptions, usage, refunds, and entitlements without unsafe open-position behavior.
12. Process privacy, source-rights, incident, and communication workflows.
13. Produce tamper-resistant evidence for every sensitive read, approval, export, and mutation.
14. Recover from control-plane, region, gateway, worker, and billing failures using tested runbooks.

## 33. Decisions required before implementation

- Company SSO/identity provider and managed-device policy.
- Hosting/cloud provider and allowed worker regions.
- Payment provider, currencies, taxes, refund rules, and usage model.
- Legal jurisdictions, data residency, retention, and rights-request periods.
- Live-trading approval authority and emergency close policy.
- On-call staffing and incident escalation coverage.
- Broker cloud-origin/static-IP permissions.
- Gateway commercial rights and production artifact.
- Signing/KMS/HSM provider and key ceremony.
- Final strategy eligibility evidence thresholds.
- Measured worker resource classes and 24/7 price.

These choices change adapters and policies, not the core safety boundaries above.

## 34. Admin review remediation map

| Review issue | Corrected architecture |
|---|---|
| Command lifecycle inconsistent | Sections 7.4 and 23.1 add dispatch, propagation, reconciliation, unknown, target results, and compensation |
| One-dimensional kill-switch precedence | Section 10 uses a formally tested policy vector/lattice |
| Automatic expiry could unlock trading | Sections 10 and 23.2 require expiry review and governed reconciled release |
| Local-worker guarantee overstated | Section 10 and observability use untampered-agent and broker-unknown wording |
| Cloud fencing lacked enforcement | Sections 10 and 14 require identity, secret, egress, process, channel, lease, and broker evidence |
| Internal worker isolation incomplete | Sections 13–14 expose Supervisor, StrategyHost, and GatewayHost separately |
| Audit transaction underspecified | Sections 7.5 and 26 define atomic command/audit/outbox persistence and immutable archive delivery |
| Impact preview can become stale | Section 7.4 requires target snapshot/watermark and pre-dispatch revalidation |
| Admin BFF had vault relationship | Sections 5–6 remove it and use SecretMetadataProjection |
| Infrastructure privilege missing | Section 4.2 defines JIT bastion/proxy access and evidence |
| Unsafe API semantics | Section 21 uses explicit revoke, purpose-bound search, quarantine registration, staged privacy/release workflows, and compensation |
| Private source access too broad | Section 8.5 defines a dedicated secure review service |
| Normal stack was emergency dependency | Sections 7.6 and 26 define an independent restrictive emergency service |
| Delivery order/MVP too large | Section 31 moves gateway governance earlier and limits the first operational MVP |
