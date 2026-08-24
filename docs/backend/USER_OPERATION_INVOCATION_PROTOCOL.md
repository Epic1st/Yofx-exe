# User-operation invocation protocol

Status: **provider-neutral database and C# boundary implemented and PG18-tested;
authenticated cross-host transport/provider unavailable; mutation remains disabled**

Version: 1.0  
Protocol message version: `requested.v4`  
Result contract version: `v5`

## Purpose

This protocol defines the provider-neutral boundary between a durable YO4X user
operation and a broker-facing gateway invocation. Its primary safety property is
that a caller cannot turn an ambiguous post-handoff failure into a terminal
"not sent" result.

The database, rather than a caller-supplied boolean, owns whether an invocation
has crossed the point of no return. A retry is permitted only when PostgreSQL
proves that no `provider_call_authorized` receipt was committed.

This document does not select an MT5 transport, broker, queue, or credential
provider. Provider-specific code starts only after the provider-neutral database
claim described here has succeeded.

## Current implementation and release gap

The provider-neutral authority boundary is implemented, but the current runtime
must remain mutation-disabled:

- Legacy `*.requested.v2` envelopes do not bind an execution deadline or an
  immutable invocation-attempt identifier. Their delayed-delivery risk is
  confined to non-executable legacy data.
- The bounded `*.requested.v3` envelope binds a deadline and assignment lease;
  an eventual receiver must reject it before beginning an invocation when
  either bound time has expired. It still has no database-owned
  invocation-attempt state or one-shot gateway begin receipt, so it is
  transitional, non-executable authority.
- Migration `002_user_operation_invocation_protocol` implements requested-v4
  attempts, generation-bound delivery claims, gateway begin, one-shot provider
  authorization, ambiguity, typed gateway observations, expiry, reconciliation,
  result-v5 acceptance/replay, and atomic target projection.
- Exact worker, supervisor, gateway, credential, and runtime-evidence database
  roles are separately attested. Runtime evidence is execute-only: it has no raw
  audit, outbox, invocation, account, assignment, or deployment table access.
- Strict C# supervisor, gateway, credential-boundary, provider-observation, and
  result-v5 adapters implement the same bindings. A fresh PG18 integration fact
  crosses every role-specific adapter with a deterministic in-memory provider,
  then proves exact result replay and persisted target evidence. That provider
  is test evidence only; it is not MT5 or broker connectivity.
- There is no production outbox consumer/router or authenticated transport that
  executes requested-v4 envelopes. Supervisor and GatewayHost register only
  role-local unavailable ports while the protocol is disabled; setting
  `UserOperationProtocol.Enabled=true` fails startup with a sanitized missing-
  capability error.
- There is no authenticated Supervisor-to-Gateway handoff, isolated
  Gateway-to-credential-boundary transport, production secret provider,
  provider invoker, result-v5 return channel, or restart coordinator.
- Broker and deployment result `v4` retain the compatibility fields
  `PreInvocationNotSentProven` and `GatewayInvoked`, but new ingress fixes them
  to `false` and `true` respectively and accepts only conclusive
  `succeeded|diverged` observations. `failed` is rejected on both original and
  challenge paths; send/IPC ambiguity therefore remains unknown/reconciling.
- Result v4 cannot terminalize a not-sent outcome. Its two retained booleans
  are fixed compatibility fields, not proof that an invocation began.
- The database now owns one-shot begin and authorization proof. Execution stays
  disabled because those proven adapters are intentionally unreachable from the
  hosts until the authenticated transport and provider boundaries above exist.
- Legacy observation-only reconciliation remains non-executable. The implemented
  attempt-bound v3 challenge/result-v5 path never republishes or re-executes the
  original mutation.

The legacy observation-only challenge remains
`yo4x.user-operation.reconciliation-requested.v2`; the attempt-bound v3
challenge belongs only to requested-v4/result-v5 attempts. The database,
contracts, and role-specific adapters below are implemented. The authenticated
multi-process transport, real provider, and execution-enabled release facts are
not implemented and must not be inferred from the database proof.

## Threat model

The protocol protects against:

