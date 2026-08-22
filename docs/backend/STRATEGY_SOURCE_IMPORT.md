# Authenticated strategy-source import

Status date: 2026-08-22 UTC

## Scope

YO4X can create an MFA-gated, tenant/user-bound import session and persist an immutable static inventory plus the exact `.mq5`/`.mqh` bytes. This path is deliberately **not** a semantic converter, compiler, deployment approval, or trading authorization.

The import boundary accepts only the verification state `static-inventory-only`. Persisted records explicitly keep parser/type-check, semantic-conversion, MetaEditor-compile, reference-parity, and demo-runtime proof flags false.

## Authority flow

1. An authenticated, verified user creates `POST /v1/strategy-source-import-sessions` with an allowlisted source label and an idempotency key.
2. The control API writes a job containing immutable tenant, user, server correlation, label, expiry, and only `SHA-256(decoded capability)`; it returns the capability once in a `Cache-Control: no-store` response.
3. The quarantined conversion worker receives the job ID as an argument and the 256-bit capability through `YO4X_CONVERSION_IMPORT_CAPABILITY`. Tenant ID, user ID, source label, correlation ID, and capability command-line options are rejected.
4. `control.acquire_strategy_import_job(job_id, raw_capability)` hashes the transient bind value inside PostgreSQL, compares it with the persisted digest, derives tenant/user/correlation from the immutable job, and reserves the job with `reservation_id = job_id`.
5. Before opening the persistence transaction, the worker reruns the sealed static analyzer over the retained source bytes and requires byte-for-byte manifest JSON parity. Caller-supplied or mutable analysis claims are not trusted.
6. Row-level security, insert triggers, tenant authority locks, and the live reservation independently bind every corpus/file insert to that job. PostgreSQL requires the manifest's top-level fields to equal the corpus columns and requires every file row to exactly equal its indexed manifest object, including all include, feature, finding, disposition, and verification evidence.
7. `control.complete_strategy_import_job(job_id, audit_id, outbox_id)` rechecks wall-clock expiry immediately before completion, recomputes counts, bytes, contiguous manifest ordering, corpus SHA-256, and disposition counts, then atomically consumes the job and writes fixed, source-free audit/outbox evidence. A deferred constraint forbids committing a corpus or partial files unless that same transaction reaches the consumed state.

An exact consumed replay may only use the original job-derived reservation and must match every immutable digest/count. A different corpus is rejected. Expired and revoked capabilities fail closed.

## Secret and source handling

- The capability is never persisted, audited, included in an outbox message, accepted on the command line, or placed in source evidence.
- The decoded capability crosses PostgreSQL only as a parameterized binary bind value and all owned byte arrays are zeroed on disposal.
- Runtime connection strings reject Npgsql `Log Parameters=true` and `Include Error Detail=true`.
- Runtime roles are configured with PostgreSQL `log_parameter_max_length = 0` and `log_parameter_max_length_on_error = 0`; the connection guard rejects a role/session where either setting is nonzero.
- These settings are required because PostgreSQL can otherwise include extended-protocol bind values in statement/error logs. See the [PostgreSQL 18 logging settings](https://www.postgresql.org/docs/18/runtime-config-logging.html) and [Npgsql parameter-logging guidance](https://www.npgsql.org/doc/diagnostics/logging.html).
- Raw source is stored as inert `bytea`, protected by forced tenant RLS and immutable triggers. The conversion role has INSERT-only column grants and cannot read source, create/promote strategy versions, or access broker credentials/runtime commands.
- Exact source, manifest, and report byte digests are checked again inside PostgreSQL. Database defaults own immutable source-evidence timestamps.

## Bounded input

The database enforces the job lifetime (maximum 30 minutes), source file/count/total-byte ceilings, manifest/report size ceilings, and per-file limits for entrypoints, includes, features, and findings. Current application validation is not the only resource boundary.

## Worker invocation contract

The worker requires these non-authoritative arguments:

```text
--static-inventory
--source-root <directory>
--manifest-output <file>
--report-output <file>
--persist-postgres
--import-job-id <canonical UUID>
```

Database connectivity is supplied through `YO4X_CONVERSION_POSTGRES_CONNECTION`; the one-time bearer is supplied through `YO4X_CONVERSION_IMPORT_CAPABILITY`. Production database connections require TLS certificate verification. Loopback plaintext requires the explicit development-only switch and is not accepted for a remote host.

No supplied MQL is executed by this command. Compile/runtime evidence remains governed by [MQL5_ISOLATED_COMPILE_ORCHESTRATION.md](./MQL5_ISOLATED_COMPILE_ORCHESTRATION.md).
