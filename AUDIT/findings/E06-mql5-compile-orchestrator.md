---
agent_id: E06
lane: mql5-compile-orchestrator
scope:
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5IsolatedCompileOrchestrator.cs
status: COMPLETE
generated: 2026-08-29T11:25:03Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# E06 — mql5-compile-orchestrator

## Scope audited
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5IsolatedCompileOrchestrator.cs` (1,337 lines)

## Verdict
The isolated compile orchestrator is exceptionally well-engineered, robust, and fail-closed across all security, isolation, and lifecycle boundaries. It enforces mandatory profile-backed toolchain approvals, deep immutable source/metadata snapshots under strict item and memory budgets, single-occupancy concurrency leases that remain held across timeouts, cryptographic memory zeroing on all paths, and rigorous ECDSA-P256 attestation verification with clock-skew, wall-clock duration, challenge-token, and output bounds. No compile proof can ever be generated without a complete, verified, deterministic double-compile attestation from an approved runner.

## Findings
None. The component thoroughly addresses all focus areas:
- **Isolation & Profile Authority:** Compilation cannot proceed without an explicitly configured backend profile (`Mql5ApprovedCompileProfile`). Callers cannot supply arbitrary platform library digests or override maximum isolation policies.
- **Resource Bounds & Host Protection:** Caller source collections are checked for file count (≤ 10,000), per-file size (≤ 4 MiB), and total corpus size (≤ 256 MiB) using checked arithmetic and indexer-only access prior to cloning. Metadata snapshots are strictly bounded to 100,000 items and 8 MiB UTF-8 to prevent host memory exhaustion.
- **Path Sanitization:** File paths are validated via `IsSafeRelativeSourcePath` to block directory traversal, control characters, backslashes, absolute paths, and Windows device names (`CON`, `PRN`, `AUX`, `NUL`, `COM1-9`, `LPT1-9`).
- **Hard Timeouts & Safe Memory Lifecycle:** Wall-clock limits are enforced via linked cancellation tokens. If an underlying runner task outlives a host timeout, the orchestrator holds capacity (`runnerInvocationOccupied`) and defers buffer zeroing until the task terminates, preventing concurrency races and reading zeroed memory.
- **Output Bounds & Parse Protection:** Compiler output length is checked against policy limits before cloning or parsing. The parser requires strict JSON-lines formatting, caps individual records at 64K characters / 256 KiB, and hashes diagnostic messages to prevent log/memory amplification.
- **Attestation & Outcome Fidelity:** Compilation proof (`Mql5CompileProofState.Proven`) strictly requires exit code 0, status `Succeeded`, zero compiler error diagnostics, matching clean-workspace repeat artifact SHA-256 digests, and a valid ECDSA-P256 attestation signature matching all corpus, manifest, conversion, package, closure, toolchain, and challenge digests.

## Referrals
None.

## Coverage gaps
None.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 58.4s | 171481 tok | id=62a04c05-a25f-4c77-ad01-19cb1352cfa8