- theft of one runtime database credential;
- arbitrary SQL available to that runtime database role;
- duplicate, delayed, reordered, or replayed outbox delivery;
- process termination or network loss at any handoff boundary;
- a response being lost after PostgreSQL committed a transition;
- concurrent delivery, rejection, expiry, and invocation claims;
- an expired, revoked, fenced, or reconciliation-only assignment attempting a
  fresh mutation;
- a caller falsely claiming that a broker-facing call was never attempted.

The protocol assumes:

- the database host and offline migration authority are trusted;
- runtime process/configuration compromise is a separately documented residual;
- the supervisor/router process cannot load a broker vendor library and cannot
  retrieve an executable credential grant;
- the gateway remains in the pinned, short-lived process-isolation boundary;
- a 256-bit bearer unavailable to the database-credential thief remains
  computationally unguessable.

## Non-negotiable invariants

1. A fresh mutation is routed only to an exact current-fence `active`
   assignment with `revoked_at is null` and enough database-clock lease
   remaining for the invocation deadline. `reconciliation_only` is never fresh
   mutation authority.
2. PostgreSQL derives the target, operation, policy, route, assignment, fence,
   deadlines, and digests from locked durable rows. Runtime callers do not
   submit authoritative copies of those values.
3. The supervisor/router receives routing metadata but no provider invocation
   authority, broker credential, or vendor library.
4. The one-shot `begin` function returns only a redemption nonce that is not
   executable authority. The gateway must commit and close that transaction;
   only then may a separate credential-boundary transaction redeem the nonce
   after observing the committed attempt and immutable start receipt. A nonce
   from a rolled-back `begin` is forever unusable.
5. `not_sent` is reachable only before the credential boundary's durable
   provider-call authorization wins. A committed `begin` remains
   non-executable; if its redemption nonce expires unconsumed, PostgreSQL can
   prove `not_sent`. Once state is `authorized`, no role or function can
   transition the attempt to `not_sent`.
6. Loss of a committed `begin` response cannot replay its nonce and eventually
   becomes provably `not_sent` if no authorization exists. Loss after provider
   authorization is ambiguous and never permits a second authorization.
7. Outbox publication, transport acknowledgement, and caller assertions are not
   proof that a provider call was or was not made.
8. A succeeded or diverged result requires an immutable gateway receipt, the
   exact attempt binding, and broker-confirmed evidence. A gateway response
   alone is not final broker state.
9. `not_sent` is terminal for one attempt, not automatically for protective
   user intent. A protective operation may receive a new attempt only after the
   preceding attempt is durably `not_sent`. It is never retried after
   `authorized` or `ambiguous`.
10. Database time is authoritative. Expiry is exclusive: a capability or claim
    is invalid when `clock_timestamp() >= expires_at`.
11. Raw bearers are never stored in authority, attempt, receipt, result, audit,
    or application-log rows. Only SHA-256 digests are stored there. A raw
    delivery and result bearers may exist only inside the access-restricted
    outbox payload; later gateway, redemption, and receipt bearers are transient
    bind/return values.
12. Every authority-changing function is `SECURITY DEFINER`, has
    `search_path = ''`, validates exact `session_user` and `current_user`, uses
    database-clock checks, and has `PUBLIC` execution revoked.

## Process and role boundaries

| Principal | Permitted responsibility | Forbidden responsibility |
|---|---|---|
| `yo4x_worker` | Lock an accepted operation, create an attempt/outbox/audit atomically, derive expiry/ambiguity, reconcile durable receipts | Read raw credentials, call a provider, claim gateway invocation, fabricate a result |
| `yo4x_supervisor_runtime` | Claim delivery metadata, pass a transient gateway bearer to the pinned gateway, reject a delivery before gateway begin | Load vendor binaries, retrieve executable credentials, mark a post-begin attempt not sent |
| `yo4x_gateway_runtime` | Consume one gateway bearer in the isolated gateway process, commit the start transition, pass a transient redemption nonce to the credential boundary, and persist an observation using a second bearer | Create operations, select arbitrary tenant data, read credentials, redeem before the start transaction commits, reset an attempt, return it to `not_sent` |
| `yo4x_credential_runtime` | On a new committed snapshot, atomically consume one redemption nonce bound to an immutable start receipt, commit a one-shot authorization, then own exactly one provider call | Accept an uncommitted start, return a reusable grant or secret, create attempts, select arbitrary credentials, or issue a second provider call |
| `yo4x_runtime_evidence` | Submit a result that references an already persisted attempt/receipt or a reconciliation challenge | Directly insert/update attempt or receipt rows, assert gateway invocation state |
| `yo4x_context_authority` | Own protected tenant-context objects only | Login or receive runtime grants |
| `yo4x_migrator` | Own schema/functions as an isolated `NOLOGIN` role | Be granted to a runtime or deployment wrapper |
| `PUBLIC` | None | Execute protocol functions or access protocol tables |

