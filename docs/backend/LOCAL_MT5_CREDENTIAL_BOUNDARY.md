# Local MT5 credential boundary

## Status and scope

`YO4X.LocalSecrets.Windows` is an isolated, Windows-only boundary for importing
MT5 demo credentials into a local DPAPI vault. It is not a cloud secret provider
and is not referenced by GatewayHost. A separate dedicated Windows connection
probe worker consumes it only for bounded connect/identity-read/disconnect use.
`YO4X.SecretIngestion.Api` remains fail closed until a managed secret provider
is selected and configured.

The boundary protects local storage and retrieval. It does not establish that a
vendor DLL is safe, licensed, or production-capable, and it never authorizes an
order or other broker command. Runtime consumption is wired only to the
dedicated connection-only worker. On 2026-08-24 that worker proved one Vantage
demo login and confirmed disconnect without rendering credentials. GatewayHost
remains disconnected from this vault and mutation submission remains disabled.

## Components

- `YO4X.LocalSecrets.Windows` owns strict source parsing, case-consistent
  server/login binding keys, Windows DPAPI protection, batch persistence, and
  disposable plaintext material.
- `YO4X.LocalCredentialImporter` accepts only an absolute source path, an
  operator-approved source SHA-256 digest, an optional vault directory, and an
  explicit rotation flag. Passwords are never command-line values or output.
- `YO4X.LocalCredentialWriter` stores exactly one credential, read from standard
  input, for the control-plane API. It is the only route from the web link
  dialog to the vault. See "Web link-dialog credential entry" below.
- `YO4X.LocalSecrets.Windows.Tests` uses synthetic credentials to exercise the
  parser, secret lifecycle, DPAPI, ACL, paths, atomic batch behavior, recovery
  residue, concurrency, tamper handling, evidence schema, the writer process,
  and the API's end-to-end handoff into the vault.

## Security invariants

1. Source and vault paths must be fully qualified paths on a fixed local drive.
   UNC/device paths, alternate data streams, and every existing reparse-point
   ancestor are rejected. The source must be a regular file no larger than
   64 KiB.
2. The exact source bytes must match the approved SHA-256 digest before any
   credential is returned. The source is opened without write/delete sharing,
   length is rechecked, and the path chain is revalidated while the handle is
   open.
3. Each block contains exactly `MT5 Login`, `MT5 Password`, and `MT5 Server` in
   that order. Unknown fields after the credential section begins, incomplete
   or duplicate fields, duplicate case-insensitive server/login bindings,
   invalid UTF-8, NULs, and out-of-bound values fail the whole parse. One
   space or tab after the field separator is syntax; additional leading or any
   trailing password whitespace is rejected as ambiguous instead of trimmed.
4. A credential owns a byte buffer. Password callbacks, snapshots, comparison,
   serialization, and disposal share one lifecycle lock; copies used for
   comparison or serialization are zeroed. Rendering exposes only an opaque
   credential key and masked metadata.
5. Persisted files contain DPAPI `CurrentUser` ciphertext with additional
   entropy bound to the exact credential key. PostgreSQL receives neither the
   password nor this local ciphertext.
6. The vault root has inheritance disabled and explicit full-control entries
   only for the current Windows user, Local System, and built-in Administrators.
   Administrators and processes running as the same user remain inside this
   local trust boundary. A fixed-size identity marker binds the exact canonical
   root and current user. Existing custom roots and their parents are
   validation-only: the importer never repairs or re-ACLs an arbitrary path.
7. `CreateOrVerify` is idempotent and rejects conflicting material. `Rotate`
   requires an existing binding and is replay-idempotent. A vault-wide
   cross-process lock serializes batches and rotations.
8. A whole import is preflighted before mutation. Changed ciphertext is staged
   and flushed, then a bounded redacted recovery journal is flushed before any
   promotion. The journal binds the batch, vault identity, credential keys,
   existence state, and pre/post ciphertext digests. Committed or rolled-back
   state is reopened, digest-checked, and flushed before transients are retired;
   the journal is deleted last. Any residual journal, `.stage-*`, or
   `.backup-*` entry blocks open, store, and delete until an operator completes
   manual recovery.
