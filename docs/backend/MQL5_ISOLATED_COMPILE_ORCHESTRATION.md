# MQL5 isolated compile orchestration

Status date: 2026-08-24 UTC

## Result

The backend now has a deterministic, fail-closed compile-package planner and a signed isolated-runner orchestration boundary. This work read source bytes only through non-executing static analyzers; it did not load them into MetaEditor, MetaTrader, or another strategy runtime, and did not compile or execute them. It did not log in to an account or place, modify, or close a trade.

The real local authority state is intentionally represented as unavailable:

- no backend-approved platform-library snapshot;
- no backend-approved production compile profile;
- no production isolated compile runner;
- no production runner signing key or attestation.

Consequently, zero supplied targets are dispatch-ready locally. A syntactically valid SHA-256 string is not platform-snapshot approval and cannot make a package runnable.

## Exact no-execution corpus result

The planner rebuilt and exact-bound the security-sanitized current source bytes, static manifest, and conversion evidence before constructing one dossier per `.mq5` target. The exact byte total below binds those current sanitized bytes; it is not a claim about the untouched credential-bearing original intake.

| Measurement | Exact result |
|---|---:|
| Source files | 198 |
| `.mq5` targets | 166 |
| `.mqh` dependencies | 32 |
| Source bytes | 12,979,438 |
| Corpus SHA-256 | `9a53e844cfd3ffe5dfcf28544bb4909ce69741ac6a373e80b139f8227779dd47` |
| Compile-package schema | `yo4x.mql5-compile-package.v2` |
| Planner | `yo4x-mql5-compile-package-planner.v2` |
| Snapshot-unavailable plan SHA-256 | `30ceaabef530b6e43522608658db718d466ba52cc5851ff6430f30d21116c80e` |
| Metadata-only formatted JSON SHA-256 | `51e88beddabc6e2d11f00a6b8a2671a27642f58f2d302453f16199da368569e7` |
| Metadata-only formatted JSON bytes | 455,612 |
| Locally dispatch-ready targets | 0 |

All 32 `.mqh` dependency files have a conversion-evidence disposition:

| Header disposition | Files |
|---|---:|
| Awaiting isolated type-check | 17 |
| Blocked on a missing dependency | 1 |
| Blocked on unsupported semantics | 13 |
| Blocked on an external-dependency snapshot | 1 |

Target-level intrinsic classifications are:

| Intrinsic disposition | Targets |
|---|---:|
| Candidate for a later isolated type-check | 12 |
| Blocked on unresolved platform-library include snapshot | 36 |
| Blocked on unsupported semantics | 108 |
| Blocked on missing local dependency | 5 |
| Blocked on invalid syntax/text controls | 3 |
| Blocked as binary/non-text | 1 |
| Blocked as all-NUL | 1 |

Because approved snapshot authority is absent, each of the 166 dossiers also carries `ApprovedPlatformSnapshotUnavailable`. The 12 intrinsic candidates have effective disposition `BlockedApprovedPlatformSnapshotUnavailable`; the other 154 preserve their stronger intrinsic blocker. Platform-library include targets remain `BlockedPlatformSnapshot` even when planning is handed an otherwise well-formed snapshot digest.

The 12 intrinsic candidates, which are not compile proofs or runnable strategies, are:

- `9od10leporadi.mq5`
- `Breakout_EA (1) (2).mq5`
- `Breakout_EA (1).mq5`
- `BTC_EMA_Crossover_TSL_EA_Hedging.mq5`
- `cm_SL-NL-TP.mq5`
- `CRUDE_OIL_EMA_Crossover_TSL_EA.mq5`
- `EMA_Crossover_TSL_EA.mq5`
- `GOLD_EMA_Crossover_TSL_EA.mq5`
- `mt DanielScalper.mq5`
- `Nasdaq Fundamental EA V2 with Tp SL.mq5`
- `Universal_EMA_Crossover_TSL_EA.mq5`
- `WINDOWS_V2.mq5`

## Compile-package dossier

Each dossier contains metadata only, never source bodies. Its canonical package digest binds:

- the full source-corpus digest, canonical static-manifest digest, conversion-evidence digest and canonical content digest, and dependency-graph digest;
- the target path and source digest, conversion-file evidence digest, and conversion dependency-closure digest;
- only that target's transitive local source closure, in proven dependency-first order, with every source length and SHA-256;
- ordered include edges and explicit missing, ambiguous, invalid, cycle, unsupported, binary, all-NUL, platform, and authority-unavailable blockers;
- intrinsic and effective dispositions;
- the approved platform snapshot's approval ID, exact snapshot SHA-256, provenance-evidence SHA-256-derived approval digest, or explicit null fields when approval is unavailable.

Unrelated corpus files are excluded from the target closure. Changing source order, paths, bytes, manifest content, conversion evidence, include resolution, closure order, blocker data, or snapshot approval changes the canonical package digest or fails planning.

`Mql5CompilePackagePlanFormatter` emits deterministic, indented, enum-safe metadata JSON with a terminal newline. The `--compile-package-plan-output` worker option reproducibly generates this metadata-only plan from the same bounded corpus command. The exact-corpus test fixes both the canonical plan digest and formatted artifact digest, verifies the checked-in [metadata-only plan](../../artifacts/verification/mql5/mq5-compile-package-plan.v2.json) byte for byte, and proves source-body text is absent.

## Dispatch and proof boundary

