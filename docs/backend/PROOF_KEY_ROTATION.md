# Replay-safe proof-key rotation

Status date: 2026-08-23 UTC

The Control API uses separate 256-bit HMAC keys for credential-ingestion
proofs and strategy-import capabilities. PostgreSQL stores only the proof hash
and a non-secret lowercase SHA-256 key identifier. Idempotency responses also
store that identifier so a replay selects the exact key that originally issued
the proof.

## Configuration

The current keys are required:

- `SecretIngestion:CredentialProofKeyBase64`
- `Conversion:ImportProofKeyBase64`

Each value must decode to exactly 32 non-zero bytes. During a rotation, configure
at most one previous key and its UTC retirement deadline as an inseparable pair:

- `SecretIngestion:PreviousCredentialProofKeyBase64`
- `SecretIngestion:PreviousCredentialProofKeyRetainUntilUtc`
- `Conversion:PreviousImportProofKeyBase64`
- `Conversion:PreviousImportProofKeyRetainUntilUtc`

Current and previous material must differ. At startup, both retirement
deadlines must be strictly more than **24 hours and 7 minutes** in the future:
the 24-hour PostgreSQL idempotency replay lifetime, the supported five-minute
absolute process-to-database clock skew, and the two-minute maximum production
API request window. A completed request can need the original key long after
its short-lived credential grant or import job has become terminal.
An incomplete pair, malformed key, non-UTC deadline, duplicate key, or deadline
that does not cover this entire window leaves the PostgreSQL Control API
unregistered and therefore not ready. The boundary is exclusive: a deadline
exactly 24 hours and 7 minutes from startup is rejected.

The production API applies the two-minute request timeout. Readiness compares
the process clock with PostgreSQL `statement_timestamp()` on every probe and
fails closed when their absolute difference exceeds five minutes. The timeout
margin covers a replay that PostgreSQL admits immediately before its 24-hour
record expires; the skew margin prevents the process clock from retiring the
key first. Operators must stop routing an instance whenever the readiness probe
fails and must keep database and host clocks synchronized within this bound.

## Distributed two-phase rotation

The second slot is passive and may hold the next key during pre-staging or the
retiring key after activation. A safe rolling A-to-B change has two mandatory
phases:

1. Pre-stage B as `Previous...` on every A-current instance. Do not allow any
   B-current instance to issue proofs while an A-only instance can still serve
   idempotent replay traffic. B's passive deadline must cover the entire planned
   activation window; refresh the pre-stage deployment before the flip if it
   would not.
2. After the fleet proves there are no A-only instances, flip every instance to
   B-current/A-passive and require readiness before routing traffic.
3. Keep A passive until the retirement deadline is later than the last moment
   any A-current instance could issue a proof plus the full 24-hour idempotency
   replay lifetime, five-minute clock-skew allowance, and two-minute in-flight
   request allowance. The startup check is only a local lower bound; the rollout
   controller must include the time until the last A-current issuer is drained.
4. After that deadline and after all A-issued grants are terminal, remove both
   passive-key settings on the next deployment.

During overlap, key-ID selection is symmetric: A-current/B-passive can replay a
B-issued proof and B-current/A-passive can replay an A-issued proof. The rollout
controller must enforce the no-A-only-overlap invariant; one process cannot
prove the configuration of its peers.

Unknown, removed, or expired key identifiers fail closed with a backend
capability-unavailable response. Replay never falls back to the current key.
Key identifiers do not change the HMAC proof material and are not bearer
credentials. Raw keys and returned proofs remain excluded from PostgreSQL,
logs, audit events, and outbox messages.

Credential proof version 2 binds the tenant, actor, broker account, generated
grant identifier, requested operation, canonical HTTPS client origin, and
idempotency key with unambiguous length-delimited variable fields. Reusing an
idempotency key after its record is retired therefore cannot reproduce a
captured bearer or nonce for a different grant. The database remains the
authority for the exact grant's immutable origin, expiry, and lifecycle state;
expiry is intentionally not duplicated in the HMAC because PostgreSQL assigns
it atomically from `statement_timestamp()` and rejects use at or after that
boundary.

At idempotency expiry, PostgreSQL retires the current key slot without deleting
the historical row. Foreign-key and audit evidence therefore remain stable,
while a partial unique current-slot index ensures concurrent reacquisition
creates exactly one replacement record. A retired row is never replayed or
rewritten.