9. Retrieval bounds the ciphertext read, uses DPAPI, and revalidates the
   embedded credential key. Corrupt, moved, wrong-user, or tampered ciphertext
   fails closed.

## Web link-dialog credential entry

The "Link a trading account" dialog collects the MT5 password. This is a
deliberate product decision and it changes who types the credential, not where
it is kept: the plaintext still ends in this DPAPI vault and still never reaches
PostgreSQL.

The path, in order:

1. The browser posts the password in the JSON body of
   `POST /v1/broker-accounts`, alongside the unmasked login the service needs to
   re-derive the binding. Never a query string, never a header, never browser
   storage. The dialog holds it in component state and clears it when the dialog
   opens and after a successful link.
2. The control-plane API refuses the request unless it arrived over loopback. A
   control plane reached across a network has no vault to write to and must not
   hold a broker password even briefly.
3. `Utf8SecretJsonConverter` copies the password's UTF-8 bytes straight out of
   the request body into a buffer the process owns. It is never materialized as
   a `string`, which could not be overwritten. `Utf8Secret` renders as
   `[REDACTED]` and refuses serialization outright.
4. The API re-derives the credential key and the masked login from the login and
   server, and rejects the request if either disagrees with what the browser
   claimed. A fingerprint that does not follow from the login and server would
   name a vault entry the connection probe never looks up.
5. The account row is written first. Authorization comes before the secret: the
   insert proves this tenant may link this server, and it persists only the
   masked login and the opaque binding fingerprint.
6. The API then spawns `YO4X.LocalCredentialWriter`, whose path and SHA-256 are
   pinned in configuration and re-verified on every call, and writes one
   credential block to its standard input. The password is never an argument: a
   command line is readable by every process on the host.
7. The writer verifies that the bytes it received hash to the digest the parent
   intended, parses them with the same `Mt5CredentialFileParser` the operator
   importer uses, refuses the write unless the derived credential key equals the
   one the API is persisting, and stores it with `CreateOrVerify`. A different
   password already bound to the same server/login is a conflict, not an
   overwrite; only an explicit rotation may replace one.
8. The writer prints a receipt carrying only the credential key, the masked
   login, and `secretsRendered = false`. Its failure modes are fixed codes on
   standard error, never parser or vault text that could quote a value.
9. Both plaintext buffers — the API's transit block and the request's
   `Utf8Secret` — are zeroed when the call returns, on every path including a
   rejection, a timeout, and a failed write.

The capability is fail closed. With no `LocalBrokerCredentialVault` section
configured the API resolves `UnavailableLocalBrokerCredentialVault`, which
refuses the write rather than falling back to any other store. A deployment that
cannot reach an on-device vault therefore cannot accept a password at all.

Known gap: the account row is committed before the vault write. If the vault
write fails, the account exists with no credential, and a retry is refused by the
unique binding constraint. Recovery is the operator importer or an explicit
rotation. The alternative — writing the secret before the row that authorizes it
— was rejected.

The password is also outside this process's control in one place that cannot be
fixed from here: the Kestrel request buffer that carried the request body. That
buffer is pooled and not zeroed on release.

## Atomicity and local-host limitations

Batch atomicity is claimed only for ordinary failures that the process can
observe and roll back. It is not filesystem crash atomicity: a power loss or
process termination during promotion can leave a mixed generation. The durable
recovery journal and transients preserve bounded evidence and prevent silent
continuation, but recovery is deliberately not automatic. Preserve the vault,
stop writers, and have an authorized operator verify the journal-bound intended
generation before resolving recovery files. A process death after atomically
creating a new custom root but before writing its identity marker also produces
a safe manual-recovery availability failure.

Path validation narrows accidental and lower-privilege redirection. It cannot
defend against an administrator, a process already running as the same Windows
identity, or every same-privilege filesystem race. Use a dedicated Windows
identity and a fixed local NTFS volume for this boundary.

Concurrent explicit rotations are serialized; when two independently
authorized rotations target the same binding, the later serialized request is
the final value. The current CLI does not carry an expected-generation token,
so it does not provide optimistic compare-and-swap semantics.