An HTTP service may mediate these functions, but its database connection must
still use the exact role shown above and its authenticated workload claims must
match the locked assignment. An API policy name is not a substitute for the
database role check.

## Durable records

### `operations.user_operation_invocation_attempts`

One row represents one possible provider invocation. Recommended columns:

- `id`, `tenant_id`, `operation_id`, `dispatch_message_id`, `attempt_number`;
- `operation_type`, `target_type`, `target_id`;
- `route_deployment_id`, `fence_generation`, `worker_assignment_id`,
  `worker_instance_id`;
- `command_sha256`, `dispatch_target_binding_sha256`,
  `dispatch_policy_snapshot_sha256`;
- `result_capability_sha256`, `result_capability_expires_at`,
  `delivery_capability_sha256`;
- nullable `gateway_capability_sha256` and `receipt_capability_sha256`;
- `state`, `state_version`;
- `created_at`, `execute_not_after`;
- nullable `delivery_claim_id`, `delivery_claimed_at`,
  `delivery_claim_expires_at`;
- nullable `invocation_id`, `invocation_started_at`,
  `invocation_receipt_deadline`;
- nullable `credential_redemption_capability_sha256`,
  `credential_redemption_expires_at`, `provider_call_authorized_at`;
- nullable `terminal_reason`, `completed_at`.

Required uniqueness includes:

- `(tenant_id, id)`;
- `(tenant_id, operation_id, attempt_number)`;
- `(tenant_id, dispatch_message_id)`;
- one open attempt per operation;
- each capability digest globally unique within its capability class;
- `(tenant_id, id, operation_id, dispatch_message_id, command_sha256,
  route_deployment_id, fence_generation, worker_assignment_id,
  worker_instance_id)` for exact receipt/result foreign keys.

The immutable-binding trigger permits only the state transitions below, an
exact `state_version + 1`, monotonic database timestamps, bounded claim fields,
and capability consumption/rotation allowed by that transition.

### `operations.user_operation_invocation_receipts`

Receipts are append-only. Each receipt binds:

- receipt ID, tenant ID, attempt ID, invocation ID;
- receipt kind and prior/next attempt state versions;
- exact command/route/assignment/fence digests;
- authenticated workload and database role;
- provider-neutral outcome and redacted evidence digest;
- broker-confirmed observation digest when applicable;
- database `occurred_at` and caller observation time;
- a canonical receipt SHA-256 digest.

Receipt kinds are allowlisted:

- `delivery_claimed`;
- `delivery_rejected_before_invocation`;
- `delivery_expired_before_invocation`;
- `gateway_invocation_started`;
- `provider_call_authorized`;
- `gateway_invocation_ambiguous`;
- `gateway_observation_succeeded`;
- `gateway_observation_diverged`;
- `reconciliation_observation_succeeded`;
- `reconciliation_observation_diverged`.

There is no caller-created `gateway_not_invoked` receipt after
`gateway_invocation_started`.

## State machine

```text
                          DB deadline / exact rejection
                    +------------------------------------+
                    |                                    v
created -> pending -> delivered --------------------------> not_sent
               |          |                                    ^
               |          | one-shot gateway begin             |
               |          v                                    |
               +------> prepared -- nonce expiry/no auth -------+
                              |
                              | committed one-shot provider authorization
                              v
                         authorized ---- timeout/crash ---> ambiguous
                              |                                |
                              | broker-confirmed proof         | reconciliation proof
                              v                                v
                           observed <--------------------------+
```

