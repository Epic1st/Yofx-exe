# PostgreSQL tenant-context capability boundary

Status date: 2026-08-23.

## Centralized connection security

Every PostgreSQL data source that crosses a YO4X runtime boundary is now
validated at the single construction chokepoint (`PostgresDatabase`). The
central fail-closed policy (`PostgresRuntimeConnectionPolicy`) enforces both
halves of every connection:

- safe session configuration (no caller-controlled diagnostics or session
  state, including legacy `Trust Server Certificate`, multiplexing, and
  reset-on-close escapes), and
- required transport (`VerifyFull`, or an explicit loopback endpoint only when
  the development escape is explicitly enabled by the composing host).

Migrator connections pass through the same validation as runtime connections;
there is no unvalidated construction path left. The tenant-context issuer
connection normalizer routes through the same policy instead of an inline TLS
check, so issuer and runtime transports cannot drift apart.

## Security claim

YO4X tenant authorization does not trust caller-writable PostgreSQL settings.
Runtime code cannot establish authority by writing `yo4x.*` GUCs. Every general
tenant transaction must consume a one-use capability issued through a database
credential that is separate from the runtime credential.

This boundary limits compromise of a runtime PostgreSQL login. It does not
protect against compromise of the application process, its configuration or
secret store, the context-issuer credential, the migrator, a PostgreSQL
superuser, or the database host.

## Role split

- `yo4x_context_authority` is `NOLOGIN NOINHERIT`. It owns only the protected
  capability relation and its security-definer context functions.
- `yo4x_context_issuer` is `LOGIN NOINHERIT`. It has the exact catalog/readiness
  grants plus execute-only access to issue and clean up capabilities. It has no
  runtime DML and does not own the protected objects.
- Runtime roles can activate a capability but cannot issue one or read/write
  the protected capability relation directly.

The semantic catalog and exact role-capability fingerprints are readiness
requirements. Extra grants, missing grants, role membership, a wrong role,
unsafe role settings, or catalog drift fail readiness closed.

## Transaction protocol

1. The runtime connection opens a transaction and reads the exact database,
   session role, backend PID, and assigned full transaction ID.
2. The provider creates exactly 32 random bytes (256 bits, not all-zero), hashes
   them with SHA-256, and sends only the digest and exact binding facts over the
   issuer connection.
3. PostgreSQL records a 15-second activation window bound to database OID/name,
   runtime role/OID, backend PID, transaction ID, tenant, actor, correlation,
   and optional session. The hard row lifetime is two minutes.
4. The runtime transaction presents the raw capability once to
   `control.activate_tenant_context(...)` with the exact context.
5. The client immediately reads every protected `control.current_*()` value and
   verifies the exact context before returning the transaction to application
   code.

Wrong, expired, replayed, cross-role, cross-backend, cross-transaction, or
cross-context capabilities fail with a generic authorization error. Owned raw
capability and digest buffers are zeroed on success, cancellation, provider
failure, activation failure, and disposal. Capability values are never included
in `ToString()` output or retained in options.

Issuance opportunistically removes committed activated rows and expired unused
rows with bounded `SKIP LOCKED` cleanup. The issuer also has an explicitly
bounded cleanup function for operational maintenance.

## Configuration and readiness

Hosts that are permitted to issue general tenant contexts require both their
normal runtime connection and `ConnectionStrings:ContextIssuer`. The issuer
connection must:

- authenticate as exactly `yo4x_context_issuer`;
- target the same normalized host, port, and database as every runtime pool it
  serves;
- use `SSL Mode=VerifyFull` outside the explicitly non-TLS disposable integration
  fixture;
- reject diagnostic parameter logging, caller-controlled `Options` or search
  paths, state-retaining pool modes, multiplexing, and certificate-trust bypass;
- use a connection-open timeout no greater than five seconds and a five-second
  command timeout.

The issuer DSN is a secret. Supply it through the deployment secret mechanism,
never source-controlled settings, command-line arguments, logs, health payloads,
or child-process environments. Safe endpoint identity exposes only normalized
host, port, and database.

Readiness opens the issuer connection with a bounded timeout, proves the exact
issuer role and semantic fingerprint, and checks the endpoint before probing
runtime database identities. Missing, unreachable, wrong-password, wrong-role,
wrong-endpoint, unsafe-TLS, or drifted issuer configuration reports not ready.
`BeginTenantTransactionAsync` also fails before opening a runtime connection if
no provider exists.

## Process composition

| Process | General issuer provider | Required behavior |
|---|---|---|
| Control Plane API | `ConnectionStrings:ContextIssuer` | Required for PostgreSQL composition and readiness. One provider is shared by the control, worker-runtime, and runtime-evidence pools targeting the same endpoint. |
| Admin BFF | `ConnectionStrings:ContextIssuer` | Required; readiness proves both issuer and admin login contracts. |
| Secret Ingestion API | `ConnectionStrings:ContextIssuer` | Required; readiness proves both issuer and ingestion login contracts. |
| Control Plane Workers | `ConnectionStrings:ContextIssuer` | Required. This is a trusted internal host with no supplied-code execution. The claim remains limited to theft of its runtime database credential. |
| Conversion Worker | None | Strategy import uses its independent, single-use job capability. Acquisition establishes the exact context inside the same raw transaction as corpus, file, classification, audit, outbox, completion, and commit writes. No general issuer DSN enters this process. |
| Gateway Host | None by default | The broad issuer DSN must not be co-located. Enabling the one-shot broker command store without a pre-registered narrowly scoped external/IPC provider fails startup with a redacted capability-unavailable error. |

Gateway PostgreSQL command execution remains an activation blocker until a
separately scoped provider or authenticated IPC boundary is implemented and
registered. Do not work around this by copying `ConnectionStrings:ContextIssuer`
into Gateway Host configuration.

## Deployment checklist

1. Apply the migration and least-privilege role script together.
2. Provision distinct passwords for each runtime login and
   `yo4x_context_issuer`; never reuse the migrator or runtime password.
3. Inject `ConnectionStrings:ContextIssuer` only into the four approved hosts
   above, and verify secret-store access policy excludes Gateway and Conversion.
4. Require TLS hostname verification and confirm runtime and issuer endpoint
   identities match exactly.
5. Block traffic/work until readiness is green; do not treat liveness as
   database authorization readiness.
6. Alert on issuer readiness failures, generic activation authorization errors,
   and sustained capability cleanup growth without logging SQL parameters.