Control-plane credential-ingestion proof keys use the bounded current/previous
rotation procedure in [PROOF_KEY_ROTATION.md](./PROOF_KEY_ROTATION.md). Exact
idempotent replay selects the persisted non-secret key identifier and never
falls back to whichever key is current. Each proof also binds the generated
grant ID, requested operation, canonical approved client origin, tenant, actor,
broker account, and idempotency key; a later reuse of the idempotency key cannot
reissue the captured proof for a different grant.

## Plaintext source lifecycle

Import intentionally does not delete, move, truncate, quarantine, or otherwise
modify the operator-supplied source file. The supplied plaintext credential
file therefore remains present until the operator handles it. After verifying
the protected import, the operator must rotate credentials where appropriate
and remove the plaintext through the approved host procedure. OS caches,
backups, endpoint tooling, and administrator access are outside DPAPI vault
protection. Never place the source in the repository or logs.

## Evidence

Successful importer output uses
`yo4x.local-credential-import-evidence.v3`, camel-case fields, and records:

- `evidenceAuthority = unsigned-local-observation` and
  `cryptographicallyAttested = false`;
- the approved source digest and exact byte count;
- fixed-local, non-reparse SHA-256 digests for both the importer entry assembly
  and `YO4X.LocalSecrets.Windows.dll`, read through handles that deny
  write/delete sharing for the full import and checked both before and after it;
- mode and created/unchanged/rotated counts;
- a SHA-256 binding to the validated root/user-specific vault identity, captured
  under the same vault lock before mutation;
- `secretsRendered = false` and DPAPI protection; and
- `evidenceContentSha256`, a deterministic hash of the other evidence fields.

The content hash detects accidental alteration but is not a signature and does
not establish provenance. A trusted signing identity and external timestamp
would be required for cryptographic attestation. The assembly hashes do not
bind the .NET runtime, Windows installation, or other host components.

Credential replay evidence is kept only as ignored, host-local material under
`artifacts/verification/credentials/`. It deliberately is not eligible for a
repository commit because its source digest can act as an offline oracle for a
password-bearing plaintext file. A v3 maintenance replay must confirm an
unchanged source, the expected binding counts, held assembly hashes, the vault
identity binding, an independently recomputed content hash, and
`secretsRendered = false`. Even then it remains an unsigned local observation,
not remote attestation or broker-login evidence.

Host-local v1 and v2 files are retained only as **unsigned legacy local
observations**. V1 has no tool hash or self-hash; v2 has no destination identity
binding. Neither can independently prove its historical run.

## Verification commands

These commands use only synthetic test data unless an operator explicitly
supplies a source path:

```powershell
dotnet test tests/YO4X.LocalSecrets.Windows.Tests/YO4X.LocalSecrets.Windows.Tests.csproj `
  --configuration Release

dotnet run --project src/Tools/YO4X.LocalCredentialImporter/YO4X.LocalCredentialImporter.csproj `
  --configuration Release -- `
  --source <absolute-approved-source-path> `
  --sha256 <approved-source-sha256>
```

Do not run the importer against the supplied plaintext source during automated
builds or toolchain probes. Import is an explicit operator maintenance action.

## Static toolchain probe

`scripts/Test-Mt5ToolchainIsolation.ps1` is a read-only host capability probe.
It requires a fixed-local, non-reparse workspace; statically hashes and checks
signatures without loading vendor assemblies; inspects examples only for
counts; queries Windows isolation controls; and never starts MetaTrader,
MetaEditor, MetaTester, WSL, a VM/container command, or supplied MQL. WSL state
is read from the user registry rather than a PATH-resolved executable. Its v4
JSON is also explicitly an unsigned, non-attested local observation, and its
verdict remains fail closed until an isolated runner is actually configured.
V4 binds the probe script bytes, records the vendor example as absent with zero
non-rendering credential/order-reference counters, and carries a deterministic evidence content
hash. The checked observation is retained at
`artifacts/verification/toolchain/mt5-toolchain-isolation.v4.json`; the v2 and v3 files
are legacy. Direct and fixed-system `powershell.exe -NoProfile -File`
invocations must return the same invariant verdict without starting any
MetaTrader process.
