# Local MT5 credential boundary

## Status and scope

`YO4X.LocalSecrets.Windows` is an isolated, Windows-only maintenance boundary
for importing MT5 demo credentials into a local DPAPI vault. It is not a cloud
secret provider and is not referenced by GatewayHost or the cloud/runtime
projects. `YO4X.SecretIngestion.Api` therefore remains fail closed until a
managed secret provider is selected and configured.

The boundary protects local storage and retrieval. It does not establish that
a vendor DLL is safe, authentic, licensed, or production-capable; it does not
prove a broker login; and it never authorizes an order or other broker command.
Runtime consumption is not wired. Any future consumer must independently pass
the signed-assignment, fresh-lease, account-binding, execution-mode, and policy
gates before opening a credential.

## Components

- `YO4X.LocalSecrets.Windows` owns strict source parsing, case-consistent
  server/login binding keys, Windows DPAPI protection, batch persistence, and
  disposable plaintext material.
- `YO4X.LocalCredentialImporter` accepts only an absolute source path, an
  operator-approved source SHA-256 digest, an optional vault directory, and an
  explicit rotation flag. Passwords are never command-line values or output.
- `YO4X.LocalSecrets.Windows.Tests` uses synthetic credentials to exercise the
  parser, secret lifecycle, DPAPI, ACL, paths, atomic batch behavior, recovery
  residue, concurrency, tamper handling, and evidence schema.

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
is read from the user registry rather than a PATH-resolved executable. Its v3
JSON is also explicitly an unsigned, non-attested local observation, and its
verdict remains fail closed until an isolated runner is actually configured.
V3 binds the probe script bytes and carries a deterministic evidence content
hash. The checked observation is retained at
`artifacts/verification/toolchain/mt5-toolchain-isolation.v3.json`; the v2 file
is legacy. Direct and fixed-system `powershell.exe -NoProfile -File`
invocations must return the same invariant verdict without starting any
MetaTrader process.