| Current state | Allowed next state | Authority and condition |
|---|---|---|
| none | `pending` | Worker create function; operation, attempt, audit, and v4 outbox commit atomically |
| `pending` | `delivered` | Exact supervisor consumes the delivery bearer before `execute_not_after`; active route is rechecked |
| `pending` | `not_sent` | Database deadline expiry or exact pre-delivery rejection; no gateway capability has existed |
| `delivered` | `delivered` | Exact claim replay/rotation while its lease is current; old gateway-cap digest is retired atomically |
| `delivered` | `not_sent` | Rejection/expiry wins the row lock before gateway begin |
| `delivered` | `prepared` | Exact gateway consumes the current gateway bearer before the deadline; a non-executable redemption nonce is returned once |
| `prepared` | `authorized` | Exact credential runtime consumes the expiring post-commit redemption nonce once and durably authorizes one provider call |
| `prepared` | `not_sent` | Database proves the redemption nonce expired without a provider-call authorization receipt |
| `authorized` | `ambiguous` | Receipt deadline passes, process dies, or transport/provider completion is not conclusive |
| `authorized` | `observed` | Exact one-use receipt bearer persists broker-confirmed succeeded/diverged evidence |
| `ambiguous` | `observed` | Exact current reconciliation challenge persists broker-confirmed succeeded/diverged evidence |
| `not_sent` | none | Attempt terminal; a policy-safe new attempt may be created from the operation |
| `observed` | none | Attempt terminal; operation reconciliation consumes the immutable result |

`authorized -> not_sent`, `ambiguous -> not_sent`, reopening a terminal attempt,
and decrementing a state version are forbidden by both function predicates and
the transition trigger. `prepared -> not_sent` is permitted only through the
DB-owned nonce-expiry path when no authorization receipt exists.

### Meaning of the point of no return

`prepared` means PostgreSQL committed the start receipt and issued one
non-executable redemption nonce. It is not the point of no return. `authorized`
means the credential boundary durably consumed that nonce and is permitted to
make exactly one provider call after the authorization transaction commits.
From `authorized` onward the system can no longer prove the command was not
sent. Every inconclusive outcome from that point is `ambiguous` and is settled
only by broker-confirmed observation.

## Function contracts

Names are normative for the first implementation unless a migration records an
explicit replacement.

### `control.create_user_operation_invocation_attempt(...)`

Caller: exact `yo4x_worker` tenant context.

The function:

1. takes the tenant authority lock and locks the user operation;
2. requires state `accepted`/claim-bound dispatching and no open attempt;
3. derives the exact current target, policy, active assignment, fence, and
   database time;
4. rejects `reconciliation_only`, assigned, revoking, revoked, expired, or
   non-current assignments for every fresh mutation;
5. derives `execute_not_after` as the minimum of the configured invocation
   window, assignment lease minus the proof margin, and any operation deadline;
6. hashes caller-generated canonical 256-bit result/delivery bearers;
7. constructs canonical `requested.v4` itself;
8. inserts attempt, audit event, outbox message, and frozen operation binding in
   the same transaction.

It does not accept route, assignment, target state, policy digest, or deadline
as authoritative caller inputs.

### `control.claim_user_operation_delivery(...)`

Caller: exact `yo4x_supervisor_runtime` and authenticated supervisor workload.

Inputs: raw delivery bearer, delivery claim ID, raw caller-generated gateway
bearer, requested claim lifetime.

The function locks the attempt, constant-time compares bearer digests, rechecks
the exact active assignment/fence and deadline, and transitions
`pending -> delivered`. It stores only the gateway-bearer digest. It returns
redacted routing metadata and the stable attempt/claim identifiers; it returns
no provider credential or executable command.

An exact claim replay may return metadata. A caller that must replace a possibly
lost gateway bearer rotates its digest under the same row lock; the previous
bearer becomes invalid before the new call returns.

### `control.reject_user_operation_before_invocation(...)`

Caller: exact supervisor for the current `delivered` claim, or the worker's
DB-owned pending-expiry/cancellation path.

The supervisor path requires `delivered`, the exact current claim ID, and its
current gateway bearer. It cannot reject a bearer-free `pending` attempt. The
worker path may settle `pending` under its exact bounded expiry/cancellation
authority. Both paths verify that no gateway invocation ID or receipt exists
and append the DB-owned not-sent receipt atomically. A race with gateway begin
is resolved by the row lock:

- rejection wins: gateway begin returns no executable authority;
- gateway begin wins: supervisor rejection fails closed and the attempt remains
  `prepared` until provider authorization wins or DB-owned nonce expiry proves
  `not_sent`.

### `control.begin_user_operation_gateway_invocation(...)`

Caller: exact `yo4x_gateway_runtime` in the pinned gateway process.

Inputs: raw current gateway bearer and a new invocation ID.

The function locks the attempt; verifies state `delivered`, the current delivery
claim, exact gateway workload/assignment/fence, unrevoked active authority, and
`clock_timestamp() < execute_not_after`; consumes the gateway bearer; transitions
to `prepared`; creates the `gateway_invocation_started` receipt; creates a
one-use receipt bearer digest and an immutable, bounded
`credential_redemption_expires_at` no later than either `execute_not_after` or
the invocation receipt deadline; and returns exactly once:

- the raw credential-redemption nonce, which is not itself executable provider
  authority;
- the raw receipt bearer;
- the invocation receipt deadline.

The gateway receives the immutable command SHA-256, never the command
descriptor. The descriptor remains database-owned until the exact credential
runtime commits provider-call authorization.

The gateway must commit and close the begin transaction before attempting
redemption. If the database commits but the response is lost, a retry returns
no nonce or receipt bearer. Without a separately committed provider
authorization the nonce expires and the DB proves `not_sent`. This deliberate
availability cost prevents a duplicate provider invocation.

### `control.authorize_user_operation_provider_call(...)`

Caller: exact `yo4x_credential_runtime` in the dedicated credential boundary.

Inputs: raw credential-redemption nonce, exact attempt and invocation IDs, and
the authenticated gateway workload binding. The call must execute in a
different transaction after gateway `begin` has committed. It locks the
attempt, joins the immutable `gateway_invocation_started` receipt, requires
state `prepared`, requires
`clock_timestamp() < credential_redemption_expires_at` and
`clock_timestamp() < execute_not_after`, and rechecks under the same authority
and row locks that the operation/current attempt, frozen target version,
current policy/containment authority, exact route, active unrevoked assignment,
fence, and lease remain current. It then constant-time compares and consumes
the nonce digest, transitions to `authorized`, and appends the
`provider_call_authorized` receipt. On that first transition only, it returns
the immutable provider-neutral command descriptor plus redacted attempt,
invocation, authorization, target, receipt, and expiry metadata to the exact
credential runtime. An exact replay returns no descriptor or receipt digest.
It never returns a credential reference, secret, or reusable grant.

A pre-commit concurrent authorization cannot observe the start row and fails.
A nonce produced by a rolled-back begin has no durable digest and can never be
authorized. Equality with the redemption expiry fails. An exact retry after a
committed authorization returns no new authority. The credential boundary must
commit and close the authorization transaction before resolving its local
secret and owns exactly one provider call for that immutable command binding.
It never returns a reusable grant to gateway code. A crash after authorization
is ambiguous and the call is not retried, even if the process cannot prove that
it reached the provider.

Authorization and expiry/revocation/cancellation/supersession race on the same
locks. If invalidation wins, the nonce cannot authorize and the DB records
`not_sent` after proving no authorization receipt exists. If authorization wins,
later invalidation cannot retroactively classify the attempt as not sent.

### `control.record_user_operation_gateway_observation_v5(...)`

Caller: exact gateway runtime holding the one-use receipt bearer.

The function accepts only `authorized`. It joins the exact attempt, start, and
provider-call authorization receipts, consumes the receipt bearer, and
persists either broker-confirmed
`succeeded` or broker-confirmed `diverged` evidence. It never accepts
`not_sent`. Provider timeout, IPC loss, or non-conclusive provider status may
append an ambiguous receipt but cannot terminalize the user operation.

### `control.record_user_operation_result_v5(...)`

Caller: exact `yo4x_runtime_evidence` tenant context.

The external result DTO contains no `PreInvocationNotSentProven` or
`GatewayInvoked` fields. It references:

- attempt and invocation IDs;
- original dispatch and operation IDs;
- gateway receipt ID/digest, or reconciliation challenge/consumption IDs;
- the raw result/challenge capability;
- exact original target/policy binding;
- result ID, request digest, outcome, observation digest, and observation time.

For an initial succeeded/diverged result, SQL derives provider-call authority
from immutable `gateway_invocation_started`, `provider_call_authorized`, and
observation receipts. For a
challenge result, SQL derives observation authority from the exact current
challenge and its one-use consumption. Exact replay remains idempotent after
expiry; conflicting identity or request reuse fails. A first use of the result
capability requires `clock_timestamp() < result_capability_expires_at`. After
that exclusive expiry, a late first result requires a fresh attempt-bound
observation challenge, unless the Worker consumes an already committed gateway
observation receipt through a DB-owned path that requires no caller bearer.

### `control.advance_user_operation_invocation_timeouts(integer)`

Caller: exact worker.

Using database time and bounded batches, this function:

- transitions expired `pending|delivered` attempts to `not_sent` with a
  DB-derived receipt;
- transitions `prepared` attempts whose redemption nonce expired without an
  authorization receipt to `not_sent`;
- transitions stale `authorized` attempts to `ambiguous`;
- never turns `authorized|ambiguous` into `not_sent`;
- never silently expires protective intent.

## Message and result versioning

### Initial mutation: `yo4x.<operation>.requested.v4`

Required canonical fields include:

- `schemaVersion: 4`;
- `attemptId`, `operationId`, `dispatchMessageId`, `tenantId`;
- operation/target identity and requested state;
- submitted resource version;
- exact target binding, route, fence, assignment, and worker IDs;
- policy and command SHA-256 digests;
- `dispatchedAtUtc`, `executeNotAfterUtc`;
- raw delivery capability;
- raw result capability and `resultCapabilityExpiresAtUtc`.

The receiver rejects unknown fields, non-canonical timestamps/digests, wrong
message type/version, and `now >= executeNotAfterUtc`. Local validation never
replaces the database delivery/gateway claims.

`requested.v2` is proof-only legacy data. The bounded-deadline
`requested.v3` contract is transitional: it lacks database-owned attempt state
and a one-shot gateway begin receipt. An execution-enabled consumer must not
deserialize or route either version as executable authority.

### Observation challenge: `yo4x.user-operation.reconciliation-requested.v3`

The challenge binds the attempt ID, original dispatch ID, frozen command/target
digests, current observation route/fence/assignment, challenge capability and
exclusive expiry. It contains no delivery/gateway bearer and cannot authorize a
provider mutation. `reconciliation_only` is permitted only on this path.

### Result: `v5`

Result `v5` removes caller-owned invocation-state booleans. The persisted result
stores the exact attempt and receipt/challenge linkage. A server may continue to
read historical v4 rows for audit, but an execution-enabled receiver must emit
v5 and the readiness contract must reject a runtime configured to accept v4 for
new mutations. Current v4 ingress is observation-only: it rejects `failed`
entirely and accepts only `succeeded|diverged` with
`preInvocationNotSentProven=false` and `gatewayInvoked=true`. Existing v4 failed
rows are audit-only and cannot terminalize a user operation or drive deployment
state projection.

## Expiry and timing rules

- PostgreSQL `clock_timestamp()` decides claim and capability validity.
- Every check uses exclusive expiry: `authority_now < expires_at`.
- `execute_not_after` is immutable and cannot be extended by lease renewal.
- `credential_redemption_expires_at` is immutable, no later than
  `execute_not_after`, and checked directly at provider-call authorization.
- `result_capability_expires_at` is stored immutably. Exact accepted replay is
  permitted after expiry, but a caller cannot first consume the capability at
  or after equality.
- Authorization is proved at `provider_call_authorized_at` and its immutable
  receipt; a later conclusive broker
  observation is not discarded merely because the original assignment expired
  after authorization. It must still arrive through the bound receipt/result capability
  or a current reconciliation challenge.
- A challenge observation must be at or after challenge issue and strictly
  before challenge expiry.
