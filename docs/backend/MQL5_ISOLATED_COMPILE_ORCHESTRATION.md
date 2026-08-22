# MQL5 isolated compile orchestration

Status date: 2026-08-22 UTC

## Current truthful state

The compile orchestration boundary is implemented and tested, but no supplied MQL5 source was compiled or executed. No MetaTrader terminal was launched, no account login was attempted, and no trade was submitted by this work.

The checked-in static inventory and deterministic conversion-evidence graph are the current corpus-wide evidence. Both are non-executing analyses:

- 198 source files: 166 `.mq5` and 32 `.mqh`
- 13,100,995 source bytes
- corpus SHA-256 `8052d74d395516aef01f221bf1a663b775ed02ccccbfa0476704d52112ee43b6`
- static analyzer `yo4x-mql5-static-analyzer.v2`; schema remains `mq5-static-manifest.v1`
- static-manifest artifact SHA-256 `8b04c8a3d8bc823cf1721e493d53f1b9c81b74ae7805fe42c7aae2f621e6ea44`
- compatibility-report artifact SHA-256 `9e4025a5561cc810682d9dbfc1e56e0d5d7ddf36d527cb92ea9ad2889b75465b`
- static dispositions: 68 need semantic validation, 3 need source, 127 unsupported, 0 rejected
- conversion-evidence analyzer `yo4x-mql5-conversion-evidence.v1`; dependency-graph SHA-256 `c463d3a6de0eaef29b912cfb9af5bd949c0591b26896d866acb2c088943ba10a`
- embedded conversion-evidence SHA-256 `6d4a18038f8b10ee8e4c68de55e96966d60293aa4d5186723e1363fae07537b1`
- conversion-evidence JSON artifact SHA-256 `2c1d766a730da057e2ba70f193cbba04c1199354ce47f04131537620c8ab94f4`
- conversion-evidence report artifact SHA-256 `2dda496841ad1d1ae2584745e2b177cc13dede7d43066aa899beea9a8aee2a53`
- conversion dispositions: 30 awaiting isolated type-check, 37 blocked on platform-library snapshots, 121 blocked on unsupported semantics, 6 blocked on missing dependencies, 2 blocked on invalid text controls, 1 blocked as all-NUL, and 1 blocked as binary/non-text
- strict encoding census: 109 UTF-8, 35 UTF-8 with BOM, 44 UTF-16LE with BOM, 5 BOM-less UTF-16LE, 3 Windows-1252, 1 all-NUL, and 1 binary/non-text
- lexical and delimiter/preprocessor structural analysis passed for 194 files; full grammar parse, type-check, restricted-IR lowering, compile, semantic parity, and runtime proof counts remain zero
- compile, reference-parity, and demo-runtime verification claims remain false

Static inventory and conversion evidence are not compile proof. An installed compiler is not compile proof. Only a fresh, trusted isolated-runner attestation bound to the exact job, source corpus, dependency graph, toolchain, isolation policy, normalized output, and repeat artifact hashes can transition a compile attempt to `Proven`.

## Host evidence and blocker

Read-only inspection found these signed local artifacts:

| Artifact | Bytes | SHA-256 | Authenticode |
|---|---:|---|---|
| `C:\Program Files\MetaTrader 5\MetaEditor64.exe` | 116,791,384 | `05718f3fa55f3f59fd2f024d8c433b457fbd58fcf39e947a16ccdad00a614ec7` | Valid, MetaQuotes Ltd. |
| `C:\Program Files\MetaTrader 5\terminal64.exe` | 121,845,920 | `7b3aaedfd3a3998f2138d399f601f46ed49ddbc9762697ce1170f8b325055b05` | Valid, MetaQuotes Ltd. |

That evidence identifies host binaries; it does not authorize running untrusted strategies on the host. There is no configured approved isolation provider, no pinned runner image digest, no pinned platform-library snapshot digest, and no isolated-runner attestation trust key. Docker, VirtualBox, and VMware runner commands were not present. The available `wsl.exe` exposes installation/help behavior rather than an installed distribution. The safe provider therefore reports `ISOLATED_RUNNER_NOT_CONFIGURED` and does not launch a process.

## Implemented boundary

The strategy-governance module now contains:

- typed compile jobs, isolated-runner requests, resource policy, toolchain pins, signed attestations, per-file evidence, and proof states;
- an `IMql5IsolatedCompileRunner` provider boundary and an intentionally unavailable default provider;
- P-256 ECDSA/SHA-256 DER attestation verification against an explicit public-key trust store;
- a fresh random challenge digest per attempt, preventing replay of a previously valid attestation;
- exact binding to job ID, corpus SHA-256, runner image digest, MetaEditor SHA-256/version, platform-library snapshot SHA-256, isolation controls, output SHA-256, and timestamps;
- mandatory network denial, read-only root filesystem, ephemeral workspace, disabled host mounts, no-new-privileges, and bounded memory, CPU, wall-clock, process, temporary-storage, and output limits;
- source re-analysis before dispatch, exact canonical static-manifest comparison, and rejection of hash drift, missing/ambiguous/invalid includes, rooted/traversing paths, and shell metacharacters;
- a bounded strict UTF-8 JSON-lines result protocol. Unknown fields, malformed values, duplicate paths, extra targets, source-hash mismatches, oversized output, and raw command-like extensions fail closed;
- diagnostic message hashing so compiler excerpts do not become durable source disclosure in evidence;
- two clean compile artifact hashes per `.mq5` target. `Proven` requires both hashes to match, every target to succeed with exit code zero, and a valid attestation;
- source copies handed to a provider are zeroed when the provider returns or fails.

No API accepts a shell command or free-form compiler arguments. A future provider must invoke its compiler with a fixed executable and argument vector inside the isolated runner; it must never concatenate source paths into a shell command.

## Proof outcomes

| State | Meaning |
|---|---|
| `StaticOnly` | Lexical/static inventory exists; no compiler claim exists. |
| `Blocked` | Compilation did not produce trusted runner evidence, including unavailable runner, unsafe inputs, host-side timeout, stale/forged attestation, or binding drift. |
| `Unsupported` | An attested runner or preflight reported the requested compile shape unsupported. This is not proof of successful compilation. |
| `Failed` | Trusted evidence reports compile failure, isolated timeout, invalid normalized output, incomplete results, or nondeterministic artifact hashes. |
| `Proven` | A trusted fresh attestation and exact per-target results prove successful deterministic compilation for the pinned inputs and toolchain only. |

An existing `Proven` evidence record is immutable and cannot be downgraded in place. A later attempt creates new evidence rather than rewriting historical truth. `Proven` does not imply semantic conversion parity or safe demo runtime behavior; those remain separate gates.

## Verification performed

Focused tests cover unsafe command/path input, traversal, source hash drift, static-manifest tampering, missing includes, forged signatures, stale attestations, attested toolchain drift, host response timeout, signed isolated timeout, strict output parsing, repeat artifact drift, successful proof requirements, and proof-state transitions.

A fresh non-executing inventory and conversion-evidence run over the source directory found exactly 198 unique allowed-extension paths. The corpus-wide invariant test binds the exact raw corpus, per-file hashes, dependency graph, encoding census, dispositions, stage outcomes, and aggregate NUL/control findings. The generated static manifest/report in both `artifacts/verification/mql5` and `docs/backend` are byte-identical; their hashes and the conversion artifact hashes match the values above.

```text
dotnet test tests/YO4X.Domain.Tests/YO4X.Domain.Tests.csproj --no-restore \
  --filter FullyQualifiedName~Mql5IsolatedCompileOrchestratorTests

Passed: 15, Failed: 0, Skipped: 0
```

The complete domain test project, including the signed semantic-equivalence evidence verifier, also passed:

```text
Passed: 113, Failed: 0, Skipped: 0
```

The conversion/static inventory Worker tests, including malformed UTF-16, BOM-less UTF-16, Windows-1252, all-NUL/binary separation, graph ordering/cycles/path safety, deterministic serialization, and exact-corpus invariants, passed:

```text
Passed: 60, Failed: 0, Skipped: 0
```

## Required work before any compile can run

1. Provision an isolated Windows-compatible runner outside the application host, with an approved immutable image and a reviewed MetaEditor licensing/provenance record.
2. Publish and pin the runner image digest and exact platform-library snapshot digest.
3. Configure enforcement evidence for no network, no host mounts, read-only base filesystem, ephemeral workspace, no privilege escalation, and the bounded resource policy.
4. Provision a runner-only P-256 signing key and configure only its public key in the backend trust store.
5. Implement the provider using fixed process APIs inside that runner, perform two clean-workspace compilations per target, normalize bounded results, and sign the exact attestation descriptor.
6. Run a representative reviewed source bundle first. Retain failed and unsupported evidence honestly; do not promote static-only or compile-only results to semantic or runtime verification.

Until all six items exist and the backend receives valid evidence, the correct operational result is blocked. The local MetaEditor and terminal must remain unlaunched for the supplied corpus.