Planning with no approved snapshot is allowed only to report intrinsic closure status. Dispatch validation requires an exact `Mql5ApprovedPlatformLibrarySnapshot`; null fails with `APPROVED_PLATFORM_SNAPSHOT_NOT_CONFIGURED`.

The orchestrator additionally requires an independently configured, backend-owned `Mql5ApprovedCompileProfile`. The profile binds:

- exact runner image digest;
- exact MetaEditor binary SHA-256 and version;
- exact approved platform snapshot and its provenance-bound approval digest;
- maximum isolation resources and mandatory network/host-mount/privilege controls;
- the allowed runner signing-key IDs.

An unconfigured profile blocks before the runner. A caller-selected toolchain, platform digest, isolation policy, or signing key cannot substitute for backend approval.

A bounded scalar and backend-approval preflight runs before capacity acquisition or any source/metadata copy, so an invalid job, toolchain, isolation policy, or unapproved profile cannot force a deep snapshot. After that gate, the orchestrator takes internally owned deep snapshots of source bytes, the complete static-manifest graph, conversion-evidence graph, compile dossier, toolchain, and isolation policy. Concurrent caller mutation, including mutate-and-restore races while the runner is blocked, cannot alter the dispatched request or later evidence. The request receives another independently owned copy of only the exact target closure and a deep dossier copy. Returned request bytes and dossier content are rechecked against the trusted package before an attestation can be accepted.

The signed attestation schema is `yo4x.mql5-runner-attestation.v3`. It binds the fresh challenge, job, backend profile, full evidence digests, exact package and closure, target, runner identity/session, toolchain, isolation policy, times, normalized output digest, and record count. A version-two descriptor or signature cannot be reinterpreted as version three.

`Proven` requires all of the following:

1. an approved profile and exact ready dossier;
2. a fresh, trusted, in-scope v3 signature;
3. exact request, package, toolchain, isolation, time, and output bindings;
4. one successful result for the selected target with exit code zero;
5. matching clean-workspace artifact and repeat-artifact SHA-256 values.

Compile proof does not imply semantic equivalence, strategy safety, reference parity, demo-runtime success, or authorization to trade. Those remain separate gates.

## Resource and ownership controls

- source corpus: at most 10,000 files, 4 MiB per file, and 256 MiB total, checked before cloning;
- compile metadata: at most 100,000 aggregate items and 8 MiB of UTF-8 text across the internally owned snapshot, checked before canonical serialization;
- compiler output policy: at most 16 MiB globally and no more than the approved job policy, checked before a second copy or parse;
- runner attestation signature: at most 256 bytes before cloning, with accepted ECDSA encoding validated again;
- result protocol: bounded strict UTF-8 JSON, one target record, no unknown fields, and diagnostic-message digests rather than durable excerpts;
- ordinary synchronous or asynchronous provider faults are normalized to `ISOLATED_RUNNER_FAILED`; attestation-verifier faults are normalized to untrusted evidence, without exception/source leakage;
- caller cancellation: propagated; host timeout without an attestation remains blocked;
- source buffers: zeroed after use. If a provider ignores cancellation, request buffers stay owned by that in-flight task and are zeroed only after it actually completes, preventing zero-while-read races.

An in-process provider cannot safely terminate a hostile compiler process. A production implementation must use an externally supervised, disposable, network-denied Windows-compatible runner with fixed executable and argument vectors. The unavailable default provider executes nothing.

## Verification

Release verification on 2026-08-23:

```text
dotnet build tests/YO4X.Domain.Tests/YO4X.Domain.Tests.csproj -c Release --no-restore
Build succeeded. 0 warnings, 0 errors.

dotnet test tests/YO4X.Domain.Tests/YO4X.Domain.Tests.csproj -c Release --no-build \
  --filter "FullyQualifiedName~Mql5CompilePackageDossierPlannerTests|FullyQualifiedName~Mql5IsolatedCompileOrchestratorTests"
Passed: 55, Failed: 0, Skipped: 0.

dotnet test tests/YO4X.Domain.Tests/YO4X.Domain.Tests.csproj -c Release --no-build
Passed: 158, Failed: 0, Skipped: 0.
```

Adversarial coverage includes source/static/conversion/dossier drift, exact closure order, unrelated-source exclusion, absent snapshot authority, arbitrary digest non-approval, path safety, per-file and aggregate size limits, hostile collection enumeration, concurrent caller mutation and restoration, runner request mutation, synchronous/asynchronous runner faults, output/signature pre-clone caps, timeouts and late completion, v2/v3 schema separation, signer scope, toolchain/profile mismatch, forged/stale attestations, strict output parsing, and nondeterministic artifacts.

## Remaining objective blockers

1. Produce and independently approve an immutable, provenance-documented platform-library snapshot. A hash alone is insufficient.
2. Provision an externally supervised Windows-compatible isolated runner with an immutable approved image and reviewed MetaEditor provenance/licensing.
3. Configure enforceable network denial, no host mounts, read-only base, ephemeral workspace, no-new-privileges, and bounded resources.
4. Provision a runner-only signing key and configure only its public key and key ID in the backend profile.
5. Implement the fixed-argument provider transport, two clean-workspace compilations per target, bounded normalized output, and v3 signing.
6. Re-plan with the approved snapshot and compile only exact dossiers that remain ready; retain every failure and unsupported result honestly.
7. Complete separate semantic-equivalence, reference-parity, review, and demo-runtime gates before any strategy can be considered for deployment or trading.

Until these blockers are resolved, zero supplied targets may be dispatched and the correct backend outcome remains blocked.