- An observation challenge may be issued only for an attempt with immutable
  `gateway_invocation_started` and `provider_call_authorized` receipts and state
  `authorized|ambiguous`; issuance transitions a stale `authorized` attempt to
  `ambiguous`. It is forbidden for `pending`, `delivered`, `prepared`,
  `not_sent`, or `observed` attempts.
- Future-clock skew is bounded; there is no generic receipt-time age cutoff that
  discards a valid immutable pre-revocation proof.

## Retry and idempotency rules

- Outbox delivery can retry because delivery claim is not provider invocation.
- Gateway begin is one-shot and intentionally does not replay its command,
  redemption nonce, or receipt bearer after a committed response loss.
- A new invocation attempt may be created only after the previous attempt is
  durably `not_sent`.
- Risk-increasing operations default to terminal failed/expired after not-sent;
  retry requires a fresh policy-authorized user operation.
- Protective operations retain their durable intent. They may create a new
  attempt under fresh active or separately modelled containment authority after
  the old attempt is proven not sent.
- No mutation is retried after `authorized`, `ambiguous`, or an outbox state
  that is not paired with an exact unconsumed attempt. A lost begin may retry
  only after the DB itself proves its nonce expired unconsumed and records
  `not_sent`.
- Result, receipt, delivery-claim, and challenge replays require exact IDs and
  canonical request digests. Conflicting reuse raises a stable conflict error.

## Credential and secret handling

- The attempt stores only credential-reference and capability digests.
- The supervisor cannot read a credential reference usable by a provider.
- Gateway begin returns only an attempt-bound redemption nonce. After seeing
  the committed start receipt, the dedicated credential boundary commits a
  one-shot provider-call authorization and then owns exactly one call. It does
  not return a grant or raw account secret to gateway code.
- SQL parameter logging and error parameter logging are disabled for every role
  that handles a bearer.
- Bind buffers are cleared where the driver permits; managed immutable strings
  are kept out of logs, exceptions, audit payloads, and long-lived caches.
- Outbox access is restricted to exact worker delivery capabilities. If the
  deployed transport cannot protect a clear delivery bearer at rest, the v4
  payload must be envelope-encrypted to the receiver before mutation is enabled.

## Readiness and schema attestation

Mutation readiness is false unless all of the following are true:

- the global semantic schema fingerprint matches the pinned PostgreSQL major
  version and exact protocol schema;
- exact symmetric role capability fingerprints match all protocol roles;
- every protocol trigger/function/ACL/owner/search path matches the manifest;
- the router and gateway advertise support for initial v4 and result v5 only;
- the dedicated credential boundary advertises the exact prepared-to-authorized
  one-shot provider-call protocol, is healthy, and has no reusable-grant path;
- the gateway subprocess binary, launch closure, vendor artifact, runtime image,
  and configuration digests are pinned;
- the result/receipt deadline worker is healthy and backlog age is within SLA;
- a live self-test proves the supervisor cannot obtain executable authority and
  the gateway cannot begin after deadline;
- a live credential-boundary self-test proves an uncommitted or unauthorized
  attempt cannot call a provider and one committed authorization permits at
  most one provider call;
- no legacy v2 or transitional v3 mutation destination is registered for
  execution.

Readiness must fail after revoking a required function/column, adding an extra
grant, disabling a guard/internal constraint trigger, disabling FORCE RLS,
altering a policy, or changing a function definition.

## Required acceptance tests

### PostgreSQL facts

1. Attempt, operation binding, audit, and v4 outbox commit atomically; an injected
   failure leaves none of them.
2. Raw bearer columns do not exist outside the sensitive outbox payload, and
   error/audit rows contain no bearer.
3. Forged, wrong-tenant, wrong-role, wrong-workload, wrong-assignment,
   wrong-fence, revoked, reconciliation-only, and expired claims fail.
4. Two concurrent delivery claims preserve one current claim/version.
5. Two concurrent gateway begins return a non-executable redemption nonce to
   exactly one caller.
6. Credential redemption on a concurrent pre-commit snapshot cannot observe or
   redeem the begin nonce; rollback leaves no redeemable authority. After
   commit, one authorization succeeds and all replays return no new authority.
   Authorization at deadline equality fails. Authorization racing expiry,
   revocation, cancellation, or target supersession in both lock orders either
   proves not sent or reaches `authorized`, never both.
7. Rejection-before-begin and begin run concurrently in both lock orders. If
   rejection wins, begin returns no nonce or receipt bearer; if begin wins,
   rejection cannot produce `not_sent`.
8. A committed begin with simulated response loss cannot replay the redemption
   nonce or receipt bearer; without an authorization receipt its nonce expiry
   produces DB-owned `not_sent`. A committed provider authorization with a lost
   response cannot replay the command descriptor or authorize a second call.
9. A v4 outbox message delivered at or after `execute_not_after` cannot obtain a
   begin receipt or provider-call authorization.
10. `pending|delivered` expiry and an unconsumed `prepared` nonce expiry produce
    DB-owned not-sent receipts; `authorized` expiry produces ambiguous, never
    not-sent.
11. Succeeded/diverged v5 proof requires exact attempt, start receipt,
    provider-call-authorization receipt, observation receipt, binding, result
    capability, and broker-confirmed state.
12. A caller cannot supply or influence derived gateway-invoked/not-sent fields.
13. First result-capability use fails at expiry equality; exact accepted replay
    succeeds after expiry. Any changed ID, request hash,
    receipt, route, or observation conflicts.
14. A different-assignment reconciliation challenge is accepted only through
    its exact immutable challenge/consumption binding and never as a fresh
    mutation, and cannot be issued without committed start and
    provider-call-authorization receipts.
15. PUBLIC execution and direct table mutation fail for every protocol object;
    each `SECURITY DEFINER` function has an empty search path.
16. Creation and authorization both fail for an empty applicable-policy set,
    ignore unrelated policy scopes, bind the accepted evaluation/baseline, and
    reject a changed policy snapshot or cross-user target/route.
17. Assignment lease shortening, revocation, and proof-margin equality race
    claim, begin, and authorization without permitting a call beyond current
    lease authority.
18. Delivery, result, gateway, redemption, receipt, and challenge bearers are
    globally class-unique; reusing any raw bearer in another class fails.
19. An upgraded v2/v3 row is deterministically backfilled with its suffix
    version, generic v3 producers persist version 3, direct v4 producers persist
    version 4, and a mixed legacy/v4 claim preserves each exact outer version.
20. Supervisor rejection of a pending attempt without an exact current claim
    fails. Credential-runtime ambiguity recording requires the exact authorized
    attempt/start/authorization binding and returns its immutable receipt.

### Process and host tests

1. The supervisor/router load closure contains no vendor assembly and has no
   credential-use provider.
2. Gateway invocation requires the DB-issued one-shot gateway bearer, a
   committed begin receipt, and a post-commit provider-call authorization; the
   credential boundary owns the single call in the pinned child process.
3. Kill the supervisor before delivery claim, after delivery claim, and while
   handing off; no provider call is possible without gateway begin.
4. Kill the gateway before begin or after committed begin but before provider
   authorization; nonce expiry proves not sent. Kill the credential boundary
   after committed authorization, during the provider call, or after provider
   response but before receipt; every such case is ambiguous and reconciles.
5. Timeout/kill paths terminate the child or fail-stop the host; no overlapping
   retry can use the same attempt.
6. Logs, traces, metrics, crash output, and HTTP errors contain no raw bearer or
   credential material.

### End-to-end release facts

1. A delayed risk-increasing message cannot execute after its deadline.
2. A protective not-sent attempt retains intent and obtains a new attempt only
   under fresh authority.
3. A provider effect followed by lost acknowledgement remains unknown, is never
   retried, and is settled by broker-confirmed reconciliation.
4. An authenticated result from a superseded assignment cannot claim fresh
   mutation authority, while an exact observation challenge remains admissible.
5. Frontend and API status distinguish `not_sent`, `prepared`, `authorized`,
   `ambiguous`, `reconciling`, `succeeded`, and `diverged/partial` without claiming that
   outbox publication proves execution.

## Release gate

Until this protocol or a demonstrably equivalent DB-owned handoff is
implemented and verified, production/demo mutation execution remains disabled.
Static strategy conversion, proof-only gateway behavior, durable request
creation, and reconciliation scaffolding do not satisfy this gate.
